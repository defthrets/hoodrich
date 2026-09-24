using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;

namespace Hoodrich.Gangs
{
    /// <summary>
    /// The game's answers to the park survey's questions. See Park and IParkWorld.
    ///
    /// Everything here is a raycast, an entity scan or a lookup the game already has, and all
    /// of it is wrapped: a question that throws is answered with the safe no -- no ground, a
    /// wall in the way -- because a square nobody rides to costs nothing and a line through a
    /// ramp costs a crash.
    /// </summary>
    internal sealed class ParkWorld : IParkWorld
    {
        /// <summary>Map and placed things, for the ground. Not cars: a car roof is not ground.</summary>
        private const IntersectFlags GroundFlags = IntersectFlags.Map | IntersectFlags.Objects;

        /// <summary>Map, placed things and cars, for a line a bike would ride.</summary>
        private const IntersectFlags LineFlags = IntersectFlags.Map | IntersectFlags.Objects | IntersectFlags.Vehicles;

        private readonly Func<Vector3, bool> _ours;
        private readonly Func<Vehicle, bool> _moving;

        /// <summary>Model sizes, asked once per model rather than once per prop per survey.</summary>
        private readonly Dictionary<int, Vector3[]> _sizes = new Dictionary<int, Vector3[]>();

        private HashSet<int> _features;

        public ParkWorld(Func<Vector3, bool> ours, Func<Vehicle, bool> moving)
        {
            _ours = ours;
            _moving = moving;
        }

