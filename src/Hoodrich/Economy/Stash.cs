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
        public const float MinPurity = 0.2f;
        public const float MaxPurity = 1.0f;

        private readonly Dictionary<string, float> _bulk = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Holding> _packaged = new Dictionary<string, Holding>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Combined carry limit across bulk and packaged, in grams.</summary>
        public float Capacity = 400f;

        public float TotalBulk
        {
            get { var s = 0f; foreach (var v in _bulk.Values) s += v; return s; }
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
            return _bulk.TryGetValue(drugId, out var v) ? v : 0f;
        }

        /// <summary>Adds uncut weight, capped by free space. Returns how much fit.</summary>
        public float AddBulk(string drugId, float grams)
        {
            if (string.IsNullOrEmpty(drugId) || grams <= 0f) return 0f;

            var accepted = Math.Min(grams, FreeSpace);
            if (accepted <= 0f) return 0f;

            _bulk[drugId] = BulkOf(drugId) + accepted;
            return accepted;
        }

        public float RemoveBulk(string drugId, float grams)
        {
            if (string.IsNullOrEmpty(drugId) || grams <= 0f) return 0f;

            var held = BulkOf(drugId);
            var taken = Math.Min(grams, held);
            if (taken <= 0f) return 0f;

            var left = held - taken;
            if (left < 0.005f) _bulk.Remove(drugId);
            else _bulk[drugId] = left;

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

        public Json ToJson()
        {
            var obj = Json.Object();
            obj.Set("capacity", Capacity);

            var bulk = Json.Object();
            foreach (var kv in _bulk) bulk.Set(kv.Key, Math.Round(kv.Value, 3));
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

            Capacity = Math.Max(1f, node["capacity"].AsFloat(Capacity));

            var bulk = node["bulk"];
            foreach (var key in bulk.Keys)
            {
                var v = bulk[key].AsFloat(0f);
                if (v > 0.005f) _bulk[key] = v;
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
