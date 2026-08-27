using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.UI
{
    /// <summary>
    /// The screen that says why it is not working.
    ///
    /// A mod that fails quietly is indistinguishable from a mod that is not installed, and the
    /// player's next move is to reinstall it -- which fixes nothing, because the thing that was
    /// wrong was never the mod. Every hour of that is an hour somebody spends being annoyed at
    /// software that is telling them nothing.
    ///
    /// So it says it. On screen, in the game, in words, with what to actually do -- once, at
    /// the start, and then it goes away and stops nagging.
    /// </summary>
    internal sealed class Trouble
    {
        private const float PanelW = 0.62f;
        private const float Pad = 0.014f;
        private const float LineH = 0.026f;
        private const float TitleH = 0.040f;

        /// <summary>Long enough to read the whole thing twice without being a wall you cannot pass.</summary>
        private const int StaysMs = 45_000;

        private readonly List<Fault> _faults = new List<Fault>();

        private string _headline = "";
        private int _until;
        private bool _shown;

        public bool IsOpen => _until != 0 && Game.GameTime < _until;

        /// <summary>Put it up. Does nothing if there is nothing to say.</summary>
        public void Raise(string headline, List<Fault> faults)
        {
            if (faults == null || faults.Count == 0) return;

            // ONCE A SESSION. The problems it reports are all install-time facts -- a missing
            // file does not stop being missing, so saying it again every few minutes would be
            // nagging about something the player already cannot fix mid-game.
            if (_shown) return;

            _headline = headline ?? "";
            _faults.Clear();
            _faults.AddRange(faults);

            _until = Game.GameTime + StaysMs;
            _shown = true;

            Log.Error("PROBLEM: " + _headline);

            for (var i = 0; i < _faults.Count; i++)
            {
                Log.Error("  " + _faults[i].What);
                Log.Error("    fix: " + _faults[i].Fix);
            }
        }

        /// <summary>Dismissed by hand, because 45 seconds is a long time to stare at a wall.</summary>
        public void Dismiss()
        {
            _until = 0;
        }

        public void Draw()
        {
            if (!IsOpen) return;

            try
            {
                // Measured before anything is drawn, because the box has to be the size of what
                // goes in it -- and what goes in it is a variable number of variable-length
                // wraps. Guessing a height is how a panel ends up with a fault hanging out the
                // bottom of it, which on a screen about things being broken is its own joke.
                var body = new List<string>();
                var kind = new List<int>();

                for (var i = 0; i < _faults.Count; i++)
                {
                    var f = _faults[i];

                    Wrap(body, kind, (f.Fatal ? "STOPS IT WORKING:  " : "worth knowing:  ") + f.What, 0);
                    Wrap(body, kind, f.Fix, 1);

                    if (i < _faults.Count - 1)
                    {
                        body.Add("");
                        kind.Add(2);
                    }
                }

                Wrap(body, kind, "Full details are in scripts\\Hoodrich\\Hoodrich.log -- " +
                                 "paste that file if you are asking for help.", 2);

                var h = TitleH + Pad * 2f + body.Count * LineH + LineH;
                var top = 0.5f - h * 0.5f;
                var left = 0.5f - PanelW * 0.5f;

                Hoodrich.UI.Draw.RectFrom(left, top, PanelW, h, Color.FromArgb(238, 8, 8, 8));
                Hoodrich.UI.Draw.RectFrom(left, top, PanelW, 0.004f, Palette.Danger);

                Hoodrich.UI.Draw.Text(Build.Name.ToUpperInvariant() + " -- " + _headline,
                          left + Pad, top + Pad, 0.44f, Palette.Danger);

                var y = top + Pad + TitleH;

                for (var i = 0; i < body.Count; i++)
                {
                    if (body[i].Length > 0)
                    {
                        Hoodrich.UI.Draw.Text(body[i], left + Pad + (kind[i] == 1 ? 0.018f : 0f), y, 0.35f,
                                  kind[i] == 0 ? Palette.Text
                                  : kind[i] == 1 ? Palette.Cash
                                  : Palette.TextDim);
                    }

                    y += LineH;
                }

                Hoodrich.UI.Draw.Text("BACKSPACE  close", left + Pad, top + h - LineH, 0.30f, Palette.TextDim);

                // The key is read the disabled way and the control is held off, so closing this
                // does not also do whatever backspace does in the world behind it.
                Fists.Off();
                Game.DisableControlThisFrame(Control.PhoneCancel);

                if (Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0,
                                        (int)Control.PhoneCancel))
                {
                    Dismiss();
                }
            }
            catch
            {
                // A panel about failures should not be the thing that fails.
                _until = 0;
            }
        }

        /// <summary>Breaks a line to the panel width, at spaces.</summary>
        private static void Wrap(List<string> into, List<int> kinds, string text, int kind)
        {
            if (string.IsNullOrEmpty(text)) return;

            // Roughly what fits at this scale and width. Deliberately conservative: a line that
            // stops short looks fine and a line that runs off the panel does not.
            //
            // THE FIX LINES GET FEWER, because they are drawn inset by 0.018 and were being
            // wrapped to the full width -- so every one of them ran three or four characters
            // off the right edge of the box. Caught by laying the panel out on paper before
            // the game ever saw it, which is the only way to catch it: in game it reads as
            // text that just happens to end at the border.
            var cols = kind == 1 ? 88 : 92;

            var words = text.Split(' ');
            var line = "";

            for (var i = 0; i < words.Length; i++)
            {
                var next = line.Length == 0 ? words[i] : line + " " + words[i];

                if (next.Length <= cols)
                {
                    line = next;
                    continue;
                }

                into.Add(line);
                kinds.Add(kind);
                line = words[i];
            }

            if (line.Length <= 0) return;

            into.Add(line);
            kinds.Add(kind);
        }
    }
}
