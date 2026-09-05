using System;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Locations
{
    /// <summary>
    /// The online map, round the block and nowhere else.
    ///
    /// LD Organics is only on the online map: on the story map that corner is a plain garage.
    /// So are the two rooms behind the doors. Holding the whole city on the online map put
    /// LD Organics on the corner and turned every other online building on as well, which was
    /// not wanted; switching only for the doors put it back to a garage the moment you
    /// stepped out.
    ///
    /// This holds the online map while you are on the block -- Lamar's yard, LD Organics,
    /// the grow room door, the pill press door -- and gives the story map back when you
    /// leave it. One circle, with a wider edge for leaving than for arriving, so driving
    /// along its rim does not flick the map on and off.
    ///
    /// A switch reloads the map data round the player, which is a hitch and a second of
    /// missing collision. It happens at the edge of the block, on the road, and nowhere
    /// near anything the mod has put on the ground. See Fixture.Ground for what happens
    /// to furniture placed inside that second.
    /// </summary>
    internal static class HomeMap
    {
        /// <summary>The middle of the block: between LD Organics and the pill press door.</summary>
        private static readonly Vector3 Centre = new Vector3(-150f, -1560f, 30f);

        /// <summary>Arriving, and leaving, as distances from that middle.</summary>
        private const float ArriveRange = 270f;
        private const float LeaveRange = 340f;

        private const int CheckMs = 1500;

        private static bool _on;
        private static int _checkedAt;

        /// <summary>Whether the block is being held on the online map right now.</summary>
        public static bool On => _on;

        /// <summary>Off, and the story map is never touched.</summary>
        public static bool Enabled = true;

        public static void Update()
        {
            if (!Enabled)
            {
                if (_on) Restore();
                return;
            }

            if (Game.GameTime - _checkedAt < CheckMs) return;
            _checkedAt = Game.GameTime;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            var at = player.Position;
            var away = new Vector2(at.X - Centre.X, at.Y - Centre.Y).Length();

            if (!_on && away < ArriveRange) Apply();
            else if (_on && away > LeaveRange) Restore();
        }

        private static void Apply()
        {
            try
            {
                Function.Call(Hash.ON_ENTER_MP);
                _on = true;
                Log.Info("On the block: the online map is on.");
            }
            catch (Exception ex)
            {
                Log.Warn("Could not switch the block to the online map: " + ex.Message);
            }
        }

        public static void Restore()
        {
            if (!_on) return;

            _on = false;

            try
            {
                Function.Call(Hash.ON_ENTER_SP);
                Log.Info("Off the block: the story map is back.");
            }
            catch (Exception ex)
            {
                Log.Warn("Could not switch the block back to the story map: " + ex.Message);
            }
        }
    }
}
