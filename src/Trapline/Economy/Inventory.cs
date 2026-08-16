using System;
using System.Collections.Generic;
using Trapline.Core;

namespace Trapline.Economy
{
    /// <summary>
    /// What the player is carrying, in grams per product.
    ///
    /// Quantities are floats because deals happen in fractions of a gram, but everything the
    /// UI shows is rounded. Balances are clamped at zero and at <see cref="Capacity"/> rather
    /// than throwing, so a bad caller loses product instead of killing the script mid-deal.
    /// </summary>
    internal sealed class Inventory
    {
        private readonly Dictionary<string, float> _units = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Total grams the player can carry across all products.</summary>
        public float Capacity = 250f;

        public float Total
        {
            get
            {
                var sum = 0f;
                foreach (var v in _units.Values) sum += v;
                return sum;
            }
        }

        public float FreeSpace => Math.Max(0f, Capacity - Total);

        public bool IsEmpty => Total < 0.005f;

        public float Get(string drugId)
        {
            if (string.IsNullOrEmpty(drugId)) return 0f;
            return _units.TryGetValue(drugId, out var v) ? v : 0f;
        }

        public bool Has(string drugId, float amount) => Get(drugId) >= amount - 0.0001f;

        /// <summary>Adds product, capped by free space. Returns how much actually fit.</summary>
        public float Add(string drugId, float amount)
        {
            if (string.IsNullOrEmpty(drugId) || amount <= 0f) return 0f;

            var accepted = Math.Min(amount, FreeSpace);
            if (accepted <= 0f) return 0f;

            _units[drugId] = Get(drugId) + accepted;
            return accepted;
        }

        /// <summary>Removes product. Returns how much was actually taken.</summary>
        public float Remove(string drugId, float amount)
        {
            if (string.IsNullOrEmpty(drugId) || amount <= 0f) return 0f;

            var held = Get(drugId);
            var taken = Math.Min(amount, held);
            if (taken <= 0f) return 0f;

            var left = held - taken;
            if (left < 0.005f) _units.Remove(drugId);
            else _units[drugId] = left;

            return taken;
        }

        public void Clear() => _units.Clear();

        /// <summary>Products currently held, in catalogue order.</summary>
        public List<DrugDef> Held(Drugs catalogue)
        {
            var list = new List<DrugDef>();
            foreach (var d in catalogue.All)
            {
                if (Get(d.Id) > 0.005f) list.Add(d);
            }
            return list;
        }

        // ---- persistence -------------------------------------------------------

        public Json ToJson()
        {
            var obj = Json.Object();
            obj.Set("capacity", Capacity);

            var stock = Json.Object();
            foreach (var kv in _units) stock.Set(kv.Key, Math.Round(kv.Value, 3));
            obj.Set("stock", stock);

            return obj;
        }

        public void LoadFrom(Json node)
        {
            _units.Clear();
            if (node == null || node.IsNull) return;

            Capacity = Math.Max(1f, node["capacity"].AsFloat(Capacity));

            var stock = node["stock"];
            foreach (var key in stock.Keys)
            {
                var v = stock[key].AsFloat(0f);
                if (v > 0.005f) _units[key] = v;
            }
        }
    }
}
