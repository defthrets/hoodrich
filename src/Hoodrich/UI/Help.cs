using System;
using GTA.Native;

namespace Hoodrich.UI
{
    /// <summary>
    /// The game's own top-left prompt, the one that tells you which button to press.
    ///
    /// Used rather than drawn text so button glyphs are right on both pad and keyboard: the
    /// game substitutes ~INPUT_...~ for whatever the player is actually holding.
    /// </summary>
    internal static class Help
    {
        /// <summary>Must be re-issued every frame, which is what makes it track the player.</summary>
        public static void ShowThisFrame(string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            try
            {
                Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_HELP, "STRING");
                Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, message);
                Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_HELP, 0, false, false, -1);
            }
            catch (Exception ex)
            {
                Core.Log.Debug("Help prompt failed: " + ex.Message);
            }
        }
    }
}
