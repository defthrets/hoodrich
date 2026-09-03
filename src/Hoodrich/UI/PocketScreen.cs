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

        /// <summary>
        /// What Bare Minimum says is in your pockets, if it is installed.
        ///
        /// A STRIP OF TILES RATHER THAN MORE LINES. Product is a list because every row is a
        /// name and a weight and a purity -- things you read. Food is three or four items that
        /// each have a picture drawn for them, so it is a row of pictures you recognise, and
        /// putting it under the product rather than beside it keeps one column of reading and
        /// one band of looking.
        ///
        /// Ids only. The names, the pictures and the counts are asked for at draw time, which
        /// keeps everything about the other mod behind one late-bound wall.
        /// </summary>
        private readonly List<string> _food = new List<string>();

        /// <summary>
        /// The food band's own height: a rule, a heading, one row of tiles, and a caption.
        ///
        /// Measured from the parts rather than guessed at. The first version reserved less
        /// than the tiles needed and drew the heading straight over "Nothing on you", which is
        /// what happens when a panel's height and its contents are two separate opinions.
        /// </summary>
        private const float FoodTile = 0.044f;

        private const float FoodHead = 0.022f;
        private const float FoodCap = 0.020f;
        private const float FoodStrip = 0.014f + FoodHead + FoodTile + FoodCap;

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

        /// <summary>When the cursor last moved, so the tile it landed on can grow into it.</summary>
        private int _pickedAt;

        /// <summary>How long that takes, and how much bigger the chosen tile's picture gets.</summary>
        private const int PickMs = 150;
        private const float PickGrow = 0.10f;

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
            _pickedAt = Game.GameTime;
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

            _food.Clear();
            foreach (var id in Core.Larder.Ids()) _food.Add(id);

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

            if (Places == 0) return;

            if (Game.IsControlJustPressed(Control.PhoneUp)) Move(-1);
            else if (Game.IsControlJustPressed(Control.PhoneDown)) Move(1);

            // ---- food ----
            //
            // SELECT rather than left/right, because left and right already mean "put it down"
            // and a key that drops product on one row and eats a taco on the next is the kind
            // of control nobody trusts twice.
            if (OnFood)
            {
                if (!Game.IsControlJustPressed(Control.PhoneSelect)) return;

                var at = _selected - _rows.Count;
                if (at < 0 || at >= _food.Count) return;

                // QUEUED, NOT EATEN HERE. The phone is an animation as well as a screen, and
                // starting a meal underneath it is two clips claiming one player. Larder waits
                // for the handset to be down and then does it -- see EatWhenPhoneIsAway.
                Core.Larder.EatWhenPhoneIsAway(_food[at]);

                Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");

                Close();
                return;
            }

            // Left or right, because there is only one direction anything can go from here and
            // making you learn which of the two it is would be a puzzle rather than a control.
            var down = Game.IsControlPressed(Control.PhoneLeft) ||
                       Game.IsControlPressed(Control.PhoneRight);

            if (down && Game.GameTime >= _nextRepeat) Drop(Game.IsControlPressed(Control.Sprint));
        }

        /// <summary>Product rows first, then one place per food tile.</summary>
        private int Places => _rows.Count + _food.Count;

        private bool OnFood => _selected >= _rows.Count;

        private void Move(int step)
        {
            if (Places == 0) return;

            _selected = (_selected + step) % Places;
            if (_selected < 0) _selected += Places;

            _pickedAt = Game.GameTime;

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
            Core.Fists.Off();
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

            // Only when there is food. An empty band with a heading over it is a promise the
            // screen is not keeping.
            if (_food.Count > 0) height += FoodStrip;

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

            var ix = x;

            if (Hud.File("stash.png", x + Hud.ToX(HeadIcon) * 0.5f, y + 0.007f, HeadIcon, 0f,
                         Palette.Alpha(Palette.Accent, 200)))
            {
                ix = x + Hud.ToX(HeadIcon) + 0.006f;
            }

            Hud.Text("INVENTORY", ix, y, 0.30f, Palette.Text, Hud.FontLabel, centre: false);

            Hud.TextRight(_rows.Count + (_rows.Count == 1 ? " LINE" : " LINES"),
                          right, y, 0.24f, Palette.TextDim, Hud.FontLabel);

            y += 0.028f;

            Carrying(x, wide, y);

            y = top + HeadHeight - 0.008f;
            Hud.RectFrom(x, y, wide, 0.0012f, Hairline);

            y = top + HeadHeight;

            if (_rows.Count == 0)
            {
                // AN EMPTY BAG BESIDE THE SENTENCE, not above it. The panel's height is worked
                // out from a row count and this state occupies exactly one row -- anything
                // stacked here comes out of the food band's space, which is how the heading
                // ended up drawn over this sentence the first time round.
                var ex = x;

                if (Hud.File("baggie.png", x + Hud.ToX(ArtSize) * 0.5f, y + RowHeight * 0.34f,
                             ArtSize, 0f, Palette.Alpha(Palette.TextDim, 120)))
                {
                    ex = x + Hud.ToX(ArtSize) + 0.007f;
                }

                Hud.Text("Nothing on you.", ex, y + 0.006f, 0.28f,
                         Palette.TextDim, Hud.FontBody, centre: false);
            }
            else if (!OnFood)
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

            // "Nothing on you." occupies a row that the loop above never walked, and the panel
            // was already sized for it -- so the cursor has to step over it too, or the food
            // band starts on top of the sentence.
            if (_rows.Count == 0) y += RowHeight;

            if (_food.Count > 0) FoodBand(x, wide, y + 0.008f, arrive);

            var footY = top + height - FootHeight + 0.008f;

            Hud.RectFrom(x, footY, wide, 0.0010f, Hairline);

            Keys(x, right, footY + 0.008f);

            Hud.Frame(left, top, panelWidth, height,
                      Color.FromArgb(150, 90, 215, 235), Palette.Accent, Rule, Tick);
        }

        /// <summary>
        /// What the buttons do, in the names of the buttons this player is actually holding.
        ///
        /// SPLIT LEFT AND RIGHT rather than one long run-on line, which is how every other
        /// panel in the mod does it. The way OUT is the thing you look for when you are lost,
        /// and it should be in the same place on every screen rather than at the end of a
        /// sentence whose length depends on what you are carrying.
        ///
        /// The pad names are Xbox letters because that is what the game itself prints on PC,
        /// whatever is plugged in. Naming them A and B and being wrong about a DualSense is
        /// better than naming neither and being useless to both.
        /// </summary>
        private void Keys(float x, float right, float y)
        {
            var pad = Hud.OnPad;

            Hud.TextRight(pad ? "B  DONE" : "BACKSPACE  DONE", right, y, 0.24f,
                          Palette.TextDim, Hud.FontLabel);

            if (Places == 0) return;

            var hx = Hud.Hint("arrow_updown.png", "PICK", x, y, 0.24f, Palette.TextDim);

            if (OnFood)
            {
                Hud.Hint(null, pad ? "A  EAT IT" : "ENTER  EAT IT", hx, y, 0.24f,
                         Palette.TextDim);
                return;
            }

            // THE DROP MARK IS THE ONE PICTURE ON THIS SCREEN THAT IS DOING REAL WORK. An arrow
            // onto a line is "onto the floor", and that this screen puts things on the FLOOR --
            // rather than into a bag, a boot or a shelf -- is the one thing about it nobody
            // would guess from a list of drugs with a left and right key.
            //
            // Left and right both do it, and neither is named. There is only one direction
            // anything can go from here, so making you work out which of the two keys it is
            // would be a puzzle rather than a control.
            hx = Hud.Hint("drop.png", "PUT IT DOWN", hx, y, 0.24f, Palette.TextDim);

            Hud.Hint(null, (pad ? "HOLD A" : "SPRINT") + "  ALL OF IT", hx, y, 0.24f,
                     Palette.TextDim);
        }

        /// <summary>
        /// The food band: a rule, a heading, a row of square tiles, and the name of the one
        /// under the cursor.
        ///
        /// LAID OUT LIKE BARE MINIMUM'S OWN POCKET, deliberately. That screen is a grid of
        /// square tiles with the picture centred, a count in the corner and the name of the
        /// selected one on a line of its own underneath -- and a player who has seen it once
        /// should not have to learn a second arrangement of the same three facts because they
        /// happened to open the phone instead of pressing F11.
        ///
        /// The tiles are that mod's own PNGs, by absolute path. Hud.File runs its argument
        /// through Path.Combine against this mod's icon folder, and Path.Combine hands back
        /// the second argument whole when it is already rooted -- so a full path loads as it
        /// is and neither mod has to know where the other keeps its art.
        /// </summary>
        private void FoodBand(float x, float width, float y, float arrive)
        {
            Hud.RectFrom(x, y, width, 0.0012f, Hairline);

            y += 0.012f;

            // ---- their mark, then the heading ----
            //
            // The drumstick is Bare Minimum's own, the same one it puts beside its menu titles
            // and draws in the corner of the screen. Borrowed rather than drawn again: this
            // band is that mod's content sitting in this mod's phone, and saying whose it is
            // costs one icon.
            var tx = x;

            var mark = Core.Larder.Mark("food");

            if (!string.IsNullOrEmpty(mark) &&
                Hud.File(mark, x + Hud.ToX(0.014f) * 0.5f, y + 0.007f, 0.014f, 0f,
                         Palette.Alpha(Palette.Accent, (int)(220f * arrive))))
            {
                tx = x + Hud.ToX(0.014f) + 0.005f;
            }

            Hud.Text("FOOD", tx, y, 0.28f, Palette.Text, Hud.FontLabel, centre: false);

            // Carried out of capacity, the same reading the product band gives above it --
            // INCLUDING THE COLOUR, which it did not have and needed. The other mod's pantry
            // can end up over its own cap (its slot count is a setting, and lowering it does
            // not take anything off you) and "4 / 3" printed in the same grey as "1 / 3" reads
            // as a broken number rather than as a full bag.
            var carried = Core.Larder.Total;
            var slots = Core.Larder.Slots;

            var full = slots > 0 && carried >= slots;

            Hud.TextRight(carried + " / " + slots, x + width, y, 0.24f,
                          full ? Palette.Warn : Palette.TextDim, Hud.FontLabel);

            var tileY = y + FoodHead;

            var tile = Hud.ToX(FoodTile);
            var gap = Hud.ToX(0.006f);

            // ---- how the tiles arrive ----
            //
            // STAGGERED, a frame or two apart. All of them fading up together is the panel
            // appearing twice; one after another reads as the pocket being unpacked, and it is
            // the same trick the shop shelf uses on its row pictures.
            var age = Game.GameTime - _shownAt;

            // The travelling sheen, on the same clock as the one crossing the panel above, so
            // the two are never quite in step and never quite unrelated.
            var sweep = (Game.GameTime % SweepMs) / (float)SweepMs;

            for (var i = 0; i < _food.Count; i++)
            {
                var id = _food[i];

                var lead = i * 60;

                var land = age <= lead ? 0f : (age - lead) / (float)EnterMs;
                if (land > 1f) land = 1f;

                // Eased out, so it settles rather than stopping dead.
                land = 1f - (1f - land) * (1f - land);

                var show = arrive * land;
                if (show <= 0.01f) continue;

                var tileTop = tileY + EnterRise * 0.5f * (1f - land);

                var tx2 = x + i * (tile + gap);
                var picked = _selected - _rows.Count == i;

                // The plate. Amber under the cursor, and a dark tile with a hairline otherwise
                // -- an unfilled square with only an outline reads as an empty slot.
                Hud.RectFrom(tx2, tileTop, tile, FoodTile,
                             picked ? Color.FromArgb((int)(235f * show), 240, 170, 56)
                                    : Color.FromArgb((int)(38f * show), 255, 255, 255));

                if (!picked)
                {
                    Hud.RectFrom(tx2, tileTop, tile, 0.0012f,
                                 Color.FromArgb((int)(70f * show), 255, 255, 255));
                }
                else
                {
                    // A band of light crossing the chosen tile. Clipped to the tile rather
                    // than drawn over it, or it is a stripe on the panel that happens to pass
                    // a tile on its way.
                    var bandW = tile * 0.34f;
                    var bandAt = tx2 - bandW + (tile + bandW) * sweep;

                    var lo = Math.Max(tx2, bandAt);
                    var hi = Math.Min(tx2 + tile, bandAt + bandW);

                    if (hi > lo)
                    {
                        Hud.RectFrom(lo, tileTop, hi - lo, FoodTile,
                                     Color.FromArgb((int)(40f * show), 255, 255, 255));
                    }
                }

                var art = Core.Larder.IconOf(id);

                if (!string.IsNullOrEmpty(art))
                {
                    // THE ONE UNDER THE CURSOR GROWS INTO IT. The plate changing colour is the
                    // whole of the feedback otherwise, and on a strip of four squares that is a
                    // colour swap you can miss while your eyes are on the caption underneath.
                    // A picture that swells over a sixth of a second is movement, and movement
                    // is what the eye actually catches.
                    var held = Game.GameTime - _pickedAt;
                    var grown = held >= PickMs ? 1f : held / (float)PickMs;

                    grown = 1f - (1f - grown) * (1f - grown);

                    var swell = picked ? 1f + PickGrow * grown : 1f;
                    // Near-black on the amber tile, the item's own colour otherwise. The art is
                    // white and CustomSprite multiplies, so one file does both.
                    var ink = picked
                        ? Color.FromArgb((int)(255f * show), 20, 18, 14)
                        : Palette.Alpha(Core.Larder.TintOf(id), (int)(238f * show));

                    Hud.File(art, tx2 + tile * 0.5f, tileTop + FoodTile * 0.46f,
                             FoodTile * 0.58f * swell, 0f, ink);
                }

                // ---- how many ----
                //
                // On its own dark chip rather than straight onto the tile. The number has to
                // read over amber and over a dark square, and one ink cannot do both.
                var many = Core.Larder.CountOf(id);

                if (many > 1)
                {
                    var chip = Hud.ToX(0.013f);

                    Hud.RectFrom(tx2 + tile - chip, tileTop + FoodTile - 0.013f, chip, 0.013f,
                                 Color.FromArgb((int)(215f * show), 12, 13, 15));

                    Hud.TextRight(many.ToString(), tx2 + tile - 0.0015f,
                                  tileTop + FoodTile - 0.0125f, 0.23f,
                                  Palette.Alpha(Palette.Text, (int)(255f * show)), Hud.FontLabel);
                }
            }

            // ---- what it is ----
            //
            // Under the grid on its own line, which is where the pocket screen puts it. It sat
            // right-aligned level with the tiles at first, so the name of the thing on the far
            // left appeared on the far right with the width of the panel between them.
            var capY = tileY + FoodTile + 0.005f;

            if (OnFood)
            {
                var at = _selected - _rows.Count;

                if (at >= 0 && at < _food.Count)
                {
                    Hud.Text(Core.Larder.NameOf(_food[at]), x, capY, 0.28f,
                             Palette.Text, Hud.FontBody, centre: false);
                    return;
                }
            }

            Hud.Text(_food.Count == 1 ? "1 thing to eat" : _food.Count + " things to eat",
                     x, capY, 0.26f, Palette.Alpha(Palette.TextDim, 190),
                     Hud.FontBody, centre: false);
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

            Hud.TextRight(row.Bagged ? row.Drug.Amount(row.Held) : row.Drug.Bulk(row.Held),
                          right, y, 0.30f,
                          flashing ? Palette.Warn : picked ? Palette.Standing : Palette.TextDim,
                          Hud.FontBody);
        }
    }
}
