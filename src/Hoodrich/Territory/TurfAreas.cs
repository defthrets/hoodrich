using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GTA.Math;
using Hoodrich.Core;

namespace Hoodrich.Territory
{
    /// <summary>
    /// One shaded block of gang turf: a rotated rectangle over real streets.
    /// </summary>
    internal sealed class TurfArea
    {
        public string GangId = "";
        public string Zone = "";
        public string Name = "";

        public float X;
        public float Y;
        public float Width = 200f;
        public float Height = 200f;

        /// <summary>Degrees clockwise from north, matching SET_BLIP_ROTATION.</summary>
        public float Rotation;

        public Vector3 Centre => new Vector3(X, Y, 0f);

        /// <summary>
        /// True when a world position falls inside the rectangle. Rotating the point back into
        /// the rectangle's own frame is cheaper and exact compared with building four edges.
        /// </summary>
        public bool Contains(Vector3 p)
        {
            var rad = Rotation * (float)Math.PI / 180f;
            var cos = (float)Math.Cos(-rad);
            var sin = (float)Math.Sin(-rad);

            var dx = p.X - X;
            var dy = p.Y - Y;

            var lx = dx * cos - dy * sin;
            var ly = dx * sin + dy * cos;

            return Math.Abs(lx) <= Width * 0.5f && Math.Abs(ly) <= Height * 0.5f;
        }

        public TurfArea Clone()
        {
            return new TurfArea
            {
                GangId = GangId,
                Zone = Zone,
                Name = Name,
                X = X,
                Y = Y,
                Width = Width,
                Height = Height,
                Rotation = Rotation
            };
        }
    }

    /// <summary>
    /// The gang turf overlay, as blocks rather than zones.
    ///
    /// A zone code answers "whose part of town is this", which is all the dealing rules need.
    /// It is far too coarse to draw, though: shading the whole of DAVIS covers streets no gang
    /// has ever set foot on. These rectangles are what actually gets shaded on the map.
    ///
    /// Shipped placements are approximate on purpose. The in-game editor writes over them to
    /// the writable copy, so the player can walk a block onto the exact streets they mean and
    /// keep it across updates.
    /// </summary>
    internal sealed class TurfAreas
    {
        private const string FileName = "turf.json";

        private readonly List<TurfArea> _areas = new List<TurfArea>();

        public IEnumerable<TurfArea> All => _areas;
        public int Count => _areas.Count;

        /// <summary>Bumped on every change, so the map overlay knows to rebuild.</summary>
        public int Revision { get; private set; }

        public static TurfAreas Load()
        {
            var areas = new TurfAreas();

            // The writable copy is the player's own edits and wins outright; a partial merge
            // would resurrect blocks they deliberately deleted.
            var path = Path.Combine(Paths.Writable, FileName);
            var edited = File.Exists(path);
            if (!edited) path = Path.Combine(Paths.Data, FileName);

            var doc = JsonFile.Read(path);
            if (doc == null)
            {
                Log.Warn("No turf.json; gang turf falls back to whole-zone circles.");
                return areas;
            }

            foreach (var node in doc["areas"].Items)
            {
                var gang = node["gang"].AsString("");
                if (string.IsNullOrEmpty(gang)) continue;

                areas._areas.Add(new TurfArea
                {
                    GangId = gang,
                    Zone = node["zone"].AsString(""),
                    Name = node["name"].AsString(""),
                    X = node["x"].AsFloat(),
                    Y = node["y"].AsFloat(),
                    Width = Math.Max(20f, node["w"].AsFloat(200f)),
                    Height = Math.Max(20f, node["h"].AsFloat(200f)),
                    Rotation = node["rot"].AsFloat()
                });
            }

            Log.Info("Turf blocks loaded: " + areas._areas.Count +
                     (edited ? " (from your edited copy)." : "."));
            return areas;
        }

        public bool Save()
        {
            var doc = Json.Object();
            doc.Set("_comment", Json.Str(
                "Edited in game via the wheel: Turf > Edit blocks. Overrides the shipped data\\turf.json."));

            var list = Json.Array();
            foreach (var a in _areas)
            {
                var node = Json.Object();
                node.Set("gang", a.GangId);
                node.Set("zone", a.Zone);
                node.Set("name", a.Name);
                node.Set("x", Round(a.X));
                node.Set("y", Round(a.Y));
                node.Set("w", Round(a.Width));
                node.Set("h", Round(a.Height));
                node.Set("rot", Round(a.Rotation));
                list.Add(node);
            }

            doc.Set("areas", list);

            var path = Path.Combine(Paths.Writable, FileName);
            if (!JsonFile.Write(path, doc)) return false;

            Log.Info("Turf blocks saved to " + path);
            return true;
        }

        private static double Round(float v) =>
            double.Parse(v.ToString("0.##", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        public List<TurfArea> ForGang(string gangId)
        {
            var list = new List<TurfArea>();
            if (string.IsNullOrEmpty(gangId)) return list;

            foreach (var a in _areas)
            {
                if (string.Equals(a.GangId, gangId, StringComparison.OrdinalIgnoreCase)) list.Add(a);
            }
            return list;
        }

        /// <summary>True when any block is drawn for this zone, i.e. no circle fallback needed.</summary>
        public bool HasZone(string gangId, string zone)
        {
            foreach (var a in _areas)
            {
                if (string.Equals(a.GangId, gangId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(a.Zone, zone, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>The block a position stands on, or null out on neutral ground.</summary>
        public TurfArea AtPosition(Vector3 p)
        {
            foreach (var a in _areas)
            {
                if (a.Contains(p)) return a;
            }
            return null;
        }

        /// <summary>Nearest block to a position, used by the editor to pick what to nudge.</summary>
        public TurfArea Nearest(Vector3 p, float maxDistance = 400f)
        {
            TurfArea best = null;
            var bestSq = maxDistance * maxDistance;

            foreach (var a in _areas)
            {
                var dx = a.X - p.X;
                var dy = a.Y - p.Y;
                var sq = dx * dx + dy * dy;
                if (sq > bestSq) continue;

                bestSq = sq;
                best = a;
            }

            return best;
        }

        public void Add(TurfArea area)
        {
            if (area == null) return;
            _areas.Add(area);
            Revision++;
        }

        public void Remove(TurfArea area)
        {
            if (area == null) return;
            if (_areas.Remove(area)) Revision++;
        }

        /// <summary>Called by the editor after moving or resizing so the overlay redraws.</summary>
        public void Touch() => Revision++;
    }
}
