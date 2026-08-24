using System;
using Keys = System.Windows.Forms.Keys;
using Control = GTA.Control;
using GTA;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.UI;

namespace Hoodrich.Phone
{
    /// <summary>
    /// Owns phone input, vanilla-phone suppression, and the open/close side effects.
    ///
    /// The vanilla phone is suppressed the same way the weapon wheel used to be: the control is
    /// disabled every frame, which stops the game acting on it while still letting us READ it
    /// through IS_DISABLED_CONTROL_JUST_PRESSED. So the same button that used to raise
    /// Franklin's phone raises ours, and nothing has to be hooked.
    ///
    /// What this deliberately does NOT touch is the weapon wheel. That was the whole point of
    /// the change: SelectWeapon is the game's again -- un-disabled, un-hidden, un-read -- and
    /// behaves exactly as it does without the mod loaded.
    ///
    /// Navigation is on the CELLPHONE controls rather than raw keys, which is both what a phone
    /// should answer to and what every other screen in this mod already uses. On keyboard those
    /// are the arrow keys, Enter and Backspace; on a pad they are the d-pad, A and B. Reading
    /// System.Windows.Forms keys instead would have meant hand-rolling a "just pressed" for
    /// every one of them and would have left pad users with no phone at all.
    /// </summary>
    internal sealed class PhoneController
    {
        /// <summary>HUD component id for the phone, hidden while ours is up.</summary>
        private const int HudCellphone = 21;

        /// <summary>Repeat delay for a held direction, then the rate once it kicks in.</summary>
        private const int RepeatFirstMs = 330;
        private const int RepeatThenMs = 90;

        private readonly Settings _cfg;
        private readonly PhoneMenu _menu;
        private readonly Func<WheelPage> _rootBuilder;

        private bool _timeScaleApplied;
        private bool _timecycleApplied;

        private bool _wasOpenPressed;

        private int _heldDir;
        private int _heldSince;
        private int _lastRepeat;

        public PhoneController(Settings cfg, PhoneMenu menu, Func<WheelPage> rootBuilder)
        {
            _cfg = cfg;
            _menu = menu;
            _rootBuilder = rootBuilder;
        }

        public bool IsOpen => _menu.IsOpen;

        /// <summary>
        /// Set by Main. True when something else already owns the screen.
        ///
        /// Main's per-frame chain already returns before this runs whenever a full screen is
        /// up, so this is a second belt: a conversation or a mission prompt is not a "screen"
        /// in that sense and would otherwise be talked over by a handset.
        /// </summary>
        public Func<bool> Busy;

        // ---- per frame ----------------------------------------------------------

        public void Update(bool available)
        {
            if (!available)
            {
                if (_menu.IsOpen) ClosePhone();
                EndVanillaMode();
                return;
            }

            if (_vanillaMode)
            {
                VanillaFrame(available);
                return;
            }

            SuppressVanillaPhone();

            var edge = ReadOpenEdge();

            if (!_menu.IsOpen)
            {
                if (Game.GameTime < _quietUntil) Swing();
                if (edge && (Busy == null || !Busy())) OpenPhone();
                return;
            }

            // Closing on the phone button is CONDITIONAL, and the condition is the whole
            // reason this is not a plain toggle.
            //
            // On PC, INPUT_PHONE and INPUT_CELLPHONE_UP are both the up arrow by default. A
            // plain toggle therefore reads "move up the list" as "put the phone away", and the
            // menu shuts the first time anybody tries to scroll it. Requiring that the up
            // direction is NOT also down disambiguates them: when they are the same key, the
            // phone button never closes and Backspace does it instead (which is what the
            // footer says and what the vanilla phone does anyway); when a player has rebound
            // them apart, the button closes it properly.
            if (edge && !Pressed(Control.PhoneUp)) { ClosePhone(); return; }

            LockControlsThisFrame();
            HandleInput();

            _menu.Render();
        }

        // ---- handing it back ----------------------------------------------------

        private bool _vanillaMode;
        private bool _vanillaUsed;
        private int _vanillaUntil;

        /// <summary>
        /// Gives the phone button back to the game for a few seconds.
        ///
        /// There is no native that opens the vanilla phone. CREATE_MOBILE_PHONE makes the prop
        /// and nothing else -- the actual interface is the game's own cellphone script reacting
        /// to INPUT_PHONE, so the only honest way to show it is to stop suppressing the button
        /// and let the player press it. That is the same thing the old wheel did to hand back
        /// the weapon wheel, and for the same reason.
        /// </summary>
        public void ShowVanillaPhone()
        {
            _menu.Close();
            RestoreWorld();

            _vanillaMode = true;
            _vanillaUsed = false;
            _vanillaUntil = Game.GameTime + Math.Max(1, _cfg.VanillaPhoneSeconds) * 1000;
        }

