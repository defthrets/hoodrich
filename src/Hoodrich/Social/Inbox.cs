using System;
using System.Collections.Generic;
using Hoodrich.Core;

namespace Hoodrich.Social
{
    /// <summary>
    /// Every text you have been sent, kept.
    ///
    /// The mod has always had text messages and has never had anywhere to read them twice.
    /// They went to the game's feed, sat there for a few seconds and were gone -- so a plug
    /// telling you where he is parked, or Lamar naming the next job, was information you had
    /// one chance to catch and no way to check. Missing one meant standing in the street with
    /// no idea what you had just been told.
    ///
    /// STATIC, and deliberately. Notify is static and is called from twelve places across
    /// nine files, none of which know about each other; threading an instance through all of
    /// them to reach the one line that stores the message would be a worse change to this
    /// codebase than a static list is. The hook is a single line inside Notify.Text, which
    /// means every text the mod has ever sent is captured -- and so is every one anybody adds
    /// later, without having to remember this class exists.
    ///
    /// Stored by WALL CLOCK, not Game.GameTime. The feed's own Ago() uses game time, which is
    /// correct for a post that cannot outlive the session -- these can. Game time restarts
    /// near zero when you load in, so a message from last night would read as having arrived
    /// seconds ago, which is worse than showing no time at all.
    /// </summary>
    internal static class Inbox
    {
        /// <summary>
        /// How many are kept. Old ones fall off the bottom.
        ///
        /// Eighty is roughly a long evening's play. The list is loaded, saved and scanned in
        /// full, so it wants a ceiling; the alternative is a save file that grows forever and
        /// a scroll bar that stops meaning anything.
        /// </summary>
        private const int Keeps = 80;

        /// <summary>
        /// Two identical messages this close together are one message sent twice.
        ///
        /// Nothing in the mod does that on purpose. Something looping on a bad tick could,
        /// though, and the cost of that bug would be an inbox holding ninety copies of one
        /// line with the real history pushed off the end -- so the store refuses the duplicate
        /// rather than trusting every caller to be well behaved.
        /// </summary>
        private const int RepeatMs = 2000;

        /// <summary>One text, as it arrived.</summary>
        internal sealed class Message
        {
            /// <summary>The CHAR_ dictionary, already resolved to one this build actually has.</summary>
            public string Portrait = "";

            /// <summary>
            /// And a PNG of ours to draw instead, where there is one.
            ///
            /// For a sender the game has no photograph of and never will. A delivery driver is
            /// a different man every order, so there is no contact to hang a picture on -- the
            /// picture comes with the message.
            /// </summary>
            public string Icon = "";

            public string Sender = "";
            public string Subject = "";
            public string Body = "";

            /// <summary>Seconds since the epoch, UTC. See the note on the class about why.</summary>
            public long At;

            /// <summary>Cleared when you open the thread it is in, not when the app opens.</summary>
            public bool Read;

            /// <summary>
            /// Yours, rather than theirs.
            ///
            /// Without this the app is a log rather than a conversation. You press the row that
            /// texts a plug for a re-up, and the only thing that ever appears in the thread is
            /// his reply -- so the screen shows him answering a question you cannot see you
            /// asked. Sender still names the OTHER man on a message of yours, because that is
            /// what threads it; who it came from is this flag's job, not the name's.
            /// </summary>
            public bool Mine;
        }

        private static readonly List<Message> Kept = new List<Message>();

        private static string _lastBody = "";
        private static string _lastSender = "";
        private static int _lastAt = -RepeatMs;

        /// <summary>Oldest first, which is the order a conversation reads in.</summary>
        public static IReadOnlyList<Message> All { get { return Kept; } }

        /// <summary>
        /// Set by Main to mark the save dirty.
        ///
        /// The save only writes when something says it has changed, and a text arriving is a
        /// change -- without this, a message received after the last sale is lost on exit, and
        /// the whole point of keeping them goes with it.
        /// </summary>
        public static Action Changed;

