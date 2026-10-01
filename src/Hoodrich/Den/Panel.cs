using System.Collections.Generic;
using System.Drawing;
using GTA;
using Hoodrich.UI;

namespace Hoodrich.Den
{
    /// <summary>
    /// The card at the bottom of the screen a game is played on: a title, a few lines, a row
    /// of choices with one lit, your money, and what the keys do. Drawn every frame it is up
    /// on the same art as everything else in the mod.
    /// </summary>
    internal static class Panel
    {
        private const float Width = 0.42f;
        private const float Left = 0.5f - Width / 2f;
        private const float Bottom = 0.96f;
        private const float Line = 0.034f;
        private const float Pad = 0.012f;

        private const float TitleScale = 0.55f;
        private const float BodyScale = 0.40f;
        private const float HintScale = 0.30f;

        /// <summary>
        /// One card. Lines are drawn top to bottom; a null line is a gap. Choices are drawn as
        /// a row with the picked one lit. The hint sits under it all in the dim ink.
        /// </summary>
        public static void Draw(string title, IList<string> lines, IList<string> choices, int picked, string hint)
        {
            var rows = 1 + (lines == null ? 0 : lines.Count) + (choices == null || choices.Count == 0 ? 0 : 1) + 1 + 1;
            var height = Pad * 2f + rows * Line;
            var top = Bottom - height;

            UI.Draw.Panel(Left, top, Width, height, Theme.Body, Palette.Brand);

            var y = top + Pad + 0.004f;
            var cx = 0.5f;

            UI.Draw.Text(title, cx, y, TitleScale, Palette.Accent);
            y += Line * 1.15f;

            if (lines != null)
            {
                foreach (var line in lines)
                {
                    if (line != null) UI.Draw.Text(line, cx, y, BodyScale, Palette.Text, UI.Draw.FontBody);
                    y += Line;
                }
            }

            if (choices != null && choices.Count > 0)
            {
                // The row is centred: each choice gets an equal share of the card's width.
                var share = (Width - Pad * 2f) / choices.Count;

                for (var i = 0; i < choices.Count; i++)
                {
                    var x = Left + Pad + share * (i + 0.5f);
                    var lit = i == picked;

                    if (lit) UI.Draw.RoundRect(x - share / 2f + 0.003f, y - 0.004f, share - 0.006f, Line - 0.002f, 0.004f, Palette.SegmentHover);

                    UI.Draw.Text(choices[i], x, y, BodyScale, lit ? Palette.TextOnHover : Palette.Text);
                }

                y += Line;
            }

            UI.Draw.Text("Cash $" + Game.Player.Money.ToString("N0"), cx, y, BodyScale, Palette.Cash);
            y += Line;

            UI.Draw.Text(hint, cx, y, HintScale, Palette.TextDim);
        }

        /// <summary>A short line above the card while nothing is being chosen: a result, a wait.</summary>
        public static void Banner(string text, Color ink)
        {
            UI.Draw.Text(text, 0.5f, Bottom - 0.30f, 0.85f, ink, UI.Draw.FontPricedown);
        }
    }
}
