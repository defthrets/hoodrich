using System;
using System.Collections.Generic;
using System.IO;
using GTA.Math;
using Hoodrich.Core;

namespace Hoodrich.Territory
{
    /// <summary>
    /// One box of a hood, straight out of the game's own zone bounds.
    ///
    /// GTA V defines every zone as a union of axis-aligned boxes, and that union is the
    /// neighbourhood's real shape -- already cut along the freeways and avenues, because that
    /// is how Rockstar drew them. Shading the boxes therefore puts the boundary exactly where
    /// the map's boundary is, which no hand-placed rectangle or circle over the middle of a
    /// zone was ever going to do.
    /// </summary>
    internal sealed class TurfBox
    {
        public string GangId = "";
        public string Zone = "";

        public float X;
        public float Y;
        public float Width;
        public float Height;

        public bool Contains(float x, float y)
        {
            return Math.Abs(x - X) <= Width * 0.5f && Math.Abs(y - Y) <= Height * 0.5f;
        }
    }

    /// <summary>Every gang's turf shape, as the boxes the map overlay is drawn from.</summary>
    internal sealed class TurfAreas
    {
        private const string FileName = "turf.json";

        private readonly List<TurfBox> _boxes = new List<TurfBox>();

        public IEnumerable<TurfBox> All => _boxes;
        public int Count => _boxes.Count;

        public static TurfAreas Load()
        {
            var areas = new TurfAreas();

            var path = Path.Combine(Paths.Data, FileName);
            var doc = JsonFile.Read(path);
            if (doc == null)
            {
                Log.Warn("No turf.json; gang turf will not be shaded on the map.");
                return areas;
            }

            foreach (var node in doc["areas"].Items)
            {
                var gang = node["gang"].AsString("");
                if (string.IsNullOrEmpty(gang)) continue;

                var w = node["w"].AsFloat();
                var h = node["h"].AsFloat();
                if (w < 1f || h < 1f) continue;

                areas._boxes.Add(new TurfBox
                {
                    GangId = gang,
                    Zone = node["zone"].AsString(""),
                    X = node["x"].AsFloat(),
                    Y = node["y"].AsFloat(),
                    Width = w,
                    Height = h
                });
            }

            Log.Info("Turf shapes loaded: " + areas._boxes.Count + " boxes.");
            return areas;
        }

        public IEnumerable<TurfBox> BoxesFor(string gangId, string zone)
        {
            foreach (var b in _boxes)
            {
                if (!string.Equals(b.GangId, gangId, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(b.Zone, zone, StringComparison.OrdinalIgnoreCase)) continue;
                yield return b;
            }
        }

        /// <summary>Whose hood a position is standing in, or null on neutral ground.</summary>
        public TurfBox AtPosition(Vector3 p)
        {
            foreach (var b in _boxes)
            {
                if (b.Contains(p.X, p.Y)) return b;
            }
            return null;
        }
    }
}
