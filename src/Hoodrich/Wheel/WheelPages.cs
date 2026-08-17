using System;
using System.Collections.Generic;
using Color = System.Drawing.Color;
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
        private readonly StashHouse _stash;
        private readonly PostUp _postUp;
        private readonly GangLeaders _leaders;
        private readonly WeaponRegistry _weapons;

        /// <summary>Set by Main. Hands the player back the game's own weapon wheel.</summary>
        public Action ShowVanillaWheel;

        /// <summary>
        /// Set by Main. Where the numbers live.
        ///
        /// The wheel says "sell" and "re-up"; multipliers, heat percentages and per-gang
        /// standings go here, on a screen you can actually read, instead of crowding the ring
        /// with figures nobody can parse while holding a button down.
        /// </summary>
        public InfoPanel Info;

        /// <summary>Set by Main. The dock worker's run out to you.</summary>
        public Delivery Delivery;

        /// <summary>Set by Main. Moving product between your pockets and the house.</summary>
        public StashScreen StashScreen;

        public WheelPages(Core.Settings cfg, PlayerState state, Drugs drugs, Pricing pricing, StreetDeal deal,
                          Cutting cutting, GangRegistry gangs, Affiliation crew, TurfWatch turf,
                          DealerManager suppliers, WeaponRegistry weapons, Market market,
                          StashHouse stash, PostUp postUp, GangLeaders leaders)
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
            _stash = stash;
            _postUp = postUp;
            _leaders = leaders;
        }

        private Stash Stash => _state.Stash;

        // ---- the numbers, on their own screen -----------------------------------

        /// <summary>
        /// What you are carrying.
        ///
        /// Product for now, item by item with what it is worth. Written as a general inventory
        /// rather than a drug list so that anything else worth carrying later -- burners,
        /// phones, whatever the missions want -- has somewhere obvious to go.
        /// </summary>
        private void ShowInventory()
        {
            // In the house, your inventory IS the transfer screen: two containers side by side
            // rather than a list of what you happen to be carrying.
            if (_stash.AtDoor && StashScreen != null)
            {
                OpenStashScreen();
                return;
            }

            var sections = new List<InfoSection>();

            var ready = new InfoSection { Title = "Ready to sell" };
            var bagged = 0;

            foreach (var drug in _drugs.All)
            {
                var have = Stash.PackagedOf(drug.Id);
                if (have <= 0.005f) continue;

                var purity = Stash.PurityOf(drug.Id);
                ready.Row(drug.Name,
                          have.ToString("0.#") + "g  ·  $" + _pricing.SaleValue(drug, have, purity).ToString("N0"),
                          Palette.Cash);
                bagged++;
            }

            if (bagged == 0) ready.Row("Nothing bagged up", "", Palette.TextDim);
            sections.Add(ready);

            var weight = new InfoSection { Title = "Still to bag up" };
            var raw = 0;

            foreach (var drug in _drugs.All)
            {
                var have = Stash.BulkOf(drug.Id);
                if (have <= 0.005f) continue;

                weight.Row(drug.Name, have.ToString("0.#") + "g", Palette.Warn);
                raw++;
            }

            if (raw == 0) weight.Row("No weight on you", "", Palette.TextDim);
            sections.Add(weight);

            var pockets = new InfoSection { Title = "Pockets" };
            pockets.Row("Cash", "$" + Game.Player.Money.ToString("N0"), Palette.Cash);
            pockets.Row("Carrying", Stash.Total.ToString("0.#") + "g");
            pockets.Row("Room left", Stash.FreeSpace.ToString("0") + "g",
                        Stash.FreeSpace < 20f ? Palette.Warn : (Color?)null);
            sections.Add(pockets);

            // At home the inventory is two containers rather than one, so what is in the house
            // is listed right beside what is on you.
            if (_stash.AtDoor)
            {
                var den = _stash.Stash;
                var home = new InfoSection { Title = "At the stash house" };

                var kept = 0;
                foreach (var drug in _drugs.All)
                {
                    var have = den.BulkOf(drug.Id) + den.PackagedOf(drug.Id);
                    if (have <= 0.005f) continue;

                    home.Row(drug.Name, have.ToString("0.#") + "g", Palette.Cash);
                    kept++;
                }

                if (kept == 0) home.Row("Empty", "", Palette.TextDim);
                home.Row("Room here", den.FreeSpace.ToString("0") + "g");
                sections.Add(home);
            }

            Info?.Open("Inventory",
                       _stash.AtDoor ? "At the stash house" : CarriedSummary(),
                       sections);
        }

        /// <summary>One line for the wheel: what is on you right now.</summary>
        private string CarriedSummary()
        {
            var total = Stash.Total;
            return total <= 0.005f ? "empty" : total.ToString("0.#") + "g";
        }

        /// <summary>Everything about you: rank, heat, money made, who rates you.</summary>
        private void ShowStatus()
        {
            var sections = new List<InfoSection>();

            var you = new InfoSection { Title = "You" };
            you.Row("Rank", _state.RankName);
            you.Row("Next", RankProgressLabel());
            you.Row("Respect", _state.Respect.ToString("N0"));
            you.Row("Heat", HeatWord(), HeatTint());
            you.Row("Running with", _crew.IsAffiliated ? _crew.Current.Name : "nobody",
                    _crew.IsAffiliated ? _crew.Current.Colour : (Color?)Palette.TextDim);
            sections.Add(you);

            var trade = new InfoSection { Title = "Trade" };
            trade.Row("Deals closed", _state.TotalDealsMade.ToString("N0"));
            trade.Row("Moved", _state.GramsSold.ToString("0.#") + "g");
            trade.Row("Total earned", "$" + _state.TotalEarned.ToString("N0"), Palette.Cash);
            sections.Add(trade);

            var crews = new InfoSection { Title = "How the gangs see you" };
            foreach (var g in _gangs.All)
            {
                var standing = _crew.StandingFor(g.Id);
                var mine = _crew.IsAffiliated && _crew.Current.Id == g.Id;

                var value = standing.Rep.ToString("0");
                if (standing.Kills > 0) value += "  ·  " + standing.Kills + " kills";
                if (standing.MoneyEarned > 0) value += "  ·  $" + standing.MoneyEarned.ToString("N0");

                crews.Row(mine ? g.Name + " (you run with them)" : g.Name, value,
                          mine ? g.Colour
                               : standing.Rep < 0f ? Palette.Danger
                               : standing.Rep > 0f ? Palette.Cash : (Color?)Palette.TextDim);
            }
            sections.Add(crews);

            var ranks = new InfoSection { Title = "The ladder" };
            for (var i = 0; i < PlayerState.RankNames.Length; i++)
            {
                var reached = _state.Rank >= i;
                ranks.Row(PlayerState.RankNames[i],
                          _state.Rank == i ? "you are here"
                          : reached ? "passed"
                          : PlayerState.RankThresholds[i].ToString("N0") + " respect",
                          _state.Rank == i ? Palette.Cash : (Color?)Palette.TextDim);
            }
            sections.Add(ranks);

            Info?.Open("Status", _state.RankName, sections);
        }

        /// <summary>Prices, heat and what the block is doing to both.</summary>
        private void ShowTradeNumbers()
        {
            var sections = new List<InfoSection>();

            var holding = new InfoSection { Title = "Holding" };
            holding.Row("Ready to sell", Stash.TotalPackaged.ToString("0.#") + "g", Palette.Cash);
            holding.Row("Still to bag", Stash.TotalBulk.ToString("0.#") + "g", Palette.Warn);
            holding.Row("Free space", Stash.FreeSpace.ToString("0") + "g");
            holding.Row("Worth", "$" + PackagedValue().ToString("N0"), Palette.Cash);
            sections.Add(holding);

            sections.Add(BlockSection());

            var contacts = new InfoSection { Title = "Your contacts" };
            foreach (var s in _dealers.All)
            {
                contacts.Row(s.Name, ImportStatus(s), ImportTint(s));
            }
            sections.Add(contacts);

            var market = new InfoSection { Title = "Street prices" };
            foreach (var drug in _drugs.All)
            {
                // Quoted at full purity: what the street pays for the real thing, before
                // whatever the player has done to it.
                market.Row(drug.Name, "$" + _pricing.StreetPrice(drug, 1f).ToString("N0") + "/g");
            }
            sections.Add(market);

            Info?.Open("The numbers", _pricing.PriceContext(), sections);
        }

        /// <summary>
        /// What standing here actually means: who owns it, what it pays, what it costs you in
        /// attention, and whether anybody has you marked.
        /// </summary>
        private InfoSection BlockSection()
        {
            var block = new InfoSection { Title = "This block" };

            block.Row("Where", _turf.ZoneName, TurfTint());
            block.Row("Whose", _turf.Owner == null ? "nobody's" : _turf.Owner.Name,
                      _turf.Owner?.Colour ?? (Color?)Palette.TextDim);
            block.Row("To you", TurfWord(), TurfTint());
            block.Row("Pays", Multiplier(_turf.TurfPriceMultiplier),
                      _turf.TurfPriceMultiplier > 1.05f ? Palette.Cash : (Color?)Palette.Text);
            block.Row("Draws heat", Multiplier(_turf.TurfHeatMultiplier),
                      _turf.TurfHeatMultiplier > 1.2f ? Palette.Danger : (Color?)Palette.Cash);
            block.Row("Gang around", _crew.NearbyAllies > 0 ? _crew.NearbyAllies + " of yours" : "none",
                      _crew.NearbyAllies > 0 ? Palette.Cash : (Color?)Palette.TextDim);
            block.Row("Foot traffic", FootfallWord(),
                      _postUp.Footfall == 0 ? Palette.Warn : (Color?)Palette.Cash);
            block.Row("Been clocked", _turf.IsExposed ? "yes" : "not yet",
                      _turf.IsExposed ? Palette.Warn : (Color?)Palette.TextDim);

            return block;
        }

        /// <summary>The block on its own, from the This block page.</summary>
        private void ShowBlockNumbers()
        {
            var sections = new List<InfoSection> { BlockSection() };

            var risk = new InfoSection { Title = "If you work here" };
            risk.Row("Selling", _turf.Status == TurfStatus.Hostile ? "they will jump you"
                              : _turf.Status == TurfStatus.Home ? "safe enough"
                              : "nobody minds much",
                     TurfTint());
            risk.Row("Your heat", HeatWord(), HeatTint());
            sections.Add(risk);

            Info?.Open(_turf.ZoneName, TurfWord(), sections);
        }

        /// <summary>Heat in words, since a percentage tells the player nothing on its own.</summary>
        private string HeatWord()
        {
            if (_state.Notoriety > 50f) return "police are on you";
            if (_state.Notoriety > 20f) return "you have been noticed";
            return "nobody is looking";
        }

        /// <summary>A multiplier written the way a person would say it.</summary>
        private static string Multiplier(float m)
        {
            if (m >= 1.30f) return "much better than normal";
            if (m >= 1.05f) return "better than normal";
            if (m <= 0.70f) return "much worse than normal";
            if (m <= 0.95f) return "worse than normal";
            return "normal";
        }

        private string TurfWord()
        {
            switch (_turf.Status)
            {
                case TurfStatus.Home: return "your block";
                case TurfStatus.Hostile: return "enemy block -- dangerous";
                case TurfStatus.Foreign: return "somebody else's block";
                default: return "nobody's block";
            }
        }

        private Color HeatTint()
        {
            return _state.Notoriety > 50f ? Palette.Danger
                 : _state.Notoriety > 20f ? Palette.Warn
                 : Palette.Cash;
        }

        /// <summary>How busy the pavement is, without making the player read a count.</summary>
        private string FootfallWord()
        {
            var n = _postUp.Footfall;
            if (n == 0) return "dead out here";
            if (n <= 2) return "quiet";
            if (n <= 5) return "steady";
            return "busy -- and hot";
        }

        /// <summary>Purity as a dealer would describe it, not as a percentage.</summary>
        private static string PurityWord(float purity)
        {
            if (purity >= 0.95f) return "untouched";
            if (purity >= 0.75f) return "barely stepped on";
            if (purity >= 0.50f) return "cut half and half";
            return "stepped on hard";
        }

        private static Icon DrugIcon(DrugDef drug) => Icons.ForDrug(drug?.Id);

        private static string DrugNames(GangDef gang)
        {
            if (gang.Drugs.Count == 0) return "whatever they can get";

            var names = new List<string>();
            foreach (var id in gang.Drugs) names.Add(id);
            return string.Join(", ", names.ToArray());
        }

        private string RivalNames(GangDef gang)
        {
            if (gang.Rivals.Count == 0) return "nobody";

            var names = new List<string>();
            foreach (var id in gang.Rivals)
            {
                var rival = _gangs.Get(id);
                names.Add(rival == null ? id : rival.Name);
            }
            return string.Join(", ", names.ToArray());
        }

        /// <summary>Everything you have done for one gang, and what they hold.</summary>
        private void ShowGangDetail(GangDef gang)
        {
            var standing = _crew.StandingFor(gang.Id);
            var mine = _crew.IsAffiliated && _crew.Current.Id == gang.Id;

            var sections = new List<InfoSection>();

            var you = new InfoSection { Title = "You and them" };
            you.Row("Where you stand", mine ? "one of theirs" : RelationLabel(gang),
                    mine ? gang.Colour : standing.Rep < 0 ? Palette.Danger : (Color?)Palette.Text);
            you.Row("Rep with them", standing.Rep.ToString("N0"),
                    standing.Rep < 0 ? Palette.Danger : Palette.Cash);
            you.Row("Bodies for them", standing.Kills.ToString("N0"));
            you.Row("Deals done", standing.Deals.ToString("N0"));
            you.Row("Money made them", "$" + standing.MoneyEarned.ToString("N0"), Palette.Cash);
            sections.Add(you);

            var them = new InfoSection { Title = "Them" };
            them.Row("Blocks", gang.TurfHint);
            them.Row("Product", DrugNames(gang));
            them.Row("Beefing with", RivalNames(gang));
            them.Row("To get in", gang.JoinRespect > 0
                ? gang.JoinRespect.ToString("F0") + " respect"
                : "just ask their leader");
            sections.Add(them);

            Info?.Open(gang.Name, mine ? "You run with them" : RelationLabel(gang), sections);
        }

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
            page.WithIcon(Icons.Guns);

            page.AddSub("Dealing", "$", BuildDrugsPage,
                detail: "Re-up, bag up, and go to work",
                value: DrugsSummary(),
                enabled: !_deal.IsBusy && !_cutting.IsBusy,
                disabledReason: _deal.IsBusy ? "Already mid-deal" : "You are cutting");
            page.WithIcon(Icons.Weed);

            page.AddSub("Gangs", "%", BuildGangsPage,
                detail: _crew.IsAffiliated
                    ? "You run with " + _crew.Current.Name
                    : "Nobody has put you on yet",
                value: _crew.IsAffiliated ? _crew.Current.Tag : "SOLO");
            page.WithIcon(Icons.Mask);

            // What you are carrying, rather than how you are doing -- the stats moved under
            // Gangs, because who rates you is a gang question.
            page.Add("Inventory", "*", ShowInventory,
                detail: _stash.AtDoor ? "Move product between your pockets and the house" : "Everything you are carrying",
                value: _stash.AtDoor ? "at the house" : CarriedSummary());
            page.WithIcon(Icons.Stash);

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

            var page = new WheelPage("Dealing", DrugsSummary());

            // Two lines, in the words you would use out loud. Grams, multipliers and market
            // prices are a screenful on their own -- they live behind The numbers.
            page.PanelTitle = "On you";
            page.Row("Ready to sell", packaged > 0.005f ? packaged.ToString("0.#") + "g" : "nothing",
                     packaged > 0.005f ? Palette.Cash : (Color?)Palette.TextDim);
            page.Row("Still to prep", bulk > 0.005f ? bulk.ToString("0.#") + "g" : "nothing",
                     bulk > 0.005f ? Palette.Warn : (Color?)Palette.TextDim);

            page.AddSub("Re-up", "+", BuildSupplyPage,
                detail: SupplyDetail(),
                value: "$" + Game.Player.Money.ToString("N0"));
            page.WithIcon(Icons.Money);

            if (_postUp.IsPosted)
            {
                page.Add("Pack up", "x", () => _postUp.Stop("You packed up."),
                    detail: "Stop dealing and move on",
                    value: _postUp.Footfall + " passing");
                page.WithIcon(Icons.Tick);
            }
            else
            {
                page.AddSub("Post up", "$", BuildSellPage,
                    detail: "Stand on a corner and let it come to you",
                    value: packaged > 0.005f ? packaged.ToString("0.#") + "g ready" : "",
                    enabled: packaged > 0.005f,
                    disabledReason: bulk > 0.005f ? "All you have is weight -- bag it up first"
                                                  : "You are holding nothing");
                page.WithIcon(Icons.Cash);
            }

            page.Add("The numbers", "=", ShowTradeNumbers,
                detail: "Prices, heat, and what this block is doing to both",
                value: "");
            page.WithIcon(Icons.Health);

            return page;
        }

        private string StashDetail()
        {
            if (_stash.AtDoor) return "Move product in and out";

            var away = _stash.DistanceTo();
            return away > 8000f
                ? "Aunt Denise's, on Forum Drive"
                : "Aunt Denise's -- " + away.ToString("0") + "m away";
        }

        private string StashValue()
        {
            var total = _stash.Stash.Total;
            return total > 0.005f ? total.ToString("0.#") + "g at home" : "";
        }

        /// <summary>
        /// The stash house: what is at home, what is on you, and moving it between the two.
        ///
        /// Product left here is off your person, so neither a death nor a bust can touch it.
        /// The trip back to Forum Drive is the price of keeping it safe.
        /// </summary>
        private WheelPage BuildStashPage()
        {
            if (!_stash.AtDoor)
            {
                var away = new WheelPage("Stash house", _stash.Name);
                away.Add("Not there", "-", null,
                    detail: StashDetail(),
                    value: StashValue(),
                    enabled: false, disabledReason: "Go to Aunt Denise's");
                away.WithIcon(Icons.Locked);
                return away;
            }

            var den = _stash.Stash;
            var page = new WheelPage("Stash house", _stash.Name);

            page.PanelTitle = "At home";
            page.Row("Bagged", den.TotalPackaged.ToString("0.#") + "g", Palette.Cash);
            page.Row("Weight", den.TotalBulk.ToString("0.#") + "g", Palette.Warn);
            page.Row("Room here", den.FreeSpace.ToString("0") + "g");
            page.Row("On you", Stash.Total.ToString("0.#") + "g");

            // Item by item, on its own screen. Doing this on the wheel meant one wedge per
            // product per direction, which is a lot of flicking to move two things.
            page.Add("Move product", "=", OpenStashScreen,
                detail: "Pockets on the left, house on the right",
                value: "");
            page.WithIcon(Icons.Stash);

            page.Add("Leave it all", "v", DepositAll,
                detail: "Everything you are carrying, into the house",
                value: Stash.Total.ToString("0.#") + "g",
                enabled: Stash.Total > 0.005f,
                disabledReason: "You are carrying nothing");
            page.WithIcon(Icons.Cash);

            page.Add("Take it all", "^", WithdrawAll,
                detail: "As much as you can carry, out of the house",
                value: den.Total.ToString("0.#") + "g",
                enabled: den.Total > 0.005f && Stash.FreeSpace > 0.005f,
                disabledReason: den.Total <= 0.005f ? "There is nothing here" : "You are full");
            page.WithIcon(Icons.Tick);

            return page;
        }

        private void OpenStashScreen()
        {
            StashScreen?.Open(Stash, _stash.Stash, _drugs, () => _state.Touch());
        }
        private void DepositAll()
        {
            var moved = 0f;
            foreach (var d in _drugs.All) moved += MoveDrug(Stash, _stash.Stash, d.Id);

            _state.Touch();
            Notify.Ticker(moved > 0.005f
                ? "~g~Stashed " + moved.ToString("0.#") + "g.~s~"
                : "~o~Nothing moved.~s~");
        }

        private void WithdrawAll()
        {
            var moved = 0f;
            foreach (var d in _drugs.All) moved += MoveDrug(_stash.Stash, Stash, d.Id);

            _state.Touch();
            Notify.Ticker(moved > 0.005f
                ? "~g~Took " + moved.ToString("0.#") + "g out.~s~"
                : "~o~No room for any of it.~s~");
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

                    // Weapon art is a long letterbox, unlike the square menu sprites.
                    IconAspect = 2f,
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

            page.PanelTitle = _turf.ZoneName;
            page.Row("This spot", TurfWord(), TurfTint());
            page.Row("Foot traffic", FootfallWord(),
                     _postUp.Footfall == 0 ? Palette.Warn : Palette.Cash);
            page.Row("Gang around", _crew.NearbyAllies > 0 ? "yes" : "no",
                     _crew.NearbyAllies > 0 ? Palette.Cash : (Color?)Palette.TextDim);

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

                page.Add(product.Name, product.Tag,
                    () => PostUpWith(product),
                    detail: PurityWord(purity) +
                            (risk > 0.15f ? " -- buyers will notice" : ""),
                    value: stock.ToString("0.#") + "g  ·  $" + value.ToString("N0") + " a sale",
                    enabled: true);
                page.WithIcon(DrugIcon(product));
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

        // ---- supply ------------------------------------------------------------

        /// <summary>
        /// Supply is about people, not a catalogue. What this page shows depends entirely on
        /// who is in front of you: the dealer you are standing at, the one you called out and
        /// have not reached yet, or -- if neither -- who you could phone and where to find them.
        /// </summary>
        private WheelPage BuildSupplyPage()
        {
            // A delivery outranks everything: he drove out here for you.
            if (Delivery.IsActive)
            {
                if (Delivery.State == DeliveryState.Waiting && Delivery.Distance <= 4f)
                {
                    return BuildDealerPage(Delivery.Def);
                }

                var run = new WheelPage("Supply", Delivery.Status);
                run.PanelTitle = Delivery.Def.Name;
                run.Row("Where", Delivery.State == DeliveryState.Calling
                                 ? "on the phone"
                                 : Delivery.Distance.ToString("0") + "m away");
                run.Row("Carries", Carries(Delivery.Def));
                run.Row("Price", Multiplier(1f / Math.Max(0.01f, Delivery.Def.PriceMultiplier)));

                run.Add("Waiting on him", ">", null,
                    detail: Delivery.State == DeliveryState.Waiting
                        ? "He is parked up. Walk over to the car."
                        : "Follow the blip. He is driving to you.",
                    value: Delivery.Distance.ToString("0") + "m",
                    enabled: false,
                    disabledReason: "He is on his way");
                run.WithIcon(Icons.Garage);

                run.Add("Call it off", "x", () => Delivery.Cancel("Told him not to bother."),
                    detail: "Send him back",
                    value: "");
                run.WithIcon(Icons.Warning);

                return run;
            }

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

            // Nobody in reach. Exactly one thing you can do from here: phone the docks. The
            // gangs are people you walk up to and the independents are places you drive to, so
            // listing all seven as pickable wedges was a directory pretending to be a menu.
            var list = new WheelPage("Re-up", "Buying weight");

            list.PanelTitle = "Where weight comes from";
            list.Row("Cash", "$" + Game.Player.Money.ToString("N0"), Palette.Cash);
            list.Row("Room left", Stash.FreeSpace.ToString("0") + "g");
            list.Row("Gangs", "talk to their leader", Palette.TextDim);
            list.Row("The docks", _state.DocksUnlocked ? "they deliver" : "you do not know them",
                     _state.DocksUnlocked ? Palette.Cash : (Color?)Palette.TextDim);

            var docks = _dealers.Docks();

            if (docks == null)
            {
                list.Add("Nothing", "-", null,
                    detail: "No contacts in dealers.json",
                    enabled: false, disabledReason: "Nobody to call");
                return list;
            }

            if (!_state.DocksUnlocked)
            {
                var toGo = DealerManager.GramsUntilSource(_state, _cfg.DocksUnlockGrams);

                list.Add("Call the docks", "=", null,
                    detail: "Ask Uncle Dee where it comes from once you have moved enough",
                    value: toGo.ToString("0") + "g more to sell",
                    enabled: false, disabledReason: "You do not know anyone at the port");
                list.WithIcon(Icons.Locked);
                return list;
            }

            var blocked = _dealers.RefusalReason(docks, _state, _crew);

            list.Add("Call the docks", "=", () => Call(docks),
                detail: blocked == null
                    ? docks.Name + " drives out to you with whatever you want"
                    : blocked,
                value: "everything, cheapest",
                enabled: blocked == null,
                disabledReason: blocked ?? "");
            list.WithIcon(Icons.Money);

            return list;
        }

        private static string Carries(DealerDef def)
        {
            return def.Drugs.Count == 0
                ? "everything"
                : string.Join(", ", def.Drugs.ToArray()).ToUpperInvariant();
        }

        /// <summary>
        /// Phones a contact out.
        ///
        /// The docks deliver: that is what moving real weight buys you, so he drives to wherever
        /// you are standing rather than naming a spot for you to drive to. Everyone else still
        /// picks a rendezvous, because making you travel is the whole shape of the early game.
        /// </summary>
        private void Call(DealerDef def)
        {
            if (def.Kind == DealerKind.Docks && Delivery != null)
            {
                var refusal = _dealers.RefusalReason(def, _state, _crew);
                if (refusal != null)
                {
                    Notify.Problem(refusal.ToLowerInvariant() + ".");
                    return;
                }

                var failed = Delivery.Call(def);
                if (failed != null) Notify.Problem(failed);
                return;
            }

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

                page.Add(product.Name, product.Tag,
                    () => Buy(def, product, lot, cost),
                    detail: "$" + _pricing.WholesalePrice(product, mult).ToString("0") + " a gram" +
                            (hasStock ? "  ·  he has " + onHand.ToString("0") + "g" : ""),
                    value: hasStock ? lot.ToString("0") + "g for $" + cost.ToString("N0") : "none left",
                    enabled: reason.Length == 0,
                    disabledReason: reason);
                page.WithIcon(Icons.ForDrug(product.Id));
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

            _crew.CreditPurchase();

            Notify.Ticker("~y~-$" + charged.ToString("N0") + "~s~  " + accepted.ToString("0.#") +
                          "g bulk " + product.Name);
            Log.Info("Bought " + accepted.ToString("0.##") + "g bulk " + product.Id +
                     " from " + def.Id + " for $" + charged + ".");

            // He drove out here for one job. Once it is done he walks back to the car and goes.
            if (Delivery != null && Delivery.IsActive && Delivery.Def != null && Delivery.Def.Id == def.Id)
            {
                Delivery.Finish();
            }
        }

        // ---- gangs -------------------------------------------------------------

        /// <summary>
        /// One wedge per gang. Picking one opens that gang's own page rather than joining
        /// immediately -- every gang is an entity you can inspect, deal with, or sign up to,
        /// and a mis-flick should never silently change who you run with.
        /// </summary>
        private WheelPage BuildGangsPage()
        {
            var page = new WheelPage("Gangs",
                _crew.IsAffiliated ? "Running with " + _crew.Current.Name : "Running solo");

            // Where you are and who is around you -- the things that change what happens if you
            // pull something out here.
            page.PanelTitle = _crew.IsAffiliated ? _crew.Current.Name : "Not with anybody";
            page.Row("You are on", _turf.ZoneName, TurfTint());
            page.Row("Whose block", TurfWord(), TurfTint());
            page.Row("Gang around", _crew.NearbyAllies > 0 ? "yes" : "no",
                     _crew.NearbyAllies > 0 ? Palette.Cash : (Color?)Palette.TextDim);

            page.AddSub("This block", "#", BuildTurfPage,
                detail: TurfWord(),
                value: _turf.ZoneName);
            page.WithIcon(Icons.Garage);

            if (_crew.IsAffiliated)
            {
                // You run WITH them; you do not run them. One wedge for the gang you are down
                // with -- listing all seven turned the wheel into a directory.
                var mine = _crew.Current;

                page.AddSub("Who you run with", "*", () => BuildGangPage(mine),
                    detail: "They run " + mine.TurfHint,
                    value: mine.Name);
                page.Items[page.Items.Count - 1].Tint = mine.Colour;
                page.WithIcon(Icons.Mask);

                // Work comes from Lamar in person, so the wheel points at him rather than
                // pretending to hand out jobs itself.
                page.Add("Work", "!", null,
                    detail: "Lamar has the jobs. He is marked on your map.",
                    value: "",
                    enabled: false, disabledReason: "Go and see Lamar");
                page.WithIcon(Icons.Warning);
            }

            else
            {
                // Joining is a conversation, not a wedge. Even standing in front of the man the
                // wheel does not offer it -- you press D-pad right and ask him yourself.
                var leader = _leaders.InReach;

                page.Add("Not with anybody", "-", null,
                    detail: leader != null
                        ? "Press D-pad right to talk to " + leader.Name
                        : "Gang leaders are marked on your map. Go and talk to one.",
                    value: leader != null ? leader.Name.ToUpperInvariant() : "SOLO",
                    enabled: false,
                    disabledReason: leader != null ? "Talk to him" : "Go and find one");
                page.WithIcon(Icons.Locked);
            }

            // Standing is a gang question, so the readout lives here rather than on the root.
            page.Add("How you stand", "*", ShowStatus,
                detail: "Your rank, your heat, and what every gang thinks of you",
                value: _state.RankName);
            page.WithIcon(Icons.Tattoo);

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

        /// <summary>Everything you can do with one particular gang.</summary>
        private WheelPage BuildGangPage(GangDef gang)
        {
            var mine = _crew.IsAffiliated && _crew.Current.Id == gang.Id;
            var standing = _crew.StandingFor(gang.Id);
            var atWar = _crew.IsAffiliated && !mine &&
                        (_crew.Current.IsRivalOf(gang.Id) || gang.IsRivalOf(_crew.Current.Id));

            var page = new WheelPage(gang.Name, mine ? "You run with them" : RelationLabel(gang));

            page.PanelTitle = gang.Name;
            page.Row("They run", gang.TurfHint);
            page.Row("They move", DrugNames(gang));
            page.Row("Beefing with", RivalNames(gang));
            page.Row("With you", mine ? "you run with them" : RelationLabel(gang),
                     mine ? gang.Colour : atWar ? Palette.Danger : (Color?)null);

            // Join / leave.
            if (mine)
            {
                page.Add("Walk away", "x", () => _crew.Leave(),
                    detail: "Stop running with them. They will not forget it.",
                    value: "");
                page.WithIcon(Icons.Warning);
            }
            else
            {
                // Joining is a conversation with the man himself, never a wedge.
                page.Add("You are not with them", "-", null,
                    detail: "Find their leader on the map and ask him yourself",
                    value: "",
                    enabled: false,
                    disabledReason: "Go and talk to them");
                page.WithIcon(Icons.Locked);
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

                page.Add("Call the plug", "+", () => Call(plug),
                    detail: plug.BuyLine,
                    value: carries + "  ·  " + Multiplier(1f / Math.Max(0.01f, mult)),
                    enabled: refusal == null,
                    disabledReason: refusal ?? "");
                page.WithIcon(Icons.Money);
            }
            else
            {
                page.Add("Call the plug", "+", null,
                    detail: "They have nobody you can call",
                    enabled: false, disabledReason: "No contact");
                page.WithIcon(Icons.Locked);
            }

            // Borrowing. Only the gang you run with will front you anything.
            var loan = _crew.Loan;
            var theirLoan = loan != null && loan.IsActive &&
                            string.Equals(loan.GangId, gang.Id, StringComparison.OrdinalIgnoreCase);

            page.AddSub("Borrow money", "$", () => BuildLoanPage(gang),
                detail: theirLoan                        ? "You owe $" + loan.TotalOwed.ToString("N0") + ", due in " + loan.DaysLeft + " days"                        : mine ? "They will front you against your name"
                           : "They will not front you anything",
                value: theirLoan ? "$" + loan.TotalOwed.ToString("N0") + " owed" : "",
                enabled: mine || theirLoan,
                disabledReason: loan != null && loan.IsActive ? "You already owe " + loan.GangId
                                                              : "Not the gang you run with");
            page.WithIcon(Icons.Cash);

            page.Add("How you stand", "*", () => ShowGangDetail(gang),
                detail: "What you have done for them, and what they hold",
                value: "");
            page.WithIcon(Icons.Tattoo);

            return page;
        }

        /// <summary>
        /// Borrowing from the gang you run with, and the vig that follows. The offer scales with rank,
        /// because a Pee-Wee has nothing to lend against.
        /// </summary>
        private WheelPage BuildLoanPage(GangDef gang)
        {
            var loan = _crew.Loan;
            var active = loan != null && loan.IsActive &&
                         string.Equals(loan.GangId, gang.Id, StringComparison.OrdinalIgnoreCase);

            var page = new WheelPage("Borrow money", gang.Name);

            if (active)
            {
                page.PanelTitle = "What you owe";
                page.Row("Owed", "$" + loan.TotalOwed.ToString("N0"), Palette.Danger);
                page.Row("Due", loan.DaysLeft <= 0 ? "now" : "in " + loan.DaysLeft + " days",
                         loan.DaysLeft <= 1 ? Palette.Danger : (Color?)null);
                page.Row("You have", "$" + Game.Player.Money.ToString("N0"), Palette.Cash);

                if (loan.MissedPeriods > 0)
                {
                    page.Row("Missed payments", loan.MissedPeriods.ToString(), Palette.Danger);
                }

                page.Add("Pay the vig", "$", PayVig,
                    detail: "Buys you another " + _cfg.LoanPeriodDays + " days. You still owe the rest.",
                    value: "$" + loan.Vig.ToString("N0"),
                    enabled: Game.Player.Money >= loan.Vig,
                    disabledReason: "You are $" + (loan.Vig - Game.Player.Money).ToString("N0") + " short");
                page.WithIcon(Icons.Money);

                page.Add("Clear it", "*", PayOff,
                    detail: "Pay the lot and be done with them",
                    value: "$" + loan.TotalOwed.ToString("N0"),
                    enabled: Game.Player.Money >= loan.TotalOwed,
                    disabledReason: "You are $" + (loan.TotalOwed - Game.Player.Money).ToString("N0") + " short");
                page.WithIcon(Icons.Tick);

                return page;
            }

            var cap = MaxLoanFor();

            page.PanelTitle = "What they will do";
            page.Row("They will lend", "up to $" + cap.ToString("N0"), Palette.Cash);
            page.Row("You pay back", _cfg.LoanVigPercent.ToString("0") + "% on top, every " +
                                     _cfg.LoanPeriodDays + " days");
            page.Row("Miss it", "they write you off", Palette.Warn);

            if (cap < 100)
            {
                page.Add("Nothing", "-", null,
                    detail: "You are not worth lending to yet",
                    enabled: false, disabledReason: "Make a name first");
                page.WithIcon(Icons.Locked);
                return page;
            }

            foreach (var fraction in new[] { 0.25f, 0.5f, 1f })
            {
                var amount = (int)Math.Round(cap * fraction / 100f) * 100;
                if (amount < 100) continue;

                var vig = Math.Max(1, (int)Math.Round(amount * _cfg.LoanVigPercent / 100f));

                page.Add("$" + (amount / 1000f).ToString("0.#") + "k", "$",
                    () => Borrow(gang, amount),
                    detail: "Costs you $" + vig.ToString("N0") + " every " + _cfg.LoanPeriodDays + " days",
                    value: "$" + amount.ToString("N0"));
                page.WithIcon(Icons.Cash);
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

        // ---- turf --------------------------------------------------------------

        private WheelPage BuildTurfPage()
        {
            var page = new WheelPage("This block", _turf.ZoneName);

            page.PanelTitle = _turf.ZoneName;
            page.Row("Whose", _turf.Owner == null ? "nobody's" : _turf.Owner.Name,
                     _turf.Owner?.Colour ?? (Color?)Palette.TextDim);
            page.Row("To you", TurfWord(), TurfTint());
            page.Row("Been clocked", _turf.IsExposed ? "yes -- they have seen you" : "not yet",
                     _turf.IsExposed ? Palette.Warn : (Color?)Palette.TextDim);

            page.Add("The numbers", "=", ShowBlockNumbers,
                detail: "What this block pays and what it costs you",
                value: "");
            page.WithIcon(Icons.Health);

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

        // ---- reputation ---------------------------------------------------------

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
