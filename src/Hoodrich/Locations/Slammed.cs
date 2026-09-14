using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using GTA;

namespace Hoodrich.Locations
{
    /// <summary>
    /// A rear axle sat down and leaned in, on one car, for as long as that car exists.
    ///
    /// THIS MOD DOES IT ITSELF AND THAT IS THE POINT. The stance could have been asked of the
    /// other mod on this machine that does suspensions properly, and for one afternoon it was
    /// -- a model named in its ini, which meant every Dorado in Los Santos, and then a model
    /// and a coordinate, which meant the right one. Both are a car in Posted Up that only sits
    /// right if somebody has a second mod installed, and Posted Up does not have dependencies.
    /// So this is ours, and it is deliberately much less than theirs: no camber on the front,
    /// no track, no wheel sizes, no menu. One axle, two numbers, one car.
    ///
    /// WHAT A WHEEL IS IN MEMORY. SHVDN hands out the address and nothing else -- there is no
    /// Camber property and no Height property, which is why this is offsets rather than a
    /// setter. A wheel keeps the two rows of its own little matrix at 0x000..0x018 and the
    /// bottom of its suspension line at 0x038.
    ///
    /// AND THE GAME ARGUES ABOUT THE ONES THAT MATTER. The cosines hold. The sines and the
    /// bottom of the suspension line are written back by the car's own code, at its own point
    /// in the frame -- so a script tick, which lands once a frame wherever it lands, wins about
    /// half the time. Standing still that is invisible, because a settled suspension is one the
    /// game has stopped writing. Moving, it is the back end juddering: the wheel takes the
    /// game's number one frame and ours the next.
    ///
    /// WHICH IS WHY THERE IS A THREAD. It does one thing -- write the handful of fields the
    /// game keeps taking back, as often as it can -- and it is the only way to hold a stance on
    /// a car that is being driven. The tick still writes the ones that hold.
    ///
    /// NOTHING IS SHARED EXCEPT ONE REFERENCE. The tick builds a whole new array of what to
    /// write and swaps it in; the thread only ever reads the current one. There is no lock, no
    /// list being added to while it is read, and no half-updated entry -- an array is either
    /// the old one or the new one.
    ///
    /// AND IT IS POINTERS INTO THE GAME, so the window between a car despawning and the tick
    /// publishing an empty array is the dangerous one. The tick republishes every frame and
    /// clears on the same frame the car goes, which makes that window one frame wide.
    ///
    /// HEIGHT IS NOT A PLACE, IT IS AN OFFSET, and this is the part that is not obvious. The
    /// bottom of the suspension line is where the wheel IS this frame and the game moves it as
    /// the suspension works; pinned to one number the wheel would be held at full droop with
    /// the body floating on top of it. So it is read, the drop taken off, and that written
    /// back -- with the bookkeeping to say which number was ours, or the next pass reads its own
    /// answer, takes the drop off again, and walks the car into the road. That ratchet is not
    /// hypothetical: clearing the bookkeeping while the car was standing still is exactly how
    /// the back end got lower every time it was parked.
    /// </summary>
    internal sealed class Slammed
    {
        /// <summary>The two rows of the wheel's own matrix: cosines hold, sines are argued over.</summary>
        private const int CosX = 0x000;
        private const int Sin = 0x008;
        private const int SinBack = 0x010;
        private const int CosZ = 0x018;

        /// <summary>The bottom of the suspension line -- where the wheel is, this frame.</summary>
        private const int BottomZ = 0x038;

        /// <summary>
        /// A wheel does not lean by five radians and does not sit five metres out.
        ///
        /// Not a limit on the setting: a test of the ADDRESS. If what is already there is not a
        /// small number then the pointer is not a wheel, and nothing is written to it.
        /// </summary>
        private const float Sane = 5f;

        /// <summary>Below this, a number is the number that means "leave it alone".</summary>
        private const float Nothing = 0.0005f;

        /// <summary>One wheel's worth of what the thread has to keep writing.</summary>
        private sealed class Job
        {
            public IntPtr At;

            /// <summary>The sine of the leaned angle, and its negation. NaN leaves them alone.</summary>
            public float Lean;

            /// <summary>Metres off the bottom of the line. Nought leaves it alone.</summary>
            public float Drop;
        }

        /// <summary>
        /// What to write, swapped in whole by the tick and only read by the thread.
        ///
        /// Volatile because two threads look at it and one of them writes it. The array itself
        /// is never changed after it is built.
        /// </summary>
        private volatile Job[] _jobs = new Job[0];

        /// <summary>
        /// The lean each wheel came with, kept because it cannot be read back.
        ///
        /// After the first write the sine at 0x008 is OURS, so asking the car what its camber is
        /// answers with what we last told it -- and a stance built on that walks a degree
        /// further over every frame. Taken once, the first time this wheel is seen.
        /// </summary>
        private readonly Dictionary<long, float> _came = new Dictionary<long, float>();

