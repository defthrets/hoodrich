using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Gangs;
using Hoodrich.State;
using Hoodrich.UI;

namespace Hoodrich.Territory
{
    /// <summary>What a car is doing at this moment.</summary>
    internal enum PatrolPhase
    {
        /// <summary>Moving between points on the block.</summary>
        Rolling,

        /// <summary>Pulled up somewhere with the light on.</summary>
        Sitting,

        /// <summary>Seen a gun on you and coming over.</summary>
        Coming,

        /// <summary>Stood over you, taking what you are carrying.</summary>
        Searching
    }

    /// <summary>One car and the two in it.</summary>
    internal sealed class Cruiser
    {
        public Vehicle Car;
        public Ped Driver;
        public readonly List<Ped> Crew = new List<Ped>();

        public PatrolPhase Phase = PatrolPhase.Rolling;

        public Vector3 Target;
        public bool StopThere;
        public int SitUntil;

        public Vector3 WasAt;
        public int LookedAt;
        public int Nudges;
        public int BornAt;

        /// <summary>When the next line over the loudspeaker and the next siren chirp are due.</summary>
        public int NextBark;
        public int NextChirp;

        /// <summary>Whichever of them got out, and when the search finishes.</summary>
        public Ped OnFoot;
        public int SearchDone;

        /// <summary>Where the light is pointing. Eased rather than snapped.</summary>
        public Vector3 Beam;

        /// <summary>When the siren goes quiet again, which is what makes a chirp a chirp.</summary>
        public int SirenUntil;
    }

    /// <summary>
    /// The law, going round the blocks because the blocks are the blocks.
    ///
    /// Not a response system. Nothing here is triggered by anything you have done -- these cars
    /// were coming down that street tonight whether you were on it or not, which is the
    /// difference between a neighbourhood that gets policed and a game that punishes you. The
    /// mod already has three systems that send police BECAUSE of something: a bust, a raid, a
    /// robbery. This is the fourth kind, and it is the only one that is just weather.
    ///
    /// Same shape as the set's own cars: one road network, the same trick for finding the backs
    /// of buildings, the same watchdog for anything that cannot reach where it is going. What
    /// makes it read as police rather than as traffic is the three things a patrol car does
    /// that nothing else does -- a light down the alley it is passing, a word over the speaker,
    /// and a half-second of siren at a junction to tell everybody it is there.
    ///
    /// And it has one opinion. A gun in your hand on a street they are driving down is the one
    /// thing that stops the car, and then it is a search rather than a shootout: they take what
    /// you are holding and they leave. Pulling on them from there is your decision and the game
    /// already knows exactly what to do about it.
    /// </summary>
    internal sealed class Patrol
    {
        private const int TickMs = 700;

        /// <summary>Far enough out to arrive rather than appear.</summary>
        private const float SpawnNear = 90f;
        private const float SpawnFar = 170f;

        /// <summary>Past this they are somebody else's problem.</summary>
        private const float LetGoRange = 300f;

        /// <summary>Nothing lasts forever. Ten minutes and this car has finished its round.</summary>
        private const int LifetimeMs = 600000;

        /// <summary>How long between cars, before the dice.</summary>
        private const int GapMinMs = 70000;
        private const int GapMaxMs = 220000;

        /// <summary>How long they sit somewhere with the light on.</summary>
        private const int SitMinMs = 14000;
        private const int SitMaxMs = 40000;

        /// <summary>Chance the next place they are headed is somewhere they will stop.</summary>
        private const int StopChancePercent = 40;

        private const float ArrivedRange = 16f;

        private const float StuckMoved = 3f;
        private const int StuckLookMs = 9000;
        private const int MaxNudges = 2;

        /// <summary>Slow. This is a car looking at things rather than going somewhere.</summary>
        private const float Cruise = 8f;
        private const int Style = 786603;

        private const int PedTypeCop = 6;

        /// <summary>How far a gun in your hand is noticed from, with the car in sight of you.</summary>
        private const float NoticeRange = 28f;

