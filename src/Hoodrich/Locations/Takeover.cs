using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Social;

namespace Hoodrich.Locations
{
    /// <summary>Where the night is up to.</summary>
    internal enum TakeoverState
    {
        None,
        Running,
        Scattering
    }

    /// <summary>
    /// The takeover.
    ///
    /// One intersection, one night, some time between nine and four. Cars and people come in
    /// from the surrounding streets, a ring forms facing inwards, and one or two cars work the
    /// inside of it sideways. It runs for hours and ends with blue lights.
    ///
    /// IT IS NOT A MISSION. No call, no blip, nothing asked of you -- it happens whether you go
    /// or not, and the whole value of it is driving past at two in the morning and finding it.
    ///
    /// NOBODY APPEARS. Every person walks in and every car drives in, from out of sight, to a
    /// place picked for them before they existed. That is the most expensive decision in this
    /// file and the one that makes it read as people turning up rather than a set being
    /// dressed: a crowd that pops into being at a junction is a crowd nobody believes, however
    /// many of them there are.
    ///
    /// THE CIRCLE IS DRIVEN, NOT TASKED. No native asks a driver to hold a donut at a radius
    /// around a point -- the closest are the burnout actions, which go wherever the car is
    /// pointing for however long you name and cannot be aimed. So a car that has ARRIVED is
    /// taken off its task and pushed round by hand: an angle that advances, a heading chasing
    /// the tangent, forward speed, grip turned down underneath. Coming in and going out it is
    /// an ordinary driver on an ordinary route, which is how it gets through the gap in the
    /// ring in the first place.
    ///
    /// AND THE DRIVERS DO NOT PANIC. Flinching at a gunshot, fleeing a collision, treating a
    /// crowd as something to escape -- every one of those is correct for a driver in traffic
    /// and catastrophic here, and the visible symptom of leaving them on is a car abandoning
    /// its own donut and tearing off through the spectators.
    /// </summary>
    internal sealed class Takeover
    {
        // ---- where and when -----------------------------------------------------

        /// <summary>The junction, read off the screen while stood in the middle of it.</summary>
        private static readonly Vector3 Middle = new Vector3(-126.840f, -1737.201f, 30.135f);

        /// <summary>How far out the ring stands. Measured on the ground: 19.1 metres.</summary>
        private const float RingAt = 19f;

        /// <summary>
        /// And how far in the cars work. Ten, tightened from fifteen.
        ///
        /// Nine metres of clearance to the ring rather than four, which is the number that
        /// matters: the back end of a car on reduced grip steps a long way wide of the line the
        /// nose is taking, and at four the front row was inside that. A tighter circle is also
        /// a faster-looking one -- the same speed round a smaller radius is more lock, more
        /// angle and more smoke in one place.
        /// </summary>
        private const float DriftMin = 9f;
        private const float DriftMax = 10.5f;

        /// <summary>How long the one on the mark gets before somebody else has a go.</summary>
        private const int BurnMinMs = 26000;
        private const int BurnMaxMs = 36000;

        /// <summary>Close enough to the mark to stop and start smoking.</summary>
        private const float OnTheMark = 4.5f;

        /// <summary>How fast he turns on the spot while he does it.</summary>
        private const float SpinRate = 62f;

        private const int FromHour = 21;
        private const int ToHour = 4;
        private const float LastsHours = 3f;

        private const float NearEnough = 200f;
        private const float LetGo = 300f;

        /// <summary>Where people and cars come FROM, which is never the junction itself.</summary>
        private const float WalkFromMin = 55f;
        private const float WalkFromMax = 130f;

        /// <summary>
        /// A block over, and that is the floor rather than a suggestion.
        ///
        /// It was ninety, which on these streets is the far side of one junction -- close
        /// enough that a car for the takeover could appear in the same shot as the takeover.
        /// The whole reason everything drives in is so that nothing is seen arriving out of
        /// nowhere, and a spawn radius that fits inside the draw distance gives that away.
        /// </summary>
        private const float DriveFromMin = 125f;
        private const float DriveFromMax = 230f;

        /// <summary>
        /// How far out ordinary traffic is talked down.
        ///
        /// A junction full of people is a junction the driving AI has no idea what to do with:
        /// a ped in the road is an obstacle, a car sideways in front of it is a threat, and the
        /// response to both is to get out of there -- which at speed, through a crowd, is the
        /// worst thing that can happen at one of these. So anybody driving near it is made
        /// patient for as long as they are near it, and given themselves back when they leave.
        /// </summary>
        private const float CalmRange = 50f;

        /// <summary>Close enough to their place to stop and turn round.</summary>
        private const float ArrivedRange = 3.5f;
        private const float CarArrivedRange = 7f;

        // ---- the crowd ----------------------------------------------------------

        private const int CrowdMin = 28;
        private const int CrowdMax = 40;

        /// <summary>How many set off at once, so it fills up rather than materialising.</summary>
        private const int PerWave = 4;
        private const int WaveGapMs = 2600;

