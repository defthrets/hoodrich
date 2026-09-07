using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GTA;
using GTA.Math;
using GTA.Native;

namespace Hoodrich.Core
{
    /// <summary>
    /// Sweeping the map under the map for interiors, and writing down where they are.
    ///
    /// WHY THIS HAD TO BE BUILT. The grow room's coordinate was measured in game, written into
    /// the ini, and worked. Then it stopped, and every explanation offered from the outside
    /// was wrong in turn: the coordinate (right), the IPL name (wrong, but fixing it changed
    /// nothing), the affiliation gate (a different bug). The one question nobody could answer
    /// was the simplest -- IS THERE AN INTERIOR THERE AT ALL -- because the only thing that
    /// knows is the running game, and it will only answer about one coordinate at a time.
    ///
    /// So it is asked about all of them. GET_INTERIOR_AT_COORDS is free, needs nobody standing
    /// anywhere near, and answers truthfully; a ten metre grid across the belt of empty space
    /// the business interiors are buried in is thirty thousand of those questions, which is
    /// about a second spread over a hundred frames. Every interior it finds is asked for its
    /// own origin, so what comes out is not "something is near here" but the exact coordinate
    /// to put in the ini.
    ///
    /// It writes rooms.txt beside the log: one line per interior, sorted, with its id and its
    /// origin. That file is the answer to a question that has cost this project three rounds
    /// of guessing.
    /// </summary>
    internal static class Rooms
    {
        /// <summary>
        /// The belt the shells are buried in.
        ///
        /// Every DLC business interior in the game sits in the same stretch of nothing under
        /// the sea south-east of the airport, between about forty metres down and twenty. The
        /// box is deliberately wider than the known cluster: the whole point is to stop
        /// assuming where things are.
        /// </summary>
        private const float FromX = 700f, ToX = 1500f;
        private const float FromY = -3500f, ToY = -2800f;
        private const float Step = 10f;

        private static readonly float[] Depths = { -45f, -40f, -35f, -30f, -25f, -20f };

        /// <summary>How many points are asked per tick. The question is cheap; the frame is not.</summary>
        private const int PerTick = 600;

        private static bool _running;
        private static int _at;
        private static int _asked;
        private static readonly Dictionary<int, Vector3> Found = new Dictionary<int, Vector3>();

        /// <summary>
        /// THE IPLS GO IN FIRST, and the last sweep is the reason why.
        ///
        /// It asked 34,506 points and found nothing at all, which was read as "the business
        /// interiors are not in this build". That reading cannot be made from that test. A DLC
        /// interior is not in the map until its own IPL has been asked for, and
        /// GET_INTERIOR_AT_COORDS answers about the map -- so a room that is present but not
        /// requested and a room that does not exist give the same answer, zero, and the sweep
        /// could not tell them apart.
        ///
        /// So every ipl any door in the mod names is requested, the sweep waits for them to go
        /// active, and rooms.txt says which ones did. Nothing found AFTER that is a real
        /// nothing.
        /// </summary>
        private static readonly List<string> Ipls = new List<string>();
        private static readonly Dictionary<string, bool> IplOn = new Dictionary<string, bool>();

        /// <summary>The doors' own inside marks, asked about by name at the end.</summary>
        private static readonly List<KeyValuePair<string, Vector3>> Marks =
            new List<KeyValuePair<string, Vector3>>();

        /// <summary>0 asking for the ipls, 1 sweeping.</summary>
        private static int _stage;
        private static int _asks;
        private static int _waitUntil;

        /// <summary>How long the ipls get to arrive before the sweep goes ahead without them.</summary>
        private const int IplWaitMs = 6000;

        public static bool Running => _running;

        /// <summary>How many points there are to ask about, worked out rather than counted.</summary>
        private static int Total
        {
            get
            {
                var wide = (int)((ToX - FromX) / Step) + 1;
                var deep = (int)((ToY - FromY) / Step) + 1;

                return wide * deep * Depths.Length;
            }
        }