        /// <summary>Close enough to be searched.</summary>
        private const float SearchRange = 3.2f;

        /// <summary>How long the search itself takes once he is stood over you.</summary>
        private const int SearchMs = 2600;

        /// <summary>He gives up coming after this long. You outran him, or he cannot get to you.</summary>
        private const int ChasePatienceMs = 30000;

        private const int BarkGapMinMs = 16000;
        private const int BarkGapMaxMs = 45000;

        private const int ChirpGapMinMs = 30000;
        private const int ChirpGapMaxMs = 90000;

        private const int ChirpMs = 550;

        private static readonly string[] Cars = { "police", "police2", "police3", "sheriff" };
        private static readonly string[] Cops = { "s_m_y_cop_01", "s_f_y_cop_01" };

        /// <summary>
        /// What they say over the speaker.
        ///
        /// Forced through the megaphone parameter, which is what makes it come out of the car
        /// rather than out of a man. Every one of these is wrapped and none of them is checked:
        /// a speech name a particular voice has not got simply does not play, and a patrol car
        /// that is quiet for one pass is not a bug worth writing code to prevent.
        /// </summary>
        private static readonly string[] Barks =
        {
            "COP_ARREST_PLAYER", "GENERIC_CURSE_MED", "CHASE_SOLO",
            "SURROUNDED", "COP_HELI_MEGAPHONE", "GENERIC_INSULT_MED"
        };

        /// <summary>Which sets get driven past. Everybody else's blocks are not our business.</summary>
        private static readonly string[] Watched = { "families", "ballas", "vagos" };

        private readonly Settings _cfg;
        private readonly GangRegistry _gangs;
        private readonly TurfWatch _turf;
        private readonly PlayerState _state;
        private readonly Random _rng = new Random();

        private readonly List<Cruiser> _out = new List<Cruiser>();

        private int _lastTick;
        private int _nextSpawn;

        /// <summary>Off while something louder is happening. Wired by the house script.</summary>
        public Func<bool> Busy;

        /// <summary>Somewhere they like to sit, if the house script names one.</summary>
        public Vector3 Doorstep;

        private bool Enabled => _cfg == null || _cfg.PatrolsEnabled;
        private int MaxCars => _cfg == null ? 1 : _cfg.PatrolCars;

        public Patrol(Settings cfg, GangRegistry gangs, TurfWatch turf, PlayerState state)
        {
            _cfg = cfg;
            _gangs = gangs;
            _turf = turf;
            _state = state;
        }

        // ---- per-tick ----------------------------------------------------------

        public void Update()
        {
            var now = Game.GameTime;
            if (now - _lastTick < TickMs) return;
            _lastTick = now;

            try
            {
                Prune(now);

                if (!Enabled)
                {
                    if (_out.Count > 0)
                    {
                        foreach (var car in _out) Release(car);
                        _out.Clear();
                    }

                    return;
                }

                var player = Game.Player.Character;
                if (player == null || !player.Exists() || !player.IsAlive) return;

                for (var i = _out.Count - 1; i >= 0; i--)
                {
                    if (!Steer(_out[i], now, player)) continue;

                    Release(_out[i]);
                    _out.RemoveAt(i);
                }

                if (!OnAGangBlock()) return;
                if (Busy != null && Busy()) return;
                if (_out.Count >= MaxCars) return;

                if (_nextSpawn == 0) _nextSpawn = now + _rng.Next(GapMinMs, GapMaxMs);
                if (now < _nextSpawn) return;

                _nextSpawn = now + _rng.Next(GapMinMs, GapMaxMs);
                Send(player);
            }
            catch (Exception ex)
            {
                Log.Debug("Patrol tripped: " + ex.Message);
            }
        }

        // ---- what one car does -------------------------------------------------