        /// <summary>
        /// Whether an arriving text makes a sound. Set by Main from the phone's PlaySounds.
        ///
        /// One switch for both, because somebody who turned the phone's clicks off has already
        /// said what they think about it making noises at them.
        /// </summary>
        public static bool Chime = true;

        public static int Unread
        {
            get
            {
                var n = 0;
                for (var i = 0; i < Kept.Count; i++) if (!Kept[i].Read) n++;
                return n;
            }
        }

        /// <summary>Files a message. Called from Notify.Text and from nowhere else.</summary>
        public static void Keep(string portrait, string sender, string subject, string body,
                                string icon = null)
        {
            if (string.IsNullOrEmpty(body)) return;

            sender = sender ?? "";

            var now = GTA.Game.GameTime;

            if (now - _lastAt < RepeatMs &&
                string.Equals(body, _lastBody, StringComparison.Ordinal) &&
                string.Equals(sender, _lastSender, StringComparison.Ordinal))
            {
                return;
            }

            _lastAt = now;
            _lastBody = body;
            _lastSender = sender;

            Kept.Add(new Message
            {
                Portrait = portrait ?? "",
                Icon = icon ?? "",
                Sender = sender,
                Subject = subject ?? "",
                Body = body,
                At = Now(),
                Read = false
            });

            while (Kept.Count > Keeps) Kept.RemoveAt(0);

            // The little noise a phone makes.
            //
            // HERE rather than on Changed, which also fires for messages you SEND and for a
            // wipe, and neither of those should chirp at you. Not in LoadFrom either: that
            // adds straight to the list without coming through here, so restoring a save
            // does not play thirty texts arriving at once.
            //
            // The game's own tone rather than a file of ours. It is the sound a player
            // already reads as "text", it costs nothing to ship, and it goes through GTA's
            // mixer -- so it sits under the game volume in a way our recordings cannot.
            if (Chime)
            {
                try
                {
                    Hoodrich.UI.Draw.PlaySound("Text_Arrive_Tone", "Phone_SoundSet_Default");
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not chime: " + ex.Message);
                }
            }

            if (Changed != null) Changed();
        }

        /// <summary>
        /// Files something YOU sent, so the thread reads both ways.
        ///
        /// Deliberately not routed through Keep. A message of yours has no portrait, is read
        /// the moment it exists, and must never be refused as a duplicate -- texting the same
        /// plug twice inside two seconds is a thing a player can genuinely do, and the second
        /// one vanishing would look like the button had failed.
        /// </summary>
        public static void Sent(string toWhom, string body)
        {
            if (string.IsNullOrEmpty(body)) return;

            Kept.Add(new Message
            {
                Portrait = "",
                Sender = toWhom ?? "",
                Subject = "",
                Body = body,
                At = Now(),
                Read = true,
                Mine = true
            });

            while (Kept.Count > Keeps) Kept.RemoveAt(0);

            if (Changed != null) Changed();
        }

