using System;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Locations
{
    /// <summary>
    /// A yard with nobody else's cars in it.
    ///
    /// LAMAR'S LOT IS A SET, NOT A CAR PARK. The mod puts a handful of cars on it on purpose,
    /// stands a party round them and points a camera at the whole thing -- and the game fills
    /// the rest of the concrete with whatever its own parked-car generators feel like, so the
    /// scene somebody built is three of ours and four taxis. From the fence you cannot tell
    /// which is which, and the ones that do not belong are usually the ones in the way.
    ///
    /// TWO HALVES, AND ONE ALONE IS NOT ENOUGH. Deleting what is there is pointless while the
    /// generator puts another one back thirty seconds later, and switching the generator off
    /// does nothing about the four already sitting on it. So the generators in the box are
    /// held off AND what is already inside is taken away.
    ///
    /// NOBODY ELSE'S, AND NOTHING WITH ANYBODY IN IT. Ours is the mod's own answer to "is this
    /// car parked on purpose" -- the same one the traffic watch uses, so a car you bought and
    /// left on the lot is as safe as one the mod placed. An occupied car is never touched
    /// either: deleting one takes the driver with it, and a car with somebody in it is leaving
    /// under its own power anyway.
    ///
    /// A CIRCLE, AND A SMALL ONE. The lot is what this is for; the alley behind it and the
    /// road past it are the city's and are left alone. Anything that wants a different shape
    /// can have a second one of these rather than a bigger radius.
    /// </summary>
    internal sealed class KeepClear
    {
        private readonly Vector3 _where;
        private readonly float _radius;
        private readonly string _name;

        /// <summary>Set by Main: anything the rest of the mod considers parked on purpose.</summary>
        public Func<Vehicle, bool> Ours;

        /// <summary>How far away the player can be before this stops bothering.</summary>
        private const float Watch = 140f;

        /// <summary>How high and low the box reaches. A car on a road above is not in this yard.</summary>
        private const float Lift = 8f;

        private const int EveryMs = 1500;

        private int _at;
        private bool _held;
        private bool _moaned;
        private int _took;

        public KeepClear(Vector3 where, float radius, string name)
        {
            _where = where;
            _radius = radius;
            _name = name;
        }

        public void Update()
        {
            int now;

            try { now = Game.GameTime; }
            catch { return; }

            if (now - _at < EveryMs) return;
            _at = now;

            Ped me;

            try
            {
                me = Game.Player.Character;
                if (me == null || !me.Exists()) return;
            }
            catch
            {
                return;
            }

            // OUT OF RANGE IS NOT THE SAME AS OFF. The generators stay held whether or not
            // anybody is looking -- that is the whole point of holding them, so the lot is
            // already empty when you walk round the corner -- but there is nothing to sweep
            // from the other side of the map.
            Hold();

            if (me.Position.DistanceTo(_where) > Watch) return;

            Sweep(me);
        }

        /// <summary>
        /// Switches the game's own parked cars off over the box. Said again every pass, not
        /// once.
        ///
        /// BECAUSE SOMEBODY ELSE KEEPS SWITCHING THEM BACK ON. The only way to undo an
        /// in-area hold is SET_ALL_VEHICLE_GENERATORS_ACTIVE, which is not "in area" at all --
        /// it is every generator on the map, and the port run calls it every time it lets the
        /// dock go. So a hold set once at the docks' convenience lasts until the first errand
        /// finishes and then the yard quietly fills up again, three miles from anything that
        /// knows this exists. Repeating it is one native call every second and a half and it
        /// cannot be got wrong.
        /// </summary>
        private void Hold()
        {
            try
            {
                Function.Call(Hash.SET_ALL_VEHICLE_GENERATORS_ACTIVE_IN_AREA,
                              _where.X - _radius, _where.Y - _radius, _where.Z - Lift,
                              _where.X + _radius, _where.Y + _radius, _where.Z + Lift,
                              false, true);

                if (_held) return;
                _held = true;

                Log.Info("Keeping " + _name + " clear: the game's parked cars are off in that box.");
            }
            catch (Exception ex)
            {
                // Then they keep coming and the sweep keeps taking them away, which is the
                // slower half of the same job rather than a failure. Said once: this runs on
                // a clock now and a fault that repeats would otherwise fill the log with the
                // same sentence forty times a minute.
                if (_moaned) return;
                _moaned = true;

                Log.Debug("Could not hold the generators at " + _name + ": " + ex.Message);
            }
        }

        private void Sweep(Ped me)
        {
            try
            {
                foreach (var car in World.GetNearbyVehicles(_where, _radius))
                {
                    if (car == null || !car.Exists()) continue;

                    // The box, not the sphere. GetNearbyVehicles is flat about height and a
                    // car on the road above the lot is not on the lot.
                    if (Math.Abs(car.Position.Z - _where.Z) > Lift) continue;

                    if (Ours != null && Ours(car)) continue;

                    if (His(car, me)) continue;

                    if (!Empty(car)) continue;

                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, car.Handle, true, true);
                    car.Delete();

                    _took++;

                    // ONCE A HANDFUL RATHER THAN ONCE EACH. A line per car is a log full of
                    // taxis; the count says the same thing and says it when it is worth
                    // reading.
                    if (_took % 5 == 0)
                    {
                        Log.Info("Keeping " + _name + " clear: " + _took + " car(s) taken off it so far.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not sweep " + _name + ": " + ex.Message);
            }
        }

        /// <summary>
        /// The one he drove in.
        ///
        /// OURS COVERS WHAT THE MOD PLACED AND WHAT HE BOUGHT -- a plate in the save -- and
        /// neither of those is the car he took off somebody on Grove Street four minutes ago
        /// and left running by the fence. That one is not on any list and is not coming back
        /// if it goes, and having it vanish while your back is turned is worse than a taxi
        /// parked where it should not be. LastVehicle survives getting out, so it holds for as
        /// long as he has not driven anything else.
        /// </summary>
        private static bool His(Vehicle car, Ped me)
        {
            try
            {
                if (me.IsInVehicle(car)) return true;

                var last = me.LastVehicle;

                return last != null && last.Exists() && last.Handle == car.Handle;
            }
            catch
            {
                return true;
            }
        }

        private static bool Empty(Vehicle car)
        {
            try
            {
                return Function.Call<int>(Hash.GET_VEHICLE_NUMBER_OF_PASSENGERS, car.Handle) == 0 &&
                       Function.Call<bool>(Hash.IS_VEHICLE_SEAT_FREE, car.Handle, -1, false);
            }
            catch
            {
                // If it cannot be asked, it is left alone. Never delete a car that might have
                // somebody in it.
                return false;
            }
        }

        /// <summary>Gives the lot its own parked cars back. Called when the mod unloads.</summary>
        public void RestoreWorld()
        {
            if (!_held) return;
            _held = false;

            try { Function.Call(Hash.SET_ALL_VEHICLE_GENERATORS_ACTIVE); }
            catch { /* they come back on their own eventually */ }
        }
    }
}
