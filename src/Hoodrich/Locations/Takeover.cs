using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.UI;

namespace Hoodrich.Locations
{
    /// <summary>Where the night is up to.</summary>
    internal enum TakeoverState
    {
        /// <summary>Not tonight, or not yet.</summary>
        None,

        /// <summary>People are there and it is running.</summary>
        Running,

        /// <summary>Blue lights. Everybody goes.</summary>
        Scattering
    }

    /// <summary>
    /// The takeover.
    ///
    /// One intersection, one night, some time between nine and four. Cars and people turn up,
    /// a ring forms, and somebody puts a car sideways in the middle of it while the ring
    /// watches. One or two at a time, and when one pulls out another goes in. It runs for
    /// hours and it ends the way these always end.
    ///
    /// IT IS NOT A MISSION AND THERE IS NOTHING TO DO. You are not called, there is no blip
    /// and nothing is asked of you -- it happens whether you go or not, and the whole value of
    /// it is that you can drive past a junction at two in the morning and find it going on. A
    /// version of this with an objective attached would be a worse thing than the thing it is.
    ///
    /// THE CIRCLE IS DRIVEN, NOT TASKED. No native asks a driver to hold a donut at a radius
    /// around a point -- the closest are the burnout actions, which go wherever the car happens
    /// to be pointing for however long you name and cannot be aimed. So the car is pushed round
    /// by hand: an angle that advances, a heading lerped toward the tangent of it, and forward
    /// speed. Grip is turned down underneath that, which is what makes the back end run wide
    /// and turns a circle into circle work.
    ///
    /// THE RING IS AT NINETEEN AND THE CARS WORK AT FIFTEEN, which is four metres of
    /// clearance and is the one number in here worth arguing about. A back end that steps a
    /// car's width wide of its line is into the front row -- and stepping wide is exactly what
    /// the reduced grip is for. It is what was asked for; it is also the first thing to move if
    /// the crowd starts getting clipped, and it moves from the ini.
    /// </summary>
    internal sealed class Takeover
    {
        // ---- where and when -----------------------------------------------------

        /// <summary>
        /// The junction, read off the screen while stood in the middle of it.
        ///
        /// Not a coordinate from a map. Somebody walked to the centre of that intersection and
        /// to the kerb the crowd stands on, and the two readings are what set the radius below.
        /// </summary>
        private static readonly Vector3 Middle = new Vector3(-126.840f, -1737.201f, 30.135f);

        /// <summary>
        /// How far out the ring stands. Measured between those two readings: 19.1 metres.
        ///
        /// It was asked for as fifteen. The two points given are nineteen apart, and the points
        /// are the better evidence -- one of them is somebody stood where they wanted the crowd.
        /// In the ini either way.
        /// </summary>
        private const float RingAt = 19f;

        /// <summary>
        /// And how far in the cars actually work. Fifteen, as asked for.
        ///
        /// FOUR METRES OF CLEARANCE, which is the thing to know about this number. The ring is
        /// at nineteen, so a car whose back end steps a car's width wide of its line is into
        /// the front row -- and the whole point of reduced grip is that the back end does
        /// exactly that. It is what was wanted and it is tuned from the ini, so if the crowd
        /// starts getting clipped, this is the number that moves.
        /// </summary>
        private const float DriftMin = 14f;
        private const float DriftMax = 15.5f;

        /// <summary>The earliest and latest it starts, on the game's own clock.</summary>
        private const int FromHour = 21;
        private const int ToHour = 4;

        /// <summary>How many in-game hours it runs for before the law turns up.</summary>
        private const float LastsHours = 3f;

        /// <summary>Close enough for it to be worth existing at all.</summary>
        private const float NearEnough = 190f;

        /// <summary>And far enough that it is packed away again.</summary>
        private const float LetGo = 260f;

        // ---- the crowd ----------------------------------------------------------

