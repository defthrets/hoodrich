using System;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.UI;

namespace Hoodrich.Locations
{
    /// <summary>Where a Knowai is up to.</summary>
    internal enum RideState
    {
        None,

        /// <summary>Assigned and driving to you.</summary>
        Coming,

        /// <summary>Stopped, doors unlocked, waiting for you to get in.</summary>
        Waiting,

        /// <summary>You are in the back and it wants to know where.</summary>
        Picking,

        /// <summary>You are in it and it is driving.</summary>
        Riding,

        /// <summary>There. Waiting for you to get out.</summary>
        Arrived
    }

    /// <summary>Somewhere a Knowai will take you.</summary>
    internal sealed class RideStop
    {
        public string Name = "";
        public string Area = "";
        public Vector3 At;
    }

    /// <summary>
    /// Knowai. A car with nobody in it that comes when you ask and takes you where you said.
    ///
    /// THE DRIVER IS A PED YOU CANNOT SEE, and there is no way round that. Nothing in this
    /// game drives a car except a ped -- the whole vehicle task system takes a driver handle,
    /// and a car with an empty seat is a car parked in the road. So one is created, put in the
    /// seat, given the route, and then made invisible. It is a trick, it is the only trick
    /// available, and from outside the car it is indistinguishable from the thing it is
    /// pretending to be.
    ///
    /// It is also why the driver is made invincible and told to ignore everything. A ped nobody
    /// can see is a ped nobody knows to avoid shooting -- and an empty car that suddenly swerves
    /// because its invisible occupant panicked at a car backfiring would be a very strange thing
    /// to be sat in.
    ///
    /// THE FARE IS TAKEN ON ARRIVAL, not when you hail it. You pay a taxi when you get out.
    /// </summary>
    internal sealed class Knowai
    {
        // ---- shape --------------------------------------------------------------

        /// <summary>
        /// The picture on its notifications: a helmet, photographed like anybody else's face.
        ///
        /// THE SERVICE HAS NO PERSON, SO IT HAS A HEAD INSTEAD. It was Simeon -- a photograph
        /// of a specific man's face on a message from a company he has nothing to do with,
        /// which is the nearest wrong thing rather than a picture.
        ///
        /// The feed's own face factory already turns any ped into a portrait, so the answer is
        /// to point it at something with no face: a helmet, a visor, a bubble. It comes back
        /// looking like a machine wearing a uniform, which is exactly what a driverless car
        /// company would put on its notifications.
        ///
        /// Behind it, the contact photos it used to use -- because the headshot is made on
        /// demand and takes a few frames, so the first card of a session may go out before it
        /// exists. See Warm.
        /// </summary>
        private const string FaceKey = "knowai";

        private static readonly string[] Heads =
        {
            "u_m_y_juggernaut_01", "s_m_y_hwaycop_01", "u_m_y_rsranger_01",
            "s_m_y_swat_01", "s_m_m_movalien_01"
        };

        private static string Face
        {
            get
            {
                var made = Headshots.Txd(FaceKey);

                if (!string.IsNullOrEmpty(made)) return made;

                return Faces.FirstReady("CHAR_LS_CUSTOMS", "CHAR_MP_MECHANIC",
                                        "CHAR_BLANK_ENTRY", "CHAR_DEFAULT");
            }
        }

        /// <summary>
        /// Asks for the head before anything needs it.
        ///
        /// Called when a car is requested, which is a good several seconds before the first
        /// card goes out -- so by the time there is something to say, there is something to
        /// say it with.
        /// </summary>
        private static void Warm()
        {
            try { Headshots.WantAs(FaceKey, Heads); }
            catch { /* the contact photo is behind it */ }
        }

        /// <summary>The car, in the order they exist on a given install.</summary>
        private static readonly string[] Cars = { "vivanite2", "vivanite", "taxi" };

        /// <summary>Far enough that it drives to you rather than appearing at the kerb.</summary>
        private const float ComeFromMin = 110f;
        private const float ComeFromMax = 220f;

        /// <summary>Close enough to you to stop and open the doors.</summary>
        private const float PickUpRange = 14f;

        /// <summary>Close enough to where you asked for to call it arrived.</summary>
        private const float DropRange = 26f;

        /// <summary>How long it will sit at the kerb before it gives up on you.</summary>
        private const int WaitMs = 120000;

        /// <summary>And how long anything else is given before it is written off.</summary>
        private const int PhaseCapMs = 300000;

