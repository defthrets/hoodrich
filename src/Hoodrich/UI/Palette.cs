using System;
using System.Drawing;

namespace Hoodrich.UI
{
    /// <summary>
    /// Hoodrich's colour scheme, tuned to sit next to the vanilla HUD rather than shout over it.
    ///
    /// The panels are near-black, rounded, and lit in GOLD and EMBER: the wash under a wordmark,
    /// the stroke on a rule, the plate under whatever is chosen, the frame that glides between
    /// things. See UI.Warm for all of that -- the screens take it from there rather than
    /// each having an opinion. Colour beyond that is spent only where it carries meaning --
    /// money green, warning amber, danger red -- exactly as the game's HUD does.
    ///
    /// THE PHONE IS THE EXCEPTION AND STAYS GREEN. It is a handset in his hand rather than a
    /// screen, with its own make and its own colour, and its chrome -- the mark, the bars, the
    /// rules, the cursor -- is the set's green. See PhoneMenu.
    ///
    /// Accent below is the old near-white highlight, kept for the vanilla-wheel reasoning it
    /// carries and for anything that has not moved over; nothing on the panels uses it now.
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
        /// The two warm colours the pocket screen is built on, from the day it stopped being
        /// white on black. Gold is the light end and ember the hot end: a chosen tile runs
        /// from one to the other top to bottom, the rules carry a stroke of ember, and the
        /// frame that glides between tiles is a brighter gold still. Neither MEANS anything
        /// the way amber and red do above -- they are what that screen is made of.
        /// </summary>
        public static readonly Color Gold = Color.FromArgb(255, 250, 196, 64);
        public static readonly Color Ember = Color.FromArgb(255, 238, 112, 30);

        /// <summary>
        /// The verified blue, and the only blue in the whole palette.
        ///
        /// Deliberately not the accent. Everything else on these screens is white, green, amber
        /// or red and means something about YOU -- money, warning, trouble. This one means
        /// something about somebody else, so it is the one colour that is not part of that
        /// conversation.
        /// </summary>
        public static readonly Color Verified = Color.FromArgb(255, 72, 158, 240);

        /// <summary>
        /// What the block reckons of your product.
        ///
        /// Its own colour, and specifically NOT Cash. Money is green, the set is green, the
        /// wordmark is green and the bar was green as well -- four different facts in one
        /// colour, on one screen, is a screen with nothing to look at. Standing is a separate
        /// question from money and now looks like one.
        ///
        /// Cool rather than warm, because the two warm colours on this HUD already mean things:
        /// amber is running out and red is going wrong.
        /// </summary>
        public static readonly Color Standing = Color.FromArgb(255, 116, 202, 232);

        /// <summary>Same colour at a different alpha.</summary>
        /// <summary>
        /// Red through to green, by where a nought-to-one figure sits.
        ///
        /// A hue sweep rather than a set of bands, because the thing being shown is continuous
        /// and bands would put a step in it -- a reputation one point either side of a boundary
        /// is not a different KIND of reputation. Zero is red, a half is yellow, and one is
        /// green, which passes through orange and through the yellow-green on the way without
        /// any of those being written down anywhere.
        ///
        /// Saturation and value are held constant so the whole sweep reads at the same weight.
        /// Left to itself, a hue ramp dims in the yellows and the middle of the bar looks like
        /// a fault rather than like the middle.
        /// </summary>
        public static Color Spectrum(float t)
        {
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;

            return FromHue(t * 120f, 0.74f, 0.88f);
        }

        /// <summary>Hue in degrees, saturation and value nought to one, out to a colour.</summary>
        private static Color FromHue(float h, float s, float v)
        {
            var c = v * s;
            var x = c * (1f - Math.Abs((h / 60f) % 2f - 1f));
            var m = v - c;

            float r = 0f, g = 0f, b = 0f;

            if (h < 60f) { r = c; g = x; }
            else if (h < 120f) { r = x; g = c; }
            else if (h < 180f) { g = c; b = x; }
            else if (h < 240f) { g = x; b = c; }
            else if (h < 300f) { r = x; b = c; }
            else { r = c; b = x; }

            return Color.FromArgb(255,
                                  (int)((r + m) * 255f),
                                  (int)((g + m) * 255f),
                                  (int)((b + m) * 255f));
        }

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
