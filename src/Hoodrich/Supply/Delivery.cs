using System;
using Control = GTA.Control;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.UI;

namespace Hoodrich.Supply
{
    /// <summary>Where a delivery has got to.</summary>
    internal enum DeliveryState
    {
        None,

        /// <summary>Player has the phone to their ear.</summary>
        Calling,

        /// <summary>He is driving over.</summary>
        Driving,

        /// <summary>He has pulled up outside the house and is waiting on you.</summary>
        Waiting,

        /// <summary>Box out of the boot, walking it to the door.</summary>
        Carrying,

        /// <summary>Box down. Back to the car and gone.</summary>
        Leaving
    }

    /// <summary>
    /// The dock worker bringing it to you.
    ///
    /// Everything else in the supply chain makes you travel, and that is the point of it -- but
    /// the docks are the reward for having moved real weight, so the docks come to you. You
    /// phone him, he sets off from somewhere out of sight, and he drives the whole way: no
    /// teleport, no popping into existence at the kerb. What you are buying with that phone
    /// call is the drive, which is why the car has to be real and has to actually arrive.
    /// </summary>
    internal sealed class Delivery
    {
        // ---- the drop ---------------------------------------------------------

        /// <summary>
        /// Exactly where he stops, and which way he is pointing when he does.
        ///
        /// Read off the HUD sitting in the spot. He is routed to a point up the street to the
        /// WEST first so he enters facing the right way, and sent off east afterwards -- pulling
        /// a U-turn outside the house every time reads as a car that cannot drive rather than a
        /// man who does this for a living.
        /// </summary>
        private static readonly Vector3 ParkSpot = new Vector3(-21.817f, -1455.482f, 30.424f);
        private const float ParkHeading = 276.921f;

        /// <summary>How far west he is aimed before the final approach.</summary>
        private const float ApproachWest = 55f;

        /// <summary>And how far east he heads once the box is down.</summary>
        private const float DepartEast = 90f;

        /// <summary>How long he spends walking it in before it counts as delivered.</summary>
        private const int CarryTimeoutMs = 45000;

        /// <summary>Close enough to the door to put it down.</summary>
        private const float DropRange = 2.2f;

        /// <summary>Once the box is down, this long before he is let go entirely.</summary>
        private const int LeaveMs = 25000;

        /// <summary>
        /// What he is actually carrying: a package, not a parcel.
        ///
        /// The game has purpose-made drug props from the story missions -- the taped bale from
        /// the trash-run, the brick from the meth deals -- and one of those in his hands says
        /// what this is without a word of dialogue. A cardboard carton says he is helping
        /// somebody move house.
        ///
        /// Tried in order, and an install missing all of them still gets the delivery: it just
        /// gets it with nothing in shot, which is a worse scene rather than a broken one, so
        /// the ordinary boxes stay on the end of the list as a last resort.
        /// </summary>
        private static readonly string[] BoxProps =
        {
            "prop_drug_package_02", "prop_drug_package", "prop_meth_bag_01",
            "prop_cash_case_01", "prop_michael_backpack",
            "prop_cs_cardbox_01", "prop_paper_box_01"
        };

        /// <summary>
        /// Carrying a box with both hands. Checked before use, because a clip that is not in
        /// this install fails silently and leaves him strolling with a crate glued to his hip.
        /// </summary>
        private static readonly string[] CarryDicts =
        {
            "anim@heists@box_carry@", "anim@heists@narcotics@trash", "missfinale_c2mcs_1"
        };

        private static readonly string[] CarryClips = { "idle", "walk", "base" };

        /// <summary>
        /// What he says, and when.
        ///
        /// Three moments worth a voice: taking the order, putting the box down, and pulling
        /// off. A man who does the whole delivery in silence is a delivery system; a man who
        /// grunts on the way past you is somebody doing you a favour at some personal risk.
        /// </summary>
        private static readonly string[] TakingLines = { "GENERIC_YES", "GENERIC_HOWS_IT_GOING" };
        private static readonly string[] DroppedLines = { "GENERIC_THANKS", "GENERIC_YES" };
        private static readonly string[] LeavingLines = { "GENERIC_BYE", "GENERIC_THANKS" };

        private static readonly Random Rng = new Random();

        private Prop _box;
        private Vector3 _dropSpot;
        private int _carryingSince;
        private int _nextNudge;

        /// <summary>What has been paid for and is still in the boot.</summary>
        private string _owedDrug = "";
        private float _owedGrams;

        /// <summary>Set by Main: the house, its door, and what is kept there.</summary>
        public Func<bool> AtHome;
        public Vector3 HouseDoor;
        public Economy.Stash House;

