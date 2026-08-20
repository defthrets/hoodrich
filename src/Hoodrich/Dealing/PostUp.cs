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
        /// How far you can drift from the pitch before you stop working it. Generous on purpose:
        /// being frozen to one tile made the whole thing feel like a menu, and left the player
        /// stuck in the scenario when stock ran out.
        /// </summary>
        private const float LeashDistance = 20f;
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
        private const float RefusedFightChance = 0.20f;

        /// <summary>A buyer who cannot reach you gives up after this and wanders off.</summary>
        private const int ApproachTimeoutMs = 60000;

        /// <summary>Said by the player once the handoff lands.</summary>
        private static readonly string[] SellerLines =
        {
            "GENERIC_BYE", "GENERIC_THANKS", "GENERIC_HOWS_IT_GOING"
        };

        private readonly Settings _cfg;
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

        private int _sales;
        private int _earned;

        /// <summary>When this pitch started, for the drive-by clock.</summary>
        private int _postedAt;

        /// <summary>Rival cars sent so far, so one pitch cannot spawn a convoy.</summary>
        private int _driveBys;

        private readonly List<Ped> _rivals = new List<Ped>();
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

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return "Not right now.";
            if (player.IsInVehicle()) return "Get out of the car first.";

            _product = product;
            _anchor = player.Position;
            _cornerHeat = 0f;
            _sales = 0;
            _earned = 0;
            _lastScan = 0;
            _postedAt = Game.GameTime;
            SchedulePatrol();
            if (Crew != null) Crew.WorkingACorner = true;
            _driveBys = 0;
            State = PostState.Posted;

            CarryTheBag(player);


            Notify.Ticker("~g~Posted up.~s~ Moving " + product.Name.ToLowerInvariant() +
                          ". A busy sidewalk sells faster and burns hotter.");
            Log.Info("Posted up with " + product.Id + " at " + _anchor + ".");
            return null;
        }

        /// <summary>
        /// The bag, while he is working.
        ///
        /// Component 5 is the slot the game keeps a ped's bag in -- the same one the heists put
        /// a duffle in. So this is not a prop stuck to his back, it is the bag the model already
        /// has, which means it moves with him and survives everything the game does to a player
        /// ped.
        ///
        /// What was there before is remembered rather than assumed to be nothing, because
        /// somebody in a heist outfit is already wearing something in that slot and taking it
        /// off him would be this mod undressing him.
        /// </summary>
        private void CarryTheBag(Ped player)
        {
            if (_bagOn) return;

            try
            {
                if (player == null || !player.Exists()) return;

                _bagWas = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, player.Handle, BagSlot);
                _bagTexWas = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, player.Handle, BagSlot);

                // Already carrying something. Leave it -- he can work with his own bag.
                if (_bagWas > 0) return;

                Function.Call(Hash.SET_PED_COMPONENT_VARIATION, player.Handle,
                              BagSlot, Duffle, 0, 0);

                _bagOn = true;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not put the bag on: " + ex.Message);
            }
        }

        /// <summary>Puts him back exactly as he was.</summary>
        private void DropTheBag()
        {
            if (!_bagOn) return;
            _bagOn = false;

            try
            {
                var player = Game.Player.Character;
                if (player == null || !player.Exists()) return;

                Function.Call(Hash.SET_PED_COMPONENT_VARIATION, player.Handle,
                              BagSlot, _bagWas, _bagTexWas, 0);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not take the bag off: " + ex.Message);
            }
        }

        /// <summary>PED_COMPONENT_HAND -- the slot a ped's bag lives in.</summary>
        private const int BagSlot = 5;

        /// <summary>The duffle. Drawable 1 on this slot is a bag on every player model.</summary>
        private const int Duffle = 1;

        private bool _bagOn;
        private int _bagWas;
        private int _bagTexWas;

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

        public void Update()
        {
            if (!IsPosted) return;

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
        private static void PlayHandoff(Ped player, Ped buyer)
        {
            PlayAnim(player, AnimPlayer);
            PlayAnim(buyer, AnimBuyer);
        }

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
                Notify.Problem("they clocked the cut.");

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
            Game.Player.Money += payout;

            _sales++;
            _earned += payout;

            if (Social != null)
            {
                Social.On(payout >= 400 ? SocialEvent.BigSale : SocialEvent.Sale,
                          product.Name, payout);
            }

            _state.AddRespect(1f + product.Tier * 0.4f);
            _state.GramsSold += sold;
            _state.TotalDealsMade++;
            _state.TotalEarned += payout;
            _state.Touch();

            // Heat is per-sale AND scaled by how public the spot is.
            var crowdFactor = 1f + Footfall * _cfg.PostUpHeatPerWitness;
            var heat = product.HeatFactor * crowdFactor * HeatRate *
                       (Turf == null ? 1f : Turf.TurfHeatMultiplier);

            _cornerHeat += heat;
            _state.AddNotoriety(heat * 0.5f);
            Turf?.MarkExposed();

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
                catch { }

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
                Bust.OnSale(customer, product);
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
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1000)) continue;

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
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1200)) continue;

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
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) continue;

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

            try
            {
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, cop.Handle, true);
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
                    if (!model.Request(1200)) continue;

                    var angle = _rng.NextDouble() * Math.PI * 2.0;
                    var spot = player.Position + new Vector3(
                        (float)Math.Cos(angle) * 70f, (float)Math.Sin(angle) * 70f, 0f);

                    try { spot = World.GetNextPositionOnSidewalk(spot); } catch { }
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

        private void TickInvestigation(Ped player)
        {
            if (_cop == null || !_cop.Exists() || !_cop.IsAlive)
            {
                ReleaseCop();
                State = PostState.Posted;
                return;
            }

            if (player.Position.DistanceTo(_cop.Position) > CopArriveRange) return;

            State = PostState.Questioned;
            _questionStartedAt = Game.GameTime;

            try
            {
                Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, _cop.Handle, player.Handle,
                              (int)(_cfg.PostUpSearchSeconds * 1000f));
            }
            catch { }

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

            var fine = Math.Min(Game.Player.Money, _cfg.PostUpFine);
            Game.Player.Money -= fine;

            _state.AddRespect(-15f);
            _state.AddNotoriety(20f);
            _state.Touch();

            ReleaseCop();
            Stop(null);

            Notify.Failure("searched. They took " + taken.ToString("0.#") + "g and fined you $" +
                           fine.ToString("N0") + ".");
            Log.Info("Post-up search: lost " + taken.ToString("0.#") + "g, fined $" + fine + ".");
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

            if (_rng.NextDouble() >= RefusedFightChance)
            {
                try { Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, buyer.Handle, false); }
                catch { /* he will wander off on his own */ }

                return;
            }

            try
            {
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, buyer.Handle, false);
                Function.Call(Hash.REMOVE_ALL_PED_WEAPONS, buyer.Handle, true);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, buyer.Handle, 46, true);
                Function.Call(Hash.TASK_COMBAT_PED, buyer.Handle, player.Handle, 0, 16);

                _cornerHeat += RefusedFightHeat;

                Notify.Problem("that one wants to do something about it.");
                Log.Info("A knocked-back buyer squared up.");
            }
            catch (Exception ex)
            {
                Log.Debug("A refused buyer could not square up: " + ex.Message);
            }
        }

        /// <summary>A scuffle outside your pitch is its own kind of attention.</summary>
        private const float RefusedFightHeat = 6f;

        private void ReleaseCustomer()
        {
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

        private void ReleaseCop()
        {
            if (_cop != null && _cop.Exists())
            {
                try
                {
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, _cop.Handle, false);
                    _cop.Task.ClearAll();
                    // One we put there is one we take away. A cop conjured up to walk over and
                    // search you has no life outside this pitch, and leaving him to wander the
                    // neighbourhood afterwards slowly fills the block with officers who arrived
                    // for a corner that no longer exists. Anybody who was already on the street
                    // is simply let go.
                    if (_copSpawned && !_cop.IsOnScreen) _cop.Delete();
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

            ReleaseCustomer();
            ReleaseCop();
            ReleaseRivals();
            ReleasePatrol();
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

            const float x = 0.5f;
            const float y = 0.86f;
            const float w = 0.20f;
            const float h = 0.016f;

            var heat = Math.Min(1f, _cornerHeat / Math.Max(1f, _cfg.PostUpHeatBeforePolice));
            var colour = heat > 0.75f ? Palette.Danger : heat > 0.4f ? Palette.Warn : Palette.Cash;

            Hud.Rect(x, y, w + 0.004f, h + 0.004f, Color.FromArgb(190, 8, 8, 10));
            Hud.Rect(x, y, w, h, Color.FromArgb(160, 30, 32, 34));

            var filled = w * heat;
            Hud.Rect(x - (w - filled) * 0.5f, y, filled, h, colour);

            // Two lines rather than one. "POSTED UP" is the state you are in and the product
            // is what you happen to be moving while in it, so they are not the same sentence --
            // and the state reads better in the house script face above the detail.
            var state = State == PostState.Questioned ? "BEING SEARCHED"
                : State == PostState.Investigated ? "PATROL INCOMING"
                : "POSTED UP";

            var detail = _product == null ? "" : "SELLING " + _product.Name.ToUpperInvariant();

            var tint = State == PostState.Posted ? Palette.Text : Palette.Danger;

            Hud.Text(state, x, y - 0.072f, 0.62f, tint, Hud.FontCursive);

            if (!string.IsNullOrEmpty(detail))
            {
                Hud.Text(detail, x, y - 0.036f, 0.30f, Palette.TextDim, Hud.FontLabel);
            }

            // What is left is the number that decides whether you stay, so it goes first and
            // turns amber as it runs down.
            var left = _product == null ? 0f : Stash.PackagedOf(_product.Id);

            // How many more of them there are in it, in whatever the product is measured in.
            var perSale = _product != null && _product.Counted ? 2f : 1.5f;
            var lots = (int)(left / perSale);

            Hud.Text((_product == null ? "0" : _product.Amount(left)) +
                     " left  ·  " + lots + " more sale" + (lots == 1 ? "" : "s"),
                     x, y + 0.024f, 0.30f,
                     left < 7f ? Palette.Warn : Palette.Cash, Hud.FontBody);

            Hud.Text(Footfall + " passing  ·  " + _sales + " sold  ·  $" + _earned.ToString("N0"),
                     x, y + 0.048f, 0.28f, Palette.TextDim, Hud.FontBody);
        }
    }
}
