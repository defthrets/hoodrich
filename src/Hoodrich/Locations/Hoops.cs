using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.UI;
using Control = GTA.Control;

namespace Hoodrich.Locations
{
    /// <summary>
    /// SHOOTING HOOPS on the Chamberlain Hills court, on the court's own hoop. Michael asked for
    /// it on 2026-09-26: a basketball, a jump and a throw, and the aim and the shot the way his
    /// Street Golf does them.
    ///
    /// THE SHOT IS STREET GOLF'S. The aim is where the camera looks. Hold the button and the
    /// meter runs up and back down; let go, and where it stood is the shot. The meter's sweet
    /// spot is the power that reaches the rim from where you stand, worked out fresh every
    /// shot, so the green moves along the bar as you walk back to the three-point line. In
    /// the green and looking at the hoop, the ball goes in: it is thrown on the exact arc to
    /// the middle of the rim. Out of the green it is thrown as hard as the meter said, and
    /// falls short or goes long; looking off the hoop, it goes wide. The game's own physics
    /// flies it either way, off the court's own backboard and rim.
    ///
    /// THE JUMP SHOT. The game has no basketball of its own and no shot animation. What it has
    /// is a jump, and the high overhand throw -- the grenade throw with the arm up -- and the
    /// throw played on the upper body over the jump is a jump shot. The ball leaves the hand as
    /// the throw releases it. Held, he carries it in the ball-game idle that goes with it.
    ///
    /// WHERE THE RIM IS is read off the hoop itself, once: a few rays at the backboard from the
    /// court side find its face and its bottom edge, and the rim is a hand's width above that
    /// edge and out from the board by the gap and its own radius. Nothing about the hoop is
    /// typed in, so a hoop on any court works the same.
    ///
    /// LIGHT ON PURPOSE. Michael's game ran out of memory on 2026-09-26, and this adds one ball
    /// and two small clip sets, asked for when a game starts and let go when it ends. No people,
    /// no vehicles, no cameras, nothing streamed. The ball is the same prop all game: it flies,
    /// and it comes back to his hands.
    /// </summary>
    internal sealed class Hoops
    {
        private enum Mode { Off, Starting, Holding, Charging, Shooting, Flying, Result }

        /// <summary>The court: Michael's HUD reading at the top of the key, facing the hoop.</summary>
        private static readonly Vector3 Spot = new Vector3(-201.428f, -1508.783f, 31.631f);

        private const string BallName = "prop_bskball_01";
        private const string HoopName = "prop_basketball_net";

        private const string HoldDict = "anim@sports@ballgame@handball@";
        private const string HoldClip = "ball_idle";
        private const string ShotDict = "weapons@projectile@";
        private const string ShotClip = "throw_h_fb_stand";

        /// <summary>How near the spot to be offered a game, and how far the ring is drawn from.</summary>
        private const float OfferReach = 1.3f;
        private const float RingReach = 30f;

        /// <summary>How far round him a hoop is looked for, and how far from it before the game is put away.</summary>
        private const float HoopFind = 30f;
        private const float CourtReach = 26f;

        /// <summary>PH_R_Hand: the ball rides in his right hand.</summary>
        private const int RightHand = 28422;

        /// <summary>Street Golf's meter: up and back down over this long while the button is held.</summary>
        private const float ChargeTime = 1.15f;

        /// <summary>What the bottom and the top of the meter throw the ball at, in metres a second.</summary>
        private const float SpeedLow = 3.5f;
        private const float SpeedHigh = 13.5f;

        /// <summary>The arc a shot leaves the hand on. A good shot drops into the rim from above.</summary>
        private const float Arc = 52f;

        /// <summary>Looking within this many degrees of the hoop is looking at it.</summary>
        private const float OnLine = 4.5f;

        /// <summary>The ball's middle has to come down through this much of the rim's middle.</summary>
        private const float MakeRadius = 0.24f;

        /// <summary>The three-point line, from the middle of the rim over the floor.</summary>
        private const float ThreeFrom = 6.75f;

        /// <summary>How high above him the ball leaves the hand, at the top of the jump.</summary>
        private const float ReleaseUp = 1.5f;

        /// <summary>The ball goes when the throw is this far through, or this long after it began.</summary>
        private const float ReleasePhase = 0.36f;
        private const int ReleaseMs = 480;

        private const float Gravity = 9.8f;

        private Mode _mode = Mode.Off;
        private int _at;
        private Prop _ball;

        /// <summary>The hoop being shot at, and its rim, read once a hoop. See Rim.</summary>
        private Prop _hoop;
        private Vector3 _rim;
        private readonly Dictionary<int, Vector3> _rims = new Dictionary<int, Vector3>();

        private float _charge;
        private float _power;
        private float _sweet;
        private float _half;
        private int _lastTick;

        private bool _good;
        private bool _onLine;
        private float _aim;
        private float _from;
        private bool _three;

