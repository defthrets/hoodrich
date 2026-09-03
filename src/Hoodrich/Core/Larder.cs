using System;
using System.Drawing;
using System.Reflection;
using GTA;

namespace Hoodrich.Core
{
    /// <summary>
    /// Talking to Bare Minimum, if it is installed.
    ///
    /// Bare Minimum is the hunger and sleep mod. Buying food there fills a pocket rather than
    /// eating on the spot, and the phone is a better place to look at that pocket than a key
    /// of its own -- so the inventory screen shows what you are carrying and lets you eat it.
    ///
    /// LATE-BOUND, exactly as Bridge is and for exactly the same reasons. A GTA scripts\ folder
    /// is ONE assembly resolution namespace: two mods that both reference a third assembly must
    /// agree about its version forever, and when they stop agreeing the failure is a
    /// TypeLoadException at load with no log, because the thing that would have written the log
    /// is the thing that did not load. Nothing here references that assembly, nothing here
    /// fails to compile without it, and the answer to "not installed" is Present == false and
    /// every method below returning nothing.
    ///
    /// LOAD ORDER IS NOT GUARANTEED, which is the part that bites. SHVDN constructs scripts in
    /// whatever order it finds them, so on roughly half of all launches Hoodrich is built
    /// before Bare Minimum exists in the AppDomain -- and a resolve-once-at-startup bridge is
    /// then permanently absent, on those launches only. So the lookup RETRIES on a timer for
    /// the first half-minute and only then gives up. Learned on the Precinct 88 bridge; not
    /// learned twice.
    ///
    /// ONLY BCL TYPES CROSS. The other side hands back strings, ints and bools -- an ARGB int
    /// rather than a Color, a full file path rather than an icon handle -- because mscorlib is
    /// the one assembly both mods are guaranteed to agree about.
    /// </summary>
    internal static class Larder
    {
        private const string Assembly = "BareMinimum";
        private const string TypeName = "BareMinimum.Api.Pantry";

        /// <summary>The contract this code was written against.</summary>
        private const int WantApi = 1;

        private const int GiveUpAfterMs = 30000;
        private const int RetryEveryMs = 2000;

        private static Type _type;
        private static bool _gaveUp;
        private static int _nextTry;
        private static int _firstTry;

        private static PropertyInfo _ready, _total, _slots;
        private static MethodInfo _ids, _countOf, _nameOf, _iconOf, _tintOf, _consume, _mark;

        // ======================================================================

        /// <summary>Whether the other mod is here AND has finished starting up.</summary>
        public static bool Present
        {
            get
            {
                var type = Resolve();
                if (type == null) return false;

                try { return _ready != null && (bool)_ready.GetValue(null, null); }
                catch { return false; }
            }
        }

        private static Type Resolve()
        {
            if (_type != null) return _type;
            if (_gaveUp) return null;

            var now = Game.GameTime;

            if (_firstTry == 0) _firstTry = now;
            if (now < _nextTry) return null;

            _nextTry = now + RetryEveryMs;

            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name != Assembly) continue;

                    var type = asm.GetType(TypeName);
                    if (type == null) continue;

                    // THE VERSION IS READ BEFORE ANYTHING ELSE, which is what makes the other
                    // side safe to change later: an old Hoodrich against a new Bare Minimum
                    // sees a number it does not like and quietly shows no food, rather than
                    // half-calling an API that has moved.
                    var api = type.GetProperty("ApiVersion", BindingFlags.Public | BindingFlags.Static);
                    var have = api == null ? 0 : (int)api.GetValue(null, null);

                    if (have != WantApi)
                    {
                        Log.Info("Bare Minimum speaks API v" + have + " and this wants v" +
                                 WantApi + ". No food on the phone.");
                        _gaveUp = true;
                        return null;
                    }

