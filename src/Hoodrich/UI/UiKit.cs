using System;
using System.Drawing;
using GTA;
using GTA.Native;
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

        /// <summary>
        /// It waits before it says anything, says it for a while, and goes. Somebody who
        /// knows the key never sees it; somebody stood there wondering does.
        /// </summary>
        public const int PromptAfterMs = 6000;
        public const int PromptForMs = 4000;
        public const int PromptFadeMs = 300;

        /// <summary>Where its FOOT sits: above Bare Minimum's own line, which owns the very bottom edge.</summary>
        private const float PromptFoot = 0.910f;

        /// <summary>How far it climbs on the way in and sinks on the way out.</summary>
        private const float PromptClimb = 0.012f;

        private const float PromptPad = 0.009f;
        private const float PromptScale = 0.27f;
        private const float CapPadX = 0.006f;
        private const float CapScale = 0.24f;
        private const float CapGap = 0.008f;

        /// <summary>Everything is drawn through this. It is an offer, not a result; it is allowed to be quiet.</summary>
        private const float PromptQuiet = 0.70f;

        /// <summary>
        /// One line at the bottom of the screen: the key on a small amber cap, then what it
        /// does in plain words -- "Pop the boot", "Pull in to Hao's muffler shop". For
        /// standing next to something, in place of the game's own yellow box.
        ///
        /// THE SAME LINE BARE MINIMUM DRAWS for a bed or a car seat, on purpose: the two
        /// mods share a key in a stopped car, so their prompts end up on the screen together,
        /// and two different-looking bars for one button read as a mistake. This one sits
        /// directly above theirs.
        ///
        /// Show is nought to one. Costs one panel and one small rounded chip.
        /// </summary>
        public static void Prompt(string cap, string text, float show)
        {
            if (show <= 0.01f || string.IsNullOrEmpty(text)) return;
            if (show > 1f) show = 1f;

            var textW = Hud.MeasureText(text, PromptScale, Hud.FontBody);
            var textH = TextHeight(PromptScale, Hud.FontBody);

            // The cap is as wide as the letters on it plus a margin either side, so E and
            // D-PAD RIGHT both sit in one that fits them.
            var hasCap = !string.IsNullOrEmpty(cap);
            var capText = hasCap ? Hud.MeasureText(cap, CapScale, Hud.FontLabel) : 0f;
            var capW = hasCap ? capText + Hud.ToX(CapPadX) * 2f : 0f;
            var capLead = hasCap ? capW + Hud.ToX(CapGap) : 0f;

            var padX = Hud.ToX(PromptPad);
            var w = padX + capLead + textW + padX;
            var h = PromptPad * 0.85f + textH + PromptPad * 0.85f;

            var x = 0.5f - w * 0.5f;

            // Up on the way in and back down on the way out, so it belongs to the bottom
            // edge rather than appearing in the middle of nothing.
            var top = PromptFoot - h + PromptClimb * (1f - show);
            var ink = show * PromptQuiet;

            Theme.Panel(x, top, w, h, ink);

            if (hasCap)
            {
                var capH = TextHeight(CapScale, Hud.FontLabel) + 0.004f;
                var capY = top + PromptPad * 0.85f + (textH - capH) * 0.5f - 0.001f;

                Hud.RoundRect(x + padX, capY, capW, capH, 0.0022f,
                              Palette.Alpha(Palette.Warn, (int)(58f * ink)), steps: 8);

                Hud.Text(cap, x + padX + (capW - capText) * 0.5f, capY + 0.002f, CapScale,
                         Palette.Alpha(Palette.Warn, (int)(255f * ink)), Hud.FontLabel, centre: false);
            }

            Hud.Text(text, x + padX + capLead, top + PromptPad * 0.85f, PromptScale,
                     Palette.Alpha(Palette.Text, (int)(232f * ink)), Hud.FontBody, centre: false);
        }

        /// <summary>How tall a line of this comes out, from the game; a guess if it will not say.</summary>
        private static float TextHeight(float scale, int font)
        {
            try { return Function.Call<float>(Hash.GET_RENDERED_CHARACTER_HEIGHT, scale, font); }
            catch { return scale * 0.1f; }
        }

        /// <summary>
        /// How far up the prompt is, nought to one, along its timeline: nothing for the first
        /// six seconds, a fade in, four seconds up, a fade out, then nothing again until the
        /// caller clears the stamp by going away and coming back.
        ///
        /// The stamp is the caller's own, set on the first call and cleared to nought by the
        /// caller when the thing is no longer in front of him.
        /// </summary>
        public static float PromptFade(ref int since, int now)
        {
            if (since == 0) since = now;

            var age = now - since - PromptAfterMs;
            if (age < 0) return 0f;

            if (age < PromptFadeMs)
            {
                var t = age / (float)PromptFadeMs;
                return 1f - (1f - t) * (1f - t);
            }

            age -= PromptFadeMs;
            if (age < PromptForMs) return 1f;

            age -= PromptForMs;
            if (age >= PromptFadeMs) return 0f;

            var u = 1f - age / (float)PromptFadeMs;
            return u * u;
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
