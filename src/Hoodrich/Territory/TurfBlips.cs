using System;
using System.Collections.Generic;
using GTA;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Gangs;

namespace Hoodrich.Territory
{
    /// <summary>
    /// Gang turf, shaded on the map in each gang's colour.
    ///
    /// Drawn from the game's own zone bounds: every zone is a union of boxes, and shading
    /// those boxes puts the boundary exactly where the map's boundary is. A single rectangle
    /// or circle over the middle of a zone is what left the minimap under overlapping washes
    /// of colour that stopped nowhere in particular.
    ///
    /// Each claimed zone also gets one small icon blip at its centre, so the shading reads as
    /// somebody's turf rather than as an unexplained wash of colour.
    ///
    /// Rebuilt whenever ownership changes, so a zone taken in a turf war immediately changes
    /// colour.
    /// </summary>
    internal sealed class TurfBlips
    {
        /// <summary>Gang skull. The same sprite the game uses for its own gang attack blips.</summary>
        private const int GangIconSprite = 84;

        private readonly List<Blip> _blips = new List<Blip>();
        private readonly GangRegistry _gangs;
        private readonly ZoneMap _zones;
        private readonly TurfAreas _areas;
        private readonly TerritoryState _territory;
        private readonly Settings _cfg;

        private string _signature = "";

        public TurfBlips(Settings cfg, GangRegistry gangs, ZoneMap zones, TurfAreas areas,
                         TerritoryState territory)
        {
            _cfg = cfg;
            _gangs = gangs;
            _zones = zones;
            _areas = areas;
            _territory = territory;
        }

        /// <summary>
        /// Rebuilds only when ownership actually differs, so this is cheap to call on a timer.
        /// </summary>
        public void Refresh()
        {
            if (!_cfg.ShowTurfOnMap)
            {
                if (_blips.Count > 0) Clear();
                return;
            }

            var signature = BuildSignature();
            if (signature == _signature && _blips.Count > 0) return;

            _signature = signature;
            Clear();

            foreach (var gang in _gangs.All)
            {
                foreach (var code in ClaimedBy(gang))
                {
                    var drewShape = false;

                    foreach (var box in _areas.BoxesFor(gang.Id, code))
                    {
                        AddBox(box, gang);
                        drewShape = true;
                    }

                    var zone = _zones.Get(code);
                    if (zone == null) continue;

                    // A zone with no boxes gets nothing rather than a blob: an honest gap
                    // beats shading streets that are not anybody's.
                    if (drewShape) AddIcon(zone, gang);
                }
            }

            Log.Info("Turf overlay: " + _blips.Count + " map markers.");
        }

        /// <summary>Every zone this gang holds, starting map plus anything they have taken.</summary>
        private IEnumerable<string> ClaimedBy(GangDef gang)
        {
            var codes = new List<string>();

            foreach (var code in gang.Turf)
            {
                // A zone someone else has captured is no longer theirs.
                var captured = _territory.OwnerOverride(code);
                if (!string.IsNullOrEmpty(captured) &&
                    !string.Equals(captured, gang.Id, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!codes.Contains(code)) codes.Add(code);
            }

            foreach (var code in _territory.ZonesHeldBy(gang.Id))
            {
                if (!codes.Contains(code)) codes.Add(code);
            }

            return codes;
        }

        /// <summary>One box of a hood's real footprint.</summary>
        private void AddBox(TurfBox box, GangDef gang)
        {
            try
            {
                var handle = Function.Call<int>(Hash.ADD_BLIP_FOR_AREA,
                    box.X, box.Y, 0f, box.Width, box.Height);
                if (handle == 0) return;
                Function.Call(Hash.SET_BLIP_COLOUR, handle, gang.BlipColour);
                Function.Call(Hash.SET_BLIP_ALPHA, handle, _cfg.TurfBlipAlpha);

                // Area blips are map furniture; keep them off the compass.
                Function.Call(Hash.SET_BLIP_AS_SHORT_RANGE, handle, true);

                _blips.Add(new Blip(handle));
            }
            catch (Exception ex)
            {
                Log.Debug("Could not shade part of " + box.Zone + ": " + ex.Message);
            }
        }

        /// <summary>The gang's mark in the middle of the shading, named on the map legend.</summary>
        private void AddIcon(ZoneInfo zone, GangDef gang)
        {
            try
            {
                var handle = Function.Call<int>(Hash.ADD_BLIP_FOR_COORD,
                    zone.Centre.X, zone.Centre.Y, zone.Centre.Z);
                if (handle == 0) return;

                Function.Call(Hash.SET_BLIP_SPRITE, handle, GangIconSprite);
                Function.Call(Hash.SET_BLIP_COLOUR, handle, gang.BlipColour);
                Function.Call(Hash.SET_BLIP_SCALE, handle, 0.7f);
                Function.Call(Hash.SET_BLIP_AS_SHORT_RANGE, handle, true);

                var blip = new Blip(handle);
                try { blip.Name = gang.Name + " turf"; }
                catch { /* the shading still reads without a legend entry */ }

                _blips.Add(blip);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not mark " + zone.Code + ": " + ex.Message);
            }
        }

        private string BuildSignature()
        {
            var parts = new List<string>();
            foreach (var gang in _gangs.All)
            {
                foreach (var code in ClaimedBy(gang)) parts.Add(gang.Id + ":" + code);
            }
            parts.Sort(StringComparer.Ordinal);

            return string.Join("|", parts.ToArray()) + "|a" + _cfg.TurfBlipAlpha;
        }

        public void Clear()
        {
            foreach (var blip in _blips)
            {
                try { if (blip != null && blip.Exists()) blip.Delete(); } catch { }
            }
            _blips.Clear();
        }

        public void RestoreWorld() => Clear();
    }
}
