using System;
using System.Collections.Generic;
using System.IO;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Parkview.Core;

namespace Hoodrich.Parkview
{
    /// <summary>
    /// What somebody built in the Object Spooner, stood up again every session. Cut out of
    /// Posted Up on 2026-09-21 as a mod of its own, with the scene at Parkview as its one scene.
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

            /// <summary>
            /// How far round its middle the scene clears away the map's own props while it is up --
            /// "bare" in the Note, or "bare: 10". Nought leaves the map as it is. See Bare.
            /// </summary>
            public float Bare;

            /// <summary>
            /// How near he has to be for the scene to go up at all -- "near: 12" in the Note -- in
            /// place of the scenery range, and it comes down as soon as he is that far off again.
            /// For a room inside a real building on the street: Apartment E2 is a flat in West
            /// Vinewood, and its things built from two hundred metres off would be standing in
            /// memory every time he drove past. 0 is the ordinary range.
            /// </summary>
            public float Near;

            /// <summary>The map props Bare has hidden, let back when the scene comes down; and the ones it left, by where they are.</summary>
            public readonly List<Spooner.Hidden> Bared = new List<Spooner.Hidden>();
            public readonly HashSet<string> BareLeft = new HashSet<string>();
            public int BareNext;

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
            /// The peds the file says never move off their marks -- in bed, and the like: no
            /// strolls, no walk-offs, never gone for the night. See Appear.
            /// </summary>
            public HashSet<int> Stays = new HashSet<int>();

            /// <summary>
            /// The peds the file says stand on a floor the navmesh does not know -- a balcony,
            /// a roof, a shop or yard walled in with props. They only ever walk in straight
            /// lines along their own floor. See "Along his own floor".
            /// </summary>
            public HashSet<int> Straight = new HashSet<int>();

            /// <summary>Who ranges wider than a stroll, and how far out. See Roams and Stroll.</summary>
            public HashSet<int> Roams = new HashSet<int>();
            public float RoamMost;

            /// <summary>"roams 80:" with nothing after it: everybody the file does not say stays.</summary>
            public bool RoamAll;

            /// <summary>
            /// The peds the file says live in a camp -- "camp:" in the Note. They never walk off,
            /// never go up the road and never leave for the small hours; everything they do is in
            /// the camp round their mark, and at night they sleep in it. See Camp.
            /// </summary>
            public HashSet<int> Camp = new HashSet<int>();

            /// <summary>A second place of work for a man, by his saved handle -- "jobs:" in the Note. See Work.</summary>
            public Dictionary<int, List<Job>> Jobs = new Dictionary<int, List<Job>>();

            /// <summary>The panes of glass that are made solid -- "glass:" in the Note. See Glaze.</summary>
            public HashSet<int> Glass = new HashSet<int>();

            /// <summary>The men on the door -- "guards:" in the Note. See Guard.</summary>
            public HashSet<int> Guards = new HashSet<int>();

            /// <summary>
            /// Who is sat down, and on what -- "sits: 811001=209448" in the Note: a ped's saved
            /// handle and the saved handle of a couch or a chair in the same file. She is put on
            /// its cushion by the seat scenario itself rather than stood on a mark. Michael asked
            /// for two Families women on the den's couches on 2026-09-26. See Sat.
            /// </summary>
            public Dictionary<int, int> Sits = new Dictionary<int, int>();

            /// <summary>
            /// A room you are put into, not a place you walk up to -- "indoors" in the Note. Its
            /// people are stood straight on their marks when it is built, because walking in from
            /// somewhere out of sight means nothing in a garage you have just been put inside,
            /// and a man who the file says stays would never be put on a mark you can see. The
            /// gambling den. They get their lives as ever once they are up.
            /// </summary>
            public bool Indoors;

            /// <summary>
            /// Whether this one lives here rather than stands in it. ASKED IN ONE PLACE,
            /// because it is asked in three -- the life, the drunk walk and the unlocking --
            /// and three copies of a rule is two chances to get it wrong.
            /// </summary>
            public bool Roaming(int handle)
            {
                if (Stays.Contains(handle)) return false;
                return RoamAll || Roams.Contains(handle);
            }

            /// <summary>Saved handles that are never taken down. See Keeps and Drop.</summary>
            public HashSet<int> Keeps = new HashSet<int>();

            /// <summary>And whole MODELS that are never taken down, by hash. See Keeps.</summary>
            public HashSet<uint> KeepModels = new HashSet<uint>();

            /// <summary>Those of them left standing, and the floor tiles under them.</summary>
            public readonly Dictionary<int, Entity> Kept = new Dictionary<int, Entity>();
            public readonly List<Entity> KeptTiles = new List<Entity>();

            /// <summary>Somewhere else worth walking to and back from, and how far round it to mill. See Outing.</summary>
            public List<Vector3> Outings = new List<Vector3>();
            public float OutingReach;

            /// <summary>What particular peds do on their marks, by saved handle: scenarios or dict/clip pairs. See Idles.</summary>
            public Dictionary<int, string[]> Idles = new Dictionary<int, string[]>();

            /// <summary>
            /// The props the file says to lay a floor under, by their saved handle, each with
            /// the height of its top where the file gave one and nothing where it did not.
            /// See Paving.
            /// </summary>
            public Dictionary<int, float?> Floors = new Dictionary<int, float?>();

            /// <summary>How many of its peds were not stood up because of the hour. See Living a little.</summary>
            public int AwayTonight;

            /// <summary>How many of its peds are walking in rather than stood on the spot. See Step.</summary>
            public int WalkingIn;

            /// <summary>How many of those walking in come in along their own floors. See ComeBackAlong.</summary>
            public int AlongIn;

            /// <summary>Whether the builder has been let past the first ped. See Step, "the ground first".</summary>
            public bool PedsGo;

            /// <summary>When it stops waiting for the ground and lets them go up anyway.</summary>
            public int PedsBy;

            /// <summary>The floors laid or refused, by saved handle, so the wait for them can end.</summary>
            public readonly HashSet<int> Paved = new HashSet<int>();

            /// <summary>Peds stood up frozen until the ground under each has loaded. See Thawing.</summary>
            public readonly List<Cold> Frozen = new List<Cold>();

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

        /// <summary>A ped waiting for the ground under him.</summary>
        private sealed class Cold
        {
            public Ped Who;
            public Spooner.Placed Item;
            public int By;

            /// <summary>Whether his mark is well above the map: a roof, a floor of a block.</summary>
            public bool Elevated;

        }

        /// <summary>How far below a mark something solid has to be for the mark to count as floored.</summary>
        private const float FloorReach = 3.5f;

        /// <summary>
        /// Whether there is something solid under a point: the map, or a prop with its
        /// collision in. A ray straight down, which is the only honest answer -- "has
        /// physics" and "collision loaded around" both said yes about a block whose bounds
        /// had not streamed, and men went through its roof on the strength of it.
        /// </summary>
        private static bool Floored(Vector3 at, Entity ignore)
        {
            try
            {
                var hit = World.Raycast(new Vector3(at.X, at.Y, at.Z + 0.3f),
                                        new Vector3(at.X, at.Y, at.Z - FloorReach),
                                        IntersectFlags.Map | IntersectFlags.Objects, ignore);

                return hit.DidHit;
            }
            catch
            {
                return false;
            }
        }

        private readonly List<Scene> _scenes = new List<Scene>();
        private bool _worldWaited;

        /// <summary>How long a scene waits at its first ped for the ground, and how long a ped stays frozen at most.</summary>
        private const int PedsWaitMs = 8000;
        private const int ThawMostMs = 12000;

        /// <summary>
        /// A ped up on a block is not let go on the clock like one on the ground. Released at
        /// a hundred and fifty metres out, before the block under him had streamed its
        /// collision, he fell thirty metres and died -- every ped on every roof at Parkview.
        /// He waits for every prop in the scene to have its body, which happens as the
        /// player comes near, and this is only a backstop against waiting for ever.
        /// </summary>
        private const int ElevatedMostMs = 300000;
        private const float ElevatedAbove = 2.5f;

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
        /// The placements a scene file says never move: "stays: 463969, 504650" in its Note
        /// is those two saved handles stood on their marks whatever the hour, taking no walks
        /// and never gone for the night. For somebody in bed; he is only ever put on his mark
        /// while nobody can see it. See Appear.
        /// </summary>
        /// <summary>
        /// The placements stood on props with no navmesh under them: "straight: 610124, ..."
        /// in its Note is those saved handles walking in, strolling and walking off along
        /// their own floor, in straight lines, never on the navmesh. For the balconies, the
        /// roofs and the shop. See "Along his own floor".
        /// </summary>
        /// <summary>
        /// Who lives here rather than stands in it: "roams 50: 610128, 610129" is those two
        /// walking anywhere within fifty metres of their mark instead of the twelve a stroll
        /// covers, and allowed a chore among the props out there whether or not they are
        /// hobos. The opposite of stays:.
        ///
        /// Michael asked for it on 2026-09-22, for the fifty-one who came down off the sky.
        /// Their marks were 30 m of empty air -- nothing in the scene reaches above Z 41 --
        /// so standing them back on their marks was never going to be the answer.
        /// </summary>
        /// <summary>
        /// Somewhere worth leaving the place for: "outings: -199.7,-1721.6,32.1@14" is one
        /// mark, milled about within fourteen metres of it. A roamer takes one now and then
        /// and walks home after. Michael asked on 2026-09-22 for some of them to walk up to
        /// Lamar's party, dance and smoke and talk a while, and walk back.
        ///
        /// THE MARK IS A COORDINATE, not another mod's scene name: Lamar's yard belongs to
        /// Hoodrich and Parkview cannot see into it, so what this is given is the spot.
        /// </summary>
        private static List<Vector3> Outings(string path, out float reach)
        {
            var spots = new List<Vector3>();
            reach = 0f;

            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var ns = System.Globalization.NumberStyles.Float;

            foreach (var line in Clauses(path))
            {
                if (!line.StartsWith("outings:", StringComparison.OrdinalIgnoreCase)) continue;

                foreach (var raw in line.Substring(8).Split(';'))
                {
                    var part = raw.Trim();
                    if (part.Length == 0) continue;

                    var at = part.IndexOf('@');

                    if (at >= 0)
                    {
                        float r;
                        if (float.TryParse(part.Substring(at + 1).Trim(), ns, ci, out r))
                        {
                            reach = Math.Max(reach, Math.Min(60f, r));
                        }

                        part = part.Substring(0, at).Trim();
                    }

                    var bits = part.Split(',');
                    if (bits.Length < 3) continue;

                    float x, y, z;

                    if (float.TryParse(bits[0].Trim(), ns, ci, out x) &&
                        float.TryParse(bits[1].Trim(), ns, ci, out y) &&
                        float.TryParse(bits[2].Trim(), ns, ci, out z))
                    {
                        spots.Add(new Vector3(x, y, z));
                    }
                }
            }

            return spots;
        }

        private static HashSet<int> Roams(string path, out float most, out bool all)
        {
            var handles = new HashSet<int>();
            most = 0f;
            all = false;

            foreach (var line in Clauses(path))
            {
                if (!line.StartsWith("roams", StringComparison.OrdinalIgnoreCase)) continue;

                var colon = line.IndexOf(':');
                if (colon < 0) continue;

                float m;
                if (float.TryParse(line.Substring(5, colon - 5).Trim(), System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out m))
                {
                    most = Math.Max(0f, Math.Min(200f, m));
                }

                var any = false;

                foreach (var raw in line.Substring(colon + 1).Split(',', ';'))
                {
                    int handle;
                    if (int.TryParse(raw.Trim(), out handle)) { handles.Add(handle); any = true; }
                }

                // A RADIUS AND NOTHING ELSE IS EVERYBODY. Naming them one by one is how the
                // thirty-two who were already on the ground got left out: the list was the
                // fifty-one brought down out of the sky and nobody thought about the rest,
                // so two thirds of the block stood still on the generic beat. "roams 80:"
                // on its own is the whole scene bar the ones it says stay.
                if (!any) all = true;
            }

            return handles;
        }

        /// <summary>
        /// What never comes down: "keeps: 610076, 610077" is those two left standing when
        /// the scene is dropped, and found again where they were next time it is built.
        ///
        /// FOR THE THINGS THAT ARE SLOW. The two apartment slabs are the whole cost of the
        /// scene -- a hundred and twenty-eight floor tiles measured and laid under them every
        /// single time the player left the block and came back, which is the wait Michael
        /// got tired of on 2026-09-22. Everything else is quick and still comes and goes.
        ///
        /// Only what the FILE names: a scene that keeps everything is a scene that never
        /// lets go of anything, and the entity budget is not ours alone.
        /// </summary>
        /// <summary>
        /// A HANDLE OR A MODEL NAME. "keeps: 610027" is that one placement; "keeps:
        /// db_apart_02_" is every one of them in the file, and any more added later.
        ///
        /// The name is the better way round and is why it is here: naming the three blocks
        /// by handle meant reading them out of the file and getting it wrong twice, and a
        /// handle stops being right the moment the scene is re-saved out of Menyoo. A model
        /// name is a thing you can see.
        /// </summary>
        private static HashSet<int> Keeps(string path, HashSet<uint> models)
        {
            var handles = new HashSet<int>();

            foreach (var line in Clauses(path))
            {
                if (!line.StartsWith("keeps:", StringComparison.OrdinalIgnoreCase)) continue;

                foreach (var raw in line.Substring(6).Split(',', ';'))
                {
                    var part = raw.Trim();
                    if (part.Length == 0) continue;

                    int handle;

                    if (int.TryParse(part, out handle)) handles.Add(handle);
                    else if (models != null) models.Add(Names.Joaat(part));
                }
            }

            return handles;
        }

        /// <summary>The saved handles listed after one key of the Note -- "stays:", "straight:" -- as a set.</summary>
        private static HashSet<int> Listed(string path, string key)
        {
            var handles = new HashSet<int>();

            foreach (var line in Clauses(path))
            {
                if (!line.StartsWith(key, StringComparison.OrdinalIgnoreCase)) continue;

                foreach (var raw in line.Substring(key.Length).Split(',', ';'))
                {
                    int handle;
                    if (int.TryParse(raw.Trim(), out handle)) handles.Add(handle);
                }
            }

            return handles;
        }

        private static HashSet<int> Stays(string path) => Listed(path, "stays:");

        /// <summary>Saved handle to saved handle after one key of the Note -- "sits: 811001=209448, 811002=741659".</summary>
        private static Dictionary<int, int> Pairs(string path, string key)
        {
            var pairs = new Dictionary<int, int>();

            foreach (var line in Clauses(path))
            {
                if (!line.StartsWith(key, StringComparison.OrdinalIgnoreCase)) continue;

                foreach (var raw in line.Substring(key.Length).Split(',', ';'))
                {
                    var eq = raw.IndexOf('=');
                    if (eq < 0) continue;

                    int who, what;

                    if (int.TryParse(raw.Substring(0, eq).Trim(), out who) &&
                        int.TryParse(raw.Substring(eq + 1).Trim(), out what))
                    {
                        pairs[who] = what;
                    }
                }
            }

            return pairs;
        }

        private static HashSet<int> Straight(string path) => Listed(path, "straight:");

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

