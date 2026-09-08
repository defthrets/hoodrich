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
        /// <summary>
        /// Sized in HEIGHT fractions and converted, so the column keeps its shape: a phone
        /// app's column on a widescreen, and still one on an ultrawide rather than a
        /// letterbox with a sentence a metre long across it.
        /// </summary>
        private const float PanelWidthH = 0.66f;
        private static float PanelWidth => Hud.ToX(PanelWidthH);
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
        /// The display name, in one place because two things measure it.
        ///
        /// Up from 0.34 against a body at 0.315. Those two numbers are close enough that the
        /// eye reads a post as one undifferentiated block and has to actually parse it to find
        /// out who is talking -- which is the whole reason the screen looked like a list of
        /// sentences rather than a feed. A name has to win its line.
        /// </summary>
        private const float NameScale = 0.34f;

        /// <summary>The handle and the stamp. Down, and further out of the way.</summary>
        private const float StampScale = 0.26f;

        private const int OpenGraceMs = 220;

        /// <summary>Engagement art. The one size in this mod proven to render these files.</summary>
        private const float MetricIcon = 0.016f;

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

        /// <summary>The picture on a composer or diss row.</summary>
        private const float RowArt = 0.017f;


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
            new Sayable { Label = "Where you're posted up", Set = "YouWhereAt",
                          Head = "WHERE YOU'RE AT",
                          Line = "Tells the block where to find you. More custom for a while, and more eyes." },
            new Sayable { Label = "Drop a diss track", Set = "YouDissTrack",
                          Head = "A DISS TRACK",
                          Line = "Named and recorded. They don't let a track go the way they let a post go." },
            new Sayable { Label = "Big up the set", Set = "YouBigUp",
                          Head = "FOR THE SET",
                          Line = "Costs nothing. The block likes to hear it, and so do yours." },

            new Sayable { Label = "Put a price on it", Set = "YouPriceDrop",
                          Head = "CHEAP TODAY",
                          Line = "Undercut the block and they'll queue for it. Yours won't thank you, and neither will the corner." },

            new Sayable { Label = "Call the homies out", Set = "YouCallHomies",
                          Head = "WHO'S OUTSIDE",
                          Line = "Asks in public instead of ringing round. Same result, more eyes." },

            new Sayable { Label = "Post the takings", Set = "YouFlex",
                          Head = "THE TAKINGS",
                          Line = "Everybody sees a man with money. Everybody includes the ones counting it." },

            new Sayable { Label = "Say sorry to somebody", Set = "YouTruce",
                          Head = "CALLING IT OFF",
                          Line = "Names the set you're worst with and takes it back. Costs you standing with your own." },

            new Sayable { Label = "Post a memorial", Set = "YouRIP",
                          Head = "FOR SOMEBODY",
                          Line = "Nobody argues with grief. Yours remember it and the block goes quiet for a minute." },

            new Sayable { Label = "Buy some followers", Set = "YouBought",
                          Head = "BOUGHT AND PAID FOR",
                          Line = "Two and a half grand for numbers that aren't real. Somebody always notices." },
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

        /// <summary>The row the cursor was on before this one, and when it moved. See Theme.Lit.</summary>
        private int _lastPick = -1;
        private int _pickedAt;

        /// <summary>The cursor frame that glides between rows. See UI.Glide.</summary>
        private readonly Glide _glide = new Glide();

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
            _lastPick = -1;
            _pickedAt = Game.GameTime;
            _glide.Reset();
            _tabAt = Game.GameTime;
            _holdFrom = 0;
            _holdArmed = false;
            _holdSpent = false;
            _note = null;
            _nudge = null;

            BuildLists();

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

            _lastPick = _pick;
            _pickedAt = Game.GameTime;

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

            // A new tab is a new list: the plate comes up under its first row with nothing
            // going down, and the frame glides over from wherever it was.
            _pick = 0;
            _lastPick = -1;
            _pickedAt = Game.GameTime;
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
        //
        // A TIMELINE, DRAWN THE WAY THE REAL ONES ARE. This is the one screen in the mod that
        // does not wear the house style, on purpose: it is a phone app pretending to be the
        // bird app, and the bird app in the stash house's letterhead was a spreadsheet with
        // avatars. Black ground, a hairline between posts instead of a card round each one,
        // the name in the reading face with a blue tick after it, the handle and the age in
        // grey on the same line, three grey glyphs with numbers under the words. Nothing is
        // raised, nothing has a shadow, nothing is rounded.
        //
        // AND IT IS CHEAP, WHICH IS NOT A SIDE EFFECT. The cards cost about ten rectangles
        // each and every blue tick was twenty more (a disc is a rectangle per pixel row), so
        // a screenful came to five hundred and the game dropped the tail of the frame -- the
        // phone flickering with everything on was this screen. A post is now one hairline;
        // a whole screenful is a few dozen rectangles.

        /// <summary>The app's own palette. The bird app's dark theme, near enough, and nobody else's.</summary>
        private static readonly Color Ground = Color.FromArgb(246, 0, 0, 0);
        private static readonly Color Hairline = Color.FromArgb(255, 47, 51, 54);
        private static readonly Color Ink = Color.FromArgb(255, 231, 233, 234);
        private static readonly Color Quiet = Color.FromArgb(255, 113, 118, 123);
        private static readonly Color Blue = Color.FromArgb(255, 29, 155, 240);
        private static readonly Color Pink = Color.FromArgb(255, 249, 24, 128);
        private static readonly Color Green = Color.FromArgb(255, 0, 186, 124);

        /// <summary>The bar, the profile block, the tabs, the bottom bar: the fixed furniture, top to bottom.</summary>
        private const float TopBarH = 0.050f;
        private const float ProfileH = 0.104f;
        private const float TabsH = 0.042f;
        private const float BottomH = 0.038f;

        /// <summary>Inside a post: air above the name and below the figures, the name line, the figures line.</summary>
        private const float PostPad = 0.009f;
        private const float MetaH = 0.026f;
        private const float ActionsH = 0.026f;

        /// <summary>What the tabs say. TabNames is the code's names for them; these are the app's.</summary>
        private static readonly string[] Labels = { "All", "About you", "Post", "Diss" };

        public void Draw()
        {
            if (!IsOpen) return;

            var left = 0.5f - PanelWidth * 0.5f;
            var right = left + PanelWidth;
            var x = left + Pad;
            var inner = right - Pad;

            // One rectangle. Black because the app's is, and because a rounded panel with a
            // wash on it is the house style this screen is deliberately not in.
            Hud.RectFrom(left, PanelTop, PanelWidth, PanelHeight, Ground);

            Count();

            // The cursor frame in the app's blue rather than the panels' gold.
            _glide.RimInk = Blue;
            _glide.GlowInk = Blue;
            _glide.Begin();

            var y = TopBar(left, x, inner);
            y = Profile(left, x, inner, y);
            y = Tabs(left, y);

            var bottom = PanelTop + PanelHeight - BottomH;

            if (!IsFeedTab)
            {
                Action(left, x, inner, y, bottom);
                BottomBar(left, x, inner);
                _glide.Draw();
                return;
            }

            var feedTop = y;
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
            else Rail(right, feedTop, bottom, count, shown);

            BottomBar(left, x, inner);
            _glide.Draw();
        }

        // ---- the furniture -----------------------------------------------------

        /// <summary>The bar along the top: your face, the app's mark in the middle, the light that says it is on.</summary>
        private float TopBar(float left, float x, float inner)
        {
            var top = PanelTop;
            var cy = top + TopBarH * 0.5f;

            Face(x + Hud.ToX(0.026f) * 0.5f, cy, 0.026f);

            Hud.File("socials.png", left + PanelWidth * 0.5f, cy, 0.028f, 0f, Ink);

            Live(inner - Hud.ToX(0.004f), cy);

            Hud.RectFrom(left, top + TopBarH - 0.0012f, PanelWidth, 0.0012f, Hairline);

            return top + TopBarH;
        }

        /// <summary>
        /// Your profile, the way the app lays one out: the face, the name with its tick, the
        /// handle under it, then "118 Following  1,520 Followers" with the numbers in white
        /// and the words in grey. The post count on the right, where the profile keeps it --
        /// or, when somebody is on their way, that, in the colour of the tab that caused it.
        /// </summary>
        private float Profile(float left, float x, float inner, float top)
        {
            const float size = 0.054f;

            Face(x + Hud.ToX(size) * 0.5f, top + 0.010f + size * 0.5f, size);

            var tx = x + Hud.ToX(size) + 0.010f;

            Hud.Text(_feed.DisplayName, tx, top + 0.006f, 0.40f, Ink, Hud.FontChaletLondon, centre: false);

            var nw = Measure(_feed.DisplayName, 0.40f, Hud.FontChaletLondon, 0.06f);
            Badge(tx + nw + 0.006f, top + 0.024f, 0.017f);

            Hud.Text(_feed.Handle, tx, top + 0.036f, 0.27f, Quiet, Hud.FontChaletLondon, centre: false);

            var ly = top + 0.064f;
            var fx = Stat(tx, ly, _feed.Following.ToString("N0"), "Following", Ink);
            Stat(fx + 0.014f, ly, _feed.Followers.ToString("N0"), "Followers", Gained());

            var payback = PaybackDue != null && PaybackDue();
            var n = _tally[0];

            Hud.TextRight(payback ? "Somebody's coming" : n + (n == 1 ? " post" : " posts"),
                          inner, top + 0.010f, 0.25f, payback ? Pink : Quiet, Hud.FontChaletLondon);

            Hud.RectFrom(left, top + ProfileH - 0.0012f, PanelWidth, 0.0012f, Hairline);

            return top + ProfileH;
        }

        /// <summary>A number in white and its word in grey, on one line. Returns where the next starts.</summary>
        private static float Stat(float x, float y, string number, string word, Color ink)
        {
            Hud.Text(number, x, y, 0.28f, ink, Hud.FontChaletLondon, centre: false);

            var w = Measure(number, 0.28f, Hud.FontChaletLondon, 0.03f);

            Hud.Text(word, x + w + 0.004f, y, 0.28f, Quiet, Hud.FontChaletLondon, centre: false);

            return x + w + 0.004f + Measure(word, 0.28f, Hud.FontChaletLondon, 0.05f);
        }

        /// <summary>The underline under the tab you are on, which travels rather than teleports.</summary>
        private readonly Eased _tabX = new Eased();
        private readonly Eased _tabW = new Eased();

        /// <summary>
        /// Four tabs across the whole width, evenly, the app's way: the one you are on in
        /// white with a blue line under it, the rest in grey. DISS is pink whether or not you
        /// are on it, because it is the one that starts fights, and an empty feed tab is
        /// fainter so it says so before you press it.
        /// </summary>
        private float Tabs(float left, float top)
        {
            var w = PanelWidth / Labels.Length;
            var ty = top + 0.010f;

            for (var i = 0; i < Labels.Length; i++)
            {
                var here = i == _tab;
                var empty = i < 2 && _tally[i] == 0;

                var ink = here ? (i == TabDiss ? Pink : Ink)
                    : i == TabDiss ? Palette.Alpha(Pink, 200)
                    : empty ? Palette.Alpha(Quiet, 150)
                    : Quiet;

                Hud.Text(Labels[i], left + w * (i + 0.5f), ty, 0.30f, ink, Hud.FontChaletLondon);
            }

            var wide = Measure(Labels[_tab], 0.30f, Hud.FontChaletLondon, 0.03f) + 0.004f;
            var markX = _tabX.To(left + w * (_tab + 0.5f) - wide * 0.5f, 13f);
            var markW = _tabW.To(wide, 13f);

            Hud.RectFrom(markX, top + TabsH - 0.0046f, markW, 0.0034f, _tab == TabDiss ? Pink : Blue);

            Hud.RectFrom(left, top + TabsH - 0.0012f, PanelWidth, 0.0012f, Hairline);

            return top + TabsH;
        }

        /// <summary>The keys, in the app's grey, and how far above you the feed goes on.</summary>
        private void BottomBar(float left, float x, float inner)
        {
            var top = PanelTop + PanelHeight - BottomH;

            Hud.RectFrom(left, top, PanelWidth, 0.0012f, Hairline);

            var pad = Hud.OnPad;
            var ud = pad ? "D-pad up/down" : "Up/down";
            var lr = pad ? "D-pad left/right" : "Left/right";
            var ok = pad ? "A" : "Enter";
            var back = pad ? "B" : "Backspace";

            string keys;

            if (Progress() > 0f) keys = "Keep holding  ·  let go to stop";
            else if (IsFeedTab) keys = ud + " scroll  ·  " + lr + " tabs  ·  " + back + " out";
            else if (Rows() == 0) keys = lr + " tabs  ·  " + back + " out";
            else if (!_live) keys = ud + " pick  ·  " + back + " out";
            else if (_tab == TabPost) keys = ud + " pick  ·  " + ok + " post  ·  " + back + " out";
            else keys = ud + " pick  ·  hold " + ok + " send  ·  " + back + " out";

            Hud.Text(keys, x, top + 0.010f, 0.245f, Quiet, Hud.FontChaletLondon, centre: false);

            // The other half of the scroll indicator. HoldPosition bumps the scroll when new
            // posts land so the one you are reading stays put, which means posts silently
            // pile up above you; _scroll is that number.
            if (IsFeedTab && _scroll > 0)
            {
                Hud.TextRight(_scroll + " above", inner, top + 0.010f, 0.245f, Quiet, Hud.FontChaletLondon);
            }
        }

        /// <summary>Where you are in the feed, down the right edge. Nothing drawn when it all fits.</summary>
        private void Rail(float right, float top, float bottom, int count, int shown)
        {
            if (shown <= 0 || count <= shown) return;

            var rx = right - Hud.ToX(0.0044f);
            var rw = Hud.ToX(0.0022f);
            var h = bottom - top;

            Hud.RectFrom(rx, top, rw, h, Palette.Alpha(Quiet, 60));

            var thumbH = Math.Max(h * 0.06f, h * (shown / (float)count));

            var pos = _scroll / (float)(count - shown);
            if (pos < 0f) pos = 0f;
            if (pos > 1f) pos = 1f;

            Hud.RectFrom(rx, top + (h - thumbH) * pos, rw, thumbH, Palette.Alpha(Quiet, 200));
        }

        /// <summary>An empty tab, saying which kind of empty it is, centred in the void.</summary>
        private void Nothing(float top)
        {
            var head = _tab == 0 ? "Nothing here yet" : "Nobody's said your name";
            var sub = _tab == 0 ? "Go and do something." : "Give them something to talk about.";

            var ey = top + 0.150f;

            Hud.File("reply.png", 0.5f, ey, 0.044f, 0f, Palette.Alpha(Quiet, 90));
            Hud.Text(head, 0.5f, ey + 0.036f, 0.36f, Ink, Hud.FontChaletLondon);
            Hud.Text(sub, 0.5f, ey + 0.068f, 0.28f, Quiet, Hud.FontChaletLondon);
        }

        // ---- the composer and the diss list --------------------------------------

        /// <summary>
        /// The composer and the diss list, laid out from wherever the tabs finished.
        ///
        /// A list of things you can say rather than a box you type in, because there is no
        /// keyboard here -- but dressed as the app's compose sheet: the question at the top,
        /// the rows under it, the one you are on with the app's grey ground and a blue edge.
        /// </summary>
        private void Action(float left, float x, float inner, float top, float bottom)
        {
            var rows = Rows();
            var head = _tab == TabPost ? "What's happening?" : "Who are you naming?";

            Hud.Text(head, x, top + 0.008f, 0.34f, Quiet, Hud.FontChaletLondon, centre: false);

            var first = top + 0.040f;
            var t = Progress();

            var grown = Theme.Grown(_pickedAt);

            // Whatever fits between here and the note strip.
            var maxRows = (int)((bottom - 0.116f - first) / RowPitch);
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

                    // THE BUTTON NAMES WHAT IS ACTUALLY GOING OUT. The row is rewritten from
                    // the same resolver that picks the post, so the two cannot disagree.
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

                    value = "Free";
                    valueInk = Green;
                }
                else
                {
                    var gang = _dissList[i];

                    label = gang.Name;
                    tick = gang.Colour;

                    // A four-character tag that is readable beats a name that is cut off. It is
                    // a bad thing to aim a war at a name you cannot read.
                    if (Measure(label, 0.30f, Hud.FontChaletLondon, 0f) > PanelWidth * 0.62f)
                    {
                        label = gang.Tag;
                    }

                    // Where you stand with them, as a word and the number, so a few disses
                    // visibly move it and a quiet while visibly moves it back.
                    var st = Crew == null ? null : Crew.StandingFor(gang.Id);
                    var rep = st == null ? 0f : st.Rep;
                    var beefing = Crew != null && Crew.Beefing(gang.Id);

                    value = (beefing ? "Beef" : rep <= -10f ? "Sour" : rep < 0f ? "Cold" : rep >= 20f ? "Tight" : "Cool")
                            + "  " + rep.ToString("0");
                    valueInk = beefing ? Pink : rep < 0f ? Palette.Warn : Quiet;
                }

                Row(left, x, inner, rowY, here, !_live, Theme.Lit(i, _pick, _lastPick, grown),
                    tick, label, value, valueInk, here && t > 0f ? t : 0f, Art(i));
            }

            if (rows == 0)
            {
                Hud.Text(_tab == TabDiss
                             ? "Nobody worth the trouble"
                             : "There's nobody you're not already with",
                         x + 0.004f, first + 0.006f, 0.30f, Palette.Alpha(Quiet, 200),
                         Hud.FontChaletLondon, centre: false);
            }

            var after = first + Math.Max(1, draw) * RowPitch;

            NoteStrip(left, x, inner, after + NoteGap, t);

            if (_tab == TabPost) Yours(left, x, after + NoteGap + NoteStripH, bottom);
        }

        /// <summary>How tall the note strip is: its rule, its heading and its three lines.</summary>
        private const float NoteStripH = 0.100f;

        /// <summary>
        /// The picture for one row of whichever list is up: the sets have their own art, and
        /// the things you can say get the phone.
        /// </summary>
        private string Art(int i)
        {
            if (_tab == TabPost) return "mobile.png";

            if (i < 0 || i >= _dissList.Count) return "";

            var id = _dissList[i].Id;
            return string.IsNullOrEmpty(id) ? "" : "gang_" + id.ToLowerInvariant() + ".png";
        }

        /// <summary>
        /// Everything you have said, under the box you say it in, newest first -- drawn with
        /// the feed's own post, because your posts are not a different kind of post.
        /// </summary>
        private void Yours(float left, float x, float top, float bottom)
        {
            if (_feed == null) return;

            Hud.Text("Your posts", x, top + 0.004f, 0.30f, Quiet, Hud.FontChaletLondon, centre: false);
            Hud.RectFrom(left, top + 0.030f, PanelWidth, 0.0012f, Hairline);

            var y = top + 0.032f;
            var floor = bottom - 0.004f;
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

            Hud.Text("Nothing yet. Say something.", x + 0.004f, y + 0.006f, 0.30f,
                     Palette.Alpha(Quiet, 200), Hud.FontChaletLondon, centre: false);
        }

        /// <summary>
        /// One row of the list: the app's grey ground under the one the cursor is on with a
        /// blue edge, the set's colour as a tick, its art, its name, and what it costs on the
        /// right. The hold fills a bar along the bottom in the colour of the tab.
        /// </summary>
        private void Row(float left, float x, float inner, float top, bool here, bool dead, float lit,
                         Color tick, string label, string value, Color valueInk, float fill,
                         string art = "")
        {
            // Under a dead row the cursor is still visibly SOMEWHERE, but visibly on something
            // inert: the ground at a third of its strength.
            var under = dead ? lit * 0.35f : lit;

            if (under > 0.01f)
            {
                Hud.RectFrom(left, top, PanelWidth, RowHeight, Color.FromArgb((int)(18f * under), 255, 255, 255));
                Hud.RectFrom(left, top, Hud.ToX(0.0024f), RowHeight,
                             Palette.Alpha(_tab == TabDiss ? Pink : Blue, (int)(255f * under)));
            }

            if (here) _glide.Target(left, top, PanelWidth, RowHeight);

            var textX = x + 0.004f;

            if (tick.A > 0)
            {
                // Their own colour, which is the one identity mark that costs no width.
                Hud.RectFrom(textX, top + 0.006f, Hud.ToX(0.0022f), 0.018f, dead ? Palette.Alpha(tick, 90) : tick);
                textX += 0.008f;
            }

            if (!string.IsNullOrEmpty(art) &&
                Hud.File(art, textX + Hud.ToX(RowArt) * 0.5f, top + 0.015f, RowArt, 0f,
                         dead ? Palette.Alpha(Quiet, 160) : Ink))
            {
                textX += Hud.ToX(RowArt) + 0.006f;
            }

            var ink = dead ? Palette.Alpha(Quiet, 200) : here ? Ink : Palette.Alpha(Ink, 190);

            Hud.Text(label, textX, top + 0.006f, 0.30f, ink, Hud.FontChaletLondon, centre: false);

            if (!string.IsNullOrEmpty(value))
            {
                Hud.TextRight(value, inner, top + 0.008f, 0.25f, dead ? Palette.Alpha(Quiet, 200) : valueInk,
                              Hud.FontChaletLondon);
            }

            if (fill > 0f)
            {
                Hud.RectFrom(left, top + RowHeight - 0.0024f, PanelWidth * fill, 0.0024f,
                             _tab == TabDiss ? Pink : Blue);
            }
        }

        /// <summary>
        /// What this row actually does, rewritten every frame from the row the cursor is on,
        /// under the list, because the moment that matters is the moment your thumb is on
        /// the key. A result or a refusal replaces the lines and keeps the head, so the set
        /// you are aiming at never leaves the screen while you are aiming at it.
        /// </summary>
        private void NoteStrip(float left, float x, float inner, float top, float t)
        {
            Hud.RectFrom(left, top, PanelWidth, 0.0012f, Hairline);

            // The hold again, across the words it is about, so the bar and the warning read as
            // one object rather than two.
            if (t > 0f)
            {
                Hud.RectFrom(left, top - 0.0024f, PanelWidth * t, 0.0024f, _tab == TabDiss ? Pink : Blue);
            }

            var headTxt = "";
            var headInk = Quiet;
            var icon = "";

            string l1 = "", l2 = "", l3 = "";
            Color i1 = Quiet, i2 = Quiet, i3 = Quiet;

            if (!_live)
            {
                headTxt = "Not right now";
                headInk = Pink;
                l1 = _why;
            }
            else if (_tab == TabPost)
            {
                headTxt = Says[_pick].Head;
                icon = "reply.png";
                l1 = Says[_pick].Line;

                // The strip explains the row above it, so it has to be told the same thing the
                // row was.
                if (_pick == 0 && Topic != null)
                {
                    try
                    {
                        var topic = Topic();

                        if (topic != null && topic.Length > 2 && topic[2].Length > 0)
                        {
                            headTxt = topic[2];

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

                headTxt = "Naming " + g.Name;
                headInk = Pink;
                icon = "megaphone.png";

                l1 = "They answer -- on here, within the minute.";
                l2 = "Then -- somebody comes to find you.";
                i2 = Pink;
                l3 = string.IsNullOrEmpty(g.TurfHint) ? "Say it where they can see it" : g.TurfHint;
                i3 = Palette.Alpha(Quiet, 190);
            }

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

            Hud.Text(Heading(headTxt), hx, top + 0.008f, 0.29f, headInk, Hud.FontChaletLondon, centre: false);

            // The mark slot: the whole feedback channel for the hold, three states and no more.
            var mark = "";
            var markInk = Blue;

            if (t > 0f) mark = "Release to stop";
            else if (_nudge != null && Game.GameTime - _nudgeAt < NudgeMs)
            {
                mark = _nudge;
                markInk = Pink;
            }
            else _nudge = null;

            if (mark != "")
            {
                Hud.TextRight(mark, inner, top + 0.008f, 0.25f, markInk, Hud.FontChaletLondon);
            }

            var w = inner - x;

            if (l1 != "") Hud.Text(Hud.Fit(l1, w, 0.27f, Hud.FontBody), x, top + 0.032f, 0.27f, i1, Hud.FontBody, centre: false);
            if (l2 != "") Hud.Text(Hud.Fit(l2, w, 0.27f, Hud.FontBody), x, top + 0.054f, 0.27f, i2, Hud.FontBody, centre: false);
            if (l3 != "") Hud.Text(Hud.Fit(l3, w, 0.27f, Hud.FontBody), x, top + 0.076f, 0.27f, i3, Hud.FontBody, centre: false);
        }

        /// <summary>The composer's headings were written in capitals for the condensed face; the app's face wants a sentence.</summary>
        private static string Heading(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s != s.ToUpperInvariant()) return s;

            var lower = s.ToLowerInvariant();
            return char.ToUpperInvariant(lower[0]) + lower.Substring(1);
        }

        // ---- a post -------------------------------------------------------------

        /// <summary>
        /// How new this one is, from 1 the instant it lands to 0 once it has settled in.
        ///
        /// Two different windows because they are two different jobs. The SLIDE is a movement
        /// and has to be over almost before you notice it; the GLOW is a marker and has to
        /// outlast the movement, or the one thing it exists to point at is gone before you
        /// have looked up.
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
        /// How long each post waits behind the one above it on the way in, and how long its
        /// own move takes. Short: a list that assembles itself in the half-second after the
        /// button reads as a thing being fetched.
        /// </summary>
        private const int DealStepMs = 38;
        private const int DealMoveMs = 260;

        /// <summary>Where this post is in its own arrival, from 1 to 0.</summary>
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

        /// <summary>
        /// One post, the app's way: the face on the left, the name with its tick and then the
        /// handle and the age in grey on the same line, the words, then reply, repost and like
        /// as grey glyphs with grey numbers spread under them, and a hairline before the next.
        /// Something about you gets the faint ground the app gives a highlighted post and a
        /// blue edge; something new lands on a wash of blue that burns off.
        /// </summary>
        private void DrawPost(float left, float top, Post post, int slot = 0)
        {
            if (post == null || post.By == null) return;

            var lines = Lines(post);

            var slide = Landing(post, SlideMs);
            var glow = Landing(post, GlowMs);

            // Two reasons a post can be moving and they do not add up: the bigger of the two
            // wins and the other is along for the ride.
            var move = Math.Max(slide * slide, Dealing(slot));
            var shift = move > 0f ? Hud.ToX(0.045f) * move : 0f;

            var h = PostHeight(post) - 0.0012f;

            if (glow > 0f)
            {
                Hud.RectFrom(left, top, PanelWidth, h, Palette.Alpha(Blue, (int)(34f * glow)));
            }

            if (post.AboutYou)
            {
                Hud.RectFrom(left, top, PanelWidth, h, Color.FromArgb(14, 255, 255, 255));
                Hud.RectFrom(left, top, Hud.ToX(0.0024f), h, Blue);
            }

            var x = left + Pad + shift;
            var cx = x + Hud.ToX(AvatarSize) * 0.5f;
            var cy = top + PostPad + AvatarSize * 0.5f;

            if (!Avatar(post, cx, cy))
            {
                // A tinted square with the initial on it until the face arrives. One rectangle.
                Hud.RectFrom(cx - Hud.ToX(AvatarSize) * 0.5f, cy - AvatarSize * 0.5f,
                             Hud.ToX(AvatarSize), AvatarSize, post.By.Tint);

                Hud.Text(post.By.Initial, cx, cy - 0.0135f, 0.46f,
                         Color.FromArgb(235, 250, 250, 248), Hud.FontChaletLondon);
            }

            var textX = x + Hud.ToX(AvatarSize) + 0.010f;
            var inner = left + PanelWidth - Pad;
            var y = top + PostPad;

            Hud.Text(post.By.Name, textX, y, NameScale, Ink, Hud.FontChaletLondon, centre: false);

            var tail = textX + Measure(post.By.Name, NameScale, Hud.FontChaletLondon, 0.06f) + 0.005f;

            if (post.By.Verified)
            {
                Badge(tail, y + 0.011f, 0.015f);
                tail += Hud.ToX(0.015f) + 0.005f;
            }

            // The handle gives way to the column, never the name.
            var stamp = Hud.Fit(post.By.Handle + " · " + SocialFeed.Ago(post.At),
                                Math.Max(0.02f, inner - tail), StampScale, Hud.FontChaletLondon);

            Hud.Text(stamp, tail, y + 0.0035f, StampScale, Quiet, Hud.FontChaletLondon, centre: false);

            y += MetaH;

            foreach (var line in lines)
            {
                // Through Emoji, which is a plain Hud.Text for any line without a picture in
                // it and runs of text with sprites between them for the ones that have.
                Emoji.Draw(line, textX, y, BodyScale, Ink, Hud.FontBody);
                y += LineHeight;
            }

            y += 0.004f;

            var span = inner - textX;

            Metric("reply.png", "replies", post.RepliesNow, textX, y);
            Metric("repost.png", "reposts", post.RepostsNow, textX + span * 0.30f, y);
            Metric("like.png", "likes", post.LikesNow, textX + span * 0.60f, y);

            Hud.RectFrom(left, top + h, PanelWidth, 0.0012f, Hairline);
        }

        /// <summary>One engagement figure: the glyph, then the number, both in the app's grey. The word only if the art is missing.</summary>
        private static void Metric(string file, string word, int value, float x, float y)
        {
            var drawn = Hud.File(file, x + Hud.ToX(MetricIcon) * 0.5f, y + 0.0075f, MetricIcon, 0f,
                                 Palette.Alpha(Quiet, 210));

            Hud.Text(drawn ? value.ToString() : value + " " + word,
                     drawn ? x + Hud.ToX(MetricIcon) + 0.005f : x, y, 0.25f, Quiet, Hud.FontChaletLondon,
                     centre: false);
        }

        /// <summary>
        /// How tall one post is. DrawPost MUST lay out inside this or posts overlap: air, the
        /// name line, the words, a breath, the figures, air, and the hairline is the last
        /// slice of the air.
        /// </summary>
        private float PostHeight(Post post)
        {
            var lines = Math.Max(1, Lines(post).Count);

            return PostPad + MetaH + lines * LineHeight + 0.004f + ActionsH + PostPad;
        }

        // ---- faces and marks ----------------------------------------------------

        /// <summary>Your own face at a size, or a square with your initial until the render lands.</summary>
        private void Face(float cx, float cy, float size)
        {
            if (MugshotReady())
            {
                Hud.Sprite(_mugshotTxd, _mugshotTxd, cx, cy, Hud.ToX(size), size, 0f, Color.White);
                return;
            }

            Hud.RectFrom(cx - Hud.ToX(size) * 0.5f, cy - size * 0.5f, Hud.ToX(size), size,
                         Color.FromArgb(255, 58, 72, 60));

            var initial = string.IsNullOrEmpty(_feed.DisplayName)
                ? "F"
                : _feed.DisplayName.Substring(0, 1).ToUpperInvariant();

            Hud.Text(initial, cx, cy - size * 0.36f, size * 12f, Ink, Hud.FontChaletLondon);
        }

        /// <summary>The blue tick. A sprite, so it costs no rectangles at all.</summary>
        private static void Badge(float x, float cy, float h)
        {
            Hud.File("tick.png", x + Hud.ToX(h) * 0.5f, cy, h, 0f, Blue);
        }

        /// <summary>The dot that says the feed is on, breathing rather than blinking. One rectangle.</summary>
        private static void Live(float x, float y)
        {
            const int PeriodMs = 1900;

            var t = (Game.GameTime % PeriodMs) / (float)PeriodMs;
            var glow = 0.5f + 0.5f * (float)Math.Sin(t * Math.PI * 2.0);

            Hud.RectFrom(x - Hud.ToX(0.0028f), y - 0.0028f, Hud.ToX(0.0056f), 0.0056f,
                         Palette.Alpha(Green, (int)(110 + 130 * glow)));
        }

        /// <summary>Measured through the game, or the caller's guess if it will not say.</summary>
        private static float Measure(string text, float scale, int font, float fallback)
        {
            try { return Hud.MeasureText(text, scale, font); }
            catch { return fallback; }
        }

        /// <summary>
        /// The follower figure, flashed blue for a moment whenever it goes up. A count that
        /// changes silently is a count nobody notices changing, and the number going up is
        /// most of why anybody opens this screen.
        /// </summary>
        private Color Gained()
        {
            var now = _feed.Followers;

            if (now != _followersWere)
            {
                if (now > _followersWere) _gainedAt = Game.GameTime;
                _followersWere = now;
            }

            if (_gainedAt == 0) return Ink;

            var since = Game.GameTime - _gainedAt;
            if (since > 1400) { _gainedAt = 0; return Ink; }

            return Theme.Lerp(Blue, Ink, since / 1400f);
        }

        private int _followersWere = -1;
        private int _gainedAt;

        /// <summary>
        /// Draws the author's picture, and says whether it managed to.
        ///
        /// A named character with a contact photo the game ships gets that; a business gets
        /// nothing made for it (the face factory photographs a ped, and a cab company posting
        /// over somebody's holiday snap is wrong); everybody else gets one made -- see
        /// UI.Headshots. Asking for it is also what keeps it in the cache.
        /// </summary>
        private bool Avatar(Post post, float cx, float cy)
        {
            if (post.By == null) return false;

            var pic = post.By.Pic;

            if (!string.IsNullOrEmpty(pic))
            {
                if (!Hud.EnsureTextureDict(pic))
                {
                    Grumble(pic);
                    return false;
                }

                Hud.Sprite(pic, pic, cx, cy, Hud.ToX(AvatarSize), AvatarSize, 0f, Color.White);
                return true;
            }

            if (post.By.IsOrg) return false;

            var made = Headshots.Txd(post.By.Handle);

            if (string.IsNullOrEmpty(made))
            {
                Headshots.Want(post.By.Handle, post.By.Gang, post.By.Gender);
                return false;
            }

            // The letter while the game says the picture is not there. See Headshots.Ready.
            if (!Headshots.Ready(post.By.Handle)) return false;

            Hud.Sprite(made, made, cx, cy, Hud.ToX(AvatarSize), AvatarSize, 0f, Color.White);
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

        // ---- the words ----------------------------------------------------------

        /// <summary>
        /// Wrapped once and remembered. Wrapping measures text through the game, a native call
        /// per word per line, and the text never changes after it is written.
        /// </summary>
        private List<string> Lines(Post post)
        {
            List<string> cached;
            if (_wrapped.TryGetValue(post.Body, out cached)) return cached;

            var width = PanelWidth - Pad * 2f - Hud.ToX(AvatarSize) - 0.010f;
            var lines = Wrap(post.Body, width, BodyScale);

            // Bounded, because the cache lives as long as the session.
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

                // Measured with the pictures counted at the size they will DRAW at.
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
