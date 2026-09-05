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

        /// <summary>
        /// How tall a row's art is, as a fraction of screen height.
        ///
        /// Matched to the body text beside it rather than to the row box. These PNGs are
        /// authored square, so the height is the whole size.
        /// </summary>
        private const float ArtSize = 0.019f;
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

        /// <summary>How this panel arrives and how it leaves. See UI.Curtain.</summary>
        private readonly Curtain _curtain = new Curtain();

        public bool IsOpen => _curtain.Showing;

        public void Open(Stash pockets, Stash house, Drugs catalogue, Action onChange)
        {
            if (pockets == null || house == null || catalogue == null) return;

            _pockets = pockets;
            _house = house;
            _catalogue = catalogue;
            _onChange = onChange;

            _selected = 0;
            _lastSelected = -1;
            _pickedAt = Game.GameTime;
            _glide.Reset();
            _openedAt = Game.GameTime;
            _curtain.Open();

            Rebuild();
            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        public void Close()
        {
            // The button that got you out of here does not also swing at somebody.
            if (IsOpen) Core.InputGuard.Swallow();
            _curtain.Close();
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

            // On its way out it still draws and still holds the controls, but it has stopped
            // listening -- otherwise the panel you just closed spends its last tenth of a
            // second acting on whatever you press next.
            if (!_curtain.Taking) return;

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
            var before = _selected;

            _selected += step;
            if (_selected < 0) _selected = _rows.Count - 1;
            if (_selected >= _rows.Count) _selected = 0;

            if (_selected != before)
            {
                _lastSelected = before;
                _pickedAt = Game.GameTime;
            }

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

            _movedAt = Game.GameTime;
            _movedRow = _selected;
            _movedHome = toHouse;

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

            // With its purity, which it used to lose crossing the kitchen.
            //
            // The bagged branch above has always carried it. This one did not, so fifty per
            // cent weight walked from your pockets into the house and came out the other side
            // pure -- the most profitable thing in the mod was picking a bag up and putting it
            // down again.
            var acceptedBulk = to.AddBulk(drugId, grams, from.BulkPurityOf(drugId));
            if (acceptedBulk <= 0.005f) return 0f;

            var takenBulk = from.RemoveBulk(drugId, acceptedBulk);
            if (takenBulk < acceptedBulk - 0.005f) to.RemoveBulk(drugId, acceptedBulk - takenBulk);
            return takenBulk;
        }

        private static void LockControls()
        {
            Core.Fists.Off();
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

        // ---- the panel's proportions -------------------------------------------

        /// <summary>Everything above the first row: mark, line, heading, columns, bars.</summary>
        private const float HeadHeight = 0.166f;

        /// <summary>And the rule and the key line under the last one.</summary>
        private const float FootHeight = 0.040f;

        /// <summary>
        /// What this screen is, in one line, under the mark.
        ///
        /// Every other panel in the mod is a thing you DO -- a menu of jobs, a list of guns, a
        /// feed. This one is a place you put things, and a person opening it for the first time
        /// is looking at two columns of numbers with no idea which side is which until they
        /// press something and watch it move. One sentence removes that entirely.
        /// </summary>
        private const string Blurb = "what's on you, and what's at the house";

        /// <summary>How tall the capacity bars are, and the purity mark beside a name.</summary>
        private const float BarHeight = 0.0075f;
        private const float MarkSize = 0.0125f;

        /// <summary>Eased fills, so a transfer slides the bar instead of teleporting it.</summary>
        private float _fillYou;
        private float _fillHome;

        /// <summary>The row the cursor was on before this one, and when it moved. See Theme.Lit.</summary>
        private int _lastSelected = -1;
        private int _pickedAt;

        /// <summary>The cursor frame that glides between rows. See UI.Glide.</summary>
        private readonly Glide _glide = new Glide();

        /// <summary>What just moved, which way, and when -- for the flash on the numbers.</summary>
        private int _movedAt;
        private int _movedRow = -1;
        private bool _movedHome;

        private const int MovedFlashMs = 420;

        public void Draw()
        {
            if (!IsOpen) return;

            var bodyRows = Math.Max(_rows.Count, 1);
            var height = HeadHeight + bodyRows * RowHeight + FootHeight;

            // Everything converted from height fractions, so the proportions hold on any screen.
            var panelWidth = Hud.ToX(PanelWidthH);
            var pad = Hud.ToX(PadH);
            var columnGap = Hud.ToX(ColumnGapH);

            var left = 0.5f - panelWidth * 0.5f;
            var top = 0.5f - height * 0.5f + _curtain.Lift;

            Theme.Panel(left, top, panelWidth, height);

            var colWidth = (panelWidth - pad * 2f - columnGap) * 0.5f;
            var leftCol = left + pad;
            var rightCol = leftCol + colWidth + columnGap;
            var middle = left + panelWidth * 0.5f;
            var lineWidth = colWidth * 2f + columnGap;

            // The letterhead, the same one every other screen in the mod carries, and one line
            // under it saying what you are looking at.
            Hud.BrandCentre(middle, top + 0.024f, 0.022f, Palette.Alpha(Palette.Text, 230));

            Hud.Text(Blurb, middle, top + 0.052f, 0.29f,
                     Palette.Alpha(Palette.TextDim, 170), Hud.FontChaletLondon);

            Theme.Rule(leftCol, top + 0.078f, lineWidth);

            var y = top + 0.088f;

            Hud.Text("STASH HOUSE", leftCol, y, 0.30f, Palette.Text, Hud.FontLabel, centre: false);

            Hud.TextRight(_rows.Count + (_rows.Count == 1 ? " LINE" : " LINES"),
                          leftCol + lineWidth, y, 0.24f, Palette.TextDim, Hud.FontLabel);

            y += 0.028f;

            // Both sides, each with its own picture, its own numbers and its own bar.
            Column(leftCol, colWidth, y, "ON YOU", "people.png", _pockets, ref _fillYou);
            Column(rightCol, colWidth, y, "AT HOME", "stash.png", _house, ref _fillHome);

            y = top + HeadHeight - 0.008f;

            Theme.Rule(leftCol, y, lineWidth);

            y = top + HeadHeight;

            if (_rows.Count == 0)
            {
                Hud.Text("Nothing on you and nothing at home.", leftCol, y + 0.006f, 0.28f,
                         Palette.TextDim, Hud.FontBody, centre: false);
            }

            var grown = Theme.Grown(_pickedAt);

            _glide.Begin();

            for (var i = 0; i < _rows.Count; i++)
            {
                Line(_rows[i], i, grown, leftCol, rightCol, colWidth, columnGap, pad, y);
                y += RowHeight;
            }

            var footY = top + height - FootHeight + 0.008f;

            Theme.Rule(leftCol, footY, lineWidth);

            // THE ARROWS SAY THE DIRECTION AND THE WORDS SAY THE ERRAND, which is the
            // rearrangement that makes this line readable at a glance. It used to name the KEY
            // and then the errand -- "LEFT  TAKE OUT" -- so the direction was a word you read
            // and then had to map onto a direction. An arrow pointing left IS left, on a
            // keyboard and on a d-pad, and it costs no reading at all.
            var hy = footY + 0.008f;
            var hx = leftCol;

            var pad2 = Hud.OnPad;

            hx = Hud.Hint("arrow_updown.png", "PICK", hx, hy, 0.24f, Palette.TextDim);
            hx = Hud.Hint("arrow_left.png", "TAKE OUT", hx, hy, 0.24f, Palette.TextDim);
            hx = Hud.Hint("arrow_right.png", "PUT AWAY", hx, hy, 0.24f, Palette.TextDim);

            Hud.Hint(null, (pad2 ? "HOLD A" : "SPRINT") + "  ALL", hx, hy, 0.24f,
                     Palette.TextDim);

            Hud.TextRight(pad2 ? "B  DONE" : "BACKSPACE  DONE", leftCol + lineWidth, hy, 0.24f,
                          Palette.TextDim, Hud.FontLabel);

            // Last, so it rides over the rows it is pointing at.
            _glide.Draw();
        }

        /// <summary>
        /// One product line: art, name, how cut it is, and the two numbers.
        /// </summary>
        private void Line(StashRow row, int i, float grown, float leftCol, float rightCol,
                          float colWidth, float columnGap, float pad, float y)
        {
            var picked = i == _selected;

            // The plate comes up under the row the cursor lands on and goes down under the one
            // it left, and the frame travels between them. Same as every other screen.
            var lit = Theme.Lit(i, _selected, _lastSelected, grown);

            var wash = leftCol - pad * 0.35f;
            var wide = colWidth * 2f + columnGap + pad * 0.7f;

            Theme.Plate(wash, y - 0.004f, wide, RowHeight, lit);
            Theme.Sheen(wash, y - 0.004f, wide, RowHeight, lit);

            if (picked) _glide.Target(wash, y - 0.004f, wide, RowHeight);

            var label = row.Label;
            var tint = Theme.Ink(picked ? Palette.Text : Palette.TextDim, lit);

            // The product's own art, the way the kitchen and the wheel show it. Hud.File places
            // by its CENTRE and Hud.Text by its TOP edge, so the art drops half a row to sit
            // level with the words.
            var art = Icons.ForDrug(row.Drug.Id);
            var tx = leftCol;

            if (art.HasFile &&
                Hud.File(art.File, leftCol + Hud.ToX(ArtSize) * 0.5f, y + RowHeight * 0.34f,
                         ArtSize, 0f, tint))
            {
                tx = leftCol + Hud.ToX(ArtSize) + 0.007f;
            }

            // Fitted to the space it actually has rather than trusted to be short enough.
            //
            // The names on this screen are the longest in the game and the figure beside them
            // is right-aligned to a fixed edge, so on a wide monitor -- where the panel is a
            // card rather than a strip and the columns are narrower in screen terms -- the
            // widest of them ran into its own number. Measured against the gap that is left
            // once the art has taken its share.
            Hud.Text(Hud.Fit(label, leftCol + colWidth - tx - 0.008f, 0.30f, Hud.FontBody),
                     tx, y, 0.30f, tint, Hud.FontBody, centre: false);

            // How cut it is, the same mark the cook screen and the buy menus use, drawn in
            // the gap between the two columns.
            //
            // Not after the name, which is where it belongs everywhere else in the mod and is
            // the one place it cannot go here. The names on this screen are the longest in the
            // game -- "Alprazolam  (weight)" -- and the number beside them is right-aligned to
            // a fixed edge, so a mark after the words collides with the figure on the widest
            // lines and on nothing else, which is the worst kind of bug: correct in testing.
            //
            // The gap is empty by construction and the same width on every row, so the marks
            // stack into a column of their own down the middle of the panel. Whichever side
            // actually holds any is the side that answers -- a purity read off an empty pocket
            // is a hundred per cent of nothing.
            var strength = Strength(row);

            if (strength > 0f)
            {
                Hud.File(Stash.Mark(strength),
                         leftCol + colWidth + columnGap * 0.5f, y + RowHeight * 0.34f,
                         MarkSize, 0f, tint);
            }

            var flashing = _movedRow == i && Game.GameTime - _movedAt < MovedFlashMs;

            Hud.TextRight(Amount(row.Drug, row.OnYou, row.Bagged), leftCol + colWidth, y, 0.30f,
                          Theme.Ink(Side(row.OnYou, picked, flashing && !_movedHome), lit),
                          Hud.FontBody);

            Hud.TextRight(Amount(row.Drug, row.AtHome, row.Bagged), rightCol + colWidth, y, 0.30f,
                          Theme.Ink(Side(row.AtHome, picked, flashing && _movedHome), lit),
                          Hud.FontBody);
        }

        /// <summary>What one side's number is coloured, including for the moment it changed.</summary>
        private static Color Side(float held, bool picked, bool flashing)
        {
            if (held <= 0.005f) return Palette.TextDisabled;
            if (flashing) return Palette.Cash;

            return picked ? Palette.Text : Palette.TextDim;
        }

        /// <summary>
        /// How strong this line is, asked of whichever side is holding any of it.
        /// </summary>
        private float Strength(StashRow row)
        {
            var where = row.OnYou > 0.005f ? _pockets : row.AtHome > 0.005f ? _house : null;
            if (where == null) return 0f;

            return row.Bagged ? where.PurityOf(row.Drug.Id) : where.BulkPurityOf(row.Drug.Id);
        }

        /// <summary>
        /// One side's heading: its picture, its name, what it is holding, and a bar of it.
        ///
        /// The bar eases toward the true figure rather than being set to it. Two numbers
        /// swapping places is arithmetic; a bar sliding one way while the other slides back is
        /// the thing you actually did, and it is the only way this screen can show a transfer
        /// as a movement rather than as a redraw.
        /// </summary>
        private static void Column(float x, float width, float y, string title, string icon,
                                   Stash stash, ref float eased)
        {
            var tx = x;

            if (Hud.File(icon, x + Hud.ToX(HeadIcon) * 0.5f, y + 0.008f, HeadIcon, 0f,
                         Palette.Alpha(Palette.Text, 225)))
            {
                tx = x + Hud.ToX(HeadIcon) + 0.006f;
            }

            Hud.Text(title, tx, y, 0.28f, Palette.Text, Hud.FontLabel, centre: false);

            var full = stash == null || stash.Capacity <= 0.01f
                ? 0f
                : stash.Total / stash.Capacity;

            if (full < 0f) full = 0f;
            if (full > 1f) full = 1f;

            // Gold while there is room, ember once it is getting full, red when it is. Yellow
            // into orange into red is the one order of those three that reads as filling up.
            var tint = full > 0.9f ? Palette.Danger : full > 0.7f ? Palette.BrandDeep : Palette.Brand;

            Hud.TextRight((stash == null ? 0f : stash.Total).ToString("0") + " / " +
                          (stash == null ? 0f : stash.Capacity).ToString("0") + "g",
                          x + width, y, 0.28f,
                          full > 0.7f ? tint : Palette.TextDim, Hud.FontBody);

            // Toward the figure rather than at it, and snapped once it is close enough that
            // another frame of easing would be a frame of nothing.
            eased += (full - eased) * 0.18f;
            if (Math.Abs(full - eased) < 0.002f) eased = full;

            var barY = y + 0.024f;

            Hud.RectFrom(x, barY, width, BarHeight, Color.FromArgb(150, 26, 28, 30));

            if (eased > 0f)
            {
                Hud.RectFrom(x, barY, width * eased, BarHeight, tint);
            }

            Hud.RectFrom(x, barY + BarHeight, width, 0.0010f, Palette.Alpha(tint, 90));
        }

        /// <summary>The picture beside a column's name.</summary>
        private const float HeadIcon = 0.016f;

        private static string Amount(DrugDef drug, float quantity, bool bagged)
        {
            if (quantity <= 0.005f) return "-";
            if (drug == null) return quantity.ToString("0.#") + "g";

            // The row already knows which of the two it is -- it puts "(weight)" after the name
            // for one of them -- so it may as well say so in the number too.
            return bagged ? drug.Amount(quantity) : drug.Bulk(quantity);
        }
    }
}
