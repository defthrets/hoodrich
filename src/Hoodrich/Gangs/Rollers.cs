using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Territory;

namespace Hoodrich.Gangs
{
    /// <summary>What one of them is doing at this moment.</summary>
    internal enum RollPhase
    {
        Rolling,
        Sitting
    }

    /// <summary>One carload of them, or one man on a bike.</summary>
    internal sealed class Roll
    {
        public Vehicle Car;
        public Ped Driver;
        public readonly List<Ped> Crew = new List<Ped>();

        /// <summary>A bike on the pavement rather than a car on the road.</summary>
        public bool OnFoot;

        public RollPhase Phase = RollPhase.Rolling;

        /// <summary>Where they are headed, and whether that spot is worth stopping at.</summary>
        public Vector3 Target;
        public bool StopThere;

        /// <summary>When the sitting ends.</summary>
        public int SitUntil;

        /// <summary>Where they were when last looked at, and when that was.</summary>
        public Vector3 WasAt;
        public int LookedAt;

        /// <summary>How many times they have been unstuck before being written off.</summary>
        public int Nudges;

        public int BornAt;
    }

    /// <summary>
    /// Their own people, out driving their own blocks.
    ///
    /// Everything in this mod that arrives by car arrives because of you: a delivery, a
    /// payback, a raid. Which means the only Families car you ever see is one that wants
    /// something, and a neighbourhood where every vehicle is plot is a neighbourhood nobody
    /// lives in. These want nothing. Four of them go round the block at thirty, pull into an
    /// alley, sit there a while, and go round again -- and they will still be doing it after
    /// you have walked off, which is the whole point of them.
    ///
    /// Two ways of moving, because a block is not only its roads. The cars use the road
    /// network. The bikes are given pavement to aim at and told to take the short way, which
    /// puts them up on the footpath cutting between the yards -- the way anybody actually rides
    /// round here.
    ///
    /// Only on our own turf, and only while you are stood on it. There is no world simulation
    /// behind this and there should not be: spawning carloads of Families across the map on the
    /// chance you drive past is a memory bill for something nobody sees.
    /// </summary>
    internal sealed class Rollers
    {
        private const int TickMs = 900;

        /// <summary>
        /// Far enough out to arrive rather than appear.
        ///
        /// Nothing is spawned inside seventy-five metres. A car that materialises at the end of
        /// the street is a bug report; the same car coming round the corner is traffic.
        /// </summary>
        private const float SpawnNear = 75f;
        private const float SpawnFar = 145f;

        /// <summary>Past this they are somebody else's problem, so they are handed back.</summary>
        private const float LetGoRange = 260f;

        private const int GapMinMs = 18000;
        private const int GapMaxMs = 55000;

        /// <summary>How long they sit in an alley with the engine running.</summary>
        private const int SitMinMs = 12000;
        private const int SitMaxMs = 45000;

        /// <summary>Chance the next place they are headed is somewhere they will stop.</summary>
        private const int StopChancePercent = 45;

        /// <summary>Close enough to call it arrived.</summary>
        private const float ArrivedRange = 16f;

        /// <summary>Nothing lasts forever, so the same four are not circling all afternoon.</summary>
        private const int LifetimeMs = 480000;

        /// <summary>
        /// Stuck: less than this much movement between two looks, that many times over.
        ///
        /// Learnt from the gang war, where two men out of eight would find a fence and stand at
        /// it for the rest of the fight. A thing that cannot reach where it is going has to be
        /// given one more try and then taken off the board, or it stops being scenery and
        /// becomes a car parked across a junction.
        /// </summary>
        private const float StuckMoved = 3f;
        private const int StuckLookMs = 9000;
        private const int MaxNudges = 2;

        /// <summary>Slow. Around thirty for a car, a bit less for a bike.</summary>
        private const float CruiseCar = 8.5f;
        private const float CruiseBike = 6f;

        /// <summary>
        /// Normal road driving for the cars.
        ///
        /// The bikes get the shortest-path bit instead, which is what puts them over the kerb:
        /// aimed at a spot on the pavement and allowed to take the direct way to it, a bicycle
        /// mounts the footpath rather than going round by the road. Peds and objects are still
        /// avoided, so it is a bike weaving past people, not a bike through them.
        /// </summary>
        private const int StyleCar = 786603;
        private const int StyleBike = 262196;

