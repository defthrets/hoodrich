using System;
using Control = GTA.Control;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.UI;

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

        /// <summary>Close enough to put a hand on him.</summary>
        private const float ReachRange = 2.6f;

        /// <summary>How long the fuss lasts before he goes back to what he was doing.</summary>
        private const int PetMs = 4200;

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

        private bool _following;
        private bool _petting;
        private int _pettingUntil;
        private bool _held;

        public Ped Ped => _chop != null && _chop.Exists() ? _chop : null;

        public void Update()
        {
            var now = Game.GameTime;
            if (now - _lastUpdate < UpdateIntervalMs) return;
            _lastUpdate = now;

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return;

            if (_petting && now >= _pettingUntil) EndPet();

            var distance = player.Position.DistanceTo(Yard);

            // Only despawn on distance from the HOUSE, not from him -- taking him for a walk is
            // the entire point, and a dog that vanishes two streets away is not a dog.
            if (_chop != null && _chop.Exists())
            {
                // Despawn on distance from HIM, not from the house -- taking him out with you is
                // the whole point of a dog you can call.
                if (player.Position.DistanceTo(_chop.Position) > DespawnRange) Despawn();
                return;
            }

            if (distance <= SpawnRange && !_gaveUp) Spawn();
        }

        private void Spawn()
        {
            // Probed from just above the authored height, and only believed if it agrees.
            // Firing a probe down from fifteen metres up in a yard hemmed in by two-storey
            // flats finds the first thing it hits, which is a balcony -- and that is how a dog
            // ended up on a roof.
            var spot = Yard;

            try
            {
                if (World.GetGroundHeight(new Vector3(spot.X, spot.Y, spot.Z + 1.5f),
                                          out var groundZ, GetGroundHeightMode.Normal) &&
                    groundZ > 0f && Math.Abs(groundZ - spot.Z) <= 3f)
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
            _following = false;
            _petting = false;
        }

        // ---- being a dog -------------------------------------------------------

        private bool InReach
        {
            get
            {
                var player = Game.Player.Character;
                if (player == null || !player.Exists() || _chop == null || !_chop.Exists()) return false;

                return player.Position.DistanceTo(_chop.Position) <= ReachRange;
            }
        }

        /// <summary>
        /// The prompt, and what the two buttons do.
        ///
        /// Two things only, because a dog is not a menu: make a fuss of him, and tell him
        /// whether he is coming. Everything else is the game own dog behaviour, which is
        /// already better than anything a script would put on top of it.
        /// </summary>
        public void UpdatePrompt()
        {
            if (!InReach || _petting) return;

            Help.ShowThisFrame(_following
                ? "~INPUT_CONTEXT~ pet Chop     ~INPUT_CELLPHONE_RIGHT~ tell him to stay"
                : "~INPUT_CONTEXT~ pet Chop     ~INPUT_CELLPHONE_RIGHT~ bring him with you");

            var pet = Tapped(Control.Context, System.Windows.Forms.Keys.E, ref _held);
            if (pet) { Pet(); return; }

            var call = Tapped(Control.PhoneRight, System.Windows.Forms.Keys.Right, ref _heldCall);
            if (call) Follow(!_following);
        }

        private bool _heldCall;

        private static bool Tapped(Control control, System.Windows.Forms.Keys key, ref bool held)
        {
            var down = false;

            try
            {
                down = Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)control)
                    || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)control)
                    || Game.IsKeyPressed(key);
            }
            catch
            {
                // Unreadable control is simply not pressed.
            }

            var pressed = down && !held;
            held = down;
            return pressed;
        }

        private void Pet()
        {
            if (_chop == null || !_chop.Exists()) return;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            _petting = true;
            _pettingUntil = Game.GameTime + PetMs;

            try
            {
                _chop.Task.ClearAll();

                Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, _chop.Handle, player.Handle, PetMs);
                Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, player.Handle, _chop.Handle, PetMs);

                // The game own dog-sitting idle. Nothing else in the animal set reads as a dog
                // enjoying itself, and a made-up pose on a quadruped looks broken rather than
                // affectionate.
                Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, _chop.Handle,
                              "WORLD_DOG_SITTING_GENERIC", 0, true);

                Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, _chop.Handle,
                              "GENERIC_HOWS_IT_GOING", "SPEECH_PARAMS_FORCE");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not make a fuss of Chop: " + ex.Message);
            }

            Notify.Ticker("~g~Chop is pleased to see you.~s~");
        }

        private void EndPet()
        {
            _petting = false;

            if (_chop == null || !_chop.Exists()) return;

            try
            {
                _chop.Task.ClearAll();

                if (_following) Heel();
                else Wander();
            }
            catch
            {
                // He will settle.
            }
        }

        /// <summary>Whether he is coming with you or staying in the yard.</summary>
        public void Follow(bool on)
        {
            if (_chop == null || !_chop.Exists()) return;

            _following = on;

            try
            {
                _chop.Task.ClearAll();

                if (on) Heel();
                else Wander();
            }
            catch (Exception ex)
            {
                Log.Debug("Chop did not hear that: " + ex.Message);
            }

            Notify.Ticker(on ? "~g~Chop is coming with you.~s~" : "~o~Chop stays.~s~");
        }

        private void Heel()
        {
            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            // Just behind and to one side, which is where a dog walks.
            Function.Call(Hash.TASK_FOLLOW_TO_OFFSET_OF_ENTITY, _chop.Handle, player.Handle,
                          -0.6f, -1.4f, 0f, 2.2f, -1, 2.5f, true);
        }

        private void Wander()
        {
            var spot = _chop.Position;

            Function.Call(Hash.TASK_WANDER_IN_AREA, _chop.Handle,
                          spot.X, spot.Y, spot.Z, WanderRadius, 2f, 6f);
        }

        public void RestoreWorld() => Despawn();
    }
}
