using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Social;

namespace Hoodrich.Gangs
{
    /// <summary>
    /// The lowriders, out once a night.
    ///
    /// Some time after ten, three of them come round the Families' blocks in a line: green
    /// on green with the neon on, hydraulics fitted, full of the set -- men and women -- with
    /// the radio up. They drive slow, they follow each other, and now and then one of them
    /// hits the switch: the front comes up and the back stays down for a few seconds.
    ///
    /// Nothing is asked of you and there is no blip. The night's time is picked once, when
    /// the clock comes round, and the cars are only actually built while you are on or near
    /// the turf to see them; a night you were elsewhere is a night it happened without you.
    ///
    /// ONE NIGHT THE LOWRIDERS, THE NEXT THE DONKS. Same three in a line, same set in them,
    /// same radio up; the donks are the Faction Custom Donk and the Benny's bodies on
    /// Benny's rims, riding up on the hydraulics rather than dropped, each its own green,
    /// and the switch drops the front rather than raising it. Which crew is out is the
    /// night's parity, so they alternate whatever the calendar does at the end of a month.
    /// </summary>
    internal sealed class Cruise
    {
        private enum Look
        {
            Lowriders,
            Donks
        }

        private sealed class Low
        {
            public Vehicle Car;
            public Ped Driver;
            public readonly List<Ped> Crew = new List<Ped>();

            /// <summary>The leader's next corner, and when it was given; the followers' last order.</summary>
            public Vector3 Target;
            public int AimedAt;
            public int Followed;

            /// <summary>Until when the front is up.</summary>
            public int SwitchUntil;
        }

        private readonly Settings _cfg;
        private readonly GangRegistry _gangs;
        private readonly string _gangId;
        private readonly Random _rng = new Random();
        private readonly List<Low> _out = new List<Low>();

        /// <summary>Set by Main: whether something else owns the block right now.</summary>
        public Func<bool> Busy;

        /// <summary>Set by Main: the feed, so the block can say it heard them.</summary>
        public SocialFeed Social;

        private int _next;
        private int _plannedNight = -1;
        private int _doneNight = -1;
        private int _startAt;
        private int _startedAt;
        private int _farSince;
        private int _nextSwitch;
        private int _nextTune;
        private Look _look;

        private const int TickMs = 700;

        /// <summary>
        /// The night, in minutes past noon, so midnight does not split it. Tonight's start
        /// is drawn between twenty to eleven and ten to twelve; after one it is too late.
        /// </summary>
        private const int EarliestMin = 21 * 60 + 40 - 12 * 60;
        private const int StartSpanMin = 70;
        private const int WindowEndMin = 25 * 60 - 12 * 60;

        /// <summary>How long they are out, and how long you can be gone before they go home.</summary>
        private const int CruiseMs = 12 * 60 * 1000;
        private const float LetGoRange = 420f;
        private const int LetGoMs = 60000;

        /// <summary>Where they turn up relative to you: down the street, not in your face.</summary>
        private const float SpawnNear = 110f;
        private const float SpawnFar = 190f;
        private const float SpawnMin = 85f;
        private const float NearTurf = 220f;

        /// <summary>Fairly slow. Seven and a half is under thirty, which is a cruise.</summary>
        private const float Speed = 7.5f;
        private const float FollowSpeed = 9f;
        private const int Style = 786603;
        private const int FollowGap = 7;
        private const float LineGap = 9f;
        private const int FollowAgainMs = 20000;
        private const float FallenBehind = 60f;
        private const float CornerReached = 22f;
        private const int CornerMs = 60000;

        private const int Cars = 3;

        private const int SwitchMinMs = 14000;
        private const int SwitchMaxMs = 30000;
        private const int HoldMinMs = 2500;
        private const int HoldMaxMs = 4500;
        private const int TuneEveryMs = 5000;

