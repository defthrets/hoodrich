using System;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.State;
using Hoodrich.UI;

namespace Hoodrich.Locations
{
    /// <summary>
    /// Trigger.
    ///
    /// He started as scenery -- somebody's dog, off the lead, wandering the yard, doing nothing
    /// for the player and deliberately not interactive, on the reasoning that a dog which
    /// turned out to be a mission would ruin the party he was standing in.
    ///
    /// That reasoning was right about missions and wrong about dogs. He is not a mission and
    /// there is nothing to complete: you pet him, and after that he comes with you. Which is
    /// the difference between a party with a dog in it and a dog.
    ///
    /// THE GROUP DOES THE WORK. Putting him in the player's ped group is what makes him follow,
    /// keep up, get left behind and catch up, and turn on whoever turns on you -- all of it,
    /// for two native calls. Writing a follow behaviour by hand would be several hundred lines
    /// that behave worse, and the homies already ride on the same mechanism.
    ///
    /// HE IS NOT IMMORTAL AND THAT IS ON PURPOSE. He can be shot, and if he is he is gone --
    /// there is no respawn and the save remembers. A bodyguard you cannot lose is a turret.
    /// </summary>
    internal sealed class Trigger
    {
        public const string Name = "Trigger";

        private const float SpawnRange = 80f;
        private const float DespawnRange = 150f;
        private const int UpdateIntervalMs = 900;

        /// <summary>Close enough to put a hand on him.</summary>
        private const float PetRange = 2.6f;

        /// <summary>How long the pet lasts, and how long before he can be petted again.</summary>
        private const int PetMs = 3400;
        private const int PetAgainMs = 6000;

        /// <summary>How long he settles before moving again, and the shortest walk he takes.</summary>
        private const float ShortestWalk = 1.5f;
        private const float PauseBetween = 4f;

        /// <summary>
        /// A Rottweiler, because that is what was asked for.
        ///
        /// Chop is the fallback and not the first choice. He is the same breed and the right
        /// shape, but he is Franklin's dog and a character in the story -- so he is what
        /// Trigger looks like on a build that has not got the plain one, rather than what
        /// Trigger IS.
        /// </summary>
        private static readonly string[] Dogs = { "a_c_rottweiler", "a_c_chop", "a_c_shepherd" };

        /// <summary>
        /// The synchronised pair the story uses for petting a Rottweiler.
        ///
        /// Two clips out of one dictionary, authored to line up -- the man's half and the dog's
        /// half. Playing either on its own is somebody miming, so both go or neither does.
        /// </summary>
        private const string PetDict = "creatures@rottweiler@tricks@";
        private const string PetMan = "petting_franklin";
        private const string PetDog = "petting_chop";

        private readonly Vector3 _where;
        private readonly float _radius;
        private readonly Random _rng = new Random();
        private readonly PlayerState _state;

        private Ped _dog;
        private int _lastUpdate;

        private int _petUntil;
        private int _petAgainAt;
        private bool _down;

        private bool _inGroup;

        public Trigger(Vector3 where, float radius, PlayerState state)
        {
            _where = where;
            _radius = radius;
            _state = state;
        }

        /// <summary>Set by Main: the yard only exists once the block is yours.</summary>
        public Func<bool> Known;

        /// <summary>Whether he has been petted and is coming with you.</summary>
        private bool Yours
        {
            get { return _state != null && _state.TriggerIsYours; }
        }

        // ---- per-tick -----------------------------------------------------------

        public void Update()
        {
            var now = Game.GameTime;
            if (now - _lastUpdate < UpdateIntervalMs) return;
            _lastUpdate = now;

            try
            {
                var player = Game.Player.Character;
                if (player == null || !player.Exists() || !player.IsAlive) return;

                // GONE IS GONE. He is not invincible and there is no second one -- a dog that
                // reappears after being shot is a prop, and the save remembers so it is not
                // undone by walking away and coming back.
                if (_dog != null && (!_dog.Exists() || !_dog.IsAlive))
                {
                    if (Yours && _dog != null && _dog.Exists() && !_dog.IsAlive)
                    {
                        _state.TriggerIsYours = false;
                        _state.Touch();

                        Notify.Failure(Name + " is gone.");
                        Log.Info("Trigger is dead.");
                    }

                    Release();
                    return;
                }

                if (Yours) { Mine(player, now); return; }

                if (Known != null && !Known())
                {
                    Release();
                    return;
                }

                Yard(player);
            }
            catch (Exception ex)
            {
                Log.Debug("Trigger tripped: " + ex.Message);
            }
        }

