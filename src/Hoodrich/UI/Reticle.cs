using System.Drawing;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.UI
{
    /// <summary>
    /// Where the paint is about to go.
    ///
    /// THE MIDDLE OF THE SCREEN IS NOT AN APPROXIMATION HERE, IT IS THE ANSWER. The probe runs
    /// from the camera along the camera's direction, and that ray leaves the screen through its
    /// exact centre -- so a dot at 0.5, 0.5 is the pixel the probe goes through, not a guess at
    /// where you are pointing.
    ///
    /// It has to be drawn at all because the spray can look hides the weapon. With the model
    /// invisible the game has no reason to show its own reticle, and free-aiming something you
    /// cannot see the aim point of is guesswork.
    ///
    /// Drawn with this mod's own primitives rather than shared with the standalone: the engine
    /// is one implementation, the HUD is not, and this is HUD.
    /// </summary>
    internal static class Reticle
    {
        /// <summary>A dark backing, so a pale colour on a pale wall is still findable.</summary>
        private static readonly Color Shadow = Color.FromArgb(200, 0, 0, 0);

        public static void Draw(Color paint, bool aiming, bool spraying)
        {
            const float cx = 0.5f;
            const float cy = 0.5f;

            var c = Legible(paint);

            var dot = spraying ? 0.0044f : 0.0032f;
            var edge = 0.0015f;

            Hud.Rect(cx, cy, Hud.ToX(dot + edge * 2f), dot + edge * 2f, Shadow);
            Hud.Rect(cx, cy, Hud.ToX(dot), dot, c);

            // Ticks only while he is actually pointing at something. A permanent crosshair in
            // the middle of the screen is a thing you stop seeing, and then it is only clutter.
            if (!aiming && !spraying) return;

            const float gap = 0.011f;
            const float len = 0.009f;
            const float thin = 0.0016f;

            var half = thin * 0.5f;

            Tick(cx - Hud.ToX(half), cy - gap - len, Hud.ToX(thin), len, c);
            Tick(cx - Hud.ToX(half), cy + gap, Hud.ToX(thin), len, c);
            Tick(cx - Hud.ToX(gap + len), cy - half, Hud.ToX(len), thin, c);
            Tick(cx + Hud.ToX(gap), cy - half, Hud.ToX(len), thin, c);
        }

        private static void Tick(float left, float top, float w, float h, Color c)
        {
            Hud.RectFrom(left - 0.0008f, top - 0.0008f, w + 0.0016f, h + 0.0016f, Shadow);
            Hud.RectFrom(left, top, w, h, c);
        }

        /// <summary>
        /// Lifted until it can be seen. Black paint would otherwise give a black dot, and the
        /// one moment you most need the aim point is the one where you cannot find it.
        /// </summary>
        private static Color Legible(Color c)
        {
            const float Floor = 0.35f;

            var l = (0.299f * c.R + 0.587f * c.G + 0.114f * c.B) / 255f;
            if (l >= Floor) return c;

            var t = 1f - l / Floor;

            return Color.FromArgb(c.A,
                                  (int)(c.R + (255 - c.R) * t),
                                  (int)(c.G + (255 - c.G) * t),
                                  (int)(c.B + (255 - c.B) * t));
        }
    }
}
