using System;
using System.Drawing;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Economy;
using Hoodrich.UI;

namespace Hoodrich.Locations
{
    /// <summary>
    /// Aunt Denise's place on Forum Drive: the one house you keep product in.
    ///
    /// Property used to be something you shopped for, block by block, at a price scaled by how
    /// developed the turf was. That was a second economy bolted onto a game about a corner, and
    /// it never earned its keep. There is one stash house instead, it is the house the story
    /// already gives you, and it is yours from the start -- so the only decision left is how
    /// much you carry versus how much you leave at home.
    /// </summary>
    internal sealed class StashHouse
    {
        /// <summary>Outside Aunt Denise's, Forum Drive, Davis.</summary>
        private static readonly Vector3 Door = new Vector3(-14.3f, -1438.4f, 31.1f);

        private const float MarkerRange = 60f;
        private const float UseRange = 2.5f;

        private readonly Settings _cfg;

        private Blip _blip;

        public StashHouse(Settings cfg)
        {
            _cfg = cfg;
            Stash = new Stash { Capacity = cfg.HideoutStashCapacity };
        }

        /// <summary>What is being kept here.</summary>
        public Stash Stash { get; }

        public Vector3 Position => Door;

        public string Name => "Aunt Denise's";

        /// <summary>True when the player is close enough to move product in or out.</summary>
        public bool AtDoor
        {
            get
            {
                var player = Game.Player.Character;
                if (player == null || !player.Exists()) return false;

                return player.Position.DistanceTo(Door) <= UseRange;
            }
        }

        public float DistanceTo()
        {
            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return 9999f;

            return player.Position.DistanceTo(Door);
        }

        public void Update()
        {
            if (_blip != null && _blip.Exists()) return;

            try
            {
                _blip = World.CreateBlip(Door);
                if (_blip == null || !_blip.Exists()) return;

                _blip.Sprite = BlipSprite.Safehouse;
                _blip.Color = BlipColor.Green;
                _blip.Name = "Stash house";
                _blip.IsShortRange = true;
                _blip.Scale = 0.85f;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not blip the stash house: " + ex.Message);
            }
        }

        /// <summary>Ground marker, so the door reads as somewhere you can use.</summary>
        public void Draw()
        {
            var distance = DistanceTo();
            if (distance > MarkerRange) return;

            try
            {
                World.DrawMarker(MarkerType.Cylinder, Door, Vector3.Zero, Vector3.Zero,
                                 new Vector3(1f, 1f, 0.8f),
                                 Color.FromArgb(140, 60, 180, 75),
                                 false, false, false, null, null, false);
            }
            catch
            {
                // Cosmetic only.
            }

            if (distance <= UseRange)
            {
                Help.ShowThisFrame("Open the wheel to use the stash house.");
            }
        }

        public void RestoreWorld()
        {
            try { if (_blip != null && _blip.Exists()) _blip.Delete(); }
            catch { /* teardown */ }

            _blip = null;
        }

        public Json ToJson() => Stash.ToJson();

        public void LoadFrom(Json node)
        {
            if (node == null) return;
            Stash.LoadFrom(node);
        }
    }
}
