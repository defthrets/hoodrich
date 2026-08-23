using System;
using GTA;
using Hoodrich.Core;
using Hoodrich.Economy;
using Hoodrich.Missions;
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
        /// <summary>
        /// Set by Main. Where a leader's product strength comes from.
        ///
        /// Not a number typed in here. Stretch on his corner and Stretch's runner are the same
        /// man, and two menus disagreeing about what he sells is two answers to one question --
        /// so the leader is asked to look up his own gang's dealer and sell what that dealer
        /// sells. Null-safe: without one, a corner falls back to StreetDefault.
        /// </summary>
        public Supply.DealerManager Dealers;

        /// <summary>
        /// What a corner sells at when nothing else has said otherwise.
        ///
        /// Not pure. Nothing bought off a man stood on a street is, and a leader who has not
        /// been given a dealer should still not be a better plug than the port.
        /// </summary>
        private const float StreetDefault = 0.75f;

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

            // Icons on every row, the way the wheel and the rack have them. A list of
            // plain sentences is a menu; the same list with the shape of each answer next to it
            // is a thing you can read at a glance without reading it.
            node.Say("Who runs round here?", () => WhoRuns(def, gang),
                     "Ask about the block");
            node.WithIcon(Icons.Mask);

            node.Say("What's the work?", () => TheWork(def, gang),
                     "Ask what they move");
            node.WithIcon(Icons.ForDrug(gang.Drugs.Count > 0 ? gang.Drugs[0] : ""));

            // The sets that will never have you sell to anybody. The one that WOULD have you
            // does not sell to strangers -- he fronts them work and watches what they do with
            // it, which is the entire opening of this mod and is also simply how it goes: a man
            // does not take your money the first time he meets you, he gives you something and
            // sees whether you come back.
            var mine = _crew.IsAffiliated && _crew.Current.Id == gang.Id;

            if (!gang.Joinable || mine)
            {
                node.Say("I'm buying.", () => BuyList(def, gang),
                         "Buy weight off him");
                node.WithIcon(Icons.Money);
            }

            if (gang.Joinable)
            {
                var settled = !_state.HasFrontedWork;
                var done = _state.FrontedWorkDone;

                // Three states, one after the other, and only one of them is ever on screen:
                // nothing yet, holding his, or holding nothing and owed a conversation.
                //
                // It runs BEFORE you are one of his and again afterwards, and the two are the
                // same conversation for a reason: the first package is how he decides whether
                // to have you, and the second is how he decides whether to put you on to the
                // people he buys from. He does not have a different way of testing somebody.
                var afterJoining = _crew.IsAffiliated && mine && !_state.DocksUnlocked
                                   && _state.PortRunStage == PortRun.StageNone;

                if (FrontsWork(def) && (!_crew.IsAffiliated || afterJoining))
                {
                    if (settled)
                    {
                        node.Say("Put me on somethin'.", () => OfferWork(def, gang),
                                 _crew.IsAffiliated
                                     ? "Move a package for him and he'll put you on"
                                     : "Take a package off him and move it");

                        node.WithIcon(Icons.ForDrug(gang.Drugs.Count > 0 ? gang.Drugs[0] : ""));
                    }
                    else if (!done)
                    {
                        node.Say("About that package.", () => WorkProgress(def, gang),
                                 "You are still holding his");
                        node.WithIcon(Icons.Locked);
                    }
                    else
                    {
                        node.Say("It's all gone.", () => Squared(def, gang),
                                 "Square up with him");
                        node.WithIcon(Icons.Tick);
                    }
                }

                var short_ = _state.Respect < gang.JoinRespect;

                // And the door only opens once his product is somebody else's problem.
                var owed = FrontsWork(def) && _state.HasFrontedWork;

                var no = _crew.IsAffiliated
                    ? "You already run with " + _crew.Current.Name
                    : owed ? "Move his work first"
                    : short_ ? "He might not rate you yet" : "";

                node.SayIf(!_crew.IsAffiliated && !owed, no,
                           "Put me on.", () => AskToJoin(def, gang),
                           "Sign on with " + gang.Name);

                node.WithIcon(owed || short_ ? Icons.Locked : Icons.Tick);
            }
            else
            {
                // They will trade with you all day and never take you on.
                node.Say("Put me on.", () => NotTakingAnyone(def, gang),
                         "He ain't taking nobody on");
                node.WithIcon(Icons.Locked);
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
        /// anything. Once you have, he does not simply say a word and hand you a phone number:
        /// he sends you down there to be looked at, and he sends you with an errand, because
        /// that is the only introduction anybody at the port would accept.
        ///
        /// So this question no longer unlocks the docks. It starts a drive, and the drive
        /// unlocks the docks -- see PortRun.
        /// </summary>
        private DialogueNode AskSource(LeaderDef def, GangDef gang)
        {
            // Already been, already delivered, and the number is in the phone.
            if (_state.DocksUnlocked && _state.PortRunStage == PortRun.StageNone)
            {
                var known = Node(def, gang, "I already told you. The port. Go see the man.");
                known.Say("Back up.", () => Root(def));
                known.Leave();
                return known;
            }

            // Sent, and still out there somewhere.
            if (_state.PortRunStage == PortRun.StageFetch)
            {
                var going = Node(def, gang,
                    "You still standing here? Elysian Island. Dock boy. Go.");

                going.Say("On my way.", () => null, "Get down to the port");
                going.Leave();
                return going;
            }

            if (_state.PortRunStage == PortRun.StageDeliver)
            {
                var owed = Node(def, gang,
                    "You got it? Good. Don't bring it to me on no corner, dawg -- the yard. " +
                    "You know the one. I'll be stood in it.");

                owed.Say("I'm going.", () => null, "Take it to the yard");
                owed.Leave();
                return owed;
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

            if (SendToThePort == null || !SendToThePort())
            {
                var stuck = Node(def, gang, "Ask me again in a minute.");
                stuck.Leave();
                return stuck;
            }

            _state.AddRespect(5f);
            _state.Touch();

            var node = Node(def, gang,
                "Alright. You earned the answer, so here go the answer: it's a boat. Elysian " +
                "Island, down the port. Man down there pulls it off the containers before " +
                "anybody counts 'em. Go see him, tell him I sent you -- he'll put somethin' in " +
                "your hands. Bring that straight back to the yard and don't open it on the way.");

            node.Say("Say less.", () => null, "Drive to the port");
            node.WithIcon(Icons.ForDrug(gang.Drugs.Count > 0 ? gang.Drugs[0] : ""));
            return node;
        }

        /// <summary>
        /// Set by Main. Starts the run, and says whether it actually started.
        ///
        /// A callback rather than a reference to the mission, because this file builds
        /// sentences and should not know how a blip gets on a map.
        /// </summary>
        public Func<bool> SendToThePort;

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
                           Weight(product, lot),
                           () => Buy(def, gang, product, lot, cost),
                           "$" + cost.ToString("N0"));

                node.WithIcon(Icons.ForDrug(product.Id));
                node.WithMark(Stash.Mark(StreetPurity(gang)));
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
        private static string Weight(DrugDef product, float grams)
        {
            // Pills are counted, so there is no ounce of them and never was.
            if (product != null && product.Counted) return product.Amount(grams) + ".";

            if (grams >= 55f) return "Two ounces.  (" + grams.ToString("0") + "g)";
            if (grams >= 27f) return "An ounce.  (" + grams.ToString("0") + "g)";
            return "An eighth.  (" + grams.ToString("0.#") + "g)";
        }

        /// <summary>How strong what this man sells actually is.</summary>
        private float StreetPurity(GangDef gang)
        {
            if (Dealers == null || gang == null || string.IsNullOrEmpty(gang.Id)) return StreetDefault;

            var his = Dealers.ForGang(gang.Id);
            return his == null ? StreetDefault : his.Purity;
        }

        private DialogueNode Buy(LeaderDef def, GangDef gang, DrugDef product, float lotGrams, int cost)
        {
            var strength = StreetPurity(gang);
            var taken = _state.Stash.AddBulk(product.Id, lotGrams, strength);
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
                          "g of " + product.Name.ToLowerInvariant() +
                          (strength < 0.999f ? "  ~y~" + Stash.Percent(strength) + "%" : ""));
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

            // Icons down the whole list, the way the wheel and the rack have them.
            node.Say("Where should I be working?", () => WhereToWork(def, gang),
                     "Ask which blocks are safe");
            node.WithIcon(Icons.Mask);

            node.Say("How am I doing?", () => Standing(def, gang),
                     "Ask how they rate you");
            node.WithIcon(Icons.Tick);

            // No weight over the counter, ever. Not to a stranger and not to one of his own.
            //
            // He does not stand on a corner in Chamberlain with a kilo in a bag waiting for
            // somebody to ask -- he drives it to a door, which is the whole reason he has a
            // phone number and the whole reason the delivery exists. In person he has exactly
            // two things: a package to move, and an opinion about how you are doing.

            // The one progression gate in the supply chain, and it belongs to him now that the
            // corner dealers are gone.
            node.Say("Where's it all coming from?", () => AskSource(def, gang),
                     _state.PortRunStage == PortRun.StageFetch ? "He's sent you to the port"
                     : _state.PortRunStage == PortRun.StageDeliver ? "He wants his package"
                     : _state.DocksUnlocked ? "You already know"
                     : "Ask about his supply");

            node.WithIcon(_state.PortRunStage != PortRun.StageNone ? Icons.Warning
                          : _state.DocksUnlocked ? Icons.Tick : Icons.Locked);

            // Stretch, and only Stretch. He is the one who put you on in the first place, so
            // he is the one you can go back to with nothing in your pockets.
            if (FrontsWork(def) && _state.MissionsDone.Count > 0)
            {
                if (_state.FrontedWorkDone)
                {
                    node.Say("Moved all your work.", () => PayForWork(def, gang),
                             "Get paid for his package");
                    node.WithIcon(Icons.Cash);
                }
                else if (_state.HasFrontedWork)
                {
                    node.Say("Still got your work.", () => WorkProgress(def, gang),
                             "You're still holding his package");
                    node.WithIcon(Icons.Stash);
                }
                else
                {
                    node.Say("I'm broke. Front me something.", () => OfferWork(def, gang),
                             "Sell a package for him");
                    node.WithIcon(Icons.Money);
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
        /// Gerald, by name. It is a Gerald thing to do -- he is the one who put you on, he
        /// takes his cut off the top, and the arrangement is exactly as generous as he is.
        ///
        /// By NAME rather than by gang, deliberately. Every set has a leader and only one of
        /// them fronts you work; if a second gang's leader should ever start doing it, that is
        /// a decision somebody makes here rather than something that happens by itself.
        /// </summary>
        private static bool FrontsWork(LeaderDef def)
        {
            return def != null &&
                   string.Equals(def.Name, "Gerald", StringComparison.OrdinalIgnoreCase);
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
            // Fronted weight is the same weight he sells, not a favour in purer product.
            var took = _state.Stash.AddBulk(product.Id, FrontGrams, StreetPurity(gang));

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
                             "~s~ off " + def.Name + ". Move all of it and go back to him.");

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

        /// <summary>
        /// His product is gone and he knows it. The moment the door opens.
        ///
        /// Nothing changes hands here -- you already have the money from selling it, which is
        /// the point of fronting rather than lending. What you get is the reputation and the
        /// offer, and the front is cleared so the ledger is empty before you sign anything.
        /// </summary>
        private DialogueNode Squared(LeaderDef def, GangDef gang)
        {
            _state.ClearFronted();

            _crew.AddRep(SquaredRep, "for moving " + def.Name + "'s work");

            _state.Touch();

            var node = Node(def, gang,
                "Aight. You took it, you moved it, you came back. That's three things most " +
                "people don't do. So we can talk about you bein' one of us now.");

            node.Say("Put me on.", () => AskToJoin(def, gang), "Sign on with " + gang.Name);
            node.WithIcon(Icons.Tick);

            node.Say("Not yet.", () => Root(def), "Think about it");

            node.Leave();
            return node;
        }

        /// <summary>What squaring up is worth. Enough on its own to be asked in.</summary>
        private const float SquaredRep = 25f;

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