        private const int PedTypeCiv = 4;

        /// <summary>
        /// What they turn up in.
        ///
        /// Read off the spawn menu's own hash rather than guessed at, so the name here is the
        /// model that was actually looked at. Half of them are 1.69 and later, which the Legacy
        /// build has never had -- hence the spares below and the check before spawning. A
        /// missing car is a different car here, not an exception in the log.
        /// </summary>
        private static readonly string[] Cars =
        {
            "minimus", "woodlander", "hardy", "driftdominator10", "driftgauntlet4",
            "driftchavosv6", "s95", "vorschlaghammer", "asterope2", "dorado",
            "driftfr36", "kanjosj", "iwagen", "toros"
        };

        /// <summary>Always present, for the install that has none of the above.</summary>
        private static readonly string[] SpareCars = { "buccaneer2", "voodoo", "manana", "primo2" };

        /// <summary>
        /// What they ride, which is no longer only bicycles.
        ///
        /// Two pushbikes, two quads, a trike and a dirt bike. They all take the same treatment
        /// -- pavement to aim at and the short way to it -- because the thing that makes this
        /// read is a rider cutting between the yards rather than what he is sat on, and a quad
        /// up on the footpath is more of that, not less.
        ///
        /// Names read off the spawn menu's own hash. Note sanchez is the one WITH the livery
        /// and sanchez2 is the plain one, which is the wrong way round from what you would
        /// guess; the paint goes over it either way.
        /// </summary>
        private static readonly string[] Bikes =
        {
            "inductor", "inductor2", "stryder", "sanchez", "blazer", "blazer4"
        };
        private static readonly string[] SpareBikes = { "bmx", "scorcher" };

        /// <summary>Dark green, out of the game's own paint table.</summary>
        private const int DarkGreen = 49;

        private readonly Settings _cfg;
        private readonly GangRegistry _gangs;
        private readonly string _gangId;
        private readonly TurfWatch _turf;
        private readonly Random _rng = new Random();

        private readonly List<Roll> _out = new List<Roll>();

        private int _lastTick;
        private int _nextSpawn;

        /// <summary>Off while something louder is happening. Wired by the house script.</summary>
        public Func<bool> Busy;

        /// <summary>
        /// The settings are read every tick rather than copied at startup.
        ///
        /// Because the wheel can change them. A value copied into a field here would leave the
        /// switch on the settings page doing nothing until the next reload, which is a switch
        /// that appears broken -- and the whole reason that page exists is that changes take.
        /// </summary>
        private bool Enabled => _cfg == null || _cfg.RollersEnabled;

        private int MaxCars => _cfg == null ? 2 : _cfg.RollerCars;
        private int MaxBikes => _cfg == null ? 2 : _cfg.RollerBikes;

        public Rollers(Settings cfg, GangRegistry gangs, string gangId, TurfWatch turf)
        {
            _cfg = cfg;
            _gangs = gangs;
            _gangId = gangId;
            _turf = turf;
        }

        // ---- per-tick ----------------------------------------------------------

