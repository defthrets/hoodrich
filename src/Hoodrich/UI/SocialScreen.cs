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
        /// <summary>
        /// Where the panel's top edge is THIS FRAME.
        ///
        /// A property rather than the constant it used to be, and that one change moves the
        /// whole screen. Every one of the two dozen places this file positions something --
        /// the frame, the cards, the rule, the footer -- measures from here, so the panel
        /// slides in and out as a single piece without any of them being touched.
        /// </summary>
        private float PanelTop => PanelTopAt + _curtain.Lift;

        private const float PanelTopAt = 0.070f;

        /// <summary>How this panel arrives and how it leaves. See UI.Curtain.</summary>
        private readonly Curtain _curtain = new Curtain();
        private const float PanelHeight = 0.860f;

        private const float Pad = 0.014f;
        private const float AvatarSize = 0.038f;
        private const float BodyScale = 0.315f;
        private const float LineHeight = 0.0248f;

        /// <summary>
        /// Gap BETWEEN two cards now, rather than under a post before the next one's rule.
        ///
        /// Wider than it was, and the width is the point. A hairline between two blocks of text
        /// separates them the way a ruled notebook separates lines -- they are still one sheet.
        /// Air between two shapes makes them two objects, and a feed is a stack of objects.
        ///
        /// It costs about one post a screenful. Worth it: eight posts you scan as a wall are
        /// worth less than seven you read.
        /// </summary>
        private const float PostGap = 0.017f;

        /// <summary>Breathing room inside a card, above the name and below the figures.</summary>
        private const float CardPad = 0.006f;

        /// <summary>How round the cards are. Enough to read as a corner, not a pill.</summary>
        private const float CardRadius = 0.005f;

        /// <summary>The card itself, and the hairline that gives it an edge in the dark.</summary>
        private static readonly Color CardFace = Color.FromArgb(64, 150, 158, 152);
        private static readonly Color CardEdge = Color.FromArgb(30, 210, 220, 214);

        /// <summary>
        /// The display name, in one place because two things measure it.
        ///
        /// Up from 0.34 against a body at 0.315. Those two numbers are close enough that the
        /// eye reads a post as one undifferentiated block and has to actually parse it to find
        /// out who is talking -- which is the whole reason the screen looked like a list of
        /// sentences rather than a feed. A name has to win its line.
        /// </summary>
        private const float NameScale = 0.385f;

        /// <summary>The handle and the stamp. Down, and further out of the way.</summary>
        private const float StampScale = 0.245f;

        private const int OpenGraceMs = 220;

        /// <summary>The fixed identity card. Everything under it scrolls; this does not.</summary>
        /// <summary>
        /// The header card: the wordmark, the screen's name, your avatar and your numbers.
        ///
        /// Grew by 0.023 when the mark went in above the title, and everything under it moved
        /// by the same amount. This one number is where the FEED starts, so getting it wrong
        /// does not clip the header -- it slides the whole timeline up over it.
        /// </summary>
        /// <summary>
        /// Taller than it was, because the masthead is now three storeys rather than two.
        ///
        /// The mark, the word under it, and the account under that. Everything below the header
        /// is laid out from what this method RETURNS rather than from a second constant, so the
        /// tab strip and the whole feed move down with it on their own.
        /// </summary>
        private const float CardHeight = 0.128f;

        /// <summary>
        /// The masthead word, and where it sits.
        ///
        /// Down from a half. At a half it competed with the mark above it rather than sitting
        /// under it, and a title the same weight as the logo is two logos.
        ///
        /// Not smaller than that. The account name below is 0.42, and a masthead that loses to
        /// the name under it has stopped being a masthead -- this is the floor, not a target.
        ///
        /// The top edge stays where it was, near enough. Text places by its TOP, so a smaller
        /// line at the same top can only free space BELOW it, which is where the hairline is
        /// and where a collision would actually show. Moving the top down to re-centre it
        /// optically would have walked the caps into that hairline for a gain nobody can see.
        /// </summary>
        private const float TitleScale = 0.42f;
        private float TitleTop => PanelTop + 0.0395f;

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

        private const float RowPitch = 0.034f;
        private const float RowHeight = 0.030f;
        private const float NoteGap = 0.014f;

        private const float TabGap = 0.022f;

        /// <summary>The picture on a composer or diss row.</summary>
        private const float RowArt = 0.017f;
        private const float SplitGap = 0.034f;

        private static readonly Color RowWash = Color.FromArgb(46, 255, 255, 255);
        private static readonly Color DeadWash = Color.FromArgb(22, 255, 255, 255);
        private static readonly Color Hairline = Color.FromArgb(40, 200, 205, 200);

        /// <summary>Border thickness, as a height fraction. Sideways it goes through ToX.</summary>
        private const float Rule = 0.0016f;

        /// <summary>How far a corner tick runs along each edge.</summary>
        private const float Tick = 0.026f;
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
        /// <summary>
        /// Set by Main: what he would post about right now, as set, subject and label.
        ///
        /// A hook rather than a lookup, because the answer depends on the mission runner, the
        /// corner and the clock -- none of which this screen knows about, and none of which it
        /// should learn.
        /// </summary>
        public Func<string[]> Topic;

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

        public bool IsOpen => _curtain.Showing;

        public void Open()
        {
            _curtain.Open();

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
            // The button that got you out of here does not also swing at somebody.
            if (IsOpen) Core.InputGuard.Swallow();
            _curtain.Close();
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

            // Still drawn on the way out, and still holding the controls, but no longer
            // listening -- or the screen you have just closed spends its last tenth of a
            // second acting on whatever you press next.
            if (!_curtain.Taking) return;

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
            Core.Fists.Off();
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

            Hud.Panel(left, PanelTop, PanelWidth, PanelHeight,
                      Color.FromArgb(238, 12, 13, 15), edge);

            Count();

            var y = DrawHeader(left, right, edge);
            y = Tabs(x, right, y + 0.010f, edge);

            if (!IsFeedTab)
            {
                Action(left, x, right, y);
                Keys(x, right);
                Frame(left, edge);
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

                DrawPost(left, y, post, shown);
                y += height;
                shown++;
            }

            _lastShown = shown;

            if (count == 0) Nothing(feedTop);
            else Rail(left, feedTop, bottom, count, shown);

            Keys(x, right);
            Frame(left, edge);
        }

        /// <summary>
        /// The border, drawn last so nothing paints over it.
        ///
        /// The panel had a bar across the top and three open sides, which is not a window --
        /// it is a dark rectangle that happens to end. On a bright street at midday the bottom
        /// edge genuinely disappears into whatever is behind it and the feed reads as text
        /// floating over the road.
        ///
        /// Thickness is one number taken two ways. Sideways it goes through ToX, which divides
        /// by the aspect, so the frame is the same number of PIXELS thick all the way round on
        /// any monitor -- a plain fraction used for both would draw a hairline top and bottom
        /// and a fat post down either side of an ultrawide.
        ///
        /// The corners are the mode colour and the rest is grey. A full accent frame would put
        /// the loudest colour on the screen around the outside of everything and leave the top
        /// bar with nothing to say; corner ticks carry the same signal in a tenth of the ink.
        /// </summary>
        private void Frame(float left, Color edge)
        {
            // Cyan, and brighter than a hairline.
            //
            // The border carries the sweep and the corner ticks, and at sixty alpha in grey it
            // was doing that where nobody could see it -- a moving thing you cannot make out is
            // the same as a still one. Cyan because nothing else on this screen is: the sets
            // own the warm colours and the greens, the mode bar owns the accent, and a frame
            // that shares a colour with any of them stops being the frame.
            //
            // The corner ticks keep the MODE colour rather than going cyan with it. They are
            // the one thing on the border that means something -- red on the tabs that start
            // fights -- and a signal painted the same colour as the thing it sits on is not a
            // signal any more.
            Hud.Frame(left, PanelTop, PanelWidth, PanelHeight, FrameInk, edge, Rule, Tick);
        }

        /// <summary>The border's own colour, which is nobody else's on this screen.</summary>
        private static readonly Color FrameInk = Color.FromArgb(150, 90, 215, 235);

        /// <summary>
        /// The tab strip, which is the armourer's shelf strip with the array swapped.
        ///
        /// The count is right-aligned to a FIXED edge rather than folded into a label. Labels
        /// are measured and laid out one after another, so "ALL 79" going to "ALL 80" would
        /// shove ABOUT YOU sideways every ten seconds -- fine in a still, twitching all evening
        /// in motion.
        /// </summary>
        /// <summary>The marker under the tab strip, which travels rather than teleports.</summary>
        private readonly Eased _tabX = new Eased();
        private readonly Eased _tabW = new Eased();

        private float Tabs(float x, float right, float y, Color edge)
        {
            // WHERE THE MARKER IS HEADED, worked out before a single label is drawn.
            //
            // It used to be painted inside the loop at whichever tab happened to be the
            // current one, which means it does not move between tabs -- it stops existing in
            // one place and starts existing in another. That is the difference between a
            // selection you can follow and one you have to go and find again, and on a strip
            // where two of the four tabs start fights, following it matters.
            var goingTo = x;

            for (var i = 0; i < _tab && i < TabNames.Length; i++)
            {
                goingTo += _stripW[i] + (i == 1 ? _stripSplit : _stripGap);
            }

            var markX = _tabX.To(goingTo, 13f);
            var markW = _tabW.To(_stripW[_tab] + 0.008f, 13f);

            // Under the labels, so it slides behind the words rather than over them.
            Hud.RectFrom(markX - 0.004f, y - 0.004f, markW, 0.024f, RowWash);

            Hud.RectFrom(markX - 0.004f, y + 0.019f, markW, 0.0022f,
                         _tab < 2 ? Palette.Accent : Palette.Danger);

            var cx = x;

            for (var i = 0; i < TabNames.Length; i++)
            {
                var here = i == _tab;

                // _tally has TWO entries and the strip has FOUR.
                var empty = i < 2 && _tally[i] == 0;

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
        /// <summary>
        /// The composer and the diss list, laid out from wherever the tabs actually finished.
        ///
        /// It used to start at a fixed height and so did its rows -- two numbers typed in when
        /// the header was a different size. The header has been rebuilt twice since, the tab
        /// strip moved down with it, and this page stayed where it was: the section label ended
        /// up printed through the tab labels, which is what "ALL" written over "SAY SOMETHING"
        /// in a screenshot actually is.
        ///
        /// Everything on the page is measured from the top it is handed now, so it cannot come
        /// apart again the next time anything above it changes height.
        /// </summary>
        private void Action(float left, float x, float right, float top)
        {
            var rows = Rows();
            var head = _tab == TabPost ? "SAY SOMETHING" : "WHO";

            Hud.Text(head, x, top, 0.26f, Palette.TextDim, Hud.FontLabel, centre: false);
            Hud.RectFrom(x, top + 0.022f, PanelWidth - Pad * 2f, 0.0010f, Hairline);

            var first = top + 0.034f;
            var t = Progress();

            // Whatever fits between here and the floor, rather than a count that was true when
            // the page began higher up.
            var maxRows = (int)((PanelTop + PanelHeight - 0.120f - first) / RowPitch);
            var draw = Math.Min(rows, Math.Max(1, maxRows));

            for (var i = 0; i < draw; i++)
            {
                var rowY = first + i * RowPitch;
                var here = i == _pick;

                string label;
                string value;
                Color valueInk;
                var tick = Color.Transparent;

                if (_tab == TabPost)
                {
                    label = Says[i].Label;

                    // THE BUTTON NAMES WHAT IS ACTUALLY GOING OUT.
                    //
                    // It always read "About the day" and then posted about the corner you were
                    // stood on, which is the one thing a compose button must not do -- you
                    // press it to say a particular thing and it says a different one. Now the
                    // row is rewritten from the same resolver that picks the post, so the two
                    // cannot disagree.
                    if (i == 0 && Topic != null)
                    {
                        try
                        {
                            var topic = Topic();
                            if (topic != null && topic.Length > 2 && topic[2].Length > 0)
                            {
                                label = topic[2];
                            }
                        }
                        catch
                        {
                            // The written label is a perfectly good fallback.
                        }
                    }
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

                Row(left, x, right, rowY, here, !_live, tick, label, value, valueInk,
                    here && t > 0f ? t : 0f, Art(i));
            }

            if (rows == 0)
            {
                Hud.Text(_tab == TabDiss
                             ? "Nobody worth the trouble"
                             : "There's nobody you're not already with",
                         x + 0.006f, first + 0.006f, 0.30f,
                         Palette.Alpha(Palette.TextDim, 110), Hud.FontChaletLondon, centre: false);
            }

            var after = first + Math.Max(1, draw) * RowPitch;

            NoteStrip(x, right, after + NoteGap, t);

            // Under the whole note strip rather than through the middle of it. The strip is a
            // rule, a heading and three lines -- about a tenth of the panel -- and the gap that
            // used to be here was half of that, which put "WHAT YOU'VE SAID" across the line
            // explaining what the thing above it costs.
            if (_tab == TabPost) Yours(left, x, after + NoteGap + NoteStripH);
        }

        /// <summary>How tall the note strip is: its rule, its heading and its three lines.</summary>
        private const float NoteStripH = 0.100f;

        /// <summary>
        /// The picture for one row of whichever list is up.
        ///
        /// The sets have their own art already -- it is on the wheel and in the war readouts --
        /// and this was the one list of gangs in the mod that showed a coloured tick instead.
        /// </summary>
        private string Art(int i)
        {
            if (_tab == TabPost) return "mobile.png";

            if (i < 0 || i >= _dissList.Count) return "";

            var id = _dissList[i].Id;
            return string.IsNullOrEmpty(id) ? "" : "gang_" + id.ToLowerInvariant() + ".png";
        }

        /// <summary>
        /// Everything you have said, under the box you say it in.
        ///
        /// The page used to be a button and a word. You pressed post, it said "Posted.", and
        /// the thing you had just written went into a feed on another tab -- so the one screen
        /// in the mod where you are the author was the one screen that never showed you what
        /// you wrote. Now it does, newest first, which means the post you just made is the top
        /// line before the confirmation has faded.
        ///
        /// Drawn with the feed's own card, deliberately. A second, simpler layout for the same
        /// object would be two things to keep in step, and your posts are not a different kind
        /// of post -- they are the same post with your name on it.
        /// </summary>
        private void Yours(float left, float x, float top)
        {
            if (_feed == null) return;

            Hud.Text("WHAT YOU'VE SAID", x, top, 0.26f, Palette.TextDim, Hud.FontLabel, centre: false);
            Hud.RectFrom(x, top + 0.022f, PanelWidth - Pad * 2f, 0.0010f, Hairline);

            var y = top + 0.034f;
            var floor = PanelTop + PanelHeight - 0.034f;
            var shown = 0;

            foreach (var post in _feed.Timeline)
            {
                if (post == null || post.By == null) continue;
                if (!string.Equals(post.By.Handle, _feed.Handle, StringComparison.OrdinalIgnoreCase)) continue;

                var height = PostHeight(post);
                if (y + height > floor) break;

                DrawPost(left, y, post);

                y += height;
                shown++;
            }

            if (shown > 0) return;

            Hud.Text("Nothing yet. Say something.", x + 0.006f, y + 0.004f, 0.30f,
                     Palette.Alpha(Palette.TextDim, 110), Hud.FontChaletLondon, centre: false);
        }

        /// <summary>A darker version of a colour, for edges and rings.</summary>
        private static Color Shade(Color c, float by)
        {
            return Color.FromArgb(c.A, (int)(c.R * by), (int)(c.G * by), (int)(c.B * by));
        }

        private static void Row(float left, float x, float right, float top, bool here, bool dead,
                                Color tick, string label, string value, Color valueInk, float fill,
                                string art = "")
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

            // And their own art after it, if this install has the file. A row with no picture
            // simply keeps its words where they were.
            if (!string.IsNullOrEmpty(art) &&
                Hud.File(art, textX + Hud.ToX(RowArt) * 0.5f, top + 0.015f, RowArt, 0f,
                         dead ? Palette.Alpha(Palette.TextDim, 110) : Palette.Text))
            {
                textX += Hud.ToX(RowArt) + 0.006f;
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

                // The strip explains the row above it, so it has to be told the same thing the
                // row was. A button reading "About the job you just did" over a heading reading
                // ABOUT THE DAY is two answers to one question.
                if (_pick == 0 && Topic != null)
                {
                    try
                    {
                        var topic = Topic();

                        if (topic != null && topic.Length > 2 && topic[2].Length > 0)
                        {
                            headTxt = topic[2].ToUpperInvariant();

                            if (topic.Length > 1 && topic[1].Length > 0)
                            {
                                l1 = "Costs you nothing. Goes out about " + topic[1] + ".";
                            }
                        }
                    }
                    catch
                    {
                        // The written pair is still correct, just less specific.
                    }
                }
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
        /// The rack has no equivalent because it shows everything at once, so this is
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
            var middle = left + PanelWidth * 0.5f;

            Sweep(left, edge);

            // The mark over the middle of the panel, the same letterhead every other screen in
            // the mod carries.
            Hud.BrandCentre(middle, PanelTop + 0.023f, 0.024f,
                            Palette.Alpha(Palette.TextDim, 175));

            // And the word under it, centred on the same axis, in the face the rest of the mod
            // reads in rather than the script one.
            //
            // It used to sit out on the left in cursive, against the avatar, which put three
            // different things -- a logo, a title and a name -- on three different alignments
            // in one header. Logo and title share a centreline now and the account owns the
            // left, which is two rules instead of none.
            Hud.Text("SOCIALS", middle, TitleTop, TitleScale, Palette.Text,
                     Hud.FontChaletLondon);

            Live(middle, TitleTop + 0.0080f);

            // A hairline under the two of them, so the masthead is visibly a masthead and the
            // account below it is visibly the account.
            Hud.RectFrom(left + Pad, PanelTop + 0.0635f, PanelWidth - Pad * 2f, 0.0012f,
                         Color.FromArgb(46, 255, 255, 255));

            // The card's own floor, full width rather than inset.
            //
            // The hairline above it separates the title from the account INSIDE the card; this
            // is where the card stops and the feed starts, and the two jobs were being done by
            // one line eight thousandths from the wrong place.
            Hud.RectFrom(left, PanelTop + CardHeight - 0.0012f, PanelWidth, 0.0012f,
                         Palette.Alpha(edge, 100));

            var cx = left + Pad + Hud.ToX(HeadSize) * 0.5f;
            var cy = PanelTop + 0.0935f;

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

            // The name where the title used to be, and the handle UNDER it rather than trailing
            // off the end of it. A name and a handle on one line is one long string that reads
            // as neither; stacked, the name is who and the handle is where.
            Hud.Text(_feed.DisplayName, headX, PanelTop + 0.0695f, 0.42f, Palette.Text,
                     Hud.FontChaletLondon, centre: false);

            var nw = 0.06f;
            try { nw = Hud.MeasureText(_feed.DisplayName, 0.42f, Hud.FontChaletLondon); }
            catch { /* the estimate will do */ }

            // A tick after it, drawn rather than loaded: a filled disc with the tick art punched
            // over it in the panel's own dark, which is the same two calls every other badge in
            // this mod is made of.
            var badge = headX + nw + 0.008f;

            Hud.Disc(badge, PanelTop + 0.0785f, 0.0062f, Palette.Alpha(Palette.Cash, 225));
            Hud.File("tick.png", badge, PanelTop + 0.0785f, 0.0092f, 0f,
                     Color.FromArgb(255, 18, 20, 22));

            Hud.Text(_feed.Handle, headX, PanelTop + 0.0955f, 0.28f,
                     Palette.TextDim, Hud.FontLabel, centre: false);

            // The wheel panel's "owed a visit" row, in a slot that already existed and was
            // already right-aligned -- so it is visible the frame the screen opens rather than
            // four tabs away, and it costs no layout.
            // The one number, right, in the money colour -- the hub's exact title rhythm, with
            // the little crowd beside it so it does not need the word FOLLOWERS to say what it
            // counts.
            var count = _feed.Followers.ToString("N0");

            Hud.TextRight(count, right, PanelTop + 0.0625f, 0.42f, Gained(), Hud.FontChaletLondon);

            var cw = 0.05f;
            try { cw = Hud.MeasureText(count, 0.42f, Hud.FontChaletLondon); }
            catch { /* the estimate will do */ }

            Hud.File("people.png", right - cw - 0.011f, PanelTop + 0.0785f, 0.0145f, 0f,
                     Palette.Alpha(Gained(), 200));

            var payback = PaybackDue != null && PaybackDue();

            Hud.TextRight(payback
                              ? "SOMEBODY'S COMING"
                              : _feed.Following.ToString("N0") + " FOLLOWING",
                          right, PanelTop + 0.0895f, 0.24f,
                          payback ? Palette.Danger : Palette.TextDim, Hud.FontLabel);

            // No underline here. The tab strip's rule closes the masthead, and two full-width
            // accent rules a few hundredths apart on a panel this narrow is a ladder.
            return PanelTop + CardHeight;
        }

        /// <summary>
        /// A bright segment travelling along the accent rule, once every two and a half seconds.
        ///
        /// The whole animation budget of this screen, spent in one place. A feed is a thing that
        /// is supposed to be live and every pixel of it was static, so one moving highlight on
        /// the rule at the top says "this is running" without anything underneath it moving --
        /// which matters, because the thing underneath is text somebody is trying to read.
        ///
        /// Clipped to the panel rather than drawn over it: it enters from off the left edge and
        /// leaves past the right, and a rectangle that starts outside the card would be a bar
        /// across the screen for the frames either side.
        /// </summary>
        private void Sweep(float left, Color edge)
        {
            const int PeriodMs = 2600;

            var t = (Game.GameTime % PeriodMs) / (float)PeriodMs;
            var wide = PanelWidth * 0.20f;

            var from = left - wide + (PanelWidth + wide * 2f) * t;

            var x0 = Math.Max(left, from);
            var x1 = Math.Min(left + PanelWidth, from + wide);

            if (x1 <= x0) return;

            Hud.RectFrom(x0, PanelTop, x1 - x0, 0.0028f,
                         Color.FromArgb(150, 255, 255, 255));
        }

        /// <summary>
        /// The dot that says the feed is on, breathing rather than blinking.
        ///
        /// Blinking is an alarm. This fades between two alphas on a slow sine, which reads as a
        /// light that is on rather than as something demanding to be looked at.
        /// </summary>
        private void Live(float middle, float y)
        {
            const int PeriodMs = 1900;

            var t = (Game.GameTime % PeriodMs) / (float)PeriodMs;
            var glow = 0.5f + 0.5f * (float)Math.Sin(t * Math.PI * 2.0);

            var alpha = (int)(90 + 130 * glow);

            var wide = 0.05f;
            try { wide = Hud.MeasureText("SOCIALS", TitleScale, Hud.FontChaletLondon); }
            catch { /* the estimate will do */ }

            Hud.Disc(middle - wide * 0.5f - 0.010f, y, 0.0038f,
                     Palette.Alpha(Palette.Danger, alpha));
        }

        /// <summary>
        /// The follower colour, flashed for a moment whenever the number goes up.
        ///
        /// A count that changes silently is a count nobody notices changing, and the number
        /// going up is most of why anybody opens this screen.
        /// </summary>
        private Color Gained()
        {
            var now = _feed.Followers;

            if (now != _followersWere)
            {
                if (now > _followersWere) _gainedAt = Game.GameTime;
                _followersWere = now;
            }

            if (_gainedAt == 0) return Palette.Cash;

            var since = Game.GameTime - _gainedAt;
            if (since > 1400) { _gainedAt = 0; return Palette.Cash; }

            // Bright at the top of the flash, back to the money colour by the end of it.
            var t = 1f - since / 1400f;
            var lift = (int)(255 * t);

            return Color.FromArgb(255,
                                  Math.Min(255, Palette.Cash.R + lift),
                                  Math.Min(255, Palette.Cash.G + lift / 3),
                                  Math.Min(255, Palette.Cash.B + lift));
        }

        private int _followersWere = -1;
        private int _gainedAt;

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

        /// <summary>
        /// How new this one is, from 1 the instant it lands to 0 once it has settled in.
        ///
        /// Two different windows because they are two different jobs. The SLIDE is a movement
        /// and has to be over almost before you notice it -- anything you can sit and watch
        /// travel is a screen being slow rather than a post arriving. The GLOW is a marker and
        /// has to outlast the movement, or the one thing it exists to point at is gone before
        /// you have looked up.
        /// </summary>
        private static float Landing(Post post, int windowMs)
        {
            try
            {
                var age = Game.GameTime - post.At;

                // Out of a save. It landed in another session and is not new to anybody.
                if (age < 0 || age >= windowMs) return 0f;

                return 1f - age / (float)windowMs;
            }
            catch
            {
                return 0f;
            }
        }

        private const int SlideMs = 420;
        private const int GlowMs = 6000;

        /// <summary>
        /// How long each card waits behind the one above it, and how long its own move takes.
        ///
        /// Short. A cascade you can sit and watch complete is a screen that is slow to open;
        /// what this is for is the half-second after the button, where a list that assembles
        /// itself reads as a thing being fetched and a list that is simply THERE reads as a
        /// static image somebody drew.
        /// </summary>
        private const int DealStepMs = 38;
        private const int DealMoveMs = 260;

        /// <summary>Where this card is in its own arrival, from 1 to 0.</summary>
        private float Dealing(int slot)
        {
            try
            {
                var since = Game.GameTime - _openedAt - slot * DealStepMs;

                if (since >= DealMoveMs) return 0f;
                if (since <= 0) return 1f;

                return 1f - since / (float)DealMoveMs;
            }
            catch
            {
                return 0f;
            }
        }

        private void DrawPost(float left, float top, Post post, int slot = 0)
        {
            var lines = Lines(post);

            if (post == null || post.By == null) return;

            // IT ARRIVES RATHER THAN APPEARING.
            //
            // Everything below is placed off `left`, so moving that one number carries the
            // whole post with it and nothing has to be threaded through forty draw calls.
            // Squared, so it comes in quickly and settles rather than gliding at a constant
            // speed the whole way, which reads as a slide rather than a thing landing.
            var slide = Landing(post, SlideMs);
            var glow = Landing(post, GlowMs);

            // Two reasons a card can be moving and they do not add up -- a post that arrives
            // in the same instant the screen opens should travel once, not twice as far. The
            // bigger of the two wins and the other is along for the ride.
            var move = Math.Max(slide * slide, Dealing(slot));

            if (move > 0f) left += Hud.ToX(0.045f) * move;

            if (glow > 0f)
            {
                // A wash that burns off. It does the work a NEW badge would do and then stops
                // existing, which a badge cannot -- and it never competes with the about-you
                // rail, because it is gone within six seconds and that one is permanent.
                Hud.RectFrom(left + 0.002f, top, PanelWidth - 0.012f, PostHeight(post) - PostGap,
                             Palette.Alpha(Palette.Accent, (int)(58f * glow)));
            }

            // Anything about you gets a change of ground as well as a rail.
            //
            // A hairline of near-white at the very edge of a narrow panel is genuinely hard to
            // catch while the list is sliding, and sliding is exactly when you need to catch it.
            // The wash is low -- a third of the hub's, because the hub marks one row for as
            // long as a cursor sits on it whereas this is permanent and can cover three posts
            // at once. It sits BEHIND the text and does not inset it, which matters: see the
            // note on the wrap cache in Lines().
            // EVERY POST IS A CARD NOW.
            //
            // It used to be a run of text with a hairline under it, which is a list. The thing
            // that makes a timeline read as a timeline is that each post is a separate object
            // you could pick up -- and that is a shape with an edge and air around it, not a
            // rule between two paragraphs.
            var cardH = PostHeight(post) - PostGap;
            var cardW = PanelWidth - 0.012f;

            Hud.RoundRect(left + 0.002f, top, cardW, cardH, CardRadius, CardFace);

            // A one-pixel lift along the top edge only. A full outline round a dark card on a
            // dark panel is a box drawn twice; a highlight on the top edge alone is the way
            // light actually falls on something raised.
            Hud.RectFrom(left + 0.002f + Hud.ToX(CardRadius), top,
                         cardW - Hud.ToX(CardRadius) * 2f, 0.0008f, CardEdge);

            if (post.AboutYou)
            {
                var h = cardH;

                Hud.RoundRect(left + 0.002f, top, cardW, h, CardRadius,
                              Color.FromArgb(26, 255, 255, 255));

                Hud.RectFrom(left + 0.002f, top + CardRadius * 0.5f, 0.0022f,
                             h - CardRadius, Palette.Accent);

                // Closed on the other three sides as well. A wash with a rail down one edge is
                // a highlight; a wash with a line all the way round it is a card, and the whole
                // point of the thing is that it is a separate object from the post above it.
                var trim = Palette.Alpha(Palette.Accent, 70);

                Hud.RectFrom(left + 0.002f + Hud.ToX(CardRadius), top,
                             cardW - Hud.ToX(CardRadius) * 2f, 0.0010f, trim);

                Hud.RectFrom(left + 0.002f + Hud.ToX(CardRadius), top + h - 0.0010f,
                             cardW - Hud.ToX(CardRadius) * 2f, 0.0010f, trim);
            }

            // Content sits inside the card from here down. One shift rather than a padding
            // term added to every offset below it, which is how those go out of step.
            top += CardPad;

            var cx = left + Pad + Hud.ToX(AvatarSize) * 0.5f;
            var cy = top + 0.004f + AvatarSize * 0.5f;

            // The author's own face, if they have one.
            //
            // The field has always been there and the toasts have always drawn it -- this
            // screen never looked at it, so the same author had a photograph on the right of
            // the screen and a coloured circle with a letter in it here. A logo is the whole
            // difference between a business account and a name.
            // Whether they have a real face rather than a coloured circle with a letter in it.
            // Kept, because it is also the answer to whether they get a tick.
            var pictured = Avatar(post, cx, cy);

            if (!pictured)
            {
                // A ring under the disc, one step darker than it. A flat circle with a letter
                // in it is a placeholder; the same circle with an edge is an avatar, and it
                // costs one more draw.
                Hud.Disc(cx, cy, AvatarSize * 0.5f + 0.0016f, Shade(post.By.Tint, 0.55f));
                Hud.Disc(cx, cy, AvatarSize * 0.5f, post.By.Tint);

                Hud.Text(post.By.Initial, cx, cy - 0.0135f, 0.46f,
                         Color.FromArgb(235, 250, 250, 248), Hud.FontChaletLondon);
            }

            // Still warm. A pip rather than a word: it is on for a few seconds and the eye
            // catches a dot appearing without having to read anything.
            if (glow > 0f)
            {
                Hud.Disc(left + PanelWidth - 0.018f, top + 0.010f, 0.0032f,
                         Palette.Alpha(Palette.Accent, (int)(230f * glow)));
            }

            var textX = left + Pad + Hud.ToX(AvatarSize) + 0.010f;
            var y = top + 0.002f;

            // Name, then handle and stamp trailing it in the quiet face. Measured so the handle
            // sits directly after the name whatever the name happens to be.
            Hud.Text(post.By.Name, textX, y, NameScale, Palette.Text, Hud.FontChaletLondon,
                     centre: false);

            var nameWidth = 0.06f;
            try { nameWidth = Hud.MeasureText(post.By.Name, NameScale, Hud.FontChaletLondon); }
            catch { /* the estimate above will do */ }

            var tail = textX + nameWidth + 0.006f;

            // A tick for anybody with a real photograph, as well as for anybody flagged.
            //
            // A display picture in this feed means a contact dictionary the game ships, which
            // means a story character -- there is no way to have one by accident. So the two
            // questions "is this somebody" and "does this somebody have a face" have the same
            // answer, and the picture is the more reliable half of it: it is a fact about the
            // game's own data rather than a flag somebody had to remember to set.
            if (post.By.Verified || pictured)
            {
                // A shape, not a dot. The mark was a small white disc immediately followed by
                // the "  ·  " in the handle string -- a dot, a gap, then another dot, which
                // reads as punctuation rather than as a badge.
                //
                // On a blue disc now, which is the one thing on this screen that is not saying
                // something about you.
                const float th = 0.014f;
                var icx = tail + Hud.ToX(th) * 0.5f;

                Hud.Disc(icx, y + 0.0098f, 0.0068f, Palette.Verified);

                if (!Hud.File("tick.png", icx, y + 0.0095f, th * 0.72f, 0f,
                              Color.FromArgb(255, 18, 20, 22)))
                {
                    // No tick art in this install, so the disc says it on its own.
                    Hud.Disc(icx, y + 0.0098f, 0.0028f, Color.FromArgb(255, 18, 20, 22));
                }

                tail += Hud.ToX(th) + 0.004f;
            }

            Hud.Text(post.By.Handle + "  ·  " + SocialFeed.Ago(post.At), tail, y + 0.0045f,
                     StampScale,
                     Palette.TextDim, Hud.FontLabel, centre: false);

            y += MetaHeight;

            foreach (var line in lines)
            {
                // Through Emoji, which is a plain Hud.Text for any line without a picture in
                // it -- which is most of them -- and runs of text with sprites between them
                // for the ones that have.
                Emoji.Draw(line, textX, y, BodyScale, Palette.Text, Hud.FontBody);
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
            var ok = Metric("reply.png", post.RepliesNow, textX, y)
                   & Metric("repost.png", post.RepostsNow, textX + MetricPitch, y)
                   & Metric("like.png", post.LikesNow, textX + MetricPitch * 2f, y);

            if (!ok)
            {
                Hud.Text(post.RepliesNow + "   REPLIES        " + post.RepostsNow + "   REPOSTS        " +
                         post.LikesNow + "   LIKES",
                         textX, y, 0.235f, Color.FromArgb(150, 150, 158, 152),
                         Hud.FontLabel, centre: false);
            }

            // No divider. The gap between two cards is the divider, and drawing a rule in it
            // as well would put a line through the middle of the space that separates them.
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

            // The card's own padding is part of the post's height, so the gap between two of
            // them stays PostGap however much is inside either one.
            return CardPad * 2f + MetaHeight + lines * LineHeight + MetricsHeight + PostGap;
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

                // Measured with the pictures counted at the size they will DRAW at, not at
                // the width of ":fire:" as six characters -- otherwise a line wraps against a
                // width that has nothing to do with what ends up on screen.
                float measured;
                try { measured = Emoji.Measure(candidate, scale, Hud.FontBody); }
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
