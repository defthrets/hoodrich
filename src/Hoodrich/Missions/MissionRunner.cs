using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Social;
using Hoodrich.Gangs;
using Hoodrich.State;
using Hoodrich.Territory;
using Hoodrich.UI;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.Missions
{
    /// <summary>Where a job has got to.</summary>
    internal enum MissionState
    {
        None,

        /// <summary>Driving to the block.</summary>
        Travel,

        /// <summary>On the block, doing the thing.</summary>
        Work,

        /// <summary>Work done, but the law is on you and Lamar is not taking delivery yet.</summary>
        Escape,

        /// <summary>
        /// The car is worth keeping, so it goes to Hao rather than up in smoke.
        ///
        /// Only ever reached on a job that rolled the Vorschlaghammer. Torching a beater is
        /// the point of a torch job; torching a clean German saloon because the script says
        /// "dump the car" is throwing away the one thing on the job worth anything.
        /// </summary>
        Deliver,

        /// <summary>
        /// The shooting is over and the law has been lost, but you are still driving the car
        /// you did it in. It has to go somewhere quiet.
        /// </summary>
        Dump,

        /// <summary>Stood next to it with a can of petrol.</summary>
        Torch,

        /// <summary>Done, on the way back to Lamar for the money.</summary>
        Collect
    }

    /// <summary>
    /// Runs one job at a time.
    ///
    /// Every job is a place you drive to, targets that are really there, and a walk back for
    /// the money -- no teleports and no mid-job menus, same as the rest of the mod. The homies
    /// are real peds who ride with you and can die, which is what makes bringing them a
    /// decision rather than free backup.
    /// </summary>
    internal sealed class MissionRunner
    {
        private const float ArriveRange = 60f;

        /// <summary>Targets are placed this far out, so the ground is streamed before they land.</summary>
        private const float PreSpawnRange = 200f;
        private const float TargetSpread = 9f;

        /// <summary>Idles for people who are not expecting you.</summary>
        private static readonly string[] IdleScenarios =
        {
            "WORLD_HUMAN_SMOKING", "WORLD_HUMAN_STAND_MOBILE", "WORLD_HUMAN_DRINKING",
            "WORLD_HUMAN_HANG_OUT_STREET", "WORLD_HUMAN_STAND_IMPATIENT"
        };
        private const int UpdateIntervalMs = 500;

        /// <summary>Rep lost for each of your own you get killed out there.</summary>
        private const float HomieLostRep = 8f;

        /// <summary>
        /// What they carry on everything that is not a hit. Micro SMGs and machine pistols.
        ///
        /// The pistol and the pump shotgun are gone, and both for the same reason: this work is
        /// done out of a car window. A pump gives you one loud noise every two seconds from a
        /// moving car and a pistol is a man plinking at a street -- the job is that a whole
        /// block hears it, which is volume.
        /// </summary>
        private static readonly string[] HomieWeapons =
        {
            "WEAPON_MICROSMG", "WEAPON_MACHINEPISTOL"
        };

        /// <summary>
        /// What they carry on a hit. Machine pistols and micro SMGs, nothing with a stock.
        ///
        /// A shotgun at a distance is a man walking into open ground to use it, and a hit on a
        /// yard full of people wants volume, not one loud noise every two seconds.
        /// </summary>
        private static readonly string[] HitWeapons =
        {
            "WEAPON_MACHINEPISTOL", "WEAPON_MICROSMG", "WEAPON_MINISMG"
        };

        /// <summary>
        /// The car a drive-by turns up in, left where the job says and nowhere else.
        ///
        /// Always the same one, and now actually so. The list used to name four and the comment
        /// above it claimed one, which was the comment being right about the intention and
        /// wrong about the code: it took the first model that loaded, so the two Vorschlaghammer
        /// entries at the front meant the other two almost never came up anyway.
        ///
        /// Cutting them is not only tidying. Both were two-seaters, so on the runs where one of
        /// them DID come up there was one passenger seat between three homies and two of them
        /// spent the job on foot chasing a car. A set car is the point: it becomes the car, and
        /// you learn where it goes afterwards.
        /// </summary>
        private static readonly string[] DriveByCars = { "vorschlaghammer" };

        /// <summary>
        /// The one out of the pool that does not get burned.
        ///
        /// The job car is drawn at random, so which ending a job has is decided by what turned
        /// up on the kerb rather than by the mission -- which is the right way round. A beater
        /// gets left somewhere; a clean saloon goes to Hao, gets new numbers, and gets sold.
        /// Lamar reads the plate at the start and tells you which it is, so it is never a
        /// surprise at the end.
        /// </summary>
        private const string KeeperCar = "vorschlaghammer";

        /// <summary>Hao's bay, off the lot. Where a car goes to stop being what it was.</summary>
        private static readonly Vector3 HaoBay = new Vector3(-22.387f, -1677.849f, 28.833f);

        /// <summary>Close enough to the bay to count as delivered.</summary>
        private const float BayRange = 9f;

        /// <summary>
        /// How far out the ring on the bay starts drawing.
        ///
        /// The same reach the port run gives its own bay, because this is the same kind of
        /// arrival: you come at it down a road at speed, and a mark that only appears once you
        /// are on top of it is a mark you have already driven past.
        /// </summary>
        private const float BayMarkerRange = 120f;

        /// <summary>Whether THIS job's car is one Hao would want.</summary>
        private bool _keeper;

        /// <summary>
        /// Whether this job is still going to end at Hao's rather than at Lamar's.
        ///
        /// The escape asks this before it forks and every line that names the destination has
        /// to ask the same question, or the card tells you to go and see Lamar while the blip
        /// points at the bay. It is more than _keeper on its own because a keeper folded round
        /// a lamppost on the way out is not a delivery any more.
        /// </summary>
        private bool GoingToHao =>
            _keeper && _jobCar != null && _jobCar.Exists() && _jobCar.IsDriveable;

        private readonly PlayerState _state;
        private readonly Affiliation _crew;
        private readonly GangRegistry _gangs;
        private readonly ZoneMap _zones;
        private readonly Random _rng = new Random();

        private readonly List<Ped> _homies = new List<Ped>();
        private readonly List<Ped> _targets = new List<Ped>();

        /// <summary>
        /// Every blip the job created.
        ///
        /// A blip attached to a ped is not cleaned up by letting go of the ped: it survives, and
        /// the minimap keeps showing homies who are no longer anything to do with you.
        /// </summary>
        private readonly List<Blip> _blips = new List<Blip>();

        private MissionDef _def;
        private Vector3 _site;
        private Blip _siteBlip;
        private Vehicle _jobCar;
        private int _lastUpdate;
        private int _homiesLost;

        /// <summary>
        /// The one job that is scripted rather than assembled.
        ///
        /// Kept as its own thing rather than folded in here: the bike ride has six legs, a
        /// conversation, a shop and a rule about weapons, and threading all of that through a
        /// runner built for "drive there, deal with them, come back" would leave both harder to
        /// follow than either is on its own.
        /// </summary>
        /// <summary>
        /// The job list, so the runner can notice when a new one opens up.
        ///
        /// Set by Main rather than taken in the constructor: FixerTalk owns the book and reads
        /// it when you walk up to him, and this only needs to LOOK at it on a slow tick to see
        /// whether there is anything worth a text.
        /// </summary>
        public MissionBook Book;

        /// <summary>
        /// Whether the shop on the bike ride ended with somebody on the floor.
        ///
        /// Asked by the hand-in, which is a different screen in a different file at the other
        /// end of the job -- so it is exposed here rather than reached for, and it is false for
        /// every job that is not that one.
        /// </summary>
        public bool ClerkKilled => _bike != null && _bike.KilledTheClerk;

        private readonly BikeRide _bike;

        /// <summary>Set by Main. Null-checked everywhere, so the feed is never load-bearing.</summary>
        public SocialFeed Social
        {
            get { return _social; }
            set { _social = value; _tags.Social = value; _bike.Social = value; }
        }

        private SocialFeed _social;


        /// <summary>
        /// The tag run, and the walls it draws from.
        ///
        /// The list is loaded once and kept, so a run picks a different handful each time
        /// rather than sending you round the same walls in the same order.
        /// </summary>
        private readonly TagRun _tags;
        private readonly List<TagSpot> _walls;

        public MissionRunner(PlayerState state, Affiliation crew, GangRegistry gangs, ZoneMap zones)
        {
            _state = state;
            _crew = crew;
            _gangs = gangs;
            _zones = zones;
            _bike = new BikeRide(crew, gangs);
            _tags = new TagRun(gangs) { Crew = crew };
            _walls = TagRun.Load();
        }

        /// <summary>Set by Main, so the bike ride can borrow Lamar for the ride out.</summary>
        public Fixer Boss
        {
            set { _bike.Boss = value; }
        }

        /// <summary>Set by Main and handed straight to the bike job for its courtyard exchange.</summary>
        public Conversation Talk
        {
            get { return _bike.Talk; }
            set { _bike.Talk = value; }
        }

        public MissionState State { get; private set; } = MissionState.None;

        public bool IsRunning => State != MissionState.None || _bike.IsRunning || _tags.IsRunning;

        private bool OnBike => _bike.IsRunning;

        private bool OnTags => _tags.IsRunning;

        public MissionDef Current => _def;

        /// <summary>What the player is meant to be doing, in one line.</summary>
        public string Objective
        {
            get
            {
                if (OnBike) return _bike.Objective;
                if (OnTags) return _tags.Objective;

                switch (State)
                {
                    case MissionState.Travel:
                        return _def.Kind == MissionKind.DriveBy || _def.Kind == MissionKind.TorchJob
                            ? "Get a car and roll out to " + ZoneName()
                            : "Get to " + ZoneName();

                    case MissionState.Work:
                        if (_def.Kind == MissionKind.TorchJob) return "Let 'em know you're there";
                        if (_def.Kind == MissionKind.DriveBy) return "Shoot up the corner -- stay in the car";
                        return Fists(_def.Kind) ? "Put hands on them" : "Put 'em down";

                    case MissionState.Escape:
                        if (_def.Kind == MissionKind.TorchJob && !_burned) return "Lose the cops";

                        return GoingToHao
                            ? "Lose the cops, then run the car to Hao"
                            : "Lose the cops, then get back to Lamar";

                    case MissionState.Deliver:
                        return "Take the car to Hao's bay";

                    case MissionState.Dump:
                        return "Dump the car somewhere quiet";

                    case MissionState.Torch:
                        return _poured
                            ? "Back up and shoot the fuel"
                            : "Pour it over the car";

                    case MissionState.Collect:
                        return "Get back to Lamar for the money";

                    default:
                        return "";
                }
            }
        }

        /// <summary>True for the jobs that are hands only, on both sides.</summary>
        private static bool Fists(MissionKind kind)
        {
            return kind == MissionKind.RideOut || kind == MissionKind.BikeRide;
        }

        private string ZoneName()
        {
            var zone = _zones.Get(_def.Zone);
            return zone == null || string.IsNullOrEmpty(zone.Name) ? _def.Zone : zone.Name;
        }

        // ---- starting ----------------------------------------------------------

        /// <summary>Returns a player-facing refusal, or null once the job is on.</summary>
        public string Start(MissionDef def)
        {
            if (def == null) return "No such job.";
            if (IsRunning) return "You're already on something.";
            try
            {
                if (!def.OpenNow(Function.Call<int>(Hash.GET_CLOCK_HOURS)))
                {
                    return "that place is shut. Come back between " +
                           MissionDef.Clock(def.OpensHour) + " and " +
                           MissionDef.Clock(def.ClosesHour) + ".";
                }
            }
            catch
            {
                // No clock, no restriction.
            }

            if (!_crew.IsAffiliated) return "You don't run with nobody.";

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return "Not right now.";

            var site = Math.Abs(def.X) > 0.01f || Math.Abs(def.Y) > 0.01f
                ? new Vector3(def.X, def.Y, def.Z)
                : _zones.GroundedCentre(def.Zone);

            if (site == Vector3.Zero) return "Nobody could tell you where that's at.";

            if (def.Kind == MissionKind.Tags)
            {
                var refused = _tags.Start(def, _walls);
                if (refused != null) return refused;

                _def = def;
                _homiesLost = 0;

                Notify.Important("~g~Job on.~s~ " + _tags.Objective + ".");
                Log.Info("Mission " + def.Id + " started as a tag run.");
                return null;
            }

            if (def.Kind == MissionKind.BikeRide)
            {
                var no = _bike.Start(def);
                if (no != null) return no;

                _def = def;
                _homiesLost = 0;

                Log.Info("Mission " + def.Id + " started as a bike ride.");
                return null;
            }

            _def = def;
            _site = site;
            _homiesLost = 0;
            State = MissionState.Travel;

            MarkSite();
            SpawnHomies(player, def);

            // Any job that names a car gets one. Keying this on the DriveBy kind meant the
            // Rancho and Grove jobs had car coordinates in their data and no car in the street.
            if (Math.Abs(def.CarX) > 0.01f || Math.Abs(def.CarY) > 0.01f) SpawnJobCar(def);

            // A torch job ends with shooting a trail of petrol, so it cannot be started by
            // somebody with empty hands.
            if (def.Kind == MissionKind.TorchJob) MakeSureHesCarrying(Game.Player.Character);

            Notify.Important("~g~Job on.~s~ " + Objective + ".");
            Log.Info("Mission " + def.Id + " started, site " + _site + ".");

            if (Social != null) Social.On(SocialEvent.MissionTaken, def.Name);
            return null;
        }

        private void MarkSite()
        {
            try
            {
                _siteBlip = World.CreateBlip(_site, ArriveRange);
                if (_siteBlip == null || !_siteBlip.Exists()) return;

                _siteBlip.Color = BlipColor.Yellow;
                _siteBlip.Alpha = 90;
                _siteBlip.ShowRoute = true;
                _siteBlip.Name = _def.Name;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not mark the job: " + ex.Message);
            }
        }

        /// <summary>
        /// Your people, waiting on you.
        ///
        /// Put in a group with the player so the game's own follow logic drives them: they get
        /// in cars with you, they keep up, and they fight what you fight, without a script
        /// nannying them every frame.
        /// </summary>
        /// <summary>
        /// Where the homies wait for a job that has no car, read off the HUD stood on it.
        ///
        /// The middle of the lot, where everybody else already is. A job on foot used to put
        /// them at the player's elbow, which is three men appearing inside whatever you were
        /// looking at.
        /// </summary>
        private static readonly Vector3 FootMuster = new Vector3(-196.639f, -1727.089f, 32.664f);

        private void SpawnHomies(Ped player, MissionDef def)
        {
            var gang = _crew.Current;
            if (gang == null || def.Homies <= 0) return;

            var group = Function.Call<int>(Hash.GET_PED_GROUP_INDEX, player.Handle);

            // Tight formation, close spacing. The default has group members trailing far enough
            // back that they arrive at a fight after it has finished -- and on the drive out it
            // reads as three men who did not come with you.
            try
            {
                Function.Call(Hash.SET_GROUP_FORMATION, group, 1);
                Function.Call(Hash.SET_GROUP_FORMATION_SPACING, group, 2.5f, 1.5f, 4f);
                Function.Call(Hash.SET_GROUP_SEPARATION_RANGE, group, 250f);
            }
            catch
            {
                // The default formation still follows.
            }

            // A drive-by crew waits at the car, not at your elbow. The walk round to where
            // the car is parked is the start of the job.
            // At the car if the job has one, and on their own corner of the lot if it does
            // not. Falling back to the PLAYER'S position was the old answer and it put three
            // men through whatever you happened to be standing in front of.
            var muster = def.CarX != 0f || def.CarY != 0f
                ? Ground(new Vector3(def.CarX, def.CarY, def.CarZ))
                : Ground(FootMuster);

            if (muster == Vector3.Zero) muster = player.Position;

            for (var i = 0; i < def.Homies; i++)
            {
                var ped = SpawnGangMember(gang, muster.Around(3f + i));
                if (ped == null) continue;

                _homies.Add(ped);

                try
                {
                    Function.Call(Hash.SET_PED_AS_GROUP_MEMBER, ped.Handle, group);
                    Function.Call(Hash.SET_PED_NEVER_LEAVES_GROUP, ped.Handle, true);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 46, true);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 5, true);
                    // 46 is BF_CanFightArmedPedsWhenNotArmed, NOT BF_AlwaysFight. That is 5.
                    Function.Call(Hash.SET_PED_ACCURACY, ped.Handle, 30);
                    Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle, gang.GroupHash);

                    // A ride-out is hands, so they only draw when the job says so.
                    if (!Fists(def.Kind))
                    {
                        var list = def.Kind == MissionKind.Hit ? HitWeapons : HomieWeapons;
                        var weapon = list[_rng.Next(list.Length)];

                        var hash = Function.Call<uint>(Hash.GET_HASH_KEY, weapon);

                        Function.Call(Hash.GIVE_WEAPON_TO_PED, ped.Handle, hash, 250, false, true);
                        Function.Call(Hash.SET_CURRENT_PED_WEAPON, ped.Handle, hash, true);
                    }

                    var blip = ped.AddBlip();
                    if (blip != null && blip.Exists())
                    {
                        blip.Color = BlipColor.Green;
                        blip.Scale = 0.6f;
                        blip.Name = "Homie";
                        _blips.Add(blip);
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not set up a homie: " + ex.Message);
                }
            }

            if (_homies.Count > 0) Notify.Ticker("~g~" + _homies.Count + " of the homies rolled out with you.~s~");
        }

        /// <summary>
        /// Leaves the car and the people who ride in it at the spot the job names.
        ///
        /// Stock, with one change: competition suspension, so it sits where it should. Anything
        /// more would be somebody else deciding what your car looks like.
        /// </summary>
        /// <summary>
        /// Makes sure he is carrying something, for a job that needs it.
        ///
        /// Only if he has nothing -- a man who turned up with a carbine keeps his carbine. This
        /// is so that a job which ends with "put a round in it" cannot be started by somebody
        /// who has nothing to put a round in it with, which would be a mission you could
        /// walk into and not be able to finish.
        /// </summary>
        private static void MakeSureHesCarrying(Ped player)
        {
            try
            {
                if (player == null || !player.Exists()) return;

                var pistol = Function.Call<uint>(Hash.GET_HASH_KEY, "WEAPON_PISTOL");

                var has = Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON, player.Handle, pistol, false);
                if (has) return;

                // Nothing at all, or nothing but fists. Either way he gets a pistol and some
                // rounds for it, and it is his to keep.
                Function.Call(Hash.GIVE_WEAPON_TO_PED, player.Handle, pistol, 120, false, false);

                Log.Info("Handed over a pistol for the job.");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not hand over a sidearm: " + ex.Message);
            }
        }

        private void SpawnJobCar(MissionDef def)
        {
            var where = Ground(new Vector3(def.CarX, def.CarY, def.CarZ));
            if (where == Vector3.Zero) return;

            // A job that names a car gets that car, and the pool is the fallback. On a job
            // that ends with the thing on fire it matters that it looked disposable from the
            // moment you got in -- a clean car nobody minds burning is a different story.
            var choices = new List<string>();
            if (!string.IsNullOrEmpty(def.CarModel)) choices.Add(def.CarModel);
            choices.AddRange(DriveByCars);

            foreach (var name in choices)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) continue;

                    _jobCar = World.CreateVehicle(model, where, def.CarHeading);
                    model.MarkAsNoLongerNeeded();

                    if (_jobCar == null || !_jobCar.Exists()) continue;

                    // Decided here, at the kerb, so everything downstream -- Lamar's brief, the
                    // ending, the blip -- agrees about which job this is.
                    _keeper = string.Equals(name, KeeperCar, StringComparison.OrdinalIgnoreCase)
                              && _def.Kind != MissionKind.TorchJob;

                    _jobCar.IsPersistent = true;
                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, _jobCar.Handle, true, true);

                    // Stock everywhere else. SET_VEHICLE_MOD_KIT must be called before any mod
                    // will take, and 15 is the suspension slot; 3 is competition.
                    Function.Call(Hash.SET_VEHICLE_MOD_KIT, _jobCar.Handle, 0);
                    Function.Call(Hash.SET_VEHICLE_MOD, _jobCar.Handle, 15, 3, false);

                    var blip = _jobCar.AddBlip();
                    if (blip != null && blip.Exists())
                    {
                        blip.Color = BlipColor.Green;
                        blip.Scale = 0.8f;
                        blip.Name = "The car";
                        _blips.Add(blip);
                    }

                    Log.Info("Mission " + def.Id + ": car left at " + where + " as " + name + ".");
                    return;
                }
                catch
                {
                    // Try the next model.
                }
            }

            Log.Warn("No drive-by car model would load for " + def.Id + ".");
        }

        /// <summary>
        /// Settles onto the ground, but only when the ground agrees with the authored height.
        ///
        /// Authored spots are read off the HUD while stood on them, so they are already right.
        /// A probe from high above a narrow alley finds a balcony, which is how things ended up
        /// on roofs.
        /// </summary>
        private static Vector3 Ground(Vector3 where)
        {
            if (Math.Abs(where.X) < 0.01f && Math.Abs(where.Y) < 0.01f) return Vector3.Zero;

            try
            {
                if (World.GetGroundHeight(new Vector3(where.X, where.Y, where.Z + 1.5f),
                                          out var groundZ, GetGroundHeightMode.Normal) &&
                    groundZ > 0f && Math.Abs(groundZ - where.Z) <= 3f)
                {
                    where.Z = groundZ;
                }
            }
            catch
            {
                // Keep the authored height.
            }

            return where;
        }

        private Ped SpawnGangMember(GangDef gang, Vector3 near)
        {
            foreach (var name in gang.MemberModels)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1200)) continue;

                    var spot = World.GetNextPositionOnSidewalk(near);
                    if (spot == Vector3.Zero) spot = near;

                    // Pavement lookups return a position, not a height, and a ped created above
                    // the ground falls to it -- which is what the drop from the sky was.
                    try
                    {
                        if (World.GetGroundHeight(new Vector3(spot.X, spot.Y, spot.Z + 15f),
                                                  out var groundZ, GetGroundHeightMode.Normal) && groundZ > 0f)
                        {
                            spot.Z = groundZ;
                        }
                    }
                    catch
                    {
                        // Keep what the pavement gave us.
                    }

                    var ped = World.CreatePed(model, spot);
                    model.MarkAsNoLongerNeeded();

                    if (ped == null || !ped.Exists()) continue;

                    ped.IsPersistent = true;
                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, ped.Handle, true, true);

                    return ped;
                }
                catch
                {
                    // Try the next model.
                }
            }

            return null;
        }

        // ---- per-tick ----------------------------------------------------------

        /// <summary>
        /// Lamar texts when there is something new, once each.
        ///
        /// Work unlocks on rank and on what you have finished, and nothing announced either --
        /// so a job could sit on his menu for an hour with no reason to go and look. He is a
        /// contact with your number; him saying so is both the obvious fix and the one that
        /// costs nothing to believe.
        ///
        /// Once per job, tracked by id in the save, because a nag every time you rank up is
        /// worse than never being told.
        /// </summary>
        /// <summary>
        /// How long he has left to himself, or 0.
        ///
        /// Session time rather than saved. Reloading a save clears it, which is the kinder
        /// answer of the two: a countdown you cannot see, that survives a reload, and that
        /// stops a man talking to you is a bug report waiting to be written.
        /// </summary>
        private int _restUntil;

        /// <summary>Whether he is off scheming rather than available.</summary>
        public bool Resting => !IsRunning && _restUntil != 0 && Game.GameTime < _restUntil;

        /// <summary>Whole minutes left of that, rounded up, never less than one.</summary>
        public int RestLeft =>
            !Resting ? 0 : Math.Max(1, (int)Math.Ceiling((_restUntil - Game.GameTime) / 60000.0));

        /// <summary>How long he wants, as the settings have it. Wired by the house script.</summary>
        public Func<float> RestMinutes;

        /// <summary>
        /// What he sends when he has had his think.
        ///
        /// One of several, because the same sentence arriving after every job is a reminder
        /// that a timer just went off. These are him having had an idea, which is what the gap
        /// was for.
        /// </summary>
        private static readonly string[] BackOnLines =
        {
            "aye cuz. i been thinkin. come see me, i got somethin",
            "ok ok ok. new idea. come thru when you can",
            "yo. that thing i was workin out? worked it out. come find me",
            "franklin. FRANKLIN. come see me man, this one different",
            "aight i'm done schemin. come get this"
        };

        /// <summary>And what he says the moment you have been paid for the last one.</summary>
        private static readonly string[] RestLines =
        {
            "good look on that. gimme a minute to scheme on the next one, i'll hit you",
            "that's that. lemme think on somethin, i'll text you when i got it",
            "aight, breathe. i gotta work out the next play. i'll let you know",
            "we good. gimme a minute cuz, i'm cookin somethin up. i'll hit your line"
        };

        private void TellHimIfThereIsWork()
        {
            if (_state == null || Book == null || IsRunning) return;

            // Not before you are one of theirs.
            //
            // The opening is supposed to be one man and one icon: Gerald texts, you go and see
            // him, you move his package, you get asked in. Lamar texting a stranger "got
            // somethin for you, cuz" in the same minute undoes all of it -- and he would not.
            // He does not know you yet.
            if (_crew == null || !_crew.IsAffiliated) return;

            // The rest ends here rather than in a tick of its own, because this is already the
            // one thing in the file that runs whether or not a job is on.
            if (_restUntil != 0 && Game.GameTime >= _restUntil)
            {
                _restUntil = 0;

                try
                {
                    Notify.Text("CHAR_LAMAR", "Lamar", "Los Santos",
                                BackOnLines[_rng.Next(BackOnLines.Length)], true);
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not text about being back on: " + ex.Message);
                }

                Log.Info("Lamar is done scheming.");
            }

            if (Resting) return;
            if (Game.GameTime < _nextWorkCheck) return;

            _nextWorkCheck = Game.GameTime + WorkCheckMs;

            var def = NextInTheWindow(true);
            if (def != null)
            {
                _state.MarkOffered(def.Id);
                _state.Touch();

                Notify.Text("CHAR_LAMAR", "Lamar", "Los Santos",
                            "aye. got somethin for you. come find me when you ready, cuz",
                            true);

                Log.Info("Lamar texted about " + def.Id + ".");
            }
        }

        /// <summary>
        /// The next job he would offer you, or null.
        ///
        /// The same window FixerTalk offers from: everything up to one past the last one
        /// finished, and nothing at all for a few minutes after you have just done one.
        /// Written once and asked twice -- the text he sends wants only jobs he has not
        /// mentioned yet, and the contacts page wants to know whether there is anything at
        /// all, including the one he already texted about.
        /// </summary>
        /// <param name="unmentionedOnly">Skip anything he has already texted about.</param>
        public MissionDef NextInTheWindow(bool unmentionedOnly)
        {
            if (_state == null || Book == null) return null;

            // He texts you when he has work. He does not text you thirty seconds after you
            // handed him the keys back, and he has nothing at all for somebody who is not in
            // the set yet -- which is what the contacts page reads to decide whether to show
            // him as having work.
            if (_crew == null || !_crew.IsAffiliated) return null;
            if (_state.JobsAreCooling) return null;

            var reached = -1;

            for (var i = 0; i < Book.All.Count; i++)
            {
                if (_state.HasDone(Book.All[i].Id)) reached = i;
            }

            for (var i = 0; i <= reached + 1 && i < Book.All.Count; i++)
            {
                var def = Book.All[i];

                if (_state.HasDone(def.Id)) continue;
                if (unmentionedOnly && _state.HasBeenOffered(def.Id)) continue;

                return def;
            }

            return null;
        }

        /// <summary>What he has for you, whether or not he has said so yet.</summary>
        public MissionDef WorkWaiting => IsRunning ? null : NextInTheWindow(false);

        private int _nextWorkCheck;

        /// <summary>Progress does not change fast. Every ten seconds is plenty.</summary>
        private const int WorkCheckMs = 10000;

        public void Update()
        {
            TellHimIfThereIsWork();

            // Above everything, and outside IsRunning: paint on a wall is the one thing here
            // that outlives the job that put it there, so it cannot hang off whether a job is
            // running. It throttles itself and does nothing at all until something is painted.
            _tags.Refresh();

            // And so does a car dropped at Hao's, for the same reason and on the same terms.
            Swept();

            // Going down, or being taken in, ends whatever was running -- and it has to be
            // taken again from the start.
            //
            // This was three different answers to one question. The ordinary jobs checked,
            // behind a half-second throttle, and failed properly. The bike ride checked inside
            // its own Update. The tag run did not check at all: you could be shot off a wall,
            // come round in Pillbox, and the run would still be live with every spot still
            // blipped and the paint still counting.
            //
            // One check, above the dispatch, so there is one answer for all three. Not
            // throttled, deliberately -- the player is only dead for the couple of seconds
            // before the game puts him outside a hospital, and a check that runs twice a
            // second can miss that window and conclude he was fine all along.
            if (IsRunning)
            {
                var who = Game.Player.Character;

                if (who == null || !who.Exists() || !who.IsAlive || Game.Player.IsDead)
                {
                    Fail("You went down out there.");
                    return;
                }

                if (Function.Call<bool>(Hash.IS_PLAYER_BEING_ARRESTED, Game.Player.Handle, false))
                {
                    Fail("They took you in.");
                    return;
                }
            }

            if (OnBike)
            {
                _bike.Update();

                var wentWrong = _bike.Failure;
                if (!string.IsNullOrEmpty(wentWrong)) Fail(wentWrong);

                return;
            }

            if (OnTags)
            {
                _tags.Update();
                return;
            }

            if (!IsRunning) return;

            var now = Game.GameTime;
            if (now - _lastUpdate < UpdateIntervalMs) return;
            _lastUpdate = now;

            var player = Game.Player.Character;

            // The death and arrest checks that used to sit here are at the top of Update now,
            // where the bike ride and the tag run get them too.
            CountLostHomies();
            LockThemIn();
            FollowMeOut();

            switch (State)
            {
                case MissionState.Travel:
                    // Talk on the way out. Rarely, and about nothing -- three men in a car on a
                    // ten-minute drive who say nothing at all are three props being delivered.
                    Chat(Riding, RidingWords, ChatRideMs);

                    // Put them in place well before you can see them. Spawning at the arrival
                    // radius is what had them appearing in mid-air and falling in as you pulled
                    // up: a ped created on unstreamed ground has nothing to stand on yet.
                    if (_targets.Count == 0 && player.Position.DistanceTo(_site) <= PreSpawnRange)
                    {
                        SpawnTargets(player);
                    }

                    if (player.Position.DistanceTo(_site) <= ArriveRange) BeginWork(player);
                    return;

                case MissionState.Work:
                    TickWork(player);
                    return;

                case MissionState.Escape:
                {
                    var shot = AnyoneHit();

                    Chat(shot ? TakingIt : Riding,
                         shot ? TakingItWords : RidingWords, ChatRideMs / 2);
                }

                    TickEscape();
                    return;

                case MissionState.Deliver:
                    TickDeliver(player);
                    return;

                case MissionState.Dump:
                    TickDump(player);
                    return;

                case MissionState.Torch:
                    TickTorch(player);
                    return;
            }
        }

        /// <summary>
        /// Puts them on the corner before you can see it.
        ///
        /// They stand about doing nothing until you actually turn up -- a corner full of men
        /// already swinging at thin air before you arrive is worse than one that is empty.
        /// </summary>
        private void SpawnTargets(Ped player)
        {
            var gang = _gangs.Get(_def.TargetGang);
            if (gang == null) return;

            for (var i = 0; i < _def.Targets; i++)
            {
                var ped = SpawnGangMember(gang, _site.Around(TargetSpread));
                if (ped == null) continue;

                _targets.Add(ped);

                try
                {
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, true);
                    Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle, gang.GroupHash);
                    Function.Call(Hash.SET_PED_ACCURACY, ped.Handle, 20);

                    // A ride-out is a beating on both sides; the rest are not.
                    if (!Fists(_def.Kind))
                    {
                        Function.Call(Hash.GIVE_WEAPON_TO_PED, ped.Handle,
                                      Function.Call<uint>(Hash.GET_HASH_KEY, "WEAPON_PISTOL"), 150, false, true);
                    }

                    // Standing about doing something rather than standing about doing nothing.
                    // Which idle they get is per-man, so five of them on a forecourt look like
                    // five people and not one man copied five times.
                    Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, ped.Handle,
                                  IdleScenarios[_rng.Next(IdleScenarios.Length)], 0, true);

                    var blip = ped.AddBlip();
                    if (blip != null && blip.Exists())
                    {
                        blip.Color = BlipColor.Red;
                        blip.Scale = 0.7f;
                        blip.Name = gang.Name;

                        _blips.Add(blip);
                        _targetBlips[ped.Handle] = blip;
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not set up a target: " + ex.Message);
                }
            }

            Log.Info("Mission " + _def.Id + ": " + _targets.Count + " waiting at " + _site + ".");
        }

        /// <summary>
        /// Makes the two sets hate each other for the length of the job.
        ///
        /// Without this every combat order given to the homies is an order to fight nobody:
        /// ambient gang groups are indifferent to each other by default, so a yard full of Vagos
        /// is not, as far as the game is concerned, a yard full of enemies. Read back first and
        /// put back in Clear, so a job cannot leave two sets permanently at war.
        /// </summary>
        private void SetFeud(bool on)
        {
            var mine = _crew.Current;
            var theirs = _def == null ? null : _gangs.Get(_def.TargetGang);

            if (mine == null || theirs == null) return;
            if (mine.GroupHash == 0 || theirs.GroupHash == 0) return;

            try
            {
                if (on)
                {
                    _wasThem = Function.Call<int>(Hash.GET_RELATIONSHIP_BETWEEN_GROUPS,
                                                  theirs.GroupHash, mine.GroupHash);
                    _wasUs = Function.Call<int>(Hash.GET_RELATIONSHIP_BETWEEN_GROUPS,
                                                mine.GroupHash, theirs.GroupHash);

                    Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, 5, theirs.GroupHash, mine.GroupHash);
                    Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, 5, mine.GroupHash, theirs.GroupHash);

                    _feuding = true;
                    return;
                }

                Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, _wasThem, theirs.GroupHash, mine.GroupHash);
                Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, _wasUs, mine.GroupHash, theirs.GroupHash);

                _feuding = false;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not set the job's relationships: " + ex.Message);
            }
        }

        /// <summary>
        /// When you get out, they get out.
        ///
        /// The doors are unlocked the moment you are on your feet, so nothing is holding them
        /// in any more -- but a man sitting in a car with no reason to move will sit in it, and
        /// on the torch job that means three of them still aboard the thing you are pouring
        /// petrol over. So they are told plainly to climb out after you rather than left to
        /// work it out.
        /// </summary>
        private void FollowMeOut()
        {
            if (!IsRunning) return;

            if (Game.GameTime < _nextFollowOut) return;
            _nextFollowOut = Game.GameTime + FollowOutMs;

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return;

            // Only once you are actually out and standing on your own feet.
            if (player.IsInVehicle()) return;

            foreach (var homie in _homies)
            {
                if (homie == null || !homie.Exists() || !homie.IsAlive) continue;
                if (!homie.IsInVehicle()) continue;

                try
                {
                    var ride = homie.CurrentVehicle;

                    Function.Call(Hash.TASK_LEAVE_VEHICLE, homie.Handle,
                                  ride == null || !ride.Exists() ? 0 : ride.Handle, 0);
                }
                catch { /* he will follow on the next pass */ }
            }
        }

        private int _nextFollowOut;
        private const int FollowOutMs = 900;

        /// <summary>
        /// Locks the car three seconds after you take the wheel, and that is the whole of it.
        ///
        /// What stood here was three rounds of machinery for keeping the crew in their seats
        /// and shooting out of the windows: blocked events, no dragging out, combat attributes
        /// taken off them, two ragdoll flags, a warp back into the seat every frame and a
        /// drive-by re-issued twice a second. It never worked once. They did not fire, and they
        /// spent the job hanging out of a rear window in a bind pose. So none of it is here any
        /// more. They are ordinary bodyguards, and the game has always known how to run those
        /// -- including having them shoot out of a car somebody else is driving.
        ///
        /// Staying in the car is the car's business now instead of theirs. A locked door is a
        /// property of the VEHICLE, so keeping them aboard costs nothing set on the men at all.
        ///
        /// Only while YOU are at the wheel, and only with somebody of ours actually in it.
        /// Locking a car you are a passenger in is locking somebody else's doors, and locking
        /// an empty one is locking a door for nobody.
        /// </summary>
        /// <summary>
        /// Whether any of the crew who are alive are outside this car.
        ///
        /// Dead men do not count. Somebody who went down in the street is not somebody waiting
        /// to get back in, and counting him would hold the doors open for the rest of the job.
        /// </summary>
        private bool AnyoneAfoot(Vehicle ride)
        {
            if (ride == null || !ride.Exists()) return false;

            foreach (var homie in _homies)
            {
                if (homie == null || !homie.Exists() || !homie.IsAlive) continue;
                if (!homie.IsInVehicle(ride)) return true;
            }

            return false;
        }

        private void LockThemIn()
        {
            var player = Game.Player.Character;
            var ride = player == null || !player.Exists() ? null : player.CurrentVehicle;

            // Out of it, off in a different one, or it is not there any more. "Unlock the door
            // when i exit the vehicle" is the ask, and handing it back is also what stops a
            // locked car being left standing in the world with nobody who knows it was us.
            //
            // The wreck case matters as much as the other two: a car that has stopped existing
            // is still a reference we are holding, and holding it means the next car you get
            // into is read as the one already locked and never gets locked at all.
            if (_lockedRide != null &&
                (!_lockedRide.Exists() || ride == null || !ride.Exists() ||
                 _lockedRide.Handle != ride.Handle))
            {
                Unlock();
            }

            if (ride == null || !ride.Exists())
            {
                _drivingSince = 0;
                return;
            }

            // ANYBODY STOOD OUTSIDE IT UNLOCKS IT, and this is not a nicety.
            //
            // Lock state 2 stops a ped getting IN as well as getting out. So the moment the
            // work started and the crew piled out to fight, the car they had just been riding
            // in was sealed against them -- three men stood in the road next to a locked car,
            // unable to get back in, for the rest of the job. The lock is there to stop them
            // bailing out at a red light on the way to somewhere; once they are out on their
            // feet it has nothing left to do and is only in the way.
            //
            // The clock resets with it, so when they are all back aboard the ten seconds start
            // again and it locks itself.
            if (AnyoneAfoot(ride))
            {
                Unlock();
                _drivingSince = 0;
                return;
            }

            // Already locked. Set once and then left alone -- re-asserting it every tick is how
            // the last version of this ended up arguing with your own door.
            if (_lockedRide != null) return;

            if (player.SeatIndex != VehicleSeat.Driver || !AnyoneAboard(ride))
            {
                _drivingSince = 0;
                return;
            }

            // The clock starts when you sit down at the wheel, and starts again from nothing
            // every time you get out and back in.
            if (_drivingSince == 0)
            {
                _drivingSince = Game.GameTime;
                return;
            }

            if (Game.GameTime - _drivingSince < LockAfterMs) return;

            try
            {
                // 2 is LOCKED, which stops a ped opening a door from either side. It is not the
                // state that shuts the player in -- that is 4, and the fact that it exists
                // separately is the proof that 2 does not do it. The first call still covers
                // him, though, so the second takes him straight back out: a torch job ends with
                // you stood next to the car you are burning, and being sealed into it would not
                // be a small bug.
                Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, ride.Handle, 2);
                Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED_FOR_PLAYER, ride.Handle,
                              Game.Player.Handle, false);

                _lockedRide = ride;
                Log.Debug("Doors locked on the ride, three seconds after pulling away.");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not lock the ride: " + ex.Message);
            }
        }

        /// <summary>Whether any of ours is actually riding in this one.</summary>
        private bool AnyoneAboard(Vehicle ride)
        {
            foreach (var homie in _homies)
            {
                if (homie == null || !homie.Exists() || !homie.IsAlive) continue;
                if (homie.IsInVehicle(ride)) return true;
            }

            return false;
        }

        /// <summary>When you took the wheel of the ride you are in, or 0 if you have not.</summary>
        private int _drivingSince;

        /// <summary>Three seconds after you start driving, which is the number he asked for.</summary>
        private const int LockAfterMs = 10000;

        /// <summary>
        /// Gives the car back the way it was found.
        ///
        /// A car left locked becomes somebody else's problem the moment the job ends -- traffic
        /// nobody can get into, or your own car refusing the next passenger.
        /// </summary>
        private void Unlock()
        {
            // Cleared whether or not there was a car to hand back, because this is also what
            // "get out and get back in" means: the three seconds start again from nothing.
            _drivingSince = 0;

            if (_lockedRide == null) return;

            try
            {
                if (_lockedRide.Exists())
                {
                    Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, _lockedRide.Handle, 1);
                    Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED_FOR_PLAYER, _lockedRide.Handle,
                                  Game.Player.Handle, false);
                }
            }
            catch { /* it is unlocked or it is gone */ }

            _lockedRide = null;
        }

        /// <summary>The one car we locked, so exactly one gets unlocked again.</summary>
        private Vehicle _lockedRide;

        /// <summary>
        /// Lets them out before you set fire to it.
        ///
        /// The doors come off the lock first, because a locked door is a door they cannot open
        /// either, and then they are told to get out. Waiting for three men to notice on their
        /// own that the car they are sitting in is about to be set alight is not a plan.
        /// </summary>
        private void ClearTheCar()
        {
            Unlock();

            foreach (var homie in _homies)
            {
                if (homie == null || !homie.Exists() || !homie.IsAlive) continue;

                try
                {
                    if (!homie.IsInVehicle()) continue;

                    Function.Call(Hash.TASK_LEAVE_ANY_VEHICLE, homie.Handle, 0, 0);
                }
                catch { /* he can climb out on his own */ }
            }
        }

        private bool _feuding;
        private int _wasThem = 4;
        private int _wasUs = 4;

        private void BeginWork(Ped player)
        {
            State = MissionState.Work;

            SetFeud(true);

            // First one lands a couple of seconds in, so it reads as somebody reacting rather
            // than somebody who was already typing.
            _nextWorkPost = Game.GameTime + 2500;

            // Late arrival: they were never placed, so place them now rather than fail.
            if (_targets.Count == 0) SpawnTargets(player);

            // A hit starts when YOU start it. They carry on with whatever they were doing until
            // somebody puts a round through it, which is the difference between walking up on
            // people and walking into an ambush that was waiting for you to arrive.
            var theyStartIt = _def.Kind != MissionKind.Hit;

            // Ours go for them on sight. They rode out here to do exactly this, and waiting for
            // the first round to be fired at them before they will look at anybody makes them
            // passengers on their own job.
            foreach (var homie in _homies)
            {
                if (homie == null || !homie.Exists() || !homie.IsAlive) continue;

                try
                {
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, homie.Handle, 46, true);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, homie.Handle, 5, true);

                    // 3 is BF_CanLeaveVehicle, and the answer is yes, on every kind of job.
                    //
                    // It used to be no for anything worked out of a car, and that turned out to
                    // be the bug rather than the fix: a bodyguard who is not allowed to leave a
                    // vehicle is a man handed a fight he cannot walk to. Keeping them aboard is
                    // the locked door's business now, and the door lets go when you get out.
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, homie.Handle, 3, true);

                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, homie.Handle, 2, true);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, homie.Handle, 1, true);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, homie.Handle, 0, true);

                    Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, homie.Handle, 2);

                    // And nothing else goes on them.
                    //
                    // Blocked non-temporary events, no dragging out and the two ragdoll flags
                    // were all set here, to hold a man in a seat and to keep a hit reaction off
                    // the drive-by that was fighting it for his body. Three goes at it and they
                    // still would not shoot and still ended up in a bind pose leaning out of a
                    // rear window. A ped left alone is a ped the game can animate.

                    // Named targets rather than "everybody hated within a hundred and twenty
                    // metres". The area order sweeps in whoever the game currently considers an
                    // enemy, and the moment you are wanted that includes the police -- so the
                    // homies would open up on a patrol car while you were trying to lose it.
                    // They came out here for these people.
                    var foe = NearestLiveTarget(homie);

                    if (foe != null) Shoot(homie, foe);
                }
                catch { /* the game's own AI takes it from here */ }
            }

            foreach (var ped in _targets)
            {
                if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

                try
                {
                    // Unblocked either way, so being shot at is something they can react to.
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);

                    if (theyStartIt) Function.Call(Hash.TASK_COMBAT_PED, ped.Handle, player.Handle, 0, 16);
                }
                catch { /* the game AI takes over */ }
            }

            if (_targets.Count == 0)
            {
                Fail("Wasn't nobody there.");
                return;
            }

            ClearSiteBlip();

            Notify.Important("~r~They're here.~s~ " + Objective + ".");
        }

        /// <summary>
        /// Whose marker is whose.
        ///
        /// SHVDN cannot get from a ped back to the blip on it, and a red dot left hovering over
        /// a body you dropped two minutes ago is the map telling you there is still somebody
        /// there to deal with.
        /// </summary>
        private readonly Dictionary<int, Blip> _targetBlips = new Dictionary<int, Blip>();

        /// <summary>Takes a target's marker off the map the moment he goes down.</summary>
        private void ClearDeadBlips()
        {
            foreach (var ped in _targets)
            {
                if (ped == null) continue;
                if (ped.Exists() && ped.IsAlive) continue;

                Blip blip;
                if (!_targetBlips.TryGetValue(ped.Handle, out blip)) continue;

                _targetBlips.Remove(ped.Handle);

                try
                {
                    if (blip != null && blip.Exists())
                    {
                        _blips.Remove(blip);
                        blip.Delete();
                    }
                }
                catch { /* it is coming off either way */ }
            }
        }

        /// <summary>
        /// The block talks about it while it is happening, not after.
        ///
        /// A job that is only reported once you have been paid is a results service. People
        /// hear a car going through a corner at the time, and half of them are on their phones
        /// before it has turned the next street -- so the feed runs during the work, keyed to
        /// what the work actually is, and the hand-in stops being the first anybody knew.
        /// </summary>
        private void TalkAboutIt()
        {
            if (Social == null || _def == null) return;
            if (Game.GameTime < _nextWorkPost) return;

            _nextWorkPost = Game.GameTime + WorkPostGapMs + _rng.Next(WorkPostGapMs);

            var gang = _gangs.Get(_def.TargetGang);
            var named = gang == null ? "" : gang.Name;

            switch (_def.Kind)
            {
                case MissionKind.TorchJob:
                    // Its own set. A torch job is a ride-through -- loud, quick, nobody hit --
                    // and the block describes it differently from a drive-by that came to
                    // leave somebody on the pavement.
                    Social.On(SocialEvent.RideThrough, named);
                    break;

                case MissionKind.DriveBy:
                    Social.On(SocialEvent.DriveBy, named);
                    break;

                case MissionKind.Hit:
                    Social.On(SocialEvent.RivalKilled, named);
                    break;

                default:
                    Social.On(SocialEvent.Brawl, named);
                    break;
            }
        }

        private int _nextWorkPost;

        /// <summary>Roughly this apart, doubled at random, while a job is being done.</summary>
        private const int WorkPostGapMs = 7000;

        // ---- what they say ------------------------------------------------------

        /// <summary>
        /// Going in. Names rather than recordings -- there is no audio of the written lines and
        /// there never will be, so a gang voice's own shouting plays under a subtitle of ours.
        /// Every one is wrapped and none is checked: a speech a particular voice has not got
        /// simply does not play, which costs a line rather than a crash.
        /// </summary>
        private static readonly string[] GoingIn =
        {
            "GENERIC_INSULT_HIGH", "CHALLENGE_THREATEN", "GENERIC_CURSE_HIGH",
            "PROVOKE_GENERIC", "GENERIC_WAR_CRY"
        };

        /// <summary>Taking it, which is a different noise from giving it.</summary>
        private static readonly string[] TakingIt =
        {
            "GENERIC_CURSE_HIGH", "GENERIC_SHOCKED_HIGH", "GENERIC_FRIGHTENED_MED",
            "GENERIC_CURSE_MED"
        };

        /// <summary>And the ordinary sort, for a long drive with nothing happening.</summary>
        private static readonly string[] Riding =
        {
            "CHAT_STATE", "GENERIC_HOWS_IT_GOING", "GENERIC_YES", "CHAT_RESP"
        };

        private static readonly string[] GoingInWords =
        {
            "Let 'em know we was here!",
            "Roll it down, roll it down!",
            "This they block? Not tonight it ain't.",
            "Hang out the window, cuz. Hang out.",
            "Everybody on that corner, everybody!",
            "Say somethin' now. Say somethin' NOW."
        };

        private static readonly string[] TakingItWords =
        {
            "They shootin' back! They shootin' back!",
            "Drive, boy, DRIVE.",
            "That one came through the door!",
            "Get us off this street.",
            "I'm hit -- nah, I'm good. Go.",
            "Somebody's aimin' proper. Move."
        };

        private static readonly string[] RidingWords =
        {
            "Long way round for a two-minute job.",
            "Turn that up.",
            "My cousin lives down here. Used to.",
            "You gon' fix them mirrors or is that the look.",
            "Whole block's watchin' us do thirty.",
            "Tell me again why it's me in the back.",
            "Aye. Don't be takin' the freeway.",
            "This the third time we come down this street."
        };

        private static readonly string[] BurningWords =
        {
            "Whole thing's goin' up!",
            "That's it. That's the car.",
            "Back up, back up, it's gon' blow.",
            "Nothin' left in there for nobody.",
            "Somebody's gon' see that from the freeway."
        };

        /// <summary>
        /// One of them says something, out loud and on screen, and then nobody does for a bit.
        ///
        /// Picked at random from whoever is alive rather than always the first man in the list,
        /// because three homies where the same one talks every time is one homie and two
        /// passengers.
        ///
        /// The gap is the whole design. Chatter is atmosphere and atmosphere that never stops
        /// is noise -- a line every few seconds during a firefight and every half a minute on
        /// a drive is somebody talking; anything faster is a radio play.
        /// </summary>
        private void Chat(string[] speech, string[] words, int gapMs)
        {
            if (Game.GameTime < _nextChat) return;

            var live = new List<Ped>();

            foreach (var homie in _homies)
            {
                if (homie != null && homie.Exists() && homie.IsAlive) live.Add(homie);
            }

            if (live.Count == 0) return;

            _nextChat = Game.GameTime + gapMs + _rng.Next(gapMs / 2);

            var who = live[_rng.Next(live.Count)];

            try
            {
                Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, who.Handle,
                              speech[_rng.Next(speech.Length)], "SPEECH_PARAMS_FORCE_SHOUTED");
            }
            catch { /* his voice has not got that one */ }

            if (words == null || words.Length == 0) return;

            try
            {
                GTA.UI.Screen.ShowSubtitle("~g~HOMIE:~s~ " + words[_rng.Next(words.Length)], 3000);
            }
            catch { /* the noise is the half that matters */ }
        }

        private int _nextChat;

        /// <summary>
        /// Whether anybody has put a round into one of ours since the last time this was asked.
        ///
        /// Asked of the game rather than tracked, and CLEARED after asking -- the flag stays set
        /// once it is set, so a homie shot in the first street would otherwise read as being
        /// shot for the rest of the job and they would spend the drive home shouting about it.
        /// </summary>
        private bool AnyoneHit()
        {
            foreach (var homie in _homies)
            {
                if (homie == null || !homie.Exists() || !homie.IsAlive) continue;

                try
                {
                    if (!Function.Call<bool>(Hash.HAS_ENTITY_BEEN_DAMAGED_BY_ANY_PED, homie.Handle))
                    {
                        continue;
                    }

                    Function.Call(Hash.CLEAR_ENTITY_LAST_DAMAGE_ENTITY, homie.Handle);
                    return true;
                }
                catch { /* he will mention it next time */ }
            }

            return false;
        }

        /// <summary>How long between lines, by what is going on.</summary>
        private const int ChatFightMs = 4000;
        private const int ChatRideMs = 26000;

        /// <summary>The closest of the people we actually came for, or null when they are down.</summary>
        private Ped NearestLiveTarget(Ped from)
        {
            Ped best = null;
            var bestDist = float.MaxValue;

            foreach (var ped in _targets)
            {
                if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

                var d = from.Position.DistanceTo(ped.Position);
                if (d >= bestDist) continue;

                best = ped;
                bestDist = d;
            }

            return best;
        }

        /// <summary>
        /// The shooting is over. Everybody stops.
        ///
        /// Without this they carry on fighting into the escape -- and by then the only hostiles
        /// left are police, so the crew you brought along turn a two-star drive home into a
        /// running battle you cannot leave. Taking the two "go and find somebody" attributes
        /// back off them is what makes it stick past the next siren.
        /// </summary>
        private void StandDown()
        {
            foreach (var homie in _homies)
            {
                if (homie == null || !homie.Exists() || !homie.IsAlive) continue;

                try
                {
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, homie.Handle, 46, false);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, homie.Handle, 5, false);

                    Function.Call(Hash.CLEAR_PED_TASKS, homie.Handle);

                    // Back to the car if you are in one, otherwise back to you. Standing where
                    // the fight was while you drive off is its own kind of wrong.
                    var player = Game.Player.Character;
                    var ride = player == null ? null : player.CurrentVehicle;

                    if (ride != null && ride.Exists())
                    {
                        Function.Call(Hash.TASK_ENTER_VEHICLE, homie.Handle, ride.Handle,
                                      12000, -2, 2f, 1, 0);
                    }
                    else if (player != null)
                    {
                        Function.Call(Hash.TASK_FOLLOW_TO_OFFSET_OF_ENTITY, homie.Handle,
                                      player.Handle, 1.5f, 0f, 0f, 2f, -1, 4f, true);
                    }
                }
                catch { /* he will find his own way home */ }
            }
        }

        /// <summary>
        /// Puts a homie on a target, from wherever he happens to be.
        ///
        /// Plain foot combat now, on every job. The drive-by half that used to be here is gone
        /// with the rest of it: three attempts at leaning them out of the windows never once
        /// produced a shot, and a bodyguard sitting in a car somebody else is driving already
        /// fires out of it on his own if you leave him alone.
        /// </summary>
        private void Shoot(Ped homie, Ped foe)
        {
            Function.Call(Hash.TASK_COMBAT_PED, homie.Handle, foe.Handle, 0, 16);
        }

        private void TickWork(Ped player)
        {
            ClearDeadBlips();
            TalkAboutIt();

            // Somebody is always saying something while it is going off. Being shot at wins
            // over shooting, because it is the more urgent of the two and the one you would
            // actually hear over the other.
            //
            // Asked ONCE into a local, and that is not tidiness. AnyoneHit clears the flag it
            // reads -- it has to, or a man shot in the first street reads as being shot for the
            // rest of the job -- so calling it twice in one expression answers true and then
            // false, and they shout about being hit while the subtitle says otherwise.
            var hit = AnyoneHit();

            Chat(hit ? TakingIt : GoingIn, hit ? TakingItWords : GoingInWords, ChatFightMs);

            var standing = 0;
            foreach (var ped in _targets)
            {
                if (ped != null && ped.Exists() && ped.IsAlive) standing++;
            }

            // A torch job is done the moment one of them notices you.
            //
            // It is a message, not a body count -- and grinding four kills from a car seat is
            // the least interesting version of every mission in this mod. Anybody dead counts
            // as noticed, obviously, for the case where the first thing they notice is that.
            // A drive-by ends the same way a torch job does: when they know you were there.
            //
            // It used to wait for every one of them to be down, which quietly made it a
            // clearing job done through a car window -- the slowest and least interesting
            // version of the mission. You are not there for a body count. Hitting somebody is
            // absolutely allowed and some of them will die; it is simply not the thing being
            // asked for, and the job should not sit there waiting for it.
            if (_def.Kind == MissionKind.TorchJob || _def.Kind == MissionKind.DriveBy)
            {
                if (standing == _targets.Count && !AnyoneNoticed(player)) return;

                Notify.Important("~r~They seen you.~s~ Now get gone.");

                StandDown();

                State = MissionState.Escape;
                Wanted(_def.HeatStars);
                return;
            }

            if (standing > 0) return;

            if (_def.EscapeHeat)
            {
                // The trip home is part of the job. Without this the drive back is a formality
                // you spend looking at a blip, and the only thing that ever went wrong happened
                // before you got in the car.
                StandDown();

                State = MissionState.Escape;

                Wanted(_def.HeatStars);

                Notify.Important(GoingToHao
                    ? "~r~Somebody called it in.~s~ Lose 'em, then run the car to Hao."
                    : "~r~Somebody called it in.~s~ Lose 'em, then get back to Lamar.");
                return;
            }

            StandDown();

            // A job with no heat on it never goes through the escape, and the escape is the only
            // place the delivery was ever decided -- so a clean saloon on a quiet job went home
            // in your hands and Hao never saw it. The fork belongs on both roads out of the
            // work, not only the loud one.
            if (GoingToHao)
            {
                State = MissionState.Deliver;
                MarkBay();

                Notify.Important("~g~That's them done.~s~ Now run the car to Hao -- " +
                                 "we ain't leaving that one out here.");
                return;
            }

            State = MissionState.Collect;
            Notify.Important("~g~That's them done.~s~ Get back to Lamar.");
        }

        /// <summary>Waiting on the stars to drop before he will take it off you.</summary>
        /// <summary>
        /// Driving the thing you did it in, looking for somewhere to leave it.
        ///
        /// The car is whatever you are actually in rather than the one we spawned. If you
        /// crashed the beater into a wall and took somebody's Sultan the rest of the way, the
        /// Sultan is the car with your fingerprints and the witnesses, and that is the one that
        /// has to burn.
        /// </summary>
        private void TickDump(Ped player)
        {
            TalkAboutIt();

            // Remembered the whole way there, not only once you are inside the circle.
            //
            // This used to record the car only while you were sat in it AND within the dump
            // radius, so parking twenty-five metres short and walking the last bit left it
            // holding nothing -- and the mission skipped the burn, which is the entire point of
            // it, with a line about there being no car.
            var riding = player.CurrentVehicle;
            if (riding != null && riding.Exists()) _dumpCar = riding;

            var here = player.Position.DistanceTo(DumpSpot) <= DumpRange;
            if (!here) return;

            // Still sitting in it. The prompt only makes sense once you are out.
            if (player.IsInVehicle())
            {
                Help.ShowThisFrame("Get out and burn it.");
                return;
            }

            // Out, but the car is parked back down the road. Point at it rather than shrugging.
            if (_dumpCar != null && _dumpCar.Exists() &&
                _dumpCar.Position.DistanceTo(player.Position) > TorchRange * 2f)
            {
                Help.ShowThisFrame("Bring the car closer, or go back to it.");
                return;
            }

            if (_dumpCar == null || !_dumpCar.Exists())
            {
                // Arrived on foot, or the car is gone. Nothing to burn, so nothing to do but
                // take the job as done -- standing here waiting for a car that does not exist
                // is a mission that cannot be finished.
                Notify.Ticker("~o~No car to burn.~s~ Get back to Lamar.");

                State = MissionState.Collect;
                ClearDumpBlip();
                return;
            }

            HandTheCan(player);

            // Out of the car before the petrol goes on it.
            ClearTheCar();

            State = MissionState.Torch;
            _poured = false;
            _pourStartedAt = 0;

            // Where the heat stood before the fire. Anything above this line is the arson, and
            // the arson is the job -- Lamar told you to burn it, so being wanted for burning it
            // is the mission arresting you for doing the mission.
            _starsBeforeFire = Game.Player.Wanted.WantedLevel;
            _fireQuietUntil = 0;

            ClearDumpBlip();
            Notify.Important("~o~Pour it over the car.~s~ Then light it.");
        }

        /// <summary>
        /// Petrol, then a light.
        ///
        /// Both are the game's own: the jerry can is a real weapon with real fuel in it, and
        /// firing it lays a trail on the ground that burns when anything ignites it. So this
        /// does not simulate anything -- it watches for the car to catch, which happens because
        /// you actually set it on fire.
        /// </summary>
        private void TickTorch(Ped player)
        {
            TalkAboutIt();
            KeepTheFireQuiet();

            // The car is gone -- blown up on the way, despawned, driven off by somebody else.
            // Whatever happened to it, it is not evidence any more, so the job is done.
            if (_dumpCar == null || !_dumpCar.Exists())
            {
                Burned();
                return;
            }

            var near = player.Position.DistanceTo(_dumpCar.Position) <= TorchRange;

            // Pouring counts once you have been near it with the can out and the trigger down
            // for a moment. A splash from across the car park is not pouring it over the car.
            if (!_poured)
            {
                var pouring = near && HoldingTheCan(player) &&
                              Game.IsControlPressed(Control.Attack);

                if (pouring)
                {
                    if (_pourStartedAt == 0) _pourStartedAt = Game.GameTime;
                    else if (Game.GameTime - _pourStartedAt >= PourMs)
                    {
                        _poured = true;

                        // The can stays in his hands. Switching weapons is a thing the player
                        // does, not a thing a script does to him mid-scene -- he has a gun
                        // because the job gave him one, and he knows how to reach for it.
                        Notify.Important("~o~That'll do.~s~ Back up and put a round in it.");
                    }
                }
                else
                {
                    _pourStartedAt = 0;
                }

                if (!_poured)
                {
                    if (near && !HoldingTheCan(player)) Help.ShowThisFrame("Get the can out.");
                    return;
                }
            }

            if (Function.Call<bool>(Hash.IS_ENTITY_ON_FIRE, _dumpCar.Handle) ||
                _dumpCar.IsDead || _dumpCar.HealthFloat <= 0f)
            {
                Burned();
                return;
            }

            if (near) Help.ShowThisFrame("Shoot the fuel.");
        }

        /// <summary>
        /// The fire does not get you wanted. Everything before it still does.
        ///
        /// Not a blanket suppression -- the stars you picked up shooting up Jamestown are the
        /// point of the escape and they stay. This clamps back to whatever the level was when
        /// you got out of the car, so the arson and the round you put through the fuel add
        /// nothing, and anything you had already earned is untouched.
        ///
        /// It runs for a few seconds past the fire as well, because a witness reporting a car
        /// going up does not do it the same frame.
        /// </summary>
        private void KeepTheFireQuiet()
        {
            try
            {
                var now = Game.Player.Wanted.WantedLevel;
                if (now <= _starsBeforeFire) return;

                Game.Player.Wanted.SetWantedLevel(_starsBeforeFire, false);
                Game.Player.Wanted.ApplyWantedLevelChangeNow(false);
            }
            catch
            {
                // Not worth an exception over a star.
            }
        }

        /// <summary>Car's gone up. Everything after this is the walk back.</summary>
        private void Burned()
        {
            // Handed back so the wreck is the game's problem, not a persistent car parked on
            // fire forever in a field.
            try
            {
                if (_dumpCar != null && _dumpCar.Exists())
                {
                    _dumpCar.IsPersistent = false;
                    _dumpCar.MarkAsNoLongerNeeded();
                }
            }
            catch { /* it is on fire; it will sort itself out */ }

            _dumpCar = null;
            _poured = false;

            TakeTheCan();

            if (Social != null) Social.On(SocialEvent.CarBurned, TargetName());

            // A witness reporting a car going up does not do it the same frame, so the clamp
            // carries on for a few seconds after the flames rather than stopping with them.
            _fireQuietUntil = Game.GameTime + FireGraceMs;

            // And somebody says something about it. Forced past the gap, because this is the
            // one moment of the job everybody is looking at the same thing.
            _nextChat = 0;
            Chat(GoingIn, BurningWords, ChatFightMs);

            // A car going up in a field is not quiet. If anybody is still looking for you --
            // and setting fire to a vehicle is its own good reason for them to start -- that
            // gets lost on foot before Lamar wants to see you.
            if (Game.Player.Wanted.WantedLevel > 0)
            {
                State = MissionState.Escape;
                _burned = true;

                Notify.Important("~r~That went up loud.~s~ Lose 'em, then get back to Lamar.");
                return;
            }

            State = MissionState.Collect;
            Notify.Important("~g~That's the car gone.~s~ Walk back to Lamar.");
        }

        /// <summary>
        /// Puts a full can in his hands, already out.
        ///
        /// Given rather than found. He has been driving a car he needs rid of for the last five
        /// minutes; the petrol is the one part of this nobody wants to go shopping for.
        /// </summary>
        private void HandTheCan(Ped player)
        {
            try
            {
                var can = Function.Call<uint>(Hash.GET_HASH_KEY, "WEAPON_PETROLCAN");

                _hadCan = Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON, player.Handle, can, false);

                Function.Call(Hash.GIVE_WEAPON_TO_PED, player.Handle, can, 4500, false, true);
                Function.Call(Hash.SET_PED_AMMO, player.Handle, can, 4500);
                Function.Call(Hash.SET_CURRENT_PED_WEAPON, player.Handle, can, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not hand over the petrol: " + ex.Message);
            }
        }

        /// <summary>Takes it away again, unless he turned up with one of his own.</summary>
        private void TakeTheCan()
        {
            if (_hadCan) return;

            try
            {
                var player = Game.Player.Character;
                if (player == null || !player.Exists()) return;

                var can = Function.Call<uint>(Hash.GET_HASH_KEY, "WEAPON_PETROLCAN");
                Function.Call(Hash.REMOVE_WEAPON_FROM_PED, player.Handle, can);
            }
            catch { /* he can keep it */ }
        }

        private static bool HoldingTheCan(Ped player)
        {
            try
            {
                var can = Function.Call<uint>(Hash.GET_HASH_KEY, "WEAPON_PETROLCAN");
                return Function.Call<uint>(Hash.GET_SELECTED_PED_WEAPON, player.Handle) == can;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Whether anybody on that corner has actually clocked you.</summary>
        private bool AnyoneNoticed(Ped player)
        {
            foreach (var ped in _targets)
            {
                if (ped == null || !ped.Exists()) continue;
                if (!ped.IsAlive) return true;

                try
                {
                    if (Function.Call<bool>(Hash.IS_PED_IN_COMBAT, ped.Handle, player.Handle)) return true;
                    if (Function.Call<bool>(Hash.IS_PED_FLEEING, ped.Handle)) return true;
                }
                catch
                {
                    // If we cannot tell, they have not.
                }
            }

            return false;
        }

        private Vector3 DumpSpot => new Vector3(_def.DumpX, _def.DumpY, _def.DumpZ);

        private void MarkDump()
        {
            ClearDumpBlip();

            try
            {
                _dumpBlip = World.CreateBlip(DumpSpot);
                if (_dumpBlip == null || !_dumpBlip.Exists()) return;

                _dumpBlip.Sprite = BlipSprite.Standard;
                _dumpBlip.Color = BlipColor.Yellow;
                _dumpBlip.Name = "Dump the car";
                _dumpBlip.ShowRoute = true;
            }
            catch { /* a blip is a nicety */ }
        }

        private void ClearDumpBlip()
        {
            try { if (_dumpBlip != null && _dumpBlip.Exists()) _dumpBlip.Delete(); }
            catch { /* teardown */ }

            _dumpBlip = null;
        }

        private string TargetName()
        {
            var gang = _def == null ? null : _gangs.Get(_def.TargetGang);
            return gang == null ? "" : gang.Name;
        }

        /// <summary>Close enough to the dump that getting out counts as dumping it.</summary>
        private const float DumpRange = 22f;

        /// <summary>Close enough to the car to be pouring it over the car.</summary>
        private const float TorchRange = 7f;

        /// <summary>
        /// How long the trigger has to be down before it counts as poured.
        ///
        /// Eight seconds. Two was long enough to prove you had pressed the button and far too
        /// short to be emptying a can over a car -- the whole beat is standing there doing
        /// something deliberate while the tank empties, and at two seconds it was over before
        /// you had walked round the boot.
        /// </summary>
        private const int PourMs = 8000;

        private Vehicle _dumpCar;
        private Blip _dumpBlip;
        private bool _poured;
        private int _pourStartedAt;
        private bool _hadCan;

        /// <summary>Heat before the fire, so only the fire's share is taken back off.</summary>
        private int _starsBeforeFire;

        /// <summary>How long past the flames the clamp keeps running.</summary>
        private int _fireQuietUntil;

        private const int FireGraceMs = 9000;

        /// <summary>True once the car has gone up, so the second escape leads to Lamar.</summary>
        private bool _burned;

        private void TickEscape()
        {
            TalkAboutIt();

            // Still inside the grace window from the fire. Without this the stars the arson
            // was not supposed to give you arrive a moment after the burn, during the escape,
            // and look exactly like the thing that was just fixed.
            if (Game.GameTime < _fireQuietUntil) KeepTheFireQuiet();

            if (Game.Player.Wanted.WantedLevel > 0) return;

            // Escape happens twice on a torch job: once with the car, and again after it goes
            // up if the fire brought anybody back. _burned is which of the two this was.
            if (_def.Kind == MissionKind.TorchJob && !_burned)
            {
                State = MissionState.Dump;
                MarkDump();

                Notify.Important("~o~Lost 'em.~s~ Now get rid of the car.");
                return;
            }

            // A car worth keeping goes to Hao instead of being abandoned in the street.
            if (GoingToHao)
            {
                State = MissionState.Deliver;
                MarkBay();

                Notify.Important("~g~You're clear.~s~ Take the car to Hao -- we ain't burning " +
                                 "that one.");
                return;
            }

            State = MissionState.Collect;
            Notify.Important("~g~You're clear.~s~ Get back to Lamar.");
        }

        /// <summary>
        /// Driving the car to Hao's bay and leaving it there.
        ///
        /// It is not enough to be near the spot -- the CAR has to be, because the whole point
        /// is that Hao ends up with it. Arriving on foot having parked it two streets back is
        /// the same as not arriving.
        /// </summary>
        private void TickDeliver(Ped player)
        {
            TalkAboutIt();

            if (_jobCar == null || !_jobCar.Exists() || !_jobCar.IsDriveable)
            {
                // Wrecked on the way. Nothing to hand over, so the job just ends.
                ClearBayBlip();

                State = MissionState.Collect;
                Notify.Important("~o~That's scrap now.~s~ Go and see Lamar.");
                return;
            }

            var carThere = _jobCar.Position.DistanceTo(HaoBay) <= BayRange;

            if (!carThere)
            {
                if (player.IsInVehicle(_jobCar)) Help.ShowThisFrame("Take it to Hao's bay.");
                else Help.ShowThisFrame("Get back in the car -- Hao wants it, not you.");
                return;
            }

            if (player.IsInVehicle())
            {
                Help.ShowThisFrame("Leave it here and walk.");
                return;
            }

            HandOverTheCar();
        }

        /// <summary>One car left with Hao, and when.</summary>
        private sealed class Drop
        {
            public Vehicle Car;
            public int At;
        }

        private readonly List<Drop> _dropped = new List<Drop>();

        /// <summary>How long a delivered car stands in the bay before it is taken away.</summary>
        private const int DropKeepMs = 300000;

        /// <summary>
        /// Far enough away that a car going is something you did not see happen.
        ///
        /// It could simply delete on the clock, and the clock is what was asked for -- but a
        /// car vanishing while you are stood next to it is the one way this can look broken
        /// rather than tidy. Once the five minutes are up it goes the moment your back is far
        /// enough turned, which on the way to Lamar is immediately.
        /// </summary>
        private const float DropGoneRange = 40f;

        private int _sweptAt;

        /// <summary>
        /// Clears out the cars handed to Hao.
        ///
        /// Runs OUTSIDE IsRunning, deliberately, the same way the paint on the walls does: the
        /// car outlives the job that delivered it, so hanging this off whether a job is running
        /// would mean the only cars ever cleaned up are the ones you deliver and then
        /// immediately start another job. It does nothing at all while the list is empty.
        /// </summary>
        private void Swept()
        {
            if (_dropped.Count == 0) return;

            var now = Game.GameTime;
            if (now - _sweptAt < 2000) return;
            _sweptAt = now;

            var player = Game.Player.Character;

            for (var i = _dropped.Count - 1; i >= 0; i--)
            {
                var drop = _dropped[i];

                if (drop.Car == null || !drop.Car.Exists())
                {
                    _dropped.RemoveAt(i);
                    continue;
                }

                if (now - drop.At < DropKeepMs) continue;

                // Never out from under the player, whatever the clock says.
                if (player != null && player.Exists())
                {
                    if (player.IsInVehicle(drop.Car)) continue;
                    if (player.Position.DistanceTo(drop.Car.Position) < DropGoneRange) continue;
                }

                try
                {
                    drop.Car.IsPersistent = false;
                    drop.Car.Delete();
                }
                catch
                {
                    // Already gone is the outcome we wanted anyway.
                }

                _dropped.RemoveAt(i);
                Log.Info("A car left with Hao was taken off the lot.");
            }
        }

        /// <summary>Hao takes it from here.</summary>
        private void HandOverTheCar()
        {
            ClearBayBlip();

            try
            {
                if (_jobCar != null && _jobCar.Exists())
                {
                    // Locked and left standing. Not deleted on the spot: watching the car you
                    // just drove across the city blink out in front of you is worse than seeing
                    // it parked in his bay, which is also the truer picture of what happened.
                    Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, _jobCar.Handle, 2);
                    Function.Call(Hash.SET_VEHICLE_ENGINE_ON, _jobCar.Handle, false, true, true);

                    // Kept PERSISTENT, which is the opposite of what it used to do, because we
                    // are taking responsibility for the lifetime rather than handing it to the
                    // streamer. Marked as no-longer-needed it went whenever the game felt like
                    // it, which in practice was never while you were stood there -- so every
                    // delivery left another one on the lot and Hao's yard slowly filled up with
                    // cars nobody was going to buy. Five minutes and it is gone; see Swept.
                    _jobCar.IsPersistent = true;

                    _dropped.Add(new Drop { Car = _jobCar, At = Game.GameTime });
                }
            }
            catch { /* he will sort it out */ }

            _keeper = false;

            State = MissionState.Collect;

            Notify.Important("~g~Dropped with Hao.~s~ He'll change its numbers. " +
                             "Now walk back to Lamar for the money.");

            if (Social != null) Social.On(SocialEvent.MissionDone);

            Log.Info("Job car delivered to Hao's bay.");
        }

        /// <summary>
        /// The ring on the tarmac at Hao's bay.
        ///
        /// The blip on its own only ever said which end of the lot to aim at, and a lot is a
        /// wide flat thing with no obvious place on it to stop -- so the car got left at the
        /// shutter, no hand-over came, and there was nothing to tell you that you were four
        /// metres short of a check you could not see. Drawn narrower than BayRange on purpose:
        /// a car sat inside the ring has definitely passed, rather than nearly passed.
        /// </summary>
        private void DrawBay()
        {
            if (State != MissionState.Deliver) return;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;
            if (player.Position.DistanceTo(HaoBay) > BayMarkerRange) return;

            try
            {
                // Sunk by nine tenths the way the port bay ring is, so only the top of the
                // cylinder clears the ground and it reads as paint on the tarmac rather than
                // as a green post you are meant to drive round.
                World.DrawMarker(MarkerType.Cylinder,
                                 HaoBay - new Vector3(0f, 0f, 0.9f),
                                 Vector3.Zero, Vector3.Zero,
                                 new Vector3(7.5f, 7.5f, 1.4f),
                                 Palette.Alpha(Palette.Cash, 90));
            }
            catch
            {
                // No ring this frame; the blip still says which lot.
            }
        }

        private Blip _bayBlip;

        private void MarkBay()
        {
            ClearBayBlip();

            try
            {
                _bayBlip = World.CreateBlip(HaoBay);
                if (_bayBlip == null || !_bayBlip.Exists()) return;

                Function.Call(Hash.SET_BLIP_SPRITE, _bayBlip.Handle, 225);
                _bayBlip.Color = BlipColor.Green;
                _bayBlip.Name = "Hao's bay";
                _bayBlip.ShowRoute = true;
            }
            catch { /* a blip is a nicety */ }
        }

        private void ClearBayBlip()
        {
            try { if (_bayBlip != null && _bayBlip.Exists()) _bayBlip.Delete(); }
            catch { /* teardown */ }

            _bayBlip = null;
        }

        /// <summary>
        /// Which of his own sets a finished job is talked about with.
        ///
        /// A drive-by falls through to the general one on purpose. The torch lines are all
        /// about the car not coming back, which is the half of that job a drive-by has not got,
        /// and a wrong-but-fluent post is worse than a plain one.
        /// </summary>
        private static string SaidAbout(MissionKind kind)
        {
            switch (kind)
            {
                case MissionKind.BikeRide: return "YouDidTheRide";
                case MissionKind.TorchJob: return "YouDidTheTorch";
                case MissionKind.Tags: return "YouDidTheTags";
                case MissionKind.Hit: return "YouDidTheHit";
                default: return "YouDidTheJob";
            }
        }

        /// <summary>
        /// The one job that changes what you can do afterwards.
        ///
        /// The torch run is the first time you are in a car with them for the length of a job
        /// rather than stood next to them in a yard, so it is the one that ends with them
        /// being people you can phone. Checked by ID rather than by kind: the later jobs also
        /// carry homies and unlocking on any of them would mean the message arrives at
        /// whichever one you happened to do first.
        /// </summary>
        private const string HomiesJob = "torch_rancho";

        private void MaybeUnlockHomies(MissionDef def)
        {
            if (_state == null || def == null) return;
            if (_state.HomiesUnlocked) return;

            if (!string.Equals(def.Id, HomiesJob, StringComparison.OrdinalIgnoreCase)) return;

            _state.HomiesUnlocked = true;
            _state.Touch();

            try
            {
                Notify.Text("CHAR_LAMAR", "Lamar", "Los Santos",
                            "aye you can text the homies for backup now dawg. dont be callin em " +
                            "for nothin though", true);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not text about the homies: " + ex.Message);
            }

            Log.Info("Homies unlocked after " + def.Id + ".");
        }

        /// <summary>Raises the wanted level, never lowers it.</summary>
        private static void Wanted(int stars)
        {
            try
            {
                if (Game.Player.Wanted.WantedLevel >= stars) return;

                Game.Player.Wanted.SetWantedLevel(stars, false);
                Game.Player.Wanted.ApplyWantedLevelChangeNow(false);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not set the wanted level: " + ex.Message);
            }
        }

        private void CountLostHomies()
        {
            for (var i = _homies.Count - 1; i >= 0; i--)
            {
                var ped = _homies[i];
                if (ped != null && ped.Exists() && ped.IsAlive) continue;

                _homies.RemoveAt(i);
                _homiesLost++;

                Notify.Problem("you lost one of the homies out there.");
            }
        }

        // ---- finishing ---------------------------------------------------------

        /// <summary>True when the player can hand the job in.</summary>
        public bool ReadyToCollect =>
            OnBike ? _bike.ReadyToCollect :
            OnTags ? _tags.ReadyToCollect :
            State == MissionState.Collect;

        /// <summary>Pays out and clears down. Returns what Lamar says.</summary>
        public string Collect()
        {
            if (!ReadyToCollect) return null;

            var def = _def;

            var pay = def.PayMin + _rng.Next(Math.Max(1, def.PayMax - def.PayMin + 1));
            var rep = Math.Max(0f, def.Rep - _homiesLost * HomieLostRep);

            Game.Player.Money += pay;

            _crew.AddRep(rep, "for the work");
            _state.AddRespect(rep * 0.5f);
            _state.MarkDone(def.Id);

            // And the one job that leaves you with a phone number afterwards.
            MaybeUnlockHomies(def);
            _state.Touch();

            Notify.Important("~g~+$" + pay.ToString("N0") + "~s~ and " + rep.ToString("0") + " rep.");
            Log.Info("Mission " + def.Id + " paid $" + pay + ", " + rep.ToString("0") + " rep, " +
                     _homiesLost + " homies lost.");

            var line = string.IsNullOrEmpty(def.Done) ? "Good look. Take that." : def.Done;

            if (Social != null)
            {
                // Reported as the thing it actually was. "A job got done" is a press release;
                // "somebody sprayed a corner on Grove" is what a neighbour would post.
                switch (def.Kind)
                {
                    case MissionKind.BikeRide: Social.On(SocialEvent.Brawl); break;
                    case MissionKind.DriveBy:
                    case MissionKind.TorchJob: Social.On(SocialEvent.DriveBy); break;
                    case MissionKind.Tags: Social.On(SocialEvent.Tagged); break;
                    default: Social.On(SocialEvent.MissionDone); break;
                }

                // And the job itself, so a run of work reads as a run of work.
                Social.On(SocialEvent.MissionDone, def.Name, pay);

                // Then HIS post about it. The block saying something happened and the man it
                // happened to saying something are two different posts, and the second one is
                // the only place in this feed where the story is told from the inside.
                //
                // The specific set first and the general one behind it, so a job kind nobody
                // has written lines for still gets a post rather than silence.
                if (Social.PostAsYou(SaidAbout(def.Kind), def.Name) == null)
                {
                    Social.PostAsYou("YouDidTheJob", def.Name);
                }
            }

            // And then he wants a minute. Set before Clear, because Clear is where the job
            // stops existing and this is a fact about the man rather than about the job.
            var rest = RestMinutes == null ? 10f : RestMinutes();

            if (rest > 0.01f)
            {
                _restUntil = Game.GameTime + (int)(rest * 60000f);

                try
                {
                    Notify.Text("CHAR_LAMAR", "Lamar", "Los Santos",
                                RestLines[_rng.Next(RestLines.Length)], false);
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not text about needing a minute: " + ex.Message);
                }

                Log.Info("Lamar is off for " + rest.ToString("0.#") + " minutes.");
            }

            Clear();
            return line;
        }

        /// <summary>
        /// You went down on a job.
        ///
        /// Separate from Fail because it is called from somewhere Fail could not reach. The
        /// death check inside Update never once ran: Main gates the whole tick on IsPlayable,
        /// IsPlayable is false while the player is not alive, so the runner stops ticking at
        /// the exact moment it needed to notice -- and by the time it started again the player
        /// was alive at Pillbox and the job carried on as though nothing had happened.
        ///
        /// The house script watches for the death frame anyway, for the socials feed. This
        /// hangs off that, which is the one place in the mod that is still looking.
        /// </summary>
        public void Died()
        {
            if (!IsRunning) return;

            var giver = _def;
            Fail("You went down out there.");

            // And he hears about it. Not the mod telling you the job ended -- a man you know
            // saying he heard, and that the work is still there.
            try
            {
                Notify.Text("CHAR_LAMAR", "Lamar", "Los Santos",
                            DeathTexts[_rng.Next(DeathTexts.Length)], false);

                Log.Info("Died on " + (giver == null ? "a job" : giver.Id) + "; Lamar texted.");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not text about the wipe: " + ex.Message);
            }
        }

        /// <summary>
        /// What he sends after you have been carried into Pillbox.
        ///
        /// Sympathy in his own register, which is not much sympathy -- and every one of them
        /// ends by telling you the job is still on, because the point of the message is that
        /// dying costs you the run rather than the chain.
        /// </summary>
        private static readonly string[] DeathTexts =
        {
            "damn dawg. heard they got you. you good? come see me when you up, we run it back",
            "aw man that sucks. get well soon cuz. job's still here when you are",
            "yo i heard. take a minute, get yourself right, then come find me. we ain't done",
            "they got you out there?? damn. rest up. come see me and we go again",
            "man. i told you it was gon be like that. anyway. come find me when you healed up",
            "pillbox again. aight. when they let you out, you know where im at"
        };


        public void Fail(string reason)
        {
            if (!IsRunning) return;

            var id = _def == null ? "?" : _def.Id;
            Clear();

            if (!string.IsNullOrEmpty(reason)) Notify.Failure(reason);
            Log.Info("Mission " + id + " failed: " + reason);

            if (Social != null) Social.On(SocialEvent.MissionFailed);
        }

        /// <summary>The yard the block party is in, by the second set of decks.</summary>
        private static readonly Vector3 PartySpot = new Vector3(-202.900f, -1729.900f, 32.664f);

        /// <summary>Past this and he just goes home. Nobody crosses the city to dance.</summary>
        private const float PartyRange = 320f;

        /// <summary>Verified against the game's own animation data, not guessed.</summary>
        private const string PartyDict = "amb@world_human_partying@male@partying_beer@base";
        private const string PartyClip = "base";

        private const int PartyMinMs = 26000;
        private const int PartyMaxMs = 48000;

        /// <summary>
        /// Gets a homie out of the car and gives him somewhere to be.
        ///
        /// A SEQUENCE, and that is the whole fix. The previous version tasked him to leave the
        /// vehicle and then tasked him to wander on the very next line -- and a second task
        /// does not queue behind the first, it REPLACES it. So the leave-vehicle was thrown
        /// away before it started, the wander could not run from a passenger seat, and three
        /// men sat in a parked car for the rest of the session. Exactly the same mistake the
        /// taxi drop-off made.
        ///
        /// Inside a sequence, 0 means "the ped performing this", and each task genuinely waits
        /// for the one before it.
        ///
        /// If the job finished anywhere near the block party he walks over and joins it for
        /// half a minute before drifting off, because a man who has just been paid does not
        /// silently evaporate into traffic. Anywhere else, he just goes.
        /// </summary>
        private void SendHimOff(Ped ped, int index)
        {
            var slot = new OutputArgument();
            var seq = 0;

            try
            {
                var atParty = ped.Position.DistanceTo(PartySpot) <= PartyRange
                              && Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, PartyDict);

                Function.Call(Hash.OPEN_SEQUENCE_TASK, slot);
                seq = slot.GetResult<int>();

                // Out of the car first, whatever comes after it. Flag 0 is the ordinary exit
                // that closes the door behind him.
                Function.Call(Hash.TASK_LEAVE_VEHICLE, 0, 0, 0);

                if (atParty)
                {
                    // Spread out, so three men do not stand inside each other by the speaker.
                    var spread = new Vector3(
                        PartySpot.X + (index - 1) * 1.3f,
                        PartySpot.Y + (index % 2 == 0 ? 0.9f : -0.9f),
                        PartySpot.Z);

                    Function.Call(Hash.TASK_GO_TO_COORD_ANY_MEANS, 0,
                                  spread.X, spread.Y, spread.Z, 1.3f, 0, false, 786603, 0f);

                    // Timed rather than looped forever: the task ends on its own and the
                    // wander behind it is what takes him home. A scenario here would never
                    // finish and he would stand in that yard until the session ended.
                    var dance = PartyMinMs + _rng.Next(PartyMaxMs - PartyMinMs);

                    Function.Call(Hash.TASK_PLAY_ANIM, 0, PartyDict, PartyClip,
                                  4f, -4f, dance, 1, 0f, false, 0, false);
                }

                Function.Call(Hash.TASK_WANDER_STANDARD, 0, 10f, 10);

                Function.Call(Hash.CLOSE_SEQUENCE_TASK, seq);
                Function.Call(Hash.TASK_PERFORM_SEQUENCE, ped.Handle, seq);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not send a homie off: " + ex.Message);
            }
            finally
            {
                // Always, even if the sequence threw halfway through building it. There are
                // only so many sequence slots and a leaked one is gone for the session.
                try { Function.Call(Hash.CLEAR_SEQUENCE_TASK, slot); } catch { }
            }
        }

        private void Clear()
        {
            // Before the job's own record of who it was against is thrown away.
            if (_feuding) SetFeud(false);

            // And before the car is forgotten about, or it stays locked for the rest of the
            // session with nobody left who knows it was us.
            Unlock();

            _bike.Clear();
            _tags.Clear();

            ClearSiteBlip();
            ClearBlips();

            foreach (var ped in _targets)
            {
                try
                {
                    if (ped == null || !ped.Exists()) continue;
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);
                    ped.MarkAsNoLongerNeeded();
                }
                catch { /* teardown */ }
            }
            _targets.Clear();
            _targetBlips.Clear();

            // Asked for before anybody is tasked, so it is resident by the time the first man
            // has finished climbing out and walked across the yard.
            if (_homies.Count > 0) Function.Call(Hash.REQUEST_ANIM_DICT, PartyDict);

            var sent = 0;

            foreach (var ped in _homies)
            {
                try
                {
                    if (ped == null || !ped.Exists()) continue;

                    Function.Call(Hash.REMOVE_PED_FROM_GROUP, ped.Handle);

                    if (ped.IsAlive) SendHimOff(ped, sent++);

                    ped.MarkAsNoLongerNeeded();
                }
                catch { /* teardown */ }
            }

            if (_homies.Count > 0) Log.Info("Job over; " + _homies.Count + " homies sent on their way.");

            _homies.Clear();

            _keeper = false;
            ClearBayBlip();

            // Let go rather than deleted. A car you drove to a job and back should still be
            // sitting outside afterwards, the same as the bikes.
            try { if (_jobCar != null && _jobCar.Exists()) _jobCar.MarkAsNoLongerNeeded(); }
            catch { /* teardown */ }

            _jobCar = null;

            // The torch job's own state. A second run of the mission that inherited _burned
            // from the first would skip the dump entirely and send you straight to Lamar.
            ClearDumpBlip();
            _dumpCar = null;
            _burned = false;
            _poured = false;
            _pourStartedAt = 0;
            _hadCan = false;
            _starsBeforeFire = 0;
            _fireQuietUntil = 0;

            _def = null;
            State = MissionState.None;
        }

        /// <summary>Removes every blip the job made, which peds do not do for you.</summary>
        private void ClearBlips()
        {
            foreach (var blip in _blips)
            {
                try { if (blip != null && blip.Exists()) blip.Delete(); }
                catch { /* teardown */ }
            }
            _blips.Clear();
        }

        private void ClearSiteBlip()
        {
            try { if (_siteBlip != null && _siteBlip.Exists()) _siteBlip.Delete(); }
            catch { /* teardown */ }

            _siteBlip = null;
        }

        public void RestoreWorld() => Clear();

        // ---- hud ---------------------------------------------------------------

        /// <summary>One line, top left, saying what you are meant to be doing.</summary>
        public void Draw()
        {
            if (!IsRunning || _def == null) return;

            _tags.Draw();
            _bike.Draw();
            DrawBay();

            // Centred at the top: it belongs to the job, not to the corner of the screen.
            var left = 0.5f - CardWidth * 0.5f;
            var ink = PhaseColour();

            // Backing, a rail down the left and a hairline along the top. The rail and the
            // line are the only two things that change colour, so the card reads as the same
            // object throughout a job while still saying which part of it you are in.
            Hud.RectFrom(left, CardTop, CardWidth, CardHeight, CardBack);
            Hud.RectFrom(left, CardTop, CardRail, CardHeight, ink);
            Hud.RectFrom(left, CardTop, CardWidth, 0.0022f, ink);

            // The icon, in its own well so it reads as a badge rather than as a stray glyph.
            var iconLeft = left + CardRail + CardPad;
            var iconWide = Hud.ToX(IconSize);

            Hud.RectFrom(iconLeft, CardTop + (CardHeight - IconSize) * 0.5f,
                         iconWide, IconSize, Color.FromArgb(20, 255, 255, 255));

            Hud.File(KindIcon(), iconLeft + iconWide * 0.5f, CardTop + CardHeight * 0.5f,
                     IconSize * 0.62f, 0f, ink);

            var x = iconLeft + iconWide + CardPad;

            Hud.Text(_def.Name.ToUpperInvariant(), x, CardTop + 0.009f, 0.30f, Palette.Text,
                     Hud.FontLabel, centre: false);

            Hud.Text(Objective, x, CardTop + 0.030f, 0.26f, Palette.TextDim,
                     Hud.FontBody, centre: false);

            // The chip: the one number that matters in this phase, right-aligned so it does
            // not move about as the objective text changes length underneath it.
            Color chipInk;
            var chip = Chip(out chipInk);

            if (!string.IsNullOrEmpty(chip))
            {
                Hud.TextRight(chip, left + CardWidth - CardPad, CardTop + 0.031f, 0.23f,
                              chipInk, Hud.FontLabel);
            }

            // And the bar. Drawn even at zero so the card does not change height between
            // phases -- a readout that reflows while you are reading it is worse than one
            // that shows an empty track.
            var barWide = CardWidth - (x - left) - CardPad;
            var barY = CardTop + CardHeight - 0.010f;

            Hud.RectFrom(x, barY, barWide, BarHeight, Color.FromArgb(40, 255, 255, 255));

            // Eased toward the figure rather than set to it.
            //
            // This is the difference between a bar and a readout. Progress on most of these
            // phases is a DISTANCE, which changes in steps as you ride -- so the fill jumped a
            // centimetre at a time and looked like a thing being redrawn rather than a thing
            // filling up. Sliding toward the target makes the same numbers read as movement,
            // which is the whole point of showing them as a bar instead of as a percentage.
            //
            // Snapped rather than eased when a phase CHANGES. The new phase's progress starts
            // at zero and easing across that boundary is a bar running backwards down the card
            // while you read the sentence that replaced it.
            var done = Progress();
            var phase = Phasing();

            if (phase != _barPhase)
            {
                _barPhase = phase;
                _bar = done;
            }

            _bar += (done - _bar) * BarRate;
            if (Math.Abs(done - _bar) < 0.002f) _bar = done;

            if (_bar > 0f)
            {
                Hud.RectFrom(x, barY, barWide * _bar, BarHeight, ink);

                // A light travelling up the filled part, so a bar that is not moving is still
                // visibly live. It stays INSIDE the fill -- a sheen running along the empty
                // track would be the card promising progress it has not made.
                var t = (Game.GameTime % BarSweepMs) / (float)BarSweepMs;
                var lit = barWide * _bar;
                var band = Math.Min(lit, barWide * 0.10f);
                var at = x - band + (lit + band) * t;

                var lo = Math.Max(x, at);
                var hi = Math.Min(x + lit, at + band);

                if (hi > lo)
                {
                    Hud.RectFrom(lo, barY, hi - lo, BarHeight,
                                 Color.FromArgb(120, 255, 255, 255));
                }
            }
        }

        private const float CardWidth = 0.300f;
        private const float CardTop = 0.052f;
        private const float CardHeight = 0.070f;
        private const float CardPad = 0.008f;
        private const float CardRail = 0.0022f;
        private const float IconSize = 0.034f;
        private const float BarHeight = 0.0045f;

        /// <summary>How fast the fill catches the figure, and how fast the light travels it.</summary>
        private const float BarRate = 0.12f;
        private const int BarSweepMs = 1600;

        /// <summary>Where the fill has got to, and which phase it belongs to.</summary>
        private float _bar;
        private string _barPhase = "";

        /// <summary>
        /// A name for the part of the job the bar is currently measuring.
        ///
        /// The state alone is not enough: the bike ride and the tag run both spend their whole
        /// length inside one MissionState while running through phases of their own, and each
        /// of those restarts the count from nothing. Anything that resets progress has to
        /// change this string, or the bar eases backwards across the boundary.
        /// </summary>
        private string Phasing()
        {
            if (OnBike) return "bike:" + _bike.Phase;
            // The tag run has no phase of its own to ask for; its progress is the count of
            // spots done, which only ever goes up, so the whole run is one band.
            if (OnTags) return "tags";

            return "job:" + State;
        }

        private static readonly Color CardBack = Color.FromArgb(232, 12, 13, 15);

        /// <summary>
        /// What colour this part of the job is.
        ///
        /// Amber for going somewhere, white for doing the thing, red for the law. Three states
        /// worth telling apart at a glance, rather than five that each need reading.
        /// </summary>
        private Color PhaseColour()
        {
            // The bike ride and the tag run have phases of their own and they were both
            // getting the same flat white, so the one card in the mod that is supposed to say
            // which part of a job you are in said nothing at all for two of the five jobs.
            if (OnBike)
            {
                switch (_bike.Phase)
                {
                    case BikePhase.Fight:
                    case BikePhase.Escape: return Palette.Danger;

                    case BikePhase.ToBike:
                    case BikePhase.Riding:
                    case BikePhase.Rob: return Palette.Warn;

                    case BikePhase.Home: return Palette.Cash;

                    default: return Palette.Accent;
                }
            }

            if (OnTags) return Palette.Warn;

            switch (State)
            {
                case MissionState.Escape: return Palette.Danger;
                case MissionState.Travel: return Palette.Warn;
                case MissionState.Deliver: return Palette.Cash;
                case MissionState.Dump:
                case MissionState.Torch: return Palette.Warn;
                default: return Palette.Accent;
            }
        }

        /// <summary>The job's own symbol, so the card is recognisable before it is read.</summary>
        private string KindIcon()
        {
            if (OnTags) return "spray.png";
            if (OnBike) return "people.png";

            switch (_def.Kind)
            {
                case MissionKind.TorchJob: return "fire.png";
                case MissionKind.DriveBy: return "car.png";
                case MissionKind.Hit: return "guns.png";
                default: return "people.png";
            }
        }

        /// <summary>
        /// How far through this phase you are, 0 to 1.
        ///
        /// Per phase rather than across the job, because the phases are not comparable: a bar
        /// that crawled across a whole mission would sit still for the entire drive out and
        /// then jump. Travel closes on the block, work counts bodies, an escape counts stars
        /// coming back down, and the torch counts its own two steps.
        /// </summary>
        private float Progress()
        {
            try
            {
                // Both of them know how far through they are; neither was ever asked, so the
                // bar sat empty for the whole of two jobs.
                if (OnBike) return Clamp01(_bike.Advance);
                if (OnTags) return Clamp01(_tags.Advance);

                var player = Game.Player.Character;

                switch (State)
                {
                    case MissionState.Travel:
                        if (player == null || !player.Exists()) return 0f;
                        var away = player.Position.DistanceTo(_site);
                        return Clamp01(1f - away / TravelBarRange);

                    case MissionState.Work:
                        var total = Math.Max(1, _def.Targets);
                        return Clamp01((total - Standing()) / (float)total);

                    case MissionState.Escape:
                        // Falling stars fill it, so it reads as getting away rather than as
                        // getting deeper in.
                        return Clamp01(1f - Game.Player.Wanted.WantedLevel / 5f);

                    case MissionState.Torch:
                        return _poured ? 0.66f : 0.33f;

                    default:
                        return 0f;
                }
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>The short right-hand label, and what colour it should be.</summary>
        private string Chip(out Color ink)
        {
            ink = Palette.TextDim;

            try
            {
                if (OnBike || OnTags) return "";

                switch (State)
                {
                    case MissionState.Travel:
                        ink = Palette.Warn;
                        return ZoneName().ToUpperInvariant();

                    case MissionState.Work:
                        var total = Math.Max(1, _def.Targets);
                        var down = Math.Max(0, total - Standing());
                        ink = down >= total ? Palette.Cash : Palette.Text;
                        return down + " OF " + total;

                    case MissionState.Escape:
                        ink = Palette.Danger;
                        return "WANTED";

                    case MissionState.Deliver:
                        ink = Palette.Cash;
                        return "TO HAO";

                    case MissionState.Dump:
                        ink = Palette.Warn;
                        return "DUMP IT";

                    case MissionState.Torch:
                        ink = Palette.Warn;
                        return _poured ? "STEP 2 OF 2" : "STEP 1 OF 2";

                    default:
                        return "";
                }
            }
            catch
            {
                return "";
            }
        }

        /// <summary>How many of them are still up.</summary>
        private int Standing()
        {
            var n = 0;

            for (var i = 0; i < _targets.Count; i++)
            {
                var ped = _targets[i];
                if (ped != null && ped.Exists() && ped.IsAlive) n++;
            }

            return n;
        }

        /// <summary>Where the travel bar starts filling from.</summary>
        private const float TravelBarRange = 900f;

        private static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            return v > 1f ? 1f : v;
        }
    }
}