        public void Update()
        {
            var now = Game.GameTime;
            if (now - _lastTick < TickMs) return;
            _lastTick = now;

            try
            {
                Prune(now);

                if (!Enabled)
                {
                    // Turned off mid-session. Handed back rather than left circling, so the
                    // switch does something you can see out of the window.
                    if (_out.Count > 0)
                    {
                        foreach (var roll in _out) Release(roll);
                        _out.Clear();
                    }

                    return;
                }

                var player = Game.Player.Character;
                if (player == null || !player.Exists() || !player.IsAlive) return;

                // Backwards, because a carload that has given up is dropped where it stands and
                // a foreach over a list something is being removed from throws.
                for (var i = _out.Count - 1; i >= 0; i--)
                {
                    if (!Steer(_out[i], now)) continue;

                    Release(_out[i]);
                    _out.RemoveAt(i);
                }

                // Existing ones are left to finish whatever they were doing when you walked off
                // our blocks; new ones only start on them.
                if (!OnOurTurf()) return;
                if (Busy != null && Busy()) return;

                if (_nextSpawn == 0) _nextSpawn = now + _rng.Next(GapMinMs, GapMaxMs);
                if (now < _nextSpawn) return;

                _nextSpawn = now + _rng.Next(GapMinMs, GapMaxMs);

                // A bike is cheaper and reads better on a quiet street, so it wins the coin
                // toss more often than not.
                var wantBike = _rng.Next(100) < 55;

                if (wantBike && Count(true) < MaxBikes) Send(player, true);
                else if (Count(false) < MaxCars) Send(player, false);
                else if (Count(true) < MaxBikes) Send(player, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Rollers tripped: " + ex.Message);
            }
        }

        private int Count(bool bikes)
        {
            var n = 0;
            foreach (var roll in _out)
            {
                if (roll.OnFoot == bikes) n++;
            }
            return n;
        }

        // ---- keeping them moving -----------------------------------------------

        /// <summary>
        /// One carload, one tick.
        ///
        /// Arriving is the only event here. Everything else -- sitting, moving off, being
        /// stuck -- hangs off whether they got where they were going, because the drive task
        /// stops the car at its destination and holds it there. That is why nothing needs to
        /// brake or park: a car told to drive to a point in an alley IS a car sat in an alley
        /// once it gets there, and all this has to do is not give it a new instruction for a
        /// while.
        /// </summary>
        /// <returns>True when this one has been given up on and should be let go.</returns>
        private bool Steer(Roll roll, int now)
        {
            if (roll.Car == null || !roll.Car.Exists()) return false;

            if (roll.Phase == RollPhase.Sitting)
            {
                if (now < roll.SitUntil) return false;

                roll.Phase = RollPhase.Rolling;
                roll.Nudges = 0;
                Aim(roll, now);
                return false;
            }

            var here = roll.Car.Position;

            if (roll.Target != Vector3.Zero && here.DistanceTo(roll.Target) < ArrivedRange)
            {
                if (roll.StopThere)
                {
                    roll.Phase = RollPhase.Sitting;
                    roll.SitUntil = now + _rng.Next(SitMinMs, SitMaxMs);
                    return false;
                }

                roll.Nudges = 0;
                Aim(roll, now);
                return false;
            }

            // Not there yet. Two looks with nothing between them is a wall.
            if (now - roll.LookedAt < StuckLookMs) return false;

            var moved = roll.LookedAt == 0 ? float.MaxValue : here.DistanceTo(roll.WasAt);

            roll.WasAt = here;
            roll.LookedAt = now;

            if (moved > StuckMoved) return false;

            roll.Nudges++;

            // Given up on. Handed back rather than deleted, because deleting a car the player
            // might be looking at is worse than one that drives off oddly.
            if (roll.Nudges > MaxNudges) return true;

            Aim(roll, now);
            return false;
        }

        /// <summary>Points them at somewhere else on the block and lets them go.</summary>
        private void Aim(Roll roll, int now)
        {
            if (roll.Car == null || !roll.Car.Exists()) return;
            if (roll.Driver == null || !roll.Driver.Exists() || !roll.Driver.IsAlive) return;

            var stop = !roll.OnFoot && _rng.Next(100) < StopChancePercent;

            var where = roll.OnFoot
                ? Pavement(roll.Car.Position)
                : Node(roll.Car.Position, stop);

            // Nowhere to send them this tick -- the probes all landed off our turf, or off the
            // road network entirely. They wander instead of standing still, because a car
            // stopped in a live lane is the exact thing the traffic watchdog exists to remove
            // and this one has a driver in it, so nothing would remove it.
            if (where == Vector3.Zero)
            {
                try
                {
                    Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, roll.Driver.Handle, roll.Car.Handle,
                                  roll.OnFoot ? CruiseBike : CruiseCar,
                                  roll.OnFoot ? StyleBike : StyleCar);

                    // No destination any more, which matters: leaving the old one in place
                    // would read as "arrived" again next tick and restart the wander every
                    // 900ms, and a car handed a fresh instruction nine times a second does not
                    // go anywhere at all.
                    roll.Target = Vector3.Zero;
                    roll.StopThere = false;
                    roll.LookedAt = now;
                    roll.WasAt = roll.Car.Position;
                }
                catch
                {
                    // They will be looked at again in nine seconds either way.
                }

                return;
            }

            roll.Target = where;
            roll.StopThere = stop;
            roll.LookedAt = now;
            roll.WasAt = roll.Car.Position;

            try
            {
                Function.Call(Hash.CLEAR_PED_TASKS, roll.Driver.Handle);

                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, roll.Driver.Handle, roll.Car.Handle,
                              where.X, where.Y, where.Z,
                              roll.OnFoot ? CruiseBike : CruiseCar,
                              0, roll.Car.Model.Hash,
                              roll.OnFoot ? StyleBike : StyleCar,
                              stop ? 4f : 15f, true);

                Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, roll.Driver.Handle,
                              roll.OnFoot ? CruiseBike : CruiseCar);

