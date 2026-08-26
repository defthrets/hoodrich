using System;
using System.Collections.Generic;

namespace Hoodrich.Missions
{
    /// <summary>
    /// The set's initials, as a list of places to put paint.
    ///
    /// THE WHOLE IDEA. A script cannot put a picture on a wall -- a picture is a texture asset,
    /// and getting one into the game means packing it into an archive the engine indexes, which
    /// is a tool and a dependency and a job for the player. That road was walked to the end and
    /// it is genuinely closed: nothing in the shipped game reads an image file off disk.
    ///
    /// But the tag run was ALREADY putting about forty decals on every wall, at random. Forty
    /// marks in the right places is letters. So this costs very little that was not already
    /// being spent -- the paint, the colour, the persistence and the streaming behaviour are
    /// all untouched, and the only thing that changes is WHERE each blob goes.
    ///
    /// LETTERS ARE PATHS, NOT POINTS, and that is the second attempt. The first version placed
    /// each dot by hand, which put five evenly down a stem and four around a whole bowl -- so
    /// stems read and every curve and diagonal fell apart into scattered dots. Rendering it
    /// offline made that obvious in about a second. Here a letter is a set of pen strokes and
    /// the dots are spaced evenly ALONG them, so a bowl gets as much paint per inch as a stem
    /// and every letter is equally solid.
    ///
    /// Coordinates are a unit box per letter: x from 0 at the left to 1 at the right, y from 0
    /// at the bottom to 1 at the top. Strokes are painted in order, and each stroke from its
    /// start to its end, so the tag draws the way a hand would draw it.
    /// </summary>
    internal static class TagLetters
    {
        /// <summary>
        /// One pen stroke: a run of points the pen passes through without lifting.
        ///
        /// Curves are polylines with enough corners to read as round once the paint is on.
        /// Nobody is going to measure the arc; it needs to look like somebody drew a B.
        /// </summary>
        private static float[][] S(params float[][] strokes) { return strokes; }
        private static float[] P(params float[] xy) { return xy; }

        /// <summary>
        /// Every letter any gang in the game needs, and no others.
        ///
        /// Taken from the tags in gangs.json rather than writing a whole alphabet on spec:
        /// VLA, FAM, BALL, VAGO, MARA, LOST, TRI, ARM, KKP -- plus C, because the Families
        /// write themselves CGF as readily as FAM. That is A B C F G I K L M O P R S T V,
        /// fifteen letters. Anything outside the set falls back to the old random splatter,
        /// which is a worse tag but is never a blank wall.
        /// </summary>
        private static readonly Dictionary<char, float[][]> Glyphs =
            new Dictionary<char, float[][]>
        {
            ['A'] = S(P(0.06f, 0.00f, 0.50f, 1.00f),
                      P(0.50f, 1.00f, 0.94f, 0.00f),
                      P(0.24f, 0.36f, 0.76f, 0.36f)),

            ['B'] = S(P(0.14f, 0.00f, 0.14f, 1.00f),
                      P(0.14f, 1.00f, 0.56f, 1.00f, 0.78f, 0.90f, 0.82f, 0.74f,
                        0.74f, 0.60f, 0.52f, 0.53f, 0.14f, 0.53f),
                      P(0.14f, 0.53f, 0.60f, 0.53f, 0.84f, 0.44f, 0.88f, 0.26f,
                        0.78f, 0.08f, 0.56f, 0.00f, 0.14f, 0.00f)),

            // The set writes itself CGF as often as FAM, so C earns its own entry rather than
            // living inside G. It IS G's opening stroke -- the same arc, without the bar.
            ['C'] = S(P(0.88f, 0.82f, 0.66f, 0.98f, 0.38f, 1.00f, 0.16f, 0.86f,
                        0.08f, 0.60f, 0.08f, 0.40f, 0.16f, 0.14f, 0.38f, 0.00f,
                        0.66f, 0.02f, 0.88f, 0.18f)),

            ['F'] = S(P(0.16f, 0.00f, 0.16f, 1.00f),
                      P(0.16f, 1.00f, 0.82f, 1.00f),
                      P(0.16f, 0.55f, 0.66f, 0.55f)),

            ['G'] = S(P(0.88f, 0.82f, 0.66f, 0.98f, 0.38f, 1.00f, 0.16f, 0.86f,
                        0.08f, 0.60f, 0.08f, 0.40f, 0.16f, 0.14f, 0.38f, 0.00f,
                        0.66f, 0.02f, 0.88f, 0.18f, 0.88f, 0.40f),
                      P(0.88f, 0.40f, 0.56f, 0.40f)),

            ['I'] = S(P(0.50f, 0.00f, 0.50f, 1.00f)),

            ['K'] = S(P(0.14f, 0.00f, 0.14f, 1.00f),
                      P(0.84f, 1.00f, 0.14f, 0.44f),
                      P(0.36f, 0.60f, 0.86f, 0.00f)),

            ['L'] = S(P(0.18f, 1.00f, 0.18f, 0.00f),
                      P(0.18f, 0.00f, 0.82f, 0.00f)),

            ['M'] = S(P(0.04f, 0.00f, 0.04f, 1.00f),
                      P(0.04f, 1.00f, 0.50f, 0.38f),
                      P(0.50f, 0.38f, 0.96f, 1.00f),
                      P(0.96f, 1.00f, 0.96f, 0.00f)),

            ['O'] = S(P(0.50f, 1.00f, 0.22f, 0.92f, 0.06f, 0.68f, 0.06f, 0.34f,
                        0.22f, 0.08f, 0.50f, 0.00f, 0.78f, 0.08f, 0.94f, 0.34f,
                        0.94f, 0.68f, 0.78f, 0.92f, 0.50f, 1.00f)),

            ['P'] = S(P(0.14f, 0.00f, 0.14f, 1.00f),
                      P(0.14f, 1.00f, 0.58f, 1.00f, 0.82f, 0.90f, 0.86f, 0.72f,
                        0.76f, 0.56f, 0.52f, 0.50f, 0.14f, 0.50f)),

            ['R'] = S(P(0.14f, 0.00f, 0.14f, 1.00f),
                      P(0.14f, 1.00f, 0.58f, 1.00f, 0.82f, 0.90f, 0.86f, 0.72f,
                        0.76f, 0.58f, 0.52f, 0.52f, 0.14f, 0.52f),
                      P(0.44f, 0.52f, 0.88f, 0.00f)),

            ['S'] = S(P(0.88f, 0.84f, 0.68f, 0.98f, 0.40f, 1.00f, 0.18f, 0.88f,
                        0.16f, 0.70f, 0.34f, 0.58f, 0.62f, 0.50f, 0.82f, 0.40f,
                        0.86f, 0.22f, 0.66f, 0.04f, 0.38f, 0.00f, 0.14f, 0.12f)),

            ['T'] = S(P(0.06f, 1.00f, 0.94f, 1.00f),
                      P(0.50f, 1.00f, 0.50f, 0.00f)),

            ['V'] = S(P(0.04f, 1.00f, 0.50f, 0.00f),
                      P(0.50f, 0.00f, 0.96f, 1.00f)),
        };

