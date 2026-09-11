using System;
using System.Collections.Generic;
using Color = System.Drawing.Color;
using GTA;
using Hoodrich.Core;
using Hoodrich.Economy;
using Hoodrich.Gangs;
using Hoodrich.State;
using Hoodrich.UI;

namespace Hoodrich.Supply
{
    /// <summary>One thing Tao Cheng will sell you, in the quantity he sells it in.</summary>
    internal sealed class Brick
    {
        public readonly string DrugId;
        public readonly float Grams;

        public Brick(string drugId, float grams)
        {
            DrugId = drugId;
            Grams = grams;
        }
    }

    /// <summary>
    /// Buying weight off Tao Cheng.
    ///
    /// He does not do grams and he does not do ounces. Every line on his list is what a
    /// container moves -- fifty kilos of weed, five of cocaine, ten thousand pills -- and what
    /// changes down the column is both the weight and the money, because the two are not the
    /// same question. Gerald sells you enough to work a corner tonight; Tao Cheng sells you
    /// enough that the corner stops being the interesting part.
    ///
    /// It all goes to the house, not your pockets. You cannot walk around with twelve kilos of
    /// anything, and pretending otherwise would make the stash house decorative.
    /// </summary>
    internal sealed class DealerTalk
    {
        /// <summary>The guard for a man with no numbers of his own. See DealerDef.PriceFloor.</summary>
        public const int Floor = 500;

        /// <summary>
        /// What he is holding today: the written six, then everything else in the catalogue.
        ///
        /// A load is sized by the PRODUCT and priced by what is in it, which is the way it
        /// is actually sold. Two wrong answers came first and both are worth remembering. Sized
        /// by what it was WORTH, a fixed spend came back as a different weight on every row --
        /// twenty-five kilos of weed against two of cocaine at the same hundred thousand
        /// dollars, which is a spreadsheet rather than a deal. Sized as one FLAT weight for
        /// everything, every row was a kilo and the cheap end cost pocket change, because a
        /// kilo of weed is not a wholesale quantity of weed.
        ///
        /// So the unit belongs to the drug -- see DrugDef.LotGrams -- and the dealer takes his
        /// own fraction of it. Rounded to his step so the figure still sounds like something a
        /// person would say.
        ///
        /// Made-only products are skipped. Nobody buys rolled joints off a container.
        /// </summary>
        private Brick[] StockToday()
        {
            var def = Def;
            var list = new List<Brick>();

            // What HE carries, which is a thing the dealer data has always said and this menu
            // has never once read. Every dealer was offered the whole catalogue -- so the man
            // whose own buy line is "I ain't the port, don't ask me for no bricks" was stood
            // there selling twelve kilos of weed, cocaine and heroin.
            //
            // An empty list still means everything, which is how the port is described.
            foreach (var d in _drugs.All)
            {
                if (d.MadeOnly) continue;
                if (def != null && def.Drugs.Count > 0 && !Sells(def, d.Id)) continue;

                list.Add(new Brick(d.Id, LotOf(def, d)));
            }

            return list.ToArray();
        }

