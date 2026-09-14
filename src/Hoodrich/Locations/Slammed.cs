using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using GTA;

namespace Hoodrich.Locations
{
    /// <summary>
    /// A rear axle sat down and leaned in, on one car, for as long as it is parked there.
    ///
    /// THIS MOD DOES IT ITSELF AND THAT IS THE POINT. The stance could have been asked of the
    /// other mod on this machine that does suspensions properly, and for one afternoon it was
    /// -- a model named in its ini, which meant every Dorado in Los Santos, and then a model
    /// and a coordinate, which meant the right one. Both are a car in Posted Up that only sits
    /// right if somebody has a second mod installed, and Posted Up does not have dependencies.
    /// So this is ours, it is small, and it is deliberately much less than theirs: no camber on
    /// the front, no track, no wheel sizes, no menu. One axle, two numbers, one car.
    ///
    /// WHAT A WHEEL IS IN MEMORY. SHVDN hands out the address and nothing else -- there is no
    /// Camber property and no Height property, which is why this is offsets rather than a
    /// setter. A wheel keeps the two rows of its own little matrix at 0x000..0x018, the top of
    /// its suspension line at 0x020 and the bottom at 0x030.
    ///
    /// AND THE GAME ARGUES ABOUT HALF OF THEM. The cosines hold; the sines are put back by the
    /// car's own code, so they are written again every frame. That is why this is ticked from
    /// ParkedCar's per-frame half rather than from behind its throttle -- a camber written once
    /// a second and a half is a wheel that flickers straight for most of them.
    ///
    /// HEIGHT IS NOT A PLACE, IT IS AN OFFSET, and this is the part that is not obvious. The
    /// bottom of the suspension line is where the wheel IS this frame and the game moves it as
    /// the suspension works; pinned to one number the wheel would be held at full droop with
    /// the body floating on top of it. So it is read, the drop is taken off, and that goes
    /// back. Which then needs the bookkeeping below, because the next pass would otherwise read
    /// its own answer and take the drop off again, and again, and walk the car into the road.
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

        private readonly Dictionary<long, Sat> _sat = new Dictionary<long, Sat>();

        /// <summary>
        /// Puts the rear axle where it was asked for. Called every frame, for one car.
        /// </summary>
        /// <param name="car">The car. Nothing happens to a car that is not there.</param>
        /// <param name="camber">Degrees of lean on the rear wheels. Negative is tucked in at the top.</param>
        /// <param name="drop">Metres the rear sits down by. Negative lowers it.</param>
        public void Hold(Vehicle car, float camber, float drop)
        {
            if (car == null || !car.Exists()) return;
            if (Math.Abs(camber) < Nothing && Math.Abs(drop) < Nothing) return;

            try
            {
                var lean = camber * (float)(Math.PI / 180.0);

                foreach (var wheel in car.Wheels)
                {
                    var id = (int)wheel.BoneId;

                    // THE FRONT AXLE IS LEFT ENTIRELY ALONE. A six-wheeler's middle axle follows
                    // the rear, which is what it looks like anyway.
                    if (id == (int)VehicleWheelBoneId.WheelLeftFront ||
                        id == (int)VehicleWheelBoneId.WheelRightFront)
                    {
                        continue;
                    }

                    var at = wheel.MemoryAddress;
                    if (at == IntPtr.Zero) continue;

                    var key = at.ToInt64();

                    if (Math.Abs(lean) >= Nothing) Lean(at, key, id, lean);
                    if (Math.Abs(drop) >= Nothing) Lower(at, key, drop);
                }
            }
            catch
            {
                // A car whose wheels cannot be read is a car with ordinary wheels, which is not
                // a crash and is not worth a log line sixty times a second.
            }
        }

        /// <summary>
        /// Gives the rear wheels back to the game, upright and at whatever height it says.
        ///
        /// THE HEIGHT NEEDS NOTHING DOING: it is an offset on a number the game is writing
        /// every frame anyway, so the moment this stops taking the drop off it, the suspension
        /// is simply the suspension again. The lean does, because the cosines HOLD -- that is
        /// the half of it the car does not argue about -- and a wheel left leaning with nothing
        /// maintaining it is a wheel that stays bent for the rest of the session.
        /// </summary>
        public void Let(Vehicle car)
        {
            if (car == null || !car.Exists() || _came.Count == 0) return;

            try
            {
                foreach (var wheel in car.Wheels)
                {
                    var at = wheel.MemoryAddress;
                    if (at == IntPtr.Zero) continue;

                    float came;
                    if (!_came.TryGetValue(at.ToInt64(), out came)) continue;

                    var s = (float)Math.Sin(came);
                    var c = (float)Math.Cos(came);

                    Write(at, CosX, c);
                    Write(at, CosZ, c);
                    Write(at, Sin, s);
                    Write(at, SinBack, -s);
                }
            }
            catch
            {
                // The game puts the sines back on its own; the cosines are the loss.
            }

            _sat.Clear();
        }

        /// <summary>Forgets a car's wheels. Their addresses belong to something else now.</summary>
        public void Forget()
        {
            _came.Clear();
            _sat.Clear();
        }

        /// <summary>
        /// The lean, written from the angle the wheel came with rather than the one it has.
        ///
        /// Left is odd -- 11 is the left front, 12 the right front, 13 the left rear -- and the
        /// two sides lean opposite ways to mean the same thing, so the side carries the sign.
        /// </summary>
        private void Lean(IntPtr at, long key, int id, float lean)
        {
            float came;

            if (!_came.TryGetValue(key, out came))
            {
                var sin = Read(at, Sin);

                // Not a sine, so not a wheel. Say nothing and touch nothing.
                if (float.IsNaN(sin) || Math.Abs(sin) > Sane) return;
                if (sin > 1f) sin = 1f;
                else if (sin < -1f) sin = -1f;

                came = (float)Math.Asin(sin);
                _came[key] = came;
            }

            var side = (id & 1) == 1 ? 1f : -1f;
            var angle = came + side * lean;

            var s = (float)Math.Sin(angle);
            var c = (float)Math.Cos(angle);

            // The pair that holds, and then the pair the car puts back.
            Write(at, CosX, c);
            Write(at, CosZ, c);
            Write(at, Sin, s);
            Write(at, SinBack, -s);
        }

        /// <summary>
        /// Moves the wheel by an offset from wherever the suspension has just put it.
        ///
        /// OURS OR THE GAME'S? If the field still holds the exact bits this last wrote, the game
        /// has not been round since and there is nothing to do -- taking the drop off again
        /// would lower the car another few centimetres every frame until it was through the
        /// road. The comparison is exact because the read gives back the very bits written.
        ///
        /// AND A PARKED CAR IS NEVER GOING ROUND AGAIN, which is the other half. A suspension at
        /// rest is one the game has stopped writing, so every pass on a car sat outside a shop
        /// reads our own number. The drop that made it is remembered beside it, so changing the
        /// drop still works: the car's own number is what we wrote plus what we took off it.
        /// </summary>
        private void Lower(IntPtr at, long key, float drop)
        {
            var now = Read(at, BottomZ);

            if (float.IsNaN(now) || Math.Abs(now) > Sane) return;

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
