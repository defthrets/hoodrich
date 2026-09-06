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
    /// </summary>
    internal sealed class Cruise
    {
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
            "voodoo", "buccaneer2", "chino2", "faction2", "sabregt2", "virgo2", "primo2", "faction3"
        };

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
                Log.Info("Lowriders: out tonight at " + Clock(_startAt) + ".");
            }

            if (_doneNight == night || late < _startAt) return;

            if (late > WindowEndMin)
            {
                _doneNight = night;
                Log.Info("Lowriders: nobody on the block to see them tonight.");
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

            var makes = new List<string>(Lowriders);
            var names = new List<string>();
            var heads = 0;

            for (var i = 0; i < Cars && makes.Count > 0; i++)
            {
                var spot = at - forward * (LineGap * i);
                var car = Make(makes, spot, heading);
                if (car == null) break;

                var low = new Low { Car = car };

                Dress(car, gang);
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
                Log.Info("Lowriders: could not put one on the road tonight.");
                return false;
            }

            _startedAt = now;
            _farSince = 0;
            _nextSwitch = now + SwitchMinMs;
            _nextTune = 0;

            foreach (var low in _out) Drive(low, now, true);

            Log.Info("Lowriders: " + _out.Count + " out on the block at " + Clock(Now()) + " -- " +
                     string.Join(", ", names.ToArray()) + ", " + heads + " of the set in them.");

            try { if (Social != null) Social.On(SocialEvent.Cruise); }
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

                    Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, car.Handle);
                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, car.Handle, true, true);

                    return car;
                }
                catch (Exception ex)
                {
                    Log.Debug("Lowriders: could not make a " + name + ": " + ex.Message);
                }
            }

            return null;
        }

        /// <summary>
        /// Green on green, the set's own colour with the pearl over it, lowrider wheels, the
        /// hydraulics fitted, a few mods, the neon on and green, and the radio up.
        /// </summary>
        private void Dress(Vehicle car, GangDef gang)
        {
            try
            {
                Function.Call(Hash.SET_VEHICLE_MOD_KIT, car.Handle, 0);
                Function.Call(Hash.SET_VEHICLE_LIVERY, car.Handle, -1);

                var paint = gang != null && gang.Paint >= 0 ? gang.Paint : DarkGreen;
                var pearl = _cfg == null ? DefaultPearl : _cfg.RollerPearl;

                Function.Call(Hash.SET_VEHICLE_COLOURS, car.Handle, paint, paint);
                Function.Call(Hash.SET_VEHICLE_EXTRA_COLOURS, car.Handle, pearl, 0);
                Function.Call(Hash.SET_VEHICLE_WINDOW_TINT, car.Handle, 2);
                Function.Call(Hash.SET_VEHICLE_DIRT_LEVEL, car.Handle, 0f);

                // Lowrider wheels: the type first, then any set of them.
                Function.Call(Hash.SET_VEHICLE_WHEEL_TYPE, car.Handle, 2);
                Mod(car, 23, 100);

                // Modified, not every slot: a car with everything on it is a menu, not a car.
                foreach (var type in new[] { 0, 1, 2, 3, 4, 6, 7, 10 }) Mod(car, type, 65);

                // THE HYDRAULICS, which is what the switch works. The last set is the strongest.
                var hyd = Function.Call<int>(Hash.GET_NUM_VEHICLE_MODS, car.Handle, 38);
                if (hyd > 0) Function.Call(Hash.SET_VEHICLE_MOD, car.Handle, 38, hyd - 1, false);

                for (var side = 0; side < 4; side++)
                {
                    Function.Call(Hash.SET_VEHICLE_NEON_ENABLED, car.Handle, side, true);
                }

                Function.Call(Hash.SET_VEHICLE_NEON_COLOUR, car.Handle, 0, 255, 90);

                Up(car, false);
                Tune(car);
            }
            catch (Exception ex)
            {
                Log.Debug("Lowriders: could not dress one: " + ex.Message);
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

        /// <summary>West Coast Classics, loud, and kept that way.</summary>
        private static void Tune(Vehicle car)
        {
            try
            {
                Function.Call(Hash.SET_VEHICLE_ENGINE_ON, car.Handle, true, true, false);
                Function.Call(Hash.SET_VEHICLE_RADIO_ENABLED, car.Handle, true);
                Function.Call(Hash.SET_VEH_RADIO_STATION, car.Handle, Radio.WestCoast);
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

                Helmets.Off(ped);

                low.Crew.Add(ped);
                return ped;
            }
            catch (Exception ex)
            {
                Log.Debug("Lowriders: could not fill a seat: " + ex.Message);
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
                Log.Info("Lowriders: none of them left on the road.");
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
                    Up(low.Car, false);
                }
            }

            if (now < _nextSwitch) return;

            _nextSwitch = now + SwitchMinMs + _rng.Next(SwitchMaxMs - SwitchMinMs);

            var pick = _out[_rng.Next(_out.Count)];
            if (pick.SwitchUntil != 0) return;

            pick.SwitchUntil = now + HoldMinMs + _rng.Next(HoldMaxMs - HoldMinMs);
            Up(pick.Car, true);
        }

        /// <summary>Front wheels up or down; the back stays down either way.</summary>
        private static void Up(Vehicle car, bool up)
        {
            if (car == null || !car.Exists()) return;

            try
            {
                for (var wheel = 0; wheel < 6; wheel++)
                {
                    Function.Call(Hash.SET_HYDRAULIC_SUSPENSION_RAISE_FACTOR, car.Handle, wheel,
                                  up && wheel < 2 ? 1f : 0f);
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
            Log.Info("Lowriders: gone home -- " + why + ".");
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
