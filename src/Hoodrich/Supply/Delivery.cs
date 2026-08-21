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
        Texting,

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
        /// How far the point he is driving to has to be before it is worth driving to.
        ///
        /// Anything nearer and the drive task is complete the moment it is given, which reads
        /// as pulling out and stopping three metres later.
        /// </summary>
        private const float MinDepartDistance = 45f;

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

        /// <summary>
        /// How long the message takes.
        ///
        /// Four seconds: long enough to be somebody typing out what they want and waiting on a
        /// reply, short enough that you are not stood in your own yard staring at a handset.
        /// The player is never frozen for it -- the animation is upper body only, so he can
        /// walk off mid-message the way anybody does texting.
        /// </summary>
        private const int CallMs = 4000;

        /// <summary>
        /// How long after the message before a car exists.
        ///
        /// He was created the instant the text was sent, which is a man who was already round
        /// the corner waiting for you to ask. Fifteen seconds is him reading it, getting up and
        /// getting in -- and it is long enough that you have usually looked away from the spot
        /// he appears on, which is the other half of why a spawn reads as a spawn.
        /// </summary>
        private const int SettingOffMs = 15000;

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

        public DeliveryState State { get; private set; } = DeliveryState.None;

        public bool IsActive => State != DeliveryState.None;

        /// <summary>Who is on the way, or null.</summary>
        public DealerDef Def => _def;

        /// <summary>The driver, once he is in the world -- the man you actually trade with.</summary>
        public Ped Driver => _driver != null && _driver.Exists() ? _driver : null;

        /// <summary>The car he came in, so nothing else mistakes it for stuck traffic.</summary>
        public Vehicle Car => _car != null && _car.Exists() ? _car : null;

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
                    case DeliveryState.Texting: return "Texting " + _def.Name;
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
                return "Text him from the house. He ain't meeting you on a corner.";
            }

            if (def == null) return "No such contact.";
            if (IsActive) return _def.Name + " is already on his way.";

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return "Not right now.";
            if (player.IsInVehicle()) return "Get out of the car to text him.";

            _def = def;
            State = DeliveryState.Texting;
            _stateSince = Game.GameTime;
            _parking = false;
            _messageSent = false;

            PlayPhoneAnimation(player);

            Notify.Ticker("~y~Texting " + def.Name + "...~s~");
            Log.Info("Texted " + def.Id + " for a delivery.");
            return null;
        }

        /// <summary>
        /// Phone out, head down, thumbs going.
        ///
        /// Not the mobile-phone TASK. That native only knows how to hold a handset to an ear --
        /// there is no texting form of it -- so a text message has to be built: the game's own
        /// texting clip, and a handset put in his hand to go with it, because the clip on its
        /// own is a man staring intently at nothing.
        ///
        /// Given once and never re-issued. The last version watched for the animation and
        /// handed the task out again whenever it looked absent, which is how Franklin came to
        /// raise the phone three times in six seconds. The timer ends this one instead.
        /// </summary>
        private void PlayPhoneAnimation(Ped player)
        {
            try
            {
                Function.Call(Hash.REQUEST_ANIM_DICT, TextDict);

                // Streaming is asynchronous. A dictionary that is not in yet is not an error,
                // and a handset on its own still reads as somebody looking at their phone, so
                // the prop goes in either way.
                PhoneInHand(player);

                if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, TextDict)) return;

                // Upper body, secondary, looping. He keeps his legs, so he can walk off
                // mid-message the way anybody does -- and unlike the ear-to-phone task it does
                // not nail him to the spot for the whole of it.
                Function.Call(Hash.TASK_PLAY_ANIM, player.Handle, TextDict, TextClip,
                              4f, -4f, -1, TextFlags, 0f, false, false, false);
            }
            catch (Exception ex)
            {
                Log.Debug("Texting animation failed: " + ex.Message);
            }
        }

        /// <summary>The texting idle, and the handset that makes it read as one.</summary>
        private const string TextDict = "cellphone@";
        private const string TextClip = "cellphone_text_read_base";

        /// <summary>Looping, upper body only, secondary -- so he keeps control of his legs.</summary>
        private const int TextFlags = 49;

        private static readonly string[] PhoneProps = { "prop_npc_phone_02", "prop_npc_phone" };

        /// <summary>The right hand. The same bone the game puts its own handset in.</summary>
        private const int RightHandBone = 28422;

        private Prop _phone;

        /// <summary>
        /// Puts a handset in his hand for the length of the message.
        ///
        /// Attached rather than positioned: an attached prop follows the bone through the
        /// animation and comes off in one call, where anything else has to be moved every frame
        /// and leaves a phone hanging in the yard the moment something goes wrong.
        /// </summary>
        private void PhoneInHand(Ped player)
        {
            if (_phone != null && _phone.Exists()) return;

            foreach (var name in PhoneProps)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(500)) continue;

                    _phone = World.CreateProp(model, player.Position, false, false);
                    model.MarkAsNoLongerNeeded();

                    if (_phone == null || !_phone.Exists()) continue;

                    var bone = Function.Call<int>(Hash.GET_PED_BONE_INDEX, player.Handle,
                                                  RightHandBone);

                    Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, _phone.Handle, player.Handle,
                                  bone, 0f, 0f, 0f, 0f, 0f, 0f,
                                  true, true, false, true, 1, true);
                    return;
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not put a phone in his hand: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Keeps the player's hands off it for the length of the call.
        ///
        /// No re-issuing any more. The previous version watched
        /// IS_PED_RUNNING_MOBILE_PHONE_TASK and handed the task out again whenever it read
        /// false -- but it reads false for this task the whole way through, so the "watchdog"
        /// simply re-issued on its own cooldown and Franklin raised the phone three times in
        /// six seconds. Once every 2.2 seconds, three times. Exactly as reported.
        ///
        /// The task below is the TIMED one now, which runs for its duration and ends itself, so
        /// there is nothing left to watch. All this does is block the three controls that would
        /// cancel it -- half-drawing a weapon and having nothing happen reads better than the
        /// phone vanishing mid-sentence.
        /// </summary>
        private void HoldThePhone(Ped player)
        {
            Game.DisableControlThisFrame(Control.Phone);
            Game.DisableControlThisFrame(Control.Aim);
            Game.DisableControlThisFrame(Control.Attack);
            Game.DisableControlThisFrame(Control.Attack2);
            Game.DisableControlThisFrame(Control.SelectWeapon);
        }

        /// <summary>
        /// Puts it away again, whether the message landed or was thought better of.
        ///
        /// Both halves, and the handset unconditionally. A looping secondary animation runs
        /// until something stops it, and an attached prop outlives the animation entirely -- so
        /// an interrupted message that cleared only one of the two would leave Franklin walking
        /// round Davis holding a phone for the rest of the session.
        /// </summary>
        private void EndPhoneAnimation()
        {
            try
            {
                var player = Game.Player.Character;

                if (player != null && player.Exists())
                {
                    Function.Call(Hash.STOP_ANIM_TASK, player.Handle, TextDict, TextClip, 4f);
                    Function.Call(Hash.TASK_USE_MOBILE_PHONE, player.Handle, false);
                }
            }
            catch
            {
                // It blends out on its own.
            }

            try
            {
                if (_phone != null && _phone.Exists())
                {
                    Function.Call(Hash.DETACH_ENTITY, _phone.Handle, true, true);
                    _phone.Delete();
                }
            }
            catch
            {
                // It will stream out.
            }

            _phone = null;
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
                case DeliveryState.Texting:
                    // The phone goes away when the message is sent. He does not turn up for
                    // another fifteen seconds, and the two are separate on purpose: standing
                    // there holding a handset for the whole wait is not what waiting looks like.
                    if (Game.GameTime - _stateSince < CallMs)
                    {
                        HoldThePhone(player);
                        return;
                    }

                    if (!_messageSent)
                    {
                        _messageSent = true;
                        EndPhoneAnimation();
                        Notify.Ticker("~y~" + _def.Name + " says give him a minute.~s~");
                    }

                    if (Game.GameTime - _stateSince >= CallMs + SettingOffMs) Dispatch(player);
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
                _car = World.CreateVehicle(carModel.Value, start, StartHeading);
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

                // At the kerb outside the house, not at the player.
                //
                // This was the whole problem. The first leg was aimed at whoever called, and
                // the long-range task stops eighteen metres short of what it is given -- so he
                // pulled up eighteen metres from you, which from the front path is the middle
                // of the road. Every later leg already used Kerb(); only the first did not.
                DriveTo(Kerb());

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
                // The last thirty metres are a park, not a drive.
                //
                // Driving to a coordinate aims the car AT a point and stops when it is within
                // the task's radius, facing wherever it happened to be facing -- which on a
                // street is a car halted in a lane near the kerb rather than a car parked at
                // it. The park task is the game's own, takes the heading, and pulls in.
                //
                // Issued once. Re-issuing it every few seconds restarts the manoeuvre from the
                // beginning, which is a car that shuffles at the kerb forever and never settles.
                if (toSpot <= ParkTaskRange)
                {
                    if (!_parking) PullIn();
                }
                else if (Game.GameTime - _lastRetask > RetaskIntervalMs)
                {
                    // Still a drive. Re-aimed on a plain clock so the legs chain whether or not
                    // anybody moves.
                    _parking = false;
                    _lastRetask = Game.GameTime;
                    DriveTo(Kerb());
                }

                Unstick();

                // He has had long enough.
                //
                // A car can be forty seconds from a kerb it will never quite reach: the last
                // few metres of a park are the hardest thing the driving AI does, and a spot
                // outside somebody's front gate with a fence one side and a parked car the
                // other is exactly where it gives up and shuffles. Everything above is still
                // tried first and this only ever fires after a minute of trying, but a delivery
                // that never arrives is the one outcome that cannot be allowed to stand.
                if (Game.GameTime - _stateSince > SettleForItMs && toSpot < 90f)
                {
                    Log.Warn("Delivery: close but not parking after " +
                             ((Game.GameTime - _stateSince) / 1000) + "s; putting him on the mark.");

                    ParkOnTheMark();
                    Arrive();
                }

                return;
            }

            // Near the mark is not the same as parked on it. Announcing "he has pulled up"
            // while the car is still rolling up the road is how you get sent out to a moving
            // vehicle, so the last check is that he has actually stopped.
            if (_car.Speed > ParkedSpeed && Game.GameTime - _stateSince < SettleForItMs)
            {
                if (!_parking) PullIn();
                Unstick();
                return;
            }

            ParkOnTheMark();
            Arrive();
        }

        /// <summary>Slower than this and he has stopped, rather than is slowing down.</summary>
        private const float ParkedSpeed = 1.2f;

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
                    // 9, not 3. Three is brake-and-turn-left, which is a car sitting exactly
                    // where it was but pointing somewhere new -- nine is reverse, which is what
                    // "back him out of it" was supposed to mean.
                    Function.Call(Hash.TASK_VEHICLE_TEMP_ACTION, _driver.Handle, _car.Handle, 9, 1400);
                    _lastRetask = 0;

                    Log.Info("Delivery: backing him out of something.");
                }
                catch { /* the move below is the fallback */ }

                return;
            }

            // Given up on him driving it. Put him on the road near the house instead -- but
            // not while you are watching. A car that vanishes and reappears fifty metres up the
            // street in plain sight is worse than the problem it is solving, so if he is on
            // screen he keeps trying until he is not.
            try
            {
                if (Function.Call<bool>(Hash.IS_ENTITY_ON_SCREEN, _car.Handle) &&
                    _car.Position.DistanceTo(Game.Player.Character.Position) < 120f)
                {
                    return;
                }

                var west = ParkSpot;
                west.X -= ApproachWest;

                var road = World.GetNextPositionOnStreet(west);
                if (road == Vector3.Zero || road.DistanceTo(west) > 25f) road = west;

                _car.Position = road;
                Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, _car.Handle);

                _stillSince = 0;
                _lastRetask = 0;
                _parking = false;

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

        /// <summary>Straightens him onto the mark, facing the way the street runs.</summary>
        private void ParkOnTheMark()
        {
            try
            {
                if (_car == null || !_car.Exists()) return;

                // Not while you are watching him.
                //
                // This straightens the car onto the mark, and straightening means moving --
                // which from the pavement is a car jumping two metres sideways and rotating.
                // Close enough is close enough when somebody is looking; the tidy-up happens
                // the moment they are not.
                var watched = Function.Call<bool>(Hash.IS_ENTITY_ON_SCREEN, _car.Handle) &&
                              _car.Position.DistanceTo(Game.Player.Character.Position) < 60f;

                // The same distance arrival is judged on. These used to disagree -- arrival at
                // nine metres, tidy-up only within six -- so a car that stopped in between was
                // announced as parked and then deliberately left where it was.
                var near = _car.Position.DistanceTo(ParkSpot) < ArriveDistance;

                if (watched && near)
                {
                    // Parked as far as anybody cares. Let him stop where he stopped.
                    if (_driver != null && _driver.Exists())
                    {
                        Function.Call(Hash.TASK_VEHICLE_TEMP_ACTION, _driver.Handle, _car.Handle, 1, 1000);
                    }

                    return;
                }

                if (watched) return;

                _car.Position = ParkSpot;
                _car.Heading = ParkHeading;

                Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, _car.Handle);

                if (_driver != null && _driver.Exists())
                {
                    Function.Call(Hash.TASK_VEHICLE_TEMP_ACTION, _driver.Handle, _car.Handle, 1, 1000);
                }
            }
            catch
            {
                // He will sit where he stopped.
            }
        }

        /// <summary>He is here. Out of the car, and the trade is on.</summary>
        private void Arrive()
        {
            State = DeliveryState.Waiting;
            _stateSince = Game.GameTime;

            try
            {
                if (_driver != null && _driver.Exists() && _car != null && _car.Exists())
                {
                    Function.Call(Hash.TASK_LEAVE_VEHICLE, _driver.Handle, _car.Handle, 0);
                    _car.IsEngineRunning = true;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Delivery driver could not get out: " + ex.Message);
            }

            Notify.Important("~g~" + _def.Name + " has pulled up.~s~ Go and see him.");
        }

        /// <summary>How long he is given to park himself before he is simply put on the mark.</summary>
        private const int SettleForItMs = 60000;

        /// <summary>Inside this, he stops driving at the mark and starts parking on it.</summary>
        private const float ParkTaskRange = 32f;

        /// <summary>Whether the park manoeuvre has been handed out for this run.</summary>
        private bool _parking;

        /// <summary>Whether the phone has been put away for this run.</summary>
        private bool _messageSent;

        /// <summary>
        /// Parks him on the mark, facing the way the street runs.
        ///
        /// Style 2 is parallel -- a kerbside stop, which is what a man dropping a box off
        /// outside a house does. The radius is what he is allowed to shuffle within to achieve
        /// it, and the engine stays on because he is not stopping long.
        /// </summary>
        private void PullIn()
        {
            if (_driver == null || !_driver.Exists() || _car == null || !_car.Exists()) return;

            try
            {
                Function.Call(Hash.TASK_VEHICLE_PARK, _driver.Handle, _car.Handle,
                              ParkSpot.X, ParkSpot.Y, ParkSpot.Z, ParkHeading,
                              ParallelPark, ParkWithin, true);

                _parking = true;
            }
            catch (Exception ex)
            {
                // No park task, so he keeps driving at it -- worse, but not broken.
                Log.Debug("Could not hand out the park: " + ex.Message);
                _parking = false;
            }
        }

        private const int ParallelPark = 2;
        private const float ParkWithin = 22f;

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

                // FOLLOW_NAV_MESH, not GO_STRAIGHT. Straight-line walking sends him into the
                // fence and leaves him pressed against it for the whole timeout; the nav mesh
                // takes him round the gate and up the path the way a person would.
                Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, _driver.Handle,
                              _dropSpot.X, _dropSpot.Y, _dropSpot.Z,
                              1.0f, CarryTimeoutMs, 1.0f, 0, 0f);

                // AFTER the walk, not before it. The carry animation was issued first and the
                // walk task replaced it on the very next line, so in every delivery ever made
                // he strolled up the path with a box welded to his hand and his arms by his
                // sides. It is an upper-body clip, so layered on top of the walk it plays.
                GiveBox();

                Stagger(true);
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

                    // Asked for again in case the clipset was still streaming when he set off.
                    if (!_staggering) Stagger(true);
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

                        // He is PARKED ON A STREET, so the nearest street node to a point up the
                        // road is very often a node he is nearly on top of already -- and the
                        // stop radius was twelve metres. Drive-to-coord with a destination
                        // inside its own stop radius is a task that is complete the instant it
                        // is given: he pulled out, went three metres, and stopped dead. Then
                        // Cancel released him, so nothing ever re-tasked him, and he sat there
                        // with the engine running as a permanent roadblock outside the house.
                        // Every delivery left another one.
                        var far = _car.Position.DistanceTo(away) > MinDepartDistance;

                        if (far)
                        {
                            Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, _driver.Handle,
                                          _car.Handle, away.X, away.Y, away.Z, 18f, 0,
                                          _car.Model.Hash, DriveStyle, 5f, true);
                        }
                        else
                        {
                            // No usable node up the road. Wandering has no destination, so it
                            // cannot be satisfied on the spot -- he just drives.
                            Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, _driver.Handle,
                                          _car.Handle, 18f, DriveStyle);
                        }

                        // And handed back, so that whatever he does next the game can clean it
                        // up. A persistent car is invisible to population control, which is the
                        // difference between a car that eventually despawns and a monument.
                        _car.IsPersistent = false;
                        _car.MarkAsNoLongerNeeded();
                        _driver.IsPersistent = false;
                        _driver.MarkAsNoLongerNeeded();
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

                // Only ever one on the floor. Left there deliberately -- it is the receipt --
                // but a house with fourteen identical packages stacked in the living room after
                // an evening of re-ups is a bug wearing a detail's clothes.
                if (_lastDropped != null && _lastDropped.Exists())
                {
                    try { _lastDropped.Delete(); }
                    catch { /* it will stream out on its own */ }
                }

                _lastDropped = _box;

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

        /// <summary>The last package left on the floor of the house.</summary>
        private Prop _lastDropped;

        /// <summary>
        /// The walk of a man who has had a few, for the trip up the path and back.
        ///
        /// A movement clipset rather than an animation: it replaces how he walks for as long as
        /// it is set, so it survives the nav-mesh task being re-issued every four seconds on the
        /// way in. An animation would be cancelled by the first of those and he would be sober
        /// again for the rest of the path.
        ///
        /// Cleared explicitly when he is let go. A clipset left on a ped the game then recycles
        /// is a random pedestrian staggering round Davis for the rest of the session.
        /// </summary>
        private const string DrunkWalk = "move_m@drunk@moderatedrunk";

        private bool _staggering;

        private void Stagger(bool on)
        {
            if (_driver == null || !_driver.Exists()) return;

            try
            {
                if (on)
                {
                    Function.Call(Hash.REQUEST_ANIM_SET, DrunkWalk);

                    // Streaming is asynchronous, so a clipset asked for this frame is not ready
                    // this frame. TickCarrying calls back in while he walks, and the walk takes
                    // several seconds, so it lands well before he reaches the door.
                    if (!Function.Call<bool>(Hash.HAS_ANIM_SET_LOADED, DrunkWalk)) return;

                    Function.Call(Hash.SET_PED_MOVEMENT_CLIPSET, _driver.Handle, DrunkWalk, 1.0f);
                    _staggering = true;
                    return;
                }

                if (!_staggering) return;

                Function.Call(Hash.RESET_PED_MOVEMENT_CLIPSET, _driver.Handle, 1.0f);
                _staggering = false;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not set the walk: " + ex.Message);
            }
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

            // Before he is handed back, and here rather than at each of the three places that
            // release him -- this is the one funnel every ending goes through. A movement
            // clipset left on a ped the game then recycles is a stranger staggering round Davis
            // for the rest of the session.
            Stagger(false);

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
                    // A SEQUENCE, not two tasks.
                    //
                    // Issued back to back, the second one replaces the first -- so he was told
                    // to drive a car he was not in yet, the drive task failed against an empty
                    // seat, and he stood on the pavement next to a car nobody was ever going to
                    // move. In a sequence he gets in first and drives after.
                    var seq = new OutputArgument();
                    Function.Call(Hash.OPEN_SEQUENCE_TASK, seq);
                    var handle = seq.GetResult<int>();

                    Function.Call(Hash.TASK_ENTER_VEHICLE, 0, _car.Handle, 10000, -1, 2f, 1, 0);
                    Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, 0, _car.Handle, 20f, DriveStyle);

                    Function.Call(Hash.CLOSE_SEQUENCE_TASK, handle);
                    Function.Call(Hash.TASK_PERFORM_SEQUENCE, _driver.Handle, handle);
                    Function.Call(Hash.CLEAR_SEQUENCE_TASK, seq);

                    _car.IsPersistent = false;
                    _car.MarkAsNoLongerNeeded();
                    _driver.IsPersistent = false;
                    _driver.MarkAsNoLongerNeeded();
                }
            }
            catch { /* he can walk if he likes */ }

            Cancel(null);
        }

        /// <summary>radar_nhp_wp2 -- the plug on his way, rather than a lorry.</summary>
        private const int PlugSprite = 596;

        private void CreateBlip()
        {
            try
            {
                if (_car == null || !_car.Exists()) return;

                _blip = _car.AddBlip();
                if (_blip == null || !_blip.Exists()) return;

                // radar_nhp_wp2, 596. The lorry sprite said "a truck is coming", which is true
                // and not the point -- what is coming is the plug, and this is the one that
                // reads as a man to meet rather than as traffic.
                Function.Call(Hash.SET_BLIP_SPRITE, _blip.Handle, PlugSprite);
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
        /// Where he sets off from.
        ///
        /// A named spot rather than a random road hundreds of metres out. The long version was
        /// chosen so the spawn could never be witnessed, and it bought that with a drive across
        /// half of Chamberlain -- which is where all the getting stuck came from, and every
        /// recovery for it ends in a car moving on its own.
        ///
        /// Read off the HUD standing on the spot. A straight run in from there: short enough
        /// that there is little to go wrong on, far enough that he arrives rather than appears.
        /// </summary>
        private static readonly Vector3 StartPoint = new Vector3(-106.196f, -1517.141f, 33.783f);

        /// <summary>
        /// Which way he is pointing when he appears.
        ///
        /// Set rather than left to the game. A car created without a heading faces due north
        /// whatever the road does, so the first thing he did was a three point turn on a main
        /// road in front of anybody standing there.
        /// </summary>
        private const float StartHeading = 305.994f;

        /// <summary>
        /// A road far enough out that the spawn is never witnessed, preferring somewhere behind
        /// the camera so even a long sightline down a street does not catch it.
        ///
        /// Only used when the named start is in view. A fixed spot you can see a car appear on
        /// is worse than a longer drive.
        /// </summary>
        private bool TryStartPoint(Vector3 origin, out Vector3 spot)
        {
            spot = Vector3.Zero;

            // The named spot first, unless you are looking straight at it.
            try
            {
                var player = Game.Player.Character;
                var seen = player != null && player.Exists() &&
                           player.Position.DistanceTo(StartPoint) < 90f &&
                           Function.Call<bool>(Hash.IS_SPHERE_VISIBLE,
                                               StartPoint.X, StartPoint.Y, StartPoint.Z, 3f);

                if (!seen)
                {
                    spot = StartPoint;
                    return true;
                }
            }
            catch
            {
                spot = StartPoint;
                return true;
            }

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
