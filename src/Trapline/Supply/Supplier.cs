using System;
using System.Collections.Generic;

namespace Trapline.Supply
{
    /// <summary>Who you are buying from. Flavour, but it also drives price and rank gating.</summary>
    internal enum SupplierKind
    {
        /// <summary>Dock workers skimming containers. Cheap, low tier, daytime.</summary>
        Docks,

        /// <summary>Mobsters. Expensive, high tier, no questions.</summary>
        Mob,

        /// <summary>Other gangs. Mid price, but they want to know who you run with.</summary>
        Gang,

        /// <summary>Street-level connect. Small weight, always available.</summary>
        Street
    }

    /// <summary>
    /// A supply contact the player can call for a meet.
    ///
    /// Contacts have no fixed map position. Calling one arranges a meet at a spot picked near
    /// the player at runtime, which means the mod ships no hardcoded coordinates that could
    /// land a ped inside a wall or under the map.
    /// </summary>
    internal sealed class SupplierDef
    {
        public string Id = "";
        public string Name = "";
        public string Tag = "";
        public SupplierKind Kind = SupplierKind.Street;

        /// <summary>
        /// Ped models tried in order. The first that is actually present in the game's files
        /// wins, so a wrong or DLC-only model name degrades instead of failing the spawn.
        /// </summary>
        public readonly List<string> Models = new List<string>();

        /// <summary>Products this contact can move.</summary>
        public readonly List<string> Drugs = new List<string>();

        /// <summary>Applied to the wholesale price. Below 1.0 is a better deal.</summary>
        public float PriceMultiplier = 1f;

        /// <summary>Player rank needed before this contact will take the call.</summary>
        public int MinRank;

        /// <summary>Largest single order, in grams of bulk.</summary>
        public float MaxOrderGrams = 100f;

        /// <summary>Hours this contact operates. Wraps midnight when Open &gt; Close.</summary>
        public int OpenHour;
        public int CloseHour = 24;

        /// <summary>Shown on the wheel so the player knows what they are calling.</summary>
        public string Blurb = "";

        public bool IsOpenAt(int hour)
        {
            if (OpenHour == CloseHour) return true;
            return OpenHour < CloseHour
                ? hour >= OpenHour && hour < CloseHour
                : hour >= OpenHour || hour < CloseHour;
        }

        public bool Sells(string drugId)
        {
            foreach (var d in Drugs)
            {
                if (string.Equals(d, drugId, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
