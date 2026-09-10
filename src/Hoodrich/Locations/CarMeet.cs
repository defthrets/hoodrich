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

        /// <summary>Where everybody ends up. Nothing parks here. See CarMeet.Crowd.</summary>
        public bool IsCrowd => string.Equals(Role, "crowd", StringComparison.OrdinalIgnoreCase);
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

        /// <summary>
        /// Near enough, and slow enough, to be squared up on the exact spot.
        ///
        /// A METRE, NOT FOUR AND A HALF. At four and a half the correction was a car visibly
        /// jumping the last stride into its bay; at one it is the difference between a car
        /// that parked well and a car that parked perfectly, and nobody can see it happen.
        ///
        /// It only ever runs on a car that has ALREADY STOPPED in its own space. There is no
        /// distance at which this fires on a moving car and no clock that makes it fire on a
        /// car that never arrived -- see Driving.
        /// </summary>
        private const float SeatWithin = 1.0f;
        private const float SeatSpeed = 0.6f;

        /// <summary>Nothing is done to a car more often than this.</summary>
        private const int TickMs = 400;

        /// <summary>One goes every few seconds, so they arrive in a trickle rather than a convoy.</summary>
        private const int SendEveryMs = 3500;

        /// <summary>How far the player has to be for the meet to keep itself alive.</summary>
        private const float ForgetAt = 700f;

        /// <summary>
        /// How they drive here.
        ///
        /// AVOID THINGS, STOP FOR NOTHING. 4|8|16|32 is go round cars, empty cars, people and
        /// objects, and that is the whole of it: no stop-before-vehicles, no stop-before-peds,
        /// no stopping at lights. The same set the takeover cars come in on, and for the same
        /// reason it was changed there -- a car obeying every light between here and half a
        /// mile out does not arrive, it queues, and what that looks like from the meet is
        /// eleven empty bays.
        ///
        /// They still go ROUND everything, which is the part that matters: this is a car in a
        /// hurry, not a car with its eyes shut.
        /// </summary>
        private const int RoadStyle = 4 | 8 | 16 | 32;
        private const float RoadSpeed = 24f;

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
            public int Hop;

            /// <summary>Out of the car and on his way to the crowd. See Crowd.</summary>
            public int OutAt;
            public bool Walked;

            /// <summary>Which knot he ended up in once it broke up. See Split.</summary>
            public int Group;
        }

        private readonly List<MeetSpot> _spots = new List<MeetSpot>();
        private readonly List<Runner> _out = new List<Runner>();
        private readonly Random _rng = new Random();

        private bool _on;
        private int _lastTick;
        private int _lastSend;
        private int _next;

        /// <summary>How many goes this space has had, and how many it gets. See Sending.</summary>
        private int _misses;
        private const int MostMisses = 4;
        private Vector3 _middle;

        /// <summary>Where they all end up, or null if the file does not say. See Crowd.</summary>
        private MeetSpot _crowd;
        private Blip _blip;

        /// <summary>Set by Main: the cars the set drives. See Takeover.</summary>
        public Func<string[]> Cars;

        /// <summary>Set by Main: who turns up to look, and what they do. See Takeover.</summary>
        public Func<string[]> Faces;
        public Func<string[]> Idles;

        /// <summary>
        /// The one on juice, and it is its OWN list rather than the takeover's.
        ///
        /// A VAN TURNED UP AND SAT THERE. The takeover's list is every Benny's body that
        /// nominally has hydraulics, which includes a Moonbeam and a Minivan -- and a van at
        /// the one spot in the meet whose entire job is to bounce is wrong twice over: it does
        /// not read as a lowrider, and on this install it did not hop.
        ///
        /// Six classics instead, which is a shorter list and the right one. This spot is not
        /// "a car with hydraulics", it is THE lowrider, and there is exactly one of it -- so
        /// the list only has to be deep enough that it is not the same car every meet.
        /// </summary>
        private static readonly string[] Hoppers =
        {
            "voodoo", "buccaneer2", "chino2", "faction2", "sabregt2", "tornado5"
        };

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

                    // THE CROWD SPOT IS NOT A SPACE AND NOTHING IS SENT TO IT. It is in the
                    // same file because it is part of the same place -- somebody laying a meet
                    // out wants the cars and the people in one list -- but a car driven to it
                    // would park in the middle of everybody.
                    if (spot.IsCrowd) { _crowd = spot; continue; }

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
            _lastFace = 0;
            _startedAt = Game.GameTime;
            _misses = 0;
            _split = false;
            _pickFrom = _rng.Next(64);

            Sweep(null);

            Blip();

            Log.Info("Car meet: on, " + _spots.Count + " space(s) to fill.");
            return null;
        }

        /// <summary>
        /// Time. Everybody unfrozen, handed back, and let go.
        ///
        /// NOT DELETED WHERE THEY STAND. Twelve cars vanishing out of a car park in front of
        /// somebody is worse than twelve cars being there too long. They are unfrozen, the
        /// handbrake comes off, the engine goes on and they are released to the game -- which
        /// means the population manager owns them again and clears them the way it clears
        /// anything else: once nobody is looking.
        ///
        /// The drivers are let go with them. They are sat in cars they own now, and a ped in a
        /// car the game owns is a car that drives away, which is exactly how a meet should end.
        /// </summary>
        private void Over()
        {
            Log.Info("Car meet: time. " + _out.Count + " car(s) heading off.");

            foreach (var r in _out)
            {
                try
                {
                    if (r.Car != null && r.Car.Exists())
                    {
                        Function.Call(Hash.FREEZE_ENTITY_POSITION, r.Car.Handle, false);
                        Function.Call(Hash.SET_VEHICLE_HANDBRAKE, r.Car.Handle, false);
                        Function.Call(Hash.SET_VEHICLE_ENGINE_ON, r.Car.Handle, true, true, true);
                        Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, r.Car.Handle, false, true);

                        r.Car.MarkAsNoLongerNeeded();
                    }

                    if (r.Driver != null && r.Driver.Exists())
                    {
                        Function.Call(Hash.SET_PED_KEEP_TASK, r.Driver.Handle, false);
                        Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, r.Driver.Handle, false);
                        Function.Call(Hash.CLEAR_PED_TASKS, r.Driver.Handle);
                        Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, r.Driver.Handle, false, true);

                        r.Driver.MarkAsNoLongerNeeded();
                    }
                }
                catch
                {
                    // It goes with the session either way.
                }
            }

            Loose();

            _out.Clear();
            _next = 0;
            _on = false;

            Unblip();
        }

        /// <summary>
        /// The crowd, handed back.
        ///
        /// Released rather than deleted, the same as the cars: fourteen people vanishing off a
        /// pavement in front of somebody is worse than fourteen people wandering off, and the
        /// population manager clears them the way it clears anybody else.
        /// </summary>
        private void Loose()
        {
            foreach (var w in _watching)
            {
                try
                {
                    if (w.Man == null || !w.Man.Exists()) continue;

                    Function.Call(Hash.SET_PED_KEEP_TASK, w.Man.Handle, false);
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, w.Man.Handle, false);
                    Function.Call(Hash.CLEAR_PED_TASKS, w.Man.Handle);
                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, w.Man.Handle, false, true);

                    w.Man.MarkAsNoLongerNeeded();
                }
                catch
                {
                    // They go with the session either way.
                }
            }

            _watching.Clear();
        }

        /// <summary>How long one lasts.</summary>
        private const int RunMs = 600000;

        /// <summary>When it was called. See Update.</summary>
        private int _startedAt;

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

            Loose();

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

                // TEN MINUTES AND THEY GO HOME. A meet with no end is a car park with twelve
                // cars welded into it for the rest of the session -- twelve vehicles and twelve
                // peds the game cannot have back, on a block that already runs a takeover and a
                // set of rollers out of the same pool.
                //
                // THE LAST CAR IN GETS ITS TIME. The clock starts when the meet is called, and
                // the twelfth car does not arrive until nearly a minute in, so the run is
                // measured from the start and the ending is not a hard cut -- see Over.
                if (now - _startedAt > RunMs) { Over(); return; }

                Sending(player, now);

                // AND THEN IT BREAKS UP. See Split -- one crowd for the first stretch, then
                // threes round the cars for the rest of it.
                Split(now);
                Watchers(player, now);

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

            // AND AGAIN, FOR THIS ONE. The spaces were emptied when the meet was called, but
            // a car sets off from half a mile away and traffic parks in things. Clearing the
            // one space a car is actually on its way to, at the moment it sets off, costs one
            // world query and is the difference between a meet and a shunt.
            Sweep(spot);

            var r = Send(player, spot, now);

            // A SPACE IS ONLY SPENT ON A CAR THAT ACTUALLY SET OFF. This moved on to the next
            // spot whether or not anything had been made for this one, so twelve failed sends
            // was twelve empty spaces and one car at the meet -- reported as exactly that. A
            // send can fail for a reason that will not be true in three seconds: no road found
            // out there, a model that would not stream, a full vehicle pool. So it keeps the
            // space and tries again, and only gives up on it after several goes.
            if (r == null)
            {
                _misses++;

                if (_misses < MostMisses) return;

                Log.Info("Car meet: gave up on one space after " + _misses + " tries.");
            }
            else
            {
                _out.Add(r);
            }

            _misses = 0;
            _next++;
        }

        /// <summary>
        /// The spaces, emptied.
        ///
        /// TRAFFIC PARKS IN CAR PARKS. Twelve surveyed spaces on a public street are twelve
        /// places the game will have put a parked Asea by the time anybody calls a meet, and
        /// a meet car driving into one either shunts it out of the way or gives up trying and
        /// sits in the road with its indicator on. Neither reads as a car meet.
        ///
        /// NOT YOURS, THOUGH, AND THAT IS THE WHOLE CARE IN HERE. A system that deletes cars
        /// in an area is one keystroke away from deleting the car somebody spent an hour
        /// building and parked outside their own house. What is spared: whatever the player is
        /// sat in, whatever they were last sat in, and anything the ledger says they own. What
        /// goes is ambient traffic, which the game made and will make again.
        ///
        /// Passed a spot it does one; passed nothing it does the lot.
        /// </summary>
        private void Sweep(MeetSpot only)
        {
            foreach (var spot in _spots)
            {
                if (only != null && only != spot) continue;

                try
                {
                    foreach (var car in World.GetNearbyVehicles(spot.At, ClearRadius))
                    {
                        if (car == null || !car.Exists()) continue;
                        if (Yours(car)) continue;

                        car.MarkAsNoLongerNeeded();
                        car.Delete();
                    }
                }
                catch
                {
                    // A space that will not clear is a space a car parks badly in.
                }
            }
        }

        /// <summary>
        /// Whether that car is one this mod has no business deleting.
        ///
        /// Three questions, cheapest first, and the ledger last because it is the only one
        /// that walks a list.
        /// </summary>
        private bool Yours(Vehicle car)
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return false;

                if (me.IsInVehicle() && me.CurrentVehicle == car) return true;

                var last = me.LastVehicle;
                if (last != null && last.Exists() && last == car) return true;

                return Owned != null && Owned(car);
            }
            catch
            {
                // If it cannot be established that a car is disposable, it is not.
                return true;
            }
        }

        /// <summary>How much of a space is cleared. A car and a bit either side of it.</summary>
        private const float ClearRadius = 3.6f;

        /// <summary>Set by Main: whether the player owns that car. See Yours.</summary>
        public Func<Vehicle, bool> Owned;

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
                var names = spot.IsHopper ? Hoppers : (Cars == null ? null : Cars());

                if (names == null || names.Length == 0) return null;

                var model = Pick(names);
                if (!model.IsValid) return null;

                var far = ComeFromMin + (float)_rng.NextDouble() * (ComeFromMax - ComeFromMin);
                var way = (float)(_rng.NextDouble() * Math.PI * 2.0);

                var probe = _middle + new Vector3((float)Math.Cos(way) * far,
                                                  (float)Math.Sin(way) * far, 0f);

                // THE MANAGED ONE, AND THIS IS WHY.
                //
                // This called GET_NTH_CLOSEST_VEHICLE_NODE_WITH_HEADING directly and it hard
                // crashed the game the instant the first car set off. That native takes TEN
                // arguments and the seventh is an int* the game writes a lane count into; nine
                // were passed, with a literal nought where the pointer belongs. So the engine
                // took nought as an address and wrote to it, which is not an exception a script
                // can catch -- it is the process going down. The log's last line was "Car meet:
                // on", which is exactly one line before this.
                //
                // GetNextPositionOnStreet is the wrapper for the same job with the argument
                // list already correct. There is no version of this worth hand-rolling.
                // UNOCCUPIED FIRST, ANY ROAD SECOND. The unoccupied flag asks for a piece of
                // road with nothing already on it, which is the right thing to want and is also
                // a question a busy city answers with nothing rather often. Asked that way
                // first because a car spawned on top of another car is a crash somebody sees;
                // asked again without it, because no road at all is a space that stays empty.
                var at = World.GetNextPositionOnStreet(probe, true);

                if (at == Vector3.Zero) at = World.GetNextPositionOnStreet(probe, false);

                if (at == Vector3.Zero)
                {
                    model.MarkAsNoLongerNeeded();
                    Log.Debug("Car meet: no road out at " + probe.X.ToString("0") + ", " +
                              probe.Y.ToString("0") + ".");
                    return null;
                }

                // POINTED AT WHERE IT IS GOING. The wrapper hands back a place on a road and
                // not which way the road runs, and a car facing across one does a three-point
                // turn before it sets off. Facing the meet is right often enough, and the drive
                // task turns it round where it is not.
                var toward = _middle - at;
                var face = (float)(Math.Atan2(toward.Y, toward.X) * 180.0 / Math.PI) - 90f;

                var car = World.CreateVehicle(model, at, face);
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

            if (r.Seated) { Hopping(r, now); Crowd(r, now); return; }

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

            // NEAR ENOUGH AND STOPPED, AND NOTHING ELSE.
            //
            // THERE WAS A GIVE-UP HERE AND IT WAS THE THING THAT LOOKED LIKE TELEPORTING. Past
            // twenty-two seconds of parking, or two and a half minutes of driving, the car was
            // simply put on its space from wherever it had got to -- which on a bad run is
            // most of the way down the street, in front of somebody stood watching them
            // arrive. The whole point of this feature is the arriving.
            //
            // So there is no clock on it any more. A car that cannot reach its space drives
            // around trying to, for as long as the meet lasts, and if it never gets there then
            // that bay stays empty -- which is a car that could not find a parking spot, and
            // is a thing that happens.
            //
            // What is left is not a teleport. It is under a metre, done while the car is
            // already stopped in its bay, and it is what squares eleven cars into a row.
            var slow = false;
            try { slow = r.Car.Speed < SeatSpeed; } catch { slow = true; }

            if (gap < SeatWithin && slow) Seat(r);
        }

        /// <summary>
        /// The car, on its spot, exactly.
        ///
        /// NO OFFSET. SET_ENTITY_COORDS applies the game's own lift and a car put down with it
        /// stands a foot in the air and then drops, which on twelve cars in a row is twelve
        /// visible thumps. The spot's Z came off a car that was parked in it, so it is already
        /// the right height.
        /// </summary>
        private void Seat(Runner r)
        {
            r.Seated = true;

            try
            {
                Function.Call(Hash.CLEAR_PED_TASKS, r.Driver.Handle);

                Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, r.Car.Handle,
                              r.Spot.At.X, r.Spot.At.Y, r.Spot.At.Z, false, false, false);

                Function.Call(Hash.SET_ENTITY_HEADING, r.Car.Handle, Facing(r));
                Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, r.Car.Handle);

                r.Car.Velocity = Vector3.Zero;

                Function.Call(Hash.SET_VEHICLE_HANDBRAKE, r.Car.Handle, true);

                // BONNET UP, WHICH IS THE ONLY REASON ANYBODY PARKS LIKE THIS. A row of closed
                // cars is a car park; a row with the lids up is people showing each other
                // things. Door four is the bonnet, opened loose so it sits rather than swings,
                // and instantly because it happens while the car is still settling and nobody
                // watches a bonnet rise on a car that has not stopped moving.
                //
                // Not the one on juice: its whole show is underneath it.
                if (!r.Spot.IsHopper)
                {
                    Function.Call(Hash.SET_VEHICLE_DOOR_OPEN, r.Car.Handle, Bonnet, true, true);
                }
                Function.Call(Hash.SET_VEHICLE_ENGINE_ON, r.Car.Handle, r.Spot.IsHopper, true, true);
                Function.Call(Hash.SET_VEHICLE_LIGHTS, r.Car.Handle, r.Spot.IsHopper ? 2 : 0);

                // He sits in it until there is somewhere for him to be. The peds who get out
                // and stand about are the next piece of this and are not written yet.
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, r.Driver.Handle, true);

                // AND IT DOES NOT GO ANYWHERE. THIS IS WHY THE FIRST ONE DROVE OFF.
                //
                // A driver whose tasks have just been cleared is a driver the game is free to
                // hand a new one to, and the one it hands somebody sat behind a wheel is drive
                // away. KEEP_TASK holds the nothing he was given -- but that on its own is a
                // promise about tasks, not about the car, and a parked show car being shunted
                // out of a row by traffic is the same problem wearing a different hat.
                //
                // FROZEN, WHICH FOR A PARKED CAR IS WHAT PARKED MEANS. It is square in a bay
                // and it should still be square in that bay in ten minutes. The cost is that
                // nobody can nudge it, and at a car meet that is not a cost.
                Function.Call(Hash.SET_PED_KEEP_TASK, r.Driver.Handle, true);

                // EXCEPT THE ONE ON JUICE. Freezing is what keeps eleven parked cars square in
                // their bays for ten minutes, and it is also, precisely, a car that cannot
                // move -- so the hydraulics were being asked to bounce something nailed to the
                // floor. It came, it was green, it had green neons and it sat there.
                //
                // It does not need freezing anyway: it is off on its own with nothing to be
                // shunted into and nothing to shunt.
                if (!r.Spot.IsHopper) Function.Call(Hash.FREEZE_ENTITY_POSITION, r.Car.Handle, true);
            }
            catch
            {
                // It is where it is.
            }

            // AND HE GETS OUT IN A MINUTE. Staggered off the arrival rather than all at
            // once: twelve doors opening on the same frame is a cutscene, and twelve men
            // wandering over one at a time as they pull in is a car meet filling up.
            r.OutAt = Game.GameTime + OutAfterMs + _rng.Next(OutVaryMs);

        }

        /// <summary>
        /// Which way round it ends up sitting.
        ///
        /// EITHER WAY DOWN THE BAY, whichever it is already nearest. The spot's heading came
        /// off a car parked in it, so it is one of the two ways a car can sit there -- and a
        /// car that has just driven in nose first was being spun a hundred and eighty degrees
        /// on the spot to match it. That is the teleport: it drove in correctly and then
        /// turned round without moving.
        ///
        /// A bay does not care which way you point. What it cares about is being square in
        /// it, which is the part the snap is actually for. So the line is kept and the
        /// direction along it is whichever the car already had -- reverse in and it stays
        /// reversed, drive in and it stays nose first, and neither is ever spun.
        /// </summary>
        private static float Facing(Runner r)
        {
            var want = r.Spot.Heading;

            try
            {
                var has = r.Car.Heading;

                var off = Math.Abs(((want - has) % 360f + 540f) % 360f - 180f);

                // More than a right angle away from the surveyed line means the other end of
                // the same line is the near one.
                if (off > 90f) return (want + 180f) % 360f;
            }
            catch
            {
                // Then the surveyed one, which is at least square in the bay.
            }

            return want;
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

            r.HopAt = now + HopMinMs + _rng.Next(HopVaryMs);

            // MORE THAN UP AND DOWN. Two states alternating is a car doing press-ups; somebody
            // actually on the switches works one corner, then the front, then drops the lot,
            // and the pattern is the point of the whole car. Walked in order rather than
            // picked at random, for the same reason the colours are -- random puts the same
            // state twice in a row about as often as not, and twice in a row is a car that
            // stopped.
            r.Hop = (r.Hop + 1) % Hops.Length;

            try
            {
                Function.Call(Hash.SET_HYDRAULIC_VEHICLE_STATE, r.Car.Handle, Hops[r.Hop]);
            }
            catch
            {
                // Some builds have no juice. It is still a green lowrider with green neons.
            }
        }

        private const int HopMinMs = 650;
        private const int HopVaryMs = 800;

        /// <summary>
        /// The hydraulic poses, in the order they are worked through.
        ///
        /// Down, up, front, back, and the two sides -- the whole switch box rather than the two
        /// ends of it. Nought comes round often so it keeps landing rather than hanging in the
        /// air, which is what a lowrider does between moves.
        /// </summary>
        private static readonly int[] Hops = { 0, 2, 0, 3, 4, 0, 5, 6, 0, 2 };

        // ---- the people who came to look -------------------------------------------------------

        /// <summary>Somebody who did not drive here.</summary>
        private sealed class Watcher
        {
            public Ped Man;
            public int MoveAt;
            public int Seen;
        }

        private readonly List<Watcher> _watching = new List<Watcher>();

        /// <summary>
        /// A crowd that came on foot, going from bonnet to bonnet.
        ///
        /// A CAR MEET WITH ONLY DRIVERS AT IT IS TWELVE MEN AND TWELVE CARS. Most of the
        /// people at one did not bring anything -- they came to look, they walk the row, they
        /// stop at whatever has something worth stopping at, and they move on. That wandering
        /// is the difference between a car park with people in it and a car meet.
        ///
        /// THEY MOVE, WHICH IS THE ENTIRE POINT. A ring of spectators standing still is
        /// scenery; the same people drifting from one nose to the next every half minute is a
        /// place with something going on in it. Nobody has a route -- each one picks a car,
        /// stands at it for a while, and picks another, so the pattern is never the same twice
        /// and never repeats.
        ///
        /// WHO AND WHAT ARE THE TAKEOVER'S OWN LISTS. Faces is Chamberlain Hills rather than
        /// the game's ambient population -- a coach party of hipsters and tourists on a block
        /// none of them live on was a mistake made once already. Watching is the idles that do
        /// NOT lean: half the standing idles in this game are authored for a ped up against a
        /// wall, and there is no wall in a car park. Both cost a night to get right and neither
        /// gets typed twice.
        /// </summary>
        private void Watchers(Ped player, int now)
        {
            if (_crowd == null) return;

            // A few at a time rather than all at once, same as the cars.
            if (_watching.Count < HowMany && now - _lastFace > FaceEveryMs)
            {
                _lastFace = now;
                Make();
            }

            for (var i = _watching.Count - 1; i >= 0; i--)
            {
                var w = _watching[i];

                if (w.Man == null || !w.Man.Exists() || !w.Man.IsAlive)
                {
                    _watching.RemoveAt(i);
                    continue;
                }

                if (now < w.MoveAt) continue;

                Wander(w, now);
            }
        }

        /// <summary>One more of them, put down out of the way and left to walk in.</summary>
        private void Make()
        {
            try
            {
                var names = Faces == null ? null : Faces();
                if (names == null || names.Length == 0) return;

                Model model = new Model(0);

                for (var tries = 0; tries < 8; tries++)
                {
                    var one = new Model(names[_rng.Next(names.Length)]);

                    if (one.IsValid && one.IsInCdImage && one.Request(1200)) { model = one; break; }
                }

                if (!model.IsValid) return;

                // ON THE PAVEMENT AND NOT IN THE ROW. Put down a little way off and left to
                // walk in on his own, so the first thing anybody sees him do is arrive.
                var way = (float)(_rng.NextDouble() * Math.PI * 2.0);

                var probe = _crowd.At + new Vector3((float)Math.Cos(way) * ComeInFrom,
                                                    (float)Math.Sin(way) * ComeInFrom, 0f);

                var at = World.GetNextPositionOnSidewalk(probe);
                if (at == Vector3.Zero) at = probe;

                var man = World.CreatePed(model, at);
                model.MarkAsNoLongerNeeded();

                if (man == null || !man.Exists()) return;

                man.IsPersistent = true;

                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, man.Handle, true, true);
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, man.Handle, true);
                Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, man.Handle, false);
                Function.Call(Hash.SET_PED_KEEP_TASK, man.Handle, true);

                var w = new Watcher { Man = man };

                _watching.Add(w);

                Wander(w, Game.GameTime);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not bring somebody to the meet: " + ex.Message);
            }
        }

        /// <summary>
        /// Off to the next bonnet.
        ///
        /// A CAR HE IS NOT ALREADY AT, picked fresh each time. The nose rather than the side,
        /// because the nose is where the lid is up and is the only part of a parked car worth
        /// walking over to -- and a little way off it and to one side, so two people who pick
        /// the same car are not stood in each other.
        ///
        /// How long he stays is not the same twice. Everybody moving on the same clock is a
        /// shift change.
        /// </summary>
        private void Wander(Watcher w, int now)
        {
            w.MoveAt = now + StayMinMs + _rng.Next(StayVaryMs);

            try
            {
                var cars = new List<Runner>();
                foreach (var r in _out) if (r.Seated) cars.Add(r);

                Vector3 stand;
                float face;

                if (cars.Count == 0)
                {
                    // Nothing parked yet, so he waits about where everybody ends up.
                    var round = (float)(_rng.NextDouble() * Math.PI * 2.0);

                    stand = _crowd.At + new Vector3((float)Math.Cos(round) * MillAbout,
                                                    (float)Math.Sin(round) * MillAbout, 0f);

                    var toCrowd = _crowd.At - stand;
                    face = (float)(Math.Atan2(toCrowd.Y, toCrowd.X) * 180.0 / Math.PI) - 90f;
                }
                else
                {
                    var pick = cars[(w.Seen + _rng.Next(1, cars.Count + 1)) % cars.Count];
                    w.Seen = (w.Seen + 1) % Math.Max(1, cars.Count);

                    var car = pick.Car;

                    var off = ((float)_rng.NextDouble() - 0.5f) * 2f * Spread;

                    stand = car.Position + car.ForwardVector * (NoseGap + (float)_rng.NextDouble())
                          + car.RightVector * off;

                    var toCar = car.Position - stand;
                    face = (float)(Math.Atan2(toCar.Y, toCar.X) * 180.0 / Math.PI) - 90f;
                }

                Function.Call(Hash.SET_PED_KEEP_TASK, w.Man.Handle, false);
                Function.Call(Hash.CLEAR_PED_TASKS, w.Man.Handle);

                Function.Call(Hash.TASK_GO_STRAIGHT_TO_COORD, w.Man.Handle,
                              stand.X, stand.Y, stand.Z, WalkPace, -1, face, 0.4f);

                var doing = Doing();

                Function.Call(Hash.TASK_START_SCENARIO_AT_POSITION, w.Man.Handle, doing,
                              stand.X, stand.Y, stand.Z, face, 0, true, true);

                Function.Call(Hash.SET_PED_KEEP_TASK, w.Man.Handle, true);
            }
            catch
            {
                // He stays where he is and tries again in half a minute.
            }
        }

        /// <summary>
        /// Something to be doing. The takeover's list, plus the cameras.
        ///
        /// PAPARAZZI IS THE ONE THAT MATTERS HERE and it is not on that list, because nobody
        /// stands at a junction with a camera up. At a car meet everybody has one out, so it
        /// goes in heavily -- and TOURIST_MAP does not, because a man reading a map at a car
        /// meet is a man who is lost.
        /// </summary>
        private string Doing()
        {
            if (_rng.Next(100) < CameraChance) return "WORLD_HUMAN_PAPARAZZI";

            var list = Idles == null ? null : Idles();

            if (list == null || list.Length == 0) return "WORLD_HUMAN_STAND_IMPATIENT_UPRIGHT";

            return list[_rng.Next(list.Length)];
        }

        /// <summary>How many turn up, how fast, and how far out they start.</summary>
        private const int HowMany = 14;
        private const int FaceEveryMs = 2600;
        private const float ComeInFrom = 26f;

        /// <summary>How long one stays at a bonnet, and how far off it he stands.</summary>
        private const int StayMinMs = 22000;
        private const int StayVaryMs = 26000;
        private const float Spread = 1.6f;
        private const float MillAbout = 3.2f;

        /// <summary>How many of them have a camera out at any one time.</summary>
        private const int CameraChance = 30;

        private int _lastFace;

        // ---- everybody stood about -----------------------------------------------------------

        /// <summary>
        /// He gets out, walks over, and stands in it.
        ///
        /// THE CROWD IS ONE COORDINATE AND THEY ARRANGE THEMSELVES ROUND IT. Twelve marked
        /// standing spots would be twelve more numbers to survey and would put everybody on a
        /// grid; a ring worked out from one point puts them in a huddle facing inwards, which
        /// is what a group of people talking is. Where in the ring is decided by which car he
        /// drove, so two men never walk to the same patch of ground.
        ///
        /// FACING THE MIDDLE, which is the whole of making it read as a conversation. Nothing
        /// here plays a talking animation at anybody in particular: a scenario in place, faced
        /// inward, at a sensible distance, is what the game itself uses for a group stood
        /// round outside a shop, and it holds up from the distance anybody watches this from.
        ///
        /// NOT THE ONE ON JUICE. Somebody has to be working the switches.
        /// </summary>
        private void Crowd(Runner r, int now)
        {
            if (_crowd == null) return;
            if (r.Walked || r.Spot.IsHopper) return;
            if (r.OutAt == 0 || now < r.OutAt) return;
            if (r.Driver == null || !r.Driver.Exists() || !r.Driver.IsAlive) { r.Walked = true; return; }

            r.Walked = true;

            try
            {
                var which = _out.IndexOf(r);
                if (which < 0) which = 0;

                var many = Math.Max(1, _spots.Count - 1);
                var round = (float)(which * Math.PI * 2.0 / many);

                var stand = _crowd.At + new Vector3((float)Math.Cos(round) * CrowdRing,
                                                    (float)Math.Sin(round) * CrowdRing, 0f);

                // Facing the middle of it, which is what everybody in a huddle is doing.
                var toward = _crowd.At - stand;
                var face = (float)(Math.Atan2(toward.Y, toward.X) * 180.0 / Math.PI) - 90f;

                Function.Call(Hash.SET_PED_KEEP_TASK, r.Driver.Handle, false);
                Function.Call(Hash.CLEAR_PED_TASKS, r.Driver.Handle);

                // OUT, THEN OVER, THEN STOOD. TASK_LEAVE_VEHICLE opens the door and steps him
                // out properly; a warp out of a parked car is a man appearing beside it.
                Function.Call(Hash.TASK_LEAVE_VEHICLE, r.Driver.Handle, r.Car.Handle, 0);

                Function.Call(Hash.TASK_GO_STRAIGHT_TO_COORD, r.Driver.Handle,
                              stand.X, stand.Y, stand.Z, WalkPace, -1, face, 0.4f);

                var doing = Standing[which % Standing.Length];

                Function.Call(Hash.TASK_START_SCENARIO_AT_POSITION, r.Driver.Handle, doing,
                              stand.X, stand.Y, stand.Z, face, 0, true, true);

                Function.Call(Hash.SET_PED_KEEP_TASK, r.Driver.Handle, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not get a driver out: " + ex.Message);
            }
        }

        /// <summary>
        /// The crowd breaks into threes and goes to look at the cars.
        ///
        /// EVERYBODY IN ONE HUDDLE IS THE FIRST TEN MINUTES OF A MEET AND NOT THE WHOLE OF IT.
        /// People arrive, they say hello in one lump, and then it thins out into knots of
        /// three or four stood round whatever somebody has the lid up on. That second half is
        /// most of what a car meet actually looks like, and a ring of twelve that never moves
        /// is a photograph of the first two minutes.
        ///
        /// THREES, AND A CAR EACH RATHER THAN A CAR BETWEEN THEM ALL. The group picks one car
        /// and stands across its nose, which is where you stand when the bonnet is up -- so
        /// the bonnet being up is not decoration, it is the reason there is somewhere to
        /// stand.
        ///
        /// ONCE. This runs on a clock and sets a flag; a meet does not re-shuffle itself every
        /// few seconds, and people who have found a car to look at stay at it.
        /// </summary>
        private void Split(int now)
        {
            if (_split) return;
            if (now - _startedAt < MingleMs) return;

            _split = true;

            // Only the ones who actually made it out and over. Anybody still driving, or the
            // one working the switches, is left where he is.
            var them = new List<Runner>();
            foreach (var r in _out) if (r.Walked && !r.Spot.IsHopper) them.Add(r);

            if (them.Count == 0) return;

            // Shuffled, so the threes are not "whoever parked next to each other" -- which
            // would put the same men together every meet and in the order they arrived.
            for (var i = them.Count - 1; i > 0; i--)
            {
                var j = _rng.Next(i + 1);
                var swap = them[i]; them[i] = them[j]; them[j] = swap;
            }

            var cars = new List<Runner>();
            foreach (var r in _out) if (r.Seated && !r.Spot.IsHopper) cars.Add(r);

            if (cars.Count == 0) return;

            var group = 0;

            for (var i = 0; i < them.Count; i += Threes)
            {
                // A DIFFERENT CAR EACH, walked round the list rather than drawn from it, so
                // four groups never all pick the same one. The offset is random so it is not
                // the same car every meet either.
                var pick = cars[(group + _pickFrom) % cars.Count];

                for (var n = 0; n < Threes && i + n < them.Count; n++)
                {
                    Round(them[i + n], pick, n);
                }

                group++;
            }

            Log.Info("Car meet: broke into " + group + " group(s) round the cars.");
        }

        /// <summary>
        /// One man, stood at the front of one car.
        ///
        /// ACROSS THE NOSE, NOT ROUND THE WHOLE THING. Three abreast a hand's width apart,
        /// facing back at the engine, which is the shape people actually make when there is
        /// something to look at under a lid. Standing round the car would be standing round a
        /// car, and there is nothing to see from the back of one.
        /// </summary>
        private void Round(Runner who, Runner car, int place)
        {
            if (who.Driver == null || !who.Driver.Exists() || !who.Driver.IsAlive) return;
            if (car.Car == null || !car.Car.Exists()) return;

            try
            {
                var nose = car.Car.Position + car.Car.ForwardVector * NoseGap;
                var side = car.Car.RightVector * ((place - 1) * Abreast);

                var stand = nose + side;

                var toward = car.Car.Position - stand;
                var face = (float)(Math.Atan2(toward.Y, toward.X) * 180.0 / Math.PI) - 90f;

                Function.Call(Hash.SET_PED_KEEP_TASK, who.Driver.Handle, false);
                Function.Call(Hash.CLEAR_PED_TASKS, who.Driver.Handle);

                Function.Call(Hash.TASK_GO_STRAIGHT_TO_COORD, who.Driver.Handle,
                              stand.X, stand.Y, stand.Z, WalkPace, -1, face, 0.3f);

                var doing = Looking[(place + who.Group) % Looking.Length];

                Function.Call(Hash.TASK_START_SCENARIO_AT_POSITION, who.Driver.Handle, doing,
                              stand.X, stand.Y, stand.Z, face, 0, true, true);

                Function.Call(Hash.SET_PED_KEEP_TASK, who.Driver.Handle, true);

                who.Group++;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not stand somebody at a car: " + ex.Message);
            }
        }

        /// <summary>
        /// What they do stood at the nose of a car. Every name off the machine's own list.
        ///
        /// WORLD_HUMAN_VEHICLE_MECHANIC is the one scenario in the game of somebody leaning
        /// INTO an engine bay, and one man doing that with two beside him talking is the exact
        /// picture this is after. Checked against the machine's own list, like the rest.
        /// </summary>
        private static readonly string[] Looking =
        {
            "WORLD_HUMAN_HANG_OUT_STREET", "WORLD_HUMAN_VEHICLE_MECHANIC",
            "WORLD_HUMAN_STAND_MOBILE", "WORLD_HUMAN_SMOKING",
            "WORLD_HUMAN_HANG_OUT_STREET", "WORLD_HUMAN_STAND_IMPATIENT"
        };

        /// <summary>How long everybody stays in one crowd before it thins out.</summary>
        private const int MingleMs = 170000;

        /// <summary>How many to a group, how far off the nose they stand, and how far apart.</summary>
        private const int Threes = 3;
        private const float NoseGap = 2.3f;
        private const float Abreast = 0.85f;

        /// <summary>Whether it has already broken up, and where the car-picking starts.</summary>
        private bool _split;
        private int _pickFrom;

        /// <summary>
        /// What they do while they are stood there.
        ///
        /// Every one of these is in the game's own scenario list on this machine. Mostly
        /// talking, because that is what the group is -- with a couple of smokers and somebody
        /// on their phone, because a dozen people all doing the identical thing is a chorus
        /// line.
        /// </summary>
        private static readonly string[] Standing =
        {
            "WORLD_HUMAN_STAND_MOBILE", "WORLD_HUMAN_HANG_OUT_STREET",
            "WORLD_HUMAN_SMOKING", "WORLD_HUMAN_STAND_IMPATIENT",
            "WORLD_HUMAN_HANG_OUT_STREET", "WORLD_HUMAN_DRINKING",
            "WORLD_HUMAN_STAND_MOBILE", "WORLD_HUMAN_HANG_OUT_STREET"
        };

        /// <summary>How wide the huddle is, and how long after parking he gets out.</summary>
        private const float CrowdRing = 2.1f;
        private const int OutAfterMs = 4000;
        private const int OutVaryMs = 5000;
        private const float WalkPace = 1.2f;

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

                // NOT ON THE ONE WITH HYDRAULICS. On a Benny's lowrider the suspension slot
                // IS the hydraulics, so fitting the lowest thing in it takes the juice off the
                // car -- which is the second reason it did not bounce, and the one that would
                // have survived unfreezing it.
                if (!spot.IsHopper)
                {
                    var many = Function.Call<int>(Hash.GET_NUM_VEHICLE_MODS, h, Suspension);
                    if (many > 0) Function.Call(Hash.SET_VEHICLE_MOD, h, Suspension, many - 1, false);
                }

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

        /// <summary>Door four is the bonnet. Nothing else in this file opens a door.</summary>
        private const int Bonnet = 4;

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

        /// <summary>
        /// A car nobody at this meet is already in.
        ///
        /// FOUR OF THE SAME CAR IS NOT A CAR MEET. This picked at random out of forty-odd
        /// names, and random over twelve draws gives a repeat far more often than people
        /// expect -- four the same in a row of twelve is an ordinary outcome of it, and it was
        /// exactly the outcome. Nobody turns up to a meet to look at four of the same Camry.
        ///
        /// So what is already parked is walked past. The list is deep enough that this never
        /// runs out -- forty names against twelve spaces -- but if it ever did, a repeat beats
        /// an empty bay, and the last few tries stop caring.
        /// </summary>
        private Model Pick(string[] names)
        {
            for (var tries = 0; tries < 24; tries++)
            {
                var name = names[_rng.Next(names.Length)];

                // The last few goes take anything, so a short list cannot leave a space empty.
                if (tries < 18 && Already(name)) continue;

                var model = new Model(name);

                if (model.IsValid && model.IsInCdImage && model.Request(1500)) return model;
            }

            return new Model(0);
        }

        /// <summary>Whether one of these is already at the meet, or on its way to it.</summary>
        private bool Already(string name)
        {
            var hash = Function.Call<int>(Hash.GET_HASH_KEY, name);

            foreach (var r in _out)
            {
                try
                {
                    if (r.Car != null && r.Car.Exists() && r.Car.Model.Hash == hash) return true;
                }
                catch
                {
                    // A car that cannot be asked is not a match.
                }
            }

            return false;
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
