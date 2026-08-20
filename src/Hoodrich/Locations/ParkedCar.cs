using System;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Locations
{
    /// <summary>
    /// A car that belongs somewhere and stays there.
    ///
    /// The same idea as <see cref="Fixture"/>, which streams a prop at an exact spot, except a
    /// vehicle needs three things a prop does not: a paint job, an engine that is off, and --
    /// most importantly -- somebody to tell the traffic watchdog that it is parked on purpose.
    ///
    /// That last one is not optional. TrafficWatch removes empty cars standing in a lane, which
    /// is exactly what this is, and without the exemption the mod would spend its afternoon
    /// deleting its own scenery.
    /// </summary>
    internal sealed class ParkedCar
    {
        /// <summary>Close enough to be worth having it there.</summary>
        private const float StreamRange = 140f;

        private const int UpdateIntervalMs = 1800;

        private readonly Vector3 _where;
        private readonly float _heading;
        private readonly string[] _models;
        private readonly System.Drawing.Color _paint;

        private Vehicle _car;
        private int _lastUpdate;

        public ParkedCar(Vector3 where, float heading, System.Drawing.Color paint,
                         params string[] models)
        {
            _where = where;
            _heading = heading;
            _paint = paint;
            _models = models;
        }

        /// <summary>Whether this is our car, for the traffic watchdog.</summary>
        public bool Owns(Vehicle car)
        {
            return car != null && _car != null && _car.Exists() && car.Handle == _car.Handle;
        }

        public void Update()
        {
            var now = Game.GameTime;
            if (now - _lastUpdate < UpdateIntervalMs) return;
            _lastUpdate = now;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            if (_car != null && !_car.Exists()) _car = null;

            // Once it is out there it is left alone. Not put back on its mark, not re-parked --
            // if somebody has taken it for a drive then it is a car that got taken, which is a
            // better thing to happen on a block than a car that cannot be moved.
            if (_car != null) return;

            if (player.Position.DistanceTo(_where) > StreamRange) return;

            Make();
        }

        private void Make()
        {
            foreach (var name in _models)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) continue;

                    _car = World.CreateVehicle(model, _where, _heading);
                    model.MarkAsNoLongerNeeded();

                    if (_car == null || !_car.Exists()) continue;

                    _car.IsPersistent = true;
                    _car.Position = _where;
                    _car.Heading = _heading;

                    // Engine off and on the ground properly, so it reads as parked rather than
                    // as something that has just been put there.
                    Function.Call(Hash.SET_VEHICLE_ENGINE_ON, _car.Handle, false, true, false);
                    Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, _car.Handle);
                    Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, _car.Handle, 1);

                    Paint();

                    Log.Info("Parked a " + name + " at " + _where + ".");
                    return;
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not park a " + name + ": " + ex.Message);
                }
            }
        }

        /// <summary>
        /// The colour, given as RGB rather than one of the game's paint indices.
        ///
        /// An index would mean looking up which number is the right green and hoping, and the
        /// set's colour is already written down in gangs.json -- so the car is painted the exact
        /// green the gang is drawn in everywhere else, which is the whole point of it being
        /// their car.
        /// </summary>
        private void Paint()
        {
            try
            {
                Function.Call(Hash.SET_VEHICLE_MOD_KIT, _car.Handle, 0);

                Function.Call(Hash.SET_VEHICLE_CUSTOM_PRIMARY_COLOUR, _car.Handle,
                              (int)_paint.R, (int)_paint.G, (int)_paint.B);

                Function.Call(Hash.SET_VEHICLE_CUSTOM_SECONDARY_COLOUR, _car.Handle,
                              (int)_paint.R, (int)_paint.G, (int)_paint.B);

                // Chrome wheels and dark glass. A gang car that is only a green shell is a
                // rental with the wrong paint on it.
                Function.Call(Hash.SET_VEHICLE_WHEEL_TYPE, _car.Handle, 7);
                Function.Call(Hash.SET_VEHICLE_MOD, _car.Handle, 23, 0, false);
                Function.Call(Hash.SET_VEHICLE_WINDOW_TINT, _car.Handle, 1);
                Function.Call(Hash.SET_VEHICLE_DIRT_LEVEL, _car.Handle, 2.5f);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not paint the parked car: " + ex.Message);
            }
        }

        public void RestoreWorld()
        {
            try
            {
                if (_car != null && _car.Exists())
                {
                    // Handed back rather than deleted. If the player is sitting in it, deleting
                    // it on unload drops him through the world.
                    _car.IsPersistent = false;
                    _car.MarkAsNoLongerNeeded();
                }
            }
            catch { /* teardown */ }

            _car = null;
        }
    }
}