        private static readonly string[] Watching =
        {
            "WORLD_HUMAN_STAND_MOBILE", "WORLD_HUMAN_STAND_MOBILE_UPRIGHT",
            "WORLD_HUMAN_STAND_IMPATIENT", "WORLD_HUMAN_DRINKING",
            "WORLD_HUMAN_SMOKING", "WORLD_HUMAN_CHEERING"
        };

        private static readonly string[] Faces =
        {
            "a_m_y_soucent_01", "a_m_y_soucent_02", "a_m_y_soucent_03", "a_m_y_soucent_04",
            "a_f_y_soucent_01", "a_f_y_soucent_02", "a_m_y_hipster_01", "a_m_m_soucent_01",
            "a_m_y_latino_01", "a_m_y_ktown_01", "a_f_y_hipster_02", "a_m_y_dhill_01",
            "a_m_y_stwhi_01", "a_m_y_downtown_01", "a_f_y_genhot_01", "a_m_y_beach_01"
        };

        /// <summary>
        /// What gets put sideways.
        ///
        /// The Hellcats and the Mustangs are the Gauntlets and the Dominators -- those are the
        /// cars this game has for those cars, and the numbered variants are the hotted-up ones.
        /// The drift* models lead where an install has them and everything after is a car every
        /// install has, so nobody ends up with an empty circle.
        /// </summary>
        private static readonly string[] Drifters =
        {
            "driftdominator10", "driftgauntlet4", "driftchavosv6", "driftfr36", "driftremus",
            "gauntlet3", "gauntlet4", "gauntlet5",
            "dominator3", "dominator7", "dominator8",
            "dominator", "buffalo3", "sultan", "futo"
        };

        /// <summary>Cars that came to watch, parked outside the ring.</summary>
        private static readonly string[] Parked =
        {
            "asterope2", "dorado", "kanjosj", "s95", "vorschlaghammer", "sultan2",
            "warrener", "faction", "primo2", "gauntlet", "dominator", "buffalo"
        };

        /// <summary>
        /// And the ones on juice.
        ///
        /// Hydraulics are a real system in this game and these are the cars that have them. A
        /// bouncing Asterope would be a bouncing bug -- SET_CAN_USE_HYDRAULICS on something
        /// without them does nothing, and the raise factor would be driven all night for free.
        /// </summary>
        private static readonly string[] Lows =
        {
            "voodoo", "buccaneer2", "chino2", "faction2", "moonbeam2",
            "slamvan3", "sabregt2", "virgo2", "tornado5", "minivan2"
        };

        private const int ParkedMin = 7;
        private const int ParkedMax = 12;
        private const int LowsMin = 2;
        private const int LowsMax = 4;

        // ---- what is out there --------------------------------------------------

        private sealed class Watcher
        {
            public Ped Man;
            public Vector3 Slot;
            public bool There;

            /// <summary>When they were first noticed away from their spot, or nought.</summary>
            public int Away;
        }

        private sealed class Parkee
        {
            public Vehicle Car;
            public Ped Driver;
            public Vector3 Slot;
            public bool There;

            public bool Low;
            public double Hop;
            public double Rate;
        }

        private sealed class Runner
        {
            public Vehicle Car;
            public Ped Driver;

            public double Angle;
            public float Radius;
            public float Speed;
            public int Way;
            public int Until;

            public bool Circling;
            public bool Leaving;

            /// <summary>
            /// This one is on the mark rather than going round it.
            ///
            /// The two are the same object because they have the same life -- drive in, do the
            /// thing, drive out -- and the only difference is what "the thing" is. Splitting
            /// them into two classes would duplicate the arrival, the timeout and every line
            /// of the cleanup to change one method.
            /// </summary>
            public bool Middle;
        }

        private readonly Settings _cfg;
        private readonly Random _rng = new Random();

        private readonly List<Watcher> _crowd = new List<Watcher>();
        private readonly List<Parkee> _parked = new List<Parkee>();
        private readonly List<Runner> _running = new List<Runner>();

        private sealed class Law
        {
            public Vehicle Car;
            public Ped Cop;
        }

        private readonly List<Law> _law = new List<Law>();

        /// <summary>How many turn up, and how far out they start.</summary>
        private const int Units = 3;
        private const float LawFrom = 150f;

        /// <summary>Close enough for the junction to notice them.</summary>
        private const float LawSeen = 70f;

        public Func<bool> Busy;

        /// <summary>Set by Main: the feed, so the block can talk about it.</summary>
        public SocialFeed Social;

        public TakeoverState State { get; private set; }

        private int _plannedFor = -1;
        private int _startsAt = -1;
        private int _endsAt;

        private int _lastTick;
        private int _lastDrive;

        private bool _scattered;
        private int _toCome;
        private int _nextWave;
        private int _nextWord;

        private const int TickMs = 700;
        private const int WordMinMs = 55000;
        private const int WordMaxMs = 130000;

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

            // Per frame: the circle and the hydraulics. Both are physics driven by hand and
            // both read as a stutter at anything less.
            if (State == TakeoverState.Running)
            {
                Circle();
                Bounce();
            }

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

