using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Territory;

namespace Hoodrich.Gangs
{
    /// <summary>What one of them is doing at this moment.</summary>
    internal enum RollPhase
    {
        Rolling,
        Sitting
    }

    /// <summary>One carload of them, or one man on a bike.</summary>
    internal sealed class Roll
    {
        public Vehicle Car;
        public Ped Driver;
        public readonly List<Ped> Crew = new List<Ped>();

        /// <summary>A bike on the pavement rather than a car on the road.</summary>
        public bool OnFoot;

        public RollPhase Phase = RollPhase.Rolling;

        /// <summary>Where they are headed, and whether that spot is worth stopping at.</summary>
        public Vector3 Target;
        public bool StopThere;

        /// <summary>When the sitting ends.</summary>
        public int SitUntil;

        /// <summary>When his speed is next reconsidered, and what it was set to. See Pace.</summary>
        public int PaceAt;
        public float Cruise;

        /// <summary>Where they were when last looked at, and when that was.</summary>
        public Vector3 WasAt;
        public int LookedAt;

        /// <summary>How many times they have been unstuck before being written off.</summary>
        public int Nudges;

        public int BornAt;

        /// <summary>
        /// The one this bike is riding with, or null for a man on his own.
        ///
        /// A REFERENCE RATHER THAN AN ID, because the only question ever asked of it is "where
        /// is he going" and the answer is a field on the other Roll. An id would mean a lookup
        /// through the list every time, for a list that is four long.
        ///
        /// The lead can die, crash, or be let go for being stuck while the rest are still out.
        /// Nothing repairs that and nothing needs to: a mate whose lead is gone falls back to
        /// picking his own way, which is what a man whose mate has ridden off does.
        /// </summary>
        public Roll Lead;

        /// <summary>While this is in the future, the front wheel is up.</summary>
        public int WheelieUntil;

        /// <summary>And not another one before this, so it is a moment and not a stance.</summary>
        public int WheelieAfter;

        /// <summary>When they were first noticed off the blocks, or nought while they are on.</summary>
        public int StrayedAt;

        /// <summary>
        /// The circle he is riding while he hangs about, and where round it he is.
        ///
        /// A BIKE THAT ARRIVES AND STOPS IS A PARKED BIKE. Sitting was written for a carload
        /// pulling up at a kerb, which is exactly what a carload does -- but four men who rode
        /// to the courts together do not sit on stationary bikes for forty seconds looking at
        /// each other. They ride round each other, and every so often somebody holds it on the
        /// brake and fills the place with smoke.
        ///
        /// Its own radius and its own place on it per rider, so four of them are four laps
        /// rather than one queue.
        /// </summary>
        public Vector3 Ring;
        public float RingRadius;
        public float RingAngle;

        /// <summary>When the next point on the lap is handed out.</summary>
        public int LapAt;

        /// <summary>While this is in the future the back wheel is going. And when the next one starts.</summary>
        public int BurnUntil;
        public int BurnAfter;

        // ---- riding the park. See Rollers.Ride and Gangs.Park. ------------------------

        /// <summary>Riding round the park rather than a circle.</summary>
        public bool InPark;

        /// <summary>His time is up and he is riding to the way out.</summary>
        public bool Leaving;

        /// <summary>Out of the park and ready to be pointed somewhere else.</summary>
        public bool ParkDone;

        /// <summary>The route he is on over the park's spots, and the next spot along it.</summary>
        public List<int> Path;
        public int PathAt;

        /// <summary>The straight line he is riding now: which spot it ends at, and where that is.</summary>
        public int LegNode = -1;
        public Vector3 LegTo;

        /// <summary>When he is next looked at, and how fast he was going the last time.</summary>
        public int WatchAt;
        public float LastSpeed;

        /// <summary>Held up -- somebody in front of him, or a brake he was given -- and since when.</summary>
        public bool Braking;
        public int BrakeSince;

        /// <summary>Whether he has got anywhere lately.</summary>
        public int ProgressAt;
        public Vector3 ProgressFrom;
        public int Stalls;

        /// <summary>When he last passed each spot, so he rides all of the park and not one corner.</summary>
        public readonly Dictionary<int, int> Seen = new Dictionary<int, int>();

        // ---- riding the path you recorded. See Rollers.RidePath and Gangs.BikePath. --------

        /// <summary>Which way along the path he is going: 1 with it, -1 back along it.</summary>
        public int Way = 1;

        /// <summary>The path he joined, by BikePath.Version. Nought: he is not on one.</summary>
        public int PathVersion;

        /// <summary>His time up and on his way out along the path: by when he is out regardless.</summary>
        public int LeaveBy;

        /// <summary>Just off the path: not taken back onto it by riding past its start before this.</summary>
        public int PathAgainAt;
    }

    /// <summary>
    /// Their own people, out driving their own blocks.
    ///
    /// Everything in this mod that arrives by car arrives because of you: a delivery, a
    /// payback, a raid. Which means the only Families car you ever see is one that wants
    /// something, and a neighbourhood where every vehicle is plot is a neighbourhood nobody
    /// lives in. These want nothing. Four of them go round the block at thirty, pull into an
    /// alley, sit there a while, and go round again -- and they will still be doing it after
    /// you have walked off, which is the whole point of them.
    ///
    /// Two ways of moving, because a block is not only its roads. The cars use the road
    /// network. The bikes are given pavement to aim at and told to take the short way, which
    /// puts them up on the footpath cutting between the yards -- the way anybody actually rides
    /// round here.
    ///
    /// Only on our own turf, and only while you are stood on it. There is no world simulation
    /// behind this and there should not be: spawning carloads of Families across the map on the
    /// chance you drive past is a memory bill for something nobody sees.
    /// </summary>
    internal sealed class Rollers
    {
        private const int TickMs = 900;

        /// <summary>
        /// Far enough out to arrive rather than appear.
        ///
        /// Nothing is spawned inside seventy-five metres. A car that materialises at the end of
        /// the street is a bug report; the same car coming round the corner is traffic.
        /// </summary>
        private const float SpawnNear = 75f;
        private const float SpawnFar = 145f;

        /// <summary>Past this they are somebody else's problem, so they are handed back.</summary>
        private const float LetGoRange = 260f;

        /// <summary>
        /// How long one gets to find its way back onto the blocks before it is let go.
        ///
        /// Long enough to drive round a corner and short enough that nobody follows a green
        /// Manchez onto the freeway wondering where it thinks it is going.
        /// </summary>
        private const int StrayMs = 20000;

        private const int GapMinMs = 8000;
        private const int GapMaxMs = 24000;

        /// <summary>How long they sit in an alley with the engine running.</summary>
        private const int SitMinMs = 12000;
        private const int SitMaxMs = 45000;

        /// <summary>Chance the next place they are headed is somewhere they will stop.</summary>
        private const int StopChancePercent = 45;

        /// <summary>
        /// And the same for a bike, which used to be nought.
        ///
        /// Bikes never stopped. The stop roll was written !OnFoot, so every rider went round
        /// and round until his eight minutes were up and he was handed back -- which is fine as
        /// traffic and useless as life. Somebody riding round the block is going somewhere;
        /// somebody stood over his bike in the park with two mates IS the block.
        /// </summary>
        private const int BikeStopChancePercent = 55;

        /// <summary>How many ride out together when they ride out together.</summary>
        private const int PackMin = 2;

        /// <summary>Up to four since 2026-09-24: Michael wants them round his path in groups sometimes.</summary>
        private const int PackMax = 4;

        /// <summary>Chance a bike going out brings his crew rather than going alone. Half, since 2026-09-24.</summary>
        private const int PackChancePercent = 50;

        /// <summary>
        /// How far off the lead's own spot the rest of a crew are aimed.
        ///
        /// Not zero, because three bikes given one identical coordinate arrive as three bikes
        /// trying to occupy one square metre, and the game resolves that by shoving them.
        /// </summary>
        private const float PackSpread = 6.5f;

        /// <summary>
        /// Pulling the front up.
        ///
        /// There is no native for this. TASK_VEHICLE_TEMP_ACTION has a list of stunts and none
        /// of them is a wheelie, so it is done the way it is done in the physics: a shove
        /// upwards applied BEHIND the centre of mass, which pitches the nose up and leaves the
        /// back wheel driving. Held frame by frame while it lasts rather than applied once,
        /// because one impulse at 900ms intervals is a bump in the road, not a wheelie.
        ///
        /// Wanting speed is what keeps it honest. A bike doing walking pace that rears up is a
        /// bike being levitated; the same bike doing thirty is a rider showing off.
        /// </summary>
        private const int WheelieChancePercent = 30;
        private const int WheelieMinMs = 1100;
        private const int WheelieMaxMs = 2600;
        private const int WheelieRestMinMs = 7000;
        private const int WheelieRestMaxMs = 22000;
        private const float WheelieNeedsSpeed = 9f;
        private const float WheelieLift = 1.9f;
        private const float WheelieBehind = 1.15f;

        /// <summary>
        /// Where they stand about with the engines off.
        ///
        /// ONE COORDINATE, AND IT IS THE ONE THAT IS KNOWN GOOD. The Chamberlain courts are
        /// already in this mod -- Lamar rides out to them -- so it is a spot that has been
        /// stood on, driven to and filmed rather than a number read off a map. Everything else
        /// is found at runtime by Green() below, which is slower to write and cannot put a bike
        /// inside a wall.
        ///
        /// Add to it freely. A hangout is only ever used when the player is already near it, so
        /// a bad entry costs nothing anywhere else on the map.
        /// </summary>
        private static readonly Vector3[] Hangouts =
        {
            // The basketball courts in Chamberlain Hills.
            new Vector3(-227.173f, -1541.756f, 31.607f)
        };

        /// <summary>Near enough to be worth riding to rather than a trip across town.</summary>
        private const float HangoutRange = 320f;

        /// <summary>No road this close, and it is open ground rather than a kerb.</summary>
        private const float OffRoad = 15f;

        /// <summary>A road this close, and he is on one.</summary>
        private const float OnRoad = 7f;

        /// <summary>
        /// How hard the front comes up, out of the ini.
        ///
        /// In the ini because it is the one number here that cannot be reasoned about from a
        /// desk. It is a force against a mass the game owns, on a model that can be swapped,
        /// and the difference between a front wheel skimming and a bike on its back is a
        /// decimal place. Whoever is looking at it can move it; nobody who is not, cannot.
        /// </summary>
        private float Lift => _cfg == null ? WheelieLift : _cfg.RollerWheelieLift;

        /// <summary>Close enough to call it arrived.</summary>
        private const float ArrivedRange = 16f;

        /// <summary>Nothing lasts forever, so the same four are not circling all afternoon.</summary>
        private const int LifetimeMs = 480000;

        /// <summary>
        /// Stuck: less than this much movement between two looks, that many times over.
        ///
        /// Learnt from the gang war, where two men out of eight would find a fence and stand at
        /// it for the rest of the fight. A thing that cannot reach where it is going has to be
        /// given one more try and then taken off the board, or it stops being scenery and
        /// becomes a car parked across a junction.
        /// </summary>
        private const float StuckMoved = 3f;
        private const int StuckLookMs = 9000;
        private const int MaxNudges = 2;

        /// <summary>Slow. Around thirty for a car, a bit less for a bike.</summary>
        private const float CruiseCar = 8.5f;
        private const float CruiseBike = 6f;

        /// <summary>
        /// Normal road driving for the cars.
        ///
        /// The bikes get the shortest-path bit instead, which is what puts them over the kerb:
        /// aimed at a spot on the pavement and allowed to take the direct way to it, a bicycle
        /// mounts the footpath rather than going round by the road. Peds and objects are still
        /// avoided, so it is a bike weaving past people, not a bike through them.
        /// </summary>
        private const int StyleCar = 786603;
        private const int StyleBike = 262196;

        private const int PedTypeCiv = 4;

        /// <summary>
        /// What they turn up in.
        ///
        /// Read off the spawn menu's own hash rather than guessed at, so the name here is the
        /// model that was actually looked at. Half of them are 1.69 and later, which the Legacy
        /// build has never had -- hence the spares below and the check before spawning. A
        /// missing car is a different car here, not an exception in the log.
        /// </summary>
        private static readonly string[] Cars =
        {
            // EVERY NAME HERE WAS CHECKED AGAINST ITS HASH, not guessed at.
            //
            // The list they came from gave a hash beside each car, and a model hash is the
            // Jenkins one-at-a-time of the lowercase spawn name -- so every candidate could be
            // hashed and compared rather than typed from memory and hoped for.
            //
            // That immediately caught one: the Sentinel GTS was listed here as "sentinelgts",
            // "sentinel4" and "sentinel3", and the GTS is none of those. It is sentinel5.
            // "sentinelgts" is not a model at all, sentinel4 is a different car, and sentinel3
            // is the Sentinel Classic -- so the car this set was supposed to be known for has
            // never once spawned, and the fallback quietly handed over a classic instead. That
            // is exactly the failure the three-names-in-a-row trick was meant to prevent, and
            // it hid it instead.
            //
            // The Sentinel GTS stays three times over for the original reason: it should be the
            // car you associate with the set rather than one you see once an evening. It is
            // just the right name now.
            "sentinel5", "sentinel5", "sentinel5",

            "cavalcade3", "fq2", "rebla", "fr36", "dominator3", "dominator9",
            "gauntlet4", "ruiner4", "vigero2", "s95", "outlaw",

            // THE OLD METAL, checked the same way the rest of this list was: the spawn name
            // hashed and compared against the hash the game itself shows for the car, rather
            // than typed from memory. Greenwood 0x026ED430, Impaler LX 0xF55D2F7A (which is
            // impaler6, not impaler5), Manana Custom 0x665F785D, Tahoma Coupe 0xE478B977,
            // Vamos 0xFD128DFD. Every one of those matched on the first name tried except the
            // Impaler, which is exactly the reason the check exists.
            "greenwood", "impaler6", "manana2", "tahoma", "vamos"
        };

        /// <summary>
        /// And the donks, which are a third of what comes out.
        ///
        /// A separate list rather than more names in Cars for the same reason the set has a
        /// car it is known for: a donk is a statement and a Sentinel is transport, and mixing
        /// them into one bag would make both of them ordinary. Rolled for separately, so
        /// roughly one in three of the cars on the block is one of these and the rest are not.
        ///
        /// They get the same dark green as everything else and they get the underglow, which
        /// nothing else does -- see Paint. A green donk on chrome with the ground lit up under
        /// it is the loudest thing the set owns, and it should be, because that is the entire
        /// point of the car.
        /// </summary>
        private static readonly string[] Donks =
        {
            "faction3", "faction2", "voodoo", "chino2", "buccaneer2", "sabregt2", "virgo2"
        };

        /// <summary>How often one that comes out is a donk.</summary>
        private const int DonkChance = 34;

        /// <summary>
        /// Whether that car is one of them.
        ///
        /// Asked of the MODEL rather than remembered on the Roll, because Paint runs on a car
        /// that has just been made and the only true thing about it at that point is what it
        /// is. A flag set at the call site is a flag that will be right until somebody adds a
        /// second way to make one of these.
        /// </summary>
        private static bool Donk(Vehicle car)
        {
            try
            {
                if (car == null || !car.Exists()) return false;

                var hash = car.Model.Hash;

                foreach (var name in Donks)
                {
                    if (hash == Function.Call<int>(Hash.GET_HASH_KEY, name)) return true;
                }
            }
            catch
            {
            }

            return false;
        }