                Function.Call(Hash.SET_PED_KEEP_TASK, roll.Driver.Handle, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not send a roller on: " + ex.Message);
            }
        }

        // ---- where they go -----------------------------------------------------

        /// <summary>
        /// A point on the road network near them, on our blocks.
        ///
        /// Back alleys are picked out by asking the map, not by listing coordinates: the game
        /// marks a node as GPS-allowed when it is a road the satnav would route you down, so the
        /// ones it will NOT route down are the service roads, the yards and the cut-throughs
        /// behind the buildings. That is close enough to the same set of places somebody would
        /// pull into, which is a happy accident rather than a design, but it holds up.
        ///
        /// A few tries and then it takes whatever it got. Insisting on an alley near a block
        /// that has none means never moving.
        /// </summary>
        private Vector3 Node(Vector3 from, bool wantAlley)
        {
            for (var tries = 0; tries < 12; tries++)
            {
                var probe = from.Around(40f + (float)_rng.NextDouble() * 110f);

                try
                {
                    var id = Function.Call<int>(Hash.GET_NTH_CLOSEST_VEHICLE_NODE_ID,
                                                probe.X, probe.Y, probe.Z,
                                                1 + _rng.Next(8), 1, 3f, 0f);

                    if (!Function.Call<bool>(Hash.IS_VEHICLE_NODE_ID_VALID, id)) continue;

                    var got = new OutputArgument();
                    Function.Call(Hash.GET_VEHICLE_NODE_POSITION, id, got);

                    var at = got.GetResult<Vector3>();
                    if (at == Vector3.Zero) continue;
                    if (!Ours(at)) continue;

                    var backstreet = !Function.Call<bool>(Hash.GET_VEHICLE_NODE_IS_GPS_ALLOWED, id);

                    // Held out for over the first eight tries, then anything on our turf will do.
                    if (tries < 8 && backstreet != wantAlley) continue;

                    return at;
                }
                catch
                {
                    // Next try.
                }
            }

            return Vector3.Zero;
        }

        /// <summary>Somewhere to aim a bike: a stretch of pavement on our blocks.</summary>
        private Vector3 Pavement(Vector3 from)
        {
            for (var tries = 0; tries < 10; tries++)
            {
                try
                {
                    var probe = from.Around(35f + (float)_rng.NextDouble() * 85f);
                    var at = World.GetNextPositionOnSidewalk(probe);

                    if (at == Vector3.Zero) continue;
                    if (!Ours(at)) continue;

                    return at;
                }
                catch
                {
                    // Next try.
                }
            }

            return Vector3.Zero;
        }

        private bool OnOurTurf()
        {
            var owner = _turf == null ? null : _turf.Owner;
            return owner != null && string.Equals(owner.Id, _gangId, StringComparison.OrdinalIgnoreCase);
        }

