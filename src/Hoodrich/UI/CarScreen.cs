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
        private float _slide;

        public CarScreen(Hao hao)
        {
            _hao = hao;
        }

        public bool IsOpen { get; private set; }

        /// <summary>Set by Main: what he says when money changes hands.</summary>
        public Action<CarLot> OnBought;

        public void Open()
        {
            IsOpen = true;
            _openedAt = Game.GameTime;
            _row = 0;
            _slide = 0f;

            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        public void Close()
        {
            if (!IsOpen) return;

            // The button that got you out of here does not also swing at somebody.
            Core.InputGuard.Swallow();

            IsOpen = false;
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
                         Control.PhoneSelect, Control.PhoneCancel, Control.Context,
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

            _row = ((_row + step) % count + count) % count;
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

            // The list just got shorter under the cursor.
            if (_row >= Stock.Count) _row = Math.Max(0, Stock.Count - 1);

            if (Stock.Count == 0) Close();
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
            var top = 0.5f - height * 0.5f;

            Hud.RectFrom(left, top, width, height, Palette.Hub);
            Corners(left, top, width, height);

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
            Hud.RectFrom(x, y, right - x, 0.0016f, Palette.Accent);
            y += 0.010f;

            if (stock.Count == 0)
            {
                Hud.Text("Lot's empty. Bring me something and I'll put it right.",
                         x, y + 0.014f, 0.30f, Palette.TextDim, Hud.FontBody, centre: false);
                Keys(x, right, top + height - 0.030f);
                return;
            }

            // ---- the eased selection bar ----
            var want = _row * RowHeight;
            _slide += (want - _slide) * 0.34f;
            if (Math.Abs(want - _slide) < 0.0004f) _slide = want;

            var barY = y - 0.004f + _slide;

            Hud.RectFrom(x - pad * 0.35f, barY, (right - x) + pad * 0.7f, RowHeight,
                         Color.FromArgb(46, 255, 255, 255));
            Hud.RectFrom(x - pad * 0.35f, barY, 0.0022f, RowHeight, Palette.Accent);

            // A travelling sheen, inside the bar and nowhere else.
            var t = (Game.GameTime % 1500) / 1500f;
            var sheenW = (right - x) * 0.18f;
            var at = x - pad * 0.35f - sheenW + ((right - x) + pad * 0.7f + sheenW * 2f) * t;
            var a = Math.Max(x - pad * 0.35f, at);
            var b = Math.Min(right + pad * 0.35f, at + sheenW);
            if (b > a) Hud.RectFrom(a, barY, b - a, RowHeight, Color.FromArgb(30, 255, 255, 255));

            // ---- the stock ----
            foreach (var car in stock)
            {
                var here = car == Chosen;
                var afford = Game.Player.Money >= car.Price;

                var ink = here ? Palette.Text : Palette.TextDim;

                Hud.Text(car.Name, x, y, 0.32f, ink, Hud.FontBody, centre: false);

                Hud.Text(car.Class, x + Hud.ToX(0.175f), y + 0.003f, 0.24f,
                         Palette.TextDim, Hud.FontBody, centre: false);

                Hud.TextRight("$" + car.Price.ToString("N0"), right, y + 0.002f, 0.28f,
                              afford ? Palette.Cash : Palette.Danger);

                y += RowHeight;
            }

            y += 0.010f;
            Hud.RectFrom(x, y, right - x, 0.0016f, Color.FromArgb(46, 255, 255, 255));
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
        }

        private static void Keys(float x, float right, float y)
        {
            Hud.Text("UP/DOWN  LOOK      ENTER  BUY IT      BACKSPACE  WALK OFF",
                     x, y, 0.22f, Palette.TextDim, Hud.FontLabel, centre: false);
        }

        /// <summary>Corner ticks rather than a full frame, the way every panel here is edged.</summary>
        private static void Corners(float left, float top, float w, float h)
        {
            var c = Palette.Accent;
            var len = 0.022f;
            var lenX = Hud.ToX(len);
            var t = 0.0022f;
            var tX = Hud.ToX(t);

            Hud.RectFrom(left, top, lenX, t, c);
            Hud.RectFrom(left, top, tX, len, c);

            Hud.RectFrom(left + w - lenX, top, lenX, t, c);
            Hud.RectFrom(left + w - tX, top, tX, len, c);

            Hud.RectFrom(left, top + h - t, lenX, t, c);
            Hud.RectFrom(left, top + h - len, tX, len, c);

            Hud.RectFrom(left + w - lenX, top + h - t, lenX, t, c);
            Hud.RectFrom(left + w - tX, top + h - len, tX, len, c);
        }
    }
}
