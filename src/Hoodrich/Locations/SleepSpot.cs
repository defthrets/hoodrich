using System;
using System.Drawing;
using Control = GTA.Control;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.UI;

namespace Hoodrich.Locations
{
    /// <summary>
    /// The bed at Aunt Denise's: sleep, save, and lose six hours.
    ///
    /// Every timed thing in the mod runs on the game clock -- dealers keep hours, the corner
    /// cools off, Uncle Dee is gone before dawn. Without a way to move the clock you either
    /// stand about waiting or you never see half of it, so the bed is the mod's clock control
    /// as much as it is a save point.
    /// </summary>
    internal sealed class SleepSpot
    {
        /// <summary>Franklin's old room, Aunt Denise's house.</summary>
        private static readonly Vector3 Bed = new Vector3(-17.935f, -1440.589f, 31.102f);

        private const float MarkerRange = 25f;
        private const float UseRange = 1.6f;

        /// <summary>Hours passed per sleep.</summary>
        private const int HoursSlept = 6;

        /// <summary>Long enough to read as sleeping, short enough not to be a wait.</summary>
        private const int FadeMs = 900;

        /// <summary>Blocks a second sleep while the first is still fading.</summary>
        private bool _sleeping;

        private readonly Action _save;

        public SleepSpot(Action save)
        {
            _save = save;
        }

        public float DistanceTo()
        {
            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return 9999f;

            return player.Position.DistanceTo(Bed);
        }

        public bool InReach => DistanceTo() <= UseRange;

        public void Update()
        {
            if (_sleeping || !InReach) return;

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return;
            if (player.IsInVehicle()) return;

            Help.ShowThisFrame("Press ~INPUT_CONTEXT~ to sleep. Six hours pass and the game saves.");

            if (!Function.Call<bool>(Hash.IS_CONTROL_JUST_PRESSED, 0, (int)Control.Context)) return;

            Sleep(player);
        }

        /// <summary>
        /// Fades out, moves the clock on, saves, and fades back.
        ///
        /// The fade is what sells it -- six hours passing in a hard cut reads as a bug. Health
        /// comes back too, because a bed that does not heal you is a bed nobody uses twice.
        /// </summary>
        private void Sleep(Ped player)
        {
            _sleeping = true;

            try
            {
                Function.Call(Hash.DO_SCREEN_FADE_OUT, FadeMs);
                Script.Wait(FadeMs + 200);

                Function.Call(Hash.ADD_TO_CLOCK_TIME, HoursSlept, 0, 0);

                player.Health = player.MaxHealth;

                _save?.Invoke();

                // The autosave is the game's own, so a Hoodrich save and a GTA save agree.
                try { Function.Call(Hash.DO_AUTO_SAVE); }
                catch { /* not fatal; the mod's own save has already happened */ }

                Script.Wait(400);

                Function.Call(Hash.DO_SCREEN_FADE_IN, FadeMs);

                Notify.Important("~g~Slept.~s~ " + HoursSlept + " hours gone, and the game is saved.");
                Log.Info("Slept " + HoursSlept + "h at the stash house.");
            }
            catch (Exception ex)
            {
                Log.Error("Sleeping failed.", ex);

                // Never leave the screen black because of our own bug.
                try { Function.Call(Hash.DO_SCREEN_FADE_IN, 200); } catch { }
            }
            finally
            {
                _sleeping = false;
            }
        }

        /// <summary>A quiet marker on the floor by the bed, only close up.</summary>
        public void Draw()
        {
            if (DistanceTo() > MarkerRange) return;

            try
            {
                World.DrawMarker(MarkerType.Cylinder, Bed - new Vector3(0f, 0f, 0.95f),
                                 Vector3.Zero, Vector3.Zero,
                                 new Vector3(0.6f, 0.6f, 0.35f),
                                 Color.FromArgb(120, 60, 180, 75),
                                 false, false, false, null, null, false);
            }
            catch
            {
                // Cosmetic only.
            }
        }
    }
}
