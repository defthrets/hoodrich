using System;
using System.Collections.Generic;
using GTA.Math;

namespace Hoodrich.Gangs
{
    /// <summary>
    /// Something in the park a bike must not ride into, seen from above: a box turned to the
    /// way the thing itself is turned, plus how high it reaches.
    /// </summary>
    internal struct Block
    {
        public float X, Y;

        /// <summary>The box's own two axes on the ground, both unit length.</summary>
        public float Ax, Ay, Bx, By;

        /// <summary>Half its size along each of those.</summary>
        public float Ha, Hb;

        /// <summary>Bottom and top of it.</summary>
        public float Lo, Hi;

        /// <summary>A circle that holds the whole box, so most of them are ruled out by one sum.</summary>
        public float Reach;

        /// <summary>A ramp or the like: something worth riding past, not only round.</summary>
        public bool Feature;

        public static Block Make(float x, float y, float ax, float ay, float ha, float hb,
                                 float lo, float hi, bool feature)
        {
            var len = (float)Math.Sqrt(ax * ax + ay * ay);
            if (len < 1e-4f) { ax = 1f; ay = 0f; len = 1f; }

            ax /= len;
            ay /= len;

            return new Block
            {
                X = x, Y = y,
                Ax = ax, Ay = ay,
                Bx = -ay, By = ax,
                Ha = Math.Abs(ha), Hb = Math.Abs(hb),
                Lo = lo, Hi = hi,
                Reach = (float)Math.Sqrt(ha * ha + hb * hb),
                Feature = feature
            };
        }

        /// <summary>How far a point on the ground is from the box. Nought inside it.</summary>
        public float Distance(float px, float py)
        {
            var dx = px - X;
            var dy = py - Y;

            var u = Math.Abs(dx * Ax + dy * Ay) - Ha;
            var v = Math.Abs(dx * Bx + dy * By) - Hb;

            if (u < 0f) u = 0f;
            if (v < 0f) v = 0f;

            return (float)Math.Sqrt(u * u + v * v);
        }

        /// <summary>
        /// How close a straight line on the ground comes to the box. Nought if it crosses it.
        ///
        /// EXACT, NOT SAMPLED. A line sampled every half metre can step over the corner of a
        /// kicker ramp, which is the one thing this exists to catch. In the box's own frame it
        /// is a rectangle, and the nearest a segment gets to a rectangle it does not cross is
        /// at one of the segment's ends or one of the rectangle's corners.
        /// </summary>
        public float Distance(float x1, float y1, float x2, float y2)
        {
            var au = (x1 - X) * Ax + (y1 - Y) * Ay;
            var av = (x1 - X) * Bx + (y1 - Y) * By;
            var bu = (x2 - X) * Ax + (y2 - Y) * Ay;
            var bv = (x2 - X) * Bx + (y2 - Y) * By;

            if (Crosses(au, av, bu, bv, Ha, Hb)) return 0f;

            var best = Math.Min(Local(au, av), Local(bu, bv));

            best = Math.Min(best, ToSegment(Ha, Hb, au, av, bu, bv));
            best = Math.Min(best, ToSegment(-Ha, Hb, au, av, bu, bv));
            best = Math.Min(best, ToSegment(Ha, -Hb, au, av, bu, bv));
            best = Math.Min(best, ToSegment(-Ha, -Hb, au, av, bu, bv));

            return best;
        }

        private float Local(float u, float v)
        {
            var eu = Math.Abs(u) - Ha;
            var ev = Math.Abs(v) - Hb;

            if (eu < 0f) eu = 0f;
            if (ev < 0f) ev = 0f;

            return (float)Math.Sqrt(eu * eu + ev * ev);
        }

