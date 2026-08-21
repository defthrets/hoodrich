using System;
using System.Collections.Generic;
using System.Drawing;
using Control = GTA.Control;
using GTA;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Gangs;
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
        private static readonly string[] TabNames =
            { "ALL", "ABOUT YOU", "POST", "DISS" };

        private const int TabPost = 2;
        private const int TabDiss = 3;

        /// <summary>Whether the body is the timeline rather than a list of things to do.</summary>
        private bool IsFeedTab { get { return _tab < 2; } }

        /// <summary>How long a hold has to last before a diss goes out.</summary>
        private const int DissHoldMs = 550;

        /// <summary>After a tab change, no hold may begin. Mirrors the open grace.</summary>
        private const int TabGraceMs = 140;

        /// <summary>How long a result, a refusal or a nudge stays on screen.</summary>
        private const int NoteMs = 2600;
        private const int NudgeMs = 1200;

        private const float BodyTop = 0.208f;
        private const float FirstRow = 0.242f;
        private const float RowPitch = 0.034f;
        private const float RowHeight = 0.030f;
        private const float NoteGap = 0.014f;

        private const float TabGap = 0.022f;
        private const float SplitGap = 0.034f;

        private static readonly Color RowWash = Color.FromArgb(46, 255, 255, 255);
        private static readonly Color DeadWash = Color.FromArgb(22, 255, 255, 255);
        private static readonly Color Hairline = Color.FromArgb(40, 200, 205, 200);
        private static readonly Color SplitInk = Color.FromArgb(70, 200, 205, 200);

        /// <summary>One line you can put out that costs nothing.</summary>
        private struct Sayable
        {
            public string Label;
            public string Set;
            public string Head;
            public string Line;
        }

        /// <summary>
        /// A table rather than a hardcoded row.
        ///
        /// There is exactly one non-diss "you" set in the content today, and a section holding
        /// a single row reads like something failed to load. A second line is one entry here
        /// plus one set in the json.
        /// </summary>
        private static readonly Sayable[] Says =
        {
            new Sayable { Label = "About the day", Set = "YouDaily",
                          Head = "ABOUT THE DAY", Line = "Costs you nothing." },
        };

        /// <summary>Posts one of the free sets. True if anything actually went out.</summary>
        public Func<string, bool> Say;

        /// <summary>Names a set in public. True if the post went out.</summary>
        public Func<string, bool> Diss;

        /// <summary>Whether somebody is already coming about something you said.</summary>
        public Func<bool> PaybackDue;

        public GangRegistry Gangs;
        public Affiliation Crew;

        /// <summary>Where the cursor is inside the current action tab's list.</summary>
        private int _pick;
        private int _tabAt;

        private int _holdFrom;
        private bool _holdArmed;
        private bool _holdSpent;

        private string _note;
        private Color _noteInk = Palette.TextDim;
        private int _noteAt;

        private string _nudge;
        private int _nudgeAt;

        /// <summary>Whether the row under the cursor can be fired, and why not. Written in Update.</summary>
        private bool _live;
        private string _why;

        private readonly List<GangDef> _dissList = new List<GangDef>();

        /// <summary>Strip metrics, measured once when the screen opens rather than every frame.</summary>
        private readonly float[] _stripW = new float[4];
        private float _stripScale = 0.26f;
        private float _stripGap = TabGap;
        private float _stripSplit = SplitGap;

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

            _pick = 0;
            _tabAt = Game.GameTime;
            _holdFrom = 0;
            _holdArmed = false;
            _holdSpent = false;
            _note = null;
            _nudge = null;

            BuildLists();
            Strip();

            RequestMugshot();
            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        public void Close()
        {
            IsOpen = false;
            _holdFrom = 0;
            ReleaseMugshot();
        }

        /// <summary>
        /// Who can be named, and who can be called out. Built once per open.
        ///
        /// Your own set is on neither list and the Families are on neither, which is the same
        /// rule the wheel pages had: there is no version of this where Franklin posts a diss
        /// aimed at the Families.
        /// </summary>
        private void BuildLists()
        {
            _dissList.Clear();

            if (Gangs == null) return;

            foreach (var gang in Gangs.All)
            {
                if (gang == null) continue;

                if (Crew != null && Crew.Current != null &&
                    string.Equals(gang.Id, Crew.Current.Id, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.Equals(gang.Id, "families", StringComparison.OrdinalIgnoreCase))
                {
                    _dissList.Add(gang);
                }
            }
        }

        /// <summary>
        /// Fits five labels and a right-hand readout across the panel, stepping down if it has to.
        ///
        /// Measured once here rather than twice a frame in Tabs, which is a net saving on what
        /// it replaces. No label is ever dropped or cut -- the gaps close first, then the type
        /// comes down, because a strip you cannot read all of is worse than a slightly tighter
        /// one.
        /// </summary>
        private void Strip()
        {
            _stripScale = 0.26f;
            _stripGap = TabGap;
            _stripSplit = SplitGap;

            var readout = 0.05f;

            try
            {
                readout = Math.Max(Hud.MeasureText("999 POSTS", 0.24f, Hud.FontLabel),
                                   Hud.MeasureText("WAR ON", 0.24f, Hud.FontLabel));
            }
            catch { /* the estimate will do */ }

            for (var pass = 0; pass < 3; pass++)
            {
                var run = 0f;

                for (var i = 0; i < TabNames.Length; i++)
                {
                    _stripW[i] = 0.02f;

                    try { _stripW[i] = Hud.MeasureText(TabNames[i], _stripScale, Hud.FontLabel); }
                    catch { /* the estimate will do */ }

                    run += _stripW[i];
                }

                var left = 0.5f - PanelWidth * 0.5f;
                var need = left + Pad + run + _stripGap * 3f + _stripSplit + 0.010f + readout;

                if (need <= left + PanelWidth - Pad) return;

                if (pass == 0) { _stripGap = 0.015f; _stripSplit = 0.024f; }
                else if (pass == 1) _stripScale = 0.235f;
            }
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

            // Everything is written here. Draw only reads.
            _live = CanFire(out _why);

            if (Game.GameTime - _openedAt < OpenGraceMs)
            {
                // Whatever opened the wheel must not become a hold the instant this appears.
                _holdFrom = 0;
                _holdArmed = false;
                return;
            }

            // The way out, before anything else can read a key. It is the panic key and it
            // never means anything else, on any tab, mid-hold included.
            if (Pressed(Control.PhoneCancel))
            {
                Hud.PlaySound("BACK", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                Close();
                return;
            }

            if (Pressed(Control.PhoneLeft)) { Tab(-1); return; }
            if (Pressed(Control.PhoneRight)) { Tab(1); return; }

            if (IsFeedTab)
            {
                if (Pressed(Control.PhoneUp)) Scroll(-1);
                else if (Pressed(Control.PhoneDown)) Scroll(1);

                // ENTER does nothing on a feed tab. It used to close the screen, which made
                // this the one screen in the mod where ENTER meant leave.
                return;
            }

            var rows = Rows();

            if (Pressed(Control.PhoneUp)) Move(-1);
            else if (Pressed(Control.PhoneDown)) Move(1);

            Commit(rows);
        }

        private int Rows()
        {
            if (_tab == TabPost) return Says.Length;
            if (_tab == TabDiss) return _dissList.Count;

            return 0;
        }

        private void Move(int step)
        {
            var rows = Rows();
            if (rows <= 0) return;

            var next = _pick + step;
            if (next < 0) next = 0;
            if (next > rows - 1) next = rows - 1;
            if (next == _pick) return;

            _pick = next;

            // A cursor move cancels a hold. Otherwise a bar started on one gang finishes on
            // whoever you happened to scroll onto.
            _holdFrom = 0;
            _note = null;

            Hud.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        /// <summary>
        /// A tap posts. A hold starts a fight.
        ///
        /// The tap is read on RELEASE rather than on the press edge, because the press edge and
        /// the first frame of a hold are the same frame -- reading the tap on press would make
        /// it impossible ever to begin a hold on the same key.
        /// </summary>
        private void Commit(int rows)
        {
            var now = Game.GameTime;
            var down = Held(Control.PhoneSelect) || Held(Control.Jump) || Held(Control.Context);

            if (!down)
            {
                // Down and back up before the bar filled: that was a tap.
                if (_holdFrom != 0 && !_holdSpent) Tapped();

                _holdFrom = 0;
                _holdSpent = false;
                _holdArmed = true;
                return;
            }

            if (!_holdArmed || _holdSpent) return;
            if (now - _tabAt < TabGraceMs) return;
            if (rows == 0) return;

            if (!_live)
            {
                // A refused row never starts a timer -- and if it goes refused MID-HOLD, because
                // a war started elsewhere or you got in a car, the bar is zeroed the same frame
                // rather than freezing full and then bouncing.
                if (_holdFrom != 0)
                {
                    _holdFrom = 0;
                    Note(_why, Palette.Warn);
                    Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                }

                _holdSpent = true;
                return;
            }

            if (_holdFrom == 0)
            {
                _holdFrom = now;

                // A free line fires on the press, so posting feels like a button rather than
                // a chore. Only the two that start something need holding.
                if (_tab == TabPost)
                {
                    _holdSpent = true;
                    _holdFrom = 0;
                    Fire();
                }

                return;
            }

            if (now - _holdFrom >= DissHoldMs)
            {
                _holdSpent = true;
                _holdFrom = 0;
                Fire();
            }
        }

        /// <summary>
        /// A tap on a row that wants a hold. Answered, never ignored.
        ///
        /// A guard that silently does nothing reads as a broken screen, and somebody who thinks
        /// a screen is broken presses harder. This is the one place a wrong guess should teach
        /// rather than punish.
        /// </summary>
        private void Tapped()
        {
            if (_tab == TabPost) return;

            _nudge = "HOLD IT DOWN";
            _nudgeAt = Game.GameTime;

            Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        private void Fire()
        {
            if (_tab == TabPost)
            {
                var went = Say != null && Say(Says[_pick].Set);

                Note(went ? "Posted." : "Nothing to say right now.",
                     went ? Palette.Cash : Palette.TextDim);

                Hud.PlaySound(went ? "SELECT" : "ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                return;
            }

            if (_tab != TabDiss) return;
            if (_pick >= _dissList.Count) return;

            var gang = _dissList[_pick];
            var sent = Diss != null && Diss(gang.Id);

            Note(sent ? "That's out there now. They read it too." : "Not right now.",
                 sent ? Palette.Danger : Palette.Warn);

            Hud.PlaySound(sent ? "SELECT" : "ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        private void Note(string text, Color ink)
        {
            _note = text;
            _noteInk = ink;
            _noteAt = Game.GameTime;
        }

        /// <summary>Whether the row under the cursor can be fired, and what to say if not.</summary>
        private bool CanFire(out string why)
        {
            why = null;

            if (_tab == TabPost)
            {
                if (Say == null) { why = "Not right now."; return false; }
                return true;
            }

            if (_tab != TabDiss) return false;

            if (Diss == null) { why = "Not right now."; return false; }
            if (_dissList.Count == 0) { why = "Nobody worth the trouble."; return false; }

            return true;
        }

        /// <summary>How far through a hold, 0..1.</summary>
        private float Progress()
        {
            if (_holdFrom == 0) return 0f;

            var t = (Game.GameTime - _holdFrom) / (float)DissHoldMs;

            return t < 0f ? 0f : t > 1f ? 1f : t;
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
            // Clamped, NOT wrapped. A modulo would put CALL OUT one LEFT press from where every
            // open starts, which is the single worst adjacency available on this screen.
            var next = _tab + step;
            if (next < 0) next = 0;
            if (next > TabNames.Length - 1) next = TabNames.Length - 1;
            if (next == _tab) return;

            _tab = next;
            _tabAt = Game.GameTime;

            _pick = 0;
            _holdFrom = 0;
            _holdSpent = false;
            _note = null;
            _nudge = null;

            // Required, not tidiness. Index 14 of ALL is not index 14 of ABOUT YOU, so carrying
            // the scroll across lands you somewhere arbitrary -- or, at 30 with six matching
            // posts, on a blank panel.
            _scroll = 0;
            _lastShown = 0;

            // _tally has two entries and the strip has five. It also goes stale while you are
            // away on an action tab, because HoldPosition returns early there -- which is why
            // it is reassigned here, on the way back in, alongside the scroll being zeroed.
            _seenCount = _tab < 2 ? _tally[_tab] : 0;

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
            if (!IsFeedTab) return 0;

            var count = _tally[_tab];
            if (count <= 0) return 0;

            return _lastShown > 0 ? Math.Max(0, count - _lastShown) : Math.Max(0, count - 1);
        }

        /// <summary>Keeps the reader looking at the same post when new ones arrive above it.</summary>
        private void HoldPosition()
        {
            if (!IsFeedTab) return;

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

        /// <summary>Whether a key is down right now, which is what a hold is made of.</summary>
        private static bool Held(Control control)
        {
            return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)control);
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

            // Not cosmetic. Several things in this mod read E on the ENABLED path, so without
            // this, pressing E with the feed open greets a homie or walks you through a door
            // while you are looking at a menu.
            Game.DisableControlThisFrame(Control.Context);
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
            // The bar across the very top turns red on the two tabs that start fights. It is
            // the one mode signal readable without looking at any particular element.
            var edge = _tab >= TabDiss ? Palette.Danger : Palette.Accent;

            Hud.RectFrom(left, PanelTop, PanelWidth, PanelHeight, Color.FromArgb(238, 12, 13, 15));
            Hud.RectFrom(left, PanelTop, PanelWidth, 0.0028f, edge);

            Count();

            var y = DrawHeader(left, right, edge);
            y = Tabs(x, right, y + 0.010f, edge);

            if (!IsFeedTab)
            {
                Action(left, x, right);
                Keys(x, right);
                return;
            }

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
        private float Tabs(float x, float right, float y, Color edge)
        {
            var cx = x;

            for (var i = 0; i < TabNames.Length; i++)
            {
                var here = i == _tab;

                // _tally has TWO entries and the strip has FOUR.
                var empty = i < 2 && _tally[i] == 0;

                if (here)
                {
                    Hud.RectFrom(cx - 0.004f, y - 0.004f, _stripW[i] + 0.008f, 0.024f, RowWash);

                    Hud.RectFrom(cx - 0.004f, y + 0.019f, _stripW[i] + 0.008f, 0.0022f,
                                 i < 2 ? Palette.Accent : Palette.Danger);
                }

                // One of the four in warning colour. POST costs nothing and should not be
                // dressed as though it did, so DISS is the only label that arrives amber.
                //
                // An empty feed tab says so before you press it. NOT Palette.TextDisabled: that
                // is full alpha and composites BRIGHTER than TextDim, which would make the
                // empty tab the loudest thing on the row.
                var ink = here ? Palette.Text
                    : i < 2 ? (empty ? Palette.Alpha(Palette.TextDim, 90) : Palette.TextDim)
                    : i == TabPost ? Palette.TextDim
                    : Palette.Alpha(Palette.Warn, 170);

                Hud.Text(TabNames[i], cx, y, _stripScale, ink, Hud.FontLabel, centre: false);

                cx += _stripW[i] + (i == 1 ? _stripSplit : _stripGap);

                // Looking, and doing. A wide GAP does the work here -- a hairline on its own is
                // two pixels and cannot carry a divide this important, so the rule sits inside
                // the gap rather than replacing it.
                if (i == 1)
                {
                    Hud.RectFrom(cx - _stripSplit * 0.5f, y - 0.002f, 0.0022f, 0.020f, SplitInk);
                }
            }

            if (IsFeedTab)
            {
                var n = _tally[_tab];

                Hud.TextRight(n + (n == 1 ? " POST" : " POSTS"), right, y + 0.0015f, 0.24f,
                              Palette.TextDim, Hud.FontLabel);
            }
            y += 0.032f;
            Hud.RectFrom(x, y, PanelWidth - Pad * 2f, 0.0022f, edge);
            return y + 0.012f;
        }

        /// <summary>The list of things you can put out, and what each one costs you.</summary>
        private void Action(float left, float x, float right)
        {
            var rows = Rows();
            var head = _tab == TabPost ? "SAY SOMETHING" : "WHO";

            Hud.Text(head, x, BodyTop, 0.26f, Palette.TextDim, Hud.FontLabel, centre: false);
            Hud.RectFrom(x, BodyTop + 0.022f, PanelWidth - Pad * 2f, 0.0010f, Hairline);

            var t = Progress();

            // Nine rows never reach the floor today. Computed anyway, so a tenth gang added
            // later clips cleanly instead of drawing off the bottom of the panel.
            var maxRows = (int)((0.760f - FirstRow) / RowPitch);
            var draw = Math.Min(rows, maxRows);

            for (var i = 0; i < draw; i++)
            {
                var top = FirstRow + i * RowPitch;
                var here = i == _pick;

                string label;
                string value;
                Color valueInk;
                var tick = Color.Transparent;

                if (_tab == TabPost)
                {
                    label = Says[i].Label;
                    value = "FREE";
                    valueInk = Palette.Cash;
                }
                else
                {
                    var gang = _dissList[i];

                    label = gang.Name;
                    tick = gang.Colour;

                    // A four-character tag that is readable beats a name that is cut off. It is
                    // a bad thing to aim a war at a name you cannot read.
                    try
                    {
                        if (Hud.MeasureText(label, 0.30f, Hud.FontChaletLondon) > 0.230f)
                        {
                            label = gang.Tag;
                        }
                    }
                    catch { /* the name will do */ }

                    var beefing = Crew != null && Crew.Beefing(gang.Id);

                    value = beefing ? "ALREADY BEEFING" : gang.Tag;
                    valueInk = beefing ? Palette.Warn : Palette.TextDim;
                }

                Row(left, x, right, top, here, !_live, tick, label, value, valueInk,
                    here && t > 0f ? t : 0f);
            }

            if (rows == 0)
            {
                Hud.Text(_tab == TabDiss
                             ? "Nobody worth the trouble"
                             : "There's nobody you're not already with",
                         x + 0.006f, FirstRow + 0.006f, 0.30f,
                         Palette.Alpha(Palette.TextDim, 110), Hud.FontChaletLondon, centre: false);
            }

            NoteStrip(x, right, FirstRow + Math.Max(1, draw) * RowPitch + NoteGap, t);
        }

        private static void Row(float left, float x, float right, float top, bool here, bool dead,
                                Color tick, string label, string value, Color valueInk, float fill)
        {
            if (here)
            {
                // Under a dead row the cursor is still visibly SOMEWHERE, but visibly on
                // something inert.
                Hud.RectFrom(left + 0.002f, top, PanelWidth - 0.012f, RowHeight,
                             dead ? DeadWash : RowWash);

                Hud.RectFrom(left + 0.002f, top, 0.0022f, RowHeight,
                             dead ? Palette.Alpha(Palette.TextDim, 120) : Palette.Accent);
            }

            var textX = x + 0.006f;

            if (tick.A > 0)
            {
                // Their own colour, which is the one identity mark that costs no width.
                Hud.RectFrom(x, top + 0.006f, 0.0022f, 0.018f,
                             dead ? Palette.Alpha(tick, 90) : tick);

                textX = x + 0.010f;
            }

            var ink = dead ? Palette.Alpha(Palette.TextDim, 110)
                : here ? Palette.Text
                : Palette.Alpha(Palette.Text, 175);

            Hud.Text(label, textX, top + 0.006f, 0.30f, ink, Hud.FontChaletLondon, centre: false);

            if (!string.IsNullOrEmpty(value))
            {
                Hud.TextRight(value, right, top + 0.008f, 0.24f,
                              dead ? Palette.Alpha(Palette.TextDim, 110) : valueInk,
                              Hud.FontLabel);
            }

            if (fill > 0f)
            {
                Hud.RectFrom(left + 0.002f, top + RowHeight - 0.0022f,
                             (PanelWidth - 0.012f) * fill, 0.0022f, Palette.Danger);
            }
        }

        /// <summary>
        /// What this row actually does, rewritten every frame from the row the cursor is on.
        ///
        /// Under the list rather than at the top of the tab, because on a nine-row list a
        /// warning above the list is most of a screen away from the thing it warns about -- and
        /// the moment that matters is the moment your thumb is on the key. Stronger than the
        /// wheel page ever was: that showed one warning for a whole sub-page, this one names
        /// the set.
        /// </summary>
        private void NoteStrip(float x, float right, float top, float t)
        {
            Hud.RectFrom(x, top, PanelWidth - Pad * 2f, 0.0010f, Hairline);

            // The hold again, across the words it is about, so the bar and the warning read as
            // one object rather than two.
            if (t > 0f)
            {
                Hud.RectFrom(x, top - 0.004f, (PanelWidth - Pad * 2f) * t, 0.0022f, Palette.Danger);
            }

            // Assigned here rather than by every branch. The chain below used to end in an
            // unconditional else -- the call-out card -- so the compiler could see that one of
            // them always ran. With that card gone the chain can fall through, and a header
            // that draws nothing is better than one that cannot compile.
            var headTxt = "";
            var headInk = Palette.TextDim;
            var icon = "";

            string l1 = "", l2 = "", l3 = "";
            Color i1 = Palette.TextDim, i2 = Palette.TextDim, i3 = Palette.TextDim;

            if (!_live)
            {
                headTxt = "NOT RIGHT NOW";
                headInk = Palette.Warn;
                l1 = _why;
            }
            else if (_tab == TabPost)
            {
                headTxt = Says[_pick].Head;
                headInk = Palette.TextDim;
                icon = "reply.png";
                l1 = Says[_pick].Line;
            }
            else if (_tab == TabDiss)
            {
                var g = _dissList[_pick];

                headTxt = "NAMING " + g.Name.ToUpperInvariant();
                headInk = Palette.Danger;
                icon = "megaphone.png";

                l1 = "They answer -- on here, within the minute.";
                l2 = "Then -- somebody comes to find you.";
                i2 = Palette.Danger;
                l3 = string.IsNullOrEmpty(g.TurfHint) ? "Say it where they can see it" : g.TurfHint;
                i3 = Palette.Alpha(Palette.TextDim, 150);
            }

            // A result or a refusal replaces the LINES and keeps the HEAD, so the set you are
            // aiming at never leaves the screen at the moment you are aiming at it.
            if (_note != null && Game.GameTime - _noteAt < NoteMs)
            {
                l1 = _note;
                i1 = _noteInk;
                l2 = "";
                l3 = "";
            }
            else _note = null;

            var hx = x;

            if (icon != "" &&
                Hud.File(icon, x + Hud.ToX(0.018f) * 0.5f, top + 0.016f, 0.018f, 0f, headInk))
            {
                hx = x + Hud.ToX(0.018f) + 0.006f;
            }

            Hud.Text(headTxt, hx, top + 0.008f, 0.25f, headInk, Hud.FontLabel, centre: false);

            // The mark slot: the whole feedback channel for the hold, three states and no more.
            var mark = "";
            var markInk = Palette.Warn;

            if (t > 0f) mark = "RELEASE TO STOP";
            else if (_nudge != null && Game.GameTime - _nudgeAt < NudgeMs)
            {
                mark = _nudge;
                markInk = Palette.Danger;
            }
            else _nudge = null;

            if (mark != "")
            {
                Hud.TextRight(mark, right, top + 0.008f, 0.24f, markInk, Hud.FontLabel);
            }

            var w = PanelWidth - Pad * 2f;

            if (l1 != "") Hud.Text(Hud.Fit(l1, w, 0.27f, Hud.FontBody), x, top + 0.030f, 0.27f,
                                   i1, Hud.FontBody, centre: false);
            if (l2 != "") Hud.Text(Hud.Fit(l2, w, 0.27f, Hud.FontBody), x, top + 0.052f, 0.27f,
                                   i2, Hud.FontBody, centre: false);
            if (l3 != "") Hud.Text(Hud.Fit(l3, w, 0.27f, Hud.FontBody), x, top + 0.074f, 0.27f,
                                   i3, Hud.FontBody, centre: false);
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

            // "TABS" rather than "FILTER", which would be a lie about half the strip now. And
            // every line ends in BACKSPACE OUT, so the way out is the last thing read in every
            // state.
            string keys;

            if (Progress() > 0f) keys = "KEEP HOLDING      LET GO TO STOP";
            else if (IsFeedTab) keys = "UP/DOWN  SCROLL      LEFT/RIGHT  TABS      BACKSPACE  OUT";
            else if (Rows() == 0) keys = "LEFT/RIGHT  TABS      BACKSPACE  OUT";
            else if (!_live) keys = "UP/DOWN  PICK      BACKSPACE  OUT";
            else if (_tab == TabPost) keys = "UP/DOWN  PICK      ENTER  POST      BACKSPACE  OUT";
            else keys = "UP/DOWN  PICK      HOLD ENTER  SEND      BACKSPACE  OUT";

            Hud.Text(keys, x, y, 0.24f, Palette.TextDim, Hud.FontLabel, centre: false);

            // The other half of the scroll indicator, and nearly free. HoldPosition bumps the
            // scroll when new posts land so the one you are reading stays put, which means the
            // reader silently accumulates unread posts above them with nothing saying so.
            // _scroll IS that number.
            if (IsFeedTab && _scroll > 0)
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
        private float DrawHeader(float left, float right, Color edge)
        {
            Hud.RectFrom(left, PanelTop, PanelWidth, CardHeight, Color.FromArgb(240, 18, 20, 22));

            // Re-asserted, so draw order cannot eat it.
            Hud.RectFrom(left, PanelTop, PanelWidth, 0.0028f, edge);

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

            // The wheel panel's "owed a visit" row, in a slot that already existed and was
            // already right-aligned -- so it is visible the frame the screen opens rather than
            // four tabs away, and it costs no layout.
            var payback = PaybackDue != null && PaybackDue();

            Hud.TextRight(payback
                              ? "SOMEBODY'S COMING"
                              : "FOLLOWERS  ·  " + _feed.Following.ToString("N0") + " FOLLOWING",
                          right, PanelTop + 0.0595f, 0.24f,
                          payback ? Palette.Danger : Palette.TextDim, Hud.FontLabel);

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
