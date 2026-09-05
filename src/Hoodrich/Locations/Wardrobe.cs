using System;
using System.Collections.Generic;
using System.Globalization;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.State;
using Hoodrich.UI;

namespace Hoodrich.Locations
{
    /// <summary>
    /// The closet in his room at Denise's: stand at it and change.
    ///
    /// THE GAME'S OWN WARDROBE STOPPED WORKING HERE the day the story moved him out, and the
    /// mod moved him back in. So this is one of ours: a prompt at the closet door, a menu
    /// of the slots a person actually dresses in (see WardrobeScreen), and what he settles
    /// on written into the save and put back on him when the game loads -- otherwise he
    /// wakes up in whatever the story last dressed him in.
    /// </summary>
    internal sealed class Wardrobe
    {
        private static readonly Vector3 Closet = new Vector3(-18.438f, -1438.564f, 31.102f);
        private const float UseRange = 1.6f;

        private readonly WardrobeScreen _screen;

        public Wardrobe(WardrobeScreen screen)
        {
            _screen = screen;
        }

        public void Update()
        {
            if (_screen.IsOpen) return;

            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists() || !me.IsAlive || me.IsInVehicle()) return;
                if (me.Position.DistanceTo(Closet) > UseRange) return;

                Help.ShowThisFrame("Press ~INPUT_CONTEXT~ to change.");

                if (!Function.Call<bool>(Hash.IS_CONTROL_JUST_PRESSED, 0, (int)Control.Context)) return;

                _screen.Open();
            }
            catch (Exception ex)
            {
                Log.Debug("The wardrobe could not ask: " + ex.Message);
            }
        }

        // ---- what he wears, kept ----------------------------------------------------

        /// <summary>The slots the closet dresses. Slot 8 is left alone: that is where the balaclava lives (see Mask).</summary>
        public static readonly int[] Components = { 2, 11, 3, 4, 6, 7 };
        public static readonly int[] Props = { 0, 1, 6, 7 };

        /// <summary>
        /// Whether the man in front of the camera is the one whose clothes these are. Drawable
        /// numbers mean nothing across models: Franklin's third jacket is Michael's third
        /// something else, so the record is only ever read off him or put on him.
        /// </summary>
        private static bool Him(Ped me)
        {
            return me != null && me.Exists() && (uint)me.Model.Hash == (uint)PedHash.Franklin;
        }

        /// <summary>Reads what he has on into the record, as "c:slot:drawable:texture" and "p:slot:drawable:texture".</summary>
        public static void Remember(PlayerState state)
        {
            if (state == null) return;

            var me = Game.Player.Character;
            if (!Him(me)) return;

            state.Outfit.Clear();

            try
            {
                foreach (var slot in Components)
                {
                    var d = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, me.Handle, slot);
                    var t = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, me.Handle, slot);
                    state.Outfit.Add("c:" + slot + ":" + d + ":" + t);
                }

                foreach (var slot in Props)
                {
                    var d = Function.Call<int>(Hash.GET_PED_PROP_INDEX, me.Handle, slot);
                    var t = Function.Call<int>(Hash.GET_PED_PROP_TEXTURE_INDEX, me.Handle, slot);
                    state.Outfit.Add("p:" + slot + ":" + d + ":" + t);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not read what he has on: " + ex.Message);
            }

            state.Touch();
        }

        /// <summary>
        /// Puts the record back on him. Nothing recorded means he stays as the game dressed
        /// him. Says whether it is done with -- false while the player is somebody else, so
        /// the caller asks again later rather than never.
        /// </summary>
        public static bool Apply(PlayerState state)
        {
            if (state == null || state.Outfit.Count == 0) return true;

            var me = Game.Player.Character;
            if (!Him(me)) return false;

            var put = 0;

            foreach (var entry in state.Outfit)
            {
                try
                {
                    var bits = entry.Split(':');
                    if (bits.Length != 4) continue;

                    var slot = int.Parse(bits[1], CultureInfo.InvariantCulture);
                    var drawable = int.Parse(bits[2], CultureInfo.InvariantCulture);
                    var texture = int.Parse(bits[3], CultureInfo.InvariantCulture);

                    if (bits[0] == "c")
                    {
                        if (Array.IndexOf(Components, slot) < 0) continue;
                        Function.Call(Hash.SET_PED_COMPONENT_VARIATION, me.Handle, slot, drawable, texture, 0);
                        put++;
                    }
                    else if (bits[0] == "p")
                    {
                        if (Array.IndexOf(Props, slot) < 0) continue;
                        if (drawable < 0) Function.Call(Hash.CLEAR_PED_PROP, me.Handle, slot);
                        else Function.Call(Hash.SET_PED_PROP_INDEX, me.Handle, slot, drawable, texture, true);
                        put++;
                    }
                }
                catch
                {
                }
            }

            if (put > 0) Log.Info("Dressed him from the save: " + put + " slot(s).");
            return true;
        }
    }
}
