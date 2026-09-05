using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Locations
{
    /// <summary>
    /// A blip on the map for every place we know roughly where to find.
    ///
    /// These are NOT doors. A door needs two coordinates measured by standing on them and it
    /// takes you inside; this is one coordinate read out of another mod's marker table and it
    /// takes you nowhere. The whole point of it is that the list of a hundred and thirty-five
    /// places is useless as a list -- "Roger's scrapyard, Cypress Flats" is a sentence, and a
    /// blip is a direction.
    ///
    /// A place that has been turned into a door is skipped, because InteriorDoor puts its own
    /// blip there and two at one address on the map is worse than none. So the map thins out
    /// as the work gets done, which is the right way round.
    /// </summary>
    internal sealed class PlaceBlips
    {
        private readonly List<Blip> _blips = new List<Blip>();

        /// <summary>Grey rather than a colour, so a place you have not done reads as pending.</summary>
        private const int Alpha = 150;

        /// <summary>
        /// One blip per place with a coordinate, minus the ones that are already doors.
        ///
        /// Made once. Eighty-odd blips is a lot to hold, and a lot to redraw -- but they are
        /// static map blips rather than anything drawn per frame, so they cost nothing after
        /// the frame that makes them.
        /// </summary>
        public void Show(IEnumerable<string> alreadyDoors)
        {
            Clear();

            var taken = new HashSet<string>();
            foreach (var section in alreadyDoors)
            {
                if (!string.IsNullOrEmpty(section)) taken.Add(section);
            }

            var made = 0;

            foreach (var place in Places.Every())
            {
                if (taken.Contains(place.Key)) continue;
                if (!Spots.Of(place.Key, out var at)) continue;

                try
                {
                    var blip = World.CreateBlip(at);
                    if (blip == null || !blip.Exists()) continue;

                    blip.Sprite = (BlipSprite)place.Sprite;
                    blip.Color = BlipColor.WhiteNotPure;
                    blip.Scale = 0.7f;
                    blip.IsShortRange = true;
                    blip.Name = place.Name;

                    // Map only, not the minimap. Eighty of these on the corner of the screen
                    // would bury the ones that matter -- the dealers, the homies, the drop.
                    Function.Call(Hash.SET_BLIP_DISPLAY, blip.Handle, 3);
                    Function.Call(Hash.SET_BLIP_ALPHA, blip.Handle, Alpha);

                    _blips.Add(blip);
                    made++;
                }
                catch
                {
                    // One blip that would not draw is not worth losing the rest over.
                }
            }

            Log.Info("Places: " + made + " map blips, " + taken.Count +
                     " skipped because they are doors already.");
        }

        /// <summary>Off, and off cleanly. Called on reload and when the setting is turned off.</summary>
        public void Clear()
        {
            foreach (var blip in _blips)
            {
                try
                {
                    if (blip != null && blip.Exists()) blip.Delete();
                }
                catch
                {
                    // Teardown.
                }
            }

            _blips.Clear();
        }

        public void RestoreWorld() => Clear();

        public int Count => _blips.Count;
    }
}
