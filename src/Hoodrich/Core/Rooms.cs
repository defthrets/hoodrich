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

        public static void Start()
        {
            Found.Clear();
            _at = 0;
            _asked = 0;
            _running = true;

            Log.Info("Room sweep: asking about " + Total + " points under the map.");
        }

        /// <summary>One slice per tick, from wherever this is ticked.</summary>
        public static void Update()
        {
            if (!_running) return;

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