        /// <summary>
        /// What particular peds do, from the Note: "idles: 610640=WORLD_HUMAN_SMOKING,
        /// missclothing/idle_storeclerk; 610642=..." is a list per saved handle, each entry a
        /// scenario name or an animation as dict/clip, and the beats rotate through the list.
        /// For the shopkeeper who stocks shelves and the old man who smokes behind the counter.
        /// </summary>
        private static Dictionary<int, string[]> Idles(string path)
        {
            var idles = new Dictionary<int, string[]>();

            foreach (var line in Clauses(path))
            {
                if (!line.StartsWith("idles:", StringComparison.OrdinalIgnoreCase)) continue;

                foreach (var one in line.Substring(6).Split(';'))
                {
                    var eq = one.IndexOf('=');
                    if (eq < 0) continue;

                    int handle;
                    if (!int.TryParse(one.Substring(0, eq).Trim(), out handle)) continue;

                    var list = new List<string>();

                    foreach (var raw in one.Substring(eq + 1).Split(','))
                    {
                        var entry = raw.Trim();
                        if (entry.Length > 0) list.Add(entry);
                    }

                    if (list.Count > 0) idles[handle] = list.ToArray();
                }
            }

            return idles;
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
                var ours = Paths.ParkviewScenery;

                if (Directory.Exists(ours))
                {
                    var dumps = 0;

                    foreach (var path in Directory.GetFiles(ours, "*.xml", SearchOption.AllDirectories))
                    {
                        // A CAPTURE IS NOT A SCENE. What the capture key writes is everything
                        // that stood round you at the moment you pressed it -- this scene, any
                        // other scene in reach, and whatever happened to be walking past. It is
                        // written in here to be merged into a scene by hand, and it was being
                        // read straight back as a scene of its own: a second copy of the lot,
                        // every session, and another copy again for every press of the key.
                        //
                        // Only the files we put here by name are scenes. Rename a capture and
                        // it becomes one.
                        if (Path.GetFileName(path).StartsWith("capture-", StringComparison.OrdinalIgnoreCase))
                        {
                            dumps++;
                            continue;
                        }

                        files.Add(path);
                    }

                    if (dumps > 0)
                    {
                        Log.Info("Scenery: " + dumps + " capture file(s) in the scenery folder are left alone. " +
                                 "A capture is a save to merge, not a scene; rename one to have it built.");
                    }
                }
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

                // What the hide key has taken out of it, before anything else looks at it. See Strike.
                Unstruck(name, items);

                // A FILE MAY BE NOTHING BUT REMOVALS. That is what hidden.xml is -- the one
                // the settings screen writes when you take a map prop out by hand -- and
                // refusing a file with no placements in it would have thrown it away.
                if (items.Count == 0 && gone.Count == 0) return;

                // THE GROUND FIRST, THEN THE PEOPLE, THEN ANYTHING STUCK TO SOMETHING. A ped
                // stood up before the block under him has a body, or before the floor under
                // him is laid, falls through it -- which is what every ped at Parkview did on
                // 2026-09-21 -- so props and vehicles come first and peds after them. Anything
                // attached goes up last, so the thing it is stuck to is already standing.
                // Sorting once here is the whole of the ordering problem; the builder itself
                // can then just walk the list, and wait once, at the first ped. See Step.
                items.Sort((a, b) => Rank(a) - Rank(b));

                var scene = new Scene { Name = name, Path = path, Items = items, Gone = gone };
                Measure(scene);

                try { scene.Stays = Stays(path); }
                catch { /* everybody takes their walks */ }

                try { scene.Straight = Straight(path); }
                catch { /* everybody walks on the navmesh */ }

                try { scene.Roams = Roams(path, out scene.RoamMost, out scene.RoamAll); }
                catch { /* nobody ranges wider than a stroll */ }

                try { scene.Keeps = Keeps(path, scene.KeepModels); }
                catch { /* everything comes down with the scene */ }

                try { scene.Outings = Outings(path, out scene.OutingReach); }
                catch { /* nowhere to go */ }

                try { scene.Floors = Floors(path); }
                catch { /* nothing gets a floor */ }

                try { scene.Idles = Idles(path); }
                catch { /* everybody idles off the lists */ }

                try { scene.Camp = Listed(path, "camp:"); }
                catch { /* nobody lives in a camp */ }

                try { scene.Jobs = Jobs(path); }
                catch { /* everybody has the one place */ }

                try { scene.Glass = Listed(path, "glass:"); }
                catch { /* the glass stays a picture of glass */ }

                try
                {
                    scene.Guards = Listed(path, "guards:");

                    foreach (var one in items)
                    {
                        if (one.What == Spooner.Kind.Ped && scene.Guards.Contains(one.Handle)) one.Guard = true;
                    }
                }
                catch { /* nobody stands guard */ }

                try { scene.Sits = Pairs(path, "sits:"); }
                catch { /* nobody sits */ }

                try { scene.Near = NearReach(path); }
                catch { /* the ordinary range */ }

                try { scene.Bare = BareReach(path, scene.Radius); }
                catch { /* the map's own props stay */ }

                try { scene.Indoors = Clauses(path).Exists(c => c.StartsWith("indoors", StringComparison.OrdinalIgnoreCase)); }
                catch { /* everybody walks in */ }

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

        private static int Rank(Spooner.Placed item)
        {
            if (item.Attached) return 2;
            return item.What == Spooner.Kind.Ped ? 1 : 0;
        }

        /// <summary>
        /// Whether everything standing in the scene has a body and every floor is laid: every
        /// prop up has been looked at by Solid, and every floor the file names is paved or
        /// refused. Collision two hundred metres off may not load until you are nearer, which
        /// is why the wait on this has a limit.
        /// </summary>
        private static bool GroundReady(Scene scene)
        {
            foreach (var handle in scene.Floors.Keys)
            {
                if (!scene.Paved.Contains(handle)) return false;
            }

            foreach (var e in scene.Up)
            {
                if (e is Prop && !scene.Solid.Contains(e.Handle)) return false;
            }

            return true;
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

            // Their names, before the ground check below -- a name is only text and
            // costs nothing, and it should be up while the scene is still settling.
            Tags();

            // NOT WHILE THE WORLD IS STILL ARRIVING. A save that loads with the player stood
            // in the middle of a scene builds it in the first seconds, before the map has
            // collision round him -- and everything stood up then, ours and the game's, is
            // stood on nothing. Peds went straight through the ground and died, unseen,
            // inside the blocks. Nothing is built, freed or walked until the ground is in.
            try
            {
                if (!Function.Call<bool>(Hash.HAS_COLLISION_LOADED_AROUND_ENTITY, me.Handle))
                {
                    if (!_worldWaited) { _worldWaited = true; Log.Info("Scenery: waiting for the world to load round him before anything is built."); }
                    return;
                }
            }
            catch { /* build on */ }

            // HE MAY BE BEHIND A DOOR. Every door in this mod is a teleport, so by distance
            // he has just left the county, and the block used to come down behind him and go
            // back up, slowly, when he came out. What is out there is measured from outside
            // the door he went in by; what is in the room with him -- the den's own furniture
            // is a scene like any other -- is measured from him. See Core.Indoors.
            var him = me.Position;
            var here = Hoodrich.Core.Indoors.From(him);

            // A scene part-way up is finished as fast as ticks allow; the ranges themselves are
            // only worth looking at now and then.
            var now = Game.GameTime;
            var lookNow = now >= _nextLook;
            if (lookNow) _nextLook = now + LookEveryMs;

            Given(now);
            Solid(now);
            Live(now);

            // The men left standing when a scene came down in view of the player. See Drop.
            if (lookNow && _stragglers.Count > 0) Stragglers(here);

            var range = _cfg == null ? 220f : _cfg.SceneryRange;

            foreach (var scene in _scenes)
            {
                if (scene.Waits.Count > 0) Waited(scene, now);

                // The map's own props out of a bare room, as the room makes them. See Bare.
                if (scene.Bare > 0f && (scene.Built || scene.Working) && now >= scene.BareNext)
                {
                    scene.BareNext = now + BareEveryMs;
                    Bare(scene);
                }

                if (scene.Working)
                {
                    Step(scene);
                    continue;
                }

                if (!lookNow) continue;

                var gap = Math.Min(here.DistanceTo(scene.Centre), him.DistanceTo(scene.Centre)) - scene.Radius;

                // Near: only when he is at it, and down again the moment he is not. See Near.
                var reach = scene.Near > 0f ? Math.Min(range, scene.Near) : range;
                var slack = scene.Near > 0f ? Math.Min(DropSlack, scene.Near) : DropSlack;

                if (!scene.Built)
                {
                    if (gap <= reach) Begin(scene);
                    continue;
                }

                if (gap > reach + slack) Drop(scene, false);
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
            // The floors kept from last time go back on the books before anything is built,
            // so the slab above them is never paved twice. See Drop.
            foreach (var tile in scene.KeptTiles)
            {
                if (tile == null || !tile.Exists()) continue;

                scene.Up.Add(tile);
                scene.Solid.Add(tile.Handle);
                _tiles.Add(tile.Handle);
            }

            scene.KeptTiles.Clear();

            scene.AwayTonight = 0;
            scene.WalkingIn = 0;
            scene.AlongIn = 0;
            scene.PedsGo = false;
            scene.PedsBy = 0;
            scene.Paved.Clear();
            scene.Frozen.Clear();

            // WHO IS NOT HERE TODAY. Rolled when the scene goes up, once per file per day,
            // so it is the same answer every time you come back on the same day.
            scene.Skip.Clear();

            try
            {
                int chance;
                var some = Sometimes(scene.Path, out chance);

                if (some.Count > 0 && !Nights.On("the sometimes at " + scene.Name, chance))
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

        /// <summary>How many of a scene's people roam, for the line the log prints.</summary>
        private static int Roamers(Scene scene)
        {
            var n = 0;

            foreach (var one in scene.Items)
            {
                if (one.What == Spooner.Kind.Ped && scene.Roaming(one.Handle)) n++;
            }

            return n;
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

                // STILL STANDING from last time, because the file said keeps:. Taken back
                // onto the books rather than made again -- which is the whole saving, since
                // making it is the slow part and paving under it is slower.
                //
                // Or standing with nothing holding it at all, which is what the orphaning
                // bug left behind -- Adopt finds those and clears the duplicates up.
                Entity kept;

                if (!scene.Kept.TryGetValue(item.Handle, out kept)) kept = Adopt(scene, item);
                else scene.Kept.Remove(item.Handle);

                if (kept != null && kept.Exists())
                {
                    scene.Made++;
                    Register(scene, item, kept, false);
                    scene.Solid.Add(kept.Handle);

                    if (scene.Floors.ContainsKey(item.Handle)) scene.Paved.Add(item.Handle);

                    scene.Cursor++;
                    scene.Waited = 0;
                    did++;
                    continue;
                }

                // WALKED IN, NOT SPAWNED ON THE SPOT. A man who appears on his mark in front
                // of you is a script; one who walks up to it is a person. So a ped is not
                // stood up here at all: he is written down as away and comes back the way
                // anybody does -- stood up out of sight and walked in, the whole scene
                // arriving over a minute or so. Michael asked for it on 2026-09-21.
                //
                // EVERY ONE OF THEM, since 2026-09-22. It used to be only the marks you could
                // see or stood within sixty metres of (WalksIn, now gone), on the argument
                // that the rest is a walk nobody watches. But a scene is BUILT AT 220 m and
                // the player is usually walking towards it, so "nobody is watching" meant
                // most of them were already planted on their marks by the time he arrived --
                // which is the thing this was written to stop. Michael asked for all of them.
                //
                // NOBODY IS FILLED ON HIS MARK while the life is on, since 2026-09-25. The
                // people on the prop floors -- the balconies, the roofs, the shop -- used to
                // be, because there is no path in for the navmesh; now they come in along
                // their own floors, in straight lines the probe has proved (see
                // ComeBackAlong). A man the file says STAYS is put on his mark only while
                // nobody can see it (see Appear). And one written down in the small hours
                // walks in when the hour passes, the same as one who was stood here when it
                // struck. The ground wait, Already, Put and Register(false) below now only
                // run for a ped with SceneryLife off.
                //
                // Everything a life needs is written down here, because Register(arriving)
                // fills none of it in: who roams, who walks drunk, and the number NPC Mind
                // knows him by. It used to be left out for a man walking in, so a roamer who
                // walked in was never a roamer.
                if (item.What == Spooner.Kind.Ped && LifeOn && !scene.Indoors)
                {
                    var stays = scene.Stays.Contains(item.Handle);
                    var camp = scene.Camp.Contains(item.Handle);

                    // A CAMP IS HOME AT NIGHT TOO: he comes in, and goes to bed in it. See TurnIn.
                    var night = _quiet && !stays && !camp;
                    var roams = scene.Roaming(item.Handle);
                    var straight = scene.Straight.Contains(item.Handle);

                    _lives.Add(new Life
                    {
                        Scene = scene,
                        Item = item,
                        State = Stage.Away,
                        NextAt = night ? 0 : Game.GameTime + Dice.Next(WalkInStaggerMs),
                        ForTheNight = night,
                        Scripted = Scripted(item),
                        Stays = stays,
                        Camp = camp,
                        Straight = straight,
                        Roams = roams,
                        Drunk = roams && Steady(item.At) % 5 == 0,
                        Id = Folk.Number(Who(item))
                    });

                    if (night) scene.AwayTonight++; else scene.WalkingIn++;
                    if (straight) scene.AlongIn++;

                    scene.Cursor++;
                    scene.Waited = 0;
                    continue;
                }

                // THE GROUND FIRST. The list has the peds at the end (see Rank); at the first
                // of them the builder waits for every prop standing to have a body and every
                // floor to be laid, up to PedsWaitMs, and then lets them go -- frozen, each
                // until the ground under him is loaded. See Person and Thawing. Only reached
                // by a ped with SceneryLife off: the rest were written down as away above.
                if (item.What == Spooner.Kind.Ped && !scene.PedsGo)
                {
                    var clock = Game.GameTime;
                    if (scene.PedsBy == 0) scene.PedsBy = clock + PedsWaitMs;

                    var ready = GroundReady(scene);
                    if (!ready && clock < scene.PedsBy) return;

                    scene.PedsGo = true;

                    if (!ready)
                    {
                        Log.Info("Scenery: \"" + scene.Name + "\" -- its people go up before all of its ground " +
                                 "is solid; each is held frozen until the ground under him has loaded.");
                    }
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

                    Log.Info("Scenery: " + Say(item) + " in \"" + scene.Name + "\" never streamed in; it is left out this time.");
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

            if (scene.Keeps.Count > 0 || scene.KeepModels.Count > 0)
            {
                var held = 0;
                foreach (var one in scene.Items)
                {
                    if (scene.Keeps.Contains(one.Handle) ||
                        scene.KeepModels.Contains(unchecked((uint)one.ModelHash))) held++;
                }

                Log.Info("Scenery: " + held + " placement(s) of \"" + scene.Name +
                         "\" stay up between builds, and their floors with them -- " +
                         scene.Keeps.Count + " by handle, " + scene.KeepModels.Count + " model(s) by name.");
            }

            Log.Info("Built \"" + scene.Name + "\": " + scene.Made + " up" +
                     (scene.Missed > 0 ? ", " + scene.Missed + " skipped or would not load" : "") +
                     (scene.WalkingIn > 0
                         ? ", " + scene.WalkingIn + " walking in" +
                           (scene.AlongIn > 0 ? " (" + scene.AlongIn + " along their own floors)" : "")
                         : "") +
                     (scene.RoamAll || scene.Roams.Count > 0
                         ? ", " + Roamers(scene) + " of them living here"
                         : "") +
                     (scene.AwayTonight > 0 ? ", " + scene.AwayTonight + " not about at this hour (it is " + ClockHour() + ":00)" : "") + ".");
        }

        /// <summary>A scene's people arrive spread over this long.</summary>
        private const int WalkInStaggerMs = 75000;

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

            // THE ONES WHO LIVE HERE ARE LET OUT OF THE LOCK. Before the walking-in return
            // below, because a man who walked in is exactly the man this is for. See Real.
            if (scene.Roaming(item.Handle) && !item.Armed)
            {
                Real(ped);
            }

            // Walking in: ComeBack gives him the walk, and his life is already on the list.
            if (arriving) return;

            StandOnMark(scene, item, ped);

            if (!LifeOn) return;

            var life = new Life
            {
                Scene = scene,
                Item = item,
                Who = ped,
                State = Stage.Marked,
                Scripted = Scripted(item),
                Stays = scene.Stays.Contains(item.Handle),
                Camp = scene.Camp.Contains(item.Handle),
                Roams = scene.Roaming(item.Handle),

                // ONE IN FIVE, and the same one in five every session -- off the mark rather
                // than a roll, so the man who walks drunk tonight walked drunk last night.
                // See Souse.
                Drunk = scene.Roaming(item.Handle) && Steady(item.At) % 5 == 0,
                Name = Called(item, ped),
                Id = Folk.Number(Who(item)),
                Talk = Voices.Talker(Voices.For(ped, Male(ped), Steady(item.At)))
            };

            life.NextAt = Game.GameTime + Beat(life);
            life.NextLine = Game.GameTime + Between(IdleLineLeastMs, IdleLineMostMs);
            _lives.Add(life);
        }

        /// <summary>
        /// A ped stood on his mark, set going there: frozen until the ground under him has
        /// loaded (see Thawing), and given his idle. One whose mark is well above the map
        /// waits for the scene's props to have bodies rather than for the clock. See
        /// ElevatedMostMs. Shared by the build with the life off and by Appear, which puts a
        /// man in bed on his mark without making a second life for him.
        /// </summary>
        private static void StandOnMark(Scene scene, Spooner.Placed item, Ped ped)
        {
            float ground;
            var elevated = Ground.Probe(new Vector3(item.At.X, item.At.Y, item.At.Z + 1f), out ground) &&
                           item.At.Z - ground > ElevatedAbove;

            scene.Frozen.Add(new Cold
            {
                Who = ped,
                Item = item,
                Elevated = elevated,
                By = Game.GameTime + (elevated ? ElevatedMostMs : ThawMostMs)
            });

            Doing(scene, ped, item);
        }

        /// <summary>
        /// What a life only gets once there is a ped to read it off: his name, his number for
        /// NPC Mind, his voice and when he next says something. Register(arriving) never set
        /// any of it, so a man who walked in had no name over his head and no idle lines.
        /// Nothing already there is written over.
        /// </summary>
        private static void WhoHeIs(Life l, Ped ped, int now)
        {
            if (string.IsNullOrEmpty(l.Name)) l.Name = Called(l.Item, ped);
            if (l.Id == 0) l.Id = Folk.Number(Who(l.Item));
            if (l.Talk == null) l.Talk = Voices.Talker(Voices.For(ped, Male(ped), Steady(l.Item.At)));
            if (l.NextLine == 0) l.NextLine = now + Between(IdleLineLeastMs, IdleLineMostMs);
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

                Thawing(scene, now);

                // OVER A COPY: Pave adds the floor tiles to Up part-way through, and a list
                // being walked does not take additions -- "Collection was modified", twice a
                // load, and the rest of the pass lost each time.
                foreach (var made in scene.Up.ToArray())
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

                            scene.Paved.Add(item.Handle);

                            if (paved == Verdict.Up) continue;
                        }
                        // THE GLASS MADE SOLID, where the file asks. See Glaze.
                        else if (!given && item != null && scene.Glass.Contains(item.Handle))
                        {
                            var glazed = Glaze(scene, made, item);

                            if (glazed == Verdict.NotYet)
                            {
                                scene.Solid.Remove(made.Handle);
                                continue;
                            }

                            if (glazed == Verdict.Up) continue;
                        }
                        else if (given && item != null && scene.Floors.ContainsKey(item.Handle))
                        {
                            // It has a body of its own; nothing to lay, nothing to wait for.
                            scene.Paved.Add(item.Handle);
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
        /// The peds stood up frozen, freed as the ground under each of them loads.
        ///
        /// Freed when the scene's ground is ready (every prop looked at, every floor laid)
        /// and the game has collision loaded round him -- or, past ThawMostMs, regardless,
        /// because a man frozen for ever on a corner two hundred metres off is worse than one
        /// who drops a foot. Put back on his mark exactly as he is freed, and the file's own
        /// frozen flag applied.
        /// </summary>
        /// <summary>
        /// How long the world gets to arrive round an elevated man before an empty space under
        /// him is believed. Past this, with the map loaded round him and every block of the
        /// scene solid, nothing under him means nothing is there.
        /// </summary>
        private const int GroundGraceMs = 8000;

        /// <summary>
        /// Down to earth: an elevated man with no roof under him, stood on whatever IS below
        /// his mark, and his mark moved there with him.
        ///
        /// THE MARK MOVES TOO, and that is not optional. Every walk he takes ends at Home,
        /// which puts him on his mark exactly -- so a man put down on the ground whose mark is
        /// still thirty metres up is back in the sky the first time he finishes anything. The
        /// placement is the one object his life and his cold both point at, so changing it here
        /// changes it everywhere for the rest of the session. The FILE is not touched: the log
        /// names every one of them, so the data can be fixed and this never have to run.
        /// </summary>
        private static bool Earth(Scene scene, Cold cold)
        {
            try
            {
                var at = cold.Item.At;
                float g;

                // From just above him, looking down: the first solid thing below is where he
                // stands -- the ground, a wall top, an awning. Not found yet and he is simply
                // asked about again on the next pass.
                if (!Ground.Probe(new Vector3(at.X, at.Y, at.Z + 1f), out g)) return false;

                var down = new Vector3(at.X, at.Y, g + 1.0f);

                cold.Item.At = down;
                cold.Who.PositionNoOffset = down;
                cold.Who.Heading = cold.Item.Yaw;
                cold.Who.IsPositionFrozen = cold.Item.Frozen;

                Log.Info("Scenery: nothing under " + Say(cold.Item) + " at Z " + at.Z.ToString("0.0") +
                         " in \"" + scene.Name + "\" -- there is no roof there, so he is stood on the ground at Z " +
                         g.ToString("0.0") + ". Fix his mark in the file and this stops.");
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not bring one down: " + ex.Message);
                return false;
            }
        }
        private static void Thawing(Scene scene, int now)
        {
            if (scene.Frozen.Count == 0) return;

            var ready = GroundReady(scene);

            for (var i = scene.Frozen.Count - 1; i >= 0; i--)
            {
                var cold = scene.Frozen[i];

                try
                {
                    if (cold.Who == null || !cold.Who.Exists())
                    {
                        scene.Frozen.RemoveAt(i);
                        continue;
                    }

                    var loaded = false;
                    try { loaded = Function.Call<bool>(Hash.HAS_COLLISION_LOADED_AROUND_ENTITY, cold.Who.Handle); }
                    catch { }

                    var floored = loaded && Floored(cold.Item.At, cold.Who);

                    if (!floored)
                    {
                        // On the ground, the clock lets him go: a foot at most.
                        if (!cold.Elevated)
                        {
                            if (now < cold.By) continue;
                        }
                        else
                        {
                            // UP HIGH WITH NOTHING UNDER HIM.
                            //
                            // This used to hold him where he was FOR EVER and log "move the
                            // mark" once -- a man frozen in the sky, on purpose, for the whole
                            // session. That was the lesser evil next to letting him fall thirty
                            // metres and die, and it was still the thing Michael kept seeing:
                            // people standing on nothing above the courts. And `ready` was
                            // worked out at the top of this method and never used, so nothing
                            // ever decided the roof simply is not there.
                            //
                            // Now it is decided. He is held while the world could still be
                            // arriving -- the map round him not loaded yet, or the scene's own
                            // blocks not given their bodies yet -- and the moment both are in
                            // and there is STILL nothing under him, there is no roof. He is put
                            // down on whatever is below his mark instead. The long wait stays
                            // as a backstop in case the world never reports in.
                            var since = cold.By - ElevatedMostMs;
                            var settled = loaded && ready && now - since >= GroundGraceMs;

                            if (!settled && now < cold.By) continue;

                            // NEVER DROPPED TO THE GROUND IN FRONT OF ANYBODY. Earth is a
                            // vertical teleport of up to nine metres, and the only way onto a
                            // mark now is out of sight (see Appear); it stays that way.
                            if (!Unseen(scene, cold.Who)) continue;

                            if (!Earth(scene, cold)) continue;

                            scene.Frozen.RemoveAt(i);
                            continue;
                        }
                    }

                    // Sat down, she is on the cushion the scenario put her on, not on her mark;
                    // put back on the mark she would be sat on the air.
                    if (!scene.Sits.ContainsKey(cold.Item.Handle))
                    {
                        cold.Who.PositionNoOffset = cold.Item.At;
                        cold.Who.Heading = cold.Item.Yaw;
                    }

                    cold.Who.IsPositionFrozen = cold.Item.Frozen;

                    scene.Frozen.RemoveAt(i);
                }
                catch
                {
                    scene.Frozen.RemoveAt(i);
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
                    Log.Info("Scenery: nothing in this game called " + Say(item) + " -- it is left out.");
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

                // SAID, because a thing the game would not make is a thing missing from a
                // scene with no trace of why, and two of Parkview went that way for a day.
                if (made == null || !made.Exists())
                {
                    Log.Info("Scenery: the game would not make " + Say(item) + " at " + item.At + "; it is left out this time.");
                    return Verdict.No;
                }

                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, made.Handle, true, true);

                // NOBODY IS STOOD UP INVISIBLE. The flag is a trap that feeds itself: a ped
                // who had not streamed in when a capture was taken reads back as invisible,
                // the capture writes that down, the file stands him up invisible, and the
                // next capture over him writes it down again. Fifty-one of Parkview's roof
                // peds went round that loop until they were found hanging there unseen.
                //
                // A PROP may still be hidden on purpose -- hidden.xml is nothing else.
                if (!item.Visible && item.What != Spooner.Kind.Ped) made.IsVisible = false;
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

                Log.Info("Scenery: " + Say(item) + " would not stand up: " + ex.Message);
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

                // A BUILDING IS DRAWN AS FAR AS THE MAP'S ARE. See Big.
                if (Big(thing)) lod = LodBuilding;

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

        /// <summary>
        /// How far a building placed as a prop is drawn from, and how big a thing has to be to be
        /// one. A whole block put down in the spooner -- Parkview's db_apart_02_ and the rest --
        /// comes without the low-detail copy the map's own buildings fade into, so at its draw
        /// distance it simply goes. At three and five hundred metres that was well inside the
        /// distance the map's buildings are still standing at, and Michael saw his blocks blink
        /// in and out on the skyline on 2026-09-26. Fifteen hundred is about where the map's
        /// own give way to the far scenery. It is only how far away it is drawn: the model is
        /// already in memory, standing, either way.
        /// </summary>
        private const int LodBuilding = 1500;
        private const float BuildingSize = 10f;

        private static readonly Dictionary<int, bool> BigModels = new Dictionary<int, bool>();

        /// <summary>Whether a thing is building-sized: ten metres or more along any side. Asked once a model.</summary>
        private static bool Big(Entity thing)
        {
            var hash = thing.Model.Hash;
            bool big;
            if (BigModels.TryGetValue(hash, out big)) return big;

            try
            {
                var lo = new OutputArgument();
                var hi = new OutputArgument();
                Function.Call(Hash.GET_MODEL_DIMENSIONS, hash, lo, hi);
                var size = hi.GetResult<Vector3>() - lo.GetResult<Vector3>();

                big = size.X >= BuildingSize || size.Y >= BuildingSize || size.Z >= BuildingSize;

                if (big)
                {
                    Log.Info("Scenery: " + Names.Say(hash) + " is building-sized (" + size.X.ToString("0") + " x " +
                             size.Y.ToString("0") + " x " + size.Z.ToString("0") + " m); drawn from " + LodBuilding + " m.");
                }
            }
            catch
            {
                big = false;
            }

            BigModels[hash] = big;
            return big;
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

            // WHO HE IS, for NPC Mind. Persistent makes him a mission-type ped, and NPC Mind
            // will not talk to one of those unless somebody vouches for him -- so without this
            // not one person in the scene could be spoken to. Keyed off his MARK, not "at":
            // a man who walks in is stood up away from his mark, so "at" is somewhere new
            // every time and would make him a stranger every session. See Who and Core.Folk.
            Folk.Stamp(ped, Who(item));

            // HIS VOICE, FOR GOOD: the one named after his model, picked by his mark, so the same
            // man sounds the same every session and the game's own reactions come out in it too.
            // See Core.Voices.
            Voices.Dress(ped, Voices.For(ped, Male(ped), Steady(item.At)));

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

            // ON FOOT AND ON THE FLAT. A walk (see Living a little) goes round a fence, not
            // over it, and never off a ledge or up a ladder: a man vaulting a fence to get to
            // his mark is a stunt, and one dropping off a roof is a body.
            Function.Call(Hash.SET_PED_PATH_CAN_USE_CLIMBOVERS, ped.Handle, false);
            Function.Call(Hash.SET_PED_PATH_CAN_USE_LADDERS, ped.Handle, false);
            Function.Call(Hash.SET_PED_PATH_CAN_DROP_FROM_HEIGHT, ped.Handle, false);
            Function.Call(Hash.SET_PED_PATH_AVOID_FIRE, ped.Handle, true);
            Function.Call(Hash.SET_PED_PATH_PREFER_TO_AVOID_WATER, ped.Handle, true);

            if (item.Health > 0)
            {
                ped.MaxHealth = Math.Max(item.Health, ped.MaxHealth);
                ped.Health = item.Health;
            }

            if (item.Armour > 0) ped.Armor = item.Armour;

            Wear(ped, item);

            // DRESSED BY THE GAME where the file says nothing about clothes. A placement whose
            // model was swapped for one of the set's (Parkview, 2026-09-21) carries no
            // variations of its own, and a model's defaults are the same man twenty times
            // over. The game's own random dressing respects what goes with what.
            if (item.Components.Count == 0 && item.Props.Count == 0)
            {
                try
                {
                    Function.Call(Hash.SET_PED_RANDOM_COMPONENT_VARIATION, ped.Handle, 0);
                    Function.Call(Hash.SET_PED_RANDOM_PROPS, ped.Handle);
                }
                catch { /* the defaults, then */ }
            }

            if (item.GroupHash != 0)
            {
                try { Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle, item.GroupHash); }
                catch { /* it keeps the group its model came with */ }
            }

            Arm(ped, item);

            // FROZEN LAST, AND ALWAYS -- for now. The animation still plays on a frozen ped;
            // what stops is him dropping through a floor that has no body yet. The file's own
            // answer is applied at the thaw (see Thawing): a ped saved frozen stays so, one
            // saved free is freed once the ground under him has loaded. One stood up somewhere
            // else to walk in from is freed at once by Loose.
            ped.IsPositionFrozen = true;

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
        /// <summary>The guard guns, one per mark, so a row of ten is a mix rather than ten of the one rifle.</summary>
        private static readonly string[] GuardGuns =
        {
            "WEAPON_PISTOL", "WEAPON_MICROSMG", "WEAPON_CARBINERIFLE", "WEAPON_PUMPSHOTGUN", "WEAPON_ASSAULTRIFLE", "WEAPON_PISTOL"
        };

        private static uint GuardGun(GTA.Math.Vector3 at)
        {
            return Names.Joaat(GuardGuns[Steady(at) % GuardGuns.Length]);
        }

        private static void Arm(Ped ped, Spooner.Placed item)
        {
            if (!item.Armed) return;

            try
            {
                // NOT THE GUN IN THE FILE. Saved with a rifle, every one of them, because a
                // rifle is what you reach for in the spooner; what he actually carries is one
                // of the guard guns, by where he stands, so a block of ten is a mix. See GuardGuns.
                //
                // EXCEPT A GUARD, whose gun is the point of him: the den's door is two Families
                // with compact rifles because Michael put compact rifles in their hands. He gets
                // the file's, and it stays in his hands -- see Guard.
                var gun = item.Guard ? item.WeaponHash : GuardGun(item.At);

                Function.Call(Hash.GIVE_WEAPON_TO_PED, ped.Handle, gun, 250, false, true);

                var busy = !item.Guard &&
                           (!string.IsNullOrEmpty(item.Scenario) ||
                            (!string.IsNullOrEmpty(item.AnimDict) && !string.IsNullOrEmpty(item.AnimClip)));

                Function.Call(Hash.SET_CURRENT_PED_WEAPON, ped.Handle,
                              busy ? Spooner.Placed.Unarmed : gun, true);

                if (item.Guard) Function.Call(Hash.SET_PED_CAN_SWITCH_WEAPON, ped.Handle, false);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not arm a placement: " + ex.Message);
            }
        }

        // ---- what it is doing ------------------------------------------------------

        /// <summary>
        /// What a ped is given when the file did not say: a SCENARIO, with the prop in his hand.
        ///
        /// It was an animation, on the argument that a scenario is the game's own behaviour --
        /// it fetches a prop, it wants a spot it approves of, and it can decide it has finished
        /// and hand the ped back to standing. All true, and all beside the point once Michael
        /// looked at the result on 2026-09-21: a smoking animation with no cigarette and a
        /// phone animation with no phone are a man shifting his weight, and forty of them read
        /// as forty men doing nothing. The scenario is the thing with the cigarette in it.
        /// Started in place, so the spot is the mark and nothing is fetched from anywhere; and
        /// one that ends is given again at the next beat, because every ped has one now. See
        /// Living a little.
        ///
        /// Every name here is checked against the game's own scenario list rather than
        /// remembered; the three the shipped scenes already use are among them.
        /// </summary>
        private static readonly string[] MenIdle =
        {
            "WORLD_HUMAN_SMOKING",
            "WORLD_HUMAN_STAND_MOBILE",
            "WORLD_HUMAN_DRINKING",
            "WORLD_HUMAN_HANG_OUT_STREET",
            "WORLD_HUMAN_DRUG_DEALER_HARD",
            "WORLD_HUMAN_SMOKING_POT",
            "WORLD_HUMAN_STAND_IMPATIENT",
            "WORLD_HUMAN_PARTYING",
            "WORLD_HUMAN_HANG_OUT_STREET_CLUBHOUSE",
            "WORLD_HUMAN_AA_SMOKE",
            "WORLD_HUMAN_STAND_MOBILE_UPRIGHT",
            "WORLD_HUMAN_MUSCLE_FLEX",
            "WORLD_HUMAN_DRUG_DEALER",
            "WORLD_HUMAN_STAND_IMPATIENT_UPRIGHT",
            "WORLD_HUMAN_TOURIST_MOBILE",
            "WORLD_HUMAN_STUPOR"
        };

        private static readonly string[] WomenIdle =
        {
            "WORLD_HUMAN_SMOKING",
            "WORLD_HUMAN_STAND_MOBILE",
            "WORLD_HUMAN_HANG_OUT_STREET",
            "WORLD_HUMAN_DRINKING",
            "WORLD_HUMAN_PARTYING",
            "WORLD_HUMAN_HANG_OUT_STREET_CLUBHOUSE",
            "WORLD_HUMAN_AA_SMOKE",
            "WORLD_HUMAN_STAND_MOBILE_UPRIGHT",
            "WORLD_HUMAN_TOURIST_MOBILE",
            "WORLD_HUMAN_STAND_IMPATIENT_UPRIGHT",
            "WORLD_HUMAN_CHEERING"
        };

        /// <summary>
        /// A hobo idles like a hobo: begging, slumped, a drink, a smoke, a sign by the road.
        /// Told from the model -- the tramps -- so any scene that has one gets it, and the
        /// camp behind the church has six.
        /// </summary>
        private static readonly string[] HoboIdle =
        {
            "WORLD_HUMAN_BUM_STANDING",
            "WORLD_HUMAN_BUM_SLUMPED",
            "WORLD_HUMAN_DRINKING",
            "WORLD_HUMAN_SMOKING",
            "WORLD_HUMAN_BUM_FREEWAY",
            "WORLD_HUMAN_STUPOR",
            "WORLD_HUMAN_AA_SMOKE",
            "WORLD_HUMAN_MUSICIAN"
        };

        private static readonly string[] HoboModels =
        {
            "a_m_m_tramp_01", "a_m_o_tramp_01", "a_f_m_tramp_01", "a_m_m_trampbeac_01", "a_f_m_trampbeac_01",
            "a_m_m_skidrow_01", "a_m_y_hippy_01", "a_m_y_acult_01", "a_m_o_acult_01", "a_m_m_acult_01",
            "a_m_o_acult_02", "a_m_y_acult_02", "u_m_y_militarybum"
        };

        private static bool IsHobo(Spooner.Placed item)
        {
            if (item == null) return false;

            foreach (var name in HoboModels)
            {
                if (unchecked((uint)item.ModelHash) == Names.Joaat(name)) return true;
            }

            return false;
        }

        /// <summary>The gang signs, thrown up over whatever he is doing. See Sign.</summary>
        private static readonly string[][] Signs =
        {
            new[] { "mp_player_int_uppergang_sign_a", "mp_player_int_gang_sign_a" },
            new[] { "mp_player_int_uppergang_sign_b", "mp_player_int_gang_sign_b" }
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

                if (item.Guard)
                {
                    Guard(ped, item, turn);
                    return;
                }

                // Sat on the seat the file gave her. See Sat.
                if (Sat(scene, ped, item)) return;

                // A list of his own, first of all: the Note picked it for this man.
                string[] own;

                if (scene != null && scene.Idles.TryGetValue(item.Handle, out own) && own.Length > 0)
                {
                    var entry = own[(Steady(item.At) + turn) % own.Length];
                    var slash = entry.IndexOf('/');

                    if (slash > 0) Give(ped, entry.Substring(0, slash), entry.Substring(slash + 1));
                    else Scenario(ped, entry);

                    return;
                }

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

        /// <summary>
        /// What a woman sat on a couch does, and a man: the plain sit first, the rest by the seat.
        /// Every one of them plays for both.
        /// </summary>
        private static readonly string[] SatDoing = { "PROP_HUMAN_SEAT_ARMCHAIR", "PROP_HUMAN_SEAT_BENCH", "PROP_HUMAN_SEAT_CHAIR" };

        /// <summary>
        /// Somebody the file sits down, on the cushion of the seat it names -- a scenario started
        /// on the cushion itself, the way Rest sits a man at his camp, because one told to sit
        /// where he stands sits on the air a foot short of it. False when the file gave him no
        /// seat, or the seat is not in the file.
        /// </summary>
        private static bool Sat(Scene scene, Ped ped, Spooner.Placed item)
        {
            if (scene == null || scene.Sits.Count == 0) return false;

            int on;
            if (!scene.Sits.TryGetValue(item.Handle, out on)) return false;

            Spooner.Placed seat = null;

            foreach (var one in scene.Items)
            {
                if (one.Handle != on || one.What != Spooner.Kind.Prop) continue;
                seat = one;
                break;
            }

            if (seat == null) return false;

            Vector3 cushion;
            float facing;
            CampSeat(scene, seat, out cushion, out facing);

            var doing = SatDoing[(int)((uint)Steady(item.At) % (uint)SatDoing.Length)];

            // Onto the cushion first. She is stood up frozen until the floor under her has loaded
            // (see Thawing), and a frozen woman the scenario could not move would sit on the air
            // at her mark.
            Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, ped.Handle, cushion.X, cushion.Y, cushion.Z, false, false, false);
            ped.Heading = facing;

            Function.Call(Hash.TASK_START_SCENARIO_AT_POSITION, ped.Handle, doing,
                          cushion.X, cushion.Y, cushion.Z, facing, 0, true, true);
            Function.Call(Hash.SET_PED_KEEP_TASK, ped.Handle, true);
            return true;
        }

        private static bool Play(Ped ped, Spooner.Placed item)
        {
            if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, item.AnimDict)) return false;

            Function.Call(Hash.TASK_PLAY_ANIM, ped.Handle, item.AnimDict, item.AnimClip,
                          8f, -8f, -1, item.AnimFlag, 0f, false, false, false);

            return true;
        }

        /// <summary>
        /// A man on a door, with a rifle. The rifle held across him -- the game's own guard waiting
        /// with one, off the Big Score, the clip another mod uses to put a rifle in a man's hands
        /// in its gun shop -- and the look-rounds and shifts of weight of the same guard now and
        /// then. The gun is put back in his hands every time, because a clip that starts on a man
        /// with his hands empty is a man holding nothing. Michael asked for the den's Families
        /// to be guards with compact rifles, doing guard animations, on 2026-09-26.
        /// </summary>
        private const string GuardDict = "missbigscore1guard_wait_rifle";

        private static readonly string[] GuardClips = { "wait_base", "wait_a", "wait_base", "wait_b", "wait_base", "wait_c" };

        private static void Guard(Ped ped, Spooner.Placed item, int turn)
        {
            try
            {
                if (item.Armed) Function.Call(Hash.SET_CURRENT_PED_WEAPON, ped.Handle, item.WeaponHash, true);

                var clip = GuardClips[(int)((uint)(Steady(item.At) + turn) % (uint)GuardClips.Length)];
                Give(ped, GuardDict, clip, 1);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not stand a guard: " + ex.Message);
            }
        }

        private static void Stand(Ped ped, Spooner.Placed item, int turn = 0)
        {
            if (item.Guard)
            {
                Guard(ped, item, turn);
                return;
            }

            // A scenario the FILE asked for is still a scenario. Somebody picked it in the
            // spooner and it is not this code's place to substitute something of its own.
            if (!string.IsNullOrEmpty(item.Scenario))
            {
                Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, ped.Handle, item.Scenario, 0, true);
                Function.Call(Hash.SET_PED_KEEP_TASK, ped.Handle, true);
                return;
            }

            // SAME PED, SAME IDLE, EVERY TIME. Taken from where it stands rather than from a
            // dice roll, so a corner you walk past twice is the same corner -- and the next
            // one along each time a beat changes it.
            if (item.Armed)
            {
                Give(ped, Armed[0], Armed[1]);
                return;
            }

            var list = IsHobo(item) ? HoboIdle : Male(ped) ? MenIdle : WomenIdle;
            Scenario(ped, Pick(ped, list, Steady(item.At) + turn));
        }

        /// <summary>How far in front of him something has to be to be worth leaning on.</summary>
        private const float LeanReach = 1.0f;

        /// <summary>
        /// Whether there is something right in front of him to put his shoulder on.
        ///
        /// LEANING ON NOTHING is the pose that gives the whole scene away -- a man at forty
        /// five degrees in the middle of open grass, holding up a wall that is not there.
        /// Michael asked on 2026-09-22 for it to stop, so the scenario is out of the idle
        /// lists entirely and comes back only through here, and through ChoreFor, which
        /// walks him to a streetlight or a fence first and is the honest version of it.
        /// </summary>
        private static bool Leans(Ped ped)
        {
            try
            {
                var at = ped.Position;
                var eye = new Vector3(at.X, at.Y, at.Z + 0.4f);
                var ahead = eye + ped.ForwardVector * LeanReach;

                var hit = World.Raycast(eye, ahead, IntersectFlags.Map | IntersectFlags.Objects, ped);
                return hit.DidHit;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// One of his idles, and a lean instead when he happens to be stood at something.
        /// Every list here is checked against the game's own Scenarios.txt, never remembered.
        /// </summary>
        private static string Pick(Ped ped, string[] list, int turn)
        {
            if (list == null || list.Length == 0) return "WORLD_HUMAN_STAND_IMPATIENT";

            // A quarter of the time, if there is really a wall there.
            if (Dice.Next(100) < 25 && Leans(ped)) return "WORLD_HUMAN_LEANING";

            return list[turn < 0 ? Dice.Next(list.Length) : turn % list.Length];
        }

        /// <summary>
        /// A ROAMER IS NOT SET DRESSING.
        ///
        /// Person locks every ped it makes all the way down, and for good reason: without it
        /// a spooner scene empties itself five minutes after you find it. But the lock is
        /// total -- no permanent events, no non-temporary events, no fleeing, no ragdoll off
        /// the player, holds his spot -- and a man in that state is furniture. Fire a gun
        /// past him and he does not blink. Walk into him and he does not move. Drive at him
        /// and he stands there. That is why they do not read as people, and it is the whole
        /// of it: the walking, the talking and the smoking were all working.
        ///
        /// So for the ones the file says roam, it is let go of. They keep their persistence,
        /// so nothing despawns them, and they keep the path flags, so nobody climbs or
        /// vaults -- only the deadness goes.
        ///
        /// NOT THE ARMED ONES, and not the ones the file says stay. An armed ped the game is
        /// allowed to task is an armed ped the game SENDS SOMEWHERE, which is the exact bug
        /// the lock was written for, and the man behind the counter is supposed to be at the
        /// counter. Michael asked for people on 2026-09-22; the guards and the shopkeeper
        /// are not the people he meant.
        /// </summary>
        private static void Real(Ped ped)
        {
            if (ped == null || !ped.Exists()) return;

            try
            {
                ped.BlockPermanentEvents = false;
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);

                // He moves when you walk into him, and he gets out of the way of a car.
                Function.Call(Hash.SET_PED_CAN_RAGDOLL_FROM_PLAYER_IMPACT, ped.Handle, true);
                Function.Call(Hash.SET_PED_CAN_EVASIVE_DIVE, ped.Handle, true);
                Function.Call(Hash.SET_PED_CONFIG_FLAG, ped.Handle, 17, false);

                // And he runs from a gun like anybody would. The beat has him back on his own
                // business within seconds of it being over -- see Choose.
                Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, ped.Handle, 0, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not bring one round: " + ex.Message);
            }
        }
        /// <summary>
        /// The person on this mark, as a string NPC Mind files him under.
        ///
        /// EXACTLY THE SHAPE HOODRICH USES for its own scenery -- "scene:" + Folk.Mark(model
        /// name, mark) -- and on purpose: the two loaders are the same code, and a man stood
        /// on the same metre in either mod is the same man with the same memory of you.
        ///
        /// Off the MARK, which comes out of the file and is the same every session, and never
        /// off where he happens to be: he roams now, eighty metres from a drifting centre.
        /// </summary>
        private static string Who(Spooner.Placed item)
        {
            return "scene:" + Folk.Mark(item.ModelName, item.At);
        }

        private static bool Male(Ped ped)
        {
            try { return Function.Call<bool>(Hash.IS_PED_MALE, ped.Handle); }
            catch { return true; }
        }

        /// <summary>One scenario, in place, kept.</summary>
        private static void Scenario(Ped ped, string name)
        {
            try
            {
                Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, ped.Handle, name, 0, true);
                Function.Call(Hash.SET_PED_KEEP_TASK, ped.Handle, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not start " + name + ": " + ex.Message);
            }
        }

        /// <summary>
        /// One idle onto one ped, waiting on its dictionary without ending the tick.
        ///
        /// When the dictionary never arrives the ped is left standing rather than handed
        /// something else. A wrong animation is harder to notice than none at all, and none is
        /// what says the name was wrong.
        /// </summary>
        private static void Give(Ped ped, string dict, string clip, int flag = 1)
        {
            try
            {
                Function.Call(Hash.REQUEST_ANIM_DICT, dict);

                if (Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dict))
                {
                    Function.Call(Hash.TASK_PLAY_ANIM, ped.Handle, dict, clip,
                                  8f, -8f, -1, flag, 0f, false, false, false);
                    return;
                }

                Soon.Add(new Later
                {
                    Who = ped,
                    Dict = dict,
                    Clip = clip,
                    Flag = flag,
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
            public int Flag = 1;
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

                    // NPC Mind has him now; Back gives him his idle again when it lets go.
                    if (Mind.Holding(wait.Who)) { Soon.RemoveAt(i); continue; }

                    if (Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, wait.Dict))
                    {
                        Function.Call(Hash.TASK_PLAY_ANIM, wait.Who.Handle, wait.Dict, wait.Clip,
                                      8f, -8f, -1, wait.Flag, 0f, false, false, false);

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

                    // NPC Mind has him now; Back gives him his idle again when it lets go.
                    if (Mind.Holding(wait.Who))
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
        /// beat one of a few things happens: he changes what he is doing; he has a word with
        /// whoever is stood nearest, the two of them turned to each other and talking with
        /// their hands; he throws a gang sign; he stretches his legs -- walks somewhere a few
        /// metres off, stands there a while, walks back; or he walks off altogether, out of
        /// sight, and is back on his mark some minutes later, walking in from wherever he
        /// went. The file's own choices still win: a man the
        /// spooner gave a scenario or an animation comes back to it, never swaps it for one of
        /// ours, and takes his walks half as often.
        ///
        /// AND IN THE SMALL HOURS NOBODY IS THERE. Between QuietFrom and QuietTo on the game's
        /// clock everybody walks off, staggered over a couple of minutes so it is a corner
        /// emptying rather than a cut, and drifts back the same way once it is over. A scene
        /// built during those hours goes up without its people, who arrive when the hour does.
        /// A file can name the ones who never move -- stays: -- for somebody in bed; he is put
        /// on his mark only while nobody can see it.
        ///
        /// ON PROPS THERE IS NO NAVMESH. The ones the file names in straight: -- the balconies,
        /// the roofs, the shop -- walk in straight lines along their own floor, probed with
        /// rays once it has collision (see FloorOf): stood up out of sight a few metres along
        /// it, strolling along it, and walking off along it to somewhere round a wall or out
        /// of frame. With nowhere out of sight to go they stay.
        ///
        /// ALWAYS ON FOOT, NEVER A CUT. Nobody is put anywhere he can be seen: a ped walking
        /// off is only deleted once he is off screen or behind something, and one coming back
        /// is stood up at a point that is out of sight and walks in from it, the last step
        /// walked rather than snapped. Follow him all the way and he waits there until he is
        /// not being watched, and a man -- or a body -- still in view when the scene comes
        /// down stays until he is not (see Stragglers). The one place anybody goes where he
        /// stands is Clear: a reload or the script going away.
        ///
        /// WHAT IT DOES NOT TOUCH. A man NPC Mind has -- talking to you, or running, swinging
        /// or with his hands up over something you did -- is left alone until it lets go, and
        /// then picks his life up from wherever he is stood (see Taken). A ped saved frozen is
        /// unfrozen for the walk and frozen again on the mark. The blocking of events stays on
        /// for everybody but the roamers (see Real) -- the rest do not startle, flee or pick
        /// fights of their own -- because the moment they do, a scene empties itself and stays
        /// empty, which is the thing this whole file exists to prevent.
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

            /// <summary>Whether the file said he ranges wider than his mark. See Roams.</summary>
            public bool Roams;

            /// <summary>Whether he walks straight lines on a floor the navmesh does not know. See "Along his own floor".</summary>
            public bool Straight;

            /// <summary>Where he can get to along his own floor, probed once per build. Null until it is. See FloorOf.</summary>
            public List<Footing> Floor;

            /// <summary>The probe part way through: the bearings still to walk, how many are done, what they found so far. See FloorOf.</summary>
            public List<float> Bearings;
            public List<Footing> Probing;
            public int Probed;
            public float ProbeFar;

            /// <summary>On the last step onto his mark, walked rather than snapped. See the arrival in Pulse.</summary>
            public bool Closing;

            /// <summary>When he first found somebody stood on his line home; 0 while nobody is. See GoHome.</summary>
            public int Blocked;

            /// <summary>Who he went for a stroll with, while he is on it. See Join.</summary>
            public Life Pal;

            /// <summary>When he started waiting to come in along his floor; 0 while he is not. For the backstop in ComeBackAlong.</summary>
            public int Since;

            /// <summary>What he is called, over his head. See Called.</summary>
            public string Name;

            /// <summary>The number NPC Mind knows him by. See Who.</summary>
            public int Id;

            /// <summary>Whether he is one of the ones who walks drunk, and whether it is on him yet.</summary>
            public bool Drunk;
            public bool Drunken;

            /// <summary>How near the mark he got, and when he last got nearer. See the stall check.</summary>
            public float Was;
            public int Moved;

            /// <summary>Whether he is away because of the hour rather than a whim.</summary>
            public bool ForTheNight;

            /// <summary>Which of his idles he is on. See Doing's turn.</summary>
            public int Turn;

            /// <summary>Walks that ran out of time and were asked for again.</summary>
            public int Tries;

            /// <summary>Stood at the far point, waiting to be unwatched before he goes.</summary>
            public bool Waiting;

            /// <summary>What he is waiting on while he is not up, for the log. See NotUp.</summary>
            public string Why;

            /// <summary>He lives in a camp, and what he is on or walking to there: a seat, a bed. See Camp.</summary>
            public bool Camp;
            public Spooner.Placed Seat;
            public Spooner.Placed Bed;
            public bool Asleep;

            /// <summary>His jobs -- his mark first -- which he is at, which he is walking to, and the corners left on the way. See Work.</summary>
            public List<Job> JobList;
            public int JobAt;
            public int JobTo = -1;
            public Queue<Vector3> Legs;

            /// <summary>Who he is talking to, while he is.</summary>
            public Life With;

            /// <summary>The scenario waiting at Going, for a hobo on a chore, and the thing he faces for it.</summary>
            public string Chore;
            public Vector3 Face;

            /// <summary>When he next says something, and whether he opened the chat he is in.</summary>
            public int NextLine;
            public bool Speaks;

            /// <summary>Stood up on his far spot and frozen, waiting for the ground there before the walk.</summary>
            public bool Held;

            /// <summary>When NPC Mind -- or a run, or a fight -- took him off us; 0 while he is ours. See Taken.</summary>
            public int TakenAt;

            /// <summary>His voice and everything in it, dealt a line at a time. Null without voices.txt. See Core.Voices.</summary>
            public Talk Talk;
        }

        /// <summary>
        /// Somewhere a man can stand along his own floor: the point at the height of his
        /// middle, how far along from his mark it is, and which of the probe's bearings it
        /// lies on. See FloorOf.
        /// </summary>
        private sealed class Footing
        {
            public Vector3 At;
            public float D;
            public int Bearing;
        }

        /// <summary>How long a man walking in waits on his far spot for the ground before he walks anyway.</summary>
        private const int HoldMostMs = 15000;

        private enum Stage
        {
            /// <summary>On the mark, doing his idle. NextAt is the next beat.</summary>
            Marked,

            /// <summary>Walking to Going, a few metres off.</summary>
            Strolling,

            /// <summary>Stood at Going. NextAt is when he heads back.</summary>
            Loitering,

            /// <summary>Talking to With. NextAt is when they are done.</summary>
            Chatting,

            /// <summary>A sign thrown up. NextAt is when the idle comes back.</summary>
            Signing,

            /// <summary>Walking back to the mark.</summary>
            Returning,

            /// <summary>Walking to Going, well off, to be deleted once nobody is looking.</summary>
            Leaving,

            /// <summary>Deleted. NextAt is when he is due back, unless it is the hour.</summary>
            Away,

            /// <summary>Walking to an outing, well off the mark. See Outing.</summary>
            Outbound,

            /// <summary>At the outing: dancing, smoking, a word with whoever is stood there.</summary>
            Partying,

            /// <summary>Stood up at Going, walking in to the mark.</summary>
            ComingBack,

            /// <summary>Turning back round on his mark after NPC Mind let go of him. NextAt is when his idle comes back. See Back.</summary>
            Turning,

            /// <summary>Walking between his jobs, a corner at a time. Going is the next corner. See Work.</summary>
            Shifting
        }

        private readonly List<Life> _lives = new List<Life>();
        private int _nextBeat;
        private const int BeatEveryMs = 900;

        /// <summary>Whether it is the small hours, and whether that has been looked at yet this session.</summary>
        private bool _quiet;
        private bool _quietKnown;

        /// <summary>
        /// Whether the quiet hours are allowed to start. Not until the clock has been seen
        /// OUTSIDE them once: a save that loads at four in the morning -- Michael's does --
        /// otherwise opens on empty corners for six real minutes, which to anybody who has
        /// just installed the mod is a mod with nobody in it. The people are built on a fresh
        /// load whatever the hour, and drift off the next time the clock strikes the hour.
        /// </summary>
        private bool _quietArmed;

        private bool LifeOn => _cfg == null || _cfg.SceneryLife;

        /// <summary>How long between beats for one ped: three quarters of a minute to two and a half on the real clock.</summary>
        private const int BeatLeastMs = 45000;
        private const int BeatMostMs = 150000;

        /// <summary>How far a stroll goes, and how far a walk-off goes.</summary>
        private const float StrollLeast = 4f;
        private const float StrollMost = 12f;

        /// <summary>A roamer barely stops: a short beat and a short stand, so he stays on his feet.</summary>
        private const int RoamBeatLeastMs = 6000;
        private const int RoamBeatMostMs = 18000;
        private const int RoamLoiterLeastMs = 5000;
        private const int RoamLoiterMostMs = 14000;

        /// <summary>One drift: how far he moves on from wherever he is stood. See Stroll.</summary>
        private const float RoamStepLeast = 5f;
        private const float RoamStepMost = 22f;

        /// <summary>
        /// An outing: how long the walk is given, how long he stays, how many may be gone at
        /// once, and the chance of one per beat. Four minutes for the walk, because Lamar's is
        /// 118 m off and that is ninety seconds at a walking pace before the first fence is in
        /// the way. Six at once, so a party up the road never empties the place.
        /// </summary>
        private const int OutingWalkMs = 240000;
        private const int PartyLeastMs = 90000;
        private const int PartyMostMs = 240000;
        private const int OutingAtOnce = 6;
        private const int OutingChance = 6;
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

        /// <summary>
        /// The long leg: walking in to a mark, or home from an outing. Two and a half
        /// minutes, because sixty metres round a fence is well over the forty-five seconds a
        /// stroll gets, and a hundred and eighteen metres back from Lamar's is ninety even
        /// unobstructed. ComeInTries is how many goes he gets before he is simply put there.
        /// </summary>
        private const int ComeInMs = 150000;
        private const int ComeInTries = 3;

        /// <summary>
        /// How long he may fail to get any nearer before the walk is treated as over. A ped
        /// on his way does NOTHING else -- no idle, no scenario, no word with anybody -- so a
        /// nav task that quietly gave up leaves a man stood in the street for the whole of
        /// ComeInMs looking like the mod has died. Twelve seconds of no ground made is a
        /// walk that is not happening.
        /// </summary>
        private const int StallMs = 12000;
        private const float There = 1.6f;

        /// <summary>Beyond this from the player, and unseen, a man walking off simply goes.</summary>
        private const float GoneAt = 35f;

        /// <summary>A man coming back is stood up no nearer the player than this.</summary>
        private const float ArriveNoNearer = 25f;

        /// <summary>A whole scene leaving or coming back is spread over this long.</summary>
        private const int StaggerMs = 150000;

        /// <summary>The nearer ring a ground walk-in is stood up in when every far way in is in view.</summary>
        private const float ArriveLeast = 20f;

        /// <summary>How long a man with nowhere to walk off to waits before he looks again.</summary>
        private const int LeaveRetryMs = 15000;

        /// <summary>More than this above or below his mark is not his floor: a man knocked off a balcony cannot walk back up.</summary>
        private const float OffFloor = 1.5f;

        /// <summary>
        /// THE LAST STEP IS WALKED. A nav walk stops within There of the mark and Home used
        /// to snap the rest, which is a flick of up to a metre and a half in front of you.
        /// Now the last step is a go-straight with a short slide, and Home has at most Snug
        /// left to put right. CloseMs is how long it gets; when it runs out with him still
        /// farther off than Snug he is asked for the step again, and is only ever put the
        /// rest of the way once neither he nor the mark can be seen.
        /// </summary>
        private const float Snug = 0.3f;
        private const int CloseMs = 5000;

        /// <summary>
        /// Where a man is looked for, above his middle -- a mark is saved about a metre above
        /// the floor. Head, chest and knee: all three have to be hidden for him to be behind
        /// something. See Behind.
        /// </summary>
        private const float KneeUp = -0.5f;
        private const float ChestUp = 0.3f;
        private const float HeadUp = 0.7f;

        /// <summary>
        /// The floor probe: how fine it steps, how far it looks, how many ways it looks, how
        /// far either side of the line there has to be floor, how short of a wall or an edge
        /// it stops, how much a floor may rise or fall in a step, how far apart the points it
        /// keeps are, and how far the go-straight task is allowed to slide at the end.
        /// See FloorOf.
        /// </summary>
        private const float AlongStep = 0.5f;
        private const float AlongMost = 16f;
        private const int AlongBearings = 16;
        private const float AlongWidth = 0.2f;
        private const float AlongShort = 0.6f;
        private const float AlongLevel = 0.3f;
        private const float AlongSpacing = 1.5f;
        private const float AlongSlide = 0.25f;

        /// <summary>How far along his floor a stroll goes, a walk-off has to go, and a walk-in starts from; and no nearer the player than this.</summary>
        private const float AlongStrollLeast = 2f;
        private const float AlongStrollMost = 10f;
        private const float AlongLeaveLeast = 4f;
        private const float AlongArriveLeast = 3f;
        private const float AlongArriveMost = 12f;
        private const float AlongNoNearer = 5f;

        /// <summary>
        /// THE SHAPE TESTS ARE SYNCHRONOUS AND THE BUDGET IS PER BEAT, AND IT COUNTS RAYS,
        /// NOT CALLS. A floor probe is up to sixty rays a bearing on an open roof -- twenty
        /// bearings is over a thousand in one tick, a hitch you can see -- so one man probes
        /// a beat and he does AlongBearingsPerProbe of his bearings at a time, the rest next
        /// beat. A look for a hidden spot is up to three rays a point (see Behind): the
        /// points looked at are one a bearing, the farthest first, and AlongLooksPerBeat is
        /// how many Behind gets asked in a beat, by everybody put together. When it runs out
        /// mid-look the man asks again next beat rather than settling for a worse point.
        /// Everybody else waits a second.
        /// </summary>
        private const int AlongRetryMs = 4000;
        private const int AlongProbesPerBeat = 1;
        private const int AlongBearingsPerProbe = 5;
        private const int AlongLooksPerBeat = 24;
        private static int _probes, _looks;

        /// <summary>How long a man on his own floor waits for the line home to clear before he walks it anyway. See GoHome.</summary>
        private const int BlockedMostMs = 30000;

        /// <summary>The chance a stroll brings the nearest neighbour along, how near the two must end up to talk, and how far short of the spot he stands.</summary>
        private const int PalChance = 50;
        private const float PalReach = 3f;
        private const float PalBeside = 1.2f;

        /// <summary>
        /// A man still in view when his scene came down, left standing until he is not.
        /// See Drop and Stragglers.
        /// </summary>
        private sealed class Straggler
        {
            public Scene Scene;
            public Spooner.Placed Item;
            public Ped Who;
        }

        private static readonly List<Straggler> _stragglers = new List<Straggler>();

        /// <summary>Past this from the player a straggler cannot be drawn (LodMost and a margin), and goes whether or not he is in frame.</summary>
        private const float StragglerFar = 550f;

        // ---- talking ------------------------------------------------------------

        /// <summary>
        /// AUDIBLE. A ped stood on a corner in the base game says things; ours were mute, and
        /// Michael asked on 2026-09-21 for all of them to be talking out loud. So: two in a
        /// chat take turns every few seconds, a statement and a response, in their models'
        /// own voices; and anybody on his mark says something to nobody in particular now
        /// and then. Only within earshot of the player, and no more than one line across
        /// every scene every couple of seconds, because fifty men each with something to say
        /// is a crowd noise rather than a corner. The set is kept quiet round Franklin by
        /// Affiliation.CalmHome so the game stops challenging him on his own block; the gag
        /// comes off for our line and CalmHome puts it back, the same as BlockTalk does.
        /// </summary>
        /// <summary>
        /// WHAT THEY CAN ACTUALLY SAY.
        ///
        /// A speech name a voice does not have plays NOTHING, in silence, exactly like a
        /// name that is spelled wrong -- and half of what was written here was in that
        /// state. Checked against menyooStuff\PedSpeechList.txt on 2026-09-22:
        /// GENERIC_HOWS_IT_GOING, GENERIC_YES and GENERIC_NO are on NONE of the four
        /// Families voices, and PED_RANT is on one tramp model and no one else here. So two
        /// of every three idle lines and half of every conversation were a man opening his
        /// mouth and no sound coming out, which is why Michael could not hear anybody.
        ///
        /// The Families sets below are the intersection of all four Families voices -- 89
        /// names, these are the ones worth hearing. The plain sets are what every other
        /// model in the scene that has a voice at all can manage.
        /// </summary>
        private static readonly string[] FamIdle =
        {
            "CHAT_STATE", "GENERIC_HI", "GENERIC_CHEER", "GENERIC_WHATEVER",
            "NICE_CAR", "GUN_COOL", "LOOKING_AT_PHONE", "KIFFLOM_GREET"
        };

        private static readonly string[] FamChat = { "CHAT_STATE", "GENERIC_HI", "GENERIC_WHATEVER" };
        private static readonly string[] FamReply = { "CHAT_RESP", "GENERIC_THANKS", "GENERIC_WHATEVER", "APOLOGY_NO_TROUBLE" };

        /// <summary>Thrown with the sign, and only by the ones whose voices carry it.</summary>
        private static readonly string[] FamSign =
        {
            "SHOUT_THREATEN_GANG", "CHALLENGE_THREATEN", "PROVOKE_GENERIC", "GENERIC_INSULT_MED"
        };

        /// <summary>Half of a phone call, for the ones stood on a phone scenario.</summary>
        private static readonly string[] FamPhone =
        {
            "PHONE_CONV1_CHAT1", "PHONE_CONV2_CHAT2", "PHONE_CONV3_CHAT1", "PHONE_CONV4_CHAT3",
            "PHONE_CONV5_CHAT2", "PHONE_CONV6_CHAT1", "PHONE_CONV7_CHAT3", "PHONE_CONV8_CHAT2"
        };

        private static readonly string[] AnyIdle = { "GENERIC_HI", "GENERIC_WHATEVER", "GENERIC_THANKS", "KIFFLOM_GREET" };
        private static readonly string[] AnyChat = { "GENERIC_HI", "GENERIC_WHATEVER" };
        private static readonly string[] AnyReply = { "GENERIC_THANKS", "GENERIC_WHATEVER" };

        /// <summary>CHAT_STATE and GENERIC_HI are on every tramp model here; PED_RANT is on one.</summary>
        private static readonly string[] HoboLines = { "CHAT_STATE", "GENERIC_HI", "GENERIC_WHATEVER" };

        /// <summary>The four Families models, by hash, so the right set is picked at runtime.</summary>
        private static readonly string[] FamModels =
        {
            "g_m_y_famca_01", "g_m_y_famdnf_01", "g_m_y_famfor_01", "g_f_y_families_01"
        };

        private static bool Fam(Spooner.Placed item)
        {
            if (item == null) return false;

            foreach (var name in FamModels)
            {
                if (unchecked((uint)item.ModelHash) == Names.Joaat(name)) return true;
            }

            return false;
        }

        /// <summary>His idle chatter, off whichever list his voice can manage.</summary>
        private static string[] IdleFor(Life l)
        {
            if (IsHobo(l.Item)) return HoboLines;
            if (!Fam(l.Item)) return AnyIdle;

            // On the phone, he is on the phone about something.
            var sc = l.Item.Scenario ?? "";
            if (sc.IndexOf("MOBILE", StringComparison.OrdinalIgnoreCase) >= 0) return FamPhone;

            return FamIdle;
        }

        private const float Earshot = 30f;
        private const int LineGapMs = 1500;
        private const int ChatTurnLeastMs = 3000;
        private const int ChatTurnMostMs = 6000;
        /// <summary>
        /// How often one man says something to nobody in particular. Twelve to thirty-two seconds:
        /// Michael wants all of them talking, and it was twenty-five to seventy.
        /// </summary>
        private const int IdleLineLeastMs = 12000;
        private const int IdleLineMostMs = 32000;
        private int _nextAnyLine;

        private void Say(Life l, Voices.Kind kind, string[] lines, int now)
        {
            var ped = l.Who;
            if (ped == null || !ped.Exists() || !ped.IsAlive) return;
            if (now < _nextAnyLine) return;

            // Quiet while NPC Mind has somebody talking to you: his is the voice that should carry.
            if (Mind.Talking) return;

            try
            {
                Function.Call(Hash.BLOCK_ALL_SPEECH_FROM_PED, ped.Handle, false, false);

                // HIS OWN VOICE AND EVERYTHING IN IT, the next card off his deck -- so he says all
                // of it before anything twice (see Core.Voices). The short lists are only for an
                // install without voices.txt.
                var line = l.Talk != null ? l.Talk.Next(kind, ped) : null;

                if (line != null)
                {
                    Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_WITH_VOICE_NATIVE, ped.Handle, line, l.Talk.Voice,
                                  "SPEECH_PARAMS_FORCE", false);
                }
                else
                {
                    Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, ped.Handle, lines[Dice.Next(lines.Length)], "SPEECH_PARAMS_FORCE");
                }
                _nextAnyLine = now + LineGapMs;
            }
            catch
            {
                // A missing line costs nothing.
            }
        }

        /// <summary>How far round his mark a hobo looks for something to do, how long he does it, and how close he stands.</summary>
        private const float ForageReach = 10f;
        private const int ChoreLeastMs = 30000;
        private const int ChoreMostMs = 90000;
        private const float ChoreStandOff = 1.1f;

        /// <summary>
        /// Something said to nobody in particular, now and then, while he is within earshot of you:
        /// on his mark, stood about, or at the party. The next line off his deck; on a phone, the
        /// next line of his call.
        /// </summary>
        private void Chatter(Life l, Ped ped, int now, Vector3 here)
        {
            if (now < l.NextLine || ped.Position.DistanceTo(here) > Earshot) return;

            Say(l, Voices.Kind.Idle, IdleFor(l), now);
            l.NextLine = now + Between(IdleLineLeastMs, IdleLineMostMs);
        }

        /// <summary>How near somebody has to be stood to be worth a word, and how long the word is.</summary>
        private const float ChatReach = 6f;
        private const int ChatLeastMs = 20000;
        private const int ChatMostMs = 45000;

        /// <summary>
        /// A sign is upper body, on top of the running task: 16 is the upper-body flag and 32
        /// the secondary one on this build. If that ever reads differently the idle is given
        /// back five seconds later regardless, which is SignMs.
        /// </summary>
        private const int SignFlag = 48;
        private const int SignMs = 5000;

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
            // A roamer is asked again in seconds rather than minutes: standing on the mark is
            // the thing he does least. See Choose.
            if (l.Roams && !l.Stays && !l.Scripted) return Between(RoamBeatLeastMs, RoamBeatMostMs);

            var ms = Between(BeatLeastMs, BeatMostMs);
            return l.Scripted ? ms * 2 : ms;
        }

        /// <summary>The hour on the game's clock, or -1 when it cannot be read.</summary>
        private static int ClockHour()
        {
            try { return Function.Call<int>(Hash.GET_CLOCK_HOURS); }
            catch { return -1; }
        }

        /// <summary>Whether the game's clock is inside the quiet hours.</summary>
        private bool QuietNow()
        {
            if (_cfg == null) return false;

            var from = _cfg.SceneryQuietFrom;
            var to = _cfg.SceneryQuietTo;
            if (from == to) return false;

            var hour = ClockHour();
            if (hour < 0) return false;

            return from < to ? hour >= from && hour < to : hour >= from || hour < to;
        }

        /// <summary>Whether that spot is in the camera's frame right now. Only the frame: a wall in the way does not count. See Behind.</summary>
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

        /// <summary>Where the picture is actually taken from: the rendered camera, whatever is driving it.</summary>
        private static Vector3 Eye()
        {
            try { return Function.Call<Vector3>(Hash.GET_FINAL_RENDERED_CAM_COORD); }
            catch { return GameplayCamera.Position; }
        }

        /// <summary>
        /// Whether a man stood at that point is hidden from the camera by something solid.
        ///
        /// IN FRAME IS NOT IN VIEW. IS_SPHERE_VISIBLE and IS_ENTITY_ON_SCREEN only say
        /// whether the point is inside the camera's frustum; a man round the corner of the
        /// block is inside it and cannot be seen. "Behind a wall" needs a line from the eye,
        /// and it needs three of them -- head, chest and knee -- because a man whose head
        /// shows over a parapet is a man you can see. All three have to be blocked, and only
        /// by the map or a kept building (see Wall): a fence, a railing or a pane of glass
        /// is something you look through, so those never hide anybody. A hit within a
        /// quarter of a metre of the sample is the floor at his feet, or him, and counts as
        /// a clear line. Glass is left out of the ray's flags on purpose.
        /// </summary>
        private static bool Behind(Scene scene, Vector3 root)
        {
            try
            {
                var eye = Eye();
                var me = Game.Player.Character;

                // Head first: one clear line is enough to be seen, and the head is the likeliest.
                foreach (var up in new[] { HeadUp, ChestUp, KneeUp })
                {
                    var at = new Vector3(root.X, root.Y, root.Z + up);
                    var hit = World.Raycast(eye, at, IntersectFlags.Map | IntersectFlags.Objects, me);

                    if (!hit.DidHit) return false;
                    if ((at - hit.HitPosition).Length() < 0.25f) return false;
                    if (!Wall(scene, hit)) return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Whether what a ray hit is something a man cannot be seen through: the map, or one
        /// of the buildings the file keeps up between builds. See-through materials are not,
        /// whatever they belong to, and neither is any other prop -- a sign, a lamp post, a
        /// fence -- because you see past those.
        /// </summary>
        private static bool Wall(Scene scene, RaycastResult hit)
        {
            switch (hit.MaterialHash)
            {
                case MaterialHash.MetalChainLinkLarge:
                case MaterialHash.MetalChainLinkSmall:
                case MaterialHash.MetalGrille:
                case MaterialHash.GlassShootThrough:
                case MaterialHash.GlassBulletproof:
                case MaterialHash.EmissiveGlass:
                case MaterialHash.PhysBarbedWire:
                case MaterialHash.PhysElectricFence:
                    return false;
            }

            var e = hit.HitEntity;

            // Nothing behind the hit is the map itself.
            if (e == null || !e.Exists()) return true;

            Spooner.Placed was;

            if (scene.Was.TryGetValue(e.Handle, out was) && was != null)
            {
                return scene.Keeps.Contains(was.Handle) ||
                       scene.KeepModels.Contains(unchecked((uint)was.ModelHash));
            }

            // Between builds the kept buildings are off the books and on this list.
            foreach (var kept in scene.Kept.Values)
            {
                if (kept != null && kept.Handle == e.Handle) return true;
            }

            return false;
        }

        /// <summary>Whether nobody can see a man stood at that point: out of frame, or behind something. See Behind.</summary>
        private static bool OutOfSight(Scene scene, Vector3 root) => !Seen(root) || Behind(scene, root);

        /// <summary>Whether nobody can see this man: off screen, or behind something. See Behind.</summary>
        private static bool Unseen(Scene scene, Ped ped) => !ped.IsOnScreen || Behind(scene, ped.Position);

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

        /// <summary>
        /// Whether he is there, near enough. HEIGHT COUNTS: this used to be measured flat, so
        /// a man stood on the ground under a balcony mark was "there" and Home lifted him four
        /// metres onto it. A navmesh point is about a metre below a ped's middle, which is
        /// still inside OffFloor.
        /// </summary>
        private static bool Arrived(Ped ped, Vector3 at)
        {
            if (Math.Abs(ped.Position.Z - at.Z) > OffFloor) return false;
            return Flat(ped.Position - at) <= There;
        }

        /// <summary>The length of a difference measured flat, with the height left out.</summary>
        private static float Flat(Vector3 d)
        {
            d.Z = 0f;
            return d.Length();
        }

        /// <summary>The heading that faces from one point to another, in the game's degrees.</summary>
        private static float HeadingTo(Vector3 from, Vector3 to) =>
            Function.Call<float>(Hash.GET_HEADING_FROM_VECTOR_2D, to.X - from.X, to.Y - from.Y);

        /// <summary>
        /// Walk there. HOW LONG IT IS ALLOWED matters: the number goes into the nav task
        /// itself, and the game abandons the walk when it runs out. Forty-five seconds is a
        /// stroll; a man walking in from sixty metres round a fence line, or home from the
        /// party a hundred and eighteen metres off, needs the long one or he stops in the
        /// street and is put on his mark by the deadline below.
        ///
        /// ON THE NAVMESH, which is the ground and nothing a script put down. A man on a prop
        /// floor walks with GoStraight instead; Walk picks between the two.
        /// </summary>
        private static void WalkTo(Ped ped, Vector3 to, float heading, int ms = WalkMs)
        {
            Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);
            Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, ped.Handle,
                          to.X, to.Y, to.Z, 1.0f, ms, There, 0, heading);
            Function.Call(Hash.SET_PED_KEEP_TASK, ped.Handle, true);
        }

        /// <summary>
        /// Walk there in a straight line, with no navmesh under it. A prop floor has none, so
        /// this is the only walk a man on a balcony can take (see "Along his own floor"), and
        /// it is also the last step onto any mark: the arguments are x, y, z, speed, the time
        /// allowed, the heading to end up facing and how far short it may slide to a stop.
        /// It goes round nothing, which is why every line it is given has been probed first.
        /// </summary>
        private static void GoStraight(Ped ped, Vector3 to, float heading, int ms)
        {
            Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);
            Function.Call(Hash.TASK_GO_STRAIGHT_TO_COORD, ped.Handle,
                          to.X, to.Y, to.Z, 1.0f, ms, heading, AlongSlide);
            Function.Call(Hash.SET_PED_KEEP_TASK, ped.Handle, true);
        }

        /// <summary>
        /// The one walk dispatcher: the navmesh for a man on the ground, a straight line for
        /// a man on his own floor. Every return path goes through here so both kinds do the
        /// right thing.
        /// </summary>
        private static void Walk(Life l, Vector3 to, float heading, int ms = WalkMs)
        {
            l.Closing = false;

            if (l.Straight) GoStraight(l.Who, to, heading, ms);
            else WalkTo(l.Who, to, heading, ms);
        }

        /// <summary>
        /// Home to his mark, on foot, and Returning until he is there. False, and he has not
        /// moved, when he is waiting for the way home to clear; NextAt says when to ask again.
        ///
        /// NOBODY ON THE LINE HOME. A straight walk goes round nobody (see LineBlocked), so a
        /// man on his own floor does not set off while somebody is stood between him and his
        /// mark -- the mate he strolled out with, walking home ahead of him, mostly -- and
        /// looks again every second. Not for ever: after BlockedMostMs he goes anyway, into
        /// the same stall handling as any walk-in that meets somebody, which only ever puts
        /// him on his mark once nobody can see it.
        /// </summary>
        private static bool GoHome(Life l, int now, int ms = ComeInMs)
        {
            if (l.Straight && l.Who != null && l.Who.Exists() &&
                LineBlocked(l.Who.Position, l.Item.At, l.Who.Handle))
            {
                if (l.Blocked == 0) l.Blocked = now;

                if (now - l.Blocked < BlockedMostMs)
                {
                    l.NextAt = now + 1000;
                    return false;
                }

                Log.Debug(Say(l.Item) + " in " + l.Scene.Name + " has had somebody on his way home for a while; going anyway.");
            }

            l.Blocked = 0;

            Loose(l);
            Walk(l, l.Item.At, l.Item.Yaw, ms);

            l.State = Stage.Returning;
            l.NextAt = now + ms;
            l.Tries = 0;
            l.Was = 0f;
            l.Moved = now;
            l.Pal = null;
            l.With = null;
            return true;
        }

        /// <summary>Off the mark: unfrozen and allowed to move. The blocking of events stays on.</summary>
        private static void Loose(Life l)
        {
            // Whatever he is loosed for, he is not waiting on his way home any more.
            l.Blocked = 0;

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

            // A roamer is never pinned again -- Real let him off it on purpose, and Home is
            // called every time he finishes anything.
            if (!l.Roams || l.Stays) Function.Call(Hash.SET_PED_CONFIG_FLAG, ped.Handle, 17, true);

            ped.IsPositionFrozen = l.Item.Frozen;

            Doing(l.Scene, ped, l.Item, l.Turn);
        }

        /// <summary>Something to do while stood about somewhere that is not his mark.</summary>
        private static void LoiterIdle(Life l)
        {
            var ped = l.Who;
            if (ped == null || !ped.Exists()) return;

            Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);

            if (l.Item.Guard)
            {
                Guard(ped, l.Item, l.Turn);
                return;
            }

            if (l.Item.Armed)
            {
                Give(ped, Armed[0], Armed[1]);
                return;
            }

            var list = Male(ped) ? MenIdle : WomenIdle;
            Scenario(ped, Pick(ped, list, -1));
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

        // ---- NPC Mind's turn ------------------------------------------------------------

        /// <summary>
        /// Whether he is somebody else's right now -- and the moment he stops being, his life
        /// handed back to him.
        ///
        /// NPC MIND RUNS HIM WHILE IT HAS HIM. Walk up and press E and he is NPC Mind's: it
        /// turns him to you, talks in his voice, and does whatever he decides about you -- a
        /// swing, a run, his hands up, a call to the police. Every beat here used to re-task
        /// him regardless, and a roamer is asked again every few seconds, so the man you were
        /// talking to would have walked off in the middle of his own sentence and one running
        /// from your gun gone back to his smoke. Michael asked on 2026-09-24 for every one of
        /// them to be run by NPC Mind.
        ///
        /// So nothing here touches him while NPC Mind says it has him (Mind.Holding), nor
        /// while he is running, fighting or down whoever started it -- a roamer is off the
        /// lock and runs from a gun like anybody (see Real) -- and then Back picks his life up
        /// from wherever it left him.
        /// </summary>
        private bool Taken(Life l, Ped ped, int now)
        {
            if (Mind.Holding(ped) || Shaken(ped))
            {
                if (l.TakenAt == 0)
                {
                    l.TakenAt = now;
                    Log.Debug(Say(l.Item) + " in " + l.Scene.Name + " is busy with something that is not ours; left alone.");
                }

                return true;
            }

            if (l.TakenAt == 0) return false;

            l.TakenAt = 0;
            Back(l, ped, now);
            return true;
        }

        /// <summary>Running, fighting, or on the ground: there is nothing to give him until he is done.</summary>
        private static bool Shaken(Ped ped)
        {
            try
            {
                var h = ped.Handle;

                return Function.Call<bool>(Hash.IS_PED_FLEEING, h) ||
                       Function.Call<bool>(Hash.IS_PED_IN_COMBAT, h, 0) ||
                       Function.Call<bool>(Hash.IS_PED_IN_MELEE_COMBAT, h) ||
                       Function.Call<bool>(Hash.IS_PED_RAGDOLL, h) ||
                       Function.Call<bool>(Hash.IS_PED_GETTING_UP, h);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>How near his mark counts as never having left it, and how long he gets to turn back round.</summary>
        private const float OnMark = 0.5f;
        private const int TurnMs = 1500;

        /// <summary>How near the party he has to still be to carry on at it.</summary>
        private const float PartyStay = 20f;

        /// <summary>
        /// His life back, from wherever he is stood when NPC Mind lets go of him -- or when the
        /// running and the fighting are over.
        ///
        /// NEVER PUT ANYWHERE. He is still in front of you, so a man who never left his mark is
        /// turned back round to it on his feet before his idle comes back, and one who ran off
        /// walks home. A roamer picks the next thing from where he is, the same as after
        /// anything else, and a man at the party carries on at the party.
        /// </summary>
        private void Back(Life l, Ped ped, int now)
        {
            // NPC Mind lets the events through when it lets go -- right for a pedestrian, wrong
            // for anybody the file keeps on the lock. See Real for who is off it.
            try
            {
                if (!l.Scene.Roaming(l.Item.Handle) || l.Item.Armed)
                {
                    ped.BlockPermanentEvents = true;
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, true);
                }
            }
            catch
            {
                // He keeps what he has.
            }

            Log.Debug(Say(l.Item) + " in " + l.Scene.Name + " is ours again.");

            // NEVER TAKEN OFF IT. NPC Mind leaves a man on a scenario on it and only turns his
            // head, so a smoke on the mark or a stand-about is still going: the beat carries on.
            if ((l.State == Stage.Marked || l.State == Stage.Loitering || l.State == Stage.Partying ||
                 (l.State == Stage.Leaving && l.Waiting)) &&
                Function.Call<bool>(Hash.IS_PED_USING_ANY_SCENARIO, ped.Handle))
            {
                return;
            }

            l.With = null;
            l.Pal = null;
            l.Closing = false;
            l.Chore = null;
            l.Held = false;
            l.Waiting = false;
            l.Seat = null;
            l.Bed = null;
            l.Asleep = false;

            // The small hours came round while he was busy: he goes, the way everybody went
            // -- and with nowhere to go from where he stands, he goes home first.
            if (_quiet && !l.Stays && !l.Camp && Leave(l, now, true)) return;

            if ((l.State == Stage.Partying || l.State == Stage.Outbound) && ped.Position.DistanceTo(l.Going) <= PartyStay)
            {
                l.State = Stage.Partying;
                l.NextAt = now + Between(PartyLeastMs, PartyMostMs);
                Revel(l);
                return;
            }

            // A MAN AT HIS OTHER JOB carries on at it where he stands, rather than walking home
            // across the shop through the shelves. See Work.
            if (l.JobAt > 0)
            {
                var jobs = JobsOf(l);

                if (l.JobAt < jobs.Count && ped.Position.DistanceTo(jobs[l.JobAt].At) <= JobStay)
                {
                    AtJob(l, l.JobAt, now);
                    return;
                }

                l.JobAt = 0;
            }

            l.JobTo = -1;
            l.Legs = null;

            var most = l.Scene.RoamMost > 0f ? l.Scene.RoamMost : StrollMost;

            if (l.Roams && !l.Stays && ped.Position.DistanceTo(l.Item.At) <= most)
            {
                l.State = Stage.Marked;
                Choose(l, now);
                return;
            }

            // OFF HIS FLOOR. A man knocked off a balcony cannot walk back up: he stands where
            // he landed until he can be put back with nobody looking. See the Returning stage.
            if (l.Straight && Math.Abs(ped.Position.Z - l.Item.At.Z) > OffFloor)
            {
                LoiterIdle(l);

                l.State = Stage.Returning;
                l.Tries = ComeInTries;
                l.NextAt = now;
                l.Was = 0f;
                l.Moved = now;
                return;
            }

            Loose(l);

            var off = ped.Position - l.Item.At;
            off.Z = 0f;

            if (off.Length() <= OnMark)
            {
                // Round to the way he faces, then Home and his idle -- by which time Home has
                // next to nothing left to put right.
                Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);
                Function.Call(Hash.TASK_ACHIEVE_HEADING, ped.Handle, l.Item.Yaw, TurnMs);

                l.State = Stage.Turning;
                l.NextAt = now + TurnMs;
                return;
            }

            Walk(l, l.Item.At, l.Item.Yaw, ComeInMs);

            l.State = Stage.Returning;
            l.NextAt = now + ComeInMs;
            l.Tries = 0;
            l.Was = 0f;
            l.Moved = now;
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
                if (l.State == Stage.Marked || l.State == Stage.Loitering ||
                    l.State == Stage.Chatting || l.State == Stage.Signing ||
                    l.State == Stage.Partying)
                {
                    l.NextAt = now + Dice.Next(StaggerMs);
                }

                return;
            }

            if (l.State == Stage.Away)
            {
                l.ForTheNight = false;
                l.NextAt = now + Dice.Next(StaggerMs);
            }

            // AND A CAMP WAKES UP, at a stagger of its own. See TurnIn.
            if (l.Camp && l.Asleep) l.NextAt = now + Dice.Next(StaggerMs);
        }

        /// <summary>
        /// WHAT HE IS CALLED. Off the mark rather than a roll, so the man on the corner is
        /// the same man every session -- which is the whole point of a name: you come back
        /// and he is still Dee.
        /// </summary>
        private static readonly string[] MenNames =
        {
            "Dee", "T-Bone", "Smokey", "Rodney", "Marcus", "Trey", "Lil Rick", "Pooh",
            "Ju", "Cutt", "Bird", "Deuce", "Ray-Ray", "Slim", "Moose", "Tank",
            "Fats", "Gee", "Loco", "Shorty", "Peanut", "Tre", "Dre", "Kev",
            "Big Mike", "Doobie", "Snap", "Wink", "Rell", "Boo"
        };

        private static readonly string[] WomenNames =
        {
            "Keisha", "Tanya", "Nay", "Roz", "Shay", "Deja", "Mika", "Trina",
            "Lala", "Bree", "Nita", "Cass", "Peaches", "Momma D", "Simone"
        };

        private static string Called(Spooner.Placed item, Ped ped)
        {
            try
            {
                var list = Male(ped) ? MenNames : WomenNames;
                return list[Steady(item.At) % list.Length];
            }
            catch
            {
                return null;
            }
        }

        /// <summary>How near, and how many at once, a name is put over somebody.</summary>
        private const float TagReach = 18f;
        private const int TagsMost = 10;

        /// <summary>The head bone, for hanging the name off.</summary>
        private const int Skull = 31086;

        /// <summary>
        /// Their names over their heads, for the ones near enough to read and not through a
        /// wall. TEXT AND NOT RECTANGLES on purpose: text comes out of its own buffer and
        /// does not spend the frame's DRAW_RECT budget, which on this install is the thing
        /// that makes other mods' HUDs disappear. Ten at once is the cap.
        /// </summary>
        private void Tags()
        {
            if (_cfg != null && !_cfg.ParkviewNames) return;

            var me = Game.Player.Character;
            if (me == null || !me.Exists()) return;

            var drawn = 0;

            // NPC MIND'S OWN TAG IS OVER THIS ONE, and it is the same name a hair higher up --
            // drawn twice it reads as a smudge. The man it is talking to has his name on its
            // panel. Neither gets ours.
            var theirs = Mind.Target;
            var talking = Mind.Partner;

            foreach (var l in _lives)
            {
                if (drawn >= TagsMost) break;
                if (l.State == Stage.Away) continue;

                var ped = l.Who;
                if (ped == null || !ped.Exists() || !ped.IsAlive) continue;
                if (ped.Handle == theirs || ped.Handle == talking) continue;
                if (ped.Position.DistanceTo(me.Position) > TagReach) continue;
                if (!ped.IsOnScreen) continue;

                try
                {
                    if (!Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, me.Handle, ped.Handle, 17)) continue;

                    var head = Function.Call<Vector3>(Hash.GET_PED_BONE_COORDS, ped.Handle, Skull, 0f, 0f, 0f);

                    Function.Call(Hash.SET_DRAW_ORIGIN, head.X, head.Y, head.Z + 0.38f, 0);

                    Function.Call(Hash.SET_TEXT_FONT, 4);
                    Function.Call(Hash.SET_TEXT_SCALE, 0.30f, 0.30f);
                    Function.Call(Hash.SET_TEXT_CENTRE, true);
                    Function.Call(Hash.SET_TEXT_COLOUR, 236, 236, 236, 205);
                    Function.Call(Hash.SET_TEXT_DROP_SHADOW);
                    Function.Call(Hash.SET_TEXT_OUTLINE);

                    // NPC MIND'S NAME FIRST. It is the name he will give you when you talk to
                    // him, and a tag that says anything else is a lie about who he is. Ours is
                    // only for an install without NPC Mind. See Core.Mind.
                    var called = Mind.NameOf(ped, l.Id) ?? l.Name;
                    if (string.IsNullOrEmpty(called)) { Function.Call(Hash.CLEAR_DRAW_ORIGIN); continue; }

                    Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
                    Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, called);
                    Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, 0f, 0f);

                    Function.Call(Hash.CLEAR_DRAW_ORIGIN);

                    drawn++;
                }
                catch
                {
                    try { Function.Call(Hash.CLEAR_DRAW_ORIGIN); } catch { }
                }
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

            if (!_quietKnown)
            {
                // SAID ONCE AT THE START, because "not about at this hour" with no hour on it
                // read as a bug the first time it was true. And on a fresh load the people are
                // built whatever the hour: see _quietArmed.
                _quietKnown = true;
                _quiet = false;
                _quietArmed = !quiet;

                Log.Info("Scenery: it is " + ClockHour() + ":00 on the game's clock; nobody is about from " +
                         (_cfg == null ? 3 : _cfg.SceneryQuietFrom) + ":00 to " + (_cfg == null ? 7 : _cfg.SceneryQuietTo) +
                         ":00" + (quiet ? ", which it is now -- but everybody is built on a fresh load, and goes the next time it strikes." : "."));
            }
            else if (!_quietArmed)
            {
                if (!quiet) _quietArmed = true;
            }
            else if (quiet != _quiet)
            {
                _quiet = quiet;

                Log.Info(quiet
                    ? "Scenery: the small hours -- everybody drifts off."
                    : "Scenery: the small hours are over -- everybody drifts back.");

                foreach (var l in _lives) Nudge(l, now);
            }

            // The shape-test budget is per beat, so thirty-four floor searches never land in one tick.
            _probes = 0;
            _looks = 0;

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

            if (now >= _notUpAt)
            {
                _notUpAt = now + NotUpEveryMs;
                NotUp(now);
            }
        }

        /// <summary>
        /// Who is still not up, and why -- once every few minutes, per scene, at a level the
        /// log actually keeps. A MAN WHO NEVER ARRIVES USED TO BE INVISIBLE: every reason he
        /// might be waiting was a retry with a Debug line or none, so the two Koreans behind
        /// the shop counter were missing for days and the log for it read the same as a night
        /// where they were there. See Life.Why.
        /// </summary>
        private void NotUp(int now)
        {
            var byScene = new Dictionary<string, List<string>>();

            foreach (var l in _lives)
            {
                if (l.State != Stage.Away || l.ForTheNight || l.Since == 0) continue;
                if (now - l.Since < NotUpAfterMs) continue;

                List<string> names;
                if (!byScene.TryGetValue(l.Scene.Name, out names)) byScene[l.Scene.Name] = names = new List<string>();

                names.Add(Say(l.Item) + " (" + (string.IsNullOrEmpty(l.Why) ? "waiting" : l.Why) + ")");
            }

            foreach (var pair in byScene)
            {
                Log.Info("Scenery: " + pair.Value.Count + " in \"" + pair.Key + "\" still not up after " +
                         (NotUpAfterMs / 60000) + " min -- " + string.Join("; ", pair.Value) + ".");
            }
        }

        private int _notUpAt;
        private const int NotUpEveryMs = 180000;
        private const int NotUpAfterMs = 120000;

        /// <summary>One look at one life. False when it is over and should be forgotten.</summary>
        private bool Pulse(Life l, int now, Vector3 here)
        {
            var scene = l.Scene;

            // The scene came down: its people went with it, and so does this.
            if (!scene.Built && !scene.Working) return false;

            if (l.State == Stage.Away)
            {
                // Caught by the hour while away on a whim: he stays away until it passes.
                if (_quiet && !l.Stays && !l.Camp) { l.ForTheNight = true; return true; }
                if (now < l.NextAt) return true;

                // THREE WAYS TO ARRIVE: a man in bed is put on his mark while nobody can see
                // it; a man on his own floor is stood up a few metres along it and walks; and
                // everybody else is stood up a way off on the navmesh and walks in.
                return l.Stays ? Appear(l, now, here)
                     : l.Straight ? ComeBackAlong(l, now, here)
                     : ComeBack(l, now, here);
            }

            var ped = l.Who;
            if (ped == null || !ped.Exists() || !ped.IsAlive) return false;

            // SOMEBODY ELSE HAS HIM: NPC Mind, or a run, or a fight. See Taken.
            if (Taken(l, ped, now)) return true;

            switch (l.State)
            {
                case Stage.Marked:
                    Chatter(l, ped, now, here);

                    if (_quiet && !l.Stays)
                    {
                        // A CAMP GOES TO BED rather than off. See TurnIn.
                        if (l.Camp)
                        {
                            if (now >= l.NextAt) TurnIn(l, now);
                            return true;
                        }

                        if (now >= l.NextAt) Leave(l, now, true);
                        return true;
                    }

                    if (now < l.NextAt) return true;

                    Choose(l, now);
                    return true;

                case Stage.Strolling:
                    if (Arrived(ped, l.Going) || now >= l.NextAt)
                    {
                        l.State = Stage.Loitering;

                        // A CAMP SEAT OR A CAMP BED: sat on or lain on, now he is at it. See Rest.
                        if (l.Seat != null || l.Bed != null)
                        {
                            Rest(l, ped, now);
                            return true;
                        }

                        if (l.Chore != null)
                        {
                            // A chore: turned to the thing, and at it for a good while.
                            try
                            {
                                var heading = Function.Call<float>(Hash.GET_HEADING_FROM_VECTOR_2D,
                                                                   l.Face.X - ped.Position.X, l.Face.Y - ped.Position.Y);
                                Function.Call(Hash.SET_ENTITY_HEADING, ped.Handle, heading);
                            }
                            catch { /* whichever way he is facing */ }

                            Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);
                            Scenario(ped, l.Chore);
                            l.Chore = null;
                            l.NextAt = now + Between(ChoreLeastMs, ChoreMostMs);
                        }
                        else
                        {
                            // THE SECOND TO ARRIVE TURNS THE TWO OF THEM TO TALK, when they went
                            // for the stroll together and the other is stood there waiting --
                            // and is still ours: not a man NPC Mind has, or a run or a fight
                            // (see Taken), because turning him to his mate would take him off
                            // it mid-sentence.
                            var pal = l.Pal;

                            if (pal != null && pal.Pal == l && pal.State == Stage.Loitering && pal.With == null &&
                                pal.TakenAt == 0 &&
                                pal.Who != null && pal.Who.Exists() &&
                                !Mind.Holding(pal.Who) && !Shaken(pal.Who) &&
                                pal.Who.Position.DistanceTo(ped.Position) <= PalReach)
                            {
                                Together(l, pal, now);
                            }
                            else
                            {
                                l.NextAt = now + (l.Roams ? Between(RoamLoiterLeastMs, RoamLoiterMostMs)
                                                          : Between(LoiterLeastMs, LoiterMostMs));
                                LoiterIdle(l);
                            }
                        }
                    }

                    return true;

                case Stage.Loitering:
                    // Stood talking to the one he strolled out with, or to nobody in particular.
                    // Not a word out of a man asleep.
                    if (l.With != null) Talk(l, now, here, ped);
                    else if (!l.Asleep) Chatter(l, ped, now, here);

                    // THE SMALL HOURS, at the stagger Nudge gave him rather than at once -- and
                    // a man on his own floor goes home first, because he leaves from his mark.
                    // Firing Leave every beat at a man who cannot leave was the old way.
                    if (_quiet && !l.Stays)
                    {
                        // A CAMP SLEEPS WHERE IT LIVES: on a bed already, he is asleep on it;
                        // otherwise, at the stagger Nudge gave him, he turns in. See TurnIn.
                        if (l.Camp)
                        {
                            if (!l.Asleep && l.Bed != null) { l.Asleep = true; return true; }
                            if (!l.Asleep && now >= l.NextAt) TurnIn(l, now);
                            return true;
                        }

                        if (now < l.NextAt) return true;
                        if (!l.Straight && Leave(l, now, true)) return true;

                        // Waiting for the way home to clear is a loiter a little longer.
                        GoHome(l, now);
                        return true;
                    }

                    if (now < l.NextAt) return true;

                    // A CAMP GETS UP AND DOES THE NEXT THING, or goes back to his spot by the
                    // stove for a while. See Camp.
                    if (l.Camp)
                    {
                        GetUp(l);
                        if (Dice.Next(100) < CampOnwardChance && Camp(l, now)) return true;
                        GoHome(l, now);
                        return true;
                    }

                    // A ROAMER DOES NOT REPORT BACK. He picks the next thing from where he is
                    // stood, which is what makes him read as somebody who lives here instead
                    // of somebody on a lead. His mark is only ever the centre of his radius.
                    if (l.Roams && !l.Stays)
                    {
                        Choose(l, now);
                        return true;
                    }

                    GoHome(l, now);
                    return true;

                case Stage.Outbound:
                    if (Arrived(ped, l.Going) || now >= l.NextAt)
                    {
                        // Got there after the hour turned: his leaving is inside the stagger,
                        // as Nudge would have set it had he been there.
                        l.State = Stage.Partying;
                        l.NextAt = now + (_quiet ? Dice.Next(StaggerMs) : Between(PartyLeastMs, PartyMostMs));
                        Revel(l);
                    }
                    return true;

                case Stage.Partying:
                    Chatter(l, ped, now, here);

                    // THE SMALL HOURS: at the stagger, and not asked again before Leave says
                    // (it sets NextAt when there is nowhere to walk off to). See Nudge.
                    if (_quiet)
                    {
                        if (now >= l.NextAt) Leave(l, now, true);
                        return true;
                    }

                    if (now < l.NextAt) return true;

                    // Home, the long way he came.
                    GoHome(l, now, OutingWalkMs);
                    return true;

                case Stage.Returning:
                case Stage.ComingBack:
                    if (l.Held)
                    {
                        if (Floored(ped.Position, ped) || now >= l.NextAt)
                        {
                            l.Held = false;
                            Loose(l);
                            Walk(l, l.Item.At, l.Item.Yaw, ComeInMs);
                            l.NextAt = now + ComeInMs;
                            l.Was = 0f;
                            l.Moved = now;
                        }

                        return true;
                    }

                    // OFF HIS FLOOR, with no way back up it: he waits where he is until both
                    // he and his mark are out of sight, and is put back then.
                    if (l.Straight && Math.Abs(ped.Position.Z - l.Item.At.Z) > OffFloor)
                    {
                        if (Unseen(scene, ped) && OutOfSight(scene, l.Item.At))
                        {
                            Home(l);
                            l.State = Stage.Marked;
                            l.NextAt = now + (_quiet && !l.Stays ? Dice.Next(StaggerMs) : Beat(l));
                        }

                        return true;
                    }

                    if (Arrived(ped, l.Item.At))
                    {
                        // THE LAST STEP IS WALKED. The nav task stops within There of the
                        // mark; the rest is a go-straight with a short slide, so that Home has
                        // Snug at most to put right rather than a metre and a half in view.
                        var off = Flat(ped.Position - l.Item.At);

                        if (off > Snug)
                        {
                            if (!l.Closing)
                            {
                                l.Closing = true;

                                // A man on a straight walk is already on it, aimed at the mark
                                // with the slide; asking again would only make him stutter.
                                if (!l.Straight) GoStraight(ped, l.Item.At, l.Item.Yaw, CloseMs);

                                l.NextAt = now + CloseMs;
                                return true;
                            }

                            if (now < l.NextAt) return true;

                            // THE LAST STEP DID NOT GET THERE -- somebody stood on the line,
                            // mostly, because a go-straight goes round nobody. Snapping him the
                            // rest of the way is the very flick this exists to avoid, so that
                            // only happens once neither he nor the mark can be seen; watched,
                            // he is asked for the step again, every CloseMs, for as long as it
                            // takes. Stood in the open beside his mark beats flicking onto it.
                            if (!(Unseen(scene, ped) && OutOfSight(scene, l.Item.At)))
                            {
                                l.Tries++;
                                GoStraight(ped, l.Item.At, l.Item.Yaw, CloseMs);
                                l.NextAt = now + CloseMs;
                                return true;
                            }
                        }

                        // On the hour's stagger rather than his beat when the small hours
                        // caught him out on a stroll, or everybody who was out walks home and
                        // turns straight round again together.
                        l.Closing = false;
                        Home(l);
                        l.State = Stage.Marked;
                        l.NextAt = now + (_quiet && !l.Stays ? Dice.Next(StaggerMs) : Beat(l));
                        return true;
                    }

                    // IS HE ACTUALLY WALKING? The deadline alone is not enough: a nav task
                    // that gave up leaves him stood still, and he does nothing else while he
                    // is on his way, so he reads as a dead ped for two and a half minutes.
                    // Ground made resets the watch; no ground made in StallMs and he is asked
                    // again now rather than at the deadline.
                    var gap = ped.Position.DistanceTo(l.Item.At);
                    var stalled = false;

                    if (l.Was <= 0f || gap < l.Was - 0.5f)
                    {
                        l.Was = gap;
                        l.Moved = now;
                    }
                    else if (now - l.Moved >= StallMs)
                    {
                        stalled = true;
                    }

                    if (now < l.NextAt && !stalled) return true;

                    // OUT OF TIME, AND THIS IS WHERE THE TELEPORT WAS. Home puts him on the
                    // mark outright, and the test used to be "off screen OR two goes in" --
                    // so being off screen was enough on its own, first time, every time. A man
                    // stood up off screen sixty metres out and walked in is off screen for the
                    // whole walk by definition, and the player is 220 m away when the scene is
                    // built, so the deadline put every one of them on his mark and the walk
                    // never happened. Michael saw them arrive by teleport twice over.
                    //
                    // Now he gets ComeInTries goes at it, and is only put there once they are
                    // all gone AND nobody can see it -- neither him nor the MARK, because a
                    // man appearing on a spot you are looking at is the same cut from the
                    // other side. Watched and out of goes, he keeps walking -- a man who
                    // cannot reach his mark is better stood in the street than flicking onto
                    // it in front of you.
                    if (l.Tries >= ComeInTries)
                    {
                        if (Unseen(scene, ped) && OutOfSight(scene, l.Item.At))
                        {
                            Home(l);
                            l.State = Stage.Marked;
                            l.NextAt = now + (_quiet && !l.Stays ? Dice.Next(StaggerMs) : Beat(l));
                            return true;
                        }

                        Walk(l, l.Item.At, l.Item.Yaw, ComeInMs);
                        l.NextAt = now + ComeInMs;
                        l.Was = 0f;
                        l.Moved = now;
                        return true;
                    }

                    l.Tries++;
                    Walk(l, l.Item.At, l.Item.Yaw, ComeInMs);
                    l.NextAt = now + ComeInMs;
                    l.Was = 0f;
                    l.Moved = now;
                    return true;

                case Stage.Chatting:
                case Stage.Signing:
                case Stage.Turning:
                    if (l.State == Stage.Chatting) Talk(l, now, here, ped);

                    if (now < l.NextAt) return true;

                    // Back to the mark and the idle. Home rather than Doing, because a chat
                    // turns him and unfreezes him, and the mark is where he belongs.
                    //
                    // EXCEPT A ROAMER, who has no business being snapped anywhere: he stands
                    // where the conversation left him and picks the next thing from there.
                    // Home is a few metres for a man on his mark and it reads as a flick.
                    l.With = null;

                    if (l.Roams && !l.Stays && !_quiet)
                    {
                        l.State = Stage.Marked;
                        Choose(l, now);
                        return true;
                    }

                    Home(l);
                    l.State = Stage.Marked;
                    l.NextAt = now + (_quiet && !l.Stays ? Dice.Next(StaggerMs) : Beat(l));
                    return true;

                case Stage.Shifting:
                    {
                        // A corner at a time: on to the next when he reaches this one, or when
                        // the leg has had its time -- and at his job when there are none left.
                        var off = ped.Position - l.Going;
                        off.Z = 0f;

                        if (off.Length() > LegThere && now < l.NextAt) return true;

                        if (l.Legs != null && l.Legs.Count > 0)
                        {
                            NextLeg(l, now);
                            return true;
                        }

                        AtJob(l, l.JobTo, now);
                        return true;
                    }

                case Stage.Leaving:
                    {
                        var far = ped.Position.DistanceTo(here);
                        var there = Arrived(ped, l.Going) || now >= l.NextAt;

                        // Gone only once nobody can see him: off screen, or behind a wall or a
                        // building. The one rule for the ground and for the floors.
                        if ((there || far > GoneAt) && Unseen(scene, ped))
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

        /// <summary>
        /// The beat: a word with somebody, a sign, a change of what he is doing, a stretch of
        /// the legs, or a walk off for a bit.
        /// </summary>
        private void Choose(Life l, int now)
        {
            // NOT WHILE HE IS STILL COLD. The floor under him is not confirmed yet (see
            // Thawing), and a beat that looses him for a chat or a walk would drop him.
            if (StillCold(l))
            {
                l.NextAt = now + 5000;
                return;
            }

            // A GUARD STAYS ON HIS DOOR. No word with anybody, no signs, no stroll: the next of
            // his guard clips, and back to watching the room.
            if (l.Item.Guard)
            {
                l.Turn++;
                Doing(l.Scene, l.Who, l.Item, l.Turn);
                l.NextAt = now + Beat(l);
                return;
            }

            // SAT, SHE STAYS SAT. Sat down again only if something has had her up: sitting her
            // down every beat would be a woman up and down off the couch every few seconds.
            if (l.Scene.Sits.ContainsKey(l.Item.Handle))
            {
                if (!Function.Call<bool>(Hash.IS_PED_USING_ANY_SCENARIO, l.Who.Handle)) Doing(l.Scene, l.Who, l.Item, l.Turn);
                l.NextAt = now + Beat(l);
                return;
            }

            var roll = Dice.Next(100);

            // A CAMP IS HOME. Every beat is something in it: a word with the other one, the
            // stove, a seat, a bed, the bins, or a smoke or a drink where he stands. Never a walk
            // off and never up the road. Michael asked for two living at the stove on the lot on
            // 2026-09-26 -- "do hobo things around that small area". See Camp.
            if (l.Camp)
            {
                if (roll < 22 && Chat(l, now)) return;
                if (roll < 85 && Camp(l, now)) return;

                l.Turn++;
                Doing(l.Scene, l.Who, l.Item, l.Turn);
                l.NextAt = now + Beat(l);
                return;
            }

            // A MAN WITH TWO JOBS works them: the one he is at, and now and then the walk to the
            // other -- the young one in the corner shop, between the fruit and the shelves. See Work.
            if (l.Scene.Jobs.ContainsKey(l.Item.Handle))
            {
                Work(l, now, roll);
                return;
            }

            // A ROAMER IS ON HIS FEET. He lives here rather than stands in it: he walks, he
            // stops for a word or a smoke or a turn with the bins, and he walks again --
            // better than half of his beats are a walk, and a beat is seconds long. Now and
            // then he goes up the road to the party and comes home. Standing on the mark is
            // the thing he does least, which is the whole point of him.
            //
            // Michael asked for this on 2026-09-22, for the fifty-one who came down off the
            // sky: "give them a life".
            if (l.Roams && !l.Stays)
            {
                if (l.Drunk && !l.Drunken) Souse(l);

                if (roll < OutingChance && Outing(l, now)) return;
                if (roll < 16 && Chat(l, now)) return;

                // GANG SIGNS. The roamer branch went in without these and the generic one
                // below has always had them, so the fifty-one were the only people on the
                // block not throwing any. Michael asked where they went.
                if (roll < 26 && !l.Item.Armed)
                {
                    Sign(l, now);
                    return;
                }

                if (roll < 40 && Forage(l, now)) return;

                if (roll < 50)
                {
                    l.Turn++;
                    Doing(l.Scene, l.Who, l.Item, l.Turn);
                    l.NextAt = now + Beat(l);
                    return;
                }

                Stroll(l, now);
                return;
            }

            // A hobo has things to do with what is lying about. See Forage.
            if (IsHobo(l.Item) && !l.Stays && roll < 45 && Forage(l, now)) return;

            // A man with a list of his own works through it: a word with whoever is near,
            // the next thing on the list, and no signs or chores -- the shopkeeper is not
            // throwing up the set with a customer in.
            if (l.Scene.Idles.ContainsKey(l.Item.Handle))
            {
                // TALKING, FIRST AND OFTEN. A routine of their own is almost always a pair or a
                // three stood facing each other -- Michael put seven pairs down on 2026-09-24
                // and every request said "talking" -- and at a quarter of the beats they spent
                // more of the evening drinking at each other than saying anything.
                if (roll < 40 && Chat(l, now)) return;

                // EXCEPT A GANG HANGOUT, which throws the set. The no-signs rule above was
                // written for the shopkeeper, and it silenced every group given a routine
                // of its own -- three Families stood drinking on a corner are exactly the
                // people who throw it up. Only Families: the Koreans still do not.
                if (roll < 55 && Fam(l.Item) && !l.Item.Armed)
                {
                    Sign(l, now);
                    return;
                }

                // THE ROUTINE STAYS MOSTLY TALKING: a stroll on one beat in nine, a walk-off
                // on one in twenty-five. And NO WAY OUT MEANS A STROLL, never a vanish
                // where he stands -- see Leave.
                if (roll < 85 || l.Stays)
                {
                    l.Turn++;
                    Doing(l.Scene, l.Who, l.Item, l.Turn);
                    l.NextAt = now + Beat(l);
                    return;
                }

                if (roll < 96) Stroll(l, now);
                else if (!Leave(l, now, false)) Stroll(l, now);

                return;
            }

            if (l.Scripted)
            {
                // A man the spooner gave something to do keeps doing it, and walks now and then.
                if (l.Stays) { l.NextAt = now + Beat(l); return; }

                if (roll < 60) Stroll(l, now);
                else if (!Leave(l, now, false)) Stroll(l, now);

                return;
            }

            if (roll < 20 && Chat(l, now)) return;

            if (roll < 35 && !l.Item.Armed)
            {
                Sign(l, now);
                return;
            }

            if (roll < 60 || l.Stays)
            {
                l.Turn++;
                Doing(l.Scene, l.Who, l.Item, l.Turn);
                l.NextAt = now + Beat(l);
                return;
            }

            // A man on his own floor walks about more often than he goes: the gallery is
            // fourteen metres long and a walk-off needs a hidden end of it. And no way out
            // means a stroll, never a vanish -- see Leave.
            var leaveFrom = l.Straight ? 92 : 80;

            if (roll < leaveFrom)
            {
                Stroll(l, now);
                return;
            }

            if (!Leave(l, now, false)) Stroll(l, now);
        }

        /// <summary>
        /// A word with whoever is stood nearest: the two of them turned to each other and
        /// talking with their hands for a while, then back to what they were doing. The
        /// game's own chat task, which is what two peds on a corner in the base game are
        /// running. Nobody the spooner gave a job to is interrupted for it.
        /// </summary>
        private bool Chat(Life l, int now)
        {
            Life other = null;
            var best = ChatReach;

            foreach (var o in _lives)
            {
                if (o == l || o.Scene != l.Scene || o.State != Stage.Marked || o.Scripted) continue;

                // Not a man NPC Mind has: turning him to somebody else would take him off it.
                if (o.TakenAt != 0) continue;

                // Nor one sat down or lying in his camp, or on his way to, or at work: a chat
                // stands him up, and puts a man at his other job back on his mark after it.
                if (o.Asleep || o.Seat != null || o.Bed != null || o.Scene.Jobs.ContainsKey(o.Item.Handle) || o.Item.Guard) continue;
                if (o.Scene.Sits.ContainsKey(o.Item.Handle)) continue;
                if (o.Who == null || !o.Who.Exists() || !o.Who.IsAlive) continue;

                // ON THE SAME FLOOR. Six metres reaches from a balcony to the ground under it,
                // and two men a storey apart turned to each other is not a conversation. And
                // not a man whose floor is not confirmed under him yet: loosing him for a
                // word would drop him. See StillCold.
                if (Math.Abs(o.Who.Position.Z - l.Who.Position.Z) > OffFloor) continue;
                if (StillCold(o)) continue;

                var d = o.Who.Position.DistanceTo(l.Who.Position);
                if (d < best) { best = d; other = o; }
            }

            if (other == null) return false;

            Loose(l);
            Loose(other);
            Face(l, other, now + Between(ChatLeastMs, ChatMostMs), now);
            l.State = other.State = Stage.Chatting;

            return true;
        }

        /// <summary>
        /// The two of them turned to each other and talking with their hands until a given
        /// time: the game's own chat task on both, and who speaks first. Shared by a chat
        /// from the mark and by two who strolled out together (see Together); the state is
        /// the caller's to set.
        /// </summary>
        private static void Face(Life a, Life b, int until, int now)
        {
            foreach (var pair in new[] { new[] { a, b }, new[] { b, a } })
            {
                var me = pair[0];
                var you = pair[1];

                Function.Call(Hash.CLEAR_PED_TASKS, me.Who.Handle);
                Function.Call(Hash.TASK_CHAT_TO_PED, me.Who.Handle, you.Who.Handle, 16, 0f, 0f, 0f, 0f, 0f);
                Function.Call(Hash.SET_PED_KEEP_TASK, me.Who.Handle, true);

                me.With = you;
                me.NextAt = until;

                // The one who walked over speaks first; the other answers a beat later.
                me.Speaks = ReferenceEquals(me, a);
                me.NextLine = now + (me.Speaks ? 500 : 2500);
            }
        }

        /// <summary>Two who strolled out together, stood talking at the far spot for as long as a loiter. Both stay Loitering.</summary>
        private static void Together(Life a, Life b, int now) => Face(a, b, now + Between(LoiterLeastMs, LoiterMostMs), now);

        /// <summary>
        /// The lines of a chat: a statement and a response, taking turns every few seconds
        /// within earshot. For two on their marks (Chatting) and for two stood at the far end
        /// of a stroll together (Loitering, With set).
        /// </summary>
        private void Talk(Life l, int now, Vector3 here, Ped ped)
        {
            if (now < l.NextLine || ped.Position.DistanceTo(here) > Earshot) return;

            Say(l, l.Speaks ? Voices.Kind.State : Voices.Kind.Reply,
                Fam(l.Item) ? (l.Speaks ? FamChat : FamReply) : (l.Speaks ? AnyChat : AnyReply), now);
            l.NextLine = now + Between(ChatTurnLeastMs, ChatTurnMostMs);
        }

        /// <summary>
        /// A chore among the props round a hobo's mark: he walks to one within ForageReach,
        /// stands a step off it facing it, and does what the thing suggests -- goes through a
        /// bin, pushes a trolley, warms his hands at a stove, picks through a wreck or a pile,
        /// slumps against a mattress or a couch, leans on a light or a wall -- for a minute or
        /// so, then walks back. Michael asked on 2026-09-21 for hobos that interact with the
        /// props round them; this is the interacting. Decals, weeds, plants and the ground
        /// pieces are not things, and are skipped.
        /// </summary>
        private bool Forage(Life l, int now)
        {
            // A chore is a navmesh walk, and a man on his own floor has none. See "Along his own floor".
            if (l.Straight) return false;

            var near = new List<Spooner.Placed>();

            foreach (var other in l.Scene.Items)
            {
                if (other == l.Item || other.What != Spooner.Kind.Prop || other.Attached) continue;
                if (l.Scene.Floors.ContainsKey(other.Handle)) continue;
                if (!IsAThing(other)) continue;

                var d = other.At - l.Item.At;
                d.Z = 0f;
                if (d.Length() > ForageReach || d.Length() < 0.5f) continue;

                near.Add(other);
            }

            if (near.Count == 0) return false;

            var thing = near[Dice.Next(near.Count)];

            // A step off it, on the side his mark is on.
            var away = l.Item.At - thing.At;
            away.Z = 0f;
            if (away.Length() < 0.1f) away = new Vector3(1f, 0f, 0f);
            away.Normalize();

            var spot = thing.At + away * ChoreStandOff;
            spot.Z = l.Item.At.Z;

            Loose(l);
            WalkTo(l.Who, spot, 0f);

            l.Going = spot;
            l.Face = thing.At;
            l.Chore = ChoreFor(thing);
            l.State = Stage.Strolling;
            l.NextAt = now + WalkMs;

            return true;
        }

        /// <summary>Whether a placement is something a man can do anything with.</summary>
        private static bool IsAThing(Spooner.Placed item)
        {
            var n = (item.ModelName ?? "").ToLowerInvariant();
            if (n.Length == 0) return true;

            foreach (var no in new[] { "decal", "weed", "poster", "graf", "mural", "des_", "plant", "tree", "sign_", "_sign", "gravestone" })
            {
                if (n.Contains(no)) return false;
            }

            return true;
        }

        /// <summary>What a hobo does at a thing, from what the thing is called.</summary>
        private static string ChoreFor(Spooner.Placed thing)
        {
            var n = (thing.ModelName ?? "").ToLowerInvariant();

            if (n.Contains("bin") && !n.Contains("binbag")) return "PROP_HUMAN_BUM_BIN";
            if (n.Contains("trolley") || n.Contains("cart")) return "PROP_HUMAN_BUM_SHOPPING_CART";
            if (n.Contains("stove") || n.Contains("fire") || n.Contains("barrel")) return "WORLD_HUMAN_STAND_FIRE";
            if (n.Contains("matress") || n.Contains("mattress") || n.Contains("couch") || n.Contains("bed") ||
                n.Contains("tent") || n.Contains("shelter") || n.Contains("chair")) return "WORLD_HUMAN_BUM_SLUMPED";
            if (n.Contains("streetlight") || n.Contains("wall") || n.Contains("pillar") || n.Contains("billboard") || n.Contains("fnc")) return "WORLD_HUMAN_LEANING";
            if (n.Contains("wreck") || n.Contains("carpart") || n.Contains("pile") || n.Contains("cont") || n.Contains("flotsam") ||
                n.Contains("litter") || n.Contains("binbag") || n.Contains("rub_") || n.Contains("crate") || n.Contains("box")) return "WORLD_HUMAN_GARDENER_PLANT";

            return "WORLD_HUMAN_BUM_STANDING";
        }

        // ==================================================================
        // Two jobs
        // ==================================================================

        /// <summary>One place a man works: where, which way he faces, the corners of the walk in from his mark, and what he does there.</summary>
        private sealed class Job
        {
            public Vector3 At;
            public float Heading;
            public readonly List<Vector3> Via = new List<Vector3>();
            public string[] Idles = new string[0];
        }

        /// <summary>
        /// A second place of work for a man -- in the Note, "jobs: 610923 = x, y, z, heading via
        /// x, y = idle, idle; ..." -- where it is, which way he faces there, the corners of the
        /// walk to it from his mark (none, or any number, each "via x, y"), and what he does
        /// there: scenarios or dict/clip pairs, the same as idles:. His mark is his first job,
        /// with his idles: list. See Work.
        /// </summary>
        private static Dictionary<int, List<Job>> Jobs(string path)
        {
            var jobs = new Dictionary<int, List<Job>>();

            foreach (var line in Clauses(path))
            {
                if (!line.StartsWith("jobs:", StringComparison.OrdinalIgnoreCase)) continue;

                foreach (var one in line.Substring(5).Split(';'))
                {
                    var parts = one.Split('=');
                    if (parts.Length != 3) continue;

                    int handle;
                    if (!int.TryParse(parts[0].Trim(), out handle)) continue;

                    var legs = parts[1].Split(new[] { " via " }, StringSplitOptions.RemoveEmptyEntries);
                    var place = Numbers(legs[0]);
                    if (place.Count < 4) continue;

                    var job = new Job { At = new Vector3(place[0], place[1], place[2]), Heading = place[3] };

                    for (var i = 1; i < legs.Length; i++)
                    {
                        var corner = Numbers(legs[i]);
                        if (corner.Count >= 2) job.Via.Add(new Vector3(corner[0], corner[1], job.At.Z));
                    }

                    var idles = new List<string>();

                    foreach (var raw in parts[2].Split(','))
                    {
                        var entry = raw.Trim();
                        if (entry.Length > 0) idles.Add(entry);
                    }

                    if (idles.Count == 0) continue;
                    job.Idles = idles.ToArray();

                    List<Job> list;
                    if (!jobs.TryGetValue(handle, out list)) jobs[handle] = list = new List<Job>();
                    list.Add(job);
                }
            }

            return jobs;
        }

        private static List<float> Numbers(string text)
        {
            var list = new List<float>();

            foreach (var raw in text.Split(','))
            {
                float f;
                if (float.TryParse(raw.Trim(), System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out f)) list.Add(f);
            }

            return list;
        }

        /// <summary>His jobs, his mark first with his own idles, worked out once.</summary>
        private List<Job> JobsOf(Life l)
        {
            if (l.JobList != null) return l.JobList;

            string[] own;
            l.Scene.Idles.TryGetValue(l.Item.Handle, out own);

            var list = new List<Job> { new Job { At = l.Item.At, Heading = l.Item.Yaw, Idles = own ?? new string[0] } };

            List<Job> more;
            if (l.Scene.Jobs.TryGetValue(l.Item.Handle, out more)) list.AddRange(more);

            l.JobList = list;
            return list;
        }

        /// <summary>
        /// A beat for a man with two jobs: now and then the walk to the other, otherwise the next
        /// thing at this one. Michael put the young one in the corner shop on 2026-09-26 bent over
        /// the fruit at one end and stocking the shelves at the other, and asked for him to "walk
        /// between the two jobs". Never a chat: see Chat.
        /// </summary>
        private void Work(Life l, int now, int roll)
        {
            var jobs = JobsOf(l);

            if (jobs.Count > 1 && roll < WorkMoveChance)
            {
                var next = (l.JobAt + 1 + Dice.Next(jobs.Count - 1)) % jobs.Count;
                Shift(l, jobs, next, now);
                return;
            }

            AtJob(l, l.JobAt, now);
        }

        /// <summary>
        /// Off to another job, in straight lines -- the shop floor is props, and a prop floor has
        /// no navmesh -- out of where he is by its own corners backwards, and in to the next by
        /// its corners forwards. See the Shifting stage.
        /// </summary>
        private void Shift(Life l, List<Job> jobs, int next, int now)
        {
            var legs = new List<Vector3>();

            var from = jobs[l.JobAt];
            for (var i = from.Via.Count - 1; i >= 0; i--) legs.Add(from.Via[i]);

            var to = jobs[next];
            legs.AddRange(to.Via);
            legs.Add(to.At);

            Loose(l);
            l.Legs = new Queue<Vector3>(legs);
            l.JobTo = next;
            l.State = Stage.Shifting;

            NextLeg(l, now);
        }

        private void NextLeg(Life l, int now)
        {
            var at = l.Legs.Dequeue();
            var ped = l.Who;

            var heading = l.Legs.Count == 0
                ? JobsOf(l)[l.JobTo].Heading
                : Function.Call<float>(Hash.GET_HEADING_FROM_VECTOR_2D, at.X - ped.Position.X, at.Y - ped.Position.Y);

            GoStraight(ped, at, heading, LegMs);

            l.Going = at;
            l.NextAt = now + LegMs;
        }

        /// <summary>At a job: facing its way, doing the next thing on its list. His mark is his idles:, the same as anybody.</summary>
        private void AtJob(Life l, int index, int now)
        {
            var ped = l.Who;
            var jobs = JobsOf(l);

            if (index < 0 || index >= jobs.Count) index = 0;

            var job = jobs[index];

            l.JobAt = index;
            l.JobTo = -1;
            l.Legs = null;
            l.State = Stage.Marked;
            l.Turn++;

            try
            {
                if (index == 0 || job.Idles.Length == 0)
                {
                    Doing(l.Scene, ped, l.Item, l.Turn);
                }
                else
                {
                    Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, ped.Handle);

                    var entry = job.Idles[(Steady(job.At) + l.Turn) % job.Idles.Length];
                    var slash = entry.IndexOf('/');

                    if (slash > 0) Give(ped, entry.Substring(0, slash), entry.Substring(slash + 1));
                    else Scenario(ped, entry);
                }

                Function.Call(Hash.SET_ENTITY_HEADING, ped.Handle, job.Heading);
            }
            catch (Exception ex)
            {
                Log.Debug("Scenery: a job would not start: " + ex.Message);
            }

            l.NextAt = now + Between(JobLeastMs, JobMostMs);
        }

        /// <summary>How often a beat is the walk to the other job, how long a stint at one lasts, and the walk's tolerances.</summary>
        private const int WorkMoveChance = 45;
        private const int JobLeastMs = 25000;
        private const int JobMostMs = 70000;
        private const float LegThere = 0.45f;
        private const int LegMs = 9000;
        private const float JobStay = 2f;

        // ==================================================================
        // Glass
        // ==================================================================

        /// <summary>
        /// A pane of glass made solid -- "glass:" in the Note.
        ///
        /// A PANE IS A PICTURE OF GLASS. The corner shop's windows at Parkview are
        /// v_24_wdr_mesh_windows, window meshes out of an interior, and a mesh made to sit in a
        /// wall the interior already has carries no collision of its own: the log says so for
        /// every one of them, and Michael walked through them on 2026-09-26. So each gets the
        /// floors' blocks stood on end -- the narrowest Bikers building block, turned upright,
        /// as many as its width needs, centred on the pane and turned with it, invisible,
        /// frozen and solid. Centred by the game's own offsets rather than by arithmetic on a
        /// rotation order, because a box stood on end can go either way up. A block per pane,
        /// so the gap the door stands in stays open.
        /// </summary>
        private static Verdict Glaze(Scene scene, Entity pane, Spooner.Placed item)
        {
            Tile best = null;
            var blo = Vector3.Zero;
            var bhi = Vector3.Zero;

            foreach (var name in TileNames)
            {
                var model = new Model(name);
                if (!model.IsValid) continue;
                if (!Models.Ready(model)) return Verdict.NotYet;

                Vector3 lo, hi;
                if (!Dimensions(model, out lo, out hi)) continue;

                var t = new Tile { Model = model, W = hi.X - lo.X, L = hi.Y - lo.Y, Top = hi.Z };
                if (best != null && t.W * t.L >= best.W * best.L) continue;

                best = t;
                blo = lo;
                bhi = hi;
            }

            if (best == null)
            {
                Log.Info("Scenery: no wall in " + Say(item) + " in " + scene.Name + " -- none of the block models are on this install.");
                return Verdict.No;
            }

            Vector3 min, max;

            try
            {
                var pl = new OutputArgument();
                var ph = new OutputArgument();
                Function.Call(Hash.GET_MODEL_DIMENSIONS, pane.Model.Hash, pl, ph);
                min = pl.GetResult<Vector3>();
                max = ph.GetResult<Vector3>();
            }
            catch
            {
                return Verdict.No;
            }

            var alongX = max.X - min.X >= max.Y - min.Y;
            var run = alongX ? max.X - min.X : max.Y - min.Y;

            if (run < 0.3f)
            {
                Log.Info("Scenery: no wall in " + Say(item) + " in " + scene.Name + " -- it is " + run.ToString("0.00") + " m across.");
                return Verdict.No;
            }

            var along = alongX ? pane.RightVector : pane.ForwardVector;
            along.Z = 0f;
            along.Normalize();

            var centre = pane.GetOffsetPosition(new Vector3((min.X + max.X) * 0.5f, (min.Y + max.Y) * 0.5f, (min.Z + max.Z) * 0.5f));
            var middle = new Vector3((blo.X + bhi.X) * 0.5f, (blo.Y + bhi.Y) * 0.5f, (blo.Z + bhi.Z) * 0.5f);
            var yaw = alongX ? item.Yaw : item.Yaw + 90f;
            var many = Math.Max(1, (int)Math.Ceiling((run - 0.2f) / best.W));
            var step = run / many;
            var made = 0;

            for (var i = 0; i < many; i++)
            {
                var at = centre + along * ((i - (many - 1) * 0.5f) * step);

                try
                {
                    var block = World.CreateProp(best.Model, at, false, false);
                    if (block == null || !block.Exists()) continue;

                    Function.Call(Hash.SET_ENTITY_ROTATION, block.Handle, 90f, 0f, yaw, 2, true);

                    // Its middle onto the pane's middle, whichever way up the box went.
                    block.PositionNoOffset = block.Position + (at - block.GetOffsetPosition(middle));

                    Function.Call(Hash.SET_ENTITY_COLLISION, block.Handle, true, true);
                    Function.Call(Hash.FREEZE_ENTITY_POSITION, block.Handle, true);
                    Function.Call(Hash.SET_ENTITY_DYNAMIC, block.Handle, false);
                    Function.Call(Hash.SET_ENTITY_VISIBLE, block.Handle, false, false);
                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, block.Handle, true, true);

                    scene.Up.Add(block);
                    scene.Solid.Add(block.Handle);
                    _tiles.Add(block.Handle);
                    made++;

                    if (!_glazeSaid)
                    {
                        _glazeSaid = true;
                        var length = block.ForwardVector;

                        Log.Info("Scenery: glass is walled with " + Names.Say(best.Model.Hash) + ", " + best.W.ToString("0.0") +
                                 " m wide and " + (bhi.Z - blo.Z).ToString("0.00") + " m thick, stood on end" +
                                 (Math.Abs(length.Z) < 0.9f ? " -- and it is NOT upright (its length points " + length + ")." : "."));
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug("A glass wall would not stand: " + ex.Message);
                }
            }

            Log.Info("Scenery: " + Say(item) + " in " + scene.Name + " is solid now -- " + made + " block(s) across its " +
                     run.ToString("0.0") + " m.");

            return made > 0 ? Verdict.Up : Verdict.No;
        }

        private static bool _glazeSaid;

        // ==================================================================
        // A camp
        // ==================================================================

        /// <summary>What a thing at a camp is for, from what it is called. See Camp.</summary>
        private enum CampUse
        {
            None,
            Fire,
            Seat,
            Bed,
            Bin,
            Pile
        }

        private static CampUse UseOf(Spooner.Placed thing)
        {
            var n = (thing.ModelName ?? "").ToLowerInvariant();

            if (n.Contains("stove") || n.Contains("fire") || n.Contains("barrel")) return CampUse.Fire;

            if (n.Contains("couch") || n.Contains("sofa") || n.Contains("seat") || n.Contains("chair") ||
                n.Contains("stool") || n.Contains("bench")) return CampUse.Seat;

            if (n.Contains("sleepbag") || n.Contains("matress") || n.Contains("mattress") || n.Contains("shelter") ||
                n.Contains("tent") || n.Contains("bed")) return CampUse.Bed;

            if ((n.Contains("bin") && !n.Contains("binbag")) || n.Contains("trolley") || n.Contains("cart")) return CampUse.Bin;

            if (n.Contains("binbag") || n.Contains("pile") || n.Contains("litter") || n.Contains("scrap")) return CampUse.Pile;

            return CampUse.None;
        }

        /// <summary>How much of a camp's day each kind of thing gets. A seat and the stove, most of it.</summary>
        private static int WeightOf(CampUse use)
        {
            switch (use)
            {
                case CampUse.Fire: return 26;
                case CampUse.Seat: return 30;
                case CampUse.Bed: return 16;
                case CampUse.Bin: return 10;
                case CampUse.Pile: return 18;
                default: return 0;
            }
        }

        /// <summary>
        /// Something to do in the camp. The things round his mark are sorted by what they are
        /// for -- the stove, a seat, a bed, the bins, a pile of rubbish -- one kind is picked by
        /// how much of a camp's day it gets, and one thing of that kind: he warms his hands at
        /// the stove, sits on a seat or the couch facing the fire, lies down on a bed or at the
        /// mouth of a shelter, goes through a bin, picks through a pile. The kind first, or the
        /// twenty bits of litter round a camp outvote its one stove. A seat or a bed somebody
        /// else is on, or walking to, is not his. False when there is nothing at all.
        /// </summary>
        private bool Camp(Life l, int now)
        {
            var ped = l.Who;
            if (ped == null || !ped.Exists()) return false;

            var kinds = new Dictionary<CampUse, List<Spooner.Placed>>();

            foreach (var one in l.Scene.Items)
            {
                var use = CampThing(l, one);
                if (use == CampUse.None) continue;

                List<Spooner.Placed> list;
                if (!kinds.TryGetValue(use, out list)) kinds[use] = list = new List<Spooner.Placed>();
                list.Add(one);
            }

            if (kinds.Count == 0) return false;

            var total = 0;
            foreach (var pair in kinds) total += WeightOf(pair.Key);

            var roll = Dice.Next(total);
            var pick = CampUse.None;

            foreach (var pair in kinds)
            {
                roll -= WeightOf(pair.Key);
                if (roll >= 0) continue;

                pick = pair.Key;
                break;
            }

            if (pick == CampUse.None) return false;

            var pool = kinds[pick];
            GoTo(l, pool[Dice.Next(pool.Count)], pick, now);
            return true;
        }

        /// <summary>
        /// What a placement is to a man at this camp, or None: a prop of the scene, on the
        /// ground, within reach of his mark, and -- a seat or a bed -- nobody else's just now.
        /// </summary>
        private CampUse CampThing(Life l, Spooner.Placed one)
        {
            if (one == l.Item || one.What != Spooner.Kind.Prop || one.Attached) return CampUse.None;
            if (l.Scene.Floors.ContainsKey(one.Handle)) return CampUse.None;

            var use = UseOf(one);
            if (use == CampUse.None) return use;

            var d = one.At - l.Item.At;
            d.Z = 0f;
            if (d.Length() > CampReach) return CampUse.None;

            // On the ground, not up a wall: the trainers slung over the wire are not a chore.
            if (one.At.Z > l.Item.At.Z + CampAbove) return CampUse.None;

            if ((use == CampUse.Seat || use == CampUse.Bed) && SpokenFor(l, one)) return CampUse.None;

            return use;
        }

        /// <summary>Whether somebody else is on that seat or bed, or on his way to it.</summary>
        private bool SpokenFor(Life l, Spooner.Placed thing)
        {
            foreach (var o in _lives)
            {
                if (ReferenceEquals(o, l)) continue;
                if (ReferenceEquals(o.Seat, thing) || ReferenceEquals(o.Bed, thing)) return true;
            }

            return false;
        }

        /// <summary>Off to a thing at the camp, walked to where he does it. See Rest for a seat and a bed.</summary>
        private void GoTo(Life l, Spooner.Placed thing, CampUse use, int now)
        {
            var ped = l.Who;
            var mark = l.Item.At;

            // From the side he comes at it, turned a little either way, so the two of them at
            // the stove do not stand in the same boots.
            var away = ped.Position - thing.At;
            away.Z = 0f;
            if (away.Length() < 0.1f) away = new Vector3(1f, 0f, 0f);
            away.Normalize();
            away = Turned(away, (float)(Dice.NextDouble() * 70.0 - 35.0));

            Vector3 spot;

            if (use == CampUse.Seat)
            {
                Vector3 cushion;
                float facing;
                CampSeat(l.Scene, thing, out cushion, out facing);

                // Stood in front of it, and sat when he gets there.
                var front = Forward(facing);
                spot = new Vector3(cushion.X + front.X * SeatFront, cushion.Y + front.Y * SeatFront, mark.Z);
                l.Seat = thing;
                l.Chore = null;
            }
            else if (use == CampUse.Bed)
            {
                // Onto a bed that lies flat; to the mouth of one with a roof on it.
                spot = LiesFlat(thing)
                    ? new Vector3(thing.At.X, thing.At.Y, mark.Z)
                    : new Vector3(thing.At.X + away.X * BedMouth, thing.At.Y + away.Y * BedMouth, mark.Z);
                l.Bed = thing;
                l.Chore = null;
            }
            else
            {
                spot = thing.At + away * ChoreStandOff;
                spot.Z = mark.Z;
                l.Chore = use == CampUse.Fire ? "WORLD_HUMAN_STAND_FIRE" : ChoreFor(thing);
            }

            Loose(l);
            l.Pal = null;
            l.With = null;
            WalkTo(ped, spot, 0f, CampWalkMs);

            l.Going = spot;
            l.Face = thing.At;
            l.State = Stage.Strolling;
            l.NextAt = now + CampWalkMs;
        }

        /// <summary>
        /// At his seat or his bed: sat on it facing the fire, or lying on it. A seat is a
        /// scenario started at the cushion itself, the way Entourage sits people, because a man
        /// told to sit where he stands sits on the air a foot short of it. A bed is the game's
        /// own hobo lying down, which is what WORLD_HUMAN_BUM_SLUMPED is -- on his side, on the
        /// ground. Through the small hours a bed is for the night. See TurnIn.
        /// </summary>
        private void Rest(Life l, Ped ped, int now)
        {
            try
            {
                if (l.Seat != null)
                {
                    Vector3 cushion;
                    float facing;
                    CampSeat(l.Scene, l.Seat, out cushion, out facing);

                    // NOT FROM ACROSS THE CAMP. The scenario puts him on the cushion from wherever
                    // he is, and a man who never got there -- somebody in the way -- would be
                    // pulled onto it from metres off. He goes back to his spot instead.
                    if (ped.Position.DistanceTo(cushion) > SitReach)
                    {
                        GetUp(l);
                        GoHome(l, now);
                        return;
                    }

                    Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);
                    Function.Call(Hash.TASK_START_SCENARIO_AT_POSITION, ped.Handle,
                                  CampSit[Dice.Next(CampSit.Length)],
                                  cushion.X, cushion.Y, cushion.Z, facing, 0, true, true);
                    Function.Call(Hash.SET_PED_KEEP_TASK, ped.Handle, true);

                    l.NextAt = now + Between(SitLeastMs, SitMostMs);
                    return;
                }

                var bed = l.Bed;
                var off = ped.Position - bed.At;
                off.Z = 0f;

                // Never got there: in the day, back to his spot; at night, down where he is.
                if (!_quiet && off.Length() > BedReach)
                {
                    GetUp(l);
                    GoHome(l, now);
                    return;
                }

                var heading = LiesFlat(bed)
                    ? Deg360(Along(bed) + (Steady(bed.At) % 2 == 0 ? 0f : 180f))
                    : Function.Call<float>(Hash.GET_HEADING_FROM_VECTOR_2D, off.X, off.Y);

                Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);
                Function.Call(Hash.SET_ENTITY_HEADING, ped.Handle, heading);
                Scenario(ped, "WORLD_HUMAN_BUM_SLUMPED");

                l.Asleep = _quiet;
                l.NextAt = now + (_quiet ? NightMs : Between(NapLeastMs, NapMostMs));
            }
            catch (Exception ex)
            {
                Log.Debug("Scenery: a camp rest went wrong: " + ex.Message);
                GetUp(l);
                l.NextAt = now + 5000;
            }
        }