                        Begin(now);
                        break;

                    case TakeoverState.Running:
                        if (near > LetGo) { Pack(); return; }
                        if (OwnedCars.NowMinutes() >= _endsAt) { Blues(); return; }

                        Calm();
                        Wave(now);
                        Walking();
                        Parking();
                        Keep(now);
                        Chatter(now);
                        break;

                    case TakeoverState.Scattering:
                        // The police are driving in. Nothing runs until one of them is close
                        // enough to be worth running from.
                        if (!_scattered && Closing())
                        {
                            _scattered = true;
                            Scatter();
                        }

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

        private bool Tonight()
        {
            if (_startsAt < 0) return false;

            try
            {
                var h = Function.Call<int>(Hash.GET_CLOCK_HOURS);
                var since = (h - _startsAt + 24) % 24;

                return since < (int)Math.Ceiling(LastsHours);
            }
            catch
            {
                return false;
            }
        }

        // ---- starting -----------------------------------------------------------

        private void Begin(int now)
        {
            State = TakeoverState.Running;

            _endsAt = OwnedCars.NowMinutes() + (int)(LastsHours * 60f);
            _toCome = _rng.Next(CrowdMin, CrowdMax + 1);
            _nextWave = now;
            _nextWord = now + _rng.Next(20000, 45000);

            Cars();

            if (Social != null) Social.On(SocialEvent.Takeover);

            Log.Info("Takeover: on. " + _toCome + " on their way.");
        }

        /// <summary>People set off in small lots rather than all at once.</summary>
        private void Wave(int now)
        {
            if (_toCome <= 0 || now < _nextWave) return;

            _nextWave = now + WaveGapMs;

            var want = Math.Min(PerWave, _toCome);

            // Counted down whether or not the spawn succeeded. A failure is a person who did
            // not come, and retrying forever would have the mod hammering the pavement finder
            // for the rest of the night on a junction where it cannot find one.
            for (var i = 0; i < want; i++)
            {
                Somebody();
                _toCome--;
            }
        }

        /// <summary>
        /// One person, put down out of sight and told to walk to their place in the ring.
        ///
        /// The slot is picked FIRST and the spawn point is chosen to be away from it, which is
        /// the right way round: everybody has somewhere to be before they exist, so the ring
        /// fills evenly instead of clumping wherever the spawner happened to succeed.
        /// </summary>
        private bool Somebody()
        {
            try
            {
                var a = _rng.NextDouble() * Math.PI * 2d;
                var r = Ring + (float)(_rng.NextDouble() * 3.5 - 1.2);

                var slot = Ground(new Vector3(Middle.X + (float)Math.Cos(a) * r,
                                              Middle.Y + (float)Math.Sin(a) * r, Middle.Z));

                var from = OnFoot(slot);
                if (from == Vector3.Zero) return false;

                var name = Faces[_rng.Next(Faces.Length)];

                var model = new Model(name);
                if (!model.IsValid || !model.IsInCdImage || !model.Request(900)) return false;

                var handle = Function.Call<int>(Hash.CREATE_PED, 4, model.Hash,
                                                from.X, from.Y, from.Z, 0f, false, false);

                model.MarkAsNoLongerNeeded();
                if (handle == 0) return false;

                var ped = Entity.FromHandle(handle) as Ped;
                if (ped == null || !ped.Exists()) return false;

                ped.IsPersistent = true;

                var h = ped.Handle;

                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, h, true, true);
                Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, h, false);

