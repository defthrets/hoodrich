using System;
using System.Collections.Generic;
using System.Drawing;

namespace Hoodrich.Gangs
{
    /// <summary>How a gang feels about the player, or about another gang.</summary>
    internal enum Stance
    {
        Hostile,
        Wary,
        Neutral,
        Friendly
    }

    /// <summary>
    /// A gang definition.
    ///
    /// Membership is identified by RELATIONSHIP GROUP, not by ped model. The game already
    /// populates each neighbourhood with the right gang, and a relationship-group check
    /// catches every ped it spawns -- including models we have never heard of and any added
    /// by other mods -- where a hardcoded model list would silently miss them.
    /// </summary>
    internal sealed class GangDef
    {
        /// <summary>Stable key used in saves and turf data.</summary>
        public string Id = "";

        public string Name = "";

        /// <summary>Short tag for the wheel; keep to 4 characters.</summary>
        public string Tag = "";

        /// <summary>
        /// Vanilla relationship group name, e.g. AMBIENT_GANG_FAMILY. Verified against
        /// DOES_RELATIONSHIP_GROUP_EXIST at load and created if the game does not have it.
        /// </summary>
        public string RelationshipGroup = "";

        /// <summary>Resolved hash of <see cref="RelationshipGroup"/>. Filled in at load.</summary>
        public int GroupHash;

        /// <summary>Colour used for blips, wheel tinting and the gang panel.</summary>
        public Color Colour = Color.Gray;

        /// <summary>Game blip colour index, for turf blips.</summary>
        public int BlipColour = 0;

        /// <summary>Products this gang moves. Affiliating unlocks better prices on these.</summary>
        public readonly List<string> Drugs = new List<string>();

        /// <summary>
        /// Models used ONLY when the mod has to spawn members itself, which is turf wars and
        /// nothing else. Everywhere else membership is read from the relationship group, so an
        /// ambient ped in a model we never listed still counts as one of theirs.
        /// </summary>
        public readonly List<string> MemberModels = new List<string>();

        /// <summary>Gang ids this gang is at war with.</summary>
        public readonly List<string> Rivals = new List<string>();

        /// <summary>
        /// Zone codes this gang claims, as returned by GET_NAME_OF_ZONE (e.g. "DAVIS").
        /// Editable in gangs.json; use the wheel's Turf > "Log this zone" action in game to
        /// discover the exact code for wherever you are standing.
        /// </summary>
        public readonly List<string> Turf = new List<string>();

        /// <summary>Human-readable turf description for the gang panel.</summary>
        public string TurfHint = "";

        /// <summary>Player respect needed before this gang will take you on.</summary>
        public float JoinRespect;

        /// <summary>
        /// Whether this gang will take you on at all.
        ///
        /// Only the Families for now. The others are fully in the world -- they hold their
        /// blocks, they sell to you, their leader talks to you -- you simply cannot sign on
        /// with them, so the story has one home rather than seven interchangeable ones.
        /// </summary>
        public bool Joinable;

        /// <summary>
        /// Whether this gang feuds with that one.
        ///
        /// Gang-versus-gang only. It used to decide who wanted the PLAYER dead as well, which
        /// is standing's job now -- see Affiliation.Beefing. This is the city's own politics,
        /// and it is what the bickering on the feed is drawn from.
        /// </summary>
        public bool IsRivalOf(string gangId)
        {
            if (string.IsNullOrEmpty(gangId)) return false;
            foreach (var r in Rivals)
            {
                if (string.Equals(r, gangId, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public override string ToString() => Id;
    }

    /// <summary>Per-gang progression the player accrues. One of these per gang the player has dealt with.</summary>
    internal sealed class GangStanding
    {
        public string GangId = "";

        /// <summary>Standing with this specific gang. Separate from global respect.</summary>
        public float Rep;

        /// <summary>Rivals of this gang killed while affiliated with it.</summary>
        public int Kills;

        /// <summary>Cash earned dealing while affiliated with this gang.</summary>
        public long MoneyEarned;

        /// <summary>Deals closed while affiliated with this gang.</summary>
        public int Deals;

        /// <summary>In-game milliseconds spent affiliated. Informational only.</summary>
        public long TimeAffiliatedMs;

        /// <summary>
        /// Bodies of THIS set that you have dropped.
        ///
        /// Not the same number as Kills above, and deliberately kept apart from it. Kills is
        /// how many rivals you dropped while running with a set -- it belongs to the set you
        /// were wearing at the time. This belongs to the set that got hit. Every gang in the
        /// city has one, including the one you run with, because you can shoot your own.
        ///
        /// Counted whether or not there was beef. Rep only moves when it is a body for the
        /// block, but the tally is a tally -- a man you dropped for no reason is still a man
        /// they lost, and the whole point of showing this is that it reads like a history.
        /// </summary>
        public int TheirDead;

        /// <summary>Times this set has come to one of your spots looking for you.</summary>
        public int Attacks;

        /// <summary>Posts this set has put on the feed.</summary>
        public int Tweets;

    }
}