        /// <summary>Always present, for the install that has none of the above.</summary>
        private static readonly string[] SpareCars = { "buccaneer2", "voodoo", "manana", "primo2" };

        /// <summary>
        /// What they ride round the back streets with an engine: Street Blazers, Sanchezes.
        ///
        /// Cut back to those, which is what was asked for twice. The list had grown a
        /// scooter, two cheap sports bikes, a chopper and a couple of customs -- and a man
        /// on a Bagger is somebody riding through the neighbourhood rather than somebody
        /// from it. These are what is actually parked in these yards.
        /// </summary>
        private static readonly string[] Engines =
        {
            // SANCHEZ DIRT BIKES AND STREET BLAZER QUADS, two each, because Michael asked for
            // them by name on 2026-09-24 -- round his path, and they had been one bike in seven
            // and one in fourteen. A crew rides whatever its lead rides (see the matching in
            // Pick), so two of each here is a whole crew of them twice as often.
            "sanchez", "sanchez2",
            "blazer4", "blazer4",

            // The Manchez stays, which was asked for before: once now rather than twice. The
            // plain Blazer went -- it was the quad behind the Street Blazer, and he asked for
            // the Street Blazer.
            "manchez"
        };

        /// <summary>
        /// And what you pedal, which is a BMX and nothing else.
        ///
        /// This ran to eight bicycle-shaped things -- road bikes, a fixie, a mountain bike,
        /// the two electric ones -- and a man from the set on a Fixter is a man riding
        /// through the neighbourhood rather than somebody from it. In this part of town the
        /// bicycle is a BMX: it is what the game itself puts under the kids on Grove Street,
        /// and it is what was asked for. One entry, because the picker draws by count and
        /// there is nothing else to weigh it against.
        /// </summary>
        private static readonly string[] Pedals = { "bmx" };

        /// <summary>
        /// How often a rider is on a pedal bike rather than an engine. Was 57, the share the
        /// old mixed list gave BMXes by count; down to 40 on 2026-09-24 so the dirt bikes and
        /// quads he asked for are most of what comes out, and the BMX is still about.
        /// </summary>
        private const int PedalChance = 40;

        private static readonly string[] SpareBikes = { "bmx" };

        /// <summary>Dark green, out of the game's own paint table.</summary>
        private const int DarkGreen = 49;

        /// <summary>Metallic black. Index nought, which is the first entry in the game's own table.</summary>
        private const int MetallicBlack = 0;

        /// <summary>
        /// The flake over the top of it. A brighter green, so it lifts rather than flattens.
        ///
        /// Overridable in the ini and deliberately so -- see the note in Paint. If this one is
        /// not the green it should be it is one line to change and nothing is rebuilt.
        /// </summary>
        private const int DefaultPearl = Silver;

        /// <summary>Metallic silver. The flake, on everything the set owns.</summary>
        private const int Silver = 4;

        private readonly Settings _cfg;
        private readonly GangRegistry _gangs;
        private readonly string _gangId;
        private readonly TurfWatch _turf;
        private readonly Random _rng = new Random();

        private readonly List<Roll> _out = new List<Roll>();

        private int _lastTick;
        private int _nextSpawn;

        /// <summary>Off while something louder is happening. Wired by the house script.</summary>
        public Func<bool> Busy;

        /// <summary>How close you have to be for one of them to notice, and how often.</summary>
        private const float GreetRange = 22f;
        private const int GreetGapMs = 20000;
        private const int SecondBeepMs = 240;

        /// <summary>
        /// The settings are read every tick rather than copied at startup.
        ///
        /// Because the wheel can change them. A value copied into a field here would leave the
        /// switch on the settings page doing nothing until the next reload, which is a switch
        /// that appears broken -- and the whole reason that page exists is that changes take.
        /// </summary>
        private bool Enabled => _cfg == null || _cfg.RollersEnabled;

        private int MaxCars => _cfg == null ? 2 : _cfg.RollerCars;
        private int MaxBikes => _cfg == null ? 2 : _cfg.RollerBikes;

        public Rollers(Settings cfg, GangRegistry gangs, string gangId, TurfWatch turf)
        {
            _cfg = cfg;
            _gangs = gangs;
            _gangId = gangId;
            _turf = turf;

            _parkWorld = new ParkWorld(Ours, Moving);
            _awayFromBad = AwayFromBad;

            // The path you rode, if there is one. See BikePath.
            BikePath.Load();
        }

        // ---- per-tick ----------------------------------------------------------

        public void Update()
        {
            var now = Game.GameTime;

            // ABOVE THE THROTTLE ON PURPOSE. A wheelie is held by pushing on the bike every
            // frame it lasts; the same push at nine-hundred-millisecond intervals is a pothole.
            // Everything below here is a decision and can wait its turn, but this is physics
            // and has to run at the rate the physics does.
            Wheelies(now);

            // Read the road, on its own clock. See Pace.
            Pacing(now);

            // THE PARK, ALSO ABOVE THE THROTTLE. It is looked over a few dozen rays a frame,
            // and the riders in it are looked after five times a second -- nine hundred
            // milliseconds is four and a half metres at park speed, which is the far side of a
            // kicker ramp by the time anybody noticed it.
            try
            {
                SurveyPark(now);
                WatchRiders(now);
            }
            catch (Exception ex)
            {
                Log.Debug("Rollers: the park tripped: " + ex.Message);
            }

            // The second tap of a double beep, which has to be its own thing -- a horn is a
            // duration, so two beeps is two calls with a gap, and the gap cannot be a sleep.
            if (_secondBeepAt != 0 && now >= _secondBeepAt)
            {
                _secondBeepAt = 0;

                if (_beepCar != null && _beepCar.Exists()) GangPeds.Beep(_beepCar);

                _beepCar = null;
            }

            if (now - _lastTick < TickMs) return;
            _lastTick = now;

            try
            {
                Greet(now);

                Prune(now);

                if (!Enabled)
                {
                    // Turned off mid-session. Handed back rather than left circling, so the
                    // switch does something you can see out of the window.
                    if (_out.Count > 0)
                    {
                        foreach (var roll in _out) Release(roll);
                        _out.Clear();
                    }

                    return;
                }

                var player = Game.Player.Character;
                if (player == null || !player.Exists() || !player.IsAlive) return;

                // Backwards, because a carload that has given up is dropped where it stands and
                // a foreach over a list something is being removed from throws.
                for (var i = _out.Count - 1; i >= 0; i--)
                {
                    if (!Steer(_out[i], now)) continue;

                    Release(_out[i]);
                    _out.RemoveAt(i);
                }

                // Existing ones are left to finish whatever they were doing when you walked off
                // our blocks; new ones only start on them.
                if (!OnOurTurf()) return;
                if (Busy != null && Busy()) return;

                if (_nextSpawn == 0) _nextSpawn = now + _rng.Next(GapMinMs, GapMaxMs);
                if (now < _nextSpawn) return;

                _nextSpawn = now + _rng.Next(GapMinMs, GapMaxMs);

                // A bike is cheaper and reads better on a quiet street, so it wins the coin
                // toss more often than not.
                var wantBike = _rng.Next(100) < 72;

                if (wantBike && Count(true) < MaxBikes)
                {
                    var lead = Send(player, true, null);

                    // The rest of his crew, spawned around him and pointed wherever he is
                    // pointed. Held to the same ceiling as everybody else, so a pack is not a
                    // way round RollerBikes -- turning that down to two gets you pairs, and
                    // turning it to one gets you the mod as it was.
                    if (lead != null && _rng.Next(100) < PackChancePercent)
                    {
                        var mates = _rng.Next(PackMin, PackMax + 1) - 1;

                        for (var i = 0; i < mates && Count(true) < MaxBikes; i++)
                        {
                            Send(player, true, lead);
                        }
                    }
                }
                else if (Count(false) < MaxCars) Send(player, false, null);
                else if (Count(true) < MaxBikes) Send(player, true, null);
            }
            catch (Exception ex)
            {
                Log.Debug("Rollers tripped: " + ex.Message);
            }
        }

        private int Count(bool bikes)
        {
            var n = 0;
            foreach (var roll in _out)
            {
                if (roll.OnFoot == bikes) n++;
            }
            return n;
        }

        // ---- keeping them moving -----------------------------------------------

        /// <summary>
        /// One carload, one tick.
        ///
        /// Arriving is the only event here. Everything else -- sitting, moving off, being
        /// stuck -- hangs off whether they got where they were going, because the drive task
        /// stops the car at its destination and holds it there. That is why nothing needs to
        /// brake or park: a car told to drive to a point in an alley IS a car sat in an alley
        /// once it gets there, and all this has to do is not give it a new instruction for a
        /// while.
        /// </summary>
        /// <returns>True when this one has been given up on and should be let go.</returns>
        private bool Steer(Roll roll, int now)
        {
            if (roll.Car == null || !roll.Car.Exists()) return false;

            if (roll.Phase == RollPhase.Sitting)
            {
                // Riding the park. Ride does all of it five times a second; this only notices
                // when he has come out of the far side, and points him somewhere else.
                if (roll.InPark)
                {
                    if (!roll.ParkDone) return false;

                    LeavePark(roll);

                    // Out past the start of it, very likely: not straight back round.
                    roll.PathAgainAt = now + PathAgainMs;

                    roll.Phase = RollPhase.Rolling;
                    roll.Nudges = 0;
                    Aim(roll, now);
                    return false;
                }

                if (now < roll.SitUntil)
                {
                    Lapping(roll, now);
                    return false;
                }

                Straighten(roll);

                roll.Phase = RollPhase.Rolling;
                roll.Nudges = 0;
                Aim(roll, now);
                return false;
            }

            var here = roll.Car.Position;

            // OFF THE BLOCKS.
            //
            // Everything about these people is that they are FROM somewhere -- they spawn on
            // Chamberlain Hills and Strawberry, they are painted the set's green, and a green
            // Manchez three districts away is not the set, it is a stray car with a story
            // nobody wrote. Every destination they are given is already checked against the
            // turf, so the only way out is drift: a wander that had nowhere better to go, or a
            // driver taking a wide line round a junction on the boundary.
            //
            // Noticed once, pointed home once, and handed back if it cannot get there. Not
            // teleported and not deleted -- a car that vanishes while you are looking at it is
            // worse than one that drives off.
            if (Ours(here))
            {
                roll.StrayedAt = 0;
            }
            else
            {
                if (roll.StrayedAt == 0)
                {
                    roll.StrayedAt = now;
                    Aim(roll, now);
                    return false;
                }

                if (now - roll.StrayedAt > StrayMs)
                {
                    Log.Debug("Rollers: one wandered off the blocks and was handed back.");
                    return true;
                }
            }

            // YOUR PATH, FROM WHERE IT STARTS. A rider who comes by the start of the path you
            // rode takes it -- round from its first point, the way you rode it. Michael asked for
            // exactly that on 2026-09-24: the bikes use it when they are close to the start.
            // Not straight after coming off it, and not when he is already on his way there.
            if (roll.OnFoot && PathOn && now >= roll.PathAgainAt &&
                !(roll.StopThere && ParkTarget(roll.Target)) &&
                Park.Flat2(here, BikePath.First) < PathJoinRange)
            {
                ToPath(roll, now);
                return false;
            }

            // Headed into the park, he is not there until he is at the door. Sixteen metres out
            // is still on the road, and the first line he is given is checked from where he is.
            var toPark = roll.OnFoot && roll.StopThere && ParkUsable && ParkTarget(roll.Target);
            var arrived = toPark ? ParkArrive : ArrivedRange;

            if (roll.Target != Vector3.Zero && here.DistanceTo(roll.Target) < arrived)
            {
                if (roll.StopThere)
                {
                    roll.Phase = RollPhase.Sitting;
                    roll.SitUntil = now + _rng.Next(SitMinMs, SitMaxMs);

                    if (toPark) StartPark(roll, now);
                    else Circling(roll, now);

                    return false;
                }

                roll.Nudges = 0;
                Aim(roll, now);
                return false;
            }

            // Not there yet. Two looks with nothing between them is a wall.
            if (now - roll.LookedAt < StuckLookMs) return false;

            var moved = roll.LookedAt == 0 ? float.MaxValue : here.DistanceTo(roll.WasAt);

            roll.WasAt = here;
            roll.LookedAt = now;

            if (moved > StuckMoved) return false;

            roll.Nudges++;

            // Given up on. Handed back rather than deleted, because deleting a car the player
            // might be looking at is worse than one that drives off oddly.
            if (roll.Nudges > MaxNudges) return true;

            Aim(roll, now);
            return false;
        }

        /// <summary>Points them at somewhere else on the block and lets them go.</summary>
        private void Aim(Roll roll, int now)
        {
            if (roll.Car == null || !roll.Car.Exists()) return;
            if (roll.Driver == null || !roll.Driver.Exists() || !roll.Driver.IsAlive) return;

            // A mate goes where the lead goes and stops when the lead stops. He does not get
            // his own opinion about it, which is the entire difference between a crew and three
            // men who happened to set off at the same time.
            //
            // A lead who has crashed, died or been handed back for being stuck leaves his mates
            // with nothing to follow, and they quietly go back to riding on their own -- which
            // is what somebody whose mate has ridden off does.
            var lead = roll.Lead;

            // STILL ONE OF OURS, checked here rather than everywhere a roll is dropped.
            //
            // Release does not null the vehicle -- it hands it back to the game, which leaves
            // Car.Exists() perfectly true on a bike that is now ordinary traffic riding off
            // with a Target it was given twenty minutes ago. Asking whether the lead is still
            // on the books catches that, and catches it in the one place that reads Lead at
            // all, so no future removal site can forget to do it.
            if (lead != null && !_out.Contains(lead))
            {
                roll.Lead = null;
                lead = null;
            }

            var following = roll.OnFoot && lead != null &&
                            lead.Car != null && lead.Car.Exists() &&
                            lead.Target != Vector3.Zero;

            var stop = following
                ? lead.StopThere
                : _rng.Next(100) < (roll.OnFoot ? BikeStopChancePercent : StopChancePercent);

            // A rider goes down the back streets about as often as he goes along the pavement.
            //
            // Pavement alone put every one of them out on the main road frontage, which is the
            // one place a bike is least interesting -- the service roads and the cut-throughs
            // behind the buildings are where anybody round here actually rides, and the road
            // network already knows which ones those are.
            // WHERE THE PROBES START, which is not always where the vehicle is.
            //
            // Every one of them looks for somewhere within a hundred metres or so and throws
            // out anything off our turf. That works perfectly while they are ON it and fails
            // completely the moment they are not: a car sat one street outside the zone probes
            // around itself, every probe lands outside as well, nothing is found, and the only
            // thing left is a wander -- which takes it further out, so the next tick fails
            // harder. It cannot look its way home from where it is standing.
            //
            // So a stray probes around the PLAYER instead. He is the one position known to be
            // on our blocks, because nothing is put out at all unless he is stood on them.
            var from = Anchor(roll);

            Vector3 where;

            if (following)
            {
                // Into the park, a mate finds his own way in rather than a spot a few metres off
                // his lead's -- in the park, a few metres off is inside a ramp.
                where = ParkUsable && ParkTarget(lead.Target)
                    ? ParkEntry(roll.Car.Position)
                    : Beside(lead.Target);
            }
            else if (roll.OnFoot && stop)
            {
                // Somewhere to stand about, rather than somewhere to ride to.
                where = Hangout(from);
            }
            else if (roll.OnFoot)
            {
                where = _rng.Next(100) < 55 ? Node(from, true) : Pavement(from);

                // AND IT HAS TO BE SOMEWHERE HE CAN ACTUALLY GET TO. The other picker gets a
                // go before the fallbacks below do -- one is on the road network and one is on
                // the pavement, and the one that fails is usually the one that landed the far
                // side of something.
                if (where != Vector3.Zero && !Reachable(roll.Car.Position, where))
                {
                    where = _rng.Next(100) < 55 ? Pavement(from) : Node(from, true);

                    if (where != Vector3.Zero && !Reachable(roll.Car.Position, where))
                    {
                        where = Vector3.Zero;
                    }
                }
            }
            else
            {
                where = Node(from, stop);
            }

            // Whichever it asked for, take the other rather than stand still.
            if (where == Vector3.Zero && roll.OnFoot) where = Pavement(from);

            // Still nothing, and already off the blocks. Anywhere on them will do -- getting
            // back is the whole job at this point and which street it is does not matter.
            if (where == Vector3.Zero && !Ours(roll.Car.Position)) where = Homeward();

            // Nowhere to send them this tick -- the probes all landed off our turf, or off the
            // road network entirely. They wander instead of standing still, because a car
            // stopped in a live lane is the exact thing the traffic watchdog exists to remove
            // and this one has a driver in it, so nothing would remove it.
            // Only ever reached ON our turf now, or with no player to steer back to. That
            // matters: wander has no idea where Chamberlain Hills is, and handing it to a car
            // that has already drifted out is an instruction to keep going.
            if (where == Vector3.Zero)
            {
                try
                {
                    Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, roll.Driver.Handle, roll.Car.Handle,
                                  roll.OnFoot ? CruiseBike : CruiseCar,
                                  roll.OnFoot ? StyleBike : StyleCar);

                    // No destination any more, which matters: leaving the old one in place
                    // would read as "arrived" again next tick and restart the wander every
                    // 900ms, and a car handed a fresh instruction nine times a second does not
                    // go anywhere at all.
                    roll.Target = Vector3.Zero;
                    roll.StopThere = false;
                    roll.LookedAt = now;
                    roll.WasAt = roll.Car.Position;
                }
                catch
                {
                    // They will be looked at again in nine seconds either way.
                }

                return;
            }

