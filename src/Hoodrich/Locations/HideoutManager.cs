using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Gangs;
using Hoodrich.Territory;
using Hoodrich.UI;

namespace Hoodrich.Locations
{
    /// <summary>
    /// Finds property to buy, and keeps what you bought.
    ///
    /// A listing appears lazily the first time you stand on a block that a gang holds, and the
    /// spot is then fixed forever -- so a hideout you passed on is still there when you come
    /// back with the money. Listings are only ever created on claimed turf, which keeps them
    /// somewhere that means something rather than scattered across the map.
    ///
    /// No coordinates are shipped: a listing's position is found at runtime on a real pavement
    /// inside the zone, then saved.
    /// </summary>
    internal sealed class HideoutManager
    {
        private const float MarkerRange = 70f;
        private const float UseRange = 2.5f;
        private const int UpdateIntervalMs = 700;

        private readonly Dictionary<string, Hideout> _byZone =
            new Dictionary<string, Hideout>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, Blip> _blips =
            new Dictionary<string, Blip>(StringComparer.OrdinalIgnoreCase);

        private readonly Settings _cfg;
        private readonly Random _rng = new Random();

        private int _lastUpdate;

        public HideoutManager(Settings cfg)
        {
            _cfg = cfg;
        }

        public IEnumerable<Hideout> All => _byZone.Values;

        public int OwnedCount
        {
            get
            {
                var n = 0;
                foreach (var h in _byZone.Values) if (h.Owned) n++;
                return n;
            }
        }

        public bool AtCap => OwnedCount >= _cfg.MaxHideouts;

        /// <summary>The listing on the block the player is standing on, owned or not.</summary>
        public Hideout InZone(string zoneCode)
        {
            if (string.IsNullOrEmpty(zoneCode)) return null;
            return _byZone.TryGetValue(zoneCode, out var h) ? h : null;
        }

        public float DistanceTo(Hideout h)
        {
            if (h == null) return float.MaxValue;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return float.MaxValue;

            return player.Position.DistanceTo(h.Position);
        }

        /// <summary>The hideout the player is standing in, if any.</summary>
        public Hideout AtPlayer
        {
            get
            {
                foreach (var h in _byZone.Values)
                {
                    if (DistanceTo(h) <= UseRange) return h;
                }
                return null;
            }
        }

        /// <summary>Closest hideout the player owns, for wayfinding.</summary>
        public Hideout NearestOwned
        {
            get
            {
                Hideout best = null;
                var bestDistance = float.MaxValue;

                foreach (var h in _byZone.Values)
                {
                    if (!h.Owned) continue;
                    var d = DistanceTo(h);
                    if (d >= bestDistance) continue;
                    bestDistance = d;
                    best = h;
                }

                return best;
            }
        }

        // ---- listings ----------------------------------------------------------

        /// <summary>What a place on this block costs. Better turf, higher price.</summary>
        public int PriceFor(string zoneCode)
        {
            return _cfg.HideoutBasePrice;
        }

        /// <summary>
        /// Creates the listing for a block the first time the player stands on it. Only ever
        /// on turf somebody claims -- neutral ground has nothing worth buying into.
        /// </summary>
        private void ListIfNeeded(string zoneCode, string zoneName, GangDef owner)
        {
            if (string.IsNullOrEmpty(zoneCode)) return;
            if (owner == null) return;
            if (_byZone.ContainsKey(zoneCode)) return;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            if (!TryPlace(player.Position, zoneCode, out var spot)) return;

            var hideout = new Hideout(zoneCode, zoneName, spot, PriceFor(zoneCode), _cfg.HideoutStashCapacity);
            _byZone[zoneCode] = hideout;

            Notify.Ticker("~y~Somewhere for sale on " + zoneName + "~s~ -- $" + hideout.Price.ToString("N0"));
            Log.Info("Hideout listed in " + zoneCode + " at $" + hideout.Price + ".");
        }

