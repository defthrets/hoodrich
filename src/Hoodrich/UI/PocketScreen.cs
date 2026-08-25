using System;
using System.Collections.Generic;
using System.Drawing;
using Control = GTA.Control;
using GTA;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Economy;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.UI
{
    /// <summary>One line of what you are carrying.</summary>
    internal sealed class PocketRow
    {
        public DrugDef Drug;

        /// <summary>True for street-ready units, false for uncut weight.</summary>
        public bool Bagged;

        public float Held;
        public float Purity = 1f;

        public string Label => Drug.Name + (Bagged ? "" : "  (weight)");
    }

    /// <summary>
    /// What is on you, and the one thing you can do about it away from the house.
    ///
    /// The stash house screen is two containers and a gap you push things across. This is the
    /// same list with nowhere to push it: on a street corner there is no shelf to put anything
    /// on, so the only transfer here is DOWN -- onto the pavement, where it stays until you
    /// come back for it or somebody else finds it.
    ///
    /// That is the whole feature, and it exists because of the search. A man who can see a
    /// patrol coming currently has exactly one option, which is to run. Putting the bag in a
    /// hedge and walking back afterwards is the other one, and it is the one anybody would
    /// actually try.
    /// </summary>
    internal sealed class PocketScreen
    {
        private const float PanelWidthH = 0.50f;
        private const float RowHeight = 0.028f;
        private const float ArtSize = 0.019f;
        private const float PadH = 0.024f;

        /// <summary>Dropped per press. Holding the run button puts the lot down.</summary>
        private const float StepGrams = 10f;

        private const int OpenGraceMs = 220;
        private const int RepeatMs = 140;

        /// <summary>Everything above the first row, and the rule and keys below the last.</summary>
        private const float HeadHeight = 0.160f;
        private const float FootHeight = 0.040f;

        private const float Rule = 0.0016f;
        private const float Tick = 0.024f;
        private const float BarHeight = 0.0075f;
        private const float MarkSize = 0.0125f;
        private const float HeadIcon = 0.016f;

        private static readonly Color Hairline = Color.FromArgb(44, 200, 205, 200);

        /// <summary>
        /// What this screen is, in one line, under the mark.
        ///
        /// The same job the kitchen's and the stash house's lines do. This one has to work
        /// harder than either, because the thing it explains -- that the only way out of your
        /// own pockets here is the floor -- is not a thing anybody would guess.
        /// </summary>
        private const string Blurb = "what's on you, and the only way to put it down";

        private Stash _pockets;
        private Drugs _catalogue;
        private DroppedBags _bags;

        private readonly List<PocketRow> _rows = new List<PocketRow>();

        private int _selected;
        private int _openedAt;
        private int _nextRepeat;

        /// <summary>Eased fill, so putting something down slides the bar rather than jumping it.</summary>
        private float _fill;

        /// <summary>The travelling highlight, and the flash on a row that just changed.</summary>
        private const int SweepMs = 2400;
        private const int EnterMs = 170;
        private const float EnterRise = 0.014f;
        private const float SlideRate = 0.30f;

        private float _slide;
        private int _shownAt;

        private int _droppedAt;
        private int _droppedRow = -1;

        private const int DropFlashMs = 420;

        /// <summary>How this panel arrives and how it leaves. See UI.Curtain.</summary>
        private readonly Curtain _curtain = new Curtain();

        public bool IsOpen => _curtain.Showing;

        public void Open(Stash pockets, Drugs catalogue, DroppedBags bags)
        {
            if (pockets == null || catalogue == null) return;

            _pockets = pockets;
            _catalogue = catalogue;
            _bags = bags;

            _selected = 0;
            _openedAt = Game.GameTime;
            _shownAt = Game.GameTime;
            _nextRepeat = 0;
            _droppedRow = -1;

            Rebuild();

            _slide = 0f;
            _fill = Full();

            _curtain.Open();

            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        public void Close()
        {
            // The button that got you out of here does not also swing at somebody.
            if (IsOpen) Core.InputGuard.Swallow();
            if (!IsOpen) return;

            _curtain.Close();
            Hud.PlaySound("BACK", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        private float Full()
        {
            if (_pockets == null || _pockets.Capacity <= 0.01f) return 0f;

            var f = _pockets.Total / _pockets.Capacity;
            return f < 0f ? 0f : f > 1f ? 1f : f;
        }

        /// <summary>
        /// Every drug you are holding, in both forms.
        ///
        /// Rebuilt rather than patched after every drop, because a row that empties has to
        /// disappear and the cursor has to end up somewhere sensible when it does.
        /// </summary>
        private void Rebuild()
        {
            _rows.Clear();

            if (_pockets == null || _catalogue == null) return;

            foreach (var drug in _catalogue.All)
            {
                if (drug == null) continue;

                var bagged = _pockets.PackagedOf(drug.Id);
                if (bagged > 0.005f)
                {
                    _rows.Add(new PocketRow
                    {
                        Drug = drug,
                        Bagged = true,
                        Held = bagged,
                        Purity = _pockets.PurityOf(drug.Id)
                    });
                }

                var weight = _pockets.BulkOf(drug.Id);
                if (weight > 0.005f)
                {
                    _rows.Add(new PocketRow
                    {
                        Drug = drug,
                        Bagged = false,
                        Held = weight,
                        Purity = _pockets.BulkPurityOf(drug.Id)
                    });
                }
            }

            if (_selected >= _rows.Count) _selected = Math.Max(0, _rows.Count - 1);
            if (_selected < 0) _selected = 0;
        }

        public void Update()
        {
            if (!IsOpen) return;

            LockControls();

            // On its way out it still draws and still holds the controls, but it has stopped
            // listening -- otherwise the panel you just closed spends its last tenth of a
            // second acting on whatever you press next.
            if (!_curtain.Taking) return;

            if (Game.GameTime - _openedAt < OpenGraceMs) return;

            if (Game.IsControlJustPressed(Control.PhoneCancel) ||
                Game.IsControlJustPressed(Control.FrontendPause))
            {
                Close();
                return;
            }

            if (_rows.Count == 0) return;

            if (Game.IsControlJustPressed(Control.PhoneUp)) Move(-1);
            else if (Game.IsControlJustPressed(Control.PhoneDown)) Move(1);

            // Left or right, because there is only one direction anything can go from here and
            // making you learn which of the two it is would be a puzzle rather than a control.
            var down = Game.IsControlPressed(Control.PhoneLeft) ||
                       Game.IsControlPressed(Control.PhoneRight);

            if (down && Game.GameTime >= _nextRepeat) Drop(Game.IsControlPressed(Control.Sprint));
        }

        private void Move(int step)
        {
            if (_rows.Count == 0) return;

            _selected = (_selected + step) % _rows.Count;
            if (_selected < 0) _selected += _rows.Count;

            Hud.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        /// <summary>
        /// Puts some of it on the floor.
        ///
        /// The bag does the removing, not this screen. It has to: the pocket is asked to hand
        /// the product over and the bag carries whatever it actually got, so a rounding error
        /// cannot leave a bag of nothing on the pavement or product in two places at once.
        /// </summary>
        private void Drop(bool everything)
        {
            if (_bags == null || _selected < 0 || _selected >= _rows.Count) return;

            var row = _rows[_selected];
            var want = everything ? row.Held : Math.Min(StepGrams, row.Held);

            if (want <= 0.005f) return;

            _bags.Drop(row.Drug.Id, want, row.Bagged);

            _nextRepeat = Game.GameTime + RepeatMs;
            _droppedAt = Game.GameTime;
            _droppedRow = _selected;

            Hud.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");

            Rebuild();
        }

        private static void LockControls()
        {
            Game.DisableControlThisFrame(Control.Attack);
            Game.DisableControlThisFrame(Control.Attack2);
            Game.DisableControlThisFrame(Control.Aim);
            Game.DisableControlThisFrame(Control.MeleeAttack1);
            Game.DisableControlThisFrame(Control.Jump);
            Game.DisableControlThisFrame(Control.Enter);
            Game.DisableControlThisFrame(Control.Phone);
            Game.DisableControlThisFrame(Control.SelectWeapon);

            Game.DisableControlThisFrame(Control.PhoneUp);
            Game.DisableControlThisFrame(Control.PhoneDown);
            Game.DisableControlThisFrame(Control.PhoneLeft);
            Game.DisableControlThisFrame(Control.PhoneRight);
            Game.DisableControlThisFrame(Control.PhoneSelect);
            Game.DisableControlThisFrame(Control.PhoneCancel);
        }

        // ---- drawing -----------------------------------------------------------

        public void Draw()
        {
            if (!IsOpen) return;

            var bodyRows = Math.Max(_rows.Count, 1);
            var height = HeadHeight + bodyRows * RowHeight + FootHeight;

            var panelWidth = Hud.ToX(PanelWidthH);
            var pad = Hud.ToX(PadH);

            var left = 0.5f - panelWidth * 0.5f;
            var top = 0.5f - height * 0.5f + _curtain.Lift;

            var age = Game.GameTime - _shownAt;
            var arrive = age >= EnterMs ? 1f : age / (float)EnterMs;
            arrive = 1f - (1f - arrive) * (1f - arrive);

            top += EnterRise * (1f - arrive);

            Hud.Panel(left, top, panelWidth, height,
                      Color.FromArgb((int)(238f * arrive), 12, 13, 15), Palette.Alpha(Palette.Accent, (int)(255f * arrive)));

            var barT = (Game.GameTime % SweepMs) / (float)SweepMs;
            var barW = panelWidth * 0.15f;
            var barAt = left - barW + (panelWidth + barW) * barT;

            var lit = Math.Max(left, barAt);
            var out2 = Math.Min(left + panelWidth, barAt + barW);

            if (out2 > lit)
            {
                Hud.RectFrom(lit, top, out2 - lit, 0.0028f,
                             Color.FromArgb((int)(85f * arrive), 255, 255, 255));
            }

            var x = left + pad;
            var right = left + panelWidth - pad;
            var wide = right - x;
            var middle = left + panelWidth * 0.5f;

            Hud.BrandCentre(middle, top + 0.024f, 0.022f, Palette.Alpha(Palette.TextDim, 180));

            Hud.Text(Blurb, middle, top + 0.052f, 0.29f,
                     Palette.Alpha(Palette.TextDim, 170), Hud.FontChaletLondon);

            Hud.RectFrom(x, top + 0.078f, wide, 0.0012f, Color.FromArgb(46, 255, 255, 255));

            var y = top + 0.088f;

            Hud.Text("INVENTORY", x, y, 0.30f, Palette.Text, Hud.FontLabel, centre: false);

            Hud.TextRight(_rows.Count + (_rows.Count == 1 ? " LINE" : " LINES"),
                          right, y, 0.24f, Palette.TextDim, Hud.FontLabel);

            y += 0.028f;

            Carrying(x, wide, y);

            y = top + HeadHeight - 0.008f;
            Hud.RectFrom(x, y, wide, 0.0012f, Hairline);

            y = top + HeadHeight;

            if (_rows.Count == 0)
            {
                Hud.Text("Nothing on you.", x, y + 0.006f, 0.28f,
                         Palette.TextDim, Hud.FontBody, centre: false);
            }
            else
            {
                _slide += (_selected - _slide) * SlideRate;
                if (Math.Abs(_selected - _slide) < 0.002f) _slide = _selected;

                var rowY = y - 0.004f + _slide * RowHeight;

                Hud.RectFrom(x - pad * 0.35f, rowY, wide + pad * 0.7f, RowHeight,
                             Color.FromArgb((int)(45f * arrive), 255, 255, 255));

                Hud.RectFrom(x - pad * 0.35f, rowY, 0.0022f, RowHeight, Palette.Accent);

                var sweepW = wide * 0.16f;
                var sweepAt = x - pad * 0.35f - sweepW + (wide + pad * 0.7f + sweepW) * barT;

                var lo = Math.Max(x - pad * 0.35f, sweepAt);
                var hi = Math.Min(x + wide + pad * 0.35f, sweepAt + sweepW);

                if (hi > lo)
                {
                    Hud.RectFrom(lo, rowY, hi - lo, RowHeight, Color.FromArgb(16, 255, 255, 255));
                }
            }

            for (var i = 0; i < _rows.Count; i++)
            {
                Line(_rows[i], i, x, right, y);
                y += RowHeight;
            }

            var footY = top + height - FootHeight + 0.008f;

            Hud.RectFrom(x, footY, wide, 0.0010f, Hairline);

            const string hint =
                "UP / DOWN  PICK     LEFT or RIGHT  PUT IT DOWN     SPRINT  ALL OF IT     BACKSPACE  DONE";

            Hud.Text(hint, x, footY + 0.008f, 0.24f, Palette.TextDim, Hud.FontLabel, centre: false);

            Hud.Frame(left, top, panelWidth, height,
                      Color.FromArgb(150, 90, 215, 235), Palette.Accent, Rule, Tick);
        }

        /// <summary>What you are holding out of what you can, with a bar of it.</summary>
        private void Carrying(float x, float width, float y)
        {
            var tx = x;

            if (Hud.File("people.png", x + Hud.ToX(HeadIcon) * 0.5f, y + 0.008f, HeadIcon, 0f,
                         Palette.Alpha(Palette.Accent, 210)))
            {
                tx = x + Hud.ToX(HeadIcon) + 0.006f;
            }

            Hud.Text("ON YOU", tx, y, 0.28f, Palette.Accent, Hud.FontLabel, centre: false);

            var full = Full();
            var tint = full > 0.9f ? Palette.Danger : full > 0.7f ? Palette.Warn : Palette.Standing;

            Hud.TextRight(_pockets.Total.ToString("0") + " / " + _pockets.Capacity.ToString("0") + "g",
                          x + width, y, 0.28f, full > 0.7f ? tint : Palette.TextDim, Hud.FontBody);

            _fill += (full - _fill) * 0.18f;
            if (Math.Abs(full - _fill) < 0.002f) _fill = full;

            var barY = y + 0.024f;

            Hud.RectFrom(x, barY, width, BarHeight, Color.FromArgb(150, 26, 28, 30));

            if (_fill > 0f) Hud.RectFrom(x, barY, width * _fill, BarHeight, tint);

            Hud.RectFrom(x, barY + BarHeight, width, 0.0010f, Palette.Alpha(tint, 90));
        }

        private void Line(PocketRow row, int i, float x, float right, float y)
        {
            var picked = i == _selected;
            var tint = picked ? Palette.Text : Palette.TextDim;

            var art = Icons.ForDrug(row.Drug.Id);
            var tx = x;

            if (art.HasFile &&
                Hud.File(art.File, x + Hud.ToX(ArtSize) * 0.5f, y + RowHeight * 0.34f,
                         ArtSize, 0f, tint))
            {
                tx = x + Hud.ToX(ArtSize) + 0.007f;
            }

            var label = picked ? "> " + row.Label : "  " + row.Label;

            Hud.Text(Hud.Fit(label, right - tx - 0.075f, 0.30f, Hud.FontBody),
                     tx, y, 0.30f, tint, Hud.FontBody, centre: false);

            // How cut it is, in its own column clear of both the name and the figure -- the
            // same reasoning as the stash house, where a mark after the words collides with a
            // right-aligned number on exactly the longest lines and nothing else.
            if (row.Purity > 0f)
            {
                Hud.File(Stash.Mark(row.Purity), right - 0.062f, y + RowHeight * 0.34f,
                         MarkSize, 0f, tint);
            }

            var flashing = _droppedRow == i && Game.GameTime - _droppedAt < DropFlashMs;

            Hud.TextRight(row.Drug.Amount(row.Held), right, y, 0.30f,
                          flashing ? Palette.Warn : picked ? Palette.Standing : Palette.TextDim,
                          Hud.FontBody);
        }
    }
}
