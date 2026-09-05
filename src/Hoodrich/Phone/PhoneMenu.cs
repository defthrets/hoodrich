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
    /// <summary>
    /// One line in the notification shade: a picture and a sentence.
    ///
    /// Deliberately dumb. Everything that could be a notification lives in a different corner
    /// of the mod -- the inbox is static, the war is a field on Main, the debt is its own
    /// object -- and none of them should have to know a phone exists. Main assembles these
    /// from what it can already see and hands over a list of sentences.
    /// </summary>
    internal sealed class Alert
    {
        public string Icon;
        public string Text;
    }

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
        /// <summary>
        /// A hair shorter than it was, and the bank card is why.
        ///
        /// Ten apps is four rows, and the body has room for five -- so a card a whole row tall
        /// leaves exactly four, which fits by one thousandth of a screen. It DOES fit, and a
        /// layout that survives on a thousandth is one bad constant away from the tenth app
        /// silently not being drawn, with nothing anywhere saying so.
        ///
        /// Four per cent off every tile buys two hundredths of clearance and is not visible on
        /// a handset this size. The alternative was a card shorter than a row, which is not
        /// what was asked for.
        /// </summary>
        private const float TileH = 0.108f;

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


        /// <summary>How round the handset and its screen are.</summary>
        private const float BodyRound = 0.020f;
        private const float ScreenRound = 0.013f;

        /// <summary>How long a tile takes to pop when the cursor lands on it.</summary>
        private const int PopMs = 150;

        /// <summary>
        /// The tiles are SQUARE, and everything below is what replaced the rounding.
        ///
        /// Rounding a 13-pixel corner out of rectangles is four bands of three pixels, and it
        /// looked like four bands of three pixels. Out of a sprite it is smooth and floats
        /// above the fill, because a runtime texture draws in a later pass -- which is where
        /// the circles on the apps came from, and why they outlived the phone closing.
        ///
        /// There is no third way to round a corner here. So they are square, and the effort
        /// goes into things rectangles are actually good at: a shadow to lift the tile off the
        /// body, a hairline border, and a light that runs around the live one.
        /// </summary>
        private const float TileShadow = 0.0028f;
        private const float TileEdge = 0.0020f;


        /// <summary>
        /// The jiggle on the icon of whatever you are hovering.
        ///
        /// A JIGGLE AND NOT A SWAY. The first version leaned five degrees over a second and a
        /// sixth, which is a slow lean -- graceful, and it read as the tile breathing rather
        /// than as the thing under your cursor being alive. Three and a bit a second is the
        /// rate that reads as a shiver.
        ///
        /// TWO PERIODS THAT DO NOT DIVIDE INTO EACH OTHER, the smaller one a third the weight
        /// of the larger. On a single sine an icon at this rate is a metronome and the eye
        /// finds the loop immediately; at 300 and 470 the two drift in and out of step and it
        /// never quite repeats.
        ///
        /// FROM _movedAt, so it starts upright every time the cursor lands rather than being
        /// caught mid-swing on a clock that never stopped -- and it arrives with a kick and
        /// settles, which is the difference between a tile that idles and one that has just
        /// been nudged by you.
        /// </summary>
        private const double JiggleMs = 300.0;
        private const double JiggleOffMs = 470.0;
        private const float JiggleDegrees = 6f;
        private const int JiggleKickMs = 380;

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
        ///
        /// AND IT STAYS GREEN while every panel in the mod went gold and ember. The phone is
        /// the one object here that is a THING rather than a screen -- a handset in his hand
        /// with its own make and its own colour -- and the user asked for it kept. So the
        /// rails, rules and cursor that used to be white on this handset are green too, rather
        /// than the panels' gold: one identity per object.
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
        /// <summary>
        /// Brightened, because at 20,46,26 it could not be seen at all.
        ///
        /// THE ROW WAS ALWAYS BEING DRAWN. The bug was never a missing highlight -- it was a
        /// near-black green laid on a near-black panel, which is a highlight in the code and
        /// nothing on the screen. "Where to?" came out as ten identical lines with no way to
        /// tell which one you were on.
        ///
        /// Still a bed rather than a block: white text has to stay readable on it, which is
        /// the whole reason this is not the inverted near-white bar it replaced.
        /// </summary>
        private static readonly Color Lit = Color.FromArgb(245, 28, 72, 38);
        private static readonly Color LitEdge = Color.FromArgb(255, 108, 196, 106);

        // ---- state --------------------------------------------------------------

        /// <summary>One level of the menu, and where the player was on it.</summary>
        private sealed class Level
        {
            public WheelPage Page;
            public int Index;
            public int Scroll;
        }

        /// <summary>
        /// The lifetime take, for the bank card. Null until Main hands it over.
        ///
        /// A hook rather than a reference to the save, because this class knows about a
        /// Settings object and nothing else, and the one number it wants is not worth handing
        /// it the whole of PlayerState to reach.
        /// </summary>
        public Func<long> Earned;

        /// <summary>What the last corner sale paid. See PlayerState.LastDeal.</summary>
        public Func<long> LastDeposit;

        /// <summary>Everything worth telling you about, newest concern first. See Shade.</summary>
        public Func<List<Alert>> Alerts;

        private List<Alert> _alerts = new List<Alert>();
        private int _askedAt;
        private int _atAlert;
        private int _turnedAt;

        /// <summary>How often the list is rebuilt, how long each one is up, and the swap.</summary>
        private const int AskEveryMs = 1500;
        private const int HoldMs = 3800;
        private const int SwapMs = 300;

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

        // ---- the handset's own animations -------------------------------------------

        /// <summary>When it was put away, so it drops and goes dark rather than vanishing.</summary>
        private int _closedAt;
        private const int DropMs = 170;

        /// <summary>
        /// The glass wakes a beat AFTER the body arrives, and how long the waking takes. A
        /// phone taken out of a pocket is a dark object first and a lit one second; a handset
        /// that appears already glowing is a panel wearing a phone.
        /// </summary>
        private const int WakeLagMs = 70;
        private const int WakeMs = 170;

        /// <summary>True for the moment after Close while the handset is still on its way down.</summary>
        public bool Leaving => !IsOpen && _closedAt != 0 && Game.GameTime - _closedAt < DropMs;

        /// <summary>
        /// The cursor frame that glides between apps and rows, in the handset's own green
        /// rather than the panels' gold. See UI.Glide.
        /// </summary>
        private readonly Glide _glide = new Glide
        {
            RimInk = Color.FromArgb(255, 176, 236, 172),
            GlowInk = Color.FromArgb(255, 108, 196, 106)
        };

        /// <summary>When the page last changed, and which way, so it can slide in.</summary>
        private int _pageAt;
        private int _pageDir;

        /// <summary>When the cursor last moved, so the tile under it can pop.</summary>
        private int _movedAt;
        private int _lastIndex = -1;

        public bool IsOpen { get; private set; }

        /// <summary>
        /// Whether the handset is showing an incoming call rather than the apps.
        ///
        /// The SAME phone, with its screen replaced. Everything below the glass -- the body, the
        /// bezel, the status bar, the way it rises into frame -- is what makes it read as his
        /// phone rather than as a panel, and none of it wants rebuilding for this.
        ///
        /// It is deliberately NOT opened through PhoneController.OpenPhone, because that slows
        /// time and blurs the world for somebody about to browse a menu. A phone ringing is not a
        /// menu: the street should carry on around it.
        /// </summary>
        public bool InCall { get; private set; }

        private string _callWho = "";
        private string _callPic = "";

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
            _closedAt = 0;
            _pageAt = Game.GameTime;
            _pageDir = 0;
            _glide.Reset();

            Beep("SELECT");
        }

        public void Close()
        {
            if (!IsOpen) return;

            // The glass goes dark and the body drops -- see Render, which keeps drawing for
            // DropMs after this. The pages are gone at once: there is nothing on a screen that
            // has just been switched off.
            IsOpen = false;
            _closedAt = Game.GameTime;
            _stack.Clear();
        }

        /// <summary>Bring it out ringing, with no page stack behind it.</summary>
        public void OpenCall(string who, string pic)
        {
            _callWho = who ?? "";
            _callPic = pic ?? "";

            _stack.Clear();

            InCall = true;
            IsOpen = true;
            _openedAt = Game.GameTime;
            _closedAt = 0;
        }

        public void CloseCall()
        {
            if (!InCall) return;

            InCall = false;
            IsOpen = false;
            _closedAt = Game.GameTime;
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
            if (!IsOpen && !Leaving) return;
            if (IsOpen && !InCall && _stack.Count == 0) return;

            // Rising in, or dropping out: the same curve, run the other way on the way down.
            var t = IsOpen ? Ease() : 1f - Drop();

            var bodyW = Hud.ToX(BodyH * BodyRatio);
            var left = BodyRight - bodyW;
            var top = BodyTop + (1f - t) * RiseBy;

            var fade = (int)(255 * t);

            // The glass lights up a beat after the body arrives and is dark the whole way out.
            var glass = IsOpen ? Wake() : 0f;

            Body(left, top, bodyW, BodyH, fade, glass);

            // On the way out there is nothing on the screen but the screen.
            if (!IsOpen) return;

            var screen = (int)(255 * t * glass);
            if (screen <= 0) return;

            var bezX = Hud.ToX(Bezel);
            var scrLeft = left + bezX;
            var scrTop = top + Bezel;
            var scrW = bodyW - bezX * 2f;
            var scrH = BodyH - Bezel * 2f;

            StatusBar(scrLeft, scrTop, scrW, screen);

            if (InCall)
            {
                CallScreen(scrLeft, scrTop + StatusH, scrW, scrH - StatusH, screen);
                return;
            }

            var headTop = scrTop + StatusH;
            Header(scrLeft, headTop, scrW, screen);

            var bodyTop = headTop + HeaderH;
            var bodyHeight = scrH - StatusH - HeaderH - FooterH;

            // The page slides in from whichever way you went. Drilling in comes from the
            // right, backing out comes from the left, so the direction carries the meaning
            // rather than the animation merely being present.
            var landed = Landed();
            var slide = _pageDir == 0 ? 0f : (1f - landed) * Hud.ToX(PageSlide) * _pageDir;
            var pageFade = (int)(screen * (0.35f + 0.65f * landed));

            _glide.Begin();

            if (AtHome) Grid(scrLeft + slide, bodyTop, scrW, bodyHeight, pageFade);
            else List(scrLeft + slide, bodyTop, scrW, bodyHeight, pageFade);

            Footer(scrLeft, scrTop + scrH - FooterH, scrW, screen);

            // Last, so it rides over whatever it is pointing at.
            _glide.Draw(screen / 255f);
        }

        /// <summary>Nought to one over the drop, eased IN: it leaves quicker than it arrived.</summary>
        private float Drop()
        {
            var age = Game.GameTime - _closedAt;
            if (age >= DropMs) return 1f;

            var x = Math.Max(0f, Math.Min(1f, age / (float)DropMs));
            return x * x;
        }

        /// <summary>How lit the glass is: nought until the body has landed, one a few frames later.</summary>
        private float Wake()
        {
            var age = Game.GameTime - _openedAt - WakeLagMs;
            if (age <= 0) return 0f;
            if (age >= WakeMs) return 1f;

            var x = age / (float)WakeMs;
            return 1f - (1f - x) * (1f - x);
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

        /// <summary>
        /// THE WALLPAPER: two soft discs of the set's green drifting slowly under the apps, and
        /// nothing else. A phone has something behind its icons; a flat black home screen reads
        /// as a menu that happens to be phone-shaped. Faint -- under a tenth of the icons' own
        /// ink -- so it is a ground rather than a picture, and slow enough that nobody sees it
        /// move, only that it has.
        ///
        /// Drawn in coarse rows: a disc this size is a hundred rectangles at full fineness,
        /// and a wash does not need a clean edge.
        /// </summary>
        private static void Wallpaper(float left, float top, float w, float h, int fade)
        {
            var t = Game.GameTime / 1000.0;

            var cx = left + w * 0.5f;
            var cy = top + h * 0.40f;

            var ax = cx + Hud.ToX(0.030f) * (float)Math.Sin(t / 9.1);
            var ay = cy + 0.030f * (float)Math.Sin(t / 12.7 + 1.0);

            var bx = cx - Hud.ToX(0.026f) * (float)Math.Sin(t / 11.3 + 2.0);
            var by = cy + 0.050f + 0.024f * (float)Math.Sin(t / 8.3);

            Hud.Disc(ax, ay, 0.085f, Fade(Color.FromArgb(20, 108, 196, 106), fade), 6);
            Hud.Disc(bx, by, 0.060f, Fade(Color.FromArgb(26, 66, 124, 68), fade), 6);
        }

        private void Body(float left, float top, float w, float h, int fade, float glass)
        {
            // THE RIM, WITH A LIGHT ON IT. Two rounded rectangles a hair apart: the lighter one
            // shows only along the top edge, where the darker one -- set a fraction lower --
            // does not cover it. A bevel for the price of one extra shape. Chrome catches
            // light from above, and a rim that is one flat grey all the way round is a sticker.
            //
            // RECTS at the corners, not the sprite: a runtime-texture sprite comes out ON TOP
            // of any rectangle drawn after it, whatever order they were issued in, and the
            // handset is the bottom layer of everything on this screen. Twenty steps is a
            // three-pixel stagger on a two-pixel band, which is to say invisible.
            const float catchLight = 0.0009f;

            Hud.RoundRect(left, top, w, h, BodyRound,
                          Fade(Color.FromArgb(255, 134, 140, 142), fade), sprite: false, steps: 20);

            Hud.RoundRect(left, top + catchLight, w, h - catchLight, BodyRound,
                          Fade(Color.FromArgb(255, 72, 76, 78), fade), sprite: false, steps: 20);

            var edge = 0.0022f;
            var edgeX = Hud.ToX(edge);

            Hud.RoundRect(left + edgeX, top + edge, w - edgeX * 2f, h - edge * 2f,
                          BodyRound - edge, Fade(Color.FromArgb(252, 8, 9, 10), fade), sprite: false);

            // THE KEYS ON THE SIDES. Power on the right, two volume keys on the left, the way
            // every handset anybody has held is laid out. They stand a couple of pixels proud
            // of the rim, in the rim's own grey, and they are the cheapest thing on this screen
            // that says "object" rather than "window".
            var keyOut = Hud.ToX(0.0024f);
            var keyInk = Fade(Color.FromArgb(255, 98, 104, 106), fade);

            Hud.RectFrom(left + w, top + h * 0.20f, keyOut, 0.046f, keyInk);
            Hud.RectFrom(left - keyOut, top + h * 0.17f, keyOut, 0.026f, keyInk);
            Hud.RectFrom(left - keyOut, top + h * 0.17f + 0.032f, keyOut, 0.026f, keyInk);

            // THE SPEAKER AND THE CAMERA, in the top bezel. A dark slit and a darker dot with
            // a point of light in it: the two marks that make a black rectangle read as the
            // front of a phone rather than the back of one.
            var mid = left + w * 0.5f;
            var slitW = Hud.ToX(0.052f);
            const float slitH = 0.0030f;
            var slitY = top + Bezel * 0.5f - slitH * 0.5f;

            Hud.RoundRect(mid - slitW * 0.5f, slitY, slitW, slitH, slitH * 0.5f,
                          Fade(Color.FromArgb(255, 30, 33, 35), fade), sprite: false, steps: 4);

            var lensX = mid + slitW * 0.5f + Hud.ToX(0.011f);
            var lensY = top + Bezel * 0.5f;

            Hud.Disc(lensX, lensY, 0.0022f, Fade(Color.FromArgb(255, 24, 28, 32), fade));
            Hud.Disc(lensX - Hud.ToX(0.0006f), lensY - 0.0006f, 0.0007f,
                     Fade(Color.FromArgb(210, 120, 150, 170), fade));

            // THE HOME BAR, in the bottom bezel.
            var barW = Hud.ToX(0.040f);

            Hud.RoundRect(mid - barW * 0.5f, top + h - Bezel * 0.5f - 0.0012f, barW, 0.0024f, 0.0012f,
                          Fade(Color.FromArgb(255, 88, 94, 96), fade), sprite: false, steps: 4);

            // AND THE GLASS, WAKING. Black until the body has landed, then up to the screen's
            // own near-black over a few frames, which is what a phone does when it is taken
            // out. Fine bands on the screen: this is the one shape everything sits on.
            var bezX = Hud.ToX(Bezel);
            var dark = Color.FromArgb(252, 2, 2, 3);
            var lit = Color.FromArgb(252, 13, 15, 17);

            Hud.RoundRect(left + bezX, top + Bezel, w - bezX * 2f, h - Bezel * 2f,
                          ScreenRound, Fade(Lerp(dark, lit, glass), fade), sprite: false, steps: 0);

            // A catch of light along the top of the glass, so it is glass rather than paint.
            if (glass > 0f)
            {
                var inX = Hud.ToX(ScreenRound);

                Hud.RectFrom(left + bezX + inX, top + Bezel, w - bezX * 2f - inX * 2f, 0.0014f,
                             Fade(Color.FromArgb((int)(22 * glass), 255, 255, 255), fade));
            }
        }

        /// <summary>
        /// Who is ringing, and the two things you can do about it.
        ///
        /// No apps, no rows, no footer. A phone showing a call shows a face, a name and two
        /// buttons; anything else on that screen is the mod talking over him.
        /// </summary>
        private void CallScreen(float left, float top, float w, float h, int fade)
        {
            var pad = Hud.ToX(0.013f);
            var mid = left + w * 0.5f;

            Hud.Text("INCOMING CALL", mid, top + 0.026f, 0.30f,
                     Fade(Palette.TextDim, fade), Hud.FontLabel, centre: true);

            // A plain block behind the face, so a texture that will not stream is a shape
            // rather than a hole.
            var picH = 0.150f;
            var picW = Hud.ToX(picH);
            var picY = top + 0.070f;

            Hud.RectFrom(mid - picW * 0.5f, picY, picW, picH, Fade(Palette.PanelHeader, fade));

            // RINGS LEAVING THE PICTURE, the way a ringing phone's screen does it: two of them,
            // half a cycle apart, growing and fading. Movement that means "answer me", which
            // is a different thing from the breathing word underneath that means "still going".
            for (var k = 0; k < 2; k++)
            {
                var ph = ((Game.GameTime + k * 700) % 1400) / 1400f;
                var g = 0.008f + 0.030f * ph;

                Theme.Rim(mid - picW * 0.5f - Hud.ToX(g), picY - g, picW + Hud.ToX(g) * 2f, picH + g * 2f,
                         0.0016f, Fade(Color.FromArgb((int)(150 * (1f - ph)), 108, 196, 106), fade));
            }

            if (!string.IsNullOrEmpty(_callPic) && Hud.EnsureTextureDict(_callPic))
            {
                Hud.Sprite(_callPic, _callPic, mid, picY + picH * 0.5f, picW, picH, 0f,
                           Color.FromArgb(fade, 255, 255, 255));
            }

            Hud.Text(_callWho, mid, picY + picH + 0.020f, 0.68f,
                     Fade(Palette.Text, fade), Hud.FontLabel, centre: true);

            // Breathing, so a still screen still reads as a phone that is ringing.
            var turn = (Game.GameTime % PulseMs) / (double)PulseMs * Math.PI * 2d;
            // 0..1, NOT -1..1. Sin swings negative and this used to be written as a plain
    // 0.35 + 0.65 * sin, which dips to minus a third -- a negative alpha, which is an
    // ArgumentException out of the draw and takes the whole tick with it. The status
    // bar above already had this right; I did not copy it properly.
    var breath = 0.35f + 0.65f * (0.5f + 0.5f * (float)Math.Sin(turn));

            Hud.Text("calling", mid, picY + picH + 0.062f, 0.34f,
                     Fade(Palette.TextDim, (int)(fade * breath)), Hud.FontBody, centre: true);

            var bw = (w - pad * 3f) * 0.5f;
            var bh = 0.055f;
            var by = top + h - bh - 0.030f;

            Hud.RectFrom(left + pad, by, bw, bh, Fade(Palette.Cash, fade));
            Hud.Text("ANSWER", left + pad + bw * 0.5f, by + 0.014f, 0.40f,
                     Fade(Palette.TextOnHover, fade), Hud.FontLabel, centre: true);

            Hud.RectFrom(left + pad * 2f + bw, by, bw, bh, Fade(Palette.Warn, fade));
            Hud.Text("DECLINE", left + pad * 2f + bw * 1.5f, by + 0.014f, 0.40f,
                     Fade(Palette.TextOnHover, fade), Hud.FontLabel, centre: true);
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

            // SMALLER THAN IT WAS, because the mark it draws is not the mark it was drawn
            // for. The wordmark went from a condensed block face to Fraktur, which is heavier
            // per line and busier at every size -- at 0.0150 it stopped being a small mark in
            // the corner of a status bar and became the loudest thing on the handset, with the
            // carrier and the wifi it should be sharing that end with nowhere left to go.
            Hud.Brand(left + pad, mid + lift, 0.0118f * swell,
                      Fade(Lerp(GreenDim, Green, breath), fade));

            // ---- WHOSE NETWORK IT IS ----
            //
            // Badger, which is the carrier in this city -- it is on the phones in the game's
            // own interface and on the masts out at the airport. Naming a real one would be
            // the single most out-of-place string in the mod.
            var markW = Hud.ToX(0.0118f) * Hud.WordmarkAspect;

            Hud.Text("BADGER", left + pad + markW + Hud.ToX(0.006f), top + 0.0075f, 0.21f,
                     Fade(GreenDim, fade), Hud.FontLabel, centre: false);

            // The game's clock, because a phone that says the wrong time is a prop.
            var hh = Function.Call<int>(Hash.GET_CLOCK_HOURS);
            var mm = Function.Call<int>(Hash.GET_CLOCK_MINUTES);

            Hud.Text(hh.ToString("00") + ":" + mm.ToString("00"),
                     left + w * 0.5f, top + 0.006f, 0.26f,
                     Fade(Palette.Text, fade), Hud.FontLabel, centre: true);

            var right = left + w - pad;

            Battery(right, mid, fade);

            // Spaced off the widths they actually occupy now. The battery is 0.027 plus its
            // nub, and the four bars come to about 0.023 -- so the old 0.026 step had the
            // signal drawing through the battery's left wall.
            var signalRight = right - Hud.ToX(0.036f);

            Signal(signalRight, mid, fade, Game.GameTime - _openedAt);
            Wifi(signalRight - Hud.ToX(0.028f), mid, fade);

            Hud.RectFrom(left, top + StatusH - 0.0014f, w, 0.0014f, Fade(GreenDim, fade));
        }

        /// <summary>
        /// The wifi fan: three arcs and a dot, climbing away from the corner.
        ///
        /// DRAWN RATHER THAN LOADED. At twelve pixels tall a PNG of this is four grey smudges,
        /// and the whole status bar is already built out of rectangles -- the battery, the
        /// signal, the rule under it. One more shape made the same way costs nothing and
        /// matches everything beside it.
        ///
        /// Each arc is three rectangles: a flat top and two shoulders dropping away from it.
        /// That is enough to read as a curve at this size, and a real arc at this size is the
        /// same three pixels with more arithmetic.
        /// </summary>
        private static void Wifi(float rightX, float midY, int fade)
        {
            var ink = Fade(Green, fade);

            var unit = Hud.ToX(0.0016f);
            var step = 0.0030f;

            var cx = rightX - Hud.ToX(0.008f);
            var foot = midY + 0.0062f;

            // The dot at the bottom, which is the part everybody actually recognises.
            Hud.RectFrom(cx - unit, foot - 0.0016f, unit * 2f, 0.0016f, ink);

            for (var i = 0; i < 3; i++)
            {
                var span = Hud.ToX(0.0040f) + i * Hud.ToX(0.0034f);
                var y = foot - 0.0030f - i * step;

                // The flat of the arc.
                Hud.RectFrom(cx - span, y, span * 2f, unit, ink);

                // And the two shoulders, dropping away from it.
                Hud.RectFrom(cx - span - unit, y + unit * 0.6f, unit, 0.0013f, ink);
                Hud.RectFrom(cx + span, y + unit * 0.6f, unit, 0.0013f, ink);
            }
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
        private static void Signal(float rightX, float midY, int fade, int wake)
        {
            const int bars = 4;
            var lit = bars;

            // IT FINDS THE NETWORK when the phone wakes: the bars climb one at a time over the
            // first half second, then hold. A phone that has full reception the instant its
            // screen comes on is a picture of reception.
            var climb = wake < 0 ? bars : Math.Min(bars, 1 + Math.Max(0, wake - WakeLagMs) / 110);

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

            // Same reasoning as the battery: three pixels wide and three tall is a bar
            // nobody can count, let alone watch climb.
            var wide = Hud.ToX(0.0040f);
            var gap = Hud.ToX(0.0018f);
            var bottom = midY + 0.0075f;

            for (var i = 0; i < bars; i++)
            {
                var h = 0.0044f + i * 0.0032f;
                var x = rightX - (bars - i) * (wide + gap);

                Hud.RectFrom(x, bottom - h, wide, h,
                             Fade(i < Math.Min(lit, climb) ? Green : Color.FromArgb(70, 96, 108, 96), fade));
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

            // BIG ENOUGH TO BE A BATTERY. The status bar is 0.030 tall and this was drawing
            // an eleven-thousandth-high shell inside it with 1.4-thousandth walls -- about
            // nine pixels by one at the resolution this is actually played at. Every part of
            // it was correct and none of it was visible.
            var bodyW = Hud.ToX(0.027f);
            var bodyH = 0.0150f;
            var left = rightX - bodyW;
            var top = midY - bodyH * 0.5f;

            // THE PHONE'S OWN GREEN, NOT THE PANELS' GREY. TextDim is 190 alpha of a
            // near-grey, which on this handset is a shape you can only find if you know it is
            // there -- and every other piece of chrome on this bar is green. Reported as the
            // battery simply not being drawn, which is what a 16x9 pixel grey outline on black
            // amounts to.
            var ink = low ? Palette.Danger : Green;

            // Blinks about twice a second, and only when it is nearly out.
            if (low)
            {
                var on = (Game.GameTime / 380) % 2 == 0;
                if (!on) ink = Color.FromArgb(70, ink.R, ink.G, ink.B);
            }

            var line = 0.0022f;
            var lineX = Hud.ToX(line);

            // Shell.
            Hud.RectFrom(left, top, bodyW, line, Fade(ink, fade));
            Hud.RectFrom(left, top + bodyH - line, bodyW, line, Fade(ink, fade));
            Hud.RectFrom(left, top, lineX, bodyH, Fade(ink, fade));
            Hud.RectFrom(left + bodyW - lineX, top, lineX, bodyH, Fade(ink, fade));

            // The nub on the end.
            Hud.RectFrom(left + bodyW, midY - 0.0034f, Hud.ToX(0.0032f), 0.0068f, Fade(ink, fade));

            // And what is left in it. Full ink rather than the shell's, so the charge reads as
            // a level rather than as more outline.
            var inset = Hud.ToX(0.0028f);
            var room = bodyW - inset * 2f;

            Hud.RectFrom(left + inset, top + 0.0034f, room * charge, bodyH - 0.0068f,
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

            // THE HOME HEADER IS A NOTIFICATION SHADE WHEN THERE IS ANYTHING IN IT.
            //
            // The set's name is a fact that does not change from one week to the next, and it
            // was taking the widest, boldest line on the phone to say it -- on the one screen
            // you open BECAUSE something has happened. A phone puts what is new up there and
            // its own name nowhere, and falls back to something quiet when there is nothing.
            //
            // Only on home. Every other page keeps its title, because GANGS or CONTACTS at the
            // top of a page you have drilled into is a breadcrumb telling you where you are,
            // and losing that to a rolling ticker would be a straight downgrade.
            if (AtHome && Shade(left + pad, top, w - pad * 2f, fade))
            {
                Hud.RectFrom(left, top + HeaderH - 0.0018f, w, 0.0018f, Fade(Green, fade));
                return;
            }

            Hud.Text(Hud.Fit(title.ToUpperInvariant(), w - pad * 2f, 0.44f, Hud.FontLabel),
                     left + pad, top + (string.IsNullOrEmpty(under) ? 0.016f : 0.007f), 0.44f,
                     Fade(Palette.Text, fade), Hud.FontLabel, centre: false);

            if (!string.IsNullOrEmpty(under))
            {
                Hud.Text(Hud.Fit(under, w - pad * 2f, 0.25f, Hud.FontBody),
                         left + pad, top + 0.032f, 0.25f,
                         Fade(Palette.TextDim, fade), Hud.FontBody, centre: false);
            }

            Hud.RectFrom(left, top + HeaderH - 0.0018f, w, 0.0018f, Fade(Green, fade));
        }

        /// <summary>
        /// The notification shade: one thing at a time, changing every few seconds.
        ///
        /// ONE AT A TIME RATHER THAN A STACK, because the band is one line tall and always was.
        /// Three shrunk to fit is three things you cannot read; one at full size that changes
        /// is the same information delivered at a speed a person can take it in, and it is what
        /// a lock screen does with more than it can show.
        ///
        /// SEQUENTIAL, NOT CROSS-FADED. The outgoing one fades out over the first half of the
        /// swap and the incoming rises in over the second -- no frame has two of them on it.
        /// A true cross-fade would need both drawn at once, and both drawn at once inside a
        /// band this shallow means one of them poking up through the status bar, which cannot
        /// be clipped away because everything here is a rectangle rather than a viewport.
        ///
        /// The pips on the right are the other half of "there is more than this". Without them
        /// a shade that changes is a header that will not sit still.
        ///
        /// Returns false when there is nothing to say, and the header goes back to printing
        /// which set you run with -- which is the right thing for a phone with nothing on it.
        /// </summary>
        private bool Shade(float x, float top, float w, int fade)
        {
            var now = Game.GameTime;

            if (Alerts != null && now - _askedAt >= AskEveryMs)
            {
                _askedAt = now;

                try
                {
                    _alerts = Alerts() ?? new List<Alert>();
                }
                catch
                {
                    // Whatever it said last time stands. A shade is not worth a crash.
                }
            }

            if (_alerts.Count == 0) return false;

            // The list is rebuilt from scratch every second and a half, so the thing you were
            // reading can vanish out from under the cursor -- a text gets read, a war ends.
            // Clamped rather than reset, so the strip does not jump back to the first one
            // every time anything at all changes.
            if (_atAlert >= _alerts.Count) _atAlert = 0;

            if (_alerts.Count > 1 && now - _turnedAt >= HoldMs)
            {
                _turnedAt = now;
                _atAlert = (_atAlert + 1) % _alerts.Count;
            }

            var since = now - _turnedAt;

            // Nought to one across the whole swap, used as two halves.
            var half = SwapMs * 0.5f;

            float lift, alpha;

            if (_turnedAt == 0 || since >= SwapMs)
            {
                lift = 0f;
                alpha = 1f;
            }
            else if (since < half)
            {
                // The one going out. Held still and faded, so nothing travels upwards.
                lift = 0f;
                alpha = 1f - since / half;
            }
            else
            {
                var t = (since - half) / half;

                lift = EnterLift * (1f - t);
                alpha = t;
            }

            var one = _alerts[since < half && _alerts.Count > 1
                              ? (_atAlert + _alerts.Count - 1) % _alerts.Count
                              : _atAlert];

            var ink = (int)(fade * alpha);

            var tx = x;

            if (!string.IsNullOrEmpty(one.Icon) &&
                Hud.File(one.Icon, x + Hud.ToX(ShadeIcon) * 0.5f, top + 0.023f - lift,
                         ShadeIcon, 0f, Fade(LitEdge, ink)))
            {
                tx = x + Hud.ToX(ShadeIcon) + 0.007f;
            }

            // The pips first, so the sentence can be trimmed to what is left rather than
            // running under them.
            var pipsW = 0f;

            if (_alerts.Count > 1)
            {
                pipsW = _alerts.Count * (Hud.ToX(PipW) + Hud.ToX(PipGap));

                for (var i = 0; i < _alerts.Count; i++)
                {
                    var px = x + w - pipsW + i * (Hud.ToX(PipW) + Hud.ToX(PipGap));

                    Hud.RectFrom(px, top + 0.0255f, Hud.ToX(PipW), 0.0030f,
                                 i == _atAlert
                                     ? Fade(Palette.Text, fade)
                                     : Color.FromArgb((int)(fade * 0.30f), 255, 255, 255));
                }

                pipsW += 0.006f;
            }

            var room = x + w - pipsW - tx;

            Hud.Text(Hud.Fit(one.Text, room, 0.34f, Hud.FontLabel), tx, top + 0.014f - lift,
                     0.34f, Fade(Palette.Text, ink), Hud.FontLabel, centre: false);

            return true;
        }

        /// <summary>The mark on a notification, and how far the incoming one rises.</summary>
        private const float ShadeIcon = 0.019f;
        private const float EnterLift = 0.009f;

        /// <summary>One pip per waiting notification.</summary>
        private const float PipW = 0.0075f;
        private const float PipGap = 0.0035f;

        // ---- home ---------------------------------------------------------------

        /// <summary>How tall one row of a list page is, and the gap under it.</summary>
        private const float ListRowH = 0.050f;
        private const float ListRowGap = 0.005f;

        /// <summary>The art on a row. Small, because on a list the words are the content.</summary>
        private const float ListArt = 0.026f;

        /// <summary>
        /// A page as a list of rows.
        ///
        /// Everything a tile does to be findable at a glance -- the pop, the sheen, the runner
        /// -- is missing on purpose. Those exist because a cursor on a grid jumps two columns
        /// and a row across and you have to re-find it; on a list it moves one line at a time
        /// and there is nowhere for it to go that you were not already looking.
        /// </summary>
        private void Rows(float left, float top, float w, float h, int fade)
        {
            var page = Current;
            if (page == null) return;

            if (Top.Index != _lastIndex)
            {
                _lastIndex = Top.Index;
                _movedAt = Game.GameTime;
            }

            var padX = Hud.ToX(TilePad);

            var x = left + padX;
            var rowW = w - padX * 2f;
            var y = top + TilePad;

            for (var i = 0; i < page.Items.Count; i++)
            {
                if (y + ListRowH > top + h) break;

                var age = Game.GameTime - _openedAt - i * TileStaggerMs;

                var lands = age <= 0 ? 0f
                          : age >= RiseMs ? 1f
                          : 1f - (float)Math.Pow(1f - age / (float)RiseMs, 3);

                Line(page.Items[i], x, y, rowW, ListRowH, i == Top.Index,
                     (int)(fade * lands), lands);

                y += ListRowH + ListRowGap;
            }
        }

        private void Line(WheelItem item, float x, float y, float w, float h, bool here,
                          int fade, float lands)
        {
            var on = here && item.Enabled;

            // Slid in from the left as it lands, which is the direction a list reads.
            x += (1f - lands) * Hud.ToX(0.018f);

            // The bed, not the ring. See Tile.
            var back = !item.Enabled ? Palette.SegmentDisabled
                     : on ? Lit
                     : Palette.Segment;

            Hud.RectFrom(x + Hud.ToX(TileShadow), y + TileShadow, w, h,
                         Fade(Color.FromArgb(150, 0, 0, 0), fade));

            Hud.RectFrom(x, y, w, h, Fade(back, fade));

            // A bar down the left of the live one. On a grid the whole tile changes colour and
            // that is enough; in a column of identical bars the eye wants an edge to run down.
            if (on)
            {
                Hud.RectFrom(x, y, Hud.ToX(0.0030f), h, Fade(Green, fade));
                Edge(x, y, w, h, TileEdge, Fade(LitEdge, fade));

                _glide.Target(x, y, w, h);
            }

            var ink = !item.Enabled ? Palette.TextDisabled : Palette.Text;

            if (item.Tint.HasValue && !on && item.Enabled) ink = item.Tint.Value;

            var textX = x + Hud.ToX(0.012f);

            // The art on the left, at a size that leaves the words the row.
            if (!string.IsNullOrEmpty(item.IconFile) &&
                Hud.File(item.IconFile, textX + Hud.ToX(ListArt) * 0.5f, y + h * 0.5f - 0.0005f,
                         ListArt, 0f, Fade(ink, fade)))
            {
                textX += Hud.ToX(ListArt) + 0.010f;
            }

            Hud.Text(item.Label, textX, y + 0.0085f, 0.34f, Fade(ink, fade),
                     Hud.FontChaletLondon, centre: false);

            // What it is, under the name and quieter. This is the half a tile could never
            // show -- there is no room under a caption for a second line.
            if (!string.IsNullOrEmpty(item.Detail))
            {
                Hud.Text(Hud.Fit(item.Detail, w - (textX - x) - Hud.ToX(0.055f), 0.24f,
                                 Hud.FontLabel),
                         textX, y + 0.0300f, 0.24f,
                         Fade(Palette.Alpha(Palette.TextDim, 190), fade),
                         Hud.FontLabel, centre: false);
            }

            if (!string.IsNullOrEmpty(item.Value))
            {
                Hud.TextRight(item.Value, x + w - Hud.ToX(0.010f), y + 0.0150f, 0.26f,
                              Fade(Palette.TextDim, fade), Hud.FontLabel);
            }
        }

        private void Grid(float left, float top, float w, float h, int fade)
        {
            var page = Current;
            if (page == null) return;

            // Some pages are read rather than recognised. See WheelPage.AsList.
            if (page.AsList)
            {
                Rows(left, top, w, h, fade);
                return;
            }

            Wallpaper(left, top, w, h, fade);

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

            // THE CARD SITS AT THE FOOT OF THE SCREEN, across all three columns.
            //
            // Under the apps rather than over them, which is where a widget goes on a phone --
            // the apps are what you came to press and a card above them pushes the thing you
            // are reaching for down the glass. It is also the shape of every banking widget
            // anybody has actually seen: a strip along the bottom you glance at on the way
            // past, not a banner you have to get past first.
            var cardTop = top + h - TileH - TilePad;

            Wallet(x0, cardTop, w - padX * 2f, TileH, fade);

            for (var i = 0; i < page.Items.Count; i++)
            {
                var col = i % Columns;
                var row = i / Columns;

                var tx = x0 + col * (tileW + padX);
                var ty = y + row * (TileH + TilePad);

                // Stops at the card rather than at the bottom of the body.
                if (ty + TileH > cardTop) break;

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

        /// <summary>
        /// The bank card: what you have, what you have ever had, and what just happened.
        ///
        /// WHY IT IS ON THE HOME SCREEN AT ALL. Every other number this mod keeps has somewhere
        /// to live -- weight is in the pocket, standing is on the gang page, the block is on the
        /// map. Money had nowhere: it is the game's own stat, so it appears in the pause menu
        /// and in a green flash by the minimap for four seconds after a sale, and at no other
        /// time. The one figure the whole mod is about was the one figure you could not look up.
        ///
        /// THE BALANCE ROLLS RATHER THAN CUTS. A number that changes between two frames is a
        /// number you did not see change; one that runs up to its new value over a third of a
        /// second is the thing a banking app does, and it is the difference between reading a
        /// figure and watching money arrive. Eased towards, not stepped, so a big sale takes
        /// visibly longer to count than a small one.
        ///
        /// AND THE LAST MOVEMENT SITS BESIDE IT, in the green or the red, for as long as it is
        /// worth reading. Cash already knows what it last did -- see Cash.LastMove -- so this
        /// is a statement line rather than a second ledger.
        /// </summary>
        private void Wallet(float x, float y, float w, float h, int fade)
        {
            int money;

            try { money = Game.Player.Money; }
            catch { return; }

            // First look of the session lands on the real figure rather than counting up from
            // nothing, which would be a slot machine every time you open the phone.
            if (!_walletSeen)
            {
                _walletSeen = true;
                _shown = money;
            }
            else
            {
                _shown += (money - _shown) * RollRate;

                if (Math.Abs(money - _shown) < 1f) _shown = money;
            }

            var pad = Hud.ToX(0.010f);

            // A plate a shade lighter than the screen, so it reads as something ON the home
            // screen rather than a hole in it -- the apps have no plate at all, which is what
            // makes this a widget rather than a fourth row of them.
            Hud.RectFrom(x, y, w, h, Color.FromArgb((int)(fade * 0.14f), 255, 255, 255));

            // ---- the bank's own colour, not the set's ----
            //
            // This was the phone's green, which made the card look like a fourth thing the gang
            // had built. It is not: it is somebody else's app running on his phone, and the one
            // cheap signal for that is that it is not in the house colours. A blue band across
            // the top and a blue mark is the whole of the branding, and it is enough -- every
            // banking widget anybody has seen is a coloured band with a number under it.
            Hud.RectFrom(x, y, w, BankBand, Fade(Fleeca, fade));

            var ix = x + pad;

            if (Hud.File("bank.png", ix + Hud.ToX(BankMark) * 0.5f, y + 0.0125f, BankMark, 0f,
                         Fade(Palette.Text, fade)))
            {
                ix += Hud.ToX(BankMark) + 0.005f;
            }

            Hud.Text("FLEECA", ix, y + 0.006f, 0.27f, Fade(Palette.Text, fade),
                     Hud.FontLabel, centre: false);

            // ---- the card, and the four digits every bank app shows you ----
            //
            // A fixed number rather than a made-up random one. It is set dressing, and set
            // dressing that changes every time you open the phone is the one kind anybody
            // notices.
            var nx = x + w - pad;

            Hud.TextRight(Account, nx, y + 0.006f, 0.25f,
                          Fade(Palette.Text, (int)(fade * 0.82f)), Hud.FontLabel);

            nx -= Hud.MeasureText(Account, 0.25f, Hud.FontLabel) + 0.005f;

            Hud.File("card.png", nx - Hud.ToX(BankMark) * 0.5f, y + 0.0125f, BankMark, 0f,
                     Fade(Palette.Text, (int)(fade * 0.82f)));

            // ---- the figure ----
            Hud.Text("$" + ((long)_shown).ToString("N0"), x + pad, y + 0.032f, 0.62f,
                     Fade(Palette.Text, fade), Hud.FontLabel, centre: false);

            // ---- the last one in, beside the balance ----
            //
            // A STATEMENT LINE, WHICH IS WHY IT DOES NOT EXPIRE. This used to be Cash.LastMove
            // on a twenty second clock -- any money at all, in or out, briefly. Wrong readout
            // for a bank card twice over: a card that has gone blank tells you nothing when you
            // open the phone twenty minutes after a sale, and "any money at all" includes
            // buying a jumper, which is not a deposit.
            //
            // This is the corner money and only the corner money. It says TRANSFER because
            // that is what an amount arriving from somebody else's account is called, and it
            // stands until the next one replaces it.
            var last = LastDeposit == null ? 0L : LastDeposit();

            if (last > 0L)
            {
                var chip = "+$" + last.ToString("N0");

                Hud.TextRight(chip, x + w - pad, y + 0.042f, 0.26f,
                              Fade(Palette.Cash, fade), Hud.FontLabel);

                Hud.TextRight("TRANSFER",
                              x + w - pad - Hud.MeasureText(chip, 0.26f, Hud.FontLabel) - 0.006f,
                              y + 0.0425f, 0.22f,
                              Fade(Palette.TextDim, (int)(fade * 0.85f)), Hud.FontLabel);
            }

            // ---- and everything the corner has ever paid, under a hairline ----
            var ruleY = y + h - 0.024f;

            Hud.RectFrom(x + pad, ruleY, w - pad * 2f, 0.0010f,
                         Color.FromArgb((int)(fade * 0.20f), 255, 255, 255));

            var take = Earned == null ? 0L : Earned();

            var tx = x + pad;

            if (Hud.File("money.png", tx + Hud.ToX(0.010f) * 0.5f, ruleY + 0.0115f, 0.010f, 0f,
                         Fade(Palette.Cash, (int)(fade * 0.9f))))
            {
                tx += Hud.ToX(0.010f) + 0.004f;
            }

            // DEPOSITS, not "all time". The figure never changed -- it has always been
            // TotalEarned, which has exactly one caller and that caller is a corner sale --
            // but "ALL TIME" beside a balance reads as a lifetime of everything, which
            // includes heists and fares and whatever the game handed you. It is the takings.
            Hud.Text("DEPOSITS", tx, ruleY + 0.006f, 0.23f,
                     Fade(Palette.TextDim, fade), Hud.FontLabel, centre: false);

            Hud.TextRight("$" + take.ToString("N0"), x + w - pad, ruleY + 0.006f, 0.23f,
                          Fade(Palette.TextDim, fade), Hud.FontLabel);
        }

        /// <summary>The bank's blue, its band, and the size of the two marks on the card.</summary>
        private static readonly Color Fleeca = Color.FromArgb(255, 41, 128, 185);

        private const float BankBand = 0.0026f;
        private const float BankMark = 0.013f;

        /// <summary>The four digits. Fixed, because set dressing that moves is set dressing you notice.</summary>
        private const string Account = "**** 4471";

        /// <summary>The rolling figure, and whether it has ever been set.</summary>
        private float _shown;
        private bool _walletSeen;

        /// <summary>How much of the gap the figure closes each frame. See Wallet.</summary>
        private const float RollRate = 0.12f;

        /// <summary>How long the last movement stays beside the balance, and its fade.</summary>
        private const int StatementMs = 20000;
        private const int StatementFadeMs = 2500;

        /// <summary>
        /// How far into the press animation the live tile is, nought to one and back.
        ///
        /// Only ever non-zero for the tile the cursor is on, because that is the only one that
        /// can have been pressed -- the cursor cannot move while the press is playing.
        /// </summary>
        private float Punch()
        {
            if (_pressAt == 0) return 0f;

            var since = Game.GameTime - _pressAt;

            if (since < 0 || since >= PressMs) return 0f;

            return (float)Math.Sin(since / (double)PressMs * Math.PI);
        }

        /// <summary>Somewhere between two colours, for an icon on its way to being chosen.</summary>
        private static Color Blend(Color a, Color b, float t)
        {
            if (t <= 0f) return a;
            if (t >= 1f) return b;

            return Color.FromArgb(
                a.A + (int)((b.A - a.A) * t),
                a.R + (int)((b.R - a.R) * t),
                a.G + (int)((b.G - a.G) * t),
                a.B + (int)((b.B - a.B) * t));
        }

        /// <summary>How long the press animation runs before the page actually changes.</summary>
        public const int PressMs = 150;

        /// <summary>How far the icon dips at the bottom of the press, as a fraction.</summary>
        private const float PunchDip = 0.26f;

        /// <summary>When the live tile was pressed, or nought when nothing is playing.</summary>
        private int _pressAt;

        /// <summary>
        /// Somebody pressed the app the cursor is on.
        ///
        /// Says whether it took, so the caller knows to wait rather than opening the page on
        /// the same frame. Only on the home grid: a list row has no icon to punch, and putting
        /// a tenth of a second in front of every row of every submenu would be latency bought
        /// for nothing.
        /// </summary>
        public bool Press()
        {
            if (!AtHome || InCall) return false;

            var item = Selected;

            if (item == null || !item.Enabled) return false;

            _pressAt = Game.GameTime;

            return true;
        }

        /// <summary>The press is spent. Called when it is acted on, and when the phone shuts.</summary>
        public void ClearPress()
        {
            _pressAt = 0;
        }

        private void Tile(WheelItem item, float x, float y, float w, float h, bool here,
                          int fade)
        {
            var on = here && item.Enabled;

            // THE BED, NOT THE RING. Filling the live tile with LitEdge and then drawing
            // its border in LitEdge painted the border in the colour of the thing directly
            // underneath it -- which is not a faint ring, it is no ring at all, and what
            // was left was a flat bright green block with white text on it. That is the
            // inverted-panel highlight the note on these two colours rejects by name.
            //
            // Lit is the dark green bed and LitEdge is the light that goes round it. Both
            // were declared for exactly this and neither renderer was using the first one.
            // NO BOX, NO SHADOW, NO BORDER. Just the icon on the black.
            //
            // It looks better and the box was never doing the work anyway. A quiet tile is
            // near-black at alpha 200 on a near-black body, so nineteen of them were nineteen
            // rectangles nobody could see -- every bit of the reading came from the icon, the
            // name, and which one was lit.
            //
            // Everything that existed to dress the box goes with it: the shadow that lifted it
            // off the body, the hairline border, the light that ran round the live one, and
            // the sheen across it. So does the whole corner-rounding argument, which cost more
            // rectangles than the rest of the screen put together and was still coming out
            // with square bites in it. What is left is one sprite and one word per app.
            //
            // The highlight moves onto the icon itself: green, swaying, and a punch when it is
            // chosen. See below.

            // The grow stays, and now it moves the icon rather than swelling a box. It is what
            // the eye follows when the cursor jumps two tiles across the grid -- a highlight
            // that simply appears somewhere else leaves you re-finding it.
            var pop = 0f;

            if (on)
            {
                var age = Game.GameTime - _movedAt;
                var t = age >= PopMs ? 1f : Math.Max(0f, age / (float)PopMs);

                var e = 1f - (float)Math.Pow(1f - t, 3);
                pop = PopBy * (0.55f + 0.45f * e);
            }

            var popX = Hud.ToX(pop);

            x -= popX;
            y -= pop;
            w += popX * 2f;
            h += pop * 2f;

            // The frame glides onto the live app -- see Glide -- so a cursor that jumps two
            // columns is followed rather than re-found.
            if (on) _glide.Target(x + Hud.ToX(0.004f), y + 0.004f, w - Hud.ToX(0.008f), h - 0.008f);

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

                var kick = since < JiggleKickMs ? 1f + (1f - since / (float)JiggleKickMs) : 1f;

                var a = (float)Math.Sin(since / JiggleMs * Math.PI * 2.0);
                var b = (float)Math.Sin(since / JiggleOffMs * Math.PI * 2.0);

                sway = (a + b * 0.35f) * JiggleDegrees * kick;
            }

            // GREEN ON THE ONE YOU ARE ON. With the box gone this is what says which app the
            // cursor is holding, so it is the icon that changes colour and not the name -- the
            // name is the thing you are trying to read, and green text on black is harder work
            // than white.
            var art = on ? Green : ink;

            // AND A PUNCH WHEN IT IS CHOSEN, which is a different event from being hovered and
            // wants a different animation. The sway says "this is the one you are on"; the
            // punch says "and you have just picked it". Before this, the only acknowledgement
            // of a press was the page changing -- which is the result, not the answer.
            //
            // DOWN AND BACK rather than out and back. The icon dips the way a key does going
            // down, and the page arrives before it has finished returning. Something growing
            // at you reads as an alert; something pressing in reads as a button.
            var punch = Punch();

            if (punch > 0f)
            {
                art = Blend(art, Color.FromArgb(255, 240, 255, 240), punch);

                // THE RIPPLE. A ring leaving the pressed app and fading as it grows, the way a
                // touch does on glass. The dip says the button went down; this says where.
                var spread = Math.Max(0f, Math.Min(1f, (Game.GameTime - _pressAt) / (float)PressMs));
                var grow = 0.014f * spread;

                Theme.Rim(x - Hud.ToX(grow), y - grow, w + Hud.ToX(grow) * 2f, h + grow * 2f, 0.0016f,
                         Fade(Color.FromArgb((int)(170 * (1f - spread)), 108, 196, 106), fade));
            }

            Art(item, x + w * 0.5f, y + h * 0.31f, 0.042f * (1f - PunchDip * punch),
                Fade(art, fade), sway);

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

                // A RAIL RATHER THAN A HAIRLINE. It was 0.0035 of a screen HEIGHT converted to
                // width, which comes out under four pixels on a 1080p screen -- a line you have
                // to already know is there to find. Three times that is an edge the eye lands
                // on without being told to look for it.
                Hud.RectFrom(left, top, Hud.ToX(0.010f), RowH, Fade(LitEdge, fade));

                _glide.Target(left, top, w, RowH);
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
                // GREEN ON THE ONE YOU ARE ON, the same as the app grid. Three cues rather than
                // one, because any single one of them can be lost against a given background:
                // the bed, the rail down the edge, and the icon changing colour. The label
                // stays white, for the same reason it does on the grid -- it is the thing you
                // are reading.
                Art(item, x + Hud.ToX(gutter) * 0.5f, top + RowH * 0.5f, gutter,
                    Fade(on ? Green : ink, fade));
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

            Hud.RectFrom(trackX, top + span * at, trackW, barH, Fade(Green, fade));
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
        /// <summary>A hairline border, as four rectangles.</summary>
        private static void Edge(float x, float y, float w, float h, float t, Color c)
        {
            var tx = Hud.ToX(t);

            Hud.RectFrom(x, y, w, t, c);
            Hud.RectFrom(x, y + h - t, w, t, c);
            Hud.RectFrom(x, y + t, tx, h - t * 2f, c);
            Hud.RectFrom(x + w - tx, y + t, tx, h - t * 2f, c);
        }


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
