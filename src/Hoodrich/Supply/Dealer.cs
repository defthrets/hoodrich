using System;
using System.Collections.Generic;
using Hoodrich.Economy;

namespace Hoodrich.Supply
{
    /// <summary>Which kind of contact this is, which decides where they stand.</summary>
    internal enum DealerKind
    {
        /// <summary>Posts up on a corner of their gang's turf. Sells only what that gang moves.</summary>
        GangCorner,

        /// <summary>Works the port. Sells anything, in weight, once you know he exists.</summary>
        Docks,

        /// <summary>
        /// Runs their own thing in their own part of town, tied to no gang.
        ///
        /// The gangs only move what the gangs move, so without these the map has whole products
        /// with nowhere to buy them. A supplier belongs to a place rather than to a crew: you
        /// find the coke on the beach and the meth out in the sand because that is where those
        /// trades live, not because somebody's turf says so.
        /// </summary>
        Independent,

        /// <summary>
        /// Runs the crew. Sells nothing -- he is the person you have to find and talk to
        /// before you can run with them at all.
        /// </summary>
        Leader
    }

    /// <summary>
    /// A person you buy from.
    ///
    /// Dealers are real peds standing in the world, not a phone menu. Where they stand is
    /// expressed as a list of GET_NAME_OF_ZONE codes rather than coordinates: the game already
    /// carves the map into neighbourhoods, so "the docks" is a set of zones and "your gang's
    /// corner" is whatever turf your crew holds. Nothing here can drop a ped through the map.
    /// </summary>
    internal sealed class DealerDef
    {
        public string Id = "";
        public string Name = "";
        public string Tag = "";
        public DealerKind Kind = DealerKind.GangCorner;

        /// <summary>The crew this dealer runs with. Empty for the dock worker.</summary>
        public string GangId = "";

        /// <summary>
        /// Ped models tried in order; the first actually present in the install wins, so a
        /// wrong or DLC-only name costs flavour rather than the dealer.
        /// </summary>
        public readonly List<string> Models = new List<string>();

        /// <summary>Products sold. EMPTY means the whole catalogue -- that is the docks.</summary>
        public readonly List<string> Drugs = new List<string>();

        /// <summary>
        /// His first text, in his own voice. Empty falls back to a generic one.
        /// </summary>
        public string OpeningText = "";

        /// <summary>
        /// The CHAR_ dictionary his texts put a face on.
        ///
        /// This lived in Delivery as a switch on the id, which is the wrong home: whose face
        /// this is is a fact about the MAN, not about one journey he happens to be making --
        /// and the moment anything other than a delivery wanted to text you, it needed the
        /// same switch a second time.
        /// </summary>
        public string Portrait = "CHAR_DEFAULT";

        /// <summary>
        /// The three texts a courier sends on a run: answering the call, setting off, and
        /// arriving outside.
        ///
        /// Data rather than code because they are VOICE, and every other line this man says is
        /// already in the same file next to them. They lived in Delivery as a ternary on his
        /// id -- drunk if he was Tao, flat if he was anybody else -- which is fine for two
        /// couriers and means the third one ever added inherits Gerald's personality by
        /// default. These are the neutral versions; the data overrides them per man.
        /// </summary>
        /// <summary>
        /// What he texts back, and there is more than one of each.
        ///
        /// One line per moment meant the same eleven words every time you rang him, which
        /// turns a person into a vending machine with a voice -- the second delivery reads
        /// exactly like the first and by the fourth you have stopped looking at the phone. The
        /// json takes a string or a list of them; a string is just a list of one.
        ///
        /// The singular fields below are the fallback for a dealer that names nothing.
        /// </summary>
        /// <summary>
        /// What he says out loud on a delivery, in his own words.
        ///
        /// FOUR BEATS, because a delivery has four: pulling up, picking the box out of the
        /// boot, putting it down, and going. There were two sets for all of it -- one written
        /// for the port and one for a corner -- so eleven different men shared six lines each
        /// and the whole run read as one courier with a wardrobe.
        ///
        /// Empty falls back to those two, which is right for the two they were written for:
        /// Tao IS the port and Gerald IS the corner.
        /// </summary>
        public readonly List<string> ArrivalLines = new List<string>();
        public readonly List<string> CarryLines = new List<string>();
        public readonly List<string> DropLines = new List<string>();
        public readonly List<string> PartingLines = new List<string>();

        public readonly List<string> CalledLines = new List<string>();
        public readonly List<string> LeavingLines = new List<string>();
        public readonly List<string> OutsideLines = new List<string>();

        public string TextCalled = "on my way. give me a minute";
        public string TextLeaving = "leaving now";
        public string TextOutside = "im outside";

        private int _lastCalled = -1;
        private int _lastLeaving = -1;
        private int _lastOutside = -1;

