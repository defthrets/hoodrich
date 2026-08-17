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
    /// The counter in Aunt Denise's kitchen: the only place product gets worked.
    ///
    /// Bagging up used to be something you did anywhere, mid-street, from a menu. That made the
    /// stash house a locker rather than a base and made the whole prep step feel like paperwork.
    /// Tying it to one counter gives the house a job: you buy weight out there, you bring it
    /// home, and you come back out with something you can sell.
    /// </summary>
    internal sealed class Kitchen
    {
        /// <summary>The worktop, Aunt Denise's kitchen.</summary>
        private static readonly Vector3 Counter = new Vector3(-11.415f, -1428.046f, 31.101f);

        private const float MarkerRange = 20f;
        private const float UseRange = 1.8f;

        private readonly Action _open;
        private readonly Func<bool> _busy;

        public Kitchen(Action open, Func<bool> busy)
        {
            _open = open;
            _busy = busy;
        }

        public float DistanceTo()
        {
            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return 9999f;

            return player.Position.DistanceTo(Counter);
        }

        public bool InReach => DistanceTo() <= UseRange;

        public void Update()
        {
            if (!InReach) return;
            if (_busy != null && _busy()) return;

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return;
            if (player.IsInVehicle()) return;

            Help.ShowThisFrame("Press ~INPUT_CONTEXT~ to work the product.");

            if (!Function.Call<bool>(Hash.IS_CONTROL_JUST_PRESSED, 0, (int)Control.Context)) return;

            _open?.Invoke();
        }

        /// <summary>A small marker on the worktop, only once you are in the room.</summary>
        public void Draw()
        {
            if (DistanceTo() > MarkerRange) return;
            if (_busy != null && _busy()) return;

            try
            {
                World.DrawMarker(MarkerType.Cylinder, Counter - new Vector3(0f, 0f, 0.95f),
                                 Vector3.Zero, Vector3.Zero,
                                 new Vector3(0.5f, 0.5f, 0.3f),
                                 Color.FromArgb(120, 126, 190, 79),
                                 false, false, false, null, null, false);
            }
            catch
            {
                // Cosmetic only.
            }
        }
    }
}
