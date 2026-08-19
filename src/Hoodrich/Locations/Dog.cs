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
    /// The honest limitation: he has to EXIST to be moved, and the game only creates him near
    /// whichever house Franklin currently lives in. So the first move happens when you are up at
    /// the mansion. After that he is held here, and held is the whole job.
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
            if (_chop == null) return;

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

                    // Held, so the game stops streaming him out from under us the moment the
                    // player drives away. Everything else about him stays the game's business.
                    _chop.IsPersistent = true;

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

        private void SendHome()
        {
            if (_chop == null || !_chop.Exists()) return;

            try
            {
                var spot = Ground(Yard);

                _chop.Task.ClearAll();
                _chop.Position = spot;
                _chop.Heading = 200f;

                Function.Call(Hash.TASK_WANDER_IN_AREA, _chop.Handle,
                              spot.X, spot.Y, spot.Z, 6f, 3f, 10f);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not put Chop in the yard: " + ex.Message);
            }
        }

        /// <summary>Walks him back if he has wandered off the yard.</summary>
        private void Leash()
        {
            if (_chop == null || !_chop.Exists() || !_chop.IsAlive) return;
            if (_chop.Position.DistanceTo(Yard) <= LeashRange) return;

            // Far enough that he has plainly been picked up by something else -- the story, a
            // stream-in somewhere across the map -- so put him back rather than walk him.
            if (_chop.Position.DistanceTo(Yard) > 120f)
            {
                SendHome();
                return;
            }

            try
            {
                var spot = Ground(Yard);

                _chop.Task.ClearAll();
                Function.Call(Hash.TASK_GO_STRAIGHT_TO_COORD, _chop.Handle,
                              spot.X, spot.Y, spot.Z, 1.6f, 20000, 0f, 0.5f);
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
                if (_chop != null && _chop.Exists()) _chop.MarkAsNoLongerNeeded();
            }
            catch { /* teardown */ }

            _chop = null;
            _moved = false;
        }
    }
}