        private static readonly Random Roll = new Random();

        /// <summary>
        /// One line out of a pool, never the same one twice running.
        ///
        /// Remembering the last index rather than shuffling, because a pool of three that is
        /// allowed to repeat says the same thing twice about a third of the time -- which is
        /// most of the way back to having one line.
        /// </summary>
        private static string Pick(List<string> pool, string fallback, ref int last)
        {
            if (pool == null || pool.Count == 0) return fallback;
            if (pool.Count == 1) return pool[0];

            int i;
            do { i = Roll.Next(pool.Count); } while (i == last);

            last = i;
            return pool[i];
        }

        public string SayCalled() { return Pick(CalledLines, TextCalled, ref _lastCalled); }
        public string SayLeaving() { return Pick(LeavingLines, TextLeaving, ref _lastLeaving); }
        public string SayOutside() { return Pick(OutsideLines, TextOutside, ref _lastOutside); }

        /// <summary>
        /// Roughly what one lot off him is worth, before his own multiplier, and the weight it
        /// is rounded to.
        ///
        /// This is the difference between a plug and a port. Tao moves bricks and says so --
        /// "nothing smaller, dont ask" -- so a lot off him is sixty thousand dollars of
        /// something rounded to the nearest half kilo. Gerald says the opposite in his own
        /// buy line, that he is not the port and not to ask him for bricks, and then offered
        /// twelve kilos of weed for fifty-five thousand dollars anyway.
        /// </summary>
        public float LotValue = 60000f;
        public float LotStep = 500f;

        /// <summary>
        /// How much of a given product he sells in one go.
        ///
        /// THE PRODUCT DECIDES THE SIZE AND THE MAN DECIDES THE SCALE. The size comes off the
        /// drug -- fifty kilos of weed, five of cocaine, ten thousand pills -- and what a given
        /// dealer does is take a fraction of it. LotValue against the port's hundred thousand
        /// is that fraction: Tao is the whole unit, a corner is a twentieth, Gerald is about
        /// four hundredths. So one authored catalogue reads as fifty kilos, two and a half
        /// kilos and a hundred and twenty grams of the same weed, and nobody writes three lists.
        ///
        /// Rounded to his own step so it stays a number a person would say out loud, and never
        /// below that step, because a lot of nothing is not an offer.
        /// </summary>
        public float LotFor(DrugDef drug)
        {
            if (drug == null) return LotStep < 1f ? 1f : LotStep;

            var step = LotStep < 1f ? 1f : LotStep;
            var units = (float)Math.Round(drug.LotGrams * (LotValue / PortLotValue) / step) * step;

            return units < step ? step : units;
        }

        /// <summary>
        /// The port's LotValue, which is the yardstick every other dealer is measured against.
        ///
        /// A SCALE, NOT A BUDGET, and the distinction is the whole history of this field. The
        /// lot used to BE a budget -- his LotValue divided by the price of whatever you asked
        /// for -- which is exactly what made every row on a screen cost the same money for a
        /// different weight.
        /// </summary>
        private const float PortLotValue = 100000f;

        /// <summary>
        /// The least a lot off him can cost, and what the price is rounded to.
        ///
        /// Both were constants belonging to the port -- a fifty thousand dollar floor and
        /// rounding to the nearest five hundred -- and they were applied to everybody. On a man
        /// selling thirty five pills that turned a two hundred and eighty dollar bag into fifty
        /// thousand dollars: the floor is larger than anything he sells, so every line on his
        /// menu quoted the same absurd number.
        ///
        /// THE SAME TRAP A SECOND TIME, and worth writing down. These were then set per dealer
        /// to roughly what one lot cost -- which was safe only while a lot was priced by being
        /// worth about that much. The moment the lot became a fixed WEIGHT and the price began
        /// to follow the product, every real price dropped below the floor and all seven rows
        /// quoted the floor instead. A minimum sized like a typical price is not a minimum, it
        /// is the price.
        ///
        /// So it is a guard and nothing else: well under the cheapest thing on his own list, so
        /// that it only ever catches an edge case nobody would say out loud.
        /// </summary>
        public int PriceFloor = 500;
        public int PriceStep = 500;

        /// <summary>
        /// Whether he walks the parcel in like a man who has had a few.
        ///
        /// One courier is a drunk and it is his whole character. It was applied to whoever
        /// happened to be carrying, so the other one -- who is not drunk, and whose entire
        /// personality is being wound too tight -- staggered up the path as well.
        /// </summary>
        public bool Drunk;

        /// <summary>
        /// Zone codes this dealer stands in. Empty on a gang dealer means "wherever my crew
        /// holds turf", read live from the gang data.
        /// </summary>
        public readonly List<string> Zones = new List<string>();

