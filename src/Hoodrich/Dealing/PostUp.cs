using System;
using System.Collections.Generic;
using System.Drawing;
using Control = GTA.Control;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Social;
using Hoodrich.Economy;
using Hoodrich.Gangs;
using Hoodrich.State;
using Hoodrich.Supply;
using Hoodrich.Territory;
using Hoodrich.UI;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.Dealing
{
    /// <summary>What the corner is doing to you right now.</summary>
    internal enum PostState
    {
        Idle,
        Posted,

        /// <summary>Someone is walking over to buy.</summary>
        Approaching,

        /// <summary>Mid handoff.</summary>
        Dealing,

        /// <summary>Police are on their way to ask what you are doing.</summary>
        Investigated,

        /// <summary>A cop is on you and the clock is running.</summary>
        Questioned
    }

    /// <summary>
    /// Standing on a corner and letting the trade come to you.
    ///
    /// This is the whole risk model in one mechanic. You do not pick customers -- you pick a
    /// SPOT, and the spot decides everything. A dead alley has no footfall, so no sales and no
    /// heat. A busy pavement pushes buyers at you and stacks heat with every one of them. Too
    /// much heat and a patrol comes over to ask what you are standing there for; stay put and
    /// you get searched, cleaned out and fined.
    ///
    /// So the interesting decision is not "who do I sell to" but "how long do I stay here".
    /// </summary>
    internal sealed class PostUp
    {
        private const int ScanIntervalMs = 2600;
        private const float FootfallRadius = 18f;
        private const float ApproachRange = 22f;

        /// <summary>
        /// How far you can drift from the pitch before you stop working it.
        ///
        /// Forty, which is double what it was. Twenty metres is about four car lengths, and a
        /// corner is bigger than that -- you cannot cross the road, walk to the far end of the
        /// block or step round the back of a building without the pitch letting go of you. The
        /// whole point of standing somewhere is that you are standing on a BLOCK, not on a
        /// paving slab, and the original number was already there because being frozen to one
        /// tile made it feel like a menu. It was just not generous enough.
        ///
        /// It is deliberately well inside CornerHeat's sixty-metre radius, so drifting to the
        /// end of your own leash never lands you on ground the mod considers a different
        /// corner -- the heat you are carrying comes with you.
        /// </summary>
        private const float LeashDistance = 40f;
        private const int DealDurationMs = 2600;
        private const float CopArriveRange = 3.5f;
        private const float CopScanRange = 160f;

        private const string AnimDict = "mp_common";
        private const string AnimPlayer = "givetake1_a";
        private const string AnimBuyer = "givetake1_b";

        /// <summary>How close the two of you stand for the handoff to look like a handoff.</summary>
        private const float HandoffDistance = 0.9f;

        /// <summary>
        /// What a passer-by actually buys: a gram, or an eighth.
        ///
        /// Those are the two amounts anybody asks for, so the money reads right without anyone
        /// having to explain it -- a gram of weed is twenty dollars and an eighth is fifty,
        /// because that is what a gram and an eighth cost.
        /// </summary>
        private static readonly float[] DealSizes = { 1f, 1f, 1f, 3.5f };

        /// <summary>
        /// And what somebody asks for when it is counted.
        ///
        /// One, two or four. At twenty-five a pill that is the twenty-five, fifty and hundred
        /// dollar deals everybody actually does, and nobody has to be told what an eighth of a
        /// pill would be.
        /// </summary>
        private static readonly float[] PillDeals = { 1f, 1f, 2f, 2f, 4f };

        /// <summary>
        /// How close a uniform has to be for a handoff to be in their view.
        ///
        /// Matched to the crawl range on purpose: a patrol easing past at walking pace is
        /// plainly looking at you, and serving somebody while they do it should cost, whether
        /// or not the car happens to be inside an arbitrary shorter radius.
        /// </summary>
        private const float CopWitnessRange = PatrolCrawlRange;

        /// <summary>
        /// How fast a pitch warms up, on top of the product and the crowd.
        ///
        /// A corner that took an age to get warm meant the police half of the mechanic almost never
        /// fired -- you ran out of product before you ran out of welcome, so the decision the whole
        /// thing is built on never got asked.
        /// </summary>
        private const float HeatRate = 1.25f;

        /// <summary>Corner heat at which the police stop needing to see anything.</summary>
        private const float HeatForWanted = 0.85f;

        /// <summary>Working a corner this long starts attracting the wrong kind of attention.</summary>
        private const int DriveByAfterMs = 5 * 60 * 1000;

        /// <summary>Chance per scan of a rival car coming past, once you have been here a while.</summary>
        private const float DriveByChancePercent = 4f;

        /// <summary>Beat between your line and the buyer's answer, so they do not overlap.</summary>
        private const int BuyerReplyDelayMs = 1000;

        /// <summary>How long they shoot for once they are alongside you.</summary>
        private const int DriveByShootMs = 3000;

        /// <summary>Close enough to be shooting at you rather than still driving over.</summary>
        private const float DriveByShootRange = 45f;

        /// <summary>If they never reach you at all, they stop trying.</summary>
        private const int DriveByFindTimeoutMs = 150000;

        /// <summary>How far a rival can be and still notice you working their block.</summary>
        private const float RivalNoticeRange = 28f;

        /// <summary>What a carload of somebody else's people turns up in.</summary>
        private static readonly string[] GangCars = { "baller", "buccaneer", "primo", "manana", "tornado", "peyote" };

        private static readonly string[] CopModels = { "s_m_y_cop_01", "s_f_y_cop_01", "s_m_y_swat_01" };

        /// <summary>Said by whoever just bought from you.</summary>
        private static readonly string[] BuyerLines =
        {
            "SPEECH_BUY_DRUGS", "GENERIC_THANKS", "GENERIC_BYE"
        };

        /// <summary>
        /// What a buyer says while it is happening, rather than after.
        ///
        /// Neutral on purpose. Somebody buying off you on a corner is doing something ordinary
        /// and slightly furtive, not starting an argument -- the aggressive lines are kept for
        /// the one thing that actually warrants them, which is being sold something short.
        /// </summary>
        private static readonly string[] BuyerChatter =
        {
            "GENERIC_HOWS_IT_GOING", "GENERIC_HI", "CHAT_STATE", "GENERIC_YES"
        };

        /// <summary>Said by somebody who has just worked out what you sold them.</summary>
        private static readonly string[] RefusedLines =
        {
            "GENERIC_INSULT_HIGH", "GENERIC_CURSE_HIGH", "GENERIC_INSULT_MED"
        };

        /// <summary>How often a knocked-back buyer decides to do something about it.</summary>
        /// <summary>
        /// How often a man who has been sold rubbish does something about it.
        ///
        /// One in five was somebody shrugging. Being handed a weak bag by a dealer you walked
        /// up to is an insult in front of whoever is on the corner, and the usual answer to it
        /// is not walking away -- so most of them square up now.
        /// </summary>
        private const float RefusedFightChance = 0.75f;

        /// <summary>
        /// And how often a woman does. Lower, deliberately.
        ///
        /// Not a rule about who fights; a rule about what this corner should look like. A queue
        /// of women swinging at Franklin over a light bag is not the scene, and the ones who do
        /// not swing still turn him down, tell people, and cost him the sale and the name.
        /// </summary>
        private const float RefusedFightChanceFemale = 0.15f;

        /// <summary>A buyer who cannot reach you gives up after this and wanders off.</summary>
        private const int ApproachTimeoutMs = 60000;

        /// <summary>Said by the player once the handoff lands.</summary>
        private static readonly string[] SellerLines =
        {
            "GENERIC_BYE", "GENERIC_THANKS", "GENERIC_HOWS_IT_GOING"
        };

        private readonly Settings _cfg;

        /// <summary>Set by Main, so a seizure is a seizure.</summary>
        public Weapons.GunLocker Locker;
        private readonly PlayerState _state;
        private readonly Pricing _pricing;
        private readonly Random _rng = new Random();
        private readonly Dictionary<int, int> _served = new Dictionary<int, int>();

        public TurfWatch Turf;

        /// <summary>Set by Main. Null-checked everywhere, so the feed is never load-bearing.</summary>
        public SocialFeed Social;


        /// <summary>The undercover roll. Owned by Main so a call outlives the pitch.</summary>
        public Bust Bust;
        public Affiliation Crew;

        private DrugDef _product;
        private Vector3 _anchor;
        private int _lastScan;

        private Ped _customer;
        private int _approachStartedAt;
        private int _dealStartedAt;
        private bool _animRequested;

        private Ped _cop;
        private bool _copSpawned;
        private int _questionStartedAt;

        /// <summary>Attention specific to this pitch. Separate from global notoriety.</summary>
        private float _cornerHeat;

        /// <summary>
        /// Heat that stays on the ground after you have walked off it.
        ///
        /// The pitch's own _cornerHeat is a session counter and always was. This is the
        /// memory underneath it -- see CornerHeat.
        /// </summary>
        private readonly CornerHeat _ground = new CornerHeat();

        /// <summary>Exposed so the reset section can wipe it with everything else.</summary>
        public CornerHeat Ground => _ground;

        /// <summary>When the last sale landed, for the mark's pulse. 0 for none.</summary>
        private int _soldAt;

        /// <summary>
        /// When one walked away, for the mark's other pulse.
        ///
        /// Two things count as walking away and they are the same event as far as this corner
        /// is concerned: somebody who looks at what you handed him and hands it back, and
        /// somebody who takes it and gets on the phone about you. Both are a sale that did not
        /// happen, both are your fault, and both deserve the same acknowledgement the good ones
        /// get -- a corner where only the wins move is a corner that never tells you off.
        /// </summary>
        private int _spookedAt;

        /// <summary>How long that pulse lasts. Long enough to see, short enough not to nag.</summary>
        private const int SalePulseMs = 650;

        /// <summary>Marks the wordmark for the red version of the sale pulse.</summary>
        private void Spooked()
        {
            _spookedAt = Game.GameTime;

            // The bad one wins outright rather than blending with the good one. Half a green
            // swell finishing under a red one is a mark that cannot make its mind up.
            _soldAt = 0;
        }

        /// <summary>How long the mark breathes for while somebody is walking over.</summary>
        private const int WalkUpPulseMs = 1100;

        /// <summary>And the faster beat it throbs at while the law is on its way.</summary>
        private const int LawPulseMs = 620;

        private int _sales;
        private int _earned;

        /// <summary>When this pitch started, for the drive-by clock.</summary>
        private int _postedAt;

        /// <summary>Rival cars sent so far, so one pitch cannot spawn a convoy.</summary>
        private int _driveBys;

        private readonly List<Ped> _rivals = new List<Ped>();

        /// <summary>
        /// Buyers who squared up over the cut and are NOT the sort to stand in front of a gun.
        ///
        /// Only the soft ones go on here. A gangster who takes offence stays on the list of
        /// people you have a problem with, not on a list of people who can be made to leave.
        /// </summary>
        private readonly List<Ped> _swinging = new List<Ped>();
        private Vehicle _driveByCar;
        private Ped _driveByDriver;
        private int _driveByStartedAt;
        private bool _driveByBailed;
        private int _driveByInRangeAt;

        /// <summary>Buyer waiting to answer, and when.</summary>
        private Ped _pendingSpeaker;
        private int _pendingSpeakAt;

        public PostUp(Settings cfg, PlayerState state, Pricing pricing)
        {
            _cfg = cfg;
            _state = state;
            _pricing = pricing;
        }

        public PostState State { get; private set; } = PostState.Idle;

        public bool IsPosted => State != PostState.Idle;

        /// <summary>What you are moving, said the way you would say it in public.</summary>
        public string CodeWord =>
            _product == null || string.IsNullOrEmpty(_product.CodeWord) ? "" : _product.CodeWord;

        public DrugDef Product => _product;

        /// <summary>Heat from something that happened here but is not ours to time.</summary>
        public void AddCornerHeat(float amount)
        {
            if (!IsPosted || amount <= 0f) return;
            _cornerHeat += amount;
        }

        public int Footfall { get; private set; }

        private Stash Stash => _state.Stash;

        // ---- starting and stopping ---------------------------------------------

        /// <summary>Returns a player-facing refusal, or null once posted.</summary>
        public string Start(DrugDef product)
        {
            if (IsPosted) return "You're already posted up.";
            if (product == null) return "Pick something to move.";
            if (Stash.PackagedOf(product.Id) < 0.5f)
            {
                return Stash.BulkOf(product.Id) > 0.005f
                    ? "That's still weight. " + product.SplitVerb + " it first."
                    : "You ain't holding no " + product.Name.ToLowerInvariant() + ".";
            }

            // Stretched past the point of being product. Refused at the corner rather than
            // priced down at it, because a bag this weak does not sell cheap -- it sells once,
            // to somebody who tells everybody, and then never again.
            if (!Stash.Sellable(Stash.PurityOf(product.Id)))
            {
                return "That " + product.Name.ToLowerInvariant() + " is stepped on to nothing. " +
                       "Nobody out here is taking that off you.";
            }

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return "Not right now.";
            if (player.IsInVehicle()) return "Get out of the car first.";

            _product = product;
            _anchor = player.Position;

            // NOT zero. Whatever this block already has is what you are standing in.
            //
            // Starting every pitch clean is what made the heat optional: the way to beat a
            // patrol was to pack up, take two steps and press the button again. Now the corner
            // remembers, and getting away from it means getting away from it.
            _cooledAt = 0;
            _cornerHeat = _ground.At(_anchor);

            // Snapped, not swept. Posting up on a corner that is already warm should show you
            // that on the first frame -- the sweep is for heat you watch yourself earn.
            _heatBar.Reset();
            _repBar.Reset();
            _sales = 0;
            _earned = 0;
            _lastScan = 0;
            _postedAt = Game.GameTime;
            SchedulePatrol();
            if (Crew != null) Crew.WorkingACorner = true;
            _driveBys = 0;
            State = PostState.Posted;

            // No bag. It was asked for, fitted, and it reads as luggage clipped to a man
            // rather than as a man carrying something -- a prop on a spine bone cannot know
            // what his arms are doing, so it swings through him on half the walk cycles. What
            // he is holding is in the readout under the bars, which does not clip through
            // anything.


            Notify.Ticker("~g~Posted up.~s~ Moving " + product.Name.ToLowerInvariant() +
                          ". A busy sidewalk sells faster and burns hotter.");

            // Said out loud, or the bar starting halfway along is a bug as far as anybody
            // watching is concerned. The heat is on the BLOCK, and a mechanic nobody is told
            // about is a mechanic that reads as one.
            if (_cornerHeat > 1f)
            {
                Notify.Ticker("~o~This block's still warm from last time.~s~ " +
                              "Try somewhere else if you want a clean start.");
            }

            // And you tell the block, sideways.
            //
            // The YouPosted set already existed and could only be reached by opening the feed
            // and choosing to say something, which is a thing nobody does mid-shift. Standing
            // on a corner IS the announcement -- the post is how anybody knows to come, and it
            // goes up in the product's street name rather than its own, because a man listing
            // his stock on a public feed is a man who gets a visit.
            //
            // Gated on a short gap rather than fired blind, so flicking the pitch off and on
            // does not fill the feed with the same man saying he is outside four times.
            if (Social != null)
            {
                var code = string.IsNullOrEmpty(product.CodeWord)
                    ? product.Name.ToLowerInvariant()
                    : product.CodeWord;

                Social.PostAsYouSometimes("YouPosted", code, PostGapMs, 100);
            }

            Log.Info("Posted up with " + product.Id + " at " + _anchor +
                     " (inherited " + _cornerHeat.ToString("0.#") + " heat).");
            return null;
        }

        /// <summary>
        /// The bag, while he is working.
        ///
        /// An attached prop, not a clothing component. Component slot 5 IS the bag slot -- on a
        /// multiplayer freemode ped. On a story ped, which Franklin is, slot 5 is HANDS, so
        /// setting drawable 1 on it put a pair of black gloves on him and never put a bag
        /// anywhere. Story peds have no bag component at all, so a prop on his back is the only
        /// way he gets one.
        ///
        /// Tried in order, first one this install has wins, and an install with none of them
        /// simply deals without a bag rather than not dealing.
        /// </summary>
        /// <summary>SKEL_Spine3 -- between the shoulder blades, where a bag hangs.</summary>
        private const int SpineBone = 24818;

        /// <summary>
        /// Where the bag hangs, and which way up.
        ///
        /// From the ini, because these are six numbers that can only be judged by looking at
        /// them and the first set was a guess that put the bag through him. Nudging one and
        /// reloading a save beats nudging one and rebuilding.
        ///
        /// Backpack rather than a heist duffle by default: prop_michael_backpack is a prop the
        /// game itself hangs off a ped, so its origin is already set up to be worn -- a heist
        /// bag's is not, which is most of why the first attempt sat inside his ribs.
        /// </summary>
        private float BagX => _cfg == null ? 0f : _cfg.BagX;
        private float BagY => _cfg == null ? -0.16f : _cfg.BagY;
        private float BagZ => _cfg == null ? 0f : _cfg.BagZ;
        private float BagPitch => _cfg == null ? 0f : _cfg.BagPitch;
        private float BagRoll => _cfg == null ? 0f : _cfg.BagRoll;
        private float BagYaw => _cfg == null ? 0f : _cfg.BagYaw;

        /// <summary>Takes it off him, and off the map.</summary>
        private void DropTheBag()
        {
            if (_bag == null) return;

            try
            {
                if (_bag.Exists())
                {
                    Function.Call(Hash.DETACH_ENTITY, _bag.Handle, true, true);
                    _bag.Delete();
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not take the bag off: " + ex.Message);
            }

            _bag = null;
        }

        private Prop _bag;

        public void Stop(string reason)
        {
            if (!IsPosted) return;

            var sales = _sales;
            var earned = _earned;

            DropTheBag();

            ReleaseCustomer();
            ReleaseCop();

            // Everything else the corner spawned, or it stays in the road forever.
            //
            // These are all IsPersistent, which is what stops the game streaming them out while
            // you are working -- and it does not stop being true when you pack up. A patrol car
            // and two rivals left persistent on every pitch you ever stood on is a city that
            // slowly fills with parked cars nobody can move, which is exactly what happened.
            ReleasePatrol();
            ReleaseRivals();

            // Left on the ground rather than binned. This is the whole mechanic.
            // Nobody is mid-deal once the corner is shut. A handle left behind is somebody
            // his own system has stopped looking after, and handles get reused.
            Serving.Clear();

            _ground.Remember(_anchor, _cornerHeat);

            State = PostState.Idle;
            _product = null;
            _cornerHeat = 0f;

            // A buyer's reply is queued a second into the future. Packing up inside that second
            // left it to fire on the NEXT pitch, so the first thing a fresh corner did was have
            // a stranger from the last one thank you.
            _pendingSpeaker = null;

            if (Crew != null) Crew.WorkingACorner = false;

            if (!string.IsNullOrEmpty(reason)) Notify.Ticker("~o~" + reason + "~s~");

            if (sales > 0)
            {
                Notify.Ticker("~g~" + sales + " sold~s~ for $" + earned.ToString("N0") + ".");
            }
        }

        // ---- per-tick ----------------------------------------------------------

        /// <summary>
        /// How long before posting up will announce itself again.
        ///
        /// Ninety seconds. Long enough that packing up to move down the road and setting up
        /// again does not read as somebody spamming their own feed, short enough that a real
        /// shift on a new block is a new post.
        /// </summary>
        private const int PostGapMs = 90000;

        /// <summary>Quiet for this long and the corner starts going off the boil.</summary>
        private const int CoolAfterMs = 25000;

        /// <summary>
        /// How much heat a minute of nobody coming takes back off.
        ///
        /// Deliberately slow. Waiting has to be a decision with a real price in time, not a
        /// button that undoes the last ten minutes -- at the first figure a quiet minute wiped
        /// out three sales' worth of attention, which made standing still strictly better than
        /// moving on and turned the corner into somewhere you never had to leave.
        /// </summary>
        private const float CoolPerMinute = 1.3f;

        private int _cooledAt;

        /// <summary>
        /// Heat comes back down when nothing is happening.
        ///
        /// It only ever went UP, once per sale, and the only way down was to pack up and let
        /// the ground cool while you were elsewhere. Which made a corner a one-way trip: stand
        /// there long enough and the police are coming whether or not you have served anybody
        /// for the last five minutes, and waiting it out -- the obvious thing to try, and the
        /// thing a person would actually do -- did nothing at all.
        ///
        /// Tied to the last SALE rather than to a clock of its own, because what makes a corner
        /// hot is traffic. A queue is what gets noticed; a man stood on his own is not, however
        /// long he has been there.
        ///
        /// Slower than it builds, on purpose. Waiting should be a real decision with a real
        /// cost in time, not a button that undoes the last ten minutes.
        /// </summary>
        private void Cool()
        {
            var now = Game.GameTime;

            if (_cooledAt == 0) { _cooledAt = now; return; }

            var since = now - _cooledAt;
            if (since < 1000) return;

            _cooledAt = now;

            if (_cornerHeat <= 0f) return;
            if (now - _soldAt < CoolAfterMs) return;

            _cornerHeat = Math.Max(0f, _cornerHeat - CoolPerMinute * (since / 60000f));
        }

        public void Update()
        {
            // Before the early return, deliberately. Blocks cool while you are somewhere else,
            // which is the only reason "come back later" is an answer as well as "go
            // somewhere else" -- and being somewhere else is exactly when this is not posted.
            _ground.Tick();

            // The bag the last customer walked off with, let go of on its own clock. Before
            // the early return, because he keeps walking whether or not the pitch is open.
            if (_baggie != null && _baggieUntil != 0 && Game.GameTime >= _baggieUntil)
            {
                DropBaggie();
            }

            // Before the early return as well. A man swinging at you does not stop being a
            // problem because you put the product away, and drawing on him is the same answer
            // whether or not the pitch is still open.
            ScareOff();

            if (!IsPosted) return;

            Cool();

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive)
            {
                Stop("You went down.");
                return;
            }

            if (player.IsInVehicle())
            {
                Stop("You packed up.");
                return;
            }

            // Wandering off the pitch ends it. This is what makes it a SPOT, not a mode.
            if (player.Position.DistanceTo(_anchor) > LeashDistance)
            {
                Stop("You left the corner.");
                return;
            }


            // The buyer's answer is on a short fuse so it lands after the player's line.
            if (_pendingSpeaker != null && Game.GameTime >= _pendingSpeakAt)
            {
                Say(_pendingSpeaker, BuyerLines);
                _pendingSpeaker = null;
            }

            switch (State)
            {
                case PostState.Dealing:
                    TickDeal(player);
                    return;
                case PostState.Approaching:
                    TickApproach(player);
                    break;
                case PostState.Questioned:
                    TickQuestioning(player);
                    return;
                case PostState.Investigated:
                    TickInvestigation(player);
                    break;
            }

            var now = Game.GameTime;
            if (now - _lastScan < ScanIntervalMs) return;
            _lastScan = now;

            Footfall = CountFootfall(player);

            // Nowhere quiet ever sells. That is the trade the player is making.
            if (State == PostState.Posted && Footfall > 0) RollCustomer(player);

            if (State != PostState.Investigated && State != PostState.Questioned) RollPolice(player);

            RollRivals(player);
            RollDriveBy(player);
            TickDriveBy(player);
            RollPatrol(player);
            TickPatrol(player);
        }

        /// <summary>How many people are actually walking past. Drives sales AND heat.</summary>
        private int CountFootfall(Ped player)
        {
            var n = 0;
            try
            {
                foreach (var ped in World.GetNearbyPeds(player, FootfallRadius))
                {
                    if (!IsPlausibleCustomer(ped, player, ignoreCooldown: true)) continue;
                    n++;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Footfall scan failed: " + ex.Message);
            }
            return n;
        }

        private bool IsPlausibleCustomer(Ped ped, Ped player, bool ignoreCooldown)
        {
            if (ped == null || !ped.Exists() || !ped.IsAlive) return false;
            if (ped.Handle == player.Handle) return false;
            if (!ped.IsHuman || ped.IsInCombat || ped.IsRagdoll) return false;

            // ON FOOT, definitively.
            //
            // IsInVehicle answers the "sat in a seat" question and says no for somebody halfway
            // through a door, or on a bike, or being carried -- so a car full of people driving
            // past counted as footfall, the corner announced a customer, and nobody ever walked
            // up. IS_PED_ON_FOOT is the question actually being asked.
            if (!Function.Call<bool>(Hash.IS_PED_ON_FOOT, ped.Handle)) return false;
            if (Function.Call<bool>(Hash.IS_PED_IN_ANY_VEHICLE, ped.Handle, true)) return false;

            // And on roughly the same ground. Without this the freeway overhead counts as a
            // pavement, and people six metres above you are queueing to buy crack.
            if (Math.Abs(ped.Position.Z - player.Position.Z) > 3.5f) return false;

            var type = Function.Call<int>(Hash.GET_PED_TYPE, ped.Handle);
            if (type == 6 || type == 27 || type == 29) return false; // police never buy

            if (Crew != null && Crew.IsRival(ped)) return false;

            if (!ignoreCooldown && _served.ContainsKey(ped.Handle)) return false;

            return true;
        }

        /// <summary>
        /// Whether this is somebody who would pick up a phone about you.
        ///
        /// Gang models never do -- a Balla who watches you serve somebody has other options and
        /// none of them involve the police. Neither do the street models from round here, who
        /// live on this block and know how that goes. Everybody else might: the woman walking a
        /// dog, the man in a shirt on his way to work, the tourist who has never seen any of
        /// this before. It is the difference between a system that punishes you and a
        /// neighbourhood that has opinions about you.
        /// </summary>
        internal static bool WouldCallItIn(Ped ped)
        {
            if (ped == null || !ped.Exists()) return false;

            try
            {
                var model = (uint)ped.Model.Hash;

                foreach (var quiet in NeverCalls)
                {
                    if (model == (uint)Function.Call<int>(Hash.GET_HASH_KEY, quiet)) return false;
                }
            }
            catch
            {
                // Cannot tell, so assume they might.
            }

            return true;
        }

        /// <summary>
        /// Who never rings the police.
        ///
        /// Every gang model in the game, and the south-central civilians -- the people who
        /// actually live where you are standing. Listed rather than derived, because there is no
        /// flag on a ped that means "from round here".
        /// </summary>
        private static readonly string[] NeverCalls =
        {
            "g_m_y_famca_01", "g_m_y_famdnf_01", "g_m_y_famfor_01",
            "g_m_y_ballaeast_01", "g_m_y_ballaorig_01", "g_m_y_ballasout_01", "g_f_y_ballas_01",
            "g_m_y_mexgang_01", "g_m_y_mexgoon_01", "g_m_y_mexgoon_02", "g_m_y_mexgoon_03",
            "g_m_y_salvaboss_01", "g_m_y_salvagoon_01", "g_m_y_salvagoon_02", "g_m_y_salvagoon_03",
            "g_m_y_lost_01", "g_m_y_lost_02", "g_m_y_lost_03", "g_f_y_lost_01",
            "g_m_y_korean_01", "g_m_y_korean_02", "g_m_y_armgoon_01", "g_m_y_armgoon_02",
            "g_m_m_armboss_01", "g_m_m_chiboss_01", "g_m_m_chicold_01", "g_m_m_chigoon_01",
            "a_m_y_soucent_01", "a_m_y_soucent_02", "a_m_y_soucent_03", "a_m_y_soucent_04",
            "a_m_m_soucent_01", "a_m_m_soucent_02", "a_m_m_soucent_03", "a_m_m_soucent_04",
            "a_f_y_soucent_01", "a_f_y_soucent_02", "a_f_y_soucent_03",
            "a_f_m_soucent_01", "a_f_m_soucent_02",
            "a_m_y_methhead_01", "a_m_m_tramp_01", "a_m_y_dhill_01", "a_f_m_trampbeac_01",
        };

        private void RollCustomer(Ped player)
        {
            // Each passer-by gets their own roll, so a busy pavement really is busier.
            //
            // And this is where the night, the block, your rank, the heat on you and what the
            // market is doing to that particular product all land. They used to be applied to
            // the price, which meant a gram of weed quietly became $34 at two in the morning.
            // They move how often somebody walks up instead: a good corner at a good hour is
            // busier, and busier is the whole reward.
            var per = _cfg.PostUpApproachChance / 100f * _pricing.Demand(_product);
            if (per > 0.9f) per = 0.9f;

            var chance = 1f - (float)Math.Pow(1f - per, Footfall);
            if (_rng.NextDouble() > chance) return;

            Ped pick = null;
            try
            {
                foreach (var ped in World.GetNearbyPeds(player, ApproachRange))
                {
                    if (!IsPlausibleCustomer(ped, player, ignoreCooldown: false)) continue;
                    pick = ped;
                    break;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Customer pick failed: " + ex.Message);
            }

            if (pick == null) return;

            _customer = pick;
            _served[pick.Handle] = Game.GameTime;

            // AND EVERYBODY ELSE LETS GO OF HIM. He may be one of the ring at a takeover or
            // one of the people at a party, and both of those are held to a mark by a pass
            // that would otherwise walk him back the moment he set off. See Serving.
            Serving.Start(pick);
            State = PostState.Approaching;
            _approachStartedAt = Game.GameTime;

            try
            {
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, pick.Handle, true);
                Function.Call(Hash.TASK_GO_TO_ENTITY, pick.Handle, player.Handle, 8000, 1.2f, 1.4f, 0, 0);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not send a customer over: " + ex.Message);
            }
        }

        private void TickApproach(Ped player)
        {
            if (_customer == null || !_customer.Exists() || !_customer.IsAlive)
            {
                ReleaseCustomer();
                State = PostState.Posted;
                return;
            }

            if (player.Position.DistanceTo(_customer.Position) > 2.2f)
            {
                // Some of them cannot get to you: a fence, a wall, a car in the way, or a spot
                // you picked that has no route into it. Waiting forever means the corner stops
                // producing and looks broken, so they give up and go about their day.
                if (Game.GameTime - _approachStartedAt < ApproachTimeoutMs) return;

                Log.Info("A buyer gave up trying to reach you.");
                ReleaseCustomer();
                State = PostState.Posted;
                return;
            }

            State = PostState.Dealing;
            _dealStartedAt = Game.GameTime;
            _animRequested = true;

            // He says something as it starts. Franklin answers when it lands, so the exchange
            // has two voices in it rather than one man muttering at a stranger.
            Say(_customer, BuyerChatter);

            try
            {
                // The give/take pair is authored for two people almost touching and facing each
                // other. Left where they happened to stop, the hands passed through empty air a
                // metre apart -- so the buyer is walked onto the mark and both are turned in.
                Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, _customer.Handle, player.Handle, DealDurationMs);
                Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, player.Handle, _customer.Handle, DealDurationMs);

                var mark = MarkInFrontOf(player);
                Function.Call(Hash.TASK_GO_STRAIGHT_TO_COORD, _customer.Handle,
                              mark.X, mark.Y, mark.Z, 1f, 1500, HeadingFrom(_customer.Position, player.Position), 0.1f);

                Function.Call(Hash.REQUEST_ANIM_DICT, AnimDict);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not start the handoff: " + ex.Message);
            }
        }

        /// <summary>The spot a buyer should stand on to be within arm's reach of the player.</summary>
        private static Vector3 MarkInFrontOf(Ped player)
        {
            var heading = player.Heading * (float)Math.PI / 180f;

            return player.Position + new Vector3(
                -(float)Math.Sin(heading) * HandoffDistance,
                (float)Math.Cos(heading) * HandoffDistance,
                0f);
        }

        private static float HeadingFrom(Vector3 from, Vector3 to)
        {
            return (float)(Math.Atan2(to.X - from.X, to.Y - from.Y) * 180.0 / Math.PI);
        }

        private void TickDeal(Ped player)
        {
            if (_customer == null || !_customer.Exists() || !_customer.IsAlive)
            {
                ReleaseCustomer();
                State = PostState.Posted;
                return;
            }

            if (_animRequested && Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, AnimDict))
            {
                // Wait for him to actually reach the mark. Firing the clips the moment the
                // dictionary loads is what had the two of them miming at each other across a
                // metre of pavement.
                if (player.Position.DistanceTo(_customer.Position) > HandoffDistance + 0.7f) return;

                _animRequested = false;
                PlayHandoff(player, _customer);
            }

            if (Game.GameTime - _dealStartedAt < DealDurationMs) return;

            CompleteSale(player);
        }

        /// <summary>
        /// Plays the handoff on each of them separately.
        ///
        /// A synchronized scene is the textbook way to line two peds up, and it was tried --
        /// but the scene origin has to agree with where the game thinks both peds are, and when
        /// it does not it teleports whoever is attached to it. Launching the player into the
        /// sky is a far worse bug than two people standing slightly too far apart, so this
        /// stays as two ordinary tasks and the alignment is handled by walking the buyer onto
        /// the mark beforehand.
        /// </summary>
        private void PlayHandoff(Ped player, Ped buyer)
        {
            PlayAnim(player, AnimPlayer);
            PlayAnim(buyer, AnimBuyer);

            // AND SOMETHING ACTUALLY CHANGES HANDS. The handshake was two people miming an
            // exchange with nothing between them -- the whole animation is built around an
            // object and there was no object, so what you watched was a peculiar high five
            // that ended in money appearing.
            Baggie(player);
        }

        /// <summary>
        /// The bag, put in his hand for the handshake.
        ///
        /// The same ladder the coke ritual uses, because it is the same question -- see
        /// Ritual.InHand, which owns it now so there is one list of names rather than two that
        /// drift apart.
        /// </summary>
        private void Baggie(Ped player)
        {
            DropBaggie();

            _baggie = Economy.Ritual.InHand(player, BaggieProps, BaggieSits, BaggieTurned);
            _baggieUntil = 0;
        }

        /// <summary>
        /// It goes to the buyer when the deal closes, and he takes it away with him.
        ///
        /// RE-ATTACHED RATHER THAN SWAPPED. One call moves it from one hand to the other, and
        /// there is never a frame where it belongs to nobody -- a frame like that is a bag
        /// falling through the pavement in front of the customer.
        ///
        /// Then it is on a clock. The buyer is released the same instant and walks off; a prop
        /// bolted to a stranger the mod has let go of is a bag that exists for the rest of the
        /// session, on somebody who will eventually be cleaned up around it.
        /// </summary>
        private void PassBaggie(Ped buyer)
        {
            if (_baggie == null || !_baggie.Exists()) { _baggie = null; return; }

            if (buyer == null || !buyer.Exists())
            {
                DropBaggie();
                return;
            }

            Economy.Ritual.Give(_baggie, buyer, BaggieSits, BaggieTurned);

            _baggieUntil = Game.GameTime + BaggieGoneMs;
        }

        private void DropBaggie()
        {
            if (_baggie == null) return;

            try
            {
                if (_baggie.Exists()) _baggie.Delete();
            }
            catch
            {
                // The streamer gets it.
            }

            _baggie = null;
            _baggieUntil = 0;
        }

        private Prop _baggie;
        private int _baggieUntil;

        /// <summary>How long the buyer keeps it before it stops being ours to worry about.</summary>
        private const int BaggieGoneMs = 7000;

        /// <summary>Names to try, and where it sits. Same list as the coke ritual.</summary>
        private static readonly string[] BaggieProps =
        {
            "prop_meth_bag_01", "prop_drug_package_02", "prop_drug_package", "prop_cash_pile_01"
        };

        private static readonly Vector3 BaggieSits = new Vector3(0.02f, 0.01f, 0.0f);
        private static readonly Vector3 BaggieTurned = new Vector3(0f, 0f, 0f);

        private static void PlayAnim(Ped ped, string anim)
        {
            try
            {
                Function.Call(Hash.TASK_PLAY_ANIM, ped.Handle, AnimDict, anim,
                              8f, -8f, -1, 0, 0f, false, false, false);
            }
            catch
            {
                // Cosmetic only.
            }
        }

        private void CompleteSale(Ped player)
        {
            var product = _product;
            var customer = _customer;

            // BEFORE THE CUSTOMER IS LET GO, because after it he is not ours to hand anything
            // to -- the reference is cleared and the man has been told to get on with his day.
            PassBaggie(customer);

            ReleaseCustomer();
            State = PostState.Posted;

            if (product == null) return;

            // A written deal if the product has any, otherwise a gram or an eighth.
            Economy.Deal deal = null;

            if (product.Deals.Count > 0)
            {
                // Weighted to the small end: most people buying on a corner buy the smallest
                // thing on it, and an ounce moving as often as a gram is a wholesaler, not a
                // corner.
                var roll = _rng.NextDouble();
                var index = roll < 0.62 ? 0 : roll < 0.9 ? 1 : 2;

                deal = product.Deals[Math.Min(index, product.Deals.Count - 1)];
            }

            var sizes = product.Counted ? PillDeals : DealSizes;
            var asked = deal != null ? deal.Quantity : sizes[_rng.Next(sizes.Length)];
            var grams = Math.Min(asked, Stash.PackagedOf(product.Id));
            if (grams < 0.05f)
            {
                Stop("You're out of " + product.Name.ToLowerInvariant() + ".");
                return;
            }

            var purity = Stash.PurityOf(product.Id);

            // Stepped-on product still gets knocked back, same as a hand-to-hand.
            if (_rng.NextDouble() < Pricing.BadCutChance(purity))
            {
                _state.AddNotoriety(1f);

                // And they tell people. This is the half of the purity system that was
                // documented and never built -- without it a refusal cost one sale and nothing
                // else, so stretching product had no downside worth the name.
                _state.RefusedAt(purity);

                Notify.Problem("they noticed the product was low purity.");

                Spooked();
                Refused(player, customer);
                return;
            }

            var sold = Stash.RemovePackaged(product.Id, grams);
            if (sold <= 0f) return;

            // Short-changed on the amount means short-changed on the money: somebody who only
            // got half an eighth does not pay for an eighth.
            var payout = deal != null && sold >= asked - 0.001f
                ? _pricing.DealValue(product, deal, purity)
                : _pricing.SaleValue(product, sold, purity);
            Cash.Give(payout);

            _sales++;
            _earned += payout;

            // A sale that landed still moves the block's opinion toward what you sold them.
            // Good product earns the name back; weak product costs it a little at a time even
            // when nobody hands it back.
            _state.SoldAt(purity);

            if (Social != null)
            {
                Social.On(payout >= 400 ? SocialEvent.BigSale : SocialEvent.Sale,
                          product.Name, payout);
            }

            // And the block remembers. Enough of these and the next customer takes longer to
            // turn up here than he would two streets over.
            _pricing.SoldHere(sold);

            _state.AddRespect(1f + product.Tier * 0.4f);
            _state.GramsSold += sold;
            _state.TotalDealsMade++;
            _state.TotalEarned += payout;

            // The one the bank card shows as the last transfer. Written here rather than
            // anywhere else because THIS is the only place money is ever earned dealing --
            // TotalEarned has exactly one caller and this is it.
            _state.LastDeal = payout;
            _state.Touch();

            // Heat is per-sale AND scaled by how public the spot is.
            var crowdFactor = 1f + Footfall * _cfg.PostUpHeatPerWitness;
            var heat = product.HeatFactor * crowdFactor * HeatRate *
                       (Turf == null ? 1f : Turf.TurfHeatMultiplier);

            _cornerHeat += heat;
            _state.AddNotoriety(heat * 0.5f);
            Turf?.MarkExposed();

            // The mark answers every sale, which is the only thing on that corner that moves.
            _soldAt = Game.GameTime;

            // AND HE SAYS SOMETHING. Money changed hands, the customer turns and walks, and
            // the man who just sold it to them said nothing at all -- which is the one moment
            // on this corner where silence is loud. The game has both of these lines in
            // Franklin's own banks, so it is his voice saying it and his face moving.
            Say(player, SoldOff);

            if (Crew != null && Crew.IsAffiliated)
            {
                var standing = Crew.CurrentStanding;
                standing.MoneyEarned += payout;
                standing.Deals++;
                Crew.CreditSale();
            }

            if (customer != null && customer.Exists())
            {
                try { Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, customer.Handle, false); }
                catch { /* he walks off either way */ }

                // Held back a beat: both talking at once was two voices over each other rather
                // than an exchange. The player speaks, then the buyer answers.
                _pendingSpeaker = customer;
                _pendingSpeakAt = Game.GameTime + BuyerReplyDelayMs;
            }

            Say(player, SellerLines);

            Notify.Ticker("~g~+$" + payout.ToString("N0") + "~s~  " + product.Amount(sold));

            // Doing it in front of a uniform is its own problem, regardless of how quiet the
            // corner has been up to now.
            if (CopIsWatching(player))
            {
                Notify.Failure("a badge just watched that.");
                Wanted(1);

                if (Social != null) Social.On(SocialEvent.Busted);

                // The patrol has to actually react. Left in its parked task it sat there while
                // the stars appeared, which reads as the game punishing you rather than as
                // being caught by the two men who were plainly watching.
                BreakOffPatrol(player);
            }
            else if (_cornerHeat >= _cfg.PostUpHeatBeforePolice * HeatForWanted)
            {
                // Word gets round without anybody having to see it.
                Notify.Failure("this corner's too hot now.");
                Wanted(1);
            }
            else if (Bust != null)
            {
                // Only reached when nobody saw it and the corner is still quiet. Stacking a
                // countdown on top of stars you already have is a pile-on, not a decision.
                // A call going out is a sale that cost you more than it made. The mark says
                // so, over the top of the green one the sale itself just started.
                if (Bust.OnSale(customer, product)) Spooked();
            }

            if (Stash.PackagedOf(product.Id) < 0.05f) Stop("That was the last of it.");
        }

        // ---- the other gangs ---------------------------------------------------

        /// <summary>
        /// Rivals who can see you working their block come and do something about it.
        ///
        /// Only once you have actually been seen dealing -- standing on somebody's corner is
        /// rude, selling on it is the problem -- and only for gangs at war with yours.
        /// </summary>
        private void RollRivals(Ped player)
        {
            if (_sales == 0 || Crew == null) return;
            if (Turf == null || !Turf.IsExposed) return;
            if (Turf.Status != TurfStatus.Hostile) return;

            try
            {
                foreach (var ped in World.GetNearbyPeds(player, RivalNoticeRange))
                {
                    if (ped == null || !ped.Exists() || !ped.IsAlive) continue;
                    if (ped.IsInCombat || _rivals.Contains(ped)) continue;
                    if (!Crew.IsRival(ped)) continue;

                    if (!Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, ped.Handle, player.Handle, 17)) continue;

                    _rivals.Add(ped);

                    // On foot it is a beating, not a shootout.
                    Function.Call(Hash.REMOVE_ALL_PED_WEAPONS, ped.Handle, true);
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, true);
                    Function.Call(Hash.TASK_COMBAT_PED, ped.Handle, player.Handle, 0, 16);

                    Notify.Failure("they caught you serving on their block.");

                    // One is enough to start it; the game's own gang AI brings the rest.
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Rival scan failed: " + ex.Message);
            }
        }

        /// <summary>
        /// A car full of somebody else's people, after you have been working long enough for
        /// word to travel. Rare per check, but near-certain if you never move.
        /// </summary>
        private void RollDriveBy(Ped player)
        {
            if (Game.GameTime - _postedAt < DriveByAfterMs) return;
            if (_driveBys >= 2) return;
            if (_driveByCar != null && _driveByCar.Exists()) return;
            if (_rng.NextDouble() * 100.0 > DriveByChancePercent) return;

            var gang = PickRivalGang();
            if (gang == null) return;

            SpawnDriveBy(player, gang);
        }

        /// <summary>Whoever has a reason to come for you. Falls back to any gang but your own.</summary>
        private GangDef PickRivalGang()
        {
            if (Crew == null) return null;

            // On somebody's turf it is them; otherwise it is whoever your lot are at war with.
            if (Turf != null && Turf.Status == TurfStatus.Hostile && Turf.Owner != null) return Turf.Owner;

            return Crew.IsAffiliated ? FirstRivalOf(Crew.Current) : null;
        }

        /// <summary>
        /// Whoever you have the worst beef with right now.
        ///
        /// Off standing rather than the gang's written rivals: the drive-by should come from
        /// somebody with a reason, and the list in the file is the same every game whatever you
        /// have or have not done. BeefingWith is sorted worst first, so this is the one who
        /// hates you most.
        /// </summary>
        private GangDef FirstRivalOf(GangDef gang)
        {
            if (Crew == null) return null;

            var beefing = Crew.BeefingWith();
            return beefing.Count == 0 ? null : beefing[0];
        }

        private void SpawnDriveBy(Ped player, GangDef gang)
        {
            var carModel = PickModel(GangCars);
            if (carModel == null) return;

            try
            {
                // Well behind the player, on a road, so it arrives rather than appears.
                var behind = player.Position - player.ForwardVector * 70f;
                var spawn = World.GetNextPositionOnStreet(behind);
                if (spawn == Vector3.Zero) return;

                _driveByCar = World.CreateVehicle(carModel.Value, spawn);
                if (_driveByCar == null || !_driveByCar.Exists()) return;

                _driveByCar.IsPersistent = true;

                for (var seat = -1; seat <= 1; seat++)
                {
                    var shooter = SpawnGangster(gang, _driveByCar, seat);
                    if (shooter == null) continue;

                    _rivals.Add(shooter);

                    if (seat == -1) _driveByDriver = shooter;
                    else ArmForDriveBy(shooter, player);
                }

                if (_driveByDriver != null)
                {
                    // Mission 6 is "run the target down": the car keeps circling and passing
                    // rather than parking. Driving TO a coordinate meant they arrived, stopped,
                    // and sat there -- which is what a delivery looks like, not an attack.
                    Function.Call(Hash.TASK_VEHICLE_MISSION_PED_TARGET, _driveByDriver.Handle,
                                  _driveByCar.Handle, player.Handle, 6, 25f, 786603, 12f, 5f, true);
                }

                _driveBys++;
                _driveByStartedAt = Game.GameTime;
                _driveByBailed = false;
                _driveByInRangeAt = 0;

                Notify.Failure("that ain't your people pulling up.");
                Log.Info("Drive-by from " + gang.Id + " after " +
                         ((Game.GameTime - _postedAt) / 1000) + "s posted up.");
            }
            catch (Exception ex)
            {
                Log.Debug("Drive-by failed: " + ex.Message);
            }
            finally
            {
                try { carModel.Value.MarkAsNoLongerNeeded(); } catch { }
            }
        }

        /// <summary>
        /// Puts a gun in a passenger's hands and tells him to lean out of the window.
        ///
        /// TASK_DRIVE_BY does nothing at all unless the ped is holding a weapon he is allowed
        /// to fire from a car, which is why the first pass had three men driving past waving.
        /// </summary>
        private static void ArmForDriveBy(Ped shooter, Ped player)
        {
            try
            {
                var weapon = Function.Call<uint>(Hash.GET_HASH_KEY, "WEAPON_MICROSMG");

                Function.Call(Hash.GIVE_WEAPON_TO_PED, shooter.Handle, weapon, 250, false, true);
                Function.Call(Hash.SET_CURRENT_PED_WEAPON, shooter.Handle, weapon, true);

                // 0 = can use cover, 1 = can use vehicles, 46 = always fight, 5 = can do drivebys.
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, shooter.Handle, 5, true);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, shooter.Handle, 46, true);
                Function.Call(Hash.SET_PED_ACCURACY, shooter.Handle, 25);

                Function.Call(Hash.TASK_DRIVE_BY, shooter.Handle, player.Handle, 0,
                              0f, 0f, 0f, 40f, 100, true, weapon);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not arm a drive-by shooter: " + ex.Message);
            }
        }

        /// <summary>
        /// Keeps the drive-by honest.
        ///
        /// They get a window to shoot from the car. After that -- or the moment the car has
        /// clearly stopped with everyone still sat in it -- they get out and fight, because a
        /// carful of men parked next to you doing nothing is the worst possible outcome.
        /// </summary>
        private void TickDriveBy(Ped player)
        {
            if (_driveByCar == null || !_driveByCar.Exists()) return;

            var elapsed = Game.GameTime - _driveByStartedAt;
            var distance = player.Position.DistanceTo(_driveByCar.Position);

            // The shooting clock starts when they are actually ON you, not when they set off.
            // Timing it from the spawn spent most of the window on the drive over, so the pass
            // itself was over before it looked like anything.
            if (_driveByInRangeAt == 0 && distance <= DriveByShootRange)
            {
                _driveByInRangeAt = Game.GameTime;
            }

            if (!_driveByBailed)
            {
                // The whole thing is one pass: they come past, they shoot, they are gone. A
                // carload that circles the block indefinitely is a siege, not a drive-by.
                var stalled = _driveByCar.Speed < 1.5f && distance < 35f && elapsed > 5000;

                var shooting = _driveByInRangeAt > 0 &&
                               Game.GameTime - _driveByInRangeAt >= DriveByShootMs;

                // Never found you at all: give up rather than circle forever.
                var gaveUp = _driveByInRangeAt == 0 && elapsed > DriveByFindTimeoutMs;

                if (!stalled && !shooting && !gaveUp) return;

                _driveByBailed = true;

                if (stalled)
                {
                    // Boxed in or stopped, so they finish it on foot -- with their hands. Men
                    // spilling out of a stalled car with rifles is a shootout, and the drive-by
                    // was supposed to be the drive-by.
                    BailOutAndBrawl(player);
                    Log.Info("Drive-by stalled after " + (elapsed / 1000) + "s; they got out.");
                    return;
                }

                DriveOff();
                Log.Info("Drive-by finished its pass after " + (elapsed / 1000) + "s in the area.");
                return;
            }

            // Everyone down or gone: let the car go.
            var standing = 0;
            foreach (var ped in _rivals)
            {
                if (ped != null && ped.Exists() && ped.IsAlive) standing++;
            }

            if (standing == 0 && _driveByCar != null && _driveByCar.Exists())
            {
                try { _driveByCar.MarkAsNoLongerNeeded(); } catch { }
                _driveByCar = null;
            }
        }

        /// <summary>They made their point; now they leave at speed and stop being ours.</summary>
        private void DriveOff()
        {
            try
            {
                if (_driveByDriver != null && _driveByDriver.Exists() && _driveByCar != null && _driveByCar.Exists())
                {
                    // Flee mission: away, fast, and not coming back round.
                    Function.Call(Hash.TASK_VEHICLE_MISSION_PED_TARGET, _driveByDriver.Handle,
                                  _driveByCar.Handle, Game.Player.Character.Handle, 8, 40f, 786603, 60f, 0f, true);
                }

                foreach (var ped in _rivals)
                {
                    if (ped == null || !ped.Exists()) continue;

                    Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);
                    ped.MarkAsNoLongerNeeded();
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Drive-by could not leave cleanly: " + ex.Message);
            }

            _rivals.Clear();

            try { if (_driveByCar != null && _driveByCar.Exists()) _driveByCar.MarkAsNoLongerNeeded(); }
            catch { /* teardown */ }

            _driveByCar = null;
            _driveByDriver = null;

            Notify.Ticker("~o~They rolled off.~s~");
        }

        /// <summary>
        /// Out of the car and onto you, with their hands.
        ///
        /// Guns are for the pass. Once they are on foot this is a beating, so every weapon is
        /// taken off them first -- otherwise a stalled car turns a drive-by into a firefight
        /// nobody asked for.
        /// </summary>
        private void BailOutAndBrawl(Ped player)
        {
            foreach (var ped in _rivals)
            {
                if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

                try
                {
                    Function.Call(Hash.REMOVE_ALL_PED_WEAPONS, ped.Handle, true);
                    Function.Call(Hash.SET_CURRENT_PED_WEAPON, ped.Handle,
                                  Function.Call<uint>(Hash.GET_HASH_KEY, "WEAPON_UNARMED"), true);

                    if (ped.IsInVehicle())
                    {
                        Function.Call(Hash.TASK_LEAVE_VEHICLE, ped.Handle, _driveByCar.Handle, 0);
                    }

                    Function.Call(Hash.TASK_COMBAT_PED, ped.Handle, player.Handle, 0, 16);
                }
                catch { /* the game's own AI takes it from here */ }
            }
        }

        private Ped SpawnGangster(GangDef gang, Vehicle car, int seat)
        {
            foreach (var name in gang.MemberModels)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !Core.Models.Ready(model)) continue;

                    var handle = Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE,
                                                    car.Handle, 4, model.Hash, seat, true, false);
                    model.MarkAsNoLongerNeeded();

                    if (handle == 0) continue;

                    var ped = (Ped)Entity.FromHandle(handle);
                    if (ped == null || !ped.Exists()) continue;

                    ped.IsPersistent = true;
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, true);
                    if (gang.GroupHash != 0)
                    {
                        Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle, gang.GroupHash);
                    }

                    Function.Call(Hash.GIVE_WEAPON_TO_PED, ped.Handle,
                                  Function.Call<uint>(Hash.GET_HASH_KEY, "WEAPON_MICROSMG"), 200, false, true);

                    return ped;
                }
                catch
                {
                    // Try the next model.
                }
            }

            return null;
        }

        // ---- the patrol car ----------------------------------------------------

        /// <summary>
        /// How long after posting up, or after the last one left, the next patrol turns up.
        ///
        /// Scheduled rather than rolled every couple of seconds: a per-tick dice roll can fire
        /// the moment you start and then again straight after, which reads as the game picking
        /// on you. One car at a random point in this window reads as luck.
        /// </summary>
        private const int PatrolGapMinMs = 50 * 1000;
        private const int PatrolGapMaxMs = 210 * 1000;

        /// <summary>
        /// How long the crawl lasts if they never quite get past you.
        ///
        /// A backstop, not the normal exit. They are meant to leave because they have driven
        /// by, not because a timer ran out.
        /// </summary>
        private const int PatrolCrawlMs = 9000;

        /// <summary>They drop to a crawl inside this, and pick up again once past you.</summary>
        private const float PatrolCrawlRange = 34f;

        /// <summary>Cruising speed on the way in and on the way out, and the crawl between.</summary>
        private const float PatrolCruiseSpeed = 17f;
        private const float PatrolCrawlSpeed = 3.2f;

        /// <summary>How far beyond you they aim, so the drive-by is a pass and not an arrival.</summary>
        private const float PatrolOvershoot = 70f;

        /// <summary>Where they set off from, so they arrive rather than appear.</summary>
        private const float PatrolStartDistance = 180f;



        /// <summary>Give up on the drive-in after this, rather than idling forever.</summary>
        private const int PatrolArriveTimeoutMs = 60000;

        /// <summary>Shuffled per call, so the same cruiser is not always the one that shows up.</summary>
        private static readonly string[] PatrolCars = { "police", "police2", "police3", "sheriff", "police4" };

        /// <summary>
        /// The patrol easing past your corner, for anything that wants to react to it.
        ///
        /// NOTHING READS THIS AT THE MOMENT. Its one consumer was the ambient patrol system's
        /// flip-off gesture -- Patrol.Passing -- and that system has moved out to Precinct 88.
        /// Kept rather than deleted because it is the only handle on the car this class
        /// dispatches, and the next thing that wants to react to a squad car easing past a
        /// corner will want exactly this.
        ///
        /// Null whenever there is no car or it is not a car worth telling about: one that has
        /// already stopped to question you is having a different conversation.
        /// </summary>
        public Vehicle RollingPast =>
            _patrolCar != null && _patrolCar.Exists() &&
            State != PostState.Investigated && State != PostState.Questioned
                ? _patrolCar
                : null;

        private Vehicle _patrolCar;
        private readonly List<Ped> _patrolCops = new List<Ped>();
        private int _patrolCrawlUntil;
        private bool _patrolCrawling;
        private int _nextPatrolAt;
        private int _patrolArrivedAt;
        private int _patrolDispatchedAt;
        private Vector3 _patrolStop;
        private Vector3 _patrolAim;

        /// <summary>
        /// A patrol driving past, slowly, having a look.
        ///
        /// They never stop. A car that parks up is a car you can simply wait out, and waiting
        /// is not a decision -- it is a pause. A car that comes down the road at speed, drops
        /// to a crawl as it draws level with you and then picks up and goes gives you a window
        /// instead of a wall: you can keep serving through it if you want to, and the witness
        /// check decides what that costs.
        ///
        /// Nothing about the pass is scripted at you. What changes is the price of the next
        /// sale while they are alongside.
        /// </summary>
        private void RollPatrol(Ped player)
        {
            if (_patrolCar != null && _patrolCar.Exists()) return;
            if (State == PostState.Investigated || State == PostState.Questioned) return;
            if (Game.GameTime < _nextPatrolAt) return;

            var carModel = PickModel(PatrolCars);

            if (carModel == null) return;

            try
            {
                // Started well down the road and driven in, rather than dropped at the kerb.
                // A car that simply exists beside you reads as a spawn; one that comes round
                // the corner, slows, and pulls in reads as a patrol.
                var angle = _rng.NextDouble() * Math.PI * 2.0;

                var far = player.Position + new Vector3(
                    (float)Math.Cos(angle) * PatrolStartDistance,
                    (float)Math.Sin(angle) * PatrolStartDistance, 0f);

                var start = World.GetNextPositionOnStreet(far);
                if (start == Vector3.Zero) { SchedulePatrol(); return; }

                // Aimed PAST you, not at you. Driving to a point beside the player is what made
                // them arrive and sit; driving to one well beyond it makes the same journey read
                // as a car going somewhere that happens to come by your corner.
                var through = player.Position - start;
                through.Z = 0f;

                if (through.Length() < 1f) { SchedulePatrol(); return; }

                _patrolAim = player.Position + Vector3.Normalize(through) * PatrolOvershoot;

                _patrolStop = World.GetNextPositionOnStreet(_patrolAim);
                if (_patrolStop == Vector3.Zero) _patrolStop = _patrolAim;

                _patrolCar = World.CreateVehicle(carModel.Value, start);
                if (_patrolCar == null || !_patrolCar.Exists()) { SchedulePatrol(); return; }

                _patrolCar.IsPersistent = true;
                _patrolCar.IsEngineRunning = true;

                for (var seat = -1; seat <= 0; seat++)
                {
                    var cop = SpawnCopInCar(_patrolCar, seat);
                    if (cop != null) _patrolCops.Add(cop);
                }

                // Drive mode 786603 obeys the road: lights, lanes, junctions.
                var driver = _patrolCops.Count > 0 ? _patrolCops[0] : null;
                if (driver != null)
                {
                    Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, driver.Handle, _patrolCar.Handle,
                                  _patrolStop.X, _patrolStop.Y, _patrolStop.Z,
                                  PatrolCruiseSpeed, 0, _patrolCar.Model.Hash, 786603, 4f, true);
                }

                _patrolCrawlUntil = 0;
                _patrolCrawling = false;
                _patrolArrivedAt = 0;
                _patrolDispatchedAt = Game.GameTime;
                SchedulePatrol();

                Log.Info("Patrol dispatched from " + start + " towards " + _patrolStop + ".");
            }
            catch (Exception ex)
            {
                Log.Debug("Patrol spawn failed: " + ex.Message);
            }
            finally
            {
                try { carModel.Value.MarkAsNoLongerNeeded(); } catch { }
            }
        }

        /// <summary>Sets when the next patrol is due, somewhere in the window.</summary>
        private void SchedulePatrol()
        {
            _nextPatrolAt = Game.GameTime + PatrolGapMinMs + _rng.Next(PatrolGapMaxMs - PatrolGapMinMs);
        }
        private void TickPatrol(Ped player)
        {
            if (_patrolCar == null || !_patrolCar.Exists()) return;

            var driver = _patrolCops.Count > 0 ? _patrolCops[0] : null;
            var near = player.Position.DistanceTo(_patrolCar.Position);

            // ---- coming down the road ------------------------------------------
            if (!_patrolCrawling && _patrolArrivedAt == 0)
            {
                if (near > PatrolCrawlRange)
                {
                    // Never got anywhere near: send them away rather than leave a car circling.
                    if (Game.GameTime - _patrolDispatchedAt > PatrolArriveTimeoutMs) ReleasePatrol();
                    return;
                }

                // Drawn level. Same destination, a fraction of the speed -- so they slow into
                // the crawl on their own rather than snapping to it, and they are still driving.
                _patrolCrawling = true;
                _patrolArrivedAt = Game.GameTime;
                _patrolCrawlUntil = Game.GameTime + PatrolCrawlMs;

                Drive(driver, _patrolStop, PatrolCrawlSpeed);

                Notify.Problem("black and white rolling past. Look busy.");
                Log.Info("Patrol crawling past at " + near.ToString("0") + "m.");
                return;
            }

            if (!_patrolCrawling) return;

            // ---- crawling past --------------------------------------------------
            // They leave because they have gone by, not because a clock ran out. The timer is
            // only there for the case where the road does not actually take them past you.
            var past = near > PatrolCrawlRange;
            var outOfPatience = Game.GameTime >= _patrolCrawlUntil;

            if (!past && !outOfPatience) return;

            // ---- and away -------------------------------------------------------
            _patrolCrawling = false;

            foreach (var cop in _patrolCops)
            {
                if (cop == null || !cop.Exists() || !cop.IsAlive) continue;

                try
                {
                    if (cop.SeatIndex == VehicleSeat.Driver)
                    {
                        Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, cop.Handle, _patrolCar.Handle,
                                      PatrolCruiseSpeed, 786603);
                    }

                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, cop.Handle, false);
                    cop.MarkAsNoLongerNeeded();
                }
                catch { /* they can look after themselves now */ }
            }

            try { _patrolCar.MarkAsNoLongerNeeded(); } catch { }

            _patrolCops.Clear();
            _patrolCar = null;
            SchedulePatrol();

            Log.Info("Patrol carried on down the road.");
        }

        /// <summary>Sends the driver somewhere at a given speed, obeying the road.</summary>
        private static void Drive(Ped driver, Vector3 to, float speed)
        {
            if (driver == null || !driver.Exists() || !driver.IsAlive) return;

            try
            {
                var car = driver.CurrentVehicle;
                if (car == null || !car.Exists()) return;

                // Drive mode 786603 obeys the road: lights, lanes, junctions.
                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, driver.Handle, car.Handle,
                              to.X, to.Y, to.Z, speed, 0, car.Model.Hash, 786603, 4f, true);
            }
            catch
            {
                // The game's own driving takes over.
            }
        }

        private Ped SpawnCopInCar(Vehicle car, int seat)
        {
            foreach (var name in CopModels)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !Core.Models.Ready(model)) continue;

                    var handle = Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE,
                                                    car.Handle, 6, model.Hash, seat, true, false);
                    model.MarkAsNoLongerNeeded();
                    if (handle == 0) continue;

                    var cop = (Ped)Entity.FromHandle(handle);
                    if (cop == null || !cop.Exists()) continue;

                    cop.IsPersistent = true;
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, cop.Handle, true);

                    return cop;
                }
                catch
                {
                    // Try the next model.
                }
            }

            return null;
        }

        /// <summary>
        /// One of these models, chosen at random rather than in order.
        ///
        /// Walking the list and taking the first that loads means the first entry wins every
        /// single time, so the "random" police car was always the same police car.
        /// </summary>
        private Model? PickModel(string[] names)
        {
            var order = new List<string>(names);

            for (var i = order.Count - 1; i > 0; i--)
            {
                var j = _rng.Next(i + 1);
                var swap = order[i];
                order[i] = order[j];
                order[j] = swap;
            }

            foreach (var name in order)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !Core.Models.Ready(model)) continue;

                    return model;
                }
                catch
                {
                    // Try the next.
                }
            }

            return null;
        }

        /// <summary>
        /// The parked patrol stops being scenery and comes for you.
        ///
        /// Clearing their tasks first is the important part: a ped left in a vehicle idle will
        /// happily keep idling while its own wanted response never starts, which looked like
        /// two officers ignoring a hand-to-hand a car length away.
        /// </summary>
        private void BreakOffPatrol(Ped player)
        {
            if (_patrolCops.Count == 0) return;

            foreach (var cop in _patrolCops)
            {
                if (cop == null || !cop.Exists() || !cop.IsAlive) continue;

                try
                {
                    Function.Call(Hash.CLEAR_PED_TASKS, cop.Handle);
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, cop.Handle, false);
                    Function.Call(Hash.TASK_COMBAT_PED, cop.Handle, player.Handle, 0, 16);
                }
                catch { /* the wanted system takes it from here */ }
            }

            // They are the law's problem now, not ours; the stars drive the rest.
            _patrolCops.Clear();

            try { if (_patrolCar != null && _patrolCar.Exists()) _patrolCar.MarkAsNoLongerNeeded(); }
            catch { /* teardown */ }

            _patrolCar = null;
            _patrolArrivedAt = 0;
            _patrolCrawling = false;
        }

        /// <summary>Lets the patrol go without ceremony, on teardown.</summary>
        private void ReleasePatrol()
        {
            foreach (var cop in _patrolCops)
            {
                try
                {
                    if (cop == null || !cop.Exists()) continue;
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, cop.Handle, false);
                    cop.MarkAsNoLongerNeeded();
                }
                catch { /* teardown */ }
            }
            _patrolCops.Clear();

            try { if (_patrolCar != null && _patrolCar.Exists()) _patrolCar.MarkAsNoLongerNeeded(); }
            catch { /* teardown */ }

            _patrolCar = null;
            _patrolCrawling = false;
            _patrolArrivedAt = 0;
        }

        /// <summary>Ambient speech, so a sale is something you hear as well as read.</summary>
        /// <summary>
        /// What he says as the customer turns to go. Both are in FRANKLIN_NORMAL's banks --
        /// checked against the speech list rather than guessed, because a speech name the
        /// voice does not have plays nothing at all and sounds exactly like it working.
        /// </summary>
        private static readonly string[] SoldOff = { "GENERIC_THANKS", "GENERIC_BYE" };

        private void Say(Ped ped, string[] lines)
        {
            if (ped == null || !ped.Exists() || lines.Length == 0) return;

            try
            {
                var line = lines[_rng.Next(lines.Length)];

                // Speech param SPEECH_PARAMS_FORCE gets a line out even when the ped is mid
                // task; the empty voice name makes the game use the ped's own voice.
                Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, ped.Handle, line, "SPEECH_PARAMS_FORCE");
            }
            catch
            {
                // A missing line costs nothing.
            }
        }

        /// <summary>True when a uniform is close enough to have seen the handoff.</summary>
        private static bool CopIsWatching(Ped player)
        {
            try
            {
                foreach (var ped in World.GetNearbyPeds(player, CopWitnessRange))
                {
                    if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

                    var type = Function.Call<int>(Hash.GET_PED_TYPE, ped.Handle);
                    if (type != 6 && type != 27) continue;

                    // Behind a wall does not count as watching.
                    if (!Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, ped.Handle, player.Handle, 17)) continue;

                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Cop witness scan failed: " + ex.Message);
            }

            return false;
        }

        /// <summary>Raises the wanted level, never lowers it.</summary>
        internal static void Wanted(int stars)
        {
            try
            {
                // REPORTED RATHER THAN SET, when Precinct 88 is installed.
                //
                // Handing somebody three stars is the vanilla idiom and it skips the only
                // interesting part: nobody has to have SEEN anything, there is no description
                // out, and the police start off knowing exactly where you are. Reported, the
                // same bust becomes a narcotics call at your position that has to be searched
                // for -- so the alley behind the corner is worth something, and so is not being
                // the man in the white shirt any more.
                //
                // The star count is deliberately dropped on the floor here. What a drugs call is
                // worth is Precinct 88's judgement and it has a table for it; passing our number
                // across would put this mod back in charge of the one thing it just handed over.
                if (Bridge.Present)
                {
                    var me = Game.Player.Character;

                    Bridge.Report("Dealing",
                                  me != null && me.Exists() ? me.Position : Vector3.Zero);
                    return;
                }

                if (Game.Player.Wanted.WantedLevel >= stars) return;

                Game.Player.Wanted.SetWantedLevel(stars, false);
                Game.Player.Wanted.ApplyWantedLevelChangeNow(false);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not set the wanted level: " + ex.Message);
            }
        }

        // ---- the police --------------------------------------------------------

        private void RollPolice(Ped player)
        {
            if (_cornerHeat < _cfg.PostUpHeatBeforePolice) return;

            var cop = FindCop(player) ?? SpawnCop(player);
            if (cop == null)
            {
                // Nobody to send; bleed a little so it is not stuck at the threshold.
                _cornerHeat *= 0.8f;
                return;
            }

            _cop = cop;
            State = PostState.Investigated;
            _investigateAt = Game.GameTime;

            // The call that put him on you, heard as he starts walking over. Fired here rather
            // than when he arrives, because the radio goes out before the officer does.
            // A NEW NAME FOR A NEW RECORDING. The game keeps the last clip it played open,
            // so a recording cannot be swapped under the old name while it is running;
            // the walkie-talkie went in as dispatch_radio and police_dispatch is gone.
            Voice.Cue("dispatch_radio", Voice.RadioScale);

            // Asked for while he is still walking over. A dictionary requested and checked in
            // the same frame has not loaded; the walk is the streaming budget.
            Territory.StopSearch.Want();

            try
            {
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, cop.Handle, true);

                // He is here to search you, not to shoot you.
                //
                // Blocking his events stops him REACTING to things, and that was already here.
                // What was not is turning his fighting off: an officer who walks up to a man
                // stood on a corner is a stop, and a stop that becomes a firefight because his
                // default combat attributes think a dealer is a threat is not the scene this
                // is trying to play. 5 is always-fight and 46 is fight-armed-while-unarmed;
                // both off, plus no shooting at all, and he will do the one thing he came for.
                //
                // Pull a gun on him yourself and none of this holds -- the game takes that over
                // and it should.
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, cop.Handle, 5, false);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, cop.Handle, 46, false);
                Function.Call(Hash.SET_PED_CAN_SWITCH_WEAPON, cop.Handle, false);
                Function.Call(Hash.SET_PED_ACCURACY, cop.Handle, 0);

                // And the stars stay off for the length of it. A stop-and-search that hands you
                // two stars while he is still walking over is an arrest, and the mod already
                // has one of those.
                LawHold.Hold(this);

                Function.Call(Hash.TASK_GO_TO_ENTITY, cop.Handle, player.Handle, 30000, 1.5f, 1.6f, 0, 0);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not send a cop over: " + ex.Message);
            }

            Notify.Failure("a patrol's taken an interest. Move.");
            Log.Info("Post-up drew police at corner heat " + _cornerHeat.ToString("0.0") + ".");
        }

        private Ped FindCop(Ped player)
        {
            try
            {
                foreach (var ped in World.GetNearbyPeds(player, CopScanRange))
                {
                    if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

                    var type = Function.Call<int>(Hash.GET_PED_TYPE, ped.Handle);
                    if (type != 6 && type != 27) continue;

                    return ped;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Cop scan failed: " + ex.Message);
            }

            return null;
        }

        private Ped SpawnCop(Ped player)
        {
            foreach (var name in CopModels)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage) continue;
                    if (!Core.Models.Ready(model)) continue;

                    var spot = RoundTheCorner(player);
                    if (spot == Vector3.Zero) continue;

                    var cop = World.CreatePed(model, spot);
                    model.MarkAsNoLongerNeeded();

                    if (cop == null || !cop.Exists()) continue;

                    cop.IsPersistent = true;
                    _copSpawned = true;
                    return cop;
                }
                catch (Exception ex)
                {
                    Log.Debug("Cop model '" + name + "' failed: " + ex.Message);
                }
            }

            return null;
        }

        /// <summary>
        /// Where a patrol comes from.
        ///
        /// It used to be a point on a seventy-metre circle at a random bearing, snapped to
        /// whatever pavement the game found nearest. On a corner in Davis that is regularly the
        /// far side of a six-lane road, or a yard behind a fence, and the officer then spent his
        /// thirty-second walk task failing to get to you -- so the patrol arrived, on the HUD,
        /// and never actually turned up.
        ///
        /// Round the corner instead. Several bearings are tried and scored rather than the first
        /// one taken: near enough to walk, far enough not to appear at your elbow, and the
        /// pavement it lands on has to be near the bearing that was asked for -- a snap that
        /// drags the point eighty metres has not found a pavement round this corner, it has
        /// found a different corner.
        ///
        /// Behind you wins where there is a choice. A policeman fading into existence up the
        /// road in full view is worse than one who was apparently always there, and the camera
        /// direction is the only honest test of what you can actually see.
        /// </summary>
        private Vector3 RoundTheCorner(Ped player)
        {
            var at = player.Position;

            Vector3 look;
            try { look = GameplayCamera.Direction; }
            catch { look = player.ForwardVector; }

            var best = Vector3.Zero;
            var bestScore = float.MaxValue;

            for (var tries = 0; tries < CopSpawnTries; tries++)
            {
                var angle = _rng.NextDouble() * Math.PI * 2.0;
                var reach = CopSpawnNear + (float)_rng.NextDouble() * (CopSpawnFar - CopSpawnNear);

                var want = at + new Vector3((float)Math.Cos(angle) * reach,
                                            (float)Math.Sin(angle) * reach, 0f);

                var walk = Vector3.Zero;
                try { walk = World.GetNextPositionOnSidewalk(want); }
                catch { continue; }

                if (walk == Vector3.Zero) continue;
                if (walk.DistanceTo(want) > CopSpawnDrift) continue;

                var gap = walk.DistanceTo(at);
                if (gap < CopSpawnNear * 0.6f) continue;

                // Dot of the direction to him against where the camera is pointed. Above zero
                // is in front of you, which costs him the length of the street in scoring.
                var towards = walk - at;
                towards.Z = 0f;
                towards.Normalize();

                var facing = towards.X * look.X + towards.Y * look.Y;
                var score = gap + (facing > 0.25f ? CopSpawnSeenPenalty : 0f);

                if (score >= bestScore) continue;

                bestScore = score;
                best = walk;
            }

            return best;
        }

        /// <summary>Close enough to walk it, far enough that he was not stood there.</summary>
        private const float CopSpawnNear = 34f;
        private const float CopSpawnFar = 58f;

        /// <summary>How far the pavement snap may drag the point before it is a different
        /// street rather than this one.</summary>
        private const float CopSpawnDrift = 16f;

        /// <summary>Bearings tried before taking the best of them.</summary>
        private const int CopSpawnTries = 10;

        /// <summary>What appearing in plain sight costs a candidate, in metres of scoring.</summary>
        private const float CopSpawnSeenPenalty = 45f;

        /// <summary>
        /// How long he gets to make it over before the whole thing is called off.
        ///
        /// His walk task carries thirty seconds and then simply stops, which left him stood in
        /// the road with the HUD still saying PATROL INCOMING and no way out of that state
        /// except walking away yourself. If he cannot get to you in this, he was never going to.
        /// </summary>
        private const int CopWalkTimeoutMs = 40000;

        private int _investigateAt;

        private void TickInvestigation(Ped player)
        {
            if (_cop == null || !_cop.Exists() || !_cop.IsAlive)
            {
                ReleaseCop();
                State = PostState.Posted;
                return;
            }

            // He could not get here. Called off rather than left hanging.
            if (Game.GameTime - _investigateAt > CopWalkTimeoutMs)
            {
                ReleaseCop();
                _cornerHeat *= 0.6f;
                State = PostState.Posted;

                Log.Debug("A patrol never reached the corner; called off.");
                return;
            }

            if (player.Position.DistanceTo(_cop.Position) > CopArriveRange) return;

            State = PostState.Questioned;
            _questionStartedAt = Game.GameTime;

            var takes = (int)(_cfg.PostUpSearchSeconds * 1000f);

            try
            {
                Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, _cop.Handle, player.Handle, takes);
            }
            catch { }

            // Hands up, and him going through them. Both are tasks rather than a lock: walking
            // off is still available for the whole of it, and it still costs you the stop.
            Territory.StopSearch.HandsUp(player, takes);
            Territory.StopSearch.Frisk(_cop);

            Dialogue.Say("Officer", "You been standing here a while. Mind if I check your pockets?");
        }

        private void TickQuestioning(Ped player)
        {
            if (_cop == null || !_cop.Exists() || !_cop.IsAlive)
            {
                ReleaseCop();
                State = PostState.Posted;
                return;
            }

            // Walking away IS the escape. The leash check above handles actually leaving.
            if (player.Position.DistanceTo(_cop.Position) > CopArriveRange + 3f)
            {
                ReleaseCop();
                _cornerHeat *= 0.5f;
                State = PostState.Posted;
                Notify.Ticker("~g~You stepped off before they got to you.~s~");
                return;
            }

            if (Game.GameTime - _questionStartedAt < _cfg.PostUpSearchSeconds * 1000f) return;

            Searched();
        }

        private void Searched()
        {
            var taken = 0f;
            foreach (var id in HeldIds())
            {
                taken += Stash.RemoveBulk(id, Stash.BulkOf(id));
                taken += Stash.RemovePackaged(id, Stash.PackagedOf(id));
            }

            // And whatever you were carrying to protect it with.
            //
            // A search that empties your pockets of product and hands you back the pistol that
            // was next to it is a search nobody would write. The flag takes the rounds with the
            // guns -- without it they go and the ammunition stays in a pocket nothing can see,
            // so the next one you pick up comes loaded.
            var armed = false;

            try
            {
                armed = Function.Call<bool>(Hash.IS_PED_ARMED, Game.Player.Character.Handle, 7);
                Function.Call(Hash.REMOVE_ALL_PED_WEAPONS, Game.Player.Character.Handle, true);

                // AND OFF THE LIST. This is a seizure -- the one case where losing them is the
                // whole point -- so the locker must forget them or it would hand them straight
                // back and make the search a formality.
                if (Locker != null) Locker.TakenOffHim("searched on the corner");
            }
            catch { /* he keeps them */ }

            var fine = Math.Min(Game.Player.Money, _cfg.PostUpFine);
            Cash.Take(fine);

            _state.AddRespect(-15f);
            _state.AddNotoriety(20f);
            _state.Touch();

            // He is finished, so he leaves -- back to his car if he came in one, on foot if
            // not. The task has to survive the release, or the last thing you see is a
            // policeman rooted to the pavement where he searched you.
            Territory.StopSearch.SendOff(_cop);

            ReleaseCop(true);
            Stop(null);

            Notify.Failure("searched. They took " + taken.ToString("0.#") + "g" +
                           (armed ? ", your piece" : "") + " and fined you $" +
                           fine.ToString("N0") + ".");

            Log.Info("Post-up search: lost " + taken.ToString("0.#") + "g, guns=" + armed +
                     ", fined $" + fine + ".");
        }

        private List<string> HeldIds()
        {
            var ids = new List<string>();
            var doc = Stash.ToJson();
            foreach (var k in doc["bulk"].Keys) if (!ids.Contains(k)) ids.Add(k);
            foreach (var k in doc["packaged"].Keys) if (!ids.Contains(k)) ids.Add(k);
            return ids;
        }

        // ---- cleanup -----------------------------------------------------------

        /// <summary>
        /// Somebody who has just been sold something short.
        ///
        /// Most of them swear at you and walk, which costs you the sale and a little standing.
        /// One in five decides that is not enough. He is not a gangster and he is not armed --
        /// he is somebody having a very bad afternoon -- so it is hands, and the corner notices,
        /// because a fight outside your pitch is exactly the sort of thing that gets a corner
        /// looked at.
        /// </summary>
        private void Refused(Ped player, Ped buyer)
        {
            if (buyer == null || !buyer.Exists() || !buyer.IsAlive) return;

            Say(buyer, RefusedLines);

            var chance = IsMale(buyer) ? RefusedFightChance : RefusedFightChanceFemale;

            if (_rng.NextDouble() >= chance)
            {
                try { Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, buyer.Handle, false); }
                catch { /* they will wander off on their own */ }

                return;
            }

            // Decided BEFORE anything is taken off him, because "would he normally have a gun"
            // cannot be asked of a man we have just disarmed.
            var hard = Hardened(buyer);

            try
            {
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, buyer.Handle, false);

                // The soft ones fight with their hands; a gangster keeps whatever he came with.
                if (!hard) Function.Call(Hash.REMOVE_ALL_PED_WEAPONS, buyer.Handle, true);

                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, buyer.Handle, 46, true);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, buyer.Handle, 5, true);
                // 46 is BF_CanFightArmedPedsWhenNotArmed, NOT BF_AlwaysFight. That is 5.
                Function.Call(Hash.TASK_COMBAT_PED, buyer.Handle, player.Handle, 0, 16);

                if (!hard) _swinging.Add(buyer);

                _cornerHeat += RefusedFightHeat;

                Notify.Problem(hard
                    ? "that one wants to do something about it, and he came prepared."
                    : "that one wants to do something about it.");

                Log.Info("A knocked-back buyer squared up (" +
                         (hard ? "armed, will not back down" : "hands, will back down") + ").");
            }
            catch (Exception ex)
            {
                Log.Debug("A refused buyer could not square up: " + ex.Message);
            }
        }

        /// <summary>Any weapon that is not fists and not melee. IS_PED_ARMED's 2|4.</summary>
        private const int ArmedNotMelee = 6;

        /// <summary>How far they get before they stop running, and for how long.</summary>
        private const float FleeDistance = 90f;
        private const int FleeForMs = -1;

        /// <summary>
        /// Whether this one is the sort who stands in front of a drawn gun.
        ///
        /// The rule is who he IS, not what he happens to be holding this second -- a gangster
        /// with his piece still tucked is not a member of the public having a bad afternoon,
        /// and should not scatter like one.
        ///
        /// ePedType answers it without a model list to keep up to date. 6 is police, 27 SWAT
        /// and 29 army; 7 to 18 are the game's own gang types, which every Ballas, Vagos,
        /// Families and Lost model in Los Santos is one of; 19 is a dealer and 22 a criminal.
        /// Everything else on a Chamberlain pavement is a civilian, and a civilian runs.
        ///
        /// Carrying a firearm counts too, on the grounds that a man who already has one out is
        /// self-evidently in the category however the game has him filed.
        /// </summary>
        private static bool Hardened(Ped ped)
        {
            if (ped == null || !ped.Exists()) return false;

            try
            {
                if (Function.Call<bool>(Hash.IS_PED_ARMED, ped.Handle, ArmedNotMelee)) return true;

                var type = Function.Call<int>(Hash.GET_PED_TYPE, ped.Handle);

                if (type == 6 || type == 27 || type == 29) return true;   // law and army
                if (type >= 7 && type <= 18) return true;                 // every gang type
                if (type == 19 || type == 22) return true;                // dealer, criminal

                return false;
            }
            catch
            {
                // Unknown falls to civilian, so the worst an unreadable ped costs is one man
                // running who might have stood.
                return false;
            }
        }

        /// <summary>
        /// Anyone still swinging clears off the moment you draw on them.
        ///
        /// Somebody who feels short-changed will throw a punch. He will not stand there
        /// throwing punches at a man pointing a pistol at him, and until now he did -- an
        /// unarmed civilian kept coming until one of you was on the floor, which turned every
        /// bad batch into a killing and made the purity system read as a punishment rather
        /// than a risk you take.
        ///
        /// Drawing is the whole input. Not aiming, not firing: producing the thing IS the
        /// point being made, and having to shoot somebody to make it is the outcome this
        /// exists to avoid.
        ///
        /// Only the soft ones are on this list. A gangster is left exactly as he was.
        /// </summary>
        private void ScareOff()
        {
            if (_swinging.Count == 0) return;

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) { _swinging.Clear(); return; }

            bool drawn;

            try { drawn = Function.Call<bool>(Hash.IS_PED_ARMED, player.Handle, ArmedNotMelee); }
            catch { return; }

            for (var i = _swinging.Count - 1; i >= 0; i--)
            {
                var ped = _swinging[i];

                if (ped == null || !ped.Exists() || !ped.IsAlive)
                {
                    _swinging.RemoveAt(i);
                    continue;
                }

                if (!drawn) continue;

                try
                {
                    // Both attributes off first, or the combat task is handed straight back to
                    // him -- 5 is always-fight and it outranks being told to run.
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 5, false);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 46, false);
                    Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, ped.Handle, 0, false);

                    Function.Call(Hash.TASK_SMART_FLEE_PED, ped.Handle, player.Handle,
                                  FleeDistance, FleeForMs, true, false);
                    Function.Call(Hash.SET_PED_KEEP_TASK, ped.Handle, true);

                    ped.MarkAsNoLongerNeeded();
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not scare off a swinger: " + ex.Message);
                }

                _swinging.RemoveAt(i);
            }

            if (drawn) Log.Info("Drawn on; the ones fighting with their hands cleared off.");
        }

        /// <summary>
        /// The label scale in a status bar, and half its drawn height.
        ///
        /// Down with the bars. The word is drawn INSIDE its bar, so the two cannot be tuned
        /// separately: a thinner bar with the same lettering puts the letters on its edges.
        /// </summary>
        private const float RepLabelScale = 0.26f;
        private const float RepLabelHalf = 0.0097f;

        /// <summary>The two gauges on the corner HUD, each catching up at its own pace.</summary>
        private readonly UI.Eased _heatBar = new UI.Eased();
        private readonly UI.Eased _repBar = new UI.Eased();

        /// <summary>Reputation moves over an evening, so its needle does too.</summary>
        private const float RepEaseRate = 2.5f;

        private const string HeatBlip = "HEAT";
        private const string RepBlip = "REPUTATION";

        private const string RepEndLow = "TRASH";
        private const string RepEndHigh = "CLEAN";

        /// <summary>How far inside the bar's own edge an end icon sits.</summary>
        private const float BarEndGap = 0.016f;

        /// <summary>
        /// How tall the art in a status bar is drawn.
        ///
        /// Three thousandths under the bar height, which puts a hair of the bar's own colour
        /// above and below it. Any taller and the icon is the bar.
        /// </summary>
        private const float BarIconH = 0.016f;

        /// <summary>
        /// Art for the bars: our own file first, then whatever the game has.
        ///
        /// The guessing is over. A blip cannot be drawn as a sprite -- ids address the map --
        /// the ~BLIP_~ tag draws nothing in a plain text draw, and the four skull names tried
        /// here all missed: the log has the skull landing on shop_franklin_icon_a, which is
        /// Franklin's face, and the police badge landing on a warning triangle. A search of
        /// every published dictionary dump says why. There is no skull in this game. There is
        /// no police badge either.
        ///
        /// So they are drawn and shipped in data\icons, and Draw.File puts them on screen.
        /// What is left below is the fallback for an install where those files have gone
        /// missing -- a warning triangle is at least the right shape of message -- and under
        /// that, the words.
        /// </summary>
        /// <summary>Our own art, in data\icons. Tried before anything the game ships.</summary>
        private const string SkullFile = "skull.png";
        private const string PoliceFile = "police.png";
        private const string HeartFile = "heart.png";

        private static readonly string[][] PoliceArt =
        {
            new[] { "commonmenu", "mp_alerttriangle" },
        };

        private static readonly string[][] SkullArt =
        {
            new[] { "commonmenu", "mp_alerttriangle" },
        };

        private static readonly string[][] HeartArt =
        {
            new[] { "commonmenu", "shop_health_icon_a" },
            new[] { "commonmenu", "shop_tick_icon" },
        };

        private static string[] _policeArt, _skullArt, _heartArt;
        private static bool _policeDone, _skullDone, _heartDone;

        /// <summary>
        /// Draws one of the bar icons, and says whether it managed to.
        ///
        /// Resolved once and remembered both ways round. HasTexture is a native call, this runs
        /// every frame the corner is up, and a name that never resolves has to be written off
        /// rather than re-asked -- which is exactly what one wheel icon did for 6,768 lines of
        /// a single session's log.
        /// </summary>
        private static bool BarIcon(string file, string[][] candidates, ref string[] found,
                                    ref bool done, string what, float x, float y, Color tint)
        {
            // Ours first. It is the one that is actually the thing it is supposed to be.
            if (file != null && Hud.File(file, x, y, BarIconH, 0f, tint)) return true;

            if (!done)
            {
                done = true;

                foreach (var pair in candidates)
                {
                    if (!Hud.HasTexture(pair[0], pair[1])) continue;

                    found = pair;
                    Log.Info("Bar icon " + what + ": " + pair[0] + "/" + pair[1] + ".");
                    break;
                }

                if (found == null) Log.Info("No " + what + " bar icon in this install; using the word.");
            }

            if (found == null) return false;

            Hud.Sprite(found[0], found[1], x, y, Hud.ToX(BarIconH), BarIconH, 0f, tint);
            return true;
        }

        /// <summary>
        /// The two ends of the reputation bar, INSIDE it.
        ///
        /// A skull at the bad end and a heart at the good one, so the bar reads as a scale
        /// between two things rather than a number with a word on it. Inside rather than
        /// outside because that is where they belong on a bar this tall -- the fill passes
        /// behind them, which is the point: the skull end fills first when it is going badly.
        /// </summary>
        private void BarEnds(string low, string high, float x, float cy, float width)
        {
            var edge = width * 0.5f - BarEndGap;

            // WHITE, both of them, and that is the whole point.
            //
            // The skull was drawn in Palette.Danger and the clean end in Palette.Cash -- which
            // are the two colours the bar itself fills with. So each icon disappeared into the
            // fill in exactly the direction it was there to warn about: the skull invisible on
            // a red bar, the heart invisible on a green one, each of them legible only while
            // the thing it marks was not happening.
            //
            // The bar is amber, green, red or dark grey depending on where you stand. White is
            // the one ink that reads on all four, and it is what the police icon in the middle
            // has always used.
            // A dark disc under each, because white alone does not do it.
            //
            // Measured rather than assumed: white on the green fill is 2.1:1 and on the amber
            // 1.9:1, both under the 3:1 a graphic needs to be told apart from its background.
            // The fill slides under these icons and changes colour as it goes, so there is no
            // single ink that works on all of it -- the answer is to give the icon a ground of
            // its own instead of a better colour.
            Hud.Disc(x - edge, cy, BarEndDisc, BarEndShade);
            Hud.Disc(x + edge, cy, BarEndDisc, BarEndShade);

            if (!BarIcon(SkullFile, SkullArt, ref _skullArt, ref _skullDone, "skull",
                         x - edge, cy, BarInk))
            {
                Hud.Text(low, x - edge, cy - RepEndHalf, RepEndScale, BarInk, Hud.FontLabel);
            }

            if (!BarIcon(HeartFile, HeartArt, ref _heartArt, ref _heartDone, "heart",
                         x + edge, cy, BarInk))
            {
                Hud.Text(high, x + edge, cy - RepEndHalf, RepEndScale, BarInk, Hud.FontLabel);
            }
        }

        /// <summary>
        /// The ink for anything drawn ON a status bar rather than beside it.
        ///
        /// Not pure white: a hair off it, so it sits with the rest of the HUD rather than
        /// glowing out of it, and fully opaque so a fill sliding underneath cannot wash it out.
        /// </summary>
        private static readonly Color BarInk = Color.FromArgb(255, 250, 250, 248);

        /// <summary>The dark ground each end icon sits on, and how far it reaches.</summary>
        private static readonly Color BarEndShade = Color.FromArgb(225, 10, 11, 13);
        private const float BarEndDisc = 0.0092f;

        private const float RepEndScale = 0.22f;
        private const float RepEndHalf = 0.0082f;

        /// <summary>
        /// What goes in the middle of a status bar.
        ///
        /// The heat bar gets a police icon; the reputation bar keeps its name, because the two
        /// ends already say what it measures and a third symbol in the middle of three is a
        /// row of pictograms rather than a bar.
        ///
        /// Hud.Text places by the TOP of the line, so the half-line offset is what centres the
        /// word rather than hanging it off the bar's middle.
        /// </summary>
        private void BarLabel(string blip, string word, float x, float cy)
        {
            if (word == "HEAT" &&
                BarIcon(PoliceFile, PoliceArt, ref _policeArt, ref _policeDone, "police",
                        x, cy, BarInk))
            {
                return;
            }

            Hud.Text(word, x, cy - RepLabelHalf, RepLabelScale, BarInk, Hud.FontChaletLondon);
        }

        /// <summary>Where the heat bar turns amber and where it turns red, as fractions of it.</summary>
        private const float HeatWarm = 0.4f;
        private const float HeatWarn = 0.75f;

        /// <summary>
        /// One status bar: ground, the eased fill, and nothing that moves by itself.
        ///
        /// THIS USED TO HAVE FOUR MORE THINGS ON IT and every one of them was a mistake. A halo
        /// in the fill's own colour so the bar had an edge; a soft ground under the whole block
        /// so the small type held up; a band of light crossing the fill every couple of
        /// seconds; a pale ghost running ahead of the fill to where it was heading. Each is
        /// defensible on its own. Together they were two glowing coloured rectangles sat on top
        /// of each other with the light in them constantly moving -- and on a HUD you look at
        /// while somebody walks up to buy something, the only thing any of it said was that the
        /// screen was busy.
        ///
        /// So what is left is what a bar is: a dark edge, a dark ground, the fill, one lighter
        /// band across the top of the fill so it has a surface, a bright edge at the end of it,
        /// and the marks. Six rectangles, none of them moving. The bar still eases -- that is
        /// the fill going somewhere, which is information -- and the wordmark above it still
        /// pulses for a sale or the law, which is one moving thing rather than five.
        /// </summary>
        private static void Bar(float x, float cy, float w, float h, float eased, Color fill)
        {
            var left = x - w * 0.5f;
            var filled = w * Math.Max(0f, Math.Min(1f, eased));

            Hud.Rect(x, cy, w + 0.004f, h + 0.004f, Color.FromArgb(200, 8, 8, 10));
            Hud.Rect(x, cy, w, h, Color.FromArgb(170, 26, 28, 30));

            if (filled > 0f)
            {
                Hud.Rect(left + filled * 0.5f, cy, filled, h, fill);

                // One lighter band across the top of the fill, so it has a surface. The only
                // thing drawn over the fill, and it runs with the fill rather than across it.
                Hud.Rect(left + filled * 0.5f, cy - h * 0.26f, filled, h * 0.44f, Color.FromArgb(30, 255, 255, 255));
            }
        }

        /// <summary>A scuffle outside your pitch is its own kind of attention.</summary>
        private const float RefusedFightHeat = 6f;

        /// <summary>Somewhere between two colours, for a mark that is changing its mind.</summary>
        private static Color Mix(Color from, Color to, float k)
        {
            if (k < 0f) k = 0f;
            if (k > 1f) k = 1f;

            return Color.FromArgb(255,
                (int)(from.R + (to.R - from.R) * k),
                (int)(from.G + (to.G - from.G) * k),
                (int)(from.B + (to.B - from.B) * k));
        }

        /// <summary>
        /// Whether this one is a man.
        ///
        /// IS_PED_MALE covers every model without a list to maintain, and a ped the game will
        /// not answer for is treated as a man -- the fight is the common case now, so an
        /// unknown falling that way keeps the corner behaving consistently.
        /// </summary>
        private static bool IsMale(Ped ped)
        {
            try
            {
                return ped != null && ped.Exists() &&
                       Function.Call<bool>(Hash.IS_PED_MALE, ped.Handle);
            }
            catch
            {
                return true;
            }
        }

        private void ReleaseCustomer()
        {
            // First, whatever owns him has him back -- and its own settle pass is what walks
            // him to his mark and starts him on what he was doing before you called him over.
            Serving.Done(_customer);

            if (_customer != null && _customer.Exists())
            {
                try
                {
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, _customer.Handle, false);
                    _customer.Task.ClearAll();
                    _customer.MarkAsNoLongerNeeded();
                }
                catch { }
            }
            _customer = null;
            _animRequested = false;
        }

        private void ReleaseCop(bool keepTask = false)
        {
            // However the stop ended -- searched, walked away from, or the man himself gone.
            // A hold that outlives the thing holding it is a city with no police in it.
            try { LawHold.Release(this); }
            catch { /* it was not held */ }

            if (_cop != null && _cop.Exists())
            {
                try
                {
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, _cop.Handle, false);

                    // Unless he has just been told to drive away, in which case clearing his
                    // tasks is cancelling the only instruction that matters.
                    if (!keepTask) _cop.Task.ClearAll();
                    // One we put there is one we take away. A cop conjured up to walk over and
                    // search you has no life outside this pitch, and leaving him to wander the
                    // neighbourhood afterwards slowly fills the block with officers who arrived
                    // for a corner that no longer exists. Anybody who was already on the street
                    // is simply let go.
                    if (_copSpawned && !keepTask && !_cop.IsOnScreen) _cop.Delete();
                    else _cop.MarkAsNoLongerNeeded();
                }
                catch { }
            }
            _cop = null;
            _copSpawned = false;
        }

        public void Prune()
        {
            if (_served.Count < 150) return;
            _served.Clear();
        }

        public void RestoreWorld()
        {
            // The bag first. Unloading the script with it still on him would leave Franklin
            // wearing a duffle for the rest of the save, which is the mod not cleaning up
            // after itself in the most visible way possible.
            DropTheBag();

            // And the little one, which is a different bag entirely -- the one that changes
            // hands on a sale. Same reasoning, smaller prop.
            DropBaggie();

            ReleaseCustomer();
            ReleaseCop();
            ReleaseRivals();
            ReleasePatrol();

            foreach (var ped in _swinging)
            {
                try
                {
                    if (ped != null && ped.Exists()) ped.MarkAsNoLongerNeeded();
                }
                catch { /* teardown */ }
            }

            _swinging.Clear();
            State = PostState.Idle;
            _product = null;
        }

        /// <summary>
        /// Lets go of anyone sent after you. They are left alive and in the world -- a fight
        /// that vanishes mid-punch is worse than one that finishes -- but stop being ours.
        /// </summary>
        private void ReleaseRivals()
        {
            foreach (var ped in _rivals)
            {
                try
                {
                    if (ped == null || !ped.Exists()) continue;
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);
                    ped.MarkAsNoLongerNeeded();
                }
                catch { /* teardown */ }
            }
            _rivals.Clear();

            try
            {
                if (_driveByCar != null && _driveByCar.Exists()) _driveByCar.MarkAsNoLongerNeeded();
            }
            catch { /* teardown */ }

            _driveByCar = null;
            _driveByDriver = null;
            _driveByBailed = false;
            _driveByInRangeAt = 0;
        }

        // ---- hud ---------------------------------------------------------------

        /// <summary>Corner readout: what you are moving, how busy it is, and how hot.</summary>
        public void Draw()
        {
            if (!IsPosted) return;

            // Turned off in the settings. Only the drawing stops -- everything the panel was
            // reporting on carries on happening, which is the whole point of being able to
            // switch it off rather than switch the corner off.
            if (_cfg != null && !_cfg.ShowDealHud) return;

            const float x = 0.5f;

            // Lifted again, because both bars now carry their own label and the block of text
            // under them would otherwise run off the bottom of the screen.
            const float y = 0.82f;
            const float w = 0.20f;

            // The same height as the reputation bar under it. They are a pair and a pair of
            // different heights reads as one of them mattering more.
            //
            // Thinner than they were. At 0.030 each they were a block of furniture across the
            // middle of the screen; the pair only has to be readable, and everything inside
            // them -- the icons, the label -- came down with them rather than being squeezed.
            const float h = 0.022f;

            // Clear air between the two bars, so they read as a pair rather than as one thick
            // bar with a line through it.
            const float RepBarGap = 0.004f;



            // EASED, both bars. See UI.Eased.
            //
            // A sale adds heat in one lump and the bar used to arrive at its new length on the
            // same frame -- which reads as the bar being redrawn wrong rather than as heat
            // going up. The number is the same; what changes is that you can now see it move,
            // which is the only reason a bar exists instead of a figure.
            //
            // The COLOUR is taken from the eased value too, so a bar sliding up through the
            // threshold changes colour when it gets there rather than the instant the sale
            // lands, several tenths before the fill catches up with it.
            var heat = _heatBar.To(Math.Min(1f, _cornerHeat / Math.Max(1f, _cfg.PostUpHeatBeforePolice)));
            var colour = heat > HeatWarn ? Palette.Danger : heat > HeatWarm ? Palette.Warn : Palette.Cash;

            Bar(x, y, w, h, heat, colour);

            // The blip itself, drawn inline in the string.
            //
            // A sprite id cannot be handed to DRAW_SPRITE -- ids address the map -- but the
            // blip ART can be put in TEXT with a ~BLIP_~ tag, which is the one route to it.
            // radar_police_chase is 42, so the tag is BLIP_POLICE_CHASE.
            //
            // Behind a switch, because the tag is documented for "help messages and other
            // supported contexts" and that is not a promise about this one -- an unsupported
            // tag renders as literal text. BlipsInBars=false in Hoodrich.ini puts the names
            // back.
            BarLabel(HeatBlip, "HEAT", x, y);

            // And underneath it, what the block reckons of your product.
            //
            // Paired with the heat bar rather than put somewhere else on screen, because the
            // two are the same kind of thing: one is how long you can stand here, the other is
            // whether anybody is going to walk up while you do. Thinner, so it reads as the
            // quieter of the two at a glance.
            // Slower than the heat bar, and that is the point of them being different.
            //
            // Heat is a thing happening to you now and should keep up. Your name is a thing
            // that moves by degrees over an evening, and a bar that eases into it says so
            // without a word of explanation.
            var rep = _repBar.To(_state == null ? 1f : _state.ProductRep, RepEaseRate);

            // Blue above the middle, red below it. The middle is where you start, so the two
            // colours are the two things that can happen to you rather than a scale from good
            // to bad.
            //
            // Blue rather than green, which it used to be. Money is green, the set is green and
            // the wordmark over the top of this is green -- reputation reading in the same
            // colour made a fourth thing that looked like the other three, and the sentence
            // under the bar is the one line on this HUD that is a verdict on you rather than a
            // number. It gets to look like nothing else.
            // The BAR runs the whole sweep -- red, orange, yellow, yellow-green, green -- so
            // its colour is the reading rather than a verdict on the reading. Two states could
            // only ever say better-or-worse-than-average; a sweep says how much.
            var repBar = Palette.Spectrum(rep);

            // The sentence keeps its own colour and does not follow the bar.
            //
            // It is a different job. The bar is a quantity and the line under it is a verdict,
            // and a verdict that changes colour by degrees is a verdict hedging. It also stays
            // out of the greens on purpose: this HUD already has money, the set and the
            // wordmark in green, and the one line that is somebody's opinion of you is the last
            // thing that should look like all of them.
            var repColour = rep >= PlayerState.Neutral ? Palette.Standing : Palette.Danger;

            // Tall enough to hold its own name. The label is drawn inside the bar, so the bar
            // has to be taller than the text or the letters sit on its edges -- which is the
            // floor on how thin this one can go, not a preference.
            const float repH = 0.022f;
            var repY = y + h * 0.5f + RepBarGap + repH * 0.5f;

            Bar(x, repY, w, repH, rep, repBar);

            // The same treatment as the heat bar above it. radar_community_series is 835.
            BarLabel(RepBlip, "REPUTATION", x, repY);

            // And what each end of it means, in blips.
            //
            // radar_trashbag on the left and radar_pickup_dtb_health on the right, so the bar
            // reads as a scale between two things rather than as a number with a word on it --
            // rubbish at one end, clean product at the other, and the fill tells you which way
            // the block has you.
            BarEnds(RepEndLow, RepEndHigh, x, repY, w);

            // Two lines rather than one. "POSTED UP" is the state you are in and the product
            // is what you happen to be moving while in it, so they are not the same sentence --
            // and the state reads better in the house script face above the detail.
            // How tall the wordmark stands in for the words it replaced.
            const float StateMarkHeight = 0.024f;

            // When the last sale landed, for the mark's pulse, and how long it lasts.
            // Long enough to see, short enough not to nag.

            // Whether the LAW is interested, which is not the same as "not idle".
            //
            // The test used to be State == Posted, and there are two perfectly ordinary states
            // that are not: Approaching, while somebody walks over, and Dealing, mid-handoff.
            // So the moment a customer set off the wordmark dropped back to typeset words and
            // only came back once the sale finished -- which is exactly "it worked, but only
            // after a deal". Those two are business as usual; heat is the two below.
            // Named apart from the corner-heat number a few lines up, which is a float in the
            // same scope and means how much attention you have drawn rather than whether
            // anybody has come over about it.
            var lawOnYou = State == PostState.Investigated || State == PostState.Questioned;

            var detail = lawOnYou
                ? (State == PostState.Questioned ? "BEING SEARCHED" : "PATROL INCOMING")
                : _product == null ? "" : "SELLING " + _product.Name.ToUpperInvariant();

            var tint = lawOnYou ? Palette.Danger : Palette.Text;

            // The wordmark itself when you are simply posted up, and words when something is
            // going wrong. "POSTED UP" is the mod's own name and it was being SET in a font;
            // the two states either side of it are warnings and have to stay readable text,
            // which is why this is a branch rather than a straight swap.
            //
            // Brand places by its left edge, so the width is worked out and halved rather than
            // guessed -- the aspect is a constant the generator prints.
            // The mark, always, and RED when the law is interested.
            //
            // It does not need a second file to do that. The wordmark is a white mask and the
            // colour goes on at draw time, the same way every icon in this mod is coloured --
            // so red is a tint rather than an asset, and one file cannot fall out of step with
            // the other.
            //
            // The warning itself moves to the line underneath, where the product name sits the
            // rest of the time. Nothing is lost: the mark turning red is the alarm and the word
            // below it says which alarm.
            // And it MOVES when somebody buys.
            //
            // The mark going red is now correctly rare -- it means the law is interested rather
            // than "you are busy", which is what it had drifted into meaning. That left a
            // corner where nothing on screen acknowledged the thing you are stood there to do.
            //
            // So a sale swells it for two thirds of a second and warms it toward the money
            // colour on the way. Sine rather than a linear ramp, so it comes back down as
            // smoothly as it went up -- a mark that snaps back to normal reads as a glitch.
            // Heat still wins the colour: an alarm is not something to be cheerful over.
            var mark = StateMarkHeight;
            var pop = tint;

            // One movement, three colours, and which one you get is what just happened.
            //
            // A swell that goes up and comes back down is what a thing FINISHING looks like, so
            // both endings share it: green for a sale, red for one that walked. The third state
            // is not an ending at all -- somebody is on their way over and has not decided yet
            // -- so it does not swell, it breathes, and it does that in amber because amber is
            // the colour of a thing that has not gone either way.
            var flashAt = _spookedAt != 0 ? _spookedAt : _soldAt;

            if (lawOnYou)
            {
                // An alarm, and alarms move. Red on its own is a colour you stop seeing after
                // the second time; red that throbs is the same information you cannot ignore.
                //
                // Faster than the walk-up breathe and harder at the top, because the two are
                // opposite instructions. One says somebody is coming to buy and the other says
                // somebody is coming to search you, and they should not read as the same tempo.
                var beat = (Game.GameTime % LawPulseMs) / (float)LawPulseMs;
                var hot = 0.5f + 0.5f * (float)Math.Sin(beat * Math.PI * 2.0);

                mark = StateMarkHeight * (1f + 0.13f * hot);
                pop = Mix(Palette.Danger, Color.FromArgb(255, 255, 196, 186), hot);

                // Whatever the corner was doing before the patrol turned up, it is not the
                // headline any more. Cleared rather than paused, so a sale that landed a moment
                // before the alarm cannot come back and pulse green after it clears.
                _soldAt = 0;
                _spookedAt = 0;
            }
            else if (flashAt != 0)
            {
                var since = Game.GameTime - flashAt;

                if (since > SalePulseMs)
                {
                    _soldAt = 0;
                    _spookedAt = 0;
                }
                else
                {
                    var k = (float)Math.Sin(Math.PI * (since / (double)SalePulseMs));

                    mark = StateMarkHeight * (1f + 0.22f * k);

                    if (!lawOnYou)
                    {
                        pop = Mix(Palette.Text, _spookedAt != 0 ? Palette.Danger : Palette.Cash, k);
                    }
                }
            }
            else if (State == PostState.Approaching && !lawOnYou)
            {
                // Somebody is walking over. A full sine on a loop rather than a one-shot, and
                // it never reaches white -- the mark is doing something for as long as they are
                // still coming, which is the entire information here.
                var t = (Game.GameTime % WalkUpPulseMs) / (float)WalkUpPulseMs;
                var k = 0.5f + 0.5f * (float)Math.Sin(t * Math.PI * 2.0);

                mark = StateMarkHeight * (1f + 0.09f * k);
                pop = Mix(Palette.Text, Palette.Warn, 0.45f + 0.55f * k);
            }

            // Grown from the middle, so it swells rather than drops.
            Hud.BrandCentre(x, y - 0.077f - (mark - StateMarkHeight) * 0.5f, mark, pop);

            // Where you stand, directly under the bar it belongs to, and without a prefix --
            // the bar says REPUTATION, so repeating it here said the word twice in two inches.
            // The house script, like every other title in the mod. It is a verdict on you
            // rather than a readout, so it reads as handwriting rather than as a label -- and
            // the scale goes up with it, because a script face at a label's size is a smudge.
            // Not shouted. A script face set in capitals is two decisions fighting each other:
            // handwriting is the informal one and block capitals are the formal one, and a
            // verdict somebody would say out loud should look like it was said rather than
            // printed.
            Hud.Text(_state == null ? "" : _state.ProductRepWord,
                     x, y + 0.054f, 0.38f, repColour, Hud.FontCursive);

            if (!string.IsNullOrEmpty(detail))
            {
                Hud.Text(detail, x, y - 0.052f, 0.30f,
                         lawOnYou ? Palette.Danger : Palette.TextDim, Hud.FontLabel);
            }

            // What is left is the number that decides whether you stay, so it goes first and
            // turns amber as it runs down.
            var left = _product == null ? 0f : Stash.PackagedOf(_product.Id);

            // How many more of them there are in it, in whatever the product is measured in.
            var perSale = _product != null && _product.Counted ? 2f : 1.5f;
            var lots = (int)(left / perSale);

            Hud.Text((_product == null ? "0" : _product.Amount(left)) +
                     " left  ·  " + lots + " more sale" + (lots == 1 ? "" : "s"),
                     x, y + 0.082f, 0.30f,
                     left < 7f ? Palette.Warn : Palette.Cash, Hud.FontBody);

            Hud.Text(Footfall + " passing  ·  " + _sales + " sold  ·  $" + _earned.ToString("N0"),
                     x, y + 0.106f, 0.28f, Palette.TextDim, Hud.FontBody);
        }
    }
}
