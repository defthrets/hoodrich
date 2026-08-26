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
            if (string.IsNullOrEmpty(message)) return;
            GTA.UI.Notification.PostTicker(message, false, true);
        }

        /// <summary>A message the player should not miss (blinks in the feed).</summary>
        public static void Important(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            GTA.UI.Notification.PostTicker(message, true, true);
        }

        /// <summary>Something went wrong, phrased for the player rather than the log.</summary>
        public static void Problem(string message)
        {
            Ticker("~o~" + Brand + ":~s~ " + message);
        }

        public static void Failure(string message)
        {
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
        public static void Text(string portrait, string sender, string subject, string body,
                                bool urgent = false)
        {
            if (string.IsNullOrEmpty(body)) return;

            // A caller that named nobody in particular gets whoever the sender turns out to be.
            if (string.IsNullOrEmpty(portrait) || portrait == Faces.Nobody)
            {
                var known = Faces.For(sender);
                if (!string.IsNullOrEmpty(known)) portrait = known;
            }

            // And whatever we settled on has to actually be in this build.
            portrait = Faces.Ready(portrait);

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
            Social.Inbox.Keep(portrait, sender, subject, body);

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

                Function.Call(Hash.END_TEXT_COMMAND_THEFEED_POST_MESSAGETEXT,
                              portrait, portrait,
                              urgent, MessageIcon, sender ?? "", subject ?? "");
            }
            catch (System.Exception ex)
            {
                // Never worth losing the message over. Fall back to the plain feed.
                Core.Log.Debug("Text message failed: " + ex.Message);
                Ticker("~y~" + sender + ":~s~ " + body);
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
