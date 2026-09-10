using System.Collections.Generic;
using Hoodrich.Core;

namespace Hoodrich.Locations
{
    /// <summary>One place with an inside, and the name of the thing that has to load for it.</summary>
    internal sealed class Place
    {
        /// <summary>The ini section it gets written to. Unique, and never changes once shipped.</summary>
        public string Key = "";

        /// <summary>What it is called on the picker and on the map.</summary>
        public string Name = "";

        /// <summary>
        /// The interiors to ask for, separated by semicolons, or empty for a room that is
        /// already in the map.
        ///
        /// Every name here was read out of the string table of the interior loader sitting in
        /// scripts\New folder on this machine -- not typed from memory and not guessed at. A
        /// wrong name here is the exact failure that cost four rounds on the grow room: the
        /// room does not appear, and nothing about the message says whether the name or the
        /// coordinate is the thing that is wrong.
        /// </summary>
        public string Ipl = "";

        /// <summary>The blip. A number the game does not have draws a plain dot, not nothing.</summary>
        public int Sprite = 40;

        /// <summary>Where to go looking. Written into the file so it can be read outside the game.</summary>
        public string Where = "";

        /// <summary>
        /// Online map content whose interior name is NOT in the string table.
        ///
        /// Normally a place with no Ipl is a mission room already in the story map, and the
        /// two presses are all it needs. A handful are neither: they are online rooms whose
        /// placement name the interior loader does not carry, so we know where they are and
        /// not what to ask for. Saying "already in the map" about those would be a lie that
        /// costs somebody a drive.
        /// </summary>
        public bool Unnamed;
    }

    /// <summary>
    /// Everywhere with an inside, in the order somebody would go looking for it.
    ///
    /// THIS IS A LIST OF NAMES, NOT A LIST OF COORDINATES. Not one number in this file was
    /// typed. The coordinates come from somebody standing at the door and pressing a button,
    /// because that is the only way this mod has ever got one right.
    ///
    /// Two kinds of place are in here and the difference matters:
    ///
    ///   * ONLINE rooms carry an Ipl. They do not exist in a story game at all, so the door
    ///     switches the map over to the online one before it asks for them.
    ///   * STORY rooms carry no Ipl. They are mission rooms that are already in the map and
    ///     simply have no way in. They need nothing but a door.
    /// </summary>
    internal static class Places
    {
        public static readonly string[] Groups =
        {
            "The casino",
            "Nightclubs",
            "Biker",
            "Offices and cargo",
            "Guns and the war",
            "Cars",
            "The agency",
            "The newer jobs",
            "Rooms with no door",
            "More rooms with no door",
            "Boats and the island",
            "Houses",
            "Odds and ends"
        };

        // ------------------------------------------------------------------ the casino

        private static readonly Place[] Casino =
        {
            P("CasinoFloor", "Diamond Casino floor", "vw_casino_main", 267,
              "Vinewood Park Drive. The round building with the fountain."),
            P("CasinoPenthouse", "Casino penthouse",
              "vw_casino_penthouse;vw_int_placement_vw_interior_1_dlc_casino_apart_milo_", 267,
              "Top of the casino. The lift on the east side of the floor."),
            P("CasinoGarage", "Casino penthouse garage",
              "vw_casino_garage;vw_int_placement_vw_interior_2_dlc_casino_garage_milo_", 357,
              "Under the penthouse."),
            P("CasinoCarPark", "Casino car park",
              "vw_casino_carpark;vw_int_placement_vw_interior_4_dlc_casino_carpark_milo_", 357,
              "The ramp on the north side."),
            P("CasinoBack", "Casino back offices",
              "ch_int_placement_ch_interior_3_dlc_casino_back_milo_", 267,
              "Behind the floor. Staff only in the game's own words."),
            P("CasinoVault", "Casino vault",
              "ch_int_placement_ch_interior_6_dlc_casino_vault_milo_", 267,
              "Under the back offices."),
            P("CasinoHotel", "Casino hotel corridor",
              "ch_int_placement_ch_interior_4_dlc_casino_hotel_milo_", 267,
              "The guest floor above the casino."),
            P("CasinoLoading", "Casino loading bay",
              "ch_int_placement_ch_interior_5_dlc_casino_loading_milo_", 357,
              "The service entrance round the back."),
            P("CasinoTunnel", "Casino service tunnel",
              "ch_int_placement_ch_interior_8_dlc_tunnel_milo_", 40,
              "The tunnel the heist crew uses."),
            P("CasinoShaft", "Casino lift shaft",
              "ch_int_placement_ch_interior_9_dlc_casino_shaft_milo_", 40,
              "The shaft off the tunnel."),
            P("HeistRoom", "Heist planning room",
              "ch_int_placement_ch_interior_0_dlc_casino_heist_milo_", 267,
              "The arcade basement, under the machines."),
            P("Arcade", "Arcade",
              "ch_int_placement_ch_interior_1_dlc_arcade_milo_", 135,
              "Six of them: Paleto, Grapeseed, Davis, Rockford, Vinewood, La Mesa.")
        };

