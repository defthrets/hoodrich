using System;
using System.Reflection;

namespace Hoodrich.Core
{
    /// <summary>
    /// Bare Minimum's food, through its own public API, if it is installed.
    ///
    /// THE SAME SHAPE AS Bridge, FOR THE SAME REASON. Nothing here references the other mod's
    /// assembly: it is found by name among what ScriptHookVDotNet has loaded, its API class is
    /// found by name, and each method is looked up once and cached. A player without Bare
    /// Minimum never sees a food page and never sees an error, because the only thing this
    /// does when the mod is absent is say so.
    ///
    /// Everything is wrapped, because the other side is somebody else's code running in
    /// somebody else's version -- a method that has been renamed returns null and the page
    /// simply does not offer that thing.
    /// </summary>
    internal static class Pantry
    {
        private const string Assembly = "BareMinimum";
        private const string TypeName = "BareMinimum.Api.Pantry";
        private const int RetryEveryMs = 3000;
        private const int GiveUpAfterMs = 60000;

        private static Type _type;
        private static bool _gaveUp;
        private static int _nextTry;
        private static int _firstTry;

        private static PropertyInfo _ready;
        private static MethodInfo _ids, _countOf, _nameOf, _iconOf, _give, _take;

        /// <summary>The counter's side of it: what they sell, and what one costs. See Menu.</summary>
        private static MethodInfo _menu, _priceOf, _descOf;

        /// <summary>How much he can carry, and how much of that is used.</summary>
        private static PropertyInfo _total, _slots;

        /// <summary>Whether the other mod is here and answering.</summary>
        public static bool Present
        {
            get
            {
                if (_type == null)
                {
                    if (_gaveUp) return false;
                    Look();
                    if (_type == null) return false;
                }

                try { return _ready != null && (bool)_ready.GetValue(null); }
                catch { return false; }
            }
        }

        /// <summary>Whether food can be moved both ways. Take is new on that side; an older build has only Give.</summary>
        public static bool CanTake => Present && _take != null;

        private static void Look()
        {
            var now = GTA.Game.GameTime;
            if (now < _nextTry) return;
            _nextTry = now + RetryEveryMs;

            if (_firstTry == 0) _firstTry = now;
            if (now - _firstTry > GiveUpAfterMs) { _gaveUp = true; return; }

            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!string.Equals(asm.GetName().Name, Assembly, StringComparison.OrdinalIgnoreCase)) continue;

                    var t = asm.GetType(TypeName, false);
                    if (t == null) continue;

                    var flags = BindingFlags.Public | BindingFlags.Static;

                    _ready = t.GetProperty("Ready", flags);
                    _ids = t.GetMethod("Ids", flags, null, Type.EmptyTypes, null);
                    _countOf = t.GetMethod("CountOf", flags, null, new[] { typeof(string) }, null);
                    _nameOf = t.GetMethod("NameOf", flags, null, new[] { typeof(string) }, null);
                    _iconOf = t.GetMethod("IconOf", flags, null, new[] { typeof(string) }, null);
                    _give = t.GetMethod("Give", flags, null, new[] { typeof(string), typeof(int) }, null);
                    _take = t.GetMethod("Take", flags, null, new[] { typeof(string), typeof(int) }, null);

                    // NEWER THAN THE REST, AND NOT REQUIRED. An older Bare Minimum has no
                    // menu to read, which costs the delivery and nothing else -- the boot
                    // and the pockets work exactly as they did. Checked for null at every
                    // use rather than being a reason to give up on the whole bridge.
                    _menu = t.GetMethod("Menu", flags, null, Type.EmptyTypes, null);
                    _priceOf = t.GetMethod("PriceOf", flags, null, new[] { typeof(string) }, null);
                    _descOf = t.GetMethod("DescOf", flags, null, new[] { typeof(string) }, null);
                    _total = t.GetProperty("Total", flags);
                    _slots = t.GetProperty("Slots", flags);

                    if (_ready == null || _ids == null || _countOf == null || _give == null)
                    {
                        Log.Info("Pantry: Bare Minimum is here but its API is not the shape expected; food stays out of the boot.");
                        _gaveUp = true;
                        return;
                    }

                    _type = t;
                    Log.Info("Pantry: Bare Minimum found" + (_take == null ? " (older build -- food goes out of the boot but not in)." : "."));
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Pantry: looking for Bare Minimum failed: " + ex.Message);
            }
        }

        public static string[] Ids()
        {
            try { return Present ? (string[])_ids.Invoke(null, null) ?? new string[0] : new string[0]; }
            catch { return new string[0]; }
        }

        public static int CountOf(string id)
        {
            try { return Present ? (int)_countOf.Invoke(null, new object[] { id }) : 0; }
            catch { return 0; }
        }

        public static string NameOf(string id)
        {
            try
            {
                var s = Present && _nameOf != null ? _nameOf.Invoke(null, new object[] { id }) as string : null;
                return string.IsNullOrEmpty(s) ? id : s;
            }
            catch { return id; }
        }

        /// <summary>The full path of the item's picture, or "" -- their icons live in their folder.</summary>
        public static string IconOf(string id)
        {
            try { return Present && _iconOf != null ? _iconOf.Invoke(null, new object[] { id }) as string ?? "" : ""; }
            catch { return ""; }
        }

        /// <summary>Into his bag. False when it is full or the id is not on the menu.</summary>
        public static bool Give(string id, int howMany)
        {
            try { return Present && (bool)_give.Invoke(null, new object[] { id, howMany }); }
            catch { return false; }
        }

        /// <summary>Whether their build is new enough to be ordered from. See LuberFood.</summary>
        public static bool CanOrder => Present && _menu != null && _priceOf != null;

        /// <summary>Every id a counter would sell, for a menu of our own. Never null.</summary>
        public static string[] Menu()
        {
            try { return CanOrder ? (string[])_menu.Invoke(null, null) ?? new string[0] : new string[0]; }
            catch { return new string[0]; }
        }

        /// <summary>The one line about it their shelf carries, or "".</summary>
        public static string DescOf(string id)
        {
            try { return Present && _descOf != null ? _descOf.Invoke(null, new object[] { id }) as string ?? "" : ""; }
            catch { return ""; }
        }

        /// <summary>What one costs over their counter, or nought.</summary>
        public static int PriceOf(string id)
        {
            try { return CanOrder ? (int)_priceOf.Invoke(null, new object[] { id }) : 0; }
            catch { return 0; }
        }

        /// <summary>How many things he is carrying, and how many he can. Nought when unknown.</summary>
        public static int Total
        {
            get
            {
                try { return Present && _total != null ? (int)_total.GetValue(null, null) : 0; }
                catch { return 0; }
            }
        }

        public static int Slots
        {
            get
            {
                try { return Present && _slots != null ? (int)_slots.GetValue(null, null) : 0; }
                catch { return 0; }
            }
        }

        /// <summary>Out of his bag without eating it. False when he has not got that many.</summary>
        public static bool Take(string id, int howMany)
        {
            try { return CanTake && (bool)_take.Invoke(null, new object[] { id, howMany }); }
            catch { return false; }
        }
    }
}
