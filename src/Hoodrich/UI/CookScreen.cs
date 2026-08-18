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
    /// <summary>
    /// Working the product, at the kitchen counter.
    ///
    /// This used to be two levels of wheel: pick a drug, pick a purity. Both are decisions you
    /// make standing over a table with the stuff in front of you, not decisions you flick
    /// through while walking down a street -- so it happens at the counter and nowhere else,
    /// and the whole thing is one screen with two axes.
    ///
    /// Up and down is what you are working. Left and right is how far you stretch it, which is
    /// the only real choice here: more units that are worth less each and get handed back more
    /// often, or fewer that nobody argues with.
    /// </summary>
    internal sealed class CookScreen
    {
        private const float PanelWidthH = 0.58f;
        private const float RowHeight = 0.028f;
        private const float PadH = 0.024f;

        private const int OpenGraceMs = 220;

        /// <summary>Most you will work in one batch.</summary>
        private const float MaxBatch = 50f;

        /// <summary>How far it can be stretched, cleanest first.</summary>
        private static readonly float[] Purities = { 1.0f, 0.75f, 0.5f, 0.33f };

        private readonly List<DrugDef> _rows = new List<DrugDef>();

        private Stash _stash;
        private Drugs _catalogue;
        private Pricing _pricing;
        private Func<DrugDef, float, float, string> _start;

        private int _selected;
        private int _purity;
        private int _openedAt;

        public bool IsOpen { get; private set; }

        public void Open(Stash stash, Drugs catalogue, Pricing pricing,
                         Func<DrugDef, float, float, string> start)
        {
            if (stash == null || catalogue == null || pricing == null) return;

            _stash = stash;
            _catalogue = catalogue;
            _pricing = pricing;
            _start = start;

            _selected = 0;
            _purity = 0;
            _openedAt = Game.GameTime;
            IsOpen = true;

            Rebuild();

            if (_rows.Count == 0)
            {
                Notify.Problem("no weight on you to work.");
                IsOpen = false;
                return;
            }

            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        public void Close()
        {
            IsOpen = false;
            _stash = null;
            _catalogue = null;
            _pricing = null;
            _start = null;
            _rows.Clear();
        }

        private void Rebuild()
        {
            _rows.Clear();

            foreach (var drug in _catalogue.All)
            {
                if (_stash.BulkOf(drug.Id) > 0.005f) _rows.Add(drug);
            }

            if (_selected >= _rows.Count) _selected = Math.Max(0, _rows.Count - 1);
        }

        // ---- input -------------------------------------------------------------

        public void Update()
        {
            if (!IsOpen) return;

            LockControls();

            if (Game.GameTime - _openedAt < OpenGraceMs) return;

            if (Pressed(Control.PhoneCancel))
            {
                Hud.PlaySound("BACK", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                Close();
                return;
            }

            if (_rows.Count == 0)
            {
                Close();
                return;
            }

            if (Pressed(Control.PhoneUp)) Move(-1);
            else if (Pressed(Control.PhoneDown)) Move(1);
            else if (Pressed(Control.PhoneLeft)) Step(-1);
            else if (Pressed(Control.PhoneRight)) Step(1);
            else if (Pressed(Control.PhoneSelect) || Pressed(Control.Context)) Begin();
        }

        private static bool Pressed(Control control)
        {
            return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)control);
        }

        private void Move(int step)
        {
            _selected += step;
            if (_selected < 0) _selected = _rows.Count - 1;
            if (_selected >= _rows.Count) _selected = 0;

            Hud.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        private void Step(int step)
        {
            _purity = Math.Max(0, Math.Min(Purities.Length - 1, _purity + step));
            Hud.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        private void Begin()
        {
            var drug = _rows[_selected];
            var batch = Math.Min(MaxBatch, _stash.BulkOf(drug.Id));

            var failure = _start?.Invoke(drug, batch, Purities[_purity]);
            if (failure != null)
            {
                Notify.Problem(failure);
                Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                return;
            }

            // The batch runs in the world, not on a menu, so the screen gets out of the way.
            Close();
        }

        private static void LockControls()
        {
            Game.DisableControlThisFrame(Control.Attack);
            Game.DisableControlThisFrame(Control.Attack2);
            Game.DisableControlThisFrame(Control.Aim);
            Game.DisableControlThisFrame(Control.Jump);
            Game.DisableControlThisFrame(Control.Sprint);
            Game.DisableControlThisFrame(Control.Context);
            Game.DisableControlThisFrame(Control.Phone);
            Game.DisableControlThisFrame(Control.SelectWeapon);
            Game.DisableControlThisFrame(Control.MoveLeftRight);
            Game.DisableControlThisFrame(Control.MoveUpDown);

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
            if (!IsOpen || _rows.Count == 0) return;

            var height = 0.186f + _rows.Count * RowHeight;

            var panelWidth = Hud.ToX(PanelWidthH);
            var pad = Hud.ToX(PadH);

            var left = 0.5f - panelWidth * 0.5f;
            var top = 0.5f - height * 0.5f;

            Hud.RectFrom(left, top, panelWidth, height, Color.FromArgb(238, 12, 13, 15));
            Hud.RectFrom(left, top, panelWidth, 0.0028f, Palette.Accent);

            var x = left + pad;
            var right = left + panelWidth - pad;
            var y = top + 0.013f;

            Hud.Text("THE KITCHEN", x, y, 0.34f, Palette.Text, Hud.FontLabel, centre: false);
            y += 0.030f;

            Hud.Text("Pick what you're working, then how far you stretch it.",
                     x, y, 0.26f, Palette.TextDim, Hud.FontBody, centre: false);
            y += 0.030f;

            foreach (var drug in _rows)
            {
                var picked = _rows[_selected] == drug;
                var have = _stash.BulkOf(drug.Id);

                if (picked)
                {
                    Hud.RectFrom(x - pad * 0.35f, y - 0.004f,
                                 panelWidth - pad * 1.3f, RowHeight,
                                 Color.FromArgb(45, 255, 255, 255));
                }

                Hud.Text((picked ? "> " : "  ") + drug.Name, x, y, 0.30f,
                         picked ? Palette.Text : Palette.TextDim, Hud.FontBody, centre: false);

                Hud.TextRight(have.ToString("0.#") + "g", right, y, 0.30f,
                              picked ? Palette.Warn : Palette.TextDim, Hud.FontBody);

                y += RowHeight;
            }

            y += 0.012f;

            // The batch, spelled out: what goes in, what comes out, what it is worth.
            var product = _rows[_selected];
            var purity = Purities[_purity];
            var batch = Math.Min(MaxBatch, _stash.BulkOf(product.Id));
            var yield = Cutting.Yield(batch, purity);
            var worth = _pricing.SaleValue(product, yield, purity);
            var risk = Pricing.BadCutChance(purity);
            var fits = _stash.FreeSpace >= yield - batch - 0.001f;

            Hud.RectFrom(x, y - 0.006f, panelWidth - pad * 2f, 0.0015f,
                         Color.FromArgb(90, 255, 255, 255));

            Hud.Text(PurityWord(purity), x, y + 0.004f, 0.32f, Palette.Accent,
                     Hud.FontLabel, centre: false);

            Hud.TextRight(batch.ToString("0") + "g  ->  " + yield.ToString("0") + "g   $" +
                          worth.ToString("N0"),
                          right, y + 0.004f, 0.30f,
                          fits ? Palette.Cash : Palette.Danger, Hud.FontBody);

            y += 0.030f;

            var note = !fits ? "No room for " + yield.ToString("0") + "g -- leave some at home first"
                     : risk < 0.01f ? "Nobody is going to complain about this"
                     : risk < 0.2f ? "The odd buyer might notice"
                     : "Expect people to hand it back";

            Hud.Text(note, x, y, 0.26f, fits ? Palette.TextDim : Palette.Danger,
                     Hud.FontBody, centre: false);
            y += 0.026f;

            Hud.Text("UP / DOWN  PRODUCT     LEFT / RIGHT  HOW FAR     ENTER  START     BACKSPACE  LEAVE",
                     x, top + height - 0.020f, 0.24f, Palette.TextDim, Hud.FontLabel, centre: false);
        }

        private static string PurityWord(float purity)
        {
            if (purity >= 0.95f) return "Untouched";
            if (purity >= 0.75f) return "Barely stepped on";
            if (purity >= 0.50f) return "Cut half and half";
            return "Stepped on hard";
        }
    }
}
