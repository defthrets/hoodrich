using System.Collections.Generic;
using GTA;

namespace Hoodrich.Gangs
{
    /// <summary>
    /// People another system is already looking after, so the general rules leave them be.
    ///
    /// THE SAME SHAPE AS Dealing.Serving AND FOR THE SAME REASON. This mod has several systems
    /// that scan the street and act on anybody wearing one of its gang groups, and several more
    /// that own a specific handful of people and task them deliberately. Those two kinds meet
    /// badly: the general rule sees somebody it recognises and re-tasks him out of whatever the
    /// system that made him just told him to do.
    ///
    /// The homies are the case this was built for. Everybody of ours runs from the police --
    /// which is right for a man stood on a corner and wrong for the two riding with you, whose
    /// whole job is not leaving. Homies minds them here and applies its own narrower rule.
    ///
    /// A set of handles and nothing else. Handles are reused by the game, so anything put in
    /// here has to be taken out when its owner is done with it -- see Forget and Clear.
    /// </summary>
    internal static class Minded
    {
        private static readonly HashSet<int> Held = new HashSet<int>();

        /// <summary>Somebody else is looking after him.</summary>
        public static void Mind(Ped ped)
        {
            if (ped == null || !ped.Exists()) return;

            Held.Add(ped.Handle);
        }

        /// <summary>Done with him. The general rules have him back.</summary>
        public static void Forget(Ped ped)
        {
            if (ped == null) return;

            Held.Remove(ped.Handle);
        }

        /// <summary>Whether somebody else has him.</summary>
        public static bool Is(Ped ped)
        {
            if (Held.Count == 0 || ped == null || !ped.Exists()) return false;

            return Held.Contains(ped.Handle);
        }

        /// <summary>Nobody is minded. For teardown, and for a system letting its whole lot go.</summary>
        public static void Clear()
        {
            Held.Clear();
        }
    }
}