        /// <summary>
        /// The small hours at the camp: to bed. The nearest bed round his mark that nobody else
        /// has, and asleep on it until the hours are over -- a man who lives in a camp does not
        /// walk off for the night, he sleeps in it. No bed free, he lies down where he is.
        /// </summary>
        private void TurnIn(Life l, int now)
        {
            var ped = l.Who;
            if (ped == null || !ped.Exists()) return;

            GetUp(l);

            Spooner.Placed best = null;
            var bestD = CampReach;

            foreach (var one in l.Scene.Items)
            {
                if (CampThing(l, one) != CampUse.Bed) continue;

                var d = one.At - l.Item.At;
                d.Z = 0f;
                if (d.Length() >= bestD) continue;

                bestD = d.Length();
                best = one;
            }

            if (best != null)
            {
                GoTo(l, best, CampUse.Bed, now);
                return;
            }

            Loose(l);
            Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);
            Scenario(ped, "WORLD_HUMAN_BUM_SLUMPED");

            l.State = Stage.Loitering;
            l.Asleep = true;
            l.NextAt = now + NightMs;
        }

        /// <summary>Off his seat or his bed, and awake: the next task stands him up.</summary>
        private static void GetUp(Life l)
        {
            l.Seat = null;
            l.Bed = null;
            l.Asleep = false;
        }

        /// <summary>
        /// Where a man sits on a seat, and which way he faces. Measured off the model the way
        /// Seating measures a couch: a seat with a back is sat on a bit under halfway up it, a
        /// low one with none -- a crate, a bucket -- is sat on its top. A couch faces out of its
        /// minus-Y, which Seating learned the hard way; anything else at a camp faces the fire.
        /// </summary>
        private static void CampSeat(Scene scene, Spooner.Placed seat, out Vector3 cushion, out float facing)
        {
            var min = new Vector3(-0.3f, -0.3f, 0f);
            var max = new Vector3(0.3f, 0.3f, 0.45f);

            try
            {
                var lo = new OutputArgument();
                var hi = new OutputArgument();
                Function.Call(Hash.GET_MODEL_DIMENSIONS, seat.ModelHash, lo, hi);
                min = lo.GetResult<Vector3>();
                max = hi.GetResult<Vector3>();
            }
            catch
            {
                // The guess above: a crate.
            }

            var tall = max.Z - min.Z;
            var name = (seat.ModelName ?? "").ToLowerInvariant();
            var couch = name.Contains("couch") || name.Contains("sofa");

            var up = couch || tall >= BacklessUnder ? min.Z + tall * 0.45f : max.Z;
            var mid = new Vector3((min.X + max.X) * 0.5f, (min.Y + max.Y) * 0.5f, up);

            // A couch's front is its minus-Y, so the cushion is a little toward it.
            if (couch) mid.Y -= (max.Y - min.Y) * 0.15f;

            cushion = Rotated(seat.At, seat.Yaw, mid);

            if (couch)
            {
                facing = Deg360(seat.Yaw + 180f);
                return;
            }

            Vector3 fire;

            if (Hearth(scene, seat.At, out fire))
            {
                facing = Function.Call<float>(Hash.GET_HEADING_FROM_VECTOR_2D, fire.X - cushion.X, fire.Y - cushion.Y);
                return;
            }

            facing = Deg360(seat.Yaw);
        }

        /// <summary>The nearest stove or fire to a spot, within reach of it.</summary>
        private static bool Hearth(Scene scene, Vector3 near, out Vector3 fire)
        {
            fire = Vector3.Zero;
            var best = HearthReach;
            var found = false;

            foreach (var one in scene.Items)
            {
                if (one.What != Spooner.Kind.Prop || UseOf(one) != CampUse.Fire) continue;

                var d = one.At - near;
                d.Z = 0f;
                if (d.Length() >= best) continue;

                best = d.Length();
                fire = one.At;
                found = true;
            }

            return found;
        }

        /// <summary>A bed a man lies on rather than in: a bag, a mattress. A shelter or a tent has a roof on it, and he lies at its mouth.</summary>
        private static bool LiesFlat(Spooner.Placed bed)
        {
            var n = (bed.ModelName ?? "").ToLowerInvariant();
            return !(n.Contains("shelter") || n.Contains("tent"));
        }

        /// <summary>The heading along a bed's long side, off the model.</summary>
        private static float Along(Spooner.Placed bed)
        {
            try
            {
                var lo = new OutputArgument();
                var hi = new OutputArgument();
                Function.Call(Hash.GET_MODEL_DIMENSIONS, bed.ModelHash, lo, hi);
                var min = lo.GetResult<Vector3>();
                var max = hi.GetResult<Vector3>();

                return max.Y - min.Y >= max.X - min.X ? bed.Yaw : bed.Yaw + 90f;
            }
            catch
            {
                return bed.Yaw;
            }
        }

        /// <summary>A point on a placed model from the model's own axes: its heading turns the offset.</summary>
        private static Vector3 Rotated(Vector3 at, float yaw, Vector3 local)
        {
            var h = yaw * (float)(Math.PI / 180.0);
            var c = (float)Math.Cos(h);
            var s = (float)Math.Sin(h);
            return new Vector3(at.X + local.X * c - local.Y * s, at.Y + local.X * s + local.Y * c, at.Z + local.Z);
        }

        /// <summary>The way a heading points, on the ground.</summary>
        private static Vector3 Forward(float heading)
        {
            var h = heading * (float)(Math.PI / 180.0);
            return new Vector3(-(float)Math.Sin(h), (float)Math.Cos(h), 0f);
        }

        private static Vector3 Turned(Vector3 v, float degrees)
        {
            var h = degrees * (float)(Math.PI / 180.0);
            var c = (float)Math.Cos(h);
            var s = (float)Math.Sin(h);
            return new Vector3(v.X * c - v.Y * s, v.X * s + v.Y * c, v.Z);
        }

        private static float Deg360(float deg)
        {
            deg %= 360f;
            return deg < 0f ? deg + 360f : deg;
        }

        /// <summary>How far round his mark a camp's things are his, how far above his feet one may be, and how far a fire warms a seat.</summary>
        private const float CampReach = 9f;
        private const float CampAbove = 0.6f;
        private const float HearthReach = 6f;

        /// <summary>After a sit or a nap: on to the next thing this often in a hundred, back to his spot the rest.</summary>
        private const int CampOnwardChance = 55;
        private const int CampWalkMs = 20000;

        /// <summary>Where he stands before he sits, how near the cushion that must be, and a roofed bed's mouth.</summary>
        private const float SeatFront = 0.8f;
        private const float SitReach = 2.6f;
        private const float BedMouth = 1.0f;
        private const float BedReach = 2.6f;

        /// <summary>Lower than this and a seat has no back: he sits on its top.</summary>
        private const float BacklessUnder = 0.75f;

        private const int SitLeastMs = 45000;
        private const int SitMostMs = 120000;
        private const int NapLeastMs = 60000;
        private const int NapMostMs = 150000;

        /// <summary>Asleep for the night; the small hours ending is what wakes him. See Nudge.</summary>
        private const int NightMs = 600000;

        /// <summary>How a man at a camp sits: with a can, or just sat.</summary>
        private static readonly string[] CampSit =
        {
            "PROP_HUMAN_SEAT_CHAIR_DRINK_BEER",
            "PROP_HUMAN_SEAT_CHAIR",
            "PROP_HUMAN_SEAT_BENCH_DRINK"
        };

        /// <summary>A gang sign, thrown up over whatever he is doing, and the idle back after it.</summary>
        private void Sign(Life l, int now)
        {
            var pick = Signs[Dice.Next(Signs.Length)];
            Give(l.Who, pick[0], pick[1], SignFlag);

            // A SIGN IS SAID AS WELL AS THROWN. Silent, it is a man waving his hands.
            if (Fam(l.Item) && Dice.Next(100) < 60) Say(l, Voices.Kind.Gang, FamSign, now);

            l.State = Stage.Signing;
            l.NextAt = now + SignMs;
        }

        /// <summary>
        /// Up the road and back. The scene's Note names a spot; he walks to it, spends a
        /// couple of minutes at it dancing or smoking or talking, and walks home to his mark.
        /// Lamar's yard is 118 m from Parkview, well past any stroll, which is why this is
        /// its own errand and not a wider radius.
        ///
        /// NOT MANY AT ONCE (OutingAtOnce), so a party up the road never empties the place.
        /// </summary>
        private bool Outing(Life l, int now)
        {
            var scene = l.Scene;
            if (scene.Outings.Count == 0) return false;

            // The party is a navmesh walk away, and a man on his own floor has none.
            if (l.Straight) return false;

            var gone = 0;

            foreach (var o in _lives)
            {
                if (o.Scene != scene) continue;
                if (o.State == Stage.Outbound || o.State == Stage.Partying) gone++;
            }

            if (gone >= OutingAtOnce) return false;

            var to = scene.Outings[Dice.Next(scene.Outings.Count)];

            Vector3 spot;
            if (!Spot(to, 1f, Math.Max(2f, scene.OutingReach), out spot)) spot = to;

            Loose(l);
            WalkTo(l.Who, spot, 0f);

            l.Going = spot;
            l.State = Stage.Outbound;
            l.NextAt = now + OutingWalkMs;

            Log.Debug(Say(l.Item) + " walks up to the party.");
            return true;
        }

        /// <summary>The dance is an ANIMATION, and this is the one place that is the right way
        /// round: there is no dancing scenario in the game's list, and dancing is the one thing
        /// that needs nothing in his hands. Everything else at a party does -- a drink, a
        /// cigarette, a joint, a phone -- so those stay scenarios. Every name is out of
        /// Scenarios.txt and PedAnimList.txt, checked rather than remembered.</summary>
        private const string DanceDict = "anim@amb@nightclub@dancers@club_ambientpeds@";

        private static readonly string[] DanceMen =
        {
            "li-mi_amb_club_06_base_male^6",
            "li-mi_amb_club_11_v1_male^4",
            "mi-hi_amb_club_09_v1_male^1"
        };

        private static readonly string[] DanceWomen =
        {
            "mi-hi_amb_club_06_base_female^1",
            "mi-hi_amb_club_06_base_female^5",
            "li-mi_amb_club_06_base_female^5",
            "li-mi_amb_club_12_v1_female^3",
            "mi-hi_amb_club_13_v1_female^6"
        };

        private static readonly string[] Revels =
        {
            "WORLD_HUMAN_PARTYING",
            "WORLD_HUMAN_SMOKING_POT_CLUBHOUSE",
            "WORLD_HUMAN_SMOKING_CLUBHOUSE",
            "WORLD_HUMAN_HANG_OUT_STREET_CLUBHOUSE",
            "WORLD_HUMAN_DRINKING",
            "WORLD_HUMAN_CHEERING",
            "WORLD_HUMAN_STAND_MOBILE_UPRIGHT_CLUBHOUSE",
            "WORLD_HUMAN_STUPOR_CLUBHOUSE"
        };

        /// <summary>What a man does once he gets there: dances, or has a drink, a smoke, a word.</summary>
        private static void Revel(Life l)
        {
            var ped = l.Who;
            if (ped == null || !ped.Exists()) return;

            try
            {
                Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);

                if (Dice.Next(100) < 45)
                {
                    var clip = Male(ped) ? DanceMen[Dice.Next(DanceMen.Length)]
                                         : DanceWomen[Dice.Next(DanceWomen.Length)];
                    Give(ped, DanceDict, clip);
                    return;
                }

                Scenario(ped, Revels[Dice.Next(Revels.Length)]);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not get him going at the party: " + ex.Message);
            }
        }

        /// <summary>
        /// The ones who walk drunk. A man who has been on it all evening should not walk like
        /// a soldier, and Michael asked for it by name. The clipsets are out of
        /// PedAnimList.txt -- a bad one is accepted in silence and changes nothing, so they
        /// are checked, not remembered.
        ///
        /// The set has to be LOADED before it will take, so a request that is not ready yet
        /// simply returns and the next beat tries again. See Choose.
        /// </summary>
        private static readonly string[] DrunkWalks =
        {
            "move_m@drunk@slightlydrunk",
            "move_m@drunk@moderatedrunk",
            "move_m@drunk@verydrunk",
            "move_m@buzzed"
        };

        private static void Souse(Life l)
        {
            var ped = l.Who;
            if (ped == null || !ped.Exists()) return;

            var set = DrunkWalks[Steady(l.Item.At) % DrunkWalks.Length];

            try
            {
                if (!Function.Call<bool>(Hash.HAS_ANIM_SET_LOADED, set))
                {
                    Function.Call(Hash.REQUEST_ANIM_SET, set);
                    return;
                }

                Function.Call(Hash.SET_PED_MOVEMENT_CLIPSET, ped.Handle, set, 1.0f);
                l.Drunken = true;
            }
            catch (Exception ex)
            {
                l.Drunken = true;
                Log.Debug("Could not give him the drunk walk: " + ex.Message);
            }
        }

        private void Stroll(Life l, int now)
        {
            // A man on his own floor strolls along it. See StrollAlong.
            if (l.Straight)
            {
                StrollAlong(l, now);
                return;
            }

            // Zero rather than bare, because the roamer's loop below may find nothing
            // on any of its goes and the compiler cannot see that the fallback covers it.
            var to = Vector3.Zero;

            // A man the file says roams walks his own radius, not the twelve metres a stroll
            // otherwise covers. See Roams.
            var most = l.Roams && l.Scene.RoamMost > 0f ? l.Scene.RoamMost : StrollMost;

            if (l.Roams && !l.Stays)
            {
                // FROM WHERE HE IS STOOD, NOT FROM THE MARK. Setting off from the mark every
                // time is a man on a lead: he goes out, comes back to the middle, goes out
                // again. Walking on from where he finished is how somebody drifts across a
                // place over an evening -- which is what Michael asked for, not wanting them
                // in the spot they were spawned in.
                //
                // The mark is still the leash: a spot further than his radius from it is
                // thrown back, so he drifts without ever wandering out of the scene.
                var got = false;

                for (var n = 0; n < 6 && !got; n++)
                {
                    if (!Spot(l.Who.Position, RoamStepLeast, RoamStepMost, out to)) continue;
                    got = to.DistanceTo(l.Item.At) <= most;
                }

                if (!got && !Spot(l.Item.At, StrollLeast, most, out to))
                {
                    l.NextAt = now + Beat(l);
                    return;
                }
            }
            else if (!Spot(l.Item.At, StrollLeast, most, out to))
            {
                l.NextAt = now + Beat(l);
                return;
            }

            Loose(l);
            WalkTo(l.Who, to, (float)(Dice.NextDouble() * 360.0));

            l.Going = to;
            l.State = Stage.Strolling;
            l.NextAt = now + WalkMs;

            // And the nearest neighbour comes along, half the time. See Join.
            if (!l.Roams) Join(l, to, now);
        }

        /// <summary>
        /// Off for a bit, or for the night: a walk well away, and gone once nobody can see
        /// him. False when there is nowhere to walk to, and then he has gone nowhere and the
        /// caller does something else with him.
        /// </summary>
        private bool Leave(Life l, int now, bool forTheNight)
        {
            // A man on his own floor leaves along it. See LeaveAlong.
            if (l.Straight) return LeaveAlong(l, now, forTheNight);

            var ped = l.Who;
            Vector3 to;

            if (!Spot(l.Item.At, LeaveLeast, LeaveMost, out to))
            {
                // NOWHERE TO WALK TO, AND SO HE DOES NOT GO. This used to delete him where he
                // stood the moment nobody looked, which was vanishing, not leaving: a corner
                // you glance back at with a man gone from it. He is asked again in a while,
                // and whoever asked gives him a stroll instead.
                l.NextAt = now + LeaveRetryMs;
                return false;
            }

            Loose(l);
            l.Pal = null;
            l.With = null;
            WalkTo(ped, to, 0f);

            l.Going = to;
            l.State = Stage.Leaving;
            l.ForTheNight = forTheNight;
            l.Waiting = false;
            l.NextAt = now + WalkMs;

            Log.Debug(Say(l.Item) + " in " + l.Scene.Name + (forTheNight ? " walks off for the night." : " walks off for a bit."));
            return true;
        }

        /// <summary>
        /// Stood up somewhere out of sight, a way off, and walked in to the mark. False when
        /// he is given up on for this build of the scene.
        /// </summary>
        private bool ComeBack(Life l, int now, Vector3 here)
        {
            // Still stood where the last teardown left him: taken back rather than made twice.
            if (Rejoin(l, now)) return true;

            // TWO RINGS. The far one first, so he walks in from a way off; the nearer one
            // when every far point is in view, which on a corner you are stood in the middle
            // of is most of the time. Either way the point has to be out of sight -- off frame
            // or behind a building -- and no nearer the player than ArriveNoNearer.
            var from = Vector3.Zero;
            var found = false;

            foreach (var ring in new[] { new[] { LeaveLeast, LeaveMost }, new[] { ArriveLeast, LeaveLeast } })
            {
                Vector3 at;
                if (!Spot(l.Item.At, ring[0], ring[1], out at)) continue;

                // HIS MIDDLE, a metre up, the way every mark is saved: a navmesh point is the
                // ground, and a man whose middle is put at the ground is stood half sunk in it.
                at.Z += 1.0f;

                if (at.DistanceTo(here) < ArriveNoNearer || !OutOfSight(l.Scene, at)) continue;

                from = at;
                found = true;
                break;
            }

            if (!found)
            {
                // Every way in is in view. Try again shortly, from another side.
                l.NextAt = now + 15000;
                return true;
            }

            // THE TWIN GUARD, kept from the on-mark build: an orphan of him left by an
            // earlier session is already stood there, and a second is the pair in one coat.
            if (Squatter(l.Item))
            {
                Log.Info("Scenery: " + Say(l.Item) + " is already stood on his mark, and not by us; not making a second.");
                return false;
            }

            Entity made;
            var verdict = Put(l.Item, out made, from);

            if (verdict == Verdict.NotYet) { l.Why = "his model is not in yet"; l.NextAt = now + 2000; return true; }

            if (verdict == Verdict.No || made == null)
            {
                Log.Info("Scenery: " + Say(l.Item) + " in \"" + l.Scene.Name + "\" would not come back -- the game would not make him; given up for this build.");
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
            WhoHeIs(l, ped, now);

            // Stood up frozen (Person does that) and held until there is ground under the
            // far spot; then loosed and walked. At load the far spot can be forty metres of
            // nothing for a second or two.
            l.Who = ped;
            l.State = Stage.ComingBack;
            l.Held = true;
            l.ForTheNight = false;
            l.Tries = 0;
            l.Since = 0;
            l.NextAt = now + HoldMostMs;

            Log.Debug(Say(l.Item) + " in " + l.Scene.Name + " comes back.");
            return true;
        }

        // ==================================================================
        // Along his own floor
        // ==================================================================

        /// <summary>
        /// ON PROPS THERE IS NO NAVMESH. The galleries of the blocks, the roof at the courts,
        /// the floor of the shop and the yard in front of it are script props, and the game's
        /// paths know nothing about them: a nav walk aimed at a balcony mark ends on the
        /// ground under it, and one aimed out of the shop pushes into the shelves. So the
        /// people the file names in straight: never get a navmesh task at all. Their floor is
        /// found with rays instead -- straight lines out from the mark, sixteen bearings and
        /// the four axes of the building under him, floor under the centre every half metre
        /// and under both shoulders every metre, all at the same level, stopping short of a
        /// wall, a window or the edge -- and every walk they take is a straight line along
        /// one of those, with TASK_GO_STRAIGHT_TO_COORD. Probed once per build, after the
        /// floor has collision and the props round it have bodies, and throttled, because
        /// the shape tests are synchronous. See FloorOf.
        ///
        /// Walking in, he is stood up at a probed point a few metres along his floor that is
        /// hidden right now -- behind a wall or a building first, out of frame failing that
        /// -- and walks straight to the mark (ComeBackAlong). A stroll is a probed point a
        /// few metres off and back (StrollAlong). A walk-off is the farthest probed point
        /// that is hidden, and he is gone once nobody can see him (LeaveAlong). With no
        /// hidden point to go to he does not go: he strolls instead, and at night he is asked
        /// again in a while. A man with no floor to walk along at all is put on his mark only
        /// while nobody can see it (Appear), and stays there.
        /// </summary>
        /// <summary>What is under a point, looking straight down from just above it: the height of it, and the thing it belongs to.</summary>
        private static bool FloorUnder(Vector3 root, out float z, out Entity on)
        {
            z = 0f;
            on = null;

            try
            {
                var hit = World.Raycast(new Vector3(root.X, root.Y, root.Z + 0.2f),
                                        new Vector3(root.X, root.Y, root.Z - 1.8f),
                                        IntersectFlags.Map | IntersectFlags.Objects, null);

                if (!hit.DidHit) return false;

                z = hit.HitPosition.Z;
                on = hit.HitEntity;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Whether every prop of ours near a point has been looked at for a body (see Solid).
        /// A floor probed before the shelves round it are solid is a floor with no walls in
        /// it, and the lines it proves run through them.
        /// </summary>
        private static bool ReadyRound(Scene scene, Vector3 at, float r)
        {
            foreach (var pair in scene.Was)
            {
                var was = pair.Value;
                if (was == null || was.What == Spooner.Kind.Ped) continue;
                if (Flat(was.At - at) > r) continue;
                if (!scene.Solid.Contains(pair.Key)) return false;
            }

            return true;
        }

        /// <summary>
        /// Where he can get to along his own floor: the probe described at the top of this
        /// section, run once and kept on the life. Null when it is not there yet -- nothing
        /// under the mark, this beat's probe already spent, or his bearings only part done --
        /// and an empty list when there is nowhere at all, which is said once in the log.
        ///
        /// A FEW BEARINGS A BEAT. An open floor -- the roof at the courts, the yard -- is
        /// twenty bearings of sixteen metres each, and every one of them is a march of rays;
        /// all in one tick is a hitch. So each call does AlongBearingsPerProbe of them and
        /// keeps its place on the life (Bearings, Probed, Probing), and the floor is only
        /// handed over once the last bearing is walked. A balcony is two or three live
        /// bearings and the rest stop at the wall in one ray, so it is done in a call.
        /// </summary>
        private static List<Footing> FloorOf(Life l)
        {
            if (l.Floor != null) return l.Floor;
            if (_probes >= AlongProbesPerBeat) return null;

            var mark = l.Item.At;
            float under;
            Entity on;

            // Nothing under the mark yet is nothing to probe; asked again next time.
            if (!FloorUnder(mark, out under, out on)) return null;

            _probes++;

            var lift = mark.Z - under;

            if (l.Bearings == null)
            {
                // THE BEARINGS: sixteen round the compass, and the four axes of the building
                // he is stood on, so a gallery run is followed exactly rather than to the
                // nearest twenty-two and a half degrees. Written down once, so that a probe
                // spread over beats walks the same list whatever is under the mark later.
                l.Bearings = new List<float>();
                for (var i = 0; i < AlongBearings; i++) l.Bearings.Add(i * (360f / AlongBearings));

                if (on != null && on is Prop)
                {
                    var axis = 0f;
                    try { axis = on.Heading; } catch { /* the compass, then */ }

                    foreach (var turn in new[] { 0f, 90f, 180f, 270f }) l.Bearings.Add(axis + turn);
                }

                l.Probing = new List<Footing>();
                l.Probed = 0;
                l.ProbeFar = 0f;
            }

            var floor = l.Probing;
            var end = Math.Min(l.Bearings.Count, l.Probed + AlongBearingsPerProbe);

            for (; l.Probed < end; l.Probed++)
            {
                var bearing = l.Probed;
                var heading = l.Bearings[bearing];
                var rad = heading * Math.PI / 180.0;
                var dir = new Vector3(-(float)Math.Sin(rad), (float)Math.Cos(rad), 0f);
                var side = new Vector3(-dir.Y, dir.X, 0f) * AlongWidth;

                // HOW FAR BEFORE A WALL: rays along the line at the shin and at the chest,
                // down the centre and down both shoulders -- a shelf end a hand's width off
                // the centre line is still a shelf end in his shoulder -- with glass counted,
                // because a window is a wall to a man walking. The nearest hit of the six.
                var clear = AlongMost;

                foreach (var s in new[] { 0f, 1f, -1f })
                {
                    foreach (var up in new[] { 0.5f, 1.3f })
                    {
                        try
                        {
                            var from = new Vector3(mark.X + side.X * s, mark.Y + side.Y * s, under + up);
                            var hit = World.Raycast(from, dir, AlongMost,
                                                    IntersectFlags.Map | IntersectFlags.Objects | IntersectFlags.Glass, l.Who);

                            if (hit.DidHit)
                            {
                                var d = Flat(hit.HitPosition - from);
                                if (d < clear) clear = d;
                            }
                        }
                        catch
                        {
                            clear = 0f;
                        }
                    }
                }

                // THE MARCH: half a metre at a time, floor under the centre every step and
                // under both shoulders every metre, each at the level of the last. The first
                // failure is the edge, and the run stops short of it.
                var steps = 0;
                var lastZ = under;
                var zs = new List<float>();
                var edge = false;

                for (var d = AlongStep; d <= clear - AlongShort + 0.001f; d += AlongStep)
                {
                    var centre = new Vector3(mark.X + dir.X * d, mark.Y + dir.Y * d, lastZ + lift);

                    float z;
                    Entity what;

                    if (!FloorUnder(centre, out z, out what) || Math.Abs(z - lastZ) > AlongLevel)
                    {
                        edge = true;
                        break;
                    }

                    if (steps % 2 == 1)
                    {
                        var wide = true;

                        foreach (var s in new[] { 1f, -1f })
                        {
                            float sz;
                            var shoulder = new Vector3(centre.X + side.X * s, centre.Y + side.Y * s, z + lift);

                            if (!FloorUnder(shoulder, out sz, out what) || Math.Abs(sz - z) > AlongLevel)
                            {
                                wide = false;
                                break;
                            }
                        }

                        if (!wide)
                        {
                            edge = true;
                            break;
                        }
                    }

                    zs.Add(z);
                    lastZ = z;
                    steps++;
                }

                var reach = steps * AlongStep - (edge ? AlongShort : 0f);
                if (reach < 1f) continue;

                // Written down every AlongSpacing along the run, and once at the end of it.
                var kept = 0f;

                for (var d = AlongSpacing; d <= reach + 0.001f; d += AlongSpacing)
                {
                    var i = Math.Min(zs.Count - 1, (int)Math.Round(d / AlongStep) - 1);
                    floor.Add(new Footing { At = new Vector3(mark.X + dir.X * d, mark.Y + dir.Y * d, zs[i] + lift), D = d, Bearing = bearing });
                    kept = d;
                }

                if (reach - kept > 0.25f)
                {
                    floor.Add(new Footing { At = new Vector3(mark.X + dir.X * reach, mark.Y + dir.Y * reach, zs[zs.Count - 1] + lift), D = reach, Bearing = bearing });
                }

                if (reach > l.ProbeFar) l.ProbeFar = reach;
            }

            // More bearings to walk: next beat, after somebody else's turn.
            if (l.Probed < l.Bearings.Count) return null;

            l.Floor = floor;
            l.Probing = null;
            l.Bearings = null;

            if (floor.Count == 0)
            {
                Log.Info("Scenery: " + Say(l.Item) + " in \"" + l.Scene.Name + "\" has no floor to walk along; " +
                         "he is put on his mark out of sight and stays there.");
            }
            else
            {
                Log.Debug(Say(l.Item) + " in " + l.Scene.Name + " has " + floor.Count + " point(s) along his floor, the farthest " +
                          l.ProbeFar.ToString("0.0") + " m off.");
            }

            return floor;
        }

        /// <summary>
        /// The points to look at for a hidden spot: one a bearing, the farthest of those
        /// between least and most along it that the caller will have, farthest first. One a
        /// bearing because a look is up to three rays a point (see Behind) and an open roof
        /// has a hundred points on it; the nearer ones on a bearing are only ever a worse
        /// version of the far one, and the caller asks for a nearer band when nothing in
        /// this one will do.
        /// </summary>
        private static List<Footing> Farthest(List<Footing> floor, float least, float most, Func<Footing, bool> ok)
        {
            var sorted = new List<Footing>(floor);
            sorted.Sort((a, b) => b.D.CompareTo(a.D));

            var taken = new HashSet<int>();
            var can = new List<Footing>();

            foreach (var p in sorted)
            {
                if (p.D < least || p.D >= most) continue;
                if (taken.Contains(p.Bearing)) continue;
                if (!ok(p)) continue;

                taken.Add(p.Bearing);
                can.Add(p);
            }

            return can;
        }

        /// <summary>
        /// The first of those points nobody can see, in the order given: behind something
        /// first, and only failing that out of frame, because the frame moves with the
        /// player's head and a wall does not. Every Behind is counted against this beat's
        /// budget (AlongLooksPerBeat); when it runs out part way, spent is true and the
        /// caller asks again next beat rather than settling for a worse point now.
        /// </summary>
        private static Footing Hidden(Scene scene, List<Footing> can, out bool spent)
        {
            spent = false;

            foreach (var p in can)
            {
                if (_looks >= AlongLooksPerBeat)
                {
                    spent = true;
                    return null;
                }

                _looks++;
                if (Behind(scene, p.At)) return p;
            }

            foreach (var p in can)
            {
                if (!Seen(p.At)) return p;
            }

            return null;
        }

        /// <summary>How near the line between him and there somebody else may stand: a capsule's width plus a man's own.</summary>
        private const float InWay = 0.65f;

        /// <summary>Whether somebody is stood on the line between this man and there. See LineBlocked.</summary>
        private static bool PedInWay(Ped ped, Vector3 to) => LineBlocked(ped.Position, to, ped.Handle);

        /// <summary>
        /// Whether somebody is stood on the line from here to there. A straight walk goes
        /// round nobody, so nobody may be on it when a man sets off -- and nobody may be on
        /// the line in from the point he is about to be stood up at, either, which is why
        /// this takes two points and a handle to leave out (0 for a man not made yet) rather
        /// than a ped.
        ///
        /// MEASURED, NOT CAST. A capsule shape test is what this wants, and the game's is
        /// asynchronous -- its result is not always in on the frame it is asked for, and
        /// "no result yet" reads exactly like "nobody there". The peds near the line are a
        /// handful, so each is measured against it instead: on the same floor, past the first
        /// half metre, no farther than the far end, and within InWay of the line sideways.
        /// </summary>
        private static bool LineBlocked(Vector3 from, Vector3 to, int ignore)
        {
            try
            {
                var way = to - from;
                way.Z = 0f;

                var len = way.Length();
                if (len < 0.6f) return false;
                way.Normalize();

                var mid = (from + to) * 0.5f;
                var near = World.GetNearbyPeds(mid, len * 0.5f + 1f);
                if (near == null) return false;

                foreach (var other in near)
                {
                    if (other == null || other.Handle == ignore || !other.Exists()) continue;

                    var at = other.Position;
                    if (Math.Abs(at.Z - from.Z) > OffFloor) continue;

                    var rel = at - from;
                    rel.Z = 0f;

                    var along = rel.X * way.X + rel.Y * way.Y;
                    if (along < 0.5f || along > len) continue;

                    var across = Math.Abs(rel.X * way.Y - rel.Y * way.X);
                    if (across <= InWay) return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Whether anybody is stood on that spot already.</summary>
        private static bool Occupied(Vector3 at)
        {
            try
            {
                var near = World.GetNearbyPeds(at, 0.8f);
                return near != null && near.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>A stretch of the legs along his own floor: a probed point a few metres off, a stand there, and back. See Stroll.</summary>
        private void StrollAlong(Life l, int now)
        {
            var floor = FloorOf(l);

            if (floor == null)
            {
                l.NextAt = now + 1000;
                return;
            }

            var can = new List<Footing>();

            foreach (var p in floor)
            {
                if (p.D < AlongStrollLeast || p.D > AlongStrollMost) continue;
                can.Add(p);
            }

            // A few draws, each asked whether anybody is stood on the way, rather than the
            // whole roof measured for one stroll.
            Footing to = null;

            for (var draw = 0; draw < 4 && can.Count > 0 && to == null; draw++)
            {
                var i = Dice.Next(can.Count);
                var p = can[i];
                can.RemoveAt(i);

                if (!PedInWay(l.Who, p.At)) to = p;
            }

            if (to == null)
            {
                // Nowhere along it he can get to right now: the next thing on his list instead.
                l.Turn++;
                Doing(l.Scene, l.Who, l.Item, l.Turn);
                l.NextAt = now + Beat(l);
                return;
            }

            Loose(l);
            GoStraight(l.Who, to.At, (float)(Dice.NextDouble() * 360.0), WalkMs);
            l.Closing = false;

            l.Going = to.At;
            l.State = Stage.Strolling;
            l.NextAt = now + WalkMs;

            Join(l, to.At, now);
        }

        /// <summary>
        /// Off along his own floor, from his mark, to the farthest probed point that is hidden
        /// right now -- behind something first, out of frame failing that -- and gone once
        /// nobody can see him. False, and he has not moved, when there is no such point.
        /// See Leave.
        /// </summary>
        private bool LeaveAlong(Life l, int now, bool forTheNight)
        {
            // He leaves from his mark: every line the probe proved starts there.
            if (Flat(l.Who.Position - l.Item.At) > 0.8f)
            {
                l.NextAt = now + LeaveRetryMs;
                return false;
            }

            if (_looks >= AlongLooksPerBeat)
            {
                l.NextAt = now + 1000;
                return false;
            }

            var floor = FloorOf(l);

            if (floor == null)
            {
                l.NextAt = now + 1000;
                return false;
            }

            if (floor.Count == 0)
            {
                l.NextAt = now + LeaveRetryMs;
                return false;
            }

            // The far end of every bearing that is far enough and has nobody on it, the
            // farthest first; the first of those that is hidden. See Farthest and Hidden.
            var can = Farthest(floor, AlongLeaveLeast, float.MaxValue, p => !PedInWay(l.Who, p.At));

            bool spent;
            var to = Hidden(l.Scene, can, out spent);

            if (spent)
            {
                l.NextAt = now + 1000;
                return false;
            }

            if (to == null)
            {
                // NOWHERE HIDDEN TO WALK TO, and so he does not go. See Leave.
                l.NextAt = now + LeaveRetryMs;
                return false;
            }

            Loose(l);
            l.Pal = null;
            l.With = null;
            GoStraight(l.Who, to.At, HeadingTo(l.Who.Position, to.At), WalkMs);
            l.Closing = false;

            l.Going = to.At;
            l.State = Stage.Leaving;
            l.ForTheNight = forTheNight;
            l.Waiting = false;
            l.NextAt = now + WalkMs;

            Log.Debug(Say(l.Item) + " in " + l.Scene.Name + " walks off along his floor" +
                      (forTheNight ? " for the night." : " for a bit."));
            return true;
        }

        /// <summary>
        /// Stood up a few metres along his own floor, somewhere hidden, and walked straight in
        /// to the mark. Not until the floor is there to probe; and with no floor at all, on
        /// his mark out of sight instead. False when he is given up on for this build.
        /// </summary>
        private bool ComeBackAlong(Life l, int now, Vector3 here)
        {
            if (Rejoin(l, now)) return true;
            if (l.Since == 0) l.Since = now;

            var mark = l.Item.At;

            // NOT UNTIL HIS FLOOR IS THERE: collision under the mark, and every prop of ours
            // within reach of it looked at for a body, or the probe would find no walls. If
            // it never comes, the backstop is the wait an elevated Cold gets, and then Appear
            // puts him on his mark out of sight and Thawing and Earth decide.
            if (!Floored(mark, null) || !ReadyRound(l.Scene, mark, AlongMost + 2f))
            {
                if (now - l.Since > ElevatedMostMs && GroundReady(l.Scene)) return Appear(l, now, here);

                l.Why = "waiting for the ground and the props round his mark";
                l.NextAt = now + AlongRetryMs;
                return true;
            }

            if (_looks >= AlongLooksPerBeat)
            {
                l.NextAt = now + 1000;
                return true;
            }

            var floor = FloorOf(l);

            if (floor == null)
            {
                l.Why = "probing his floor";
                l.NextAt = now + 1000;
                return true;
            }

            // No floor to walk along: on his mark, then, while nobody can see it.
            if (floor.Count == 0) return Appear(l, now, here);

            // A START: a few metres along, no nearer the player than AlongNoNearer, nobody
            // stood on it, NOBODY ON THE LINE IN from it to the mark -- his neighbour on his
            // own mark a metre along the gallery, say; a go-straight would shove him -- and
            // hidden: behind something first, out of frame failing that, the farthest first
            // (see Farthest and Hidden). The nearer band only when nothing farther will do.
            Footing start = null;
            var spent = false;

            foreach (var band in new[] { new[] { AlongArriveLeast, AlongArriveMost }, new[] { 1.5f, AlongArriveLeast } })
            {
                var can = Farthest(floor, band[0], band[1],
                                   p => p.At.DistanceTo(here) >= AlongNoNearer && !Occupied(p.At) && !LineBlocked(p.At, mark, 0));

                start = Hidden(l.Scene, can, out spent);
                if (start != null || spent) break;
            }

            if (spent)
            {
                l.Why = "looking for a start along his floor that is out of sight";
                l.NextAt = now + 1000;
                return true;
            }

            if (start == null)
            {
                // THIS IS WHERE A MAN BOXED IN BY FURNITURE STAYS FOREVER, silently, which is
                // how the two Koreans behind the shop counter went missing on 2026-09-26:
                // every point along their floor had a shelf or the counter on the line in.
                // Name him in stays: and he is put on his mark instead. See Appear.
                l.Why = "no start along his floor that is out of sight, clear of the furniture and five metres from you";
                l.NextAt = now + AlongRetryMs;
                return true;
            }

            if (Squatter(l.Item))
            {
                Log.Info("Scenery: " + Say(l.Item) + " is already stood on his mark, and not by us; not making a second.");
                return false;
            }

            Entity made;
            var verdict = Put(l.Item, out made, start.At);

            if (verdict == Verdict.NotYet) { l.Why = "his model is not in yet"; l.NextAt = now + 2000; return true; }

            if (verdict == Verdict.No || made == null)
            {
                Log.Info("Scenery: " + Say(l.Item) + " in \"" + l.Scene.Name + "\" would not come back -- the game would not make him; given up for this build.");
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
            WhoHeIs(l, ped, now);

            // Frozen (Person does that) and held until the floor is under him; then loosed
            // and walked straight to the mark. See the Returning stage.
            l.Who = ped;
            l.State = Stage.ComingBack;
            l.Held = true;
            l.ForTheNight = false;
            l.Tries = 0;
            l.Since = 0;
            l.NextAt = now + HoldMostMs;

            Log.Debug(Say(l.Item) + " in " + l.Scene.Name + " comes out along his floor, " +
                      start.D.ToString("0.0") + " m off his mark.");
            return true;
        }

        /// <summary>
        /// On his mark, directly -- only while nobody can see the mark and it is not right by
        /// the player. For a man in bed (stays:), and for one with no floor to walk along. He
        /// is stood up the way the build stands anybody up: frozen until the ground under him
        /// is confirmed, with his idle (see StandOnMark). False when he is given up on for
        /// this build.
        /// </summary>
        private bool Appear(Life l, int now, Vector3 here)
        {
            if (Rejoin(l, now)) return true;
            if (l.Since == 0) l.Since = now;

            var mark = l.Item.At;

            if (mark.DistanceTo(here) < AlongNoNearer || !OutOfSight(l.Scene, mark))
            {
                l.Why = "his mark is in view, or within five metres of you";
                l.NextAt = now + AlongRetryMs;
                return true;
            }

            if (Squatter(l.Item))
            {
                Log.Info("Scenery: " + Say(l.Item) + " is already stood on his mark, and not by us; not making a second.");
                return false;
            }

            // The mark given as the place: Put's own twin check is only for the build, and
            // Squatter has just asked the same question with the stragglers left out.
            Entity made;
            var verdict = Put(l.Item, out made, mark);

            if (verdict == Verdict.NotYet) { l.Why = "his model is not in yet"; l.NextAt = now + 2000; return true; }

            if (verdict == Verdict.No || made == null)
            {
                Log.Info("Scenery: " + Say(l.Item) + " in \"" + l.Scene.Name + "\" would not appear -- the game would not make him; given up for this build.");
                return false;
            }

            var ped = made as Ped;

            if (ped == null)
            {
                Log.Debug(Say(l.Item) + " in " + l.Scene.Name + " appeared as something else.");
                return false;
            }

            l.Scene.Made++;
            Register(l.Scene, l.Item, made, true);
            StandOnMark(l.Scene, l.Item, ped);
            WhoHeIs(l, ped, now);

            l.Who = ped;
            l.State = Stage.Marked;
            l.Held = false;
            l.ForTheNight = false;
            l.Since = 0;
            l.NextAt = now + Beat(l);

            Log.Debug(Say(l.Item) + " in " + l.Scene.Name + " is on his mark, unseen.");
            return true;
        }

        /// <summary>
        /// The nearest neighbour brought along on a stroll, half the time: he walks to a spot
        /// of his own a step short of where the first is going, and the two talk there once
        /// both have arrived (see Together). Somebody on his mark, on the same floor and of
        /// the same kind -- a man on a gallery takes a man on the gallery, on a probed point
        /// of HIS own floor near the spot.
        /// </summary>
        private void Join(Life l, Vector3 to, int now)
        {
            if (l.Roams || l.Stays || Dice.Next(100) >= PalChance) return;

            Life pal = null;
            var best = ChatReach;

            foreach (var o in _lives)
            {
                if (o == l || o.Scene != l.Scene || o.State != Stage.Marked) continue;
                if (o.Scripted || o.Stays || o.Roams || o.TakenAt != 0) continue;
                if (o.Straight != l.Straight) continue;
                if (o.Who == null || !o.Who.Exists() || !o.Who.IsAlive) continue;
                if (StillCold(o)) continue;
                if (Math.Abs(o.Who.Position.Z - l.Who.Position.Z) > OffFloor) continue;

                var d = o.Who.Position.DistanceTo(l.Who.Position);
                if (d < best) { best = d; pal = o; }
            }

            if (pal == null) return;

            // A step short of the spot, on the near side of it, so the two end up facing.
            var way = to - l.Who.Position;
            way.Z = 0f;
            if (way.Length() < 0.1f) return;
            way.Normalize();

            var want = to - way * PalBeside;
            Vector3 by;

            if (l.Straight)
            {
                var floor = FloorOf(pal);
                if (floor == null) return;

                Footing near = null;
                var nearest = 1f;

                foreach (var p in floor)
                {
                    var gap = Flat(p.At - to);
                    if (gap < 0.8f || gap > 2.5f) continue;

                    var d = Flat(p.At - want);
                    if (d > nearest) continue;
                    if (PedInWay(pal.Who, p.At)) continue;

                    nearest = d;
                    near = p;
                }

                if (near == null) return;
                by = near.At;
            }
            else if (!Spot(want, 0.5f, 1.5f, out by))
            {
                return;
            }

            Loose(pal);
            Walk(pal, by, HeadingTo(by, to), WalkMs);

            pal.Going = by;
            pal.State = Stage.Strolling;
            pal.NextAt = now + WalkMs;
            pal.Pal = l;
            l.Pal = pal;
        }

        /// <summary>
        /// A man still stood where the last teardown left him, taken back into his life rather
        /// than made a second time: written back into the books and walked home -- or, in
        /// bed (stays:), left where he is, which is his mark. A body is left on the list for
        /// Stragglers to take once nobody can see it, and his life waits until it has gone
        /// rather than stand a new man up beside it. See Drop.
        /// </summary>
        private bool Rejoin(Life l, int now)
        {
            for (var i = _stragglers.Count - 1; i >= 0; i--)
            {
                var s = _stragglers[i];
                if (!ReferenceEquals(s.Item, l.Item)) continue;

                var ped = s.Who;

                if (ped == null || !ped.Exists())
                {
                    _stragglers.RemoveAt(i);
                    return false;
                }

                if (!ped.IsAlive)
                {
                    l.NextAt = now + AlongRetryMs;
                    return true;
                }

                _stragglers.RemoveAt(i);

                l.Scene.Made++;
                Register(l.Scene, l.Item, ped, true);
                WhoHeIs(l, ped, now);

                l.Who = ped;
                l.Held = false;
                l.ForTheNight = false;
                l.Since = 0;

                if (l.Stays)
                {
                    // He never walks, and he is already stood on it: Home moves him nowhere.
                    Home(l);
                    l.State = Stage.Marked;
                    l.NextAt = now + Beat(l);
                }
                else if (!GoHome(l, now))
                {
                    // Somebody on his way home: he stands about until it clears. See GoHome.
                    LoiterIdle(l);
                    l.State = Stage.Loitering;
                }

                Log.Debug(Say(l.Item) + " in " + l.Scene.Name + " was still stood there from last time; taken back.");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Whether somebody of his model is already stood on his mark who is not ours: an
        /// orphan left by an earlier session. The build's Twin check, less the stragglers,
        /// who are ours and are taken back by Rejoin.
        /// </summary>
        private bool Squatter(Spooner.Placed item)
        {
            try
            {
                var near = World.GetNearbyPeds(item.At, TwinRange);
                if (near == null) return false;

                foreach (var other in near)
                {
                    if (other == null || !other.Exists()) continue;
                    if (other == Game.Player.Character) continue;
                    if (other.Model.Hash != item.ModelHash) continue;
                    if (Mine(other.Handle)) continue;

                    var straggler = false;

                    foreach (var s in _stragglers)
                    {
                        if (s.Who != null && s.Who.Handle == other.Handle) { straggler = true; break; }
                    }

                    if (straggler) continue;

                    return true;
                }
            }
            catch
            {
                // If the world cannot be asked, build him.
            }

            return false;
        }

        /// <summary>Whether his floor is still unconfirmed under him (see Thawing). The name Cold is the class's.</summary>
        private static bool StillCold(Life l)
        {
            var ped = l.Who;
            if (ped == null) return false;

            foreach (var cold in l.Scene.Frozen)
            {
                if (cold.Who != null && cold.Who.Handle == ped.Handle) return true;
            }

            return false;
        }

        /// <summary>
        /// The men left standing when their scene came down, each deleted once nobody can see
        /// him or he is too far off to be drawn. See Drop.
        /// </summary>
        private static void Stragglers(Vector3 here)
        {
            for (var i = _stragglers.Count - 1; i >= 0; i--)
            {
                var s = _stragglers[i];

                try
                {
                    if (s.Who == null || !s.Who.Exists())
                    {
                        _stragglers.RemoveAt(i);
                        continue;
                    }

                    if (!Unseen(s.Scene, s.Who) && s.Who.Position.DistanceTo(here) <= StragglerFar) continue;

                    s.Who.MarkAsNoLongerNeeded();
                    s.Who.Delete();
                    _stragglers.RemoveAt(i);
                }
                catch
                {
                    _stragglers.RemoveAt(i);
                }
            }
        }

        // ==================================================================
        // Paving
        // ==================================================================

        /// <summary>How far round a slab an old tile of ours counts as being under it.</summary>
        private const float SweepReach = 60f;

        /// <summary>
        /// Floor tiles left behind by an earlier run, cleared before new ones are laid.
        ///
        /// MY MESS. keeps: used to leave the slabs standing and remember them on the Scene
        /// object, which Reload throws away -- so every reload orphaned a slab AND the
        /// sixty-four invisible blocks under it. That is fixed, but the ones already in the
        /// world are not reachable by anything: they are out of scene.Up, and _tiles is a
        /// static that empties every time the script reloads, so even this mod no longer
        /// knows they are its own. Michael found ninety-six of them turning up in a capture.
        ///
        /// So before a slab is paved, anything of OUR OWN TILE MODELS within reach of it that
        /// this run did not lay is deleted. Narrow on purpose: only the four block models the
        /// paver uses, and only round a slab that is about to be paved anyway. A biker block
        /// somebody placed on purpose elsewhere is never touched.
        /// </summary>
        private static void Sweep(Spooner.Placed item)
        {
            try
            {
                var props = World.GetNearbyProps(item.At, SweepReach);
                if (props == null) return;

                var gone = 0;

                foreach (var prop in props)
                {
                    if (prop == null || !prop.Exists()) continue;
                    if (_tiles.Contains(prop.Handle)) continue;

                    var mine = false;

                    foreach (var name in TileNames)
                    {
                        if (unchecked((uint)prop.Model.Hash) != Names.Joaat(name)) continue;
                        mine = true;
                        break;
                    }

                    if (!mine) continue;

                    try { prop.Delete(); gone++; }
                    catch { /* the next pave gets it */ }
                }

                if (gone > 0)
                {
                    Log.Info("Scenery: cleared " + gone + " floor tile(s) left over from an earlier run " +
                             "before paving under " + Say(item) + ".");
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not sweep the old tiles: " + ex.Message);
            }
        }
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

            Sweep(item);

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
                    // EXCLUDING SCRIPT OBJECTS, and it has to be. The plain CREATE_MODEL_HIDE
                    // takes EVERY copy of the model in the sphere, ours included -- the game
                    // ships a separate _EXCLUDING_SCRIPT_OBJECTS variant precisely because the
                    // plain one does not spare them. The take-over key stands a copy of the
                    // bench dead centre in the sphere it hides the map one with, so with the
                    // plain call every build of the scene hid Michael's own bench along with
                    // the old one, and the only benches left to see were the old ones that
                    // had been moved before they were taken over. This hides the map's props
                    // and leaves anything a script put there standing.
                    Function.Call(on ? Hash.CREATE_MODEL_HIDE_EXCLUDING_SCRIPT_OBJECTS : Hash.REMOVE_MODEL_HIDE,
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

        /// <summary>
        /// Take the scene down. FOR GOOD is the whole of the difference between walking away
        /// from the block and being finished with it.
        ///
        /// THE BUG THIS IS: keeps: left the big things standing and remembered them on the
        /// Scene object -- and Reload is Clear() then Load(), which THROWS THE SCENE OBJECTS
        /// AWAY. So every Shift+F9 left eight buildings standing that nothing had a handle on
        /// any more and then built eight more, and RestoreWorld is Clear() too, so every
        /// press of Insert did it again. Michael pressed both all evening on my say-so and
        /// ended up with Parkview several times over.
        ///
        /// So: only the range teardown keeps anything. Anything that ends the scene -- a
        /// reload, the script going away, the setting going off -- takes it all with it.
        /// </summary>
        /// <summary>How near a thing has to be to its mark to BE the thing on that mark.</summary>
        private const float AdoptRange = 0.8f;

        /// <summary>
        /// Whatever is already standing on this mark, taken over instead of built again --
        /// and any second and third copy of it deleted.
        ///
        /// FOR THE MESS ALREADY MADE. The keeps: bug orphaned a building on every reload,
        /// and those are still stood in the world with nothing holding them. They cannot be
        /// cleared by anything that works off scene.Up, because they were taken out of it on
        /// the way past. This finds them by model and mark, keeps one, and deletes the rest,
        /// so the next build of the scene tidies up after me.
        ///
        /// Only for the models the file says to keep. Everything else is made and taken down
        /// in the ordinary way and has never been at risk of this.
        /// </summary>
        private static Entity Adopt(Scene scene, Spooner.Placed item)
        {
            if (scene.Keeps.Count == 0 && scene.KeepModels.Count == 0) return null;

            var mine = scene.Keeps.Contains(item.Handle) ||
                       scene.KeepModels.Contains(unchecked((uint)item.ModelHash));

            if (!mine) return null;

            try
            {
                var near = World.GetNearbyProps(item.At, AdoptRange);
                if (near == null) return null;

                Entity first = null;
                var extra = 0;

                foreach (var prop in near)
                {
                    if (prop == null || !prop.Exists()) continue;
                    if (unchecked((uint)prop.Model.Hash) != unchecked((uint)item.ModelHash)) continue;

                    if (first == null) { first = prop; continue; }

                    try { prop.Delete(); extra++; }
                    catch { /* the next build gets it */ }
                }

                if (first == null) return null;

                Log.Info("Scenery: " + Say(item) + " was already standing; taken over" +
                         (extra > 0 ? " and " + extra + " spare copy(s) of it deleted." : "."));

                return first;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not look for one already standing: " + ex.Message);
                return null;
            }
        }
        private static void Drop(Scene scene, bool forGood)
        {
            Vanish(scene, false);
            Unbare(scene);

            scene.Kept.Clear();
            scene.KeptTiles.Clear();

            var keeping = !forGood && (scene.Keeps.Count > 0 || scene.KeepModels.Count > 0);
            var kept = 0;

            foreach (var e in scene.Up)
            {
                try
                {
                    if (e == null) continue;

                    // STILL IN VIEW AS IT COMES DOWN. The teardown is three hundred metres
                    // off, but a man is drawn from up to five hundred, so he can be on screen
                    // for it: he is left standing and goes once nobody is looking, or is
                    // taken back into his life if the scene is built again first. See
                    // Stragglers and Rejoin. A body too: one you are looking at through a
                    // scope from three hundred metres does not vanish either. Not for good:
                    // a reload or the script going away takes everybody, in view or not,
                    // because the player or the engine forced it.
                    var man = e as Ped;

                    if (!forGood && man != null && man.Exists() && !Unseen(scene, man))
                    {
                        Spooner.Placed who;
                        scene.Was.TryGetValue(man.Handle, out who);

                        _stragglers.Add(new Straggler { Scene = scene, Item = who, Who = man });
                        kept++;
                        continue;
                    }

                    if (keeping && e.Exists())
                    {
                        // WHAT THE FILE SAID TO KEEP. Held by its SAVED handle, off Was --
                        // the runtime handle means nothing next session and this has to
                        // survive into the next build to be any use.
                        Spooner.Placed was;
                        var known = scene.Was.TryGetValue(e.Handle, out was) && was != null;
                        var saved = known ? was.Handle : 0;

                        var byName = known && scene.KeepModels.Count > 0 &&
                                     scene.KeepModels.Contains(unchecked((uint)was.ModelHash));

                        if (saved != 0 && (scene.Keeps.Contains(saved) || byName))
                        {
                            scene.Kept[saved] = e;
                            continue;
                        }

                        // AND THE FLOOR WITH IT. A tile is nobody's placement -- it has no
                        // saved handle of its own -- so it cannot be named in keeps:, and a
                        // kept slab whose tiles were deleted is men falling through a block.
                        if (_tiles.Contains(e.Handle))
                        {
                            scene.KeptTiles.Add(e);
                            continue;
                        }
                    }

                    _tiles.Remove(e.Handle);
                    if (e.Exists()) e.Delete();
                }
                catch
                {
                    // Already gone.
                }
            }

            if (kept > 0)
            {
                Log.Info("Scenery: " + kept + " of \"" + scene.Name + "\" still in view as it comes down; " +
                         "each goes once nobody is looking.");
            }

            scene.Up.Clear();
            scene.ByHandle.Clear();
            scene.Was.Clear();
            scene.Solid.Clear();
            scene.Waits.Clear();
            scene.Frozen.Clear();
            scene.Paved.Clear();
            scene.PedsGo = false;
            scene.PedsBy = 0;
            scene.Cursor = 0;
            scene.Waited = 0;
            scene.Working = false;
            scene.Built = false;
        }

        public void Clear()
        {
            foreach (var scene in _scenes) Drop(scene, true);
            _lives.Clear();

            // The player or the engine forced this, so the stragglers go where they stand too.
            foreach (var s in _stragglers)
            {
                try
                {
                    if (s.Who != null && s.Who.Exists())
                    {
                        s.Who.MarkAsNoLongerNeeded();
                        s.Who.Delete();
                    }
                }
                catch
                {
                    // Already gone.
                }
            }

            _stragglers.Clear();
        }

        public void RestoreWorld()
        {
            Clear();
        }

        // ==================================================================
        // Struck
        // ==================================================================

        /// <summary>The list of what the hide key has taken out of our scenes. See Strike.</summary>
        private static string StruckPath => Path.Combine(Paths.ParkviewScenery, "struck.txt");

        /// <summary>How close to where it was struck a placement has to be to be the one struck.</summary>
        private const float StruckSame = 0.3f;

        /// <summary>How near the line you are looking along one of ours has to be to be the one you mean.</summary>
        private const float AimWidth = 0.35f;

        /// <summary>
        /// ONE OF OURS, OUT FOR GOOD, with the hide key: the thing itself, its placement, and a
        /// line in struck.txt so it is never built again -- not after a restart, and not after a
        /// deploy writes the scene file over, because struck.txt is not a file anything ships.
        ///
        /// Menyoo can delete one of ours from in front of you, but only until the scene is next
        /// built, and nothing it does reaches the scene file. Michael deleted things in the den
        /// on 2026-09-26 and they were all stood there again the next time he walked in.
        /// </summary>
        public bool Strike(int handle, out string what)
        {
            what = "";
            if (handle == 0) return false;

            foreach (var scene in _scenes)
            {
                Spooner.Placed item;
                if (!scene.Was.TryGetValue(handle, out item) || item == null) continue;

                if (item.What != Spooner.Kind.Prop) return false;

                what = Say(item);

                // Off the books first, so nothing tidies it, adopts it or builds it again.
                scene.Was.Remove(handle);
                scene.Solid.Remove(handle);

                for (var i = scene.Up.Count - 1; i >= 0; i--)
                {
                    if (scene.Up[i] != null && scene.Up[i].Handle == handle) scene.Up.RemoveAt(i);
                }

                Entity by;
                if (item.Handle != 0 && scene.ByHandle.TryGetValue(item.Handle, out by) && by != null && by.Handle == handle)
                {
                    scene.ByHandle.Remove(item.Handle);
                }

                // Out of the list the builder walks. The builder's place in it moves back with it,
                // so a scene part-way up does not skip the next one along.
                var at = scene.Items.IndexOf(item);

                if (at >= 0)
                {
                    scene.Items.RemoveAt(at);
                    if (at < scene.Cursor) scene.Cursor--;
                }

                try
                {
                    var e = Entity.FromHandle(handle);
                    if (e != null && e.Exists()) e.Delete();
                }
                catch
                {
                    // Already gone.
                }

                try
                {
                    var fresh = !File.Exists(StruckPath);

                    using (var w = new StreamWriter(StruckPath, true))
                    {
                        if (fresh)
                        {
                            w.WriteLine("# What the hide key has taken out of Parkview's own scenes, never built again.");
                            w.WriteLine("# scene | saved handle | model | model hash | x | y | z");
                            w.WriteLine("# Delete a line to have that one back.");
                        }

                        var c = System.Globalization.CultureInfo.InvariantCulture;

                        w.WriteLine(scene.Name + " | " + item.Handle + " | " + what + " | 0x" +
                                    unchecked((uint)item.ModelHash).ToString("x8") + " | " +
                                    item.At.X.ToString("0.000", c) + " | " + item.At.Y.ToString("0.000", c) + " | " +
                                    item.At.Z.ToString("0.000", c));
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("Could not write struck.txt: " + ex.Message + ". " + what + " is gone until the scene is built again.");
                }

                Log.Info("Struck from \"" + scene.Name + "\" for good: " + what + " (saved handle " + item.Handle +
                         ") at " + item.At + ". In struck.txt; take the line out to have it back.");

                what = what + " in " + scene.Name;
                return true;
            }

            return false;
        }

        /// <summary>What one of ours is called, for asking before it goes. Null for anything not placed by a scene.</summary>
        public string Named(int handle)
        {
            foreach (var scene in _scenes)
            {
                Spooner.Placed item;
                if (scene.Was.TryGetValue(handle, out item) && item != null && item.What == Spooner.Kind.Prop) return Say(item);
            }

            return null;
        }

        /// <summary>
        /// The one of ours nearest the line you are looking along, within a hand of it and no
        /// further off than the ray got. For what has no collision to be hit -- a neon, a line of
        /// powder, litter -- which a ray goes straight through. 0 when there is none.
        /// </summary>
        public int Aimed(Vector3 eye, Vector3 dir, float reach)
        {
            var best = 0;
            var bestOff = AimWidth;

            foreach (var scene in _scenes)
            {
                foreach (var pair in scene.Was)
                {
                    if (pair.Value == null || pair.Value.What != Spooner.Kind.Prop) continue;

                    Vector3 middle;

                    try
                    {
                        var e = Entity.FromHandle(pair.Key);
                        if (e == null || !e.Exists()) continue;

                        var lo = new OutputArgument();
                        var hi = new OutputArgument();
                        Function.Call(Hash.GET_MODEL_DIMENSIONS, e.Model.Hash, lo, hi);
                        middle = e.GetOffsetPosition((lo.GetResult<Vector3>() + hi.GetResult<Vector3>()) * 0.5f);
                    }
                    catch
                    {
                        continue;
                    }

                    var to = middle - eye;
                    var t = Vector3.Dot(to, dir);
                    if (t < 0.3f || t > reach) continue;

                    var off = (to - dir * t).Length();
                    if (off >= bestOff) continue;

                    bestOff = off;
                    best = pair.Key;
                }
            }

            return best;
        }

        /// <summary>A scene's placements with what struck.txt names taken out. See Strike.</summary>
        private static void Unstruck(string scene, List<Spooner.Placed> items)
        {
            try
            {
                if (!File.Exists(StruckPath)) return;

                var c = System.Globalization.CultureInfo.InvariantCulture;
                var taken = 0;

                foreach (var raw in File.ReadAllLines(StruckPath))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;

                    var part = line.Split('|');
                    if (part.Length < 7) continue;
                    if (!string.Equals(part[0].Trim(), scene, StringComparison.OrdinalIgnoreCase)) continue;

                    uint hash;
                    var hex = part[3].Trim();
                    if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex.Substring(2);
                    if (!uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, c, out hash)) continue;

                    float x, y, z;
                    if (!float.TryParse(part[4].Trim(), System.Globalization.NumberStyles.Float, c, out x) ||
                        !float.TryParse(part[5].Trim(), System.Globalization.NumberStyles.Float, c, out y) ||
                        !float.TryParse(part[6].Trim(), System.Globalization.NumberStyles.Float, c, out z)) continue;

                    var at = new Vector3(x, y, z);

                    for (var i = items.Count - 1; i >= 0; i--)
                    {
                        var one = items[i];
                        if (unchecked((uint)one.ModelHash) != hash || one.At.DistanceTo(at) > StruckSame) continue;

                        items.RemoveAt(i);
                        taken++;
                    }
                }

                if (taken > 0) Log.Info("Scenery: " + taken + " of \"" + scene + "\" struck with the hide key and left out. See struck.txt.");
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read struck.txt: " + ex.Message);
            }
        }

        // ==================================================================
        // Bare
        // ==================================================================

        /// <summary>How often a bare room is looked over for the map's props: often, because it is made as you walk in.</summary>
        private const int BareEveryMs = 250;

        /// <summary>How far above or below the scene's middle a prop can be and still be in the room.</summary>
        private const float BareHigh = 4.5f;

        /// <summary>The sphere each one is hidden with: its own spot, and nothing of the same model a step away.</summary>
        private const float BareSpot = 0.25f;

        /// <summary>Anything this big one way may be the room itself -- a wall, a floor -- and is left.</summary>
        private const float BareBiggest = 6f;

        /// <summary>"near: 12" in a scene's Note: how near he has to be for it to go up. 0 when it says nothing.</summary>
        private static float NearReach(string path)
        {
            foreach (var line in Clauses(path))
            {
                if (!line.StartsWith("near:", StringComparison.OrdinalIgnoreCase)) continue;

                float reach;
                if (float.TryParse(line.Substring(5).Trim(), System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out reach) && reach > 0f)
                {
                    return reach;
                }
            }

            return 0f;
        }

        /// <summary>"bare" in a scene's Note, as a reach: the number after it, or a little past the scene's own edge.</summary>
        private static float BareReach(string path, float radius)
        {
            foreach (var line in Clauses(path))
            {
                if (!line.StartsWith("bare", StringComparison.OrdinalIgnoreCase)) continue;

                var colon = line.IndexOf(':');
                float reach;

                if (colon > 0 && float.TryParse(line.Substring(colon + 1).Trim(), System.Globalization.NumberStyles.Float,
                                                 System.Globalization.CultureInfo.InvariantCulture, out reach) && reach > 0f)
                {
                    return reach;
                }

                return radius + 2f;
            }

            return 0f;
        }

        /// <summary>
        /// EVERYTHING THE MAP PUT IN THE ROOM, OUT, while the scene is up. For a room furnished
        /// from nothing: the den is the story game's two-car garage, and the garage's own things
        /// stood in among Michael's tables. He deleted them in Menyoo and they were back every
        /// time the room was loaded again, because the interior makes them again -- so each one
        /// is model-hidden on its own spot, which the game holds to, and let back when the scene
        /// comes down. Michael asked for just ours in the den on 2026-09-26.
        ///
        /// Anything a script made is left: ours, the dealers' cards and chips, the reels,
        /// Menyoo's own. So is anything held -- the joint a scenario puts in a man's hand is the
        /// game's, and hiding it would hide every one after it on that spot -- any door, so the
        /// room is never open onto the void behind it, and anything big enough to be the room.
        /// </summary>
        private void Bare(Scene scene)
        {
            try
            {
                var near = World.GetNearbyProps(scene.Centre, scene.Bare);
                if (near == null) return;

                foreach (var prop in near)
                {
                    if (prop == null || !prop.Exists()) continue;

                    var handle = prop.Handle;
                    if (Function.Call<bool>(Hash.IS_ENTITY_A_MISSION_ENTITY, handle)) continue;
                    if (Mine(handle)) continue;
                    if (Function.Call<bool>(Hash.IS_ENTITY_ATTACHED, handle)) continue;

                    var at = prop.Position;
                    if (Math.Abs(at.Z - scene.Centre.Z) > BareHigh) continue;

                    var hash = unchecked((uint)prop.Model.Hash);
                    var key = hash.ToString("x8") + "@" + at.X.ToString("0.0") + "," + at.Y.ToString("0.0") + "," + at.Z.ToString("0.0");

                    if (scene.BareLeft.Contains(key)) continue;

                    var already = false;

                    foreach (var one in scene.Bared)
                    {
                        if (one.ModelHash == hash && one.At.DistanceTo(at) < 0.3f) { already = true; break; }
                    }

                    if (already) continue;

                    var name = Names.Say(prop.Model.Hash);

                    if (name.IndexOf("door", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("gate", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        scene.BareLeft.Add(key);
                        Log.Info("Scenery: \"" + scene.Name + "\" is bare but the map's " + name + " at " + at + " is left -- a door.");
                        continue;
                    }

                    var lo = new OutputArgument();
                    var hi = new OutputArgument();
                    Function.Call(Hash.GET_MODEL_DIMENSIONS, prop.Model.Hash, lo, hi);
                    var size = hi.GetResult<Vector3>() - lo.GetResult<Vector3>();

                    if (size.X > BareBiggest || size.Y > BareBiggest || size.Z > BareBiggest)
                    {
                        scene.BareLeft.Add(key);
                        Log.Info("Scenery: \"" + scene.Name + "\" is bare but the map's " + name + " at " + at + " is left -- " +
                                 size.X.ToString("0.0") + " x " + size.Y.ToString("0.0") + " x " + size.Z.ToString("0.0") +
                                 " m may be the room itself.");
                        continue;
                    }

                    Function.Call(Hash.CREATE_MODEL_HIDE_EXCLUDING_SCRIPT_OBJECTS, at.X, at.Y, at.Z, BareSpot, unchecked((int)hash), false);

                    scene.Bared.Add(new Spooner.Hidden { ModelName = name, ModelHash = hash, At = at, Radius = BareSpot });
                    Log.Info("Scenery: the map's " + name + " at " + at + " is out of \"" + scene.Name + "\" -- it is bare.");
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Scenery: could not clear the map's props out of " + scene.Name + ": " + ex.Message);
            }
        }

        /// <summary>The map's props let back into a bare room as it comes down.</summary>
        private static void Unbare(Scene scene)
        {
            foreach (var one in scene.Bared)
            {
                try
                {
                    Function.Call(Hash.REMOVE_MODEL_HIDE, one.At.X, one.At.Y, one.At.Z, one.Radius, unchecked((int)one.ModelHash), false);
                }
                catch
                {
                    // It is only a hole left open.
                }
            }

            scene.Bared.Clear();
            scene.BareLeft.Clear();
            scene.BareNext = 0;
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
