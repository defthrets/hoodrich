using System;
using GTA.Math;

namespace Hoodrich.Locations
{
    /// <summary>
    /// A performer's wedge of the junction: the slice that is his, and the numbers that follow
    /// from it -- where his spot is, how far his donut may wander, how wide a loop fits.
    ///
    /// NO NATIVES, like TakeoverRig, so both junctions can be run offline for a whole night and
    /// checked in seconds before a car is ever put on it. Takeover does the driving.
    ///
    /// WHY A WEDGE. The marks sit on a ring round the circle, one car each, and the junction is
    /// cut through them into equal slices like a cake: each car has its own, from the middle out
    /// to the crowd, and NOTHING IT DOES EVER LEAVES IT. That one rule is what lets every car do
    /// its own thing on its own clock. The loops used to be safe because everybody looped
    /// together -- same way, same rate, same moment, so the gap between any two cars never
    /// changed -- and that is exactly what made them look rehearsed. Cars that each keep to
    /// their own slice cannot meet, whatever they are doing or when, so the timing can be
    /// anything at all. And it is: see Routine.
    ///
    /// The slice is shrunk by half a car and half the gap on every side, so it is the car's
    /// MIDDLE that has to stay inside, whichever way the car is pointing. Conservative -- a car
    /// side-on to the line needs less -- and cheap, which is what a rule checked every frame on
    /// four cars should be. The pavement corners and the kerbs inside the ring are kept off the
    /// same way: a donut's reach and a loop's edge stay a gap short of every one of them.
    ///
    /// THE SPOT IS NOT THE MARK. The marks were walked seven metres out, and four donuts with
    /// their backs stepping out do not fit on a seven-metre ring without touching -- so the spot
    /// sits on the mark's bearing, as far out as the slice needs and no further, and the loops
    /// go round the spot. The walked marks still decide where the slices are.
    /// </summary>
    internal sealed class Wedge
    {
        /// <summary>The least two cars' bodies ever come to each other, whatever both are doing.</summary>
        public const float Gap = 0.8f;

        /// <summary>The least a car's body comes to the ring the crowd stands on.</summary>
        public const float CrowdGap = 1.5f;

        /// <summary>And to a pavement corner inside it, where people also stand.</summary>
        public const float PeopleGap = 1.5f;

        /// <summary>And to a kerb a spectator's car is parked on: a point, with a car round it.</summary>
        public const float ParkedGap = 4f;

        /// <summary>How far the pivot wanders from the spot, at full room. A donut walks; it does not sit.</summary>
        public const float WanderTop = 0.6f;

        /// <summary>How far a lost donut may carry the pivot from the spot, at full room.</summary>
        public const float DriftTop = 1.4f;

        /// <summary>How far the back may step out beyond the front axle, at full room: the slip.</summary>
        public const float SlipTop = 1.2f;

        /// <summary>A loop narrower than this is a donut, and not worth the trip out to it.</summary>
        public const float LoopLeast = 2.5f;

        /// <summary>How far past the walked ring the spot may be pushed to make room.</summary>
        public const float HomeMost = 4.5f;

        public Vector3 Centre;

        /// <summary>Out along the mark's bearing, and to the left of it. Flat.</summary>
        public Vector3 U;
        public Vector3 V;

        /// <summary>Half the wedge's angle, radians, and how many wedges there are.</summary>
        public float Alpha;
        public int Marks;

        public float Ring;
        public float HalfLong;
        public float HalfWide;
        public float Axle;

        /// <summary>How far out the spot is, and how much of the wander, the slip and the slide there is room for, nought to one.</summary>
        public float Home;
        public float Room;

        public float Wander;
        public float Drift;
        public float SlipMost;

        /// <summary>How far the car's middle keeps from the wedge's sides, and how far out it may go.</summary>
        public float Keep;
        public float Outer;

        /// <summary>How wide a loop round the spot fits. Nought: none does.</summary>
        public float LoopRadius;

        /// <summary>Nothing fitted, not even a plain donut. It gets the mark and no room at all.</summary>
        public bool Cramped;

        private float _sin;
        private float _cos;

        /// <summary>The everyday slip: how far the back sits out beyond the axle when nothing is happening.</summary>
        public float Slip => 0.25f * Room;

        public Vector3 Spot => new Vector3(Centre.X + U.X * Home, Centre.Y + U.Y * Home, Centre.Z);

