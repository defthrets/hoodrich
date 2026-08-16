using System;
using GTA.Native;
using Trapline.Core;
using Trapline.State;

namespace Trapline.Economy
{
    /// <summary>
    /// Works out what product is worth right now.
    ///
    /// The model is deliberately transparent: street price is the base price walked through a
    /// small number of named multipliers, each of which the player can feel. Night is the big
    /// one, because it is the lever that makes dealing after dark the interesting choice.
    /// </summary>
    internal sealed class Pricing
    {
        // Night window wraps midnight: prices ramp from START, peak at PEAK, fade out by END.
        private const int NightStartHour = 22;
        private const int NightEndHour = 4;
        private const int NightPeakHour = 2;
        private const float NightFloorMultiplier = 1.4f;
        private const float NightPeakMultiplier = 3.0f;

        private readonly Settings _cfg;
        private readonly PlayerState _state;

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
                var start = NightStartHour;
                var end = NightEndHour + 24;
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

        /// <summary>
        /// Heat cuts into the street price: when rivals and police are watching, buyers get
        /// jumpy and haggle harder.
        /// </summary>
        public float NotorietyMultiplier => 1f - Math.Min(0.35f, _state.Notoriety / 100f * 0.35f);

        /// <summary>Per-gram street price for a single sale, before per-deal haggling.</summary>
        public float StreetPrice(DrugDef drug)
        {
            if (drug == null) return 0f;
            return drug.BasePrice * NightMultiplier * RankMultiplier * NotorietyMultiplier;
        }

        /// <summary>
        /// Per-gram wholesale price the player PAYS a supplier. Based on the undiscounted base
        /// price, so buying stock is not made cheap simply by it being 2am.
        /// </summary>
        public float WholesalePrice(DrugDef drug)
        {
            if (drug == null) return 0f;
            var discount = 1f - _cfg.BulkPurchaseDiscountPercent / 100f;
            return Math.Max(0.5f, drug.BasePrice * discount);
        }

        /// <summary>What the player nets selling <paramref name="grams"/> in one transaction.</summary>
        public int SaleValue(DrugDef drug, float grams)
        {
            if (drug == null || grams <= 0f) return 0;
            return Math.Max(1, (int)Math.Round(StreetPrice(drug) * grams));
        }

        /// <summary>What buying <paramref name="grams"/> costs.</summary>
        public int PurchaseCost(DrugDef drug, float grams)
        {
            if (drug == null || grams <= 0f) return 0;
            return Math.Max(1, (int)Math.Round(WholesalePrice(drug) * grams));
        }

        /// <summary>Short human-readable reason the current price is what it is, for the wheel hub.</summary>
        public string PriceContext()
        {
            if (!IsNight) return "daytime rates";

            var m = NightMultiplier;
            return "night rates x" + m.ToString("0.0");
        }
    }
}