                // THEY REACT NOW, and that is a reversal of what was here before.
                //
                // Being deaf to the world kept the ring perfectly still, which is also what
                // made it a diorama -- a car sliding four metres from your feet and nobody so
                // much as turning their head is the one thing at a takeover that could not
                // happen. So they flinch, and they duck, and some of them run.
                //
                // What stops that emptying the junction is not the ped -- it is Walking()
                // below, which notices anybody who has left their spot and walks them back.
                // They are allowed to bolt; they are not allowed to keep going.
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, h, false);

                Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, h,
                              slot.X, slot.Y, slot.Z, 1.2f, -1, 1.5f, true, 0f);

                Function.Call(Hash.SET_PED_KEEP_TASK, h, true);

                _crowd.Add(new Watcher { Man = ped, Slot = slot });
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Takeover could not send somebody: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Anybody who has reached their place stops and turns to face the middle.
        ///
        /// AND IS TURNED BACK. A scenario picks its own heading and several of these rotate a
        /// ped as they play, so a ring faced inwards once is a ring facing every which way a
        /// minute later. One comparison and one call per person per tick, and it is the
        /// difference between a crowd watching something and a crowd standing near it.
        /// </summary>
        /// <summary>How far he may drift before he is walked back, and how long he gets first.</summary>
        private const float StrayRange = 9f;
        private const int LetHimRunMs = 4000;

        private void Walking()
        {
            var now = Game.GameTime;

            foreach (var w in _crowd)
            {
                if (w.Man == null || !w.Man.Exists() || !w.Man.IsAlive) continue;

                if (!w.There)
                {
                    if (w.Man.Position.DistanceTo(w.Slot) > ArrivedRange) continue;

                    w.There = true;
                    w.Away = 0;

                    try
                    {
                        Function.Call(Hash.CLEAR_PED_TASKS, w.Man.Handle);
                        Function.Call(Hash.SET_ENTITY_HEADING, w.Man.Handle, Facing(w.Man.Position));

                        Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, w.Man.Handle,
                                      Watching[_rng.Next(Watching.Length)], 0, true);

                        // AND AGAIN AFTER THE SCENARIO, not only before it. A scenario picks
                        // its own facing when it starts, so a heading set first is a heading
                        // thrown away -- which is why the ring kept coming out pointing every
                        // which way however carefully each man was placed.
                        Function.Call(Hash.SET_ENTITY_HEADING, w.Man.Handle, Facing(w.Man.Position));
                    }
                    catch
                    {
                        // He stands there either way.
                    }

                    continue;
                }

                // SPOOKED, AND THEN BACK. He is allowed to jump out of the way of something --
                // that is the whole reason he can hear the world now -- but a takeover where
                // one loud noise empties the pavement is a takeover that ends itself.
                //
                // Given a few seconds to have his reaction before anybody interferes with it.
                // Pulling him back the instant he moves would cancel the flinch mid-animation
                // and read as a man being dragged, which is worse than not flinching at all.
                var strayed = w.Man.Position.DistanceTo(w.Slot);

                if (strayed > StrayRange)
                {
                    if (w.Away == 0)
                    {
                        w.Away = now;
                        continue;
                    }

                    if (now - w.Away < LetHimRunMs) continue;

                    w.There = false;
                    w.Away = 0;

                    try
                    {
                        Function.Call(Hash.CLEAR_PED_TASKS, w.Man.Handle);

                        Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, w.Man.Handle,
                                      w.Slot.X, w.Slot.Y, w.Slot.Z, 1.6f, -1, 1.5f, true, 0f);

                        Function.Call(Hash.SET_PED_KEEP_TASK, w.Man.Handle, true);
                    }
                    catch
                    {
                        // He wanders back or he does not.
                    }

                    continue;
                }

                w.Away = 0;

                try
                {
                    // TIGHTER THAN IT WAS. Twenty-five degrees of slack is a third of the
                    // way to standing side-on, and across thirty people that reads as a crowd
                    // milling rather than a crowd watching. Ten is close enough to inwards that
                    // the whole ring points at the same thing.
                    var want = Facing(w.Man.Position);
                    var have = w.Man.Heading;
                    var off = Math.Abs(((want - have + 540f) % 360f) - 180f);

                    if (off > 10f) Function.Call(Hash.SET_ENTITY_HEADING, w.Man.Handle, want);
                }
                catch
                {
                    // Next tick.
                }
            }
        }

        /// <summary>
        /// Ordinary traffic near the junction, talked down.
        ///
        /// Everybody in a car within the radius who is not the player and not one of ours: no
        /// aggression, no panic, and a speed that suits a road with forty people stood in it.
        /// Re-applied every tick rather than once, because these are cars the game is streaming
        /// in and out constantly -- the one that just arrived is exactly the one that has not
        /// been told yet.
        ///
        /// THE PLAYER IS NOT TOUCHED. Taking the aggression off the person driving would be
        /// the mod deciding how they get to drive, which is not its business.
        /// </summary>
        private void Calm()
        {
            try
            {
                var player = Game.Player.Character;
                var mine = player != null && player.Exists() && player.CurrentVehicle != null
                           && player.CurrentVehicle.Exists()
                    ? player.CurrentVehicle.Handle
                    : 0;

                foreach (var car in World.GetNearbyVehicles(Middle, CalmRange))
                {
                    if (car == null || !car.Exists()) continue;
                    if (mine != 0 && car.Handle == mine) continue;

                    var driver = car.Driver;

                    if (driver == null || !driver.Exists() || !driver.IsAlive) continue;
                    if (driver.IsPlayer) continue;

                    // One of ours is already calm and already has a job. Telling it to slow
                    // down would take the circle apart.
                    if (Ours(driver)) continue;

                    var h = driver.Handle;

                    Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, h, 0.0f);
                    Function.Call(Hash.SET_DRIVER_ABILITY, h, 1.0f);
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, h, true);
                    Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, h, 0, false);

                    // Crawling pace. Not a stop -- a road that nobody can drive down at all
                    // backs traffic up for half a district and that is its own kind of wrong.
                    Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, h, 6f);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Takeover could not calm the traffic: " + ex.Message);
            }
        }

        /// <summary>Whether this driver is one this file put there.</summary>
        private bool Ours(Ped who)
        {
            foreach (var r in _running)
            {
                if (r.Driver != null && r.Driver.Exists() && r.Driver.Handle == who.Handle) return true;
            }

            foreach (var p in _parked)
            {
                if (p.Driver != null && p.Driver.Exists() && p.Driver.Handle == who.Handle) return true;
            }

            foreach (var l in _law)
            {
                if (l.Cop != null && l.Cop.Exists() && l.Cop.Handle == who.Handle) return true;
            }

            return false;
        }

        // ---- the cars that came to watch ----------------------------------------

        private void Cars()
        {
            var want = _rng.Next(ParkedMin, ParkedMax + 1);
            var lows = _rng.Next(LowsMin, LowsMax + 1);

            for (var i = 0; i < want; i++) Spectator(i < lows);
        }

        private void Spectator(bool low)
        {
            try
            {
                var a = _rng.NextDouble() * Math.PI * 2d;
                var r = Ring + 5f + (float)(_rng.NextDouble() * 9.0);

                var slot = new Vector3(Middle.X + (float)Math.Cos(a) * r,
                                       Middle.Y + (float)Math.Sin(a) * r, Middle.Z);

                var from = OnRoad(DriveFromMin + (float)_rng.NextDouble() * (DriveFromMax - DriveFromMin));
                if (from == Vector3.Zero) return;

                var car = Make(low ? Lows : Parked, from);
                if (car == null) return;

                var driver = Behind(car);

                if (driver == null)
                {
                    car.Delete();
                    return;
                }

                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, driver.Handle, car.Handle,
                              slot.X, slot.Y, slot.Z, 14f, 0, car.Model.Hash, 786603, 4f, true);

                Function.Call(Hash.SET_PED_KEEP_TASK, driver.Handle, true);

                if (low) Function.Call(Hash.SET_CAN_USE_HYDRAULICS, car.Handle, true);

                _parked.Add(new Parkee
                {
                    Car = car,
                    Driver = driver,
                    Slot = slot,
                    Low = low,
                    Hop = _rng.NextDouble() * Math.PI * 2d,
                    Rate = 2.2 + _rng.NextDouble() * 2.6
                });
            }
            catch (Exception ex)
            {
                Log.Debug("Takeover could not send a spectator: " + ex.Message);
            }
        }

        private void Parking()
        {
            foreach (var p in _parked)
            {
                if (p.There) continue;
                if (p.Car == null || !p.Car.Exists()) continue;
                if (p.Car.Position.DistanceTo(p.Slot) > CarArrivedRange) continue;

                p.There = true;

                try
                {
                    if (p.Driver != null && p.Driver.Exists())
                    {
                        Function.Call(Hash.TASK_VEHICLE_TEMP_ACTION, p.Driver.Handle,
                                      p.Car.Handle, 1, 4000);
                    }

                    Function.Call(Hash.SET_ENTITY_HEADING, p.Car.Handle, Facing(p.Car.Position));
                }
                catch
                {
                    // It stops where it stops.
                }
            }
        }

        /// <summary>The juice. Driven per frame, or it is a car changing height rather than hopping.</summary>
        private void Bounce()
        {
            foreach (var p in _parked)
            {
                if (!p.Low || !p.There) continue;
                if (p.Car == null || !p.Car.Exists()) continue;

                try
                {
                    p.Hop += p.Rate * 0.016;

                    // Squared, so it sits low most of the way round and snaps up. A plain sine
                    // is a car floating, which is not what a hydraulic does.
                    var s = 0.5 + 0.5 * Math.Sin(p.Hop);

                    Function.Call(Hash.SET_HYDRAULIC_SUSPENSION_RAISE_FACTOR,
                                  p.Car.Handle, (float)(s * s));
                }
                catch
                {
                    // Next frame.
                }
            }
        }

        // ---- the circle ---------------------------------------------------------

        private void Keep(int now)
        {
            for (var i = _running.Count - 1; i >= 0; i--)
            {
                var r = _running[i];

                var dead = r.Car == null || !r.Car.Exists()
                           || r.Driver == null || !r.Driver.Exists() || !r.Driver.IsAlive;

                if (dead)
                {
                    Out(r);
                    _running.RemoveAt(i);
                    continue;
                }

                // On the way out. Once clear of the ring it belongs to the world again.
                if (r.Leaving)
                {
                    if (r.Car.Position.DistanceTo(Middle) < Ring + 14f) continue;

                    Out(r);
                    _running.RemoveAt(i);
                    continue;
                }

                // On the way in. Close enough to its circle and it takes over by hand.
                if (!r.Circling)
                {
                    var wants = r.Middle ? OnTheMark : r.Radius + 6f;

                    if (r.Car.Position.DistanceTo(Middle) > wants) continue;

                    r.Circling = true;

                    r.Until = now + (r.Middle
                        ? _rng.Next(BurnMinMs, BurnMaxMs)
                        : _rng.Next(24000, 52000));

                    r.Angle = Math.Atan2(r.Car.Position.Y - Middle.Y,
                                         r.Car.Position.X - Middle.X);

                    try
                    {
                        Function.Call(Hash.CLEAR_PED_TASKS, r.Driver.Handle);
                        Function.Call(Hash.SET_VEHICLE_REDUCE_GRIP, r.Car.Handle, true);
                    }
                    catch
                    {
                        // It still goes round.
                    }

                    continue;
                }

                if (now < r.Until) continue;

                Leave(r);
            }

            // ONE ON THE MARK AND ONE OR TWO ROUND THE OUTSIDE, counted separately -- the
            // middle is a place rather than a share of the traffic, and letting them come out
            // of the same pool means the mark stands empty whenever the circle is busy.
            var mark = 0;
            var round = 0;

            foreach (var r in _running)
            {
                if (r.Leaving) continue;

                if (r.Middle) mark++;
                else round++;
            }

            if (mark < 1) In(true);

            var want = _rng.Next(100) < 45 ? 2 : 1;

            while (round < want)
            {
                if (!In(false)) break;
                round++;
            }
        }

        /// <summary>Somebody drives in for their go, on the mark or round the outside.</summary>
        private bool In(bool middle)
        {
            try
            {
                var from = OnRoad(DriveFromMin + (float)_rng.NextDouble() * (DriveFromMax - DriveFromMin));
                if (from == Vector3.Zero) return false;

                var car = Make(Drifters, from);
                if (car == null) return false;

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
                    Middle = middle,
                    Radius = DriftMin + (float)_rng.NextDouble() * (DriftMax - DriftMin),
                    Speed = 9f + (float)_rng.NextDouble() * 5f,
                    Way = _rng.Next(2) == 0 ? 1 : -1
                };

                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, driver.Handle, car.Handle,
                              Middle.X, Middle.Y, Middle.Z, 16f, 0, car.Model.Hash,
                              786603, 5f, true);

                Function.Call(Hash.SET_PED_KEEP_TASK, driver.Handle, true);

                _running.Add(r);
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Takeover could not send one in: " + ex.Message);
                return false;
            }
        }

        /// <summary>Their go is over. Grip back, smoke off, and out the way they came.</summary>
        private void Leave(Runner r)
        {
            r.Leaving = true;
            r.Circling = false;

            try
            {
                Function.Call(Hash.SET_VEHICLE_BURNOUT, r.Car.Handle, false);
                Function.Call(Hash.SET_VEHICLE_REDUCE_GRIP, r.Car.Handle, false);

                var away = OnRoad(150f + (float)_rng.NextDouble() * 110f);
                if (away == Vector3.Zero) away = Middle.Around(190f);

                Function.Call(Hash.CLEAR_PED_TASKS, r.Driver.Handle);

                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, r.Driver.Handle, r.Car.Handle,
                              away.X, away.Y, away.Z, 15f, 0, r.Car.Model.Hash, 786603, 10f, true);

                Function.Call(Hash.SET_PED_KEEP_TASK, r.Driver.Handle, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Takeover could not send one out: " + ex.Message);
            }
        }

        /// <summary>One frame of circle work, for whoever is actually working.</summary>
        private void Circle()
        {
            for (var i = 0; i < _running.Count; i++)
            {
                var r = _running[i];

                if (!r.Circling) continue;
                if (r.Car == null || !r.Car.Exists()) continue;

                // ON THE MARK. Held where it stopped and turned on the spot with the throttle
                // buried, which is a burnout -- no forward speed, because the moment it has any
                // it is not a burnout, it is a small circle. The tyres do the moving.
                if (r.Middle)
                {
                    try
                    {
                        Function.Call(Hash.SET_ENTITY_HEADING, r.Car.Handle,
                                      (r.Car.Heading + r.Way * SpinRate * 0.016f + 360f) % 360f);

                        Function.Call(Hash.SET_VEHICLE_BURNOUT, r.Car.Handle, true);
                    }
                    catch
                    {
                        // Next frame.
                    }

                    continue;
                }

                try
                {
                    r.Angle += r.Way * (r.Speed / Math.Max(2f, r.Radius)) * 0.016;

                    var tangent = r.Angle + r.Way * Math.PI * 0.5;

                    var want = (float)(Math.Atan2(Math.Sin(tangent), Math.Cos(tangent))
                                       * 180.0 / Math.PI);

                    want = (90f - want + 360f) % 360f;

                    var have = r.Car.Heading;
                    var turn = ((want - have + 540f) % 360f) - 180f;

                    Function.Call(Hash.SET_ENTITY_HEADING, r.Car.Handle,
                                  (have + turn * 0.25f + 360f) % 360f);

                    Function.Call(Hash.SET_VEHICLE_FORWARD_SPEED, r.Car.Handle, r.Speed);

                    Function.Call(Hash.SET_VEHICLE_BURNOUT, r.Car.Handle,
                                  (Game.GameTime / 1400) % 3 == 0);
                }
                catch
                {
                    // Next frame.
                }
            }
        }

        // ---- the feed -----------------------------------------------------------

        /// <summary>The block says something about it while it is on.</summary>
        private void Chatter(int now)
        {
            if (Social == null || now < _nextWord) return;

            _nextWord = now + _rng.Next(WordMinMs, WordMaxMs);

            try { Social.On(SocialEvent.Takeover); }
            catch (Exception ex) { Log.Debug("Takeover could not post: " + ex.Message); }
        }

        // ---- the law ------------------------------------------------------------

        /// <summary>
        /// Three of them, from three directions, a block out.
        ///
        /// THEY ARRIVE BEFORE ANYTHING SCATTERS. One car parked at the end of the street is a
        /// prop; three sets of lights closing from three bearings is the thing that actually
        /// empties a junction, and the gap between hearing them and seeing them is most of
        /// what makes it work. So this only sends them -- the running is in Closing(), when
        /// somebody is near enough to be worth running from.
        /// </summary>
        private void Blues()
        {
            State = TakeoverState.Scattering;
            _lastDrive = Game.GameTime + 60000;

            for (var i = 0; i < Units; i++)
            {
                try
                {
                    // Spread round the compass rather than random, so three cars cannot all
                    // come up the same street.
                    var bearing = (i / (double)Units) * Math.PI * 2d + _rng.NextDouble() * 0.6;

                    var probe = new Vector3(Middle.X + (float)Math.Cos(bearing) * LawFrom,
                                            Middle.Y + (float)Math.Sin(bearing) * LawFrom,
                                            Middle.Z);

                    var at = World.GetNextPositionOnStreet(probe, true);
                    if (at == Vector3.Zero) continue;

                    var car = Make(new[] { "police3", "police", "police2" }, at);
                    if (car == null) continue;

                    var cop = Behind(car);

                    if (cop == null)
                    {
                        car.Delete();
                        continue;
                    }

                    Function.Call(Hash.SET_VEHICLE_SIREN, car.Handle, true);

                    Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, cop.Handle, car.Handle,
                                  Middle.X, Middle.Y, Middle.Z, 20f, 0,
                                  car.Model.Hash, 786603, 6f, true);

                    Function.Call(Hash.SET_PED_KEEP_TASK, cop.Handle, true);

                    _law.Add(new Law { Car = car, Cop = cop });
                }
                catch (Exception ex)
                {
                    Log.Debug("Takeover: no police car: " + ex.Message);
                }
            }

            Log.Info("Takeover: " + _law.Count + " units on the way.");
        }

        /// <summary>Whether any of them is close enough to be worth running from.</summary>
        private bool Closing()
        {
            foreach (var l in _law)
            {
                if (l.Car == null || !l.Car.Exists()) continue;
                if (l.Car.Position.DistanceTo(Middle) <= LawSeen) return true;
            }

            return false;
        }

        /// <summary>The bit everybody has been waiting for.</summary>
        private void Scatter()
        {
            foreach (var w in _crowd)
            {
                if (w.Man == null || !w.Man.Exists() || !w.Man.IsAlive) continue;

                try
                {
                    // Off, so they can react to the world again. Being deaf to it all night is
                    // what kept the ring standing; turning it back on IS scattering.
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, w.Man.Handle, false);
                    Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, w.Man.Handle, 0, true);

                    Function.Call(Hash.CLEAR_PED_TASKS, w.Man.Handle);
                    Function.Call(Hash.TASK_SMART_FLEE_COORD, w.Man.Handle,
                                  Middle.X, Middle.Y, Middle.Z, 240f, -1, false, false);
                }
                catch
                {
                    // He runs or he does not.
                }
            }

            foreach (var p in _parked)
            {
                if (p.Car == null || !p.Car.Exists()) continue;

                try
                {
                    Function.Call(Hash.SET_HYDRAULIC_SUSPENSION_RAISE_FACTOR, p.Car.Handle, 0f);

                    if (p.Driver == null || !p.Driver.Exists()) continue;

                    // NOT A WANDER. Wander is a car pottering off at the speed limit, which is
                    // not what anybody does when the lights come round the corner. They are
                    // given somewhere to be and told to get there.
                    var off = OnRoad(200f + (float)_rng.NextDouble() * 150f);
                    if (off == Vector3.Zero) off = Middle.Around(250f);

                    Function.Call(Hash.CLEAR_PED_TASKS, p.Driver.Handle);

                    Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, p.Driver.Handle, 1.0f);

                    Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, p.Driver.Handle, p.Car.Handle,
                                  off.X, off.Y, off.Z, 28f, 0, p.Car.Model.Hash, 786603, 15f, true);

                    Function.Call(Hash.SET_PED_KEEP_TASK, p.Driver.Handle, true);
                }
                catch
                {
                    // It goes when it goes.
                }
            }

            foreach (var r in _running)
            {
                if (r.Car != null && r.Car.Exists() && r.Driver != null && r.Driver.Exists())
                {
                    Leave(r);
                }
            }

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
                    Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, car.Handle);
                    Function.Call(Hash.SET_VEHICLE_ENGINE_ON, car.Handle, true, true, false);

                    return car;
                }
                catch
                {
                    // Next name.
                }
            }

            return null;
        }

        /// <summary>
        /// Somebody at the wheel who will not panic.
        ///
        /// Every line here switches off a reaction that is correct for a driver in traffic and
        /// catastrophic at a takeover. A car that flinches at a gunshot, treats a crowd as an
        /// obstacle to escape, or takes a knock personally is a car that abandons its own donut
        /// and drives through the spectators -- which is the exact failure this exists to stop.
        /// </summary>
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

                var h = ped.Handle;

                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, h, true, true);
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, h, true);
                Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, h, false);
                Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, h, 0, false);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, h, 5, false);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, h, 46, false);

                // Good enough to hold a line, calm enough not to race anybody out of it.
                Function.Call(Hash.SET_DRIVER_ABILITY, h, 1.0f);
                Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, h, 0.0f);

                return ped;
            }
            catch (Exception ex)
            {
                Log.Debug("Takeover could not find a driver: " + ex.Message);
                return null;
            }
        }

        /// <summary>A pavement to walk in from, well away from where they are going.</summary>
        private Vector3 OnFoot(Vector3 slot)
        {
            for (var tries = 0; tries < 12; tries++)
            {
                try
                {
                    var away = WalkFromMin + (float)_rng.NextDouble() * (WalkFromMax - WalkFromMin);
                    var at = World.GetNextPositionOnSidewalk(Middle.Around(away));

                    if (at == Vector3.Zero) continue;
                    if (at.DistanceTo(slot) < WalkFromMin * 0.7f) continue;

                    return at;
                }
                catch
                {
                    // Next try.
                }
            }

            return Vector3.Zero;
        }

        /// <summary>A road to drive in from.</summary>
        private Vector3 OnRoad(float away)
        {
            for (var tries = 0; tries < 12; tries++)
            {
                try
                {
                    var at = World.GetNextPositionOnStreet(Middle.Around(away), true);

                    if (at == Vector3.Zero) continue;
                    if (at.DistanceTo(Middle) < DriveFromMin * 0.6f) continue;

                    return at;
                }
                catch
                {
                    // Next try.
                }
            }

            return Vector3.Zero;
        }

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
                // The given height came off the road in the first place.
            }

            return at;
        }

        // ---- putting it away ----------------------------------------------------

        private void Out(Runner r)
        {
            try
            {
                if (r.Car != null && r.Car.Exists())
                {
                    Function.Call(Hash.SET_VEHICLE_BURNOUT, r.Car.Handle, false);
                    Function.Call(Hash.SET_VEHICLE_REDUCE_GRIP, r.Car.Handle, false);

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

        private void Pack()
        {
            foreach (var r in _running) Out(r);
            _running.Clear();

            foreach (var w in _crowd)
            {
                try
                {
                    if (w.Man == null || !w.Man.Exists()) continue;

                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, w.Man.Handle, false);

                    w.Man.IsPersistent = false;
                    w.Man.MarkAsNoLongerNeeded();
                }
                catch
                {
                    // Already gone.
                }
            }

            _crowd.Clear();

            foreach (var p in _parked)
            {
                try
                {
                    if (p.Driver != null && p.Driver.Exists())
                    {
                        p.Driver.IsPersistent = false;
                        p.Driver.MarkAsNoLongerNeeded();
                    }

                    if (p.Car == null || !p.Car.Exists()) continue;

                    p.Car.IsPersistent = false;
                    p.Car.MarkAsNoLongerNeeded();
                }
                catch
                {
                    // Already gone.
                }
            }

            _parked.Clear();

            foreach (var l in _law)
            {
                try
                {
                    if (l.Cop != null && l.Cop.Exists())
                    {
                        l.Cop.IsPersistent = false;
                        l.Cop.MarkAsNoLongerNeeded();
                    }

                    if (l.Car == null || !l.Car.Exists()) continue;

                    l.Car.IsPersistent = false;
                    l.Car.MarkAsNoLongerNeeded();
                }
                catch
                {
                    // Already gone.
                }
            }

            _law.Clear();

            _scattered = false;
            _toCome = 0;

            State = TakeoverState.None;
        }

        public void RestoreWorld()
        {
            try
            {
                foreach (var w in _crowd) { if (w.Man != null && w.Man.Exists()) w.Man.Delete(); }

                foreach (var p in _parked)
                {
                    if (p.Driver != null && p.Driver.Exists()) p.Driver.Delete();
                    if (p.Car != null && p.Car.Exists()) p.Car.Delete();
                }

                foreach (var r in _running)
                {
                    if (r.Driver != null && r.Driver.Exists()) r.Driver.Delete();
                    if (r.Car != null && r.Car.Exists()) r.Car.Delete();
                }

                foreach (var l in _law)
                {
                    if (l.Cop != null && l.Cop.Exists()) l.Cop.Delete();
                    if (l.Car != null && l.Car.Exists()) l.Car.Delete();
                }
            }
            catch
            {
                // Teardown.
            }

            _crowd.Clear();
            _parked.Clear();
            _running.Clear();
            _law.Clear();

            _scattered = false;

            State = TakeoverState.None;
        }
    }
}