        private const int CrowdMin = 16;
        private const int CrowdMax = 24;

        /// <summary>Watching. Half of them are filming it, which is what the phone one is.</summary>
        private static readonly string[] Watching =
        {
            "WORLD_HUMAN_STAND_MOBILE", "WORLD_HUMAN_STAND_MOBILE_UPRIGHT",
            "WORLD_HUMAN_STAND_IMPATIENT", "WORLD_HUMAN_DRINKING",
            "WORLD_HUMAN_SMOKING", "WORLD_HUMAN_CHEERING"
        };

        /// <summary>Who turns up. Everybody, not one set -- this is not a gang thing.</summary>
        private static readonly string[] Faces =
        {
            "a_m_y_soucent_01", "a_m_y_soucent_02", "a_m_y_soucent_03",
            "a_f_y_soucent_01", "a_f_y_soucent_02", "a_m_y_hipster_01",
            "a_m_y_latino_01", "a_m_y_ktown_01", "a_f_y_hipster_02",
            "a_m_y_stwhi_01", "a_m_y_downtown_01", "a_f_y_genhot_01"
        };

        /// <summary>What gets put sideways, in the order they exist.</summary>
        private static readonly string[] Drifters =
        {
            "driftdominator10", "driftgauntlet4", "driftchavosv6", "driftfr36",
            "driftremus", "driftfuto", "dominator", "buffalo3", "sultan", "futo"
        };

        /// <summary>And what everybody else turned up in, parked outside the ring.</summary>
        private static readonly string[] Parked =
        {
            "asterope2", "dorado", "kanjosj", "s95", "vorschlaghammer", "sultan2",
            "warrener", "faction", "voodoo", "primo2", "buccaneer2"
        };

        private const int ParkedMin = 5;
        private const int ParkedMax = 9;

        // ---- state --------------------------------------------------------------

        private sealed class Runner
        {
            public Vehicle Car;
            public Ped Driver;

            /// <summary>Where it is on the circle, and how big a circle.</summary>
            public double Angle;
            public float Radius;
            public float Speed;

            /// <summary>Which way round, and when it has had its go.</summary>
            public int Way;
            public int Until;
        }

        private readonly Settings _cfg;
        private readonly Random _rng = new Random();

        private readonly List<Ped> _crowd = new List<Ped>();
        private readonly List<Vehicle> _parked = new List<Vehicle>();
        private readonly List<Runner> _running = new List<Runner>();

        private Vehicle _law;
        private Ped _cop;

        /// <summary>Off while something louder is happening.</summary>
        public Func<bool> Busy;

        public TakeoverState State { get; private set; }

        /// <summary>The in-game day this was last scheduled for, and the hour it starts.</summary>
        private int _plannedFor = -1;
        private int _startsAt = -1;

        /// <summary>The in-game minute it ends. See OwnedCars.NowMinutes for the clock.</summary>
        private int _endsAt;

        private int _lastTick;
        private int _lastDrive;

        private const int TickMs = 900;

        private bool Enabled => _cfg == null || _cfg.TakeoverEnabled;

        private float Ring => _cfg == null || _cfg.TakeoverRadius <= 1f ? RingAt : _cfg.TakeoverRadius;

        public Takeover(Settings cfg)
        {
            _cfg = cfg;
        }

        // ---- per-tick -----------------------------------------------------------