        /// <summary>How long the player holds the phone before he sets off.</summary>
        /// <summary>
        /// How long he is on the phone.
        ///
        /// Longer than it was, because a four-second call reads as a text message. The player is
        /// never frozen for it -- the animation runs over the top of whatever they are doing and
        /// they can walk off mid-sentence, the way anybody does on a phone.
        /// </summary>
        private const int CallMs = 11000;

        /// <summary>Spawned this far out, so the car is never seen appearing.</summary>
        private const float SpawnMinDistance = 220f;
        private const float SpawnMaxDistance = 340f;

        /// <summary>He parks about here and waits.</summary>
        /// <summary>
        /// How close to the mark counts as parked.
        ///
        /// Tight, because the mark is an exact spot outside a specific house rather than a
        /// vague "near the player" -- eighteen metres of slack put him round the corner.
        /// </summary>
        private const float ArriveDistance = 9f;

        /// <summary>Give up if the drive takes longer than this -- traffic, cliffs, the ocean.</summary>
        private const int DriveTimeoutMs = 5 * 60 * 1000;

        /// <summary>Wander off if you never come to the car.</summary>
        private const int WaitTimeoutMs = 3 * 60 * 1000;

        /// <summary>How often the route is re-aimed at a player who keeps moving.</summary>
        private const int RetaskIntervalMs = 4000;

        /// <summary>Only re-aim once you have actually gone somewhere.</summary>
        private const float RetaskMoveDistance = 35f;

        /// <summary>Far enough that he stops bothering to follow you.</summary>
        private const float AbandonDistance = 500f;

        /// <summary>
        /// He turns up in the same car every time, and it is not a work van.
        ///
        /// The Astron is a DLC model, so an install without it falls through to something else
        /// black and expensive rather than failing the delivery -- but on any current copy it
        /// is always the Astron, which is the point: you learn to recognise it coming.
        /// </summary>
        private static readonly string[] CarModels =
        {
            "astron", "baller3", "baller4", "baller2", "granger"
        };

        /// <summary>Metallic black, inside and out.</summary>
        private const int BlackPaint = 0;

        /// <summary>Window tint 5 is the limo one.</summary>
        private const int LimoTint = 5;

        private readonly Random _rng = new Random();

        private DealerDef _def;
        private Ped _driver;
        private Vehicle _car;
        private Blip _blip;

        private int _stateSince;
        private int _lastRetask;
        private Vector3 _target;

        public DeliveryState State { get; private set; } = DeliveryState.None;

        public bool IsActive => State != DeliveryState.None;

        /// <summary>Who is on the way, or null.</summary>
        public DealerDef Def => _def;

        /// <summary>The driver, once he is in the world -- the man you actually trade with.</summary>
        public Ped Driver => _driver != null && _driver.Exists() ? _driver : null;

        /// <summary>Set by Main: the conversation screen, and what he has to say.</summary>
        public Conversation Talk;
        public Func<DialogueNode> TalkBuilder;

        /// <summary>
        /// How he drives.
        ///
        /// Normal road driving with StopForVehicles dropped and SwerveAroundAllCars added, so a
        /// van double-parked on Innocence does not end the delivery -- he goes round it. He sat
        /// behind traffic indefinitely before, which reads as a broken errand rather than a
        /// careful driver.
        /// </summary>
        private const int DriveStyle = 786606;

        /// <summary>Close enough to do business over the roof of the car.</summary>
        private const float TalkRange = 4.5f;

        private bool _talkHeld;

        /// <summary>
        /// Walking up on him once he has parked.
        ///
        /// He used to be bought from through the wheel, which meant standing next to a man and
        /// opening a menu about him -- and if the range check the wheel used disagreed with
        /// where you were standing, there was nothing to press at all. Everybody else in this
        /// mod is talked to; so is he.
        /// </summary>
        public void UpdatePrompt()
        {
            if (Talk == null || Talk.IsOpen) return;
            if (State != DeliveryState.Waiting) return;

            var driver = Driver;
            if (driver == null) return;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;
            if (player.Position.DistanceTo(driver.Position) > TalkRange) return;

            Help.ShowThisFrame("Press ~INPUT_CELLPHONE_RIGHT~ to do business with " + Def.Name + ".");

            if (!Pressed()) return;

            var root = TalkBuilder == null ? null : TalkBuilder();
            if (root == null) return;

            Talk.Speaker = driver;
            Talk.Open(root, this);
        }

