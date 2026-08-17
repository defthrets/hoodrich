using System;
using Keys = System.Windows.Forms.Keys;
using Control = GTA.Control;
using GTA;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.UI
{
    /// <summary>
    /// Owns wheel input, vanilla weapon-wheel suppression, and the open/close side effects
    /// (time scale, background blur, control locking).
    ///
    /// The vanilla wheel is suppressed by disabling its control rather than by any
    /// BLOCK_WEAPON_WHEEL native, which this SHVDN build does not expose. Disabling a control
    /// still lets us read its state via IS_DISABLED_CONTROL_PRESSED, so the same button that
    /// used to raise the weapon wheel now raises ours.
    /// </summary>
    internal sealed class WheelController
    {
        private const int HudWeaponWheel = 19;
        private const int HudWeaponWheelStats = 20;

        /// <summary>Mouse deltas are per-frame, so they are integrated into a virtual stick.</summary>
        private const float MouseGain = 2.6f;

        private readonly Settings _cfg;
        private readonly RadialMenu _menu;
        private readonly Func<WheelPage> _rootBuilder;

        private float _dirX;
        private float _dirY;
        private bool _timeScaleApplied;
        private bool _timecycleApplied;
        private bool _wasHeld;

        public WheelController(Settings cfg, RadialMenu menu, Func<WheelPage> rootBuilder)
        {
            _cfg = cfg;
            _menu = menu;
            _rootBuilder = rootBuilder;
        }

        public bool IsOpen => _menu.IsOpen;

        // ---- per-frame ---------------------------------------------------------

        /// <summary>True while the player has the game's own weapon wheel instead of ours.</summary>
        private bool _vanillaMode;

        /// <summary>Hold the handoff at least this long, so it cannot end on the same frame.</summary>
        private int _vanillaMinUntil;

        /// <summary>
        /// Closes our wheel and gives the player the REAL weapon wheel.
        ///
        /// Hoodrich normally suppresses it by disabling its control, so handing it back is a
        /// matter of not doing that and forcing it open -- the player is still holding the
        /// button, so it appears under their thumb as though it always had.
        ///
        /// The handoff lasts until they LET GO, not for a fixed time. A timer expired while the
        /// wheel was still open, which snatched it back mid-selection and left its looping
        /// audio without the close event that stops it.
        /// </summary>
        public void ShowVanillaWheel()
        {
            // Deliberately does NOT call CloseWheel(): the commit path closes the wheel straight
            // after this returns, and CloseWheel -> RestoreWorld would cancel the handoff on the
            // very same frame it was requested. That is why nothing appeared.
            _menu.Close();
            RestoreTimeAndBlur();

            _vanillaMode = true;

            // Selection happens on RELEASE, so by the time we get here the button is already up.
            // The wheel therefore has to be held open on a timer rather than by the player.
            _vanillaMinUntil = Game.GameTime + _cfg.VanillaWheelSeconds * 1000;
        }

        private void EndVanillaMode()
        {
            _vanillaMode = false;

            // Release it explicitly. Forcing it open and then simply walking away is what
            // leaves the wheel and its sound stuck on.
            try { Function.Call(Hash.HUD_FORCE_WEAPON_WHEEL, false); }
            catch { /* teardown */ }
        }

        public void Update(bool available)
        {
            if (_vanillaMode)
            {
                // Hands off: no suppression at all, and hold the real wheel open so the player
                // can pick from it without having to keep the button down.
                Function.Call(Hash.HUD_FORCE_WEAPON_WHEEL, true);

                var expired = Game.GameTime >= _vanillaMinUntil;

                // Holding the button again means they want it up for longer; let them.
                if (Game.IsControlPressed(Control.SelectWeapon))
                {
                    _vanillaMinUntil = Game.GameTime + 400;
                    expired = false;
                }

                if (!available || expired)
                {
                    EndVanillaMode();

                    // Do not let the same button press fall straight back into our wheel.
                    _wasHeld = true;
                }

                return;
            }

            if (_cfg.WheelMode == WheelMode.Replace) SuppressVanillaWheel();

            if (!available)
            {
                if (_menu.IsOpen) CloseWheel();
                return;
            }

            var held = ReadOpenInput();

            if (_cfg.HoldToOpen)
            {
                if (held && !_menu.IsOpen) OpenWheel();
                else if (!held && _menu.IsOpen) CommitAndClose();
            }
            else
            {
                // Toggle: react to the rising edge only.
                if (held && !_wasHeld)
                {
                    if (_menu.IsOpen) CommitAndClose();
                    else OpenWheel();
                }
            }

            _wasHeld = held;

            if (!_menu.IsOpen) return;

            LockControlsThisFrame();
            UpdateSelection();
            HandleInPlaceInput();

            _menu.Render();
            DrawFooterHint();
        }

        /// <summary>
        /// Held every frame, open or not, so the vanilla wheel never gets a chance to appear.
        /// </summary>
        private void SuppressVanillaWheel()
        {
            Game.DisableControlThisFrame(Control.SelectWeapon);
            Game.DisableControlThisFrame(Control.WeaponWheelNext);
            Game.DisableControlThisFrame(Control.WeaponWheelPrev);
            Game.DisableControlThisFrame(Control.WeaponWheelLeftRight);
            Game.DisableControlThisFrame(Control.WeaponWheelUpDown);

            Function.Call(Hash.HIDE_HUD_COMPONENT_THIS_FRAME, HudWeaponWheel);
            Function.Call(Hash.HIDE_HUD_COMPONENT_THIS_FRAME, HudWeaponWheelStats);
        }

        private bool ReadOpenInput()
        {
            if (_cfg.WheelMode == WheelMode.Replace)
            {
                // Disabled, so read it through the disabled-control path.
                return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)Control.SelectWeapon);
            }

            if (_cfg.WheelKey == Keys.None) return false;
            if (_cfg.WheelModifier != Keys.None && !Game.IsKeyPressed(_cfg.WheelModifier)) return false;
            return Game.IsKeyPressed(_cfg.WheelKey);
        }

        // ---- open / close ------------------------------------------------------

        private void OpenWheel()
        {
            WheelPage root;
            try
            {
                root = _rootBuilder();
            }
            catch (Exception ex)
            {
                Log.Error("Root wheel page builder threw; not opening.", ex);
                return;
            }

            if (root == null) return;

            _dirX = 0f;
            _dirY = 0f;
            _menu.Open(root);

            if (_cfg.WheelTimeScale < 0.999f)
            {
                Game.TimeScale = _cfg.WheelTimeScale;
                _timeScaleApplied = true;
            }

            if (_cfg.BlurBackground && !string.IsNullOrEmpty(_cfg.TimecycleModifier))
            {
                Function.Call(Hash.SET_TRANSITION_TIMECYCLE_MODIFIER, _cfg.TimecycleModifier, 0.35f);
                _timecycleApplied = true;
            }
        }

        private void CommitAndClose()
        {
            // Commit returns false for a submenu, which must not close the wheel. In hold mode
            // the player has already let go, so a submenu open there would strand the UI --
            // treat any non-closing commit as a close.
            _menu.Commit();
            CloseWheel();
        }

        /// <summary>
        /// Closes our wheel.
        ///
        /// Deliberately does NOT end a vanilla-wheel handoff. Picking Weapons runs through
        /// Commit and then straight into this, so restoring everything here cancelled the
        /// handoff on the same frame it was asked for -- which is why the real weapon wheel
        /// never appeared. Ending it belongs to the paths that mean "stop", not to closing.
        /// </summary>
        public void CloseWheel()
        {
            _menu.Close();
            RestoreTimeAndBlur();
        }

        /// <summary>
        /// Undoes every global side effect. Called on close, on script abort, and defensively
        /// whenever the wheel is not open -- a mod that leaves the world at 0.25x time scale
        /// after a crash is worse than one that never opened.
        /// </summary>
        public void RestoreWorld()
        {
            if (_vanillaMode) EndVanillaMode();
            RestoreTimeAndBlur();
        }

        /// <summary>Undoes only the world effects, leaving any vanilla-wheel handoff alone.</summary>
        private void RestoreTimeAndBlur()
        {

            if (_timeScaleApplied)
            {
                Game.TimeScale = 1f;
                _timeScaleApplied = false;
            }

            if (_timecycleApplied)
            {
                Function.Call(Hash.CLEAR_TIMECYCLE_MODIFIER);
                _timecycleApplied = false;
            }
        }

        // ---- while open --------------------------------------------------------

        private static void LockControlsThisFrame()
        {
            // Everything that would fire a weapon, move the camera, or open another UI.
            Game.DisableControlThisFrame(Control.Attack);
            Game.DisableControlThisFrame(Control.Attack2);
            Game.DisableControlThisFrame(Control.Aim);
            Game.DisableControlThisFrame(Control.MeleeAttack1);
            Game.DisableControlThisFrame(Control.MeleeAttack2);
            Game.DisableControlThisFrame(Control.VehicleAttack);
            Game.DisableControlThisFrame(Control.VehicleAttack2);
            Game.DisableControlThisFrame(Control.NextWeapon);
            Game.DisableControlThisFrame(Control.PrevWeapon);
            Game.DisableControlThisFrame(Control.SelectNextWeapon);
            Game.DisableControlThisFrame(Control.SelectPrevWeapon);
            Game.DisableControlThisFrame(Control.DropWeapon);
            Game.DisableControlThisFrame(Control.Phone);
            Game.DisableControlThisFrame(Control.LookLeftRight);
            Game.DisableControlThisFrame(Control.LookUpDown);
            Game.DisableControlThisFrame(Control.ScaledLookLeftRight);
            Game.DisableControlThisFrame(Control.ScaledLookUpDown);
        }

        private void UpdateSelection()
        {
            var lx = Game.GetDisabledControlValueNormalized(Control.LookLeftRight);
            var ly = Game.GetDisabledControlValueNormalized(Control.LookUpDown);

            if (IsUsingMouse())
            {
                // Deltas: integrate, then clamp to the unit circle.
                _dirX += lx * MouseGain * _cfg.MouseSensitivity;
                _dirY -= ly * MouseGain * _cfg.MouseSensitivity;

                var mag = (float)Math.Sqrt(_dirX * _dirX + _dirY * _dirY);
                if (mag > 1f)
                {
                    _dirX /= mag;
                    _dirY /= mag;
                }
            }
            else
            {
                // Stick: absolute position, y inverted (LookUpDown is down-positive).
                _dirX = lx;
                _dirY = -ly;
            }

            _menu.UpdateSelection(_dirX, _dirY);
        }

        private void HandleInPlaceInput()
        {
            // Edge-triggered, not level-triggered: with IS_DISABLED_CONTROL_PRESSED, simply
            // holding the button would re-fire every frame and cascade through nested pages.

            // Right mouse / left trigger steps back out of a submenu, or closes at the root.
            if (Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)Control.Aim))
            {
                if (!_menu.Back()) CloseWheel();
                _dirX = 0f;
                _dirY = 0f;
                return;
            }

            // Left mouse / right trigger commits without waiting for the hold to be released,
            // which is the only way to reach a submenu in hold mode.
            if (Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)Control.Attack))
            {
                if (_menu.Commit()) CloseWheel();
                _dirX = 0f;
                _dirY = 0f;
            }
        }

        private static bool IsUsingMouse()
        {
            return Function.Call<bool>(Hash.IS_USING_KEYBOARD_AND_MOUSE, 2);
        }

        private void DrawFooterHint()
        {
            var mouse = IsUsingMouse();
            var select = mouse ? "LMB" : "RT";
            var back = mouse ? "RMB" : "LT";

            var hint = _menu.Depth > 1
                ? select + " SELECT     " + back + " BACK"
                : select + " SELECT     " + back + " CLOSE";

            Draw.Text(hint, 0.5f, 0.5f + _cfg.OuterRadius + 0.045f, 0.30f, Palette.TextDim, Draw.FontLabel);
        }
    }
}
