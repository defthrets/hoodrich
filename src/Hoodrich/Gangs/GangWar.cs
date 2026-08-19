using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Social;
using Hoodrich.State;
using Hoodrich.UI;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.Gangs
{
    /// <summary>Somewhere of ours worth attacking, and the man who stands there.</summary>
    internal sealed class WarTarget
    {
        public string Who = "";
        public Vector3 Where;
    }

    /// <summary>
    /// The other sets coming to us.
    ///
    /// Every fight in the mod so far has been one you went and started. This is the one that
    /// comes to you, at a place you care about, whether or not you were doing anything -- and
    /// the only decision it asks for is whether you turn up.
    ///
    /// Deliberately NOT a wave arena. Carloads arrive over five minutes at irregular intervals,
    /// one or two at a time, so it reads as people driving over rather than a spawner emptying
    /// itself. Ours are there too, in matched numbers, because a block that needs one man to
    /// save it is a block with nobody living on it. You are the difference at the margin, not
    /// the entire defence -- which is the honest shape of it, and it also means turning up late
    /// still matters.
    /// </summary>
    internal sealed class GangWar
    {
        // ---- shape ------------------------------------------------------------

        /// <summary>How long it runs before the last of them break off.</summary>
        private const int WarMs = 300000;

        /// <summary>Gap between carloads. Irregular on purpose.</summary>
        private const int WaveGapMinMs = 42000;
        private const int WaveGapMaxMs = 78000;

        /// <summary>Two turn up together at the start; after that it is usually one.</summary>
        private const int OpeningCars = 2;
        private const float DoubleCarChance = 0.3f;

        private const int PerCar = 4;

        /// <summary>Ours, spawned to match. Four a car, same as theirs.</summary>
        private const int DefendersPerCar = 4;

        /// <summary>Where they come in from, and where they aim for.</summary>
        private const float ApproachDistance = 150f;
        private const float DropRange = 32f;

        /// <summary>Close enough to count as having turned up.</summary>
        private const float DefendRange = 70f;

        /// <summary>How often the war is even considered, and how likely it is when it is.</summary>
        private const int RollIntervalMs = 240000;
        private const float WarChance = 0.28f;

        /// <summary>Nothing starts within this of the last one ending.</summary>
        private const int CalmMs = 600000;

        private const int PedTypeCiv = 4;
        private const int UpdateIntervalMs = 700;

        private static readonly string[] RivalWeapons =
        {
            "WEAPON_PISTOL", "WEAPON_MICROSMG", "WEAPON_MACHINEPISTOL", "WEAPON_PUMPSHOTGUN"
        };

        private static readonly string[] DefenderWeapons =
        {
            "WEAPON_PISTOL", "WEAPON_MICROSMG", "WEAPON_MACHINEPISTOL"
        };

        // ---- state ------------------------------------------------------------

        private readonly GangRegistry _gangs;
        private readonly Affiliation _crew;
        private readonly PlayerState _state;
        private readonly Random _rng = new Random();

        private readonly List<WarTarget> _targets = new List<WarTarget>();

        private readonly List<Ped> _rivals = new List<Ped>();
        private readonly List<Ped> _defenders = new List<Ped>();
        private readonly List<Vehicle> _cars = new List<Vehicle>();
        private readonly List<Blip> _blips = new List<Blip>();

        private GangDef _attacker;
        private WarTarget _target;
        private Blip _marker;

        private int _lastUpdate;
        private int _nextRoll;
        private int _startedAt;
        private int _nextWave;
        private int _kills;
        private bool _showedUp;

        public GangWar(GangRegistry gangs, Affiliation crew, PlayerState state)
        {
            _gangs = gangs;
            _crew = crew;
            _state = state;
        }

        /// <summary>Set by Main. Null-checked, so the feed is never load-bearing.</summary>
        public SocialFeed Social;

        public bool IsRunning { get; private set; }

        public GangWar Defend(string who, Vector3 where)
        {
            _targets.Add(new WarTarget { Who = who, Where = where });
            return this;
        }

        // ---- per-tick ----------------------------------------------------------

        public void Update()
        {
            var now = Game.GameTime;
            if (now - _lastUpdate < UpdateIntervalMs) return;
            _lastUpdate = now;

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive)
            {
                if (IsRunning) End(false, "You went down.");
                return;
            }

            if (IsRunning) { Tick(player, now); return; }

            if (now < _nextRoll) return;
            _nextRoll = now + RollIntervalMs;

            // Only somebody who runs with a set has a set worth attacking.
            if (!_crew.IsAffiliated) return;
            if (_targets.Count == 0) return;
            if (_rng.NextDouble() > WarChance) return;

            Begin();
        }

        private void Begin()
        {
            _attacker = PickAttacker();
            if (_attacker == null) return;

            _target = _targets[_rng.Next(_targets.Count)];

            _startedAt = Game.GameTime;
            _nextWave = 0;
            _kills = 0;
            _showedUp = false;
            IsRunning = true;

            Mark();
            HoldTheLaw(true);
            SpawnDefenders();

            Notify.Important("~r~" + _attacker.Name + " rolling up on " + _target.Who + ".~s~ Get over there.");
            Log.Info("Gang war: " + _attacker.Id + " attacking " + _target.Who + ".");

            if (Social != null) Social.On(SocialEvent.WarStarted, _attacker.Name);

            SendWave(OpeningCars);
        }

        /// <summary>
        /// Switches the police off for the length of the raid, and back on when it ends.
        ///
        /// Two sets shooting at each other on a residential street would normally bring every
        /// unit in the division inside a minute, and then the fight you were asked to turn up
        /// for is a five-star chase you cannot win. The block settles this one itself; the law
        /// arrives afterwards, the way it always does.
        ///
        /// Restored FIRST in teardown, because leaving the player permanently un-arrestable is
        /// far worse than any amount of litter.
        /// </summary>
        private void HoldTheLaw(bool held)
        {
            try
            {
                if (held)
                {
                    Game.Player.Wanted.SetWantedLevel(0, false);
                    Game.Player.Wanted.ApplyWantedLevelChangeNow(false);
                }

                Function.Call(Hash.SET_MAX_WANTED_LEVEL, held ? 0 : 5);
                Function.Call(Hash.SET_POLICE_IGNORE_PLAYER, Game.Player.Handle, held);
                Function.Call(Hash.SET_CREATE_RANDOM_COPS, !held);

                Log.Info(held ? "Gang war: the law is off until it is over." : "Gang war: the law is back on.");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not change the wanted rules: " + ex.Message);
            }
        }

        private GangDef PickAttacker()
        {
            var mine = _crew.Current;
            if (mine == null) return null;

            var options = new List<GangDef>();

            foreach (var gang in _gangs.All)
            {
                if (gang.Id == mine.Id) continue;
                if (!mine.IsRivalOf(gang.Id) && !gang.IsRivalOf(mine.Id)) continue;
                if (gang.MemberModels.Count == 0) continue;

                options.Add(gang);
            }

            return options.Count == 0 ? null : options[_rng.Next(options.Count)];
        }

        private void Tick(Ped player, int now)
        {
            var elapsed = now - _startedAt;
            var here = player.Position.DistanceTo(_target.Where) <= DefendRange;

            if (here && !_showedUp)
            {
                _showedUp = true;
                Notify.Ticker("~g~You showed up.~s~ Hold the block.");
            }

            CountKills();

            // More of them, until the clock runs out.
            if (elapsed < WarMs - WaveGapMinMs && now >= _nextWave)
            {
                SendWave(_rng.NextDouble() < DoubleCarChance ? 2 : 1);
            }

            if (elapsed < WarMs) return;

            // Time. Anybody still standing decides they have made their point.
            End(_showedUp && _kills > 0, null);
        }

        private void SendWave(int cars)
        {
            _nextWave = Game.GameTime + WaveGapMinMs + _rng.Next(WaveGapMaxMs - WaveGapMinMs);

            for (var i = 0; i < cars; i++) SendCar();
        }

        /// <summary>
        /// One carload, driven in from a few streets out.
        ///
        /// Their own car, in their own colour, because a Balla raid arriving in a taxi is not a
        /// raid. Started well away and driven in for the same reason the police are: a car that
        /// simply exists beside you reads as a spawn, and one that comes round the corner reads
        /// as people who decided to come.
        /// </summary>
        private void SendCar()
        {
            if (_attacker == null || _target == null) return;

            var model = PickCar(_attacker);
            if (model == null) return;

            try
            {
                var angle = _rng.NextDouble() * Math.PI * 2.0;

                var far = _target.Where + new Vector3(
                    (float)Math.Cos(angle) * ApproachDistance,
                    (float)Math.Sin(angle) * ApproachDistance, 0f);

                var start = World.GetNextPositionOnStreet(far);
                if (start == Vector3.Zero) return;

                var car = World.CreateVehicle(model.Value, start);
                model.Value.MarkAsNoLongerNeeded();

                if (car == null || !car.Exists()) return;

                car.IsPersistent = true;
                car.IsEngineRunning = true;

                Paint(car, _attacker);
                _cars.Add(car);

                Ped driver = null;

                for (var seat = -1; seat < PerCar - 1; seat++)
                {
                    var ped = SpawnRival(car, seat);
                    if (ped == null) continue;

                    if (seat == -1) driver = ped;
                    _rivals.Add(ped);
                }

                if (driver != null)
                {
                    var drop = World.GetNextPositionOnStreet(_target.Where.Around(DropRange));
                    if (drop == Vector3.Zero) drop = _target.Where;

                    Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, driver.Handle, car.Handle,
                                  drop.X, drop.Y, drop.Z, 22f, 0, car.Model.Hash, 786603, 8f, true);
                }

                Log.Info("Gang war: a carload of " + _attacker.Id + " on the way in.");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not send a carload: " + ex.Message);
            }
        }

        private Model? PickCar(GangDef gang)
        {
            foreach (var name in CarsFor(gang.Id))
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) continue;
                    return model;
                }
                catch
                {
                    // Try the next.
                }
            }

            return null;
        }

        /// <summary>The cars each set actually drives.</summary>
        private static IEnumerable<string> CarsFor(string gangId)
        {
            switch (gangId)
            {
                case "ballas":
                    yield return "baller"; yield return "buccaneer2";
                    yield return "peyote"; yield return "manana";
                    break;

                case "vagos":
                    yield return "tornado"; yield return "chino2";
                    yield return "voodoo"; yield return "buccaneer";
                    break;

                case "marabunta":
                    yield return "virgo3"; yield return "tornado4"; yield return "primo";
                    break;

                default:
                    yield return "buccaneer"; yield return "manana"; yield return "primo";
                    break;
            }
        }

        /// <summary>Their colour, so you know who it is before anybody gets out.</summary>
        private static void Paint(Vehicle car, GangDef gang)
        {
            try
            {
                var c = gang.Colour;

                Function.Call(Hash.SET_VEHICLE_MOD_KIT, car.Handle, 0);
                Function.Call(Hash.SET_VEHICLE_CUSTOM_PRIMARY_COLOUR, car.Handle, (int)c.R, (int)c.G, (int)c.B);
                Function.Call(Hash.SET_VEHICLE_CUSTOM_SECONDARY_COLOUR, car.Handle, (int)c.R, (int)c.G, (int)c.B);
                Function.Call(Hash.SET_VEHICLE_WINDOW_TINT, car.Handle, 1);
            }
            catch
            {
                // A car in the wrong colour is still a car.
            }
        }

        private Ped SpawnRival(Vehicle car, int seat)
        {
            foreach (var name in _attacker.MemberModels)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1200)) continue;

                    var handle = Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE,
                                                    car.Handle, PedTypeCiv, model.Hash, seat, true, false);

                    model.MarkAsNoLongerNeeded();
                    if (handle == 0) continue;

                    var ped = Entity.FromHandle(handle) as Ped;
                    if (ped == null || !ped.Exists()) continue;

                    ped.IsPersistent = true;

                    Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle, _attacker.GroupHash);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 46, true);
                    Function.Call(Hash.SET_PED_ACCURACY, ped.Handle, 22);
                    Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, ped.Handle, 2);

                    Function.Call(Hash.GIVE_WEAPON_TO_PED, ped.Handle,
                                  Function.Call<uint>(Hash.GET_HASH_KEY,
                                                      RivalWeapons[_rng.Next(RivalWeapons.Length)]),
                                  200, false, true);

                    var blip = ped.AddBlip();
                    if (blip != null && blip.Exists())
                    {
                        blip.Color = BlipColor.Red;
                        blip.Scale = 0.65f;
                        blip.Name = _attacker.Name;
                        _blips.Add(blip);
                    }

                    return ped;
                }
                catch
                {
                    // Try the next model.
                }
            }

            return null;
        }

        /// <summary>
        /// Ours, already there.
        ///
        /// Matched to what is coming, because the block defending itself is the point -- you
        /// are the difference at the margin rather than the entire defence. It also means
        /// turning up two minutes late still matters, which a one-man last stand would not.
        /// </summary>
        private void SpawnDefenders()
        {
            var mine = _crew.Current;
            if (mine == null) return;

            var count = DefendersPerCar * OpeningCars;

            for (var i = 0; i < count; i++)
            {
                foreach (var name in mine.MemberModels)
                {
                    try
                    {
                        var model = new Model(name);
                        if (!model.IsValid || !model.IsInCdImage || !model.Request(1200)) continue;

                        var at = _target.Where.Around(4f + (float)_rng.NextDouble() * 12f);

                        var handle = Function.Call<int>(Hash.CREATE_PED, PedTypeCiv, model.Hash,
                                                        at.X, at.Y, at.Z, 0f, false, false);

                        model.MarkAsNoLongerNeeded();
                        if (handle == 0) continue;

                        var ped = Entity.FromHandle(handle) as Ped;
                        if (ped == null || !ped.Exists()) continue;

                        ped.IsPersistent = true;

                        Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle, mine.GroupHash);
                        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 46, true);
                        Function.Call(Hash.SET_PED_ACCURACY, ped.Handle, 25);

                        Function.Call(Hash.GIVE_WEAPON_TO_PED, ped.Handle,
                                      Function.Call<uint>(Hash.GET_HASH_KEY,
                                                          DefenderWeapons[_rng.Next(DefenderWeapons.Length)]),
                                      200, false, true);

                        var blip = ped.AddBlip();
                        if (blip != null && blip.Exists())
                        {
                            blip.Color = BlipColor.Green;
                            blip.Scale = 0.55f;
                            blip.Name = mine.Name;
                            _blips.Add(blip);
                        }

                        _defenders.Add(ped);
                        break;
                    }
                    catch
                    {
                        // Try the next model.
                    }
                }
            }

            Log.Info("Gang war: " + _defenders.Count + " of ours holding " + _target.Who + ".");
        }

        private void CountKills()
        {
            for (var i = _rivals.Count - 1; i >= 0; i--)
            {
                var ped = _rivals[i];
                if (ped != null && ped.Exists() && ped.IsAlive) continue;

                var byYou = ped != null && ped.Exists() &&
                            Function.Call<bool>(Hash.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY,
                                                ped.Handle, Game.Player.Character.Handle, true);

                if (byYou) _kills++;

                try { if (ped != null && ped.Exists()) ped.MarkAsNoLongerNeeded(); }
                catch { /* teardown */ }

                _rivals.RemoveAt(i);
            }
        }

        private void Mark()
        {
            ClearMarker();

            try
            {
                _marker = World.CreateBlip(_target.Where, DefendRange);
                if (_marker == null || !_marker.Exists()) return;

                _marker.Color = BlipColor.Red;
                _marker.Alpha = 110;
                _marker.ShowRoute = true;
                _marker.Name = _target.Who + " under attack";
            }
            catch (Exception ex)
            {
                Log.Debug("Could not mark the war: " + ex.Message);
            }
        }

        private void ClearMarker()
        {
            try { if (_marker != null && _marker.Exists()) _marker.Delete(); }
            catch { /* teardown */ }

            _marker = null;
        }

        // ---- finishing ---------------------------------------------------------

        private void End(bool held, string reason)
        {
            var attacker = _attacker;
            var kills = _kills;
            var showed = _showedUp;

            // Before anything else. A cleanup that throws must not leave the player unable to
            // attract police for the rest of the session.
            HoldTheLaw(false);

            // Whoever is left decides they have made their point and goes home.
            Scatter();

            IsRunning = false;
            _nextRoll = Game.GameTime + CalmMs;

            ClearMarker();
            Clear();

            if (!string.IsNullOrEmpty(reason))
            {
                Notify.Failure(reason);
                return;
            }

            if (held)
            {
                var rep = 25f + kills * 4f;

                _crew.AddRep(rep, "for holding the block");
                _state.AddRespect(rep * 0.6f);
                _state.Touch();

                Notify.Important("~g~You held it.~s~ " + kills + " of theirs down.");
                if (Social != null) Social.On(SocialEvent.WarHeld, attacker == null ? "" : attacker.Name);

                return;
            }

            if (!showed)
            {
                // Not turning up is the only real failure here. Everybody saw that you did not.
                _crew.AddRep(-30f, "for leaving the block");
                _state.Touch();

                Notify.Failure("they hit " + (_target == null ? "the block" : _target.Who) +
                               " and you were nowhere.");

                if (Social != null) Social.On(SocialEvent.WarLost, attacker == null ? "" : attacker.Name);
                return;
            }

            Notify.Ticker("~o~They pulled off.~s~ Nobody's calling that a win.");
        }

        /// <summary>Sends the survivors home rather than deleting them out from under you.</summary>
        private void Scatter()
        {
            var player = Game.Player.Character;

            foreach (var ped in _rivals)
            {
                if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

                try
                {
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);

                    if (player != null && player.Exists())
                    {
                        Function.Call(Hash.TASK_SMART_FLEE_PED, ped.Handle, player.Handle, 300f, -1, false, false);
                    }

                    ped.MarkAsNoLongerNeeded();
                }
                catch { /* they will find their own way */ }
            }

            _rivals.Clear();
        }

        private void Clear()
        {
            foreach (var blip in _blips)
            {
                try { if (blip != null && blip.Exists()) blip.Delete(); }
                catch { /* teardown */ }
            }
            _blips.Clear();

            foreach (var ped in _defenders)
            {
                try { if (ped != null && ped.Exists()) ped.MarkAsNoLongerNeeded(); }
                catch { /* teardown */ }
            }
            _defenders.Clear();

            // Let go, never deleted -- the same rule as everything else that ends up in a street.
            foreach (var car in _cars)
            {
                try { if (car != null && car.Exists()) car.MarkAsNoLongerNeeded(); }
                catch { /* teardown */ }
            }
            _cars.Clear();

            _attacker = null;
            _target = null;
        }

        public void RestoreWorld()
        {
            if (IsRunning) HoldTheLaw(false);

            Scatter();
            ClearMarker();
            Clear();
            IsRunning = false;
        }

        // ---- hud ---------------------------------------------------------------

        /// <summary>How long is left, and how it is going.</summary>
        public void Draw()
        {
            if (!IsRunning || _target == null) return;

            var left = Math.Max(0, WarMs - (Game.GameTime - _startedAt));
            var done = 1f - left / (float)WarMs;

            const float x = 0.5f;
            const float y = 0.115f;
            const float w = 0.22f;
            const float h = 0.014f;

            Hud.Text(_target.Who.ToUpperInvariant() + " UNDER ATTACK", x, y - 0.052f, 0.60f,
                     Palette.Danger, Hud.FontCursive);

            Hud.Text(_attacker == null ? "" : _attacker.Name.ToUpperInvariant(),
                     x, y - 0.018f, 0.28f, Palette.TextDim, Hud.FontLabel);

            Hud.Rect(x, y, w + 0.004f, h + 0.004f, System.Drawing.Color.FromArgb(190, 8, 8, 10));
            Hud.Rect(x, y, w, h, System.Drawing.Color.FromArgb(160, 30, 32, 34));

            var filled = w * done;
            Hud.Rect(x - (w - filled) * 0.5f, y, filled, h, Palette.Danger);

            Hud.Text((left / 1000) + "s   ·   " + _kills + " down", x, y + 0.016f, 0.26f,
                     Palette.TextDim, Hud.FontBody);
        }
    }
}
