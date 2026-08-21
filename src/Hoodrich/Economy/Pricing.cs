using System;
using System.Collections.Generic;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Gangs;
using Hoodrich.State;
using Hoodrich.Territory;

namespace Hoodrich.Economy
{
    /// <summary>
    /// What product is worth, and how busy the corner is.
    ///
    /// The price is the price. A gram of weed is twenty dollars at four in the afternoon on
    /// your own block at rank one, and it is twenty dollars at two in the morning on somebody
    /// else's at rank nine. Every figure in the catalogue is paid out literally, so a number
    /// on screen can always be checked against the number you were quoted.
    ///
    /// What the hour and the block and the rank move instead is DEMAND -- how often somebody
    /// walks up to you. That is the same idea told honestly: a corner at 2am is busier, not
    /// dearer. Purity keeps its teeth through <see cref="BadCutChance"/> rather than through
    /// price; nobody knocks money off for a weak gram, they take it, clock it, and stop coming.
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
        /// Demand never dies completely and never runs away, whatever the multipliers do.
        ///
        /// Without a ceiling a rank-nine dealer on friendly turf with lookouts at 2am gets a
        /// customer on essentially every scan, which is not a corner, it is a queue.
        /// </summary>
        private const float MinDemand = 0.35f;
        private const float MaxDemand = 2.6f;

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

        public float TurfMultiplier => Turf == null ? 1f : Turf.TurfPriceMultiplier;

        public float LookoutMultiplier => Crew == null ? 1f : Crew.LookoutMultiplier;

        /// <summary>
        /// What selling rubbish costs you: customers.
        ///
        /// A corner with a bad name is a quiet corner. Never zero, because somebody desperate
        /// will always come -- but at the bottom it is a third of the traffic, which is the
        /// whole counterweight to cutting. Three times the units at a third of the footfall is
        /// a decision. Three times the units at the same footfall, which is what it used to be,
        /// is not a decision, it is a free lunch.
        /// </summary>
        public float ReputationMultiplier =>
            _state == null ? 1f : RepFloor + (1f - RepFloor) * _state.ProductRep;

        private const float RepFloor = 0.34f;

        /// <summary>
        /// How busy it is out here.
        ///
        /// Everything that used to bend the price bends this instead: the hour, your rank, the
        /// heat on you, whose block it is, whether anybody is watching your back, and what the
        /// market is doing to that particular product. High demand means people keep walking
        /// up. It does not mean they pay more, because they do not.
        /// </summary>
        public float Demand(DrugDef drug)
        {
            var d = NightMultiplier
                    * RankMultiplier
                    * NotorietyMultiplier
                    * TurfMultiplier
                    * LookoutMultiplier
                    * ReputationMultiplier
                    * (Market == null || drug == null ? 1f : Market.Multiplier(drug.Id));

            return d < MinDemand ? MinDemand : d > MaxDemand ? MaxDemand : d;
        }

        /// <summary>
        /// Per-gram wholesale price for BULK. Based on the undiscounted base price so buying
        /// stock is not made cheap simply by it being 2am.
        /// </summary>
        public float WholesalePrice(DrugDef drug, float supplierMultiplier = 1f)
        {
            if (drug == null) return 0f;
            // A price the catalogue actually states beats one worked out from a percentage.
            if (drug.BulkPrice > 0f) return Math.Max(0.5f, drug.BulkPrice * supplierMultiplier);

            var discount = 1f - _cfg.BulkPurchaseDiscountPercent / 100f;
            return Math.Max(0.5f, drug.BasePrice * discount * supplierMultiplier);
        }

        /// <summary>
        /// What a named deal pays. The number written in the catalogue, and nothing else.
        ///
        /// An ounce is two hundred dollars. Not two hundred adjusted for the hour, or two
        /// hundred less what the cut did to it -- two hundred.
        /// </summary>
        public int DealValue(DrugDef drug, Deal deal, float purity)
        {
            return deal == null ? 0 : Math.Max(1, deal.Price);
        }

        /// <summary>
        /// What a loose quantity is worth, valued off the ladder rather than a flat per-unit
        /// rate: the biggest deal that fits gets used first, and whatever is left over goes at
        /// the single-unit price. So 30g of weed is an ounce and two singles -- $240 -- which is
        /// what somebody would actually get for it, not 30 x $20.
        /// </summary>
        public int SaleValue(DrugDef drug, float grams, float purity)
        {
            if (drug == null || grams <= 0f) return 0;
            if (drug.Deals.Count == 0) return Math.Max(1, (int)Math.Round(drug.BasePrice * grams));

            var left = grams;
            var total = 0f;

            // Largest first, so weight goes at weight prices.
            var order = new List<Deal>(drug.Deals);
            order.Sort((a, b) => b.Quantity.CompareTo(a.Quantity));

            foreach (var deal in order)
            {
                if (deal.Quantity <= 0f) continue;

                var lots = (int)(left / deal.Quantity);
                if (lots <= 0) continue;

                total += lots * deal.Price;
                left -= lots * deal.Quantity;
            }

            // The tail goes at the single-unit rate, which is the dearest way to sell it.
            if (left > 0.005f) total += left * drug.UnitPrice;

            return Math.Max(1, (int)Math.Round(total));
        }

        public int PurchaseCost(DrugDef drug, float grams, float supplierMultiplier = 1f)
        {
            if (drug == null || grams <= 0f) return 0;
            return Math.Max(1, (int)Math.Round(WholesalePrice(drug, supplierMultiplier) * grams));
        }

        /// <summary>
        /// Chance a buyer notices the product is stepped on.
        ///
        /// This is now the only thing purity does, so it has to do it properly. Cutting a kilo
        /// into three used to just lower the per-gram price, which still left you ahead -- the
        /// maths always said cut it further. Now the price never moves and the refusals do, so
        /// stretching product is a straight gamble against how much of it you can move before
        /// the block decides you sell rubbish.
        ///
        /// Clean product is never refused. At the 20% floor it is turned down three times in
        /// four, which is what garbage deserves.
        ///
        /// The dead zone above 90% is gone. It used to return zero for anything at or above
        /// 0.9, and yield at 0.9 is 1.11x -- so cutting to exactly ninety was an eleven per
        /// cent bigger stash with literally no chance of ever being noticed. Only whole
        /// product is never refused now.
        /// </summary>
        public static float BadCutChance(float purity)
        {
            if (purity >= 1f) return 0f;
            return Math.Min(0.76f, (1f - purity) * 0.85f);
        }

        /// <summary>
        /// Short human-readable reason the corner is as busy as it is.
        ///
        /// Deliberately not a price context any more: the prices do not move, so a line
        /// explaining why they had would be a lie printed under the numbers it was lying about.
        /// </summary>
        public string PriceContext()
        {
            var parts = IsNight ? "night, busy" : "daytime";

            // Only once the block has actually made its mind up. While you have no name it is
            // not news, and printing "you ain't got a name yet" under every readout for the
            // first hour is noise.
            if (_state != null && !_state.ProductRepIsNeutral)
            {
                parts += "  " + _state.ProductRepWord;
            }

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
