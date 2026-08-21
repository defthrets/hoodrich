using System;
using System.Collections.Generic;
using System.Drawing;
using Control = GTA.Control;
using GTA;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Social;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.UI
{
    /// <summary>
    /// The feed.
    ///
    /// Built to be read rather than operated, which is a different job from every other screen
    /// in the mod. There is no value column and nothing lines up on the right, because a
    /// timeline is a column of paragraphs and forcing it into a label/value grid is what makes
    /// a feed look like a spreadsheet with avatars.
    ///
    /// Three faces do the separating: the display name in the standard HUD face at full weight,
    /// the handle and the timestamp in the condensed face at low contrast, and the body in the
    /// reading face. That is the whole visual system, and it is enough -- the thing that makes a
    /// real timeline legible is not decoration, it is that the eye can find the name, skip the
    /// handle, and land on the words.
    /// </summary>
    internal sealed class SocialScreen
    {
        private const float PanelWidth = 0.360f;
        private const float PanelTop = 0.070f;
        private const float PanelHeight = 0.860f;

        private const float Pad = 0.014f;
        private const float AvatarSize = 0.038f;
        private const float BodyScale = 0.315f;
        private const float LineHeight = 0.0248f;

        /// <summary>Gap under a post, before the next one's rule.</summary>
        private const float PostGap = 0.012f;

        private const int OpenGraceMs = 220;

        /// <summary>The fixed identity card. Everything under it scrolls; this does not.</summary>
        private const float CardHeight = 0.084f;

        /// <summary>Your own face. Larger than a stranger's, and the best-rendered thing here.</summary>
        private const float HeadSize = 0.046f;

        /// <summary>Name/handle/stamp row above the body. DrawPost and PostHeight share it.</summary>
        private const float MetaHeight = 0.023f;

        /// <summary>Gap between the last body line and the engagement row.</summary>
        private const float MetricGap = 0.004f;

        /// <summary>
        /// The engagement row. NOT grown for the icons, and that is deliberate -- the row went
        /// from a 0.0143 text cap to a 0.016 icon and still fits inside the clearance that was
        /// already there. Growing it would cost 0.004 on every post, which is roughly one fewer
        /// post per screenful, to buy room nobody needed.
        /// </summary>
        private const float MetricsHeight = 0.022f;

        /// <summary>Engagement art. The one size in this mod proven to render these files.</summary>
        private const float MetricIcon = 0.016f;

        /// <summary>
        /// Fixed pitch for the three engagement figures.
        ///
        /// Columns, not a flowed run of words. As words the three groups slid left and right by
        /// however many digits each post happened to have, so nothing lined up down the page and
        /// the eye had to re-find them on every single post.
        /// </summary>
        private const float MetricPitch = 0.052f;

        private static readonly Color MetricArt = Color.FromArgb(120, 150, 158, 152);
        private static readonly Color MetricNum = Color.FromArgb(170, 158, 164, 160);

        /// <summary>
        /// ALL, and the ones about you.
        ///
        /// Two, not three. The only other partition the data supports is the author's gang, and
        /// it does not work -- ordinary ambient posts draw from all 142 authors with no gang
        /// filter, so a "SETS" tab would quietly collect a Families civilian selling a sofa. It
        /// would fill, and it would look like it worked, which is worse than being empty.
        /// AboutYou is authored per post, is already drawn, and is the one question anybody
        /// opens this screen twice to ask.
        /// </summary>
        private static readonly string[] TabNames = { "ALL", "ABOUT YOU" };

        private int _tab;

        /// <summary>
        /// How many posts each tab can see.
        ///
        /// Counts, deliberately, and NOT a filtered list. With a list, Draw and Update can end
        /// up indexing two different copies a frame apart; with counts the worst case is the
        /// scroll being clamped one post out, which corrects itself on the next frame.
        /// </summary>
        private readonly int[] _tally = new int[2];

        /// <summary>How many posts the last Draw actually fitted, so the last one lands flush.</summary>
        private int _lastShown;

        private readonly SocialFeed _feed;

        private readonly Dictionary<string, List<string>> _wrapped =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);

        private int _scroll;
        private int _openedAt;

        /// <summary>
        /// How many posts existed when the reader last touched the scroll.
        ///
        /// New posts go on at the TOP, so on a feed that writes itself every few seconds a
        /// reader who has scrolled down watches the thing they were reading slide away from
        /// them. The scroll index is nudged by however many arrived, so the post under your eye
        /// stays under your eye -- except at the very top, where staying pinned to the newest
        /// post is exactly what you want.
        /// </summary>
        private int _seenCount;

        /// <summary>Franklin's actual head, borrowed from the game's own contact-photo system.</summary>
        private int _mugshot;
        private string _mugshotTxd = "";

        public SocialScreen(SocialFeed feed)
        {
            _feed = feed;
        }

        public bool IsOpen { get; private set; }

        public void Open()
        {
            IsOpen = true;

            Count();

            _tab = 0;
            _scroll = 0;
            _lastShown = 0;
            _seenCount = _tally[0];
            _openedAt = Game.GameTime;

            RequestMugshot();
            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        public void Close()
        {
            IsOpen = false;
            ReleaseMugshot();
        }

        // ---- the profile picture -----------------------------------------------

        /// <summary>
        /// Asks the game for a headshot of the player.
        ///
        /// This is the same machinery the phone uses for contact photos, so it is a real render
        /// of the actual character -- his face, his haircut, whatever he is wearing right now.
        /// A drawn stand-in would have been easier and would have looked like a stand-in.
        /// </summary>
        private void RequestMugshot()
        {
            if (_mugshot != 0) return;

            try
            {
                var player = Game.Player.Character;
                if (player == null || !player.Exists()) return;

                _mugshot = Function.Call<int>(Hash.REGISTER_PEDHEADSHOT, player.Handle);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not ask for a headshot: " + ex.Message);
            }
        }

        private void ReleaseMugshot()
        {
            try
            {
                if (_mugshot != 0) Function.Call(Hash.UNREGISTER_PEDHEADSHOT, _mugshot);
            }
            catch { /* teardown */ }

            _mugshot = 0;
            _mugshotTxd = "";
        }

        private bool MugshotReady()
        {
            if (_mugshot == 0) return false;
            if (!string.IsNullOrEmpty(_mugshotTxd)) return true;

            try
            {
                if (!Function.Call<bool>(Hash.IS_PEDHEADSHOT_READY, _mugshot)) return false;
                if (!Function.Call<bool>(Hash.IS_PEDHEADSHOT_VALID, _mugshot)) return false;

                _mugshotTxd = Function.Call<string>(Hash.GET_PEDHEADSHOT_TXD_STRING, _mugshot);
                return !string.IsNullOrEmpty(_mugshotTxd);
            }
            catch
            {
                return false;
            }
        }

        // ---- input -------------------------------------------------------------

        public void Update()
        {
            if (!IsOpen) return;

            Count();
            HoldPosition();
            LockControls();

            if (Game.GameTime - _openedAt < OpenGraceMs) return;

            if (Pressed(Control.PhoneUp)) Scroll(-1);
            else if (Pressed(Control.PhoneDown)) Scroll(1);
            else if (Pressed(Control.PhoneLeft)) Tab(-1);
            else if (Pressed(Control.PhoneRight)) Tab(1);
            else if (Pressed(Control.PhoneCancel) || Pressed(Control.PhoneSelect))
            {
                Hud.PlaySound("BACK", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                Close();
            }
        }

        /// <summary>Whether a post belongs to a tab.</summary>
        ///
        /// <remarks>
        /// The null-author check lives here rather than in DrawPost, so the counts and the draw
        /// loop agree about it. DrawPost reaches into By.Tint, By.Initial, By.Name and
        /// By.Verified without a guard, so one authored post with a missing author would have
        /// taken the whole screen down.
        /// </remarks>
        private static bool Shows(int tab, Post post)
        {
            if (post == null || post.By == null) return false;

            return tab == 0 || post.AboutYou;
        }

        /// <summary>How many posts each tab can see. One walk, no allocation, no natives.</summary>
        private void Count()
        {
            _tally[0] = 0;
            _tally[1] = 0;

            var line = _feed.Timeline;

            for (var i = 0; i < line.Count; i++)
            {
                if (Shows(0, line[i])) _tally[0]++;
                if (Shows(1, line[i])) _tally[1]++;
            }
        }

        private void Tab(int step)
        {
            var next = (_tab + step) % TabNames.Length;
            if (next < 0) next += TabNames.Length;
            if (next == _tab) return;

            _tab = next;

            // Required, not tidiness. Index 14 of ALL is not index 14 of ABOUT YOU, so carrying
            // the scroll across lands you somewhere arbitrary -- or, at 30 with six matching
            // posts, on a blank panel.
            _scroll = 0;
            _lastShown = 0;
            _seenCount = _tally[_tab];

            Hud.PlaySound("NAV_LEFT_RIGHT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        /// <summary>
        /// The furthest you can scroll: far enough that the LAST post sits at the bottom.
        ///
        /// Off what the previous frame actually fitted, so it drifts by a post as the bodies
        /// under you change length. That is the honest answer for a list whose rows are not the
        /// same height, and it self-corrects on the next frame.
        /// </summary>
        private int MaxScroll()
        {
            var count = _tally[_tab];
            if (count <= 0) return 0;

            return _lastShown > 0 ? Math.Max(0, count - _lastShown) : Math.Max(0, count - 1);
        }

        /// <summary>Keeps the reader looking at the same post when new ones arrive above it.</summary>
        private void HoldPosition()
        {
            var count = _tally[_tab];
            var arrived = count - _seenCount;

            _seenCount = count;

            // At the top you want the newest; anywhere else you want to keep your place.
            if (arrived <= 0 || _scroll == 0) return;

            _scroll = Math.Min(MaxScroll(), _scroll + arrived);
        }

        private void Scroll(int step)
        {
            var count = _tally[_tab];
            if (count == 0) return;

            _scroll = Math.Max(0, Math.Min(MaxScroll(), _scroll + step));
            _seenCount = count;

            Hud.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        private static bool Pressed(Control control)
        {
            return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)control);
        }

        private static void LockControls()
        {
            Game.DisableControlThisFrame(Control.Attack);
            Game.DisableControlThisFrame(Control.Attack2);
            Game.DisableControlThisFrame(Control.Aim);
            Game.DisableControlThisFrame(Control.Jump);
            Game.DisableControlThisFrame(Control.Enter);
            Game.DisableControlThisFrame(Control.Phone);
            Game.DisableControlThisFrame(Control.SelectWeapon);
            Game.DisableControlThisFrame(Control.PhoneUp);
            Game.DisableControlThisFrame(Control.PhoneDown);
            Game.DisableControlThisFrame(Control.PhoneLeft);
            Game.DisableControlThisFrame(Control.PhoneRight);
            Game.DisableControlThisFrame(Control.PhoneSelect);
            Game.DisableControlThisFrame(Control.PhoneCancel);
        }

        // ---- drawing -----------------------------------------------------------

        public void Draw()
        {
            if (!IsOpen) return;

            var left = 0.5f - PanelWidth * 0.5f;
            var x = left + Pad;
            var right = left + PanelWidth - Pad;

            // The hub's ground and the hub's bar across the top.
            //
            // The soft grey halo that used to sit outside the panel is gone. Two framing
            // systems on one screen -- a glow out here and an accent rule in the header -- was
            // one more than the rest of the mod uses, and the rule is the one that matches.
            Hud.RectFrom(left, PanelTop, PanelWidth, PanelHeight, Color.FromArgb(238, 12, 13, 15));
            Hud.RectFrom(left, PanelTop, PanelWidth, 0.0028f, Palette.Accent);

            Count();

            var y = DrawHeader(left, right);
            y = Tabs(x, right, y + 0.010f);

            var feedTop = y;
            var bottom = PanelTop + PanelHeight - 0.030f;
            var count = _tally[_tab];

            var shown = 0;
            var index = 0;

            for (var i = 0; i < _feed.Timeline.Count; i++)
            {
                var post = _feed.Timeline[i];

                if (!Shows(_tab, post)) continue;

                // Counts MATCHING posts, which is why it is not the loop variable -- on the
                // second tab the two run at completely different rates.
                if (index++ < _scroll) continue;

                var height = PostHeight(post);
                if (y + height > bottom) break;

                DrawPost(left, y, post);
                y += height;
                shown++;
            }

            _lastShown = shown;

            if (count == 0) Nothing(feedTop);
            else Rail(left, feedTop, bottom, count, shown);

            Keys(x, right);
        }

        /// <summary>
        /// The tab strip, which is Grimes's shelf strip with the array swapped.
        ///
        /// The count is right-aligned to a FIXED edge rather than folded into a label. Labels
        /// are measured and laid out one after another, so "ALL 79" going to "ALL 80" would
        /// shove ABOUT YOU sideways every ten seconds -- fine in a still, twitching all evening
        /// in motion.
        /// </summary>
        private float Tabs(float x, float right, float y)
        {
            var cx = x;

            for (var i = 0; i < TabNames.Length; i++)
            {
                var here = i == _tab;
                var empty = _tally[i] == 0;

                var width = 0.02f;
                try { width = Hud.MeasureText(TabNames[i], 0.26f, Hud.FontLabel); }
                catch { /* the estimate will do */ }

                if (here)
                {
                    Hud.RectFrom(cx - 0.004f, y - 0.004f, width + 0.008f, 0.024f,
                                 Color.FromArgb(46, 255, 255, 255));

                    Hud.RectFrom(cx - 0.004f, y + 0.019f, width + 0.008f, 0.0022f, Palette.Accent);
                }

                // An empty tab says so before you press it. NOT Palette.TextDisabled: that is
                // full alpha and composites BRIGHTER than TextDim, so it would have made the
                // empty tab the loudest thing on the strip.
                var ink = here ? Palette.Text
                    : empty ? Palette.Alpha(Palette.TextDim, 90) : Palette.TextDim;

                Hud.Text(TabNames[i], cx, y, 0.26f, ink, Hud.FontLabel, centre: false);

                cx += width + 0.022f;
            }

            var n = _tally[_tab];

            Hud.TextRight(n + (n == 1 ? " POST" : " POSTS"), right, y + 0.0015f, 0.24f,
                          Palette.TextDim, Hud.FontLabel);

            y += 0.032f;
            Hud.RectFrom(x, y, PanelWidth - Pad * 2f, 0.0022f, Palette.Accent);
            return y + 0.012f;
        }

        /// <summary>
        /// Where you are in the feed, in the margin.
        ///
        /// Grimes has no equivalent because Grimes shows everything at once, so this is
        /// invented -- but invented in the hub's own vocabulary: the hub says "you are here in
        /// a list" with a thin accent bar on a row's left edge, and this is the same sentence
        /// on the other axis.
        /// </summary>
        private void Rail(float left, float top, float bottom, int count, int shown)
        {
            // Nothing to scroll, so nothing drawn. A full-length thumb that never moves is
            // furniture, and on a three-post tab it is a lie about there being more.
            if (shown <= 0 || count <= shown) return;

            var rx = left + PanelWidth - 0.0062f;
            var h = bottom - top;

            Hud.RectFrom(rx, top, 0.0022f, h, Color.FromArgb(30, 200, 205, 200));

            // A floor, so an eighty-post feed still gets a thumb rather than a tick mark.
            var thumbH = Math.Max(h * 0.06f, h * (shown / (float)count));

            // count - shown, not count: it is what makes the thumb land flush at the bottom
            // exactly when the last post is on screen, which is the one thing a scroll
            // indicator has to be able to say.
            var pos = _scroll / (float)(count - shown);
            if (pos < 0f) pos = 0f;
            if (pos > 1f) pos = 1f;

            Hud.RectFrom(rx, top + (h - thumbH) * pos, 0.0022f, thumbH,
                         Palette.Alpha(Palette.Accent, 160));
        }

        /// <summary>
        /// An empty tab, saying which kind of empty it is.
        ///
        /// The only place on this panel where the left alignment is deliberately broken. An
        /// empty panel has no column to align to, and a dim sentence hanging off the left edge
        /// of a screen-tall void looks like something failed to draw.
        /// </summary>
        private void Nothing(float top)
        {
            var head = _tab == 0 ? "NOTHING YET" : "NOBODY'S SAID YOUR NAME";
            var sub = _tab == 0 ? "Go and do something." : "Give them something to talk about.";

            var ey = top + 0.150f;

            // A ghost bubble: not a picture, a centre for the void, so the panel reads as empty
            // on purpose rather than as broken. No new art -- it is reply.png at low alpha.
            Hud.File("reply.png", 0.5f, ey, 0.044f, 0f, Color.FromArgb(34, 200, 205, 200));

            Hud.Text(head, 0.5f, ey + 0.036f, 0.26f, Palette.TextDim, Hud.FontLabel);
            Hud.Text(sub, 0.5f, ey + 0.060f, 0.28f, Palette.Alpha(Palette.TextDim, 140), Hud.FontBody);
        }

        private void Keys(float x, float right)
        {
            var y = PanelTop + PanelHeight - 0.019f;

            Hud.RectFrom(x, PanelTop + PanelHeight - 0.026f, PanelWidth - Pad * 2f, 0.0012f,
                         Color.FromArgb(60, 200, 205, 200));

            Hud.Text("UP/DOWN  SCROLL      LEFT/RIGHT  FILTER      ENTER  CLOSE",
                     x, y, 0.24f, Palette.TextDim, Hud.FontLabel, centre: false);

            // The other half of the scroll indicator, and nearly free. HoldPosition bumps the
            // scroll when new posts land so the one you are reading stays put, which means the
            // reader silently accumulates unread posts above them with nothing saying so.
            // _scroll IS that number.
            if (_scroll > 0)
            {
                Hud.TextRight(_scroll + " ABOVE", right, y, 0.24f, Palette.TextDim, Hud.FontLabel);
            }
        }

        /// <summary>
        /// Your own card at the top: face, name, and the number that moves.
        ///
        /// The face sits BESIDE the title rather than under it, which is the whole rearrangement.
        /// A 0.74 cursive line with flourishes stacked directly above content in the same column
        /// is a collision waiting on one bad estimate; putting the face alongside removes the
        /// possibility instead of budgeting for it. That also drops the card from 0.108 to
        /// 0.084, which is where the tab strip's height comes from.
        ///
        /// And the text column here is now EXACTLY the post text column. It used to sit a few
        /// thousandths to the right of it, which read as a second ragged left edge down a narrow
        /// panel.
        /// </summary>
        private float DrawHeader(float left, float right)
        {
            Hud.RectFrom(left, PanelTop, PanelWidth, CardHeight, Color.FromArgb(240, 18, 20, 22));

            // Re-asserted, so draw order cannot eat it.
            Hud.RectFrom(left, PanelTop, PanelWidth, 0.0028f, Palette.Accent);

            var headX = left + Pad + Hud.ToX(AvatarSize) + 0.010f;

            Hud.Text("SOCIALS", headX, PanelTop + 0.009f, 0.74f, Palette.Text,
                     Hud.FontCursive, centre: false);

            // The one number, right, in the money colour -- the hub's exact title rhythm.
            Hud.TextRight(_feed.Followers.ToString("N0"), right, PanelTop + 0.023f, 0.34f,
                          Palette.Cash, Hud.FontChaletLondon);

            var cx = left + Pad + Hud.ToX(HeadSize) * 0.5f;
            var cy = PanelTop + 0.042f;

            if (MugshotReady())
            {
                Hud.Sprite(_mugshotTxd, _mugshotTxd, cx, cy, Hud.ToX(HeadSize), HeadSize, 0f, Color.White);
            }
            else
            {
                // Until the render lands, a disc rather than a hole.
                Hud.Disc(cx, cy, HeadSize * 0.5f, Color.FromArgb(255, 58, 72, 60));

                // Your initial, not a hardcoded F. It stops being wrong the first time somebody
                // renames the account.
                var initial = string.IsNullOrEmpty(_feed.DisplayName)
                    ? "F"
                    : _feed.DisplayName.Substring(0, 1).ToUpperInvariant();

                Hud.Text(initial, cx, cy - 0.016f, 0.60f, Palette.Text, Hud.FontChaletLondon);
            }

            Hud.Text(_feed.DisplayName, headX, PanelTop + 0.057f, 0.34f, Palette.Text,
                     Hud.FontChaletLondon, centre: false);

            var nw = 0.06f;
            try { nw = Hud.MeasureText(_feed.DisplayName, 0.34f, Hud.FontChaletLondon); }
            catch { /* the estimate will do */ }

            Hud.Text(_feed.Handle, headX + nw + 0.006f, PanelTop + 0.060f, 0.26f,
                     Palette.TextDim, Hud.FontLabel, centre: false);

            Hud.TextRight("FOLLOWERS  ·  " + _feed.Following.ToString("N0") + " FOLLOWING",
                          right, PanelTop + 0.0595f, 0.24f, Palette.TextDim, Hud.FontLabel);

            // No underline here. The tab strip's rule closes the masthead, and two full-width
            // accent rules a few hundredths apart on a panel this narrow is a ladder.
            return PanelTop + CardHeight;
        }

        /// <summary>
        /// Draws the author's picture, and says whether it managed to.
        ///
        /// A dictionary that is not in this install streams forever and never arrives, so the
        /// caller needs a straight answer rather than a blank square -- false and it falls back
        /// to the letter. Names that never resolve are logged once each, so a guessed contact
        /// dictionary that does not exist tells us rather than quietly showing nothing.
        /// </summary>
        private bool Avatar(Post post, float cx, float cy)
        {
            var pic = post.By == null ? "" : post.By.Pic;
            if (string.IsNullOrEmpty(pic)) return false;

            if (!Hud.EnsureTextureDict(pic))
            {
                Grumble(pic);
                return false;
            }

            Hud.Sprite(pic, pic, cx, cy, Hud.ToX(AvatarSize), AvatarSize, 0f, Color.White);
            return true;
        }

        private static readonly HashSet<string> Moaned =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static void Grumble(string pic)
        {
            if (Moaned.Contains(pic)) return;

            Moaned.Add(pic);
            Log.Debug("Avatar '" + pic + "' would not load; falling back to the initial.");
        }

        private void DrawPost(float left, float top, Post post)
        {
            var lines = Lines(post);

            if (post == null || post.By == null) return;

            // Anything about you gets a change of ground as well as a rail.
            //
            // A hairline of near-white at the very edge of a narrow panel is genuinely hard to
            // catch while the list is sliding, and sliding is exactly when you need to catch it.
            // The wash is low -- a third of the hub's, because the hub marks one row for as
            // long as a cursor sits on it whereas this is permanent and can cover three posts
            // at once. It sits BEHIND the text and does not inset it, which matters: see the
            // note on the wrap cache in Lines().
            if (post.AboutYou)
            {
                var h = PostHeight(post) - PostGap;

                Hud.RectFrom(left + 0.002f, top, PanelWidth - 0.012f, h,
                             Color.FromArgb(28, 255, 255, 255));

                Hud.RectFrom(left + 0.002f, top, 0.0022f, h, Palette.Accent);
            }

            var cx = left + Pad + Hud.ToX(AvatarSize) * 0.5f;
            var cy = top + 0.004f + AvatarSize * 0.5f;

            // The author's own face, if they have one.
            //
            // The field has always been there and the toasts have always drawn it -- this
            // screen never looked at it, so the same author had a photograph on the right of
            // the screen and a coloured circle with a letter in it here. A logo is the whole
            // difference between a business account and a name.
            if (!Avatar(post, cx, cy))
            {
                Hud.Disc(cx, cy, AvatarSize * 0.5f, post.By.Tint);

                Hud.Text(post.By.Initial, cx, cy - 0.0135f, 0.46f,
                         Color.FromArgb(235, 250, 250, 248), Hud.FontChaletLondon);
            }

            var textX = left + Pad + Hud.ToX(AvatarSize) + 0.010f;
            var y = top + 0.002f;

            // Name, then handle and stamp trailing it in the quiet face. Measured so the handle
            // sits directly after the name whatever the name happens to be.
            Hud.Text(post.By.Name, textX, y, 0.34f, Palette.Text, Hud.FontChaletLondon, centre: false);

            var nameWidth = 0.06f;
            try { nameWidth = Hud.MeasureText(post.By.Name, 0.34f, Hud.FontChaletLondon); }
            catch { /* the estimate above will do */ }

            var tail = textX + nameWidth + 0.006f;

            if (post.By.Verified)
            {
                // A shape, not a dot. The mark was a small white disc immediately followed by
                // the "  ·  " in the handle string -- a dot, a gap, then another dot, which
                // reads as punctuation rather than as a badge.
                const float th = 0.014f;
                var icx = tail + Hud.ToX(th) * 0.5f;

                if (!Hud.File("tick.png", icx, y + 0.0095f, th, 0f, Palette.Accent))
                {
                    Hud.Disc(icx, y + 0.010f, 0.005f, Palette.Accent);
                }

                tail += Hud.ToX(th) + 0.004f;
            }

            Hud.Text(post.By.Handle + "  ·  " + SocialFeed.Ago(post.At), tail, y + 0.003f, 0.26f,
                     Palette.TextDim, Hud.FontLabel, centre: false);

            y += MetaHeight;

            foreach (var line in lines)
            {
                Hud.Text(line, textX, y, BodyScale, Palette.Text, Hud.FontBody, centre: false);
                y += LineHeight;
            }

            y += MetricGap;

            // Art and figures on three fixed columns.
            //
            // The words REPLIES / REPOSTS / LIKES are gone: forty-seven characters of shouting
            // per post, eight posts on screen, nearly four hundred capitals competing with the
            // bodies this screen exists to show. Largest single reduction in noise available
            // here.
            //
            // Non-short-circuit &, deliberately, so all three are attempted and the fallback is
            // all-or-nothing rather than one icon and two gaps.
            var ok = Metric("reply.png", post.Replies, textX, y)
                   & Metric("repost.png", post.Reposts, textX + MetricPitch, y)
                   & Metric("like.png", post.Likes, textX + MetricPitch * 2f, y);

            if (!ok)
            {
                Hud.Text(post.Replies + "   REPLIES        " + post.Reposts + "   REPOSTS        " +
                         post.Likes + "   LIKES",
                         textX, y, 0.235f, Color.FromArgb(150, 150, 158, 152),
                         Hud.FontLabel, centre: false);
            }

            // Divider under the post, inset so it reads as a separator rather than a box edge.
            Hud.RectFrom(left + Pad, top + PostHeight(post) - PostGap * 0.6f,
                         PanelWidth - Pad * 2f, 0.0010f,
                         Color.FromArgb(40, 200, 205, 200));
        }

        /// <summary>
        /// One engagement figure: art, then the number, on a column the whole feed shares.
        ///
        /// Hud.File centres what it draws while Hud.Text places by the TOP edge, so the two need
        /// different anchors to sit on one line. The art goes a hair below the text's optical
        /// centre because a solid filled glyph reads heavier than two digits.
        ///
        /// Grey, both of them. Not a red heart: the about-you wash is the one thing on this
        /// screen that has to win, and a coloured glyph on every post would out-shout it.
        /// </summary>
        private static bool Metric(string file, int value, float x, float y)
        {
            if (!Hud.File(file, x + Hud.ToX(MetricIcon) * 0.5f, y + 0.0075f, MetricIcon, 0f, MetricArt))
            {
                return false;
            }

            Hud.Text(value.ToString(), x + Hud.ToX(MetricIcon) + 0.005f, y, 0.26f,
                     MetricNum, Hud.FontLabel, centre: false);

            return true;
        }

        /// <summary>
        /// How tall one post is. DrawPost MUST lay out inside this or posts overlap.
        ///
        /// Unchanged in value -- only named into its parts, so the two methods cannot drift.
        ///
        ///   budget    MetaHeight + body + MetricsHeight + PostGap
        ///   DrawPost  starts at top + 0.002, adds MetaHeight, the body, then MetricGap
        ///
        /// which puts the engagement row's top edge at top + 0.029 + body. The art is 0.016
        /// centred 0.0075 under that, so it bottoms out at top + 0.0445 + body, the divider
        /// sits at top + 0.0498 + body and the next post starts at top + 0.057 + body. The row
        /// grew from a text cap to a taller icon and still clears, which is exactly why
        /// MetricsHeight did not need to grow with it.
        /// </summary>
        private float PostHeight(Post post)
        {
            var lines = Math.Max(1, Lines(post).Count);

            return MetaHeight + lines * LineHeight + MetricsHeight + PostGap;
        }

        /// <summary>
        /// Wrapped once and remembered.
        ///
        /// Wrapping measures text through the game, which is a native call per word per line --
        /// doing that for every visible post every frame is hundreds of calls a frame for text
        /// that never changes after it is written.
        /// </summary>
        private List<string> Lines(Post post)
        {
            List<string> cached;
            if (_wrapped.TryGetValue(post.Body, out cached)) return cached;

            var width = PanelWidth - Pad * 2f - Hud.ToX(AvatarSize) - 0.012f;
            var lines = Wrap(post.Body, width, BodyScale);

            // Bounded, because the cache lives as long as the session and a very long game
            // would otherwise keep every post ever written.
            if (_wrapped.Count > 400) _wrapped.Clear();

            _wrapped[post.Body] = lines;
            return lines;
        }

        private static List<string> Wrap(string text, float width, float scale)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text)) return lines;

            var words = text.Split(' ');
            var current = "";

            foreach (var word in words)
            {
                var candidate = current.Length == 0 ? word : current + " " + word;

                float measured;
                try { measured = Hud.MeasureText(candidate, scale, Hud.FontBody); }
                catch { measured = candidate.Length * scale * 0.011f; }

                if (measured <= width || current.Length == 0)
                {
                    current = candidate;
                    continue;
                }

                lines.Add(current);
                current = word;
            }

            if (current.Length > 0) lines.Add(current);
            return lines;
        }

        public void RestoreWorld() => ReleaseMugshot();
    }
}