        /// <summary>Liang-Barsky: whether any of the segment survives clipping to the rectangle.</summary>
        private static bool Crosses(float au, float av, float bu, float bv, float hu, float hv)
        {
            float t0 = 0f, t1 = 1f;
            var du = bu - au;
            var dv = bv - av;

            return Clip(-du, au + hu, ref t0, ref t1)
                && Clip(du, hu - au, ref t0, ref t1)
                && Clip(-dv, av + hv, ref t0, ref t1)
                && Clip(dv, hv - av, ref t0, ref t1);
        }

        private static bool Clip(float p, float q, ref float t0, ref float t1)
        {
            if (Math.Abs(p) < 1e-9f) return q >= 0f;

            var r = q / p;

            if (p < 0f)
            {
                if (r > t1) return false;
                if (r > t0) t0 = r;
            }
            else
            {
                if (r < t0) return false;
                if (r < t1) t1 = r;
            }

            return true;
        }

        private static float ToSegment(float px, float py, float ax, float ay, float bx, float by)
        {
            var dx = bx - ax;
            var dy = by - ay;
            var len2 = dx * dx + dy * dy;

            var t = len2 < 1e-9f ? 0f : ((px - ax) * dx + (py - ay) * dy) / len2;
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;

            var cx = ax + dx * t - px;
            var cy = ay + dy * t - py;

            return (float)Math.Sqrt(cx * cx + cy * cy);
        }
    }

    /// <summary>
    /// What the survey needs to ask the world.
    ///
    /// AN INTERFACE AND NOT THE NATIVES, so the whole of the park -- the survey, the map it
    /// makes, the routes over it and every leg a rider is given -- can be run offline against a
    /// park built out of boxes and checked, before any of it is let near the game.
    /// </summary>
    internal interface IParkWorld
    {
        /// <summary>
        /// The ground under a spot, looked for between two heights. False if there is none.
        /// OnThing is true when what was hit is a placed thing rather than the map itself.
        /// </summary>
        bool Ground(float x, float y, float fromZ, float toZ, out float z, out bool onThing);

        /// <summary>Whether a straight line from a to b runs into anything solid.</summary>
        bool Hits(Vector3 a, Vector3 b);

        bool OnRoad(Vector3 at);

        /// <summary>On the set's own turf.</summary>
        bool Ours(Vector3 at);

        /// <summary>How far to the nearest road, for choosing the way out.</summary>
        float ToRoad(Vector3 at);

        /// <summary>Everything placed near the park that a bike could ride into.</summary>
        List<Block> Things(Vector3 centre, float radius);
    }

    /// <summary>
    /// The park, surveyed: where on it a bike can be, and which straight lines between those
    /// places are clear to ride.
    ///
    /// WHY IT HAS TO BE SURVEYED. The riders used to be handed a circle round a point -- six
    /// to nine metres out from wherever they stopped -- and told to ride it. Nothing asked
    /// what was on that circle, and the courts are now full of things: kicker ramps, a
    /// halfpipe, quarter pipes, a skip ramp, cars, a camp, three apartment blocks and eighty
    /// people. Every lap went through some of them. A rider cannot see; the drive task will
    /// steer round a thing it happens to be pointed at and not much else. So the looking is
    /// done here, once, for the whole park, and the riders only ever get lines that have been
    /// checked.
    ///
    /// HOW. A grid over the park, six metres a square. A square is a SPOT if there is ground
    /// under it at about the park's height, the ground is the map and not something standing
    /// on it, it is not a road, it is on our turf, and nothing is within a bike's length of it.
    /// Two neighbouring spots are LINKED if the line between them is clear: nothing placed
    /// comes within reach of it, three rays along it at handlebar height hit nothing, and the
    /// ground under it has no step, drop or ramp in it. Only the largest joined-up patch is
    /// kept, so nobody is sent to a spot they cannot get to.
    ///
    /// Done a few rays at a time, over as many frames as it takes -- see Step.
    /// </summary>
    internal sealed class Park
    {
        /// <summary>Grid spacing. A bike's turning circle, roughly, and a sensible number of rays.</summary>
        public const float Cell = 6f;