        /// <returns>True when this one has been given up on and should be let go.</returns>
        private bool Steer(Cruiser car, int now, Ped player)
        {
            if (car.Car == null || !car.Car.Exists()) return false;

            Chatter(car, now);

            switch (car.Phase)
            {
                case PatrolPhase.Searching: return Search(car, now, player);
                case PatrolPhase.Coming: return Coming(car, now, player);
            }

            // A gun in your hand, on a street they are on, in sight of them. Checked before
            // anything else a rolling car does, because it is the one thing that stops it.
            if (Armed(player) && Sees(car, player)) return Stop(car, now, player);

            if (car.Phase == PatrolPhase.Sitting)
            {
                if (now < car.SitUntil) return false;

                car.Phase = PatrolPhase.Rolling;
                car.Nudges = 0;
                Aim(car, now);
                return false;
            }

            var here = car.Car.Position;

            if (car.Target != Vector3.Zero && here.DistanceTo(car.Target) < ArrivedRange)
            {
                if (car.StopThere)
                {
                    car.Phase = PatrolPhase.Sitting;
                    car.SitUntil = now + _rng.Next(SitMinMs, SitMaxMs);
                    return false;
                }

                car.Nudges = 0;
                Aim(car, now);
                return false;
            }

            if (now - car.LookedAt < StuckLookMs) return false;

            var moved = car.LookedAt == 0 ? float.MaxValue : here.DistanceTo(car.WasAt);

            car.WasAt = here;
            car.LookedAt = now;

            if (moved > StuckMoved) return false;

            car.Nudges++;
            if (car.Nudges > MaxNudges) return true;

            Aim(car, now);
            return false;
        }

        /// <summary>
        /// Pulls up and sends one of them over.
        ///
        /// The car stops where it is rather than parking properly, which is what a car does
        /// when the man in it has decided something.
        /// </summary>
        private bool Stop(Cruiser car, int now, Ped player)
        {
            car.Phase = PatrolPhase.Coming;
            car.SearchDone = now + ChasePatienceMs;
            car.Target = Vector3.Zero;

            var who = car.Crew.Count > 1 && Alive(car.Crew[1]) ? car.Crew[1] : car.Driver;
            car.OnFoot = who;

            try
            {
                if (Alive(car.Driver))
                {
                    Function.Call(Hash.CLEAR_PED_TASKS, car.Driver.Handle);
                }

                Chirp(car);
                Bark(who ?? car.Driver);

                if (Alive(who))
                {
                    Function.Call(Hash.TASK_LEAVE_VEHICLE, who.Handle, car.Car.Handle, 0);
                    Function.Call(Hash.SET_PED_KEEP_TASK, who.Handle, true);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Patrol could not stop: " + ex.Message);
            }

            Notify.Ticker("~o~That's a gun in your hand.~s~ They've seen it.");
            return false;
        }

        /// <summary>Walking over. Gives up if you have gone, or if he cannot get to you.</summary>
        private bool Coming(Cruiser car, int now, Ped player)
        {
            var who = car.OnFoot;

            if (!Alive(who))
            {
                car.Phase = PatrolPhase.Rolling;
                car.OnFoot = null;
                Aim(car, now);
                return false;
            }

            var away = who.Position.DistanceTo(player.Position);

            if (now > car.SearchDone || away > NoticeRange * 2.5f)
            {
                // Lost you, or you drove off. Back in the car and on with the round.
                Back(car, now);
                return false;
            }

            if (away <= SearchRange)
            {
                car.Phase = PatrolPhase.Searching;
                car.SearchDone = now + SearchMs;

                try
                {
                    Function.Call(Hash.CLEAR_PED_TASKS, who.Handle);
                    Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, who.Handle, player.Handle, 2000);
                }
                catch { /* he can search you sideways */ }

                Bark(who);
                Notify.Important("~o~Hands where he can see them.~s~");
                return false;
            }

            try
            {
                if (!Function.Call<bool>(Hash.GET_IS_TASK_ACTIVE, who.Handle, 224))
                {
                    Function.Call(Hash.TASK_GO_TO_ENTITY, who.Handle, player.Handle,
                                  -1, SearchRange * 0.7f, 2.2f, 1073741824, 0);
                }
            }
            catch { /* he will find his own way over */ }

            return false;
        }