        public static Wedge Make(Vector3 centre, Vector3 toMark, int marks, float ring, float pitchRing,
                                 float halfLong, float halfWide, float axle, float loopMost,
                                 Vector3[] people, Vector3[] parked)
        {
            var w = new Wedge
            {
                Centre = centre,
                Marks = marks,
                Ring = ring,
                HalfLong = halfLong,
                HalfWide = halfWide,
                Axle = axle
            };

            var flat = new Vector3(toMark.X, toMark.Y, 0f);
            var len = flat.Length();
            w.U = len < 0.01f ? new Vector3(0f, 1f, 0f) : flat * (1f / len);
            w.V = new Vector3(-w.U.Y, w.U.X, 0f);

            w.Alpha = marks > 1 ? (float)Math.PI / marks : (float)Math.PI;
            w._sin = marks > 1 ? (float)Math.Sin(w.Alpha) : 0f;
            w._cos = marks > 1 ? (float)Math.Cos(w.Alpha) : -1f;

            var halfDiag = Hypot(halfLong, halfWide);
            w.Keep = halfDiag + Gap / 2f;
            w.Outer = ring - CrowdGap - halfDiag;

            // AS MUCH ROOM AS FITS, and the spot as far in as that room allows. Full room is a
            // donut whose pivot wanders and slides and whose back steps right out; none is the
            // pivot nailed to the spot and the car turning about its axle, which is what this
            // was before. Tried from full downwards, so a junction that has the room gets it.
            for (var i = 20; i >= 0; i--)
            {
                var room = i / 20f;
                var drift = DriftTop * room;
                var slip = SlipTop * room;

                // Everything a donut can reach: the pivot anywhere within its drift of the
                // spot, the car's middle up to the axle plus the slip behind that, and the far
                // corner of the car beyond.
                var reach = drift + Hypot(axle + slip + halfLong, halfWide);

                var least = marks > 1 ? (reach + Gap / 2f) / w._sin : 0f;
                if (least < pitchRing) least = pitchRing;

                var most = ring - CrowdGap - reach;
                if (most > pitchRing + HomeMost) most = pitchRing + HomeMost;

                if (least > most + 0.001f) continue;

                var bestHome = -1f;
                var bestLoop = float.MinValue;

                for (var home = least; home <= most + 0.001f; home += 0.1f)
                {
                    if (!Clear(w, home, reach, people, parked)) continue;

                    var loop = LoopAt(w, home, halfDiag, loopMost, people, parked);

                    if (loop > bestLoop + 0.02f)
                    {
                        bestLoop = loop;
                        bestHome = home;
                    }

                    // The ini's ask is met: no further out than that.
                    if (loop >= loopMost - 0.001f) break;
                }

                if (bestHome < 0f) continue;

                w.Room = room;
                w.Home = bestHome;
                w.Wander = WanderTop * room;
                w.Drift = drift;
                w.SlipMost = slip;
                w.LoopRadius = bestLoop >= LoopLeast ? bestLoop : 0f;
                return w;
            }

            w.Cramped = true;
            w.Home = pitchRing;
            return w;
        }

        /// <summary>A donut on a spot this far out keeps its distance from everybody stood inside the ring.</summary>
        private static bool Clear(Wedge w, float home, float reach, Vector3[] people, Vector3[] parked)
        {
            var sx = w.Centre.X + w.U.X * home;
            var sy = w.Centre.Y + w.U.Y * home;

            if (people != null)
            {
                foreach (var p in people)
                {
                    if (Hypot(p.X - sx, p.Y - sy) < reach + PeopleGap) return false;
                }
            }

            if (parked != null)
            {
                foreach (var q in parked)
                {
                    if (Hypot(q.X - sx, q.Y - sy) < reach + ParkedGap) return false;
                }
            }

            return true;
        }

        /// <summary>The widest loop round a spot this far out that stays off the sides, the crowd and everybody inside the ring.</summary>
        private static float LoopAt(Wedge w, float home, float halfDiag, float loopMost, Vector3[] people, Vector3[] parked)
        {
            var r = loopMost;

            if (w.Marks > 1) r = Math.Min(r, home * w._sin - (Gap / 2f + halfDiag));
            r = Math.Min(r, w.Ring - CrowdGap - halfDiag - home);

            var sx = w.Centre.X + w.U.X * home;
            var sy = w.Centre.Y + w.U.Y * home;

            if (people != null)
            {
                foreach (var p in people) r = Math.Min(r, Hypot(p.X - sx, p.Y - sy) - PeopleGap - halfDiag);
            }

            if (parked != null)
            {
                foreach (var q in parked) r = Math.Min(r, Hypot(q.X - sx, q.Y - sy) - ParkedGap - halfDiag);
            }

            return r;
        }

