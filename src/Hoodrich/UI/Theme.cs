using System;
using System.Drawing;
using GTA;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.UI
{
    /// <summary>
    /// The look every panel in the mod shares, from the pocket screen where it started.
    ///
    /// THE PANEL IS THE ROUNDED BLACK AND NOTHING ROUND IT. The screens used to wear a hairline
    /// frame with corner ticks, a stripe along the top edge, a band of light crossing that
    /// stripe and, on one of them, a second light walking the whole perimeter -- four things
    /// happening at the border of a screen whose content is the point. None of that is drawn
    /// any more, anywhere.
    ///
    /// THE COLOUR LIVES INSIDE. Gold and ember (see Palette): an ember wash fading down from
    /// under the wordmark, a short ember stroke on every rule, gold on the marks beside the
    /// headings, and a gold-to-ember plate under whatever is chosen.
    ///
    /// SELECTION IS MOTION, NOT A SWAP. The plate under the newly chosen thing comes up over
    /// PickMs while the plate under the thing the cursor left goes down; the ink on both runs
    /// through every shade between light and dark on the way; the caption slides in from the
    /// left; and a frame with a breathing glow -- see Glide -- travels from the old place to
    /// the new one. Every screen gets all of that from here, which is why the screens agree
    /// with each other.
    ///
    /// EVERYTHING HERE IS STATELESS except Glide, which is a class because a cursor has a
    /// position, and each screen owns one.
    /// </summary>
    internal static class Theme
    {
        /// <summary>The ground of every panel.</summary>
        public static readonly Color Body = Color.FromArgb(255, 12, 13, 15);

        /// <summary>Ink on a lit plate: near-black, the way the vanilla menus punch text out of a highlight.</summary>
        public static readonly Color Dark = Color.FromArgb(255, 10, 20, 13);

        /// <summary>The grey rule every panel has always drawn under its headings.</summary>
        public static readonly Color Hairline = Color.FromArgb(44, 200, 205, 200);

        /// <summary>How long the plate takes to come up under a newly chosen thing.</summary>
        public const int PickMs = 150;

        /// <summary>How far down the panel the wash under the wordmark reaches.</summary>
        public const float WashDepth = 0.062f;

        /// <summary>One lap of the light that crosses a lit plate.</summary>
        public const int SweepMs = 2400;

        // ======================================================================
        // The panel
        // ======================================================================

        /// <summary>
        /// The panel: rounded black, no stripe, no frame, an ember wash under its top.
        /// Arrive is the screen's own entrance, nought to one; pass one for a screen without.
        /// </summary>
        public static void Panel(float left, float top, float w, float h, float arrive = 1f)
        {
            // An accent with no alpha is Hud.Panel's way of being told there is no stripe.
            Hud.Panel(left, top, w, h,
                      Color.FromArgb((int)(238f * arrive), Body.R, Body.G, Body.B),
                      Color.FromArgb(0, 0, 0, 0));

            Wash(left, top, w, WashDepth, (int)(30f * arrive));
        }

        /// <summary>
        /// A warm wash fading downward from the top of a panel: a few bands, each fainter than
        /// the last. The panel's corners are round, so the bands level with the corners are
        /// pulled in by the corner radius rather than pushing colour past the curve.
        /// </summary>
        public static void Wash(float left, float top, float width, float height, int alpha)
        {
            if (alpha <= 0) return;

            const int bands = 7;
            var bandH = height / bands;

            var inset = Hud.ToX(Hud.PanelRound);

            for (var i = 0; i < bands; i++)
            {
                var a = (int)(alpha * (1f - i / (float)bands));
                if (a <= 0) continue;

                var bandTop = top + i * bandH;
                var inCorner = bandTop < top + Hud.PanelRound;

                Hud.RectFrom(left + (inCorner ? inset : 0f), bandTop,
                             width - (inCorner ? inset * 2f : 0f), bandH,
                             Palette.Alpha(Palette.BrandDeep, a));
            }
        }

        /// <summary>
        /// A rule with some heat in it: the grey hairline, with a short ember stroke at its left
        /// end like the tab on a folder. Under every heading, so the sections of every screen
        /// share one mark rather than each having its own idea.
        /// </summary>
        public static void Rule(float x, float y, float width, float arrive = 1f)
        {
            Hud.RectFrom(x, y, width, 0.0012f, Palette.Alpha(Hairline, (int)(Hairline.A * arrive)));

            Hud.RectFrom(x, y - 0.0004f, width * 0.14f, 0.0020f,
                         Palette.Alpha(Palette.BrandDeep, (int)(215f * arrive)));
        }

        /// <summary>
        /// A small in-world card -- the job readout, the war banner, the spray card -- as the
        /// same rounded black the panels are, with a rail down its left in whatever colour the
        /// card means by. No stripe along the top and no corner ticks: those were the border,
        /// and the border is gone everywhere.
        ///
        /// The rail is inset by the corner radius top and bottom, or it pokes out past the
        /// curve at both ends and the corners stop being corners.
        /// </summary>
        public const float CardRound = 0.010f;

        public static void Card(float left, float top, float w, float h, Color back, Color rail, float railW)
        {
            Hud.RoundRect(left, top, w, h, CardRound, back, sprite: false, steps: 12);

            if (rail.A <= 0 || railW <= 0f) return;

            var r = Math.Min(CardRound, h * 0.5f);

            Hud.RectFrom(left, top + r, railW, h - r * 2f, rail);
        }

        // ======================================================================
        // What is chosen
        // ======================================================================

        /// <summary>
        /// How far the newly chosen thing has come up: nought the moment it was picked, one
        /// after PickMs, eased so it settles rather than stops.
        /// </summary>
        public static float Grown(int pickedAt)
        {
            var held = Game.GameTime - pickedAt;
            var grown = held >= PickMs ? 1f : held / (float)PickMs;

            return 1f - (1f - grown) * (1f - grown);
        }

        /// <summary>
        /// How lit thing number i is, nought to one: coming up under the cursor, going down
        /// on the thing the cursor just left, dark everywhere else.
        /// </summary>
        public static float Lit(int i, int selected, int lastSelected, float grown)
        {
            if (i == selected) return grown;
            if (i == lastSelected) return 1f - grown;
            return 0f;
        }

        /// <summary>
        /// The lit plate under a chosen thing: gold at the top running to ember at the bottom,
        /// in bands. Strength is how lit it is, nought to one, which is what lets a plate come
        /// up as the cursor arrives and go down as it leaves instead of snapping either way.
        /// </summary>
        public static void Plate(float x, float y, float w, float h, float strength)
        {
            if (strength <= 0.01f || w <= 0f || h <= 0f) return;

            // Fewer bands on a thin row, or the bands are thinner than a pixel and the
            // rectangle count is spent on nothing anybody can see.
            var bands = h > 0.04f ? 6 : 4;
            var bandH = h / bands;

            for (var i = 0; i < bands; i++)
            {
                var c = Lerp(Palette.Brand, Palette.BrandDeep, (i + 0.5f) / bands);

                Hud.RectFrom(x, y + i * bandH, w, bandH, Palette.Alpha(c, (int)(235f * strength)));
            }
        }

        /// <summary>
        /// A band of light crossing a lit plate. Clipped to the plate rather than drawn over
        /// it, or it is a stripe on the panel that happens to pass a plate on its way.
        /// </summary>
        public static void Sheen(float x, float y, float w, float h, float strength)
        {
            if (strength <= 0.01f) return;

            var sweep = (Game.GameTime % SweepMs) / (float)SweepMs;

            var bandW = w * 0.34f;
            if (w > 0.2f) bandW = w * 0.16f;

            var bandAt = x - bandW + (w + bandW) * sweep;

            var lo = Math.Max(x, bandAt);
            var hi = Math.Min(x + w, bandAt + bandW);

            if (hi > lo)
            {
                Hud.RectFrom(lo, y, hi - lo, h, Color.FromArgb((int)(46f * strength), 255, 255, 255));
            }
        }

        /// <summary>
        /// What ink to use on a thing that is this lit: its ordinary colour on the dark, the
        /// near-black on a plate, and every shade between while the plate is on its way. The
        /// ordinary colour's alpha is kept.
        /// </summary>
        public static Color Ink(Color ordinary, float lit)
        {
            if (lit <= 0f) return ordinary;

            return Lerp(ordinary, Color.FromArgb(ordinary.A, Dark.R, Dark.G, Dark.B), lit);
        }

        /// <summary>
        /// The name of the chosen thing, sliding in from the left and brightening as its plate
        /// comes up, so the word arrives with the plate rather than swapping under it.
        /// </summary>
        public static void Caption(string words, float x, float y, float grown, float scale = 0.28f)
        {
            Hud.Text(words, x + Hud.ToX(0.010f) * (1f - grown), y, scale,
                     Palette.Alpha(Palette.Text, (int)(90f + 165f * grown)), Hud.FontBody, centre: false);
        }

        // ======================================================================
        // Odds and ends
        // ======================================================================

        public static Color Lerp(Color a, Color b, float t)
        {
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;

            return Color.FromArgb((int)(a.A + (b.A - a.A) * t),
                                  (int)(a.R + (b.R - a.R) * t),
                                  (int)(a.G + (b.G - a.G) * t),
                                  (int)(a.B + (b.B - a.B) * t));
        }

        /// <summary>Four strokes round a box. Left and right are the rule turned into an x-width.</summary>
        public static void Rim(float x, float y, float w, float h, float rule, Color c)
        {
            var rX = Hud.ToX(rule);

            Hud.RectFrom(x, y, w, rule, c);
            Hud.RectFrom(x, y + h - rule, w, rule, c);
            Hud.RectFrom(x, y + rule, rX, h - rule * 2f, c);
            Hud.RectFrom(x + w - rX, y + rule, rX, h - rule * 2f, c);
        }

        /// <summary>The rim of the cursor frame: a brighter gold than the plate's top, so it reads on it.</summary>
        public static readonly Color RimInk = Lerp(Palette.Brand, Color.White, 0.45f);
    }

    /// <summary>
    /// The cursor: a frame that GLIDES from the thing it was on to the thing it is going to,
    /// rather than appearing there. One per screen, because it has a position.
    ///
    /// Each frame the screen calls Begin, the thing under the cursor calls Target with its
    /// box, and the screen calls Draw last so the frame rides over everything else. Nothing
    /// calling Target means no cursor -- an empty list, say -- and the next Target after that
    /// puts the frame straight onto the box rather than gliding in from wherever it was left.
    /// </summary>
    internal sealed class Glide
    {
        /// <summary>How thick the rim is, how far the glow reaches past it, one breath of that glow.</summary>
        public const float Rule = 0.0022f;
        public const float Glow = 0.0030f;
        public const int PulseMs = 1500;

        /// <summary>What share of the remaining distance it closes each sixtieth of a second.</summary>
        public const float Chase = 0.26f;

        /// <summary>The rim and the glow. Gold and ember by default; the phone sets its own green.</summary>
        public Color RimInk = Theme.RimInk;
        public Color GlowInk = Palette.BrandDeep;

        private float _x, _y, _w, _h;
        private bool _on;

        private bool _target;
        private float _tx, _ty, _tw, _th;

        /// <summary>Nothing wants the cursor until something asks for it this frame.</summary>
        public void Begin()
        {
            _target = false;
        }

        /// <summary>The thing under the cursor, saying where the frame belongs this frame.</summary>
        public void Target(float x, float y, float w, float h)
        {
            _target = true;
            _tx = x;
            _ty = y;
            _tw = w;
            _th = h;
        }

        /// <summary>Forget where it was, so the next Draw lands rather than glides.</summary>
        public void Reset()
        {
            _on = false;
            _target = false;
        }

        /// <summary>Where the frame is right now, for anything that wants to follow it.</summary>
        public float X => _x;
        public float Y => _y;
        public float W => _w;
        public float H => _h;

        /// <summary>Draws the frame where it IS, which for a sixth of a second after every press is not where the cursor is -- that is the point.</summary>
        public void Draw(float arrive = 1f)
        {
            if (!_target)
            {
                _on = false;
                return;
            }

            if (!_on)
            {
                // Straight onto the first thing. Gliding in from wherever it was left last time
                // would be a frame arriving from somewhere off the screen.
                _x = _tx;
                _y = _ty;
                _w = _tw;
                _h = _th;
                _on = true;
            }
            else
            {
                // The same share of what is left every sixtieth of a second whatever the frame
                // time is, so it lands in the same time at thirty frames and at a hundred and
                // forty.
                var dt = Game.LastFrameTime;
                if (dt <= 0f || dt > 0.25f) dt = 1f / 60f;

                var k = 1f - (float)Math.Pow(1f - Chase, dt * 60f);

                _x += (_tx - _x) * k;
                _y += (_ty - _y) * k;
                _w += (_tw - _w) * k;
                _h += (_th - _h) * k;

                if (Math.Abs(_tx - _x) < 0.0002f) _x = _tx;
                if (Math.Abs(_ty - _y) < 0.0002f) _y = _ty;
                if (Math.Abs(_tw - _w) < 0.0002f) _w = _tw;
                if (Math.Abs(_th - _h) < 0.0002f) _h = _th;
            }

            // It breathes: a slow rise and fall in the glow and a little in the rim, so a
            // cursor left alone still reads as the live thing on the screen.
            var pulse = 0.5f + 0.5f * (float)Math.Sin(Game.GameTime / (double)PulseMs * Math.PI * 2.0);

            var gX = Hud.ToX(Glow);

            Theme.Rim(_x - gX, _y - Glow, _w + gX * 2f, _h + Glow * 2f, Glow,
                     Palette.Alpha(GlowInk, (int)((30f + 40f * pulse) * arrive)));

            Theme.Rim(_x, _y, _w, _h, Rule,
                     Palette.Alpha(RimInk, (int)((205f + 50f * pulse) * arrive)));
        }
    }
}
