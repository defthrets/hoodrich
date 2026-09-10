using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Locations
{
    /// <summary>One place a car goes, read out of carmeet.json.</summary>
    internal sealed class MeetSpot
    {
        public string Role = "park";
        public Vector3 At;
        public float Heading;

        public bool IsHopper => string.Equals(Role, "hop", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// THE CAR MEET.
    ///
    /// A dozen cars turn up on Carson Ave of an evening, park in a row, and sit there with
    /// their neons on. That is the whole of it, and every decision below is about the word
    /// TURN UP.
    ///
    /// THEY ARE NOT PLACED, THEY ARRIVE. Spawning twelve cars in twelve parking spaces takes
    /// one line and looks like exactly what it is: a car park that was empty and then was
    /// not. So each one is put on a road a few hundred metres out, given a driver and told to
    /// drive here -- normally, stopping at lights, in traffic, like anybody else -- and only
    /// once it is at the mouth of the meet is it told to park. You can stand on the corner
    /// and watch them come in, which is what a meet IS.
    ///
    /// AND THEN THEY ARE PLACED, ONCE. The game's own parking task gets a car near enough and
    /// no nearer; a row of twelve where each is a foot off and two degrees out reads as a
    /// shunt rather than a meet. So the last half-metre is a snap to the surveyed coordinate,
    /// done while the car is already stopped in roughly the right place, which is invisible
    /// and is the difference between parked and nearly parked.
    ///
    /// WHAT THEY LOOK LIKE. Competition suspension on everything, whatever the car has that
    /// is lowest. No liveries -- a livery is somebody's racing team and this is somebody's
    /// street. Any colour at all, and neons on most of them in any colour, because a car park
    /// where every car has neons is a showroom and one where none do is a car park.
    ///
    /// THE ONE ON JUICE is its own thing: a green lowrider away from the row, green neons,
    /// bouncing. See Hopping.
    ///
    /// It uses the same cars as the block and the takeovers, which is the point -- these are
    /// the set's cars, and turning up in the same Sultans and Sentinels is what makes it
    /// their meet rather than a car show.
    /// </summary>
    internal sealed class CarMeet
    {
        /// <summary>How far out they are put on the road, and how far they may be from a node.</summary>
        private const float ComeFromMin = 220f;
        private const float ComeFromMax = 480f;

        /// <summary>Near enough to the meet to stop driving and start parking.</summary>
        private const float ParkFrom = 26f;

        /// <summary>Near enough, and slow enough, to be sat down on the exact spot.</summary>
        private const float SeatWithin = 4.5f;
        private const float SeatSpeed = 1.6f;

        /// <summary>How long one car is given before it is simply put where it was going.</summary>
        private const int DriveGiveUpMs = 150000;
        private const int ParkGiveUpMs = 22000;

        /// <summary>Nothing is done to a car more often than this.</summary>
        private const int TickMs = 400;

        /// <summary>One goes every few seconds, so they arrive in a trickle rather than a convoy.</summary>
        private const int SendEveryMs = 3500;

        /// <summary>How far the player has to be for the meet to keep itself alive.</summary>
        private const float ForgetAt = 700f;

        /// <summary>Driving style: obey the lights, use the indicators, be a normal car.</summary>
        private const int RoadStyle = 786603;
        private const float RoadSpeed = 17f;

        /// <summary>How many of the twelve get neons.</summary>
        private const int NeonChance = 70;

        private sealed class Runner
        {
            public Vehicle Car;
            public Ped Driver;
            public MeetSpot Spot;

            public int SentAt;
            public bool Parking;
            public int ParkFrom;
            public bool Seated;

            public int HopAt;
            public bool HopUp;
        }

        private readonly List<MeetSpot> _spots = new List<MeetSpot>();
        private readonly List<Runner> _out = new List<Runner>();
        private readonly Random _rng = new Random();

        private bool _on;
        private int _lastTick;
        private int _lastSend;
        private int _next;
        private Vector3 _middle;
        private Blip _blip;

        /// <summary>Set by Main: the cars the set drives. See Takeover.</summary>
        public Func<string[]> Cars;
        public Func<string[]> Lowriders;

        public bool IsOn => _on;
        public int Parked
        {
            get
            {
                var n = 0;
                foreach (var r in _out) if (r.Seated) n++;
                return n;
            }
        }

        public int Places => _spots.Count;

        // ---- the file ----------------------------------------------------------------

        /// <summary>
        /// The spots, once.
        ///
        /// A meet with no file is not an error and does not complain twice: it is a mod
        /// somebody has deleted a data file out of, and the answer is to do nothing quietly
        /// rather than to log a line every tick for the rest of the session.
        /// </summary>
        public void Load()
        {
            _spots.Clear();

            try
            {
                var path = System.IO.Path.Combine(Paths.Data, "carmeet.json");

                if (!System.IO.File.Exists(path))
                {
                    Log.Info("No carmeet.json, so there is nowhere to hold one.");
                    return;
                }

                var doc = JsonFile.Read(path);
                var list = doc["spots"];

                if (list.Kind != JsonKind.Array) return;

                for (var i = 0; i < list.Count; i++)
                {
                    var n = list[i];

                    var spot = new MeetSpot
                    {
                        Role = n["role"].AsString("park"),
                        At = new Vector3(n["x"].AsFloat(0f), n["y"].AsFloat(0f), n["z"].AsFloat(0f)),
                        Heading = n["heading"].AsFloat(0f)
                    };

                    if (Math.Abs(spot.At.X) < 0.01f && Math.Abs(spot.At.Y) < 0.01f) continue;

                    _spots.Add(spot);
                }

                if (_spots.Count == 0) return;

                var sum = Vector3.Zero;
                foreach (var s in _spots) sum += s.At;
                _middle = sum / _spots.Count;

                Log.Info("Car meet: " + _spots.Count + " space(s) around " +
                         _middle.X.ToString("0") + ", " + _middle.Y.ToString("0") + ".");
            }
            catch (Exception ex)
            {
                Log.Warn("carmeet.json would not read: " + ex.Message);
                _spots.Clear();
            }
        }

        // ---- starting and stopping -----------------------------------------------------

        /// <summary>Put one on. Returns why not, or null once it is running.</summary>
        public string Start()
        {
            if (_spots.Count == 0) return "there's nowhere to hold one.";
            if (_on) return "there's one on already.";

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return "not right now.";

            _on = true;
            _next = 0;
            _lastSend = 0;

            Blip();

            Log.Info("Car meet: on, " + _spots.Count + " space(s) to fill.");
            return null;
        }

        public void Stop()
        {
            foreach (var r in _out)
            {
                try
                {
                    if (r.Driver != null && r.Driver.Exists())
                    {
                        r.Driver.MarkAsNoLongerNeeded();
                        r.Driver.Delete();
                    }

                    if (r.Car != null && r.Car.Exists()) r.Car.MarkAsNoLongerNeeded();
                }
                catch
                {
                    // The game takes them back.
                }
            }

            _out.Clear();
            _next = 0;
            _on = false;

            Unblip();
        }

        // ---- the tick -------------------------------------------------------------------

        public void Update()
        {
            if (!_on) return;

            var now = Game.GameTime;
            if (now - _lastTick < TickMs) return;
            _lastTick = now;

            try
            {
                var player = Game.Player.Character;
                if (player == null || !player.Exists()) return;

                // FAR ENOUGH AWAY AND IT NEVER HAPPENED. Twelve cars and twelve drivers held
                // across the map is twelve of everything the game could have been using for
                // wherever the player actually is.
                if (player.Position.DistanceTo(_middle) > ForgetAt) { Stop(); return; }

                Sending(player, now);

                foreach (var r in _out) Driving(r, now);
            }
            catch (Exception ex)
            {
                Log.Debug("The meet fell over: " + ex.Message);
            }
        }

        /// <summary>One more car sets off, every few seconds, until the places are full.</summary>
        private void Sending(Ped player, int now)
        {
            if (_next >= _spots.Count) return;
            if (_lastSend != 0 && now - _lastSend < SendEveryMs) return;

            _lastSend = now;

            var spot = _spots[_next];
            _next++;

            var r = Send(player, spot, now);

            if (r != null) _out.Add(r);
        }

        /// <summary>
        /// One car, on a road, a long way off, pointed at its space.
        ///
        /// GET_NTH_CLOSEST_VEHICLE_NODE_WITH_HEADING rather than a coordinate we picked: the
        /// car has to start ON a road facing the way the road goes, or the first thing it does
        /// is a three-point turn in somebody's garden. Asked around a point a few hundred
        /// metres out in a random direction, so they do not all come down the same street.
        /// </summary>
        private Runner Send(Ped player, MeetSpot spot, int now)
        {
            try
            {
                var names = spot.IsHopper
                    ? (Lowriders == null ? null : Lowriders())
                    : (Cars == null ? null : Cars());

                if (names == null || names.Length == 0) return null;

                var model = Pick(names);
                if (!model.IsValid) return null;

                var far = ComeFromMin + (float)_rng.NextDouble() * (ComeFromMax - ComeFromMin);
                var way = (float)(_rng.NextDouble() * Math.PI * 2.0);

                var probe = _middle + new Vector3((float)Math.Cos(way) * far,
                                                  (float)Math.Sin(way) * far, 0f);

                var node = new OutputArgument();
                var head = new OutputArgument();

                if (!Function.Call<bool>(Hash.GET_NTH_CLOSEST_VEHICLE_NODE_WITH_HEADING,
                                         probe.X, probe.Y, probe.Z, 1, node, head, 0, 0, 0))
                {
                    model.MarkAsNoLongerNeeded();
                    return null;
                }

                var at = node.GetResult<Vector3>();

                var car = World.CreateVehicle(model, at, head.GetResult<float>());
                model.MarkAsNoLongerNeeded();

                if (car == null || !car.Exists()) return null;

                car.IsPersistent = true;
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, car.Handle, true, true);

                Dress(car, spot);

                var driver = car.CreatePedOnSeat(VehicleSeat.Driver, DriverModel());

                if (driver == null || !driver.Exists())
                {
                    car.Delete();
                    return null;
                }

                driver.IsPersistent = true;
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, driver.Handle, true, true);
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, driver.Handle, true);
                Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, driver.Handle, false);
                Function.Call(Hash.SET_DRIVER_ABILITY, driver.Handle, 1f);
                Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, driver.Handle, 0.1f);

                // The mouth of the meet rather than the space itself. He drives here like
                // anybody else and only starts parking once he has arrived -- see Driving.
                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, driver.Handle, car.Handle,
                              spot.At.X, spot.At.Y, spot.At.Z,
                              RoadSpeed, 0, car.Model.Hash, RoadStyle, 12f, true);

                return new Runner { Car = car, Driver = driver, Spot = spot, SentAt = now };
            }
            catch (Exception ex)
            {
                Log.Debug("Could not send a car to the meet: " + ex.Message);
                return null;
            }
        }

        /// <summary>Where one car is up to.</summary>
        private void Driving(Runner r, int now)
        {
            if (r.Car == null || !r.Car.Exists()) return;

            if (r.Seated) { Hopping(r, now); return; }

            var gap = r.Car.Position.DistanceTo(r.Spot.At);

            // AT THE MOUTH OF IT, so stop driving and start parking.
            if (!r.Parking && gap < ParkFrom)
            {
                r.Parking = true;
                r.ParkFrom = now;

                try
                {
                    Function.Call(Hash.CLEAR_PED_TASKS, r.Driver.Handle);

                    // Mode 0 is nose first, which is how anybody pulls into a bay.
                    Function.Call(Hash.TASK_VEHICLE_PARK, r.Driver.Handle, r.Car.Handle,
                                  r.Spot.At.X, r.Spot.At.Y, r.Spot.At.Z, r.Spot.Heading,
                                  0, 20f, true);
                }
                catch
                {
                    // The seat below catches it either way.
                }

                return;
            }

            // NEAR ENOUGH AND STOPPED, or it has had long enough. Either way it goes on the
            // spot exactly -- see the note on the class about the last half-metre.
            var slow = false;
            try { slow = r.Car.Speed < SeatSpeed; } catch { slow = true; }

            var close = gap < SeatWithin && slow;
            var late = r.Parking ? now - r.ParkFrom > ParkGiveUpMs : now - r.SentAt > DriveGiveUpMs;

            if (close || late) Seat(r, late && !close);
        }

        /// <summary>
        /// The car, on its spot, exactly.
        ///
        /// NO OFFSET. SET_ENTITY_COORDS applies the game's own lift and a car put down with it
        /// stands a foot in the air and then drops, which on twelve cars in a row is twelve
        /// visible thumps. The spot's Z came off a car that was parked in it, so it is already
        /// the right height.
        /// </summary>
        private void Seat(Runner r, bool hauled)
        {
            r.Seated = true;

            try
            {
                Function.Call(Hash.CLEAR_PED_TASKS, r.Driver.Handle);

                Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, r.Car.Handle,
                              r.Spot.At.X, r.Spot.At.Y, r.Spot.At.Z, false, false, false);

                Function.Call(Hash.SET_ENTITY_HEADING, r.Car.Handle, r.Spot.Heading);
                Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, r.Car.Handle);

                r.Car.Velocity = Vector3.Zero;

                Function.Call(Hash.SET_VEHICLE_HANDBRAKE, r.Car.Handle, true);
                Function.Call(Hash.SET_VEHICLE_ENGINE_ON, r.Car.Handle, r.Spot.IsHopper, true, true);
                Function.Call(Hash.SET_VEHICLE_LIGHTS, r.Car.Handle, r.Spot.IsHopper ? 2 : 0);

                // He sits in it until there is somewhere for him to be. The peds who get out
                // and stand about are the next piece of this and are not written yet.
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, r.Driver.Handle, true);
            }
            catch
            {
                // It is where it is.
            }

            if (hauled) Log.Debug("Car meet: one was put in its space rather than driving in.");
        }

        // ---- the one on juice ---------------------------------------------------------------

        /// <summary>
        /// The lowrider, bouncing.
        ///
        /// TWO STATES ON A CLOCK, not a held one. SET_HYDRAULIC_VEHICLE_STATE is a pose rather
        /// than a motion -- it puts the car somewhere and leaves it there -- so a bounce is
        /// asking for one and then the other, which is exactly what somebody working the
        /// switches is doing anyway.
        ///
        /// The gap is not the same every time. A car hopping on a metronome reads as a script
        /// and a car hopping when somebody hits the switch reads as a car.
        /// </summary>
        private void Hopping(Runner r, int now)
        {
            if (!r.Spot.IsHopper) return;
            if (r.HopAt != 0 && now < r.HopAt) return;

            r.HopUp = !r.HopUp;
            r.HopAt = now + HopMinMs + _rng.Next(HopVaryMs);

            try
            {
                Function.Call(Hash.SET_HYDRAULIC_VEHICLE_STATE, r.Car.Handle, r.HopUp ? 1 : 0);
            }
            catch
            {
                // Some builds have no juice. It is still a green lowrider with green neons.
            }
        }

        private const int HopMinMs = 700;
        private const int HopVaryMs = 900;

        // ---- what they look like ---------------------------------------------------------------

        /// <summary>
        /// Competition suspension, no livery, any colour, and neons on most of them.
        ///
        /// THE LOWEST THE CAR HAS, rather than index four. Suspension runs stock, lowered,
        /// street, sport, competition -- but not every car has all five, and asking for an
        /// index a car does not have leaves it at stock, which on a row of twelve shows up as
        /// two of them sitting high for no reason anybody could explain. The count is asked
        /// for and the last one taken, which is competition wherever competition exists and
        /// the lowest it goes everywhere else.
        ///
        /// AND THE MOD KIT FIRST. Nothing takes without it, which is the quiet way a car ends
        /// up looking untouched with no error anywhere.
        /// </summary>
        private void Dress(Vehicle car, MeetSpot spot)
        {
            try
            {
                var h = car.Handle;

                Function.Call(Hash.SET_VEHICLE_MOD_KIT, h, 0);

                var many = Function.Call<int>(Hash.GET_NUM_VEHICLE_MODS, h, Suspension);
                if (many > 0) Function.Call(Hash.SET_VEHICLE_MOD, h, Suspension, many - 1, false);

                // NO RACING TEAMS. A livery is somebody's sponsor and this is somebody's
                // street. Both calls, because the older cars carry it as a livery and the
                // newer ones as mod slot forty-eight, and a car can have either.
                Function.Call(Hash.SET_VEHICLE_LIVERY, h, -1);
                Function.Call(Hash.SET_VEHICLE_MOD, h, Livery, -1, false);

                Function.Call(Hash.SET_VEHICLE_DIRT_LEVEL, h, 0f);
                Function.Call(Hash.SET_VEHICLE_WINDOW_TINT, h, _rng.Next(2) == 0 ? 1 : 3);

                if (spot.IsHopper)
                {
                    // GREEN, AND GREEN. The one car at this meet that is not "whatever colour"
                    // -- it is the set's car and it is out on its own for people to look at.
                    Function.Call(Hash.SET_VEHICLE_COLOURS, h, Green, Green);
                    Function.Call(Hash.SET_VEHICLE_EXTRA_COLOURS, h, GreenPearl, 0);

                    Neon(car, 0, 255, 60);

                    Function.Call(Hash.SET_CAN_USE_HYDRAULICS, h, true);
                    return;
                }

                var paint = Paints[_rng.Next(Paints.Length)];

                Function.Call(Hash.SET_VEHICLE_COLOURS, h, paint, paint);
                Function.Call(Hash.SET_VEHICLE_EXTRA_COLOURS, h, Paints[_rng.Next(Paints.Length)], 0);

                // MOST OF THEM, NOT ALL OF THEM. A car park where every car glows is a
                // showroom; one where none do is a car park. Somewhere in between is a meet.
                if (_rng.Next(100) >= NeonChance) return;

                Neon(car, _rng.Next(60, 256), _rng.Next(60, 256), _rng.Next(60, 256));
            }
            catch (Exception ex)
            {
                Log.Debug("Could not dress a meet car: " + ex.Message);
            }
        }

        /// <summary>All four tubes, in one colour.</summary>
        private static void Neon(Vehicle car, int r, int g, int b)
        {
            try
            {
                for (var i = 0; i < 4; i++)
                {
                    Function.Call(Hash.SET_VEHICLE_NEON_ENABLED, car.Handle, i, true);
                }

                Function.Call(Hash.SET_VEHICLE_NEON_COLOUR, car.Handle, r, g, b);
            }
            catch
            {
                // Not every body takes them.
            }
        }

        /// <summary>The mod slots. 15 is suspension, 48 the livery the newer cars use.</summary>
        private const int Suspension = 15;
        private const int Livery = 48;

        /// <summary>The green, and the flake over it. The game's own numbers.</summary>
        private const int Green = 53;
        private const int GreenPearl = 55;

        /// <summary>
        /// Whatever colour, out of the ones that read as a paint job.
        ///
        /// Not 0-159 at random: a good third of the game's palette is primer, rust, matte
        /// service colours and the browns off a taxi, and a meet made of those looks like a
        /// scrapyard. These are the metallics and the brights.
        /// </summary>
        private static readonly int[] Paints =
        {
            2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21,
            22, 23, 24, 25, 26, 27, 28, 30, 31, 33, 34, 36, 37, 38, 41, 42, 43, 44,
            48, 49, 50, 51, 52, 53, 54, 55, 62, 64, 68, 69, 70, 71, 72, 73, 74, 75,
            80, 82, 83, 84, 87, 88, 89, 91, 92, 93, 94, 95, 96, 97, 98, 99, 100, 101,
            111, 112, 113, 114, 115, 116, 117, 118, 119, 120, 125, 126, 127, 128, 129,
            130, 131, 132, 133, 134, 135, 136, 137, 138, 139, 140, 141, 142, 143, 145
        };

        private Model Pick(string[] names)
        {
            for (var tries = 0; tries < 12; tries++)
            {
                var model = new Model(names[_rng.Next(names.Length)]);

                if (model.IsValid && model.IsInCdImage && model.Request(1500)) return model;
            }

            return new Model(0);
        }

        /// <summary>Somebody to drive it. Anybody; they are in the car and not the point.</summary>
        private Model DriverModel()
        {
            var names = new[]
            {
                "g_m_y_famca_01", "g_m_y_famdnf_01", "g_m_y_famfor_01",
                "a_m_y_soucent_01", "a_m_y_soucent_02", "a_m_y_soucent_03",
                "a_m_m_soucent_01", "a_m_y_hipster_01"
            };

            for (var tries = 0; tries < 8; tries++)
            {
                var model = new Model(names[_rng.Next(names.Length)]);
                if (model.IsValid && model.IsInCdImage && model.Request(1200)) return model;
            }

            return new Model("a_m_y_soucent_01");
        }

        // ---- the blip -----------------------------------------------------------------------

        private void Blip()
        {
            try
            {
                if (_blip != null && _blip.Exists()) return;

                _blip = World.CreateBlip(_middle);
                if (_blip == null || !_blip.Exists()) return;

                // 226 is radar_race, which is the one with the flag on it.
                _blip.Sprite = (BlipSprite)226;
                _blip.Color = BlipColor.Green;
                _blip.Scale = 0.9f;
                _blip.IsShortRange = false;
                _blip.Name = "Car meet";
            }
            catch
            {
                _blip = null;
            }
        }

        private void Unblip()
        {
            try { if (_blip != null && _blip.Exists()) _blip.Delete(); }
            catch { /* it goes with the session */ }

            _blip = null;
        }
    }
}
