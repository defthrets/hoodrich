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
        private bool _read;
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

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not look in the spooner folder: " + ex.Message);
                }
            }

            foreach (var path in files)
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (!seen.Add(name)) continue;

                var items = Spooner.Read(path);
                if (items.Count == 0) continue;

                // ANYTHING STUCK TO SOMETHING GOES UP LAST, so the thing it is stuck to is
                // already standing when its turn comes. Sorting once here is the whole of the
                // ordering problem; the builder itself can then just walk the list.
                items.Sort((a, b) => (a.Attached ? 1 : 0) - (b.Attached ? 1 : 0));

                var scene = new Scene { Name = name, Path = path, Items = items };
                Measure(scene);
                _scenes.Add(scene);

                int peds = 0, props = 0, cars = 0;

                foreach (var one in items)
                {
                    if (one.What == Spooner.Kind.Ped) peds++;
                    else if (one.What == Spooner.Kind.Vehicle) cars++;
                    else props++;
                }

                Log.Info("Scene \"" + name + "\": " + peds + " ped(s), " + props + " prop(s), " + cars +
                         " vehicle(s), around " + scene.Centre.X.ToString("0") + ", " +
                         scene.Centre.Y.ToString("0") + " and " + scene.Radius.ToString("0") + " m out.");
            }

            if (_scenes.Count == 0)
            {
                Log.Info("No scenery files. Save an Object Spooner placement in Menyoo and it is built from then on.");
            }
        }

        /// <summary>Where the scene is and how far it reaches, from what is in it.</summary>
        private static void Measure(Scene scene)
        {
            var sum = Vector3.Zero;

            foreach (var item in scene.Items) sum += item.At;

            scene.Centre = sum / Math.Max(1, scene.Items.Count);

            var far = 0f;

            foreach (var item in scene.Items)
            {
                var d = item.At.DistanceTo(scene.Centre);
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
            scene.Working = true;
            scene.Cursor = 0;
            scene.Made = 0;
            scene.Missed = 0;
            scene.Waited = 0;

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
                scene.Up.Add(made);
                scene.Was[made.Handle] = item;

                if (item.Handle != 0 && !scene.ByHandle.ContainsKey(item.Handle)) scene.ByHandle[item.Handle] = made;

                if (item.Attached) Stick(scene, item, made);

                var ped = made as Ped;

                if (ped != null)
                {
                    Doing(scene, ped, item);

                    // Built while it is already going off: it comes up fighting rather than
                    // standing there smoking through a gun battle until the next war starts.
                    if (_fighting && Ours(_ours, item)) Rouse(ped, _ours);
                }
            }

            if (scene.Cursor < scene.Items.Count) return;

            scene.Working = false;
            scene.Built = true;

            Log.Info("Built \"" + scene.Name + "\": " + scene.Made + " up" +
                     (scene.Missed > 0 ? ", " + scene.Missed + " skipped or would not load" : "") + ".");
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

                        Log.Info("Scenery: " + name + " in " + scene.Name + " had no body; " +
                                 (given ? "it has one now." : "the model has no collision to give it."));
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

        private static Verdict Put(Spooner.Placed item, out Entity made)
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
                        made = Person(item, model);
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

            return prop;
        }

        private static Entity Car(Spooner.Placed item, Model model)
        {
            var car = World.CreateVehicle(model, item.At, item.Yaw);
            if (car == null || !car.Exists()) return null;

            car.PositionNoOffset = item.At;
            Function.Call(Hash.SET_ENTITY_ROTATION, car.Handle, item.Pitch, item.Roll, item.Yaw, 2, true);

            car.IsPersistent = true;

            if (item.Frozen) car.IsPositionFrozen = true;
            else Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, car.Handle);

            return car;
        }

        private static Ped Person(Spooner.Placed item, Model model)
        {
            var ped = World.CreatePed(model, item.At, item.Yaw);
            if (ped == null || !ped.Exists()) return null;

            ped.PositionNoOffset = item.At;
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

        private static void Doing(Scene scene, Ped ped, Spooner.Placed item)
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
                    Stand(ped, item);

                    Function.Call(Hash.REQUEST_ANIM_DICT, item.AnimDict);

                    scene.Waits.Add(new Waiting
                    {
                        Who = ped,
                        What = item,
                        GiveUpAt = Game.GameTime + AnimWaitMs
                    });

                    return;
                }

                Stand(ped, item);
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

        private static void Stand(Ped ped, Spooner.Placed item)
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
                pick = list[Steady(item.At) % list.Length];
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
                    else Settle(ped, was);
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

        // ---- taking it out again ---------------------------------------------------

        private static void Drop(Scene scene)
        {
            foreach (var e in scene.Up)
            {
                try
                {
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
