using System;
using System.Collections.Generic;
using Hoodrich.Core;

namespace Hoodrich.Economy
{
    /// <summary>Packaged product: grams at an effective purity.</summary>
    internal sealed class Holding
    {
        public float Grams;

        /// <summary>0.2 .. 1.0. Drives price per gram and the chance a buyer takes offence.</summary>
        public float Purity = 1f;
    }

    /// <summary>
    /// What the player is carrying, in two distinct forms.
    ///
    /// BULK is what suppliers sell: uncut weight, worth nothing on the street until it has been
    /// cut and bagged. PACKAGED is street-ready product, and is the only thing that can be sold.
    /// Forcing product through that conversion is the whole point -- it is where the risk/greed
    /// decision lives (cut it harder for more units at a worse price and a worse reaction).
    ///
    /// Packaged purity is tracked as a single weighted average per product rather than as a list
    /// of individual bags. Mixing a fresh batch into an existing one blends the purity, which is
    /// both realistic and vastly simpler than tracking every baggie.
    /// </summary>
    internal sealed class Stash
    {
        /// <summary>
        /// Which of the four marks a purity wears.
        ///
        /// Four steps rather than a printed percentage, because these are drawn at about
        /// thirteen pixels beside a row label -- there is no room for three digits there and no
        /// hue worth reading at that size, but a full disc, a three-quarter, a half and a third
        /// are four silhouettes you can tell apart across a room. Same reasoning as the rank
        /// pips, which is the other thing in this mod that has to say a number without printing
        /// one.
        ///
        /// The bands break at the midpoints, so a batch blended to 0.62 shows as a half rather
        /// than rounding up to something it is not. Anything that has not been touched shows
        /// the full disc, which is the point of the whole system: uncut looks different from
        /// stepped on before you have read a single figure.
        /// </summary>
        public static string Mark(float purity)
        {
            if (purity >= 0.875f) return "cut_100.png";
            if (purity >= 0.625f) return "cut_75.png";
            if (purity >= 0.415f) return "cut_50.png";

            if (purity >= 0.29f) return "cut_33.png";

            // Below the sellable floor, so it gets a mark of its own rather than
            // wearing the third's. A bag you cannot move is not a weaker version of
            // one you can.
            return "cut_25.png";
        }

        /// <summary>The same four as a number, for anywhere with the room to say it.</summary>
        public static int Percent(float purity)
        {
            if (purity >= 0.875f) return 100;
            if (purity >= 0.625f) return 75;
            if (purity >= 0.415f) return 50;

            if (purity >= 0.29f) return 33;

            return 25;
        }

        /// <summary>
        /// Weakest anybody on the street will take off you.
        ///
        /// Below this it is not cheap product, it is not product. Cutting fifty per cent weight
        /// to a half again lands here, which is the whole reason the number exists: stretching
        /// stepped-on weight has to have an end, and the end is a bag nobody buys rather than a
        /// bag that sells for slightly less.
        /// </summary>
        public const float Unsellable = 0.30f;

        /// <summary>Whether anybody would take it.</summary>
        public static bool Sellable(float purity) => purity >= Unsellable;

        public const float MinPurity = 0.2f;
        public const float MaxPurity = 1.0f;

        /// <summary>
        /// Weight, and how strong it already is.
        ///
        /// It used to be a bare number, on the assumption that everything a supplier sells is
        /// untouched. That is true of a plug at the docks and false of everybody else: a man
        /// selling weight off his own person has already stepped on it, and what he hands over
        /// is fifty per cent before you have done anything to it.
        ///
        /// Which makes cutting MULTIPLICATIVE rather than absolute. Cut fifty per cent weight
        /// to a half again and you have twenty-five, and twenty-five is not a product, it is a
        /// thing nobody on the street will take off you twice.
        /// </summary>
        private readonly Dictionary<string, Holding> _bulk =
            new Dictionary<string, Holding>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Holding> _packaged = new Dictionary<string, Holding>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Combined carry limit across bulk and packaged, in grams.</summary>
        public float Capacity = 400f;

        public float TotalBulk
        {
            get { var s = 0f; foreach (var h in _bulk.Values) s += h.Grams; return s; }
        }

        public float TotalPackaged
        {
            get { var s = 0f; foreach (var h in _packaged.Values) s += h.Grams; return s; }
        }

        public float Total => TotalBulk + TotalPackaged;

        public float FreeSpace => Math.Max(0f, Capacity - Total);

        // ---- bulk --------------------------------------------------------------

        public float BulkOf(string drugId)
        {
            if (string.IsNullOrEmpty(drugId)) return 0f;
            return _bulk.TryGetValue(drugId, out var h) ? h.Grams : 0f;
        }

        /// <summary>How strong the weight on hand is. 1.0 when holding none.</summary>
        public float BulkPurityOf(string drugId)
        {
            if (string.IsNullOrEmpty(drugId)) return 1f;
            return _bulk.TryGetValue(drugId, out var h) && h.Grams > 0.005f ? h.Purity : 1f;
        }

        /// <summary>
        /// Adds weight at a strength, blending by weight. Returns how much fit.
        ///
        /// Full strength by default, so every caller that was written when weight was simply
        /// weight still means what it said. Only somebody who KNOWS his product is stepped on
        /// has to say so.
        /// </summary>
        public float AddBulk(string drugId, float grams, float purity = 1f)
        {
            if (string.IsNullOrEmpty(drugId) || grams <= 0f) return 0f;

            var accepted = Math.Min(grams, FreeSpace);
            if (accepted <= 0f) return 0f;

            purity = Clamp(purity, MinPurity, MaxPurity);

            if (!_bulk.TryGetValue(drugId, out var h))
            {
                _bulk[drugId] = new Holding { Grams = accepted, Purity = purity };
                return accepted;
            }

            var total = h.Grams + accepted;
            h.Purity = total <= 0f ? purity : (h.Purity * h.Grams + purity * accepted) / total;
            h.Grams = total;
            return accepted;
        }

