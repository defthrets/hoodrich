using System;
using System.Collections.Generic;
using System.Drawing;
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
        private const float LeashDistance = 6f;
        private const int DealDurationMs = 2600;
        private const float CopArriveRange = 3.5f;
        private const float CopScanRange = 160f;

        private const string AnimDict = "mp_common";
        private const string AnimPlayer = "givetake1_a";
        private const string AnimBuyer = "givetake1_b";

        /// <summary>Scenarios tried, in order, to make the player look like they are working.</summary>
        private static readonly string[] PostScenarios =
        {
            "WORLD_HUMAN_DRUG_DEALER", "WORLD_HUMAN_STAND_IMPATIENT", "WORLD_HUMAN_SMOKING"
        };

        private static readonly string[] CopModels = { "s_m_y_cop_01", "s_f_y_cop_01", "s_m_y_swat_01" };

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
            State = PostState.Posted;

            PlayPostScenario(player);

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

            try
            {
                var player = Game.Player.Character;
                if (player != null && player.Exists()) player.Task.ClearAll();
            }
            catch
            {
                // Nothing to do.
            }

            if (!string.IsNullOrEmpty(reason)) Notify.Ticker("~o~" + reason + "~s~");

            if (sales > 0)
            {
                Notify.Ticker("~g~" + sales + " sold~s~ for $" + earned.ToString("N0") + ".");
            }
        }

        private void PlayPostScenario(Ped player)
        {
            foreach (var scenario in PostScenarios)
            {
                try
                {
                    Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, player.Handle, scenario, 0, true);
                    return;
                }
                catch
                {
                    // Try the next one.
                }
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
                Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, _customer.Handle, player.Handle, DealDurationMs);
                Function.Call(Hash.REQUEST_ANIM_DICT, AnimDict);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not start the handoff: " + ex.Message);
            }
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
            PlayPostScenario(player);

            if (product == null) return;

            var grams = Math.Min(_cfg.PostUpDealGrams, Stash.PackagedOf(product.Id));
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
            var heat = product.HeatFactor * crowdFactor * (Turf == null ? 1f : Turf.TurfHeatMultiplier);

            _cornerHeat += heat;
            _state.AddNotoriety(heat * 0.5f);
            Turf?.MarkExposed();

            if (Crew != null && Crew.IsAffiliated)
            {
                var standing = Crew.CurrentStanding;
                standing.MoneyEarned += payout;
                standing.Deals++;
            }

            if (customer != null && customer.Exists())
            {
                try { Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, customer.Handle, false); }
                catch { }
            }

            Notify.Ticker("~g~+$" + payout.ToString("N0") + "~s~  " + sold.ToString("0.#") + "g");

            if (Stash.PackagedOf(product.Id) < 0.05f) Stop("That is the last of it.");
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
            State = PostState.Idle;
            _product = null;
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

            Hud.Text(Footfall + " passing  ·  " + _sales + " sold  ·  $" + _earned.ToString("N0"),
                     x, y + 0.024f, 0.28f, Palette.TextDim, Hud.FontBody);
        }
    }
}
