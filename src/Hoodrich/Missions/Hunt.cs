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
    ///   THE RIFLE. He is handed one at the start and it goes when the job does. Hunting is
    ///   done at range or it is not hunting -- a shot from inside SpookRange is a mugging, and
    ///   the job says so and does not count it.
    ///
    ///   THE TRACKS. Every target lays a trail of footprints from where he came in to where he
    ///   is now, oldest faintest, and they are only drawn when you are close enough to be
    ///   reading the ground. Follow them and they lead to him.
    ///
    ///   THE LOOKOUTS. What the wind was, and better, because you can see it coming.
    ///
    ///   The wind was the minigame's own rule and it did not survive the move: an elk in a
    ///   valley smells you, and a man on a corner in Davis does not care which way the air is
    ///   going. What he cares about is who is stood on the next block watching him.
    ///
    ///   So there are other Ballas out there who are NOT the job. They are not hunting you and
    ///   they will not shoot at you. They are looking, and if one of them gets a clear look for
    ///   long enough he gets his phone out -- and once that call goes through, one of the three
    ///   walks off, because somebody just told him. That is the same lesson the wind taught,
    ///   said in a language this place speaks: get seen and you lose one.
    ///
    ///   And it is answerable, which the wind never was. Drop the lookout before he finishes
    ///   dialling and the call does not happen -- but a shot is a shot, and everybody within
    ///   earshot looks up.
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
        /// short to stand in the open. And a look he has half-finished drains away again at
        /// the same rate as the quarry's, so breaking line of sight is a real answer.
        ///
        /// CallMs is the window you have to do something about it.
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

            /// <summary>
            /// Every print he left, and which of them are on the ground yet.
            ///
            /// WORKED OUT ONCE AND STAMPED AS YOU REACH THEM. This used to be re-derived
            /// every four seconds from where he was standing at the time, and only while the
            /// player was within twenty-six metres OF THE MAN -- which is the one distance at
            /// which nobody needs tracks, because you are looking straight at him. Out at the
            /// range you actually pick a trail up, nothing was ever drawn at all. Reported as
            /// there being no footprints, and there were not.
            ///
            /// So the trail is a fixed list of places, made when he is, and each one goes down
            /// the first time YOU are near enough to be reading that piece of ground. They do
            /// not stack, they do not move, and they are there before you can see him.
            /// </summary>
            public Vector3[] Trail;
            public bool[] Laid;

            /// <summary>When the trail was last looked over, so it is not walked every frame.</summary>
            public int PrintedAt;

            /// <summary>Coming to look because Lamar shouted. See Call.</summary>
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

            /// <summary>When he started dialling, and whether it went through.</summary>
            public int CallingFrom;
            public bool Called;
        }

        private readonly List<Lookout> _eyes = new List<Lookout>();

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

                Watch(q, player, crouched, sprinting, now);
                Prints(q, player, now);
            }

            Phase = anyBleeding ? HuntPhase.Bleeding : HuntPhase.Tracking;

            Watching(player, crouched, now);

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
        /// THREE THINGS FEED IT: how close you are, whether he has a clear line to you, and
        /// how much noise you are making. There was a fourth and it was the wind, and it has
        /// gone -- see the note on the class. The lookouts are what replaced it, and they are
        /// their own thing rather than a modifier on this one, because a man being TOLD you are
        /// out here is not the same as a man noticing you himself and should not be maths on
        /// the same number.
        /// </summary>
        private void Watch(Quarry q, Ped player, bool crouched, bool sprinting, int now)
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

            var reach = SeeRange;

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

            // A SHOT IS A SHOT. Everybody within earshot looks up, and no amount of being
            // careful up to that point counts for anything.
            if (_shotAt != 0 && now - _shotAt < 400) rising += ShotSpike;

            q.Suspicion += (rising - CalmPerSecond) * dt;

            if (q.Suspicion < 0f) q.Suspicion = 0f;

            if (q.Suspicion < 1f) return;

            Gone(q);
        }

        // ---- the lookouts ----------------------------------------------------------------

        /// <summary>
        /// Men on the corners who are not the job, put out at the same time as it is.
        ///
        /// Placed on the far side of the field from where you came in, so the first thing you
        /// do is not walk into one -- and blipped, because a rule you cannot see coming is not
        /// a rule, it is a punishment. That was the wind's real problem: an arrow on a card is
        /// not the same as knowing where the danger is stood.
        /// </summary>
        private void Post(Ped player)
        {
            for (var i = 0; i < Eyes; i++)
            {
                var at = Somewhere(player.Position);
                if (at == Vector3.Zero) continue;

                var man = Make(at);
                if (man == null) continue;

                var eye = new Lookout { Man = man };

                try
                {
                    Function.Call(Hash.SET_PED_SEEING_RANGE, man.Handle, EyeRange);

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

            if (_eyes.Count > 0) Log.Info("Hunt: " + _eyes.Count + " lookout(s) on the corners.");
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
                    // Dropped mid-call. The call goes with him.
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

                var looking = false;

                if (gap < EyeRange)
                {
                    try
                    {
                        looking = Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY,
                                                      eye.Man.Handle, player.Handle, 17);
                    }
                    catch
                    {
                        looking = gap < EyeRange * 0.4f;
                    }
                }

                if (looking)
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

            if (nearest == null) return;

            Gone(nearest);
            Say("that's one gone. somebody told him");
            Log.Info("Hunt: the call landed and one of them walked.");
        }

        /// <summary>A lookout who has finished being one.</summary>
        private void Strip(Lookout eye)
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

                var from = at + (Vector3.RandomXY() * (PrintCount * PrintStride));

                var q = new Quarry
                {
                    Man = man,
                    CameFrom = from,
                    Suspicion = 0f
                };

                Walked(q, from, at);

                _out.Add(q);
            }

            if (_out.Count == 0) Failure = "there was nobody out there.";
            else Log.Info("Hunt: " + _out.Count + " of them on the ground.");

            Post(player);
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

            if (far < 1f)
            {
                q.Trail = new Vector3[0];
                q.Laid = new bool[0];
                return;
            }

            var step = new Vector3(run.X / far, run.Y / far, run.Z / far);
            var side = new Vector3(-step.Y, step.X, 0f);

            var many = (int)(far / PrintStride);
            if (many > PrintCount * 3) many = PrintCount * 3;
            if (many < 2) many = 2;

            q.Trail = new Vector3[many];
            q.Laid = new bool[many];

            for (var i = 0; i < many; i++)
            {
                var t = i / (float)(many - 1);

                q.Trail[i] = from + run * t + side * (i % 2 == 0 ? PrintSide : -PrintSide);
            }
        }

        /// <summary>How far a foot lands off the middle of the path. See Walked.</summary>
        private const float PrintSide = 0.16f;

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
            if (q.Trail == null || q.Trail.Length == 0) return;
            if (now - q.PrintedAt < 400) return;

            q.PrintedAt = now;

            var me = player.Position;

            for (var i = 0; i < q.Trail.Length; i++)
            {
                if (q.Laid[i]) continue;

                var at = q.Trail[i];

                // NEAR THE PRINT, not near the man. This is the whole repair -- see the note
                // on Quarry.Trail. You read the ground where you are stood, and the far end of
                // a trail is a piece of ground like any other.
                if (me.DistanceTo(at) > PrintRange) continue;

                q.Laid[i] = true;

                // Oldest faintest, which is what tracking is: the fresh ones are the ones
                // pointing at him.
                var age = 0.30f + 0.70f * (i / (float)Math.Max(1, q.Trail.Length - 1));

                var step = i + 1 < q.Trail.Length
                    ? q.Trail[i + 1] - at
                    : at - q.Trail[Math.Max(0, i - 1)];

                Spot(at, step, 0.30f * age, 0.27f * age, 0.24f * age, PrintSize, i % 2 == 0);
            }
        }

        /// <summary>
        /// One print on the ground, pointing the way he was walking.
        ///
        /// TYPE 2040, WHICH IS A FOOTPRINT. It was 1023, which is not -- decals are picked out
        /// of decals.dat by number and that number is not one of the ones that resolves to the
        /// fxdecal_footprints sheet, so every call went through and nothing appeared. 2040 and
        /// 2140 are the two BLOOD TRANSFER soles off that sheet, which is where the game's own
        /// bloody footprints come from; they are two different treads, so alternating them
        /// stops a trail being one stamp repeated. The colour is ours either way -- the art is
        /// a greyscale mask and takes whatever tint it is handed.
        ///
        /// AND THE LAST THREE FLAGS ARE ALL FALSE. The first of them was true, which is the
        /// other half of why nothing was ever drawn.
        ///
        /// The side vector is what turns it: across the direction of travel, so the toe points
        /// the way he went. A trail you can read the direction of is a trail; one you cannot is
        /// a line of smudges.
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
                // No tracks, then. The blips still lead him there.
            }
        }

        /// <summary>
        /// The two soles, and how big a print is.
        ///
        /// Both sit under BLOOD TRANSFER in decals.dat and both point at the same footprint
        /// sheet. Taller than it is wide, because a foot is.
        /// </summary>
        private const int PrintDecal = 2040;
        private const int PrintDecalAlt = 2140;
        private const float PrintSize = 0.17f;

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

            foreach (var eye in _eyes)
            {
                try
                {
                    if (eye.Mark != null && eye.Mark.Exists()) eye.Mark.Delete();

                    if (eye.Man != null && eye.Man.Exists())
                    {
                        Function.Call(Hash.SET_PED_KEEP_TASK, eye.Man.Handle, false);
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
