using System;
using GTA;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Locations
{
    /// <summary>
    /// The online city, in the story game, all the time.
    ///
    /// Los Santos has two maps: the story one and the online one, and everything added since
    /// the game came out -- the Diamond Casino, the auto shops, the agencies, the bail
    /// offices, the Freakshop -- is on the online one. The story game never switches to it,
    /// so from the street those are closed shells, building sites or nothing at all. The
    /// doors were switching the map on their way in and back on their way out, which meant
    /// the casino stood there as a building site until the moment you walked into it.
    ///
    /// With this on the map is switched once, at the start, and left there. The outsides
    /// that are placements in their own right -- the shop fronts, the office fronts, the
    /// casino's doors and windows -- are asked for as well, so the places on the list look
    /// like places before you get to them. The interiors are still the doors' business.
    ///
    /// EVERY NAME HERE CAME OUT OF THE INTERIOR LOADER'S STRING TABLE. Not one was typed.
    /// The nightclub fronts are NOT here on purpose: the game has ten club exteriors and one
    /// set of barrier placements per club that decides which of the ten is open, and
    /// loading all ten at once is ten answers to one question. Each club's own door asks
    /// for its own when you make it.
    ///
    /// The map is checked every so often rather than trusted. A save loading, a mission
    /// ending or another mod can put the story map back, and a casino that is a building
    /// site again with no explanation is the exact thing this exists to stop.
    /// </summary>
    internal static class OnlineMap
    {
        /// <summary>The outsides worth having from the street. See the class note for what is not here.</summary>
        private static readonly string[] Outsides =
        {
            // The casino: its doors, its glass, the plant on the roof.
            "hei_dlc_casino_door",
            "hei_dlc_windows_casino",
            "hei_dlc_casino_aircon",

            // All five auto shop fronts.
            "tr_tuner_shop_burton",
            "tr_tuner_shop_mesa",
            "tr_tuner_shop_mission",
            "tr_tuner_shop_rancho",
            "tr_tuner_shop_strawberry",

            // All four agency fronts.
            "sf_fixeroffice_bh1_05",
            "sf_fixeroffice_hw1_08",
            "sf_fixeroffice_kt1_05",
            "sf_fixeroffice_kt1_08",

            // All five bail offices.
            "m24_1_bailoffice_davis",
            "m24_1_bailoffice_delperro",
            "m24_1_bailoffice_missionrow",
            "m24_1_bailoffice_paletobay",
            "m24_1_bailoffice_vinewood",

            // The Freakshop, the cargo ship at the docks, and the Kortz Center's additions.
            "xm3_warehouse",
            "m23_2_cargoship",
            "m26_1_mp2026_01_additions_exterior"
        };

        /// <summary>The one asked about when checking the map is still ours.</summary>
        private const string Canary = "tr_tuner_shop_strawberry";

        private const int CheckMs = 15000;

        private static bool _on;
        private static int _checkedAt;

        /// <summary>Whether the city is being held on the online map.</summary>
        public static bool On => _on;

        public static void Set(bool on)
        {
            if (on) Apply();
            else Restore();
        }

        /// <summary>Switches the map and asks for the outsides. Safe to call again.</summary>
        public static void Apply()
        {
            try
            {
                Function.Call(Hash.ON_ENTER_MP);

                foreach (var name in Outsides)
                {
                    Function.Call(Hash.REQUEST_IPL, name);
                }

                _on = true;
                _checkedAt = Game.GameTime;

                Log.Info("The city is on the online map, with " + Outsides.Length + " outsides asked for.");
            }
            catch (Exception ex)
            {
                Log.Warn("Could not put the city on the online map: " + ex.Message);
            }
        }

        /// <summary>The story map back, and the outsides taken away. For the setting going off, and for teardown.</summary>
        public static void Restore()
        {
            if (!_on) return;

            _on = false;

            try
            {
                foreach (var name in Outsides)
                {
                    Function.Call(Hash.REMOVE_IPL, name);
                }

                Function.Call(Hash.ON_ENTER_SP);

                Log.Info("The city is back on the story map.");
            }
            catch (Exception ex)
            {
                Log.Warn("Could not put the city back on the story map: " + ex.Message);
            }
        }

        /// <summary>
        /// Every so often, asks whether the map is still ours and puts it back if not.
        ///
        /// One IPL stands for the lot. If the strawberry auto shop has gone, the map has
        /// been reset under us, and everything is asked for again.
        /// </summary>
        public static void Update()
        {
            if (!_on) return;
            if (Game.GameTime - _checkedAt < CheckMs) return;

            _checkedAt = Game.GameTime;

            try
            {
                if (Function.Call<bool>(Hash.IS_IPL_ACTIVE, Canary)) return;

                Log.Info("The online map has been reset under us; putting it back.");
                Apply();
            }
            catch
            {
                // Asked again in fifteen seconds.
            }
        }
    }
}
