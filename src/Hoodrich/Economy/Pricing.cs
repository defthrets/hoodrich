using System;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Gangs;
using Hoodrich.State;
using Hoodrich.Territory;

namespace Hoodrich.Economy
{
    /// <summary>
    /// Works out what product is worth right now.
    ///
    /// The model is deliberately transparent: a base price walked through a handful of named
    /// multipliers, each of which the player can feel and reason about. Night is the loudest,
    /// then purity, then whose block you are standing on.
    /// </summary>
    internal sealed class Pricing
    {
        // Night window wraps midnight: prices ramp from START, peak at PEAK, fade out by END.
        private const int NightStartHour = 22;
        private const int NightEndHour = 4;
        private const int NightPeakHour = 2;
        private const float NightFloorMultiplier = 1.4f;
        private const float NightPeakMultiplier = 3.0f;

        /// <summary>
        /// Price floor for worthless product. At 100% purity this is 1.0; at the 20% floor it
        /// is 0.48, so cutting 1g into 5g grosses far more but each unit is near-garbage.
        /// </summary>
        private const float PurityFloor = 0.35f;

        private readonly Settings _cfg;
        private readonly PlayerState _state;

        /// <summary>Assigned by Main after construction; both are optional for pricing to work.</summary>
        public TurfWatch Turf;
        public Affiliation Crew;
        public Market Market;

        public Pricing(Settings cfg, PlayerState state)
        {
            _cfg = cfg;
            _state = state;
        }

        public static int ClockHour => Function.Call<int>(Hash.GET_CLOCK_HOURS);

        public bool IsNight => IsNightHour(ClockHour);

        private static bool IsNightHour(int hour)
        {
            return NightStartHour > NightEndHour
                ? hour >= NightStartHour || hour < NightEndHour
                : hour >= NightStartHour && hour < NightEndHour;
        }

        /// <summary>
        /// Time-of-day multiplier. Ramps linearly from the floor at the window edges up to the
        /// peak at <see cref="NightPeakHour"/>, and is flat 1.0 during the day.
        /// </summary>
        public float NightMultiplier
        {
            get
            {
                var hour = ClockHour;
                if (!IsNightHour(hour)) return 1f;

                // Re-base onto a linear axis so the midnight wrap stops being a special case.
                var h = hour < NightEndHour ? hour + 24 : hour;
                const int start = NightStartHour;
                const int end = NightEndHour + 24;
                var peak = NightPeakHour < NightEndHour ? NightPeakHour + 24 : NightPeakHour;

                float t;
                if (h <= peak)
                {
                    var span = peak - start;
                    t = span <= 0 ? 1f : (h - start) / (float)span;
                }
                else
                {
                    var span = end - peak;
                    t = span <= 0 ? 0f : 1f - (h - peak) / (float)span;
                }

                t = Math.Min(1f, Math.Max(0f, t));
                return NightFloorMultiplier + (NightPeakMultiplier - NightFloorMultiplier) * t;
            }
        }

        /// <summary>Higher rank means better connections and a better take.</summary>
        public float RankMultiplier => 1f + _state.Rank * 0.06f;

        /// <summary>Heat makes buyers jumpy and they haggle harder.</summary>
        public float NotorietyMultiplier => 1f - Math.Min(0.35f, _state.Notoriety / 100f * 0.35f);

        /// <summary>Cut product is worth less per gram, but never nothing.</summary>
        public static float PurityMultiplier(float purity)
        {
            purity = purity < Stash.MinPurity ? Stash.MinPurity : purity > Stash.MaxPurity ? Stash.MaxPurity : purity;
            return PurityFloor + (1f - PurityFloor) * purity;
        }

        public float TurfMultiplier => Turf == null ? 1f : Turf.TurfPriceMultiplier;

        public float LookoutMultiplier => Crew == null ? 1f : Crew.LookoutMultiplier;

        /// <summary>Per-gram street price for packaged product of a given purity.</summary>
        public float StreetPrice(DrugDef drug, float purity)
        {
            if (drug == null) return 0f;

            return drug.BasePrice
                   * NightMultiplier
                   * RankMultiplier
                   * NotorietyMultiplier
                   * PurityMultiplier(purity)
                   * TurfMultiplier
                   * LookoutMultiplier
                   * (Market == null ? 1f : Market.Multiplier(drug.Id));
        }

        /// <summary>
        /// Per-gram wholesale price for BULK. Based on the undiscounted base price so buying
        /// stock is not made cheap simply by it being 2am.
        /// </summary>
        public float WholesalePrice(DrugDef drug, float supplierMultiplier = 1f)
        {
            if (drug == null) return 0f;
            var discount = 1f - _cfg.BulkPurchaseDiscountPercent / 100f;
            return Math.Max(0.5f, drug.BasePrice * discount * supplierMultiplier);
        }

        public int SaleValue(DrugDef drug, float grams, float purity)
        {
            if (drug == null || grams <= 0f) return 0;
            return Math.Max(1, (int)Math.Round(StreetPrice(drug, purity) * grams));
        }

        public int PurchaseCost(DrugDef drug, float grams, float supplierMultiplier = 1f)
        {
            if (drug == null || grams <= 0f) return 0;
            return Math.Max(1, (int)Math.Round(WholesalePrice(drug, supplierMultiplier) * grams));
        }

        /// <summary>
        /// Chance a buyer notices the product is stepped on. Heavily cut product gets you
        /// refused, and sometimes gets you a problem.
        /// </summary>
        public static float BadCutChance(float purity)
        {
            if (purity >= 0.9f) return 0f;
            return Math.Min(0.45f, (0.9f - purity) * 0.6f);
        }

        /// <summary>Short human-readable reason the current price is what it is.</summary>
        public string PriceContext()
        {
            var parts = IsNight ? "night x" + NightMultiplier.ToString("0.0") : "daytime";

            if (Turf != null && Math.Abs(TurfMultiplier - 1f) > 0.01f)
            {
                parts += "  turf x" + TurfMultiplier.ToString("0.00");
            }

            if (Crew != null && Crew.NearbyAllies > 0)
            {
                parts += "  " + Crew.NearbyAllies + " watching";
            }

            return parts;
        }
    }
}