        // ------------------------------------------------------------------ nightclubs
        //
        // TEN OF THEM AND ONLY ONE IS OPEN AT A TIME. The barrier set is what unblocks a
        // particular one's front door, so each place asks for its own barrier case plus the
        // shared interior. The numbers are the game's, not mine -- they came out of the
        // string table paired with the district names exactly as they read here.

        private const string Club =
            "ba_int_placement_ba_interior_0_dlc_int_01_ba_milo_;" +
            "ba_int_placement_ba_interior_1_dlc_int_01_ba_milo_";

        private static readonly Place[] Clubs =
        {
            P("ClubLaMesa", "Nightclub, La Mesa", "ba_barriers_case0;" + Club, 121,
              "Popular Street, east of the tracks."),
            P("ClubMissionRow", "Nightclub, Mission Row", "ba_barriers_case1;" + Club, 121,
              "Behind the police station."),
            P("ClubStrawberry", "Nightclub, Strawberry", "ba_barriers_case2;" + Club, 121,
              "Innocence Boulevard."),
            P("ClubWestVinewood", "Nightclub, West Vinewood", "ba_barriers_case3;" + Club, 121,
              "Eclipse Boulevard."),
            P("ClubCypressFlats", "Nightclub, Cypress Flats", "ba_barriers_case4;" + Club, 121,
              "Kimble Hill Drive end of the industrial block."),
            P("ClubDelPerro", "Nightclub, Del Perro", "ba_barriers_case5;" + Club, 121,
              "Bay City Avenue, near the pier."),
            P("ClubLSIA", "Nightclub, LSIA", "ba_barriers_case6;" + Club, 121,
              "The airport service road."),
            P("ClubElysian", "Nightclub, Elysian Island", "ba_barriers_case7;" + Club, 121,
              "The docks."),
            P("ClubDowntownVinewood", "Nightclub, Downtown Vinewood", "ba_barriers_case8;" + Club, 121,
              "Elgin Avenue."),
            P("ClubVespucci", "Nightclub, Vespucci Canals", "ba_barriers_case9;" + Club, 121,
              "Prosperity Street."),
            P("ClubGarage", "Nightclub garage",
              "ba_int_placement_ba_interior_1_dlc_int_01_ba_milo_", 357,
              "The shutter beside whichever club you picked.")
        };

        // ------------------------------------------------------------------ biker
        //
        // WHICH WAREHOUSE IS WHICH came out of the string table by what follows each name:
        // ware01 is followed by the meth sets, ware03 by the cocaine sets, ware04 by the
        // counterfeit cash piles. ware02 is the weed farm and is the room this mod already
        // opens at Lamar's.

