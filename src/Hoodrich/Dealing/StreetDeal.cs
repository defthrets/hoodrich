using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Economy;
using Hoodrich.Gangs;
using Hoodrich.State;
using Hoodrich.Territory;
using Hoodrich.UI;

namespace Hoodrich.Dealing
{
    /// <summary>
    /// A hand-to-hand sale to an ambient ped.
    ///
    /// Runs as a small state machine driven from the script tick rather than a blocking wait,
    /// so the rest of Hoodrich keeps updating while a deal plays out and an interruption
    /// (player runs off, buyer dies) can be handled cleanly.
    /// </summary>
    internal sealed class StreetDeal
    {
        private const float SearchRadius = 12f;
        private const float MaxDealDistance = 4.5f;
        private const int DealDurationMs = 2400;
        private const int BuyerCooldownMs = 90_000;

        /// <summary>PED_TYPE values that will never buy from you.</summary>
        private static readonly HashSet<int> NonBuyerPedTypes = new HashSet<int> { 6, 27, 28, 29 };

        private const string AnimDict = "mp_common";
        private const string AnimPlayer = "givetake1_a";
        private const string AnimBuyer = "givetake1_b";

        private readonly PlayerState _state;
        private readonly Pricing _pricing;
        private readonly Random _rng = new Random();

        private readonly Dictionary<int, int> _recentBuyers = new Dictionary<int, int>();

        /// <summary>Assigned by Main after construction.</summary>
        public TurfWatch Turf;
        public Affiliation Crew;

        private Ped _buyer;
        private DrugDef _product;
        private float _grams;
        private float _purity;
        private int _payout;
        private int _startedAt;
        private bool _animsRequested;

        public StreetDeal(PlayerState state, Pricing pricing)
        {
            _state = state;
            _pricing = pricing;
        }

        private Stash Stash => _state.Stash;

        public bool IsBusy => _buyer != null;

        // ---- buyer selection ---------------------------------------------------

        /// <summary>
        /// Picks the most plausible buyer: on foot, alive, civilian, and in front of the player.
        /// Returns null when nobody nearby qualifies.
        /// </summary>
        public Ped FindBuyer()
        {
            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return null;

            var forward = player.ForwardVector;
            var origin = player.Position;
            var now = Game.GameTime;

            Ped best = null;
            var bestScore = float.MinValue;

            Ped[] nearby;
            try
            {
                nearby = World.GetNearbyPeds(player, SearchRadius);
            }
            catch (Exception ex)
            {
                Log.Error("Ped scan failed.", ex);
                return null;
            }

            foreach (var ped in nearby)
            {
                if (!IsPlausibleBuyer(ped, now)) continue;

                var delta = ped.Position - origin;
                var distance = delta.Length();
                if (distance < 0.3f || distance > SearchRadius) continue;

                var dot = Vector3.Dot(Vector3.Normalize(delta), forward);
                if (dot < 0.25f) continue; // behind or beside the player

                // Prefer whoever is most directly ahead, then whoever is closest.
                var score = dot * 2f - distance / SearchRadius;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = ped;
                }
            }

            return best;
        }

        private bool IsPlausibleBuyer(Ped ped, int now)
        {
            if (ped == null || !ped.Exists()) return false;
            if (ped.Handle == Game.Player.Character.Handle) return false;
            if (!ped.IsAlive || ped.IsDead) return false;
            if (!ped.IsHuman) return false;
            if (ped.IsInVehicle()) return false;
            if (ped.IsInCombat) return false;
            if (ped.IsRagdoll) return false;

            var pedType = Function.Call<int>(Hash.GET_PED_TYPE, ped.Handle);
            if (NonBuyerPedTypes.Contains(pedType)) return false;

            // Never try to sell to a member of the gang whose block you are standing on.
            if (Crew != null && Crew.IsRival(ped)) return false;

            if (_recentBuyers.TryGetValue(ped.Handle, out var last) && now - last < BuyerCooldownMs) return false;

            return true;
        }

        // ---- selling -----------------------------------------------------------

        /// <summary>
        /// Attempts to start a sale of PACKAGED product. Returns a player-facing reason on
        /// failure, or null on success.
        /// </summary>
        public string TrySell(DrugDef product, float grams)
        {
            if (IsBusy) return "Already mid-deal.";
            if (product == null) return "No product selected.";
            if (grams <= 0f) return "Nothing to sell.";

            if (!Stash.HasPackaged(product.Id, grams))
            {
                var bulk = Stash.BulkOf(product.Id);
                return bulk > 0.005f
                    ? "That is still bulk. Cut it before you can sell it."
                    : "Not holding that much " + product.Name + ".";
            }

            var buyer = FindBuyer();
            if (buyer == null) return "No buyer nearby. Find someone on foot and face them.";

            var player = Game.Player.Character;
            if (player.Position.DistanceTo(buyer.Position) > MaxDealDistance) return "Get closer to the buyer.";

            _buyer = buyer;
            _product = product;
            _grams = grams;
            _purity = Stash.PurityOf(product.Id);
            _payout = _pricing.SaleValue(product, grams, _purity);
            _startedAt = Game.GameTime;
            _animsRequested = false;

            BeginDealAnimation(player, buyer);
            return null;
        }

        private void BeginDealAnimation(Ped player, Ped buyer)
        {
            try
            {
                Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, buyer.Handle, player.Handle, DealDurationMs);
                Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, player.Handle, buyer.Handle, 800);

