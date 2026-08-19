using System;
using System.Collections.Generic;
using GTA;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Social;

namespace Hoodrich.Territory
{
    /// <summary>
    /// Police the player has killed.
    ///
    /// Its own watcher rather than a branch inside the gang kill scan, because this is not a
    /// gang matter -- it has nothing to do with who you run with, it does not move anybody's
    /// rep, and the city reacts to it whether or not you are affiliated. The feed is the only
    /// thing that cares, and it cares a great deal.
    /// </summary>
    internal sealed class CopWatch
    {
        private const float ScanRange = 90f;
        private const int UpdateIntervalMs = 900;

        /// <summary>PED_TYPE values that count as police.</summary>
        private static readonly int[] CopTypes = { 6, 27, 29 };

        /// <summary>
        /// Handles already counted.
        ///
        /// A body lies there for a long time and the scan runs every second, so without this
        /// one dead officer would be reported over and over until he streamed out.
        /// </summary>
        private readonly HashSet<int> _counted = new HashSet<int>();

        private int _lastUpdate;

        /// <summary>Set by Main. Null-checked, so the feed is never load-bearing.</summary>
        public SocialFeed Social;

        public void Update()
        {
            var now = Game.GameTime;
            if (now - _lastUpdate < UpdateIntervalMs) return;
            _lastUpdate = now;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            try
            {
                foreach (var ped in World.GetNearbyPeds(player, ScanRange))
                {
                    if (ped == null || !ped.Exists() || ped.IsAlive) continue;
                    if (_counted.Contains(ped.Handle)) continue;

                    var type = Function.Call<int>(Hash.GET_PED_TYPE, ped.Handle);
                    if (Array.IndexOf(CopTypes, type) < 0) continue;

                    if (!Function.Call<bool>(Hash.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY,
                                             ped.Handle, player.Handle, true))
                    {
                        // Somebody else's doing. Still marked, so it is not re-checked forever.
                        _counted.Add(ped.Handle);
                        continue;
                    }

                    _counted.Add(ped.Handle);

                    Log.Info("An officer was killed by the player.");
                    if (Social != null) Social.On(SocialEvent.CopKilled);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Cop kill scan failed: " + ex.Message);
            }

            if (_counted.Count > 400) _counted.Clear();
        }
    }
}
