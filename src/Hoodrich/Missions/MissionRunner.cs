using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Gangs;
using Hoodrich.State;
using Hoodrich.Territory;
using Hoodrich.UI;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.Missions
{
    /// <summary>Where a job has got to.</summary>
    internal enum MissionState
    {
        None,

        /// <summary>Driving to the block.</summary>
        Travel,

        /// <summary>On the block, doing the thing.</summary>
        Work,

        /// <summary>Done, on the way back to Lamar for the money.</summary>
        Collect
    }

    /// <summary>
    /// Runs one job at a time.
    ///
    /// Every job is a place you drive to, targets that are really there, and a walk back for
    /// the money -- no teleports and no mid-job menus, same as the rest of the mod. The homies
    /// are real peds who ride with you and can die, which is what makes bringing them a
    /// decision rather than free backup.
    /// </summary>
    internal sealed class MissionRunner
    {
        private const float ArriveRange = 60f;

        /// <summary>Targets are placed this far out, so the ground is streamed before they land.</summary>
        private const float PreSpawnRange = 200f;
        private const float TargetSpread = 9f;
        private const int UpdateIntervalMs = 500;

        /// <summary>Rep lost for each of your own you get killed out there.</summary>
        private const float HomieLostRep = 8f;

        private static readonly string[] HomieWeapons =
        {
            "WEAPON_PISTOL", "WEAPON_MICROSMG", "WEAPON_PUMPSHOTGUN"
        };

        private readonly PlayerState _state;
        private readonly Affiliation _crew;
        private readonly GangRegistry _gangs;
        private readonly ZoneMap _zones;
        private readonly Random _rng = new Random();

        private readonly List<Ped> _homies = new List<Ped>();
        private readonly List<Ped> _targets = new List<Ped>();

        /// <summary>
        /// Every blip the job created.
        ///
        /// A blip attached to a ped is not cleaned up by letting go of the ped: it survives, and
        /// the minimap keeps showing homies who are no longer anything to do with you.
        /// </summary>
        private readonly List<Blip> _blips = new List<Blip>();

        private MissionDef _def;
        private Vector3 _site;
        private Blip _siteBlip;
        private int _lastUpdate;
        private int _startedAt;
        private int _homiesLost;

        public MissionRunner(PlayerState state, Affiliation crew, GangRegistry gangs, ZoneMap zones)
        {
            _state = state;
            _crew = crew;
            _gangs = gangs;
            _zones = zones;
        }

        public MissionState State { get; private set; } = MissionState.None;

        public bool IsRunning => State != MissionState.None;

        public MissionDef Current => _def;

        /// <summary>What the player is meant to be doing, in one line.</summary>
        public string Objective
        {
            get
            {
                switch (State)
                {
                    case MissionState.Travel:
                        return _def.Kind == MissionKind.DriveBy
                            ? "Get a car and drive to " + ZoneName()
                            : "Get to " + ZoneName();

                    case MissionState.Work:
                        return _def.Kind == MissionKind.DriveBy
                            ? "Shoot up the corner -- stay in the car"
                            : "Put hands on them";

                    case MissionState.Collect:
                        return "Go back to Lamar for the money";

                    default:
                        return "";
                }
            }
        }

        private string ZoneName()
        {
            var zone = _zones.Get(_def.Zone);
            return zone == null || string.IsNullOrEmpty(zone.Name) ? _def.Zone : zone.Name;
        }

        // ---- starting ----------------------------------------------------------

        /// <summary>Returns a player-facing refusal, or null once the job is on.</summary>
        public string Start(MissionDef def)
        {
            if (def == null) return "No such job.";
            if (IsRunning) return "You are already on something.";
            if (!_crew.IsAffiliated) return "You do not run with anyone.";

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return "Not right now.";

            var site = Math.Abs(def.X) > 0.01f || Math.Abs(def.Y) > 0.01f
                ? new Vector3(def.X, def.Y, def.Z)
                : _zones.GroundedCentre(def.Zone);

            if (site == Vector3.Zero) return "Nobody could tell you where that is.";

            _def = def;
            _site = site;
            _homiesLost = 0;
            _startedAt = Game.GameTime;
            State = MissionState.Travel;

            MarkSite();
            SpawnHomies(player, def);

            Notify.Important("~g~Job on.~s~ " + Objective + ".");
            Log.Info("Mission " + def.Id + " started, site " + _site + ".");
            return null;
        }

        private void MarkSite()
        {
            try
            {
                _siteBlip = World.CreateBlip(_site, ArriveRange);
                if (_siteBlip == null || !_siteBlip.Exists()) return;

                _siteBlip.Color = BlipColor.Yellow;
                _siteBlip.Alpha = 90;
                _siteBlip.ShowRoute = true;
                _siteBlip.Name = _def.Name;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not mark the job: " + ex.Message);
            }
        }

        /// <summary>
        /// Your people, waiting on you.
        ///
        /// Put in a group with the player so the game's own follow logic drives them: they get
        /// in cars with you, they keep up, and they fight what you fight, without a script
        /// nannying them every frame.
        /// </summary>
        private void SpawnHomies(Ped player, MissionDef def)
        {
            var gang = _crew.Current;
            if (gang == null || def.Homies <= 0) return;

            var group = Function.Call<int>(Hash.GET_PED_GROUP_INDEX, player.Handle);

            for (var i = 0; i < def.Homies; i++)
            {
                var ped = SpawnGangMember(gang, player.Position.Around(3f + i));
                if (ped == null) continue;

                _homies.Add(ped);

                try
                {
                    Function.Call(Hash.SET_PED_AS_GROUP_MEMBER, ped.Handle, group);
                    Function.Call(Hash.SET_PED_NEVER_LEAVES_GROUP, ped.Handle, true);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 46, true);
                    Function.Call(Hash.SET_PED_ACCURACY, ped.Handle, 30);
                    Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle, gang.GroupHash);

                    // A ride-out is hands, so they only draw when the job says so.
                    if (def.Kind != MissionKind.RideOut)
                    {
                        var weapon = HomieWeapons[_rng.Next(HomieWeapons.Length)];
                        Function.Call(Hash.GIVE_WEAPON_TO_PED, ped.Handle,
                                      Function.Call<uint>(Hash.GET_HASH_KEY, weapon), 200, false, true);
                    }

                    var blip = ped.AddBlip();
                    if (blip != null && blip.Exists())
                    {
                        blip.Color = BlipColor.Green;
                        blip.Scale = 0.6f;
                        blip.Name = "Homie";
                        _blips.Add(blip);
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not set up a homie: " + ex.Message);
                }
            }

            if (_homies.Count > 0) Notify.Ticker("~g~" + _homies.Count + " of the homies rolled out with you.~s~");
        }

        private Ped SpawnGangMember(GangDef gang, Vector3 near)
        {
            foreach (var name in gang.MemberModels)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1200)) continue;

                    var spot = World.GetNextPositionOnSidewalk(near);
                    if (spot == Vector3.Zero) spot = near;

                    // Pavement lookups return a position, not a height, and a ped created above
                    // the ground falls to it -- which is what the drop from the sky was.
                    try
                    {
                        if (World.GetGroundHeight(new Vector3(spot.X, spot.Y, spot.Z + 15f),
                                                  out var groundZ, GetGroundHeightMode.Normal) && groundZ > 0f)
                        {
                            spot.Z = groundZ;
                        }
                    }
                    catch
                    {
                        // Keep what the pavement gave us.
                    }

                    var ped = World.CreatePed(model, spot);
                    model.MarkAsNoLongerNeeded();

                    if (ped == null || !ped.Exists()) continue;

                    ped.IsPersistent = true;
                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, ped.Handle, true, true);

                    return ped;
                }
                catch
                {
                    // Try the next model.
                }
            }

            return null;
        }

        // ---- per-tick ----------------------------------------------------------

        public void Update()
        {
            if (!IsRunning) return;

            var now = Game.GameTime;
            if (now - _lastUpdate < UpdateIntervalMs) return;
            _lastUpdate = now;

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive)
            {
                Fail("You went down out there.");
                return;
            }

            CountLostHomies();

            switch (State)
            {
                case MissionState.Travel:
                    // Put them in place well before you can see them. Spawning at the arrival
                    // radius is what had them appearing in mid-air and falling in as you pulled
                    // up: a ped created on unstreamed ground has nothing to stand on yet.
                    if (_targets.Count == 0 && player.Position.DistanceTo(_site) <= PreSpawnRange)
                    {
                        SpawnTargets(player);
                    }

                    if (player.Position.DistanceTo(_site) <= ArriveRange) BeginWork(player);
                    return;

                case MissionState.Work:
                    TickWork(player);
                    return;
            }
        }

        /// <summary>
        /// Puts them on the corner before you can see it.
        ///
        /// They stand about doing nothing until you actually turn up -- a corner full of men
        /// already swinging at thin air before you arrive is worse than one that is empty.
        /// </summary>
        private void SpawnTargets(Ped player)
        {
            var gang = _gangs.Get(_def.TargetGang);
            if (gang == null) return;

            for (var i = 0; i < _def.Targets; i++)
            {
                var ped = SpawnGangMember(gang, _site.Around(TargetSpread));
                if (ped == null) continue;

                _targets.Add(ped);

                try
                {
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, true);
                    Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle, gang.GroupHash);
                    Function.Call(Hash.SET_PED_ACCURACY, ped.Handle, 20);

                    // A ride-out is a beating on both sides; a drive-by is not.
                    if (_def.Kind != MissionKind.RideOut)
                    {
                        Function.Call(Hash.GIVE_WEAPON_TO_PED, ped.Handle,
                                      Function.Call<uint>(Hash.GET_HASH_KEY, "WEAPON_PISTOL"), 150, false, true);
                    }

                    Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, ped.Handle,
                                  "WORLD_HUMAN_STAND_IMPATIENT", 0, true);

                    var blip = ped.AddBlip();
                    if (blip != null && blip.Exists())
                    {
                        blip.Color = BlipColor.Red;
                        blip.Scale = 0.7f;
                        blip.Name = gang.Name;
                        _blips.Add(blip);
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not set up a target: " + ex.Message);
                }
            }

            Log.Info("Mission " + _def.Id + ": " + _targets.Count + " waiting at " + _site + ".");
        }

        private void BeginWork(Ped player)
        {
            State = MissionState.Work;

            // Late arrival: they were never placed, so place them now rather than fail.
            if (_targets.Count == 0) SpawnTargets(player);

            foreach (var ped in _targets)
            {
                if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

                try { Function.Call(Hash.TASK_COMBAT_PED, ped.Handle, player.Handle, 0, 16); }
                catch { /* the game's AI takes over */ }
            }

            if (_targets.Count == 0)
            {
                Fail("There was nobody there.");
                return;
            }

            ClearSiteBlip();

            Notify.Important("~r~They are here.~s~ " + Objective + ".");
        }

        private void TickWork(Ped player)
        {
            var standing = 0;
            foreach (var ped in _targets)
            {
                if (ped != null && ped.Exists() && ped.IsAlive) standing++;
            }

            if (standing > 0) return;

            State = MissionState.Collect;
            Notify.Important("~g~That is them done.~s~ Go back to Lamar.");
        }

        private void CountLostHomies()
        {
            for (var i = _homies.Count - 1; i >= 0; i--)
            {
                var ped = _homies[i];
                if (ped != null && ped.Exists() && ped.IsAlive) continue;

                _homies.RemoveAt(i);
                _homiesLost++;

                Notify.Problem("you lost one of the homies.");
            }
        }

        // ---- finishing ---------------------------------------------------------

        /// <summary>True when the player can hand the job in.</summary>
        public bool ReadyToCollect => State == MissionState.Collect;

        /// <summary>Pays out and clears down. Returns what Lamar says.</summary>
        public string Collect()
        {
            if (!ReadyToCollect) return null;

            var def = _def;

            var pay = def.PayMin + _rng.Next(Math.Max(1, def.PayMax - def.PayMin + 1));
            var rep = Math.Max(0f, def.Rep - _homiesLost * HomieLostRep);

            Game.Player.Money += pay;

            _crew.AddRep(rep, "for the work");
            _state.AddRespect(rep * 0.5f);
            _state.Touch();

            Notify.Important("~g~+$" + pay.ToString("N0") + "~s~ and " + rep.ToString("0") + " rep.");
            Log.Info("Mission " + def.Id + " paid $" + pay + ", " + rep.ToString("0") + " rep, " +
                     _homiesLost + " homies lost.");

            var line = string.IsNullOrEmpty(def.Done) ? "Good look. Take that." : def.Done;

            Clear();
            return line;
        }

        public void Fail(string reason)
        {
            if (!IsRunning) return;

            var id = _def == null ? "?" : _def.Id;
            Clear();

            if (!string.IsNullOrEmpty(reason)) Notify.Failure(reason);
            Log.Info("Mission " + id + " failed: " + reason);
        }

        private void Clear()
        {
            ClearSiteBlip();
            ClearBlips();

            foreach (var ped in _targets)
            {
                try
                {
                    if (ped == null || !ped.Exists()) continue;
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);
                    ped.MarkAsNoLongerNeeded();
                }
                catch { /* teardown */ }
            }
            _targets.Clear();

            foreach (var ped in _homies)
            {
                try
                {
                    if (ped == null || !ped.Exists()) continue;
                    Function.Call(Hash.REMOVE_PED_FROM_GROUP, ped.Handle);
                    ped.MarkAsNoLongerNeeded();
                }
                catch { /* teardown */ }
            }
            _homies.Clear();

            _def = null;
            State = MissionState.None;
        }

        /// <summary>Removes every blip the job made, which peds do not do for you.</summary>
        private void ClearBlips()
        {
            foreach (var blip in _blips)
            {
                try { if (blip != null && blip.Exists()) blip.Delete(); }
                catch { /* teardown */ }
            }
            _blips.Clear();
        }

        private void ClearSiteBlip()
        {
            try { if (_siteBlip != null && _siteBlip.Exists()) _siteBlip.Delete(); }
            catch { /* teardown */ }

            _siteBlip = null;
        }

        public void RestoreWorld() => Clear();

        // ---- hud ---------------------------------------------------------------

        /// <summary>One line, top left, saying what you are meant to be doing.</summary>
        public void Draw()
        {
            if (!IsRunning) return;

            // Centred at the top: it belongs to the job, not to the corner of the screen.
            const float width = 0.26f;
            const float y = 0.075f;

            var left = 0.5f - width * 0.5f;
            var x = left + 0.010f;

            Hud.RectFrom(left, y - 0.008f, width, 0.052f, Color.FromArgb(200, 12, 13, 15));
            Hud.RectFrom(left, y - 0.008f, width, 0.0025f, Palette.Accent);

            Hud.Text(_def.Name.ToUpperInvariant(), x, y, 0.28f, Palette.Text,
                     Hud.FontLabel, centre: false);

            Hud.Text(Objective, x, y + 0.022f, 0.26f, Palette.TextDim, Hud.FontBody, centre: false);
        }
    }
}
