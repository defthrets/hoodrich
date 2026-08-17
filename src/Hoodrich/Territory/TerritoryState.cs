using System;
using System.Collections.Generic;
using GTA;
using Hoodrich.Core;

namespace Hoodrich.Territory
{
    /// <summary>
    /// Who owns what, after the map has been fought over.
    ///
    /// gangs.json holds the STARTING map. This holds every change made since: a zone captured
    /// in a turf war gets an owner override here, and that is what everything else reads. The
    /// starting data is never rewritten, so a player can always be reset by deleting the save.
    ///
    /// Zones also carry a VALUE. Richer turf pays more and takes more to hold, and value creeps
    /// up on its own while a gang keeps hold of it -- so a block you have owned for a while is
    /// worth defending and worth taking off someone.
    ///
    /// The value model is adapted from lucasvinbr's GTA5GangMod (MIT).
    /// </summary>
    internal sealed class TerritoryState
    {
        private readonly Dictionary<string, string> _owner =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, int> _value =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private readonly Settings _cfg;
        private readonly Random _rng = new Random();

        private int _lastUpgrade;

        public TerritoryState(Settings cfg)
        {
            _cfg = cfg;
        }

        /// <summary>Gang id that has taken this zone since the game started, or null.</summary>
        public string OwnerOverride(string zoneCode)
        {
            if (string.IsNullOrEmpty(zoneCode)) return null;
            return _owner.TryGetValue(zoneCode, out var g) ? g : null;
        }

        public void SetOwner(string zoneCode, string gangId)
        {
            if (string.IsNullOrEmpty(zoneCode)) return;

            _owner[zoneCode] = gangId ?? "";

            // A freshly taken block is worth little until it is worked.
            _value[zoneCode] = 0;
        }

        /// <summary>0..MaxTurfValue. Drives payout, and how hard the zone is to take.</summary>
        public int ValueOf(string zoneCode)
        {
            if (string.IsNullOrEmpty(zoneCode)) return 0;
            return _value.TryGetValue(zoneCode, out var v) ? v : 0;
        }

        /// <summary>Every owned zone creeps up in value while it is held.</summary>
        public void UpgradeTick()
        {
            if (_cfg.TurfUpgradeMinutes <= 0f) return;

            var now = Game.GameTime;
            var intervalMs = (int)(_cfg.TurfUpgradeMinutes * 60_000f);
            if (_lastUpgrade != 0 && now - _lastUpgrade < intervalMs) return;
            _lastUpgrade = now;

            var zones = new List<string>(_owner.Keys);
            foreach (var zone in zones)
            {
                if (string.IsNullOrEmpty(_owner[zone])) continue;

                var current = ValueOf(zone);
                if (current >= _cfg.MaxTurfValue) continue;

                // Not every zone every tick -- turf develops unevenly.
                if (_rng.NextDouble() > 0.5) continue;

                _value[zone] = Math.Min(_cfg.MaxTurfValue, current + 1);
            }
        }

        /// <summary>Zones the given gang holds by conquest (not counting their starting turf).</summary>
        public List<string> ZonesHeldBy(string gangId)
        {
            var held = new List<string>();
            if (string.IsNullOrEmpty(gangId)) return held;

            foreach (var kv in _owner)
            {
                if (string.Equals(kv.Value, gangId, StringComparison.OrdinalIgnoreCase)) held.Add(kv.Key);
            }
            return held;
        }

        // ---- persistence -------------------------------------------------------

        public Json ToJson()
        {
            var owners = Json.Object();
            foreach (var kv in _owner) owners.Set(kv.Key, kv.Value);

            var values = Json.Object();
            foreach (var kv in _value) values.Set(kv.Key, kv.Value);

            return Json.Object().Set("owners", owners).Set("values", values);
        }

        public void LoadFrom(Json node)
        {
            _owner.Clear();
            _value.Clear();
            if (node == null || node.IsNull) return;

            var owners = node["owners"];
            foreach (var key in owners.Keys) _owner[key] = owners[key].AsString("");

            var values = node["values"];
            foreach (var key in values.Keys)
            {
                _value[key] = Math.Max(0, Math.Min(_cfg.MaxTurfValue, values[key].AsInt(0)));
            }

            Log.Info("Territory loaded: " + _owner.Count + " zones changed hands.");
        }
    }
}
