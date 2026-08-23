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
        /// <summary>
        /// What somebody has asked for, and when they last asked.
        ///
        /// THE PROMPT USED TO FLASH, and this is why. The game's help box has to be re-issued
        /// EVERY FRAME or it is not on screen -- that is what "this frame" means -- but almost
        /// every caller in the mod asks for it from a throttled Update that runs three times a
        /// second. So the prompt was drawn on one frame in eighteen and blinked.
        ///
        /// Fixing it at the call sites would mean moving prompt logic out of a dozen Updates
        /// and into a dozen Draws, and every future caller would have to remember. So the
        /// request is LATCHED here instead: asking puts the message in the latch, and one pump
        /// per frame in Main re-issues whatever is in it. A caller that stops asking lets it
        /// lapse on its own.
        /// </summary>
        private static string _wanted;
        private static int _askedAt;

        /// <summary>
        /// How long a request survives without being renewed.
        ///
        /// It has to be longer than the SLOWEST caller's throttle or the prompt gaps between
        /// renewals, which is the flicker again in slower motion. The slowest that asks from a
        /// throttled tick runs at 800ms, so this is 900. The cost is that a prompt lingers up
        /// to that long after you walk away from the thing, which is a great deal less
        /// noticeable than a blinking box.
        /// </summary>
        private const int HoldMs = 900;

        /// <summary>Asks for the prompt. Safe to call from a throttled tick.</summary>
        public static void ShowThisFrame(string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            _wanted = message;
            _askedAt = GTA.Game.GameTime;
        }

        /// <summary>
        /// Puts it on screen. Called once per frame from Main, and nowhere else.
        /// </summary>
        public static void Tick()
        {
            if (string.IsNullOrEmpty(_wanted)) return;

            if (GTA.Game.GameTime - _askedAt > HoldMs)
            {
                _wanted = null;
                return;
            }

            Issue(_wanted);
        }

        /// <summary>Drops whatever is showing, for a screen that wants the corner to itself.</summary>
        public static void Clear() => _wanted = null;

        private static void Issue(string message)
        {
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
