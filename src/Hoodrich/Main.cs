using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Dealing;
using Hoodrich.Economy;
using Hoodrich.Gangs;
using Hoodrich.Locations;
using Hoodrich.Missions;
using Hoodrich.Social;
using Hoodrich.State;
using Hoodrich.Supply;
using Hoodrich.Territory;
using Hoodrich.UI;
using Hoodrich.Weapons;
using Hoodrich.Wheel;

namespace Hoodrich
{
    /// <summary>
    /// Script entry point and the single owner of the update loop.
    ///
    /// Hoodrich deliberately exposes ONE Script subclass. SHVDN instantiates every Script it
    /// finds and ticks them in an unspecified order; a single entry point means subsystem
    /// update order is ours to define, and there is exactly one place that has to be
    /// exception-safe.
    /// </summary>
    public sealed class Main : Script
    {
        /// <summary>
        /// Models for the woman working the lot, tried in order.
        ///
        /// Named here rather than inside Entourage because Entourage is about a gang and she is
        /// not in one -- she is somebody who works this block, which is a different fact.
        ///
        /// This list was written and then never wired to anything, so the only woman in that
        /// yard who looked like she was working was a random ambient that happened to spawn in
        /// that outfit. She has her own station now: she is there because somebody put her
        /// there, and she stays when the rest of the women become the set's.
        /// </summary>
        private static readonly string[] WorkingGirls =
        {
            "s_f_y_hooker_01", "s_f_y_hooker_02", "s_f_y_hooker_03", "a_f_y_soucent_02"
        };

        /// <summary>Cadence for work that does not need to run every frame.</summary>
        private const int SlowTickMs = 1000;

        /// <summary>Notoriety bled off per second of not being noticed.</summary>
        private const float NotorietyDecayPerSecond = 0.15f;

        /// <summary>Consecutive tick failures before the script parks itself.</summary>
        private const int MaxConsecutiveFailures = 10;

        private readonly Core.Settings _cfg;
        private readonly PlayerState _state;
        private readonly Drugs _drugs;
        private readonly GangRegistry _gangs;
        private readonly Affiliation _crew;
        private readonly TurfWatch _turf;
        private readonly Pricing _pricing;
        private readonly Cutting _cutting;
        private readonly DealerManager _dealers;
        private readonly Delivery _delivery;
        private readonly WeaponRegistry _weapons;
        private readonly Market _market;
        private readonly Bust _bust;
        private readonly DeadDrop _deadDrop;
        private readonly PostUp _postUp;
        private readonly ZoneMap _zoneMap;
        private readonly GangLeaders _leaders;
        private readonly LeaderTalk _leaderTalk;
        private readonly Conversation _talk;
        private BlockTalk _blockTalk;
        private readonly InfoPanel _info;
        private readonly StashScreen _stashScreen;
        private readonly StashHouse _stash;
        private readonly SleepSpot _sleep;
        private readonly Kitchen _kitchen;
        private readonly MissionBook _missions;
        private readonly Fixer _fixer;
        private readonly Armourer _bigj;

        /// <summary>
        /// The block, talking about itself, and the screen it is read on.
        ///
        /// The feed keeps filling whether or not the screen is open, because a timeline that
        /// only writes itself while you are looking at it is a timeline you can watch being
        /// written, and that is the one thing it must never look like.
        /// </summary>
        private readonly SocialFeed _social;
        private readonly SocialScreen _socialScreen;

        /// <summary>
        /// The people who stand near the people who matter. One each for Lamar and Stretch;
        /// their coordinates are the men's own, so the two sets never need keeping in step.
        /// </summary>
        private readonly Entourage _lamarCrew;
        private readonly Entourage _stretchCrew;
        private readonly Entourage _grimesCrew;
        private readonly Entourage _labCrew;
        private readonly Entourage _denCrew;

        /// <summary>
        /// Men who live outside.
        ///
        /// Not the set and never armed. They are here because a pill press with nobody outside
        /// it is a shutter, and the point of the place is that people come to it.
        /// </summary>
        private static readonly string[] Tramps =
        {
            "a_m_m_tramp_01", "a_m_o_tramp_01", "a_m_m_hillbilly_01",
        };

        /// <summary>
        /// Customers.
        ///
        /// The meth head model twitches on its own, which does most of the work -- the scenario
        /// only has to stop him standing to attention.
        /// </summary>
        private static readonly string[] Crackheads =
        {
            "a_m_y_methhead_01", "a_m_m_skater_01", "a_m_y_dhill_01",
        };
        /// <summary>
        /// Cars we park somewhere and leave, same idea as the scenery list.
        ///
        /// A second one should not need a second field, a second Update call, a second teardown
        /// line and a second clause in the traffic watchdog.
        /// </summary>
        private readonly List<ParkedCar> _cars = new List<ParkedCar>();

        /// <summary>The game's own metallic dark green, which is what a lowrider is painted.</summary>
        private const int MetallicDarkGreen = 49;

        /// <summary>Brighter, for a van that belongs to a shop rather than to somebody.</summary>
        private const int ShopGreen = 53;

        private readonly Entourage _party;
        private readonly Fixture _partyBarrel;
        private readonly Fixture _partyCouch;
        private readonly Fixture _stretchBox;

        /// <summary>
        /// Props we put somewhere and leave.
        ///
        /// Everything in here is the same job -- stream a model at a coordinate, hand it back
        /// on unload -- so a new one is a line in a list rather than a field, an Update call
        /// and a teardown call. The named fixtures above predate this and should drift into it.
        /// </summary>
        private readonly List<Fixture> _scenery = new List<Fixture>();

        /// <summary>The decks on the lot, and the music coming out of them.</summary>
        private readonly Boombox _decks;

        /// <summary>
        /// Women from round here.
        ///
        /// There is no female Families ped in the game. All three Families models are men, and
        /// the only female gang ped Rockstar ever made is a Balla -- so a Families woman cannot
        /// be spawned, only approximated.
        ///
        /// These are the game's own South Central women, which is who is actually on these
        /// blocks. They are spawned through the families Entourage, so they take the set's
        /// relationship group like everybody else standing here: they fight alongside you, the
        /// rivals treat them as ours, and they are in the set in every way the engine can
        /// express. The model is the one part that cannot follow.
        /// </summary>
        private static readonly string[] Women =
        {
            // The set's own, not whoever was walking past.
            //
            // This was four ambient South Central models -- and one of them, a_f_m_soucent_01,
            // is the MIDDLE-AGED variant, which is why there was a woman in her fifties working
            // the decks at a yard party.
            //
            // The game ships exactly one Families female, so all three stations are the same
            // model. That would be triplets if they were not given separate outfits, which is
            // what the component roll in SpawnMember is for -- she has several and they are
            // different enough to read as different people.
            "g_f_y_families_01",

            // If an install somehow has not got her.
            "a_f_y_soucent_01", "a_f_y_soucent_03",
        };

        /// <summary>
        /// The three Families models, one at a time, by index.
        ///
        /// Entourage rotates its model list by station so nobody is a copy of his neighbour,
        /// but there are only three faces and a lot of stations, so two men stood together
        /// landing on the same one is a coin toss. Where it matters that they are different
        /// people, they are named.
        /// </summary>
        /// <summary>
        /// Somebody dancing, and somebody working the decks.
        ///
        /// Dict then clip, tried in pairs. None of these are guaranteed -- the club sets are
        /// DLC and which of them an install has varies -- so each list ends with something
        /// older, and a station whose clips are all missing falls back to the scenario it was
        /// given instead of standing there in a T-pose.
        /// </summary>
        private static readonly string[] Dancing =
        {
            "anim@amb@nightclub@mini@dance@dance_solo@female@var_a@", "high_center",
            "anim@amb@nightclub@dancers@crowddance_facedj_11_amy@", "hi_dance_facedj_11_v2_amy",
            "mini@strip_club@idle_dance@idle_a", "idle_a_song_a"
        };