        private static readonly Place[] Biker =
        {
            P("ClubhouseA", "Biker clubhouse",
              "bkr_biker_interior_placement_interior_0_biker_dlc_int_01_milo", 121,
              "Twelve of them. The big ones are Paleto, Sandy Shores, La Mesa, Del Perro."),
            P("ClubhouseB", "Biker clubhouse, one floor",
              "bkr_biker_interior_placement_interior_1_biker_dlc_int_02_milo", 121,
              "The smaller pattern of clubhouse."),
            P("MethLab", "Meth lab",
              "bkr_biker_interior_placement_interior_2_biker_dlc_int_ware01_milo", 267,
              "The lockups out in the county."),
            P("CocaineLockup", "Cocaine lockup",
              "bkr_biker_interior_placement_interior_4_biker_dlc_int_ware03_milo", 267,
              "Same sort of unit, different town."),
            P("CounterfeitCash", "Counterfeit cash factory",
              "bkr_biker_interior_placement_interior_5_biker_dlc_int_ware04_milo", 267,
              "Same again."),
            // WARE02 IS THE WEED FARM, not the forgery office. The string table settles it:
            // ware01 is followed by the meth sets, ware03 by the cocaine, ware04 by the
            // counterfeit cash, and ware02 by the dryers and the weed stashes. It is the room
            // this mod already opens at Lamar's, which is the other half of the proof.
            //
            // The forgery office is a fifth business and its placement name is not in the
            // string table at all, so it is not on this list. A name nobody can check is how
            // the grow room went wrong four times.
            P("WeedFarm", "Weed farm",
              "bkr_biker_interior_placement_interior_3_biker_dlc_int_ware02_milo", 267,
              "Grapeseed, the barn off the highway.")
        };

        // ------------------------------------------------------------------ offices and cargo

        private static readonly Place[] Offices =
        {
            P("MazeBankTower", "Maze Bank Tower office", "ex_dt1_11_office_01a", 267,
              "The tallest building in the city, Pillbox Hill."),
            P("ArcadiusTower", "Arcadius Center office", "ex_dt1_02_office_01a", 267,
              "Power Street, downtown."),
            P("MazeBankWest", "Maze Bank West office", "ex_sm_15_office_01a", 267,
              "Del Perro, on the boulevard."),
            P("LombokTower", "Lombank West office", "ex_sm_13_office_01a", 267,
              "Del Perro, the black tower."),
            P("CeoGarageArcadius", "CEO garage, Arcadius",
              "imp_dt1_02_cargarage_a;imp_dt1_02_cargarage_b;imp_dt1_02_cargarage_c", 357,
              "Under the Arcadius tower."),
            P("CeoGarageMazeBank", "CEO garage, Maze Bank Tower",
              "imp_dt1_11_cargarage_a;imp_dt1_11_cargarage_b;imp_dt1_11_cargarage_c", 357,
              "Under the Maze Bank Tower."),
            P("CeoGarageLombok", "CEO garage, Lombank",
              "imp_sm_13_cargarage_a;imp_sm_13_cargarage_b;imp_sm_13_cargarage_c", 357,
              "Under Lombank West."),
            P("CeoGarageMbWest", "CEO garage, Maze Bank West",
              "imp_sm_15_cargarage_a;imp_sm_15_cargarage_b;imp_sm_15_cargarage_c", 357,
              "Under Maze Bank West."),
            P("CargoSmall", "Small cargo warehouse",
              "ex_exec_warehouse_placement_interior_1_int_warehouse_s_dlc_milo", 267,
              "Roller doors all over the industrial districts."),
            P("CargoMedium", "Medium cargo warehouse",
              "ex_exec_warehouse_placement_interior_0_int_warehouse_m_dlc_milo", 267,
              "Same sort of unit, bigger inside."),
            P("CargoLarge", "Large cargo warehouse",
              "ex_exec_warehouse_placement_interior_2_int_warehouse_l_dlc_milo", 267,
              "The big ones down by the docks."),
            P("VehicleWarehouse", "Vehicle warehouse",
              "imp_impexp_interior_placement_interior_1_impexp_intwaremed_milo_", 357,
              "La Mesa, Cypress Flats, Elysian Island.")
        };

        // ------------------------------------------------------------------ guns and the war

