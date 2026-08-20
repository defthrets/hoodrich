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
                // Same trap as the dialogue panel had: a help command opened with "STRING"
                // honours exactly ONE substring component, so anything past 96 characters is
                // thrown away without a word -- which is how a long prompt used to lose its
                // last line. CELL_EMAIL_BCON is the game's own multi-component format.
                Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_HELP, Draw.FormatFor(message));

                const int chunk = 96;
                for (var i = 0; i < message.Length; i += chunk)
                {
                    var len = Math.Min(chunk, message.Length - i);
                    Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, message.Substring(i, len));
                }

                Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_HELP, 0, false, false, -1);
            }
            catch (Exception ex)
            {
                Core.Log.Debug("Help prompt failed: " + ex.Message);
            }
        }
    }
}
