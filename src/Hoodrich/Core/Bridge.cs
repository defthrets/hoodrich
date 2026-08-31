using System;
using System.Reflection;
using GTA;
using GTA.Math;

namespace Hoodrich.Core
{
    /// <summary>
    /// Talking to Precinct 88, if it is installed.
    ///
    /// Precinct 88 is the police overhaul that took over this mod's ambient patrol. The two are
    /// shipped separately and either can be updated without the other, so this is late-bound:
    /// nothing here references that assembly, nothing here fails to compile without it, and the
    /// answer to "it is not installed" is Present == false and every method below doing
    /// nothing.
    ///
    /// WHY NOT A SHARED INTEROP DLL, which is the obvious alternative. A GTA scripts\ folder is
    /// ONE ASSEMBLY RESOLUTION NAMESPACE. Two mods that both reference a third assembly are two
    /// mods that must agree about its exact version forever, and when they stop agreeing the
    /// failure is a TypeLoadException at load with no log -- because the thing that would have
    /// written the log is the thing that did not load. That fight has already been had on this
    /// machine over NAudio, which is strong-named and therefore exact-match bound. Late binding
    /// cannot lose it.
    ///
    /// LOAD ORDER IS NOT GUARANTEED AND THIS IS THE PART THAT BITES. SHVDN constructs scripts in
    /// whatever order it finds them, so on roughly half of all launches Hoodrich is built before
    /// Precinct 88 exists in the AppDomain -- and a resolve-once-at-startup bridge is then
    /// permanently absent, on those launches only, which is about the worst shape a bug can
    /// have. So the lookup RETRIES on a timer for the first half-minute of the session and only
    /// then gives up for good.
    /// </summary>
    internal static class Bridge
    {
        private const string Assembly = "Precinct88";
        private const string TypeName = "Precinct88.Api.Dispatch";

        /// <summary>The contract this code was written against.</summary>
        private const int WantApi = 1;

        /// <summary>How long to keep looking before concluding it is genuinely not there.</summary>
        private const int GiveUpAfterMs = 30000;

        private const int RetryEveryMs = 2000;

        private static Type _type;
        private static bool _gaveUp;
        private static int _nextTry;
        private static int _firstTry;

        private static MethodInfo _report;
        private static MethodInfo _hold;
        private static MethodInfo _release;
        private static MethodInfo _cap;
        private static MethodInfo _uncap;
        private static MethodInfo _lawHeld;
        private static MethodInfo _sendUnit;
        private static MethodInfo _onSeize;
        private static MethodInfo _inCustody;
        private static MethodInfo _ready;

        /// <summary>What it calls itself, for our log.</summary>
        public static string Version { get; private set; } = string.Empty;

        /// <summary>
        /// Whether Precinct 88 is there and answering.
        ///
        /// Asked freely -- it is a cached field plus a clock most of the time, and the actual
        /// assembly walk happens at most once every two seconds and only for the first half
        /// minute.
        /// </summary>
        public static bool Present
        {
            get
            {
                if (_type != null) return Ready();
                if (_gaveUp) return false;

                Look();
                return _type != null && Ready();
            }
        }

        /// <summary>
        /// Whether it is not merely loaded but wired.
        ///
        /// Separate from Present-as-found, because there is a window: its Main assigns its own
        /// systems onto the bridge at the END of its constructor, so a call landing before then
        /// would reach a class whose fields are all null.
        /// </summary>
        private static bool Ready()
        {
            try { return _ready != null && (bool)_ready.Invoke(null, null); }
            catch { return false; }
        }

        private static void Look()
        {
            var now = Game.GameTime;

            if (_firstTry == 0) _firstTry = now;
            if (now < _nextTry) return;

            _nextTry = now + RetryEveryMs;

            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name != Assembly) continue;

                    var type = asm.GetType(TypeName, false);
                    if (type == null) continue;

                    var api = type.GetProperty("ApiVersion", BindingFlags.Public | BindingFlags.Static);
                    var version = (int)(api == null ? -1 : api.GetValue(null, null));

                    if (version != WantApi)
                    {
                        // A version we do not understand. Half-calling an API that has moved is
                        // worse than not calling it -- so this gives up permanently rather than
                        // retrying, and says exactly what it found.
                        _gaveUp = true;
                        Log.Warn("Precinct 88 is installed but speaks API v" + version +
                                 " and this build of Hoodrich speaks v" + WantApi +
                                 ". Not bridging; both mods run on their own.");
                        return;
                    }

                    Bind(type);
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Bridge lookup failed: " + ex.Message);
            }

