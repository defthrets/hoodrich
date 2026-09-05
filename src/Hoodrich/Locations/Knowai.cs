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

        /// <summary>Pulled in at a marker you dropped on the map mid-ride. Carries on after.</summary>
        Stopped,

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
        /// exists. See Theme.
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
        private static void Theme()
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

        /// <summary>How long it sits at a stop with you still in the back before it carries on itself.</summary>
        private const int StopIdleMs = 10000;

        /// <summary>How often it looks at the map for a marker while it is driving.</summary>
        private const int MarkEveryMs = 1000;

        /// <summary>How long it will sit at the kerb before it gives up on you.</summary>
        private const int WaitMs = 120000;

        /// <summary>How close you have to be for it to offer you a way in.</summary>
        private const float BoardRange = 12f;

        /// <summary>And how long anything else is given before it is written off.</summary>
        private const int PhaseCapMs = 300000;

        /// <summary>Once you are out and it has driven this far, it stops existing.</summary>
        /// <summary>
        /// Kept for the ride itself. The leaving is measured by OutOfSight now -- see Follow --
        /// because "far enough that deleting it is not something anybody sees" and "far enough
        /// that the ride is over" are different questions with different right answers.
        /// </summary>
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

        /// <summary>A stop on the way: a marker dropped on the map mid-ride. Null when there is none.</summary>
        private RideStop _stop;
        private Vector3 _stopAt;

        /// <summary>Whether you got out at the stop, which is what it waits on before carrying on.</summary>
        private bool _stopLeft;

        /// <summary>The marker as it was last seen, so one already known is not a new stop.</summary>
        private Vector3 _sawMark;
        private int _markLookedAt;

        /// <summary>Where it was when last looked at, and when that was.</summary>
        private Vector3 _wasAt;
        private int _lookedAt;
        private int _nudges;

        /// <summary>Barely moved in this long, this many times over, and it is written off.</summary>
        private const float StuckMoved = 3f;

        /// <summary>
        /// Five seconds, down from nine, and five tries instead of three.
        ///
        /// Nine seconds of a car not moving is a long time to sit in the back of something
        /// that is supposed to be taking you somewhere -- and the old number was chosen when
        /// the only response was to re-route, which mostly did not work, so being slow to do
        /// it was hiding how often it failed. Now that the response actually frees the car,
        /// it is worth doing sooner and worth doing more times before giving up.
        /// </summary>
        private const int StuckLookMs = 5000;
        private const int MaxNudges = 5;

        /// <summary>How long it reverses for before trying the route again.</summary>
        private const int ReverseMs = 2000;

        // ---- hailing ------------------------------------------------------------

        /// <summary>
        /// Requests one, to somewhere. Returns a player-facing refusal, or null once it is on
        /// its way.
        ///
        /// THE DESTINATION COMES FIRST NOW. It used to be asked from the back seat, which is
        /// how a street cab works and not how an app does: you found out the fare after a
        /// two-minute wait for the car, from a list with nothing on it but names. The picker
        /// (UI.RideScreen) shows the place and the fare before anything is sent, and the car
        /// pulls away for it the moment you are in. Null still means the old shape -- get in,
        /// and it asks -- which is also where a booked place it cannot reach falls back to.
        /// </summary>
        public string Hail(RideStop to)
        {
            if (IsRunning) return "You've already got one coming.";

            if (Busy != null && Busy()) return "Not right now.";

            var player = Game.Player.Character;

            if (player == null || !player.Exists() || !player.IsAlive) return "Not right now.";
            if (player.IsInVehicle()) return "Get out of the car first.";

            var start = Somewhere(player.Position);
            if (start == Vector3.Zero) return "Nothing free near you.";

            if (!Make(start)) return "Nothing free near you.";

            Theme();

            _to = to;
            _stop = null;
            _stopLeft = false;
            _sawMark = Vector3.Zero;
            Going = to == null ? "" : to.Name;
            _fare = 0;

            Send(player.Position);
            Mark();

            Begin(RideState.Coming);

            Notify.Card(Face, "Knowai", "on the way",
                        to == null ? "A car's been assigned. Sit tight."
                                   : "A car's been assigned for " + to.Name + ". Sit tight.");

            Log.Info("Knowai: pickup requested" + (to == null ? "." : " for " + to.Name + "."));

            return null;
        }

        /// <summary>
        /// Where to, chosen from the back seat -- the fallback for a ride booked without a
        /// place, or to one it could not reach. Returns a refusal or null.
        /// </summary>
        public string Go(RideStop stop)
        {
            if (stop == null) return "Nowhere selected.";
            if (State != RideState.Picking) return "Not in a Knowai.";

            return Depart(stop) ? null : "Knowai doesn't go there.";
        }

        /// <summary>
        /// What it would cost from where he is stood, or -1 when it will not go there. For
        /// the picker, which shows the number before anything is sent.
        /// </summary>
        public int Quote(RideStop stop)
        {
            if (stop == null) return -1;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return -1;

            var drop = OnRoad(stop.At);
            if (drop == Vector3.Zero) return -1;

            return Flagfall + (int)(player.Position.DistanceTo(drop) / 100f * PerHundred);
        }

        /// <summary>Pulls away for a place, with him in the back. False when there is no road to it.</summary>
        private bool Depart(RideStop stop)
        {
            var drop = OnRoad(stop.At);
            if (drop == Vector3.Zero) return false;

            var player = Game.Player.Character;
            var from = player != null && player.Exists() ? player.Position : _car.Position;

            _to = stop;
            _dropAt = drop;
            _fare = Flagfall + (int)(from.DistanceTo(drop) / 100f * PerHundred);

            // A marker already on the map when it pulls away is not a stop -- it is either
            // the destination itself or something older -- so it is noted as seen. Only one
            // dropped on the way is (see Detour).
            var mark = Waypoint();
            _sawMark = mark == null ? Vector3.Zero : mark.At;
            _stop = null;
            _stopLeft = false;

            Going = stop.Name;

            Begin(RideState.Riding);
            Drive(_dropAt, 22f);

            Line();

            // THE FARE ON ITS OWN. The destination is already on the row you just pressed,
            // on the blip, and out of the window in about a minute; the number is the only
            // thing here you did not already know.
            Notify.Card(Face, "Knowai", stop.Name, "$" + _fare);

            return true;
        }

        /// <summary>Called off, from the app or from anything going wrong.</summary>
        public void Cancel(string why)
        {
            if (!string.IsNullOrEmpty(why)) Notify.Failure(why);

            Clean();
            _stop = null;
            _stopLeft = false;
            _sawMark = Vector3.Zero;
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

                // The same, every tick, for the same reason: the visible flag comes back when a
                // ped streams out and in. A driver who reappears is a stranger at the wheel; a
                // passenger who reappears is a stranger sat next to you, which is worse.
                if (_rider != null && _rider.Exists())
                {
                    try { Function.Call(Hash.SET_ENTITY_VISIBLE, _rider.Handle, false, false); }
                    catch { /* next tick */ }
                }

                // The same argument as the invisible driver, for the same reason: both are
                // things that come back on their own and both are things you only notice once
                // they have broken the illusion.
                Locks();
                Backseat(player);

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
                    case RideState.Stopped: Stopped(player, now); break;
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

            // BACK UP FIRST. Re-routing on its own was asking a car with its nose against a
            // wall to work out a route, from a position where every route starts by going
            // through the wall -- so it computed the same path into the same wall and the next
            // check found it exactly where it was.
            //
            // A real driver reverses. Two seconds of it is enough to be off whatever it caught,
            // and the route is asked for after that rather than before, from a place the car
            // can actually leave. Temp action 3 is reverse-straight; the drive task is issued
            // on the NEXT check, which is what the shortened look-again gap below is for.
            Log.Info("Knowai: stuck, backing it off and re-routing (" + _nudges + ").");

            try
            {
                Function.Call(Hash.TASK_VEHICLE_TEMP_ACTION, _driver.Handle, _car.Handle,
                              3, ReverseMs);
            }
            catch
            {
                // Straight to the re-route then.
            }

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
                // BOOKED AHEAD: it already knows, so it goes. Only a place it cannot reach
                // by road falls back to asking.
                if (_to != null && Depart(_to)) return;

                Begin(RideState.Picking);

                try { if (Choose != null) Choose(); }
                catch (Exception ex) { Log.Debug("Could not ask where to: " + ex.Message); }

                return;
            }

            // ONLY WHEN YOU ARE NEXT TO IT.
            //
            // The prompt used to show from the moment it parked, whatever street you were on --
            // so a car waiting round the corner put "get in your Knowai" across the top of the
            // screen for two solid minutes while you were doing something else entirely. A
            // prompt for a thing you cannot do from where you are stood is not a prompt, it is
            // a caption.
            //
            // The keypress is gated on the same distance rather than just the text. A prompt
            // you cannot see is not a reason to still be able to press it -- and this key talks
            // to everybody else in the mod, so an ungated one is a Knowai you board by accident
            // while trying to speak to somebody a street away from it.
            if (player.Position.DistanceTo(_car.Position) > BoardRange) return;

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

            var now = Game.GameTime;

            Detour(now);

            if (Stuck(now, _stop != null ? _stopAt : _dropAt))
            {
                Charge?.Invoke(_fare);

                Notify.Card(Face, "Knowai", "ride ended", "Couldn't get through. $" + _fare);

                Away();
                Cancel("");
                return;
            }

            // THE STOP FIRST, when there is one.
            if (_stop != null)
            {
                if (_car.Position.DistanceTo(_stopAt) > DropRange) return;

                Halt();
                Begin(RideState.Stopped);

                try { Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, _car.Handle, 1); }
                catch { /* unlocked is the default */ }

                Notify.Card(Face, "Knowai", "your stop", "It'll wait. Get out, or carry on.");
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

        /// <summary>
        /// A marker dropped on the map mid-ride is a stop on the way. Looked for once a
        /// second, and only a marker that is new since the last look counts: the one that
        /// was already there when the car pulled away was noted in Depart, and the same one
        /// twice is the same one.
        /// </summary>
        private void Detour(int now)
        {
            if (_stop != null || now - _markLookedAt < MarkEveryMs) return;
            _markLookedAt = now;

            var mark = Waypoint();

            if (mark == null)
            {
                _sawMark = Vector3.Zero;
                return;
            }

            if (_sawMark != Vector3.Zero && mark.At.DistanceTo(_sawMark) < 5f) return;
            _sawMark = mark.At;

            // Not the destination, and not somewhere the car is already sat.
            if (mark.At.DistanceTo(_dropAt) < DropRange * 2f) return;
            if (mark.At.DistanceTo(_car.Position) < DropRange) return;

            _stop = mark;
            _stopAt = mark.At;
            _stopLeft = false;

            // The extra goes on the meter: out to the stop and on from it, less the straight
            // run it would have been.
            var here = _car.Position;
            var extra = here.DistanceTo(_stopAt) + _stopAt.DistanceTo(_dropAt) - here.DistanceTo(_dropAt);
            if (extra > 0f) _fare += (int)(extra / 100f * PerHundred);

            Begin(RideState.Riding);
            Drive(_stopAt, 22f);

            Notify.Card(Face, "Knowai", "stopping first", "Swinging by your marker on the way. $" + _fare);
        }

        /// <summary>
        /// Pulled in at the stop. With you still in the back it waits a moment for a word,
        /// then carries on itself; once you have got out it waits for you to get back in,
        /// as long as it would have waited at the kerb in the first place.
        /// </summary>
        private void Stopped(Ped player, int now)
        {
            if (player.IsInVehicle(_car))
            {
                if (_stopLeft)
                {
                    Resume();
                    return;
                }

                Help.ShowThisFrame("Your stop. Get out, or press ~INPUT_CELLPHONE_RIGHT~ to carry on to " + _to.Name + ".");

                if (Tapped() || now - _phaseFrom > StopIdleMs) Resume();

                return;
            }

            if (!_stopLeft)
            {
                _stopLeft = true;
                _phaseFrom = now;
            }

            if (now - _phaseFrom < WaitMs) return;

            Charge?.Invoke(_fare);
            Notify.Card(Face, "Knowai", "ride ended", "It gave up waiting at your stop. $" + _fare);
            Away();
            Cancel("");
        }

        private void Resume()
        {
            _stop = null;
            _stopLeft = false;
            _sawMark = Vector3.Zero;

            // The marker has been used, and left on the map it would be found again.
            try { Function.Call(Hash.SET_WAYPOINT_OFF); }
            catch { /* it may already be gone */ }

            Begin(RideState.Riding);
            Drive(_dropAt, 22f);

            Notify.Card(Face, "Knowai", "carrying on", _to.Name + ". $" + _fare);
        }

        private void Arrived(Ped player)
        {
            if (player.IsInVehicle(_car))
            {
                Help.ShowThisFrame("You're here. Get out.");
                return;
            }

            // OUT, AND IT GOES. It used to sit at the kerb for the rest of the session.
            //
            // Two faults, and the first one is the whole reason it never moved. Away() was
            // called from here EVERY TICK for as long as the car was within a hundred and
            // thirty metres -- and the first thing Away() does is CLEAR_PED_TASKS. So the
            // drive-off was cancelled and reissued several times a second, and a driver handed
            // a fresh task before he has pulled out never pulls out. It was being told to leave
            // so often that it could not.
            //
            // The second is that leaving was measured by distance with nothing behind it. A car
            // that could not get a hundred and thirty metres away -- boxed in, on a dead end,
            // nose against the kerb it just parked on -- stayed for ever, and there was no
            // other way out of this state.
            //
            // Both go. It is handed to the leaving machinery instead, which is the same code
            // that sees off a cancelled ride: the driver is kept so there is somebody to drive
            // it, he is kept invisible on the way out, and the pair are deleted once they are
            // out of sight or after two minutes if they never manage it. Once, from here, and
            // then this state is finished with.
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
                // CLEAR GLASS. Tint 1 is pure black, which is the one thing a car with
                // nobody in it must not have -- the whole joke is that you can see there is
                // no driver, and blacked-out windows hide the only thing worth looking at.
                // 0 is the stock glass the model shipped with.
                Function.Call(Hash.SET_VEHICLE_WINDOW_TINT, _car.Handle, 0);

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

                // AND SOMEBODY IS ALREADY SITTING IN THE FRONT.
                //
                // A TAKEN SEAT IS THE ONLY BLOCK THE GAME CANNOT ARGUE WITH. Everything else
                // tried here was a rule about the seat rather than the seat itself: exclusive
                // driver only defends the wheel, the door locks are a request the game grants
                // until something else overrides them, and the move-him-to-the-back check runs
                // AFTER the player is already sat where he should not be. Each of those is a
                // way of saying "please do not", and all three can be got round.
                //
                // Occupied cannot. There is no seat to take because a man is in it, and the
                // game's own entry logic simply routes you to the back the way it routes you
                // round any full seat -- no rule, no correction, no frame where you are sat in
                // the wrong place.
                //
                // The other three stay. This one is the wall; they are the signs on it, and a
                // sign that is never read costs nothing.
                Riding();

                // THE BEST DRIVER IN THE CITY, WHICH IS THE PRODUCT.
                //
                // Ability at maximum, because there is no argument for a self-driving car that
                // is worse at driving than the man it replaced.
                //
                // Aggression at a third rather than at nothing, and that is the counter-intuitive
                // one. A driver on zero does not drive calmly, it drives TIMIDLY -- it will not
                // commit to a gap, will not pull out at a junction with anything approaching, and
                // waits for a road that is completely empty before it moves. Which reads as
                // exactly the thing being complained about: stuck. A third is decisive without
                // being a maniac.
                Function.Call(Hash.SET_DRIVER_ABILITY, _driver.Handle, 1.0f);
                Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, _driver.Handle, 0.35f);

                // Said again as ped behaviour, because the driving style is a property of the
                // TASK and these are properties of the DRIVER -- a re-task that forgot the
                // style would still have a driver who steers round things.
                Function.Call(Hash.SET_PED_STEERS_AROUND_VEHICLES, _driver.Handle, true);
                Function.Call(Hash.SET_PED_STEERS_AROUND_PEDS, _driver.Handle, true);
                Function.Call(Hash.SET_PED_STEERS_AROUND_OBJECTS, _driver.Handle, true);

                // Nothing takes his hands off the wheel. He cannot be scared out of the car,
                // cannot be dragged out of it, and does not stop driving because somebody
                // nearby started shooting -- all of which end with a passenger sat in a
                // stationary taxi wondering what happened.
                Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, _driver.Handle, 0, false);
                Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, _driver.Handle, false);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, _driver.Handle, 17, false);
                Function.Call(Hash.SET_ENTITY_INVINCIBLE, _driver.Handle, true);

                // AND THE FRONT DOORS DO NOT OPEN FOR YOU EITHER.
                //
                // Exclusive driver only defends the WHEEL. It stops the player taking the
                // driver's seat and then the game does the helpful thing and puts him in the
                // next one along -- the front passenger seat, beside a man he cannot see, in a
                // car with nobody driving it. Which is the one seat in this vehicle that makes
                // the whole idea fall over.
                //
                // Both front doors are locked individually, so the rear ones still work and
                // every ordinary way in is a back seat. Locking the vehicle outright would
                // lock him out of his own taxi.
                Locks();

                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not seat a Knowai driver: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Lock the two front doors and leave the back ones alone.
        ///
        /// Re-asserted rather than set once, because a door lock is one of the things that
        /// comes back when a vehicle streams out and in again -- and the car spends its whole
        /// life driving between two places the player may not be standing at.
        ///
        /// The native is addressed by hash. SET_VEHICLE_INDIVIDUAL_DOORS_LOCKED is not carried
        /// by every build of the scripting library under the same name, and a name that is
        /// missing is a mod that will not compile, while a hash that is wrong is a door that
        /// stays unlocked.
        /// </summary>
        private void Locks()
        {
            if (_car == null || !_car.Exists()) return;

            try
            {
                // Door 0 is the driver's, door 1 the front passenger. 2 is "locked".
                Function.Call(Hash.SET_VEHICLE_INDIVIDUAL_DOORS_LOCKED, _car.Handle, 0, 2);
                Function.Call(Hash.SET_VEHICLE_INDIVIDUAL_DOORS_LOCKED, _car.Handle, 1, 2);
            }
            catch
            {
                // The seat check below is the one that actually has to hold.
            }
        }

        /// <summary>
        /// And if he is in the front anyway, he is moved.
        ///
        /// The doors are the polite version and this is the one that cannot be argued with.
        /// There are ways into a front seat that no lock covers -- another mod warping him, a
        /// cutscene putting him back, the exclusive driver being cleared by something else --
        /// and all of them end with the player sat where the illusion breaks.
        ///
        /// Near-side rear first, off-side if something is already there. If both are somehow
        /// taken there is nowhere to move him to and he stays where he is, which is better
        /// than putting him out of the car mid-fare.
        /// </summary>
        private void Backseat(Ped player)
        {
            if (_car == null || !_car.Exists()) return;
            if (player == null || !player.Exists() || !player.IsInVehicle(_car)) return;

            try
            {
                var upFront =
                    Function.Call<int>(Hash.GET_PED_IN_VEHICLE_SEAT, _car.Handle, -1) == player.Handle
                    || Function.Call<int>(Hash.GET_PED_IN_VEHICLE_SEAT, _car.Handle, 0) == player.Handle;

                if (!upFront) return;

                var seat = RearSeat;

                if (!Function.Call<bool>(Hash.IS_VEHICLE_SEAT_FREE, _car.Handle, RearSeat))
                {
                    if (!Function.Call<bool>(Hash.IS_VEHICLE_SEAT_FREE, _car.Handle, 1)) return;
                    seat = 1;
                }

                Function.Call(Hash.SET_PED_INTO_VEHICLE, player.Handle, _car.Handle, seat);
                Log.Info("Knowai: he got in the front, so he was moved to the back.");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not move him into the back: " + ex.Message);
            }
        }

        /// <summary>
        /// The passenger nobody can see, whose entire job is to be in the way.
        ///
        /// Made the same way as the driver and for the same reasons -- invisible, deaf, silent,
        /// invincible, and impossible to drag out. A ped nobody can see still reacts, still
        /// talks, and can still be pulled out of a car by a stranger, and all three of those
        /// end with somebody appearing out of nowhere on a pavement.
        ///
        /// He is a passenger rather than the driver, so he is not the one the exclusive-driver
        /// lock names, and nothing about him touches how the car drives.
        ///
        /// If he cannot be made, nothing is lost: the door locks and the move-to-the-back check
        /// were doing this job on their own before he existed, and they still run.
        /// </summary>
        private void Riding()
        {
            try
            {
                var model = new Model(PedHash.Autoshop01SMM);

                if (!model.IsValid || !model.Request(1500))
                {
                    model = new Model("a_m_y_business_01");
                    if (!model.IsValid || !model.Request(1500)) return;
                }

                _rider = World.CreatePed(model, _car.Position);
                model.MarkAsNoLongerNeeded();

                if (_rider == null || !_rider.Exists()) return;

                _rider.IsPersistent = true;

                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, _rider.Handle, true, true);

                // Seat 0 is the front passenger -- the one seat this car has that a rider
                // should never be in, which is exactly why it is filled.
                Function.Call(Hash.SET_PED_INTO_VEHICLE, _rider.Handle, _car.Handle, 0);

                Function.Call(Hash.SET_ENTITY_VISIBLE, _rider.Handle, false, false);
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, _rider.Handle, true);
                Function.Call(Hash.SET_ENTITY_INVINCIBLE, _rider.Handle, true);
                Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, _rider.Handle, false);
                Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, _rider.Handle, false);
                Function.Call(Hash.SET_PED_CONFIG_FLAG, _rider.Handle, 251, true);

                Function.Call(Hash.STOP_PED_SPEAKING, _rider.Handle, true);
                Function.Call(Hash.DISABLE_PED_PAIN_AUDIO, _rider.Handle, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Knowai: no front passenger: " + ex.Message);

                _rider = null;
            }
        }

        private void Send(Vector3 to)
        {
            Drive(to, 20f);
        }

        /// <summary>
        /// Wherever you have put a marker on the map, as somewhere to be driven.
        ///
        /// THE LIST OF PLACES IS A LIST OF PLACES WE THOUGHT OF. It is a good list and it is
        /// still the fast way to the ones you use, but a car you can only take to eleven
        /// addresses is not a car service. A waypoint is you saying where, in the one way the
        /// game already has for saying it.
        ///
        /// SNAPPED TO A ROAD, which is the part that matters. A map marker is a point on a
        /// texture -- it lands in the sea, on a roof, in the middle of a field -- and its Z is
        /// nothing at all, because the map is flat. The nearest vehicle node is a place a car
        /// can actually be, so that is what gets driven to.
        ///
        /// Null when there is no marker, which is what keeps the row off the list rather than
        /// putting a dead one on it.
        /// </summary>
        public static RideStop Waypoint()
        {
            try
            {
                if (!Function.Call<bool>(Hash.IS_WAYPOINT_ACTIVE)) return null;

                var flat = World.WaypointPosition;

                if (flat == Vector3.Zero) return null;

                // GetNextPositionOnStreet rather than the node native directly: the native
                // hands its answer back through a pointer, this build is not compiled unsafe,
                // and the wrapper is the same lookup without needing it to be.
                var road = World.GetNextPositionOnStreet(flat, true);

                if (road == Vector3.Zero) return null;

                return new RideStop { Name = "Your waypoint", Area = "On the map", At = road };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Knowai's own line on the map for the trip you are on.
        ///
        /// THE CAR IS DRIVING AND YOU ARE NOT, which is exactly why this is worth having: the
        /// one thing you can do in the back seat is watch where you are being taken, and until
        /// now there was nothing on the map that said. The game draws a route to a blip when
        /// asked, so it is asked.
        ///
        /// ITS OWN COLOUR, not the game's yellow. A yellow line is the one YOU set, and having
        /// the car overwrite it would take away the waypoint you had before you got in -- this
        /// is a second line, in the app's blue, alongside whatever you were already following.
        ///
        /// Short range off, obviously: a route to a blip you cannot see is not a route.
        /// </summary>
        private void Line()
        {
            Unline();

            try
            {
                if (_to == null) return;

                _route = World.CreateBlip(_dropAt);

                if (_route == null || !_route.Exists()) return;

                _route.Sprite = BlipSprite.Standard;
                _route.Color = BlipColor.Blue;
                _route.Scale = 0.9f;
                _route.Name = "Knowai -- " + _to.Name;
                _route.IsShortRange = false;

                Function.Call(Hash.SET_BLIP_ROUTE, _route.Handle, true);
                Function.Call(Hash.SET_BLIP_ROUTE_COLOUR, _route.Handle, 3);
            }
            catch (Exception ex)
            {
                Log.Debug("Knowai could not draw its route: " + ex.Message);

                _route = null;
            }
        }

        /// <summary>The line goes when the trip does. A route to nowhere outlives the ride.</summary>
        private void Unline()
        {
            try
            {
                if (_route != null && _route.Exists())
                {
                    Function.Call(Hash.SET_BLIP_ROUTE, _route.Handle, false);
                    _route.Delete();
                }
            }
            catch
            {
                // It is going either way.
            }

            _route = null;
        }

        /// <summary>The destination blip that carries the route. See Line.</summary>
        private Blip _route;

        private void Drive(Vector3 to, float speed)
        {
            try
            {
                Function.Call(Hash.CLEAR_PED_TASKS, _driver.Handle);

                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                              _driver.Handle, _car.Handle, to.X, to.Y, to.Z,
                              speed, Style, 8f);

                Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, _driver.Handle, speed);
                Function.Call(Hash.SET_DRIVE_TASK_DRIVING_STYLE, _driver.Handle, Style);
                Function.Call(Hash.SET_PED_KEEP_TASK, _driver.Handle, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not send a Knowai on: " + ex.Message);
            }
        }

        /// <summary>
        /// How it drives, and this is what was wrong with it.
        ///
        /// IT WAS 786603, WHICH IS THE GAME'S ORDINARY TRAFFIC STYLE. Read as flags that is
        /// stop-before-vehicles, stop-before-peds, avoid-EMPTY-vehicles, avoid-objects and
        /// stop-at-lights -- and what it does NOT contain is the bit for steering around a
        /// vehicle that has somebody in it, or around a person. So the car stopped for things
        /// and never went round them. A double-parked van, a bin lorry, a car waiting to turn:
        /// each one is a full stop it has no instruction to solve, and it sits there until the
        /// obstacle decides to move.
        ///
        /// That is not a bad driver. That is a driver that was never told it was allowed to
        /// overtake anything.
        ///
        ///     1   stop before vehicles          16   steer around peds
        ///     2   stop before peds              32   steer around objects
        ///     4   steer around vehicles        128   stop at lights
        ///     8   steer around empty vehicles  256   indicate
        ///
        /// All eight. It still stops for people and still stops at lights -- a self-driving
        /// taxi that runs reds is a different kind of wrong -- but it now goes round the things
        /// that are never going to move.
        /// </summary>
        private const int Style = 1 | 2 | 4 | 8 | 16 | 32 | 128 | 256;

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

                Unline();

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

                Unline();

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
                // THE FRONT PASSENGER GOES NOW, not with the car. He exists to keep you out
                // of a seat, and once the ride is over there is nobody left to keep out -- so
                // he is deleted here rather than being carried along on the way out. The
                // driver is kept because the driver has a job to do; this one does not.
                try { if (_rider != null && _rider.Exists()) _rider.Delete(); }
                catch { }

                _rider = null;

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
            _rider = null;
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

        /// <summary>The passenger who is only there to be in the way. See Riding.</summary>
        private Ped _rider;

        private Vehicle _leaving;
        private Ped _goneDriver;
        private int _leftAt;

        /// <summary>Teardown. An invisible man in a car is not a thing to leave behind.</summary>
        public void RestoreWorld()
        {
            try
            {
                if (_driver != null && _driver.Exists()) _driver.Delete();
                if (_rider != null && _rider.Exists()) _rider.Delete();
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
            _rider = null;
            _blip = null;
            _to = null;
            _stop = null;
            _leaving = null;
            _goneDriver = null;

            State = RideState.None;
            Going = "";
        }
    }
}
