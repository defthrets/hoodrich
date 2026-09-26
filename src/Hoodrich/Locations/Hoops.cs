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

        /// <summary>
        /// The court's two ends: Michael's HUD readings at the top of each key, and the way he faced
        /// the hoop from there. Either one starts a game; in the game, turn to the other hoop and it
        /// is the one you shoot at.
        /// </summary>
        private static readonly Vector3[] Spots =
        {
            new Vector3(-201.428f, -1508.783f, 31.631f),
            new Vector3(-209.672f, -1518.733f, 31.616f)
        };

        private static readonly float[] SpotFacing = { 316.079f, 136.238f };

        private const string BallName = "prop_bskball_01";

        private const string HoldDict = "anim@sports@ballgame@handball@";
        private const string HoldClip = "ball_idle";
        /// <summary>
        /// One way to shoot: the clip, on the upper body; whether the legs jump under it; how far
        /// through it the ball goes; and how high over him the ball is when it does.
        /// </summary>
        private sealed class Style
        {
            public string Name;
            public string Dict;
            public string Clip;
            public bool Jump;
            public float Release;
            public float Up;
        }

        /// <summary>
        /// THE WAYS TO SHOOT, picked on the court with D-pad left and remembered. The game has no
        /// basketball of its own and no clip of anybody shooting one, and the first build's jump
        /// with the grenade throw on top looked, in Michael's word, jank. So there are four of the
        /// nearest things the game has, and he keeps the one that looks right: both hands pushing
        /// the ball up from the chest, with a jump or without -- the up half of Raise the Roof --
        /// the high throw with a jump, and the medium throw without one.
        /// </summary>
        private static readonly Style[] Styles =
        {
            new Style { Name = "Set shot", Dict = "anim@mp_player_intupperraise_the_roof", Clip = "enter", Jump = false, Release = 0.55f, Up = 1.2f },
            new Style { Name = "Two-hand jumper", Dict = "anim@mp_player_intupperraise_the_roof", Clip = "enter", Jump = true, Release = 0.5f, Up = 1.5f },
            new Style { Name = "Jump shot", Dict = "weapons@projectile@", Clip = "throw_h_fb_stand", Jump = true, Release = 0.36f, Up = 1.5f },
            new Style { Name = "Push shot", Dict = "weapons@projectile@", Clip = "throw_m_fb_stand", Jump = false, Release = 0.34f, Up = 1.2f }
        };

        /// <summary>Every clip set a game asks for, and lets go of at the end.</summary>
        private static readonly string[] Dicts = { HoldDict, "anim@mp_player_intupperraise_the_roof", "weapons@projectile@" };

        private int _style;
        private int _shotStyle;
        private bool _styleRead;

        private Style Shot => Styles[_style];

        /// <summary>How near the spot to be offered a game, and how far the ring is drawn from.</summary>
        private const float OfferReach = 1.3f;
        private const float RingReach = 30f;

        /// <summary>How far down the court a backboard is looked for, and how far from the rim before the game is put away.</summary>
        private const float HoopFind = 16f;
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

        /// <summary>The ball goes when the clip is its style's way through, or this long after it began.</summary>
        private const int ReleaseMs = 520;

        private const float Gravity = 9.8f;

        private Mode _mode = Mode.Off;
        private int _at;
        private Prop _ball;

        /// <summary>The rim being shot at, and every rim read so far, by where its board is. See RimAhead.</summary>
        private Vector3 _rim;
        private bool _haveRim;
        private readonly Dictionary<string, Vector3> _rims = new Dictionary<string, Vector3>();

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
            if (!me.IsAlive || me.IsInVehicle() || me.IsRagdoll || !_haveRim || me.Position.DistanceTo(_rim) > CourtReach)
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
            var at = -1;

            for (var i = 0; i < Spots.Length; i++)
            {
                var d = me.Position.DistanceTo(Spots[i]);
                if (d > RingReach) continue;

                Ring(Spots[i]);
                if (d <= OfferReach) at = i;
            }

            if (at < 0 || me.IsInVehicle() || Mind.Busy || InputGuard.Busy) return;

            Help.ShowThisFrame("Press ~INPUT_CONTEXT~ to shoot hoops.");

            if (Game.IsControlJustPressed(Control.Context)) Start(me, at);
        }

        private static void Ring(Vector3 spot)
        {
            try
            {
                Function.Call(Hash.DRAW_MARKER, 1, spot.X, spot.Y, spot.Z - 1.0f,
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

        private void Start(Ped me, int spot)
        {
            InputGuard.Swallow();

            if (!Aim(me, SpotFacing[spot], Spots[spot]))
            {
                Notify.Problem("Can't find the hoop from here -- look at it and press again.");
                return;
            }

            // Asked for now and let go at the end: the only things this ever loads.
            try
            {
                foreach (var d in Dicts) Function.Call(Hash.REQUEST_ANIM_DICT, d);
                Models.Ready(new Model(BallName));

                if (!_styleRead)
                {
                    _styleRead = true;
                    _style = ReadStyle();
                }
            }
            catch { /* asked again while it waits */ }

            _score = _streak = _shots = _made = 0;
            _banner = "";
            _mode = Mode.Starting;
            _at = Game.GameTime;

            Log.Info("Hoops: on the court at ring " + (spot + 1) + ", the rim at " + _rim + ".");
        }

        private void Starting(Ped me, int now)
        {
            var ready = false;

            try
            {
                ready = Models.Ready(new Model(BallName));
                foreach (var d in Dicts) ready = ready && Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, d);
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
                foreach (var d in Dicts) Function.Call(Hash.REMOVE_ANIM_DICT, d);
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
            _haveRim = false;
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

            // Another way to shoot, kept for next time.
            if (JustPressed(Control.PhoneLeft))
            {
                _style = (_style + 1) % Styles.Length;
                SaveStyle();
                Sound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                Banner(Shot.Name.ToUpperInvariant(), "D-pad left for another.", Color.White);
                _bannerUntil = Game.GameTime + 1100;
            }

            if (!Down(Control.Attack) || now - _at < 250) return;

            // The other hoop, if he has turned round to it; the one he had, if nothing else is there.
            if (Math.Abs(AngleDiff(Deg360(GameplayCamera.Rotation.Z), Toward(GameplayCamera.Position, _rim))) > 60f) Aim(me, -1f, Vector3.Zero);

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
            var from = me.Position + Vector3.WorldUp * Shot.Up;
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

            _shotStyle = _style;
            var s = Styles[_shotStyle];

            try
            {
                // The legs jump, if this way does; the arms shoot, on the upper body over whatever the legs do.
                if (s.Jump) Function.Call(Hash.TASK_JUMP, me.Handle, true, false, false);
                Function.Call(Hash.TASK_PLAY_ANIM, me.Handle, s.Dict, s.Clip, 8f, -8f, -1, 48, 0f, false, false, false);
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
                var s = Styles[_shotStyle];
                if (Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM, me.Handle, s.Dict, s.Clip, 3))
                    phase = Function.Call<float>(Hash.GET_ENTITY_ANIM_CURRENT_TIME, me.Handle, s.Dict, s.Clip);
            }
            catch { }

            if (phase < Styles[_shotStyle].Release && now - _at < ReleaseMs) return;

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

        // ---- the hoops and their rims -----------------------------------------------------------

        /// <summary>
        /// The hoop he is looking at -- or, when the camera is off it, the one the ring faces -- and
        /// its rim. False when nothing like a backboard is down the court from him.
        /// </summary>
        private bool Aim(Ped me, float facing, Vector3 ring)
        {
            Vector3 rim;

            if (!RimAhead(me, Deg360(GameplayCamera.Rotation.Z), out rim) &&
                !(facing >= 0f && RimAhead(me, facing, out rim)))
            {
                if (facing < 0f) return false;

                // No backboard answered from the ring: the rim where a regulation court has it
                // from the free-throw line, 4.2 m down the court and 3.05 m up.
                float ground;
                var floor = Ground.Probe(ring + Vector3.WorldUp * 0.5f, out ground) ? ground : ring.Z - 1f;
                rim = new Vector3(ring.X, ring.Y, floor + 3.05f) + Dir(facing) * 4.2f;

                Log.Warn("Hoops: no backboard answered from the ring at " + ring + "; the rim is put where a " +
                         "regulation court has it, at " + rim + ".");
            }

            _rim = rim;
            _haveRim = true;
            return true;
        }

        /// <summary>
        /// THE HOOPS ARE THE MAP'S. The court's poles and backboards are built into the map rather
        /// than props the game hands a script -- no prop_basketball_net stands on this court, which
        /// is why the first build said there was no hoop -- so the hoop is found by looking for it.
        /// Rays straight down the court from him at the heights a backboard hangs: the NEAREST flat
        /// upright face turned back at him, at one depth over a run of heights, is the board. The
        /// building behind the far hoop is further off than its board and is passed over.
        /// </summary>
        private bool RimAhead(Ped me, float heading, out Vector3 rim)
        {
            rim = Vector3.Zero;

            try
            {
                var look = Dir(heading);
                var at = me.Position;

                float ground;
                var floor = Ground.Probe(at + Vector3.WorldUp * 0.5f, out ground) ? ground : at.Z - 1f;

                var hs = new List<float>();
                var ds = new List<float>();
                var ns = new List<Vector3>();

                for (var h = floor + 2.2f; h <= floor + 5.0f; h += 0.1f)
                {
                    var from = new Vector3(at.X, at.Y, h);
                    Vector3 where, normal;

                    if (!Ray(from, from + look * HoopFind, me.Handle, out where, out normal)) continue;

                    var n = Flat3D(normal);
                    if (n.Length() < 0.85f) continue;
                    n.Normalize();

                    if (Vector3.Dot(n, -look) < 0.5f) continue;

                    hs.Add(h);
                    ds.Add(Vector3.Dot(where - from, look));
                    ns.Add(n);
                }

                // The nearest group at one depth with a board's worth of height in it.
                var order = new List<int>();
                for (var i = 0; i < ds.Count; i++) order.Add(i);
                order.Sort((a, b) => ds[a].CompareTo(ds[b]));

                List<int> board = null;

                foreach (var first in order)
                {
                    var group = new List<int>();
                    float lo = float.MaxValue, hi = float.MinValue;

                    foreach (var k in order)
                    {
                        if (ds[k] < ds[first] || ds[k] > ds[first] + 0.25f) continue;
                        group.Add(k);
                        if (hs[k] < lo) lo = hs[k];
                        if (hs[k] > hi) hi = hs[k];
                    }

                    if (group.Count >= 4 && hi - lo >= 0.3f)
                    {
                        board = group;
                        break;
                    }
                }

                if (board == null) return false;

                float depth = 0f, mid = 0f;
                var facing = Vector3.Zero;

                foreach (var k in board)
                {
                    depth += ds[k];
                    mid += hs[k];
                    facing += ns[k];
                }

                depth /= board.Count;
                mid = mid / board.Count - floor;
                facing.Normalize();

                // Where the board is, on the floor under its face: every board is read once.
                var o = new Vector3(at.X, at.Y, floor) + look * depth;
                var key = Math.Round(o.X) + "," + Math.Round(o.Y);

                if (_rims.TryGetValue(key, out rim)) return true;

                if (!RimAt(o, facing, mid, me.Handle, out rim)) return false;

                _rims[key] = rim;
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Hoops: could not look for the hoop: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// The rim off its board: across the board at its middle for its edges and so its centre,
        /// then up it either side of the centre for its bottom edge and its top. The rim is 15 cm
        /// above the bottom edge and out from the face by the gap and its own radius, as a
        /// regulation hoop is built.
        /// </summary>
        private static bool RimAt(Vector3 o, Vector3 toward, float mid, int ignore, out Vector3 rim)
        {
            rim = Vector3.Zero;

            var side = new Vector3(toward.Y, -toward.X, 0f);
            float left = float.MaxValue, right = float.MinValue;

            for (var s = -1.6f; s <= 1.6f; s += 0.05f)
            {
                float along;
                if (!Board(o, toward, side, s, mid, ignore, out along)) continue;

                if (s < left) left = s;
                if (s > right) right = s;
            }

            if (left > right) return false;

            var centre = (left + right) * 0.5f;
            var c = o + side * centre;

            float low, high;
            if (!Band(c, toward, side, ignore, out low, out high)) return false;

            rim = c + toward * 0.38f + Vector3.WorldUp * (low + 0.15f);

            Log.Info("Hoops: a backboard " + (right - left).ToString("0.00") + " m wide and " + (high - low).ToString("0.00") +
                     " m tall, its bottom " + low.ToString("0.00") + " m up; the rim is at " + rim + ".");
            return true;
        }

        /// <summary>
        /// The board's bottom and top: rays two either side of its centre at a slice of heights,
        /// each pair landing on its face. The longest unbroken run of those is the board.
        /// </summary>
        private static bool Band(Vector3 c, Vector3 toward, Vector3 side, int ignore, out float low, out float high)
        {
            low = high = 0f;

            var rows = new List<float>();

            for (var dz = 1.8f; dz <= 6.0f; dz += 0.08f)
            {
                float a, b;
                if (!Board(c, toward, side, -0.4f, dz, ignore, out a)) continue;
                if (!Board(c, toward, side, 0.4f, dz, ignore, out b)) continue;

                rows.Add(dz);
            }

            int bestStart = -1, bestLen = 0;

            for (var i = 0; i < rows.Count; )
            {
                var j = i + 1;
                while (j < rows.Count && rows[j] - rows[j - 1] <= 0.12f) j++;

                if (j - i > bestLen)
                {
                    bestLen = j - i;
                    bestStart = i;
                }

                i = j;
            }

            if (bestLen < 4) return false;

            low = rows[bestStart];
            high = rows[bestStart + bestLen - 1];

            return high - low >= 0.4f && high - low <= 2.5f;
        }

        /// <summary>
        /// One ray at the board, from the court: whether it hit something upright ON the board's
        /// face -- not the wall behind the far hoop, not the court, not the sky.
        /// </summary>
        private static bool Board(Vector3 o, Vector3 toward, Vector3 side, float s, float dz, int ignore, out float along)
        {
            along = 0f;

            var a = o + toward * 3.5f + side * s + Vector3.WorldUp * dz;
            var b = o - toward * 1.5f + side * s + Vector3.WorldUp * dz;

            Vector3 where, normal;
            if (!Ray(a, b, ignore, out where, out normal)) return false;

            along = Vector3.Dot(where - o, toward);
            return Math.Abs(along) <= 0.2f && Math.Abs(normal.Z) < 0.5f;
        }

        /// <summary>A ray into the map and anything standing, and where it hit and what way that faces.</summary>
        private static bool Ray(Vector3 a, Vector3 b, int ignore, out Vector3 where, out Vector3 normal)
        {
            where = Vector3.Zero;
            normal = Vector3.Zero;

            try
            {
                var ray = Function.Call<int>(Hash.START_EXPENSIVE_SYNCHRONOUS_SHAPE_TEST_LOS_PROBE,
                                             a.X, a.Y, a.Z, b.X, b.Y, b.Z, 1 | 16, ignore, 7);

                var hit = new OutputArgument();
                var at = new OutputArgument();
                var n = new OutputArgument();
                var thing = new OutputArgument();

                Function.Call<int>(Hash.GET_SHAPE_TEST_RESULT, ray, hit, at, n, thing);
                if (!hit.GetResult<bool>()) return false;

                where = at.GetResult<Vector3>();
                normal = n.GetResult<Vector3>();
                return true;
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
                                 Control.PhoneLeft, "Shot: " + Shot.Name,
                                 Control.PhoneCancel, "Put the ball down");
            }

            if (_mode == Mode.Holding || _mode == Mode.Charging) Line(me);
            if (_mode == Mode.Charging) Meter();
        }

        /// <summary>
        /// STREET GOLF'S AIM LINE: the flight worked out the way the shot will be, and drawn as a
        /// thin ribbon turned to the camera. Holding the ball, it is the shot in the green along
        /// where he is looking; with the meter running, it is the shot as the meter stands now.
        /// It ends where it comes down through the rim's height, and it is green when that is
        /// through the rim. Michael asked for the golf line on the basketball on 2026-09-26.
        /// </summary>
        private void Line(Ped me)
        {
            if (!_haveRim) return;

            try
            {
                var eye = GameplayCamera.Position;
                var aim = Deg360(GameplayCamera.Rotation.Z);
                var from = me.Position + Vector3.WorldUp * Shot.Up + Dir(aim) * 0.25f;
                var onLine = Math.Abs(AngleDiff(aim, Toward(eye, _rim))) <= OnLine;
                var charging = _mode == Mode.Charging;
                var good = charging && _sweet >= 0f && Math.Abs(_power - _sweet) <= _half;

                Vector3 v;
                float speed;

                if (!((!charging || good) && onLine && Solve(from, _rim, out v, out speed)))
                {
                    var p = charging ? _power : (_sweet >= 0f ? _sweet : 0.5f);
                    var th = Arc * (float)Math.PI / 180f;
                    speed = SpeedLow + (SpeedHigh - SpeedLow) * p;
                    v = Dir(onLine ? Toward(from, _rim) : aim) * (speed * (float)Math.Cos(th)) +
                        Vector3.WorldUp * (speed * (float)Math.Sin(th));
                }

                var floor = me.Position.Z - 1f;
                var pos = from;
                var end = from;
                var through = false;
                var pts = new List<Vector3> { from };

                for (var i = 0; i < 90; i++)
                {
                    var next = pos + v * LineStep;
                    v.Z -= Gravity * LineStep;

                    // Coming down through the rim's height: the end of the line, in or out.
                    if (pos.Z > _rim.Z && next.Z <= _rim.Z && v.Z < 0f)
                    {
                        var t = (pos.Z - _rim.Z) / Math.Max(0.0001f, pos.Z - next.Z);
                        end = pos + (next - pos) * t;
                        through = Flat(end - _rim) <= MakeRadius;
                        pts.Add(end);
                        break;
                    }

                    if (next.Z < floor) break;

                    pts.Add(next);
                    pos = next;
                    end = next;
                }

                var ink = through ? Color.FromArgb(150, 114, 204, 114)
                        : charging ? Color.FromArgb(120, 240, 200, 80)
                        : Color.FromArgb(80, 255, 255, 255);

                for (var i = 0; i + 1 < pts.Count; i++) Segment(pts[i], pts[i + 1], 0.025f, ink, eye);

                if (through)
                {
                    Function.Call(Hash.DRAW_MARKER, 25, _rim.X, _rim.Y, _rim.Z + 0.02f,
                                  0f, 0f, 0f, 0f, 0f, 0f, 0.5f, 0.5f, 0.5f,
                                  114, 204, 114, 140, false, false, 2, false, 0, 0, false);
                }
            }
            catch
            {
                // No line this frame.
            }
        }

        /// <summary>How far apart in time the line's points are worked out.</summary>
        private const float LineStep = 0.03f;

        /// <summary>
        /// One piece of the line: a thin quad turned to face the camera, the way Street Golf draws
        /// its own, and a little wider further off so it does not thin to nothing and shimmer.
        /// </summary>
        private static void Segment(Vector3 a, Vector3 b, float width, Color ink, Vector3 eye)
        {
            var d = b - a;
            var dl = d.Length();
            if (dl < 0.01f) return;
            d *= 1f / dl;

            var to = eye - a;
            var tl = to.Length();
            if (tl < 0.05f) return;
            to *= 1f / tl;

            var side = Vector3.Cross(d, to);
            var sl = side.Length();
            if (sl < 0.001f) return;

            var grow = Math.Min(3f, 1f + tl / 40f);
            side *= width * grow / sl;

            Poly(a - side, a + side, b + side, ink);
            Poly(a - side, b + side, b - side, ink);
        }

        private static void Poly(Vector3 p, Vector3 q, Vector3 r, Color c)
        {
            Function.Call(Hash.DRAW_POLY, p.X, p.Y, p.Z, q.X, q.Y, q.Z, r.X, r.Y, r.Z, (int)c.R, (int)c.G, (int)c.B, (int)c.A);
        }

        // ---- the remembered shot ---------------------------------------------------------------

        private static string StylePath => System.IO.Path.Combine(Paths.Writable, "hoops.txt");

        private static int ReadStyle()
        {
            try
            {
                if (!System.IO.File.Exists(StylePath)) return 0;

                foreach (var raw in System.IO.File.ReadAllLines(StylePath))
                {
                    var line = raw.Trim();
                    if (!line.StartsWith("shot=", StringComparison.OrdinalIgnoreCase)) continue;

                    var name = line.Substring(5).Trim();

                    for (var i = 0; i < Styles.Length; i++)
                    {
                        if (string.Equals(Styles[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
                    }
                }
            }
            catch
            {
                // The first one, then.
            }

            return 0;
        }

        private void SaveStyle()
        {
            try
            {
                System.IO.File.WriteAllText(StylePath, "# The way he shoots on the court. Written by the mod." + Environment.NewLine +
                                                       "shot=" + Shot.Name + Environment.NewLine);
                Log.Info("Hoops: shooting the " + Shot.Name + " now.");
            }
            catch
            {
                // It is only remembered for this game.
            }
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
