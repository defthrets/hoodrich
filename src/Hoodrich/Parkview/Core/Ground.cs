// GENERATED -- DO NOT EDIT. This is Parkview, copied in by tools/sync-parkview.py from
// C:\projects\parkview\src\Parkview\Core\Ground.cs. Change it there; the next build overwrites this.
using GTA.Math;
using GTA.Native;

namespace Hoodrich.Parkview.Core
{
    /// <summary>
    /// Where the ground is under a point.
    ///
    /// THE NATIVE, NOT THE WRAPPER. ScriptHookVDotNet 3.9 has World.GetGroundHeight with an out
    /// parameter and a mode; 3.6 has one that returns the height and nothing to say whether
    /// it found any -- and the mod is built against 3.6 now, so that a 3.6 install, a 3.7
    /// nightly and the 3.9 fork all load the same dll. GET_GROUND_Z_FOR_3D_COORD is what both
    /// wrappers call and has not changed shape since the game shipped, so the twenty-nine
    /// places that asked the wrapper ask this instead and stop caring which build is running.
    ///
    /// It looks DOWN from the point it is given, so a probe from a metre or two above a spot
    /// finds the road under it and a probe from mid-air over a hill finds the hill.
    /// </summary>
    internal static class Ground
    {
        public static bool Probe(Vector3 from, out float z)
        {
            try
            {
                var arg = new OutputArgument();

                var hit = Function.Call<bool>(Hash.GET_GROUND_Z_FOR_3D_COORD,
                                              from.X, from.Y, from.Z, arg, false, false);

                z = hit ? arg.GetResult<float>() : 0f;
                return hit;
            }
            catch
            {
                z = 0f;
                return false;
            }
        }
    }
}
