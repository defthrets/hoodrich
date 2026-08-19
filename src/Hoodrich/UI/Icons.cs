namespace Hoodrich.UI
{
    /// <summary>
    /// A streamed game texture, named so call sites read as intent.
    ///
    /// Carries alternatives rather than one name. A texture that does not exist fails silently
    /// -- the dictionary streams, the sprite call succeeds, and nothing is drawn -- so a single
    /// wrong guess leaves a wedge permanently blank with nothing to debug. With a list, the
    /// first name that actually exists wins and a bad guess costs nothing.
    /// </summary>
    internal struct Icon
    {
        public readonly string Dict;
        public readonly string[] Textures;

        public Icon(string dict, params string[] textures)
        {
            Dict = dict;
            Textures = textures;
        }

        public bool IsSet => !string.IsNullOrEmpty(Dict) && Textures != null && Textures.Length > 0;

        /// <summary>The first name the game actually has, or the first as a last resort.</summary>
        public string Resolve()
        {
            float aspect;
            return Resolve(out aspect);
        }

        /// <summary>
        /// As above, and reports the winning texture's own width-to-height ratio.
        ///
        /// Which candidate wins decides the shape, so the shape has to be measured here rather
        /// than assumed at the call site: these dictionaries mix square shop icons with wide
        /// banner art, and drawing both at 1:1 squashed the wide ones to half their width.
        /// </summary>
        public string Resolve(out float aspect)
        {
            aspect = 1f;
            if (!IsSet) return "";

            foreach (var name in Textures)
            {
                if (Draw.HasTexture(Dict, name, out aspect)) return name;
            }

            // Nothing in the list is in this install, so say so. Handing back the first name
            // anyway meant the wedge drew a texture that does not exist -- which renders as
            // nothing at all and is indistinguishable from a broken icon. Empty lets the item
            // fall back to its text glyph, which is the whole point of having one.
            aspect = 1f;
            return "";
        }
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
        /// <summary>
        /// Crack, ecstasy and heroin have no sprite of their own anywhere in the game, so each
        /// borrows one that reads at a glance: the rocket for a rock, the pill for a pill, and
        /// the yoga figure for the nod. Several candidates each, because these live in the
        /// activity and shop dictionaries and which names an install actually has varies --
        /// anything that resolves wins, and if none do the item keeps its text glyph.
        /// </summary>
        public static readonly Icon Crack = new Icon(Menu,
            "mp_specitem_crack", "shop_ammo_icon_a", "shop_franklin_icon_a");
        public static readonly Icon Meth = new Icon(Inventory, "mp_specitem_meth");
        public static readonly Icon Heroin = new Icon(Menu,
            "mp_specitem_heroin", "shop_michael_icon_a", "shop_health_icon_a");
        public static readonly Icon Ecstasy = new Icon(Menu,
            "mp_specitem_pills", "mp_specitem_ecstasy", "shop_health_icon_a");

        // Actions.
        public static readonly Icon Money = new Icon(Menu, "shop_money_icon_a");
        public static readonly Icon Cash = new Icon(Inventory, "mp_specitem_cash");
        public static readonly Icon Guns = new Icon(Menu, "shop_gunclub_icon_a");
        public static readonly Icon Garage = new Icon(Menu, "shop_garage_icon_a");
        public static readonly Icon Mask = new Icon(Menu, "shop_mask_icon_a");
        public static readonly Icon Health = new Icon(Menu, "shop_health_icon_a");
        public static readonly Icon Ammo = new Icon(Menu, "shop_ammo_icon_a");
        public static readonly Icon Tattoo = new Icon(Menu, "shop_tattoos_icon_a");

        /// <summary>
        /// What you are carrying.
        ///
        /// Several candidates because this one has been guessed wrong twice: the first name the
        /// install actually has wins, and the last is the same sprite the weapons wedge already
        /// proves renders, so the wedge cannot end up blank again.
        /// </summary>
        public static readonly Icon Stash = new Icon(Menu,
            "shop_ammo_icon_a", "shop_clothing_icon_a", "shop_garage_icon_a", "shop_gunclub_icon_a");
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