        private void EndVanillaMode()
        {
            _vanillaMode = false;
            _vanillaUsed = false;
        }

        /// <summary>
        /// Hands entirely off: nothing disabled, nothing hidden, nothing forced.
        ///
        /// Whether they took the offer is read from the ped rather than from the button --
        /// IS_PED_RUNNING_MOBILE_PHONE_TASK is true for exactly as long as the phone is
        /// actually up, so the handoff ends when they put it away rather than on a timer that
        /// might snatch it back mid-call.
        /// </summary>
        private void VanillaFrame(bool available)
        {
            var player = Game.Player.Character;

            var up = player != null && player.Exists() &&
                     Function.Call<bool>(Hash.IS_PED_RUNNING_MOBILE_PHONE_TASK, player.Handle);

            if (up) _vanillaUsed = true;

            if (!up && !_vanillaUsed)
            {
                Help.ShowThisFrame("Press ~INPUT_PHONE~ for your own phone.");
            }

            var finished = _vanillaUsed ? !up : Game.GameTime >= _vanillaUntil;

            if (!available || finished)
            {
                EndVanillaMode();

                // And do not let the same press fall straight back into ours.
                _wasOpenPressed = true;
            }
        }

        /// <summary>
        /// Held every frame so the game's own phone never gets a chance to come up.
        ///
        /// The directional phone controls go with it. They are the arrow keys, and leaving them
        /// live meant navigating our list ALSO drove a vanilla phone that was not visible --
        /// so putting ours away left the real one sitting on whatever it had wandered onto.
        /// </summary>
        private static void SuppressVanillaPhone()
        {
            Game.DisableControlThisFrame(Control.Phone);
            Game.DisableControlThisFrame(Control.PhoneUp);
            Game.DisableControlThisFrame(Control.PhoneDown);
            Game.DisableControlThisFrame(Control.PhoneLeft);
            Game.DisableControlThisFrame(Control.PhoneRight);
            Game.DisableControlThisFrame(Control.PhoneSelect);
            Game.DisableControlThisFrame(Control.PhoneCancel);

            Function.Call(Hash.HIDE_HUD_COMPONENT_THIS_FRAME, HudCellphone);
        }

        /// <summary>Rising edge of whatever opens the phone.</summary>
        private bool ReadOpenEdge()
        {
            var down = Pressed(Control.Phone);

            // A key of your own still works, for anybody who would rather keep the real phone.
            if (!down && _cfg.PhoneKey != Keys.None)
            {
                if (_cfg.PhoneModifier == Keys.None || Game.IsKeyPressed(_cfg.PhoneModifier))
                {
                    down = Game.IsKeyPressed(_cfg.PhoneKey);
                }
            }

            var edge = down && !_wasOpenPressed;
            _wasOpenPressed = down;
            return edge;
        }

        /// <summary>Set on open; cleared once every direction has actually been let go.</summary>
        private bool _navBlocked;

        private void HandleInput()
        {
            if (_navBlocked)
            {
                if (Pressed(Control.PhoneUp) || Pressed(Control.PhoneDown)
                    || Pressed(Control.PhoneLeft) || Pressed(Control.PhoneRight)
                    || Pressed(Control.PhoneSelect))
                {
                    return;
                }

                _navBlocked = false;
            }

            // Up and down always move. On the home grid that is a whole row of apps; in a list
            // it is one line.
            if (Repeat(1, Pressed(Control.PhoneUp))) _menu.MoveRow(-1);
            else if (Repeat(2, Pressed(Control.PhoneDown))) _menu.MoveRow(1);
            else if (Repeat(3, Pressed(Control.PhoneLeft))) _menu.MoveColumn(-1);
            else if (Repeat(4, Pressed(Control.PhoneRight))) _menu.MoveColumn(1);

            if (JustPressed(Control.PhoneSelect))
            {
                var picked = _menu.Pick();
                if (picked != null) Act(picked);
                return;
            }

            if (JustPressed(Control.PhoneCancel))
            {
                if (!_menu.Back()) ClosePhone();
            }
        }