        /// <summary>
        /// How near a placed thing a bike's line may pass. Half a bike's width, the rider's knees
        /// and handlebars, and room for the drive task to wobble -- measured from the thing's own
        /// box, not its middle, so a long ramp is given its length.
        /// </summary>
        public const float Margin = 1.25f;

        /// <summary>How far above and below the park's height ground still counts as the park.</summary>
        public const float Band = 5f;

        /// <summary>The steepest a line may climb, rise over run. A kerb is fine; a stair is not.</summary>
        public const float Steepest = 0.3f;

        /// <summary>The biggest bump under a line before it is a step rather than the ground.</summary>
        public const float Bump = 0.45f;

        /// <summary>
        /// Heights the rays go along, above the ground. The low one catches kerbs of things and
        /// ramps; the high one catches railings, fences and a car's wing.
        /// </summary>
        private const float RayLow = 0.55f;
        private const float RayHigh = 1.15f;

        /// <summary>How far either side of the middle the low rays go. Handlebar width.</summary>
        private const float Side = 0.45f;

        /// <summary>Something flatter than this is ridden over, not round: a skateboard, a mat.</summary>
        private const float Flat = 0.14f;

        /// <summary>How far round a spot a quick look is taken for walls, before any link is tried.</summary>
        private const float Elbow = 1.5f;

        /// <summary>Longest straight line a rider is ever given in one go.</summary>
        public const float LegMost = 26f;

        /// <summary>Near enough a feature -- a ramp, the courts -- to count as riding past it.</summary>
        public const float FeatureNear = 14f;

        public readonly Vector3 Centre;
        public readonly float Radius;

        /// <summary>Extra points of interest the survey is told about: the courts, for one.</summary>
        public readonly List<Vector3> Landmarks = new List<Vector3>();

        // ---- the map --------------------------------------------------------------

        public readonly List<Vector3> Spots = new List<Vector3>();
        public readonly List<List<int>> Links = new List<List<int>>();

        /// <summary>Spots on the edge of the park, which is where you come in and go out.</summary>
        public readonly List<int> Edge = new List<int>();

        /// <summary>Spots within riding-past distance of a ramp or a landmark.</summary>
        public readonly HashSet<int> Near = new HashSet<int>();

        /// <summary>The edge spot nearest a road, for leaving by.</summary>
        public int Exit = -1;

        /// <summary>
        /// Edge spots with a road just beyond them: the ways in.
        ///
        /// A crew rides to the park by road, and the last bit -- off the road and onto the
        /// park -- is the one stretch nobody has checked. Coming in through a spot the road runs
        /// right past keeps that stretch to a few metres.
        /// </summary>
        public readonly List<int> Doors = new List<int>();

        /// <summary>How near a road an edge spot has to be to count as a way in.</summary>
        public const float DoorRoad = 14f;

        public List<Block> Blocks = new List<Block>();

        public bool Ready { get; private set; }

        /// <summary>A survey is under way.</summary>
        public bool Busy { get; private set; }

        /// <summary>When the last one finished, in game milliseconds.</summary>
        public int SurveyedAt { get; private set; }

        /// <summary>Rays used by the last survey, for the log.</summary>
        public int RaysUsed { get; private set; }

        public Park(Vector3 centre, float radius)
        {
            Centre = centre;
            Radius = radius;
        }

        public bool Inside(Vector3 at)
        {
            var dx = at.X - Centre.X;
            var dy = at.Y - Centre.Y;
            return dx * dx + dy * dy <= Radius * Radius;
        }

        // ---- the survey -------------------------------------------------------------

        private IParkWorld _world;
        private int _stage;
        private int _i, _j;
        private int _half;

        private readonly List<Vector3> _cells = new List<Vector3>();
        private readonly List<long> _cellKeys = new List<long>();

        private readonly List<Vector3> _found = new List<Vector3>();
        private readonly List<long> _foundKeys = new List<long>();
        private readonly Dictionary<long, int> _byKey = new Dictionary<long, int>();
        private List<List<int>> _foundLinks;
        private int _rays;

