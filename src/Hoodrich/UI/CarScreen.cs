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

        /// <summary>The entrance: up and in over a sixth of a second, like every other panel.</summary>
        private const int EnterMs = 170;
        private const float EnterRise = 0.014f;

        /// <summary>What he says about the one you are on, and the line of facts under it.</summary>
        private const float NoteH = 0.050f;

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

        /// <summary>The lot's own sign, and the shape of the file. See tools/make_haos.py.</summary>
        private const float HaosAspect = 6.6930f;

        public void Draw()
        {
            if (!IsOpen) return;

            var stock = Stock;

            var width = Hud.ToX(PanelWidthH);
            var left = 0.5f - width * 0.5f;
            var pad = Hud.ToX(PadH);

            var rows = Math.Max(1, stock.Count);
            var height = UiKit.HeadH + 0.004f + rows * RowHeight + 0.010f + 0.012f + NoteH + UiKit.FootH;
            var top = 0.5f - height * 0.5f + _curtain.Lift;

            var age = Game.GameTime - _openedAt;
            var arrive = age >= EnterMs ? 1f : age / (float)EnterMs;
            arrive = 1f - (1f - arrive) * (1f - arrive);
            top += EnterRise * (1f - arrive);

            Theme.Panel(left, top, width, height, arrive);

            var x = left + pad;
            var right = left + width - pad;
            var wide = right - x;
            var caps = Palette.Alpha(Palette.TextDim, (int)(190f * arrive));

            // ---- his name over the door, and what is in your pocket ----
            var y = UiKit.Head(left, top, width, pad, "car.png", "HAO'S AUTOS",
                             "the row you just walked past",
                             "$" + Game.Player.Money.ToString("N0") + " ON YOU", arrive,
                             "haos.png", HaosAspect);
            y += 0.004f;

            if (stock.Count == 0)
            {
                Hud.Text("Lot's empty. Bring me something and I'll put it right.",
                         x, y + 0.014f, 0.30f, Palette.Alpha(Palette.TextDim, (int)(255f * arrive)),
                         Hud.FontBody, centre: false);
                Keys(x, right, top + height - UiKit.FootH + 0.006f, arrive);
                return;
            }

            // ---- the stock ----
            //
            // THE PLATE COMES UP UNDER THE ROW rather than sliding to it: the one under the
            // new row rises over a sixth of a second while the one under the old row sinks,
            // and the frame -- see Glide -- travels between them. Same as every other screen.
            var grown = Theme.Grown(_pickedAt);
            var barWide = wide + pad * 0.7f;

            _glide.Begin();

            for (var i = 0; i < stock.Count; i++)
            {
                var car = stock[i];

                var here = i == _row;
                var afford = Game.Player.Money >= car.Price;

                var lit = Theme.Lit(i, _row, _lastRow, grown);

                Theme.Plate(x - pad * 0.35f, y - 0.004f, barWide, RowHeight, lit * arrive);
                Theme.Sheen(x - pad * 0.35f, y - 0.004f, barWide, RowHeight, lit * arrive);

                if (here) _glide.Target(x - pad * 0.35f, y - 0.004f, barWide, RowHeight);

                var ink = Theme.Ink(Palette.Alpha(here ? Palette.Text : Palette.TextDim, (int)(255f * arrive)), lit);

                // The name, and what kind of car it is on a chip after it -- a tag is a
                // different kind of thing from a name and now looks like one.
                Hud.Text(car.Name, x, y + 0.006f, 0.30f, ink, Hud.FontBody, centre: false);

                if (!string.IsNullOrEmpty(car.Class))
                {
                    var after = x + Hud.MeasureText(car.Name, 0.30f, Hud.FontBody) + 0.008f;
                    UiKit.Tag(after, y + 0.0085f, car.Class.ToUpperInvariant(), Palette.TextDim,
                              arrive * (0.75f + 0.25f * lit));
                }

                Hud.TextRight("$" + car.Price.ToString("N0"), right, y + 0.007f, 0.28f,
                              Theme.Ink(Palette.Alpha(afford ? Palette.Cash : Palette.Danger, (int)(255f * arrive)), lit),
                              Hud.FontLabel);

                y += RowHeight;
            }

            y += 0.010f;
            Theme.Rule(x, y, wide, arrive);
            y += 0.012f;

            // ---- what he says about the one you are on ----
            var pick = Chosen;
            if (pick != null)
            {
                Hud.Text(Hud.Fit(pick.Note, wide, 0.28f, Hud.FontBody), x, y, 0.28f,
                         Palette.Alpha(Palette.Text, (int)(255f * arrive)), Hud.FontBody, centre: false);

                y += 0.024f;

                Hud.Text("COMPETITION SUSPENSION  \u00b7  NO PAPERWORK  \u00b7  PARKED OUTSIDE",
                         x, y, 0.22f, caps, Hud.FontLabel, centre: false);

                if (Game.Player.Money < pick.Price)
                {
                    Hud.TextRight("SHORT BY $" + (pick.Price - Game.Player.Money).ToString("N0"),
                                  right, y, 0.22f, Palette.Alpha(Palette.Danger, (int)(255f * arrive)), Hud.FontLabel);
                }
            }

            Keys(x, right, top + height - UiKit.FootH + 0.006f, arrive);

            // Last, so it rides over the rows it is pointing at.
            _glide.Draw(arrive);
        }

        /// <summary>The keys, drawn as keys, with the way out in the corner it is on every other screen.</summary>
        private static void Keys(float x, float right, float footY, float arrive)
        {
            Theme.Rule(x, footY, right - x, arrive);

            var ky = footY + 0.011f;

            UiKit.KeyRight(right, ky, UiKit.Back, "WALK OFF", arrive);

            var kx = UiKit.Key(x, ky, null, "arrow_updown.png", "LOOK", arrive);
            UiKit.Key(kx, ky, UiKit.Confirm, null, "BUY IT", arrive);
        }
    }
}
