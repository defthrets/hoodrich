using System;
using System.Collections.Generic;

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
        /// The least a lot off him can cost, and what the price is rounded to.
        ///
        /// Both were constants belonging to the port -- a fifty thousand dollar floor and
        /// rounding to the nearest five hundred -- and they were applied to everybody. On a man
        /// selling thirty five pills that turned a two hundred and eighty dollar bag into fifty
        /// thousand dollars: the floor is larger than anything he sells, so every line on his
        /// menu quoted the same absurd number.
        /// </summary>
        public int PriceFloor = 50000;
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

        /// <summary>What he is actually charging, and what it is actually worth.</summary>
        public float PriceNow => PureTonight ? PureMultiplier : PriceMultiplier;

        public float PurityNow => PureTonight ? PurePurity : Purity;
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
