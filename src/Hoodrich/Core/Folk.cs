using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;

namespace Hoodrich.Core
{
    /// <summary>
    /// Telling NPC Mind who the people we put on the street actually are.
    ///
    /// The decorator name, its type, Mark and Number are a contract with NPC Mind
    /// (Integration/Vouched): a man is the same man tomorrow only if this turns him into the
    /// same number tomorrow.
    ///
    /// WHY ANY OF THIS IS NEEDED. Every ped this mod spawns is made with CREATE_PED and then
    /// marked persistent, which gives it population type 6 or 7 -- and NPC Mind refuses to
    /// talk to a ped of those types, on purpose, because that is the signal that a script
    /// placed it and owns it, and hijacking somebody else's mission ped is how a mission
    /// breaks. The rule is right. The consequence was not: it meant the single most
    /// interesting population in the city was the one population that mod would not say a word
    /// to. You could stand in front of any of the eighty people around Chamberlain Hills, press
    /// the talk key, and nothing would happen, with nothing on screen to say why.
    ///
    /// A DECORATOR, NOT A BRIDGE. The other three seams between these mods are late-bound
    /// reflection, which is right for asking a question once a frame. This is different: it has
    /// to be answerable by the OTHER mod, in its targeting loop, several times a second, about
    /// a ped belonging to a script that may not have loaded yet. A decorator has none of those
    /// problems -- it lives on the entity, the game carries it across the script boundary, and
    /// a ped nobody stamped simply answers zero. No assembly reference either way, no load
    /// order, no version handshake.
    ///
    /// THE NUMBER IS THE PERSON, and that is the part worth getting right. It is what their
    /// memory is filed under at the far end, so the man on that corner is the same man
    /// tomorrow if and only if he gets the same number tomorrow. Which means it must be
    /// derived from WHAT HE IS -- his crew and his corner, his slot in the scene, the shop he
    /// works at -- and never from anything the engine hands us. A ped handle is recycled within
    /// a minute and would file every session under a different stranger.
    ///
    /// Safe on an install without NPC Mind: it is a decorator on a ped nobody reads.
    ///
    /// AND THE DECORATOR NEVER WORKED, which is why there is a table as well. DECOR_REGISTER
    /// only takes while registration is open, and the game's own startup scripts register
    /// theirs and lock it before ScriptHookVDotNet loads a single script -- so on Michael's
    /// machine the register failed in every session from the first, the log said "Could not
    /// register" every time, and NPC Mind turned away every one of our people as another
    /// script's mission ped. He pressed E at the whole Families crew and got nothing.
    ///
    /// Every script in the scripts folder runs in one AppDomain, so the same vouch goes in a
    /// table in that domain's data, which needs no registration and no reference: see List.
    /// NPC Mind reads it whenever the decorator is not there (Integration/Vouched).
    /// </summary>
    internal static class Folk
    {
        /// <summary>
        /// NPC Mind's published name for this. Not ours, and never to be renamed from here --
        /// see GtaNpcMind Integration/Vouched, which is the other half of the contract.
        /// </summary>
        private const string Decor = "npcmind_id";

        /// <summary>Decorator type 3 is an int.</summary>
        private const int TypeInt = 3;

        private static bool _ready;
        private static bool _tried;
        private static int _stamped;

        /// <summary>
        /// Registers the decorator, once.
        ///
        /// Registering the same name and type twice is a no-op, so it does not matter which
        /// script in the folder gets there first.
        ///
        /// It can fail -- a script that calls DECOR_REGISTER_LOCK first closes registration for
        /// the session -- and nothing is done about that. The failure is survivable and silent
        /// in the right direction: nothing gets stamped, every ped answers zero at the far end,
        /// and both mods behave exactly as they did before this file existed.
        /// </summary>
        private static bool Ready()
        {
            if (_tried) return _ready;
            _tried = true;

            try
            {
                Function.Call(Hash.DECOR_REGISTER, Decor, TypeInt);
                _ready = Function.Call<bool>(Hash.DECOR_IS_REGISTERED_AS_TYPE, Decor, TypeInt);

                Log.Info(_ready
                    ? "Our people can be talked to: '" + Decor + "' is registered."
                    : "Could not register '" + Decor + "' -- the game locks decorators before " +
                      "scripts load. Our people are introduced through the shared table instead.");
            }
            catch (Exception ex)
            {
                _ready = false;
                Log.Debug("Could not register the people decorator: " + ex.Message);
            }

            return _ready;
        }

        /// <summary>
        /// Says who this ped is, for as long as anybody is listening.
        ///
        /// <paramref name="who"/> must describe the PERSON and be the same string in every
        /// session: "hao", "crew:2", "scenery:parkview-apartments:41", "posted:families:-172,-1620".
        /// Anything derived from a handle, a spawn order or a clock will give him a new life
        /// every time you reload, which is worse than not stamping him at all -- the far end
        /// would accumulate a thousand strangers who each met you once.
        /// </summary>
        public static void Stamp(Ped ped, string who)
        {
            if (ped == null || string.IsNullOrEmpty(who)) return;

            try
            {
                if (!ped.Exists()) return;

                var id = Number(who);

                // BOTH, and the table whatever happens to the decorator: see the note on the
                // class. The decorator is for an install where registration was left open.
                if (Ready()) Function.Call(Hash.DECOR_SET_INT, ped.Handle, Decor, id);
                List(ped, id);
                _stamped++;

                // PROOF IN THE LOG THAT IT RAN, because there is no other way to tell. A
                // decorator is invisible, the mod that reads it may not be installed, and the
                // symptom of this silently not working is identical to the symptom of it never
                // having been written: you press the talk key and nothing happens. The first
                // one is named so a wrong key shape shows up immediately; after that it counts.
                if (_stamped == 1) Log.Info("First of our people introduced: " + who + ".");
                else if (_stamped % 50 == 0) Log.Info(_stamped + " of our people introduced.");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not say who " + who + " is: " + ex.Message);
            }
        }

