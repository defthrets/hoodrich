using System;
using System.Reflection;

namespace Hoodrich.Core
{
    /// <summary>
    /// Five0 Patrol's body drag, asked for from over here.
    ///
    /// THE BRIDGE ALREADY RAN THE OTHER WAY. That mod asks this one whether a corpse has been
    /// searched yet -- see Api.Corpse -- so its drag prompt does not fight this one's loot
    /// prompt over the same key. This is the return leg: the loot card is the last thing you
    /// look at before you want the body gone, and the footer of that card is the honest place
    /// to say so rather than closing it and hunting for a second prompt.
    ///
    /// LATE-BOUND BY REFLECTION, WHICH IS THE ONLY WAY TWO SCRIPTS IN ONE FOLDER CAN TALK. A
    /// shared interop assembly would make both mods agree about its exact version forever, and
    /// the day they stopped agreeing the failure would be a TypeLoadException at load with no
    /// log -- because the thing that would have written the log is the thing that did not load.
    /// Larder does this for Bare Minimum's pantry and Contraband does it in the other direction;
    /// this is deliberately the same shape so there is one pattern on this machine.
    ///
    /// ONLY BCL TYPES CROSS. A ped is an int handle, the way it is in the natives underneath.
    ///
    /// AND NOTHING IN HERE THROWS. A mod that is not installed, is older, or has not finished
    /// starting all answer the same way: no. The screen then does not draw the key, which is
    /// exactly right -- an option that cannot work should not be on the footer.
    /// </summary>
    internal static class Undertaker
    {
        private const string TypeName = "Five0Patrol.Api.Bodies";

        private static bool _looked;
        private static Type _type;
        private static PropertyInfo _enabled, _holding;
        private static MethodInfo _take;

        /// <summary>Whether the other mod is here and its drag is switched on.</summary>
        public static bool Present
        {
            get
            {
                Look();

                try
                {
                    return _enabled != null && (bool)_enabled.GetValue(null, null);
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>Whether a body is in hand right now, so the key can say so.</summary>
        public static bool Holding
        {
            get
            {
                Look();

                try
                {
                    return _holding != null && (bool)_holding.GetValue(null, null);
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>Takes hold of that body. False if it would not have worked.</summary>
        public static bool Take(int pedHandle)
        {
            Look();

            try
            {
                if (_take == null || pedHandle == 0) return false;

                return (bool)_take.Invoke(null, new object[] { pedHandle });
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Finds it once.
        ///
        /// EVERY LOADED ASSEMBLY, because a script's own name is whatever the dll on disk is
        /// called and a player may well have renamed it. The type name is the contract.
        /// </summary>
        private static void Look()
        {
            if (_looked) return;
            _looked = true;

            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type found;

                    try { found = asm.GetType(TypeName, false); }
                    catch { continue; }

                    if (found == null) continue;

                    _type = found;

                    _enabled = found.GetProperty("Enabled", BindingFlags.Public | BindingFlags.Static);
                    _holding = found.GetProperty("Holding", BindingFlags.Public | BindingFlags.Static);
                    _take = found.GetMethod("Take", BindingFlags.Public | BindingFlags.Static);

                    Log.Info("Five0 Patrol found. A body can be dragged off the loot card.");
                    return;
                }
            }
            catch
            {
                // Not installed, which is the ordinary case and not worth a line.
            }
        }
    }
}
