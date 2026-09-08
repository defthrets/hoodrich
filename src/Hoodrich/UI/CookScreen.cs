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

        /// <summary>
        /// How far it can be stretched, cleanest first.
        ///
        /// A third is the floor. There was a quarter under it, deliberately below what anybody
        /// would buy, and it is gone -- so nothing the counter makes is unsellable any more.
        /// The floor itself stays in the stash and the corner still refuses anything under it:
        /// blending can arrive at a number no single cut here reaches, and a bag that weak
        /// should still be a bag nobody takes.
        /// </summary>
        private static readonly float[] Purities = { 1.0f, 0.75f, 0.5f, 0.33f };

        /// <summary>How big the disc beside a percentage draws. A suffix, not a picture.</summary>
        private const float MarkSize = 0.0115f;

        /// <summary>
        /// The bag size used to be chosen here, and is not any more.
        ///
        /// Four options -- singles, eighths, quarters, ounces -- and the only thing any of them
        /// changed was how long you stood at the counter. The stash is measured in weight, so
        /// an ounce and twenty-eight singles were the same entry in it, worth the same money
        /// and sold the same way. The screen offered a decision that nothing downstream could
        /// read, which is a worse thing to put in front of somebody than no decision at all.
        ///
        /// What is left is the choice that does something: how far you step on it.
        /// </summary>

        /// <summary>One line at the counter: what you work, and what comes off it.</summary>
        private sealed class CookRow
        {
            public DrugDef Source;
            public DrugDef Output;

            public bool Rolling => Output != null && Source != null && Output.Id != Source.Id;

            public string Label => Rolling ? Source.Name + "  ->  " + Output.Name : Source.Name;
        }

        private readonly List<CookRow> _rows = new List<CookRow>();

        private Stash _stash;

        /// <summary>
        /// The cupboard behind you.
        ///
        /// The counter used to work only out of your pockets, so a house full of weight showed
        /// up as a one-line menu and there was nowhere to go back to. Anything in here can be
        /// worked without leaving the room; it gets fetched when the batch starts.
        /// </summary>
        private Stash _house;

        private Drugs _catalogue;
        private Pricing _pricing;
        private Func<DrugDef, DrugDef, float, float, string> _start;

        private int _selected;
        private int _purity;
        private int _openedAt;

        /// <summary>
        /// How tall a row's art is, as a fraction of screen height.
        ///
        /// Matched to the body text it sits beside rather than to the row box: art as tall as
        /// the row crowds the words either side of it, and these PNGs are authored square so
        /// the height is the whole size.
        /// </summary>
        private const float ArtSize = 0.019f;

        /// <summary>How this panel arrives and how it leaves. See UI.Curtain.</summary>
        private readonly Curtain _curtain = new Curtain();

        public bool IsOpen => _curtain.Showing;

        public void Open(Stash stash, Stash house, Drugs catalogue, Pricing pricing,
                         Func<DrugDef, DrugDef, float, float, string> start)
        {
            if (stash == null || catalogue == null || pricing == null) return;

            _stash = stash;
            _house = house;
            _catalogue = catalogue;
            _pricing = pricing;
            _start = start;

            _selected = 0;
            _purity = 0;
            _openedAt = Game.GameTime;
            _curtain.Open();
            _shownAt = Game.GameTime;

            // Landed, not arrived at: nothing fades out on open and the frame drops straight
            // onto the first row rather than gliding in from wherever it was last time.
            _lastSelected = -1;
            _pickedAt = Game.GameTime;
            _glide.Reset();

            Rebuild();

            if (_rows.Count == 0)
            {
                Notify.Problem("nothing here to work.");
                _curtain.Close();
                return;
            }

            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        public void Close()
        {
            // The button that got you out of here does not also swing at somebody.
            if (IsOpen) Core.InputGuard.Swallow();
            _curtain.Close();
            _stash = null;
            _house = null;
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
                if (Held(drug) <= 0.005f) continue;

                _rows.Add(new CookRow { Source = drug, Output = drug });

                // And the other thing it can become, if it can become one. Weed goes out either
                // bagged by weight or rolled and sold one at a time, and both are the same
                // weight of the same product on the counter.
                if (string.IsNullOrEmpty(drug.RollsInto)) continue;

                var rolled = _catalogue.Get(drug.RollsInto);
                if (rolled != null) _rows.Add(new CookRow { Source = drug, Output = rolled });
            }

            if (_selected >= _rows.Count) _selected = Math.Max(0, _rows.Count - 1);
        }

        /// <summary>
        /// How much of something there is to work, pockets and house together.
        ///
        /// The house share is capped by what your pockets can still take, because the batch has
        /// to physically come across the room before it can go on the counter. Offering to work
        /// a kilo you have no room to carry is offering something that cannot happen.
        /// </summary>
        private float Workable(DrugDef drug)
        {
            if (drug == null || _stash == null) return 0f;

            var onYou = _stash.BulkOf(drug.Id);
            if (_house == null) return onYou;

            var fromHouse = Math.Min(_house.BulkOf(drug.Id), RoomToWork());
            return onYou + Math.Max(0f, fromHouse);
        }

        /// <summary>
        /// How much you could get onto the counter, which is not the same as your free space.
        ///
        /// This deadlocked after exactly one batch, and it deadlocked hard: the cut PUTS its
        /// yield in your pockets, so the moment you finished one your pockets were full, free
        /// space was nil, and the cupboard's share clamped to nothing. Every row read zero, the
        /// batch read "0g -> 0g", and the screen said there was nothing to cut with thirty five
        /// kilos of it listed above the message.
        ///
        /// Free space was the wrong measure because you are STOOD IN THE HOUSE. Finished bags
        /// go in the cupboard -- that is what the cupboard is for -- so the room available is
        /// your pockets plus whatever of what you are carrying the cupboard would take. Begin
        /// does exactly that before it fetches, so this is a promise the next line keeps.
        /// </summary>
        private float RoomToWork()
        {
            if (_stash == null) return 0f;
            if (_house == null) return _stash.FreeSpace;

            return _stash.FreeSpace + Math.Min(_stash.Total, _house.FreeSpace);
        }

        /// <summary>
        /// Puts finished product in the cupboard until there is room to work.
        ///
        /// Only packaged, and only as much as is needed. Bulk stays where it is because bulk is
        /// the thing about to be worked, and taking more than the batch needs would empty your
        /// pockets every time you walked in here.
        /// </summary>
        private void MakeRoom(float wanted)
        {
            if (_house == null || _stash == null) return;

            foreach (var drug in _catalogue.All)
            {
                if (_stash.FreeSpace >= wanted - 0.001f) return;

                var held = _stash.PackagedOf(drug.Id);
                if (held <= 0.005f) continue;

                var need = wanted - _stash.FreeSpace;
                var move = Math.Min(held, Math.Min(need, _house.FreeSpace));
                if (move <= 0.005f) continue;

                var purity = _stash.PurityOf(drug.Id);
                var taken = _stash.RemovePackaged(drug.Id, move);
                if (taken <= 0f) continue;

                var put = _house.AddPackaged(drug.Id, taken, purity);
                if (put < taken - 0.005f) _stash.AddPackaged(drug.Id, taken - put, purity);
            }
        }

        /// <summary>
        /// How much of this there IS, on you and in the cupboard together.
        ///
        /// Kept apart from Workable, and that separation is the whole point. Workable clamps
        /// the cupboard's share by your free space -- correctly, because a batch has to cross
        /// the room before it can go on the counter -- but free space is ONE number shared by
        /// every product. So the moment the house held more of anything than you could carry,
        /// every row on this screen printed that same free-space figure instead of its own
        /// amount: marijuana 161g, oxycodone 161 pills, cocaine 161g, all of it the same 161.
        ///
        /// It looked exactly like the products were sharing a pool. They never were -- Stash
        /// keys bulk by drug id and always has. Pressing fifty pills only moved the number on
        /// the weed row because it moved your free space, and every row was quoting that.
        /// </summary>
        private float Held(DrugDef drug)
        {
            if (drug == null || _stash == null) return 0f;

            var onYou = _stash.BulkOf(drug.Id);
            return _house == null ? onYou : onYou + _house.BulkOf(drug.Id);
        }

        /// <summary>How much of this batch is coming out of the cupboard rather than your pocket.</summary>
        private float FromHouse(DrugDef drug, float batch)
        {
            if (drug == null || _stash == null) return 0f;
            return Math.Max(0f, batch - _stash.BulkOf(drug.Id));
        }

        /// <summary>
        /// Moves the shortfall across before the batch starts.
        ///
        /// Anything that will not fit goes straight back in the cupboard rather than being
        /// quietly lost, which matters because RemoveBulk has already taken it by then.
        /// </summary>
        private float Fetch(DrugDef drug, float wanted)
        {
            if (wanted <= 0.005f || _house == null || drug == null) return 0f;

            // THE STRENGTH TRAVELS WITH IT, read before the weight leaves the cupboard.
            //
            // AddBulk's purity argument defaults to 1f, and these were the only two calls in
            // the mod that left it off -- every other one passes a real value. So walking to
            // the kitchen with 200g of 75% in the house and fetching it onto the counter handed
            // you 200g of PURE, out of nothing, every batch. Cut that to a third and you got
            // 151g where the honest answer was 113g; sold it at 100% and the refusal chance
            // that punishes stepped-on product went to zero, and product rep never took the
            // knock it was supposed to. The whole penalty for buying cut weight was erased by
            // carrying it across one room.
            var strength = _house.BulkPurityOf(drug.Id);

            var taken = _house.RemoveBulk(drug.Id, wanted);
            if (taken <= 0f) return 0f;

            var accepted = _stash.AddBulk(drug.Id, taken, strength);
            var over = taken - accepted;

            if (over > 0.005f) _house.AddBulk(drug.Id, over, strength);

            return accepted;
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

        /// <summary>What is coming off the counter.</summary>
        private DrugDef Made()
        {
            if (_rows.Count == 0) return null;

            var row = _rows[Math.Max(0, Math.Min(_selected, _rows.Count - 1))];
            return row.Output ?? row.Source;
        }

        /// <summary>
        /// Moves along the rungs, skipping the ones stronger than what is on the counter.
        ///
        /// Greying them out is half the job. A cursor that still stops on a dead rung is a
        /// cursor that appears to be broken -- you press right, the number changes, and nothing
        /// about the batch does, because the maths clamps it straight back down.
        /// </summary>
        private void Step(int step)
        {
            var top = Strongest();

            var next = _purity;

            for (var tries = 0; tries < Purities.Length; tries++)
            {
                next = Math.Max(0, Math.Min(Purities.Length - 1, next + step));

                // BY THE BAND, NOT THE FLOAT. A cupboard of product at 0.999 after a few
                // merges is 100% on every screen and was refused the 100% rung here.
                if (Stash.Percent(Purities[next]) <= Stash.Percent(top)) break;

                // Walked into the dead end at the top of the list and there is nowhere further
                // to go in that direction.
                if (next == 0 || next == Purities.Length - 1) break;
            }

            if (Purities[next] > top + 0.001f) return;

            _purity = next;
            Hud.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        /// <summary>The purity of what is actually on the counter, or full if there is none.</summary>
        private float Strongest()
        {
            if (_rows.Count == 0 || _stash == null) return 1f;

            var row = _rows[Math.Max(0, Math.Min(_selected, _rows.Count - 1))];
            if (row == null || row.Source == null) return 1f;

            // The pocket if there is weight in it, otherwise the cupboard.
            //
            // These two go together with the fix above and cannot be separated. A row is listed
            // whenever there is weight in EITHER place, but this only ever asked the pocket --
            // and BulkPurityOf answers "full strength" for a drug you are holding none of. So
            // with the lot in the cupboard the screen offered a 100% rung for 75% product.
            // While Fetch was laundering it that was accidentally consistent; with Fetch fixed
            // and this left alone the batch would be fetched, then refused outright with
            // "that's already cut to 75%", and the fetched weight left on the counter.
            if (_stash.BulkOf(row.Source.Id) > 0.005f) return _stash.BulkPurityOf(row.Source.Id);

            return _house == null ? 1f : _house.BulkPurityOf(row.Source.Id);
        }

        private void Begin()
        {
            var row = _rows[_selected];
            var batch = Math.Min(MaxBatch, Workable(row.Source));

            // Bags in the cupboard first, so there is somewhere to put the weight.
            //
            // Without this the second batch could not start: your pockets came out of the first
            // one full of product, and bulk cannot be fetched into a full pocket. You are stood
            // at the cupboard -- putting the finished ones away is what a person does before
            // starting the next lot.
            var wanted = FromHouse(row.Source, batch);

            // ROOM FIRST, EVEN WHEN NOTHING NEEDS FETCHING.
            //
            // This used to be gated on wanted > 0, so it only ran when weight had to come out
            // of the cupboard. But the batch that fails for want of room is the one where the
            // bulk is ALREADY in your pocket -- two batches of weed at a third leave you with
            // 303g of packaged product and 47g of space, and the third batch is refused with
            // "no room for 152g, sell some first" while you are stood at a cupboard with four
            // and a half kilos free. The bags it needs to put away are the same bags either
            // way, so the fetch is not what decides whether to put them away.
            MakeRoom(Math.Max(wanted, batch));

            // Out of the cupboard and onto the counter, which is the step that used to have to
            // be done by hand through a different screen in a different room.
            var fetched = Fetch(row.Source, wanted);
            if (fetched > 0.005f)
            {
                Notify.Ticker("~s~You took " + row.Source.Short(fetched) + " out the cupboard.");
            }

            batch = Math.Min(batch, _stash.BulkOf(row.Source.Id));

            var failure = _start?.Invoke(row.Source, row.Output, batch, Purities[_purity]);
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
            Core.Fists.Off();
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

        /// <summary>When the screen went up, for its entrance.</summary>
        private int _shownAt;

        /// <summary>The row the cursor was on before this one, and when it moved. See Theme.Lit.</summary>
        private int _lastSelected = -1;
        private int _pickedAt;

        /// <summary>The cursor frame that glides between rows. See UI.Glide.</summary>
        private readonly Glide _glide = new Glide();

        private const int EnterMs = 170;
        private const float EnterRise = 0.014f;

        /// <summary>The heights of the parts, so the panel is as tall as what is in it.</summary>
        private const float LabelH = 0.026f;
        private const float RowH = 0.030f;
        private const float GaugeH = 0.086f;
        private const float BatchH = 0.104f;
        private const float RungH = 0.032f;

        /// <summary>
        /// Where the lit rung is, easing toward the one chosen so the plate slides along the
        /// gauge rather than jumping between steps. See UI.Eased.
        /// </summary>
        private readonly Eased _rung = new Eased();

        public void Draw()
        {
            if (!IsOpen || _rows.Count == 0) return;

            var height = UiKit.HeadH + LabelH + _rows.Count * RowH + 0.014f + GaugeH + BatchH + UiKit.FootH;

            var panelWidth = Hud.ToX(PanelWidthH);
            var pad = Hud.ToX(PadH);

            var left = 0.5f - panelWidth * 0.5f;
            var top = 0.5f - height * 0.5f + _curtain.Lift;

            // Up and in, eased out so it slows as it lands -- the same arrival every other
            // screen in the mod uses, so opening any of them feels like opening one thing.
            var age = Game.GameTime - _shownAt;
            var arrive = age >= EnterMs ? 1f : age / (float)EnterMs;
            arrive = 1f - (1f - arrive) * (1f - arrive);

            top += EnterRise * (1f - arrive);

            Theme.Panel(left, top, panelWidth, height, arrive);

            var x = left + pad;
            var right = left + panelWidth - pad;
            var wide = right - x;

            // ---- the letterhead ----
            //
            // The mark, then the room in the house script -- written rather than shouted,
            // because a room in somebody's house is the informal thing -- and the money on
            // the right, the same rhythm as the other two screens' heads.
            Hud.BrandCentre(left + panelWidth * 0.5f, top + 0.021f, 0.019f,
                            Palette.Alpha(Palette.Text, (int)(225f * arrive)));

            Hud.Text("The Kitchen", x, top + 0.026f, 0.70f, Palette.Alpha(Palette.Text, (int)(255f * arrive)),
                     Hud.FontCursive, centre: false);

            Hud.TextRight("$" + Game.Player.Money.ToString("N0"), right, top + 0.047f, 0.32f,
                          Palette.Alpha(Palette.Cash, (int)(255f * arrive)), Hud.FontChaletLondon);

            Theme.Rule(x, top + UiKit.HeadH - 0.006f, wide, arrive);

            var y = top + UiKit.HeadH;

            // ---- what you are working ----
            Hud.Text("WHAT YOU'RE WORKING", x, y, 0.24f, Palette.Alpha(Palette.TextDim, (int)(200f * arrive)),
                     Hud.FontLabel, centre: false);

            y += LabelH;

            var grown = Theme.Grown(_pickedAt);
            var barWide = panelWidth - pad * 1.3f;

            _glide.Begin();

            for (var i = 0; i < _rows.Count; i++)
            {
                Line(_rows[i], i, grown, x, right, pad, barWide, y, arrive);
                y += RowH;
            }

            y += 0.014f;

            Theme.Rule(x, y - 0.008f, wide, arrive);

            // ---- how far you step on it ----
            var chosen = _rows[_selected];
            var product = chosen.Source;
            var made = chosen.Output ?? chosen.Source;

            // What is on the counter, and what you can cut it to. A target above the source
            // is not a choice: no amount of filler makes a gram stronger.
            var from = Strongest();
            var purity = Math.Min(from, Purities[_purity]);

            Gauge(x, wide, y, from, arrive);

            y += GaugeH;

            // ---- the batch, spelled out ----
            var batch = Math.Min(MaxBatch, Workable(product));
            var yield = Cutting.YieldOf(product, made, batch, from, purity);
            var worth = _pricing.SaleValue(made, yield, purity);
            var risk = Pricing.BadCutChance(purity);
            var fits = _stash.FreeSpace >= yield - batch - 0.001f;

            Batch(x, wide, y, chosen, product, made, batch, yield, worth, risk, purity, fits, arrive);

            // ---- the keys ----
            var footY = top + height - UiKit.FootH + 0.006f;

            Theme.Rule(x, footY, wide, arrive);

            var ky = footY + 0.011f;

            UiKit.KeyRight(right, ky, UiKit.Back, "LEAVE", arrive);

            var kx = UiKit.Key(x, ky, null, "arrow_updown.png", "PICK PRODUCT", arrive);
            kx = UiKit.Key(kx, ky, null, "arrow_leftright.png", "HOW FAR", arrive);
            UiKit.Key(kx, ky, UiKit.Confirm, null, "START", arrive);

            // Last, so it rides over the rows it is pointing at.
            _glide.Draw(arrive);
        }

        /// <summary>
        /// One thing on the counter: its picture, its name, what it turns into if it turns
        /// into something, how much of it there is, and where it is when it is not on you.
        /// </summary>
        private void Line(CookRow row, int i, float grown, float x, float right, float pad,
                          float barWide, float y, float arrive)
        {
            var picked = i == _selected;
            var lit = Theme.Lit(i, _selected, _lastSelected, grown) * arrive;

            Theme.Plate(x - pad * 0.35f, y, barWide, RowH, lit);
            Theme.Sheen(x - pad * 0.35f, y, barWide, RowH, lit);

            if (picked) _glide.Target(x - pad * 0.35f, y, barWide, RowH);

            var have = Held(row.Source);
            var stored = _house == null ? 0f : _house.BulkOf(row.Source.Id);
            var onYou = _stash == null ? 0f : _stash.BulkOf(row.Source.Id);

            var ink = Theme.Ink(Palette.Alpha(picked ? Palette.Text : Palette.TextDim, (int)(255f * arrive)), lit);

            var textY = y + 0.005f;
            var midY = y + RowH * 0.5f;

            // The SOURCE's art in the gutter: this column is what you put on the counter,
            // and the arrow in the label says what comes off it.
            var art = Icons.ForDrug(row.Source.Id);
            var tx = x;

            if (art.HasFile && Hud.File(art.File, x + Hud.ToX(ArtSize) * 0.5f, midY, ArtSize, 0f, ink))
            {
                tx = x + Hud.ToX(ArtSize) + 0.008f;
            }

            // ---- how much, and where, on the right ----
            var amount = row.Source.Bulk(have);
            var amountW = Hud.MeasureText(amount, 0.30f, Hud.FontBody);

            Hud.TextRight(amount, right, textY, 0.30f, ink, Hud.FontBody);

            var leftEdge = right - amountW - 0.010f;

            // Where it is, when it is not simply on you. A number that includes the cupboard
            // reads as a number in your pocket, and the two are not the same thing the moment
            // you walk out of the room.
            if (stored > 0.005f)
            {
                var tag = onYou > 0.005f ? "SOME IN CUPBOARD" : "IN CUPBOARD";
                var tagW = Hud.MeasureText(tag, 0.19f, Hud.FontLabel) + 0.007f;

                UiKit.Tag(leftEdge - tagW, textY + 0.0025f, tag, Palette.TextDim, arrive * (0.75f + 0.25f * lit));

                leftEdge -= tagW + 0.010f;
            }

            // ---- the name, and what it becomes ----
            var label = row.Rolling ? row.Source.Name + "   >   " + row.Output.Name : row.Source.Name;

            var outArt = row.Rolling ? Icons.ForDrug(row.Output.Id) : default(Icon);
            var outW = outArt.HasFile ? Hud.ToX(ArtSize) + 0.006f : 0f;

            var fitted = Hud.Fit(label, leftEdge - tx - outW, 0.30f, Hud.FontBody);

            Hud.Text(fitted, tx, textY, 0.30f, ink, Hud.FontBody, centre: false);

            if (outArt.HasFile && fitted == label)
            {
                var after = tx + Hud.MeasureText(fitted, 0.30f, Hud.FontBody) + 0.006f;

                Hud.File(outArt.File, after + Hud.ToX(ArtSize) * 0.5f, midY, ArtSize, 0f, ink);
            }
        }

        /// <summary>
        /// The gauge: every cut on one row, the lit plate sliding to the one you are on.
        ///
        /// Rungs stronger than what is on the counter are greyed and locked -- the maths has
        /// always refused them, but a rung that looks pickable and does nothing looks like a
        /// broken control. Under the sellable floor the rung is red whether the cursor is on
        /// it or not: "nobody will buy this" is a fact about the step, not about where you
        /// happen to be looking.
        /// </summary>
        private void Gauge(float x, float wide, float y, float from, float arrive)
        {
            var tx = x;

            if (Hud.File("scales.png", x + Hud.ToX(0.015f) * 0.5f, y + 0.0085f, 0.015f, 0f,
                         Palette.Alpha(Palette.TextDim, (int)(220f * arrive))))
            {
                tx = x + Hud.ToX(0.015f) + 0.006f;
            }

            Hud.Text("HOW FAR YOU STEP ON IT", tx, y, 0.24f,
                     Palette.Alpha(Palette.TextDim, (int)(200f * arrive)), Hud.FontLabel, centre: false);

            // What it is now, on the right, so the locked rungs explain themselves.
            Hud.TextRight("ON THE COUNTER  " + Stash.Percent(from) + "%", x + wide, y, 0.24f,
                          Palette.Alpha(Palette.TextDim, (int)(200f * arrive)), Hud.FontLabel);

            var rungY = y + 0.028f;
            var gap = Hud.ToX(0.006f);
            var rungW = (wide - gap * (Purities.Length - 1)) / Purities.Length;

            // The track: one dark square per rung.
            for (var i = 0; i < Purities.Length; i++)
            {
                Hud.RectFrom(x + i * (rungW + gap), rungY, rungW, RungH,
                             Color.FromArgb((int)(24f * arrive), 255, 255, 255));
            }

            // The plate, sliding.
            var slid = _rung.To(x + _purity * (rungW + gap), 14f);

            Theme.Plate(slid, rungY, rungW, RungH, arrive);
            Theme.Sheen(slid, rungY, rungW, RungH, arrive);

            var markW = Hud.ToX(MarkSize);

            for (var i = 0; i < Purities.Length; i++)
            {
                var rungX = x + i * (rungW + gap);
                var on = i == _purity;

                var tooStrong = Stash.Percent(Purities[i]) > Stash.Percent(from);
                var under = Purities[i] < Stash.Unsellable;

                var ink = tooStrong ? Palette.Alpha(Palette.TextDisabled, 110)
                        : under ? (on ? Palette.Danger : Palette.Alpha(Palette.Danger, 175))
                        : on ? Palette.Text : Palette.TextDim;

                ink = Palette.Alpha(ink, (int)(ink.A * arrive));

                // The mark this step leaves on the bag -- the same disc the stash shows
                // afterwards -- then the number that makes it.
                Hud.File(Stash.Mark(Purities[i]), rungX + 0.008f + markW * 0.5f, rungY + RungH * 0.5f,
                         MarkSize, 0f, ink);

                Hud.Text((Purities[i] * 100f).ToString("0") + "%", rungX + 0.008f + markW + 0.006f,
                         rungY + 0.006f, 0.30f, ink, Hud.FontBody, centre: false);

                if (tooStrong)
                {
                    Hud.File("locked.png", rungX + rungW - 0.006f - Hud.ToX(0.011f) * 0.5f,
                             rungY + RungH * 0.5f, 0.011f, 0f, ink);
                }
            }
        }

        /// <summary>
        /// The batch as a card: what goes in on the left, chevrons running through the
        /// press, what comes out, and what it is worth on the right. The verb and the purity
        /// word underneath, and the note under the card -- how it will be received, or why it
        /// cannot start.
        /// </summary>
        private void Batch(float x, float wide, float y, CookRow chosen, DrugDef product, DrugDef made,
                           float batch, float yield, float worth, float risk, float purity, bool fits,
                           float arrive)
        {
            var cardH = BatchH - 0.034f;

            Theme.Plate(x, y, wide, cardH, 0.5f * arrive);

            var good = fits ? Palette.Cash : Palette.Danger;
            var goodInk = Palette.Alpha(good, (int)(255f * arrive));
            var white = Palette.Alpha(Palette.Text, (int)(240f * arrive));

            var lineY = y + 0.009f;
            var midY = lineY + 0.011f;

            // ---- in ----
            var inArt = Icons.ForDrug(product.Id);
            var cx = x + 0.010f;

            if (inArt.HasFile &&
                Hud.File(inArt.File, cx + Hud.ToX(BatchArt) * 0.5f, midY, BatchArt, 0f, white))
            {
                cx += Hud.ToX(BatchArt) + 0.008f;
            }

            var inText = product.Bulk(batch);

            Hud.Text(inText, cx, lineY, 0.32f, white, Hud.FontBody, centre: false);
            cx += Hud.MeasureText(inText, 0.32f, Hud.FontBody) + 0.010f;

            // ---- through the press ----
            //
            // The chevrons ARE the press: grams of powder on the left and whatever it presses
            // into on the right. Green while the batch can start, red when there is no room
            // for what would come out of it.
            var flowW = 0.040f;

            UiKit.Flow(cx, lineY, flowW, 1, arrive, good, 0.32f);
            cx += flowW + 0.010f;

            // ---- out ----
            var outArt = Icons.ForDrug(made.Id);

            if (outArt.HasFile &&
                Hud.File(outArt.File, cx + Hud.ToX(BatchArt) * 0.5f, midY, BatchArt, 0f, goodInk))
            {
                cx += Hud.ToX(BatchArt) + 0.008f;
            }

            Hud.Text(made.Amount(yield), cx, lineY, 0.32f, goodInk, Hud.FontBody, centre: false);

            // ---- worth ----
            Hud.TextRight("$" + worth.ToString("N0"), x + wide - 0.010f, y + 0.006f, 0.44f, goodInk,
                          Hud.FontLabel);

            // ---- the verb and the purity word ----
            var words = (chosen.Rolling ? made.WorkVerb : product.WorkVerb) + "  ·  " + UiKit.PurityWord(purity);

            Hud.Text(words, x + 0.010f, y + 0.040f, 0.27f,
                     Palette.Alpha(UiKit.PurityInk(purity), (int)(220f * arrive)), Hud.FontBody, centre: false);

            // ---- the note ----
            var note = !fits ? "Need room for " + made.Amount(Math.Max(0f, yield - batch)) +
                               " more -- leave some at home first"
                     : risk < 0.01f ? "Nobody is going to complain about this"
                     : risk < 0.2f ? "The odd buyer might notice"
                     : "Expect people to hand it back";

            Hud.Text(note, x, y + cardH + 0.008f, 0.26f,
                     Palette.Alpha(fits ? Palette.TextDim : Palette.Danger, (int)(220f * arrive)),
                     Hud.FontBody, centre: false);
        }

        private const float BatchArt = 0.026f;
    }
}