        /// <summary>Before he is yours: he is in the yard, and you can put a hand on him.</summary>
        private void Yard(Ped player)
        {
            var away = player.Position.DistanceTo(_where);

            if (_dog == null)
            {
                if (away <= SpawnRange) Make(_where);
                return;
            }

            if (away > DespawnRange)
            {
                Release();
                return;
            }

            Offer(player);
        }

        /// <summary>After: he is wherever you are.</summary>
        private void Mine(Ped player, int now)
        {
            if (_dog == null || !_dog.Exists())
            {
                // Behind you rather than in front, so he arrives at your shoulder instead of
                // appearing in the shot you are looking at.
                Make(player.Position - player.ForwardVector * 3f);

                if (_dog == null) return;
            }

            Join();
            Offer(player);
            Ride(player);
        }

        /// <summary>
        /// In the player's group, which is the whole of following and defending.
        ///
        /// Re-asserted rather than set once. The group membership is dropped by a good number
        /// of things -- a cutscene, a mission, dying -- and a dog that has quietly left the
        /// group stands in the road looking at you.
        /// </summary>
        private void Join()
        {
            try
            {
                var group = Function.Call<int>(Hash.GET_PLAYER_GROUP, Game.Player.Handle);

                if (!Function.Call<bool>(Hash.IS_PED_GROUP_MEMBER, _dog.Handle, group))
                {
                    Function.Call(Hash.SET_PED_AS_GROUP_MEMBER, _dog.Handle, group);
                    Function.Call(Hash.SET_PED_NEVER_LEAVES_GROUP, _dog.Handle, true);

                    _inGroup = true;
                }
            }
            catch
            {
                // He catches up on the next tick.
            }
        }

        // ---- petting ------------------------------------------------------------

        private void Offer(Ped player)
        {
            if (_dog == null || !_dog.Exists()) return;
            if (player.IsInVehicle()) return;

            var now = Game.GameTime;

            if (now < _petUntil) return;
            if (_dog.Position.DistanceTo(player.Position) > PetRange) return;

            Help.ShowThisFrame(Yours
                ? "Press ~INPUT_CELLPHONE_RIGHT~ to pet " + Name + "."
                : "Press ~INPUT_CELLPHONE_RIGHT~ to pet him.");

            if (!Tapped()) return;
            if (now < _petAgainAt) return;

            Pet(player, now);
        }

        private void Pet(Ped player, int now)
        {
            _petUntil = now + PetMs;
            _petAgainAt = now + PetMs + PetAgainMs;

            try
            {
                // Facing each other first. The two clips are authored as a pair and line up
                // only if the pair does; a dog petted over its own shoulder is worse than a
                // dog not petted at all.
                var toDog = _dog.Position - player.Position;

                player.Heading = (float)((Math.Atan2(toDog.X, toDog.Y) * 180.0 / Math.PI + 360.0) % 360.0);
                _dog.Heading = (player.Heading + 180f) % 360f;

                Function.Call(Hash.REQUEST_ANIM_DICT, PetDict);

                if (Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, PetDict))
                {
                    Function.Call(Hash.TASK_PLAY_ANIM, player.Handle, PetDict, PetMan,
                                  4f, -4f, PetMs, 0, 0f, false, false, false);

                    Function.Call(Hash.TASK_PLAY_ANIM, _dog.Handle, PetDict, PetDog,
                                  4f, -4f, PetMs, 0, 0f, false, false, false);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not play the petting: " + ex.Message);
            }

            if (Yours) return;

            // THE FIRST ONE IS THE WHOLE TRANSACTION. Nothing is accepted and nothing is
            // confirmed -- you put a hand on a dog and after that the dog comes with you,
            // which is how it works with dogs.
            _state.TriggerIsYours = true;
            _state.Touch();

            Notify.Important("~g~" + Name + ".~s~ He's coming with you.");
            Log.Info("Trigger has been adopted.");
        }