        /// <summary>
        /// How pure what he sells is, and therefore what happens to it when you take it.
        ///
        /// Full strength means WEIGHT: uncut product that is worth nothing until you have cut
        /// and bagged it yourself, which is what a plug at the docks sells. Anything less means
        /// he has already done that -- what changes hands is street-ready and goes straight into
        /// the bagged pile at the purity he cut it to.
        ///
        /// Which is the actual difference between the two men in this mod. One sells you a
        /// brick and lets you decide how far to stretch it. The other sells you bags off his own
        /// person, already halved, at a price that reflects it -- and you cannot un-cut them.
        /// </summary>
        public float Purity = 1f;

        /// <summary>
        /// What he turns up ON, if it is not a car.
        ///
        /// Empty means the delivery picks its own, which is the sensible default for somebody
        /// driving a load in from the port. Gerald is coming from four streets away with a
        /// bag, and a man who lives on the block arriving in a van reads as a stranger.
        /// </summary>
        public readonly List<string> Rides = new List<string>();

        /// <summary>
        /// What colour his ride is painted, as a GTA paint index. -1 leaves it black.
        ///
        /// Every delivery car in the mod was blacked out on the reasoning that a man driving
        /// product wants to be unremarkable. That is true of a stranger coming in from the
        /// port and wrong about somebody from the block: Gerald turning up in the same anonymous
        /// black saloon as the docks contact makes the two men read as one supplier with two
        /// phone numbers. A colour he chose is a fact about him.
        /// </summary>
        public int RidePaint = -1;

        /// <summary>
        /// What it says on the back of his car.
        ///
        /// The same reasoning as the paint: two men turning up in identical black cars with
        /// identical plates are one supplier with two phone numbers. A plate he chose is a
        /// fact about him, and it is the first thing you read walking up to a parked car.
        /// Empty leaves the fleet plate on it.
        /// </summary>
        public string Plate = "";

        public float PriceMultiplier = 1f;

        /// <summary>
        /// Some nights he has got hold of something that has not been touched.
        ///
        /// THE TRADE-OFF IS THE POINT. A gang corner is cheap because what he is handing you
        /// has already been halved -- that is the deal, and it is a good one right up until you
        /// want to cut it yourself and find there is nothing left to cut. So the corner is
        /// three quarters of the price at two thirds of the strength, and the independent up
        /// the coast is dearer and clean, and neither of them is simply better.
        ///
        /// The uncut night is what stops that being a fixed choice you make once. It is the
        /// same man on the same corner with something off a boat that nobody has stepped on,
        /// and he wants paying for it -- so the cheap option is briefly the expensive one, and
        /// worth it, and gone tomorrow.
        /// </summary>
        public float PurePurity = 1f;

        /// <summary>What he charges on those nights. Above 1 means dearer than the street.</summary>
        public float PureMultiplier = 1.3f;

        /// <summary>Chance, per restock, that tonight is one of them. 0 for a man it never happens to.</summary>
        public float PureChancePercent;

        /// <summary>
        /// RUNTIME, not data. Rolled by DealerManager when he restocks and never read from
        /// json -- it lives here because it is a fact about the man that DealerTalk needs, and
        /// DealerTalk is handed a def and nothing else.
        /// </summary>
        public bool PureTonight;

        /// <summary>
        /// What he says the first time, when he hands over the number.
        ///
        /// THE MOMENT THE WHOLE SEARCH IS FOR. Finding a man on his corner is now what puts him
        /// in your phone, and until this line existed the game never said so -- he was in
        /// Messages afterwards and you were left to notice. A mechanic nobody is told about is
        /// a mechanic nobody uses.
        /// </summary>
        public string NumberLine = "";

        /// <summary>
        /// Being put on the set, in his words.
        ///
        /// The corner dealer is who you ask now. He is on that block every day selling their
        /// product -- if anybody is going to vouch for you it is him, and it was always a
        /// slightly strange errand to go and find a boss in a car park to ask.
        /// </summary>
        public string JoinAsk = "";
        public string JoinAccept = "";
        public string JoinRefuse = "";
        public string JoinAlready = "";

        /// <summary>
        /// RUNTIME. True for the one conversation where you have just met him.
        ///
        /// Set by DealerManager at the moment it writes him down, read once by the greeting and
        /// cleared there -- the same shape as PureTonight, and for the same reason: DealerTalk
        /// is handed a def and nothing else.
        /// </summary>
        public bool JustMet;

        /// <summary>What he is actually charging, and what it is actually worth.</summary>
        public float PriceNow => PureTonight ? PureMultiplier : PriceMultiplier;

        public float PurityNow => PureTonight ? PurePurity : Purity;