        public void Local(Vector3 p, out float x, out float y)
        {
            var dx = p.X - Centre.X;
            var dy = p.Y - Centre.Y;
            x = dx * U.X + dy * U.Y;
            y = dx * V.X + dy * V.Y;
        }

        public Vector3 World(float x, float y, float z)
        {
            return new Vector3(Centre.X + U.X * x + V.X * y, Centre.Y + U.Y * x + V.Y * y, z);
        }

        /// <summary>How far inside the nearer side of the wedge a point is. Negative is outside it.</summary>
        public float Side(float x, float y)
        {
            if (Marks <= 1) return float.MaxValue;
            return x * _sin - Math.Abs(y) * _cos;
        }

        /// <summary>
        /// The point kept where the car's middle may be: Keep inside both sides and no further
        /// out than Outer. Says how far it had to move, which is nought whenever the routine is
        /// doing its job -- this is the net under it, not the rope.
        /// </summary>
        public Vector3 Clamp(Vector3 p, out float pushed)
        {
            float x, y;
            Local(p, out x, out y);

            var x0 = x;
            var y0 = y;

            for (var i = 0; i < 6; i++)
            {
                var moved = false;

                if (Marks > 1)
                {
                    var d = Side(x, y);

                    if (d < Keep - 0.0005f)
                    {
                        var push = Keep - d;

                        if (Math.Abs(y) < 0.01f || _sin >= 0.9999f)
                        {
                            x += push / _sin;
                        }
                        else
                        {
                            x += push * _sin;
                            y -= Math.Sign(y) * push * _cos;
                        }

                        moved = true;
                    }
                }

                var rho = Hypot(x, y);

                if (rho > Outer && rho > 0.001f)
                {
                    var s = Outer / rho;
                    x *= s;
                    y *= s;
                    moved = true;
                }

                if (!moved) break;
            }

            pushed = Hypot(x - x0, y - y0);
            return pushed < 1e-5f ? p : World(x, y, p.Z);
        }

        public static float Hypot(float a, float b)
        {
            return (float)Math.Sqrt(a * a + b * b);
        }
    }

    /// <summary>
    /// What one performer does on his spot, frame by frame: where his middle should be, which
    /// way he should point, and which way the wheel is held.
    ///
    /// IT STARTED TOO CLEANLY AND THEY ALL DID THE SAME THING AT THE SAME TIME. The old rig put
    /// a car straight onto a perfect circle at a hundred and fifty degrees a second the frame the
    /// smoke ended, every car at the same rate, and then sent all of them into the same loop
    /// together and brought them all back turning the same way. It held the line -- that was
    /// the point of it -- and it looked like a machine holding a line.
    ///
    /// This is the same rig with a driver in it. Every number in here that could be a constant
    /// is a random draw per car, and most of them are drawn again each time he hooks up:
    ///
    ///   THE HOOK-UP. From the standing burnout the turn ramps in over two or three seconds
    ///   with a wobble on it that dies away -- the back steps out, bites, steps out again --
    ///   and the back settles a little way out beyond the axle rather than on it.
    ///
    ///   THE DONUT. Not a circle: the rate breathes, the back sits out and comes in, the pivot
    ///   wanders round the spot, and every few seconds a KICK -- the back steps right out, the
    ///   car bogs down as it is gathered back, and the pivot walks a little.
    ///
    ///   LOSING IT. Now and then the turn runs away with him, the whole car slides a metre or
    ///   two, stops nearly dead, sits for a moment, and hooks up again -- half the time the
    ///   other way. Half of those are milder: he lets it stop and goes the other way.
    ///
    ///   PINNING IT OUT. The donut opens up into a loop round the spot -- a spiral out over
    ///   most of a lap, a lap or two sideways with the nose in and the opposite lock on, and a
    ///   spiral back in to a near stop -- and a fresh hook-up from there.
    ///
    /// Everything stays inside his wedge by construction (see Wedge), and a net under it all
    /// says so every frame. No two cars share a clock, a rate, a direction or a decision.
    /// </summary>
    internal sealed class Routine
    {
        public enum Move
        {
            Hook,
            Donut,
            Loss,
            Loop
        }

