using Control = GTA.Control;
using GTA;
using GTA.Native;

namespace Hoodrich.Core
{
    /// <summary>
    /// Eats the button that closed a screen, for a moment after it closes.
    ///
    /// Every full screen in the mod disables the controls while it is up, which is correct and
    /// is not the problem. The problem is the frame AFTER: the screen has gone, nothing is
    /// disabling anything, and the button you pressed to get out of it is still down -- so the
    /// game reads it fresh and Franklin swings at somebody, or fires, or gets into a car.
    ///
    /// A press is not an instant, and a screen closing on the leading edge of one leaves the
    /// rest of it lying around. This holds the handful of controls that would do something
    /// regrettable for as long as a button is plausibly still held.
    ///
    /// Only the destructive ones. Movement, the camera and the phone all carry on -- a guard
    /// that freezes the player for a fifth of a second to solve a stray swing is worse than the
    /// swing.
    /// </summary>
    internal static class InputGuard
    {
        /// <summary>
        /// How long a button gets to be released.
        ///
        /// A fifth of a second. Long enough for a tap and the frames either side of it, short
        /// enough that it is not a delay anybody can notice -- and it is refreshed on every
        /// close, so holding the button simply keeps it held off.
        /// </summary>
        private const int HoldMs = 220;

        private static int _until;

        /// <summary>Called by a screen as it closes.</summary>
        public static void Swallow()
        {
            _until = Game.GameTime + HoldMs;
        }

        /// <summary>Whether anything is currently being held off.</summary>
        public static bool Busy => Game.GameTime < _until;

        /// <summary>
        /// Whether a control is down right now, asked the way that still works once it has
        /// been disabled -- which by this point in the frame it has been.
        /// </summary>
        private static bool Down(Control control)
        {
            try
            {
                return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)control);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Called every frame by Main, before anything else reads a control.
        ///
        /// Disabled rather than consumed. IS_DISABLED_CONTROL_JUST_PRESSED still sees these, so
        /// anything of ours that deliberately wants to read a disabled control still can --
        /// what stops is the GAME acting on them, which is the whole point.
        /// </summary>
        public static void Tick()
        {
            if (!Busy) return;

            try
            {
                // HELD IS NOT THE SAME AS TAPPED, and the fixed window was only ever right for
                // a tap. Two hundred and twenty milliseconds covers a press and the frames
                // either side of it -- but somebody backing out of a screen that does not
                // vanish instantly holds the key a beat longer to make sure, and a fifth of a
                // second is easy to beat. The guard expired with the finger still down, the
                // game read the button fresh, and Franklin swung at whoever was in front of
                // him.
                //
                // So the clock is pushed forward for as long as anything that could do damage
                // is still down. A tap is unaffected; a hold is simply held off until it ends,
                // which is what the comment at the top always claimed happened.
                //
                // Asked of the whole family through Fists rather than of a list typed out here,
                // because a list typed out here is what the last round of this was: it named
                // three melee controls, none of which were the one swinging, and the guard held
                // off inputs nobody was pressing while the punch went through underneath it.
                if (Fists.AnyDown() ||
                    Down(Control.Enter) || Down(Control.VehicleExit) ||
                    Down(Control.PhoneCancel) || Down(Control.PhoneSelect))
                {
                    _until = Game.GameTime + HoldMs;
                }

                Fists.Off();

                // And the two that put you somewhere rather than hurt somebody: B on a pad is
                // both "back" and "get out of the car", and Enter is both "pick this" and
                // "get into that one".
                Game.DisableControlThisFrame(Control.Enter);
                Game.DisableControlThisFrame(Control.VehicleExit);
            }
            catch
            {
                // A frame without the guard is a stray swing, not a crash.
            }
        }
    }
}
