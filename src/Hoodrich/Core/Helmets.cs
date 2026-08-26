using System;
using GTA;
using GTA.Native;

namespace Hoodrich.Core
{
    /// <summary>
    /// Nobody in this mod puts a crash helmet on.
    ///
    /// The game equips one automatically the moment a ped sits on a motorbike, and it is the
    /// single most out-of-place thing that can happen to this mod's cast. Franklin riding a
    /// Sanchez down an alley in a full-face lid does not read as a man from Chamberlain doing
    /// anything; it reads as a road safety advert. The same goes double for the set out on
    /// their own blocks -- four men in matching helmets is a cycling club.
    ///
    /// Two calls, because they answer different questions. SET_PED_HELMET is permission: turn
    /// it off and the game stops REACHING for one. REMOVE_PED_HELMET is the one already on his
    /// head, which the permission flag has nothing to say about -- so a man who mounted before
    /// anybody told him keeps wearing it until he is asked to take it off.
    ///
    /// Deliberately says nothing about the police. A traffic officer on a bike wears a helmet
    /// because a traffic officer on a bike wears a helmet, and this is not applied to anybody
    /// the mod did not put there.
    /// </summary>
    internal static class Helmets
    {
        /// <summary>Takes the helmet off, and stops the game handing him another.</summary>
        public static void Off(Ped ped)
        {
            if (ped == null || !ped.Exists()) return;

            try
            {
                Function.Call(Hash.SET_PED_HELMET, ped.Handle, false);

                // Asked before it is taken, because REMOVE_PED_HELMET on a bare head is a
                // wasted call every frame on the player -- and this runs on the player every
                // frame.
                if (Function.Call<bool>(Hash.IS_PED_WEARING_HELMET, ped.Handle))
                {
                    Function.Call(Hash.REMOVE_PED_HELMET, ped.Handle, true);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not take a helmet off: " + ex.Message);
            }
        }
    }
}
