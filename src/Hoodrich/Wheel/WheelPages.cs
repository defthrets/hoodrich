using System;
using System.Collections.Generic;
using GTA;
using Hoodrich.Core;
using Hoodrich.Dealing;
using Hoodrich.Economy;
using Hoodrich.Gangs;
using Hoodrich.Locations;
using Hoodrich.State;
using Hoodrich.Supply;
using Hoodrich.Territory;
using Hoodrich.UI;
using Hoodrich.Weapons;

namespace Hoodrich.Wheel
{
    /// <summary>
    /// Builds wheel pages from live game state.
    ///
    /// Pages are rebuilt every time the wheel opens rather than cached, so prices, stock, turf
    /// and rank gating are always current. Items the player cannot use are shown disabled with
    /// a reason rather than hidden, so segment positions never move -- muscle memory is the
    /// entire point of a radial menu.
    /// </summary>
    internal sealed class WheelPages
    {
        /// <summary>Grams moved in a single hand-to-hand sale.</summary>
        private const float DealSize = 5f;

        /// <summary>Purity options offered when cutting, high to low.</summary>
        private static readonly float[] PurityOptions = { 1.0f, 0.75f, 0.5f, 0.33f };

        private readonly PlayerState _state;
        private readonly Drugs _drugs;
        private readonly Pricing _pricing;
        private readonly StreetDeal _deal;
        private readonly Cutting _cutting;
        private readonly GangRegistry _gangs;
        private readonly Affiliation _crew;
        private readonly TurfWatch _turf;
        private readonly DealerManager _dealers;
        private readonly Core.Settings _cfg;
        private readonly Market _market;
        private readonly TurfWar _war;
        private readonly HideoutManager _hideouts;
        private readonly PostUp _postUp;
        private readonly WeaponRegistry _weapons;

        /// <summary>Set by Main. Hands the player back the game's own weapon wheel.</summary>
        public Action ShowVanillaWheel;

        public WheelPages(Core.Settings cfg, PlayerState state, Drugs drugs, Pricing pricing, StreetDeal deal,
                          Cutting cutting, GangRegistry gangs, Affiliation crew, TurfWatch turf,
                          DealerManager suppliers, WeaponRegistry weapons, Market market,
                          TurfWar war, HideoutManager hideouts, PostUp postUp)
        {
            _cfg = cfg;
            _state = state;
            _drugs = drugs;
            _pricing = pricing;
            _deal = deal;
            _cutting = cutting;
            _gangs = gangs;
            _crew = crew;
            _turf = turf;
            _dealers = suppliers;
            _weapons = weapons;
            _market = market;
            _war = war;
            _hideouts = hideouts;
            _postUp = postUp;
        }

        private Stash Stash => _state.Stash;

        // ---- root --------------------------------------------------------------

        public WheelPage BuildRoot()
        {
            // Opening the wheel is the cue to start streaming weapon art, so it is resident by
            // the time the player flicks into the weapons page rather than popping in under them.
            _weapons.PrewarmCarried();

            var packaged = Stash.TotalPackaged;
            var bulk = Stash.TotalBulk;

            var page = new WheelPage("Hoodrich",
                _crew.IsAffiliated ? _crew.Current.Name : "Unaffiliated");

            // Four wedges at 90 degrees each. Every top-level tab owns its own sub-tabs rather
            // than spilling onto the root: product and its paperwork under Drugs, everything
            // territorial under Gangs, and your own standing under Reputation.
            //
            // Weapons sits at index 0 -- straight up, the easiest flick on the wheel. Hoodrich
            // took the weapon-wheel button, so getting a gun back has to be the fastest thing
            // on here, not something buried behind the business menu.
            page.Add("Weapons", "^", () => ShowVanillaWheel?.Invoke(),
                detail: "Opens the game's own weapon wheel",
                value: CurrentWeaponName());

            page.AddSub("Drugs", "$", BuildDrugsPage,
                detail: "Sell, cut and resupply",
                value: DrugsSummary(),
                enabled: !_deal.IsBusy && !_cutting.IsBusy,
                disabledReason: _deal.IsBusy ? "Already mid-deal" : "You are cutting");

            page.AddSub("Gangs", "%", BuildGangsPage,
                detail: _crew.IsAffiliated ? "Crews and turf" : "Pick who you run with",
                value: _crew.IsAffiliated ? _crew.Current.Tag : "SOLO");

            page.AddSub("Reputation", "*", BuildReputationPage,
                detail: RankProgressLabel(),
                value: _state.RankName);

            return page;
        }

        private string DrugsSummary()
        {
            var packaged = Stash.TotalPackaged;
            var bulk = Stash.TotalBulk;

            if (packaged > 0.005f && bulk > 0.005f)
            {
                return packaged.ToString("0.#") + "g bagged, " + bulk.ToString("0.#") + "g bulk";
            }
            if (packaged > 0.005f) return packaged.ToString("0.#") + "g bagged";
            if (bulk > 0.005f) return bulk.ToString("0.#") + "g bulk";
            return "empty";
        }

        private string SupplyDetail()
        {
            if (_dealers.InReach != null) return "Your contact is right here";
            if (_dealers.HasMeet) return _dealers.MeetDealer.Name + " -- " +
                                          _dealers.MeetDistance.ToString("0") + "m away";
            return "Call a contact for bulk weight";
        }

        // ---- drugs -------------------------------------------------------------

        /// <summary>Everything to do with product, in the order you actually do it.</summary>
        private WheelPage BuildDrugsPage()
        {
            var packaged = Stash.TotalPackaged;
            var bulk = Stash.TotalBulk;

            var page = new WheelPage("Drugs", DrugsSummary());
            page.PanelTitle = "Holding";
            page.Row("Bulk", bulk.ToString("0.#") + "g");
            page.Row("Bagged", packaged.ToString("0.#") + "g");
            page.Row("Free space", Stash.FreeSpace.ToString("0") + "g");
            page.Row("Street value", "$" + PackagedValue().ToString("N0"), Palette.Cash);
            page.Row("Prices", _pricing.PriceContext());

            page.AddSub("Buy", "+", BuildSupplyPage,
                detail: SupplyDetail(),
                value: "$" + Game.Player.Money.ToString("N0"));

            page.AddSub("Prep", "/", BuildCutPage,
                detail: "Break weight down into sellable amounts",
                value: bulk > 0.005f ? bulk.ToString("0.#") + "g bulk" : "",
                enabled: bulk > 0.005f,
                disabledReason: "Nothing to work -- buy some weight first");

            if (_postUp.IsPosted)
            {
                page.Add("Pack up", "x", () => _postUp.Stop("You packed up."),
                    detail: "Stop dealing and move on",
                    value: _postUp.Footfall + " passing");
            }
            else
            {
                page.AddSub("Sell", "$", BuildSellPage,
                    detail: "Post up on a corner and let it come to you",
                    value: packaged > 0.005f ? packaged.ToString("0.#") + "g ready" : "",
                    enabled: packaged > 0.005f,
                    disabledReason: bulk > 0.005f ? "All you have is weight -- prep it first"
                                                  : "You are holding nothing");
            }

            return page;
        }

        private string StashDetail()
        {
            var here = _hideouts.AtPlayer;
            if (here != null)
            {
                return here.Owned ? "Move product in and out" : "For sale -- $" + here.Price.ToString("N0");
            }

            var nearest = _hideouts.NearestOwned;
            if (nearest != null)
            {
                return nearest.ZoneName + " -- " + _hideouts.DistanceTo(nearest).ToString("0") + "m away";
            }

            return "Buy somewhere on a block to bank product";
        }

