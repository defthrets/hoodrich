using System;
using GTA;
using Color = System.Drawing.Color;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Locations
{
    /// <summary>
    /// A car that belongs somewhere and stays there.
    ///
    /// The same idea as <see cref="Fixture"/>, which streams a prop at an exact spot, except a
    /// vehicle needs three things a prop does not: a paint job, something decided about the
    /// engine, and -- most importantly -- somebody to tell the traffic watchdog that it is
    /// parked on purpose.
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
        private readonly int _paint;

        /// <summary>
        /// Puts back the three things the game turns off behind your back.
        ///
        /// Engine, radio and underglow are all electrics, and the game switches them off on an
        /// unoccupied vehicle -- on a timer, when the area streams out and back, and whenever
        /// anything else in the world decides a parked car should be quiet. Set once at spawn
        /// they were correct for about a minute, which is exactly as long as it takes to walk
        /// over and notice.
        ///
        /// Only while nobody is in it. The moment somebody gets in it stops being scenery and
        /// becomes their car -- fighting a player over his own radio every second and a half is
        /// worse than a van that goes quiet when it is driven away.
        /// </summary>
        private void Keep()
        {
            if (!Running && Neon == null && string.IsNullOrEmpty(Radio)) return;

            try
            {
                if (_car == null || !_car.Exists()) return;

                var driver = _car.Driver;
                if (driver != null && driver.Exists()) return;

                // Off hours: the key comes out. All three go together because they are one
                // switch -- somebody turned it off and went inside.
                if (Quiet)
                {
                    Function.Call(Hash.SET_VEHICLE_ENGINE_ON, _car.Handle, false, true, false);
                    Function.Call(Hash.SET_VEHICLE_RADIO_ENABLED, _car.Handle, false);

                    if (Neon.HasValue)
                    {
                        for (var side = 0; side < 4; side++)
                        {
                            Function.Call(Hash.SET_VEHICLE_NEON_ENABLED, _car.Handle, side, false);
                        }
                    }

                    return;
                }

                if (Running)
                {
                    Function.Call(Hash.SET_VEHICLE_ENGINE_ON, _car.Handle, true, true, false);
                }

                if (!string.IsNullOrEmpty(Radio))
                {
                    Function.Call(Hash.SET_VEHICLE_RADIO_ENABLED, _car.Handle, true);
                    Function.Call(Hash.SET_VEH_RADIO_STATION, _car.Handle, Radio);
                    Function.Call(Hash.SET_VEHICLE_RADIO_LOUD, _car.Handle, true);
                }

                if (Neon.HasValue) Light(Neon.Value);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not keep the parked car going: " + ex.Message);
            }
        }

        /// <summary>
        /// Every performance mod at its top index, the turbo, and the body kit.
        ///
        /// Off by default, because most of these are somebody's parked car and a stock car is
        /// what a parked car looks like. On for the one that is somebody's PROJECT.
        /// </summary>
        public bool Built;

        /// <summary>Underglow, if it has any. Null for a car nobody has lit.</summary>
        public Color? Neon;

        /// <summary>Whether the boot is standing open -- a van being unloaded, or worked on.</summary>
        public bool BootOpen;

        /// <summary>
        /// Interior and dashboard paint, or null for whatever it came with.
        ///
        /// The natives for this are in the enum as SET_VEHICLE_EXTRA_COLOUR_5 and _6, which is
        /// why they look missing when you go searching for INTERIOR and DASHBOARD. Five is the
        /// interior and six is the dash; checked against the assembly rather than called by a
        /// hash typed from memory, because a wrong native hash is a crash and not an error.
        /// </summary>
        public int? Interior;

        /// <summary>
        /// Whether it sits there with the engine running.
        ///
        /// Not a detail. An unoccupied car with the engine off has no radio you can hear and no
        /// underglow that stays lit -- the game switches both off with the electrics, which is
        /// why the neon came on at spawn and was dark by the time anybody walked over. A van
        /// somebody has parked up to play music out of is a van with the key still in it.
        /// </summary>
        public bool Running;

        /// <summary>The station, if it is playing anything.</summary>
        public string Radio;

        /// <summary>
        /// The hours it goes dark and quiet, or -1 for never.
        ///
        /// The engine, the radio and the underglow all go together, because they are the same
        /// switch: somebody came out, turned the key off and went in. A van still thumping at
        /// four in the morning in a yard everybody has left is the one thing that would give
        /// the whole trick away.
        /// </summary>
        public int QuietFrom = -1;
        public int QuietTo = -1;

        private bool Quiet
        {
            get
            {
                if (QuietFrom < 0 || QuietTo < 0 || QuietFrom == QuietTo) return false;

                try
                {
                    var hour = Function.Call<int>(Hash.GET_CLOCK_HOURS);

                    return QuietTo > QuietFrom
                        ? hour >= QuietFrom && hour < QuietTo
                        : hour >= QuietFrom || hour < QuietTo;
                }
                catch
                {
                    return false;
                }
            }
        }

        private Vehicle _car;
        private int _lastUpdate;

        public ParkedCar(Vector3 where, float heading, int paint,
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
            if (_car != null)
            {
                Keep();
                return;
            }

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
                    Function.Call(Hash.SET_VEHICLE_ENGINE_ON, _car.Handle, Running, true, false);
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
        /// Paint and wheels.
        ///
        /// The colour is one of the game's own paint indices rather than an RGB triple. RGB
        /// gives a flat colour with no flake in it, which is how the first attempt came out as
        /// hard poster green -- the paint table has the finish baked in, and a lowrider is
        /// painted, not printed.
        ///
        /// The wheels are Benny's, which is a two-step thing: the wheel TYPE has to be set to
        /// the Benny's family before the rim index means anything, and the mod kit has to be
        /// open before either. Set them in the wrong order and you get stock wheels and no
        /// error to tell you why.
        /// </summary>
        private void Paint()
        {
            try
            {
                Function.Call(Hash.SET_VEHICLE_MOD_KIT, _car.Handle, 0);

                Function.Call(Hash.SET_VEHICLE_COLOURS, _car.Handle, _paint, _paint);

                // Everything below the paint is car-shaped: lowrider rims, lowered springs and
                // tinted glass mean nothing on two wheels or four small ones. Asked rather than
                // assumed, so a bike parked here later does not quietly get a suspension kit it
                // has no springs for.
                var onWheels = !Function.Call<bool>(Hash.IS_THIS_MODEL_A_BIKE, _car.Model.Hash)
                               && !Function.Call<bool>(Hash.IS_THIS_MODEL_A_QUADBIKE, _car.Model.Hash);

                if (onWheels)
                {
                    // Benny's Original, and a rim out of that set. Lowered on its springs,
                    // because a lowrider sitting at factory height is a saloon with nice wheels.
                    Function.Call(Hash.SET_VEHICLE_WHEEL_TYPE, _car.Handle, BennysWheels);
                    Function.Call(Hash.SET_VEHICLE_MOD, _car.Handle, 23, BennysRim, false);
                    Function.Call(Hash.SET_VEHICLE_MOD, _car.Handle, 15, 3, false);

                    Function.Call(Hash.SET_VEHICLE_WINDOW_TINT, _car.Handle, 1);
                }

                Function.Call(Hash.SET_VEHICLE_DIRT_LEVEL, _car.Handle, Built ? 0.4f : 1.5f);

                if (Built) BuildIt();

                // Five is the interior, six is the dash. Both, because a green interior with a
                // black dashboard is half a job you can see from the door.
                if (Interior.HasValue)
                {
                    Function.Call(Hash.SET_VEHICLE_EXTRA_COLOUR_5, _car.Handle, Interior.Value);
                    Function.Call(Hash.SET_VEHICLE_EXTRA_COLOUR_6, _car.Handle, Interior.Value);
                }

                Keep();
                if (Neon.HasValue) Light(Neon.Value);

                // Door 5 is the boot. Instantly rather than swung, because the car is being
                // created in front of you and a boot easing itself open on spawn is a car
                // doing something rather than a car that was already like that.
                if (BootOpen)
                {
                    Function.Call(Hash.SET_VEHICLE_DOOR_OPEN, _car.Handle, 5, false, true);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not paint the parked car: " + ex.Message);
            }
        }

        /// <summary>
        /// Everything the shop sells, at the top of each list.
        ///
        /// Asked for rather than assumed: GET_NUM_VEHICLE_MODS says how many a given car
        /// actually has, and the top one is the last index. Hardcoding "4" fits some cars and
        /// silently does nothing on the rest, which is the sort of thing that looks like the
        /// mod not working.
        /// </summary>
        private void BuildIt()
        {
            // 11 engine, 12 brakes, 13 transmission, 15 suspension, 16 armour -- and then the
            // body: 0 spoiler through 10 roof, which is what makes it read as somebody's build
            // rather than a stock van with a fast engine nobody can see.
            foreach (var kind in new[] { 11, 12, 13, 15, 16,
                                         0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 })
            {
                try
                {
                    var many = Function.Call<int>(Hash.GET_NUM_VEHICLE_MODS, _car.Handle, kind);
                    if (many > 0) Function.Call(Hash.SET_VEHICLE_MOD, _car.Handle, kind, many - 1, false);
                }
                catch { /* a slot this car has not got */ }
            }

            try
            {
                Function.Call(Hash.TOGGLE_VEHICLE_MOD, _car.Handle, 18, true);   // turbo
                Function.Call(Hash.TOGGLE_VEHICLE_MOD, _car.Handle, 22, true);   // xenons
            }
            catch { /* neither is worth a log line */ }
        }

        /// <summary>
        /// Underglow, all four sides, in one colour.
        ///
        /// The natives are SET_VEHICLE_NEON_ENABLED and SET_VEHICLE_NEON_COLOUR in this build
        /// -- not the _LIGHT_ spellings the docs use, which are simply not in the enum. Checked
        /// against the assembly rather than typed from memory.
        /// </summary>
        private void Light(Color c)
        {
            try
            {
                for (var side = 0; side < 4; side++)
                {
                    Function.Call(Hash.SET_VEHICLE_NEON_ENABLED, _car.Handle, side, true);
                }

                Function.Call(Hash.SET_VEHICLE_NEON_COLOUR, _car.Handle, (int)c.R, (int)c.G, (int)c.B);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not light the parked car: " + ex.Message);
            }
        }

        /// <summary>Wheel type 7 is the Benny's Original family.</summary>
        private const int BennysWheels = 7;

        /// <summary>
        /// Knock-Offs, out of that set.
        ///
        /// Fourth in the shop's list, which is index 2: the list opens with Stock -- index -1,
        /// the absence of a wheel choice -- so the numbered ones start one line below it.
        /// </summary>
        private const int BennysRim = 2;

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