        public float RemoveBulk(string drugId, float grams)
        {
            if (string.IsNullOrEmpty(drugId) || grams <= 0f) return 0f;

            if (!_bulk.TryGetValue(drugId, out var h)) return 0f;

            var taken = Math.Min(grams, h.Grams);
            if (taken <= 0f) return 0f;

            h.Grams -= taken;
            if (h.Grams < 0.005f) _bulk.Remove(drugId);

            return taken;
        }

        // ---- packaged ----------------------------------------------------------

        public float PackagedOf(string drugId)
        {
            if (string.IsNullOrEmpty(drugId)) return 0f;
            return _packaged.TryGetValue(drugId, out var h) ? h.Grams : 0f;
        }

        /// <summary>Effective purity of the packaged product on hand. 1.0 when holding none.</summary>
        public float PurityOf(string drugId)
        {
            if (string.IsNullOrEmpty(drugId)) return 1f;
            return _packaged.TryGetValue(drugId, out var h) && h.Grams > 0.005f ? h.Purity : 1f;
        }

        /// <summary>
        /// Adds street-ready product, blending purity by weight. Returns how much fit.
        /// </summary>
        public float AddPackaged(string drugId, float grams, float purity)
        {
            if (string.IsNullOrEmpty(drugId) || grams <= 0f) return 0f;

            var accepted = Math.Min(grams, FreeSpace);
            if (accepted <= 0f) return 0f;

            purity = Clamp(purity, MinPurity, MaxPurity);

            if (!_packaged.TryGetValue(drugId, out var h))
            {
                _packaged[drugId] = new Holding { Grams = accepted, Purity = purity };
                return accepted;
            }

            var total = h.Grams + accepted;
            h.Purity = total <= 0f ? purity : (h.Purity * h.Grams + purity * accepted) / total;
            h.Grams = total;
            return accepted;
        }

        public float RemovePackaged(string drugId, float grams)
        {
            if (string.IsNullOrEmpty(drugId) || grams <= 0f) return 0f;
            if (!_packaged.TryGetValue(drugId, out var h)) return 0f;

            var taken = Math.Min(grams, h.Grams);
            if (taken <= 0f) return 0f;

            h.Grams -= taken;
            if (h.Grams < 0.005f) _packaged.Remove(drugId);

            return taken;
        }

        // ---- queries -----------------------------------------------------------

        public List<DrugDef> WithPackaged(Drugs catalogue)
        {
            var list = new List<DrugDef>();
            foreach (var d in catalogue.All)
            {
                if (PackagedOf(d.Id) > 0.005f) list.Add(d);
            }
            return list;
        }

        public void Clear()
        {
            _bulk.Clear();
            _packaged.Clear();
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;

        // ---- persistence -------------------------------------------------------

        /// <summary>
        /// What is in it. Deliberately NOT how much it holds.
        ///
        /// Capacity used to be written here and read back on load, which meant the number baked
        /// into a save the first time it was written outlived every later change to the ini --
        /// so raising the house limit did nothing at all to a game already in progress, and
        /// there was no way to tell from the outside why. Whoever owns a container decides how
        /// big it is when they build it; the save only remembers what was inside.
        /// </summary>
        public Json ToJson()
        {
            var obj = Json.Object();

            var bulk = Json.Object();
            foreach (var kv in _bulk)
            {
                bulk.Set(kv.Key, Json.Object()
                    .Set("grams", Math.Round(kv.Value.Grams, 3))
                    .Set("purity", Math.Round(kv.Value.Purity, 3)));
            }
            obj.Set("bulk", bulk);

            var packaged = Json.Object();
            foreach (var kv in _packaged)
            {
                packaged.Set(kv.Key, Json.Object()
                    .Set("grams", Math.Round(kv.Value.Grams, 3))
                    .Set("purity", Math.Round(kv.Value.Purity, 3)));
            }
            obj.Set("packaged", packaged);

            return obj;
        }

        public void LoadFrom(Json node)
        {
            Clear();
            if (node == null || node.IsNull) return;

            // Read BOTH shapes, because the old one is in every save that already exists.
            //
            // Bulk used to be written as a bare number of grams. Reading that as an object gets
            // nothing and silently empties somebody's stash, which is the worst possible way to
            // handle a format change. A number means weight from before strength was tracked,
            // and weight from back then was untouched by definition -- so it loads at full.
            var bulk = node["bulk"];
            foreach (var key in bulk.Keys)
            {
                var entry = bulk[key];

                var grams = entry["grams"].AsFloat(0f);
                var purity = Clamp(entry["purity"].AsFloat(1f), MinPurity, MaxPurity);

                if (grams <= 0.005f)
                {
                    grams = entry.AsFloat(0f);
                    purity = 1f;
                }

                if (grams > 0.005f) _bulk[key] = new Holding { Grams = grams, Purity = purity };
            }

            var packaged = node["packaged"];
            foreach (var key in packaged.Keys)
            {
                var grams = packaged[key]["grams"].AsFloat(0f);
                if (grams <= 0.005f) continue;

                _packaged[key] = new Holding
                {
                    Grams = grams,
                    Purity = Clamp(packaged[key]["purity"].AsFloat(1f), MinPurity, MaxPurity)
                };
            }
        }
    }
}
