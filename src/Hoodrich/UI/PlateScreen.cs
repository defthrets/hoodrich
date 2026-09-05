using System;
using System.Text;
using GTA;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.State;
using Control = GTA.Control;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.UI
{
    /// <summary>
    /// The plate on a car you have just bought: what it says, and what style it is.
    ///
    /// A small panel, up the moment the keys are yours. Enter on the number opens the game's
    /// own keyboard; left and right on the style walk every plate the game has -- the six the
    /// story shipped with and the seven Online added -- and each one goes onto the car the
    /// moment it is chosen, because the car is stood right there and the car is the preview.
    ///
    /// The plate is also how the mod finds the car again (see OwnedCars), so a custom number
    /// is written into the owned record and saved, and two of your cars cannot wear the same
    /// one.
    /// </summary>
    internal sealed class PlateScreen
    {
        private enum Row { Number, Style, Done }

        /// <summary>Set by Main: writes the save.</summary>
        public Action Save;

        /// <summary>Set by Main: whether another car of yours already wears this number.</summary>
        public Func<OwnedCar, string, bool> Taken;

        private readonly Curtain _curtain = new Curtain();

        private Vehicle _car;
        private OwnedCar _owned;
        private string _name = "";
        private Row _row;
        private int _lastRow = -1;
        private int _pickedAt;
        private int _openedAt;
        private int _style;
        private string _text = "";
        private bool _dirty;

        private const float PanelWidthH = 0.46f;
        private const float RowHeight = 0.040f;
        private const float PadH = 0.024f;
        private const int OpenGraceMs = 220;

        /// <summary>The game's own limit on a plate.</summary>
        private const int Longest = 8;

        /// <summary>
        /// Every plate the game has, by index: the story's six, then the seven that came with
        /// the tuners. Anything the game reports past the end of this is "Style N".
        /// </summary>
        private static readonly string[] Styles =
        {
            "Blue on white", "Yellow on black", "Yellow on blue", "Blue on white 2",
            "Blue on white 3", "North Yankton", "eCola", "Las Venturas", "Liberty City",
            "LS Car Meet", "LS Panic", "LS Pounders", "Sprunk"
        };

        public bool IsOpen => _curtain.Showing;

        public void Open(Vehicle car, OwnedCar owned, string name)
        {
            if (car == null || !car.Exists() || owned == null) return;

            _car = car;
            _owned = owned;
            _name = name ?? "";
            _row = Row.Number;
            _lastRow = -1;
            _dirty = false;
            _pickedAt = _openedAt = Game.GameTime;

            try
            {
                _text = (Function.Call<string>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT, car.Handle) ?? "").Trim();
                _style = Function.Call<int>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT_INDEX, car.Handle);
            }
            catch
            {
                _text = owned.Plate;
                _style = owned.PlateStyle;
            }

            _curtain.Open();
            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        public void Close()
        {
            Leave("BACK");
        }

        private void Leave(string sound)
        {
            if (!IsOpen) return;

            // Whatever was chosen is already on the car, so it is kept either way.
            if (_dirty)
            {
                try { Save?.Invoke(); }
                catch (Exception ex) { Log.Debug("Could not save the plate: " + ex.Message); }
                _dirty = false;
            }

            InputGuard.Swallow();
            _curtain.Close();
            Hud.PlaySound(sound, "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        public void Update()
        {
            if (!IsOpen) return;

            LockControls();

            if (!_curtain.Taking) return;
            if (Game.GameTime - _openedAt < OpenGraceMs) return;

            if (_car == null || !_car.Exists()) { Close(); return; }

            if (Pressed(Control.PhoneCancel)) { Close(); return; }

            if (Pressed(Control.PhoneUp)) Move(-1);
            else if (Pressed(Control.PhoneDown)) Move(1);
            else if (_row == Row.Style && Pressed(Control.PhoneLeft)) Cycle(-1);
            else if (_row == Row.Style && Pressed(Control.PhoneRight)) Cycle(1);
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
                case Row.Number: Type(); break;
                case Row.Style: Cycle(1); break;
                case Row.Done: Leave("SELECT"); break;
            }
        }

        // ---- the style -----------------------------------------------------------

        /// <summary>How many plates this build of the game has. The list above when it will not say.</summary>
        private static int Count()
        {
            try
            {
                var n = Function.Call<int>(Hash.GET_NUMBER_OF_VEHICLE_NUMBER_PLATES);
                return n >= 6 ? n : Styles.Length;
            }
            catch
            {
                return Styles.Length;
            }
        }

        private static string StyleName(int i)
        {
            return i >= 0 && i < Styles.Length ? Styles[i] : "Style " + (i + 1);
        }

        private void Cycle(int by)
        {
            var n = Count();
            _style = (_style + by + n) % n;
            _pickedAt = Game.GameTime;

            try { Function.Call(Hash.SET_VEHICLE_NUMBER_PLATE_TEXT_INDEX, _car.Handle, _style); }
            catch { /* the panel still says what it would have been */ }

            _owned.PlateStyle = _style;
            _dirty = true;

            Hud.PlaySound("NAV_LEFT_RIGHT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        // ---- the number ----------------------------------------------------------

        private void Type()
        {
            string typed;

            try
            {
                typed = Game.GetUserInput(WindowTitle.EnterMessage20, _text, Longest);
            }
            catch (Exception ex)
            {
                Log.Debug("The keyboard would not open: " + ex.Message);
                return;
            }

            // The keyboard's own Enter or Escape must not land on this panel as well.
            InputGuard.Swallow();

            if (typed == null) return;

            var clean = Clean(typed);

            if (clean.Length == 0)
            {
                Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                return;
            }

            if (Taken != null && Taken(_owned, clean))
            {
                Notify.Problem("that's on your other car.");
                Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                return;
            }

            _text = clean;

            try { Function.Call(Hash.SET_VEHICLE_NUMBER_PLATE_TEXT, _car.Handle, clean); }
            catch { /* the record still changes, and the next spawn puts it on */ }

            _owned.Plate = clean;
            _dirty = true;

            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        /// <summary>Capitals, digits and spaces, eight at most, which is all a plate can carry.</summary>
        private static string Clean(string typed)
        {
            var sb = new StringBuilder();

            foreach (var ch in (typed ?? "").ToUpperInvariant())
            {
                if ((ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == ' ') sb.Append(ch);
                if (sb.Length >= Longest) break;
            }

            return sb.ToString().Trim();
        }

        // ---- input ----------------------------------------------------------------

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

        // ---- drawing --------------------------------------------------------------

        public void Draw()
        {
            if (!IsOpen) return;

            var width = Hud.ToX(PanelWidthH);
            var left = 0.5f - width * 0.5f;
            var pad = Hud.ToX(PadH);
            var height = 0.170f + 3 * RowHeight;
            var top = 0.5f - height * 0.5f + _curtain.Lift;

            Theme.Panel(left, top, width, height);

            var x = left + pad;
            var right = left + width - pad;
            var y = top + 0.020f;

            Hud.Text("PLATES", x, y - 0.004f, 0.74f, Palette.Text, Hud.FontCursive, centre: false);
            Hud.TextRight(_name.ToUpperInvariant(), right, y + 0.010f, 0.34f, Palette.TextDim);

            y += 0.052f;

            Hud.Text("PUT YOUR NAME ON IT", x, y, 0.26f, Palette.TextDim, Hud.FontLabel, centre: false);
            Hud.TextRight("8 LETTERS OR NUMBERS", right, y, 0.24f, Palette.TextDim);

            y += 0.026f;
            Theme.Rule(x, y, right - x);
            y += 0.010f;

            var grown = Theme.Grown(_pickedAt);
            var barWide = (right - x) + pad * 0.7f;

            for (var i = 0; i < 3; i++)
            {
                var here = i == (int)_row;
                var lit = Theme.Lit(i, (int)_row, _lastRow, grown);

                Theme.Plate(x - pad * 0.35f, y - 0.004f, barWide, RowHeight, lit);
                Theme.Sheen(x - pad * 0.35f, y - 0.004f, barWide, RowHeight, lit);

                var ink = Theme.Ink(here ? Palette.Text : Palette.TextDim, lit);

                string label, value;

                switch ((Row)i)
                {
                    case Row.Number:
                        label = "NUMBER";
                        value = _text.Length == 0 ? "--" : _text;
                        break;
                    case Row.Style:
                        label = "STYLE";
                        value = (here ? "<  " : "") + StyleName(_style).ToUpperInvariant() + (here ? "  >" : "");
                        break;
                    default:
                        label = "DONE";
                        value = "";
                        break;
                }

                Hud.Text(label, x, y + 0.008f, 0.32f, ink, Hud.FontBody, centre: false);
                if (value.Length > 0) Hud.TextRight(value, right, y + 0.009f, 0.30f, ink, Hud.FontBody);

                y += RowHeight;
            }

            Hud.Text(Hud.OnPad
                         ? "D-PAD  MOVE      LEFT/RIGHT  STYLE      A  TYPE IT / DONE      B  KEEP IT"
                         : "UP/DOWN  MOVE      LEFT/RIGHT  STYLE      ENTER  TYPE IT / DONE      BACKSPACE  KEEP IT",
                     x, top + height - 0.030f, 0.24f, Palette.TextDim, Hud.FontLabel, centre: false);
        }
    }
}
