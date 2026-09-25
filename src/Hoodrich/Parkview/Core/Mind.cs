// GENERATED -- DO NOT EDIT. This is Parkview, copied in by tools/sync-parkview.py from
// C:\projects\parkview\src\Parkview\Core\Mind.cs. Change it there; the next build overwrites this.
using System;
using System.Collections.Generic;
using System.Reflection;
using GTA;

namespace Hoodrich.Parkview.Core
{
    /// <summary>
    /// What NPC Mind will call somebody, and whether it has him right now -- asked of NPC Mind
    /// itself.
    ///
    /// WHY THIS RATHER THAN A LIST OF OUR OWN. NPC Mind does not keep names; it DERIVES them,
    /// every time, from two things -- the ped's model and the number he is known by -- in
    /// Persona.Create(modelName, seed). For a ped another mod vouched for, the seed IS the
    /// npcmind_id we stamp on him (see Folk and GtaNpcMind Persistence/IdentityRegistry). So
    /// his name is already decided the moment we stamp him, and any name this mod made up on
    /// its own would be a second, different name: the tag over his head saying Dee while he
    /// introduced himself as Marcus Johnson.
    ///
    /// So the tag asks the same two methods NPC Mind asks, with the same two inputs:
    /// PedModelNames.NameOf(model hash) for the model string -- which is how NPC Mind's own
    /// targeter and body store get it, never our HashName, which for some of the scene is a
    /// display label like "Families CA Male" -- and Persona.Create(that, seed).
    ///
    /// AND WHO IT HAS. Once one of ours can be talked to, NPC Mind runs him for as long as it
    /// has him -- turned to you, talking, and then doing what he decided about you -- and the
    /// scene must keep its hands off him meanwhile. Api.Dialogue says who: Holding for the
    /// whole of it, Partner on an NPC Mind from before Holding, Target for the man its own tag
    /// is over. See Scenery.Taken.
    ///
    /// BY REFLECTION AND NOTHING ELSE. Parkview references the BCL and ScriptHookVDotNet and
    /// that is all; a reference to GtaNpcMind would mean Parkview will not load without it.
    /// Found once by name in whatever is loaded, which by our first tick is every script in
    /// the folder. Absent, or not answering, and every answer here is "nobody" and the scene
    /// runs as it always did.
    /// </summary>
    internal static class Mind
    {
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
                    if (asm.GetName().Name != "GtaNpcMind") continue;
                    mind = asm;
                    break;
                }

                if (mind == null)
                {
                    Log.Info("NPC Mind is not loaded; the scene uses its own names and runs its own people.");
                    return;
                }

                Names(mind);
                Dialogue(mind);
            }
            catch (Exception ex)
            {
                _create = null;
                Log.Debug("Could not look for NPC Mind: " + ex.Message);
            }
        }

        private static void Names(Assembly mind)
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
                Log.Info("NPC Mind does not name people the way it used to; the scene uses its own names.");
                return;
            }

            Log.Info("NPC Mind is here: the scene's people go by the names it gives them.");
        }

        private static void Dialogue(Assembly mind)
        {
            var api = mind.GetType("GtaNpcMind.Api.Dialogue", false);

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
                    : "This NPC Mind does not say who it is talking to; the scene may re-task somebody mid-sentence.");
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
        /// somebody. The context key is both its talk key and our doors' key, and one press
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
