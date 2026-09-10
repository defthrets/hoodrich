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

        /// <summary>
        /// Whether the ceiling above has been read yet.
        ///
        /// IT IS READ ONCE PER SESSION AND THEN NEVER AGAIN, because after the first time this
        /// class touches the police every value GET_MAX_WANTED_LEVEL can return is one we put
        /// there. This used to be "read it on the first hold", which is the same idea with one
        /// case missing: a CAP is also one of ours, and a cap does not go through Hold.
        ///
        /// So tagging a wall in front of a cop capped the ceiling at one, a gang war an hour
        /// later read that one as "what it was before", and put it back to one when the war
        /// ended -- for the rest of the session, silently, with no way up. Guarding the hold
        /// and not the cap is what made it a ratchet instead of a bug you would notice.
        /// </summary>
        private static bool _read;

        /// <summary>
        /// The ceiling as it was before this mod touched it, read at most once.
        ///
        /// Called at the top of both Hold and Cap -- the two doors into changing it -- so
        /// whichever happens first is the one that gets to look.
        /// </summary>
        private static void Remember()
        {
            if (_read) return;
            _read = true;

            try
            {
                _wasMax = Function.Call<int>(Hash.GET_MAX_WANTED_LEVEL);
                if (_wasMax <= 0) _wasMax = 5;
            }
            catch (System.Exception ex)
            {
                Log.Debug("Could not read the wanted ceiling: " + ex.Message);
                _wasMax = 5;
            }
        }

        /// <summary>
        /// Whether anybody is currently holding the police off.
        ///
        /// Asks the BRIDGE first when it is there, because Precinct 88 is then the arbiter and
        /// its own bookings hold it too -- so a Hoodrich system reading this to decide whether
        /// the police are a factor has to see holds it did not place. Reading our own set here
        /// would say "nobody" during somebody else's arrest.
        /// </summary>
        public static bool Held => Bridge.Present ? Bridge.LawIsHeld() : Holders.Count > 0;

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

            // ONE ARBITER, AND IT IS THE OTHER MOD WHEN THE OTHER MOD IS THERE.
            //
            // Precinct 88 has a counted hold of its own, for bookings. Two counted holds that
            // do not know about each other is exactly the bug THIS class was written to fix,
            // one layer up: whichever finishes first hands the police back to the other, so a
            // booking that ends during a gang war brings a helicopter to the war.
            //
            // So when it is installed, this forwards and pushes no natives itself. When it is
            // not, nothing below changes and this stays the arbiter, as it has been.
            // A set, so adding somebody already on it changes nothing and costs nothing.
            // Recorded even when bridged, because ReleaseAll has to know who we are holding
            // in order to let go of them individually at the other end.
            var first = Holders.Count == 0;
            Holders.Add(who);

            if (Bridge.Hold(who.GetType().Name)) return;

            // Before the ceiling below is written over. See Remember -- at most once a
            // session, and a cap may well have got here first.
            Remember();

            if (first) Log.Info("Law: off, held by " + who.GetType().Name + ".");

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

        /// <summary>
        /// Puts a lid on how far it can go, without turning the police off.
        ///
        /// A hold is "none of this is a police matter". A cap is "this IS a police matter, and
        /// it is worth exactly this much" -- a corner shop with a bike outside is one star, and
        /// the game will happily make it three if it sees you ride away from it.
        ///
        /// Through here rather than a raw native call at the call site, because the level to go
        /// back to is remembered in this file and nowhere else. Anything that caps has to be
        /// able to uncap without guessing at five.
        ///
        /// A hold outranks a cap and simply wins: nothing is more capped than off.
        /// </summary>
        /// <summary>
        /// Returns whether the cap actually went on, so a caller does not record one that did not.
        ///
        /// It returned void and stood down silently during a hold -- and Main set its own
        /// "capped" latch either way, so a cap asked for during a gang war was never applied,
        /// never re-applied when the war ended, and never asked for again.
        /// </summary>
        public static bool Cap(int stars)
        {
            if (Bridge.Cap(stars)) return true;
            if (Held) return false;

            // Before we write over it, exactly as Hold does. A cap is very often the first
            // thing in a session to touch the ceiling at all.
            Remember();

            try
            {
                Function.Call(Hash.SET_MAX_WANTED_LEVEL, stars < 0 ? 0 : stars);
                return true;
            }
            catch (System.Exception ex)
            {
                Log.Debug("Could not cap the law: " + ex.Message);
                return false;
            }
        }

        /// <summary>Back to whatever the ceiling was before anybody touched it.</summary>
        public static void Uncap()
        {
            if (Bridge.Uncap()) return;
            if (Held) return;

            try { Function.Call(Hash.SET_MAX_WANTED_LEVEL, _wasMax); }
            catch (System.Exception ex) { Log.Debug("Could not lift the cap: " + ex.Message); }
        }

        public static void Release(object who)
        {
            if (who == null || !Holders.Remove(who)) return;

            if (Bridge.Release(who.GetType().Name)) return;

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
            // Let go at the other end FIRST, one at a time, because that is the only vocabulary
            // the bridge has -- and because pushing our own natives while Precinct 88 still
            // thinks it is holding for a booking would hand the player back to the police in
            // the middle of being arrested.
            if (Bridge.Present)
            {
                foreach (var who in Holders) Bridge.Release(who.GetType().Name);

                Holders.Clear();
                return;
            }

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
