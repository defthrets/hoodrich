using System;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Locations
{
    /// <summary>
    /// Chop, in the yard at Aunt Denise's.
    ///
    /// He is not a mechanic, he is furniture with a heartbeat: something alive at the house so
    /// the place reads as somewhere you live rather than a container you visit. The game's own
    /// dog behaviour does the rest -- he wanders, he follows, and if you take him out with you
    /// that is between you and him.
    /// </summary>
    internal sealed class Dog
    {
        /// <summary>The yard, off the back of the house.</summary>
        private static readonly Vector3 Yard = new Vector3(-9.9f, -1435.2f, 31.1f);

        private const float SpawnRange = 90f;
        private const float DespawnRange = 160f;
        private const float WanderRadius = 12f;
        private const int UpdateIntervalMs = 900;

        private Ped _chop;
        private int _lastUpdate;

        public Ped Ped => _chop != null && _chop.Exists() ? _chop : null;

        public void Update()
        {
            var now = Game.GameTime;
            if (now - _lastUpdate < UpdateIntervalMs) return;
            _lastUpdate = now;

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return;

            var distance = player.Position.DistanceTo(Yard);

            // Only despawn on distance from the HOUSE, not from him -- taking him for a walk is
            // the entire point, and a dog that vanishes two streets away is not a dog.
            if (_chop != null && _chop.Exists())
            {
                if (player.Position.DistanceTo(_chop.Position) > DespawnRange) Despawn();
                return;
            }

            if (distance <= SpawnRange) Spawn();
        }

        private void Spawn()
        {
            try
            {
                var model = new Model("a_c_chop");
                if (!model.IsValid || !model.IsInCdImage || !model.Request(2000)) return;

                var spot = Yard;

                try
                {
                    if (World.GetGroundHeight(new Vector3(spot.X, spot.Y, spot.Z + 15f),
                                              out var groundZ, GetGroundHeightMode.Normal) && groundZ > 0f)
                    {
                        spot.Z = groundZ;
                    }
                }
                catch
                {
                    // Use the authored height.
                }

                _chop = World.CreatePed(model, spot);
                model.MarkAsNoLongerNeeded();

                if (_chop == null || !_chop.Exists()) return;

                _chop.IsPersistent = true;
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, _chop.Handle, true, true);
                Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, _chop.Handle, false);

                // Friendly to the player, so he is a pet rather than an animal that bites.
                Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, _chop.Handle,
                              Function.Call<int>(Hash.GET_HASH_KEY, "PLAYER"));

                Function.Call(Hash.TASK_WANDER_IN_AREA, _chop.Handle,
                              spot.X, spot.Y, spot.Z, WanderRadius, 2f, 6f);

                Log.Info("Chop is in the yard.");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not put Chop in the yard: " + ex.Message);
            }
        }

        private void Despawn()
        {
            try
            {
                if (_chop != null && _chop.Exists())
                {
                    _chop.MarkAsNoLongerNeeded();
                    _chop.Delete();
                }
            }
            catch { /* teardown */ }

            _chop = null;
        }

        public void RestoreWorld() => Despawn();
    }
}
