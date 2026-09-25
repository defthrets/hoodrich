// GENERATED -- DO NOT EDIT. This is Parkview, copied in by tools/sync-parkview.py from
// C:\projects\parkview\src\Parkview\UI\Notify.cs. Change it there; the next build overwrites this.
using GTA.Native;

namespace Hoodrich.Parkview.UI
{
    /// <summary>
    /// One line on the feed, through the natives the game's own scripts use -- not the
    /// ScriptHookVDotNet wrapper, whose name changed between builds and left older hosts
    /// asking for a method they did not have.
    /// </summary>
    internal static class Notify
    {
        public static void Important(string message) => Feed(message);
        public static void Problem(string message) => Feed("~r~" + message);

        private static void Feed(string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            try
            {
                Function.Call(Hash.BEGIN_TEXT_COMMAND_THEFEED_POST, "STRING");
                Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, message);
                Function.Call(Hash.END_TEXT_COMMAND_THEFEED_POST_TICKER, false, true);
            }
            catch (System.Exception ex)
            {
                Core.Log.Debug("Feed post failed: " + ex.Message);
            }
        }
    }
}
