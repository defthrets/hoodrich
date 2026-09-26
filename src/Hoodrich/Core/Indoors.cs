using GTA.Math;

namespace Hoodrich.Core
{
    /// <summary>
    /// Where the player really is, for everything that keeps itself near him.
    ///
    /// EVERY DOOR IN THIS MOD IS A TELEPORT. The gambling den, the rented room, Leroy's
    /// basement: the room is somewhere else on the map -- under the sea, across the city --
    /// and the player is put there. Anything that spawns and despawns by distance from him
    /// then sees him vanish: Parkview's eighty people and four hundred props came down the
    /// moment he stepped into the den and went back up, slowly, when he stepped out, which
    /// Michael reported on 2026-09-26 as "things take ages to load in". So a door that moves
    /// him says where he went in, and the range checks measure from there while he is inside.
    ///
    /// Nothing is unloaded by the game for being far from him: what a script made and keeps
    /// is kept, frozen where the collision is not, and picks itself up when he is back.
    /// </summary>
    internal static class Indoors
    {
        /// <summary>Outside the door he went in by, or nought if he is not behind one.</summary>
        public static Vector3 Outside { get; private set; }

        /// <summary>Which door said so.</summary>
        public static string Where { get; private set; } = "";

        public static bool Away => Outside != Vector3.Zero;

        public static void Enter(Vector3 outside, string where)
        {
            Outside = outside;
            Where = where ?? "";
        }

        /// <summary>Only the door that said he went in may say he came out, so two doors never clear each other.</summary>
        public static void Exit(string where)
        {
            if (!Away) return;
            if (!string.IsNullOrEmpty(Where) && Where != where) return;

            Outside = Vector3.Zero;
            Where = "";
        }

        /// <summary>Where distance is measured from: outside the door while he is in, himself otherwise.</summary>
        public static Vector3 From(Vector3 him)
        {
            return Away ? Outside : him;
        }
    }
}