        /// <summary>
        /// Takes what you are holding.
        ///
        /// Guns and product, and nothing else. Not your money, not an arrest, no stars -- this
        /// is a street search that goes badly rather than a bust, and the mod already has a bust
        /// that does the rest. What makes it hurt is that it takes the thing you were carrying
        /// TO somewhere, which is the whole evening.
        /// </summary>
        private bool Search(Cruiser car, int now, Ped player)
        {
            if (!Alive(car.OnFoot))
            {
                car.Phase = PatrolPhase.Rolling;
                car.OnFoot = null;
                Aim(car, now);
                return false;
            }

            if (player.Position.DistanceTo(car.OnFoot.Position) > SearchRange * 2.2f)
            {
                // Walked off mid-search. He is not going to chase you twice.
                Back(car, now);
                return false;
            }

            if (now < car.SearchDone) return false;

            var took = 0f;
            var guns = false;

            try
            {
                guns = Function.Call<bool>(Hash.IS_PED_ARMED, player.Handle, 7);
                Function.Call(Hash.REMOVE_ALL_PED_WEAPONS, player.Handle, true);
            }
            catch { /* he found nothing */ }

            if (_state != null && _state.Stash != null)
            {
                took = _state.Stash.Total;
                if (took > 0.005f) _state.Stash.Clear();

                _state.Touch();
            }

            Bark(car.OnFoot);

            if (took > 0.005f && guns)
            {
                Notify.Failure("they took your piece and " + took.ToString("0.#") + "g off you.");
            }
            else if (took > 0.005f)
            {
                Notify.Failure("they took " + took.ToString("0.#") + "g off you.");
            }
            else if (guns)
            {
                Notify.Failure("they took your piece.");
            }
            else
            {
                Notify.Ticker("~o~Nothing on you.~s~ On your way.");
            }

            Log.Info("Patrol searched the player: guns=" + guns + ", " + took.ToString("0.#") + "g.");

            Back(car, now);
            return false;
        }

        /// <summary>Back in the car and on with the round.</summary>
        private void Back(Cruiser car, int now)
        {
            var who = car.OnFoot;

            car.OnFoot = null;
            car.Phase = PatrolPhase.Rolling;
            car.Nudges = 0;

            try
            {
                if (Alive(who) && car.Car != null && car.Car.Exists())
                {
                    Function.Call(Hash.CLEAR_PED_TASKS, who.Handle);
                    Function.Call(Hash.TASK_ENTER_VEHICLE, who.Handle, car.Car.Handle,
                                  20000, 0, 2f, 1, 0);
                }
            }
            catch { /* he can walk it off */ }

            Aim(car, now);
        }

        // ---- the three things that make it a patrol car ------------------------

        /// <summary>A word over the speaker and a half-second of siren, now and then.</summary>
        private void Chatter(Cruiser car, int now)
        {
            if (car.NextBark == 0) car.NextBark = now + _rng.Next(BarkGapMinMs, BarkGapMaxMs);
            if (car.NextChirp == 0) car.NextChirp = now + _rng.Next(ChirpGapMinMs, ChirpGapMaxMs);

            if (now >= car.NextBark)
            {
                car.NextBark = now + _rng.Next(BarkGapMinMs, BarkGapMaxMs);
                Bark(car.Driver);
            }

            if (now >= car.NextChirp)
            {
                car.NextChirp = now + _rng.Next(ChirpGapMinMs, ChirpGapMaxMs);
                Chirp(car);
            }

            // And turns itself off half a second later, which is the whole difference between
            // a car announcing itself at a junction and a car chasing somebody.
            if (car.SirenUntil != 0 && now >= car.SirenUntil)
            {
                car.SirenUntil = 0;

                try
                {
                    if (car.Car != null && car.Car.Exists())
                    {
                        Function.Call(Hash.SET_VEHICLE_SIREN, car.Car.Handle, false);
                    }
                }
                catch { /* it will time out on its own */ }
            }
        }