        /// <summary>Starts a fresh survey. The old map keeps being used until the new one is done.</summary>
        public void Begin(IParkWorld world)
        {
            _world = world;
            _stage = 0;
            _i = _j = 0;
            _rays = 0;
            Busy = true;
        }

        /// <summary>
        /// A slice of the survey, spending at most this many rays.
        ///
        /// A FEW DOZEN A FRAME, NOT THE LOT AT ONCE. The whole park is a thousand-odd rays and
        /// they are the synchronous kind: all of them in one frame is a visible hitch every
        /// time a crew decides to go to the park. Spread out, it is a second or two of frames
        /// nobody notices, and the riders keep using the last map until this one is done.
        /// </summary>
        /// <returns>True on the call that finishes it.</returns>
        public bool Step(int budget, int now)
        {
            if (!Busy || _world == null) return false;

            var spent = 0;

            while (spent < budget)
            {
                switch (_stage)
                {
                    case 0:
                        Stage0Things();
                        _stage = 1;
                        break;

                    case 1:
                        if (_i >= _cells.Count)
                        {
                            _stage = 2;
                            _i = 0;
                            _foundLinks = new List<List<int>>();
                            for (var n = 0; n < _found.Count; n++) _foundLinks.Add(new List<int>());
                            break;
                        }

                        spent += Stage1Cell(_i++);
                        break;

                    case 2:
                        if (_i >= _found.Count)
                        {
                            _rays += spent;
                            Stage3Finish(now);
                            return true;
                        }

                        spent += Stage2Links(_i, _j);

                        if (++_j >= Neighbours.Length)
                        {
                            _j = 0;
                            _i++;
                        }

                        break;
                }
            }

            _rays += spent;
            return false;
        }

        /// <summary>Half the neighbours: every pair of squares is tried once, from one end.</summary>
        private static readonly int[][] Neighbours =
        {
            new[] { 1, 0 }, new[] { 0, 1 }, new[] { 1, 1 }, new[] { 1, -1 },

            // AND A KNIGHT'S MOVE. Eight directions on a grid make a rider zig-zag to go
            // anywhere that is not along one of them; these give him sixteen, and the legs a
            // rider is actually handed are straightened out on top of that. See NextLeg.
            new[] { 2, 1 }, new[] { 1, 2 }, new[] { 2, -1 }, new[] { 1, -2 }
        };

        private void Stage0Things()
        {
            _cells.Clear();
            _cellKeys.Clear();
            _found.Clear();
            _foundKeys.Clear();
            _byKey.Clear();

            try { _nextBlocks = _world.Things(Centre, Radius + 15f) ?? new List<Block>(); }
            catch { _nextBlocks = new List<Block>(); }

            _half = (int)Math.Ceiling(Radius / Cell);

            for (var gx = -_half; gx <= _half; gx++)
            {
                for (var gy = -_half; gy <= _half; gy++)
                {
                    var x = Centre.X + gx * Cell;
                    var y = Centre.Y + gy * Cell;

                    var dx = x - Centre.X;
                    var dy = y - Centre.Y;
                    if (dx * dx + dy * dy > Radius * Radius) continue;

                    _cells.Add(new Vector3(x, y, Centre.Z));
                    _cellKeys.Add(Key(gx, gy));
                }
            }
        }

        private List<Block> _nextBlocks = new List<Block>();

