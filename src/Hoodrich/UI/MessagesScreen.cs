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
    /// Somebody you can text, handed to the screen from outside.
    ///
    /// The screen has no idea what a dealer is and is not going to learn. It needs a name, a
    /// face, whether the man will answer right now, and a token to hand back if you press
    /// send -- so that is what it takes. Everything about stock, distance, rank and gang
    /// standing stays in the systems that already know about it.
    /// </summary>
    internal sealed class PhoneContact
    {
        public string Id = "";
        public string Name = "";
        public string Portrait = "";

        /// <summary>Why he will not answer, or null if he will.</summary>
        public string Refusal;

        /// <summary>What pressing send would say to him, shown before you press it.</summary>
        public string Line = "";

        /// <summary>
        /// A file icon, for a contact the game has no photograph of.
        ///
        /// The homies are three men rather than one, so there is no CHAR_ mugshot that is
        /// them -- and a coloured circle with an H in it beside Gerald's actual face reads as
        /// a contact that failed to load. The Contacts wheel has always drawn them with
        /// people.png and this draws them with the same art.
        /// </summary>
        public string Icon = "";
    }

    /// <summary>
    /// The phone's messages.
    ///
    /// Two screens in one, because that is what a messages app is: a list of people, and one
    /// conversation. Pressing a person opens their thread; backing out of a thread returns to
    /// the list rather than closing the app, which is the one navigation rule that makes the
    /// app feel like a phone instead of a menu with a submenu.
    ///
    /// The list is NOT just people who have texted you. Everybody you can reach is on it --
    /// the plugs, and your own people -- whether they have ever sent you anything or not,
    /// because the second half of this feature is texting THEM. A contact you cannot see is a
    /// contact you cannot message. One with no history reads as an empty conversation, which
    /// is exactly what it is.
    ///
    /// It does not invent a second way to do any of it. Pressing send runs the same call the
    /// Contacts wheel has always run, refusals and all, so there is one path to a re-up and
    /// one path to calling the homies over. The app is a new door onto them, not a new room.
    /// </summary>
    internal sealed class MessagesScreen
    {
        private const float PanelWidth = 0.360f;
        /// <summary>
        /// The TALLEST the panel is allowed to get, not the height it always is.
        ///
        /// It was fixed, and a fixed panel is wrong for this screen in a way it is not wrong
        /// for the feed: a timeline is always long, whereas the people list is four rows on a
        /// new save. Full height meant four conversations floating at the top of a black
        /// rectangle two thirds of which was empty, which reads as a screen that failed to
        /// load rather than a phone with four contacts in it.
        ///
        /// Sized to its content and centred instead, which is what every other panel in this
        /// mod already does -- see StashScreen, which has had it right the whole time.
        /// </summary>
        private const float PanelMax = 0.860f;

        /// <summary>Below this it stops shrinking, or one message looks like an error box.</summary>
        private const float PanelMin = 0.240f;
        private const float Pad = 0.014f;

        /// <summary>Whatever opened this must not immediately count as a press inside it.</summary>
        private const int OpenGraceMs = 220;

        /// <summary>How often the plugs are re-asked whether they are answering.</summary>
        private const int BookMs = 500;

        /// <summary>
        /// The name, the locale under it, and the rule below both.
        ///
        /// It was 0.058 and the rule went straight through "Chamberlain Hills". The header is
        /// two stacked lines in the thread and only one in the list, so it has to be sized for
        /// the taller of the two -- the list just gets a little more air above its first row,
        /// which it can afford.
        /// </summary>
        private const float HeaderHeight = 0.068f;

        private const float RowHeight = 0.050f;
        private const float AvatarSize = 0.038f;

        private const float NameScale = 0.335f;
        private const float BodyScale = 0.315f;
        private const float SmallScale = 0.275f;

        private const float LineHeight = 0.0232f;

        /// <summary>Space above and below a message's text, inside its own ground.</summary>
        private const float BubblePad = 0.005f;
        private const float BubbleGap = 0.008f;

        /// <summary>How far a bubble is inset from the far side, so it reads as one-sided.</summary>
        private const float BubbleInset = 0.052f;

        /// <summary>A bubble with three dots in it, and the gap under it.</summary>
        private const float TypingHeight = LineHeight + BubblePad * 2f + BubbleGap;

        private const float FooterHeight = 0.042f;
        private const float KeysHeight = 0.026f;

        private readonly Curtain _curtain = new Curtain();

        /// <summary>Who you can reach. Set by Main; see PhoneContact.</summary>
        public Func<List<PhoneContact>> Contacts;

        /// <summary>
        /// Sends the message. Takes the contact id and reports its own problems.
        ///
        /// Was TextPlug, and is not plugs-only any more -- the homies are on the other end of
        /// it too, and whatever gets a thread next will be as well.
        /// </summary>
        public Action<string> TextContact;

        /// <summary>A row in the people list.</summary>
        private sealed class Row
        {
            public string Name = "";
            public string Portrait = "";

            /// <summary>Empty when this is somebody who texted you but cannot be texted back.</summary>
            public string Id = "";

            public string Refusal;
            public string Line = "";
            public string Icon = "";

            public string Preview = "";
            public string When = "";
            public int Unread;
            public bool Mine;
        }

        private readonly List<Row> _rows = new List<Row>();

        /// <summary>Null on the list, a name while a thread is open.</summary>
        private string _who;

        private int _pick;
        private int _scroll;
        private int _openedAt;

        /// <summary>
        /// How many messages the store held when the list was last built.
        ///
        /// A text can arrive while you are looking at the app -- that is not a rare case, it
        /// is what happens every time you order and then sit reading the thread. Comparing the
        /// count is enough to notice, and costs nothing per frame.
        /// </summary>
        private int _stamp = -1;

        /// <summary>When the contact list was last asked who would answer.</summary>
        private int _bookAt;

        /// <summary>Messages above the top of the thread view. Written by the draw, read by it.</summary>
        private int _earlier;

        /// <summary>
        /// Who is writing back, and how many of their messages there were when they started.
        ///
        /// The count is how we know they have finished: their reply does not arrive on the
        /// frame you press send -- Delivery holds the phone to Franklin's ear first -- so the
        /// only honest signal that he has answered is a new message from him turning up. When
        /// one does, the dots go and his line takes their place.
        /// </summary>
        private string _typingFor;
        private int _typingCount;
        private int _typingSince;

        /// <summary>
        /// How long the dots are given before they are taken down.
        ///
        /// Not a guess at how long he takes -- his reply removes them the moment it lands, and
        /// this only covers the case where no reply is ever coming. Dots that sit there
        /// forever are worse than no dots, because they say something is on its way when
        /// nothing is.
        /// </summary>
        private const int TypingGivesUpMs = 30000;

        /// <summary>How fast the dot travels along the three.</summary>
        private const int TypingStepMs = 260;

        private readonly Dictionary<Inbox.Message, string[]> _wrapped =
            new Dictionary<Inbox.Message, string[]>();

        public bool IsOpen { get { return _curtain.Showing; } }

        public void Open()
        {
            _curtain.Open();

            _who = null;
            _pick = 0;
            _scroll = 0;
            _openedAt = Game.GameTime;
            _stamp = -1;
            _bookAt = 0;

            Build();

            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        public void Close()
        {
            // The key that got you out of here does not also swing at somebody.
            if (IsOpen) Core.InputGuard.Swallow();

            _curtain.Close();
            _who = null;
            _typingFor = null;
            _wrapped.Clear();
        }

        // ---- the people list ----------------------------------------------------

        /// <summary>
        /// Everyone worth a row, in the order a phone would put them.
        ///
        /// Conversations first, most recent at the top, because whoever just messaged you is
        /// what you opened this for. Then the plugs you have never heard from, which are
        /// contacts rather than conversations and belong under them.
        /// </summary>
        private void Build()
        {
            _rows.Clear();

            var book = Contacts == null ? null : Contacts();

            foreach (var who in Inbox.Senders())
            {
                var last = Inbox.Latest(who);
                if (last == null) continue;

                var row = new Row
                {
                    Name = who,
                    Preview = last.Body,
                    When = Inbox.Ago(last.At),
                    Unread = Inbox.UnreadFrom(who),
                    Mine = last.Mine
                };

                // His face comes off the newest message that HAS one. A thread whose last line
                // is yours carries no portrait, and falling back to nothing would blank the
                // avatar of a man you have been talking to all evening.
                row.Portrait = FaceIn(who);

                var known = Find(book, who);

                if (known != null)
                {
                    row.Id = known.Id;
                    row.Refusal = known.Refusal;
                    row.Line = known.Line;
                    row.Icon = known.Icon;

                    if (string.IsNullOrEmpty(row.Portrait)) row.Portrait = known.Portrait;
                }

                // Lamar is not a plug and never will be, so he is on nobody's contact list --
                // but the game has a photograph of him and this is a conversation with him.
                // The same lookup his texts use answers for anybody in that position.
                if (string.IsNullOrEmpty(row.Portrait)) row.Portrait = Faces.For(who);

                _rows.Add(row);
            }

            if (book == null) return;

            foreach (var contact in book)
            {
                if (contact == null || string.IsNullOrEmpty(contact.Name)) continue;
                if (Has(contact.Name)) continue;

                _rows.Add(new Row
                {
                    Name = contact.Name,
                    Portrait = contact.Portrait,
                    Icon = contact.Icon,
                    Id = contact.Id,
                    Refusal = contact.Refusal,
                    Line = contact.Line,
                    Preview = "",
                    When = "",
                    Unread = 0
                });
            }
        }

        private static PhoneContact Find(List<PhoneContact> book, string name)
        {
            if (book == null) return null;

            for (var i = 0; i < book.Count; i++)
            {
                if (book[i] != null &&
                    string.Equals(book[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return book[i];
                }
            }

            return null;
        }

        private bool Has(string name)
        {
            for (var i = 0; i < _rows.Count; i++)
            {
                if (string.Equals(_rows[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The newest REAL face this person has sent under.
        ///
        /// The silhouette does not count as one. Faces.Ready hands back CHAR_DEFAULT while a
        /// portrait is still streaming in, so the first message of a session can genuinely be
        /// filed under the grey outline -- and taking the newest portrait at face value would
        /// then keep showing it for a man whose photograph arrived a second later.
        /// </summary>
        private static string FaceIn(string who)
        {
            var all = Inbox.From(who);

            for (var i = all.Count - 1; i >= 0; i--)
            {
                var pic = all[i].Portrait;

                if (string.IsNullOrEmpty(pic)) continue;
                if (string.Equals(pic, Faces.Nobody, StringComparison.OrdinalIgnoreCase)) continue;

                return pic;
            }

            return "";
        }

        private Row Current
        {
            get
            {
                if (_pick < 0 || _pick >= _rows.Count) return null;
                return _rows[_pick];
            }
        }

        private Row RowFor(string name)
        {
            for (var i = 0; i < _rows.Count; i++)
            {
                if (string.Equals(_rows[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return _rows[i];
                }
            }

            return null;
        }

        // ---- input --------------------------------------------------------------

        public void Update()
        {
            if (!IsOpen) return;

            LockControls();

            // Still drawn on the way out and still holding the controls, but no longer
            // listening -- or the screen you have just closed spends its last tenth of a
            // second acting on whatever you press next.
            if (!_curtain.Taking) return;

            // Rebuilt when a text arrives, and on a slow beat regardless.
            //
            // The message count catches the obvious case. The timer catches the other one:
            // whether a plug will answer is a live question -- rank, cash, who you run with --
            // and the send row is a promise about what pressing ENTER will do. A row that says
            // he is good for it because he was good for it when the app opened is a lie the
            // player finds out about by pressing it.
            //
            // On a timer rather than every frame because the rebuild asks every dealer that
            // question and allocates a list to answer, and a menu does not need that sixty
            // times a second.
            if (Inbox.All.Count != _stamp || Game.GameTime - _bookAt >= BookMs)
            {
                _stamp = Inbox.All.Count;
                _bookAt = Game.GameTime;

                Build();

                // A rebuild reorders the list, so the cursor is put back on the row it was on
                // rather than on whatever has moved into that slot. Losing your place because
                // somebody texted you is exactly the sort of small rudeness that makes a menu
                // feel cheap.
                if (_who != null) Keep(_who);
                else Steady();
            }

            Typing();

            if (Game.GameTime - _openedAt < OpenGraceMs) return;

            if (Pressed(Control.PhoneCancel))
            {
                Hud.PlaySound("BACK", "HUD_FRONTEND_DEFAULT_SOUNDSET");

                // Out of the thread first, and only then out of the app.
                if (_who != null)
                {
                    _who = null;
                    _scroll = 0;
                    return;
                }

                Close();
                return;
            }

            if (_who != null)
            {
                ThreadKeys();
                return;
            }

            ListKeys();
        }

        /// <summary>Whether the dots are up for the thread you are looking at.</summary>
        private bool IsTyping
        {
            get
            {
                return _typingFor != null && _who != null &&
                       string.Equals(_typingFor, _who, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>Puts the dots away once they have done their job, or failed to.</summary>
        private void Typing()
        {
            if (_typingFor == null) return;

            if (Game.GameTime - _typingSince >= TypingGivesUpMs)
            {
                _typingFor = null;
                return;
            }

            var his = Inbox.From(_typingFor);

            // A new message that is HIS. Yours arriving in the meantime -- you can send twice --
            // is not him answering, and should not take the dots down.
            if (his.Count > _typingCount && !his[his.Count - 1].Mine)
            {
                _typingFor = null;
            }
        }

        private void ListKeys()
        {
            if (Pressed(Control.PhoneUp)) Move(-1);
            else if (Pressed(Control.PhoneDown)) Move(1);
            else if (Pressed(Control.PhoneSelect)) OpenThread();
        }

        /// <summary>
        /// Whether the conversation is taller than the panel is allowed to be.
        ///
        /// The panel grows to fit a short thread, so a short thread has nothing above the view
        /// to scroll to -- and letting it scroll anyway would push the newest message off the
        /// bottom of a panel with empty space in it, which looks like the screen breaking
        /// rather than like scrolling.
        /// </summary>
        private bool Overflows()
        {
            return ThreadHeight() >= PanelMax - 0.0001f;
        }

        private void ThreadKeys()
        {
            var count = Inbox.From(_who).Count;
            var room = Overflows();

            if (!room && _scroll != 0) _scroll = 0;

            if (Pressed(Control.PhoneUp))
            {
                // Up walks BACK through the conversation, which means further from the newest
                // message -- the thread is pinned to its bottom, so scrolling is counted from
                // there.
                if (room && _scroll < Math.Max(0, count - 1))
                {
                    _scroll++;
                    Hud.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                }
            }
            else if (Pressed(Control.PhoneDown))
            {
                if (_scroll > 0)
                {
                    _scroll--;
                    Hud.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                }
            }
            else if (Pressed(Control.PhoneSelect))
            {
                Send();
            }
        }

        private void Move(int step)
        {
            if (_rows.Count == 0) return;

            var next = _pick + step;
            if (next < 0) next = 0;
            if (next > _rows.Count - 1) next = _rows.Count - 1;
            if (next == _pick) return;

            _pick = next;
            Steady();

            Hud.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        /// <summary>Keeps the cursor on screen after it moves or the list is rebuilt.</summary>
        private void Steady()
        {
            var fits = Fits();

            if (_pick < _scroll) _scroll = _pick;
            else if (_pick >= _scroll + fits) _scroll = _pick - fits + 1;

            if (_scroll < 0) _scroll = 0;
        }

        private void Keep(string name)
        {
            for (var i = 0; i < _rows.Count; i++)
            {
                if (string.Equals(_rows[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    _pick = i;
                    return;
                }
            }
        }

        private int Fits()
        {
            var room = PanelMax - HeaderHeight - KeysHeight - Pad;
            var most = (int)(room / RowHeight);

            if (most < 1) most = 1;

            // Never more slots than there are people. This is what lets the panel shrink, and
            // it is also what stops a scroll rail appearing beside a list that fits.
            return _rows.Count > 0 && _rows.Count < most ? _rows.Count : most;
        }

        private void OpenThread()
        {
            var row = Current;
            if (row == null) return;

            _who = row.Name;
            _scroll = 0;
            _earlier = 0;

            // A thread you have just opened is not one somebody is mid-reply on. The dots
            // belong to the conversation they were started in.
            if (!IsTyping) _typingFor = null;

            // Read on OPENING it, which is the honest moment. See the note in Inbox.MarkRead.
            Inbox.MarkRead(row.Name);

            _stamp = -1;

            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        /// <summary>
        /// Texts the plug, through the same door the Contacts wheel uses.
        ///
        /// The refusal is checked here as well as inside that call, and both are wanted: this
        /// one keeps the button on the screen honest so the row is greyed out before you press
        /// it, and the one underneath is the rule. A screen that decided for itself when a plug
        /// would answer would drift out of step with the wheel the first time either changed.
        /// </summary>
        private void Send()
        {
            var row = RowFor(_who);

            if (row == null) return;

            // NOT A CONTACT, SO IT IS A CONVERSATION. Everybody who has ever texted you gets
            // an answer that fits what they said -- see Social.Replies, which reads the last
            // thing in the thread and picks from the set that claims its words.
            if (string.IsNullOrEmpty(row.Id) || TextContact == null)
            {
                Answer();
                return;
            }

            if (row.Refusal != null)
            {
                Notify.Problem(row.Refusal.ToLowerInvariant() + ".");
                Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                return;
            }

            // Counted BEFORE the send, so his reply is the message that ends the dots.
            //
            // Your own line goes into the store during TextContact, which is why this cannot be
            // read afterwards: the count would already include it and the very next frame would
            // look like he had answered.
            var before = Inbox.From(_who).Count;

            TextContact(row.Id);

            // Only if the send actually put your line in. A refusal deeper down leaves the
            // count alone, and dots for a message that was never sent are a lie.
            if (Inbox.From(_who).Count > before)
            {
                _typingFor = _who;
                _typingCount = Inbox.From(_who).Count;
                _typingSince = Game.GameTime;
            }

            // Whatever it did, the store is the thing that knows -- the send path files your
            // line itself, so the thread is rebuilt from the store rather than from a guess
            // made here about whether it went.
            _stamp = -1;
            _scroll = 0;

            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        /// <summary>
        /// Text them back something that fits what they said.
        ///
        /// THE LAST THING THEY SAID, not the first. A thread is read from the bottom and the
        /// message you are answering is the one at the bottom of it -- answering the top of a
        /// fourteen-message thread is answering something from three days ago.
        ///
        /// And it must be THEIRS. Pressing reply twice should not have you answering yourself,
        /// so the walk backwards skips over anything of your own.
        /// </summary>
        private void Answer()
        {
            var all = Inbox.From(_who);

            string subject = null;
            string body = null;

            for (var i = all.Count - 1; i >= 0; i--)
            {
                if (all[i].Mine) continue;

                subject = all[i].Subject;
                body = all[i].Body;
                break;
            }

            if (body == null) return;

            string[] back;

            var line = Replies.To(subject, body, out back);

            if (string.IsNullOrEmpty(line)) return;

            Inbox.Sent(_who, line);

            _stamp = -1;
            _scroll = 0;

            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");

            // AND THEY MIGHT ANSWER THAT. Held rather than sent now, so the dots run and the
            // reply arrives a beat later like a person typing -- the thread machinery for that
            // already exists because the plugs use it.
            var said = Replies.Back(back);

            if (string.IsNullOrEmpty(said)) return;

            _backFrom = _who;
            _backLine = said;
            _backAt = Game.GameTime + BackMinMs + _rng.Next(BackSpreadMs);

            _typingFor = _who;
            _typingCount = Inbox.From(_who).Count;
            _typingSince = Game.GameTime;
        }

        /// <summary>A reply of theirs, waiting on its own clock. See Answer.</summary>
        private string _backFrom = "";
        private string _backLine = "";
        private int _backAt;

        private readonly Random _rng = new Random();

        /// <summary>How long they take to answer, at the fastest and how much longer.</summary>
        private const int BackMinMs = 2200;
        private const int BackSpreadMs = 2600;

        /// <summary>
        /// Their answer, when it is due.
        ///
        /// Called from the tick rather than the draw, so it lands whether or not you are still
        /// looking at the thread -- somebody who answers only while you watch is a puppet.
        /// </summary>
        /// <summary>
        /// PUBLIC AND CALLED FROM MAIN, because this screen's Update only runs while the
        /// screen is open -- and an answer that only arrives while you happen to be looking at
        /// the thread is a puppet rather than a person. Close the phone after texting somebody
        /// and their reply should still turn up.
        /// </summary>
        public void Pending()
        {
            if (_backAt == 0 || Game.GameTime < _backAt) return;

            var who = _backFrom;
            var line = _backLine;

            _backAt = 0;
            _backFrom = "";
            _backLine = "";

            if (string.IsNullOrEmpty(who) || string.IsNullOrEmpty(line)) return;

            Notify.Text(null, who, "", line);

            _stamp = -1;
        }

        private static bool Pressed(Control control)
        {
            return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)control);
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
            // this, pressing E in here greets a homie or walks you through a door while you
            // are looking at a menu.
            Game.DisableControlThisFrame(Control.Context);
        }

        // ---- drawing ------------------------------------------------------------

        /// <summary>Exactly the room the people list needs, within the ceiling.</summary>
        private float ListHeight()
        {
            // The 0.012 is a gap, not a fudge. Without it the hints sit hard against the
            // bottom row and read as a fifth conversation with a very strange name.
            var h = HeaderHeight + Fits() * RowHeight + KeysHeight + 0.012f;

            if (h > PanelMax) h = PanelMax;
            if (h < PanelMin) h = PanelMin;

            return h;
        }

        /// <summary>
        /// The room a conversation needs, which is however tall it has grown.
        ///
        /// Measured before anything is drawn, because the panel has to be painted first and it
        /// cannot be painted until its size is known. That is the whole reason the message
        /// heights are worked out in a pass of their own rather than while drawing them.
        /// </summary>
        private float ThreadHeight()
        {
            var width = PanelWidth - Pad * 2f;
            var all = Inbox.From(_who);

            var used = 0f;

            for (var i = 0; i < all.Count; i++) used += HeightOf(all[i], width);

            // Gated on being at the bottom, exactly as the draw is. Reserving room for a
            // bubble that is not drawn leaves a hole under the last message.
            if (IsTyping && _scroll == 0) used += TypingHeight;

            var row = RowFor(_who);
            var footer = row != null && !string.IsNullOrEmpty(row.Id) ? FooterHeight : 0f;

            var h = HeaderHeight + used + footer + KeysHeight + 0.010f;

            if (h > PanelMax) h = PanelMax;
            if (h < PanelMin) h = PanelMin;

            return h;
        }

        public void Draw()
        {
            if (!IsOpen) return;

            var height = _who == null ? ListHeight() : ThreadHeight();

            var left = 0.5f - PanelWidth * 0.5f;
            var x = left + Pad;
            var right = left + PanelWidth - Pad;

            // Centred, and riding the curtain in. Every other panel in the mod opens this way.
            var top = 0.5f - height * 0.5f + _curtain.Lift;

            Hud.Panel(left, top, PanelWidth, height,
                      Color.FromArgb(238, 12, 13, 15), Palette.Accent);

            if (_who == null) DrawList(left, x, right, top, height);
            else DrawThread(left, x, right, top, height);
        }

        private void DrawList(float left, float x, float right, float top, float height)
        {
            var y = top + 0.014f;

            Hud.Text("MESSAGES", x, y, 0.42f, Palette.Text, Hud.FontChaletLondon, centre: false);

            var unread = Inbox.Unread;

            if (unread > 0)
            {
                Hud.TextRight(unread + " NEW", right, y + 0.003f,
                              SmallScale, Palette.Accent, Hud.FontChaletComprimeCologne);
            }

            y += 0.030f;
            Hud.RectFrom(x, y, PanelWidth - Pad * 2f, 0.0014f, Palette.Alpha(Palette.Accent, 120));

            y = top + HeaderHeight;

            if (_rows.Count == 0)
            {
                Hud.Text("Nobody has texted you yet.", 0.5f, y + 0.030f, BodyScale,
                         Palette.TextDim, Hud.FontChaletLondon);
                Keys(x, right, top, height, "", "CLOSE");
                return;
            }

            var fits = Fits();
            var shown = 0;

            for (var i = _scroll; i < _rows.Count && shown < fits; i++, shown++)
            {
                DrawRow(left, x, right, y, _rows[i], i == _pick);
                y += RowHeight;
            }

            if (_rows.Count > fits) Rail(left, top + HeaderHeight, fits, shown);

            Keys(x, right, top, height, "OPEN", "CLOSE");
        }

        private void DrawRow(float left, float x, float right, float top, Row row, bool on)
        {
            var h = RowHeight - 0.004f;

            if (on)
            {
                Hud.RectFrom(left + 0.002f, top, PanelWidth - 0.004f, h,
                             Color.FromArgb(30, 255, 255, 255));

                Hud.RectFrom(left + 0.002f, top, 0.0022f, h, Palette.Accent);
            }

            var cx = x + Hud.ToX(AvatarSize) * 0.5f;
            var cy = top + h * 0.5f;

            if (!Face(row.Portrait, cx, cy, AvatarSize, row.Icon))
            {
                Hud.Disc(cx, cy, AvatarSize * 0.5f, Tint(row.Name));

                Hud.Text(Initial(row.Name), cx, cy - 0.0135f, 0.44f,
                         Color.FromArgb(235, 250, 250, 248), Hud.FontChaletLondon);
            }

            var textX = x + Hud.ToX(AvatarSize) + 0.010f;

            // The stamp is measured first so the name can be trimmed around it rather than
            // drawn straight through it. A name and a time sharing one line with no agreement
            // about who owns the middle is how the contacts rows used to overlap.
            var stampW = 0f;

            if (!string.IsNullOrEmpty(row.When))
            {
                try { stampW = Hud.MeasureText(row.When, SmallScale, Hud.FontChaletComprimeCologne); }
                catch { stampW = 0.016f; }

                Hud.TextRight(row.When, right, top + 0.006f, SmallScale,
                              Palette.TextDim, Hud.FontChaletComprimeCologne);
            }

            var nameRoom = right - textX - stampW - 0.008f;

            Hud.Text(Hud.Fit(row.Name, nameRoom, NameScale, Hud.FontChaletLondon),
                     textX, top + 0.004f, NameScale,
                     row.Unread > 0 ? Palette.Text : Palette.Alpha(Palette.Text, 225),
                     Hud.FontChaletLondon, centre: false);

            // ON THE FACE, not at the end of the preview.
            //
            // It sat bottom-right, one line under the timestamp, and the two were about a
            // thousandth of a screen apart -- which holds at 1080p with the HUD at default and
            // stops holding the moment either changes. Every phone ever made puts this on the
            // picture, it cannot collide with anything there, and it hands the preview line
            // back the width it was reserving.
            if (row.Unread > 0)
            {
                var bx = cx + Hud.ToX(AvatarSize) * 0.42f;
                var by = cy - AvatarSize * 0.36f;

                Hud.Disc(bx, by, 0.0078f, Palette.Accent);

                Hud.Text(row.Unread.ToString(), bx, by - 0.0090f, 0.26f,
                         Color.FromArgb(255, 14, 15, 17), Hud.FontChaletLondon);
            }

            var preview = row.Preview;

            if (string.IsNullOrEmpty(preview))
            {
                preview = string.IsNullOrEmpty(row.Id) ? "" : "No messages yet";
            }
            else if (row.Mine)
            {
                // Marked as yours in the list too, or a thread whose last line you sent reads
                // as though he answered and you have not looked.
                preview = "You: " + preview;
            }

            if (!string.IsNullOrEmpty(preview))
            {
                var room = right - textX;

                Hud.Text(Hud.Fit(Flatten(preview), room, BodyScale, Hud.FontChaletLondon),
                         textX, top + 0.024f, BodyScale,
                         row.Unread > 0 ? Palette.Alpha(Palette.Text, 210) : Palette.TextDim,
                         Hud.FontChaletLondon, centre: false);
            }
        }

        // ---- the thread ---------------------------------------------------------

        private void DrawThread(float left, float x, float right, float top, float height)
        {
            var row = RowFor(_who);
            var y = top + 0.010f;

            var cx = x + Hud.ToX(0.032f) * 0.5f;
            var cy = y + 0.018f;

            var face = row == null ? "" : row.Portrait;
            var icon = row == null ? "" : row.Icon;

            if (!Face(face, cx, cy, 0.032f, icon))
            {
                Hud.Disc(cx, cy, 0.016f, Tint(_who));

                Hud.Text(Initial(_who), cx, cy - 0.0115f, 0.38f,
                         Color.FromArgb(235, 250, 250, 248), Hud.FontChaletLondon);
            }

            var textX = x + Hud.ToX(0.032f) + 0.009f;

            Hud.Text(_who, textX, y + 0.002f, 0.40f, Palette.Text,
                     Hud.FontChaletLondon, centre: false);

            var where = Where(_who);

            if (!string.IsNullOrEmpty(where))
            {
                Hud.Text(where, textX, y + 0.024f, SmallScale, Palette.TextDim,
                         Hud.FontChaletComprimeCologne, centre: false);
            }

            // Counted on the previous frame's draw, which is a frame behind and does not
            // matter: it changes when you scroll, and you cannot scroll faster than the
            // screen redraws.
            if (_earlier > 0)
            {
                Hud.TextRight(_earlier + " earlier", right, y + 0.024f, SmallScale,
                              Palette.Alpha(Palette.TextDim, 160),
                              Hud.FontChaletComprimeCologne);
            }

            y = top + HeaderHeight;
            Hud.RectFrom(x, y - 0.008f, PanelWidth - Pad * 2f, 0.0014f,
                         Palette.Alpha(Palette.Accent, 120));

            // ANYBODY WHO TEXTED YOU CAN BE TEXTED BACK, which was the whole of what was
            // missing. This was "row has a contact id", so the only threads with a key that did
            // anything were the plugs and the homies -- and what that key did was place an
            // ORDER, which is not replying to anybody. Everyone else was a wall you read.
            var canText = row != null;

            var bottom = top + height - KeysHeight
                       - (canText ? FooterHeight : 0f) - 0.006f;

            DrawMessages(left, x, right, y, bottom);

            if (canText) Footer(left, x, right, bottom + 0.004f, row);

            Keys(x, right, top, height, canText ? "REPLY" : "", "BACK");
        }

        /// <summary>
        /// The conversation, pinned to the bottom.
        ///
        /// Walked backwards from the newest and drawn forwards, which is the only arrangement
        /// that keeps the latest message on screen without knowing the total height first --
        /// and the latest message is the one you opened the thread to read.
        /// </summary>
        private void DrawMessages(float left, float x, float right, float top, float bottom)
        {
            var all = Inbox.From(_who);

            if (all.Count == 0)
            {
                Hud.Text("No messages with " + _who + " yet.", 0.5f, top + 0.030f, BodyScale,
                         Palette.TextDim, Hud.FontChaletLondon);
                return;
            }

            // The dots sit where his reply is about to, so the thread does not jump when it
            // lands -- the bubble is already that size and simply gets words in it.
            var dots = IsTyping && _scroll == 0;

            var room = bottom - top - (dots ? TypingHeight : 0f);

            var stack = new List<Inbox.Message>();
            var used = 0f;

            var from = all.Count - 1 - _scroll;
            if (from < 0) from = 0;

            for (var i = from; i >= 0; i--)
            {
                var h = HeightOf(all[i], right - x);

                if (used + h > room && stack.Count > 0) break;

                stack.Insert(0, all[i]);
                used += h;
            }

            var y = bottom - used - (dots ? TypingHeight : 0f);

            for (var i = 0; i < stack.Count; i++)
            {
                y += DrawMessage(x, right, y, stack[i]);
            }

            if (dots) DrawTyping(x, right, y);

            // How many are above the view, remembered for the header to say.
            //
            // It used to be drawn here as an ellipsis just above the first bubble, which is
            // exactly where the first bubble is when the thread is full -- the one case the
            // marker exists for. The header has room and nothing else competing for it.
            _earlier = all.Count - _scroll - stack.Count;
        }

        private float HeightOf(Inbox.Message message, float width)
        {
            var lines = Lines(message, width);
            return lines.Length * LineHeight + BubblePad * 2f + BubbleGap;
        }

        private string[] Lines(Inbox.Message message, float width)
        {
            string[] cached;
            if (_wrapped.TryGetValue(message, out cached)) return cached;

            var lines = Fold(message.Body, width - BubbleInset - 0.016f);

            _wrapped[message] = lines;
            return lines;
        }

        /// <summary>
        /// A message split on the breaks its author put in it.
        ///
        /// Rockstar-tag ~n~ and a real newline both count. Tao's subtitle puts the Cantonese
        /// and the translation on separate lines and neither means much run together, so the
        /// break is honoured rather than folded away.
        /// </summary>
        private static string[] Paragraphs(string text)
        {
            var breaks = new[] { "~n~", NewLine, Carriage };

            return text.Split(breaks, StringSplitOptions.RemoveEmptyEntries);
        }

        private const string NewLine = "\n";
        private const string Carriage = "\r";

        /// <summary>
        /// Breaks a message into as many lines as it needs.
        ///
        /// NOT Draw.Wrap, and this is worth writing down because it looked like the obvious
        /// call. That one takes a width PER LINE and stops when it runs out of them -- it is
        /// built for a label that gets two lines and no more, and everything past the last
        /// width is crammed onto it with an ellipsis. Handed a single width it produces a
        /// single line, so every message longer than one line would have been silently cut
        /// off. The feed and the dialogue box both grew their own version of this for the same
        /// reason.
        ///
        /// Rockstar's ~n~ is honoured as a real break, because the lines that use it wrote it
        /// deliberately -- Tao's subtitle puts the Cantonese and the translation on separate
        /// lines and neither means much run together.
        /// </summary>
        private static string[] Fold(string text, float width)
        {
            var lines = new List<string>();

            if (string.IsNullOrEmpty(text)) return new[] { "" };

            foreach (var para in Paragraphs(text))
            {
                if (para.Length == 0) continue;

                var words = para.Split(' ');
                var line = "";

                foreach (var word in words)
                {
                    if (word.Length == 0) continue;

                    var candidate = line.Length == 0 ? word : line + " " + word;

                    float measured;
                    try { measured = Hud.MeasureText(candidate, BodyScale, Hud.FontChaletLondon); }
                    catch { measured = candidate.Length * BodyScale * 0.011f; }

                    // A single word wider than the line still goes on it, or nothing is ever
                    // taken and the loop never ends.
                    if (measured <= width || line.Length == 0)
                    {
                        line = candidate;
                        continue;
                    }

                    lines.Add(line);
                    line = word;
                }

                if (line.Length > 0) lines.Add(line);
            }

            if (lines.Count == 0) lines.Add(text);

            return lines.ToArray();
        }

        /// <summary>The widest a bubble may be, and the narrowest, as fractions of the column.</summary>
        private const float BubbleMost = 0.74f;
        private const float BubbleLeast = 0.055f;

        /// <summary>Draws one message and returns how much room it took.</summary>
        private float DrawMessage(float x, float right, float top, Inbox.Message message)
        {
            var width = right - x;

            // WRAPPED TO WHAT IT MAY USE, THEN SIZED TO WHAT IT ACTUALLY USED.
            //
            // Every bubble was the same width as every other bubble, which is what made this
            // read as a list of grey boxes rather than a conversation. A messaging app does one
            // thing above all others: the bubble is the SHAPE of the message. "aight" is a
            // stub and a paragraph is a slab, and the ragged right edge down a thread is most
            // of what tells you at a glance who said the long thing.
            //
            // Two passes, and they are cheap: wrap against the widest it is allowed to be, then
            // measure the longest line that came out and use that. Measuring is a native call
            // per line and there are at most a handful of lines in a text message.
            var most = width * BubbleMost;

            var lines = Lines(message, most - BubblePad * 2f);

            var widest = 0f;

            for (var i = 0; i < lines.Length; i++)
            {
                var run = Hud.MeasureText(lines[i], BodyScale, Hud.FontChaletLondon);
                if (run > widest) widest = run;
            }

            var h = lines.Length * LineHeight + BubblePad * 2f;

            var w = widest + BubblePad * 2f;

            if (w > most) w = most;
            if (w < BubbleLeast) w = BubbleLeast;

            // Yours hugs the right edge, theirs hugs the left. Neither reaches the far side,
            // because a bubble that touches both edges is a paragraph again.
            var boxLeft = message.Mine ? right - w : x;

            var wash = message.Mine
                ? Palette.Alpha(Palette.Standing, 30)
                : Color.FromArgb(26, 255, 255, 255);

            Hud.RectFrom(boxLeft, top, w, h, wash);

            var rail = message.Mine ? Palette.Standing : Palette.Alpha(Palette.Accent, 170);

            // The rail goes on the outside edge of the bubble -- the side the message came
            // from. Both on the left would make the tint the only thing separating them, and
            // tint alone is the first thing to go on a bright street.
            if (message.Mine)
            {
                Hud.RectFrom(boxLeft + w - Hud.ToX(0.0022f), top, Hud.ToX(0.0022f), h, rail);
            }
            else
            {
                Hud.RectFrom(boxLeft, top, Hud.ToX(0.0022f), h, rail);
            }

            var ty = top + BubblePad;

            for (var i = 0; i < lines.Length; i++)
            {
                // LEFT-ALIGNED IN BOTH, now the bubble is the width of the words. Right
                // aligning yours made sense while every bubble was full width and the text had
                // to be pushed to its own side; in a bubble that already IS its own side it is
                // a ragged left edge inside a box, which is harder to read for no gain.
                Hud.Text(lines[i], boxLeft + BubblePad, ty, BodyScale,
                         Palette.Text, Hud.FontChaletLondon, centre: false);

                ty += LineHeight;
            }

            // The stamp hangs off the bubble's inner edge rather than sitting inside it, so a
            // one-line message stays one line high.
            var stamp = Inbox.Ago(message.At);

            // Just outside the bubble on its open side, which is where a phone puts it.
            if (message.Mine)
            {
                Hud.TextRight(stamp, boxLeft - 0.006f, top + h - LineHeight, SmallScale,
                              Palette.Alpha(Palette.TextDim, 150),
                              Hud.FontChaletComprimeCologne);
            }
            else
            {
                Hud.Text(stamp, boxLeft + w + 0.006f, top + h - LineHeight, SmallScale,
                         Palette.Alpha(Palette.TextDim, 150), Hud.FontChaletComprimeCologne,
                         centre: false);
            }

            return h + BubbleGap;
        }

        /// <summary>
        /// Three dots, in a bubble the shape of the one his answer will arrive in.
        ///
        /// The travelling dot is the whole animation: one of the three is bright and the other
        /// two are dim, and which one is bright walks along. Every messaging app on earth does
        /// this and it is instantly readable because of that -- there is nothing to invent
        /// here, and inventing something would only make it take a moment to understand.
        /// </summary>
        private void DrawTyping(float x, float right, float top)
        {
            var width = right - x;
            var w = width - BubbleInset;
            var h = LineHeight + BubblePad * 2f;

            Hud.RectFrom(x, top, w, h, Color.FromArgb(26, 255, 255, 255));
            Hud.RectFrom(x, top, Hud.ToX(0.0022f), h, Palette.Alpha(Palette.Accent, 170));

            var lit = (Game.GameTime / TypingStepMs) % 3;
            var cy = top + h * 0.5f;

            for (var i = 0; i < 3; i++)
            {
                var cx = x + 0.012f + Hud.ToX(0.0125f) * i;

                Hud.Disc(cx, cy, 0.0030f,
                         Palette.Alpha(Palette.Text, i == lit ? 235 : 70));
            }
        }

        /// <summary>
        /// The send row, which is the second half of what this app is for.
        ///
        /// It shows the line before you send it. A button that says "text him" and then puts
        /// words in your mouth is a button you press once and then stop trusting.
        /// </summary>
        private void Footer(float left, float x, float right, float top, Row row)
        {
            var open = row.Refusal == null;
            var h = FooterHeight - 0.008f;

            Hud.RectFrom(left + 0.002f, top, PanelWidth - 0.004f, h,
                         open ? Palette.Alpha(Palette.Standing, 26)
                              : Color.FromArgb(24, 255, 255, 255));

            Hud.RectFrom(left + 0.002f, top, PanelWidth - 0.004f, 0.0012f,
                         open ? Palette.Standing : Palette.Alpha(Palette.TextDim, 120));

            if (open)
            {
                Hud.Text("SEND", x, top + 0.006f, SmallScale, Palette.Standing,
                         Hud.FontChaletComprimeCologne, centre: false);

                var line = string.IsNullOrEmpty(row.Line) ? "you got anything?" : row.Line;

                Hud.Text(Hud.Fit("\"" + line + "\"", right - x - 0.040f, BodyScale,
                                 Hud.FontChaletLondon),
                         x + 0.040f, top + 0.004f, BodyScale, Palette.Text,
                         Hud.FontChaletLondon, centre: false);
            }
            else
            {
                Hud.Text(Hud.Fit(row.Refusal, right - x, BodyScale, Hud.FontChaletLondon),
                         x, top + 0.004f, BodyScale, Palette.TextDisabled,
                         Hud.FontChaletLondon, centre: false);
            }
        }

        // ---- furniture ----------------------------------------------------------

        private void Keys(float x, float right, float top, float height,
                          string select, string back)
        {
            var y = top + height - KeysHeight + 0.002f;

            var hint = string.IsNullOrEmpty(select)
                ? "UP/DOWN  MOVE"
                : "UP/DOWN  MOVE     ENTER  " + select;

            Hud.Text(hint, x, y, SmallScale, Palette.Alpha(Palette.TextDim, 170),
                     Hud.FontChaletComprimeCologne, centre: false);

            Hud.TextRight("ESC  " + back, right, y, SmallScale,
                          Palette.Alpha(Palette.TextDim, 170), Hud.FontChaletComprimeCologne);
        }

        private void Rail(float left, float top, int fits, int shown)
        {
            var height = fits * RowHeight;
            var x = left + PanelWidth - Hud.ToX(0.0018f) - 0.001f;

            Hud.RectFrom(x, top, Hud.ToX(0.0018f), height, Color.FromArgb(40, 255, 255, 255));

            var span = Math.Max(0.08f, (float)shown / Math.Max(1, _rows.Count));
            var at = (float)_scroll / Math.Max(1, _rows.Count);

            Hud.RectFrom(x, top + height * at, Hud.ToX(0.0018f), height * span,
                         Palette.Alpha(Palette.Accent, 190));
        }

        /// <summary>
        /// Their picture: the game's mugshot if they have one, our own art if they do not.
        ///
        /// Two different systems, which is why this is one function rather than two calls at
        /// every site. A CHAR_ dictionary is streamed and drawn as a sprite; a file icon is
        /// loaded off disk and drawn through ScaledDraw. Everything above here only wants to
        /// know whether a picture happened.
        /// </summary>
        private bool Face(string pic, float cx, float cy, float size = AvatarSize,
                          string icon = null)
        {
            if (!string.IsNullOrEmpty(pic) && Hud.EnsureTextureDict(pic))
            {
                Hud.Sprite(pic, pic, cx, cy, Hud.ToX(size), size, 0f, Color.White);
                return true;
            }

            if (!string.IsNullOrEmpty(icon) && Hud.File(icon, cx, cy, size * 0.86f, 0f,
                                                        Palette.Text))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Where he said he was, off his newest message that named anywhere.
        ///
        /// Every text carries a locale because the game's feed asks for one, and it is the one
        /// piece of context a thread header can show that the name does not already say.
        /// </summary>
        private static string Where(string who)
        {
            var all = Inbox.From(who);

            for (var i = all.Count - 1; i >= 0; i--)
            {
                if (!all[i].Mine && !string.IsNullOrEmpty(all[i].Subject)) return all[i].Subject;
            }

            return "";
        }

        private static string Initial(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            return name.Substring(0, 1).ToUpperInvariant();
        }

        /// <summary>
        /// A colour for somebody with no photograph, stable for the same name.
        ///
        /// Off the name rather than off the row index, or a contact's circle would change
        /// colour every time somebody else texted you and the list reordered.
        /// </summary>
        private static Color Tint(string name)
        {
            if (string.IsNullOrEmpty(name)) return Palette.TextDim;

            var hash = 0;

            for (var i = 0; i < name.Length; i++) hash = (hash * 31 + name[i]) & 0x7fffffff;

            return Palette.Spectrum((hash % 1000) / 1000f);
        }

        /// <summary>
        /// One line's worth of a message that may have been written as several.
        ///
        /// The preview is drawn with a single Text call, and a newline inside it does not wrap
        /// there -- it draws a control character. Rockstar's own colour tags are left alone;
        /// they are handled by the text system and are part of how these lines are written.
        /// </summary>
        private static string Flatten(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            return text.Replace("~n~", " ").Replace("\r", " ").Replace("\n", " ");
        }
    }
}
