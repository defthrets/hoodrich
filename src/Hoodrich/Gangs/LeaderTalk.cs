using System;
using System.Collections.Generic;
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

            // The one man who will explain the whole thing again, on demand, forever.
            //
            // Everything he says here is said once at the start, when there is a lot of it and
            // none of it has happened to you yet -- so half of it does not stick, and a mod
            // whose answer to that is "read the welcome screen you already closed" is a mod
            // that expects you to take notes. He is the tutorial voice; being able to ask him
            // twice is the least he can do.
            if (FrontsWork(def))
            {
                node.Say("Run it by me again.", () => Refresher(def, gang),
                         "Have him explain how all of it works");
                node.WithIcon(Icons.FromFile("reply.png"));
            }

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
                if (FrontsWork(def) && (!_crew.IsAffiliated || (mine && AfterJoining())))
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
                        // The NUMBERS, on the row itself.
                        //
                        // "You are still holding his" is a state, not a progress bar, and it
                        // reads identically at one gram in and at nineteen -- so somebody five
                        // bars short of clearing a package and somebody who has just taken one
                        // saw the same sentence, and both of them reasonably concluded nothing
                        // was tracking.
                        node.Say("About that package.", () => WorkProgress(def, gang),
                                 Math.Max(0f, _state.FrontedGrams - _state.FrontedMoved)
                                     .ToString("0") + " of " +
                                 _state.FrontedGrams.ToString("0") + " still to move");
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

                // And once you have actually moved some.
                //
                // You could walk up on the first evening having met nobody and sold nothing,
                // ask to be put on, and he said yes -- which makes the package he fronts you
                // decoration rather than a test, and is the opposite of the whole shape of the
                // opening. He gives you something, he watches what you do with it, THEN he
                // decides.
                //
                // Counted in grams sold rather than a new flag: it is already tracked, it
                // survives a save, and it is the same number the package is measured in. Move
                // his twenty and the door opens.
                var proved = !FrontsWork(def) || _state.GramsSold >= FrontGrams;

                var no = _crew.IsAffiliated
                    ? "You already run with " + _crew.Current.Name
                    : owed ? "Move his work first"
                    : !proved ? "Move his work first -- " + _state.GramsSold.ToString("0") +
                                "/" + FrontGrams.ToString("0") + "g"
                    : short_ ? "He might not rate you yet" : "";

                node.SayIf(!_crew.IsAffiliated && !owed && proved, no,
                           "Put me on.", () => AskToJoin(def, gang),
                           "Sign on with " + gang.Name);

                node.WithIcon(owed || short_ || !proved ? Icons.Locked : Icons.Tick);
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

        /// <summary>
        /// The whole business, explained again, in his voice and by its actual parts.
        ///
        /// A hub rather than one long speech: the panel wraps by width and does not honour
        /// line breaks, so a five-paragraph answer is a wall. Each part is a page, each page
        /// names the thing you actually press, and every one of them comes back here -- so
        /// somebody who only wanted to know about cutting gets that and gets out.
        /// </summary>
        private DialogueNode Refresher(LeaderDef def, GangDef gang)
        {
            var node = Node(def, gang,
                "Man, again? Aight, aight. Which part you forget.");

            node.Say("What are we doing here?", () => TwoPackages(def, gang),
                     "The two packages, and where they lead");
            node.WithIcon(Icons.FromFile("rank.png"));

            node.Say("How do I get weight?", () => HowWeight(def, gang),
                     "Where product comes from");
            node.WithIcon(Icons.Money);

            node.Say("What do I do with it?", () => HowCut(def, gang),
                     "Cutting it, and why");
            node.WithIcon(Icons.FromFile("scales.png"));

            node.Say("How do I sell it?", () => HowSell(def, gang),
                     "Posting up on a corner");
            node.WithIcon(Icons.FromFile("deal.png"));

            node.Say("Where do I keep it?", () => HowStash(def, gang),
                     "Pockets, the house, and deliveries");
            node.WithIcon(Icons.Stash);

            node.Say("What gets me caught?", () => HowHeat(def, gang),
                     "Police, and other people's blocks");
            node.WithIcon(Icons.Warning);

            node.Say("I got it.", () => Root(def));
            node.Leave();
            return node;
        }

        private DialogueNode HowWeight(LeaderDef def, GangDef gang)
        {
            var node = Node(def, gang,
                "Three ways. I front you a bag when you broke -- that's me takin' a risk on " +
                "you, and you bring me back what it's worth. Or you buy weight off me proper, " +
                "off your phone, Contacts. Or once you movin' real numbers you text the man at " +
                "the port and he sell you BRICKS, which is a whole different conversation and a " +
                "whole different pile of money.");

            node.Say("What else?", () => Refresher(def, gang));
            node.Leave();
            return node;
        }

        private DialogueNode HowCut(LeaderDef def, GangDef gang)
        {
            var node = Node(def, gang,
                "Weight ain't product. You can't stand on no corner with a brick, dawg. You " +
                "take it to the kitchen at the house and you step on it -- and that's where " +
                "the money actually is, 'cause a hundred grams cut to half strength is two " +
                "hundred grams to sell. Step on it too hard though and people hand it back, " +
                "and a refusal ain't one lost sale. The block remembers.");

            node.Say("What else?", () => Refresher(def, gang));
            node.Leave();
            return node;
        }

        private DialogueNode HowSell(LeaderDef def, GangDef gang)
        {
            var node = Node(def, gang,
                "Phone, Dealing, post up. You ain't pickin' customers -- you pickin' a SPOT, " +
                "and the spot decide everything. Busy pavement move product fast and get you " +
                "clocked fast. Dead alley don't do neither. Stand there long enough and " +
                "somebody call it in, so you take your money and you walk before it get to " +
                "that.");

            node.Say("What else?", () => Refresher(def, gang));
            node.Leave();
            return node;
        }

        private DialogueNode HowStash(LeaderDef def, GangDef gang)
        {
            var node = Node(def, gang,
                "What's on you is what you can sell and what you can lose. What's at the house " +
                "is safe and it's the only place a delivery can land -- text a plug while you " +
                "stood at the spot and it come to you there. Move it between the two out your " +
                "Inventory. And don't be walkin' round holdin' everything you own.");

            node.Say("What else?", () => Refresher(def, gang));
            node.Leave();
            return node;
        }

        private DialogueNode HowHeat(LeaderDef def, GangDef gang)
        {
            var node = Node(def, gang,
                "Two different problems. Police is heat -- stand on one corner too long and " +
                "they come and search you, and if you holdin' they take it. Other people's " +
                "blocks is worse, 'cause they ain't gonna search you. Look at the notice when " +
                "you cross into somewhere: it tell you whose it is by the colour before you " +
                "even read it.");

            node.Say("What else?", () => Refresher(def, gang));
            node.Leave();
            return node;
        }

        private DialogueNode TheWork(LeaderDef def, GangDef gang)
        {
            var product = gang.Drugs.Count == 0 ? null : _drugs.Get(gang.Drugs[0]);
            var what = product == null ? "product" : product.Name.ToLowerInvariant();

            // He is not briefing you. He is answering a question he has been asked a hundred
            // times by people who did not last the month, and the answer is short because most
            // of it is things not to do.
            var node = Node(def, gang,
                what + ". That's it, that's the whole business, ain't no second page. You get " +
                "a bag, you go stand somewhere, you bring me back what it's worth. And listen " +
                "-- don't smoke it, don't be frontin' it to your homies, and don't be out " +
                "there on somebody else corner tryna look like a big man. Dudes done got shot " +
                "over less than a corner, dawg.");

            node.Say("How the bread work?", () => TheBread(def, gang),
                     "Ask how you actually make money");
            node.WithIcon(Icons.Cash);

            node.Say("And where this go?", () => TheComeUp(def, gang),
                     "Ask how you move up");
            node.WithIcon(Icons.FromFile("rank.png"));

            node.Say("What if it go wrong?", () => TheRules(def, gang),
                     "Ask what happens then");
            node.WithIcon(Icons.Warning);

            node.Say("Back up.", () => Root(def));
            node.Leave();
            return node;
        }

        /// <summary>
        /// How dealing actually works, said by the only man who would bother telling you.
        ///
        /// This is the mod's tutorial and it has to be, because nothing else explains posting
        /// up or what a name is worth -- but a tutorial that reads like a tutorial is a
        /// tooltip with a face on it. So it is one man explaining a job to somebody who is
        /// about to go and do it badly, which is the same information in the only shape he
        /// would ever say it.
        /// </summary>
        private DialogueNode TheBread(LeaderDef def, GangDef gang)
        {
            var node = Node(def, gang,
                "Aight, listen. You take the pack and you go post up somewhere people already " +
                "walk past. A corner, a lot, outside a store, I don't care -- long as it's " +
                "busy and it ain't nobody else's. And you don't chase nobody, that's for " +
                "amateurs. You stand there and they come to YOU. Serve 'em, pocket it, keep " +
                "it movin'. Do that all day and you got somethin'.");

            node.Say("What about cuttin' it?", () => TheCut(def, gang),
                     "Ask about stretching the product");
            node.WithIcon(Icons.FromFile("cut_50.png"));

            node.Say("Aight.", () => TheWork(def, gang));
            node.Leave();
            return node;
        }

        /// <summary>The one real decision in the whole economy, put as a warning.</summary>
        private DialogueNode TheCut(LeaderDef def, GangDef gang)
        {
            var node = Node(def, gang,
                "Course you can step on it. Cut it, stretch it, twenty turn into thirty and " +
                "you just made a extra ten out of nothin'. Everybody do it, I done it. But " +
                "they can TASTE it, dawg. Fiend ain't stupid, he broke -- that's different. He " +
                "get home with some weak pack and next week he walk PAST your corner to " +
                "somebody else's. That's how you lose a spot without nobody shootin' at you. " +
                "So that's your whole decision out here: how greedy you is, against how good " +
                "your name is. Get that wrong enough times and you ain't got one.");

            node.Say("I hear you.", () => TheWork(def, gang));
            node.Leave();
            return node;
        }

        /// <summary>
        /// The ladder, said out loud once, in the order it actually happens.
        ///
        /// He does not sell the ending. He mentions the boat and then tells you it is a long
        /// way off, which is true and is also exactly how somebody fifteen years into this
        /// would say it to somebody who turned up yesterday.
        /// </summary>
        private DialogueNode TheComeUp(LeaderDef def, GangDef gang)
        {
            var node = Node(def, gang,
                "Long as you movin' MY pack, you a errand boy. No disrespect -- everybody " +
                "start there, I started there. Move it, bring my bread back, and then we can " +
                "talk about you bein' one of us for real. After that it's yours: somewhere to " +
                "keep your work, people to call when you run dry instead of standin' in my " +
                "yard, and when you moved enough that I ain't gotta worry about you no more, " +
                "I'll put you on to where it actually come from. And I don't mean no corner " +
                "boy. I mean the boat. But that's a long way from where you standin' right " +
                "now, so do the first thing first.");

            node.Say("Say less.", () => TheWork(def, gang));
            node.Leave();
            return node;
        }

        /// <summary>
        /// What happens when it goes wrong, which is the half nobody asks about.
        ///
        /// He does not threaten you, and that is the point -- a man who has to say what he
        /// will do to you is a man nobody tells stories about. He explains that it is your
        /// problem, calmly, which is worse.
        /// </summary>
        private DialogueNode TheRules(LeaderDef def, GangDef gang)
        {
            var node = Node(def, gang,
                "Man. Aight. It go wrong, it went wrong on YOU. Laws take it off you, that's " +
                "your tab. Somebody take it off you, that's your tab AND everybody gon know " +
                "somebody took somethin off you, which is worse. And I ain't comin to find " +
                "you neither, I'm too old for all that. I'm just gon stop pickin up. Out here " +
                "that's the same thing.");

            node.Say("Aight.", () => Root(def));
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

            // Gated on HIS packages, not on a lifetime total.
            //
            // It used to want fifty grams sold ever, which is a different thing from anything
            // he asked you to do -- you could clear both of his fronts, be told he had
            // something for you, walk over, and be sent away for not having moved enough of
            // somebody else's product. Two packages taken and two packages cleared is the
            // whole test, and it is a test he actually set.
            if (_state.FrontsDone < 2)
            {
                var soon = Node(def, gang,
                    _state.HasFrontedWork
                        ? "You still holding mine. Finish that first, then we talk about where " +
                          "it comes from."
                        : "Nah. Take somethin' off me and move it first. Twice. Then I'll tell " +
                          "you where I get it.");

                soon.Say("Back up.", () => Root(def), PackageProgress());
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
            // The trial rows above own the finished-package conversation whenever they are
            // showing. This block is the same man saying the same thing a second time, and two
            // rows for one event is how the count got lost in the first place.
            var trialHasIt = FrontsWork(def) && (!_crew.IsAffiliated || AfterJoining());

            if (FrontsWork(def) && _state.MissionsDone.Count > 0 && !trialHasIt)
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
        /// <summary>
        /// Signed on with him, but he has not put you on to the port yet.
        ///
        /// The window the second package lives in. One definition, because two rows read it and
        /// they must agree about which of them owns the conversation.
        /// </summary>
        private bool AfterJoining()
        {
            return _crew.IsAffiliated && !_state.DocksUnlocked
                   && _state.PortRunStage == PortRun.StageNone;
        }

        private static bool FrontsWork(LeaderDef def)
        {
            return def != null &&
                   string.Equals(def.Name, "Gerald", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// What he will put in your hand, and in what order he offers it.
        ///
        /// Bars first and only bars. A man who has just met you does not ask what you would
        /// like to sell -- he hands you the thing that is easiest to move, needs no
        /// explaining, and that he can afford to lose if you turn out to be nobody. Once
        /// you have taken one out and brought the money back, he lets you pick.
        /// </summary>
        private static readonly string[] FirstFront = { "xanax" };
        private static readonly string[] LaterFronts = { "weed", "ecstasy", "xanax" };

        private DialogueNode OfferWork(LeaderDef def, GangDef gang)
        {
            if (_state.Stash.FreeSpace < FrontGrams)
            {
                var full = Node(def, gang,
                    "Man, your bag already full. What you askin me for?");

                full.Say("Back up.", () => Root(def));
                full.Leave();
                return full;
            }

            var first = _state.FrontsDone <= 0;
            var ids = first ? FirstFront : LaterFronts;

            // Anything the catalogue does not have simply is not offered, so a trimmed
            // drugs.json cannot leave him standing there with an empty list.
            var stock = new List<DrugDef>();

            foreach (var id in ids)
            {
                var d = _drugs.Get(id);
                if (d != null) stock.Add(d);
            }

            if (stock.Count == 0)
            {
                var nothing = Node(def, gang, "Ain't got nothin spare right now.");
                nothing.Say("Back up.", () => Root(def));
                nothing.Leave();
                return nothing;
            }

            if (first)
            {
                var bars = stock[0];

                var one = Node(def, gang,
                    "Look at you. Broke, standin' in my yard askin' for somethin'. Aight -- " +
                    bars.Amount(FrontGrams) + ", already bagged, you ain't gotta do nothin' to " +
                    "'em but stand somewhere. Don't ask me for nothin' else, this is what you " +
                    "get, 'cause bars sell theyself and I ain't gotta teach you nothin'. Move " +
                    "all of it, come back, I break you off a lil somethin'. You eat 'em or you " +
                    "run off with my money, we gon have a whole different conversation.");

                // The WHOLE arc, said once, before it starts.
                //
                // Two packages is the gate on everything that comes after -- the set, the
                // port, the man who sells by the brick -- and it was never stated anywhere.
                // Somebody who has moved fifteen of twenty bars and been told "take somethin'
                // off me and move it, twice" cannot tell whether that is a thing they are
                // partway through or a thing they have not started, because nobody ever told
                // them there were two of them or where the second one leads.
                one.Say("How many times we doin' this?", () => TwoPackages(def, gang),
                        "Ask where this is going");
                one.WithIcon(Icons.FromFile("rank.png"));

                one.Say("Give it here.", () => TakeWork(def, gang, bars), "Take his bars");
                one.WithIcon(Icons.ForDrug(bars.Id));

                one.Say("Nah.", () => Root(def));
                return one;
            }

            var node = Node(def, gang,
                "Aight, you been out there once and you came back, so you get to pick this " +
                "time. Same deal -- " + FrontGrams.ToString("0") + " of whatever you take, " +
                "bagged and ready same as before, all of it moved, then you see me.");

            foreach (var d in stock)
            {
                var pick = d;

                // Amount() and Singular, not "g" and "a gram".
                //
                // Xanax is counted in bars and oxy in pills, so the old line offered the
                // player "20g of Alprazolam ... about $40 a gram out there" for a product the
                // entire rest of the mod calls twenty bars at forty dollars a bar. Amount()
                // exists for exactly this and says so in its own summary; this node was the
                // one place that went round it.
                node.Say(pick.Name, () => TakeWork(def, gang, pick),
                         pick.Amount(FrontGrams) + "  ·  about $" +
                         pick.BasePrice.ToString("0") + " a " + pick.Singular + " out there");

                node.WithIcon(Icons.ForDrug(pick.Id));
            }

            node.Say("Not right now.", () => Root(def));
            return node;
        }

        /// <summary>
        /// Two packages, and what the second one is actually for.
        ///
        /// Reachable both before the first bag and from the refresher afterwards, because it
        /// is the one thing about him a player needs to be holding in their head and the one
        /// thing the mod never said out loud.
        /// </summary>
        private DialogueNode TwoPackages(LeaderDef def, GangDef gang)
        {
            var node = Node(def, gang,
                "Twice. That's it, that's the whole thing. You take a bag off me, you move " +
                "ALL of it -- not most, all -- and you bring yourself back here. Then I give " +
                "you a second one and you do it again.");

            node.Say("And then what?", () => TwoPackagesWhy(def, gang));
            node.Say("Aight.", () => Root(def));
            node.Leave();
            return node;
        }

        private DialogueNode TwoPackagesWhy(LeaderDef def, GangDef gang)
        {
            var node = Node(def, gang,
                "Then you ain't a stranger no more and I put you on to where I get mine. " +
                "That's the port, dawg, and that's bricks -- proper weight, proper money, " +
                "the kind you gotta take home and cut yourself. Everything after that is " +
                "yours. Two bags is just me findin' out if you gon' come back.");

            node.Say("I'll come back.", () => Root(def));
            node.Leave();
            return node;
        }

        private DialogueNode TakeWork(LeaderDef def, GangDef gang, DrugDef product)
        {
            // PACKAGED, not weight. It is already bagged and it is ready to go.
            //
            // He was handing over raw weight, which cannot be sold -- so the first thing the
            // mod asked a broke player to do with a favour was drive to a kitchen and learn
            // the cutting screen before a single thing had happened to them. That is the
            // wrong first lesson and it is not what fronting somebody a bag means: a man
            // putting you on does not hand you a brick and wish you luck, he hands you
            // something you can stand on a corner with tonight.
            //
            // BOTH his packages come this way, not just the first. Weight, cutting and purity
            // are still the whole economy -- they arrive the moment you BUY rather than are
            // given, which is the honest place for them: what a man fronts you is a favour and
            // it is ready to go; what you buy is raw and the work is yours.
            //
            // Fronted product is the same strength he sells, not a favour in purer product.
            var took = _state.Stash.AddPackaged(product.Id, FrontGrams, StreetPurity(gang));

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

        /// <summary>
        /// How far through his two packages you actually are, in one line.
        ///
        /// "0 of 2 packages cleared" was true and useless: it is the ONLY number on that row,
        /// and it does not move for the entire time you are working a package -- which is all
        /// of the time you would be looking at it. Somebody halfway through the first bag and
        /// somebody who has not started see the same line, so the one thing it is there to
        /// tell you is the one thing it does not.
        ///
        /// The count still leads, because that is what the gate is measured in. What is on
        /// THIS package follows it, so the row moves as you sell.
        /// </summary>
        private string PackageProgress()
        {
            var done = _state.FrontsDone + " of 2 cleared";

            if (!_state.HasFrontedWork) return done;

            var moved = Math.Min(_state.FrontedMoved, _state.FrontedGrams);

            return done + "  ·  " + moved.ToString("0") + " of " +
                   _state.FrontedGrams.ToString("0") + " moved on this one";
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
            Settle(def, 0);
            return Cleared(def, gang);
        }

        /// <summary>
        /// What he says once the ledger is clear -- and only that. No bookkeeping.
        ///
        /// Split off so the paid route can end up here too without settling a second time.
        /// </summary>
        private DialogueNode Cleared(LeaderDef def, GangDef gang)
        {
            // The second package is a different conversation from the first, and offering to
            // sign somebody up who signed up an hour ago is the sort of thing that makes a
            // whole cast feel like a menu. First one gets you in; second one gets you the
            // number of the man he buys from.
            if (_state.FrontsDone >= 2)
            {
                var twice = Node(def, gang,
                    "Twice now. Took it, moved it, brought it back, didn't eat none of it and " +
                    "didn't get got. Aight -- you been askin' where I get mine. I'm done " +
                    "pretendin' I didn't hear you.");

                twice.Say("So where?", () => AskSource(def, gang), "He'll tell you now");
                twice.WithIcon(Icons.Tick);

                twice.Say("Another time.", () => Root(def));
                twice.Leave();
                return twice;
            }

            // Already signed on, so there is nothing to offer him but the next bag -- and he
            // says how many are left, because that is the only number this whole arc turns on
            // and the man who set the test should be the one keeping the count out loud.
            if (_crew.IsAffiliated)
            {
                var one = Node(def, gang,
                    "That's one. Take ONE more off me, same as that, move all of it and bring " +
                    "yourself back -- then I'll tell you where it all comes from.");

                one.Say("Front me the next one.", () => OfferWork(def, gang),
                        "Take his second package");
                one.WithIcon(Icons.ForDrug(gang.Drugs.Count > 0 ? gang.Drugs[0] : ""));

                one.Say("Later.", () => Root(def), PackageProgress());
                one.Leave();
                return one;
            }

            var node = Node(def, gang,
                "Aight. You took it, you moved it, you came back. That's three things most " +
                "people don't do. So we can talk about you bein' one of us now -- and then " +
                "you're takin' ONE more bag off me, same as that one, and after that I tell " +
                "you where it all comes from.");

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

        /// <summary>
        /// Handing his package back, and everything that follows from it. ONE place.
        ///
        /// It used to be two, and they disagreed. Gerald offered "It's all gone" (which counted
        /// toward his two packages) and "Moved all your work" (which paid you and did not),
        /// both on the same man, for the same finished package, at the same time -- so which
        /// row you happened to press decided whether the thing you had just spent an hour doing
        /// had happened at all.
        ///
        /// Pressing the one with money on it, which is the one anybody presses, cleared the
        /// ledger without crediting it. Gerald then said "0 of 2 cleared" to somebody who had
        /// just cleared one, and because the front was settled there was no row left to say so
        /// -- the package was gone, the count had not moved, and there was no way back to it.
        ///
        /// So the two rows are one settlement now. Moving his work is moving his work; it
        /// counts, it pays, and it does both no matter how you tell him about it.
        /// </summary>
        private void Settle(LeaderDef def, int pay)
        {
            _state.ClearFronted();

            // The one that decides whether he ever asks you what you want to carry.
            _state.FrontsDone++;

            if (pay > 0) Game.Player.Money += pay;

            _crew.AddRep(SquaredRep, "for moving " + def.Name + "'s work");

            _state.Touch();

            if (pay > 0 && Social != null)
            {
                Social.On(Hoodrich.Social.SocialEvent.FrontedPaid, def.Name, pay);
            }
        }

        private DialogueNode PayForWork(LeaderDef def, GangDef gang)
        {
            var pay = FrontPayMin + _rng.Next(FrontPayMax - FrontPayMin);

            Settle(def, pay);

            // And it goes on to say the same thing the other row says, because it IS the other
            // row -- the money is just handed over on the way past.
            var node = Node(def, gang,
                "There you go. " + pay.ToString("N0") + ". Don't spend it all on nothing stupid.");

            node.Say("What now?", () => Cleared(def, gang), PackageProgress());
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
