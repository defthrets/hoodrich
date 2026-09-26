using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.UI;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.Gangs
{
    /// <summary>
    /// The way the riders go round Parkview, ridden by Michael rather than worked out.
    ///
    /// WHY IT EXISTS. The park survey (see Park) looks the ground over and hands a rider only
    /// lines it has checked clear, and with Parkview built up round the courts it keeps finding
    /// two or three dozen places to ride out of four hundred -- five and six hundred placed
    /// things inside seventy metres, every fence, bench, bin, ramp and block. On a map that
    /// small a crew shuttles between the same few spots, stalls, and rides out. Michael said the
    /// bikes were not working and asked on 2026-09-24 for a setting to record the path himself.
    ///
    /// HOW. Settings, The block, "Record the riders' path". Ride it -- a BMX is best, but on
    /// foot or in a car records just the same -- and stop it on the same row, with the settings
    /// open or shut: it records whatever is on screen (see Main.OnTick). A point is kept every
    /// metre of ground covered. Finish near where you started and it is a loop they
    /// ride round; finish anywhere else and they ride it there and back. It is written to
    /// bike-path.txt beside the log, a point a line, so it can be read and put right by hand.
    ///
    /// TRUSTED, NOT CHECKED. Every metre of it was ridden, so they ride it: over the ramp if
    /// that is where it went, down the kerb if that is where it went. They still brake for
    /// people and anything moving -- see Rollers.RidePath.
    /// </summary>
    internal static class BikePath
    {
        /// <summary>
        /// A point every metre of ground covered while recording. EVERY METRE, NOT EVERY THREE:
        /// Michael wants his track copied exactly, and a bend taken at three-metre steps is a
        /// polygon. The riders do not pay for it -- a straight is still handed over as one long
        /// line (see Ahead) -- and a lap of the whole park is a few hundred lines of text.
        /// </summary>
        private const float Spacing = 1f;

        /// <summary>Finish within this of the start and it is a loop.</summary>
        private const float LoopGap = 15f;

        /// <summary>Further than this between two looks is a teleport, not riding.</summary>
        private const float Jump = 25f;

        /// <summary>Fewer points than this, or shorter than MinLength, and it is not a path.</summary>
        public const int MinPoints = 6;
        private const float MinLength = 30f;

        /// <summary>How near a point a destination has to be to BE that point. See On.</summary>
        private const float OnPoint = 1.5f;

        /// <summary>How far round the player the path is drawn, and how much of it at most.</summary>
        private const float DrawReach = 150f;
        private const int DrawMost = 1200;

        /// <summary>The path the riders ride. Empty until one is recorded.</summary>
        public static readonly List<Vector3> Points = new List<Vector3>();

        /// <summary>Whether the last point joins the first.</summary>
        public static bool Loop { get; private set; }

        /// <summary>Goes up every time the path changes, so a rider on the old one knows to look again.</summary>
        public static int Version { get; private set; }

        public static bool Recording { get; private set; }

        /// <summary>Drawn on the ground while this is on. Not saved: see the settings row.</summary>
        public static bool Showing;

        /// <summary>A path to ride. Not while a new one is being ridden -- that one is not finished.</summary>
        public static bool Ready => Points.Count >= MinPoints;

        /// <summary>Where it starts: the first point he rode. Where the riders get on.</summary>
        public static Vector3 First => Points.Count > 0 ? Points[0] : Vector3.Zero;

        /// <summary>How long it is, closing leg and all.</summary>
        public static float Metres => _length;

        private static float _length;
        private static bool _loaded;

        /// <summary>The one being recorded. The riders keep the old one until this is saved.</summary>
        private static readonly List<Vector3> _taking = new List<Vector3>();
        private static float _takingLength;

        // ---- the file ---------------------------------------------------------------------

        public static void Load()
        {
            if (_loaded) return;
            _loaded = true;

            try
            {
                // Your own recording if there is one, and the one that ships if not.
                var file = File.Exists(Paths.BikePathFile) ? Paths.BikePathFile : Paths.BikePathShipped;
                if (!File.Exists(file)) return;

                var pts = new List<Vector3>();
                var loop = false;
                var c = CultureInfo.InvariantCulture;

                foreach (var raw in File.ReadAllLines(file))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;

                    if (line.Equals("loop", StringComparison.OrdinalIgnoreCase))
                    {
                        loop = true;
                        continue;
                    }

                    var parts = line.Split(',');
                    if (parts.Length < 3) continue;

                    float x, y, z;
                    if (!float.TryParse(parts[0].Trim(), NumberStyles.Float, c, out x)) continue;
                    if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, c, out y)) continue;
                    if (!float.TryParse(parts[2].Trim(), NumberStyles.Float, c, out z)) continue;

                    pts.Add(new Vector3(x, y, z));
                }

                if (pts.Count < MinPoints)
                {
                    Log.Info("Rollers: bike-path.txt has only " + pts.Count + " points; the riders find their own way round the park.");
                    return;
                }

                Set(pts, loop);
                Log.Info("Rollers: the riders' path is loaded -- " + Describe() + ".");
            }
            catch (Exception ex)
            {
                Log.Warn("Rollers: could not read the riders' path: " + ex.Message);
            }
        }

        private static bool Save()
        {
            try
            {
                var c = CultureInfo.InvariantCulture;
                var sb = new StringBuilder();

                sb.AppendLine("# Hoodrich -- the way the riders go round the park. Recorded in the game:");
                sb.AppendLine("# Settings, The block, Record the riders' path. One point a line, X, Y, Z.");
                sb.AppendLine("# \"loop\" on a line of its own means the last point joins the first;");
                sb.AppendLine("# without it they ride to the end and back.");

                if (Loop) sb.AppendLine("loop");

                foreach (var p in Points)
                {
                    sb.AppendLine(string.Format(c, "{0:0.00}, {1:0.00}, {2:0.00}", p.X, p.Y, p.Z));
                }

                File.WriteAllText(Paths.BikePathFile, sb.ToString());
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("Rollers: could not write the riders' path: " + ex.Message);
                return false;
            }
        }

        private static void Set(List<Vector3> pts, bool loop)
        {
            Points.Clear();
            Points.AddRange(pts);
            Loop = loop && pts.Count > 2;
            _length = Length(Points, Loop);
            Version++;
        }

        // ---- recording --------------------------------------------------------------------

        public static void Start()
        {
            _taking.Clear();
            _takingLength = 0f;
            Recording = true;

            Take();

            Log.Info("Rollers: recording the riders' path.");
            Notify.Important("~g~Recording the riders' path.~s~ Ride it -- a BMX is best -- then stop it on the same row. " +
                             "Finish where you started for a loop.");
        }

        /// <summary>Stops recording and keeps it, if there is enough of it. What happened, in a sentence.</summary>
        public static string Stop()
        {
            if (!Recording) return "Nothing was being recorded.";

            Recording = false;

            var pts = new List<Vector3>(_taking);
            _taking.Clear();

            if (pts.Count < MinPoints || Length(pts, false) < MinLength)
            {
                Log.Info("Rollers: the riders' path was too short to keep (" + pts.Count + " points).");
                return "Too short to ride -- nothing kept. Ride at least " + MinLength.ToString("0") + " m.";
            }

            var gap = Flat(pts[pts.Count - 1], pts[0]);
            var loop = gap <= LoopGap;

            // Stopped right on top of the start: that last point is the start again, and the
            // closing line from it would be nothing.
            if (loop && gap < Spacing * 0.5f) pts.RemoveAt(pts.Count - 1);

            Set(pts, loop);

            var saved = Save();
            Log.Info("Rollers: the riders' path recorded -- " + Describe() +
                     (saved ? ", written to " + Paths.BikePathFile + "." : ". It could not be written, so it lasts this session."));

            return (saved ? "Saved: " : "Kept for this session only: ") + Describe() + ".";
        }

        /// <summary>Takes the path away, file and all. The riders go back to finding their own way.</summary>
        public static string Forget()
        {
            Recording = false;
            _taking.Clear();

            var had = Points.Count > 0;

            Points.Clear();
            Loop = false;
            _length = 0f;
            Version++;

            try
            {
                if (File.Exists(Paths.BikePathFile)) File.Delete(Paths.BikePathFile);
            }
            catch (Exception ex)
            {
                Log.Warn("Rollers: could not delete the riders' path: " + ex.Message);
            }

            Log.Info("Rollers: the riders' path was forgotten.");
            return had ? "Forgotten. The riders find their own way round the park again." : "There was no path to forget.";
        }

        public static string Describe()
        {
            if (Recording)
            {
                return "recording: " + _taking.Count + " points, " + _takingLength.ToString("0") + " m";
            }

            if (Points.Count == 0) return "none yet -- they find their own way";

            return Points.Count + " points, " + _length.ToString("0") + " m, " + (Loop ? "a loop" : "there and back");
        }

        /// <summary>Every frame: a point when he has moved far enough, and the path drawn.</summary>
        public static void Tick()
        {
            if (!_loaded) Load();

            try
            {
                if (Recording)
                {
                    Take();

                    // Stop can run from inside Take, on a teleport.
                    if (Recording)
                    {
                        Draw(_taking, false, true);
                        Hint();
                    }

                    return;
                }

                if (Showing && Points.Count > 1) Draw(Points, Loop, false);
            }
            catch (Exception ex)
            {
                Log.Debug("Rollers: the path recorder tripped: " + ex.Message);
            }
        }

        private static void Take()
        {
            var me = Game.Player.Character;
            if (me == null || !me.Exists()) return;

            // The bike's wheels when he is on one, his feet when he is not.
            Entity body = me;
            if (me.IsInVehicle() && me.CurrentVehicle != null) body = me.CurrentVehicle;

            var at = body.Position;
            var here = new Vector3(at.X, at.Y, Ground(at, body, body == me ? 1.0f : 0.45f));

            if (_taking.Count == 0)
            {
                _taking.Add(here);
                return;
            }

            var d = Flat(_taking[_taking.Count - 1], here);
            if (d < Spacing) return;

            // A LEAP IS NOT A RIDE. A teleport -- a door, a trainer, a reload -- would join two
            // places with a straight line through whatever is between them, and a rider would
            // be sent along it. So it stops where he was and keeps what he rode.
            if (d > Jump)
            {
                var said = Stop();
                Notify.Important("You moved " + d.ToString("0") + " m in one go, so the recording stopped where you were. " + said);
                return;
            }

            _taking.Add(here);
            _takingLength += d;
        }

        /// <summary>The ground under a point: what a raycast finds, else a fair guess from the height of the thing.</summary>
        private static float Ground(Vector3 at, Entity ignore, float above)
        {
            try
            {
                var hit = World.Raycast(at + new Vector3(0f, 0f, 0.5f), at - new Vector3(0f, 0f, 3f),
                                        IntersectFlags.Map | IntersectFlags.Objects, ignore);

                if (hit.DidHit) return hit.HitPosition.Z;
            }
            catch
            {
                // The guess.
            }

            return at.Z - above;
        }

        // ---- drawing ----------------------------------------------------------------------

        private static void Draw(List<Vector3> pts, bool loop, bool live)
        {
            var me = Game.Player.Character;
            if (me == null || !me.Exists()) return;

            var eye = me.Position;

            // Green while it is being ridden, blue once it is the path.
            int r = live ? 70 : 90, g = live ? 230 : 170, b = live ? 110 : 255;

            var drawn = 0;

            for (var i = 1; i < pts.Count && drawn < DrawMost; i++)
            {
                if (Flat(pts[i - 1], eye) > DrawReach && Flat(pts[i], eye) > DrawReach) continue;

                Line(pts[i - 1], pts[i], r, g, b, 255);
                drawn++;
            }

            if (loop && pts.Count > 2) Line(pts[pts.Count - 1], pts[0], r, g, b, 255);

            if (!live || pts.Count == 0) return;

            // Where he is now, from the last point kept, fainter.
            var body = me.IsInVehicle() && me.CurrentVehicle != null ? (Entity)me.CurrentVehicle : me;
            var at = body.Position;
            Line(pts[pts.Count - 1], new Vector3(at.X, at.Y, at.Z - (body == me ? 1.0f : 0.45f)), r, g, b, 120);

            // And the start, so he can see where to finish for a loop.
            var s = pts[0];

            try
            {
                Function.Call(Hash.DRAW_MARKER, 1, s.X, s.Y, s.Z - 0.1f, 0f, 0f, 0f, 0f, 0f, 0f,
                              1.2f, 1.2f, 0.6f, r, g, b, 140, false, false, 2, false, null, null, false);
            }
            catch
            {
                // The line shows it well enough.
            }
        }

        private static void Line(Vector3 a, Vector3 b, int r, int g, int bl, int alpha)
        {
            Function.Call(Hash.DRAW_LINE, a.X, a.Y, a.Z + 0.35f, b.X, b.Y, b.Z + 0.35f, r, g, bl, alpha);
        }

        private static void Hint()
        {
            var text = "RECORDING THE RIDERS' PATH   " + _taking.Count + " points, " + _takingLength.ToString("0") + " m";

            if (_taking.Count >= MinPoints && Flat(_taking[_taking.Count - 1], _taking[0]) <= LoopGap)
            {
                text += "   -- stop here and it is a loop";
            }

            text += "   -- stop it in the settings, The block";

            Hud.Text(text, 0.5f, 0.86f, 0.32f, Color.FromArgb(235, 120, 235, 150), Hud.FontLabel, true, true, false);
        }

        // ---- riding it --------------------------------------------------------------------

        /// <summary>The point nearest a place, or -1.</summary>
        public static int Nearest(Vector3 at)
        {
            var best = -1;
            var bestD = float.MaxValue;

            for (var i = 0; i < Points.Count; i++)
            {
                var d = Flat(Points[i], at);
                if (d >= bestD) continue;

                bestD = d;
                best = i;
            }

            return best;
        }

        /// <summary>How far a place is from the path, on the ground.</summary>
        public static float DistanceTo(Vector3 at)
        {
            var n = Nearest(at);
            return n < 0 ? float.MaxValue : Flat(Points[n], at);
        }

        /// <summary>Whether a destination is one of the path's own points: somebody sent to ride it.</summary>
        public static bool On(Vector3 at)
        {
            return DistanceTo(at) <= OnPoint;
        }

        /// <summary>
        /// The next point along. Round and round a loop; on a there-and-back path it turns at
        /// the ends, and the way he is going comes back turned.
        /// </summary>
        public static int Next(int i, ref int way)
        {
            var n = Points.Count;
            if (n == 0) return -1;

            if (way == 0) way = 1;

            if (Loop) return ((i + way) % n + n) % n;

            var j = i + way;

            if (j < 0 || j >= n)
            {
                way = -way;
                j = i + way;
            }

            return j < 0 || j >= n ? i : j;
        }

        /// <summary>
        /// How near the point he is riding to he has to be before a line past it may leave it
        /// out. Just more than the drive task's own stopping distance, so a rider who pulls up
        /// short of a corner still gets round it.
        /// </summary>
        private const float Reached = 1.6f;

        /// <summary>
        /// Where to ride to next, heading for point <paramref name="from"/>: the furthest point
        /// along that is no more than <paramref name="most"/> metres of path away and whose
        /// straight line from his wheels stays within <paramref name="hug"/> of every point it
        /// cuts past -- <paramref name="from"/> as well, until he is nearly on it. It is
        /// <paramref name="from"/> itself when nothing past it keeps to the path yet: ride on.
        ///
        /// A LINE, NOT A POINT AT A TIME. Three metres a point, ridden point by point, is a rider
        /// twitching the bars every half second. On a straight he is given the whole straight.
        ///
        /// AND NOT ACROSS THE CORNER. Handed the next line three metres short of a corner, a line
        /// that skips the corner point cuts it by a metre and a half -- a fence post, the end of
        /// a wall. So round a corner he rides on into it, and the line on only comes once he is
        /// there. <paramref name="least"/> keeps a line from ending while he is on top of its end.
        /// </summary>
        public static int Ahead(int from, ref int way, Vector3 wheels, float most, float hug, float least)
        {
            if (Points.Count < 2 || from < 0 || from >= Points.Count) return from;

            var reached = Flat(wheels, Points[from]) <= Reached;

            var pick = from;
            var pickWay = way;
            var prev = from;
            var probeWay = way;
            var run = 0f;

            for (var steps = 0; steps < 16; steps++)
            {
                var before = probeWay;
                var nx = Next(prev, ref probeWay);

                if (nx == prev) break;

                // A there-and-back path turns round at its end, and the turn is a line of its own.
                if (probeWay != before && steps > 0) break;

                run += Flat(Points[prev], Points[nx]);
                if (run > most && Flat(wheels, Points[pick]) >= least) break;

                if (!Keeps(from, way, prev, wheels, nx, hug, reached)) break;

                pick = nx;
                pickWay = probeWay;
                prev = nx;
            }

            way = pickWay;
            return pick;
        }

        /// <summary>
        /// Whether the straight line from his wheels to point <paramref name="to"/> keeps within
        /// <paramref name="hug"/> of every point it passes on the way, from <paramref name="from"/>
        /// -- left out once he has reached it -- to <paramref name="last"/>.
        /// </summary>
        private static bool Keeps(int from, int way, int last, Vector3 wheels, int to, float hug, bool reached)
        {
            var k = from;
            var kw = way;

            for (var guard = 0; guard < 40; guard++)
            {
                if (!(reached && k == from) && Park.Near2(Points[k], wheels, Points[to]) > hug) return false;
                if (k == last) return true;

                k = Next(k, ref kw);
            }

            return true;
        }

        // ---- sums -------------------------------------------------------------------------

        private static float Length(List<Vector3> pts, bool loop)
        {
            var total = 0f;

            for (var i = 1; i < pts.Count; i++) total += Flat(pts[i - 1], pts[i]);
            if (loop && pts.Count > 2) total += Flat(pts[pts.Count - 1], pts[0]);

            return total;
        }

        private static float Flat(Vector3 a, Vector3 b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
