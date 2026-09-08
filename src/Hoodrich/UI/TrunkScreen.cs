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
    /// THREE PAGES, ONE RULE. Product, guns and food, and on every page right puts a thing in
    /// the boot and left takes it back out -- the same two columns and the same two arrows
    /// as the house, so nobody has to learn it twice. The product page is the stash screen's
    /// own move against a second Stash; the guns page uses the locker's row format so a gun
    /// comes out with the magazine and the parts it went in with; the food page is a bag of
    /// counts talking to Bare Minimum through Core.Pantry, and it is simply not there when
    /// that mod is not.
    ///
    /// The screen does not know about the car. Locations.Boot hands it a Trunk and a way to
    /// say something changed, and takes both away again when the lid shuts.
    /// </summary>
    internal sealed class TrunkScreen
    {
        private enum Page { Product, Guns, Food }

        private sealed class Row
        {
            public string Id = "";
            public string Name = "";
            public DrugDef Drug;           // product only
            public bool Bagged;            // product only
            public string Icon = "";       // guns: the art pack name; food: a full path
            public float You, Boot;        // grams, or counts, or 0/1 for a gun
            public string YouTag = "", BootTag = "";
        }

        private const float PanelWidthH = 0.62f;
        private const float PadH = 0.024f;
        private const float RowH = 0.032f;
        private const float CapsH = 0.020f;
        private const float TabsH = 0.030f;
        private const float ArtSize = 0.019f;
        private const float StepGrams = 10f;
        private const int OpenGraceMs = 220;
        private const int RepeatMs = 110;
        private const int EnterMs = 170;
        private const float EnterRise = 0.014f;
        private const int MovedFlashMs = 520;

        /// <summary>Rows on the page at once. A list longer than this scrolls under the cursor.</summary>
        private const int Shown = 7;

        /// <summary>
        /// Where the bottom of the card rests, and how tall its own slim head is.
        ///
        /// STOOD ON THE BOTTOM OF THE SCREEN rather than centred, and without the wordmark
        /// the room screens carry. This is a card about the car, the car is what the camera
        /// is looking at, and a panel in the middle of the screen sat across the boot he
        /// was leaning into.
        /// </summary>
        private const float BottomY = 0.945f;
        private const float HeadH = 0.040f;
        private const float HeadIcon = 0.017f;

        private readonly Curtain _curtain = new Curtain();
        private readonly Glide _glide = new Glide();
        private readonly List<Row> _rows = new List<Row>();

        private Trunk _trunk;
        private Stash _pockets;
        private Drugs _drugs;
        private WeaponRegistry _guns;
        private GunLocker _locker;
        private Action _changed;
        private string _carName = "";

        private Page _page;
        private int _selected, _lastSelected = -1, _top;
        private int _openedAt, _pickedAt, _nextRepeat;
        private int _movedAt, _movedRow = -1;
        private bool _movedIn;

        public bool IsOpen => _curtain.Showing;

        public void Open(Trunk trunk, Stash pockets, Drugs drugs, WeaponRegistry guns, GunLocker locker,
                         string carName, Action changed)
        {
            if (trunk == null || pockets == null || drugs == null) return;

            _trunk = trunk; _pockets = pockets; _drugs = drugs; _guns = guns; _locker = locker;
            _changed = changed; _carName = carName ?? "";

            _page = Page.Product;
            _selected = 0; _lastSelected = -1; _top = 0;
            _movedRow = -1;
            _openedAt = _pickedAt = Game.GameTime;
            _glide.Reset();
            _curtain.Open();
            Rebuild();
            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        public void Close()
        {
            if (IsOpen) InputGuard.Swallow();
            _curtain.Close();
            _trunk = null; _pockets = null; _drugs = null; _guns = null; _locker = null; _changed = null;
            _rows.Clear();
        }

        // ---- the rows -------------------------------------------------------------------

        /// <summary>
        /// Rebuilds the page from both sides. A thing appears if it is on either side, so the
        /// last of something moved across does not vanish from under the cursor.
        /// </summary>
        private void Rebuild()
        {
            var keep = _selected >= 0 && _selected < _rows.Count ? Key(_rows[_selected]) : null;
            _rows.Clear();

            if (_trunk == null) return;

            if (_page == Page.Product) BuildProduct();
            else if (_page == Page.Guns) BuildGuns();
            else BuildFood();

            if (keep != null)
            {
                for (var i = 0; i < _rows.Count; i++)
                {
                    if (Key(_rows[i]) != keep) continue;
                    _selected = i;
                    Scroll();
                    return;
                }
            }

            _selected = Math.Min(_selected, Math.Max(0, _rows.Count - 1));
            Scroll();
        }

        private static string Key(Row row) => row.Id + (row.Bagged ? "|b" : "|w");

        private void BuildProduct()
        {
            foreach (var drug in _drugs.All)
            {
                AddProduct(drug, true);
                AddProduct(drug, false);
            }
        }

        private void AddProduct(DrugDef drug, bool bagged)
        {
            var you = bagged ? _pockets.PackagedOf(drug.Id) : _pockets.BulkOf(drug.Id);
            var boot = bagged ? _trunk.Stash.PackagedOf(drug.Id) : _trunk.Stash.BulkOf(drug.Id);
            if (you <= 0.005f && boot <= 0.005f) return;

            _rows.Add(new Row
            {
                Id = drug.Id, Name = drug.Name, Drug = drug, Bagged = bagged,
                You = you, Boot = boot,
                YouTag = Amount(drug, you, bagged), BootTag = Amount(drug, boot, bagged)
            });
        }

        private void BuildGuns()
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
                    var inBoot = GunRow(def.Id) != null;
                    if (!has && !inBoot) continue;

                    seen.Add(def.Id);
                    _rows.Add(new Row
                    {
                        Id = def.Id, Name = def.Name, Icon = def.Icon ?? "",
                        You = has ? 1 : 0, Boot = inBoot ? 1 : 0,
                        YouTag = has ? Rounds(me, def.Hash) : "-",
                        BootTag = inBoot ? "IN" : "-"
                    });
                }
            }

            // A gun in the boot the registry does not know is kept listed so it can come out.
            foreach (var row in _trunk.Guns)
            {
                var id = (row ?? "").Split('|')[0];
                if (id.Length == 0 || seen.Contains(id)) continue;
                seen.Add(id);
                _rows.Add(new Row { Id = id, Name = Named(id), Boot = 1, YouTag = "-", BootTag = "IN" });
            }
        }

        private void BuildFood()
        {
            var ids = new List<string>(Pantry.Ids());
            foreach (var k in _trunk.Food.Keys) if (!ids.Contains(k)) ids.Add(k);

            foreach (var id in ids)
            {
                var you = Pantry.CountOf(id);
                int boot;
                _trunk.Food.TryGetValue(id, out boot);
                if (you <= 0 && boot <= 0) continue;

                _rows.Add(new Row
                {
                    Id = id, Name = Pantry.NameOf(id), Icon = Pantry.IconOf(id),
                    You = you, Boot = boot,
                    YouTag = you > 0 ? you.ToString() : "-", BootTag = boot > 0 ? boot.ToString() : "-"
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
                return n > 0 ? n + " RDS" : "ON YOU";
            }
            catch { return "ON YOU"; }
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

            if (Pressed(Control.PhoneCancel))
            {
                Hud.PlaySound("BACK", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                Close();
                return;
            }

            // Q and Tab on a keyboard, the bumpers on a pad -- the two keys either side of
            // the ones that move things.
            if (Pressed(Control.Cover)) { Flip(1); return; }
            if (Pressed(Control.SelectWeapon)) { Flip(-1); return; }

            if (_rows.Count == 0) return;

            if (Pressed(Control.PhoneUp)) Move(-1);
            else if (Pressed(Control.PhoneDown)) Move(1);

            if (Game.GameTime < _nextRepeat) return;

            // Through the DISABLED path, the same as the house: a held sprint moves the lot.
            var all = Held(Control.Sprint) || Held(Control.Jump);

            if (Held(Control.PhoneRight)) Transfer(true, all);
            else if (Held(Control.PhoneLeft)) Transfer(false, all);
        }

        private void Flip(int by)
        {
            var pages = Pantry.Present ? 3 : 2;
            _page = (Page)(((int)_page + by + pages) % pages);
            _selected = 0; _lastSelected = -1; _top = 0; _movedRow = -1;
            _pickedAt = Game.GameTime;
            _glide.Reset();
            Rebuild();
            Hud.PlaySound("NAV_LEFT_RIGHT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        private void Move(int step)
        {
            var before = _selected;
            _selected = (_selected + step + _rows.Count) % _rows.Count;

            if (_selected != before)
            {
                _lastSelected = before;
                _pickedAt = Game.GameTime;
            }

            Scroll();
            Hud.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        private void Scroll()
        {
            if (_selected < _top) _top = _selected;
            if (_selected >= _top + Shown) _top = _selected - Shown + 1;
            if (_top < 0) _top = 0;
        }

        private void Transfer(bool intoBoot, bool everything)
        {
            if (_selected < 0 || _selected >= _rows.Count || _trunk == null) return;

            var row = _rows[_selected];
            var moved = false;

            try
            {
                if (_page == Page.Product) moved = MoveProduct(row, intoBoot, everything);
                else if (_page == Page.Guns) moved = MoveGun(row, intoBoot);
                else moved = MoveFood(row, intoBoot, everything);
            }
            catch (Exception ex)
            {
                Log.Debug("Boot: could not move " + row.Id + ": " + ex.Message);
            }

            if (!moved)
            {
                Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                _nextRepeat = Game.GameTime + RepeatMs * 3;
                return;
            }

            _nextRepeat = Game.GameTime + RepeatMs;
            Hud.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");

            _movedAt = Game.GameTime;
            _movedRow = _selected;
            _movedIn = intoBoot;

            _changed?.Invoke();
            Rebuild();
        }

        /// <summary>
        /// Product, the way the house does it: the far side is asked first how much it will
        /// take, and only that much leaves the near side, so nothing is ever lost to a full
        /// boot.
        /// </summary>
        private bool MoveProduct(Row row, bool inward, bool everything)
        {
            var from = inward ? _pockets : _trunk.Stash;
            var to = inward ? _trunk.Stash : _pockets;

            var available = row.Bagged ? from.PackagedOf(row.Id) : from.BulkOf(row.Id);
            if (available <= 0.005f) return false;

            var want = everything ? available : Math.Min(StepGrams, available);

            if (row.Bagged)
            {
                var accepted = to.AddPackaged(row.Id, want, from.PurityOf(row.Id));
                if (accepted <= 0.005f) return false;

                var taken = from.RemovePackaged(row.Id, accepted);
                if (taken < accepted - 0.005f) to.RemovePackaged(row.Id, accepted - taken);
                return taken > 0.005f;
            }

            var acceptedBulk = to.AddBulk(row.Id, want, from.BulkPurityOf(row.Id));
            if (acceptedBulk <= 0.005f) return false;

            var takenBulk = from.RemoveBulk(row.Id, acceptedBulk);
            if (takenBulk < acceptedBulk - 0.005f) to.RemoveBulk(row.Id, acceptedBulk - takenBulk);
            return takenBulk > 0.005f;
        }

        /// <summary>
        /// A gun into the boot takes its magazine, its parts and its rounds with it, and comes
        /// back with all three. The locker is told either way, or it would hand the gun back
        /// after the next death as though the boot were a grave.
        /// </summary>
        private bool MoveGun(Row row, bool inward)
        {
            var me = Game.Player.Character;
            if (me == null || !me.Exists()) return false;

            var hash = Function.Call<uint>(Hash.GET_HASH_KEY, row.Id);
            if (hash == 0) return false;

            if (inward)
            {
                if (row.You <= 0 || _trunk.Guns.Count >= Trunk.BootGuns) return false;
                if (GunRow(row.Id) != null) return false;
                if (!Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON, me.Handle, hash, false)) return false;

                var ammo = Function.Call<int>(Hash.GET_AMMO_IN_PED_WEAPON, me.Handle, hash);
                var parts = Attachments.On(me, row.Id) ?? new List<string>();

                _trunk.Guns.Add(row.Id + "|" + Math.Max(0, ammo) +
                                (parts.Count > 0 ? "|" + string.Join("|", parts.ToArray()) : ""));

                Function.Call(Hash.REMOVE_WEAPON_FROM_PED, me.Handle, hash);
                _locker?.Stowed(row.Id);
                return true;
            }

            var saved = GunRow(row.Id);
            if (saved == null) return false;

            var bits = saved.Split('|');
            var rounds = 0;
            if (bits.Length > 1) int.TryParse(bits[1], out rounds);

            var back = new List<string>();
            for (var i = 2; i < bits.Length; i++) if (!string.IsNullOrEmpty(bits[i])) back.Add(bits[i]);

            Function.Call(Hash.GIVE_WEAPON_TO_PED, me.Handle, hash, Math.Max(0, rounds), false, false);

            // The locker's order: what was on it first, the big magazine only into an empty slot.
            Attachments.GiveTo(me, row.Id, back);

            var chose = false;
            foreach (var p in back) if (p.IndexOf("_CLIP_", StringComparison.OrdinalIgnoreCase) >= 0) chose = true;
            if (!chose) ExtendedClips.GiveTo(me, row.Id);

            _trunk.Guns.Remove(saved);
            _locker?.Bought(row.Id);
            return true;
        }

        private bool MoveFood(Row row, bool inward, bool everything)
        {
            int boot;
            _trunk.Food.TryGetValue(row.Id, out boot);

            if (inward)
            {
                var you = Pantry.CountOf(row.Id);
                if (you <= 0 || !Pantry.CanTake) return false;

                var room = Trunk.BootFood - _trunk.FoodCount;
                var want = Math.Min(everything ? you : 1, room);
                if (want <= 0) return false;

                if (!Pantry.Take(row.Id, want)) return false;
                _trunk.Food[row.Id] = boot + want;
                return true;
            }

            if (boot <= 0) return false;

            // One at a time out, so a full bag stops cleanly with the rest still in the boot.
            var moved = 0;
            var tries = everything ? boot : 1;
            for (var i = 0; i < tries; i++)
            {
                if (!Pantry.Give(row.Id, 1)) break;
                moved++;
            }

            if (moved == 0) return false;

            var left = boot - moved;
            if (left > 0) _trunk.Food[row.Id] = left; else _trunk.Food.Remove(row.Id);
            return true;
        }

        // ---- drawing --------------------------------------------------------------------

        public void Draw()
        {
            if (!IsOpen) return;

            var shown = Math.Min(Math.Max(_rows.Count, 1), Shown);
            var height = HeadH + TabsH + CapsH + shown * RowH + 0.006f + UiKit.FootH;

            var panelWidth = Hud.ToX(PanelWidthH);
            var pad = Hud.ToX(PadH);

            var left = 0.5f - panelWidth * 0.5f;
            var top = BottomY - height + _curtain.Lift;

            // Up and in, eased out so it slows as it lands, the same as every other panel.
            var age = Game.GameTime - _openedAt;
            var arrive = age >= EnterMs ? 1f : age / (float)EnterMs;
            arrive = 1f - (1f - arrive) * (1f - arrive);
            top += EnterRise * (1f - arrive);

            Theme.Panel(left, top, panelWidth, height, arrive);

            var x = left + pad;
            var right = left + panelWidth - pad;
            var wide = right - x;
            var caps = Palette.Alpha(Palette.TextDim, (int)(190f * arrive));

            // ---- the head: the mark, the name of the thing, whose car ----
            var y = top + 0.010f;
            var tx = x;

            if (Hud.File("car.png", x + Hud.ToX(HeadIcon) * 0.5f, y + 0.0085f, HeadIcon, 0f,
                         Palette.Alpha(Palette.Brand, (int)(235f * arrive))))
            {
                tx = x + Hud.ToX(HeadIcon) + 0.006f;
            }

            Hud.Text("THE BOOT", tx, y, 0.31f, Palette.Alpha(Palette.Text, (int)(255f * arrive)),
                     Hud.FontLabel, centre: false);
            Hud.TextRight(_carName.ToUpperInvariant(), right, y + 0.002f, 0.25f,
                          Palette.Alpha(Palette.TextDim, (int)(200f * arrive)), Hud.FontLabel);

            Theme.Rule(x, top + HeadH - 0.004f, wide, arrive);
            y = top + HeadH;

            // ---- the pages, as tabs, and how much room this one has left ----
            var names = Pantry.Present ? new[] { "PRODUCT", "GUNS", "FOOD" } : new[] { "PRODUCT", "GUNS" };
            tx = x;

            for (var i = 0; i < names.Length; i++)
            {
                var here = (int)_page == i;
                var ink = Palette.Alpha(here ? Palette.Text : Palette.TextDim, (int)(255f * arrive));

                Hud.Text(names[i], tx, y + 0.003f, 0.27f, ink, Hud.FontLabel, centre: false);

                var w = Hud.MeasureText(names[i], 0.27f, Hud.FontLabel);
                if (here) Hud.RectFrom(tx, y + 0.0245f, w, 0.0022f, Palette.Alpha(Palette.Brand, (int)(220f * arrive)));

                tx += w + Hud.ToX(0.028f);
            }

            var room = _page == Page.Product ? Grams(_trunk.Stash.FreeSpace) + " ROOM"
                     : _page == Page.Guns ? (Trunk.BootGuns - _trunk.Guns.Count) + " OF " + Trunk.BootGuns + " FREE"
                     : (Trunk.BootFood - _trunk.FoodCount) + " OF " + Trunk.BootFood + " FREE";

            if (_rows.Count > Shown) room = (_selected + 1) + " / " + _rows.Count + "   \u00b7   " + room;

            Hud.TextRight(room, right, y + 0.005f, 0.22f, caps, Hud.FontLabel);

            y += TabsH;

            // ---- the two places ----
            var nameW = wide * 0.46f;
            var area = wide - nameW;
            var colW = area * 0.36f;

            var youX = x + nameW;
            var bootX = right - colW;
            var gapX = youX + colW;
            var gapW = bootX - gapX;

            var rowsH = shown * RowH;

            Hud.RectFrom(youX, y, colW, CapsH + rowsH, Color.FromArgb((int)(12f * arrive), 255, 255, 255));
            Hud.RectFrom(bootX, y, colW, CapsH + rowsH, Color.FromArgb((int)(12f * arrive), 255, 255, 255));

            Hud.Text(names[(int)_page], x, y + 0.002f, 0.22f, caps, Hud.FontLabel, centre: false);
            Hud.TextRight("ON YOU", youX + colW - 0.004f, y + 0.002f, 0.22f, caps, Hud.FontLabel);
            Hud.TextRight("IN THE BOOT", bootX + colW - 0.004f, y + 0.002f, 0.22f, caps, Hud.FontLabel);

            y += CapsH;

            if (_rows.Count == 0)
            {
                var why = _page == Page.Product ? "Nothing on you and nothing in the boot."
                        : _page == Page.Guns ? "No guns on you and none in the boot."
                        : Pantry.CanTake ? "Nothing to eat on you and nothing in the boot."
                        : "Bare Minimum needs updating before food can go in.";

                Hud.Text(why, x, y + 0.006f, 0.28f, Palette.Alpha(Palette.TextDim, (int)(255f * arrive)),
                         Hud.FontBody, centre: false);
            }

            var grown = Theme.Grown(_pickedAt);

            _glide.Begin();

            for (var i = _top; i < Math.Min(_rows.Count, _top + Shown); i++)
            {
                Line(_rows[i], i, grown, x, wide, pad, youX, bootX, colW, gapX, gapW, y, arrive);
                y += RowH;
            }

            // ---- the keys ----
            //
            // Four and the way out. Up and down need no telling; the two arrows are the
            // whole idea of the screen and the words after them are short enough to sit
            // beside the caps rather than run into the next one.
            var footY = top + height - UiKit.FootH + 0.004f;

            Theme.Rule(x, footY, wide, arrive);

            var ky = footY + 0.011f;

            UiKit.KeyRight(right, ky, UiKit.Back, "SHUT IT", arrive);

            var kx = UiKit.Key(x, ky, null, "arrow_left.png", "OUT", arrive);
            kx = UiKit.Key(kx, ky, null, "arrow_right.png", "IN", arrive);
            kx = UiKit.Key(kx, ky, UiKit.All, null, "THE LOT", arrive);
            UiKit.Key(kx, ky, Hud.OnPad ? "LB / RB" : "TAB / Q", null, "PAGE", arrive);

            _glide.Draw(arrive);
        }

        private void Line(Row row, int i, float grown, float x, float wide, float pad,
                          float youX, float bootX, float colW, float gapX, float gapW, float y,
                          float arrive)
        {
            var picked = i == _selected;
            var lit = Theme.Lit(i, _selected, _lastSelected, grown);

            var wash = x - pad * 0.35f;
            var wideRow = wide + pad * 0.7f;

            Theme.Plate(wash, y, wideRow, RowH, lit * arrive);
            Theme.Sheen(wash, y, wideRow, RowH, lit * arrive);

            if (picked) _glide.Target(wash, y, wideRow, RowH);

            var ink = Theme.Ink(Palette.Alpha(picked ? Palette.Text : Palette.TextDim, (int)(255f * arrive)), lit);

            var textY = y + 0.0055f;
            var midY = y + RowH * 0.5f;
            var tx = x;

            // ---- the art ----
            if (_page == Page.Product)
            {
                var art = Icons.ForDrug(row.Id);
                if (art.HasFile && Hud.File(art.File, x + Hud.ToX(ArtSize) * 0.5f, midY, ArtSize, 0f, ink))
                {
                    tx = x + Hud.ToX(ArtSize) + 0.007f;
                }
            }
            else if (_page == Page.Guns)
            {
                var slot = Hud.ToX(RowH * 1.7f);
                if (!string.IsNullOrEmpty(row.Icon) &&
                    GunArt.Draw(row.Icon, x + slot * 0.5f, midY, slot * 0.92f, RowH * 0.92f, ink))
                {
                    tx = x + slot + 0.006f;
                }
            }
            else if (!string.IsNullOrEmpty(row.Icon) &&
                     Hud.File(row.Icon, x + Hud.ToX(ArtSize) * 0.5f, midY, ArtSize, 0f, ink))
            {
                tx = x + Hud.ToX(ArtSize) + 0.007f;
            }

            // ---- the name, and a tag for raw weight ----
            var tagRoom = _page == Page.Product && !row.Bagged ? 0.042f : 0f;
            var name = Hud.Fit(row.Name, youX - 0.010f - tx - tagRoom, 0.30f, Hud.FontBody);

            Hud.Text(name, tx, textY, 0.30f, ink, Hud.FontBody, centre: false);

            if (tagRoom > 0f)
            {
                var after = tx + Hud.MeasureText(name, 0.30f, Hud.FontBody) + 0.008f;
                UiKit.Tag(after, textY + 0.0025f, "WEIGHT", Palette.TextDim, arrive * (0.75f + 0.25f * lit));
            }

            // ---- the two figures, and the side that just grew lit green ----
            var flash = _movedRow == i ? UiKit.Flash(_movedAt, MovedFlashMs) : 0f;

            Hud.TextRight(row.YouTag, youX + colW - 0.004f, textY, 0.30f,
                          Theme.Ink(Side(row.You, picked, flash > 0f && !_movedIn, arrive), lit), Hud.FontBody);

            Hud.TextRight(row.BootTag, bootX + colW - 0.004f, textY, 0.30f,
                          Theme.Ink(Side(row.Boot, picked, flash > 0f && _movedIn, arrive), lit), Hud.FontBody);

            if (flash > 0f)
            {
                UiKit.Flow(gapX, textY, gapW, _movedIn ? 1 : -1, flash, Palette.Cash);
            }
            else if (picked)
            {
                Hud.Text("<   >", gapX + gapW * 0.5f, textY, 0.28f,
                         Palette.Alpha(Palette.Brand, (int)(110f * grown * arrive)), Hud.FontBody, centre: true);
            }
        }

        private static Color Side(float held, bool picked, bool flashing, float arrive)
        {
            var c = held <= 0.005f ? Palette.TextDisabled
                  : flashing ? Palette.Cash
                  : picked ? Palette.Text : Palette.TextDim;

            return Palette.Alpha(c, (int)(c.A * arrive));
        }

        private static string Grams(float g) =>
            g >= 1000f ? (g / 1000f).ToString("0.##") + "KG" : g.ToString("0") + "G";

        // ---- controls -------------------------------------------------------------------

        private static bool Pressed(Control control)
        {
            return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)control);
        }

        private static bool Held(Control control)
        {
            return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)control);
        }

        private static void LockControls()
        {
            Core.Fists.Off();

            Game.DisableControlThisFrame(Control.Jump);
            Game.DisableControlThisFrame(Control.Sprint);
            Game.DisableControlThisFrame(Control.Enter);
            Game.DisableControlThisFrame(Control.Phone);
            Game.DisableControlThisFrame(Control.SelectWeapon);
            Game.DisableControlThisFrame(Control.Cover);
            Game.DisableControlThisFrame(Control.MoveLeftRight);
            Game.DisableControlThisFrame(Control.MoveUpDown);
            Game.DisableControlThisFrame(Control.Attack);
            Game.DisableControlThisFrame(Control.Aim);
            Game.DisableControlThisFrame(Control.VehicleExit);
            Game.DisableControlThisFrame(Control.VehicleAccelerate);
            Game.DisableControlThisFrame(Control.VehicleBrake);
            Game.DisableControlThisFrame(Control.VehicleHorn);

            Game.DisableControlThisFrame(Control.PhoneUp);
            Game.DisableControlThisFrame(Control.PhoneDown);
            Game.DisableControlThisFrame(Control.PhoneLeft);
            Game.DisableControlThisFrame(Control.PhoneRight);
            Game.DisableControlThisFrame(Control.PhoneSelect);
            Game.DisableControlThisFrame(Control.PhoneCancel);
        }
    }
}