        /// <returns>Rays spent.</returns>
        private int Stage1Cell(int index)
        {
            var at = _cells[index];
            var spent = 0;

            try
            {
                if (!_world.Ours(at)) return 0;

                float z;
                bool onThing;

                spent++;
                if (!_world.Ground(at.X, at.Y, Centre.Z + Band, Centre.Z - Band, out z, out onThing)) return spent;

                // Standing on something placed -- a ramp, the slab under a block, a roof.
                if (onThing) return spent;

                var here = new Vector3(at.X, at.Y, z);

                if (_world.OnRoad(here)) return spent;

                // Nothing placed within a bike's length of the middle of the square.
                foreach (var b in _nextBlocks)
                {
                    if (!Tall(b, z, z)) continue;
                    if (b.Distance(here.X, here.Y) < Margin) return spent;
                }

                // A quick look round for walls. Two of the four blocked is a corner or a
                // doorway, and a bike does not go and stand in one of those.
                var shut = 0;
                var up = new Vector3(0f, 0f, RayLow);

                for (var k = 0; k < 4; k++)
                {
                    var turn = k * Math.PI * 0.5;
                    var out_ = new Vector3((float)Math.Cos(turn) * Elbow, (float)Math.Sin(turn) * Elbow, 0f);

                    spent++;
                    if (_world.Hits(here + up, here + up + out_)) shut++;

                    if (shut >= 2) return spent;
                }

                _byKey[_cellKeys[index]] = _found.Count;
                _found.Add(here);
                _foundKeys.Add(_cellKeys[index]);
            }
            catch
            {
                // A square that could not be asked about is a square nobody rides to.
            }

            return spent;
        }

        /// <returns>Rays spent.</returns>
        private int Stage2Links(int node, int which)
        {
            long key = _foundKeys[node];
            int gx, gy;
            Unkey(key, out gx, out gy);

            var d = Neighbours[which];

            int other;
            if (!_byKey.TryGetValue(Key(gx + d[0], gy + d[1]), out other)) return 0;

            int spent;
            if (!ClearWith(_world, _nextBlocks, _found[node], _found[other], out spent)) return spent;

            _foundLinks[node].Add(other);
            _foundLinks[other].Add(node);

            return spent;
        }

        private void Stage3Finish(int now)
        {
            // The largest joined-up patch only. A spot you cannot ride to is not a spot.
            var seen = new int[_found.Count];
            var bestSize = 0;
            var bestMark = 0;
            var mark = 0;

            for (var s = 0; s < _found.Count; s++)
            {
                if (seen[s] != 0) continue;

                mark++;
                var size = 0;
                var stack = new Stack<int>();
                stack.Push(s);
                seen[s] = mark;

                while (stack.Count > 0)
                {
                    var n = stack.Pop();
                    size++;

                    foreach (var m in _foundLinks[n])
                    {
                        if (seen[m] != 0) continue;
                        seen[m] = mark;
                        stack.Push(m);
                    }
                }

                if (size > bestSize)
                {
                    bestSize = size;
                    bestMark = mark;
                }
            }

            var renumber = new int[_found.Count];

            Spots.Clear();
            Links.Clear();
            Edge.Clear();
            Near.Clear();

            var keys = new List<long>();

            for (var n = 0; n < _found.Count; n++)
            {
                if (seen[n] != bestMark || bestSize < MinSpots)
                {
                    renumber[n] = -1;
                    continue;
                }

                renumber[n] = Spots.Count;
                Spots.Add(_found[n]);
                keys.Add(_foundKeys[n]);
                Links.Add(new List<int>());
            }

            for (var n = 0; n < _found.Count; n++)
            {
                if (renumber[n] < 0) continue;

                foreach (var m in _foundLinks[n])
                {
                    if (renumber[m] >= 0) Links[renumber[n]].Add(renumber[m]);
                }
            }

            var have = new HashSet<long>(keys);

            for (var n = 0; n < Spots.Count; n++)
            {
                int gx, gy;
                Unkey(keys[n], out gx, out gy);

                // An edge spot is one with a gap somewhere in the eight squares round it.
                var round = 0;

                for (var ox = -1; ox <= 1; ox++)
                {
                    for (var oy = -1; oy <= 1; oy++)
                    {
                        if (ox == 0 && oy == 0) continue;
                        if (have.Contains(Key(gx + ox, gy + oy))) round++;
                    }
                }

                if (round < 8) Edge.Add(n);

                foreach (var b in _nextBlocks)
                {
                    if (!b.Feature) continue;
                    if (b.Distance(Spots[n].X, Spots[n].Y) > FeatureNear) continue;
                    Near.Add(n);
                    break;
                }

                foreach (var l in Landmarks)
                {
                    if (Flat2(Spots[n], l) > FeatureNear) continue;
                    Near.Add(n);
                    break;
                }
            }

            Exit = -1;
            Doors.Clear();
            var nearest = float.MaxValue;

            foreach (var n in Edge)
            {
                float road;
                try { road = _world.ToRoad(Spots[n]); }
                catch { continue; }

                if (road < 0f) continue;
                if (road <= DoorRoad) Doors.Add(n);

                if (road >= nearest) continue;

                nearest = road;
                Exit = n;
            }

            if (Exit < 0 && Edge.Count > 0) Exit = Edge[0];

            Blocks = _nextBlocks;
            Ready = Spots.Count >= MinSpots;
            Busy = false;
            SurveyedAt = now;
            RaysUsed = _rays;
            _foundLinks = null;
        }

