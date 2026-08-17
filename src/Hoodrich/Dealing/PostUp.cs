using System;
using System.Collections.Generic;
using System.Drawing;
using Control = GTA.Control;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
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

        /// <summary>A sale this close to a uniform is a sale a uniform saw.</summary>
        private const float CopWitnessRange = 25f;

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

        /// <summary>How long they get to shoot from the car before they get out and fight.</summary>
        private const int DriveByShootMs = 14000;

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
        public Affiliation Crew;

        private DrugDef _product;
        private Vector3 _anchor;
        private int _lastScan;

        private Ped _customer;
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

        public float CornerHeat => _cornerHeat;

        public int Footfall { get; private set; }

        private Stash Stash => _state.Stash;

        // ---- starting and stopping ---------------------------------------------

        /// <summary>Returns a player-facing refusal, or null once posted.</summary>
        public string Start(DrugDef product)
        {
            if (IsPosted) return "You are already posted up.";
            if (product == null) return "Pick something to move.";
            if (Stash.PackagedOf(product.Id) < 0.5f)
            {
                return Stash.BulkOf(product.Id) > 0.005f
                    ? "That is still bulk. " + product.SplitVerb + " it first."
                    : "You are not holding any " + product.Name + ".";
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


            Notify.Ticker("~g~Posted up.~s~ Moving " + product.Name.ToLowerInvariant() +
                          ". Busy pavement sells faster and burns hotter.");
            Log.Info("Posted up with " + product.Id + " at " + _anchor + ".");
            return null;
        }

        public void Stop(string reason)
        {
            if (!IsPosted) return;

            var sales = _sales;
            var earned = _earned;

            ReleaseCustomer();
            ReleaseCop();

            State = PostState.Idle;
            _product = null;
            _cornerHeat = 0f;
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



            


            switch (State)            {                case PostState.Dealing:
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
            if (!ped.IsHuman || ped.IsInVehicle() || ped.IsInCombat || ped.IsRagdoll) return false;

            var type = Function.Call<int>(Hash.GET_PED_TYPE, ped.Handle);
            if (type == 6 || type == 27 || type == 29) return false; // police never buy

            if (Crew != null && Crew.IsRival(ped)) return false;

            if (!ignoreCooldown && _served.ContainsKey(ped.Handle)) return false;

            return true;
        }

        private void RollCustomer(Ped player)
        {
            // Each passer-by gets their own roll, so a busy pavement really is busier.
            var chance = 1f - (float)Math.Pow(1f - _cfg.PostUpApproachChance / 100f, Footfall);
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

            if (player.Position.DistanceTo(_customer.Position) > 2.2f) return;

            State = PostState.Dealing;
            _dealStartedAt = Game.GameTime;
            _animRequested = true;

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
                _animRequested = false;
                PlayAnim(player, AnimPlayer);
                PlayAnim(_customer, AnimBuyer);
            }

            if (Game.GameTime - _dealStartedAt < DealDurationMs) return;

            CompleteSale(player);
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

            var asked = DealSizes[_rng.Next(DealSizes.Length)];
            var grams = Math.Min(asked, Stash.PackagedOf(product.Id));
            if (grams < 0.05f)
            {
                Stop("You are out of " + product.Name.ToLowerInvariant() + ".");
                return;
            }

            var purity = Stash.PurityOf(product.Id);

            // Stepped-on product still gets knocked back, same as a hand-to-hand.
            if (_rng.NextDouble() < Pricing.BadCutChance(purity))
            {
                _state.AddNotoriety(1f);
                Notify.Problem("they clocked the cut and walked.");
                return;
            }

            var sold = Stash.RemovePackaged(product.Id, grams);
            if (sold <= 0f) return;

            var payout = _pricing.SaleValue(product, sold, purity);
            Game.Player.Money += payout;

            _sales++;
            _earned += payout;

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
                standing.MoneyEarned += payout;                standing.Deals++;
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

            Notify.Ticker("~g~+$" + payout.ToString("N0") + "~s~  " + sold.ToString("0.#") + "g");

            // Doing it in front of a uniform is its own problem, regardless of how quiet the
            // corner has been up to now.
            if (CopIsWatching(player))
            {
                Notify.Failure("a cop watched that.");
                Wanted(1);
            }
            else if (_cornerHeat >= _cfg.PostUpHeatBeforePolice * HeatForWanted)
            {
                // Word gets round without anybody having to see it.
                Notify.Failure("this corner is too hot now.");
                Wanted(1);
            }

            if (Stash.PackagedOf(product.Id) < 0.05f) Stop("That is the last of it.");
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

                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, true);
                    Function.Call(Hash.TASK_COMBAT_PED, ped.Handle, player.Handle, 0, 16);

                    Notify.Failure("you have been seen selling on their block.");

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

        /// <summary>The first gang your lot are at war with that still exists in the registry.</summary>
        private GangDef FirstRivalOf(GangDef gang)
        {
            if (gang == null || Crew == null) return null;

            foreach (var id in gang.Rivals)
            {
                var rival = Crew.GangById(id);
                if (rival != null) return rival;
            }

            return null;
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

                Notify.Failure("that is not your car coming.");
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

            if (!_driveByBailed)
            {
                var stalled = _driveByCar.Speed < 1.5f &&
                              player.Position.DistanceTo(_driveByCar.Position) < 35f &&
                              elapsed > 6000;

                if (!stalled && elapsed < DriveByShootMs) return;

                _driveByBailed = true;

                foreach (var ped in _rivals)
                {
                    if (ped == null || !ped.Exists() || !ped.IsAlive) continue;
                    if (!ped.IsInVehicle()) continue;

                    try
                    {
                        Function.Call(Hash.TASK_LEAVE_VEHICLE, ped.Handle, _driveByCar.Handle, 0);
                        Function.Call(Hash.TASK_COMBAT_PED, ped.Handle, player.Handle, 0, 16);
                    }
                    catch { /* he will be dealt with by the game's own AI */ }
                }

                Log.Info("Drive-by turned into a fight after " + (elapsed / 1000) + "s.");
                return;
            }

            // Everyone down or gone: let the car go.
            var standing = 0;
            foreach (var ped in _rivals)
            {
                if (ped != null && ped.Exists() && ped.IsAlive) standing++;
            }

            if (standing == 0 && _driveByCar.Exists())
            {
                try { _driveByCar.MarkAsNoLongerNeeded(); } catch { }
                _driveByCar = null;
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

        /// <summary>How long they sit there before losing interest.</summary>
        private const int PatrolWatchMs = 60000;

        /// <summary>Not so close it is comedy, not so far they cannot see you.</summary>
        private const float PatrolMinDistance = 22f;
        private const float PatrolMaxDistance = 40f;

        /// <summary>Shuffled per call, so the same cruiser is not always the one that shows up.</summary>


        private static readonly string[] PatrolCars = { "police", "police2", "police3", "sheriff", "police4" };



        private Vehicle _patrolCar;
        private readonly List<Ped> _patrolCops = new List<Ped>();
        private int _patrolUntil;
        private int _nextPatrolAt;

        /// <summary>
        /// A patrol pulling over to sit on the corner for a minute.
        ///
        /// Nothing happens on its own -- they park, they watch, they go. What they change is
        /// the cost of the next sale: the existing witness check already turns a handoff in
        /// front of a uniform into a star, so the decision is simply whether you can wait a
        /// minute or whether you are greedy.
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
                var angle = _rng.NextDouble() * Math.PI * 2.0;
                var distance = PatrolMinDistance +
                               (float)_rng.NextDouble() * (PatrolMaxDistance - PatrolMinDistance);

                var near = player.Position + new Vector3(
                    (float)Math.Cos(angle) * distance, (float)Math.Sin(angle) * distance, 0f);

                var kerb = World.GetNextPositionOnStreet(near);
                if (kerb == Vector3.Zero) return;
                if (kerb.DistanceTo(player.Position) > PatrolMaxDistance * 1.6f) return;

                _patrolCar = World.CreateVehicle(carModel.Value, kerb);
                if (_patrolCar == null || !_patrolCar.Exists()) return;

                _patrolCar.IsPersistent = true;
                _patrolCar.IsEngineRunning = true;

                for (var seat = -1; seat <= 0; seat++)
                {
                    var cop = SpawnCopInCar(_patrolCar, seat);
                    if (cop != null) _patrolCops.Add(cop);
                }

                _patrolUntil = Game.GameTime + PatrolWatchMs;
                SchedulePatrol();

                Notify.Problem("a patrol just pulled up. Sit tight or serve in front of them.");
                Log.Info("Patrol parked up near the pitch.");
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
            if (Game.GameTime < _patrolUntil) return;

            // Minute is up and nothing happened, so they move on.
            foreach (var cop in _patrolCops)
            {
                if (cop == null || !cop.Exists() || !cop.IsAlive) continue;

                try
                {
                    if (cop.SeatIndex == VehicleSeat.Driver)
                    {
                        Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, cop.Handle, _patrolCar.Handle,
                                      15f, 786603);
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

            Log.Info("Patrol moved on.");
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
        private static void Wanted(int stars)
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

            Notify.Failure("a patrol has taken an interest. Move.");
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
                    _cop.MarkAsNoLongerNeeded();
                    if (_copSpawned) _cop.MarkAsNoLongerNeeded();
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

            var label = State == PostState.Questioned ? "BEING SEARCHED"
                : State == PostState.Investigated ? "PATROL INCOMING"
                : _product == null ? "POSTED UP"
                : "POSTED UP  ·  " + _product.Name.ToUpperInvariant();

            Hud.Text(label, x, y - 0.042f, 0.34f,
                     State == PostState.Posted ? Palette.Text : Palette.Danger, Hud.FontLabel);

            // What is left is the number that decides whether you stay, so it goes first and
            // turns amber as it runs down.
            var left = _product == null ? 0f : Stash.PackagedOf(_product.Id);
            var lots = (int)(left / 1.5f);

            Hud.Text(left.ToString("0.#") + "g left  ·  " + lots + " more sale" + (lots == 1 ? "" : "s"),
                     x, y + 0.024f, 0.30f,
                     left < 7f ? Palette.Warn : Palette.Cash, Hud.FontBody);

            Hud.Text(Footfall + " passing  ·  " + _sales + " sold  ·  $" + _earned.ToString("N0"),
                     x, y + 0.048f, 0.28f, Palette.TextDim, Hud.FontBody);
        }
    }
}
