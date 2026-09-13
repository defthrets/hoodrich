using System;
using System.Collections.Generic;
using Hoodrich.Core;
using Hoodrich.Economy;

namespace Hoodrich.State
{
    /// <summary>
    /// The bag. One of them, forever, and whatever is in it.
    ///
    /// THE ONLY WAY TO MOVE WEIGHT. Packaged product goes in a jacket pocket because packaged
    /// product is a handful of little bags; a kilo does not, and pretending it does is the
    /// reason the whole supply half of this mod has always felt weightless. You buy bulk, you
    /// carry bulk, and now there is one object in the world that can do that -- which means
    /// there is one object in the world you can lose, put down, forget, or be caught holding.
    ///
    /// TWENTY SLOTS, NOT A NUMBER OF GRAMS. A capacity in grams is a readout nobody can picture
    /// and a decision nobody can make; twenty spaces is a thing you can count on the screen and
    /// run out of in front of a man who is still holding the rest of it. Product takes a slot
    /// per hundred grams, food takes one each -- see Used -- so the trade is legible: four
    /// burgers is four hundred grams you are not carrying home.
    ///
    /// IT IS A THING, NOT A MENU. It hangs off the player (the vest slot, drawable seven, which
    /// is the strap across his chest) and it comes off onto the pavement where he drops it,
    /// with everything still inside. Nothing about that is a screen: you put it down, it is
    /// there, you walk away and it is still there, and so is everything in it. See Strap.
    ///
    /// SAVED WITH EVERYTHING ELSE, including where it is. A bag left in an alley is in that
    /// alley after a restart, which is the whole reason a player would ever trust it enough to
    /// leave one there.
    /// </summary>
    internal sealed class Satchel
    {
        /// <summary>How many things fit in it, and how much product counts as one thing.</summary>
        public const int Slots = 20;
        public const float SlotGrams = 100f;

        /// <summary>Product, in the same shape as the pockets, the boot and the house.</summary>
        public readonly Stash Stash = new Stash { Capacity = Slots * SlotGrams };

        /// <summary>Food, by Bare Minimum's own ids. See Trunk, which carries it the same way.</summary>
        public readonly Dictionary<string, int> Food =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Where it is: on him, or on the floor somewhere.
        ///
        /// A bag is never nowhere. Either he is wearing it or it is a real object at a real
        /// coordinate, and those are the only two states there are -- which is what makes
        /// losing it possible and makes finding it again just a matter of remembering.
        /// </summary>
        public bool Worn = true;

        public float DownX;
        public float DownY;
        public float DownZ;

        /// <summary>
        /// What was in the vest slot before the bag took it over.
        ///
        /// The strap IS a vest drawable, so putting the bag on overwrites whatever the wardrobe
        /// had there. Written down at the moment it is taken over and put back when the bag
        /// comes off, or a player who drops his bag is permanently wearing whatever the bag was
        /// standing in for.
        /// </summary>
        public int WasVest = -1;
        public int WasVestTexture;

        public int FoodCount
        {
            get
            {
                var n = 0;
                foreach (var kv in Food) n += kv.Value;
                return n;
            }
        }

        /// <summary>
        /// Slots in use: one per hundred grams of anything, one per item of food.
        ///
        /// ROUNDED UP, AND ON PURPOSE. Ten grams in the bag is a slot with ten grams in it --
        /// the space is spent the moment anything is in it, the same way a real bag does not
        /// get roomier because the parcel is small. It also stops a player carrying nineteen
        /// separate almost-nothings and calling the bag empty.
        /// </summary>
        public int Used
        {
            get
            {
                var grams = Stash.Total;
                var taken = grams <= 0.005f ? 0 : (int)Math.Ceiling(grams / SlotGrams);

                return taken + FoodCount;
            }
        }

        public int Free => Math.Max(0, Slots - Used);

        public bool IsEmpty => Stash.Total <= 0.005f && FoodCount == 0;

        /// <summary>Whether that much more product would still fit. See Used.</summary>
        public bool RoomFor(float grams)
        {
            if (grams <= 0f) return true;

            var after = Stash.Total + grams;
            var taken = after <= 0.005f ? 0 : (int)Math.Ceiling(after / SlotGrams);

            return taken + FoodCount <= Slots;
        }

        /// <summary>How much more product it would take before the slots ran out.</summary>
        public float GramsFree
        {
            get
            {
                var room = Slots - FoodCount;
                if (room <= 0) return 0f;

                var most = room * SlotGrams;
                var left = most - Stash.Total;

                return left <= 0f ? 0f : left;
            }
        }

        public Json ToJson()
        {
            var obj = Json.Object()
                .Set("stash", Stash.ToJson())
                .Set("worn", Worn)
                .Set("x", DownX)
                .Set("y", DownY)
                .Set("z", DownZ)
                .Set("wasVest", WasVest)
                .Set("wasVestTex", WasVestTexture);

            var food = Json.Object();
            foreach (var kv in Food) if (kv.Value > 0) food.Set(kv.Key, kv.Value);
            obj.Set("food", food);

            return obj;
        }

        public void FromJson(Json node)
        {
            if (node == null || node.Kind != JsonKind.Object) return;

            try { Stash.LoadFrom(node["stash"]); }
            catch { /* an empty bag */ }
            Stash.Capacity = Slots * SlotGrams;

            Worn = node["worn"].AsBool(true);

            DownX = node["x"].AsFloat(0f);
            DownY = node["y"].AsFloat(0f);
            DownZ = node["z"].AsFloat(0f);

            WasVest = node["wasVest"].AsInt(-1);
            WasVestTexture = node["wasVestTex"].AsInt(0);

            Food.Clear();

            var food = node["food"];

            if (food.Kind == JsonKind.Object)
            {
                foreach (var key in food.Keys)
                {
                    var n = food[key].AsInt(0);
                    if (n > 0) Food[key] = n;
                }
            }

            // A BAG THAT IS NEITHER WORN NOR ANYWHERE IS A BAG THAT IS GONE. An old save, or a
            // write that went in halfway, would leave it down at the origin -- under the map,
            // out at sea -- with everything in it. It goes back on his shoulder instead.
            if (!Worn && DownX == 0f && DownY == 0f && DownZ == 0f)
            {
                Worn = true;
                Log.Warn("Bag: it was down at nowhere in the save. Put it back on him.");
            }
        }
    }
}
