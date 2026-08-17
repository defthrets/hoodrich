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
        /// <summary>Aunt Denise's, Forum Drive, Davis.</summary>
        private static readonly Vector3 House = new Vector3(-14.3f, -1438.4f, 31.1f);

        /// <summary>
        /// Anywhere in or around the house counts.
        ///
        /// A two-metre door point meant standing INSIDE put you out of range, because the
        /// interior sits several metres off the doorstep. A radius covers the yard, the porch
        /// and every room without needing to know where the game hides the interior.
        /// </summary>
        private const float UseRange = 14f;

        /// <summary>How close somebody has to be for their shouting to be our problem.</summary>
        private const float QuietRange = 22f;

        private readonly Settings _cfg;

        private Blip _blip;

        public StashHouse(Settings cfg)
        {
            _cfg = cfg;
            Stash = new Stash { Capacity = cfg.HideoutStashCapacity };
        }

        /// <summary>What is being kept here.</summary>
        public Stash Stash { get; }

        public Vector3 Position => House;

        public string Name => "Aunt Denise's";

        /// <summary>True when the player is close enough to move product in or out.</summary>
        public bool AtDoor => DistanceTo() <= UseRange;

        public float DistanceTo()
        {
            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return 9999f;

            return player.Position.DistanceTo(House);
        }

        /// <summary>True on the frame the player crosses into the house.</summary>
        private bool _inside;

        public void Update()
        {
            EnsureBlip();
            Hush();

            var here = AtDoor;
            if (here == _inside) return;

            _inside = here;

            // Told once on the way in, rather than a marker on the floor. It is a house, not a
            // pickup: standing in the right two metres should not be part of using it.
            if (_inside)
            {
                Notify.Ticker("~g~You are at the stash house.~s~ Open your inventory to move product in or out.");
            }
        }

        private void EnsureBlip()
        {
            if (_blip != null && _blip.Exists()) return;

            try
            {
                _blip = World.CreateBlip(House);
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

        /// <summary>
        /// Keeps the house quiet.
        ///
        /// Denise has a lot to say and says all of it at volume, which is fine for a story
        /// mission and wearing when the house is somewhere you come back to every time your
        /// pockets fill up. Her ambient lines are cut off as they start; everything else about
        /// her is untouched.
        /// </summary>
        private void Hush()
        {
            if (!AtDoor) return;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            try
            {
                foreach (var ped in World.GetNearbyPeds(player, QuietRange))
                {
                    if (ped == null || !ped.Exists() || ped.Handle == player.Handle) continue;

                    Function.Call(Hash.STOP_CURRENT_PLAYING_AMBIENT_SPEECH, ped.Handle);
                    Function.Call(Hash.DISABLE_PED_PAIN_AUDIO, ped.Handle, true);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not quieten the house: " + ex.Message);
            }
        }

        /// <summary>Nothing is drawn at the house. It is a building, not a checkpoint.</summary>
        public void Draw()
        {
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
