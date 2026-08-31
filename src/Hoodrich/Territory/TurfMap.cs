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
    /// <summary>One box of somebody's turf, out of turf.json.</summary>
    internal sealed class TurfBox
    {
        public string Zone = "";
        public Vector3 At;
        public float Wide = 100f;
        public float Deep = 100f;
        public float Rot;
    }

    /// <summary>
    /// Whose block is whose, drawn on the map.
    ///
    /// FIRST ATTEMPT WAS DISCS AND IT LOOKED LIKE SPILLED PAINT. Every zone was drawn as
    /// ADD_BLIP_FOR_RADIUS at the zone's own radius -- which is the circle that CONTAINS the
    /// zone, not the shape of it. Strawberry alone is three hundred and fifty metres of circle.
    /// Half a dozen of them overlapping buried the streets, and a disc cannot be laid along a
    /// road no matter how big or small you make it, so no amount of tuning was going to fix it.
    ///
    /// ADD_BLIP_FOR_AREA DRAWS A BOX, AND SET_BLIP_ROTATION_WITH_FLOAT TURNS IT. That is the
    /// whole difference: a turned box has four straight edges that can be put down the middle
    /// of a street, and a zone can have as many of them as it needs -- two or three laid end to
    /// end follow a boundary that one shape cannot.
    ///
    /// There is still no native that draws a polygon on the map, so this is the closest the
    /// game gets. It is a real answer rather than the only one available.
    ///
    /// THE NUMBERS LIVE IN turf.json AND ARE MEANT TO BE CORRECTED. They are seeded from the
    /// bounding circles in zones.json, which puts them in the right ballpark and no better --
    /// getting a line onto a particular street means standing on that street and reading the
    /// coordinate, and that is an edit to a data file rather than a rebuild.
    ///
    /// Made once and left alone: nothing about a zone moves, and a blip rebuilt on a tick is a
    /// blip that flickers.
    /// </summary>
    internal sealed class TurfMap
    {
        /// <summary>
        /// How solid the boxes are, out of 255.
        ///
        /// Down from eighty with the discs. Boxes stack far less -- they were only overlapping
        /// because circles are the wrong shape -- so the same wash now reads much heavier, and
        /// the point of this is to be able to see the streets underneath it.
        /// </summary>
        private const int Wash = 55;

        private readonly List<Blip> _boxes = new List<Blip>();

        public bool Showing => _boxes.Count > 0;

        /// <summary>
        /// Put them up.
        ///
        /// Falls back to the zone discs when turf.json is missing or has nothing for a set, so
        /// an install without the file still shows something rather than nothing -- and says
        /// which it used, because "my turf looks like circles again" is otherwise a mystery.
        /// </summary>
        public void Show(GangRegistry gangs, Dictionary<string, List<TurfBox>> turf)
        {
            Hide();

            if (gangs == null) return;

            var boxes = 0;
            var shaded = 0;

            foreach (var gang in gangs.All)
            {
                if (gang == null) continue;

                List<TurfBox> mine = null;

                if (turf != null) turf.TryGetValue(gang.Id, out mine);

                if (mine != null && mine.Count > 0) shaded++;

                // A SET WITH NO WALKED SHAPE GETS NOTHING, and that is deliberate.
                //
                // There used to be a fallback: no polygon meant the zone's own bounding circle,
                // drawn as a disc. It was well meant and it was the thing that made this look
                // like spilled paint -- a bounding circle is not a shape, so the fallback was
                // guaranteed to be wrong everywhere it was used, and it covered the map while
                // being wrong. Nothing is better than approximately something here: an unshaded
                // set reads as "not done yet", which is true, and a disc read as the answer.
                if (mine == null || mine.Count == 0) continue;

                foreach (var box in mine)
                {
                    if (Box(box, gang)) boxes++;
                }
            }

            Log.Info("Turf map: " + boxes + " box(es) across " + shaded + " set(s).");
        }

        private bool Box(TurfBox box, GangDef gang)
        {
            try
            {
                var handle = Function.Call<int>(Hash.ADD_BLIP_FOR_AREA,
                                                box.At.X, box.At.Y, box.At.Z,
                                                box.Wide, box.Deep);

                if (handle == 0) return false;

                Function.Call(Hash.SET_BLIP_COLOUR, handle, gang.BlipColour);
                Function.Call(Hash.SET_BLIP_ALPHA, handle, Wash);

                // SHORT RANGE, OR THE MINIMAP GROWS A COLUMN OF GREEN DOTS DOWN ITS EDGE.
                //
                // A blip that is off the minimap does not disappear, it CLAMPS to the edge and
                // sits there pointing at itself. That is right for a mission marker and absurd
                // for sixty-one strips of a shaded area: every strip you are not stood on
                // stacks up on the rim, so a shape you cannot even see is a solid bar of its
                // own colour down one side of the map.
                //
                // Short range stops the clamping. The strip is drawn when you are on it and
                // simply is not there when you are not -- and the pause map is unaffected,
                // which is where the whole shape is meant to be read anyway.
                Function.Call(Hash.SET_BLIP_AS_SHORT_RANGE, handle, true);

                // THE ROTATION IS THE WHOLE POINT. South Los Santos is on a grid turned about
                // twenty-seven degrees, so an unturned box over Chamberlain or Davis sits at an
                // angle to every street it is supposed to be bounded by -- which is the thing
                // that reads as wrong without being easy to name.
                if (Math.Abs(box.Rot) > 0.01f)
                {
                    Function.Call(Hash.SET_BLIP_ROTATION_WITH_FLOAT, handle, box.Rot);
                }

                Function.Call(Hash.BEGIN_TEXT_COMMAND_SET_BLIP_NAME, "STRING");
                Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, gang.Name);
                Function.Call(Hash.END_TEXT_COMMAND_SET_BLIP_NAME, handle);

                _boxes.Add(new Blip(handle));
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Turf map could not box " + box.Zone + ": " + ex.Message);
                return false;
            }
        }

        public void Hide()
        {
            foreach (var blip in _boxes)
            {
                try { if (blip != null && blip.Exists()) blip.Delete(); }
                catch { /* going anyway */ }
            }

            _boxes.Clear();
        }

        /// <summary>
        /// Cuts a shape into boxes.
        ///
        /// A ROTATED BOX HAS A HARD EDGE, SO ENOUGH OF THEM HAVE ANY EDGE YOU LIKE. That is the
        /// whole idea, and it is why "there is no polygon native" was never the end of the
        /// answer -- the game will not draw a shape, but it will draw fifty boxes, and fifty
        /// boxes laid side by side ARE a shape. Every one of them still has its own hard line;
        /// they simply happen to line up.
        ///
        /// The cut is made in the STREET GRID's frame, not the world's. The polygon is turned
        /// by minus the grid angle, sliced into horizontal strips there, and each strip is
        /// turned back -- so every strip runs ALONG the street rather than across it, and an
        /// edge that follows a road is one clean line rather than a staircase. That is the
        /// difference between this and simply chopping the shape up: the angle is chosen so
        /// the joins fall where nobody looks and the edges fall where everybody does.
        ///
        /// One strip per step, so a three hundred metre zone at a twenty-five metre step is a
        /// dozen boxes. The step is per shape because a long thin strip of turf down one street
        /// wants a finer cut than a whole neighbourhood.
        /// </summary>
        private static void Tile(List<Vector3> poly, float rot, float step, float bleed,
                                 string zone, List<TurfBox> into)
        {
            if (step < 4f) step = 4f;

            var rad = rot * (float)Math.PI / 180f;
            var cos = (float)Math.Cos(-rad);
            var sin = (float)Math.Sin(-rad);

            // Turned about the shape's own middle, so the numbers stay small and the boxes come
            // back where they started rather than swung across the map.
            float cx = 0f, cy = 0f;

            foreach (var p in poly) { cx += p.X; cy += p.Y; }

            cx /= poly.Count;
            cy /= poly.Count;

            var flat = new List<Vector3>(poly.Count);

            float lo = float.MaxValue, hi = float.MinValue;

            foreach (var p in poly)
            {
                var dx = p.X - cx;
                var dy = p.Y - cy;

                var f = new Vector3(dx * cos - dy * sin, dx * sin + dy * cos, 0f);

                flat.Add(f);

                if (f.Y < lo) lo = f.Y;
                if (f.Y > hi) hi = f.Y;
            }

            // Back the other way, for putting each strip where it belongs.
            var bcos = (float)Math.Cos(rad);
            var bsin = (float)Math.Sin(rad);

            var crossings = new List<float>();

            for (var y = lo + step * 0.5f; y < hi; y += step)
            {
                crossings.Clear();

                // Where the shape's edges cross this line. An even number of them, and the
                // inside of the shape is between the first and second, the third and fourth,
                // and so on -- which is what lets a shape with a notch in it work.
                for (var i = 0; i < flat.Count; i++)
                {
                    var a = flat[i];
                    var b = flat[(i + 1) % flat.Count];

                    if (Math.Abs(a.Y - b.Y) < 0.0001f) continue;
                    if (y < Math.Min(a.Y, b.Y) || y >= Math.Max(a.Y, b.Y)) continue;

                    crossings.Add(a.X + (y - a.Y) / (b.Y - a.Y) * (b.X - a.X));
                }

                if (crossings.Count < 2) continue;

                crossings.Sort();

                for (var k = 0; k + 1 < crossings.Count; k += 2)
                {
                    var x0 = crossings[k];
                    var x1 = crossings[k + 1];

                    var wide = x1 - x0;
                    if (wide < 2f) continue;

                    var mx = (x0 + x1) * 0.5f;

                    into.Add(new TurfBox
                    {
                        Zone = zone,
                        At = new Vector3(cx + (mx * bcos - y * bsin),
                                         cy + (mx * bsin + y * bcos), 0f),

                        // AT BLEED 1.0 THIS IS EXACTLY ZERO OVERLAP AND ZERO GAP. Centres sit
                        // one step apart and each strip is one step deep, so they abut on the
                        // line and neither cross it nor fall short of it. That is the default
                        // and it is what the arithmetic actually guarantees.
                        //
                        // The knob exists because an area blip does not have a hard edge -- it
                        // fades -- so perfect abutment can still show a faint join where two
                        // fades meet and sum to less than one solid. Bleed above 1 overlaps
                        // them to hide that, at the cost of a darker band where they cross,
                        // which is worse the further above 1 it goes. There is no value that
                        // is both, and 1.0 is the honest one.
                        Wide = wide + step * (bleed - 1f),
                        Deep = step * bleed,
                        Rot = rot
                    });
                }
            }
        }

        /// <summary>How thick a strip is when a shape does not say.</summary>
        private const float DefaultStep = 25f;

        /// <summary>
        /// How much bigger than its step each strip is cut, per shape, from "bleed".
        ///
        /// ONE IS ZERO OVERLAP AND ZERO GAP, which is the default and the honest answer. Above
        /// one hides the faint join between two soft-edged strips and pays for it with a darker
        /// band where they cross -- pick whichever of the two you can live with, because there
        /// is no number that is neither.
        /// </summary>
        private const float DefaultBleed = 1.0f;

        /// <summary>Past this many boxes, say so -- see the note where it is checked.</summary>
        private const int BoxWarnAt = 400;

        /// <summary>
        /// Reads turf.json.
        ///
        /// A missing file is not a problem and is not logged as one -- the discs above are a
        /// perfectly good fallback and an install that has never had this file should not be
        /// told off for it.
        /// </summary>
        public static Dictionary<string, List<TurfBox>> Load()
        {
            var all = new Dictionary<string, List<TurfBox>>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var doc = JsonFile.Read(Path.Combine(Paths.Data, "turf.json"));
                if (doc == null) return all;

                var turf = doc["turf"];

                foreach (var gangId in turf.Keys)
                {
                    var list = new List<TurfBox>();

                    foreach (var b in turf[gangId].Items)
                    {
                        // A SHAPE RATHER THAN A BOX. See Tile.
                        var poly = b["poly"];

                        if (poly != null && !poly.IsNull && poly.Count >= 3)
                        {
                            var pts = new List<Vector3>();

                            foreach (var p in poly.Items)
                            {
                                if (p.Count < 2) continue;

                                pts.Add(new Vector3(p[0].AsFloat(0f), p[1].AsFloat(0f), 0f));
                            }

                            if (pts.Count >= 3)
                            {
                                Tile(pts,
                                     b["rot"].AsFloat(0f),
                                     b["step"].AsFloat(DefaultStep),
                                     b["bleed"].AsFloat(DefaultBleed),
                                     b["zone"].AsString(""),
                                     list);
                            }

                            continue;
                        }

                        var wide = b["w"].AsFloat(0f);
                        var deep = b["h"].AsFloat(0f);

                        // A box with no size is a typo, and drawing it would put an invisible
                        // blip on the map that nobody can find to delete.
                        if (wide <= 1f || deep <= 1f) continue;

                        list.Add(new TurfBox
                        {
                            Zone = b["zone"].AsString(""),
                            At = new Vector3(b["x"].AsFloat(0f), b["y"].AsFloat(0f), 0f),
                            Wide = wide,
                            Deep = deep,
                            Rot = b["rot"].AsFloat(0f)
                        });
                    }

                    if (list.Count > 0) all[gangId] = list;
                }

                var total = 0;
                foreach (var pair in all) total += pair.Value.Count;

                // THE COUNT MATTERS AND IS WORTH SAYING. Every box is a blip, and the game's
                // blip pool is not enormous -- a shape cut too fine will quietly eat the
                // budget the rest of the mod's markers need, and the first symptom is other
                // blips silently not appearing rather than anything about turf.
                Log.Info("Turf shapes loaded: " + all.Count + " set(s), " + total + " box(es).");

                if (total > BoxWarnAt)
                {
                    Log.Warn("Turf map is using " + total + " blips. That is a lot -- raise " +
                             "\"step\" in turf.json to cut the shapes more coarsely if other " +
                             "map markers start going missing.");
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not read turf.json: " + ex.Message);
            }

            return all;
        }
    }
}