        private static readonly Place[] War =
        {
            P("Bunker", "Bunker",
              "gr_grdlc_interior_placement_interior_1_grdlc_int_01_milo_", 557,
              "Hatches in the ground out in the county. Eleven of them."),
            P("WeaponWorkshop", "Weapon workshop",
              "gr_grdlc_interior_placement_interior_1_grdlc_int_02_milo_", 557,
              "The far end of the bunker."),
            P("Hangar", "Hangar",
              "sm_smugdlc_interior_placement_interior_0_smugdlc_int_01_milo_", 40,
              "LSIA and Fort Zancudo."),
            P("Facility", "Doomsday facility",
              "xm_x17dlc_int_placement_interior_4_x17dlc_int_facility_milo_", 557,
              "Nine hatches, mostly in the hills and the desert."),
            P("Facility2", "Doomsday facility, other pattern",
              "xm_x17dlc_int_placement_interior_5_x17dlc_int_facility2_milo_", 557,
              "The second shell the facility can have."),
            P("Silo", "Missile silo",
              "xm_x17dlc_int_placement_interior_9_x17dlc_int_01_milo_;" +
              "xm_x17dlc_int_placement_interior_33_x17dlc_int_02_milo_;" +
              "xm_x17dlc_int_placement_interior_35_x17dlc_int_tun_entry_milo_", 40,
              "Mount Chiliad, the doors in the rock."),
            P("DoomsdayLab", "Doomsday lab",
              "xm_x17dlc_int_placement_interior_34_x17dlc_int_lab_milo_", 40,
              "Off the silo tunnels."),
            P("DoomsdaySub", "Doomsday submarine",
              "xm_x17dlc_int_placement_interior_8_x17dlc_int_sub_milo_", 40,
              "Under the water off the north coast."),
            // NO IPL, AND NO CUTSCENE TO BUILD EITHER.
            //
            // The range is part of the base map and always loaded, so this needs nothing asked
            // for -- and it is the one room on this list you can already walk into, which is
            // exactly why it is worth a second door. A shooting range half an hour's drive
            // away is a thing you did once.
            //
            // AND THE DOOR PUTS YOU IN THE SHOP, NOT IN THE RANGE. That looks like the lazier
            // choice and it is the opposite: the walk from the counter through the range door
            // is where the game plays its own arrival, and the challenge board on the far wall
            // is the game's own too. Land somebody past all of it and you have skipped the
            // thing they came for in order to save them nine steps. So the door does what a
            // door does, and everything after it is Rockstar's -- the transition, the
            // attendant, the challenges, the medals and the unlocks.
            // LEROY'S ELECTRICAL. A shop front on Strawberry with a warehouse floor behind
            // it, and the grow. Both coordinates in the ini were stood on rather than worked
            // out, which is the only way this mod has ever got one right -- these two rows are
            // how they got there and are how to move it.
            P("LeroysElectrical", "Leroy's Electrical", "", 51,
              "Strawberry Ave, the black door with the NO TRESPASSING sign. Comes out in the " +
              "Torture room in Banning -- the same room as the entry below, under a shop name."),
            P("GunRange", "Ammu-Nation range", "", 110,
              "Inside the shops that have one: Cypress Flats, Little Seoul, Vinewood, " +
              "Sandy Shores, Paleto Bay. Stand at the counter, facing the range door."),
            P("IaaFacility", "IAA facility", "", 40,
              "The one under the Kortz Center."),
            P("ServerFarm", "IAA server farm", "", 40,
              "Also under the Kortz Center.")
        };

        // ------------------------------------------------------------------ cars