        /// <summary>Fewer than this and it is not a park, it is a patch, and nobody is sent.</summary>
        public const int MinSpots = 10;

        public int LinkCount
        {
            get
            {
                var n = 0;
                foreach (var l in Links) n += l.Count;
                return n / 2;
            }
        }

        // ---- is a line clear --------------------------------------------------------

        /// <summary>
        /// Whether a bike can ride the straight line from a to b right now.
        ///
        /// Both ends are GROUND heights -- a spot's own height, or where a bike's wheels are.
        /// Asked again every time a rider is given a line, not only by the survey, because
        /// between the two somebody may have parked a car across it.
        /// </summary>
        public bool Clear(IParkWorld world, Vector3 a, Vector3 b)
        {
            int spent;
            return ClearWith(world, Blocks, a, b, out spent);
        }

        private static bool ClearWith(IParkWorld world, List<Block> blocks, Vector3 a, Vector3 b, out int spent)
        {
            spent = 0;

            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var run = (float)Math.Sqrt(dx * dx + dy * dy);

            if (run < 0.5f) return true;
            if (Math.Abs(b.Z - a.Z) > run * Steepest + Bump) return false;

            // Placed things first: pure arithmetic against the box of every one of them, and it
            // is what the riders were hitting.
            var lo = Math.Min(a.Z, b.Z);
            var hi = Math.Max(a.Z, b.Z);

            foreach (var blk in blocks)
            {
                if (!Tall(blk, lo, hi)) continue;

                // Nowhere near the line at all: one comparison and on to the next.
                var mx = (a.X + b.X) * 0.5f - blk.X;
                var my = (a.Y + b.Y) * 0.5f - blk.Y;
                var far = run * 0.5f + blk.Reach + Margin;
                if (mx * mx + my * my > far * far) continue;

                if (blk.Distance(a.X, a.Y, b.X, b.Y) < Margin) return false;
            }

            // Then the map, which the boxes know nothing about: walls, fences, kerbs of
            // buildings, the four-foot wall across B.J. Smith.
            var sx = -dy / run * Side;
            var sy = dx / run * Side;

            var low = new Vector3(0f, 0f, RayLow);
            var high = new Vector3(0f, 0f, RayHigh);
            var left = new Vector3(sx, sy, RayLow);
            var right = new Vector3(-sx, -sy, RayLow);

            spent++;
            if (world.Hits(a + low, b + low)) return false;
            spent++;
            if (world.Hits(a + left, b + left)) return false;
            spent++;
            if (world.Hits(a + right, b + right)) return false;
            spent++;
            if (world.Hits(a + high, b + high)) return false;

            // And the ground under it, every three metres or so: no hole, no step, and not the
            // top of something -- a ramp's own surface is ground to a ray and a wall to a bike.
            var pieces = Math.Max(1, (int)Math.Ceiling(run / 3f));

            for (var k = 1; k < pieces; k++)
            {
                var t = k / (float)pieces;
                var x = a.X + dx * t;
                var y = a.Y + dy * t;
                var z = a.Z + (b.Z - a.Z) * t;

                float ground;
                bool onThing;

                spent++;
                if (!world.Ground(x, y, z + 1.2f, z - 1.5f, out ground, out onThing)) return false;
                if (onThing) return false;
                if (Math.Abs(ground - z) > Bump) return false;
            }

            return true;
        }

