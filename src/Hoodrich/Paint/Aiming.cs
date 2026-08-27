using GTA;
using GTA.Native;

namespace Hoodrich.Paint
{
    /// <summary>
    /// Whether he is looking down the sights rather than merely holding it.
    ///
    /// IN THE ENGINE RATHER THAN IN THE UI, small as it is, because both mods need the answer
    /// and neither of them is asking a question about drawing. It decides whether he turns to
    /// face the aim as well as whether a reticle is worth showing, so a copy living next to one
    /// mod's HUD would be a copy the other mod has to make.
    /// </summary>
    internal static class Aiming
    {
        public static bool Now()
        {
            try
            {
                return Function.Call<bool>(Hash.IS_PLAYER_FREE_AIMING, Game.Player.Handle);
            }
            catch
            {
                return false;
            }
        }
    }
}
