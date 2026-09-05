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

        private const float BarHeight = 0.0075f;
        private const float HeadIcon = 0.016f;


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

        /// <summary>Eased fill, so putting something down slides the bar rather than jumping it.</summary>
        private float _fill;

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

        public void Draw()
        {
            if (!IsOpen) return;

            // HOW MANY ROWS OF TILES, not how many items. Worked out here because the panel
            // has to be tall enough before anything is drawn into it, and it is the same sum
            // the band itself does -- see Across.
            var lines = _rows.Count == 0 ? 1 : (_rows.Count + Across() - 1) / Across();

            var height = HeadHeight + FootHeight +
                         (_rows.Count == 0
                              ? RowHeight
                              : lines * (Cell + CellGap) - CellGap + CellCap + 0.006f);

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

            // THE BLACK, ROUNDED, AND NOTHING ROUND IT. This panel used to wear a hairline
            // frame with corner ticks, a stripe along its top edge, a band of light crossing
            // that stripe and a second light walking the whole perimeter -- four things
            // happening at the border of a screen whose content is the point. All of it has
            // gone. An accent with no alpha is Panel's way of being told there is no stripe.
            Theme.Panel(left, top, panelWidth, height, arrive);

            var x = left + pad;
            var right = left + panelWidth - pad;
            var wide = right - x;
            var middle = left + panelWidth * 0.5f;

            Hud.BrandCentre(middle, top + 0.024f, 0.022f, Palette.Alpha(Palette.Text, (int)(230f * arrive)));

            Hud.Text(Blurb, middle, top + 0.052f, 0.29f,
                     Palette.Alpha(Palette.TextDim, 170), Hud.FontChaletLondon);

            Theme.Rule(x, top + 0.078f, wide, arrive);

            var y = top + 0.088f;

            var ix = x;

            if (Hud.File("stash.png", x + Hud.ToX(HeadIcon) * 0.5f, y + 0.007f, HeadIcon, 0f,
                         Palette.Alpha(Palette.Text, 225)))
            {
                ix = x + Hud.ToX(HeadIcon) + 0.006f;
            }

            Hud.Text("INVENTORY", ix, y, 0.30f, Palette.Text, Hud.FontLabel, centre: false);

            Hud.TextRight(_rows.Count + (_rows.Count == 1 ? " LINE" : " LINES"),
                          right, y, 0.24f, Palette.TextDim, Hud.FontLabel);

            y += 0.028f;

            Carrying(x, wide, y);

            Theme.Rule(x, top + HeadHeight - 0.008f, wide, arrive);

            y = top + HeadHeight;

            // Nothing wants the cursor until a tile asks for it this frame.
            _glide.Begin();

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
            else
            {
                y = ProductBand(x, wide, y, arrive);
            }

            // "Nothing on you." occupies a row nothing above walked, and the panel was already
            // sized for it -- so the cursor has to step over it too, or the food band starts
            // on top of the sentence.
            if (_rows.Count == 0) y += RowHeight;

            if (_food.Count > 0) FoodBand(x, wide, y + 0.008f, arrive);

            var footY = top + height - FootHeight + 0.008f;

            Theme.Rule(x, footY, wide, arrive);

            Keys(x, right, footY + 0.008f);

            // Last, so it rides over the tiles it is pointing at.
            _glide.Draw(arrive);
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

            var hx = Hud.Hint("arrow_leftright.png", "PICK", x, y, 0.24f, Palette.TextDim);

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
            hx = Hud.Hint("drop.png", (pad ? "X" : "SPACE") + "  PUT IT DOWN", hx, y, 0.24f,
                          Palette.TextDim);

            hx = Hud.Hint(null, "HOLD  ALL OF IT", hx, y, 0.24f, Palette.TextDim);

            // Only on the rows it works on. A bagged line can be taken; a weight line is uncut
            // bulk and has to go through the kitchen first, and offering it on a row that will
            // refuse is worse than not offering it.
            if (_selected >= 0 && _selected < _rows.Count && _rows[_selected].Bagged)
            {
                Hud.Hint("pills.png", (pad ? "A" : "ENTER") + "  TAKE ONE", hx, y, 0.24f,
                         Palette.TextDim);
            }
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
            Theme.Rule(x, y, width, arrive);

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
                         Palette.Alpha(Palette.Text, (int)(230f * arrive))))
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
                          full ? Palette.Text : Palette.TextDim, Hud.FontLabel);

            var tileY = y + FoodHead;

            var tile = Hud.ToX(FoodTile);
            var gap = Hud.ToX(0.006f);

            // ---- how the tiles arrive ----
            //
            // STAGGERED, a frame or two apart. All of them fading up together is the panel
            // appearing twice; one after another reads as the pocket being unpacked, and it is
            // the same trick the shop shelf uses on its row pictures.
            var age = Game.GameTime - _shownAt;

            var grown = Theme.Grown(_pickedAt);

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

                var at = _rows.Count + i;
                var picked = _selected == at;
                var lit = Theme.Lit(at, _selected, _lastSelected, grown);

                // The dark tile with its hairline, always; the warm plate comes up over it as
                // the cursor arrives and goes back down where the cursor just was. Two things
                // fading rather than one thing switching, so the eye is led rather than told.
                Hud.RectFrom(tx2, tileTop, tile, FoodTile,
                             Color.FromArgb((int)(38f * show), 255, 255, 255));
                Hud.RectFrom(tx2, tileTop, tile, 0.0012f,
                             Color.FromArgb((int)(70f * (1f - lit) * show), 255, 255, 255));

                Theme.Plate(tx2, tileTop, tile, FoodTile, lit * show);

                Theme.Sheen(tx2, tileTop, tile, FoodTile, lit * show);

                if (picked) _glide.Target(tx2, tileTop, tile, FoodTile);

                var art = Core.Larder.IconOf(id);

                if (!string.IsNullOrEmpty(art))
                {
                    // THE ONE UNDER THE CURSOR GROWS INTO IT. A picture that swells over a
                    // sixth of a second is movement, and movement is what the eye catches.
                    var swell = picked ? 1f + PickGrow * grown : 1f;

                    // The item's own colour on the dark tile, near-black once the plate is
                    // under it, and every shade between while the plate is on its way. The art
                    // is white and CustomSprite multiplies, so one file does all of it.
                    var ink = Theme.Lerp(Palette.Alpha(Core.Larder.TintOf(id), (int)(238f * show)),
                                   Color.FromArgb((int)(255f * show), 20, 18, 14), lit);

                    Hud.File(art, tx2 + tile * 0.5f, tileTop + FoodTile * 0.46f,
                             FoodTile * 0.58f * swell, 0f, ink);
                }

                // ---- how many ----
                //
                // On its own dark chip rather than straight onto the tile. The number has to
                // read over the plate and over a dark square, and one ink cannot do both.
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
                    Theme.Caption(Core.Larder.NameOf(_food[at]), x, capY, grown);
                    return;
                }
            }

            Hud.Text(_food.Count == 1 ? "1 thing to eat" : _food.Count + " things to eat",
                     x, capY, 0.26f, Palette.Alpha(Palette.TextDim, 190),
                     Hud.FontBody, centre: false);
        }

        /// <summary>
        /// What you are holding out of what you can, with a bar of it.
        ///
        /// Gold while there is room, ember once it is getting full, red when it is. Yellow
        /// into orange into red is the one order of those three that reads as filling up.
        /// </summary>
        private void Carrying(float x, float width, float y)
        {
            var tx = x;

            if (Hud.File("people.png", x + Hud.ToX(HeadIcon) * 0.5f, y + 0.008f, HeadIcon, 0f,
                         Palette.Alpha(Palette.Text, 225)))
            {
                tx = x + Hud.ToX(HeadIcon) + 0.006f;
            }

            Hud.Text("ON YOU", tx, y, 0.28f, Palette.Text, Hud.FontLabel, centre: false);

            var full = Full();
            var tint = full > 0.9f ? Palette.Danger : full > 0.7f ? Palette.Text : Palette.Text;

            Hud.TextRight(_pockets.Total.ToString("0") + " / " + _pockets.Capacity.ToString("0") + "g",
                          x + width, y, 0.28f, full > 0.7f ? tint : Palette.TextDim, Hud.FontBody);

            _fill += (full - _fill) * 0.18f;
            if (Math.Abs(full - _fill) < 0.002f) _fill = full;

            var barY = y + 0.024f;

            Hud.RectFrom(x, barY, width, BarHeight, Color.FromArgb(150, 26, 28, 30));

            if (_fill > 0f) Hud.RectFrom(x, barY, width * _fill, BarHeight, tint);

            Hud.RectFrom(x, barY + BarHeight, width, 0.0010f, Palette.Alpha(tint, 90));
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
        /// THE SAME BAND AS THE FOOD, ON PURPOSE. See Cell -- the two sit one above the other
        /// and the product being a list of lines while the food was a grid of squares made the
        /// panel read as two screens glued together. Now the only difference between the bands
        /// is what is written under them.
        ///
        /// The picture carries the drug, the chip along the bottom carries the amount, the
        /// corner carries how cut it is, and the caption under the strip carries the name of
        /// whichever one is under the cursor. A tile is a glance; the line underneath is for
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

                // Staggered a frame or two apart, same as the food -- all of them fading up
                // together is the panel appearing twice, one after another is a pocket being
                // unpacked.
                var lead = i * 55;

                var land = age <= lead ? 0f : (age - lead) / (float)EnterMs;
                if (land > 1f) land = 1f;
                land = 1f - (1f - land) * (1f - land);

                var show = arrive * land;
                if (show <= 0.01f) continue;

                var col = i % across;
                var line = i / across;

                var tx = x + col * (tile + gap);
                var ty = y + line * (Cell + CellGap) + EnterRise * 0.5f * (1f - land);

                var picked = i == _selected;
                var lit = Theme.Lit(i, _selected, _lastSelected, grown);

                var flashing = _droppedRow == i && Game.GameTime - _droppedAt < DropFlashMs;

                // The dark square with its hairline, always; the plate comes up over it under
                // the cursor and goes down where the cursor just left.
                Hud.RectFrom(tx, ty, tile, Cell, Color.FromArgb((int)(38f * show), 255, 255, 255));
                Hud.RectFrom(tx, ty, tile, 0.0012f,
                             Color.FromArgb((int)(70f * (1f - lit) * show), 255, 255, 255));

                Theme.Plate(tx, ty, tile, Cell, lit * show);

                // The tile that just had something taken off it flashes gold and fades, over
                // whatever else it is doing: a whole square lighting up, which used to be a
                // colour change on a number.
                if (flashing)
                {
                    var left = 1f - (Game.GameTime - _droppedAt) / (float)DropFlashMs;

                    Hud.RectFrom(tx, ty, tile, Cell,
                                 Palette.Alpha(Palette.Brand, (int)(200f * left * show)));
                }

                // How dark the ink on this tile should be: fully, over a plate or a flash.
                var dark = flashing ? 1f : lit;

                Theme.Sheen(tx, ty, tile, Cell, lit * show);

                if (picked) _glide.Target(tx, ty, tile, Cell);

                // ---- the picture ----
                var art = Icons.ForDrug(row.Drug.Id);

                if (art.HasFile)
                {
                    var swell = picked ? 1f + PickGrow * grown : 1f;

                    var ink = Theme.Lerp(Palette.Alpha(Palette.Text, (int)(225f * show)),
                                   Color.FromArgb((int)(255f * show), 20, 18, 14), dark);

                    Hud.File(art.File, tx + tile * 0.5f, ty + Cell * 0.40f,
                             Cell * 0.52f * swell, 0f, ink);
                }

                // ---- how much ----
                //
                // Across the whole tile rather than in a corner chip, because these are not
                // counts. "57.5g" and "87 bars" are four and seven characters, and a corner
                // badge sized for a single digit turns both into a smudge. Gold on the tile
                // under the cursor, so the number you are about to act on is the lit one.
                Hud.RectFrom(tx, ty + Cell - ChipHeight, tile, ChipHeight,
                             Color.FromArgb((int)((200f - 50f * dark) * show), 12, 13, 15));

                var amount = row.Bagged ? row.Drug.Amount(row.Held) : row.Drug.Bulk(row.Held);

                Hud.Text(Hud.Fit(amount, tile - 0.004f, 0.22f, Hud.FontLabel),
                         tx + tile * 0.5f, ty + Cell - ChipHeight - 0.0005f, 0.22f,
                         Theme.Lerp(Palette.Alpha(Palette.Text, (int)(255f * show)),
                              Palette.Alpha(Palette.Text, (int)(255f * show)), lit),
                         Hud.FontLabel);

                // ---- how cut ----
                if (row.Purity > 0f)
                {
                    Hud.File(Stash.Mark(row.Purity), tx + tile - Hud.ToX(0.008f),
                             ty + 0.008f, 0.010f, 0f,
                             Theme.Lerp(Palette.Alpha(Palette.TextDim, (int)(210f * show)),
                                  Color.FromArgb((int)(220f * show), 20, 18, 14), dark));
                }
            }

            var capY = y + lines * (Cell + CellGap) - CellGap + 0.005f;

            // ---- what it is ----
            //
            // One line under the strip, which is where the food band puts it and where the
            // eye is already going. The weight is on the tile; this is the word.
            if (!OnFood && _selected >= 0 && _selected < _rows.Count)
            {
                Theme.Caption(_rows[_selected].Label, x, capY, grown);
            }
            else
            {
                Hud.Text(_rows.Count == 1 ? "1 line on you" : _rows.Count + " lines on you",
                         x, capY, 0.28f, Palette.TextDim, Hud.FontBody, centre: false);
            }

            return capY + CellCap;
        }
    }
}
