using System;
using System.Collections.Generic;
using GTA;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Gangs;

namespace Hoodrich.Territory
{
    /// <summary>
    /// Whose block is whose, drawn on the map.
    ///
    /// THE MOD ALREADY KNEW ALL OF THIS AND NEVER SHOWED YOU ANY OF IT. gangs.json says which
    /// zone codes each set holds and zones.json says where each code is and how big it is, and
    /// between them that is a complete map of the city's turf -- but the only way to read it
    /// was to walk into a zone and see the name change in the corner. A player who has not done
    /// that has no idea Davis is Ballas or that the Vagos hold four zones on the east side.
    ///
    /// WHY CIRCLES AND NOT THE SHAPES ON THE PAUSE MAP. There is no native that draws a polygon
    /// on the map. ADD_BLIP_FOR_RADIUS is what the game itself uses for exactly this -- it is
    /// how a mission marks an area -- and a radius blip appears on BOTH the minimap and the
    /// pause map with no extra work and no per-frame drawing. The alternative is drawing over
    /// the pause map in screen space, which means converting world coordinates to a map that
    /// pans and zooms, and there is no native for that either.
    ///
    /// So a zone is a disc. The zones themselves are already stored as a centre and a radius,
    /// so this is not an approximation of the data -- it IS the data, drawn.
    ///
    /// Made once and left alone. These are static map furniture: nothing moves, nothing
    /// changes, and a blip that is recreated on a tick is a blip that flickers.
    /// </summary>
    internal sealed class TurfMap
    {
        /// <summary>
        /// How solid the discs are, out of 255.
        ///
        /// Eighty is enough to read the colour and see the shape while leaving the streets,
        /// blips and route lines under it legible. A turf overlay that hides the road you are
        /// trying to follow is a turf overlay people switch off.
        /// </summary>
        private const int Wash = 80;

        private readonly List<Blip> _discs = new List<Blip>();

        /// <summary>Whether they are up.</summary>
        public bool Showing => _discs.Count > 0;

        /// <summary>
        /// Put them up.
        ///
        /// A zone code a set claims that zones.json has never heard of is logged by name rather
        /// than skipped in silence -- it is a typo in one of two data files and the only way
        /// anybody finds it is being told which code and which set.
        /// </summary>
        public void Show(GangRegistry gangs, ZoneMap zones)
        {
            Hide();

            if (gangs == null || zones == null) return;

            var drawn = 0;
            var missing = 0;

            foreach (var gang in gangs.All)
            {
                if (gang == null || gang.Turf.Count == 0) continue;

                foreach (var code in gang.Turf)
                {
                    if (string.IsNullOrEmpty(code)) continue;

                    var zone = zones.Get(code);

                    if (zone == null)
                    {
                        missing++;
                        Log.Warn("Turf map: " + gang.Name + " claims zone '" + code +
                                 "' and zones.json has no such code.");
                        continue;
                    }

                    try
                    {
                        var blip = World.CreateBlip(zone.Centre, zone.Radius);
                        if (blip == null || !blip.Exists()) continue;

                        // The set's own colour, by number. The Blip class exposes a colour as a
                        // short enum of named colours and gangs.json stores the real palette
                        // index -- going through the enum would round every set to whichever of
                        // the named ones happened to be nearest.
                        Function.Call(Hash.SET_BLIP_COLOUR, blip.Handle, gang.BlipColour);
                        Function.Call(Hash.SET_BLIP_ALPHA, blip.Handle, Wash);

                        // Named, because a radius blip with no name is an unlabelled smear on
                        // the pause map and the whole point is to say whose it is.
                        blip.Name = zone.Name + " -- " + gang.Name;

                        _discs.Add(blip);
                        drawn++;
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("Turf map could not draw " + code + ": " + ex.Message);
                    }
                }
            }

            Log.Info("Turf map: " + drawn + " zone(s) shaded" +
                     (missing > 0 ? ", " + missing + " claimed code(s) not in zones.json." : "."));
        }

        /// <summary>Take them down.</summary>
        public void Hide()
        {
            foreach (var blip in _discs)
            {
                try { if (blip != null && blip.Exists()) blip.Delete(); }
                catch { /* going anyway */ }
            }

            _discs.Clear();
        }
    }
}
