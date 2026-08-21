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

        /// <summary>Set by Main: opens the feed, and reads the follower count for the wedge.</summary>
        public Action ShowSocials;

        /// <summary>Whether somebody is already on their way about something you said.</summary>
        public Func<bool> PaybackDue;
        public Func<int> Followers;

        /// <summary>Set by Main: clears the feed and the follower count.</summary>
        public Action WipeSocials;

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
                             readyGrams.ToString("0.#") + "g ready");

            var cap = Math.Max(1f, Stash.Capacity);

            onYou.Meter("Carrying",
                        Stash.Total.ToString("0.#") + "g of " + cap.ToString("0") + "g",
                        Stash.Total / cap,
                        Stash.FreeSpace < 15f ? Palette.Danger
                            : Stash.FreeSpace < 40f ? Palette.Warn
                            : Palette.Cash,
                        Stash.FreeSpace.ToString("0") + "g of room left");

            onYou.Row("Cash", Money(Game.Player.Money), Palette.Cash);
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

                ready.Row(drug.Name, "$" + _pricing.SaleValue(drug, have, purity).ToString("N0"),
                          Palette.Cash,
                          r => { r.Note = note; r.Art = art; r.ArtTint = ProductArt; });

                bagged++;
            }

            if (bagged == 0) ready.Row("Nothing bagged up", "", Palette.TextDim);
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

                weight.Row(drug.Name, drug.Amount(have), Palette.Warn,
                           r => { r.Note = note; r.Art = art; r.ArtTint = ProductArt; });
            }

            if (raw <= 0.005f)
            {
                weight.Row("No weight on you", "", Palette.TextDim);
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

                    home.Row(drug.Name, drug.Amount(have), Palette.Cash);
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
                               .ToString("N0") + " respect");

            if (!maxed)
            {
                var need = PlayerState.RankThresholds[_state.Rank + 1] - _state.Respect;

                you.Meter("Next up", PlayerState.RankNames[_state.Rank + 1], _state.RankProgress,
                          Palette.Cash,
                          Math.Max(0f, need).ToString("N0") + " respect to go");
            }

            // Five rows saying "you are third of five" become one row that shows it.
            you.Row("The ladder", (_state.Rank + 1) + " of " + PlayerState.RankNames.Length,
                    Palette.Cash,
                    r =>
                    {
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

            // ---- trade -------------------------------------------------------
            var trade = new InfoSection { Title = "Trade" };

            // Deals and grams are the supporting detail for the money, which is what a note is.
            trade.Hero("Total earned", Money(_state.TotalEarned), Palette.Cash,
                       _state.TotalDealsMade.ToString("N0") + " deals  ·  " +
                       _state.GramsSold.ToString("0.#") + "g moved");

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

            if (carried.Count == 0) holding.Row("Nothing on you", "", Palette.TextDim);
            foreach (var line in carried) holding.Row(line[0], line[1], Palette.Cash);

            holding.Row("Free space", Stash.FreeSpace.ToString("0") + "g", null,
                        r => r.ArtFile = "box.png");
            holding.Row("Worth", "$" + PackagedValue().ToString("N0"), Palette.Cash);
            sections.Add(holding);

            var atHouse = new InfoSection { Title = "At the house" };
            var stored = _stash == null ? new List<string[]>() : DrugLines(_stash.Stash);

            if (stored.Count == 0) atHouse.Row("Nothing at the house", "", Palette.TextDim);
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

            block.Row("Where", _turf.ZoneName, TurfTint());

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
                    mine ? gang.Colour : standing.Rep < 0 ? Palette.Danger : (Color?)Palette.Text);
            you.Row("Rep with them", standing.Rep.ToString("N0"),
                    standing.Rep < 0 ? Palette.Danger : Palette.Cash);
            you.Row("Bodies for them", standing.Kills.ToString("N0"));
            you.Row("Deals done", standing.Deals.ToString("N0"), null,
                    r => r.ArtFile = "deal.png");
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
                detail: "Re-up, bag up, go to work",
                value: DrugsSummary(),
                enabled: !_cutting.IsBusy,
                disabledReason: "You're working the counter");
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

            // Its own wedge rather than a line inside something else. What the block is saying
            // about you is not a sub-heading of your inventory, and burying it two levels down
            // would mean nobody ever reads it -- which defeats the entire point of writing it.
            //
            // A leaf, not a submenu. Everything the sub-page offered -- saying something,
            // naming a set, calling one out -- is a section inside the feed screen now, next to
            // the timeline those posts land in. A wheel page whose four items were "open a
            // screen" and three things belonging ON that screen was one door too many.
            page.Add("Socials", "@", () => ShowSocials?.Invoke(),
                detail: PaybackDue != null && PaybackDue()
                    ? "Somebody's coming about what you said"
                    : "What the block is saying, and what you say back",
                value: Followers == null ? "" : Followers().ToString("N0") + " followers",
                enabled: ShowSocials != null,
                disabledReason: "Not right now");
            page.WithIcon(Icons.Tattoo);

            return page;
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

            page.Row("ON YOU", "", Palette.TextDim);

            if (onYou.Count == 0) page.Row("nothing", "", Palette.TextDim);
            foreach (var line in onYou) page.Row(line[0], line[1], Palette.Cash);

            page.Row("AT THE HOUSE", "", Palette.TextDim);

            if (atHouse.Count == 0) page.Row("nothing", "", Palette.TextDim);
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

        private string SupplyDetail()
        {
            if (_dealers.InReach != null) return "Your contact is right here";
            if (_dealers.HasMeet) return _dealers.MeetDealer.Name + " -- " +
                                          _dealers.MeetDistance.ToString("0") + "m away";
            return "Text a contact for bulk weight";
        }

        // ---- drugs -------------------------------------------------------------

        /// <summary>Everything to do with product, in the order you actually do it.</summary>
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
                run.Row("Where", Delivery.State == DeliveryState.Texting
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
                    disabledReason: "He's on his way");
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
                    detail: "Follow the blip and walk up on him",
                    value: _dealers.MeetDistance.ToString("0") + "m",
                    enabled: false, disabledReason: "Get to the meet");
                page.WithIcon(Icons.FromFile("mask.png"));

                page.Add("Call off", "x", () => _dealers.CancelMeet("You called it off."),
                    detail: "Cancel the meet");
                page.WithIcon(Icons.FromFile("warning.png"));
                return page;
            }

            // Nobody in reach. Exactly one thing you can do from here: phone the docks. The
            // gangs are people you walk up to and the independents are places you drive to, so
            // listing all seven as pickable wedges was a directory pretending to be a menu.
            var list = new WheelPage("Re-up", "Buying weight");

            list.PanelTitle = "Where the weight comes from";
            list.Row("Cash", "$" + Game.Player.Money.ToString("N0"), Palette.Cash);
            list.Row("Room left", Stash.FreeSpace.ToString("0") + "g");
            list.Row("Gangs", "talk to their leader", Palette.TextDim);
            list.Row("The port", _state.DocksUnlocked ? "he delivers" : "you don't know nobody",
                     _state.DocksUnlocked ? Palette.Cash : (Color?)Palette.TextDim);

            var docks = _dealers.Docks();

            if (docks == null)
            {
                list.Add("Nothing", "-", null,
                    detail: "Nobody in your phone",
                    enabled: false, disabledReason: "Nobody to text");
                list.WithIcon(Icons.FromFile("locked.png"));

                return list;
            }

            if (!_state.DocksUnlocked)
            {
                var toGo = DealerManager.GramsUntilSource(_state, _cfg.DocksUnlockGrams);

                list.Add("Text the plug", "=", null,
                    detail: "Dock worker. Ask Stretch about him once you've moved enough",
                    value: toGo.ToString("0") + "g more to sell",
                    enabled: false, disabledReason: "You don't know nobody at the port");
                list.WithIcon(Icons.Locked);
                return list;
            }

            var blocked = _dealers.RefusalReason(docks, _state, _crew);

            list.Add("Text the plug", "=", () => Call(docks),
                detail: blocked == null
                    ? "Dock worker. " + docks.Name + " pulls up out front with whatever you want"
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

            var accepted = Stash.AddBulk(product.Id, supplied);
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

            if (_crew.IsAffiliated)
            {
                var mine = _crew.CurrentStanding;

                page.Row("Your rep", mine == null ? "0" : mine.Rep.ToString("0"),
                         mine != null && mine.Rep < 0 ? Palette.Danger : Palette.Cash);
                page.Row("Bodies for them", mine == null ? "0" : mine.Kills.ToString("N0"));
                page.Row("Beefing with", BeefNames(), Palette.Danger);
            }

            page.Row("You are on", _turf.ZoneName, TurfTint());
            page.Row("Whose block", TurfWord(), TurfTint());

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
            }

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
                    Palette.TextDim);
            }
            else
            {
                page.Row("Work", "Lamar's got it", Palette.TextDim);
            }

            // Standing is a gang question, so the readout lives here rather than on the root.
            page.Add("How you stand", "*", ShowStatus,
                detail: "Your rank, your heat, and what every gang thinks of you",
                value: _state.RankName);
            page.WithIcon(Icons.Tattoo);

            // Behind its own page, because none of it can be undone and a wheel is a thing you
            // flick through. One accidental commit should never erase a save's worth of
            // standing -- so the flick lands on a list of questions rather than on the act.
            page.AddSub("Start over", "x", BuildStartOverPage,
                detail: "Undo what you have done, in pieces or all at once",
                value: "");
            page.WithIcon(Icons.Warning);

            return page;
        }

        /// <summary>
        /// Everything you can undo, in pieces.
        ///
        /// Separate entries rather than one big reset, because the reasons people want these are
        /// unrelated: wanting to run with a different set has nothing to do with wanting to play
        /// Lamar's chain again, and neither has anything to do with a follower count that ran
        /// away with itself. One button for all three would make two of them collateral.
        /// </summary>
        private WheelPage BuildStartOverPage()
        {
            var page = new WheelPage("Start over", "None of this can be undone");

            page.PanelTitle = "Where you are";
            page.Row("Running with", _crew.IsAffiliated ? _crew.Current.Name : "nobody",
                     _crew.IsAffiliated ? _crew.Current.Colour : (Color?)Palette.TextDim);
            page.Row("Respect", _state.Respect.ToString("N0") + "  ·  " + _state.RankName);
            page.Row("Cash", "$" + Game.Player.Money.ToString("N0"), Palette.Cash);
            page.Row("On you", Stash.Total.ToString("0.#") + "g");
            page.Row("At the house",
                     _stash == null || _stash.Stash == null
                         ? "0g"
                         : _stash.Stash.Total.ToString("0.#") + "g");
            page.Row("Jobs finished", _state.MissionsDone.Count.ToString());
            page.Row("Followers", Followers == null ? "0" : Followers().ToString("N0"), Palette.Cash);

            page.AddSub("The gangs", "%", () => Confirm(
                    "Wipe the gangs",
                    "Every standing, every body, every dollar you made them, and whoever you run " +
                    "with. Your respect, your money and your product are untouched.",
                    () => _crew.ResetEverything()),
                detail: "Standings, affiliation and any debt",
                value: _crew.IsAffiliated ? _crew.Current.Tag : "SOLO");
            page.WithIcon(Icons.Mask);

            page.AddSub("Lamar's work", "!", () => Confirm(
                    "Forget the jobs",
                    "He works down his list from the top again. What he already paid you stays paid.",
                    () => _state.ForgetMissions()),
                detail: "Play his chain from the beginning",
                value: _state.MissionsDone.Count + " done",
                enabled: _state.MissionsDone.Count > 0,
                disabledReason: "You have not finished any yet");
            page.WithIcon(Icons.Tick);

            page.AddSub("Your socials", "@", () => Confirm(
                    "Wipe your socials",
                    "Followers back to nobody and the timeline cleared. The block carries on " +
                    "talking; it just stops knowing who you are.",
                    () => WipeSocials?.Invoke()),
                detail: "Followers and the whole timeline",
                value: Followers == null ? "" : Followers().ToString("N0") + " followers",
                enabled: WipeSocials != null,
                disabledReason: "Nothing to wipe");
            page.WithIcon(Icons.Tattoo);

            page.AddSub("Your name", "*", () => Confirm(
                    "Back to nobody",
                    "Respect to nothing, so the rank goes with it -- it is worked out from the " +
                    "respect rather than stored. Deals, grams and earnings forgotten, and what " +
                    "the block reckons of your product back to the middle.",
                    () => _state.ForgetName()),
                detail: "Respect, rank and everything you have moved",
                value: _state.Respect.ToString("N0") + " respect");
            page.WithIcon(Icons.Tattoo);

            page.AddSub("Your money", "$", () => Confirm(
                    "Empty your pockets",
                    "Every dollar on you, gone. Not what is in the bank and not anything you " +
                    "own -- this is the cash in hand and nothing else.",
                    () => Game.Player.Money = 0),
                detail: "The cash in your pocket",
                value: "$" + Game.Player.Money.ToString("N0"),
                enabled: Game.Player.Money > 0,
                disabledReason: "You have not got any");
            page.WithIcon(Icons.Money);

            page.AddSub("Your guns", "!", () => Confirm(
                    "Drop every gun",
                    "Every weapon and every round, off you. You keep your fists, which is what " +
                    "the game leaves you with whatever it is told.",
                    DropAllGuns),
                detail: "Every weapon and all the ammo",
                value: "");
            page.WithIcon(Icons.Guns);

            page.AddSub("What you're carrying", "%", () => Confirm(
                    "Bin what is on you",
                    "Everything in your pockets, bagged and unbagged, gone. What is at the " +
                    "stash house is a separate button and stays where it is.",
                    () => Stash.Clear()),
                detail: "Product on you, bagged and raw",
                value: Stash.Total.ToString("0.#") + "g",
                enabled: Stash.Total > 0.005f,
                disabledReason: "You are not carrying anything");
            page.WithIcon(Icons.Stash);

            page.AddSub("The stash house", "%", () => Confirm(
                    "Empty the house",
                    "Everything kept at the house, gone. What is on you right now is a separate " +
                    "button and stays in your pockets.",
                    () => { if (_stash != null && _stash.Stash != null) _stash.Stash.Clear(); }),
                detail: "Everything kept at the house",
                value: _stash == null || _stash.Stash == null
                    ? "0g" : _stash.Stash.Total.ToString("0.#") + "g",
                enabled: _stash != null && _stash.Stash != null && _stash.Stash.Total > 0.005f,
                disabledReason: "There is nothing in it");
            page.WithIcon(Icons.Stash);

            page.AddSub("All of it", "x", () => Confirm(
                    "Wipe all of it",
                    "Gangs, jobs, socials, your name, your money, your guns and every gram on " +
                    "you and at the house. Everything on this page at once, and none of it " +
                    "comes back.",
                    () =>
                    {
                        _crew.ResetEverything();
                        _state.ForgetMissions();
                        _state.ForgetName();

                        WipeSocials?.Invoke();

                        Game.Player.Money = 0;
                        DropAllGuns();

                        Stash.Clear();
                        if (_stash != null && _stash.Stash != null) _stash.Stash.Clear();
                    }),
                detail: "Every button on this page at once",
                value: "");
            page.WithIcon(Icons.Warning);

            return page;
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

        /// <summary>
        /// A page that asks before it does.
        ///
        /// Built rather than written out four times, and the wording is always specific about
        /// what survives -- "are you sure" tells nobody anything they can weigh.
        /// </summary>
        private WheelPage Confirm(string title, string what, Action act)
        {
            var page = new WheelPage(title, "There is no undo");

            page.PanelTitle = title;
            foreach (var line in Split(what)) page.Row(line, "");

            page.Add("Do it", "!", act,
                detail: what,
                value: "no undo");
            page.WithIcon(Icons.Warning);

            page.Add("Leave it", "-", null,
                detail: "Nothing changes",
                value: "",
                enabled: false,
                disabledReason: "Back out with LT");
            page.WithIcon(Icons.Tick);

            return page;
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

        /// <summary>Everything you can do with one particular gang.</summary>
        private WheelPage BuildGangPage(GangDef gang)
        {
            var mine = _crew.IsAffiliated && _crew.Current.Id == gang.Id;
            var standing = _crew.StandingFor(gang.Id);
            var atWar = _crew.IsAffiliated && !mine &&
                        _crew.Beefing(gang.Id);

            var page = new WheelPage(gang.Name, mine ? "You run with them" : RelationLabel(gang));

            page.PanelTitle = gang.Name;
            page.Row("They run", gang.TurfHint);
            page.Row("They move", DrugNames(gang));
            page.Row("Their old rivals", RivalNames(gang));
            // Two different questions, and they used to carry the same label -- the panel read
            // "With you: no problem" directly above "With you: you run with them".
            page.Row("Beef", _crew.Beefing(gang.Id) ? "at war" : "no problem",
                     _crew.Beefing(gang.Id) ? Palette.Danger : (Color?)Palette.TextDim);
            page.Row("Where you stand", mine ? "you run with them" : RelationLabel(gang),
                     mine ? gang.Colour : atWar ? Palette.Danger : (Color?)null);

            // Joining only. There is no walking away.
            //
            // The wedge that used to be here called Leave and put you in a state the rest of
            // the mod has no answer for: no block to hold, nobody to stand with, no rep to
            // earn, and no route back except the man you just walked out on. One set, and it
            // is the one you are in.
            if (mine)
            {
                page.Add("You run with them", "-", null,
                    detail: "This is your set.",
                    value: "",
                    enabled: false, disabledReason: "You're already in");
                page.WithIcon(Icons.Tick);
            }
            else
            {
                // Joining is a conversation with the man himself, never a wedge.
                page.Add("You don't run with them", "-", null,
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

                page.Add("Text the plug", "+", () => Call(plug),
                    detail: plug.BuyLine,
                    value: carries + "  ·  " + Multiplier(1f / Math.Max(0.01f, mult)),
                    enabled: refusal == null,
                    disabledReason: refusal ?? "");
                page.WithIcon(Icons.Money);
            }
            else
            {
                page.Add("Text the plug", "+", null,
                    detail: "They have nobody you can call",
                    enabled: false, disabledReason: "No contact");
                page.WithIcon(Icons.Locked);
            }

            // "How you stand" is what the Gangs page above calls its own status entry, and two
            // wedges one level apart with the same words on them is how a menu stops being
            // readable. This one is about THEM.
            page.Add("What they think", "*", () => ShowGangDetail(gang),
                detail: "What you have done for them, and what they hold",
                value: "");
            page.WithIcon(Icons.Tattoo);

            return page;
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
