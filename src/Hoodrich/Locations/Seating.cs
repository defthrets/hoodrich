using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Locations
{
    /// <summary>
    /// The furniture on the block, and who is sat on it.
    ///
    /// THE COUCHES WERE ALREADY THERE AND NOBODY EVER SAT ON THEM. Fixture drags one out into
    /// each courtyard and its own comment says the point of it is that it does nothing -- which
    /// was true when it was scenery. It stopped being true the moment there were people stood a
    /// metre from it holding drinks, because a couch in a yard full of people that every single
    /// one of them is ignoring is more obviously fake than no couch at all.
    ///
    /// A REGISTRY RATHER THAN A LOOKUP, because the couch knows where its cushions are and the
    /// man does not. Fixture measures its prop when it spawns and offers what it finds; anybody
    /// who wants somewhere to sit asks for the nearest free one. That way a seat is claimed by
    /// exactly one person -- two men clipped through each other on the same cushion is the
    /// failure this exists to prevent, and it is the one you cannot fix from the ped's side.
    ///
    /// It is deliberately open. Nothing here knows about couches: it knows about positions
    /// somebody may sit at, so a crate, a step or a bench can offer them later without this
    /// file changing.
    /// </summary>
    internal static class Seating
    {
        /// <summary>One place one person may sit.</summary>
        internal sealed class Cushion
        {
            public Vector3 At;
            public float Facing;

            /// <summary>Whoever put it out, so it can take it away again.</summary>
            public object Owner;

            /// <summary>What the facing was worked out from, kept so it can be worked out again once the couch has collision.</summary>
            public Vector3 Mid;
            public Vector3 Front;
            public float Depth;
            public Prop Prop;
            public bool Sure;

            /// <summary>Who is on it, or null.</summary>
            public Ped Sitter;
        }

        private static readonly List<Cushion> Seats = new List<Cushion>();

        /// <summary>
        /// How far along a couch one person takes up, and how many one may hold.
        ///
        /// Sixty centimetres is a cushion. Three is the cap because a four-seater in this game
        /// is still only about two metres of usable length once the arms are off it, and four
        /// men shoulder to shoulder on one read as a bus stop.
        /// </summary>
        private const float PerSeat = 0.62f;
        private const int MostSeats = 3;

        /// <summary>
        /// How far up the prop the seat surface is, as a fraction of its height.
        ///
        /// MEASURED FROM THE MODEL RATHER THAN GUESSED IN METRES, because the same number has
        /// to work for a two-foot couch and a bar stool. A seat sits a bit under halfway up the
        /// thing it belongs to on almost everything with a back on it -- the backrest is the
        /// top half and the cushion is where it starts.
        /// </summary>
        private const float SeatUp = 0.45f;

        /// <summary>How far forward off centre, as a fraction of the depth, the cushion is.</summary>
        private const float SeatOut = 0.12f;

        /// <summary>How far a probe looks for the backrest, and what counts as blocked.</summary>
        private const float LookAhead = 1.6f;
        private const float Blocked = 1.0f;

        // ---- putting furniture out ---------------------------------------------

        /// <summary>
        /// Whether a model is something anybody would sit on.
        ///
        /// BY NAME, WHICH IS THE ONLY THING AVAILABLE. There is no flag on a model that says
        /// "seat" -- the game knows because the map author attached a scenario point to it, and
        /// a prop this mod spawns has none. Rockstar's naming is consistent enough for this to
        /// work on the models actually in use and to fail safe on anything else: an unrecognised
        /// prop simply offers no seats, which is exactly what happens today.
        /// </summary>
        public static bool IsSeat(string model)
        {
            if (string.IsNullOrEmpty(model)) return false;

            var name = model.ToLowerInvariant();

            return name.Contains("couch") || name.Contains("sofa") ||
                   name.Contains("chair") || name.Contains("bench") ||
                   name.Contains("seat") || name.Contains("stool");
        }

        /// <summary>
        /// Work out where somebody could sit on this prop, and offer those places.
        ///
        /// THE PROP IS MEASURED, NOT LOOKED UP IN A TABLE. Fixture picks its model from a
        /// ladder of candidates -- prop_couch_03, then 04, then 01, then the old one -- so
        /// which one is actually standing there depends on the install, and a table of cushion
        /// offsets per model would be right for whichever one it was written against and
        /// silently wrong for the rest. The bounding box is true for all of them.
        /// </summary>
        public static void Offer(object owner, Prop prop, string model)
        {
            if (owner == null || prop == null || !prop.Exists()) return;

            Withdraw(owner);

            try
            {
                var lo = new OutputArgument();
                var hi = new OutputArgument();

                Function.Call(Hash.GET_MODEL_DIMENSIONS, prop.Model.Hash, lo, hi);

                var min = lo.GetResult<Vector3>();
                var max = hi.GetResult<Vector3>();

                var wide = max.X - min.X;
                var deep = max.Y - min.Y;
                var tall = max.Z - min.Z;

                if (wide < 0.2f || deep < 0.2f || tall < 0.2f) return;

                // The long way along it is the way the cushions run. Which of the model's two
                // horizontal axes that is varies per prop and there is no convention to rely
                // on, so it is decided by which one is longer.
                var alongX = wide >= deep;

                var run = alongX ? wide : deep;
                var depth = alongX ? deep : wide;

                var seats = (int)(run / PerSeat);

                if (seats < 1) seats = 1;
                if (seats > MostSeats) seats = MostSeats;

                var step = run / seats;

                // A seated ped's root goes at the seat surface, not on the floor -- see
                // Entourage.Idle, where the onProp flag exists to stop the ground probe
                // dragging a man off the cushion and onto the concrete under it.
                var up = min.Z + tall * SeatUp;

                // Which way the person on it looks. The model's own forward if the cushions
                // run across it, its right if they run along it -- either way it is the axis
                // the cushions do NOT run along, because that is the way out of the seat.
                var out_ = alongX ? prop.ForwardVector : prop.RightVector;

                out_ = Flat(out_);

                if (out_.LengthSquared() < 0.01f) return;

                // A COUCH'S FRONT IS ITS MINUS-Y. The party couch stands with its heading
                // pointing at the table, and its backrest is the side that faces the table,
                // so whoever sat on it facing the model's own forward sat looking at the
                // wall -- three times, through two rounds of probing that read the same
                // height both sides of a box-shaped collision and fell back to that same
                // forward. The probe stays for couches whose collision has a shape; this is
                // the answer for the ones whose collision does not.
                var couch = model.ToLowerInvariant();

                if (couch.Contains("couch") || couch.Contains("sofa"))
                {
                    out_ = out_ * -1f;
                }

                var made = 0;

                for (var i = 0; i < seats; i++)
                {
                    // Spread about the middle: one seat sits on centre, two straddle it, three
                    // put one in the middle and one on either arm.
                    var slide = (i - (seats - 1) * 0.5f) * step;

                    var fore = depth * SeatOut;

                    var local = alongX
                        ? new Vector3(slide, fore, up)
                        : new Vector3(fore, slide, up);

                    var at = prop.GetOffsetPosition(local);

                    // The middle of the seat, for the backrest probe: the seat point is
                    // already pushed toward the front, and the probe wants to compare the
                    // two edges either side of the centre.
                    var mid = prop.GetOffsetPosition(alongX
                        ? new Vector3(slide, (min.Y + max.Y) * 0.5f, up)
                        : new Vector3((min.X + max.X) * 0.5f, slide, up));

                    bool sure;
                    var facing = Facing(at, mid, out_, prop, depth, out sure);

                    Seats.Add(new Cushion
                    {
                        At = at,
                        Facing = facing,
                        Owner = owner,
                        Mid = mid,
                        Front = out_,
                        Depth = depth,
                        Prop = prop,
                        Sure = sure
                    });
                    made++;
                }

                Log.Info("Seating: " + made + " place(s) to sit on the " + model + ".");
            }
            catch (Exception ex)
            {
                Log.Debug("Seating: could not measure " + model + ": " + ex.Message);
            }
        }

        /// <summary>Takes back everything one owner put out.</summary>
        public static void Withdraw(object owner)
        {
            if (owner == null) return;

            for (var i = Seats.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(Seats[i].Owner, owner)) Seats.RemoveAt(i);
            }
        }

        // ---- sitting on it -----------------------------------------------------

        /// <summary>
        /// The nearest free seat to a spot, claimed, or null if there is nothing.
        ///
        /// HANDS BACK THE SAME ONE EVERY TIME FOR THE SAME PERSON. Entourage re-issues an idle
        /// whenever the scenario task has gone -- which is often, and by design -- so a call
        /// that picked a fresh cushion each time would walk a man down the couch one seat per
        /// re-task until he ran out of couch.
        /// </summary>
        public static Cushion Take(Ped who, Vector3 near, float range)
        {
            if (who == null || !who.Exists() || Seats.Count == 0) return null;

            // Anybody who has died or been streamed out gives their seat up. Doing it here
            // rather than on a teardown hook means there is no teardown hook to forget: the
            // sweep happens on the only call that could care about the answer.
            for (var i = 0; i < Seats.Count; i++)
            {
                var sat = Seats[i].Sitter;

                if (sat != null && (!sat.Exists() || sat.IsDead)) Seats[i].Sitter = null;
            }

            for (var i = 0; i < Seats.Count; i++)
            {
                if (Seats[i].Sitter != null && Seats[i].Sitter.Handle == who.Handle)
                {
                    return Seats[i];
                }
            }

            Cushion best = null;
            var bestSq = range * range;

            for (var i = 0; i < Seats.Count; i++)
            {
                if (Seats[i].Sitter != null) continue;

                var gap = Seats[i].At.DistanceToSquared(near);

                if (gap > bestSq) continue;

                bestSq = gap;
                best = Seats[i];
            }

            if (best != null)
            {
                best.Sitter = who;

                // MEASURED AGAIN NOW IF IT COULD NOT BE THEN. The couch is put out and its
                // seats offered on the same frame, before the game has given it collision,
                // so the probe at that moment reads nothing and falls back to the guess that
                // sat him backwards. By the time somebody sits, the couch has been there for
                // seconds.
                if (!best.Sure && best.Prop != null && best.Prop.Exists())
                {
                    bool sure;
                    var facing = Facing(best.At, best.Mid, best.Front, best.Prop, best.Depth, out sure);

                    if (sure)
                    {
                        best.Facing = facing;
                        best.Sure = true;
                        Log.Info("Seating: measured the couch again as somebody sat; facing " + (int)facing + ".");
                    }
                }
            }

            return best;
        }

        /// <summary>Gives one up, for anybody who knows they are getting off it.</summary>
        public static void Let(Ped who)
        {
            if (who == null) return;

            for (var i = 0; i < Seats.Count; i++)
            {
                if (Seats[i].Sitter != null && Seats[i].Sitter.Handle == who.Handle)
                {
                    Seats[i].Sitter = null;
                }
            }
        }

        /// <summary>Everything, for a teardown.</summary>
        public static void Clear() => Seats.Clear();

        // ---- which way round ---------------------------------------------------

        /// <summary>
        /// Which way somebody on this cushion looks.
        ///
        /// THE MODEL'S OWN FRONT, UNLESS THE WORLD SAYS OTHERWISE. There is no reliable
        /// convention for which end of a couch model is the back -- prop_couch_03 and
        /// prop_old_couch_01 need not agree, and Fixture will spawn whichever of them this
        /// install has. Guessing gets a man sat looking into the backrest half the time.
        ///
        /// So it asks the room. A backrest is a solid thing a hand's width behind the cushion,
        /// and open yard is not: probe both ways and take the clear one. It only overrides the
        /// model when there is actual evidence -- if both ways are clear, or both blocked, the
        /// prop's own front wins and nothing has been made worse by looking.
        /// </summary>
        private static float Facing(Vector3 at, Vector3 mid, Vector3 front, Prop prop, float depth,
                                    out bool sure)
        {
            sure = false;

            var head = Heading(front);

            // THE BACKREST SAYS WHICH WAY ROUND. A couch is low at the front and tall at the
            // back, so the couch's own height at the two edges says which is which, and
            // the man faces away from the tall one. This is what the look-ahead below could
            // never see: it casts past the couch on purpose, so the one thing that would
            // have told it the answer was the one thing it ignored.
            //
            // SEVERAL POINTS A SIDE, AND THE TALLEST WINS. One probe a side at a third of
            // the depth landed on the cushion both sides of a deep couch and read the same
            // height twice, which is how the first version of this still sat him the wrong
            // way round. The backrest is at the very edge; the probes go out to it.
            //
            // And SURE says whether both sides were actually read. A couch that was made
            // this frame has no collision to read yet, so Take asks again later.
            try
            {
                var ahead = Missed;
                var behind = Missed;

                foreach (var f in Reaches)
                {
                    var reach = Math.Max(0.20f, depth * f);

                    ahead = Math.Max(ahead, Tall(mid + front * reach, prop));
                    behind = Math.Max(behind, Tall(mid - front * reach, prop));
                }

                Log.Info("Seating: probed the " + prop.Model.Hash + " at " + at + ": ahead " +
                         (ahead > Missed ? ahead.ToString("0.00") : "missed") + ", behind " +
                         (behind > Missed ? behind.ToString("0.00") : "missed") + ".");

                if (ahead > Missed && behind > Missed)
                {
                    sure = true;

                    if (Math.Abs(ahead - behind) > 0.10f)
                    {
                        return ahead > behind ? Heading(front * -1f) : head;
                    }

                    // The same height both sides is a bench. The look-ahead decides.
                }
            }
            catch
            {
                // The look-ahead, then.
            }

            try
            {
                var eye = at + new Vector3(0f, 0f, 0.35f);

                var ahead = Shut(eye, front, prop);
                var behind = Shut(eye, front * -1f, prop);

                if (ahead && !behind) return Heading(front * -1f);
            }
            catch
            {
                // The model's own front, then.
            }

            return head;
        }

        /// <summary>Whether there is something solid within arm's reach that way.</summary>
        /// <summary>
        /// How high the couch's own surface is under a point, probed from above; Missed when
        /// the probe lands on anything that is not the couch, or on nothing.
        /// </summary>
        private static float Tall(Vector3 over, Prop prop)
        {
            var from = over + new Vector3(0f, 0f, 1.6f);

            var hit = World.Raycast(from, new Vector3(0f, 0f, -1f), 3.2f, IntersectFlags.Objects);

            if (!hit.DidHit || hit.HitEntity == null || hit.HitEntity.Handle != prop.Handle) return Missed;

            return hit.HitPosition.Z;
        }

        private const float Missed = -9999f;

        /// <summary>How far out from the middle of the seat the probes go, as shares of its depth.</summary>
        private static readonly float[] Reaches = { 0.30f, 0.40f, 0.47f };

        private static bool Shut(Vector3 from, Vector3 dir, Prop prop)
        {
            var hit = World.Raycast(from, dir, LookAhead,
                                    IntersectFlags.Map | IntersectFlags.Objects, prop);

            return hit.DidHit && from.DistanceTo(hit.HitPosition) < Blocked;
        }

        /// <summary>A direction as a heading, in the game's own convention.</summary>
        private static float Heading(Vector3 dir)
        {
            var deg = (float)(Math.Atan2(-dir.X, dir.Y) * 180.0 / Math.PI);

            while (deg < 0f) deg += 360f;
            while (deg >= 360f) deg -= 360f;

            return deg;
        }

        /// <summary>Flattened and unit length, because nobody sits at an angle.</summary>
        private static Vector3 Flat(Vector3 v)
        {
            var flat = new Vector3(v.X, v.Y, 0f);

            var len = flat.Length();

            return len < 0.01f ? Vector3.Zero : flat * (1f / len);
        }
    }
}
