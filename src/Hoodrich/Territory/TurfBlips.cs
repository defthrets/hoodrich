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
    /// One radius blip per claimed zone, centred and sized from the game's own zone bounds.
    /// A circle is an approximation of an irregular zone, so the radius is the equivalent-area
    /// one rather than a bounding one -- it under-covers rather than bleeding into neighbours,
    /// which reads better than overlapping washes of colour.
    ///
    /// Rebuilt whenever ownership changes, so a zone taken in a turf war immediately changes
    /// colour.
    /// </summary>
    internal sealed class TurfBlips
    {
        /// <summary>ADD_BLIP_FOR_RADIUS returns a raw handle, which Blip can be built around.</summary>
        private readonly List<Blip> _blips = new List<Blip>();
        private readonly GangRegistry _gangs;
        private readonly ZoneMap _zones;
        private readonly TerritoryState _territory;
        private readonly Settings _cfg;

        private string _signature = "";

        public TurfBlips(Settings cfg, GangRegistry gangs, ZoneMap zones, TerritoryState territory)
        {
            _cfg = cfg;
            _gangs = gangs;
            _zones = zones;
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
                    var zone = _zones.Get(code);
                    if (zone == null) continue;

                    AddBlip(zone, gang);
                }
            }

            Log.Info("Turf overlay: " + _blips.Count + " zones shaded.");
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

        private void AddBlip(ZoneInfo zone, GangDef gang)
        {
            try
            {
                var handle = Function.Call<int>(Hash.ADD_BLIP_FOR_RADIUS,
                    zone.Centre.X, zone.Centre.Y, zone.Centre.Z, zone.Radius);
                if (handle == 0) return;

                Function.Call(Hash.SET_BLIP_COLOUR, handle, gang.BlipColour);
                Function.Call(Hash.SET_BLIP_ALPHA, handle, _cfg.TurfBlipAlpha);

                // Radius blips are map-only furniture; keep them off the compass.
                Function.Call(Hash.SET_BLIP_AS_SHORT_RANGE, handle, true);

                _blips.Add(new Blip(handle));
            }
            catch (Exception ex)
            {
                Log.Debug("Could not shade " + zone.Code + ": " + ex.Message);
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
