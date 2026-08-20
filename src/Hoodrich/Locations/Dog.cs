using System;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.UI;

namespace Hoodrich.Locations
{
    /// <summary>
    /// Chop, moved home.
    ///
    /// This used to spawn a Chop of its own and give him a menu. That was the wrong idea twice
    /// over: the interactions never worked, and even when they did it was a copy of the dog
    /// rather than the dog. The game already has a Chop with his own petting, his own tricks
    /// and his own app -- all of it better than anything a script bolts on.
    ///
    /// So this spawns nothing and adds nothing. It finds the game's own Chop, wherever the story
    /// has put him, and puts him back in the yard on Forum Drive -- then leaves him alone to be
    /// the game's dog, at the house he used to live at.
    ///
    /// The limitation that used to come with that: he has to EXIST to be moved, and the game
    /// only creates him near whichever house Franklin currently lives in -- post-story, the
    /// mansion. So getting him home began with a drive up the hill to fetch him.
    ///
    /// Now there is a fallback. If there is no Chop anywhere near and you are at the house, one
    /// is put in the yard. The real one is still always preferred and always wins: if the
    /// game's own dog turns up later, ours is removed on sight and his is kept. The fallback is
    /// there to fill a gap, not to compete for the job.
    /// </summary>
    internal sealed class Dog
    {
        /// <summary>The kennel in the yard, read off the HUD standing on it.</summary>
        private static readonly Vector3 Yard = new Vector3(-11.095f, -1423.223f, 30.678f);

        /// <summary>How far he may drift from the kennel before he is walked back.</summary>
        private const float LeashRange = 14f;

        /// <summary>How far out we look for him. Wide, because he is usually somewhere else.</summary>
        private const float FindRange = 400f;

        private const int UpdateIntervalMs = 1500;

        /// <summary>Both Chop models. Enhanced ships a second one.</summary>
        private static readonly string[] Models = { "a_c_chop", "a_c_chop_02" };

        private Ped _chop;
        private int _lastUpdate;
        private bool _moved;

        public Ped Ped => _chop != null && _chop.Exists() ? _chop : null;

        public void Update()
        {
            var now = Game.GameTime;
            if (now - _lastUpdate < UpdateIntervalMs) return;
            _lastUpdate = now;

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return;

            if (_chop != null && !_chop.Exists()) _chop = null;

            if (_chop == null) Find(player);

            // Ours was standing in for a dog that has since arrived. His is the one with the
            // interactions, so ours goes without ceremony.
            if (_chop != null && _ours && RealOneNearby(player))
            {
                Remove();
                Find(player);
            }

            if (_chop == null)
            {
                MakeOne(player);
                return;
            }

            Leash();
        }