        private string StashValue()
        {
            var total = 0f;
            foreach (var h in _hideouts.All) if (h.Owned) total += h.Stash.Total;

            return _hideouts.OwnedCount > 0
                ? total.ToString("0.#") + "g in " + _hideouts.OwnedCount + " place" +
                  (_hideouts.OwnedCount == 1 ? "" : "s")
                : "";
        }

        /// <summary>
        /// The hideout you are standing in: buy it, or move product through it.
        ///
        /// Product banked here is off your person, so neither a death nor a bust can touch it.
        /// The trip out here is the price of keeping it safe.
        /// </summary>
        private WheelPage BuildStashPage()
        {
            var here = _hideouts.AtPlayer;
            if (here == null)
            {
                var empty = new WheelPage("Stash", "Nowhere to stash");
                empty.Add("Nothing here", "-", null,
                    detail: "Stand in a hideout you own",
                    enabled: false, disabledReason: "Not at a hideout");
                return empty;
            }

            if (!here.Owned) return BuildBuyHideoutPage(here);

            var den = here.Stash;
            var page = new WheelPage("Stash", here.ZoneName);

            page.PanelTitle = here.ZoneName;
            page.Row("Stashed bulk", den.TotalBulk.ToString("0.#") + "g");
            page.Row("Stashed bagged", den.TotalPackaged.ToString("0.#") + "g", Palette.Cash);
            page.Row("Space here", den.FreeSpace.ToString("0") + "g");
            page.Row("", "");
            page.Row("On you", Stash.Total.ToString("0.#") + "g");
            page.Row("Your space", Stash.FreeSpace.ToString("0") + "g");
            page.Row("Places owned", _hideouts.OwnedCount + " / " + _cfg.MaxHideouts);

            page.Add("Deposit all", "v", () => DepositAll(here),
                detail: "Everything you are carrying, into this place",
                value: Stash.Total.ToString("0.#") + "g",
                enabled: Stash.Total > 0.005f,
                disabledReason: "You are carrying nothing");

            page.Add("Withdraw all", "^", () => WithdrawAll(here),
                detail: "As much as you can carry, out of this place",
                value: den.Total.ToString("0.#") + "g",
                enabled: den.Total > 0.005f && Stash.FreeSpace > 0.005f,
                disabledReason: den.Total <= 0.005f ? "This place is empty" : "You are full");

            page.Add("Sell up", "x", () => SellHideout(here),
                detail: "Sell this place back. Empty it first.",
                value: "+$" + ((int)(here.Price * _cfg.HideoutSellbackPercent / 100f)).ToString("N0"),
                enabled: den.IsEmpty,
                disabledReason: "Empty it first");

            foreach (var d in _drugs.All)
            {
                var drug = d;
                var carried = Stash.BulkOf(drug.Id) + Stash.PackagedOf(drug.Id);
                var stashed = den.BulkOf(drug.Id) + den.PackagedOf(drug.Id);
                if (carried <= 0.005f && stashed <= 0.005f) continue;

                page.Add(drug.Tag, drug.Tier >= 3 ? "!" : "o", () => DepositOne(here, drug),
                    detail: "Deposit your " + drug.Name + "  ·  " + stashed.ToString("0.#") + "g here",
                    value: carried > 0.005f ? carried.ToString("0.#") + "g on you" : "none on you",
                    enabled: carried > 0.005f,
                    disabledReason: "None on you");
            }

            return page;
        }

        /// <summary>A place that is for sale, and what it would cost you.</summary>
        private WheelPage BuildBuyHideoutPage(Hideout hideout)
        {
            var page = new WheelPage("For sale", hideout.ZoneName);

            page.PanelTitle = hideout.ZoneName;
            page.Row("Price", "$" + hideout.Price.ToString("N0"), Palette.Warn);
            page.Row("Cash", "$" + Game.Player.Money.ToString("N0"), Palette.Cash);
            page.Row("Holds", _cfg.HideoutStashCapacity.ToString("N0") + "g");
            page.Row("Places owned", _hideouts.OwnedCount + " / " + _cfg.MaxHideouts,
                     _hideouts.AtCap ? Palette.Danger : (System.Drawing.Color?)null);
            page.Row("Block", _turf.StatusLine, TurfTint());

            var reason = _hideouts.AtCap ? "You already hold " + _cfg.MaxHideouts
                : Game.Player.Money < hideout.Price
                    ? "Short $" + (hideout.Price - Game.Player.Money).ToString("N0")
                    : "";

            page.Add("Buy it", "$", () => BuyHideout(hideout),
                detail: "Product banked here survives death and arrest",
                value: "$" + hideout.Price.ToString("N0"),
                enabled: reason.Length == 0,
                disabledReason: reason);

            page.Add("Walk away", "x", null,
                detail: "It will still be here",
                enabled: false, disabledReason: "Back out to leave");

            return page;
        }

        private void BuyHideout(Hideout hideout)
        {
            var failure = _hideouts.Buy(hideout);
            if (failure != null) Notify.Problem(failure);
            else _state.Touch();
        }

        private void SellHideout(Hideout hideout)
        {
            var failure = _hideouts.Sell(hideout);
            if (failure != null) Notify.Problem(failure);
            else _state.Touch();
        }

        private void DepositAll(Hideout hideout)
        {
            var moved = 0f;
            foreach (var d in _drugs.All) moved += MoveDrug(Stash, hideout.Stash, d.Id);

            _state.Touch();
            Notify.Ticker(moved > 0.005f
                ? "~g~Stashed " + moved.ToString("0.#") + "g.~s~"
                : "~o~Nothing moved.~s~");
        }

        private void WithdrawAll(Hideout hideout)
        {
            var moved = 0f;
            foreach (var d in _drugs.All) moved += MoveDrug(hideout.Stash, Stash, d.Id);

            _state.Touch();
            Notify.Ticker(moved > 0.005f
                ? "~g~Took " + moved.ToString("0.#") + "g out.~s~"
                : "~o~No room for any of it.~s~");
        }

        private void DepositOne(Hideout hideout, DrugDef drug)
        {
            var moved = MoveDrug(Stash, hideout.Stash, drug.Id);
            _state.Touch();

            Notify.Ticker(moved > 0.005f
                ? "~g~Stashed " + moved.ToString("0.#") + "g " + drug.Name + ".~s~"
                : "~o~No room in the den.~s~");
        }

        /// <summary>
        /// Moves one product between two stashes, preserving purity. Only ever removes what the
        /// destination actually accepted, so nothing can be lost to a full container.
        /// </summary>
        private static float MoveDrug(Stash from, Stash to, string drugId)
        {
            var moved = 0f;

            var bulk = from.BulkOf(drugId);
            if (bulk > 0.005f)
            {
                var accepted = to.AddBulk(drugId, bulk);
                if (accepted > 0.005f)
                {
                    from.RemoveBulk(drugId, accepted);
                    moved += accepted;
                }
            }

            var packaged = from.PackagedOf(drugId);
            if (packaged > 0.005f)
            {
                var purity = from.PurityOf(drugId);
                var accepted = to.AddPackaged(drugId, packaged, purity);
                if (accepted > 0.005f)
                {
                    from.RemovePackaged(drugId, accepted);
                    moved += accepted;
                }
            }

            return moved;
        }