        /// <summary>Where he stood for the shot, for calling a miss short or long.</summary>
        private Vector3 _shotAt;

        private bool _released;
        private bool _scored;
        private bool _touched;
        private bool _crossed;
        private Vector3 _cross;
        private Vector3 _prev;

        private int _score;
        private int _streak;
        private int _best;
        private int _shots;
        private int _made;

        private int _holdAt;

        private string _banner = "";
        private string _bannerSub = "";
        private Color _bannerInk = Color.White;
        private int _bannerUntil;

        private static readonly Color Gold = Color.FromArgb(255, 240, 200, 80);
        private static readonly Color Green = Color.FromArgb(255, 114, 204, 114);
        private static readonly Color Red = Color.FromArgb(255, 224, 50, 50);
        private static readonly Color Track = Color.FromArgb(160, 20, 20, 20);

        /// <summary>A game is up: the shooting buttons are his, and the rest of the mod's prompts stand down.</summary>
        public bool Playing => _mode != Mode.Off;

        public void Update()
        {
            var me = Game.Player.Character;
            if (me == null || !me.Exists()) return;

            var now = Game.GameTime;
            var dt = _lastTick == 0 ? 0f : Math.Min(0.1f, (now - _lastTick) / 1000f);
            _lastTick = now;

            if (_mode == Mode.Off)
            {
                Offer(me);
                return;
            }

            // Put away if he is not on the court to play any more.
            if (!me.IsAlive || me.IsInVehicle() || me.IsRagdoll ||
                _hoop == null || !_hoop.Exists() || me.Position.DistanceTo(_hoop.Position) > CourtReach)
            {
                Stop(me.IsAlive && !me.IsInVehicle() ? "Off the court -- the ball stays behind." : null);
                return;
            }

            HoldTheButtons(_mode == Mode.Charging);

            switch (_mode)
            {
                case Mode.Starting: Starting(me, now); break;
                case Mode.Holding: Holding(me, now); break;
                case Mode.Charging: Charging(me, dt); break;
                case Mode.Shooting: Shooting(me, now); break;
                case Mode.Flying: Flying(now); break;
                case Mode.Result: if (now - _at >= 700) GiveBall(me); break;
            }

            if (_mode != Mode.Off) Draw(me, now);
        }

        // ---- on and off the court ---------------------------------------------------------

        private void Offer(Ped me)
        {
            var d = me.Position.DistanceTo(Spot);
            if (d > RingReach) return;

            Ring(me);

            if (d > OfferReach || me.IsInVehicle() || Mind.Busy || InputGuard.Busy) return;

            Help.ShowThisFrame("Press ~INPUT_CONTEXT~ to shoot hoops.");

            if (Game.IsControlJustPressed(Control.Context)) Start(me);
        }

        private static void Ring(Ped me)
        {
            try
            {
                Function.Call(Hash.DRAW_MARKER, 1, Spot.X, Spot.Y, Spot.Z - 1.0f,
                              0f, 0f, 0f, 0f, 0f, 0f,
                              1.1f, 1.1f, 0.35f,
                              240, 160, 60, 105,
                              false, false, 2, false, 0, 0, false);
            }
            catch
            {
                // A court without its ring is still a court.
            }
        }

        private void Start(Ped me)
        {
            InputGuard.Swallow();

            if (!Aim(me))
            {
                Notify.Problem("There is no hoop on this court.");
                return;
            }

            // Asked for now and let go at the end: the only things this ever loads.
            try
            {
                Function.Call(Hash.REQUEST_ANIM_DICT, HoldDict);
                Function.Call(Hash.REQUEST_ANIM_DICT, ShotDict);
                Models.Ready(new Model(BallName));
            }
            catch { /* asked again while it waits */ }

            _score = _streak = _shots = _made = 0;
            _banner = "";
            _mode = Mode.Starting;
            _at = Game.GameTime;

            Log.Info("Hoops: on the court, shooting at the hoop at " + _hoop.Position + ", the rim at " + _rim + ".");
        }

        private void Starting(Ped me, int now)
        {
            var ready = false;

            try
            {
                ready = Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, HoldDict) &&
                        Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, ShotDict) &&
                        Models.Ready(new Model(BallName));
            }
            catch { }

            if (ready)
            {
                GiveBall(me);
                return;
            }

