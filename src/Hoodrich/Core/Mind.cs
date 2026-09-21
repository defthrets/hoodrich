using System;
using System.Reflection;
using GTA;

namespace Hoodrich.Core
{
    /// <summary>
    /// Asking NPC Mind whether it is holding the screen, if it is installed.
    ///
    /// ONE QUESTION, AND IT HAS TO BE ASKED FROM THIS SIDE. NPC Mind puts a conversation panel
    /// on screen and reads the keyboard while it is up. This mod's phone opens on a POLLED RAW
    /// KEY rather than on a game control -- see PhoneController, which says so -- and
    /// DISABLE_CONTROL_ACTION only suppresses game controls, so nothing that mod can do will
    /// stop our phone landing on top of its panel. A script cannot intercept another script's
    /// keyboard polling. So the fix is a question we ask before we open: one line, here.
    ///
    /// LATE-BOUND, exactly as Bridge and Larder are and for exactly the same reasons. A GTA
    /// scripts\ folder is ONE assembly resolution namespace: two mods that both reference a
    /// third assembly must agree about its version forever, and when they stop agreeing the
    /// failure is a TypeLoadException at load with no log, because the thing that would have
    /// written the log is the thing that did not load. Nothing here references that assembly,
    /// nothing here fails to compile without it, and the answer to "not installed" is false.
    ///
    /// LOAD ORDER IS NOT GUARANTEED, which is the part that bites. SHVDN constructs scripts in
    /// whatever order it finds them, so on roughly half of all launches this is built before
    /// NPC Mind exists in the AppDomain -- and a resolve-once-at-startup bridge is then
    /// permanently absent, on those launches only. So the lookup RETRIES on a timer for the
    /// first half-minute and only then gives up. Learned on the Precinct 88 bridge; not
    /// learned a third time.
    ///
    /// ONLY BCL TYPES CROSS: a bool and an int. mscorlib is the one assembly both mods are
    /// guaranteed to agree about.
    /// </summary>
    internal static class Mind
    {
        private const string Assembly = "GtaNpcMind";
        private const string TypeName = "GtaNpcMind.Api.Dialogue";

        /// <summary>The contract this code was written against.</summary>
        private const int WantApi = 1;

        private const int GiveUpAfterMs = 30000;
        private const int RetryEveryMs = 2000;

        private static Type _type;
        private static bool _gaveUp;
        private static int _nextTry;
        private static int _firstTry;

        private static PropertyInfo _ready;
        private static PropertyInfo _busy;

        /// <summary>Whether NPC Mind is there, answering, and wired up at its end.</summary>
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

        /// <summary>
        /// Whether it owns the screen and the keyboard this frame: a conversation panel up, or
        /// its own card over a body. Do not open the phone on top of that.
        ///
        /// FALSE FOR EVERY OTHER REASON THERE COULD BE. Not installed, an API it does not
        /// recognise, not wired yet, a throw on the far side of a reflection call -- all of
        /// them answer no, because the failure mode of a wrong yes is a phone key that has
        /// silently stopped working and nothing on screen to say why.
        /// </summary>
        public static bool IsBusy
        {
            get
            {
                try
                {
                    return Present && _busy != null && (bool)_busy.GetValue(null, null);
                }
                catch
                {
                    return false;
                }
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
                    // side safe to change later: an old Posted Up against a new NPC Mind sees
                    // a number it does not like and stops asking, rather than half-calling an
                    // API that has moved. Stopping asking means the phone opens as it always
                    // did, which is the safe way round for a guard.
                    var api = type.GetProperty("ApiVersion", BindingFlags.Public | BindingFlags.Static);
                    var have = api == null ? 0 : (int)api.GetValue(null, null);

                    if (have != WantApi)
                    {
                        Log.Info("NPC Mind speaks API v" + have + " and this wants v" + WantApi +
                                 ". The phone will not hold off for it.");
                        _gaveUp = true;
                        return null;
                    }

                    _ready = type.GetProperty("Ready", BindingFlags.Public | BindingFlags.Static);
                    _busy = type.GetProperty("IsBusy", BindingFlags.Public | BindingFlags.Static);

                    _type = type;

                    var version = type.GetProperty("Version", BindingFlags.Public | BindingFlags.Static);
                    Log.Info("NPC Mind " +
                             (version == null ? "?" : version.GetValue(null, null) as string) +
                             " found. The phone holds off while it is talking to somebody.");

                    return _type;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not look for NPC Mind: " + ex.Message);
            }

            if (now - _firstTry > GiveUpAfterMs)
            {
                _gaveUp = true;
                Log.Debug("NPC Mind is not installed. Nothing to hold off for.");
            }

            return null;
        }
    }
}