        public Move Now;

        /// <summary>Where the car's middle should be this frame, how fast that place is moving, and the way it should point.</summary>
        public Vector3 To;
        public Vector3 Feed;
        public float Heading;

        /// <summary>Which way the wheel is held: +1 left, -1 right. Into the turn on a donut, opposite on a loop.</summary>
        public int Lock;

        /// <summary>On a loop, or opening into one: he is out wide of his spot.</summary>
        public bool Wide;

        /// <summary>Whether a loop may begin. Takeover says no while anybody is still driving in.</summary>
        public bool MayGoWide = true;

        public Vector3 Pivot;
        public float Radius;

        /// <summary>The turn, degrees a second, signed the way headings go.</summary>
        public float Rate;
        public int Way;

        /// <summary>For the log, and the harness.</summary>
        public int Kicks;
        public int Losses;
        public int Loops;

        /// <summary>How far the net had to move him this frame. Nought is the routine doing its job.</summary>
        public float Need;

        private readonly Wedge _w;
        private readonly Random _rng;
        private readonly float _speed;
        private readonly float _top;

        /// <summary>Time on the rig, and time in this move.</summary>
        private float _t;
        private float _tm;

        private Vector3 _push;
        private bool _fed;

        private float _rateAim;
        private float _slip;
        private float _slipAim;

        private float _n1T, _n1P, _n2T, _n2P;
        private float _s3T, _s3P;
        private float _w4T, _w4P, _w5T, _w5P;

        private float _hookTime;
        private float _wobble;

        private float _nextKick;
        private float _kick;
        private float _kickLen;
        private float _kickAmp;

        private float _nextLoss;
        private float _nextLoop;

        private int _lossPhase;
        private float _snap;
        private float _sit;
        private bool _mild;
        private Vector3 _slide;

        private int _loopWay = 1;
        private float _loopR;
        private float _loopOmega;
        private float _loopTotal;
        private float _loopDrift;
        private float _theta0;
        private float _r0;
        private float _rEnd;
        private float _turns;

        /// <summary>How fast the pivot walks, metres a second: to the spot, and after its wander.</summary>
        public const float PivotCreep = 1.5f;

        /// <summary>How fast the back steps out or comes in, metres a second.</summary>
        public const float SlipRate = 1.2f;

        /// <summary>How fast the net may move him, metres a second. A car eased onto its line, not snapped.</summary>
        public const float NetRate = 1.5f;

        /// <summary>How fast the nose may swing onto a loop's heading, degrees a second.</summary>
        public const float LoopTurnMost = 360f;

        /// <summary>A loop is up to speed, and down again, over this many seconds.</summary>
        public const float LoopRamp = 1.5f;

        /// <summary>How much of a lap the spiral out takes, and the spiral in.</summary>
        public const float Open = 0.75f;

        private const float TwoPi = (float)(Math.PI * 2.0);

        public Routine(Wedge w, Vector3 at, float heading, int way, float donutSpeed, float loopTop, Random rng)
        {
            _w = w;
            _rng = rng;
            _speed = donutSpeed;
            _top = loopTop;

            Heading = TakeoverRig.Wrap(heading);
            Way = way >= 0 ? 1 : -1;

            // Pinned where his front wheels ARE, so the first frame is a car starting to turn
            // and not a car jumping to a new spot. The pivot then walks to the spot.
            var f = TakeoverRig.Fwd(Heading);
            Pivot = new Vector3(at.X + f.X * w.Axle, at.Y + f.Y * w.Axle, at.Z);
            Radius = w.Axle;
            To = at;

            _w4T = Between(7f, 12f);
            _w4P = Between(0f, TwoPi);
            _w5T = Between(9f, 15f);
            _w5P = Between(0f, TwoPi);
            _s3T = Between(3f, 6f);
            _s3P = Between(0f, TwoPi);

            _nextLoop = Between(15f, 40f);
            _nextLoss = Between(20f, 60f);

            BeginHook(true);
        }

        public bool Settled => Flat(Pivot, _w.Spot) < 1.5f && _push.Length() < 0.15f;

        public Wedge Wedge => _w;

