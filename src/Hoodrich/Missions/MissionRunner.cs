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

        private static readonly string[] HomieWeapons =
        {
            "WEAPON_PISTOL", "WEAPON_MICROSMG", "WEAPON_PUMPSHOTGUN"
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
        /// Always the same one. A drive-by you did in whatever happened to be parked nearby is
        /// a drive-by you did in a stranger car -- the point of a set car is that it becomes
        /// the car, and you learn to leave it somewhere afterwards.
        /// </summary>
        private static readonly string[] DriveByCars = { "vorschlafhammer", "vorschlaghammer", "buccaneer2", "faction" };

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
            _tags = new TagRun(gangs);
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
                        return _def.Kind == MissionKind.TorchJob && !_burned
                            ? "Lose the cops"
                            : "Lose the cops, then get back to Lamar";

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
            var muster = Ground(new Vector3(def.CarX, def.CarY, def.CarZ));

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

                        Function.Call(Hash.GIVE_WEAPON_TO_PED, ped.Handle,
                                      Function.Call<uint>(Hash.GET_HASH_KEY, weapon), 250, false, true);
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
        private void TellHimIfThereIsWork()
        {
            if (_state == null || Book == null || IsRunning) return;
            if (Game.GameTime < _nextWorkCheck) return;

            _nextWorkCheck = Game.GameTime + WorkCheckMs;

            // The same window FixerTalk offers from: everything up to one past the last one
            // finished, gated on rank.
            var reached = -1;

            for (var i = 0; i < Book.All.Count; i++)
            {
                if (_state.HasDone(Book.All[i].Id)) reached = i;
            }

            for (var i = 0; i <= reached + 1 && i < Book.All.Count; i++)
            {
                var def = Book.All[i];

                if (_state.Rank < def.MinRank) continue;
                if (_state.HasDone(def.Id)) continue;
                if (_state.HasBeenOffered(def.Id)) continue;

                _state.MarkOffered(def.Id);
                _state.Touch();

                Notify.Text("CHAR_LAMAR", "Lamar", "Los Santos",
                            "aye. got somethin for you. come find me when you ready, cuz",
                            true);

                Log.Info("Lamar texted about " + def.Id + ".");
                return;
            }
        }

        private int _nextWorkCheck;

        /// <summary>Rank and progress do not change fast. Every ten seconds is plenty.</summary>
        private const int WorkCheckMs = 10000;

        public void Update()
        {
            TellHimIfThereIsWork();

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
            KeepThemSeated();

            switch (State)
            {
                case MissionState.Travel:
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
                    TickEscape();
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
        /// Puts anybody who has got out back in, for the whole length of a job done from a car.
        ///
        /// KeepShooting already did this, but only ever ran in the WORK state -- so the moment
        /// the shooting was over and the objective read "lose the cops" nothing was watching
        /// them any more, and that is exactly where they were last seen stood in the road
        /// firing at a patrol car. The lock has to hold for the drive out, the work, the
        /// escape and the run back, because it is the same car ride throughout.
        ///
        /// Released in Clear, which is what runs when the job hands in at Lamar's.
        /// </summary>
        private void KeepThemSeated()
        {
            if (!FromTheCar) return;
            if (Game.GameTime < _nextSeatCheck) return;
            _nextSeatCheck = Game.GameTime + SeatCheckMs;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            var ride = player.CurrentVehicle;
            if (ride == null || !ride.Exists()) return;

            foreach (var homie in _homies)
            {
                if (homie == null || !homie.Exists() || !homie.IsAlive) continue;
                if (homie.IsInVehicle()) continue;

                SitBackDown(homie, ride);
            }
        }

        private int _nextSeatCheck;
        private const int SeatCheckMs = 900;

        /// <summary>
        /// Puts anybody idle back on a target, a couple of times a second at most.
        /// </summary>
        private void KeepShooting()
        {
            if (Game.GameTime < _nextDriveBy) return;
            _nextDriveBy = Game.GameTime + DriveByRetaskMs;

            var player = Game.Player.Character;
            var ride = player == null || !player.Exists() ? null : player.CurrentVehicle;

            foreach (var homie in _homies)
            {
                if (homie == null || !homie.Exists() || !homie.IsAlive) continue;

                try
                {
                    if (!homie.IsInVehicle())
                    {
                        SitBackDown(homie, ride);
                        continue;
                    }

                    // Only when he has actually stopped. Re-issuing over a running task
                    // restarts the aim every time and he never gets a round off -- the same
                    // mistake that had the bike homies permanently starting to follow.
                    if (Function.Call<bool>(Hash.GET_IS_TASK_ACTIVE, homie.Handle, DriveByTask)) continue;

                    var foe = NearestLiveTarget(homie);
                    if (foe != null) Shoot(homie, foe);
                }
                catch { /* he will sit this one out */ }
            }
        }

        /// <summary>
        /// Puts a man who has got out back in.
        ///
        /// Combat attribute 3 stops a man leaving a car to FIGHT, and it is set, and it works.
        /// It has nothing to say about the other reason he gets out, which is that he is in
        /// your GROUP -- and group members follow the leader, so the moment anything makes the
        /// game think it should reposition him, out he goes. That is not a combat decision and
        /// no combat flag touches it.
        ///
        /// So it is answered where it happens rather than prevented somewhere it cannot be.
        /// If he is out and there is a car, he gets back in it.
        ///
        /// Only while YOU are in one. If you have parked and got out yourself, a homie being
        /// dragged back into an empty car by an invisible hand is a worse bug than the one
        /// being fixed -- and this only runs during the work phase anyway, so the moment the
        /// job turns into burning the car they are free to get out with you.
        /// </summary>
        private static void SitBackDown(Ped homie, Vehicle ride)
        {
            if (ride == null || !ride.Exists()) return;
            if (Function.Call<bool>(Hash.GET_IS_TASK_ACTIVE, homie.Handle, EnterVehicleTask)) return;

            Function.Call(Hash.TASK_ENTER_VEHICLE, homie.Handle, ride.Handle,
                          12000, -2, 2f, 1, 0);
        }

        /// <summary>CTaskEnterVehicle, so he is not told to get in while he is getting in.</summary>
        private const int EnterVehicleTask = 160;

        private int _nextDriveBy;
        private const int DriveByRetaskMs = 700;

        /// <summary>CTaskVehicleGun, which is what TASK_DRIVE_BY actually starts.</summary>
        private const int DriveByTask = 100;

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

                    // 3 is BF_CanLeaveVehicle, and on a drive-by the answer is no.
                    //
                    // This is why they were piling out of the car and fighting on foot. They
                    // were told they could get out and then handed a foot-combat task, which
                    // is a request to get out -- the mission asked for exactly what it did not
                    // want. On a job that is done from a moving car they stay in it, and the
                    // job ends when the car leaves rather than when everybody is dead.
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, homie.Handle, 3, !FromTheCar);

                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, homie.Handle, 2, true);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, homie.Handle, 1, true);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, homie.Handle, 0, true);

                    Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, homie.Handle, 2);

                    // Locked in, on a job done out of a window.
                    //
                    // Attribute 3 alone was not enough and they kept piling out. Two other
                    // things were letting them: non-temporary events were left UNBLOCKED, so
                    // being shot at is an event they answer by bailing out and taking cover,
                    // and nothing stopped them being pulled out by anybody who fancied it.
                    // On foot jobs none of this applies -- getting out is the job.
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS,
                                  homie.Handle, FromTheCar);
                    Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, homie.Handle, !FromTheCar);

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
        /// running battle you cannot leave. Blocking permanent events is the part that matters:
        /// clearing tasks alone lasts until the next siren.
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
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, homie.Handle, true);

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
        /// Whether this job is shot out of a car window rather than on foot.
        ///
        /// A torch job is a message delivered at speed -- pull up, let them hear it, burn the
        /// car and go. A drive-by is the same shape. Everything else is people getting out.
        /// </summary>
        private bool FromTheCar =>
            _def != null &&
            (_def.Kind == MissionKind.TorchJob || _def.Kind == MissionKind.DriveBy);

        /// <summary>
        /// Puts a homie on a target, from wherever he is.
        ///
        /// TASK_DRIVE_BY leans him out of the window and fires; TASK_COMBAT_PED is the foot
        /// version. Which one depends on the job AND on where he actually is, because a man
        /// who never made it into the car cannot do a drive-by and would simply stand there.
        /// </summary>
        private void Shoot(Ped homie, Ped foe)
        {
            var mounted = FromTheCar && homie.IsInVehicle();

            if (mounted)
            {
                Function.Call(Hash.TASK_DRIVE_BY, homie.Handle, foe.Handle, 0,
                              0f, 0f, 0f, DriveByRange, DriveByAccuracy, true, FullAuto);
                return;
            }

            Function.Call(Hash.TASK_COMBAT_PED, homie.Handle, foe.Handle, 0, 16);
        }

        /// <summary>How far out of the window they will bother shooting.</summary>
        private const float DriveByRange = 45f;

        /// <summary>
        /// Not marksmen. They are hanging out of a moving car.
        ///
        /// The point of the job is that a whole street hears it, not that four men die, so
        /// spraying a block and hitting some of it is the correct result rather than a
        /// shortcoming.
        /// </summary>
        private const int DriveByAccuracy = 40;

        private static readonly uint FullAuto = 0xC6EE6B4C;

        private void TickWork(Ped player)
        {
            ClearDeadBlips();
            TalkAboutIt();

            // Kept on the trigger.
            //
            // A drive-by task ends on its own -- the target dies, or goes out of range, or the
            // car turns a corner and breaks line of sight -- and a homie with no task falls
            // back on his own judgement, which is to get out and go after somebody. Re-issuing
            // is what keeps him in his seat for the length of the street.
            if (FromTheCar) KeepShooting();

            KeepThemSeated();

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
            if (_def.Kind == MissionKind.TorchJob)
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
                Notify.Important("~r~Somebody called it in.~s~ Lose 'em, then get back to Lamar.");
                return;
            }

            StandDown();

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

            State = MissionState.Collect;
            Notify.Important("~g~You're clear.~s~ Get back to Lamar.");
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
            }

            Clear();
            return line;
        }

        public void Fail(string reason)
        {
            if (!IsRunning) return;

            var id = _def == null ? "?" : _def.Id;
            Clear();

            if (!string.IsNullOrEmpty(reason)) Notify.Failure(reason);
            Log.Info("Mission " + id + " failed: " + reason);

            if (Social != null) Social.On(SocialEvent.MissionFailed);
        }

        private void Clear()
        {
            // Before the job's own record of who it was against is thrown away.
            if (_feuding) SetFeud(false);

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

            foreach (var ped in _homies)
            {
                try
                {
                    if (ped == null || !ped.Exists()) continue;

                    // Let go of them. They were locked into the car for the length of the job
                    // and would otherwise spend the rest of the session unable to get out of
                    // one, unable to be pulled out of one, and deaf to everything around them.
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 3, true);
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);
                    Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, ped.Handle, true);

                    Function.Call(Hash.REMOVE_PED_FROM_GROUP, ped.Handle);
                    ped.MarkAsNoLongerNeeded();
                }
                catch { /* teardown */ }
            }
            _homies.Clear();

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

            var done = Progress();
            if (done > 0f) Hud.RectFrom(x, barY, barWide * done, BarHeight, ink);
        }

        private const float CardWidth = 0.300f;
        private const float CardTop = 0.052f;
        private const float CardHeight = 0.070f;
        private const float CardPad = 0.008f;
        private const float CardRail = 0.0022f;
        private const float IconSize = 0.034f;
        private const float BarHeight = 0.0045f;

        private static readonly Color CardBack = Color.FromArgb(232, 12, 13, 15);

        /// <summary>
        /// What colour this part of the job is.
        ///
        /// Amber for going somewhere, white for doing the thing, red for the law. Three states
        /// worth telling apart at a glance, rather than five that each need reading.
        /// </summary>
        private Color PhaseColour()
        {
            if (OnBike || OnTags) return Palette.Accent;

            switch (State)
            {
                case MissionState.Escape: return Palette.Danger;
                case MissionState.Travel: return Palette.Warn;
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
                if (OnBike || OnTags) return 0f;

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
