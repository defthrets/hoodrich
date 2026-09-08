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

        /// <summary>
        /// The stock phone animation, which is three clips and not one.
        ///
        /// cellphone_text_in takes it out of his pocket, cellphone_text_read_base is him
        /// holding it and reading, cellphone_text_out puts it away. Playing only the base pose
        /// makes the phone appear in a raised hand with no reach for it, which reads as a
        /// teleport rather than as picking something up.
        ///
        /// Every clip verified against the game's own animation data.
        /// </summary>
        private const string PhoneDict = "cellphone@";
        private const string ClipIn = "cellphone_text_in";
        private const string ClipHold = "cellphone_text_read_base";
        private const string ClipOut = "cellphone_text_out";

        /// <summary>Franklin's own handset, with the generic one behind it.</summary>
        private static readonly string[] PhoneProps = { "prop_phone_cs_frank", "prop_npc_phone_02" };

        /// <summary>PH_R_Hand. The prop helper, so it sits where a hand holds it.</summary>
        private const int RightHandBone = 28422;

        /// <summary>How long the take-out runs before he settles into holding it.</summary>
        private const int IntroMs = 620;

        /// <summary>And how long the put-away runs before the prop goes.</summary>
        private const int OutroMs = 700;

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

        /// <summary>
        /// Show and hide the ringing screen.
        ///
        /// Deliberately NOT through OpenPhone. That one slows time and blurs the world for
        /// somebody about to read a menu; a phone ringing wants the street carrying on around
        /// it, so this goes straight to the handset.
        /// </summary>
        public void ShowIncoming(string who, string pic) { _menu.OpenCall(who, pic); }

        public void HideIncoming() { _menu.CloseCall(); }

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
            // Before anything else, and outside every early return: the put-away clip outlives
            // the menu that started it and the prop has to survive until it has finished.
            TickHandset();

            if (!available)
            {
                if (_menu.IsOpen) ClosePhone();
                EndVanillaMode();
                DropHandset();
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
                // Still on its way down. It draws for a few frames after it stopped taking
                // input, so the handset drops out of frame rather than blinking off.
                if (_menu.Leaving) _menu.Render();

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
            HoldItUp();
            HandleInput();

            _menu.Render();
        }

        // ---- him actually holding it ---------------------------------------------

        private Prop _handset;
        private int _shownAt;
        private bool _holding;
        private int _puttingAwayAt;

        /// <summary>
        /// Takes the phone out, the way the game does it.
        ///
        /// UPPER BODY and SECONDARY (flag 49), so his legs stay his own -- you can still walk
        /// while the phone is up, which is what the stock one lets you do and what anybody
        /// would expect. A full-body clip would root him to the spot the moment the menu
        /// opened.
        /// </summary>
        private void TakeItOut()
        {
            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return;

            // On foot only. cellphone@ is the standing set -- the reach into a pocket and the
            // arm position are both built for a man stood up, and played behind a steering
            // wheel his forearm goes through it. The game swaps to a seated set of its own for
            // this and matching that properly is a bigger job than it is worth right now, so
            // in a car the menu simply opens without the theatre.
            if (player.IsInVehicle()) return;

            _shownAt = Game.GameTime;
            _holding = false;
            _puttingAwayAt = 0;

            try
            {
                if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, PhoneDict))
                {
                    Function.Call(Hash.REQUEST_ANIM_DICT, PhoneDict);
                }

                GiveHandset(player);

                Function.Call(Hash.TASK_PLAY_ANIM, player.Handle, PhoneDict, ClipIn,
                              8f, -8f, -1, 48, 0f, false, 0, false);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not raise the phone: " + ex.Message);
            }
        }

        /// <summary>Settles him into the reading pose once the take-out has played.</summary>
        private void HoldItUp()
        {
            if (_holding || _shownAt == 0) return;
            if (Game.GameTime - _shownAt < IntroMs) return;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            _holding = true;

            try
            {
                Function.Call(Hash.TASK_PLAY_ANIM, player.Handle, PhoneDict, ClipHold,
                              4f, -4f, -1, 49, 0f, false, 0, false);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not hold the phone: " + ex.Message);
            }
        }

        /// <summary>Puts it away, and only bins the prop once the clip has run.</summary>
        private void PutItAway()
        {
            var player = Game.Player.Character;

            _shownAt = 0;
            _holding = false;

            if (player == null || !player.Exists() || !player.IsAlive)
            {
                DropHandset();
                return;
            }

            try
            {
                // Put back if it has already been taken off him. RestoreWorld drops the prop
                // and runs first, so without this he mimes pocketing an empty hand for the
                // whole clip -- which is the one thing the outro exists to avoid.
                GiveHandset(player);

                Function.Call(Hash.TASK_PLAY_ANIM, player.Handle, PhoneDict, ClipOut,
                              8f, -8f, -1, 48, 0f, false, 0, false);

                _puttingAwayAt = Game.GameTime;
            }
            catch
            {
                DropHandset();
            }
        }

        /// <summary>
        /// Ticked even when the menu is shut, because putting it away outlives the menu.
        ///
        /// The outro is most of a second long and the phone closes instantly -- so if the prop
        /// went with the screen his hand would be empty for the whole animation of him putting
        /// something into his pocket.
        /// </summary>
        private void TickHandset()
        {
            if (_puttingAwayAt == 0) return;
            if (Game.GameTime - _puttingAwayAt < OutroMs) return;

            _puttingAwayAt = 0;

            var player = Game.Player.Character;

            try
            {
                if (player != null && player.Exists())
                {
                    Function.Call(Hash.CLEAR_PED_SECONDARY_TASK, player.Handle);
                }
            }
            catch { /* it runs out */ }

            DropHandset();
        }

        private void GiveHandset(Ped player)
        {
            if (_handset != null && _handset.Exists()) return;

            foreach (var name in PhoneProps)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(600)) continue;

                    _handset = World.CreateProp(model, player.Position, false, false);
                    model.MarkAsNoLongerNeeded();

                    if (_handset == null || !_handset.Exists()) continue;

                    var bone = Function.Call<int>(Hash.GET_PED_BONE_INDEX, player.Handle,
                                                  RightHandBone);

                    // PH_R_Hand is a prop helper, so it already sits where a held object goes:
                    // no offset, no rotation.
                    Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, _handset.Handle, player.Handle,
                                  bone, 0f, 0f, 0f, 0f, 0f, 0f, true, true, false, true, 1, true);
                    return;
                }
                catch (Exception ex)
                {
                    Log.Debug("No handset: " + ex.Message);
                }
            }
        }

        private void DropHandset()
        {
            try
            {
                if (_handset != null && _handset.Exists()) _handset.Delete();
            }
            catch { /* gone */ }

            _handset = null;
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

        /// <summary>
        /// One line in the log, the first time the phone button is pressed.
        ///
        /// THIS WAS A GATE AND IT BROKE THE PHONE TWICE, so it is a question now instead of an
        /// answer. The idea was sound -- a mod menu opened on a hotkey takes the arrow keys by
        /// disabling them, and Pressed reads through that because it is
        /// IS_DISABLED_CONTROL_PRESSED, so scrolling somebody else's menu opened ours on top.
        ///
        /// WHAT IS NOT SOUND IS TESTING A CONTROL WE DISABLE OURSELVES. SuppressVanillaPhone
        /// turns off INPUT_PHONE every single frame to keep the game's handset down. Sampling
        /// above that call only gives an honest answer if the game has already cleared the
        /// PREVIOUS frame's disable by the time our tick runs, and that ordering is not
        /// something to bet a working phone on. It read disabled, the gate held the button off
        /// for good, and no amount of picking a different control number fixes the shape of it.
        ///
        /// So the button works again and this writes down what the natives actually say, once,
        /// so the next attempt starts from a measurement rather than from my reasoning about
        /// what the game probably does.
        /// </summary>
        private void Probe()
        {
            if (_probed) return;

            _probed = true;

            try
            {
                var phone = Function.Call<bool>(Hash.IS_CONTROL_ENABLED, 0, (int)Control.Phone);
                var up = Function.Call<bool>(Hash.IS_CONTROL_ENABLED, 2, (int)Control.PhoneUp);

                Log.Info("Phone probe on first press: INPUT_PHONE(0,27) enabled=" + phone +
                         ", INPUT_CELLPHONE_UP(2,172) enabled=" + up + ".");
            }
            catch (Exception ex)
            {
                Log.Info("Phone probe failed: " + ex.Message);
            }
        }

        private bool _probed;

        /// <summary>
        /// Which other script has a menu up right now, or null.
        ///
        /// NOT A CONTROL, WHICH IS THE WHOLE POINT. The reason a control-based gate cannot work
        /// is written out on Probe: we disable INPUT_PHONE ourselves every frame, so asking the
        /// game whether it is enabled is asking a question we already answered, and the answer
        /// depends on which script ran first this frame. A variable has no frame order.
        ///
        /// Every SHVDN script lives in the one AppDomain, so its data slots are shared. The
        /// convention is small: a script that opens a menu sets "MenuOpen" to its own name for as
        /// long as the menu is up, and a script with a hotkey looks before it listens. Vehicle
        /// Tweaks sets it; anything else that adopts the same name gets the same courtesy.
        /// </summary>
        private static string MenuOwner()
        {
            return Menus.Owner();
        }

        private string _yielded;

        /// <summary>Rising edge of whatever opens the phone.</summary>
        private bool ReadOpenEdge()
        {
            // SOMEBODY ELSE'S MENU IS UP, AND THEIR ARROW KEYS ARE NOT OUR PHONE BUTTON. Up on a
            // settings panel is the same physical key as the phone, and this used to open on it.
            // Read as "not down" rather than returned from early, so whatever edge memory sits
            // below this sees an ordinary released frame instead of a stale one.
            var owner = MenuOwner();

            if (owner != null && _yielded != owner)
            {
                _yielded = owner;
                Log.Info("Phone button left alone while " + owner + " has a menu open.");
            }

            if (owner == null) _yielded = null;

            var down = owner == null && Pressed(Control.Phone);

            if (down) Probe();

            // A KEY OF YOUR OWN STILL WORKS, AND IS DELIBERATELY NOT GATED ABOVE.
            //
            // It is read straight off the keyboard, so no other mod can disable it -- and that
            // makes it the way back in if something out there holds the arrow keys down for
            // good rather than only while a menu is up. The arrow key defers to whoever else
            // claimed it; a key you chose yourself answers to nobody.
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

        /// <summary>When a held press should actually open its app, or nought. See HandleInput.</summary>
        private int _actAt;

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
            // A ringing phone is not a menu. Answer and decline belong to the call, and the
            // navigation below would be walking a page stack that is not there.
            if (_menu.InCall) return;

            if (Repeat(1, Pressed(Control.PhoneUp))) _menu.MoveRow(-1);
            else if (Repeat(2, Pressed(Control.PhoneDown))) _menu.MoveRow(1);
            else if (Repeat(3, Pressed(Control.PhoneLeft))) _menu.MoveColumn(-1);
            else if (Repeat(4, Pressed(Control.PhoneRight))) _menu.MoveColumn(1);

            // A PRESSED APP PLAYS ITS ANIMATION BEFORE THE PAGE MOVES.
            //
            // The page used to change on the same frame as the press, so the pressed icon was
            // never drawn once in its pressed state -- the only acknowledgement was the thing
            // you asked for arriving. A tenth of a second is under the threshold where anybody
            // calls a menu slow, and it is the whole difference between a button and a jump.
            //
            // Held HERE rather than in the menu because this is about when the press takes
            // effect; the menu's job is to draw it.
            if (_actAt != 0)
            {
                if (Game.GameTime < _actAt) return;

                _actAt = 0;
                _menu.ClearPress();

                var late = _menu.Pick();
                if (late != null) Act(late);
                return;
            }

            if (JustPressed(Control.PhoneSelect))
            {
                // Only the home grid takes the beat. Press says whether it did.
                if (_menu.Press())
                {
                    _actAt = Game.GameTime + PhoneMenu.PressMs;
                    return;
                }

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

            // Some rows take the phone with them. Texting a plug puts a handset in his hand
            // and loops him reading it, and both that and our put-away run on cellphone@ --
            // so playing ours over the top pocketed a phone he had only just taken out.
            ClosePhone(item.KeepsPhoneUp);

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

        /// <summary>
        /// Brings it out already on one page, with nothing behind it.
        ///
        /// For something that needs an answer rather than a browse -- the car asking where you
        /// are going. No page stack, so backing out puts the phone away instead of walking you
        /// to a home screen you did not ask for.
        /// </summary>
        public void OpenAt(WheelPage page)
        {
            if (page == null || _menu.IsOpen) return;

            _menu.Open(page);
            TakeItOut();

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

            // He reaches into his pocket for it, same as he does for his own.
            TakeItOut();

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
            ClosePhone(false);
        }

        /// <summary>
        /// Puts the phone away. <paramref name="handingOver"/> means something else is about
        /// to use it, so ours leaves without the theatre.
        /// </summary>
        private void ClosePhone(bool handingOver)
        {
            var was = _menu.IsOpen;

            // A press that was still playing when the phone shut does not get to open its app
            // a tenth of a second later on a menu that is no longer there.
            _actAt = 0;
            _menu.ClearPress();

            _menu.Close();
            RestoreWorld();

            // AFTER RestoreWorld, which drops the prop. He needs it back in his hand for the
            // length of the put-away clip, and TickHandset takes it off him when that ends.
            if (was && !handingOver) PutItAway();

            if (handingOver)
            {
                // Not merely skipping the outro -- the pending clear has to go too, or it
                // fires most of a second later and takes the OTHER animation's secondary task
                // off him instead of ours.
                _shownAt = 0;
                _holding = false;
                _puttingAwayAt = 0;
            }

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
            DropHandset();

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
        /// Kept as a name rather than inlined, because "stop him swinging" is what the call
        /// sites mean and TickQuiet's whole job is to say it again a frame later. The list it
        /// used to hold lives in Core.Fists now -- it was right here and wrong in nine other
        /// places, which is the wrong way round for a list nine screens depend on.
        /// </summary>
        private static void Swing()
        {
            Core.Fists.Off();
        }

        private static void LockControlsThisFrame()
        {
            Swing();

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