        /// <summary>
        /// The books: every product's bulk and street-ready weight, and where every supply
        /// line currently stands.
        ///
        /// Segments are the products themselves rather than actions, so flicking round the ring
        /// reads each one out in the hub; the standing board lives in the side panel where there
        /// is room for it.
        /// </summary>
        private WheelPage BuildDrugStatusPage()
        {
            var page = new WheelPage("Status", DrugsSummary());

            page.PanelTitle = "Imports";

            // Where every supply line stands right now.
            foreach (var s in _dealers.All)
            {
                page.Row(s.Tag, ImportStatus(s), ImportTint(s));
            }

            page.Row("", "");
            page.Row("Bulk", Stash.TotalBulk.ToString("0.#") + "g");
            page.Row("Ready to sell", Stash.TotalPackaged.ToString("0.#") + "g",
                     Stash.TotalPackaged > 0.005f ? Palette.Cash : Palette.TextDim);
            page.Row("Free space", Stash.FreeSpace.ToString("0") + "g",
                     Stash.FreeSpace < 20f ? Palette.Warn : (System.Drawing.Color?)null);
            page.Row("Street value", "$" + PackagedValue().ToString("N0"), Palette.Cash);

            // Both of these move the street price, so they belong on the product board.
            page.Row("Prices", _pricing.PriceContext());
            page.Row("Heat", _state.Notoriety.ToString("F0") + "%",
                     _state.Notoriety > 50f ? Palette.Danger
                        : _state.Notoriety > 20f ? Palette.Warn : (System.Drawing.Color?)null);

            // Every product in the catalogue, held or not, so the board never goes blank.
            foreach (var d in _drugs.All)
            {
                var drug = d;
                var bulk = Stash.BulkOf(drug.Id);
                var ready = Stash.PackagedOf(drug.Id);
                var purity = Stash.PurityOf(drug.Id);
                var holding = bulk > 0.005f || ready > 0.005f;

                var value = ready > 0.005f
                    ? ready.ToString("0.#") + "g ready"
                    : bulk > 0.005f ? bulk.ToString("0.#") + "g bulk" : "none";

                var market = _market == null ? "" : "  ·  mkt " + _market.TrendLabel(drug.Id);

                

                var detail = !holding
                    ? "Not holding any" + market
                    : bulk.ToString("0.#") + "g bulk  ·  " + ready.ToString("0.#") + "g cut" +
                      (ready > 0.005f ? " @ " + (purity * 100f).ToString("0") + "%" : "") + market;

                page.Add(drug.Tag, drug.Tier >= 3 ? "!" : "o", null,
                    detail: detail,
                    value: value,
                    enabled: holding,
                    disabledReason: "Not holding any " + drug.Name);

                if (holding && ready > 0.005f)
                {
                    page.Items[page.Items.Count - 1].Value =
                        ready.ToString("0.#") + "g = $" + _pricing.SaleValue(drug, ready, purity).ToString("N0");
                }
            }

            return page;
        }

        /// <summary>One-line state of a supply line, for the import board.</summary>
        private string ImportStatus(DealerDef def)
        {
            var isActive = _dealers.HasMeet &&
                           string.Equals(_dealers.MeetDealer.Id, def.Id, StringComparison.OrdinalIgnoreCase);

            if (isActive)
            {
                return _dealers.InReach != null
                    ? "HERE NOW"
                    : "inbound " + _dealers.MeetDistance.ToString("0") + "m";
            }

            // Another meet is already running, so nothing else can be called yet.
            if (_dealers.HasMeet) return "waiting";

            var refusal = _dealers.RefusalReason(def, _state, _crew);
            if (refusal != null) return refusal.ToLowerInvariant();

            return "ready to call";
        }

        private System.Drawing.Color? ImportTint(DealerDef def)
        {
            var isActive = _dealers.HasMeet &&
                           string.Equals(_dealers.MeetDealer.Id, def.Id, StringComparison.OrdinalIgnoreCase);

            if (isActive) return _dealers.InReach != null ? Palette.Cash : Palette.Warn;
            if (_dealers.HasMeet) return Palette.TextDim;

            return _dealers.RefusalReason(def, _state, _crew) != null
                ? Palette.TextDim
                : (System.Drawing.Color?)null;
        }

        private int PackagedValue()
        {
            var worth = 0;
            foreach (var d in _drugs.All)
            {
                worth += _pricing.SaleValue(d, Stash.PackagedOf(d.Id), Stash.PurityOf(d.Id));
            }
            return worth;
        }

        // ---- weapons -----------------------------------------------------------

        private string CurrentWeaponName()
        {
            var def = _weapons.Get(WeaponRegistry.CurrentWeaponHash());
            return def == null ? "Unarmed" : def.Name;
        }

        /// <summary>
        /// The eight vanilla wheel slots, minus any the player has nothing in. Slot positions are
        /// not held stable here the way the business pages are: an empty Sniper wedge would be
        /// dead space on the one page that needs to be fast.
        /// </summary>
        private WheelPage BuildWeaponsPage()
        {
            var page = new WheelPage("Weapons", CurrentWeaponName());
            var currentHash = WeaponRegistry.CurrentWeaponHash();

            page.Add("Unarmed", "-", () => Equip(WeaponRegistry.UnarmedHash, "Unarmed"),
                detail: "Put it away",
                value: currentHash == WeaponRegistry.UnarmedHash ? "EQUIPPED" : "");

            foreach (var slot in _weapons.OccupiedSlots())
            {
                var name = slot;
                var carried = _weapons.CarriedInSlot(name);

                // Show the slot's own equipped weapon in the hub if it holds one.
                var equipped = carried.Find(w => w.Hash == currentHash);

                page.AddSub(name, "o", () => BuildWeaponSlotPage(name),
                    detail: carried.Count + (carried.Count == 1 ? " weapon" : " weapons"),
                    value: equipped != null ? equipped.Name : "");
            }

            return page;
        }

        private WheelPage BuildWeaponSlotPage(string slot)
        {
            var carried = _weapons.CarriedInSlot(slot);
            var currentHash = WeaponRegistry.CurrentWeaponHash();

            var page = new WheelPage(slot, CurrentWeaponName());

            foreach (var w in carried)
            {
                var def = w;
                var ammo = WeaponRegistry.AmmoFor(def.Hash);
                var isMelee = string.Equals(slot, "Melee", StringComparison.OrdinalIgnoreCase);
                var equipped = def.Hash == currentHash;

                var item = new WheelItem
                {
                    Label = def.Name,
                    Symbol = "o",
                    IconDict = def.Icon,
                    IconTexture = def.Icon,
                    IconReady = () => _weapons.IconReady(def),
                    Detail = equipped ? "In your hands" : "Equip",
                    Value = isMelee ? (equipped ? "EQUIPPED" : "") : ammo.ToString("N0") + " rounds",
                    OnSelect = () => Equip(def.Hash, def.Name)
                };

                page.Add(item);
            }

            return page;
        }

        private void Equip(uint hash, string name)
        {
            WeaponRegistry.Equip(hash);
            Log.Debug("Equipped " + name + ".");
        }

        // ---- sell --------------------------------------------------------------

