using System;
using GTA;
using GTA.Math;
using GTA.Native;

namespace Hoodrich.Paint
{
    /// <summary>Where the spray landed, and which way that wall is facing.</summary>
    internal struct Hit
    {
        public bool Landed;
        public Vector3 At;

        /// <summary>Straight out of the surface. This is the whole reason any of it works.</summary>
        public Vector3 Normal;

        /// <summary>How far the wall is from his SPRAYING HAND. See Nozzle and InFront.</summary>
        public float Away;

        public int Entity;
    }

    /// <summary>
    /// Finding the thing in front of you.
    ///
    /// A decal has to be told three things: where it goes, which way it faces INTO the surface,
    /// and which way is sideways along it. Only the first is obvious, and a decal given a
    /// guessed facing lies flat in mid-air or disappears edge-on into a wall.
    ///
    /// The probe answers all three. GET_SHAPE_TEST_RESULT hands back a surface normal along
    /// with the hit point, which is what makes "every surface" a real claim rather than
    /// "walls, if they happen to run north". Ceilings, kerbs, the side of a skip, the road --
    /// they all come back with a normal and they all take paint the same way.
    /// </summary>
    internal static class Surface
    {
        /// <summary>
        /// Everything solid, and nothing else.
        ///
        /// 1 map, 2 vehicles, 4 peds ... the full set is 511. Peds are deliberately out: paint
        /// that lands on a passer-by is paint that walks off down the street, because a decal
        /// is placed in WORLD space and does not follow what it hit.
        ///
        /// Vehicles are in, for now, and the same caveat applies to them -- see the note in
        /// Sprayer. Whether that is a bug or a feature is a decision for after it has been
        /// looked at rather than before.
        /// </summary>
        private const int Solid = 1 | 2 | 16;

        /// <summary>
        /// What is in front of the camera, out to a given distance.
        ///
        /// SYNCHRONOUS on purpose. The asynchronous probe is the right call for anything that
        /// can wait a frame or two, and this cannot: the paint has to land where the player was
        /// pointing when they pressed, not where they are pointing two frames later, and at
        /// spray rates that is a visibly crooked line. One expensive probe a few times a second
        /// is cheaper than the wrong answer.
        /// </summary>
        public static Hit InFront(float metres)
        {
            var hit = new Hit();

            try
            {
                var cam = GameplayCamera.Position;
                var dir = GameplayCamera.Direction;

                // THE REACH IS FROM THE MAN, NOT FROM THE CAMERA, and that is what was
                // wrong. The ray has to START at the camera, or the paint does not land where
                // the reticle is -- but in third person the camera sits two or three metres
                // BEHIND him, so a four-metre can spent most of its range getting back to his
                // own shoulder and had about a metre left in front of him. Standing a normal
                // distance off a wall there was simply nothing inside the probe, and a probe
                // that hits nothing is indistinguishable from paint that does not work.
                //
                // So the camera's own distance is added back on. The tag run in the other mod
                // casts from the player for this exact reason; this keeps the reticle honest
                // AND gives the advertised reach.
                // From the hand, so that the reach in the ini means what it says: CanRange is
                // how far the can sprays, measured from the can. It used to be measured from
                // his feet, which on a wall you are standing square to is most of a metre of
                // the budget spent going nowhere.
                var lead = Nozzle().DistanceTo(cam);

                var from = cam;
                var to = cam + dir * (metres + lead);

                var me = Game.Player.Character;
                var ignore = me == null || !me.Exists() ? 0 : me.Handle;

                var probe = Function.Call<int>(
                    Hash.START_EXPENSIVE_SYNCHRONOUS_SHAPE_TEST_LOS_PROBE,
                    from.X, from.Y, from.Z, to.X, to.Y, to.Z, Solid, ignore, 7);

                var landed = new OutputArgument();
                var end = new OutputArgument();
                var normal = new OutputArgument();
                var entity = new OutputArgument();

                Function.Call<int>(Hash.GET_SHAPE_TEST_RESULT, probe, landed, end, normal, entity);

                if (!landed.GetResult<bool>()) return hit;

                hit.Landed = true;
                hit.At = end.GetResult<Vector3>();

                // How far it is from HIS HAND, and the hand is the point.
                //
                // This was measured from me2.Position, and a ped's Position is its ROOT --
                // which is on the floor between its feet. So the nearest part of any wall was
                // its skirting board, and the splatter grew with height from there: finest at
                // the bottom of a piece and fattest at the top, on a wall the player was
                // standing square to the whole time. It looks like a deliberate effect until
                // you notice the thinnest paint is always at ankle height.
                //
                // From the can, the geometry comes out right on its own. The closest point of
                // the wall is the one level with his hands, so a piece is finest across the
                // middle where he is actually working and opens up above his reach and below
                // it -- which is what a can does, because that is when the can is furthest from
                // the wall.
                hit.Away = Nozzle().DistanceTo(hit.At);
                hit.Normal = normal.GetResult<Vector3>();
                hit.Entity = entity.GetResult<int>();
            }
            catch
            {
                // A frame with no probe is a frame with no paint. Nothing to clean up.
            }

            return hit;
        }