            roll.Target = where;
            roll.StopThere = stop;
            roll.LookedAt = now;
            roll.WasAt = roll.Car.Position;

            try
            {
                Function.Call(Hash.CLEAR_PED_TASKS, roll.Driver.Handle);

                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, roll.Driver.Handle, roll.Car.Handle,
                              where.X, where.Y, where.Z,
                              roll.OnFoot ? CruiseBike : CruiseCar,
                              0, roll.Car.Model.Hash,
                              roll.OnFoot ? StyleBike : StyleCar,
                              stop ? 4f : 15f, true);

                // A BIKE'S SPEED IS PACING'S BUSINESS FROM HERE. This sets a sane opening
                // number and the road takes over on the next look -- otherwise every new
                // destination would slam him back to the flat cruise and he would arrive at the
                // next corner at exactly the speed that was putting him in walls.
                Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, roll.Driver.Handle,
                              roll.OnFoot ? CruiseBike : CruiseCar);

                if (roll.OnFoot)
                {
                    roll.Cruise = 0f;
                    roll.PaceAt = 0;

                    // Good enough to hold a line at fifteen. The default is a rider who can be
                    // given a speed and not the skill to use it.
                    Function.Call(Hash.SET_DRIVER_ABILITY, roll.Driver.Handle, 1.0f);
                }

