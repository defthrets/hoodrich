using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Social;
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

        /// <summary>Work done, but the law is on you and Lamar is not taking delivery yet.</summary>
        Escape,

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

        /// <summary>Idles for people who are not expecting you.</summary>
        private static readonly string[] IdleScenarios =
        {
            "WORLD_HUMAN_SMOKING", "WORLD_HUMAN_STAND_MOBILE", "WORLD_HUMAN_DRINKING",
            "WORLD_HUMAN_HANG_OUT_STREET", "WORLD_HUMAN_STAND_IMPATIENT"
        };
        private const int UpdateIntervalMs = 500;

        /// <summary>Rep lost for each of your own you get killed out there.</summary>
        private const float HomieLostRep = 8f;

        private static readonly string[] HomieWeapons =
        {
            "WEAPON_PISTOL", "WEAPON_MICROSMG", "WEAPON_PUMPSHOTGUN"
        };

        /// <summary>
        /// What they carry on a hit. Machine pistols and micro SMGs, nothing with a stock.
        ///
        /// A shotgun at a distance is a man walking into open ground to use it, and a hit on a
        /// yard full of people wants volume, not one loud noise every two seconds.
        /// </summary>
        private static readonly string[] HitWeapons =
        {
            "WEAPON_MACHINEPISTOL", "WEAPON_MICROSMG", "WEAPON_MINISMG"
        };

        /// <summary>
        /// The car a drive-by turns up in, left where the job says and nowhere else.
        ///
        /// Always the same one. A drive-by you did in whatever happened to be parked nearby is
        /// a drive-by you did in a stranger car -- the point of a set car is that it becomes
        /// the car, and you learn to leave it somewhere afterwards.
        /// </summary>
        private static readonly string[] DriveByCars = { "vorschlafhammer", "vorschlaghammer", "buccaneer2", "faction" };

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
        private Vehicle _jobCar;
        private int _lastUpdate;
        private int _startedAt;
        private int _homiesLost;

        /// <summary>
        /// The one job that is scripted rather than assembled.
        ///
        /// Kept as its own thing rather than folded in here: the bike ride has six legs, a
        /// conversation, a shop and a rule about weapons, and threading all of that through a
        /// runner built for "drive there, deal with them, come back" would leave both harder to
        /// follow than either is on its own.
        /// </summary>
        private readonly BikeRide _bike;

        /// <summary>Set by Main. Null-checked everywhere, so the feed is never load-bearing.</summary>
        public SocialFeed Social
        {
            get { return _social; }
            set { _social = value; _tags.Social = value; _bike.Social = value; }
        }

        private SocialFeed _social;


        /// <summary>
        /// The tag run, and the walls it draws from.
        ///
        /// The list is loaded once and kept, so a run picks a different handful each time
        /// rather than sending you round the same walls in the same order.
        /// </summary>
        private readonly TagRun _tags;
        private readonly List<TagSpot> _walls;

        public MissionRunner(PlayerState state, Affiliation crew, GangRegistry gangs, ZoneMap zones)
        {
            _state = state;
            _crew = crew;
            _gangs = gangs;
            _zones = zones;
            _bike = new BikeRide(crew, gangs);
            _tags = new TagRun(gangs, crew);
            _walls = TagRun.Load();
        }

        /// <summary>Set by Main and handed straight to the bike job for its courtyard exchange.</summary>
        public Conversation Talk
        {
            get { return _bike.Talk; }
            set { _bike.Talk = value; }
        }

        public MissionState State { get; private set; } = MissionState.None;

        public bool IsRunning => State != MissionState.None || _bike.IsRunning || _tags.IsRunning;

        private bool OnBike => _bike.IsRunning;

        private bool OnTags => _tags.IsRunning;

        public MissionDef Current => _def;

        /// <summary>What the player is meant to be doing, in one line.</summary>
        public string Objective
        {
            get
            {
                if (OnBike) return _bike.Objective;
                if (OnTags) return _tags.Objective;

                switch (State)
                {
                    case MissionState.Travel:
                        return _def.Kind == MissionKind.DriveBy
                            ? "Get a car and roll out to " + ZoneName()
                            : "Get to " + ZoneName();

                    case MissionState.Work:
                        if (_def.Kind == MissionKind.DriveBy) return "Shoot up the corner -- stay in the car";
                        return Fists(_def.Kind) ? "Put hands on them" : "Put 'em down";

                    case MissionState.Escape:
                        return "Lose the cops, then get back to Lamar";

                    case MissionState.Collect:
                        return "Get back to Lamar for the money";

                    default:
                        return "";
                }
            }
        }

        /// <summary>True for the jobs that are hands only, on both sides.</summary>
        private static bool Fists(MissionKind kind)
        {
            return kind == MissionKind.RideOut || kind == MissionKind.BikeRide;
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
            if (IsRunning) return "You're already on something.";
            if (!_crew.IsAffiliated) return "You don't run with nobody.";

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return "Not right now.";

            var site = Math.Abs(def.X) > 0.01f || Math.Abs(def.Y) > 0.01f
                ? new Vector3(def.X, def.Y, def.Z)
                : _zones.GroundedCentre(def.Zone);

            if (site == Vector3.Zero) return "Nobody could tell you where that's at.";

            if (def.Kind == MissionKind.Tags)
            {
                var refused = _tags.Start(def, _walls);
                if (refused != null) return refused;

                _def = def;
                _homiesLost = 0;
                _startedAt = Game.GameTime;

                Notify.Important("~g~Job on.~s~ " + _tags.Objective + ".");
                Log.Info("Mission " + def.Id + " started as a tag run.");
                return null;
            }

            if (def.Kind == MissionKind.BikeRide)
            {
                var no = _bike.Start(def);
                if (no != null) return no;

                _def = def;
                _homiesLost = 0;
                _startedAt = Game.GameTime;

                Log.Info("Mission " + def.Id + " started as a bike ride.");
                return null;
            }

            _def = def;
            _site = site;
            _homiesLost = 0;
            _startedAt = Game.GameTime;
            State = MissionState.Travel;

            MarkSite();
            SpawnHomies(player, def);

            // Any job that names a car gets one. Keying this on the DriveBy kind meant the
            // Rancho and Grove jobs had car coordinates in their data and no car in the street.
            if (Math.Abs(def.CarX) > 0.01f || Math.Abs(def.CarY) > 0.01f) SpawnJobCar(def);

            Notify.Important("~g~Job on.~s~ " + Objective + ".");
            Log.Info("Mission " + def.Id + " started, site " + _site + ".");

            if (Social != null) Social.On(SocialEvent.MissionTaken, def.Name);
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

            // Tight formation, close spacing. The default has group members trailing far enough
            // back that they arrive at a fight after it has finished -- and on the drive out it
            // reads as three men who did not come with you.
            try
            {
                Function.Call(Hash.SET_GROUP_FORMATION, group, 1);
                Function.Call(Hash.SET_GROUP_FORMATION_SPACING, group, 2.5f, 1.5f, 4f);
                Function.Call(Hash.SET_GROUP_SEPARATION_RANGE, group, 250f);
            }
            catch
            {
                // The default formation still follows.
            }

            // A drive-by crew waits at the car, not at your elbow. The walk round to where
            // the car is parked is the start of the job.
            var muster = Ground(new Vector3(def.CarX, def.CarY, def.CarZ));

            if (muster == Vector3.Zero) muster = player.Position;

            for (var i = 0; i < def.Homies; i++)
            {
                var ped = SpawnGangMember(gang, muster.Around(3f + i));
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
                    if (!Fists(def.Kind))
                    {
                        var list = def.Kind == MissionKind.Hit ? HitWeapons : HomieWeapons;
                        var weapon = list[_rng.Next(list.Length)];

                        Function.Call(Hash.GIVE_WEAPON_TO_PED, ped.Handle,
                                      Function.Call<uint>(Hash.GET_HASH_KEY, weapon), 250, false, true);
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

        /// <summary>
        /// Leaves the car and the people who ride in it at the spot the job names.
        ///
        /// Stock, with one change: competition suspension, so it sits where it should. Anything
        /// more would be somebody else deciding what your car looks like.
        /// </summary>
        private void SpawnJobCar(MissionDef def)
        {
            var where = Ground(new Vector3(def.CarX, def.CarY, def.CarZ));
            if (where == Vector3.Zero) return;

            foreach (var name in DriveByCars)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) continue;

                    _jobCar = World.CreateVehicle(model, where, def.CarHeading);
                    model.MarkAsNoLongerNeeded();

                    if (_jobCar == null || !_jobCar.Exists()) continue;

                    _jobCar.IsPersistent = true;
                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, _jobCar.Handle, true, true);

                    // Stock everywhere else. SET_VEHICLE_MOD_KIT must be called before any mod
                    // will take, and 15 is the suspension slot; 3 is competition.
                    Function.Call(Hash.SET_VEHICLE_MOD_KIT, _jobCar.Handle, 0);
                    Function.Call(Hash.SET_VEHICLE_MOD, _jobCar.Handle, 15, 3, false);

                    var blip = _jobCar.AddBlip();
                    if (blip != null && blip.Exists())
                    {
                        blip.Color = BlipColor.Green;
                        blip.Scale = 0.8f;
                        blip.Name = "The car";
                        _blips.Add(blip);
                    }

                    Log.Info("Mission " + def.Id + ": car left at " + where + " as " + name + ".");
                    return;
                }
                catch
                {
                    // Try the next model.
                }
            }

            Log.Warn("No drive-by car model would load for " + def.Id + ".");
        }

        /// <summary>
        /// Settles onto the ground, but only when the ground agrees with the authored height.
        ///
        /// Authored spots are read off the HUD while stood on them, so they are already right.
        /// A probe from high above a narrow alley finds a balcony, which is how things ended up
        /// on roofs.
        /// </summary>
        private static Vector3 Ground(Vector3 where)
        {
            if (Math.Abs(where.X) < 0.01f && Math.Abs(where.Y) < 0.01f) return Vector3.Zero;

            try
            {
                if (World.GetGroundHeight(new Vector3(where.X, where.Y, where.Z + 1.5f),
                                          out var groundZ, GetGroundHeightMode.Normal) &&
                    groundZ > 0f && Math.Abs(groundZ - where.Z) <= 3f)
                {
                    where.Z = groundZ;
                }
            }
            catch
            {
                // Keep the authored height.
            }

            return where;
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
            if (OnBike)
            {
                _bike.Update();

                var wentWrong = _bike.Failure;
                if (!string.IsNullOrEmpty(wentWrong)) Fail(wentWrong);

                return;
            }

            if (OnTags)
            {
                _tags.Update();
                return;
            }

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

            // Being taken in ends it the same as being killed. Coming round in a cell with the
            // job still marked live, and the blip still on the map, is the game pretending
            // nothing happened.
            if (Game.Player.IsDead || Function.Call<bool>(Hash.IS_PLAYER_BEING_ARRESTED, Game.Player.Handle, false))
            {
                Fail("They took you in.");
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

                case MissionState.Escape:
                    TickEscape();
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

                    // A ride-out is a beating on both sides; the rest are not.
                    if (!Fists(_def.Kind))
                    {
                        Function.Call(Hash.GIVE_WEAPON_TO_PED, ped.Handle,
                                      Function.Call<uint>(Hash.GET_HASH_KEY, "WEAPON_PISTOL"), 150, false, true);
                    }

                    // Standing about doing something rather than standing about doing nothing.
                    // Which idle they get is per-man, so five of them on a forecourt look like
                    // five people and not one man copied five times.
                    Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, ped.Handle,
                                  IdleScenarios[_rng.Next(IdleScenarios.Length)], 0, true);

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

            // A hit starts when YOU start it. They carry on with whatever they were doing until
            // somebody puts a round through it, which is the difference between walking up on
            // people and walking into an ambush that was waiting for you to arrive.
            var theyStartIt = _def.Kind != MissionKind.Hit;

            foreach (var ped in _targets)
            {
                if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

                try
                {
                    // Unblocked either way, so being shot at is something they can react to.
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);

                    if (theyStartIt) Function.Call(Hash.TASK_COMBAT_PED, ped.Handle, player.Handle, 0, 16);
                }
                catch { /* the game AI takes over */ }
            }

            if (_targets.Count == 0)
            {
                Fail("Wasn't nobody there.");
                return;
            }

            ClearSiteBlip();

            Notify.Important("~r~They're here.~s~ " + Objective + ".");
        }

        private void TickWork(Ped player)
        {
            var standing = 0;
            foreach (var ped in _targets)
            {
                if (ped != null && ped.Exists() && ped.IsAlive) standing++;
            }

            if (standing > 0) return;

            if (_def.EscapeHeat)
            {
                // The trip home is part of the job. Without this the drive back is a formality
                // you spend looking at a blip, and the only thing that ever went wrong happened
                // before you got in the car.
                State = MissionState.Escape;

                Wanted(_def.HeatStars);
                Notify.Important("~r~Somebody called it in.~s~ Lose 'em, then get back to Lamar.");
                return;
            }

            State = MissionState.Collect;
            Notify.Important("~g~That's them done.~s~ Get back to Lamar.");
        }

        /// <summary>Waiting on the stars to drop before he will take it off you.</summary>
        private void TickEscape()
        {
            if (Game.Player.Wanted.WantedLevel > 0) return;

            State = MissionState.Collect;
            Notify.Important("~g~You're clear.~s~ Get back to Lamar.");
        }

        /// <summary>Raises the wanted level, never lowers it.</summary>
        private static void Wanted(int stars)
        {
            try
            {
                if (Game.Player.Wanted.WantedLevel >= stars) return;

                Game.Player.Wanted.SetWantedLevel(stars, false);
                Game.Player.Wanted.ApplyWantedLevelChangeNow(false);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not set the wanted level: " + ex.Message);
            }
        }

        private void CountLostHomies()
        {
            for (var i = _homies.Count - 1; i >= 0; i--)
            {
                var ped = _homies[i];
                if (ped != null && ped.Exists() && ped.IsAlive) continue;

                _homies.RemoveAt(i);
                _homiesLost++;

                Notify.Problem("you lost one of the homies out there.");
            }
        }

        // ---- finishing ---------------------------------------------------------

        /// <summary>True when the player can hand the job in.</summary>
        public bool ReadyToCollect =>
            OnBike ? _bike.ReadyToCollect :
            OnTags ? _tags.ReadyToCollect :
            State == MissionState.Collect;

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
            _state.MarkDone(def.Id);
            _state.Touch();

            Notify.Important("~g~+$" + pay.ToString("N0") + "~s~ and " + rep.ToString("0") + " rep.");
            Log.Info("Mission " + def.Id + " paid $" + pay + ", " + rep.ToString("0") + " rep, " +
                     _homiesLost + " homies lost.");

            var line = string.IsNullOrEmpty(def.Done) ? "Good look. Take that." : def.Done;

            if (Social != null)
            {
                // Reported as the thing it actually was. "A job got done" is a press release;
                // "somebody sprayed a corner on Grove" is what a neighbour would post.
                switch (def.Kind)
                {
                    case MissionKind.BikeRide: Social.On(SocialEvent.Brawl); break;
                    case MissionKind.DriveBy: Social.On(SocialEvent.DriveBy); break;
                    case MissionKind.Tags: Social.On(SocialEvent.Tagged); break;
                    default: Social.On(SocialEvent.MissionDone); break;
                }

                // And the job itself, so a run of work reads as a run of work.
                Social.On(SocialEvent.MissionDone, def.Name, pay);
            }

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

            if (Social != null) Social.On(SocialEvent.MissionFailed);
        }

        private void Clear()
        {
            _bike.Clear();
            _tags.Clear();

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

            // Let go rather than deleted. A car you drove to a job and back should still be
            // sitting outside afterwards, the same as the bikes.
            try { if (_jobCar != null && _jobCar.Exists()) _jobCar.MarkAsNoLongerNeeded(); }
            catch { /* teardown */ }

            _jobCar = null;

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
            if (!IsRunning || _def == null) return;

            _tags.Draw();

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
