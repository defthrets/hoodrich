using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Gangs
{
    /// <summary>
    /// Three of ours in a ring, kicking a ball about.
    ///
    /// THE BLOCK DOING SOMETHING TOGETHER. Everything else stood on a corner is stood on it
    /// alone -- smoking, leaning, on a phone -- and three men doing three separate things a
    /// metre apart are three men who happen to be near each other. A ball going between them
    /// is the one prop that makes them a GROUP: each of them is waiting on the other two,
    /// looking where the other two are looking, and the thing they are doing has no point
    /// except each other.
    ///
    /// ONE GAME AT A TIME, ON WHICHEVER OF THE SPOTS THE CLOCK SAYS. Five spots round the
    /// blocks and only one of them live at once, moving on to the next every few hours of
    /// game time -- but never while you are stood watching it. A game you walked up to stays
    /// where it is until you have walked away from it, and the next time you come near a
    /// spot it is whichever one the clock has moved on to.
    ///
    /// THE BALL IS MOVED BY HAND, NOT BY PHYSICS. A real ball kicked by a real force goes
    /// under a car, off a kerb, into the road, and three men stood facing an empty ring is the
    /// last thing anybody wants to walk up on. So it is frozen, its collision is off, and it
    /// is carried from one man's feet to the next along a line, fast off the foot and slowing
    /// as it arrives, turning over as a ball rolling that far would. Nobody has to chase it.
    ///
    /// THE KICK IS A REAL CLIP AND THE BALL LEAVES ON THE FOOT. The game's own melee kick is
    /// played on whoever has the ball, and the ball is released when the clip reaches the
    /// frame the foot comes through -- read off the clip's phase, not a timer, so it leaves
    /// on contact whatever the frame rate is doing. A clip that never starts does not stop
    /// the ball: it goes anyway, and the log says the clip did not play.
    ///
    /// THEY ARE DEAF ON PURPOSE, until something happens TO them. Blocking non-temporary
    /// events is what keeps a man stood on his mark through a car horn and a siren; the cost
    /// is that he would stand through a gunfight too. So the game is broken up by hand the
    /// moment one of them is hurt, knocked over, pulled into a fight, or you start shooting
    /// near it -- the ball is let go to roll where it likes and the three of them are handed
    /// back to the game to react the way the set does.
    /// </summary>
    internal sealed class Kickabout
    {
        /// <summary>One of the three, and where he belongs.</summary>
        private sealed class Man
        {
            public Ped Who;

            /// <summary>His mark on the ring, and the way he faces stood on it: the middle.</summary>
            public Vector3 Mark;
            public float Facing;

            /// <summary>Where the ball sits when it is his: a short step in from the mark.</summary>
            public Vector3 Feet;

            /// <summary>When he was last walked back onto his mark. See Tidy.</summary>
            public int TidiedAt;
        }

        /// <summary>Where the ball is in the game: at somebody's feet, on somebody's foot, or between them.</summary>
        private enum Play { Nowhere, Held, Kicking, Rolling }

        private readonly Settings _cfg;
        private readonly GangRegistry _gangs;
        private readonly string _gangId;
        private readonly Vector3[] _spots;
        private readonly float[] _headings;
        private readonly Random _rng = new Random();
        private readonly List<Man> _men = new List<Man>();

        /// <summary>Which spots the block has already posted about this session.</summary>
        private readonly bool[] _told;

        /// <summary>
        /// The feed, for the one line the block posts about it. Set by Main, and named Feed
        /// rather than Social for the reason Homies gives: a field called Social shadows the
        /// namespace.
        /// </summary>
        public Hoodrich.Social.SocialFeed Feed;

        /// <summary>Which spot is spawned, or -1. See Slot.</summary>
        private int _live = -1;

        /// <summary>The middle of the ring, on the ground. The spot as read is at hip height.</summary>
        private Vector3 _middle;

        private bool _staffed;

        /// <summary>True once the game has been broken up. It stays broken until you leave. See Break.</summary>
        private bool _broken;

        private int _lastUpdate;

        /// <summary>You, as of the last throttled pass, for the per-frame shots check.</summary>
        private int _you;
        private bool _youNear;

        private Prop _ball;
        private string _ballAs = "";

        /// <summary>How far above the ground the ball's origin sits. Measured, see MakeBall.</summary>
        private float _lift = 0.11f;

        /// <summary>Which ball model is being waited on, and since when. Same queue as Fixture.</summary>
        private int _tryingBall;
        private int _tryingSince;

        private Play _play = Play.Nowhere;

        /// <summary>Who has the ball, and who it is going to.</summary>
        private int _holder = -1;
        private int _target = -1;

        /// <summary>When the man with the ball stops looking at it and kicks it.</summary>
        private int _holdUntil;

        /// <summary>The pass in flight: from where, to where, since when, for how long, pointed which way.</summary>
        private Vector3 _from;
        private Vector3 _to;
        private int _rollStart;
        private int _rollMs;
        private float _rollYaw;

        /// <summary>The kick being waited on. See Kick and Released.</summary>
        private int _kickStart;
        private string _kickClip = "";
        private bool _kickSaid;

        private int _nextChat;
        private int _nextGlance;
        private int _kicks;

        public Kickabout(Settings cfg, GangRegistry gangs, string gangId, Vector3[] spots, float[] headings)
        {
            _cfg = cfg;
            _gangs = gangs;
            _gangId = gangId;
            _spots = spots ?? new Vector3[0];
            _headings = headings ?? new float[0];
            _told = new bool[_spots.Length];
        }

        private bool Enabled => _cfg == null || _cfg.KickaboutEnabled;

        // ---- per-tick ----------------------------------------------------------------------

        public void Update()
        {
            if (_spots.Length == 0) return;

            if (!Enabled)
            {
                if (_live >= 0) Clear("switched off");
                return;
            }

            var now = Game.GameTime;

            // EVERY FRAME: the ball in flight, and the kick it is waiting on. A ball moved on a
            // timer is a ball that jumps, and a kick released on a timer is a foot that misses.
            if (_live >= 0 && !_broken && _staffed) Run(now);

            if (now - _lastUpdate < UpdateIntervalMs) return;
            _lastUpdate = now;

            Ped player;

            try { player = Game.Player.Character; }
            catch { return; }

            if (player == null || !player.Exists()) return;

            _you = player.Handle;

            if (_live < 0)
            {
                var slot = Slot();
                var away = player.Position.DistanceTo(_spots[slot]);

                if (away > SpawnRange) return;

                // NOT IN FRONT OF YOU. Three men and a ball appearing on a pavement you are
                // looking straight down is the one thing about this that would read as a mod.
                // So it waits until the spot is off screen -- and gives up waiting once you
                // are close enough that they would have been there, because a spot you never
                // take your eyes off is otherwise a spot that never has anybody on it.
                if (away > PopInRange && Seen(_spots[slot])) return;

                _live = slot;
                _middle = _spots[slot];

                Log.Info("Kickabout: spot " + (slot + 1) + " of " + _spots.Length + " is live, at " + _middle + ".");
            }

            var far = player.Position.DistanceTo(_spots[_live]);

            if (far > DespawnRange)
            {
                Clear("you left");
                return;
            }

            _youNear = far < ShotsRange;

            // Broken up stays broken up. The corner is theirs to be scattered on until you
            // have gone far enough away for the next game to be a different game.
            if (_broken) return;

            if (!Bury()) return;

            if (!_staffed)
            {
                Fill();
                if (!_staffed) return;
            }

            if (_ball == null || !_ball.Exists())
            {
                _ball = null;
                if (!MakeBall(now)) return;
            }

            Tell(player);
        }

        /// <summary>
        /// Which spot the clock says.
        ///
        /// Read off the game's own day and hour rather than kept in the save, so it is the same
        /// answer for anybody who reloads the script, and it moves whether or not the mod was
        /// running while the hours went by. Three game hours a spot: about six real minutes,
        /// long enough to walk up on and watch, short enough that a lap of the blocks in the
        /// evening finds it somewhere else than it was in the afternoon.
        /// </summary>
        private int Slot()
        {
            try
            {
                var day = Function.Call<int>(Hash.GET_CLOCK_DAY_OF_MONTH);
                var hour = Function.Call<int>(Hash.GET_CLOCK_HOURS);
                var n = (day * 24 + hour) / HoursPerSpot;

                return ((n % _spots.Length) + _spots.Length) % _spots.Length;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>Whether that spot is on screen right now.</summary>
        private static bool Seen(Vector3 at)
        {
            try
            {
                return Function.Call<bool>(Hash.IS_SPHERE_VISIBLE, at.X, at.Y, at.Z + 0.5f, 2f);
            }
            catch
            {
                return false;
            }
        }

        // ---- the three of them ---------------------------------------------------------------

        /// <summary>
        /// The ring, filled from wherever it got to last time.
        ///
        /// TOPPED UP RATHER THAN FILLED ONCE, the way the corners are: a model is asked for and
        /// not waited on, so the third man is often not resident on the pass that made the
        /// first two. It keeps asking until it has three and then the game starts.
        /// </summary>
        private void Fill()
        {
            // NOT INTO A WORLD THAT IS ALREADY FULL. See Core.Crowded.
            if (Core.Crowded.Busy)
            {
                Core.Crowded.HeldOff("Kickabout");
                return;
            }

            var gang = _gangs == null ? null : _gangs.Get(_gangId);
            if (gang == null || gang.MemberModels.Count == 0) return;

            // THE GROUND FIRST. The spot was read off a man stood on it, which puts it at his
            // hips, and a probe that finds nothing means the collision is not in yet -- so
            // nobody is put down until it is, rather than three men put down in the air.
            if (_men.Count == 0)
            {
                float floor;

                if (!Core.Ground.Probe(new Vector3(_middle.X, _middle.Y, _middle.Z + 2f), out floor)) return;

                _middle = new Vector3(_middle.X, _middle.Y, floor);
            }

            var heading = _live < _headings.Length ? _headings[_live] : 0f;

            for (var i = _men.Count; i < Three; i++)
            {
                try
                {
                    // Round the ring from the way the reading was taken, a third of a turn
                    // apart, each facing the middle -- which is where the ball is.
                    var a = (heading + i * 120f) * (float)Math.PI / 180f;
                    var outward = new Vector3(-(float)Math.Sin(a), (float)Math.Cos(a), 0f);

                    var mark = Ground(_middle + outward * Ring);
                    var feet = Ground(_middle + outward * (Ring - StepIn));
                    var facing = Heading(-outward);

                    // Shuffled per man. OnFoot takes the first model on the list that is
                    // resident, and the same list in the same order is the same man three
                    // times over.
                    var who = GangPeds.OnFoot(gang, Shuffled(gang.MemberModels), mark, facing);
                    if (who == null) return;

                    Settle(who, facing);

                    _men.Add(new Man { Who = who, Mark = mark, Facing = facing, Feet = feet });
                }
                catch (Exception ex)
                {
                    Log.Debug("Kickabout: could not put one of them on the ring: " + ex.Message);
                    return;
                }
            }

            if (_men.Count < Three) return;

            _staffed = true;
            _holder = 0;
            _kicks = 0;
        }

        /// <summary>Stood on his mark, facing the middle, and not going anywhere for anything.</summary>
        private static void Settle(Ped who, float facing)
        {
            var h = who.Handle;

            try
            {
                // Dressed differently from the man next to him. CREATE_PED hands out the
                // model's first outfit every time, and three of the same outfit is a team.
                Function.Call(Hash.SET_PED_RANDOM_COMPONENT_VARIATION, h, 0);
                Function.Call(Hash.SET_PED_RANDOM_PROPS, h);
            }
            catch
            {
                // The first outfit, then.
            }

            try
            {
                Function.Call(Hash.SET_ENTITY_HEADING, h, facing);

                // Deaf to the street. See the note on the class: a man who is not held here
                // wanders off the first time a horn goes, and the game is broken up by hand
                // for the things that should break it up.
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, h, true);
                Function.Call(Hash.SET_PED_KEEP_TASK, h, true);
            }
            catch
            {
                // He stands where he was put regardless; that is what a ped with no task does.
            }
        }

        /// <summary>The list in a different order each time. Short, so a plain shuffle.</summary>
        private List<string> Shuffled(List<string> models)
        {
            var list = new List<string>(models);

            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = _rng.Next(i + 1);
                var t = list[i];
                list[i] = list[j];
                list[j] = t;
            }

            return list;
        }

        /// <summary>
        /// Anything wrong with any of them, and the game is over.
        ///
        /// False when it broke the game up, so the caller stops there.
        /// </summary>
        private bool Bury()
        {
            foreach (var m in _men)
            {
                var who = m.Who;

                if (who == null || !who.Exists())
                {
                    Break("one of them is gone");
                    return false;
                }

                try
                {
                    if (!who.IsAlive || Function.Call<bool>(Hash.IS_PED_INJURED, who.Handle))
                    {
                        Break("one of them is down");
                        return false;
                    }

                    if (Function.Call<bool>(Hash.IS_PED_RAGDOLL, who.Handle))
                    {
                        Break("one of them was knocked over");
                        return false;
                    }

                    if (Function.Call<bool>(Hash.IS_PED_IN_COMBAT, who.Handle, 0))
                    {
                        Break("one of them is fighting");
                        return false;
                    }

                    if (Function.Call<bool>(Hash.HAS_ENTITY_BEEN_DAMAGED_BY_ANY_PED, who.Handle))
                    {
                        Break("somebody hit one of them");
                        return false;
                    }
                }
                catch
                {
                    // A question that cannot be asked is not a reason to end the game.
                }
            }

            return true;
        }

        // ---- the ball ------------------------------------------------------------------------

        /// <summary>
        /// The ball, as the first model on the list this install can stream. The same queue as
        /// Fixture, for the same reason: the first that is RESIDENT is whichever happened to
        /// be nearby, and a soccer ball that loses to a resident basketball every time is a
        /// list that means nothing.
        /// </summary>
        private bool MakeBall(int now)
        {
            var at = _holder >= 0 && _holder < _men.Count ? _men[_holder].Feet : _men[0].Feet;

            while (_tryingBall < Balls.Length)
            {
                var name = Balls[_tryingBall];

                try
                {
                    var model = new Model(name);

                    if (!model.IsValid || !model.IsInCdImage)
                    {
                        SkipBall("this install has not got it");
                        continue;
                    }

                    if (!Core.Streamer.Here(model))
                    {
                        if (_tryingSince == 0) _tryingSince = now;
                        if (now - _tryingSince < GiveUpMs) return false;

                        SkipBall("it never streamed in");
                        continue;
                    }

                    var prop = World.CreateProp(model, at + new Vector3(0f, 0f, 0.5f), false, false);
                    model.MarkAsNoLongerNeeded();

                    if (prop == null || !prop.Exists())
                    {
                        SkipBall("it would not create");
                        continue;
                    }

                    prop.IsPersistent = true;
                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, prop.Handle, true, true);

                    // HOW HIGH A BALL SITS. Put on the ground once by the game, with its
                    // collision still on, and the height it settles at over the probed floor
                    // is the height it is carried at from then on -- a soccer ball and a
                    // basketball do not sit at the same height, and a model's origin is not
                    // its underside.
                    _lift = 0.11f;

                    try
                    {
                        if (Function.Call<bool>(Hash.PLACE_OBJECT_ON_GROUND_PROPERLY, prop.Handle))
                        {
                            var up = prop.Position.Z - at.Z;
                            if (up > 0.04f && up < 0.3f) _lift = up;
                        }
                    }
                    catch
                    {
                        // A soccer ball's worth, then.
                    }

                    // Frozen and passing through everything, from here on. See the class note.
                    Function.Call(Hash.FREEZE_ENTITY_POSITION, prop.Handle, true);
                    Function.Call(Hash.SET_ENTITY_COLLISION, prop.Handle, false, false);

                    _ball = prop;
                    _ballAs = name;

                    Put(at);
                    Start(now);

                    Log.Info("Kickabout: three of ours and a " + name + " at spot " + (_live + 1) +
                             ", " + _middle + ".");
                    return true;
                }
                catch
                {
                    SkipBall("it threw");
                }
            }

            Log.Warn("Kickabout: none of " + Balls.Length + " ball models would spawn; no game today.");
            Break("no ball");
            return false;
        }

        private void SkipBall(string why)
        {
            if (_tryingBall < Balls.Length)
            {
                Log.Debug("Kickabout: " + Balls[_tryingBall] + " skipped -- " + why + ".");
            }

            _tryingBall++;
            _tryingSince = 0;
        }

        /// <summary>The ball at somebody's feet, dead still.</summary>
        private void Put(Vector3 feet)
        {
            if (_ball == null || !_ball.Exists()) return;

            try
            {
                Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, _ball.Handle,
                              feet.X, feet.Y, feet.Z + _lift, false, false, false);
            }
            catch
            {
                // It is where it was.
            }
        }

        /// <summary>The game starts: everybody watches the ball, and whoever has it holds it a moment.</summary>
        private void Start(int now)
        {
            if (_holder < 0 || _holder >= _men.Count) _holder = 0;

            _target = -1;
            _play = Play.Held;
            _holdUntil = now + HoldMinMs + _rng.Next(HoldMaxMs - HoldMinMs);

            foreach (var m in _men) Watch(m);
        }

        /// <summary>His eyes on the ball, wherever it goes, for as long as it is out.</summary>
        private void Watch(Man m)
        {
            if (m == null || m.Who == null || !m.Who.Exists() || _ball == null || !_ball.Exists()) return;

            try
            {
                Function.Call(Hash.TASK_LOOK_AT_ENTITY, m.Who.Handle, _ball.Handle, -1, 0, 2);
            }
            catch
            {
                // He looks where he likes.
            }
        }

        // ---- the game, every frame -----------------------------------------------------------

        private void Run(int now)
        {
            if (_ball == null || !_ball.Exists()) return;
            if (_holder < 0 || _holder >= _men.Count) return;

            // YOU SHOOTING NEAR IT ENDS IT, checked every frame rather than on the throttle
            // because a single shot is one frame long and a check every seven hundred
            // milliseconds would miss most of them.
            if (_youNear)
            {
                try
                {
                    if (Function.Call<bool>(Hash.IS_PED_SHOOTING, _you))
                    {
                        Break("shots");
                        return;
                    }
                }
                catch
                {
                    // Then it carries on.
                }
            }

            switch (_play)
            {
                case Play.Held:
                    if (now < _holdUntil) return;

                    // YOU ARE STOOD IN THE RING. He keeps it under his foot and looks at you,
                    // which is what a man does when somebody walks into the middle of his
                    // game, and it starts again when you step out.
                    if (Inside(now))
                    {
                        _holdUntil = now + 600;
                        return;
                    }

                    Kick(now);
                    return;

                case Play.Kicking:
                    if (Released(now)) Launch(now);
                    return;

                case Play.Rolling:
                    Roll(now);
                    return;
            }
        }

        /// <summary>Whether you are inside the ring, and a look from the man with the ball if so.</summary>
        private bool Inside(int now)
        {
            Ped you;

            try { you = Game.Player.Character; }
            catch { return false; }

            if (you == null || !you.Exists()) return false;

            var p = you.Position;
            var flat = new Vector3(p.X - _middle.X, p.Y - _middle.Y, 0f).Length();

            if (flat > Ring - 0.4f || Math.Abs(p.Z - _middle.Z) > 2.5f) return false;

            if (now >= _nextGlance)
            {
                _nextGlance = now + GlanceEveryMs;
                GangPeds.Notice(_men[_holder].Who, you, 2500);
            }

            return true;
        }

        /// <summary>
        /// The man with the ball kicks it to one of the other two.
        ///
        /// The dict is asked for and not waited on: until it is in, he holds the ball a
        /// little longer, which nobody can tell from thinking about it.
        /// </summary>
        private void Kick(int now)
        {
            try
            {
                Function.Call(Hash.REQUEST_ANIM_DICT, KickDict);

                if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, KickDict))
                {
                    _holdUntil = now + 250;
                    return;
                }
            }
            catch
            {
                // Then the clip will not play, and Released sends the ball on regardless.
            }

            var kicker = _men[_holder].Who;

            if (kicker == null || !kicker.Exists())
            {
                Break("the man with the ball is gone");
                return;
            }

            _target = Other(_holder);
            _kickClip = KickClips[_rng.Next(KickClips.Length)];
            _kickStart = now;
            _kickSaid = false;

            try
            {
                // Once, full body, blended both ways. Not locked to the mark: the clip steps
                // into it a little and Tidy walks him back if the steps add up.
                Function.Call(Hash.TASK_PLAY_ANIM, kicker.Handle, KickDict, _kickClip,
                              8f, -8f, -1, 0, 0f, false, false, false);
            }
            catch
            {
                // See Released.
            }

            // He looks at who he is kicking it to. The other two are already on the ball.
            try
            {
                var to = _men[_target].Who;
                if (to != null && to.Exists()) Function.Call(Hash.TASK_LOOK_AT_ENTITY, kicker.Handle, to.Handle, 1500, 0, 2);
            }
            catch
            {
                // He kicks it without looking, which some of them do.
            }

            _play = Play.Kicking;
        }

        /// <summary>One of the other two, at random.</summary>
        private int Other(int notHim)
        {
            var pick = _rng.Next(_men.Count - 1);
            return pick >= notHim ? pick + 1 : pick;
        }

        /// <summary>
        /// Whether the foot has come through.
        ///
        /// Read off the clip's phase, so the ball leaves on contact. A clip that has not
        /// started a moment after it was asked for, or that has already finished, is not
        /// waited on -- the ball goes and the log says why. A timer backs all of it.
        /// </summary>
        private bool Released(int now)
        {
            var kicker = _men[_holder].Who;

            try
            {
                if (kicker != null && kicker.Exists() &&
                    Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM, kicker.Handle, KickDict, _kickClip, 3))
                {
                    var phase = Function.Call<float>(Hash.GET_ENTITY_ANIM_CURRENT_TIME, kicker.Handle, KickDict, _kickClip);
                    if (phase >= KickAt) return true;
                }
                else if (now - _kickStart > KickStartMs)
                {
                    if (!_kickSaid)
                    {
                        _kickSaid = true;
                        Log.Debug("Kickabout: " + _kickClip + " did not play on him; the ball went anyway.");
                    }

                    return true;
                }
            }
            catch
            {
                return true;
            }

            return now - _kickStart > KickFallbackMs;
        }

        /// <summary>The ball leaves his foot for the other man's.</summary>
        private void Launch(int now)
        {
            if (_target < 0 || _target >= _men.Count)
            {
                _play = Play.Held;
                _holdUntil = now + 500;
                return;
            }

            _from = _ball.Position;
            _to = _men[_target].Feet + new Vector3(0f, 0f, _lift);

            var flat = new Vector3(_to.X - _from.X, _to.Y - _from.Y, 0f);
            var len = flat.Length();
            var speed = RollSpeedMin + (float)_rng.NextDouble() * (RollSpeedMax - RollSpeedMin);

            _rollMs = Math.Max(150, (int)(len / speed * 1000f));
            _rollYaw = len > 0.01f ? Heading(flat) : 0f;
            _rollStart = now;
            _play = Play.Rolling;
            _kicks++;

            Say(_holder, now);
        }

        /// <summary>
        /// The ball between them, this frame.
        ///
        /// Eased out: a kicked ball is fastest the moment it leaves the foot and slows all the
        /// way to the next one, and a ball at one speed the whole way is a ball on a string.
        /// It turns over as it goes, by how far it has gone over how big it is, about the
        /// axis across its line of travel -- which is what rolling is.
        /// </summary>
        private void Roll(int now)
        {
            var t = (now - _rollStart) / (float)_rollMs;

            if (t >= 1f)
            {
                Arrive(now);
                return;
            }

            var p = 1f - (1f - t) * (1f - t);
            var at = _from + (_to - _from) * p;

            var gone = new Vector3(at.X - _from.X, at.Y - _from.Y, 0f).Length();
            var pitch = -(gone / Math.Max(0.05f, _lift)) * 57.2958f;

            try
            {
                Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, _ball.Handle, at.X, at.Y, at.Z, false, false, false);
                Function.Call(Hash.SET_ENTITY_ROTATION, _ball.Handle, pitch % 360f, 0f, _rollYaw, 2, true);
            }
            catch
            {
                // It is where it was last frame.
            }
        }

        /// <summary>At the other man's feet. He has it now, and the man who kicked it is tidied.</summary>
        private void Arrive(int now)
        {
            Put(_men[_target].Feet);
            Tidy(_holder, now);

            _holder = _target;
            _target = -1;
            _play = Play.Held;
            _holdUntil = now + HoldMinMs + _rng.Next(HoldMaxMs - HoldMinMs);
        }

        /// <summary>
        /// The man who just kicked: facing the middle again, back on his mark if the clip
        /// walked him off it, and his eyes back on the ball.
        /// </summary>
        private void Tidy(int i, int now)
        {
            if (i < 0 || i >= _men.Count) return;

            var m = _men[i];
            if (m.Who == null || !m.Who.Exists()) return;

            try
            {
                var p = m.Who.Position;
                var off = new Vector3(p.X - m.Mark.X, p.Y - m.Mark.Y, 0f).Length();

                if (off > Drift && now - m.TidiedAt > TidyGapMs)
                {
                    m.TidiedAt = now;

                    Function.Call(Hash.TASK_GO_STRAIGHT_TO_COORD, m.Who.Handle,
                                  m.Mark.X, m.Mark.Y, m.Mark.Z, 1f, 3000, m.Facing, 0.2f);
                }
                else
                {
                    Function.Call(Hash.TASK_ACHIEVE_HEADING, m.Who.Handle, m.Facing, 1200);
                }
            }
            catch
            {
                // He stands how he stands.
            }

            Watch(m);
        }

        /// <summary>Something said on the way through, now and again. The set's speech is unblocked for the one line.</summary>
        private void Say(int i, int now)
        {
            if (now < _nextChat || _rng.Next(4) != 0) return;

            _nextChat = now + ChatGapMs;

            if (i < 0 || i >= _men.Count) return;

            var who = _men[i].Who;
            if (who == null || !who.Exists()) return;

            try
            {
                Function.Call(Hash.BLOCK_ALL_SPEECH_FROM_PED, who.Handle, false, false);
                Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, who.Handle,
                              Chat[_rng.Next(Chat.Length)], "SPEECH_PARAMS_FORCE");
            }
            catch
            {
                // Quiet, then.
            }
        }

        // ---- the feed --------------------------------------------------------------------------

        /// <summary>The block says something about it, once per spot, once you are close enough to have seen it.</summary>
        private void Tell(Ped player)
        {
            if (_live < 0 || _live >= _told.Length || _told[_live]) return;
            if (player.Position.DistanceTo(_middle) > TellRange) return;

            _told[_live] = true;

            try
            {
                if (Feed != null) Feed.On(Hoodrich.Social.SocialEvent.Kickabout);
            }
            catch
            {
                // The feed can miss one.
            }
        }

        // ---- ending it -----------------------------------------------------------------------

        /// <summary>
        /// The game is over and the three of them are the game's again.
        ///
        /// Not CLEAR_PED_TASKS: a man already fighting is left to it. The block on events comes
        /// off so they react to whatever ended it, and the ball is let go rather than deleted
        /// -- a ball rolling loose on the pavement is what a kickabout that just scattered
        /// leaves behind.
        /// </summary>
        private void Break(string why)
        {
            foreach (var m in _men)
            {
                try
                {
                    var who = m.Who;
                    if (who == null || !who.Exists()) continue;

                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, who.Handle, false);
                    Function.Call(Hash.TASK_CLEAR_LOOK_AT, who.Handle);

                    who.MarkAsNoLongerNeeded();
                }
                catch
                {
                    // He is the game's anyway.
                }
            }

            _men.Clear();
            Loose();

            _staffed = false;
            _broken = true;
            _play = Play.Nowhere;
            _holder = -1;
            _target = -1;

            Log.Info("Kickabout: broke up at spot " + (_live + 1) + " -- " + why + ", after " + _kicks + " kick(s).");
        }

        /// <summary>The ball let go: solid again, unfrozen, and no longer ours.</summary>
        private void Loose()
        {
            try
            {
                if (_ball != null && _ball.Exists())
                {
                    Function.Call(Hash.SET_ENTITY_COLLISION, _ball.Handle, true, true);
                    Function.Call(Hash.FREEZE_ENTITY_POSITION, _ball.Handle, false);
                    _ball.MarkAsNoLongerNeeded();
                }
            }
            catch
            {
                // Wherever it is.
            }

            _ball = null;
            _ballAs = "";
        }

        private void Clear(string why)
        {
            foreach (var m in _men)
            {
                try
                {
                    if (m.Who != null && m.Who.Exists())
                    {
                        m.Who.MarkAsNoLongerNeeded();
                        m.Who.Delete();
                    }
                }
                catch
                {
                    // Already gone.
                }
            }

            _men.Clear();

            try
            {
                if (_ball != null && _ball.Exists())
                {
                    _ball.MarkAsNoLongerNeeded();
                    _ball.Delete();
                }
            }
            catch
            {
                // Already gone.
            }

            try { Function.Call(Hash.REMOVE_ANIM_DICT, KickDict); }
            catch { /* it goes when the game says */ }

            _ball = null;
            _ballAs = "";
            _play = Play.Nowhere;
            _holder = -1;
            _target = -1;
            _staffed = false;
            _broken = false;
            _tryingBall = 0;
            _tryingSince = 0;

            if (_live >= 0)
            {
                Log.Info("Kickabout: packed up at spot " + (_live + 1) + " -- " + why + ", after " + _kicks + " kick(s).");
            }

            _live = -1;
            _kicks = 0;
        }

        public void RestoreWorld() => Clear("unloaded");

        // ---- geometry ------------------------------------------------------------------------

        /// <summary>The ground under a point, probed from above it; the middle's floor if the world will not say.</summary>
        private Vector3 Ground(Vector3 at)
        {
            try
            {
                float z;

                if (Core.Ground.Probe(new Vector3(at.X, at.Y, at.Z + 2f), out z) && Math.Abs(z - _middle.Z) < 3f)
                {
                    return new Vector3(at.X, at.Y, z);
                }
            }
            catch
            {
                // The middle's floor stands.
            }

            return new Vector3(at.X, at.Y, _middle.Z);
        }

        /// <summary>A heading, in the game's degrees, for a direction: north is 0 and it turns anticlockwise.</summary>
        private static float Heading(Vector3 dir)
        {
            var h = (float)(Math.Atan2(-dir.X, dir.Y) * 180.0 / Math.PI);
            return h < 0f ? h + 360f : h;
        }

        // ---- the numbers ---------------------------------------------------------------------

        private const int Three = 3;

        /// <summary>The ring: how far out they stand, and how far in from that the ball sits.</summary>
        private const float Ring = 3f;
        private const float StepIn = 0.6f;

        /// <summary>How near you have to be for a game to be on, and how far to give it up.</summary>
        private const float SpawnRange = 100f;
        private const float DespawnRange = 160f;

        /// <summary>Nearer than this it appears whether or not you are looking. See Update.</summary>
        private const float PopInRange = 45f;

        /// <summary>Game hours a spot stays live before the clock moves it on.</summary>
        private const int HoursPerSpot = 3;

        /// <summary>How long a man keeps the ball under his foot before he kicks it.</summary>
        private const int HoldMinMs = 500;
        private const int HoldMaxMs = 1400;

        /// <summary>How fast a pass rolls off the foot, metres a second.</summary>
        private const float RollSpeedMin = 4.5f;
        private const float RollSpeedMax = 6.5f;

        /// <summary>How far into the kick clip the foot comes through, and the timers behind it.</summary>
        private const float KickAt = 0.38f;
        private const int KickStartMs = 220;
        private const int KickFallbackMs = 900;

        /// <summary>How long one ball model gets to stream in before the next is asked for.</summary>
        private const int GiveUpMs = 6000;

        /// <summary>How far off his mark a man can get before he is walked back, and how often.</summary>
        private const float Drift = 0.45f;
        private const int TidyGapMs = 4000;

        /// <summary>Shots this near the ring end the game; a look at the block this near earns a post.</summary>
        private const float ShotsRange = 35f;
        private const float TellRange = 30f;

        private const int ChatGapMs = 9000;
        private const int GlanceEveryMs = 3000;
        private const int UpdateIntervalMs = 700;

        private const string KickDict = "melee@unarmed@streamed_core";
        private static readonly string[] KickClips = { "kick_close_a", "kick_close_b" };

        /// <summary>A soccer ball, or failing that a basketball, or failing that a beach volleyball.</summary>
        private static readonly string[] Balls = { "p_ld_soc_ball_01", "prop_bskball_01", "prop_beach_volball01" };

        /// <summary>Nothing rude. These are ours, and Franklin is stood right there.</summary>
        private static readonly string[] Chat =
        {
            "CHAT_STATE", "GENERIC_YES", "CHAT_RESP", "GENERIC_THANKS", "GENERIC_HOWS_IT_GOING"
        };
    }
}
