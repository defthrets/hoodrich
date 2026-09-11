using GTA.Native;

namespace Hoodrich.UI
{
    /// <summary>
    /// Single funnel for player-facing feed messages.
    ///
    /// Wrapping GTA.UI.Notification here keeps the obsolete-API churn (Show -> PostTicker)
    /// in one file, and gives every Hoodrich message a consistent prefix and colour.
    /// GTA text colour codes: ~y~ yellow, ~g~ green, ~r~ red, ~o~ orange, ~s~ reset.
    /// </summary>
    internal static class Notify
    {
        /// <summary>
        /// What this thing is called on screen. See Build.Name -- there is only the one now.
        /// </summary>
        private const string Brand = Core.Build.Name;


        public static void Ticker(string message)
        {
            message = Core.Lang.T(message);

            if (string.IsNullOrEmpty(message) || Repeat(message)) return;
            GTA.UI.Notification.PostTicker(message, false, true);
        }

        /// <summary>A message the player should not miss (blinks in the feed).</summary>
        public static void Important(string message)
        {
            message = Core.Lang.T(message);

            if (string.IsNullOrEmpty(message) || Repeat(message)) return;

            // NO BLINK. The second argument to the game's ticker is "blink", and it was on
            // for everything important -- ninety-odd call sites -- which is a notification
            // that flashes black and white for a second every time it arrives. Important is
            // said with the words and the colour now, not with a strobe.
            GTA.UI.Notification.PostTicker(message, false, true);
        }

        /// <summary>
        /// Whether this exact thing was posted within the last couple of seconds.
        ///
        /// A message put on the feed twice in quick succession -- two systems announcing
        /// the same event, or a caller in a loop without a guard -- is two identical items
        /// shoving each other, which reads as the notification flickering. The second is
        /// dropped, and said so in the log by name, once, so the caller can be found.
        /// </summary>
        private static bool Repeat(string key)
        {
            var now = GTA.Game.GameTime;
            int last;

            if (_recent.TryGetValue(key, out last) && now - last < RepeatMs)
            {
                if (!_named.Contains(key))
                {
                    _named.Add(key);
                    Core.Log.Info("Notify: dropped a repeat within " + RepeatMs + " ms of \"" +
                                  (key.Length > 70 ? key.Substring(0, 70) + "..." : key) + "\".");
                }

                return true;
            }

            _recent[key] = now;

            if (_recent.Count > 64)
            {
                var stale = new System.Collections.Generic.List<string>();
                foreach (var pair in _recent) if (now - pair.Value > RepeatMs) stale.Add(pair.Key);
                foreach (var k in stale) _recent.Remove(k);
            }

            return false;
        }

        private static readonly System.Collections.Generic.Dictionary<string, int> _recent =
            new System.Collections.Generic.Dictionary<string, int>();
        private static readonly System.Collections.Generic.HashSet<string> _named =
            new System.Collections.Generic.HashSet<string>();
        private const int RepeatMs = 2000;

        /// <summary>Something went wrong, phrased for the player rather than the log.</summary>
        public static void Problem(string message)
        {
            message = Core.Lang.T(message);

            Ticker("~o~" + Brand + ":~s~ " + message);
        }

        public static void Failure(string message)
        {
            message = Core.Lang.T(message);

            Important("~r~" + Brand + ":~s~ " + message);
        }

