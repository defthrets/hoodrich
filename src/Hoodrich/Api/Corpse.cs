using System;

namespace Hoodrich.Api
{
    /// <summary>
    /// What another mod is allowed to know about the bodies on the pavement.
    ///
    /// ONE QUESTION: has this one been gone through yet. It exists because two mods now want
    /// the same corpse and the same key. This one searches it -- pockets, phone, whatever he
    /// was carrying -- and Five0 Patrol drags it somewhere out of sight so the killing is never
    /// connected to you. Both are held on the context key over a body, so with neither side
    /// knowing about the other you get two prompts stacked on one man and a coin toss about
    /// which one you meant.
    ///
    /// THE ORDER IS THE ANSWER, not a hotkey. You search him and THEN you move him, every
    /// time, because moving him first means walking back to wherever you put him. So the drag
    /// waits until this says yes, and until then there is one prompt on screen.
    ///
    /// SAME SHAPE AS Api/Block AND Api/Drugs, deliberately: one pattern on this machine rather
    /// than three. Only BCL types cross -- int, bool -- because the caller reaches this by
    /// REFLECTION and holds no reference to this assembly, so it cannot name Ped. Nothing
    /// thrown leaves this file, because an exception crossing a reflection call arrives at the
    /// other end wrapped in a type the caller does not have. And it is safe before Hoodrich has
    /// started: everything answers "no" until Ready.
    /// </summary>
    public static class Corpse
    {
        /// <summary>
        /// The contract version. Bumped when a signature here changes in a way that breaks.
        /// Read by the caller BEFORE anything else.
        /// </summary>
        public static int ApiVersion => 1;

        /// <summary>Hoodrich's own version string, for the other side's log.</summary>
        public static string Version
        {
            get { try { return Core.Build.Version; } catch { return "?"; } }
        }

        private static Economy.Bodies _bodies;

        /// <summary>Whether this has been wired up yet. Everything below answers no until it is.</summary>
        public static bool Ready
        {
            get { return _bodies != null; }
        }

        /// <summary>Called once by Main, after the body store exists.</summary>
        internal static void Wire(Economy.Bodies bodies)
        {
            _bodies = bodies;
        }

        /// <summary>
        /// Whether this body has already been searched.
        ///
        /// BY HANDLE, because a Ped cannot cross the seam. The caller has one; the caller got
        /// it from the same engine we did, so it means the same thing on both sides.
        /// </summary>
        public static bool Searched(int handle)
        {
            try
            {
                if (_bodies == null || handle == 0) return false;

                return _bodies.Opened(handle);
            }
            catch
            {
                return false;
            }
        }
    }
}
