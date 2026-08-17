using System;
using System.Collections.Generic;
using System.IO;
using GTA;
using GTA.Math;
using Hoodrich.Core;

namespace Hoodrich.Territory
{
    /// <summary>Where a named zone actually is on the map.</summary>
    internal sealed class ZoneInfo
    {
        public string Code = "";
        public string Name = "";

        /// <summary>Centre of the zone's bounds. Z is found against the ground at runtime.</summary>
        public Vector3 Centre;

        /// <summary>Circle approximating the zone, for the minimap overlay.</summary>
        public float Radius = 100f;
    }

    /// <summary>
    /// Zone geometry, from the game's own bounds data.
    ///
    /// Up to now Hoodrich only ever knew a zone by NAME, which was enough to answer "whose
    /// block am I standing on" but not "where is it" -- so nothing could be put on the map
    /// before the player had already walked there. This supplies the missing half: a real
    /// centre and radius per zone, so gang turf can be shaded on the minimap and gang leaders
    /// can be marked somewhere you have never been.
    /// </summary>
    internal sealed class ZoneMap
    {
        private readonly Dictionary<string, ZoneInfo> _byCode =
            new Dictionary<string, ZoneInfo>(StringComparer.OrdinalIgnoreCase);

        public int Count => _byCode.Count;

        public ZoneInfo Get(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            return _byCode.TryGetValue(code, out var z) ? z : null;
        }

        public static ZoneMap Load()
        {
            var map = new ZoneMap();

            var path = Path.Combine(Paths.Data, "zones.json");
            var doc = JsonFile.Read(path);
            if (doc == null)
            {
                Log.Warn("No zones.json; turf will not be shaded on the map and leaders will not be marked.");
                return map;
            }

            foreach (var node in doc["zones"].Items)
            {
                var code = node["code"].AsString("");
                if (string.IsNullOrEmpty(code)) continue;

                map._byCode[code] = new ZoneInfo
                {
                    Code = code,
                    Name = node["name"].AsString(code),
                    Centre = new Vector3(node["x"].AsFloat(), node["y"].AsFloat(), 0f),
                    Radius = Math.Max(20f, node["radius"].AsFloat(100f))
                };
            }

            Log.Info("Zone geometry loaded: " + map._byCode.Count + " zones.");
            return map;
        }

        /// <summary>
        /// A usable spot near a zone's centre: on a pavement, on the ground.
        /// Falls back to the raw centre so a caller always gets something.
        /// </summary>
        public Vector3 GroundedCentre(string code)
        {
            var zone = Get(code);
            if (zone == null) return Vector3.Zero;

            var spot = zone.Centre;

            try
            {
                var onFoot = World.GetNextPositionOnSidewalk(spot);
                if (onFoot != Vector3.Zero) spot = onFoot;
            }
            catch
            {
                // Keep the raw centre.
            }

            try
            {
                if (World.GetGroundHeight(spot, out var groundZ, GetGroundHeightMode.Normal))
                {
                    spot.Z = groundZ;
                }
            }
            catch
            {
                // Ground probe only works on streamed terrain; the map blip does not care.
            }

            return spot;
        }
    }
}
