using System;
using System.Collections.Generic;
using System.Drawing;
using Control = GTA.Control;
using GTA;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Locations;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.UI
{
    /// <summary>
    /// Hao's lot, as a list you can read.
    ///
    /// The cars are already standing outside, which is the point of them -- so this is not a
    /// catalogue of things that will appear when bought. It is the paperwork for the row of
    /// metal you have just walked past, and buying a line here unlocks THAT car, where it is
    /// parked, with the keys in it.
    ///
    /// Priced off a man in a yard rather than off a forecourt, and the screen says so: there
    /// is no finance, no colour picker and no warranty, because none of those are things Hao
    /// is offering.
    /// </summary>
    internal sealed class CarScreen
    {
        private const float PanelWidthH = 0.60f;
        private const float RowHeight = 0.034f;
        private const float PadH = 0.024f;

        /// <summary>Long enough that the press which opened it cannot also buy something.</summary>
        private const int OpenGraceMs = 220;

        private readonly Hao _hao;

        private int _row;
        private int _openedAt;
        /// <summary>The row the cursor was on before this one, and when it moved. See Theme.Lit.</summary>
        private int _lastRow = -1;
        private int _pickedAt;

        /// <summary>The cursor frame that glides between rows. See UI.Glide.</summary>
        private readonly Glide _glide = new Glide();

        public CarScreen(Hao hao)
        {
            _hao = hao;
        }

        /// <summary>How this panel arrives and how it leaves. See UI.Curtain.</summary>
        private readonly Curtain _curtain = new Curtain();

        public bool IsOpen => _curtain.Showing;

        /// <summary>Set by Main: what he says when money changes hands.</summary>
        public Action<CarLot> OnBought;

        public void Open()
        {
            _curtain.Open();
            _openedAt = Game.GameTime;
            _row = 0;
            _lastRow = -1;
            _pickedAt = Game.GameTime;
            _glide.Reset();

            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        public void Close()
        {
            if (!IsOpen) return;

            // The button that got you out of here does not also swing at somebody.
            Core.InputGuard.Swallow();

            _curtain.Close();
            Hud.PlaySound("BACK", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        private IReadOnlyList<CarLot> Stock => _hao == null ? new List<CarLot>() : _hao.Stock;

        private CarLot Chosen
        {
            get
            {
                var stock = Stock;
                if (stock.Count == 0) return null;
                return stock[Math.Max(0, Math.Min(_row, stock.Count - 1))];
            }
        }

        // ---- input -------------------------------------------------------------

        public void Update()
        {
            if (!IsOpen) return;

            LockControls();

            // On its way out it still draws and still holds the controls, but it has stopped
            // listening -- otherwise the panel you just closed spends its last tenth of a
            // second acting on whatever you press next.
            if (!_curtain.Taking) return;

            if (Game.GameTime - _openedAt < OpenGraceMs) return;

            if (Pressed(Control.PhoneCancel)) { Close(); return; }

            if (Pressed(Control.PhoneUp)) Move(-1);
            else if (Pressed(Control.PhoneDown)) Move(1);
            else if (Pressed(Control.PhoneSelect) || Pressed(Control.Context)) Buy();
        }

        private static bool Pressed(Control control)
        {
            return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)control);
        }

        /// <summary>The same lock every other full screen in this mod uses.</summary>
        private static void LockControls()
        {
            Function.Call(Hash.DISABLE_ALL_CONTROL_ACTIONS, 0);

            foreach (var control in new[]
                     {
                         Control.PhoneUp, Control.PhoneDown, Control.PhoneLeft, Control.PhoneRight,
                         // Context is not here for the same reason it is not in GunScreen:
                         // the screen reads the disabled variant, so enabling it only let
                         // pressing E to buy a car also fire the world's context action.
                         Control.PhoneSelect, Control.PhoneCancel,
                         Control.LookLeftRight, Control.LookUpDown
                     })
            {
                Function.Call(Hash.ENABLE_CONTROL_ACTION, 0, (int)control, true);
            }
        }

        private void Move(int step)
        {
            var count = Stock.Count;
            if (count == 0) return;

            var before = _row;

            _row = ((_row + step) % count + count) % count;

            if (_row != before)
            {
                _lastRow = before;
                _pickedAt = Game.GameTime;
            }

            Hud.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        private void Buy()
        {
            var car = Chosen;
            if (car == null) return;

            if (Game.Player.Money < car.Price)
            {
                Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                Notify.Problem("that's more than you've got.");
                return;
            }

            var no = _hao.Buy(car);

            if (no != null)
            {
                Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                Notify.Problem(no);
                return;
            }

            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
            Notify.Important("~y~-$" + car.Price.ToString("N0") + "~s~  " + car.Name +
                             ".  ~g~It's outside. It's yours.~s~");

            OnBought?.Invoke(car);

            // ONE AT A TIME. The plate panel comes up over the car the moment it is yours
            // (see Main), so the showroom gets out of its way; it is a walk back in for the
            // next one, which is how buying a car goes.
            if (_row >= Stock.Count) _row = Math.Max(0, Stock.Count - 1);
            Close();
        }

        // ---- drawing -----------------------------------------------------------

        public void Draw()
        {
            if (!IsOpen) return;

            var stock = Stock;

            var width = Hud.ToX(PanelWidthH);
            var left = 0.5f - width * 0.5f;
            var pad = Hud.ToX(PadH);

            var rows = Math.Max(1, stock.Count);
            var height = 0.250f + rows * RowHeight;
            var top = 0.5f - height * 0.5f + _curtain.Lift;

            Theme.Panel(left, top, width, height);

            var x = left + pad;
            var right = left + width - pad;

            var y = top + 0.020f;

            // ---- his name over the door ----
            Hud.Text("HAO'S", x, y - 0.004f, 0.74f, Palette.Text, Hud.FontCursive, centre: false);
            Hud.TextRight("$" + Game.Player.Money.ToString("N0"), right, y + 0.010f, 0.34f,
                          Palette.Cash);

            y += 0.052f;

            Hud.Text("EVERYTHING OUT FRONT", x, y, 0.26f, Palette.TextDim,
                     Hud.FontLabel, centre: false);
            Hud.TextRight(stock.Count + (stock.Count == 1 ? " motor" : " motors"),
                          right, y, 0.24f, Palette.TextDim);

            y += 0.026f;
            Theme.Rule(x, y, right - x);
            y += 0.010f;

            if (stock.Count == 0)
            {
                Hud.Text("Lot's empty. Bring me something and I'll put it right.",
                         x, y + 0.014f, 0.30f, Palette.TextDim, Hud.FontBody, centre: false);
                Keys(x, right, top + height - 0.030f);
                return;
            }

            // ---- the stock ----
            //
            // THE PLATE COMES UP UNDER THE ROW rather than sliding to it: the one under the
            // new row rises over a sixth of a second while the one under the old row sinks,
            // and the frame -- see Glide -- travels between them. Same as every other screen.
            var grown = Theme.Grown(_pickedAt);
            var barWide = (right - x) + pad * 0.7f;

            _glide.Begin();

            for (var i = 0; i < stock.Count; i++)
            {
                var car = stock[i];

                var here = i == _row;
                var afford = Game.Player.Money >= car.Price;

                var lit = Theme.Lit(i, _row, _lastRow, grown);

                Theme.Plate(x - pad * 0.35f, y - 0.004f, barWide, RowHeight, lit);
                Theme.Sheen(x - pad * 0.35f, y - 0.004f, barWide, RowHeight, lit);

                if (here) _glide.Target(x - pad * 0.35f, y - 0.004f, barWide, RowHeight);

                var ink = Theme.Ink(here ? Palette.Text : Palette.TextDim, lit);

                Hud.Text(car.Name, x, y, 0.32f, ink, Hud.FontBody, centre: false);

                Hud.Text(car.Class, x + Hud.ToX(0.175f), y + 0.003f, 0.24f,
                         Theme.Ink(Palette.TextDim, lit), Hud.FontBody, centre: false);

                Hud.TextRight("$" + car.Price.ToString("N0"), right, y + 0.002f, 0.28f,
                              Theme.Ink(afford ? Palette.Cash : Palette.Danger, lit));

                y += RowHeight;
            }

            y += 0.010f;
            Theme.Rule(x, y, right - x);
            y += 0.012f;

            // ---- what he says about the one you are on ----
            var pick = Chosen;
            if (pick != null)
            {
                Hud.Text(pick.Note, x, y, 0.28f, Palette.Text, Hud.FontBody, centre: false);

                y += 0.024f;

                Hud.Text("COMPETITION SUSPENSION  ·  NO PAPERWORK  ·  PARKED OUTSIDE",
                         x, y, 0.22f, Palette.TextDim, Hud.FontLabel, centre: false);

                if (Game.Player.Money < pick.Price)
                {
                    Hud.TextRight("SHORT BY $" + (pick.Price - Game.Player.Money).ToString("N0"),
                                  right, y, 0.22f, Palette.Danger, Hud.FontLabel);
                }
            }

            Keys(x, right, top + height - 0.030f);

            // Last, so it rides over the rows it is pointing at.
            _glide.Draw();
        }

        /// <summary>
        /// What the buttons do, with a picture on each. Same treatment as the rest of the
        /// panels -- and it finally uses the right edge it was already being handed, so the
        /// way out is where it is on every other screen rather than at the end of a sentence.
        /// </summary>
        private static void Keys(float x, float right, float y)
        {
            var pad = Hud.OnPad;
            var ink = Palette.TextDim;

            var hx = Hud.Hint("arrow_updown.png", "LOOK", x, y, 0.22f, ink);

            Hud.Hint("cash.png", (pad ? "A" : "ENTER") + "  BUY IT", hx, y, 0.22f, ink);

            Hud.TextRight(pad ? "B  WALK OFF" : "BACKSPACE  WALK OFF", right, y, 0.22f, ink,
                          Hud.FontLabel);
        }
    }
}
