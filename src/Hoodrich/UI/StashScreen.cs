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
    /// <summary>One movable line: a product, in one of its two forms.</summary>
    internal sealed class StashRow
    {
        public DrugDef Drug;

        /// <summary>True for street-ready units, false for uncut weight.</summary>
        public bool Bagged;

        public float OnYou;
        public float AtHome;

        public string Label => Drug.Name + (Bagged ? "" : "  (weight)");
    }

    /// <summary>
    /// Moving product between your pockets and the house.
    ///
    /// Two columns, because that is the whole idea: what is on you on the left, what is at home
    /// on the right, and the gap between them is the thing you are operating. Up and down picks
    /// a line; left and right push it the way you are looking at. No cursor, no dragging --
    /// every input is a direction on a stick, so it plays the same on a pad as on a keyboard.
    /// </summary>
    internal sealed class StashScreen
    {
        /// <summary>
        /// Sized in HEIGHT fractions and converted, so the card keeps its shape.
        ///
        /// Width in screen fractions makes the panel as wide as the monitor is: on an ultrawide
        /// the first version came out as a letterbox strip with two columns a metre apart. A
        /// card should be a card at any aspect ratio.
        /// </summary>
        private const float PanelWidthH = 0.62f;

        private const float ColumnGapH = 0.03f;
        private const float RowHeight = 0.028f;
        private const float PadH = 0.024f;

        /// <summary>Moved per press. Holding the run button moves the lot instead.</summary>
        private const float StepGrams = 10f;

        /// <summary>Ignore input briefly, or the button that opened this acts on it.</summary>
        private const int OpenGraceMs = 220;

        /// <summary>Held direction repeats at this rate, so you can pour a stack across.</summary>
        private const int RepeatMs = 110;

        private Stash _pockets;
        private Stash _house;
        private Drugs _catalogue;
        private Action _onChange;

        private readonly List<StashRow> _rows = new List<StashRow>();
        private int _selected;
        private int _openedAt;
        private int _nextRepeat;

        public bool IsOpen { get; private set; }

        public void Open(Stash pockets, Stash house, Drugs catalogue, Action onChange)
        {
            if (pockets == null || house == null || catalogue == null) return;

            _pockets = pockets;
            _house = house;
            _catalogue = catalogue;
            _onChange = onChange;

            _selected = 0;
            _openedAt = Game.GameTime;
            IsOpen = true;

            Rebuild();
            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        public void Close()
        {
            IsOpen = false;
            _pockets = null;
            _house = null;
            _catalogue = null;
            _onChange = null;
            _rows.Clear();
        }

        /// <summary>
        /// Rebuilds the lines from both containers.
        ///
        /// A product appears if it exists on either side, so something you have just put away
        /// does not vanish off the screen the moment you move the last of it.
        /// </summary>
        private void Rebuild()
        {
            var keepId = _selected >= 0 && _selected < _rows.Count ? _rows[_selected].Drug.Id : null;
            var keepBagged = _selected >= 0 && _selected < _rows.Count && _rows[_selected].Bagged;

            _rows.Clear();

            foreach (var drug in _catalogue.All)
            {
                AddRow(drug, true);
                AddRow(drug, false);
            }

            if (keepId == null) return;

            for (var i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Drug.Id != keepId || _rows[i].Bagged != keepBagged) continue;
                _selected = i;
                return;
            }

            _selected = Math.Min(_selected, Math.Max(0, _rows.Count - 1));
        }

        private void AddRow(DrugDef drug, bool bagged)
        {
            var mine = bagged ? _pockets.PackagedOf(drug.Id) : _pockets.BulkOf(drug.Id);
            var home = bagged ? _house.PackagedOf(drug.Id) : _house.BulkOf(drug.Id);

            if (mine <= 0.005f && home <= 0.005f) return;

            _rows.Add(new StashRow { Drug = drug, Bagged = bagged, OnYou = mine, AtHome = home });
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

            if (_rows.Count == 0) return;

            if (Pressed(Control.PhoneUp)) Move(-1);
            else if (Pressed(Control.PhoneDown)) Move(1);

            // Left and right are the transfer, in the direction the columns are laid out.
            if (Game.GameTime < _nextRepeat) return;

            // Read through the DISABLED path. Both of these are turned off by LockControls a few
            // lines up, and IsControlPressed reports false for a disabled control -- so "hold
            // sprint to move the lot" has never once worked.
            var all = Held(Control.Sprint) || Held(Control.Jump);

            if (Held(Control.PhoneRight)) Transfer(toHouse: true, everything: all);
            else if (Held(Control.PhoneLeft)) Transfer(toHouse: false, everything: all);
        }

        private static bool Pressed(Control control)
        {
            return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)control);
        }

        private static bool Held(Control control)
        {
            return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)control);
        }

        private void Move(int step)
        {
            _selected += step;
            if (_selected < 0) _selected = _rows.Count - 1;
            if (_selected >= _rows.Count) _selected = 0;

            Hud.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        private void Transfer(bool toHouse, bool everything)
        {
            if (_selected < 0 || _selected >= _rows.Count) return;

            var row = _rows[_selected];
            var from = toHouse ? _pockets : _house;
            var to = toHouse ? _house : _pockets;

            var available = row.Bagged ? from.PackagedOf(row.Drug.Id) : from.BulkOf(row.Drug.Id);
            if (available <= 0.005f)
            {
                Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                _nextRepeat = Game.GameTime + RepeatMs * 3;
                return;
            }

            var want = everything ? available : Math.Min(StepGrams, available);
            var moved = MoveSome(from, to, row.Drug.Id, row.Bagged, want);

            _nextRepeat = Game.GameTime + RepeatMs;

            if (moved <= 0.005f)
            {
                // The far side is full.
                Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                _nextRepeat = Game.GameTime + RepeatMs * 3;
                return;
            }

            Hud.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");

            _onChange?.Invoke();
            Rebuild();
        }

        /// <summary>
        /// Moves an amount between two containers without ever losing any.
        ///
        /// The destination is asked FIRST how much it will take, and only that much is removed
        /// from the source -- the other order silently destroys product whenever the far side
        /// is nearly full.
        /// </summary>
        private static float MoveSome(Stash from, Stash to, string drugId, bool bagged, float grams)
        {
            if (bagged)
            {
                var purity = from.PurityOf(drugId);
                var accepted = to.AddPackaged(drugId, grams, purity);
                if (accepted <= 0.005f) return 0f;

                var taken = from.RemovePackaged(drugId, accepted);

                // Whatever the source could not actually supply goes back.
                if (taken < accepted - 0.005f) to.RemovePackaged(drugId, accepted - taken);
                return taken;
            }

            var acceptedBulk = to.AddBulk(drugId, grams);
            if (acceptedBulk <= 0.005f) return 0f;

            var takenBulk = from.RemoveBulk(drugId, acceptedBulk);
            if (takenBulk < acceptedBulk - 0.005f) to.RemoveBulk(drugId, acceptedBulk - takenBulk);
            return takenBulk;
        }

        private static void LockControls()
        {
            Game.DisableControlThisFrame(Control.Attack);
            Game.DisableControlThisFrame(Control.Attack2);
            Game.DisableControlThisFrame(Control.Aim);
            Game.DisableControlThisFrame(Control.MeleeAttack1);
            Game.DisableControlThisFrame(Control.Jump);
            Game.DisableControlThisFrame(Control.Sprint);
            Game.DisableControlThisFrame(Control.Enter);
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
            if (!IsOpen) return;

            var bodyRows = Math.Max(_rows.Count, 1);
            var height = 0.092f + bodyRows * RowHeight + 0.036f;

            // Everything converted from height fractions, so the proportions hold on any screen.
            var panelWidth = Hud.ToX(PanelWidthH);
            var pad = Hud.ToX(PadH);
            var columnGap = Hud.ToX(ColumnGapH);

            var left = 0.5f - panelWidth * 0.5f;
            var top = 0.5f - height * 0.5f;

            Hud.RectFrom(left, top, panelWidth, height, Color.FromArgb(238, 12, 13, 15));
            Hud.RectFrom(left, top, panelWidth, 0.0028f, Palette.Accent);

            var colWidth = (panelWidth - pad * 2f - columnGap) * 0.5f;
            var leftCol = left + pad;
            var rightCol = leftCol + colWidth + columnGap;

            var y = top + 0.013f;

            Hud.Text("STASH HOUSE", leftCol, y, 0.34f, Palette.Text, Hud.FontLabel, centre: false);
            y += 0.032f;

            // Column headers, each with what that side is holding out of what it can.
            DrawColumnHead(leftCol, colWidth, y, "ON YOU", _pockets);
            DrawColumnHead(rightCol, colWidth, y, "AT HOME", _house);
            y += 0.030f;

            Hud.RectFrom(leftCol, y - 0.006f, colWidth * 2f + columnGap, 0.0015f,
                         Color.FromArgb(90, 255, 255, 255));

            if (_rows.Count == 0)
            {
                Hud.Text("Nothing on you and nothing at home.", leftCol, y + 0.008f, 0.28f,
                         Palette.TextDim, Hud.FontBody, centre: false);
            }

            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                var picked = i == _selected;

                if (picked)
                {
                    Hud.RectFrom(leftCol - pad * 0.35f, y - 0.004f,
                                 colWidth * 2f + columnGap + pad * 0.7f, RowHeight,
                                 Color.FromArgb(45, 255, 255, 255));
                }

                var label = picked ? "> " + row.Label : "  " + row.Label;
                var tint = picked ? Palette.Text : Palette.TextDim;

                Hud.Text(label, leftCol, y, 0.30f, tint, Hud.FontBody, centre: false);

                Hud.TextRight(Amount(row.OnYou), leftCol + colWidth, y, 0.30f,
                              row.OnYou > 0.005f ? (picked ? Palette.Text : Palette.TextDim)
                                                 : Palette.TextDisabled,
                              Hud.FontBody);

                Hud.TextRight(Amount(row.AtHome), rightCol + colWidth, y, 0.30f,
                              row.AtHome > 0.005f ? (picked ? Palette.Cash : Palette.TextDim)
                                                  : Palette.TextDisabled,
                              Hud.FontBody);

                y += RowHeight;
            }

            var hint = "UP / DOWN  PICK     LEFT  TAKE OUT     RIGHT  PUT AWAY     SPRINT  ALL     BACKSPACE  DONE";

            Hud.Text(hint, leftCol, top + height - 0.022f, 0.24f, Palette.TextDim,
                     Hud.FontLabel, centre: false);
        }

        private static void DrawColumnHead(float x, float width, float y, string title, Stash stash)
        {
            Hud.Text(title, x, y, 0.28f, Palette.Accent, Hud.FontLabel, centre: false);

            var full = stash.Capacity <= 0.01f ? 0f : stash.Total / stash.Capacity;
            var tint = full > 0.9f ? Palette.Danger : full > 0.7f ? Palette.Warn : Palette.TextDim;

            Hud.TextRight(stash.Total.ToString("0") + " / " + stash.Capacity.ToString("0") + "g",
                          x + width, y, 0.28f, tint, Hud.FontBody);
        }

        private static string Amount(float grams)
        {
            return grams <= 0.005f ? "-" : grams.ToString("0.#") + "g";
        }
    }
}
