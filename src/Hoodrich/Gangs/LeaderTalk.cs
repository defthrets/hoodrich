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

            // Stretch, and only Stretch. He is the one who put you on in the first place, so
            // he is the one you can go back to with nothing in your pockets.
            if (FrontsWork(def) && _state.MissionsDone.Count > 0)
            {
                if (_state.FrontedWorkDone)
                {
                    node.Say("Moved all your work.", () => PayForWork(def, gang),
                             "Get paid for his package");
                }
                else if (_state.HasFrontedWork)
                {
                    node.Say("Still got your work.", () => WorkProgress(def, gang),
                             "You're still holding his package");
                }
                else
                {
                    node.Say("I'm broke. Front me something.", () => OfferWork(def, gang),
                             "Sell a package for him");
                }
            }

            node.SayIf(false, "Coming soon",
                       "Got any work for me?", () => null,
                       "Ask for a job");

            node.Leave("I'm out.");
            return node;
        }

        // ---- his package -------------------------------------------------------

        /// <summary>How much of his he hands over. Small: this is bus fare, not a re-up.</summary>
        private const float FrontGrams = 20f;

        /// <summary>What he pays when it is all gone.</summary>
        private const int FrontPayMin = 250;
        private const int FrontPayMax = 500;

        /// <summary>
        /// Whether this man will front you work.
        ///
        /// Stretch, by name. It is a Stretch thing to do -- he is the one who put you on, he
        /// takes his cut off the top, and the arrangement is exactly as generous as he is.
        /// </summary>
        private static bool FrontsWork(LeaderDef def)
        {
            return def != null &&
                   string.Equals(def.Name, "Stretch", StringComparison.OrdinalIgnoreCase);
        }

        private DialogueNode OfferWork(LeaderDef def, GangDef gang)
        {
            var product = gang.Drugs.Count == 0 ? null : _drugs.Get(gang.Drugs[0]);

            if (product == null)
            {
                var nothing = Node(def, gang, "Ain't got nothing spare right now.");
                nothing.Say("Back up.", () => Root(def));
                nothing.Leave();
                return nothing;
            }

            if (_state.Stash.FreeSpace < FrontGrams)
            {
                var full = Node(def, gang,
                    "Nigga, your bag is already full. What you asking me for?");

                full.Say("Back up.", () => Root(def));
                full.Leave();
                return full;
            }

            var node = Node(def, gang,
                "Look at you. Aight, check it -- " + FrontGrams.ToString("0") + " of my " +
                product.Name.ToLowerInvariant() + ". You go stand on a corner and move all of " +
                "it, then you come back and I break you off a lil something. That's it. " +
                "You smoke it, you sell it and run off with my money, we gon have a whole " +
                "different conversation.");

            node.Say("Give it here.", () => TakeWork(def, gang, product), "Take his package");
            node.Say("Nah.", () => Root(def));

            return node;
        }

        private DialogueNode TakeWork(LeaderDef def, GangDef gang, DrugDef product)
        {
            var took = _state.Stash.AddBulk(product.Id, FrontGrams);

            if (took <= 0f)
            {
                var no = Node(def, gang, "You got nowhere to put it. Sort yourself out first.");
                no.Say("Back up.", () => Root(def));
                no.Leave();
                return no;
            }

            _state.FrontedDrug = product.Id;
            _state.FrontedGrams = took;
            _state.FrontedAtGrams = _state.GramsSold;
            _state.Touch();

            Notify.Important("~g~" + took.ToString("0") + "g of " + product.Name.ToLowerInvariant() +
                             "~s~ off Stretch. Move all of it and go back to him.");

            if (Social != null) Social.On(Hoodrich.Social.SocialEvent.FrontedWork, def.Name);

            var node = Node(def, gang,
                "Go on then. And don't be standing round here with it neither, that's my corner.");

            node.Leave("Say less.");
            return node;
        }

        private DialogueNode WorkProgress(LeaderDef def, GangDef gang)
        {
            var left = Math.Max(0f, _state.FrontedGrams - _state.FrontedMoved);

            var node = Node(def, gang,
                "You still holding " + left.ToString("0") + " of it. Come back when it's gone.");

            // There has to be a way out of this. Product can be lost -- robbed, arrested, wiped
            // by a reset -- and without this the promise stays open forever, which locks the one
            // option in the mod that exists for players with nothing, behind having lost
            // something. It costs, because it should.
            node.Say("I lost it.", () => LostWork(def, gang), "Write it off, and wear it");

            node.Say("Back up.", () => Root(def));
            node.Leave();
            return node;
        }

        private DialogueNode LostWork(LeaderDef def, GangDef gang)
        {
            _state.ClearFronted();
            _state.Touch();

            _crew.AddRep(-12f, "for losing his work");

            var node = Node(def, gang,
                "You lost it. Course you did. That's on your tab and everybody's gonna hear " +
                "about it. Get out my face and come back when you ready to work it off.");

            node.Leave("Aight.");
            return node;
        }

        private DialogueNode PayForWork(LeaderDef def, GangDef gang)
        {
            var pay = FrontPayMin + _rng.Next(FrontPayMax - FrontPayMin);

            _state.ClearFronted();
            _state.Touch();

            Game.Player.Money += pay;
            _crew.AddRep(6f, "for moving his work");

            if (Social != null) Social.On(Hoodrich.Social.SocialEvent.FrontedPaid, def.Name, pay);

            var node = Node(def, gang,
                "There you go. " + pay.ToString("N0") + ". Don't spend it all on nothing stupid. " +
                "Come see me when you need another one.");

            node.Leave("Appreciate it.");
            return node;
        }

        private readonly Random _rng = new Random();

        /// <summary>Set by Main, so the block hears when he puts you back on your feet.</summary>
        public Hoodrich.Social.SocialFeed Social;

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
