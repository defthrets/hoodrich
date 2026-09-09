using System;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.UI;

namespace Hoodrich.Locations
{
    /// <summary>
    /// The mask shop on Vespucci Beach.
    ///
    /// THE SHOP IS ALREADY THERE. Online has a mask counter on the boardwalk and the building
    /// is in single player too -- it is just shut, the way half the shops in this city are
    /// shut when the story is not using them. So this is a door on the front of it rather than
    /// a new place: stand where the counter is and somebody serves you.
    ///
    /// A BLIP, BECAUSE A SHOP NOBODY CAN FIND IS NOT A SHOP. The game's own mask sprite, so it
    /// reads as what it is on a map already full of icons, and short range -- you are told
    /// where it is when you are near enough for that to be a decision, not from across the
    /// map. It is not the mod announcing itself; it is the shop being open.
    ///
    /// What it sells is whatever the body you walked in with can wear. See UI.MaskScreen.
    /// </summary>
    internal sealed class MaskShop
    {
        /// <summary>
        /// The counter on the boardwalk.
        ///
        /// THE POSITION IS THE ONLINE STORE'S. If it turns out to be a metre inside a wall,
        /// that is one number to change and it is this one.
        /// </summary>
        private static readonly Vector3 Counter = new Vector3(-1338.0f, -1278.0f, 4.9f);

        private const float UseRange = 2.4f;

        /// <summary>The game's own mask-shop sprite.</summary>
        private const int MaskSprite = 362;

        private readonly MaskScreen _screen;

        private Blip _blip;

        /// <summary>Set by Main: off while something louder is happening.</summary>
        public Func<bool> Busy;

        public MaskShop(MaskScreen screen)
        {
            _screen = screen;
        }

        public void Mark()
        {
            try
            {
                if (_blip != null && _blip.Exists()) return;

                _blip = World.CreateBlip(Counter);
                if (_blip == null || !_blip.Exists()) return;

                Function.Call(Hash.SET_BLIP_SPRITE, _blip.Handle, MaskSprite);

                _blip.Color = BlipColor.White;
                _blip.Scale = 0.8f;
                _blip.Name = "Masks";

                Function.Call(Hash.SET_BLIP_AS_SHORT_RANGE, _blip.Handle, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not put the mask shop on the map: " + ex.Message);
            }
        }

        public void Update()
        {
            if (_screen == null || _screen.IsOpen) return;

            try
            {
                if (Busy != null && Busy()) return;

                var me = Game.Player.Character;
                if (me == null || !me.Exists() || !me.IsAlive || me.IsInVehicle()) return;
                if (me.Position.DistanceTo(Counter) > UseRange) return;

                Help.ShowThisFrame("Press ~INPUT_CONTEXT~ to look at the masks.");

                if (!Function.Call<bool>(Hash.IS_CONTROL_JUST_PRESSED, 0, (int)Control.Context)) return;

                _screen.Open();
            }
            catch (Exception ex)
            {
                Log.Debug("The mask shop could not ask: " + ex.Message);
            }
        }

        public void Clear()
        {
            try
            {
                if (_blip != null && _blip.Exists()) _blip.Delete();
            }
            catch
            {
                // Nothing else to do about a blip.
            }

            _blip = null;
        }
    }
}