        private bool TryPlace(Vector3 origin, string zone, out Vector3 spot)
        {
            spot = Vector3.Zero;

            for (var attempt = 0; attempt < 12; attempt++)
            {
                var angle = _rng.NextDouble() * Math.PI * 2.0;
                var distance = 40f + (float)_rng.NextDouble() * 70f;

                var candidate = origin + new Vector3(
                    (float)Math.Cos(angle) * distance, (float)Math.Sin(angle) * distance, 0f);

                Vector3 onFoot;
                try { onFoot = World.GetNextPositionOnSidewalk(candidate); }
                catch { continue; }

                if (onFoot == Vector3.Zero) continue;

                // It has to actually be on the block we are listing.
                var code = Function.Call<string>(Hash.GET_NAME_OF_ZONE, onFoot.X, onFoot.Y, onFoot.Z);
                if (!string.Equals(code, zone, StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    if (World.GetGroundHeight(onFoot, out var groundZ, GetGroundHeightMode.Normal))
                    {
                        onFoot.Z = groundZ;
                    }
                }
                catch
                {
                    // Sidewalk Z is usually fine.
                }

                spot = onFoot;
                return true;
            }

            return false;
        }

        // ---- buying ------------------------------------------------------------

        /// <summary>Returns a player-facing refusal, or null on purchase.</summary>
        public string Buy(Hideout hideout)
        {
            if (hideout == null) return "Nothing for sale here.";
            if (hideout.Owned) return "You already own this one.";
            if (AtCap) return "You already hold " + _cfg.MaxHideouts + " places. Sell one first.";

            if (Game.Player.Money < hideout.Price)
            {
                return "Short $" + (hideout.Price - Game.Player.Money).ToString("N0") + ".";
            }

            Game.Player.Money -= hideout.Price;
            hideout.Owned = true;

            Notify.Important("~g~" + hideout.ZoneName + " is yours.~s~ -$" + hideout.Price.ToString("N0"));
            Log.Info("Bought hideout in " + hideout.ZoneCode + " for $" + hideout.Price + ".");
            return null;
        }

        /// <summary>Sells back at a fraction of what was paid. Product inside comes with you or is lost.</summary>
        public string Sell(Hideout hideout)
        {
            if (hideout == null || !hideout.Owned) return "You do not own this.";
            if (!hideout.Stash.IsEmpty) return "Empty it first.";

            var back = (int)(hideout.Price * _cfg.HideoutSellbackPercent / 100f);
            Game.Player.Money += back;
            hideout.Owned = false;

            Notify.Ticker("~g~+$" + back.ToString("N0") + "~s~ for " + hideout.ZoneName + ".");
            return null;
        }

        // ---- per-tick ----------------------------------------------------------

        public void Update(TurfWatch turf)
        {
            var now = Game.GameTime;
            if (now - _lastUpdate < UpdateIntervalMs) return;
            _lastUpdate = now;

            ListIfNeeded(turf.ZoneCode, turf.ZoneName, turf.Owner);
            SyncBlips();
        }

        private void SyncBlips()
        {
            foreach (var kv in _byZone)
            {
                var h = kv.Value;

                // Owned places are always on the map; listings only show once you are nearby.
                var wantBlip = h.Owned || DistanceTo(h) <= MarkerRange * 3f;

                if (!wantBlip)
                {
                    if (_blips.TryGetValue(h.Id, out var stale))
                    {
                        try { if (stale != null && stale.Exists()) stale.Delete(); } catch { }
                        _blips.Remove(h.Id);
                    }
                    continue;
                }

                if (_blips.TryGetValue(h.Id, out var existing) && existing != null && existing.Exists())
                {
                    existing.Color = h.Owned ? BlipColor.Green : BlipColor.Yellow;
                    continue;
                }

                try
                {
                    var blip = World.CreateBlip(h.Position);
                    if (blip == null || !blip.Exists()) continue;

                    blip.Sprite = BlipSprite.Safehouse;
                    blip.Color = h.Owned ? BlipColor.Green : BlipColor.Yellow;
                    blip.Name = h.Owned ? "Hideout -- " + h.ZoneName : "For sale -- " + h.ZoneName;
                    blip.IsShortRange = !h.Owned;
                    blip.Scale = 0.85f;

                    _blips[h.Id] = blip;
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not blip a hideout: " + ex.Message);
                }
            }
        }

        /// <summary>Ground markers, drawn only for places close enough to walk to.</summary>
        public void Draw()
        {
            foreach (var h in _byZone.Values)
            {
                var distance = DistanceTo(h);
                if (distance > MarkerRange) continue;

                try
                {
                    var colour = h.Owned
                        ? (distance <= UseRange ? Palette.Cash : Palette.Accent)
                        : Palette.Warn;

                    World.DrawMarker(MarkerType.Cylinder,
                                     h.Position, Vector3.Zero, Vector3.Zero,
                                     new Vector3(1.4f, 1.4f, 0.8f),
                                     Color.FromArgb(140, colour.R, colour.G, colour.B),
                                     false, false, false, null, null, false);
                }
                catch (Exception ex)
                {
                    Log.Debug("Hideout marker failed: " + ex.Message);
                }
            }
        }

        public void RestoreWorld()
        {
            foreach (var kv in _blips)
            {
                try { if (kv.Value != null && kv.Value.Exists()) kv.Value.Delete(); } catch { }
            }
            _blips.Clear();
        }

        // ---- persistence -------------------------------------------------------

        public Json ToJson()
        {
            var arr = Json.Array();
            foreach (var h in _byZone.Values) arr.Add(h.ToJson());
            return Json.Object().Set("hideouts", arr);
        }

        public void LoadFrom(Json node)
        {
            _byZone.Clear();
            if (node == null || node.IsNull) return;

            foreach (var item in node["hideouts"].Items)
            {
                var h = Hideout.FromJson(item);
                if (h == null) continue;
                _byZone[h.ZoneCode] = h;
            }

            Log.Info("Hideouts loaded: " + _byZone.Count + " known, " + OwnedCount + " owned.");
        }
    }
}
