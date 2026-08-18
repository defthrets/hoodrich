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

        /// <summary>
        /// Model candidates. Enhanced ships a second Chop, and an install that has only one of
        /// them should still get a dog rather than an empty yard.
        /// </summary>
        private static readonly string[] Models = { "a_c_chop", "a_c_chop_02" };

        /// <summary>CREATE_PED ped type. 28 is PED_TYPE_ANIMAL; a dog made as a civilian is not one.</summary>
        private const int PedTypeAnimal = 28;

        private Ped _chop;
        private int _lastUpdate;
        private bool _gaveUp;

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

            if (distance <= SpawnRange && !_gaveUp) Spawn();
        }

        private void Spawn()
        {
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

            foreach (var name in Models)
            {
                if (TrySpawn(name, spot)) return;
            }

            // Said once, not every second. A yard with no dog in it is worth one line in the log;
            // one that says so every tick for the rest of the session is not.
            _gaveUp = true;
            Log.Warn("No Chop model would load, so the yard stays empty.");
        }

        private bool TrySpawn(string name, Vector3 spot)
        {
            try
            {
                var model = new Model(name);
                if (!model.IsValid || !model.IsInCdImage || !model.Request(2000))
                {
                    Log.Debug("Chop model " + name + " is not in this install.");
                    return false;
                }

                // Made as an ANIMAL rather than through the generic ped helper, which asks for a
                // civilian. A dog created as the wrong ped type for its model is how the yard
                // ended up empty with nothing in the log to say why.
                var handle = Function.Call<int>(Hash.CREATE_PED, PedTypeAnimal, model.Hash,
                                                spot.X, spot.Y, spot.Z, 0f, false, false);

                model.MarkAsNoLongerNeeded();

                if (handle == 0) return false;

                _chop = Entity.FromHandle(handle) as Ped;

                if (_chop == null || !_chop.Exists())
                {
                    _chop = null;
                    return false;
                }

                _chop.IsPersistent = true;
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, _chop.Handle, true, true);
                Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, _chop.Handle, false);

                // Friendly to the player, so he is a pet rather than an animal that bites.
                Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, _chop.Handle,
                              Function.Call<int>(Hash.GET_HASH_KEY, "PLAYER"));

                Function.Call(Hash.TASK_WANDER_IN_AREA, _chop.Handle,
                              spot.X, spot.Y, spot.Z, WanderRadius, 2f, 6f);

                Log.Info("Chop is in the yard at " + spot + " as " + name + ".");
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not put Chop in the yard as " + name + ": " + ex.Message);
                return false;
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