        /// <summary>Once you are out and it has driven this far, it stops existing.</summary>
        private const float GoneRange = 130f;

        /// <summary>What it costs: on the meter, plus this much a hundred metres.</summary>
        private const int Flagfall = 40;
        private const float PerHundred = 9f;

        /// <summary>
        /// Everywhere it goes.
        ///
        /// EVERY ONE OF THESE IS A COORDINATE THE MOD ALREADY USES for something else -- the
        /// yard Hao stands in, the counter in Denise's kitchen, the gate at the docks, the
        /// courts Lamar rides out to. Not one of them is a number read off a map, which is the
        /// only way to be sure a car sent there is being sent somewhere that exists.
        ///
        /// They are snapped to the nearest road before anybody drives to them, so a spot inside
        /// a building is still a fare to the kerb outside it rather than a car trying to get
        /// into a kitchen.
        /// </summary>
        public static readonly RideStop[] Stops =
        {
            new RideStop { Name = "Home", Area = "Aunt Denise's, Forum Drive",
                           At = new Vector3(-14.3f, -1438.4f, 31.1f) },

            new RideStop { Name = "Gerald's", Area = "The flats, Chamberlain Hills",
                           At = new Vector3(-160.738f, -1637.779f, 34.029f) },

            new RideStop { Name = "Lamar's block", Area = "Forum Drive",
                           At = new Vector3(-210.409f, -1720.489f, 32.664f) },

            new RideStop { Name = "The courts", Area = "Chamberlain Hills",
                           At = new Vector3(-227.173f, -1541.756f, 31.607f) },

            new RideStop { Name = "Stretch's", Area = "Strawberry",
                           At = new Vector3(-129.187f, -1461.375f, 33.823f) },

            new RideStop { Name = "Hao's lot", Area = "Little Seoul",
                           At = new Vector3(-40.439f, -1675.140f, 29.470f) },

            new RideStop { Name = "The docks", Area = "Elysian Island",
                           At = new Vector3(780.991f, -2973.205f, 5.801f) },

            new RideStop { Name = "The container yard", Area = "Terminal",
                           At = new Vector3(1245.656f, -3165.266f, 5.645f) },

            new RideStop { Name = "La Puerta", Area = "The west side",
                           At = new Vector3(-103.338f, -1417.405f, 29.170f) },

            new RideStop { Name = "Cypress Flats", Area = "East side",
                           At = new Vector3(500.6f, -1697.562f, 29.789f) }
        };

        // ---- wiring -------------------------------------------------------------

        /// <summary>Set by Main: take the fare. False when he cannot pay it.</summary>
        public Func<int, bool> Charge;

        /// <summary>Set by Main: off while something louder is happening.</summary>
        public Func<bool> Busy;

        /// <summary>Set by Main: you are in the back and it wants a destination.</summary>
        public Action Choose;

        public RideState State { get; private set; }

        public bool IsRunning => State != RideState.None;

        /// <summary>Where it is taking you, for the app to say so.</summary>
        public string Going { get; private set; } = "";

        private readonly Random _rng = new Random();

        private Vehicle _car;
        private Ped _driver;
        private Blip _blip;

        private RideStop _to;
        private Vector3 _dropAt;

        private int _phaseFrom;
        private int _fare;

        /// <summary>Where it was when last looked at, and when that was.</summary>
        private Vector3 _wasAt;
        private int _lookedAt;
        private int _nudges;

        /// <summary>Barely moved in this long, this many times over, and it is written off.</summary>
        private const float StuckMoved = 3f;
        private const int StuckLookMs = 9000;
        private const int MaxNudges = 3;

        // ---- hailing ------------------------------------------------------------

        /// <summary>
        /// Requests one. Returns a player-facing refusal, or null once it is on its way.
        ///
        /// NO DESTINATION AT THIS POINT, and that is the shape of the real thing. You do not
        /// tell a cab where you are going before it has arrived -- you get in, and then it
        /// asks. Choosing on the phone in the street also meant choosing before you knew
        /// whether the car was going to turn up at all.
        /// </summary>
        public string Hail()
        {
            if (IsRunning) return "You've already got one coming.";

            if (Busy != null && Busy()) return "Not right now.";

            var player = Game.Player.Character;

            if (player == null || !player.Exists() || !player.IsAlive) return "Not right now.";
            if (player.IsInVehicle()) return "Get out of the car first.";

            var start = Somewhere(player.Position);
            if (start == Vector3.Zero) return "Nothing free near you.";

            if (!Make(start)) return "Nothing free near you.";

            Warm();

            _to = null;
            Going = "";
            _fare = 0;

            Send(player.Position);
            Mark();

            Begin(RideState.Coming);

            Notify.Card(Face, "Knowai", "on the way", "A car's been assigned. Sit tight.");

            Log.Info("Knowai: pickup requested.");

            return null;
        }