        /// <summary>Stamp somebody whose identity is the place they are standing.</summary>
        public static void StampAt(Ped ped, string what, Vector3 at)
        {
            Stamp(ped, Mark(what, at));
        }

        /// <summary>
        /// A key from a thing and a spot, rounded to the metre.
        ///
        /// ROUNDED ON PURPOSE. A mark comes out of a data file, so the coordinate is the same
        /// every session to the last decimal -- but a mark that gets nudged a few centimetres
        /// in a later edit should not give the man who stands on it a whole new identity and
        /// wipe what he remembers about you. A metre survives an edit.
        ///
        /// AND ALL THREE AXES, which the first version did not have. Two people of the same
        /// model standing within a metre of each other would otherwise share one key and
        /// therefore one memory, and this mod has deliberate PAIRS in its scenes -- two men on
        /// the same step, two women at the same rail. On the ground they were distinguishable
        /// and in the file they were the same person. The height separates the ones that are
        /// genuinely stacked and costs nothing for everybody else, since a mark's Z is
        /// authored too.
        /// </summary>
        public static string Mark(string what, Vector3 at)
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;

            return (what ?? "") + ":" +
                   ((int)System.Math.Round(at.X)).ToString(c) + "," +
                   ((int)System.Math.Round(at.Y)).ToString(c) + "," +
                   ((int)System.Math.Round(at.Z)).ToString(c);
        }

        /// <summary>
        /// The number for a name. FNV-1a, because it has to mean the same thing on every
        /// machine and in every session, which rules out anything involving GetHashCode --
        /// string hashing in .NET is randomised per process and would hand every player a
        /// different set of people.
        /// </summary>
        public static int Number(string who)
        {
            unchecked
            {
                var hash = 2166136261u;

                foreach (var c in who)
                {
                    hash ^= c;
                    hash *= 16777619u;
                }

                var n = (int)hash;

                // Zero is "nobody vouched for this one" at the far end, so it is the one value
                // this cannot return.
                return n == 0 ? 1 : n;
            }
        }

        /// <summary>How many we have introduced this session. For the log.</summary>
        public static int Stamped { get { return _stamped; } }

        // ---- the table ------------------------------------------------------------------

        /// <summary>The handles we have put on the table, so we only ever take our own off.</summary>
        private static readonly HashSet<int> _listed = new HashSet<int>();

        /// <summary>
        /// NPC Mind's shared table, made if nobody has made it yet. The contract is NPC
        /// Mind's (Integration/Vouched) and fixed: a Dictionary&lt;int, long&gt; in the
        /// AppDomain's data under the decorator's own name, ped handle to
        /// (uint)model hash &lt;&lt; 32 | (uint)id, locked round every read and write.
        /// </summary>
        private static Dictionary<int, long> Table()
        {
            var domain = AppDomain.CurrentDomain;
            var table = domain.GetData(Decor) as Dictionary<int, long>;
            if (table != null) return table;

            table = new Dictionary<int, long>();
            domain.SetData(Decor, table);
            return table;
        }

        /// <summary>
        /// Puts him on the table. THE MODEL GOES IN WITH HIM because a handle outlives the
        /// ped it was given for: if one of ours were ever missed on the way out, NPC Mind
        /// checks the model and will not take the man the game hands that handle to next for
        /// one of ours.
        /// </summary>
        private static void List(Ped ped, int id)
        {
            var table = Table();
            var entry = ((long)unchecked((uint)ped.Model.Hash) << 32) | unchecked((uint)id);

            lock (table) table[ped.Handle] = entry;
            _listed.Add(ped.Handle);
        }

        /// <summary>
        /// Takes ours off the table: the ones that no longer exist, or every one of them when
        /// <paramref name="all"/> -- which is the way out, when nobody is vouching for them
        /// any more. Cheap enough for every couple of seconds: one native per person.
        /// </summary>
        public static void Prune(bool all = false)
        {
            if (_listed.Count == 0) return;

            try
            {
                List<int> gone = null;

                foreach (var h in _listed)
                {
                    if (!all && Function.Call<bool>(Hash.DOES_ENTITY_EXIST, h)) continue;
                    if (gone == null) gone = new List<int>();
                    gone.Add(h);
                }

                if (gone == null) return;

                var table = AppDomain.CurrentDomain.GetData(Decor) as Dictionary<int, long>;

                foreach (var h in gone)
                {
                    _listed.Remove(h);
                    if (table != null) lock (table) table.Remove(h);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not tidy the people table: " + ex.Message);
            }
        }
    }
}
