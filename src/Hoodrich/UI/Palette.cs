using System.Drawing;

namespace Hoodrich.UI
{
    /// <summary>Hoodrich's colour scheme. One place to retheme the whole UI.</summary>
    internal static class Palette
    {
        public static readonly Color Backdrop = Color.FromArgb(170, 6, 6, 8);

        /// <summary>Unselected wheel segment.</summary>
        public static readonly Color Segment = Color.FromArgb(190, 22, 24, 26);

        /// <summary>Segment under the cursor.</summary>
        public static readonly Color SegmentHover = Color.FromArgb(235, 200, 255, 50);

        /// <summary>Segment that exists but cannot be picked right now.</summary>
        public static readonly Color SegmentDisabled = Color.FromArgb(140, 16, 16, 18);

        public static readonly Color SegmentEdge = Color.FromArgb(200, 8, 8, 10);

        public static readonly Color Hub = Color.FromArgb(225, 12, 13, 15);
        public static readonly Color HubEdge = Color.FromArgb(220, 200, 255, 50);

        public static readonly Color Text = Color.FromArgb(235, 232, 236, 232);
        public static readonly Color TextDim = Color.FromArgb(170, 138, 142, 138);
        public static readonly Color TextOnHover = Color.FromArgb(255, 10, 12, 8);
        public static readonly Color TextDisabled = Color.FromArgb(120, 96, 98, 96);

        public static readonly Color Accent = Color.FromArgb(255, 200, 255, 50);
        public static readonly Color Cash = Color.FromArgb(255, 76, 217, 100);
        public static readonly Color Warn = Color.FromArgb(255, 255, 176, 32);
        public static readonly Color Danger = Color.FromArgb(255, 255, 59, 48);

        /// <summary>Same colour at a different alpha.</summary>
        public static Color Alpha(Color c, int alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

        /// <summary>Linear blend; t = 0 gives <paramref name="a"/>.</summary>
        public static Color Lerp(Color a, Color b, float t)
        {
            if (t <= 0f) return a;
            if (t >= 1f) return b;
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }
    }
}
