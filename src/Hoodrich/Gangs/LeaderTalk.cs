using System;
using GTA;
using Hoodrich.Core;
using Hoodrich.Economy;
using Hoodrich.State;
using Hoodrich.UI;

namespace Hoodrich.Gangs
{
    /// <summary>
    /// What a gang leader will actually talk to you about.
    ///
    /// Signing on used to be a wedge, which made joining a crew feel like changing a setting.
    /// It belongs here instead: you find him, you walk up, and you ask -- and he can say no to
    /// your face, which a menu cannot do.
    /// </summary>
    internal sealed class LeaderTalk
    {
        private readonly GangLeaders _leaders;
        private readonly GangRegistry _gangs;
        private readonly Affiliation _crew;
        private readonly PlayerState _state;
        private readonly Drugs _drugs;
        private readonly Pricing _pricing;
        private readonly Settings _cfg;

        public LeaderTalk(GangLeaders leaders, GangRegistry gangs, Affiliation crew,
                          PlayerState state, Drugs drugs, Pricing pricing, Settings cfg)
        {
            _leaders = leaders;
            _gangs = gangs;
            _crew = crew;
            _state = state;
            _drugs = drugs;
            _pricing = pricing;
            _cfg = cfg;
        }

        public DialogueNode Root(LeaderDef def)
        {
            var gang = _gangs.Get(def.GangId);
            if (gang == null) return null;

            var mine = _crew.IsAffiliated &&
                       string.Equals(_crew.Current.Id, gang.Id, StringComparison.OrdinalIgnoreCase);

            return mine ? MemberRoot(def, gang) : StrangerRoot(def, gang);
        }

        private DialogueNode Node(LeaderDef def, GangDef gang, string line)
        {
            return new DialogueNode(def.Name, line) { SpeakerColour = gang.Colour };
        }

        // ---- before you are in -------------------------------------------------

        private DialogueNode StrangerRoot(LeaderDef def, GangDef gang)
        {
            var node = Node(def, gang, def.Greeting);

            node.Say("Who runs round here?", () => WhoRuns(def, gang),
                     "Ask about the block");

            node.Say("What's the work?", () => TheWork(def, gang),
                     "Ask what they move");

            // Every gang sells to you whether or not they will have you. That is the point of
            // keeping the other six around.
            node.Say("I'm buying.", () => BuyList(def, gang),
                     "Buy weight off him");

            if (gang.Joinable)
            {
                var short_ = _state.Respect < gang.JoinRespect;

                node.SayIf(!_crew.IsAffiliated, "You already run with " +
                           (_crew.IsAffiliated ? _crew.Current.Name : "somebody"),
                           "Put me on.", () => AskToJoin(def, gang),
                           short_ ? "He might not rate you yet"
                                  : "Sign on with " + gang.Name);
            }
            else
            {
                // They will trade with you all day and never take you on.
                node.Say("Put me on.", () => NotTakingAnyone(def, gang),
                         "He ain't taking nobody on");
            }

            node.Leave("Forget it.");
            return node;
        }

        private DialogueNode WhoRuns(LeaderDef def, GangDef gang)
        {
            var rivals = gang.Rivals.Count == 0
                ? "Nobody worth naming."
                : "We got problems with " + JoinNames(gang) + ".";

            var node = Node(def, gang,
                "We do. " + gang.TurfHint + ", all of it. " + rivals +
                " You walk them blocks wearing the wrong thing, that's on you.");

            node.Say("Back up.", () => Root(def));
            node.Leave();
            return node;
        }

        private DialogueNode TheWork(LeaderDef def, GangDef gang)
        {
            var product = gang.Drugs.Count == 0 ? null : _drugs.Get(gang.Drugs[0]);
            var what = product == null ? "product" : product.Name.ToLowerInvariant();

            var node = Node(def, gang,
                "We move " + what + ". You want in, you take a bag, you post up somewhere, " +
                "and you bring back what it's worth. Simple as that. Don't get greedy, " +
                "don't get caught, don't sell on nobody else's block.");

            node.Say("Back up.", () => Root(def));
            node.Leave();
            return node;
        }

