using System;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Locations
{
    /// <summary>
    /// Somebody's dog, off the lead, in the yard.
    ///
    /// The same idea as <see cref="Fixture"/> -- a thing that belongs on a block and streams
    /// with it -- except this one moves. It does nothing for the player and cannot be
    /// interacted with, and that is deliberate: a party with a dog wandering through it is a
    /// party somebody lives at, and a dog that turned out to be a mission would ruin that.
    ///
    /// NOT an Entourage station. Entourage describes a man standing somewhere doing an
    /// animation, across nine parallel lists that all have to agree by index -- adding a tenth
    /// for the one thing in the yard that does not stand still is how that class ends up with
    /// the bug this codebase has already been bitten by twice. A dog is its own small thing,
    /// so it is its own small class.
    ///
    /// He wanders rather than patrols. TASK_WANDER_IN_AREA is the game's own "mill about near
    /// here" and it keeps him inside a radius without a route, which is what a dog in a yard
    /// actually does -- a scripted circuit would read as a guard dog on a beat.
    /// </summary>
    internal sealed class YardDog
    {
        private const float SpawnRange = 80f;
        private const float DespawnRange = 150f;
        private const int UpdateIntervalMs = 2200;

        /// <summary>
        /// How long he settles before moving again, and the shortest walk he will take.
        ///
        /// Both small. A dog that crosses the yard in one go and then stands still for a minute
        /// is a dog on a schedule; short hops with short pauses is a dog.
        /// </summary>
        private const float ShortestWalk = 1.5f;
        private const float PauseBetween = 4f;

        /// <summary>
        /// What turns up. Street dogs first, because this is a yard in Chamberlain.
        ///
        /// Chop is deliberately absent. He is Franklin's dog and a character in the story, and
        /// finding a second one wandering somebody else's party is worse than having no dog.
        /// </summary>
        private static readonly string[] Dogs =
        {
            "a_c_shepherd", "a_c_rottweiler", "a_c_retriever", "a_c_husky", "a_c_westy"
        };

        private readonly Vector3 _where;
        private readonly float _radius;
        private readonly Random _rng = new Random();

        private Ped _dog;
        private int _lastUpdate;

        public YardDog(Vector3 where, float radius)
        {
            _where = where;
            _radius = radius;
        }

        /// <summary>Set by Main: only there once the block is yours, like the rest of the yard.</summary>
        public Func<bool> Known;

        public void Update()
        {
            var now = Game.GameTime;
            if (now - _lastUpdate < UpdateIntervalMs) return;
            _lastUpdate = now;

            if (Known != null && !Known())
            {
                Clear();
                return;
            }

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            if (_dog != null && (!_dog.Exists() || !_dog.IsAlive))
            {
                // Dead or gone. Let go of him rather than standing a fresh one up on the spot,
                // which would read as the same dog respawning in front of you.
                Release();
                return;
            }

            var away = player.Position.DistanceTo(_where);

            if (_dog == null)
            {
                if (away <= SpawnRange) Make();
                return;
            }

            if (away > DespawnRange) Release();
        }

        private void Make()
        {
            foreach (var name in Dogs)
            {
                try
                {
                    var model = new Model(name);

                    if (!model.IsValid || !model.IsInCdImage) continue;

                    // Not loaded yet is a reason to wait, not a reason to bring a different
                    // dog. Update comes back in a couple of seconds.
                    if (!model.Request(1000)) return;

                    var spot = _where;

                    float ground;
                    if (World.GetGroundHeight(new Vector3(spot.X, spot.Y, spot.Z + 2f),
                                              out ground, GetGroundHeightMode.Normal))
                    {
                        spot = new Vector3(spot.X, spot.Y, ground);
                    }

                    _dog = World.CreatePed(model, spot, (float)(_rng.NextDouble() * 360d));
                    model.MarkAsNoLongerNeeded();

                    if (_dog == null || !_dog.Exists()) continue;

                    var h = _dog.Handle;

                    _dog.IsPersistent = true;
                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, h, true, true);

                    // NOBODY'S PROBLEM. He is not shot at, he does not start anything, and he
                    // does not bolt the moment a car backfires -- a dog that sprints out of the
                    // yard the first time somebody revs an engine is a dog you see once.
                    Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, h, false);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, h, 5, false);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, h, 46, false);
                    Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, h, 0, false);
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, h, true);

                    Function.Call(Hash.TASK_WANDER_IN_AREA, h,
                                  spot.X, spot.Y, spot.Z, _radius, ShortestWalk, PauseBetween);

                    Function.Call(Hash.SET_PED_KEEP_TASK, h, true);

                    Log.Info("A dog is out in the yard: " + name + ".");
                    return;
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not put a dog in the yard: " + ex.Message);
                    return;
                }
            }
        }

        /// <summary>Handed back to the game rather than deleted, so he is not removed in view.</summary>
        private void Release()
        {
            try
            {
                if (_dog != null && _dog.Exists())
                {
                    _dog.IsPersistent = false;
                    _dog.MarkAsNoLongerNeeded();
                }
            }
            catch { /* he is the game's now either way */ }

            _dog = null;
        }

        private void Clear()
        {
            Release();
        }

        public void RestoreWorld()
        {
            Release();
        }
    }
}
