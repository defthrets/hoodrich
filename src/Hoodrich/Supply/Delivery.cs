using System;
using System.Collections.Generic;
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
        private static readonly Vector3 ParkSpot = new Vector3(-23.045f, -1455.360f, 30.047f);

        // FACING THE WAY HE CAME. He arrives from the west and leaves to the east, and the
        // heading he was straightened onto was 101.9 -- which on the game's compass, where
        // ninety is west, is facing west: a truck spun round on the mark to point back the
        // way it had just driven in. Turned by a hundred and eighty.
        private const float ParkHeading = 281.923f;

        /// <summary>How far west he is aimed before the final approach.</summary>
        private const float ApproachWest = 55f;

        /// <summary>
        /// Where he is heading once the box is down.
        ///
        /// A place rather than a bearing. This used to be the park spot with ninety metres
        /// added to its X, which is a direction dressed up as a destination -- and the nearest
        /// street node to a point ninety metres off a kerb is whatever happens to be there.
        /// </summary>
        private static readonly Vector3 LeaveFor = new Vector3(-146.676f, -1489.605f, 32.786f);

        /// <summary>How long he spends walking it in before it counts as delivered.</summary>
        private const int CarryTimeoutMs = 45000;

        /// <summary>Close enough to the door to put it down.</summary>
        private const float DropRange = 2.2f;

        /// <summary>
        /// Where it ends up: the store room inside the house, against the boxes.
        ///
        /// He used to stop at the front door and put it down on the step, which is a man
        /// delivering to a doorstep rather than a man dropping a re-up off. This is in the
        /// back, in among the cartons and the paint tins, where a package that nobody is
        /// supposed to notice would actually go.
        /// </summary>
        private static readonly Vector3 DropInside = new Vector3(-15.769f, -1430.328f, 31.102f);

        /// <summary>
        /// How long he gets for the second leg, from the door to the store room.
        ///
        /// Much shorter than the walk up the path, and it is a deadline rather than a target.
        /// The path to the door is open ground and he will always make it; the leg through the
        /// house depends on a door being open and the inside being navigable, and neither is
        /// something this code controls. So he is given a fair go at walking it in, and if the
        /// house will not let him through he puts it down where it was going anyway -- the
        /// delivery is what matters, and it must not be able to stall on a shut door.
        /// </summary>
        private const int WalkInMs = 12000;

        /// <summary>Once the box is down, this long before he is let go entirely.</summary>
        // Long enough for a walk that is now a real walk.
        //
        // It was 25 seconds, which was comfortable while the enter task warped him in at 20 --
        // the state never had to wait for an actual walk across a dock yard plus the door
        // animation. Without the warp the walk takes as long as it takes, and a state that
        // gave up at 25 would hand him back mid-stride: no goodbye from the driver's seat, no
        // road to drive to, just a released ped finishing his own task in silence.
        private const int LeaveMs = 45000;

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
        /// This list used to open with prop_drug_package_02, which measures 25 by 17 by 6
        /// centimetres -- a flat taped envelope. Being base game it was present in every
        /// install, so it won the loop every single time and no other entry was ever reached.
        /// A man drove a kilo of weight across Los Santos and carried in something the size of
        /// a paperback.
        ///
        /// The biker and gunrunning packs ship proper bales, and those go first now: a metre
        /// of shrink-wrapped kilos, which is what the trip was for. They are DLC, so the base
        /// game bale is under them and an ordinary carton is under that -- an install missing
        /// everything still gets the delivery, just with nothing in shot, which is a worse
        /// scene rather than a broken one.
        ///
        /// Deliberately not here: imp_prop_impexp_boxcoke_01, which is the biggest of the lot
        /// and has no collision. PutDown drops the box 20cm up and lets gravity finish, so a
        /// collisionless prop would hang in the air in Franklin's front room forever.
        /// </summary>
        private static readonly string[] BoxProps =
        {
            // A DRUG PACKAGE, not a cardboard box.
            //
            // It was the heist box for a while, on the reasoning that the carry animation is
            // both arms out cradling something wide and a box is what fits those hands. That
            // is true and it is beside the point: this is a man delivering weight, and a
            // moving carton is a man delivering a carton. The reach maths already sizes the
            // hold to whatever prop wins, so a package sits in them properly regardless.
            "prop_drug_package", "prop_drug_package_02", "prop_mp_drug_pack_red",
            "bkr_prop_coke_block_01a", "ba_prop_battle_coke_block_01a",
            "bkr_prop_meth_bigbag_01a", "bkr_prop_weed_bigbag_01a",
            "prop_paper_box_01"
        };

        /// <summary>
        /// What everybody who is not the port brings.
        ///
        /// Gerald drives over with twenty grams of bars. A metre of shrink-wrapped kilos in his
        /// arms is the port's delivery, not his -- so the small package leads for him, which is
        /// the flat taped one at twenty-five by seventeen centimetres. It is the wrong size for
        /// a kilo off a boat and exactly the right size for what he actually turned up with.
        /// </summary>
        private static readonly string[] SmallBoxProps =
        {
            "prop_drug_package_02", "prop_drug_package", "prop_mp_drug_pack_red",
            "prop_paper_box_01"
        };

        /// <summary>
        /// Carrying a box with both hands.
        ///
        /// ONE dictionary, because there is only one. The other two in this list were decoys:
        /// anim@heists@narcotics@trash holds nothing but bin-bag throws and missfinale_c2mcs_1
        /// is a cutscene, and neither contains a clip called idle, walk or base -- so falling
        /// through to them could only ever have played nothing. Checked against the game's own
        /// animation data rather than assumed, which is how they were caught.
        /// </summary>
        private const string CarryDict = "anim@heists@box_carry@";

        /// <summary>Both real, and walk is the one that matches a man walking.</summary>
        private static readonly string[] CarryClips = { "walk", "idle" };

        /// <summary>
        /// What he says, and when.
        ///
        /// FOUR moments now, not three, and each of them has words as well as a noise. These
        /// arrays are the noise -- GENERIC_* ambient clips, which is the only voice the game has
        /// for a man it has never heard of. They carry the tone. The words below carry what he
        /// actually said, and they go out as subtitles, because a grunt is not a character.
        ///
        /// He was doing the whole delivery in dumb show before: three ambient barks and not one
        /// readable line, which makes him a vending machine that drives.
        /// </summary>
        private static readonly string[] TakingLines = { "GENERIC_YES", "GENERIC_HOWS_IT_GOING" };
        private static readonly string[] DroppedLines = { "GENERIC_THANKS", "GENERIC_YES" };
        private static readonly string[] LeavingLines = { "GENERIC_BYE", "GENERIC_THANKS" };

        /// <summary>
        /// What the man at your door actually says, and WHO is saying it.
        ///
        /// These were written once and never heard: Speak took them as an argument and threw
        /// them away, playing a generic grunt instead. Twenty-four lines of dialogue that no
        /// player has ever seen. They are shown now.
        ///
        /// And they are split, because the old single set could not be right for both men. It
        /// talked about containers, the water and the gate at the port -- true of the plug off
        /// Elysian Island and nonsense out of Gerald, who has driven four streets from a
        /// Chamberlain corner and has never been near a boat. A shared pool is only a saving
        /// when the people sharing it are interchangeable, and these two are the opposite of
        /// that: the whole point of having both is that they are different men.
        /// </summary>
        private static readonly string[] PortArrival =
        {
            "Ayy. This the address? This the address. Okay.",
            "Engine stay on, my dude. That's not rude, that's business.",
            "I drove this one myself. MYSELF. Think about that.",
            "Off a boat at five, on your lawn by two. That's my day.",
            "Nice street. Lot of windows on it tho.",
            "You could came to the port, bro. There's a gate and everything."
        };

        private static readonly string[] PortCarry =
        {
            "This is heavy. Don't just stand there lookin'.",
            "At the yard I got a forklift for this. Out here I got arms.",
            "Nah. I'm not makin' two trips, my guy.",
            "Anybody ask, this a television.",
            "Ten steps of driveway. Nobody paid me for these ten.",
            "Door. Any time now."
        };

        private static readonly string[] PortDrop =
        {
            "Weight correct. It's always correct.",
            "Keep the box. I don't want it back and I don't want it in your trash.",
            "Sealed at the port, opened by you. Nothin' in between.",
            "Get it inside before you get curious.",
            "Delivered. That's the whole ceremony, bro.",
            "Don't drag it. Lift it. It's not a suitcase."
        };

        private static readonly string[] PortParting =
        {
            "I got a container due in at six. This was the detour.",
            "The people I answer to like a quiet week. See they get one.",
            "Goin' back to the water. Smells worse, asks less.",
            "Anything go wrong, it went wrong after I left.",
            "Same number, same hours. Don't get creative with either.",
            "That's me. Go inside."
        };

        private static readonly string[] CornerArrival =
        {
            "You called, I came. Don't make that a habit.",
            "I'm double parked, dawg. That's your problem now.",
            "Out my yard, cross town, for this.",
            "Quiet street. Keep it that way.",
            "This ain't what I do. This a favour with a price on it.",
            "Make it quick, I got somewhere to be."
        };

        private static readonly string[] CornerCarry =
        {
            "It ain't heavy. It's just not somethin' to be holdin' in the open.",
            "Ten steps up a driveway. I don't do driveways.",
            "Somebody watchin' this? 'Cause somebody always watchin'.",
            "Move, dawg. I ain't stood out here all day.",
            "Front door. Not the step. In.",
            "This the last time I'm the one carryin'."
        };

        private static readonly string[] CornerDrop =
        {
            "That's yours. Count it if you want, it's right.",
            "Take the bag too. Don't leave it in your bin.",
            "Get it inside 'fore somebody see it.",
            "There. Done. That's the whole thing.",
            "Weight's right. It's always right, that's why you call me.",
            "Now it's your problem. Look after it."
        };

        private static readonly string[] CornerParting =
        {
            "Aight. Don't call me for nothin' small.",
            "Next time have somethin' worth the drive.",
            "I'm gone. You ain't seen me.",
            "Same number. Don't wear it out.",
            "Go on inside. Standin' there don't help neither of us.",
            "That's me. Go make some money with it."
        };


        private static readonly Random Rng = new Random();

        private Prop _box;
        private Vector3 _dropSpot;

        /// <summary>Set once he is through the front door and heading for the store room.</summary>
        private bool _wentIn;
        private int _carryingSince;
        private int _nextNudge;

        /// <summary>What has been paid for and is still in the boot.</summary>
        private string _owedDrug = "";
        private float _owedGrams;

        /// <summary>
        /// How strong the order was, stamped when it is placed rather than read when it lands.
        ///
        /// The hand-to-hand path has always taken the dealer's purity; this one dropped it on
        /// the floor and every delivery in the game landed at a hundred per cent, whoever sold
        /// it. It only ever looked right because the port sells pure -- call anybody who steps
        /// on his weight out to the house and the drive turned it back into pure product.
        /// </summary>
        private float _owedPurity = 1f;

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
        private const int DriveStyle = 786476;

        /// <summary>
        /// How fast he comes, in metres per second.
        ///
        /// He was doing twenty-two on the long leg and sixteen on the approach, which on
        /// Forum Drive is a man arriving at fifty miles an hour to hand over a bag. Fourteen
        /// and ten reads as somebody who knows the address and is not in a hurry to be seen
        /// at it -- and it gives the last few metres a chance to settle rather than arriving
        /// hot and overshooting the mark.
        /// </summary>
        private const float CruiseSpeed = 14f;
        private const float ApproachSpeed = 10f;

        /// <summary>
        /// How close before he stops following roads and drives straight at the point.
        ///
        /// This is the last argument of TASK_VEHICLE_DRIVE_TO_COORD and it is a FLOAT. It was
        /// being passed `true`, which marshals as 1.0 -- one metre -- so he stayed glued to the
        /// road node network until he was practically on top of the mark. The mark is at the
        /// kerb rather than on a node, which is most of why the last few metres of every
        /// delivery were a shuffle.
        ///
        /// Twenty metres. Rockstar ship this parameter 238 times, most often at 100.
        /// </summary>
        private const float StraightLineAt = 20f;

        /// <summary>
        /// Whose face goes on his messages.
        ///
        /// A CHAR_ dictionary the game already ships, chosen from who he is rather than
        /// configured -- there are two couriers and the mod knows both of them. An unknown one
        /// draws no picture and keeps the words, so a new dealer costs a blank portrait rather
        /// than a broken message.
        /// </summary>
        private string Portrait =>
            _def == null || string.IsNullOrEmpty(_def.Portrait) ? "CHAR_DEFAULT" : _def.Portrait;

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
            _messageSent = false;
            _greeted = false;
            _carrying = false;

            // The whole drive over is the loading window for the carry clip. Asking here rather
            // than at the moment he needs it is the difference between a man walking up the
            // path holding a box and a man walking up the path next to one.
            WantCarry();

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
                _handset.Show(player);

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

        /// <summary>The phone he texts on. See Core.Handset -- this file used to own a copy.</summary>
        private readonly Handset _handset = new Handset();

        /// <summary>
        /// Puts a handset in his hand for the length of the message.
        ///
        /// Attached rather than positioned: an attached prop follows the bone through the
        /// animation and comes off in one call, where anything else has to be moved every frame
        /// and leaves a phone hanging in the yard the moment something goes wrong.
        /// </summary>

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
            Core.Fists.Off();
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

            _handset.Hide();
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
                        // From HIM, with his face on it. You have just phoned a contact; a
                        // grey ticker saying he says give him a minute is the mod relaying a
                        // message he is perfectly capable of sending himself.
                        Notify.Text(Portrait, _def.Name, "Los Santos", _def.SayCalled());
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

            // His own ride first, the mod's default second.
            //
            // A pushbike is a vehicle like any other as far as everything downstream is
            // concerned -- he gets on it, drives it to the mark, gets off and walks the bag in
            // -- so this is a list swap rather than a second delivery.
            var rides = _def != null && _def.Rides.Count > 0
                ? _def.Rides.ToArray()
                : CarModels;

            Model? carModel = null;
            foreach (var name in rides)
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
                BlackOut(_car, _def != null ? _def.RidePaint : -1,
                         _def == null ? "" : _def.Plate);

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

                Notify.Text(Portrait, _def.Name, "Los Santos", _def.SayLeaving(), true);
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
        /// Painted and tinted, every panel, every time.
        ///
        /// A mod kit has to be set before any modification takes, which is the usual reason
        /// wheels and trim stay factory while the paint changes.
        ///
        /// The colour is the dealer's if he has one and black otherwise, so the default is
        /// still the anonymous car this always spawned and only a man who has been given a
        /// colour deviates from it.
        /// </summary>
        private static void BlackOut(Vehicle car, int paint, string plate)
        {
            try
            {
                var colour = paint >= 0 ? paint : BlackPaint;

                Function.Call(Hash.SET_VEHICLE_MOD_KIT, car.Handle, 0);

                Function.Call(Hash.SET_VEHICLE_COLOURS, car.Handle, colour, colour);
                Function.Call(Hash.SET_VEHICLE_EXTRA_COLOURS, car.Handle, colour, colour);
                Function.Call(Hash.SET_VEHICLE_WINDOW_TINT, car.Handle, LimoTint);

                // Black wheels and trim, so nothing on it catches the light.
                Function.Call(Hash.SET_VEHICLE_WHEEL_TYPE, car.Handle, 7);
                Function.Call(Hash.SET_VEHICLE_MOD_COLOR_1, car.Handle, 0, 0, 0);
                Function.Call(Hash.SET_VEHICLE_MOD_COLOR_2, car.Handle, 0, 0);

                Function.Call(Hash.SET_VEHICLE_DIRT_LEVEL, car.Handle, 0f);
                // HIS OWN PLATE WHERE HE HAS ONE. The fleet plate is what a car with nobody
                // behind it wears; a man who has been supplying this block for years has
                // put something of his own on the back of his.
                Function.Call(Hash.SET_VEHICLE_NUMBER_PLATE_TEXT, car.Handle,
                              string.IsNullOrEmpty(plate) ? "HOODRCH" : plate);

                OpenHisWindow(car);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not paint the delivery car: " + ex.Message);
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
                                  where.X, where.Y, where.Z, CruiseSpeed, DriveStyle, 18f);
                    return;
                }

                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, _driver.Handle, _car.Handle,
                              where.X, where.Y, where.Z,
                              ApproachSpeed, 0, _car.Model.Hash, DriveStyle, 3f, StraightLineAt);
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
                // He drives at the mark. There is no park manoeuvre any more.
                //
                // TASK_VEHICLE_PARK was doing the job it is for and the job was wrong: style 2
                // is a parallel park, which means driving past the space and reversing into it,
                // and style 3 still hands the last few metres to a manoeuvre that shuffles. The
                // last few metres of a park are the hardest thing the driving AI does, and a
                // spot outside a front gate with a fence one side and a parked car the other is
                // exactly where it gives up and rocks back and forth.
                //
                // A straight drive gets him near the mark facing roughly the right way, and
                // ParkOnTheMark straightens him onto it the moment nobody is looking. That was
                // always the fallback; it is the whole plan now.
                if (Game.GameTime - _lastRetask > RetaskIntervalMs)
                {
                    _lastRetask = Game.GameTime;
                    DriveTo(Kerb());
                }

                Unstick();

                // He has had long enough.
                //
                // A car can be forty seconds from a kerb it will never quite reach. A mark on
                // a street with a fence one side and a parked car the other is somewhere the
                // driving AI can circle without ever closing the last few metres, and dropping
                // the park task does not change that -- it only removes the reversing. The
                // drive above is still tried first and this only fires after a minute of it,
                // but a delivery that never arrives is the one outcome that cannot stand.
                if (Game.GameTime - _stateSince > SettleForItMs && toSpot < 90f)
                {
                    Log.Warn("Delivery: close but not arriving after " +
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

            Notify.Text(Portrait, _def.Name, "Los Santos", _def.SayOutside(), true);
        }

        /// <summary>How long he is given to park himself before he is simply put on the mark.</summary>
        private const int SettleForItMs = 60000;

        /// <summary>Whether the phone has been put away for this run.</summary>
        private bool _messageSent;

        /// <summary>
        /// Parks him on the mark, facing the way the street runs.
        ///
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
            _owedPurity = _def == null ? 1f : _def.Purity;

            _dropSpot = HouseDoor;
            _wentIn = false;
            _carryingSince = Game.GameTime;
            _carrying = false;
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

                // Shut the moment it is in his hand, and INSIDE the try on purpose -- if the
                // prop never made it into his hand the boot stays up, because a boot that
                // closes on nothing is a man who has taken delivery of thin air.
                ShutTheBoot();

                Stagger(true);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not start the drop-off: " + ex.Message);
            }

            Speak(Mine(_def == null ? null : _def.CarryLines, PortCarry, CornerCarry), TakingLines);
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

        /// <summary>
        /// A noise. Not a sentence.
        ///
        /// The written lines went out as subtitles for a while and they are gone: a caption
        /// across the bottom of the screen every time a man walks up a path is the mod talking
        /// over the game, and there is nothing in any of them you cannot see him doing.
        ///
        /// He still makes a sound at every one of those moments, which is the half that was
        /// worth having -- you hear somebody arrive, take the weight and leave without being
        /// read to. The words stay in the file because they are what the barks are chosen
        /// against, and because putting them back is one line.
        /// </summary>
        /// <summary>
        /// The line, on screen, and a noise out of him to go with it.
        ///
        /// It used to take the words and drop them -- every call passed a set of written lines
        /// and every call played a grunt instead, so the whole delivery happened in silence
        /// with a man mouthing GENERIC_YES at you. The words are the point; the speech name is
        /// there so his mouth moves while they are up, exactly as it is for Lamar on the ride.
        ///
        /// There is no recording of any of this and there never will be. A subtitle with a
        /// name on it is how every other scene in this mod solves that.
        /// </summary>
        private void Speak(string[] words, string[] voice)
        {
            Say(voice);

            if (words == null || words.Length == 0) return;

            // NO CAPTION. The line goes to its recording if there is one and nowhere if there
            // is not: a yellow caption over the delivery was the one piece of this that read
            // as a mod, and the man is stood right there making his own noise. Cued, so a
            // recorded line plays every time it comes up, and a line with no recording is
            // logged under its name at Debug, which is how the to-record list finds it.
            try
            {
                var line = Fresh(words);
                var who = _def == null ? "" : _def.Name;
                var key = Voice.Key(who, line);

                if (!Voice.Cue(key)) Log.Debug("Voice: nothing for " + key + " -- " + line);
            }
            catch
            {
                // He still made a noise, and the delivery still lands.
            }
        }

        /// <summary>
        /// Whose mouth this is. The man off the boat, or the man off the corner.
        ///
        /// Same test the carry animation uses -- a delivery is either the port or it is not,
        /// and everything about how it looks and sounds follows from that one fact.
        /// </summary>
        private string[] Mine(string[] port, string[] corner)
        {
            return Bales() ? port : corner;
        }

        /// <summary>
        /// His own words if he has any, otherwise whichever house set fits him.
        ///
        /// The fallback is not a placeholder. The port lines were written for the man at the
        /// port and the corner lines for the man on the corner, so the two who do not override
        /// anything are the two the originals were about.
        /// </summary>
        private string[] Mine(List<string> own, string[] port, string[] corner)
        {
            if (own != null && own.Count > 0) return own.ToArray();
            return Mine(port, corner);
        }

        /// <summary>
        /// A line from the set, never the same one twice running.
        ///
        /// Six lines picked blind repeat about one delivery in six, and a repeat inside a set
        /// this small is what makes the whole set feel like two lines. Remembering the last one
        /// per set costs nothing and takes those odds to zero.
        /// </summary>
        private string Fresh(string[] pool)
        {
            if (pool.Length == 1) return pool[0];

            string last;
            _lastLine.TryGetValue(pool, out last);

            string pick;
            var tries = 0;

            do { pick = pool[Rng.Next(pool.Length)]; }
            while (pick == last && ++tries < 8);

            _lastLine[pool] = pick;
            return pick;
        }

        /// <summary>The last line taken from each set. Keyed on the array itself.</summary>
        private readonly Dictionary<string[], string> _lastLine =
            new Dictionary<string[], string>();

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
            // Kept asking until the dictionary lands. It is normally in by the time he is out
            // of the car, and on a cold start it is a stride or two.
            //
            // GATED, and this is why Gerald kept cradling a package in both arms after the
            // gate was added. The gate was on the first attempt only; this retry asked the
            // same question a tick later without it, found _carrying false -- which it always
            // is for him, by design -- and started the animation anyway. A guard that a retry
            // walks straight around is not a guard.
            if (Bales() && !_carrying && _box != null && _box.Exists()) _carrying = PlayCarry();

            CentreOnHands();

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

            // The door is halfway, not the end.
            //
            // Reaching the front door used to be the delivery -- he stopped on the step, put
            // the box on the mat and turned round. Now the door is the first of two legs, and
            // arriving at it sends him on through the house to the store room. Everything
            // below this only runs once he is at the far spot or has run out of time getting
            // there, so the drop, the payment and the line he says are all unchanged.
            if (!_wentIn)
            {
                _wentIn = true;
                _dropSpot = DropInside;
                _carryingSince = Game.GameTime - (CarryTimeoutMs - WalkInMs);
                _nextNudge = 0;

                try
                {
                    Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, _driver.Handle,
                                  _dropSpot.X, _dropSpot.Y, _dropSpot.Z,
                                  1.0f, WalkInMs, 1.0f, 0, 0f);
                }
                catch { /* the timeout below will put it down regardless */ }

                return;
            }

            // Put down where it was ALWAYS going, even if he never got there.
            //
            // If the house would not let him in he is standing at the door holding it, and the
            // package still belongs in the store room -- so the box goes to the spot rather
            // than to wherever he happens to be stood. It is the one thing here that has to
            // work whatever the door does.
            _dropSpot = DropInside;

            PutDown();
            Land();
            Speak(Mine(_def == null ? null : _def.DropLines, PortDrop, CornerDrop), DroppedLines);

            State = DeliveryState.Leaving;
            _stateSince = Game.GameTime;

            try
            {
                _driver.Task.ClearAll();

                // Not if he never got out. Telling a man to board the car he is sitting in
                // makes the game put him out through the door and back in -- which is what the
                // drive-by turned out to be, and this path can reach it: if his leave-vehicle
                // was discarded on the way in he is still belted in when the delivery ends.
                if (_car != null && _car.Exists() && !_driver.IsInVehicle(_car))
                {
                    // WALKING, and the native's own documentation is where the number comes
                    // from: "speed 1.0 = walk, 2.0 = run". It was 2, so having handed over the
                    // package he turned and ran out of the house to his car, which reads as
                    // somebody fleeing a burglary rather than a man who has finished a job.
                    //
                    // It is also why Tao never staggered on the way out. A movement clipset is
                    // a WALK clipset -- a drunk running is just a man running -- so the whole
                    // second half of his delivery was sober, and only the walk up the path
                    // ever showed it.
                    // TIMEOUT -1, AND THAT IS THE WHOLE FIX. It was 20000, and a timeout on
                    // this task is not "give up after twenty seconds" -- it is "get in by any
                    // means after twenty seconds", and the means is a warp. So he walked back
                    // across the yard, ran out of clock at the door, and appeared behind the
                    // wheel without ever opening it.
                    //
                    // Rockstar's own scripts are unambiguous about which knob does what. Of
                    // 801 calls, 131 pass timeout -1 with flag 1 -- walk over, open the door,
                    // get in, no clock. The twenty that pass a timeout of 1 pair it with flag
                    // 16, which the native list documents as "teleport directly into vehicle":
                    // a one millisecond clock is how you ASK for the warp. We were asking for
                    // it on a longer fuse.
                    //
                    // Nothing is lost by removing the clock. TickLeaving gives up on its own
                    // after LeaveMs and Cancel hands him and the car back to the game, so the
                    // worst case is a courier the world recycles rather than one who pops.
                    Function.Call(Hash.TASK_ENTER_VEHICLE, _driver.Handle, _car.Handle,
                                  -1, -1, 1f, 1, 0);

                    // Said again on the way out. Nothing has taken it off him, but the walk out
                    // is the longer of the two and the one where it is worth being certain.
                    Stagger(true);
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
                    Speak(Mine(_def == null ? null : _def.PartingLines, PortParting, CornerParting), LeavingLines);

                    try
                    {
                        // A named place he is actually going, up towards Chamberlain, rather
                        // than a point measured off the kerb he is stood on. Same reason as
                        // before -- a car that wanders the moment the door shuts turns round in
                        // somebody's driveway -- but now it is somewhere rather than "that way".
                        var away = World.GetNextPositionOnStreet(LeaveFor);
                        if (away == Vector3.Zero) away = LeaveFor;

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
                                          _car.Model.Hash, DriveStyle, 5f, StraightLineAt);
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

            var taken = House == null ? 0f : House.AddBulk(_owedDrug, _owedGrams, _owedPurity);

            // KILOS OR GRAMS, whichever the delivery actually was.
            //
            // It said kilos every time. That is fine for the port, which is what it was written
            // for, and Gerald brings twenty grams -- which divided by a thousand and rounded is
            // nought, so his deliveries announced themselves as "0 kilos in the house".
            var landed = taken >= 1000f
                ? (taken / 1000f).ToString("0.#") + " kilos"
                : taken.ToString("0.#") + "g";

            Notify.Important("~g~Delivered.~s~ " + landed +
                             " in the house" +
                             (_owedPurity < 0.999f
                                 ? ", ~y~" + Economy.Stash.Percent(_owedPurity) + "%~s~."
                                 : "."));

            Log.Info("Delivery landed: " + taken.ToString("0") + "g " + _owedDrug + ".");

            _owedDrug = "";
            _owedGrams = 0f;
            _owedPurity = 1f;
        }

        private void GiveBox()
        {
            TakeBox();

            foreach (var name in (_def != null && _def.Kind == DealerKind.Docks
                                  && !_def.IsGangDealer ? BoxProps : SmallBoxProps))
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(900)) continue;

                    _box = World.CreateProp(model, _driver.Position, false, false);
                    model.MarkAsNoLongerNeeded();

                    if (_box == null || !_box.Exists()) continue;

                    // TWO DIFFERENT ATTACHMENTS, because they are two different pictures.
                    //
                    // A man cradling a crate in both arms holds it in the middle of his chest,
                    // and a man walking a package to a door holds it at his side. The first
                    // has nothing to do with either hand -- it is centred on HIM -- and trying
                    // to express that as an offset from the left hand bone is what has had
                    // this box hanging off the outside of an arm through three attempts to
                    // move it.
                    //
                    // The reason it kept failing is worth stating: a bone's local axes are not
                    // written down anywhere, are not implied by its name, and change with the
                    // pose. Every fix was a guess at which way was inwards. The PED's own axes
                    // are not a guess -- X is his right, Y is his front, Z is up -- so the
                    // carry attaches to the ped rather than to a hand, and X of zero is dead
                    // centre between his arms by construction rather than by tuning.
                    //
                    // The cost is that it no longer tracks his hands frame by frame. For a man
                    // walking twenty feet up a path holding a box still, that is not a cost.
                    float yaw;

                    // HIS HAND EITHER WAY, to begin with.
                    //
                    // The crate used to be stood in front of his chest by measurement from the
                    // ped's own origin, and it came out level with the top of his head and
                    // stayed there for the whole walk up the path -- snapping into his arms
                    // only when CentreOnHands finally got a look at the pose. A box floating
                    // over a man for twenty feet is worse than a box held slightly wrong.
                    //
                    // The hand bone is never wildly out. So the first attachment is the same
                    // for both, and CentreOnHands upgrades the carried one to the true midpoint
                    // between his hands the moment the animation is actually on him.
                    _centred = false;

                    {
                        // In his hand, so it swings with his arm, and held a little in from
                        // its edge rather than through its middle -- see HandGrip. It was
                        // centred on the bone, on the reasoning that a package in one fist has
                        // its middle in that fist, which holds for something you can close a
                        // hand around and not for a taped envelope a quarter of a metre wide.
                        var off = HandOffset(model, out yaw);

                        // Plus whatever was settled on for this prop on the settings screen --
                        // "In his hand" fits the package the same way it fits the joint.
                        var fit = Economy.Fit.Offset(name);
                        var turn = Economy.Fit.Turn(name);

                        Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, _box.Handle, _driver.Handle,
                                      Function.Call<int>(Hash.GET_PED_BONE_INDEX, _driver.Handle, 60309),
                                      off.X + fit.X, off.Y + fit.Y, off.Z + fit.Z, turn.X, turn.Y, yaw + turn.Z,
                                      false, false, false, false, 2, true);
                    }

                    // The carry animation is the PORT's. Gerald brings twenty grams to a door
                    // in one hand, and a man cradling a flat package in both arms like a crate
                    // of stock reads as somebody miming. It stays stuck to his hand and he
                    // walks up the path normally.
                    _carrying = Bales() && PlayCarry();

                    // Straight away if the dictionary was already in. TickCarrying asks again
                    // on every pass for the case where it was not.
                    CentreOnHands();
                    return;
                }
                catch
                {
                    // Try the next prop.
                }
            }

            Log.Debug("No box prop in this install; he will carry it in his hands.");
        }

        private bool _centred;

        /// <summary>
        /// Puts the crate exactly between his hands, once they are actually holding it.
        ///
        /// MEASURED. Every previous go at this was a number somebody picked, and the number
        /// was wrong three times running because "the middle of both arms" is not a constant
        /// -- it is wherever the animation happens to put his hands, and that is a thing the
        /// game will tell you if you ask it.
        ///
        /// So both hand bones are read, the point halfway between them is the answer, and it
        /// is converted into the ped's own space -- where it can be handed straight to an
        /// attachment. X falls out dead centre because it is the midpoint of a left and a
        /// right; nothing is tuned to make that true.
        ///
        /// Raised by half the crate's own height, because the carry pose has his hands under
        /// it rather than around it, and the middle of a box sitting on your hands is half a
        /// box above them.
        ///
        /// Deliberately after the animation, and once. Sampled before it, the hands are in
        /// whatever pose he got out of the car in and the answer is his idle stance rather
        /// than his carry. Sampled every frame it would fight the animation's own motion, and
        /// a box that corrects itself continuously reads worse than one held slightly wrong.
        /// </summary>
        private void CentreOnHands()
        {
            if (_centred || !_carrying) return;
            if (_box == null || !_box.Exists()) return;
            if (_driver == null || !_driver.Exists() || !_driver.IsAlive) return;

            try
            {
                var left = Function.Call<Vector3>(Hash.GET_PED_BONE_COORDS,
                                                  _driver.Handle, LeftHand, 0f, 0f, 0f);
                var right = Function.Call<Vector3>(Hash.GET_PED_BONE_COORDS,
                                                   _driver.Handle, RightHand, 0f, 0f, 0f);

                // A bone the game will not answer for comes back as the world origin, which is
                // four kilometres away and would put the crate in the sea.
                if (left == Vector3.Zero || right == Vector3.Zero) return;
                if (left.DistanceTo(right) > 1.5f) return;

                var mid = (left + right) * 0.5f;

                var local = Function.Call<Vector3>(Hash.GET_OFFSET_FROM_ENTITY_GIVEN_WORLD_COORDS,
                                                   _driver.Handle, mid.X, mid.Y, mid.Z);

                var model = _box.Model;
                var size = Measure(model);
                var centre = Middle(model);

                var yaw = 0f;

                if (size.Y > size.X * 1.4f)
                {
                    yaw = 90f;
                    centre = new Vector3(-centre.Y, centre.X, centre.Z);
                    size = new Vector3(size.Y, size.X, size.Z);
                }

                // AND FORWARD, off his chest.
                //
                // The hand midpoint is where his hands are, and his hands in the carry pose are
                // held in against his body -- so putting the box's MIDDLE there buries half of
                // it in his ribs. What belongs at his hands is the box's near face; its middle
                // is half a box further out. Measured, so a deep crate clears him by more than
                // a flat one does, exactly as much more as it is deeper.
                //
                // The half-depth is size.Y in both cases: a long prop has already been turned
                // ninety degrees above and had its extents swapped with it, so Y is the axis
                // pointing out of his chest whichever way round the thing ended up.
                var out_ = local.Y + size.Y * 0.5f + ChestClear;

                var sit = new Vector3(local.X, out_, local.Z + size.Z * 0.5f + ChestLift) - centre;

                Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, _box.Handle, _driver.Handle,
                              0, sit.X, sit.Y, sit.Z, 0f, 0f, yaw,
                              false, false, false, false, 2, true);

                _centred = true;

                Log.Info("Crate centred on his hands at " +
                         sit.X.ToString("0.00") + ", " + sit.Y.ToString("0.00") + ", " +
                         sit.Z.ToString("0.00") + ".");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not centre the crate: " + ex.Message);
            }
        }

        /// <summary>
        /// A little above his hands, rather than exactly level with them.
        ///
        /// The bone midpoint is where his palms are, and a man carrying a box has it resting
        /// ON his hands with the mass above them -- so measuring to the middle of the box and
        /// stopping there hangs it at his thighs. This is the rest of the lift, and it is the
        /// last number in this placement that is a judgement rather than a measurement.
        /// </summary>
        private const float ChestLift = 0.11f;

        /// <summary>
        /// Daylight between the box and his shirt.
        ///
        /// Small, because half the box's own depth is doing the actual work -- this is only the
        /// gap that stops a corner grazing his forearm as he walks.
        /// </summary>
        private const float ChestClear = 0.05f;

        /// <summary>SKEL_L_Hand and SKEL_R_Hand. The hands themselves, not the prop sockets.</summary>
        private const int LeftHand = 18905;
        private const int RightHand = 57005;

        /// <summary>
        /// A package in one hand, centred on the hand bone.
        ///
        /// No sideways push. That existed for the crate, where the left hand is at one END of
        /// what is being held and the middle of it is half a box-width further in -- and it is
        /// the reason a package Gerald carries in one fist ended up floating outside his arm.
        /// A thing held IN a hand has its middle in that hand, and the only correction it needs
        /// is for the model's own pivot.
        /// </summary>
        /// <summary>
        /// How far along its own length the fist sits, as a fraction of that length.
        ///
        /// 0 is dead centre, which is what this used to be and what looked wrong: the comment
        /// above the attach reasoned that a package in one fist has its middle in that fist,
        /// which is true of a thing you can close your hand around and not true of a taped
        /// envelope twenty-five centimetres across. His hand ended up in the middle of it, and
        /// a hand in the middle of a flat package reads as the package being skewered rather
        /// than carried. People hold a package like that nearer an edge.
        ///
        /// A FRACTION rather than a distance, because the prop is whichever one this install
        /// happens to have -- the port's metre-long bale and Gerald's envelope both come
        /// through here, and a fixed number of centimetres that suits one puts the other
        /// somewhere silly. A quarter is a hand a little in from the edge, which is where one
        /// naturally goes.
        ///
        /// NEGATIVE is toward the model's own origin end. If it has gone the wrong way it is
        /// this sign and nothing else -- the axis is picked for you below.
        /// </summary>
        private const float HandGrip = -0.25f;

        /// <summary>
        /// How far it is pulled in toward him, across its own width.
        ///
        /// The grip offset above moves it ALONG its length, which is what stops his fist being
        /// in the middle of it. This is the other axis: the package was sitting far enough out
        /// that his forearm went through the corner of it, so it comes in against him instead
        /// of being held away at arm's width.
        ///
        /// A fraction of the prop's own width for the same reason the other one is: the port's
        /// bale and Gerald's envelope both come through here, and a fixed number of centimetres
        /// that suits one buries the other in his ribs.
        ///
        /// NEGATIVE is toward him. If it has gone out rather than in, this sign is the whole
        /// fix -- the axis is worked out below and follows the prop round when it is turned.
        /// </summary>
        private const float HandIn = -0.30f;

        private static Vector3 HandOffset(Model model, out float yaw)
        {
            yaw = 0f;

            var size = Measure(model);
            var centre = Middle(model);

            if (size.Length() < 0.01f) return Vector3.Zero;

            // Whether the long side is already pointing out of his fist, or has to be turned
            // to. The turn is a quarter circle, and it takes the middle round with it.
            var turned = size.Y > size.X * 1.4f;

            if (turned)
            {
                yaw = 90f;
                centre = new Vector3(-centre.Y, centre.X, centre.Z);
            }

            var off = -centre;

            // Two shifts: one along its length, one across its width. Both written for the
            // UNTURNED case and then put through the same quarter turn the prop got, so a long
            // prop slides along itself rather than across his palm.
            var along = (turned ? size.Y : size.X) * HandGrip;
            var into = (turned ? size.X : size.Y) * HandIn;

            // The turn above maps (x, y) onto (-y, x). Anything added afterwards has to be
            // mapped the same way or the two disagree the moment a bale comes through.
            return turned
                ? new Vector3(off.X - into, off.Y + along, off.Z)
                : new Vector3(off.X + along, off.Y + into, off.Z);
        }

        /// <summary>How big a model is, in its own space. Zero if it cannot be read.</summary>
        private static Vector3 Measure(Model model)
        {
            try
            {
                var lo = new OutputArgument();
                var hi = new OutputArgument();
                Function.Call(Hash.GET_MODEL_DIMENSIONS, model.Hash, lo, hi);

                return hi.GetResult<Vector3>() - lo.GetResult<Vector3>();
            }
            catch
            {
                return Vector3.Zero;
            }
        }

        /// <summary>
        /// Where a model's middle sits relative to its own origin.
        ///
        /// Subtracted from every placement, and it is what stops a prop pivoted at its base and
        /// a prop pivoted through its middle needing two different sets of numbers.
        /// </summary>
        private static Vector3 Middle(Model model)
        {
            try
            {
                var lo = new OutputArgument();
                var hi = new OutputArgument();
                Function.Call(Hash.GET_MODEL_DIMENSIONS, model.Hash, lo, hi);

                return (lo.GetResult<Vector3>() + hi.GetResult<Vector3>()) * 0.5f;
            }
            catch
            {
                return Vector3.Zero;
            }
        }

        /// <summary>Whether the carry clip has actually been put on him yet.</summary>
        private bool _carrying;

        /// <summary>
        /// Starts the dictionary loading, well before anybody needs it.
        ///
        /// Called when the delivery is arranged, so the whole drive across the city is the
        /// loading window. Costs nothing once it is in.
        /// </summary>
        private static void WantCarry()
        {
            try
            {
                if (Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, CarryDict)) return;
                Function.Call(Hash.REQUEST_ANIM_DICT, CarryDict);
            }
            catch
            {
                // It will be asked for again on the next pass.
            }
        }

        /// <summary>
        /// Puts the box in his hands, once the game has the animation to do it with.
        ///
        /// THIS IS WHY HE CARRIED IT AT HIS HIP. REQUEST_ANIM_DICT is a request, not a load:
        /// it returns immediately and the dictionary arrives some frames later. The old version
        /// asked for the dictionary and then tested HAS_ANIM_DICT_LOADED on the very next line,
        /// which is false for a cold dictionary every time -- so it skipped that entry, skipped
        /// the other two, ran off the end of the list and played nothing at all. Every delivery
        /// ever made walked up the path with a package welded to one hand and both arms down.
        ///
        /// This file knew it, too. PrepAnimation carries a comment saying in as many words that
        /// the first attempt at a cold dictionary always fails.
        ///
        /// So it returns whether it managed it, and the carry tick keeps asking until it does.
        /// The clip is upper body and looped, so it layers over whatever walk he is doing
        /// rather than replacing it -- which is the other half of why he has to be given the
        /// walk task first and this second.
        /// </summary>
        /// <summary>
        /// Whether this delivery is big enough to be carried with both arms.
        ///
        /// The same question the box list asks: the port brings a bale off a boat and
        /// everybody else brings a package. Only the bale is worth an animation.
        /// </summary>
        private bool Bales()
        {
            return _def != null && _def.Kind == DealerKind.Docks && !_def.IsGangDealer;
        }

        private bool PlayCarry()
        {
            if (_driver == null || !_driver.Exists() || !_driver.IsAlive) return false;

            try
            {
                if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, CarryDict))
                {
                    Function.Call(Hash.REQUEST_ANIM_DICT, CarryDict);
                    return false;
                }

                foreach (var clip in CarryClips)
                {
                    Function.Call(Hash.TASK_PLAY_ANIM, _driver.Handle, CarryDict, clip,
                                  4f, -4f, -1, 49, 0f, false, false, false);

                    Log.Info("Delivery carry: " + CarryDict + " / " + clip + ".");
                    return true;
                }
            }
            catch
            {
                // Asked again next pass.
            }

            return false;
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

            // His, not whoever is carrying. The clipset was applied to the courier rather than
            // to the drunk, so the man whose whole character is being wound too tight walked
            // the parcel up the path like he had been in Bahama Mamas all afternoon.
            if (on && (_def == null || !_def.Drunk)) return;

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

            // The first thing he says, and he says it on his feet.
            //
            // Not in Arrive(), which is where it would go if the beat were "has pulled up" --
            // at that moment he is still belted into the driver's seat with the leave-vehicle
            // task barely handed out, and a man talking through a windscreen is the wrong
            // picture. This waits until he is actually standing on the street.
            if (!_greeted && _car != null && _car.Exists() && !_driver.IsInVehicle(_car))
            {
                _greeted = true;

                Speak(Mine(_def == null ? null : _def.ArrivalLines, PortArrival, CornerArrival), TakingLines);

                OpenTheBoot();
                WalkToMeet();
            }

            // And kept there. A man standing in a spot gets nudged out of it by traffic, by you
            // walking into him, and by the game's own idle shuffling, and the prompt follows HIM
            // rather than the car -- so if he drifts, the place you have to stand to talk drifts
            // with him.
            if (_greeted) HoldTheSpot();

            if (Distance > AbandonDistance) Cancel("You left him standing there.");
        }

        /// <summary>Whether he has said his piece on getting out, this run.</summary>
        private bool _greeted;

        /// <summary>
        /// Where he stands to do business, once he is out of the car.
        ///
        /// On the kerb rather than at the driver's door. Read off the HUD standing on the spot:
        /// far enough round the car that you are not talking to him through it, and facing the
        /// way you come from.
        /// </summary>
        private static readonly Vector3 MeetSpot = new Vector3(-18.198f, -1455.772f, 30.481f);
        private const float MeetHeading = 356.059f;

        /// <summary>How far he may drift before he is walked back.</summary>
        private const float MeetDrift = 1.8f;

        /// <summary>Task 224 is the nav-mesh walk. He is already going; leave him alone.</summary>
        private const int WalkTask = 224;

        /// <summary>
        /// The boot, up.
        ///
        /// Door 5 is the boot. He is here to hand over weight and it comes out of the back of
        /// the car, so the car should look like a car somebody is unloading rather than one
        /// that has simply stopped.
        /// </summary>
        private void OpenTheBoot()
        {
            if (_car == null || !_car.Exists()) return;

            try { Function.Call(Hash.SET_VEHICLE_DOOR_OPEN, _car.Handle, BootDoor, false, false); }
            catch { /* it stays shut, and nothing else changes */ }
        }

        private void ShutTheBoot()
        {
            if (_car == null || !_car.Exists()) return;

            try { Function.Call(Hash.SET_VEHICLE_DOOR_SHUT, _car.Handle, BootDoor, false); }
            catch { /* it goes when the car does */ }
        }

        private const int BootDoor = 5;

        /// <summary>
        /// Round to the kerb, and facing the right way when he gets there.
        ///
        /// The nav mesh rather than a straight line: between him and that spot is his own car,
        /// and a straight-line walk puts him into the wing and leaves him grinding against it.
        /// The last argument is the heading he settles on, which is how every other walk in this
        /// mod ends up pointing the right way.
        /// </summary>
        private void WalkToMeet()
        {
            if (_driver == null || !_driver.Exists() || !_driver.IsAlive) return;

            try
            {
                // Weaving, if he is the one who weaves.
                //
                // The clipset was only ever put on for the walk up the path with the parcel,
                // so the walk from the car to where he stands -- the one you actually watch
                // him do, every single delivery -- was a sober man's. Stagger checks whose
                // delivery this is, so the other courier still walks like a man on a bike.
                Stagger(true);

                Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, _driver.Handle,
                              MeetSpot.X, MeetSpot.Y, MeetSpot.Z,
                              1.2f, 20000, 0.5f, 0, MeetHeading);
            }
            catch
            {
                // He waits by the door, which is where he used to wait anyway.
            }
        }

        /// <summary>Walks him back if he has been shoved off it, and not more than once a second.</summary>
        private void HoldTheSpot()
        {
            if (_driver == null || !_driver.Exists() || !_driver.IsAlive) return;
            if (Game.GameTime < _nextHold) return;

            _nextHold = Game.GameTime + 1000;

            try
            {
                if (_driver.Position.DistanceTo(MeetSpot) <= MeetDrift) return;
                if (Function.Call<bool>(Hash.GET_IS_TASK_ACTIVE, _driver.Handle, WalkTask)) return;

                WalkToMeet();
            }
            catch { /* he stays where he stopped */ }
        }

        private int _nextHold;

        // ---- cleanup -----------------------------------------------------------

        /// <summary>Ends the run and lets the world have the driver and the van back.</summary>
        public void Cancel(string reason)
        {
            // The bale goes with it. See DropTheBox -- every abnormal ending came through here
            // and none of them released the prop.
            DropTheBox();

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
            // Whether or not anything came out of it. A car driving off with its boot up is a
            // car nobody shut, and he is not that man.
            ShutTheBoot();

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

                    // Walking here too. Same reason as the leaving path above: 2.0 is a run,
                    // and a courier sprinting to his own car is a man being chased.
                    // Same again, and the ped argument is 0 because that is how a task goes
                    // into a sequence rather than onto a ped -- Rockstar do it identically in
                    // abigail2 and docks_setup. Only the clock was wrong.
                    Function.Call(Hash.TASK_ENTER_VEHICLE, 0, _car.Handle, -1, -1, 1f, 1, 0);

                    // A DESTINATION first, and only then a wander.
                    //
                    // He used to go straight to TASK_VEHICLE_DRIVE_WANDER off a kerbside stop
                    // with a fence one side and parked cars the other. Wander has no
                    // destination -- it picks a direction and negotiates from where it is
                    // standing -- and from that spot it is a car rocking back and forth
                    // against a kerb for as long as anybody watches it.
                    //
                    // Arriving was never the problem and is untouched. Leaving now has a road
                    // to get to first: he drives to the departure point, which is out on a
                    // through road, and wanders from THERE where wandering works.
                    Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, 0, _car.Handle,
                                  LeaveFor.X, LeaveFor.Y, LeaveFor.Z,
                                  CruiseSpeed, 0, _car.Model.Hash, DriveStyle, 12f, StraightLineAt);

                    Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, 0, _car.Handle,
                                  CruiseSpeed, DriveStyle);

                    Function.Call(Hash.CLOSE_SEQUENCE_TASK, handle);
                    Function.Call(Hash.TASK_PERFORM_SEQUENCE, _driver.Handle, handle);
                    Function.Call(Hash.CLEAR_SEQUENCE_TASK, seq);

                }
            }
            catch { /* he can walk if he likes */ }

            // NOT Cancel(), and that is the fix.
            //
            // Cancel unblocks non-temporary events and hands the ped back on the same frame --
            // and at this point he is stood on the pavement with a sequence telling him to get
            // into a car. An unblocked ped reacts to the world: a gunshot two streets away, an
            // armed player next to him, anything at all, and he abandons the sequence and
            // walks. What you were left looking at was his car sitting there with the boot
            // open and nobody in it.
            //
            // So he is handed to the Leaving state instead, which is the path that already
            // works: it waits until he is actually behind the wheel, says goodbye from the
            // driver's seat, gives him somewhere to drive TO, and only then releases him.
            try
            {
                if (_blip != null && _blip.Exists()) _blip.Delete();
                _blip = null;
            }
            catch { /* teardown */ }

            EndPhoneAnimation();

            State = DeliveryState.Leaving;
            _stateSince = Game.GameTime;
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
        private static readonly Vector3 StartPoint = new Vector3(72.825f, -1474.496f, 28.665f);

        /// <summary>
        /// Which way he is pointing when he appears.
        ///
        /// Set rather than left to the game. A car created without a heading faces due north
        /// whatever the road does, so the first thing he did was a three point turn on a main
        /// road in front of anybody standing there.
        /// </summary>
        private const float StartHeading = 143.024f;

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

        /// <summary>
        /// Drops the bale, wherever the delivery got to.
        ///
        /// _box is a prop this class makes and attaches to the driver, and only the two normal
        /// endings ever released it. Every abnormal one -- you died, you drove off, the driver
        /// was killed, the script unloaded -- went through Cancel, which tidies the phone, the
        /// driver, the car and the blip and never mentions the box. A pallet of prop
        /// stayed welded to a corpse or floating in the front garden for the session.
        /// </summary>
        private void DropTheBox()
        {
            try
            {
                if (_box != null && _box.Exists())
                {
                    Function.Call(Hash.DETACH_ENTITY, _box.Handle, true, true);
                    _box.IsPersistent = false;
                    _box.Delete();
                }
            }
            catch { /* the streamer gets it */ }

            _box = null;
        }

        public void RestoreWorld() => Cancel(null);
    }
}
