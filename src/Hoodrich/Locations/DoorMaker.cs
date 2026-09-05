using System;
using System.Globalization;
using GTA.Math;
using Hoodrich.Core;

namespace Hoodrich.Locations
{
    /// <summary>
    /// Writing a door into Hoodrich.ini from where somebody is standing.
    ///
    /// EVERY COORDINATE IN THIS MOD THAT WAS TYPED RATHER THAN MEASURED HAS BEEN WRONG. The
    /// grow room cost four rounds of it: the coordinate was blamed, then the IPL name, then
    /// the code, and the answer turned out to be that nobody could see the room from here at
    /// all. A number read off a player standing on the spot cannot be wrong in that way.
    ///
    /// So this is the way doors get added from now on. Stand at the entrance, press the row;
    /// stand inside, press the other. What comes out is a section of ini identical in shape
    /// to the two the mod ships, which InteriorDoor then treats identically -- there is no
    /// second class of door and no code to write for a new one.
    /// </summary>
    internal static class DoorMaker
    {
        /// <summary>The section being filled in, between the two presses.</summary>
        private static string _open = "";

        /// <summary>
        /// The outside of a new door. Picks the next free name, writes the coordinate, and
        /// adds the name to the list the loader reads.
        /// </summary>
        public static string Outside(Vector3 at, float heading)
        {
            try
            {
                var section = Next();

                Settings.Put(section, "Name", section);
                Settings.Put(section, "Blip", "true");
                Settings.Put(section, "DoorX", N(at.X));
                Settings.Put(section, "DoorY", N(at.Y));
                Settings.Put(section, "DoorZ", N(at.Z));
                Settings.Put(section, "DoorHeading", N(heading));

                // Listed, or the loader never looks at the section.
                var more = Settings.Read("Doors", "More", "");
                var list = more.Trim().Length == 0 ? section : more.Trim() + "," + section;

                Settings.Put("Doors", "More", list);

                _open = section;

                Log.Info("Door " + section + ": outside at " + at + ", heading " + N(heading) + ".");

                return section;
            }
            catch (Exception ex)
            {
                Log.Warn("Could not write a door: " + ex.Message);
                return "";
            }
        }

        /// <summary>The inside of the door most recently started. Nothing without one.</summary>
        public static string Inside(Vector3 at, float heading)
        {
            if (_open.Length == 0) return "";

            try
            {
                Settings.Put(_open, "InsideX", N(at.X));
                Settings.Put(_open, "InsideY", N(at.Y));
                Settings.Put(_open, "InsideZ", N(at.Z));
                Settings.Put(_open, "InsideHeading", N(heading));

                Log.Info("Door " + _open + ": inside at " + at + ", heading " + N(heading) + ".");

                var done = _open;
                _open = "";

                return done;
            }
            catch (Exception ex)
            {
                Log.Warn("Could not finish a door: " + ex.Message);
                return "";
            }
        }

        /// <summary>
        /// The next name nobody has used: Door1, Door2, and so on.
        ///
        /// Read back out of the ini rather than counted in memory, so a name is not reused
        /// after a reload and one session cannot quietly overwrite the doors of the last.
        /// </summary>
        private static string Next()
        {
            var more = Settings.Read("Doors", "More", "");

            for (var i = 1; i < 200; i++)
            {
                var name = "Door" + i.ToString(CultureInfo.InvariantCulture);

                if (more.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) continue;

                return name;
            }

            return "Door";
        }

        private static string N(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
