using System;
using GTA;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.State;
using Control = GTA.Control;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.UI
{
    /// <summary>
    /// The mask counter: every face covering the body you are in can wear, and the money to
    /// take one home.
    ///
    /// IT ASKS THE GAME RATHER THAN HOLDING A LIST. There is no native that says "drawable 7
    /// is a hockey mask" -- nothing in this game names any of it -- so a hardcoded catalogue
    /// would be a list of guesses that is wrong the moment a clothing pack lands. The counter
    /// walks the slot live with GET_NUMBER_OF_PED_DRAWABLE_VARIATIONS and shows whatever is
    /// actually there, which is why it needs no maintenance and cannot advertise something the
    /// install has not got.
    ///
    /// WHICH IS ALSO WHY THE SHELF CHANGES SIZE. A drawable index means something different on
    /// every body: Franklin's mask slot holds a handful, and the online freemode bodies hold
    /// the whole Online mask catalogue. Walk in as Franklin and the shelf is short. Walk in
    /// wearing one of the online bodies -- which the wardrobe at Denise's already records
    /// outfits for -- and every mask Online has is on it. The shop does not have to know which
    /// body you are in; it just counts.
    ///
    /// WHAT IS BOUGHT IS KEPT, per body. A mask you have paid for is yours and costs nothing
    /// the next time; one you have not is charged for once and then never again. The record
    /// carries the model hash for the same reason the wardrobe's does -- the thirty-first mask
    /// on one body is a different object on another, and paying for one must not silently
    /// unlock the other.
    ///
    /// THE PREVIEW IS THE PLAYER. There is nowhere in this mod to render a floating head, and
    /// there does not need to be: he is stood in the shop wearing whatever the cursor is on.
    /// Walk out without buying and he has it taken off him again.
    /// </summary>
    internal sealed class MaskScreen
    {
        private enum Row { Mask, Colour, Take, Done }

        /// <summary>Set by Main: takes the money, false when he cannot cover it.</summary>
        public Func<int, bool> Charge;

        /// <summary>Set by Main: writes the save.</summary>
        public Action Save;

        /// <summary>Set by Main: what a new one costs.</summary>
        public Func<int> Price;

        public PlayerState State;
        public Settings Cfg;

        private readonly Curtain _curtain = new Curtain();

        private Row _row;
        private int _lastRow = -1;
        private int _pickedAt;
        private int _openedAt;

        /// <summary>What he had on and whether he had it on, so walking out puts it all back.</summary>
        private int _wasDrawable;
        private int _wasTexture;
        private bool _wasOn;
        private bool _bought;

        private const float PanelWidthH = 0.46f;
        private const float RowHeight = 0.040f;
        private const float PadH = 0.024f;
        private const int OpenGraceMs = 220;

        public bool IsOpen => _curtain.Showing;

        public void Open()
        {
            if (Cfg == null) return;

            _row = Row.Mask;
            _lastRow = -1;
            _bought = false;
            _pickedAt = _openedAt = Game.GameTime;

            _wasDrawable = Cfg.MaskDrawable;
            _wasTexture = Cfg.MaskTexture;
            _wasOn = Mask.Wearing;

            // On, so the first thing he sees is the first thing on the shelf.
            Mask.On(Cfg);

            _curtain.Open();
            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        /// <summary>Out without buying: back to the face he came in with.</summary>
        public void Close()
        {
            if (!IsOpen) return;

            if (!_bought)
            {
                Cfg.MaskDrawable = _wasDrawable;
                Cfg.MaskTexture = _wasTexture;

                if (_wasOn) Mask.Refresh(Cfg);
                else Mask.Off(Cfg);
            }

            InputGuard.Swallow();
            _curtain.Close();
            Hud.PlaySound("BACK", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        public void Update()
        {
            if (!IsOpen) return;

            LockControls();

            if (!_curtain.Taking) return;
            if (Game.GameTime - _openedAt < OpenGraceMs) return;

            if (Cfg == null || Count() <= 0) { Close(); return; }

            if (Pressed(Control.PhoneCancel)) { Close(); return; }

            if (Pressed(Control.PhoneUp)) Move(-1);
            else if (Pressed(Control.PhoneDown)) Move(1);
            else if (_row == Row.Mask && Pressed(Control.PhoneLeft)) Wear(-1, 0);
            else if (_row == Row.Mask && Pressed(Control.PhoneRight)) Wear(1, 0);
            else if (_row == Row.Colour && Pressed(Control.PhoneLeft)) Wear(0, -1);
            else if (_row == Row.Colour && Pressed(Control.PhoneRight)) Wear(0, 1);
            else if (Pressed(Control.PhoneSelect) || Pressed(Control.Context)) Choose();
        }

        private void Move(int step)
        {
            var rows = Enum.GetValues(typeof(Row)).Length;
            _lastRow = (int)_row;
            _row = (Row)(((int)_row + step + rows) % rows);
            _pickedAt = Game.GameTime;
            Hud.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        private void Choose()
        {
            switch (_row)
            {
                case Row.Mask: Wear(1, 0); break;
                case Row.Colour: Wear(0, 1); break;
                case Row.Take: Take(); break;
                default: Close(); break;
            }
        }

        // ---- the shelf -------------------------------------------------------------

        private int Count()
        {
            return Mask.Count(Cfg);
        }

        private int Colours()
        {
            var n = Mask.Textures(Cfg);
            return n > 0 ? n : 1;
        }

        /// <summary>
        /// Along the shelf, or through the colours of the one under the cursor.
        ///
        /// THE COLOUR IS CLAMPED WHEN THE MASK CHANGES. Every drawable carries its own number
        /// of textures and they are nothing like each other -- one has eight, the next has one.
        /// Carrying the old index across is how you ask for texture six of a mask that has two,
        /// which the game answers by putting nothing on his head at all.
        /// </summary>
        private void Wear(int mask, int colour)
        {
            var n = Count();
            if (n <= 0) return;

            if (mask != 0)
            {
                Cfg.MaskDrawable = (Cfg.MaskDrawable + mask + n) % n;
                Cfg.MaskTexture = 0;
            }

            if (colour != 0)
            {
                var c = Colours();
                Cfg.MaskTexture = (Cfg.MaskTexture + colour + c) % c;
            }
            else if (Cfg.MaskTexture >= Colours())
            {
                Cfg.MaskTexture = 0;
            }

            Mask.Refresh(Cfg);

            _pickedAt = Game.GameTime;
            Hud.PlaySound("NAV_LEFT_RIGHT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        // ---- what is yours ---------------------------------------------------------

        /// <summary>
        /// One mask, on one body.
        ///
        /// The model is in the key for the reason the wardrobe gives: a drawable index is an
        /// index into ONE model's wardrobe, and the thirty-first mask on Franklin is a
        /// different object from the thirty-first on a freemode body. Paying for one must not
        /// quietly hand you the other.
        /// </summary>
        private static string Key(Settings cfg)
        {
            var me = Game.Player.Character;
            var body = me != null && me.Exists()
                ? unchecked((uint)me.Model.Hash).ToString("X8")
                : "00000000";

            return body + "|" + cfg.MaskSlot + ":" + cfg.MaskDrawable + ":" + cfg.MaskTexture;
        }

        private bool Owned()
        {
            return State != null && Cfg != null && State.Masks.Contains(Key(Cfg));
        }

        private void Take()
        {
            if (Cfg == null || Count() <= 0) return;

            if (Owned())
            {
                _bought = true;
                Keep();
                return;
            }

            var cost = Price == null ? 0 : Price();

            if (cost > 0 && (Charge == null || !Charge(cost)))
            {
                Notify.Problem("you can't cover that.");
                Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                return;
            }

            State?.Masks.Add(Key(Cfg));

            _bought = true;
            Keep();
        }

        /// <summary>Yours. It stays on his face and the choice is written down.</summary>
        private void Keep()
        {
            try { Save?.Invoke(); }
            catch (Exception ex) { Log.Debug("Could not save the mask: " + ex.Message); }

            State?.Touch();

            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");

            InputGuard.Swallow();
            _curtain.Close();
        }

        // ---- input ------------------------------------------------------------------

        private static bool Pressed(Control control)
        {
            return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)control);
        }

        private static void LockControls()
        {
            Function.Call(Hash.DISABLE_ALL_CONTROL_ACTIONS, 0);

            foreach (var control in new[]
                     {
                         Control.PhoneUp, Control.PhoneDown, Control.PhoneLeft, Control.PhoneRight,
                         Control.PhoneSelect, Control.PhoneCancel,
                         Control.LookLeftRight, Control.LookUpDown
                     })
            {
                Function.Call(Hash.ENABLE_CONTROL_ACTION, 0, (int)control, true);
            }
        }

        // ---- drawing -----------------------------------------------------------------

        public void Draw()
        {
            if (!IsOpen || Cfg == null) return;

            var width = Hud.ToX(PanelWidthH);
            var left = 0.5f - width * 0.5f;
            var pad = Hud.ToX(PadH);
            var height = 0.170f + 4 * RowHeight;
            var top = 0.5f - height * 0.5f + _curtain.Lift;

            Theme.Panel(left, top, width, height);

            var x = left + pad;
            var right = left + width - pad;
            var y = top + 0.020f;

            Hud.Text("MASKS", x, y - 0.004f, 0.74f, Palette.Text, Hud.FontCursive, centre: false);
            Hud.TextRight("VESPUCCI BEACH", right, y + 0.010f, 0.34f, Palette.TextDim);

            y += 0.052f;

            var n = Count();
            var cost = Price == null ? 0 : Price();

            Hud.Text("NOBODY NEEDS TO KNOW IT WAS YOU", x, y, 0.26f, Palette.TextDim,
                     Hud.FontLabel, centre: false);
            Hud.TextRight(n + " ON THE SHELF", right, y, 0.24f, Palette.TextDim);

            y += 0.026f;
            Theme.Rule(x, y, right - x);
            y += 0.010f;

            var grown = Theme.Grown(_pickedAt);
            var barWide = (right - x) + pad * 0.7f;
            var owned = Owned();

            for (var i = 0; i < 4; i++)
            {
                var here = i == (int)_row;
                var lit = Theme.Lit(i, (int)_row, _lastRow, grown);

                Theme.Plate(x - pad * 0.35f, y - 0.004f, barWide, RowHeight, lit);
                Theme.Sheen(x - pad * 0.35f, y - 0.004f, barWide, RowHeight, lit);

                var ink = Theme.Ink(here ? Palette.Text : Palette.TextDim, lit);

                string label, value;

                switch ((Row)i)
                {
                    case Row.Mask:
                        label = "MASK";
                        value = Arrows(here, n <= 0 ? "NONE" : (Cfg.MaskDrawable + 1) + " OF " + n);
                        break;

                    case Row.Colour:
                        label = "COLOUR";
                        value = Arrows(here, (Cfg.MaskTexture + 1) + " OF " + Colours());
                        break;

                    case Row.Take:
                        label = owned ? "WEAR IT" : "BUY IT";
                        value = owned ? "YOURS" : (cost > 0 ? "$" + cost : "FREE");
                        break;

                    default:
                        label = "LEAVE IT";
                        value = "";
                        break;
                }

                Hud.Text(label, x, y + 0.008f, 0.32f, ink, Hud.FontBody, centre: false);
                if (value.Length > 0) Hud.TextRight(value, right, y + 0.009f, 0.30f, ink, Hud.FontBody);

                y += RowHeight;
            }

            Hud.Text(Hud.OnPad
                         ? "D-PAD  MOVE      LEFT/RIGHT  TRY ONE      A  TAKE IT      B  LEAVE"
                         : "UP/DOWN  MOVE      LEFT/RIGHT  TRY ONE      ENTER  TAKE IT      BACKSPACE  LEAVE",
                     x, top + height - 0.030f, 0.24f, Palette.TextDim, Hud.FontLabel, centre: false);
        }

        private static string Arrows(bool here, string value)
        {
            return here ? "<  " + value + "  >" : value;
        }
    }
}
