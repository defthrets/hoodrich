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
        /// couriers and means the third one ever added inherits Stretch's personality by
        /// default. These are the neutral versions; the data overrides them per man.
        /// </summary>
        public string TextCalled = "on my way. give me a minute";
        public string TextLeaving = "leaving now";
        public string TextOutside = "im outside";

        /// <summary>
        /// Roughly what one lot off him is worth, before his own multiplier, and the weight it
        /// is rounded to.
        ///
        /// This is the difference between a plug and a port. Tao moves bricks and says so --
        /// "nothing smaller, dont ask" -- so a lot off him is sixty thousand dollars of
        /// something rounded to the nearest half kilo. Stretch says the opposite in his own
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
        /// What he turns up ON, if it is not a car.
        ///
        /// Empty means the delivery picks its own, which is the sensible default for somebody
        /// driving a load in from the port. Stretch is coming from four streets away with a
        /// bag, and a man who lives on the block arriving in a van reads as a stranger.
        /// </summary>
        public readonly List<string> Rides = new List<string>();

        public float PriceMultiplier = 1f;
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

        public bool IsGangDealer => Kind == DealerKind.GangCorner && !string.IsNullOrEmpty(GangId);

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
