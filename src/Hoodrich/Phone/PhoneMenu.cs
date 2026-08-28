using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.UI;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.Phone
{
    /// <summary>
    /// Hoodrich's phone: the whole mod, as a handset.
    ///
    /// This replaces the radial wheel, and the reason is the button it was costing. The wheel
    /// hung off the weapon-wheel control, so every time you wanted a gun you got a business
    /// menu -- a mod about dealing was charging you the one control a GTA player uses most.
    /// The phone button costs nothing: the vanilla phone is a contacts list and an email client
    /// for missions that are already over by the time this mod is interesting.
    ///
    /// What it deliberately does NOT do is rewrite the menus. Everything on here is still built
    /// by WheelPages, still a WheelPage of WheelItems, and every item still carries a label, an
    /// icon, a detail line, a right-hand value, an enabled flag and either an action or a
    /// submenu. That was always a list; it was only ever DRAWN as a ring. So the content layer
    /// is untouched and this is a second presentation of it -- which is why a change of this
    /// size does not put a single business rule at risk.
    ///
    /// Two modes:
    ///   HOME  -- the root page, as a grid of app tiles
    ///   LIST  -- everything below it, as a scrolling text menu
    /// </summary>
    internal sealed class PhoneMenu
    {
        // ---- the handset --------------------------------------------------------

        /// <summary>Height of the whole device, as a fraction of screen height.</summary>
        private const float BodyH = 0.760f;

        /// <summary>Width over height. A tall modern handset, not a 2013 one.</summary>
        private const float BodyRatio = 0.472f;

        /// <summary>Where its right edge sits. The vanilla phone lives on this side.</summary>
        private const float BodyRight = 0.972f;

        private const float BodyTop = 0.118f;

        /// <summary>Bezel between the body edge and the screen.</summary>
        private const float Bezel = 0.009f;

        private const float StatusH = 0.030f;
        private const float HeaderH = 0.058f;
        private const float FooterH = 0.038f;

        // ---- the grid -----------------------------------------------------------

        private const int Columns = 3;
        private const float TilePad = 0.011f;
        private const float TileH = 0.112f;

        // ---- the list -----------------------------------------------------------

        private const float RowH = 0.054f;

        /// <summary>How long the open animation runs.</summary>
        private const int RiseMs = 190;

        /// <summary>And how far it rises through, in screen heights.</summary>
        private const float RiseBy = 0.055f;

        /// <summary>Apps land one after another rather than all at once.</summary>
        private const int TileStaggerMs = 26;

        /// <summary>
        /// How long a page takes to slide in when you drill in or back out.
        ///
        /// This replaced a press-flash on the picked row, which could never work: pressing a
        /// submenu swaps the page on the same frame, so the flash landed on whatever row
        /// happened to sit at that index on the NEW page, and pressing a leaf closes the phone
        /// so it was never on screen at all. The thing that acknowledges a press is the page
        /// arriving, so that is what is animated.
        /// </summary>
        private const int PageMs = 155;

        /// <summary>How far a page slides in from, in X.</summary>
        private const float PageSlide = 0.055f;

        /// <summary>One full pass of the sheen across a selected row.</summary>
        private const int SheenMs = 1500;

        /// <summary>How wide the sheen is, as a fraction of the row.</summary>
        private const float SheenWide = 0.22f;

        /// <summary>How round the app tiles are, in screen heights.</summary>
        private const float TileRound = 0.012f;

        /// <summary>How round the handset and its screen are.</summary>
        private const float BodyRound = 0.020f;
        private const float ScreenRound = 0.013f;

        /// <summary>How long a tile takes to pop when the cursor lands on it.</summary>
        private const int PopMs = 150;

        /// <summary>
        /// The sway on the icon of whatever you are hovering.
        ///
        /// FROM _movedAt, so it starts upright every time the cursor lands rather than being
        /// caught mid-swing on a clock that never stopped. A tile you have just arrived at
        /// should begin at rest and set off, not be found already leaning.
        ///
        /// It arrives with a kick and settles to a steady sway -- the same shape as the can's
        /// shake and for the same reason: something that starts at its resting amplitude reads
        /// as an idle loop, and something that overshoots and settles reads as having been
        /// nudged by you.
        /// </summary>
        private const double SwayMs = 1150.0;
        private const float SwayDegrees = 5f;
        private const int SwayKickMs = 420;

        /// <summary>
        /// And how far it grows.
        ///
        /// Deliberately smaller than half the gap between tiles, RIM INCLUDED, so a grown tile
        /// and its neighbour can never touch. The first pass overshot past that and the live
        /// app collided with the one next to it.
        /// </summary>
        private const float PopBy = 0.0030f;

        /// <summary>
        /// The phone's own green.
        ///
        /// Deliberately NOT Palette.Cash. Money green already means money on every other screen
        /// in this mod, and a handset lit in it would be saying "money" about the clock, the
        /// signal and the wordmark. This is the SET's green -- whose phone it is -- and it is
        /// spent on chrome only: the mark, the bars, the rules and the cursor. Never on a
        /// number, because that is the one place green already means something else.
        /// </summary>
        private static readonly Color Green = Color.FromArgb(255, 108, 196, 106);
        private static readonly Color GreenDim = Color.FromArgb(150, 66, 124, 68);

        /// <summary>One full breath of the wordmark.</summary>
        private const int PulseMs = 2600;

        /// <summary>
        /// The fill under whatever is selected, and the ink on top of it.
        ///
        /// A near-white bar with near-black text on it, which is what this used to be, is a
        /// PANEL's idea of a highlight -- and it is the one thing on a black handset that
        /// cannot survive its background going missing. When the fill dropped, the item did
        /// not fall back to looking unselected; it went invisible, because its text had been
        /// coloured for a surface that was not there.
        ///
        /// Lit instead of inverted. A dark green bed with the set's own green on it, and the
        /// text stays WHITE either way -- so the worst this can ever look now is an item that
        /// is merely not highlighted, which is a thing you can still read.
        /// </summary>
        private static readonly Color Lit = Color.FromArgb(236, 20, 46, 26);
        private static readonly Color LitEdge = Color.FromArgb(255, 108, 196, 106);

        // ---- state --------------------------------------------------------------

        /// <summary>One level of the menu, and where the player was on it.</summary>
        private sealed class Level
        {
            public WheelPage Page;
            public int Index;
            public int Scroll;
        }

        private readonly Settings _cfg;

        private readonly List<Level> _stack = new List<Level>();

        public PhoneMenu(Settings cfg)
        {
            _cfg = cfg;
        }

        /// <summary>Every click goes through here, so one setting can silence the lot.</summary>
        private void Beep(string sound)
        {
            if (_cfg != null && !_cfg.PlaySounds) return;
            Hud.PlaySound(sound, "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        private int _openedAt;

        /// <summary>When the page last changed, and which way, so it can slide in.</summary>
        private int _pageAt;
        private int _pageDir;

        /// <summary>When the cursor last moved, so the tile under it can pop.</summary>
        private int _movedAt;
        private int _lastIndex = -1;

        public bool IsOpen { get; private set; }

        /// <summary>True while the home grid is showing rather than a list.</summary>
        public bool AtHome => _stack.Count == 1;

        private WheelPage Current => _stack.Count == 0 ? null : _stack[_stack.Count - 1].Page;

        private Level Top => _stack.Count == 0 ? null : _stack[_stack.Count - 1];

        /// <summary>The item under the cursor, or null.</summary>
        private WheelItem Selected
        {
            get
            {
                var lvl = Top;
                if (lvl == null || lvl.Page.Items.Count == 0) return null;
                if (lvl.Index < 0 || lvl.Index >= lvl.Page.Items.Count) return null;
                return lvl.Page.Items[lvl.Index];
            }
        }

        // ---- opening and closing ------------------------------------------------

        public void Open(WheelPage root)
        {
            if (root == null) return;

            _stack.Clear();
            _stack.Add(new Level { Page = root, Index = FirstPickable(root) });

            IsOpen = true;
            _openedAt = Game.GameTime;
            _pageAt = Game.GameTime;
            _pageDir = 0;

            Beep("SELECT");
        }

        public void Close()
        {
            if (!IsOpen) return;

            IsOpen = false;
            _stack.Clear();
        }

        private static int FirstPickable(WheelPage page)
        {
            for (var i = 0; i < page.Items.Count; i++)
            {
                if (page.Items[i].Enabled) return i;
            }
            return 0;
        }

        // ---- navigation ---------------------------------------------------------

        private void MoveBy(int by)
        {
            var lvl = Top;
            if (lvl == null || lvl.Page.Items.Count == 0) return;

            var n = lvl.Page.Items.Count;
            var i = lvl.Index;

            // Steps over disabled rows rather than landing on them, but gives up after a full
            // lap so a page where everything is locked cannot spin forever.
            for (var tried = 0; tried < n; tried++)
            {
                i = ((i + by) % n + n) % n;
                if (lvl.Page.Items[i].Enabled) break;
            }

            if (i == lvl.Index) return;

            lvl.Index = i;
            Beep("NAV_UP_DOWN");
        }

        /// <summary>Left and right. One app along on the home grid; nothing in a list.</summary>
        public void MoveColumn(int by)
        {
            if (!AtHome) return;
            MoveBy(by);
        }

        /// <summary>
        /// Up and down. One line in a list, one ROW of apps on the home grid.
        ///
        /// The grid clamps where a list wraps, and that is the difference between the two.
        /// Stepping a whole row with the same wrapping arithmetic a list uses lands you in a
        /// different column every time it runs off the end -- pressing down on the third app
        /// of a five-app grid took you to the first, which is neither where you were pointing
        /// nor anywhere a phone would have put you.
        /// </summary>
        public void MoveRow(int by)
        {
            var lvl = Top;
            if (lvl == null || lvl.Page.Items.Count == 0) return;

            if (!AtHome)
            {
                MoveBy(by);
                return;
            }

            var n = lvl.Page.Items.Count;
            var target = lvl.Index + by * Columns;

            if (target < 0 || target >= n)
            {
                // Off the end of the grid. Going down from a part-filled last row lands on the
                // final app rather than nowhere; going up from the top row stays put.
                if (by <= 0) return;
                if (lvl.Index == n - 1) return;
                target = n - 1;
            }

            Land(target, by);
        }

        /// <summary>
        /// Moves to an index, stepping past anything locked in the direction of travel.
        ///
        /// Clamped rather than wrapped, so a grid move that ends on a disabled app walks
        /// onward and gives up at the edge instead of appearing on the other side of the
        /// screen.
        /// </summary>
        private void Land(int target, int dir)
        {
            var lvl = Top;
            var n = lvl.Page.Items.Count;

            var step = dir >= 0 ? 1 : -1;

            while (target >= 0 && target < n && !lvl.Page.Items[target].Enabled)
            {
                target += step;
            }

            if (target < 0 || target >= n || target == lvl.Index) return;

            lvl.Index = target;
            Beep("NAV_UP_DOWN");
        }

        /// <summary>Drills in. Returns the item to ACT on, or null if it only opened a page.</summary>
        public WheelItem Pick()
        {
            var item = Selected;
            if (item == null) return null;

            if (!item.Enabled)
            {
                Beep("ERROR");
                return null;
            }



            if (item.IsSubmenu)
            {
                WheelPage next;
                try
                {
                    next = item.Submenu();
                }
                catch (Exception ex)
                {
                    Log.Error("A phone page failed to build.", ex);
                    Beep("ERROR");
                    return null;
                }

                if (next == null || next.Items.Count == 0)
                {
                    Beep("ERROR");
                    return null;
                }

                _stack.Add(new Level { Page = next, Index = FirstPickable(next) });
                Turn(1);
                Beep("SELECT");
                return null;
            }

            Beep("SELECT");
            return item;
        }

        /// <summary>Back one level. Returns false when there is nowhere left to go.</summary>
        public bool Back()
        {
            if (_stack.Count <= 1) return false;

            _stack.RemoveAt(_stack.Count - 1);
            Turn(-1);
            Beep("BACK");
            return true;
        }

        /// <summary>Straight to the home screen, the way a home button works.</summary>
        public void Home()
        {
            if (_stack.Count <= 1) return;

            while (_stack.Count > 1) _stack.RemoveAt(_stack.Count - 1);
            Turn(-1);
            Beep("BACK");
        }

        // ---- drawing ------------------------------------------------------------

        public void Render()
        {
            if (!IsOpen || _stack.Count == 0) return;

            var t = Ease();

            var bodyW = Hud.ToX(BodyH * BodyRatio);
            var left = BodyRight - bodyW;
            var top = BodyTop + (1f - t) * RiseBy;

            var fade = (int)(255 * t);

            Body(left, top, bodyW, BodyH, fade);

            var bezX = Hud.ToX(Bezel);
            var scrLeft = left + bezX;
            var scrTop = top + Bezel;
            var scrW = bodyW - bezX * 2f;
            var scrH = BodyH - Bezel * 2f;

            StatusBar(scrLeft, scrTop, scrW, fade);

            var headTop = scrTop + StatusH;
            Header(scrLeft, headTop, scrW, fade);

            var bodyTop = headTop + HeaderH;
            var bodyHeight = scrH - StatusH - HeaderH - FooterH;

            // The page slides in from whichever way you went. Drilling in comes from the
            // right, backing out comes from the left, so the direction carries the meaning
            // rather than the animation merely being present.
            var landed = Landed();
            var slide = _pageDir == 0 ? 0f : (1f - landed) * Hud.ToX(PageSlide) * _pageDir;
            var pageFade = (int)(fade * (0.35f + 0.65f * landed));

            if (AtHome) Grid(scrLeft + slide, bodyTop, scrW, bodyHeight, pageFade);
            else List(scrLeft + slide, bodyTop, scrW, bodyHeight, pageFade);

            Footer(scrLeft, scrTop + scrH - FooterH, scrW, fade);
        }

        /// <summary>Starts a page transition. +1 drilling in, -1 coming back.</summary>
        private void Turn(int dir)
        {
            _pageAt = Game.GameTime;
            _pageDir = dir;
        }

        /// <summary>0 while a page is still arriving, 1 once it has landed.</summary>
        private float Landed()
        {
            var age = Game.GameTime - _pageAt;
            if (age >= PageMs) return 1f;

            var x = Math.Max(0f, age / (float)PageMs);
            return 1f - (1f - x) * (1f - x) * (1f - x);
        }

        /// <summary>
        /// A travelling highlight, drawn INSIDE the selection fill and nowhere else.
        ///
        /// Clipped to the fill on purpose. A sheen that runs the width of the screen is a
        /// screensaver; one that runs the width of the thing you have selected is that thing
        /// telling you it is the live one.
        /// </summary>
        private static void Sheen(float left, float top, float w, float h, int fade,
                                  float inset = 0f)
        {
            var t = (Game.GameTime % SheenMs) / (float)SheenMs;

            var wide = w * SheenWide;
            var at = left - wide + (w + wide * 2f) * t;

            // Trimmed to the row rather than allowed to hang off either end.
            var a = Math.Max(left, at);
            var b = Math.Min(left + w, at + wide);
            if (b <= a) return;

            // KEPT OUT OF THE CORNERS, and that is what the inset is for.
            //
            // The band is a square-cornered rectangle laid over a rounded one. On a row the two
            // agree; on a tile they do not, and every pass of the shimmer painted the four
            // corner patches the rounded shape had deliberately left empty -- so the live app
            // grew square ears twice a second.
            //
            // Inset by the corner radius, the band only ever crosses the straight middle of the
            // shape, which is where a shimmer reads anyway.
            var top2 = top + inset;
            var high = h - inset * 2f;
            if (high <= 0f) return;

            // Two passes rather than one: a wide soft wash with a brighter core inside it, so
            // it travels as a highlight with an edge rather than as a grey block sliding past.
            Hud.RectFrom(a, top2, b - a, high, Fade(Color.FromArgb(26, 255, 255, 255), fade));

            var coreWide = (b - a) * 0.34f;
            var coreAt = a + (b - a) * 0.5f - coreWide * 0.5f;

            if (coreWide > 0f)
            {
                Hud.RectFrom(coreAt, top2, coreWide, high,
                             Fade(Color.FromArgb(34, 255, 255, 255), fade));
            }
        }

        /// <summary>Eased-out rise, the same curve every other panel in the mod opens on.</summary>
        private float Ease()
        {
            var age = Game.GameTime - _openedAt;
            if (age >= RiseMs) return 1f;

            var x = Math.Max(0f, Math.Min(1f, age / (float)RiseMs));
            return 1f - (1f - x) * (1f - x) * (1f - x);
        }

        private static Color Fade(Color c, int fade)
        {
            return Color.FromArgb(c.A * fade / 255, c.R, c.G, c.B);
        }

        private void Body(float left, float top, float w, float h, int fade)
        {
            // The handset, rounded.
            //
            // It was an angular slab with corner ticks, on the reasoning that every other
            // panel in this mod is angular. That is true of PANELS, and this is not one -- it
            // is a picture of an object everybody has in their pocket, and the one thing every
            // one of those has in common is that the corners are round. A phone with square
            // corners reads as a menu pretending to be a phone.
            //
            // Drawn as a rim and then a body inside it, so the bezel is a real edge rather
            // than a hairline that disappears at small sizes.
            // RECTS at the corners here, not the sprite, and that is the whole reason this
            // argument exists.
            //
            // A runtime-texture sprite does not sit in the same queue as DRAW_RECT: it comes out
            // ON TOP of any rectangle drawn after it, whatever order they were issued in. The
            // handset is the bottom layer of the screen, so its four corner discs floated up
            // over everything that is drawn as a rectangle afterwards -- the battery, the signal
            // bars, the rule under the status bar -- and read as four grey circles sitting on
            // the phone with the chrome behind them.
            //
            // The tiles keep the sprite because nothing is drawn under them; the body cannot,
            // because everything is.
            // Coarse bands on the OUTER shell, fine ones on the screen.
            //
            // This is the chrome rim, and the body drawn inside it covers all but its outermost
            // two pixels -- so a per-row corner here was two hundred and thirty rectangles
            // spent on an arc that is a two-pixel band by the time anything is on top of it.
            // That is the same waste the tile rim was deleted for, and between them they were
            // what pushed the frame past GTA's rectangle ceiling and started costing other
            // shapes their corners. Twenty steps is a three-pixel stagger on a two-pixel band,
            // which is to say invisible.
            Hud.RoundRect(left, top, w, h, BodyRound,
                      Fade(Color.FromArgb(255, 96, 102, 104), fade), sprite: false, steps: 20);

            var edge = 0.0022f;
            var edgeX = Hud.ToX(edge);

            Hud.RoundRect(left + edgeX, top + edge, w - edgeX * 2f, h - edge * 2f,
                      BodyRound - edge, Fade(Color.FromArgb(252, 8, 9, 10), fade), sprite: false);

            // And the screen it houses, rounded with it.
            var bezX = Hud.ToX(Bezel);
            Hud.RoundRect(left + bezX, top + Bezel, w - bezX * 2f, h - Bezel * 2f,
                      ScreenRound, Fade(Color.FromArgb(252, 13, 15, 17), fade),
                      sprite: false, steps: 0);
        }

        private void StatusBar(float left, float top, float w, int fade)
        {
            var pad = Hud.ToX(0.012f);
            var mid = top + StatusH * 0.5f;

            // The wordmark, breathing.
            //
            // The real mark rather than the words typed out -- logo.png is the same art the
            // welcome screen and the panel headers carry, so the phone WEARS the mod's name
            // instead of spelling it. It lives in the status bar, which means it is on screen
            // on every page rather than only the home one.
            //
            // A slow sine, not a blink. The battery is the only thing on this screen allowed
            // to blink, because blinking means "look at this" and a logo has nothing to report.
            var turn = (Game.GameTime % PulseMs) / (double)PulseMs * Math.PI * 2d;
            var breath = 0.5f + 0.5f * (float)Math.Sin(turn);

            // And it MOVES, a little.
            //
            // The colour breathing on its own is a thing you have to be looking at to notice,
            // and it is the mod's own name sitting on every page of the phone -- it should have
            // a bit of life in it. So it rises and falls by about a pixel, on a quarter turn
            // behind the colour so the two are not obviously the same wave, and it grows by a
            // couple of per cent at the top of the breath.
            //
            // Deliberately small. This is a logo in a status bar, not a notification: if you
            // can see it move without looking for it, it is too much.
            var lift = (float)Math.Sin(turn - Math.PI * 0.5d) * 0.0011f;
            var swell = 1f + 0.03f * breath;

            Hud.Brand(left + pad, mid + lift, 0.0122f * swell,
                      Fade(Lerp(GreenDim, Green, breath), fade));

            // The game's clock, because a phone that says the wrong time is a prop.
            var hh = Function.Call<int>(Hash.GET_CLOCK_HOURS);
            var mm = Function.Call<int>(Hash.GET_CLOCK_MINUTES);

            Hud.Text(hh.ToString("00") + ":" + mm.ToString("00"),
                     left + w * 0.5f, top + 0.006f, 0.26f,
                     Fade(Palette.Text, fade), Hud.FontLabel, centre: true);

            var right = left + w - pad;

            Battery(right, mid, fade);
            Signal(right - Hud.ToX(0.026f), mid, fade);

            Hud.RectFrom(left, top + StatusH - 0.0014f, w, 0.0014f, Fade(GreenDim, fade));
        }

        /// <summary>Blends two colours, for the pulse.</summary>
        private static Color Lerp(Color a, Color b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));

            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        /// <summary>
        /// Reception, as four climbing bars.
        ///
        /// It was three identical blocks typed into a string, which reads as decoration
        /// because nothing about it could ever change. Bars that climb and that empty out are
        /// the shape everybody already knows -- and this set has something true to put in it:
        /// signal falls off the further you get from the block. Stood in Chamberlain Hills you
        /// have all four; out past the airport you are down to one.
        /// </summary>
        private static void Signal(float rightX, float midY, int fade)
        {
            const int bars = 4;
            var lit = bars;

            try
            {
                var player = Game.Player.Character;
                if (player != null && player.Exists())
                {
                    var home = new Vector3(-150f, -1640f, 33f);
                    var away = player.Position.DistanceTo2D(home);

                    lit = away < 700f ? 4
                        : away < 1800f ? 3
                        : away < 3400f ? 2
                        : 1;
                }
            }
            catch
            {
                // Full bars is a fine thing to be wrong about.
            }

            var wide = Hud.ToX(0.0030f);
            var gap = Hud.ToX(0.0016f);
            var bottom = midY + 0.0058f;

            for (var i = 0; i < bars; i++)
            {
                var h = 0.0032f + i * 0.0026f;
                var x = rightX - (bars - i) * (wide + gap);

                Hud.RectFrom(x, bottom - h, wide, h,
                             Fade(i < lit ? Green : Color.FromArgb(70, 96, 108, 96), fade));
            }
        }




        /// <summary>
        /// The battery, and it means something.
        ///
        /// Tied to the game clock rather than to a timer of our own: full first thing, down to
        /// a sliver by the early hours. A phone that reads 100% at four in the morning after a
        /// night of driving around is a picture of a battery; one that does not is a phone.
        ///
        /// It blinks red under a fifth, which is the one place on this screen anything blinks
        /// -- so it means "look at this" rather than being decoration.
        /// </summary>
        private static void Battery(float rightX, float midY, int fade)
        {
            var hour = Function.Call<int>(Hash.GET_CLOCK_HOURS);
            var mins = Function.Call<int>(Hash.GET_CLOCK_MINUTES);

            // Runs from 7am, so the day drains it and sleeping puts it back.
            var since = ((hour - 7 + 24) % 24) + mins / 60f;
            var charge = Math.Max(0.06f, 1f - since / 26f);

            var low = charge < 0.2f;

            var bodyW = Hud.ToX(0.019f);
            var bodyH = 0.011f;
            var left = rightX - bodyW;
            var top = midY - bodyH * 0.5f;

            var ink = low ? Palette.Danger : Palette.TextDim;

            // Blinks about twice a second, and only when it is nearly out.
            if (low)
            {
                var on = (Game.GameTime / 380) % 2 == 0;
                if (!on) ink = Color.FromArgb(70, ink.R, ink.G, ink.B);
            }

            var line = 0.0014f;
            var lineX = Hud.ToX(line);

            // Shell.
            Hud.RectFrom(left, top, bodyW, line, Fade(ink, fade));
            Hud.RectFrom(left, top + bodyH - line, bodyW, line, Fade(ink, fade));
            Hud.RectFrom(left, top, lineX, bodyH, Fade(ink, fade));
            Hud.RectFrom(left + bodyW - lineX, top, lineX, bodyH, Fade(ink, fade));

            // The nub on the end.
            Hud.RectFrom(left + bodyW, midY - 0.0026f, Hud.ToX(0.0028f), 0.0052f, Fade(ink, fade));

            // And what is left in it.
            var inset = Hud.ToX(0.0022f);
            var room = bodyW - inset * 2f;
            Hud.RectFrom(left + inset, top + 0.0026f, room * charge, bodyH - 0.0052f,
                         Fade(ink, fade));
        }

        private void Header(float left, float top, float w, int fade)
        {
            var page = Current;
            if (page == null) return;

            var pad = Hud.ToX(0.013f);

            Hud.RectFrom(left, top, w, HeaderH, Fade(Palette.PanelHeader, fade));

            // The home page does NOT repeat its own name.
            //
            // The wordmark in the status bar already says POSTED UP, in the mod's own type, on
            // every page -- so the home screen was printing it twice, once as art and once as
            // a word, an inch apart. Which set you run with is the fact that actually belongs
            // there, so on home it gets the big line and the title is dropped. Every other
            // page keeps its title, because GANGS or CONTACTS under the mark is a breadcrumb
            // rather than an echo.
            var title = AtHome ? page.Subtitle : page.Title;
            var under = AtHome ? "" : page.Subtitle;

            if (string.IsNullOrEmpty(title)) title = AtHome ? "Unaffiliated" : "Posted Up";

            Hud.Text(Hud.Fit(title.ToUpperInvariant(), w - pad * 2f, 0.44f, Hud.FontLabel),
                     left + pad, top + (string.IsNullOrEmpty(under) ? 0.016f : 0.007f), 0.44f,
                     Fade(Palette.Text, fade), Hud.FontLabel, centre: false);

            if (!string.IsNullOrEmpty(under))
            {
                Hud.Text(Hud.Fit(under, w - pad * 2f, 0.25f, Hud.FontBody),
                         left + pad, top + 0.032f, 0.25f,
                         Fade(Palette.TextDim, fade), Hud.FontBody, centre: false);
            }

            Hud.RectFrom(left, top + HeaderH - 0.0018f, w, 0.0018f, Fade(Palette.Accent, fade));
        }

        // ---- home ---------------------------------------------------------------

        private void Grid(float left, float top, float w, float h, int fade)
        {
            var page = Current;
            if (page == null) return;

            // The cursor landing somewhere is an event, and the tile it lands on says so.
            if (Top.Index != _lastIndex)
            {
                _lastIndex = Top.Index;
                _movedAt = Game.GameTime;
            }

            var padX = Hud.ToX(TilePad);
            var tileW = (w - padX * (Columns + 1)) / Columns;

            var x0 = left + padX;
            var y = top + TilePad;

            for (var i = 0; i < page.Items.Count; i++)
            {
                var col = i % Columns;
                var row = i / Columns;

                var tx = x0 + col * (tileW + padX);
                var ty = y + row * (TileH + TilePad);

                if (ty + TileH > top + h) break;

                // Each app lands a beat after the one before it. The whole run is under a
                // fifth of a second -- long enough to read as arriving, short enough that
                // nobody waiting to press something has to wait for it.
                //
                // NEVER SKIPPED. A tile whose turn has not come is drawn at zero alpha rather
                // than passed over: `continue` meant the row it belonged to had a hole in it
                // for a fraction of a second, and on a grid that is not "not yet arrived", it
                // is "missing".
                var age = Game.GameTime - _openedAt - i * TileStaggerMs;
                var lands = age <= 0 ? 0f
                          : age >= RiseMs ? 1f
                          : 1f - (float)Math.Pow(1f - age / (float)RiseMs, 3);

                Tile(page.Items[i], tx, ty + (1f - lands) * 0.016f, tileW, TileH,
                     i == Top.Index, (int)(fade * lands));
            }
        }

        private void Tile(WheelItem item, float x, float y, float w, float h, bool here,
                          int fade)
        {
            var on = here && item.Enabled;

            var back = !item.Enabled ? Palette.SegmentDisabled
                     : on ? LitEdge
                     : Palette.Segment;

            // Rounded, and the selected one grows into place.
            //
            // The pop is what the eye follows when the cursor jumps two tiles across a grid: a
            // highlight that simply appears somewhere else leaves you re-finding it, and a
            // tile that swells out of the row tells you where it went.
            //
            // Grown about its own CENTRE and clamped, which the first version was not -- it
            // overshot by more than the gap between tiles and drew a separate, larger halo
            // offset from the tile it belonged to, so the live app came out as two shapes that
            // did not line up.
            var pop = 0f;

            if (on)
            {
                var age = Game.GameTime - _movedAt;
                var t = age >= PopMs ? 1f : Math.Max(0f, age / (float)PopMs);

                var e = 1f - (float)Math.Pow(1f - t, 3);
                pop = PopBy * (0.55f + 0.45f * e);
            }

            var popX = Hud.ToX(pop);

            var gx = x - popX;
            var gy = y - pop;
            var gw = w + popX * 2f;
            var gh = h + pop * 2f;

            // A green rim on the live one, concentric with it rather than beside it, so the
            // set's colour marks the tile you are pointing at without becoming a second shape.
            // NO SEPARATE RIM. One shape, and this is the third go at it.
            //
            // A bright outline under a darker fill needs the two to agree about where the
            // corner is, and they cannot: Hud.Disc snaps its rows to a pixel grid anchored on
            // each disc's OWN centre, and these two centres sit a few pixels apart. So the ring
            // came out even down the straight edges and blobbed at the corners -- which is what
            // has been getting reported as circles on the apps.
            //
            // A solid fill has nothing to line up with. The live app is simply green.
            if (false)
            {
                var rim = 0.0026f;
                var rimX = Hud.ToX(rim);

                // Stacked corners, not the sprite, for the same reason the handset uses them.
                //
                // This one shape on the whole screen is drawn UNDERNEATH something else -- the
                // tile fill goes straight over it and only the rim's margin is meant to show.
                // A sprite corner does not stay under a rectangle, so the four corners of the
                // rim came up through the fill and the live app wore four green rings while
                // its straight edges showed nothing at all.
                //
                // Every other tile keeps the sprite: a tile fill has only its own icon and
                // label on top, and both of those are sprites and text, which do layer.
                Hud.RoundRect(gx - rimX, gy - rim, gw + rimX * 2f, gh + rim * 2f,
                          TileRound + rim, Fade(LitEdge, fade), sprite: false, steps: 14);
            }

            // Rounded only when it can be SEEN.
            //
            // A quiet tile is near-black at alpha 200 on a near-black body -- its corners are
            // a difference nobody has ever been able to make out, and rounding all seven of
            // them cost more rectangles than the rest of the screen put together. The live one
            // is the only tile with a shape you can actually read, so it is the only one that
            // gets the treatment.
            //
            // RECTANGLE corners, at four bands each. The sprite version is gone and the
            // reason it was ruled out the first time turned out to be the reason it had to go
            // the second: A RUNTIME TEXTURE FLOATS ABOVE ANY RECTANGLE DRAWN AFTER IT, and
            // that was waved away here on the grounds that nothing overlaps a tile.
            //
            // Two things do. The corner discs came up through the tile's own fill, so the live
            // app wore four visible circles at its corners -- and they outlived the phone
            // itself, still on screen for the frame after it closed, because a sprite in that
            // later pass is not cleared by the rectangles below it going away.
            //
            // Per-row rectangles were what blew the budget before: 208 for one tile at this
            // radius, on top of the handset's shells, past GTA's ceiling -- and over it the
            // game silently drops whatever was issued LAST, which is a rounded rect's corners,
            // which is why it kept coming out with four square bites in it.
            //
            // Six-pixel bands are the middle that was never tried: four bands per corner, 32
            // rectangles rather than 208, on a 13-pixel radius where four steps is a curve
            // and not a staircase. Peak goes to about 355 in a frame, against the 460 that
            // broke it.
            if (on) Hud.RoundRect(gx, gy, gw, gh, TileRound, Fade(back, fade),
                                  sprite: false, steps: 6);
            else Hud.RectFrom(gx, gy, gw, gh, Fade(back, fade));

            if (on) Sheen(gx, gy, gw, gh, fade, TileRound);

            x = gx;
            y = gy;
            w = gw;
            h = gh;

            // White on the live one as well as the quiet ones. See Lit.
            var ink = !item.Enabled ? Palette.TextDisabled : Palette.Text;

            if (item.Tint.HasValue && !on && item.Enabled) ink = item.Tint.Value;

            // Icon, then what it says about itself, then its name. In that order down the
            // tile, because that is the order you read it in.
            //
            // The badge used to sit ABOVE the icon, hard against the top edge, which put "20g"
            // and "FAM" closer to the tile above them than to the app they belong to -- on a
            // grid that reads as a caption for the wrong thing. Underneath, between the icon
            // and the name, it is plainly part of this tile.
            //
            // The icon rides up to make the room rather than the name coming down, so the row
            // of app names stays on one line across the whole screen.
            var named = y + h - 0.024f;
            var badged = named - 0.019f;

            // The hovered app's icon sways. Only the hovered one: a grid where every icon
            // moves is a grid with nothing to look at, and the whole job of this is to say
            // which one the cursor is on.
            var sway = 0f;

            if (on)
            {
                var since = Game.GameTime - _movedAt;

                var kick = since < SwayKickMs ? 1f + (1f - since / (float)SwayKickMs) : 1f;

                sway = (float)Math.Sin(since / SwayMs * Math.PI * 2.0) * SwayDegrees * kick;
            }

            Art(item, x + w * 0.5f, y + h * 0.31f, 0.042f, Fade(ink, fade), sway);

            // A badge when the tile has something to say -- a count, a price, a "3 waiting".
            // The wheel put this in its hub; a grid has no hub, so it goes with the name.
            if (!string.IsNullOrEmpty(item.Value) && item.Value.Length <= 12)
            {
                Hud.Text(Hud.Fit(item.Value, w * 0.94f, 0.20f, Hud.FontBody),
                         x + w * 0.5f, badged, 0.20f,
                         Fade(on ? Green : Palette.TextDim, fade),
                         Hud.FontBody, centre: true);
            }

            Hud.Text(Hud.Fit(item.Label, w * 0.94f, 0.26f, Hud.FontLabel),
                     x + w * 0.5f, named, 0.26f,
                     Fade(ink, fade), Hud.FontLabel, centre: true);
        }

        // ---- lists --------------------------------------------------------------

        private void List(float left, float top, float w, float h, int fade)
        {
            var lvl = Top;
            var page = lvl.Page;

            var visible = Math.Max(1, (int)(h / RowH));

            // Keep the cursor on screen without ever letting the window run off the end.
            if (lvl.Index < lvl.Scroll) lvl.Scroll = lvl.Index;
            if (lvl.Index >= lvl.Scroll + visible) lvl.Scroll = lvl.Index - visible + 1;

            var maxScroll = Math.Max(0, page.Items.Count - visible);
            if (lvl.Scroll > maxScroll) lvl.Scroll = maxScroll;
            if (lvl.Scroll < 0) lvl.Scroll = 0;

            var padX = Hud.ToX(0.012f);

            for (var n = 0; n < visible; n++)
            {
                var i = lvl.Scroll + n;
                if (i >= page.Items.Count) break;

                Row(page.Items[i], left, top + n * RowH, w, i == lvl.Index, i % 2 == 1, padX, fade);
            }

            if (page.Items.Count > visible) ScrollBar(left, top, w, h, lvl, visible, fade);

            if (page.Items.Count == 0)
            {
                Hud.Text("Nothing here.", left + w * 0.5f, top + 0.030f, 0.30f,
                         Fade(Palette.TextDim, fade), Hud.FontBody, centre: true);
            }
        }

        private void Row(WheelItem item, float left, float top, float w, bool here, bool alt,
                         float padX, int fade)
        {
            var on = here && item.Enabled;

            if (on)
            {
                Hud.RectFrom(left, top, w, RowH, Fade(Lit, fade));
                Sheen(left, top, w, RowH, fade);
                Hud.RectFrom(left, top, Hud.ToX(0.0035f), RowH, Fade(LitEdge, fade));
            }
            else if (alt)
            {
                Hud.RectFrom(left, top, w, RowH, Fade(Palette.PanelRowAlt, fade));
            }

            var ink = item.Enabled ? Palette.Text : Palette.TextDisabled;

            var sub = !item.Enabled ? Palette.TextDisabled
                    : on ? Color.FromArgb(235, 176, 214, 178)
                    : Palette.TextDim;

            if (item.Tint.HasValue && !on && item.Enabled) ink = item.Tint.Value;

            var x = left + padX;

            // Art in the gutter, where every other list in the mod puts it.
            var gutter = 0.030f;
            if (HasArt(item))
            {
                Art(item, x + Hud.ToX(gutter) * 0.5f, top + RowH * 0.5f, gutter, Fade(ink, fade));
                x += Hud.ToX(gutter) + Hud.ToX(0.008f);
            }

            var right = left + w - padX;

            // The value first, so the label can be trimmed to whatever is left rather than
            // drawn over the top of it -- and the value itself is CAPPED, which is the half
            // that was missing.
            //
            // Measuring it and subtracting only works while the answer leaves room for a
            // name. A value wider than the row drove `room` to its floor and the label was cut
            // to two letters while the value carried on drawing straight through it. Neither
            // is allowed more than its share now: the value gets at most 45% of the row and is
            // trimmed to fit it, so the name always has the rest.
            var valueWide = 0f;

            if (!string.IsNullOrEmpty(item.Value))
            {
                var vs = 0.27f;
                var cap = (right - x) * 0.45f;

                var shown = Hud.Fit(item.Value, cap, vs, Hud.FontBody);
                valueWide = Hud.MeasureText(shown, vs, Hud.FontBody) + Hud.ToX(0.010f);

                Hud.TextRight(shown, right, top + 0.010f, vs,
                              Fade(on ? Green : Palette.TextDim, fade),
                              Hud.FontBody);
            }

            var room = Math.Max(Hud.ToX(0.04f), right - valueWide - x);

            var detail = item.Enabled ? item.Detail : item.DisabledReason;
            var tall = !string.IsNullOrEmpty(detail);

            Hud.Text(Hud.Fit(item.Label, room, 0.31f, Hud.FontBody),
                     x, top + (tall ? 0.006f : 0.014f), 0.31f,
                     Fade(ink, fade), Hud.FontBody, centre: false);

            if (tall)
            {
                Hud.Text(Hud.Fit(detail, room, 0.23f, Hud.FontBody),
                         x, top + 0.029f, 0.23f,
                         Fade(sub, fade), Hud.FontBody, centre: false);
            }

            // A submenu says so, the way every list on a phone says so.
            if (item.IsSubmenu && string.IsNullOrEmpty(item.Value))
            {
                Hud.TextRight(">", right, top + 0.011f, 0.30f,
                              Fade(on ? Green : Palette.TextDim, fade),
                              Hud.FontBody);
            }
        }

        private static void ScrollBar(float left, float top, float w, float h, Level lvl,
                                      int visible, int fade)
        {
            var trackX = left + w - Hud.ToX(0.0045f);
            var trackW = Hud.ToX(0.0022f);

            Hud.RectFrom(trackX, top, trackW, h, Fade(Color.FromArgb(40, 255, 255, 255), fade));

            var n = lvl.Page.Items.Count;
            var frac = visible / (float)n;
            var barH = Math.Max(0.018f, h * frac);
            var span = h - barH;
            var at = n - visible <= 0 ? 0f : lvl.Scroll / (float)(n - visible);

            Hud.RectFrom(trackX, top + span * at, trackW, barH, Fade(Palette.Accent, fade));
        }

        // ---- bits ---------------------------------------------------------------

        private static bool HasArt(WheelItem item)
        {
            return !string.IsNullOrEmpty(item.IconFile)
                || !string.IsNullOrEmpty(item.IconBlip)
                || item.HasIcon
                || !string.IsNullOrEmpty(item.Symbol);
        }

        /// <summary>
        /// Whatever art this item has, in the order the mod already resolves it.
        ///
        /// Ours first: a PNG needs nothing streamed and cannot fail halfway. Then the game's
        /// own texture if it has finished streaming, then a blip drawn as TEXT because blip
        /// sprites address the map and cannot be handed to DRAW_SPRITE, then the plain glyph.
        /// </summary>
        private static void Art(WheelItem item, float cx, float cy, float size, Color c,
                                float spin = 0f)
        {
            if (!string.IsNullOrEmpty(item.IconFile))
            {
                if (Hud.File(item.IconFile, cx, cy, size, spin, c)) return;
            }

            if (item.HasIcon)
            {
                var w = Hud.ToX(size) * (item.IconAspect <= 0f ? 1f : item.IconAspect);
                Hud.Sprite(item.IconDict, item.IconTexture, cx, cy, w, size, spin, c);
                return;
            }

            if (!string.IsNullOrEmpty(item.IconBlip))
            {
                Hud.Text(item.IconBlip, cx, cy - 0.013f, 0.36f, c, Hud.FontBody, centre: true);
                return;
            }

            if (!string.IsNullOrEmpty(item.Symbol))
            {
                Hud.Text(item.Symbol, cx, cy - 0.012f, 0.34f, c, Hud.FontLabel, centre: true);
            }
        }

        private void Footer(float left, float top, float w, int fade)
        {
            Hud.RectFrom(left, top, w, FooterH, Fade(Color.FromArgb(220, 16, 18, 20), fade));
            Hud.RectFrom(left, top, w, 0.0014f, Fade(Color.FromArgb(46, 255, 255, 255), fade));

            var hint = AtHome ? "ARROWS  MOVE      ENTER  OPEN      BACKSPACE  PUT IT AWAY"
                              : "ARROWS  MOVE      ENTER  PICK      BACKSPACE  BACK";

            Hud.Text(Hud.Fit(hint, w * 0.96f, 0.20f, Hud.FontLabel),
                     left + w * 0.5f, top + 0.012f, 0.20f,
                     Fade(Palette.TextDim, fade), Hud.FontLabel, centre: true);
        }
    }
}
