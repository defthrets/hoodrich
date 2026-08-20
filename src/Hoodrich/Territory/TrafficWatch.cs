using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Territory
{
    /// <summary>
    /// Cars that stop in the road outside the house and never move again.
    ///
    /// Something on this block jams traffic. It could be the delivery car sat on the mark with a
    /// lane and a half left, it could be somebody's bumper on the kerb, it could be one driver
    /// waiting on another driver who is waiting on him -- Los Santos manages that on its own
    /// without help. It does not much matter which: from the pavement it looks like a lorry
    /// parked across Forum Drive with its engine running, and it stays that way.
    ///
    /// Rather than guess at the cause this watches for the symptom, and only within sight of the
    /// house: a car with a driver in it, stopped dead for long enough that no light and no queue
    /// explains it, gets told to go about its business. If it was jammed it leaves. If it was
    /// parked it was not stopped WITH A DRIVER, so it is never touched.
    ///
    /// Nothing the mod owns is ever nudged -- those have their own jobs and their own watchdogs.
    /// </summary>
    internal sealed class TrafficWatch
    {
        /// <summary>Only near the house. The rest of the city is the game's problem.</summary>
        private const float Radius = 55f;

        private const int UpdateIntervalMs = 2000;

        /// <summary>Stopped this long with a driver aboard is a jam, not a red light.</summary>
        private const int StuckMs = 22000;

        /// <summary>Anything under this is stopped as far as anybody watching is concerned.</summary>
        private const float MovingSpeed = 0.8f;

        private readonly Vector3 _where;

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

        public TrafficWatch(Vector3 where)
        {
            _where = where;
        }

        public void Update()
        {
            var now = Game.GameTime;
            if (now - _lastUpdate < UpdateIntervalMs) return;
            _lastUpdate = now;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            // Only while you are actually here to see it.
            if (player.Position.DistanceTo(_where) > Radius * 2f)
            {
                if (_still.Count > 0) _still.Clear();
                return;
            }

            try
            {
                var seen = new HashSet<int>();

                foreach (var car in World.GetNearbyVehicles(_where, Radius))
                {
                    if (car == null || !car.Exists()) continue;
                    if (Ours != null && Ours(car)) continue;

                    var driver = car.Driver;
                    if (driver == null || !driver.Exists() || !driver.IsAlive) continue;
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
                if (_still.Count <= seen.Count) return;

                var gone = new List<int>();
                foreach (var handle in _still.Keys)
                {
                    if (!seen.Contains(handle)) gone.Add(handle);
                }

                foreach (var handle in gone) _still.Remove(handle);
            }
            catch (Exception ex)
            {
                Log.Debug("Traffic scan failed: " + ex.Message);
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

                Log.Debug("Traffic: sent a stopped car on its way outside the house.");
            }
            catch
            {
                // It will sit there. Nothing worth an exception.
            }
        }
    }
}