        private const int FemaleChance = 45;
        private const int DarkGreen = 49;
        private const int DefaultPearl = 53;
        private const int PedTypeCiv = 4;

        /// <summary>The makes. Every name here is one the Rollers already checked against its hash.</summary>
        private static readonly string[] Lowriders =
        {
            "voodoo", "buccaneer2", "chino2", "faction2", "sabregt2", "virgo2", "primo2"
        };

        /// <summary>The donk itself first, then the Benny's bodies that go up on big rims.</summary>
        private static readonly string[] Donks =
        {
            "faction3", "chino2", "buccaneer2", "voodoo", "sabregt2", "virgo2", "faction2"
        };

        /// <summary>
        /// The greens, for the donks: every one of the three a different one, so they read as
        /// three cars somebody owns rather than a fleet. Racing, bright, gasoline, lime,
        /// hunter, dark -- the game's own names for them.
        /// </summary>
        /// <summary>
        /// The greens, and the flake over them.
        ///
        /// TWO GREENS AND ONE SILVER, DOWN FROM SIX AND THREE, and the ones that went were the
        /// ones nobody had checked. This list carried 50, 54, 55 and 139 on the strength of
        /// somebody having called them greens once; the two that are left are the two the
        /// game's own table names -- 49 is the dark metallic green the whole set is painted
        /// in and 53 is the bright one. A colour index nobody has looked up is a car that
        /// turns out to be red on somebody else's install.
        ///
        /// The pearl is silver on all of them. A green flake in green paint does nothing; a
        /// silver one catches the light as the car turns, which is the entire effect.
        /// </summary>
        private static readonly int[] Greens = { 49, 53 };
        private static readonly int[] Pearls = { 4 };

        private const string Female = "g_f_y_families_01";

        public Cruise(Settings cfg, GangRegistry gangs, string gangId)
        {
            _cfg = cfg;
            _gangs = gangs;
            _gangId = gangId;
        }

        private bool Enabled => _cfg == null || _cfg.CruiseEnabled;

        public void Update()
        {
            var now = Game.GameTime;
            if (now < _next) return;
            _next = now + TickMs;

            if (!Enabled)
            {
                if (_out.Count > 0) Pack(false, "switched off");
                return;
            }

            if (Busy != null && Busy())
            {
                if (_out.Count > 0) Pack(false, "something else is happening on the block");
                return;
            }

            int hour, minute, night;

            try
            {
                hour = Function.Call<int>(Hash.GET_CLOCK_HOURS);
                minute = Function.Call<int>(Hash.GET_CLOCK_MINUTES);
                night = Function.Call<int>(Hash.GET_CLOCK_DAY_OF_MONTH) + 31 * Function.Call<int>(Hash.GET_CLOCK_MONTH);
            }
            catch
            {
                return;
            }

            if (hour < 12) night -= 1;
            var late = hour * 60 + minute + (hour < 12 ? 24 * 60 : 0) - 12 * 60;

            if (_out.Count > 0)
            {
                Keep(now);
                return;
            }

            if (_plannedNight != night)
            {
                _plannedNight = night;
                _startAt = EarliestMin + _rng.Next(StartSpanMin + 1);
                _look = (night & 1) == 0 ? Look.Lowriders : Look.Donks;
                Log.Info("Cruise: the " + Word() + " are out tonight at " + Clock(_startAt) + ".");
            }

            if (_doneNight == night || late < _startAt) return;

            if (late > WindowEndMin)
            {
                _doneNight = night;
                Log.Info("Cruise: nobody on the block to see the " + Word() + " tonight.");
                return;
            }

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !Near(player.Position)) return;

            if (Start(player, now)) _doneNight = night;
        }

        // ---- out ------------------------------------------------------------