        private DialogueNode AskToJoin(LeaderDef def, GangDef gang)
        {
            // Join() says his piece and fronts the bag; a returned string is the refusal.
            var refusal = _leaders.Join(def, _drugs);

            if (refusal != null)
            {
                var no = Node(def, gang, def.Refuse);
                no.Say("I'll be back.", () => null, refusal);
                return no;
            }

            var yes = Node(def, gang, def.Accept);
            yes.Say("I got you.", () => null, "You run with " + gang.Name + " now");
            return yes;
        }

        /// <summary>
        /// A gang that does not recruit, saying so in its own voice. He still sells to you --
        /// business is business -- he just is not making you one of his.
        /// </summary>
        private DialogueNode NotTakingAnyone(LeaderDef def, GangDef gang)
        {
            var node = Node(def, gang,
                "Nah. We don't take people in off the street, and you ain't people. " +
                "You want to buy somethin', that's different. That I'll do all day.");

            node.Say("Fair enough.", () => Root(def));
            node.Leave();
            return node;
        }

        private static string JoinNames(GangDef gang)
        {
            return string.Join(" and ", gang.Rivals.ToArray());
        }

        /// <summary>
        /// Where the weight really comes from.
        ///
        /// He will not tell a stranger and he will not tell somebody who has not moved
        /// anything. Once you have, the port exists for you and the whole catalogue with it --
        /// which is the single step-up in the supply chain, so it is worth making you earn.
        /// </summary>
        private DialogueNode AskSource(LeaderDef def, GangDef gang)
        {
            if (_state.DocksUnlocked)
            {
                var known = Node(def, gang, "I already told you. The port. Go see the man.");
                known.Say("Back up.", () => Root(def));
                known.Leave();
                return known;
            }

            if (_state.GramsSold < _cfg.DocksUnlockGrams)
            {
                var soon = Node(def, gang,
                    "You moved what, a couple of grams? Come back when you're worth telling.");

                soon.Say("Back up.", () => Root(def),
                         _state.GramsSold.ToString("0") + " / " + _cfg.DocksUnlockGrams.ToString("0") + "g moved");
                soon.Leave();
                return soon;
            }

            _state.DocksUnlocked = true;
            _state.AddRespect(15f);
            _state.Touch();

            Notify.Important("~g~The port's open to you.~s~ Go find the dock worker down there.");
            Log.Info("Docks unlocked after " + _state.GramsSold.ToString("0.#") + "g sold.");

            var node = Node(def, gang,
                "Alright. You've earned the answer. It's the boat -- down the port, Elysian. " +
                "Dock boy pulls it off the containers before anybody counts them. Tell him I sent you, " +
                "and don't waste his time.");

            node.Say("I'm on it.", () => null, "The docks are open");
            return node;
        }

        // ---- buying off him ----------------------------------------------------

        /// <summary>
        /// The lots he deals in.
        ///
        /// More than one size because a single 30g lot priced everything out of reach at the
        /// start and made no difference at all later. A tenth of an ounce is what somebody
        /// starting out can actually put their hands on; two ounces is what you come back for.
        /// </summary>
        private static readonly float[] Lots = { 3.5f, 28f, 56f };

        /// <summary>
        /// Buying weight, face to face.
        ///
        /// There used to be a separate corner dealer standing about for every gang doing this
        /// job, which was a second man to find for no reason. The leader IS the connect: you
        /// walk up to him and ask, and every gang will sell to you whether or not they will
        /// have you.
        /// </summary>
        private DialogueNode BuyList(LeaderDef def, GangDef gang)
        {
            var node = Node(def, gang, "How much you want?");

            foreach (var id in gang.Drugs)
            {
                var product = _drugs.Get(id);
                if (product == null) continue;

                var picked = product;

                node.Say(product.Name + ".", () => LotList(def, gang, picked),
                         "$" + _pricing.WholesalePrice(product).ToString("0.##") + " a gram");

                node.WithIcon(Icons.ForDrug(product.Id));
            }

            if (gang.Drugs.Count == 0)
            {
                node.Say("...", () => Root(def), "He ain't got nothing for you");
            }

            node.Say("Not right now.", () => Root(def));
            return node;
        }

