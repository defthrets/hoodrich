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
        /// <summary>
        /// Gap between carloads.
        /// 
        /// Short enough that the next lot is on the block before the last is finished, which is
        /// what keeps it a fight rather than four separate skirmishes with quiet in between.
        /// Still irregular, so it never sounds like a metronome.
        /// </summary>
        private const int WaveGapMinMs = 22000;
        private const int WaveGapMaxMs = 42000;

        /// <summary>Two turn up together at the start; after that it is usually one.</summary>
        private const int OpeningCars = 2;
        private const float DoubleCarChance = 0.3f;

        /// <summary>
        /// Where ours stand when a given man is the one being hit.
        ///
        /// Exact coordinates, all three axes, used verbatim -- two of these are up on walkways
        /// and a ground probe would drop everybody standing on them into the courtyard below.
        /// Read off the HUD standing on each spot.
        /// </summary>
        private static readonly Dictionary<string, Vector3> Musters =
            new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase)
            {
                { "Grimes",  new Vector3(-134.105f, -1472.521f, 36.192f) },
                { "Stretch", new Vector3(-161.651f, -1637.566f, 37.246f) },
                { "Lamar",   new Vector3(-99.960f,  -1632.762f, 32.106f) },
            };

        private const int PerCar = 4;

        /// <summary>Ours, spawned to match. Four a car, same as theirs.</summary>
        private const int DefendersPerCar = 4;

        /// <summary>Where they come in from, and where they aim for.</summary>
        private const float ApproachDistance = 150f;
        private const float DropRange = 32f;

        /// <summary>Close enough to count as having turned up.</summary>
        private const float DefendRange = 70f;

        /// <summary>
        /// How often the war is even considered, and how likely it is when it is.
        ///
        /// Tuned for about one an hour of play. A raid on your own block is only an event while
        /// it is rare -- three of them in twenty minutes and it stops being a raid and becomes
        /// the weather.
        /// </summary>
        private const int RollIntervalMs = 600000;
        private const float WarChance = 0.16f;

        /// <summary>Nothing starts within this of the last one ending.</summary>
        private const int CalmMs = 1800000;

        /// <summary>Stars handed over once it is finished, and not a moment before.</summary>
        private const int StarsAfter = 2;

        private const int PedTypeCiv = 4;
        private const int UpdateIntervalMs = 700;

        /// <summary>
        /// What both sides bring.
        ///
        /// The same list for everybody, because this is one fight and a side that turns up with
        /// worse guns is a side that was always going to lose -- which makes turning up
        /// pointless rather than decisive.
        /// </summary>
        private static readonly string[] WarWeapons =
        {
            "WEAPON_COMPACTRIFLE", "WEAPON_APPISTOL", "WEAPON_MACHINEPISTOL",
            "WEAPON_MICROSMG", "WEAPON_MINISMG", "WEAPON_ASSAULTSMG"
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
            SpawnDefenders(DefendersPerCar * OpeningCars);

            Notify.Important("~r~" + _attacker.Name + " rolling up on " + _target.Who + ".~s~ Get over there.");
            Log.Info("Gang war: " + _attacker.Id + " attacking " + _target.Who + ".");

            // Held until somebody actually fires. Cars pulling up is not news, and narrating
            // the drive over defeats the arrival.
            if (Social != null)
            {
                Social.HoldUntilShots = true;
                Social.On(SocialEvent.WarStarted, _attacker.Name);
            }

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
            ListenForShots(player);
            PushIn(now);

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

            // Ours turn out again every time theirs do, so the fight stays even for the whole
            // five minutes rather than being decided by the first thirty seconds.
            SpawnDefenders(DefendersPerCar * cars);
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

        /// <summary>
        /// A car they have not turned up in yet.
        ///
        /// Five identical Ballers arriving one after another reads as a spawner rather than as
        /// people. The colour stays the same -- that is the set -- but the cars do not, and the
        /// list only resets once they have been through all of it.
        /// </summary>
        private Model? PickCar(GangDef gang)
        {
            var all = new List<string>(CarsFor(gang.Id));

            var fresh = new List<string>();
            foreach (var name in all)
            {
                if (!_usedCars.Contains(name)) fresh.Add(name);
            }

            if (fresh.Count == 0)
            {
                _usedCars.Clear();
                fresh = all;
            }

            // Shuffled, so the order is not the order they were written in.
            for (var i = fresh.Count - 1; i > 0; i--)
            {
                var j = _rng.Next(i + 1);
                var tmp = fresh[i]; fresh[i] = fresh[j]; fresh[j] = tmp;
            }

            foreach (var name in fresh)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) continue;

                    _usedCars.Add(name);
                    return model;
                }
                catch
                {
                    // Try the next.
                }
            }

            return null;
        }

        private readonly HashSet<string> _usedCars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The cars each set actually drives.</summary>
        private static IEnumerable<string> CarsFor(string gangId)
        {
            switch (gangId)
            {
                case "ballas":
                    yield return "baller"; yield return "buccaneer2"; yield return "peyote";
                    yield return "manana"; yield return "primo2"; yield return "tornado";
                    yield return "voodoo"; yield return "faction"; yield return "emperor";
                    break;

                case "vagos":
                    yield return "tornado"; yield return "chino2"; yield return "voodoo";
                    yield return "buccaneer"; yield return "primo"; yield return "faction2";
                    yield return "virgo"; yield return "manana"; yield return "peyote";
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
                    Function.Call(Hash.SET_PED_ACCURACY, ped.Handle, 22);

                    // The combat attributes that make somebody actually fight rather than stand
                    // in a street holding a rifle. 46 is "always fight", 5 is "leave the car to
                    // do it", 3 is "chase on foot", 1 is "use cover". Without these they arrive,
                    // get out, and look at each other -- which is exactly what happened.
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 46, true);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 5, true);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 3, true);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 1, true);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 2, true);

                    Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, ped.Handle, 2);
                    Function.Call(Hash.SET_PED_COMBAT_RANGE, ped.Handle, 2);
                    Function.Call(Hash.SET_PED_ALERTNESS, ped.Handle, 3);
                    Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, ped.Handle, 0, false);
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);

                    // They came for the block, so that is where they go, and they fight anybody
                    // in the way on their own initiative rather than waiting to be told.
                    Function.Call(Hash.TASK_COMBAT_HATED_TARGETS_AROUND_PED, ped.Handle, 120f, 0);

                    Function.Call(Hash.GIVE_WEAPON_TO_PED, ped.Handle,
                                  Function.Call<uint>(Hash.GET_HASH_KEY,
                                                      WarWeapons[_rng.Next(WarWeapons.Length)]),
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
        private void SpawnDefenders(int count)
        {
            var mine = _crew.Current;
            if (mine == null) return;

            // Everybody comes out of the same doorway -- the muster point for whoever is being
            // hit -- and then wanders, so they spread across the block and find the fight
            // themselves rather than being placed in a firing line.
            var muster = _target != null && Musters.ContainsKey(_target.Who)
                ? Musters[_target.Who]
                : _target.Where;

            for (var i = 0; i < count; i++)
            {
                foreach (var name in mine.MemberModels)
                {
                    try
                    {
                        var model = new Model(name);
                        if (!model.IsValid || !model.IsInCdImage || !model.Request(1200)) continue;

                        // On the mark itself, height and all. These spots are on walkways and
                        // stairs, so the authored Z is the whole point of them.
                        var at = muster;

                        var handle = Function.Call<int>(Hash.CREATE_PED, PedTypeCiv, model.Hash,
                                                        at.X, at.Y, at.Z, 0f, false, false);

                        model.MarkAsNoLongerNeeded();
                        if (handle == 0) continue;

                        var ped = Entity.FromHandle(handle) as Ped;
                        if (ped == null || !ped.Exists()) continue;

                        ped.IsPersistent = true;

                        Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle, mine.GroupHash);
                        Function.Call(Hash.SET_PED_ACCURACY, ped.Handle, 25);

                        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 46, true);
                        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 5, true);
                        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 3, true);
                        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 1, true);
                        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 2, true);

                        Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, ped.Handle, 2);
                        Function.Call(Hash.SET_PED_ALERTNESS, ped.Handle, 3);
                        Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, ped.Handle, 0, false);
                        Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);

                        // Wander first, fight on sight. Sent straight into combat they all run
                        // to the same corner; left to wander they spread out and meet whatever
                        // arrives, which looks like a block defending itself.
                        Function.Call(Hash.TASK_WANDER_IN_AREA, ped.Handle,
                                      muster.X, muster.Y, muster.Z, 45f, 3f, 10f);

                        Function.Call(Hash.TASK_COMBAT_HATED_TARGETS_AROUND_PED, ped.Handle, 90f, 0);

                        Function.Call(Hash.GIVE_WEAPON_TO_PED, ped.Handle,
                                      Function.Call<uint>(Hash.GET_HASH_KEY,
                                                          WarWeapons[_rng.Next(WarWeapons.Length)]),
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

        /// <summary>
        /// Keeps them moving toward the man they came for.
        ///
        /// Combat tasks make a ped fight whoever is in front of them, which is right until the
        /// nearest enemy is dead and they stand in the road having achieved their objective.
        /// Anybody not currently in a fight gets sent at the target again, so the pressure keeps
        /// arriving at the same place instead of dissolving into the street.
        /// </summary>
        private void PushIn(int now)
        {
            if (now < _nextPush) return;
            _nextPush = now + PushIntervalMs;

            foreach (var ped in _rivals)
            {
                if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

                try
                {
                    if (Function.Call<bool>(Hash.IS_PED_IN_COMBAT, ped.Handle, 0)) continue;

                    // Out of the car first if they are still in it, then on foot to the block.
                    if (ped.IsInVehicle()) Function.Call(Hash.TASK_LEAVE_ANY_VEHICLE, ped.Handle, 0, 0);

                    Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, ped.Handle,
                                  _target.Where.X, _target.Where.Y, _target.Where.Z,
                                  2.0f, 20000, 6f, 0, 0f);

                    Function.Call(Hash.TASK_COMBAT_HATED_TARGETS_AROUND_PED, ped.Handle, 120f, 0);
                }
                catch
                {
                    // They will find it or they will not.
                }
            }
        }

        private int _nextPush;

        /// <summary>How often anybody idle is pointed back at the block.</summary>
        private const int PushIntervalMs = 6000;

        /// <summary>
        /// Lets the feed start the moment the first round goes off.
        ///
        /// Checked on everybody, not just the player: the first shot is as likely to be one of
        /// theirs, and the post that matters is "shots on the block" rather than "the player has
        /// opened fire".
        /// </summary>
        private void ListenForShots(Ped player)
        {
            if (Social == null || !Social.HoldUntilShots) return;

            try
            {
                if (Function.Call<bool>(Hash.IS_PED_SHOOTING, player.Handle))
                {
                    Social.HoldUntilShots = false;
                    return;
                }

                foreach (var ped in _rivals)
                {
                    if (ped == null || !ped.Exists() || !ped.IsAlive) continue;
                    if (!Function.Call<bool>(Hash.IS_PED_SHOOTING, ped.Handle)) continue;

                    Social.HoldUntilShots = false;
                    return;
                }

                foreach (var ped in _defenders)
                {
                    if (ped == null || !ped.Exists() || !ped.IsAlive) continue;
                    if (!Function.Call<bool>(Hash.IS_PED_SHOOTING, ped.Handle)) continue;

                    Social.HoldUntilShots = false;
                    return;
                }
            }
            catch
            {
                // If we cannot tell, let it talk.
                Social.HoldUntilShots = false;
            }
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

            if (Social != null) Social.HoldUntilShots = false;

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

            // Two stars, now it is over. Nobody in that street called it in while it was
            // happening -- they called it in once the shooting stopped, which is both how it
            // actually goes and the reason the fight itself is allowed to be a fight.
            if (showed)
            {
                try
                {
                    Game.Player.Wanted.SetWantedLevel(StarsAfter, false);
                    Game.Player.Wanted.ApplyWantedLevelChangeNow(false);
                }
                catch { /* the law will find him eventually */ }
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
