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
        private readonly BlockDemand _blocks;
        private readonly StashRaid _raid;
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
        private readonly PocketScreen _pocketScreen;
        private readonly SettingsScreen _settingsScreen = new SettingsScreen();
        private readonly StashHouse _stash;
        private readonly SleepSpot _sleep;
        private readonly Kitchen _kitchen;
        private readonly MissionBook _missions;
        private readonly OwnedCars _ownedCars;
        private readonly Fixer _fixer;
        private readonly Armourer _bigj;
        private readonly Hao _hao;

        /// <summary>
        /// The block, talking about itself, and the screen it is read on.
        ///
        /// The feed keeps filling whether or not the screen is open, because a timeline that
        /// only writes itself while you are looking at it is a timeline you can watch being
        /// written, and that is the one thing it must never look like.
        /// </summary>
        private readonly SocialFeed _social;
        private readonly SocialScreen _socialScreen;

        /// <summary>The inbox. Its store is static; only the screen is an object.</summary>
        private readonly MessagesScreen _messages = new MessagesScreen();

        /// <summary>
        /// The people who stand near the people who matter. One each for Lamar and Stretch;
        /// their coordinates are the men's own, so the two sets never need keeping in step.
        /// </summary>
        private readonly Entourage _lamarCrew;
        private readonly Entourage _leaderCrew;
        private readonly Entourage _armourerCrew;
        private readonly Entourage _labCrew;
        private readonly Entourage _denCrew;

        /// <summary>
        /// The people actually working the two rooms, inside them.
        ///
        /// _labCrew and _denCrew are the men stood OUTSIDE these doors and always have been.
        /// Through the door was an empty warehouse -- the product appeared in the stash because
        /// a number went up, and the room it supposedly came out of had nobody in it. Online
        /// puts staff on the benches in exactly these interiors and it is most of what makes
        /// them read as a business rather than a menu with scenery.
        /// </summary>
        private readonly Entourage _growWorkers;
        private readonly Entourage _pressWorkers;

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

        /// <summary>Not a colour. Tells the parked car to leave the paint alone.</summary>
        private const int LeaveThePaint = -1;

        /// <summary>Brighter, for a van that belongs to a shop rather than to somebody.</summary>
        private const int ShopGreen = 53;

        private readonly Entourage _party;
        private readonly Entourage _meet;
        private readonly Social.Newsroom _newsroom;
        private readonly DroppedBags _bags;

        /// <summary>The drive down to Elysian and the van coming back.</summary>
        private readonly Missions.PortRun _port;

        /// <summary>And the two who come out when you text them.</summary>
        private readonly Gangs.Homies _homies;
        private readonly ParkedCar _meetOne;
        private readonly ParkedCar _meetTwo;
        private readonly Fixture _partyBarrel;
        private readonly Fixture _partyCouch;
        private readonly YardDog _partyDog;
        private readonly Fixture _leaderBox;

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
        private readonly Boombox _partyDecks;

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
            "anim@amb@nightclub@mini@dance@dance_solo@female@var_b@", "high_center",
            "anim@amb@nightclub@dancers@crowddance_facedj@hi_intensity", "hi_dance_facedj_09_v1_female^1",
            "mini@strip_club@idles@stripper", "stripper_idle_01"
        };

        /// <summary>
        /// The same idea for the men, because the women's clips do not read right on them.
        ///
        /// Ends on the club crowd clip and then the base-game one, so an install without After
        /// Hours still gets movement -- and failing all of it, the scenario these stations are
        /// given is WORLD_HUMAN_PARTYING, which is at least somebody at a party.
        /// </summary>
        private static readonly string[] DancingMen =
        {
            "anim@amb@nightclub@mini@dance@dance_solo@male@var_a@", "high_center",
            "anim@amb@nightclub@mini@dance@dance_solo@male@var_b@", "high_center",
            "anim@amb@nightclub@dancers@crowddance_groups@hi_intensity", "hi_dance_crowd_15_v1_male^1",
            "anim@amb@nightclub@dancers@crowddance_facedj@hi_intensity", "hi_dance_facedj_09_v1_female^1"
        };

        private static readonly string[] Deejaying =
        {
            // Working the decks, if this install has the club DLC that shipped these.
            "anim@amb@nightclub@djs@dixon@", "dixn_dance_cntr_up_dix",
            "anim@amb@nightclub@djs@black_madonna@", "bmad_dance_cntr_up_bm",
            "anim@amb@nightclub@djs@solomun@", "solo_dance_cntr_up_solo",
            "anim@amb@nightclub@djs@tale_of_us@", "tou_dance_cntr_up_tou",

            // And dancing if it has not. A woman dancing behind a pair of decks reads fine;
            // the scenario underneath this is WORLD_HUMAN_MUSICIAN, which without a prop in
            // her hands is a woman playing an air guitar at a mixer. The list ends on the
            // base-game clip so there is something here for an install with no DLC at all.
            "anim@amb@nightclub@mini@dance@dance_solo@female@var_a@", "high_center",
            "anim@amb@nightclub@mini@dance@dance_solo@female@var_b@", "high_center",
            "anim@amb@nightclub@dancers@crowddance_facedj@hi_intensity", "hi_dance_facedj_09_v1_female^1",
            "mini@strip_club@idles@stripper", "stripper_idle_01"
        };

        /// <summary>
        /// Her, and two fallbacks. All base game, so this needs no DLC.
        /// </summary>
        private static readonly string[] Working =
        {
            "s_f_y_hooker_01", "s_f_y_hooker_02", "s_f_y_hooker_03"
        };

        /// <summary>
        /// Who tends the plants. Nobody's cousin, and nobody armed.
        ///
        /// A hippy, a factory hand and a farmer -- people who would be paid to be in a room
        /// full of plants rather than people who would be shot for being in one. Deliberately
        /// not the set's own models: the men outside the door are the set, and the people
        /// inside working the room being the same faces would make it one gang standing in two
        /// places rather than a business with staff.
        /// </summary>
        private static readonly string[] Growers =
        {
            "a_m_y_hippy_01", "s_m_y_factory_01", "a_m_m_farmer_01"
        };

        /// <summary>
        /// Who works the press. All women, and that is the ANIMATION's doing rather than a
        /// casting choice.
        ///
        /// The game ships its drug-bench set in exactly two flavours: weed as male-only and
        /// coke as female-only. There is no male coke variant and no female weed one. This
        /// room uses the coke clips -- cutting and packing, which is what a press room does --
        /// so a man stood at one of these benches is a male ped playing an animation authored
        /// for a female frame. The first version of this list had two of them.
        ///
        /// It also gives the two rooms something to tell them apart. Men working plants in one
        /// and women working the bench in the other reads as two different jobs; the same
        /// three lads in both would have read as the same room twice.
        ///
        /// A sweatshop worker leads it because that model IS this: somebody sat in a back room
        /// working a bench all day. The chem-plant model that used to be here was security --
        /// a uniform rather than workwear, and a guard rather than staff.
        /// </summary>
        private static readonly string[] Pressers =
        {
            "s_f_y_sweatshop_01", "s_f_y_factory_01", "s_f_y_migrant_01"
        };

        /// <summary>
        /// The game's own drug-bench workers, in dictionary/clip pairs.
        ///
        /// anim@amb@drug_processors@ is what Rockstar put on somebody working a table of
        /// product, and it ships as an A and a B so two people at the same bench are not the
        /// same loop -- which is exactly what Entourage's pair rotation is for. Every one of
        /// these dictionaries holds a single clip and that clip is called "base", so the name
        /// repeats down the list; it looks like a mistake and is not.
        ///
        /// Read out of the animation dump rather than remembered. There are meth and weed
        /// "business" sets as well with far more clips in them, but those are authored around
        /// specific Online bench props that are not in these rooms, so they play as somebody
        /// miming at thin air.
        /// </summary>
        private static readonly string[] Processing =
        {
            "anim@amb@drug_processors@weed@male_a@base", "base",
            "anim@amb@drug_processors@weed@male_b@base", "base"
        };

        private static readonly string[] Packing =
        {
            "anim@amb@drug_processors@coke@female_a@base", "base",
            "anim@amb@drug_processors@coke@female_b@base", "base"
        };

        /// <summary>
        /// Somebody from the Unicorn, at a yard party.
        ///
        /// The lite model is last rather than first on purpose: it is the same girl in street
        /// clothes, which is the right fallback for an install missing the club models and the
        /// wrong first choice for a woman who is visibly working.
        /// </summary>
        private static readonly string[] Strippers =
        {
            "s_f_y_stripper_01", "s_f_y_stripper_02", "s_f_y_stripperlite", "a_f_y_topless_01"
        };

        /// <summary>
        /// Her own dance, which is not the one the other women are doing.
        ///
        /// The private-dance clips rather than the pole ones, and that is the whole decision
        /// here: a pole routine is authored around a pole, so played in the middle of a
        /// concrete yard it is a woman holding onto nothing and leaning off it. The private
        /// dance is standing work with no prop in it and reads correctly anywhere.
        ///
        /// Ends on the club idle and then the plain solo dance, so an install without the strip
        /// club dictionaries still gets somebody dancing rather than somebody stood still.
        /// </summary>
        private static readonly string[] Stripping =
        {
            "mini@strip_club@private_dance@part1", "priv_dance_p1",
            "mini@strip_club@private_dance@part2", "priv_dance_p2",
            "mini@strip_club@private_dance@part3", "priv_dance_p3",
            "mini@strip_club@idles@stripper", "stripper_idle_01",
            "anim@amb@nightclub@mini@dance@dance_solo@female@var_a@", "high_center"
        };

        /// <summary>
        /// A different dance from the one the other woman is doing.
        ///
        /// Same idea as Dancing and deliberately NOT the same order: two people a few metres
        /// apart playing the identical clip are a copy-paste, and the wheel-sized version of
        /// that problem is why the models rotate by station in the first place.
        ///
        /// Led by the base-game strip club clips rather than the nightclub ones -- they are in
        /// every install, and on this model they are the right dance anyway.
        /// </summary>
        private static readonly string[] DancingAlt =
        {
            "mini@strip_club@idles@stripper", "stripper_idle_01",
            "mini@strip_club@private_dance@part1", "priv_dance_p1",
            "anim@amb@nightclub@mini@dance@dance_solo@female@var_b@", "high_center",
            "anim@amb@nightclub@dancers@crowddance_facedj@hi_intensity", "hi_dance_facedj_09_v1_female^1"
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
        private readonly Fixture _armourerStockA;
        private readonly Fixture _armourerStockB;
        private readonly List<InteriorDoor> _doors = new List<InteriorDoor>();

        /// <summary>Franklin's and Denise's front doors, held open. See HouseDoors.</summary>
        private readonly HouseDoors _houseDoors = new HouseDoors();
        private readonly BlockLife _block;
        private readonly Rollers _rollers;
        private readonly Patrol _patrol;
        private readonly GangWar _war;
        private ArmourerTalk _bigjTalk;
        private GunScreen _gunScreen;
        private Weapons.GunLocker _locker;
        private HaoTalk _haoTalk;
        private CarScreen _carScreen;
        private DealerTalk _juanTalk;
        private readonly FixerTalk _fixerTalk;
        private readonly MissionRunner _jobs;
        private readonly CookScreen _cook;
        private readonly Phone.PhoneMenu _menu;
        private readonly Phone.PhoneController _phone;

        private int _lastSlowTick;
        private int _lastSave;
        private int _failures;
        private bool _parked;

        /// <summary>The screen that says why, when there is a why.</summary>
        private readonly Trouble _trouble = new Trouble();

        public Main()
        {
            // Anything thrown out of a Script constructor kills the script before it ever ticks,
            // and SHVDN reports it as a bare load failure. Fail soft and park instead.
            // BEFORE ANYTHING ELSE, and outside the try, because the whole point of it is to
            // have written the environment down before whatever goes wrong goes wrong.
            Preflight.WriteEnvironment();

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
                _sleep = new SleepSpot(_state, () => SaveGame.Save(_state, _crew, _market, _stash, true, _jobs.Paint, _blocks));
                _market = new Market(_cfg);
                _blocks = new BlockDemand(_cfg);

                SaveGame.Load(_state, _crew, _market, _stash, null, _blocks);

                _turf = new TurfWatch(_gangs, _crew, _state);
                _crew.Turf = _turf;
                _bust = new Bust(_cfg, _state) { Turf = _turf };
                _deadDrop = new DeadDrop(_cfg, _state);

                _pricing = new Pricing(_cfg, _state) { Turf = _turf, Crew = _crew, Market = _market, Blocks = _blocks };
                _raid = new StashRaid(_cfg, _state, _stash) { Crew = _crew };
                _cutting = new Cutting(_state.Stash, _state);

                // Product you have put down. Its own thing rather than a corner of the stash,
                // because a bag on a pavement is not storage -- it is somewhere you left
                // something in a hurry.
                _bags = new DroppedBags { Pockets = _state.Stash };

                // Nobody's set turns on itself over a stray round. Once, at startup, because
                // relationships between groups are global and survive until something changes
                // them -- there is nothing to re-apply per spawn.
                _crew.MakeGangsWholeToThemselves();

                // Two of yours, on call once you have ridden a job out with them.
                _homies = new Gangs.Homies(_gangs, _crew, _state);
                _cook = new CookScreen();
                _kitchen = new Kitchen(OpenKitchen, () => _cutting.IsBusy);
                _postUp = new PostUp(_cfg, _state, _pricing) { Turf = _turf, Crew = _crew, Bust = _bust };
                _bust.Post = _postUp;
                _leaders = new GangLeaders(_cfg, _gangs, _zoneMap, _crew, _state);

                _jobs = new MissionRunner(_state, _crew, _gangs, _zoneMap);

                // The paint, read back now the runner that owns it exists. The rest of the
                // save was read further up, before there was anything to hand this to -- see
                // SaveGame.LoadTags.
                SaveGame.LoadTags(_jobs.Paint);

                // How the letters are drawn, from the ini. Pushed rather than read live so the
                // tag run does not need to know settings exist.
                _jobs.Paint.TagSpacing = _cfg.TagDotSpacing;
                _jobs.Paint.TagDotSize = _cfg.TagDotSize;

                // And the wall going up marks the save dirty, or a tag sprayed after the last
                // sale would not survive to the next load.
                _jobs.Paint.Changed = () => _state.Touch();

                _fixer = new Fixer(_crew);

                // The bike ride borrows him off his corner and rides him out with the rest.
                _jobs.Boss = _fixer;
                _jobs.Book = _missions;

                // How long he wants to himself between jobs, read live so the settings screen
                // can change it without a reload.
                _jobs.RestMinutes = () => _cfg.LamarRestMinutes;
                _bigj = new Armourer(_gangs) { Working = () => _crew != null && _crew.IsAffiliated };

                // Hao runs a second economy off the same map: metal instead of weight, with
                // its own yard, its own money and eventually its own jobs.
                _hao = new Hao(_state);

                // A car you paid for outlives the session now. See OwnedCars.
                _ownedCars = new OwnedCars(_state)
                {
                    // A car is too much money to leave sitting in memory for two minutes.
                    SaveNow = () => SaveGame.Save(_state, _crew, _market, _stash, true, _jobs.Paint, _blocks)
                };
                _hao.Owned = _ownedCars;

                // Nobody sends you to a car dealer in Little Seoul before you are anybody. The
                // yard is there the whole time and you can find it on foot; what waits is the
                // marker telling you to.
                _hao.Known = () => _crew != null && _crew.IsAffiliated;

                _social = SocialFeed.Load();
                _socialScreen = new SocialScreen(_social);

                // HERE, not thirty lines up where it used to be.
                //
                // This is a field copy, not a lambda, and it was made before _social existed --
                // so the crew held a null feed for the entire life of the script and the one
                // event they raise, HomiesOut, was swallowed by its own null guard every time.
                // No error, no log line, nothing to notice.
                _homies.Feed = _social;

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

                // The armourer sells ammunition, so he gets something to sell it off. One either
                // side of where he stands and both facing the way he does, set out along the
                // line he faces rather than dropped at arbitrary angles -- a man with stock
                // laid out has arranged it; a man with two crates at odd angles has been
                // burgled.
                //
                // Placed by eye off his own coordinate, since none was given. Send a HUD
                // readout from where each should stand and they move.
                _armourerStockA = new Fixture(new Vector3(-128.073f, -1462.524f, 33.823f), 225.844f,
                                            "ex_office_swag_guns04");

                _armourerStockB = new Fixture(new Vector3(-130.301f, -1460.226f, 33.823f), 225.844f,
                                            "ex_office_swag_guns02");

                // He has people now. His corner is one of the three a raid comes for and
                // he was stood on it alone, which is not how anybody holds anything.
                _armourerCrew = new Entourage(_gangs, "families",
                                            new Vector3(-129.187f, -1461.375f, 33.823f),
                                            225.844f, "Stretch")
                    .Stand(new Vector3(-128.394f, -1458.440f, 33.823f), 225.844f,
                           "WORLD_HUMAN_GUARD_STAND");

                // The set's own places, and only once you are in the set. See InteriorDoor.
                foreach (var spec in _cfg.Doors)
                {
                    _doors.Add(new InteriorDoor(spec)
                    {
                        Known = () => _crew != null && _crew.IsAffiliated
                    });
                }

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

                // The sound van, next to the decks. A Minivan Custom rather than the Voodoo
                // that used to sit here: a saloon parked by a set of decks is a car, and a
                // built van with its boot up is where the music is coming from.
                //
                // Paint 49 is metallic dark green -- the set's colour with flake in it rather
                // than the flat poster green an RGB triple gives you -- and the underglow is
                // the same green, so the van reads as theirs after dark as well.
                _cars.Add(new ParkedCar(new Vector3(-196.745f, -1718.838f, 32.664f), 319.530f,
                                        MetallicDarkGreen,
                                        "minivan2", "voodoo", "buccaneer2")
                {
                    Built = true,
                    BootOpen = true,
                    Neon = System.Drawing.Color.FromArgb(60, 200, 80),

                    // Green inside as well as out, key in it, and something coming out of it.
                    // It is the reason there is a party in this yard.
                    Interior = MetallicDarkGreen,
                    Running = true,
                    Radio = "RADIO_09_HIPHOP_OLD",

                    // Key out at two, back in at six, with the yard.
                    QuietFrom = 2,
                    QuietTo = 6
                });

                // A quad against the shutters, the same green as everything else in the lot.
                //
                // The neon is asked for and may simply not answer: underglow is a Los Santos
                // Customs slot, and a quad has not got one. It costs two calls to try and
                // nothing to be told no, so it is tried.
                _cars.Add(new ParkedCar(new Vector3(-197.960f, -1714.836f, 31.955f), 290.056f,
                                        MetallicDarkGreen,
                                        "blazer4", "blazer", "faggio2")
                {
                    Neon = System.Drawing.Color.FromArgb(60, 200, 80)
                });

                // Somebody's FR36 on the pad at the bottom of the lot.
                //
                // Stock, on purpose. The van is the build in this yard and one yard does not
                // need two of them -- a set's cars are cars people drive, with the paint and
                // the glow and nothing else, and the one project parked with its boot up is
                // what makes it read as a project.
                //
                // West Coast Classics, like everything else with a speaker in it.
                //
                // One station across the whole mod now, asked for by name after hearing it
                // several ways: the van, this, the Journey, the two on the court, the boombox
                // and the cars the set drives round the block. The yard sounds like one place
                // with one radio on rather than four things that each happened to be tuned
                // somewhere.
                //
                // Key out at two and back in at six, with the van and with the yard. It is one
                // switch: somebody came out, turned everything off and went in.
                _cars.Add(new ParkedCar(new Vector3(-198.2f, -1734.7f, 32.2f), 318.897f,
                                        MetallicDarkGreen,
                                        "fr36", "elegy2", "sultan")
                {
                    Stock = true,
                    Lowered = true,
                    Running = true,
                    Radio = "RADIO_09_HIPHOP_OLD",
                    Neon = System.Drawing.Color.FromArgb(60, 200, 80),

                    QuietFrom = 2,
                    QuietTo = 6
                });

                // The Journey at the top of the yard, with its own music going.
                //
                // Left the colour it came in. Everything else in this lot is the set's green
                // because everything else in this lot belongs to the set; a camper somebody has
                // been living in for fifteen years is scenery, and painting it would say "the
                // mod put this here" out loud. Its paint index is below zero, which the parked
                // car reads as no instruction rather than as a colour.
                //
                // Same station as the other van, which is what was asked for and is also right
                // -- these two are thirty metres apart at opposite ends of one yard, and one
                // yard with one thing playing is a party rather than two speakers.
                _cars.Add(new ParkedCar(new Vector3(-192.162f, -1738.344f, 32.146f), 321.334f,
                                        LeaveThePaint,
                                        "journey", "camper", "rvcamper")
                {
                    Running = true,
                    Radio = "RADIO_09_HIPHOP_OLD",

                    QuietFrom = 2,
                    QuietTo = 6
                });

                // ---- the meet on the court, after dark -----------------------------
                //
                // Two cars and the people stood round them, and that is the whole thing. No
                // race, no timer, nothing to press: it is a place that is busy at midnight and
                // empty at midday, which is the only feature it needs.
                //
                // Quiet hours run six till twenty, which is the daytime -- so "quiet" here
                // means gone, and the hours it is NOT quiet are the hours there is a meet.
                _meetOne = new ParkedCar(new Vector3(-227.440f, -1697.994f, 33.300f), 214.105f,
                                         MetallicDarkGreen, "gauntlet4", "gauntlet", "dominator")
                {
                    // Stock. Not a build -- no rims, no drop, no tint, nothing but the paint
                    // and what is glowing under it, which is what was asked for and is also
                    // the more convincing of the two: one car at a meet is somebody's project
                    // and the rest are cars people drove there.
                    Stock = true,
                    Running = true,
                    Lights = true,
                    Radio = "RADIO_09_HIPHOP_OLD",
                    Neon = System.Drawing.Color.FromArgb(60, 200, 80),
                    QuietFrom = 6,
                    QuietTo = 20,
                    GoneWhenQuiet = true
                };

                _meetTwo = new ParkedCar(new Vector3(-223.179f, -1696.639f, 33.294f), 180.686f,
                                         MetallicDarkGreen, "hermes", "tornado", "buccaneer2")
                {
                    Built = true,
                    Running = true,
                    Lights = true,

                    Radio = "RADIO_09_HIPHOP_OLD",
                    Neon = System.Drawing.Color.FromArgb(60, 200, 80),
                    QuietFrom = 6,
                    QuietTo = 20,
                    GoneWhenQuiet = true
                };

                _cars.Add(_meetOne);
                _cars.Add(_meetTwo);

                // And the people. A ring worked out from the midpoint of the two cars rather
                // than ten marks typed one at a time -- people at a meet stand round the cars
                // and face them, and five metres out clears both without anybody standing in a
                // door. Nobody is flagged for the night watch, so at six in the morning the
                // whole set simply is not made.
                _meet = new Entourage(_gangs, "families",
                                      new Vector3(-225.310f, -1697.316f, 33.297f), 270f, "the meet")

                    .Stand(new Vector3(-220.310f, -1697.316f, 33.297f), 270.000f,
                           "WORLD_HUMAN_STAND_MOBILE", Fam(0), armed: false)

                    .Stand(new Vector3(-221.264f, -1694.378f, 33.297f), 234.000f,
                           "WORLD_HUMAN_SMOKING", Fam(1), armed: false)

                    .Stand(new Vector3(-223.764f, -1692.561f, 33.297f), 198.000f,
                           "WORLD_HUMAN_DRINKING", Women, armed: false)

                    .Stand(new Vector3(-226.855f, -1692.561f, 33.297f), 162.000f,
                           "WORLD_HUMAN_STAND_IMPATIENT", Fam(2), armed: false)

                    .Stand(new Vector3(-229.355f, -1694.378f, 33.297f), 126.000f,
                           "WORLD_HUMAN_SMOKING_POT", Fam(0), armed: false)

                    .Stand(new Vector3(-230.310f, -1697.316f, 33.297f), 90.000f,
                           "WORLD_HUMAN_LEANING", Fam(1), armed: false)

                    .Stand(new Vector3(-229.355f, -1700.255f, 33.297f), 54.000f,
                           "WORLD_HUMAN_DRINKING", Fam(2), armed: false)

                    .Stand(new Vector3(-226.855f, -1702.072f, 33.297f), 18.000f,
                           "WORLD_HUMAN_PARTYING", Women, armed: false, anim: DancingAlt)

                    .Stand(new Vector3(-223.764f, -1702.072f, 33.297f), 342.000f,
                           "WORLD_HUMAN_STAND_MOBILE", Fam(1), armed: false)

                    .Stand(new Vector3(-221.264f, -1700.255f, 33.297f), 306.000f,
                           "WORLD_HUMAN_SMOKING", Fam(2), armed: false);

                _meet.QuietFrom = 6;
                _meet.QuietTo = 20;

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
                // ---- inside the two rooms ------------------------------------------
                //
                // The animations are the game's OWN business-worker set, pulled out of the
                // dump rather than guessed: anim@amb@drug_processors@ is what Rockstar put on
                // the people working a drug bench, and it comes in a male weed pair and a
                // female packing pair. One clip each, called "base", which is why every entry
                // below repeats it -- Entourage takes dictionary and clip in pairs and rotates
                // through them by station so two men side by side are not the same loop.
                //
                // COORDINATES ARE ESTIMATED, and this is the one thing here I could not read
                // off the game. What is known is where the door drops you -- those two anchors
                // come from Settings -- so the benches are placed a few metres off them. If
                // anybody ends up in a wall, stand where you want them and read the mark, the
                // way every other position in this file was got.
                //
                // onProp is set on all of them, which is not about props: it is the flag that
                // stops Entourage running a ground probe and overwriting the height. The grow
                // room is the exact interior InteriorDoor warns about dropping people through
                // the floor of, and in here the Z from the door anchor is already right.
                _growWorkers = new Entourage(_gangs, "families",
                                             new Vector3(1039.000f, -3098.000f, -39.000f),
                                             180f, "the grow room")

                    .Stand(new Vector3(1042.000f, -3100.500f, -39.000f), 90f,
                           "WORLD_HUMAN_SMOKING", Growers, armed: false, onProp: true,
                           anim: Processing)

                    .Stand(new Vector3(1036.500f, -3100.000f, -39.000f), 270f,
                           "WORLD_HUMAN_SMOKING", Growers, armed: false, onProp: true,
                           anim: Processing)

                    .Stand(new Vector3(1039.500f, -3103.000f, -39.000f), 0f,
                           "WORLD_HUMAN_SMOKING", Growers, armed: false, onProp: true,
                           anim: Processing);

                // See Entourage.IndoorsOnly. Nobody is made until the room is actually there
                // and the player is in it.
                _growWorkers.IndoorsOnly = true;

                _pressWorkers = new Entourage(_gangs, "families",
                                              new Vector3(1000.000f, -3200.000f, -38.000f),
                                              180f, "the press")

                    .Stand(new Vector3(1003.000f, -3202.000f, -38.000f), 90f,
                           "WORLD_HUMAN_SMOKING", Pressers, armed: false, onProp: true,
                           anim: Packing)

                    .Stand(new Vector3(997.500f, -3201.500f, -38.000f), 270f,
                           "WORLD_HUMAN_SMOKING", Pressers, armed: false, onProp: true,
                           anim: Packing);

                _pressWorkers.IndoorsOnly = true;

                _party = new Entourage(_gangs, "families",
                                       new Vector3(-199.604f, -1728.764f, 32.664f), 186.646f, "the lot")

                    // THE TWO WHO ARE ALWAYS HERE, and the only two.
                    //
                    // A drink and a smoke, which is what a yard belonging to somebody looks
                    // like at two in the afternoon. Everybody below them is marked party -- see
                    // Entourage.PartyFrom -- so in daylight this lot is these two, the man
                    // watching the back fence, and nobody else. The decks, the dancing and the
                    // fifteen bodies are an evening, not a permanent installation.
                    .Stand(new Vector3(-199.196f, -1726.450f, 32.664f), 190.000f,
                           "WORLD_HUMAN_DRINKING", Fam(0), armed: false)

                    .Stand(new Vector3(-197.277f, -1728.437f, 32.664f), 262.000f,
                           "WORLD_HUMAN_SMOKING", Fam(1), armed: false)

                    // Dancing, not "partying". WORLD_HUMAN_PARTYING is somebody holding a
                    // drink and nodding; this is somebody actually moving to what is coming
                    // out of the decks, which is the difference between a yard with people in
                    // it and a yard with a party in it.
                    .Stand(new Vector3(-197.722f, -1730.278f, 32.664f), 130.367f,
                           "WORLD_HUMAN_PARTYING", Women, armed: false, anim: Dancing, party: true)

                    .Stand(new Vector3(-201.294f, -1730.396f, 32.664f), 46.000f,
                           "WORLD_HUMAN_SMOKING_POT", Fam(2), armed: false, party: true)

                    .Stand(new Vector3(-201.679f, -1727.661f, 32.664f), 118.000f,
                           "WORLD_HUMAN_DRINKING", Women, armed: false, party: true)

                    // On the decks. Facing the yard, which is the direction the music goes.
                    .Stand(new Vector3(-194.279f, -1723.069f, 32.664f), 148.079f,
                           "WORLD_HUMAN_MUSICIAN", Women, armed: false, anim: Deejaying, party: true)

                    // ---- the rest of the yard ------------------------------------------
                    //
                    // Marks read off the game: somebody stood where they wanted the man and
                    // faced the way they wanted him facing, so the position and the heading
                    // come from the same reading and neither is guessed.
                    //
                    // Mixed on purpose, and mixed by NEIGHBOUR rather than at random -- two
                    // men drinking shoulder to shoulder read as one prop repeated. The pair by
                    // the lowrider are a smoke and a drink; the two nearest the decks both
                    // dance, which is the one place that is right.
                    // Not one of the set. She is at the party rather than in it, the same way
                    // the women round the fire are, and she gets her own clip list so the two
                    // women dancing in the same yard are not doing the identical loop in sync.
                    .Stand(new Vector3(-194.412f, -1727.852f, 32.664f), 349.215f,
                           "WORLD_HUMAN_PARTYING", Working, armed: false, anim: DancingAlt, party: true)

                    // THE TWO WHO MOVE, and they are the two with the least to do: a man whose
                    // entire station is "holding a drink" has no reason to be welded to a spot
                    // for twenty hours, and a yard where every single person is fixed reads as
                    // a diorama rather than a party.
                    //
                    // Two, not six. The smokers, the dancers and the woman on the decks are all
                    // doing something that happens standing still, and a crowd where everybody
                    // mills about is just as wrong in the other direction.
                    //
                    // This one also un-stacks a collision: his mark is the same position AND
                    // the same heading as the all-night smoker further down, so outside quiet
                    // hours there were two men standing inside each other here. He walks off it
                    // now. If you want him somewhere specific instead, stand where you want him
                    // and read the mark off -- that is where every other number in this list
                    // came from.
                    .Stand(new Vector3(-196.532f, -1725.500f, 32.664f), 300.596f,
                           "WORLD_HUMAN_DRINKING", Fam(1), armed: false, wander: 5f, party: true)

                    .Stand(new Vector3(-199.096f, -1723.577f, 32.664f), 319.476f,
                           "WORLD_HUMAN_DRINKING", Fam(0), armed: false, wander: 4.5f, party: true)

                    .Stand(new Vector3(-196.101f, -1729.308f, 32.664f), 20.826f,
                           "WORLD_HUMAN_PARTYING", Fam(1), armed: false, anim: DancingMen, party: true)

                    // The one by the car. PARTYING underneath rather than SMOKING_POT: the
                    // scenario is what shows if none of the clips load, and a dancer falling
                    // back to a woman smoking on her own is the wrong picture in the one place
                    // it matters.
                    .Stand(new Vector3(-198.543f, -1731.535f, 32.664f), 16.019f,
                           "WORLD_HUMAN_PARTYING", Strippers, armed: false, anim: Stripping, party: true)

                    .Stand(new Vector3(-203.007f, -1729.627f, 32.664f), 251.512f,
                           "WORLD_HUMAN_DRINKING", Fam(0), armed: false, party: true)

                    .Stand(new Vector3(-206.169f, -1731.598f, 32.664f), 340.253f,
                           "WORLD_HUMAN_SMOKING", Fam(1), armed: false, party: true)

                    // Somebody watching the back of the lot, where the skips are and the fence
                    // is low enough to come over. Armed, and the only man here who is -- a yard
                    // full of people drinking needs one person who is not.
                    .Stand(new Vector3(-204.430f, -1725.562f, 32.664f), 79.008f,
                           "WORLD_HUMAN_GUARD_STAND", Fam(2), weapon: "WEAPON_COMPACTRIFLE",
                           nights: true)

                    // And one on the near corner who does not go home either. Two is the number:
                    // one is a man forgotten in a yard, and three is the party still going.
                    .Stand(new Vector3(-196.532f, -1725.500f, 32.664f), 300.596f,
                           "WORLD_HUMAN_SMOKING", Fam(1), armed: false, nights: true);

                // Two till six the yard empties out. Everybody without a reason to be stood in
                // it goes home, which is the one thing that makes the other twenty hours read
                // as a party rather than as scenery that happens to be lit.
                _party.QuietFrom = 2;
                _party.QuietTo = 6;

                // Seven at night until two in the morning, which hands straight over to the
                // quiet hours above -- the party ends and the lot is empty, rather than there
                // being an hour in between where a handful of people stand about in the dark
                // having apparently not noticed it finished.
                _party.PartyFrom = 19;
                _party.PartyTo = 2;

                // Off the road at the top of the block, which is where somebody coming to this
                // would actually park and walk in from. They spawn along the kerb and come up
                // through the gate on the nav mesh; the walk is the point, so it is a real
                // route rather than a straight line through the fence.
                _party.ArriveAt = new Vector3(-258.752f, -1696.881f, 26.937f);

                // The music keeps the party's hours. It played around the clock, which after
                // the yard went quiet by day meant a stereo going at seven in the morning to
                // three people and a barrel fire.
                if (_partyDecks != null) _partyDecks.Playing = () => _party.IsPartyOn;

                // The barrel IS the fire -- it burns on its own. A camp fire was stacked on
                // top of it as well, which is two fires a metre apart and reads as a bug even
                // before you notice the logs floating.
                _partyBarrel = new Fixture(new Vector3(-199.604f, -1728.764f, 32.664f), 0f,
                                           "gr_prop_gr_hobo_stove_01", "prop_barrel_02a");

                _partyCouch = new Fixture(new Vector3(-202.400f, -1727.000f, 32.664f), 118.000f,
                                          "prop_couch_03", "prop_old_couch_01", "prop_rub_couch01");

                // And somebody's dog, off the lead, doing laps of the yard nobody asked him to.
                //
                // Six metres, which is a dog mooching round a party rather than a dog on a
                // beat. Placed between the barrel and the couch so his round takes him through
                // where the people are instead of along a wall.
                _partyDog = new YardDog(new Vector3(-200.900f, -1727.900f, 32.664f), 6f)
                {
                    // The yard is only a party once it is your block. Before that he is
                    // somebody else's dog in somebody else's yard.
                    Known = () => _crew != null && _crew.IsAffiliated
                };

                // Somebody's bag, open, on the table by the couch. Height read off the HUD stood
                // ON the table rather than beside it, so it sits on the top rather than through
                // it -- the yard is at 32.664 and the table is most of a metre above that.
                _scenery.Add(new Fixture(new Vector3(-203.944f, -1726.461f, 33.396f), 133.896f,
                                         "bkr_prop_weed_bag_01a", "bkr_prop_weed_bigbag_01a",
                                         "prop_drug_package_02", "prop_drug_package"));

                // A second speaker, the other end of the yard.
                //
                // One was never going to be a party. The decks are by the shutters and the
                // couch is thirty feet away with nothing playing at it, so half the yard was
                // people standing in silence next to somebody else's music.
                _partyDecks = new Boombox(new Vector3(-202.900f, -1729.900f, 32.664f), 20f,
                                          "ba_prop_battle_speaker_01a",
                                          "prop_boombox_01", "prop_ld_ferris_wheel");

                // Weight sitting by Stretch's door, which is the whole reason anybody goes to
                // that door. Fallbacks behind it: the Bikers bag and then a plain crate, so an
                // install without the newer DLC gets something rather than nothing.
                // Turned off square with the wall. At 2.4 degrees the stack sat parallel to it
                // and the corner of the top box went through the render; twenty-odd degrees is
                // enough to clear it and reads as boxes somebody put down rather than boxes
                // somebody aligned.
                _leaderBox = new Fixture(new Vector3(-162.562f, -1637.442f, 34.029f), 24.500f,
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
                // His own crew because there is nothing else within a leash of here -- the yard
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

                // A SCREEN, not scenery.
                //
                // Cheng's two men are made at a mark on the apron and walk from it to the
                // tailgate, and a ped is made the way everything in this game is made: it is
                // not there and then it is. Standing in the bay watching two men appear out of
                // nothing four metres away is the one moment of that whole scene that says
                // "script" out loud.
                //
                // So there is a box truck between the bay and the mark, sat lengthways. They
                // come out from behind it, which is where men on a dock come from. Nothing
                // clever, and it fixes the thing completely.
                //
                // Parked rather than spawned by the mission, so it is there before you arrive,
                // there after you leave, and holds the ground against ambient traffic the
                // whole time -- which is the other half of what it is for. See ParkedCar: the
                // traffic watchdog is told it is parked on purpose, and PortRun's bay sweep
                // knows its models by name so it does not clear away its own screen.
                // Two metres up its own nose from where it was first placed. Heading 269.5 is
                // within half a degree of due east, so forward is +X and almost nothing else.
                _cars.Add(new ParkedCar(new Vector3(1248.484f, -3169.455f, 5.249f), 269.487f,
                                        -1,
                                        "benson", "mule3", "mule", "pounder"));

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
                                  || (_port != null && _port.Owns(car))
                                  || OwnedByPlayer(car)
                                  || (_decks != null && _decks.Owns(car))
                                  || (_partyDecks != null && _partyDecks.Owns(car))
                };

                _payback = new Payback(_gangs);

                // Ours, out driving their own blocks, wanting nothing. Every other car in this
                // mod turns up because of the player; these are the ones that would be there
                // whether he was or not.
                _rollers = new Rollers(_cfg, _gangs, "families", _turf);

                // The law, going round because the blocks are the blocks. Nothing to do with
                // heat, a bust or a raid -- this car was coming down that street tonight
                // whether you were on it or not.
                _patrol = new Patrol(_cfg, _gangs, _turf, _state)
                {
                    Doorstep = StashHouse.House
                };

                _toasts = new TweetToast
                {
                    Enabled = _cfg.TweetsOnTheRight,

                    // Not over a full-screen UI. They keep queueing and keep ageing while it is
                    // up, so nothing is lost -- they are simply not drawn across a menu.
                    Hidden = () => _phone.IsOpen || _socialScreen.IsOpen || _messages.IsOpen ||
                                   _stashScreen.IsOpen || _pocketScreen.IsOpen
                                   || _settingsScreen.IsOpen
                                   || _info.IsOpen || _talk.IsOpen || _cook.IsOpen
                                   || _gunScreen.IsOpen || _carScreen.IsOpen,
                };

                _social.Toasts = _toasts;

                _war = new GangWar(_gangs, _crew, _state)
                    .Defend("Lamar", Fixer.Spot)
                    .Defend("Stretch", new Vector3(-129.187f, -1461.375f, 33.823f));

                // Lamar's own guard is gone with the move.
                //
                // Its six marks were hand-placed around the Chamberlain courtyard -- a rifle on
                // those steps, a beer by that pool -- and none of them mean anything in a lot a
                // hundred and twenty metres away. Leaving them where they were is six armed men
                // standing round a corner the man they were guarding has left.
                //
                // And the new yard does not need them: there are already fifteen people in it,
                // one of them on a rifle by the shutters. Say the word with marks read off the
                // HUD and he gets a guard again; until then the party IS his crew.
                _lamarCrew = new Entourage(_gangs, "families", Fixer.Spot, 124.548f, "Lamar");

                // One for Stretch, on the spot it was read off the HUD at.
                var leader = _leaders.Get("families");
                _leaderCrew = leader == null
                    ? null
                    : new Entourage(_gangs, "families",
                                    new Vector3(leader.SpotX, leader.SpotY, leader.SpotZ),
                                    leader.Heading, leader.Name)
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

                if (leader != null)
                {
                    _war.Defend(leader.Name, new Vector3(leader.SpotX, leader.SpotY, leader.SpotZ));
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
                // ONE ANSWER, asked in four places.
                //
                // Every one of these lists used to name _jobs on its own, and _jobs is Lamar's
                // book -- it does not know the port run exists. So the whole time you were
                // driving Gerald's twenty kilos across the city, the mod considered you idle:
                // gang wars could start, a debt could come due, riders kept coming out on the
                // block and patrols kept rolling. A war going off mid-errand is not a
                // coincidence, it is this.
                //
                // Written down once so the next job added cannot be forgotten from four
                // separate lists, which is exactly how this one was.
                Func<bool> onAJob = () => (_jobs != null && _jobs.IsRunning)
                                          || (_port != null && _port.Running);

                // AND NOT WHILE YOU ARE WANTED.
                //
                // A war holds the law off, and holding the law off starts by setting the wanted
                // level to zero and re-applying it every seven hundred milliseconds for the
                // whole fight. So a war that began during a three star chase deleted the chase
                // and kept it deleted for up to eight minutes -- rob, run people over, shoot at
                // police, nothing sticks. Patrol already refuses to act while you are wanted
                // for the same reason; the war did not.
                _war.Busy = () => onAJob() || Game.Player.Wanted.WantedLevel > 0;

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
                _payback.Busy = () => onAJob()
                                      || (_war != null && _war.IsRunning);

                // Nobody goes for a drive round the block during a raid. Existing ones are left
                // where they are -- they simply stop being replaced.
                _rollers.Busy = () => onAJob()
                                      || (_war != null && _war.IsRunning)
                                      || (_payback != null && _payback.IsRunning);

                // And the law stays out of a raid, a job and a bust -- all three send police of
                // their own, and two lots of police for two different reasons in one street is
                // neither of them reading as what it is.
                _patrol.Busy = () => onAJob()
                                     || (_war != null && _war.IsRunning)
                                     || (_payback != null && _payback.IsRunning)
                                     || Game.Player.Wanted.WantedLevel > 0;

                // Right on the d-pad is the talk-to-somebody button everywhere in the mod, so
                // the gesture must not be offered while anything else already owns it -- a
                // squad car rolling past while you are stood in front of Gerald would otherwise
                // turn "talk to Gerald" into a hand at the police.
                // The car that comes past while you are dealing is PostUp's, not the patrol
                // system's, and it is the one you most want to put a hand up at.
                _patrol.Passing = () => _postUp == null ? null : _postUp.RollingPast;

                _patrol.Occupied = () => _phone.IsOpen || _talk.IsOpen || _info.IsOpen
                                         || _cook.IsOpen || _socialScreen.IsOpen
                                         || _messages.IsOpen
                                         || _stashScreen.IsOpen || _pocketScreen.IsOpen
                                         || _settingsScreen.IsOpen || _gunScreen.IsOpen
                                         || _carScreen.IsOpen;

                _copWatch.Social = _social;

                // So the set that turns up because of something you posted can answer it on
                // the same feed you posted it to.
                _payback.Social = _social;
                _postUp.Social = _social;

                _social.GangById = id => _gangs.GetLoose(id);
                _raid.Social = _social;
                _blocks.Social = _social;

                // Without the map there is nowhere to name, and Tip returns before it does
                // anything -- which is a feature that ships doing nothing at all.
                _blocks.Zones = _zoneMap;
                _crew.Social = _social;

                // Who said what, tallied per set. The feed knows the author of every post and
                // nothing about standings; the standings want a count and nothing about how a
                // post gets written. One line joins them and neither has to know the other.
                _social.Posted = gangId =>
                {
                    if (string.IsNullOrEmpty(gangId)) return;
                    if (_crew.GangById(gangId) == null) return;

                    _crew.StandingFor(gangId).Tweets++;
                };

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
                        (_hao != null && _hao.InReach) ||
                        (_kitchen != null && _kitchen.InReach) ||
                        (_sleep != null && _sleep.InReach) ||
                        (_delivery != null && _delivery.IsActive) ||
                        (_postUp != null && _postUp.IsPosted)
                };

                // The bike job runs its own exchange on the court, so it needs the same screen.
                _jobs.Talk = _talk;

                _info = new InfoPanel();
                _stashScreen = new StashScreen();
                _pocketScreen = new PocketScreen();
                _port = new Missions.PortRun(_state)
                {
                    // Nothing of ours gets swept off a kerb by the port run.
                    Spare = car => OurParkedCar(car) || OwnedByPlayer(car),
                    Talk = _talk,
                    Social = _social,
                    // A raid banner and this card are both centred at the top of the screen
                    // and both drew at once -- GERALD UNDER ATTACK printed straight across
                    // "meet the dock worker". A raid is the more urgent of the two and it is
                    // over in a minute, so the errand stands down and comes back.
                    Busy = () => (_jobs != null && _jobs.IsRunning)
                                 || (_war != null && _war.IsRunning)
                };

                // He cannot be on his corner and stood in the yard at the same time, and the
                // leader system only ever holds one of them alive -- so this is how it is told
                // he has gone out.
                _leaders.StandDown = gangId =>
                    _port.WaitingAtTheDrop &&
                    string.Equals(gangId, "families", StringComparison.OrdinalIgnoreCase);

                _dealers.SendToThePort = () => _port.Send();

                _leaderTalk = new LeaderTalk(_leaders, _gangs, _crew, _state, _drugs, _pricing, _cfg);
                _leaderTalk.SendToThePort = () => _port.Send();

                // Telling Wei Cheng about his son changes who is stood on the dock. The talk
                // knows nothing about docks and should not learn -- see LeaderTalk.TookHisSon
                // and DealerManager.Handover.
                _leaderTalk.TookHisSon = () =>
                {
                    _dealers.Handover("docks", _state);

                    Notify.Important("~g~The port's changed hands.~s~ New number, new man, " +
                                     "better price.");

                    _social?.On(SocialEvent.PortChanged, "the port", 0);
                };
                _leaders.Talk = _talk;
                _leaders.TalkBuilder = def => _leaderTalk.Root(def);
                _leaderTalk.Social = _social;
                _leaderTalk.Dealers = _dealers;

                _fixerTalk = new FixerTalk(_fixer, _missions, _jobs, _crew, _state);
                _fixer.Talk = _talk;
                _fixer.TalkBuilder = () => _fixerTalk.Root();

                // The one thing that lets him be spoken to while a job still technically has
                // him: the work is done and the only thing left is getting paid for it.
                _fixer.Finished = () => _jobs != null && _jobs.ReadyToCollect;

                // Not on the map until you are one of theirs. The opening is one man and one
                // icon, and Lamar is a Families man who has never met you.
                _fixer.Known = () => _crew != null && _crew.IsAffiliated;
                _fixerTalk.BlockUnderAttack = () => _war != null && _war.IsRunning;

                _bigjTalk = new ArmourerTalk(_bigj, _crew, _state);

                // The rack is a screen now. The conversation is still how you get to it -- you
                // walk up to a man and he says something -- but what he shows you once you have
                // asked is a laid-out stock list rather than five pages of dialogue choices.
                _gunScreen = new GunScreen(_state);

                // AFTER _gunScreen EXISTS, which is the whole point of it being here.
                //
                // This block sat a hundred and sixteen lines earlier and set .Locker on a field
                // that had not been constructed yet -- a NullReferenceException out of the
                // constructor, which parks the mod for the session. The diagnostic panel caught
                // it and said so, which is the one good thing about the afternoon.
                _locker = new Weapons.GunLocker(_state, _weapons);
                _gunScreen.Locker = _locker;
                _postUp.Locker = _locker;
                _jobs.Locker = _locker;
                _bigjTalk.Rack = () => _gunScreen.Open();

                // Wired at last. GunScreen has declared this since the rack became a screen,
                // documents it as "set by Main", and invokes it on every sale -- and Main has
                // never assigned it, so the armourer took your money in total silence and the
                // compiler said so on every build. Buying off him is the one moment on that
                // screen where there is a man on the other side of it.
                _gunScreen.OnBought = (piece, rounds) =>
                {
                    var lines = rounds ? ArmourerTalk.OverTheAmmo : ArmourerTalk.OverTheCounter;
                    if (lines.Length == 0) return;

                    Dialogue.Say(_bigj.Name, lines[_rng.Next(lines.Length)]);
                };
                _carScreen = new CarScreen(_hao);

                _haoTalk = new HaoTalk(_hao, _state);
                _haoTalk.Showroom = () => _carScreen.Open();

                _carScreen.OnBought = car =>
                {
                    Dialogue.Say(_hao.Name, "Keys are in it. Don't bring it back.");
                };

                _hao.Talk = _talk;
                _hao.Showroom = () => _carScreen.Open();
                _hao.TalkBuilder = () =>
                {
                    _talk.Title = "Hao's";
                    return _haoTalk.Root();
                };

                _bigj.Talk = _talk;
                _bigj.TalkBuilder = () =>
                {
                    // Not "the table". There is no table -- he stands in a courtyard next to
                    // a couple of crates -- and a screen naming furniture that is not there is
                    // the sort of small wrongness that makes the whole thing read as written
                    // rather than seen.
                    _talk.Title = "Hood Weaponry";
                    _talk.TheirVoice = ArmourerTalk.Voice;
                    return _bigjTalk.Root();
                };
                _juanTalk = new DealerTalk(_delivery, _drugs, _pricing, _state, _crew)
                {
                    House = _stash.Stash
                };

                _delivery.Talk = _talk;

                // Who is null for a delivery: the screen reads the courier off the run itself.
                _delivery.TalkBuilder = () =>
                {
                    _juanTalk.Who = null;
                    return _juanTalk.Root();
                };

                // And named when you have walked up to somebody instead, so the same screen
                // quotes the same man's prices either way.
                // So a dealer stood on a corner during a raid is on the same side as the men
                // defending it. See DealerManager.GroupFor.
                _dealers.GroupFor = id =>
                {
                    var gang = _gangs == null ? null : _gangs.Get(id);
                    return gang == null ? 0 : gang.GroupHash;
                };

                _dealers.Talk = _talk;
                _dealers.State = _state;
                _dealers.GangById = id => _gangs.GetLoose(id);
                // Joining is asked of the man on the corner now. See DealerTalk.PutMeOnRow.
                _juanTalk.PutMeOn = def =>
                {
                    var gang = _gangs.Get(def.GangId);
                    if (gang == null) return "He don't speak for nobody.";

                    return _leaders.JoinThrough(gang, _drugs, def.Name,
                                                def.JoinAccept, def.JoinRefuse, def.JoinAlready);
                };

                _juanTalk.JoinRefusal = def =>
                {
                    var gang = _gangs.Get(def.GangId);
                    if (gang == null) return "He don't speak for nobody";

                    if (_crew.IsAffiliated)
                    {
                        return _crew.Current.Id == gang.Id
                            ? "You already run with them"
                            : "You run with " + _crew.Current.Name;
                    }

                    return _state.Respect < gang.JoinRespect
                        ? "Need " + gang.JoinRespect.ToString("F0") + " respect"
                        : null;
                };

                _dealers.TalkBuilder = def =>
                {
                    _juanTalk.Who = def;
                    return _juanTalk.Root();
                };

                // He delivers to an address, so he needs the address -- and the only place you
                // can call him from is standing at it.
                _delivery.AtHome = () => _stash.AtDoor;
                _dealers.AtHome = () => _stash.AtDoor;
                _delivery.HouseDoor = _stash.Position;
                _delivery.House = _stash.Stash;

                var pages = new WheelPages(_cfg, _state, _drugs, _pricing, _cutting,
                                           _gangs, _crew, _turf, _dealers, _weapons,
                                           _stash, _postUp, _leaders);

                pages.Info = _info;
                pages.Delivery = _delivery;
                pages.WorkWaiting = () => _jobs == null ? null : _jobs.WorkWaiting;
                pages.StashScreen = _stashScreen;
                pages.PocketScreen = _pocketScreen;
                pages.Bags = _bags;
                pages.Crew = _homies;
                pages.ShowSocials = () => _socialScreen.Open();
                pages.ShowMessages = () => _messages.Open();
                pages.Jobs = _jobs;

                // The app asks the wheel who can be reached and hands ids back to it, so both
                // doors onto a re-up run the same call with the same refusals. See PhoneBook.
                _messages.Contacts = pages.PhoneBook;
                _messages.TextContact = pages.TextContact;

                // A text arriving is a change worth saving. Without this the inbox only reaches
                // the disk when something else happens to be dirty, so the last few messages of
                // a session -- which are the ones you would actually want back -- are the ones
                // that get lost.
                Social.Inbox.Changed = () => _state.Touch();
                pages.ShowSettings = () => _settingsScreen.Open(_cfg, pages.ResetOptions());

                // The two settings that are COPIED rather than read live, pushed again whenever
                // the screen changes anything. Both would otherwise have looked broken: the log
                // level is handed to Log once at load, and the toast switch was read into the
                // toaster when it was built -- change either on the screen and nothing happened
                // until the next restart, which is the exact failure this screen exists to end.
                _settingsScreen.Changed = () =>
                {
                    Log.Level = _cfg.LogLevel;
                    if (_toasts != null) _toasts.Enabled = _cfg.TweetsOnTheRight;
                };
                pages.Followers = () => _social.Followers;
                pages.WipeSocials = () => _social.Wipe();

                pages.AllJobs = () =>
                {
                    var ids = new List<string>();
                    foreach (var def in _missions.All)
                    {
                        if (def != null && !string.IsNullOrEmpty(def.Id)) ids.Add(def.Id);
                    }
                    return ids;
                };

                // The feed screen posts now, so it gets what the wheel page used to hold. The
                // wedge is a door and nothing else.
                _socialScreen.Gangs = _gangs;
                _socialScreen.Crew = _crew;
                _socialScreen.PaybackDue = () => _payback != null && _payback.IsOwed;

                _socialScreen.Say = set =>
                {
                    // Stood on a corner, the day post is about the corner.
                    //
                    // Not spelled out. It goes up as the product's street name and the
                    // neighbourhood, which is what somebody actually posts -- a man announcing
                    // his inventory to a public feed is a man who gets a visit.
                    if (set == "YouDaily" && _postUp != null && _postUp.IsPosted)
                    {
                        var code = _postUp.CodeWord;

                        if (!string.IsNullOrEmpty(code) &&
                            _social.PostAsYou("YouPosted", code) != null)
                        {
                            return true;
                        }
                    }

                    return _social.PostAsYou(set, "") != null;
                };

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
                _newsroom = new Social.Newsroom(_social);

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



                _menu = new Phone.PhoneMenu(_cfg);
                _phone = new Phone.PhoneController(_cfg, _menu, pages.BuildRoot);

                // A conversation is not a "screen" as far as the frame chain is concerned, so
                // it would otherwise be talked over by a handset appearing in front of it.
                _phone.Busy = () => _talk.IsOpen;

                pages.ShowVanillaPhone = () => _phone.ShowVanillaPhone();

                Interval = 0;
                Tick += OnTick;
                Aborted += OnAborted;

                Log.Info("Paths: data=" + Paths.Data + "  writable=" + Paths.Writable);

                // Skulls left on the map by a build that no longer exists. See StaleBlips.
                UI.StaleBlips.Sweep();

                // A save where the old man was already told loads straight into the world it
                // left. The choice is written down; the dock has to agree with it.
                if (_state != null && _state.ToldTheOldMan) _dealers.Handover("docks");


                // STARTED IS NOT THE SAME AS HEALTHY -- but it is also not worth a wall across
                // the screen. A mod that is running gets a ticker and a log; the panel is for
                // the case where nothing is running at all, which is the only time somebody
                // needs telling in letters that big.
                var niggles = Preflight.Check();

                if (niggles.Count > 0)
                {
                    for (var i = 0; i < niggles.Count; i++)
                    {
                        Log.Error("  " + niggles[i].What);
                        Log.Error("    fix: " + niggles[i].Fix);
                    }

                    Notify.Problem(niggles.Count + " thing" + (niggles.Count == 1 ? "" : "s") +
                                   " wrong with the install -- see Hoodrich.log.");
                }

                Log.Info(Build.Name + " " + Build.Version + " loaded. Phone: phone button" +
                         (_cfg.PhoneKey == System.Windows.Forms.Keys.None
                             ? ""
                             : " or " + _cfg.PhoneKey) +
                         ". The weapon wheel is the game's again.");
            }
            catch (Exception ex)
            {
                _parked = true;
                Log.Error(Build.Name + " failed to initialise and is disabled for this session.", ex);

                // AND SAY SO ON SCREEN. Parking silently is indistinguishable from not being
                // installed, and the player's next move is to reinstall -- which fixes nothing,
                // because what was wrong was never the mod.
                //
                // The preflight runs here rather than at the top so that a startup which threw
                // gets its reason attached to the crash: nine times in ten the exception is a
                // symptom of the missing file or the wrong loader, and reporting both together
                // is the difference between a fix and a guess.
                var why = Preflight.Check();

                why.Add(new Fault
                {
                    Fatal = true,
                    What = "Startup threw: " + ex.GetType().Name + " -- " + ex.Message,
                    Fix = "If nothing above explains it, this is one for the mod author. " +
                          "Hoodrich.log has the full stack."
                });

                _trouble.Raise("could not start", why);

                Tick += OnBrokenTick;
            }
        }

        /// <summary>
        /// The only thing a dead mod still does: explain itself.
        ///
        /// Hooked instead of OnTick when startup fails, so the panel gets drawn without any of
        /// the systems behind it existing -- most of them are null on this path, and a tick
        /// that touched them would throw every frame and bury the reason in a wall of noise.
        /// </summary>
        private void OnBrokenTick(object sender, EventArgs e)
        {
            try { _trouble.Draw(); }
            catch { /* nothing left to fall back to */ }
        }

        private void OnTick(object sender, EventArgs e)
        {
            // BEFORE THE ENABLED CHECK, and before Franklin. Somebody whose install is broken
            // is not necessarily playing as Franklin when they find out, and a report they can
            // only see by switching character is a report they will not see.
            if (_trouble.IsOpen)
            {
                _trouble.Draw();
                return;
            }

            if (_parked || _cfg == null || !_cfg.Enabled) return;

            // FRANKLIN'S MOD. Michael and Trevor get none of it.
            //
            // Everything in here is his: his set, his block, his phone, his fifteen-year-old
            // history with Lamar. A retired bank robber in Rockford Hills opening a phone that
            // says Chamberlain Gangster Families on it is not a feature, and neither is
            // Gerald's front door glowing on the map while you are flying a plane in Sandy
            // Shores.
            //
            // A hard return rather than a flag on each system, because "off" has to mean off:
            // no blips, no props, no dealers, no cards, nothing spawning, nothing listening for
            // the phone button.
            //
            // The teardown runs ONCE on the way out. Returning early on its own would freeze
            // the world exactly as Franklin left it -- his stash house still marked, the men
            // outside Gerald's still stood there -- for whoever you switched to. Coming back is
            // free: every one of these systems stands its own world back up on the next tick.
            if (!IsFranklin())
            {
                if (!_asleep)
                {
                    _asleep = true;
                    Log.Info("Not Franklin. Standing down until he is back.");

                    try { TryRestore(); }
                    catch (Exception ex) { Log.Debug("Could not stand down cleanly: " + ex.Message); }
                }

                return;
            }

            if (_asleep)
            {
                _asleep = false;
                Log.Info("Franklin again. Back on.");
            }

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

                // No lid. Every frame rather than on mounting, because the game hands one out
                // the moment he sits on a bike and would do it again on the next one -- and
                // it is two natives on a bare head, one of which is only asked.
                Core.Helmets.Off(Game.Player.Character);

                // Before any of the full-screen UIs, every one of which returns early. A narc
                // on the phone and a corner you are stood on both run on wall time, and a
                // countdown that stops because you opened a menu is a countdown you can beat by
                // opening a menu.
                // Before anything else reads a control. A screen that closed a moment ago is
                // still holding the button that closed it.
                InputGuard.Tick();

                _bust.Update();
                _postUp.Update();

                // What is on you owns the screen the same way moving it does. Closed if the
                // mod goes unavailable underneath it, so a screen cannot outlive the thing it
                // is a view of.
                if (_pocketScreen.IsOpen)
                {
                    if (!available) _pocketScreen.Close();
                    else
                    {
                        _pocketScreen.Update();
                        _pocketScreen.Draw();
                        SlowTick();
                        _failures = 0;
                        return;
                    }
                }

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
                if (_carScreen.IsOpen)
                {
                    // Closed by walking away, the same as every other counter in the mod.
                    if (!available || !_hao.InReach) _carScreen.Close();
                    else
                    {
                        _carScreen.Update();
                        _carScreen.Draw();
                        SlowTick();
                        _failures = 0;
                        return;
                    }
                }

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
                if (_settingsScreen.IsOpen)
                {
                    if (!available) _settingsScreen.Close();
                    else
                    {
                        _settingsScreen.Update();
                        _settingsScreen.Draw();

                        SlowTick();
                        _failures = 0;
                        return;
                    }
                }

                // KEPT UP THROUGH EVERY SCREEN, and above all of them on purpose.
                //
                // Each of the branches below returns out of the tick while its screen is open,
                // so anything after them stops happening -- and one of the things that stopped
                // was the house being kept quiet. Standing in Denise's front room with the
                // phone open, she started talking again. This is the one thing that has to run
                // whatever is on screen, so it runs before anything can return past it.
                if (_stash != null) _stash.KeepQuiet();

                if (_messages.IsOpen)
                {
                    if (!available) _messages.Close();
                    else
                    {
                        _messages.Update();
                        _messages.Draw();

                        // THE WORLD KEEPS TURNING WHILE YOU READ, which for this screen is the
                        // whole point rather than a nicety.
                        //
                        // The plug's reply is not sent when you press send -- Delivery holds
                        // the phone to Franklin's ear for a moment and posts his answer on a
                        // later tick. This branch returns before that tick ever happens, so the
                        // reply could not arrive until you closed the app: you sent a message
                        // into a thread and nothing came back while you sat looking at it.
                        //
                        // The prompts are deliberately NOT ticked. Those draw on the world and
                        // would paint through the panel.
                        _dealers.Update(_turf, _crew, _state);
                        _delivery.Update();

                        SlowTick();
                        _failures = 0;
                        return;
                    }
                }

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

                // Once, on a save that has never seen it.
                //
                // Almost nothing in this mod is discoverable by pressing things. Weight cannot
                // be sold until it has been cut, the wheel is on a button that already does
                // something else, and a corner sells at a rate set by how busy the pavement is.
                // Somebody who does not know that buys a kilo, stands on a corner, sells
                // nothing and concludes the mod is broken.
                //
                // Gated on being able to act rather than on a timer, so it does not open behind
                // a loading screen or during a cutscene, and the flag is set the moment it is
                // shown -- not when it is closed -- because a player who dismisses it with the
                // pause menu should not be handed it again on the next load.
                if (available && _state != null && !_state.SeenWelcome && !_info.IsOpen
                    && !_menu.IsOpen && !_talk.IsOpen)
                {
                    _state.SeenWelcome = true;
                    _state.Touch();

                    _info.Open("Posted Up", "Everything you need, once", Welcome.Pages());
                    Log.Info("Showed the first-run guide.");
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
                        _hao.ReleaseFromTalk();
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

                _phone.Update(available);

                if (available)
                {
                    _crew.Update();
                    _turf.Update();
                    _dealers.Update(_turf, _crew, _state);
                    _dealers.GreetIfNeeded();
                    _dealers.UpdatePrompt();
                    _delivery.Update();
                    _delivery.UpdatePrompt();
                    _cutting.Update();
                    _deadDrop.Update();
                    _market.Update(_drugs);
                    _blocks.Update(_turf == null ? "" : _turf.ZoneCode);
                    _locker.Update();
                    _raid.Update();
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

                    _hao.Update();
                    if (_ownedCars != null) _ownedCars.Update();
                    _hao.UpdatePrompt();

                    _social.Update();

                    // The desk, watching the street for the two things nothing else reports.
                    if (_newsroom != null) _newsroom.Update();

                    // And anything you have put down, waiting to be picked back up.
                    if (_bags != null) _bags.Update();

                    if (_homies != null) _homies.Update();

                    // The van, the port, and the man waiting on the other end of it.
                    if (_port != null) _port.Update();

                    _copWatch.Update();
                    _block.Update();
                    _couch.Update();
                    _stove.Update();
                    _armourerStockA.Update();
                    _armourerStockB.Update();
                    foreach (var door in _doors) door.Update();
                    _traffic.Update();
                    _payback.Update();
                    _rollers.Update();
                    _patrol.Update();
                    _war.Update();

                    _lamarCrew.Update();
                    if (_leaderCrew != null) _leaderCrew.Update();
                    _armourerCrew.Update();
                    _labCrew.Update();
                    _denCrew.Update();
                    _growWorkers.Update();
                    _pressWorkers.Update();
                    foreach (var car in _cars) car.Update();
                    _party.Update();
                    _meet.Update();
                    _partyBarrel.Update();
                    _partyCouch.Update();
                    _partyDog?.Update();
                    _leaderBox.Update();
                    foreach (var prop in _scenery) prop.Update();
                    _decks.Update();
                    _partyDecks.Update();
                    _jobs.Update();
                }

                // The whole HUD stands down while the phone is up, and it has to be a whole
                // -- nothing draws in front of it, because everything below this line runs
                // AFTER the phone has rendered and would therefore land on top of it.
                //
                // That is a change in kind from the wheel, which sat in the middle of the
                // screen and only ever clashed with the banners across the top. The phone
                // stands on the RIGHT, which is where the posted-up readout, the deal prompts
                // and the licence strip all live -- so half the HUD would have been drawn
                // through the handset rather than merely near it.
                //
                // Help.Tick is the one exception and stays live: it is the bottom-centre
                // prompt, it is how the player is told what a button does, and it is the only
                // thing here that is not competing for the same corner.
                if (!_phone.IsOpen)
                {
                    _war.Draw();
                    _jobs.Draw();
                    if (_port != null) _port.Draw();
                }

                // The prompt, every frame rather than every tick, for exactly the reason the
                // spotlight below is. Anything that asked for it this tick, or recently
                // enough, gets re-issued now -- see Help.
                UI.Help.Tick();

                // The button that closed a conversation stays off the trigger for a moment
                // after the panel has gone -- see Conversation.TickQuiet.
                UI.Conversation.TickQuiet();

                // The green "+$" the game leaves behind after we pay somebody. See Cash.
                UI.Cash.Tick();

                // The spotlight, every frame rather than every tick -- a beam that exists for
                // one frame in nine is a strobe.
                _patrol.Draw();

                if (!_phone.IsOpen)
                {
                    _cutting.Draw();
                    _bust.Draw();
                    _postUp.Draw();
                    _stash.Draw();
                    _sleep.Draw();
                    _kitchen.Draw();
                }

                // Last, deliberately. Everything above owns a specific spot and has first
                // claim on the key; this is whoever happens to be stood there otherwise.
                if (!_phone.IsOpen) _blockTalk.Draw();

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
                    Log.Error("Too many consecutive failures; " + Build.Name +
                              " is parked for this session.");
                    Notify.Failure("shut itself off for this session. Check the log.");
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

            // The sink is inside Denise's house and cutting is not optional, so a door the
            // story locked is the middle of the loop gone. See HouseDoors.
            _houseDoors.Update();

            // Contact pictures pulled in early, so the first text of a session has a face on it
            // rather than losing the race with its own texture request. Stops asking once they
            // are in.
            UI.Faces.Warm();

            if (_cfg.SaveIntervalSeconds > 0 && now - _lastSave >= _cfg.SaveIntervalSeconds * 1000)
            {
                _lastSave = now;
                SaveGame.Save(_state, _crew, _market, _stash, false, _jobs.Paint, _blocks);
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

                // Dead means the hospital took everything, which is the game doing what the
                // locker is explicitly not meant to undo.
                if (player.IsDead && _locker != null) _locker.TakenOffHim("wasted");

                if (player.IsDead)
                {
                    // Read on the frame he goes down, not when he wakes up. The game clears
                    // the source and the cause once the ped is respawned, so waiting until
                    // Pillbox means asking a question that no longer has an answer.
                    if (!_wasDown)
                    {
                        RememberWhoGotYou(player);

                        // And the job ends here, on the frame he goes down. This is the only
                        // code still running at that moment: the whole gameplay tick is gated
                        // on IsPlayable, which is false while he is dead, so anything that
                        // waited for the runner's own update waited until he was alive again
                        // at Pillbox -- by which point the mission had simply resumed.
                        if (_jobs != null) _jobs.Died();
                    }

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
            // The batch finishes after this screen has closed, so Cutting needs the cupboard
            // itself rather than borrowing the kitchen's copy of it.
            _cutting.House = _stash == null ? null : _stash.Stash;

            _cook.Open(_state.Stash, _stash == null ? null : _stash.Stash, _drugs, _pricing,
                       (drug, output, grams, purity) =>
                           _cutting.TryStart(drug, output, grams, purity));
        }

        /// <summary>True when the player is in normal control and the mod should be live.</summary>
        /// <summary>
        /// Whether the man on screen is Franklin.
        ///
        /// By MODEL rather than by the character-switch index, because the model is what is
        /// true right now -- during a switch, in a cutscene that borrows a body, and on a save
        /// loaded straight into somebody else. The index has to be asked for and can be stale;
        /// the ped standing there cannot be.
        /// </summary>
        private static bool IsFranklin()
        {
            try
            {
                var player = Game.Player?.Character;
                if (player == null || !player.Exists()) return false;

                if (_franklinModel == 0)
                {
                    _franklinModel = Function.Call<int>(Hash.GET_HASH_KEY, FranklinModelName);
                }

                return player.Model.Hash == _franklinModel;
            }
            catch
            {
                // Cannot tell who it is, so do nothing. Off is the safe answer.
                return false;
            }
        }

        /// <summary>player_one is Franklin. Zero is Michael and two is Trevor.</summary>
        private const string FranklinModelName = "player_one";

        private static int _franklinModel;

        /// <summary>True while the mod is stood down because somebody else is on screen.</summary>
        private bool _asleep;

        /// <summary>
        /// A car the player actually paid for, by plate.
        ///
        /// OwnedCars stamps a plate on everything bought off Hao and that plate is the one
        /// thing about the car nothing else in the world shares -- which makes it the only
        /// honest way to ask this question about a vehicle handle that may have been rebuilt
        /// since the sale.
        /// </summary>
        private bool OwnedByPlayer(Vehicle car)
        {
            if (car == null || !car.Exists() || _state == null) return false;

            try
            {
                var plate = Function.Call<string>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT, car.Handle);
                if (string.IsNullOrEmpty(plate)) return false;

                foreach (var owned in _state.Owned)
                {
                    if (string.Equals(owned.Plate, plate, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Cannot read the plate, so cannot claim it.
            }

            return false;
        }

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
                SaveGame.Save(_state, _crew, _market, _stash, true, _jobs.Paint, _blocks);
                Log.Info(Build.Name + " unloaded cleanly.");
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

            try { _phone?.RestoreWorld(); }
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
            try { _hao?.RestoreWorld(); } catch { /* teardown */ }
            try { _ownedCars?.RestoreWorld(); } catch { /* teardown */ }
            try { _socialScreen?.RestoreWorld(); } catch { /* teardown */ }
            try { _block?.RestoreWorld(); } catch { /* teardown */ }
            try { _couch?.RestoreWorld(); } catch { /* teardown */ }
            try { _stove?.RestoreWorld(); } catch { /* teardown */ }
            try { _bags?.RestoreWorld(); } catch { /* teardown */ }
            try { _port?.RestoreWorld(); } catch { /* teardown */ }
            try { _homies?.RestoreWorld(); } catch { /* teardown */ }
            try { _armourerStockA?.RestoreWorld(); } catch { /* teardown */ }
            try { _armourerStockB?.RestoreWorld(); } catch { /* teardown */ }
            foreach (var door in _doors)
            {
                try { door.RestoreWorld(); } catch { /* teardown */ }
            }
            try { _war?.RestoreWorld(); } catch { /* teardown */ }
            try { _payback?.RestoreWorld(); } catch { /* teardown */ }
            try { _turf?.RestoreWorld(); } catch { /* teardown */ }
            try { _rollers?.RestoreWorld(); } catch { /* teardown */ }
            try { _patrol?.RestoreWorld(); } catch { /* teardown */ }
            try { _lamarCrew?.RestoreWorld(); } catch { /* teardown */ }
            try { _leaderCrew?.RestoreWorld(); } catch { /* teardown */ }
            try { _armourerCrew?.RestoreWorld(); } catch { /* teardown */ }
            try { _labCrew?.RestoreWorld(); } catch { /* teardown */ }
            try { _denCrew?.RestoreWorld(); } catch { /* teardown */ }
            try { _growWorkers?.RestoreWorld(); } catch { /* teardown */ }
            try { _pressWorkers?.RestoreWorld(); } catch { /* teardown */ }
            foreach (var car in _cars)
            {
                try { car.RestoreWorld(); } catch { /* teardown */ }
            }
            try { _party?.RestoreWorld(); } catch { /* teardown */ }
            try { _meet?.RestoreWorld(); } catch { /* teardown */ }
            try { _partyBarrel?.RestoreWorld(); } catch { /* teardown */ }
            try { _partyCouch?.RestoreWorld(); } catch { /* teardown */ }
            try { _partyDog?.RestoreWorld(); } catch { /* teardown */ }
            try { _leaderBox?.RestoreWorld(); } catch { /* teardown */ }

            try { _decks?.RestoreWorld(); } catch { /* teardown */ }
            try { _partyDecks?.RestoreWorld(); } catch { /* teardown */ }

            foreach (var prop in _scenery)
            {
                try { prop.RestoreWorld(); } catch { /* teardown */ }
            }
            try { _jobs?.RestoreWorld(); } catch { /* teardown */ }
            try { _stash?.RestoreWorld(); } catch { /* teardown */ }
        }
    }
}