        public void Update()
        {
            var now = Game.GameTime;

            // The circle is driven every frame. Everything else is a decision and can wait.
            if (State == TakeoverState.Running) Circle();

            if (now - _lastTick < TickMs) return;
            _lastTick = now;

            try
            {
                if (!Enabled)
                {
                    if (State != TakeoverState.None) Pack();
                    return;
                }

                var player = Game.Player.Character;
                if (player == null || !player.Exists() || !player.IsAlive) return;

                Plan();

                var near = player.Position.DistanceTo(Middle);

                switch (State)
                {
                    case TakeoverState.None:
                        if (Busy != null && Busy()) return;
                        if (!Tonight()) return;
                        if (near > NearEnough) return;

                        Begin();
                        break;

                    case TakeoverState.Running:
                        if (near > LetGo) { Pack(); return; }

                        if (OwnedCars.NowMinutes() >= _endsAt) { Blues(); return; }

                        Keep(now);
                        break;

                    case TakeoverState.Scattering:
                        if (near > LetGo || now > _lastDrive) Pack();
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Takeover tripped: " + ex.Message);
                Pack();
            }
        }

        // ---- the diary ----------------------------------------------------------

        /// <summary>
        /// Picks tonight's hour, once per day.
        ///
        /// Nine at night to four in the morning, which wraps midnight -- so the hour is picked
        /// out of a seven-long run starting at nine and folded back round, rather than out of a
        /// range that would have to be two ranges.
        /// </summary>
        private void Plan()
        {
            int day;

            try { day = Function.Call<int>(Hash.GET_CLOCK_DAY_OF_MONTH); }
            catch { return; }

            if (day == _plannedFor) return;

            _plannedFor = day;

            var span = (24 - FromHour) + ToHour;
            _startsAt = (FromHour + _rng.Next(span)) % 24;

            Log.Info("Takeover: tonight's is at " + _startsAt + ":00.");
        }

        /// <summary>Whether the clock is inside tonight's window.</summary>
        private bool Tonight()
        {
            if (_startsAt < 0) return false;

            try
            {
                var h = Function.Call<int>(Hash.GET_CLOCK_HOURS);

                // The window wraps, so "after the start" is not a single comparison.
                var since = (h - _startsAt + 24) % 24;

                return since < (int)Math.Ceiling(LastsHours);
            }
            catch
            {
                return false;
            }
        }

        // ---- setting up ---------------------------------------------------------

        private void Begin()
        {
            Crowd();

            if (_crowd.Count == 0) return;

            Cars();

            State = TakeoverState.Running;
            _endsAt = OwnedCars.NowMinutes() + (int)(LastsHours * 60f);

            Log.Info("Takeover: on, " + _crowd.Count + " watching.");
        }

        /// <summary>The ring, every one of them turned to face the middle.</summary>
        private void Crowd()
        {
            var want = _rng.Next(CrowdMin, CrowdMax + 1);

            for (var i = 0; i < want; i++)
            {
                try
                {
                    // Spread round the whole circle with a bit of slop, so it is a crowd and
                    // not a fence.
                    var a = (i / (double)want) * Math.PI * 2d + (_rng.NextDouble() - 0.5) * 0.18;
                    var r = Ring + (float)(_rng.NextDouble() * 3.0 - 1.0);

                    var at = Ground(new Vector3(
                        Middle.X + (float)Math.Cos(a) * r,
                        Middle.Y + (float)Math.Sin(a) * r,
                        Middle.Z));

                    var name = Faces[_rng.Next(Faces.Length)];

                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(900)) continue;

                    var handle = Function.Call<int>(Hash.CREATE_PED, 4, model.Hash,
                                                    at.X, at.Y, at.Z, Facing(at), false, false);

                    model.MarkAsNoLongerNeeded();
                    if (handle == 0) continue;

                    var ped = Entity.FromHandle(handle) as Ped;
                    if (ped == null || !ped.Exists()) continue;

                    ped.IsPersistent = true;

                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, ped.Handle, true, true);

                    // THEY DO NOT REACT TO THE CARS. Without this every one of them dives out
                    // of the way of a vehicle that was never going to hit them, and the ring
                    // turns into a panic within seconds of the first car going sideways.
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, true);
                    Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, ped.Handle, false);

                    Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, ped.Handle,
                                  Watching[_rng.Next(Watching.Length)], 0, true);

