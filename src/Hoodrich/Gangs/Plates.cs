using System;
using System.Text;
using GTA;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Gangs
{
    /// <summary>
    /// What it says on a set's plates.
    ///
    /// EVERY CAR A SET PUTS ON THE ROAD SAYS WHOSE IT IS, the way the paint already does: the
    /// rollers, the lowriders, the war cars, the ones that come looking for you, the meet.
    /// The words live in gangs.json beside the colours, so a set is one entry wherever it is
    /// described; a set without any gets its tag with something after it, which is what
    /// somebody with a tag and no imagination would order.
    ///
    /// EIGHT CHARACTERS, CAPITALS, DIGITS AND SPACES: the plate's own alphabet. Anything else
    /// the game prints as a blank, so it is cleaned here rather than trusted from the file.
    /// Never the owned cars' HR-and-six-digits shape, which is how OwnedCars finds them
    /// again -- nothing in the file is that shape and nothing made here can be.
    /// </summary>
    internal static class Plates
    {
        /// <summary>Set by Main once the gangs are loaded, for the places that only know a set by its id.</summary>
        public static GangRegistry Registry;

        private const int Most = 8;

        private static readonly string[] Ends = { "4LYF", "4L", "GANG", "1", "4EVA", "RIDE" };

        /// <summary>One for this set, at random, or null for no set.</summary>
        public static string Pick(GangDef gang, Random rng)
        {
            if (gang == null || rng == null) return null;

            if (gang.Plates.Count > 0)
            {
                var pick = Clean(gang.Plates[rng.Next(gang.Plates.Count)]);
                if (pick.Length > 0) return pick;
            }

            var tag = Clean(gang.Tag);
            if (tag.Length == 0) tag = Clean(gang.Id);
            if (tag.Length == 0) return null;

            return Clean(tag + Ends[rng.Next(Ends.Length)]);
        }

        /// <summary>The same, by id, through the registry Main handed over. Null if either is missing.</summary>
        public static string Pick(string gangId, Random rng)
        {
            return Registry == null || string.IsNullOrEmpty(gangId) ? null : Pick(Registry.Get(gangId), rng);
        }

        /// <summary>Puts one on, if the car is real and the set is known. Says whether it did.</summary>
        public static bool Stamp(Vehicle car, GangDef gang, Random rng)
        {
            if (car == null || !car.Exists()) return false;

            var plate = Pick(gang, rng);
            if (string.IsNullOrEmpty(plate)) return false;

            try
            {
                Function.Call(Hash.SET_VEHICLE_NUMBER_PLATE_TEXT, car.Handle, plate);
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not plate a " + gang.Id + " car: " + ex.Message);
                return false;
            }
        }

        /// <summary>Capitals, digits and spaces, eight at most, no leading or trailing space.</summary>
        public static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";

            var sb = new StringBuilder();

            foreach (var c in s.ToUpperInvariant())
            {
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == ' ') sb.Append(c);
                if (sb.Length >= Most) break;
            }

            return sb.ToString().Trim();
        }
    }
}
