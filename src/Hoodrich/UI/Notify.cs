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
            Ticker("~o~Hoodrich:~s~ " + message);
        }

        public static void Failure(string message)
        {
            Important("~r~Hoodrich:~s~ " + message);
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
        /// streamed or shipped for this. A name the install does not have simply draws no
        /// picture and keeps the words, which is why an unknown portrait is not worth guarding
        /// against.
        /// </summary>
        public static void Text(string portrait, string sender, string subject, string body,
                                bool urgent = false)
        {
            if (string.IsNullOrEmpty(body)) return;

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
                              portrait ?? "CHAR_DEFAULT", portrait ?? "CHAR_DEFAULT",
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
