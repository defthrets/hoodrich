namespace Hoodrich.UI
{
    /// <summary>A streamed game texture, named so call sites read as intent.</summary>
    internal struct Icon
    {
        public readonly string Dict;
        public readonly string Texture;

        public Icon(string dict, string texture)
        {
            Dict = dict;
            Texture = texture;
        }

        public bool IsSet => !string.IsNullOrEmpty(Dict) && !string.IsNullOrEmpty(Texture);
    }

    /// <summary>
    /// The wheel's iconography, taken from the game's own texture dictionaries.
    ///
    /// Drawing the mod's art rather than inventing it is the whole reason the wheel reads as
    /// stock: these are the same sprites GTA uses for its inventory and shop menus, so they
    /// match the game's weight, palette and rendering exactly. Weapons already did this with
    /// their model art; this extends it to everything else.
    ///
    /// A texture that is missing on a given install simply does not stream, and the item falls
    /// back to its text symbol -- so a wrong guess here costs a glyph, never a crash.
    /// </summary>
    internal static class Icons
    {
        private const string Inventory = "mpinventory";
        private const string Menu = "commonmenu";

        // Product. The multiplayer inventory dictionary carries one sprite per drug, which is
        // as close to purpose-made art as this mod is ever going to get.
        public static readonly Icon Weed = new Icon(Inventory, "mp_specitem_weed");
        public static readonly Icon Coke = new Icon(Inventory, "mp_specitem_coke");
        public static readonly Icon Crack = new Icon(Inventory, "mp_specitem_crack");
        public static readonly Icon Meth = new Icon(Inventory, "mp_specitem_meth");
        public static readonly Icon Heroin = new Icon(Inventory, "mp_specitem_heroin");
        public static readonly Icon Ecstasy = new Icon(Inventory, "mp_specitem_ecstasy");

        // Actions.
        public static readonly Icon Money = new Icon(Menu, "shop_money_icon_a");
        public static readonly Icon Cash = new Icon(Inventory, "mp_specitem_cash");
        public static readonly Icon Guns = new Icon(Menu, "shop_gunclub_icon_a");
        public static readonly Icon Garage = new Icon(Menu, "shop_garage_icon_a");
        public static readonly Icon Clothes = new Icon(Menu, "shop_clothing_icon_a");
        public static readonly Icon Mask = new Icon(Menu, "shop_mask_icon_a");
        public static readonly Icon Health = new Icon(Menu, "shop_health_icon_a");
        public static readonly Icon Armour = new Icon(Menu, "shop_armour_icon_a");
        public static readonly Icon Ammo = new Icon(Menu, "shop_ammo_icon_a");
        public static readonly Icon Tattoo = new Icon(Menu, "shop_tattoos_icon_a");
        public static readonly Icon Warning = new Icon(Menu, "mp_alerttriangle");
        public static readonly Icon Tick = new Icon(Menu, "shop_tick_icon");
        public static readonly Icon Locked = new Icon(Menu, "shop_lock");

        /// <summary>The sprite for a drug id, or an unset icon if it has none.</summary>
        public static Icon ForDrug(string drugId)
        {
            switch ((drugId ?? "").ToLowerInvariant())
            {
                case "weed": return Weed;
                case "coke":
                case "cocaine": return Coke;
                case "crack": return Crack;
                case "meth": return Meth;
                case "heroin": return Heroin;
                case "ecstasy":
                case "pills": return Ecstasy;
                default: return new Icon();
            }
        }
    }
}