        private bool Start(Ped player, int now)
        {
            Vector3 at;
            float heading;

            if (!Kerb(player.Position, out at, out heading)) return false;

            var gang = _gangs == null ? null : _gangs.Get(_gangId);
            if (gang == null) return false;

            var rad = heading * (float)Math.PI / 180f;
            var forward = new Vector3(-(float)Math.Sin(rad), (float)Math.Cos(rad), 0f);

            var makes = new List<string>(_look == Look.Donks ? Donks : Lowriders);
            var names = new List<string>();
            var heads = 0;

            for (var i = 0; i < Cars && makes.Count > 0; i++)
            {
                var spot = at - forward * (LineGap * i);
                var car = Make(makes, spot, heading);
                if (car == null) break;

                var low = new Low { Car = car };

                Dress(car, gang, i);
                heads += Fill(low, gang);

                if (low.Driver == null)
                {
                    Scrap(low);
                    break;
                }

                _out.Add(low);
                names.Add(car.DisplayName.ToLowerInvariant());
            }

            if (_out.Count == 0)
            {
                Log.Info("Cruise: could not put one of the " + Word() + " on the road tonight.");
                return false;
            }

            _startedAt = now;
            _farSince = 0;
            _nextSwitch = now + SwitchMinMs;
            _nextTune = 0;

            foreach (var low in _out) Drive(low, now, true);

            Log.Info("Cruise: " + _out.Count + " " + Word() + " out on the block at " + Clock(Now()) + " -- " +
                     string.Join(", ", names.ToArray()) + ", " + heads + " of the set in them.");

            try { if (Social != null) Social.On(_look == Look.Donks ? SocialEvent.Donks : SocialEvent.Cruise); }
            catch { /* the block heard them either way */ }

            return true;
        }

        /// <summary>
        /// A road on our turf, down the street from you: out of sight of where you stand,
        /// close enough that the sound arrives first. Position and heading of the node, so
        /// the line can be laid along the road it is on.
        /// </summary>
        private bool Kerb(Vector3 from, out Vector3 at, out float heading)
        {
            at = Vector3.Zero;
            heading = 0f;

            for (var tries = 0; tries < 16; tries++)
            {
                var probe = from.Around(SpawnNear + (float)_rng.NextDouble() * (SpawnFar - SpawnNear));

                try
                {
                    var pos = new OutputArgument();
                    var head = new OutputArgument();

                    if (!Function.Call<bool>(Hash.GET_CLOSEST_VEHICLE_NODE_WITH_HEADING,
                                             probe.X, probe.Y, probe.Z, pos, head, 1, 3f, 0f)) continue;

                    var got = pos.GetResult<Vector3>();
                    if (got == Vector3.Zero) continue;
                    if (got.DistanceTo(from) < SpawnMin) continue;
                    if (!Ours(got)) continue;

                    at = got;
                    heading = head.GetResult<float>();
                    return true;
                }
                catch
                {
                    // The next probe.
                }
            }

            return false;
        }

