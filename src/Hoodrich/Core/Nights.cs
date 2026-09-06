using System;
using GTA.Native;

namespace Hoodrich.Core
{
    /// <summary>
    /// Which nights a thing is on.
    ///
    /// A meet that happens every single night is not a meet, it is furniture. So a thing
    /// that is "some nights" rolls once per night -- by the night and by its own name, so
    /// it does not flicker within the night and so two things do not roll together -- and
    /// is simply not there on the nights it loses.
    /// </summary>
    internal static class Nights
    {
        /// <summary>Tonight, as a number: the date, with the small hours counted as the night before.</summary>
        public static int Key()
        {
            try
            {
                var hour = Function.Call<int>(Hash.GET_CLOCK_HOURS);
                var night = Function.Call<int>(Hash.GET_CLOCK_DAY_OF_MONTH) + 31 * Function.Call<int>(Hash.GET_CLOCK_MONTH);
                return hour < 12 ? night - 1 : night;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>Whether the thing called this is on tonight, at that chance in a hundred.</summary>
        public static bool On(string name, int chancePercent)
        {
            if (chancePercent >= 100) return true;
            if (chancePercent <= 0) return false;

            var seed = unchecked(Key() * 1000003 ^ Stable(name));
            return Math.Abs(seed % 100) < chancePercent;
        }

        private static int Stable(string s)
        {
            var h = 17;
            foreach (var c in s) h = unchecked(h * 31 + c);
            return h;
        }
    }
}
