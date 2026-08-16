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
    }
}
