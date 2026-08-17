using System;
using Hoodrich.Core;

namespace Hoodrich.Supply
{
    /// <summary>
    /// Dealer speech.
    ///
    /// Lines go out as GTA subtitles rather than through a bespoke dialogue box: it is the
    /// game's own channel for someone talking to you, so it needs no new input model and reads
    /// as stock. The player's side of the conversation is the wheel -- approach a dealer and
    /// the choices are wedges.
    /// </summary>
    internal static class Dialogue
    {
        private const int MinDurationMs = 2500;
        private const int MaxDurationMs = 9000;

        /// <summary>Roughly how long a reader needs, so long lines are not cut off.</summary>
        private static int DurationFor(string line)
        {
            var ms = 1200 + line.Length * 55;
            return ms < MinDurationMs ? MinDurationMs : ms > MaxDurationMs ? MaxDurationMs : ms;
        }

        public static void Say(string speaker, string line)
        {
            if (string.IsNullOrEmpty(line)) return;

            try
            {
                var text = string.IsNullOrEmpty(speaker)
                    ? line
                    : "~y~" + speaker + ":~s~ " + line;

                GTA.UI.Screen.ShowSubtitle(text, DurationFor(line));
            }
            catch (Exception ex)
            {
                Log.Debug("Subtitle failed: " + ex.Message);
            }
        }
    }
}
