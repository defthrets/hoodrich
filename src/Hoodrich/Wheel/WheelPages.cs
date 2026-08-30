using System;
using System.Collections.Generic;
using Color = System.Drawing.Color;
using GTA;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Dealing;
using Hoodrich.Economy;
using Hoodrich.Gangs;
using Hoodrich.Locations;
using Hoodrich.Missions;
using Hoodrich.Social;
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
        private readonly PlayerState _state;
        private readonly Drugs _drugs;
        private readonly Pricing _pricing;
        private readonly Cutting _cutting;
        private readonly GangRegistry _gangs;
        private readonly Affiliation _crew;
        private readonly TurfWatch _turf;
        private readonly DealerManager _dealers;
        private readonly Core.Settings _cfg;
        private readonly StashHouse _stash;
        private readonly PostUp _postUp;
        private readonly GangLeaders _leaders;
        private readonly WeaponRegistry _weapons;

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

        /// <summary>
        /// What Lamar has for you, or null. Set by the house script from the mission runner.
        ///
        /// A function rather than the runner itself, because the page wants one fact and
        /// handing a whole subsystem to the menu so it can ask one question is how a menu ends
        /// up able to start missions by accident.
        /// </summary>
        public Func<MissionDef> WorkWaiting;

        /// <summary>Set by Main. Moving product between your pockets and the house.</summary>
        public StashScreen StashScreen;

        /// <summary>Set by Main. What you are carrying, away from the house.</summary>
        public PocketScreen PocketScreen;

        /// <summary>Set by Main: opens the feed, and reads the follower count for the wedge.</summary>
        public Action ShowSocials;

        /// <summary>Set by Main: hail one, or say why not. See Locations.Knowai.</summary>
        public Func<string> HailRide;

        /// <summary>Set by Main: name a stop from the back seat.</summary>
        public Func<Locations.RideStop, string> RideTo;

        /// <summary>Set by Main: what the car is doing, if anything.</summary>
        public Func<Locations.RideState> RideState;
        public Func<string> RideGoing;
        public Action CancelRide;

        /// <summary>Opens the can. See UI.GraffitiScreen.</summary>
        public Action ShowGraffiti;

        /// <summary>How many marks are up, for the tile. Null while it is not wired.</summary>
        public Func<int> MarksUp;

        /// <summary>Set by Main: opens the inbox.</summary>
        public Action ShowMessages;

        /// <summary>Set by Main. Lamar's work, and the paint on the walls that came out of it.</summary>
        public Missions.MissionRunner Jobs;

        /// <summary>
        /// Opens the settings screen.
        ///
        /// A wedge used to BUILD a page of five toggles here. Five was never the number -- there
        /// are fifty-one settings in the ini -- and a ring of eight things you pick between is
        /// the wrong shape for a column of things you adjust.
        /// </summary>
        public Action ShowSettings;

        /// <summary>
        /// Closes ours and hands the button back so the game's own phone can be opened.
        ///
        /// A phone that has replaced the phone owes the player the real one back. Franklin's
        /// contacts, Lester, the emergency services and every story number live in there, and
        /// none of it is ours to take away.
        /// </summary>
        public Action ShowVanillaPhone;

        /// <summary>Whether somebody is already on their way about something you said.</summary>
        public Func<bool> PaybackDue;
        public Func<int> Followers;

        /// <summary>Set by Main: clears the feed and the follower count.</summary>
        public Action WipeSocials;

        /// <summary>
        /// Every job in the book, for the unlock-everything row.
        ///
        /// A hook rather than a reference, because this class has no business holding the
        /// mission catalogue for the sake of one row in a settings list -- and because a null
        /// hook means that row unlocks everything else and no jobs, which is a smaller failure
        /// than not compiling.
        /// </summary>
        public Func<IEnumerable<string>> AllJobs;

        public WheelPages(Core.Settings cfg, PlayerState state, Drugs drugs, Pricing pricing,
                          Cutting cutting, GangRegistry gangs, Affiliation crew, TurfWatch turf,
                          DealerManager suppliers, WeaponRegistry weapons,
                          StashHouse stash, PostUp postUp, GangLeaders leaders)
        {
            _cfg = cfg;
            _state = state;
            _drugs = drugs;
            _pricing = pricing;
            _cutting = cutting;
            _gangs = gangs;
            _crew = crew;
            _turf = turf;
            _dealers = suppliers;
            _weapons = weapons;
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

            // And anywhere else it is the same list with nowhere to push it. One container, and
            // the only way out of it is the floor.
            if (PocketScreen != null)
            {
                PocketScreen.Open(Stash, _drugs, Bags);
                return;
            }

            var sections = new List<InfoSection>();

            // ---- on you ------------------------------------------------------
            // Capacity is the only number here that can stop you working, and it was two rows
            // at the bottom stating one fact twice -- what you carry, and what is left.
            var onYou = new InfoSection { Title = "On you" };

            var kinds = 0;
            var readyGrams = 0f;

            foreach (var d in _drugs.All)
            {
                var h = Stash.PackagedOf(d.Id);
                if (h <= 0.005f) continue;

                kinds++;
                readyGrams += h;
            }

            onYou.Hero("Worth bagged up", Money(PackagedValue()), Palette.Cash,
                       kinds == 0
                           ? "nothing ready to move"
                           : kinds + (kinds == 1 ? " kind  ·  " : " kinds  ·  ") +
                             readyGrams.ToString("0.#") + "g ready",
                r => r.ArtFile = "money.png");

            var cap = Math.Max(1f, Stash.Capacity);

            onYou.Meter("Carrying",
                        Stash.Total.ToString("0.#") + "g of " + cap.ToString("0") + "g",
                        Stash.Total / cap,
                        Stash.FreeSpace < 15f ? Palette.Danger
                            : Stash.FreeSpace < 40f ? Palette.Warn
                            : Palette.Cash,
                        Stash.FreeSpace.ToString("0") + "g of room left",
                r => r.ArtFile = "stash.png");

            onYou.Row("Cash", Money(Game.Player.Money), Palette.Cash,
                r => r.ArtFile = "cash.png");
            sections.Add(onYou);

            // ---- ready to sell -----------------------------------------------
            // Somebody opening this screen is deciding WHAT TO SELL, and the money decides it.
            // So the money is the bright number on the right, and how much you have and how
            // badly it is cut go on the grey line under the name -- which is finally where the
            // purity gets said out loud, and therefore why your coke earns less than your meth.
            var ready = new InfoSection { Title = "Ready to sell" };
            var bagged = 0;

            foreach (var drug in _drugs.All)
            {
                var have = Stash.PackagedOf(drug.Id);
                if (have <= 0.005f) continue;

                var purity = Stash.PurityOf(drug.Id);
                var note = drug.Amount(have) + "  ·  " + PurityWord(purity);
                var art = Icons.ForDrug(drug.Id);

                // The mark goes on the packaged list and nowhere else, because this is the
                // only list where purity is a number that varies. Weight still to be bagged is
                // uncut by definition -- marking every row of it with a full disc would be
                // four identical pictures saying something the heading already says.
                var mark = Stash.Mark(purity);

                ready.Row(drug.Name, "$" + _pricing.SaleValue(drug, have, purity).ToString("N0"),
                          Palette.Cash,
                          r => { r.Note = note; r.Art = art; r.ArtTint = ProductArt;
                                 r.MarkFile = mark; });

                bagged++;
            }

            if (bagged == 0) ready.Row("Nothing bagged up", "", Palette.TextDim,
                r => r.ArtFile = "box.png");
            sections.Add(ready);

            // ---- still to bag up ---------------------------------------------
            var weight = new InfoSection { Title = "Still to bag up" };
            var raw = 0f;

            foreach (var drug in _drugs.All)
            {
                var have = Stash.BulkOf(drug.Id);
                if (have <= 0.005f) continue;

                raw += have;

                var note = "worth nothing until you " + SplitPhrase(drug.SplitVerb);
                var art = Icons.ForDrug(drug.Id);

                weight.Row(drug.Name, drug.Bulk(have), Palette.Warn,
                           r => { r.Note = note; r.Art = art; r.ArtTint = ProductArt; });
            }

            if (raw <= 0.005f)
            {
                weight.Row("No weight on you", "", Palette.TextDim,
                    r => r.ArtFile = "box.png");
            }
            else
            {
                weight.Total = raw.ToString("0.#") + "g";
                weight.TotalColour = Palette.Warn;
            }

            sections.Add(weight);

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

                    // WEIGHED, because this line adds the two halves together and one of them
                    // is powder. For everything except the two pill entries the question does
                    // not arise. For those it does, and neither answer is completely right --
                    // a number that is partly grams of un-pressed powder and partly finished
                    // bars is not honestly a count of anything, and calling the total "35 bars"
                    // claims you have thirty-five bars when some of it has never seen a press.
                    // Grams is the unit both halves are actually stored in, so grams is the
                    // half-truth that does not overstate what is in the house.
                    home.Row(drug.Name, drug.Bulk(have), Palette.Cash);
                    kept++;
                }

                if (kept == 0) home.Row("Empty", "", Palette.TextDim,
                    r => r.ArtFile = "box.png");
                home.Row("Room here", den.FreeSpace.ToString("0") + "g", null,
                    r => r.ArtFile = "garage.png");
                sections.Add(home);
            }

            Info?.Open("Inventory",
                       _stash.AtDoor ? "At the stash house" : CarriedSummary(),
                       sections);
        }

        /// <summary>
        /// The tint for art that says WHAT a thing is rather than how it is going.
        ///
        /// A coke sprite tinted the money colour is a green brick. Product art is identity, so
        /// it always draws neutral whether it resolved to a game sprite or fell through to one
        /// of ours.
        /// </summary>
        private static readonly Color ProductArt = Color.FromArgb(235, 255, 255, 255);

        /// <summary>
        /// "Bag up" becomes "bag it up", "Cut" becomes "cut it".
        ///
        /// A two-word verb takes its object in the MIDDLE, which is the whole difference
        /// between the mod's voice and "bag up it".
        /// </summary>
        private static string SplitPhrase(string verb)
        {
            var v = (verb ?? "cut").ToLowerInvariant();
            var space = v.IndexOf(' ');

            return space < 0 ? v + " it" : v.Substring(0, space) + " it " + v.Substring(space + 1);
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
            var maxed = _state.Rank >= PlayerState.RankNames.Length - 1;

            // ---- you ---------------------------------------------------------
            var you = new InfoSection { Title = "You" };

            you.Hero("Rank", _state.RankName, Palette.Text,
                     maxed
                         ? "top of the ladder"
                         : "OG at " +
                           PlayerState.RankThresholds[PlayerState.RankThresholds.Length - 1]
                               .ToString("N0") + " respect",
                r => r.ArtFile = "rank.png");

            if (!maxed)
            {
                var need = PlayerState.RankThresholds[_state.Rank + 1] - _state.Respect;

                you.Meter("Next up", PlayerState.RankNames[_state.Rank + 1], _state.RankProgress,
                          Palette.Cash,
                          Math.Max(0f, need).ToString("N0") + " respect to go",
                    r => r.ArtFile = "rank.png");
            }

            // Five rows saying "you are third of five" become one row that shows it.
            you.Row("The ladder", (_state.Rank + 1) + " of " + PlayerState.RankNames.Length,
                    Palette.Cash,
                    r =>
                    {
                        // Inside the hand it already had, the way the Heat row below does it.
                        // Appending a second lambda would be Row(label, value, colour, Action,
                        // Action), which is not an overload that exists.
                        r.ArtFile = "rank.png";
                        r.Pips = PlayerState.RankNames.Length;
                        r.PipsOn = _state.Rank;
                        r.PipAt = _state.Rank;
                    });

            // The word for heat only moves at twenty and fifty, so a man at nineteen and a man
            // at one read identically and somebody watching it climb sees nothing until it
            // jumps. The pips show the climb.
            you.Row("Heat", HeatShort(), HeatTint(),
                    r =>
                    {
                        r.ArtFile = "police.png";
                        r.Pips = 5;
                        r.PipsOn = Math.Max(0, Math.Min(5,
                            (int)Math.Ceiling(_state.Notoriety / 20f)));
                    });

            you.Row("Running with", _crew.IsAffiliated ? _crew.Current.Name : "nobody",
                    _crew.IsAffiliated ? Palette.Text : (Color?)Palette.TextDim,
                    r =>
                    {
                        if (!_crew.IsAffiliated) return;

                        r.Tab = _crew.Current.Colour;
                        r.ArtFile = "tick.png";
                    });

            sections.Add(you);

            // ---- your set ----------------------------------------------------
            //
            // This was its own wheel page one flick away -- "Who you run with" -- which asked
            // the same question this screen asks and answered it in a different kind of
            // window. Four rows do not need a page of their own, and standing and set are one
            // subject: who you are with is most of what your standing IS.
            if (_crew.IsAffiliated)
            {
                var mine = _crew.Current;
                var set = new InfoSection { Title = "Your set" };

                set.Hero("Running with", mine.Name, mine.Colour,
                         "They run " + mine.TurfHint,
                    r => r.ArtFile = "mask.png");

                set.Row("They move", DrugNames(mine), null,
                    r => r.ArtFile = "pills.png");

                set.Row("Their old rivals", RivalNames(mine), null,
                    r => r.ArtFile = "guns.png");

                var beefing = _crew.Beefing(mine.Id);

                set.Row("Beef", beefing ? "at war" : "no problem",
                        beefing ? Palette.Danger : Palette.Cash,
                    r => r.ArtFile = beefing ? "warning.png" : "tick.png");

                sections.Add(set);
            }

            // ---- trade -------------------------------------------------------
            var trade = new InfoSection { Title = "Trade" };

            // Deals and grams are the supporting detail for the money, which is what a note is.
            trade.Hero("Total earned", Money(_state.TotalEarned), Palette.Cash,
                       _state.TotalDealsMade.ToString("N0") + " deals  ·  " +
                       _state.GramsSold.ToString("0.#") + "g moved",
                r => r.ArtFile = "money.png");

            sections.Add(trade);

            // ---- how the gangs see you ---------------------------------------
            var crews = new InfoSection { Title = "How the gangs see you" };
            var quiet = 0;

            foreach (var g in _gangs.All)
            {
                var standing = _crew.StandingFor(g.Id);
                var mine = _crew.IsAffiliated && _crew.Current.Id == g.Id;

                // A gang you have never met is not a fact about you, it is the absence of one.
                // Six of these were filling most of the screen with the number nought.
                if (!mine && Math.Abs(standing.Rep) < 0.5f && standing.Kills == 0 &&
                    standing.MoneyEarned == 0)
                {
                    quiet++;
                    continue;
                }

                var atWar = !mine && standing.Rep <= Affiliation.BeefAt;

                // The bands come off BeefAt, which is the only real threshold in the system --
                // below it they raid your blocks and the drive-bys come from them.
                var word = mine ? "one of theirs"
                    : atWar ? "at war with you"
                    : standing.Rep < 0f ? "bad blood"
                    : standing.Rep >= 100f ? "tight with you"
                    : "cool with you";

                var tint = mine ? Palette.Cash
                    : atWar ? Palette.Danger
                    : standing.Rep < 0f ? Palette.Warn
                    : Palette.Cash;

                var note = "rep " + standing.Rep.ToString("0");

                if (standing.Kills > 0)
                {
                    note += "  ·  " + standing.Kills +
                            (standing.Kills == 1 ? " body" : " bodies") + " for them";
                }

                if (standing.MoneyEarned > 0)
                {
                    note += "  ·  $" + standing.MoneyEarned.ToString("N0") + " earned them";
                }

                var them = g;
                var isMine = mine;
                var war = atWar;
                var kills = standing.Kills;

                crews.Row(g.Name, word, tint,
                          r =>
                          {
                              r.Note = note;

                              // Their colour as a strip, never as the text. These run from
                              // yellow to deep maroon and half of them are unreadable as ink.
                              r.Tab = them.Colour;

                              if (isMine) r.ArtFile = "tick.png";
                              else if (war || kills > 0) r.ArtFile = "skull.png";
                          });
            }

            if (quiet > 0)
            {
                crews.Row(quiet + (quiet == 1 ? " other set" : " other sets"),
                          "never dealt with you", Palette.TextDisabled);
            }

            sections.Add(crews);

            Info?.Open("Status", _state.Respect.ToString("N0") + " respect", sections);
        }

        /// <summary>Heat in two words, for a row whose pips already show the amount.</summary>
        private string HeatShort()
        {
            if (_state.Notoriety > 50f) return "on you";
            if (_state.Notoriety > 20f) return "noticed";
            return "clear";
        }

        /// <summary>
        /// Money, shortened only where it would otherwise be trimmed.
        ///
        /// A hero draws at double a body row and a nine-figure sum does not fit a panel this
        /// narrow. Below eight figures the exact number is worth more than the tidiness.
        /// </summary>
        private static string Money(long amount)
        {
            if (amount >= 10000000L) return "$" + (amount / 1000000f).ToString("0.#") + "M";
            return "$" + amount.ToString("N0");
        }

        /// <summary>Prices, heat and what the block is doing to both.</summary>
        private void ShowTradeNumbers()
        {
            var sections = new List<InfoSection>();

            var holding = new InfoSection { Title = "On you" };
            var carried = DrugLines(Stash);

            if (carried.Count == 0) holding.Row("Nothing on you", "", Palette.TextDim,
                r => r.ArtFile = "box.png");
            foreach (var line in carried) holding.Row(line[0], line[1], Palette.Cash);

            holding.Row("Free space", Stash.FreeSpace.ToString("0") + "g", null,
                        r => r.ArtFile = "box.png");
            holding.Row("Worth", "$" + PackagedValue().ToString("N0"), Palette.Cash,
                r => r.ArtFile = "money.png");
            sections.Add(holding);

            var atHouse = new InfoSection { Title = "At the house" };
            var stored = _stash == null ? new List<string[]>() : DrugLines(_stash.Stash);

            if (stored.Count == 0) atHouse.Row("Nothing at the house", "", Palette.TextDim,
                r => r.ArtFile = "garage.png");
            foreach (var line in stored) atHouse.Row(line[0], line[1]);

            if (_stash != null)
            {
                atHouse.Row("Free space", _stash.Stash.FreeSpace.ToString("0") + "g", null,
                            r => r.ArtFile = "box.png");
            }

            sections.Add(atHouse);

            sections.Add(BlockSection());

            var contacts = new InfoSection { Title = "Your contacts" };
            foreach (var s in _dealers.All)
            {
                contacts.Row(s.Name, ImportStatus(s), ImportTint(s));
            }
            sections.Add(contacts);

            var market = new InfoSection { Title = "What things go for" };
            foreach (var drug in _drugs.All)
            {
                // The ladder, literally. These are the numbers that get paid -- nothing on this
                // line is an estimate or a quote-before-modifiers, because there are no
                // modifiers left. The hour and the block change how often somebody buys.
                market.Row(drug.Name, drug.Ladder());
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

            block.Row("Where", _turf.ZoneName, TurfTint(),
                r => r.ArtFile = "pin.png");

            // The owner's own emblem, in the owner's own colour. It is the one row on this
            // panel where the art means IDENTITY rather than state, so it keeps its tint
            // instead of taking the row's.
            block.Row("Whose", _turf.Owner == null ? "nobody's" : _turf.Owner.Name,
                      _turf.Owner?.Colour ?? (Color?)Palette.TextDim,
                      r =>
                      {
                          if (_turf.Owner == null) return;

                          r.ArtFile = Icons.ForGang(_turf.Owner.Id).File;
                          r.ArtTint = _turf.Owner.Colour;
                          r.Tab = _turf.Owner.Colour;
                      });

            block.Row("To you", TurfWord(), TurfTint(), r => r.ArtFile = "mask.png");
            block.Row("Pays", Multiplier(_turf.TurfPriceMultiplier),
                      _turf.TurfPriceMultiplier > 1.05f ? Palette.Cash : (Color?)Palette.Text,
                      r => r.ArtFile = "cash.png");
            block.Row("Draws heat", Multiplier(_turf.TurfHeatMultiplier),
                      _turf.TurfHeatMultiplier > 1.2f ? Palette.Danger : (Color?)Palette.Cash,
                      r => r.ArtFile = "police.png");
            block.Row("Gang around", _crew.NearbyAllies > 0 ? _crew.NearbyAllies + " of yours" : "none",
                      _crew.NearbyAllies > 0 ? Palette.Cash : (Color?)Palette.TextDim,
                      r => r.ArtFile = "people.png");
            block.Row("Foot traffic", FootfallWord(),
                      _postUp.Footfall == 0 ? Palette.Warn : (Color?)Palette.Cash,
                      r => r.ArtFile = "footfall.png");
            block.Row("Been clocked", _turf.IsExposed ? "yes" : "not yet",
                      _turf.IsExposed ? Palette.Warn : (Color?)Palette.TextDim,
                      r => r.ArtFile = "warning.png");

            return block;
        }

        /// <summary>The block on its own, from the This block page.</summary>
        private void ShowBlockNumbers()
        {
            var sections = new List<InfoSection> { BlockSection() };

            var risk = new InfoSection { Title = "If you work here" };
            risk.Row("Serving", _turf.Status == TurfStatus.Hostile ? "they'll jump you"
                              : _turf.Status == TurfStatus.Home ? "safe enough"
                              : "nobody minds much",
                     TurfTint(), r => r.ArtFile = "warning.png");
            risk.Row("Your heat", HeatWord(), HeatTint(), r => r.ArtFile = "police.png");
            sections.Add(risk);

            sections.Add(EverySet());
            sections.Add(AllTold());

            Info?.Open(_turf.ZoneName, TurfWord(), sections);
        }

        /// <summary>
        /// Every set in the city and what is between you and them.
        ///
        /// One row each rather than a page each, because the thing worth knowing is almost
        /// always comparative -- who has hit you most, who you owe, who has gone quiet. Nine
        /// separate screens cannot answer any of those and one list answers all three.
        ///
        /// Ordered by how much history there is, not alphabetically and not by the order they
        /// happen to sit in gangs.json. A set you have never met is a row of zeroes and it
        /// belongs underneath the ones you have been trading bodies with. Your own set goes
        /// first regardless, because it is the one you are reading this as.
        /// </summary>
        private InfoSection EverySet()
        {
            var sets = new InfoSection { Title = "Every set in the city" };

            var mine = _crew.Current;
            var all = new List<GangDef>(_gangs.All);

            all.Sort((a, b) =>
            {
                if (mine != null && a.Id == mine.Id) return -1;
                if (mine != null && b.Id == mine.Id) return 1;

                var byHistory = History(b).CompareTo(History(a));
                return byHistory != 0
                    ? byHistory
                    : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            foreach (var gang in all)
            {
                var st = _crew.StandingFor(gang.Id);
                var isMine = mine != null && gang.Id == mine.Id;

                sets.Row(gang.Name, StandingWord(gang, st, isMine), StandingTint(gang, st, isMine),
                         r =>
                         {
                             r.ArtFile = Icons.ForGang(gang.Id).File;

                             // The set's own colour on both, and never as the row's colour --
                             // the row's colour says how it is going with them, which is a
                             // different fact and one that changes.
                             r.ArtTint = gang.Colour;
                             r.Tab = gang.Colour;

                             r.Note = Tally(st);
                         });
            }

            return sets;
        }

        /// <summary>How much has actually happened with a set, for ordering the list.</summary>
        private int History(GangDef gang)
        {
            var st = _crew.StandingFor(gang.Id);
            return st.TheirDead + st.Attacks + st.Tweets;
        }

        /// <summary>
        /// The three numbers, on one line, in the order they happen to you.
        ///
        /// Written out with the units attached rather than as a row of bare figures under
        /// three icons. The icons are in the gutter already and there is one gutter per row,
        /// so a second set of them would have to be drawn inline as glyphs -- and "4 bodies,
        /// 2 raids, 11 posts" is shorter to read than any arrangement of symbols that says
        /// the same thing.
        /// </summary>
        private static string Tally(GangStanding st)
        {
            if (st.TheirDead == 0 && st.Attacks == 0 && st.Tweets == 0)
            {
                return "nothing between you";
            }

            var parts = new List<string>();

            if (st.TheirDead > 0) parts.Add(Count(st.TheirDead, "body", "bodies"));
            if (st.Attacks > 0) parts.Add(Count(st.Attacks, "raid", "raids"));
            if (st.Tweets > 0) parts.Add(Count(st.Tweets, "post", "posts"));

            return string.Join(", ", parts.ToArray());
        }

        private static string Count(int n, string one, string many)
        {
            return n + " " + (n == 1 ? one : many);
        }

        /// <summary>
        /// The city's totals, which is the part you cannot get by reading the list above.
        ///
        /// Summed over every set rather than tracked separately, so it cannot drift out of
        /// step with the rows it is a total of.
        /// </summary>
        private InfoSection AllTold()
        {
            var dead = 0;
            var raids = 0;
            var posts = 0;

            foreach (var gang in _gangs.All)
            {
                var st = _crew.StandingFor(gang.Id);

                dead += st.TheirDead;
                raids += st.Attacks;
                posts += st.Tweets;
            }

            var told = new InfoSection { Title = "All told" };

            told.Row("Bodies dropped", dead.ToString(),
                     dead > 0 ? Palette.Danger : (Color?)Palette.TextDim,
                     r => r.ArtFile = "skull.png");

            told.Row("Times they came for you", raids.ToString(),
                     raids > 0 ? Palette.Warn : (Color?)Palette.TextDim,
                     r => r.ArtFile = "warning.png");

            told.Row("Posts about the sets", posts.ToString(),
                     posts > 0 ? Palette.Text : (Color?)Palette.TextDim,
                     r => r.ArtFile = "megaphone.png");

            told.Row("Sets you are at war with", _crew.BeefingWith().Count.ToString(),
                     _crew.BeefingWith().Count > 0 ? Palette.Danger : (Color?)Palette.Cash,
                     r => r.ArtFile = "guns.png");

            return told;
        }

        /// <summary>Where you stand with a set, in the words somebody would actually use.</summary>
        private string StandingWord(GangDef gang, GangStanding st, bool isMine)
        {
            if (isMine) return "your set";
            if (_crew.Beefing(gang.Id)) return "at war";

            if (st.Rep <= -10f) return "bad blood";
            if (st.Rep >= 30f) return "solid";
            if (st.Rep >= 10f) return "friendly";

            return "no problem yet";
        }

        private Color StandingTint(GangDef gang, GangStanding st, bool isMine)
        {
            if (isMine) return Palette.Cash;
            if (_crew.Beefing(gang.Id)) return Palette.Danger;

            if (st.Rep <= -10f) return Palette.Warn;
            if (st.Rep >= 10f) return Palette.Cash;

            return Palette.TextDim;
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

        /// <summary>
        /// Who is actually at war with you, worst first.
        ///
        /// Not the gang's written rivals, which never change and are the same for everybody
        /// who ever runs with them. This is the line that tells you whether calling somebody
        /// out on the feed did anything, so it has to be the live number.
        /// </summary>
        private string BeefNames()
        {
            var beefing = _crew.BeefingWith();
            if (beefing.Count == 0) return "nobody";

            var names = new List<string>();
            foreach (var gang in beefing) names.Add(gang.Name);

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
                    mine ? gang.Colour : standing.Rep < 0 ? Palette.Danger : (Color?)Palette.Text,
                r => r.ArtFile = "mask.png");
            you.Row("Rep with them", standing.Rep.ToString("N0"),
                    standing.Rep < 0 ? Palette.Danger : Palette.Cash,
                r => r.ArtFile = "rank.png");
            you.Row("Bodies for them", standing.Kills.ToString("N0"), null,
                r => r.ArtFile = "skull.png");
            you.Row("Deals done", standing.Deals.ToString("N0"), null,
                    r => r.ArtFile = "deal.png");
            you.Row("Money made them", "$" + standing.MoneyEarned.ToString("N0"), Palette.Cash,
                r => r.ArtFile = "money.png");
            sections.Add(you);

            var them = new InfoSection { Title = "Them" };
            them.Row("Blocks", gang.TurfHint, null,
                r => r.ArtFile = "pin.png");
            them.Row("Product", DrugNames(gang), null,
                r => r.ArtFile = "pills.png");
            them.Row("Beefing with", RivalNames(gang), null,
                r => r.ArtFile = "guns.png");
            them.Row("To get in", gang.JoinRespect > 0
                ? gang.JoinRespect.ToString("F0") + " respect"
                : "just ask their leader", null,
                r => r.ArtFile = "locked.png");
            sections.Add(them);

            Info?.Open(gang.Name, mine ? "You run with them" : RelationLabel(gang), sections);
        }

        // ---- root --------------------------------------------------------------

        /// <summary>
        /// The home screen: every app, as a grid.
        ///
        /// There is no Weapons entry any more and that is the point of the whole change. It
        /// existed because the mod had taken the weapon-wheel button and owed the player a way
        /// to get a gun back -- a menu entry whose only job was apologising for the menu. The
        /// phone costs nothing, so SelectWeapon is the game's again and the apology is gone
        /// with it.
        /// </summary>
        public WheelPage BuildRoot()
        {
            var page = new WheelPage("Posted Up",
                _crew.IsAffiliated ? _crew.Current.Name : "Unaffiliated");

            // Every top-level app owns its own sub-pages rather than spilling onto the home
            // screen: product and its paperwork under Dealing, everything territorial under
            // Gangs, your own standing inside that.
            // First on the home screen, top-left, because it is the one app on here that is
            // not ours. Somebody looking for their own phone should not have to read past six
            // things that took it off them.
            // No badge. It said "yours", which is a thing the tile already says by being
            // called Phone and sitting on a phone -- and a badge slot is for a number you
            // would otherwise have to open the app to find out.
            page.Add("Phone", ">", () => ShowVanillaPhone?.Invoke(),
                detail: "Hands the button back so you can open your own phone",
                enabled: ShowVanillaPhone != null,
                disabledReason: "Not wired up");
            page.WithIcon(Icons.FromFile("mobile.png"));

            // SECOND, right under the real phone, because it is the thing on here you open
            // most and the one with something waiting in it. Dealing moved down to sit beside
            // Gangs, where the rest of the work is.
            //
            // The unread count is the value rather than the total. A number that only ever goes
            // up tells you nothing; the question a phone answers from across the screen is
            // whether anything is waiting.
            page.Add("Messages", "@", () => ShowMessages?.Invoke(),
                detail: Inbox.Unread > 0
                    ? Inbox.Unread == 1 ? "Somebody's texted you" : "You've got messages waiting"
                    : "Everything anybody has texted you, and a line back to your people",
                value: Inbox.Unread > 0 ? Inbox.Unread + " new" : "",
                enabled: ShowMessages != null,
                disabledReason: "Not wired up");
            page.WithIcon(Icons.FromFile("reply.png"));

            // Contacts sits on the RIGHT, two slots off Socials rather than next to it. The
            // wheel fills clockwise from the top, so where a wedge is added is where it lands
            // -- and the two most phone-shaped things on here sharing an edge made them read
            // as one pair rather than as the feed and the phone book.
            page.AddSub("Contacts", "#", BuildContactsPage,
                detail: "Everyone you can reach, and what you can say to them",
                value: ContactsSummary());
            page.WithIcon(Icons.FromFile("phone.png"));

            // A baggie, not a bong. The old one was a picture of SMOKING and this menu is
            // about selling, which is the one thing nobody in it ever does with the product.
            page.AddSub("Dealing", "$", BuildDrugsPage,
                detail: "Re-up, bag up, go to work",
                value: DrugsSummary(),
                enabled: !_cutting.IsBusy,
                disabledReason: "You're working the counter");
            page.WithIcon(Icons.FromFile("baggie.png"));

            // IN THE MIDDLE OF THE GRID, swapped with Knowai. It is the app with something
            // new in it most often, and it was sat on its own on a fourth row underneath three
            // apps you open once a session.
            page.Add("Socials", "@", () => ShowSocials?.Invoke(),
                detail: PaybackDue != null && PaybackDue()
                    ? "Somebody's coming about what you said"
                    : "What the block is saying, and what you say back",
                value: Followers == null ? "" : Followers().ToString("N0") + " followers",
                enabled: ShowSocials != null,
                disabledReason: "Not right now");

            // The handset with a heart on the screen, so it cannot be confused with the Phone
            // app. mobile.png put a feed on the screen, and at twenty pixels a feed is three
            // smudges -- exactly what a phone with nothing on it looks like.
            page.WithIcon(Icons.FromFile("socials.png"));

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

            // Its own wedge rather than a line inside something else. What the block is saying
            // about you is not a sub-heading of your inventory, and burying it two levels down
            // would mean nobody ever reads it -- which defeats the entire point of writing it.
            //
            // A leaf, not a submenu. Everything the sub-page offered -- saying something,
            // naming a set, calling one out -- is a section inside the feed screen now, next to
            // the timeline those posts land in. A wheel page whose four items were "open a
            // screen" and three things belonging ON that screen was one door too many.
            // Its own app rather than a line at the bottom of Gangs. Settings was buried
            // three rows into a page about turf, which is nowhere anybody would look for it --
            // it is not a gang question, it is a question about the mod.
            page.Add("Settings", "*", () => ShowSettings?.Invoke(),
                detail: "How the mod behaves, and how to undo what you have done",
                value: "",
                enabled: ShowSettings != null,
                disabledReason: "Not wired up");
            page.WithIcon(Icons.FromFile("scales.png"));

            // Between Inventory and Settings rather than at the end. It is a thing you
            // carry and use, not a thing you configure, so it belongs with the other two apps
            // about what is in your hands.
            //
            // The value is how much is up rather than a total that only ever climbs -- the
            // question a tile answers from across the screen is "is there anything there".
            page.Add("Graffiti", "*", () => ShowGraffiti?.Invoke(),
                detail: "Pick a colour, take a can, and go and put your name on something",
                value: MarksUp == null || MarksUp() == 0 ? "" : MarksUp() + " up",
                enabled: ShowGraffiti != null,
                disabledReason: "Not wired up");

            // A CAP, not a can, and drawn for THIS size rather than shrunk to it.
            //
            // Every tile here is 64x64 and the draw call forces the square. The cap icons the
            // picker uses are two thirds as wide as they are tall, so handing one of those
            // straight to the wheel gives a squat, stretched cap -- the same trap the can tile
            // was made to dodge, and its stroke is drawn for a thirty-pixel chip and vanishes
            // at sixteen anyway.
            //
            // So capapp.png is a separate shape of the same object: squarer, much heavier in
            // the line, bigger hole. It holds down to sixteen pixels, which is the only test a
            // tile has to pass.
            page.WithIcon(Icons.FromFile("capapp.png"));

            // LAST, where Socials used to sit. It is a service you use rather than a part of
            // the business, and the only app on here that will still be useful to somebody who
            // has stopped dealing -- which is a reason to keep it and not a reason to put it
            // in front of the work.
            page.AddSub("Knowai", "~", BuildKnowaiPage,
                detail: "Driverless cars. It comes to you and takes you where you say",
                value: RideSummary());
            page.WithIcon(Icons.FromFile("car.png"));

            return page;
        }


        /// <summary>
        /// What a plug carries, said the way the rest of the mod says it.
        ///
        /// The drug IDS were being printed straight out and uppercased, and one of them does
        /// not match its own name: the id is "ecstasy" and the product is Oxycodone -- percs.
        /// So Gerald's contact row advertised ecstasy, which he has never sold, while the buy
        /// screen two clicks later correctly called the same thing Oxycodone.
        ///
        /// An id is a key. It is not a word for a player to read, and this is the only place
        /// one ever reached the screen.
        /// </summary>
        private string Carrying(IEnumerable<string> ids)
        {
            var names = new List<string>();

            foreach (var id in ids)
            {
                var drug = _drugs.Get(id);
                names.Add(drug == null ? id : drug.Name);
            }

            return string.Join(", ", names.ToArray());
        }

        /// <summary>What a plug is FOR, in one line, under his name.</summary>
        private string WhatTheyDo(DealerDef def)
        {
            if (def == null) return "";

            return def.Drugs.Count == 0
                ? "Bricks, and nothing smaller"
                : Carrying(def.Drugs);
        }

        private string DrugsSummary()
        {
            var packaged = Stash.TotalPackaged;
            var bulk = Stash.TotalBulk;

            if (packaged > 0.005f && bulk > 0.005f)
            {
                return packaged.ToString("0.#") + " ready, " + bulk.ToString("0.#") + " to prep";
            }
            if (packaged > 0.005f) return packaged.ToString("0.#") + " ready to sell";
            if (bulk > 0.005f) return bulk.ToString("0.#") + " still to prep";
            return "empty";
        }

        /// <summary>
        /// What is ready to sell, said properly -- "40 pills" when that is all there is, and
        /// "3 kinds ready" when it is a mixture, because adding pills to grams gives a number
        /// that is not true about anything.
        /// </summary>
        private string ReadyWord()
        {
            DrugDef only = null;
            var kinds = 0;

            foreach (var drug in _drugs.All)
            {
                if (Stash.PackagedOf(drug.Id) <= 0.005f) continue;
                kinds++;
                only = drug;
            }

            if (kinds == 0) return "";
            if (kinds == 1) return only.Amount(Stash.PackagedOf(only.Id)) + " ready";

            return kinds + " kinds ready";
        }

        /// <summary>
        /// Two lists: what is in your pockets, and what is at the house. Each one names the
        /// product and says the amount in that product's own units, so pills are counted in
        /// pills and weight is weighed.
        ///
        /// Packaged and bulk are shown on one line per product rather than as two separate
        /// lists, because "40 pills, 60 to press" is one fact about ecstasy, not two.
        /// </summary>
        private void HoldingRows(WheelPage page)
        {
            var house = _stash == null ? null : _stash.Stash;

            var onYou = DrugLines(Stash);
            var atHouse = house == null ? new List<string[]>() : DrugLines(house);

            page.Row("ON YOU", "", Palette.TextDim, "stash.png");

            if (onYou.Count == 0) page.Row("nothing", "", Palette.TextDim, "box.png");
            foreach (var line in onYou) page.Row(line[0], line[1], Palette.Cash);

            page.Row("AT THE HOUSE", "", Palette.TextDim, "garage.png");

            if (atHouse.Count == 0) page.Row("nothing", "", Palette.TextDim, "box.png");
            foreach (var line in atHouse) page.Row(line[0], line[1], Palette.Text);
        }

        /// <summary>
        /// One line per product that is present at all, reading "40 pills · 60 to press".
        /// Anything the container has none of is left out entirely rather than listed as zero.
        /// </summary>
        private List<string[]> DrugLines(Stash from)
        {
            var lines = new List<string[]>();
            if (from == null) return lines;

            foreach (var drug in _drugs.All)
            {
                var ready = from.PackagedOf(drug.Id);
                var raw = from.BulkOf(drug.Id);
                if (ready <= 0.005f && raw <= 0.005f) continue;

                var text = ready > 0.005f ? drug.Amount(ready) : "";

                if (raw > 0.005f)
                {
                    if (text.Length > 0) text += "  ·  ";
                    text += drug.Short(raw) + " to prep";
                }

                lines.Add(new[] { drug.Name, text });
            }

            return lines;
        }

        // ---- drugs -------------------------------------------------------------

        /// <summary>Everything to do with product, in the order you actually do it.</summary>
        /// <summary>
        /// The phone book.
        ///
        /// Texting a plug was reachable from exactly one place: three levels down, inside the
        /// page for the gang he happens to supply. That is a fine place to find out what he
        /// charges and a terrible one to find him when you just want a bag brought over -- and
        /// it meant the second plug, who supplies nobody, could not be texted at all.
        ///
        /// So everybody you can reach is in one list: what they are to you, whether they are
        /// answering, and the one thing you can say to them. It is not the game's own phone --
        /// that needs a library this mod does not take -- but it is the same idea in the mod's
        /// own furniture, and it is one flick from the wheel rather than three.
        /// </summary>
        /// <summary>
        /// Knowai. A list of places and a car that comes and takes you to one.
        ///
        /// A page rather than a screen of its own, and that is not a shortcut -- it is a list
        /// of rows you press, which is what every other list on this phone is. A bespoke screen
        /// for it would be a second way of drawing the same object, and the two would drift
        /// the first time anything about a row changed.
        /// </summary>
        /// <summary>The Knowai page on its own, for the car to put in front of you.</summary>
        public WheelPage KnowaiPage()
        {
            return BuildKnowaiPage();
        }

        private WheelPage BuildKnowaiPage()
        {
            var page = new WheelPage("Knowai", RideSummary());
            page.PanelTitle = "Where to?";

            var state = RideState == null ? Locations.RideState.None : RideState();

            // NOTHING YET: one button, and it is the only thing anybody wants from this app
            // when they are stood on a pavement. You do not tell a cab where you are going
            // before it has arrived.
            if (state == Locations.RideState.None)
            {
                page.Add("Request a pickup", ">", () =>
                {
                    var no = HailRide == null ? "Not wired up" : HailRide();

                    if (!string.IsNullOrEmpty(no)) UI.Notify.Failure(no);
                },
                    detail: "One comes to you. You say where when you're in it",
                    enabled: HailRide != null,
                    disabledReason: "Not wired up");

                page.WithIcon(Icons.FromFile("car.png"));

                return page;
            }

            // IN THE BACK AND IT IS ASKING. This is the only state that shows the list, which
            // is why the list is not on the home screen: it is a question the car asks, not a
            // menu you browse.
            if (state == Locations.RideState.Picking)
            {
                page.PanelTitle = "Where to?";

                // Ten place names are ten things you read, not ten shapes you recognise.
                page.AsList = true;

                foreach (var stop in Locations.Knowai.Stops)
                {
                    // Captured, because the loop variable is one variable and every row would
                    // otherwise ask for wherever the loop finished.
                    var where = stop;

                    page.Add(where.Name, ">", () =>
                    {
                        var no = RideTo == null ? "Not wired up" : RideTo(where);

                        if (!string.IsNullOrEmpty(no)) UI.Notify.Failure(no);
                    },
                        detail: where.Area,
                        enabled: RideTo != null,
                        disabledReason: "Not wired up");

                    page.WithIcon(Icons.FromFile("pin.png"));
                }

                return page;
            }

            // Coming, riding or arrived. One row, and it is the row you want.
            var going = RideGoing == null ? "" : RideGoing();

            page.Add("Cancel the ride", "x", () => CancelRide?.Invoke(),
                detail: state == Locations.RideState.Riding
                    ? "You're in it. This gets you out where you are"
                    : "Send it away and ask for another later",
                value: string.IsNullOrEmpty(going) ? "" : going);

            page.WithIcon(Icons.FromFile("car.png"));

            return page;
        }

        /// <summary>What the app says on the home screen without being opened.</summary>
        private string RideSummary()
        {
            var state = RideState == null ? Locations.RideState.None : RideState();

            switch (state)
            {
                case Locations.RideState.Coming: return "on the way";
                case Locations.RideState.Waiting: return "waiting";
                case Locations.RideState.Picking: return "where to?";
                case Locations.RideState.Riding: return "riding";
                case Locations.RideState.Arrived: return "arrived";
                default: return Locations.Knowai.Stops.Length + " stops";
            }
        }

        private WheelPage BuildContactsPage()
        {
            var page = new WheelPage("Contacts", ContactsSummary());
            page.PanelTitle = "Who you can reach";

            // The homies, where Lamar used to sit.
            //
            // He was a row that could never be pressed -- it existed to tell you whether he
            // had work, which the text message and his own blip both already do. A contacts
            // page whose first entry is permanently greyed out teaches you that the page is
            // decoration.
            //
            // This is a phone number that does something.
            if (Crew != null && Crew.Available)
            {
                // "On the way" is a third state and it needs to read as one. They are not with
                // you and they are not on call -- there is a cab out there with three men in
                // it, and pressing the row again while it is driving should call it off rather
                // than send for another one.
                var coming = Crew.Inbound;
                var out_ = Crew.AnyOut || coming;

                page.Add(out_ ? "Send the homies home" : "Text the homies",
                         out_ ? "x" : ">",
                         RingHomies,
                    detail: coming
                        ? "They're in a cab on the way over. Stay put and it'll find you"
                        : out_
                        ? "Tell them you're good and let them get on"
                        : "Three of yours get a cab over and roll with you until you say otherwise",
                    value: coming ? "on the way" : out_ ? Crew.Standing + " with you" : "on call");

                page.WithIcon(Icons.FromFile("people.png"));
            }
            else
            {
                var job = WorkWaiting == null ? null : WorkWaiting();

                page.Add("Lamar", "L", null,
                    detail: job != null
                        ? "He's got something. Go and see him on Forum Drive"
                        : "Ride a job out with the homies and they'll pick up for you",
                    value: job != null ? job.Name : "quiet",
                    enabled: false,
                    disabledReason: job != null ? "Go and see him" : "Not yet");
                page.WithIcon(Icons.FromFile("people.png"));
            }

            // Then the plugs, in the order the data lists them.
            foreach (var def in _dealers.All)
            {
                if (def == null) continue;

                var plug = def;
                // Same as the Messages list: a man you have never met is not a greyed-out
                // contact, he is not a contact.
                if (!Supply.DealerManager.HaveMet(plug, _state)) continue;

                var refusal = _dealers.RefusalReason(plug, _state, _crew);

                // SHORT. The right-hand value is a status, not a stock list.
                //
                // It was the full product list -- "MARIJUANA, OXYCODONE, ALPRAZOLAM" -- which
                // is wider than the row, so the label was squeezed down to "Ge..." and the
                // list was drawn straight over the top of it. The names belong on the detail
                // line underneath, where there is room and where they already are.
                var carries = plug.Drugs.Count == 0
                    ? "everything"
                    : plug.Drugs.Count + (plug.Drugs.Count == 1 ? " kind" : " kinds");

                // His NAME, his FACE, and what he is for underneath it.
                //
                // It was "Text <name>" with a generic crate beside it, and the "Text " prefix
                // was eating the width -- "Text Gerald" came out as "Text Gera..." with his
                // stock shouted across the rest of the row. The verb is the same on every line
                // in a contacts list, which makes it the one word on it carrying no
                // information, so it is gone and the name has the room back.
                //
                // The portrait is the game's own CHAR_ mugshot -- the same face that appears
                // on his text messages, so the list and the message agree.
                page.Add(plug.Name, ">", () => Call(plug),
                    detail: refusal ?? WhatTheyDo(plug),
                    value: carries,
                    enabled: refusal == null,
                    disabledReason: refusal ?? "");

                // He gets his phone out to send this one, so ours does not take it off him.
                page.KeepsPhone();

                page.WithIcon(Icons.Portrait(plug.Portrait));
            }

            return page;
        }

        /// <summary>Set by Main. The three who come out when you ask.</summary>
        public Gangs.Homies Crew;

        /// <summary>How many of them are actually answering.</summary>
        private string ContactsSummary()
        {
            var open = 0;
            var all = 0;

            foreach (var def in _dealers.All)
            {
                if (def == null) continue;

                all++;
                if (_dealers.RefusalReason(def, _state, _crew) == null) open++;
            }

            if (all == 0)
            {
                if (Crew != null && Crew.Inbound) return "the homies are on their way";
                return Crew != null && Crew.AnyOut ? "the homies are with you" : "nobody yet";
            }

            var line = open + " of " + all + " plugs answering";

            if (Crew != null && Crew.Inbound) line += "  ·  homies on the way";
            else if (Crew != null && Crew.AnyOut) line += "  ·  " + Crew.Standing + " with you";

            return line;
        }

        private WheelPage BuildDrugsPage()
        {
            var page = new WheelPage("Dealing", DrugsSummary());

            // What you have, by name, in both places it can be.
            //
            // A single grams figure was meaningless the moment the catalogue held pills and
            // joints alongside weight -- "53g ready" when forty of those were pills. And a
            // count of what is on you says nothing about the far bigger pile sitting at Aunt
            // Denise's, which is usually the number you actually wanted.
            page.PanelTitle = "What you're holding";
            HoldingRows(page);

            if (_postUp.IsPosted)
            {
                page.Add("Pack up", "x", () => _postUp.Stop("You packed up."),
                    detail: "Stop dealing and move on",
                    value: _postUp.Footfall + " passing");
                page.WithIcon(Icons.Tick);
            }
            else
            {
                var packaged = Stash.TotalPackaged;
                var bulk = Stash.TotalBulk;

                page.AddSub("Post up", "$", BuildSellPage,
                    detail: "Stand on a corner and let it come to you",
                    value: packaged > 0.005f ? ReadyWord() : "",
                    enabled: packaged > 0.005f,
                    disabledReason: bulk > 0.005f ? "All you have is weight -- prep it first"
                                                  : "You are holding nothing");
                page.WithIcon(Icons.Cash);
            }

            page.Add("The numbers", "=", ShowTradeNumbers,
                detail: "Prices, heat, and what this block does to both",
                value: "");
            page.WithIcon(Icons.Health);

            return page;
        }

        private void OpenStashScreen()
        {
            StashScreen?.Open(Stash, _stash.Stash, _drugs, () => _state.Touch());
        }

        /// <summary>Set by Main. Where anything you put down ends up.</summary>
        public Economy.DroppedBags Bags;
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

        // ---- sell --------------------------------------------------------------

        private WheelPage BuildSellPage()
        {
            var page = new WheelPage("Post up", "Pick what you are moving");

            page.PanelTitle = _turf.ZoneName;
            page.Row("This spot", TurfWord(), TurfTint(), "pin.png");
            page.Row("Foot traffic", FootfallWord(),
                     _postUp.Footfall == 0 ? Palette.Warn : Palette.Cash, "footfall.png");
            page.Row("Gang around", _crew.NearbyAllies > 0 ? "yes" : "no",
                     _crew.NearbyAllies > 0 ? Palette.Cash : (Color?)Palette.TextDim, "people.png");

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
                var risk = Pricing.BadCutChance(purity);

                page.Add(product.Name, product.Tag,
                    () => PostUpWith(product),
                    detail: product.Ladder() +
                            (risk > 0.15f ? "  ·  " + PurityWord(purity) + ", they'll notice" : ""),
                    value: product.Amount(stock),
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

        // ---- supply ------------------------------------------------------------

        private string Carries(DealerDef def)
        {
            return def.Drugs.Count == 0
                ? "everything"
                : Carrying(def.Drugs).ToUpperInvariant();
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
            // FILED HERE, not in the screen that asked for it.
            //
            // This is the one function both doors go through -- the Contacts row and the
            // Messages app -- so a re-up ordered off the wheel shows up in the thread too.
            //
            // And filed BEFORE the call, which is the part that took a second look. Both of
            // these calls send his reply themselves: ArrangeMeet texts you back, and so does
            // the delivery. Recording your line afterwards put the answer above the question
            // in a conversation read top to bottom -- he was replying to something you had not
            // said yet.
            //
            // Guarded on the refusal so a plug who is not answering does not collect a wall of
            // questions nobody was ever asked. That check is the same one the send row greys
            // itself out with, so the button and the thread agree about what just happened.
            var refusal = _dealers.RefusalReason(def, _state, _crew);

            // EVERY PLUG DELIVERS, and the rendezvous is gone.
            //
            // It used to fork on kind: the two Docks contacts drove to the house and everybody
            // else picked a street corner and blipped it. Two behaviours behind one identical
            // row, and the corner version was the weaker of the two anyway -- it sent you to
            // stand somewhere you had already been, to do a thing you could have done by
            // walking up to him in the first place.
            //
            // Meeting him on his own block is now how you GET the number. What the number is
            // for is having it brought to your door.
            if (refusal != null)
            {
                Notify.Problem(refusal.ToLowerInvariant() + ".");
                return;
            }

            if (Delivery == null)
            {
                Notify.Problem("can't reach nobody right now.");
                return;
            }

            Inbox.Sent(def.Name, SayTo(def));

            var failed = Delivery.Call(def);
            if (failed != null) Notify.Problem(failed);
        }

        /// <summary>The id the Messages app hands back for the homies. Not a dealer.</summary>
        public const string HomiesId = "homies";

        /// <summary>
        /// Sends for the homies, or sends them home -- whichever the state calls for.
        ///
        /// One function, pressed from two places: the Contacts row and the Messages thread.
        /// The plugs learned this lesson already -- the moment the app grew its own version of
        /// when somebody answers, the two doors start disagreeing about it.
        ///
        /// The state is read HERE rather than captured when the row was built. A wheel page is
        /// built once when it opens and can be looked at for a while; they can arrive in that
        /// time, and a row that then dismisses them because it was drawn before they got here
        /// is a button that does the opposite of what it says.
        /// </summary>
        private void RingHomies()
        {
            if (Crew == null || !Crew.Available)
            {
                Notify.Problem("you ain't got nobody to call yet.");
                return;
            }

            var away = Crew.AnyOut || Crew.Inbound;

            var no = away ? Crew.Dismiss() : Crew.Call();

            if (no != null)
            {
                Notify.Problem(no);
                return;
            }

            // Yours first, then theirs. Same ordering the plugs need and for the same reason:
            // a reply filed above the question reads as them answering something you have not
            // said yet.
            Inbox.Sent(Gangs.Homies.ContactName, away ? HomiesStand : HomiesCome);

            Notify.Text(null, Gangs.Homies.ContactName, Gangs.Homies.ContactZone,
                        away ? "aight. hit us up" : "on our way. sit tight");
        }

        /// <summary>What you say to them, shown on the send row before you press it.</summary>
        private const string HomiesCome = "yall come thru";
        private const string HomiesStand = "we good, head back";

        /// <summary>
        /// What you say when you text a plug, shown on the send row before you press it.
        ///
        /// Two lines rather than one because the docks is a different transaction: everybody
        /// else you are asking whether they are holding, and the port you are telling to bring
        /// it to you.
        /// </summary>
        private static string SayTo(DealerDef def)
        {
            return def.Kind == DealerKind.Docks ? "need a pickup" : "you got anything?";
        }

        /// <summary>
        /// The plugs, as the Messages app wants them: a name, a face, and whether he will
        /// answer right now.
        ///
        /// Built here rather than in the screen because everything the answer depends on --
        /// the dealer list, your rank, who you run with -- already lives on this class. The
        /// screen gets four strings and stays out of Supply entirely.
        /// </summary>
        public List<PhoneContact> PhoneBook()
        {
            var book = new List<PhoneContact>();

            // Your own people first, and only once they are yours to call. Before that they
            // are not a contact you have -- Contacts says as much in its own way, and a thread
            // you cannot use is worse here than no thread at all.
            if (Crew != null && Crew.Available)
            {
                var away = Crew.AnyOut || Crew.Inbound;

                book.Add(new PhoneContact
                {
                    Id = HomiesId,
                    Name = Gangs.Homies.ContactName,

                    // No mugshot, because there are three of them. See PhoneContact.Icon.
                    Portrait = "",
                    Icon = "people.png",

                    Refusal = null,
                    Line = away ? HomiesStand : HomiesCome
                });
            }

            foreach (var def in _dealers.All)
            {
                if (def == null) continue;

                // ONLY PEOPLE HE HAS MET. The app was listing all fourteen, nine of them
                // reading "No messages yet" under a grey silhouette -- a contacts list of
                // strangers, which is not a contacts list, it is a roster.
                //
                // Everything else on the phone greys a row out and says why, because those
                // refusals are things the player can act on: get the rank, come back in the
                // evening, go home first. "You have never met this man" is not that. It is not
                // a locked door, it is a door he has not found, and putting it on screen tells
                // him it exists.
                if (!Supply.DealerManager.HaveMet(def, _state)) continue;

                book.Add(new PhoneContact
                {
                    Id = def.Id,
                    Name = def.Name,

                    // ASKED BY NAME, exactly as a text message asks.
                    //
                    // Almost nothing in dealers.json sets a portrait -- Gerald does not and
                    // neither does Cheng -- so DealerDef.Portrait is the CHAR_DEFAULT
                    // silhouette for nearly everybody. Their real faces have always come from
                    // the name lookup, which is how their texts get one, and a contacts list
                    // showing grey outlines beside messages carrying photographs would be the
                    // app disagreeing with the notification it came from.
                    Portrait = string.IsNullOrEmpty(Faces.For(def.Name))
                        ? def.Portrait
                        : Faces.For(def.Name),
                    Refusal = _dealers.RefusalReason(def, _state, _crew),
                    Line = SayTo(def)
                });
            }

            return book;
        }

        /// <summary>
        /// Texts somebody from the Messages app, down the same path as the Contacts row.
        ///
        /// A thin wrapper on purpose. The app is a second door onto ordering a re-up, and the
        /// moment it grew its own version of the rules about when a plug answers, the two
        /// doors would start disagreeing.
        /// </summary>
        public void TextContact(string id)
        {
            if (string.IsNullOrEmpty(id)) return;

            if (string.Equals(id, HomiesId, StringComparison.OrdinalIgnoreCase))
            {
                RingHomies();
                return;
            }

            foreach (var def in _dealers.All)
            {
                if (def != null && string.Equals(def.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    Call(def);
                    return;
                }
            }
        }

        /// <summary>
        /// Face to face with a dealer: what they sell, and the one question worth asking.
        /// </summary>
        private WheelPage BuildDealerPage(DealerDef def)
        {
            var mult = def.PriceNow;

            var page = new WheelPage(def.Name, def.BuyLine);
            page.PanelTitle = def.Name;
            page.Row("Cash", "$" + Game.Player.Money.ToString("N0"), Palette.Cash, "cash.png");
            page.Row("Free space", Stash.FreeSpace.ToString("0") + "g", null, "box.png");
            page.Row("Carries", Carries(def), null, "crate.png");

            // WHAT HE IS HANDING OVER, which on a gang corner is most of the decision. Cheap
            // and halved or dear and clean is a real choice, and it is not one anybody can make
            // from a price alone.
            page.Row(def.PureTonight ? "Tonight" : "Strength",
                     def.PureTonight
                         ? "uncut -- " + Economy.Stash.Percent(def.PurityNow) + "%, and he knows it"
                         : Economy.Stash.Percent(def.PurityNow) + "%",
                     def.PureTonight ? Palette.Cash : (Color?)null,
                     def.PureTonight ? "crown.png" : "scales.png");
            // HIS OFFER, ON THE ROW THE OFFER IS ABOUT. A discount that only shows up in
            // the final total is a discount the player finds out about after deciding.
            if (Supply.ColdCall.With(def.Id))
            {
                var left = (int)Math.Ceiling(Supply.ColdCall.MinutesLeft);

                page.Row("Price",
                         "x" + (mult * Supply.ColdCall.Multiplier(def.Id)).ToString("0.00") +
                         "  -" + (int)Math.Round(Supply.ColdCall.Off * 100f) + "%",
                         Palette.Cash, "cash.png");

                page.Row("Offer ends", left <= 1 ? "any minute" : "about " + left + " minutes",
                         Palette.Warn, "warning.png");
            }
            else
            {
                page.Row("Price", "x" + mult.ToString("0.00"), null, "cash.png");
            }
            page.Row("Max order", def.MaxOrderGrams.ToString("0") + "g", null, "crate.png");
            page.Row("Sold so far", _state.GramsSold.ToString("0.#") + "g", null, "deal.png");

            // The question that opens the game up. Only a gang dealer knows the answer.
            if (def.IsGangDealer)
            {
                var known = _state.DocksUnlocked;
                var owed = DealerManager.PackagesUntilSource(_state);

                page.Add("Ask source", "?",
                    () => _dealers.AskSource(def, _state, _cfg.DocksUnlockGrams),
                    detail: known
                        ? "You already know: the port"
                        : owed > 0
                            ? "Nobody says until Gerald's packages are cleared -- " + owed + " to go"
                            : "\"Where are you getting this?\"",
                    value: known ? "KNOWN" : owed > 0 ? owed + " left" : "ASK HIM");
                page.WithIcon(Icons.FromFile("reply.png"));
            }

            page.Add("Leave", "x", () => _dealers.SayBye(),
                detail: "Walk away");

            return BuildDealerStock(page, def, mult);
        }

        private WheelPage BuildDealerStock(WheelPage page, DealerDef def, float mult)
        {
            // An empty Drugs list means this contact carries the whole catalogue.
            var stock = new List<DrugDef>();

            if (def.Drugs.Count == 0)
            {
                // Everything he could get hold of -- which is not the same as everything in the
                // catalogue. Nobody buys rolled joints off a container at the port.
                foreach (var d in _drugs.All)
                {
                    if (!d.MadeOnly) stock.Add(d);
                }
            }
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

            // AND THE SAME RULE AS STANDING IN FRONT OF HIM. A delivery of weight is weight;
            // a small drop is whatever he has stepped it to. Two paths quoting different
            // strengths for the same money is exactly the sort of thing nobody reports as a
            // bug because they assume they misread it.
            //
            // HIS purity, not a hundred per cent.
            //
            // AddBulk's purity argument is optional and defaults to pure, and this call left it
            // off -- so every gram bought through the phone arrived perfect no matter who sold
            // it. Gerald sells at seventy-five and it turned up at a hundred; so did everybody
            // else's, which meant the whole purity economy only existed on the selling side.
            // The delivery path and the face-to-face path both pass it; this was the one that
            // did not.
            var accepted = Stash.AddBulk(product.Id, supplied, def.PurityFor(product, supplied));
            if (accepted <= 0f)
            {
                // Back in his bag before we walk away. It was taken off him a few lines up so
                // that he could not sell what he was not holding, and with nowhere to put it
                // the weight would otherwise stop existing -- a man with a full stash could
                // empty a dealer just by failing to buy from him, over and over.
                _dealers.GiveStock(def, product.Id, supplied);

                Notify.Problem("you can't carry no more.");
                return;
            }

            // The same thing for a partial fit. The player is charged for what fitted, which
            // was always right; what did not fit needs to go back rather than evaporate.
            if (supplied - accepted > 0.005f)
            {
                _dealers.GiveStock(def, product.Id, supplied - accepted);
            }

            var charged = (int)Math.Round(cost * (accepted / grams));
            UI.Cash.Take(charged);
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

            if (_crew.IsAffiliated)
            {
                var mine = _crew.CurrentStanding;

                // The number and what it makes you, on one line. A figure on its own is a
                // score; the same figure next to "Enforcer" is a position, and the position is
                // the part you actually hold.
                page.Row("Your rep",
                         (mine == null ? "0" : mine.Rep.ToString("0")) + "  ·  " + _state.RankName,
                         mine != null && mine.Rep < 0 ? Palette.Danger : Palette.Cash, "rank.png");
                page.Row("Bodies for them", mine == null ? "0" : mine.Kills.ToString("N0"), null, "skull.png");
                page.Row("Beefing with", BeefNames(), Palette.Danger, "guns.png");
            }

            page.Row("You are on", _turf.ZoneName, TurfTint(), "pin.png");
            page.Row("Whose block", TurfWord(), TurfTint(), "mask.png");

            page.AddSub("This block", "#", BuildTurfPage,
                detail: TurfWord(),
                value: _turf.ZoneName);
            page.WithIcon(Icons.Garage);

            // Two wedges used to live here that could not be pressed: one saying work comes
            // from Lamar, one saying go and find a leader. Both true, both already in the panel
            // above, and both taking a slot on a wheel where a slot is the scarcest thing
            // there is. A wedge you cannot press teaches you the menu is not worth flicking
            // through, so the panel says it and the wheel keeps its slots for things that do
            // something.
            if (!_crew.IsAffiliated)
            {
                var leader = _leaders.InReach;

                page.Row("Get put on", leader != null
                        ? "talk to " + leader.Name
                        : "leaders are on your map",
                    Palette.TextDim, "key.png");
            }
            else
            {
                page.Row("Work", "Lamar's got it", Palette.TextDim, "car.png");
            }

            // Standing is a gang question, so the readout lives here rather than on the root.
            // One readout rather than two. "Who you run with" was a wheel page of rows about
            // your own set, and "How you stand" was a readout of rows about you -- the same
            // question asked twice, one flick apart, in two different kinds of screen.
            page.Add("How you stand", "*", ShowStatus,
                detail: _crew.IsAffiliated
                    ? "You, your set, and what every gang thinks of you"
                    : "Your rank, your heat, and what every gang thinks of you",
                value: _crew.IsAffiliated ? _crew.Current.Name : _state.RankName);
            page.WithIcon(Icons.Mask);

            return page;
        }




        /// <summary>
        /// The undo half of the settings screen, handed to it as rows.
        ///
        /// Built here rather than in the screen because everything they touch lives here -- the
        /// affiliation, the save, the stash, the feed. The screen knows how to draw a thing you
        /// hold down and what to do when the holding finishes; it has no business knowing what
        /// resetting a gang standing means.
        ///
        /// Held rather than confirmed on a second page. Each of these used to open a page that
        /// spelled out what survived and asked again, which is a good pattern for a wheel and a
        /// bad one inside a list -- you either meant it or you did not, and a second of holding
        /// says so without going anywhere. The wording that was on those pages is the note
        /// under each row now, so nothing about what survives has been lost.
        /// </summary>
        /// <summary>Every job in the catalogue, so "unlock everything" means every job.</summary>
        private IEnumerable<string> AllMissionIds()
        {
            return AllJobs == null ? new List<string>() : AllJobs();
        }

        /// <summary>And every set, so no leader is still hiding on the map.</summary>
        private IEnumerable<string> AllGangIds()
        {
            var ids = new List<string>();

            if (_gangs != null)
            {
                foreach (var g in _gangs.All)
                {
                    if (g != null && !string.IsNullOrEmpty(g.Id)) ids.Add(g.Id);
                }
            }

            return ids;
        }

        public IEnumerable<Opt> ResetOptions()
        {
            yield return new Opt { Kind = OptKind.Heading, Label = "Start over" };

            yield return new Opt
            {
                Kind = OptKind.Danger,
                Label = "The gangs",
                Note = "Every standing, every body, every dollar you made them, and whoever you " +
                       "run with. Respect, money and product untouched",
                Do = () => _crew.ResetEverything()
            };

            yield return new Opt
            {
                Kind = OptKind.Danger,
                Label = "Lamar's work",
                Note = "He works down his list from the top again. What he already paid you stays paid",
                Enabled = () => _state.MissionsDone.Count > 0,
                Do = () => _state.ForgetMissions()
            };

            yield return new Opt
            {
                Kind = OptKind.Danger,
                Label = "Your graffiti",
                Note = "Every tag you have sprayed comes off the walls, and off the save with " +
                       "it. The walls go back to how the game had them",
                Enabled = () => Jobs != null && Jobs.PaintCount > 0,
                Do = () => { if (Jobs != null) Jobs.ForgetPaint(); }
            };

            yield return new Opt
            {
                Kind = OptKind.Danger,
                Label = "Hot blocks",
                Note = "Every corner you have made hot forgets it. The city starts cold again",
                Enabled = () => _postUp != null && _postUp.Ground.WarmBlocks > 0,
                Do = () => _postUp.Ground.Forget()
            };

            yield return new Opt
            {
                Kind = OptKind.Danger,
                Label = "Gerald's packages",
                Note = "Both fronts back on the table and the port shut again, so his whole " +
                       "sequence runs from the top. What you already sold stays sold",
                // Always available, deliberately.
                //
                // It used to require the sequence to be visibly under way, which meant the one
                // state you could not reset out of was the one where the count had gone wrong
                // and everything read as zero -- the escape hatch was locked by the same
                // reading that made you need it. Resetting something you have not started is a
                // no-op, and a no-op is a far better failure than a dead end.
                Do = () => _state.ForgetFronts()
            };

            yield return new Opt
            {
                Kind = OptKind.Danger,
                Label = "Finish Gerald's package",
                Note = "Counts what he's fronted you as sold without selling it, so he gets in " +
                       "touch and you can hand it in. Skips the audition, so the set will take " +
                       "you. Press it again for the second one and the port opens up",
                Enabled = () => _state.FrontsDone < 2 || _state.HasFrontedWork,
                Do = () => Notify.Important("~g~" + _state.FinishFront() + "~s~")
            };

            yield return new Opt
            {
                Kind = OptKind.Danger,
                Label = "Count Gerald's packages done",
                Note = "Marks both of his fronts as moved without moving them. For a save that " +
                       "cleared one and was not credited for it -- he opens up the port instead " +
                       "of asking again",
                Enabled = () => _state.FrontsDone < 2,
                Do = () => _state.CreditFronts()
            };

            yield return new Opt
            {
                Kind = OptKind.Danger,
                Label = "Your socials",
                Note = "Followers back to nobody and the timeline cleared. The block carries on " +
                       "talking; it stops knowing who you are",
                Enabled = () => WipeSocials != null,
                Do = () => WipeSocials?.Invoke()
            };

            yield return new Opt
            {
                Kind = OptKind.Danger,
                Label = "Unlock everything",
                Note = "Every job marked done, every leader met, the port open, Gerald's " +
                       "packages cleared and the rank to match. For seeing the far end of the " +
                       "mod without playing the near end again",
                Do = () =>
                {
                    _state.UnlockEverything(AllMissionIds(), AllGangIds());
                    Notify.Important("~g~Everything's open.~s~");
                }
            };

            yield return new Opt
            {
                Kind = OptKind.Danger,
                Label = "Unlock Lamar's work",
                Note = "Every job on his list open at once. He works the list in order, so this " +
                       "marks them all as done -- and a done job still shows on his list as " +
                       "one you can run again",
                Do = () =>
                {
                    foreach (var id in AllMissionIds()) _state.MarkDone(id);

                    _state.Touch();
                    Notify.Important("~g~Lamar's list is open.~s~");
                }
            };

            yield return new Opt
            {
                Kind = OptKind.Danger,
                Label = "Start the mod over",
                Note = "Everything this mod has written down, gone -- standing, rank, record, " +
                       "unlocks, contacts and the stash with them. Your money and your guns " +
                       "are the game's, not ours, so they stay",
                Do = () =>
                {
                    _state.ForgetEverything();

                    // AND you leave the set, which the first version of this did not do.
                    //
                    // Affiliation does not live in PlayerState -- it is its own thing with its
                    // own save -- so forgetting everything in the save forgot everything except
                    // the one fact the whole opening hangs off. You came out of a full reset
                    // still a Families man: Lamar texting you about work on a fresh start,
                    // his marker on his corner, and Gerald talking to you like somebody he had
                    // already vouched for.
                    if (_crew != null) _crew.Leave();

                    WipeSocials?.Invoke();
                    // The feed and the inbox are separate stores and only the feed was being cleared,
                    // so a man who had wiped himself back to nobody still had Gerald texting him about
                    // work he had never done.
                    Social.Inbox.Wipe();

                    Notify.Important("~o~Back to nobody.~s~");
                }
            };

            yield return new Opt
            {
                Kind = OptKind.Danger,
                Label = "Your name",
                Note = "Respect to nothing, and the rank with it. Deals, grams and earnings forgotten",
                Do = () => _state.ForgetName()
            };

            yield return new Opt
            {
                Kind = OptKind.Danger,
                Label = "Your money",
                Note = "The cash in hand. Not the bank and not anything you own",
                Enabled = () => Game.Player.Money > 0,
                Do = () => Game.Player.Money = 0
            };

            yield return new Opt
            {
                Kind = OptKind.Danger,
                Label = "Your guns",
                Note = "Every weapon and every round. You keep your fists",
                Do = DropAllGuns
            };

            yield return new Opt
            {
                Kind = OptKind.Danger,
                Label = "What you're carrying",
                Note = "Everything in your pockets, bagged and raw. The house is a separate row",
                Enabled = () => Stash.Total > 0.005f,
                Do = () => Stash.Clear()
            };

            yield return new Opt
            {
                Kind = OptKind.Danger,
                Label = "The stash house",
                Note = "Everything kept at the house. What is on you stays in your pockets",
                Enabled = () => _stash != null && _stash.Stash != null && _stash.Stash.Total > 0.005f,
                Do = () => { if (_stash != null && _stash.Stash != null) _stash.Stash.Clear(); }
            };

            yield return new Opt
            {
                Kind = OptKind.Danger,
                Label = "All of it",
                Note = "Gangs, jobs, socials, your name, your money, your guns and every gram. " +
                       "None of it comes back",
                Do = () =>
                {
                    _crew.ResetEverything();
                    _state.ForgetMissions();
                    _state.ForgetName();

                    WipeSocials?.Invoke();

                    Social.Inbox.Wipe();

                    Game.Player.Money = 0;
                    DropAllGuns();

                    Stash.Clear();
                    if (_stash != null && _stash.Stash != null) _stash.Stash.Clear();
                }
            };
        }

        /// <summary>
        /// Every weapon and every round.
        ///
        /// The flag says "and the ammo with them" -- without it the guns go and the rounds stay
        /// in a pocket nothing can see, so picking one up off the floor hands it back loaded.
        /// </summary>
        private static void DropAllGuns()
        {
            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            try { Function.Call(Hash.REMOVE_ALL_PED_WEAPONS, player.Handle, true); }
            catch { /* he keeps them, and the panel still says he has them */ }
        }


        /// <summary>Breaks a sentence into panel rows, since a row is not a paragraph.</summary>
        private static IEnumerable<string> Split(string text)
        {
            const int width = 46;

            var line = "";

            foreach (var word in text.Split(' '))
            {
                if (line.Length + word.Length + 1 > width)
                {
                    yield return line;
                    line = word;
                    continue;
                }

                line = line.Length == 0 ? word : line + " " + word;
            }

            if (line.Length > 0) yield return line;
        }

        /// <summary>How this gang currently reads to the player, in two words.</summary>
        private string RelationLabel(GangDef gang)
        {
            var standing = _crew.StandingFor(gang.Id);

            if (_crew.IsAffiliated)
            {
                var mine = _crew.Current;
                if (_crew.Beefing(gang.Id)) return "AT WAR";
            }

            if (standing.Rep <= -50f) return "HOSTILE";
            if (standing.Rep >= 100f) return "TRUSTED";
            return "rep " + standing.Rep.ToString("0");
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
                     _turf.Owner?.Colour ?? (Color?)Palette.TextDim, "mask.png");
            page.Row("To you", TurfWord(), TurfTint(), "mask.png");
            page.Row("Been clocked", _turf.IsExposed ? "yes -- they have seen you" : "not yet",
                     _turf.IsExposed ? Palette.Warn : (Color?)Palette.TextDim, "warning.png");

            // HOW WORKED OVER IT IS, said plainly. The trade slowing down on a block he has
            // been standing on all evening is something he did, and a consequence the player
            // cannot see is indistinguishable from a bug -- he would go looking for what broke
            // rather than walking two streets over, which is the entire point of the mechanic.
            if (_pricing != null && _pricing.Blocks != null)
            {
                var worked = _pricing.Blocks.Saturation(_turf.ZoneCode);

                page.Row("Trade here",
                         _pricing.Blocks.IsHot(_turf.ZoneCode)
                             ? "busy -- " + _pricing.Blocks.Word(_turf.ZoneCode)
                             : _pricing.Blocks.Word(_turf.ZoneCode),
                         _pricing.Blocks.IsHot(_turf.ZoneCode) ? Palette.Cash
                             : worked < 0.25f ? Palette.Cash
                                        : worked < 0.5f ? (Color?)Palette.TextDim
                                                        : Palette.Warn,
                         _pricing.Blocks.IsHot(_turf.ZoneCode) ? "footfall.png" : "weed.png");
            }

            // It is no longer only about this block, so it no longer says it is. The panel
            // behind this opens on the block and then runs through every set in the city.
            page.Add("The numbers", "=", ShowBlockNumbers,
                detail: "This block, and every set you have history with",
                value: "");
            page.WithIcon(Icons.FromFile("mask.png"));

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

    }
}
