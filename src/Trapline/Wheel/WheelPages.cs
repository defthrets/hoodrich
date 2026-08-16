using System;
using GTA;
using Trapline.Core;
using Trapline.Dealing;
using Trapline.Economy;
using Trapline.State;
using Trapline.UI;

namespace Trapline.Wheel
{
    /// <summary>
    /// Builds wheel pages from live game state.
    ///
    /// Pages are rebuilt every time the wheel opens rather than cached, so prices, stock and
    /// rank gating are always current. Items the player cannot use yet are shown disabled with
    /// a reason rather than hidden, so the wheel layout stays in the same place every time --
    /// muscle memory is the entire point of a radial menu.
    /// </summary>
    internal sealed class WheelPages
    {
        /// <summary>Grams moved in a single hand-to-hand sale.</summary>
        private const float DealSize = 5f;

        private readonly Settings _cfg;
        private readonly PlayerState _state;
        private readonly Drugs _drugs;
        private readonly Pricing _pricing;
        private readonly StreetDeal _deal;

        public WheelPages(Settings cfg, PlayerState state, Drugs drugs, Pricing pricing, StreetDeal deal)
        {
            _cfg = cfg;
            _state = state;
            _drugs = drugs;
            _pricing = pricing;
            _deal = deal;
        }

        public WheelPage BuildRoot()
        {
            var held = _state.Inventory.Total;
            var page = new WheelPage("Trapline",
                _state.RankName + "  ·  " + _state.Respect.ToString("F0") + " respect");

            page.AddSub("Sell", "$", BuildSellPage,
                detail: "Hand-to-hand to someone on foot",
                value: held > 0.005f ? held.ToString("0.#") + "g held" : "",
                enabled: held > 0.005f && !_deal.IsBusy,
                disabledReason: _deal.IsBusy ? "Already mid-deal" : "You are holding nothing");

            page.AddSub("Resupply", "+", BuildResupplyPage,
                detail: "Call the plug for a drop",
                value: "$" + Game.Player.Money.ToString("N0"),
                enabled: !_deal.IsBusy,
                disabledReason: "Already mid-deal");

            page.Add("Status", "*", ShowStatus,
                detail: _pricing.PriceContext(),
                value: "Heat " + _state.Notoriety.ToString("F0") + "%");

            page.Add("Crew", "^", null,
                detail: "Recruit and command your people",
                enabled: false, disabledReason: "Not in this build yet");

            page.Add("Turf", "#", null,
                detail: "Claim and hold territory",
                enabled: false, disabledReason: "Not in this build yet");

            page.Add("Stash", "=", null,
                detail: "Bank product off your person",
                enabled: false, disabledReason: "Not in this build yet");

            return page;
        }

        // ---- sell --------------------------------------------------------------

        private WheelPage BuildSellPage()
        {
            var page = new WheelPage("Sell", "Facing a buyer on foot");
            var held = _state.Inventory.Held(_drugs);

            if (held.Count == 0)
            {
                page.Add("Nothing", "-", null, detail: "You are holding nothing",
                         enabled: false, disabledReason: "You are holding nothing");
                return page;
            }

            foreach (var drug in held)
            {
                var product = drug;
                var stock = _state.Inventory.Get(product.Id);
                var amount = Math.Min(DealSize, stock);
                var value = _pricing.SaleValue(product, amount);

                page.Add(product.Tag, product.Tier >= 3 ? "!" : "o",
                    () => Sell(product, amount),
                    detail: "$" + _pricing.StreetPrice(product).ToString("0") + "/g · " + _pricing.PriceContext(),
                    value: amount.ToString("0.#") + "g for $" + value.ToString("N0"));
            }

            return page;
        }

        private void Sell(DrugDef product, float grams)
        {
            var failure = _deal.TrySell(product, grams);
            if (failure != null) Notify.Ticker("~o~Trapline:~s~ " + failure);
        }

        // ---- resupply ----------------------------------------------------------

        private WheelPage BuildResupplyPage()
        {
            var page = new WheelPage("Resupply", "Wholesale, paid up front");

            foreach (var drug in _drugs.All)
            {
                var product = drug;

                // Rank gates what the plug will front you, and how much of it.
                var requiredRank = Math.Max(0, product.Tier - 1);
                var lot = LotSizeFor(product);
                var cost = _pricing.PurchaseCost(product, lot);

                var rankOk = _state.Rank >= requiredRank;
                var canAfford = Game.Player.Money >= cost;
                var fits = _state.Inventory.FreeSpace >= lot - 0.001f;

                var reason =
                    !rankOk ? "Need rank " + PlayerState.RankNames[Math.Min(requiredRank, PlayerState.RankNames.Length - 1)]
                    : !canAfford ? "Short $" + (cost - Game.Player.Money).ToString("N0")
                    : !fits ? "No room -- sell some first"
                    : "";

                page.Add(product.Tag, product.Tier >= 3 ? "!" : "o",
                    () => Buy(product, lot, cost),
                    detail: "$" + _pricing.WholesalePrice(product).ToString("0") + "/g wholesale",
                    value: lot.ToString("0") + "g for $" + cost.ToString("N0"),
                    enabled: reason.Length == 0,
                    disabledReason: reason);
            }

            return page;
        }

        /// <summary>Higher rank means the plug will move more weight at a time.</summary>
        private float LotSizeFor(DrugDef product)
        {
            var baseLot = 10f + _state.Rank * 10f;
            // Heavier tiers move in smaller lots so the cash outlay stays comparable.
            return Math.Max(2f, baseLot / product.Tier);
        }

        private void Buy(DrugDef product, float grams, int cost)
        {
            if (Game.Player.Money < cost)
            {
                Notify.Ticker("~o~Trapline:~s~ not enough cash.");
                return;
            }

            var accepted = _state.Inventory.Add(product.Id, grams);
            if (accepted <= 0f)
            {
                Notify.Ticker("~o~Trapline:~s~ you cannot carry any more.");
                return;
            }

            // Charge only for what actually fit.
            var charged = (int)Math.Round(cost * (accepted / grams));
            Game.Player.Money -= charged;
            _state.Touch();

            Notify.Ticker("~y~-$" + charged.ToString("N0") + "~s~  picked up " +
                                     accepted.ToString("0.#") + "g " + product.Name);
            Log.Info("Bought " + accepted.ToString("0.##") + "g " + product.Id + " for $" + charged + ".");
        }

        // ---- status ------------------------------------------------------------

        private void ShowStatus()
        {
            var inv = _state.Inventory;
            var worth = 0;
            foreach (var d in _drugs.All) worth += _pricing.SaleValue(d, inv.Get(d.Id));

            var next = _state.Rank >= PlayerState.RankNames.Length - 1
                ? "max rank"
                : (_state.RankProgress * 100f).ToString("F0") + "% to " + PlayerState.RankNames[_state.Rank + 1];

            Notify.Ticker(
                "~y~" + _state.RankName + "~s~  " + next + "\n" +
                "Holding " + inv.Total.ToString("0.#") + "g worth ~g~$" + worth.ToString("N0") + "~s~\n" +
                "Deals " + _state.TotalDealsMade + "  ·  Heat " + _state.Notoriety.ToString("F0") + "%  ·  " +
                _pricing.PriceContext());
        }
    }
}
