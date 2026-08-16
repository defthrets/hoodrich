using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Trapline.Core;
using Trapline.Economy;
using Trapline.State;
using Trapline.UI;

namespace Trapline.Dealing
{
    /// <summary>
    /// A hand-to-hand sale to an ambient ped.
    ///
    /// Runs as a small state machine driven from the script tick rather than a blocking wait,
    /// so the rest of Trapline keeps updating while a deal plays out and an interruption
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

        private readonly Settings _cfg;
        private readonly PlayerState _state;
        private readonly Pricing _pricing;

        private readonly Dictionary<int, int> _recentBuyers = new Dictionary<int, int>();

        private Ped _buyer;
        private DrugDef _product;
        private float _grams;
        private int _payout;
        private int _startedAt;
        private bool _animsRequested;

        public StreetDeal(Settings cfg, PlayerState state, Pricing pricing)
        {
            _cfg = cfg;
            _state = state;
            _pricing = pricing;
        }

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

            if (_recentBuyers.TryGetValue(ped.Handle, out var last) && now - last < BuyerCooldownMs) return false;

            return true;
        }

        // ---- selling -----------------------------------------------------------

        /// <summary>
        /// Attempts to start a sale. Returns a player-facing reason on failure, or null on success.
        /// </summary>
        public string TrySell(DrugDef product, float grams)
        {
            if (IsBusy) return "Already mid-deal.";
            if (product == null) return "No product selected.";
            if (grams <= 0f) return "Nothing to sell.";
            if (!_state.Inventory.Has(product.Id, grams)) return "Not holding that much " + product.Name + ".";

            var buyer = FindBuyer();
            if (buyer == null) return "No buyer nearby. Find someone on foot and face them.";

            var player = Game.Player.Character;
            if (player.Position.DistanceTo(buyer.Position) > MaxDealDistance) return "Get closer to the buyer.";

            _buyer = buyer;
            _product = product;
            _grams = grams;
            _payout = _pricing.SaleValue(product, grams);
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
            var payout = _payout;
            var buyer = _buyer;

            Reset();

            var sold = _state.Inventory.Remove(product.Id, grams);
            if (sold <= 0f)
            {
                Notify.Ticker("~r~Trapline:~s~ the product was gone before the handoff.");
                return;
            }

            // Pay out proportionally in case stock changed between starting and finishing.
            var actualPayout = Math.Max(1, (int)Math.Round(payout * (sold / grams)));
            Game.Player.Money += actualPayout;

            var respect = 1f + product.Tier * 0.5f;
            _state.AddRespect(respect);
            _state.AddNotoriety(product.HeatFactor * 1.5f);
            _state.TotalDealsMade++;
            _state.TotalEarned += actualPayout;
            _state.Touch();

            if (buyer != null && buyer.Exists()) _recentBuyers[buyer.Handle] = Game.GameTime;

            Notify.Ticker("~g~+$" + actualPayout + "~s~  " + sold.ToString("0.#") + "g " + product.Name);
            Log.Info("Sold " + sold.ToString("0.##") + "g " + product.Id + " for $" + actualPayout +
                     " (" + _pricing.PriceContext() + ").");
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