        public static void Start(IEnumerable<string> ipls,
                                 IEnumerable<KeyValuePair<string, Vector3>> marks)
        {
            Found.Clear();
            Ipls.Clear();
            IplOn.Clear();
            Marks.Clear();

            if (ipls != null)
            {
                foreach (var one in ipls)
                {
                    if (string.IsNullOrEmpty(one) || Ipls.Contains(one)) continue;
                    Ipls.Add(one);
                }
            }

            if (marks != null) Marks.AddRange(marks);

            _at = 0;
            _asked = 0;
            _asks = 0;
            _stage = 0;
            _waitUntil = 0;
            _running = true;

            Log.Info("Room sweep: " + Ipls.Count + " ipl(s) to ask for, then " + Total +
                     " points under the map.");
        }

        /// <summary>
        /// Ask for every ipl, then wait for them.
        ///
        /// Requested once and then only watched -- REQUEST_IPL every frame for six seconds is
        /// the same ask made three hundred times, and it does not arrive any sooner for it.
        /// </summary>
        private static void Asking()
        {
            var now = Game.GameTime;

            if (_waitUntil == 0)
            {
                foreach (var one in Ipls)
                {
                    try
                    {
                        Function.Call(Hash.REQUEST_IPL, one);
                        _asks++;
                    }
                    catch
                    {
                        // A name the game does not know is not an error, it is an answer.
                    }
                }

                _waitUntil = now + IplWaitMs;
                return;
            }

            var waiting = false;

            foreach (var one in Ipls)
            {
                bool on;

                try { on = Function.Call<bool>(Hash.IS_IPL_ACTIVE, one); }
                catch { on = false; }

                IplOn[one] = on;
                if (!on) waiting = true;
            }

            if (waiting && now < _waitUntil) return;

            var live = 0;
            foreach (var pair in IplOn) if (pair.Value) live++;

            Log.Info("Room sweep: " + live + " of " + Ipls.Count + " ipl(s) active" +
                     (waiting ? " (gave up waiting for the rest)" : "") + "; sweeping now.");

            _stage = 1;
        }

        /// <summary>One slice per tick, from wherever this is ticked.</summary>
        public static void Update()
        {
            if (!_running) return;

            if (_stage == 0)
            {
                Asking();
                return;
            }

            var wide = (int)((ToX - FromX) / Step) + 1;
            var deep = (int)((ToY - FromY) / Step) + 1;
            var stop = Math.Min(_at + PerTick, Total);

            for (; _at < stop; _at++)
            {
                // One running number unpicked into three, so the sweep can stop and start
                // anywhere without carrying three counters across ticks.
                var i = _at;
                var z = Depths[i / (wide * deep)];
                var rest = i % (wide * deep);
                var x = FromX + (rest % wide) * Step;
                var y = FromY + (rest / wide) * Step;

                try
                {
                    var interior = Function.Call<int>(Hash.GET_INTERIOR_AT_COORDS, x, y, z);
                    _asked++;

                    if (interior == 0 || Found.ContainsKey(interior)) continue;

                    var origin = Function.Call<Vector3>(Hash.GET_OFFSET_FROM_INTERIOR_IN_WORLD_COORDS,
                                                        interior, 0f, 0f, 0f);

                    Found[interior] = origin == Vector3.Zero ? new Vector3(x, y, z) : origin;

                    Log.Info("Room sweep: interior " + interior + " at " + Found[interior] +
                             " (found from " + x.ToString("0") + ", " + y.ToString("0") + ", " +
                             z.ToString("0") + ").");
                }
                catch
                {
                    // One dead point costs nothing.
                }
            }

            if (_at < Total) return;

            _running = false;
            Write();
        }

