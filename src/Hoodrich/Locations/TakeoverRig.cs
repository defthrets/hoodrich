using System;
using GTA.Math;

namespace Hoodrich.Locations
{
    /// <summary>
    /// The geometry of a donut and a loop, and nothing else.
    ///
    /// NO NATIVES IN HERE, so all of it can be run offline against the junctions' real marks
    /// and checked -- that no two cars ever touch, that nobody reaches the crowd, that a loop
    /// ends where it began -- before a car is ever put on it. Takeover does the driving.
    ///
    /// Headings are the game's: degrees, nought is north, and they go up turning LEFT. A car
    /// on heading h points along (-sin h, cos h).
    /// </summary>
    internal static class TakeoverRig
    {
        public const float Deg = (float)(Math.PI / 180.0);

        /// <summary>The way a heading points, on the ground.</summary>
        public static Vector3 Fwd(float heading)
        {
            var h = heading * Deg;
            return new Vector3(-(float)Math.Sin(h), (float)Math.Cos(h), 0f);
        }

        /// <summary>The heading of a direction on the ground.</summary>
        public static float HeadingOf(Vector3 v)
        {
            return Wrap((float)(Math.Atan2(-v.X, v.Y) / Deg));
        }

        public static float Wrap(float deg)
        {
            deg %= 360f;
            return deg < 0f ? deg + 360f : deg;
        }

        /// <summary>The short way round from one heading to another, -180 to 180.</summary>
        public static float Diff(float from, float to)
        {
            var d = Wrap(to - from);
            return d > 180f ? d - 360f : d;
        }

        /// <summary>A heading turned toward another by no more than a step.</summary>
        public static float Approach(float from, float to, float most)
        {
            var d = Diff(from, to);
            if (d > most) d = most;
            if (d < -most) d = -most;
            return Wrap(from + d);
        }

        // ---- the donut -----------------------------------------------------------------

        /// <summary>
        /// Where the middle of the car is when its front axle is on the pivot. A donut turns on
        /// the front wheels and swings the back round them, so the pivot is the axle, not the
        /// middle of the car -- the middle goes round it.
        /// </summary>
        public static Vector3 DonutMiddle(Vector3 pivot, float heading, float axle)
        {
            var f = Fwd(heading);
            return new Vector3(pivot.X - f.X * axle, pivot.Y - f.Y * axle, pivot.Z);
        }

        /// <summary>How fast the middle of the car moves while it turns on the pivot.</summary>
        public static Vector3 DonutVelocity(float heading, float axle, float degPerSecond)
        {
            var h = heading * Deg;
            var rate = degPerSecond * Deg;
            return new Vector3((float)Math.Cos(h) * axle * rate, (float)Math.Sin(h) * axle * rate, 0f);
        }

        // ---- the loop ------------------------------------------------------------------

        /// <summary>A point on a loop, at an angle round it (radians, anticlockwise from east).</summary>
        public static Vector3 LoopPoint(Vector3 centre, float radius, float theta)
        {
            return new Vector3(centre.X + (float)Math.Cos(theta) * radius,
                               centre.Y + (float)Math.Sin(theta) * radius,
                               centre.Z);
        }

        /// <summary>The way round the loop, at that angle, going the given way (+1 anticlockwise).</summary>
        public static Vector3 LoopTangent(float theta, int way)
        {
            return new Vector3(-(float)Math.Sin(theta) * way, (float)Math.Cos(theta) * way, 0f);
        }

        /// <summary>
        /// Which way the car points on the loop: along it, with the nose turned in by the drift
        /// angle -- into the circle, which is what makes it a car going sideways round a loop
        /// rather than one driving round a roundabout.
        /// </summary>
        public static float LoopHeading(float theta, int way, float drift)
        {
            return Wrap(HeadingOf(LoopTangent(theta, way)) + way * drift);
        }

        /// <summary>
        /// How fast round a loop of this size. As fast as the setting allows, but never faster
        /// than a car could plausibly be thrown round it -- a three-metre loop at ten metres a
        /// second is three g, and it reads as a toy on a string.
        /// </summary>
        public static float LoopSpeed(float radius, float most)
        {
            var s = (float)Math.Sqrt(LoopGrip * radius);
            return Math.Min(most, s);
        }

        /// <summary>Sideways acceleration a loop is driven at: a hard-driven car, not a toy.</summary>
        public const float LoopGrip = 14f;

        /// <summary>
        /// How far the nose is turned in. More on a tight loop, less on a wide one -- a car on a
        /// three-metre loop is nearly side-on to where it is going, one on eight is not.
        /// </summary>
        public static float Drift(float radius)
        {
            var d = 70f - 4f * radius;
            if (d < 30f) d = 30f;
            if (d > 60f) d = 60f;
            return d;
        }

        /// <summary>
        /// How far a car's place round its loop may differ from the others', in radians.
        ///
        /// THE CARS LOOP TOGETHER, and that is what keeps them off each other. Each loops round
        /// its own mark, and if they all go the same way at the same rate from the same angle,
        /// every car is the one next to it moved over by the distance between their marks -- so
        /// the gap between them never changes at all, however big the loops. A little of each
        /// car's own on top, so it is not a formation; little enough that no two ever close by
        /// more than a metre and a half.
        /// </summary>
        public static float JitterMost(float radius)
        {
            return Math.Min(0.4f, 1.5f / Math.Max(1f, radius));
        }

        /// <summary>
        /// How far round a loop has gone after t seconds, and how fast it is going: up to speed
        /// over the ramp, round, and down again over the ramp, ending exactly on a whole number of
        /// laps. Plain arithmetic, so it is the same whatever the frame rate.
        /// </summary>
        public static float LoopAngle(float t, float omega, float ramp, float total, out float rate)
        {
            if (t <= 0f)
            {
                rate = 0f;
                return 0f;
            }

            if (t < ramp)
            {
                rate = omega * t / ramp;
                return omega * t * t / (2f * ramp);
            }

            var cruise = total - 2f * ramp;

            if (t < ramp + cruise)
            {
                rate = omega;
                return omega * ramp * 0.5f + omega * (t - ramp);
            }

            var end = omega * (cruise + ramp);

            if (t < total)
            {
                var u = total - t;
                rate = omega * u / ramp;
                return end - omega * u * u / (2f * ramp);
            }

            rate = 0f;
            return end;
        }

        /// <summary>How long a loop of so many laps takes, ramps and all.</summary>
        public static float LoopTime(int laps, float omega, float ramp)
        {
            var cruise = laps * 2f * (float)Math.PI / omega - ramp;
            if (cruise < 0f) cruise = 0f;
            return cruise + 2f * ramp;
        }
    }
}
