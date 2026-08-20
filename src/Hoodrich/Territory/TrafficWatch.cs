using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Territory
{
    /// <summary>
    /// Cars that stop in the road and never move again.
    ///
    /// This was written for one block, because outside the house is where it was first seen,
    /// and the assumption underneath it -- that the cause is something the mod does near the
    /// house -- has not held up. It happens all over the map. So rather than keep hunting a
    /// single cause that may not be a single cause, the watch travels with the player: same
    /// rule, same exclusions, wherever he is standing.
    ///
    /// It could be a delivery car sat on its mark with a lane and a half left, it could be
    /// somebody's bumper on a kerb, it could be one driver waiting on another driver who is
    /// waiting on him -- Los Santos manages that on its own without any help from us. It does
    /// not much matter which. From the pavement it is a lorry parked across the road with its
    /// engine running, and it stays that way.
    ///
    /// Two shapes of it, and they need different answers:
    ///
    ///   With a driver -- told to go about his business. If he was jammed he leaves. A parked
    ///   car is not stopped WITH A DRIVER IN IT, so parked cars are never touched.
    ///
    ///   With nobody in it -- cannot be told anything. An abandoned car in a live lane with a
    ///   queue behind it going nowhere is exactly the jam in the screenshots, and the only
    ///   thing to do with it is give it back to the game's population control, then take it
    ///   away if the game still has not and nobody is looking.
    ///
    /// Nothing the mod owns is ever nudged -- those have their own jobs and their own watchdogs.
    /// </summary>
    internal sealed class TrafficWatch
    {
        /// <summary>
        /// How far around the player to look.
        ///
        /// Far enough to cover the road you are on and the junction at each end of it, and no
        /// further: a jam three streets away that you cannot see is not a jam, it is scenery,
        /// and it will have been streamed out before you get there.
        /// </summary>
        private const float Radius = 80f;

        private const int UpdateIntervalMs = 2000;

        /// <summary>Stopped this long with a driver aboard is a jam, not a red light.</summary>
        private const int StuckMs = 22000;

        /// <summary>Anything under this is stopped as far as anybody watching is concerned.</summary>
        private const float MovingSpeed = 0.8f;

        /// <summary>
        /// How long an EMPTY car sits in the road before it gets taken away.
        ///
        /// Much longer than the driver case, because this one is destructive and the game
        /// usually sorts it out on its own given a minute. This is the backstop for when it
        /// does not.
        /// </summary>
        private const int AbandonedMs = 45000;

        /// <summary>Close enough to the kerb that it is parked rather than blocking.</summary>
        private const float KerbRange = 4.2f;

        /// <summary>Vehicle handle to when it stopped. Cleared out as they move on.</summary>
        private readonly Dictionary<int, int> _still = new Dictionary<int, int>();

        private int _lastUpdate;

        /// <summary>
        /// Set by Main: vehicles that belong to the mod and are stopped on purpose.
        ///
        /// The plug is parked outside the house because he was told to park outside the house.
        /// Sending him off about his business would be this class breaking the thing it is
        /// supposed to be helping.
        /// </summary>
        public Func<Vehicle, bool> Ours;

        /// <summary>Empty cars in the road: handle to when we first saw it stopped there.</summary>
        private readonly Dictionary<int, int> _abandoned = new Dictionary<int, int>();

        public void Update()
        {
            var now = Game.GameTime;
            if (now - _lastUpdate < UpdateIntervalMs) return;
            _lastUpdate = now;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            // Whatever you are standing next to, or driving through. Your own car is skipped
            // below -- being sat still in it is your business -- but everything around it is
            // fair game, because a queue you are stuck in the middle of is exactly the thing
            // worth clearing.
            var here = player.Position;

            try
            {
                var seen = new HashSet<int>();

                foreach (var car in World.GetNearbyVehicles(here, Radius))
                {
                    if (car == null || !car.Exists()) continue;
                    if (Ours != null && Ours(car)) continue;
                    if (player.CurrentVehicle != null && player.CurrentVehicle.Handle == car.Handle) continue;

                    var driver = car.Driver;

                    // Nobody at the wheel. Nothing to task, so it is handled separately -- but
                    // only if it is not a car somebody would come back to. The one you parked
                    // and walked away from, and anything wearing a blip, belong to you or to
                    // another script, and an empty-road watchdog eating the player's car is a
                    // far worse bug than the one it exists to fix.
                    if (driver == null || !driver.Exists() || !driver.IsAlive)
                    {
                        if (!Spoken(player, car)) Abandoned(car, now);
                        continue;
                    }

                    if (driver.Handle == player.Handle) continue;

                    seen.Add(car.Handle);

                    if (car.Speed > MovingSpeed)
                    {
                        _still.Remove(car.Handle);
                        continue;
                    }

                    int since;
                    if (!_still.TryGetValue(car.Handle, out since))
                    {
                        _still[car.Handle] = now;
                        continue;
                    }

                    if (now - since < StuckMs) continue;

                    _still.Remove(car.Handle);
                    MoveAlong(car, driver);
                }

                // Anything that has left the area stops being watched.
                if (_still.Count > seen.Count)
                {
                    var gone = new List<int>();
                    foreach (var handle in _still.Keys)
                    {
                        if (!seen.Contains(handle)) gone.Add(handle);
                    }

                    foreach (var handle in gone) _still.Remove(handle);
                }

                // The abandoned list has no natural end -- an empty car does not drive out of
                // range -- so anything that no longer exists is dropped here. Handles get
                // reused, and a stale one means the next car to inherit it is judged on how
                // long a different car stood somewhere else.
                var dead = new List<int>();
                foreach (var handle in _abandoned.Keys)
                {
                    var car = Entity.FromHandle(handle) as Vehicle;
                    if (car == null || !car.Exists()) dead.Add(handle);
                }

                foreach (var handle in dead) _abandoned.Remove(handle);
            }
            catch (Exception ex)
            {
                Log.Debug("Traffic scan failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Whether somebody has a claim on this car: the player's own, or one another script
        /// has marked. Either way it is not ours to move and certainly not ours to remove.
        /// </summary>
        private static bool Spoken(Ped player, Vehicle car)
        {
            try
            {
                if (player.LastVehicle != null && player.LastVehicle.Handle == car.Handle) return true;
                if (Function.Call<int>(Hash.GET_BLIP_FROM_ENTITY, car.Handle) != 0) return true;
            }
            catch
            {
                // If we cannot tell, assume somebody does. Leaving a car alone is free.
                return true;
            }

            return false;
        }

        /// <summary>
        /// An empty car standing in the road.
        ///
        /// Nothing can be tasked, so the first move is to hand it back: a vehicle the mod or a
        /// script made persistent is invisible to the game's population control, and giving it
        /// back is usually enough on its own -- the game takes it away as soon as you are not
        /// looking, which is exactly what should have happened in the first place.
        ///
        /// If it is still there three quarters of a minute later, it goes. Only off screen, so
        /// nothing ever pops out of existence in front of you, and only if it is out in a lane:
        /// a car against a kerb is parked, and every street in Los Santos is lined with them.
        /// </summary>
        private void Abandoned(Vehicle car, int now)
        {
            try
            {
                if (car.Speed > MovingSpeed)
                {
                    _abandoned.Remove(car.Handle);
                    return;
                }

                // Against the kerb is parked, not stuck.
                var kerb = World.GetNextPositionOnStreet(car.Position);
                if (kerb != Vector3.Zero && car.Position.DistanceTo(kerb) > KerbRange)
                {
                    _abandoned.Remove(car.Handle);
                    return;
                }

                int since;
                if (!_abandoned.TryGetValue(car.Handle, out since))
                {
                    _abandoned[car.Handle] = now;

                    // Give it back to the game before doing anything harsher.
                    car.IsPersistent = false;
                    car.MarkAsNoLongerNeeded();
                    return;
                }

                if (now - since < AbandonedMs) return;
                if (car.IsOnScreen) return;

                _abandoned.Remove(car.Handle);
                car.Delete();

                Log.Debug("Traffic: removed a car abandoned in the road.");
            }
            catch
            {
                // Not worth an exception. It will stream out eventually.
            }
        }

        /// <summary>
        /// Tells one driver to get on with it.
        ///
        /// Cleared and re-tasked to ordinary wandering, which is what a car on a road is doing
        /// when nothing else is going on. The nudge forward first is for the case where he is
        /// nose to tail with something and has decided there is no way through.
        /// </summary>
        private static void MoveAlong(Vehicle car, Ped driver)
        {
            try
            {
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, driver.Handle, false);
                Function.Call(Hash.CLEAR_PED_TASKS, driver.Handle);

                // 786606: the ordinary road rules, minus waiting behind stationary traffic, plus
                // going round it. The same style the plug drives with, for the same reason.
                Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, driver.Handle, car.Handle, 14f, 786606);

                Log.Debug("Traffic: sent a stopped car on its way.");
            }
            catch
            {
                // It will sit there. Nothing worth an exception.
            }
        }
    }
}
