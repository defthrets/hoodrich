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
    /// So the division of labour is this. Places.cs holds the NAMES, every one of them read
    /// out of the interior loader's own string table. This holds no names and no numbers at
    /// all -- it takes the place somebody picked, takes where they are standing, and puts the
    /// two together in the ini. Neither half can be guessed into being wrong.
    /// </summary>
    internal static class DoorMaker
    {
        /// <summary>The place whose outside was marked last, so the inside can be checked against it.</summary>
        private static string _open = "";

        /// <summary>
        /// The outside of a door: where it is, what it is called, and what has to load for it.
        /// </summary>
        public static bool Outside(Place place, Vector3 at, float heading)
        {
            if (place == null) return false;

            try
            {
                var section = place.Key;

                Settings.Put(section, "Name", place.Name);
                Settings.Put(section, "Blip", "true");
                Settings.Put(section, "Sprite", place.Sprite.ToString(CultureInfo.InvariantCulture));

                // ONE NAME IN Ipl AND THE REST IN Extra, because the door asks for every name
                // in both but watches only Ipl to decide whether the room ever arrived. A
                // semicolon list in Ipl would make that check ask about a name that does not
                // exist and report a working room as broken.
                var names = place.Ipl.Split(';');

                Settings.Put(section, "Ipl", names.Length > 0 ? names[0].Trim() : "");
                Settings.Put(section, "Extra", names.Length > 1
                    ? string.Join(";", names, 1, names.Length - 1)
                    : "");

                Settings.Put(section, "DoorX", N(at.X));
                Settings.Put(section, "DoorY", N(at.Y));
                Settings.Put(section, "DoorZ", N(at.Z));
                Settings.Put(section, "DoorHeading", N(heading));

                List(section);

                _open = section;

                Log.Info("Door " + section + " (" + place.Name + "): outside at " + at +
                         ", heading " + N(heading) + ", ipl " + place.Ipl);

                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("Could not write a door: " + ex.Message);
                return false;
            }
        }

        /// <summary>Where he arrives on the other side of it.</summary>
        public static bool Inside(Place place, Vector3 at, float heading)
        {
            if (place == null) return false;

            try
            {
                Settings.Put(place.Key, "InsideX", N(at.X));
                Settings.Put(place.Key, "InsideY", N(at.Y));
                Settings.Put(place.Key, "InsideZ", N(at.Z));
                Settings.Put(place.Key, "InsideHeading", N(heading));

                // Listed here as well as in Outside. Somebody who marks an inside for a place
                // whose outside was done in an earlier session would otherwise finish a door
                // that never gets read.
                List(place.Key);

                Log.Info("Door " + place.Key + ": inside at " + at + ", heading " + N(heading));

                _open = "";

                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("Could not finish a door: " + ex.Message);
                return false;
            }
        }

        /// <summary>Whether the outside marked last was this one. Only used to word the message.</summary>
        public static bool Started(Place place)
        {
            return place != null && _open == place.Key;
        }

        /// <summary>
        /// Named in [Doors] More, or the loader never looks at the section.
        ///
        /// Checked against the list first: pressing the row twice for one place used to leave
        /// the name in there twice, which loads the same door twice and puts two blips on the
        /// map at one address.
        /// </summary>
        private static void List(string section)
        {
            var more = Settings.Read("Doors", "More", "").Trim();

            foreach (var one in more.Split(','))
            {
                if (string.Equals(one.Trim(), section, StringComparison.OrdinalIgnoreCase)) return;
            }

            Settings.Put("Doors", "More", more.Length == 0 ? section : more + "," + section);
        }

        private static string N(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