                Function.Call(Hash.SET_PED_KEEP_TASK, roll.Driver.Handle, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not send a roller on: " + ex.Message);
            }
        }

        // ---- where they go -----------------------------------------------------

        /// <summary>
        /// A point on the road network near them, on our blocks.
        ///
        /// Back alleys are picked out by asking the map, not by listing coordinates: the game
        /// marks a node as GPS-allowed when it is a road the satnav would route you down, so the
        /// ones it will NOT route down are the service roads, the yards and the cut-throughs
        /// behind the buildings. That is close enough to the same set of places somebody would
        /// pull into, which is a happy accident rather than a design, but it holds up.
        ///
        /// A few tries and then it takes whatever it got. Insisting on an alley near a block
        /// that has none means never moving.
        /// </summary>
        /// <summary>
        /// Somewhere to stand about with the engine off.
        ///
        /// A known spot first, and only if he is near enough that riding to it is a thing he
        /// might have been doing anyway -- a bike sent across two districts to reach a car park
        /// is not hanging about, it is commuting.
        ///
        /// Failing that, found rather than listed. See Green.
        /// </summary>
        /// <summary>
        /// Sets a bike up to ride laps where it stopped.
        ///
        /// ONLY BIKES, AND ONLY WHERE THERE IS ROOM. A carload parking up at a kerb is a
        /// carload parking up at a kerb -- that is what they came to do, and a saloon turning
        /// circles on Grove Street is a takeover, which is a different thing this mod already
        /// has. This is the pack of them on bikes at the courts.
        ///
        /// THE CIRCLE IS WHERE HE STOPPED, not the coordinate he was sent to. He was told to
        /// drive to within four metres of a point and he stopped wherever the last obstacle
        /// let him, so measuring from him keeps four riders on four circles instead of all of
        /// them converging on one spot and shunting each other off it.
        /// </summary>
        private void Circling(Roll roll, int now)
        {
            if (!roll.OnFoot) return;
            if (roll.Car == null || !roll.Car.Exists()) return;
            if (roll.Driver == null || !roll.Driver.Exists() || !roll.Driver.IsAlive) return;

            var here = roll.Car.Position;

            roll.RingRadius = LapMin + (float)_rng.NextDouble() * (LapMax - LapMin);

            // The middle is a radius BEHIND him, so his first point is roughly ahead rather
            // than a hard turn on the spot the moment he arrives.
            var back = roll.Car.ForwardVector;
            back.Z = 0f;

            if (back.Length() < 0.1f) back = new Vector3(1f, 0f, 0f);
            else back.Normalize();

            roll.Ring = here - back * roll.RingRadius;
            roll.Ring.Z = here.Z;

            var out_ = here - roll.Ring;
            roll.RingAngle = (float)Math.Atan2(out_.Y, out_.X);

            roll.LapAt = 0;
            roll.BurnUntil = 0;
            roll.BurnAfter = now + _rng.Next(BurnFirstMinMs, BurnFirstMaxMs);
        }

        /// <summary>
        /// One tick of hanging about on a bike: round the circle, and now and then a burnout.
        ///
        /// THE LAP IS POINTS, NOT A LOCK. A car does a circle by holding full steering lock
        /// with the grip cut, and a bike given the same treatment falls over -- it has two
        /// wheels and the game will not hold it up through that. So a rider is handed the next
        /// point on his circle every second and a bit, at walking-ish pace, and rides between
        /// them. From outside it is a man riding round in circles, which is the thing.
        ///
        /// THE BURNOUT IS A LOCK, because that one works on a bike: burnout mode holds the
        /// front and spins the rear, which is exactly what somebody does at a stop. The temp
        /// action runs for as long as it was asked for and the lap picks up after it.
        /// </summary>
        private void Lapping(Roll roll, int now)
        {
            if (!roll.OnFoot || roll.RingRadius <= 0f) return;
            if (roll.Car == null || !roll.Car.Exists()) return;
            if (roll.Driver == null || !roll.Driver.Exists() || !roll.Driver.IsAlive) return;

            // Mid-burnout. Nothing to do but let it run.
            if (now < roll.BurnUntil) return;

            if (roll.BurnUntil != 0)
            {
                // Just finished one. Off the brake, and back to the lap on the next tick.
                roll.BurnUntil = 0;
                roll.LapAt = 0;

                try { Function.Call(Hash.SET_VEHICLE_BURNOUT, roll.Car.Handle, false); }
                catch { }

                roll.BurnAfter = now + _rng.Next(BurnRestMinMs, BurnRestMaxMs);
                return;
            }

            // Not with somebody stood at his back wheel, or a wall a metre off it. See RoomToBurn.
            if (now >= roll.BurnAfter && !RoomToBurn(roll))
            {
                roll.BurnAfter = now + BurnRestMinMs;
            }

            if (now >= roll.BurnAfter)
            {
                var ms = BurnMinMs + _rng.Next(BurnMaxMs - BurnMinMs);

                try
                {
                    Function.Call(Hash.CLEAR_PED_TASKS, roll.Driver.Handle);
                    Function.Call(Hash.SET_VEHICLE_BURNOUT, roll.Car.Handle, true);
                    Function.Call(Hash.TASK_VEHICLE_TEMP_ACTION, roll.Driver.Handle,
                                  roll.Car.Handle, BurnAction, ms);

                    roll.BurnUntil = now + ms;
                }
                catch
                {
                    roll.BurnAfter = now + BurnRestMinMs;
                }

                return;
            }

            if (now < roll.LapAt) return;

            // THE NEXT POINT ONLY IF HE CAN GET THERE. The circle used to be handed out blind,
            // and a circle drawn round wherever he stopped goes through whatever is standing
            // there -- which is how they kept riding into things. Each point is checked from
            // where his wheels are: walls, placed things, parked cars and the ground. A couple
            // of points further round get a try, and if none of the circle can be reached the
            // spot is too crowded to hang about in and he moves on.
            var wheels = Wheels(roll.Car);
            var at = Vector3.Zero;

            for (var tries = 0; tries < 3; tries++)
            {
                roll.RingAngle += LapStep;

                var p = roll.Ring + new Vector3((float)Math.Cos(roll.RingAngle) * roll.RingRadius,
                                                (float)Math.Sin(roll.RingAngle) * roll.RingRadius,
                                                0f);
                p.Z = wheels.Z;

                if (!Park.LineClear(_parkWorld, wheels, p)) continue;

                at = p;
                break;
            }

            if (at == Vector3.Zero)
            {
                roll.SitUntil = now;
                return;
            }

            try
            {
                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, roll.Driver.Handle, roll.Car.Handle,
                              at.X, at.Y, at.Z, LapSpeed, 0, roll.Car.Model.Hash,
                              StyleBike, 2f, true);

                Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, roll.Driver.Handle, LapSpeed);
            }
            catch
            {
                // The next point comes round anyway.
            }

            roll.LapAt = now + LapEveryMs;
        }

        /// <summary>Off the brake and off the circle, whatever he was in the middle of.</summary>
        private static void Straighten(Roll roll)
        {
            roll.RingRadius = 0f;
            roll.BurnUntil = 0;
            roll.LapAt = 0;

            if (roll.Car == null || !roll.Car.Exists()) return;

            try { Function.Call(Hash.SET_VEHICLE_BURNOUT, roll.Car.Handle, false); }
            catch { }
        }

        /// <summary>
        /// The shape of a lap and the rhythm of the burnouts.
        ///
        /// TIGHT. Six to nine metres is a circle you ride round rather than a block you ride
        /// round, and at four metres a second a rider is most of a second between points --
        /// which is a lean, not a stop. The step is a fifth of a turn, so five points is a lap.
        ///
        /// 23 is the burnout temp action, the same one the takeover's dirt bikes use.
        /// </summary>
        private const float LapMin = 6f;
        private const float LapMax = 9f;
        private const float LapStep = 1.2566371f;
        private const float LapSpeed = 4f;
        private const int LapEveryMs = 1100;

        private const int BurnAction = 23;
        private const int BurnMinMs = 2200;
        private const int BurnMaxMs = 4200;
        private const int BurnFirstMinMs = 2500;
        private const int BurnFirstMaxMs = 12000;
        private const int BurnRestMinMs = 7000;
        private const int BurnRestMaxMs = 20000;

        private Vector3 Hangout(Vector3 from)
        {
            // THE PARK FIRST, once it has been looked over and when it is near enough to ride
            // to. In by the way nearest where he is coming from; see Ride for what happens once
            // he is there.
            // Your path, when there is one: on at the point of it nearest where he is.
            if (PathOn && Park.Flat2(BikePath.First, from) < HangoutRange) return ParkEntry(from);

            if (ParkUsable && !PathOn && Park.Flat2(_park.Centre, from) < HangoutRange)
            {
                var door = _park.EntryFor(from);
                if (door >= 0) return _park.Spots[door];
            }

            var best = Vector3.Zero;
            var nearest = HangoutRange;

            foreach (var spot in Hangouts)
            {
                try
                {
                    var d = spot.DistanceTo(from);

                    if (d >= nearest) continue;
                    if (!Ours(spot)) continue;

                    // A hangout inside the park is only ever ridden AS the park. Sent there on
                    // its own -- before the park has been looked over -- it is a circle through
                    // whatever is on the court, which is the thing this replaced.
                    if (InsidePark(spot)) continue;

                    best = spot;
                    nearest = d;
                }
                catch
                {
                    // Next one.
                }
            }

            // Scattered around it, because three bikes handed one coordinate arrive as three
            // bikes trying to stand in the same square metre and the game settles that by
            // shoving them into each other.
            if (best != Vector3.Zero) return Beside(best);

            var green = Green(from);

            // Nothing open nearby. The pavement is still better than standing in the road.
            return green != Vector3.Zero ? green : Pavement(from);
        }

        /// <summary>
        /// Open ground on our turf, found by asking the map instead of by listing coordinates.
        ///
        /// A pavement spot with a road running past it is a pavement. The same spot with no
        /// road within fifteen metres is a green, a yard, a court or the back of a car park --
        /// which is the same set of places people actually stand about in, arrived at without
        /// anybody typing a single coordinate that could turn out to be inside a wall.
        ///
        /// That is the whole reason it is done this way round. A hand-written list of parks is
        /// better scenery and worse code: every entry is a number somebody read off a map, and
        /// the ones that are wrong put a bike through a fence on somebody else's machine.
        /// </summary>
        private Vector3 Green(Vector3 from)
        {
            for (var tries = 0; tries < 12; tries++)
            {
                try
                {
                    var probe = from.Around(30f + (float)_rng.NextDouble() * 95f);
                    var at = World.GetNextPositionOnSidewalk(probe);

                    if (at == Vector3.Zero) continue;
                    if (!Ours(at)) continue;

                    // Not the park: that is ridden on checked lines or not at all. See Ride.
                    if (InsidePark(at)) continue;

                    var street = World.GetNextPositionOnStreet(at);
                    if (street != Vector3.Zero && street.DistanceTo(at) < OffRoad) continue;

                    return at;
                }
                catch
                {
                    // Next try.
                }
            }

            return Vector3.Zero;
        }

        /// <summary>
        /// Where to look from: themselves while they are on the blocks, the player once they
        /// are not. See the note in Aim for why looking from where they are stood cannot work.
        /// </summary>
        private Vector3 Anchor(Roll roll)
        {
            try
            {
                var here = roll.Car.Position;
                if (Ours(here)) return here;

                var player = Game.Player.Character;
                if (player != null && player.Exists() && Ours(player.Position)) return player.Position;

                return here;
            }
            catch
            {
                return roll.Car.Position;
            }
        }

        /// <summary>Anywhere at all back on our blocks, for one that has drifted off them.</summary>
        private Vector3 Homeward()
        {
            try
            {
                var player = Game.Player.Character;

                if (player == null || !player.Exists()) return Vector3.Zero;
                if (!Ours(player.Position)) return Vector3.Zero;

                var back = Node(player.Position, false);

                return back != Vector3.Zero ? back : player.Position;
            }
            catch
            {
                return Vector3.Zero;
            }
        }

        /// <summary>Near a spot rather than on it.</summary>
        private Vector3 Beside(Vector3 spot)
        {
            try
            {
                var turn = _rng.NextDouble() * Math.PI * 2d;
                var reach = PackSpread * (0.35f + (float)_rng.NextDouble());

                return new Vector3(spot.X + (float)Math.Cos(turn) * reach,
                                   spot.Y + (float)Math.Sin(turn) * reach,
                                   spot.Z);
            }
            catch
            {
                return spot;
            }
        }

        // ---- the front wheel ---------------------------------------------------

        /// <summary>
        /// Whoever is up on the back wheel, held there.
        ///
        /// RUN EVERY FRAME, from above the tick throttle. The lift is a push applied behind the
        /// centre of mass, and a push is only a wheelie if it keeps coming -- delivered once
        /// every nine hundred milliseconds it is a kerb being hit.
        ///
        /// Started rarely and only on a road at speed. A bike doing walking pace that rears up
        /// on the pavement is being levitated; the same bike doing thirty down Carson is a
        /// rider showing off, which is the thing worth seeing.
        /// </summary>
        /// <summary>
        /// One of yours clocks you from the car.
        ///
        /// A HORN OR A SHOUT, NOT BOTH, and the horn is the rarer of the two because it is the
        /// louder gesture -- somebody leaning out and shouting your name is the normal way one
        /// of your own says hello, and two taps on the horn is what you get when they are
        /// already moving and cannot.
        ///
        /// The whole car looks at you either way, which is the bit that reads at speed: you
        /// notice heads turning in a passing car long before you make out what anybody said.
        /// </summary>
        private void Greet(int now)
        {
            if (now < _nextGreet) return;

            Ped you;

            try
            {
                you = Game.Player.Character;
                if (you == null || !you.Exists() || !you.IsAlive) return;
            }
            catch
            {
                return;
            }

            foreach (var roll in _out)
            {
                if (roll.Car == null || !roll.Car.Exists()) continue;
                if (roll.Car.Position.DistanceTo(you.Position) > GreetRange) continue;

                _nextGreet = now + GreetGapMs;

                foreach (var m in roll.Crew) GangPeds.Notice(m, you, 3000);

                // A bike has no horn worth the name and nobody beeps a scooter at a friend.
                var canBeep = roll.Car.ClassType != VehicleClass.Motorcycles
                              && roll.Car.ClassType != VehicleClass.Cycles;

                if (canBeep && _rng.Next(100) < 35)
                {
                    GangPeds.Beep(roll.Car);

                    _beepCar = roll.Car;
                    _secondBeepAt = now + SecondBeepMs;
                }
                else
                {
                    GangPeds.Hello(roll.Driver, _rng);
                }

                return;
            }
        }

        private int _nextGreet;
        private int _secondBeepAt;
        private Vehicle _beepCar;

        /// <summary>
        /// Riders look at the road ahead and pick a speed for it.
        ///
        /// A FIXED CRUISE IS WHY THEY CRASH. Six metres a second everywhere is slow on a
        /// straight and still too quick for a ninety-degree turn into a service road, and the
        /// driving AI does not brake for a corner it was told to take at a constant speed -- it
        /// arrives at the same rate it left, understeers into the wall, and sits there.
        ///
        /// So the speed comes from the road rather than from a constant. A point is taken
        /// twenty-odd metres ahead, snapped to the nearest road, and the angle between "where
        /// he is pointing" and "where that road is" says how much it bends. Straight ahead is
        /// nothing and he opens it up; a sharp bend is most of a right angle and he is down to
        /// walking pace before he reaches it.
        ///
        /// EASED RATHER THAN SNAPPED. Cruise speed set straight from the reading jumps every
        /// time the look-ahead lands on a different node, which is a bike surging and dropping
        /// on a straight road. It moves a third of the way each time instead, so it rolls off
        /// and rolls back on.
        ///
        /// Bikes only. A car on these streets is doing eight and a half and is not the thing
        /// that keeps ending up in a wall.
        /// </summary>
        private void Pacing(int now)
        {
            foreach (var roll in _out)
            {
                if (!roll.OnFoot) continue;

                // NOT WHILE HE IS RIDING THE CIRCLE. Pacing reads the road ahead and sets a
                // cruise speed off it, and a man doing laps of a basketball court at four
                // metres a second would be handed the open-road number every second and a half
                // -- so the tight circle would open into a wide fast one and then into the
                // fence. See Lapping.
                if (roll.Phase == RollPhase.Sitting) continue;

                if (now < roll.PaceAt) continue;

                roll.PaceAt = now + PaceEveryMs;

                if (roll.Car == null || !roll.Car.Exists()) continue;
                if (roll.Driver == null || !roll.Driver.Exists() || !roll.Driver.IsAlive) continue;

                try
                {
                    var want = ForTheRoad(roll.Car);

                    // THE LAST STRETCH TO THE PARK AT PARK SPEED, near enough. The road reading
                    // would have him arrive at the door doing fifteen, and the door is where the
                    // checked lines start -- a man going that fast is past the first one before
                    // he has been given it.
                    if (roll.StopThere && ParkUsable && ParkTarget(roll.Target) &&
                        Park.Flat2(roll.Car.Position, roll.Target) < ParkApproach)
                    {
                        want = Math.Min(want, ParkSpeed + 1f);
                    }

                    // First look of his life -- start where the reading says rather than easing
                    // up from zero, which would have him crawl away from every spawn.
                    if (roll.Cruise <= 0f) roll.Cruise = want;
                    else roll.Cruise += (want - roll.Cruise) * PaceEase;

                    Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, roll.Driver.Handle, roll.Cruise);
                }
                catch
                {
                    // He keeps whatever he had.
                }
            }
        }

        /// <summary>
        /// The speed the road ahead deserves.
        ///
        /// Snapped to a road rather than measured off the raw look-ahead point, because a point
        /// twenty metres in front of a bike in an alley is frequently inside a building. What
        /// is wanted is where the ROAD goes, and the node network is the only thing that knows.
        /// </summary>
        private static float ForTheRoad(Vehicle bike)
        {
            var at = bike.Position;
            var fwd = bike.ForwardVector;

            var road = World.GetNextPositionOnStreet(at + fwd * LookAhead, true);

            // Nothing out there to read. Middling speed rather than either extreme: guessing
            // fast puts him in a wall and guessing slow makes every unmapped yard a crawl.
            if (road == Vector3.Zero) return (BikeStraight + BikeBend) * 0.5f;

            var to = road - at;

            to = new Vector3(to.X, to.Y, 0f);

            var len = to.Length();
            if (len < 1f) return BikeBend;

            to = to * (1f / len);

            var dot = fwd.X * to.X + fwd.Y * to.Y;

            if (dot > 1f) dot = 1f;
            if (dot < -1f) dot = -1f;

            var bend = (float)(Math.Acos(dot) * 180.0 / Math.PI);

            if (bend <= StraightUnder) return BikeStraight;
            if (bend >= BendOver) return BikeBend;

            var t = (bend - StraightUnder) / (BendOver - StraightUnder);

            return BikeStraight + (BikeBend - BikeStraight) * t;
        }

        /// <summary>How far ahead the road is read, and how often.</summary>
        private const float LookAhead = 22f;
        private const int PaceEveryMs = 350;

        /// <summary>How much of the way to the new speed he moves each time. See Pacing.</summary>
        private const float PaceEase = 0.34f;

        /// <summary>Straight and bent, in metres a second, and the angles that count as each.</summary>
        private const float BikeStraight = 15f;
        private const float BikeBend = 5f;
        private const float StraightUnder = 8f;
        private const float BendOver = 42f;

        private void Wheelies(int now)
        {
            if (!Enabled || _out.Count == 0) return;
            if (_cfg != null && !_cfg.RollerWheelies) return;

            for (var i = 0; i < _out.Count; i++)
            {
                var roll = _out[i];

                if (!roll.OnFoot || roll.Phase == RollPhase.Sitting) continue;

                var bike = roll.Car;
                if (bike == null || !bike.Exists()) continue;

                var rider = roll.Driver;
                if (rider == null || !rider.Exists() || !rider.IsAlive) continue;

                float speed;

                try { speed = bike.Speed; }
                catch { continue; }

                if (now < roll.WheelieUntil)
                {
                    // Comes down on its own when he runs out of road or slows for a junction,
                    // rather than being carried nose-up at walking pace to the end of a timer.
                    if (speed < WheelieNeedsSpeed * 0.6f)
                    {
                        roll.WheelieUntil = 0;
                        continue;
                    }

                    try
                    {
                        Function.Call(Hash.APPLY_FORCE_TO_ENTITY, bike.Handle, 1,
                                      0f, 0f, Lift,
                                      0f, -WheelieBehind, 0f,
                                      0, true, true, true, false, true);
                    }
                    catch
                    {
                        // Next frame.
                    }

                    continue;
                }

                if (now < roll.WheelieAfter || speed < WheelieNeedsSpeed) continue;

                // Whether he goes for one or not, he is not asked again for a while. Rolling
                // the dice every frame at speed would make it a certainty within about two.
                roll.WheelieAfter = now + _rng.Next(WheelieRestMinMs, WheelieRestMaxMs);

                if (_rng.Next(100) >= WheelieChancePercent) continue;

                // On a road, checked only here. It is a native call and this is the one branch
                // that is rare enough to afford it.
                try
                {
                    var street = World.GetNextPositionOnStreet(bike.Position);
                    if (street == Vector3.Zero || street.DistanceTo(bike.Position) > OnRoad) continue;
                }
                catch
                {
                    continue;
                }

                roll.WheelieUntil = now + _rng.Next(WheelieMinMs, WheelieMaxMs);
            }
        }

        private Vector3 Node(Vector3 from, bool wantAlley)
        {
            for (var tries = 0; tries < 12; tries++)
            {
                var probe = from.Around(40f + (float)_rng.NextDouble() * 110f);

                try
                {
                    var id = Function.Call<int>(Hash.GET_NTH_CLOSEST_VEHICLE_NODE_ID,
                                                probe.X, probe.Y, probe.Z,
                                                1 + _rng.Next(8), 1, 3f, 0f);

                    if (!Function.Call<bool>(Hash.IS_VEHICLE_NODE_ID_VALID, id)) continue;

                    var got = new OutputArgument();
                    Function.Call(Hash.GET_VEHICLE_NODE_POSITION, id, got);

                    var at = got.GetResult<Vector3>();
                    if (at == Vector3.Zero) continue;
                    if (!Ours(at)) continue;

                    var backstreet = !Function.Call<bool>(Hash.GET_VEHICLE_NODE_IS_GPS_ALLOWED, id);

                    // Held out for over the first eight tries, then anything on our turf will do.
                    if (tries < 8 && backstreet != wantAlley) continue;

                    return at;
                }
                catch
                {
                    // Next try.
                }
            }

            return Vector3.Zero;
        }

        /// <summary>
        /// Whether he can actually get there, or whether it is the far side of something.
        ///
        /// THE WALL AT B.J. SMITH IS WHY THIS EXISTS. A rider was being sent to a pavement
        /// point inside the park, which is thirty metres away in a straight line and has a
        /// four-foot wall across the middle of it. StyleBike carries the shortest-path flag --
        /// deliberately, because that is what lets a BMX use pavements and cut-throughs
        /// instead of being stuck on the road network -- and shortest path to somewhere behind
        /// a wall is the wall.
        ///
        /// AND THE STUCK CHECK COULD NEVER CATCH IT. That asks whether he has moved since the
        /// last look, and a bike hitting a wall is not stationary: it bounces, the rider gets
        /// back on, and he tries again. He moves plenty. He simply never arrives, for as long
        /// as anybody is watching.
        ///
        /// So the question is asked before he sets off rather than after he fails. The road
        /// network already knows how far it is to drive somewhere -- if that is more than a
        /// couple of times the straight line, the straight line goes through something, and
        /// this is a rider who would spend the next five minutes proving it.
        ///
        /// Generous on purpose. A real corner is a detour and a normal one is well under twice
        /// the crow's distance; twice and a bit only rejects genuinely walled-off ground.
        /// </summary>
        private static bool Reachable(Vector3 from, Vector3 to)
        {
            try
            {
                var straight = from.DistanceTo(to);

                // Close enough that any detour is noise, and short enough that he is nearly
                // there anyway.
                if (straight < 6f) return true;

                var road = Function.Call<float>(Hash.CALCULATE_TRAVEL_DISTANCE_BETWEEN_POINTS,
                                                from.X, from.Y, from.Z, to.X, to.Y, to.Z);

                // Nought or negative is the network saying it cannot route there at all, which
                // is a clearer no than a long way round.
                if (road <= 0f) return false;

                return road <= straight * DetourMost;
            }
            catch
            {
                // If it cannot be asked, he goes. A rider who never rides is worse.
                return true;
            }
        }

        /// <summary>How far round the houses a destination may be before it is not one.</summary>
        private const float DetourMost = 2.4f;

        /// <summary>Somewhere to aim a bike: a stretch of pavement on our blocks.</summary>
        private Vector3 Pavement(Vector3 from)
        {
            for (var tries = 0; tries < 10; tries++)
            {
                try
                {
                    var probe = from.Around(35f + (float)_rng.NextDouble() * 85f);
                    var at = World.GetNextPositionOnSidewalk(probe);

                    if (at == Vector3.Zero) continue;
                    if (!Ours(at)) continue;

                    // A rider passing through at road speed is not sent across the park: the
                    // pavement inside it runs between the ramps. The park has its own way in.
                    if (InsidePark(at)) continue;

                    return at;
                }
                catch
                {
                    // Next try.
                }
            }

            return Vector3.Zero;
        }

        private bool OnOurTurf()
        {
            var owner = _turf == null ? null : _turf.Owner;
            return owner != null && string.Equals(owner.Id, _gangId, StringComparison.OrdinalIgnoreCase);
        }

        private bool Ours(Vector3 at)
        {
            try
            {
                var code = Function.Call<string>(Hash.GET_NAME_OF_ZONE, at.X, at.Y, at.Z) ?? "";
                var owner = _gangs.OwnerOfZone(code);

                return owner != null && string.Equals(owner.Id, _gangId, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        // ---- putting one out ---------------------------------------------------

        /// <summary>
        /// One of them out on the block, optionally alongside somebody already out there.
        /// </summary>
        /// <returns>The one that went out, so a caller can send his mates after him.</returns>
        private Roll Send(Ped player, bool bike, Roll lead)
        {
            var gang = _gangs.Get(_gangId);
            if (gang == null || gang.MemberModels.Count == 0) return null;

            // A mate comes from where his lead is, not from the far end of the block. The
            // near-spawn rule is relaxed for him on purpose: the thing it protects against is
            // a vehicle appearing in front of you, and one appearing beside a bike you are
            // already watching is worse, so he is put behind the lead and slightly off him.
            var spawn = lead != null && lead.Car != null && lead.Car.Exists()
                ? Beside(lead.Car.Position - lead.Car.ForwardVector * 6f)
                : Somewhere(player, bike);

            if (spawn == Vector3.Zero) return null;

            var roll = new Roll { OnFoot = bike, BornAt = Game.GameTime, Lead = lead };

            try
            {
                roll.Car = Make(bike, spawn, lead == null ? null : lead.Car);
                if (roll.Car == null) return null;

                // Two or four up. A bike is one man, and a two-seater is a man and his mate --
                // asking for four in a coupe gets you two and a warning nobody reads.
                var room = Function.Call<int>(Hash.GET_VEHICLE_MAX_NUMBER_OF_PASSENGERS, roll.Car.Handle);
                // Two up where there is a pillion to sit on. Bicycles have none, so the clamp
                // below quietly puts those back to one man -- which is why this asks for it
                // rather than checking what he is riding.
                var want = bike
                    ? (_rng.Next(100) < 40 ? 1 : 0)
                    : (_rng.Next(2) == 0 ? 1 : 3);
                if (want > room) want = room;

                for (var seat = -1; seat < want; seat++)
                {
                    var man = Fill(gang, roll.Car, seat);
                    if (man == null) continue;

                    if (seat == -1) roll.Driver = man;
                    roll.Crew.Add(man);
                }

                if (roll.Driver == null)
                {
                    Scrap(roll);
                    return null;
                }

                Paint(roll.Car, bike);

                _out.Add(roll);
                Aim(roll, Game.GameTime);

                Log.Info("Rollers: " +
                         (bike ? (lead == null ? "a bike" : "a bike alongside another")
                               : "a car with " + roll.Crew.Count + " up") +
                         " came out on " + (_turf == null ? "the block" : _turf.ZoneName) + ".");

                return roll;
            }
            catch (Exception ex)
            {
                Log.Debug("Rollers could not put one out: " + ex.Message);
                Scrap(roll);
                _out.Remove(roll);
            }

            return null;
        }

        /// <summary>
        /// A spot to come from: on the road, on our turf, and not in your lap.
        ///
        /// The near limit is the whole trick. Everything else about this is ordinary spawning.
        /// </summary>
        private Vector3 Somewhere(Ped player, bool bike)
        {
            for (var tries = 0; tries < 12; tries++)
            {
                try
                {
                    var away = SpawnNear + (float)_rng.NextDouble() * (SpawnFar - SpawnNear);
                    var probe = player.Position.Around(away);

                    var at = bike
                        ? World.GetNextPositionOnSidewalk(probe)
                        : World.GetNextPositionOnStreet(probe, true);

                    if (at == Vector3.Zero) continue;
                    if (at.DistanceTo(player.Position) < SpawnNear) continue;
                    if (!Ours(at)) continue;

                    // A bike is made on the pavement, and a bike made in the park is a bike
                    // made on top of whatever is standing there.
                    if (bike && InsidePark(at)) continue;

                    return at;
                }
                catch
                {
                    // Next try.
                }
            }

            return Vector3.Zero;
        }

        /// <summary>
        /// The vehicle itself.
        ///
        /// Every model is checked against this copy of the game before it is asked for, and a
        /// miss simply moves on to the next one. Half of the list is content the Legacy build
        /// never received, so on that install this quietly becomes a shorter list rather than a
        /// stream of failures.
        /// </summary>
        private Vehicle Make(bool bike, Vector3 at, Vehicle matching)
        {

            // NOT INTO A WORLD THAT IS ALREADY FULL. See Core.Crowded.
            //
            // kocabac's ERR_MEM_EMBEDDEDALLOC went away when they turned the rollers OFF, which is this spawner naming itself in a crash report.
            if (Core.Crowded.Busy)
            {
                Core.Crowded.HeldOff("Rollers");
                return null;
            }
            // A third of the cars are donks. Bikes are never one, for reasons. A bike is
            // an engine or a BMX, decided here rather than by how many of each the list
            // happened to hold.
            var wanted = bike ? (_rng.Next(100) < PedalChance ? Pedals : Engines)
                       : (_rng.Next(100) < DonkChance ? Donks : Cars);
            var spares = bike ? SpareBikes : SpareCars;

            var order = new List<string>();

            // THE SAME BIKE AS THE MAN HE IS RIDING WITH, tried first. Three riders on three
            // different frames read as three strangers who happen to be on the same street;
            // three on the same frame read as people who came together, which is the whole
            // thing being built here. It is only a preference -- if that model will not load
            // the ordinary list is right behind it.
            if (matching != null && matching.Exists())
            {
                try
                {
                    var name = matching.Model.Hash;
                    if (Function.Call<bool>(Hash.IS_MODEL_IN_CDIMAGE, name)) order.Add(null);
                }
                catch
                {
                    // The list below is a complete answer on its own.
                }
            }

            for (var i = 0; i < 8; i++) order.Add(wanted[_rng.Next(wanted.Length)]);
            order.AddRange(spares);

            foreach (var name in order)
            {
                try
                {
                    // The null placeholder is the lead's own model, carried by reference rather
                    // than by name because a hash is what we have and a hash is what Model takes.
                    var model = name == null ? new Model(matching.Model.Hash) : new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !Core.Models.Ready(model)) continue;

                    var car = World.CreateVehicle(model, at);
                    model.MarkAsNoLongerNeeded();

                    if (car == null || !car.Exists()) continue;

                    car.IsPersistent = true;

                    // Ours, so it is scenery until somebody gets in it. See Core.Petrol.
                    Core.Petrol.Spare(car);

                    Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, car.Handle);
                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, car.Handle, true, true);

                    return car;
                }
                catch (Exception ex)
                {
                    Log.Debug("Rollers could not make a " + name + ": " + ex.Message);
                }
            }

            return null;
        }

        /// <summary>
        /// Dark green, the whole set of them.
        ///
        /// The colour is a paint index rather than an RGB triple, for the same reason the cars
        /// parked on the block are: the table has the metal flake in it and a raw green comes
        /// out flat. The radio is on and loud in the cars because a car going past with nothing
        /// coming out of it is a car nobody is in.
        /// </summary>
        private void Paint(Vehicle car, bool bike)
        {
            try
            {
                // Whose it is, on the plate as well as in the paint.
                Plates.Stamp(car, _gangs.Get(_gangId), _rng);

                Function.Call(Hash.SET_VEHICLE_MOD_KIT, car.Handle, 0);

                // Nothing over the top of it.
                Function.Call(Hash.SET_VEHICLE_LIVERY, car.Handle, -1);
                Function.Call(Hash.SET_VEHICLE_MOD, car.Handle, 48, -1, false);

                // The set's own index rather than a second copy of the number. This file and
                // gangs.json both said 49 and only one of them could be the place to change it.
                // BLACK OR THE SET'S GREEN, and never anything else.
                //
                // Every car on the block was the same green, which is a fleet rather than a
                // set -- nine of them parked down one street read as a livery somebody bought
                // in bulk. Half of them are metallic black now. It is still obviously the same
                // people, because the ones that are green are the exact same green and the
                // black ones are on the same glass and the same springs; what it stops being
                // is a colour scheme.
                //
                // The set's own index rather than a second copy of the number. This file and
                // gangs.json both said 49 and only one of them could be the place to change it.
                var gang = _gangs == null ? null : _gangs.Get(_gangId);
                var green = gang != null && gang.Paint >= 0 ? gang.Paint : DarkGreen;

                var black = _rng.Next(2) == 0;
                var paint = black ? MetallicBlack : green;

                // PRIMARY DARK, PEARL BRIGHT, and the gap between them is the whole effect.
                //
                // It used to set the pearl to the same index as the body, which is the same as
                // having no pearl at all -- a flake that matches the paint it is suspended in
                // does not catch anything. A lighter green over the dark one is what makes it
                // shift as the car turns under a street light, which is what anybody means by
                // a pearlescent green.
                //
                // The pearl is in the ini because the colour table is not documented anywhere
                // we can check and the difference between "green" and the wrong green is one
                // number. The body is not: that is the set's own colour and it belongs to the
                // set, not to a taste.
                // SILVER OVER BOTH OF THEM.
                //
                // The pearl was a bright green over the green, which is the flake matching the
                // paint it is suspended in -- and then black cars got their own black, which is
                // no flake at all. Neither of those is what a pearl does. Silver is: it is
                // colourless in the shade and catches the light as the car turns, so the green
                // shifts and the black lifts, and it is the one flake that suits both.
                //
                // The index is in the ini and the DEFAULT is what changed. It is the only
                // number here anybody should be turning, and every other paint value in this
                // file is now one of two things that have been checked against the game's own
                // table: metallic black and the set's dark green. Nothing picks a colour
                // nobody has looked up.
                var pearl = _cfg == null ? DefaultPearl : _cfg.RollerPearl;

                Function.Call(Hash.SET_VEHICLE_COLOURS, car.Handle, paint, paint);
                Function.Call(Hash.SET_VEHICLE_EXTRA_COLOURS, car.Handle, pearl, 0);

                if (bike) return;

                // DARK SMOKE, NOT PURE BLACK. Tint 1 is limo glass you cannot see a thing
                // through; 2 is the smoke people actually put on a street car, and it still
                // reads as tinted from outside while leaving somebody visible behind it. These
                // are cars with the set IN them, and the men in them are the point.
                Function.Call(Hash.SET_VEHICLE_WINDOW_TINT, car.Handle, 2);

                // AND IT SITS ON ITS ARCHES. Competition is the top suspension index, and the
                // count is ASKED FOR rather than assumed: hardcoding 3 fits some cars and does
                // nothing at all on the rest, which looks like the mod not working. The last
                // index is the lowest the car can go, whatever that car happens to have.
                try
                {
                    var drops = Function.Call<int>(Hash.GET_NUM_VEHICLE_MODS, car.Handle, 15);

                    if (drops > 0)
                    {
                        Function.Call(Hash.SET_VEHICLE_MOD, car.Handle, 15, drops - 1, false);
                    }
                }
                catch
                {
                    // It rides at whatever height it came at.
                }
                Function.Call(Hash.SET_VEHICLE_DIRT_LEVEL, car.Handle, 1f);

                Function.Call(Hash.SET_VEHICLE_RADIO_ENABLED, car.Handle, true);
                // WEST COAST CLASSICS. It was Blonded for a while -- the DLC station, which
                // is a real thing a car on this block would be playing -- and it is the wrong
                // sound coming out of the set's own cars at a meet. The classics is what a
                // lowrider is for and it is what the rest of the meet is already on: the
                // hopper carrying the sound system at the car meet has been on it all along.
                Function.Call(Hash.SET_VEH_RADIO_STATION, car.Handle, Core.Radio.WestCoast);
                Function.Call(Hash.SET_VEHICLE_RADIO_LOUD, car.Handle, true);

                // AND THE DONKS GET THE UNDERGLOW, WHICH NOTHING ELSE DOES.
                //
                // Deliberately only them. Neon on everything is a fleet and neon on the donk is
                // a car somebody has spent money on -- and the whole reason to have a donk in
                // the set is that it is the loudest thing the set owns. Green, to match the
                // paint it is under, because the point is the car and not the colour of the
                // light.
                //
                // Left dirty like the rest of them, note. A donk that has never been driven in
                // the rain is a showroom car, and these live on the same streets as everything
                // else in this list.
                if (!Donk(car)) return;

                Function.Call(Hash.SET_VEHICLE_MOD_KIT, car.Handle, 0);

                for (var side = 0; side < 4; side++)
                {
                    Function.Call(Hash.SET_VEHICLE_NEON_ENABLED, car.Handle, side, true);
                }

                Function.Call(Hash.SET_VEHICLE_NEON_COLOUR, car.Handle, 0, 255, 90);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not paint a roller: " + ex.Message);
            }
        }

        /// <summary>One of them, in one seat.</summary>
        private Ped Fill(GangDef gang, Vehicle car, int seat)
        {
            try
            {
                var name = gang.MemberModels[_rng.Next(gang.MemberModels.Count)];

                var model = new Model(name);
                if (!model.IsValid || !model.IsInCdImage || !Core.Models.Ready(model)) return null;

                var handle = Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE, car.Handle,
                                                PedTypeCiv, model.Hash, seat, false, false);

                model.MarkAsNoLongerNeeded();
                if (handle == 0) return null;

                var man = Entity.FromHandle(handle) as Ped;
                if (man == null || !man.Exists()) return null;

                man.IsPersistent = true;

                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, man.Handle, true, true);
                Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, man.Handle, gang.GroupHash);

                // Not told to ignore the world. They are ours and they are on our blocks, so a
                // set that drove past a fight without looking at it would be worse than one that
                // gets out of the car -- and the stuck watchdog covers the mess either way.
                Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, man.Handle, false);

                // Not on a bike, and not on the bikes they are about to be put on. Set at the
                // seat rather than after, because CREATE_PED_INSIDE_VEHICLE has already sat him
                // down by the time this line runs and the lid goes on with the seat.
                Core.Helmets.Off(man);

                return man;
            }
            catch (Exception ex)
            {
                Log.Debug("Rollers could not fill a seat: " + ex.Message);
                return null;
            }
        }

        // ---- the park ------------------------------------------------------------

        /// <summary>The park, once it has been looked over. See Gangs.Park.</summary>
        private Park _park;
        private readonly ParkWorld _parkWorld;
        private readonly Func<Vector3, Vector3, bool> _awayFromBad;

        /// <summary>
        /// Where somebody came off, or ran into something the survey did not have, this session.
        ///
        /// PLACES, NOT LINKS, so they outlive the park being looked over again: a fresh survey
        /// numbers its spots from scratch, and a list of link numbers would point at different
        /// lines afterwards. Every survey turns these back into links -- see Surveyed.
        /// </summary>
        private readonly List<Vector3> _bad = new List<Vector3>();

        /// <summary>The park's links that run near one of those, or that were found blocked.</summary>
        private readonly HashSet<long> _badLinks = new HashSet<long>();

        private readonly List<Vector3> _heading = new List<Vector3>();

        private bool ParkOn => _cfg == null || _cfg.RollerParkRide;

        /// <summary>
        /// A path you recorded, and the setting to ride it. While this is on the survey is not
        /// used at all: they ride your line. See BikePath and RidePath.
        /// </summary>
        private bool PathOn => ParkOn && (_cfg == null || _cfg.RollerParkPath) && BikePath.Ready;

        private bool ParkUsable => ParkOn && (PathOn || (_park != null && _park.Ready));

        /// <summary>Whether a destination is the park: a point of your path, or inside the surveyed park.</summary>
        private bool ParkTarget(Vector3 at) => PathOn ? BikePath.On(at) : _park != null && _park.Ready && _park.Inside(at);

        /// <summary>Where somebody coming from there gets on: the nearest point of your path, or the survey's way in.</summary>
        private Vector3 ParkEntry(Vector3 from)
        {
            // Your path is got on at its start, and ridden round from there. See Steer.
            if (PathOn) return BikePath.First;

            var door = _park.EntryFor(from);
            return door >= 0 ? _park.Spots[door] : Vector3.Zero;
        }
        private bool InsidePark(Vector3 at) => ParkOn && _park != null && _park.Inside(at);

        private Vector3 ParkCentre => _cfg == null
            ? new Vector3(-252f, -1578f, 31f)
            : new Vector3(_cfg.RollerParkX, _cfg.RollerParkY, _cfg.RollerParkZ);

        private float ParkRadius => _cfg == null ? 70f : _cfg.RollerParkRadius;
        private float ParkSpeed => _cfg == null ? 5f : _cfg.RollerParkSpeed;

        /// <summary>Rays a frame for the survey. A thousand-odd rays is a couple of seconds of these.</summary>
        private const int ParkRaysPerFrame = 60;

        /// <summary>Only looked over while you are near enough for its ground to be loaded.</summary>
        private const float SurveyRange = 200f;

        /// <summary>
        /// Looked over again every four minutes while you are about, because things get placed,
        /// moved and parked. A failed look is tried again sooner.
        /// </summary>
        private const int ResurveyMs = 240000;
        private const int RetryMs = 60000;

        /// <summary>How long a crew rides the park before heading out. Long enough to go round it.</summary>
        private const int ParkRideMinMs = 50000;
        private const int ParkRideMaxMs = 130000;

        /// <summary>How near the way in he has to be before he is riding the park.</summary>
        private const float ParkArrive = 6f;

        /// <summary>How far out from the way in he slows to park speed.</summary>
        private const float ParkApproach = 35f;

        /// <summary>How often a rider in the park is looked at.</summary>
        private const int WatchMs = 200;

        /// <summary>Near enough the end of a line to be given the next one without slowing.</summary>
        private const float LegDone = 3f;

        /// <summary>
        /// A crash, from the outside: doing more than this, and a fifth of a second later less
        /// than this share of it, without having been told to brake. Slowing for a corner or
        /// the end of a line is gentler than that; hitting a ramp is not.
        /// </summary>
        private const float CrashFrom = 3f;
        private const float CrashDrop = 0.35f;

        /// <summary>Less than this far in this long, this many times running, and he gives up on the park.</summary>
        private const int ProgressMs = 3500;
        private const float StallMove = 1.5f;
        private const int StallsMost = 4;

        /// <summary>How long he waits for somebody in his way before going another way round.</summary>
        private const int PersonWaitMs = 3000;

        /// <summary>The brake temp action, and for how long.</summary>
        private const int BrakeAction = 1;
        private const int BrakeMs = 700;

        /// <summary>A bike's origin sits about this far above its wheels' contact with the ground.</summary>
        private const float WheelsBelow = 0.45f;

        /// <summary>How wide a berth a bad place gets, and how many are remembered.</summary>
        private const float BadKeepOff = 2f;
        private const int BadMost = 64;

        /// <summary>A new line more than seventy-odd degrees off where he is pointing: slow for it.</summary>
        private const float SharpTurn = 0.35f;

        /// <summary>How far ahead he looks: some, plus more the faster he is going, never past the line's end.</summary>
        private const float LookLeast = 1.6f;
        private const float LookPerSpeed = 0.9f;
        private const float LookMost = 7f;

        /// <summary>Where the looking starts: in front of the bike, so it never sees its own front wheel.</summary>
        private const float NoseAhead = 1.2f;

        /// <summary>How far ahead the ground is checked for a drop or a step.</summary>
        private const float GroundAhead = 2.5f;

        /// <summary>Above the bike's origin: about knee and shin height off the ground.</summary>
        private static readonly float[] LookHeights = { 0.3f, 0f };

        private const IntersectFlags SeeFlags = IntersectFlags.Map | IntersectFlags.Objects
                                              | IntersectFlags.Vehicles | IntersectFlags.Peds;

        /// <summary>
        /// Room round the back wheel a burnout needs, and how near nobody may be stood. 2.5 m
        /// rather than 3.5: the bike does not go anywhere while it smokes, and at 3.5 almost
        /// nowhere along a path that runs by a wall or a bench had room, so they never did it.
        /// </summary>
        private const float BurnRoom = 2.5f;
        private const float BurnPeople = 5f;

        /// <summary>
        /// How a line in the park is ridden. The game's own driving flags:
        ///
        ///    2  stop for peds               16  steer round peds
        ///    4  swerve round cars           32  steer round objects
        ///    8  steer round parked cars     16777216  straight line
        ///
        /// THE STRAIGHT LINE IS THE IMPORTANT ONE. Without it the drive task treats any point
        /// near a road as a road trip -- down to the nearest road node, along the road, and in
        /// again from wherever that comes out -- which is the route nobody checked. The line it
        /// is given here HAS been checked, so the line is what it rides.
        /// </summary>
        private const int ParkStyle = 2 | 4 | 8 | 16 | 32 | 16777216;

        private enum Sight
        {
            Clear,
            Person,
            Thing
        }

        /// <summary>
        /// Looks the park over when it needs it, a slice a frame.
        ///
        /// The park is made from the ini on the first frame and remade if the ini's park moves,
        /// so InsidePark is answering from the start -- nothing is sent across it at road speed
        /// while it is still being looked at.
        /// </summary>
        private void SurveyPark(int now)
        {
            if (!ParkOn) return;

            var centre = ParkCentre;
            var radius = ParkRadius;

            if (_park == null || Park.Flat2(_park.Centre, centre) > 0.01f ||
                Math.Abs(_park.Centre.Z - centre.Z) > 0.01f || Math.Abs(_park.Radius - radius) > 0.01f)
            {
                _park = new Park(centre, radius);

                // The courts, and anything else somebody has listed as a place to be: the
                // riders make a point of passing them.
                foreach (var spot in Hangouts) _park.Landmarks.Add(spot);

                // Anybody riding the old one is holding spot numbers that mean nothing now.
                foreach (var roll in _out)
                {
                    if (roll.InPark) roll.ParkDone = true;
                }
            }

            // RIDING YOUR PATH, NOT A SURVEY. Nothing to look over: the park stays as a circle,
            // so every other destination still keeps out of it, and not a ray is spent.
            if (PathOn) return;

            Ped player;

            try
            {
                player = Game.Player.Character;
            }
            catch
            {
                return;
            }

            if (player == null || !player.Exists()) return;

            var away = Park.Flat2(player.Position, centre);

            if (_park.Busy)
            {
                // Paused rather than dropped while you are too far off for its ground to be
                // loaded -- a ray at ground that is not there finds nothing, and a survey done
                // from the other side of town would say there is no park.
                if (away > SurveyRange + 60f) return;
                if (!_park.Step(ParkRaysPerFrame, now)) return;

                Surveyed();
                return;
            }

            if (!Enabled) return;
            if (away > SurveyRange) return;
            if (!OnOurTurf()) return;

            if (_park.SurveyedAt != 0 &&
                now - _park.SurveyedAt < (_park.Ready ? ResurveyMs : RetryMs))
            {
                return;
            }

            _park.Begin(_parkWorld);
        }

        private void Surveyed()
        {
            // Everybody in the park is holding spot numbers from the last map.
            foreach (var roll in _out)
            {
                if (!roll.InPark) continue;

                roll.Path = null;
                roll.PathAt = 0;
                roll.LegNode = -1;
                roll.LegTo = Vector3.Zero;
                roll.Seen.Clear();
            }

            _badLinks.Clear();
            foreach (var at in _bad) MarkLinksNear(at);

            var ramps = 0;
            foreach (var b in _park.Blocks)
            {
                if (b.Feature) ramps++;
            }

            if (_park.Ready)
            {
                Log.Info("Rollers: the park looked over -- " + _park.Spots.Count + " places to ride, " +
                         _park.LinkCount + " clear lines between them, " + _park.Blocks.Count +
                         " things in it to go round (" + ramps + " of them ramps), " +
                         _park.Doors.Count + " ways in off the road. " + _park.RaysUsed + " rays.");
            }
            else
            {
                Log.Info("Rollers: the park looked over and only " + _park.Spots.Count +
                         " places were clear to ride, so nobody is sent in until it is looked at again. " +
                         "If that is wrong, check RollerParkX and RollerParkY are the middle of the park.");
            }
        }

        /// <summary>A crew arrives at the way in: from here Ride has him.</summary>
        private void StartPark(Roll roll, int now)
        {
            roll.InPark = true;
            roll.Leaving = false;
            roll.ParkDone = false;
            roll.SitUntil = now + _rng.Next(ParkRideMinMs, ParkRideMaxMs);

            // YOUR PATH, ROUND AT LEAST ONCE. A minute or two of the survey's park was plenty,
            // but a loop you rode is something to go round: all of it, and up to half again,
            // timed off its own length at a little under his park speed for the corners.
            if (PathOn)
            {
                var lap = (int)(BikePath.Metres * (BikePath.Loop ? 1f : 2f) / Math.Max(2f, ParkSpeed - 1f) * 1000f);
                roll.SitUntil = now + lap + _rng.Next(lap / 2 + 1);
            }

            // A CREW GOES ROUND TOGETHER AND LEAVES TOGETHER: one clock between them, the
            // lead's, whichever of them got there first.
            var lead = roll.Lead != null && _out.Contains(roll.Lead) ? roll.Lead : null;
            if (lead != null && lead.InPark) roll.SitUntil = lead.SitUntil;

            foreach (var mate in _out)
            {
                if (mate.Lead == roll && mate.InPark) mate.SitUntil = roll.SitUntil;
            }

            roll.Path = null;
            roll.PathAt = 0;
            roll.LegNode = -1;
            roll.LegTo = Vector3.Zero;

            roll.PathVersion = 0;
            roll.Way = 1;
            roll.LeaveBy = 0;

            roll.WatchAt = now;
            roll.LastSpeed = 0f;
            roll.Braking = false;
            roll.BrakeSince = 0;

            roll.ProgressAt = now + ProgressMs;
            roll.ProgressFrom = roll.Car.Position;
            roll.Stalls = 0;
            roll.Seen.Clear();

            roll.RingRadius = 0f;
            roll.BurnUntil = 0;
            roll.BurnAfter = now + _rng.Next(BurnFirstMinMs, BurnFirstMaxMs) * 2;

            try
            {
                Function.Call(Hash.SET_DRIVER_ABILITY, roll.Driver.Handle, 1.0f);
                Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, roll.Driver.Handle, 0.0f);
            }
            catch
            {
                // He rides as he is.
            }

            Log.Debug("Rollers: a rider went into the park.");
        }

        private static void LeavePark(Roll roll)
        {
            roll.InPark = false;
            roll.Leaving = false;
            roll.ParkDone = false;
            roll.Path = null;
            roll.LegNode = -1;
            roll.LegTo = Vector3.Zero;
            roll.Braking = false;

            if (roll.BurnUntil == 0) return;

            roll.BurnUntil = 0;

            try { Function.Call(Hash.SET_VEHICLE_BURNOUT, roll.Car.Handle, false); }
            catch { }
        }

        /// <summary>Everybody hanging about on a bike, looked at five times a second, each on his own clock.</summary>
        private void WatchRiders(int now)
        {
            for (var i = 0; i < _out.Count; i++)
            {
                var roll = _out[i];

                if (!roll.OnFoot || roll.Phase != RollPhase.Sitting) continue;
                if (now < roll.WatchAt) continue;

                roll.WatchAt = now + WatchMs;

                try
                {
                    if (roll.InPark) Ride(roll, now);
                    else if (roll.RingRadius > 0f) Mind(roll, now);
                }
                catch (Exception ex)
                {
                    Log.Debug("Rollers: a rider tripped: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// One look at one rider in the park.
        ///
        /// WHAT HE IS DOING is riding a straight line the park survey says is clear, from one
        /// spot to another, and being handed the next line before he gets to the end of this
        /// one -- so from outside it is somebody riding round the park, not stopping and
        /// starting. Where he is going is somewhere he has not been for a while, so over a
        /// couple of minutes he takes in all of it.
        ///
        /// WHAT HE IS WATCHING FOR, because a checked line is only checked for what was there
        /// when it was checked:
        ///   somebody in the way   -- slows right down and waits, then goes another way
        ///   something in the way  -- brakes, and that place is off the map for everybody
        ///   a crash anyway        -- a sudden stop nobody asked for; same again
        ///   not getting anywhere  -- a new route, and after a few of those, out of the park
        /// </summary>
        private void Ride(Roll roll, int now)
        {
            var bike = roll.Car;
            var rider = roll.Driver;

            if (bike == null || !bike.Exists()) return;
            if (rider == null || !rider.Exists() || !rider.IsAlive) return;

            // YOUR PATH, when there is one. See RidePath.
            if (PathOn)
            {
                RidePath(roll, now);
                return;
            }

            // Riding your path until it went -- forgotten, or switched off. What he holds are
            // points of it, not spots on the survey's map, so he starts the survey's over.
            if (roll.PathVersion != 0)
            {
                roll.PathVersion = 0;
                roll.Path = null;
                roll.LegNode = -1;
                roll.LegTo = Vector3.Zero;
            }

            // The park went while he was in it -- switched off, or looked over again and found
            // wanting. Out the ordinary way.
            if (!ParkUsable)
            {
                roll.ParkDone = true;
                return;
            }

            var pos = bike.Position;
            var speed = bike.Speed;

            // ---- a burnout running ------------------------------------------------------
            if (roll.BurnUntil != 0)
            {
                if (now < roll.BurnUntil)
                {
                    roll.LastSpeed = 0f;
                    return;
                }

                roll.BurnUntil = 0;

                try { Function.Call(Hash.SET_VEHICLE_BURNOUT, bike.Handle, false); }
                catch { }

                roll.BurnAfter = now + _rng.Next(BurnRestMinMs, BurnRestMaxMs) * 2;
                roll.LegTo = Vector3.Zero;
                roll.ProgressFrom = pos;
                roll.ProgressAt = now + ProgressMs;
            }

            // ---- time up: the rest of the ride is to the way out -------------------------
            if (!roll.Leaving && now >= roll.SitUntil)
            {
                roll.Leaving = true;
                roll.Path = null;
            }

            // ---- a stop nobody asked for ------------------------------------------------
            // Only from park speed. Coming off the road he can arrive at the door doing three
            // times that, and slowing to ride the first line is a big drop that is not a crash.
            if (!roll.Braking && roll.LegTo != Vector3.Zero &&
                roll.LastSpeed > CrashFrom && roll.LastSpeed <= ParkSpeed + 1.5f &&
                speed < roll.LastSpeed * CrashDrop)
            {
                Bad(pos);
                Log.Info(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "Rollers: a rider hit something in the park at {0:0.0}, {1:0.0}. That spot is off the map this session.",
                    pos.X, pos.Y));

                roll.Path = null;
                roll.LegTo = Vector3.Zero;
            }

            roll.LastSpeed = speed;

            // ---- the end of the line ----------------------------------------------------
            if (roll.LegTo != Vector3.Zero && Park.Flat2(pos, roll.LegTo) < LegDone)
            {
                if (roll.LegNode >= 0) roll.Seen[roll.LegNode] = now;

                roll.LegTo = Vector3.Zero;

                if (roll.Leaving && roll.LegNode == _park.Exit)
                {
                    roll.ParkDone = true;
                    return;
                }

                if (!roll.Leaving && now >= roll.BurnAfter)
                {
                    if (RoomToBurn(roll))
                    {
                        Burn(roll, now);
                        return;
                    }

                    roll.BurnAfter = now + BurnRestMinMs;
                }
            }

            // ---- looking where he is going ----------------------------------------------
            if (roll.LegTo != Vector3.Zero && (speed > 0.6f || roll.Braking))
            {
                Vector3 seen;
                var what = Ahead(bike, speed, Park.Flat2(pos, roll.LegTo) + 1f, out seen);

                if (what == Sight.Clear)
                {
                    if (roll.Braking)
                    {
                        roll.Braking = false;
                        roll.BrakeSince = 0;
                        SetSpeed(roll, LegSpeed(roll, pos));
                    }
                }
                else if (what == Sight.Person)
                {
                    // Somebody in the way. Wait for them, the way anybody on a bike does -- and
                    // if they are not going anywhere, go somewhere else.
                    if (!roll.Braking)
                    {
                        roll.Braking = true;
                        roll.BrakeSince = now;
                        SetSpeed(roll, 0.5f);
                    }
                    else if (now - roll.BrakeSince > PersonWaitMs)
                    {
                        roll.Braking = false;
                        roll.BrakeSince = 0;
                        roll.Path = null;
                        roll.LegTo = Vector3.Zero;
                    }
                }
                else
                {
                    // Something the survey did not have: put there since, or too thin for the
                    // rays that made the map. On the brakes, and nobody goes past it again.
                    Bad(seen);
                    Brake(roll, now);

                    roll.Path = null;
                    roll.LegTo = Vector3.Zero;
                    return;
                }
            }

            // ---- getting anywhere -------------------------------------------------------
            if (now >= roll.ProgressAt)
            {
                var moved = Park.Flat2(pos, roll.ProgressFrom);

                roll.ProgressFrom = pos;
                roll.ProgressAt = now + ProgressMs;

                if (moved >= StallMove)
                {
                    roll.Stalls = 0;
                }
                else if (!roll.Braking)
                {
                    roll.Path = null;
                    roll.LegTo = Vector3.Zero;

                    if (++roll.Stalls >= StallsMost)
                    {
                        Log.Debug("Rollers: a rider could not get anywhere in the park and rode out.");
                        roll.ParkDone = true;
                        return;
                    }
                }
            }

            if (roll.LegTo == Vector3.Zero) NextParkLeg(roll, now);
        }

        // ---- riding your path -------------------------------------------------------------

        /// <summary>How much of your path one line takes at most, how close to it the line keeps, and the shortest line given.</summary>
        private const float PathLegMost = 16f;
        private const float PathHug = 0.75f;
        private const float PathLegLeast = 6f;

        /// <summary>Time up: this long to get round to the way out, and how near it counts as out.</summary>
        private const int PathLeaveMostMs = 60000;

        /// <summary>How near the start of your path a rider has to come to take it, and how long after coming off it before he will again.</summary>
        private const float PathJoinRange = 35f;
        private const int PathAgainMs = 120000;
        private const float PathOut = 8f;

        /// <summary>The point of your path nearest a road, and which path that was worked out for.</summary>
        private int _pathExit = -1;
        private int _pathExitFor = -1;

        /// <summary>
        /// One look at one rider on your path.
        ///
        /// THE SAME RIDER ON A DIFFERENT MAP. What Ride does with the survey's spots this does
        /// with the points you rode: a straight line along as much of it as keeps to it, the
        /// next one handed over before the end of this one, round and round a loop or to the end
        /// and back. When his time is up he carries on round to the point nearest a road and
        /// rides out there.
        ///
        /// WHAT IS DIFFERENT IS WHAT HE WATCHES FOR. The survey's lines were checked, so anything
        /// in the way was new and worth taking off the map. Yours were ridden -- a ramp in front
        /// of him is a ramp you rode over -- so he brakes for people and anything moving and for
        /// nothing else. If he hits something anyway the log says where, so that stretch can be
        /// ridden again.
        /// </summary>
        private void RidePath(Roll roll, int now)
        {
            var bike = roll.Car;
            var pos = bike.Position;
            var speed = bike.Speed;

            // ---- a burnout running ------------------------------------------------------
            if (roll.BurnUntil != 0)
            {
                if (now < roll.BurnUntil)
                {
                    roll.LastSpeed = 0f;
                    return;
                }

                roll.BurnUntil = 0;

                try { Function.Call(Hash.SET_VEHICLE_BURNOUT, bike.Handle, false); }
                catch { }

                roll.BurnAfter = now + _rng.Next(BurnRestMinMs, BurnRestMaxMs) * 2;
                roll.LegTo = Vector3.Zero;
                roll.ProgressFrom = pos;
                roll.ProgressAt = now + ProgressMs;
            }

            // ---- a path recorded since he joined this one: he joins that ----------------
            if (roll.PathVersion != BikePath.Version)
            {
                roll.PathVersion = BikePath.Version;
                roll.LegNode = -1;
                roll.LegTo = Vector3.Zero;
            }

            // ---- time up: on round to the way out ---------------------------------------
            if (!roll.Leaving && now >= roll.SitUntil)
            {
                roll.Leaving = true;
                roll.LeaveBy = now + PathLeaveMostMs;
            }

            if (roll.Leaving)
            {
                var exit = PathExit();

                if (now >= roll.LeaveBy || exit < 0 || Park.Flat2(pos, BikePath.Points[exit]) < PathOut)
                {
                    roll.ParkDone = true;
                    return;
                }
            }

            // ---- a stop nobody asked for ------------------------------------------------
            if (!roll.Braking && roll.LegTo != Vector3.Zero &&
                roll.LastSpeed > CrashFrom && roll.LastSpeed <= ParkSpeed + 1.5f &&
                speed < roll.LastSpeed * CrashDrop)
            {
                Log.Info(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "Rollers: a rider hit something on your path at {0:0.0}, {1:0.0}. If it keeps happening there, ride that stretch again.",
                    pos.X, pos.Y));

                roll.LegTo = Vector3.Zero;
            }

            roll.LastSpeed = speed;

            // ---- the end of the line ----------------------------------------------------
            // Near it he is asked for the next one -- which round a corner is this one still,
            // until he is into the corner. See BikePath.Ahead.
            var due = roll.LegTo != Vector3.Zero && Park.Flat2(pos, roll.LegTo) < LegDone;

            // HE STOPS AND LIGHTS UP THE BACK WHEEL. Now and then, where there is room, a man on
            // an engine pulls up, holds it on the brake and smokes the back tyre for a few
            // seconds, then rides on -- and his crew beside him do it with him. Michael asked for
            // it on 2026-09-24. Not on a BMX, which has no engine to do it with.
            if (due && !roll.Leaving && now >= roll.BurnAfter)
            {
                if (!Pedal(roll) && RoomToBurn(roll))
                {
                    var ms = PathBurnMinMs + _rng.Next(PathBurnMaxMs - PathBurnMinMs);

                    roll.LegTo = Vector3.Zero;
                    Burn(roll, now, ms);
                    CrewBurns(roll, now, ms);
                    return;
                }

                roll.BurnAfter = now + BurnRestMinMs;
            }

            // ---- people, and anything moving --------------------------------------------
            if (roll.LegTo != Vector3.Zero && (speed > 0.6f || roll.Braking))
            {
                Vector3 seen;
                var what = Ahead(bike, speed, Park.Flat2(pos, roll.LegTo) + 1f, out seen, true);

                if (what == Sight.Clear)
                {
                    if (roll.Braking)
                    {
                        roll.Braking = false;
                        roll.BrakeSince = 0;
                        SetSpeed(roll, LegSpeed(roll, pos));
                    }
                }
                else if (!roll.Braking)
                {
                    roll.Braking = true;
                    roll.BrakeSince = now;
                    SetSpeed(roll, 0.5f);
                }
                else if (now - roll.BrakeSince > PersonWaitMs)
                {
                    // Still there. The line again from here, and the drive task goes round him.
                    roll.Braking = false;
                    roll.BrakeSince = 0;
                    roll.LegTo = Vector3.Zero;
                }
            }

            // ---- getting anywhere -------------------------------------------------------
            if (now >= roll.ProgressAt)
            {
                var moved = Park.Flat2(pos, roll.ProgressFrom);

                roll.ProgressFrom = pos;
                roll.ProgressAt = now + ProgressMs;

                if (moved >= StallMove)
                {
                    roll.Stalls = 0;
                }
                else if (!roll.Braking)
                {
                    roll.LegTo = Vector3.Zero;

                    if (++roll.Stalls >= StallsMost)
                    {
                        Log.Info(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "Rollers: a rider could not get on along your path at {0:0.0}, {1:0.0} and rode out.",
                            pos.X, pos.Y));

                        roll.ParkDone = true;
                        return;
                    }
                }
            }

            if (roll.LegTo == Vector3.Zero || due) NextPathLeg(roll);
        }

        /// <summary>The next straight line along your path, from wherever his wheels are.</summary>
        private void NextPathLeg(Roll roll)
        {
            var pts = BikePath.Points;

            if (pts.Count < 2)
            {
                roll.ParkDone = true;
                return;
            }

            var wheels = Wheels(roll.Car);

            // Joining it: at the nearest point, going the way that has more of it ahead.
            if (roll.LegNode < 0 || roll.LegNode >= pts.Count)
            {
                var n = BikePath.Nearest(wheels);

                if (n < 0)
                {
                    roll.ParkDone = true;
                    return;
                }

                roll.Way = BikePath.Loop || n < pts.Count / 2 ? 1 : -1;
                roll.LegNode = n;

                if (Park.Flat2(wheels, pts[n]) > LegDone)
                {
                    GoPath(roll, n);
                    return;
                }
            }

            var way = roll.Way;
            var k = BikePath.Ahead(roll.LegNode, ref way, wheels, PathLegMost, PathHug, PathLegLeast);

            // Nothing past the point he is riding to keeps to the path from here yet: he rides on
            // into it on the task he already has, rather than being handed the same one again.
            if (k == roll.LegNode && roll.LegTo != Vector3.Zero) return;

            roll.Way = way;
            GoPath(roll, k);
        }

        /// <summary>
        /// Off to the start of your path, to ride it: the same instruction Aim gives a rider going
        /// to hang out, with the start as the place. Steer starts him on it when he gets there.
        /// </summary>
        private void ToPath(Roll roll, int now, bool crew = true)
        {
            var where = BikePath.First;

            // NUDGES ARE LEFT AS THEY ARE, on purpose. A man who cannot get to the start is
            // pointed somewhere else by the stuck check, rides past the start again, and is sent
            // back to it -- and if this cleared his count he would do that for ever instead of
            // being handed back.
            roll.Target = where;
            roll.StopThere = true;
            roll.LookedAt = now;
            roll.WasAt = roll.Car.Position;

            try
            {
                Function.Call(Hash.CLEAR_PED_TASKS, roll.Driver.Handle);

                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, roll.Driver.Handle, roll.Car.Handle,
                              where.X, where.Y, where.Z, CruiseBike, 0, roll.Car.Model.Hash, StyleBike, 4f, true);

                Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, roll.Driver.Handle, CruiseBike);
                roll.Cruise = 0f;
                roll.PaceAt = 0;

                Function.Call(Hash.SET_DRIVER_ABILITY, roll.Driver.Handle, 1.0f);
                Function.Call(Hash.SET_PED_KEEP_TASK, roll.Driver.Handle, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Rollers: could not send a rider to the path: " + ex.Message);
            }

            if (!crew) return;

            // HIS CREW COMES WITH HIM, wherever they are behind him: a crew that splits at the
            // start of the path is two groups of strangers.
            var with = 0;

            foreach (var other in _out)
            {
                if (other == roll || !other.OnFoot || other.InPark || !SameCrew(roll, other)) continue;
                if (other.Phase != RollPhase.Rolling || (other.StopThere && ParkTarget(other.Target))) continue;
                if (other.Car == null || !other.Car.Exists()) continue;
                if (other.Driver == null || !other.Driver.Exists() || !other.Driver.IsAlive) continue;

                ToPath(other, now, false);
                with++;
            }

            Log.Info("Rollers: a rider came by the start of your path and took it" +
                     (with > 0 ? ", and " + with + " of his crew with him." : "."));
        }

        /// <summary>How long a burnout on the path lasts, and how near his crew have to be to join in.</summary>
        private const int PathBurnMinMs = 3000;
        private const int PathBurnMaxMs = 6500;
        private const float CrewBurnNear = 15f;

        /// <summary>His crew, near him on the path, pull up and do it with him, and all ride on after.</summary>
        private void CrewBurns(Roll roll, int now, int ms)
        {
            foreach (var other in _out)
            {
                if (other == roll || !other.InPark || other.Leaving || other.BurnUntil != 0) continue;
                if (!SameCrew(roll, other) || Pedal(other)) continue;
                if (other.Car == null || !other.Car.Exists()) continue;
                if (other.Driver == null || !other.Driver.Exists() || !other.Driver.IsAlive) continue;
                if (Park.Flat2(other.Car.Position, roll.Car.Position) > CrewBurnNear) continue;

                other.LegTo = Vector3.Zero;
                Burn(other, now, ms + _rng.Next(-600, 601));
            }
        }

        /// <summary>Whether two of them rode out together: one the other's lead, or both the same man's.</summary>
        private static bool SameCrew(Roll a, Roll b)
        {
            return a.Lead == b || b.Lead == a || (a.Lead != null && a.Lead == b.Lead);
        }

        /// <summary>A pedal bike: no engine, so no burnout.</summary>
        private static bool Pedal(Roll roll)
        {
            try { return roll.Car != null && roll.Car.Exists() && roll.Car.Model.IsBicycle; }
            catch { return true; }
        }

        /// <summary>Off along one line, to one point of your path.</summary>
        private void GoPath(Roll roll, int index)
        {
            roll.LegNode = index;
            roll.LegTo = BikePath.Points[index];
            roll.Braking = false;
            roll.BrakeSince = 0;

            var speed = LegSpeed(roll, roll.Car.Position);
            var to = roll.LegTo;

            try
            {
                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, roll.Driver.Handle, roll.Car.Handle,
                              to.X, to.Y, to.Z, speed, 0, roll.Car.Model.Hash, ParkStyle, 1.5f, true);

                Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, roll.Driver.Handle, speed);
                Function.Call(Hash.SET_PED_KEEP_TASK, roll.Driver.Handle, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Rollers: could not send a rider along the path: " + ex.Message);
            }
        }

        /// <summary>The point of your path nearest a road: where he rides out. Worked out once a path.</summary>
        private int PathExit()
        {
            if (_pathExitFor == BikePath.Version) return _pathExit;

            _pathExitFor = BikePath.Version;
            _pathExit = -1;

            var pts = BikePath.Points;
            var best = float.MaxValue;

            // Every other point is plenty, and it is a native call apiece.
            for (var i = 0; i < pts.Count; i += 2)
            {
                var d = _parkWorld.ToRoad(pts[i]);
                if (d < 0f || d >= best) continue;

                best = d;
                _pathExit = i;
            }

            if (_pathExit < 0 && pts.Count > 0) _pathExit = 0;
            return _pathExit;
        }

        /// <summary>
        /// The next straight line for a rider in the park.
        ///
        /// Somewhere to go if he has nowhere (or the way out, if his time is up), a route there
        /// over the park's links that stays off every bad place, and then the longest straight
        /// piece of that route he can ride from where his wheels actually are -- checked there
        /// and then against the world, not taken on trust from the survey.
        /// </summary>
        private void NextParkLeg(Roll roll, int now)
        {
            var wheels = Wheels(roll.Car);
            var from = _park.Nearest(wheels);

            if (from < 0)
            {
                roll.ParkDone = true;
                return;
            }

            // Off the grid -- just in from the road, or pushed off a line -- and the nearest
            // spot is a clear ride away: that first.
            if (Park.Flat2(wheels, _park.Spots[from]) > 2.5f &&
                _awayFromBad(wheels, _park.Spots[from]) &&
                _park.Clear(_parkWorld, wheels, _park.Spots[from]))
            {
                Go(roll, from, roll.Car.Position);
                return;
            }

            for (var tries = 0; tries < 4; tries++)
            {
                if (roll.Path == null || roll.PathAt >= roll.Path.Count)
                {
                    var goal = roll.Leaving
                        ? _park.Exit
                        : _park.Pick(from, roll.Seen, Heading(roll), _rng, now);

                    if (goal < 0 || goal == from)
                    {
                        if (roll.Leaving)
                        {
                            roll.ParkDone = true;
                            return;
                        }

                        continue;
                    }

                    roll.Path = _park.Route(from, goal, _badLinks);
                    roll.PathAt = 1;

                    if (roll.Path == null || roll.Path.Count < 2)
                    {
                        roll.Path = null;

                        // No way to the door that stays off the bad places. Out by road.
                        if (roll.Leaving)
                        {
                            roll.ParkDone = true;
                            return;
                        }

                        continue;
                    }
                }

                var k = _park.NextLeg(_parkWorld, wheels, roll.Path, roll.PathAt, _awayFromBad);

                if (k >= 0)
                {
                    roll.PathAt = k + 1;
                    Go(roll, roll.Path[k], roll.Car.Position);
                    return;
                }

                // Not even the next spot along is clear from here: something is in the way that
                // was not there when the park was looked over. Off the map, another way round.
                if (Park.Flat2(wheels, _park.Spots[from]) < 3f && roll.PathAt < roll.Path.Count)
                {
                    _badLinks.Add(Park.LinkKey(from, roll.Path[roll.PathAt]));
                }

                roll.Path = null;
            }

            // Nowhere to go from here right now. Stood still, and looked at again shortly --
            // and if that keeps happening, out of the park.
            if (++roll.Stalls >= StallsMost)
            {
                roll.ParkDone = true;
                return;
            }

            Brake(roll, now);
        }

        /// <summary>Off along one line, to one spot.</summary>
        private void Go(Roll roll, int spot, Vector3 pos)
        {
            roll.LegNode = spot;
            roll.LegTo = _park.Spots[spot];
            roll.Braking = false;
            roll.BrakeSince = 0;

            var speed = LegSpeed(roll, pos);
            var to = roll.LegTo;

            try
            {
                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, roll.Driver.Handle, roll.Car.Handle,
                              to.X, to.Y, to.Z, speed, 0, roll.Car.Model.Hash, ParkStyle, 1.5f, true);

                Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, roll.Driver.Handle, speed);
                Function.Call(Hash.SET_PED_KEEP_TASK, roll.Driver.Handle, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Rollers: could not send a rider across the park: " + ex.Message);
            }
        }

        /// <summary>The park speed, less for a short line and less again for a sharp turn onto one.</summary>
        private float LegSpeed(Roll roll, Vector3 pos)
        {
            var speed = ParkSpeed;
            if (roll.LegTo == Vector3.Zero) return speed;

            var to = roll.LegTo - pos;
            to.Z = 0f;

            var len = to.Length();
            if (len < 7f) speed = Math.Min(speed, 3.5f);
            if (len < 0.1f) return speed;

            try
            {
                var fwd = roll.Car.ForwardVector;
                fwd.Z = 0f;

                var flen = fwd.Length();
                if (flen > 0.1f && (fwd.X * to.X + fwd.Y * to.Y) / (flen * len) < SharpTurn)
                {
                    speed = Math.Min(speed, 2.5f);
                }
            }
            catch
            {
                // The plain number.
            }

            return speed;
        }

        private static void SetSpeed(Roll roll, float speed)
        {
            try { Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, roll.Driver.Handle, speed); }
            catch { }
        }

        /// <summary>On the brakes for a moment. Nothing new is decided until they are off.</summary>
        private static void Brake(Roll roll, int now)
        {
            roll.Braking = true;
            roll.BrakeSince = now;
            roll.WatchAt = now + BrakeMs;

            try
            {
                Function.Call(Hash.TASK_VEHICLE_TEMP_ACTION, roll.Driver.Handle, roll.Car.Handle,
                              BrakeAction, BrakeMs);
            }
            catch
            {
                SetSpeed(roll, 0f);
            }
        }

        /// <summary>
        /// What is in front of him, as far as the end of his line and no further.
        ///
        /// NO FURTHER BECAUSE HE TURNS THERE. Looking past the end of a line sees whatever the
        /// line was drawn to go round -- a ramp a metre and a bit beyond the spot -- and brakes
        /// for a thing he was never going to reach.
        ///
        /// Two rays at knee and shin height, from in front of the bike so it never sees its own
        /// wheel. A slope is not a wall: a hit on ground facing upwards is the ground rising.
        /// Then the ground a couple of metres on, for a drop, a step, or the top of something
        /// placed -- a ray reads a ramp's surface as ground, and a bike does not.
        /// </summary>
        private Sight Ahead(Vehicle bike, float speed, float limit, out Vector3 at, bool peopleOnly = false)
        {
            at = Vector3.Zero;

            var fwd = bike.ForwardVector;
            fwd.Z = 0f;
            if (fwd.Length() < 0.1f) return Sight.Clear;
            fwd.Normalize();

            var pos = bike.Position;
            var reach = Math.Min(LookMost, LookLeast + speed * LookPerSpeed);
            reach = Math.Min(reach, Math.Max(1f, limit - NoseAhead));

            var nose = pos + fwd * NoseAhead;

            foreach (var lift in LookHeights)
            {
                var from = nose + new Vector3(0f, 0f, lift);
                var hit = World.Raycast(from, from + fwd * reach, SeeFlags, bike);

                if (!hit.DidHit) continue;

                var who = hit.HitEntity;

                // The ground rising ahead, not something stood on it.
                if (who == null && hit.SurfaceNormal.Z > 0.7f) continue;

                at = hit.HitPosition;

                if ((who as Ped) != null) return Sight.Person;

                // Another rider, or a car going past: somebody to wait for, not a wall.
                var car = who as Vehicle;
                if (car != null && Moving(car)) return Sight.Person;

                // On a path somebody rode, a thing in front of him is a thing he rode past or over.
                if (peopleOnly) continue;

                return Sight.Thing;
            }

            if (peopleOnly || limit < GroundAhead) return Sight.Clear;

            var probe = pos + fwd * GroundAhead;
            var under = pos.Z - WheelsBelow;

            var g = World.Raycast(new Vector3(probe.X, probe.Y, pos.Z + 0.6f),
                                  new Vector3(probe.X, probe.Y, under - 1.6f),
                                  IntersectFlags.Map | IntersectFlags.Objects, bike);

            if (!g.DidHit)
            {
                at = probe;
                return Sight.Thing;
            }

            var rise = g.HitPosition.Z - under;
            var onThing = g.HitEntity != null && g.HitEntity.Exists();

            // A slope as steep as the park allows, over this distance, plus a kerb's worth.
            var most = Park.Bump + GroundAhead * Park.Steepest;

            if (onThing || rise > most || rise < -(0.8f + GroundAhead * Park.Steepest))
            {
                at = g.HitPosition;
                return Sight.Thing;
            }

            return Sight.Clear;
        }

        /// <summary>
        /// A rider hanging about somewhere that is not the park -- riding his circle -- given
        /// the same eyes. Anything in front of him and the circle is done: he brakes and moves on.
        /// </summary>
        private void Mind(Roll roll, int now)
        {
            if (roll.BurnUntil != 0 && now < roll.BurnUntil) return;

            var bike = roll.Car;
            if (bike == null || !bike.Exists()) return;
            if (roll.Driver == null || !roll.Driver.Exists() || !roll.Driver.IsAlive) return;

            float speed;

            try { speed = bike.Speed; }
            catch { return; }

            if (speed < 0.6f) return;

            Vector3 seen;
            if (Ahead(bike, speed, LookMost, out seen) == Sight.Clear) return;

            Brake(roll, now);
            roll.SitUntil = now;
        }

        /// <summary>
        /// Whether there is room for a burnout here: nothing placed, no wall and no car within
        /// reach of the back wheel, and nobody stood close enough to get it in the shins.
        /// </summary>
        private bool RoomToBurn(Roll roll)
        {
            try
            {
                var bike = roll.Car;
                var pos = bike.Position;
                var ground = pos.Z - WheelsBelow;

                if (_park != null && _park.Ready)
                {
                    foreach (var b in _park.Blocks)
                    {
                        if (b.Hi < ground + 0.14f || b.Lo > ground + 2.2f) continue;
                        if (b.Distance(pos.X, pos.Y) < BurnRoom) return false;
                    }
                }

                var from = pos + new Vector3(0f, 0f, 0.2f);

                for (var k = 0; k < 8; k++)
                {
                    var turn = k * Math.PI / 4.0;
                    var to = from + new Vector3((float)Math.Cos(turn) * BurnRoom, (float)Math.Sin(turn) * BurnRoom, 0f);

                    var hit = World.Raycast(from, to, IntersectFlags.Map | IntersectFlags.Objects | IntersectFlags.Vehicles, bike);
                    if (hit.DidHit && !(hit.HitEntity == null && hit.SurfaceNormal.Z > 0.7f)) return false;
                }

                foreach (var ped in World.GetNearbyPeds(pos, BurnPeople))
                {
                    if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

                    var crew = false;
                    foreach (var m in roll.Crew)
                    {
                        if (m != null && m.Handle == ped.Handle) crew = true;
                    }

                    if (!crew) return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private void Burn(Roll roll, int now, int ms = 0)
        {
            if (ms <= 0) ms = BurnMinMs + _rng.Next(BurnMaxMs - BurnMinMs);

            try
            {
                Function.Call(Hash.CLEAR_PED_TASKS, roll.Driver.Handle);
                Function.Call(Hash.SET_VEHICLE_BURNOUT, roll.Car.Handle, true);
                Function.Call(Hash.TASK_VEHICLE_TEMP_ACTION, roll.Driver.Handle, roll.Car.Handle, BurnAction, ms);

                roll.BurnUntil = now + ms;
                roll.LastSpeed = 0f;
            }
            catch
            {
                roll.BurnAfter = now + BurnRestMinMs;
            }
        }

        /// <summary>Where his wheels are: the ground under the bike, or a fair guess at it.</summary>
        private static Vector3 Wheels(Vehicle bike)
        {
            var pos = bike.Position;

            try
            {
                var hit = World.Raycast(pos + new Vector3(0f, 0f, 0.3f), pos - new Vector3(0f, 0f, 2f),
                                        IntersectFlags.Map | IntersectFlags.Objects, bike);

                if (hit.DidHit) return new Vector3(pos.X, pos.Y, hit.HitPosition.Z);
            }
            catch
            {
                // The guess below.
            }

            return new Vector3(pos.X, pos.Y, pos.Z - WheelsBelow);
        }

        /// <summary>Where the other riders in the park are headed, so this one goes somewhere else.</summary>
        private List<Vector3> Heading(Roll me)
        {
            _heading.Clear();

            foreach (var r in _out)
            {
                if (r == me || !r.InPark || r.Path == null || r.Path.Count == 0) continue;

                var goal = r.Path[r.Path.Count - 1];
                if (goal >= 0 && goal < _park.Spots.Count) _heading.Add(_park.Spots[goal]);
            }

            return _heading;
        }

        /// <summary>Somewhere nobody should ride past: remembered, and its links taken off the map.</summary>
        private void Bad(Vector3 at)
        {
            if (at == Vector3.Zero) return;

            foreach (var p in _bad)
            {
                if (Park.Flat2(p, at) < 1f) return;
            }

            if (_bad.Count >= BadMost) _bad.RemoveAt(0);

            _bad.Add(at);
            MarkLinksNear(at);
        }

        private void MarkLinksNear(Vector3 at)
        {
            if (_park == null) return;

            for (var i = 0; i < _park.Spots.Count; i++)
            {
                foreach (var j in _park.Links[i])
                {
                    if (j < i) continue;
                    if (Park.Near2(at, _park.Spots[i], _park.Spots[j]) < BadKeepOff) _badLinks.Add(Park.LinkKey(i, j));
                }
            }
        }

        private bool AwayFromBad(Vector3 a, Vector3 b)
        {
            foreach (var p in _bad)
            {
                if (Park.Near2(p, a, b) < BadKeepOff) return false;
            }

            return true;
        }

        /// <summary>
        /// A car that is going somewhere: moving, one of ours, or yours. Left out of the park's
        /// survey, because it will not be there, and waited for rather than written off as a
        /// wall when it is in the way.
        /// </summary>
        private bool Moving(Vehicle car)
        {
            try
            {
                if (car == null || !car.Exists()) return false;
                if (car.Speed > 0.5f) return true;

                foreach (var roll in _out)
                {
                    if (roll.Car != null && roll.Car.Handle == car.Handle) return true;
                }

                var you = Game.Player.Character;
                if (you != null && you.Exists() && you.CurrentVehicle != null &&
                    you.CurrentVehicle.Handle == car.Handle)
                {
                    return true;
                }
            }
            catch
            {
                return true;
            }

            return false;
        }

        // ---- clearing up -------------------------------------------------------

        private void Prune(int now)
        {
            var player = Game.Player.Character;
            var mine = player != null && player.Exists() && player.CurrentVehicle != null
                       && player.CurrentVehicle.Exists()
                ? player.CurrentVehicle.Handle
                : 0;

            for (var i = _out.Count - 1; i >= 0; i--)
            {
                var roll = _out[i];

                var gone = roll.Car == null || !roll.Car.Exists();
                var noDriver = roll.Driver == null || !roll.Driver.Exists() || !roll.Driver.IsAlive;

                if (gone || noDriver)
                {
                    Release(roll);
                    _out.RemoveAt(i);
                    continue;
                }

                // Taken off us: the player got in it, or somebody else is behind the wheel. It
                // stops being ours the moment either happens.
                var driver = roll.Car.Driver;
                var taken = driver == null || !driver.Exists()
                            || driver.Handle != roll.Driver.Handle
                            || (mine != 0 && roll.Car.Handle == mine);

                var old = now - roll.BornAt > LifetimeMs;

                var far = player != null && player.Exists()
                          && roll.Car.Position.DistanceTo(player.Position) > LetGoRange;

                // Off his bike in the park is the one thing all of the looking was for, so it
                // goes in the log with where it happened. Nobody else is sent past that spot.
                if (taken && roll.InPark && !(mine != 0 && roll.Car.Handle == mine))
                {
                    var at = roll.Car.Position;
                    Bad(at);
                    Log.Info(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        PathOn
                            ? "Rollers: a rider came off on your path at {0:0.0}, {1:0.0}. If it keeps happening there, ride that stretch again."
                            : "Rollers: a rider came off in the park at {0:0.0}, {1:0.0}. Nobody is sent past there again this session.",
                        at.X, at.Y));
                }

                if (taken || old || far)
                {
                    Release(roll);
                    _out.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Handed back to the game rather than deleted.
        ///
        /// The game clears up its own traffic when nobody is looking, and it is better at
        /// choosing the moment than a distance check is. Deleting outright is for teardown,
        /// where there is no later moment to wait for.
        /// </summary>
        private void Release(Roll roll)
        {
            try
            {
                foreach (var man in roll.Crew)
                {
                    if (man == null || !man.Exists()) continue;

                    man.IsPersistent = false;
                    man.MarkAsNoLongerNeeded();
                }

                if (roll.Car != null && roll.Car.Exists())
                {
                    roll.Car.IsPersistent = false;
                    roll.Car.MarkAsNoLongerNeeded();
                }
            }
            catch
            {
                // Letting go of something already gone.
            }
        }

        private void Scrap(Roll roll)
        {
            try
            {
                foreach (var man in roll.Crew)
                {
                    if (man != null && man.Exists()) man.Delete();
                }

                if (roll.Car != null && roll.Car.Exists()) roll.Car.Delete();
            }
            catch
            {
                // Nothing left to scrap.
            }
        }

        /// <summary>Everything off the street, for a reload.</summary>
        public void RestoreWorld()
        {
            foreach (var roll in _out) Scrap(roll);
            _out.Clear();
        }
    }
}