        private static readonly Place[] Cars =
        {
            P("AutoShopBurton", "Auto shop, Burton",
              "tr_tuner_shop_burton;tr_int_placement_tr_interior_0_tuner_mod_garage_milo_", 72,
              "Popular Street."),
            P("AutoShopLaMesa", "Auto shop, La Mesa",
              "tr_tuner_shop_mesa;tr_int_placement_tr_interior_0_tuner_mod_garage_milo_", 72,
              "Palomino Avenue."),
            P("AutoShopMissionRow", "Auto shop, Mission Row",
              "tr_tuner_shop_mission;tr_int_placement_tr_interior_0_tuner_mod_garage_milo_", 72,
              "Behind the station, near the club."),
            P("AutoShopRancho", "Auto shop, Rancho",
              "tr_tuner_shop_rancho;tr_int_placement_tr_interior_0_tuner_mod_garage_milo_", 72,
              "Roy Lowenstein Boulevard. Closest one to the block."),
            P("AutoShopStrawberry", "Auto shop, Strawberry",
              "tr_tuner_shop_strawberry;tr_int_placement_tr_interior_0_tuner_mod_garage_milo_", 72,
              "Davis Avenue."),
            P("CarMeet", "LS Car Meet",
              "tr_int_placement_tr_interior_6_tuner_car_meet_milo_;tr_tuner_race_line", 72,
              "Cypress Flats, the warehouse with the shutter."),
            P("SalvageYard", "Salvage yard",
              "m24_1_int_placement_m24_1_g9ec_interior_0_dlc_int_mod_milo_", 72,
              "Five of them. Strawberry, Paleto, Sandy Shores, La Mesa, Murrieta."),
            P("VinewoodCarClub", "Vinewood Car Club mod shop",
              "m24_1_int_placement_m24_1_g9ec_interior_0_dlc_int_mod_milo_", 72,
              "Under the casino."),
            P("ArenaWar", "Maze Bank Arena floor", "xs_arena_interior", 40,
              "The arena itself, in Little Seoul."),
            P("ArenaWorkshop", "Arena workshop", "xs_arena_interior_mod", 72,
              "Under the arena."),
            P("ArenaVip", "Arena VIP box", "xs_arena_interior_vip", 267,
              "Also under the arena.")
        };

        // ------------------------------------------------------------------ the agency

        private static readonly Place[] Agency =
        {
            P("AgencyHawick", "Agency, Hawick",
              "sf_fixeroffice_hw1_08;sf_int_placement_sec_interior_0_dlc_office_sec_milo_", 267,
              "Hawick Avenue."),
            P("AgencyRockford", "Agency, Rockford Hills",
              "sf_fixeroffice_bh1_05;sf_int_placement_sec_interior_0_dlc_office_sec_milo_", 267,
              "Marathon Avenue."),
            P("AgencyLittleSeoul", "Agency, Little Seoul",
              "sf_fixeroffice_kt1_05;sf_int_placement_sec_interior_0_dlc_office_sec_milo_", 267,
              "Vespucci Boulevard."),
            P("AgencyVespucci", "Agency, Vespucci Canals",
              "sf_fixeroffice_kt1_08;sf_int_placement_sec_interior_0_dlc_office_sec_milo_", 267,
              "Bay City Avenue."),
            P("MusicStudio", "Record A Studios",
              "sf_int_placement_sec_interior_1_dlc_studio_sec_milo_", 135,
              "West Vinewood, the studio lot."),
            P("AgencyGarage", "Agency garage",
              "sf_int_placement_sec_interior_2_dlc_garage_sec_milo_", 357,
              "Under whichever agency you took."),
            P("ContractWarehouse", "The Contract warehouse",
              "sf_int_placement_sec_interior_7_dlc_warehouse_sec_milo_", 267,
              "The one the jewellery job runs out of.")
        };

        // ------------------------------------------------------------------ the newer jobs
        //
        // THE FOUR DRUG WARS ROOMS SHARE THEIR NAMES. The string table lists all four
        // placements but does not say which is the acid lab and which is the hideout, so each
        // door here asks for all four. Asking for a placement that turns out to be the wrong
        // one costs a little memory and nothing else; guessing which is which and being wrong
        // costs an evening.

        private const string DrugWars =
            "xm3_int_placement_xm3_interior_0_dlc_int_01_xm3_milo_;" +
            "xm3_int_placement_xm3_interior_1_dlc_int_02_xm3_milo_;" +
            "xm3_int_placement_xm3_interior_2_dlc_int_03_xm3_milo_;" +
            "xm3_int_placement_xm3_interior_3_dlc_int_04_xm3_milo_";