        public void Step(float dt)
        {
            if (dt <= 0f) return;

            _t += dt;
            _tm += dt;

            var prev = To;

            switch (Now)
            {
                case Move.Hook: Hook(dt); break;
                case Move.Donut: Donut(dt); break;
                case Move.Loss: Loss(dt); break;
                case Move.Loop: Loop(dt); break;
            }

            Net(dt);

            Feed = _fed ? (To - prev) * (1f / dt) : Vector3.Zero;
            _fed = true;

            Lock = Wide ? -_loopWay : Way;
        }

        // ---- the moves --------------------------------------------------------------------

        private void BeginHook(bool first)
        {
            Now = Move.Hook;
            _tm = 0f;
            _kick = 0f;
            Wide = false;

            _hookTime = first ? Between(2.2f, 3.2f) : Between(1.1f, 1.9f);
            _wobble = first ? 0.45f : 0.6f;

            // Not all at the same rate, and not the same rate he had last time either.
            _rateAim = _speed * Between(0.72f, 1.12f);

            _n1T = Between(2.5f, 4f);
            _n1P = Between(0f, TwoPi);
            _n2T = Between(0.9f, 1.4f);
            _n2P = Between(0f, TwoPi);
        }

        /// <summary>The turn ramps in with a wobble that dies away, and the back steps out as it does.</summary>
        private void Hook(float dt)
        {
            var k = Smooth(_tm / _hookTime);
            var wob = 1f + _wobble * (float)Math.Exp(-_tm) * (float)Math.Sin(TwoPi * 0.85f * _tm + 0.4f);

            Rate = Way * _rateAim * k * wob;
            _slipAim = _w.Slip * k;

            Creep(dt);
            Spin(dt);

            if (_tm >= _hookTime)
            {
                Now = Move.Donut;
                _tm = 0f;
                _nextKick = _t + Between(2f, 7f);
            }
        }

        /// <summary>The donut: breathing, wandering, kicking, and deciding what comes next.</summary>
        private void Donut(float dt)
        {
            var n = 1f + 0.10f * (float)Math.Sin(TwoPi * _t / _n1T + _n1P)
                       + 0.06f * (float)Math.Sin(TwoPi * _t / _n2T + _n2P);

            var shape = 0f;

            if (_kick > 0f)
            {
                _kick += dt;
                var u = Math.Min(1f, _kick / _kickLen);
                shape = (float)Math.Pow(Math.Sin(Math.PI * u), 1.2);

                if (_kick >= _kickLen)
                {
                    _kick = 0f;
                    _nextKick = _t + Between(4f, 10f);
                }
            }
            else if (_t >= _nextKick && _w.SlipMost > 0.05f)
            {
                _kick = 0.0001f;
                _kickLen = Between(1.5f, 2.6f);
                _kickAmp = Between(0.5f, 1f) * _w.SlipMost;
                Kicks++;
            }

            // The back steps out and the car bogs down while it is gathered back.
            Rate = Way * _rateAim * n * (1f - 0.35f * shape);
            _slipAim = _w.Slip + 0.1f * _w.Room * (float)Math.Sin(TwoPi * _t / _s3T + _s3P) + _kickAmp * shape;

            if (shape > 0f)
            {
                // And the front wheels crawl: the car walks a little on every kick.
                var before = Pivot;
                var f = TakeoverRig.Fwd(Heading);
                Pivot = new Vector3(Pivot.X + f.X * 0.5f * shape * dt, Pivot.Y + f.Y * 0.5f * shape * dt, Pivot.Z);
                Pivot = Held(before, Pivot);
            }

            Creep(dt);
            Spin(dt);

            if (_kick > 0f) return;

            if (_t >= _nextLoss)
            {
                BeginLoss();
                return;
            }

            if (_w.LoopRadius <= 0f || _t < _nextLoop) return;

            if (!MayGoWide)
            {
                _nextLoop = _t + 3f;
                return;
            }

            // WHEN THE NOSE COMES ROUND. He opens up out of the donut along the loop, so he
            // waits until he is pointing more or less the way the loop goes there -- at most a
            // turn or two of the donut -- rather than flicking round on the spot to find it.
            var spot = _w.Spot;
            var th = (float)Math.Atan2(To.Y - spot.Y, To.X - spot.X);
            var along = TakeoverRig.LoopHeading(th, Way, TakeoverRig.Drift(_w.LoopRadius));

            if (Math.Abs(TakeoverRig.Diff(Heading, along)) < 70f || _t > _nextLoop + 5f) BeginLoop();
        }

