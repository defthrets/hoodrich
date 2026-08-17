using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Economy;
using Hoodrich.Gangs;
using Hoodrich.UI;

namespace Hoodrich.Locations
{
    /// <summary>
    /// Your crew's house, and the stash inside it.
    ///
    /// A den is placed once, on the gang's own turf, and then SAVED -- so unlike the corner
    /// dealer, who picks a fresh pitch each session, your den is in the same place forever.
    /// That is what makes it somewhere you go back to rather than somewhere you find again.
    ///
    /// The stash it holds is the answer to carry capacity: product parked here is off your
    /// person, so it cannot be lost when you die or get arrested.
    ///
    /// Den-as-a-place is the idea taken from Los Santos RED (unlicensed -- mechanic only).
    /// </summary>
    internal sealed class GangDen
    {
        private const float MarkerRange = 60f;
        private const float UseRange = 2.5f;
        private const int UpdateIntervalMs = 700;

        /// <summary>Where each gang's den ended up, keyed by gang id. Persisted.</summary>
        private readonly Dictionary<string, Vector3> _dens =
            new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Product parked at the den. Not carried, so never lost.</summary>
        public readonly Stash Stash = new Stash { Capacity = 5000f };

        private readonly Affiliation _crew;
        private readonly Random _rng = new Random();

        private Blip _blip;
        private string _blipGang = "";
        private int _lastUpdate;

        public GangDen(Affiliation crew)
        {
            _crew = crew;
        }

        /// <summary>The den position for the player's crew, if one has been established.</summary>
        public Vector3? Current
        {
            get
            {
                if (!_crew.IsAffiliated) return null;
                return _dens.TryGetValue(_crew.Current.Id, out var v) ? v : (Vector3?)null;
            }
        }

        public bool HasDen => Current.HasValue;

        public float Distance
        {
            get
            {
                var den = Current;
                if (!den.HasValue) return float.MaxValue;

                var player = Game.Player.Character;
                if (player == null || !player.Exists()) return float.MaxValue;

                return player.Position.DistanceTo(den.Value);
            }
        }

        /// <summary>True when the player is standing in the den and can use the stash.</summary>
        public bool IsPlayerAtDen => Distance <= UseRange;

        // ---- placement ---------------------------------------------------------

        /// <summary>
        /// Establishes the den the first time the player stands on their crew's turf, then
        /// never moves it.
        /// </summary>
        public void EstablishIfNeeded(string currentZone, GangDef gang)
        {
            if (gang == null || string.IsNullOrEmpty(currentZone)) return;
            if (_dens.ContainsKey(gang.Id)) return;

            // Only place it on ground the gang actually holds.
            var onTurf = false;
            foreach (var z in gang.Turf)
            {
                if (string.Equals(z, currentZone, StringComparison.OrdinalIgnoreCase)) { onTurf = true; break; }
            }
            if (!onTurf) return;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            if (!TryPlace(player.Position, currentZone, out var spot)) return;

            _dens[gang.Id] = spot;
            Notify.Important("~g~" + gang.Name + " den~s~ -- marked on your map. Stash your product here.");
            Log.Info("Den established for " + gang.Id + " in " + currentZone + " at " + spot + ".");
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

                // It has to be on the crew's own block.
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

        // ---- per-tick ----------------------------------------------------------

        public void Update(string currentZone)
        {
            var now = Game.GameTime;
            if (now - _lastUpdate < UpdateIntervalMs) return;
            _lastUpdate = now;

            if (_crew.IsAffiliated) EstablishIfNeeded(currentZone, _crew.Current);

            SyncBlip();
        }

        private void SyncBlip()
        {
            var wanted = _crew.IsAffiliated ? _crew.Current.Id : "";

            if (_blipGang != wanted || (!string.IsNullOrEmpty(wanted) && !HasDen))
            {
                ClearBlip();
                _blipGang = wanted;
            }

            if (!HasDen || (_blip != null && _blip.Exists())) return;

            try
            {
                _blip = World.CreateBlip(Current.Value);
                if (_blip == null || !_blip.Exists()) return;

                _blip.Sprite = BlipSprite.Safehouse;
                _blip.Color = BlipColor.Green;
                _blip.Name = _crew.Current.Name + " den";
                _blip.IsShortRange = false;
                _blip.Scale = 0.9f;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not blip the den: " + ex.Message);
            }
        }

        /// <summary>Ground marker, drawn only when you are close enough to see it.</summary>
        public void Draw()
        {
            var den = Current;
            if (!den.HasValue) return;
            if (Distance > MarkerRange) return;

            try
            {
                var colour = IsPlayerAtDen ? Palette.Cash : Palette.Accent;

                World.DrawMarker(MarkerType.Cylinder,
                                 den.Value, Vector3.Zero, Vector3.Zero,
                                 new Vector3(1.4f, 1.4f, 0.8f),
                                 Color.FromArgb(140, colour.R, colour.G, colour.B),
                                 false, false, false, null, null, false);
            }
            catch (Exception ex)
            {
                Log.Debug("Den marker failed: " + ex.Message);
            }
        }

        public void ClearBlip()
        {
            try { if (_blip != null && _blip.Exists()) _blip.Delete(); } catch { }
            _blip = null;
        }

        public void RestoreWorld() => ClearBlip();

        // ---- persistence -------------------------------------------------------

        public Json ToJson()
        {
            var dens = Json.Object();
            foreach (var kv in _dens)
            {
                dens.Set(kv.Key, Json.Array()
                    .Add(Json.Number(Math.Round(kv.Value.X, 2)))
                    .Add(Json.Number(Math.Round(kv.Value.Y, 2)))
                    .Add(Json.Number(Math.Round(kv.Value.Z, 2))));
            }

            return Json.Object().Set("dens", dens).Set("stash", Stash.ToJson());
        }

        public void LoadFrom(Json node)
        {
            _dens.Clear();
            if (node == null || node.IsNull) return;

            var dens = node["dens"];
            foreach (var key in dens.Keys)
            {
                var arr = dens[key];
                if (arr.Count < 3) continue;
                _dens[key] = new Vector3(arr[0].AsFloat(), arr[1].AsFloat(), arr[2].AsFloat());
            }

            Stash.LoadFrom(node["stash"]);
            Log.Info("Dens loaded: " + _dens.Count + ", stash " + Stash.Total.ToString("F1") + "g.");
        }
    }
}
