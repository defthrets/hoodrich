using System;
using System.Collections.Generic;
using System.IO;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Locations
{
    /// <summary>
    /// What somebody built in the Object Spooner, stood up again every session.
    ///
    /// THE SPOONER IS THE LEVEL EDITOR. Placing a man against a wall with a rifle on his
    /// shoulder takes a minute in Menyoo and you can see him while you do it; writing the same
    /// man into a C# file takes ten and you cannot. So the deal is: arrange it in the spooner,
    /// save it, and this reads the file and builds it -- out of the mod's own scenery folder
    /// and, because that is where Menyoo already saves, straight out of menyooStuff\Spooner as
    /// well. Nothing to copy and nothing to convert.
    ///
    /// STREAMED, AND BUILT A FEW AT A TIME. A folder of scenes is a few hundred peds and props,
    /// and a few hundred of anything standing permanently is a frame rate rather than a mod.
    /// Each FILE is a scene with a centre and a radius worked out from what is in it, built
    /// when you come within range and taken out again once you are well past.
    ///
    /// And it is built ACROSS TICKS, a handful at a time, asking the streamer for each model
    /// without waiting on it. The blocking form of that wait ENDS THE TICK -- which for this
    /// mod means every rectangle it draws is missing for as long as the wait, so a scene
    /// arriving would black out the phone, the toasts and the corner readout together. See
    /// Core.Models: measured at up to 967 milliseconds in a single tick.
    ///
    /// AND EVERY PED IS DOING SOMETHING. A spooner ped saved with no scenario and no animation
    /// stands there with its arms at its sides, which reads as broken. Where the file says what
    /// it is doing it does that; where it does not, it is given something that suits what it is
    /// holding -- armed stands guard, unarmed hangs out, smokes, drinks or is on the phone --
    /// and the same one every session, because it is picked from where it stands.
    /// </summary>
    internal sealed class Scenery
    {
        private readonly Settings _cfg;

        public Scenery(Settings cfg)
        {
            _cfg = cfg;

            // A capture leaves the floor tiles out. See Paving.
            Spooner.Ignore = handle => _tiles.Contains(handle);
        }

        // ---- the shape of it -------------------------------------------------------

        /// <summary>A ped whose animation dictionary has not arrived yet.</summary>
        private sealed class Waiting
        {
            public Ped Who;
            public Spooner.Placed What;
            public int GiveUpAt;
        }

        private sealed class Scene
        {
            public string Name = "";
            public string Path = "";
            public List<Spooner.Placed> Items = new List<Spooner.Placed>();

            /// <summary>Map props this scene takes OUT. See Spooner.Hidden and Vanish.</summary>
            public List<Spooner.Hidden> Gone = new List<Spooner.Hidden>();

            /// <summary>Whether those holes are currently being held open.</summary>
            public bool Holed;

            public Vector3 Centre;
            public float Radius;

            /// <summary>Where the builder has got to, and whether it is part-way through.</summary>
            public int Cursor;
            public bool Working;
            public bool Built;

            public int Made;
            public int Missed;
            public int Waited;

            /// <summary>What is standing, and what each placement's saved handle turned into.</summary>
            public readonly List<Entity> Up = new List<Entity>();
            public readonly Dictionary<int, Entity> ByHandle = new Dictionary<int, Entity>();

            /// <summary>The placements not built today, by their saved handle.</summary>
            public readonly HashSet<int> Skip = new HashSet<int>();

            /// <summary>The peds the file says never leave, by their saved handle. See Stays.</summary>
            public HashSet<int> Stays = new HashSet<int>();

            /// <summary>
            /// The props the file says to lay a floor under, by their saved handle, each with
            /// the height of its top where the file gave one and nothing where it did not.
            /// See Paving.
            /// </summary>
            public Dictionary<int, float?> Floors = new Dictionary<int, float?>();

            /// <summary>How many of its peds were not stood up because of the hour. See Living a little.</summary>
            public int AwayTonight;

            /// <summary>
            /// The placement each standing thing came from, by its handle in THIS session.
            ///
            /// Kept because a ped that stops being scenery has to be able to go back to being
            /// scenery: the spot, the heading and the scenario it was doing are all in the
            /// placement, and none of them can be read back off a ped in the middle of a fight.
            /// </summary>
            public readonly Dictionary<int, Spooner.Placed> Was = new Dictionary<int, Spooner.Placed>();

            /// <summary>Peds still waiting on an animation dictionary.</summary>
            public readonly List<Waiting> Waits = new List<Waiting>();

            /// <summary>The props that have been looked at for a body. See Solid.</summary>
            public readonly HashSet<int> Solid = new HashSet<int>();
        }

        private readonly List<Scene> _scenes = new List<Scene>();

        /// <summary>
        /// Whether that handle is something a scene of ours put there.
        ///
        /// FOR ANYBODY WHO WANTS TO CLEAR UP AROUND IT. The gun counter tidies away weapon
        /// objects left near the bench by an earlier run of the script -- and the rifle laid on
        /// that crate in Menyoo is a weapon object too, sat right where the tidying happens. It
        /// is not the counter's job to know a scene from a stray; it is this file's, because
        /// this file put one of them there.
        ///
        /// ASKED OF THE HANDLE IT HAS TODAY, WHICH IS THE POINT AND WAS THE BUG.
        ///
        /// This used to ask ByHandle, and ByHandle is keyed by the handle SAVED IN THE FILE --
        /// a number out of somebody else's Menyoo session that has no meaning in this one. So
        /// it answered no to everything, every time, and the tidy-up ate the rifle this method
        /// exists to spare. Was is the map with today's handles in it; its own comment says so.
        ///
        /// Up is scanned after it as a second net. Was is written for every single thing that
        /// goes up and only Forget takes an entry out -- a ped who has walked off for a while,
        /// whose handle the game may hand to somebody else -- so it should never come to that.
        /// But the cost of being wrong here is somebody's hand-placed prop deleted out from
        /// under them, and that is worth a loop over a few dozen entities.
        /// </summary>
        public bool Mine(int handle)
        {
            if (handle == 0) return false;

            for (var i = 0; i < _scenes.Count; i++)
            {
                var scene = _scenes[i];

                if (scene.Was.ContainsKey(handle)) return true;

                for (var j = 0; j < scene.Up.Count; j++)
                {
                    var e = scene.Up[j];
                    if (e != null && e.Handle == handle) return true;
                }
            }

            return false;
        }

        private bool _read;

        /// <summary>
        /// The files still to be read, and which of them came from Menyoo's folder.
        ///
        /// READ A FEW A TICK, NOT ALL AT ONCE. One player's spooner folder held sixty-odd
        /// downloaded maps -- a hospital with fifteen hundred props, a basement with as many,
        /// seven versions of the same haunted city -- and reading the lot in one tick held it
        /// for five seconds, which is ScriptHookVDotNet's timeout to the millisecond. The next
        /// tick's first wait tipped it over and the script was killed, which the player saw
        /// as the mod crashing on load. So the list is made in Load and worked through here,
        /// a slice of a frame at a time, and the scenes go up as they arrive.
        /// </summary>
        private readonly List<string> _pending = new List<string>();
        private readonly HashSet<string> _theirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private const int ReadBudgetMs = 20;

        /// <summary>
        /// The most a scene from MENYOO'S folder may reach or hold and still be built.
        ///
        /// A scene builds when you are within range of its EDGE, so one that reaches five
        /// kilometres builds everywhere, and a folder of downloaded maps is a folder of those:
        /// eighty peds and fifty props each, standing up all over the map the moment the game
        /// loads. The mod's own scenery folder has no limit -- what ships was measured -- but
        /// a street scene somebody saved in Menyoo is a corner, not a county.
        /// </summary>
        private const float MenyooReachMost = 250f;
        private const int MenyooItemsMost = 200;
        private int _nextLook;
        private int _nextSolid;
        private const int SolidEveryMs = 700;

        /// <summary>How often the ranges are checked, and how many placements go up per tick while building.</summary>
        private const int LookEveryMs = 700;
        private const int PerTick = 3;

        /// <summary>How far past the scene's own edge it stays standing before it is taken out.</summary>
        private const float DropSlack = 90f;

        /// <summary>How many ticks one placement waits for its model before it is given up on.</summary>
        private const int ModelTries = 60;

        /// <summary>How long a ped waits for its animation dictionary before it is left on the scenario.</summary>
        private const int AnimWaitMs = 6000;

        // ---- reading the folder ----------------------------------------------------

        /// <summary>
        /// The spooner saves a scene file says it was made from: the names after "absorbs:"
        /// in its Note, without their extension. Nothing, for a file with no such note.
        /// </summary>
        private static List<string> Absorbed(string path)
        {
            var names = new List<string>();

            foreach (var line in Clauses(path))
            {
                if (!line.StartsWith("absorbs:", StringComparison.OrdinalIgnoreCase)) continue;

                foreach (var raw in line.Substring(8).Split(',', ';'))
                {
                    var name = raw.Trim();
                    if (name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
                    if (name.Length > 0) names.Add(name);
                }
            }

            return names;
        }

        /// <summary>
        /// The placements a scene file says are only there some days: "sometimes 60: 463969,
        /// 504650" in its Note is those two saved handles, six days in ten. The roll is one
        /// per file per day, so the two go missing together, and the same all day.
        /// </summary>
        private static HashSet<int> Sometimes(string path, out int chance)
        {
            var handles = new HashSet<int>();
            chance = 100;

            foreach (var line in Clauses(path))
            {
                if (!line.StartsWith("sometimes", StringComparison.OrdinalIgnoreCase)) continue;

                var colon = line.IndexOf(':');
                if (colon < 0) continue;

                int pct;
                if (int.TryParse(line.Substring(9, colon - 9).Trim(), out pct)) chance = Math.Max(0, Math.Min(100, pct));

                foreach (var raw in line.Substring(colon + 1).Split(',', ';'))
                {
                    int handle;
                    if (int.TryParse(raw.Trim(), out handle)) handles.Add(handle);
                }
            }

            return handles;
        }

        /// <summary>
        /// The placements a scene file says never leave: "stays: 463969, 504650" in its Note
        /// is those two saved handles stood on their marks whatever the hour, taking no walks.
        /// For the man behind a counter and the two on the pipe at the camp.
        /// </summary>
        private static HashSet<int> Stays(string path)
        {
            var handles = new HashSet<int>();

            foreach (var line in Clauses(path))
            {
                if (!line.StartsWith("stays:", StringComparison.OrdinalIgnoreCase)) continue;

                foreach (var raw in line.Substring(6).Split(',', ';'))
                {
                    int handle;
                    if (int.TryParse(raw.Trim(), out handle)) handles.Add(handle);
                }
            }

            return handles;
        }

        /// <summary>
        /// The placements a scene file says need a floor laid under them: "floors: 204034,
        /// 204290" in its Note is those two saved handles. For a slab whose model has no
        /// collision of its own. "204034@30.85" says where the top of it is, as a height in
        /// the world, for a model whose bounding box is taller than the ground you see --
        /// which the first one was, by four metres. See Paving.
        /// </summary>
        private static Dictionary<int, float?> Floors(string path)
        {
            var floors = new Dictionary<int, float?>();

            foreach (var line in Clauses(path))
            {
                if (!line.StartsWith("floors:", StringComparison.OrdinalIgnoreCase)) continue;

                foreach (var raw in line.Substring(7).Split(',', ';'))
                {
                    var part = raw.Trim();
                    float? top = null;

                    var at = part.IndexOf('@');

                    if (at >= 0)
                    {
                        float z;
                        if (float.TryParse(part.Substring(at + 1).Trim(), System.Globalization.NumberStyles.Float,
                                           System.Globalization.CultureInfo.InvariantCulture, out z))
                        {
                            top = z;
                        }

                        part = part.Substring(0, at).Trim();
                    }

                    int handle;
                    if (int.TryParse(part, out handle)) floors[handle] = top;
                }
            }

            return floors;
        }

        /// <summary>The lines of a scene file's Note, trimmed. Nothing, for a file without one.</summary>
        private static List<string> Clauses(string path)
        {
            var lines = new List<string>();

            try
            {
                var text = File.ReadAllText(path);
                var open = text.IndexOf("<Note>", StringComparison.OrdinalIgnoreCase);
                if (open < 0) return lines;

                var close = text.IndexOf("</Note>", open, StringComparison.OrdinalIgnoreCase);
                if (close < 0) return lines;

                foreach (var raw in text.Substring(open + 6, close - open - 6).Split('\n', '|'))
                {
                    var line = raw.Trim();
                    if (line.Length > 0) lines.Add(line);
                }
            }
            catch
            {
                // A file that cannot be read says nothing.
            }

            return lines;
        }

        /// <summary>
        /// Every scene file, from the mod's own folder and from Menyoo's.
        ///
        /// A file in the mod's scenery folder wins over one of the same name in Menyoo's, so a
        /// scene can be taken into the mod to ship without it then being built twice.
        /// </summary>
        public void Load()
        {
            _read = true;
            _scenes.Clear();

            if (_cfg != null && !_cfg.Scenery) return;

            var files = new List<string>();

            try
            {
                var ours = Paths.Scenery;
                if (Directory.Exists(ours)) files.AddRange(Directory.GetFiles(ours, "*.xml", SearchOption.AllDirectories));
            }
            catch (Exception ex)
            {
                Log.Debug("Could not look in the scenery folder: " + ex.Message);
            }

            // WHAT THE SHIPPED FILES HAVE ABSORBED. A scene taken into the mod is usually
            // built out of two or three spooner saves, merged and then edited -- a sign
            // taken off a wall, a model swapped for one with collision -- and the saves it
            // came from are still in Menyoo's folder, still read, and still put the sign
            // back. Not by the same-name rule, because the names differ, and not by the
            // same-spot rule, because the thing was taken out. So a shipped file names the
            // saves it was made from in its Note -- "absorbs: newnew, newlamar" -- and
            // those are skipped from then on.
            var absorbed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in files)
            {
                foreach (var name in Absorbed(path))
                {
                    if (!absorbed.ContainsKey(name)) absorbed[name] = Path.GetFileName(path);
                }
            }

            if (_cfg == null || _cfg.SceneryFromMenyoo)
            {
                try
                {
                    var root = Names.GameRoot();
                    var theirs = string.IsNullOrEmpty(root) ? "" : Path.Combine(root, @"menyooStuff\Spooner");

                    if (Directory.Exists(theirs))
                    {
                        foreach (var path in Directory.GetFiles(theirs, "*.xml", SearchOption.AllDirectories))
                        {
                            string by;

                            if (absorbed.TryGetValue(Path.GetFileNameWithoutExtension(path), out by))
                            {
                                Log.Info("Scenery: " + Path.GetFileName(path) + " in the spooner folder is skipped -- " +
                                         by + " absorbed it.");
                                continue;
                            }

                            files.Add(path);
                            _theirs.Add(path);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not look in the spooner folder: " + ex.Message);
                }
            }

            _pending.Clear();
            _seen.Clear();
            _pending.AddRange(files);

            if (_pending.Count == 0)
            {
                Log.Info("No scenery files. Save an Object Spooner placement in Menyoo and it is built from then on.");
            }
        }

        /// <summary>A few of the pending files, within the tick's budget. See _pending.</summary>
        private void ReadSome()
        {
            if (_pending.Count == 0) return;

            var clock = System.Diagnostics.Stopwatch.StartNew();

            while (_pending.Count > 0 && clock.ElapsedMilliseconds < ReadBudgetMs)
            {
                var path = _pending[0];
                _pending.RemoveAt(0);

                try
                {
                    ReadOne(path);
                }
                catch (Exception ex)
                {
                    Log.Warn("Could not read " + Path.GetFileName(path) + ": " + ex.Message);
                }
            }
        }

        private void ReadOne(string path)
        {
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (!_seen.Add(name)) return;

                var items = Spooner.Read(path);
                var gone = Spooner.Gone(path);

                // A FILE MAY BE NOTHING BUT REMOVALS. That is what hidden.xml is -- the one
                // the settings screen writes when you take a map prop out by hand -- and
                // refusing a file with no placements in it would have thrown it away.
                if (items.Count == 0 && gone.Count == 0) return;

                // ANYTHING STUCK TO SOMETHING GOES UP LAST, so the thing it is stuck to is
                // already standing when its turn comes. Sorting once here is the whole of the
                // ordering problem; the builder itself can then just walk the list.
                items.Sort((a, b) => (a.Attached ? 1 : 0) - (b.Attached ? 1 : 0));

                var scene = new Scene { Name = name, Path = path, Items = items, Gone = gone };
                Measure(scene);

                try { scene.Stays = Stays(path); }
                catch { /* everybody takes their walks */ }

                try { scene.Floors = Floors(path); }
                catch { /* nothing gets a floor */ }

                int peds = 0, props = 0, cars = 0;

                foreach (var one in items)
                {
                    if (one.What == Spooner.Kind.Ped) peds++;
                    else if (one.What == Spooner.Kind.Vehicle) cars++;
                    else props++;
                }

                var count = peds + " ped(s), " + props + " prop(s), " + cars + " vehicle(s)" +
                            (gone.Count > 0 ? ", " + gone.Count + " removal(s)" : "");

                // TOO BIG TO BE A STREET SCENE. Menyoo's folder only; see MenyooReachMost.
                if (_theirs.Contains(path) && (scene.Radius > MenyooReachMost || items.Count > MenyooItemsMost))
                {
                    Log.Info("Scene \"" + name + "\" skipped: " + count + ", reaching " +
                             scene.Radius.ToString("0") + " m -- too big for a street scene. The limit is " +
                             MenyooReachMost.ToString("0") + " m and " + MenyooItemsMost +
                             " things for a file in Menyoo's folder; the mod's own scenery folder has none.");
                    return;
                }

                _scenes.Add(scene);

                Log.Info("Scene \"" + name + "\": " + count +
                         ", around " + scene.Centre.X.ToString("0") + ", " +
                         scene.Centre.Y.ToString("0") + " and " + scene.Radius.ToString("0") + " m out.");
            }
        }

        /// <summary>Where the scene is and how far it reaches, from what is in it.</summary>
        private static void Measure(Scene scene)
        {
            var sum = Vector3.Zero;
            var many = 0;

            foreach (var item in scene.Items) { sum += item.At; many++; }

            // REMOVALS COUNT TOWARDS WHERE A SCENE IS, because a file can be nothing but
            // removals -- and a scene measured off an empty list sits at the origin with a
            // radius of nothing, which is a hole in the world held open in the sea.
            foreach (var hole in scene.Gone) { sum += hole.At; many++; }

            scene.Centre = sum / Math.Max(1, many);

            var far = 0f;

            foreach (var item in scene.Items)
            {
                var d = item.At.DistanceTo(scene.Centre);
                if (d > far) far = d;
            }

            foreach (var hole in scene.Gone)
            {
                var d = hole.At.DistanceTo(scene.Centre);
                if (d > far) far = d;
            }

            scene.Radius = far;
        }

        // ---- coming and going ------------------------------------------------------

        public void Update()
        {
            if (_cfg != null && !_cfg.Scenery)
            {
                if (_scenes.Count > 0) Clear();
                return;
            }

            if (!_read) Load();

            ReadSome();

            if (_scenes.Count == 0) return;

            var me = Game.Player.Character;
            if (me == null || !me.Exists()) return;

            var here = me.Position;

            // A scene part-way up is finished as fast as ticks allow; the ranges themselves are
            // only worth looking at now and then.
            var now = Game.GameTime;
            var lookNow = now >= _nextLook;
            if (lookNow) _nextLook = now + LookEveryMs;

            Rally(now);
            Given(now);
            Solid(now);
            Live(now);

            var range = _cfg == null ? 220f : _cfg.SceneryRange;

            foreach (var scene in _scenes)
            {
                if (scene.Waits.Count > 0) Waited(scene, now);

                if (scene.Working)
                {
                    Step(scene);
                    continue;
                }

                if (!lookNow) continue;

                var gap = here.DistanceTo(scene.Centre) - scene.Radius;

                if (!scene.Built)
                {
                    if (gap <= range) Begin(scene);
                    continue;
                }

                if (gap > range + DropSlack) Drop(scene);
            }
        }

        /// <summary>
        /// Start building. Every animation dictionary the scene needs is asked for here, at the
        /// front, so that by the time the peds are up the clips are usually already in.
        /// </summary>
        private static void Begin(Scene scene)
        {
            // The holes first, before anything is stood up. A prop placed where a map object
            // still is would spend the frame or two before the hide inside it.
            Vanish(scene, true);

            scene.Working = true;
            scene.Cursor = 0;
            scene.Made = 0;
            scene.Missed = 0;
            scene.Waited = 0;
            scene.AwayTonight = 0;

            // WHO IS NOT HERE TODAY. Rolled when the scene goes up, once per file per day,
            // so it is the same answer every time you come back on the same day.
            scene.Skip.Clear();

            try
            {
                int chance;
                var some = Sometimes(scene.Path, out chance);

                if (some.Count > 0 && !Core.Nights.On("the sometimes at " + scene.Name, chance))
                {
                    foreach (var handle in some) scene.Skip.Add(handle);
                    Log.Info("Scenery: " + some.Count + " of \"" + scene.Name + "\" not here today.");
                }
            }
            catch
            {
                // Everybody turns up.
            }

            foreach (var item in scene.Items)
            {
                if (string.IsNullOrEmpty(item.AnimDict)) continue;

                try { Function.Call(Hash.REQUEST_ANIM_DICT, item.AnimDict); }
                catch { /* asked for again when the ped is up */ }
            }

            if (scene.Floors.Count > 0)
            {
                foreach (var name in TileNames)
                {
                    try { Models.Ready(new Model(name)); }
                    catch { /* asked for again when the slab is looked at */ }
                }
            }
        }

        /// <summary>A few placements per tick, and never a wait that ends the tick.</summary>
        private void Step(Scene scene)
        {
            var did = 0;

            while (scene.Cursor < scene.Items.Count && did < PerTick)
            {
                var item = scene.Items[scene.Cursor];

                // Not today.
                if (scene.Skip.Count > 0 && scene.Skip.Contains(item.Handle))
                {
                    scene.Cursor++;
                    scene.Waited = 0;
                    continue;
                }

                // NOT AT THIS HOUR. A ped built in the small hours is not built: he is written
                // down as away and walks in when the hour passes, the same as one who was
                // stood here when it struck. Unless the file says he stays. See Living a little.
                if (item.What == Spooner.Kind.Ped && _quiet && LifeOn && !scene.Stays.Contains(item.Handle))
                {
                    _lives.Add(new Life
                    {
                        Scene = scene,
                        Item = item,
                        State = Stage.Away,
                        ForTheNight = true,
                        Scripted = Scripted(item)
                    });

                    scene.AwayTonight++;
                    scene.Cursor++;
                    scene.Waited = 0;
                    continue;
                }

                // ALREADY THERE, FROM ANOTHER FILE. Saving a scene, adding to it and saving
                // again under a new name is the obvious way to work, and it leaves the same
                // thing described twice in two files -- both of which get built. Two identical
                // frozen props on one spot z-fight, which reads as a flickering fence rather
                // than as a duplicate, so it is caught here rather than left to be noticed.
                if (Already(scene, item))
                {
                    scene.Cursor++;
                    scene.Waited = 0;
                    scene.Missed++;
                    continue;
                }

                Entity made;
                var verdict = Put(item, out made);

                if (verdict == Verdict.NotYet)
                {
                    // The streamer has not got to it. Come back next tick, up to a point.
                    scene.Waited++;

                    if (scene.Waited < ModelTries) return;

                    Log.Debug("Gave up waiting for " + Say(item) + " in \"" + scene.Name + "\".");
                    scene.Missed++;
                    scene.Cursor++;
                    scene.Waited = 0;
                    continue;
                }

                scene.Cursor++;
                scene.Waited = 0;
                did++;

                if (verdict == Verdict.No || made == null)
                {
                    scene.Missed++;
                    continue;
                }

                scene.Made++;
                Register(scene, item, made, false);
            }

            if (scene.Cursor < scene.Items.Count) return;

            scene.Working = false;
            scene.Built = true;

            Log.Info("Built \"" + scene.Name + "\": " + scene.Made + " up" +
                     (scene.Missed > 0 ? ", " + scene.Missed + " skipped or would not load" : "") +
                     (scene.AwayTonight > 0 ? ", " + scene.AwayTonight + " not about at this hour" : "") + ".");
        }

        /// <summary>
        /// One thing stood up, written into the scene's books -- and a ped given something to
        /// do and, unless he is walking in from somewhere, a life. See Living a little.
        /// </summary>
        private void Register(Scene scene, Spooner.Placed item, Entity made, bool arriving)
        {
            scene.Up.Add(made);
            scene.Was[made.Handle] = item;

            if (item.Handle != 0 && !scene.ByHandle.ContainsKey(item.Handle)) scene.ByHandle[item.Handle] = made;

            if (item.Attached) Stick(scene, item, made);

            var ped = made as Ped;
            if (ped == null) return;

            // Walking in: ComeBack gives him the walk, and his life is already on the list.
            if (arriving) return;

            Doing(scene, ped, item);

            // Built while it is already going off: it comes up fighting rather than
            // standing there smoking through a gun battle until the next war starts.
            if (_fighting && Ours(_ours, item)) Rouse(ped, _ours);

            if (!LifeOn) return;

            var life = new Life
            {
                Scene = scene,
                Item = item,
                Who = ped,
                State = Stage.Marked,
                Scripted = Scripted(item),
                Stays = scene.Stays.Contains(item.Handle)
            };

            life.NextAt = Game.GameTime + Beat(life);
            _lives.Add(life);
        }

        /// <summary>How close two of the same model have to be to count as the same thing.</summary>
        private const float SameSpot = 0.3f;

        // ==================================================================
        // Solid
        // ==================================================================

        /// <summary>
        /// Whether every prop standing actually has a body -- something a man walks into
        /// rather than through.
        ///
        /// A prop is stood up the moment its scene comes into range, up to two hundred
        /// metres out, with collision switched on; but a prop made before the game has
        /// streamed anything round it can come up with no physics at all, and one frozen
        /// in that state stays a picture of a fridge for the rest of the night. So each
        /// prop is looked at once the ground round it has loaded, and one without a body
        /// is given one: collision on again, physics woken, put back exactly where it was
        /// saved and frozen again. One that still has none is a model with no collision to
        /// give -- a neon sign, a line of powder -- and is written down by name so that is
        /// known rather than guessed. Nothing that already has a body is touched.
        /// </summary>
        private void Solid(int now)
        {
            if (now < _nextSolid) return;
            _nextSolid = now + SolidEveryMs;

            foreach (var scene in _scenes)
            {
                if (!scene.Built && !scene.Working) continue;

                foreach (var made in scene.Up)
                {
                    if (made == null || !(made is Prop)) continue;
                    if (scene.Solid.Contains(made.Handle)) continue;

                    try
                    {
                        if (!made.Exists())
                        {
                            scene.Solid.Add(made.Handle);
                            continue;
                        }

                        if (!Function.Call<bool>(Hash.HAS_COLLISION_LOADED_AROUND_ENTITY, made.Handle)) continue;

                        scene.Solid.Add(made.Handle);

                        if (Function.Call<bool>(Hash.DOES_ENTITY_HAVE_PHYSICS, made.Handle)) continue;

                        Spooner.Placed item;
                        scene.Was.TryGetValue(made.Handle, out item);

                        Function.Call(Hash.SET_ENTITY_COLLISION, made.Handle, true, true);
                        Function.Call(Hash.ACTIVATE_PHYSICS, made.Handle);

                        if (item != null)
                        {
                            Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, made.Handle,
                                          item.At.X, item.At.Y, item.At.Z, false, false, false);
                            Function.Call(Hash.SET_ENTITY_ROTATION, made.Handle,
                                          item.Pitch, item.Roll, item.Yaw, 2, true);

                            if (item.Frozen || !item.Gravity)
                            {
                                Function.Call(Hash.FREEZE_ENTITY_POSITION, made.Handle, true);
                                Function.Call(Hash.SET_ENTITY_DYNAMIC, made.Handle, false);
                            }
                        }

                        var given = Function.Call<bool>(Hash.DOES_ENTITY_HAVE_PHYSICS, made.Handle);
                        var name = item == null ? Names.Say(made.Model.Hash) : Say(item);

                        // A FLOOR LAID UNDER IT, where the file asks. Not yet, if the tile
                        // models are still streaming: the slab comes off the looked-at list
                        // so the next pass tries again.
                        if (!given && item != null && scene.Floors.ContainsKey(item.Handle))
                        {
                            var paved = Pave(scene, made, item);

                            if (paved == Verdict.NotYet)
                            {
                                scene.Solid.Remove(made.Handle);
                                continue;
                            }

                            if (paved == Verdict.Up) continue;
                        }

                        Log.Info("Scenery: " + name + " in " + scene.Name + " had no body; " +
                                 (given ? "it has one now." : "the model has no collision to give it." +
                                  (item != null && item.Handle != 0 && !scene.Floors.ContainsKey(item.Handle)
                                      ? " Name it in the file's Note -- floors: " + item.Handle + " -- and a floor is laid under it."
                                      : "")));
                    }
                    catch
                    {
                        scene.Solid.Add(made.Handle);
                    }
                }
            }
        }

        /// <summary>
        /// Whether this exact thing is already standing, from ANOTHER scene.
        ///
        /// The same model at the same place, within a third of a metre. Deliberately narrow:
        /// two of the same fence a metre apart is a fence line somebody built on purpose, and
        /// only a pair sat inside each other is a duplicate. A scene's own placements are
        /// never held against each other: two lines of powder four centimetres apart on a
        /// fridge were laid out that way on purpose, and the second was being skipped as a
        /// copy of the first.
        /// </summary>
        private bool Already(Scene mine, Spooner.Placed item)
        {
            foreach (var scene in _scenes)
            {
                if (ReferenceEquals(scene, mine)) continue;

                foreach (var pair in scene.Was)
                {
                    var was = pair.Value;
                    if (was == null || was.ModelHash != item.ModelHash) continue;
                    if (was.At.DistanceTo(item.At) > SameSpot) continue;

                    return true;
                }
            }

            return false;
        }

        private static void Stick(Scene scene, Spooner.Placed item, Entity made)
        {
            Entity to;
            if (!scene.ByHandle.TryGetValue(item.AttachedTo, out to) || to == null || !to.Exists()) return;

            try
            {
                Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, made.Handle, to.Handle,
                              item.Bone < 0 ? 0 : item.Bone,
                              item.AttachOffset.X, item.AttachOffset.Y, item.AttachOffset.Z,
                              item.AttachRotation.X, item.AttachRotation.Y, item.AttachRotation.Z,
                              false, false, false, false, 2, true);
            }
            catch
            {
                // It stands where it was saved instead.
            }
        }

        // ---- putting one thing down ------------------------------------------------

        private enum Verdict
        {
            Up,
            NotYet,
            No
        }

        /// <param name="where">
        /// Somewhere other than the mark to stand a PED up at: the far point he walks in
        /// from. Nothing for everything else, and no twin check there -- the mark is empty,
        /// that is the point.
        /// </param>
        private static Verdict Put(Spooner.Placed item, out Entity made, Vector3? where = null)
        {
            made = null;

            try
            {
                var model = item.Model;

                if (!model.IsValid || !model.IsInCdImage)
                {
                    Log.Debug("Nothing in the game called " + Say(item) + ".");
                    return Verdict.No;
                }

                // Asked for, not waited on. See the note at the top of the class.
                if (!Models.Ready(model)) return Verdict.NotYet;

                switch (item.What)
                {
                    case Spooner.Kind.Ped:
                        // ALREADY THERE. Somebody of this exact model stood within a metre of
                        // this mark is this placement, standing, and putting a second one
                        // inside him gives you the pair on the wall at Lamar's: two men in one
                        // coat, both frozen, one of them impossible to account for.
                        //
                        // The cause is not in this file -- the scene holds one of each, every
                        // save it absorbed is skipped, and the log builds it once per load --
                        // so this is deliberately a check on the WORLD rather than on our own
                        // bookkeeping. It answers "is there a man here already", which is the
                        // question that actually matters, whoever put him there.
                        //
                        // On the model as well as the spot, so a passer-by walking over a mark
                        // as the scene goes up does not quietly delete somebody from it.
                        if (where == null && Twin(item, model)) return Verdict.No;

                        made = Person(item, model, where ?? item.At);
                        break;

                    case Spooner.Kind.Vehicle:
                        made = Car(item, model);
                        break;

                    default:
                        made = Thing(item, model);
                        break;
                }

                model.MarkAsNoLongerNeeded();

                if (made == null || !made.Exists()) return Verdict.No;

                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, made.Handle, true, true);

                if (!item.Visible) made.IsVisible = false;
                if (item.Opacity >= 0 && item.Opacity < 255) made.Opacity = item.Opacity;
                if (item.Invincible) Function.Call(Hash.SET_ENTITY_INVINCIBLE, made.Handle, true);

                return Verdict.Up;
            }
            catch (Exception ex)
            {
                // AND IT GOES BACK, RATHER THAN STANDING THERE FOR EVER.
                //
                // By this point `made` may be a live ped or prop, already persistent. The
                // caller treats Verdict.No as "nothing was built" and never adds it to
                // scene.Up -- so Drop() and RestoreWorld() cannot find it, and one malformed
                // item near the player's route left another one behind on every single
                // rebuild of that scene.
                try
                {
                    if (made != null && made.Exists())
                    {
                        made.MarkAsNoLongerNeeded();
                        made.Delete();
                    }
                }
                catch
                {
                    // Nothing more we can do for it.
                }

                made = null;

                Log.Debug("A placement would not stand up (" + Say(item) + "): " + ex.Message);
                return Verdict.No;
            }
        }

        /// <summary>What to call it in the log: its own name where the file gave one, else the bench's.</summary>
        private static string Say(Spooner.Placed item)
        {
            return string.IsNullOrEmpty(item.ModelName) ? Names.Say(item.ModelHash) : item.ModelName;
        }

        private static Entity Thing(Spooner.Placed item, Model model)
        {
            // Its collision asked for by name as well as its model, so a prop stood up two
            // hundred metres out has a body to be given. Cheap, and no-op where it is loaded.
            try { Function.Call(Hash.REQUEST_COLLISION_FOR_MODEL, model.Hash); } catch { }

            var prop = World.CreateProp(model, item.At, false, false);
            if (prop == null || !prop.Exists()) return null;

            // NO OFFSET, or everything stands a metre in the air.
            //
            // Setting Position on an entity goes through SET_ENTITY_COORDS, which does not put
            // the entity where you asked -- it applies the game's own offset, and for a ped
            // that lifts it clear of the ground by about its own base. Menyoo saved these
            // coordinates with a plain read and restores them with SET_ENTITY_COORDS_NO_OFFSET,
            // so that is the only call that puts a scene back exactly as it was arranged.
            prop.PositionNoOffset = item.At;
            Function.Call(Hash.SET_ENTITY_ROTATION, prop.Handle, item.Pitch, item.Roll, item.Yaw, 2, true);
            Function.Call(Hash.SET_ENTITY_COLLISION, prop.Handle, true, true);

            if (item.Frozen || !item.Gravity)
            {
                prop.IsPositionFrozen = true;
                Function.Call(Hash.SET_ENTITY_DYNAMIC, prop.Handle, false);
            }

            Settle(prop, item);

            return prop;
        }

        /// <summary>
        /// TOLD WHICH ROOM IT IS IN, AND HOW FAR OFF TO DRAW. Both of these are why the first
        /// scene built INSIDE a house flickered.
        ///
        /// AN OBJECT A SCRIPT MAKES BELONGS TO NO ROOM. The game culls what is inside an
        /// interior through its portals -- doorways and windows -- and to do that every entity
        /// in there has to be assigned to a room. One created by CREATE_OBJECT is assigned to
        /// none, so the portal system has no answer for it and settles the question differently
        /// from frame to frame: drawn, culled, drawn. That is the flicker exactly, and it is
        /// why the six scenes before this one never showed it. Every one of them is outdoors.
        ///
        /// The room is read off the object itself once it exists -- it is standing at the
        /// coordinate, so the game can say which interior and which room that is -- and then
        /// forced, which is the difference between "it happens to resolve there" and "it is
        /// there". Nothing at all happens to a prop in the open air: the interior comes back as
        /// zero and this returns.
        ///
        /// AND THE DRAW DISTANCE, from the file. Menyoo writes one per placement and it was
        /// never read, so everything got whatever a script object is given by default -- which
        /// for a carton on a worktop is short enough to blink as you cross the kitchen.
        /// </summary>
        private static void Settle(Entity thing, Spooner.Placed item)
        {
            if (thing == null || !thing.Exists()) return;

            try
            {
                var lod = item == null || item.Lod <= 0 ? LodDefault : item.Lod;

                if (lod > LodMost) lod = LodMost;
                if (lod < LodLeast) lod = LodLeast;

                Function.Call(Hash.SET_ENTITY_LOD_DIST, thing.Handle, lod);
            }
            catch
            {
                // It draws at whatever the game gives it.
            }

            try
            {
                var inside = Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, thing.Handle);

                if (inside == 0) return;

                var room = Function.Call<int>(Hash.GET_ROOM_KEY_FROM_ENTITY, thing.Handle);

                Function.Call(Hash.FORCE_ROOM_FOR_ENTITY, thing.Handle, inside, room);
            }
            catch
            {
                // Outdoors, or an interior that will not say. It draws as it did before.
            }
        }

        /// <summary>
        /// What a scene thing is drawn from, when the file does not say.
        ///
        /// Menyoo writes 16960 on everything, which is its way of saying "always" -- honoured
        /// up to a ceiling, because a hundred props each insisting on being drawn from two
        /// kilometres away is a bill somebody pays in frames. Three hundred metres is further
        /// than any scene in this mod is visible from anyway.
        /// </summary>
        private const int LodDefault = 300;
        private const int LodLeast = 60;
        private const int LodMost = 500;

        private static Entity Car(Spooner.Placed item, Model model)
        {
            var car = World.CreateVehicle(model, item.At, item.Yaw);
            if (car == null || !car.Exists()) return null;

            car.PositionNoOffset = item.At;
            Function.Call(Hash.SET_ENTITY_ROTATION, car.Handle, item.Pitch, item.Roll, item.Yaw, 2, true);

            car.IsPersistent = true;

            if (item.Frozen) car.IsPositionFrozen = true;
            else Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, car.Handle);

            Settle(car, item);

            return car;
        }

        /// <summary>How close another one of him has to be to count as him.</summary>
        private const float TwinRange = 1.0f;

        /// <summary>
        /// Whether this placement is already up.
        ///
        /// Cheap on purpose: the ped list is only walked at the moment a scene is being built,
        /// which is a handful of frames when you arrive somewhere, and never after.
        /// </summary>
        private static bool Twin(Spooner.Placed item, Model model)
        {
            try
            {
                var near = World.GetNearbyPeds(item.At, TwinRange);
                if (near == null) return false;

                foreach (var other in near)
                {
                    if (other == null || !other.Exists()) continue;
                    if (other == Game.Player.Character) continue;

                    // NAMED EVEN WHEN IT IS NOT A MATCH. A mark that already has somebody on
                    // it who is NOT this placement is the interesting case: it means another
                    // system got there first, and knowing which model it put there is the
                    // whole of what is needed to find it. Info rather than Debug, because it
                    // should never happen twice and a log nobody turned up is no use.
                    if (other.Model.Hash != model.Hash)
                    {
                        Log.Info("Scenery: something else is stood on " + Say(item) +
                                 " -- model " + other.Model.Hash + ", " +
                                 other.Position.DistanceTo(item.At).ToString("0.00") + "m off the mark.");
                        continue;
                    }

                    Log.Info("Scenery: " + Say(item) + " is already stood there; not making a second.");
                    return true;
                }
            }
            catch
            {
                // If the world cannot be asked, build it -- an empty mark is worse than a pair.
            }

            return false;
        }

        private static Ped Person(Spooner.Placed item, Model model, Vector3 at)
        {
            var ped = World.CreatePed(model, at, item.Yaw);
            if (ped == null || !ped.Exists()) return null;

            ped.PositionNoOffset = at;
            ped.Heading = item.Yaw;
            ped.IsPersistent = true;

            // SET DRESSING, NOT A CROWD. It holds its spot: it does not startle, does not
            // wander off to a scenario of its own and does not join a fight two streets away.
            // Without this a spooner scene has emptied itself five minutes after you find it,
            // and the peds in the first real file are stood on corners holding rifles -- which
            // is exactly the kind of ped the game likes to send somewhere.
            ped.BlockPermanentEvents = true;
            Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, true);
            Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, ped.Handle, 0, false);
            Function.Call(Hash.SET_PED_CAN_RAGDOLL_FROM_PLAYER_IMPACT, ped.Handle, false);
            Function.Call(Hash.SET_PED_CONFIG_FLAG, ped.Handle, 17, true);   // Stays put.
            Function.Call(Hash.SET_PED_CONFIG_FLAG, ped.Handle, 208, true);  // Does not go looking for a scenario.

            if (item.Health > 0)
            {
                ped.MaxHealth = Math.Max(item.Health, ped.MaxHealth);
                ped.Health = item.Health;
            }

            if (item.Armour > 0) ped.Armor = item.Armour;

            Wear(ped, item);

            if (item.GroupHash != 0)
            {
                try { Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle, item.GroupHash); }
                catch { /* it keeps the group its model came with */ }
            }

            Arm(ped, item);

            // Frozen LAST, and only where the file said so. The animation still plays on a
            // frozen ped; what stops is it being shoved off its mark by traffic or by you.
            if (item.Frozen) ped.IsPositionFrozen = true;

            Settle(ped, item);

            return ped;
        }

        /// <summary>What the file said it had on. Anything that will not apply is skipped, not fatal.</summary>
        private static void Wear(Ped ped, Spooner.Placed item)
        {
            foreach (var c in item.Components)
            {
                try
                {
                    if (c[1] < 0) continue;
                    Function.Call(Hash.SET_PED_COMPONENT_VARIATION, ped.Handle, c[0], c[1], c[2] < 0 ? 0 : c[2], 0);
                }
                catch
                {
                }
            }

            foreach (var c in item.Props)
            {
                try
                {
                    if (c[1] < 0) Function.Call(Hash.CLEAR_PED_PROP, ped.Handle, c[0]);
                    else Function.Call(Hash.SET_PED_PROP_INDEX, ped.Handle, c[0], c[1], c[2] < 0 ? 0 : c[2], true);
                }
                catch
                {
                }
            }
        }

        /// <summary>
        /// The weapon, for anybody saved with one.
        ///
        /// Given rather than only equipped, so it is still there if anything ever wakes them,
        /// and put away in the holster when they have something else to do with their hands --
        /// a scenario or an animation puts a phone or a cigarette in there, and a rifle in the
        /// same hands at the same time is the clipping you see in every spooner scene that was
        /// saved with a weapon on.
        /// </summary>
        private static void Arm(Ped ped, Spooner.Placed item)
        {
            if (!item.Armed) return;

            try
            {
                // NOT THE GUN IN THE FILE. Saved with a rifle, every one of them, because a
                // rifle is what you reach for in the spooner; what he actually carries is one
                // of the guard guns, by where he stands, so a block of ten is a mix. See Arms.
                var gun = Arms.GuardHashAt(item.At);

                Function.Call(Hash.GIVE_WEAPON_TO_PED, ped.Handle, gun, 250, false, true);

                var busy = !string.IsNullOrEmpty(item.Scenario) ||
                           (!string.IsNullOrEmpty(item.AnimDict) && !string.IsNullOrEmpty(item.AnimClip));

                Function.Call(Hash.SET_CURRENT_PED_WEAPON, ped.Handle,
                              busy ? Spooner.Placed.Unarmed : gun, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not arm a placement: " + ex.Message);
            }
        }

        // ---- what it is doing ------------------------------------------------------

        /// <summary>
        /// What a ped is given when the file did not say: an ANIMATION, not a scenario.
        ///
        /// A scenario was the old answer and it is the wrong shape here. A scenario is the
        /// game's own behaviour -- it fetches a prop, it wants a spot it approves of, and it
        /// can decide it has finished and hand the ped back to standing. Half the peds in
        /// these scenes already carry an animation somebody picked in the spooner, so the ones
        /// that do not should be the same KIND of thing rather than a different system that
        /// happens to look similar from a distance.
        ///
        /// STANDING ONES ONLY, and every pair here is checked against the game's own animation
        /// list rather than remembered. A dictionary or a clip the game does not have plays
        /// nothing at all, which looks exactly like a ped that was never given anything.
        /// </summary>
        private static readonly string[][] MenIdle =
        {
            new[] { "amb@world_human_hang_out_street@male_a@idle_a", "idle_a" },
            new[] { "amb@world_human_hang_out_street@male_a@idle_a", "idle_b" },
            new[] { "amb@world_human_hang_out_street@male_a@idle_a", "idle_c" },
            new[] { "amb@world_human_smoking@male@male_a@idle_a", "idle_a" },
            new[] { "amb@world_human_smoking@male@male_a@idle_a", "idle_c" },
            new[] { "amb@world_human_drug_dealer_hard@male@idle_a", "idle_a" },
            new[] { "amb@world_human_drug_dealer_hard@male@idle_a", "idle_c" },
            new[] { "amb@world_human_stand_impatient@male@no_sign@idle_a", "idle_a" },
            new[] { "amb@world_human_stand_mobile@male@text@idle_a", "idle_a" },
            new[] { "amb@world_human_stand_mobile@male@standing@call@idle_a", "idle_a" }
        };

        private static readonly string[][] WomenIdle =
        {
            new[] { "amb@world_human_hang_out_street@female_hold_arm@idle_a", "idle_a" },
            new[] { "amb@world_human_hang_out_street@female_hold_arm@idle_a", "idle_b" },
            new[] { "amb@world_human_hang_out_street@female_arms_crossed@idle_a", "idle_a" },
            new[] { "amb@world_human_hang_out_street@female_arm_side@idle_a", "idle_a" },
            new[] { "amb@world_human_smoking@female@idle_a", "idle_a" },
            new[] { "amb@world_human_smoking@female@idle_a", "idle_c" },
            new[] { "amb@world_human_stand_mobile@female@text@idle_a", "idle_a" },
            new[] { "amb@world_human_stand_mobile@female@standing@call@idle_a", "idle_a" }
        };

        /// <summary>
        /// Somebody holding a rifle stands like somebody holding a rifle.
        ///
        /// The dealer idle rather than the guard scenario it used to be: it is a man stood on
        /// a street with his hands where a man's hands go, which is what these are, and it
        /// does not try to take the weapon off him to hold something else.
        /// </summary>
        private static readonly string[] Armed =
        {
            "amb@world_human_drug_dealer_hard@male@idle_a", "idle_b"
        };

        /// <param name="turn">
        /// Which of his idles, for a ped the file gave nothing: the first every session as
        /// before, and the next along each time a beat changes what he is doing.
        /// </param>
        private static void Doing(Scene scene, Ped ped, Spooner.Placed item, int turn = 0)
        {
            try
            {
                Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, ped.Handle);

                // What the file said, first: it is somebody's decision and it wins.
                if (!string.IsNullOrEmpty(item.AnimDict) && !string.IsNullOrEmpty(item.AnimClip))
                {
                    if (Play(ped, item)) return;

                    // Not in yet. Stand it on the scenario it would otherwise have had, so it
                    // is never stood there with its arms down, and swap to the animation the
                    // moment the dictionary arrives.
                    Stand(ped, item, turn);

                    Function.Call(Hash.REQUEST_ANIM_DICT, item.AnimDict);

                    scene.Waits.Add(new Waiting
                    {
                        Who = ped,
                        What = item,
                        GiveUpAt = Game.GameTime + AnimWaitMs
                    });

                    return;
                }

                Stand(ped, item, turn);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not give a placement something to do: " + ex.Message);
            }
        }

        private static bool Play(Ped ped, Spooner.Placed item)
        {
            if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, item.AnimDict)) return false;

            Function.Call(Hash.TASK_PLAY_ANIM, ped.Handle, item.AnimDict, item.AnimClip,
                          8f, -8f, -1, item.AnimFlag, 0f, false, false, false);

            return true;
        }

        private static void Stand(Ped ped, Spooner.Placed item, int turn = 0)
        {
            // A scenario the FILE asked for is still a scenario. Somebody picked it in the
            // spooner and it is not this code's place to substitute something of its own.
            if (!string.IsNullOrEmpty(item.Scenario))
            {
                Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, ped.Handle, item.Scenario, 0, true);
                Function.Call(Hash.SET_PED_KEEP_TASK, ped.Handle, true);
                return;
            }

            // SAME PED, SAME IDLE, EVERY TIME. Taken from where it stands rather than from a
            // dice roll, so a corner you walk past twice is the same corner.
            string[] pick;

            if (item.Armed)
            {
                pick = Armed;
            }
            else
            {
                var man = true;

                try { man = Function.Call<bool>(Hash.IS_PED_MALE, ped.Handle); }
                catch { /* the men's list, which is the longer of the two */ }

                var list = man ? MenIdle : WomenIdle;
                pick = list[(Steady(item.At) + turn) % list.Length];
            }

            Give(ped, pick[0], pick[1]);
        }

        /// <summary>
        /// One idle onto one ped, waiting on its dictionary without ending the tick.
        ///
        /// When the dictionary never arrives the ped is left standing rather than handed
        /// something else. A wrong animation is harder to notice than none at all, and none is
        /// what says the name was wrong.
        /// </summary>
        private static void Give(Ped ped, string dict, string clip)
        {
            try
            {
                Function.Call(Hash.REQUEST_ANIM_DICT, dict);

                if (Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dict))
                {
                    Function.Call(Hash.TASK_PLAY_ANIM, ped.Handle, dict, clip,
                                  8f, -8f, -1, 1, 0f, false, false, false);
                    return;
                }

                Soon.Add(new Later
                {
                    Who = ped,
                    Dict = dict,
                    Clip = clip,
                    GiveUpAt = Game.GameTime + AnimWaitMs
                });
            }
            catch (Exception ex)
            {
                Log.Debug("Could not give an idle: " + ex.Message);
            }
        }

        /// <summary>A ped whose idle is still streaming in.</summary>
        private sealed class Later
        {
            public Ped Who;
            public string Dict;
            public string Clip;
            public int GiveUpAt;
        }

        private static readonly List<Later> Soon = new List<Later>();

        /// <summary>The idles still waiting on a dictionary, looked at once a tick.</summary>
        private static void Given(int now)
        {
            for (var i = Soon.Count - 1; i >= 0; i--)
            {
                var wait = Soon[i];

                try
                {
                    if (wait.Who == null || !wait.Who.Exists()) { Soon.RemoveAt(i); continue; }

                    if (Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, wait.Dict))
                    {
                        Function.Call(Hash.TASK_PLAY_ANIM, wait.Who.Handle, wait.Dict, wait.Clip,
                                      8f, -8f, -1, 1, 0f, false, false, false);

                        Soon.RemoveAt(i);
                        continue;
                    }

                    if (now < wait.GiveUpAt) continue;

                    Log.Debug("The idle " + wait.Dict + " / " + wait.Clip + " never loaded.");
                    Soon.RemoveAt(i);
                }
                catch
                {
                    Soon.RemoveAt(i);
                }
            }
        }

        /// <summary>The peds still waiting on a dictionary, looked at once a tick.</summary>
        private static void Waited(Scene scene, int now)
        {
            for (var i = scene.Waits.Count - 1; i >= 0; i--)
            {
                var wait = scene.Waits[i];

                try
                {
                    if (wait.Who == null || !wait.Who.Exists())
                    {
                        scene.Waits.RemoveAt(i);
                        continue;
                    }

                    if (Play(wait.Who, wait.What))
                    {
                        scene.Waits.RemoveAt(i);
                        continue;
                    }

                    if (now < wait.GiveUpAt) continue;

                    Log.Debug("The animation " + wait.What.AnimDict + " / " + wait.What.AnimClip +
                              " never loaded; it stays on the scenario.");

                    scene.Waits.RemoveAt(i);
                }
                catch
                {
                    scene.Waits.RemoveAt(i);
                }
            }
        }

        /// <summary>A number from a position that does not change between sessions.</summary>
        private static int Steady(Vector3 at)
        {
            var n = (int)(Math.Abs(at.X) * 73f) + (int)(Math.Abs(at.Y) * 31f) + (int)(Math.Abs(at.Z) * 17f);
            return n < 0 ? -n : n;
        }

        // ---- when it goes off round here -------------------------------------------

        /// <summary>Whose side the scenery is on while a war is running, and whether one is.</summary>
        private Gangs.GangDef _ours;
        private bool _fighting;
        private int _nextRally;

        /// <summary>How often a defender is told again to go and find somebody.</summary>
        private const int RallyEveryMs = 5000;

        /// <summary>How far a defender will look for somebody to fight.</summary>
        private const float LookFor = 80f;

        /// <summary>
        /// A war starts or ends on the block, and the set's own people stood around on it stop
        /// being furniture.
        ///
        /// ONLY THE SET'S OWN, and only the ones with the set's models -- which is the whole
        /// test, because a spooner scene is whatever somebody placed and half of it might be
        /// civilians, rivals or a man with a bicycle. The models come from the gang's own list
        /// rather than from anything in the file, so a scene full of Ballas standing in
        /// Chamberlain does not turn out to be on your side the moment it kicks off.
        ///
        /// Their blocking comes off, they are put in the set's relationship group so the game
        /// itself knows who they hate, and they are sent after whoever is hated nearby. When it
        /// is over they go back to the mark, the heading and the scenario in their placement.
        /// </summary>
        public void Defend(Gangs.GangDef ours, bool on)
        {
            _ours = ours;
            _fighting = on && ours != null;
            _nextRally = 0;

            foreach (var scene in _scenes)
            {
                foreach (var e in scene.Up)
                {
                    var ped = e as Ped;
                    if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

                    Spooner.Placed was;
                    if (!scene.Was.TryGetValue(ped.Handle, out was)) continue;

                    if (!Ours(ours, was)) continue;

                    if (_fighting) Rouse(ped, ours);
                    else { Settle(ped, was); Rested(ped); }
                }
            }

            if (_fighting) Log.Info("Scenery: the set's own are in it.");
        }

        /// <summary>Whether this placement is one of the set's own, by its model.</summary>
        private static bool Ours(Gangs.GangDef ours, Spooner.Placed was)
        {
            if (ours == null || was == null) return false;

            foreach (var model in ours.MemberModels)
            {
                if (string.IsNullOrEmpty(model)) continue;
                if (unchecked((uint)was.ModelHash) == Names.Joaat(model)) return true;
            }

            return false;
        }

        private static void Rouse(Ped ped, Gangs.GangDef ours)
        {
            try
            {
                ped.IsPositionFrozen = false;
                ped.BlockPermanentEvents = false;

                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);
                Function.Call(Hash.SET_PED_CONFIG_FLAG, ped.Handle, 17, false);
                Function.Call(Hash.SET_PED_CONFIG_FLAG, ped.Handle, 208, false);
                Function.Call(Hash.SET_PED_CAN_RAGDOLL_FROM_PLAYER_IMPACT, ped.Handle, true);

                if (ours.GroupHash != 0)
                {
                    Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle, ours.GroupHash);
                }

                // Nobody who has stood on a corner all night is a marksman. Middling ability
                // and poor accuracy, so they are a nuisance to the other lot rather than a
                // firing squad -- and so the fight is still yours to win.
                Function.Call(Hash.SET_PED_ACCURACY, ped.Handle, 25);
                Function.Call(Hash.SET_PED_COMBAT_ABILITY, ped.Handle, 1);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 46, true);  // Will fight rather than flee.
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 5, true);   // Uses cover.
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 2, true);   // Can do drivebys, if in one.

                Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);
                Function.Call(Hash.TASK_COMBAT_HATED_TARGETS_AROUND_PED, ped.Handle, LookFor, 0);
            }
            catch (Exception ex)
            {
                Log.Debug("One of the set would not join in: " + ex.Message);
            }
        }

        /// <summary>Back to the mark, the heading and whatever it was doing before it kicked off.</summary>
        private static void Settle(Ped ped, Spooner.Placed was)
        {
            try
            {
                Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);

                ped.PositionNoOffset = was.At;
                ped.Heading = was.Yaw;

                ped.BlockPermanentEvents = true;
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, true);
                Function.Call(Hash.SET_PED_CONFIG_FLAG, ped.Handle, 17, true);
                Function.Call(Hash.SET_PED_CONFIG_FLAG, ped.Handle, 208, true);

                if (was.Frozen) ped.IsPositionFrozen = true;

                Stand(ped, was);
            }
            catch (Exception ex)
            {
                Log.Debug("One of the set would not go back to standing: " + ex.Message);
            }
        }

        /// <summary>
        /// While it is going off: anybody who has run out of things to do is sent looking again.
        ///
        /// A combat task ENDS when the thing it was about is dead or gone, and a ped whose task
        /// ended goes back to standing there -- in the middle of a firefight, which reads as
        /// somebody who has decided it is not their problem. Asked again every few seconds.
        /// </summary>
        private void Rally(int now)
        {
            if (!_fighting || _ours == null) return;
            if (now < _nextRally) return;

            _nextRally = now + RallyEveryMs;

            foreach (var scene in _scenes)
            {
                foreach (var e in scene.Up)
                {
                    var ped = e as Ped;
                    if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

                    Spooner.Placed was;
                    if (!scene.Was.TryGetValue(ped.Handle, out was)) continue;
                    if (!Ours(_ours, was)) continue;

                    try
                    {
                        if (Function.Call<bool>(Hash.IS_PED_IN_COMBAT, ped.Handle, 0)) continue;

                        Function.Call(Hash.TASK_COMBAT_HATED_TARGETS_AROUND_PED, ped.Handle, LookFor, 0);
                    }
                    catch
                    {
                        // He stays where he is.
                    }
                }
            }
        }

        // ==================================================================
        // Living a little
        // ==================================================================

        /// <summary>
        /// A scene ped with somewhere to be other than his mark.
        ///
        /// SET DRESSING THAT BREATHES. Everything above this line stands a ped on a mark and
        /// keeps him there, which is right for a scene and wrong for a person: ten men who
        /// have not shifted their weight in four hours read as mannequins the second time you
        /// walk past. Michael asked on 2026-09-21 for the people in the scenes to be people --
        /// idle like people, walk off sometimes, not be stood on a corner at four in the
        /// morning.
        ///
        /// SO EVERY PED HAS A NEXT BEAT, a minute to four away on the real clock, and at the
        /// beat one of three things happens: he changes what he is doing; he stretches his
        /// legs -- walks somewhere a few metres off, stands there a while, walks back; or he
        /// walks off altogether, out of sight, and is back on his mark some minutes later,
        /// walking in from wherever he went. The file's own choices still win: a man the
        /// spooner gave a scenario or an animation comes back to it, never swaps it for one of
        /// ours, and takes his walks half as often.
        ///
        /// AND IN THE SMALL HOURS NOBODY IS THERE. Between QuietFrom and QuietTo on the game's
        /// clock everybody walks off, staggered over a couple of minutes so it is a corner
        /// emptying rather than a cut, and drifts back the same way once it is over. A scene
        /// built during those hours goes up without its people, who arrive when the hour does.
        /// A file can name the ones who stay -- "stays: 463969" in its Note -- for the man
        /// behind the counter and the two on the pipe.
        ///
        /// ALWAYS ON FOOT, NEVER A CUT. Nobody is put anywhere he can be seen: a ped walking
        /// off is only deleted once he is off screen or well away, and one coming back is
        /// stood up at a far point that is off screen and walks in from it. Follow him all the
        /// way and he waits there until he is not being watched.
        ///
        /// WHAT IT DOES NOT TOUCH. A fight takes the set's own out of this (Defend) and Settle
        /// hands them back with a fresh beat; while it is going off nobody starts a walk. A
        /// ped saved frozen is unfrozen for the walk and frozen again on the mark. The blocking
        /// of events stays on throughout -- they still do not startle, flee or pick fights of
        /// their own -- because the moment they do, a scene empties itself and stays empty,
        /// which is the thing this whole file exists to prevent.
        /// </summary>
        private sealed class Life
        {
            public Scene Scene;
            public Spooner.Placed Item;
            public Ped Who;
            public Stage State = Stage.Marked;

            /// <summary>When the next thing happens: the beat, the end of a stand-about, the walk's deadline, the return.</summary>
            public int NextAt;

            /// <summary>Where he is walking to, or was stood up at to walk in from.</summary>
            public Vector3 Going;

            /// <summary>Whether the file gave him something of its own to do, which is kept.</summary>
            public bool Scripted;

            /// <summary>Whether the file said he never leaves.</summary>
            public bool Stays;

            /// <summary>Whether he is away because of the hour rather than a whim.</summary>
            public bool ForTheNight;

            /// <summary>Which of his idles he is on. See Doing's turn.</summary>
            public int Turn;

            /// <summary>Walks that ran out of time and were asked for again.</summary>
            public int Tries;

            /// <summary>Stood at the far point, waiting to be unwatched before he goes.</summary>
            public bool Waiting;
        }

        private enum Stage
        {
            /// <summary>On the mark, doing his idle. NextAt is the next beat.</summary>
            Marked,

            /// <summary>Walking to Going, a few metres off.</summary>
            Strolling,

            /// <summary>Stood at Going. NextAt is when he heads back.</summary>
            Loitering,

            /// <summary>Walking back to the mark.</summary>
            Returning,

            /// <summary>Walking to Going, well off, to be deleted once nobody is looking.</summary>
            Leaving,

            /// <summary>Deleted. NextAt is when he is due back, unless it is the hour.</summary>
            Away,

            /// <summary>Stood up at Going, walking in to the mark.</summary>
            ComingBack
        }

        private readonly List<Life> _lives = new List<Life>();
        private int _nextBeat;
        private const int BeatEveryMs = 900;

        /// <summary>Whether it is the small hours, and whether that has been looked at yet this session.</summary>
        private bool _quiet;
        private bool _quietKnown;

        private bool LifeOn => _cfg == null || _cfg.SceneryLife;

        /// <summary>How long between beats for one ped: a minute to four on the real clock.</summary>
        private const int BeatLeastMs = 60000;
        private const int BeatMostMs = 240000;

        /// <summary>How far a stroll goes, and how far a walk-off goes.</summary>
        private const float StrollLeast = 4f;
        private const float StrollMost = 12f;
        private const float LeaveLeast = 45f;
        private const float LeaveMost = 70f;

        /// <summary>How long a stroll's stand-about lasts.</summary>
        private const int LoiterLeastMs = 20000;
        private const int LoiterMostMs = 60000;

        /// <summary>How long a walk-off lasts before he is due back.</summary>
        private const int AwayLeastMs = 180000;
        private const int AwayMostMs = 480000;

        /// <summary>How long a walk is given before it is asked for again, and how near counts as there.</summary>
        private const int WalkMs = 45000;
        private const float There = 1.6f;

        /// <summary>Beyond this from the player, and unseen, a man walking off simply goes.</summary>
        private const float GoneAt = 35f;

        /// <summary>A man coming back is stood up no nearer the player than this.</summary>
        private const float ArriveNoNearer = 25f;

        /// <summary>A whole scene leaving or coming back is spread over this long.</summary>
        private const int StaggerMs = 150000;

        private static readonly Random Dice = new Random();

        private static int Between(int least, int most)
        {
            return least + Dice.Next(Math.Max(1, most - least + 1));
        }

        private static bool Scripted(Spooner.Placed item)
        {
            return !string.IsNullOrEmpty(item.Scenario) ||
                   (!string.IsNullOrEmpty(item.AnimDict) && !string.IsNullOrEmpty(item.AnimClip));
        }

        /// <summary>A man the spooner gave something to do sticks with it twice as long.</summary>
        private static int Beat(Life l)
        {
            var ms = Between(BeatLeastMs, BeatMostMs);
            return l.Scripted ? ms * 2 : ms;
        }

        /// <summary>Whether the game's clock is inside the quiet hours.</summary>
        private bool QuietNow()
        {
            if (_cfg == null) return false;

            var from = _cfg.SceneryQuietFrom;
            var to = _cfg.SceneryQuietTo;
            if (from == to) return false;

            int hour;
            try { hour = Function.Call<int>(Hash.GET_CLOCK_HOURS); }
            catch { return false; }

            return from < to ? hour >= from && hour < to : hour >= from || hour < to;
        }

        /// <summary>Whether that spot is on screen right now.</summary>
        private static bool Seen(Vector3 at)
        {
            try
            {
                return Function.Call<bool>(Hash.IS_SPHERE_VISIBLE, at.X, at.Y, at.Z + 0.5f, 2f);
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// Somewhere to walk to, between least and most metres from a point.
        ///
        /// Pavement first, so a man walking off walks off down the street rather than across
        /// the middle of the road; anything on the navmesh after that, because half the scenes
        /// are on open ground with no pavement for forty metres. The nearest safe spot to a
        /// guess can be a long way from the guess, so the answer is measured, and one that is
        /// not in range is tried again from another bearing.
        /// </summary>
        private static bool Spot(Vector3 from, float least, float most, out Vector3 at)
        {
            at = Vector3.Zero;

            for (var tries = 0; tries < 6; tries++)
            {
                var bearing = Dice.NextDouble() * Math.PI * 2;
                var dist = least + (float)Dice.NextDouble() * (most - least);
                var guess = new Vector3(from.X + (float)Math.Cos(bearing) * dist,
                                        from.Y + (float)Math.Sin(bearing) * dist,
                                        from.Z);

                foreach (var flags in new[] { 1, 0 })
                {
                    var safe = new OutputArgument();

                    if (!Function.Call<bool>(Hash.GET_SAFE_COORD_FOR_PED, guess.X, guess.Y, guess.Z, true, safe, flags)) continue;

                    var found = safe.GetResult<Vector3>();
                    var d = found.DistanceTo(from);

                    if (d < least * 0.5f || d > most * 1.5f) continue;

                    at = found;
                    return true;
                }
            }

            return false;
        }

        private static bool Arrived(Ped ped, Vector3 at)
        {
            var d = ped.Position - at;
            d.Z = 0f;
            return d.Length() <= There;
        }

        private static void WalkTo(Ped ped, Vector3 to, float heading)
        {
            Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);
            Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, ped.Handle,
                          to.X, to.Y, to.Z, 1.0f, WalkMs, There, 0, heading);
            Function.Call(Hash.SET_PED_KEEP_TASK, ped.Handle, true);
        }

        /// <summary>Off the mark: unfrozen and allowed to move. The blocking of events stays on.</summary>
        private static void Loose(Life l)
        {
            var ped = l.Who;
            if (ped == null || !ped.Exists()) return;

            ped.IsPositionFrozen = false;
            Function.Call(Hash.SET_PED_CONFIG_FLAG, ped.Handle, 17, false);
        }

        /// <summary>Back on the mark exactly, frozen if the file said, doing his thing.</summary>
        private static void Home(Life l)
        {
            var ped = l.Who;
            if (ped == null || !ped.Exists()) return;

            Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);

            ped.PositionNoOffset = l.Item.At;
            ped.Heading = l.Item.Yaw;

            Function.Call(Hash.SET_PED_CONFIG_FLAG, ped.Handle, 17, true);
            if (l.Item.Frozen) ped.IsPositionFrozen = true;

            Doing(l.Scene, ped, l.Item, l.Turn);
        }

        /// <summary>Something to do while stood about somewhere that is not his mark.</summary>
        private static void LoiterIdle(Life l)
        {
            var ped = l.Who;
            if (ped == null || !ped.Exists()) return;

            string[] pick;

            if (l.Item.Armed)
            {
                pick = Armed;
            }
            else
            {
                var man = true;
                try { man = Function.Call<bool>(Hash.IS_PED_MALE, ped.Handle); }
                catch { /* the men's list */ }

                var list = man ? MenIdle : WomenIdle;
                pick = list[Dice.Next(list.Length)];
            }

            Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);
            Give(ped, pick[0], pick[1]);
        }

        /// <summary>Gone, from the world and from the books. The life stays, as Away.</summary>
        private static void Forget(Life l)
        {
            var ped = l.Who;
            l.Who = null;
            if (ped == null) return;

            try
            {
                var scene = l.Scene;
                scene.Up.Remove(ped);
                scene.Was.Remove(ped.Handle);

                if (l.Item.Handle != 0)
                {
                    Entity by;
                    if (scene.ByHandle.TryGetValue(l.Item.Handle, out by) && by != null && by.Handle == ped.Handle)
                    {
                        scene.ByHandle.Remove(l.Item.Handle);
                    }
                }

                if (ped.Exists())
                {
                    ped.MarkAsNoLongerNeeded();
                    ped.Delete();
                }
            }
            catch
            {
                // Already gone.
            }
        }

        /// <summary>A fight is over and this one is back on his mark: his next beat is from now.</summary>
        private void Rested(Ped ped)
        {
            foreach (var l in _lives)
            {
                if (l.Who == null || l.Who.Handle != ped.Handle) continue;

                l.State = Stage.Marked;
                l.Waiting = false;
                l.NextAt = Game.GameTime + Beat(l);
                return;
            }
        }

        /// <summary>
        /// The hour has turned: everybody's next move is inside the stagger rather than
        /// wherever his beat happened to fall, so the corner empties over a couple of
        /// minutes and fills the same way.
        /// </summary>
        private void Nudge(Life l, int now)
        {
            if (l.Stays) return;

            if (_quiet)
            {
                if (l.State == Stage.Marked || l.State == Stage.Loitering) l.NextAt = now + Dice.Next(StaggerMs);
                return;
            }

            if (l.State == Stage.Away)
            {
                l.ForTheNight = false;
                l.NextAt = now + Dice.Next(StaggerMs);
            }
        }

        /// <summary>Every life, once every BeatEveryMs. See the note on Life.</summary>
        private void Live(int now)
        {
            if (!LifeOn)
            {
                // Switched off mid-session: everybody who is about goes back to standing on
                // his mark. Anybody away is back the next time his scene is built.
                if (_lives.Count > 0)
                {
                    foreach (var l in _lives)
                    {
                        try { if (l.Who != null && l.Who.Exists()) Home(l); }
                        catch { /* he stands where he is */ }
                    }

                    _lives.Clear();
                }

                return;
            }

            if (now < _nextBeat) return;
            _nextBeat = now + BeatEveryMs;

            var me = Game.Player.Character;
            if (me == null || !me.Exists()) return;

            var here = me.Position;
            var quiet = QuietNow();

            if (!_quietKnown || quiet != _quiet)
            {
                var flipped = _quietKnown;
                _quiet = quiet;
                _quietKnown = true;

                if (flipped)
                {
                    Log.Info(quiet
                        ? "Scenery: the small hours -- everybody drifts off."
                        : "Scenery: the small hours are over -- everybody drifts back.");

                    foreach (var l in _lives) Nudge(l, now);
                }
            }

            for (var i = _lives.Count - 1; i >= 0; i--)
            {
                var l = _lives[i];

                try
                {
                    if (!Pulse(l, now, here)) _lives.RemoveAt(i);
                }
                catch (Exception ex)
                {
                    Log.Debug("One of the scenery's would not live: " + ex.Message);
                    _lives.RemoveAt(i);
                }
            }
        }

        /// <summary>One look at one life. False when it is over and should be forgotten.</summary>
        private bool Pulse(Life l, int now, Vector3 here)
        {
            var scene = l.Scene;

            // The scene came down: its people went with it, and so does this.
            if (!scene.Built && !scene.Working) return false;

            if (l.State == Stage.Away)
            {
                // Caught by the hour while away on a whim: he stays away until it passes.
                if (_quiet && !l.Stays) { l.ForTheNight = true; return true; }
                if (now < l.NextAt) return true;

                return ComeBack(l, now, here);
            }

            var ped = l.Who;
            if (ped == null || !ped.Exists() || !ped.IsAlive) return false;

            // A fight has him. Settle hands him back, and Rested gives him a fresh beat.
            if (_fighting && Ours(_ours, l.Item)) return true;

            switch (l.State)
            {
                case Stage.Marked:
                    if (_quiet && !l.Stays)
                    {
                        if (now >= l.NextAt) Leave(l, now, true);
                        return true;
                    }

                    if (now < l.NextAt) return true;

                    // Nobody starts a walk while it is going off round here.
                    if (_fighting) { l.NextAt = now + 20000; return true; }

                    Choose(l, now);
                    return true;

                case Stage.Strolling:
                    if (Arrived(ped, l.Going) || now >= l.NextAt)
                    {
                        l.State = Stage.Loitering;
                        l.NextAt = now + Between(LoiterLeastMs, LoiterMostMs);
                        LoiterIdle(l);
                    }

                    return true;

                case Stage.Loitering:
                    if (_quiet && !l.Stays) { Leave(l, now, true); return true; }
                    if (now < l.NextAt) return true;

                    WalkTo(ped, l.Item.At, l.Item.Yaw);
                    l.State = Stage.Returning;
                    l.NextAt = now + WalkMs;
                    l.Tries = 0;
                    return true;

                case Stage.Returning:
                case Stage.ComingBack:
                    if (Arrived(ped, l.Item.At))
                    {
                        Home(l);
                        l.State = Stage.Marked;
                        l.NextAt = now + Beat(l);
                        return true;
                    }

                    if (now < l.NextAt) return true;

                    // Out of time. Unwatched, or three walks in, he is simply put on the mark;
                    // watched, he is asked to walk again.
                    if (!ped.IsOnScreen || l.Tries >= 2)
                    {
                        Home(l);
                        l.State = Stage.Marked;
                        l.NextAt = now + Beat(l);
                        return true;
                    }

                    l.Tries++;
                    WalkTo(ped, l.Item.At, l.Item.Yaw);
                    l.NextAt = now + WalkMs;
                    return true;

                case Stage.Leaving:
                    {
                        var far = ped.Position.DistanceTo(here);
                        var there = Arrived(ped, l.Going) || now >= l.NextAt;

                        if ((there || far > GoneAt) && !ped.IsOnScreen)
                        {
                            var forTheNight = l.ForTheNight;
                            Forget(l);
                            l.State = Stage.Away;
                            l.Waiting = false;
                            l.NextAt = forTheNight ? 0 : now + Between(AwayLeastMs, AwayMostMs);
                            return true;
                        }

                        // Got there, still being watched: he stands about until he is not.
                        if (there && !l.Waiting)
                        {
                            l.Waiting = true;
                            LoiterIdle(l);
                        }

                        return true;
                    }
            }

            return true;
        }

        /// <summary>The beat: change what he is doing, stretch his legs, or walk off for a bit.</summary>
        private void Choose(Life l, int now)
        {
            var canVary = !l.Scripted && !l.Item.Armed;
            var vary = canVary ? 40 : 0;
            var stroll = canVary ? 35 : 60;
            var roll = Dice.Next(100);

            if (roll < vary)
            {
                l.Turn++;
                Doing(l.Scene, l.Who, l.Item, l.Turn);
                l.NextAt = now + Beat(l);
                return;
            }

            if (roll < vary + stroll || l.Stays)
            {
                Stroll(l, now);
                return;
            }

            Leave(l, now, false);
        }

        private void Stroll(Life l, int now)
        {
            Vector3 to;

            if (!Spot(l.Item.At, StrollLeast, StrollMost, out to))
            {
                l.NextAt = now + Beat(l);
                return;
            }

            Loose(l);
            WalkTo(l.Who, to, (float)(Dice.NextDouble() * 360.0));

            l.Going = to;
            l.State = Stage.Strolling;
            l.NextAt = now + WalkMs;
        }

        private void Leave(Life l, int now, bool forTheNight)
        {
            var ped = l.Who;
            Vector3 to;

            if (!Spot(l.Item.At, LeaveLeast, LeaveMost, out to))
            {
                // Nowhere to walk to. He is simply not there once nobody is looking.
                if (ped != null && ped.Exists() && !ped.IsOnScreen)
                {
                    Forget(l);
                    l.State = Stage.Away;
                    l.ForTheNight = forTheNight;
                    l.NextAt = forTheNight ? 0 : now + Between(AwayLeastMs, AwayMostMs);
                }
                else
                {
                    l.NextAt = now + 15000;
                }

                return;
            }

            Loose(l);
            WalkTo(ped, to, 0f);

            l.Going = to;
            l.State = Stage.Leaving;
            l.ForTheNight = forTheNight;
            l.Waiting = false;
            l.NextAt = now + WalkMs;

            Log.Debug(Say(l.Item) + " in " + l.Scene.Name + (forTheNight ? " walks off for the night." : " walks off for a bit."));
        }

        /// <summary>
        /// Stood up somewhere off screen, a way off, and walked in to the mark. False when he
        /// is given up on for this build of the scene.
        /// </summary>
        private bool ComeBack(Life l, int now, Vector3 here)
        {
            if (_fighting && Ours(_ours, l.Item)) { l.NextAt = now + 20000; return true; }

            Vector3 from;

            if (!Spot(l.Item.At, LeaveLeast, LeaveMost, out from) || Seen(from) || from.DistanceTo(here) < ArriveNoNearer)
            {
                // Every way in is in view. Try again shortly, from another side.
                l.NextAt = now + 15000;
                return true;
            }

            Entity made;
            var verdict = Put(l.Item, out made, from);

            if (verdict == Verdict.NotYet) { l.NextAt = now + 2000; return true; }

            if (verdict == Verdict.No || made == null)
            {
                Log.Debug(Say(l.Item) + " in " + l.Scene.Name + " would not come back.");
                return false;
            }

            var ped = made as Ped;

            if (ped == null)
            {
                Log.Debug(Say(l.Item) + " in " + l.Scene.Name + " came back as something else.");
                return false;
            }

            l.Scene.Made++;
            Register(l.Scene, l.Item, made, true);

            l.Who = ped;
            Loose(l);
            WalkTo(ped, l.Item.At, l.Item.Yaw);

            l.State = Stage.ComingBack;
            l.ForTheNight = false;
            l.Tries = 0;
            l.NextAt = now + WalkMs;

            Log.Debug(Say(l.Item) + " in " + l.Scene.Name + " comes back.");
            return true;
        }

        // ==================================================================
        // Paving
        // ==================================================================

        /// <summary>
        /// A floor laid under a slab whose model has none.
        ///
        /// THE GROUND UNDER PARKVIEW IS A PICTURE. The apartment blocks there stand on two
        /// pieces of des_aptblock_root002, which is a destruction model -- the ground the
        /// game shows while a building comes down -- and destruction models carry no
        /// collision of their own; the map's static bounds do that job where the game uses
        /// them. Stood up by a script on open ground there is nothing underneath, and
        /// Michael asked on 2026-09-21 for the floors to be solid and not clip through.
        ///
        /// SO A FLOOR IS LAID: invisible building blocks -- the Bikers stunt blocks, which
        /// are plain boxes with a flat top and a full collision body -- placed so their tops
        /// sit exactly at the slab's top, inside its footprint, rotated with it. Nothing is
        /// assumed about sizes: the slab and the four block sizes are measured with
        /// GET_MODEL_DIMENSIONS when the slab is looked at, and the footprint is tiled
        /// largest block first, the leftover strips with smaller ones, so the edges are
        /// covered to within half the smallest block. A tile never stands proud of the slab
        /// by more than that.
        ///
        /// NAMED, NOT GUESSED. Only a placement the file names in its Note ("floors: 204034")
        /// gets one, because a destruction model can as easily be a wall as a floor, and a
        /// box laid at the top of a wall is a ledge in the air. The log says what to write
        /// when it finds a slab with no body that is not named.
        ///
        /// THE TILES ARE THE SCENE'S. They go into Up so they come down with it, into Solid so
        /// they are never "given a body" themselves, and into _tiles so a capture leaves
        /// them out and the gun counter's tidy-up knows they are ours.
        /// </summary>
        private static readonly string[] TileNames =
        {
            "bkr_prop_biker_bblock_xl1",
            "bkr_prop_biker_bblock_lrg1",
            "bkr_prop_biker_bblock_mdm1",
            "bkr_prop_biker_bblock_sml1"
        };

        /// <summary>The tiles standing, by handle, across every scene.</summary>
        private static readonly HashSet<int> _tiles = new HashSet<int>();

        /// <summary>The most tiles one slab gets, and the widest slab that gets any.</summary>
        private const int TilesMost = 160;
        private const float SlabMost = 90f;

        /// <summary>Whether the block sizes have been written down this session.</summary>
        private static bool _tilesSaid;

        private sealed class Tile
        {
            public Model Model;
            public float W, L;

            /// <summary>How far its top is above its origin.</summary>
            public float Top;

            /// <summary>Its place in the measured list, which is what a laid tile is written down as.</summary>
            public int Index;
        }

        private static bool Dimensions(Model model, out Vector3 min, out Vector3 max)
        {
            min = Vector3.Zero;
            max = Vector3.Zero;

            try
            {
                var lo = new OutputArgument();
                var hi = new OutputArgument();

                Function.Call(Hash.GET_MODEL_DIMENSIONS, model.Hash, lo, hi);

                min = lo.GetResult<Vector3>();
                max = hi.GetResult<Vector3>();

                return max.X - min.X > 0.1f && max.Y - min.Y > 0.1f;
            }
            catch
            {
                return false;
            }
        }

        private static Verdict Pave(Scene scene, Entity slab, Spooner.Placed item)
        {
            // The blocks, measured. Not one of them may still be streaming.
            var tiles = new List<Tile>();

            foreach (var name in TileNames)
            {
                var model = new Model(name);
                if (!model.IsValid) continue;
                if (!Models.Ready(model)) return Verdict.NotYet;

                Vector3 lo, hi;
                if (!Dimensions(model, out lo, out hi)) continue;

                tiles.Add(new Tile { Model = model, W = hi.X - lo.X, L = hi.Y - lo.Y, Top = hi.Z });
            }

            if (tiles.Count == 0)
            {
                Log.Info("Scenery: no floor for " + Say(item) + " in " + scene.Name + " -- none of the block models are on this install.");
                return Verdict.No;
            }

            // Largest first: the sort is on area, and the footprint goes to the biggest block that fits.
            tiles.Sort((a, b) => (b.W * b.L).CompareTo(a.W * a.L));
            for (var i = 0; i < tiles.Count; i++) tiles[i].Index = i;

            if (!_tilesSaid)
            {
                _tilesSaid = true;
                var sizes = "";
                foreach (var t in tiles) sizes += (sizes.Length > 0 ? ", " : "") + t.W.ToString("0.0") + " by " + t.L.ToString("0.0");
                Log.Info("Scenery: the floor blocks measure " + sizes + " m.");
            }

            Vector3 min, max;

            if (!Dimensions(slab.Model, out min, out max))
            {
                Log.Info("Scenery: no floor for " + Say(item) + " in " + scene.Name + " -- the model would not say how big it is.");
                return Verdict.No;
            }

            var width = max.X - min.X;
            var length = max.Y - min.Y;

            if (width > SlabMost || length > SlabMost)
            {
                Log.Info("Scenery: no floor for " + Say(item) + " in " + scene.Name + " -- " +
                         width.ToString("0.0") + " by " + length.ToString("0.0") + " m is a district, not a slab.");
                return Verdict.No;
            }

            if (Math.Abs(item.Pitch) > 3f || Math.Abs(item.Roll) > 3f)
            {
                Log.Info("Scenery: " + Say(item) + " in " + scene.Name + " is tilted " + item.Pitch.ToString("0") + "/" +
                         item.Roll.ToString("0") + " degrees; the floor under it is laid level.");
            }

            // WHERE THE TOP IS. The bounding box says one thing and the ground you see can be
            // another: the first slab's box stood four metres above its surface, so the floor
            // was laid in the air. The file's own number wins; failing that, the height the
            // scene's other props stand at inside the footprint, which is where somebody put
            // a couch down on it; failing that, the box, and the log says so.
            var said = scene.Floors[item.Handle];
            var guessed = said == null ? Surface(scene, item, min, max) : null;
            var how = said != null ? "from the file" : guessed != null ? "from what stands on it" : "from the bounding box";
            var top = said ?? guessed ?? item.At.Z + max.Z;

            var laid = new List<Vector3>();   // model-space centres, with the tile index in Z

            Fill(tiles, min.X, min.Y, max.X, max.Y, laid);

            if (laid.Count == 0)
            {
                Log.Info("Scenery: no floor for " + Say(item) + " in " + scene.Name + " -- too small for the smallest block.");
                return Verdict.No;
            }

            var rad = item.Yaw * Math.PI / 180.0;
            var cos = (float)Math.Cos(rad);
            var sin = (float)Math.Sin(rad);
            var made = 0;

            foreach (var at in laid)
            {
                var tile = tiles[(int)at.Z];
                var world = new Vector3(item.At.X + at.X * cos - at.Y * sin,
                                        item.At.Y + at.X * sin + at.Y * cos,
                                        top - tile.Top);

                try
                {
                    var block = World.CreateProp(tile.Model, world, false, false);
                    if (block == null || !block.Exists()) continue;

                    block.PositionNoOffset = world;
                    Function.Call(Hash.SET_ENTITY_ROTATION, block.Handle, 0f, 0f, item.Yaw, 2, true);
                    Function.Call(Hash.SET_ENTITY_COLLISION, block.Handle, true, true);
                    Function.Call(Hash.FREEZE_ENTITY_POSITION, block.Handle, true);
                    Function.Call(Hash.SET_ENTITY_DYNAMIC, block.Handle, false);
                    Function.Call(Hash.SET_ENTITY_VISIBLE, block.Handle, false, false);
                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, block.Handle, true, true);

                    scene.Up.Add(block);
                    scene.Solid.Add(block.Handle);
                    _tiles.Add(block.Handle);
                    made++;
                }
                catch (Exception ex)
                {
                    Log.Debug("A floor tile would not stand: " + ex.Message);
                }
            }

            Log.Info("Scenery: laid " + made + " tile(s) under " + Say(item) + " in " + scene.Name + " -- " +
                     width.ToString("0.0") + " by " + length.ToString("0.0") + " m, top at " + top.ToString("0.00") +
                     " " + how + (laid.Count >= TilesMost ? ", and ran out of tiles" : "") + ".");

            return made > 0 ? Verdict.Up : Verdict.No;
        }

        /// <summary>
        /// Where the surface of a slab is, from the props stood on it: the middle height of
        /// every other prop in the scene whose spot falls inside the slab's footprint and
        /// between its bottom and its top. A couch, a bin bag and a streetlight all have their
        /// origin at their base, so the middle of them is the ground. Nothing, with fewer than
        /// three to go on.
        /// </summary>
        private static float? Surface(Scene scene, Spooner.Placed slab, Vector3 min, Vector3 max)
        {
            var heights = new List<float>();
            var rad = -slab.Yaw * Math.PI / 180.0;
            var cos = (float)Math.Cos(rad);
            var sin = (float)Math.Sin(rad);

            foreach (var other in scene.Items)
            {
                if (other == slab || other.What != Spooner.Kind.Prop || other.Attached) continue;
                if (scene.Floors.ContainsKey(other.Handle)) continue;

                // Into the slab's own space: the offset from its origin, turned back by its yaw.
                var dx = other.At.X - slab.At.X;
                var dy = other.At.Y - slab.At.Y;
                var lx = dx * cos - dy * sin;
                var ly = dx * sin + dy * cos;

                if (lx < min.X || lx > max.X || ly < min.Y || ly > max.Y) continue;

                var lz = other.At.Z - slab.At.Z;
                if (lz < min.Z - 0.5f || lz > max.Z + 0.5f) continue;

                heights.Add(other.At.Z);
            }

            if (heights.Count < 3) return null;

            heights.Sort();
            return heights[heights.Count / 2];
        }

        /// <summary>
        /// A rectangle of the footprint, in the slab's own space, filled with blocks.
        ///
        /// The biggest block that fits goes down in a grid from the corner; what is left is
        /// two strips -- down the side, and along the top -- each filled the same way with
        /// whatever fits them. A strip narrower than the smallest block but wider than half
        /// of it gets one anyway, centred, standing proud by at most half a block; narrower
        /// than that it is left, which is a gap you would have to aim for.
        /// </summary>
        private static void Fill(List<Tile> tiles, float x0, float y0, float x1, float y1, List<Vector3> laid)
        {
            if (laid.Count >= TilesMost) return;

            var w = x1 - x0;
            var l = y1 - y0;
            if (w <= 0.05f || l <= 0.05f) return;

            Tile pick = null;
            foreach (var t in tiles)
            {
                if (t.W <= w + 0.01f && t.L <= l + 0.01f) { pick = t; break; }
            }

            if (pick == null)
            {
                // Nothing fits both ways. A strip: the smallest block, laid along whichever
                // way it does fit and centred across the other, the remainder getting one
                // more if it is at least half a block.
                var small = tiles[tiles.Count - 1];
                var cx = (x0 + x1) * 0.5f;
                var cy = (y0 + y1) * 0.5f;

                if (l >= small.L * 0.5f && w >= small.W)
                {
                    var n = (int)Math.Floor(w / small.W + 0.001f);
                    for (var i = 0; i < n && laid.Count < TilesMost; i++) laid.Add(new Vector3(x0 + (i + 0.5f) * small.W, cy, small.Index));
                    var rem = w - n * small.W;
                    if (rem >= small.W * 0.5f && laid.Count < TilesMost) laid.Add(new Vector3(x0 + n * small.W + rem * 0.5f, cy, small.Index));
                }
                else if (w >= small.W * 0.5f && l >= small.L)
                {
                    var n = (int)Math.Floor(l / small.L + 0.001f);
                    for (var j = 0; j < n && laid.Count < TilesMost; j++) laid.Add(new Vector3(cx, y0 + (j + 0.5f) * small.L, small.Index));
                    var rem = l - n * small.L;
                    if (rem >= small.L * 0.5f && laid.Count < TilesMost) laid.Add(new Vector3(cx, y0 + n * small.L + rem * 0.5f, small.Index));
                }
                else if (w >= small.W * 0.5f && l >= small.L * 0.5f)
                {
                    laid.Add(new Vector3(cx, cy, small.Index));
                }

                return;
            }

            var nx = (int)Math.Floor(w / pick.W + 0.001f);
            var ny = (int)Math.Floor(l / pick.L + 0.001f);
            var which = pick.Index;

            for (var i = 0; i < nx && laid.Count < TilesMost; i++)
            {
                for (var j = 0; j < ny && laid.Count < TilesMost; j++)
                {
                    laid.Add(new Vector3(x0 + (i + 0.5f) * pick.W, y0 + (j + 0.5f) * pick.L, which));
                }
            }

            // Down the side, then along the top, with the smaller blocks.
            var rest = tiles.GetRange(tiles.IndexOf(pick) + 1, tiles.Count - tiles.IndexOf(pick) - 1);
            if (rest.Count == 0) rest = new List<Tile> { pick };

            Fill(rest, x0 + nx * pick.W, y0, x1, y0 + ny * pick.L, laid);
            Fill(rest, x0, y0 + ny * pick.L, x1, y1, laid);
        }

        // ---- taking it out again ---------------------------------------------------

        /// <summary>
        /// The map props this scene takes out, taken out -- or put back.
        ///
        /// CREATE_MODEL_HIDE is the game's own answer and it is a SPHERE AND A MODEL rather
        /// than a handle, which is exactly what survives a session. The last argument is the
        /// network flag and it is false: this is a singleplayer mod and a hidden object that
        /// announces itself to nobody is the right kind of hidden.
        ///
        /// Held only while the scene is up. A hole kept open across the whole map would mean a
        /// bin missing from a street twelve blocks away that happens to share a model with one
        /// somebody deleted at Denise's -- which is the same reason the radius is small.
        /// </summary>
        private static void Vanish(Scene scene, bool on)
        {
            if (scene.Gone.Count == 0 || scene.Holed == on) return;

            foreach (var hole in scene.Gone)
            {
                try
                {
                    Function.Call(on ? Hash.CREATE_MODEL_HIDE : Hash.REMOVE_MODEL_HIDE,
                                  hole.At.X, hole.At.Y, hole.At.Z,
                                  hole.Radius, unchecked((int)hole.ModelHash), false);
                }
                catch
                {
                    // The next pass through here asks again.
                }
            }

            scene.Holed = on;
        }

        private static void Drop(Scene scene)
        {
            Vanish(scene, false);

            foreach (var e in scene.Up)
            {
                try
                {
                    if (e != null) _tiles.Remove(e.Handle);
                    if (e != null && e.Exists()) e.Delete();
                }
                catch
                {
                    // Already gone.
                }
            }

            scene.Up.Clear();
            scene.ByHandle.Clear();
            scene.Was.Clear();
            scene.Solid.Clear();
            scene.Waits.Clear();
            scene.Cursor = 0;
            scene.Waited = 0;
            scene.Working = false;
            scene.Built = false;
        }

        public void Clear()
        {
            foreach (var scene in _scenes) Drop(scene);
            _lives.Clear();
        }

        public void RestoreWorld()
        {
            Clear();
        }

        /// <summary>Read the folder again and stand it all up fresh. For the settings screen.</summary>
        public int Reload()
        {
            Clear();
            Load();

            var n = 0;
            foreach (var scene in _scenes) n += scene.Items.Count;

            return n;
        }

        /// <summary>How many scenes and how much of it is standing, for the settings screen to say.</summary>
        public string Tally()
        {
            if (!_read) return "not read yet";
            if (_scenes.Count == 0) return "nothing saved";

            var things = 0;
            var up = 0;

            foreach (var scene in _scenes)
            {
                things += scene.Items.Count;
                up += scene.Up.Count;
            }

            return _scenes.Count + (_scenes.Count == 1 ? " scene, " : " scenes, ") +
                   things + " placed, " + up + " standing";
        }
    }
}
