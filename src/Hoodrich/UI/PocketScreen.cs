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

        /// <summary>In the bag rather than in his pockets. See PocketScreen.Shift.</summary>
        public bool InBag;

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
        /// The flat index of every place, split into the two panes it is drawn in.
        ///
        /// ONE CURSOR, TWO SQUARES. Everything else on this screen -- the card, the drop, the
        /// transfer -- works off one flat number, and it should keep working off one flat
        /// number; what the panes change is where a thing is DRAWN, not what it is. So the
        /// index stays flat and these two lists say which side each of them landed on.
        ///
        /// Food is on the left with the pockets, because food IS in your pockets: Bare Minimum
        /// has one of them and the bag lends it room rather than being a second one.
        /// </summary>
        private readonly List<int> _left = new List<int>();
        private readonly List<int> _right = new List<int>();

        /// <summary>
        /// What the bag has to eat, which is a different shelf from what his pockets have.
        ///
        /// FOOD USED TO HAVE NOWHERE TO GO. Bare Minimum owns one pocket and the bag simply
        /// lent it slots, so a sandwich showed on the left and stayed there for ever: the bag
        /// was extra room rather than a second container, and there was no "over there" to
        /// move it to. The bag has a shelf of its own now -- see Satchel.Food, which has been
        /// saved with everything else since the day it was written -- and these are the ids
        /// sitting on it. Core.Larder's Give and Take are how one crosses the seam without
        /// being eaten on the way.
        /// </summary>
        private readonly List<string> _bagFood = new List<string>();

        /// <summary>Set by Main: the bag's own food shelf, so it can be shown and moved.</summary>
        public Func<Dictionary<string, int>> BagFood;

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
        /// THE SIZE BARE MINIMUM DRAWS ONE AT, which is what this screen was asked to look
        /// like and is nearly twice what it was. 0.044 was sized when only the food used it and
        /// 0.056 was a compromise for a grid drawn twice over -- and a tile that small with a
        /// picture, an amount across its foot and a cut mark in its corner is three things
        /// competing inside a square the size of a stamp. The other mod's pocket has one
        /// picture per tile at 0.105 and reads at a glance, which is the whole argument.
        ///
        /// SIZED BY HEIGHT, and the width follows through the aspect, so it is square on
        /// screen and the panel is as wide as five of them and no wider.
        /// </summary>
        private const float FoodTile = 0.105f;

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

        /// <summary>
        /// The air between tiles.
        ///
        /// NEARLY NOTHING, BECAUSE THE OTHER MOD'S TILES TOUCH. A six-thousandth gap between
        /// twenty small squares is a grid with lines ruled through it; at this size the tile's
        /// own dark plate is the separation and the gap only has to stop two plates fusing
        /// into one block.
        /// </summary>
        private const float CellGap = 0.0018f;
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

        /// <summary>
        /// Set by Main: the bag, and taking it off from in here.
        ///
        /// THE PLACE YOU LOOK AT WHAT YOU ARE CARRYING IS THE PLACE YOU PUT IT DOWN. The B key
        /// works anywhere and always will, but a player who has just opened this screen to see
        /// how full he is should not have to close it, remember a key and do it in the street.
        /// Null until Main wires it, and every use is guarded, so the screen is still a screen
        /// on a build where the bag does not exist.
        /// </summary>
        public Func<bool> BagOn;
        public Action DropBag;

        /// <summary>Set by Main: how many of the bag's slots are spent, and how many there are.</summary>
        public Func<int> BagUsed;
        public Func<int> BagSlots;

        /// <summary>Set by Main: what is in the bag, so things can be moved into and out of it.</summary>
        public Func<Stash> BagStash;

        private Stash Bag
        {
            get
            {
                if (!Carrying || BagStash == null) return null;

                try { return BagStash(); }
                catch { return null; }
            }
        }

        /// <summary>The bag's food shelf, or null when there is no bag on him.</summary>
        private Dictionary<string, int> Shelf
        {
            get
            {
                if (!Carrying || BagFood == null) return null;

                try { return BagFood(); }
                catch { return null; }
            }
        }

        /// <summary>How many of one thing the bag is carrying: their count when they keep the shelf, ours otherwise.</summary>
        private int Held(string id)
        {
            if (!Carrying || string.IsNullOrEmpty(id)) return 0;

            if (Core.Larder.BagShelf) return Core.Larder.BagCountOf(id);

            var shelf = Shelf;
            if (shelf == null) return 0;

            return shelf.TryGetValue(id, out var many) ? many : 0;
        }

        /// <summary>
        /// One onto the bag's shelf, whichever shelf that is. See Larder.BagShelf.
        ///
        /// THEIRS IS ASKED, OURS IS WRITTEN. Their shelf can refuse -- it is twenty slots
        /// between two mods and it knows how many the product is in -- so the answer
        /// matters and every caller reads it.
        /// </summary>
        private bool BagPutOne(string id)
        {
            if (Core.Larder.BagShelf) return Core.Larder.BagGive(id);

            var shelf = Shelf;
            if (shelf == null) return false;

            shelf[id] = Held(id) + 1;
            return true;
        }

        /// <summary>One off the bag's shelf, whichever shelf that is. False, nothing moved, when there is none.</summary>
        private bool BagTakeOne(string id)
        {
            if (Core.Larder.BagShelf) return Core.Larder.BagTake(id);

            var shelf = Shelf;
            if (shelf == null || Held(id) <= 0) return false;

            var left = Held(id) - 1;

            if (left <= 0) shelf.Remove(id);
            else shelf[id] = left;

            return true;
        }

        /// <summary>Whether their pocket has a place free, which is where anything leaving the bag goes.</summary>
        private static bool PocketRoom => Core.Larder.Total < Core.Larder.Slots;

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

            // ON THE FIRST THING THERE IS, which with one grid is also which CONTAINER opens.
            // The cursor's side is the side that is drawn, so a zero that happens to be in the
            // bag opens the bag -- right -- and a zero that is in neither because there is
            // nothing at all opens the pocket and says it is empty. Asked of First rather than
            // assumed, so the two can never disagree.
            _selected = First();

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

            // And whatever is on the bag's own shelf, which is only ever a handful of ids --
            // the ones actually in there, not the whole catalogue the way the pocket lists it.
            _bagFood.Clear();

            // WHAT IS ON THE BAG'S SHELF, whichever shelf that is. This used to walk the
            // POCKET's ids and keep the ones the shelf also had, so a sandwich in the bag
            // with no sandwich in the pocket was in the bag and not on the screen.
            if (Carrying)
            {
                if (Core.Larder.BagShelf)
                {
                    foreach (var id in Core.Larder.BagIds()) _bagFood.Add(id);
                }
                else
                {
                    var shelf = Shelf;

                    if (shelf != null)
                    {
                        foreach (var kv in shelf)
                        {
                            if (kv.Value > 0) _bagFood.Add(kv.Key);
                        }
                    }
                }
            }

            if (_pockets == null || _catalogue == null) return;

            // ---- HIS POCKETS, THEN THE BAG, AND THEY ARE NOT THE SAME PLACE ----
            //
            // Two containers, one grid, and every tile says which of them it is in -- so the
            // screen is the whole of what you are carrying without hiding half of it behind a
            // toggle nobody would find. See Shift, which is how a thing gets from one to the
            // other, and Strap.Room, which is why they are separate at all.
            Fill(_pockets, false);
            Fill(Bag, true);

            Sides();
        }

        /// <summary>Sorts every place into the pane it belongs in. See _left.</summary>
        private void Sides()
        {
            _left.Clear();
            _right.Clear();

            for (var i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].InBag) _right.Add(i);
                else _left.Add(i);
            }

            // Food last in each pane, so a side reads product and then sandwiches rather than
            // interleaving the two.
            for (var i = 0; i < _food.Count; i++) _left.Add(_rows.Count + i);
            for (var i = 0; i < _bagFood.Count; i++) _right.Add(_rows.Count + _food.Count + i);
        }

        /// <summary>
        /// A pane's name and its count, with a rule under them carrying the pane's own colour.
        ///
        /// The live side is lit and the other dimmed, which is what says where the cursor is
        /// when it is sat on a tile you are not looking at. Taken from the boot screen, which
        /// has been telling two containers apart this way for as long as it has existed.
        /// </summary>
        /// <summary>
        /// The two containers as a pair of tabs, with the live one lit.
        ///
        /// SPLIT DOWN THE MIDDLE AND BOTH ALWAYS DRAWN. A tab that disappeared when its side
        /// was empty would be a screen that changes shape as you use it, and worse, would hide
        /// the fact that there is anywhere else to put anything. "Bag's empty" is information;
        /// a missing tab is not.
        ///
        /// The count sits under the name rather than beside it, because at half the panel's
        /// old width there is no longer room for "IN THE BAG" and "5 / 20" on one line and a
        /// count that ellipsises is worse than no count.
        /// </summary>
        private void Tabs(float x, float y, float w, float arrive)
        {
            var half = w * 0.5f;

            Cap(x, y, half - 0.004f, "ON YOU", PocketFigure(), 0, arrive, Overstuffed);
            Cap(x + half + 0.004f, y, half - 0.004f, "IN THE BAG", BagFigure(), 1, arrive);
        }

        private void Cap(float x, float y, float w, string label, string figure, int side, float arrive,
                         bool warn = false)
        {
            var live = SideOf(_selected) == side;
            var tint = side == 1 ? Palette.Standing : Palette.Brand;

            Hud.Text(label, x, y + 0.003f, 0.26f,
                     Palette.Alpha(live ? tint : Palette.TextDim, (int)((live ? 245f : 170f) * arrive)),
                     Hud.FontLabel, centre: false);

            Hud.TextRight(figure, x + w, y + 0.004f, 0.24f,
                          Palette.Alpha(warn ? Palette.Warn : Palette.TextDim, (int)(215f * arrive)),
                          Hud.FontLabel);

            var ry = y + CapH - 0.005f;

            Hud.RectFrom(x, ry, w, 0.0012f, Palette.Alpha(Theme.Hairline, (int)(Theme.Hairline.A * arrive)));
            Hud.RectFrom(x, ry - 0.0004f, w * 0.14f, 0.0020f, Palette.Alpha(tint, (int)(215f * arrive)));
        }

        /// <summary>
        /// What his pockets are holding: grams of product, and how many things to eat.
        ///
        /// TWO CAPACITIES, BECAUSE THERE REALLY ARE TWO. Product in a jacket is weighed and
        /// food in a jacket is counted -- they are different mods' pockets sharing one man --
        /// and the heading showed only the grams. So a pane with eight sandwiches in it and no
        /// product at all read "0 / 400g", which is true and answers nothing anybody was
        /// looking at. The bag next door says 5 / 20 and means it; this now says what it is
        /// carrying with the same directness.
        /// </summary>
        private string PocketFigure()
        {
            var grams = UiKit.Holding(_pockets);

            var slots = Core.Larder.Slots;
            if (slots <= 0) return grams;

            return grams + "   " + Core.Larder.Total + " / " + slots;
        }

        /// <summary>
        /// More in his pockets than they hold, which is a real state and not a bug.
        ///
        /// THE CAP CAN DROP UNDER WHAT IS ALREADY IN THERE. The bag used to LEND the food
        /// pocket its twenty slots -- pockets of five became pockets of twenty-five while the
        /// bag was on -- and the bag has its own shelf now, so the lending was withdrawn. A
        /// player who had filled those lent slots keeps everything he was carrying and is over
        /// the line until he eats it or moves it across, which is the right outcome: the
        /// alternative is a mod that deletes a man's shopping to make a number tidy.
        ///
        /// It fixes itself and nothing can make it worse -- their pocket refuses anything new
        /// while it is full -- but it is invisible unless the heading says so, and an invisible
        /// over-capacity looks exactly like a broken slot count. See PocketFigure.
        /// </summary>
        private bool Overstuffed
        {
            get
            {
                var slots = Core.Larder.Slots;
                return slots > 0 && Core.Larder.Total > slots;
            }
        }

        /// <summary>What the bag is holding, for its heading. Empty when it is not on his back.</summary>
        private string BagFigure()
        {
            if (!Carrying) return "not on you";

            var used = BagUsed == null ? 0 : BagUsed();
            var slots = BagSlots == null ? 0 : BagSlots();

            return used + " / " + slots;
        }

        /// <summary>Which pane a place is drawn in: 0 on you, 1 in the bag.</summary>
        private int SideOf(int at)
        {
            return _right.Contains(at) ? 1 : 0;
        }

        private List<int> Pane(int side) => side == 1 ? _right : _left;

        /// <summary>Every lot in one container, appended to the grid.</summary>
        private void Fill(Stash from, bool inBag)
        {
            if (from == null) return;

            foreach (var drug in _catalogue.All)
            {
                if (drug == null) continue;

                var bagged = from.PackagedOf(drug.Id);
                if (bagged > 0.005f)
                {
                    _rows.Add(new PocketRow
                    {
                        Drug = drug,
                        Bagged = true,
                        Held = bagged,
                        InBag = inBag,
                        Purity = from.PurityOf(drug.Id)
                    });
                }

                var weight = from.BulkOf(drug.Id);
                if (weight > 0.005f)
                {
                    _rows.Add(new PocketRow
                    {
                        Drug = drug,
                        Bagged = false,
                        Held = weight,
                        InBag = inBag,
                        Purity = from.BulkPurityOf(drug.Id)
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
                var id = FoodAt(_selected);
                if (string.IsNullOrEmpty(id)) return;

                // E ACROSS THE SEAM, the same key that carries product across it. A food tile
                // used to swallow every key but SELECT, which is why the bag key printed in the
                // legend while standing on a sandwich did nothing at all.
                if (Swapping() && Carrying)
                {
                    ShiftFood(Game.IsControlPressed(Control.Sprint));
                    return;
                }

                if (DroppingBag() && Carrying)
                {
                    try { if (DropBag != null) DropBag(); }
                    catch { /* the key in the street still works */ }

                    Close();
                    return;
                }

                if (!Game.IsControlJustPressed(Control.PhoneSelect)) return;

                // OUT OF THE BAG FIRST, because eating is Bare Minimum's and Bare Minimum only
                // knows about a pocket. A meal out of the bag is therefore a transfer and then
                // a meal -- and if the pocket will not take it, nothing is eaten and nothing is
                // lost, rather than a burger disappearing on the way to his mouth.
                if (FoodInBag(_selected))
                {
                    if (Held(id) <= 0) return;

                    // POCKET ROOM FIRST. Their Give falls through to the bag when the pocket
                    // is full -- see their Stow -- which would take the sandwich off the
                    // shelf and put it straight back with a "moved", and then eat it out of
                    // a pocket it is not in.
                    if (!PocketRoom || !BagTakeOne(id))
                    {
                        Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                        Notify.Problem("no room in your pockets.");
                        return;
                    }

                    if (!Core.Larder.Give(id))
                    {
                        BagPutOne(id);

                        Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                        Notify.Problem("no room in your pockets.");
                        return;
                    }
                }

                // QUEUED, NOT EATEN HERE. The phone is an animation as well as a screen, and
                // starting a meal underneath it is two clips claiming one player. Larder waits
                // for the handset to be down and then does it -- see EatWhenPhoneIsAway.
                Core.Larder.EatWhenPhoneIsAway(id);

                Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");

                Close();
                return;
            }

            // ---- the bag off the shoulder ----
            //
            // COVER, WHICH IS B, which is the same key that drops it in the street -- one idea,
            // one key, whether or not a screen happens to be open. It is checked before
            // anything else on this screen because it is the only action here that is not about
            // the row under the cursor, and a player pressing it means it whatever is selected.
            if (DroppingBag() && Carrying)
            {
                try { if (DropBag != null) DropBag(); }
                catch { /* the key in the street still works */ }

                Close();
                return;
            }

            // ---- between the jacket and the bag ----
            //
            // CONTEXT, WHICH IS E, and is the same key that picks the bag up off the pavement.
            // One idea -- this thing and that bag -- on one key wherever you are standing.
            if (Swapping() && Carrying && !OnFood)
            {
                Shift(Game.IsControlPressed(Control.Sprint));
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

        /// <summary>
        /// Moves the chosen lot the other way: jacket to bag, or bag to jacket.
        ///
        /// NOTHING MOVES ON ITS OWN AND THAT IS THE WHOLE POINT. The bag used to simply make
        /// the pockets bigger, so everything bought went into the jacket and the bag was a
        /// number that never changed -- see Strap.Room. They are two containers now, and this
        /// is the only thing in the mod that carries anything between them.
        ///
        /// A HUNDRED GRAMS A PRESS, OR THE LOT ON A HOLD. The same escalation the drop key
        /// uses, on the same idea: a tap is a decision about some of it, holding is a decision
        /// about all of it, and nobody has to learn a second gesture.
        ///
        /// TAKEN FROM THE FAR SIDE FIRST. Add to the destination, see what it actually
        /// accepted, and only then remove that much from the source -- so a bag with room for
        /// sixty grams takes sixty and the other forty stay where they were, rather than
        /// vanishing into a container that was never going to hold them.
        /// </summary>
        private void Shift(bool everything)
        {
            var bag = Bag;

            if (bag == null || _pockets == null) return;
            if (_selected < 0 || _selected >= _rows.Count) return;

            var row = _rows[_selected];

            var from = row.InBag ? bag : _pockets;
            var to = row.InBag ? _pockets : bag;

            var want = everything ? row.Held : Math.Min(ShiftGrams, row.Held);
            if (want <= 0.005f) return;

            var moved = row.Bagged
                ? Carry(from, to, row.Drug.Id, want, true)
                : Carry(from, to, row.Drug.Id, want, false);

            if (moved <= 0.005f)
            {
                Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                Notify.Problem(row.InBag ? "no room in your pockets." : "the bag's full.");
                return;
            }

            _nextRepeat = Game.GameTime + RepeatMs;
            _droppedAt = Game.GameTime;
            _droppedRow = _selected;

            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");

            Rebuild();
        }

        /// <summary>
        /// The same key, on food: this shelf to that one.
        ///
        /// ONE AT A TIME, OR THE LOT ON A HOLD, which is what the product row does and there is
        /// no reason a burger should be a different gesture. Food is counted rather than
        /// weighed, so the unit is an item instead of a hundred grams.
        ///
        /// TAKEN FROM THE FAR SIDE FIRST, again for the same reason -- add to the destination,
        /// see what it took, and only then remove that much. A pocket that would not accept the
        /// fourth burger leaves the fourth burger in the bag rather than eating it in transit.
        /// </summary>
        private void ShiftFood(bool everything)
        {
            var id = FoodAt(_selected);

            if (string.IsNullOrEmpty(id)) return;
            if (!Core.Larder.BagShelf && Shelf == null) return;

            var toBag = !FoodInBag(_selected);

            var have = toBag ? Core.Larder.CountOf(id) : Held(id);
            if (have <= 0) return;

            var want = everything ? have : 1;
            var moved = 0;

            for (var i = 0; i < want; i++)
            {
                if (toBag)
                {
                    // Into the bag: is there a slot, does their pocket actually give it up.
                    // The slot count is asked of the bag rather than worked out here, because
                    // product and food spend the same twenty between them -- see Satchel.Used.
                    var slots = BagSlots == null ? 0 : BagSlots();
                    var used = BagUsed == null ? 0 : BagUsed();

                    if (used >= slots) break;

                    if (!Core.Larder.Take(id)) break;

                    if (!BagPutOne(id))
                    {
                        // Their shelf would not take it: back in the pocket it came out of.
                        Core.Larder.Give(id);
                        break;
                    }
                }
                else
                {
                    if (Held(id) <= 0) break;

                    // POCKET ROOM FIRST, for the reason the eating path gives: their Give
                    // overflows into the bag, and a sandwich moved from the bag to the bag
                    // is a sandwich that has not moved.
                    if (!PocketRoom) break;
                    if (!BagTakeOne(id)) break;

                    if (!Core.Larder.Give(id))
                    {
                        BagPutOne(id);
                        break;
                    }
                }

                moved++;
            }

            if (moved == 0)
            {
                Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                Notify.Problem(toBag ? "the bag's full." : "no room in your pockets.");
                return;
            }

            _nextRepeat = Game.GameTime + RepeatMs;
            _droppedAt = Game.GameTime;
            _droppedRow = _selected;

            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");

            Rebuild();
        }

        /// <summary>
        /// Y on a pad, E on a keyboard, and BOTH are watched whatever the mod thinks you are on.
        ///
        /// THE SAME LESSON THE BAG LEARNED IN THE STREET. OnPad is a guess about the last thing
        /// you touched, and a control that is only watched when the guess agrees is a control
        /// that silently does nothing for anybody who nudged a stick an hour ago. Nobody
        /// pressing either of these, on this screen, with the cursor on a thing, wanted
        /// something else to happen. See Strap.Holding, which is the same three lines for the
        /// same reason.
        ///
        /// AND THE DISABLED READ AS WELL. The phone is up and the guard has hold of the pad;
        /// a control the guard has switched off answers no to the ordinary question forever.
        ///
        /// The keyboard E is INPUT_CONTEXT itself and needs no line of its own -- and could not
        /// have one, since this build of ScriptHookVDotNet has IsKeyPressed but no edge-read to
        /// go with it, and a held key on a menu key is a row of transfers.
        /// </summary>
        private static bool Swapping()
        {
            try
            {
                if (Game.IsControlJustPressed(Control.FrontendY)) return true;

                if (Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0,
                                        (int)Control.FrontendY))
                {
                    return true;
                }

                if (!Game.IsControlJustPressed(Control.Context)) return false;

                // ---- AND NOT WHEN THE D-PAD SAID IT ----
                //
                // INPUT_CONTEXT IS D-PAD RIGHT. Not a button near it, the same one -- the mod
                // prints "D-PAD RIGHT" for this control at Hao's muffler shop and has done for
                // months. INPUT_CELLPHONE_RIGHT is also d-pad right, and that is how the cursor
                // moves along a row of tiles.
                //
                // So on a pad, one press of right did both: the cursor stepped, and the thing
                // it stepped off went into the bag. Walk along a row of eight sandwiches and
                // four of them are in the bag before you have pressed anything else -- which
                // from the other side of the screen is indistinguishable from the mod eating
                // your shopping. (It was not. They were in the bag, whole.)
                //
                // Asked as "was this the same press" rather than "is he on a pad", because that
                // is the actual question and it needs no guess about the device: a keyboard E
                // does not move the cursor, so nothing on a keyboard changes.
                return !Game.IsControlJustPressed(Control.PhoneRight) &&
                       !Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0,
                                            (int)Control.PhoneRight);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// The bag off the shoulder: B on a keyboard, LB on a pad.
        ///
        /// THE FOOTER USED TO PRINT B TWICE, and neither of them was this. It is INPUT_COVER,
        /// which is Q on a keyboard -- so the label was wrong there -- and on a pad B is
        /// INPUT_CELLPHONE_CANCEL, which is DONE, sat two keys along in the same row saying the
        /// same letter. Pressing B on a pad closed the screen.
        ///
        /// B is read directly, the way Strap reads it in the street, so the key that drops the
        /// bag standing in a road is the key that drops it standing in this screen. Its own
        /// edge is tracked here because this build of ScriptHookVDotNet has no just-pressed for
        /// a raw key and a held B would otherwise drop the bag on every frame of the press.
        /// </summary>
        private bool DroppingBag()
        {
            var down = false;

            try
            {
                down = Game.IsControlJustPressed(Control.FrontendLb)
                    || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0,
                                           (int)Control.FrontendLb)
                    || Game.IsKeyPressed(System.Windows.Forms.Keys.B);
            }
            catch
            {
                // Unreadable is not pressed.
            }

            var pressed = down && !_bagKeyHeld;
            _bagKeyHeld = down;

            return pressed;
        }

        private bool _bagKeyHeld;

        /// <summary>Grams moved by one press. The lot goes on a hold.</summary>
        private const float ShiftGrams = 100f;

        /// <summary>
        /// One lot across, and it never leaves more than it takes.
        ///
        /// The same order the stash and the boot use: offer it to the destination, ask what
        /// went in, take exactly that much out of the source, and put back anything the source
        /// could not actually give up. Product that goes missing between two containers is the
        /// one bug in an inventory nobody ever forgives.
        /// </summary>
        private static float Carry(Stash from, Stash to, string id, float grams, bool bagged)
        {
            if (from == null || to == null || grams <= 0.005f) return 0f;

            var purity = bagged ? from.PurityOf(id) : from.BulkPurityOf(id);

            var took = bagged
                ? to.AddPackaged(id, grams, purity)
                : to.AddBulk(id, grams, purity);

            if (took <= 0.005f) return 0f;

            var gone = bagged ? from.RemovePackaged(id, took) : from.RemoveBulk(id, took);

            if (gone < took - 0.005f)
            {
                // The source had less than it said. Hand the difference back rather than
                // minting it.
                if (bagged) to.RemovePackaged(id, took - gone);
                else to.RemoveBulk(id, took - gone);
            }

            return gone;
        }

        /// <summary>Product rows first, then his food, then the bag's.</summary>
        private int Places => _rows.Count + _food.Count + _bagFood.Count;

        /// <summary>The id under a food place, whichever shelf it is on.</summary>
        private string FoodAt(int at)
        {
            var i = at - _rows.Count;

            if (i < 0) return null;
            if (i < _food.Count) return _food[i];

            i -= _food.Count;

            return i < _bagFood.Count ? _bagFood[i] : null;
        }

        /// <summary>Whether a food place is the bag's shelf rather than his pockets.</summary>
        private bool FoodInBag(int at) => at >= _rows.Count + _food.Count;

        /// <summary>Whether the bag is on his back, and therefore able to come off.</summary>
        private bool Carrying
        {
            get
            {
                if (BagOn == null) return false;

                try { return BagOn(); }
                catch { return false; }
            }
        }

        private bool OnFood => _selected >= _rows.Count;

        /// <summary>When the drop key went down, so holding it can mean all of it.</summary>
        private int _holdFrom;

        /// <summary>How long it has to be held before a tap becomes the lot.</summary>
        private const int HoldAllMs = 600;

        /// <summary>
        /// Up and down, which is a line at a time inside the pane you are in.
        ///
        /// IT STAYS ON ITS SIDE. Left and right cross the seam because the seam is a horizontal
        /// gap and stepping over it is what left and right look like they do; up and down move
        /// within a container, which is what a column of a container is for.
        /// </summary>
        private void Vertical(int dir)
        {
            if (Places == 0) return;

            var side = SideOf(_selected);
            var list = Pane(side);

            var at = list.IndexOf(_selected);
            if (at < 0) { Land(First()); return; }

            var next = at + dir * Across;

            if (next >= 0 && next < list.Count) { Land(list[next]); return; }

            if (dir <= 0) return;

            // Down off a short last line: the last thing in this pane, if we are not on it.
            var last = list.Count - 1;
            if (at < last) Land(list[last]);
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

        /// <summary>
        /// Left and right, which walks the pane you are in and then steps over the seam.
        ///
        /// OFF THE END OF ONE PANE IS THE START OF THE OTHER, which is what the gap down the
        /// middle of this screen looks like it should do. It used to wrap round the whole flat
        /// list, which with two squares drawn side by side meant walking off the right of the
        /// bag and reappearing in your jacket having crossed nothing.
        /// </summary>
        private void Move(int step)
        {
            if (Places == 0) return;

            var side = SideOf(_selected);
            var list = Pane(side);

            var at = list.IndexOf(_selected);
            if (at < 0) { Land(First()); return; }

            var next = at + step;

            if (next >= 0 && next < list.Count) { Land(list[next]); return; }

            // Over the seam, landing at the near end of the pane you are stepping into.
            var other = Pane(1 - side);

            if (other.Count == 0)
            {
                // Nothing over there, so wrap inside this one rather than going nowhere.
                Land(list[next < 0 ? list.Count - 1 : 0]);
                return;
            }

            Land(next < 0 ? other[other.Count - 1] : other[0]);
        }

        /// <summary>The first place there is, whichever pane it is in.</summary>
        private int First()
        {
            if (_left.Count > 0) return _left[0];
            return _right.Count > 0 ? _right[0] : 0;
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
            // ---- THE BAG IS THE DROP, WITH EVERYTHING IN IT ----
            //
            // Weight only moves in the bag, so a man carrying one who puts product down is
            // putting THE BAG down -- not tipping thirty grams into a hedge and keeping the
            // holdall. Two containers on one pavement, one inside the other, is a fiction the
            // player has to hold in their head for no reason at all.
            //
            // Without the bag it is the old behaviour, which is the right answer for the case
            // it was written for: pockets, a patrol coming, and somewhere to put a parcel.
            if (Carrying)
            {
                try { if (DropBag != null) DropBag(); }
                catch { /* the key in the street still works */ }

                Close();
                return;
            }

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
        /// <summary>
        /// Where the grid starts, under the head and its one meter.
        ///
        /// ONE METER, NOT TWO. The head carried ON YOU and then, with a bag on, a second row
        /// carrying BAG -- two bars stacked over two grids, saying in a readout what the tabs
        /// under them now say by being tabs. The bag's count lives on its own tab.
        /// </summary>
        private const float ContentTop = UiKit.HeadH + 0.026f;

        /// <summary>The card under the tiles that says what the chosen one is.</summary>
        private const float CardH = 0.072f;

        
        /// <summary>The heading over a pane: its name on the left, its count on the right.</summary>
        private const float CapH = 0.026f;

        /// <summary>Tiles across ONE pane. Two panes, so the panel is twice this plus the seam.</summary>
        /// <summary>
        /// How many tiles across, and it is ONE GRID now rather than two.
        ///
        /// THE SCREEN WAS TWO OF EVERYTHING. Two meters, two headings, two four-wide grids
        /// side by side in a panel half the width of the screen, and a card under the lot.
        /// Every one of those pairs was defensible on its own -- the seam down the middle
        /// really does say "this side is on you, that side is in the bag" -- and together they
        /// were more furniture than inventory.
        ///
        /// So it is laid out the way Bare Minimum lays out a pocket: one grid, five across,
        /// in a panel exactly as wide as the grid. Which container you are looking at is a
        /// pair of tabs above it rather than a second grid beside it, and walking off the end
        /// of one crosses to the other exactly as it did when they were both on screen --
        /// Move has always worked a pane at a time, so nothing about moving around has
        /// changed, only how much of it you can see at once.
        /// </summary>
        private const int Across = 5;

        /// <summary>
        /// How many rows are on screen at once, and why there is a limit at all.
        ///
        /// A TILE AT 0.105 IS TWICE THE HEIGHT IT WAS, and the panel grew with it. Eight drugs
        /// carried as both bagged units and uncut weight is sixteen places before a sandwich
        /// is counted, which at four across used to be four rows of small squares and at five
        /// across is four rows of big ones -- fine. Past that the panel grows off the bottom of
        /// the screen, and a screen that does that is one nobody can use precisely when they
        /// have most to look at.
        ///
        /// Four rows is twenty tiles, which is Bare Minimum's page exactly. Over that the grid
        /// scrolls by rows under the cursor rather than paging, because the cursor here walks
        /// a flat list that crosses into the other container at its ends -- a page number
        /// would be a third thing to keep in step with that, and a window is none.
        /// </summary>
        private const int MostRows = 4;

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
            // the grid itself does -- see Lines, which both of them now call so they cannot
            // disagree about it. They used to be two copies of the arithmetic and one of them
            // only counted the product.
            var tiles = CapH + Lines() * (Cell + CellGap) - CellGap + 0.010f;

            var height = ContentTop + tiles + CardH + UiKit.FootH;

            var pad = Hud.ToX(PadH);

            // THE PANEL IS THE WIDTH OF ITS GRID, NOT THE OTHER WAY ROUND.
            //
            // It used to be the width of TWO grids and a gutter, which on a wide monitor was
            // most of the screen for twenty small squares. One grid, five tiles across, and
            // the panel is that and its padding. Bare Minimum's pocket measures itself the
            // same way and says so in its own comment; this is the same sentence.
            var gridWide = Across * Hud.ToX(Cell) + (Across - 1) * Hud.ToX(CellGap);
            var panelWidth = gridWide + pad * 2f;

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

            // ---- TWO TABS AND ONE GRID ----
            //
            // The seam between the two containers is still the point; it is a pair of tabs
            // now rather than a pair of grids. Half a screen of furniture said the same thing
            // the word IN THE BAG says, and said it at the cost of every tile being half the
            // size it should be.
            //
            // Walking off the end of one side still steps into the other -- see Move, which
            // has always worked a pane at a time -- so the tab follows the cursor rather than
            // being something else to press.
            Tabs(x, y, gridWide, arrive);

            y += CapH;

            var lines = Lines();

            Square(x, y, gridWide, SideOf(_selected), lines, arrive);

            y += lines * (Cell + CellGap) - CellGap + 0.010f;

            y = Card(x, wide, y, arrive);

            // ---- the keys ----            // ---- the keys ----
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
            // DONE IS PINNED AND EVERYTHING ELSE GETS WHAT IS LEFT.
            //
            // The row flows left to right and DONE sits in the corner every screen in the mod
            // puts it in, so the two meet somewhere in the middle -- and they were meeting ON
            // each other, printing "ALL OF IT B" and "DONE A" over one another as soon as the
            // bag added a key. So the corner is measured first and the flow simply stops when
            // it would reach it. A key that does not fit is dropped, not overlapped: the last
            // ones on the row are the least used, and a legend that lies about the layout is
            // worse than one that is short.
            var stop = right - UiKit.RightWidth(UiKit.Back, "DONE") - 0.012f;

            UiKit.KeyRight(right, y, UiKit.Back, "DONE", arrive);

            var kx = x;

            if (Places > 0)
            {
                kx = Fits(kx, stop, y, null, "arrow_leftright.png", "PICK", arrive);

                if (OnFood)
                {
                    kx = Fits(kx, stop, y, UiKit.Confirm, null, "EAT IT", arrive);

                    if (Carrying)
                    {
                        kx = Fits(kx, stop, y, UiKit.Swap, null,
                                  FoodInBag(_selected) ? "TO POCKETS" : "TO BAG", arrive);
                    }
                }
                else
                {
                    if (Carrying && _selected >= 0 && _selected < _rows.Count)
                    {
                        kx = Fits(kx, stop, y, UiKit.Swap, null,
                                  _rows[_selected].InBag ? "TO POCKETS" : "TO BAG", arrive);
                    }

                    // NOT "DROP THE BAG". This is the jump key and it puts the LOT under the
                    // cursor on the pavement -- a hundred grams of it, or all of it on a hold.
                    // It has never had anything to do with the bag, and saying so on a footer
                    // one key along from the bag's own row is how a man loses a kilo learning
                    // what a button does.
                    kx = Fits(kx, stop, y, UiKit.Drop, null, "PUT IT DOWN", arrive);

                    if (!Carrying) kx = Fits(kx, stop, y, "HOLD", null, "ALL OF IT", arrive);

                    if (_selected >= 0 && _selected < _rows.Count && _rows[_selected].Bagged)
                    {
                        kx = Fits(kx, stop, y, UiKit.Confirm, null, "TAKE ONE", arrive);
                    }
                }
            }

            // OFFERED WHEREVER THE CURSOR IS. It was only printed on a food tile or an empty
            // screen, so the one row where a player is most likely to want the bag off -- stood
            // on a lot of product, deciding what he is carrying -- was the row that never
            // mentioned it. Fits drops it if the row has run out of width, which is the honest
            // way to be short of room.
            if (Carrying) Fits(kx, stop, y, UiKit.Sling, null, "DROP BAG", arrive);
        }

        /// <summary>One key, if there is room for it before the corner. See Keys.</summary>
        private static float Fits(float x, float stop, float y, string cap, string icon, string words,
                                  float arrive)
        {
            if (x + UiKit.KeyWidth(cap, icon, words) > stop) return x;

            return UiKit.Key(x, y, cap, icon, words, arrive);
        }

        /// <summary>
        /// The card under the tiles: the chosen thing, said properly.        /// <summary>
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

            // ---- WHATEVER THE CURSOR IS ON, AND THERE IS ONLY ONE CURSOR ----
            //
            // This used to step back to a one-line summary on the food, because the food band
            // had a caption of its own and two captions for one cursor is one too many. There
            // is one band now, so the card answers for all of it.
            if (OnFood)
            {
                var id = FoodAt(_selected);

                if (!string.IsNullOrEmpty(id))
                {
                    FoodCard(id, x, wide, y, arrive, grown, FoodInBag(_selected));
                    return y + CardH;
                }
            }

            if (_selected < 0 || _selected >= _rows.Count)
            {
                var says = Places == 0
                    ? "Nothing on you."
                    : Places == 1 ? "1 thing on you" : Places + " things on you";

                Hud.Text(says, x, y + 0.004f, 0.27f,
                         Palette.Alpha(Palette.TextDim, (int)(190f * arrive)),
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

            var form = UiKit.Tag(afterName, nameY + 0.005f, row.Bagged ? "BAGGED" : "WEIGHT",
                                 row.Bagged ? Palette.Brand : Palette.TextDim, show);

            UiKit.Tag(form, nameY + 0.005f, row.InBag ? "IN THE BAG" : "ON YOU",
                      row.InBag ? Palette.Brand : Palette.TextDim, show);

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
        /// The same card the product gets, for a thing you eat.
        ///
        /// FOOD HAD A NAME AND NOTHING ELSE. The product rows get a plate, the picture at
        /// size, the name, a tag saying what form it is in and a line saying how much -- and
        /// the food got one grey caption, on the same screen, under the same cursor. Two
        /// halves of one inventory answering the same question differently is the sort of
        /// thing a player reads as one of them being unfinished, and they were right.
        ///
        /// WHAT IT SAYS COMES FROM NEXT DOOR. The name, the picture, the shelf it came off and
        /// the line describing it are all Bare Minimum's -- see Core.Larder -- and every one
        /// of them falls back to nothing rather than to a guess, so an install without them
        /// gets a card with a name on it rather than a card full of empty labels.
        /// </summary>
        private void FoodCard(string id, float x, float wide, float y, float arrive, float grown,
                              bool inBag)
        {
            var cardH = CardH - 0.010f;

            // The same plate, the same height and the same left rail the product card sits on,
            // because the whole point is that the two are one screen. See Card.

            var slide = Hud.ToX(0.012f) * (1f - grown);
            var show = arrive * (0.6f + 0.4f * grown);

            Theme.Plate(x, y, wide, cardH, 0.55f * show);

            // ---- the picture, big ----
            var file = Core.Larder.IconOf(id);
            var ax = x + Hud.ToX(0.014f) + slide;
            var tx = x + 0.012f + slide;

            if (!string.IsNullOrEmpty(file) &&
                Hud.File(file, ax + Hud.ToX(BigArt) * 0.5f, y + cardH * 0.5f, BigArt, 0f,
                         Core.Larder.TintOf(id)))
            {
                tx = ax + Hud.ToX(BigArt) + 0.010f;
            }

            // ---- the name, and which shelf it came off ----
            var name = Core.Larder.NameOf(id);
            var nameY = y + 0.008f;

            Hud.Text(name, tx, nameY, 0.34f, Palette.Alpha(Palette.Text, (int)(255f * show)),
                     Hud.FontBody, centre: false);

            var shelf = Core.Larder.CategoryOf(id);

            if (!string.IsNullOrEmpty(shelf))
            {
                UiKit.Tag(tx + Hud.MeasureText(name, 0.34f, Hud.FontBody) + 0.009f, nameY + 0.005f,
                          shelf.ToUpperInvariant(), Palette.Brand, show);
            }

            // ---- how many, and what it is ----
            var lineY = y + 0.037f;

            var many = inBag ? Held(id) : Core.Larder.CountOf(id);
            var count = many + (inBag ? " in the bag" : " on you");

            Hud.Text(count, tx, lineY, 0.30f, Palette.Alpha(Palette.Cash, (int)(255f * show)),
                     Hud.FontBody, centre: false);

            var desc = Core.Larder.DescOf(id);

            if (!string.IsNullOrEmpty(desc))
            {
                Hud.Text(Hud.Fit(desc, wide - (tx - x) - 0.020f -
                                       Hud.MeasureText(count, 0.30f, Hud.FontBody), 0.27f, Hud.FontBody),
                         tx + Hud.MeasureText(count, 0.30f, Hud.FontBody) + 0.012f, lineY + 0.001f,
                         0.27f, Palette.Alpha(Palette.TextDim, (int)(215f * show)),
                         Hud.FontBody, centre: false);
            }
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
        /// What is on you, as squares. Returns the y the next thing may start at.
        ///
        /// The picture carries the drug, the chip along the bottom carries the amount, the
        /// corner carries how cut it is. A tile is a glance; the card under the strip is for
        /// reading.
        /// </summary>
        /// <summary>
        /// One pane's worth of squares.
        ///
        /// FILLED FROM THE TOP LEFT OF ITS OWN PANE, not centred: a pane is a container with a
        /// heading over it, and a container whose contents drift toward the middle does not
        /// read as a container. The centring was right when this was one grid floating in a
        /// panel; it is wrong now that each side has an edge of its own.
        /// </summary>
        private void Square(float x, float y, float w, int side, int lines, float arrive)
        {
            var list = Pane(side);

            var tile = Hud.ToX(Cell);
            var gap = Hud.ToX(CellGap);

            if (list.Count == 0)
            {
                Hud.Text(side == 1 ? (Carrying ? "Bag's empty." : "No bag on you.") : "Nothing on you.",
                         x + w * 0.5f, y + Cell * 0.5f - 0.008f, 0.26f,
                         Palette.Alpha(Palette.TextDim, (int)(180f * arrive)), Hud.FontBody);

                return;
            }

            var age = Game.GameTime - _shownAt;
            var grown = Theme.Grown(_pickedAt);

            // WHICH ROW THE GRID STARTS ON. Nought until there is more than fits, and then
            // the smallest number that keeps the cursor on screen -- so it does not move at
            // all until you walk off the bottom, and then it moves one row. See MostRows.
            var from = 0;

            var here = list.IndexOf(_selected);

            if (here >= 0)
            {
                var onLine = here / Across;

                if (onLine >= lines) from = onLine - lines + 1;
            }

            for (var i = from * Across; i < list.Count; i++)
            {
                var at = list[i];

                var col = i % Across;
                var line = i / Across - from;

                if (line >= lines) break;

                // Staggered a frame or two apart, so the pocket is unpacked rather than
                // switched on.
                var land = UiKit.Landed(age, (i - from * Across) * 45, EnterMs);

                var show = arrive * land;
                if (show <= 0.01f) continue;

                // CENTRED ON WHAT IS IN THE ROW, not filled from the left. The grid is five
                // wide whatever you are carrying, so three things sat in the corner of a panel
                // built for twenty with the rest of it empty. The panel cannot shrink -- the
                // head has to fit its meter and the tabs their names -- so the tiles move to
                // the middle of it instead. Straight out of Bare Minimum's pocket.
                var rowFirst = (line + from) * Across;
                var inRow = Math.Min(Across, list.Count - rowFirst);

                var tx = x + (w - (inRow * tile + (inRow - 1) * gap)) * 0.5f + col * (tile + gap);
                var ty = y + line * (Cell + CellGap) + EnterRise * 0.5f * (1f - land);

                var picked = at == _selected;
                var lit = Theme.Lit(at, _selected, _lastSelected, grown);

                var flashLeft = _droppedRow == at ? UiKit.Flash(_droppedAt, DropFlashMs) : 0f;
                var flashing = flashLeft > 0f;

                Tile(tx, ty, tile, Cell, lit, show, flashing, flashLeft);

                if (picked) _glide.Target(tx, ty, tile, Cell);

                var bright = flashing ? 1f : lit;

                if (at < _rows.Count) Lot(_rows[at], tx, ty, tile, show, bright, picked, grown);
                else Bite(FoodAt(at), tx, ty, tile, show, lit, picked, grown, FoodInBag(at));
            }
        }

        /// <summary>
        /// How many rows the grid needs for the side being shown.
        ///
        /// THE SIDE YOU ARE ON, not the taller of the two. It used to be the larger of both
        /// panes because both were drawn at once and a panel sized for one cut the other off;
        /// with one grid on screen the panel is as tall as what is actually in it, so a pocket
        /// with four things in it is a panel with one row in it rather than a panel sized for
        /// whatever happens to be in the bag.
        ///
        /// One row minimum, so an empty side still has somewhere to say it is empty.
        /// </summary>
        private int Lines()
        {
            var many = Pane(SideOf(_selected)).Count;
            var rows = (many + Across - 1) / Across;

            if (rows > MostRows) rows = MostRows;

            return rows < 1 ? 1 : rows;
        }

        /// <summary>One square of product: the picture, the amount across the foot, the cut mark.</summary>
        private void Lot(PocketRow row, float tx, float ty, float tile, float show, float bright,
                         bool picked, float grown)
        {
            var art = Icons.ForDrug(row.Drug.Id);

            if (art.HasFile)
            {
                var swell = picked ? 1f + PickGrow * grown : 1f;

                Hud.File(art.File, tx + tile * 0.5f, ty + Cell * 0.40f, Cell * 0.52f * swell, 0f,
                         Theme.Ink(Palette.Alpha(Palette.Text, (int)(215f * show)), bright));
            }

            // Across the whole tile rather than in a corner chip, because these are not counts.
            // "57.5g" and "87 bars" are four and seven characters, and a corner badge sized for
            // a single digit turns both into a smudge.
            Hud.RectFrom(tx, ty + Cell - ChipHeight, tile, ChipHeight,
                         Color.FromArgb((int)((205f - 60f * bright) * show), 12, 13, 15));

            var amount = row.Bagged ? row.Drug.Amount(row.Held) : row.Drug.Bulk(row.Held);

            Hud.Text(Hud.Fit(amount, tile - 0.004f, 0.22f, Hud.FontLabel),
                     tx + tile * 0.5f, ty + Cell - ChipHeight - 0.0005f, 0.22f,
                     Palette.Alpha(picked ? Palette.Text : Palette.TextDim, (int)(255f * show)),
                     Hud.FontLabel);

            // WHICH SIDE IT IS ON, in the corner the cut mark does not use. A grid holding
            // two containers has to say which is which on every tile or it is one container
            // drawn wrong.
            if (row.InBag)
            {
                Hud.RectFrom(tx + 0.0015f, ty + 0.0015f, Hud.ToX(0.009f), 0.009f,
                             Palette.Alpha(Palette.Brand, (int)(200f * show)));
            }

            if (row.Purity <= 0f) return;

            Hud.File(Stash.Mark(row.Purity), tx + tile - Hud.ToX(0.008f), ty + 0.008f, 0.010f, 0f,
                     Theme.Ink(Palette.Alpha(Palette.TextDim, (int)(210f * show)), bright));
        }

        /// <summary>One square of food: the picture in its own colour, and a count if there is more than one.</summary>
        private void Bite(string id, float tx, float ty, float tile, float show, float lit,
                          bool picked, float grown, bool inBag)
        {
            if (string.IsNullOrEmpty(id)) return;

            var art = Core.Larder.IconOf(id);

            if (!string.IsNullOrEmpty(art))
            {
                var swell = picked ? 1f + PickGrow * grown : 1f;

                // The item's own colour on the dark tile, brightening as the plate comes up.
                // The art is white and the sprite multiplies, so one file does all of it.
                var ink = Theme.Ink(Palette.Alpha(Core.Larder.TintOf(id), (int)(238f * show)), lit);

                Hud.File(art, tx + tile * 0.5f, ty + Cell * 0.44f, Cell * 0.56f * swell, 0f, ink);
            }

            var many = inBag ? Held(id) : Core.Larder.CountOf(id);
            if (many <= 1) return;

            var chip = Hud.ToX(0.013f);

            Hud.RectFrom(tx + tile - chip, ty + Cell - 0.013f, chip, 0.013f,
                         Color.FromArgb((int)(215f * show), 12, 13, 15));

            Hud.TextRight(many.ToString(), tx + tile - 0.0015f, ty + Cell - 0.0125f, 0.23f,
                          Palette.Alpha(Palette.Text, (int)(255f * show)), Hud.FontLabel);
        }
    }
}