        private static readonly Place[] Newer =
        {
            P("AcidLab", "Acid lab", DrugWars, 267,
              "The trailer at the Freakshop, Mirror Park."),
            P("JuggaloHideout", "Juggalo hideout", DrugWars, 267,
              "Up past Paleto, off the Great Ocean Highway."),
            P("MultiStoreyGarage", "Multi storey garage", DrugWars + ";xm3_warehouse", 357,
              "The Drug Wars garage."),
            P("DrugWarsMorgue", "Drug Wars morgue", DrugWars, 40,
              "The morgue the Fooliganz job uses."),
            P("Freakshop", "Freakshop",
              "m23_1_int_placement_m23_1_interior_1_dlc_int_02_m23_1_milo_", 267,
              "Mirror Park, the yard behind the fence."),
            P("DealershipBasement", "Dealership basement",
              "m23_1_int_placement_m23_1_interior_2_dlc_int_03_m23_1_milo_", 72,
              "Under the Premium Deluxe lot."),
            P("BailDavis", "Bail office, Davis",
              "m24_1_bailoffice_davis;m24_1_int_placement_m24_1_interior_dlc_int_bounty_milo_", 267,
              "Davis Avenue."),
            P("BailDelPerro", "Bail office, Del Perro",
              "m24_1_bailoffice_delperro;m24_1_int_placement_m24_1_interior_dlc_int_bounty_milo_", 267,
              "Del Perro."),
            P("BailMissionRow", "Bail office, Mission Row",
              "m24_1_bailoffice_missionrow;m24_1_int_placement_m24_1_interior_dlc_int_bounty_milo_", 267,
              "Opposite the police station."),
            P("BailVinewood", "Bail office, Vinewood",
              "m24_1_bailoffice_vinewood;m24_1_int_placement_m24_1_interior_dlc_int_bounty_milo_", 267,
              "Vinewood Boulevard."),
            P("BailPaleto", "Bail office, Paleto Bay",
              "m24_1_bailoffice_paletobay;m24_1_int_placement_m24_1_interior_dlc_int_bounty_milo_", 267,
              "Paleto Boulevard."),
            P("HackerBasement", "Hacker basement",
              "m24_2_int_placement_interior_int_hacker_basement_milo_;" +
              "m24_2_int_placement_interior_int_hacker_garage_milo_", 267,
              "The Agents of Sabotage hideout.")
        };

        // ------------------------------------------------------------------ rooms with no door
        //
        // NOT ONE OF THESE NEEDS AN IPL. They are mission rooms that are in the story map
        // already and have simply never had a way in. A door is all they are missing, which
        // makes them the cheapest ones on the list to add and the most likely to work first
        // time.

        private static readonly Place[] Story =
        {
            P("BahamaMamas", "Bahama Mamas", "", 93,
              "West Vinewood, the club on the corner."),
            P("Tequilala", "Tequi-la-la", "", 93,
              "Vinewood Boulevard."),
            P("SplitSides", "Split Sides comedy club", "", 135,
              "Vinewood, next to the cinema."),
            P("CluckinBell", "Cluckin' Bell factory", "", 40,
              "Paleto Bay, the big plant on the water."),
            P("MeatPacking", "Meat packing factory", "", 40,
              "Cypress Flats."),
            P("Simeon", "Simeon's showroom", "shr_int;hei_showroom_open", 72,
              "Premium Deluxe Motorsport, Pillbox Hill."),
            P("Vangelico", "Vangelico jewellers", "", 40,
              "Portola Drive, Rockford Hills."),
            P("LifeInvader", "Life Invader offices", "", 40,
              "Boulevard Del Perro."),
            P("FibOffice", "FIB office", "", 40,
              "The FIB tower, downtown."),
            P("Friedlander", "Dr Friedlander's office", "", 40,
              "Vinewood, the low white building."),
            P("Coroner", "Coroner's office", "", 40,
              "Davis, beside the hospital."),
            P("Morgue", "Morgue", "", 40,
              "The old one, under the hospital.")
        };