        private Vehicle Make(List<string> makes, Vector3 at, float heading)
        {
            while (makes.Count > 0)
            {
                var name = makes[_rng.Next(makes.Count)];
                makes.Remove(name);

                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !Models.Ready(model)) continue;

                    var car = World.CreateVehicle(model, at, heading);
                    model.MarkAsNoLongerNeeded();

                    if (car == null || !car.Exists()) continue;

                    car.IsPersistent = true;

                    // Ours, so it is scenery until somebody gets in it. See Core.Petrol.
                    Core.Petrol.Spare(car);

                    Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, car.Handle);
                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, car.Handle, true, true);

                    return car;
                }
                catch (Exception ex)
                {
                    Log.Debug("Cruise: could not make a " + name + ": " + ex.Message);
                }
            }

            return null;
        }

        /// <summary>
        /// Green on green, the hydraulics fitted, a few mods, the neon on and green, and the
        /// radio up. The lowriders are the set's own colour with the pearl over it, on
        /// lowrider wheels, dropped; the donks are each a green of their own, on Benny's
        /// rims, up.
        /// </summary>
        private void Dress(Vehicle car, GangDef gang, int index)
        {
            try
            {
                Plates.Stamp(car, gang, _rng);

                var donk = _look == Look.Donks;

                Function.Call(Hash.SET_VEHICLE_MOD_KIT, car.Handle, 0);
                Function.Call(Hash.SET_VEHICLE_LIVERY, car.Handle, -1);

                var paint = donk ? Greens[index % Greens.Length]
                          : gang != null && gang.Paint >= 0 ? gang.Paint : DarkGreen;
                var pearl = donk ? Pearls[index % Pearls.Length]
                          : _cfg == null ? DefaultPearl : _cfg.RollerPearl;

                Function.Call(Hash.SET_VEHICLE_COLOURS, car.Handle, paint, paint);
                Function.Call(Hash.SET_VEHICLE_EXTRA_COLOURS, car.Handle, pearl, 0);
                Function.Call(Hash.SET_VEHICLE_WINDOW_TINT, car.Handle, 2);
                Function.Call(Hash.SET_VEHICLE_DIRT_LEVEL, car.Handle, 0f);

                // The wheels: the type first, then any set of them. 2 is the lowrider rack, 8
                // is Benny's Original, which is where the big chrome lives.
                Function.Call(Hash.SET_VEHICLE_WHEEL_TYPE, car.Handle, donk ? 8 : 2);
                Mod(car, 23, 100);

                // Modified, not every slot: a car with everything on it is a menu, not a car.
                // The donks get more of them; that is what a donk is.
                foreach (var type in new[] { 0, 1, 2, 3, 4, 6, 7, 10 }) Mod(car, type, donk ? 80 : 65);

                // THE HYDRAULICS, which is what the switch works. The last set is the strongest.
                var hyd = Function.Call<int>(Hash.GET_NUM_VEHICLE_MODS, car.Handle, 38);
                if (hyd > 0) Function.Call(Hash.SET_VEHICLE_MOD, car.Handle, 38, hyd - 1, false);

                for (var side = 0; side < 4; side++)
                {
                    Function.Call(Hash.SET_VEHICLE_NEON_ENABLED, car.Handle, side, true);
                }

                Function.Call(Hash.SET_VEHICLE_NEON_COLOUR, car.Handle, 0, 255, 90);

                Stance(car, false);
                Tune(car);
            }
            catch (Exception ex)
            {
                Log.Debug("Cruise: could not dress one: " + ex.Message);
            }
        }

        private void Mod(Vehicle car, int type, int chance)
        {
            try
            {
                if (_rng.Next(100) >= chance) return;

                var n = Function.Call<int>(Hash.GET_NUM_VEHICLE_MODS, car.Handle, type);
                if (n <= 0) return;

                Function.Call(Hash.SET_VEHICLE_MOD, car.Handle, type, _rng.Next(n), false);
            }
            catch
            {
                // That slot stays stock.
            }
        }

        /// <summary>West Coast Classics for the lowriders, Radio Los Santos for the donks; loud, and kept that way.</summary>
        private void Tune(Vehicle car)
        {
            try
            {
                Function.Call(Hash.SET_VEHICLE_ENGINE_ON, car.Handle, true, true, false);
                Function.Call(Hash.SET_VEHICLE_RADIO_ENABLED, car.Handle, true);
                Function.Call(Hash.SET_VEH_RADIO_STATION, car.Handle,
                              _look == Look.Donks ? Radio.LosSantos : Radio.WestCoast);
                Function.Call(Hash.SET_VEHICLE_RADIO_LOUD, car.Handle, true);
            }
            catch
            {
                // Quiet, then.
            }
        }

        /// <summary>Every seat. The driver is one of the men; the rest are the set, women among them.</summary>
        private int Fill(Low low, GangDef gang)
        {
            var heads = 0;

            low.Driver = Seat(low, gang, -1, false);
            if (low.Driver != null) heads++;

            int seats;
            try { seats = Function.Call<int>(Hash.GET_VEHICLE_MAX_NUMBER_OF_PASSENGERS, low.Car.Handle); }
            catch { seats = 0; }

            for (var seat = 0; seat < seats; seat++)
            {
                if (Seat(low, gang, seat, _rng.Next(100) < FemaleChance) != null) heads++;
            }

            return heads;
        }

        private Ped Seat(Low low, GangDef gang, int seat, bool woman)
        {
            try
            {
                var name = woman || gang.MemberModels.Count == 0
                    ? Female
                    : gang.MemberModels[_rng.Next(gang.MemberModels.Count)];

                var model = new Model(name);
                if (!model.IsValid || !model.IsInCdImage || !Models.Ready(model)) return null;

                var handle = Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE, low.Car.Handle,
                                                PedTypeCiv, model.Hash, seat, false, false);
                model.MarkAsNoLongerNeeded();
                if (handle == 0) return null;

                var ped = Entity.FromHandle(handle) as Ped;
                if (ped == null || !ped.Exists()) return null;

                ped.IsPersistent = true;

                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, ped.Handle, true, true);
                Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle, gang.GroupHash);
                Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, ped.Handle, false);

                // Deaf to the street, so a backfire does not empty the car at the lights.
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, true);

                // AND THEY STAY IN IT. Combat attribute 3 is "can leave the vehicle" and it
                // is off, so nobody gets out for a fight; the flee attributes are cleared,
                // so nobody gets out to run; and the driver keeps his task through whatever
                // happens round him. A donk with its doors open at the lights and four of
                // the set stood in the road is not a cruise.
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 3, false);
                Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, ped.Handle, 0, false);
                Function.Call(Hash.SET_PED_KEEP_TASK, ped.Handle, true);

                Helmets.Off(ped);

                low.Crew.Add(ped);
                return ped;
            }
            catch (Exception ex)
            {
                Log.Debug("Cruise: could not fill a seat: " + ex.Message);
                return null;
            }
        }

        // ---- the drive ------------------------------------------------------

        private void Keep(int now)
        {
            // Anybody missing is out of the line; the line closes up behind the next.
            for (var i = _out.Count - 1; i >= 0; i--)
            {
                var low = _out[i];
                var gone = low.Car == null || !low.Car.Exists()
                           || low.Driver == null || !low.Driver.Exists() || !low.Driver.IsAlive;
                if (!gone) continue;

                Release(low);
                _out.RemoveAt(i);
            }

            if (_out.Count == 0)
            {
                Log.Info("Cruise: none of the " + Word() + " left on the road.");
                return;
            }

            if (now - _startedAt > CruiseMs)
            {
                Pack(false, "done for the night");
                return;
            }

            var player = Game.Player.Character;

            if (player != null && player.Exists() && player.Position.DistanceTo(_out[0].Car.Position) > LetGoRange)
            {
                if (_farSince == 0) _farSince = now;

                if (now - _farSince > LetGoMs)
                {
                    Pack(false, "you were long gone");
                    return;
                }
            }
            else
            {
                _farSince = 0;
            }

            for (var i = 0; i < _out.Count; i++) Drive(_out[i], now, false);

            Switches(now);

            if (now >= _nextTune)
            {
                _nextTune = now + TuneEveryMs;
                foreach (var low in _out) Tune(low.Car);
            }
        }

        /// <summary>The first one picks the corners; the rest follow the one in front.</summary>
        private void Drive(Low low, int now, bool fresh)
        {
            var index = _out.IndexOf(low);

            if (index <= 0)
            {
                var reached = low.Target != Vector3.Zero && low.Car.Position.DistanceTo(low.Target) < CornerReached;

                if (!fresh && low.Target != Vector3.Zero && !reached && now - low.AimedAt < CornerMs) return;

                Aim(low, now);
                return;
            }

            var lead = _out[index - 1];
            if (lead.Car == null || !lead.Car.Exists()) return;

            var behind = low.Car.Position.DistanceTo(lead.Car.Position);

            if (!fresh && now - low.Followed < FollowAgainMs && behind < FallenBehind) return;

            try
            {
                Function.Call(Hash.TASK_VEHICLE_FOLLOW, low.Driver.Handle, low.Car.Handle, lead.Car.Handle,
                              FollowSpeed, Style, FollowGap);
                Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, low.Driver.Handle, FollowSpeed);
            }
            catch
            {
                // Next tick.
            }

            low.Followed = now;
        }

        /// <summary>The leader's next corner: a road on our turf a couple of streets away.</summary>
        private void Aim(Low low, int now)
        {
            var from = low.Car.Position;
            var to = Vector3.Zero;

            for (var tries = 0; tries < 14; tries++)
            {
                var probe = from.Around(120f + (float)_rng.NextDouble() * 140f);

                try
                {
                    var id = Function.Call<int>(Hash.GET_NTH_CLOSEST_VEHICLE_NODE_ID,
                                                probe.X, probe.Y, probe.Z, 1 + _rng.Next(6), 1, 3f, 0f);

                    if (!Function.Call<bool>(Hash.IS_VEHICLE_NODE_ID_VALID, id)) continue;

                    var got = new OutputArgument();
                    Function.Call(Hash.GET_VEHICLE_NODE_POSITION, id, got);

                    var at = got.GetResult<Vector3>();
                    if (at == Vector3.Zero || at.DistanceTo(from) < 50f) continue;
                    if (!Ours(at)) continue;

                    to = at;
                    break;
                }
                catch
                {
                    // The next probe.
                }
            }

            low.AimedAt = now;

            if (to == Vector3.Zero)
            {
                // Nowhere on the turf to point at from here: wander, slowly, and try again in a minute.
                low.Target = Vector3.Zero;

                try
                {
                    Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, low.Driver.Handle, low.Car.Handle, Speed, Style);
                    Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, low.Driver.Handle, Speed);
                }
                catch
                {
                    // Next tick.
                }

                return;
            }

            low.Target = to;

            try
            {
                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, low.Driver.Handle, low.Car.Handle,
                              to.X, to.Y, to.Z, Speed, 0, low.Car.Model.Hash, Style, 12f, true);
                Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, low.Driver.Handle, Speed);
            }
            catch
            {
                // Next tick.
            }
        }

        // ---- the switch -----------------------------------------------------

        /// <summary>Now and then one of them puts the front up for a few seconds, back still down.</summary>
        private void Switches(int now)
        {
            foreach (var low in _out)
            {
                if (low.SwitchUntil != 0 && now >= low.SwitchUntil)
                {
                    low.SwitchUntil = 0;
                    Stance(low.Car, false);
                }
            }

            if (now < _nextSwitch) return;

            _nextSwitch = now + SwitchMinMs + _rng.Next(SwitchMaxMs - SwitchMinMs);

            var pick = _out[_rng.Next(_out.Count)];
            if (pick.SwitchUntil != 0) return;

            pick.SwitchUntil = now + HoldMinMs + _rng.Next(HoldMaxMs - HoldMinMs);
            Stance(pick.Car, true);
        }

        /// <summary>
        /// Where the car sits. A lowrider rides dropped and puts the front up on the switch;
        /// a donk rides up and drops the front on it. Wheels 0 and 1 are the front; 2 to 5
        /// cover the back on anything with four wheels or six.
        /// </summary>
        private void Stance(Vehicle car, bool switched)
        {
            if (car == null || !car.Exists()) return;

            var donk = _look == Look.Donks;
            var front = donk ? (switched ? 0f : 1f) : (switched ? 1f : 0f);
            var rear = donk ? 1f : 0f;

            try
            {
                for (var wheel = 0; wheel < 6; wheel++)
                {
                    Function.Call(Hash.SET_HYDRAULIC_SUSPENSION_RAISE_FACTOR, car.Handle, wheel,
                                  wheel < 2 ? front : rear);
                }
            }
            catch
            {
                // It stays where it is.
            }
        }

        // ---- home -----------------------------------------------------------

        private void Pack(bool scrap, string why)
        {
            foreach (var low in _out)
            {
                if (scrap) Scrap(low);
                else Release(low);
            }

            _out.Clear();
            Log.Info("Cruise: the " + Word() + " gone home -- " + why + ".");
        }

        private string Word()
        {
            return _look == Look.Donks ? "donks" : "lowriders";
        }

        /// <summary>Handed back to the game, driving away rather than parked where they were.</summary>
        private void Release(Low low)
        {
            try
            {
                if (low.Driver != null && low.Driver.Exists() && low.Driver.IsAlive
                    && low.Car != null && low.Car.Exists())
                {
                    Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, low.Driver.Handle, low.Car.Handle, 12f, Style);
                }

                foreach (var ped in low.Crew)
                {
                    if (ped == null || !ped.Exists()) continue;

                    // EVERYTHING Seat() DID, UNDONE. It makes them deaf to the street, unable
                    // to leave the car, unable to flee and impossible to drag out -- which is
                    // right for a cruise and is not something to hand back to the world. This
                    // released them with all five still set, so after every night's cruise
                    // four ambient drivers were out there who would sit at a red light being
                    // shot at. GangWar puts blocking back in four places; this was the one
                    // release path in the mod that never did.
                    try
                    {
                        Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);
                        Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, ped.Handle, true);
                        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 3, true);
                        Function.Call(Hash.SET_PED_KEEP_TASK, ped.Handle, false);
                    }
                    catch
                    {
                        // He drives off either way.
                    }

                    ped.IsPersistent = false;
                    ped.MarkAsNoLongerNeeded();
                }

                if (low.Car != null && low.Car.Exists())
                {
                    low.Car.IsPersistent = false;
                    low.Car.MarkAsNoLongerNeeded();
                }
            }
            catch
            {
                // Gone already.
            }
        }

        private static void Scrap(Low low)
        {
            try
            {
                foreach (var ped in low.Crew)
                {
                    if (ped != null && ped.Exists()) ped.Delete();
                }

                if (low.Car != null && low.Car.Exists()) low.Car.Delete();
            }
            catch
            {
                // Gone already.
            }
        }

        public void RestoreWorld()
        {
            foreach (var low in _out) Scrap(low);
            _out.Clear();
        }

        // ---- where ----------------------------------------------------------

        private bool Ours(Vector3 at)
        {
            try
            {
                var code = Function.Call<string>(Hash.GET_NAME_OF_ZONE, at.X, at.Y, at.Z) ?? "";
                var owner = _gangs == null ? null : _gangs.OwnerOfZone(code);

                return owner != null && string.Equals(owner.Id, _gangId, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>On the turf, or a couple of streets off it.</summary>
        private bool Near(Vector3 at)
        {
            if (Ours(at)) return true;

            return Ours(at + new Vector3(NearTurf, 0f, 0f)) || Ours(at - new Vector3(NearTurf, 0f, 0f))
                || Ours(at + new Vector3(0f, NearTurf, 0f)) || Ours(at - new Vector3(0f, NearTurf, 0f));
        }

        private static int Now()
        {
            try
            {
                var hour = Function.Call<int>(Hash.GET_CLOCK_HOURS);
                var minute = Function.Call<int>(Hash.GET_CLOCK_MINUTES);
                return hour * 60 + minute + (hour < 12 ? 24 * 60 : 0) - 12 * 60;
            }
            catch
            {
                return 0;
            }
        }

        private static string Clock(int late)
        {
            var minutes = (late + 12 * 60) % (24 * 60);
            return (minutes / 60).ToString("00") + ":" + (minutes % 60).ToString("00");
        }
    }
}
