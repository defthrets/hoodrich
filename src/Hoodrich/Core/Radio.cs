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

        /// <summary>West Coast Classics, which is what a lowrider is for.</summary>
        private static readonly string[] WestCoastWanted = { "RADIO_09_HIPHOP_OLD", "RADIO_09_HIPHOP_OLD_RADIO" };
        private static string _westCoast;

        public static string WestCoast
        {
            get
            {
                if (_westCoast != null) return _westCoast;

                _westCoast = Find(WestCoastWanted);
                return _westCoast;
            }
        }

        /// <summary>The station the block is on.</summary>
        public static string Blonded
        {
            get
            {
                if (_found != null) return _found;

                _found = Find(Wanted);
                return _found;
            }
        }

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

                foreach (var want in wanted)
                {
                    foreach (var name in have)
                    {
                        if (!string.Equals(name, want, StringComparison.OrdinalIgnoreCase)) continue;

                        Log.Info("Radio: the block is on " + name + ".");
                        return name;
                    }
                }

                Log.Info("Radio: none of the wanted stations are in this build (" + count +
                         " available); leaving it to the game.");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not read the radio list: " + ex.Message);
            }

            // Nothing matched. The last of the wanted list is the old default and is safe to
            // ask for even if the enumeration failed -- at worst it does nothing, which is
            // exactly where this started.
            return wanted[wanted.Length - 1];
        }
    }
}