        private void BeginLoss()
        {
            Now = Move.Loss;
            _tm = 0f;
            _lossPhase = 0;
            Losses++;

            _mild = _rng.NextDouble() < 0.5;
            _snap = Between(0.55f, 0.85f);
            _sit = Between(0.4f, 1.3f);

            // The whole car goes the way the back was going.
            var v = TakeoverRig.DonutVelocity(Heading, Radius, Rate);
            var len = v.Length();
            _slide = len > 0.001f ? v * ((_mild ? 1f : 2.2f) * _w.Room / len) : Vector3.Zero;

            _nextLoss = _t + Between(30f, 75f);
        }

        /// <summary>Lost it: the turn runs away and the car slides, it stops nearly dead, he sits, and he hooks up again.</summary>
        private void Loss(float dt)
        {
            switch (_lossPhase)
            {
                case 0:
                {
                    var s = Math.Min(1f, _tm / _snap);
                    Rate = Way * _rateAim * (1f + (_mild ? 0.3f : 1.2f) * (float)Math.Sin(Math.PI * s));

                    var before = Pivot;
                    Pivot = new Vector3(Pivot.X + _slide.X * (1f - s) * dt, Pivot.Y + _slide.Y * (1f - s) * dt, Pivot.Z);
                    Pivot = Held(before, Pivot);

                    _slipAim = _mild ? _w.Slip + 0.4f * _w.Room : _w.SlipMost;

                    if (_tm >= _snap)
                    {
                        _lossPhase = 1;
                        _tm = 0f;
                    }

                    // No walk back to the spot while he is sliding away from it; that is the slide.
                    Spin(dt);
                    return;
                }

                case 1:
                    Rate = Way * _rateAim * (1f - Smooth(_tm / 0.5f));

                    if (_tm >= 0.5f)
                    {
                        _lossPhase = 2;
                        _tm = 0f;
                    }

                    break;

                default:
                    Rate = Way * 6f;
                    _slipAim = _w.Slip;

                    if (_tm >= _sit)
                    {
                        if (_rng.NextDouble() < 0.45) Way = -Way;
                        BeginHook(false);
                    }

                    break;
            }

            Creep(dt);
            Spin(dt);
        }

        private void BeginLoop()
        {
            Now = Move.Loop;
            _tm = 0f;
            Wide = true;
            Loops++;

            _loopWay = Way;
            _loopR = _w.LoopRadius;

            var speed = TakeoverRig.LoopSpeed(_loopR, _top);
            _loopOmega = speed / _loopR;
            _loopDrift = TakeoverRig.Drift(_loopR);

            var laps = _rng.NextDouble() < 0.6 ? 1 : 2;
            _turns = Open + laps + Open;
            _loopTotal = TakeoverRig.LoopTime(_turns, _loopOmega, LoopRamp);

            var spot = _w.Spot;
            _theta0 = (float)Math.Atan2(To.Y - spot.Y, To.X - spot.X);
            _r0 = Math.Max(0.6f, Flat(To, spot));

            // Tight enough in at the end that the fresh pivot -- his front axle, wherever he
            // stopped -- is well within the walk back to the spot, even on a long car.
            _rEnd = Between(0.7f, 1.2f);
        }

        /// <summary>Pinned out: a spiral out to the loop, round it, and a spiral back in to a near stop.</summary>
        private void Loop(float dt)
        {
            float rate;
            var gone = TakeoverRig.LoopAngle(_tm, _loopOmega, LoopRamp, _loopTotal, out rate);

            var open = Open * TwoPi;
            var close = _turns * TwoPi - open;

            float r;

            if (gone < open) r = _r0 + (_loopR - _r0) * Smooth(gone / open);
            else if (gone < close) r = _loopR;
            else r = _loopR + (_rEnd - _loopR) * Smooth((gone - close) / open);

            var th = _theta0 + _loopWay * gone;

            To = TakeoverRig.LoopPoint(_w.Spot, r, th);
            Radius = r;

            // The nose comes in as he gets up to speed and straightens as he slows at the end.
            var drift = _loopDrift * Math.Min(1f, rate / _loopOmega);
            var want = TakeoverRig.LoopHeading(th, _loopWay, drift);
            var was = Heading;

            Heading = TakeoverRig.Approach(Heading, want, LoopTurnMost * dt);
            Rate = TakeoverRig.Diff(was, Heading) / dt;

            if (_tm < _loopTotal) return;

            // Back onto his front wheels where he stopped, the same way round, and hooked up
            // from nothing again. The pivot walks the rest of the way to the spot.
            var f = TakeoverRig.Fwd(Heading);
            Pivot = new Vector3(To.X + f.X * _w.Axle, To.Y + f.Y * _w.Axle, To.Z);
            Way = _loopWay;
            _slip = 0f;
            Radius = _w.Axle;

            _nextLoop = _t + Between(25f, 70f);
            if (_nextLoss < _t + 12f) _nextLoss = _t + 12f;

            BeginHook(false);
        }

