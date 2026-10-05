using System;
using System.Collections.Generic;
using System.Reflection;
using GTA;

namespace Hoodrich.Core
{
    /// <summary>
    /// NPC Mind, if it is installed: whether it is holding the screen, what it will call one of
    /// our people, and whether it has him right now.
    ///
    /// TWO JOBS IN ONE PLACE, because both are the same question -- is NPC Mind here, and what
    /// is it doing -- asked of the same assembly.
    ///
    /// THE PHONE. NPC Mind puts a conversation panel on screen and reads the keyboard while it
    /// is up. This mod's phone opens on a POLLED RAW KEY rather than on a game control -- see
    /// PhoneController, which says so -- and DISABLE_CONTROL_ACTION only suppresses game
    /// controls, so nothing that mod can do will stop our phone landing on top of its panel. A
    /// script cannot intercept another script's keyboard polling. So the fix is a question we
    /// ask before we open: IsBusy.
    ///
    /// THE PEOPLE. NPC Mind does not keep names; it DERIVES them, every time, from two things --
    /// the ped's model and the number he is known by -- in Persona.Create(modelName, seed). For a
    /// ped we vouched for, the seed IS the npcmind_id we stamp on him (see Folk), so his name is
    /// decided the moment we stamp him, and any name this mod made up on its own would be a
    /// second, different name: the tag over his head saying Dee while he introduced himself as
    /// Marcus Johnson. NameOf asks the same two methods NPC Mind asks, with the same two inputs.
    /// And once one of ours can be talked to, NPC Mind runs him for as long as it has him --
    /// turned to you, talking, and then doing what he decided about you -- and the scene must
    /// keep its hands off him meanwhile: Holding says who. See Parkview.Scenery.Taken.
    ///
    /// LATE-BOUND, exactly as Bridge and Larder are and for exactly the same reasons. A GTA
    /// scripts\ folder is ONE assembly resolution namespace: two mods that both reference a
    /// third assembly must agree about its version forever, and when they stop agreeing the
    /// failure is a TypeLoadException at load with no log. Nothing here references that
    /// assembly, nothing here fails to compile without it, and the answer to "not installed" is
    /// false, nobody, and null.
    ///
    /// LOAD ORDER IS NOT GUARANTEED. SHVDN constructs scripts in whatever order it finds them,
    /// so on roughly half of all launches this is built before NPC Mind exists in the AppDomain
    /// -- and a resolve-once-at-startup bridge is then permanently absent, on those launches
    /// only. So the phone's lookup RETRIES on a timer for the first half-minute, and the
    /// people's lookup happens on the first tick, by which time every script in the folder is
    /// loaded. Learned on the Precinct 88 bridge; not learned a third time.
    ///
    /// ONLY BCL TYPES CROSS: bools, ints and strings.
    /// </summary>
    internal static class Mind
    {
        private const string MindAssembly = "GtaNpcMind";
        private const string DialogueType = "GtaNpcMind.Api.Dialogue";

        // =====================================================================================
        // The phone: does NPC Mind own the screen this frame
        // =====================================================================================

        /// <summary>The contract the phone guard was written against.</summary>
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
                    if (asm.GetName().Name != MindAssembly) continue;

                    var type = asm.GetType(DialogueType);
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

        // =====================================================================================
        // The people: what NPC Mind calls them, and whether it has them
        // =====================================================================================

        private static bool _looked;
        private static MethodInfo _modelName;
        private static MethodInfo _create;
        private static PropertyInfo _display;

        private static Func<int, bool> _holding;
        private static Func<int> _partner;
        private static Func<int> _target;
        private static Func<bool> _talking;

        /// <summary>Names already worked out, by seed and model. A Persona is not free to build.</summary>
        private static readonly Dictionary<long, string> _known = new Dictionary<long, string>();

        /// <summary>When a lookup last failed, so a missing mod is not asked sixty times a second.</summary>
        private static int _failedAt = -100000;
        private const int RetryMs = 5000;

        private static void Look()
        {
            if (_looked) return;
            _looked = true;

            try
            {
                Assembly mind = null;

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name != MindAssembly) continue;
                    mind = asm;
                    break;
                }

                if (mind == null)
                {
                    Log.Info("NPC Mind is not loaded; the scenes use their own names and run their own people.");
                    return;
                }