        private static void Write()
        {
            var sb = new StringBuilder();

            sb.Append("INTERIORS UNDER THE MAP\r\n");
            sb.Append("=======================\r\n\r\n");
            sb.Append("Swept X ").Append(FromX).Append(" to ").Append(ToX)
              .Append(", Y ").Append(FromY).Append(" to ").Append(ToY)
              .Append(", at ").Append(Depths.Length).Append(" depths, every ").Append(Step).Append(" m.\r\n");
            sb.Append("Points asked: ").Append(_asked).Append("\r\n");
            sb.Append("Interiors found: ").Append(Found.Count).Append("\r\n\r\n");

            // WHICH IPLS WENT IN. Without this the count above cannot be read: nothing found
            // with nothing loaded means nothing, and nothing found with all of them loaded is
            // a real answer about this build of the game.
            sb.Append("IPLS ASKED FOR\r\n");
            sb.Append("--------------\r\n");

            if (Ipls.Count == 0)
            {
                sb.Append("none -- swept the raw map\r\n");
            }
            else
            {
                foreach (var one in Ipls)
                {
                    bool on;
                    sb.Append(IplOn.TryGetValue(one, out on) && on ? "  ACTIVE   " : "  not on   ")
                      .Append(one).Append("\r\n");
                }
            }

            sb.Append("\r\n");

            // AND THE DOORS' OWN MARKS, ASKED ABOUT DIRECTLY. The sweep is a grid and a grid
            // can step over a small room; this asks the exact coordinate every door in the mod
            // will teleport somebody to, which is the question that actually matters.
            sb.Append("WHERE THE DOORS GO\r\n");
            sb.Append("------------------\r\n");

            foreach (var mark in Marks)
            {
                var at = mark.Value;

                var interior = 0;
                var valid = false;
                var ready = false;

                try
                {
                    interior = Function.Call<int>(Hash.GET_INTERIOR_AT_COORDS, at.X, at.Y, at.Z);

                    if (interior != 0)
                    {
                        valid = Function.Call<bool>(Hash.IS_VALID_INTERIOR, interior);
                        ready = Function.Call<bool>(Hash.IS_INTERIOR_READY, interior);
                    }
                }
                catch
                {
                    // Reported as whatever it got to.
                }

                sb.Append("  ").Append(mark.Key).Append("\r\n");
                sb.Append("    at        ").Append(at.X.ToString("0.000")).Append(", ")
                  .Append(at.Y.ToString("0.000")).Append(", ").Append(at.Z.ToString("0.000")).Append("\r\n");
                sb.Append("    interior  ").Append(interior == 0 ? "NONE -- there is nothing there" : interior.ToString())
                  .Append("\r\n");

                if (interior != 0)
                {
                    sb.Append("    valid     ").Append(valid).Append("\r\n");
                    sb.Append("    ready     ").Append(ready).Append("\r\n");
                }

                sb.Append("\r\n");
            }

            sb.Append("\r\n");

            if (Found.Count == 0)
            {
                sb.Append("NOTHING AT ALL. Either the business interiors are not in this build of\r\n");
                sb.Append("the game, or they are not in this box, or they only exist once their own\r\n");
                sb.Append("IPL is active and none of the names asked for are right.\r\n");
            }
            else
            {
                foreach (var pair in Found)
                {
                    sb.Append("interior ").Append(pair.Key).Append("  at  ")
                      .Append(pair.Value.X.ToString("0.000")).Append(", ")
                      .Append(pair.Value.Y.ToString("0.000")).Append(", ")
                      .Append(pair.Value.Z.ToString("0.000")).Append("\r\n");
                }
            }

            try
            {
                var path = Path.Combine(Paths.Writable, "rooms.txt");
                System.IO.File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));

                Log.Info("Room sweep: " + Found.Count + " interior(s) written to " + path + ".");
                UI.Notify.Important(Found.Count + " room(s) found. Written to rooms.txt.");
            }
            catch (Exception ex)
            {
                Log.Warn("Could not write the room list: " + ex.Message);
                UI.Notify.Important(Found.Count + " room(s) found. See the log.");
            }
        }
    }
}
