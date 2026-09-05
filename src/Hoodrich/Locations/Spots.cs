using System.Collections.Generic;
using GTA.Math;

namespace Hoodrich.Locations
{
    /// <summary>
    /// Roughly where each place is, for the map blip.
    ///
    /// NOT ONE OF THESE WAS TYPED. Every coordinate below was read out of the marker table of
    /// the interior loader sitting in scripts\New folder on this machine, by finding the
    /// instruction that pushes each marker's name and taking the three floats pushed
    /// immediately before it. Nine of them were then checked against places whose whereabouts
    /// are not in dispute -- Denise's house, Simeon's showroom, Vangelico, Life Invader,
    /// Lester's house, the arena, the casino, Cluckin' Bell, Tequi-la-la -- and every one
    /// landed within sixteen metres.
    ///
    /// It is worth saying why that check exists. Reading the floats AFTER the name, which is
    /// the obvious way round, pairs every marker with the next one's coordinate. It looked
    /// right on the casino by luck and put Cluckin' Bell five kilometres out to sea. The
    /// spot check is what caught it.
    ///
    /// A COORDINATE HERE IS NOT A DOOR. It is close enough to drive to and no closer -- a
    /// blip so you can find the place, not a way in. The way in is still the two presses on
    /// the settings screen, standing on the spot. See DoorMaker.
    /// </summary>
    internal static class Spots
    {
        private static readonly Dictionary<string, Vector3> Where =
            new Dictionary<string, Vector3>
        {
            { "CasinoFloor", new Vector3(924.600f, 45.400f, 80.000f) },   // Casino
            { "CasinoCarPark", new Vector3(935.540f, -0.150f, 77.700f) },   // The Chop Shop Casino Base
            { "Arcade", new Vector3(-243.100f, 6208.800f, 31.000f) },   // Arcade
            { "ClubStrawberry", new Vector3(-121.400f, -1290.500f, 28.500f) },   // Nightclub
            { "MethLab", new Vector3(202.045f, 2461.467f, 54.700f) },   // Meth Lab
            { "CocaineLockup", new Vector3(-1462.010f, -382.291f, 37.500f) },   // Cocaine Lockup
            { "CounterfeitCash", new Vector3(670.873f, -2667.514f, 5.200f) },   // Counterfeit Office
            { "WeedFarm", new Vector3(2882.437f, 4462.606f, 47.300f) },   // Weed Farm
            { "MazeBankTower", new Vector3(-70.800f, -800.400f, 43.600f) },   // Maze Bank Tower
            { "ArcadiusTower", new Vector3(-118.300f, -608.400f, 35.600f) },   // Arcadius Tower
            { "MazeBankWest", new Vector3(-1370.800f, -503.600f, 32.500f) },   // Maze Bank West Tower
            { "LombokTower", new Vector3(-1582.400f, -556.600f, 33.900f) },   // Lombok Tower
            { "CargoSmall", new Vector3(-424.789f, 185.590f, 79.800f) },   // Small Cargo Warehouse
            { "CargoMedium", new Vector3(-295.311f, -1353.118f, 30.310f) },   // Medium Cargo Warehouse
            { "CargoLarge", new Vector3(-873.377f, -2734.696f, 12.920f) },   // Large Cargo Warehouse
            { "VehicleWarehouse", new Vector3(1211.081f, -1262.385f, 34.220f) },   // Vehicle Warehouse
            { "Bunker", new Vector3(-388.838f, 4339.795f, 55.172f) },   // Bunker
            { "Facility", new Vector3(3388.648f, 5509.233f, 24.760f) },   // Facility
            { "Silo", new Vector3(-361.360f, 4827.090f, 142.500f) },   // Doomsday Silo Base
            { "DoomsdaySub", new Vector3(-1291.800f, 6859.500f, -90.000f) },   // Submarine
            { "Hangar", new Vector3(-1394.400f, -3267.200f, 13.000f) },   // Smugglers Run Hangar
            { "IaaFacility", new Vector3(2049.900f, 2949.500f, 46.700f) },   // IAA Facility
            { "ServerFarm", new Vector3(2477.260f, -402.020f, 93.500f) },   // Server Farm
            { "AutoShopMissionRow", new Vector3(493.230f, -894.320f, 24.730f) },   // Tuner Workshop
            { "CarMeet", new Vector3(779.598f, -1867.766f, 28.220f) },   // Los Santos Car Meet
            { "SalvageYard", new Vector3(-512.422f, -1738.619f, 18.290f) },   // The Chop Shop Salvage Yard
            { "VinewoodCarClub", new Vector3(1233.540f, -3234.950f, 4.520f) },   // San Andreas Mercanaries Vinewood Car Club
            { "ArenaWar", new Vector3(-254.190f, -2027.200f, 29.000f) },   // Maze Bank Arena
            { "ArenaWorkshop", new Vector3(-377.103f, -1876.600f, 19.500f) },   // Arena War Workshop
            { "ArenaVip", new Vector3(200.800f, 5180.000f, -89.000f) },   // Arena War Vip Area
            { "AgencyHawick", new Vector3(389.260f, -75.190f, 67.180f) },   // Agency
            { "AgencyGarage", new Vector3(371.970f, -60.067f, 102.360f) },   // Agency Garage
            { "MusicStudio", new Vector3(-841.560f, -229.240f, 36.250f) },   // Record A Studios
            { "JuggaloHideout", new Vector3(597.430f, -408.150f, 25.040f) },   // Los Santos Drug Wars Juggalo Hideout
            { "MultiStoreyGarage", new Vector3(316.940f, -1104.800f, 28.400f) },   // Los Santos Drug Wars Multistory Garage
            { "DrugWarsMorgue", new Vector3(232.230f, -1360.900f, 27.600f) },   // Los Santos Drug Wars Morgue
            { "Freakshop", new Vector3(118.520f, -3164.460f, 5.000f) },   // The Criminal Enterprise Warehouse
            { "DealershipBasement", new Vector3(116.120f, -2688.961f, 5.000f) },   // The Criminal Enterprise Basement
            { "BailMissionRow", new Vector3(485.700f, -943.550f, 26.150f) },   // Bottom Dollar Bounties Bail Office
            { "HackerBasement", new Vector3(739.970f, -970.234f, 23.450f) },   // Agents of Sabotage Hacker Basement
            { "BahamaMamas", new Vector3(-1388.900f, -586.110f, 29.500f) },   // Bahama Mamas West
            { "Tequilala", new Vector3(-564.200f, 275.500f, 82.000f) },   // Tequilala
            { "SplitSides", new Vector3(-430.040f, 261.718f, 82.000f) },   // Split Sides Commedy Club
            { "CluckinBell", new Vector3(-70.800f, 6266.300f, 30.000f) },   // Cluck N Bell
            { "MeatPacking", new Vector3(961.600f, -2185.200f, 29.000f) },   // Meat Packing Factory
            { "Simeon", new Vector3(-60.500f, -1093.600f, 26.000f) },   // Simeon's Dealership
            { "Vangelico", new Vector3(-632.600f, -238.600f, 38.000f) },   // Vangelico
            { "LifeInvader", new Vector3(-1045.300f, -230.300f, 39.000f) },   // Life Invader
            { "FibOffice", new Vector3(-106.900f, -8.670f, 70.000f) },   // FIB Office
            { "Friedlander", new Vector3(-1898.100f, -572.500f, 11.000f) },   // Dr Friedlander Office
            { "Morgue", new Vector3(240.900f, -1379.400f, 32.800f) },   // Morgue
            { "Foundry", new Vector3(1083.343f, -1974.480f, 30.000f) },   // Foundry
            { "Omega", new Vector3(2331.700f, 2576.100f, 45.600f) },   // Omega's Garage
            { "HainesAutos", new Vector3(483.500f, -1312.500f, 29.000f) },   // Haines Autos
            { "LesterFactory", new Vector3(718.100f, -975.700f, 24.000f) },   // Lester's Factory
            { "Janitor", new Vector3(-1150.300f, -1521.050f, 10.000f) },   // Jannitor's Appartment
            { "Kiflom", new Vector3(241.800f, 360.680f, 105.000f) },   // Kiflom Storage Room
            { "Torture", new Vector3(134.400f, -2203.400f, 7.000f) },   // Torture Room
            { "KortzCenter", new Vector3(-2290.609f, 364.125f, 173.703f) },   // The Kortz Center
            { "CargoShip", new Vector3(-166.500f, -2369.035f, 20.000f) },   // Cargo Ship
            { "GunrunYacht", new Vector3(-1414.300f, 6750.700f, 11.900f) },   // Gunrunning Yacht
            { "RichmanMansion", new Vector3(-1691.942f, 490.002f, 128.360f) },   // Richman Mansion
            { "VinewoodResidence", new Vector3(541.143f, 776.741f, 201.574f) },   // Vinewood Mansion
            { "TongvaEstate", new Vector3(-2555.205f, 1912.112f, 168.260f) },   // Tongva Hills Mansion
            { "Madrazo", new Vector3(1395.060f, 1141.700f, 113.400f) },   // Martin Madrazo's House
            { "Denise", new Vector3(-14.090f, -1442.260f, 31.000f) },   // Denise's House
            { "Lester", new Vector3(1274.600f, -1720.900f, 54.000f) },   // Lester's House
            { "Solomon", new Vector3(-1007.900f, -487.300f, 39.100f) },   // Solomon Richard's Office
            { "HotelRoom", new Vector3(1121.437f, 2641.793f, 37.300f) },   // Hotel Room
            { "HumaneLabs", new Vector3(3623.210f, 3753.350f, 28.500f) },   // Humaine Labs
            { "McKenzieHangar", new Vector3(2146.031f, 4782.017f, 39.990f) },   // McKenzie Field Hangar Office
            { "OneilFarm", new Vector3(2452.800f, 4969.933f, 46.000f) },   // O'Niels Farm
            { "RogersScrapyard", new Vector3(104.178f, -744.490f, 45.000f) },   // Roger's Scrap yard
            { "WreckedHospital", new Vector3(299.030f, -584.450f, 43.000f) },   // Wrecked Hospital
            { "MansionArtStudio", new Vector3(-1647.862f, 478.145f, 117.397f) },   // Mansion Art Studio
            { "MerryweatherFacility", new Vector3(551.115f, -3052.221f, 12.280f) },   // San Andreas Mercanaries Merryweather Facility
            { "ChopWarehouseA", new Vector3(928.760f, -2307.951f, 29.500f) },   // The Chop Shop Warehouse A
            { "ChopWarehouseB", new Vector3(877.070f, -2403.086f, 26.930f) },   // The Chop Shop Warehouse B
            { "ChopCounterfeit", new Vector3(-104.900f, -1408.530f, 28.600f) },   // The Chop Shop Counterfiet Cash Factory
            { "KortzLoading", new Vector3(-2350.420f, 261.792f, 163.564f) },   // The Kortz Center Loading Bay
            { "KortzSewer", new Vector3(-2304.079f, 218.772f, 166.829f) },   // The Kortz Center Sewer Access
            { "HackerGarage", new Vector3(738.710f, -948.670f, 24.630f) },   // Agents of Sabotage Hacker Basement Garage
            { "MoneyFrontCarWash", new Vector3(10.530f, -1405.650f, 28.200f) },   // Money Front Car Wash Office
        };

        /// <summary>Where a place is, or nothing if nobody knows.</summary>
        public static bool Of(string key, out Vector3 at)
        {
            return Where.TryGetValue(key, out at);
        }

        /// <summary>How many places have somewhere to put a blip.</summary>
        public static int Count => Where.Count;
    }
}
