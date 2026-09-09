using System;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.UI;

namespace Hoodrich.Locations
{
    /// <summary>
    /// Franklin's Buffalo, brought up to date.
    ///
    /// HIS CAR IS THE GAME'S, NOT OURS. The white Buffalo S on his drive is a story vehicle
    /// with a story plate, and nothing in this mod put it there -- so it cannot simply be
    /// spawned as something else. What can be done is what a man with money would actually
    /// do: get the same car in the current shape. The STX is the same Bravado Buffalo eight
    /// years on, and swapping one for the other is the only thing in this file.
    ///
    /// SWAPPED WHEN HE GETS IN IT, and only then. A watcher that replaced the car wherever it
    /// stood would be fighting the game every time the story put the original back -- and it
    /// would do it out of sight, so the first anybody knew of it would be a car that had
    /// changed shape while they were indoors. Doing it on the way in means the swap happens
    /// once, in front of you, and repeats itself for free the next time the game respawns the
    /// old one.
    ///
    /// EVERYTHING THAT MADE IT HIS COMES ACROSS: the plate, both colours, the dirt on it and
    /// where it was stood. It keeps the same seat you were getting into.
    /// </summary>
    internal sealed class Buffalo
    {
        /// <summary>What the game gives him, and what it becomes.</summary>
        private const string Was = "buffalo2";
        private const string Now = "buffalo4";

        /// <summary>
        /// The plate the game puts on his.
        ///
        /// THE ONLY THING THAT SAYS WHICH BUFFALO S IS HIS. There are others on the street and
        /// swapping one somebody happened to steal would be this mod editing a stranger's car.
        /// If you have changed his plate, this stops finding it, which is the right way round
        /// for a test that could otherwise be wrong about somebody else's property.
        /// </summary>
        private const string His = "FC1988";

        /// <summary>And what it says once it is his rather than the story's.</summary>
        private const string Says = "HOODRICH";

        /// <summary>
        /// Competition suspension. Type 15, and the index is asked for rather than written
        /// down -- "competition" is the LAST one a model offers and how many it offers is
        /// not the same on every car, so a hardcoded 3 fits the ones with four and silently
        /// does nothing on the rest. The same reasoning as Hao's lot, and the same number.
        /// </summary>
        private const int ModSuspension = 15;

        /// <summary>Dark smoke. 1 is pure black and hides the inside completely; 2 is the one people fit.</summary>
        private const int DarkSmoke = 2;

        /// <summary>How often the street is looked at, and how far.</summary>
        private const int LookEveryMs = 900;
        private const float Reach = 12f;

        /// <summary>Set by Main: off when the mod is standing down.</summary>
        public Func<bool> Busy;

        private int _nextLook;
        private int _swapped;

        public void Update(Ped player)
        {
            if (player == null || !player.Exists() || !player.IsAlive) return;
            if (Busy != null && Busy()) return;

            var now = Game.GameTime;
            if (now < _nextLook) return;

            _nextLook = now + LookEveryMs;

            try
            {
                var car = Near(player);
                if (car == null) return;

                Swap(car, player);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not look at the Buffalo: " + ex.Message);
            }
        }

        /// <summary>
        /// His Buffalo S, if he is stood at one or sat in one.
        ///
        /// Both cases, because a car you have just got into is the ordinary way this happens
        /// and a car you are walking up to is the one that lets the swap finish before you
        /// open the door.
        /// </summary>
        private Vehicle Near(Ped player)
        {
            var inside = player.CurrentVehicle;

            if (inside != null && inside.Exists() && Mine(inside)) return inside;

            foreach (var car in World.GetNearbyVehicles(player, Reach))
            {
                if (car == null || !car.Exists()) continue;
                if (!Mine(car)) continue;

                // Not while somebody is sat in it who is not him.
                if (car.IsSeatFree(VehicleSeat.Driver) || car.Driver == player) return car;
            }

            return null;
        }

