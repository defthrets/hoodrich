using System;
using GTA.Math;
using GTA.Native;

namespace Hoodrich.Core
{
    /// <summary>
    /// What the men standing guard round the map are holding.
    ///
    /// Six guns, and which one a man gets is decided by where he stands rather than by a
    /// dice roll -- so the same man on the same corner has the same gun every session, the
    /// way he keeps the same face and the same idle, while the block as a whole is a mix
    /// rather than a rack of identical rifles.
    /// </summary>
    internal static class Arms
    {
        public static readonly string[] Guard =
        {
            "WEAPON_COMPACTRIFLE",
            "WEAPON_MACHINEPISTOL",
            "WEAPON_CARBINERIFLE_MK2",
            "WEAPON_SMG",
            "WEAPON_PISTOL",
            "WEAPON_ASSAULTRIFLE"
        };

        /// <summary>The gun for whoever stands here.</summary>
        public static string GuardAt(Vector3 at)
        {
            // Half-metre cells, so a mark nudged by a few centimetres in the spooner keeps its gun.
            var x = (int)Math.Round(at.X * 2f);
            var y = (int)Math.Round(at.Y * 2f);
            var z = (int)Math.Round(at.Z);
            var seed = unchecked(x * 73856093 ^ y * 19349663 ^ z * 83492791);

            return Guard[Math.Abs(seed % Guard.Length)];
        }

        /// <summary>The same, as the hash the natives want.</summary>
        public static uint GuardHashAt(Vector3 at)
        {
            return Function.Call<uint>(Hash.GET_HASH_KEY, GuardAt(at));
        }
    }
}
