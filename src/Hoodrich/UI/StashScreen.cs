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

        /// <summary>Where the rows start: the letterhead, then the two meters under it.</summary>
        private const float ContentTop = UiKit.HeadH + 0.046f;

        /// <summary>A row, and the captions over the two amount columns.</summary>
        private const float RowH = 0.032f;
        private const float CapsH = 0.020f;

        private const float MarkSize = 0.0125f;

        /// <summary>
        /// What this screen is, in one line, beside the title.
        ///
        /// A person opening this for the first time is looking at two columns of numbers with
        /// no idea which side is which until they press something and watch it move. One
        /// sentence removes that entirely.
        /// </summary>
        private const string Blurb = "what's on you, and what's at the house";

        private const int EnterMs = 170;
        private const float EnterRise = 0.014f;

        /// <summary>The two needles, easing toward how full each side is. See UI.Eased.</summary>
        private readonly Eased _meterYou = new Eased();
        private readonly Eased _meterHome = new Eased();

        /// <summary>
        /// Every figure on the screen, easing toward the truth, one per product per form per
        /// side. Keyed rather than kept on the rows, because the rows are rebuilt after every
        /// press and a figure that started over each time would never be seen to move.
        /// </summary>
        private readonly Dictionary<string, Eased> _figures = new Dictionary<string, Eased>();
        private int _figuresFor;

        /// <summary>The row the cursor was on before this one, and when it moved. See Theme.Lit.</summary>
        private int _lastSelected = -1;
        private int _pickedAt;

        /// <summary>The cursor frame that glides between rows. See UI.Glide.</summary>
        private readonly Glide _glide = new Glide();

        /// <summary>What just moved, which way, and when -- for the chevrons and the flash.</summary>
        private int _movedAt;
        private int _movedRow = -1;
        private bool _movedHome;

        private const int MovedFlashMs = 520;

        public void Draw()
        {
            if (!IsOpen) return;

            // Figures from the last visit would roll into this one's. Start them over.
            if (_figuresFor != _openedAt)
            {
                _figuresFor = _openedAt;
                _figures.Clear();
            }

            var bodyRows = Math.Max(_rows.Count, 1);
            var height = ContentTop + CapsH + bodyRows * RowH + 0.008f + UiKit.FootH;

            // Everything converted from height fractions, so the proportions hold on any screen.
            var panelWidth = Hud.ToX(PanelWidthH);
            var pad = Hud.ToX(PadH);

            var left = 0.5f - panelWidth * 0.5f;
            var top = 0.5f - height * 0.5f + _curtain.Lift;

            // Up and in, eased out so it slows as it lands -- the same arrival every other
            // screen in the mod uses.
            var age = Game.GameTime - _openedAt;
            var arrive = age >= EnterMs ? 1f : age / (float)EnterMs;
            arrive = 1f - (1f - arrive) * (1f - arrive);

            top += EnterRise * (1f - arrive);

            Theme.Panel(left, top, panelWidth, height, arrive);

            var x = left + pad;
            var right = left + panelWidth - pad;
            var wide = right - x;

            // ---- the letterhead, and a meter for each side ----
            var y = UiKit.Head(left, top, panelWidth, pad, "stash.png", "STASH HOUSE", Blurb,
                             _rows.Count + (_rows.Count == 1 ? " LINE" : " LINES"), arrive);

            var gap = Hud.ToX(ColumnGapH);
            var half = (wide - gap) * 0.5f;

            var fullYou = UiKit.Full(_pockets);
            var fullHome = UiKit.Full(_house);

            UiKit.Meter(x, y + 0.004f, half, "people.png", "ON YOU", UiKit.Holding(_pockets),
                      _meterYou.To(fullYou), fullYou, arrive);

            UiKit.Meter(x + half + gap, y + 0.004f, half, "stash.png", "AT HOME", UiKit.Holding(_house),
                      _meterHome.To(fullHome), fullHome, arrive);

            y = top + ContentTop;

            // ---- the two places ----
            //
            // The name has the left half of the line. The right half is two columns with a
            // gap between them, and the gap is the thing you operate: what is on you sits on
            // the left of it, what is at home on the right, and pressing left or right pushes
            // the figure across. Each column has a faint ground of its own so the two read
            // as places rather than as numbers.
            var nameW = wide * 0.46f;
            var area = wide - nameW;
            var colW = area * 0.36f;

            var youX = x + nameW;
            var homeX = right - colW;
            var gapX = youX + colW;
            var gapW = homeX - gapX;

            var rowsH = bodyRows * RowH;

            Hud.RectFrom(youX, y, colW, CapsH + rowsH, Color.FromArgb((int)(12f * arrive), 255, 255, 255));
            Hud.RectFrom(homeX, y, colW, CapsH + rowsH, Color.FromArgb((int)(12f * arrive), 255, 255, 255));

            var caps = Palette.Alpha(Palette.TextDim, (int)(190f * arrive));

            Hud.Text("PRODUCT", x, y + 0.002f, 0.22f, caps, Hud.FontLabel, centre: false);
            Hud.TextRight("ON YOU", youX + colW - 0.004f, y + 0.002f, 0.22f, caps, Hud.FontLabel);
            Hud.TextRight("AT HOME", homeX + colW - 0.004f, y + 0.002f, 0.22f, caps, Hud.FontLabel);

            y += CapsH;

            if (_rows.Count == 0)
            {
                Hud.Text("Nothing on you and nothing at home.", x, y + 0.006f, 0.28f,
                         Palette.TextDim, Hud.FontBody, centre: false);
            }

            var grown = Theme.Grown(_pickedAt);

            _glide.Begin();

            for (var i = 0; i < _rows.Count; i++)
            {
                Line(_rows[i], i, grown, x, wide, pad, youX, homeX, colW, gapX, gapW, y, arrive);
                y += RowH;
            }

            // ---- the keys ----
            //
            // THE ARROWS SAY THE DIRECTION AND THE WORDS SAY THE ERRAND. An arrow pointing
            // left IS left, on a keyboard and on a d-pad, and it costs no reading at all.
            var footY = top + height - UiKit.FootH + 0.006f;

            Theme.Rule(x, footY, wide, arrive);

            var ky = footY + 0.011f;

            UiKit.KeyRight(right, ky, UiKit.Back, "DONE", arrive);

            var kx = UiKit.Key(x, ky, null, "arrow_updown.png", "PICK", arrive);
            kx = UiKit.Key(kx, ky, null, "arrow_left.png", "TAKE OUT", arrive);
            kx = UiKit.Key(kx, ky, null, "arrow_right.png", "PUT AWAY", arrive);
            UiKit.Key(kx, ky, UiKit.All, null, "ALL OF IT", arrive);

            // Last, so it rides over the rows it is pointing at.
            _glide.Draw(arrive);
        }

        /// <summary>
        /// One product line: art, name, a tag for raw weight, how cut it is, the two figures
        /// easing toward the truth, and the chevrons running across the gap when something
        /// just crossed it.
        /// </summary>
        private void Line(StashRow row, int i, float grown, float x, float wide, float pad,
                          float youX, float homeX, float colW, float gapX, float gapW, float y,
                          float arrive)
        {
            var picked = i == _selected;

            // The plate comes up under the row the cursor lands on and goes down under the one
            // it left, and the frame travels between them. Same as every other screen.
            var lit = Theme.Lit(i, _selected, _lastSelected, grown);

            var wash = x - pad * 0.35f;
            var wideRow = wide + pad * 0.7f;

            Theme.Plate(wash, y, wideRow, RowH, lit * arrive);
            Theme.Sheen(wash, y, wideRow, RowH, lit * arrive);

            if (picked) _glide.Target(wash, y, wideRow, RowH);

            var ink = Theme.Ink(Palette.Alpha(picked ? Palette.Text : Palette.TextDim, (int)(255f * arrive)), lit);

            var textY = y + 0.0055f;
            var midY = y + RowH * 0.5f;

            // ---- the art, then the name fitted to what is left ----
            var art = Icons.ForDrug(row.Drug.Id);
            var tx = x;

            if (art.HasFile &&
                Hud.File(art.File, x + Hud.ToX(ArtSize) * 0.5f, midY, ArtSize, 0f, ink))
            {
                tx = x + Hud.ToX(ArtSize) + 0.007f;
            }

            var markW = Hud.ToX(MarkSize);
            var tagRoom = row.Bagged ? 0f : 0.042f;

            var name = Hud.Fit(row.Drug.Name, youX - 0.010f - markW - 0.006f - tx - tagRoom, 0.30f,
                               Hud.FontBody);

            Hud.Text(name, tx, textY, 0.30f, ink, Hud.FontBody, centre: false);

            // Raw weight says so on a tag, rather than as a longer name.
            if (!row.Bagged)
            {
                var after = tx + Hud.MeasureText(name, 0.30f, Hud.FontBody) + 0.008f;

                UiKit.Tag(after, textY + 0.0025f, "WEIGHT", Palette.TextDim, arrive * (0.75f + 0.25f * lit));
            }

            // How cut it is, in a column of its own just before the figures. Whichever side
            // actually holds any is the side that answers -- a purity read off an empty
            // pocket is a hundred per cent of nothing.
            var strength = Strength(row);

            if (strength > 0f)
            {
                Hud.File(Stash.Mark(strength), youX - 0.006f - markW * 0.5f, midY, MarkSize, 0f, ink);
            }

            // ---- the two figures ----
            //
            // Each eases toward the truth, so ten grams pressed across is a number counting
            // down on one side and up on the other rather than two numbers swapping. The side
            // that just grew is lit green for half a second.
            var flashLeft = _movedRow == i ? UiKit.Flash(_movedAt, MovedFlashMs) : 0f;

            var youShown = Figure(row, false, row.OnYou);
            var homeShown = Figure(row, true, row.AtHome);

            Hud.TextRight(Amount(row.Drug, youShown, row.Bagged), youX + colW - 0.004f, textY, 0.30f,
                          Theme.Ink(Side(row.OnYou, picked, flashLeft > 0f && !_movedHome, arrive), lit),
                          Hud.FontBody);

            Hud.TextRight(Amount(row.Drug, homeShown, row.Bagged), homeX + colW - 0.004f, textY, 0.30f,
                          Theme.Ink(Side(row.AtHome, picked, flashLeft > 0f && _movedHome, arrive), lit),
                          Hud.FontBody);

            // ---- the gap ----
            //
            // Chevrons run the way the product just went; on the row under the cursor with
            // nothing moving they sit dim, both ways, saying that it can.
            if (flashLeft > 0f)
            {
                UiKit.Flow(gapX, textY, gapW, _movedHome ? 1 : -1, flashLeft, Palette.Cash);
            }
            else if (picked)
            {
                Hud.Text("<   >", gapX + gapW * 0.5f, textY, 0.28f,
                         Palette.Alpha(Palette.Brand, (int)(110f * grown * arrive)), Hud.FontBody,
                         centre: true);
            }
        }

        /// <summary>The figure shown for one side of one line, easing toward what it really is.</summary>
        private float Figure(StashRow row, bool home, float target)
        {
            var key = row.Drug.Id + (row.Bagged ? "|b" : "|w") + (home ? "|h" : "|y");

            Eased eased;

            if (!_figures.TryGetValue(key, out eased))
            {
                eased = new Eased();
                _figures[key] = eased;
            }

            return eased.To(target, 8f);
        }

        /// <summary>What one side's number is coloured, including for the moment it changed.</summary>
        private static Color Side(float held, bool picked, bool flashing, float arrive)
        {
            var c = held <= 0.005f ? Palette.TextDisabled
                  : flashing ? Palette.Cash
                  : picked ? Palette.Text : Palette.TextDim;

            return Palette.Alpha(c, (int)(c.A * arrive));
        }

        /// <summary>How strong this line is, asked of whichever side is holding any of it.</summary>
        private float Strength(StashRow row)
        {
            var where = row.OnYou > 0.005f ? _pockets : row.AtHome > 0.005f ? _house : null;
            if (where == null) return 0f;

            return row.Bagged ? where.PurityOf(row.Drug.Id) : where.BulkPurityOf(row.Drug.Id);
        }

        private static string Amount(DrugDef drug, float quantity, bool bagged)
        {
            if (quantity <= 0.005f) return "-";
            if (drug == null) return quantity.ToString("0.#") + "g";

            return bagged ? drug.Amount(quantity) : drug.Bulk(quantity);
        }
    }
}
