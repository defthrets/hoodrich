using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.State;

namespace Hoodrich.Locations
{
    /// <summary>
    /// The cars you have actually paid for, kept between sessions.
    ///
    /// Buying one off Hao used to make it yours for as long as the game felt like keeping it.
    /// IsPersistent and SET_ENTITY_AS_MISSION_ENTITY hold a vehicle against the population
    /// manager, which is the whole of what they do -- neither of them survives a reload, so a
    /// car you spent twenty-two thousand on was gone the next time you started up and the only
    /// record left was Hao refusing to sell you another one.
    ///
    /// So the car is written down rather than held: model, plate, paint, and where it was last
    /// seen standing. On a session where it is missing from the world it gets put back exactly
    /// there. That is not the same thing as a garage and does not pretend to be -- there is no
    /// interior, no list to browse and nothing stopping it being blown up. It is a car parked
    /// where you left it, which is what it was before you closed the game.
    ///
    /// Identified by PLATE rather than by model or by handle. Handles do not survive a reload
    /// and a model is not unique -- park a bought Baller next to a traffic one and a
    /// model-and-distance test picks whichever it scanned first. A plate we wrote ourselves is
    /// the one thing about that car nothing else in the world shares.
    /// </summary>
    internal sealed class OwnedCars
    {
        /// <summary>
        /// How far a plate scan reaches.
        ///
        /// This has to be the LARGER of the two. It was ninety against a two hundred and
        /// twenty metre replace range, which meant a ring around your own car -- from ninety
        /// metres out to two hundred and twenty -- where the search could not see it and the
        /// replacement fired anyway. Standing anywhere in that ring built a fresh Asbo every
        /// few seconds, each one immediately invisible to the next scan, until the population
        /// manager started culling them and took the real one with the copies.
        ///
        /// That is what "it is gone everywhere" was. Not a car that failed to save -- a car
        /// that was being rebuilt faster than the game could keep it.
        /// </summary>
        private const float FoundRange = 130f;

        /// <summary>
        /// How close you have to be before a car being missing MEANS anything.
        ///
        /// Deliberately shorter than the scan. Vehicles only exist while the game has streamed
        /// them in, so "I cannot see it" is only evidence from close up -- from across the
        /// map it is evidence of nothing at all, and acting on it is what put a dozen of them
        /// on that street.
        /// </summary>
        private const float SeenRange = 100f;

        /// <summary>Scanning every vehicle around the player is not a per-frame job.</summary>
        private const int TickMs = 2500;

        /// <summary>The soonest a given car may be stood back up after the last attempt.</summary>
        private const int RebuildGapMs = 30000;

        private readonly PlayerState _state;
        private readonly Dictionary<string, int> _rebuiltAt =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private int _next;

        private int Since(OwnedCar owned)
        {
            int at;
            return _rebuiltAt.TryGetValue(owned.Id ?? "", out at) ? at : int.MinValue / 2;
        }

        private void Stamp(OwnedCar owned, int now)
        {
            _rebuiltAt[owned.Id ?? ""] = now;
        }

        public OwnedCars(PlayerState state)
        {
            _state = state;
        }

        /// <summary>
        /// Writes a bought car down, and stamps the plate that identifies it forever after.
        ///
        /// Called at the moment of sale, while the car is still standing on the lot -- which is
        /// the one moment everything about it is known and true at the same time.
        /// </summary>
        public void Bought(string id, string name, Vehicle car)
        {
            if (_state == null || car == null || !car.Exists()) return;

            var plate = PlateFor(id);

            try
            {
                Function.Call(Hash.SET_VEHICLE_NUMBER_PLATE_TEXT, car.Handle, plate);
            }
            catch
            {
                // A plate we cannot set is a car we cannot find again, but the sale still stands.
            }

            var owned = new OwnedCar
            {
                Id = id,
                Name = name ?? id,
                Model = car.Model.Hash,
                Plate = plate,
                Paint = Colour(car, true),
                Paint2 = Colour(car, false),
                Where = car.Position,
                Heading = car.Heading
            };

            _state.Owned.RemoveAll(o => string.Equals(o.Id, id, StringComparison.OrdinalIgnoreCase));
            _state.Owned.Add(owned);
            _state.Touch();

            Log.Info("Bought car " + id + " recorded on plate " + plate + ".");
        }

