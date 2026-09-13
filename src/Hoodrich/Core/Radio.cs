using System;
using GTA;
using GTA.Native;

namespace Hoodrich.Core
{
    /// <summary>
    /// What is playing on the block.
    ///
    /// THE STATION IS FOUND, NOT NAMED. A radio station's internal name is not the name on the
    /// dial -- Blonded Los Santos 97.8 is RADIO_21_DLC_XM17 -- and writing that constant into
    /// two files would be a guess that fails silently: SET_VEH_RADIO_STATION with a name this
    /// build has never heard of does nothing at all, so the car plays whatever it was already
    /// playing and nobody can tell the difference between "wrong name" and "mod did not run".
    ///
    /// So the list is read out of the game. GET_NUMBER_OF_RADIO_STATIONS and
    /// GET_RADIO_STATION_NAME give the stations this install actually has, the wanted ones are
    /// looked for in order, and the first that is really there wins. An install without the
    /// DLC gets the fallback and a line in the log saying so, rather than silence.
    ///
    /// Resolved once. The list cannot change while the game is running.
    /// </summary>
    internal static class Radio
    {
        /// <summary>
        /// Blonded Los Santos 97.8, and what to play if this build has not got it.
        ///
        /// The alternates are the same station under names other builds have used for it. The
        /// last entry is the old hip-hop station -- which is what the block played before any
        /// of this and is a perfectly good answer for somebody on a copy without the DLC.
        /// </summary>
        private static readonly string[] Wanted =
        {
            "RADIO_21_DLC_XM17", "RADIO_21_DLC_XM17_RADIO", "RADIO_21_BLONDED",
            "RADIO_09_HIPHOP_OLD"
        };

        private static string _found;

        /// <summary>Whether the install's station list has been written down. See Find.</summary>
        private static bool _listed;

        /// <summary>West Coast Classics, which is what a lowrider is for.</summary>
        private static readonly string[] WestCoastWanted = { "RADIO_09_HIPHOP_OLD", "RADIO_09_HIPHOP_OLD_RADIO" };
        private static string _westCoast;

        /// <summary>Radio Los Santos, for the donks.</summary>
        private static readonly string[] LosSantosWanted = { "RADIO_03_HIPHOP_NEW", "RADIO_03_HIPHOP_NEW_RADIO" };
        private static string _losSantos;

        /// <summary>
        /// West Coast Talk Radio, for a parked car with nobody at it.
        ///
        /// Talk rather than music is the whole point of it: a car left on all day with a
        /// station playing is somebody's car with the key in, and talk is what that sounds
        /// like. The fallback is the classics, which is at least the right car.
        ///
        /// IT WAS ASKING FOR THE WRONG STATION AND GETTING IT. West Coast Talk Radio is
        /// RADIO_05_TALK_01. RADIO_11_TALK_02 -- which is what this asked for -- is BLAINE
        /// COUNTY RADIO, the one out of Sandy Shores, and it is a real station that really
        /// exists, so the lookup matched, the log said it had found what it wanted, and what
        /// came out of Lamar's yard all day was hillbilly talk radio. A name that is wrong
        /// and VALID is worse than one that is wrong and missing: nothing anywhere reports
        /// it. Blaine County stays on the list one place down, because it is at least talk.
        /// </summary>
        private static readonly string[] TalkWanted =
        {
            "RADIO_05_TALK_01", "RADIO_05_TALK_01_RADIO",
            "RADIO_11_TALK_02", "RADIO_09_HIPHOP_OLD"
        };
        private static string _talk;

        public static string Talk
        {
            get
            {
                if (_talk != null) return _talk;

                _talk = Find(TalkWanted);

                // A LOOKUP THAT FOUND NOTHING IS NOT AN ANSWER TO KEEP. See Find.
                return _talk ?? TalkWanted[TalkWanted.Length - 1];
            }
        }

        public static string LosSantos
        {
            get
            {
                if (_losSantos != null) return _losSantos;

                _losSantos = Find(LosSantosWanted);

                // A LOOKUP THAT FOUND NOTHING IS NOT AN ANSWER TO KEEP. See Find.
                return _losSantos ?? LosSantosWanted[LosSantosWanted.Length - 1];
            }
        }

        public static string WestCoast
        {
            get
            {
                if (_westCoast != null) return _westCoast;

                _westCoast = Find(WestCoastWanted);

                // A LOOKUP THAT FOUND NOTHING IS NOT AN ANSWER TO KEEP. See Find.
                return _westCoast ?? WestCoastWanted[WestCoastWanted.Length - 1];
            }
        }

        /// <summary>The station the block is on.</summary>
        public static string Blonded
        {
            get
            {
                if (_found != null) return _found;

                _found = Find(Wanted);

                // A LOOKUP THAT FOUND NOTHING IS NOT AN ANSWER TO KEEP. See Find.
                return _found ?? Wanted[Wanted.Length - 1];
            }
        }

        /// <summary>
        /// The first of these this install really has, or NULL.
        ///
        /// NULL RATHER THAN THE FALLBACK, and that is the whole of the second bug in here.
        /// It used to hand back the last of the wanted list whenever the enumeration came up
        /// empty -- and the callers CACHE what this returns for the life of the session. The
        /// station list is not populated the instant a script loads, so a mod that asked
        /// during its constructor could get a count of nought, cache West Coast Classics as
        /// the answer to "what is the talk station", and play music in that yard from then
        /// until the game was restarted. Returning null leaves the caller's cache empty, so
        /// the next ask tries again, and the caller still has the fallback to play in the
        /// meantime.
        /// </summary>
        private static string Find(string[] wanted)
        {
            try
            {
                // UNLOCKED rather than all of them, and that is the right list: a station
                // the player cannot tune to is a station the block cannot be playing either.
                var count = Function.Call<int>(Hash.GET_NUM_UNLOCKED_RADIO_STATIONS);

                // Read once into a set of names rather than an n-squared walk. There are about
                // thirty of them, so this is not an optimisation, it is just the shape that
                // says what it is doing.
                var have = new System.Collections.Generic.List<string>();

                for (var i = 0; i < count; i++)
                {
                    var name = Function.Call<string>(Hash.GET_RADIO_STATION_NAME, i);
                    if (!string.IsNullOrEmpty(name)) have.Add(name);
                }

                // ONCE A SESSION, THE WHOLE LIST. Every failure this thing has is a name
                // that is not on it, and until now the only way to find that out was to guess
                // again. Printed at INFO because it is four lines a session and it is the
                // answer to every radio question anybody will ever ask about this mod.
                if (!_listed && have.Count > 0)
                {
                    _listed = true;
                    Log.Info("Radio: " + count + " station(s) on this build -- " +
                             string.Join(", ", have.ToArray()));
                }

                foreach (var want in wanted)
                {
                    foreach (var name in have)
                    {
                        if (!string.Equals(name, want, StringComparison.OrdinalIgnoreCase)) continue;

                        Log.Info("Radio: wanted " + wanted[0] + ", playing " + name + ".");
                        return name;
                    }
                }

                Log.Info("Radio: none of the wanted stations are in this build (" + count +
                         " available); falling back to " + wanted[wanted.Length - 1] + ".");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not read the radio list: " + ex.Message);
            }

            // Nothing matched, and nothing is what that is worth saying. The caller plays
            // the last of its wanted list in the meantime and asks again next time.
            return null;
        }
    }
}