        private WheelPage BuildSellPage()
        {
            var page = new WheelPage("Post up", "Pick what you are moving");
            page.PanelTitle = "This spot";
            page.Row("Zone", _turf.ZoneName);
            page.Row("Status", _turf.StatusLine, TurfTint());
            page.Row("Turf price", "x" + _pricing.TurfMultiplier.ToString("0.00"));
            page.Row("Heat per sale", "x" + _turf.TurfHeatMultiplier.ToString("0.0"),
                     _turf.TurfHeatMultiplier > 1.2f ? Palette.Danger : Palette.Cash);
            page.Row("Lookouts", _crew.NearbyAllies.ToString(),
                     _crew.NearbyAllies > 0 ? Palette.Cash : Palette.TextDim);
            page.Row("", "");
            page.Row("Passing now", _postUp.Footfall.ToString(),
                     _postUp.Footfall == 0 ? Palette.Warn : Palette.Cash);
            page.Row("Busier is", "faster and hotter", Palette.TextDim);

            var held = Stash.WithPackaged(_drugs);
            if (held.Count == 0)
            {
                page.Add("Nothing", "-", null, detail: "Nothing ready to sell",
                         enabled: false, disabledReason: "Nothing ready to sell");
                return page;
            }

            foreach (var drug in held)
            {
                var product = drug;
                var stock = Stash.PackagedOf(product.Id);
                var purity = Stash.PurityOf(product.Id);
                var value = _pricing.SaleValue(product, _cfg.PostUpDealGrams, purity);
                var risk = Pricing.BadCutChance(purity);

                page.Add(product.Tag, product.Tier >= 3 ? "!" : "o",
                    () => PostUpWith(product),
                    detail: (purity * 100f).ToString("0") + "% pure" +
                            (risk > 0.01f ? "  ~ " + (risk * 100f).ToString("0") + "% knockback" : ""),
                    value: stock.ToString("0.#") + "g  ·  $" + value.ToString("N0") + " a sale",
                    enabled: true);
            }

            return page;
        }

        /// <summary>
        /// Posts up with a product. You do not choose buyers any more -- you choose a spot,
        /// and the footfall there decides both how fast it moves and how hot it gets.
        /// </summary>
        private void PostUpWith(DrugDef product)
        {
            var failure = _postUp.Start(product);
            if (failure != null) Notify.Problem(failure);
        }

        private void Sell(DrugDef product, float grams)
        {
            var failure = _deal.TrySell(product, grams);
            if (failure != null) Notify.Problem(failure);
        }

        // ---- cut ---------------------------------------------------------------

        private WheelPage BuildCutPage()
        {
            var page = new WheelPage("Cut", "Bulk into street units");
            page.PanelTitle = "Cutting";
            page.Row("Bulk held", Stash.TotalBulk.ToString("0.#") + "g");
            page.Row("Bagged", Stash.TotalPackaged.ToString("0.#") + "g");
            page.Row("Free space", Stash.FreeSpace.ToString("0") + "g");

            var blocker = _cutting.WhyCannotCut();
            page.Row("Ready", blocker == null ? "yes" : "no",
                     blocker == null ? Palette.Cash : Palette.Warn);

            var bulk = Stash.WithBulk(_drugs);
            if (bulk.Count == 0)
            {
                page.Add("Nothing", "-", null, detail: "No bulk on you",
                         enabled: false, disabledReason: "No bulk on you");
                return page;
            }

            foreach (var drug in bulk)
            {
                var product = drug;
                var have = Stash.BulkOf(product.Id);

                page.AddSub(product.Tag, product.Tier >= 3 ? "!" : "o",
                    () => BuildPurityPage(product),
                    detail: "Choose how hard to step on it",
                    value: have.ToString("0.#") + "g bulk",
                    enabled: blocker == null,
                    disabledReason: blocker ?? "");
            }

            return page;
        }

        private WheelPage BuildPurityPage(DrugDef product)
        {
            var have = Stash.BulkOf(product.Id);
            var batch = Math.Min(have, 50f);

            var page = new WheelPage(product.Name, "Cutting " + batch.ToString("0") + "g");
            page.PanelTitle = product.Name + " batch";
            page.Row("Bulk on hand", have.ToString("0.#") + "g");
            page.Row("Batch size", batch.ToString("0") + "g");
            page.Row("Base price", "$" + product.BasePrice.ToString("0") + "/g");

            foreach (var p in PurityOptions)
            {
                var purity = p;
                var yield = Cutting.Yield(batch, purity);
                var gross = _pricing.SaleValue(product, yield, purity);
                var risk = Pricing.BadCutChance(purity);
                var fits = Stash.FreeSpace >= yield - batch - 0.001f;

                page.Add((purity * 100f).ToString("0") + "%",
                    risk > 0.2f ? "!" : "o",
                    () => Cut(product, batch, purity),
                    detail: risk < 0.01f
                        ? "Clean. Buyers never blink."
                        : (risk * 100f).ToString("0") + "% chance of a knockback",
                    value: yield.ToString("0") + "g  ~$" + gross.ToString("N0"),
                    enabled: fits,
                    disabledReason: "No room for " + yield.ToString("0") + "g");
            }

            return page;
        }

        private void Cut(DrugDef product, float grams, float purity)
        {
            var failure = _cutting.TryStart(product, grams, purity);
            if (failure != null) Notify.Problem(failure);
        }

        // ---- supply ------------------------------------------------------------

        /// <summary>
        /// Supply is about people, not a catalogue. What this page shows depends entirely on
        /// who is in front of you: the dealer you are standing at, the one you called out and
        /// have not reached yet, or -- if neither -- who you could phone and where to find them.
        /// </summary>
        private WheelPage BuildSupplyPage()
        {
            // Standing in front of someone: talk and trade.
            var here = _dealers.InReach;
            if (here != null) return BuildDealerPage(here);

            // Called someone out but not there yet.
            if (_dealers.HasMeet)
            {
                var def = _dealers.MeetDealer;
                var page = new WheelPage("Supply", "Meet is on");
                page.PanelTitle = def.Name;
                page.Row("Distance", _dealers.MeetDistance.ToString("0") + "m");
                page.Row("Carries", Carries(def));
                page.Row("Price", "x" + def.PriceMultiplier.ToString("0.00"));
                page.Row("Max order", def.MaxOrderGrams.ToString("0") + "g");

                page.Add("Waiting", ">", null,
                    detail: "Follow the blip and walk up to them",
                    value: _dealers.MeetDistance.ToString("0") + "m",
                    enabled: false, disabledReason: "Get to the meet");
                page.Add("Call off", "x", () => _dealers.CancelMeet("You called it off."),
                    detail: "Cancel the meet");
                return page;
            }

            // Nobody in reach: who can you call, and where do they stand.
            var list = new WheelPage("Supply", "Phone someone out, or go find them");
            list.PanelTitle = "Your connects";
            list.Row("Cash", "$" + Game.Player.Money.ToString("N0"), Palette.Cash);
            list.Row("Free space", Stash.FreeSpace.ToString("0") + "g");
            list.Row("Standing on", _turf.ZoneName);
            list.Row("Docks", _state.DocksUnlocked ? "open" : "unknown to you",
                     _state.DocksUnlocked ? Palette.Cash : Palette.TextDim);

            if (!_state.DocksUnlocked)
            {
                var toGo = DealerManager.GramsUntilSource(_state, _cfg.DocksUnlockGrams);
                list.Row("To the source", toGo.ToString("0.#") + "g more sold", Palette.Warn);
            }

            foreach (var d in _dealers.All)
            {
                var def = d;
                var refusal = _dealers.RefusalReason(def, _state, _crew);
                var gang = def.IsGangDealer ? _gangs.Get(def.GangId) : null;

                // Where they stand, so the wheel doubles as directions.
                var where = def.Kind == DealerKind.Docks
                    ? "At the port"
                    : gang != null ? "On " + gang.TurfHint : "";

                list.Add(def.Tag, def.Kind == DealerKind.Docks ? "=" : "o",
                    () => Call(def),
                    detail: where + (refusal == null ? "  ·  phone them out" : ""),
                    value: Carries(def) + "  x" + def.PriceMultiplier.ToString("0.00"),
                    enabled: refusal == null,
                    disabledReason: refusal ?? "");

                if (gang != null) list.Items[list.Items.Count - 1].Tint = gang.Colour;
            }

            return list;
        }