        /// <summary>
        /// One person's messages, oldest first.
        ///
        /// Matched on the NAME, because the name is the only thing every caller passes. Two
        /// different portraits under one name would still be one conversation, which is right:
        /// it is the man you are talking to, not the mugshot the game had for him that day.
        /// </summary>
        public static List<Message> From(string sender)
        {
            var found = new List<Message>();

            for (var i = 0; i < Kept.Count; i++)
            {
                if (string.Equals(Kept[i].Sender, sender, StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(Kept[i]);
                }
            }

            return found;
        }

        /// <summary>
        /// Who has texted you, most recent conversation first.
        ///
        /// Ordered by the LAST message rather than the first, which is what makes it a phone
        /// rather than an archive -- whoever just messaged you is the row you are looking for.
        /// </summary>
        public static List<string> Senders()
        {
            var seen = new List<string>();

            for (var i = Kept.Count - 1; i >= 0; i--)
            {
                var who = Kept[i].Sender;
                if (string.IsNullOrEmpty(who)) continue;

                var already = false;

                for (var j = 0; j < seen.Count; j++)
                {
                    if (string.Equals(seen[j], who, StringComparison.OrdinalIgnoreCase))
                    {
                        already = true;
                        break;
                    }
                }

                if (!already) seen.Add(who);
            }

            return seen;
        }

        /// <summary>The newest message from someone, for the line under their name.</summary>
        public static Message Latest(string sender)
        {
            for (var i = Kept.Count - 1; i >= 0; i--)
            {
                if (string.Equals(Kept[i].Sender, sender, StringComparison.OrdinalIgnoreCase))
                {
                    return Kept[i];
                }
            }

            return null;
        }

        public static int UnreadFrom(string sender)
        {
            var n = 0;

            for (var i = 0; i < Kept.Count; i++)
            {
                if (!Kept[i].Read &&
                    string.Equals(Kept[i].Sender, sender, StringComparison.OrdinalIgnoreCase))
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>
        /// Marks a conversation read, which happens when you OPEN it.
        ///
        /// Not when the app opens. A list that clears every badge the moment you glance at it
        /// cannot tell you which of six people you have actually got back to.
        /// </summary>
        public static void MarkRead(string sender)
        {
            var touched = false;

            for (var i = 0; i < Kept.Count; i++)
            {
                if (Kept[i].Read) continue;

                if (string.Equals(Kept[i].Sender, sender, StringComparison.OrdinalIgnoreCase))
                {
                    Kept[i].Read = true;
                    touched = true;
                }
            }

            if (touched && Changed != null) Changed();
        }

        public static void Wipe()
        {
            Kept.Clear();
            _lastBody = "";
            _lastSender = "";
            _lastAt = -RepeatMs;

            if (Changed != null) Changed();
        }

        // ---- time ---------------------------------------------------------------

        private static readonly DateTime Epoch =
            new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static long Now()
        {
            return (long)(DateTime.UtcNow - Epoch).TotalSeconds;
        }

        /// <summary>
        /// How long ago, in the shortest form that is still true.
        ///
        /// Clamped at zero because a save carried between machines can hold a stamp from the
        /// future, and "in -3h" is a worse answer than "now".
        /// </summary>
        public static string Ago(long at)
        {
            var seconds = Now() - at;
            if (seconds < 0) seconds = 0;

            if (seconds < 60) return "now";
            if (seconds < 3600) return (seconds / 60) + "m";
            if (seconds < 86400) return (seconds / 3600) + "h";

            return (seconds / 86400) + "d";
        }

        // ---- the save -----------------------------------------------------------

        public static Json ToJson()
        {
            var arr = Json.Array();

            for (var i = 0; i < Kept.Count; i++)
            {
                var m = Kept[i];

                arr.Add(Json.Object()
                    .Set("who", m.Sender)
                    .Set("where", m.Subject)
                    .Set("face", m.Portrait)
                    .Set("icon", m.Icon)
                    .Set("body", m.Body)
                    .Set("at", (double)m.At)
                    .Set("read", m.Read)
                    .Set("mine", m.Mine));
            }

            return arr;
        }

        public static void LoadFrom(Json node)
        {
            Kept.Clear();

            if (node == null || node.IsNull) return;

            try
            {
                foreach (var item in node.Items)
                {
                    var body = item["body"].AsString("");
                    if (string.IsNullOrEmpty(body)) continue;

                    Kept.Add(new Message
                    {
                        Sender = item["who"].AsString(""),
                        Subject = item["where"].AsString(""),
                        Portrait = item["face"].AsString(""),
                        Icon = item["icon"].AsString(""),
                        Body = body,
                        At = item["at"].AsLong(0),

                        // An old message you never read is still unread. The badge is about
                        // whether you have SEEN it, and closing the game is not reading it.
                        Read = item["read"].AsBool(false),
                        Mine = item["mine"].AsBool(false)
                    });
                }

                while (Kept.Count > Keeps) Kept.RemoveAt(0);

                Log.Info("Inbox: " + Kept.Count + " message(s) restored.");
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read the inbox: " + ex.Message);
            }
        }
    }
}
