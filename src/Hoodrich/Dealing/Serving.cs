using System.Collections.Generic;
using GTA;

namespace Hoodrich.Dealing
{
    /// <summary>
    /// Who is in the middle of buying something off you, so that whatever owns them lets go
    /// of them for as long as it takes.
    ///
    /// THE CROWD AT A TAKEOVER AND THE PEOPLE AT A PARTY WERE ALREADY PLAUSIBLE CUSTOMERS.
    /// The corner scans everybody on foot near you and none of its tests excluded them, so
    /// one would be picked, told to walk over -- and then, within a second, the thing that
    /// put him on that pavement in the first place would notice he had wandered off his mark
    /// and walk him straight back. He never arrived, the corner timed him out, and it read as
    /// "nobody at a takeover ever buys anything" rather than as two systems each doing their
    /// job correctly.
    ///
    /// So the corner says who it is serving and everybody else leaves that person alone. It
    /// is one set of handles and two lines of skip in each of the systems that hold people to
    /// a spot -- the takeover's ring and the entourages, which is the parties and the corners.
    ///
    /// AND NOTHING HAS TO PUT THEM BACK. Both of those systems already run a pass every tick
    /// whose entire job is walking somebody who has left his mark back to it and starting him
    /// on what he was doing. Suspending that pass is the whole of "he goes and buys something";
    /// resuming it is the whole of "and then he goes back to what he was doing". There is no
    /// state to save, which means there is none to get wrong.
    /// </summary>
    internal static class Serving
    {
        private static readonly HashSet<int> Busy = new HashSet<int>();

        /// <summary>He is being served. Whoever owns him should leave him be.</summary>
        public static void Start(Ped ped)
        {
            if (ped == null || !ped.Exists()) return;

            Busy.Add(ped.Handle);
        }

        /// <summary>Done with him, one way or another. His own system picks him up again.</summary>
        public static void Done(Ped ped)
        {
            if (ped == null) return;

            Busy.Remove(ped.Handle);
        }

        /// <summary>Whether this one is mid-deal.</summary>
        public static bool Is(Ped ped)
        {
            if (Busy.Count == 0 || ped == null || !ped.Exists()) return false;

            return Busy.Contains(ped.Handle);
        }

        /// <summary>
        /// Nobody is being served.
        ///
        /// For teardown and for the corner closing. A handle left in here is somebody his own
        /// system has quietly stopped looking after -- and handles are reused, so it would
        /// eventually be somebody else entirely.
        /// </summary>
        public static void Clear()
        {
            Busy.Clear();
        }
    }
}