                LookNames(mind);
                LookDialogue(mind);
            }
            catch (Exception ex)
            {
                _create = null;
                Log.Debug("Could not look for NPC Mind's people: " + ex.Message);
            }
        }

        private static void LookNames(Assembly mind)
        {
            var names = mind.GetType("GtaNpcMind.Personas.PedModelNames", false);
            var persona = mind.GetType("GtaNpcMind.Personas.Persona", false);

            if (names != null && persona != null)
            {
                _modelName = names.GetMethod("NameOf", BindingFlags.Public | BindingFlags.Static,
                                             null, new[] { typeof(uint) }, null);
                _create = persona.GetMethod("Create", BindingFlags.Public | BindingFlags.Static,
                                            null, new[] { typeof(string), typeof(uint) }, null);
                _display = persona.GetProperty("DisplayName", BindingFlags.Public | BindingFlags.Instance);
            }

            if (_modelName == null || _create == null || _display == null)
            {
                _create = null;
                Log.Info("NPC Mind does not name people the way it used to; the scenes use their own names.");
                return;
            }

            Log.Info("NPC Mind is here: the scenes' people go by the names it gives them.");
        }

        private static void LookDialogue(Assembly mind)
        {
            var api = mind.GetType(DialogueType, false);

            if (api != null)
            {
                var holding = api.GetMethod("Holding", BindingFlags.Public | BindingFlags.Static,
                                            null, new[] { typeof(int) }, null);
                if (holding != null && holding.ReturnType == typeof(bool))
                {
                    _holding = (Func<int, bool>)Delegate.CreateDelegate(typeof(Func<int, bool>), holding);
                }

                _partner = Getter<int>(api, "Partner");
                _target = Getter<int>(api, "Target");
                _talking = Getter<bool>(api, "InConversation");
            }

            Log.Info(_holding != null
                ? "NPC Mind runs our people while it has them: the talk, and whatever they decide about you after."
                : _partner != null
                    ? "NPC Mind runs our people while it is talking to them. (This NPC Mind does not say who is " +
                      "still running from you after; update it and they are left alone for that too.)"
                    : "This NPC Mind does not say who it is talking to; a scene may re-task somebody mid-sentence.");
        }

        /// <summary>A public static property of NPC Mind's as a delegate, or null when it has none of that shape.</summary>
        private static Func<T> Getter<T>(Type type, string name)
        {
            var p = type.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
            var get = p == null ? null : p.GetGetMethod();
            if (get == null || get.ReturnType != typeof(T)) return null;

            return (Func<T>)Delegate.CreateDelegate(typeof(Func<T>), get);
        }

        /// <summary>
        /// Whether NPC Mind is running this man right now: talking to him, or he is carrying
        /// out what he decided about you -- a swing, a run, his hands up, a call to the police.
        /// </summary>
        public static bool Holding(Ped ped)
        {
            if (ped == null) return false;
            Look();

            try
            {
                var h = ped.Handle;

                if (_holding != null) return _holding(h);
                return _partner != null && _partner() == h;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>The handle of the man NPC Mind is talking to, or 0.</summary>
        public static int Partner
        {
            get
            {
                Look();
                try { return _partner != null ? _partner() : 0; }
                catch { return 0; }
            }
        }

        /// <summary>The handle of the man NPC Mind's own name tag and talk prompt are over, or 0.</summary>
        public static int Target
        {
            get
            {
                Look();
                try { return _target != null ? _target() : 0; }
                catch { return 0; }
            }
        }

        /// <summary>A conversation panel is up.</summary>
        public static bool Talking
        {
            get
            {
                Look();
                try { return _talking != null && _talking(); }
                catch { return false; }
            }
        }

        /// <summary>
        /// NPC Mind wants E this frame: a conversation is up, or its "press E to talk" is over
        /// somebody. The context key is both its talk key and the rooms' key, and one press
        /// must never do both.
        /// </summary>
        public static bool Busy
        {
            get { return Talking || Target != 0; }
        }

        /// <summary>
        /// The name NPC Mind will give this ped when he is stamped with <paramref name="id"/>,
        /// or null when it cannot be asked -- in which case the caller uses its own.
        /// </summary>
        public static string NameOf(Ped ped, int id)
        {
            if (ped == null || id == 0) return null;

            Look();
            if (_create == null) return null;

            try
            {
                if (!ped.Exists()) return null;

                var hash = unchecked((uint)ped.Model.Hash);
                var key = ((long)hash << 32) | unchecked((uint)id);

                string name;
                if (_known.TryGetValue(key, out name)) return name;

                if (Game.GameTime - _failedAt < RetryMs) return null;

                var model = _modelName.Invoke(null, new object[] { hash }) as string;
                if (string.IsNullOrEmpty(model)) { _failedAt = Game.GameTime; return null; }

                var p = _create.Invoke(null, new object[] { model, unchecked((uint)id) });
                name = p == null ? null : _display.GetValue(p, null) as string;

                if (string.IsNullOrEmpty(name)) { _failedAt = Game.GameTime; return null; }

                _known[key] = name;
                return name;
            }
            catch (Exception ex)
            {
                // NPC Mind loaded but not ready yet -- its tables come in off disk on its own
                // first tick, and ours may run before that. Asked again shortly.
                _failedAt = Game.GameTime;
                Log.Debug("NPC Mind would not name one yet: " + ex.Message);
                return null;
            }
        }
    }
}
