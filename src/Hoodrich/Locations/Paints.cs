namespace Hoodrich.Locations
{
    /// <summary>
    /// The game's paint colours by index, with the names the game gives them, so a respray
    /// menu can say "Metallic Torino Red" rather than "27". The order is the game's own and
    /// must stay that way: the number is what goes onto the car.
    /// </summary>
    internal static class Paints
    {
        public static readonly string[] Names =
        {
            "Metallic Black", "Metallic Graphite Black", "Metallic Black Steel", "Metallic Dark Silver",
            "Metallic Silver", "Metallic Blue Silver", "Metallic Steel Gray", "Metallic Shadow Silver",
            "Metallic Stone Silver", "Metallic Midnight Silver", "Metallic Gun Metal", "Metallic Anthracite Grey",
            "Matte Black", "Matte Gray", "Matte Light Grey", "Util Black",
            "Util Black Poly", "Util Dark Silver", "Util Silver", "Util Gun Metal",
            "Util Shadow Silver", "Worn Black", "Worn Graphite", "Worn Silver Grey",
            "Worn Silver", "Worn Blue Silver", "Worn Shadow Silver", "Metallic Red",
            "Metallic Torino Red", "Metallic Formula Red", "Metallic Blaze Red", "Metallic Graceful Red",
            "Metallic Garnet Red", "Metallic Desert Red", "Metallic Cabernet Red", "Metallic Candy Red",
            "Metallic Sunrise Orange", "Metallic Classic Gold", "Metallic Orange", "Matte Red",
            "Matte Dark Red", "Matte Orange", "Matte Yellow", "Util Red",
            "Util Bright Red", "Util Garnet Red", "Worn Red", "Worn Golden Red",
            "Worn Dark Red", "Metallic Dark Green", "Metallic Racing Green", "Metallic Sea Green",
            "Metallic Olive Green", "Metallic Green", "Metallic Gasoline Blue Green", "Matte Lime Green",
            "Util Dark Green", "Util Green", "Worn Dark Green", "Worn Green",
            "Worn Sea Wash", "Metallic Midnight Blue", "Metallic Dark Blue", "Metallic Saxony Blue",
            "Metallic Blue", "Metallic Mariner Blue", "Metallic Harbor Blue", "Metallic Diamond Blue",
            "Metallic Surf Blue", "Metallic Nautical Blue", "Metallic Bright Blue", "Metallic Purple Blue",
            "Metallic Spinnaker Blue", "Metallic Ultra Blue", "Metallic Bright Blue 2", "Util Dark Blue",
            "Util Midnight Blue", "Util Blue", "Util Sea Foam Blue", "Util Lightning Blue",
            "Util Maui Blue Poly", "Util Bright Blue", "Matte Dark Blue", "Matte Blue",
            "Matte Midnight Blue", "Worn Dark Blue", "Worn Blue", "Worn Light Blue",
            "Metallic Taxi Yellow", "Metallic Race Yellow", "Metallic Bronze", "Metallic Yellow Bird",
            "Metallic Lime", "Metallic Champagne", "Metallic Pueblo Beige", "Metallic Dark Ivory",
            "Metallic Choco Brown", "Metallic Golden Brown", "Metallic Light Brown", "Metallic Straw Beige",
            "Metallic Moss Brown", "Metallic Biston Brown", "Metallic Beechwood", "Metallic Dark Beechwood",
            "Metallic Choco Orange", "Metallic Beach Sand", "Metallic Sun Bleached Sand", "Metallic Cream",
            "Util Brown", "Util Medium Brown", "Util Light Brown", "Metallic White",
            "Metallic Frost White", "Worn Honey Beige", "Worn Brown", "Worn Dark Brown",
            "Worn Straw Beige", "Brushed Steel", "Brushed Black Steel", "Brushed Aluminium",
            "Chrome", "Worn Off White", "Util Off White", "Worn Orange",
            "Worn Light Orange", "Metallic Securicor Green", "Worn Taxi Yellow", "Police Car Blue",
            "Matte Green", "Matte Brown", "Worn Orange 2", "Matte White",
            "Worn White", "Worn Olive Army Green", "Pure White", "Hot Pink",
            "Salmon Pink", "Metallic Vermillion Pink", "Orange", "Green",
            "Blue", "Metallic Black Blue", "Metallic Black Purple", "Metallic Black Red",
            "Hunter Green", "Metallic Purple", "Metallic V Dark Blue", "Modshop Black",
            "Matte Purple", "Matte Dark Purple", "Metallic Lava Red", "Matte Forest Green",
            "Matte Olive Drab", "Matte Desert Brown", "Matte Desert Tan", "Matte Foliage Green",
            "Default Alloy", "Epsilon Blue", "Pure Gold", "Brushed Gold"
        };

        public static string Name(int index)
        {
            return index >= 0 && index < Names.Length ? Names[index] : "Colour " + index;
        }
    }
}