        private static readonly string[] Deejaying =
        {
            "anim@amb@nightclub@djs@dixon@", "dixn_dance_cntr_up_dix",
            "anim@amb@nightclub@djs@black_madonna@", "bmad_dance_cntr_up_bm",
            "anim@amb@nightclub@mini@dance@dance_solo@female@var_a@", "high_center"
        };

        private static string[] Fam(int which)
        {
            var all = new[] { "g_m_y_famca_01", "g_m_y_famdnf_01", "g_m_y_famfor_01" };
            return new[] { all[((which % all.Length) + all.Length) % all.Length] };
        }

        /// <summary>
        /// People who live here, and the other sets coming to take it off them.
        ///
        /// Kept together because they are the same idea from two ends: a block is only worth
        /// attacking if somebody is standing on it, and people standing on it are only
        /// interesting if somebody might come.
        /// </summary>
        private readonly CopWatch _copWatch;

        /// <summary>Keeps the street outside the house from silting up with stopped cars.</summary>
        private readonly TrafficWatch _traffic;
        private readonly Payback _payback;
        private readonly TweetToast _toasts;
        private readonly Random _rng = new Random();

        /// <summary>The couch in Lamar's courtyard. Furniture, and nothing else.</summary>
        private readonly Fixture _couch;
        private readonly Fixture _stove;
        private readonly Fixture _grimesStockA;
        private readonly Fixture _grimesStockB;
        private readonly List<InteriorDoor> _doors = new List<InteriorDoor>();
        private readonly BlockLife _block;
        private readonly GangWar _war;
        private ArmourerTalk _bigjTalk;
        private GunScreen _gunScreen;
        private DealerTalk _juanTalk;
        private readonly FixerTalk _fixerTalk;
        private readonly MissionRunner _jobs;
        private readonly CookScreen _cook;
        private readonly RadialMenu _menu;
        private readonly WheelController _wheel;

        private int _lastSlowTick;
        private int _lastSave;
        private int _failures;
        private bool _parked;