            if (now - _firstTry > GiveUpAfterMs)
            {
                _gaveUp = true;
                Log.Info("Precinct 88 is not installed. Hoodrich runs on its own.");
            }
        }

        private static void Bind(Type type)
        {
            const BindingFlags Pub = BindingFlags.Public | BindingFlags.Static;

            _ready = type.GetMethod("Ready", Pub, null, Type.EmptyTypes, null);

            _report = type.GetMethod("Report", Pub, null,
                new[] { typeof(string), typeof(float), typeof(float), typeof(float) }, null);

            _hold = type.GetMethod("HoldLaw", Pub, null, new[] { typeof(string) }, null);
            _release = type.GetMethod("ReleaseLaw", Pub, null, new[] { typeof(string) }, null);
            _cap = type.GetMethod("CapLaw", Pub, null, new[] { typeof(int) }, null);
            _uncap = type.GetMethod("UncapLaw", Pub, null, Type.EmptyTypes, null);
            _lawHeld = type.GetMethod("LawIsHeld", Pub, null, Type.EmptyTypes, null);

            _sendUnit = type.GetMethod("SendUnit", Pub, null,
                new[] { typeof(float), typeof(float), typeof(float), typeof(string) }, null);

            // Func<string,string> is an mscorlib type, so it has the same identity in both
            // assemblies and the delegate crosses the boundary as itself rather than as
            // something either side has to reflect over.
            _onSeize = type.GetMethod("OnSeize", Pub, null,
                new[] { typeof(Func<string, string>) }, null);

            _inCustody = type.GetMethod("PlayerInCustody", Pub, null, Type.EmptyTypes, null);

            var ver = type.GetProperty("Version", Pub);
            Version = ver == null ? "?" : (ver.GetValue(null, null) as string) ?? "?";

            _type = type;

            Log.Info("Bridged to Precinct 88 " + Version + " (API v" + WantApi + "). " +
                     "It owns the ambient patrol, the wanted level and the law hold.");
        }

        // ---- calling out -------------------------------------------------------

        /// <summary>
        /// Tells the police something happened.
        ///
        /// The offence name is matched loosely at the other end -- "drugs", "gun", "murder" and
        /// its own names all land somewhere sensible -- so a word that does not match exactly is
        /// a mild report rather than a dropped one.
        /// </summary>
        public static void Report(string offence, Vector3 where)
        {
            if (!Present) return;

            try { _report.Invoke(null, new object[] { offence, where.X, where.Y, where.Z }); }
            catch (Exception ex) { Log.Debug("Bridge report failed: " + ex.Message); }
        }

        /// <summary>
        /// Holds the police off through Precinct 88's counted hold.
        ///
        /// Returns whether it went across. False means Hoodrich's own LawHold has to do the
        /// work itself, which is exactly what it does -- see LawHold.
        /// </summary>
        public static bool Hold(string who)
        {
            if (!Present) return false;

            try { _hold.Invoke(null, new object[] { who }); return true; }
            catch (Exception ex) { Log.Debug("Bridge hold failed: " + ex.Message); return false; }
        }

        public static bool Release(string who)
        {
            if (!Present) return false;

            try { _release.Invoke(null, new object[] { who }); return true; }
            catch (Exception ex) { Log.Debug("Bridge release failed: " + ex.Message); return false; }
        }

        public static bool Cap(int stars)
        {
            if (!Present) return false;

            try { _cap.Invoke(null, new object[] { stars }); return true; }
            catch (Exception ex) { Log.Debug("Bridge cap failed: " + ex.Message); return false; }
        }

        public static bool Uncap()
        {
            if (!Present) return false;

            try { _uncap.Invoke(null, null); return true; }
            catch (Exception ex) { Log.Debug("Bridge uncap failed: " + ex.Message); return false; }
        }

        /// <summary>
        /// Whether anything is holding the police off over there.
        ///
        /// Asked by LawHold.Held, which cannot answer from its own set once it is forwarding --
        /// Precinct 88 holds for its own bookings too, and a Hoodrich system reading "is the law
        /// a factor right now" has to see holds it did not place.
        /// </summary>
        public static bool LawIsHeld()
        {
            if (!Present) return false;

            try { return (bool)_lawHeld.Invoke(null, null); }
            catch { return false; }
        }

        /// <summary>
        /// Asks for a car.
        ///
        /// FALSE IS A REAL ANSWER. Precinct 88's police come out of a finite pool on a beat, so
        /// a quiet district at four in the morning genuinely has nobody to send -- and a caller
        /// that spawns its own car when this returns false has thrown away the only thing it
        /// gained by asking.
        /// </summary>
        public static bool SendUnit(Vector3 to, string reason)
        {
            if (!Present) return false;

            try
            {
                return (bool)_sendUnit.Invoke(null, new object[] { to.X, to.Y, to.Z, reason });
            }
            catch (Exception ex)
            {
                Log.Debug("Bridge dispatch failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>Whether the player is cuffed, in the back of a car, or in a cell.</summary>
        public static bool PlayerInCustody()
        {
            if (!Present) return false;

            try { return (bool)_inCustody.Invoke(null, null); }
            catch { return false; }
        }

        // ---- being called ------------------------------------------------------

        private static bool _registered;

        /// <summary>
        /// Registers what to take off the player when Precinct 88 searches or books him.
        ///
        /// The contract, and it is the important part: the handler must ALREADY HAVE TAKEN
        /// whatever it is going to take by the time it returns. What it returns is the line the
        /// player is shown. It is a seizure, not a query, because the alternative is two calls
        /// with a window between them in which he walks off having been told he was robbed and
        /// not having been.
        ///
        /// Called every tick from Main and cheap to call -- it registers once and then does
        /// nothing, which is what makes it safe to call from a tick at all given that Precinct
        /// 88 may not have loaded yet on the frame Hoodrich would rather have done this.
        /// </summary>
        public static void OfferSeizure(Func<string, string> handler)
        {
            if (_registered || handler == null) return;
            if (!Present) return;

            try
            {
                _onSeize.Invoke(null, new object[] { handler });
                _registered = true;

                Log.Info("Registered a seizure handler with Precinct 88; a search now costs " +
                         "product as well as weapons.");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not register a seizure handler: " + ex.Message);
            }
        }
    }
}