        /// <summary>Whether a thing reaches into the height a bike on this line takes up.</summary>
        private static bool Tall(Block b, float groundLo, float groundHi)
        {
            // Entirely below the ground, or above a rider's head.
            if (b.Hi < groundLo + Flat) return false;
            if (b.Lo > groundHi + 2.2f) return false;
            return true;
        }

        // ---- getting about ----------------------------------------------------------

        /// <summary>The spot nearest a point, or -1.</summary>
        public int Nearest(Vector3 at)
        {
            var best = -1;
            var bestD = float.MaxValue;

            for (var n = 0; n < Spots.Count; n++)
            {
                var d = Flat2(Spots[n], at);
                if (d >= bestD) continue;
                bestD = d;
                best = n;
            }

            return best;
        }

        /// <summary>
        /// Where somebody arriving from a point comes in: the nearest way in off a road, else the
        /// nearest edge spot, else the nearest spot of all.
        /// </summary>
        public int EntryFor(Vector3 from)
        {
            var best = NearestOf(Doors, from);
            if (best < 0) best = NearestOf(Edge, from);
            return best >= 0 ? best : Nearest(from);
        }

        private int NearestOf(List<int> among, Vector3 from)
        {
            var best = -1;
            var bestD = float.MaxValue;

            foreach (var n in among)
            {
                var d = Flat2(Spots[n], from);
                if (d >= bestD) continue;
                bestD = d;
                best = n;
            }

            return best;
        }

        /// <summary>
        /// Whether a bike can ride a straight line, asked of the world alone -- walls, placed
        /// things, parked cars and the ground -- with no survey behind it. For the riders who
        /// hang about somewhere that is not the park.
        /// </summary>
        public static bool LineClear(IParkWorld world, Vector3 a, Vector3 b)
        {
            int spent;
            return ClearWith(world, NoBlocks, a, b, out spent);
        }

        private static readonly List<Block> NoBlocks = new List<Block>();

        /// <summary>
        /// Somewhere to ride to next.
        ///
        /// THE WHOLE PARK, NOT THE NEAREST CORNER OF IT. Somewhere at least a street's length
        /// away, and whichever this rider has been longest without seeing -- so over a couple
        /// of minutes he covers all of it, the courts and the ramps and the far end, instead of
        /// wearing a groove between two spots. Ramps and landmarks weigh more, so he rides past
        /// the kickers more than past a fence. And away from where his mates are headed, so four
        /// of them are four riders round the park rather than a queue.
        /// </summary>
        public int Pick(int from, Dictionary<int, int> seen, List<Vector3> othersHeading, Random rng, int now)
        {
            if (Spots.Count == 0) return -1;

            var here = from >= 0 ? Spots[from] : Centre;
            var best = -1;
            var bestScore = float.MinValue;

            for (var tries = 0; tries < 40; tries++)
            {
                var n = rng.Next(Spots.Count);
                if (n == from) continue;

                var d = Flat2(Spots[n], here);
                if (d < 18f && tries < 30) continue;

                int last;
                var since = seen != null && seen.TryGetValue(n, out last) ? (now - last) / 1000f : 600f;
                if (since > 600f) since = 600f;

                var score = since + (float)rng.NextDouble() * 60f;

                if (Near.Contains(n)) score += 90f;

                if (othersHeading != null)
                {
                    foreach (var o in othersHeading)
                    {
                        if (Flat2(o, Spots[n]) < 12f) score -= 150f;
                    }
                }

                if (score <= bestScore) continue;

                bestScore = score;
                best = n;
            }

            return best;
        }

