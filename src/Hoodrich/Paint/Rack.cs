using System.Drawing;

namespace Hoodrich.Paint
{
    /// <summary>
    /// One entry on the rack.
    ///
    /// A NAME AND A SHEEN, not just a colour, because two of them are not flat paint and the
    /// difference has to survive being handed to the sprayer and to whatever is drawing the
    /// swatch. Three parallel arrays would have been the smaller change and the wrong one --
    /// this is exactly the shape of thing where somebody adds a colour to one array, misses
    /// the third, and the eleventh swatch gets the tenth's name.
    /// </summary>
    internal sealed class Swatch
    {
        public readonly Color Colour;
        public readonly string Name;

        /// <summary>
        /// How far this paint's shade wanders from mark to mark. 0 is ordinary flat paint.
        /// </summary>
        public readonly float Sheen;

        public Swatch(Color colour, string name, float sheen)
        {
            Colour = colour;
            Name = name;
            Sheen = sheen;
        }

        public bool Metallic { get { return Sheen > 0f; } }
    }

    /// <summary>
    /// The colours, in one place for both mods.
    ///
    /// NOT called Palette, which is what it wants to be called: Posted Up already has a
    /// UI.Palette for its own chrome, its phone screens have both namespaces open, and two
    /// types of that name in scope is a compile error rather than a matter of taste.
    ///
    /// This used to be a pair of arrays inside each mod's own UI file, which meant adding a
    /// colour was two edits in two repositories that no tool checked against each other -- the
    /// precise failure the shared engine exists to stop. It lives here now, so the phone app
    /// and the F3 panel cannot disagree about what "gold" is.
    /// </summary>
    internal static class Rack
    {
        /// <summary>
        /// Spread round the wheel rather than picked for prettiness, so whatever somebody has
        /// in mind when they think "I want that X" has something near it -- then the two
        /// neutrals, which is where most actual graffiti lives, and then the two metallics,
        /// which is where the rest of it lives.
        /// </summary>
        public static readonly Swatch[] All =
        {
            new Swatch(Color.FromArgb(255, 228,  46,  46), "red",    0f),
            new Swatch(Color.FromArgb(255, 244, 130,  30), "orange", 0f),
            new Swatch(Color.FromArgb(255, 245, 218,  50), "yellow", 0f),
            new Swatch(Color.FromArgb(255, 122, 214,  56), "lime",   0f),
            new Swatch(Color.FromArgb(255,  40, 180, 120), "green",  0f),
            new Swatch(Color.FromArgb(255,  50, 190, 226), "cyan",   0f),
            new Swatch(Color.FromArgb(255,  52, 110, 226), "blue",   0f),
            new Swatch(Color.FromArgb(255, 140,  76, 220), "purple", 0f),
            new Swatch(Color.FromArgb(255, 240, 100, 180), "pink",   0f),
            new Swatch(Color.FromArgb(255, 245, 245, 245), "white",  0f),

            // NOT PURE ZERO. The decal arguments multiply the texture, so 0,0,0 is a true
            // black that reads as a hole punched in the wall rather than as paint on it -- and
            // it takes the plume down with it, leaving nothing to aim by. A hair above black
            // keeps both legible and is indistinguishable from black on a wall.
            new Swatch(Color.FromArgb(255,  20,  20,  22), "black",  0f),

            // ---- and the two that are not flat paint ----
            //
            // CHROME AND GOLD ARE NOT COLOURS, they are ranges, and that is the whole trick.
            // Nothing in this game can make a decal reflective: a mark is a texture multiplied
            // by one tint and it lands as flat as the wall behind it. Chrome sprayed as a flat
            // mid-grey is primer, and gold sprayed as a flat yellow-brown is mustard -- which
            // is what every "metallic" that is really just a tint looks like.
            //
            // What sells metal on a flat surface is that neighbouring marks disagree. Some
            // land near white and some land gunmetal, and the eye assembles the scatter into a
            // reflection because that is the only thing that scatter is ever caused by. So the
            // colour here is the MIDDLE of the range and Sheen is how far either side of it
            // each mark is allowed to fall.
            //
            // It costs nothing. The marks were being placed anyway; they are just no longer
            // all the same shade.
            new Swatch(Color.FromArgb(255, 198, 202, 212), "chrome", 0.55f),
            new Swatch(Color.FromArgb(255, 214, 170,  64), "gold",   0.45f)
        };

        /// <summary>
        /// A colour moved along its metallic range. -1 is the deepest shadow, +1 the hottest
        /// highlight, 0 the paint as picked.
        ///
        /// The bright end goes toward WHITE rather than simply brighter, because a highlight
        /// on metal loses its hue -- gold caught by the sun goes pale, not more orange. Scaling
        /// the channels up instead gives a bright mustard, which is the tell that something is
        /// tinted rather than lit.
        ///
        /// The dark end multiplies down and keeps the hue, because a shadow on gold IS bronze.
        /// It bottoms out at a third rather than at nothing: paint in shadow is still paint,
        /// and a mark at zero is the hole-in-the-wall problem the black swatch avoids.
        /// </summary>
        public static Color Lit(Color c, float t)
        {
            if (t > 1f) t = 1f;
            if (t < -1f) t = -1f;

            if (t >= 0f)
                return Blend(c, Color.FromArgb(255, 255, 255, 255), t);

            return Blend(c, Color.FromArgb(255, (int)(c.R * 0.32f),
                                                (int)(c.G * 0.32f),
                                                (int)(c.B * 0.32f)), -t);
        }

        private static Color Blend(Color a, Color b, float t)
        {
            return Color.FromArgb(255,
                                  (int)(a.R + (b.R - a.R) * t),
                                  (int)(a.G + (b.G - a.G) * t),
                                  (int)(a.B + (b.B - a.B) * t));
        }
    }
}
