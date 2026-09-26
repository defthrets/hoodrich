using System;

namespace Hoodrich.Court
{
    /// <summary>
    /// WHAT THE COURT ASKS OF THE MOD IT IS IN.
    ///
    /// The court is one game in two mods: Hoops on its own, and Posted Up with it built in. Every
    /// file in Court\ is the same in both -- tools\sync-court.py copies them from here and changes
    /// the namespace and nothing else -- so everything that differs between the two comes through
    /// this class: where settings are kept, where the log goes, how the help box and the feed are
    /// shown, and whether something else has the player's attention. Each mod fills these in once,
    /// as it starts.
    ///
    /// Nothing in Court\ reaches past this for anything of its host's. The moment one file there
    /// names a host class, the copy stops compiling in the other mod -- which is the point: two
    /// hand-kept copies of one game drift, and a sync that only renames cannot.
    ///
    /// Every default is safe on its own: a court whose host set nothing still plays, just with
    /// nothing logged, nothing remembered and its labels in English.
    /// </summary>
    internal static class CourtHost
    {
        public static Action<string> Info = _ => { };
        public static Action<string> Warn = _ => { };
        public static Action<string> Debug = _ => { };

        /// <summary>One value from the host's ini: section, key, and what to use when it is not there.</summary>
        public static Func<string, string, string, string> Read = (section, key, fallback) => fallback;

        /// <summary>One value written back to the host's ini.</summary>
        public static Action<string, string, string> Put = (section, key, value) => { };

        /// <summary>The game's help box, for this frame.</summary>
        public static Action<string> Help = _ => { };

        /// <summary>A line on the feed; and one that says something went wrong.</summary>
        public static Action<string> Ticker = _ => { };
        public static Action<string> Problem = _ => { };

        /// <summary>Something else has the player -- a menu, a conversation -- and the court should not offer itself.</summary>
        public static Func<bool> Busy = () => false;

        /// <summary>The press that started a game, eaten, so nothing else answers it too.</summary>
        public static Action Swallow = () => { };

        /// <summary>A label in the player's language. English, unless the host speaks others.</summary>
        public static Func<string, string> T = s => s;

        /// <summary>
        /// A rectangle went into the game's draw list. Every mod in the set shares one count of
        /// those, because the game drops rectangles past about 350 a frame across all of them;
        /// the host adds this to it. See CourtDraw.Rect.
        /// </summary>
        public static Action Counted = () => { };

        /// <summary>The last thing touched was a pad rather than the keyboard.</summary>
        public static Func<bool> OnPad = () => false;
    }
}