        private static bool Mine(Vehicle car)
        {
            try
            {
                if (car.Model != new Model(Was)) return false;

                var plate = Function.Call<string>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT, car.Handle) ?? "";

                return plate.Trim().Equals(His, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>The old one out, the new one in, and everything about it carried across.</summary>
        private void Swap(Vehicle old, Ped player)
        {
            var model = new Model(Now);

            if (!model.IsValid || !model.IsInCdImage)
            {
                // An install without the newer car. Said once and never again, because it is
                // not going to become true later in the session.
                if (_swapped == 0)
                {
                    _swapped = -1;
                    Log.Info("Buffalo: this install has no " + Now + ", so his stays as it is.");
                }

                return;
            }

            if (!model.Request(2000)) return;

            var at = old.Position;
            var heading = old.Heading;

            var driving = player.CurrentVehicle != null && player.CurrentVehicle.Handle == old.Handle;

            int primary = 0, secondary = 0, pearl = 0, wheelColour = 0;
            var dirt = 0f;

            try
            {
                var p = new OutputArgument();
                var s = new OutputArgument();

                Function.Call(Hash.GET_VEHICLE_COLOURS, old.Handle, p, s);

                primary = p.GetResult<int>();
                secondary = s.GetResult<int>();

                var pe = new OutputArgument();
                var wc = new OutputArgument();

                Function.Call(Hash.GET_VEHICLE_EXTRA_COLOURS, old.Handle, pe, wc);

                pearl = pe.GetResult<int>();
                wheelColour = wc.GetResult<int>();

                dirt = old.DirtLevel;
            }
            catch
            {
                // White with clean paint, then.
            }

            try
            {
                old.Delete();
            }
            catch
            {
                // The new one goes in beside it if it will not go.
            }

            var made = World.CreateVehicle(model, at, heading);
            model.MarkAsNoLongerNeeded();

            if (made == null || !made.Exists())
            {
                Log.Info("Buffalo: the STX would not spawn. His old one is gone; sorry about that.");
                return;
            }

            try
            {
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, made.Handle, true, true);

                // NOT THE PLATE IT CAME WITH. FC1988 is the plate the game printed on a car
                // he was given; this is the one a man who owns the block puts on the car he
                // paid for. It is also why the swap only ever happens once per respawn --
                // the new plate no longer answers the test that found the old car.
                Function.Call(Hash.SET_VEHICLE_NUMBER_PLATE_TEXT, made.Handle, Says);

                Function.Call(Hash.SET_VEHICLE_MOD_KIT, made.Handle, 0);

                // DROPPED AND TINTED. The two things anybody does to this car first, and
                // the mod kit has to be on before either will take.
                try
                {
                    var drops = Function.Call<int>(Hash.GET_NUM_VEHICLE_MODS, made.Handle, ModSuspension);

                    if (drops > 0)
                    {
                        Function.Call(Hash.SET_VEHICLE_MOD, made.Handle, ModSuspension, drops - 1, false);
                    }
                }
                catch
                {
                    // It sits at stock height. Still his car.
                }

                Function.Call(Hash.SET_VEHICLE_WINDOW_TINT, made.Handle, DarkSmoke);

                Function.Call(Hash.SET_VEHICLE_COLOURS, made.Handle, primary, secondary);
                Function.Call(Hash.SET_VEHICLE_EXTRA_COLOURS, made.Handle, pearl, wheelColour);
                Function.Call(Hash.SET_VEHICLE_DIRT_LEVEL, made.Handle, dirt);

                Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, made.Handle);
                Function.Call(Hash.SET_VEHICLE_ENGINE_ON, made.Handle, true, true, false);

                // HANDED TO THE GAME, NOT KEPT. It is his car, not the mod's, and a car this
                // holds on to forever is a car the game can never clean up or replace.
                made.IsPersistent = true;
                made.MarkAsNoLongerNeeded();

                if (driving) Function.Call(Hash.SET_PED_INTO_VEHICLE, player.Handle, made.Handle, -1);
            }
            catch (Exception ex)
            {
                Log.Debug("Buffalo: could not dress the new one: " + ex.Message);
            }

            _swapped++;

            Notify.Ticker("~g~that's the STX.~s~  Dropped, tinted, same plate.");

            Log.Info("Buffalo: swapped his " + Was + " for a " + Now + ".");
        }
    }
}
