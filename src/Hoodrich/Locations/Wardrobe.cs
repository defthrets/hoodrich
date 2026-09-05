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

        /// <summary>Every component and prop slot the game has. See WardrobeScreen for what each is.</summary>
        public static readonly int[] Components = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
        public static readonly int[] Props = { 0, 1, 2, 6, 7 };

        /// <summary>
        /// Whether there is a body in front of the camera worth writing down.
        ///
        /// Anything the player can be. It used to be Franklin and only Franklin, which was
        /// right while he was the only body the closet could dress -- now that the rail can put
        /// him in the online models, refusing to record those would mean an hour of choosing
        /// that survives until the next load and no longer.
        /// </summary>
        private static bool Him(Ped me)
        {
            return me != null && me.Exists() && me.Model.IsPed;
        }

        /// <summary>Reads what he has on into the record, as "c:slot:drawable:texture" and "p:slot:drawable:texture".</summary>
        public static void Remember(PlayerState state)
        {
            if (state == null) return;

            var me = Game.Player.Character;
            if (!Him(me)) return;

            // FILED UNDER THE BODY IT WAS WORN ON, because a drawable number is an index into
            // one model's wardrobe and means something else entirely on another. Putting the
            // online man's forty-first jacket on Franklin gets whatever is forty-first on him,
            // or nothing. So the rows carry the model, only rows for the body he is in are
            // ever put back on, and choosing a look for each body keeps each of them.
            var body = unchecked((uint)me.Model.Hash).ToString("X8");

            state.Outfit.RemoveAll(row => row.StartsWith(body + "|", StringComparison.Ordinal));

            try
            {
                foreach (var slot in Components)
                {
                    var d = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, me.Handle, slot);
                    var t = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, me.Handle, slot);
                    state.Outfit.Add(body + "|c:" + slot + ":" + d + ":" + t);
                }

                foreach (var slot in Props)
                {
                    var d = Function.Call<int>(Hash.GET_PED_PROP_INDEX, me.Handle, slot);
                    var t = Function.Call<int>(Hash.GET_PED_PROP_TEXTURE_INDEX, me.Handle, slot);
                    state.Outfit.Add(body + "|p:" + slot + ":" + d + ":" + t);
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
            var body = unchecked((uint)me.Model.Hash).ToString("X8");

            foreach (var row in state.Outfit)
            {
                try
                {
                    var entry = row;

                    // Rows from before this was written down carry no body. They were all
                    // Franklin's, because he was the only one the closet could dress.
                    var bar = entry.IndexOf('|');

                    if (bar >= 0)
                    {
                        if (string.Compare(entry.Substring(0, bar), body, StringComparison.OrdinalIgnoreCase) != 0) continue;
                        entry = entry.Substring(bar + 1);
                    }
                    else if ((uint)me.Model.Hash != (uint)PedHash.Franklin)
                    {
                        continue;
                    }

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
