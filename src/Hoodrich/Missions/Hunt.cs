using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Gangs;
using Hoodrich.UI;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.Missions
{
    /// <summary>Where a hunt is up to.</summary>
    internal enum HuntPhase
    {
        None = 0,

        /// <summary>Driving out to the ground with Lamar in the passenger seat.</summary>
        Riding,

        /// <summary>On foot, following tracks, looking for the next one.</summary>
        Tracking,

        /// <summary>One of them is wounded and running. Follow the blood.</summary>
        Bleeding,

        /// <summary>Three down. Back to the car.</summary>
        Leaving
    }

    /// <summary>
    /// THE HUNT.
    ///
    /// The game already has a hunting minigame and it is the best thing in it: a rifle, a set
    /// of tracks on the ground, a wind direction, and an animal that leaves if you are loud or
    /// upwind. This is that, with Lamar in the passenger seat and Ballas instead of elk.
    ///
    /// EVERY PIECE OF IT IS THE MINIGAME'S PIECE, in the same order:
    ///
    ///   THE RIFLE. He is handed one at the start and it goes when the job does. Hunting is
    ///   done at range or it is not hunting -- a shot from inside SpookRange is a mugging, and
    ///   the job says so and does not count it.
    ///
    ///   THE TRACKS. Every target lays a trail of footprints from where he came in to where he
    ///   is now, oldest faintest, and they are only drawn when you are close enough to be
    ///   reading the ground. Follow them and they lead to him.
    ///
    ///   THE WIND. The game has a wind direction and it is the whole reason the minigame is a
    ///   game. Come at him with it in your face and he hears nothing; come at him with it at
    ///   your back and he notices at twice the distance. There is an arrow on the card saying
    ///   which way it is going, and it is the same arrow the hunting HUD has.
    ///
    ///   THE STEALTH. Crouched is quiet, walking is not much worse, sprinting is a man
    ///   arriving. Line of sight matters more than anything else. It all feeds one number per
    ///   target, and when that number fills he is gone for good.
    ///
    ///   THE CLEAN KILL. A head shot drops him where he stands. Anything else and he runs
    ///   bleeding, and you follow the blood the same way you followed the tracks -- which is
    ///   exactly what a wounded elk does.
    ///
    ///   THE CALL. Lamar shouts something across the block and whoever is nearest comes to
    ///   look. That is the elk call, and it is the one thing in here he is actually useful for.
    /// </summary>
    internal sealed class Hunt
    {
        /// <summary>How many of them, and how far apart they start.</summary>
        private const int Many = 3;
        private const float Spread = 55f;

        /// <summary>How far out the ground itself is, from where he takes the job.</summary>
        private const float FieldRadius = 90f;

        /// <summary>Closer than this and it is not hunting, it is a mugging. See the note on the class.</summary>
        private const float TooClose = 22f;

        /// <summary>How far off he can be and still be tracked at all.</summary>
        private const float TrackRange = 180f;

        /// <summary>Prints are only drawn when you are near enough to be reading the ground.</summary>
        private const float PrintRange = 26f;

        /// <summary>How many prints a trail has, and how far apart.</summary>
        private const int PrintCount = 14;
        private const float PrintStride = 1.5f;

        /// <summary>How far he can see you when you are downwind of him, and how much worse it gets upwind.</summary>
        private const float SeeRange = 42f;
        private const float UpwindPenalty = 2.0f;

        /// <summary>What fills his suspicion per second, at the worst of it.</summary>
        private const float SeenPerSecond = 0.55f;
        private const float SprintPerSecond = 0.9f;
        private const float ShotSpike = 0.75f;

        /// <summary>And what he forgets per second when nothing is happening.</summary>
        private const float CalmPerSecond = 0.22f;

        /// <summary>Crouched, he hears half of it.</summary>
        private const float CrouchQuiet = 0.45f;

        /// <summary>How long the call holds his attention, and how long between calls.</summary>
        private const int CallLookMs = 9000;
        private const int CallEveryMs = 30000;

        /// <summary>How far the blood trail runs before he drops on his own.</summary>
        private const int BleedMs = 22000;

        /// <summary>The rifle he is lent, in the order an install has them.</summary>
        private static readonly string[] Rifles =
        {
            "WEAPON_SNIPERRIFLE", "WEAPON_MARKSMANRIFLE", "WEAPON_HEAVYSNIPER"
        };

        private const int Rounds = 20;

        /// <summary>Who is out there.</summary>
        private static readonly string[] Models =
        {
            "g_m_y_ballaeast_01", "g_m_y_ballaorig_01", "g_m_y_ballasout_01"
        };

        private sealed class Quarry
        {
            public Ped Man;
            public Blip Mark;

            /// <summary>Where he walked in from, which is where his tracks start.</summary>
            public Vector3 CameFrom;

            /// <summary>Nought to one. At one he is gone. See Watch.</summary>
            public float Suspicion;

            public bool Down;
            public bool Spooked;

            /// <summary>Hit and running. The blood is the trail now.</summary>
            public bool Bleeding;
            public int BleedFrom;
            public Vector3 LastBlood;

            /// <summary>When his tracks were last laid down, so they are not laid every frame.</summary>
            public int PrintedAt;

            /// <summary>Coming to look because Lamar shouted. See Call.</summary>
            public int LookingUntil;
        }

        private readonly Affiliation _crew;
        private readonly GangRegistry _gangs;
        private readonly Random _rng = new Random();

        private readonly List<Quarry> _out = new List<Quarry>();

        private MissionDef _def;
        private Vector3 _field;
        private Ped _lamar;

        private int _phaseFrom;
        private int _nextCall;
        private int _shotAt;
        private string _said = "";
        private int _saidAt;

        /// <summary>The rifle he was lent, so it can be taken back.</summary>
        private uint _lent;

        public HuntPhase Phase { get; private set; }

        public bool IsRunning => Phase != HuntPhase.None;

        public bool ReadyToCollect { get; private set; }

        public string Failure { get; private set; }

        /// <summary>Set by MissionRunner: Lamar, so the hunt does not have to make its own.</summary>
        public Func<Ped> Fixer;

        public int Down
        {
            get
            {
                var n = 0;
                foreach (var q in _out) if (q.Down) n++;
                return n;
            }
        }

        private int Lost
        {
            get
            {
                var n = 0;
                foreach (var q in _out) if (q.Spooked) n++;
                return n;
            }
        }

        public float Advance => Many <= 0 ? 0f : Down / (float)Many;

        public string Objective
        {
            get
            {
                switch (Phase)
                {
                    case HuntPhase.Riding: return "Get out to the block with Lamar";
                    case HuntPhase.Bleeding: return "Follow the blood";
                    case HuntPhase.Leaving: return "Get out of there";

                    default:
                        return "Track them down  --  " + Down + " of " + Many;
                }
            }
        }

        public Hunt(Affiliation crew, GangRegistry gangs)
        {
            _crew = crew;
            _gangs = gangs;
        }

        // ---- starting ---------------------------------------------------------------

        public string Start(MissionDef def)
        {
            if (def == null) return "nothing to hunt.";

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return "not right now.";

            Clear();

            _def = def;
            _field = new Vector3(def.X, def.Y, def.Z);

            if (_field == Vector3.Zero) _field = player.Position.Around(220f);

            Failure = null;
            ReadyToCollect = false;

            Rifle(player);

            Phase = HuntPhase.Riding;
            _phaseFrom = Game.GameTime;
            _nextCall = Game.GameTime + CallEveryMs;

            Say("get us out there. and go quiet when we're close");

            Log.Info("Hunt: on, at " + _field.X.ToString("0") + ", " + _field.Y.ToString("0") + ".");

            return null;
        }

        /// <summary>
        /// The rifle, and it is a loan.
        ///
        /// Written down so it can be taken back at the end. A job that hands out a sniper
        /// rifle and forgets about it is a job that has changed the rest of the save.
        /// </summary>
        private void Rifle(Ped player)
        {
            foreach (var name in Rifles)
            {
                try
                {
                    var hash = Function.Call<uint>(Hash.GET_HASH_KEY, name);
                    if (hash == 0) continue;

                    if (Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON, player.Handle, hash, false))
                    {
                        // He has his own. Nothing is lent and nothing will be taken.
                        _lent = 0;
                        Function.Call(Hash.SET_CURRENT_PED_WEAPON, player.Handle, hash, true);
                        return;
                    }

                    Function.Call(Hash.GIVE_WEAPON_TO_PED, player.Handle, hash, Rounds, false, false);
                    Function.Call(Hash.SET_CURRENT_PED_WEAPON, player.Handle, hash, true);

                    _lent = hash;
                    Log.Info("Hunt: lent him a " + name + ".");
                    return;
                }
                catch
                {
                    // Next name.
                }
            }
        }

        private void HandItBack()
        {
            if (_lent == 0) return;

            try
            {
                var player = Game.Player.Character;

                if (player != null && player.Exists())
                {
                    Function.Call(Hash.REMOVE_WEAPON_FROM_PED, player.Handle, _lent);
                }
            }
            catch
            {
                // He keeps it, then.
            }

            _lent = 0;
        }

        // ---- the tick ----------------------------------------------------------------

        public void Update()
        {
            if (!IsRunning) return;

            try
            {
                var player = Game.Player.Character;

                if (player == null || !player.Exists() || !player.IsAlive)
                {
                    Failure = "you didn't make it back.";
                    return;
                }

                var now = Game.GameTime;

                Lamar();

                // A SHOT IS NOTICED HERE rather than reported from outside. Everything the
                // hunt needs to know about is either the player firing or a target losing
                // blood, and both can be read off the world every tick -- so nothing else in
                // the mod has to know this job exists.
                try
                {
                    if (Function.Call<bool>(Hash.IS_PED_SHOOTING, player.Handle)) _shotAt = now;
                }
                catch
                {
                    // Then the only thing that gives him away is being seen.
                }

                switch (Phase)
                {
                    case HuntPhase.Riding: Riding(player, now); break;
                    case HuntPhase.Tracking: Tracking(player, now); break;
                    case HuntPhase.Bleeding: Tracking(player, now); break;
                    case HuntPhase.Leaving: Leaving(player, now); break;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("The hunt fell over: " + ex.Message);
                Failure = "it went wrong out there.";
            }
        }

        /// <summary>Out to the block. The ground is only laid once he is near enough to see it.</summary>
        private void Riding(Ped player, int now)
        {
            if (player.Position.DistanceTo(_field) > FieldRadius + 60f) return;

            Lay(player);

            Phase = HuntPhase.Tracking;
            _phaseFrom = now;

            Say("they're out here somewhere. read the ground");
        }

        /// <summary>
        /// The hunt itself: everybody watching, everybody being watched, and the tracks under
        /// your feet.
        /// </summary>
        private void Tracking(Ped player, int now)
        {
            var crouched = Crouching(player);
            var sprinting = player.IsSprinting;
            var wind = Wind();

            var anyBleeding = false;

            foreach (var q in _out)
            {
                if (q.Down || q.Spooked) continue;

                if (q.Man == null || !q.Man.Exists())
                {
                    q.Spooked = true;
                    continue;
                }

                if (!q.Man.IsAlive)
                {
                    Dropped(q, now);
                    continue;
                }

                // HIT AND STILL UP. The minigame's wounded elk: he runs, and the blood is
                // the trail from here on.
                if (!q.Bleeding && q.Man.Health < q.Man.MaxHealth - 20) Hurt(q, now);

                if (q.Bleeding)
                {
                    anyBleeding = true;
                    Bleed(q, now);
                    continue;
                }

                Watch(q, player, crouched, sprinting, wind, now);
                Prints(q, player, now);
            }

            Phase = anyBleeding ? HuntPhase.Bleeding : HuntPhase.Tracking;

            Called(player, now);

            if (Down >= Many)
            {
                Phase = HuntPhase.Leaving;
                _phaseFrom = now;
                Say("that's the three. we out");
                return;
            }

            // NOTHING LEFT TO HUNT. Spooking all of them is a way to lose this, and it is the
            // only one that is entirely your own doing.
            if (Down + Lost >= Many && Down < Many)
            {
                Failure = "you spooked the lot of them.";
            }
        }

        /// <summary>
        /// One man, deciding whether he has noticed you.
        ///
        /// FOUR THINGS FEED IT and they are the four the minigame uses: how close, whether he
        /// can see you, which way the wind is going, and how much noise you are making. The
        /// wind is the interesting one -- it doubles his range when it is at your back, which
        /// is the entire reason you circle round in the hunting mission instead of walking
        /// straight at the thing.
        /// </summary>
        private void Watch(Quarry q, Ped player, bool crouched, bool sprinting, Vector3 wind, int now)
        {
            var to = player.Position - q.Man.Position;
            var gap = to.Length();

            if (gap > TrackRange)
            {
                q.Suspicion = 0f;
                return;
            }

            var dt = Game.LastFrameTime;
            if (dt <= 0f || dt > 0.25f) dt = 1f / 60f;

            // UPWIND OF HIM IS THE MISTAKE. The wind blows from him to you means your noise
            // goes away from him; the other way round and he has you at twice the distance.
            var carried = 1f;

            if (gap > 0.5f)
            {
                var toward = new Vector3(to.X / gap, to.Y / gap, 0f);
                var with = toward.X * wind.X + toward.Y * wind.Y;

                // Positive: the wind is going from him toward you, which is in your favour.
                carried = with > 0f ? 1f : 1f + (UpwindPenalty - 1f) * Math.Min(1f, -with);
            }

            var reach = SeeRange * carried;

            var rising = 0f;

            if (gap < reach)
            {
                var seen = false;

                try
                {
                    seen = Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, q.Man.Handle,
                                               player.Handle, 17);
                }
                catch
                {
                    seen = gap < reach * 0.5f;
                }

                var near = 1f - gap / reach;

                if (seen) rising += SeenPerSecond * near;
                if (sprinting) rising += SprintPerSecond * near;

                if (crouched) rising *= CrouchQuiet;
            }

            // A SHOT IS A SHOT. Everybody within earshot looks up, wherever the wind is.
            if (_shotAt != 0 && now - _shotAt < 400) rising += ShotSpike;

            q.Suspicion += (rising - CalmPerSecond) * dt;

            if (q.Suspicion < 0f) q.Suspicion = 0f;

            if (q.Suspicion < 1f) return;

            Gone(q);
        }

        /// <summary>He has had enough and he is off. That one is not coming back.</summary>
        private void Gone(Quarry q)
        {
            q.Spooked = true;

            try
            {
                Function.Call(Hash.SET_PED_KEEP_TASK, q.Man.Handle, true);
                Function.Call(Hash.TASK_SMART_FLEE_PED, q.Man.Handle,
                              Game.Player.Character.Handle, 300f, -1, false, false);

                q.Man.MarkAsNoLongerNeeded();
            }
            catch
            {
                // He runs on his own.
            }

            if (q.Mark != null && q.Mark.Exists()) { q.Mark.Delete(); q.Mark = null; }

            Say("he's gone man. you was too loud");

            Notify.Failure("he saw you. that one's gone.");

            Log.Info("Hunt: one spooked. " + Down + " down, " + Lost + " lost.");
        }

        /// <summary>
        /// Down, and how he went down.
        ///
        /// A HEAD SHOT IS A CLEAN KILL and anything else is a wounded man running, which is
        /// the minigame's own rule and the reason it has a blood trail in it at all. Checked
        /// off the damage the game recorded rather than guessed.
        /// </summary>
        private void Dropped(Quarry q, int now)
        {
            q.Down = true;
            q.Bleeding = false;

            if (q.Mark != null && q.Mark.Exists()) { q.Mark.Delete(); q.Mark = null; }

            Say(Down >= Many ? "that's three. let's move"
                             : (Down == 1 ? "one down. two more" : "two. one left"));

            Log.Info("Hunt: one down. " + Down + " of " + Many + ".");
        }

        /// <summary>
        /// Wounded and running, leaving blood behind him.
        ///
        /// The same trail the tracks are, in a different colour and laid as he goes rather
        /// than in advance. He drops on his own at the end of it, which is what stops a
        /// wounded man running to the far side of the map with the job attached to him.
        /// </summary>
        private void Bleed(Quarry q, int now)
        {
            if (q.Man == null || !q.Man.Exists()) { q.Spooked = true; return; }

            if (q.LastBlood == Vector3.Zero || q.Man.Position.DistanceTo(q.LastBlood) > 1.6f)
            {
                Spot(q.Man.Position, 0.55f, 0.06f, 0.05f, 0.34f);
                q.LastBlood = q.Man.Position;
            }

            if (now - q.BleedFrom < BleedMs) return;

            try
            {
                Function.Call(Hash.APPLY_DAMAGE_TO_PED, q.Man.Handle, 400, true, 0, 0);
            }
            catch
            {
                // He is written off either way.
            }

            Dropped(q, now);
        }

        /// <summary>Hit but not dropped: he runs, and the blood starts.</summary>
        private void Hurt(Quarry q, int now)
        {
            {
                if (q.Down || q.Spooked || q.Bleeding) return;

                q.Bleeding = true;
                q.BleedFrom = now;
                q.LastBlood = Vector3.Zero;

                try
                {
                    Function.Call(Hash.SET_PED_KEEP_TASK, q.Man.Handle, true);
                    Function.Call(Hash.TASK_SMART_FLEE_PED, q.Man.Handle,
                                  Game.Player.Character.Handle, 200f, -1, false, false);
                }
                catch
                {
                    // He limps off on his own.
                }

                Say("you winged him. follow the blood");
                return;
            }
        }

        // ---- the call ------------------------------------------------------------------

        /// <summary>
        /// Lamar shouts, and whoever is nearest comes to look.
        ///
        /// THE ELK CALL, and it works the same way: it does not make him safe, it makes him
        /// COME, which is the trade. A man walking toward you is a man who will see you sooner.
        /// </summary>
        private void Called(Ped player, int now)
        {
            if (now < _nextCall) return;
            if (Phase != HuntPhase.Tracking) return;

            Quarry near = null;
            var closest = 90f;

            foreach (var q in _out)
            {
                if (q.Down || q.Spooked || q.Bleeding) continue;
                if (q.Man == null || !q.Man.Exists()) continue;

                var gap = q.Man.Position.DistanceTo(player.Position);
                if (gap > closest || gap < TooClose) continue;

                closest = gap;
                near = q;
            }

            if (near == null) return;

            _nextCall = now + CallEveryMs;
            near.LookingUntil = now + CallLookMs;

            try
            {
                Function.Call(Hash.CLEAR_PED_TASKS, near.Man.Handle);
                Function.Call(Hash.TASK_GO_TO_COORD_ANY_MEANS, near.Man.Handle,
                              player.Position.X, player.Position.Y, player.Position.Z,
                              1.2f, 0, false, 786603, 0f);
                Function.Call(Hash.SET_PED_KEEP_TASK, near.Man.Handle, true);
            }
            catch
            {
                // He stays where he is.
            }

            Say("watch -- he's coming to look");
        }

        // ---- the ground ------------------------------------------------------------------

        /// <summary>Puts three of them out, well apart, each with somewhere he walked in from.</summary>
        private void Lay(Ped player)
        {
            for (var i = 0; i < Many; i++)
            {
                var at = Somewhere(player.Position);
                if (at == Vector3.Zero) continue;

                var man = Make(at);
                if (man == null) continue;

                _out.Add(new Quarry
                {
                    Man = man,
                    CameFrom = at + (Vector3.RandomXY() * (PrintCount * PrintStride)),
                    Suspicion = 0f
                });
            }

            if (_out.Count == 0) Failure = "there was nobody out there.";
            else Log.Info("Hunt: " + _out.Count + " of them on the ground.");
        }

        private Vector3 Somewhere(Vector3 from)
        {
            for (var tries = 0; tries < 20; tries++)
            {
                var probe = _field + (Vector3.RandomXY() * (Spread + (float)_rng.NextDouble() * Spread));

                var at = World.GetNextPositionOnSidewalk(probe);
                if (at == Vector3.Zero) at = probe;

                if (at.DistanceTo(from) < TooClose * 2f) continue;

                var clear = true;

                foreach (var q in _out)
                {
                    if (q.Man == null || !q.Man.Exists()) continue;
                    if (q.Man.Position.DistanceTo(at) > Spread * 0.5f) continue;

                    clear = false;
                    break;
                }

                if (clear) return at;
            }

            return Vector3.Zero;
        }

        private Ped Make(Vector3 at)
        {
            foreach (var name in Models)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(2000)) continue;

                    var man = World.CreatePed(model, at);
                    model.MarkAsNoLongerNeeded();

                    if (man == null || !man.Exists()) continue;

                    man.IsPersistent = true;

                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, man.Handle, true, true);

                    // He is standing about, not patrolling, and he is not looking for you --
                    // that is what the suspicion is for.
                    Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, man.Handle,
                                  _rng.Next(2) == 0 ? "WORLD_HUMAN_SMOKING" : "WORLD_HUMAN_STAND_MOBILE",
                                  0, true);

                    Function.Call(Hash.SET_PED_ALERTNESS, man.Handle, 0);
                    Function.Call(Hash.SET_PED_SEEING_RANGE, man.Handle, SeeRange);
                    Function.Call(Hash.SET_PED_HEARING_RANGE, man.Handle, SeeRange * 0.7f);
                    Function.Call(Hash.SET_PED_KEEP_TASK, man.Handle, true);

                    if (_gangs != null)
                    {
                        var ballas = _gangs.Get("ballas");

                        if (ballas != null && ballas.GroupHash != 0)
                        {
                            Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, man.Handle, ballas.GroupHash);
                        }
                    }

                    return man;
                }
                catch
                {
                    // Next model.
                }
            }

            return null;
        }

        /// <summary>
        /// His tracks, laid on the ground between where he came in and where he is.
        ///
        /// ONLY WHEN YOU ARE NEAR ENOUGH TO BE READING THEM. Decals are a budget shared with
        /// every other script on the machine -- see the blood mod -- so a trail per man laid
        /// across the whole block would be spent on ground nobody is looking at.
        ///
        /// Re-laid on a slow clock rather than once, because he moves.
        /// </summary>
        private void Prints(Quarry q, Ped player, int now)
        {
            if (now - q.PrintedAt < 4000) return;

            var here = q.Man.Position;

            if (player.Position.DistanceTo(here) > PrintRange) return;

            q.PrintedAt = now;

            var from = q.CameFrom;
            var to = here;

            for (var i = 0; i < PrintCount; i++)
            {
                var t = i / (float)(PrintCount - 1);

                var at = from + (to - from) * t;

                // Oldest faintest, which is what tracking is: the fresh ones are the ones
                // pointing at him.
                var age = 0.25f + 0.75f * t;

                Spot(at, 0.28f * age, 0.26f * age, 0.24f * age, 0.22f);
            }
        }

        /// <summary>One mark on the ground, dropped straight down onto whatever is under it.</summary>
        private static void Spot(Vector3 at, float r, float g, float b, float size)
        {
            try
            {
                Function.Call(Hash.ADD_DECAL, 1023,
                              at.X, at.Y, at.Z + 0.2f,
                              0f, 0f, -1f,
                              1f, 0f, 0f,
                              size, size,
                              r, g, b, 0.9f,
                              600000f, true, false, false);
            }
            catch
            {
                // No tracks, then. The blips still lead him there.
            }
        }

        // ---- Lamar ---------------------------------------------------------------------

        /// <summary>He walks it with you, and he does not get in the way of a rifle.</summary>
        private void Lamar()
        {
            if (_lamar == null || !_lamar.Exists())
            {
                if (Fixer != null) _lamar = Fixer();
                if (_lamar == null || !_lamar.Exists()) return;

                try
                {
                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, _lamar.Handle, true, true);
                    Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, _lamar.Handle, false);
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, _lamar.Handle, true);
                }
                catch
                {
                    // He follows either way.
                }
            }

            if (!_lamar.IsAlive)
            {
                Failure = "lamar's down. that's the job.";
                return;
            }

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            var gap = _lamar.Position.DistanceTo(player.Position);

            if (gap < 9f || gap > 200f) return;

            try
            {
                Function.Call(Hash.TASK_FOLLOW_TO_OFFSET_OF_ENTITY, _lamar.Handle, player.Handle,
                              -1.2f, -2.0f, 0f, 2.0f, -1, 3f, true);

                Function.Call(Hash.SET_PED_KEEP_TASK, _lamar.Handle, true);
            }
            catch
            {
                // He catches up on his own.
            }
        }

        private void Say(string words)
        {
            if (string.IsNullOrEmpty(words)) return;

            _said = words;
            _saidAt = Game.GameTime;

            Notify.Text("CHAR_LAMAR", "Lamar", "on the block", words, false);
        }

        // ---- leaving --------------------------------------------------------------------

        private void Leaving(Ped player, int now)
        {
            if (player.Position.DistanceTo(_field) < FieldRadius) return;

            ReadyToCollect = true;
        }

        // ---- the card -------------------------------------------------------------------

        /// <summary>
        /// The hunting card: how many are left, which way the wind is going, and how close
        /// the nearest one is to hearing you.
        ///
        /// THE WIND ARROW IS THE WHOLE HUD. It is the one thing the minigame puts on screen
        /// that you cannot work out by looking at the world, and without it the wind rule is
        /// a rule nobody can play around.
        /// </summary>
        public void Draw()
        {
            if (!IsRunning) return;
            if (Phase == HuntPhase.Riding) return;

            const float w = 0.150f;
            const float h = 0.062f;

            var left = 0.5f - w * 0.5f;
            var top = 0.795f;

            Theme.Panel(left, top, w, h);

            var x = left + 0.010f;

            Hud.Text("THE HUNT", x, top + 0.006f, 0.26f,
                     Palette.Alpha(Palette.TextDim, 210), Hud.FontLabel, centre: false);

            Hud.TextRight(Down + " / " + Many, left + w - 0.010f, top + 0.004f, 0.34f,
                          Palette.Text, Hud.FontLabel);

            // ---- the wind ----
            var wind = Wind();

            var cx = x + 0.014f;
            var cy = top + 0.038f;

            Arrow(cx, cy, wind);

            Hud.Text("WIND", x + 0.030f, top + 0.030f, 0.22f,
                     Palette.Alpha(Palette.TextDim, 190), Hud.FontLabel, centre: false);

            // ---- how close the nearest one is to hearing you ----
            var worst = 0f;

            foreach (var q in _out)
            {
                if (q.Down || q.Spooked) continue;
                if (q.Suspicion > worst) worst = q.Suspicion;
            }

            var barX = x + 0.058f;
            var barW = w - 0.068f - 0.010f;

            Hud.RectFrom(barX, top + 0.034f, barW, 0.0075f,
                         Color.FromArgb(90, 255, 255, 255));

            if (worst > 0.01f)
            {
                var ink = worst > 0.66f ? Palette.Danger : worst > 0.33f ? Palette.Warn : Palette.Brand;

                Hud.RectFrom(barX, top + 0.034f, barW * Math.Min(1f, worst), 0.0075f, ink);
            }

            if (!string.IsNullOrEmpty(_said) && Game.GameTime - _saidAt < 5200)
            {
                Hud.Text(_said, left + w * 0.5f, top + 0.046f, 0.24f,
                         Palette.Alpha(Palette.TextDim, 210), Hud.FontBody);
            }
        }

        /// <summary>The wind, as an arrow. Four rectangles and no sprite.</summary>
        private static void Arrow(float cx, float cy, Vector3 wind)
        {
            var len = 0.016f;

            var dx = Hud.ToX(len) * wind.X;
            var dy = len * wind.Y;

            // The game's Y is north and the screen's is down, so it is flipped.
            var ex = cx + dx;
            var ey = cy - dy;

            var steps = 6;

            for (var i = 0; i <= steps; i++)
            {
                var t = i / (float)steps;

                Hud.RectFrom(cx + (ex - cx) * t - 0.0008f, cy + (ey - cy) * t - 0.0014f,
                             0.0016f, 0.0028f, Palette.Text);
            }

            Hud.RectFrom(ex - 0.0022f, ey - 0.0022f, 0.0044f, 0.0044f, Palette.Brand);
        }

        /// <summary>Which way the wind is going, flat and normalised.</summary>
        private static Vector3 Wind()
        {
            try
            {
                var w = Function.Call<Vector3>(Hash.GET_WIND_DIRECTION);

                var flat = new Vector3(w.X, w.Y, 0f);

                if (flat.Length() < 0.01f) return new Vector3(1f, 0f, 0f);

                return Vector3.Normalize(flat);
            }
            catch
            {
                return new Vector3(1f, 0f, 0f);
            }
        }

        private static bool Crouching(Ped player)
        {
            try { return Function.Call<bool>(Hash.GET_PED_STEALTH_MOVEMENT, player.Handle); }
            catch { return false; }
        }

        // ---- the end --------------------------------------------------------------------

        public void Clear()
        {
            HandItBack();

            foreach (var q in _out)
            {
                try
                {
                    if (q.Mark != null && q.Mark.Exists()) q.Mark.Delete();

                    if (q.Man != null && q.Man.Exists())
                    {
                        Function.Call(Hash.SET_PED_KEEP_TASK, q.Man.Handle, false);
                        q.Man.MarkAsNoLongerNeeded();
                    }
                }
                catch
                {
                    // The game takes them back.
                }
            }

            _out.Clear();

            try
            {
                if (_lamar != null && _lamar.Exists())
                {
                    Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, _lamar.Handle, true);
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, _lamar.Handle, false);
                }
            }
            catch
            {
                // He was borrowed.
            }

            _lamar = null;
            _def = null;
            _said = "";
            _shotAt = 0;

            Phase = HuntPhase.None;
            ReadyToCollect = false;
            Failure = null;
        }
    }
}