        private static bool Pressed(Control c)
        {
            return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)c);
        }

        private static bool JustPressed(Control c)
        {
            return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)c);
        }

        /// <summary>
        /// Key-repeat, so a held direction scrolls a long list instead of stepping once.
        ///
        /// One slot rather than one per direction, because only one direction can be the
        /// active one -- pressing down while up is held simply takes over, which is what a
        /// d-pad does anyway.
        /// </summary>
        private bool Repeat(int dir, bool down)
        {
            if (!down)
            {
                if (_heldDir == dir) _heldDir = 0;
                return false;
            }

            var now = Game.GameTime;

            if (_heldDir != dir)
            {
                _heldDir = dir;
                _heldSince = now;
                _lastRepeat = now;
                return true;
            }

            var wait = now - _heldSince < RepeatFirstMs ? RepeatFirstMs : RepeatThenMs;
            if (now - _lastRepeat < wait) return false;

            _lastRepeat = now;
            return true;
        }

        private void Act(WheelItem item)
        {
            if (item == null) return;

            // Closed FIRST, then the action runs. Half these items open a screen of their own
            // -- the feed, the stash, the settings -- and those cannot be drawn inside a
            // handset; opening one underneath a phone that is still drawing over the top of it
            // is how you get a screen you can hear but not see.
            var action = item.OnSelect;

            ClosePhone();

            try
            {
                action?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Error("A phone action threw.", ex);
                Notify.Problem("that didn't work.");
            }
        }

        // ---- open and close -----------------------------------------------------

        private void OpenPhone()
        {
            WheelPage root;
            try
            {
                root = _rootBuilder();
            }
            catch (Exception ex)
            {
                Log.Error("Phone home page builder threw; not opening.", ex);
                return;
            }

            if (root == null) return;

            _menu.Open(root);

            // The key that opened it is still down, and on the default binding it is also the
            // "up" key -- so without this the phone opens and instantly scrolls itself.
            _navBlocked = true;

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

        /// <summary>Attack stays dead until this, so the closing press cannot land.</summary>
        private int _quietUntil;

        private const int QuietAfterCloseMs = 350;

        public void ClosePhone()
        {
            _menu.Close();
            RestoreWorld();

            // Disabling a control only lasts the frame you disable it on, and the button that
            // closed the phone is still held down on the NEXT one -- which is the frame we
            // have already stopped drawing and stopped locking. That gap is exactly one punch
            // wide, so it is held shut for a moment afterwards instead.
            _quietUntil = Game.GameTime + QuietAfterCloseMs;

            // So the world does not eat the same press that put the phone away.
            InputGuard.Swallow();
        }

        /// <summary>Puts back everything opening it changed. Safe to call twice.</summary>
        public void RestoreWorld()
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

        /// <summary>
        /// Everything the player must not be doing while a phone is up in front of them.
        ///
        /// Aim and attack especially: a held right mouse button behind the handset means the
        /// player comes back to the world already aiming at whatever the camera drifted onto.
        ///
        /// SelectWeapon is NOT in this list even while the phone is open, and that is
        /// deliberate -- it is the one control this whole change exists to hand back, and a
        /// player who wants their gun out mid-menu is allowed to have it.
        /// </summary>
        /// <summary>
        /// Every control that throws a punch or pulls a trigger.
        ///
        /// There are NINE of these and the first pass disabled four, which is why closing the
        /// phone swung a fist: Backspace and pad B both land on melee inputs that were not in
        /// the list, so the press that put the handset away went straight through to Franklin.
        /// Attack and MeleeAttack1/2 are the obvious ones; MeleeAttackLight, MeleeAttackHeavy,
        /// MeleeAttackAlternate and MeleeBlock are the ones that actually fire on a pad.
        /// </summary>
        private static void Swing()
        {
            Game.DisableControlThisFrame(Control.Attack);
            Game.DisableControlThisFrame(Control.Attack2);
            Game.DisableControlThisFrame(Control.MeleeAttack1);
            Game.DisableControlThisFrame(Control.MeleeAttack2);
            Game.DisableControlThisFrame(Control.MeleeAttackLight);
            Game.DisableControlThisFrame(Control.MeleeAttackHeavy);
            Game.DisableControlThisFrame(Control.MeleeAttackAlternate);
            Game.DisableControlThisFrame(Control.MeleeBlock);
            Game.DisableControlThisFrame(Control.VehicleMeleeHold);
        }

        private static void LockControlsThisFrame()
        {
            Swing();

            Game.DisableControlThisFrame(Control.Aim);
            Game.DisableControlThisFrame(Control.VehicleAttack);
            Game.DisableControlThisFrame(Control.VehicleAttack2);

            Game.DisableControlThisFrame(Control.SelectNextWeapon);
            Game.DisableControlThisFrame(Control.SelectPrevWeapon);
            Game.DisableControlThisFrame(Control.DropWeapon);

            Game.DisableControlThisFrame(Control.Cover);
            Game.DisableControlThisFrame(Control.Jump);
            Game.DisableControlThisFrame(Control.Enter);
            Game.DisableControlThisFrame(Control.Duck);
            Game.DisableControlThisFrame(Control.Context);

            Game.DisableControlThisFrame(Control.CursorScrollUp);
            Game.DisableControlThisFrame(Control.CursorScrollDown);
        }
    }
}
