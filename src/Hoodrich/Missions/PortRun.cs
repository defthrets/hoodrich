using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Social;
using Hoodrich.State;
using Hoodrich.UI;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.Missions
{
    /// <summary>
    /// The run out to the port, and the box coming back.
    ///
    /// The port used to open because a counter passed fifty grams. Gerald said a sentence, a
    /// contact appeared in the phone, and you had never been to the port or met anybody at it --
    /// so the one real progression gate in the whole mod was a number going up in a menu.
    ///
    /// Now it is a drive, in two halves, and they are deliberately different halves. Going out
    /// is an INTRODUCTION: a man you have never met is stood by his own car with the boot open,
    /// he hands you something on somebody else's word, and you leave with his number. Coming
    /// back is a DELIVERY: you are carrying a quarter of a kilo that is not yours across half
    /// the city to a yard, which is the job the rest of the mod is made of.
    ///
    /// And it is done in HIS van, not in whatever you happened to arrive on. A quarter kilo
    /// does not go in a saddlebag; a filthy old Rumpo parked up a street in Chamberlain is what
    /// somebody actually moves weight in, and being handed the keys to it is the first thing
    /// Gerald has ever trusted you with.
    ///
    /// Nothing here can be failed. There is no timer, nobody jumps you, and the box cannot be
    /// lost -- because the point of the trip is the two people at the ends of it, and a first
    /// meeting that can go wrong is a first meeting you reload rather than have.
    /// </summary>
    internal sealed class PortRun
    {
        public const int StageNone = 0;

        /// <summary>Told where to go, not yet spoken to him.</summary>
        public const int StageFetch = 1;

        /// <summary>Spoken to, and owed a reverse park round the back of the sheds.</summary>
        public const int StageBay = 2;

        /// <summary>Loaded, and owing him a yard in Chamberlain.</summary>
        public const int StageDeliver = 3;

        // ---- his van -----------------------------------------------------------

        /// <summary>
        /// Where it is parked. Up the street from him, nose south, against the kerb.
        /// </summary>
        private static readonly Vector3 VanSpot = new Vector3(-178.840f, -1634.584f, 33.290f);
        private const float VanHeading = 181.403f;

        /// <summary>
        /// A pickup, which ends the argument about branding rather than continuing it.
        ///
        /// Three vans were tried and all three came with somebody's advertising. The rumpo
        /// carries exactly two liveries and index 0 is Weazel News, with no way to say "none".
        /// rumpo2 has no livery table at all -- and turned out to have DELUDAMOL painted into
        /// the model itself, which no livery call can touch. That is not a bug to keep fixing;
        /// vans in this game are advertising with wheels.
        ///
        /// A Yosemite has no livery table, no extras and nothing written on it. And a pickup
        /// bed is better for this anyway: the load sits in the open where you can see it,
        /// instead of being a box you have to take on trust behind a closed door.
        ///
        /// Hash checked against the spawner rather than typed off a menu -- yosemite1500 is
        /// 0x8EF5E388, which is the truck that was picked. The plain yosemite behind it is the
        /// same shape for an install without the newer one.
        /// </summary>
        private static readonly string[] VanModels = { "yosemite1500", "yosemite", "yosemite2", "rumpo2" };

        /// <summary>Metallic dark green -- the index, not an RGB, so it takes the flake.</summary>
        private const int VanGreen = 49;

        /// <summary>Filthy. Fifteen is as far as the game goes.</summary>
        private const float VanDirt = 15f;

        /// <summary>How near the van has to be for anything to be loaded into it.</summary>
        private const float VanRange = 15f;

        // ---- the port ----------------------------------------------------------

        /// <summary>Where you leave it, which is what the marker and the blip point at.</summary>
        private static readonly Vector3 ParkSpot = new Vector3(780.991f, -2973.205f, 5.801f);

        /// <summary>Where he stands.</summary>
        private static readonly Vector3 HisSpot = new Vector3(779.273f, -2976.181f, 5.801f);
        private const float HisHeading = 18.452f;

        /// <summary>And where his car is, boot up.</summary>
        private static readonly Vector3 CarSpot = new Vector3(782.038f, -2977.529f, 5.188f);
        private const float CarHeading = 249.706f;

        // ---- and the yard it goes to -------------------------------------------

        /// <summary>
        /// Where the van goes. The ring on the ground, and nothing else.
        ///
        /// He used to stand ON this, which made one mark mean two things -- park here AND the
        /// man is here. They are nine metres apart in reality: you pull the van into the yard
        /// and he is up by the garage door watching you do it, which is where somebody waiting
        /// on a delivery actually stands.
        /// </summary>
        private static readonly Vector3 DropSpot = new Vector3(-103.338f, -1417.405f, 29.170f);

        /// <summary>And where he waits: up at the pill press roller door, watching the yard.</summary>
        private static readonly Vector3 GeraldSpot = new Vector3(-106.059f, -1408.996f, 29.706f);
        private const float DropHeading = 213.916f;

        // ---- bay one, round the back ------------------------------------------

        /// <summary>Where the van has to end up, and which way round.</summary>
        private static readonly Vector3 BaySpot = new Vector3(1245.656f, -3165.266f, 5.645f);
        private const float BayHeading = 270.509f;

        /// <summary>How close, how square, and how stopped it has to be.</summary>
        private const float BayRange = 4.2f;
        private const float BaySquare = 38f;
        private const float BayStopped = 1.6f;

        /// <summary>Where his men come from. Under a roof, so the ground probe is guarded.</summary>
        private static readonly Vector3 LoaderFrom = new Vector3(1242.045f, -3173.763f, 5.528f);
        private const float LoaderFromHeading = 355.469f;

        /// <summary>And where they end up, which is the back of the van.</summary>
        private static readonly Vector3 LoaderTo = new Vector3(1242.067f, -3165.410f, 5.528f);
        private const float LoaderToHeading = 276.086f;

        private const int LoaderCount = 2;

        /// <summary>How far apart they stand, so two men are not one man.</summary>
        private const float LoaderGap = 0.95f;

        /// <summary>Close enough to the back of the van to be loading it.</summary>
        private const float LoaderArrive = 2.6f;

        /// <summary>How long they take over it before anybody says anything.</summary>
        private const int LoaderSettleMs = 3000;

        /// <summary>And how long before the job stops waiting on a man stuck on a pallet.</summary>
        private const int LoaderGiveUpMs = 40000;

        /// <summary>
        /// Kkangpae, so the voice that says it is genuinely Korean.
        ///
        /// Ambient speech is spoken in the ped's own voice, and a Korean gang model has a
        /// Korean one -- so this is the difference between a subtitle claiming a language and
        /// a man actually speaking it. All three verified against the game's ped dump.
        /// </summary>
        private static readonly string[] LoaderModels =
        {
            "g_m_y_korean_01", "g_m_y_korean_02", "g_m_y_korlieut_01", "s_m_y_dockwork_01"
        };

        /// <summary>
        /// What ends up in the back. ONE crate, not a stack.
        ///
        /// A PALLET, and that is the point of it. This came off a ship: it is the biggest
        /// single thing in the run and the one moment where you can see what the whole errand
        /// was about, so it should fill the bed rather than sit in the middle of it looking
        /// posted. The drug package that led this list before is a flat taped envelope
        /// twenty-five centimetres across -- correct for what Gerald hands you at a door,
        /// laughable as a quarter kilo off a boat.
        ///
        /// Wooden crates behind them, and the old paper box last, so a model missing from an
        /// install costs the look rather than the load.
        ///
        /// prop_boxpile_07d is out of the list entirely: it is a pile about a metre and a half
        /// tall whose origin sits at its base, so dropped into a bed it came out through the
        /// roof and read as two crates riding on the cab.
        /// </summary>
        private static readonly string[] CrateModels =
        {
            "bkr_prop_coke_pallet_01a", "hei_prop_heist_weed_pallet",
            "bkr_prop_coke_block_01a", "prop_boxpile_06a",
            "prop_box_wood04a", "prop_drug_package", "prop_paper_box_01"
        };

        /// <summary>
        /// Where the box sits in the bed, in the truck's own space.
        ///
        /// A Yosemite measures -2.60 to +2.34 along Y and -0.37 to +1.13 up, so the bed floor
        /// sits a little above the origin and the middle of it is about a metre and a half
        /// back. High rather than low on purpose: a box hovering two centimetres reads as a
        /// box, and one sunk two centimetres reads as a bug.
        /// </summary>
        private const float CrateY = -1.50f;

        /// <summary>Where the bed FLOOR is, in the truck's own space. The rest is measured.</summary>
        private const float BedFloorZ = 0.30f;

        // ---- and the ride home is not quiet ------------------------------------

        /// <summary>Where they are sat waiting, on the way back up out of the docks.</summary>
        private static readonly Vector3 AmbushSpot = new Vector3(429.047f, -1945.180f, 24.288f);
        private const float AmbushHeading = 113.385f;

        /// <summary>Placed at this range, so they are parked before you can see them arrive.</summary>
        private const float AmbushPlace = 130f;

        /// <summary>And they pull out at this one, which is roughly level with them.</summary>
        private const float AmbushWake = 42f;

        /// <summary>
        /// How close to the yard they will follow you.
        ///
        /// They break off a hundred and fifty metres out rather than chasing you onto the
        /// block. Partly because two Vagos in a lowrider do not drive into Chamberlain and
        /// park, and partly because the errand should not end with a firefight in the yard you
        /// are supposed to be quietly dropping a van in.
        /// </summary>
        private const float AmbushLetGo = 150f;

        /// <summary>
        /// What they are in. A gang car, made to keep up with a van.
        ///
        /// Their own cars are lowriders and classics, which is what they should be seen in --
        /// but a stock Tornado tops out below a Rumpo and would spend the whole chase in the
        /// mirror getting smaller. So it is a gang car with the engine, brakes and gearbox
        /// wound to the top, which is a Vagos car somebody has spent money on rather than a
        /// police interceptor in yellow.
        /// </summary>
        private static readonly string[] AmbushCars =
        {
            "buccaneer2", "faction", "voodoo", "tornado", "chino"
        };

        /// <summary>Metallic yellow, out of the game's own table -- the same index gangs.json gives them.</summary>
        private const int VagosPaint = 88;

        private static readonly string[] VagosModels =
        {
            "g_m_y_mexgoon_01", "g_m_y_mexgoon_02", "g_m_y_mexgoon_03", "a_m_y_mexthug_01"
        };

        private const string VagosGun = "WEAPON_MACHINEPISTOL";
        private const int AmbushCrew = 2;

        /// <summary>Close enough to the marker to have arrived.</summary>
        private const float ParkRange = 9f;

        /// <summary>And close enough to a man to talk to him.</summary>
        private const float TalkRange = 3.2f;

        /// <summary>Everything is placed at this range, so it is there before you can see it.</summary>
        private const float StreamRange = 120f;

        private const int TickMs = 400;

        /// <summary>
        /// What Tao stands next to. Hash checked the same way: astron is 0x258C9364.
        ///
        /// Black, because a man doing this in a dock car park did not drive something memorable.
        /// </summary>
        private static readonly string[] CarModels = { "astron", "baller", "cavalcade" };

        private const int Black = 0;

        private static readonly string[] TaoModels =
        {
            "ig_taocheng", "cs_taocheng", "u_m_y_ushi", "a_m_y_business_01"
        };

        /// <summary>The same list leaders.json gives him, so it is the same man either end.</summary>
        private static readonly string[] GeraldModels =
        {
            "ig_g", "csb_g", "g_m_y_famdnf_01", "a_m_m_soucent_01"
        };

        /// <summary>What is in the boot, and what it is worth for carrying it.</summary>
        private const float Package = 250f;
        private const int PayMin = 1800;
        private const int PayMax = 2500;

        private readonly PlayerState _state;

        private Ped _tao;
        private Ped _gerald;
        private Vehicle _car;
        private Vehicle _van;
        private Blip _blip;
        private Blip _vanBlip;

        private int _next;
        private bool _saidHere;

        // ---- the loading bay ---------------------------------------------------

        /// <summary>0 nothing, 1 parked and waiting, 2 on you, 3 finished with.</summary>
        private int _ambush;
        private Vehicle _hunters;
        private Blip _huntBlip;
        private readonly List<Ped> _vagos = new List<Ped>();

        /// <summary>0 waiting on the van, 1 walking, 2 loading, 3 done and wandering.</summary>
        private int _bay;
        private int _bayAt;
        private readonly List<Ped> _loaders = new List<Ped>();
        private Prop _crate;
        private readonly Random _rng = new Random();

        public PortRun(PlayerState state)
        {
            _state = state;
        }

        /// <summary>Set by Main. The screen both meetings happen on.</summary>
        public Conversation Talk;

        /// <summary>The block, so a quarter kilo coming off a boat gets talked about.</summary>
        public SocialFeed Social;

        /// <summary>True while a job owns the top of the screen, so two cards never stack.</summary>
        public Func<bool> Busy;

        public int Stage => _state == null ? StageNone : _state.PortRunStage;

        public bool Running => Stage != StageNone;

        /// <summary>
        /// Whether Gerald is off his corner.
        ///
        /// GangLeaders keeps exactly one leader alive anywhere in the world, and the delivery
        /// puts a second Gerald in a yard two hundred metres from the first. So while the box
        /// is on its way to him he is NOT on the corner at all -- which is not a workaround, it
        /// is the truth: he is stood in the yard waiting for it.
        /// </summary>
        /// <summary>
        /// Whether he is off his corner.
        ///
        /// True while he is lingering as well as while the errand is on, because there is a
        /// window after the hand-off where he is still stood in the yard -- and the corner is
        /// only two hundred metres away, well inside the range the leader system would spawn
        /// the other one at. Two Geralds is worse than a slightly late one.
        /// </summary>
        public bool WaitingAtTheDrop => Stage == StageDeliver || _lingering;

        /// <summary>Where the mark is, whichever leg of the run you are in.</summary>
        private Vector3 Mark =>
            Stage == StageDeliver ? DropSpot :
            Stage == StageBay ? BaySpot :
            ParkSpot;

        // ---- starting it -------------------------------------------------------

        /// <summary>
        /// Gerald sends you. Called from whichever conversation asked him the question.
        ///
        /// Returns false if the run is already on or already done, so a second ask cannot
        /// restart a trip you are halfway through.
        /// </summary>
        public bool Send()
        {
            if (_state == null || _state.DocksUnlocked || Running) return false;

            _state.PortRunStage = StageFetch;
            _state.Touch();

            MakeVan();

            Notify.Important("~g~Take his truck.~s~ Elysian Island, ask for the dock worker.");
            Log.Info("Port run started after " + _state.GramsSold.ToString("0.#") + "g sold.");

            return true;
        }

        // ---- the run itself ----------------------------------------------------

        public void Update()
        {
            if (!Running)
            {
                // He does not vanish while you are stood in front of him.
                //
                // Everything else can go the moment the errand ends -- the blips, the mark,
                // the men at the port -- but deleting the man you have just finished talking
                // to, two feet away, in daylight, is the single most obviously scripted thing
                // this mod could do. So the tidy-up runs and he is left standing there, and he
                // is cleared when you are far enough away that nobody sees it happen.
                if (_lingering)
                {
                    Linger();
                    return;
                }

                if (_tao != null || _gerald != null || _car != null || _van != null || _blip != null) Pack();
                return;
            }

            if (Game.GameTime < _next) return;
            _next = Game.GameTime + TickMs;

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return;

            Route();
            KeepVan();

            // Two scenes, each held up by ITS OWN distance rather than by whichever half of
            // the run is current.
            //
            // Tying them to the stage popped Tao out of existence in front of you the instant
            // he finished talking: the stage flipped to Deliver, the mark jumped four
            // kilometres across the map, and the man you were stood next to was suddenly too
            // far away to exist. He stays until you actually drive off, which is what leaving
            // somewhere looks like.
            // The loading bay is a leg of its own and it is not about walking up to a man,
            // so it runs before the rest and returns.
            if (Stage == StageBay)
            {
                Loading(player);
                return;
            }

            // Somebody has been sat waiting on the road up out of the docks.
            if (Stage == StageDeliver) Ambush(player);

            var toPort = player.Position.DistanceTo(ParkSpot);
            var toDrop = player.Position.DistanceTo(DropSpot);

            if (toPort <= StreamRange) Tao(); else ClearPort();

            if (toDrop <= StreamRange && Stage == StageDeliver) Gerald(); else ClearDrop();

            // He is nine metres off the mark now, so "you have arrived" is about the ring and
            // "go and talk to him" is about him. Two different distances, said separately.

            // The drop plays itself out; the port is still a walk-up.
            if (Stage == StageDeliver && toDrop <= StreamRange && HandItOverHere(player, toDrop))
            {
                return;
            }

            // And only one of them is the man you owe something to.
            var man = Stage == StageDeliver
                ? (toDrop <= StreamRange ? _gerald : null)
                : (toPort <= StreamRange ? _tao : null);

            if (man == null || !man.Exists() || !man.IsAlive) return;

            var away = Stage == StageDeliver ? toDrop : toPort;
            var toHim = player.Position.DistanceTo(man.Position);

            if (toHim > TalkRange)
            {
                if (away <= ParkRange && !_saidHere)
                {
                    _saidHere = true;

                    Notify.Important(Stage == StageDeliver
                        ? "~g~He's waiting.~s~ Hand it over."
                        : "~g~That's him.~s~ Go and talk to him.");
                }

                // And say it on the windscreen as well as in the corner.
                //
                // The toast fires once, on the way in, and is gone by the time the truck has
                // stopped -- so somebody who parks, looks up and sees nothing has been told
                // what to do and has no way of being told again. The prompt below only ever
                // appeared once you were already stood next to him, which is exactly when you
                // no longer need it.
                if (away <= ParkRange && player.IsInVehicle())
                {
                    Help.ShowThisFrame("Get out and talk to him.");
                }

                return;
            }

            // On foot, both ends. One of them is loading a van and the other is unloading it,
            // and neither happens through a window.
            if (player.IsInVehicle())
            {
                Help.ShowThisFrame("Get out and talk to him.");
                return;
            }

            // And the van has to be here, because the van is what the weight travels in. A
            // quarter of a kilo does not go in your pockets and it does not go on a pushbike.
            if (!VanIsNear(man))
            {
                Help.ShowThisFrame(Stage == StageDeliver
                    ? "Bring Gerald's truck -- it's still in the back."
                    : "Bring Gerald's truck. It ain't going in your pockets.");

                return;
            }

            Help.ShowThisFrame(Stage == StageDeliver
                ? "Press ~INPUT_CONTEXT~ to hand it over."
                : "Press ~INPUT_CONTEXT~ to talk to him.");

            // EVERY key the rest of the mod takes for this, and the disabled state with it.
            //
            // Context alone, read live, is why Tao could not be talked to: something else in
            // the frame disables that control -- the vehicle prompts do it constantly around a
            // parked car -- and IS_CONTROL_JUST_PRESSED answers false for a disabled control
            // even while the player is pressing it. Every other walk-up in this mod goes
            // through the same handful of inputs and reads IS_DISABLED_CONTROL_PRESSED too;
            // this was the one place that did not, and it was the one place that did not work.
            if (!WantsToTalk()) return;
            if (Talk == null || Talk.IsOpen) return;

            Talk.Speaker = man;
            Talk.Open(Stage == StageDeliver ? HandOver() : Meeting(), this);
        }

        /// <summary>
        /// How long the truck has to be stood still on the mark before you get out of it.
        ///
        /// Two seconds, which is long enough to be a pause rather than a snatch, and short
        /// enough that nobody sits there wondering whether the run is over.
        /// </summary>
        private const int SettleMs = 2000;

        /// <summary>Close enough that he is talking to you rather than at you.</summary>
        private const float WalkedUpRange = 2.4f;

        private int _stoppedAt;
        private bool _gotOut;
        private bool _walkingOver;

        /// <summary>
        /// The end of the run, played rather than prompted.
        ///
        /// The last thing you did was drive four kilometres with a quarter kilo in the back;
        /// being asked to press a button at the end of that is the mod handing the moment back
        /// to you to perform. So the truck stops, Franklin gets out on his own, and the man
        /// waiting for it walks over -- and the conversation opens when he arrives, because he
        /// is the one who has come to you.
        ///
        /// Every step is guarded on its own flag rather than on distance alone: TASK_LEAVE_
        /// VEHICLE and the walk are both things you ask for ONCE, and asking again every tick
        /// is how a man ends up permanently starting to get out of a car.
        /// </summary>
        private bool HandItOverHere(Ped player, float toDrop)
        {
            if (Stage != StageDeliver) { Reset(); return false; }

            var man = _gerald;
            if (man == null || !man.Exists() || !man.IsAlive) return false;

            if (toDrop > ParkRange) { Reset(); return false; }

            var now = Game.GameTime;

            // Stood still on the mark, in the truck he lent you.
            if (!_gotOut)
            {
                if (!player.IsInVehicle())
                {
                    // Already on foot, so there is nothing to climb out of.
                    _gotOut = true;
                }
                else
                {
                    var ride = player.CurrentVehicle;
                    var still = ride != null && ride.Exists() && ride.Speed < 0.6f;

                    if (!still) { _stoppedAt = 0; return true; }

                    if (_stoppedAt == 0) _stoppedAt = now;
                    if (now - _stoppedAt < SettleMs) return true;

                    try { Function.Call(Hash.TASK_LEAVE_VEHICLE, player.Handle, ride.Handle, 0); }
                    catch { /* he can get out himself */ }

                    _gotOut = true;
                }
            }

            // And he comes over. Asked once.
            if (!_walkingOver)
            {
                _walkingOver = true;

                try
                {
                    Function.Call(Hash.TASK_GO_TO_ENTITY, man.Handle, player.Handle,
                                  20000, WalkedUpRange, 1.4f, 0f, 0);
                }
                catch { /* he will be talked to where he stands */ }
            }

            if (Talk == null || Talk.IsOpen) return true;
            if (player.IsInVehicle()) return true;

            if (player.Position.DistanceTo(man.Position) > WalkedUpRange + 1.2f) return true;

            Talk.Speaker = man;
            Talk.Open(HandOver(), this);
            return true;
        }

        private void Reset()
        {
            _stoppedAt = 0;
            _gotOut = false;
            _walkingOver = false;
        }

        private bool _talkHeld;
        private bool _talkArmed;

        /// <summary>
        /// Watches the walk-up button EVERY FRAME and remembers that it was pressed.
        ///
        /// This is the fix for "it only works if I bash it". A leading edge is "down now, up
        /// last time you looked", and the rest of this file looks every four hundred
        /// milliseconds -- so an ordinary tap begins and ends between two samples and is never
        /// seen at all. Hammering the key worked because it raised the odds of one sample
        /// catching it down while the one before had caught it up.
        ///
        /// Called from Draw, which the game runs every frame, and the press is held until the
        /// tick gets round to acting on it. Every other walk-up in the mod already polls per
        /// frame through its own UpdatePrompt; this was the one that did not.
        /// </summary>
        public void PollTalk()
        {
            var down = false;

            try
            {
                down = Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)GTA.Control.Context)
                    || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)GTA.Control.Context)
                    || Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)GTA.Control.PhoneRight)
                    || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)GTA.Control.PhoneRight)
                    || Game.IsKeyPressed(System.Windows.Forms.Keys.E)
                    || Game.IsKeyPressed(System.Windows.Forms.Keys.Right);
            }
            catch
            {
                // An unreadable control is simply not pressed.
            }

            if (down && !_talkHeld) _talkArmed = true;
            _talkHeld = down;
        }

        /// <summary>Takes the remembered press, if there is one.</summary>
        private bool WantsToTalk()
        {
            if (!_talkArmed) return false;

            _talkArmed = false;
            return true;
        }

        private bool VanIsNear(Ped man)
        {
            if (_van == null || !_van.Exists()) return false;
            return _van.Position.DistanceTo(man.Position) <= VanRange;
        }

        // ---- what they say -----------------------------------------------------

        /// <summary>
        /// The port, in two beats: who he is, then how fast he wants to be somewhere else.
        ///
        /// He is a Cheng. That means family money, a position he did not earn, and a register
        /// he is borrowing from people who did -- so the slang is a size too big on him and he
        /// keeps checking whether it landed. The joke is entirely on Tao: a rich kid playing
        /// at a thing the man he is talking to actually does, in a car park, at five o'clock,
        /// while the only genuine urgency he has all day is a bottle at home.
        ///
        /// He still says almost nothing about the business, and that part is real. The one
        /// fact you need is that it goes through him. Everything else is performance.
        /// </summary>
        private DialogueNode Meeting()
        {
            var node = new DialogueNode("Tao Cheng",
                "Ayy. You Gerald guy. Okay okay, I see you, I see you. So peep game, my dude: " +
                "NOTHING come off a boat in this yard unless I say it come off. Nothing. My " +
                "family own this -- like, the actual paperwork, bro, not some little " +
                "handshake thing. So don't be askin' me whose it is, don't ask me how often, " +
                "'cause that's not a question you ask a man in my position. You feel me? " +
                "Say you feel me.")
            {
                SpeakerColour = Palette.Cash
            };

            node.Say("I feel you.", Hurry, "Let him have it");
            node.WithIcon(Icons.FromFile("reply.png"));

            node.Say("...Sure.", Hurry, "Don't encourage him");
            node.WithIcon(Icons.Tick);

            return node;
        }

        /// <summary>And the half where the act stops holding and he just wants the bottle.</summary>
        private DialogueNode Hurry()
        {
            var node = new DialogueNode("Tao Cheng",
                "BET. Okay so -- you get a number, you call the number, thing show up, you pay " +
                "for the thing. Congratulations, you in the import business, my guy. Now go " +
                "round the back of the sheds, near bay one, back it in, nose out. My guys ain't " +
                "carryin' nothin' further than they gotta and they ain't gonna ASK you to " +
                "move, they just gonna stand there and hate you. And listen -- it's gone five. " +
                "I got a bottle at the crib older than you with my actual name on the label. " +
                "So park it correct and don't make this my whole evening. Please.")
            {
                SpeakerColour = Palette.Cash
            };

            node.Say("Bay one. Got it.", Take, "Reverse in near bay one, round the back");
            node.WithIcon(Icons.FromFile("box.png"));

            return node;
        }

        /// <summary>
        /// The box goes in the van and the number goes in the phone.
        ///
        /// The weight does NOT go in the stash. It is Gerald's quarter kilo off somebody
        /// else's boat and you are carrying it for him -- if it landed in your house you could
        /// cut it, bag it and stand on a corner with it, and there would be nothing left to
        /// deliver.
        /// </summary>
        private DialogueNode Take()
        {
            _state.DocksUnlocked = true;
            _state.PortRunStage = StageBay;
            _state.AddRespect(8f);
            _state.Touch();

            _saidHere = false;
            Reset();
            _bay = 0;
            _bayAt = 0;

            Notify.Important("~g~Round the back.~s~ Reverse in near bay one and wait.");
            Log.Info("Port run: docks unlocked, sent to the loading bay.");

            if (Social != null) Social.On(SocialEvent.PortRun, "Tao Cheng");

            return null;
        }

        /// <summary>
        /// The yard. He counts nothing, which is the compliment.
        ///
        /// And he does not make a speech. "The man at the port is yours now, use him" is a
        /// crime boss bestowing a territory in a film -- Gerald is a man in a yard in
        /// Chamberlain who has people for this and would like to go inside. What he actually
        /// did was vouch for somebody, which is a small thing that costs him if it goes wrong,
        /// and that is the only part he mentions.
        /// </summary>
        private DialogueNode HandOver()
        {
            var node = new DialogueNode("Gerald",
                "Aight. Leave it where it is, I got people for that. And quit standin' there " +
                "lookin' at me like I'm finna say somethin' -- it's done, that's the whole " +
                "thing. You got his number now so you call him your damn self, I ain't sittin' " +
                "in the middle of nobody's business. All I did was say you was solid. Don't " +
                "make me look stupid for it.")
            {
                SpeakerColour = Palette.Cash
            };

            node.Say("I got you.", Drop, "Hand over the truck");
            node.WithIcon(Icons.FromFile("box.png"));

            return node;
        }

        private DialogueNode Drop()
        {
            var pay = PayMin + _rng.Next(PayMax - PayMin);

            _state.PortRunStage = StageNone;
            _state.DocksUnlocked = true;
            _state.AddRespect(15f);
            _state.Touch();

            Game.Player.Money += pay;

            Notify.Important("~g~$" + pay.ToString("N0") + "~s~ for the run. The port's yours.");
            Log.Info("Port run finished: $" + pay + " paid, docks unlocked.");

            if (Social != null)
            {
                Social.On(SocialEvent.PortRunDone, "Gerald", pay);
                Social.PostAsYouSometimes("YouDidThePort", "", 4200, 70);
            }

            // Everything except him.
            _lingering = true;
            Pack();

            return null;
        }

        /// <summary>True while he is still stood in the yard after the errand has ended.</summary>
        private bool _lingering;

        /// <summary>
        /// Holds him there until you are out of sight, then lets him go.
        ///
        /// Released rather than deleted at the end of it -- MarkAsNoLongerNeeded hands him to
        /// the population manager, which reclaims him in its own time exactly as it does every
        /// other ped. Nothing pops.
        /// </summary>
        private void Linger()
        {
            if (_gerald == null || !_gerald.Exists())
            {
                _lingering = false;
                _gerald = null;
                return;
            }

            if (Game.GameTime < _next) return;
            _next = Game.GameTime + TickMs;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            if (player.Position.DistanceTo(_gerald.Position) <= StreamRange) return;

            try
            {
                _gerald.IsPersistent = false;
                _gerald.MarkAsNoLongerNeeded();
            }
            catch { /* he is somebody else's problem now */ }

            _gerald = null;
            _lingering = false;

            Log.Info("Port run: Gerald released once out of sight.");
        }

        // ---- the mark on the map -----------------------------------------------

        private void Route()
        {
            var wanted = Mark;

            if (_blip != null && _blip.Exists())
            {
                if (_blip.Position.DistanceTo(wanted) < 1f) return;

                // The run changed halves, so the mark moves rather than a second one appearing.
                try { _blip.Delete(); } catch { /* it is gone */ }
                _blip = null;
            }

            try
            {
                _blip = World.CreateBlip(wanted);
                if (_blip == null || !_blip.Exists()) return;

                // 596 is radar_nhp_wp2, which the delivery already uses for a hand-off, so the
                // trip out and the trip back wear the same mark.
                Function.Call(Hash.SET_BLIP_SPRITE, _blip.Handle, 596);
                Function.Call(Hash.SET_BLIP_COLOUR, _blip.Handle, 2);

                _blip.Name = Stage == StageDeliver ? "Drop it to Gerald"
                           : Stage == StageBay ? "Near bay one"
                           : "Meet the plug";
                _blip.ShowRoute = true;
                _blip.IsShortRange = false;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not mark the port run: " + ex.Message);
            }
        }

        /// <summary>The ring on the ground, drawn every frame while the run is on.</summary>
        public void Draw()
        {
            if (!Running) return;

            PollTalk();

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            Card();

            var where = Mark;
            if (player.Position.DistanceTo(where) > StreamRange) return;

            // Tighter at the yard than at the port, because one of them is a place to leave a
            // van and the other is a man stood in the middle of it.
            var radius = Stage == StageBay ? 3.6f : 5f;

            try
            {
                Function.Call(Hash.DRAW_MARKER, (int)MarkerType.Cylinder,
                              where.X, where.Y, where.Z - 0.9f,
                              0f, 0f, 0f, 0f, 0f, 0f,
                              radius, radius, 1.4f,
                              60, 200, 80, 90,
                              false, false, 2, false, 0, 0, false);
            }
            catch
            {
                // The blip still says where.
            }
        }

        // ---- the card at the top -----------------------------------------------

        private const float CardWidth = 0.300f;
        private const float CardTop = 0.052f;
        private const float CardHeight = 0.070f;
        private const float CardPad = 0.008f;
        private const float CardRail = 0.0022f;
        private const float IconSize = 0.034f;
        private const float BarHeight = 0.0045f;

        /// <summary>How fast the fill catches the figure, and how fast the light travels it.</summary>
        private const float BarRate = 0.12f;
        private const int BarSweepMs = 1600;

        /// <summary>How long the card takes to arrive, and how far it rises on the way.</summary>
        private const int EnterMs = 180;
        private const float EnterRise = 0.014f;

        private static readonly Color CardBack = Color.FromArgb(232, 12, 13, 15);

        /// <summary>Where the fill has got to, and which leg it belongs to.</summary>
        private float _bar;
        private int _barLeg = -1;

        /// <summary>The furthest away this leg has been, which is what the bar is measured against.</summary>
        private float _legFar;

        /// <summary>And the highest the fill has reached, so it cannot slide back down.</summary>
        private float _legBest;

        /// <summary>When the card first appeared, for the entrance.</summary>
        private int _cardAt;

        /// <summary>
        /// How far through the whole errand you are.
        ///
        /// Self-normalising rather than measured against a distance decided up front: the bar
        /// remembers the furthest this leg has been and reads the current distance against
        /// that. Starting the run stood next to the van and starting it from the other side of
        /// the map both fill the bar honestly, and nothing has to know how far the port is.
        ///
        /// Two legs, half the bar each, so collecting the package is visibly the middle of the
        /// job rather than the end of it.
        /// </summary>
        private float Progress()
        {
            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return 0f;

            var leg = Stage;

            if (leg != _barLeg)
            {
                _barLeg = leg;
                _legFar = 0f;
                _legBest = 0f;
            }

            var d = player.Position.DistanceTo(Mark);
            if (d > _legFar) _legFar = d;

            var done = _legFar < 1f ? 1f : 1f - d / _legFar;
            if (done < 0f) done = 0f;
            if (done > 1f) done = 1f;

            // Never backwards inside a leg.
            //
            // Dying is what exposed this. You come round outside a hospital four kilometres
            // from where you were, the furthest-seen distance jumps, and every previous
            // reading is suddenly worth less than it was -- so the bar slides back down the
            // card while you are reading the same sentence it had before you died. Getting
            // further from somewhere is not un-doing the drive you already made, so the fill
            // holds its high-water mark and only ever climbs.
            if (done > _legBest) _legBest = done;
            done = _legBest;

            // Three legs, a third of the bar each: find him, get loaded, get it home.
            if (leg == StageDeliver) return 0.667f + done * 0.333f;
            if (leg == StageBay) return 0.333f + done * 0.334f;

            return done * 0.333f;
        }

        /// <summary>
        /// The same card a job draws, in the same place, because it is the same kind of thing.
        ///
        /// It stands down while a mission is running rather than drawing over it. Two cards in
        /// one slot is not a layout problem to solve with an offset -- you can only be doing one
        /// of them at a time in any sense that matters, and the job is the one you chose.
        /// </summary>
        /// <summary>One full breath of the status tag.</summary>
        private const int TagPulseMs = 1800;

        /// <summary>Whether the player is sat in the truck this errand is about.</summary>
        private bool InTheTruck
        {
            get
            {
                var player = Game.Player.Character;
                if (player == null || !player.Exists() || !player.IsInVehicle()) return false;
                if (_van == null || !_van.Exists()) return false;

                return player.IsInVehicle(_van);
            }
        }

        /// <summary>
        /// What the card says you are doing, RIGHT NOW rather than this leg.
        ///
        /// One line per leg was true and unhelpful. "Meet the dock worker at Elysian Island" is
        /// correct from the moment he hands you the keys to the moment you walk up to Tao, and
        /// for most of that time the thing you actually have to do is something else -- get in
        /// the truck, drive it four kilometres, get out again. A card that says the same
        /// sentence for six minutes stops being read.
        ///
        /// So it follows the step. The leg is still the leg; what changes is which part of it
        /// is in front of you.
        /// </summary>
        private string StepLine()
        {
            var player = Game.Player.Character;
            var here = player != null && player.Exists() ? player.Position : Vector3.Zero;

            var mark = Mark;
            var close = here != Vector3.Zero && here.DistanceTo(mark) <= ParkRange;

            if (!InTheTruck)
            {
                // On foot at the far end is the arrival, not a missing truck.
                if (close)
                {
                    return Stage == StageDeliver ? "Hand it over to Gerald"
                         : Stage == StageBay ? "Wait here while they load it"
                         : "Go and talk to the dock worker";
                }

                var far = _van != null && _van.Exists() && here != Vector3.Zero
                          && here.DistanceTo(_van.Position) > 25f;

                return far ? "Get back to Gerald's truck" : "Get in Gerald's truck";
            }

            if (close)
            {
                return Stage == StageDeliver ? "Pull up on the mark and stop"
                     : Stage == StageBay ? "Back in near bay one, then sound the horn"
                     : "Park up and get out";
            }

            return Stage == StageDeliver ? "Drive his truck back to the yard in Chamberlain"
                 : Stage == StageBay ? "Round the back of the sheds, near bay one"
                 : "Take his truck out to Elysian Island";
        }

        /// <summary>The short status on the right of the card.</summary>
        private string StepTag()
        {
            if (Stage == StageDeliver) return Package.ToString("0") + "g";
            if (Stage == StageBay) return "BAY 1";

            return InTheTruck ? "TAO" : "TRUCK";
        }

        private void Card()
        {
            if (Busy != null && Busy()) return;

            // Not over a loading screen, a death, an arrest or a cutscene. The card is a thing
            // the game draws on top of the world, and during any of those there is no world
            // under it -- which is why it turned up sitting on a black screen on the way in and
            // again on the way back from the hospital.
            var who = Game.Player.Character;

            if (who == null || !who.Exists() || !who.IsAlive || Game.Player.IsDead) return;
            if (Game.IsLoading || Game.IsPaused) return;
            if (Function.Call<bool>(Hash.IS_PLAYER_BEING_ARRESTED, Game.Player.Handle, false)) return;

            if (_cardAt == 0) _cardAt = Game.GameTime;

            // Rises into place and fades up, eased out so it arrives rather than snaps. The
            // same entrance every other panel in the mod uses.
            var age = Game.GameTime - _cardAt;
            var enter = age >= EnterMs ? 1f : age / (float)EnterMs;
            var eased = 1f - (1f - enter) * (1f - enter);

            var top = CardTop + EnterRise * (1f - eased);
            var fade = eased;

            var left = 0.5f - CardWidth * 0.5f;
            var ink = Fade(Stage == StageFetch ? Palette.Standing : Palette.Cash, fade);

            Hud.RectFrom(left, top, CardWidth, CardHeight, Fade(CardBack, fade));
            Hud.RectFrom(left, top, CardRail, CardHeight, ink);
            Hud.RectFrom(left, top, CardWidth, 0.0022f, ink);

            var iconLeft = left + CardRail + CardPad;
            var iconWide = Hud.ToX(IconSize);

            Hud.RectFrom(iconLeft, top + (CardHeight - IconSize) * 0.5f,
                         iconWide, IconSize, Color.FromArgb((int)(20 * fade), 255, 255, 255));

            Hud.File(Stage == StageFetch ? "crate.png" : "box.png",
                     iconLeft + iconWide * 0.5f, top + CardHeight * 0.5f,
                     IconSize * 0.62f, 0f, ink);

            var x = iconLeft + iconWide + CardPad;

            Hud.Text("THE PORT RUN", x, top + 0.009f, 0.30f, Fade(Palette.Text, fade),
                     Hud.FontLabel, centre: false);

            Hud.Text(StepLine(), x, top + 0.030f, 0.26f, Fade(Palette.TextDim, fade),
                     Hud.FontBody, centre: false);

            // The tag breathes while the leg is live, and sits still once the step is done.
            //
            // It is the one part of the card that is a STATUS rather than an instruction, so
            // it is the part that should look like it is still running. A slow sine, the same
            // one the phone's wordmark uses -- enough to read as a thing in progress from the
            // corner of your eye, not enough to pull the eye off the road.
            var pulse = 0.72f + 0.28f * (float)Math.Sin(
                (Game.GameTime % TagPulseMs) / (double)TagPulseMs * Math.PI * 2d);

            Hud.TextRight(StepTag(),
                          left + CardWidth - CardPad, top + 0.031f, 0.23f,
                          Fade(ink, pulse), Hud.FontLabel);

            // ---- the bar ------------------------------------------------------
            //
            // Drawn even at zero so the card does not change height between legs. A readout
            // that reflows while you are reading it is worse than one showing an empty track.
            var barWide = CardWidth - (x - left) - CardPad;
            var barY = top + CardHeight - 0.010f;

            Hud.RectFrom(x, barY, barWide, BarHeight, Color.FromArgb((int)(40 * fade), 255, 255, 255));

            var done = Progress();

            // Snapped when the LEG changes, eased within one. Progress here is a distance, so
            // it arrives in steps as you drive and a bar set straight to it reads as something
            // being redrawn rather than something filling up. Easing across the leg boundary
            // would instead be a bar running backwards while you read the new sentence.
            if (_barLeg != _lastDrawnLeg)
            {
                _lastDrawnLeg = _barLeg;
                _bar = done;
            }

            _bar += (done - _bar) * BarRate;
            if (Math.Abs(done - _bar) < 0.002f) _bar = done;

            if (_bar <= 0f) return;

            Hud.RectFrom(x, barY, barWide * _bar, BarHeight, ink);

            // A light travelling up the FILLED part, so a bar that is not moving is still
            // visibly live. It stays inside the fill: a sheen running along the empty track
            // would be the card promising progress it has not made.
            var t = (Game.GameTime % BarSweepMs) / (float)BarSweepMs;
            var lit = barWide * _bar;
            var band = Math.Min(lit, barWide * 0.10f);
            var at = x - band + (lit + band) * t;

            var lo = Math.Max(x, at);
            var hi = Math.Min(x + lit, at + band);

            if (hi > lo)
            {
                Hud.RectFrom(lo, barY, hi - lo, BarHeight,
                             Color.FromArgb((int)(120 * fade), 255, 255, 255));
            }
        }

        /// <summary>Which leg the bar was last drawn for, so a change can snap it.</summary>
        private int _lastDrawnLeg = -1;

        private static Color Fade(Color c, float by)
        {
            if (by >= 0.999f) return c;
            return Color.FromArgb((int)(c.A * by), c.R, c.G, c.B);
        }

        // ---- who is stood there ------------------------------------------------

        private Ped Tao()
        {
            if (_car == null || !_car.Exists()) MakeCar();

            if (_tao != null && _tao.Exists()) return _tao;

            _tao = Stand(TaoModels, HisSpot, HisHeading, "Tao Cheng");
            return _tao;
        }

        private Ped Gerald()
        {
            if (_gerald != null && _gerald.Exists()) return _gerald;

            _gerald = Stand(GeraldModels, GeraldSpot, DropHeading, "Gerald");
            return _gerald;
        }

        /// <summary>
        /// The same spot, but on the floor.
        ///
        /// Tao was spawning in the sky and dropping a couple of metres. The coordinates are
        /// read off a player standing there, and a ped's position is not measured at the soles
        /// of their shoes -- so a height that is exactly right for a man standing on the dock
        /// is a height that puts a NEW man that far above it, and he falls the difference.
        ///
        /// The probe is trusted only when it AGREES with the authored height to within a
        /// couple of metres. That guard matters: a probe fired next to a container or under
        /// the overpass can come back with the roof of something, and silently relocating a
        /// man onto a shipping container is a worse bug than the one being fixed.
        /// </summary>
        private static Vector3 Standing(Vector3 where)
        {
            try
            {
                if (World.GetGroundHeight(new Vector3(where.X, where.Y, where.Z + 2f),
                                          out var floor, GetGroundHeightMode.Normal)
                    && floor > 0f && Math.Abs(floor - where.Z) <= 2.5f)
                {
                    return new Vector3(where.X, where.Y, floor);
                }
            }
            catch
            {
                // Unstreamed ground. The authored height is the better guess.
            }

            return where;
        }

        private Ped Stand(string[] models, Vector3 where, float heading, string name)
        {
            foreach (var modelName in models)
            {
                try
                {
                    var model = new Model(modelName);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) continue;

                    var ped = World.CreatePed(model, Standing(where), heading);
                    model.MarkAsNoLongerNeeded();

                    if (ped == null || !ped.Exists()) continue;

                    var h = ped.Handle;

                    ped.IsPersistent = true;
                    ped.BlockPermanentEvents = true;

                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, h, true, true);
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, h, true);
                    Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, h, false);

                    // Neither of them can be killed, for the same reason the leaders cannot: a
                    // stray round from a fight two streets away would take the only way into the
                    // rest of the supply chain off the map with nothing to say why.
                    Function.Call(Hash.SET_ENTITY_INVINCIBLE, h, true);
                    Function.Call(Hash.SET_PED_DIES_WHEN_INJURED, h, false);
                    Function.Call(Hash.SET_PED_SUFFERS_CRITICAL_HITS, h, false);
                    Function.Call(Hash.SET_PED_CAN_RAGDOLL, h, false);

                    // Stood about waiting, which is what both of them are doing.
                    Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, h,
                                  "WORLD_HUMAN_STAND_MOBILE", 0, true);

                    ped.Heading = heading;

                    // Belt and braces on the drop: the ped is put on the floor a second time
                    // once he exists, because the ground probe is more reliable against a
                    // streamed-in world than against one that is still arriving.
                    try
                    {
                        var settled = Standing(ped.Position);
                        if (Math.Abs(settled.Z - ped.Position.Z) > 0.05f) ped.Position = settled;
                    }
                    catch { /* he will fall the last few inches */ }

                    // Attached to him, so deleting him takes it with him -- there is no
                    // handle to hold on to and no way to leave one behind on an empty pavement.
                    var blip = ped.AddBlip();

                    if (blip != null && blip.Exists())
                    {
                        Function.Call(Hash.SET_BLIP_SPRITE, blip.Handle, 596);
                        blip.Color = BlipColor.Green;
                        blip.Scale = 0.8f;
                        blip.Name = name;
                    }

                    Log.Info(name + " is waiting for the port run.");
                    return ped;
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not place " + name + ": " + ex.Message);
                }
            }

            return null;
        }

        // ---- the van -----------------------------------------------------------

        /// <summary>
        /// Keeps his van in the world for the whole run.
        ///
        /// Unlike everything else here it is NOT streamed by distance: it is parked up a
        /// street in Chamberlain the moment he sends you, it is marked on the map, and it has
        /// to still be wherever you left it when you walk back to it. Persistent, so the
        /// population manager cannot quietly reclaim it while you are at the port.
        /// </summary>
        private void KeepVan()
        {
            if (_van != null && _van.Exists()) { MarkVan(); return; }

            _van = null;

            // Only put a replacement back when you are nowhere near where it lived -- a fresh
            // van fading in ten metres away because the old one is a burnt shell is worse than
            // the burnt shell.
            var player = Game.Player.Character;

            if (player != null && player.Exists() &&
                player.Position.DistanceTo(VanSpot) < StreamRange) return;

            MakeVan();
        }

        private void MakeVan()
        {
            if (_van != null && _van.Exists()) return;

            foreach (var name in VanModels)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) continue;

                    _van = World.CreateVehicle(model, VanSpot, VanHeading);
                    model.MarkAsNoLongerNeeded();

                    if (_van == null || !_van.Exists()) continue;

                    var h = _van.Handle;

                    _van.IsPersistent = true;
                    _van.Position = VanSpot;
                    _van.Heading = VanHeading;

                    Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, h);

                    // Has to come first: nothing in the mod system answers honestly until the
                    // kit is on.
                    Function.Call(Hash.SET_VEHICLE_MOD_KIT, h, 0);

                    // Strip the branding before the paint goes on, and find out whether it
                    // actually came off. A model whose every livery is a wrap gets put back
                    // and the next one tried -- unless it is the last one, in which case a
                    // branded van beats no van.
                    var clean = Blank(h, name);

                    if (!clean && name != VanModels[VanModels.Length - 1])
                    {
                        Log.Info("Van " + name + " has no unbranded livery; trying the next one.");

                        try { _van.Delete(); } catch { /* gone */ }
                        _van = null;
                        continue;
                    }

                    // The index into the game's own paint table, not an RGB. The RGB call gives
                    // a flat poster green; the index gives the metallic flake, which is the
                    // difference between a painted van and a coloured shape.
                    Function.Call(Hash.SET_VEHICLE_COLOURS, h, VanGreen, VanGreen);
                    Function.Call(Hash.SET_VEHICLE_EXTRA_COLOURS, h, VanGreen, 0);

                    // Filthy. It has been up that kerb a long time.
                    Function.Call(Hash.SET_VEHICLE_DIRT_LEVEL, h, VanDirt);

                    Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, h, 1);
                    Function.Call(Hash.SET_VEHICLE_NEEDS_TO_BE_HOTWIRED, h, false);
                    Function.Call(Hash.SET_VEHICLE_HAS_BEEN_OWNED_BY_PLAYER, h, true);

                    MarkVan();

                    Log.Info("Gerald's van is parked up for the port run.");
                    return;
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not park Gerald's van: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Takes every wrap, decal and bolt-on off a vehicle.
        ///
        /// The van turned up as a WEAZEL NEWS van, in our green, with the branding still on
        /// top of it -- so the paint was landing and the livery was surviving. The reason is
        /// that GTA has THREE separate systems for putting artwork on a vehicle and this only
        /// cleared one and a half of them:
        ///
        ///   SET_VEHICLE_LIVERY     the old per-model livery
        ///   SET_VEHICLE_LIVERY2    a second layer some vehicles carry on top of the first
        ///   SET_VEHICLE_MOD(48)    the mod-kit livery slot, which is where anything with a
        ///                          modkit actually keeps its wraps -- and rumpo has one,
        ///                          "81_rumpo_modkit", confirmed in the game's vehicle dump
        ///
        /// plus EXTRAS, which are physical bolt-on parts rather than textures and are how
        /// several vans carry signage. The rumpo has none, but the fallback vans do.
        ///
        /// Every one is cleared, and -1 is not trusted to mean "none": the value is read back,
        /// and anything still showing is forced to index 0. What each system reported goes in
        /// the log, because the only way to find out from outside the game which one was
        /// holding the branding is to have written the numbers down.
        /// </summary>
        private static bool Blank(int h, string what)
        {
            var told = "";
            var clean = true;

            try
            {
                var count = Function.Call<int>(Hash.GET_VEHICLE_LIVERY_COUNT, h);

                if (count > 0)
                {
                    Function.Call(Hash.SET_VEHICLE_LIVERY, h, -1);

                    if (Function.Call<int>(Hash.GET_VEHICLE_LIVERY, h) >= 0)
                    {
                        // -1 will not take, so this model has no "none" -- every index it
                        // owns is somebody's branding, and forcing 0 is how the van ended up
                        // in full Weazel white. Nothing here can save it; say so and let the
                        // caller try a different van.
                        clean = false;

                        for (var i = 0; i < count; i++)
                        {
                            told += " [" + i + "=" +
                                    Function.Call<string>(Hash.GET_LIVERY_NAME, h, i) + "]";
                        }
                    }
                }

                told += " livery " + count + "->" + Function.Call<int>(Hash.GET_VEHICLE_LIVERY, h);
            }
            catch { told += " livery n/a"; }

            try
            {
                var count = Function.Call<int>(Hash.GET_VEHICLE_LIVERY2_COUNT, h);

                if (count > 0)
                {
                    Function.Call(Hash.SET_VEHICLE_LIVERY2, h, -1);

                    if (Function.Call<int>(Hash.GET_VEHICLE_LIVERY2, h) >= 0)
                    {
                        Function.Call(Hash.SET_VEHICLE_LIVERY2, h, 0);
                    }
                }

                told += ", livery2 " + count + "->" + Function.Call<int>(Hash.GET_VEHICLE_LIVERY2, h);
            }
            catch { told += ", livery2 n/a"; }

            try
            {
                // 48 is the mod-kit livery slot.
                var mods = Function.Call<int>(Hash.GET_NUM_VEHICLE_MODS, h, 48);

                if (mods > 0)
                {
                    Function.Call(Hash.SET_VEHICLE_MOD, h, 48, -1, false);

                    if (Function.Call<int>(Hash.GET_VEHICLE_MOD, h, 48) >= 0)
                    {
                        Function.Call(Hash.SET_VEHICLE_MOD, h, 48, 0, false);
                    }
                }

                told += ", mod48 " + mods + "->" + Function.Call<int>(Hash.GET_VEHICLE_MOD, h, 48);
            }
            catch { told += ", mod48 n/a"; }

            // And the bolt-ons. Toggle 1 turns an extra OFF, which reads backwards and is
            // genuinely how the native works.
            var off = 0;

            for (var extra = 1; extra <= 14; extra++)
            {
                try
                {
                    if (!Function.Call<bool>(Hash.DOES_EXTRA_EXIST, h, extra)) continue;

                    Function.Call(Hash.SET_VEHICLE_EXTRA, h, extra, 1);
                    off++;
                }
                catch
                {
                    // Next one.
                }
            }

            Log.Info("Van " + what + " stripped:" + told + ", " + off + " extras off, " +
                     (clean ? "clean." : "STILL BRANDED."));

            return clean;
        }

        private void MarkVan()
        {
            if (_vanBlip != null && _vanBlip.Exists()) return;
            if (_van == null || !_van.Exists()) return;

            try
            {
                _vanBlip = _van.AddBlip();
                if (_vanBlip == null || !_vanBlip.Exists()) return;

                // 225 is radar_gang_vehicle, which is exactly what it is.
                Function.Call(Hash.SET_BLIP_SPRITE, _vanBlip.Handle, 225);
                _vanBlip.Color = BlipColor.Green;
                _vanBlip.Scale = 0.8f;
                _vanBlip.Name = "Gerald's truck";
            }
            catch (Exception ex)
            {
                Log.Debug("Could not mark Gerald's van: " + ex.Message);
            }
        }

        private void MakeCar()
        {
            foreach (var name in CarModels)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) continue;

                    _car = World.CreateVehicle(model, CarSpot, CarHeading);
                    model.MarkAsNoLongerNeeded();

                    if (_car == null || !_car.Exists()) continue;

                    _car.IsPersistent = true;
                    _car.Position = CarSpot;
                    _car.Heading = CarHeading;

                    Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, _car.Handle);
                    Function.Call(Hash.SET_VEHICLE_MOD_KIT, _car.Handle, 0);
                    Function.Call(Hash.SET_VEHICLE_LIVERY, _car.Handle, -1);
                    Function.Call(Hash.SET_VEHICLE_MOD, _car.Handle, 48, -1, false);
                    Function.Call(Hash.SET_VEHICLE_COLOURS, _car.Handle, Black, Black);
                    Function.Call(Hash.SET_VEHICLE_WINDOW_TINT, _car.Handle, 1);
                    Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, _car.Handle, 2);

                    // His plate. Eight characters is the limit and this is exactly eight.
                    Function.Call(Hash.SET_VEHICLE_NUMBER_PLATE_TEXT, _car.Handle, "HOODRICH");

                    // Boot SHUT. It stood open on the reasoning that he had just unloaded out
                    // of it, which was true for about four seconds and then was a man having a
                    // conversation next to his own open boot for the rest of the errand -- and
                    // the load does not come out of his car anyway, it comes off the dock.
                    Function.Call(Hash.SET_VEHICLE_DOOR_SHUT, _car.Handle, 5, true);

                    Log.Info("The plug's car is parked at the port.");
                    return;
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not park the plug's car: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// How far to lift a prop so its underside lands on the bed rather than through it.
        ///
        /// GET_MODEL_DIMENSIONS gives the model's own bounding box in its own space. A prop
        /// pivoted at its base reports a minimum Z of about zero and needs no lift at all; one
        /// that straddles its middle reports a negative minimum and has to come up by that
        /// much. Returns zero on anything it cannot measure, which is the old behaviour.
        /// </summary>
        private static float Underside(Model model)
        {
            try
            {
                var lo = new OutputArgument();
                var hi = new OutputArgument();

                Function.Call(Hash.GET_MODEL_DIMENSIONS, model.Hash, lo, hi);

                var min = lo.GetResult<Vector3>();
                return min.Z < 0f ? -min.Z : 0f;
            }
            catch
            {
                return 0f;
            }
        }

        // ---- taking it away ----------------------------------------------------

        // ---- the ride home ------------------------------------------------------

        /// <summary>
        /// Two Vagos who know what is in the van.
        ///
        /// The delivery leg is a four-kilometre drive with nothing in it, and a quarter kilo
        /// riding in the back is exactly the kind of thing somebody would try to take. So they
        /// are sat on the road up out of the docks with the engine off, and they pull out
        /// behind you as you go past.
        ///
        /// Deliberately survivable rather than scripted: they can be shot, rammed, lost or
        /// simply outdriven, and none of those outcomes fails anything. The load cannot be
        /// taken off you -- what they cost you is paint, time and a wanted level's worth of
        /// noise on a job you were supposed to do quietly.
        /// </summary>
        private void Ambush(Ped player)
        {
            switch (_ambush)
            {
                case 0: PlaceAmbush(player); return;
                case 1: WakeAmbush(player); return;
                case 2: RunAmbush(); return;
                default: return;
            }
        }

        private void PlaceAmbush(Ped player)
        {
            if (player.Position.DistanceTo(AmbushSpot) > AmbushPlace) return;

            if (!MakeHunters())
            {
                // Nothing would spawn, so the road home is quiet. Better than a half-built
                // ambush that follows you with one man in it.
                _ambush = 3;
                return;
            }

            _ambush = 1;
            Log.Info("Port run: the Vagos are sat waiting on the dock road.");
        }

        private void WakeAmbush(Ped player)
        {
            if (_hunters == null || !_hunters.Exists() || !_hunters.IsDriveable)
            {
                _ambush = 3;
                return;
            }

            if (player.Position.DistanceTo(_hunters.Position) > AmbushWake) return;

            _ambush = 2;

            try
            {
                _hunters.IsEngineRunning = true;

                for (var i = 0; i < _vagos.Count; i++)
                {
                    var ped = _vagos[i];
                    if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

                    Function.Call(Hash.SET_PED_AS_ENEMY, ped.Handle, true);
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);

                    // 2 is can-do-drivebys, 3 is can-leave-vehicle. They shoot from the car and
                    // they stay in it -- the brief is running you off the road, not a shootout
                    // on the hard shoulder.
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 2, true);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 3, false);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 5, true);

                    if (i == 0)
                    {
                        Function.Call(Hash.SET_DRIVER_ABILITY, ped.Handle, 1f);
                        Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, ped.Handle, 1f);

                        Function.Call(Hash.TASK_VEHICLE_CHASE, ped.Handle, player.Handle);

                        // Right up on the back of the van. The default keeps a respectful
                        // distance, which is a car following you rather than one trying to put
                        // you into a wall.
                        Function.Call(Hash.SET_TASK_VEHICLE_CHASE_IDEAL_PURSUIT_DISTANCE,
                                      ped.Handle, 4f);
                    }
                    else
                    {
                        Function.Call(Hash.TASK_DRIVE_BY, ped.Handle, player.Handle, 0,
                                      0f, 0f, 0f, 45f, 60, true, unchecked((int)0xC6EE6B4C));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("The Vagos could not get going: " + ex.Message);
            }

            MarkHunters();

            Notify.Important("~r~Yellow on your tail.~s~ Get that truck home.");

            if (Social != null) Social.On(SocialEvent.RideThrough, "Los Santos Vagos");
        }

        private void RunAmbush()
        {
            // Nothing left of them, or nothing left driveable.
            var alive = 0;

            foreach (var ped in _vagos)
            {
                if (ped != null && ped.Exists() && ped.IsAlive) alive++;
            }

            var wrecked = _hunters == null || !_hunters.Exists() || !_hunters.IsDriveable;

            if (alive == 0 || wrecked)
            {
                Log.Info("Port run: the Vagos are done.");
                LetGo(false);
                return;
            }

            // Close enough to the yard. They are not driving into Chamberlain.
            if (_hunters.Position.DistanceTo(DropSpot) <= AmbushLetGo)
            {
                Log.Info("Port run: the Vagos broke off short of the yard.");
                LetGo(true);
            }
        }

        /// <summary>
        /// Ends the chase.
        ///
        /// The car and the men are RELEASED rather than deleted. Deleting a car you are
        /// currently looking at in the mirror is worse than any tidiness it buys, and a wreck
        /// with two dead men in it is a thing that happened -- it should still be there if you
        /// drive back past.
        /// </summary>
        private void LetGo(bool peeled)
        {
            _ambush = 3;

            foreach (var ped in _vagos)
            {
                try
                {
                    if (ped == null || !ped.Exists()) continue;

                    if (ped.IsAlive && peeled)
                    {
                        Function.Call(Hash.SET_PED_AS_ENEMY, ped.Handle, false);
                        ped.Task.ClearAll();
                    }

                    ped.IsPersistent = false;
                    ped.MarkAsNoLongerNeeded();
                }
                catch { /* they are somebody else's problem now */ }
            }

            _vagos.Clear();

            try
            {
                if (_hunters != null && _hunters.Exists())
                {
                    _hunters.IsPersistent = false;
                    _hunters.MarkAsNoLongerNeeded();
                }
            }
            catch { /* teardown */ }

            _hunters = null;

            try { if (_huntBlip != null && _huntBlip.Exists()) _huntBlip.Delete(); }
            catch { /* teardown */ }

            _huntBlip = null;

            if (peeled) Notify.Ticker("~g~They peeled off.~s~ Too close to the block for 'em.");
        }

        private bool MakeHunters()
        {
            foreach (var name in AmbushCars)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) continue;

                    _hunters = World.CreateVehicle(model, AmbushSpot, AmbushHeading);
                    model.MarkAsNoLongerNeeded();

                    if (_hunters == null || !_hunters.Exists()) continue;

                    var h = _hunters.Handle;

                    _hunters.IsPersistent = true;
                    Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, h);
                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, h, true, true);

                    // Their colour, out of the game's own table so it takes the flake.
                    Function.Call(Hash.SET_VEHICLE_MOD_KIT, h, 0);
                    Function.Call(Hash.SET_VEHICLE_COLOURS, h, VagosPaint, VagosPaint);
                    Function.Call(Hash.SET_VEHICLE_WINDOW_TINT, h, 1);
                    Function.Call(Hash.SET_VEHICLE_DIRT_LEVEL, h, 2f);

                    // And the money they spent on it. Slots 11 engine, 12 brakes, 13 gearbox,
                    // 18 turbo -- the difference between a lowrider and a lowrider that can
                    // stay behind a van doing eighty.
                    Tune(h, 11);
                    Tune(h, 12);
                    Tune(h, 13);

                    Function.Call(Hash.TOGGLE_VEHICLE_MOD, h, 18, true);
                    Function.Call(Hash.MODIFY_VEHICLE_TOP_SPEED, h, 1.25f);

                    _hunters.IsEngineRunning = false;

                    if (SeatThem()) return true;

                    // A car with nobody in it is not an ambush.
                    try { _hunters.Delete(); } catch { /* gone */ }
                    _hunters = null;
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not park the Vagos: " + ex.Message);
                }
            }

            return false;
        }

        private static void Tune(int veh, int slot)
        {
            try
            {
                var top = Function.Call<int>(Hash.GET_NUM_VEHICLE_MODS, veh, slot) - 1;
                if (top >= 0) Function.Call(Hash.SET_VEHICLE_MOD, veh, slot, top, false);
            }
            catch
            {
                // It goes slower. It still goes.
            }
        }

        private bool SeatThem()
        {
            for (var seat = 0; seat < AmbushCrew; seat++)
            {
                var ped = SeatOne(seat == 0 ? -1 : 0);
                if (ped != null) _vagos.Add(ped);
            }

            return _vagos.Count > 0;
        }

        private Ped SeatOne(int seat)
        {
            foreach (var name in VagosModels)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) continue;

                    var ped = Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE,
                                                 _hunters.Handle, 4, model.Hash, seat, true, false);

                    model.MarkAsNoLongerNeeded();
                    if (ped == 0) continue;

                    var who = Entity.FromHandle(ped) as Ped;
                    if (who == null || !who.Exists()) continue;

                    who.IsPersistent = true;
                    who.BlockPermanentEvents = true;

                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, ped, true, true);
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped, true);
                    Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped,
                                  Game.GenerateHash("AMBIENT_GANG_MEXICAN"));

                    Function.Call(Hash.GIVE_WEAPON_TO_PED, ped,
                                  Game.GenerateHash(VagosGun), 400, false, true);

                    Function.Call(Hash.SET_PED_ACCURACY, ped, 28);
                    Function.Call(Hash.SET_PED_ARMOUR, ped, 25);

                    return who;
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not seat a Vago: " + ex.Message);
                }
            }

            return null;
        }

        private void MarkHunters()
        {
            if (_huntBlip != null && _huntBlip.Exists()) return;
            if (_hunters == null || !_hunters.Exists()) return;

            try
            {
                _huntBlip = _hunters.AddBlip();
                if (_huntBlip == null || !_huntBlip.Exists()) return;

                // 225 is radar_gang_vehicle, which is what it is.
                Function.Call(Hash.SET_BLIP_SPRITE, _huntBlip.Handle, 225);
                _huntBlip.Color = BlipColor.Red;
                _huntBlip.Scale = 0.9f;
                _huntBlip.Name = "Vagos";
            }
            catch (Exception ex)
            {
                Log.Debug("Could not blip the Vagos: " + ex.Message);
            }
        }

        // ---- the loading bay ---------------------------------------------------

        /// <summary>
        /// Backing into bay one and being loaded.
        ///
        /// The one part of the errand you can get WRONG, and deliberately the mildest kind of
        /// wrong: nothing fails, nothing is lost, the men simply do not come out until the van
        /// is where it is supposed to be. Position and heading both, because "reverse in" is
        /// the instruction and a van nosed into the bay is not reversed into it -- and stopped,
        /// because being dragged past the mark at forty is not parking.
        /// </summary>
        private void Loading(Ped player)
        {
            switch (_bay)
            {
                case 0: WaitingForTheVan(player); return;
                case 1: Walking(); return;
                case 2: Settling(); return;
                default: return;
            }
        }

        /// <summary>
        /// What is wrong with the park, in the order somebody would notice it.
        ///
        /// One reason at a time. "It is not right" is a hint; "you are facing the wrong way"
        /// is an instruction, and there is no version of this where telling somebody all three
        /// possible faults at once helps them fix the one they have.
        /// </summary>
        private string WhyNot()
        {
            if (_van == null || !_van.Exists()) return "where's the truck?";

            if (_van.Position.DistanceTo(BaySpot) > BayRange) return "you ain't in the bay.";
            if (_van.Speed > BayStopped) return "stop the truck first.";

            return "turn it round -- back in, nose out.";
        }

        /// <summary>Is the van in the bay, square to it, and stood still.</summary>
        private bool Parked()
        {
            if (_van == null || !_van.Exists()) return false;
            if (_van.Position.DistanceTo(BaySpot) > BayRange) return false;
            if (_van.Speed > BayStopped) return false;

            // Wrapped, so 359 and 1 are two degrees apart rather than three hundred and fifty.
            var off = Math.Abs(_van.Heading - BayHeading) % 360f;
            if (off > 180f) off = 360f - off;

            return off <= BaySquare;
        }

        /// <summary>Was the horn down last time we looked, so one press is one press.</summary>
        private bool _hornWasDown;

        /// <summary>
        /// Backing in, and telling them you are in.
        ///
        /// It used to fire the moment the van happened to be square, which meant the sequence
        /// started while you were still shuffling back and forth and you never knew what the
        /// deciding moment was. A horn is the deciding moment: you park it, you beep, and it
        /// tells you either that they are coming or exactly what is wrong. It is also what
        /// anybody reversing into a loading bay actually does.
        /// </summary>
        private void WaitingForTheVan(Ped player)
        {
            if (player.Position.DistanceTo(BaySpot) > StreamRange) return;
            if (_van == null || !_van.Exists()) return;

            var driving = player.IsInVehicle(_van);

            if (driving)
            {
                Help.ShowThisFrame(Parked()
                    ? "Press ~INPUT_VEH_HORN~ to let them know you're in."
                    : "Back in near bay one -- nose out. Then sound the horn.");
            }

            // Edge, not level. A horn held down is one beep, not forty.
            var down = false;

            try { down = Function.Call<bool>(Hash.IS_HORN_ACTIVE, _van.Handle); }
            catch { /* no horn, no answer */ }

            var beeped = down && !_hornWasDown;
            _hornWasDown = down;

            if (!beeped || !driving) return;

            if (!Parked())
            {
                Notify.Problem(WhyNot());
                return;
            }

            _bay = 1;
            _bayAt = Game.GameTime;

            Notify.Text("CHAR_DEFAULT", "Tao Cheng", "Elysian Island",
                        "guys are coming out to you now. dont get out, dont help, dont talk to them");

            Log.Info("Port run: van parked in bay one, loaders sent.");

            MakeLoaders();
        }

        private void Walking()
        {
            var arrived = 0;
            var alive = 0;

            foreach (var ped in _loaders)
            {
                if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

                alive++;
                if (ped.Position.DistanceTo(LoaderTo) <= LoaderArrive) arrived++;
            }

            // Nobody left to wait for, or they have taken long enough that something has them
            // stuck on a pallet. Either way the job does not hang on it.
            var late = Game.GameTime - _bayAt > LoaderGiveUpMs;

            if (alive == 0 || late || arrived >= alive)
            {
                if (late) Log.Warn("Port run: loaders never arrived; loading anyway.");

                _bay = 2;
                _bayAt = Game.GameTime;

                LoadTheVan();

                foreach (var ped in _loaders) Face(ped, LoaderToHeading);
            }
        }

        private void Settling()
        {
            if (Game.GameTime - _bayAt < LoaderSettleMs) return;

            // Said in Korean, by men whose own voice is Korean. The English underneath is what
            // a subtitle is for -- and it is the half that still works if this install's font
            // has no Hangul in it.
            GTA.UI.Screen.ShowSubtitle("~s~\uAC00\uB3C4 \uB429\uB2C8\uB2E4.~n~~c~(You can go now.)", 4000);

            foreach (var ped in _loaders)
            {
                if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

                try
                {
                    Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, ped.Handle,
                                  "GENERIC_BYE", "SPEECH_PARAMS_FORCE");
                }
                catch { /* the subtitle carries it */ }

                // The boxes are in the van, so they are not still holding boxes. Clearing the
                // anim before the wander matters: a secondary upper-body loop survives a
                // movement task by design, so without this they walk off down the dock still
                // carrying something that is already in the back of the van.
                try
                {
                    Function.Call(Hash.STOP_ANIM_TASK, ped.Handle, CarryDict, CarryClip, -4f);
                    Function.Call(Hash.CLEAR_PED_SECONDARY_TASK, ped.Handle);
                    ped.Task.ClearAll();
                }
                catch { /* he will drop it eventually */ }

                try
                {
                    ped.BlockPermanentEvents = false;
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);
                    Function.Call(Hash.TASK_WANDER_STANDARD, ped.Handle, 10f, 10);
                    ped.MarkAsNoLongerNeeded();
                }
                catch { /* he will stand there */ }
            }

            _loaders.Clear();

            _bay = 3;
            _state.PortRunStage = StageDeliver;
            _state.Touch();

            _saidHere = false;
            Reset();

            Notify.Important("~g~Loaded.~s~ Get it back to Gerald.");
            Log.Info("Port run: van loaded, heading for the yard.");
        }

        /// <summary>The box-carry set. Three clips, and all three are real.</summary>
        private const string CarryDict = "anim@heists@box_carry@";
        private const string CarryClip = "idle";

        /// <summary>
        /// Loop, hold, upper body, secondary.
        ///
        /// The secondary bit is the one that matters: it puts the clip in the secondary task
        /// slot so the walk task in the primary slot keeps running underneath. Without it the
        /// animation replaces the walk and two men stand in a shed holding boxes forever.
        /// </summary>
        private const int CarryFlags = 51;

        private void MakeLoaders()
        {
            var from = Standing(LoaderFrom);
            var to = Standing(LoaderTo);

            // Side by side rather than inside each other, along the line they are walking.
            var side = new Vector3((float)Math.Cos(LoaderToHeading * Math.PI / 180d),
                                   (float)Math.Sin(LoaderToHeading * Math.PI / 180d), 0f);

            for (var i = 0; i < LoaderCount; i++)
            {
                var shift = (i - (LoaderCount - 1) * 0.5f) * LoaderGap;

                var start = from + side * shift;
                var end = to + side * shift;

                var ped = MakeLoader(start, end);
                if (ped != null) _loaders.Add(ped);
            }

            if (_loaders.Count == 0) Log.Warn("Port run: no loader model would spawn.");
        }

        private Ped MakeLoader(Vector3 from, Vector3 to)
        {
            foreach (var name in LoaderModels)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) continue;

                    var ped = World.CreatePed(model, from, LoaderFromHeading);
                    model.MarkAsNoLongerNeeded();

                    if (ped == null || !ped.Exists()) continue;

                    var h = ped.Handle;

                    ped.IsPersistent = true;
                    ped.BlockPermanentEvents = true;

                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, h, true, true);
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, h, true);
                    Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, h, false);
                    Function.Call(Hash.SET_PED_CAN_RAGDOLL, h, false);

                    // The walk goes in FIRST and the box goes on top of it. The other way round
                    // and the walk task replaces the animation instead of running under it.
                    Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, h,
                                  to.X, to.Y, to.Z, 1f, LoaderGiveUpMs, 0.5f, false, 0f);

                    Carry(ped);

                    return ped;
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not put a loader on the dock: " + ex.Message);
                }
            }

            return null;
        }

        private static void Carry(Ped ped)
        {
            if (ped == null || !ped.Exists()) return;

            try
            {
                if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, CarryDict))
                {
                    Function.Call(Hash.REQUEST_ANIM_DICT, CarryDict);
                }

                // The last three are bPhaseControlled, IkFlags and a multiplayer flag -- NOT
                // position locks, whatever the community header says.
                Function.Call(Hash.TASK_PLAY_ANIM, ped.Handle, CarryDict, CarryClip,
                              4f, -4f, -1, CarryFlags, 0f, false, 0, false);
            }
            catch (Exception ex)
            {
                Log.Debug("No box for the loader: " + ex.Message);
            }
        }

        private static void Face(Ped ped, float heading)
        {
            if (ped == null || !ped.Exists() || !ped.IsAlive) return;

            try { ped.Heading = heading; }
            catch { /* he can stand how he likes */ }
        }

        /// <summary>
        /// Puts the load in the back of the van, so the errand has a physical object in it.
        ///
        /// Attached rather than placed, so it rides with the van across the city instead of
        /// being left standing in the bay the moment you pull away. Collision off: a crate that
        /// can be shoved about inside a moving van will find its way through a door.
        /// </summary>
        private void LoadTheVan()
        {
            if (_crate != null && _crate.Exists()) return;
            if (_van == null || !_van.Exists()) return;

            foreach (var name in CrateModels)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1200)) continue;

                    _crate = World.CreateProp(model, _van.Position, false, false);
                    model.MarkAsNoLongerNeeded();

                    if (_crate == null || !_crate.Exists()) continue;

                    _crate.IsPersistent = true;

                    // MEASURED, not guessed.
                    //
                    // The old offset was a single number tuned against a cardboard box, and it
                    // only worked because everything in the list was about that size. A pallet
                    // is not, and props do not agree about where their own origin sits -- some
                    // are pivoted at the base and some straddle their middle -- so one height
                    // for all of them puts half the list through the bed floor and the other
                    // half hovering above it.
                    //
                    // So the model's own box is read and its underside is placed on the bed
                    // floor. Whatever wins the list sits on the deck properly.
                    Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, _crate.Handle, _van.Handle, -1,
                                  0f, CrateY, BedFloorZ + Underside(model), 0f, 0f, 0f,
                                  false, false, false, false, 2, true);

                    Log.Info("Port run: " + name + " loaded into the van.");
                    return;
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not load the van: " + ex.Message);
                }
            }
        }

        private void ClearBay()
        {
            foreach (var ped in _loaders)
            {
                try { if (ped != null && ped.Exists()) ped.Delete(); }
                catch { /* gone */ }
            }

            _loaders.Clear();
        }

        /// <summary>Takes the port away but leaves the run, and the van, alone.</summary>
        private void ClearPort()
        {
            if (_tao == null && _car == null) return;

            Sweep(ref _tao);

            try
            {
                if (_car != null && _car.Exists())
                {
                    _car.IsPersistent = false;
                    _car.Delete();
                }
            }
            catch { /* it is gone */ }

            _car = null;
        }

        /// <summary>And the same for the yard.</summary>
        private void ClearDrop()
        {
            if (_gerald == null) return;

            // Not while he is being left to walk off on his own. See Linger.
            if (_lingering) return;

            Sweep(ref _gerald);
        }

        private void Clear()
        {
            ClearPort();
            ClearDrop();
        }

        private static void Sweep(ref Ped ped)
        {
            try { if (ped != null && ped.Exists()) ped.Delete(); }
            catch { /* he is gone */ }

            ped = null;
        }

        /// <summary>
        /// And this takes the run away with it.
        ///
        /// The van is RELEASED rather than deleted. It is his and it is now parked in his yard
        /// with a quarter kilo out of the back of it -- making it vanish the second he says
        /// thanks would undo the only physical thing in the whole errand. Dropping persistence
        /// lets the game clean it up in its own time, the way it does every other car.
        /// </summary>
        public void Pack()
        {
            Clear();
            ClearBay();

            try { if (_crate != null && _crate.Exists()) _crate.Delete(); }
            catch { /* teardown */ }

            _crate = null;
            _bay = 0;
            _bayAt = 0;
            _hornWasDown = false;

            if (_ambush != 0) LetGo(false);
            _ambush = 0;

            try { if (_blip != null && _blip.Exists()) _blip.Delete(); }
            catch { /* teardown */ }

            try { if (_vanBlip != null && _vanBlip.Exists()) _vanBlip.Delete(); }
            catch { /* teardown */ }

            try { if (_van != null && _van.Exists()) _van.IsPersistent = false; }
            catch { /* teardown */ }

            _blip = null;
            _vanBlip = null;
            _van = null;
            _saidHere = false;
            Reset();

            // So the card arrives properly rather than being already there next time.
            _cardAt = 0;
            _bar = 0f;
            _barLeg = -1;
            _lastDrawnLeg = -1;
            _legFar = 0f;
            _legBest = 0f;
        }

        public void RestoreWorld()
        {
            // The linger is dropped FIRST, because ClearDrop deliberately refuses to touch him
            // while it is set -- which is right every tick of the game and wrong exactly once,
            // when the mod is being torn down and there will be no next tick to release him.
            _lingering = false;
            Pack();
        }
    }
}