        /// <summary>
        /// Spend this much in one go and it comes uncut.
        ///
        /// MEASURED IN MONEY, NOT GRAMS, and that is the only way it works across a catalogue
        /// where a gram of coke is fourteen grams of weed. A hundred grams is a serious order
        /// of one and a pocketful of the other, so a gram threshold would have meant "weight"
        /// arriving at a different point for every product and reading as random.
        ///
        /// What the player is actually choosing between is a bag off his person and a load out
        /// of his boot, and the thing that separates those is HOW MUCH OF IT there is.
        ///
        /// MEASURED IN HIS OWN LOTS -- not in money, and not in grams either. Both of those
        /// have been tried and both say something nobody means. Money says cocaine arrives clean
        /// and the identical weight of marijuana arrives stepped on, because one is dearer. A
        /// flat weight says the reverse the moment lots differ by product: fifty kilos of weed
        /// clears any threshold you care to set and five of cocaine may not, so the cheap thing
        /// is always the pure thing.
        ///
        /// He is not cutting it according to your receipt or to the scales. He breaks up what he
        /// sells SMALL and leaves weight alone, and small is relative to what he normally shifts.
        /// One lot off him is a bag; four is a delivery.
        ///
        /// It is why the corner is worth knowing at all now. Cheap AND cut was strictly worse
        /// than Tao for anybody who could afford Tao; cheap, cut in small amounts and clean in
        /// weight is a different shop rather than a worse one.
        /// </summary>
        public float UncutFromLots = 2f;

        /// <summary>
        /// Where he actually stands, read off the coordinate HUD.
        ///
        /// ZONES WERE ALWAYS AN APPROXIMATION. A dealer with only a zone list is put on
        /// whatever stretch of pavement the game finds first, which is a different stretch
        /// every visit -- so "go and see Poncho" meant driving round Rancho until somebody
        /// green turned up, and you could never learn where he was because there was nothing
        /// to learn.
        ///
        /// A man with an address is a place. It is also the only way the map marker can mean
        /// anything: you cannot pin a wandering spawn.
        ///
        /// Zero means no address and the old behaviour, which is right for Tao -- he has never
        /// stood anywhere, he drives to you.
        /// </summary>
        public float SpotX;
        public float SpotY;
        public float SpotZ;
        public float SpotHeading;

        /// <summary>Whether anybody wrote his address down.</summary>
        public bool HasSpot => Math.Abs(SpotX) > 0.01f || Math.Abs(SpotY) > 0.01f;

        /// <summary>What an order of this size actually arrives at. See UncutFromLots.</summary>
        public float PurityFor(DrugDef drug, float grams)
        {
            var lot = LotFor(drug);
            if (lot < 0.01f) return PurityNow;

            // A hair under, because four lots is four lots and a float multiply need not agree.
            return grams >= lot * UncutFromLots - 0.01f ? 1f : PurityNow;
        }
        public int MinRank;
        public float MaxOrderGrams = 100f;

        public int OpenHour;
        public int CloseHour = 24;

        // ---- what they say -----------------------------------------------------

        public string Greeting = "";
        public string BuyLine = "";

        /// <summary>Reply when the player asks where the product comes from, and it works.</summary>
        public string SourceReply = "";

        /// <summary>Reply when the player asks too early.</summary>
        public string SourceTooSoon = "";

        public string Farewell = "";

        /// <summary>
        /// Ambient voice name, so the ped speaks in character. Applied with
        /// SET_AMBIENT_VOICE_NAME; a wrong name costs the voice, not the ped.
        /// </summary>
        public string Voice = "";

        /// <summary>
        /// Whether this plug is a GANG's own man.
        ///
        /// Answered by whether he belongs to a set, and by nothing else. It used to also
        /// demand Kind == GangCorner, which quietly locked Gerald out of his own mod: he is
        /// Docks-kind because that is how he delivers -- a box to your door rather than a
        /// corner to stand on -- so he was not a "gang dealer" by this test, so the PORT's
        /// lock caught him, so the man on your own block told you that you do not know anybody
        /// at the port. Which is true, and has nothing to do with him.
        ///
        /// The guard that lock is written with says as much three lines above it: "both
        /// entries are Docks-kind because both drive a box to your door -- that is what the
        /// kind means here". The kind is HOW he trades. This is WHO he trades for.
        /// </summary>
        public bool IsGangDealer => !string.IsNullOrEmpty(GangId);

        public bool IsOpenAt(int hour)
        {
            if (OpenHour == CloseHour) return true;
            return OpenHour < CloseHour
                ? hour >= OpenHour && hour < CloseHour
                : hour >= OpenHour || hour < CloseHour;
        }

        public bool Sells(string drugId)
        {
            if (Drugs.Count == 0) return true;
            foreach (var d in Drugs)
            {
                if (string.Equals(d, drugId, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public override string ToString() => Id;
    }
}
