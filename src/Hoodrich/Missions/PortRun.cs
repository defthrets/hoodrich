using System;
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

        /// <summary>Told where to go, box not collected.</summary>
        public const int StageFetch = 1;

        /// <summary>Holding his quarter kilo, owing him a yard in Chamberlain.</summary>
        public const int StageDeliver = 2;

        // ---- his van -----------------------------------------------------------

        /// <summary>
        /// Where it is parked. Up the street from him, nose south, against the kerb.
        /// </summary>
        private static readonly Vector3 VanSpot = new Vector3(-178.840f, -1634.584f, 33.290f);
        private const float VanHeading = 181.403f;

        /// <summary>
        /// Hash checked against the spawner rather than typed off a menu: rumpo is 0x4543B74D.
        /// The two behind it are the same van, for an install that has not got the first.
        /// </summary>
        private static readonly string[] VanModels = { "rumpo", "burrito3", "youga" };

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
        /// The drop. A yard off the alley, walled on one side and fenced on the other.
        ///
        /// He stands ON the mark rather than beside it. Every other marker in the mod is a
        /// place to put a car and this one is a place to put a box, and the box goes to a man:
        /// the ring around him says he is the drop, which saves inventing a second mark two
        /// metres away for him to stand in and hoping there is no wall there.
        /// </summary>
        private static readonly Vector3 DropSpot = new Vector3(-103.338f, -1417.405f, 29.170f);

        /// <summary>Facing back down the alley, which is the way you come in.</summary>
        private const float DropHeading = 236.125f;

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
        public bool WaitingAtTheDrop => Stage == StageDeliver;

        /// <summary>Where the mark is, whichever half of the run you are in.</summary>
        private Vector3 Mark => Stage == StageDeliver ? DropSpot : ParkSpot;

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

            Notify.Important("~g~Take his van.~s~ Elysian Island, ask for the dock worker.");
            Log.Info("Port run started after " + _state.GramsSold.ToString("0.#") + "g sold.");

            return true;
        }

        // ---- the run itself ----------------------------------------------------

        public void Update()
        {
            if (!Running)
            {
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
            var toPort = player.Position.DistanceTo(ParkSpot);
            var toDrop = player.Position.DistanceTo(DropSpot);

            if (toPort <= StreamRange) Tao(); else ClearPort();

            if (toDrop <= StreamRange && Stage == StageDeliver) Gerald(); else ClearDrop();

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
                    ? "Bring Gerald's van -- it's still in it."
                    : "Bring Gerald's van. It ain't going in your pockets.");

                return;
            }

            Help.ShowThisFrame(Stage == StageDeliver
                ? "Press ~INPUT_CONTEXT~ to hand it over."
                : "Press ~INPUT_CONTEXT~ to talk to him.");

            if (!Game.IsControlJustPressed(GTA.Control.Context)) return;
            if (Talk == null || Talk.IsOpen) return;

            Talk.Speaker = man;
            Talk.Open(Stage == StageDeliver ? HandOver() : Meeting(), this);
        }

        private bool VanIsNear(Ped man)
        {
            if (_van == null || !_van.Exists()) return false;
            return _van.Position.DistanceTo(man.Position) <= VanRange;
        }

        // ---- what they say -----------------------------------------------------

        /// <summary>The port. Short, because it is an introduction rather than a deal.</summary>
        private DialogueNode Meeting()
        {
            var node = new DialogueNode("Tao Cheng",
                "You're the one Gerald called about. Yeah, yeah -- back the van up, it goes in " +
                "the van, don't stand here counting it. And put my number in that phone, 'cause " +
                "next time you ain't driving all the way down here for a box.")
            {
                SpeakerColour = Palette.Cash
            };

            node.Say("Appreciate it.", Take, "Load the van and take his number");
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
            _state.PortRunStage = StageDeliver;
            _state.AddRespect(8f);
            _state.Touch();

            _saidHere = false;

            Notify.Important("~g~You've got his number.~s~ Now get that van back to Gerald.");
            Log.Info("Port run: van loaded, docks unlocked.");

            if (Social != null) Social.On(SocialEvent.PortRun, "Tao Cheng");

            return null;
        }

        /// <summary>The yard. He counts nothing, which is the compliment.</summary>
        private DialogueNode HandOver()
        {
            var node = new DialogueNode("Gerald",
                "There he is. Nah -- don't open it, don't tell me what's in it, I know what's " +
                "in it. You drove it here and you drove it here on time, and that's the whole " +
                "test, dawg. Man at the port's yours now. Use him.")
            {
                SpeakerColour = Palette.Cash
            };

            node.Say("Say less.", Drop, "Hand over the van");
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

            Pack();
            return null;
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

                _blip.Name = Stage == StageDeliver ? "Drop it to Gerald" : "Meet the plug";
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

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            Card();

            var where = Mark;
            if (player.Position.DistanceTo(where) > StreamRange) return;

            // Tighter at the yard than at the port, because one of them is a place to leave a
            // van and the other is a man stood in the middle of it.
            var radius = Stage == StageDeliver ? 2.4f : 5f;

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
            }

            var d = player.Position.DistanceTo(Mark);
            if (d > _legFar) _legFar = d;

            var done = _legFar < 1f ? 1f : 1f - d / _legFar;
            if (done < 0f) done = 0f;
            if (done > 1f) done = 1f;

            return leg == StageDeliver ? 0.5f + done * 0.5f : done * 0.5f;
        }

        /// <summary>
        /// The same card a job draws, in the same place, because it is the same kind of thing.
        ///
        /// It stands down while a mission is running rather than drawing over it. Two cards in
        /// one slot is not a layout problem to solve with an offset -- you can only be doing one
        /// of them at a time in any sense that matters, and the job is the one you chose.
        /// </summary>
        private void Card()
        {
            if (Busy != null && Busy()) return;

            if (_cardAt == 0) _cardAt = Game.GameTime;

            // Rises into place and fades up, eased out so it arrives rather than snaps. The
            // same entrance every other panel in the mod uses.
            var age = Game.GameTime - _cardAt;
            var enter = age >= EnterMs ? 1f : age / (float)EnterMs;
            var eased = 1f - (1f - enter) * (1f - enter);

            var top = CardTop + EnterRise * (1f - eased);
            var fade = eased;

            var left = 0.5f - CardWidth * 0.5f;
            var ink = Fade(Stage == StageDeliver ? Palette.Cash : Palette.Standing, fade);

            Hud.RectFrom(left, top, CardWidth, CardHeight, Fade(CardBack, fade));
            Hud.RectFrom(left, top, CardRail, CardHeight, ink);
            Hud.RectFrom(left, top, CardWidth, 0.0022f, ink);

            var iconLeft = left + CardRail + CardPad;
            var iconWide = Hud.ToX(IconSize);

            Hud.RectFrom(iconLeft, top + (CardHeight - IconSize) * 0.5f,
                         iconWide, IconSize, Color.FromArgb((int)(20 * fade), 255, 255, 255));

            Hud.File(Stage == StageDeliver ? "box.png" : "crate.png",
                     iconLeft + iconWide * 0.5f, top + CardHeight * 0.5f,
                     IconSize * 0.62f, 0f, ink);

            var x = iconLeft + iconWide + CardPad;

            Hud.Text("THE PORT RUN", x, top + 0.009f, 0.30f, Fade(Palette.Text, fade),
                     Hud.FontLabel, centre: false);

            Hud.Text(Stage == StageDeliver
                        ? "Get his van back to the yard in Chamberlain"
                        : "Meet the dock worker at Elysian Island",
                     x, top + 0.030f, 0.26f, Fade(Palette.TextDim, fade),
                     Hud.FontBody, centre: false);

            Hud.TextRight(Stage == StageDeliver ? Package.ToString("0") + "g" : "TAO",
                          left + CardWidth - CardPad, top + 0.031f, 0.23f,
                          ink, Hud.FontLabel);

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

            _gerald = Stand(GeraldModels, DropSpot, DropHeading, "Gerald");
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

                    // Strip the branding before the paint goes on, and check that it took.
                    Blank(h);

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
        private static void Blank(int h)
        {
            var told = "";

            try
            {
                var count = Function.Call<int>(Hash.GET_VEHICLE_LIVERY_COUNT, h);

                if (count > 0)
                {
                    Function.Call(Hash.SET_VEHICLE_LIVERY, h, -1);

                    if (Function.Call<int>(Hash.GET_VEHICLE_LIVERY, h) >= 0)
                    {
                        Function.Call(Hash.SET_VEHICLE_LIVERY, h, 0);
                    }
                }

                told += "livery " + count + "->" + Function.Call<int>(Hash.GET_VEHICLE_LIVERY, h);
            }
            catch { told += "livery n/a"; }

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

            Log.Info("Van stripped: " + told + ", " + off + " extras off.");
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
                _vanBlip.Name = "Gerald's van";
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

                    // Door 5 is the boot, opened instantly rather than swung -- it was already
                    // up when you drove in.
                    Function.Call(Hash.SET_VEHICLE_DOOR_OPEN, _car.Handle, 5, false, true);

                    Log.Info("The plug's car is parked at the port.");
                    return;
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not park the plug's car: " + ex.Message);
                }
            }
        }

        // ---- taking it away ----------------------------------------------------

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

            // So the card arrives properly rather than being already there next time.
            _cardAt = 0;
            _bar = 0f;
            _barLeg = -1;
            _lastDrawnLeg = -1;
            _legFar = 0f;
        }

        public void RestoreWorld() => Pack();
    }
}
