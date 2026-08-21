using System.Collections.Generic;
using GTA;
using GTA.Native;

namespace Hoodrich.Core
{
    /// <summary>
    /// One switch for the police, shared by everything that needs them out of the way.
    ///
    /// Two systems were each turning the wanted system off and back on with no idea the other
    /// existed. A gang war starting while the bike ride was running -- which nothing prevents,
    /// they roll on separate clocks -- meant whichever finished first turned the police back on
    /// for the other, so either a straightener on a basketball court brought a helicopter, or a
    /// raid on your own block did. Both are the exact failure each of them was written to avoid.
    ///
    /// Counted rather than boolean, for the same reason a lock is: the last one out restores it,
    /// not the first. The level to go back to is read from the game at the first hold instead of
    /// being assumed to be five, so an install that has been set to something else keeps it.
    /// </summary>
    internal static class LawHold
    {
        private static readonly HashSet<object> Holders = new HashSet<object>();

        private static int _wasMax = 5;

        /// <summary>Whether anybody is currently holding the police off.</summary>
        public static bool Held => Holders.Count > 0;

        /// <summary>
        /// Takes the police off, and puts them off again every time it is asked.
        ///
        /// Asking twice used to be free in the worst sense: the first line was
        /// `if (Holders.Contains(who)) return;`, so a caller already on the list got nothing
        /// at all. GangWar calls this every single tick of a raid and says in its own comment
        /// that it is re-asserting, because the game resets the max wanted level on a mission
        /// finishing, a cutscene, an area reload -- and it was not re-asserting anything. The
        /// natives were pushed once at the start of the war and whatever happened to them
        /// after that stood. Which is exactly what "we keep getting stars during the raid"
        /// looks like from the street.
        ///
        /// So re-asking now re-applies. The one thing that must NOT happen twice is reading
        /// the level to go back to -- read it again while it is held and it reads zero, and
        /// the police never come back at all.
        /// </summary>
        public static void Hold(object who)
        {
            if (who == null) return;

            var first = Holders.Count == 0;

            // A set, so adding somebody already on it changes nothing and costs nothing.
            Holders.Add(who);

            if (first)
            {
                try
                {
                    _wasMax = Function.Call<int>(Hash.GET_MAX_WANTED_LEVEL);
                    if (_wasMax <= 0) _wasMax = 5;

                    Log.Info("Law: off, held by " + who.GetType().Name + ".");
                }
                catch (System.Exception ex)
                {
                    Log.Debug("Could not read the wanted ceiling: " + ex.Message);
                    _wasMax = 5;
                }
            }

            try
            {
                // Cleared as well as capped. A star already showing when the raid starts, or
                // one the game hands out in the frame before the ceiling takes, would otherwise
                // sit there for the whole fight with the ceiling quietly stopping it going any
                // higher -- suppressed, and still on screen.
                Game.Player.Wanted.SetWantedLevel(0, false);
                Game.Player.Wanted.ApplyWantedLevelChangeNow(false);

                Function.Call(Hash.SET_MAX_WANTED_LEVEL, 0);
                Function.Call(Hash.SET_POLICE_IGNORE_PLAYER, Game.Player.Handle, true);
                Function.Call(Hash.SET_CREATE_RANDOM_COPS, false);
            }
            catch (System.Exception ex)
            {
                Log.Debug("Could not hold the law: " + ex.Message);
            }
        }

        public static void Release(object who)
        {
            if (who == null || !Holders.Remove(who)) return;
            if (Holders.Count > 0) return;

            Restore();
        }

        /// <summary>
        /// Puts it back regardless of who was holding it.
        ///
        /// For teardown only. Leaving the player permanently un-arrestable because a script
        /// unloaded mid-raid is far worse than any amount of litter, so this does not care about
        /// the count.
        /// </summary>
        public static void ReleaseAll()
        {
            Holders.Clear();
            Restore();
        }

        private static void Restore()
        {
            try
            {
                Function.Call(Hash.SET_MAX_WANTED_LEVEL, _wasMax);
                Function.Call(Hash.SET_POLICE_IGNORE_PLAYER, Game.Player.Handle, false);
                Function.Call(Hash.SET_CREATE_RANDOM_COPS, true);

                Log.Info("Law: back on.");
            }
            catch (System.Exception ex)
            {
                Log.Debug("Could not put the law back: " + ex.Message);
            }
        }
    }
}
