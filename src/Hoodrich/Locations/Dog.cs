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

        /// <summary>A game of fetch runs a bit longer than a pat does.</summary>
        private const int PlayMs = 7000;

        /// <summary>
        /// How often we look for a second Chop, and how far out.
        ///
        /// The game keeps its own Chop at whichever house Franklin currently lives in, which
        /// after the story is the place up in the hills. Two of them is worse than either, so
        /// any Chop that is not ours is sent away wherever it turns up.
        /// </summary>
        private const int RivalScanMs = 4000;
        private const float RivalScanRange = 220f;

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

        private int _lastRivalScan;
        private bool _playing;

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

            SendAwayTheOtherOne(player, now);

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
            _playing = false;
        }

        /// <summary>
        /// Makes sure there is exactly one Chop.
        ///
        /// Not a fix for the game so much as an agreement with it: the mod puts a Chop at the
        /// house on Forum Drive because that is where he lived, and the game puts one wherever
        /// Franklin currently lives. Two Chops is nobody's idea of anything, so the one that is
        /// not ours is let go. The game brings its own back when the mod is unloaded.
        /// </summary>
        private void SendAwayTheOtherOne(Ped player, int now)
        {
            if (now - _lastRivalScan < RivalScanMs) return;
            _lastRivalScan = now;

            try
            {
                var ours = _chop != null && _chop.Exists() ? _chop.Handle : 0;

                foreach (var ped in World.GetNearbyPeds(player, RivalScanRange))
                {
                    if (ped == null || !ped.Exists()) continue;
                    if (ours != 0 && ped.Handle == ours) continue;
                    if (!IsChop(ped)) continue;

                    ped.IsPersistent = false;
                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, ped.Handle, false, true);
                    ped.MarkAsNoLongerNeeded();
                    ped.Delete();

                    Log.Info("Sent away a second Chop.");
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not check for a second Chop: " + ex.Message);
            }
        }

        private static bool IsChop(Ped ped)
        {
            try
            {
                var model = (uint)ped.Model.Hash;

                foreach (var name in Models)
                {
                    if (model == (uint)Function.Call<int>(Hash.GET_HASH_KEY, name)) return true;
                }
            }
            catch
            {
                // A ped we cannot identify is somebody else's.
            }

            return false;
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
                ? "~INPUT_CONTEXT~ pet     ~INPUT_CELLPHONE_UP~ play     ~INPUT_CELLPHONE_RIGHT~ tell him to stay"
                : "~INPUT_CONTEXT~ pet     ~INPUT_CELLPHONE_UP~ play     ~INPUT_CELLPHONE_RIGHT~ take him with you");

            if (Tapped(Control.Context, System.Windows.Forms.Keys.E, ref _held)) { Pet(); return; }
            if (Tapped(Control.PhoneUp, System.Windows.Forms.Keys.Up, ref _heldPlay)) { Play(); return; }

            if (Tapped(Control.PhoneRight, System.Windows.Forms.Keys.Right, ref _heldCall)) Follow(!_following);
        }

        private bool _heldPlay;

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

        /// <summary>
        /// A game in the yard.
        ///
        /// He runs off a little way, turns, and comes back at you -- which is as close to fetch
        /// as the game's animal set gets without a ball entity to chase. Longer than a pat and
        /// worth doing because it is the thing you actually did with him.
        /// </summary>
        private void Play()
        {
            if (_chop == null || !_chop.Exists()) return;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            _petting = true;
            _playing = true;
            _pettingUntil = Game.GameTime + PlayMs;

            try
            {
                _chop.Task.ClearAll();

                var away = player.Position.Around(7f);
                away.Z = _chop.Position.Z;

                // Out and back rather than a scenario: a dog that runs somewhere and returns to
                // you reads as playing, and a dog stood still playing an animation does not.
                Function.Call(Hash.TASK_GO_STRAIGHT_TO_COORD, _chop.Handle,
                              away.X, away.Y, away.Z, 2.5f, PlayMs / 2, 0f, 0f);

                Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, _chop.Handle,
                              "GENERIC_WAR_CRY", "SPEECH_PARAMS_FORCE");
            }
            catch (Exception ex)
            {
                Log.Debug("Chop did not fancy a game: " + ex.Message);
            }

            Notify.Ticker("~g~Chop wants to play.~s~");
        }

        private void EndPet()
        {
            if (_playing && _chop != null && _chop.Exists())
            {
                // Second half of the game: he comes back to you.
                _playing = false;
                _pettingUntil = Game.GameTime + PlayMs / 2;

                try
                {
                    var player = Game.Player.Character;

                    if (player != null && player.Exists())
                    {
                        Function.Call(Hash.TASK_GO_TO_ENTITY, _chop.Handle, player.Handle,
                                      PlayMs / 2, 1.5f, 2.5f, 0f, 0);
                        return;
                    }
                }
                catch
                {
                    // Fall through and settle.
                }
            }

            _playing = false;
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