        private bool Pressed()
        {
            var down = false;

            try
            {
                down = Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)Control.PhoneRight)
                    || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)Control.PhoneRight)
                    || Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)Control.Context)
                    || Game.IsKeyPressed(System.Windows.Forms.Keys.Right)
                    || Game.IsKeyPressed(System.Windows.Forms.Keys.E);
            }
            catch
            {
                // Unreadable control is simply not pressed.
            }

            var pressed = down && !_talkHeld;
            _talkHeld = down;
            return pressed;
        }

        public float Distance
        {
            get
            {
                var player = Game.Player.Character;
                if (player == null || !player.Exists()) return 9999f;

                if (_car != null && _car.Exists()) return player.Position.DistanceTo(_car.Position);
                if (_driver != null && _driver.Exists()) return player.Position.DistanceTo(_driver.Position);

                return 9999f;
            }
        }

        /// <summary>One line for the wheel.</summary>
        public string Status
        {
            get
            {
                switch (State)
                {
                    case DeliveryState.Calling: return "On the phone to " + _def.Name;
                    case DeliveryState.Driving: return _def.Name + " is driving over -- " +
                                                       Distance.ToString("0") + "m";
                    case DeliveryState.Waiting: return _def.Name + " is waiting on you -- " +
                                                       Distance.ToString("0") + "m";
                    default: return "";
                }
            }
        }

        // ---- placing the call --------------------------------------------------

        /// <summary>Returns a player-facing refusal, or null once the call is placed.</summary>
        public string Call(DealerDef def)
        {
            // Only from the house. He is bringing a box to a door, so there has to be a door --
            // and it stops the plug being a vending machine you carry around with you.
            if (AtHome != null && !AtHome())
            {
                return "Call him from the house. He ain't meeting you on a corner.";
            }

            if (def == null) return "No such contact.";
            if (IsActive) return _def.Name + " is already on his way.";

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return "Not right now.";
            if (player.IsInVehicle()) return "Get out of the car to make a call.";

            _def = def;
            State = DeliveryState.Calling;
            _stateSince = Game.GameTime;

            PlayPhoneAnimation(player);

            Notify.Ticker("~y~Calling " + def.Name + "...~s~");
            Log.Info("Called " + def.Id + " out for a delivery.");
            return null;
        }

        /// <summary>
        /// Puts the phone to the player's ear for the length of the call.
        ///
        /// The timed mobile task is the game's own: it draws the phone, the hand, and the whole
        /// idle, and it ends by itself -- so there is no prop to attach and nothing left stuck
        /// in the player's hand if the script is interrupted mid-call.
        /// </summary>
        private static void PlayPhoneAnimation(Ped player)
        {
            try
            {
                // The UNTIMED form. The timed one is a full task and nails the player to the
                // spot for its whole duration, which is why a longer call was unbearable --
                // this one runs alongside walking, driving and everything else, exactly like
                // holding a phone to your ear does.
                Function.Call(Hash.TASK_USE_MOBILE_PHONE, player.Handle, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Phone animation failed: " + ex.Message);
            }
        }

        /// <summary>Puts it away again, whether the call landed or was called off.</summary>
        private static void EndPhoneAnimation()
        {
            try
            {
                var player = Game.Player.Character;
                if (player == null || !player.Exists()) return;

                Function.Call(Hash.TASK_USE_MOBILE_PHONE, player.Handle, false);
            }
            catch
            {
                // It puts itself away eventually.
            }
        }

        // ---- per-tick ----------------------------------------------------------

        public void Update()
        {
            if (!IsActive) return;

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive)
            {
                Cancel(null);
                return;
            }

            switch (State)
            {
                case DeliveryState.Calling:
                    if (Game.GameTime - _stateSince >= CallMs)
                    {
                        EndPhoneAnimation();
                        Dispatch(player);
                    }
                    return;

                case DeliveryState.Driving:
                    TickDriving(player);
                    return;

                case DeliveryState.Carrying:
                    TickCarrying();
                    return;

                case DeliveryState.Leaving:
                    TickLeaving();
                    return;

                case DeliveryState.Waiting:
                    TickWaiting(player);
                    return;
            }
        }

        /// <summary>Puts him and the car on a road far enough out to be off screen, and sends him.</summary>
        private void Dispatch(Ped player)
        {
            if (!TryStartPoint(player.Position, out var start))
            {
                Cancel("He could not get to you from where you are.");
                return;
            }

            Model? carModel = null;
            foreach (var name in CarModels)
            {
                var m = new Model(name);
                if (!m.IsValid || !m.IsInCdImage || !m.Request(2000)) continue;
                carModel = m;
                break;
            }

            if (carModel == null)
            {
                Cancel("He could not get a car out.");
                return;
            }

            try
            {
                _car = World.CreateVehicle(carModel.Value, start);
                if (_car == null || !_car.Exists())
                {
                    Cancel("He could not get a car out.");
                    return;
                }

                _car.IsPersistent = true;
                BlackOut(_car);

                var pedModel = ResolveDriverModel();
                if (pedModel == null)
                {
                    Cancel("Nobody could make the run.");
                    return;
                }

                var handle = Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE,
                                                _car.Handle, 4, pedModel.Value.Hash, -1, true, false);
                try { pedModel.Value.MarkAsNoLongerNeeded(); } catch { }

                if (handle == 0)
                {
                    Cancel("Nobody could make the run.");
                    return;
                }

                _driver = (Ped)Entity.FromHandle(handle);
                if (_driver == null || !_driver.Exists())
                {
                    Cancel("Nobody could make the run.");
                    return;
                }

                _driver.IsPersistent = true;
                _driver.BlockPermanentEvents = true;
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, _driver.Handle, true, true);
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, _driver.Handle, true);
                Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, _driver.Handle, false);

                DriveTo(player.Position);

                CreateBlip();

                State = DeliveryState.Driving;
                _stateSince = Game.GameTime;
                _stillSince = 0;

                Notify.Important("~y~" + _def.Name + "~s~ is on his way to you.");
                Log.Info("Delivery dispatched from " + start + ".");
            }
            catch (Exception ex)
            {
                Log.Error("Could not dispatch the delivery.", ex);
                Cancel("Something went wrong with the run.");
            }
            finally
            {
                try { carModel.Value.MarkAsNoLongerNeeded(); } catch { }
            }
        }

        /// <summary>
        /// Blacked out and tinted, every panel, every time.
        ///
        /// A mod kit has to be set before any modification takes, which is the usual reason
        /// wheels and trim stay factory while the paint changes.
        /// </summary>
        private static void BlackOut(Vehicle car)
        {
            try
            {
                Function.Call(Hash.SET_VEHICLE_MOD_KIT, car.Handle, 0);

                Function.Call(Hash.SET_VEHICLE_COLOURS, car.Handle, BlackPaint, BlackPaint);
                Function.Call(Hash.SET_VEHICLE_EXTRA_COLOURS, car.Handle, BlackPaint, BlackPaint);
                Function.Call(Hash.SET_VEHICLE_WINDOW_TINT, car.Handle, LimoTint);

                // Black wheels and trim, so nothing on it catches the light.
                Function.Call(Hash.SET_VEHICLE_WHEEL_TYPE, car.Handle, 7);
                Function.Call(Hash.SET_VEHICLE_MOD_COLOR_1, car.Handle, 0, 0, 0);
                Function.Call(Hash.SET_VEHICLE_MOD_COLOR_2, car.Handle, 0, 0);

                Function.Call(Hash.SET_VEHICLE_DIRT_LEVEL, car.Handle, 0f);
                Function.Call(Hash.SET_VEHICLE_NUMBER_PLATE_TEXT, car.Handle, "HOODRCH");

                OpenHisWindow(car);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not black out the delivery car: " + ex.Message);
            }
        }

        /// <summary>
        /// Points the car at somewhere and lets the game drive it there.
        ///
        /// Drive mode DriveStyle is the normal "obey the road" set: stops at lights, avoids traffic,
        /// takes junctions properly. He is delivering, not fleeing.
        /// </summary>
        private void DriveTo(Vector3 where)
        {
            if (_driver == null || !_driver.Exists() || _car == null || !_car.Exists()) return;

            _target = where;

            try
            {
                var far = _car.Position.DistanceTo(where) > 70f;

                if (far)
                {
                    // The long-range task is the one built for crossing a city. The ordinary
                    // drive-to-coord plans a route from where it is issued and gives up at the
                    // far end of it, which on a two-hundred-metre run across junctions is how
                    // you end up with a car stopped in a live lane waiting for a task that has
                    // already finished.
                    Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                                  _driver.Handle, _car.Handle,
                                  where.X, where.Y, where.Z, 22f, DriveStyle, 18f);
                    return;
                }

                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, _driver.Handle, _car.Handle,
                              where.X, where.Y, where.Z,
                              16f, 0, _car.Model.Hash, DriveStyle, 3f, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not route the delivery: " + ex.Message);
            }
        }

        private Model? ResolveDriverModel()
        {
            foreach (var name in _def.Models)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage) continue;
                    if (!model.Request(1500)) continue;
                    return model;
                }
                catch
                {
                    // Try the next.
                }
            }
            return null;
        }

        private void TickDriving(Ped player)
        {
            if (_driver == null || !_driver.Exists() || !_driver.IsAlive ||
                _car == null || !_car.Exists())
            {
                Cancel("Your delivery never turned up.");
                return;
            }

            if (Game.GameTime - _stateSince > DriveTimeoutMs)
            {
                Cancel("He gave up trying to reach you.");
                return;
            }

            // Measured against the ADDRESS, not the player.
            //
            // This is what had him stopping up the street: he drives to a fixed spot now, but
            // arrival was still being judged on how close he was to YOU. Stood at the house
            // waiting, you were inside the arrival radius while his car was still fifty metres
            // west at the approach point -- so he "arrived" without arriving, and the re-task
            // that would have given him his second leg only fired if the PLAYER moved, which
            // standing at your own front door you never do.
            var toSpot = _car != null && _car.Exists()
                ? _car.Position.DistanceTo(ParkSpot)
                : float.MaxValue;

            if (toSpot > ArriveDistance)
            {
                // Re-aimed on a plain clock, so the two legs chain whether or not anybody moves.
                if (Game.GameTime - _lastRetask > RetaskIntervalMs)
                {
                    _lastRetask = Game.GameTime;
                    DriveTo(Kerb());
                }

                Unstick();
                return;
            }

            State = DeliveryState.Waiting;

            // Put on the mark facing east, so he is never sat across the pavement at an angle.
            try
            {
                if (_car != null && _car.Exists() && _car.Position.DistanceTo(ParkSpot) < ArriveDistance + 4f)
                {
                    _car.Position = ParkSpot;
                    _car.Heading = ParkHeading;

                    Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, _car.Handle);
                    Function.Call(Hash.TASK_VEHICLE_TEMP_ACTION, _driver.Handle, _car.Handle, 1, 1000);
                }
            }
            catch
            {
                // He will park where he stopped.
            }
            _stateSince = Game.GameTime;

            try
            {
                // Pulls up and gets out, so the trade happens face to face at the car.
                Function.Call(Hash.TASK_LEAVE_VEHICLE, _driver.Handle, _car.Handle, 0);
                _car.IsEngineRunning = true;
            }
            catch (Exception ex)
            {
                Log.Debug("Delivery driver could not get out: " + ex.Message);
            }

            Notify.Important("~g~" + _def.Name + " has pulled up.~s~ Go and see him.");
        }

        /// <summary>
        /// Notices when he has stopped moving, and does something about it.
        ///
        /// A car with a perfectly valid drive task and a speed of zero is the single most common
        /// way any of this goes wrong: wedged on a kerb, nose to nose with a parked van, boxed
        /// in at a junction by traffic that is itself waiting for him. Nothing in the game will
        /// resolve that on its own, and the player just sees a delivery that never came.
        ///
        /// So it escalates. A few seconds still means nothing -- he could be at a light. Longer
        /// than that and he backs up and is re-routed. Longer still and he is put on the road by
        /// hand, behind you where possible so it is not done in front of your face. Being moved
        /// is a bad outcome; a man frozen in the road forever is a worse one.
        /// </summary>
        private void Unstick()
        {
            if (_car == null || !_car.Exists() || _driver == null || !_driver.Exists()) return;

            var now = Game.GameTime;

            if (_car.Speed > 1.2f)
            {
                _stillSince = 0;
                return;
            }

            if (_stillSince == 0)
            {
                _stillSince = now;
                return;
            }

            var stuck = now - _stillSince;

            if (stuck < StuckNudgeMs) return;

            if (stuck < StuckMoveMs)
            {
                if (now - _lastNudge < 3000) return;
                _lastNudge = now;

                try
                {
                    // Reverse out of whatever it is, then take the route again.
                    Function.Call(Hash.TASK_VEHICLE_TEMP_ACTION, _driver.Handle, _car.Handle, 3, 1200);
                    _lastRetask = 0;

                    Log.Info("Delivery: backing him out of something.");
                }
                catch { /* the move below is the fallback */ }

                return;
            }

            // Given up on him driving it. Put him on the road near the house instead.
            try
            {
                var west = ParkSpot;
                west.X -= ApproachWest;

                var road = World.GetNextPositionOnStreet(west);
                if (road == Vector3.Zero || road.DistanceTo(west) > 25f) road = west;

                _car.Position = road;
                Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, _car.Handle);

                _stillSince = 0;
                _lastRetask = 0;

                DriveTo(ParkSpot);

                Log.Warn("Delivery: he was wedged, so he has been put back on the road.");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not unstick the delivery: " + ex.Message);
            }
        }

        private int _stillSince;
        private int _lastNudge;

        /// <summary>Stopped this long and he gets backed out of it.</summary>
        private const int StuckNudgeMs = 7000;

        /// <summary>Stopped this long and he gets moved.</summary>
        private const int StuckMoveMs = 22000;

        /// <summary>
        /// Where he is aiming right now.
        ///
        /// A point up the street to the west until he is close, then the park spot itself. Two
        /// legs rather than one, because a car routed straight at a kerb from whichever side it
        /// happened to be on arrives facing the wrong way and then spends a minute shuffling.
        /// </summary>
        private Vector3 Kerb()
        {
            var here = Game.Player.Character != null && Game.Player.Character.Exists()
                ? Game.Player.Character.Position
                : ParkSpot;

            var carAt = _car != null && _car.Exists() ? _car.Position : here;

            if (carAt.DistanceTo(ParkSpot) > ApproachWest * 1.4f)
            {
                var west = ParkSpot;
                west.X -= ApproachWest;

                try
                {
                    // Checked, not trusted. GetNextPositionOnStreet hands back the nearest road
                    // node to a point, and the nearest node to a spot in the middle of a block
                    // can be on another street entirely -- which he would then drive to, stop
                    // at, and never leave, because as far as the task was concerned he had
                    // arrived. Anything that comes back more than a bus length from where the
                    // approach was meant to be is thrown away.
                    var onRoad = World.GetNextPositionOnStreet(west);

                    if (onRoad != Vector3.Zero && onRoad.DistanceTo(west) < 25f) return onRoad;
                }
                catch
                {
                    // Fall through to the raw point.
                }

                return west;
            }

            return ParkSpot;
        }

        /// <summary>
        /// Takes the order and starts him walking it in.
        ///
        /// Nothing is credited here. It goes in the stash when the box is on the floor of the
        /// house, because the whole point of watching a man carry it inside is that it has not
        /// arrived until he has.
        /// </summary>
        public void Deliver(string drugId, float grams)
        {
            if (State != DeliveryState.Waiting) return;
            if (_driver == null || !_driver.Exists()) return;

            _owedDrug = drugId;
            _owedGrams = grams;

            _dropSpot = HouseDoor;
            _carryingSince = Game.GameTime;
            State = DeliveryState.Carrying;

            try
            {
                _driver.Task.ClearAll();
                Function.Call(Hash.TASK_LEAVE_VEHICLE, _driver.Handle, _car.Handle, 0);

                GiveBox();

                // FOLLOW_NAV_MESH, not GO_STRAIGHT. Straight-line walking sends him into the
                // fence and leaves him pressed against it for the whole timeout; the nav mesh
                // takes him round the gate and up the path the way a person would.
                Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, _driver.Handle,
                              _dropSpot.X, _dropSpot.Y, _dropSpot.Z,
                              1.0f, CarryTimeoutMs, 1.0f, 0, 0f);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not start the drop-off: " + ex.Message);
            }

            Say(TakingLines);
            Notify.Ticker("~g~He's bringing it in.~s~");
        }

        /// <summary>
        /// Both front windows, down and staying down.
        ///
        /// Everything else about the car is blacked out to limo tint, which is the point of it
        /// -- and it also means anybody sat inside is a silhouette. With the front windows down
        /// he is a person you can see and talk through, which is what makes walking up to the
        /// car feel like walking up to somebody rather than to a vehicle. The back stays dark.
        ///
        /// Re-asserted rather than set once: a window can come back up when the game reloads
        /// the vehicle's state, and there is no cost to saying it again.
        /// </summary>
        private static readonly int[] FrontWindows = { 0, 1 };

        private static void OpenHisWindow(Vehicle car)
        {
            if (car == null || !car.Exists()) return;

            foreach (var window in FrontWindows)
            {
                try { Function.Call(Hash.ROLL_DOWN_WINDOW, car.Handle, window); }
                catch { /* a shut window is not worth an exception */ }
            }
        }

        /// <summary>One ambient line, over whatever he was already saying.</summary>
        private void Say(string[] lines)
        {
            if (_driver == null || !_driver.Exists() || !_driver.IsAlive) return;

            try
            {
                Function.Call(Hash.STOP_CURRENT_PLAYING_AMBIENT_SPEECH, _driver.Handle);
                Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, _driver.Handle,
                              lines[Rng.Next(lines.Length)], "SPEECH_PARAMS_FORCE");
            }
            catch
            {
                // A missing line costs nothing.
            }
        }

        private void TickCarrying()
        {
            if (_driver == null || !_driver.Exists() || !_driver.IsAlive)
            {
                // He is gone but you have paid, so the goods are yours regardless.
                Land();
                State = DeliveryState.Leaving;
                _stateSince = Game.GameTime;
                return;
            }

            var arrived = _driver.Position.DistanceTo(_dropSpot) <= DropRange;
            var late = Game.GameTime - _carryingSince > CarryTimeoutMs;

            if (!arrived && !late)
            {
                // Re-issued, because a walk task through a gate and up a path does not always
                // survive the first thing that gets in his way.
                if (Game.GameTime >= _nextNudge)
                {
                    _nextNudge = Game.GameTime + 4000;

                    try
                    {
                        Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, _driver.Handle,
                                      _dropSpot.X, _dropSpot.Y, _dropSpot.Z,
                                      1.0f, 8000, 1.0f, 0, 0f);
                    }
                    catch { /* he will get there or he will not */ }
                }

                return;
            }

            PutDown();
            Land();
            Say(DroppedLines);

            State = DeliveryState.Leaving;
            _stateSince = Game.GameTime;

            try
            {
                _driver.Task.ClearAll();

                if (_car != null && _car.Exists())
                {
                    Function.Call(Hash.TASK_ENTER_VEHICLE, _driver.Handle, _car.Handle,
                                  20000, -1, 2f, 1, 0);
                }
            }
            catch { /* he will find his own way back */ }
        }

        private void TickLeaving()
        {
            if (Game.GameTime - _stateSince < LeaveMs)
            {
                // Once he is behind the wheel, he goes.
                if (_driver != null && _driver.Exists() && _car != null && _car.Exists() &&
                    _driver.IsInVehicle(_car))
                {
                    // Said from the driver's seat, with the door shut, the way anybody says
                    // goodbye when they are already leaving.
                    Say(LeavingLines);

                    try
                    {
                        // East, up the street he came down, rather than wandering off the moment
                        // the door shuts and turning round in somebody's driveway.
                        var east = ParkSpot;
                        east.X += DepartEast;

                        var away = World.GetNextPositionOnStreet(east);
                        if (away == Vector3.Zero) away = east;

                        Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, _driver.Handle, _car.Handle,
                                      away.X, away.Y, away.Z, 18f, 0, _car.Model.Hash,
                                      DriveStyle, 12f, true);
                    }
                    catch { /* the game drives him */ }

                    Cancel(null);
                }

                return;
            }

            Cancel(null);
        }

        /// <summary>Puts the goods in the house. This is the moment it is actually yours.</summary>
        private void Land()
        {
            if (string.IsNullOrEmpty(_owedDrug) || _owedGrams <= 0f) return;

            var taken = House == null ? 0f : House.AddBulk(_owedDrug, _owedGrams);

            Notify.Important("~g~Delivered.~s~ " + (taken / 1000f).ToString("0.#") +
                             " kilos in the house.");

            Log.Info("Delivery landed: " + taken.ToString("0") + "g " + _owedDrug + ".");

            _owedDrug = "";
            _owedGrams = 0f;
        }

        private void GiveBox()
        {
            TakeBox();

            foreach (var name in BoxProps)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(900)) continue;

                    _box = World.CreateProp(model, _driver.Position, false, false);
                    model.MarkAsNoLongerNeeded();

                    if (_box == null || !_box.Exists()) continue;

                    // Held out in front with both hands, on the left hand bone, which is where
                    // the carry animation puts a crate.
                    Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, _box.Handle, _driver.Handle,
                                  Function.Call<int>(Hash.GET_PED_BONE_INDEX, _driver.Handle, 60309),
                                  0.05f, 0.10f, -0.18f, 0f, 0f, 0f,
                                  false, false, false, false, 2, true);

                    PlayCarry();
                    return;
                }
                catch
                {
                    // Try the next prop.
                }
            }

            Log.Debug("No box prop in this install; he will carry it in his hands.");
        }

        private void PlayCarry()
        {
            foreach (var dict in CarryDicts)
            {
                try
                {
                    if (!Function.Call<bool>(Hash.DOES_ANIM_DICT_EXIST, dict)) continue;

                    Function.Call(Hash.REQUEST_ANIM_DICT, dict);
                    if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dict)) continue;

                    foreach (var clip in CarryClips)
                    {
                        Function.Call(Hash.TASK_PLAY_ANIM, _driver.Handle, dict, clip,
                                      4f, -4f, -1, 49, 0f, false, false, false);

                        Log.Info("Delivery carry: " + dict + " / " + clip + ".");
                        return;
                    }
                }
                catch
                {
                    // Try the next dictionary.
                }
            }
        }

        /// <summary>Box off him and on the floor, where it stays.</summary>
        private void PutDown()
        {
            if (_box == null || !_box.Exists()) return;

            try
            {
                Function.Call(Hash.DETACH_ENTITY, _box.Handle, true, true);

                var spot = _dropSpot;
                spot.Z += 0.2f;

                _box.Position = spot;
                _box.IsPersistent = false;

                // Left there rather than deleted. A box on the floor of the house is the
                // receipt, and it disappearing the instant he turns round would undo the whole
                // reason for watching him carry it in.
                _box.MarkAsNoLongerNeeded();
            }
            catch (Exception ex)
            {
                Log.Debug("Could not put the box down: " + ex.Message);
            }

            _box = null;
        }

        private void TakeBox()
        {
            try
            {
                if (_box != null && _box.Exists()) _box.Delete();
            }
            catch { /* teardown */ }

            _box = null;
        }

        private void TickWaiting(Ped player)
        {
            OpenHisWindow(_car);

            if (_driver == null || !_driver.Exists() || !_driver.IsAlive)
            {
                Cancel("Your delivery is gone.");
                return;
            }

            if (Game.GameTime - _stateSince > WaitTimeoutMs)
            {
                Cancel("He was not waiting around all day.");
                return;
            }

            if (Distance > AbandonDistance) Cancel("You left him standing there.");
        }

        // ---- cleanup -----------------------------------------------------------

        /// <summary>Ends the run and lets the world have the driver and the van back.</summary>
        public void Cancel(string reason)
        {
            var name = _def == null ? "Your contact" : _def.Name;

            // Whatever happened, the phone comes down. A call that is called off leaving the
            // player walking round with a handset up is worse than no animation at all.
            EndPhoneAnimation();

            try
            {
                if (_driver != null && _driver.Exists())
                {
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, _driver.Handle, false);
                    _driver.MarkAsNoLongerNeeded();
                }
            }
            catch { /* teardown */ }

            try
            {
                if (_car != null && _car.Exists()) _car.MarkAsNoLongerNeeded();
            }
            catch { /* teardown */ }

            try
            {
                if (_blip != null && _blip.Exists()) _blip.Delete();
            }
            catch { /* teardown */ }

            _driver = null;
            _car = null;
            _blip = null;
            _def = null;
            State = DeliveryState.None;

            if (!string.IsNullOrEmpty(reason)) Notify.Ticker("~o~" + name + ": " + reason + "~s~");
        }

        /// <summary>Called once the trade is done, so he drives off rather than standing there.</summary>
        public void Finish()
        {
            try
            {
                if (_driver != null && _driver.Exists() && _car != null && _car.Exists())
                {
                    Function.Call(Hash.TASK_ENTER_VEHICLE, _driver.Handle, _car.Handle, 10000, -1, 2f, 1, 0);
                    Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, _driver.Handle, _car.Handle, 20f, DriveStyle);
                }
            }
            catch { /* he can walk if he likes */ }

            Cancel(null);
        }

        private void CreateBlip()
        {
            try
            {
                if (_car == null || !_car.Exists()) return;

                _blip = _car.AddBlip();
                if (_blip == null || !_blip.Exists()) return;

                _blip.Sprite = BlipSprite.Truck;
                _blip.Color = BlipColor.Blue;
                _blip.Name = _def.Name;
                _blip.IsShortRange = false;
                _blip.Scale = 0.8f;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not blip the delivery: " + ex.Message);
            }
        }

        /// <summary>
        /// A road far enough out that the spawn is never witnessed, preferring somewhere behind
        /// the camera so even a long sightline down a street does not catch it.
        /// </summary>
        private bool TryStartPoint(Vector3 origin, out Vector3 spot)
        {
            spot = Vector3.Zero;

            var behind = -Vector3.Zero;
            try { behind = GameplayCamera.Direction; }
            catch { /* fall back to any direction */ }

            for (var attempt = 0; attempt < 14; attempt++)
            {
                double angle;

                if (attempt < 8 && behind != Vector3.Zero)
                {
                    // Behind the camera, give or take a quarter turn.
                    var facing = Math.Atan2(behind.Y, behind.X);
                    angle = facing + Math.PI + (_rng.NextDouble() - 0.5) * (Math.PI * 0.5);
                }
                else
                {
                    angle = _rng.NextDouble() * Math.PI * 2.0;
                }

                var distance = SpawnMinDistance +
                               (float)_rng.NextDouble() * (SpawnMaxDistance - SpawnMinDistance);

                var candidate = origin + new Vector3(
                    (float)Math.Cos(angle) * distance, (float)Math.Sin(angle) * distance, 0f);

                Vector3 onRoad;
                try { onRoad = World.GetNextPositionOnStreet(candidate); }
                catch { continue; }

                if (onRoad == Vector3.Zero) continue;
                if (onRoad.DistanceTo(origin) < SpawnMinDistance * 0.6f) continue;

                spot = onRoad;
                return true;
            }

            return false;
        }

        public void RestoreWorld() => Cancel(null);
    }
}
