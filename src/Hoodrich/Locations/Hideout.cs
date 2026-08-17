using GTA.Math;
using Hoodrich.Core;
using Hoodrich.Economy;

namespace Hoodrich.Locations
{
    /// <summary>
    /// A property on a block, and the stash inside it.
    ///
    /// Hideouts are bought, not given, and they belong to YOU rather than to a crew -- so
    /// switching gangs never costs you the product you have banked. Each one holds its own
    /// stash, which is what makes owning a second worth the money: one near where you deal,
    /// one near where you buy.
    ///
    /// Purchasable-property-as-stash is the idea taken from Ex41T's lsgangs-mod (MIT).
    /// </summary>
    internal sealed class Hideout
    {
        /// <summary>Stable id, derived from the zone. One hideout per block.</summary>
        public string Id = "";

        /// <summary>GET_NAME_OF_ZONE code this hideout sits in.</summary>
        public string ZoneCode = "";

        /// <summary>Friendly zone name, for the wheel.</summary>
        public string ZoneName = "";

        public Vector3 Position;

        public bool Owned;

        /// <summary>What it costs, fixed when the listing is created.</summary>
        public int Price;

        /// <summary>Product banked here. Off your person, so a death or bust cannot touch it.</summary>
        public readonly Stash Stash = new Stash();

        public Hideout(string zoneCode, string zoneName, Vector3 position, int price, float capacity)
        {
            Id = "hideout_" + zoneCode;
            ZoneCode = zoneCode;
            ZoneName = zoneName;
            Position = position;
            Price = price;
            Stash.Capacity = capacity;
        }

        private Hideout() { }

        public Json ToJson()
        {
            return Json.Object()
                .Set("zone", ZoneCode)
                .Set("zoneName", ZoneName)
                .Set("x", System.Math.Round(Position.X, 2))
                .Set("y", System.Math.Round(Position.Y, 2))
                .Set("z", System.Math.Round(Position.Z, 2))
                .Set("owned", Owned)
                .Set("price", Price)
                .Set("stash", Stash.ToJson());
        }

        public static Hideout FromJson(Json node)
        {
            if (node == null || node.IsNull) return null;

            var zone = node["zone"].AsString("");
            if (string.IsNullOrEmpty(zone)) return null;

            var h = new Hideout
            {
                Id = "hideout_" + zone,
                ZoneCode = zone,
                ZoneName = node["zoneName"].AsString(zone),
                Position = new Vector3(node["x"].AsFloat(), node["y"].AsFloat(), node["z"].AsFloat()),
                Owned = node["owned"].AsBool(false),
                Price = node["price"].AsInt(0)
            };

            h.Stash.LoadFrom(node["stash"]);
            return h;
        }
    }
}
