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
            // The button that got you out of here does not also swing at somebody.
            if (IsOpen) Core.InputGuard.Swallow();
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

        // ---- the panel's proportions -------------------------------------------

        /// <summary>Everything above the first row: mark, line, heading, columns, bars.</summary>
        private const float HeadHeight = 0.166f;

        /// <summary>And the rule and the key line under the last one.</summary>
        private const float FootHeight = 0.040f;

        private const float Rule = 0.0016f;
        private const float Tick = 0.024f;

        private static readonly Color Hairline = Color.FromArgb(44, 200, 205, 200);

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

        /// <summary>The travelling highlight on the selected row.</summary>
        private const int SweepMs = 2400;

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
            var top = 0.5f - height * 0.5f;

            Hud.RectFrom(left, top, panelWidth, height, Color.FromArgb(238, 12, 13, 15));
            Hud.RectFrom(left, top, panelWidth, 0.0028f, Palette.Accent);

            var colWidth = (panelWidth - pad * 2f - columnGap) * 0.5f;
            var leftCol = left + pad;
            var rightCol = leftCol + colWidth + columnGap;
            var middle = left + panelWidth * 0.5f;
            var lineWidth = colWidth * 2f + columnGap;

            // The letterhead, the same one every other screen in the mod carries, and one line
            // under it saying what you are looking at.
            Hud.BrandCentre(middle, top + 0.024f, 0.022f, Palette.Alpha(Palette.TextDim, 180));

            Hud.Text(Blurb, middle, top + 0.052f, 0.29f,
                     Palette.Alpha(Palette.TextDim, 170), Hud.FontChaletLondon);

            Hud.RectFrom(leftCol, top + 0.078f, lineWidth, 0.0012f,
                         Color.FromArgb(46, 255, 255, 255));

            var y = top + 0.088f;

            Hud.Text("STASH HOUSE", leftCol, y, 0.30f, Palette.Text, Hud.FontLabel, centre: false);

            Hud.TextRight(_rows.Count + (_rows.Count == 1 ? " LINE" : " LINES"),
                          leftCol + lineWidth, y, 0.24f, Palette.TextDim, Hud.FontLabel);

            y += 0.028f;

            // Both sides, each with its own picture, its own numbers and its own bar.
            Column(leftCol, colWidth, y, "ON YOU", "people.png", _pockets, ref _fillYou);
            Column(rightCol, colWidth, y, "AT HOME", "stash.png", _house, ref _fillHome);

            y = top + HeadHeight - 0.008f;

            Hud.RectFrom(leftCol, y, lineWidth, 0.0012f, Hairline);

            y = top + HeadHeight;

            if (_rows.Count == 0)
            {
                Hud.Text("Nothing on you and nothing at home.", leftCol, y + 0.006f, 0.28f,
                         Palette.TextDim, Hud.FontBody, centre: false);
            }

            for (var i = 0; i < _rows.Count; i++)
            {
                Line(_rows[i], i, leftCol, rightCol, colWidth, columnGap, pad, y);
                y += RowHeight;
            }

            var footY = top + height - FootHeight + 0.008f;

            Hud.RectFrom(leftCol, footY, lineWidth, 0.0010f, Hairline);

            const string hint =
                "UP / DOWN  PICK     LEFT  TAKE OUT     RIGHT  PUT AWAY     SPRINT  ALL     BACKSPACE  DONE";

            Hud.Text(hint, leftCol, footY + 0.008f, 0.24f, Palette.TextDim,
                     Hud.FontLabel, centre: false);

            // Last, so nothing paints over it.
            Hud.Frame(left, top, panelWidth, height,
                      Color.FromArgb(64, 205, 212, 205), Palette.Accent, Rule, Tick);
        }

        /// <summary>
        /// One product line: art, name, how cut it is, and the two numbers.
        /// </summary>
        private void Line(StashRow row, int i, float leftCol, float rightCol,
                          float colWidth, float columnGap, float pad, float y)
        {
            var picked = i == _selected;

            if (picked)
            {
                var wash = leftCol - pad * 0.35f;
                var wide = colWidth * 2f + columnGap + pad * 0.7f;

                Hud.RectFrom(wash, y - 0.004f, wide, RowHeight, Color.FromArgb(45, 255, 255, 255));

                // A rail down the near edge, and a highlight travelling along the row.
                //
                // The wash alone was the whole cursor, and a still wash on a still list is easy
                // to lose track of when every line says roughly the same thing. The sweep is
                // the one moving object on the panel and it is always the line you are on.
                Hud.RectFrom(wash, y - 0.004f, 0.0022f, RowHeight, Palette.Accent);

                var t = (Game.GameTime % SweepMs) / (float)SweepMs;
                var band = wide * 0.16f;
                var at = wash - band + (wide + band) * t;

                var clippedLeft = Math.Max(wash, at);
                var clippedRight = Math.Min(wash + wide, at + band);

                if (clippedRight > clippedLeft)
                {
                    Hud.RectFrom(clippedLeft, y - 0.004f, clippedRight - clippedLeft, RowHeight,
                                 Color.FromArgb(16, 255, 255, 255));
                }
            }

            var label = picked ? "> " + row.Label : "  " + row.Label;
            var tint = picked ? Palette.Text : Palette.TextDim;

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

            Hud.TextRight(Amount(row.Drug, row.OnYou), leftCol + colWidth, y, 0.30f,
                          Side(row.OnYou, picked, flashing && !_movedHome, Palette.Text),
                          Hud.FontBody);

            Hud.TextRight(Amount(row.Drug, row.AtHome), rightCol + colWidth, y, 0.30f,
                          Side(row.AtHome, picked, flashing && _movedHome, Palette.Standing),
                          Hud.FontBody);
        }

        /// <summary>What one side's number is coloured, including for the moment it changed.</summary>
        private static Color Side(float held, bool picked, bool flashing, Color own)
        {
            if (held <= 0.005f) return Palette.TextDisabled;
            if (flashing) return Palette.Cash;

            return picked ? own : Palette.TextDim;
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
                         Palette.Alpha(Palette.Accent, 210)))
            {
                tx = x + Hud.ToX(HeadIcon) + 0.006f;
            }

            Hud.Text(title, tx, y, 0.28f, Palette.Accent, Hud.FontLabel, centre: false);

            var full = stash == null || stash.Capacity <= 0.01f
                ? 0f
                : stash.Total / stash.Capacity;

            if (full < 0f) full = 0f;
            if (full > 1f) full = 1f;

            var tint = full > 0.9f ? Palette.Danger : full > 0.7f ? Palette.Warn : Palette.Standing;

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

        private static string Amount(DrugDef drug, float quantity)
        {
            if (quantity <= 0.005f) return "-";
            return drug == null ? quantity.ToString("0.#") + "g" : drug.Amount(quantity);
        }
    }
}
