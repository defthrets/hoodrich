using System.Drawing;

namespace Hoodrich.UI
{
    /// <summary>
    /// Hoodrich's colour scheme, tuned to sit next to the vanilla HUD rather than shout over it.
    ///
    /// The reference is GTA V's own weapon wheel: near-black translucent wedges, a near-WHITE
    /// highlight with dark text punched out of it, and white labels. Colour is spent only where
    /// it carries meaning -- money green, warning amber, danger red -- exactly as the game's HUD
    /// does. There is deliberately no branded accent hue; on the vanilla wheel, white IS the accent.
    /// </summary>
    internal static class Palette
    {
        /// <summary>Full-screen dim behind the wheel. Vanilla darkens lightly, not to black.</summary>
        public static readonly Color Backdrop = Color.FromArgb(140, 0, 0, 0);

        /// <summary>Unselected wedge.</summary>
        public static readonly Color Segment = Color.FromArgb(200, 10, 12, 14);

        /// <summary>Wedge under the cursor: near-white, like the vanilla selection.</summary>
        public static readonly Color SegmentHover = Color.FromArgb(240, 240, 242, 240);

        /// <summary>Present but not pickable.</summary>
        public static readonly Color SegmentDisabled = Color.FromArgb(200, 44, 46, 50);


        public static readonly Color Hub = Color.FromArgb(225, 8, 9, 11);
        /// <summary>Solid header strip on a panel, the way GTA's own menus title a column.</summary>
        public static readonly Color PanelHeader = Color.FromArgb(235, 22, 24, 26);

        /// <summary>Alternating row wash. GTA menus stripe their lists very faintly.</summary>
        public static readonly Color PanelRowAlt = Color.FromArgb(26, 255, 255, 255);

        public static readonly Color Text = Color.FromArgb(245, 255, 255, 255);
        public static readonly Color TextDim = Color.FromArgb(190, 176, 179, 181);

        /// <summary>Text drawn on top of a highlighted (near-white) wedge.</summary>
        public static readonly Color TextOnHover = Color.FromArgb(255, 16, 18, 20);

        public static readonly Color TextDisabled = Color.FromArgb(255, 150, 152, 156);

        /// <summary>White, matching the vanilla wheel. Used for page titles and rules.</summary>
        public static readonly Color Accent = Color.FromArgb(255, 245, 245, 245);

        /// <summary>GTA HUD money green.</summary>
        public static readonly Color Cash = Color.FromArgb(255, 126, 190, 79);

        public static readonly Color Warn = Color.FromArgb(255, 232, 177, 44);
        public static readonly Color Danger = Color.FromArgb(255, 214, 69, 58);

        /// <summary>
        /// The verified blue, and the only blue in the whole palette.
        ///
        /// Deliberately not the accent. Everything else on these screens is white, green, amber
        /// or red and means something about YOU -- money, warning, trouble. This one means
        /// something about somebody else, so it is the one colour that is not part of that
        /// conversation.
        /// </summary>
        public static readonly Color Verified = Color.FromArgb(255, 72, 158, 240);

        /// <summary>Same colour at a different alpha.</summary>
        public static Color Alpha(Color c, int alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

        /// <summary>
        /// Readable text colour for a given wedge fill. Gang tints range from pale yellow to deep
        /// maroon, so picking black-or-white by luminance keeps every label legible.
        /// </summary>
        public static Color TextOn(Color fill)
        {
            var luma = (0.2126f * fill.R + 0.7152f * fill.G + 0.0722f * fill.B) / 255f;
            return luma > 0.55f ? TextOnHover : Text;
        }
    }
}
