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
        /// Zone codes this dealer stands in. Empty on a gang dealer means "wherever my crew
        /// holds turf", read live from the gang data.
        /// </summary>
        public readonly List<string> Zones = new List<string>();

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

        /// <summary>What a leader says when he takes you on.</summary>
        public string JoinAccept = "";

        /// <summary>What a leader says when he will not.</summary>
        public string JoinRefuse = "";

        /// <summary>
        /// Ambient voice name, so the ped speaks in character. Applied with
        /// SET_AMBIENT_VOICE_NAME; a wrong name costs the voice, not the ped.
        /// </summary>
        public string Voice = "";

        public bool IsGangDealer => Kind == DealerKind.GangCorner && !string.IsNullOrEmpty(GangId);

        public bool IsLeader => Kind == DealerKind.Leader && !string.IsNullOrEmpty(GangId);

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