        private static bool Sells(DealerDef def, string drugId)
        {
            for (var i = 0; i < def.Drugs.Count; i++)
            {
                if (string.Equals(def.Drugs[i], drugId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// One lot off this man, which is the product's own wholesale unit cut to his size.
        ///
        /// The arithmetic lives on DealerDef, because the phone path needs the same answer and
        /// two routines working out how big a lot is would drift apart within a week. This is
        /// only here to answer for the man who is nobody -- a null dealer takes the unit whole.
        /// </summary>
        private static float LotOf(DealerDef def, DrugDef drug)
        {
            if (def != null) return def.LotFor(drug);

            return drug == null ? 500f : drug.LotGrams;
        }

        /// <summary>How many of one thing you can take at once.</summary>
        private static readonly int[] Lots = { 1, 2, 4 };

        private readonly Delivery _delivery;

        /// <summary>
        /// Who you are stood in front of, when it is not a delivery.
        ///
        /// This screen was written for the man who drives to your door, so it read the dealer
        /// off the DELIVERY -- and the only other way to buy, walking up to one at his own meet
        /// spot, went through a completely separate wheel page with its own prices. Two screens
        /// quoting different numbers for the same man.
        ///
        /// Set before Root() to talk to somebody in person; left null for a delivery, which
        /// still answers with whoever is driving.
        /// </summary>
        public DealerDef Who;

        /// <summary>The man this conversation is about, however it started.</summary>
        private DealerDef Def => Who ?? (_delivery == null ? null : _delivery.Def);
        private readonly Drugs _drugs;
        private readonly Pricing _pricing;
        private readonly PlayerState _state;
        private readonly Affiliation _crew;

        /// <summary>Set by Main. Everything he sells lands here.</summary>
        public Stash House;

        public DealerTalk(Delivery delivery, Drugs drugs, Pricing pricing,
                          PlayerState state, Affiliation crew)
        {
            _delivery = delivery;
            _drugs = drugs;
            _pricing = pricing;
            _state = state;
            _crew = crew;
        }

        private string Name => Def == null ? "Tao Cheng" : Def.Name;

        /// <summary>
        /// His price, and whatever he has knocked off it tonight.
        ///
        /// The cold call multiplies rather than replaces, so a dealer who is dear stays
        /// relatively dear -- an offer is him being generous by his own standards, not
        /// everybody converging on the same number when a text goes out.
        /// </summary>
        private float Multiplier =>
            (Def == null ? 0.75f : Def.PriceNow) *
            (Def == null ? 1f : ColdCall.Multiplier(Def.Id));

        /// <summary>
        /// The recording of his goodbye, which the screen plays on every way out of it: his
        /// farewell from dealers.json, under the name the rest of his pack uses.
        /// </summary>
        /// <summary>
        /// His goodbye, but only when you walked up to him.
        ///
        /// NOT ON A DELIVERY. Conversation.Close plays this on every way out of the screen,
        /// and on a delivery the screen closes when you have finished BUYING -- so he said
        /// goodbye and then carried a box up your path and into your house. Delivery cues it
        /// instead, at the car door, which is where a goodbye goes.
        ///
        /// Face to face it is still right here: the screen closing is him finishing with you,
        /// and there is nothing after it.
        /// </summary>
        private string Bye => Def == null || Who == null || string.IsNullOrEmpty(Def.Farewell)
            ? ""
            : Voice.Key(Name, Def.Farewell);

        private DialogueNode Node(string line) =>
            new DialogueNode(Name, line) { SpeakerColour = Palette.Cash, Farewell = Bye };

        /// <summary>
        /// His words where he has them, the shared ones where he does not. See DealerDef.
        /// </summary>
        private string His(string mine, string shared)
        {
            return string.IsNullOrEmpty(mine) ? shared : mine;
        }

        /// <summary>
        /// A line that says itself and hands on. See DialogueNode.Beat.
        ///
        /// No row under it, so nothing to press: it plays for as long as the recording lasts
        /// and the next thing is up. That is the only reason a greeting can exist at all here
        /// -- a page you have to click past to reach the stock would cost every player a
        /// button every time to hear a line they have heard before.
        /// </summary>
        private DialogueNode Beat(string line, Func<DialogueNode> next)
        {
            return new DialogueNode(Name, line)
            {
                SpeakerColour = Palette.Cash,
                Farewell = Bye
            }.Beat(next);
        }

        /// <summary>
        /// True while this is the first node of a NEW conversation. Set by Main, spent here.
        ///
        /// Root is re-entered constantly -- every "show me", every back-out of a sub-page --
        /// and a man who says hello each time you return to his stock list is a jingle. There
        /// is nothing on this class that can tell the two apart, so the caller says so.
        /// </summary>
        public void Fresh() { _opening = true; }

        private bool _opening;

        public DialogueNode Root()
        {
            if (House == null)
            {
                return Node(His(Def == null ? "" : Def.NoRoomLine,
                                "Come back when you got somewhere to put it."));
            }

            // He says it before you ask, because it is the thing that decides everything you
            // can do with the weight afterwards. Pure cuts three ways and gets you three times
            // the bags; half-strength cuts once and the second cut is powder nobody will buy.
            // Standing in a menu working out why the same money bought half as much product is
            // a fact the game had and did not mention.
            // HELLO FIRST, AND HIS NUMBER IF YOU HAVE NOT MET. Both are beats, so neither
            // costs a press -- see Beat. Only on the opening node of a conversation; Root is
            // re-entered every time you back out of a sub-page.
            if (_opening)
            {
                _opening = false;

                var hello = Def == null ? "" : Def.Greeting;

                // THE NUMBER, ONCE. Read and cleared here, so it is said in the conversation
                // where you met him and never again -- a man who hands you his number every
                // time you walk up is not a contact, he is a jingle.
                var number = "";

                if (Def != null && Def.JustMet)
                {
                    Def.JustMet = false;
                    number = Def.NumberLine ?? "";
                }

                if (!string.IsNullOrEmpty(number))
                {
                    return string.IsNullOrEmpty(hello)
                        ? Beat(number, Counter)
                        : Beat(hello, () => Beat(number, Counter));
                }

                if (!string.IsNullOrEmpty(hello)) return Beat(hello, Counter);
            }

            return Counter();
        }

        /// <summary>What he is holding and what it costs. The page you actually buy from.</summary>
        private DialogueNode Counter()
        {
            var strength = Def == null ? 1f : Def.PurityNow;

            // HIS OWN WORDS WHERE HE HAS THEM. Empty falls back to the shared line, which is
            // what all three said before any of it was recorded.
            var whole = Def != null && !string.IsNullOrEmpty(Def.ShopLine)
                ? Def.ShopLine
                : "Ain't got time to stand here. What you taking? Nothing here's been touched.";

            var cut = Def != null && !string.IsNullOrEmpty(Def.ShopCutLine)
                ? Def.ShopCutLine
                : "Ain't got time to stand here. What you taking? It's all stepped on already, " +
                  Stash.Percent(strength) + " per cent.";

            var node = Node((strength >= 0.999f ? whole : cut) + Standing());

                // NO HASH CAN REACH THIS ONE. What he says is fixed, but the money on you and
                // the room at the house are stapled to the end of it and change every time -- so
                // the words are never twice the same and no file could be named after them.
                //
                // The line names itself instead, per dealer and per which of the two things he
                // says, and the recording just leaves the arithmetic out. The screen is already
                // showing it.
                node.Voiced(Voice.Named(Name, strength >= 0.999f ? "shop" : "shopcut"));

            foreach (var brick in StockToday())
            {
                var product = _drugs.Get(brick.DrugId);
                if (product == null) continue;

                var pick = brick;
                var cost = Cost(product, brick.Grams, 1);

                // The product's own words for the amount -- "40 pills", "112g" -- rather than
                // a kilo count. Kilos are true of the port and nonsense off a bicycle, where
                // the same routine was rendering an ounce as "0.1 of a kilo".
                // WHAT ONE COSTS AND WHERE THE CLEAN ONE STARTS, in the label, because the
                // detail column is invisible on every row but the highlighted one.
                //
                // "Uncut from $7,000" in the greeting with three rows all reading 50% is a shop
                // advertising something it appears not to stock. The uncut lot was always there
                // -- it is the second one -- and nothing on this screen said which.
                var clean = Def != null && Def.PurityFor(product, brick.Grams) >= 0.999f;
                var at = clean ? 0 : FirstCleanLot(product, brick);

                var strengthBit = clean
                    ? "uncut"
                    : Stash.Percent(strength) + "%" + (at > 0 ? ", uncut at " + at + "x" : "");

                node.Say(product.Name + "  --  " + product.Bulk(brick.Grams) +
                         "  from $" + cost.ToString("N0") + "  --  " + strengthBit,
                         () => Amounts(pick, product),
                         "How many");

                node.WithIcon(Icons.ForDrug(product.Id));

                // The same mark the stash and the cook screen use, on the row you buy it from.
                node.WithMark(Stash.Mark(strength));
            }

            // THE QUESTION, AGAIN, AND FOR AS LONG AS IT TAKES.
            //
            // He gives his father up at the port, once, in a scene you can walk out of by
            // picking either of the other two answers -- and everything that comes after it is
            // gated on having heard it. A one-shot line that locks a whole branch of the mod
            // behind noticing it at the time is not a choice, it is a trap.
            //
            // So he can be asked at your own door as well, every time he stands at it, until
            // you have it. He does not mind being asked. Being asked is the best thing that
            // happens to him all week.
            PutMeOnRow(node);

            SourceRow(node);

            OldManRow(node);

            node.Leave("Not today.");

            // Sending him off, which there was no way to do.
            //
            // "Not today" only closes the conversation -- he stays parked outside until
            // something else moves him. This is the other half: he gets in and drives away,
            // the same exit he takes once a delivery has landed.
            // Only offered when there is something to send away. A man you walked up to at
            // his own spot leaves when he leaves; telling him to go is the delivery's exit.
            if (Who == null)
            {
                node.Say("That's you done. Go on.", () =>
                {
                    _delivery?.Finish();
                    return null;
                }, "He gets in and goes");

                node.WithIcon(Icons.FromFile("car.png"));
            }

            return node;
        }

        /// <summary>How many bricks, once you have said what.</summary>
        /// <summary>
        /// Where he gets it, for a man whose answer is only ever flavour.
        ///
        /// NOT FOR A GANG DEALER. Theirs is "Ask source" on the wheel, and that row is the
        /// port sequence -- it counts packages, it refuses until Gerald is squared up, and it
        /// unlocks the docks. A second way of asking the same question in a conversation would
        /// make all of that optional, which is worse than the line going unsaid.
        ///
        /// So this is the other kind: a man with no port behind him, who just answers. Hao's
        /// is "a guy, at a thing", which tells you nothing and tells you everything about him.
        /// </summary>
        private void SourceRow(DialogueNode node)
        {
            if (node == null || Def == null) return;
            if (Def.IsGangDealer) return;
            if (string.IsNullOrEmpty(Def.SourceReply)) return;

            node.Say("Where you getting this?", () =>
            {
                var said = Node(Def.SourceReply);
                said.Say("Fair enough.", Counter, "Back to what he's holding");
                said.Leave();
                return said;
            }, "Ask him straight");

            node.WithIcon(Icons.FromFile("eyes.png"));
        }

        /// <summary>Only the man off the boat, and only while it is still news.</summary>
        private void OldManRow(DialogueNode node)
        {
            if (node == null || _state == null || Def == null) return;
            if (_state.KnowsCheng || _state.ToldTheOldMan) return;
            if (Def.Kind != DealerKind.Docks || Def.IsGangDealer) return;

            node.Say("Who are you, really?", Whose, "He has been waiting to be asked");
            node.WithIcon(Icons.FromFile("eyes.png"));
        }

        /// <summary>
        /// The same thing he says at the port, said on a driveway instead.
        ///
        /// Written out rather than shared with PortRun, because it is not the same scene: there
        /// he is on his own dock in front of his own men and it is a boast. Here he has driven
        /// a box across the city himself, in the evening, and it is closer to a complaint. Same
        /// fact, and the fact is the only part that has to match.
        /// </summary>
        private DialogueNode Whose()
        {
            if (_state != null && !_state.KnowsCheng)
            {
                _state.KnowsCheng = true;
                _state.Touch();

                Log.Info("Asked on the doorstep. Tao named his father anyway.");
            }

            var node = Node(
                "Who am I. Bro. CHENG. Wei Cheng, that's my father, that's the name on every " +
                "container in that yard -- and here I am, on your driveway, at night, carryin' " +
                "a box up a path like a. like a guy who carries boxes." +
                "\n\nHe don't know about " +
                "this part. Obviously he don't know about this part. And you're not gonna be " +
                "the one who tells him, 'cause then who brings you your stuff? Nobody. Exactly. " +
                "So we're good. We're good, right?");

            node.Say("We're good.", () => Root(), "Let him believe it");
            node.Leave("Sure.");
            return node;
        }

        /// <summary>
        /// What you have got and what you can hold, under whatever he just said.
        ///
        /// THE THREE THINGS EVERY ROW ON THIS SCREEN IS SILENTLY ABOUT. Standing in front of a
        /// man reading "$4,500" is a question about your wallet you have to close the menu to
        /// answer; "200g" is a question about your house you cannot answer at all from here.
        /// The screen was already refusing rows you could not pay for or fit, so it knew both
        /// numbers -- it just would not say either until you had picked something and been
        /// told no.
        ///
        /// The third is the one nobody would ever work out on their own: buying enough in one
        /// go arrives uncut. That is the whole shape of the shop and it was invisible.
        /// </summary>
        /// <summary>At or above this, the multiplier is a "never" rather than a number.</summary>
        private const float NeverUncut = 50f;

        private string Standing()
        {
            var cash = Game.Player.Money;
            var room = House == null ? 0f : House.FreeSpace;

            var line = "\n\n$" + cash.ToString("N0") + " on you  --  " +
                       Room(room) + " room at the house";

            // A LOT NOBODY CAN REACH IS NOT AN OFFER. Gerald's uncut multiplier is 999,
            // which is not a number he means -- it is how the data says "this one never sells
            // uncut, ever". Printed literally it came out as "uncut from 999x", which reads
            // as a broken number rather than as a rule, and the rule it is standing in for is
            // better said by saying nothing.
            if (Def != null && Def.PurityNow < 0.999f && Def.UncutFromLots < NeverUncut)
            {
                line += "  --  uncut from " + Def.UncutFromLots.ToString("0.#") + "x";
            }

            return line;
        }

        /// <summary>
        /// Set by Main: takes the dealer, returns a refusal or null.
        ///
        /// A callback rather than a reference to the joining code, because DealerTalk is built
        /// per conversation and handed a def -- the same reason ColdCall is static and the pure
        /// flag lives on the def. Everything it needs to know it is told.
        /// </summary>
        public Func<DealerDef, string> PutMeOn;

        /// <summary>Whether this man could put you on, and whether it is worth offering.</summary>
        public Func<DealerDef, string> JoinRefusal;

        /// <summary>
        /// Asking to be put on the set.
        ///
        /// THIS USED TO BE A MAN IN A CAR PARK. Joining was asked of a leader ped who existed
        /// for that one question, and finding him was a errand with nothing in it -- he sold
        /// you nothing, told you nothing you could not get elsewhere, and once you were in he
        /// had no further purpose. The corner dealer is on that block every day moving their
        /// product; if anybody is vouching for you it is him.
        ///
        /// Shown greyed with the reason rather than hidden, which is the opposite of what the
        /// contacts list does with strangers and right for the same underlying rule: "get more
        /// respect" is a thing you can go and do, so seeing it is a reason to go and do it.
        /// </summary>
        private void PutMeOnRow(DialogueNode node)
        {
            if (node == null || Def == null || PutMeOn == null) return;
            if (!Def.IsGangDealer) return;
            if (string.IsNullOrEmpty(Def.JoinAsk)) return;

            var refusal = JoinRefusal == null ? null : JoinRefusal(Def);

            node.Say(Def.JoinAsk, () =>
            {
                var no = PutMeOn(Def);
                if (no != null) Notify.Problem(no.ToLowerInvariant() + ".");

                return null;
            }, refusal ?? "Ask him to put you on", refusal == null, refusal ?? "");

            node.WithIcon(Icons.FromFile("crown.png"));
        }

        /// <summary>Free space, said the way somebody would say it.</summary>
        private static string Room(float grams)
        {
            // "300000g" is a number off a scale. Nobody describes a house that way.
            return grams >= 1000f
                ? (grams / 1000f).ToString("0.#") + "kg"
                : grams.ToString("0") + "g";
        }

        private DialogueNode Amounts(Brick brick, DrugDef product)
        {
            var node = Node("How many? And don't say one if you mean four." + Standing());

            // NAMED, BECAUSE NO HASH CAN EVER REACH THIS ONE.
            //
            // Standing() staples the money on you and the room at the house onto the end, so
            // the string is different on every single utterance and the file it would hash to
            // never exists twice. It carried no key at all, which made it the most-heard line
            // in the mod and the only one that could never be given a voice: every purchase
            // from every dealer comes through here.
            //
            // Counter() solved this the same way one method up -- the recording says the words
            // and leaves the arithmetic to the screen, which is already showing it.
            node.Voiced(Voice.Named(Name, "amount"));

            foreach (var lot in Lots)
            {
                var count = lot;
                var grams = brick.Grams * lot;
                var cost = Cost(product, brick.Grams, lot);

                var canPay = Game.Player.Money >= cost;
                var fits = House.FreeSpace >= grams - 0.5f;

                var blocked = !canPay ? "You're $" + (cost - Game.Player.Money).ToString("N0") + " short"
                            : !fits ? "The house won't hold that"
                            : "";

                // EACH ROW CARRIES ITS OWN STRENGTH, because they are no longer the same
                // purchase at three sizes -- the small one is a bag off him and the big ones
                // are weight, and that is the actual decision on this menu.
                var arrives = Def == null ? 1f : Def.PurityFor(product, grams);
                var clean = arrives >= 0.999f;

                node.SayIf(blocked.Length == 0, blocked,
                           Weight(product, brick, count, cost, arrives),
                           () => Buy(product, grams, cost),
                           blocked.Length > 0 ? blocked : "Take it");

                node.WithMark(Stash.Mark(arrives));

                node.WithIcon(Icons.ForDrug(product.Id));
            }

            node.Say("Something else.", Root);
            node.Leave("Forget it.");
            return node;
        }

        /// <summary>
        /// One line of the how-many list, in the product's own units.
        ///
        /// This used to read a hand-written phrase off the brick for a single lot and print
        /// KILOS for anything more -- both of which assume the man you are stood in front of
        /// deals in bricks. Off a pushbike that rendered sixty grams of weed as "0.1 kilos",
        /// and the single-lot line as nothing at all once the phrases went.
        /// </summary>
        /// <summary>
        /// The smallest multiple of this product that arrives uncut, or 0 if none does.
        ///
        /// Worked out rather than assumed, because it is not always the second: a dear dealer's
        /// single lot can already clear his own threshold and a cheap one's may need the
        /// biggest. Telling somebody "uncut at 2x" when it is actually 4x is worse than saying
        /// nothing.
        /// </summary>
        private int FirstCleanLot(DrugDef product, Brick brick)
        {
            if (Def == null) return 0;

            for (var i = 0; i < Lots.Length; i++)
            {
                if (Def.PurityFor(product, brick.Grams * Lots[i]) >= 0.999f) return Lots[i];
            }

            return 0;
        }

        /// <summary>
        /// A whole row's worth of row.
        ///
        /// THE DETAIL COLUMN ONLY DRAWS ON THE HIGHLIGHTED ROW -- Conversation renders
        /// `picked ? choice.Detail : ""` -- so anything put there is invisible until you have
        /// already arrowed onto it. On a menu whose entire job is comparing three prices that
        /// is the wrong column, and it is why the shop read as offering an uncut lot with
        /// nothing uncut in it: the promise was in the greeting and the answer was two
        /// keypresses away in a field only one row at a time could show.
        ///
        /// So the label carries the lot, the weight, the money and the strength, and every row
        /// says all of itself at once.
        /// </summary>
        private static string Weight(DrugDef product, Brick brick, int lot, int cost, float purity)
        {
            var line = lot + "x  --  " + product.Bulk(brick.Grams * lot) +
                       "  --  $" + cost.ToString("N0");

            return line + (purity >= 0.999f ? "  --  uncut" : "  --  " + Stash.Percent(purity) + "%");
        }

        /// <summary>
        /// What it costs, rounded to something a man would actually say out loud.
        ///
        /// Never below the floor: if the maths came out at forty-eight thousand he would round
        /// it up rather than break his own rule, and so does this.
        /// </summary>
        private int Cost(DrugDef product, float gramsPerBrick, int lot)
        {
            var def = Def;

            // His floor and his rounding, not the port's. A five hundred dollar step on a two
            // hundred and eighty dollar bag is a forty percent error before the floor gets
            // anywhere near it.
            var floor = def == null ? Floor : def.PriceFloor;
            var step = def == null || def.PriceStep < 1 ? 500 : def.PriceStep;

            var raw = _pricing.WholesalePrice(product, Multiplier) * gramsPerBrick * lot;

            var rounded = (int)(Math.Round(raw / step) * step);
            return Math.Max(floor * lot, rounded);
        }

        /// <summary>
        /// Paying for it. Nothing arrives yet.
        ///
        /// The money leaves now and the goods land when the box is on the floor inside, because
        /// the whole reason he walks it in is that it has not been delivered until he has. It
        /// also means the only way to lose an order is to make it impossible for him to reach
        /// the door -- which is a thing you did, not a thing that happened to you.
        /// </summary>
        private DialogueNode Buy(DrugDef product, float grams, int cost)
        {
            if (House.FreeSpace < grams - 0.5f)
            {
                return Node(His(Def == null ? "" : Def.NoSpaceLine,
                                "You got nowhere to put it. Sort that out first."));
            }

            UI.Cash.Take(cost);
            _state.Touch();

            if (_crew != null) _crew.CreditPurchase();

            // In person, it changes hands where you are stood. There is no courier to walk it
            // anywhere -- that whole sequence belongs to the man who drove out to your door.
            if (Who != null)
            {
                // Weight either way -- his is just weaker.
                //
                // It used to land as finished bags when a plug sold cut product, on the reading
                // that "already cut" meant "already bagged". It does not. A man who steps on
                // his weight before he sells it has sold you weaker WEIGHT: you still have to
                // cut and bag it, you simply have less to work with, and cutting fifty per cent
                // to a half again leaves you twenty-five, which is the punishment for trying.
                // Weight is weight whoever sold it. A brick has not been opened; the bags in
                // his jacket have.
                var strength = Def == null ? 1f : Def.PurityFor(product, grams);
                var cut = strength < 0.999f;

                var pocket = _state.Stash.AddBulk(product.Id, grams, strength);
                var spare = grams - pocket;

                // Whatever will not fit goes to the house, and anything that fits nowhere is
                // refunded rather than taken off you for nothing.
                if (spare > 0.005f && House != null)
                {
                    spare -= House.AddBulk(product.Id, spare, strength);
                }

                if (spare > 0.005f) UI.Cash.Give((int)(cost * (spare / grams)));

                Notify.Important("~y~-$" + cost.ToString("N0") + "~s~  " +
                                 product.Bulk(grams - Math.Max(0f, spare)) + " of " +
                                 product.Name.ToLowerInvariant() +
                                 (cut ? "  ~y~" + Stash.Percent(strength) + "%" : ""));

                Log.Info("Bought " + grams.ToString("0") + "g " + product.Id + " off " + Name +
                         " for $" + cost + ", hand to hand.");

                var got = Node(His(Def == null ? "" : Def.HandOverLine,
                                   "Don't stand there holding it. Go on."));
                got.Leave("Aight.");
                return got;
            }

            _delivery.Deliver(product.Id, grams);

            // Whatever this drug is actually counted in.
            //
            // Everything was divided by a thousand and called kilos, which is right for a
            // brick off a boat and nonsense for twenty pills -- Gerald's rolls came through as
            // "0 kilos of ecstasy", a delivery of nothing. DrugDef already knows whether it
            // deals in grams or in units, and every other readout in the mod asks it.
            Notify.Important("~y~-$" + cost.ToString("N0") + "~s~  " +
                             product.Bulk(grams) + " of " +
                             product.Name.ToLowerInvariant());

            Log.Info("Bought " + grams.ToString("0") + "g " + product.Id + " off " + Name +
                     " for $" + cost + "; he is walking it in.");

            var node = Node(His(Def == null ? "" : Def.WalkItInLine,
                                "Stand aside. I'll put it inside for you."));
            node.Leave("Go on then.");
            return node;
        }
    }
}
