using System;
using GTA;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.UI
{
    /// <summary>
    /// Clears up map markers left behind by an older version of this mod.
    ///
    /// There used to be a TurfBlips class that put a gang skull on the middle of every set's
    /// territory. It is gone from the source, but a blip is not owned by the code that made it
    /// -- it belongs to the game, and reloading a script does not take its markers with it. So
    /// anybody who has had this mod running across a rebuild is left with skulls sat on the map
    /// that nothing in the current build knows about, cannot explain, and will not remove. They
    /// survive every reload and only a full restart of the game clears them.
    ///
    /// Sprite 84 and nothing else. That is the exact one the old class used, it is not used
    /// anywhere in this mod any more, and single-player has no gang attacks of its own to put
    /// one on the map -- so a skull on the map is ours, from before, and can go.
    ///
    /// Once, at startup. This is a mess we made and stopped making, not a thing to keep
    /// sweeping for: a player who deliberately marks something with a skull from another mod
    /// after we have loaded should keep it.
    /// </summary>
    internal static class StaleBlips
    {
        /// <summary>The gang skull the old turf markers were drawn with.</summary>
        private const int OldTurfSkull = 84;

        public static void Sweep()
        {
            var gone = 0;

            try
            {
                foreach (var blip in World.GetAllBlips())
                {
                    if (blip == null || !blip.Exists()) continue;

                    if (Function.Call<int>(Hash.GET_BLIP_SPRITE, blip.Handle) != OldTurfSkull)
                    {
                        continue;
                    }

                    blip.Delete();
                    gone++;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not sweep old turf blips: " + ex.Message);
                return;
            }

            if (gone == 0) return;

            Log.Info("Cleared " + gone + " turf skull" + (gone == 1 ? "" : "s") +
                     " left on the map by an older build.");
        }
    }
}