        /// <summary>What was last written to a wheel's height, and the drop that made it.</summary>
        private sealed class Sat
        {
            public float Wrote;
            public float Drop;
        }

        /// <summary>Owned by the thread once it is running. See Lower.</summary>
        private readonly Dictionary<long, Sat> _sat = new Dictionary<long, Sat>();

        private Thread _hand;
        private volatile bool _stop;

        /// <summary>
        /// Puts the rear axle where it was asked for. Called every frame, for one car.
        /// </summary>
        /// <param name="car">The car. Nothing happens to a car that is not there.</param>
        /// <param name="camber">Degrees of lean on the rear wheels. Negative is tucked in at the top.</param>
        /// <param name="drop">Metres the rear sits down by. Negative lowers it.</param>
        /// <param name="nose">Metres the FRONT sits by. Positive raises it, and that is the rake.</param>
        public void Hold(Vehicle car, float camber, float drop, float nose = 0f)
        {
            var leaning = Math.Abs(camber) >= Nothing;
            var lowering = Math.Abs(drop) >= Nothing;
            var lifting = Math.Abs(nose) >= Nothing;

            if (car == null || !car.Exists() || (!leaning && !lowering && !lifting))
            {
                _jobs = new Job[0];
                return;
            }

            try
            {
                var lean = camber * (float)(Math.PI / 180.0);
                var made = new List<Job>(4);

                foreach (var wheel in car.Wheels)
                {
                    var id = (int)wheel.BoneId;

                    // FRONT IS THE FIRST AXLE AND EVERYTHING ELSE IS THE REAR. A six-wheeler's
                    // middle axle has no number of its own and follows the back, which is what
                    // it looks like anyway.
                    var front = id == (int)VehicleWheelBoneId.WheelLeftFront ||
                                id == (int)VehicleWheelBoneId.WheelRightFront;

                    // THE FRONT ONLY EVER GETS A HEIGHT. The lean is a rear-axle idea here --
                    // a car sat down at the back with its wheels tucked under it -- and a front
                    // wheel leaning to match is a different and much more expensive-looking car.
                    if (front && !lifting) continue;

                    var at = wheel.MemoryAddress;
                    if (at == IntPtr.Zero) continue;

                    var job = new Job
                    {
                        At = at,
                        Lean = float.NaN,
                        Drop = front ? nose : (lowering ? drop : 0f),
                    };

                    if (leaning && !front)
                    {
                        var angle = Came(at, id, lean);

                        if (!float.IsNaN(angle))
                        {
                            var s = (float)Math.Sin(angle);

                            // THE ONES THAT HOLD, FROM HERE. Written once a frame is plenty for
                            // a field nothing else touches.
                            Write(at, CosX, (float)Math.Cos(angle));
                            Write(at, CosZ, (float)Math.Cos(angle));

                            job.Lean = s;
                        }
                    }

                    made.Add(job);
                }

                _jobs = made.ToArray();

                Start();
            }
            catch
            {
                // A car whose wheels cannot be read is a car with ordinary wheels, which is not
                // a crash and is not worth a log line sixty times a second.
            }
        }

        /// <summary>
        /// The leaned angle for a wheel, off the one it came with. NaN for anything that is not
        /// a wheel at that address.
        /// </summary>
        private float Came(IntPtr at, int id, float lean)
        {
            float came;
            var key = at.ToInt64();

            if (!_came.TryGetValue(key, out came))
            {
                var sin = Read(at, Sin);

                if (float.IsNaN(sin) || Math.Abs(sin) > Sane) return float.NaN;

                if (sin > 1f) sin = 1f;
                else if (sin < -1f) sin = -1f;

                came = (float)Math.Asin(sin);
                _came[key] = came;
            }

            // Left is odd -- 11 is the left front, 12 the right front, 13 the left rear -- and
            // the two sides lean opposite ways to mean the same thing.
            var side = (id & 1) == 1 ? 1f : -1f;

            return came + side * lean;
        }

        /// <summary>
        /// Stops writing and lets the car have its wheels back.
        ///
        /// THE HEIGHT IS PUT BACK RATHER THAN FORGOTTEN, and that distinction is a bug this
        /// already had. A standing car's suspension is one the game has stopped writing, so
        /// simply dropping the bookkeeping leaves the lowered number sitting there as if the car
        /// had always been that low -- and the next time the stance is applied it reads that as
        /// the car's own height and takes the drop off it again. Every stop was a centimetre.
        /// </summary>
        public void Let(Vehicle car)
        {
            _jobs = new Job[0];

            if (car == null || !car.Exists()) { _sat.Clear(); return; }

            try
            {
                foreach (var wheel in car.Wheels)
                {
                    var at = wheel.MemoryAddress;
                    if (at == IntPtr.Zero) continue;

                    var key = at.ToInt64();

                    float came;

                    if (_came.TryGetValue(key, out came))
                    {
                        var s = (float)Math.Sin(came);
                        var c = (float)Math.Cos(came);

                        Write(at, CosX, c);
                        Write(at, CosZ, c);
                        Write(at, Sin, s);
                        Write(at, SinBack, -s);
                    }

                    Sat mine;

                    if (_sat.TryGetValue(key, out mine))
                    {
                        // Only if it is still ours. If the game has moved it since, it is the
                        // game's number already and writing anything would be a guess.
                        if (Read(at, BottomZ) == mine.Wrote) Write(at, BottomZ, mine.Wrote + mine.Drop);

                        _sat.Remove(key);
                    }
                }
            }
            catch
            {
                // The game puts the sines and the line back on its own soon enough.
            }
        }

