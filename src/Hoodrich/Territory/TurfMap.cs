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
        public void Show(GangRegistry gangs, ZoneMap zones, Dictionary<string, List<TurfBox>> turf)
        {
            Hide();

            if (gangs == null) return;

            var boxes = 0;
            var discs = 0;

            foreach (var gang in gangs.All)
            {
                if (gang == null) continue;

                List<TurfBox> mine = null;

                if (turf != null) turf.TryGetValue(gang.Id, out mine);

                if (mine != null && mine.Count > 0)
                {
                    foreach (var box in mine)
                    {
                        if (Box(box, gang)) boxes++;
                    }

                    continue;
                }

                // Nothing shaped for this set, so the old behaviour rather than a gap.
                if (zones == null) continue;

                foreach (var code in gang.Turf)
                {
                    var zone = zones.Get(code);
                    if (zone == null) continue;

                    if (Disc(zone.Centre, zone.Radius, zone.Name + " -- " + gang.Name, gang))
                    {
                        discs++;
                    }
                }
            }

            Log.Info("Turf map: " + boxes + " box(es)" +
                     (discs > 0 ? " and " + discs + " unshaped zone(s) as discs." : "."));
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

        private bool Disc(Vector3 at, float radius, string name, GangDef gang)
        {
            try
            {
                var blip = World.CreateBlip(at, radius);
                if (blip == null || !blip.Exists()) return false;

                Function.Call(Hash.SET_BLIP_COLOUR, blip.Handle, gang.BlipColour);
                Function.Call(Hash.SET_BLIP_ALPHA, blip.Handle, Wash);

                blip.Name = name;

                _boxes.Add(blip);
                return true;
            }
            catch
            {
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

                Log.Info("Turf shapes loaded: " + all.Count + " set(s).");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not read turf.json: " + ex.Message);
            }

            return all;
        }
    }
}
