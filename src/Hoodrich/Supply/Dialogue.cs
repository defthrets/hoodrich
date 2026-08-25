using System;
using GTA;
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

        /// <param name="who">
        /// The man saying it, when the caller knows. Optional, and only used to place the
        /// voice in space -- everything else about this method behaves the same without it.
        ///
        /// An OPTIONAL PARAMETER rather than a search. The obvious alternative is a helper
        /// that picks the nearest ped and hopes it is the speaker, which in a yard with four
        /// men in it is a coin toss -- and every one of the seven callers already has the ped
        /// in hand. Asking is free; guessing is wrong about a quarter of the time.
        /// </param>
        public static void Say(string speaker, string line, Ped who = null)
        {
            if (string.IsNullOrEmpty(line)) return;

            // Said aloud if anybody recorded it. The subtitle below happens either way -- this
            // layer never replaces the words, it only adds a voice to them.
            Voice.VoiceHook.Speak(speaker, line, who);

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
