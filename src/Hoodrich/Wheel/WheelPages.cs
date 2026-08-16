using System;
using System.Collections.Generic;
using GTA;
using Hoodrich.Core;
using Hoodrich.Dealing;
using Hoodrich.Economy;
using Hoodrich.Gangs;
using Hoodrich.State;
using Hoodrich.Supply;
using Hoodrich.Territory;
using Hoodrich.UI;

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
        private readonly SupplierManager _suppliers;

        public WheelPages(PlayerState state, Drugs drugs, Pricing pricing, StreetDeal deal,
                          Cutting cutting, GangRegistry gangs, Affiliation crew, TurfWatch turf,
                          SupplierManager suppliers)
        {
            _state = state;
            _drugs = drugs;
            _pricing = pricing;
            _deal = deal;
            _cutting = cutting;
            _gangs = gangs;
            _crew = crew;
            _turf = turf;
            _suppliers = suppliers;
        }

        private Stash Stash => _state.Stash;

        // ---- root --------------------------------------------------------------

        public WheelPage BuildRoot()
        {
            var packaged = Stash.TotalPackaged;
            var bulk = Stash.TotalBulk;

            var page = new WheelPage("Hoodrich",
                _crew.IsAffiliated ? _crew.Current.Name : "Unaffiliated");

            page.Add("Sell", "$", null, enabled: false); // replaced below; keeps ordering obvious
            page.Items.Clear();

            page.AddSub("Sell", "$", BuildSellPage,
                detail: "Hand-to-hand to someone on foot",
                value: packaged > 0.005f ? packaged.ToString("0.#") + "g bagged" : "",
                enabled: packaged > 0.005f && !_deal.IsBusy && !_cutting.IsBusy,
                disabledReason: _deal.IsBusy ? "Already mid-deal"
                    : _cutting.IsBusy ? "You are cutting"
                    : bulk > 0.005f ? "All you have is bulk -- cut it first"
                    : "You are holding nothing");

            page.AddSub("Cut", "/", BuildCutPage,
                detail: "Bag bulk into street units",
                value: bulk > 0.005f ? bulk.ToString("0.#") + "g bulk" : "",
                enabled: bulk > 0.005f && !_cutting.IsBusy && !_deal.IsBusy,
                disabledReason: _cutting.IsBusy ? "Already cutting"
                    : _deal.IsBusy ? "Already mid-deal"
                    : "No bulk to cut -- buy some first");

            page.AddSub("Supply", "+", BuildSupplyPage,
                detail: SupplyDetail(),
                value: "$" + Game.Player.Money.ToString("N0"),
                enabled: !_deal.IsBusy && !_cutting.IsBusy,
                disabledReason: "Busy");

            page.AddSub("Gang", "^", BuildGangPage,
                detail: _crew.IsAffiliated ? "Your crew and your standing" : "Pick who you run with",
                value: _crew.IsAffiliated ? _crew.Current.Tag : "SOLO");

            page.AddSub("Turf", "#", BuildTurfPage,
                detail: _turf.StatusLine,
                value: _turf.ZoneName);

            page.Add("Status", "*", ShowStatus,
                detail: _pricing.PriceContext(),
                value: "Heat " + _state.Notoriety.ToString("F0") + "%");

            return page;
        }

        private string SupplyDetail()
        {
            if (_suppliers.TradablePed != null) return "Your contact is right here";
            if (_suppliers.HasMeet) return _suppliers.ActiveMeet.Name + " -- " +
                                          _suppliers.MeetDistance.ToString("0") + "m away";
            return "Call a contact for bulk weight";
        }

        // ---- sell --------------------------------------------------------------

        private WheelPage BuildSellPage()
        {
            var page = new WheelPage("Sell", "Face a buyer on foot");
            page.PanelTitle = "This block";
            page.Row("Zone", _turf.ZoneName);
            page.Row("Status", _turf.StatusLine, TurfTint());
            page.Row("Turf price", "x" + _pricing.TurfMultiplier.ToString("0.00"));
            page.Row("Heat per sale", "x" + _turf.TurfHeatMultiplier.ToString("0.0"),
                     _turf.TurfHeatMultiplier > 1.2f ? Palette.Danger : Palette.Cash);
            page.Row("Lookouts", _crew.NearbyAllies.ToString(),
                     _crew.NearbyAllies > 0 ? Palette.Cash : Palette.TextDim);

            var held = Stash.WithPackaged(_drugs);
            if (held.Count == 0)
            {
                page.Add("Nothing", "-", null, detail: "Nothing bagged",
                         enabled: false, disabledReason: "Nothing bagged");
                return page;
            }

            foreach (var drug in held)
            {
                var product = drug;
                var stock = Stash.PackagedOf(product.Id);
                var purity = Stash.PurityOf(product.Id);
                var amount = Math.Min(DealSize, stock);
                var value = _pricing.SaleValue(product, amount, purity);
                var risk = Pricing.BadCutChance(purity);

                page.Add(product.Tag, product.Tier >= 3 ? "!" : "o",
                    () => Sell(product, amount),
                    detail: (purity * 100f).ToString("0") + "% pure" +
                            (risk > 0.01f ? "  ~ " + (risk * 100f).ToString("0") + "% knockback" : ""),
                    value: amount.ToString("0.#") + "g for $" + value.ToString("N0"),
                    enabled: true);
            }

            return page;
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

        private WheelPage BuildSupplyPage()
        {
            // Standing in front of a contact: trade.
            var ped = _suppliers.TradablePed;
            if (ped != null && _suppliers.ActiveMeet != null) return BuildBuyPage(_suppliers.ActiveMeet);

            // Meet arranged but not there yet.
            if (_suppliers.HasMeet)
            {
                var def = _suppliers.ActiveMeet;
                var page = new WheelPage("Supply", "Meet is on");
                page.PanelTitle = def.Name;
                page.Row("Distance", _suppliers.MeetDistance.ToString("0") + "m");
                page.Row("Sells", string.Join(", ", def.Drugs.ToArray()).ToUpperInvariant());
                page.Row("Price", "x" + def.PriceMultiplier.ToString("0.00"));
                page.Row("Max order", def.MaxOrderGrams.ToString("0") + "g");

                page.Add("Go to meet", ">", () => Notify.Ticker("Marked on your map."),
                         detail: "Follow the yellow blip", value: _suppliers.MeetDistance.ToString("0") + "m");
                page.Add("Call off", "x", () => _suppliers.CancelMeet("You called it off."),
                         detail: "Cancel the meet");
                return page;
            }

            // Nobody arranged: pick a contact.
            var list = new WheelPage("Supply", "Call a contact");
            list.PanelTitle = "Your connects";
            list.Row("Cash", "$" + Game.Player.Money.ToString("N0"), Palette.Cash);
            list.Row("Free space", Stash.FreeSpace.ToString("0") + "g");
            list.Row("Rank", _state.RankName);

            foreach (var s in _suppliers.All)
            {
                var def = s;
                var refusal = _suppliers.RefusalReason(def, _state, _crew);
                var mult = _suppliers.EffectiveMultiplier(def, _crew);
                var note = _suppliers.PriceNote(def, _crew);

                // What they actually carry, so the wheel reads as a shopping list.
                var carries = def.Drugs.Count == 0
                    ? "everything"
                    : string.Join(", ", def.Drugs.ToArray()).ToUpperInvariant();

                list.Add(def.Tag, def.IsGangContact ? "o" : "=", () => Call(def),
                    detail: def.Blurb + (note.Length > 0 ? "  (" + note + ")" : ""),
                    value: carries + "  x" + mult.ToString("0.00"),
                    enabled: refusal == null,
                    disabledReason: refusal ?? "");

                var gang = def.IsGangContact ? _gangs.Get(def.GangId) : null;
                if (gang != null) list.Items[list.Items.Count - 1].Tint = gang.Colour;
            }

            return list;
        }

        private void Call(SupplierDef def)
        {
            var failure = _suppliers.ArrangeMeet(def, _state, _crew);
            if (failure != null) Notify.Problem(failure);
        }

        private WheelPage BuildBuyPage(SupplierDef def)
        {
            var mult = _suppliers.EffectiveMultiplier(def, _crew);
            var note = _suppliers.PriceNote(def, _crew);

            var page = new WheelPage(def.Name, "Buying bulk");
            page.PanelTitle = def.Name;
            page.Row("Cash", "$" + Game.Player.Money.ToString("N0"), Palette.Cash);
            page.Row("Free space", Stash.FreeSpace.ToString("0") + "g");
            page.Row("Price", "x" + mult.ToString("0.00"),
                     mult < def.PriceMultiplier ? Palette.Cash : (System.Drawing.Color?)null);
            if (note.Length > 0) page.Row("Standing", note, Palette.Cash);

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
                var lot = LotSizeFor(def, product);
                var cost = _pricing.PurchaseCost(product, lot, mult);

                var canAfford = Game.Player.Money >= cost;
                var fits = Stash.FreeSpace >= lot - 0.001f;

                var reason = !canAfford ? "Short $" + (cost - Game.Player.Money).ToString("N0")
                    : !fits ? "No room -- sell or drop some"
                    : "";

                page.Add(product.Tag, product.Tier >= 3 ? "!" : "o",
                    () => Buy(def, product, lot, cost),
                    detail: "$" + _pricing.WholesalePrice(product, mult).ToString("0") + "/g bulk",
                    value: lot.ToString("0") + "g for $" + cost.ToString("N0"),
                    enabled: reason.Length == 0,
                    disabledReason: reason);
            }

            return page;
        }

        /// <summary>Rank raises how much weight a contact will move at once, up to their cap.</summary>
        private float LotSizeFor(SupplierDef def, DrugDef product)
        {
            var baseLot = 20f + _state.Rank * 20f;
            var scaled = Math.Max(5f, baseLot / product.Tier);
            return Math.Min(def.MaxOrderGrams, scaled);
        }

        private void Buy(SupplierDef def, DrugDef product, float grams, int cost)
        {
            if (Game.Player.Money < cost)
            {
                Notify.Problem("not enough cash.");
                return;
            }

            var accepted = Stash.AddBulk(product.Id, grams);
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

        // ---- gang --------------------------------------------------------------

        private WheelPage BuildGangPage()
        {
            var page = new WheelPage("Gang",
                _crew.IsAffiliated ? "Running with " + _crew.Current.Name : "Running solo");

            var standing = _crew.CurrentStanding;

            page.PanelTitle = _crew.IsAffiliated ? _crew.Current.Name : "Unaffiliated";
            page.Row("Affiliation", _crew.IsAffiliated ? _crew.Current.Name : "none",
                     _crew.IsAffiliated ? _crew.Current.Colour : (System.Drawing.Color?)Palette.TextDim);
            page.Row("Rank", _state.RankName);
            page.Row("Respect", _state.Respect.ToString("N0"));
            page.Row("Gang rep", standing == null ? "-" : standing.Rep.ToString("N0"),
                     standing == null ? Palette.TextDim
                        : standing.Rep < 0 ? Palette.Danger : Palette.Cash);
            page.Row("Kills for them", standing == null ? "-" : standing.Kills.ToString("N0"));
            page.Row("Money made", standing == null ? "-" : "$" + standing.MoneyEarned.ToString("N0"),
                     Palette.Cash);
            page.Row("Deals", standing == null ? "-" : standing.Deals.ToString("N0"));
            page.Row("Lookouts near", _crew.NearbyAllies.ToString(),
                     _crew.NearbyAllies > 0 ? Palette.Cash : Palette.TextDim);
            page.Row("Their turf", _crew.IsAffiliated ? _crew.Current.TurfHint : "-");

            foreach (var g in _gangs.All)
            {
                var gang = g;
                var mine = _crew.IsAffiliated && _crew.Current.Id == gang.Id;
                var theirStanding = _crew.StandingFor(gang.Id);

                var reason = mine ? "" :
                    theirStanding.Rep <= -50f ? "They want you dead"
                    : _state.Respect < gang.JoinRespect
                        ? "Need " + gang.JoinRespect.ToString("F0") + " respect"
                        : "";

                page.Add(gang.Tag, mine ? "*" : "o",
                    mine ? (Action)(() => _crew.Leave()) : () => Join(gang),
                    detail: mine ? "Your crew -- pick to walk away" : gang.TurfHint,
                    value: mine ? "AFFILIATED" : "rep " + theirStanding.Rep.ToString("0"),
                    enabled: mine || reason.Length == 0,
                    disabledReason: reason);

                page.Items[page.Items.Count - 1].Tint = gang.Colour;
            }

            return page;
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

            page.Add("Claim", "#", null,
                detail: "Take this block by force",
                enabled: false, disabledReason: "Turf wars are the next build");

            return page;
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

        // ---- status ------------------------------------------------------------

        private void ShowStatus()
        {
            var worth = 0;
            foreach (var d in _drugs.All)
            {
                worth += _pricing.SaleValue(d, Stash.PackagedOf(d.Id), Stash.PurityOf(d.Id));
            }

            var next = _state.Rank >= PlayerState.RankNames.Length - 1
                ? "max rank"
                : (_state.RankProgress * 100f).ToString("F0") + "% to " + PlayerState.RankNames[_state.Rank + 1];

            Notify.Ticker(
                "~y~" + _state.RankName + "~s~  " + next + "\n" +
                Stash.TotalBulk.ToString("0.#") + "g bulk  ·  " +
                Stash.TotalPackaged.ToString("0.#") + "g bagged (~g~$" + worth.ToString("N0") + "~s~)\n" +
                (_crew.IsAffiliated ? _crew.Current.Name : "Solo") + "  ·  " + _turf.StatusLine + "\n" +
                "Deals " + _state.TotalDealsMade + "  ·  Heat " + _state.Notoriety.ToString("F0") + "%  ·  " +
                _pricing.PriceContext());
        }
    }
}
