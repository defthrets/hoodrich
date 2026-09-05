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

                if (Purities[next] <= top + 0.001f) break;

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

        public void Draw()
        {
            if (!IsOpen || _rows.Count == 0) return;

            // 0.286 rather than 0.268: the wordmark added a band above the title and the
            // panel has to own that height, or the last row hangs off the bottom of it.
            var height = 0.300f + _rows.Count * RowHeight;

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
            // The title line, moved down to leave room for the mark above it. Everything
            // below steps off this, so shifting it here shifts the whole screen together.
            var y = top + 0.045f;

            // The mark, then the room -- the same order as every other screen. Anchored to
            // the panel TOP rather than to the title, because measuring it off the title put
            // it half a centimetre above the panel and outside its own ground.
            Hud.BrandCentre(left + panelWidth * 0.5f, top + 0.025f, 0.024f,
                            Palette.Alpha(Palette.Text, 225));

            // The house script, the same face every other screen in the mod is titled in --
            // and in the case it is written in rather than shouted. A cursive face set in block
            // capitals is two decisions fighting each other: handwriting is the informal one
            // and capitals are the formal one, and a room in somebody's house is the informal
            // thing.
            Hud.Text("The Kitchen", x, y - 0.004f, 0.74f, Palette.Text, Hud.FontCursive, centre: false);

            Hud.TextRight("$" + Game.Player.Money.ToString("N0"), right, y + 0.010f, 0.34f,
                          Palette.Cash, Hud.FontChaletLondon);

            y += 0.044f;

            Theme.Rule(x, y, panelWidth - pad * 2f, arrive);
            y += 0.012f;

            Hud.Text("WHAT YOU'RE WORKING", x, y, 0.26f, Palette.TextDim, Hud.FontLabel, centre: false);
            y += 0.026f;

            // THE PLATE COMES UP UNDER THE ROW rather than sliding to it: the one under the
            // new row rises over a sixth of a second while the one under the old row sinks,
            // and the frame -- see Glide -- travels between them. Same as every other screen.
            var grown = Theme.Grown(_pickedAt);
            var barWide = panelWidth - pad * 1.3f;

            _glide.Begin();

            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];

                var picked = i == _selected;
                var lit = Theme.Lit(i, _selected, _lastSelected, grown) * arrive;

                Theme.Plate(x - pad * 0.35f, y - 0.005f, barWide, RowHeight, lit);
                Theme.Sheen(x - pad * 0.35f, y - 0.005f, barWide, RowHeight, lit);

                if (picked) _glide.Target(x - pad * 0.35f, y - 0.005f, barWide, RowHeight);

                var have = Held(row.Source);
                var stored = _house == null ? 0f : _house.BulkOf(row.Source.Id);

                // The product's own art, in the gutter, the way every other screen in the mod
                // marks a row. Drawn from the SOURCE rather than the output: this column is
                // what you are putting on the counter, and the arrow in the label already says
                // what comes off it.
                //
                // Hud.File places by its CENTRE and Hud.Text by its TOP edge, so the art is
                // pushed down half a row to sit level with the words rather than above them.
                var art = Icons.ForDrug(row.Source.Id);
                var ax = x + Hud.ToX(ArtSize) * 0.5f;

                var ink = Theme.Ink(picked ? Palette.Text : Palette.TextDim, lit);

                var drew = art.HasFile &&
                           Hud.File(art.File, ax, y + RowHeight * 0.32f, ArtSize, 0f, ink);

                // Indented past the art when there is art, and left where it was when there is
                // not -- a row that silently loses its icon should lose the space with it
                // rather than sit in a column of its own.
                var tx = drew ? x + Hud.ToX(ArtSize) + 0.008f : x;

                Hud.Text(row.Label, tx, y, 0.30f, ink, Hud.FontBody, centre: false);

                // Where it is, when it is not simply on you. Otherwise a number that includes
                // the cupboard reads as a number in your pocket, and the two are not the same
                // thing the moment you walk out of the room.
                var where = stored > 0.005f
                    ? row.Source.Bulk(have) + (_stash.BulkOf(row.Source.Id) > 0.005f
                        ? "  (some in the cupboard)"
                        : "  (in the cupboard)")
                    : row.Source.Bulk(have);

                Hud.TextRight(where, right, y, 0.30f, ink, Hud.FontBody);

                y += RowHeight;
            }

            y += 0.012f;

            // The batch, spelled out: what goes in, what comes out, what it is worth.
            var chosen = _rows[_selected];
            var product = chosen.Source;
            var made = chosen.Output ?? chosen.Source;

            // What is on the counter, and what you can cut it to.
            //
            // A target above the source is not a choice, it is arithmetic that does not exist:
            // no amount of filler makes a gram stronger. So the chooser is capped at what the
            // weight already is, and fifty per cent weight simply has fewer options than
            // untouched weight does.
            var from = _stash.BulkPurityOf(product.Id);
            var purity = Math.Min(from, Purities[_purity]);

            var batch = Math.Min(MaxBatch, Workable(product));
            var yield = Cutting.YieldOf(product, made, batch, from, purity);
            var worth = _pricing.SaleValue(made, yield, purity);
            var risk = Pricing.BadCutChance(purity);
            var fits = _stash.FreeSpace >= yield - batch - 0.001f;

            Theme.Rule(x, y - 0.006f, panelWidth - pad * 2f, arrive);

            // Every cut on screen at once, with the one you are on lit up. They were always
            // all available -- left and right has stepped through them since the day it was
            // written -- but the screen only ever showed the one, so there was nothing to tell
            // you the other three existed.
            // The scales mark the row, and the chips start after them.
            //
            // They were drawn at x -- the same x the chips start at -- so the picture landed
            // straight on top of the first percentage. An icon over a number is worse than no
            // icon at all, because now neither can be read.
            var cx = x;

            if (Hud.File("scales.png", x + Hud.ToX(ArtSize) * 0.5f, y + 0.011f, ArtSize, 0f,
                         Palette.TextDim))
            {
                cx = x + Hud.ToX(ArtSize) + 0.008f;
            }

            for (var i = 0; i < Purities.Length; i++)
            {
                var label = (Purities[i] * 100f).ToString("0") + "%";
                var on = i == _purity;

                // Above what is already on the counter, so it is not a choice.
                //
                // No amount of filler makes a gram stronger, and half-strength weight simply has
                // fewer rungs left than untouched weight does. The maths has always refused this
                // -- the target is clamped to the source -- but the rung still looked pickable,
                // so choosing it appeared to do nothing. It is shown and greyed instead: what he
                // has and what he cannot get to, in one row.
                var tooStrong = Purities[i] > from + 0.001f;

                // The mark this step would leave on the bag, beside the number that makes it.
                // The same discs the stash shows afterwards, so what you pick here and what you
                // read on the shelf later are visibly one thing rather than two ways of saying
                // it.
                var mark = Stash.Mark(Purities[i]);
                var markW = Hud.ToX(MarkSize);

                var width = markW + 0.004f +
                            Hud.MeasureText(label, 0.30f, Hud.FontBody) + 0.011f;

                // The same plate the rows get, so the chosen cut and the chosen product are
                // visibly the same kind of thing.
                if (on && !tooStrong)
                {
                    Theme.Plate(cx - 0.004f, y - 0.002f, width, 0.026f, 1f);
                }

                var ink = tooStrong
                    ? Palette.Alpha(Palette.TextDisabled, 120)
                    : on ? Palette.Text : Palette.TextDim;

                // Under the floor it is drawn in the danger colour whether the cursor is on it
                // or not: "nobody will buy this" is a fact about the step, not about what you
                // happen to be looking at.
                if (!tooStrong && Purities[i] < Stash.Unsellable)
                {
                    ink = on ? Palette.Danger : Palette.Alpha(Palette.Danger, 175);
                }

                Hud.File(mark, cx + 0.003f + markW * 0.5f, y + 0.0115f, MarkSize, 0f, ink);

                Hud.Text(label, cx + 0.003f + markW + 0.005f, y + 0.002f, 0.30f, ink,
                         Hud.FontBody, centre: false);

                cx += width + 0.006f;
            }

            var outArt = Icons.ForDrug(made.Id);
            var words = (chosen.Rolling ? made.WorkVerb : product.WorkVerb) + "  ·  " + PurityWord(purity);

            y += 0.032f;

            // Weight in, count out. That arrow IS the press: grams of powder on the left and
            // whatever it presses into on the right, and writing both sides the same way was
            // what made the whole screen look like it turned pills into pills.
            var arrow = product.Bulk(batch) + "  ->  " + made.Amount(yield);

            Hud.Text(arrow, x, y, 0.30f, fits ? Palette.Cash : Palette.Danger,
                     Hud.FontBody, centre: false);

            var money = "$" + worth.ToString("N0");

            Hud.TextRight(money, right, y, 0.30f, fits ? Palette.Cash : Palette.Danger,
                          Hud.FontBody);

            // The verb and the purity word live on THIS line now, in the gap between what you
            // put in and what it is worth.
            //
            // They used to sit right-aligned on the row of percentages, which fitted exactly
            // until the percentages grew a disc each and then the two ran through one another.
            // Fitted to the space that is actually left rather than trusted to be short enough,
            // because that is the assumption that broke it the first time.
            try
            {
                var used = Hud.MeasureText(arrow, 0.30f, Hud.FontBody);
                var owed = Hud.MeasureText(money, 0.30f, Hud.FontBody);

                var room = right - owed - 0.010f - (x + used + 0.010f);

                if (room > 0.03f)
                {
                    var fitted = Hud.Fit(words, room, 0.28f, Hud.FontLabel);
                    var wide = Hud.MeasureText(fitted, 0.28f, Hud.FontLabel);
                    var wx = right - owed - 0.010f - wide;

                    if (outArt.HasFile && room > wide + Hud.ToX(ArtSize) + 0.006f)
                    {
                        Hud.File(outArt.File, wx - Hud.ToX(ArtSize) * 0.65f, y + 0.010f,
                                 ArtSize, 0f, Palette.Text);
                    }

                    Hud.Text(fitted, wx, y + 0.001f, 0.28f, Palette.Text, Hud.FontLabel,
                             centre: false);
                }
            }
            catch { /* the note underneath still says how far you are stepping on it */ }

            y += 0.028f;

            var note = !fits ? "Need room for " + made.Amount(Math.Max(0f, yield - batch)) +
                               " more -- leave some at home first"
                     : risk < 0.01f ? "Nobody is going to complain about this"
                     : risk < 0.2f ? "The odd buyer might notice"
                     : "Expect people to hand it back";

            Hud.Text(note, x, y, 0.26f, fits ? Palette.TextDim : Palette.Danger,
                     Hud.FontBody, centre: false);
            y += 0.026f;

            Hud.Text("UP / DOWN  PICK PRODUCT      LEFT / RIGHT  HOW FAR      " +
                     "ENTER  START      BACKSPACE  LEAVE",
                     x, top + height - 0.020f, 0.24f, Palette.TextDim, Hud.FontLabel, centre: false);

            // Last, so it rides over the rows it is pointing at.
            _glide.Draw(arrive);
        }

        private static string PurityWord(float purity)
        {
            if (purity >= 0.95f) return "Untouched";
            if (purity >= 0.75f) return "Barely stepped on";
            if (purity >= 0.50f) return "Cut half and half";
            if (purity < Stash.Unsellable) return "~r~Nobody will buy this";
            return "Stepped on hard";
        }
    }
}