        // ---- the car ------------------------------------------------------------

        /// <summary>
        /// Shotgun, like Chop.
        ///
        /// The group will not put him in a car on its own -- it follows on foot and stands
        /// there while you drive off. So when you are in something and he is not, he is told
        /// to get in, and the seat is chosen rather than left to the game: front passenger if
        /// it is free, the back if it is not, and nothing at all if the car is full.
        /// </summary>
        private void Ride(Ped player)
        {
            try
            {
                var car = player.CurrentVehicle;

                if (car == null || !car.Exists())
                {
                    return;
                }

                if (_dog.IsInVehicle(car)) return;

                // Already on his way. Asking again every tick restarts the approach and he
                // never reaches the door.
                if (Function.Call<bool>(Hash.GET_IS_TASK_ACTIVE, _dog.Handle, 160)) return;

                var seat = Free(car);
                if (seat == int.MinValue) return;

                Function.Call(Hash.TASK_ENTER_VEHICLE, _dog.Handle, car.Handle,
                              -1, seat, 2f, 1, 0);
            }
            catch
            {
                // He runs alongside, which is not the worst outcome.
            }
        }

        /// <summary>The best empty seat, or MinValue when there is not one.</summary>
        private static int Free(Vehicle car)
        {
            try
            {
                var seats = Function.Call<int>(Hash.GET_VEHICLE_MAX_NUMBER_OF_PASSENGERS, car.Handle);

                for (var seat = 0; seat < seats; seat++)
                {
                    if (Function.Call<bool>(Hash.IS_VEHICLE_SEAT_FREE, car.Handle, seat)) return seat;
                }
            }
            catch
            {
                // Fall through.
            }

            return int.MinValue;
        }

        // ---- making him ---------------------------------------------------------

        private void Make(Vector3 at)
        {
            foreach (var name in Dogs)
            {
                try
                {
                    var model = new Model(name);

                    if (!model.IsValid || !model.IsInCdImage) continue;

                    // Not loaded yet is a reason to wait, not a reason to bring a different
                    // dog. Update comes back in under a second.
                    if (!model.Request(1000)) return;

                    var spot = at;

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

                    // The dark coat. Rottweilers only have a couple of variations and which
                    // index is which is not written down anywhere, so this asks for the first
                    // and takes what it gets -- it is the breed that does the work here.
                    Function.Call(Hash.SET_PED_COMPONENT_VARIATION, h, 0, 0, 0, 0);

                    Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, h, false);
                    Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, h, 0, false);

                    if (Yours)
                    {
                        // HE FIGHTS NOW. The yard dog was told to ignore everything, which is
                        // right for scenery and useless in a bodyguard -- so a dog that is
                        // yours gets the opposite treatment: he reacts, he does not run, and
                        // he does not need to be told who to bite because the group tells him.
                        Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, h, false);
                        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, h, 5, true);
                        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, h, 46, true);
                        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, h, 17, false);

                        Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, h,
                                      Game.GenerateHash("PLAYER"));
                    }
                    else
                    {
                        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, h, 5, false);
                        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, h, 46, false);
                        Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, h, true);

                        Function.Call(Hash.TASK_WANDER_IN_AREA, h,
                                      spot.X, spot.Y, spot.Z, _radius, ShortestWalk, PauseBetween);

                        Function.Call(Hash.SET_PED_KEEP_TASK, h, true);
                    }

                    Log.Info((Yours ? "Trigger is with you: " : "A dog is out in the yard: ") + name + ".");
                    return;
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not put a dog out: " + ex.Message);
                    return;
                }
            }
        }

        private bool Tapped()
        {
            var down = false;

            try
            {
                down = Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)Control.PhoneRight)
                    || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)Control.PhoneRight)
                    || Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)Control.Context);
            }
            catch
            {
            }

            var hit = down && !_down;
            _down = down;

            return hit;
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
            _inGroup = false;
        }

        public void RestoreWorld()
        {
            Release();
        }
    }
}