        private static readonly Place[] Story2 =
        {
            P("Foundry", "The Foundry", "", 40,
              "El Burro Heights, the industrial block."),
            P("Omega", "Omega's garage", "", 357,
              "Sandy Shores."),
            P("HainesAutos", "Haines Autos", "", 72,
              "Sandy Shores, the covered cars."),
            P("LesterFactory", "Lester's factory", "", 40,
              "Murrieta Heights."),
            P("Janitor", "Janitor's apartment", "", 267,
              "The one from the FIB job."),
            P("Kiflom", "Epsilon storage room", "", 40,
              "Rockford Hills."),
            // BANNING, NOT SANDY SHORES. This said "under the sheriff's building" and that is
            // a different room in a different mission -- somebody has now stood in this one
            // and read the coordinates off the screen, and they are on Dutch London St in
            // Banning, which is where By the Book actually happens. A hint nobody has checked
            // loses to a coordinate somebody measured, every time.
            //
            // No Ipl, and that is worth stating because it was doubted: it is a STORY mission
            // room, already in the map, with no way in rather than nothing there. See the note
            // at the top of this file for what that distinction means.
            P("Torture", "Torture room", "", 40,
              "The warehouse floor off Dutch London St, Banning."),
            P("MazeArena", "Maze Bank Arena stands", "", 40,
              "Little Seoul."),
            P("FameOrShame", "Fame or Shame studio",
              "hei_hw1_blimp_interior_v_studio_lo_milo_", 135,
              "The studio lot, West Vinewood."),
            P("Mugshot", "Online mugshot room",
              "hw1_int_placement_interior_v_mugshot_milo_", 40,
              "Nowhere in particular. The room the game photographs you in."),
            P("WinningRoom", "Online winning room",
              "hw1_int_placement_interior_v_winningroom_milo_", 40,
              "The other one of those."),
            P("KortzCenter", "The Kortz Center", "", 40,
              "Pacific Bluffs, on the cliff.")
        };

        // ------------------------------------------------------------------ boats and the island

        private static readonly Place[] Water =
        {
            P("Kosatka", "Kosatka submarine",
              "h4_int_placement_h4_interior_0_int_sub_h4_milo_", 40,
              "Off the coast, wherever you left it."),
            P("IslandNightclub", "Cayo Perico nightclub",
              "h4_int_placement_h4_interior_1_dlc_int_02_h4_milo_", 121,
              "The island. Needs the island loaded, which is its own job."),
            P("HeistYacht", "Yacht",
              "hei_yacht_heist;hei_yacht_heist_bar;hei_yacht_heist_bedrm;" +
              "hei_yacht_heist_bridge;hei_yacht_heist_lounge;hei_yacht_heist_enginrm", 40,
              "Anchored off the coast."),
            P("GunrunYacht", "Gunrunning yacht",
              "gr_Heist_Yacht2;gr_Heist_Yacht2_Bar;gr_Heist_Yacht2_Bedrm;" +
              "gr_Heist_Yacht2_Bridge;gr_Heist_Yacht2_Lounge;gr_Heist_Yacht2_enginrm", 40,
              "The other yacht."),
            P("Carrier", "Aircraft carrier",
              "hei_Carrier_int1;hei_Carrier_int2;hei_Carrier_int3;" +
              "hei_Carrier_int4;hei_Carrier_int5;hei_Carrier_int6", 40,
              "Out at sea, north west."),
            P("CarrierHangar", "Aircraft carrier hangar",
              "m24_1_int_mph_carrierhang1;m24_1_int_mph_carrierhang2;m24_1_int_mph_carrierhang3", 40,
              "Below the flight deck."),
            P("CargoShip", "Cargo ship",
              "m23_2_cargoship;m23_2_cargoship_bridge", 40,
              "The one moored at the docks."),
            P("NorthYankton", "North Yankton", "prologue06_int", 40,
              "The whole town, not a room. Big ask and worth trying once.")
        };

        // ------------------------------------------------------------------ houses

        private static readonly Place[] Houses =
        {
            P("RichmanMansion", "Richman mansion", "The_Richman_Villa_Mansion", 267,
              "Richman, the one with the long drive."),
            P("VinewoodResidence", "Vinewood residence", "The_Vinewood_Residence_Mansion", 267,
              "Vinewood Hills."),
            P("TongvaEstate", "Tongva Hills estate", "The_Tongva_Estate_Mansion", 267,
              "Out past the vineyard."),
            P("Madrazo", "Martin Madrazo's house", "", 267,
              "Vinewood Hills, up the private road."),
            P("Denise", "Denise's house", "", 40,
              "Strawberry. Next door but one to the block."),
            P("Lester", "Lester's house", "", 40,
              "Murrieta Heights."),
            P("Solomon", "Solomon Richards' office", "", 40,
              "Richards Majestic, the studio lot."),
            P("HotelRoom", "Hotel room", "", 267,
              "The Von Crastenburg, Rockford Hills.")
        };