        /// <summary>How much of it, once you have said what.</summary>
        private DialogueNode LotList(LeaderDef def, GangDef gang, DrugDef product)
        {
            var node = Node(def, gang, "How much " + product.Name.ToLowerInvariant() + " you want?");

            foreach (var grams in Lots)
            {
                var lot = grams;
                var cost = _pricing.PurchaseCost(product, lot);

                var canPay = Game.Player.Money >= cost;
                var fits = _state.Stash.FreeSpace >= lot - 0.001f;

                var blocked = !canPay ? "You're $" + (cost - Game.Player.Money).ToString("N0") + " short"
                            : !fits ? "You can't carry that much"
                            : "";

                node.SayIf(blocked.Length == 0, blocked,
                           Weight(lot),
                           () => Buy(def, gang, product, lot, cost),
                           "$" + cost.ToString("N0"));

                node.WithIcon(Icons.ForDrug(product.Id));
            }

            node.Say("Something else.", () => BuyList(def, gang));
            return node;
        }

        /// <summary>
        /// Weights as they are actually asked for.
        ///
        /// Nobody buying weight asks for 28 grams, they ask for an ounce. The gram figure is
        /// still there because the stash is measured in grams and the two have to agree.
        /// </summary>
        private static string Weight(float grams)
        {
            if (grams >= 55f) return "Two ounces.  (" + grams.ToString("0") + "g)";
            if (grams >= 27f) return "An ounce.  (" + grams.ToString("0") + "g)";
            return "An eighth.  (" + grams.ToString("0.#") + "g)";
        }

        private DialogueNode Buy(LeaderDef def, GangDef gang, DrugDef product, float lotGrams, int cost)
        {
            var taken = _state.Stash.AddBulk(product.Id, lotGrams);
            if (taken <= 0.005f)
            {
                return Node(def, gang, "You got nowhere to put it. Come back with empty pockets.");
            }

            // Charged for what actually fit, so a part-full pocket is not a part-paid robbery.
            var charged = (int)Math.Round(cost * (taken / lotGrams));
            Game.Player.Money -= charged;

            _state.Touch();
            _crew.CreditPurchase();

            Notify.Ticker("~y~-$" + charged.ToString("N0") + "~s~  " + taken.ToString("0.#") +
                          "g of " + product.Name.ToLowerInvariant());
            Log.Info("Bought " + taken.ToString("0.#") + "g " + product.Id + " off " + def.Name +
                     " for $" + charged + ".");

            var node = Node(def, gang,
                "That's " + taken.ToString("0.#") + " grams. " + product.SplitVerb +
                " it before you try and move it, and don't come back empty handed.");

            node.Say("Anything else.", () => BuyList(def, gang));
            node.Leave("Got it.");
            return node;
        }

        // ---- once you are in ---------------------------------------------------

        private DialogueNode MemberRoot(LeaderDef def, GangDef gang)
        {
            var node = Node(def, gang, def.Already);

            node.Say("Where should I be working?", () => WhereToWork(def, gang),
                     "Ask which blocks are safe");

            node.Say("How am I doing?", () => Standing(def, gang),
                     "Ask how they rate you");

            node.Say("I need a re-up.", () => BuyList(def, gang),
                     "Buy weight off him");

            // The one progression gate in the supply chain, and it belongs to him now that the
            // corner dealers are gone.
            node.Say("Where's it all coming from?", () => AskSource(def, gang),
                     _state.DocksUnlocked ? "You already know" : "Ask about his supply");

            node.SayIf(false, "Coming soon",
                       "Got any work for me?", () => null,
                       "Ask for a job");

            node.Leave("I'm out.");
            return node;
        }

        private DialogueNode WhereToWork(LeaderDef def, GangDef gang)
        {
            var node = Node(def, gang,
                "Ours is " + gang.TurfHint + ". Stay on it and nobody touches you. " +
                "Off it you're on your own, and if you get seen dealing on " +
                JoinNames(gang) + " turf, you'll get stomped. That's not a threat, " +
                "that's just what happens.");

            node.Say("Back up.", () => Root(def));
            node.Leave();
            return node;
        }

        private DialogueNode Standing(LeaderDef def, GangDef gang)
        {
            var standing = _crew.StandingFor(gang.Id);
            var rep = standing == null ? 0f : standing.Rep;

            var verdict =
                rep >= 75f ? "You're solid. People know your name round here."
              : rep >= 40f ? "You're alright. Keep it up."
              : rep >= 10f ? "You're new. Ain't nobody made their mind up about you yet."
              : rep >= 0f ? "Nobody knows you. Go put in some work."
                          : "You've been a problem. Fix that before you ask me for anything.";

            var node = Node(def, gang, verdict);

            node.Say("Back up.", () => Root(def));
            node.Leave();
            return node;
        }
    }
}
