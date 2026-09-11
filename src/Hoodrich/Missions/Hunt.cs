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
    /// of tracks on the ground, something that gives you away, and an animal that leaves if
    /// you are loud or careless. This is that, with Lamar in the passenger seat, Ballas instead
    /// of elk, and lookouts on the corners instead of the wind.
    ///
    /// EVERY PIECE OF IT IS THE MINIGAME'S PIECE, in the same order:
    ///
    ///   THE RIDE OUT. A yellow ring on the map, the size of the ground, with a route to it.
    ///   Lamar gets in whatever you get in and gets out when you do; on foot he walks behind
    ///   you, crouches when you crouch, and stands still when you are looking down the glass,
    ///   because a man wandering across a sniper scope is the end of a hunt.
    ///
    ///   THE GROUND. Three of them, put down out of your sight somewhere in the block, each
    ///   with a purple ring on the map that says roughly where and not exactly. They do not
    ///   stand still: every so often one walks between his two spots, and every step of that
    ///   is a fresh print.
    ///
    ///   THE TRACKS. Every target has a trail of prints from where he walked in to where he
    ///   is now, oldest faintest, and they only go down when you are close enough to be
    ///   reading the ground. Stand on his trail and you have picked it up: the ring comes off
    ///   the map and a mark goes on the man, so from there it is a stalk.
    ///
    ///   THE LOOKOUTS. What the wind was, and better, because you can see them coming. Other
    ///   Ballas who are not the job, on the corners, looking. A clear look at you for three
    ///   seconds and the phone comes out; four seconds after that the call lands and the one
    ///   nearest him walks off. Drop the lookout before the call lands and it never does --
    ///   but a shot is a shot, and everybody within earshot looks up.
    ///
    ///   THE STEALTH. Crouched is quiet, walking is not much worse, sprinting is a man
    ///   arriving. Line of sight matters more than anything else. It all feeds one number per
    ///   target, and when that number fills he is gone for good.
    ///
    ///   THE CLEAN KILL. A head shot drops him where he stands. Anything else and he runs
    ///   bleeding, and you follow the blood the same way you followed the tracks.
    ///
    ///   THE CALL. When you are crouched and still with one of them somewhere out of sight
    ///   and not too far, Lamar shouts something across the block, and that one comes to
    ///   look -- to a place a rifle's length short of you, where he stands and looks about
    ///   for a while and then goes back. That is the elk call: it does not make him safe, it
    ///   makes him COME, and a man walking toward you is a man who will see you sooner.
    ///
    /// NOBODY IN THIS JOB IS EVER TASKED TWICE ON ONE FRAME. The first version of it sent
    /// Lamar a fresh follow task every frame, which is a man who never finishes starting to
    /// walk; and its call sent the quarry to your exact coordinate, where he arrived and stood
    /// in your face for the rest of the job. Every task here is issued once, on a change, or
    /// on a clock.
    ///
    /// The three Ballas and the three lookouts have their permanent events blocked, which is
    /// what stops the game's own gang hatred turning a stalk into a shootout the moment one
    /// of them sees a Families man. Only this job moves them.
    /// </summary>
    internal sealed class Hunt
    {
        // ======================================================================
        // Measures
        // ======================================================================

        /// <summary>How many of them, and how far apart they start.</summary>
        private const int Many = 3;
        private const float Spread = 55f;

        /// <summary>How far out the ground itself is, from where he takes the job.</summary>
        private const float FieldRadius = 90f;

        /// <summary>Near enough to the ground to put the men down, and how long that is given.</summary>
        private const float LayFrom = 150f;
        private const int LayPatienceMs = 25000;

        /// <summary>
        /// No man is put down nearer to you than this, and never where the camera can see.
        ///
        /// The ground used to be laid the moment you crossed the outer ring, wherever the
        /// dice landed, which on an open block was a man appearing forty metres in front of
        /// you. They are put down out of sight or not yet.
        /// </summary>
        private const float SpawnClear = 50f;

        /// <summary>How far off he can be and still be tracked at all.</summary>
        private const float TrackRange = 180f;

        /// <summary>Prints are only drawn when you are near enough to be reading the ground.</summary>
        private const float PrintRange = 30f;

        /// <summary>How long his first trail is, how far apart the prints are, and how many are kept.</summary>
        private const int PrintCount = 22;
        private const float PrintStride = 1.5f;
        private const int TrailMost = 70;

        /// <summary>Stood this close to one of his prints and you have picked up his trail.</summary>
        private const float FoundWithin = 6f;

        /// <summary>The ring on the map for each of them: this big, and slipped off him by up to this.</summary>
        private const float AreaRadius = 42f;
        private const float AreaSlip = 16f;

        /// <summary>How often one of them walks between his two spots, and how far apart they are.</summary>
        private const int WanderMinMs = 35000;
        private const int WanderVaryMs = 35000;
        private const float BeatFar = 14f;

        /// <summary>How far one of them can see you.</summary>
        private const float SeeRange = 42f;

        /// <summary>
        /// The lookouts: how many, how far they can see, how long a look takes and how long
        /// the call takes once he has started making it.
        ///
        /// FURTHER THAN THE QUARRY CAN SEE, ON PURPOSE. A lookout is doing nothing else. The
        /// three you are here for are stood about smoking and are not expecting anybody; the
        /// man on the corner is the reason the other three feel safe enough to do that.
        ///
        /// SpotSeconds is a LOOK, not a glance -- three seconds of clear line of sight, which
        /// is long enough to cross a gap between two walls without paying for it and far too
        /// short to stand in the open. A look he has half-finished drains away again at the
        /// same rate as the quarry's, so breaking line of sight is a real answer. CallMs is
        /// the window you have to do something about it.
        /// </summary>
        private const int Eyes = 3;
        private const float EyeRange = 65f;
        private const float SpotSeconds = 3.0f;
        private const int CallMs = 4200;

        /// <summary>What fills his suspicion per second, at the worst of it.</summary>
        private const float SeenPerSecond = 0.55f;
        private const float SprintPerSecond = 0.9f;
        private const float ShotSpike = 0.75f;

        /// <summary>And what he forgets per second when nothing is happening.</summary>
        private const float CalmPerSecond = 0.22f;

        /// <summary>Crouched, he hears half of it.</summary>
        private const float CrouchQuiet = 0.45f;

        /// <summary>How often a line of sight is actually asked for. Six raycasts a frame was the old figure.</summary>
        private const int LosEveryMs = 150;

        /// <summary>
        /// The call: how long he looks, how long between calls, how far short of you he
        /// stops, and how near and how far he can be for Lamar to bother.
        /// </summary>
        private const int CallLookMs = 9000;
        private const int CallEveryMs = 30000;
        private const float LookShort = 26f;
        private const float CallNear = 28f;
        private const float CallFar = 90f;

        /// <summary>How far the blood trail runs before he drops on his own.</summary>
        private const int BleedMs = 22000;

        /// <summary>Lamar, on foot: how far behind, and how often he is re-tasked if he is lagging.</summary>
        private const float LamarBehind = 4.5f;
        private const float LamarRunFrom = 12f;
        private const float LamarWalkFrom = 6f;
        private const int LamarRetaskMs = 4000;

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

        /// <summary>
        /// What the three are doing when you find them, and what the corners are doing.
        ///
        /// A dealer, a smoker and a man on his phone: three men on a block who are not
        /// expecting anybody. The lookouts stand like men whose job is standing. Every name
        /// is in Scenarios.txt on this machine.
        /// </summary>
        private static readonly string[] QuarryDoing =
        {
            "WORLD_HUMAN_DRUG_DEALER", "WORLD_HUMAN_SMOKING", "WORLD_HUMAN_STAND_MOBILE",
            "WORLD_HUMAN_HANG_OUT_STREET"
        };

        private static readonly string[] LookoutDoing =
        {
            "WORLD_HUMAN_GUARD_STAND", "WORLD_HUMAN_STAND_IMPATIENT", "WORLD_HUMAN_SMOKING"
        };

        // ======================================================================
        // The people in it
        // ======================================================================

        private sealed class Quarry
        {
            public Ped Man;

            /// <summary>The ring on the map before you have his trail, and the mark on him after.</summary>
            public Blip Area;
            public Blip Mark;

            /// <summary>His two spots, which one he is at, and what he does there.</summary>
            public Vector3 Home;
            public Vector3 Beat;
            public bool AtBeat;
            public string Doing = "";

            /// <summary>When he next walks, whether he is walking now, and where his last print went.</summary>
            public int WanderAt;
            public bool Walking;
            public int WalkFrom;
            public Vector3 LastStep;

            /// <summary>Nought to one. At one he is gone. See Watch.</summary>
            public float Suspicion;

            /// <summary>The last line-of-sight answer, and when it was asked.</summary>
            public bool Seen;
            public int LosAt;

            public bool Down;
            public bool Spooked;

            /// <summary>You stood on his trail. See Prints.</summary>
            public bool Found;

            /// <summary>Hit and running. The blood is the trail now.</summary>
            public bool Bleeding;
            public int BleedFrom;
            public Vector3 LastBlood;

            /// <summary>
            /// Every print he has left, and which of them are on the ground yet.
            ///
            /// A LIST, NOT AN ARRAY, because he keeps walking. The first stretch is laid when
            /// he is -- from where he walked in to where he stands -- and every walk between
            /// his spots after that adds to the end of it. Each one goes down the first time
            /// YOU are near enough to be reading that piece of ground, and they do not move.
            /// </summary>
            public readonly List<Vector3> Trail = new List<Vector3>();
            public readonly List<bool> Laid = new List<bool>();

            /// <summary>When the trail was last looked over, so it is not walked every frame.</summary>
            public int PrintedAt;

            /// <summary>Coming to look because Lamar shouted, and until when. See Called.</summary>
            public int LookingUntil;
        }

        /// <summary>
        /// One man on a corner who is not the job.
        ///
        /// He never fights and he is never worth points. All he does is look, and then tell
        /// somebody. See Watching.
        /// </summary>
        private sealed class Lookout
        {
            public Ped Man;
            public Blip Mark;

            /// <summary>Nought to one, filling while he has a clear look at you.</summary>
            public float Spot;

            public bool Seen;
            public int LosAt;

            /// <summary>When he started dialling, and whether it went through.</summary>
            public int CallingFrom;
            public bool Called;
        }

        /// <summary>What Lamar is doing, so he is only re-tasked when it changes.</summary>
        private enum Walk { None, Follow, Run, Still, Boarding, Riding, Leaving }

        private readonly List<Lookout> _eyes = new List<Lookout>();
        private readonly List<Quarry> _out = new List<Quarry>();

        private readonly Affiliation _crew;
        private readonly GangRegistry _gangs;
        private readonly Random _rng = new Random();

        private MissionDef _def;
        private Vector3 _field;
        private Blip _fieldMark;
        private int _layFrom;

        private Ped _lamar;
        private Walk _lamarDoing;
        private int _lamarAt;
        private bool _lamarStealth;

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

        /// <summary>
        /// Set by MissionRunner: hands Lamar back to his corner when the job is over.
        ///
        /// THE HUNT NEVER GAVE HIM BACK. The bike ride calls Fixer.TakeBack at its end and
        /// this did not, so after a hunt he stayed lent for the rest of the session -- which
        /// is a man you cannot talk to, stood on his own corner.
        /// </summary>
        public Action GiveBack;

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

        // ======================================================================
        // Starting
        // ======================================================================

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
            _layFrom = 0;
            _lamarDoing = Walk.None;
            _lamarAt = 0;
            _lamarStealth = false;

            MarkField();

            Say("get us out there. and go quiet when we're close");

            Log.Info("Hunt: on, at " + _field.X.ToString("0") + ", " + _field.Y.ToString("0") + ".");

            return null;
        }

        /// <summary>
        /// The ground, on the map, for the ride out.
        ///
        /// THERE WAS NO MARKER. Every other job marks its site; the hunt handed you a rifle
        /// and an objective that said "get out to the block" and nothing that said which
        /// block. The same yellow ring the other jobs use, with the route, until the ground
        /// is laid and the rings for the three take over.
        /// </summary>
        private void MarkField()
        {
            try
            {
                _fieldMark = World.CreateBlip(_field, FieldRadius);
                if (_fieldMark == null || !_fieldMark.Exists()) return;

                _fieldMark.Color = BlipColor.Yellow;
                _fieldMark.Alpha = 90;
                _fieldMark.ShowRoute = true;
                _fieldMark.Name = _def != null && !string.IsNullOrEmpty(_def.Name) ? _def.Name : "The hunt";
            }
            catch (Exception ex)
            {
                Log.Debug("Could not mark the hunt: " + ex.Message);
            }
        }

        private void UnmarkField()
        {
            try { if (_fieldMark != null && _fieldMark.Exists()) _fieldMark.Delete(); }
            catch { /* it goes with the job */ }

            _fieldMark = null;
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

        // ======================================================================
        // The tick
        // ======================================================================

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

                Lamar(player, now);

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

        /// <summary>
        /// Out to the block. The ground is laid once he is near enough, a man at a time as
        /// somewhere out of sight turns up for each, and the hunt starts when they are all
        /// down or the patience has run out with at least one.
        /// </summary>
        private void Riding(Ped player, int now)
        {
            if (player.Position.DistanceTo(_field) > LayFrom) return;

            if (_layFrom == 0) _layFrom = now;

            Lay(player, now);

            var waited = now - _layFrom;

            if (_out.Count < Many && waited < LayPatienceMs) return;

            if (_out.Count == 0)
            {
                Failure = "there was nobody out there.";
                return;
            }

            Post(player);
            UnmarkField();

            Phase = HuntPhase.Tracking;
            _phaseFrom = now;

            Log.Info("Hunt: " + _out.Count + " of them on the ground, " + _eyes.Count + " lookout(s) on the corners.");

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

            var anyBleeding = false;

            foreach (var q in _out)
            {
                if (q.Down || q.Spooked) continue;

                if (q.Man == null || !q.Man.Exists())
                {
                    q.Spooked = true;
                    Unring(q);
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

                Looking(q, now);
                Wander(q, now);
                Watch(q, player, crouched, sprinting, now);
                Prints(q, player, now);
            }

            Phase = anyBleeding ? HuntPhase.Bleeding : HuntPhase.Tracking;

            Watching(player, crouched, now);

            Called(player, crouched, sprinting, now);

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
        /// THREE THINGS FEED IT: how close you are, whether he has a clear line to you, and
        /// how much noise you are making. There was a fourth and it was the wind, and it has
        /// gone -- see the note on the class. The lookouts are what replaced it, and they are
        /// their own thing rather than a modifier on this one, because a man being TOLD you are
        /// out here is not the same as a man noticing you himself and should not be maths on
        /// the same number.
        ///
        /// The line of sight is asked for a few times a second, not every frame: the answer
        /// does not change faster than that and the raycast is the dearest thing in here.
        /// </summary>
        private void Watch(Quarry q, Ped player, bool crouched, bool sprinting, int now)
        {
            var gap = q.Man.Position.DistanceTo(player.Position);

            if (gap > TrackRange)
            {
                q.Suspicion = 0f;
                return;
            }

            var dt = Game.LastFrameTime;
            if (dt <= 0f || dt > 0.25f) dt = 1f / 60f;

            var rising = 0f;

            if (gap < SeeRange)
            {
                if (now - q.LosAt >= LosEveryMs)
                {
                    q.LosAt = now;

                    try
                    {
                        q.Seen = Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, q.Man.Handle,
                                                     player.Handle, 17);
                    }
                    catch
                    {
                        q.Seen = gap < SeeRange * 0.5f;
                    }
                }

                var near = 1f - gap / SeeRange;

                if (q.Seen) rising += SeenPerSecond * near;
                if (sprinting) rising += SprintPerSecond * near;

                if (crouched) rising *= CrouchQuiet;
            }
            else
            {
                q.Seen = false;
            }

            // A SHOT IS A SHOT. Everybody within earshot looks up, and no amount of being
            // careful up to that point counts for anything.
            if (_shotAt != 0 && now - _shotAt < 400) rising += ShotSpike;

            q.Suspicion += (rising - CalmPerSecond) * dt;

            if (q.Suspicion < 0f) q.Suspicion = 0f;

            if (q.Suspicion < 1f) return;

            Gone(q);
        }

        // ======================================================================
        // The ground
        // ======================================================================

        /// <summary>
        /// Puts them out, well apart, each with somewhere he walked in from, a second spot to
        /// walk to, and a ring on the map that says roughly where.
        ///
        /// A MAN A TICK, until there are three. Somewhere out of your sight is not always
        /// available on the first ask, and the old version gave up on the spot and ran the
        /// hunt with whoever it had managed. This keeps asking for as long as the ride is
        /// patient, and only settles for fewer when that runs out.
        /// </summary>
        private void Lay(Ped player, int now)
        {
            if (_out.Count >= Many) return;

            var at = Somewhere(player);
            if (at == Vector3.Zero) return;

            var doing = QuarryDoing[_out.Count % QuarryDoing.Length];

            var man = Make(at, doing, SeeRange);
            if (man == null) return;

            var from = at + (Vector3.RandomXY() * (PrintCount * PrintStride));

            var q = new Quarry
            {
                Man = man,
                Home = at,
                Beat = Nearby(at),
                Doing = doing,
                LastStep = at,
                WanderAt = now + WanderMinMs + _rng.Next(WanderVaryMs)
            };

            Walked(q, from, at);
            Ring(q);

            _out.Add(q);
        }

        /// <summary>His second spot: a little way off along the pavement.</summary>
        private Vector3 Nearby(Vector3 home)
        {
            for (var tries = 0; tries < 6; tries++)
            {
                var probe = home + (Vector3.RandomXY() * (BeatFar * (0.7f + (float)_rng.NextDouble() * 0.6f)));

                var at = World.GetNextPositionOnSidewalk(probe);
                if (at == Vector3.Zero) continue;
                if (at.DistanceTo(home) < 6f) continue;

                return at;
            }

            return home;
        }

        /// <summary>
        /// The ring on the map for one of them.
        ///
        /// SLIPPED OFF HIM. A ring centred on the man is a marker on the man with extra
        /// steps; centred somewhere within a few strides of him it says "in here" and no
        /// more, which is what a search area is. It comes off the moment you have his
        /// trail, and a mark on the man goes on in its place -- see Prints.
        /// </summary>
        private void Ring(Quarry q)
        {
            try
            {
                var slip = Vector3.RandomXY() * ((float)_rng.NextDouble() * AreaSlip);

                q.Area = World.CreateBlip(q.Home + slip, AreaRadius);
                if (q.Area == null || !q.Area.Exists()) return;

                q.Area.Color = BlipColor.Purple;
                q.Area.Alpha = 70;
            }
            catch
            {
                // The prints still lead to him.
            }
        }

        private static void Unring(Quarry q)
        {
            try { if (q.Area != null && q.Area.Exists()) q.Area.Delete(); }
            catch { /* it goes with the job */ }

            try { if (q.Mark != null && q.Mark.Exists()) q.Mark.Delete(); }
            catch { /* likewise */ }

            q.Area = null;
            q.Mark = null;
        }

        /// <summary>
        /// Where he walked, as a list of places a foot went.
        ///
        /// LEFT AND RIGHT, NOT A LINE OF DOTS. Each print steps a hand's width off the middle
        /// of the path and the side alternates, which is the difference between a trail and a
        /// dotted line -- and it is the thing that makes a print readable as a print at all
        /// once it is only a few inches across on the ground.
        /// </summary>
        private static void Walked(Quarry q, Vector3 from, Vector3 to)
        {
            var run = to - from;
            var far = run.Length();

            if (far < 1f) return;

            var step = new Vector3(run.X / far, run.Y / far, run.Z / far);
            var side = new Vector3(-step.Y, step.X, 0f);

            var many = (int)(far / PrintStride);
            if (many > PrintCount * 2) many = PrintCount * 2;
            if (many < 2) many = 2;

            for (var i = 0; i < many; i++)
            {
                var t = i / (float)(many - 1);

                q.Trail.Add(from + run * t + side * (i % 2 == 0 ? PrintSide : -PrintSide));
                q.Laid.Add(false);
            }
        }

        /// <summary>How far a foot lands off the middle of the path. See Walked.</summary>
        private const float PrintSide = 0.16f;

        /// <summary>
        /// A place on the block for one of them, or nothing this tick.
        ///
        /// ON A PAVEMENT, out of the camera's sight, not close to you, and not on top of
        /// anybody already out. Nothing here is guaranteed on the first ask; the callers
        /// ask again next tick.
        /// </summary>
        private Vector3 Somewhere(Ped player)
        {
            var me = player.Position;

            for (var tries = 0; tries < 24; tries++)
            {
                var probe = _field + (Vector3.RandomXY() * (Spread + (float)_rng.NextDouble() * Spread));

                var at = World.GetNextPositionOnSidewalk(probe);
                if (at == Vector3.Zero) continue;

                if (at.DistanceTo(me) < SpawnClear) continue;

                try
                {
                    if (Function.Call<bool>(Hash.IS_SPHERE_VISIBLE, at.X, at.Y, at.Z, 2.5f)) continue;
                }
                catch
                {
                    // Then out of sight cannot be checked, and distance will have to do.
                }

                if (!Clear(at)) continue;

                return at;
            }

            return Vector3.Zero;
        }

        /// <summary>Whether nobody of ours is already stood near there.</summary>
        private bool Clear(Vector3 at)
        {
            foreach (var q in _out)
            {
                if (q.Man == null || !q.Man.Exists()) continue;
                if (q.Home.DistanceTo(at) < Spread * 0.5f) return false;
            }

            foreach (var eye in _eyes)
            {
                if (eye.Man == null || !eye.Man.Exists()) continue;
                if (eye.Man.Position.DistanceTo(at) < Spread * 0.4f) return false;
            }

            return true;
        }

        /// <summary>
        /// One Balla, stood somewhere doing something, who reacts to nothing but this job.
        ///
        /// PERMANENT EVENTS BLOCKED. Without that the game's own gang hatred is in charge:
        /// a Ballas ped who sees a Families man pulls a pistol, which turned a stalk into a
        /// gunfight the moment a target or a lookout happened to glance your way. Now the only
        /// things that move him are the numbers in here.
        /// </summary>
        private Ped Make(Vector3 at, string doing, float sees)
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
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, man.Handle, true);

                    Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, man.Handle, doing, 0, true);

                    Function.Call(Hash.SET_PED_ALERTNESS, man.Handle, 0);
                    Function.Call(Hash.SET_PED_SEEING_RANGE, man.Handle, sees);
                    Function.Call(Hash.SET_PED_HEARING_RANGE, man.Handle, sees * 0.7f);
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
        /// Walk there, then do that, as one sequence -- the same shape the car meet uses.
        /// Two tasks issued back to back are one task; in a sequence they are two.
        /// </summary>
        private static bool Send(Ped man, Vector3 to, string doing, float pace)
        {
            var slot = new OutputArgument();

            try
            {
                var d = to - man.Position;
                var face = (float)(Math.Atan2(d.Y, d.X) * 180.0 / Math.PI) - 90f;

                Function.Call(Hash.SET_PED_KEEP_TASK, man.Handle, false);

                Function.Call(Hash.OPEN_SEQUENCE_TASK, slot);
                var seq = slot.GetResult<int>();

                Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, 0,
                              to.X, to.Y, to.Z, pace, 40000, 0.8f, 0, face);

                Function.Call(Hash.TASK_START_SCENARIO_AT_POSITION, 0, doing,
                              to.X, to.Y, to.Z, face, -1, false, false);

                Function.Call(Hash.CLOSE_SEQUENCE_TASK, seq);
                Function.Call(Hash.TASK_PERFORM_SEQUENCE, man.Handle, seq);

                Function.Call(Hash.SET_PED_KEEP_TASK, man.Handle, true);
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Hunt: could not send a man across the block: " + ex.Message);
                return false;
            }
            finally
            {
                try { Function.Call(Hash.CLEAR_SEQUENCE_TASK, slot); } catch { }
            }
        }

        /// <summary>
        /// Between his two spots, now and then, leaving prints as he goes.
        ///
        /// A MAN WHO NEVER MOVES LEAVES ONE TRAIL, and once you have read it there is nothing
        /// left to read. Every so often he walks to his other spot and back, and every step
        /// of it goes on the end of his trail -- so the ground keeps saying where he went,
        /// which is what tracking is. Not while he is suspicious, and not while Lamar has
        /// him coming over.
        /// </summary>
        private void Wander(Quarry q, int now)
        {
            if (q.Walking)
            {
                // Prints behind him as he goes.
                if (q.Man.Position.DistanceTo(q.LastStep) >= PrintStride)
                {
                    var side = Vector3.Cross(q.Man.ForwardVector, Vector3.WorldUp);
                    side = new Vector3(side.X, side.Y, 0f);

                    q.Trail.Add(q.Man.Position + side * (q.Trail.Count % 2 == 0 ? PrintSide : -PrintSide));
                    q.Laid.Add(false);
                    q.LastStep = q.Man.Position;

                    while (q.Trail.Count > TrailMost)
                    {
                        q.Trail.RemoveAt(0);
                        q.Laid.RemoveAt(0);
                    }
                }

                var arrived = false;
                try { arrived = Function.Call<bool>(Hash.IS_PED_USING_ANY_SCENARIO, q.Man.Handle); }
                catch { arrived = true; }

                if (arrived || now - q.WalkFrom > 40000)
                {
                    q.Walking = false;
                    q.WanderAt = now + WanderMinMs + _rng.Next(WanderVaryMs);
                }

                return;
            }

            if (now < q.WanderAt) return;
            if (q.LookingUntil != 0 || q.Suspicion > 0.2f) return;
            if (q.Beat == q.Home) { q.WanderAt = now + WanderMinMs; return; }

            var to = q.AtBeat ? q.Home : q.Beat;

            if (!Send(q.Man, to, q.Doing, 1.0f))
            {
                q.WanderAt = now + 8000;
                return;
            }

            q.AtBeat = !q.AtBeat;
            q.Walking = true;
            q.WalkFrom = now;
            q.LastStep = q.Man.Position;
        }

        /// <summary>
        /// His tracks, laid on the ground between where he came in and where he is.
        ///
        /// ONLY WHEN YOU ARE NEAR ENOUGH TO BE READING THEM. Decals are a budget shared with
        /// every other script on the machine -- see the blood mod -- so a trail per man laid
        /// across the whole block would be spent on ground nobody is looking at.
        ///
        /// AND STAND ON IT AND YOU HAVE HIM. Within a few strides of any print of his that
        /// is down, the ring comes off the map and a mark goes on the man: you have picked
        /// up the trail, and from here it is a stalk rather than a search.
        /// </summary>
        private void Prints(Quarry q, Ped player, int now)
        {
            if (q.Trail.Count == 0) return;
            if (now - q.PrintedAt < 400) return;

            q.PrintedAt = now;

            var me = player.Position;
            var stood = false;

            for (var i = 0; i < q.Trail.Count; i++)
            {
                var at = q.Trail[i];
                var gap = me.DistanceTo(at);

                if (q.Laid[i])
                {
                    if (gap < FoundWithin) stood = true;
                    continue;
                }

                // NEAR THE PRINT, not near the man. You read the ground where you are stood,
                // and the far end of a trail is a piece of ground like any other.
                if (gap > PrintRange) continue;

                q.Laid[i] = true;

                // Oldest faintest, which is what tracking is: the fresh ones are the ones
                // pointing at him.
                var age = 0.35f + 0.65f * (i / (float)Math.Max(1, q.Trail.Count - 1));

                var step = i + 1 < q.Trail.Count
                    ? q.Trail[i + 1] - at
                    : at - q.Trail[Math.Max(0, i - 1)];

                Spot(at, step, 0.46f * age, 0.40f * age, 0.30f * age, PrintSize, i % 2 == 0);

                if (gap < FoundWithin) stood = true;
            }

            if (stood && !q.Found) Found(q);
        }

        /// <summary>The trail is picked up: the ring off, a mark on him.</summary>
        private void Found(Quarry q)
        {
            q.Found = true;

            try { if (q.Area != null && q.Area.Exists()) q.Area.Delete(); }
            catch { /* it goes with the job */ }

            q.Area = null;

            try
            {
                q.Mark = q.Man.AddBlip();

                if (q.Mark != null && q.Mark.Exists())
                {
                    q.Mark.Sprite = (BlipSprite)1;
                    q.Mark.Color = BlipColor.Red;
                    q.Mark.Scale = 0.55f;
                    q.Mark.Name = "Ballas";
                    q.Mark.IsShortRange = true;
                }
            }
            catch
            {
                // Then the prints are all there is, which is still a trail.
            }

            Log.Info("Hunt: picked up a trail.");
        }

        /// <summary>
        /// One print on the ground, pointing the way he was walking.
        ///
        /// TYPE 2040, WHICH IS A FOOTPRINT: one of the two BLOOD TRANSFER soles off the
        /// fxdecal_footprints sheet, where the game's own bloody footprints come from; 2140 is
        /// the other tread, and alternating them stops a trail being one stamp repeated. The
        /// colour is ours -- the art is a greyscale mask and takes whatever tint it is
        /// handed -- and it is a good deal lighter than it was, because a print tinted
        /// thirty percent grey on wet asphalt was a print nobody could see. The side vector
        /// turns it across the direction of travel, so the toe points the way he went.
        /// </summary>
        private static void Spot(Vector3 at, Vector3 step, float r, float g, float b,
                                 float size, bool left)
        {
            var flat = new Vector3(step.X, step.Y, 0f);

            if (flat.Length() < 0.001f) flat = new Vector3(1f, 0f, 0f);
            else flat.Normalize();

            var side = new Vector3(-flat.Y, flat.X, 0f);

            try
            {
                Function.Call(Hash.ADD_DECAL, left ? PrintDecal : PrintDecalAlt,
                              at.X, at.Y, at.Z + 0.15f,
                              0f, 0f, -1f,
                              side.X, side.Y, 0f,
                              size, size * 1.6f,
                              r, g, b, 0.9f,
                              600000f, false, false, false);
            }
            catch
            {
                // No tracks, then. The rings still say where to look.
            }
        }

        /// <summary>The two soles, and how big a print is. Taller than it is wide, because a foot is.</summary>
        private const int PrintDecal = 2040;
        private const int PrintDecalAlt = 2140;
        private const float PrintSize = 0.21f;

        // ======================================================================
        // The lookouts
        // ======================================================================

        /// <summary>
        /// Men on the corners who are not the job, put out once the three are.
        ///
        /// Blipped, because a rule you cannot see coming is not a rule, it is a punishment.
        /// That was the wind's real problem: an arrow on a card is not the same as knowing
        /// where the danger is stood.
        /// </summary>
        private void Post(Ped player)
        {
            for (var i = 0; i < Eyes; i++)
            {
                var at = Somewhere(player);
                if (at == Vector3.Zero) continue;

                var man = Make(at, LookoutDoing[i % LookoutDoing.Length], EyeRange);
                if (man == null) continue;

                var eye = new Lookout { Man = man };

                try
                {
                    eye.Mark = man.AddBlip();

                    if (eye.Mark != null && eye.Mark.Exists())
                    {
                        eye.Mark.Sprite = (BlipSprite)1;
                        eye.Mark.Color = BlipColor.Purple;
                        eye.Mark.Scale = 0.6f;
                        eye.Mark.Name = "Lookout";
                        eye.Mark.IsShortRange = true;
                    }
                }
                catch
                {
                    // He still watches.
                }

                _eyes.Add(eye);
            }
        }

        /// <summary>
        /// The lookouts, looking.
        ///
        /// A CLEAR LINE FOR THREE SECONDS, then the phone. Not proximity -- you can walk past
        /// one at ten metres with a wall between you and he never knows. What he needs is to
        /// actually see you, and what you need is for him not to.
        ///
        /// Crouching helps here the same as it does everywhere else, and it is the same number,
        /// because there is one idea in this job about being careful and it should not mean
        /// two different things depending on who is looking at you.
        ///
        /// ONCE HE IS DIALLING, ONLY A BULLET STOPS IT. Breaking line of sight after he has
        /// the phone out is too late -- he has already decided, and the whole point of the call
        /// is that it reaches somebody who is not here. So there is a window, and there is one
        /// thing you can do in it, and that thing is loud.
        /// </summary>
        private void Watching(Ped player, bool crouched, int now)
        {
            var dt = Game.LastFrameTime;
            if (dt <= 0f || dt > 0.25f) dt = 1f / 60f;

            foreach (var eye in _eyes)
            {
                if (eye.Called) continue;

                if (eye.Man == null || !eye.Man.Exists() || !eye.Man.IsAlive)
                {
                    // Dropped mid-call, or before it. The call goes with him.
                    eye.Called = true;
                    Strip(eye);
                    continue;
                }

                if (eye.CallingFrom != 0)
                {
                    if (now - eye.CallingFrom >= CallMs) Told(eye);
                    continue;
                }

                var gap = eye.Man.Position.DistanceTo(player.Position);

                if (gap < EyeRange)
                {
                    if (now - eye.LosAt >= LosEveryMs)
                    {
                        eye.LosAt = now;

                        try
                        {
                            eye.Seen = Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY,
                                                           eye.Man.Handle, player.Handle, 17);
                        }
                        catch
                        {
                            eye.Seen = gap < EyeRange * 0.4f;
                        }
                    }
                }
                else
                {
                    eye.Seen = false;
                }

                if (eye.Seen)
                {
                    var rate = 1f / SpotSeconds;
                    if (crouched) rate *= CrouchQuiet;

                    eye.Spot += rate * dt;
                }
                else
                {
                    eye.Spot -= CalmPerSecond * dt;
                }

                if (eye.Spot < 0f) eye.Spot = 0f;
                if (eye.Spot < 1f) continue;

                Dial(eye, now);
            }
        }

        /// <summary>Phone out. Four seconds, and then somebody knows.</summary>
        private void Dial(Lookout eye, int now)
        {
            eye.CallingFrom = now;
            eye.Spot = 1f;

            try
            {
                Function.Call(Hash.CLEAR_PED_TASKS, eye.Man.Handle);
                Function.Call(Hash.TASK_USE_MOBILE_PHONE_TIMED, eye.Man.Handle, CallMs + 1500);
            }
            catch
            {
                // The clock runs either way.
            }

            if (eye.Mark != null && eye.Mark.Exists()) eye.Mark.Color = BlipColor.Red;

            Say("somebody's on the phone. shut him up");
            Log.Info("Hunt: a lookout is making the call.");
        }

        /// <summary>
        /// The call went through, and the nearest one to him walks off.
        ///
        /// NEAREST TO THE LOOKOUT, not to you. He rang the man he can see, and the man he can
        /// see is the one who leaves -- which means where you get spotted decides which of the
        /// three you lose, and that is worth knowing before you cross a road.
        /// </summary>
        private void Told(Lookout eye)
        {
            eye.Called = true;

            Quarry nearest = null;
            var best = float.MaxValue;

            foreach (var q in _out)
            {
                if (q.Down || q.Spooked) continue;
                if (q.Man == null || !q.Man.Exists()) continue;

                var gap = q.Man.Position.DistanceTo(eye.Man.Position);

                if (gap >= best) continue;

                best = gap;
                nearest = q;
            }

            Strip(eye);

            // Back to standing about. His job is done.
            try
            {
                Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, eye.Man.Handle,
                              LookoutDoing[0], 0, true);
            }
            catch
            {
                // He stands there anyway.
            }

            if (nearest == null) return;

            Gone(nearest);
            Say("that's one gone. somebody told him");
            Log.Info("Hunt: the call landed and one of them walked.");
        }

        /// <summary>A lookout who has finished being one.</summary>
        private static void Strip(Lookout eye)
        {
            try
            {
                if (eye.Mark != null && eye.Mark.Exists()) eye.Mark.Delete();
            }
            catch
            {
                // It goes with the mission.
            }

            eye.Mark = null;
        }

        // ======================================================================
        // Losing them, and dropping them
        // ======================================================================

        /// <summary>He has had enough and he is off. That one is not coming back.</summary>
        private void Gone(Quarry q)
        {
            q.Spooked = true;
            q.LookingUntil = 0;

            try
            {
                Function.Call(Hash.SET_PED_KEEP_TASK, q.Man.Handle, true);
                Function.Call(Hash.CLEAR_PED_TASKS, q.Man.Handle);
                Function.Call(Hash.TASK_SMART_FLEE_PED, q.Man.Handle,
                              Game.Player.Character.Handle, 300f, -1, false, false);

                q.Man.MarkAsNoLongerNeeded();
            }
            catch
            {
                // He runs on his own.
            }

            Unring(q);

            Say("he's gone man. you was too loud");

            Notify.Failure("he saw you. that one's gone.");

            Log.Info("Hunt: one spooked. " + Down + " down, " + Lost + " lost.");
        }

        /// <summary>
        /// Down, and how he went down.
        ///
        /// A HEAD SHOT IS A CLEAN KILL and anything else is a wounded man running, which is
        /// the minigame's own rule and the reason it has a blood trail in it at all.
        /// </summary>
        private void Dropped(Quarry q, int now)
        {
            q.Down = true;
            q.Bleeding = false;
            q.LookingUntil = 0;

            Unring(q);

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
            if (q.Man == null || !q.Man.Exists()) { q.Spooked = true; Unring(q); return; }

            if (q.LastBlood == Vector3.Zero || q.Man.Position.DistanceTo(q.LastBlood) > 1.6f)
            {
                // ALONG HIS OWN HEADING. A print in advance knows where the next one is; a
                // drop of blood is left behind a man who is still running, so the only
                // direction there is to point it is the way he is facing.
                Spot(q.Man.Position, q.Man.ForwardVector, 0.55f, 0.06f, 0.05f,
                     PrintSize * 1.5f, q.LastBlood == Vector3.Zero);

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
            if (q.Down || q.Spooked || q.Bleeding) return;

            q.Bleeding = true;
            q.BleedFrom = now;
            q.LastBlood = Vector3.Zero;
            q.LookingUntil = 0;
            q.Walking = false;

            try
            {
                Function.Call(Hash.SET_PED_KEEP_TASK, q.Man.Handle, true);
                Function.Call(Hash.CLEAR_PED_TASKS, q.Man.Handle);
                Function.Call(Hash.TASK_SMART_FLEE_PED, q.Man.Handle,
                              Game.Player.Character.Handle, 200f, -1, false, false);
            }
            catch
            {
                // He limps off on his own.
            }

            // The mark stays on a running man, so the blood has somewhere to be leading.
            if (q.Mark == null || !q.Mark.Exists())
            {
                try { if (q.Area != null && q.Area.Exists()) q.Area.Delete(); }
                catch { /* it goes with the job */ }

                q.Area = null;
            }

            Say("you winged him. follow the blood");
        }

        // ======================================================================
        // The call
        // ======================================================================

        /// <summary>
        /// Lamar shouts, and one of them comes to look.
        ///
        /// WHEN IT IS WORTH SHOUTING, not on a clock. The old call went every thirty seconds
        /// whatever you were doing, and it sent the man to your exact coordinate, where he
        /// arrived and stood in your face for the rest of the job. It goes when you are
        /// crouched and still with one of them somewhere between a rifle's length and a
        /// block away who has not got a look at you -- and it brings him to a place short
        /// of you, not to you. He stands there and looks about for a while and then walks
        /// back. That is the elk call: it does not make him safe, it makes him COME, which
        /// is the trade, because a man walking toward you is a man who will see you sooner.
        /// </summary>
        private void Called(Ped player, bool crouched, bool sprinting, int now)
        {
            if (now < _nextCall) return;
            if (Phase != HuntPhase.Tracking) return;
            if (!crouched || sprinting) return;
            if (_lamar == null || !_lamar.Exists() || !_lamar.IsAlive) return;

            var still = false;
            try { still = player.Velocity.Length() < 0.4f; } catch { still = true; }
            if (!still) return;

            Quarry near = null;
            var closest = CallFar;

            foreach (var q in _out)
            {
                if (q.Down || q.Spooked || q.Bleeding || q.Walking || q.LookingUntil != 0) continue;
                if (q.Man == null || !q.Man.Exists()) continue;
                if (q.Seen || q.Suspicion > 0.25f) continue;

                var gap = q.Man.Position.DistanceTo(player.Position);
                if (gap > closest || gap < CallNear) continue;

                closest = gap;
                near = q;
            }

            if (near == null) return;

            _nextCall = now + CallEveryMs;

            // Short of you along the line between you, on the pavement if there is one.
            var d = player.Position - near.Man.Position;
            var far = d.Length();
            var toward = far < 0.01f ? new Vector3(1f, 0f, 0f) : d / far;

            var stop = player.Position - toward * LookShort;
            var on = World.GetNextPositionOnSidewalk(stop);
            if (on != Vector3.Zero && on.DistanceTo(stop) < 8f) stop = on;

            try
            {
                Function.Call(Hash.SET_PED_KEEP_TASK, near.Man.Handle, false);
                Function.Call(Hash.CLEAR_PED_TASKS, near.Man.Handle);
                Function.Call(Hash.TASK_GO_TO_COORD_ANY_MEANS, near.Man.Handle,
                              stop.X, stop.Y, stop.Z, 1.2f, 0, false, 786603, 0f);
                Function.Call(Hash.SET_PED_KEEP_TASK, near.Man.Handle, true);
            }
            catch
            {
                // He stays where he is.
            }

            near.LookingUntil = now + CallLookMs;
            near.Walking = false;

            try
            {
                Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, _lamar.Handle,
                              "GENERIC_INSULT_HIGH", "SPEECH_PARAMS_FORCE_SHOUTED");
            }
            catch
            {
                // The text says it.
            }

            Say("watch -- he's coming to look");
        }

        /// <summary>
        /// The man who came to look, looking, and then going home.
        ///
        /// Turned to face you when he gets there, which is what a man who has walked over
        /// to see about a noise does -- and it is the trade, because facing you is how he
        /// gets his clear line. When the look is over he walks back to his spot and picks
        /// up what he was doing, and every step of that is a print.
        /// </summary>
        private void Looking(Quarry q, int now)
        {
            if (q.LookingUntil == 0) return;

            if (now < q.LookingUntil)
            {
                var player = Game.Player.Character;
                if (player == null || !player.Exists()) return;

                // Arrived: face the noise. Once, near the end of the walk.
                if (!q.Walking && now > q.LookingUntil - CallLookMs + 2500 &&
                    q.Man.Position.DistanceTo(player.Position) < LookShort + 6f)
                {
                    try
                    {
                        if (!Function.Call<bool>(Hash.GET_IS_TASK_ACTIVE, q.Man.Handle, 224))
                        {
                            Function.Call(Hash.TASK_LOOK_AT_ENTITY, q.Man.Handle, player.Handle, 4000, 0, 2);
                        }
                    }
                    catch
                    {
                        // He looks about on his own.
                    }
                }

                return;
            }

            q.LookingUntil = 0;

            var home = q.AtBeat ? q.Beat : q.Home;

            if (Send(q.Man, home, q.Doing, 1.0f))
            {
                q.Walking = true;
                q.WalkFrom = now;
                q.LastStep = q.Man.Position;
            }

            q.WanderAt = now + WanderMinMs + _rng.Next(WanderVaryMs);
        }

        // ======================================================================
        // Lamar
        // ======================================================================

        /// <summary>
        /// He comes with you, in the car or on foot, and he does not get in the way of a rifle.
        ///
        /// IN WHATEVER YOU ARE IN. The ride out said "with Lamar" and nothing put him in the
        /// car: he was left on the corner and turned up on the block, if he turned up at all,
        /// by the fixer's own despawn rules. Now he gets in whatever you get in, is warped
        /// into it if you have driven off without him and nobody can see him, and gets out
        /// when you do.
        ///
        /// ON FOOT, BEHIND YOU, AT YOUR PACE. Walking when you walk, running when you are
        /// away from him, crouched when you are crouched -- the same stealth movement you are
        /// using -- and STOOD STILL when you are looking down the glass with him near, because
        /// a man walking across a scope is the end of a hunt. Tasked when that changes, and
        /// otherwise left alone: the old version re-issued his follow task every frame, which
        /// is a man who never finishes starting to walk.
        /// </summary>
        private void Lamar(Ped player, int now)
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
                    Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, _lamar.Handle, false);
                }
                catch
                {
                    // He follows either way.
                }

                _lamarDoing = Walk.None;
                _lamarAt = 0;
            }

            if (!_lamar.IsAlive)
            {
                Failure = "lamar's down. that's the job.";
                return;
            }

            // ---- in the car ----
            if (player.IsInVehicle())
            {
                var car = player.CurrentVehicle;
                if (car == null || !car.Exists()) return;

                if (_lamar.IsInVehicle(car))
                {
                    _lamarDoing = Walk.Riding;
                    return;
                }

                var gap = _lamar.Position.DistanceTo(car.Position);

                // Driven off without him and nobody looking: he is simply in the car. A man
                // sprinting after a car for three streets is not a passenger, it is a chase.
                if (gap > 60f && !_lamar.IsOnScreen)
                {
                    var seat = FreeSeat(car);

                    if (seat != NoSeat)
                    {
                        try
                        {
                            Function.Call(Hash.SET_PED_INTO_VEHICLE, _lamar.Handle, car.Handle, seat);
                            _lamarDoing = Walk.Riding;
                        }
                        catch
                        {
                            // Next pass.
                        }
                    }

                    return;
                }

                if (_lamarDoing == Walk.Boarding && now - _lamarAt < LamarRetaskMs) return;

                _lamarDoing = Walk.Boarding;
                _lamarAt = now;

                try
                {
                    Stealth(false);
                    Function.Call(Hash.CLEAR_PED_TASKS, _lamar.Handle);
                    Function.Call(Hash.TASK_ENTER_VEHICLE, _lamar.Handle, car.Handle, -1, -2, 2f, 1, 0);
                }
                catch
                {
                    // He will find his own way in.
                }

                return;
            }

            // ---- you are out and he is not ----
            if (_lamar.IsInVehicle())
            {
                if (_lamarDoing == Walk.Leaving && now - _lamarAt < LamarRetaskMs) return;

                _lamarDoing = Walk.Leaving;
                _lamarAt = now;

                try
                {
                    var ride = _lamar.CurrentVehicle;

                    Function.Call(Hash.TASK_LEAVE_VEHICLE, _lamar.Handle,
                                  ride == null || !ride.Exists() ? 0 : ride.Handle, 0);
                }
                catch
                {
                    // He gets out on the next pass.
                }

                return;
            }

            // ---- on foot ----
            var away = _lamar.Position.DistanceTo(player.Position);

            // Left a long way behind and out of sight: put behind you rather than made to run
            // three blocks. Same rule as the car.
            if (away > 90f && !_lamar.IsOnScreen)
            {
                try
                {
                    var behind = player.Position - player.ForwardVector * 5f;
                    var on = World.GetNextPositionOnSidewalk(behind);
                    if (on != Vector3.Zero && on.DistanceTo(behind) < 10f) behind = on;

                    Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, _lamar.Handle,
                                  behind.X, behind.Y, behind.Z, false, false, false);
                }
                catch
                {
                    // Then he runs.
                }

                _lamarDoing = Walk.None;
            }

            var crouched = Crouching(player);
            var aiming = Game.Player.IsAiming;

            Stealth(crouched);

            Walk want;

            if (aiming && away < LamarRunFrom) want = Walk.Still;
            else if (player.IsSprinting || away > LamarRunFrom) want = Walk.Run;
            else if (_lamarDoing == Walk.Run && away > LamarWalkFrom) want = Walk.Run;
            else want = Walk.Follow;

            // Re-issued on a change, and every few seconds while he is lagging, in case the
            // task he had fell off. Never every frame.
            var lagging = away > LamarRunFrom && now - _lamarAt > LamarRetaskMs;

            if (want == _lamarDoing && !lagging) return;

            _lamarDoing = want;
            _lamarAt = now;

            try
            {
                if (want == Walk.Still)
                {
                    Function.Call(Hash.TASK_STAND_STILL, _lamar.Handle, -1);
                }
                else
                {
                    Function.Call(Hash.TASK_FOLLOW_TO_OFFSET_OF_ENTITY, _lamar.Handle, player.Handle,
                                  -0.9f, -LamarBehind, 0f, want == Walk.Run ? 2f : 1f, -1, 2.5f, true);
                }

                Function.Call(Hash.SET_PED_KEEP_TASK, _lamar.Handle, true);
            }
            catch
            {
                // He catches up on his own.
            }
        }

        /// <summary>His crouch follows yours. Set only on a change; the native is not free.</summary>
        private void Stealth(bool on)
        {
            if (on == _lamarStealth) return;
            _lamarStealth = on;

            try
            {
                Function.Call(Hash.SET_PED_STEALTH_MOVEMENT, _lamar.Handle, on, "DEFAULT_ACTION");
            }
            catch
            {
                // He walks upright, then.
            }
        }

        private const int NoSeat = int.MinValue;

        /// <summary>A passenger seat with nobody in it, or NoSeat. Same as the mission runner's.</summary>
        private static int FreeSeat(Vehicle ride)
        {
            try
            {
                var seats = Function.Call<int>(Hash.GET_VEHICLE_MODEL_NUMBER_OF_SEATS, ride.Model.Hash);

                for (var seat = 0; seat <= seats - 2; seat++)
                {
                    if (Function.Call<bool>(Hash.IS_VEHICLE_SEAT_FREE, ride.Handle, seat, false))
                    {
                        return seat;
                    }
                }
            }
            catch
            {
                // No seat, then.
            }

            return NoSeat;
        }

        private void Say(string words)
        {
            if (string.IsNullOrEmpty(words)) return;

            _said = words;
            _saidAt = Game.GameTime;

            Notify.Text("CHAR_LAMAR", "Lamar", "on the block", words, false);
        }

        // ======================================================================
        // Leaving
        // ======================================================================

        private void Leaving(Ped player, int now)
        {
            if (player.Position.DistanceTo(_field) < FieldRadius) return;

            ReadyToCollect = true;
        }

        // ======================================================================
        // The card
        // ======================================================================

        /// <summary>
        /// The hunting card: how many are left, whether anybody is looking at you, and how
        /// close the nearest one is to hearing you.
        ///
        /// THE LOOKOUT READOUT IS WHAT THE WIND ARROW WAS. Both exist for the same reason -- a
        /// rule you cannot see the state of is a rule you cannot play around -- and this one
        /// has the advantage of being about something that is actually on the map. The word
        /// goes amber while somebody has a look going and red once the phone is out, which is
        /// the only warning there is that you have about four seconds to do something.
        /// </summary>
        public void Draw()
        {
            if (!IsRunning) return;
            if (Phase == HuntPhase.Riding) return;

            const float w = 0.160f;
            const float h = 0.062f;

            var left = 0.5f - w * 0.5f;
            var top = 0.795f;

            Theme.Panel(left, top, w, h);

            var x = left + 0.010f;

            Hud.Text("THE HUNT", x, top + 0.006f, 0.26f,
                     Palette.Alpha(Palette.TextDim, 210), Hud.FontLabel, centre: false);

            Hud.TextRight(Down + " / " + Many, left + w - 0.010f, top + 0.004f, 0.34f,
                          Palette.Text, Hud.FontLabel);

            // ---- who is looking ----
            var watched = 0f;
            var dialling = false;
            var eyes = 0;

            foreach (var eye in _eyes)
            {
                if (eye.Called) continue;
                if (eye.Man == null || !eye.Man.Exists() || !eye.Man.IsAlive) continue;

                eyes++;

                if (eye.CallingFrom != 0) dialling = true;
                if (eye.Spot > watched) watched = eye.Spot;
            }

            var eyeInk = dialling ? Palette.Danger
                       : watched > 0.35f ? Palette.Warn
                       : Palette.Alpha(Palette.TextDim, 190);

            Hud.Text(dialling ? "ON THE PHONE" : eyes > 0 ? "EYES  " + eyes : "NO EYES",
                     x, top + 0.030f, 0.22f, eyeInk, Hud.FontLabel, centre: false);

            // ---- how close the nearest one is to hearing you ----
            var worst = 0f;

            foreach (var q in _out)
            {
                if (q.Down || q.Spooked) continue;
                if (q.Suspicion > worst) worst = q.Suspicion;
            }

            var barX = x + 0.062f;
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

        private static bool Crouching(Ped player)
        {
            try { return Function.Call<bool>(Hash.GET_PED_STEALTH_MOVEMENT, player.Handle); }
            catch { return false; }
        }

        // ======================================================================
        // The end
        // ======================================================================

        public void Clear()
        {
            HandItBack();
            UnmarkField();

            foreach (var q in _out)
            {
                try
                {
                    Unring(q);

                    if (q.Man != null && q.Man.Exists())
                    {
                        Function.Call(Hash.SET_PED_KEEP_TASK, q.Man.Handle, false);
                        Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, q.Man.Handle, false);
                        q.Man.MarkAsNoLongerNeeded();
                    }
                }
                catch
                {
                    // The game takes them back.
                }
            }

            _out.Clear();

            foreach (var eye in _eyes)
            {
                try
                {
                    Strip(eye);

                    if (eye.Man != null && eye.Man.Exists())
                    {
                        Function.Call(Hash.SET_PED_KEEP_TASK, eye.Man.Handle, false);
                        Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, eye.Man.Handle, false);
                        eye.Man.MarkAsNoLongerNeeded();
                    }
                }
                catch
                {
                    // The game takes them back.
                }
            }

            _eyes.Clear();

            try
            {
                if (_lamar != null && _lamar.Exists())
                {
                    Stealth(false);
                    Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, _lamar.Handle, true);
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, _lamar.Handle, false);
                }
            }
            catch
            {
                // He was borrowed.
            }

            // And handed back to his corner. See GiveBack.
            if (_lamar != null && GiveBack != null)
            {
                try { GiveBack(); }
                catch { /* the fixer takes him back on its own clock */ }
            }

            _lamar = null;
            _lamarDoing = Walk.None;
            _lamarStealth = false;
            _def = null;
            _said = "";
            _shotAt = 0;
            _layFrom = 0;

            Phase = HuntPhase.None;
            ReadyToCollect = false;
            Failure = null;
        }
    }
}