        public bool Ground(float x, float y, float fromZ, float toZ, out float z, out bool onThing)
        {
            z = 0f;
            onThing = false;

            try
            {
                var hit = World.Raycast(new Vector3(x, y, fromZ), new Vector3(x, y, toZ), GroundFlags);
                if (!hit.DidHit) return false;

                z = hit.HitPosition.Z;

                var what = hit.HitEntity;
                onThing = what != null && what.Exists();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool Hits(Vector3 a, Vector3 b)
        {
            try
            {
                return World.Raycast(a, b, LineFlags).DidHit;
            }
            catch
            {
                return true;
            }
        }

        public bool OnRoad(Vector3 at)
        {
            try
            {
                return Function.Call<bool>(Hash.IS_POINT_ON_ROAD, at.X, at.Y, at.Z, 0);
            }
            catch
            {
                return false;
            }
        }

        public bool Ours(Vector3 at)
        {
            try { return _ours == null || _ours(at); }
            catch { return false; }
        }

        public float ToRoad(Vector3 at)
        {
            try
            {
                var street = World.GetNextPositionOnStreet(at);
                return street == Vector3.Zero ? -1f : Park.Flat2(street, at);
            }
            catch
            {
                return -1f;
            }
        }

        /// <summary>
        /// Every prop and parked car near the park, as a box on the ground.
        ///
        /// FROM THE MODEL'S OWN SIZE, TURNED THE WAY THE THING IS TURNED. A kicker ramp is three
        /// metres long and a metre wide; treating it as a circle either makes it fat enough to
        /// close the path beside it or thin enough to ride into the end of it. The box is the
        /// model's dimensions laid along the entity's own right and forward, and projected onto
        /// the ground properly if the thing is tipped -- a quarter pipe stood on its side still
        /// covers the ground it covers.
        ///
        /// Left out: anything attached to something (a phone in a hand), anything with its
        /// collision off, cars that are moving (the rays see those at the time), and things
        /// small enough to ride over. People are left to the riders' own eyes -- they move.
        /// </summary>
        public List<Block> Things(Vector3 centre, float radius)
        {
            var list = new List<Block>();

            if (_features == null)
            {
                _features = new HashSet<int>();

                foreach (var name in Features)
                {
                    try { _features.Add(Function.Call<int>(Hash.GET_HASH_KEY, name)); }
                    catch { }
                }
            }

            try
            {
                foreach (var prop in World.GetNearbyProps(centre, radius))
                {
                    Add(list, prop, false);
                }
            }
            catch
            {
                // Whatever was added before it threw is still worth avoiding.
            }

            try
            {
                foreach (var car in World.GetNearbyVehicles(centre, radius))
                {
                    if (car == null || !car.Exists()) continue;
                    if (_moving != null && _moving(car)) continue;

                    Add(list, car, true);
                }
            }
            catch
            {
            }

            return list;
        }

        private void Add(List<Block> list, Entity e, bool car)
        {
            try
            {
                if (e == null || !e.Exists()) return;
                if (Function.Call<bool>(Hash.IS_ENTITY_ATTACHED, e.Handle)) return;
                if (Function.Call<bool>(Hash.GET_ENTITY_COLLISION_DISABLED, e.Handle)) return;

                var hash = e.Model.Hash;

                Vector3[] size;
                if (!_sizes.TryGetValue(hash, out size))
                {
                    var min = new OutputArgument();
                    var max = new OutputArgument();
                    Function.Call(Hash.GET_MODEL_DIMENSIONS, hash, min, max);

                    size = new[] { min.GetResult<Vector3>(), max.GetResult<Vector3>() };
                    _sizes[hash] = size;
                }

                var lo = size[0];
                var hi = size[1];

                var hx = (hi.X - lo.X) * 0.5f;
                var hy = (hi.Y - lo.Y) * 0.5f;
                var hz = (hi.Z - lo.Z) * 0.5f;

                // Rubbish, cans, a skateboard: ridden over, not round.
                if (hx < 0.2f && hy < 0.2f && hz < 0.15f) return;

                var right = e.RightVector;
                var fwd = e.ForwardVector;
                var up = e.UpVector;

                var mid = (lo + hi) * 0.5f;
                var at = e.Position + right * mid.X + fwd * mid.Y + up * mid.Z;

                // The ground axis the box is laid along: its own right, or its forward if it is
                // stood on end and has no right to speak of on the ground.
                var ax = right.X;
                var ay = right.Y;

                if (ax * ax + ay * ay < 0.01f)
                {
                    ax = fwd.X;
                    ay = fwd.Y;
                }

                var len = (float)Math.Sqrt(ax * ax + ay * ay);
                if (len < 1e-4f) return;

                ax /= len;
                ay /= len;

                var bx = -ay;
                var by = ax;

                // Half its size along each ground axis, from all three of its own axes: exact
                // for the box as it actually stands, tipped or not.
                var ha = Math.Abs(right.X * ax + right.Y * ay) * hx
                       + Math.Abs(fwd.X * ax + fwd.Y * ay) * hy
                       + Math.Abs(up.X * ax + up.Y * ay) * hz;

                var hb = Math.Abs(right.X * bx + right.Y * by) * hx
                       + Math.Abs(fwd.X * bx + fwd.Y * by) * hy
                       + Math.Abs(up.X * bx + up.Y * by) * hz;

                var hh = Math.Abs(right.Z) * hx + Math.Abs(fwd.Z) * hy + Math.Abs(up.Z) * hz;

                list.Add(Block.Make(at.X, at.Y, ax, ay, ha, hb, at.Z - hh, at.Z + hh,
                                    !car && _features.Contains(hash)));
            }
            catch
            {
                // One thing that could not be measured. The rays still see it.
            }
        }

        /// <summary>
        /// The things worth riding past: the skate ramps that were put in the park, by the names
        /// they are saved under. Anything else is only something to go round.
        /// </summary>
        private static readonly string[] Features =
        {
            "prop_skate_flatramp", "prop_skate_flatramp_cr",
            "prop_skate_kickers", "prop_skate_kickers_cr",
            "prop_skate_halfpipe", "prop_skate_halfpipe_cr",
            "prop_skate_quartpipe", "prop_skate_quartpipe_cr",
            "prop_skate_funbox", "prop_skate_funbox_cr",
            "prop_skate_rail", "prop_skate_spiner", "prop_skate_spiner_cr",
            "tr_prop_tr_skip_ramp_01a"
        };
    }
}