        private static string Carries(DealerDef def)
        {
            return def.Drugs.Count == 0
                ? "everything"
                : string.Join(", ", def.Drugs.ToArray()).ToUpperInvariant();
        }

        private void Call(DealerDef def)
        {
            var failure = _dealers.ArrangeMeet(def, _state, _crew);
            if (failure != null) Notify.Problem(failure);
        }

        /// <summary>
        /// Face to face with a dealer: what they sell, and the one question worth asking.
        /// </summary>
        private WheelPage BuildDealerPage(DealerDef def)
        {
            var mult = def.PriceMultiplier;

            var page = new WheelPage(def.Name, def.BuyLine);
            page.PanelTitle = def.Name;
            page.Row("Cash", "$" + Game.Player.Money.ToString("N0"), Palette.Cash);
            page.Row("Free space", Stash.FreeSpace.ToString("0") + "g");
            page.Row("Carries", Carries(def));
            page.Row("Price", "x" + mult.ToString("0.00"));
            page.Row("Max order", def.MaxOrderGrams.ToString("0") + "g");
            page.Row("Sold so far", _state.GramsSold.ToString("0.#") + "g");

            // The question that opens the game up. Only a gang dealer knows the answer.
            if (def.IsGangDealer)
            {
                var known = _state.DocksUnlocked;
                var toGo = DealerManager.GramsUntilSource(_state, _cfg.DocksUnlockGrams);

                page.Add("Ask source", "?",
                    () => _dealers.AskSource(def, _state, _cfg.DocksUnlockGrams),
                    detail: known
                        ? "You already know: the port"
                        : toGo > 0f
                            ? "He will not say yet -- " + toGo.ToString("0.#") + "g more to move"
                            : "\"Where are you getting this?\"",
                    value: known ? "KNOWN" : toGo > 0f ? toGo.ToString("0.#") + "g to go" : "ASK HIM");
            }

            page.Add("Leave", "x", () => Dialogue.Say(def.Name, def.Farewell),
                detail: "Walk away");

            return BuildDealerStock(page, def, mult);
        }

        private WheelPage BuildDealerStock(WheelPage page, DealerDef def, float mult)
        {

            // An empty Drugs list means this contact carries the whole catalogue.
            var stock = new List<DrugDef>();
            if (def.Drugs.Count == 0) stock.AddRange(_drugs.All);
            else
            {
                foreach (var id in def.Drugs)
                {
                    var d = _drugs.Get(id);
                    if (d != null) stock.Add(d);
                }
            }

            foreach (var s in stock)
            {
                var product = s;

                // He can only sell what he is actually holding.
                var onHand = _dealers.StockOf(def, product.Id);
                var lot = Math.Min(LotSizeFor(def, product), onHand);
                var cost = _pricing.PurchaseCost(product, lot, mult);

                var hasStock = onHand > 0.5f;
                var canAfford = Game.Player.Money >= cost;
                var fits = Stash.FreeSpace >= lot - 0.001f;

                var reason = !hasStock
                    ? (_dealers.IsDry(def) ? "He is dry today" : "He is out of " + product.Name)
                    : !canAfford ? "Short $" + (cost - Game.Player.Money).ToString("N0")
                    : !fits ? "No room -- sell or drop some"
                    : "";

                page.Add(product.Tag, product.Tier >= 3 ? "!" : "o",
                    () => Buy(def, product, lot, cost),
                    detail: "$" + _pricing.WholesalePrice(product, mult).ToString("0") + "/g bulk" +
                            (hasStock ? "  ·  he has " + onHand.ToString("0") + "g" : ""),
                    value: hasStock ? lot.ToString("0") + "g for $" + cost.ToString("N0") : "NONE",
                    enabled: reason.Length == 0,
                    disabledReason: reason);
            }

            return page;
        }

        /// <summary>Rank raises how much weight a contact will move at once, up to their cap.</summary>
        private float LotSizeFor(DealerDef def, DrugDef product)
        {
            var baseLot = 20f + _state.Rank * 20f;
            var scaled = Math.Max(5f, baseLot / product.Tier);
            return Math.Min(def.MaxOrderGrams, scaled);
        }

        private void Buy(DealerDef def, DrugDef product, float grams, int cost)
        {
            if (Game.Player.Money < cost)
            {
                Notify.Problem("not enough cash.");
                return;
            }

            // Take it off him first: he cannot sell what he does not have.
            var supplied = _dealers.TakeStock(def, product.Id, grams);
            if (supplied <= 0f)
            {
                Notify.Problem("he has none of that on him.");
                return;
            }

            var accepted = Stash.AddBulk(product.Id, supplied);
            if (accepted <= 0f)
            {
                Notify.Problem("you cannot carry any more.");
                return;
            }

            var charged = (int)Math.Round(cost * (accepted / grams));
            Game.Player.Money -= charged;
            _state.Touch();

            Notify.Ticker("~y~-$" + charged.ToString("N0") + "~s~  " + accepted.ToString("0.#") +
                          "g bulk " + product.Name);
            Log.Info("Bought " + accepted.ToString("0.##") + "g bulk " + product.Id +
                     " from " + def.Id + " for $" + charged + ".");
        }

        // ---- gangs -------------------------------------------------------------

        /// <summary>
        /// One wedge per gang. Picking one opens that gang's own page rather than joining
        /// immediately -- every crew is an entity you can inspect, deal with, or sign up to,
        /// and a mis-flick should never silently change who you run with.
        /// </summary>
        private WheelPage BuildGangsPage()
        {
            var page = new WheelPage("Gangs",
                _crew.IsAffiliated ? "Running with " + _crew.Current.Name : "Running solo");

            // Operational context only. The standing numbers -- rep, kills, money made for
            // each crew -- all live on the Reputation page so they are in one place.
            page.PanelTitle = _crew.IsAffiliated ? _crew.Current.Name : "Unaffiliated";
            page.Row("Affiliation", _crew.IsAffiliated ? _crew.Current.Name : "none",
                     _crew.IsAffiliated ? _crew.Current.Colour : (System.Drawing.Color?)Palette.TextDim);
            page.Row("Rank", _state.RankName);
            page.Row("Respect", _state.Respect.ToString("N0"));
            page.Row("Lookouts near", _crew.NearbyAllies.ToString(),
                     _crew.NearbyAllies > 0 ? Palette.Cash : Palette.TextDim);
            page.Row("Standing on", _turf.ZoneName, TurfTint());
            page.Row("This block", _turf.StatusLine, TurfTint());

            // Only YOUR crew belongs here. Listing all seven turned the wheel into a directory,
            // and joining is something you do by finding a leader in the world, not by picking
            // a wedge. Standing with the other crews is a readout, and lives on Reputation.
            page.AddSub("Turf", "#", BuildTurfPage,
                detail: _turf.StatusLine,
                value: _turf.ZoneName);

            if (_crew.IsAffiliated)
            {
                var mine = _crew.Current;

                page.AddSub("My crew", "*", () => BuildGangPage(mine),
                    detail: mine.TurfHint,
                    value: mine.Name);
                page.Items[page.Items.Count - 1].Tint = mine.Colour;

                page.Add("Homies", "^", null,
                    detail: "Pick up a homie to run with you",
                    enabled: false, disabledReason: "Not in this build yet");

                page.Add("Activities", "!", null,
                    detail: "Work the crew puts your way",
                    enabled: false, disabledReason: "Not in this build yet");
            }
            else
            {
                page.Add("No crew", "-", null,
                    detail: "Find a gang leader on their turf and talk to them",
                    value: "SOLO",
                    enabled: false, disabledReason: "Go and find one");
            }

            return page;
        }

