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

        private Stash _pockets;
        private Drugs _catalogue;
        private DroppedBags _bags;

        /// <summary>Set by Main. What happens when you take some of it yourself.</summary>
        public Economy.Highs Highs;

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
        /// <summary>
        /// How big a tile is.
        ///
        /// UP FROM 0.044, WHICH WAS SIZED WHEN ONLY THE FOOD USED IT. Three or four food
        /// pictures at that size sat in a corner and looked deliberate; a whole inventory of
        /// them looks like a row of stamps, and the amount written across the bottom of one had
        /// nowhere to go. Both bands share the constant, so both grow together.
        /// </summary>
        private const float FoodTile = 0.056f;

        private const float FoodHead = 0.022f;
        private const float FoodCap = 0.020f;
        private const float FoodStrip = 0.014f + FoodHead + FoodTile + FoodCap;

        /// <summary>
        /// The product tiles, which are the food tiles.
        ///
        /// SAME SIZE, SAME GAP, SAME CAPTION UNDERNEATH -- literally the same numbers, because
        /// the two bands sit one above the other and any difference between them reads as a
        /// mistake rather than as a distinction. The product used to be a list of lines with a
        /// 0.019 icon on the left and the food a grid of 0.044 squares, and stacked together
        /// they looked like two screens that had been glued.
        /// </summary>
        private const float Cell = FoodTile;
        private const float CellGap = 0.006f;
        private const float CellCap = 0.020f;

        /// <summary>The bottom strip on a tile that carries the amount.</summary>
        private const float ChipHeight = 0.0125f;

        private int _selected;
        private int _openedAt;
        private int _nextRepeat;

        /// <summary>The entrance, and the flash on a row that just changed.</summary>
        private const int EnterMs = 170;
        private const float EnterRise = 0.014f;

        private int _shownAt;

        private int _droppedAt;
        private int _droppedRow = -1;

        /// <summary>When the cursor last moved, so the tile it landed on can grow into it.</summary>
        private int _pickedAt;

        /// <summary>The tile it was on before that, so its plate goes down as the new one comes up.</summary>
        private int _lastSelected = -1;

        /// <summary>The cursor frame that glides between tiles. See UI.Glide.</summary>
        private readonly Glide _glide = new Glide();

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
            _lastSelected = -1;
            _glide.Reset();
            _openedAt = Game.GameTime;
            _pickedAt = Game.GameTime;
            _shownAt = Game.GameTime;
            _nextRepeat = 0;
            _droppedRow = -1;

            Rebuild();

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

            // ---- LEFT AND RIGHT ALONG THE STRIP, UP AND DOWN BETWEEN THEM ----
            //
            // These are rows of squares now, and up and down through a row of squares is the
            // wrong shape entirely -- the cursor jumped sideways when the key said vertical.
            // Sideways is what the eye expects and it is what the tiles are laid out for.
            //
            // Which freed up nothing, because left and right USED to put things on the floor.
            // See Jump below.
            if (Game.IsControlJustPressed(Control.PhoneRight)) Move(1);
            else if (Game.IsControlJustPressed(Control.PhoneLeft)) Move(-1);
            else if (Game.IsControlJustPressed(Control.PhoneUp)) Vertical(-1);
            else if (Game.IsControlJustPressed(Control.PhoneDown)) Vertical(1);

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

            // ---- taking some of it yourself ----
            //
            // SELECT, the same key that eats a burger one row down. Both are "put this in your
            // mouth", and giving the two of them different buttons because one is food and one
            // is not would be a distinction the hand has to remember and the head already knows.
            if (Game.IsControlJustPressed(Control.PhoneSelect))
            {
                Use();
                return;
            }

            // ---- PUTTING IT DOWN, ON A KEY OF ITS OWN ----
            //
            // Jump: space, or X on a pad. It had to move off left and right so the cursor could
            // have them, and this is the better home for it anyway -- a key that means "put
            // something down" should not also be the key that means "look at the next one".
            //
            // TAP FOR SOME, HOLD FOR ALL. It was a modifier before, Sprint held down alongside,
            // which is two hands for one idea and undiscoverable on a pad. Holding the same key
            // longer is the same escalation with nothing extra to learn.
            var putting = Game.IsControlPressed(Control.Jump);

            if (!putting)
            {
                _holdFrom = 0;
                return;
            }

            if (_holdFrom == 0) _holdFrom = Game.GameTime;

            if (Game.GameTime >= _nextRepeat)
            {
                Drop(Game.GameTime - _holdFrom >= HoldAllMs);
            }
        }

        /// <summary>
        /// Take one of whatever the cursor is on.
        ///
        /// BAGGED ONLY, and that is a rule about the product rather than about the menu. The
        /// weight rows are uncut bulk -- the thing that has not been cut, weighed or bagged
        /// yet, which is measured in hundreds of grams and is not a dose. Taking "some" of it
        /// is a question with no sensible answer, and the kitchen counter is where that stuff
        /// turns into things a person could actually take.
        ///
        /// ONE UNIT. A bar, a pill, a gram -- whatever the product counts itself in, which the
        /// drug already knows how to say.
        /// </summary>
        private void Use()
        {
            if (Highs == null || _selected < 0 || _selected >= _rows.Count) return;

            var row = _rows[_selected];

            if (!row.Bagged)
            {
                Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                Notify.Failure("that's raw. bag it up first.");
                return;
            }

            if (row.Held < UseUnit - 0.001f)
            {
                Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                return;
            }

            var no = Highs.Refusal(row.Drug.Id);

            if (no != null)
            {
                Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                Notify.Failure(no.ToLowerInvariant());
                return;
            }

            // TAKEN OFF YOU FIRST, and only then does it do anything. The stash says how much
            // it actually managed to remove, so a rounding error cannot leave you high on a
            // gram you still have.
            var got = _pockets.RemovePackaged(row.Drug.Id, UseUnit);

            if (got <= 0.001f)
            {
                Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                return;
            }

            Highs.Take(row.Drug.Id, row.Drug.Amount(got));

            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");

            // OUT OF THE MENU, the same as eating. Whatever it does to the picture, the walk
            // and the camera is the entire point of having done it, and none of it can be seen
            // from behind a panel.
            Close();
        }

        /// <summary>One of whatever it counts itself in.</summary>
        private const float UseUnit = 1f;

        /// <summary>Product rows first, then one place per food tile.</summary>
        private int Places => _rows.Count + _food.Count;

        private bool OnFood => _selected >= _rows.Count;

        /// <summary>When the drop key went down, so holding it can mean all of it.</summary>
        private int _holdFrom;

        /// <summary>How long it has to be held before a tap becomes the lot.</summary>
        private const int HoldAllMs = 600;

        /// <summary>
        /// Up and down, which between two strips of tiles means BETWEEN them.
        ///
        /// Within the product band it steps a whole line, so a wrapped inventory walks in the
        /// shape it is drawn in. Off the end of it, it crosses into the food -- keeping the
        /// column, so going down from the third thing you are carrying lands on the third thing
        /// you can eat rather than on the first.
        /// </summary>
        private void Vertical(int dir)
        {
            if (Places == 0) return;

            var across = Across();

            if (!OnFood)
            {
                var next = _selected + dir * across;

                if (next >= 0 && next < _rows.Count) { Land(next); return; }

                // Off the bottom, into the food if there is any.
                if (dir > 0 && _food.Count > 0)
                {
                    Land(_rows.Count + Math.Min(_food.Count - 1, _selected % across));
                }

                return;
            }

            var col = _selected - _rows.Count;

            var here = col + dir * across;

            if (here >= 0 && here < _food.Count) { Land(_rows.Count + here); return; }

            // Off the top of the food, back into the last line of the product.
            if (dir < 0 && _rows.Count > 0)
            {
                var lastLine = (_rows.Count - 1) / across * across;

                Land(Math.Min(_rows.Count - 1, lastLine + Math.Min(col, across - 1)));
            }
        }

        /// <summary>Puts the cursor somewhere and makes the noise. Move does the wrapping.</summary>
        private void Land(int where)
        {
            if (where == _selected) return;

            _lastSelected = _selected;
            _selected = where;
            _pickedAt = Game.GameTime;

            Hud.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        private void Move(int step)
        {
            if (Places == 0) return;

            _lastSelected = _selected;
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

        /// <summary>Where the tiles start: the letterhead, then the meter under it.</summary>
        private const float ContentTop = UiKit.HeadH + 0.044f;

        /// <summary>The card under the tiles that says what the chosen one is.</summary>
        private const float CardH = 0.072f;

        /// <summary>The meter's needle, easing toward how full you are. See UI.Eased.</summary>
        private readonly Eased _meter = new Eased();

        /// <summary>
        /// The figure on the card, easing toward the true amount so putting ten grams down
        /// rolls the number rather than swapping it.
        ///
        /// Reset whenever the cursor lands on a different tile. Left alone it would slide
        /// from the last tile's figure to this one's -- "57g" turning into "12 bars" -- which
        /// is a morph between two unrelated things and reads as a fault.
        /// </summary>
        private readonly Eased _amount = new Eased();
        private string _amountFor = "";

        public void Draw()
        {
            if (!IsOpen) return;

            // HOW MANY ROWS OF TILES, not how many items. Worked out here because the panel
            // has to be tall enough before anything is drawn into it, and it is the same sum
            // the band itself does -- see Across.
            var across = Across();
            var lines = _rows.Count == 0 ? 1 : (_rows.Count + across - 1) / across;

            var tiles = _rows.Count == 0
                ? RowHeight + 0.008f
                : lines * (Cell + CellGap) - CellGap + 0.010f;

            var height = ContentTop + tiles + (_rows.Count == 0 ? 0f : CardH) + UiKit.FootH;

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

            Theme.Panel(left, top, panelWidth, height, arrive);

            var x = left + pad;
            var right = left + panelWidth - pad;
            var wide = right - x;

            // ---- the letterhead, and the meter under it ----
            var y = UiKit.Head(left, top, panelWidth, pad, "stash.png", "INVENTORY",
                             "what's on you", _rows.Count + (_rows.Count == 1 ? " LINE" : " LINES"),
                             arrive);

            var full = Full();

            UiKit.Meter(x, y + 0.004f, wide, "people.png", "ON YOU", UiKit.Holding(_pockets),
                      _meter.To(full), full, arrive);

            y = top + ContentTop;

            // Nothing wants the cursor until a tile asks for it this frame.
            _glide.Begin();

            if (_rows.Count == 0)
            {
                // AN EMPTY BAG BESIDE THE SENTENCE, not above it.
                var ex = x;

                if (Hud.File("baggie.png", x + Hud.ToX(ArtSize) * 0.5f, y + RowHeight * 0.34f,
                             ArtSize, 0f, Palette.Alpha(Palette.TextDim, 120)))
                {
                    ex = x + Hud.ToX(ArtSize) + 0.007f;
                }

                Hud.Text("Nothing on you.", ex, y + 0.006f, 0.28f,
                         Palette.TextDim, Hud.FontBody, centre: false);

                y += RowHeight + 0.008f;
            }
            else
            {
                y = ProductBand(x, wide, y, arrive);
                y = Card(x, wide, y, arrive);
            }

            if (_food.Count > 0) FoodBand(x, wide, y, arrive);

            // ---- the keys ----
            var footY = top + height - UiKit.FootH + 0.006f;

            Theme.Rule(x, footY, wide, arrive);

            Keys(x, right, footY + 0.011f, arrive);

            // Last, so it rides over the tiles it is pointing at.
            _glide.Draw(arrive);
        }

        /// <summary>
        /// What the buttons do, as key caps, with the way out in the same corner as every
        /// other screen. Only the keys that would do something on the tile you are on: a
        /// weight line cannot be taken, so TAKE ONE is not offered on it.
        /// </summary>
        private void Keys(float x, float right, float y, float arrive)
        {
            UiKit.KeyRight(right, y, UiKit.Back, "DONE", arrive);

            if (Places == 0) return;

            var kx = UiKit.Key(x, y, null, "arrow_leftright.png", "PICK", arrive);

            if (OnFood)
            {
                UiKit.Key(kx, y, UiKit.Confirm, null, "EAT IT", arrive);
                return;
            }

            // THE FLOOR IS THE ONE THING ABOUT THIS SCREEN NOBODY WOULD GUESS. The drop mark
            // -- an arrow onto a line -- rides the cap, so the key that puts things on the
            // pavement is the one key on the row with a picture of doing that.
            kx = UiKit.Key(kx, y, UiKit.Drop, null, "PUT IT DOWN", arrive);
            kx = UiKit.Key(kx, y, "HOLD", null, "ALL OF IT", arrive);

            if (_selected >= 0 && _selected < _rows.Count && _rows[_selected].Bagged)
            {
                UiKit.Key(kx, y, UiKit.Confirm, null, "TAKE ONE", arrive);
            }
        }

        /// <summary>
        /// The card under the tiles: the chosen thing, said properly.
        ///
        /// A tile is a glance -- a picture, a figure, a dot for how cut it is. This is where
        /// those become words: the name in full, whether it is bagged or raw weight, the
        /// amount rolling as you put it down, and how stepped on it is with the percentage
        /// beside it. It slides in from the left as the plate comes up under the new tile,
        /// so the word arrives with the cursor rather than swapping under it.
        ///
        /// On the food it steps back to a one-line summary, because the food band has its own
        /// caption and two captions for one cursor is one too many.
        /// </summary>
        private float Card(float x, float wide, float y, float arrive)
        {
            var cardH = CardH - 0.010f;
            var grown = Theme.Grown(_pickedAt);

            if (OnFood || _selected < 0 || _selected >= _rows.Count)
            {
                Hud.Text(_rows.Count == 1 ? "1 line on you" : _rows.Count + " lines on you",
                         x, y + 0.004f, 0.27f, Palette.Alpha(Palette.TextDim, (int)(190f * arrive)),
                         Hud.FontBody, centre: false);

                return y + CardH;
            }

            var row = _rows[_selected];

            // The figure eases toward the truth, and starts over on a new tile.
            var key = row.Drug.Id + (row.Bagged ? "|b" : "|w");

            if (_amountFor != key)
            {
                _amountFor = key;
                _amount.Reset();
            }

            var held = _amount.To(row.Held, 9f);

            var slide = Hud.ToX(0.012f) * (1f - grown);
            var show = arrive * (0.6f + 0.4f * grown);

            // The card's ground: a shade up from the panel with the brand rail down its left,
            // which is the plate every chosen thing in the mod sits on. Two rectangles.
            Theme.Plate(x, y, wide, cardH, 0.55f * show);

            // ---- the picture, big ----
            var art = Icons.ForDrug(row.Drug.Id);
            var ax = x + Hud.ToX(0.014f) + slide;
            var tx = x + 0.012f + slide;

            if (art.HasFile &&
                Hud.File(art.File, ax + Hud.ToX(BigArt) * 0.5f, y + cardH * 0.5f, BigArt, 0f,
                         Palette.Alpha(Palette.Text, (int)(240f * show))))
            {
                tx = ax + Hud.ToX(BigArt) + 0.010f;
            }

            // ---- the name, and what form it is in ----
            var nameY = y + 0.008f;

            Hud.Text(row.Drug.Name, tx, nameY, 0.34f, Palette.Alpha(Palette.Text, (int)(255f * show)),
                     Hud.FontBody, centre: false);

            var afterName = tx + Hud.MeasureText(row.Drug.Name, 0.34f, Hud.FontBody) + 0.009f;

            UiKit.Tag(afterName, nameY + 0.005f, row.Bagged ? "BAGGED" : "WEIGHT",
                    row.Bagged ? Palette.Brand : Palette.TextDim, show);

            // ---- how much, and how cut ----
            var lineY = y + 0.037f;

            var amount = row.Bagged ? row.Drug.Amount(held) : row.Drug.Bulk(held);

            Hud.Text(amount, tx, lineY, 0.30f, Palette.Alpha(Palette.Cash, (int)(255f * show)),
                     Hud.FontBody, centre: false);

            var afterAmount = tx + Hud.MeasureText(amount, 0.30f, Hud.FontBody) + 0.012f;

            if (row.Purity > 0f)
            {
                var word = UiKit.PurityWord(row.Purity);

                Hud.Text(word, afterAmount, lineY + 0.001f, 0.27f,
                         Palette.Alpha(UiKit.PurityInk(row.Purity), (int)(220f * show)),
                         Hud.FontBody, centre: false);

                // The percentage, large, with its mark, on the right of the card: the one
                // figure about a bag that decides what it sells for.
                var pct = Stash.Percent(row.Purity) + "%";
                var pctW = Hud.MeasureText(pct, 0.42f, Hud.FontLabel);
                var markW = Hud.ToX(0.016f);

                Hud.TextRight(pct, x + wide - 0.010f, y + 0.014f, 0.42f,
                              Palette.Alpha(Palette.Text, (int)(245f * show)), Hud.FontLabel);

                Hud.File(Stash.Mark(row.Purity), x + wide - 0.010f - pctW - 0.006f - markW * 0.5f,
                         y + 0.031f, 0.016f, 0f, Palette.Alpha(Palette.Text, (int)(230f * show)));
            }

            return y + CardH;
        }

        private const float BigArt = 0.040f;

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
            Theme.Rule(x, y, width, arrive);

            y += 0.012f;

            // ---- their mark, then the heading ----
            var tx = x;

            var mark = Core.Larder.Mark("food");

            if (!string.IsNullOrEmpty(mark) &&
                Hud.File(mark, x + Hud.ToX(0.014f) * 0.5f, y + 0.007f, 0.014f, 0f,
                         Palette.Alpha(Palette.Text, (int)(230f * arrive))))
            {
                tx = x + Hud.ToX(0.014f) + 0.005f;
            }

            Hud.Text("FOOD", tx, y, 0.28f, Palette.Text, Hud.FontLabel, centre: false);

            // Carried out of capacity, coloured once it is full: "4 / 3" in the same grey as
            // "1 / 3" reads as a broken number rather than as a full bag.
            var carried = Core.Larder.Total;
            var slots = Core.Larder.Slots;

            var full = slots > 0 && carried >= slots;

            Hud.TextRight(carried + " / " + slots, x + width, y, 0.24f,
                          full ? Palette.Warn : Palette.TextDim, Hud.FontLabel);

            var tileY = y + FoodHead;

            var tile = Hud.ToX(FoodTile);
            var gap = Hud.ToX(0.006f);

            var age = Game.GameTime - _shownAt;
            var grown = Theme.Grown(_pickedAt);

            for (var i = 0; i < _food.Count; i++)
            {
                var id = _food[i];

                // Staggered a frame or two apart, so the pocket is unpacked rather than
                // switched on.
                var land = UiKit.Landed(age, i * 60, EnterMs);

                var show = arrive * land;
                if (show <= 0.01f) continue;

                var tileTop = tileY + EnterRise * 0.5f * (1f - land);

                var tx2 = x + i * (tile + gap);

                var at = _rows.Count + i;
                var picked = _selected == at;
                var lit = Theme.Lit(at, _selected, _lastSelected, grown);

                Tile(tx2, tileTop, tile, FoodTile, lit, show, false);

                if (picked) _glide.Target(tx2, tileTop, tile, FoodTile);

                var art = Core.Larder.IconOf(id);

                if (!string.IsNullOrEmpty(art))
                {
                    var swell = picked ? 1f + PickGrow * grown : 1f;

                    // The item's own colour on the dark tile, brightening as the plate comes
                    // up. The art is white and the sprite multiplies, so one file does all of it.
                    var ink = Theme.Ink(Palette.Alpha(Core.Larder.TintOf(id), (int)(238f * show)), lit);

                    Hud.File(art, tx2 + tile * 0.5f, tileTop + FoodTile * 0.46f,
                             FoodTile * 0.58f * swell, 0f, ink);
                }

                // ---- how many ----
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
            var capY = tileY + FoodTile + 0.005f;

            if (OnFood)
            {
                var at = _selected - _rows.Count;

                if (at >= 0 && at < _food.Count)
                {
                    Theme.Caption(Core.Larder.NameOf(_food[at]), x, capY, grown);
                    return;
                }
            }

            Hud.Text(_food.Count == 1 ? "1 thing to eat" : _food.Count + " things to eat",
                     x, capY, 0.26f, Palette.Alpha(Palette.TextDim, 190),
                     Hud.FontBody, centre: false);
        }

        /// <summary>
        /// One square: the dark ground with a hairline along its top, the plate coming up
        /// under the cursor and going down where the cursor left, the light crossing the lit
        /// one, and a flash of the brand green over a tile that just changed. Up to five
        /// rectangles, and the same five on the food and on the product.
        /// </summary>
        private static void Tile(float x, float y, float w, float h, float lit, float show, bool flash,
                                 float flashLeft = 0f)
        {
            Hud.RectFrom(x, y, w, h, Color.FromArgb((int)(34f * show), 255, 255, 255));
            Hud.RectFrom(x, y, w, 0.0012f, Color.FromArgb((int)(64f * (1f - lit) * show), 255, 255, 255));

            Theme.Plate(x, y, w, h, lit * show);

            if (flash)
            {
                Hud.RectFrom(x, y, w, h, Palette.Alpha(Palette.Brand, (int)(190f * flashLeft * show)));
            }

            Theme.Sheen(x, y, w, h, lit * show);
        }

        /// <summary>
        /// How many tiles fit across the panel.
        ///
        /// Asked rather than fixed, because the panel width is a constant but the aspect ratio
        /// is not -- Hud.ToX turns a height fraction into a width one, and on an ultrawide that
        /// answer is different. A hardcoded four would wrap on some screens and leave a gap on
        /// others.
        /// </summary>
        private static int Across()
        {
            var wide = Hud.ToX(PanelWidthH) - Hud.ToX(PadH) * 2f;
            var step = Hud.ToX(Cell) + Hud.ToX(CellGap);

            var many = step <= 0f ? 4 : (int)((wide + Hud.ToX(CellGap)) / step);

            if (many < 1) many = 1;
            if (many > 8) many = 8;

            return many;
        }

        /// <summary>
        /// What is on you, as squares. Returns the y the next thing may start at.
        ///
        /// The picture carries the drug, the chip along the bottom carries the amount, the
        /// corner carries how cut it is. A tile is a glance; the card under the strip is for
        /// reading.
        /// </summary>
        private float ProductBand(float x, float width, float y, float arrive)
        {
            var tile = Hud.ToX(Cell);
            var gap = Hud.ToX(CellGap);

            var across = Across();

            var age = Game.GameTime - _shownAt;
            var grown = Theme.Grown(_pickedAt);

            var lines = (_rows.Count + across - 1) / across;

            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];

                var land = UiKit.Landed(age, i * 55, EnterMs);

                var show = arrive * land;
                if (show <= 0.01f) continue;

                var col = i % across;
                var line = i / across;

                var tx = x + col * (tile + gap);
                var ty = y + line * (Cell + CellGap) + EnterRise * 0.5f * (1f - land);

                var picked = i == _selected;
                var lit = Theme.Lit(i, _selected, _lastSelected, grown);

                // The tile that just had something taken off it flashes green and fades: a
                // whole square lighting up, over whatever else it is doing.
                var flashLeft = _droppedRow == i ? UiKit.Flash(_droppedAt, DropFlashMs) : 0f;
                var flashing = flashLeft > 0f;

                Tile(tx, ty, tile, Cell, lit, show, flashing, flashLeft);

                if (picked) _glide.Target(tx, ty, tile, Cell);

                // How bright the ink on this tile should be: fully over a plate or a flash.
                var bright = flashing ? 1f : lit;

                // ---- the picture ----
                var art = Icons.ForDrug(row.Drug.Id);

                if (art.HasFile)
                {
                    var swell = picked ? 1f + PickGrow * grown : 1f;

                    Hud.File(art.File, tx + tile * 0.5f, ty + Cell * 0.40f, Cell * 0.52f * swell, 0f,
                             Theme.Ink(Palette.Alpha(Palette.Text, (int)(215f * show)), bright));
                }

                // ---- how much ----
                //
                // Across the whole tile rather than in a corner chip, because these are not
                // counts. "57.5g" and "87 bars" are four and seven characters, and a corner
                // badge sized for a single digit turns both into a smudge.
                Hud.RectFrom(tx, ty + Cell - ChipHeight, tile, ChipHeight,
                             Color.FromArgb((int)((205f - 60f * bright) * show), 12, 13, 15));

                var amount = row.Bagged ? row.Drug.Amount(row.Held) : row.Drug.Bulk(row.Held);

                Hud.Text(Hud.Fit(amount, tile - 0.004f, 0.22f, Hud.FontLabel),
                         tx + tile * 0.5f, ty + Cell - ChipHeight - 0.0005f, 0.22f,
                         Palette.Alpha(picked ? Palette.Text : Palette.TextDim, (int)(255f * show)),
                         Hud.FontLabel);

                // ---- how cut ----
                if (row.Purity > 0f)
                {
                    Hud.File(Stash.Mark(row.Purity), tx + tile - Hud.ToX(0.008f), ty + 0.008f, 0.010f, 0f,
                             Theme.Ink(Palette.Alpha(Palette.TextDim, (int)(210f * show)), bright));
                }
            }

            return y + lines * (Cell + CellGap) - CellGap + 0.010f;
        }
    }
}
