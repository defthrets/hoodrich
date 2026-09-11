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

        /// <summary>Where everybody ends up. Nothing parks here. See CarMeet.Plan.</summary>
        public bool IsCrowd => string.Equals(Role, "crowd", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// THE CAR MEET.
    ///
    /// A dozen cars turn up on Carson Ave of an evening, park in a row with the lids up, and
    /// the people who came in them and the people who walked over stand about the cars for
    /// ten minutes and then go home. Every decision below is about the words TURN UP, STAND
    /// ABOUT and GO HOME, because a meet is those three things and a car park is none of them.
    ///
    /// THEY ARE NOT PLACED, THEY ARRIVE. Each car is put on a road a few hundred metres out,
    /// given a driver -- and sometimes a passenger -- and told to drive here. At the mouth of
    /// the lot it slows right down and starts stopping for things, because the last fifty
    /// metres are a car park with eleven other cars trying to get into it, and it parks with
    /// the game's own parking task. The last half-metre is a snap to the surveyed bay, done
    /// while the car is already stopped in roughly the right place, which is invisible and is
    /// the difference between parked and nearly parked.
    ///
    /// NOBODY IS EVER WARPED AND NOBODY EVER STANDS DOING NOTHING. This is the part that was
    /// wrong. Every "walk over" used to be a go-to task followed on the same frame by a
    /// scenario task with its teleport flag set -- so the go-to was cancelled before it
    /// started, the man was warped to the coordinate, and because the coordinate was a car's
    /// centre height he was warped into the ground to the waist. On a driver still sat in his
    /// car the same call played a standing scenario on a seated man, which is the "weird
    /// motions", and he never got out. Now every move is ONE SEQUENCE: walk there on the nav
    /// mesh, then start the scenario at the spot with no teleport and no end. The man walks,
    /// arrives, and does the thing; and a tick watches every one of them and gives anybody
    /// found stood with no task somewhere else to be.
    ///
    /// WHAT THEY DO is chosen by where they are. At a nose with the lid up: lean into the
    /// bay, talk, film it, stand and look. Down a side: photographs, filming, a smoke, a
    /// crouch at the wheel. In the huddle: talk, drink, smoke, phone. Round the lowrider:
    /// film it and whoop. Nobody stays anywhere longer than a minute and nobody goes to the
    /// same place twice running, so the shape of it changes the whole time it is on.
    ///
    /// AND THEN THEY GO HOME. At time the passengers get back in, the drivers follow, the
    /// lids come down and the cars pull out one by one and drive off into traffic; the people
    /// who walked in walk back out the way they came. Nothing vanishes in front of anybody.
    ///
    /// Every scenario name in this file is in RampageFiles\Lists\Scenarios.txt on this
    /// machine. None of them is guessed.
    /// </summary>
    internal sealed class CarMeet
    {
        // ======================================================================
        // Measures
        // ======================================================================

        /// <summary>How far out they are put on the road.</summary>
        private const float ComeFromMin = 220f;
        private const float ComeFromMax = 480f;

        /// <summary>
        /// The mouth of the lot: inside this they drive like people in a car park.
        ///
        /// THIS IS THE CRASH FIX. Out on the road they come in at speed and stop for nothing,
        /// which is right for a car that has to actually arrive. Inside fifty-five metres
        /// there are eleven other cars, twenty people on foot and one car park entrance, and
        /// a car doing twenty-four metres a second through that hits something. So the drive
        /// is re-issued at walking-pace-for-a-car with STOP for vehicles and STOP for people
        /// switched on: a car that finds another one parking in front of it waits, which is
        /// what a queue at a meet looks like.
        /// </summary>
        private const float LotFrom = 55f;
        private const float LotSpeed = 7f;

        /// <summary>Near enough to the bay to stop driving and start parking.</summary>
        private const float ParkFrom = 26f;

        /// <summary>
        /// Near enough, and slow enough, to be squared up on the exact spot.
        ///
        /// A METRE, NOT FOUR AND A HALF. At four and a half the correction was a car visibly
        /// jumping the last stride into its bay; at one it is the difference between a car
        /// that parked well and a car that parked perfectly, and nobody can see it happen.
        /// </summary>
        private const float SeatWithin = 1.0f;
        private const float SeatSpeed = 0.6f;

        /// <summary>
        /// The patience on a park that is not quite landing.
        ///
        /// The game's parking task gets a car near and no nearer, and now and then "near" is
        /// three metres off and stopped, at which point the metre above never fires and the
        /// car sits crooked across two bays with its driver in it for the rest of the meet --
        /// which is a driver who never got out. So a car that has been parking this long and
        /// is within a few metres is squared up from there: a bigger snap, seen once in a
        /// while, against a car stuck for ten minutes. Further off than that and it has given
        /// up somewhere daft, and it is asked to park again.
        /// </summary>
        private const int ParkPatienceMs = 20000;
        private const float SeatLoose = 4f;
        private const int ParkRetryMs = 32000;

        /// <summary>Nothing is done to anybody more often than this.</summary>
        private const int TickMs = 400;

        /// <summary>
        /// One goes every few seconds, so they arrive in a trickle rather than a convoy --
        /// and NOT while the last one is still in the lot and not yet parked, up to a limit.
        /// Two cars parking side by side at once is the other way they crash.
        /// </summary>
        private const int SendEveryMs = 6500;
        private const int LotHoldMs = 16000;

        /// <summary>How far the player has to be for the meet to keep itself alive.</summary>
        private const float ForgetAt = 700f;

        /// <summary>
        /// How they drive here, out on the road.
        ///
        /// AVOID THINGS, STOP FOR NOTHING. 4|8|16|32 is go round cars, empty cars, people and
        /// objects, and no stopping at lights. A car obeying every light between here and
        /// half a mile out does not arrive, it queues, and what that looks like from the meet
        /// is eleven empty bays. They still go ROUND everything.
        /// </summary>
        private const int RoadStyle = 4 | 8 | 16 | 32;
        private const float RoadSpeed = 24f;

        /// <summary>
        /// How they drive inside the lot: stop for vehicles (1), stop for people (2), steer
        /// round parked cars (8), people (16) and objects (32). No swerving -- a swerve in a
        /// car park is a shunt.
        /// </summary>
        private const int LotStyle = 1 | 2 | 8 | 16 | 32;

        /// <summary>How they leave: the game's ordinary careful traffic style.</summary>
        private const int HomeStyle = 786603;
        private const float HomeSpeed = 14f;

        /// <summary>How many of the twelve get neons, and how many bring somebody.</summary>
        private const int NeonChance = 70;
        private const int PassengerChance = 45;

        /// <summary>How long one lasts, and how long the going-home is given before it is cut.</summary>
        private const int RunMs = 600000;
        private const int EndingMs = 80000;

        // ---- people ----

        /// <summary>How many walk in, how often, and from how far out.</summary>
        private const int WalkInCount = 8;
        private const int WalkInEveryMs = 4000;
        private const float WalkFromMin = 70f;
        private const float WalkFromVary = 40f;

        /// <summary>
        /// A walk-in is only put down where the player cannot see, and not close.
        ///
        /// The old figure was twenty-six metres, in an open car park, in daylight -- which is
        /// a man appearing. Seventy to a hundred out on a pavement, behind a sphere the camera
        /// cannot see, and the first thing anybody sees him do is walk up.
        /// </summary>
        private const float SpawnClearOfPlayer = 40f;

        /// <summary>How long after parking the driver gets out, and his passenger before him.</summary>
        private const int OutAfterMs = 3000;
        private const int OutVaryMs = 5000;
        private const int PassengerOutMs = 1200;
        private const int PassengerOutVaryMs = 2500;

        /// <summary>A stint somewhere, and the longer first one at your own car.</summary>
        private const int StayMinMs = 18000;
        private const int StayVaryMs = 22000;
        private const int OwnStayMinMs = 35000;
        private const int OwnStayVaryMs = 25000;

        /// <summary>How long everybody favours the one huddle before it thins out round the cars.</summary>
        private const int MingleMs = 150000;

        /// <summary>Walking paces: brisk to arrive, a stroll between cars.</summary>
        private const float ArrivePace = 1.35f;
        private const float StrollPace = 1.0f;

        /// <summary>The nav-mesh walk's own patience, and how long a walk may take before it is doubted.</summary>
        private const int WalkTimeoutMs = 90000;
        private const int WalkDoubtMs = 30000;

        /// <summary>How long a man is allowed to be stood with no task before he is given one.</summary>
        private const int IdleDoubtMs = 3500;

        /// <summary>Where they stand: off the nose, down the side, round the huddle, back from the hopper.</summary>
        private const float NoseGap = 2.3f;
        private const float Abreast = 0.9f;
        private const float SideGap = 2.4f;
        private const float SideSlide = 1.3f;
        private const float RingMin = 1.8f;
        private const float RingVary = 1.2f;
        private const float HopperGap = 4.0f;
        private const float HopperSlide = 1.6f;
        private const float Elbow = 0.75f;

        // ======================================================================
        // What they do, by where they are. Every name is in Scenarios.txt here.
        // ======================================================================

        /// <summary>
        /// At a nose with the lid up. VEHICLE_MECHANIC is the one scenario in the game of a
        /// man leaning INTO an engine bay, and one of those with two beside him talking is the
        /// exact picture. INSPECT_STAND is a man looking down at something with his hands on
        /// his hips.
        /// </summary>
        private static readonly string[] AtNose =
        {
            "WORLD_HUMAN_VEHICLE_MECHANIC", "WORLD_HUMAN_HANG_OUT_STREET",
            "WORLD_HUMAN_HANG_OUT_STREET", "WORLD_HUMAN_MOBILE_FILM_SHOCKING",
            "WORLD_HUMAN_INSPECT_STAND", "WORLD_HUMAN_STAND_MOBILE"
        };

        /// <summary>Down a side: photographs, filming, a smoke, a crouch at the wheel.</summary>
        private static readonly string[] AtSide =
        {
            "WORLD_HUMAN_PAPARAZZI", "WORLD_HUMAN_MOBILE_FILM_SHOCKING",
            "WORLD_HUMAN_SMOKING", "WORLD_HUMAN_STAND_MOBILE",
            "WORLD_HUMAN_INSPECT_CROUCH", "WORLD_HUMAN_HANG_OUT_STREET"
        };

        /// <summary>
        /// In the huddle: mostly talking, because that is what the group is, with a couple of
        /// smokers, a drinker and somebody on their phone, because a dozen people all doing
        /// the identical thing is a chorus line. One in eight is dancing a bit.
        /// </summary>
        private static readonly string[] InHuddle =
        {
            "WORLD_HUMAN_HANG_OUT_STREET", "WORLD_HUMAN_HANG_OUT_STREET",
            "WORLD_HUMAN_HANG_OUT_STREET", "WORLD_HUMAN_DRINKING",
            "WORLD_HUMAN_SMOKING", "WORLD_HUMAN_STAND_MOBILE",
            "WORLD_HUMAN_HANG_OUT_STREET", "WORLD_HUMAN_PARTYING"
        };

        /// <summary>Round the one on juice: phones up, and somebody whooping at it.</summary>
        private static readonly string[] AtHopper =
        {
            "WORLD_HUMAN_MOBILE_FILM_SHOCKING", "WORLD_HUMAN_MOBILE_FILM_SHOCKING",
            "WORLD_HUMAN_PAPARAZZI", "WORLD_HUMAN_CHEERING", "WORLD_HUMAN_HANG_OUT_STREET"
        };

        /// <summary>Before anything has parked: waiting about, the way people do.</summary>
        private static readonly string[] Waiting =
        {
            "WORLD_HUMAN_STAND_MOBILE", "WORLD_HUMAN_SMOKING",
            "WORLD_HUMAN_HANG_OUT_STREET", "WORLD_HUMAN_STAND_IMPATIENT"
        };

        // ======================================================================
        // The things in it
        // ======================================================================

        private sealed class Runner
        {
            public Vehicle Car;
            public Ped Driver;
            public Ped Passenger;
            public MeetSpot Spot;

            public int SentAt;
            public bool InLot;
            public bool Parking;
            public int ParkFrom;
            public int ParkTries;
            public bool Seated;

            public int HopAt;
            public int Hop;

            /// <summary>Going home: told to pull out, and when. See Ending.</summary>
            public bool Going;
            public int LeftAt;
            public bool Released;
        }

        private enum Stage { Riding, Leaving, Walking, Standing, Boarding, Going }

        /// <summary>
        /// Anybody at the meet on foot, or about to be: a driver, a passenger, a walk-in.
        ///
        /// ONE KIND OF PERSON. The drivers and the crowd used to be two systems with two sets
        /// of rules, which is how the drivers came to be treated as furniture. Once he is out
        /// of the car a driver is somebody at a car meet like everybody else; the only thing
        /// his car buys him is that his first stint is at it.
        /// </summary>
        private sealed class Person
        {
            public Ped Man;
            public Runner Ride;
            public bool Drives;
            public Stage Stage;

            /// <summary>When this stage started, and when he next picks somewhere.</summary>
            public int At;
            public int MoveAt;

            public Vector3 Stand;
            public float Face;
            public string Doing = "";

            /// <summary>The car he is stood at, or null in the huddle.</summary>
            public Runner Near;
            public int Stints;

            /// <summary>Where a walk-in was put down, to walk back out to. See Ending.</summary>
            public Vector3 Home;
            public bool WalkedIn;
            public bool Released;
        }

        private readonly List<MeetSpot> _spots = new List<MeetSpot>();
        private readonly List<Runner> _out = new List<Runner>();
        private readonly List<Person> _people = new List<Person>();
        private readonly Random _rng = new Random();

        private bool _on;
        private bool _ending;
        private int _endAt;
        private int _lastTick;
        private int _lastSend;
        private int _lastWalkIn;
        private int _next;
        private int _startedAt;

        /// <summary>How many goes this space has had, and how many it gets. See Sending.</summary>
        private int _misses;
        private const int MostMisses = 4;
        private Vector3 _middle;

        /// <summary>Where the huddle is, or null if the file does not say.</summary>
        private MeetSpot _crowd;
        private Blip _blip;

        /// <summary>Set by Main: the cars the set drives. See Takeover.</summary>
        public Func<string[]> Cars;

        /// <summary>Set by Main: who turns up to look. See Takeover.</summary>
        public Func<string[]> Faces;

        /// <summary>
        /// Set by Main: the takeover's watching idles. Kept for the side of a car, where
        /// filming and photographing -- which is most of that list -- is exactly right.
        /// </summary>
        public Func<string[]> Idles;

        /// <summary>Set by Main: whether the player owns that car. See Yours.</summary>
        public Func<Vehicle, bool> Owned;

        /// <summary>
        /// The one on juice, and it is its OWN list rather than the takeover's.
        ///
        /// Six classics: this spot is not "a car with hydraulics", it is THE lowrider, and
        /// there is exactly one of it, so the list only has to be deep enough that it is not
        /// the same car every meet. A van at the one spot whose whole job is to bounce was a
        /// mistake made once already.
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

        // ======================================================================
        // The file
        // ======================================================================

        /// <summary>
        /// The spots, once.
        ///
        /// A meet with no file is not an error and does not complain twice: it is a mod
        /// somebody has deleted a data file out of, and the answer is to do nothing quietly.
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
                    // same file because it is part of the same place, but a car driven to it
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

        // ======================================================================
        // Starting and stopping
        // ======================================================================

        /// <summary>Put one on. Returns why not, or null once it is running.</summary>
        public string Start()
        {
            if (_spots.Count == 0) return "there's nowhere to hold one.";
            if (_on) return "there's one on already.";

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return "not right now.";

            _on = true;
            _ending = false;
            _next = 0;
            _lastSend = 0;
            _lastWalkIn = 0;
            _startedAt = Game.GameTime;
            _misses = 0;

            Sweep(null);
            Blip();

            Log.Info("Car meet: on, " + _spots.Count + " space(s) to fill.");
            return null;
        }

        /// <summary>
        /// Everything ours, gone. For a teardown or for the player driving off the map.
        ///
        /// THE PEOPLE ARE DELETED and the cars handed back. A script reload on site used to
        /// leave twelve cars and twelve drivers behind, persistent, mission-flagged and never
        /// reclaimed; and twenty-odd people released with KEEP_TASK on are twenty-odd people
        /// stood in scenarios for the rest of the session. Deleting them is the only cleanup
        /// that is actually clean. Nobody is watching a teardown.
        /// </summary>
        public void Stop()
        {
            foreach (var p in _people)
            {
                try
                {
                    if (p.Man == null || !p.Man.Exists()) continue;
                    p.Man.MarkAsNoLongerNeeded();
                    p.Man.Delete();
                }
                catch
                {
                    // The game takes them back.
                }
            }

            foreach (var r in _out)
            {
                try
                {
                    if (r.Car != null && r.Car.Exists())
                    {
                        Function.Call(Hash.FREEZE_ENTITY_POSITION, r.Car.Handle, false);
                        Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, r.Car.Handle, false, true);
                        r.Car.MarkAsNoLongerNeeded();
                    }
                }
                catch
                {
                    // Likewise.
                }
            }

            _people.Clear();
            _out.Clear();
            _next = 0;
            _on = false;
            _ending = false;

            Unblip();
        }

        public void RestoreWorld()
        {
            try { Stop(); }
            catch { /* the session is ending either way */ }
        }

        // ======================================================================
        // The tick
        // ======================================================================

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

                // FAR ENOUGH AWAY AND IT NEVER HAPPENED. Twelve cars and thirty people held
                // across the map is all of that the game could have been using for wherever
                // the player actually is.
                if (player.Position.DistanceTo(_middle) > ForgetAt) { Stop(); return; }

                if (_ending) { Ending(now); return; }

                // TEN MINUTES AND THEY GO HOME. Measured from the call, so the last car in
                // gets the least of it, and the going is a thing you watch -- see Over.
                if (now - _startedAt > RunMs) { Over(now); return; }

                Sending(now);
                WalkIns(player, now);

                foreach (var r in _out) Driving(r, now);

                People(now);
            }
            catch (Exception ex)
            {
                Log.Debug("The meet fell over: " + ex.Message);
            }
        }

        // ======================================================================
        // The cars, arriving
        // ======================================================================

        /// <summary>One more car sets off, every few seconds, until the places are full.</summary>
        private void Sending(int now)
        {
            if (_next >= _spots.Count) return;
            if (_lastSend != 0 && now - _lastSend < SendEveryMs) return;

            // NOT WHILE THE LAST ONE IS STILL PARKING. Two cars threading into neighbouring
            // bays at once is the shunt everybody sees; one at a time is a queue, which is
            // what the entrance to a meet looks like anyway. Bounded, so a car that never
            // manages it does not hold the other ten on the road for the rest of the night.
            var last = _out.Count > 0 ? _out[_out.Count - 1] : null;

            if (last != null && last.InLot && !last.Seated && now - last.ParkFrom < LotHoldMs &&
                last.Car != null && last.Car.Exists())
            {
                return;
            }

            _lastSend = now;

            var spot = _spots[_next];

            // AND AGAIN, FOR THIS ONE. The spaces were emptied when the meet was called, but a
            // car sets off from half a mile away and traffic parks in things.
            Sweep(spot);

            var r = Send(spot, now);

            // A SPACE IS ONLY SPENT ON A CAR THAT ACTUALLY SET OFF. A send can fail for a
            // reason that will not be true in six seconds -- no road found out there, a model
            // that would not stream -- so it keeps the space and tries again, and only gives
            // up on it after several goes.
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
        /// TRAFFIC PARKS IN CAR PARKS. NOT YOURS, THOUGH, AND NOT OURS: whatever the player is
        /// sat in, whatever they were last sat in, anything the ledger says they own, and any
        /// car that is at this meet already -- a meet car that landed a foot into the next bay
        /// used to be swept away by the next car sent to it. What goes is ambient traffic,
        /// which the game made and will make again.
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
                        if (Yours(car) || Mine(car)) continue;

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

        private bool Mine(Vehicle car)
        {
            foreach (var r in _out)
            {
                if (r.Car != null && r.Car.Exists() && r.Car == car) return true;
            }

            return false;
        }

        /// <summary>How much of a space is cleared. A car and a bit either side of it.</summary>
        private const float ClearRadius = 3.6f;

        /// <summary>
        /// One car, on a road, a long way off, pointed at its space, with somebody in it and
        /// sometimes somebody beside them.
        ///
        /// GetNextPositionOnStreet rather than the node native by hand: that native takes a
        /// pointer the game writes a lane count into, and passing a nought where it belongs
        /// took the process down. The wrapper has the argument list right.
        /// </summary>
        private Runner Send(MeetSpot spot, int now)
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

                var at = World.GetNextPositionOnStreet(probe, true);
                if (at == Vector3.Zero) at = World.GetNextPositionOnStreet(probe, false);

                if (at == Vector3.Zero)
                {
                    model.MarkAsNoLongerNeeded();
                    Log.Debug("Car meet: no road out at " + probe.X.ToString("0") + ", " +
                              probe.Y.ToString("0") + ".");
                    return null;
                }

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

                Keep(driver);
                Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, driver.Handle, false);
                Function.Call(Hash.SET_DRIVER_ABILITY, driver.Handle, 1f);
                Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, driver.Handle, 0.1f);

                var r = new Runner { Car = car, Driver = driver, Spot = spot, SentAt = now };

                // SOMEBODY IN THE PASSENGER SEAT, on most cars but not all. He costs nothing
                // to arrive -- he is in the car -- and he is one more person at the meet who
                // was not put down on the pavement. Not in the lowrider: that man is working.
                if (!spot.IsHopper && _rng.Next(100) < PassengerChance)
                {
                    var mate = car.CreatePedOnSeat(VehicleSeat.RightFront, DriverModel());

                    if (mate != null && mate.Exists())
                    {
                        Keep(mate);
                        r.Passenger = mate;
                    }
                }

                // The bay itself, from out on the road. The lot re-issues this slower once he
                // is at the mouth of it -- see Driving.
                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, driver.Handle, car.Handle,
                              spot.At.X, spot.At.Y, spot.At.Z,
                              RoadSpeed, 0, car.Model.Hash, RoadStyle, 12f, true);

                _people.Add(new Person { Man = driver, Ride = r, Drives = true, Stage = Stage.Riding, At = now });

                if (r.Passenger != null)
                {
                    _people.Add(new Person { Man = r.Passenger, Ride = r, Stage = Stage.Riding, At = now });
                }

                return r;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not send a car to the meet: " + ex.Message);
                return null;
            }
        }

        /// <summary>Ours, and not somebody the game gets to redirect.</summary>
        private static void Keep(Ped ped)
        {
            ped.IsPersistent = true;
            Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, ped.Handle, true, true);
            Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, true);
            Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, ped.Handle, false);
        }

        /// <summary>Where one car is up to.</summary>
        private void Driving(Runner r, int now)
        {
            if (r.Car == null || !r.Car.Exists()) return;

            if (r.Seated) { Hopping(r, now); return; }
            if (r.Driver == null || !r.Driver.Exists() || !r.Driver.IsAlive) return;

            var gap = r.Car.Position.DistanceTo(r.Spot.At);

            // AT THE MOUTH OF THE LOT: slow right down and start stopping for things.
            if (!r.InLot && gap < LotFrom)
            {
                r.InLot = true;
                r.ParkFrom = now;

                try
                {
                    Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, r.Driver.Handle, r.Car.Handle,
                                  r.Spot.At.X, r.Spot.At.Y, r.Spot.At.Z,
                                  LotSpeed, 0, r.Car.Model.Hash, LotStyle, 8f, true);
                }
                catch
                {
                    // He carries on at road pace, which is what he did before this existed.
                }

                return;
            }

            // AT THE BAY: park. Mode 0 is nose first, which is how anybody pulls into one.
            if (!r.Parking && gap < ParkFrom)
            {
                Park(r, now);
                return;
            }

            if (!r.Parking) return;

            var slow = false;
            try { slow = r.Car.Speed < SeatSpeed; } catch { slow = true; }

            // NEAR ENOUGH AND STOPPED, AND NOTHING ELSE, is the ordinary way in.
            if (gap < SeatWithin && slow) { Seat(r, now); return; }

            var parking = now - r.ParkFrom;

            // Close and stopped for a while, but not the metre: squared up from there.
            if (parking > ParkPatienceMs && gap < SeatLoose && slow) { Seat(r, now); return; }

            // Stopped somewhere daft, or still wandering: asked to park again, a few times.
            if (parking > ParkRetryMs && r.ParkTries < 3)
            {
                Log.Debug("Car meet: a car is " + gap.ToString("0.0") + " m off its bay after " +
                          (parking / 1000) + " s; asking again.");
                Park(r, now);
            }
        }

        private void Park(Runner r, int now)
        {
            r.Parking = true;
            r.ParkFrom = now;
            r.ParkTries++;

            try
            {
                Function.Call(Hash.CLEAR_PED_TASKS, r.Driver.Handle);
                Function.Call(Hash.TASK_VEHICLE_PARK, r.Driver.Handle, r.Car.Handle,
                              r.Spot.At.X, r.Spot.At.Y, r.Spot.At.Z, r.Spot.Heading,
                              0, 20f, true);
            }
            catch
            {
                // The seat above catches it either way.
            }
        }

        /// <summary>
        /// The car, on its spot, exactly, and the people in it given their cue to get out.
        ///
        /// NO OFFSET. SET_ENTITY_COORDS applies the game's own lift and a car put down with it
        /// stands a foot in the air and then drops. The spot's Z came off a car that was
        /// parked in it, so it is already the right height.
        /// </summary>
        private void Seat(Runner r, int now)
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

                // BONNET UP, WHICH IS THE ONLY REASON ANYBODY PARKS LIKE THIS. Door four is the
                // bonnet, opened loose so it sits rather than swings. Not the one on juice: its
                // whole show is underneath it.
                if (!r.Spot.IsHopper)
                {
                    Function.Call(Hash.SET_VEHICLE_DOOR_OPEN, r.Car.Handle, Bonnet, true, true);
                }

                Function.Call(Hash.SET_VEHICLE_ENGINE_ON, r.Car.Handle, r.Spot.IsHopper, true, true);
                Function.Call(Hash.SET_VEHICLE_LIGHTS, r.Car.Handle, r.Spot.IsHopper ? 2 : 0);

                // FROZEN, WHICH FOR A PARKED CAR IS WHAT PARKED MEANS. Square in a bay and
                // still square in it in ten minutes; nobody can nudge it, and at a car meet
                // that is not a cost. Not the one on juice, which has to be able to move.
                if (!r.Spot.IsHopper) Function.Call(Hash.FREEZE_ENTITY_POSITION, r.Car.Handle, true);

                // THE LOWRIDER HAS THE MUSIC. Engine running, doors shut, radio loud -- one
                // car at the meet is the sound system, and it is the one everybody is stood
                // round anyway.
                if (r.Spot.IsHopper)
                {
                    Function.Call(Hash.SET_VEHICLE_RADIO_ENABLED, r.Car.Handle, true);
                    Function.Call(Hash.SET_VEH_RADIO_STATION, r.Car.Handle, Radio.WestCoast);
                    Function.Call(Hash.SET_VEHICLE_RADIO_LOUD, r.Car.Handle, true);
                }

                Function.Call(Hash.SET_PED_KEEP_TASK, r.Driver.Handle, true);

                Log.Info("Car meet: " + r.Car.DisplayName + " is in, bay " + (_out.IndexOf(r) + 1) +
                         " of " + _spots.Count + ".");
            }
            catch
            {
                // It is where it is.
            }

            // AND THEY GET OUT IN A MOMENT. Staggered off the arrival rather than all at
            // once, passenger first, the way it goes. Not the man on the switches.
            if (r.Spot.IsHopper) return;

            foreach (var p in _people)
            {
                if (p.Ride != r || p.Stage != Stage.Riding) continue;

                p.MoveAt = now + (p.Drives
                    ? OutAfterMs + _rng.Next(OutVaryMs)
                    : PassengerOutMs + _rng.Next(PassengerOutVaryMs));
            }
        }

        /// <summary>
        /// Which way round it ends up sitting.
        ///
        /// EITHER WAY DOWN THE BAY, whichever it is already nearest. A car that has just
        /// driven in nose first was being spun a hundred and eighty degrees on the spot to
        /// match the surveyed heading. A bay does not care which way you point; what it cares
        /// about is being square in it.
        /// </summary>
        private static float Facing(Runner r)
        {
            var want = r.Spot.Heading;

            try
            {
                var has = r.Car.Heading;
                var off = Math.Abs(((want - has) % 360f + 540f) % 360f - 180f);

                if (off > 90f) return (want + 180f) % 360f;
            }
            catch
            {
                // Then the surveyed one, which is at least square in the bay.
            }

            return want;
        }

        // ---- the one on juice ----

        /// <summary>
        /// The lowrider, bouncing. Two poses on a clock rather than a held one, worked round
        /// the switch box in order, with a gap that is not the same twice.
        /// </summary>
        private void Hopping(Runner r, int now)
        {
            if (!r.Spot.IsHopper) return;
            if (r.HopAt != 0 && now < r.HopAt) return;

            r.HopAt = now + HopMinMs + _rng.Next(HopVaryMs);
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

        /// <summary>Down, up, front, back, and the two sides. Nought comes round often.</summary>
        private static readonly int[] Hops = { 0, 2, 0, 3, 4, 0, 5, 6, 0, 2 };

        // ======================================================================
        // The people who came on foot
        // ======================================================================

        /// <summary>
        /// One more of them, put down a long way off where the camera is not looking, and
        /// left to walk in.
        ///
        /// Tried a handful of places round the compass and given up for this tick if none
        /// is out of sight: a man appearing in front of you is worse than a man arriving
        /// four seconds later.
        /// </summary>
        private void WalkIns(Ped player, int now)
        {
            var have = 0;
            foreach (var p in _people) if (p.WalkedIn) have++;

            if (have >= WalkInCount) return;
            if (_lastWalkIn != 0 && now - _lastWalkIn < WalkInEveryMs) return;

            _lastWalkIn = now;

            try
            {
                var names = Faces == null ? null : Faces();
                if (names == null || names.Length == 0) return;

                var centre = _crowd != null ? _crowd.At : _middle;
                var me = player.Position;

                var at = Vector3.Zero;

                for (var tries = 0; tries < 6; tries++)
                {
                    var way = (float)(_rng.NextDouble() * Math.PI * 2.0);
                    var far = WalkFromMin + (float)_rng.NextDouble() * WalkFromVary;

                    var probe = centre + new Vector3((float)Math.Cos(way) * far,
                                                     (float)Math.Sin(way) * far, 0f);

                    var on = World.GetNextPositionOnSidewalk(probe);
                    if (on == Vector3.Zero) continue;
                    if (on.DistanceTo(me) < SpawnClearOfPlayer) continue;
                    if (Function.Call<bool>(Hash.IS_SPHERE_VISIBLE, on.X, on.Y, on.Z, 3f)) continue;

                    at = on;
                    break;
                }

                if (at == Vector3.Zero) return;

                Model model = new Model(0);

                for (var tries = 0; tries < 8; tries++)
                {
                    var one = new Model(names[_rng.Next(names.Length)]);

                    if (one.IsValid && one.IsInCdImage && one.Request(1200)) { model = one; break; }
                }

                if (!model.IsValid) return;

                var man = World.CreatePed(model, at);
                model.MarkAsNoLongerNeeded();

                if (man == null || !man.Exists()) return;

                Keep(man);

                var p = new Person { Man = man, Stage = Stage.Walking, At = now, Home = at, WalkedIn = true };
                _people.Add(p);

                Plan(p, now, ArrivePace);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not bring somebody to the meet: " + ex.Message);
            }
        }

        // ======================================================================
        // Everybody, every tick
        // ======================================================================

        /// <summary>
        /// Where each person is up to, and what to do about it.
        ///
        /// RIDING waits for the cue Seat gave him, then he is told to get out. LEAVING waits
        /// until he is actually out of the car (or has had long enough) and then plans his
        /// first stint, at his own car. WALKING ends when the scenario at the far end takes
        /// him, and is doubted if it takes too long. STANDING ends on his clock -- and early,
        /// if the scenario has dropped him and he is stood there with nothing, which is the
        /// one thing nobody at this meet is allowed to do.
        /// </summary>
        private void People(int now)
        {
            for (var i = _people.Count - 1; i >= 0; i--)
            {
                var p = _people[i];

                if (p.Man == null || !p.Man.Exists() || !p.Man.IsAlive)
                {
                    _people.RemoveAt(i);
                    continue;
                }

                try
                {
                    switch (p.Stage)
                    {
                        case Stage.Riding:
                            if (p.Ride == null || !p.Ride.Seated || p.Ride.Spot.IsHopper) break;
                            if (p.MoveAt == 0 || now < p.MoveAt) break;

                            if (p.Ride.Car != null && p.Ride.Car.Exists())
                            {
                                Function.Call(Hash.SET_PED_KEEP_TASK, p.Man.Handle, false);
                                Function.Call(Hash.CLEAR_PED_TASKS, p.Man.Handle);
                                Function.Call(Hash.TASK_LEAVE_VEHICLE, p.Man.Handle, p.Ride.Car.Handle, 0);
                            }

                            p.Stage = Stage.Leaving;
                            p.At = now;
                            break;

                        case Stage.Leaving:
                            if (p.Man.IsInVehicle() && now - p.At < LeaveDoubtMs) break;

                            Plan(p, now, StrollPace);
                            break;

                        case Stage.Walking:
                            if (Function.Call<bool>(Hash.IS_PED_USING_ANY_SCENARIO, p.Man.Handle))
                            {
                                p.Stage = Stage.Standing;
                                p.At = now;
                                break;
                            }

                            // Not walking and not stood in it: the route failed or the
                            // scenario would not take there. Somewhere else, then.
                            if (now - p.At > WalkDoubtMs ||
                                (now - p.At > IdleDoubtMs &&
                                 !Function.Call<bool>(Hash.GET_IS_TASK_ACTIVE, p.Man.Handle, 224)))
                            {
                                Plan(p, now, StrollPace);
                            }
                            break;

                        case Stage.Standing:
                            if (now >= p.MoveAt)
                            {
                                Plan(p, now, StrollPace);
                                break;
                            }

                            if (now - p.At > IdleDoubtMs &&
                                !Function.Call<bool>(Hash.IS_PED_USING_ANY_SCENARIO, p.Man.Handle))
                            {
                                Plan(p, now, StrollPace);
                            }
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug("Somebody at the meet fell over: " + ex.Message);
                }
            }
        }

        /// <summary>How long a man is given to get out of a car before he is planned for anyway.</summary>
        private const int LeaveDoubtMs = 9000;

        // ======================================================================
        // Somewhere to be
        // ======================================================================

        /// <summary>
        /// Pick him somewhere, and something to do there, and send him.
        ///
        /// HIS OWN CAR FIRST if he came in one -- the driver at the nose with the lid up, his
        /// passenger down the side -- and for longer, because a man who just drove his car to
        /// a meet stands at it. After that it is the huddle, mostly, for the first couple of
        /// minutes, and the cars mostly after -- and the lowrider now and then, because it is
        /// the thing bouncing. Never the car he is already at.
        /// </summary>
        private void Plan(Person p, int now, float pace)
        {
            var cars = new List<Runner>();
            Runner hopper = null;

            foreach (var r in _out)
            {
                if (!r.Seated || r.Car == null || !r.Car.Exists()) continue;
                if (r.Spot.IsHopper) hopper = r;
                else cars.Add(r);
            }

            var first = p.Stints == 0 && p.Ride != null && p.Ride.Seated && p.Ride.Car != null && p.Ride.Car.Exists();
            var mingle = _crowd != null && now - _startedAt < MingleMs;

            Vector3 stand;
            float face;
            string doing;
            Runner near = null;
            var own = false;

            if (first)
            {
                near = p.Ride;
                own = true;

                if (p.Drives) Nose(near, out stand, out face, out doing, true);
                else Side(near, out stand, out face, out doing);
            }
            else
            {
                var roll = _rng.Next(100);
                var huddle = _crowd != null && roll < (mingle ? 45 : 18);
                var juice = !huddle && hopper != null && hopper != p.Near && roll < (mingle ? 57 : 32);

                if (huddle)
                {
                    Ring(_crowd.At, RingMin, RingVary, InHuddle, out stand, out face, out doing);
                }
                else if (juice)
                {
                    near = hopper;
                    Watch(hopper, out stand, out face, out doing);
                }
                else if (cars.Count > 0)
                {
                    // One he is not at. Walked from a random start so a full row is used.
                    near = cars[_rng.Next(cars.Count)];

                    if (near == p.Near && cars.Count > 1)
                    {
                        near = cars[(cars.IndexOf(near) + 1 + _rng.Next(cars.Count - 1)) % cars.Count];
                    }

                    if (_rng.Next(100) < 62) Nose(near, out stand, out face, out doing, false);
                    else Side(near, out stand, out face, out doing);
                }
                else if (_crowd != null)
                {
                    Ring(_crowd.At, RingMin, RingVary, InHuddle, out stand, out face, out doing);
                }
                else
                {
                    Ring(_middle, 4f, 3f, Waiting, out stand, out face, out doing);
                }
            }

            if (!Go(p, stand, face, doing, pace))
            {
                // Try again next tick rather than leaving him.
                p.Stage = Stage.Standing;
                p.At = now - IdleDoubtMs;
                p.MoveAt = now + 800;
                return;
            }

            p.Stage = Stage.Walking;
            p.At = now;
            p.Stand = stand;
            p.Face = face;
            p.Doing = doing;
            p.Near = near;
            p.Stints++;

            p.MoveAt = now + (own
                ? OwnStayMinMs + _rng.Next(OwnStayVaryMs)
                : StayMinMs + _rng.Next(StayVaryMs));
        }

        /// <summary>
        /// Across the nose, three abreast a hand's width apart, facing back at the engine,
        /// which is the shape people make when there is something to look at under a lid.
        /// The middle slot is the mechanic's if the driver wants it.
        /// </summary>
        private void Nose(Runner car, out Vector3 stand, out float face, out string doing, bool owner)
        {
            var v = car.Car;
            var nose = v.Position + v.ForwardVector * NoseGap;

            var order = new[] { 0, -1, 1 };
            Shuffle(order);

            stand = nose;

            foreach (var slot in order)
            {
                var candidate = nose + v.RightVector * (slot * Abreast);
                if (!Free(candidate)) continue;

                stand = candidate;
                break;
            }

            stand = Ground(stand);
            face = Toward(v.Position, stand);

            doing = owner && _rng.Next(100) < 55
                ? "WORLD_HUMAN_VEHICLE_MECHANIC"
                : AtNose[_rng.Next(AtNose.Length)];
        }

        /// <summary>Down a side, a stride off it, somewhere along its length, looking at it.</summary>
        private void Side(Runner car, out Vector3 stand, out float face, out string doing)
        {
            var v = car.Car;

            stand = v.Position;

            for (var tries = 0; tries < 5; tries++)
            {
                var hand = _rng.Next(2) == 0 ? -1f : 1f;
                var along = ((float)_rng.NextDouble() - 0.5f) * 2f * SideSlide;

                var candidate = v.Position + v.RightVector * (hand * SideGap) + v.ForwardVector * along;

                stand = candidate;
                if (Free(candidate)) break;
            }

            stand = Ground(stand);
            face = Toward(v.Position, stand);

            // The takeover's watching list is filming and photographs, which down the side of
            // a car is right. Half and half with ours.
            var theirs = Idles == null ? null : Idles();

            doing = theirs != null && theirs.Length > 0 && _rng.Next(2) == 0
                ? theirs[_rng.Next(theirs.Length)]
                : AtSide[_rng.Next(AtSide.Length)];
        }

        /// <summary>Back from the lowrider, either side, phone up.</summary>
        private void Watch(Runner car, out Vector3 stand, out float face, out string doing)
        {
            var v = car.Car;

            stand = v.Position;

            for (var tries = 0; tries < 5; tries++)
            {
                var hand = _rng.Next(2) == 0 ? -1f : 1f;
                var along = ((float)_rng.NextDouble() - 0.5f) * 2f * HopperSlide;

                var candidate = v.Position + v.RightVector * (hand * HopperGap) + v.ForwardVector * along;

                stand = candidate;
                if (Free(candidate)) break;
            }

            stand = Ground(stand);
            face = Toward(v.Position, stand);
            doing = AtHopper[_rng.Next(AtHopper.Length)];
        }

        /// <summary>
        /// Round a point, facing in, which is what a group of people talking is. Where in the
        /// ring is wherever is free, so two men never walk to the same patch of ground.
        /// </summary>
        private void Ring(Vector3 centre, float radius, float vary, string[] pool,
                          out Vector3 stand, out float face, out string doing)
        {
            stand = centre;

            for (var tries = 0; tries < 8; tries++)
            {
                var round = (float)(_rng.NextDouble() * Math.PI * 2.0);
                var far = radius + (float)_rng.NextDouble() * vary;

                var candidate = centre + new Vector3((float)Math.Cos(round) * far,
                                                     (float)Math.Sin(round) * far, 0f);

                stand = candidate;
                if (Free(candidate)) break;
            }

            stand = Ground(stand);
            face = Toward(centre, stand);
            doing = pool[_rng.Next(pool.Length)];
        }

        /// <summary>Whether nobody else is headed for, or stood on, that patch.</summary>
        private bool Free(Vector3 at)
        {
            foreach (var p in _people)
            {
                if (p.Stage != Stage.Walking && p.Stage != Stage.Standing) continue;
                if (p.Stand.DistanceTo2D(at) < Elbow) return false;
            }

            return true;
        }

        private static float Toward(Vector3 target, Vector3 from)
        {
            var d = target - from;
            return (float)(Math.Atan2(d.Y, d.X) * 180.0 / Math.PI) - 90f;
        }

        /// <summary>
        /// The ground under a point.
        ///
        /// A car's Position is its centre, half a metre up; a ped's is his pelvis. Handing a
        /// car's height to a scenario as a place to stand is how they ended up in the ground
        /// to the waist. Asked from a bit above, and only believed within a few metres.
        /// </summary>
        private static Vector3 Ground(Vector3 where)
        {
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
                // Then where it was, which the nav mesh will settle anyway.
            }

            return where;
        }

        private void Shuffle(int[] a)
        {
            for (var i = a.Length - 1; i > 0; i--)
            {
                var j = _rng.Next(i + 1);
                var t = a[i]; a[i] = a[j]; a[j] = t;
            }
        }

        /// <summary>
        /// Walk there, then do that, as one sequence.
        ///
        /// THIS IS THE FIX FOR THE WARPING. Two tasks issued back to back are one task: the
        /// second replaces the first on the same frame. In a sequence they run in order --
        /// the nav-mesh walk first, which goes round parked cars rather than through them,
        /// and then the scenario AT the spot with the teleport flag OFF and no end, so he
        /// stays in it until his clock says otherwise. Same shape Homies uses for the cab.
        /// </summary>
        private bool Go(Person p, Vector3 stand, float face, string doing, float pace)
        {
            var slot = new OutputArgument();

            try
            {
                Function.Call(Hash.SET_PED_KEEP_TASK, p.Man.Handle, false);

                Function.Call(Hash.OPEN_SEQUENCE_TASK, slot);
                var seq = slot.GetResult<int>();

                Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, 0,
                              stand.X, stand.Y, stand.Z, pace, WalkTimeoutMs, 0.6f, 0, face);

                Function.Call(Hash.TASK_START_SCENARIO_AT_POSITION, 0, doing,
                              stand.X, stand.Y, stand.Z, face, -1, false, false);

                Function.Call(Hash.CLOSE_SEQUENCE_TASK, seq);
                Function.Call(Hash.TASK_PERFORM_SEQUENCE, p.Man.Handle, seq);

                Function.Call(Hash.SET_PED_KEEP_TASK, p.Man.Handle, true);
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not send somebody across the meet: " + ex.Message);
                return false;
            }
            finally
            {
                try { Function.Call(Hash.CLEAR_SEQUENCE_TASK, slot); } catch { }
            }
        }

        // ======================================================================
        // Going home
        // ======================================================================

        /// <summary>
        /// Time. The people who walked in walk out; the passengers get back in; the drivers
        /// follow in a moment; and the cars pull out as they fill -- see Ending.
        ///
        /// NOT DELETED WHERE THEY STAND AND NOT ABANDONED WHERE THEY STAND EITHER. Twelve
        /// cars vanishing out of a car park in front of somebody is worse than twelve cars
        /// being there too long; and twelve cars simply "released" with their drivers stood
        /// about in scenarios was twelve cars welded to the block until the game got round
        /// to them. This is the going that a meet actually has.
        /// </summary>
        private void Over(int now)
        {
            _ending = true;
            _endAt = now;

            Log.Info("Car meet: time. " + _out.Count + " car(s) and " + _people.Count + " people heading off.");

            var nth = 0;

            foreach (var p in _people)
            {
                try
                {
                    if (p.Man == null || !p.Man.Exists()) continue;

                    Function.Call(Hash.SET_PED_KEEP_TASK, p.Man.Handle, false);

                    if (p.WalkedIn || p.Ride == null || p.Ride.Car == null || !p.Ride.Car.Exists())
                    {
                        // Back out the way he came, or if he has nowhere, off the middle of it.
                        var home = p.WalkedIn ? p.Home : Away(now, nth++);

                        Function.Call(Hash.CLEAR_PED_TASKS, p.Man.Handle);
                        Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, p.Man.Handle,
                                      home.X, home.Y, home.Z, ArrivePace, WalkTimeoutMs, 1.0f, 0, 0f);

                        p.Stage = Stage.Going;
                        p.At = now;
                        continue;
                    }

                    // A driver: the car he came in wants to be able to move again first, and
                    // the lid comes down. A passenger goes straight for his door; the driver
                    // a few seconds after, staggered so twelve doors do not open on one frame.
                    if (p.Drives)
                    {
                        p.MoveAt = now + 2500 + nth * 900 + _rng.Next(2500);
                        nth++;
                    }
                    else
                    {
                        p.MoveAt = now + 400 + _rng.Next(1500);
                    }

                    p.Stage = Stage.Boarding;
                    p.At = now;
                }
                catch
                {
                    // He goes with the session.
                }
            }
        }

        /// <summary>Somewhere off the meet to walk to when there is no "home" to go back to.</summary>
        private Vector3 Away(int now, int nth)
        {
            var round = (float)((nth * 0.9 + (now % 1000) / 1000.0) % (Math.PI * 2.0));
            var probe = _middle + new Vector3((float)Math.Cos(round) * 60f, (float)Math.Sin(round) * 60f, 0f);

            var on = World.GetNextPositionOnSidewalk(probe);
            return on == Vector3.Zero ? probe : on;
        }

        /// <summary>
        /// The going, every tick.
        ///
        /// A car pulls out once its driver is in and its passenger is in or has had long
        /// enough. Anything twenty-five seconds down the road, or out of sight, is handed
        /// back to the game. When everything is handed back -- or the whole thing has run
        /// long enough -- what is left is stopped the hard way, which by then is nothing.
        /// </summary>
        private void Ending(int now)
        {
            var left = 0;

            for (var i = _people.Count - 1; i >= 0; i--)
            {
                var p = _people[i];

                if (p.Man == null || !p.Man.Exists() || !p.Man.IsAlive) { _people.RemoveAt(i); continue; }
                if (p.Released) continue;

                try
                {
                    if (p.Stage == Stage.Boarding)
                    {
                        left++;

                        if (p.MoveAt != 0 && now >= p.MoveAt)
                        {
                            p.MoveAt = 0;
                            Board(p);
                        }
                        continue;
                    }

                    if (p.Stage == Stage.Going)
                    {
                        var far = p.Man.Position.DistanceTo(_middle);

                        if (far > 55f || (far > 30f && !p.Man.IsOnScreen) || now - p.At > 60000)
                        {
                            Let(p.Man);
                            p.Released = true;
                        }
                        else
                        {
                            left++;
                        }
                        continue;
                    }

                    // Still in the car he came in (the man on the switches, or anybody whose
                    // cue never came): he leaves with it.
                    left++;
                }
                catch
                {
                    // Counted as gone.
                }
            }

            foreach (var r in _out)
            {
                if (r.Released) continue;

                try
                {
                    if (r.Car == null || !r.Car.Exists() || r.Driver == null || !r.Driver.Exists())
                    {
                        Release(r);
                        continue;
                    }

                    if (!r.Going)
                    {
                        left++;

                        var driverIn = r.Driver.IsInVehicle(r.Car);
                        var mateIn = r.Passenger == null || !r.Passenger.Exists() ||
                                     r.Passenger.IsInVehicle(r.Car) || now - _endAt > 30000;

                        // Nobody got in within the time: the car is handed back as it is.
                        if (!driverIn && now - _endAt > 45000) { Release(r); continue; }

                        if (driverIn && mateIn) PullOut(r, now);
                        continue;
                    }

                    var gone = r.Car.Position.DistanceTo(_middle);

                    if (gone > 70f || (gone > 35f && !r.Car.IsOnScreen) || now - r.LeftAt > 30000)
                    {
                        Release(r);
                    }
                    else
                    {
                        left++;
                    }
                }
                catch
                {
                    // Counted as gone.
                }
            }

            if (left == 0 || now - _endAt > EndingMs)
            {
                Log.Info("Car meet: over.");
                Stop();
            }
        }

        /// <summary>Get back in. The driver's car is unfrozen and shut up first.</summary>
        private void Board(Person p)
        {
            var r = p.Ride;
            if (r == null || r.Car == null || !r.Car.Exists()) return;

            if (p.Drives)
            {
                Function.Call(Hash.FREEZE_ENTITY_POSITION, r.Car.Handle, false);
                Function.Call(Hash.SET_VEHICLE_HANDBRAKE, r.Car.Handle, false);
                Function.Call(Hash.SET_VEHICLE_DOOR_SHUT, r.Car.Handle, Bonnet, false);
            }

            if (p.Man.IsInVehicle(r.Car)) return;

            Function.Call(Hash.CLEAR_PED_TASKS, p.Man.Handle);
            Function.Call(Hash.TASK_ENTER_VEHICLE, p.Man.Handle, r.Car.Handle,
                          30000, p.Drives ? -1 : 0, 1.6f, 1, 0);
        }

        /// <summary>Out of the bay and off into traffic, carefully.</summary>
        private void PullOut(Runner r, int now)
        {
            r.Going = true;
            r.LeftAt = now;

            try
            {
                Function.Call(Hash.FREEZE_ENTITY_POSITION, r.Car.Handle, false);
                Function.Call(Hash.SET_VEHICLE_HANDBRAKE, r.Car.Handle, false);
                Function.Call(Hash.SET_VEHICLE_DOOR_SHUT, r.Car.Handle, Bonnet, false);
                Function.Call(Hash.SET_VEHICLE_ENGINE_ON, r.Car.Handle, true, true, false);
                Function.Call(Hash.SET_VEHICLE_LIGHTS, r.Car.Handle, 0);

                if (r.Spot.IsHopper)
                {
                    Function.Call(Hash.SET_HYDRAULIC_VEHICLE_STATE, r.Car.Handle, 0);
                    Function.Call(Hash.SET_VEHICLE_RADIO_LOUD, r.Car.Handle, false);
                }

                Function.Call(Hash.SET_PED_KEEP_TASK, r.Driver.Handle, false);
                Function.Call(Hash.CLEAR_PED_TASKS, r.Driver.Handle);
                Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, r.Driver.Handle, r.Car.Handle, HomeSpeed, HomeStyle);
            }
            catch (Exception ex)
            {
                Log.Debug("A meet car would not pull out: " + ex.Message);
            }
        }

        /// <summary>A car and whoever is in it, handed back to the game.</summary>
        private void Release(Runner r)
        {
            r.Released = true;

            try
            {
                if (r.Car != null && r.Car.Exists())
                {
                    Function.Call(Hash.FREEZE_ENTITY_POSITION, r.Car.Handle, false);
                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, r.Car.Handle, false, true);
                    r.Car.IsPersistent = false;
                    r.Car.MarkAsNoLongerNeeded();
                }

                foreach (var p in _people)
                {
                    if (p.Ride != r || p.Released) continue;
                    if (p.Man == null || !p.Man.Exists()) continue;

                    Let(p.Man);
                    p.Released = true;
                }
            }
            catch
            {
                // It goes with the session either way.
            }
        }

        /// <summary>One person, handed back. His task stays with him so he keeps going.</summary>
        private static void Let(Ped man)
        {
            try
            {
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, man.Handle, false);
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, man.Handle, false, true);
                man.IsPersistent = false;
                man.MarkAsNoLongerNeeded();
            }
            catch
            {
                // Likewise.
            }
        }

        // ======================================================================
        // What they look like
        // ======================================================================

        /// <summary>
        /// Competition suspension, no livery, any colour, and neons on most of them.
        ///
        /// THE LOWEST THE CAR HAS, rather than index four: not every car has all five
        /// suspension mods. NOT ON THE ONE WITH HYDRAULICS -- on a Benny's lowrider the
        /// suspension slot IS the hydraulics.
        /// </summary>
        private void Dress(Vehicle car, MeetSpot spot)
        {
            try
            {
                var h = car.Handle;

                Function.Call(Hash.SET_VEHICLE_MOD_KIT, h, 0);

                if (!spot.IsHopper)
                {
                    var many = Function.Call<int>(Hash.GET_NUM_VEHICLE_MODS, h, Suspension);
                    if (many > 0) Function.Call(Hash.SET_VEHICLE_MOD, h, Suspension, many - 1, false);
                }

                Function.Call(Hash.SET_VEHICLE_LIVERY, h, -1);
                Function.Call(Hash.SET_VEHICLE_MOD, h, Livery, -1, false);

                Function.Call(Hash.SET_VEHICLE_DIRT_LEVEL, h, 0f);
                Function.Call(Hash.SET_VEHICLE_WINDOW_TINT, h, _rng.Next(2) == 0 ? 1 : 3);

                if (spot.IsHopper)
                {
                    Function.Call(Hash.SET_VEHICLE_COLOURS, h, Green, Green);
                    Function.Call(Hash.SET_VEHICLE_EXTRA_COLOURS, h, GreenPearl, 0);

                    Neon(car, 0, 255, 60);

                    Function.Call(Hash.SET_CAN_USE_HYDRAULICS, h, true);
                    return;
                }

                var paint = Paints[_rng.Next(Paints.Length)];

                Function.Call(Hash.SET_VEHICLE_COLOURS, h, paint, paint);
                Function.Call(Hash.SET_VEHICLE_EXTRA_COLOURS, h, Paints[_rng.Next(Paints.Length)], 0);

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
        /// Whatever colour, out of the ones that read as a paint job: the metallics and the
        /// brights, not the primers and the browns off a taxi.
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
        /// A car nobody at this meet is already in. Four of the same car is not a car meet,
        /// and random over twelve draws gives a repeat far more often than people expect.
        /// The last few tries take anything, so a short list cannot leave a space empty.
        /// </summary>
        private Model Pick(string[] names)
        {
            for (var tries = 0; tries < 24; tries++)
            {
                var name = names[_rng.Next(names.Length)];

                if (tries < 18 && Already(name)) continue;

                var model = new Model(name);

                if (model.IsValid && model.IsInCdImage && model.Request(1500)) return model;
            }

            return new Model(0);
        }

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

        /// <summary>Somebody to drive it, or ride in it. The set and the block.</summary>
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

        // ======================================================================
        // The blip
        // ======================================================================

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