        /// <summary>
        /// The way from one spot to another over the links, shortest first, never over a link
        /// in the avoid set. Null if there is none.
        /// </summary>
        public List<int> Route(int from, int to, HashSet<long> avoid)
        {
            if (from < 0 || to < 0 || from >= Spots.Count || to >= Spots.Count) return null;
            if (from == to) return new List<int> { from };

            var dist = new float[Spots.Count];
            var back = new int[Spots.Count];
            var done = new bool[Spots.Count];

            for (var n = 0; n < dist.Length; n++)
            {
                dist[n] = float.MaxValue;
                back[n] = -1;
            }

            dist[from] = 0f;

            // A few hundred spots: the plain version is quicker than anything with a heap in it.
            for (var round = 0; round < Spots.Count; round++)
            {
                var u = -1;
                var best = float.MaxValue;

                for (var n = 0; n < dist.Length; n++)
                {
                    if (done[n] || dist[n] >= best) continue;
                    best = dist[n];
                    u = n;
                }

                if (u < 0 || u == to) break;
                done[u] = true;

                foreach (var v in Links[u])
                {
                    if (done[v]) continue;
                    if (avoid != null && avoid.Contains(LinkKey(u, v))) continue;

                    var w = dist[u] + Flat2(Spots[u], Spots[v]);
                    if (w >= dist[v]) continue;

                    dist[v] = w;
                    back[v] = u;
                }
            }

            if (back[to] < 0) return null;

            var path = new List<int>();
            for (var n = to; n >= 0; n = back[n])
            {
                path.Add(n);
                if (n == from) break;
            }

            path.Reverse();
            return path[0] == from ? path : null;
        }

        /// <summary>
        /// How far along a route a rider can go in one straight line from where he is.
        ///
        /// A ROUTE OVER A GRID IS A STAIRCASE. Ridden spot by spot it is a man jinking left and
        /// right every six metres, which is not riding round a park, it is being remote
        /// controlled. So from wherever he is, the furthest spot along the route that he has a
        /// clear straight line to is where he goes next, up to one long leg -- and "clear" is
        /// asked of the world there and then, not taken from the survey.
        /// </summary>
        /// <returns>Index into path of the spot to ride to, or -1 if not even the next one is clear.</returns>
        public int NextLeg(IParkWorld world, Vector3 wheels, List<int> path, int from,
                           Func<Vector3, Vector3, bool> allowed = null)
        {
            if (path == null) return -1;

            var furthest = Math.Min(path.Count - 1, from + 4);

            for (var k = furthest; k >= from; k--)
            {
                var spot = Spots[path[k]];
                if (Flat2(spot, wheels) > LegMost) continue;

                // The caller's own objections first -- somewhere a rider has already come off,
                // say -- because they are arithmetic and the world check is rays.
                if (allowed != null && !allowed(wheels, spot)) continue;

                if (Clear(world, wheels, spot)) return k;
            }

            return -1;
        }

        /// <summary>How near a line comes to a point, on the ground.</summary>
        public static float Near2(Vector3 p, Vector3 a, Vector3 b)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var len2 = dx * dx + dy * dy;

            var t = len2 < 1e-9f ? 0f : ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;

            var cx = a.X + dx * t - p.X;
            var cy = a.Y + dy * t - p.Y;

            return (float)Math.Sqrt(cx * cx + cy * cy);
        }

        public static long LinkKey(int a, int b)
        {
            return a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        }

        private static long Key(int gx, int gy)
        {
            return ((long)(gx + 100000) << 32) | (uint)(gy + 100000);
        }

        private static void Unkey(long key, out int gx, out int gy)
        {
            gx = (int)(key >> 32) - 100000;
            gy = (int)(key & 0xFFFFFFFF) - 100000;
        }

        public static float Flat2(Vector3 a, Vector3 b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