        /// <summary>
        /// Where the paint is actually leaving from: his right hand.
        ///
        /// The prop helper bone rather than the wrist -- 28422 is PH_R_Hand, the non-deforming
        /// bone the animators hang props off, which is where the can is attached and therefore
        /// where its nozzle is. It moves with the animation, so crouching, leaning and the
        /// first-person pose all come out right without any of them being special cases.
        ///
        /// Falls back to a metre above his feet, which is roughly where a hand is on a standing
        /// man -- still far better than the floor, which is what this used to use.
        /// </summary>
        private static Vector3 Nozzle()
        {
            var me = Game.Player.Character;

            if (me == null || !me.Exists()) return GameplayCamera.Position;

            try
            {
                var at = Function.Call<Vector3>(Hash.GET_PED_BONE_COORDS, me.Handle, RightHand,
                                                0f, 0f, 0f);

                // A bone that is not there comes back as the world origin, which would put
                // every wall in Los Santos a few thousand metres away.
                if (at != Vector3.Zero) return at;
            }
            catch
            {
                // Fall through to the estimate.
            }

            return me.Position + new Vector3(0f, 0f, 1f);
        }

        /// <summary>PH_R_Hand. The same bone the can is attached to -- see Spraycan.</summary>
        private const int RightHand = 28422;

        /// <summary>
        /// Any direction lying flat along the surface.
        ///
        /// ADD_DECAL wants a side vector as well as a facing, and any perpendicular will do --
        /// it only decides which way round the splatter sits, and a splatter has no up.
        ///
        /// Crossed with world up, except when the surface IS pointing up, where that cross
        /// product collapses to nothing and every decal on every road and every rooftop would
        /// come out as a zero-size nothing. Those get crossed with north instead.
        /// </summary>
        public static Vector3 Along(Vector3 normal)
        {
            var up = new Vector3(0f, 0f, 1f);

            var side = Vector3.Cross(normal, up);

            if (side.LengthSquared() < 0.001f)
            {
                side = Vector3.Cross(normal, new Vector3(0f, 1f, 0f));
            }

            side.Normalize();
            return side;
        }

        /// <summary>
        /// The same direction, spun round the surface by a given angle.
        ///
        /// WITHOUT THIS EVERY SPLATTER IS THE SAME SPLATTER. Along() is deterministic -- it
        /// crosses the normal with world up -- so every mark on a given wall came out at the
        /// identical angle, and a wall covered in one blob repeated fifty times reads as a
        /// tiling texture rather than as paint. Size jitter alone does not hide it, because
        /// the eye picks up the repeated shape long before it notices the scale.
        ///
        /// side and the vector below are both unit and both perpendicular to the normal, so
        /// they span the plane the decal lies in. Rotating inside that plane is therefore
        /// Rodrigues with its third term dropped: that term needs the component of side along
        /// the axis, and here it is zero by construction.
        /// </summary>
        public static Vector3 Along(Vector3 normal, float turn)
        {
            var side = Along(normal);

            var up = Vector3.Cross(normal, side);
            up.Normalize();

            var c = (float)Math.Cos(turn);
            var s = (float)Math.Sin(turn);

            var spun = side * c + up * s;
            spun.Normalize();

            return spun;
        }
    }
}
