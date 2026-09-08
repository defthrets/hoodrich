using System;
using System.Drawing;
using GTA;
using Hoodrich.Economy;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.UI
{
    /// <summary>
    /// The parts the three product screens are built from: the pocket, the stash house and
    /// the kitchen.
    ///
    /// They were three screens that had each grown their own letterhead, their own capacity
    /// bar, their own idea of a key hint -- one wrote the keys as a run-on sentence, one as
    /// icons, one as both -- and the same product looked like three different things in
    /// three rooms. Everything here is one thing drawn one way, and the screens compose it.
    ///
    /// EVERY ANIMATION IS A FUNCTION OF TIME, never of frames. The meters ease with Eased,
    /// the chevrons ride the clock, the key caps arrive with the panel. Nothing here has to be
    /// ticked, so nothing here can stall.
    ///
    /// BUDGET. GTA drops rectangles past about four hundred in a frame on this install, so
    /// every piece below says what it costs. A key cap is two rectangles; a meter is four; a
    /// tag is one. Rounded corners are the expensive thing (fifty rectangles a chip) and are
    /// spent on the panel only.
    /// </summary>
    internal static class UiKit
    {
        // ======================================================================
        // The letterhead
        // ======================================================================

        /// <summary>How tall the head is: wordmark, title row, one rule. Meters go under it.</summary>
        public const float HeadH = 0.078f;

        /// <summary>
        /// The wordmark, then one title row: a picture, the room in capitals, what it is in
        /// small dim words after it, and a figure on the right. Returns the y under the rule.
        ///
        /// ONE ROW WHERE THERE WERE THREE. The screens used to stack the wordmark, a blurb
        /// line, a rule, a heading and a count before anything you could act on -- a sixth of
        /// the screen of introduction. The blurb rides the title now and the count sits on the
        /// same line, so the first thing under the mark is the room.
        /// </summary>
        public static float Head(float left, float top, float w, float pad, string icon,
                                 string title, string blurb, string right, float arrive)
        {
            var middle = left + w * 0.5f;
            var x = left + pad;
            var edge = left + w - pad;

            Hud.BrandCentre(middle, top + 0.021f, 0.019f,
                            Palette.Alpha(Palette.Text, (int)(225f * arrive)));

            var y = top + 0.044f;
            var tx = x;

            if (!string.IsNullOrEmpty(icon) &&
                Hud.File(icon, x + Hud.ToX(HeadIcon) * 0.5f, y + 0.0085f, HeadIcon, 0f,
                         Palette.Alpha(Palette.Brand, (int)(235f * arrive))))
            {
                tx = x + Hud.ToX(HeadIcon) + 0.006f;
            }

            Hud.Text(title, tx, y, 0.31f, Palette.Alpha(Palette.Text, (int)(255f * arrive)),
                     Hud.FontLabel, centre: false);

            if (!string.IsNullOrEmpty(blurb))
            {
                var after = tx + Hud.MeasureText(title, 0.31f, Hud.FontLabel) + 0.010f;

                Hud.Text(blurb, after, y + 0.0035f, 0.25f,
                         Palette.Alpha(Palette.TextDim, (int)(175f * arrive)),
                         Hud.FontBody, centre: false);
            }

            if (!string.IsNullOrEmpty(right))
            {
                Hud.TextRight(right, edge, y + 0.002f, 0.25f,
                              Palette.Alpha(Palette.TextDim, (int)(200f * arrive)), Hud.FontLabel);
            }

            Theme.Rule(x, top + HeadH - 0.006f, w - pad * 2f, arrive);

            return top + HeadH;
        }

        private const float HeadIcon = 0.017f;

        // ======================================================================
        // The meter
        // ======================================================================

        /// <summary>A meter is a label line and a track under it, this tall together.</summary>
        public const float MeterH = 0.034f;

        /// <summary>
        /// Green while there is room, amber as it fills, red when it is full. The one order
        /// of those three that reads as filling up.
        /// </summary>
        public static Color MeterTint(float full)
        {
            if (full >= 0.9f) return Palette.Danger;
            if (full >= 0.7f) return Palette.Warn;
            return Palette.Brand;
        }

        /// <summary>
        /// A capacity meter: a picture and a name on the left, the figure on the right, and a
        /// track under both that the fill slides along. Four rectangles.
        ///
        /// SHOWN IS WHERE THE NEEDLE IS, FULL IS WHERE IT IS GOING. The caller eases shown
        /// toward full -- see Eased -- so the bar moves when something changes and holds still
        /// when nothing does. The tint follows the true figure, not the eased one, or a bar
        /// crossing into the red would go red a beat late.
        /// </summary>
        public static void Meter(float x, float y, float w, string icon, string label,
                                 string figure, float shown, float full, float arrive)
        {
            var tx = x;

            if (!string.IsNullOrEmpty(icon) &&
                Hud.File(icon, x + Hud.ToX(MeterIcon) * 0.5f, y + 0.0075f, MeterIcon, 0f,
                         Palette.Alpha(Palette.Text, (int)(220f * arrive))))
            {
                tx = x + Hud.ToX(MeterIcon) + 0.005f;
            }

            Hud.Text(label, tx, y, 0.26f, Palette.Alpha(Palette.Text, (int)(240f * arrive)),
                     Hud.FontLabel, centre: false);

            var tint = MeterTint(full);

            Hud.TextRight(figure, x + w, y + 0.001f, 0.25f,
                          Palette.Alpha(full >= 0.7f ? tint : Palette.TextDim, (int)(230f * arrive)),
                          Hud.FontBody);

            var barY = y + 0.021f;

            if (shown < 0f) shown = 0f;
            if (shown > 1f) shown = 1f;

            // The track, the fill, a bright tip on the fill so the end of it is a point
            // rather than an edge, and a faint line under the lot.
            Hud.RectFrom(x, barY, w, MeterTrack, Color.FromArgb((int)(160f * arrive), 24, 30, 26));

            if (shown > 0.001f)
            {
                var fillW = w * shown;

                Hud.RectFrom(x, barY, fillW, MeterTrack, Palette.Alpha(tint, (int)(235f * arrive)));

                var tipW = Math.Min(fillW, Hud.ToX(0.0028f));

                Hud.RectFrom(x + fillW - tipW, barY, tipW, MeterTrack,
                             Color.FromArgb((int)(150f * arrive), 255, 255, 255));
            }

            Hud.RectFrom(x, barY + MeterTrack, w, 0.0008f, Palette.Alpha(tint, (int)(70f * arrive)));
        }

        private const float MeterTrack = 0.0065f;
        private const float MeterIcon = 0.015f;

        /// <summary>What a stash is holding, as words for a meter.</summary>
        public static string Holding(Stash stash)
        {
            if (stash == null) return "0 / 0g";

            return stash.Total.ToString("0") + " / " + stash.Capacity.ToString("0") + "g";
        }

        /// <summary>How full a stash is, nought to one.</summary>
        public static float Full(Stash stash)
        {
            if (stash == null || stash.Capacity <= 0.01f) return 0f;

            var f = stash.Total / stash.Capacity;
            return f < 0f ? 0f : f > 1f ? 1f : f;
        }

        // ======================================================================
        // Key caps
        // ======================================================================

        /// <summary>The footer: a rule and one line of key caps, this tall together.</summary>
        public const float FootH = 0.040f;

        /// <summary>
        /// One key and what it does: the key on a small dark cap, the errand in dim words
        /// after it. Two rectangles. Returns where the next one starts.
        ///
        /// A CAP, NOT A WORD. "BACKSPACE  DONE" is two words in the same colour and the same
        /// face, and which of them is the key is something you work out. A key drawn as a key
        /// -- a little block with a letter on it -- is read as a key before it is read at all,
        /// which is what the bottom of every keyboard looks like.
        ///
        /// An icon instead of a cap label draws the picture on the cap: the arrows, the drop
        /// mark. Either way the cap is the same height, so a row of them is a row.
        /// </summary>
        public static float Key(float x, float y, string cap, string icon, string words, float arrive)
        {
            var ink = (int)(255f * arrive);

            var capH = 0.0175f;
            var capW = string.IsNullOrEmpty(icon)
                ? Hud.MeasureText(cap, 0.22f, Hud.FontLabel) + 0.008f
                : Hud.ToX(capH) + 0.004f;

            var capTop = y - 0.0015f;

            Hud.RectFrom(x, capTop, capW, capH, Color.FromArgb((int)(36f * arrive), 255, 255, 255));
            Hud.RectFrom(x, capTop + capH - 0.0012f, capW, 0.0012f,
                         Palette.Alpha(Palette.BrandDeep, (int)(200f * arrive)));

            if (!string.IsNullOrEmpty(icon))
            {
                Hud.File(icon, x + capW * 0.5f, capTop + capH * 0.5f, capH * 0.72f, 0f,
                         Palette.Alpha(Palette.Text, ink));
            }
            else
            {
                Hud.Text(cap, x + capW * 0.5f, y, 0.22f, Palette.Alpha(Palette.Text, ink),
                         Hud.FontLabel, centre: true);
            }

            var wx = x + capW + 0.005f;

            Hud.Text(words, wx, y, 0.23f, Palette.Alpha(Palette.TextDim, (int)(215f * arrive)),
                     Hud.FontLabel, centre: false);

            return wx + Hud.MeasureText(words, 0.23f, Hud.FontLabel) + 0.014f;
        }

        /// <summary>The same, ending at a right edge: the way out, in the same corner on every screen.</summary>
        public static void KeyRight(float right, float y, string cap, string words, float arrive)
        {
            var capW = Hud.MeasureText(cap, 0.22f, Hud.FontLabel) + 0.008f;
            var wordsW = Hud.MeasureText(words, 0.23f, Hud.FontLabel);

            Key(right - capW - 0.005f - wordsW, y, cap, null, words, arrive);
        }

        /// <summary>What this player calls the buttons. Xbox letters, because that is what the game prints.</summary>
        public static string Confirm => Hud.OnPad ? "A" : "ENTER";
        public static string Back => Hud.OnPad ? "B" : "BACKSPACE";
        public static string Drop => Hud.OnPad ? "X" : "SPACE";
        public static string All => Hud.OnPad ? "HOLD A" : "SHIFT";

        // ======================================================================
        // Tags and words
        // ======================================================================

        /// <summary>
        /// A small label on a dark chip: WEIGHT, BAGGED, CUPBOARD. One rectangle. Returns its width.
        ///
        /// The word "(weight)" used to ride the end of every bulk row's name in the same face
        /// as the name, so "Marijuana  (weight)" read as a longer name. A tag is a different
        /// kind of thing from a name and now looks like one.
        /// </summary>
        public static float Tag(float x, float y, string text, Color ink, float arrive)
        {
            var w = Hud.MeasureText(text, 0.19f, Hud.FontLabel) + 0.007f;

            Hud.RectFrom(x, y + 0.0015f, w, 0.0145f, Palette.Alpha(ink, (int)(34f * arrive)));

            Hud.Text(text, x + w * 0.5f, y + 0.0005f, 0.19f, Palette.Alpha(ink, (int)(225f * arrive)),
                     Hud.FontLabel, centre: true);

            return w;
        }

        /// <summary>How stepped on it is, in words. The same words the kitchen has always used.</summary>
        public static string PurityWord(float purity)
        {
            if (purity >= 0.95f) return "Untouched";
            if (purity >= 0.75f) return "Barely stepped on";
            if (purity >= 0.50f) return "Cut half and half";
            if (purity < Stash.Unsellable) return "Nobody will buy this";
            return "Stepped on hard";
        }

        /// <summary>What colour that word is: the danger red under the floor, otherwise dim.</summary>
        public static Color PurityInk(float purity)
        {
            return purity < Stash.Unsellable ? Palette.Danger : Palette.TextDim;
        }

        // ======================================================================
        // Motion
        // ======================================================================

        /// <summary>
        /// Chevrons that run one way: a transfer crossing the gap, a batch going through the
        /// press. Three glyphs, each lit a little after the last, so the eye reads a
        /// direction rather than three marks. Text, so it costs no rectangles at all.
        ///
        /// Strength is nought to one and fades the lot; dir is plus for rightward.
        /// </summary>
        public static void Flow(float x, float y, float w, int dir, float strength, Color ink,
                                float scale = 0.30f)
        {
            if (strength <= 0.01f || w <= 0f) return;

            const int Count = 3;
            const int LapMs = 620;

            var glyph = dir >= 0 ? ">" : "<";
            var glyphW = Hud.MeasureText(glyph, scale, Hud.FontBody);

            var span = glyphW * Count * 1.05f;
            var start = x + (w - span) * 0.5f;

            var t = (Game.GameTime % LapMs) / (float)LapMs;

            for (var i = 0; i < Count; i++)
            {
                // Each one peaks a third of a lap after the one before it, in the direction
                // of travel, so the light runs along the row.
                var order = dir >= 0 ? i : Count - 1 - i;
                var phase = t - order / (float)Count;
                phase -= (float)Math.Floor(phase);

                var lit = 0.35f + 0.65f * (float)Math.Pow(1f - phase, 2.2);

                Hud.Text(glyph, start + i * glyphW * 1.05f, y, scale,
                         Palette.Alpha(ink, (int)(ink.A * lit * strength)), Hud.FontBody,
                         centre: false);
            }
        }

        // ======================================================================
        // A prompt in the world
        // ======================================================================

        /// <summary>Where the bar sits, how it is spaced, and how long it takes to come up.</summary>
        private const float PromptY = 0.905f;
        private const float PromptScale = 0.27f;
        private const float PromptPad = 0.014f;
        private const float PromptIcon = 0.011f;
        public const int PromptFadeMs = 160;

        /// <summary>The bar's own ground. Darker and thinner than a panel -- it is a caption.</summary>
        private static readonly Color PromptBack = Color.FromArgb(185, 8, 9, 11);

        /// <summary>
        /// One line at the bottom of the screen: a picture, what you are stood at, then the
        /// key and what it does. For standing next to something -- a car, a bay -- in place
        /// of the game's own yellow box, which was the one piece of stock chrome left on a
        /// mod that draws everything else itself. The dog's bar, made shared.
        ///
        /// Fade is nought to one. Costs one rounded rectangle.
        /// </summary>
        public static void Prompt(string icon, string who, string what, float fade)
        {
            if (fade <= 0f) return;
            if (fade > 1f) fade = 1f;

            var iconW = string.IsNullOrEmpty(icon) ? 0f : Hud.ToX(PromptIcon) + 0.004f;

            var width = iconW + Hud.MeasureText(who, PromptScale, Hud.FontLabel) + 0.016f
                      + Hud.MeasureText(what, PromptScale, Hud.FontLabel);

            var left = 0.5f - width * 0.5f;

            Hud.RoundRect(left - PromptPad, PromptY - 0.007f, width + PromptPad * 2f, 0.027f, 0.0135f,
                          Palette.Alpha(PromptBack, (int)(PromptBack.A * fade)), steps: 10);

            var x = Hud.Hint(icon, who, left, PromptY, PromptScale,
                             Palette.Alpha(Palette.Text, (int)(Palette.Text.A * fade)));

            Hud.Text(what, x, PromptY, PromptScale,
                     Palette.Alpha(Palette.TextDim, (int)(Palette.TextDim.A * fade)), Hud.FontLabel, centre: false);
        }

        /// <summary>
        /// How far up the prompt is, nought to one, from when it first showed. Pass the
        /// caller's own stamp; it is set on the first call and the caller clears it to
        /// nought when the prompt goes away, so the next one fades in again.
        /// </summary>
        public static float PromptFade(ref int since, int now)
        {
            if (since == 0) since = now;

            var age = now - since;
            return age >= PromptFadeMs ? 1f : age / (float)PromptFadeMs;
        }

        /// <summary>
        /// How far into a short flash we are, one at the moment and nought when it is over.
        /// </summary>
        public static float Flash(int at, int lengthMs)
        {
            if (at <= 0) return 0f;

            var since = Game.GameTime - at;
            if (since < 0 || since >= lengthMs) return 0f;

            return 1f - since / (float)lengthMs;
        }

        /// <summary>
        /// A tile has landed this far, nought to one, for the staggered entrance every strip
        /// of tiles makes: a frame or two apart, eased out so each settles rather than stops.
        /// </summary>
        public static float Landed(int age, int lead, int over)
        {
            var land = age <= lead ? 0f : (age - lead) / (float)over;
            if (land > 1f) land = 1f;

            return 1f - (1f - land) * (1f - land);
        }
    }
}
