using System.Drawing;

namespace Hoodrich.Court
{
    /// <summary>
    /// The black bars stacked above the buttons with the score -- the game's own timer bars, the
    /// way every table in Posted Up's casino shows them. The court's own copy, like the buttons.
    /// </summary>
    internal static class CourtBars
    {
        private const string Dict = "timerbars";
        private const string Back = "all_black_bg";

        private const float CentreX = 0.9219f;
        private const float Width = 0.1563f;
        private const float Height = 0.0343f;
        private const float Bottom = 0.9245f;
        private const float Step = 0.0370f;
        private const float LabelRight = 0.906f;
        private const float ValueRight = 0.9896f;

        private static readonly Color Shade = Color.FromArgb(170, 255, 255, 255);

        /// <summary>Rows bottom up: a label, a value, and the ink for the value.</summary>
        public static void Draw(params object[] rows)
        {
            var ready = CourtDraw.EnsureTextureDict(Dict);
            var line = 0;

            for (var i = 0; i + 1 < rows.Length; i += 3)
            {
                var label = rows[i] as string;
                var value = rows[i + 1] as string;
                var ink = i + 2 < rows.Length && rows[i + 2] is Color c ? c : Color.White;

                var y = Bottom - Step * line;
                line++;

                if (ready) CourtDraw.Sprite(Dict, Back, CentreX, y, Width, Height, 0f, Shade);
                else CourtDraw.Rect(CentreX, y, Width, Height, Color.FromArgb(150, 0, 0, 0));

                CourtDraw.TextRight(label, LabelRight, y - 0.0112f, 0.30f, Color.White, CourtDraw.FontBody, false);
                CourtDraw.TextRight(value, ValueRight, y - 0.0175f, 0.45f, ink, CourtDraw.FontBody, false);
            }
        }
    }
}
