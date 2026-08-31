using System;
using System.Collections.Generic;
using System.IO;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Gangs;

namespace Hoodrich.Territory
{
    /// <summary>
    /// Measures every set's turf out of the game and writes it to turf.json.
    ///
    /// THE ALTERNATIVE WAS ME GUESSING, AND A GUESS DRAWN ON A MAP IS INDISTINGUISHABLE FROM A
    /// MEASUREMENT. That is the whole reason the seeded rectangles were thrown away: they came
    /// from bounding circles, they were wrong everywhere, and nothing about looking at them
    /// said so. Doing the remaining eight sets by hand from what I happen to know about Los
    /// Santos would have produced exactly the same thing again, one step further from the
    /// evidence.
    ///
    /// THE GAME ALREADY KNOWS. GET_NAME_OF_ZONE hands back the zone code at any coordinate, so
    /// "where does Davis end" is a question with an exact answer that can simply be asked --
    /// several thousand times, over a grid, until the shape falls out. That is a measurement.
    /// It is the same thing walking the corners does, done by something that never gets bored
    /// and never misreads a number off a screen.
    ///
    /// AXIS-ALIGNED, WHICH IS A BONUS RATHER THAN A COMPROMISE. The grid is sampled along the
    /// world axes, so every box comes out unrotated -- and the game's own zones are axis-
    /// aligned boxes to begin with, so the fit is exact rather than approximate. It also side-
    /// steps the rotation entirely, which is worth having while rotated areas are still coming
    /// out skewed on the minimap facing north and south.
    ///
    /// RUN ONCE, NOT EVERY LOAD. Several thousand native calls per set is nothing as a one-off
    /// and absurd on a tick, so the answer is written to turf.json and read like any other
    /// hand-authored shape. The file it writes is the file a person edits.
    /// </summary>
    internal static class TurfBake
    {
        /// <summary>
        /// How far apart the samples are, in metres.
        ///
        /// Twelve. Fine enough that a boundary lands within about a car's length of where it
        /// really is, coarse enough that a six hundred metre zone is two and a half thousand
        /// questions rather than forty thousand. The merge below means a finer grid does not
        /// cost blips -- only time -- so this is bounded by patience and not by the map.
        /// </summary>
        private const float Step = 12f;

        /// <summary>
        /// How far out to look for a zone, as a multiple of its own radius.
        ///
        /// Slightly over one, because the radius in zones.json is the circle that contains the
        /// zone and a square that contains that circle has corners outside it. Under-sampling
        /// clips the corners off a shape and nothing about the result says it happened.
        /// </summary>
        private const float Reach = 1.15f;

        /// <summary>
        /// Measure everything and write it out.
        /// </summary>
        /// <returns>A line about what happened, for the player.</returns>
        public static string Run(GangRegistry gangs, ZoneMap zones)
        {
            if (gangs == null || zones == null) return "No gangs or no zones to measure.";

            var began = Game.GameTime;

            var doc = Json.Object();
            var turf = Json.Object();

            var sets = 0;
            var boxes = 0;
            var asked = 0;

            foreach (var gang in gangs.All)
            {
                if (gang == null || gang.Turf.Count == 0) continue;

                var list = Json.Array();
                var mine = 0;

                foreach (var code in gang.Turf)
                {
                    var zone = zones.Get(code);
                    if (zone == null) continue;

                    var shape = Measure(code, zone, ref asked);

                    foreach (var box in shape)
                    {
                        list.Add(Json.Object()
                            .Set("zone", code)
                            .Set("x", Math.Round(box.At.X, 1))
                            .Set("y", Math.Round(box.At.Y, 1))
                            .Set("w", Math.Round(box.Wide, 1))
                            .Set("h", Math.Round(box.Deep, 1))
                            .Set("rot", 0));

                        mine++;
                    }
                }

                if (mine == 0) continue;

                turf.Set(gang.Id, list);
                sets++;
                boxes += mine;
            }

            doc.Set("_comment", Json.Array()
                .Add(Json.Str("Measured out of the game rather than drawn by hand."))
                .Add(Json.Str(""))
                .Add(Json.Str("Every box here came from asking GET_NAME_OF_ZONE at a grid of points and keeping"))
                .Add(Json.Str("the ones that answered with the zone's own code. It is the shape the game itself"))
                .Add(Json.Str("uses, to within the sampling step, and it is axis-aligned because the sampling is."))
                .Add(Json.Str(""))
                .Add(Json.Str("Edit it freely -- it is read exactly like a hand-authored file, and an entry can be"))
                .Add(Json.Str("replaced with a walked \"poly\" whenever a real boundary is worth more than the"))
                .Add(Json.Str("game's own idea of where a neighbourhood stops.")));

            doc.Set("turf", turf);

            var path = Path.Combine(Paths.Data, "turf.json");

            if (!JsonFile.Write(path, doc)) return "Measured it, but could not write turf.json.";

            var took = (Game.GameTime - began) / 1000f;

            Log.Info("Turf bake: " + sets + " set(s), " + boxes + " box(es), " + asked +
                     " sample(s) in " + took.ToString("0.0") + "s.");

            return sets + " sets measured into " + boxes + " shapes.";
        }