                    _ready = type.GetProperty("Ready", BindingFlags.Public | BindingFlags.Static);
                    _total = type.GetProperty("Total", BindingFlags.Public | BindingFlags.Static);
                    _slots = type.GetProperty("Slots", BindingFlags.Public | BindingFlags.Static);

                    _ids = type.GetMethod("Ids", BindingFlags.Public | BindingFlags.Static);
                    _countOf = type.GetMethod("CountOf", BindingFlags.Public | BindingFlags.Static);
                    _nameOf = type.GetMethod("NameOf", BindingFlags.Public | BindingFlags.Static);
                    _iconOf = type.GetMethod("IconOf", BindingFlags.Public | BindingFlags.Static);
                    _tintOf = type.GetMethod("TintOf", BindingFlags.Public | BindingFlags.Static);
                    _consume = type.GetMethod("Consume", BindingFlags.Public | BindingFlags.Static);

                    // Optional: an older Bare Minimum on the same API version will not
                    // have it, and a null here just means no mark beside the heading.
                    _mark = type.GetMethod("Mark", BindingFlags.Public | BindingFlags.Static);

                    _type = type;

                    var version = type.GetProperty("Version", BindingFlags.Public | BindingFlags.Static);
                    Log.Info("Bare Minimum " +
                             (version == null ? "?" : version.GetValue(null, null) as string) +
                             " found. Food will show on the phone.");

                    return _type;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not look for Bare Minimum: " + ex.Message);
            }

            if (now - _firstTry > GiveUpAfterMs)
            {
                _gaveUp = true;
                Log.Info("Bare Minimum is not installed. No food on the phone.");
            }

            return null;
        }

        // ======================================================================

        /// <summary>Ids carried, oldest first. Never null.</summary>
        public static string[] Ids()
        {
            try
            {
                if (!Present || _ids == null) return new string[0];

                return _ids.Invoke(null, null) as string[] ?? new string[0];
            }
            catch { return new string[0]; }
        }

        public static int CountOf(string id)
        {
            try
            {
                if (!Present || _countOf == null) return 0;
                return (int)_countOf.Invoke(null, new object[] { id });
            }
            catch { return 0; }
        }

        public static string NameOf(string id)
        {
            try
            {
                if (!Present || _nameOf == null) return "";
                return _nameOf.Invoke(null, new object[] { id }) as string ?? "";
            }
            catch { return ""; }
        }

        /// <summary>The full path of that item's picture, or "". Drawn with Hud.File.</summary>
        public static string IconOf(string id)
        {
            try
            {
                if (!Present || _iconOf == null) return "";
                return _iconOf.Invoke(null, new object[] { id }) as string ?? "";
            }
            catch { return ""; }
        }

        public static Color TintOf(string id)
        {
            try
            {
                if (!Present || _tintOf == null) return Color.FromArgb(255, 235, 235, 240);
                return Color.FromArgb((int)_tintOf.Invoke(null, new object[] { id }));
            }
            catch { return Color.FromArgb(255, 235, 235, 240); }
        }

        public static int Total
        {
            get
            {
                try { return !Present || _total == null ? 0 : (int)_total.GetValue(null, null); }
                catch { return 0; }
            }
        }

        public static int Slots
        {
            get
            {
                try { return !Present || _slots == null ? 0 : (int)_slots.GetValue(null, null); }
                catch { return 0; }
            }
        }

        /// <summary>The other mod's own mark, as a full path. "" when it has none.</summary>
        public static string Mark(string which)
        {
            try
            {
                if (!Present || _mark == null) return "";
                return _mark.Invoke(null, new object[] { which }) as string ?? "";
            }
            catch { return ""; }
        }

        /// <summary>Eats, drinks or smokes one. True when it actually started.</summary>
        public static bool Consume(string id)
        {
            try
            {
                if (!Present || _consume == null) return false;
                return (bool)_consume.Invoke(null, new object[] { id });
            }
            catch { return false; }
        }
    }
}