        /// <summary>How this gang currently reads to the player, in two words.</summary>
        private string RelationLabel(GangDef gang)
        {
            var standing = _crew.StandingFor(gang.Id);

            if (_crew.IsAffiliated)
            {
                var mine = _crew.Current;
                if (mine.IsRivalOf(gang.Id) || gang.IsRivalOf(mine.Id)) return "AT WAR";
            }

            if (standing.Rep <= -50f) return "HOSTILE";
            if (standing.Rep >= 100f) return "TRUSTED";
            return "rep " + standing.Rep.ToString("0");
        }

        /// <summary>Everything you can do with one particular crew.</summary>
        private WheelPage BuildGangPage(GangDef gang)
        {
            var mine = _crew.IsAffiliated && _crew.Current.Id == gang.Id;
            var standing = _crew.StandingFor(gang.Id);
            var atWar = _crew.IsAffiliated && !mine &&
                        (_crew.Current.IsRivalOf(gang.Id) || gang.IsRivalOf(_crew.Current.Id));

            var page = new WheelPage(gang.Name, mine ? "Your crew" : RelationLabel(gang));

            page.PanelTitle = gang.Name;
            page.Row("Standing", mine ? "your crew" : RelationLabel(gang),
                     mine ? gang.Colour : atWar ? Palette.Danger : (System.Drawing.Color?)null);
            page.Row("Rep", standing.Rep.ToString("N0"),
                     standing.Rep < 0 ? Palette.Danger : Palette.Cash);
            page.Row("Kills for them", standing.Kills.ToString("N0"));
            page.Row("Money made", "$" + standing.MoneyEarned.ToString("N0"), Palette.Cash);
            page.Row("Deals", standing.Deals.ToString("N0"));
            page.Row("They move", string.Join(", ", gang.Drugs.ToArray()).ToUpperInvariant());
            page.Row("Turf", gang.TurfHint);
            page.Row("At war with", gang.Rivals.Count == 0 ? "nobody"
                                                           : string.Join(", ", gang.Rivals.ToArray()));

            // Join / leave.
            if (mine)
            {
                page.Add("Leave", "x", () => _crew.Leave(),
                    detail: "Walk away. Costs you rep with them.",
                    value: "-25 rep");
            }
            else
            {
                var reason = atWar ? "At war with " + _crew.Current.Name
                    : standing.Rep <= -50f ? "They want you dead"
                    : _state.Respect < gang.JoinRespect
                        ? "Need " + gang.JoinRespect.ToString("F0") + " respect"
                        : "";

                page.Add("Join", "+", () => Join(gang),
                    detail: _crew.IsAffiliated
                        ? "Switch crews -- " + _crew.Current.Name + " will remember"
                        : "Run with " + gang.Name,
                    value: gang.JoinRespect > 0 ? gang.JoinRespect.ToString("F0") + " respect" : "free",
                    enabled: reason.Length == 0,
                    disabledReason: reason);
            }

            // Their supply contact, if they have one.
            var plug = FindPlugFor(gang);
            if (plug != null)
            {
                var refusal = _dealers.RefusalReason(plug, _state, _crew);
                var mult = plug.PriceMultiplier;
                var carries = plug.Drugs.Count == 0
                    ? "everything"
                    : string.Join(", ", plug.Drugs.ToArray()).ToUpperInvariant();

                page.Add("Their plug", "+", () => Call(plug),
                    detail: plug.BuyLine,
                    value: carries + "  x" + mult.ToString("0.00"),
                    enabled: refusal == null,
                    disabledReason: refusal ?? "");
            }
            else
            {
                page.Add("Their plug", "+", null,
                    detail: "They have no contact you can call",
                    enabled: false, disabledReason: "No contact");
            }

            // Borrowing. Only your own crew will front you anything.
            var loan = _crew.Loan;
            var theirLoan = loan != null && loan.IsActive &&
                            string.Equals(loan.GangId, gang.Id, StringComparison.OrdinalIgnoreCase);

            page.AddSub("Loan", "$", () => BuildLoanPage(gang),
                detail: theirLoan
                    ? "Owe $" + loan.TotalOwed.ToString("N0") + "  ·  vig due in " + loan.DaysLeft + "d"
                    : mine ? "Borrow against your standing" : "They will not front you anything",
                value: theirLoan ? "$" + loan.TotalOwed.ToString("N0") + " OWED" : "",
                enabled: mine || theirLoan,
                disabledReason: loan != null && loan.IsActive ? "You already owe " + loan.GangId
                                                              : "Not your crew");

            page.Add("Their turf", "#", () => LogGangTurf(gang),
                detail: "Write their claimed zones to the log",
                value: gang.Turf.Count + " zones");

            page.Add("Dossier", "*", () => ShowGangDossier(gang, standing, mine),
                detail: "Full standing with this crew");

            return page;
        }

        /// <summary>
        /// Borrowing from the crew, and the vig that follows. The offer scales with rank,
        /// because a Pee-Wee has nothing to lend against.
        /// </summary>
        private WheelPage BuildLoanPage(GangDef gang)
        {
            var loan = _crew.Loan;
            var active = loan != null && loan.IsActive &&
                         string.Equals(loan.GangId, gang.Id, StringComparison.OrdinalIgnoreCase);

            var page = new WheelPage("Loan", gang.Name);
            page.PanelTitle = active ? "Outstanding" : "Terms";

            if (active)
            {
                page.Row("Principal", "$" + loan.Principal.ToString("N0"));
                page.Row("Vig due", "$" + loan.Vig.ToString("N0"), Palette.Warn);
                page.Row("Total owed", "$" + loan.TotalOwed.ToString("N0"), Palette.Danger);
                page.Row("Due in", loan.DaysLeft + " days",
                         loan.DaysLeft <= 1 ? Palette.Danger : (System.Drawing.Color?)null);
                page.Row("Missed", loan.MissedPeriods + " / " + _cfg.LoanDefaultAfterMissed,
                         loan.MissedPeriods > 0 ? Palette.Danger : Palette.TextDim);
                page.Row("Cash", "$" + Game.Player.Money.ToString("N0"), Palette.Cash);

                page.Add("Pay vig", "$", PayVig,
                    detail: "Clears this period and resets the clock. Principal stays.",
                    value: "$" + loan.Vig.ToString("N0"),
                    enabled: Game.Player.Money >= loan.Vig,
                    disabledReason: "Short $" + (loan.Vig - Game.Player.Money).ToString("N0"));

                page.Add("Pay it off", "*", PayOff,
                    detail: "Clear the whole debt and be done",
                    value: "$" + loan.TotalOwed.ToString("N0"),
                    enabled: Game.Player.Money >= loan.TotalOwed,
                    disabledReason: "Short $" + (loan.TotalOwed - Game.Player.Money).ToString("N0"));

                return page;
            }

            var cap = MaxLoanFor();
            page.Row("Your limit", "$" + cap.ToString("N0"), Palette.Cash);
            page.Row("Vig", _cfg.LoanVigPercent.ToString("0") + "% per " + _cfg.LoanPeriodDays + " days");
            page.Row("Default after", _cfg.LoanDefaultAfterMissed + " missed periods", Palette.Warn);
            page.Row("Rank", _state.RankName);

            if (cap < 100)
            {
                page.Add("Nothing", "-", null,
                    detail: "You are not worth lending to yet",
                    enabled: false, disabledReason: "Rank up first");
                return page;
            }

            foreach (var fraction in new[] { 0.25f, 0.5f, 1f })
            {
                var amount = (int)Math.Round(cap * fraction / 100f) * 100;
                if (amount < 100) continue;

                var vig = Math.Max(1, (int)Math.Round(amount * _cfg.LoanVigPercent / 100f));

                page.Add("$" + (amount / 1000f).ToString("0.#") + "k", "$",
                    () => Borrow(gang, amount),
                    detail: "Vig $" + vig.ToString("N0") + " every " + _cfg.LoanPeriodDays + " days",
                    value: "$" + amount.ToString("N0"));
            }

            return page;
        }

