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
        Searching,

        /// <summary>
        /// Pulled over because you gave them the finger.
        ///
        /// Not a stop and not a search -- they cannot do you for it and both of you know it.
        /// They put the light on you, chirp the siren, say something through the loudspeaker
        /// and drive off, which is exactly the amount of nothing that actually happens.
        /// </summary>
        Told
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

        /// <summary>When they have had enough and pull away again.</summary>
        public int ToldUntil;

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

        /// <summary>
        /// How long the search itself takes once he is stood over you.
        ///
        /// Up from 2.6 seconds, which was long enough for a timer and too short for a scene.
        /// There is an animation to watch now and a decision to make while it runs -- you can
        /// still walk off mid-search -- and neither of those fits in under three seconds.
        /// </summary>
        private const int SearchMs = 4500;

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

        /// <summary>
        /// What comes out of the loudspeaker when you flip them off.
        ///
        /// Their own voices through the PA rather than lines of ours, on the same
        /// SPEECH_PARAMS_FORCE_MEGAPHONE the searches already use -- so it arrives sounding
        /// like it came out of the car instead of out of a man stood next to it.
        /// </summary>
        private static readonly string[] Insults =
        {
            "GENERIC_INSULT_HIGH", "GENERIC_INSULT_MED", "GENERIC_CURSE_HIGH",
            "GENERIC_CURSE_MED", "COP_ARREST_PLAYER"
        };

        /// <summary>Close enough to be worth doing, and close enough for them to see it.</summary>
        private const float FingerRange = 25f;

        /// <summary>How long they sit there making the point before pulling away.</summary>
        private const int ToldMs = 7000;

        /// <summary>Camera ease on and off the car, either side of the hold.</summary>
        private const int HintEase = 900;

        /// <summary>
        /// The gesture. Checked against the game's own animation data, not guessed.
        ///
        /// It is the one from Online, which is the only place the base game keeps it -- there
        /// is no single-player clip of a man doing this to anybody.
        ///
        /// Three clips, not one, because that is how the dictionary is built: the arm goes up,
        /// it stays up, it comes down. Playing the middle one on its own is the pose appearing
        /// on him between two frames, which reads as a glitch rather than a gesture.
        /// </summary>
        /// <summary>
        /// ONE arm, not two.
        ///
        /// anim@mp_player_intupperfinger is Up Yours, and Up Yours is a man throwing both arms
        /// in the air. Right sentiment, wrong number of hands -- what this wants is somebody
        /// putting a finger up at a car going past without making a performance of it.
        ///
        /// The celebration set is the single-handed one, and it comes in a male and a female
        /// cut because it is animated per body rather than retargeted. One clip, no enter and
        /// no exit, which is why the three-stage walk below went with the old dictionary.
        /// </summary>
        private const string FingerDictMale = "anim@mp_player_intcelebrationmale@finger";
        private const string FingerDictFemale = "anim@mp_player_intcelebrationfemale@finger";
        private const string FingerClip = "finger";

        /// <summary>
        /// How long before ANY car can be told again.
        ///
        /// Deliberately one clock for the lot rather than one per car: it is a gesture, and a
        /// man stood on a corner working his way down a line of squad cars is a button being
        /// spammed. Longer than a stop lasts, so it also guarantees the previous car has
        /// finished and pulled away before another one can be started.
        /// </summary>
        private const int FingerCooldownMs = 11000;

        private int _lastFinger;
        private bool _fingerHeld;

        /// <summary>
        /// Whether something else on screen owns the button right now.
        ///
        /// Right on the d-pad is the mod's talk-to-somebody button everywhere else, so a cop
        /// car rolling past a man stood in front of Gerald must not turn "talk to Gerald" into
        /// a gesture at the police. Set by Main, which is the only thing that knows what is up.
        /// </summary>
        public Func<bool> Occupied;

        /// <summary>
        /// A police car somebody else is running that should also be flip-off-able.
        ///
        /// The patrol that eases past while you are stood on a corner is PostUp's, not this
        /// class's -- it is part of the dealing loop, dispatched by heat and driven on its own
        /// schedule. So the one police car you most want to put a finger up at was the only one
        /// in the game you could not, because Closest only ever looked at cars this system had
        /// sent out itself.
        ///
        /// Borrowed rather than adopted. We do not own it, we do not stop it and we do not put
        /// it into a phase -- PostUp is still driving, and a car halted out from under its own
        /// state machine is a bug in somebody else's file. What it gets is the reaction: the
        /// hand, a chirp, something said over the speaker and the light swung onto you.
        /// </summary>
        public Func<Vehicle> Passing;

        /// <summary>Refilled rather than allocated. There is only ever one borrowed car.</summary>
        private readonly Cruiser _borrowed = new Cruiser();

        /// <summary>When a borrowed car's siren goes back off. Nothing else ticks it.</summary>
        private int _borrowedSirenOff;

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
                case PatrolPhase.Told: return Told(car, now);
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
                // Asked for on approach rather than on arrival, so they have the walk to load.
                StopSearch.Want();

                car.Phase = PatrolPhase.Searching;
                car.SearchDone = now + SearchMs;

                try
                {
                    Function.Call(Hash.CLEAR_PED_TASKS, who.Handle);
                    Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, who.Handle, player.Handle, 2000);
                }
                catch { /* he can search you sideways */ }

                // The same scene the corner stop plays, because it is the same event: your
                // hands go up and he goes through your pockets. Walking off during it is still
                // allowed -- the check above sends him back to the car if you do.
                StopSearch.HandsUp(player, SearchMs);
                StopSearch.Frisk(who);

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

            // A ROADSIDE STOP DOES NOT TAKE YOUR GUNS.
            //
            // It used to empty the lot -- every weapon and every round, to a patrol car that
            // happened to roll past while you were standing still. That is a different event
            // from the one it looks like: the post-up bust is a consequence of a risk you
            // chose to keep taking, and losing your arsenal to it is the price of that. This
            // is a car driving by. Getting stripped of everything you own by traffic is not a
            // setback, it is an ambush, and there is nothing you could have done differently.
            //
            // The product still goes. That is what they were looking for.

            if (_state != null && _state.Stash != null)
            {
                took = _state.Stash.Total;
                if (took > 0.005f) _state.Stash.Clear();

                _state.Touch();
            }

            Bark(car.OnFoot);

            if (took > 0.005f)
            {
                Notify.Failure("they took " + took.ToString("0.#") + "g off you.");
            }
            else
            {
                Notify.Ticker("~o~Nothing on you.~s~ On your way.");
            }

            Log.Info("Patrol searched the player: " + took.ToString("0.#") + "g (guns left alone).");

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
                // Same guard the drive-by needed. A man already in the car is not told to
                // get into it -- the game services that by putting him out through the door
                // first, so the order to come back is what sends him away.
                if (Alive(who) && car.Car != null && car.Car.Exists() &&
                    !who.IsInVehicle(car.Car))
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
            Bark(who, Barks);
        }

        private void Bark(Ped who, string[] pool)
        {
            if (!Alive(who) || pool == null || pool.Length == 0) return;

            try
            {
                Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, who.Handle,
                              pool[_rng.Next(pool.Length)], "SPEECH_PARAMS_FORCE_MEGAPHONE");
            }
            catch
            {
                // A line his voice has not got costs nothing.
            }
        }

        /// <summary>
        /// Sitting there making their point, then leaving.
        ///
        /// Nothing happens to you, and that is the joke. There is nothing they can do a man for
        /// over a hand, so what they CAN do is stop, put the light on him, make a noise and say
        /// something -- and then they have to drive off again. That is the whole exchange, and
        /// dressing it up as anything more (a search, a wanted level, a chase) would be the mod
        /// deciding the gesture was a crime, which is the opposite of the point.
        /// </summary>
        private bool Told(Cruiser car, int now)
        {
            if (now < car.ToldUntil) return false;

            // Enough said. Back on the round.
            car.Phase = PatrolPhase.Rolling;
            car.Nudges = 0;

            // Released explicitly rather than left to expire. The halt and the ToldMs above are
            // two clocks for the same thing, and a car whose halt outlives its phase by even a
            // few frames is a car sat still with somewhere to be.
            try { Function.Call(Hash.STOP_BRINGING_VEHICLE_TO_HALT, car.Car.Handle); }
            catch { /* it lapses on its own */ }

            StopWatching();
            Aim(car, now);
            return false;
        }

        /// <summary>
        /// Whether this car can be given the finger right now.
        ///
        /// Rolling or sitting only. A car already coming for you, or already stood over you
        /// going through your pockets, is having a different conversation -- interrupting it
        /// with this would replace a phase that is in the middle of something.
        /// </summary>
        private static bool CanBeTold(Cruiser car)
        {
            return car.Phase == PatrolPhase.Rolling || car.Phase == PatrolPhase.Sitting;
        }

        /// <summary>
        /// The gesture, and everything it sets off.
        ///
        /// Franklin does NOT whistle here. A whistle is how you ask a taxi for something, and
        /// this is the opposite of asking -- so it is the finger, upper body, played over
        /// whatever he happens to be stood doing.
        /// </summary>
        private void GiveThemThe(Cruiser car, Ped player, int now)
        {
            _lastFinger = now;

            var ours = !ReferenceEquals(car, _borrowed);

            if (ours)
            {
                car.Phase = PatrolPhase.Told;
                car.ToldUntil = now + ToldMs;

                // Halted where they are rather than tasked to park somewhere. They have pulled
                // up to have a word, not to attend anything.
                try
                {
                    Function.Call(Hash.BRING_VEHICLE_TO_HALT, car.Car.Handle, 6f, ToldMs, false);
                }
                catch
                {
                    // They will roll to a stop on their own.
                }
            }
            else
            {
                // Somebody else's car, so somebody else's driving. The siren is switched on
                // here and off in Draw, because the per-car timer that does it for ours only
                // runs over cars this system is holding.
                _borrowedSirenOff = now + ChirpMs;
            }

            Hand(player);

            // The light already swings to whatever a non-rolling car is looking at, so leaving
            // Rolling aims it at him for free -- see Draw. The noise and the voice are not free.
            Chirp(car);
            Bark(Talker(car), Insults);

            Watch(car.Car);

            Log.Info(ours ? "Flipped off a patrol car."
                          : "Flipped off a passing patrol at the corner.");
        }

        /// <summary>
        /// The arm goes up, once.
        ///
        /// One clip rather than the raise-hold-drop it used to be. That machinery existed
        /// because Up Yours ships as three clips and issuing them together plays only the last;
        /// the one-handed version is a single gesture with its own beginning and end, so there
        /// is nothing left to sequence and the whole stage walk went with it.
        ///
        /// The dict is requested here and played here, which is a race the first time and only
        /// the first time -- REQUEST_ANIM_DICT is asynchronous, so a cold dictionary costs the
        /// player the animation on his first press and nothing after that. Worth it against
        /// holding a streaming request open for a gesture nobody may ever use.
        /// </summary>
        private void Hand(Ped player)
        {
            var dict = Female(player) ? FingerDictFemale : FingerDictMale;

            try
            {
                if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dict))
                {
                    Function.Call(Hash.REQUEST_ANIM_DICT, dict);
                }
            }
            catch
            {
                // It will be there by the time it is asked for again.
            }

            Clip(player, dict, FingerClip, 48);
        }

        /// <summary>Which of the two cuts to play. Franklin is not the only man this can be.</summary>
        private static bool Female(Ped player)
        {
            try { return !Function.Call<bool>(Hash.IS_PED_MALE, player.Handle); }
            catch { return false; }
        }

        /// <summary>
        /// One clip, upper body, over whatever he is already doing.
        ///
        /// Upper body and secondary so his legs stay his own: he can keep walking through the
        /// whole thing, and being able to walk away mid-gesture is the correct way to do this
        /// to a police car.
        /// </summary>
        private static void Clip(Ped player, string dict, string clip, int flags, int ms = -1)
        {
            try
            {
                Function.Call(Hash.TASK_PLAY_ANIM, player.Handle, dict, clip,
                              8f, -8f, ms, flags, 0f, false, false, false);
            }
            catch
            {
                // He meant it anyway.
            }
        }

        /// <summary>Whoever is in the car to do the talking.</summary>
        private static Ped Talker(Cruiser car)
        {
            if (Alive(car.Driver)) return car.Driver;

            if (car.Crew != null)
            {
                foreach (var cop in car.Crew)
                {
                    if (Alive(cop)) return cop;
                }
            }

            return null;
        }

        /// <summary>
        /// Points the camera at them, the way the game does when a taxi pulls up.
        ///
        /// A gameplay HINT rather than a camera of our own: it borrows the player's camera,
        /// eases onto the entity, holds it, and hands it back -- without ever taking control
        /// away. Anything hand-rolled would be a cutscene, and this is not one; you can walk
        /// off in the middle of it, which you should be able to.
        /// </summary>
        private static void Watch(Vehicle car)
        {
            if (car == null || !car.Exists()) return;

            try
            {
                Function.Call(Hash.SET_GAMEPLAY_ENTITY_HINT, car.Handle,
                              0f, 0f, 0.5f, true, ToldMs - HintEase, HintEase, HintEase, 0);
            }
            catch
            {
                // The light is enough on its own.
            }
        }

        private static void StopWatching()
        {
            try
            {
                if (Function.Call<bool>(Hash.IS_GAMEPLAY_HINT_ACTIVE))
                {
                    Function.Call(Hash.STOP_GAMEPLAY_HINT, false);
                }
            }
            catch
            {
                // It times out on its own.
            }
        }

        /// <summary>Puts a borrowed car's siren back off. Ours are handled per car in Draw.</summary>
        private void BorrowedSiren()
        {
            if (_borrowedSirenOff == 0 || Game.GameTime < _borrowedSirenOff) return;

            _borrowedSirenOff = 0;

            try
            {
                if (_borrowed.Car != null && _borrowed.Car.Exists())
                {
                    Function.Call(Hash.SET_VEHICLE_SIREN, _borrowed.Car.Handle, false);
                }
            }
            catch { /* it will time out on its own */ }
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
            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            // BEFORE the early return, and that is the whole of the second half of this fix.
            //
            // Draw used to give up the moment this system had no cars out, which is exactly
            // the state you are in while posted up: heat sends PostUp's patrol past you and
            // ours are somewhere else or switched off. So the prompt never appeared for the
            // one car in the game a man on a corner would actually want to do this to.
            //
            // Offer costs a distance check when there is nothing to tell.
            Offer(player);
            BorrowedSiren();

            if (!Enabled || _out.Count == 0) return;

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

        /// <summary>
        /// The prompt, and the press.
        ///
        /// Read every frame rather than on the tick, because a button offered nine frames out
        /// of ten is a button that does not work -- the tick is 700ms and a tap is not.
        /// </summary>
        private void Offer(Ped player)
        {
            // Read on EVERY path, not just the ones that get as far as offering. The edge is
            // "down now, up last frame", so a frame that skips the read entirely leaves a stale
            // "up" behind -- and a button held down through a menu fires the moment the menu
            // closes, which is not a press anybody made.
            var down = Down();
            var pressed = down && !_fingerHeld;
            _fingerHeld = down;

            if (player.IsInVehicle()) return;
            if (Game.Player.Wanted.WantedLevel > 0) return;
            if (Occupied != null && Occupied()) return;
            if (Core.InputGuard.Busy) return;

            var now = Game.GameTime;
            if (now - _lastFinger < FingerCooldownMs) return;

            var near = Closest(player);
            if (near == null) return;

            // And the game's own hail is held off while the prompt is up.
            //
            // Right on the d-pad is how you whistle a taxi down, and that is a vanilla
            // behaviour on the same button -- so pressing it did both: Franklin whistled AND
            // put his hand up, which is a man being friendly and rude at the same time and
            // reads as neither. Disabled rather than rebound, because the button is the one the
            // player was told to press.
            //
            // Down() reads IS_DISABLED_CONTROL_PRESSED as well as the live one, so disabling it
            // costs us nothing -- the gesture still fires, the whistle does not.
            Game.DisableControlThisFrame(Control.PhoneRight);

            Help.ShowThisFrame("Press ~INPUT_CELLPHONE_RIGHT~ to let them know how you feel.");

            if (pressed) GiveThemThe(near, player, now);
        }

        /// <summary>The nearest car that could be told, if any is close enough.</summary>
        private Cruiser Closest(Ped player)
        {
            Cruiser near = null;
            var nearest = FingerRange;

            foreach (var car in _out)
            {
                if (car.Car == null || !car.Car.Exists()) continue;
                if (!CanBeTold(car)) continue;
                if (!Alive(car.Driver)) continue;

                var away = car.Car.Position.DistanceTo(player.Position);
                if (away > nearest) continue;

                nearest = away;
                near = car;
            }

            // Ours first, always. A car we are driving can actually be made to stop.
            return near ?? Borrowed(player);
        }

        /// <summary>The passing car, if there is one and it is close enough to be told.</summary>
        private Cruiser Borrowed(Ped player)
        {
            if (Passing == null) return null;

            try
            {
                var car = Passing();
                if (car == null || !car.Exists()) return null;
                if (car.Position.DistanceTo(player.Position) > FingerRange) return null;

                var driver = car.GetPedOnSeat(VehicleSeat.Driver);
                if (!Alive(driver)) return null;

                _borrowed.Car = car;
                _borrowed.Driver = driver;

                return _borrowed;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Right on the d-pad.
        ///
        /// Deliberately NOT the Context button the other prompts also accept. Context is a
        /// crowded key and a stray press of it near a police car should not be this.
        /// </summary>
        private static bool Down()
        {
            try
            {
                return Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)Control.PhoneRight)
                    || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)Control.PhoneRight)
                    || Game.IsKeyPressed(System.Windows.Forms.Keys.Right);
            }
            catch
            {
                // An unreadable control is simply not pressed.
                return false;
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
            // A car let go of mid-word takes the camera with it otherwise: the hint outlives
            // the thing it was pointed at, and the player spends the rest of it looking at an
            // empty stretch of road.
            if (car.Phase == PatrolPhase.Told) StopWatching();

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
                    Function.Call(Hash.STOP_BRINGING_VEHICLE_TO_HALT, car.Car.Handle);

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