        // ------------------------------------------------------------------ odds and ends
        //
        // Places the interior loader's marker table had that our own list did not. Every one
        // of them has a coordinate, which is more than most of the list can say.

        private static readonly Place[] Odds =
        {
            P("HumaneLabs", "Humane Labs", "", 40,
              "Out east past the Alamo Sea, on the water."),
            P("MerryweatherFacility", "Merryweather facility", "", 40,
              "Elysian Island.", true),
            P("McKenzieHangar", "McKenzie Field hangar office", "", 40,
              "The airstrip at Grapeseed."),
            P("OneilFarm", "O'Neil farm", "", 40,
              "Grapeseed, the farmhouse."),
            P("RogersScrapyard", "Roger's scrapyard", "", 40,
              "Cypress Flats."),
            P("WreckedHospital", "Wrecked hospital", "", 61,
              "Pillbox Hill. The one from the first heist."),
            P("MansionArtStudio", "Mansion art studio", "", 267,
              "Richman, beside the mansion."),
            P("KortzLoading", "Kortz Center loading bay", "", 40,
              "The back of the Kortz Center."),
            P("KortzSewer", "Kortz Center sewer access", "", 40,
              "Below the Kortz Center."),
            P("ChopWarehouseA", "Chop shop warehouse A", "", 267,
              "Down by the docks.", true),
            P("ChopWarehouseB", "Chop shop warehouse B", "", 267,
              "Also down by the docks.", true),
            P("ChopCounterfeit", "Counterfeit cash factory, Strawberry", "", 267,
              "Strawberry, a street from the block.", true),
            P("HackerGarage", "Hacker basement garage",
              "m24_2_int_placement_interior_int_hacker_garage_milo_", 357,
              "Beside the hacker basement."),
            P("MoneyFrontCarWash", "Money front car wash",
              "reh_int_placement_sum2_interior_0_dlc_int_03_sum2_milo_;" +
              "reh_int_placement_sum2_interior_1_dlc_int_04_sum2_milo_", 72,
              "Strawberry Avenue, the one by the block.")
        };

        private static readonly Place[][] All =
        {
            Casino, Clubs, Biker, Offices, War, Cars, Agency, Newer, Story, Story2, Water,
            Houses, Odds
        };

        private static Place P(string key, string name, string ipl, int sprite, string where,
                               bool unnamed = false)
        {
            return new Place
            {
                Key = key,
                Name = name,
                Ipl = ipl,
                Sprite = sprite,
                Where = where,
                Unnamed = unnamed
            };
        }

        /// <summary>Everything in one group, or the first group for a number out of range.</summary>
        public static Place[] In(int group)
        {
            if (group < 0 || group >= All.Length) return All[0];
            return All[group];
        }

        /// <summary>
        /// The picker's own text: the names, with the ones already done marked.
        ///
        /// Read back off the disk rather than remembered, so a place done in an earlier
        /// session still reads as done and nobody walks across the map twice.
        /// </summary>
        public static string[] Labels(int group)
        {
            var list = In(group);
            var out_ = new string[list.Length];

            for (var i = 0; i < list.Length; i++)
            {
                out_[i] = Done(list[i]) ? list[i].Name + " - done" : list[i].Name;
            }

            return out_;
        }

        /// <summary>Whether both ends of this one have been written down already.</summary>
        public static bool Done(Place place)
        {
            try
            {
                var x = Settings.Read(place.Key, "InsideX", "");
                return x.Length > 0 && x != "0";
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Everywhere, for the blips and for counting.</summary>
        public static IEnumerable<Place> Every()
        {
            foreach (var group in All)
            {
                foreach (var place in group) yield return place;
            }
        }

        /// <summary>How many are finished, out of how many there are. For the readout.</summary>
        public static string Tally()
        {
            var done = 0;
            var all = 0;

            foreach (var place in Every())
            {
                all++;
                if (Done(place)) done++;
            }

            return done + " of " + all;
        }
    }
}
