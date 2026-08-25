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

        /// <summary>
        /// radar_dead, which this mod uses for one gang leader.
        ///
        /// Swept as well, and only here at startup, because our own leader marks have not been
        /// made yet when this runs -- so a skull already on the map at this moment is one that
        /// outlived a build rather than one we are about to draw.
        /// </summary>
        private const int LeaderSkull = 274;

        /// <summary>
        /// Enough of the map to find one marker in, without writing out the whole atlas.
        ///
        /// A save with every shop, safehouse and activity marked runs to a couple of hundred
        /// blips, and the sprite-1 ones are the game's own furniture. Anything else is either
        /// a mod's or ours, which is exactly the list somebody hunting a stray skull wants.
        /// </summary>
        private const int DumpCap = 80;

        public static void Sweep()
        {
            var gone = 0;

            try
            {
                Dump();

                foreach (var blip in World.GetAllBlips())
                {
                    if (blip == null || !blip.Exists()) continue;

                    var sprite = Function.Call<int>(Hash.GET_BLIP_SPRITE, blip.Handle);

                    if (sprite != OldTurfSkull && sprite != LeaderSkull) continue;

                    Log.Info("Clearing a stale skull (sprite " + sprite + ") at " +
                             blip.Position + ".");

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

            Log.Info("Cleared " + gone + " stale skull" + (gone == 1 ? "" : "s") +
                     " left on the map by an older build.");
        }

        /// <summary>
        /// Writes down what is actually on the map, once, before anything is touched.
        ///
        /// Put here because "there is an orange skull at this spot and it will not go away" is
        /// not something anybody can act on. A sprite number and a colour number is. Every
        /// marker that is not the game's own plain dot gets a line, and then whatever is
        /// sitting there can be looked up rather than guessed at -- including working out that
        /// it belongs to another mod entirely, which is a real answer and a quick one.
        /// </summary>
        private static void Dump()
        {
            var seen = 0;

            try
            {
                foreach (var blip in World.GetAllBlips())
                {
                    if (blip == null || !blip.Exists()) continue;

                    var sprite = Function.Call<int>(Hash.GET_BLIP_SPRITE, blip.Handle);

                    // 1 is the plain dot the game marks half the map with, and 8 is the
                    // waypoint. Neither is anybody's mystery.
                    if (sprite <= 1 || sprite == 8) continue;

                    if (seen >= DumpCap)
                    {
                        Log.Info("  ... and more, past the " + DumpCap + " listed.");
                        return;
                    }

                    seen++;

                    var colour = Function.Call<int>(Hash.GET_BLIP_COLOUR, blip.Handle);
                    var pos = blip.Position;

                    Log.Info("  blip sprite " + sprite + " colour " + colour + " at " +
                             pos.X.ToString("0.0") + ", " + pos.Y.ToString("0.0") + ", " +
                             pos.Z.ToString("0.0"));
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not list the map's blips: " + ex.Message);
                return;
            }

            if (seen == 0) return;

            Log.Info("Map had " + seen + " marked blip" + (seen == 1 ? "" : "s") +
                     " on it before this mod drew any of its own.");
        }
    }
}
