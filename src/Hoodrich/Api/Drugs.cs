using System;
using System.Collections.Generic;

namespace Hoodrich.Api
{
    /// <summary>
    /// What another mod is allowed to know about the product in your pockets, and nothing else.
    ///
    /// THIS EXISTS BECAUSE THERE WAS NO WAY IN. Hoodrich keeps the stash on PlayerState, which
    /// is an instance held by Main -- so a mod trying to read it by reflection can find the
    /// TYPE and never the object. Bare Minimum wants to show what you are carrying in its own
    /// pocket and let you take it from there, and could not, because nothing static pointed at
    /// the live stash. This is that pointer and only that.
    ///
    /// IT IS THE MIRROR OF Core/Larder, which is how Hoodrich already reads Bare Minimum's
    /// food. Same shape on purpose: one pattern on this machine rather than two.
    ///
    /// ONLY BCL TYPES CROSS -- string, float, bool and arrays of those. The caller reaches
    /// this by REFLECTION and holds no reference to this assembly, so it cannot name DrugDef
    /// or Stash; hand one back and the other side gets an object it can only poke at with
    /// more reflection. mscorlib is the one assembly both mods are guaranteed to agree about.
    ///
    /// NOTHING THROWN LEAVES THIS FILE. An exception crossing a reflection call arrives at the
    /// other end as a TargetInvocationException wrapping a type the caller does not have --
    /// which is a crash in a mod whose author cannot read the stack trace.
    ///
    /// IT IS SAFE BEFORE HOODRICH HAS STARTED. SHVDN builds scripts in whatever order it finds
    /// them, so the other mod can call in before Wire has run. Everything answers "nothing"
    /// until Ready, and the caller is expected to keep asking.
    /// </summary>
    public static class Drugs
    {
        /// <summary>
        /// The contract version. Bumped when a signature here changes in a way that breaks.
        ///
        /// Read by the caller BEFORE anything else: an old client talking to a new Hoodrich
        /// checks this, does not like it, and quietly shows nothing rather than half-calling
        /// an API that has moved.
        /// </summary>
        public static int ApiVersion => 1;

        /// <summary>Hoodrich's own version string, for the other side's log.</summary>
        public static string Version
        {
            get { try { return Core.Build.Version; } catch { return "?"; } }
        }

        private static State.PlayerState _state;
        private static Economy.Drugs _catalogue;

        /// <summary>Called by Main once the state and the catalogue exist. Not for outside use.</summary>
        internal static void Wire(State.PlayerState state, Economy.Drugs catalogue)
        {
            _state = state;
            _catalogue = catalogue;
        }

        internal static void Unwire()
        {
            _state = null;
            _catalogue = null;
        }

        /// <summary>Whether Hoodrich is here AND has finished starting up.</summary>
        public static bool Ready
        {
            get
            {
                try { return _state != null && _state.Stash != null && _catalogue != null; }
                catch { return false; }
            }
        }

        /// <summary>
        /// The ids of everything STREET-READY in your pockets. Bulk weight is not included.
        ///
        /// Deliberate: a brick that has not been cut is not something you take, it is stock.
        /// Bare Minimum is asking what you could put in your mouth, and the answer is the
        /// bagged product.
        /// </summary>
        public static string[] Ids()
        {
            try
            {
                if (!Ready) return new string[0];

                var found = new List<string>();
                foreach (var d in _catalogue.All)
                {
                    if (_state.Stash.PackagedOf(d.Id) > 0.005f) found.Add(d.Id);
                }

                return found.ToArray();
            }
            catch { return new string[0]; }
        }

        /// <summary>Grams of street-ready product on hand. Zero for anything unknown.</summary>
        public static float GramsOf(string id)
        {
            try { return Ready ? _state.Stash.PackagedOf(id) : 0f; }
            catch { return 0f; }
        }

        /// <summary>What it is called on a menu. The id back if the catalogue has never heard of it.</summary>
        public static string NameOf(string id)
        {
            try
            {
                if (!Ready) return id ?? "";
                var def = _catalogue.Get(id);
                return def == null || string.IsNullOrEmpty(def.Name) ? (id ?? "") : def.Name;
            }
            catch { return id ?? ""; }
        }

        /// <summary>How pure it is, 0 to 1. One when holding none, which is what Stash says.</summary>
        public static float PurityOf(string id)
        {
            try { return Ready ? _state.Stash.PurityOf(id) : 1f; }
            catch { return 1f; }
        }

        /// <summary>
        /// Takes product OUT of the stash and says how much it actually got.
        ///
        /// THE CALLER DOES NOT DECIDE WHAT IT DOES. This removes weight and nothing else --
        /// no high, no animation, no wanted level. Bare Minimum wants a gram gone so it can
        /// feed its own hunger and sleep meters with it, and Hoodrich's own high is a
        /// different thing that its own phone starts. Two mods writing one effect is how you
        /// get a player who is high twice.
        ///
        /// Returns 0 if there was none, if Hoodrich has not started, or if anything failed.
        /// </summary>
        public static float Take(string id, float grams)
        {
            try
            {
                if (!Ready || string.IsNullOrEmpty(id) || grams <= 0f) return 0f;
                return _state.Stash.RemovePackaged(id, grams);
            }
            catch { return 0f; }
        }
    }
}