        /// <summary>
        /// One zone, sampled and squared off.
        ///
        /// Two passes and the second one is what makes this usable. The first walks the grid
        /// row by row and records the runs of samples that answered with this zone -- which
        /// for a six hundred metre zone is fifty rows and therefore fifty boxes, and fifty
        /// blips per zone across twenty zones is a thousand blips for a map decoration.
        ///
        /// The second merges rows that have IDENTICAL runs into one taller box. A rectangular
        /// zone collapses from fifty boxes to one; an L-shaped one to two or three. The cost of
        /// a finer grid is then time rather than blips, which is the right way round.
        /// </summary>
        private static List<TurfBox> Measure(string code, ZoneInfo zone, ref int asked)
        {
            var found = new List<TurfBox>();

            var reach = zone.Radius * Reach;
            var z = zone.Centre.Z;

            // Runs per row, in order, so identical rows can be spotted by comparing them.
            var rows = new List<List<float[]>>();
            var ys = new List<float>();

            for (var y = zone.Centre.Y - reach; y <= zone.Centre.Y + reach; y += Step)
            {
                var runs = new List<float[]>();

                var open = false;
                var from = 0f;
                var last = 0f;

                for (var x = zone.Centre.X - reach; x <= zone.Centre.X + reach; x += Step)
                {
                    var here = false;

                    try
                    {
                        asked++;

                        var name = Function.Call<string>(Hash.GET_NAME_OF_ZONE, x, y, z);

                        here = string.Equals(name, code, StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        here = false;
                    }

                    if (here && !open)
                    {
                        open = true;
                        from = x;
                    }
                    else if (!here && open)
                    {
                        open = false;
                        runs.Add(new[] { from, last });
                    }

                    last = x;
                }

                // A run that reaches the edge of the sampled square never closed.
                if (open) runs.Add(new[] { from, last });

                rows.Add(runs);
                ys.Add(y);
            }

            // ---- merge identical rows ------------------------------------------------
            var i = 0;

            while (i < rows.Count)
            {
                if (rows[i].Count == 0) { i++; continue; }

                var j = i + 1;

                while (j < rows.Count && Same(rows[i], rows[j])) j++;

                // The band runs from the top of row i to the bottom of row j-1. Half a step is
                // added at each end because a sample sits at the CENTRE of the cell it stands
                // for, so a band of one row is one step deep and not zero.
                var top = ys[i] - Step * 0.5f;
                var bottom = ys[j - 1] + Step * 0.5f;

                foreach (var run in rows[i])
                {
                    var left = run[0] - Step * 0.5f;
                    var right = run[1] + Step * 0.5f;

                    found.Add(new TurfBox
                    {
                        Zone = code,
                        At = new Vector3((left + right) * 0.5f, (top + bottom) * 0.5f, 0f),
                        Wide = right - left,
                        Deep = bottom - top,
                        Rot = 0f
                    });
                }

                i = j;
            }

            return found;
        }

        /// <summary>Whether two rows have the same runs in the same places.</summary>
        private static bool Same(List<float[]> a, List<float[]> b)
        {
            if (a.Count != b.Count) return false;

            for (var i = 0; i < a.Count; i++)
            {
                if (Math.Abs(a[i][0] - b[i][0]) > 0.01f) return false;
                if (Math.Abs(a[i][1] - b[i][1]) > 0.01f) return false;
            }

            return true;
        }
    }
}