                    _crowd.Add(ped);
                }
                catch (Exception ex)
                {
                    Log.Debug("Takeover could not add somebody: " + ex.Message);
                }
            }
        }

        /// <summary>What they came in, left outside the ring.</summary>
        private void Cars()
        {
            var want = _rng.Next(ParkedMin, ParkedMax + 1);

            for (var i = 0; i < want; i++)
            {
                try
                {
                    var a = _rng.NextDouble() * Math.PI * 2d;
                    var r = Ring + 5f + (float)(_rng.NextDouble() * 7.0);

                    var at = new Vector3(Middle.X + (float)Math.Cos(a) * r,
                                         Middle.Y + (float)Math.Sin(a) * r, Middle.Z);

                    var car = Make(Parked, at);
                    if (car == null) continue;

                    // Nose in, which is how anybody parks at one of these.
                    car.Heading = Facing(at);

                    Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, car.Handle);
                    Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, car.Handle, 2);

                    _parked.Add(car);
                }
                catch (Exception ex)
                {
                    Log.Debug("Takeover could not park one: " + ex.Message);
                }
            }
        }

        // ---- the circle ---------------------------------------------------------

        /// <summary>Keeps one or two of them going, and swaps them out when their go is up.</summary>
        private void Keep(int now)
        {
            for (var i = _running.Count - 1; i >= 0; i--)
            {
                var r = _running[i];

                var dead = r.Car == null || !r.Car.Exists()
                           || r.Driver == null || !r.Driver.Exists() || !r.Driver.IsAlive;

                if (!dead && now < r.Until) continue;

                Out(r);
                _running.RemoveAt(i);
            }

            var want = _rng.Next(100) < 45 ? 2 : 1;

            while (_running.Count < want)
            {
                if (!In(now)) break;
            }
        }

        /// <summary>Somebody takes their turn.</summary>
        private bool In(int now)
        {
            try
            {
                var radius = DriftMin + (float)_rng.NextDouble() * (DriftMax - DriftMin);
                var angle = _rng.NextDouble() * Math.PI * 2d;

                var at = new Vector3(Middle.X + (float)Math.Cos(angle) * radius,
                                     Middle.Y + (float)Math.Sin(angle) * radius, Middle.Z);

                var car = Make(Drifters, at);
                if (car == null) return false;

                Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, car.Handle);

                // THE SLIDEY WHEELS. This is the whole look -- a car driven round a circle with
                // full grip is a car going round a roundabout.
                Function.Call(Hash.SET_VEHICLE_REDUCE_GRIP, car.Handle, true);
                Function.Call(Hash.SET_VEHICLE_ENGINE_ON, car.Handle, true, true, false);

                var driver = Behind(car);

                if (driver == null)
                {
                    car.Delete();
                    return false;
                }

                var r = new Runner
                {
                    Car = car,
                    Driver = driver,
                    Angle = angle,
                    Radius = radius,
                    Speed = 9f + (float)_rng.NextDouble() * 5f,
                    Way = _rng.Next(2) == 0 ? 1 : -1,
                    Until = now + _rng.Next(22000, 48000)
                };

                _running.Add(r);
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Takeover could not send one in: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// One frame of circle work, per car.
        ///
        /// The angle is advanced, the car is pointed at the tangent of where it now is, and it
        /// is pushed forward. Grip is already down, so the back end runs wide of the line the
        /// nose is taking -- which is the difference between driving a circle and drifting one.
        ///
        /// Heading is LERPED rather than set. Snapping it every frame would hold the car
        /// perfectly on the line and look like it was on rails; letting it chase the tangent
        /// leaves it always slightly behind, which is the angle.
        /// </summary>
        private void Circle()
        {
            for (var i = _running.Count - 1; i >= 0; i--)
            {
                var r = _running[i];

                if (r.Car == null || !r.Car.Exists()) continue;

                try
                {
                    r.Angle += r.Way * (r.Speed / Math.Max(2f, r.Radius)) * 0.016;

                    var tangent = r.Angle + r.Way * Math.PI * 0.5;

                    var want = (float)((Math.Atan2(Math.Sin(tangent), Math.Cos(tangent))
                                        * 180.0 / Math.PI));

                    // The game's headings run the other way round from atan2's, and from north.
                    want = (90f - want + 360f) % 360f;

                    var have = r.Car.Heading;
                    var turn = ((want - have + 540f) % 360f) - 180f;

                    Function.Call(Hash.SET_ENTITY_HEADING, r.Car.Handle,
                                  (have + turn * 0.25f + 360f) % 360f);

                    Function.Call(Hash.SET_VEHICLE_FORWARD_SPEED, r.Car.Handle, r.Speed);

                    // Smoke, in bursts rather than constantly. A car that is permanently on the
                    // limiter is a car nobody is driving.
                    Function.Call(Hash.SET_VEHICLE_BURNOUT, r.Car.Handle,
                                  (Game.GameTime / 1400) % 3 == 0);
                }
                catch
                {
                    // Next frame.
                }
            }
        }

        /// <summary>Their go is over. They drive out rather than vanishing.</summary>
        private void Out(Runner r)
        {
            try
            {
                if (r.Car != null && r.Car.Exists())
                {
                    Function.Call(Hash.SET_VEHICLE_BURNOUT, r.Car.Handle, false);
                    Function.Call(Hash.SET_VEHICLE_REDUCE_GRIP, r.Car.Handle, false);

                    if (r.Driver != null && r.Driver.Exists())
                    {
                        Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, r.Driver.Handle,
                                      r.Car.Handle, 20f, 786603);
                    }

                    r.Car.IsPersistent = false;
                    r.Car.MarkAsNoLongerNeeded();
                }

                if (r.Driver != null && r.Driver.Exists())
                {
                    r.Driver.IsPersistent = false;
                    r.Driver.MarkAsNoLongerNeeded();
                }
            }
            catch
            {
                // It leaves either way.
            }
        }

        // ---- the law ------------------------------------------------------------

        /// <summary>
        /// Blue lights, and then there is nobody there.
        ///
        /// One car. It is not a raid and nobody is arrested -- the point of the police here is
        /// that they end it, and one set of lights at the end of a street empties a junction
        /// faster than anything else that could be written.
        /// </summary>
        private void Blues()
        {
            State = TakeoverState.Scattering;
            _lastDrive = Game.GameTime + 20000;

            try
            {
                var at = new Vector3(Middle.X, Middle.Y - 55f, Middle.Z);

                _law = Make(new[] { "police3", "police", "police2" }, at);

                if (_law != null && _law.Exists())
                {
                    Function.Call(Hash.SET_VEHICLE_SIREN, _law.Handle, true);
                    _cop = Behind(_law);

                    if (_cop != null && _cop.Exists())
                    {
                        Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, _cop.Handle, _law.Handle,
                                      Middle.X, Middle.Y, Middle.Z, 18f, 0,
                                      _law.Model.Hash, 786603, 8f, true);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Takeover: no police car: " + ex.Message);
            }

            foreach (var ped in _crowd)
            {
                if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

                try
                {
                    // Off, so they are allowed to react to anything again -- which is the whole
                    // of scattering. They have been deaf to the world all night on purpose.
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);

                    Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);
                    Function.Call(Hash.TASK_SMART_FLEE_COORD, ped.Handle,
                                  Middle.X, Middle.Y, Middle.Z, 220f, -1, false, false);
                }
                catch
                {
                    // He runs or he does not.
                }
            }

            foreach (var r in _running) Out(r);
            _running.Clear();

            Log.Info("Takeover: police. Everybody gone.");
        }

        // ---- making things ------------------------------------------------------

        private Vehicle Make(string[] names, Vector3 at)
        {
            foreach (var name in names)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1200)) continue;

                    var car = World.CreateVehicle(model, at);
                    model.MarkAsNoLongerNeeded();

                    if (car == null || !car.Exists()) continue;

                    car.IsPersistent = true;
                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, car.Handle, true, true);

                    return car;
                }
                catch
                {
                    // Next name.
                }
            }

            return null;
        }

        private Ped Behind(Vehicle car)
        {
            try
            {
                var name = Faces[_rng.Next(Faces.Length)];

                var model = new Model(name);
                if (!model.IsValid || !model.Request(1200)) return null;

                var handle = Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE, car.Handle,
                                                4, model.Hash, -1, false, false);

                model.MarkAsNoLongerNeeded();
                if (handle == 0) return null;

                var ped = Entity.FromHandle(handle) as Ped;
                if (ped == null || !ped.Exists()) return null;

                ped.IsPersistent = true;

                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, ped.Handle, true, true);
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, true);
                Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, ped.Handle, false);

                return ped;
            }
            catch (Exception ex)
            {
                Log.Debug("Takeover could not find a driver: " + ex.Message);
                return null;
            }
        }

        /// <summary>The heading that points something at the middle.</summary>
        private static float Facing(Vector3 from)
        {
            var dx = Middle.X - from.X;
            var dy = Middle.Y - from.Y;

            return (float)((Math.Atan2(dx, dy) * 180.0 / Math.PI + 360.0) % 360.0);
        }

        private static Vector3 Ground(Vector3 at)
        {
            try
            {
                float z;

                if (World.GetGroundHeight(new Vector3(at.X, at.Y, at.Z + 2f), out z,
                                          GetGroundHeightMode.Normal))
                {
                    return new Vector3(at.X, at.Y, z);
                }
            }
            catch
            {
                // The given height will do; it came off the road in the first place.
            }

            return at;
        }

        // ---- putting it away ----------------------------------------------------

        private void Pack()
        {
            foreach (var r in _running) Out(r);
            _running.Clear();

            foreach (var ped in _crowd)
            {
                try
                {
                    if (ped == null || !ped.Exists()) continue;

                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);

                    ped.IsPersistent = false;
                    ped.MarkAsNoLongerNeeded();
                }
                catch
                {
                    // Already gone.
                }
            }

            _crowd.Clear();

            foreach (var car in _parked)
            {
                try
                {
                    if (car == null || !car.Exists()) continue;

                    car.IsPersistent = false;
                    car.MarkAsNoLongerNeeded();
                }
                catch
                {
                    // Already gone.
                }
            }

            _parked.Clear();

            try
            {
                if (_cop != null && _cop.Exists())
                {
                    _cop.IsPersistent = false;
                    _cop.MarkAsNoLongerNeeded();
                }

                if (_law != null && _law.Exists())
                {
                    _law.IsPersistent = false;
                    _law.MarkAsNoLongerNeeded();
                }
            }
            catch
            {
                // Already gone.
            }

            _cop = null;
            _law = null;

            State = TakeoverState.None;
        }

        /// <summary>Teardown. Nothing of a party is left standing in a junction.</summary>
        public void RestoreWorld()
        {
            try
            {
                foreach (var ped in _crowd) { if (ped != null && ped.Exists()) ped.Delete(); }
                foreach (var car in _parked) { if (car != null && car.Exists()) car.Delete(); }

                foreach (var r in _running)
                {
                    if (r.Driver != null && r.Driver.Exists()) r.Driver.Delete();
                    if (r.Car != null && r.Car.Exists()) r.Car.Delete();
                }

                if (_cop != null && _cop.Exists()) _cop.Delete();
                if (_law != null && _law.Exists()) _law.Delete();
            }
            catch
            {
                // Teardown.
            }

            _crowd.Clear();
            _parked.Clear();
            _running.Clear();

            _cop = null;
            _law = null;

            State = TakeoverState.None;
        }
    }
}
