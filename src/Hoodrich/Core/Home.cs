using System;

namespace Hoodrich.Core
{
    /// <summary>
    /// The house's hooks, for the room Parkview rents.
    ///
    /// TWO SCRIPTS, ONE HOUSE. Posted Up's Main owns the closet, the cook screen and the
    /// cupboard; Parkview's Main owns the rented room and the spots in it. They are two
    /// SHVDN scripts in one dll, started in whatever order SHVDN finds them, and neither
    /// holds the other -- which is why the room's closet and table said "open it on the
    /// phone for now" for a week. This is the shelf they leave things on for each other:
    /// Main puts the screens here, Parkview puts where the player is stood, and every
    /// question answers "no" until the other side has arrived.
    ///
    /// THE SAME CLOSET, THE SAME COUNTER, THE SAME CUPBOARD. The room is not a second house
    /// with a second stash to keep in step; it is another door onto the one the mod has.
    /// What you leave at Denise's is what you find at Parkview, because there is one pile of
    /// product in this game and two places to stand next to it.
    /// </summary>
    internal static class Home
    {
        /// <summary>Set by Main: open the closet on the player, turned to this heading first.</summary>
        public static Action<float> OpenWardrobe;

        /// <summary>Set by Main: open the cook screen as the table.</summary>
        public static Action OpenTable;

        /// <summary>Set by Main: a screen is up or a batch is running, so the room keeps its prompts down.</summary>
        public static Func<bool> Busy;

        /// <summary>Set by Parkview: the player is inside a room he rents.</summary>
        public static Func<bool> InRoom;

        /// <summary>Set by Parkview: the player is stood at the room's table.</summary>
        public static Func<bool> AtTable;

        public static bool IsBusy
        {
            get { try { return Busy != null && Busy(); } catch { return false; } }
        }

        public static bool IsInRoom
        {
            get { try { return InRoom != null && InRoom(); } catch { return false; } }
        }

        public static bool IsAtTable
        {
            get { try { return AtTable != null && AtTable(); } catch { return false; } }
        }

        /// <summary>Main's half, taken back down with Main.</summary>
        public static void UnwireHouse()
        {
            OpenWardrobe = null;
            OpenTable = null;
            Busy = null;
        }

        /// <summary>Parkview's half, taken back down with Parkview.</summary>
        public static void UnwireRoom()
        {
            InRoom = null;
            AtTable = null;
        }
    }
}