        /// <summary>
        /// Keeps the record honest, and puts back anything the world has lost.
        ///
        /// Two jobs on the same scan because they are the same question asked twice: is this
        /// car where we think it is? If it is, remember where that actually was. If it is not,
        /// and we are close enough that it ought to be visible, stand it back up.
        /// </summary>
        public void Update()
        {
            if (_state == null || _state.Owned.Count == 0) return;

            var now = Game.GameTime;
            if (now - _next < TickMs) return;
            _next = now + TickMs;

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return;

            var here = player.Position;
            var moved = false;

            foreach (var owned in _state.Owned)
            {
                // Searched around where the CAR was left, not around the player. A car parked
                // round the corner has not moved because you walked away from it.
                var live = Find(owned, owned.Where);

                if (live != null)
                {
                    // Seen it. Where it is now is where it will be next time.
                    if (live.Position.DistanceTo(owned.Where) > 2f)
                    {
                        owned.Where = live.Position;
                        owned.Heading = live.Heading;
                        moved = true;
                    }

                    Hold(live);
                    continue;
                }

                // Not in the world. Only worth doing anything about it if you are close enough
                // to be looking at the space where it should be.
                if (here.DistanceTo(owned.Where) > SeenRange) continue;

                // And not again for a while, whatever happens.
                //
                // A belt-and-braces stop on exactly the failure above: even if a car cannot be
                // found for some reason nobody has thought of yet, this puts a floor under how
                // often that mistake can be repeated. One car every thirty seconds is
                // recoverable and visible in the log; one every two and a half is a flood.
                if (now - Since(owned) < RebuildGapMs) continue;

                Stamp(owned, now);

                if (PutBack(owned)) moved = true;
            }

            if (moved) _state.Touch();
        }

        /// <summary>The one with our plate on it, if it is anywhere nearby.</summary>
        private static Vehicle Find(OwnedCar owned, Vector3 here)
        {
            try
            {
                foreach (var car in World.GetNearbyVehicles(here, FoundRange))
                {
                    if (car == null || !car.Exists()) continue;

                    var plate = Function.Call<string>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT, car.Handle);
                    if (!string.Equals(plate, owned.Plate, StringComparison.OrdinalIgnoreCase)) continue;

                    return car;
                }
            }
            catch
            {
                // A scan that failed is not an answer either way, so it waits for the next one.
            }

            return null;
        }

        /// <summary>
        /// Stands it back where it was left.
        ///
        /// Everything that made it recognisable goes back on with it -- the paint, the plate,
        /// and the competition suspension every car on that lot leaves with. A car that comes
        /// back a different colour on stock springs is not the car you bought.
        /// </summary>
        private static bool PutBack(OwnedCar owned)
        {
            try
            {
                var model = new Model(owned.Model);
                if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) return false;

                var car = World.CreateVehicle(model, owned.Where, owned.Heading);
                model.MarkAsNoLongerNeeded();

                if (car == null || !car.Exists()) return false;

                var h = car.Handle;

                Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, h);
                Function.Call(Hash.SET_VEHICLE_NUMBER_PLATE_TEXT, h, owned.Plate);

                if (owned.Paint >= 0)
                {
                    Function.Call(Hash.SET_VEHICLE_COLOURS, h, owned.Paint,
                                  owned.Paint2 >= 0 ? owned.Paint2 : owned.Paint);
                }

                // The mod kit first, or nothing after it takes.
                Function.Call(Hash.SET_VEHICLE_MOD_KIT, h, 0);

                try
                {
                    var n = Function.Call<int>(Hash.GET_NUM_VEHICLE_MODS, h, 15);
                    if (n > 0) Function.Call(Hash.SET_VEHICLE_MOD, h, 15, n - 1, false);
                }
                catch
                {
                    // Stock ride height. Still the right car.
                }

                Hold(car);

                Log.Info("Put back owned car " + owned.Id + " at " + owned.Where + ".");
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not put back " + owned.Id + ": " + ex.Message);
                return false;
            }
        }

        /// <summary>Marks a car as the player's, so nothing tows or reclaims it out from under him.</summary>
        private static void Hold(Vehicle car)
        {
            try
            {
                Function.Call(Hash.SET_VEHICLE_HAS_BEEN_OWNED_BY_PLAYER, car.Handle, true);
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, car.Handle, true, true);
                Function.Call(Hash.SET_VEHICLE_IS_STOLEN, car.Handle, false);

                car.IsPersistent = true;
            }
            catch
            {
                // It is still the car; it is just less protected than we would like.
            }
        }

        private static int Colour(Vehicle car, bool primary)
        {
            try
            {
                var a = new OutputArgument();
                var b = new OutputArgument();

                Function.Call(Hash.GET_VEHICLE_COLOURS, car.Handle, a, b);

                return primary ? a.GetResult<int>() : b.GetResult<int>();
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        /// A plate nothing else in Los Santos is wearing.
        ///
        /// Eight characters is the hard limit, so it is HR plus six of the lot id's hash --
        /// stable for a given car, different for every other one, and it reads as a plate
        /// rather than as a serial number if anybody looks at it.
        /// </summary>
        private static string PlateFor(string id)
        {
            var hash = 5381;

            foreach (var c in id ?? "")
            {
                unchecked { hash = hash * 33 + c; }
            }

            return "HR" + Math.Abs(hash % 1000000).ToString("000000");
        }
    }
}