        private void Bark(Ped who)
        {
            if (!Alive(who)) return;

            try
            {
                Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, who.Handle,
                              Barks[_rng.Next(Barks.Length)], "SPEECH_PARAMS_FORCE_MEGAPHONE");
            }
            catch
            {
                // A line his voice has not got costs nothing.
            }
        }

        private void Chirp(Cruiser car)
        {
            if (car.Car == null || !car.Car.Exists()) return;

            try
            {
                Function.Call(Hash.SET_VEHICLE_SIREN, car.Car.Handle, true);
                car.SirenUntil = Game.GameTime + ChirpMs;
            }
            catch
            {
                // No siren, no chirp.
            }
        }

        /// <summary>
        /// The light, drawn rather than switched on.
        ///
        /// SET_VEHICLE_SEARCHLIGHT is a helicopter's, and a police car has no light the game
        /// will turn on for you. DRAW_SPOT_LIGHT puts one wherever you ask for the frame you
        /// ask, so the beam is ours: out of the driver's window, at whatever they are looking
        /// at, eased toward it rather than snapped so it reads as somebody swinging it.
        ///
        /// Drawn from Draw rather than from the tick, because a light that exists for one frame
        /// in nine is a strobe.
        /// </summary>
        public void Draw()
        {
            if (!Enabled || _out.Count == 0) return;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            foreach (var car in _out)
            {
                if (car.Car == null || !car.Car.Exists()) continue;
                if (car.Car.Position.DistanceTo(player.Position) > 90f) continue;

                try
                {
                    var want = car.Phase == PatrolPhase.Rolling && car.Target != Vector3.Zero
                        ? car.Target
                        : player.Position;

                    // Eased. A beam that jumps to a new target between two frames is a light
                    // being teleported; one that swings is a light being aimed.
                    car.Beam = car.Beam == Vector3.Zero ? want : car.Beam + (want - car.Beam) * 0.06f;

                    var from = car.Car.Position
                               + car.Car.ForwardVector * 1.1f
                               + car.Car.RightVector * -0.9f
                               + new Vector3(0f, 0f, 0.75f);

                    var dir = car.Beam - from;
                    if (dir.Length() < 0.5f) continue;

                    dir.Normalize();

                    Function.Call(Hash.DRAW_SPOT_LIGHT,
                                  from.X, from.Y, from.Z,
                                  dir.X, dir.Y, dir.Z,
                                  235, 240, 255,
                                  38f, 14f, 0f, 11f, 1f);
                }
                catch
                {
                    // No light this frame.
                }
            }
        }

        // ---- where they go -----------------------------------------------------

        private void Aim(Cruiser car, int now)
        {
            if (car.Car == null || !car.Car.Exists()) return;
            if (!Alive(car.Driver)) return;

            var stop = _rng.Next(100) < StopChancePercent;

            // Every so often the place they pull up is a particular doorstep rather than
            // wherever the road network offered. Nothing happens there; that is the point.
            var where = stop && Doorstep != Vector3.Zero && _rng.Next(100) < 25
                ? Doorstep
                : Node(car.Car.Position, stop);

            if (where == Vector3.Zero)
            {
                try
                {
                    Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, car.Driver.Handle,
                                  car.Car.Handle, Cruise, Style);

                    car.Target = Vector3.Zero;
                    car.StopThere = false;
                    car.LookedAt = now;
                    car.WasAt = car.Car.Position;
                }
                catch { /* looked at again in nine seconds */ }

                return;
            }

            car.Target = where;
            car.StopThere = stop;
            car.LookedAt = now;
            car.WasAt = car.Car.Position;