        /// <summary>Lending limit, scaled by rank and by how they feel about you.</summary>
        private int MaxLoanFor()
        {
            if (!_crew.IsAffiliated) return 0;

            var rankScale = (_state.Rank + 1) / (float)PlayerState.RankNames.Length;
            var standing = _crew.CurrentStanding;
            var repScale = standing == null ? 1f : 1f + Math.Max(-0.5f, Math.Min(0.5f, standing.Rep / 400f));

            return (int)(_cfg.MaxLoanAmount * rankScale * repScale);
        }

        private void Borrow(GangDef gang, int amount)
        {
            if (_crew.Loan != null && _crew.Loan.IsActive)
            {
                Notify.Problem("you already owe somebody.");
                return;
            }

            _crew.Loan = GangLoan.Open(gang.Id, amount, _cfg.LoanVigPercent, _cfg.LoanPeriodDays);
            Game.Player.Money += amount;
            _state.Touch();

            Notify.Important("~g~+$" + amount.ToString("N0") + "~s~ from " + gang.Name +
                             ". Vig due in " + _cfg.LoanPeriodDays + " days.");
            Log.Info("Borrowed $" + amount + " from " + gang.Id + ".");
        }

        private void PayVig()
        {
            var loan = _crew.Loan;
            if (loan == null || !loan.PayVig(_cfg.LoanPeriodDays)) Notify.Problem("not enough cash.");
            else _state.Touch();
        }

        private void PayOff()
        {
            var loan = _crew.Loan;
            if (loan == null) return;

            if (!loan.PayOff())
            {
                Notify.Problem("not enough cash.");
                return;
            }

            // Clearing a debt is remembered.
            var standing = _crew.StandingFor(loan.GangId);
            standing.Rep = Math.Min(1000f, standing.Rep + 25f);

            _crew.Loan = null;
            _state.Touch();
        }

        private DealerDef FindPlugFor(GangDef gang)
        {
            foreach (var s in _dealers.All)
            {
                if (s.IsGangDealer && string.Equals(s.GangId, gang.Id, StringComparison.OrdinalIgnoreCase))
                {
                    return s;
                }
            }
            return null;
        }

        private void LogGangTurf(GangDef gang)
        {
            Log.Info("TURF  " + gang.Id.PadRight(12) + " " + string.Join(", ", gang.Turf.ToArray()));
            Notify.Ticker("~y~" + gang.Name + "~s~ holds " + gang.Turf.Count + " zones -- written to the log.");
        }

        private void ShowGangDossier(GangDef gang, GangStanding standing, bool mine)
        {
            Notify.Ticker(
                "~y~" + gang.Name + "~s~  " + (mine ? "your crew" : RelationLabel(gang)) + "\n" +
                "Rep " + standing.Rep.ToString("N0") + "  ·  " + standing.Kills + " kills  ·  " +
                standing.Deals + " deals\n" +
                "Made ~g~$" + standing.MoneyEarned.ToString("N0") + "~s~ under them\n" +
                gang.TurfHint);
        }

        private void Join(GangDef gang)
        {
            var failure = _crew.Join(gang, _state.Respect);
            if (failure != null) Notify.Problem(failure);
        }

        // ---- turf --------------------------------------------------------------

        private WheelPage BuildTurfPage()
        {
            var page = new WheelPage("Turf", _turf.StatusLine);

            page.PanelTitle = _turf.ZoneName;
            page.Row("Zone code", _turf.ZoneCode);
            page.Row("Claimed by", _turf.Owner == null ? "nobody" : _turf.Owner.Name,
                     _turf.Owner?.Colour ?? (System.Drawing.Color?)Palette.TextDim);
            page.Row("To you", _turf.Status.ToString(), TurfTint());
            page.Row("Price here", "x" + _turf.TurfPriceMultiplier.ToString("0.00"),
                     _turf.TurfPriceMultiplier > 1.05f ? Palette.Cash : Palette.Text);
            page.Row("Heat here", "x" + _turf.TurfHeatMultiplier.ToString("0.0"),
                     _turf.TurfHeatMultiplier > 1.2f ? Palette.Danger : Palette.Cash);
            page.Row("Seen dealing", _turf.IsExposed ? "yes" : "no",
                     _turf.IsExposed ? Palette.Warn : Palette.TextDim);

            page.Add("Log zone", "=", LogZone,
                detail: "Writes this zone code to Hoodrich.log",
                value: _turf.ZoneCode);

            page.Add("Dossier", "*", ShowTurfDossier,
                detail: "Who claims what, in the log",
                value: "");

            var claimBlocker =
                _war.IsActive ? "You are already in a war"
                : !_crew.IsAffiliated ? "You need a crew behind you"
                : _turf.Owner == null ? "Nobody holds this block"
                : _turf.Status == TurfStatus.Home ? "This is already your block"
                : "";

            page.AddSub("Claim", "#", BuildClaimPage,
                detail: claimBlocker.Length == 0
                    ? "Take " + _turf.ZoneName + " off " + _turf.Owner.Name
                    : "Take this block by force",
                value: claimBlocker.Length == 0
                    ? _war.DefenderReinforcements(_turf.ZoneCode) + " defenders"
                    : "",
                enabled: claimBlocker.Length == 0,
                disabledReason: claimBlocker);

            return page;
        }

        /// <summary>
        /// How hard to go in. A war is decided by reinforcements, so the only question is
        /// whether you brought more than they have -- and each tier costs far more than the last.
        /// </summary>
        private WheelPage BuildClaimPage()
        {
            var zone = _turf.ZoneCode;
            var defenders = _war.DefenderReinforcements(zone);
            var recommended = _war.RecommendedStrength(zone);

            var page = new WheelPage("Claim", _turf.ZoneName + " -- " + _turf.Owner.Name);

            page.PanelTitle = _turf.ZoneName;
            page.Row("Held by", _turf.Owner.Name, _turf.Owner.Colour);
            page.Row("Block value", _territoryValue(zone) + " / " + _cfg.MaxTurfValue);
            page.Row("Defenders", defenders.ToString("N0"), Palette.Danger);
            page.Row("Suggested", recommended.ToString(), Palette.Warn);
            page.Row("Cash", "$" + Game.Player.Money.ToString("N0"), Palette.Cash);

            foreach (AttackStrength strength in Enum.GetValues(typeof(AttackStrength)))
            {
                var s = strength;
                var cost = _war.AttackCost(s);
                var mine = _war.AttackerReinforcements(s);
                var canAfford = Game.Player.Money >= cost;

                page.Add(s.ToString(), s == recommended ? "*" : "o",
                    () => Claim(s),
                    detail: mine + " of yours against " + defenders + " of theirs" +
                            (mine >= defenders ? "  ·  should hold" : "  ·  you will be outnumbered"),
                    value: "$" + cost.ToString("N0"),
                    enabled: canAfford,
                    disabledReason: "Short $" + (cost - Game.Player.Money).ToString("N0"));
            }

            return page;
        }