        // ---- the parts they share -----------------------------------------------------------

        /// <summary>The heading turned, the back stepped, and the middle put where that leaves it.</summary>
        private void Spin(float dt)
        {
            Heading = TakeoverRig.Wrap(Heading + Rate * dt);

            var step = SlipRate * dt;
            var d = _slipAim - _slip;
            if (d > step) d = step;
            if (d < -step) d = -step;
            _slip += d;

            if (_slip > _w.SlipMost) _slip = _w.SlipMost;
            if (_slip < 0f) _slip = 0f;

            Radius = _w.Axle + _slip;
            To = TakeoverRig.DonutMiddle(Pivot, Heading, Radius);
        }

        /// <summary>The pivot walks toward where it wants to be: the spot, plus a slow wander round it.</summary>
        private void Creep(float dt)
        {
            var spot = _w.Spot;
            var wx = _w.Wander * (float)Math.Sin(TwoPi * _t / _w4T + _w4P);
            var wy = _w.Wander * (float)Math.Sin(TwoPi * _t / _w5T + _w5P);

            var ax = spot.X + _w.U.X * wx + _w.V.X * wy;
            var ay = spot.Y + _w.U.Y * wx + _w.V.Y * wy;

            var dx = ax - Pivot.X;
            var dy = ay - Pivot.Y;
            var len = Wedge.Hypot(dx, dy);
            if (len < 0.001f) return;

            var step = Math.Min(len, PivotCreep * dt);
            Pivot = new Vector3(Pivot.X + dx * step / len, Pivot.Y + dy * step / len, Pivot.Z);
        }

        /// <summary>
        /// A nudged pivot kept within its drift of the spot. One that was already further out --
        /// still walking in from where the car stopped -- is let no further out than it was.
        /// </summary>
        private Vector3 Held(Vector3 before, Vector3 after)
        {
            var spot = _w.Spot;
            var was = Flat(before, spot);
            var now = Flat(after, spot);
            var most = Math.Max(_w.Drift, was);

            if (now <= most || now < 0.0001f) return after;

            var s = most / now;
            return new Vector3(spot.X + (after.X - spot.X) * s, spot.Y + (after.Y - spot.Y) * s, after.Z);
        }

        /// <summary>
        /// The net. Where the routine wants him, kept inside the wedge -- and eased there rather
        /// than snapped, so a car that stopped a few metres off its spot slides onto its line
        /// over a couple of seconds instead of being thrown at it.
        /// </summary>
        private void Net(float dt)
        {
            float need;
            var kept = _w.Clamp(To, out need);
            Need = need;

            var wx = kept.X - To.X;
            var wy = kept.Y - To.Y;

            var dx = wx - _push.X;
            var dy = wy - _push.Y;
            var len = Wedge.Hypot(dx, dy);

            if (len > 0.0001f)
            {
                var step = Math.Min(len, NetRate * dt);
                _push = new Vector3(_push.X + dx * step / len, _push.Y + dy * step / len, 0f);
            }

            To = new Vector3(To.X + _push.X, To.Y + _push.Y, To.Z);
        }

        private float Between(float a, float b)
        {
            return a + (b - a) * (float)_rng.NextDouble();
        }

        public static float Smooth(float u)
        {
            if (u <= 0f) return 0f;
            if (u >= 1f) return 1f;
            return u * u * (3f - 2f * u);
        }

        public static float Flat(Vector3 a, Vector3 b)
        {
            return Wedge.Hypot(a.X - b.X, a.Y - b.Y);
        }
    }
}