        /// <summary>
        /// A text message, from a person, with their face on it.
        ///
        /// The game's own phone-message feed post rather than a ticker line. "Tao Cheng is on
        /// his way to you" as a grey ticker is the MOD telling you something; the same words
        /// under his portrait, with his name on them, are HIM telling you -- and he is a
        /// contact who has just been phoned, so that is what it should have been.
        ///
        /// The portrait is a CHAR_ texture dictionary the game already ships, so nothing is
        /// streamed or shipped for this.
        ///
        /// And it is worked out HERE rather than trusted from the call site. Half of these
        /// calls passed CHAR_DEFAULT -- including the first text of the whole mod, Gerald
        /// asking you to come and see him, which went out under a grey silhouette. A message
        /// from a man you know should have his face on it, and the sender's name is already
        /// sitting right there in the argument list, so nothing else needs to be remembered.
        /// </summary>
        /// <summary>
        /// icon: a PNG in data\icons to stand in for the portrait where the phone draws it.
        ///
        /// THE GAME'S FEED CANNOT TAKE ONE. Its notification takes a texture dictionary and
        /// nothing else, so a brand of ours can only appear on the half of this that we draw
        /// ourselves -- which is the messages app. That is where a thread is read anyway; the
        /// feed card is the thing that flashes past.
        /// </summary>
        public static void Text(string portrait, string sender, string subject, string body,
                                bool urgent = false, string icon = null)
        {
            if (string.IsNullOrEmpty(body)) return;

            subject = Core.Lang.T(subject);
            body = Core.Lang.T(body);
            if (Repeat((sender ?? "") + "|" + body)) return;

            // A caller that named nobody in particular gets whoever the sender turns out to be.
            if (string.IsNullOrEmpty(portrait) || portrait == Faces.Nobody)
            {
                var known = Faces.For(sender);
                if (!string.IsNullOrEmpty(known)) portrait = known;
            }

            // And whatever we settled on has to actually be in this build.
            //
            // A PEDHEADSHOT IS NOT A STREAMED DICTIONARY, and this is where that mattered.
            // Faces.Ready asks HAS_STREAMED_TEXTURE_DICT_LOADED, which is exactly the right
            // question about a CHAR_ dictionary and is permanently the wrong one about a
            // headshot -- those are made by REGISTER_PEDHEADSHOT and answered for by
            // IS_PEDHEADSHOT_READY instead. So every portrait the headshot factory rendered
            // was tested with the wrong native, failed, and was replaced by CHAR_DEFAULT.
            //
            // Which on this install draws as the LS Customs badge. That is why a text from a
            // LUber driver arrived signed by a car customiser -- his photograph existed the
            // whole time and was being thrown away one line before it was used.
            portrait = Headshots.Holds(portrait) ? portrait : Faces.Ready(portrait);

            // FILED BEFORE IT IS SHOWN, and this is the only place it happens.
            //
            // Twelve callers across nine files send texts and not one of them should have to
            // know there is somewhere to keep them. Storing it here means the inbox holds
            // every message the mod has ever sent, including the ones written after this line
            // was, and that the face on the message and the face in the inbox are the same
            // face -- which is why it goes below the resolution above rather than at the top.
            //
            // Before the try, not inside it. If the feed post throws, you were still sent the
            // message, and the one place you can now go to read it is the one place that must
            // not have missed it.
            Social.Inbox.Keep(portrait, sender, subject, body, icon);

            try
            {
                Function.Call(Hash.BEGIN_TEXT_COMMAND_THEFEED_POST, "STRING");

                // Split rather than truncated. The feed takes 99 characters a go and drops
                // anything past it silently, so a long message loses its own ending.
                foreach (var part in Split(body))
                {
                    // Misnamed in every header there is: this is the generic "add a literal
                    // string" component and has nothing to do with player names.
                    Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, part);
                }

                // The third argument is the blink, whatever the header calls it. Never.
                Function.Call(Hash.END_TEXT_COMMAND_THEFEED_POST_MESSAGETEXT,
                              portrait, portrait,
                              false, MessageIcon, sender ?? "", subject ?? "");
            }
            catch (System.Exception ex)
            {
                // Never worth losing the message over. Fall back to the plain feed.
                Core.Log.Debug("Text message failed: " + ex.Message);
                Ticker("~y~" + sender + ":~s~ " + body);
            }
        }

        /// <summary>
        /// The same card a text arrives on, for something that is not a text.
        ///
        /// NOT KEPT. Text files everything it sends, which is right for a text and wrong for
        /// a service telling you its car is outside -- three of those per ride would push a
        /// real conversation off the end of the inbox to say something that stops being true
        /// in ninety seconds.
        ///
        /// So this is the picture, the name and the line, and then it is gone. Everything a
        /// ticker cannot do and none of what an inbox is for.
        /// </summary>
        public static void Card(string portrait, string from, string subject, string body)
        {
            if (string.IsNullOrEmpty(body)) return;

            subject = Core.Lang.T(subject);
            body = Core.Lang.T(body);
            if (Repeat((from ?? "") + "|" + body)) return;

            // The same as Text. See the note there about which native answers for which
            // kind of picture.
            portrait = Headshots.Holds(portrait) ? portrait : Faces.Ready(portrait);

            try
            {
                Function.Call(Hash.BEGIN_TEXT_COMMAND_THEFEED_POST, "STRING");

                foreach (var part in Split(body))
                {
                    Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, part);
                }

                Function.Call(Hash.END_TEXT_COMMAND_THEFEED_POST_MESSAGETEXT,
                              portrait, portrait, false, MessageIcon,
                              from ?? "", subject ?? "");
            }
            catch (System.Exception ex)
            {
                Core.Log.Debug("Card failed: " + ex.Message);
                Ticker(body);
            }
        }

        /// <summary>Icon type 1 is the phone-message chevron, which is what this is.</summary>
        private const int MessageIcon = 1;

        /// <summary>The feed takes 99 characters per string, so longer ones go in pieces.</summary>
        private static System.Collections.Generic.List<string> Split(string body)
        {
            var parts = new System.Collections.Generic.List<string>();

            for (var i = 0; i < body.Length; i += 99)
            {
                parts.Add(body.Substring(i, System.Math.Min(99, body.Length - i)));
            }

            return parts;
        }
    }
}