        /// <summary>Whether every letter of this tag can be drawn.</summary>
        public static bool CanWrite(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return false;

            foreach (var c in tag.ToUpperInvariant())
            {
                if (c == ' ') continue;
                if (!Glyphs.ContainsKey(c)) return false;
            }

            return true;
        }

        /// <summary>
        /// The whole tag as evenly spaced points, in the order they should be painted.
        ///
        /// <paramref name="spacing"/> is the gap between dots in LETTER HEIGHTS, so it is
        /// independent of how big the tag ends up on the wall. Smaller is more solid and costs
        /// more decals; the caller owns that trade because the caller owns the budget.
        ///
        /// X comes back centred on zero rather than 0..1, because the caller places this
        /// against a point that is the middle of the tag. Y stays 0..1 and is offset by the
        /// caller, since a tag hangs from where the can is rather than being centred on it.
        /// </summary>
        public static List<float[]> Layout(string tag, float spacing)
        {
            var points = new List<float[]>();
            if (string.IsNullOrEmpty(tag)) return points;
            if (spacing < 0.02f) spacing = 0.02f;

            var text = tag.ToUpperInvariant();

            // Letter box plus the gap after it, in letter widths.
            const float Pitch = 1.24f;

            var drawn = 0;
            foreach (var c in text) if (c != ' ' && Glyphs.ContainsKey(c)) drawn++;
            if (drawn == 0) return points;

            var span = drawn * Pitch - (Pitch - 1f);
            var slot = 0;

            foreach (var c in text)
            {
                if (c == ' ') { slot++; continue; }

                float[][] glyph;
                if (!Glyphs.TryGetValue(c, out glyph)) continue;

                var left = slot * Pitch;

                foreach (var stroke in glyph) Walk(stroke, left, span, spacing, points);

                slot++;
            }

            return points;
        }

        /// <summary>
        /// Lays dots along one stroke at even spacing, corner to corner.
        ///
        /// Spacing is measured in the LETTER's own space rather than the word's, so a four
        /// letter tag is not painted thinner than a three letter one -- the word gets wider on
        /// the wall, it does not get sparser.
        /// </summary>
        private static void Walk(float[] stroke, float left, float span, float spacing,
                                 List<float[]> into)
        {
            if (stroke == null || stroke.Length < 4) return;

            // Carried between segments so a corner does not restart the rhythm and leave a
            // double-thick blob on every bend.
            var carried = 0f;

            for (var i = 0; i + 3 < stroke.Length; i += 2)
            {
                var ax = stroke[i];
                var ay = stroke[i + 1];
                var bx = stroke[i + 2];
                var by = stroke[i + 3];

                var dx = bx - ax;
                var dy = by - ay;

                var len = (float)Math.Sqrt(dx * dx + dy * dy);
                if (len <= 0.0001f) continue;

                var at = carried;

                while (at < len)
                {
                    var t = at / len;

                    var x = ax + dx * t;
                    var y = ay + dy * t;

                    into.Add(new[] { (left + x) / span - 0.5f, y });

                    at += spacing;
                }

                // How far past the end of this segment the next dot falls, carried into the
                // next one. Updated OUTSIDE the loop on purpose: a segment shorter than the
                // spacing places no dots at all, and the first version left the carry stale in
                // that case rather than counting the segment down. O and S are long chains of
                // short segments, so they got skipped almost entirely -- LOST came out at
                // seventeen dots where BALL was sixty-one, which is what gave it away.
                carried = at - len;
                if (carried < 0f) carried = 0f;
            }

            // The very end of the stroke, so a letter never stops a dot short of its own
            // corner -- the bottom of an L, the tail of an R.
            into.Add(new[]
            {
                (left + stroke[stroke.Length - 2]) / span - 0.5f,
                stroke[stroke.Length - 1]
            });
        }
    }
}