        /// <summary>
        /// Where to, chosen from the back seat. Returns a refusal or null.
        /// </summary>
        public string Go(RideStop stop)
        {
            if (stop == null) return "Nowhere selected.";
            if (State != RideState.Picking) return "Not in a Knowai.";

            var drop = OnRoad(stop.At);
            if (drop == Vector3.Zero) return "Knowai doesn't go there.";

            var player = Game.Player.Character;
            var from = player != null && player.Exists() ? player.Position : _car.Position;

            _to = stop;
            _dropAt = drop;
            _fare = Flagfall + (int)(from.DistanceTo(drop) / 100f * PerHundred);

            Going = stop.Name;

            Begin(RideState.Riding);
            Drive(_dropAt, 22f);

            // THE FARE ON ITS OWN. The destination is already on the row you just pressed,
            // on the blip, and out of the window in about a minute; the number is the only
            // thing here you did not already know.
            Notify.Card(Face, "Knowai", stop.Name, "$" + _fare);

            return null;
        }

        /// <summary>Called off, from the app or from anything going wrong.</summary>
        public void Cancel(string why)
        {
            if (!string.IsNullOrEmpty(why)) Notify.Failure(why);

            Clean();
            State = RideState.None;
            Going = "";
        }

        // ---- per-tick -----------------------------------------------------------

        public void Update(Ped player)
        {
            // BEFORE THE RUNNING CHECK. A cab on its way out has by definition finished its
            // ride, so anything gated on a ride being live would never tidy it up -- which is
            // exactly how the old version left one on the kerb.
            try { Follow(); }
            catch { /* next tick */ }

            try
            {
                if (!IsRunning) return;

                if (player == null || !player.Exists() || !player.IsAlive)
                {
                    Cancel("");
                    return;
                }

                if (_car == null || !_car.Exists())
                {
                    Cancel("Your Knowai's gone.");
                    return;
                }

                // INVISIBLE EVERY TICK, NOT ONCE.
                //
                // Setting it at creation is not enough: the flag comes back on when the ped
                // streams out and in again, when it is handed back to the game, and when its
                // task changes -- which is why a man appeared behind the wheel the moment the
                // ride ended. It is one native call against one ped and it is the difference
                // between a driverless car and a car with a stranger in it.
                if (_driver != null && _driver.Exists())
                {
                    try { Function.Call(Hash.SET_ENTITY_VISIBLE, _driver.Handle, false, false); }
                    catch { /* next tick */ }
                }

                var now = Game.GameTime;

                if (now - _phaseFrom > PhaseCapMs)
                {
                    Cancel("That Knowai never turned up.");
                    return;
                }

                switch (State)
                {
                    case RideState.Coming: Coming(player, now); break;
                    case RideState.Waiting: Waiting(player, now); break;
                    case RideState.Picking: Picking(player, now); break;
                    case RideState.Riding: Riding(player); break;
                    case RideState.Arrived: Arrived(player); break;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Knowai tripped: " + ex.Message);
                Cancel("");
            }
        }

        /// <summary>
        /// Whether it has stopped getting anywhere, and what to do about it.
        ///
        /// A CAR THAT CANNOT REACH YOU USED TO SIT THERE FOR FIVE MINUTES. The only limit was
        /// the phase cap, so a Knowai that nosed into an alley wall was a Knowai you waited on
        /// until the whole thing timed out -- and there is no worse outcome for a service whose
        /// entire promise is that it turns up.
        ///
        /// Re-routed rather than teleported. The route is asked for again from where it
        /// actually is, which is usually all it needs; three of those and it is written off
        /// and you are told, which is far better than silence.
        /// </summary>
        /// <returns>True when it has been given up on.</returns>
        private bool Stuck(int now, Vector3 goingTo)
        {
            if (now - _lookedAt < StuckLookMs) return false;

            var here = _car.Position;
            var moved = _lookedAt == 0 ? float.MaxValue : here.DistanceTo(_wasAt);

            _wasAt = here;
            _lookedAt = now;

            if (moved > StuckMoved)
            {
                _nudges = 0;
                return false;
            }

            _nudges++;

            if (_nudges > MaxNudges) return true;

            Log.Info("Knowai: stuck, re-routing (" + _nudges + ").");

            Drive(goingTo, State == RideState.Riding ? 22f : 20f);

            return false;
        }

        private void Coming(Ped player, int now)
        {
            if (Stuck(now, player.Position))
            {
                Away();
                Cancel("That Knowai couldn't reach you. Ask for another.");
                return;
            }

            if (_car.Position.DistanceTo(player.Position) > PickUpRange) return;

            Halt();
            Begin(RideState.Waiting);

            try { Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, _car.Handle, 1); }
            catch { /* it was probably unlocked anyway */ }

            Notify.Card(Face, "Knowai", "outside", "Your car's here.");
        }