        private int _territoryValue(string zone) => _war.DefenderReinforcements(zone) > 0
            ? (int)Math.Round((_war.DefenderReinforcements(zone) - _cfg.BaseKillsBeforeWarVictory)
                              / Math.Max(0.01f, _cfg.ExtraKillsPerTurfValue))
            : 0;

        private void Claim(AttackStrength strength)
        {
            var failure = _war.TryStart(_turf.ZoneCode, _turf.ZoneName, _turf.Owner, strength);
            if (failure != null) Notify.Problem(failure);
        }

        private System.Drawing.Color TurfTint()
        {
            switch (_turf.Status)
            {
                case TurfStatus.Home: return Palette.Cash;
                case TurfStatus.Hostile: return Palette.Danger;
                case TurfStatus.Foreign: return Palette.Warn;
                default: return Palette.TextDim;
            }
        }

        /// <summary>
        /// Prints the current zone code so the turf map can be filled in accurately from
        /// inside the game rather than guessed at in a text editor.
        /// </summary>
        private void LogZone()
        {
            var owner = _turf.Owner == null ? "unclaimed" : _turf.Owner.Id;
            Log.Info("ZONE  code=\"" + _turf.ZoneCode + "\"  name=\"" + _turf.ZoneName +
                     "\"  owner=" + owner);
            Notify.Ticker("~y~" + _turf.ZoneCode + "~s~ (" + _turf.ZoneName + ") -> " + owner +
                          "  logged");
        }

        private void ShowTurfDossier()
        {
            foreach (var g in _gangs.All)
            {
                Log.Info("TURF  " + g.Id.PadRight(12) + " " + string.Join(", ", g.Turf.ToArray()));
            }
            Notify.Ticker("Turf map written to Hoodrich.log.");
        }

        // ---- reputation ---------------------------------------------------------

        /// <summary>
        /// Everything about where you stand: your own rank ladder, and your standing with every
        /// crew in the city.
        ///
        /// Gangs is for doing things -- joining, buying, checking whose block you are on.
        /// Reputation is for reading the numbers those actions produced, all in one ring, so
        /// you never have to walk seven gang pages to compare where you are welcome.
        /// </summary>
        private WheelPage BuildReputationPage()
        {
            var page = new WheelPage("Reputation", RankProgressLabel());

            var totalKills = 0;
            var totalGangDeals = 0;
            long gangEarnings = 0;
            foreach (var s in _crew.AllStandings)
            {
                totalKills += s.Kills;
                totalGangDeals += s.Deals;
                gangEarnings += s.MoneyEarned;
            }

            page.PanelTitle = _state.RankName;
            page.Row("Respect", _state.Respect.ToString("N0"));
            page.Row("Next rank", RankProgressLabel(),
                     _state.Rank >= PlayerState.RankNames.Length - 1 ? Palette.Cash
                                                                     : (System.Drawing.Color?)null);
            page.Row("Heat", _state.Notoriety.ToString("F0") + "%",
                     _state.Notoriety > 50f ? Palette.Danger
                        : _state.Notoriety > 20f ? Palette.Warn : Palette.Cash);
            page.Row("", "");
            page.Row("Crew", _crew.IsAffiliated ? _crew.Current.Name : "solo",
                     _crew.IsAffiliated ? _crew.Current.Colour : (System.Drawing.Color?)Palette.TextDim);
            page.Row("Deals closed", _state.TotalDealsMade.ToString("N0"));
            page.Row("Total earned", "$" + _state.TotalEarned.ToString("N0"), Palette.Cash);
            page.Row("", "");
            page.Row("Rival kills", totalKills.ToString("N0"));
            page.Row("Deals for crews", totalGangDeals.ToString("N0"));
            page.Row("Earned for crews", "$" + gangEarnings.ToString("N0"), Palette.Cash);

            // Standing with every crew goes in the PANEL, not on the ring. Seven wedges of
            // pure readout was noise: the wheel is for picking things, the panel is for reading.
            page.Row("", "");
            foreach (var g in _gangs.All)
            {
                var standing = _crew.StandingFor(g.Id);
                var mine = _crew.IsAffiliated && _crew.Current.Id == g.Id;

                var value = standing.Rep.ToString("0");
                if (standing.Kills > 0) value += "  ·  " + standing.Kills + "k";
                if (standing.MoneyEarned > 0) value += "  ·  $" + standing.MoneyEarned.ToString("N0");

                page.Row(mine ? g.Tag + "  (yours)" : g.Tag, value,
                         mine ? g.Colour
                              : standing.Rep < 0f ? Palette.Danger
                              : standing.Rep > 0f ? Palette.Cash : (System.Drawing.Color?)Palette.TextDim);
            }

            // The ring itself is the rank ladder: what you passed, where you are, what is next.
            var current = _state.Rank;

            for (var i = 0; i < PlayerState.RankNames.Length; i++)
            {
                var rank = i;
                var threshold = PlayerState.RankThresholds[rank];
                var reached = current >= rank;
                var isCurrent = current == rank;

                var detail = isCurrent
                    ? "You are here -- " + RankProgressLabel()
                    : reached ? "Passed" : "Needs " + threshold.ToString("N0") + " respect";

                page.Add(PlayerState.RankNames[rank],
                    isCurrent ? "*" : reached ? "o" : "-",
                    null,
                    detail: detail + ".  " + RankUnlocks(rank),
                    value: reached ? (isCurrent ? "CURRENT" : "passed")
                                   : threshold.ToString("N0") + " respect",
                    enabled: reached,
                    disabledReason: "Needs " + threshold.ToString("N0") + " respect.  " + RankUnlocks(rank));
            }

            return page;
        }

        /// <summary>
        /// What a rank buys you, derived from the live data rather than hardcoded, so editing
        /// suppliers.json keeps this honest.
        /// </summary>
        private string RankUnlocks(int rank)
        {
            var opened = new List<string>();
            foreach (var s in _dealers.All)
            {
                if (s.MinRank == rank && rank > 0) opened.Add(s.Tag);
            }

            // LotSizeFor()'s base, before the per-product tier divide.
            var lot = 20f + rank * 20f;
            var text = "Lots up to " + lot.ToString("0") + "g";

            if (opened.Count > 0) text += ";  " + string.Join(", ", opened.ToArray()) + " take your call";

            return text;
        }

        // ---- shared readouts ----------------------------------------------------

        /// <summary>Progress toward the next rank, phrased for a panel row.</summary>
        private string RankProgressLabel()
        {
            return _state.Rank >= PlayerState.RankNames.Length - 1
                ? "max rank"
                : (_state.RankProgress * 100f).ToString("F0") + "% to " +
                  PlayerState.RankNames[_state.Rank + 1];
        }
    }
}