        public Main()
        {
            // Anything thrown out of a Script constructor kills the script before it ever ticks,
            // and SHVDN reports it as a bare load failure. Fail soft and park instead.
            try
            {
                // Fully qualified: Script exposes an inherited `Settings` property that would
                // otherwise win name resolution over Hoodrich.Core.Settings.
                _cfg = Core.Settings.Load();

                _drugs = Drugs.Load();
                _gangs = GangRegistry.Load();
                _dealers = DealerManager.Load(_cfg);
                _delivery = new Delivery();
                _weapons = WeaponRegistry.Load();
                _zoneMap = ZoneMap.Load();
                _missions = MissionBook.Load();

                // Everything the save writes into has to exist before the save is read.
                _state = new PlayerState();
                _crew = new Affiliation(_gangs);
                _stash = new StashHouse(_cfg);
                _sleep = new SleepSpot(_state, () => SaveGame.Save(_state, _crew, _market, _stash, true));
                _market = new Market(_cfg);

                SaveGame.Load(_state, _crew, _market, _stash);

                _turf = new TurfWatch(_gangs, _crew, _state);
                _crew.Turf = _turf;
                _bust = new Bust(_cfg, _state) { Turf = _turf };
                _deadDrop = new DeadDrop(_cfg, _state);

                _pricing = new Pricing(_cfg, _state) { Turf = _turf, Crew = _crew, Market = _market };
                _cutting = new Cutting(_state.Stash, _state);
                _cook = new CookScreen();
                _kitchen = new Kitchen(OpenKitchen, () => _cutting.IsBusy);
                _postUp = new PostUp(_cfg, _state, _pricing) { Turf = _turf, Crew = _crew, Bust = _bust };
                _bust.Post = _postUp;
                _leaders = new GangLeaders(_cfg, _gangs, _zoneMap, _crew, _state);

                _jobs = new MissionRunner(_state, _crew, _gangs, _zoneMap);

                _fixer = new Fixer(_crew);

                // The bike ride borrows him off his corner and rides him out with the rest.
                _jobs.Boss = _fixer;
                _bigj = new Armourer(_gangs);

                _social = SocialFeed.Load();
                _socialScreen = new SocialScreen(_social);

                // Two for Lamar, on their own marks -- one on watch, one smoking, because a
                // courtyard where both men are doing the same thing looks staged.
                // No standing crowds any more. Four stoops of people who did nothing mostly got
                // in the way of the fight, and the block reading as occupied during a war and
                // quiet the rest of the time is closer to true anyway.
                _block = new BlockLife(_gangs, "families");

                // Dragged out into the courtyard and left there, the way they are.
                _couch = new Fixture(new Vector3(-86.429f, -1609.917f, 31.485f), 40.880f,
                                     "prop_couch_03", "prop_couch_04", "prop_couch_01",
                                     "prop_old_couch_01", "prop_rub_couch01");

                // A stove going, a couple of metres along from the couch. Set square to it
                // rather than square to the world, so the two read as one arrangement somebody
                // made rather than two props that happen to be near each other.
                //
                // Placed by eye off the couch's own coordinate -- send a HUD readout from where
                // it should actually stand and it moves.
                _stove = new Fixture(new Vector3(-84.610f, -1611.480f, 31.470f), 40.880f,
                                     "gr_prop_gr_hobo_stove_01");

                // Grimes sells ammunition, so he gets something to sell it off. One either
                // side of where he stands and both facing the way he does, set out along the
                // line he faces rather than dropped at arbitrary angles -- a man with stock
                // laid out has arranged it; a man with two crates at odd angles has been
                // burgled.
                //
                // Placed by eye off his own coordinate, since none was given. Send a HUD
                // readout from where each should stand and they move.
                _grimesStockA = new Fixture(new Vector3(-128.073f, -1462.524f, 33.823f), 225.844f,
                                            "ex_office_swag_guns04");

                _grimesStockB = new Fixture(new Vector3(-130.301f, -1460.226f, 33.823f), 225.844f,
                                            "ex_office_swag_guns02");

                // Grimes has people now. His corner is one of the three a raid comes for and
                // he was stood on it alone, which is not how anybody holds anything.
                _grimesCrew = new Entourage(_gangs, "families",
                                            new Vector3(-129.187f, -1461.375f, 33.823f),
                                            225.844f, "Grimes")
                    .Stand(new Vector3(-128.394f, -1458.440f, 33.823f), 225.844f,
                           "WORLD_HUMAN_GUARD_STAND");

                foreach (var spec in _cfg.Doors) _doors.Add(new InteriorDoor(spec));

                // The lab has people on it.
                _labCrew = new Entourage(_gangs, "families",
                                         new Vector3(-201.384f, -1707.909f, 32.664f),
                                         313.362f, "the lab")

                    // On the shutter, with the rifle.
                    .Stand(new Vector3(-197.729f, -1712.040f, 32.664f), 138.478f,
                           "WORLD_HUMAN_GUARD_STAND", Fam(0))

                    // Round the side on a beer.
                    .Stand(new Vector3(-204.729f, -1710.548f, 32.664f), 243.455f,
                           "WORLD_HUMAN_DRINKING", Fam(1), armed: false)

                    // And his mate, on a cigarette.
                    .Stand(new Vector3(-204.190f, -1711.620f, 32.664f), 243.455f,
                           "WORLD_HUMAN_SMOKING", Fam(2), armed: false)

                    // Off the couch, stood on the concrete with a drink, facing the fire.
                    //
                    // He used to be sat on the cushion, which needed the onProp flag to stop
                    // the ground probe overwriting the seat height with the floor under it.
                    // On his feet none of that applies: he wants the floor, so the probe is
                    // exactly right and the flag comes off with the height.
                    //
                    // Forward 1.4m along his own heading, which walks him out from the couch
                    // into the group rather than leaving him pressed against the arm of it.
                    .Stand(new Vector3(-201.910f, -1724.694f, 32.664f), 295.109f,
                           "WORLD_HUMAN_DRINKING", Fam(1), armed: false)

                    // Stood in the group, talking. HANG_OUT_STREET is the loose-limbed
                    // gesturing idle the game uses for people in a conversation -- MOBILE is
                    // the other candidate and puts a phone in her hand, which is somebody
                    // ignoring the party rather than at it.
                    // Turning a pistol over in his hands. Armed, but with a pistol rather than
                    // the block's usual rifle -- a man at a party with a choppa out is not at
                    // the party.
                    .Stand(new Vector3(-203.980f, -1730.601f, 32.664f), 58.749f,
                           "WORLD_HUMAN_GUARD_STAND", Fam(1), weapon: "WEAPON_PISTOL")

                    // On the wall at the back of the lot, smoking, looking out at the freeway.
                    .Stand(new Vector3(-205.225f, -1732.264f, 32.664f), 315.516f,
                           "WORLD_HUMAN_SMOKING", Fam(2), armed: false)

                    // Working the party. Not one of the set and not armed, same as the one in
                    // Lamar's courtyard -- a block with nobody on it but soldiers is a barracks.
                    .Stand(new Vector3(-202.367f, -1728.077f, 32.664f), 229.080f,
                           "WORLD_HUMAN_PROSTITUTE_HIGH_CLASS", WorkingGirls, armed: false)

                    // Out front of the shop, not carrying. Entourage leaves permanent events
                    // unblocked, so he ducks at gunfire and reacts to being shoved like anybody
                    // else -- a man stood outside a shop who does not flinch is furniture.
                    .Stand(new Vector3(-186.874f, -1700.152f, 32.920f), 308.772f,
                           "WORLD_HUMAN_GUARD_STAND", Fam(0), armed: false);

                // And their car outside it. The Voodoo is the lowrider, and it is painted the
                // exact green the set is drawn in everywhere else rather than a paint index
                // that is roughly right.
                // Paint 49 is metallic dark green -- the set's colour with flake in it rather
                // than the flat poster green an RGB triple gives you.
                _cars.Add(new ParkedCar(new Vector3(-196.745f, -1718.838f, 32.664f), 319.530f,
                                        MetallicDarkGreen,
                                        "voodoo", "buccaneer2", "chino2"));

                // The shop's van, up on the road above the lot.
                _cars.Add(new ParkedCar(new Vector3(-214.160f, -1739.805f, 31.709f), 52.137f,
                                        ShopGreen,
                                        "youga2", "youga", "surfer", "burrito3"));

                // The lot behind the lab, of an evening.
                //
                // Everybody is placed on a ring around the fire and turned to face it, worked
                // out from the fire's own coordinate rather than typed one at a time -- people
                // stood round a fire stand round it evenly and look at it, and five hand-picked
                // positions never quite land on a circle.
                //
                // The women are ambient South Central models rather than the set's own. There
                // is no female Families ped in the game; all three are men. They are at the
                // party rather than in it, which is also how a party works.
                _party = new Entourage(_gangs, "families",
                                       new Vector3(-199.604f, -1728.764f, 32.664f), 186.646f, "the lot")

                    .Stand(new Vector3(-199.196f, -1726.450f, 32.664f), 190.000f,
                           "WORLD_HUMAN_DRINKING", Fam(0), armed: false)

                    .Stand(new Vector3(-197.277f, -1728.437f, 32.664f), 262.000f,
                           "WORLD_HUMAN_SMOKING", Fam(1), armed: false)

                    // Dancing, not "partying". WORLD_HUMAN_PARTYING is somebody holding a
                    // drink and nodding; this is somebody actually moving to what is coming
                    // out of the decks, which is the difference between a yard with people in
                    // it and a yard with a party in it.
                    .Stand(new Vector3(-198.072f, -1730.828f, 32.664f), 130.367f,
                           "WORLD_HUMAN_PARTYING", Women, armed: false, anim: Dancing)

                    .Stand(new Vector3(-201.294f, -1730.396f, 32.664f), 46.000f,
                           "WORLD_HUMAN_SMOKING_POT", Fam(2), armed: false)

                    .Stand(new Vector3(-201.679f, -1727.661f, 32.664f), 118.000f,
                           "WORLD_HUMAN_DRINKING", Women, armed: false)

                    // On the decks. Facing the yard, which is the direction the music goes.
                    .Stand(new Vector3(-194.279f, -1723.069f, 32.664f), 148.079f,
                           "WORLD_HUMAN_MUSICIAN", Women, armed: false, anim: Deejaying);

                // The barrel IS the fire -- it burns on its own. A camp fire was stacked on
                // top of it as well, which is two fires a metre apart and reads as a bug even
                // before you notice the logs floating.
                _partyBarrel = new Fixture(new Vector3(-199.604f, -1728.764f, 32.664f), 0f,
                                           "gr_prop_gr_hobo_stove_01", "prop_barrel_02a");

                _partyCouch = new Fixture(new Vector3(-202.400f, -1727.000f, 32.664f), 118.000f,
                                          "prop_couch_03", "prop_old_couch_01", "prop_rub_couch01");

                // Weight sitting by Stretch's door, which is the whole reason anybody goes to
                // that door. Fallbacks behind it: the Bikers bag and then a plain crate, so an
                // install without the newer DLC gets something rather than nothing.
                // Turned off square with the wall. At 2.4 degrees the stack sat parallel to it
                // and the corner of the top box went through the render; twenty-odd degrees is
                // enough to clear it and reads as boxes somebody put down rather than boxes
                // somebody aligned.
                _stretchBox = new Fixture(new Vector3(-162.562f, -1637.442f, 34.029f), 24.500f,
                                          "m24_2_prop_m42_weedboxpile_01a",
                                          "bkr_prop_weed_bigbag_01a",
                                          "prop_boxpile_07d");

                // Plants growing in the yard behind the lab, in a row down the wall where
                // Michael marked them. An array rather than three fields: they are one thing
                // that happens to be three props, and a fourth should not need a new field, an
                // Update line and a teardown line to exist.
                foreach (var plant in new[]
                {
                    new Vector3(-209.880f, -1712.280f, 32.664f),
                    new Vector3(-211.089f, -1713.247f, 32.669f),
                    new Vector3(-212.390f, -1714.471f, 32.664f),
                })
                {
                    _scenery.Add(new Fixture(plant, 238.0f, "sf_prop_sf_weed_med_01a",
                                             "bkr_prop_weed_med_01a", "prop_weed_02"));
                }

                // Decks in the yard, and something coming out of them.
                // Turned round to face the yard rather than the fence.
                _decks = new Boombox(new Vector3(-194.813f, -1723.732f, 32.664f), 317.977f,
                                     "sf_prop_sf_dj_desk_01a",
                                     "ch_prop_ch_turntable_01a",
                                     "prop_dj_deck_01");

                // A table on the lot, because a party with a fire and no table is a vigil.
                _scenery.Add(new Fixture(new Vector3(-203.915f, -1726.602f, 32.664f), 114.060f,
                                         "prop_protest_table_01",
                                         "prop_table_04",
                                         "prop_table_03"));

                // And another box of weight against the back wall, by the shutter.
                _scenery.Add(new Fixture(new Vector3(-205.039f, -1708.503f, 32.664f), 217.911f,
                                         "m24_2_prop_m42_weedboxpile_01a",
                                         "bkr_prop_weed_bigbag_01a",
                                         "prop_boxpile_07d"));

                // Somebody outside the pill press, which is what a place like that has outside it.
                //
                // His own crew because there is nothing else within a leash of here -- Grimes
                // is sixty metres off and Lamar is two hundred. He takes the set's relationship
                // group like anything an Entourage spawns, which for a man sat on a kerb means
                // only that our lot leave him alone.
                _denCrew = new Entourage(_gangs, "families",
                                         new Vector3(-105.053f, -1408.631f, 29.673f),
                                         226.934f, "the den")
                    .Stand(new Vector3(-95.682f, -1411.403f, 29.490f), 352.017f,
                           "WORLD_HUMAN_BUM_STANDING", Tramps, armed: false)

                    // On the shutter itself, with the rifle. This is the door, so this is the
                    // one that gets held rather than watched.
                    .Stand(new Vector3(-102.389f, -1408.656f, 29.598f), 186.552f,
                           "WORLD_HUMAN_GUARD_STAND", Fam(0))

                    // And one round the corner by the bins, on a beer.
                    .Stand(new Vector3(-99.879f, -1409.827f, 29.535f), 118.828f,
                           "WORLD_HUMAN_DRINKING", Fam(1), armed: false)

                    // Another by the bins on a cigarette, facing the street.
                    .Stand(new Vector3(-100.969f, -1412.268f, 29.588f), 2.755f,
                           "WORLD_HUMAN_SMOKING", Fam(2), armed: false)

                    // A customer, out on the pavement. STAND_IMPATIENT rather than a bum idle:
                    // it is the shifting, fidgeting, cannot-keep-still one, and on the meth
                    // head model -- which twitches on its own -- it reads as somebody waiting
                    // on a door rather than somebody sleeping by it.
                    .Stand(new Vector3(-96.049f, -1408.985f, 29.503f), 243.667f,
                           "WORLD_HUMAN_STAND_IMPATIENT", Crackheads, armed: false);

                // And a car of ours round the side.
                _cars.Add(new ParkedCar(new Vector3(-110.187f, -1414.896f, 29.975f), 39.782f,
                                        MetallicDarkGreen,
                                        "buccaneer2", "voodoo", "chino2", "primo2"));

                _copWatch = new CopWatch();

                _traffic = new TrafficWatch()
                {
                    // The plug is parked there because he was told to park there.
                    // The plug is parked there because he was told to park there, and a
                    // carload that came for something you posted is stopped for a reason too.
                    Ours = car => (_delivery != null && _delivery.IsActive &&
                                   _delivery.Car != null && car != null &&
                                   car.Handle == _delivery.Car.Handle)
                                  || (_payback != null && _payback.Owns(car))
                                  || OurParkedCar(car)
                                  || (_decks != null && _decks.Owns(car))
                };

                _payback = new Payback(_gangs);

                _toasts = new TweetToast
                {
                    Enabled = _cfg.TweetsOnTheRight,

                    // Not over a full-screen UI. They keep queueing and keep ageing while it is
                    // up, so nothing is lost -- they are simply not drawn across a menu.
                    Hidden = () => _wheel.IsOpen || _socialScreen.IsOpen || _stashScreen.IsOpen
                                   || _info.IsOpen || _talk.IsOpen || _cook.IsOpen
                                   || _gunScreen.IsOpen,
                };

                _social.Toasts = _toasts;

                _war = new GangWar(_gangs, _crew, _state)
                    .Defend("Lamar", Fixer.Spot)
                    .Defend("Grimes", new Vector3(-129.187f, -1461.375f, 33.823f));

                _lamarCrew = new Entourage(_gangs, "families", Fixer.Spot, 206f, "Lamar")
                    .Stand(new Vector3(-82.4f, -1613.8f, 31.485f), 120f, "WORLD_HUMAN_GUARD_STAND")
                    .Stand(new Vector3(-89.1f, -1607.9f, 31.485f), 250f, "WORLD_HUMAN_SMOKING_POT")

                    // Somebody working the courtyard. Not one of the set and not armed -- she
                    // is here because a block with nobody on it but soldiers is a barracks.
                    .Stand(new Vector3(-84.832f, -1609.433f, 31.485f), 237.436f,
                           "WORLD_HUMAN_PROSTITUTE_HIGH_CLASS", WorkingGirls, armed: false)

                    // On the steps at the front with a rifle, watching the way in.
                    .Stand(new Vector3(-95.614f, -1614.153f, 32.314f), 23.701f,
                           "WORLD_HUMAN_GUARD_STAND")

                    // And one on a beer by the pool.
                    .Stand(new Vector3(-87.599f, -1607.578f, 32.312f), 102.240f,
                           "WORLD_HUMAN_DRINKING", armed: false)

                    // Round the back, covering the other way in.
                    .Stand(new Vector3(-73.146f, -1617.436f, 31.469f), 243.053f,
                           "WORLD_HUMAN_GUARD_STAND");

                // One for Stretch, on the spot it was read off the HUD at.
                var stretch = _leaders.Get("families");
                _stretchCrew = stretch == null
                    ? null
                    : new Entourage(_gangs, "families",
                                    new Vector3(stretch.SpotX, stretch.SpotY, stretch.SpotZ),
                                    stretch.Heading, stretch.Name)
                        .Stand(new Vector3(-161.084f, -1635.432f, 34.029f), 70.469f, "WORLD_HUMAN_GUARD_STAND")

                        // Two of theirs round the side, not doing anything in particular.
                        .Stand(new Vector3(-162.494f, -1630.635f, 33.639f), 85.456f,
                               "WORLD_HUMAN_DRINKING", armed: false)
                        .Stand(new Vector3(-165.765f, -1630.464f, 33.655f), 288.636f,
                               "WORLD_HUMAN_SMOKING_POT", armed: false)

                        // On the gate, one of the set, and not carrying. He was a dealer
                        // model doing the dealing idle, then a rifle guard -- both wrong for a
                        // gate you walk through to talk to somebody. A man stood at the front
                        // of a courtyard is watching who comes in, not holding a position.
                        .Stand(new Vector3(-172.341f, -1632.777f, 33.463f), 101.654f,
                               "WORLD_HUMAN_GUARD_STAND", armed: false)

                        // Down the south end, facing back up the walkway.
                        .Stand(new Vector3(-159.851f, -1681.244f, 36.966f), 181.575f,
                               "WORLD_HUMAN_GUARD_STAND")

                        // And one on a beer round the front, doing nothing at all.
                        .Stand(new Vector3(-149.837f, -1696.416f, 32.872f), 49.515f,
                               "WORLD_HUMAN_DRINKING", armed: false)

                        // Somebody on a joint a few steps off him, so the two of them read as
                        // people stood about together rather than two separate installations.
                        .Stand(new Vector3(-150.441f, -1694.220f, 32.872f), 153.053f,
                               "WORLD_HUMAN_SMOKING_POT", armed: false);

                if (stretch != null)
                {
                    _war.Defend(stretch.Name, new Vector3(stretch.SpotX, stretch.SpotY, stretch.SpotZ));
                }


                // Handed over as functions rather than references, so the feed never holds on
                // to a system that can be torn down under it.
                _social.WhereYouAre = ZoneNameHere;
                _social.StreetYouAre = StreetNameHere;
                _social.YourGang = () => _crew.IsAffiliated ? _crew.Current.Name : "";
                _social.Changed = () => { _state.Followers = _social.Followers; _state.Touch(); };

                _social.Start(_state.Followers);

                _state.RankedUp = rank => _social.On(SocialEvent.RankUp);

                _jobs.Social = _social;
                _war.Social = _social;
                _war.Busy = () => _jobs != null && _jobs.IsRunning;

                // Whose block you are stood on, so a war you start yourself knows it is being
                // started on theirs.
                _war.Turf = _turf;

                // Three of theirs in five seconds on their own turf and they come for you.
                // Affiliation already works out which set a body belonged to and refuses to
                // count the same one twice, so the war system listens to that rather than
                // running a second scan of its own.
                _crew.RivalDropped = gang => _war.RivalDropped(gang);

                // Not in the middle of a job. It keeps waiting rather than being cancelled --
                // the debt does not expire because you happened to be working when it came due.
                _payback.Busy = () => (_jobs != null && _jobs.IsRunning)
                                      || (_war != null && _war.IsRunning);
                _copWatch.Social = _social;
                _postUp.Social = _social;
                _crew.Social = _social;

                _talk = new Conversation();

                // The context key pointed at people rather than at places: a nod for one of
                // yours in passing, a conversation with one who is posted up, and something to
                // say over anybody on the pavement.
                _blockTalk = new BlockTalk(_crew, _talk)
                {
                    // Not mid-raid either. Your own defenders are stood right there and a
                    // prompt offering to nod at one of them while they are being shot at is
                    // the wrong thing on screen.
                    Busy = () => (_jobs != null && _jobs.IsRunning) ||
                                 (_war != null && _war.IsRunning),

                    // Everybody on this block is ALSO somebody. Lamar has two men stood with
                    // him and Stretch has two more, so without this, walking up to Lamar offers
                    // a nod at his hanger-on instead of the work he is holding.
                    Suppressed = () =>
                        (_fixer != null && _fixer.InReach) ||
                        (_bigj != null && _bigj.InReach) ||
                        (_kitchen != null && _kitchen.InReach) ||
                        (_sleep != null && _sleep.InReach) ||
                        (_delivery != null && _delivery.IsActive) ||
                        (_postUp != null && _postUp.IsPosted)
                };

                // The bike job runs its own exchange on the court, so it needs the same screen.
                _jobs.Talk = _talk;

                _info = new InfoPanel();
                _stashScreen = new StashScreen();
                _leaderTalk = new LeaderTalk(_leaders, _gangs, _crew, _state, _drugs, _pricing, _cfg);
                _leaders.Talk = _talk;
                _leaders.TalkBuilder = def => _leaderTalk.Root(def);
                _leaderTalk.Social = _social;

                _fixerTalk = new FixerTalk(_fixer, _missions, _jobs, _crew, _state);
                _fixer.Talk = _talk;
                _fixer.TalkBuilder = () => _fixerTalk.Root();
                _fixerTalk.BlockUnderAttack = () => _war != null && _war.IsRunning;

                _bigjTalk = new ArmourerTalk(_bigj, _crew, _state);

                // The rack is a screen now. The conversation is still how you get to it -- you
                // walk up to a man and he says something -- but what he shows you once you have
                // asked is a laid-out stock list rather than five pages of dialogue choices.
                _gunScreen = new GunScreen(_state);
                _bigjTalk.Rack = () => _gunScreen.Open();
                _bigj.Talk = _talk;
                _bigj.TalkBuilder = () =>
                {
                    // Not "the table". There is no table -- he stands in a courtyard next to
                    // a couple of crates -- and a screen naming furniture that is not there is
                    // the sort of small wrongness that makes the whole thing read as written
                    // rather than seen.
                    _talk.Title = _bigj.Name + " -- what he's got";
                    _talk.TheirVoice = ArmourerTalk.Voice;
                    return _bigjTalk.Root();
                };
                _juanTalk = new DealerTalk(_delivery, _drugs, _pricing, _state, _crew)
                {
                    House = _stash.Stash
                };

                _delivery.Talk = _talk;
                _delivery.TalkBuilder = () => _juanTalk.Root();

                // He delivers to an address, so he needs the address -- and the only place you
                // can call him from is standing at it.
                _delivery.AtHome = () => _stash.AtDoor;
                _dealers.AtHome = () => _stash.AtDoor;
                _delivery.HouseDoor = _stash.Position;
                _delivery.House = _stash.Stash;

                var pages = new WheelPages(_cfg, _state, _drugs, _pricing, _cutting,
                                           _gangs, _crew, _turf, _dealers, _weapons,
                                           _stash, _postUp, _leaders);

                pages.ShowVanillaWheel = () => _wheel.ShowVanillaWheel();
                pages.Info = _info;
                pages.Delivery = _delivery;
                pages.StashScreen = _stashScreen;
                pages.ShowSocials = () => _socialScreen.Open();
                pages.Followers = () => _social.Followers;
                pages.WipeSocials = () => _social.Wipe();

                // The feed screen posts now, so it gets what the wheel page used to hold. The
                // wedge is a door and nothing else.
                _socialScreen.Gangs = _gangs;
                _socialScreen.Crew = _crew;
                _socialScreen.War = _war;
                _socialScreen.PaybackDue = () => _payback != null && _payback.IsOwed;

                _socialScreen.Say = set => _social.PostAsYou(set, "") != null;

                // Naming a set does three things at once and they have to happen together: the
                // post goes up, they answer it on the feed, and somebody starts driving.
                _socialScreen.Diss = id =>
                {
                    var gang = _gangs.Get(id);
                    if (gang == null) return false;

                    var said = _social.PostAsYou("YouDiss" + Pretty(id), gang.Name);
                    if (said == null) return false;

                    _social.Dissed(gang.Id, gang.Name, 2 + _rng.Next(3));
                    _payback.Owed(gang.Id);

                    // And it costs you with them. Enough of it and they cross into beef on
                    // their own, without anybody declaring anything -- which is the only way to
                    // make an enemy of somebody who was not one.
                    _crew.Taunted(gang.Id);
                    return true;
                };

                // Two other gangs with a problem, picked fresh each time.
                //
                // Every gang with rivals is a candidate, including the ones Franklin has
                // nothing to do with -- Cheng's people and Simeon's people being rude about
                // each other is the city carrying on without him, which is the whole point.
                // What the block reckons of the product, for the posts that talk about it.
                _social.ProductRep = () => _state == null ? 0.5f : _state.ProductRep;

                _social.BickerPair = () =>
                {
                    var speakers = new List<GangDef>();
                    foreach (var g in _gangs.All)
                    {
                        if (g != null && g.Rivals.Count > 0) speakers.Add(g);
                    }

                    if (speakers.Count == 0) return null;

                    var who = speakers[_rng.Next(speakers.Count)];
                    var about = _gangs.Get(who.Rivals[_rng.Next(who.Rivals.Count)]);

                    return about == null ? null : new[] { who.Id, about.Name };
                };

                // The tickers that used to sit here are gone with them. Those exact words are
                // in the note strip now, a hair under the cursor, on a screen that is open and
                // being read -- and two channels saying one sentence is noise.

                pages.PaybackDue = () => _payback != null && _payback.IsOwed;



                _menu = new RadialMenu(_cfg);
                _wheel = new WheelController(_cfg, _menu, pages.BuildRoot);

                Interval = 0;
                Tick += OnTick;
                Aborted += OnAborted;

                Log.Info("Paths: data=" + Paths.Data + "  writable=" + Paths.Writable);

                Log.Info("Hoodrich " + Build.Version + " loaded. Wheel: " +
                         (_cfg.WheelMode == WheelMode.Replace
                             ? "weapon-wheel button"
                             : _cfg.WheelKey.ToString()) + ".");
            }
            catch (Exception ex)
            {
                _parked = true;
                Log.Error("Hoodrich failed to initialise and is disabled for this session.", ex);
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (_parked || _cfg == null || !_cfg.Enabled) return;

            try
            {
                Draw.BeginFrame();

                // Before every early return below. The cards age inside Draw, so a full-screen
                // UI that returns early would freeze the stack rather than hide it -- and you
                // would close a menu to find four tweets from a minute ago still sat there. It
                // skips the actual drawing while a UI is up on its own.
                _toasts.Draw();

                var available = IsPlayable();

                WatchForPillbox();
                WatchForGunfire();

                // Before any of the full-screen UIs, every one of which returns early. A narc
                // on the phone and a corner you are stood on both run on wall time, and a
                // countdown that stops because you opened a menu is a countdown you can beat by
                // opening a menu.
                _bust.Update();
                _postUp.Update();

                // Moving product owns the screen outright, the same as any other full UI.
                if (_stashScreen.IsOpen)
                {
                    if (!available || !_stash.AtDoor) _stashScreen.Close();
                    else
                    {
                        _stashScreen.Update();
                        _stashScreen.Draw();
                        SlowTick();
                        _failures = 0;
                        return;
                    }
                }

                // The rack owns the screen the same way the kitchen does. Without this the
                // wheel could be opened on top of it, both would fight over up and down, and
                // every walk-up prompt in the mod would carry on showing behind it.
                if (_gunScreen.IsOpen)
                {
                    if (!available || !_bigj.InReach) _gunScreen.Close();
                    else
                    {
                        _gunScreen.Update();
                        _gunScreen.Draw();
                        SlowTick();
                        _failures = 0;
                        return;
                    }
                }

                // Working product owns the screen while the choice is being made.
                if (_cook.IsOpen)
                {
                    if (!available || !_kitchen.InReach) _cook.Close();
                    else
                    {
                        _cook.Update();
                        _cook.Draw();
                        SlowTick();
                        _failures = 0;
                        return;
                    }
                }

                // The feed owns the screen like every other full UI. It was the one screen that
                // did not, so the wheel could be opened on top of it, both fought over up and
                // down, and every walk-up prompt in the mod carried on showing behind it.
                if (_socialScreen.IsOpen)
                {
                    if (!available) _socialScreen.Close();
                    else
                    {
                        _socialScreen.Update();
                        _socialScreen.Draw();

                        // The block keeps talking while you read it, which is the entire point.
                        _social.Update();

                        SlowTick();
                        _failures = 0;
                        return;
                    }
                }

                // A popup readout owns the screen the same way a conversation does: the wheel
                // would fight it for the same buttons.
                if (_info.IsOpen)
                {
                    if (!available) _info.Close();
                    else
                    {
                        _info.Update();
                        _info.Draw();
                        SlowTick();
                        _failures = 0;
                        return;
                    }
                }


                // A conversation owns the screen while it is up: the wheel would fight it for
                // the same buttons, and you cannot be talking to a man and shopping at once.
                if (_talk.IsOpen)
                {
                    if (!available || WalkedAwayFromTalk())
                    {
                        _talk.Close();
                        _leaders.ReleaseFromTalk();
                        _fixer.ReleaseFromTalk();
                        _bigj.ReleaseFromTalk();
                    }
                    else
                    {
                        _talk.Update();
                        _talk.Draw();
                        SlowTick();
                        _failures = 0;
                        return;
                    }
                }

                _wheel.Update(available);

                if (available)
                {
                    _crew.Update();
                    _turf.Update();
                    _dealers.Update(_turf, _crew, _state);
                    _dealers.GreetIfNeeded();
                    _delivery.Update();
                    _delivery.UpdatePrompt();
                    _cutting.Update();
                    _deadDrop.Update();
                    _market.Update(_drugs);
                    _stash.Update();
                    _sleep.Update();
                    _kitchen.Update();
                    _sleep.RestoreOnLoad();
                    _leaders.Update();
                    _leaders.UpdatePrompt();
                    _fixer.Update();
                    _fixer.UpdatePrompt();
                    _blockTalk.Update();
                    _bigj.Update();
                    _bigj.UpdatePrompt();

                    _social.Update();
                    _copWatch.Update();
                    _block.Update();
                    _couch.Update();
                    _stove.Update();
                    _grimesStockA.Update();
                    _grimesStockB.Update();
                    foreach (var door in _doors) door.Update();
                    _traffic.Update();
                    _payback.Update();
                    _war.Update();

                    _lamarCrew.Update();
                    if (_stretchCrew != null) _stretchCrew.Update();
                    _grimesCrew.Update();
                    _labCrew.Update();
                    _denCrew.Update();
                    foreach (var car in _cars) car.Update();
                    _party.Update();
                    _partyBarrel.Update();
                    _partyCouch.Update();
                    _stretchBox.Update();
                    foreach (var prop in _scenery) prop.Update();
                    _decks.Update();
                    _jobs.Update();
                }

                // Everything that writes across the top of the screen stands down while the
                // wheel is up. The wheel is modal and puts its own readout there, so a raid
                // banner and a mission objective underneath it is three things in one place --
                // which is exactly what it looked like.
                if (!_wheel.IsOpen)
                {
                    _war.Draw();
                    _jobs.Draw();
                }

                _cutting.Draw();
                _bust.Draw();
                _postUp.Draw();
                _stash.Draw();
                _sleep.Draw();
                _kitchen.Draw();

                // Last, deliberately. Everything above owns a specific spot and has first
                // claim on the key; this is whoever happens to be stood there otherwise.
                _blockTalk.Draw();

                SlowTick();

                _failures = 0;
            }
            catch (Exception ex)
            {
                _failures++;
                Log.Error("Tick failed (" + _failures + "/" + MaxConsecutiveFailures + ").", ex);

                // Never leave the world in a modified state because of our own bug.
                TryRestore();

                if (_failures >= MaxConsecutiveFailures)
                {
                    _parked = true;
                    Log.Error("Too many consecutive failures; Hoodrich is parked for this session.");
                    Notify.Failure("Hoodrich shut itself off for this session. Check the log.");
                }
            }
        }

        private void SlowTick()
        {
            var now = Game.GameTime;
            if (now - _lastSlowTick < SlowTickMs) return;

            var elapsedSeconds = (now - _lastSlowTick) / 1000f;
            _lastSlowTick = now;

            // Heat only cools while you are not actively drawing attention.
            if (_state.Notoriety > 0f && !_bust.CallInProgress && !_turf.IsExposed)
            {
                _state.AddNotoriety(-NotorietyDecayPerSecond * Math.Min(elapsedSeconds, 5f));
            }

            _postUp.Prune();
            _turf.Prune();

            if (_cfg.SaveIntervalSeconds > 0 && now - _lastSave >= _cfg.SaveIntervalSeconds * 1000)
            {
                _lastSave = now;
                SaveGame.Save(_state, _crew, _market, _stash);
            }
        }

        /// <summary>
        /// Ends a conversation you have walked out of. Talking is a thing done at arm's length,
        /// so a dialogue box that follows you down the street would be a bug, not a feature.
        /// </summary>
        private bool WalkedAwayFromTalk()
        {
            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return true;

            var subject = _talk.Subject as LeaderDef;
            if (subject == null) return false;

            // Measured to the MAN, on the ground plane. Measuring to his authored spot closed
            // the conversation the instant it opened: that spot's height is probed from across
            // the map and is often still zero, so a player standing 30m above sea level was
            // "30 metres away" from somebody they were stood next to.
            return _leaders.DistanceTo(subject) > 8f;
        }

        /// <summary>
        /// Waking up at Pillbox.
        ///
        /// Watched on the WAKE rather than on the death. You are out cold for the fade, nothing
        /// on screen means anything while it is black, and a post that lands during it is a post
        /// nobody sees. He stands back up, and that is also about when word would have got
        /// round -- somebody always knows before you are out of the bed.
        /// </summary>
        private void WatchForPillbox()
        {
            try
            {
                var player = Game.Player.Character;
                if (player == null || !player.Exists()) return;

                if (player.IsDead)
                {
                    // Read on the frame he goes down, not when he wakes up. The game clears
                    // the source and the cause once the ped is respawned, so waiting until
                    // Pillbox means asking a question that no longer has an answer.
                    if (!_wasDown) RememberWhoGotYou(player);

                    _wasDown = true;
                    _pillboxAt = 0;
                }
                else if (_wasDown)
                {
                    _wasDown = false;

                    // Not the instant he is upright: the fade is still on the way out and a
                    // notification behind a black screen is a notification thrown away.
                    _pillboxAt = Game.GameTime + PillboxDelayMs;
                }
            }
            catch
            {
                _wasDown = false;
            }

            // Deliberately outside the branch above. Arming the timer clears _wasDown, so a
            // version of this that returned early on "not down" would set the clock and then
            // never look at it again.
            if (_pillboxAt == 0 || Game.GameTime < _pillboxAt) return;

            _pillboxAt = 0;
            _social?.On(SocialEvent.Hospital, "");

            // And whoever did it, still talking about it. Your people worrying and theirs
            // laughing, which is how waking up in Pillbox would actually arrive.
            if (_social != null && !string.IsNullOrEmpty(_killedByGang))
            {
                _social.RivalGang = _killedByGang;
                _social.WastedHow = _killedHow;
                _social.On(SocialEvent.WastedBy, _killedByName);

                _killedByGang = "";
            }
        }

        private string _killedByGang = "";
        private string _killedByName = "";
        private string _killedHow = "shot";

        /// <summary>
        /// Works out who put you down and what with.
        ///
        /// GET_PED_SOURCE_OF_DEATH gives the entity, which is the only way to know whose set to
        /// hand the gloating to -- and GET_PED_CAUSE_OF_DEATH gives the weapon, which decides
        /// how they tell it. Being shot and being beaten with something are not the same story.
        ///
        /// A killer who is nobody's -- traffic, a fall, the police -- leaves the gang empty and
        /// nothing is posted, because a set that had nothing to do with it has nothing to say.
        /// </summary>
        private void RememberWhoGotYou(Ped player)
        {
            _killedByGang = "";
            _killedByName = "";
            _killedHow = "shot";

            try
            {
                var killer = Function.Call<int>(Hash.GET_PED_SOURCE_OF_DEATH, player.Handle);
                if (killer == 0 || killer == player.Handle) return;

                var ped = Entity.FromHandle(killer) as Ped;
                if (ped == null || !ped.Exists()) return;

                var gang = _crew == null ? null : _crew.GangOf(ped);
                if (gang == null) return;

                // Your own set killing you is not a set to gloat about it.
                if (_crew.IsAffiliated &&
                    string.Equals(gang.Id, _crew.Current.Id, StringComparison.OrdinalIgnoreCase)) return;

                _killedByGang = gang.Id;
                _killedByName = gang.Name;
                _killedHow = HowTheyGotYou(player);

                Log.Info("Wasted by " + gang.Id + " (" + _killedHow + ").");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not work out who got you: " + ex.Message);
                _killedByGang = "";
            }
        }

        /// <summary>
        /// The weapon, bucketed into the four ways this ends that are worth telling apart.
        ///
        /// Hashes rather than names: the cause of death is a weapon hash and there is no native
        /// that turns one back into a string, so the handful that matter are compared directly
        /// and everything else is a gun.
        /// </summary>
        private static string HowTheyGotYou(Ped player)
        {
            try
            {
                var cause = Function.Call<uint>(Hash.GET_PED_CAUSE_OF_DEATH, player.Handle);

                foreach (var name in MeleeCauses)
                {
                    if (cause == (uint)Function.Call<int>(Hash.GET_HASH_KEY, name)) return "melee";
                }

                foreach (var name in BlastCauses)
                {
                    if (cause == (uint)Function.Call<int>(Hash.GET_HASH_KEY, name)) return "blast";
                }

                foreach (var name in CarCauses)
                {
                    if (cause == (uint)Function.Call<int>(Hash.GET_HASH_KEY, name)) return "car";
                }
            }
            catch
            {
                // A gun is the common case and a safe default.
            }

            return "shot";
        }

        private static readonly string[] MeleeCauses =
        {
            "WEAPON_UNARMED", "WEAPON_KNIFE", "WEAPON_BAT", "WEAPON_CROWBAR", "WEAPON_MACHETE",
            "WEAPON_SWITCHBLADE", "WEAPON_KNUCKLE", "WEAPON_GOLFCLUB", "WEAPON_HAMMER",
            "WEAPON_HATCHET", "WEAPON_NIGHTSTICK", "WEAPON_WRENCH", "WEAPON_BOTTLE",
            "WEAPON_DAGGER", "WEAPON_POOLCUE", "WEAPON_BATTLEAXE", "WEAPON_STONE_HATCHET"
        };

        private static readonly string[] BlastCauses =
        {
            "WEAPON_GRENADE", "WEAPON_STICKYBOMB", "WEAPON_MOLOTOV", "WEAPON_PIPEBOMB",
            "WEAPON_RPG", "WEAPON_GRENADELAUNCHER", "WEAPON_EXPLOSION", "WEAPON_FIRE"
        };

        private static readonly string[] CarCauses =
        {
            "WEAPON_RUN_OVER_BY_CAR", "WEAPON_RAMMED_BY_CAR", "WEAPON_VEHICLE_ROCKET"
        };

        /// <summary>
        /// The road he is standing on, for a post that names where it happened.
        ///
        /// Deliberately the street rather than the district. "Down Forum Dr" is what somebody
        /// types; "in Davis" is what a news report says, and the feed already has a slot for
        /// that.
        /// </summary>
        private static string StreetNameHere()
        {
            try
            {
                var player = Game.Player.Character;
                if (player == null || !player.Exists()) return "";

                var here = player.Position;

                var street = new OutputArgument();
                var crossing = new OutputArgument();

                Function.Call(Hash.GET_STREET_NAME_AT_COORD, here.X, here.Y, here.Z,
                              street, crossing);

                var hash = street.GetResult<int>();
                if (hash == 0) return "";

                return Function.Call<string>(Hash.GET_STREET_NAME_FROM_HASH_KEY, hash) ?? "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// Somebody always hears it.
        ///
        /// Every other thing on the feed is something Franklin did to somebody. This is the one
        /// everybody else experiences -- he can empty a magazine on a residential street and
        /// until now the block had nothing to say about the loudest thing that happened all
        /// week.
        ///
        /// Throttled hard, and deliberately not every time. A firefight is one thing the block
        /// mentions, not thirty notifications arriving while you are still in it.
        /// </summary>
        private void WatchForGunfire()
        {
            try
            {
                var now = Game.GameTime;
                if (now < _nextShotPost) return;

                var player = Game.Player.Character;
                if (player == null || !player.Exists() || !player.IsAlive) return;

                if (!Function.Call<bool>(Hash.IS_PED_SHOOTING, player.Handle)) return;

                // The clock resets whether or not anybody posts, so a long firefight stays
                // quiet after the first mention instead of rolling the dice every two minutes.
                _nextShotPost = now + ShotPostGapMs + _rng.Next(ShotPostGapMs);

                if (_rng.NextDouble() > ShotPostChance) return;

                _social?.On(SocialEvent.Shots, "");
            }
            catch
            {
                // A tweet is not worth an exception.
            }
        }

        /// <summary>Earliest the block will mention gunfire again.</summary>
        private int _nextShotPost;

        /// <summary>Roughly this apart, doubled at random.</summary>
        private const int ShotPostGapMs = 110000;

        /// <summary>Not every time. Plenty of shots nobody bothers to type about.</summary>
        private const double ShotPostChance = 0.55;

        /// <summary>True while he is on the floor, so the wake can be spotted.</summary>
        private bool _wasDown;

        /// <summary>When the block gets to hear about it, or 0 for not pending.</summary>
        private int _pillboxAt;

        /// <summary>Long enough for the fade out of the hospital to have finished.</summary>
        private const int PillboxDelayMs = 6500;

        /// <summary>Whether a vehicle is one we parked on purpose, for the traffic watchdog.</summary>
        private bool OurParkedCar(Vehicle car)
        {
            foreach (var parked in _cars)
            {
                if (parked.Owns(car)) return true;
            }

            return false;
        }

        /// <summary>A gang id as the per-gang diss sets spell it: "ballas" -> "Ballas".</summary>
        private static string Pretty(string gangId)
        {
            if (string.IsNullOrEmpty(gangId)) return "";
            return char.ToUpperInvariant(gangId[0]) + gangId.Substring(1).ToLowerInvariant();
        }

        /// <summary>Opens the kitchen screen with everything it needs to start a batch.</summary>
        private void OpenKitchen()
        {
            // The house stash goes in too: you are standing in the kitchen of the place the
            // weight is kept, and having to walk to the other screen to move a kilo eight feet
            // is not a decision, it is an errand.
            _cook.Open(_state.Stash, _stash == null ? null : _stash.Stash, _drugs, _pricing,
                       (drug, output, grams, purity, size) =>
                           _cutting.TryStart(drug, output, grams, purity, size));
        }

        /// <summary>True when the player is in normal control and the mod should be live.</summary>
        private bool IsPlayable()
        {
            try
            {
                var player = Game.Player?.Character;
                if (player == null || !player.Exists() || !player.IsAlive) return false;

                if (Game.IsPaused) return false;
                if (!Function.Call<bool>(Hash.IS_PLAYER_CONTROL_ON, Game.Player.Handle)) return false;
                if (Function.Call<bool>(Hash.IS_PAUSE_MENU_ACTIVE)) return false;
                if (Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE)) return false;
                if (!Function.Call<bool>(Hash.IS_SCREEN_FADED_IN)) return false;

                if (_cfg.PauseDuringMission && Function.Call<bool>(Hash.GET_MISSION_FLAG)) return false;

                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Playability probe failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// The neighbourhood the player is actually in, in words.
        ///
        /// Asked of the game rather than worked out from coordinates, because the game already
        /// knows and its answer is the one the map agrees with.
        /// </summary>
        private string ZoneNameHere()
        {
            try
            {
                var player = Game.Player.Character;
                if (player == null || !player.Exists()) return "";

                var pos = player.Position;
                var code = Function.Call<string>(Hash.GET_NAME_OF_ZONE, pos.X, pos.Y, pos.Z);

                var zone = _zoneMap == null ? null : _zoneMap.Get(code);
                return zone == null || string.IsNullOrEmpty(zone.Name) ? "the block" : zone.Name;
            }
            catch
            {
                return "the block";
            }
        }

        private void OnAborted(object sender, EventArgs e)
        {
            TryRestore();

            try
            {
                SaveGame.Save(_state, _crew, _market, _stash, true);
                Log.Info("Hoodrich unloaded cleanly.");
            }
            catch (Exception ex)
            {
                Log.Error("Save on abort failed.", ex);
            }
        }

        /// <summary>
        /// Puts back everything global we changed: time scale, timecycle, gang relationships,
        /// and any spawned supplier. A mod that leaves the world altered after unloading is
        /// worse than one that never loaded.
        /// </summary>
        private void TryRestore()
        {
            // First, and outside every other try. Everything else in here is litter; this one
            // is the player being unable to attract a police car for the rest of the session
            // because the script unloaded while somebody was holding the switch.
            try { LawHold.ReleaseAll(); } catch { /* teardown */ }

            try { _wheel?.RestoreWorld(); }
            catch { try { Game.TimeScale = 1f; } catch { /* nothing more we can do */ } }

            try { _crew?.RestoreWorld(); } catch { /* teardown */ }
            try { _dealers?.RestoreWorld(); } catch { /* teardown */ }
            try { _delivery?.RestoreWorld(); } catch { /* teardown */ }
            try { _deadDrop?.RestoreWorld(); } catch { /* teardown */ }
            try { _postUp?.RestoreWorld(); } catch { /* teardown */ }
            try { _bust?.RestoreWorld(); } catch { /* teardown */ }
            try { _leaders?.RestoreWorld(); } catch { /* teardown */ }
            try { _fixer?.RestoreWorld(); } catch { /* teardown */ }
            try { _bigj?.RestoreWorld(); } catch { /* teardown */ }
            try { _socialScreen?.RestoreWorld(); } catch { /* teardown */ }
            try { _block?.RestoreWorld(); } catch { /* teardown */ }
            try { _couch?.RestoreWorld(); } catch { /* teardown */ }
            try { _stove?.RestoreWorld(); } catch { /* teardown */ }
            try { _grimesStockA?.RestoreWorld(); } catch { /* teardown */ }
            try { _grimesStockB?.RestoreWorld(); } catch { /* teardown */ }
            foreach (var door in _doors)
            {
                try { door.RestoreWorld(); } catch { /* teardown */ }
            }
            try { _war?.RestoreWorld(); } catch { /* teardown */ }
            try { _payback?.RestoreWorld(); } catch { /* teardown */ }
            try { _lamarCrew?.RestoreWorld(); } catch { /* teardown */ }
            try { _stretchCrew?.RestoreWorld(); } catch { /* teardown */ }
            try { _grimesCrew?.RestoreWorld(); } catch { /* teardown */ }
            try { _labCrew?.RestoreWorld(); } catch { /* teardown */ }
            try { _denCrew?.RestoreWorld(); } catch { /* teardown */ }
            foreach (var car in _cars)
            {
                try { car.RestoreWorld(); } catch { /* teardown */ }
            }
            try { _party?.RestoreWorld(); } catch { /* teardown */ }
            try { _partyBarrel?.RestoreWorld(); } catch { /* teardown */ }
            try { _partyCouch?.RestoreWorld(); } catch { /* teardown */ }
            try { _stretchBox?.RestoreWorld(); } catch { /* teardown */ }

            try { _decks?.RestoreWorld(); } catch { /* teardown */ }

            foreach (var prop in _scenery)
            {
                try { prop.RestoreWorld(); } catch { /* teardown */ }
            }
            try { _jobs?.RestoreWorld(); } catch { /* teardown */ }
            try { _stash?.RestoreWorld(); } catch { /* teardown */ }
        }
    }
}