        private void Waiting(Ped player, int now)
        {
            if (player.IsInVehicle(_car))
            {
                Begin(RideState.Picking);

                try { if (Choose != null) Choose(); }
                catch (Exception ex) { Log.Debug("Could not ask where to: " + ex.Message); }

                return;
            }

            Help.ShowThisFrame("Press ~INPUT_CELLPHONE_RIGHT~ to get in your Knowai.");

            if (Tapped()) Board(player);

            // It is a car with nowhere else to be, not a mission timer -- but it does not sit
            // at that kerb for the rest of the session either.
            if (now - _phaseFrom < WaitMs) return;

            Away();
            Cancel("Your Knowai gave up waiting.");
        }

        /// <summary>
        /// Puts him in the back, which is where a passenger sits.
        ///
        /// SEAT 2 -- the near-side rear. Not the front, and not because the front is taken:
        /// the driver's seat has a man in it you cannot see, and somebody sat in the front
        /// passenger seat of a driverless car looks like somebody whose driver has vanished.
        /// The back is the seat that reads as being driven.
        ///
        /// Walking up and pressing the game's own enter key would put him at the wheel, so the
        /// car has an exclusive driver set on it -- see Wheel. That makes every ordinary way in
        /// a passenger seat, and this makes it the right one.
        /// </summary>
        private void Board(Ped player)
        {
            try
            {
                Function.Call(Hash.TASK_ENTER_VEHICLE, player.Handle, _car.Handle,
                              12000, RearSeat, 1.5f, 1, 0);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not put him in the back: " + ex.Message);
            }
        }

        /// <summary>The near-side rear seat. -1 driver, 0 front passenger, 1 and 2 the back.</summary>
        private const int RearSeat = 2;

        private bool _down;

