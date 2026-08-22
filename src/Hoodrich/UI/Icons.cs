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

        /// <summary>
        /// A ~BLIP_~ tag to draw instead of a texture, where the art wanted is a blip's.
        ///
        /// Blip sprites address the map and cannot be given to DRAW_SPRITE, but the art can be
        /// put in a string. So an icon that wants to BE a blip is drawn as text rather than as
        /// a sprite, and this is how a call site says which it is.
        /// </summary>
        public readonly string Blip;

        /// <summary>
        /// Art of our own to fall back to, in data\icons.
        ///
        /// For the icons where every one of the game's own candidates is a BORROWED sprite --
        /// crack, heroin and the pills all end their lists on a shop icon, and two of them end
        /// on the SAME shop icon, so on an install without the mp_specitem_ names two different
        /// drugs draw the same picture and neither is what it claims to be.
        /// </summary>
        public readonly string File;

        public Icon(string dict, params string[] textures)
        {
            Dict = dict;
            Textures = textures;
            Blip = "";
            File = "";
        }

        private Icon(string blip)
        {
            Dict = "";
            Textures = null;
            Blip = blip;
            File = "";
        }

        private Icon(string dict, string[] textures, string blip, string file)
        {
            Dict = dict;
            Textures = textures;
            Blip = blip;
            File = file;
        }

        /// <summary>The same icon, with a PNG of ours behind it.</summary>
        public Icon WithFile(string png)
        {
            return new Icon(Dict, Textures, Blip, png);
        }

        public bool HasFile => !string.IsNullOrEmpty(File);

        /// <summary>An icon that is a blip rather than a texture.</summary>
        public static Icon FromBlip(string tag)
        {
            return new Icon(tag);
        }

        public bool IsBlip => !string.IsNullOrEmpty(Blip);

        public bool IsSet => IsBlip || HasFile ||
                             (!string.IsNullOrEmpty(Dict) && Textures != null && Textures.Length > 0);

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
        public static readonly Icon Weed = new Icon(Inventory, "mp_specitem_weed").WithFile("bong.png");
        public static readonly Icon Coke = new Icon(Inventory, "mp_specitem_coke").WithFile("coke.png");
        /// <summary>
        /// Crack, ecstasy and heroin have no sprite of their own anywhere in the game, so each
        /// borrows one that reads at a glance: the rocket for a rock, the pill for a pill, and
        /// the yoga figure for the nod. Several candidates each, because these live in the
        /// activity and shop dictionaries and which names an install actually has varies --
        /// anything that resolves wins, and if none do the item keeps its text glyph.
        /// </summary>
        public static readonly Icon Crack = new Icon(Menu,
            "mp_specitem_crack", "shop_ammo_icon_a", "shop_franklin_icon_a").WithFile("crystal.png");
        public static readonly Icon Meth = new Icon(Inventory, "mp_specitem_meth").WithFile("meth.png");
        public static readonly Icon Heroin = new Icon(Menu,
            "mp_specitem_heroin", "shop_michael_icon_a", "shop_health_icon_a").WithFile("heroin.png");
        /// <summary>
        /// Oxycodone.
        ///
        /// It was radar_crim_wanted as a ~BLIP_~ tag, on the strength of the tag working in
        /// text. It does not work HERE -- the dialogue row drew nothing at all, which is worse
        /// than a wrong picture because there is no way to tell it from a broken icon.
        ///
        /// Back to textures, and the list ends on one this install is known to have: the log
        /// shows shop_health_icon_a resolving for The Numbers, so the row can no longer come
        /// out empty whatever the first two do.
        /// </summary>
        public static readonly Icon Ecstasy = new Icon(Menu,
            "mp_specitem_pills", "mp_specitem_ecstasy", "shop_health_icon_a").WithFile("pills.png");

        // Actions.
        public static readonly Icon Money = new Icon(Menu, "shop_money_icon_a").WithFile("money.png");
        public static readonly Icon Cash = new Icon(Inventory, "mp_specitem_cash").WithFile("cash.png");
        public static readonly Icon Guns = new Icon(Menu, "shop_gunclub_icon_a").WithFile("guns.png");
        public static readonly Icon Garage = new Icon(Menu, "shop_garage_icon_a").WithFile("garage.png");
        public static readonly Icon Mask = new Icon(Menu, "shop_mask_icon_a").WithFile("mask.png");
        public static readonly Icon Health = new Icon(Menu, "shop_health_icon_a").WithFile("health.png");
        public static readonly Icon Ammo = new Icon(Menu, "shop_ammo_icon_a").WithFile("ammo.png");
        public static readonly Icon Tattoo = new Icon(Menu, "shop_tattoos_icon_a").WithFile("tattoo.png");

        /// <summary>
        /// What you are carrying.
        ///
        /// Several candidates because this one has been guessed wrong twice: the first name the
        /// install actually has wins, and the last is the same sprite the weapons wedge already
        /// proves renders, so the wedge cannot end up blank again.
        /// </summary>
        public static readonly Icon Stash = new Icon(Menu,
            "shop_ammo_icon_a", "shop_clothing_icon_a", "shop_garage_icon_a", "shop_gunclub_icon_a").WithFile("stash.png");
        public static readonly Icon Warning = new Icon(Menu, "mp_alerttriangle").WithFile("warning.png");
        public static readonly Icon Tick = new Icon(Menu, "shop_tick_icon").WithFile("tick.png");
        public static readonly Icon Locked = new Icon(Menu, "shop_lock").WithFile("locked.png");

        /// <summary>The sprite for a drug id, or an unset icon if it has none.</summary>
        /// <summary>
        /// A set's own mark.
        ///
        /// Geometry rather than heraldry, and deliberately so: nine crests all drawn as crests
        /// are nine grey smudges at the size these render, whereas nine clearly DIFFERENT
        /// silhouettes can be told apart before you have learned which is which. Tinted by the
        /// gang's own colour at the call site.
        ///
        /// A gang the file has never heard of gets nothing rather than somebody else's mark --
        /// wearing another set's colours by accident is worse than wearing none.
        /// </summary>
        public static Icon ForGang(string gangId)
        {
            if (string.IsNullOrEmpty(gangId)) return default(Icon);

            switch (gangId.ToLowerInvariant())
            {
                case "families": return FromFile("gang_families.png");
                case "ballas": return FromFile("gang_ballas.png");
                case "vagos": return FromFile("gang_vagos.png");
                case "aztecas": return FromFile("gang_aztecas.png");
                case "marabunta": return FromFile("gang_marabunta.png");
                case "lost": return FromFile("gang_lost.png");
                case "triads": return FromFile("gang_triads.png");
                case "armenians": return FromFile("gang_armenians.png");
                case "koreans": return FromFile("gang_koreans.png");
                default: return default(Icon);
            }
        }

        /// <summary>Art of ours with no game texture behind it at all.</summary>
        public static Icon FromFile(string png)
        {
            return new Icon("", new string[0]).WithFile(png);
        }

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

                // The joint drawing, which came free. weed.png WAS the weed icon until weed
                // took the bong, and a pre-roll is exactly what it is a picture of -- so the
                // one product on the wheel still falling back to a text glyph gets the art
                // that was drawn for it in the first place.
                case "xanax": return FromFile("xanax.png");
                default: return new Icon();
            }
        }
    }
}