        /// <summary>
        /// Stops writing without giving anything back or forgetting anything.
        ///
        /// FOR THE FEW SECONDS SOMEBODY IS CLIMBING IN. Getting into a car is an animation
        /// pinned to the car, and the car is being moved under it -- this thread rewrites the
        /// bottom of the suspension line about a thousand times a second, and every one of
        /// those is the body shifting a centimetre and a half while a man has his hand on the
        /// roof. Let would be wrong here: it hands the height back, so the car would drop as
        /// the door opened and rise again as it shut. This simply stops, and the bookkeeping
        /// survives so it picks up exactly where it was.
        /// </summary>
        public void Pause()
        {
            _jobs = new Job[0];
        }

        /// <summary>Forgets a car's wheels. Their addresses belong to something else now.</summary>
        public void Forget()
        {
            _jobs = new Job[0];

            _came.Clear();
            _sat.Clear();
        }

        /// <summary>Ends the thread. For teardown, when the mod is going away.</summary>
        public void Stop()
        {
            _stop = true;
            _jobs = new Job[0];
            _hand = null;
        }

        /// <summary>
        /// The thread, started the first time there is anything for it to do.
        ///
        /// BACKGROUND, so it cannot hold the game open if anything here ever gets it wrong, and
        /// below normal priority because it is a busy loop and the game is the thing that
        /// matters. It sleeps a millisecond a pass, which is several times a frame -- enough to
        /// win the argument, not enough to be a core.
        /// </summary>
        private void Start()
        {
            if (_hand != null || _stop) return;

            _hand = new Thread(Race)
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = "hoodrich-stance",
            };

            _hand.Start();

            Core.Log.Info("The stance thread is up.");
        }

        private void Race()
        {
            while (!_stop)
            {
                try
                {
                    var jobs = _jobs;

                    for (var i = 0; i < jobs.Length; i++)
                    {
                        var job = jobs[i];

                        if (job == null || job.At == IntPtr.Zero) continue;

                        if (!float.IsNaN(job.Lean))
                        {
                            Write(job.At, Sin, job.Lean);
                            Write(job.At, SinBack, -job.Lean);
                        }

                        if (Math.Abs(job.Drop) >= Nothing) Lower(job.At, job.Drop);
                    }
                }
                catch
                {
                    // A wheel that has gone while we were looking at it. The tick will publish
                    // an empty list on the next frame.
                }

                Thread.Sleep(1);
            }
        }

        /// <summary>
        /// Moves the wheel by an offset from wherever the suspension has just put it.
        ///
        /// OURS OR THE GAME'S? If the field still holds the exact bits this last wrote, the game
        /// has not been round since and there is nothing to do -- taking the drop off again
        /// would lower the car another few centimetres every pass until it was through the road.
        /// The comparison is exact because the read gives back the very bits written.
        ///
        /// AND A PARKED CAR IS NEVER GOING ROUND AGAIN, which is the other half. A suspension at
        /// rest is one the game has stopped writing, so every pass on a car sat outside a shop
        /// reads our own number. The drop that made it is remembered beside it, so changing the
        /// drop still works: the car's own number is what we wrote plus what we took off it.
        /// </summary>
        private void Lower(IntPtr at, float drop)
        {
            var now = Read(at, BottomZ);

            if (float.IsNaN(now) || Math.Abs(now) > Sane) return;

            var key = at.ToInt64();

            Sat mine;
            var known = _sat.TryGetValue(key, out mine);

            float own;

            if (known && now == mine.Wrote)
            {
                if (Math.Abs(drop - mine.Drop) < 0.0000001f) return;

                own = now + mine.Drop;
            }
            else
            {
                own = now;
            }

            var wrote = own - drop;

            Write(at, BottomZ, wrote);

            if (!known)
            {
                mine = new Sat();
                _sat[key] = mine;
            }

            mine.Wrote = wrote;
            mine.Drop = drop;
        }

        private static float Read(IntPtr at, int offset)
        {
            return BitConverter.ToSingle(BitConverter.GetBytes(Marshal.ReadInt32(at, offset)), 0);
        }

        private static void Write(IntPtr at, int offset, float value)
        {
            Marshal.WriteInt32(at, offset, BitConverter.ToInt32(BitConverter.GetBytes(value), 0));
        }
    }
}
