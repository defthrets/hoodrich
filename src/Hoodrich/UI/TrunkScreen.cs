using System;
using System.Collections.Generic;
using System.Drawing;
using Control = GTA.Control;
using GTA;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Economy;
using Hoodrich.State;
using Hoodrich.Weapons;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.UI
{
    /// <summary>
    /// The boot of your car, open.
    ///
    /// TWO GRIDS, SIDE BY SIDE: what is on you on the left, what is in the boot on the right,
    /// and one key that moves the thing under the cursor across the gutter. The same screen
    /// Bare Minimum's fridge is, on purpose -- anybody who has put a sandwich in a fridge
    /// over there already knows how to put a gun in a boot over here. A transfer is a thing
    /// with a direction, and two columns make the direction obvious without a word: from the
    /// left side SPACE puts it in, from the right side SPACE takes it out.
    ///
    /// EVERYTHING IN ONE GRID. It used to be three pages of a list -- product, guns, food --
    /// and a boot is one place, so it is one grid on each side: the product first, then the
    /// guns, then the food, each drawn as the picture it has everywhere else in the mod, with
    /// how much of it on a chip. The product page's move is the stash screen's own against a
    /// second Stash; a gun goes in with its magazine and its parts and comes back with both;
    /// the food talks to Bare Minimum through Core.Pantry, and is simply not there when that
    /// mod is not.
    ///
    /// THE CURSOR CROSSES AT THE EDGE, and refuses to cross to an empty side, exactly as the
    /// fridge does. Each side remembers where its cursor was.
    ///
    /// The screen does not know about the car. Locations.Boot hands it a Trunk and a way to
    /// say something changed, and takes both away again when the lid shuts.
    /// </summary>
    internal sealed class TrunkScreen
    {
        private enum Kind { Product, Gun, Food }

        /// <summary>One thing that is on either side, or both.</summary>
        private sealed class Item
        {
            public Kind Kind;
            public string Id = "";
            public string Name = "";
            public DrugDef Drug;           // product only
            public bool Bagged;            // product only
            public string Icon = "";       // guns: the art pack name; food: a full path
            public float You, Boot;        // grams, or counts, or 0/1 for a gun
            public string YouTag = "", BootTag = "";
            public float YouPurity, BootPurity;
            public Color Tint = Palette.Text;

            public string TagOn(int side) { return side == 0 ? YouTag : BootTag; }
            public float PurityOn(int side) { return side == 0 ? YouPurity : BootPurity; }
        }

        /// <summary>
        /// How tall a tile is, as a fraction of the screen's height; its width follows through
        /// the aspect so it is square on screen. A touch under the fridge's, because this
        /// panel stands on the bottom of the screen rather than hanging from the top.
        /// </summary>
        private const float TileH = 0.090f;

        private const int Columns = 4;

        /// <summary>
        /// Rows on screen at once, per side. A ceiling, not a fixed height -- see Shown. Three
        /// keeps the whole panel under the middle of the screen with the car behind it.
        /// </summary>
        private const int MaxRows = 3;

        private const float PadH = 0.024f;
        private const float GridPad = 0.006f;

        /// <summary>The caption over each pane: its name, its figure, a rule in its colour.</summary>
        private const float CapH = 0.028f;

        /// <summary>The card under the grids: the chosen thing, said properly.</summary>
        private const float CardH = 0.056f;

        /// <summary>The seam between the two panes, so they read as two things.</summary>
        private const float Gutter = 0.010f;

        /// <summary>The gap between tiles, as an x fraction. Turned into y through the aspect.</summary>
        private const float Gap = 0.0018f;

        /// <summary>How much the chosen picture swells as its plate comes up.</summary>
        private const float PickGrow = 0.10f;

        /// <summary>
        /// Where the bottom of the panel rests.
        ///
        /// STOOD ON THE BOTTOM OF THE SCREEN rather than hung from the top like the fridge:
        /// this is a card about the car, the car is what the camera is looking at, and a
        /// panel in the middle of the screen sat across the boot he was leaning into.
        /// </summary>
        private const float BottomY = 0.945f;

        private const float StepGrams = 10f;
        private const int OpenGraceMs = 220;
        private const int RepeatMs = 110;
        private const int EnterMs = 170;
        private const float EnterRise = 0.014f;
        private const int MovedFlashMs = 420;

        private readonly Curtain _curtain = new Curtain();
        private readonly Glide _glide = new Glide();

        /// <summary>What each side is showing, rebuilt after anything moves.</summary>
        private readonly List<Item> _mine = new List<Item>();
        private readonly List<Item> _boot = new List<Item>();

        private Trunk _trunk;
        private Stash _pockets;
        private Drugs _drugs;
        private WeaponRegistry _guns;
        private GunLocker _locker;
        private Action _changed;
        private string _carName = "";

        /// <summary>0 = on you, 1 = in the boot.</summary>
        private int _side;

        /// <summary>Where the cursor is on each side, so crossing back returns to it.</summary>
        private readonly int[] _index = new int[2];
        private readonly int[] _page = new int[2];

        /// <summary>The slot the cursor was on before this one, and when it moved. See Slot.</summary>
        private int _last = -1;
        private int _openedAt, _pickedAt, _nextRepeat;

        /// <summary>The thing that just arrived on a side, so its tile can flash there.</summary>
        private string _movedKey;
        private int _movedSide, _movedAt;

        /// <summary>
        /// SPACE has to come up before it moves anything else. A held key moves ten grams a
        /// tick, which is right for product; when the last of a thing leaves a side the tile
        /// under the cursor becomes the next thing, and a key still down would sweep that
        /// across too.
        /// </summary>
        private bool _release;

        public bool IsOpen => _curtain.Showing;

        public void Open(Trunk trunk, Stash pockets, Drugs drugs, WeaponRegistry guns, GunLocker locker,
                         string carName, Action changed)
        {
            if (trunk == null || pockets == null || drugs == null) return;

            _trunk = trunk; _pockets = pockets; _drugs = drugs; _guns = guns; _locker = locker;
            _changed = changed; _carName = carName ?? "";

            _index[0] = _index[1] = 0;
            _page[0] = _page[1] = 0;
            _last = -1;
            _movedKey = null;
            _release = false;
            _openedAt = _pickedAt = Game.GameTime;
            _glide.Reset();
            _curtain.Open();

            Rebuild();

            // Start on whichever side has something, and on you when both do.
            _side = _mine.Count > 0 || _boot.Count == 0 ? 0 : 1;

            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        public void Close()
        {
            if (IsOpen) InputGuard.Swallow();
            _curtain.Close();
            _trunk = null; _pockets = null; _drugs = null; _guns = null; _locker = null; _changed = null;
            _mine.Clear();
            _boot.Clear();
        }

        // ---- the two sides ------------------------------------------------------------------

        /// <summary>
        /// Both lists from scratch: the product, then the guns, then the food, each on whichever
        /// side has some of it. The cursor stays on the thing it was on where that thing is
        /// still there, and is clamped back into what is left where it is not.
        /// </summary>
        private void Rebuild()
        {
            var keep = Key(Picked());

            _mine.Clear();
            _boot.Clear();

            if (_trunk == null) return;

            var all = new List<Item>();
            BuildProduct(all);
            BuildGuns(all);
            BuildFood(all);

            foreach (var item in all)
            {
                if (item.You > 0.005f) _mine.Add(item);
                if (item.Boot > 0.005f) _boot.Add(item);
            }

            Settle(0, keep);
            Settle(1, keep);

            // If the side the cursor is on has just emptied, move it to the one that has not.
            if (Count(_side) == 0 && Count(1 - _side) > 0) _side = 1 - _side;
        }

        private void Settle(int side, string keep)
        {
            var list = List(side);

            if (keep != null)
            {
                for (var i = 0; i < list.Count; i++)
                {
                    if (Key(list[i]) != keep) continue;
                    _index[side] = i;
                    break;
                }
            }

            if (_index[side] >= list.Count) _index[side] = Math.Max(0, list.Count - 1);
            if (_index[side] < 0) _index[side] = 0;

            var perPage = Columns * Shown();
            _page[side] = perPage <= 0 ? 0 : _index[side] / perPage;
        }

        private List<Item> List(int side) => side == 0 ? _mine : _boot;

        private int Count(int side) => List(side).Count;

        /// <summary>The thing under the cursor, or null.</summary>
        private Item Picked()
        {
            var list = List(_side);
            if (list.Count == 0) return null;

            return list[Clamp(_index[_side], 0, list.Count - 1)];
        }

        private static string Key(Item item)
        {
            if (item == null) return null;
            return (int)item.Kind + "|" + item.Id + (item.Bagged ? "|b" : "|w");
        }

        /// <summary>One index space across both panes, so a plate can go down on one side while another comes up on the other.</summary>
        private static int Slot(int side, int i) => side * 1000 + i;

        private void BuildProduct(List<Item> into)
        {
            foreach (var drug in _drugs.All)
            {
                AddProduct(into, drug, true);
                AddProduct(into, drug, false);
            }
        }

        private void AddProduct(List<Item> into, DrugDef drug, bool bagged)
        {
            var you = bagged ? _pockets.PackagedOf(drug.Id) : _pockets.BulkOf(drug.Id);
            var boot = bagged ? _trunk.Stash.PackagedOf(drug.Id) : _trunk.Stash.BulkOf(drug.Id);
            if (you <= 0.005f && boot <= 0.005f) return;

            into.Add(new Item
            {
                Kind = Kind.Product,
                Id = drug.Id, Name = drug.Name, Drug = drug, Bagged = bagged,
                You = you, Boot = boot,
                YouTag = Amount(drug, you, bagged), BootTag = Amount(drug, boot, bagged),
                YouPurity = bagged ? _pockets.PurityOf(drug.Id) : _pockets.BulkPurityOf(drug.Id),
                BootPurity = bagged ? _trunk.Stash.PurityOf(drug.Id) : _trunk.Stash.BulkPurityOf(drug.Id)
            });
        }

        private void BuildGuns(List<Item> into)
        {
            var me = Game.Player.Character;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (_guns != null)
            {
                foreach (var def in _guns.All)
                {
                    if (def == null || def.Hash == WeaponRegistry.UnarmedHash) continue;

                    var has = me != null && me.Exists() &&
                              Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON, me.Handle, def.Hash, false);
                    var saved = GunRow(def.Id);
                    if (!has && saved == null) continue;

                    seen.Add(def.Id);
                    into.Add(new Item
                    {
                        Kind = Kind.Gun,
                        Id = def.Id, Name = def.Name, Icon = def.Icon ?? "",
                        You = has ? 1 : 0, Boot = saved != null ? 1 : 0,
                        YouTag = has ? Rounds(me, def.Hash) : "-",
                        BootTag = saved != null ? SavedRounds(saved) : "-"
                    });
                }
            }

            // A gun in the boot the registry does not know is kept listed so it can come out.
            foreach (var row in _trunk.Guns)
            {
                var id = (row ?? "").Split('|')[0];
                if (id.Length == 0 || seen.Contains(id)) continue;
                seen.Add(id);
                into.Add(new Item
                {
                    Kind = Kind.Gun, Id = id, Name = Named(id), Boot = 1,
                    YouTag = "-", BootTag = SavedRounds(row)
                });
            }
        }

        private void BuildFood(List<Item> into)
        {
            var ids = new List<string>(Pantry.Ids());
            foreach (var k in _trunk.Food.Keys) if (!ids.Contains(k)) ids.Add(k);

            foreach (var id in ids)
            {
                var you = Pantry.CountOf(id);
                int boot;
                _trunk.Food.TryGetValue(id, out boot);
                if (you <= 0 && boot <= 0) continue;

                into.Add(new Item
                {
                    Kind = Kind.Food,
                    Id = id, Name = Pantry.NameOf(id), Icon = Pantry.IconOf(id),
                    You = you, Boot = boot,
                    YouTag = you > 0 ? you.ToString() : "-", BootTag = boot > 0 ? boot.ToString() : "-",
                    Tint = Larder.Present ? Larder.TintOf(id) : Palette.Text
                });
            }
        }

        private static string Amount(DrugDef drug, float quantity, bool bagged)
        {
            if (quantity <= 0.005f) return "-";
            if (drug == null) return quantity.ToString("0.#") + "g";
            return bagged ? drug.Amount(quantity) : drug.Bulk(quantity);
        }

        private static string Rounds(Ped me, uint hash)
        {
            try
            {
                var n = Function.Call<int>(Hash.GET_AMMO_IN_PED_WEAPON, me.Handle, hash);
                return n > 0 ? n + " RDS" : "HELD";
            }
            catch { return "HELD"; }
        }

        /// <summary>The rounds a gun went into the boot with, off its saved row.</summary>
        private static string SavedRounds(string saved)
        {
            var bits = (saved ?? "").Split('|');
            var rounds = 0;
            if (bits.Length > 1) int.TryParse(bits[1], out rounds);
            return rounds > 0 ? rounds + " RDS" : "HELD";
        }

        /// <summary>The parts a gun in the boot went in with, off its saved row.</summary>
        private static int SavedParts(string saved)
        {
            var bits = (saved ?? "").Split('|');
            var n = 0;
            for (var i = 2; i < bits.Length; i++) if (!string.IsNullOrEmpty(bits[i])) n++;
            return n;
        }

        private string Named(string weaponId)
        {
            try
            {
                var def = _guns == null ? null : _guns.Get(Function.Call<uint>(Hash.GET_HASH_KEY, weaponId));
                if (def != null) return def.Name;
            }
            catch { /* the id is its own name then */ }

            var s = weaponId.StartsWith("WEAPON_", StringComparison.OrdinalIgnoreCase) ? weaponId.Substring(7) : weaponId;
            return s.Replace('_', ' ');
        }

        private string GunRow(string weaponId)
        {
            foreach (var row in _trunk.Guns)
            {
                if (row != null && row.StartsWith(weaponId + "|", StringComparison.OrdinalIgnoreCase)) return row;
            }
            return null;
        }

        // ---- input ----------------------------------------------------------------------

        public void Update()
        {
            if (!IsOpen) return;

            LockControls();

            if (!_curtain.Taking) return;
            if (Game.GameTime - _openedAt < OpenGraceMs) return;

            // THE MENU CONTROLS AS WELL AS THE PHONE ONES. The phone set is what every
            // screen in the house reads and it is right on foot; sat in a car the game has
            // its own ideas about the arrow keys and the d-pad, and the FRONTEND set is what
            // its own menus read there. Either answers.
            if (Pressed(Control.PhoneCancel) || Pressed(Control.FrontendCancel))
            {
                Hud.PlaySound("BACK", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                Close();
                return;
            }

            // The pages of a side: Q and E on a keyboard, the bumpers on a pad.
            if (Pressed(Control.FrontendLb) || Pressed(Control.Cover)) { Flip(-1); return; }
            if (Pressed(Control.FrontendRb) || Pressed(Control.Context)) { Flip(1); return; }

            if (Pressed(Control.PhoneUp) || Pressed(Control.FrontendUp)) Move(0, -1);
            else if (Pressed(Control.PhoneDown) || Pressed(Control.FrontendDown)) Move(0, 1);
            else if (Pressed(Control.PhoneLeft) || Pressed(Control.FrontendLeft)) Move(-1, 0);
            else if (Pressed(Control.PhoneRight) || Pressed(Control.FrontendRight)) Move(1, 0);

            // SPACE MOVES IT, and which way is decided by the side the cursor is on rather
            // than by a second key. Held, it keeps moving -- ten grams a tick -- through the
            // disabled path, the same as the house; a held sprint moves the lot.
            var down = Held(Control.Jump);

            if (!down) _release = false;

            if (!down || _release) return;
            if (Game.GameTime < _nextRepeat) return;

            Transfer(Held(Control.Sprint));
        }

        /// <summary>A page over on the side the cursor is on, if it has one.</summary>
        private void Flip(int by)
        {
            var list = List(_side);
            var perPage = Columns * Shown();
            if (list.Count == 0 || perPage <= 0) return;

            var pages = (list.Count + perPage - 1) / perPage;
            var to = Clamp(_page[_side] + by, 0, pages - 1);
            if (to == _page[_side]) return;

            _last = Slot(_side, _index[_side]);
            _page[_side] = to;
            _index[_side] = Clamp(to * perPage, 0, list.Count - 1);
            _pickedAt = Game.GameTime;

            Hud.PlaySound("NAV_LEFT_RIGHT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        /// <summary>
        /// Moves the cursor, clamped within a side and CROSSING at the outer edges.
        ///
        /// Clamped rather than wrapped: a grid has two axes and wrapping either of them is
        /// wrong half the time. Crossing is the exception, and only at the edge that faces
        /// the other pane.
        /// </summary>
        private void Move(int dx, int dy)
        {
            var list = List(_side);
            if (list.Count == 0) return;

            var at = Clamp(_index[_side], 0, list.Count - 1);
            var col = at % Columns;

            if (dy != 0)
            {
                var to = at + dy * Columns;
                if (to < 0 || to >= list.Count) return;

                Land(_side, to);
                return;
            }

            // ---- sideways ----
            var last = col == Columns - 1 || at + 1 >= list.Count;

            if (dx > 0)
            {
                if (!last) { Land(_side, at + 1); return; }
                if (_side == 0) Cross(1);
                return;
            }

            if (col > 0) { Land(_side, at - 1); return; }
            if (_side == 1) Cross(0);
        }

        /// <summary>Over to the other pane, to wherever its cursor was left.</summary>
        private void Cross(int side)
        {
            // Nothing to point at and nothing to do once you got there.
            if (Count(side) == 0) return;

            _last = Slot(_side, _index[_side]);
            _side = side;
            Settle(side, null);
            _pickedAt = Game.GameTime;

            Hud.PlaySound("NAV_LEFT_RIGHT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        private void Land(int side, int to)
        {
            _last = Slot(side, _index[side]);
            _index[side] = to;
            _pickedAt = Game.GameTime;

            var perPage = Columns * Shown();
            _page[side] = perPage <= 0 ? 0 : to / perPage;

            Hud.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        /// <summary>In or out, depending which side the cursor is on.</summary>
        private void Transfer(bool everything)
        {
            var item = Picked();
            if (item == null || _trunk == null) return;

            var inward = _side == 0;
            var moved = false;

            try
            {
                if (item.Kind == Kind.Product) moved = MoveProduct(item, inward, everything);
                else if (item.Kind == Kind.Gun) moved = MoveGun(item, inward);
                else moved = MoveFood(item, inward, everything);
            }
            catch (Exception ex)
            {
                Log.Info("Boot: could not move " + item.Id + ": " + ex.Message);
            }

            if (!moved)
            {
                Log.Info("Boot: would not move " + item.Id + (inward ? " in" : " out") + " (" +
                         Why(item, inward) + ").");
                Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                _nextRepeat = Game.GameTime + RepeatMs * 3;
                _release = true;
                return;
            }

            _nextRepeat = Game.GameTime + RepeatMs;
            Hud.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");

            var key = Key(item);

            _movedAt = Game.GameTime;
            _movedKey = key;
            _movedSide = 1 - _side;

            _changed?.Invoke();
            Rebuild();

            // A whole gun, or the last of something: the key has to come up before the next
            // thing under the cursor goes anywhere.
            if (item.Kind == Kind.Gun || Key(Picked()) != key) _release = true;
        }

        /// <summary>The reason a move did not happen, for the log. A best guess from what the item says.</summary>
        private string Why(Item item, bool inward)
        {
            if (_trunk == null) return "no trunk";
            if (inward && item.You <= 0.005f) return "none on him";
            if (!inward && item.Boot <= 0.005f) return "none in the boot";
            if (item.Kind == Kind.Gun && inward && _trunk.Guns.Count >= Trunk.BootGuns) return "boot full of guns";
            if (item.Kind == Kind.Food && inward && !Pantry.CanTake) return "Bare Minimum has no Take";
            if (item.Kind == Kind.Food && inward && _trunk.FoodCount >= Trunk.BootFood) return "boot full of food";
            if (item.Kind == Kind.Product)
            {
                return inward ? _trunk.Stash.FreeSpace.ToString("0") + "g room in the boot"
                              : _pockets.FreeSpace.ToString("0") + "g room on him";
            }
            return "refused";
        }

        /// <summary>
        /// Product, the way the house does it: the far side is asked first how much it will
        /// take, and only that much leaves the near side, so nothing is ever lost to a full
        /// boot.
        /// </summary>
        private bool MoveProduct(Item item, bool inward, bool everything)
        {
            var from = inward ? _pockets : _trunk.Stash;
            var to = inward ? _trunk.Stash : _pockets;

            var available = item.Bagged ? from.PackagedOf(item.Id) : from.BulkOf(item.Id);
            if (available <= 0.005f) return false;

            var want = everything ? available : Math.Min(StepGrams, available);

            if (item.Bagged)
            {
                var accepted = to.AddPackaged(item.Id, want, from.PurityOf(item.Id));
                if (accepted <= 0.005f) return false;

                var taken = from.RemovePackaged(item.Id, accepted);
                if (taken < accepted - 0.005f) to.RemovePackaged(item.Id, accepted - taken);
                return taken > 0.005f;
            }

            var acceptedBulk = to.AddBulk(item.Id, want, from.BulkPurityOf(item.Id));
            if (acceptedBulk <= 0.005f) return false;

            var takenBulk = from.RemoveBulk(item.Id, acceptedBulk);
            if (takenBulk < acceptedBulk - 0.005f) to.RemoveBulk(item.Id, acceptedBulk - takenBulk);
            return takenBulk > 0.005f;
        }

        /// <summary>
        /// A gun into the boot takes its magazine, its parts and its rounds with it, and comes
        /// back with all three. The locker is told either way, or it would hand the gun back
        /// after the next death as though the boot were a grave.
        /// </summary>
        private bool MoveGun(Item item, bool inward)
        {
            var me = Game.Player.Character;
            if (me == null || !me.Exists()) return false;

            var hash = Function.Call<uint>(Hash.GET_HASH_KEY, item.Id);
            if (hash == 0) return false;

            if (inward)
            {
                if (item.You <= 0 || _trunk.Guns.Count >= Trunk.BootGuns) return false;
                if (GunRow(item.Id) != null) return false;
                if (!Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON, me.Handle, hash, false)) return false;

                var ammo = Function.Call<int>(Hash.GET_AMMO_IN_PED_WEAPON, me.Handle, hash);
                var parts = Attachments.On(me, item.Id) ?? new List<string>();

                _trunk.Guns.Add(item.Id + "|" + Math.Max(0, ammo) +
                                (parts.Count > 0 ? "|" + string.Join("|", parts.ToArray()) : ""));

                Function.Call(Hash.REMOVE_WEAPON_FROM_PED, me.Handle, hash);
                _locker?.Stowed(item.Id);
                return true;
            }

            var saved = GunRow(item.Id);
            if (saved == null) return false;

            var bits = saved.Split('|');
            var rounds = 0;
            if (bits.Length > 1) int.TryParse(bits[1], out rounds);

            var back = new List<string>();
            for (var i = 2; i < bits.Length; i++) if (!string.IsNullOrEmpty(bits[i])) back.Add(bits[i]);

            Function.Call(Hash.GIVE_WEAPON_TO_PED, me.Handle, hash, Math.Max(0, rounds), false, false);

            // The locker's order: what was on it first, the big magazine only into an empty slot.
            Attachments.GiveTo(me, item.Id, back);

            var chose = false;
            foreach (var p in back) if (p.IndexOf("_CLIP_", StringComparison.OrdinalIgnoreCase) >= 0) chose = true;
            if (!chose) ExtendedClips.GiveTo(me, item.Id);

            _trunk.Guns.Remove(saved);
            _locker?.Bought(item.Id);
            return true;
        }

        private bool MoveFood(Item item, bool inward, bool everything)
        {
            int boot;
            _trunk.Food.TryGetValue(item.Id, out boot);

            if (inward)
            {
                var you = Pantry.CountOf(item.Id);
                if (you <= 0 || !Pantry.CanTake) return false;

                var room = Trunk.BootFood - _trunk.FoodCount;
                var want = Math.Min(everything ? you : 1, room);
                if (want <= 0) return false;

                if (!Pantry.Take(item.Id, want)) return false;
                _trunk.Food[item.Id] = boot + want;
                return true;
            }

            if (boot <= 0) return false;

            // One at a time out, so a full bag stops cleanly with the rest still in the boot.
            var moved = 0;
            var tries = everything ? boot : 1;
            for (var i = 0; i < tries; i++)
            {
                if (!Pantry.Give(item.Id, 1)) break;
                moved++;
            }

            if (moved == 0) return false;

            var left = boot - moved;
            if (left > 0) _trunk.Food[item.Id] = left; else _trunk.Food.Remove(item.Id);
            return true;
        }

        // ---- drawing --------------------------------------------------------------------

        /// <summary>How many rows the grids are this frame: the fuller side, floored at two, capped.</summary>
        private int Shown()
        {
            var most = Math.Max(_mine.Count, _boot.Count);

            var rows = (most + Columns - 1) / Columns;

            if (rows < 2) rows = 2;
            if (rows > MaxRows) rows = MaxRows;

            return rows;
        }

        /// <summary>
        /// The panel: the rounded black, the letterhead, a caption over each pane, the two
        /// grids, a card naming the chosen thing, and the keys as caps. Measured first,
        /// drawn second, because the panel is the ground.
        /// </summary>
        public void Draw()
        {
            if (!IsOpen) return;

            var rows = Shown();

            var tileH = TileH;
            var tileW = Hud.ToX(TileH);

            // The panel is the width of its two panes, not the other way round.
            var paneW = tileW * Columns;
            var wide = paneW * 2f + Gutter;
            var pad = Hud.ToX(PadH);
            var panelW = wide + pad * 2f;

            var height = UiKit.HeadH + CapH + rows * tileH + GridPad + CardH + UiKit.FootH;

            var left = 0.5f - panelW * 0.5f;
            var top = BottomY - height + _curtain.Lift;

            // Up and in, eased out so it slows as it lands, the same as every other panel.
            var age = Game.GameTime - _openedAt;
            var arrive = age >= EnterMs ? 1f : age / (float)EnterMs;
            arrive = 1f - (1f - arrive) * (1f - arrive);
            top += EnterRise * (1f - arrive);

            Theme.Panel(left, top, panelW, height, arrive);

            var x = left + pad;
            var right = left + panelW - pad;

            var y = UiKit.Head(left, top, panelW, pad, "car.png", "THE BOOT",
                               "what is on you, and what is in the car",
                               _carName.ToUpperInvariant(), arrive);

            _glide.Begin();

            var bootX = x + paneW + Gutter;

            Caption(x, y, paneW, "ON YOU", 0, Palette.Brand, arrive);
            Caption(bootX, y, paneW, "IN THE BOOT", 1, Palette.Standing, arrive);

            y += CapH;

            Pane(x, y, paneW, tileW, tileH, rows, 0, arrive);
            Pane(bootX, y, paneW, tileW, tileH, rows, 1, arrive);

            y += rows * tileH + GridPad;

            Card(x, y, wide, arrive);

            Foot(x, right, top + height - UiKit.FootH + 0.004f, arrive);

            // Last, so it rides over the tile it is pointing at.
            _glide.Draw(arrive);
        }

        /// <summary>
        /// A pane's name and figure, with a rule under them carrying the pane's own colour on
        /// its stroke: the brand green for you, the cold blue for the boot. The live side is
        /// lit and the other dimmed, which is what says where the cursor is when no tile is
        /// under it.
        /// </summary>
        private void Caption(float x, float y, float w, string label, int side, Color tint, float arrive)
        {
            var live = _side == side;

            Hud.Text(label, x, y + 0.003f, 0.28f,
                     Palette.Alpha(live ? tint : Palette.TextDim, (int)((live ? 245f : 170f) * arrive)),
                     Hud.FontLabel, centre: false);

            bool full;
            var figure = Figure(side, out full);

            Hud.TextRight(figure, x + w, y + 0.004f, 0.25f,
                          Palette.Alpha(full ? Palette.Warn : Palette.TextDim, (int)(215f * arrive)),
                          Hud.FontLabel);

            var ry = y + CapH - 0.005f;

            Hud.RectFrom(x, ry, w, 0.0012f, Palette.Alpha(Theme.Hairline, (int)(Theme.Hairline.A * arrive)));
            Hud.RectFrom(x, ry - 0.0004f, w * 0.14f, 0.0020f, Palette.Alpha(tint, (int)(215f * arrive)));
        }

        /// <summary>
        /// What a side holds of the KIND OF THING UNDER THE CURSOR, against what it can. The
        /// grid mixes three kinds with three different limits -- grams, guns, food -- and one
        /// figure for all of them would be a sentence; the one that matters is the one for
        /// the thing you are about to move.
        /// </summary>
        private string Figure(int side, out bool full)
        {
            full = false;

            var picked = Picked();
            var kind = picked == null ? Kind.Product : picked.Kind;

            if (kind == Kind.Product)
            {
                var stash = side == 0 ? _pockets : _trunk.Stash;
                full = stash.FreeSpace <= 0.5f;
                return Grams(stash.Total) + " OF " + Grams(stash.Capacity);
            }

            if (kind == Kind.Gun)
            {
                if (side == 0)
                {
                    var n = 0;
                    foreach (var item in _mine) if (item.Kind == Kind.Gun) n++;
                    return n + (n == 1 ? " GUN" : " GUNS");
                }

                full = _trunk.Guns.Count >= Trunk.BootGuns;
                return _trunk.Guns.Count + " OF " + Trunk.BootGuns;
            }

            if (side == 0)
            {
                if (Larder.Present && Larder.Slots > 0)
                {
                    full = Larder.Total >= Larder.Slots;
                    return Larder.Total + " OF " + Larder.Slots;
                }

                var n = 0;
                foreach (var item in _mine) if (item.Kind == Kind.Food) n += (int)item.You;
                return n + " FOOD";
            }

            full = _trunk.FoodCount >= Trunk.BootFood;
            return _trunk.FoodCount + " OF " + Trunk.BootFood;
        }

        private void Pane(float x, float y, float w, float tileW, float tileH, int rows, int side, float arrive)
        {
            var list = List(side);

            if (list.Count == 0)
            {
                Hud.Text(side == 0 ? "Nothing on you." : "The boot is empty.",
                         x + w / 2f, y + rows * tileH / 2f - 0.012f, 0.30f,
                         Palette.Alpha(Palette.TextDim, (int)(180f * arrive)), Hud.FontBody, centre: true);
                return;
            }

            var perPage = Columns * rows;
            var first = _page[side] * perPage;

            var age = Game.GameTime - _openedAt;
            var grown = Theme.Grown(_pickedAt);

            var selected = Slot(_side, _index[_side]);

            for (var i = 0; i < perPage; i++)
            {
                var at = first + i;
                if (at >= list.Count) break;

                var col = i % Columns;
                var row = i / Columns;

                // The boot unpacks a beat after you do, so the two sides arrive as two things.
                var land = UiKit.Landed(age, i * 35 + side * 40, EnterMs);

                var show = arrive * land;
                if (show <= 0.01f) continue;

                var tx = x + col * tileW;
                var ty = y + row * tileH + EnterRise * 0.5f * (1f - land);

                var here = side == _side && at == _index[side];
                var lit = Theme.Lit(Slot(side, at), selected, _last, grown);

                Square(list[at], side, tx, ty, tileW, tileH, lit, show, i, here, grown);

                if (here)
                {
                    _glide.Target(tx + Gap, ty + Gap * Hud.Aspect,
                                  tileW - Gap * 2f, tileH - Gap * 2f * Hud.Aspect);
                }
            }

            // Which page, only when there is more than one; the grid on its own has no way
            // at all of saying so.
            var pages = (list.Count + perPage - 1) / perPage;
            if (pages <= 1) return;

            Hud.TextRight((_page[side] + 1) + " / " + pages, x + w - 0.004f, y + rows * tileH - 0.018f,
                          0.22f, Palette.Alpha(Palette.TextDim, (int)(190f * arrive)), Hud.FontLabel);
        }

        /// <summary>
        /// One square: the dark ground, the plate coming up under the cursor, the thing's own
        /// picture -- a bag, a gun, a sandwich -- with how much of it on a chip, and a flash
        /// of the cash green over a tile something just arrived on. The same tile the pocket
        /// and Bare Minimum's fridge draw.
        /// </summary>
        private void Square(Item item, int side, float x, float y, float w, float h, float lit, float show,
                            int slot, bool picked, float grown)
        {
            var gx = Gap;
            var gy = Gap * Hud.Aspect;

            var tx = x + gx;
            var ty = y + gy;
            var tw = w - gx * 2f;
            var th = h - gy * 2f;

            // The dark ground, and the plate coming up under the cursor. No hairline along the
            // top and no light crossing it: at tile size those were chrome, not information.
            Hud.RectFrom(tx, ty, tw, th, Color.FromArgb((int)(26f * show), 255, 255, 255));

            Theme.Plate(tx, ty, tw, th, lit * show);

            var flash = _movedKey != null && _movedSide == side && Key(item) == _movedKey
                ? UiKit.Flash(_movedAt, MovedFlashMs) : 0f;

            if (flash > 0f)
            {
                Hud.RectFrom(tx, ty, tw, th, Palette.Alpha(Palette.Cash, (int)(150f * flash * show)));
            }

            var bright = flash > 0f ? 1f : lit;
            var swell = picked ? 1f + PickGrow * grown : 1f;

            var cx = tx + tw / 2f;
            var cy = ty + th * 0.44f;

            // ---- the picture ----
            if (item.Kind == Kind.Product)
            {
                var art = Icons.ForDrug(item.Id);

                if (art.HasFile)
                {
                    Hud.File(art.File, cx, cy, th * 0.52f * swell, 0f,
                             Theme.Ink(Palette.Alpha(Palette.Text, (int)(215f * show)), bright));
                }

                // How cut it is, in the corner, the mark the pocket uses.
                var purity = item.PurityOn(side);

                if (purity > 0f)
                {
                    Hud.File(Stash.Mark(purity), tx + tw - Hud.ToX(0.008f), ty + 0.008f, 0.010f, 0f,
                             Theme.Ink(Palette.Alpha(Palette.TextDim, (int)(210f * show)), bright));
                }
            }
            else if (item.Kind == Kind.Gun)
            {
                if (!string.IsNullOrEmpty(item.Icon))
                {
                    GunArt.Draw(item.Icon, cx, cy, tw * 0.86f * swell, th * 0.40f * swell,
                                Theme.Ink(Palette.Alpha(Palette.Text, (int)(225f * show)), bright));
                }
            }
            else if (!string.IsNullOrEmpty(item.Icon))
            {
                // The item's own colour on the dark tile, brightening toward white as the
                // plate comes up. The art is white and the sprite multiplies, so one file
                // does all of it.
                Hud.File(item.Icon, cx, cy, th * 0.56f * swell, 0f,
                         Theme.Ink(Palette.Alpha(item.Tint, (int)(238f * show)), bright));
            }

            // ---- how much, on a chip in the corner ----
            //
            // Sized to the figure: "1" and "57.5g" and "48 RDS" are one, five and six
            // characters, and a fixed box is too wide round the first and a smudge round
            // the last.
            var tag = item.TagOn(side);

            const float chipH = 0.013f;

            var chipW = Hud.MeasureText(tag, 0.22f, Hud.FontLabel) + Hud.ToX(0.007f);

            Hud.RectFrom(tx + tw - chipW, ty + th - chipH, chipW, chipH,
                         Color.FromArgb((int)(215f * show), 12, 13, 15));

            Hud.TextRight(tag, tx + tw - 0.0022f, ty + th - chipH + 0.0005f, 0.22f,
                          Palette.Alpha(Palette.Text, (int)(255f * show)), Hud.FontLabel);
        }

        /// <summary>
        /// The card under the grids: a tile is a glance, this is where it becomes words. The
        /// name slides in from the left as the plate comes up under the new tile, with what
        /// kind of thing it is on a tag after it, this side's figure on the right, and one
        /// line about it under.
        /// </summary>
        private void Card(float x, float y, float wide, float arrive)
        {
            // The same plate every chosen thing sits on, faint, so the name and the line under
            // it read as one card rather than two lines of text loose on the panel.
            Theme.Plate(x, y, wide, CardH - 0.006f, 0.55f * arrive);

            var tx = x + 0.010f;

            var item = Picked();

            if (item == null)
            {
                var why = Pantry.Present && !Pantry.CanTake
                    ? "Bare Minimum needs updating before food can go in."
                    : "Nothing on you and nothing in the boot.";

                Hud.Text(why, tx, y + 0.008f, 0.30f,
                         Palette.Alpha(Palette.TextDim, (int)(210f * arrive)), Hud.FontBody, centre: false);

                Hud.Text("Product, guns and food all go in, and come back out the same.", tx, y + 0.030f, 0.25f,
                         Palette.Alpha(Palette.TextDim, (int)(170f * arrive)), Hud.FontBody, centre: false);
                return;
            }

            var grown = Theme.Grown(_pickedAt);

            var name = item.Kind == Kind.Product && !item.Bagged ? item.Name + "  (weight)" : item.Name;

            Theme.Caption(name, tx, y + 0.008f, grown, 0.32f);

            // WHAT KIND OF THING, on a tag after the name. The grid mixes three kinds and a
            // bag of crack and a bag of crisps are the same size at tile size.
            var after = tx + Hud.ToX(0.010f) * (1f - grown) + Hud.MeasureText(name, 0.32f, Hud.FontBody) + 0.008f;

            var kindWord = item.Kind == Kind.Product ? "PRODUCT" : item.Kind == Kind.Gun ? "GUN" : "FOOD";

            UiKit.Tag(after, y + 0.0105f, kindWord, Palette.TextDim, arrive * (0.6f + 0.4f * grown));

            // HOW MUCH OF IT IS ON THIS SIDE, on the right, in the side's own colour.
            var tint = _side == 0 ? Palette.Brand : Palette.Standing;
            var figure = item.TagOn(_side);

            Hud.TextRight(figure, x + wide - 0.004f, y + 0.010f, 0.26f,
                          Palette.Alpha(tint, (int)((150f + 105f * grown) * arrive)), Hud.FontLabel);

            // ---- one line about it ----
            var line = About(item);

            if (!string.IsNullOrEmpty(line))
            {
                Hud.Text(Hud.Fit(line, wide - 0.014f, 0.25f, Hud.FontBody), tx, y + 0.031f, 0.25f,
                         Palette.Alpha(Palette.TextDim, (int)((110f + 90f * grown) * arrive)),
                         Hud.FontBody, centre: false);
            }
        }

        /// <summary>The one line under the name: how cut it is, what is on the gun, what it is.</summary>
        private string About(Item item)
        {
            try
            {
                if (item.Kind == Kind.Product)
                {
                    var purity = item.PurityOn(_side);
                    if (purity <= 0f) return item.Bagged ? "Bagged up, ready to sell." : "Uncut weight, for the kitchen.";

                    return UiKit.PurityWord(purity) + " -- " + Math.Round(purity * 100f) + "% pure, "
                           + (item.Bagged ? "bagged up." : "uncut weight.");
                }

                if (item.Kind == Kind.Gun)
                {
                    int parts;

                    if (_side == 0)
                    {
                        var me = Game.Player.Character;
                        var on = me != null && me.Exists() ? Attachments.On(me, item.Id) : null;
                        parts = on == null ? 0 : on.Count;
                    }
                    else
                    {
                        parts = SavedParts(GunRow(item.Id));
                    }

                    var what = parts == 0 ? "Nothing bolted on it." : parts == 1 ? "One part on it." : parts + " parts on it.";

                    return what + (_side == 0 ? " Goes in with its rounds and its parts."
                                              : " Comes out the way it went in.");
                }

                return _side == 0 ? "Keeps in the boot for as long as you like." : "Comes back to your pocket.";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// The keys as caps. WHICH WAY SPACE MOVES THINGS IS SPELLED OUT rather than left to an
        /// arrow, because it is the one thing about this screen that is not obvious from
        /// looking at it -- and it changes with the side the cursor is on.
        /// </summary>
        private void Foot(float x, float right, float y, float arrive)
        {
            Theme.Rule(x, y, right - x, arrive);

            var ky = y + 0.011f;

            UiKit.KeyRight(right, ky, UiKit.Back, "SHUT IT", arrive);

            var item = Picked();
            if (item == null) return;

            var kx = UiKit.Key(x, ky, null, "arrow_leftright.png", "PICK", arrive);

            kx = UiKit.Key(kx, ky, UiKit.Drop, null, _side == 0 ? "PUT IT IN" : "TAKE IT OUT", arrive);

            if (item.Kind != Kind.Gun) kx = UiKit.Key(kx, ky, UiKit.All, null, "THE LOT", arrive);

            var perPage = Columns * Shown();
            var pages = perPage <= 0 ? 1 : (Count(_side) + perPage - 1) / perPage;

            if (pages > 1) UiKit.Key(kx, ky, Hud.OnPad ? "LB / RB" : "Q / E", null, "PAGE", arrive);
        }

        private static string Grams(float g) =>
            g >= 1000f ? (g / 1000f).ToString("0.##") + "KG" : g.ToString("0") + "G";

        private static int Clamp(int v, int lo, int hi)
        {
            return v < lo ? lo : v > hi ? hi : v;
        }

        // ---- controls -------------------------------------------------------------------

        private static bool Pressed(Control control)
        {
            return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)control);
        }

        private static bool Held(Control control)
        {
            return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)control);
        }

        /// <summary>
        /// Everything off, the camera back on. The same lock the car screens use, because this
        /// one can be up in a car: the house's targeted list left the d-pad and the arrow
        /// keys doing their in-car jobs underneath the screen, the radio wheel for one.
        /// Everything the screen reads, it reads through the disabled path.
        /// </summary>
        private static void LockControls()
        {
            Core.Fists.Off();

            Function.Call(Hash.DISABLE_ALL_CONTROL_ACTIONS, 0);

            foreach (var control in new[] { Control.LookLeftRight, Control.LookUpDown })
            {
                Function.Call(Hash.ENABLE_CONTROL_ACTION, 0, (int)control, true);
            }
        }
    }
}