        private bool Ours(Vector3 at)
        {
            try
            {
                var code = Function.Call<string>(Hash.GET_NAME_OF_ZONE, at.X, at.Y, at.Z) ?? "";
                var owner = _gangs.OwnerOfZone(code);

                return owner != null && string.Equals(owner.Id, _gangId, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        // ---- putting one out ---------------------------------------------------

        private void Send(Ped player, bool bike)
        {
            var gang = _gangs.Get(_gangId);
            if (gang == null || gang.MemberModels.Count == 0) return;

            var spawn = Somewhere(player, bike);
            if (spawn == Vector3.Zero) return;

            var roll = new Roll { OnFoot = bike, BornAt = Game.GameTime };

            try
            {
                roll.Car = Make(bike, spawn);
                if (roll.Car == null) return;

                // Two or four up. A bike is one man, and a two-seater is a man and his mate --
                // asking for four in a coupe gets you two and a warning nobody reads.
                var room = Function.Call<int>(Hash.GET_VEHICLE_MAX_NUMBER_OF_PASSENGERS, roll.Car.Handle);
                // Two up where there is a pillion to sit on. Bicycles have none, so the clamp
                // below quietly puts those back to one man -- which is why this asks for it
                // rather than checking what he is riding.
                var want = bike
                    ? (_rng.Next(100) < 40 ? 1 : 0)
                    : (_rng.Next(2) == 0 ? 1 : 3);
                if (want > room) want = room;

                for (var seat = -1; seat < want; seat++)
                {
                    var man = Fill(gang, roll.Car, seat);
                    if (man == null) continue;

                    if (seat == -1) roll.Driver = man;
                    roll.Crew.Add(man);
                }

                if (roll.Driver == null)
                {
                    Scrap(roll);
                    return;
                }

                Paint(roll.Car, bike);

                _out.Add(roll);
                Aim(roll, Game.GameTime);

                Log.Info("Rollers: " + (bike ? "a bike" : "a car with " + roll.Crew.Count + " up") +
                         " came out on " + (_turf == null ? "the block" : _turf.ZoneName) + ".");
            }
            catch (Exception ex)
            {
                Log.Debug("Rollers could not put one out: " + ex.Message);
                Scrap(roll);
                _out.Remove(roll);
            }
        }

        /// <summary>
        /// A spot to come from: on the road, on our turf, and not in your lap.
        ///
        /// The near limit is the whole trick. Everything else about this is ordinary spawning.
        /// </summary>
        private Vector3 Somewhere(Ped player, bool bike)
        {
            for (var tries = 0; tries < 12; tries++)
            {
                try
                {
                    var away = SpawnNear + (float)_rng.NextDouble() * (SpawnFar - SpawnNear);
                    var probe = player.Position.Around(away);

                    var at = bike
                        ? World.GetNextPositionOnSidewalk(probe)
                        : World.GetNextPositionOnStreet(probe, true);

                    if (at == Vector3.Zero) continue;
                    if (at.DistanceTo(player.Position) < SpawnNear) continue;
                    if (!Ours(at)) continue;

                    return at;
                }
                catch
                {
                    // Next try.
                }
            }

            return Vector3.Zero;
        }

        /// <summary>
        /// The vehicle itself.
        ///
        /// Every model is checked against this copy of the game before it is asked for, and a
        /// miss simply moves on to the next one. Half of the list is content the Legacy build
        /// never received, so on that install this quietly becomes a shorter list rather than a
        /// stream of failures.
        /// </summary>
        private Vehicle Make(bool bike, Vector3 at)
        {
            var wanted = bike ? Bikes : Cars;
            var spares = bike ? SpareBikes : SpareCars;

            var order = new List<string>();

            for (var i = 0; i < 8; i++) order.Add(wanted[_rng.Next(wanted.Length)]);
            order.AddRange(spares);

            foreach (var name in order)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) continue;

                    var car = World.CreateVehicle(model, at);
                    model.MarkAsNoLongerNeeded();

                    if (car == null || !car.Exists()) continue;

                    car.IsPersistent = true;

                    Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, car.Handle);
                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, car.Handle, true, true);

                    return car;
                }
                catch (Exception ex)
                {
                    Log.Debug("Rollers could not make a " + name + ": " + ex.Message);
                }
            }

            return null;
        }

        /// <summary>
        /// Dark green, the whole set of them.
        ///
        /// The colour is a paint index rather than an RGB triple, for the same reason the cars
        /// parked on the block are: the table has the metal flake in it and a raw green comes
        /// out flat. The radio is on and loud in the cars because a car going past with nothing
        /// coming out of it is a car nobody is in.
        /// </summary>
        private void Paint(Vehicle car, bool bike)
        {
            try
            {
                Function.Call(Hash.SET_VEHICLE_MOD_KIT, car.Handle, 0);
                Function.Call(Hash.SET_VEHICLE_COLOURS, car.Handle, DarkGreen, DarkGreen);
                Function.Call(Hash.SET_VEHICLE_EXTRA_COLOURS, car.Handle, DarkGreen, 0);

                if (bike) return;

                Function.Call(Hash.SET_VEHICLE_WINDOW_TINT, car.Handle, 1);
                Function.Call(Hash.SET_VEHICLE_DIRT_LEVEL, car.Handle, 1f);

                Function.Call(Hash.SET_VEHICLE_RADIO_ENABLED, car.Handle, true);
                Function.Call(Hash.SET_VEH_RADIO_STATION, car.Handle, "RADIO_03_HIPHOP_NEW");
                Function.Call(Hash.SET_VEHICLE_RADIO_LOUD, car.Handle, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not paint a roller: " + ex.Message);
            }
        }

        /// <summary>One of them, in one seat.</summary>
        private Ped Fill(GangDef gang, Vehicle car, int seat)
        {
            try
            {
                var name = gang.MemberModels[_rng.Next(gang.MemberModels.Count)];

                var model = new Model(name);
                if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) return null;

                var handle = Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE, car.Handle,
                                                PedTypeCiv, model.Hash, seat, false, false);

                model.MarkAsNoLongerNeeded();
                if (handle == 0) return null;

                var man = Entity.FromHandle(handle) as Ped;
                if (man == null || !man.Exists()) return null;

                man.IsPersistent = true;

                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, man.Handle, true, true);
                Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, man.Handle, gang.GroupHash);

                // Not told to ignore the world. They are ours and they are on our blocks, so a
                // set that drove past a fight without looking at it would be worse than one that
                // gets out of the car -- and the stuck watchdog covers the mess either way.
                Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, man.Handle, false);

                return man;
            }
            catch (Exception ex)
            {
                Log.Debug("Rollers could not fill a seat: " + ex.Message);
                return null;
            }
        }

        // ---- clearing up -------------------------------------------------------

        private void Prune(int now)
        {
            var player = Game.Player.Character;
            var mine = player != null && player.Exists() && player.CurrentVehicle != null
                       && player.CurrentVehicle.Exists()
                ? player.CurrentVehicle.Handle
                : 0;

            for (var i = _out.Count - 1; i >= 0; i--)
            {
                var roll = _out[i];

                var gone = roll.Car == null || !roll.Car.Exists();
                var noDriver = roll.Driver == null || !roll.Driver.Exists() || !roll.Driver.IsAlive;

                if (gone || noDriver)
                {
                    Release(roll);
                    _out.RemoveAt(i);
                    continue;
                }

                // Taken off us: the player got in it, or somebody else is behind the wheel. It
                // stops being ours the moment either happens.
                var driver = roll.Car.Driver;
                var taken = driver == null || !driver.Exists()
                            || driver.Handle != roll.Driver.Handle
                            || (mine != 0 && roll.Car.Handle == mine);

                var old = now - roll.BornAt > LifetimeMs;

                var far = player != null && player.Exists()
                          && roll.Car.Position.DistanceTo(player.Position) > LetGoRange;

                if (taken || old || far)
                {
                    Release(roll);
                    _out.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Handed back to the game rather than deleted.
        ///
        /// The game clears up its own traffic when nobody is looking, and it is better at
        /// choosing the moment than a distance check is. Deleting outright is for teardown,
        /// where there is no later moment to wait for.
        /// </summary>
        private void Release(Roll roll)
        {
            try
            {
                foreach (var man in roll.Crew)
                {
                    if (man == null || !man.Exists()) continue;

                    man.IsPersistent = false;
                    man.MarkAsNoLongerNeeded();
                }

                if (roll.Car != null && roll.Car.Exists())
                {
                    roll.Car.IsPersistent = false;
                    roll.Car.MarkAsNoLongerNeeded();
                }
            }
            catch
            {
                // Letting go of something already gone.
            }
        }

        private void Scrap(Roll roll)
        {
            try
            {
                foreach (var man in roll.Crew)
                {
                    if (man != null && man.Exists()) man.Delete();
                }

                if (roll.Car != null && roll.Car.Exists()) roll.Car.Delete();
            }
            catch
            {
                // Nothing left to scrap.
            }
        }

        /// <summary>Everything off the street, for a reload.</summary>
        public void RestoreWorld()
        {
            foreach (var roll in _out) Scrap(roll);
            _out.Clear();
        }
    }
}
