using System;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Locations
{
    /// <summary>
    /// Franklin's own bike, swapped for the one he would actually ride.
    ///
    /// THE GAME GIVES HIM A BAGGER. It is spawned by the game's own scripts outside his house,
    /// brought back there when it is lost, and there is no call that changes which model those
    /// scripts use. So it is replaced as it appears: a Bagger with nobody on it, or with
    /// Franklin on it and stopped, within sight of him, is deleted and the bike named in
    /// Hoodrich.ini is put on the same spot -- same heading, same plate, same paint, the livery
    /// asked for -- and he is put back in the saddle if he was in it. The game notices its bike
    /// has gone at some point and puts another Bagger outside the house, and that one gets the
    /// same treatment when he next comes home, which is the price of not being able to tell
    /// the game's script anything, and is invisible unless you are watching the driveway from
    /// far enough away to see it happen.
    ///
    /// ONLY A BAGGER WITH NOBODY ELSE ON IT. The Lost ride Baggers now and then, and theirs is
    /// theirs. The mod spawns no Baggers of its own, so there is nothing of ours to protect.
    ///
    /// NOT ONE MORE BIKE EVERY TIME THE GAME RE-SUPPLIES ONE. If the last bike this made is
    /// still stood near the new Bagger, the Bagger simply goes: the game has put back a bike
    /// that is already there. A second one is only made when the first has been ridden away.
    ///
    /// The new bike is handed straight back to the game rather than kept, so it lives and dies
    /// by the rules of the one it replaced. It is not one more persistent thing on a map that
    /// has plenty.
    /// </summary>
    internal sealed class HisBike
    {
        private const int TickMs = 1500;
        private const float LookRange = 120f;

        /// <summary>Not swapped out from under him while he is actually riding it.</summary>
        private const float StoppedSpeed = 1.5f;

        /// <summary>A Bagger this close to the bike already made for him is a re-supply, not a new bike.</summary>
        private const float SameSpot = 60f;

        // A property rather than a static initialiser: a native called while the type is
        // being loaded runs before anything has checked the game is ready to be asked.
        private static int Bagger => Function.Call<int>(Hash.GET_HASH_KEY, "bagger");

        private readonly Settings _cfg;
        private int _lastTick;
        private Vehicle _last;

        public HisBike(Settings cfg)
        {
            _cfg = cfg;
        }

        private string Wanted => _cfg == null ? "sanchez" : (_cfg.FranklinsBike ?? "").Trim();
        private int Livery => _cfg == null ? 0 : _cfg.FranklinsBikeLivery;

        public void Update()
        {
            var now = Game.GameTime;
            if (now - _lastTick < TickMs) return;
            _lastTick = now;

            var want = Wanted;
            if (want.Length == 0 || string.Equals(want, "bagger", StringComparison.OrdinalIgnoreCase)) return;

            try
            {
                var player = Game.Player.Character;
                if (player == null || !player.Exists() || !player.IsAlive) return;

                foreach (var bike in World.GetNearbyVehicles(player.Position, LookRange))
                {
                    if (bike == null || !bike.Exists() || bike.IsDead) continue;
                    if (bike.Model.Hash != Bagger) continue;

                    var rider = bike.Driver;
                    var onIt = rider != null && rider.Exists() && rider.Handle == player.Handle;

                    if (rider != null && rider.Exists() && !onIt) continue;
                    if (onIt && Function.Call<float>(Hash.GET_ENTITY_SPEED, bike.Handle) > StoppedSpeed) continue;

                    Swap(bike, onIt ? player : null);
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("His bike: " + ex.Message);
            }
        }

        private void Swap(Vehicle old, Ped rider)
        {
            var at = old.Position;
            var heading = old.Heading;

            // The game putting back a bike that is already stood there.
            if (rider == null && _last != null && _last.Exists() && !_last.IsDead &&
                _last.Position.DistanceTo(at) <= SameSpot)
            {
                Bin(old);
                Log.Info("His bike: the game put the Bagger back; it went again.");
                return;
            }

            var model = new Model(Wanted);
            if (!model.IsValid || !model.IsInCdImage || !model.Request(2000))
            {
                Log.Warn("His bike: \"" + Wanted + "\" is not a vehicle this game has; the Bagger stays.");
                return;
            }

            // What the old one looked like, so the new one is his rather than a dealer's.
            var plate = Function.Call<string>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT, old.Handle) ?? "";
            var plateStyle = Function.Call<int>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT_INDEX, old.Handle);

            var primary = new OutputArgument();
            var secondary = new OutputArgument();
            Function.Call(Hash.GET_VEHICLE_COLOURS, old.Handle, primary, secondary);

            var pearl = new OutputArgument();
            var wheels = new OutputArgument();
            Function.Call(Hash.GET_VEHICLE_EXTRA_COLOURS, old.Handle, pearl, wheels);

            if (rider != null)
            {
                // Out of the saddle before the bike goes, or he goes with it.
                Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, rider.Handle);
            }

            Bin(old);

            var made = World.CreateVehicle(model, at, heading);
            model.MarkAsNoLongerNeeded();

            if (made == null || !made.Exists())
            {
                Log.Warn("His bike: could not put the " + Wanted + " down.");
                return;
            }

            try
            {
                Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, made.Handle);

                if (plate.Trim().Length > 0) Function.Call(Hash.SET_VEHICLE_NUMBER_PLATE_TEXT, made.Handle, plate);
                Function.Call(Hash.SET_VEHICLE_NUMBER_PLATE_TEXT_INDEX, made.Handle, plateStyle);

                Function.Call(Hash.SET_VEHICLE_COLOURS, made.Handle, primary.GetResult<int>(), secondary.GetResult<int>());
                Function.Call(Hash.SET_VEHICLE_EXTRA_COLOURS, made.Handle, pearl.GetResult<int>(), wheels.GetResult<int>());

                // The livery asked for, if the bike has that many. Counted from nought.
                var liveries = Function.Call<int>(Hash.GET_VEHICLE_LIVERY_COUNT, made.Handle);
                if (liveries > 0) Function.Call(Hash.SET_VEHICLE_LIVERY, made.Handle, Math.Max(0, Math.Min(Livery, liveries - 1)));

                Function.Call(Hash.SET_VEHICLE_HAS_BEEN_OWNED_BY_PLAYER, made.Handle, true);
                Function.Call(Hash.SET_VEHICLE_IS_STOLEN, made.Handle, false);

                if (rider != null)
                {
                    Function.Call(Hash.SET_PED_INTO_VEHICLE, rider.Handle, made.Handle, -1);
                    Function.Call(Hash.SET_VEHICLE_ENGINE_ON, made.Handle, true, true, false);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("His bike: could not finish dressing it: " + ex.Message);
            }

            // The game's, not ours.
            made.IsPersistent = false;
            made.MarkAsNoLongerNeeded();

            _last = made;

            Log.Info("His bike: the Bagger is a " + Wanted + " now" + (rider != null ? ", with him on it." : "."));
        }

        /// <summary>Takes the game's bike off the map.</summary>
        private static void Bin(Vehicle old)
        {
            try
            {
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, old.Handle, true, true);
                old.Delete();
            }
            catch
            {
                // Then it stays, and is looked at again next time.
            }
        }

        /// <summary>Nothing of ours is kept, so there is nothing to give back.</summary>
        public void RestoreWorld()
        {
            _last = null;
        }
    }
}
