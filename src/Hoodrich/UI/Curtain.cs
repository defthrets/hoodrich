using System;
using GTA;

namespace Hoodrich.UI
{
    /// <summary>
    /// How a panel arrives and how it leaves.
    ///
    /// Eight full-screen panels in this mod appeared on one frame and vanished on another.
    /// Not slowly and not badly -- instantly, both ways, which the eye reads as the screen
    /// being switched off rather than as something being closed. The 220ms every one of them
    /// carries is an input GRACE period, so the key that opened the panel does not immediately
    /// press something inside it; it was never an animation and nothing was ever drawn moving.
    ///
    /// PURELY A FUNCTION OF TIME, and that is the important part. A curtain that had to be
    /// ticked would stop halfway whenever the thing driving it stopped being called -- and
    /// these panels are closed from outside all the time: walk away from the stash door, lose
    /// the phone, start a mission. Asking the clock instead means a panel always finishes
    /// leaving, whoever stopped looking at it.
    ///
    /// It offers a LIFT rather than a fade because of how these screens are built. Every one
    /// lays its contents out relative to a single `top`, so moving that one number moves the
    /// whole panel as a piece -- header, rows, bars, footer. Fading instead would mean
    /// threading an alpha through every colour in all eight files, for a result that reads no
    /// better than a panel that slides.
    /// </summary>
    internal sealed class Curtain
    {
        /// <summary>Arriving is a touch slower than leaving. Things settle; they do not settle away.</summary>
        private const int InMs = 150;
        private const int OutMs = 110;

        /// <summary>How far it travels, as a height fraction. Small on purpose -- this is a
        /// panel settling into place, not a card being dealt.</summary>
        private const float Travel = 0.020f;

        private int _openedAt;
        private int _closedAt;
        private bool _up;

        /// <summary>Whether anything should still be drawn, including on the way out.</summary>
        public bool Showing => _up || Leaving;

        /// <summary>Whether it is all the way there and should answer the player.</summary>
        public bool Taking => _up && Game.GameTime - _openedAt >= InMs;

        /// <summary>Whether it is on its way out.</summary>
        public bool Leaving => !_up && _closedAt != 0 && Game.GameTime - _closedAt < OutMs;

        public void Open()
        {
            _up = true;
            _openedAt = Game.GameTime;
            _closedAt = 0;
        }

        public void Close()
        {
            if (!_up) return;

            _up = false;
            _closedAt = Game.GameTime;
        }

        /// <summary>Gone now, with no animation. For teardown, where nobody is watching.</summary>
        public void Drop()
        {
            _up = false;
            _closedAt = 0;
        }

        /// <summary>
        /// What to add to the panel's own top, this frame.
        ///
        /// Comes UP into place and goes UP out of it, rather than dropping back the way it
        /// came. A panel that reverses its own entrance looks like it was cancelled; one that
        /// carries on in the same direction looks like it is leaving.
        /// </summary>
        public float Lift
        {
            get
            {
                if (_up)
                {
                    var t = Ease(Game.GameTime - _openedAt, InMs);
                    return (1f - t) * Travel;
                }

                if (!Leaving) return 0f;

                var k = Ease(Game.GameTime - _closedAt, OutMs);
                return -k * Travel;
            }
        }

        /// <summary>
        /// How solid it is, for the one thing worth fading: the backdrop behind it.
        ///
        /// A screen that dims the game behind it should undim on the way out, or the panel
        /// leaves and the darkness stays for a frame.
        /// </summary>
        public float Solid
        {
            get
            {
                if (_up) return Ease(Game.GameTime - _openedAt, InMs);
                return Leaving ? 1f - Ease(Game.GameTime - _closedAt, OutMs) : 0f;
            }
        }

        /// <summary>Cubic out. Fast off the mark, gentle into place, which is how things stop.</summary>
        private static float Ease(int since, int over)
        {
            if (since <= 0) return 0f;
            if (since >= over) return 1f;

            var t = since / (float)over;
            return 1f - (float)Math.Pow(1f - t, 3);
        }
    }
}
