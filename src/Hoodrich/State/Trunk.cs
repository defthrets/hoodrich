using System;
using System.Collections.Generic;
using Hoodrich.Core;
using Hoodrich.Economy;

namespace Hoodrich.State
{
    /// <summary>
    /// What is in the boot of one of your cars.
    ///
    /// A STASH ON WHEELS, and deliberately not much more than that. Product is the same Stash
    /// the pockets and the house use, so purity, packaging and the two-way move are the code
    /// that already works. Guns are the locker's own row format -- "WEAPON|ammo|part|part" --
    /// so what goes in comes out with the same magazine and the same suppressor. Food is a bag
    /// of counts, because that is all Bare Minimum needs to hand back.
    ///
    /// Keyed by the owned car's id and saved with everything else, so it survives the car being
    /// stood back up, a reload, and a restart. The car being wrecked or sold is the owner's
    /// problem: the record stays until it is asked to go, and Boot asks.
    /// </summary>
    internal sealed class Trunk
    {
        /// <summary>Product, in the same shape as the pockets. A boot holds more than a jacket.</summary>
        public readonly Stash Stash = new Stash { Capacity = BootGrams };

        /// <summary>Guns, as "WEAPON_ID|ammo|COMPONENT|COMPONENT...". See Weapons.GunLocker for the parts half.</summary>
        public readonly List<string> Guns = new List<string>();

        /// <summary>Food, by Bare Minimum's own ids.</summary>
        public readonly Dictionary<string, int> Food = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public const float BootGrams = 800f;
        public const int BootGuns = 6;
        public const int BootFood = 24;

        public bool IsEmpty => Stash.Total <= 0.005f && Guns.Count == 0 && FoodCount == 0;

        public int FoodCount
        {
            get
            {
                var n = 0;
                foreach (var kv in Food) n += kv.Value;
                return n;
            }
        }

        public Json ToJson()
        {
            var obj = Json.Object().Set("stash", Stash.ToJson());

            var guns = Json.Array();
            foreach (var row in Guns) if (!string.IsNullOrEmpty(row)) guns.Add(Json.Str(row));
            obj.Set("guns", guns);

            var food = Json.Object();
            foreach (var kv in Food) if (kv.Value > 0) food.Set(kv.Key, kv.Value);
            obj.Set("food", food);

            return obj;
        }

        public static Trunk From(Json node)
        {
            var t = new Trunk();
            if (node == null || node.IsNull) return t;

            try { t.Stash.LoadFrom(node["stash"]); } catch { /* an empty boot */ }
            t.Stash.Capacity = BootGrams;

            foreach (var g in node["guns"].Items)
            {
                var row = g.AsString("");
                if (!string.IsNullOrEmpty(row)) t.Guns.Add(row);
            }

            var food = node["food"];
            foreach (var key in food.Keys)
            {
                var n = food[key].AsInt(0);
                if (n > 0) t.Food[key] = n;
            }

            return t;
        }
    }
}