        private bool Tapped()
        {
            var down = false;

            try
            {
                down = Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)Control.PhoneRight)
                    || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)Control.PhoneRight)
                    || Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)Control.Context);
            }
            catch
            {
            }

            var hit = down && !_down;
            _down = down;

            return hit;
        }

        /// <summary>Sat in the back with nowhere named yet.</summary>
        private void Picking(Ped player, int now)
        {
            if (!player.IsInVehicle(_car))
            {
                Away();
                Cancel("You got out.");
                return;
            }

            Help.ShowThisFrame("Open the phone and pick where you're going.");
        }

        private void Riding(Ped player)
        {
            // Out early. It is a taxi, not a cage: getting out is allowed and ends the ride
            // wherever you did it, which is what stepping out of a moving cab means.
            //
            // AND IT IS STILL CHARGED FOR. A car came out, found you and drove you part of the
            // way; changing your mind halfway is a thing you are allowed to do and not a thing
            // that makes it free. It is the whole fare rather than a share of it, because a
            // meter that has to be explained is worse than one that is simply firm.
            if (!player.IsInVehicle(_car))
            {
                var paid = Charge == null || Charge(_fare);

                Notify.Card(Face, "Knowai", "ride ended early",
                            paid ? "$" + _fare : "Unpaid. $" + _fare + " owed.");

                Away();
                Cancel("");
                return;
            }

            if (Stuck(Game.GameTime, _dropAt))
            {
                Charge?.Invoke(_fare);

                Notify.Card(Face, "Knowai", "ride ended", "Couldn't get through. $" + _fare);

                Away();
                Cancel("");
                return;
            }

            if (_car.Position.DistanceTo(_dropAt) > DropRange) return;

            Halt();
            Begin(RideState.Arrived);

            try { Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, _car.Handle, 1); }
            catch { /* unlocked is the default */ }

            if (Charge != null && !Charge(_fare))
            {
                Notify.Failure("you couldn't cover the fare. That'll be remembered.");
            }
            else
            {
                Notify.Card(Face, "Knowai", "paid", "$" + _fare);
            }
        }

        private void Arrived(Ped player)
        {
            if (player.IsInVehicle(_car))
            {
                Help.ShowThisFrame("You're here. Get out.");
                return;
            }

            // Out. It has somewhere else to be.
            Away();

            if (_car.Position.DistanceTo(player.Position) < GoneRange) return;

            Clean();
            State = RideState.None;
            Going = "";
        }

        // ---- the car ------------------------------------------------------------

        private bool Make(Vector3 at)
        {
            try
            {
                foreach (var name in Cars)
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(2000)) continue;

                    _car = World.CreateVehicle(model, at);
                    model.MarkAsNoLongerNeeded();

                    if (_car == null || !_car.Exists()) continue;

                    Log.Info("Knowai: sent a " + name + ".");
                    break;
                }

                if (_car == null || !_car.Exists()) return false;

                _car.IsPersistent = true;

                Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, _car.Handle);
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, _car.Handle, true, true);

                // Clean, lit and locked to everybody but you. A driverless cab that turns up
                // filthy has been somewhere, and this one has been nowhere -- it was on a
                // charger.
                Function.Call(Hash.SET_VEHICLE_DIRT_LEVEL, _car.Handle, 0f);
                Function.Call(Hash.SET_VEHICLE_LIGHTS, _car.Handle, 2);

                // WHITE, EVERY ONE OF THEM. A fleet is a fleet because it looks like one --
                // you should know what pulled up before you can read anything on it.
                //
                // Set as a custom RGB rather than a paint index. The index table has several
                // whites in it and they are not the same white; one of them is nearly grey.
                // 255,255,255 is the only one that cannot be argued with, and it does not
                // depend on a table nobody has documented.
                Function.Call(Hash.SET_VEHICLE_MOD_KIT, _car.Handle, 0);
                Function.Call(Hash.SET_VEHICLE_CUSTOM_PRIMARY_COLOUR, _car.Handle, 255, 255, 255);
                Function.Call(Hash.SET_VEHICLE_CUSTOM_SECONDARY_COLOUR, _car.Handle, 255, 255, 255);
                Function.Call(Hash.SET_VEHICLE_WINDOW_TINT, _car.Handle, 1);

                return Wheel();
            }
            catch (Exception ex)
            {
                Log.Debug("Could not put a Knowai out: " + ex.Message);
                return false;
            }
        }

        /// <summary>The driver you are not meant to see. See the note on the class.</summary>
        private bool Wheel()
        {
            try
            {
                var model = new Model(PedHash.Autoshop01SMM);

                if (!model.IsValid || !model.Request(1500))
                {
                    model = new Model("a_m_y_business_01");
                    if (!model.IsValid || !model.Request(1500)) return false;
                }

                _driver = World.CreatePed(model, _car.Position);
                model.MarkAsNoLongerNeeded();

                if (_driver == null || !_driver.Exists()) return false;

                _driver.IsPersistent = true;

                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, _driver.Handle, true, true);
                Function.Call(Hash.SET_PED_INTO_VEHICLE, _driver.Handle, _car.Handle, -1);

                // The whole trick, in one line.
                Function.Call(Hash.SET_ENTITY_VISIBLE, _driver.Handle, false, false);

                // And the consequences of it. Nobody can see him, so nobody knows to leave him
                // alone -- and a car that swerves because its invisible occupant flinched at a
                // backfire is a very strange thing to be sat in.
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, _driver.Handle, true);
                Function.Call(Hash.SET_ENTITY_INVINCIBLE, _driver.Handle, true);
                Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, _driver.Handle, false);
                Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, _driver.Handle, false);
                Function.Call(Hash.SET_PED_CONFIG_FLAG, _driver.Handle, 251, true);

                // AND HE DOES NOT TALK. A ped nobody can see still has a voice, and a
                // driverless car whose empty driver's seat says "watch it!" at a cyclist is
                // the whole illusion gone in one line of dialogue.
                Function.Call(Hash.STOP_PED_SPEAKING, _driver.Handle, true);
                Function.Call(Hash.DISABLE_PED_PAIN_AUDIO, _driver.Handle, true);

                // AND NOBODY ELSE DRIVES IT. Without this, walking up and pressing the game's
                // own enter key puts the player at the wheel -- on top of a man he cannot see,
                // in a car that is meant to drive itself. With it, every way in is a passenger
                // seat and the only question left is which one.
                Function.Call(Hash.SET_VEHICLE_EXCLUSIVE_DRIVER, _car.Handle, _driver.Handle, 0);

                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not seat a Knowai driver: " + ex.Message);
                return false;
            }
        }

        private void Send(Vector3 to)
        {
            Drive(to, 20f);
        }

        private void Drive(Vector3 to, float speed)
        {
            try
            {
                Function.Call(Hash.CLEAR_PED_TASKS, _driver.Handle);

                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                              _driver.Handle, _car.Handle, to.X, to.Y, to.Z,
                              speed, 786603, 8f);

                Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, _driver.Handle, speed);
                Function.Call(Hash.SET_PED_KEEP_TASK, _driver.Handle, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not send a Knowai on: " + ex.Message);
            }
        }

        private void Halt()
        {
            try
            {
                Function.Call(Hash.CLEAR_PED_TASKS, _driver.Handle);
                Function.Call(Hash.TASK_VEHICLE_TEMP_ACTION, _driver.Handle, _car.Handle, 1, 3000);
            }
            catch
            {
                // It stops when it stops.
            }
        }

        /// <summary>Sends it off to be somebody else's ride.</summary>
        private void Away()
        {
            try
            {
                if (_blip != null && _blip.Exists()) _blip.Delete();
                _blip = null;

                Function.Call(Hash.CLEAR_PED_TASKS, _driver.Handle);

                Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, _driver.Handle, _car.Handle,
                              18f, 786603);

                Function.Call(Hash.SET_PED_KEEP_TASK, _driver.Handle, true);
            }
            catch
            {
                // It will be cleaned up either way.
            }
        }

        // ---- where -------------------------------------------------------------

        /// <summary>
        /// The nearest kerb to a point.
        ///
        /// Every stop in the table is a place somebody stands, and half of them are indoors.
        /// A car cannot go to a kitchen counter, so nothing is ever driven to the coordinate
        /// itself -- it is driven to the road outside it, which is where a taxi drops you.
        /// </summary>
        private static Vector3 OnRoad(Vector3 near)
        {
            try
            {
                var at = World.GetNextPositionOnStreet(near, true);
                return at == Vector3.Zero ? near : at;
            }
            catch
            {
                return near;
            }
        }

        private Vector3 Somewhere(Vector3 near)
        {
            for (var tries = 0; tries < 14; tries++)
            {
                try
                {
                    var away = ComeFromMin + (float)_rng.NextDouble() * (ComeFromMax - ComeFromMin);
                    var probe = near.Around(away);

                    var at = World.GetNextPositionOnStreet(probe, true);

                    if (at == Vector3.Zero) continue;
                    if (at.DistanceTo(near) < ComeFromMin * 0.6f) continue;

                    return at;
                }
                catch
                {
                    // Next try.
                }
            }

            return Vector3.Zero;
        }

        // ---- housekeeping -------------------------------------------------------

        private void Begin(RideState state)
        {
            State = state;
            _phaseFrom = Game.GameTime;

            // Fresh for every leg. Carrying the last one's reading over means the first look
            // of a new leg compares against where the car was before it was given a new route,
            // which reads as movement whether or not there has been any.
            _lookedAt = 0;
            _nudges = 0;
            _wasAt = _car != null && _car.Exists() ? _car.Position : Vector3.Zero;
        }

        private void Mark()
        {
            try
            {
                if (_car == null || !_car.Exists()) return;

                _blip = _car.AddBlip();
                if (_blip == null || !_blip.Exists()) return;

                _blip.Sprite = BlipSprite.PersonalVehicleCar;
                _blip.Color = BlipColor.Blue;
                _blip.Scale = 0.8f;
                _blip.Name = "Knowai";
            }
            catch (Exception ex)
            {
                Log.Debug("Could not blip a Knowai: " + ex.Message);
            }
        }

        private void Clean()
        {
            try
            {
                if (_blip != null && _blip.Exists()) _blip.Delete();
                _blip = null;

                // IT DRIVES OFF. IT IS NOT ABANDONED.
                //
                // This used to delete the driver and hand the car back, which is the worst of
                // both: the only thing that could move the car was destroyed, and the car was
                // then given to a game that parks loose vehicles and leaves them. What that
                // produced was a white driverless cab sat on the kerb for the rest of the
                // session -- and "driverless" is the one thing this car is supposed to be, so
                // it looked deliberate.
                //
                // Both are kept instead, and both stay OURS. The driver has to stay alive to
                // drive, and he has to stay ours to stay invisible -- an invisible ped handed
                // back is a ped the game will render whenever it feels like it, which is a
                // stranger appearing behind the wheel of the car you just got out of.
                //
                // Follow() then watches it go and deletes the pair once nobody is looking, or
                // after two minutes if it cannot get anywhere.
                if (_car != null && _car.Exists())
                {
                    _leaving = _car;
                    _leftAt = Game.GameTime;
                    _goneDriver = _driver;

                    try
                    {
                        if (_driver != null && _driver.Exists())
                        {
                            Function.Call(Hash.CLEAR_PED_TASKS, _driver.Handle);

                            Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, _driver.Handle,
                                          _car.Handle, 18f, 786603);

                            Function.Call(Hash.SET_PED_KEEP_TASK, _driver.Handle, true);
                        }
                    }
                    catch
                    {
                        // It gets deleted on the timer instead.
                    }
                }
                else if (_driver != null && _driver.Exists())
                {
                    // No car to drive, so there is nothing for him to do and nothing to see.
                    _driver.Delete();
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Knowai cleanup: " + ex.Message);
            }

            _car = null;
            _driver = null;
            _to = null;
        }

        /// <summary>
        /// See it off the premises.
        ///
        /// Two ways it ends and the second one is the point. Normally it drives away and is
        /// deleted once it is far enough off that nobody watches it happen. But a car that has
        /// just told you it could not reach you is, by its own admission, a car that cannot get
        /// anywhere -- boxed in, wedged, on the wrong side of a closed road -- so it cannot be
        /// left to the first rule or it would sit there for ever. Two minutes and it goes
        /// wherever it is.
        ///
        /// The driver is kept invisible the whole way out, for the same reason he is kept
        /// invisible during the ride: he is not supposed to exist, and the moment he is seen
        /// the car stops being driverless.
        /// </summary>
        private void Follow()
        {
            if (_leaving == null) return;

            if (!_leaving.Exists())
            {
                _leaving = null;
                _goneDriver = null;
                return;
            }

            if (_goneDriver != null && _goneDriver.Exists())
            {
                try { Function.Call(Hash.SET_ENTITY_VISIBLE, _goneDriver.Handle, false, false); }
                catch { /* next tick */ }
            }

            var stranded = _goneDriver == null || !_goneDriver.Exists() || !_goneDriver.IsAlive;
            var out_ = Game.GameTime - _leftAt > GiveUpMs;
            var away = false;

            try
            {
                var you = Game.Player.Character;

                if (you != null && you.Exists())
                {
                    away = _leaving.Position.DistanceTo(you.Position) > OutOfSight;
                }
            }
            catch
            {
            }

            if (!stranded && !out_ && !away) return;

            try
            {
                if (_goneDriver != null && _goneDriver.Exists()) _goneDriver.Delete();
                if (_leaving.Exists()) _leaving.Delete();
            }
            catch
            {
                // It is going either way.
            }

            _leaving = null;
            _goneDriver = null;
        }

        /// <summary>How far away is out of sight, and how long a stuck one gets.</summary>
        private const float OutOfSight = 120f;
        private const int GiveUpMs = 120000;

        private Vehicle _leaving;
        private Ped _goneDriver;
        private int _leftAt;

        /// <summary>Teardown. An invisible man in a car is not a thing to leave behind.</summary>
        public void RestoreWorld()
        {
            try
            {
                if (_driver != null && _driver.Exists()) _driver.Delete();
                if (_car != null && _car.Exists()) _car.Delete();
                if (_blip != null && _blip.Exists()) _blip.Delete();

                // Including one already on its way out. This is the hard teardown, and a car
                // we had promised to delete is still a car we promised to delete.
                if (_goneDriver != null && _goneDriver.Exists()) _goneDriver.Delete();
                if (_leaving != null && _leaving.Exists()) _leaving.Delete();
            }
            catch
            {
                // Teardown.
            }

            _car = null;
            _driver = null;
            _blip = null;
            _to = null;
            _leaving = null;
            _goneDriver = null;

            State = RideState.None;
            Going = "";
        }
    }
}