                Function.Call(Hash.REQUEST_ANIM_DICT, AnimDict);
                _animsRequested = true;
            }
            catch (Exception ex)
            {
                Log.Error("Could not start deal animation.", ex);
            }
        }

        /// <summary>Ticked every frame while a deal is in flight.</summary>
        public void Update()
        {
            if (_buyer == null) return;

            var player = Game.Player.Character;
            var elapsed = Game.GameTime - _startedAt;

            if (!_buyer.Exists() || !_buyer.IsAlive || player == null || !player.Exists())
            {
                Abort("The buyer is gone.");
                return;
            }

            if (player.Position.DistanceTo(_buyer.Position) > MaxDealDistance + 2f)
            {
                Abort("You walked away from the deal.");
                return;
            }

            // Kick the animation off once the dictionary has streamed in.
            if (_animsRequested && Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, AnimDict))
            {
                _animsRequested = false;
                PlayAnim(player, AnimPlayer);
                PlayAnim(_buyer, AnimBuyer);
            }

            if (elapsed >= DealDurationMs) Complete();
        }

        private static void PlayAnim(Ped ped, string anim)
        {
            Function.Call(Hash.TASK_PLAY_ANIM, ped.Handle, AnimDict, anim,
                          8f, -8f, -1, 0, 0f, false, false, false);
        }

        private void Complete()
        {
            var product = _product;
            var grams = _grams;
            var purity = _purity;
            var payout = _payout;
            var buyer = _buyer;

            Reset();

            if (buyer != null && buyer.Exists()) _recentBuyers[buyer.Handle] = Game.GameTime;

            // The buyer inspects it. Heavily stepped-on product gets knocked back.
            var badCutChance = Pricing.BadCutChance(purity);
            if (badCutChance > 0f && _rng.NextDouble() < badCutChance)
            {
                RefuseBadCut(buyer, product, purity);
                return;
            }

            var sold = Stash.RemovePackaged(product.Id, grams);
            if (sold <= 0f)
            {
                Notify.Problem("the product was gone before the handoff.");
                return;
            }

            // Pay out proportionally in case stock changed between starting and finishing.
            var actualPayout = Math.Max(1, (int)Math.Round(payout * (sold / grams)));
            Game.Player.Money += actualPayout;

            _state.AddRespect(1f + product.Tier * 0.5f);
            _state.AddNotoriety(product.HeatFactor * 1.5f * (Turf == null ? 1f : Turf.TurfHeatMultiplier));
            _state.TotalDealsMade++;
            _state.TotalEarned += actualPayout;
            _state.Touch();

            // Dealing is what gets you noticed on someone else's block.
            Turf?.MarkExposed();

            if (Crew != null && Crew.IsAffiliated)
            {
                var standing = Crew.CurrentStanding;
                standing.MoneyEarned += actualPayout;
                standing.Deals++;
                standing.Rep = Math.Min(1000f, standing.Rep + 0.5f);
            }

            Notify.Ticker("~g~+$" + actualPayout.ToString("N0") + "~s~  " + sold.ToString("0.#") + "g " +
                          product.Name + " @ " + (purity * 100f).ToString("0") + "%");

            Log.Info("Sold " + sold.ToString("0.##") + "g " + product.Id + " at " +
                     purity.ToString("0.00") + " purity for $" + actualPayout +
                     " (" + _pricing.PriceContext() + ").");
        }

        /// <summary>
        /// The buyer refuses. Bad product costs you respect, and sometimes the buyer takes it
        /// personally -- which on the wrong block is how a sale turns into a fight.
        /// </summary>
        private void RefuseBadCut(Ped buyer, DrugDef product, float purity)
        {
            _state.AddRespect(-2f);
            _state.AddNotoriety(2f);
            _state.Touch();

            Notify.Problem("they clocked the cut on your " + product.Name.ToLowerInvariant() +
                           " (" + (purity * 100f).ToString("0") + "%). No sale.");

            // A quarter of the time they do more than walk away.
            if (buyer == null || !buyer.Exists() || !buyer.IsAlive) return;
            if (_rng.NextDouble() > 0.25) return;

            try
            {
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, buyer.Handle, 46, true);
                Function.Call(Hash.TASK_COMBAT_PED, buyer.Handle, Game.Player.Character.Handle, 0, 16);
                Function.Call(Hash.SET_PED_KEEP_TASK, buyer.Handle, true);
                Notify.Important("~r~They are not happy about it.~s~");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not anger the buyer: " + ex.Message);
            }
        }

        private void Abort(string reason)
        {
            Reset();
            Notify.Ticker("~o~Deal off.~s~ " + reason);
        }

        private void Reset()
        {
            _buyer = null;
            _product = null;
            _grams = 0f;
            _purity = 1f;
            _payout = 0;
            _animsRequested = false;
        }

        /// <summary>Drops cooldown entries for peds that have long since despawned.</summary>
        public void PruneCooldowns()
        {
            if (_recentBuyers.Count == 0) return;

            var now = Game.GameTime;
            List<int> stale = null;
            foreach (var kv in _recentBuyers)
            {
                if (now - kv.Value <= BuyerCooldownMs) continue;
                (stale ?? (stale = new List<int>())).Add(kv.Key);
            }

            if (stale == null) return;
            foreach (var handle in stale) _recentBuyers.Remove(handle);
        }
    }
}
