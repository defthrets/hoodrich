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
                Game.DisableControlThisFrame(Control.Attack);
                Game.DisableControlThisFrame(Control.Attack2);
                Game.DisableControlThisFrame(Control.Aim);
                Game.DisableControlThisFrame(Control.MeleeAttack1);
                Game.DisableControlThisFrame(Control.MeleeAttack2);
                Game.DisableControlThisFrame(Control.MeleeAttackAlternate);
                Game.DisableControlThisFrame(Control.VehicleAttack);
                Game.DisableControlThisFrame(Control.VehicleAttack2);

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