        /// <summary>
        /// Looks for the game's own Chop and, having found him, moves him home once.
        ///
        /// Nothing is created here. If the story has him in the hills then that is where he is
        /// found, and the drive up there is the price of fetching your dog.
        /// </summary>
        private void Find(Ped player)
        {
            try
            {
                foreach (var ped in World.GetNearbyPeds(player, FindRange))
                {
                    if (ped == null || !ped.Exists() || !ped.IsAlive) continue;
                    if (!IsChop(ped)) continue;

                    _chop = ped;
                    _ours = false;

                    // Deliberately NOT held persistent, and deliberately not tasked.
                    //
                    // Petting him, playing with him, taking him for a walk -- all of that is the
                    // game's own script, and it only runs on a dog the game still owns. Marking
                    // him as ours takes him off it, which is why he was recognisably Chop and
                    // had none of Chop's prompts. If he streams out we simply find him again.

                    if (!_moved)
                    {
                        _moved = true;
                        SendHome();

                        Notify.Ticker("~g~Chop's back at the house.~s~");
                        Log.Info("Found the game's Chop and moved him to the yard.");
                    }

                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not look for Chop: " + ex.Message);
            }
        }

        /// <summary>
        /// Whether the game's own Chop is about, ignoring the one we made.
        /// </summary>
        private bool RealOneNearby(Ped player)
        {
            try
            {
                foreach (var ped in World.GetNearbyPeds(player, FindRange))
                {
                    if (ped == null || !ped.Exists() || !ped.IsAlive) continue;
                    if (_chop != null && ped.Handle == _chop.Handle) continue;
                    if (!IsChop(ped)) continue;

                    return true;
                }
            }
            catch
            {
                // If we cannot tell, assume not, and keep the one we have.
            }

            return false;
        }

        /// <summary>
        /// Puts a dog in the yard when the game has not got round to it.
        ///
        /// Only at the house, and only when there is genuinely no Chop anywhere -- spawning one
        /// while the real one is two streets away would give you two, and one of them would be
        /// the wrong one.
        ///
        /// Created and then left completely alone, for the same reason the real one is: whatever
        /// the game wants to do with a dog standing in a yard is better than anything scripted
        /// on top of it.
        /// </summary>
        private void MakeOne(Ped player)
        {
            if (_madeOne) return;
            if (player.Position.DistanceTo(Yard) > MakeRange) return;

            foreach (var name in Models)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) continue;

                    var spot = Ground(Yard);

                    var handle = Function.Call<int>(Hash.CREATE_PED, PedTypeAnimal, model.Hash,
                                                    spot.X, spot.Y, spot.Z, 200f, false, false);

                    model.MarkAsNoLongerNeeded();
                    if (handle == 0) continue;

                    var dog = Entity.FromHandle(handle) as Ped;
                    if (dog == null || !dog.Exists()) continue;

                    _chop = dog;
                    _ours = true;
                    _madeOne = true;
                    _moved = true;

                    Notify.Ticker("~g~Chop's out in the yard.~s~");
                    Log.Info("No Chop anywhere; put one in the yard at " + spot + ".");
                    return;
                }
                catch
                {
                    // Try the other model.
                }
            }
        }

        /// <summary>Takes ours away again. Only ever called on one we made.</summary>
        private void Remove()
        {
            try
            {
                if (_chop != null && _chop.Exists())
                {
                    _chop.MarkAsNoLongerNeeded();
                    _chop.Delete();
                }

                Log.Info("The game's own Chop turned up; ours has been removed.");
            }
            catch { /* it will stream out */ }

            _chop = null;
            _ours = false;
            _madeOne = false;
        }

        /// <summary>PED_TYPE_ANIMAL, so the game treats him as the animal he is.</summary>
        private const int PedTypeAnimal = 28;

        /// <summary>Close enough to the yard to be the one putting a dog in it.</summary>
        private const float MakeRange = 60f;

        /// <summary>True when the dog we are holding is one we made rather than the game's.</summary>
        private bool _ours;

        /// <summary>So a dog that gets shot is not immediately replaced by another one.</summary>
        private bool _madeOne;

        private void SendHome()
        {
            if (_chop == null || !_chop.Exists()) return;

            try
            {
                var spot = Ground(Yard);

                // Moved, and then left alone. No task of ours: whatever he was doing he goes
                // back to doing, in the yard instead of the hills.
                _chop.Position = spot;
                _chop.Heading = 200f;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not put Chop in the yard: " + ex.Message);
            }
        }

        /// <summary>How long he is left to walk back before he is told again.</summary>
        private const int LeashRetaskMs = 9000;

        /// <summary>Close enough that you are plainly with him, and he is yours to walk.</summary>
        private const float WithYouRange = 14f;

        private int _lastLeash;

        /// <summary>
        /// Walks him back if he has wandered off the yard, and otherwise leaves him alone.
        ///
        /// Two things were wrong with doing this every tick. Clearing his tasks is exactly what
        /// stops the game's own Chop from being the game's own Chop -- petting him, playing with
        /// him, walking him anywhere at all -- and this fired every second and a half the moment
        /// he was fourteen metres from a kennel. And re-issuing the walk on every pass restarts
        /// the path, so a dog told to come home four times a minute never gets there.
        ///
        /// So: if you are anywhere near him he is yours and nothing here touches him, and if he
        /// is genuinely off on his own he is told once and given time to arrive.
        /// </summary>
        private void Leash()
        {
            if (_chop == null || !_chop.Exists() || !_chop.IsAlive) return;
            if (_chop.Position.DistanceTo(Yard) <= LeashRange) return;

            var player = Game.Player.Character;

            if (player != null && player.Exists() &&
                player.Position.DistanceTo(_chop.Position) <= WithYouRange)
            {
                return;
            }

            // Far enough that he has plainly been picked up by something else -- the story, a
            // stream-in somewhere across the map -- so put him back rather than walk him.
            if (_chop.Position.DistanceTo(Yard) > 120f)
            {
                SendHome();
                return;
            }

            if (Game.GameTime - _lastLeash < LeashRetaskMs) return;
            _lastLeash = Game.GameTime;

            // Only when nobody is looking, and only by moving him -- not by tasking him. A task
            // of ours is a task instead of his, and his are the ones worth having.
            if (_chop.IsOnScreen) return;

            try
            {
                _chop.Position = Ground(Yard);
            }
            catch
            {
                // He will find his own way.
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

        private static Vector3 Ground(Vector3 where)
        {
            try
            {
                if (World.GetGroundHeight(new Vector3(where.X, where.Y, where.Z + 1.5f),
                                          out var groundZ, GetGroundHeightMode.Normal) &&
                    groundZ > 0f && Math.Abs(groundZ - where.Z) <= 3f)
                {
                    where.Z = groundZ;
                }
            }
            catch
            {
                // Keep the authored height.
            }

            return where;
        }

        /// <summary>
        /// Hands him back.
        ///
        /// He is not ours to delete -- he is the game's dog and we only ever borrowed a handle,
        /// so unloading releases him exactly where he stands rather than removing him.
        /// </summary>
        public void RestoreWorld()
        {
            try
            {
                if (_chop != null && _chop.Exists())
                {
                    // One we made is ours to take away. The game's dog is only ever borrowed,
                    // so he is let go exactly where he stands.
                    if (_ours) _chop.Delete();
                    else _chop.MarkAsNoLongerNeeded();
                }
            }
            catch { /* teardown */ }

            _chop = null;
            _moved = false;
            _ours = false;
            _madeOne = false;
        }
    }
}