            try
            {
                Function.Call(Hash.CLEAR_PED_TASKS, car.Driver.Handle);

                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, car.Driver.Handle, car.Car.Handle,
                              where.X, where.Y, where.Z, Cruise, 0, car.Car.Model.Hash,
                              Style, stop ? 5f : 15f, true);

                Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, car.Driver.Handle, Cruise);
                Function.Call(Hash.SET_PED_KEEP_TASK, car.Driver.Handle, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not send a patrol on: " + ex.Message);
            }
        }

        /// <summary>
        /// A point on the road network near them, on somebody's blocks.
        ///
        /// The same trick the set's own cars use: the game marks a node GPS-allowed when it is
        /// a road the satnav would route down, so the ones it will not route down are the
        /// service roads and the cut-throughs behind the buildings. A patrol car that only ever
        /// drove the main road would be a car on the main road.
        /// </summary>
        private Vector3 Node(Vector3 from, bool wantAlley)
        {
            for (var tries = 0; tries < 12; tries++)
            {
                var probe = from.Around(50f + (float)_rng.NextDouble() * 140f);

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
                    if (!OnAGangBlock(at)) continue;

                    var backstreet = !Function.Call<bool>(Hash.GET_VEHICLE_NODE_IS_GPS_ALLOWED, id);
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

        private bool OnAGangBlock()
        {
            var owner = _turf == null ? null : _turf.Owner;
            return owner != null && Watches(owner.Id);
        }

        private bool OnAGangBlock(Vector3 at)
        {
            try
            {
                var code = Function.Call<string>(Hash.GET_NAME_OF_ZONE, at.X, at.Y, at.Z) ?? "";
                var owner = _gangs.OwnerOfZone(code);

                return owner != null && Watches(owner.Id);
            }
            catch
            {
                return false;
            }
        }

        private static bool Watches(string gangId)
        {
            foreach (var id in Watched)
            {
                if (string.Equals(id, gangId, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        // ---- seeing you --------------------------------------------------------

        /// <summary>
        /// Whether there is a gun in your hand right now.
        ///
        /// Flag 7 is pistols, long guns and thrown together, which is the set of things a
        /// passing car would react to. Fists and a phone are not a gun and neither is a weapon
        /// you own but have not got out.
        /// </summary>
        private static bool Armed(Ped player)
        {
            try
            {
                // Flag 7 is pistols, long guns and thrown together, and it answers for what is
                // IN HIS HANDS rather than for what he owns -- put it away and this goes false,
                // which is the behaviour that makes putting it away worth doing.
                return Function.Call<bool>(Hash.IS_PED_ARMED, player.Handle, 7);
            }
            catch
            {
                return false;
            }
        }

        private static bool Sees(Cruiser car, Ped player)
        {
            try
            {
                if (car.Car.Position.DistanceTo(player.Position) > NoticeRange) return false;
                if (!Alive(car.Driver)) return false;

                return Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY,
                                           car.Driver.Handle, player.Handle, 17);
            }
            catch
            {
                return false;
            }
        }

        private static bool Alive(Ped who) => who != null && who.Exists() && who.IsAlive;

        // ---- putting one out ---------------------------------------------------

        private void Send(Ped player)
        {
            var spawn = Somewhere(player);
            if (spawn == Vector3.Zero) return;

            var car = new Cruiser { BornAt = Game.GameTime };

            try
            {
                car.Car = Make(spawn);
                if (car.Car == null) return;

                for (var seat = -1; seat < 1; seat++)
                {
                    var cop = Fill(car.Car, seat);
                    if (cop == null) continue;

                    if (seat == -1) car.Driver = cop;
                    car.Crew.Add(cop);
                }

                if (car.Driver == null)
                {
                    Scrap(car);
                    return;
                }

                _out.Add(car);
                Aim(car, Game.GameTime);

                Log.Info("Patrol out on " + (_turf == null ? "the block" : _turf.ZoneName) + ".");
            }
            catch (Exception ex)
            {
                Log.Debug("Patrol could not put one out: " + ex.Message);
                Scrap(car);
                _out.Remove(car);
            }
        }

        private Vector3 Somewhere(Ped player)
        {
            for (var tries = 0; tries < 12; tries++)
            {
                try
                {
                    var away = SpawnNear + (float)_rng.NextDouble() * (SpawnFar - SpawnNear);
                    var at = World.GetNextPositionOnStreet(player.Position.Around(away), true);

                    if (at == Vector3.Zero) continue;
                    if (at.DistanceTo(player.Position) < SpawnNear) continue;
                    if (!OnAGangBlock(at)) continue;

                    return at;
                }
                catch
                {
                    // Next try.
                }
            }

            return Vector3.Zero;
        }

        private Vehicle Make(Vector3 at)
        {
            foreach (var name in Cars)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) continue;

                    var car = World.CreateVehicle(model, at);
                    model.MarkAsNoLongerNeeded();

                    if (car == null || !car.Exists()) continue;

                    car.IsPersistent = true;

                    Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, car.Handle);
                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, car.Handle, true, true);

                    // Lights on, siren off. The light is what you see coming; the siren is
                    // saved for the half-second they want you to hear it.
                    Function.Call(Hash.SET_VEHICLE_LIGHTS, car.Handle, 2);
                    Function.Call(Hash.SET_VEHICLE_SIREN, car.Handle, false);

                    return car;
                }
                catch (Exception ex)
                {
                    Log.Debug("Patrol could not make a " + name + ": " + ex.Message);
                }
            }

            return null;
        }

        private Ped Fill(Vehicle car, int seat)
        {
            try
            {
                var name = Cops[_rng.Next(Cops.Length)];

                var model = new Model(name);
                if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) return null;

                var handle = Function.Call<int>(Hash.CREATE_PED_INSIDE_VEHICLE, car.Handle,
                                                PedTypeCop, model.Hash, seat, false, false);

                model.MarkAsNoLongerNeeded();
                if (handle == 0) return null;

                var cop = Entity.FromHandle(handle) as Ped;
                if (cop == null || !cop.Exists()) return null;

                cop.IsPersistent = true;

                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, cop.Handle, true, true);
                Function.Call(Hash.SET_PED_AS_COP, cop.Handle, true);
                Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, cop.Handle, false);

                return cop;
            }
            catch (Exception ex)
            {
                Log.Debug("Patrol could not fill a seat: " + ex.Message);
                return null;
            }
        }

        // ---- clearing up -------------------------------------------------------

        private void Prune(int now)
        {
            var player = Game.Player.Character;

            for (var i = _out.Count - 1; i >= 0; i--)
            {
                var car = _out[i];

                var gone = car.Car == null || !car.Car.Exists();
                var noDriver = !Alive(car.Driver);

                var old = now - car.BornAt > LifetimeMs;

                var far = !gone && player != null && player.Exists()
                          && car.Car.Position.DistanceTo(player.Position) > LetGoRange;

                if (gone || noDriver || old || far)
                {
                    Release(car);
                    _out.RemoveAt(i);
                }
            }
        }

        private void Release(Cruiser car)
        {
            try
            {
                foreach (var cop in car.Crew)
                {
                    if (cop == null || !cop.Exists()) continue;

                    cop.IsPersistent = false;
                    cop.MarkAsNoLongerNeeded();
                }

                if (car.Car != null && car.Car.Exists())
                {
                    Function.Call(Hash.SET_VEHICLE_SIREN, car.Car.Handle, false);

                    car.Car.IsPersistent = false;
                    car.Car.MarkAsNoLongerNeeded();
                }
            }
            catch
            {
                // Letting go of something already gone.
            }
        }

        private void Scrap(Cruiser car)
        {
            try
            {
                foreach (var cop in car.Crew)
                {
                    if (cop != null && cop.Exists()) cop.Delete();
                }

                if (car.Car != null && car.Car.Exists()) car.Car.Delete();
            }
            catch
            {
                // Nothing left to scrap.
            }
        }

        /// <summary>Everything off the street, for a reload.</summary>
        public void RestoreWorld()
        {
            foreach (var car in _out) Scrap(car);
            _out.Clear();
        }
    }
}