            if (now - _at > 4000)
            {
                Log.Warn("Hoops: the ball or the clips would not load in four seconds.");
                Stop("The ball would not load. Try again in a moment.");
            }
        }

        /// <summary>The game put away: the ball gone, his hands his own, and what was asked for let go.</summary>
        private void Stop(string why)
        {
            var me = Game.Player.Character;

            try
            {
                if (_ball != null && _ball.Exists()) _ball.Delete();
            }
            catch { }

            _ball = null;

            try
            {
                if (me != null && me.Exists()) Function.Call(Hash.CLEAR_PED_SECONDARY_TASK, me.Handle);
                Den.Buttons.Drop();
                Function.Call(Hash.REMOVE_ANIM_DICT, HoldDict);
                Function.Call(Hash.REMOVE_ANIM_DICT, ShotDict);
                new Model(BallName).MarkAsNoLongerNeeded();
            }
            catch { }

            if (_mode != Mode.Starting && _shots > 0)
            {
                Log.Info("Hoops: " + _score + " point(s), " + _made + " of " + _shots + ", best streak " + _best + ".");
                Notify.Ticker("~g~Hoops.~s~ " + _score + " points, " + _made + " of " + _shots +
                              (_best > 1 ? ", " + _best + " in a row." : "."));
            }
            else if (!string.IsNullOrEmpty(why))
            {
                Notify.Problem(why);
            }

            _mode = Mode.Off;
            _hoop = null;
            InputGuard.Swallow();
        }

        public void RestoreWorld()
        {
            if (_mode != Mode.Off) Stop(null);
        }

        // ---- the ball in his hands ------------------------------------------------------------

        /// <summary>The ball in his right hand -- the same ball every time -- and the ball-game idle on his arms.</summary>
        private void GiveBall(Ped me)
        {
            try
            {
                if (_ball == null || !_ball.Exists())
                {
                    var model = new Model(BallName);
                    if (!Models.Ready(model)) return;

                    var at = me.Position + me.ForwardVector * 0.4f;
                    var handle = Function.Call<int>(Hash.CREATE_OBJECT, model.Hash, at.X, at.Y, at.Z, false, false, true);
                    _ball = handle == 0 ? null : GTA.Entity.FromHandle(handle) as Prop;

                    if (_ball == null)
                    {
                        Stop("The ball would not load. Try again in a moment.");
                        return;
                    }

                    Function.Call(Hash.SET_ENTITY_RECORDS_COLLISIONS, _ball.Handle, true);
                }

                Function.Call(Hash.SET_ENTITY_VELOCITY, _ball.Handle, 0f, 0f, 0f);

                var bone = Function.Call<int>(Hash.GET_PED_BONE_INDEX, me.Handle, RightHand);
                Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, _ball.Handle, me.Handle, bone,
                              0.14f, 0.02f, -0.03f, 0f, 0f, 0f,
                              false, false, false, false, 2, true);

                Hold(me, true);

                _mode = Mode.Holding;
                _at = Game.GameTime;
            }
            catch (Exception ex)
            {
                Log.Debug("Hoops: could not put the ball in his hands: " + ex.Message);
                Stop("The ball would not load. Try again in a moment.");
            }
        }

        /// <summary>The idle on his arms, put back if something took it off -- not every frame, which T-poses a man.</summary>
        private void Hold(Ped me, bool now)
        {
            try
            {
                if (!now)
                {
                    if (Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM, me.Handle, HoldDict, HoldClip, 3)) return;
                    if (Game.GameTime - _holdAt < 500) return;
                }

                _holdAt = Game.GameTime;

                // Loop, upper body only, over whatever his legs are doing: he walks with it.
                Function.Call(Hash.TASK_PLAY_ANIM, me.Handle, HoldDict, HoldClip, 4f, -4f, -1, 49, 0f, false, false, false);
            }
            catch { }
        }

        private void Holding(Ped me, int now)
        {
            Hold(me, false);

            if (JustPressed(Control.PhoneCancel))
            {
                Stop(null);
                return;
            }

            if (!Down(Control.Attack) || now - _at < 250) return;

            // The hoop he is looking at, if he has walked to the other end.
            Aim(me);

            _charge = 0f;
            _power = 0f;
            _mode = Mode.Charging;
            _at = now;
        }

        // ---- the meter ------------------------------------------------------------------------

        private void Charging(Ped me, float dt)
        {
            _charge += dt;

            var t = _charge / ChargeTime;
            var cycle = t % 2f;
            _power = cycle <= 1f ? cycle : 2f - cycle;

            // Turned to where he is looking, the way Street Golf turns the golfer.
            _aim = Deg360(GameplayCamera.Rotation.Z);
            Turn(me, _aim, dt);

            Sweet(me);

            if (!Down(Control.Attack)) Shoot(me);
        }

        /// <summary>Where the green is on the meter from where he stands: the power that reaches the rim.</summary>
        private void Sweet(Ped me)
        {
            var from = me.Position + Vector3.WorldUp * ReleaseUp;
            var flat = Flat(_rim - me.Position);

            Vector3 v;
            float speed;

            if (!Solve(from, _rim, out v, out speed))
            {
                _sweet = -1f;
                return;
            }

            _sweet = Clamp((speed - SpeedLow) / (SpeedHigh - SpeedLow), 0.04f, 0.96f);

            // Wider close in, narrower from deep.
            _half = Clamp(0.07f - 0.004f * (flat - 4f), 0.035f, 0.085f);
        }

        private static void Turn(Ped me, float want, float dt)
        {
            try
            {
                var cur = me.Heading;
                var diff = AngleDiff(want, cur);
                var step = 540f * dt;

                cur = Math.Abs(diff) <= step ? want : Deg360(cur + Math.Sign(diff) * step);
                Function.Call(Hash.SET_ENTITY_HEADING, me.Handle, cur);
            }
            catch { }
        }

        // ---- the shot -------------------------------------------------------------------------

        private void Shoot(Ped me)
        {
            _good = _sweet >= 0f && Math.Abs(_power - _sweet) <= _half;
            // Looking at it from where the camera is: over his shoulder, not out of his head.
            _onLine = Math.Abs(AngleDiff(_aim, Toward(GameplayCamera.Position, _rim))) <= OnLine;
            _from = Flat(_rim - me.Position);
            _three = _from > ThreeFrom;
            _shotAt = me.Position;

            try
            {
                // The legs jump; the arms throw, over the jump.
                Function.Call(Hash.TASK_JUMP, me.Handle, true, false, false);
                Function.Call(Hash.TASK_PLAY_ANIM, me.Handle, ShotDict, ShotClip, 8f, -8f, -1, 48, 0f, false, false, false);
            }
            catch { }

            _released = false;
            _mode = Mode.Shooting;
            _at = Game.GameTime;
        }

        private void Shooting(Ped me, int now)
        {
            if (_released) return;

            var phase = 0f;

            try
            {
                if (Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM, me.Handle, ShotDict, ShotClip, 3))
                    phase = Function.Call<float>(Hash.GET_ENTITY_ANIM_CURRENT_TIME, me.Handle, ShotDict, ShotClip);
            }
            catch { }

            if (phase < ReleasePhase && now - _at < ReleaseMs) return;

            Release(me);
        }

        /// <summary>
        /// Out of his hand and on its way. In the green and on line, on the exact arc to the rim;
        /// otherwise as hard as the meter said, the way he was looking.
        /// </summary>
        private void Release(Ped me)
        {
            _released = true;

            if (_ball == null || !_ball.Exists())
            {
                _mode = Mode.Result;
                _at = Game.GameTime;
                return;
            }

            try
            {
                Function.Call(Hash.DETACH_ENTITY, _ball.Handle, true, true);

                var from = _ball.Position;
                Vector3 v;
                float speed;

                if (!(_good && _onLine && Solve(from, _rim, out v, out speed)))
                {
                    var heading = _onLine ? Toward(from, _rim) : _aim;
                    var dir = Dir(heading);
                    var th = Arc * (float)Math.PI / 180f;
                    speed = SpeedLow + (SpeedHigh - SpeedLow) * _power;
                    v = dir * (speed * (float)Math.Cos(th)) + Vector3.WorldUp * (speed * (float)Math.Sin(th));
                }

                // Flown by the game, with nothing slowing it but the rim -- Street Golf's ball.
                Function.Call(Hash.FREEZE_ENTITY_POSITION, _ball.Handle, false);
                Function.Call(Hash.SET_ENTITY_COLLISION, _ball.Handle, true, true);
                Function.Call(Hash.ACTIVATE_PHYSICS, _ball.Handle);
                Function.Call(Hash.SET_OBJECT_PHYSICS_PARAMS, _ball.Handle, -1f, -1f, 0f, 0f, 0.01f, -1f, -1f, -1f, -1f, -1f, -1f);
                Function.Call(Hash.APPLY_FORCE_TO_ENTITY, _ball.Handle, 1, 0.001f, 0.001f, 0f, 0f, 0f, 0f, 0, false, false, true, false, true);
                Function.Call(Hash.SET_ENTITY_MAX_SPEED, _ball.Handle, 60f);
                Function.Call(Hash.SET_ENTITY_VELOCITY, _ball.Handle, v.X, v.Y, v.Z);
                Function.Call(Hash.SET_ENTITY_NO_COLLISION_ENTITY, _ball.Handle, me.Handle, true);

                // What it touches on the way is only what it touches from here.
                Function.Call(Hash.SET_ENTITY_RECORDS_COLLISIONS, _ball.Handle, false);
                Function.Call(Hash.SET_ENTITY_RECORDS_COLLISIONS, _ball.Handle, true);

                _prev = from;
            }
            catch (Exception ex)
            {
                Log.Debug("Hoops: the ball would not fly: " + ex.Message);
            }

            _shots++;
            _scored = false;
            _touched = false;
            _crossed = false;
            _mode = Mode.Flying;
            _at = Game.GameTime;
        }

        /// <summary>The ball in the air: through the rim from above is a basket, and the end of the flight is the verdict.</summary>
        private void Flying(int now)
        {
            if (_ball == null || !_ball.Exists())
            {
                Verdict();
                return;
            }

            var pos = _ball.Position;

            try
            {
                if (!_scored && Function.Call<bool>(Hash.HAS_ENTITY_COLLIDED_WITH_ANYTHING, _ball.Handle)) _touched = true;
            }
            catch { }

            // Coming down through the rim's height: where, and whether that is inside the rim.
            if (_prev.Z > _rim.Z && pos.Z <= _rim.Z)
            {
                var t = (_prev.Z - _rim.Z) / Math.Max(0.0001f, _prev.Z - pos.Z);
                var at = _prev + (pos - _prev) * t;

                if (!_crossed)
                {
                    _crossed = true;
                    _cross = at;
                }

                if (!_scored && Flat(at - _rim) <= MakeRadius) Made();
            }

            _prev = pos;

            var age = now - _at;
            var still = false;

            try { still = _ball.Velocity.Length() < 0.4f && age > 900; }
            catch { }

            // A basket drops through the net before the next ball; a miss is called when it lands.
            if (_scored && age > 1200 || !_scored && (still || age > 3200) || age > 4500) Verdict();
        }

        private void Made()
        {
            _scored = true;

            var points = _three ? 3 : 2;
            _score += points;
            _made++;
            _streak++;
            if (_streak > _best) _best = _streak;

            var head = !_touched ? (_three ? "SWISH -- THREE" : "SWISH") : _three ? "THREE" : "BUCKET";
            var sub = "+" + points + (_streak > 1 ? " -- " + _streak + " in a row" : "") + ".  " + _score + " points.";
            Banner(head, sub, _three ? Gold : Green);

            Sound("CHECKPOINT_PERFECT", "HUD_MINI_GAME_SOUNDSET");
            Log.Info("Hoops: " + head + " from " + _from.ToString("0.0") + " m" + (_good ? ", in the green" : "") + ".");
        }

        /// <summary>The shot called, and a word on why it missed -- short, long or wide is what the meter needs to know.</summary>
        private void Verdict()
        {
            if (!_scored)
            {
                _streak = 0;

                string why;

                if (!_onLine) why = "Wide -- look at the hoop.";
                else if (!_crossed) why = "Short -- more on the meter.";
                else
                {
                    // Where it came down through the rim's height, against where the rim is.
                    var along = Flat(_cross - _shotAt) - Flat(_rim - _shotAt);

                    why = along < -0.25f ? "Short -- more on the meter."
                        : along > 0.25f ? "Long -- less on the meter."
                        : "Off the rim.";
                }

                Banner("MISS", why, Red);
                Sound("CHECKPOINT_MISSED", "HUD_MINI_GAME_SOUNDSET");
            }

            _mode = Mode.Result;
            _at = Game.GameTime;
        }

        // ---- the hoop and its rim ---------------------------------------------------------------

        /// <summary>
        /// The hoop he is looking at, of those on the court, and its rim. False when there is no
        /// hoop in reach at all.
        /// </summary>
        private bool Aim(Ped me)
        {
            Prop best = null;
            var bestScore = float.MaxValue;
            var look = Dir(GameplayCamera.Rotation.Z);

            try
            {
                foreach (var p in World.GetNearbyProps(me.Position, HoopFind, new Model(HoopName)))
                {
                    if (p == null || !p.Exists()) continue;

                    var to = Flat3D(p.Position - me.Position);
                    var d = to.Length();
                    if (d < 0.01f) continue;

                    // Mostly which one he faces, then which is nearer.
                    var facing = Vector3.Dot(to * (1f / d), look);
                    var score = (1f - facing) * 40f + d;

                    if (score >= bestScore) continue;

                    bestScore = score;
                    best = p;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Hoops: could not look for the hoop: " + ex.Message);
            }

            if (best == null) return false;

            _hoop = best;

            Vector3 rim;
            if (!_rims.TryGetValue(best.Handle, out rim))
            {
                rim = Rim(best, me.Position);
                _rims[best.Handle] = rim;
            }

            _rim = rim;
            return true;
        }

        /// <summary>
        /// Where the middle of the rim is, off the hoop's own backboard: rays from the court side
        /// at a slice of heights, two either side of the middle so the pole is missed and only the
        /// board is hit, give the board's face and its bottom edge; a sweep across at the board's
        /// middle gives its centre. The rim is 15 cm above the bottom edge and out from the face by
        /// the gap and its own radius, as a regulation hoop is built. Read once a hoop.
        /// </summary>
        private static Vector3 Rim(Prop hoop, Vector3 court)
        {
            var o = hoop.Position;
            var toward = Flat3D(court - o);
            if (toward.Length() < 0.1f) toward = Flat3D(hoop.ForwardVector);
            toward.Normalize();

            var me = Game.Player.Character;
            var ignore = me != null && me.Exists() ? me.Handle : 0;

            // The court under it: a backboard hangs well above head height, and nothing lower
            // -- the low wall round the court, a bench -- is taken for one.
            float ground;
            var floor = Ground.Probe(o + Vector3.WorldUp * 1f, out ground) ? ground : o.Z;
            var lowest = floor + 1.8f - o.Z;

            // Twice: once from where he stands, and again square to the board itself if he was
            // stood off to one side of it, so the rim is put out from its face and not his line.
            for (var pass = 0; pass < 2; pass++)
            {
                var side = new Vector3(toward.Y, -toward.X, 0f);

                float low, high, face;
                Vector3 normal;

                if (!Band(o, toward, side, ignore, lowest, out low, out high, out face, out normal)) break;

                if (pass == 0 && normal.Length() > 0.5f && Vector3.Dot(normal, toward) < 0.996f && Vector3.Dot(normal, toward) > 0.5f)
                {
                    toward = normal;
                    continue;
                }

                // Across the board at its middle: its centre, which is the rim's.
                var mid = (low + high) * 0.5f;
                float left = float.MaxValue, right = float.MinValue;

                for (var s = -1.6f; s <= 1.6f; s += 0.05f)
                {
                    float along;
                    float up;
                    if (!Board(o, toward, side, s, mid, ignore, out along, out up)) continue;
                    if (Math.Abs(along - face) > 0.12f) continue;

                    if (s < left) left = s;
                    if (s > right) right = s;
                }

                var centre = left <= right ? (left + right) * 0.5f : 0f;
                var rim = o + toward * (face + 0.38f) + side * centre + Vector3.WorldUp * (low + 0.15f);

                Log.Info("Hoops: the backboard of the hoop at " + o + " is " + (high - low).ToString("0.00") + " m tall and " +
                         (left <= right ? (right - left).ToString("0.00") : "?") + " m wide, " + face.ToString("0.00") +
                         " m out, its bottom " + low.ToString("0.00") + " m up; the rim is at " + rim + ".");
                return rim;
            }

            // Nothing answered: the box the model comes in, and a regulation height off the ground under it.
            var lo = new OutputArgument();
            var hi = new OutputArgument();
            Function.Call(Hash.GET_MODEL_DIMENSIONS, hoop.Model.Hash, lo, hi);
            var min = lo.GetResult<Vector3>();
            var max = hi.GetResult<Vector3>();

            var reach = Math.Max(Math.Max(Math.Abs(min.X), Math.Abs(max.X)), Math.Max(Math.Abs(min.Y), Math.Abs(max.Y)));
            var guess = new Vector3(o.X, o.Y, floor + 3.05f) + toward * Math.Max(0.3f, reach - 0.23f);

            Log.Warn("Hoops: no backboard answered at the hoop at " + o + "; the rim is guessed at " + guess +
                     " off the model's box (" + min + " to " + max + "). Tell Claude how the shots fall.");
            return guess;
        }

        /// <summary>
        /// The backboard as a band of heights: rays two either side of the middle, at a slice of
        /// heights, each pair landing on one flat upright face at the same depth. The longest
        /// unbroken run of those is the board -- a stray hit on a fence behind or the court
        /// under it is a different depth, or a floor, and is not part of it.
        /// </summary>
        private static bool Band(Vector3 o, Vector3 toward, Vector3 side, int ignore, float lowest,
                                 out float low, out float high, out float face, out Vector3 normal)
        {
            low = high = face = 0f;
            normal = Vector3.Zero;

            var dzs = new List<float>();
            var faces = new List<float>();

            for (var dz = Math.Max(-1.0f, lowest); dz <= 6.0f; dz += 0.08f)
            {
                float a, b, na, nb;
                if (!Board(o, toward, side, -0.45f, dz, ignore, out a, out na)) continue;
                if (!Board(o, toward, side, 0.45f, dz, ignore, out b, out nb)) continue;
                if (Math.Abs(a - b) > 0.15f) continue;

                dzs.Add(dz);
                faces.Add((a + b) * 0.5f);
            }

            // The longest run of neighbouring heights at one depth.
            int bestStart = -1, bestLen = 0;

            for (var i = 0; i < dzs.Count; )
            {
                var j = i + 1;
                while (j < dzs.Count && dzs[j] - dzs[j - 1] <= 0.12f && Math.Abs(faces[j] - faces[j - 1]) <= 0.12f) j++;

                if (j - i > bestLen)
                {
                    bestLen = j - i;
                    bestStart = i;
                }

                i = j;
            }

            if (bestLen < 4) return false;

            low = dzs[bestStart];
            high = dzs[bestStart + bestLen - 1];
            if (high - low < 0.4f || high - low > 2.5f) return false;

            for (var k = bestStart; k < bestStart + bestLen; k++) face += faces[k];
            face /= bestLen;

            // Its face, which way it looks, off one more ray at its middle.
            normal = Facing(o, toward, (low + high) * 0.5f, ignore);
            return true;
        }

        /// <summary>The board's own facing, flat, off a ray at its middle.</summary>
        private static Vector3 Facing(Vector3 o, Vector3 toward, float dz, int ignore)
        {
            try
            {
                var a = o + toward * 3.5f + Vector3.WorldUp * dz;
                var b = o - toward * 1.5f + Vector3.WorldUp * dz;

                var ray = Function.Call<int>(Hash.START_EXPENSIVE_SYNCHRONOUS_SHAPE_TEST_LOS_PROBE,
                                             a.X, a.Y, a.Z, b.X, b.Y, b.Z, 1 | 16, ignore, 7);

                var hit = new OutputArgument();
                var where = new OutputArgument();
                var normal = new OutputArgument();
                var thing = new OutputArgument();

                Function.Call<int>(Hash.GET_SHAPE_TEST_RESULT, ray, hit, where, normal, thing);
                if (!hit.GetResult<bool>()) return Vector3.Zero;

                var n = Flat3D(normal.GetResult<Vector3>());
                if (n.Length() < 0.5f) return Vector3.Zero;
                n.Normalize();

                // Out of the board towards the court, whichever way the model wound it.
                return Vector3.Dot(n, toward) < 0f ? -n : n;
            }
            catch
            {
                return Vector3.Zero;
            }
        }

        /// <summary>
        /// One ray at the board: how far out from the hoop's middle it hit, if it hit something
        /// upright within the hoop's own depth. A floor or a roof is not a backboard.
        /// </summary>
        private static bool Board(Vector3 o, Vector3 toward, Vector3 side, float s, float dz, int ignore, out float along, out float upright)
        {
            along = 0f;
            upright = 0f;

            try
            {
                var a = o + toward * 3.5f + side * s + Vector3.WorldUp * dz;
                var b = o - toward * 1.5f + side * s + Vector3.WorldUp * dz;

                var ray = Function.Call<int>(Hash.START_EXPENSIVE_SYNCHRONOUS_SHAPE_TEST_LOS_PROBE,
                                             a.X, a.Y, a.Z, b.X, b.Y, b.Z, 1 | 16, ignore, 7);

                var hit = new OutputArgument();
                var where = new OutputArgument();
                var normal = new OutputArgument();
                var thing = new OutputArgument();

                Function.Call<int>(Hash.GET_SHAPE_TEST_RESULT, ray, hit, where, normal, thing);
                if (!hit.GetResult<bool>()) return false;

                along = Vector3.Dot(where.GetResult<Vector3>() - o, toward);
                upright = Math.Abs(normal.GetResult<Vector3>().Z);

                // The hoop's own depth, and a face that stands up: anything behind it, well out on
                // the court, or lying flat is not its board.
                return along > -0.6f && along < 2.2f && upright < 0.5f;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// The throw that lands on a point: the speed at Arc degrees that carries the ball from one
        /// point to the other, and a steeper arc if that one cannot get there. Street Golf's physics
        /// flies it without drag, so the sum is the flight.
        /// </summary>
        private static bool Solve(Vector3 from, Vector3 to, out Vector3 velocity, out float speed)
        {
            velocity = Vector3.Zero;
            speed = 0f;

            var d = to - from;
            var flat = Flat3D(d);
            var dist = flat.Length();
            if (dist < 0.05f) return false;
            flat *= 1f / dist;

            foreach (var arc in new[] { Arc, 62f, 72f })
            {
                var th = arc * (float)Math.PI / 180f;
                var c = (float)Math.Cos(th);
                var denom = 2f * c * c * (dist * (float)Math.Tan(th) - d.Z);
                if (denom <= 0.01f) continue;

                speed = (float)Math.Sqrt(Gravity * dist * dist / denom);
                velocity = flat * (speed * c) + Vector3.WorldUp * (speed * (float)Math.Sin(th));
                return true;
            }

            return false;
        }

        // ---- the buttons ----------------------------------------------------------------------

        /// <summary>
        /// While he has the ball: no punches, no aiming a gun, no weapon wheel, no jump of his own
        /// and no getting into a car -- and while the meter runs, his feet are planted.
        /// </summary>
        private static void HoldTheButtons(bool planted)
        {
            try
            {
                Game.DisableControlThisFrame(Control.Attack);
                Game.DisableControlThisFrame(Control.Attack2);
                Game.DisableControlThisFrame(Control.Aim);
                Game.DisableControlThisFrame(Control.MeleeAttackLight);
                Game.DisableControlThisFrame(Control.MeleeAttackHeavy);
                Game.DisableControlThisFrame(Control.MeleeAttackAlternate);
                Game.DisableControlThisFrame(Control.MeleeBlock);
                Game.DisableControlThisFrame(Control.SelectWeapon);
                Game.DisableControlThisFrame(Control.WeaponWheelNext);
                Game.DisableControlThisFrame(Control.WeaponWheelPrev);
                Game.DisableControlThisFrame(Control.Jump);
                Game.DisableControlThisFrame(Control.Cover);
                Game.DisableControlThisFrame(Control.Enter);

                if (!planted) return;

                Game.DisableControlThisFrame(Control.MoveLeftRight);
                Game.DisableControlThisFrame(Control.MoveUpDown);
                Game.DisableControlThisFrame(Control.Sprint);
            }
            catch { }
        }

        private static bool Down(Control c)
        {
            try { return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)c); }
            catch { return false; }
        }

        private static bool JustPressed(Control c)
        {
            try { return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)c); }
            catch { return false; }
        }

        // ---- the screen ----------------------------------------------------------------------

        private void Draw(Ped me, int now)
        {
            var dist = _rim == Vector3.Zero ? 0f : Flat(_rim - me.Position);
            var three = dist > ThreeFrom;

            if (_banner.Length > 0 && now < _bannerUntil)
            {
                UI.Draw.Rect(0.5f, 0.235f, 1f, 0.13f, Color.FromArgb(120, 0, 0, 0));
                UI.Draw.Text(_banner, 0.5f, 0.17f, 1.1f, _bannerInk, UI.Draw.FontPricedown, true, true, true);
                UI.Draw.Text(_bannerSub, 0.5f, 0.262f, 0.45f, Color.White, UI.Draw.FontBody, true, true, false);
            }
            else if (_mode == Mode.Holding)
            {
                Help.ShowThisFrame(dist.ToString("0.0") + " m -- " + (three ? "a three" : "a two") +
                                   ". Look at the hoop, hold ~INPUT_ATTACK~ and let go in the green.");
            }

            Den.Bars.Draw("SCORE", _score.ToString(), Gold,
                          "STREAK", _streak.ToString(), _streak > 1 ? Green : Color.White,
                          "MADE", _made + " / " + _shots, Color.White);

            if (_mode == Mode.Holding)
            {
                Den.Buttons.Show(Control.Attack, "Hold to shoot",
                                 Control.PhoneCancel, "Put the ball down");
            }

            if (_mode == Mode.Charging) Meter();
        }

        /// <summary>
        /// Street Golf's meter: sixteen cells, amber on the way up, green inside the sweet spot,
        /// red past it, and a white tick where the power is.
        /// </summary>
        private void Meter()
        {
            const int cells = 16;
            const float left = 0.38f;
            const float width = 0.24f;
            const float y = 0.84f;
            const float h = 0.012f;
            const float gap = 0.002f;

            var cw = (width - gap * (cells - 1)) / cells;
            var lo = _sweet < 0f ? 2f : _sweet - _half;
            var hi = _sweet < 0f ? 2f : _sweet + _half;
            var sweet = _power >= lo && _power <= hi;

            UI.Draw.Rect(left + width * 0.5f, y, width + 0.012f, h + 0.012f, Color.FromArgb(150, 0, 0, 0));

            for (var c = 0; c < cells; c++)
            {
                var f0 = c / (float)cells;
                var f1 = (c + 1) / (float)cells;
                var x = left + c * (cw + gap) + cw * 0.5f;
                var zone = f1 > lo && f0 < hi;

                var fill = _power >= f1 ? 1f : _power > f0 ? (_power - f0) / (f1 - f0) : 0f;
                Color ink;

                if (fill > 0f) ink = sweet ? Green : f0 >= hi ? Red : Gold;
                else ink = zone ? Color.FromArgb(150, 114, 204, 114) : Track;

                UI.Draw.Rect(x, y, cw, h, ink);
            }

            UI.Draw.Rect(left + width * Clamp(_power, 0f, 1f), y, 0.002f, h + 0.012f, Color.White);
        }

        private void Banner(string big, string small, Color ink)
        {
            _banner = big;
            _bannerSub = small;
            _bannerInk = ink;
            _bannerUntil = Game.GameTime + 1800;
        }

        private static void Sound(string name, string set)
        {
            try { Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, name, set, true); }
            catch { }
        }

        // ---- sums -----------------------------------------------------------------------------

        private static Vector3 Flat3D(Vector3 v) => new Vector3(v.X, v.Y, 0f);

        private static float Flat(Vector3 v) => (float)Math.Sqrt(v.X * v.X + v.Y * v.Y);

        /// <summary>The way a heading faces, flat.</summary>
        private static Vector3 Dir(float heading)
        {
            var r = heading * (float)Math.PI / 180f;
            return new Vector3(-(float)Math.Sin(r), (float)Math.Cos(r), 0f);
        }

        /// <summary>The heading from one point to another.</summary>
        private static float Toward(Vector3 from, Vector3 to)
        {
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            return Deg360((float)(Math.Atan2(-dx, dy) * 180.0 / Math.PI));
        }

        private static float Deg360(float d)
        {
            d %= 360f;
            return d < 0f ? d + 360f : d;
        }

        private static float AngleDiff(float a, float b)
        {
            var d = (a - b) % 360f;
            if (d > 180f) d -= 360f;
            if (d < -180f) d += 360f;
            return d;
        }

        private static float Clamp(float x, float lo, float hi) => x < lo ? lo : x > hi ? hi : x;
    }
}
