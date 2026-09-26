using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using GTA;
using GTA.Math;
using GTA.Native;
using Control = GTA.Control;

namespace Hoodrich.Court
{
    /// <summary>
    /// SHOOTING HOOPS on the Chamberlain Hills court, on the court's own hoops. Michael asked for
    /// it on 2026-09-26: a basketball, a jump and a throw, and the aim and the shot the way his
    /// Street Golf does them.
    ///
    /// THE THROW IS FREE. The aim is where the camera looks. Hold the button and the meter runs
    /// up and back down, and the line out of his hand stretches with it -- from a lob at his feet
    /// to a heave forty metres down the street. The line fades out before it comes down, so it
    /// gives the idea and never the spot. Nothing works a basket out for him and nothing steers
    /// the ball at the hoop: Michael asked for it that way, "if you wanted to throw it really far
    /// you could, dont try and make it swosh".
    ///
    /// A BASKET IS THE BALL DOWN THROUGH THE HOOP'S RING. Each hoop has a flat ring where its rim
    /// is, and the ball's middle has to come down through it. The hoops are built into the map,
    /// so the game will not say where they are, and reading the backboards with rays found
    /// neither of this court's -- the log of 2026-09-26 -- so the balls went in through a guess.
    /// The rings are placed by hand on the court instead (the developer tools, D-pad right with
    /// the ball) and written into RingAt for everybody else.
    ///
    /// THE SHOT is both hands pushing the ball up from the chest -- the up half of Raise the Roof,
    /// played quick and cut after the one push, on the arms only: no jump. The game has no
    /// basketball of its own and no clip of anybody shooting one; that is the nearest thing to it.
    /// Held, he carries the ball in the ball-game idle that goes with it.
    ///
    /// LIGHT ON PURPOSE. Michael's game ran out of memory on 2026-09-26, and this adds one ball
    /// and two small clip sets, asked for when a game starts and let go when it ends. No people,
    /// no vehicles, no cameras, nothing streamed. The ball is the same prop all game: it flies,
    /// and it comes back to his hands.
    /// </summary>
    internal sealed class Shootaround
    {
        private enum Mode { Off, Starting, Holding, Charging, Shooting, Flying, Result }

        /// <summary>
        /// WHERE A GAME STARTS: one ring on the court. There were two, one at the top of each key,
        /// and Michael asked for the one on 2026-09-26 -- set where he stands with the developer
        /// tools (D-pad down, holding the ball) and read from the ini over this, which is the
        /// middle of the court until he has.
        /// </summary>
        private static readonly Vector3 StartAt = new Vector3(-205.550f, -1513.758f, 31.624f);

        private Vector3 _start;
        private bool _startRead;

        private const string BallName = "prop_bskball_01";

        private const string HoldDict = "anim@sports@ballgame@handball@";
        private const string HoldClip = "ball_idle";

        /// <summary>
        /// THE SHOT: both hands pushing the ball up from the chest -- the up half of Raise the Roof,
        /// played 1.6 times as fast, the ball gone 30% of the way through it and the clip stopped at
        /// 46%, after the one push. Michael kept it over two grenade throws and a jump, and the
        /// picker that tried them went for the release.
        /// </summary>
        private const string ShotDict = "anim@mp_player_intupperraise_the_roof";
        private const string ShotAnim = "enter";
        private const float ShotRelease = 0.30f;
        private const float ShotSpeed = 1.6f;
        private const float ShotCut = 0.46f;

        /// <summary>How high over him the ball goes, for a line drawn before the ball is in his hands.</summary>
        private const float ShotUp = 1.2f;

        /// <summary>Every clip set a game asks for, and lets go of at the end.</summary>
        private static readonly string[] Dicts = { HoldDict, ShotDict };

        /// <summary>How near the start to be offered a game, and how far its ring is drawn from.</summary>
        private const float OfferReach = 1.3f;
        private const float RingReach = 30f;

        /// <summary>How far from every hoop before the game is put away.</summary>
        private const float CourtReach = 26f;

        /// <summary>
        /// THE RINGS, one for each hoop on the court: the middle of its rim, and how far from there
        /// to the edge of the ring the ball has to come down through. Every one counts, wherever
        /// the game was started. Placed by hand on the court (see RingPlacing); a placing in the
        /// ini is read over these, and a hoop added on the court (D-pad left) is Ring3 and on.
        ///
        /// Hoop 2's is Michael's own, placed on 2026-09-26: 51 cm across, 3.09 m up, and 5.2 m
        /// from the spot -- a metre further out than a regulation court puts it, which is where the
        /// first guess had it. Hoop 1's is hoop 2's turned end for end about the middle of the court
        /// until he places it too.
        /// </summary>
        private static readonly Vector3[] RingAt =
        {
            new Vector3(-197.998f, -1504.840f, 33.707f),
            new Vector3(-213.102f, -1522.676f, 33.692f)
        };

        private static readonly float[] RingWide = { 0.254f, 0.254f };

        /// <summary>
        /// PH_L_Hand: the ball rides in his LEFT hand. The ball-game idle holds that hand up in
        /// front of him for it, and the right one hangs; Michael moved it there on 2026-09-26.
        /// </summary>
        private const int LeftHand = 60309;

        /// <summary>
        /// Where in his hand: centimetres along the hand bone's own three axes, then degrees about
        /// them. Michael placed it himself on the court on 2026-09-26 and asked for it kept, and
        /// for the placer to go.
        /// </summary>
        private static readonly float[] Grip = { -2.5f, 0f, 4.5f, 10f, 0f, 0f };

        /// <summary>The developer tools are on: the ring placer and the other ways to shoot are offered. Read as a game starts.</summary>
        private bool _dev;

        /// <summary>Street Golf's meter, slowed for a hoop: up and back down over this long while the button is held.</summary>
        private const float ChargeTime = 2.6f;

        /// <summary>
        /// THE METER AGAINST HOW FAR: the throw comes down through the hoop's height this far out
        /// from the ball -- a metre on an empty meter, ten at MidAt of it, forty at the top. Even and
        /// slow across a court, where a rim's width is all there is to hit, and quicker past it for
        /// the heave down the street. It is the distance that is shared out along the meter, not
        /// Street Golf's pace: pace for power put the whole court in the first sliver of it.
        /// </summary>
        private const float ReachNear = 1f;
        private const float ReachMid = 10f;
        private const float MidAt = 0.72f;
        private const float ReachFar = 40f;

        /// <summary>Street Golf's fine aim: while the meter runs, the left stick turns the line this fast, and this far either way.</summary>
        private const float FineSpeed = 18f;
        private const float FineMost = 25f;

        /// <summary>
        /// The arc a shot leaves the hand on -- steeper close in, so the ball is always coming DOWN
        /// when it gets to the ring's height, never going up through it. See Launch.
        /// </summary>
        private const float Arc = 52f;

        /// <summary>How fast the ring placer slides a ring and grows it, a second; X for four times that.</summary>
        private const float RingSlide = 0.2f;
        private const float RingGrow = 0.08f;

        /// <summary>The three-point line, from the middle of the rim over the floor.</summary>
        private const float ThreeFrom = 6.75f;

        /// <summary>The ball goes when the clip is its style's way through, or this long after it began.</summary>
        private const int ReleaseMs = 520;

        private const float Gravity = 9.8f;

        private Mode _mode = Mode.Off;
        private int _at;
        private Prop _ball;

        /// <summary>Every hoop's ring as this game has them, and the hoop he is shooting at: the one he looks at.</summary>
        private readonly List<Vector3> _ring = new List<Vector3>();
        private readonly List<float> _ringSize = new List<float>();

        /// <summary>The most rings a court is read for: Ring1 to Ring8.</summary>
        private const int MostRings = 8;
        private int _hoop;

        private Vector3 Rim => _ring[_hoop];

        private float _charge;
        private float _power;

        /// <summary>Where the meter stood for the last shot: the line he lines the next one up with.</summary>
        private float _lastPower = 0.3f;
        private int _lastTick;

        private float _aim;
        private float _fine;

        /// <summary>The throw worked out as he let go: where it left from, and how.</summary>
        private Vector3 _launchFrom;
        private Vector3 _launch;

        /// <summary>Where the line was going when he let go -- down through the hoop's height. See Release.</summary>
        private Vector3 _launchTo;

        /// <summary>The shot's clip is still on his arms, to be sped up and cut short. See ShotClip.</summary>
        private bool _clipLive;

        /// <summary>Where he stood for the shot: a three is from past the line.</summary>
        private Vector3 _shotAt;

        private bool _released;
        private bool _scored;
        private bool _touched;
        private bool _crossed;
        private Vector3 _cross;
        private Vector3 _prev;

        /// <summary>The ring placer: whose ring, how it was before, and the floor it is measured from.</summary>
        private bool _ringPlacing;
        private int _ringWho;
        private Vector3 _ringWas;
        private float _ringWasSize;
        private float _ringFloor;
        private bool _ringWait;
        private bool _ringNew;

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

        /// <summary>The ring placer is up: the D-pad is its, so the phone stays shut.</summary>
        public bool Placing => _ringPlacing;

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
            if (!me.IsAlive || me.IsInVehicle() || me.IsRagdoll || Nearest(me.Position) > CourtReach)
            {
                Stop(me.IsAlive && !me.IsInVehicle() ? "Off the court -- the ball stays behind." : null);
                return;
            }

            HoldTheButtons(_mode == Mode.Charging || _mode == Mode.Shooting || Placing);
            ShotClip(me);

            switch (_mode)
            {
                case Mode.Starting: Starting(me, now); break;
                case Mode.Holding:
                    if (_ringPlacing) RingPlacing(me, dt);
                    else Holding(me, now);
                    break;
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
            if (!_startRead)
            {
                _startRead = true;
                _start = ReadStart();
            }

            var d = me.Position.DistanceTo(_start);
            if (d > RingReach) return;

            Ring(_start);

            if (d > OfferReach || me.IsInVehicle() || CourtHost.Busy()) return;

            CourtHost.Help("Press ~INPUT_CONTEXT~ to shoot hoops.");

            if (Game.IsControlJustPressed(Control.Context)) Start(me);
        }

        /// <summary>Where a game starts: his own spot from the ini, or the middle of the court.</summary>
        private static Vector3 ReadStart()
        {
            try
            {
                var parts = CourtHost.Read("Hoops", "Start", "").Split(',');

                float x, y, z;
                if (parts.Length == 3 &&
                    float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out x) &&
                    float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out y) &&
                    float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out z))
                {
                    return new Vector3(x, y, z);
                }
            }
            catch { }

            return StartAt;
        }

        private static void Ring(Vector3 spot)
        {
            try
            {
                Function.Call(Hash.DRAW_MARKER, 1, spot.X, spot.Y, spot.Z - 1.0f,
                              0f, 0f, 0f, 0f, 0f, 0f,
                              1.1f, 1.1f, 0.35f,
                              240, 160, 60, 45,
                              false, false, 2, false, 0, 0, false);
            }
            catch
            {
                // A court without its ring is still a court.
            }
        }

        private void Start(Ped me)
        {
            CourtHost.Swallow();

            // Whether the builder's tools are his.
            _dev = false;

            try
            {
                _dev = string.Equals(CourtHost.Read("Developer", "Tools", "false").Trim(), "true", StringComparison.OrdinalIgnoreCase);
            }
            catch { }

            // Every hoop on the court, and the one he is looking at to start with.
            LoadRings();
            _hoop = 0;
            _hoop = HoopToward(me.Position, Deg360(GameplayCamera.Rotation.Z));

            // Asked for now and let go at the end: the only things this ever loads.
            try
            {
                foreach (var d in Dicts) Function.Call(Hash.REQUEST_ANIM_DICT, d);
                Ready(new Model(BallName));
            }
            catch { /* asked again while it waits */ }

            _score = _streak = _shots = _made = 0;
            _fine = 0f;
            _ringPlacing = false;
            _banner = "";
            _mode = Mode.Starting;
            _at = Game.GameTime;

            CourtHost.Info("Hoops: a game on, " + _ring.Count + " hoop(s) on the court.");
        }

        private void Starting(Ped me, int now)
        {
            var ready = false;

            try
            {
                ready = Ready(new Model(BallName));
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
                CourtHost.Warn("Hoops: the ball or the clips would not load in four seconds.");
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
                CourtButtons.Drop();
                CourtDraw.Release();
                foreach (var d in Dicts) Function.Call(Hash.REMOVE_ANIM_DICT, d);
                new Model(BallName).MarkAsNoLongerNeeded();
            }
            catch { }

            if (_mode != Mode.Starting && _shots > 0)
            {
                CourtHost.Info("Hoops: " + _score + " point(s), " + _made + " of " + _shots + ", best streak " + _best + ".");
                CourtHost.Ticker("~g~Hoops.~s~ " + _score + " points, " + _made + " of " + _shots +
                              (_best > 1 ? ", " + _best + " in a row." : "."));
            }
            else if (!string.IsNullOrEmpty(why))
            {
                CourtHost.Problem(why);
            }

            _mode = Mode.Off;
            _ringPlacing = false;
            CourtHost.Swallow();
        }

        public void RestoreWorld()
        {
            if (_mode != Mode.Off) Stop(null);
        }

        // ---- the ball in his hands ------------------------------------------------------------

        /// <summary>The ball in his left hand -- the same ball every time -- and the ball-game idle on his arms.</summary>
        private void GiveBall(Ped me)
        {
            try
            {
                if (_ball == null || !_ball.Exists())
                {
                    var model = new Model(BallName);
                    if (!Ready(model)) return;

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

                Attach(me);
                Hold(me, true);

                _mode = Mode.Holding;
                _at = Game.GameTime;
            }
            catch (Exception ex)
            {
                CourtHost.Debug("Hoops: could not put the ball in his hands: " + ex.Message);
                Stop("The ball would not load. Try again in a moment.");
            }
        }

        /// <summary>The ball into his left hand, where Michael put it.</summary>
        private void Attach(Ped me)
        {
            if (_ball == null || !_ball.Exists()) return;

            try
            {
                var bone = Function.Call<int>(Hash.GET_PED_BONE_INDEX, me.Handle, LeftHand);
                Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, _ball.Handle, me.Handle, bone,
                              Grip[0] * 0.01f, Grip[1] * 0.01f, Grip[2] * 0.01f, Grip[3], Grip[4], Grip[5],
                              false, false, false, false, 2, true);
            }
            catch { }
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

            // The hoop he is looking at is the one he is shooting at.
            _hoop = HoopToward(me.Position, Deg360(GameplayCamera.Rotation.Z));

            // The builder's tools: the near hoop's ring, a ring for a hoop not yet on the list, and
            // where a game starts.
            if (_dev)
            {
                if (JustPressed(Control.PhoneRight))
                {
                    StartRing(me, NearestHoop(me.Position), false);
                    return;
                }

                if (JustPressed(Control.PhoneLeft))
                {
                    AddRing(me);
                    return;
                }

                if (JustPressed(Control.PhoneDown))
                {
                    StartHere(me);
                    return;
                }
            }

            if (!Down(Control.Attack) || now - _at < 250) return;

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

            // Street Golf's aim: where the camera looks, and a fine turn on the left stick with his
            // feet planted. He turns to it, the way the golfer does.
            try
            {
                var fine = Function.Call<float>(Hash.GET_DISABLED_CONTROL_NORMAL, 0, (int)Control.MoveLeftRight);
                if (Math.Abs(fine) > 0.15f) _fine = Clamp(_fine - fine * FineSpeed * dt, -FineMost, FineMost);
            }
            catch { }

            _aim = Deg360(GameplayCamera.Rotation.Z + _fine);
            Turn(me, _aim, dt);

            _hoop = HoopToward(me.Position, _aim);

            if (!Down(Control.Attack)) Shoot(me);
        }

        /// <summary>
        /// Where the line starts: THE BALL IN HIS HAND, where it is right now. Michael asked for the
        /// line to come from Franklin so it shows where the ball really goes; it started over his
        /// head, where the ball was only going to be. Before there is a ball, over his head.
        /// </summary>
        private Vector3 From(Ped me, float aim)
        {
            try
            {
                if (_ball != null && _ball.Exists() && Function.Call<bool>(Hash.IS_ENTITY_ATTACHED, _ball.Handle))
                    return _ball.Position;
            }
            catch { }

            return me.Position + Vector3.WorldUp * ShotUp + Dir(aim) * 0.25f;
        }

        /// <summary>
        /// The flight, point by point, and where it ends -- coming down through the rim's height, or
        /// at the floor. Every point is the flight's own curve at that moment, not a sum of steps:
        /// the steps drifted, and the line came down 15 to 50 cm past where the ball did (worked
        /// out on 2026-09-26), which on a rim 46 cm across is the whole shot.
        /// </summary>
        private List<Vector3> Fly(Vector3 from, Vector3 v, float floor, out Vector3 end, out bool down)
        {
            var pts = new List<Vector3> { from };
            var prev = from;
            end = from;
            down = false;

            var rim = Rim.Z;

            for (var i = 1; i <= 160; i++)
            {
                var t = i * LineStep;
                var next = At(from, v, t);

                if (prev.Z > rim && next.Z <= rim && v.Z - Gravity * t < 0f)
                {
                    // Exactly where it comes down through the rim's height, not the nearest step.
                    var disc = v.Z * v.Z - 2f * Gravity * (rim - from.Z);
                    end = disc >= 0f ? At(from, v, (v.Z + (float)Math.Sqrt(disc)) / Gravity) : next;
                    end.Z = rim;
                    down = true;
                    pts.Add(end);
                    break;
                }

                if (next.Z < floor)
                {
                    end = next;
                    down = true;
                    pts.Add(next);
                    break;
                }

                pts.Add(next);
                prev = next;
                end = next;
            }

            return pts;
        }

        /// <summary>Where a throw is, this long after it left.</summary>
        private static Vector3 At(Vector3 from, Vector3 v, float t)
        {
            return from + v * t + Vector3.WorldUp * (-0.5f * Gravity * t * t);
        }

        /// <summary>How far out the throw comes down for a power. See ReachNear.</summary>
        private static float Reach(float power)
        {
            var p = Clamp(power, 0f, 1f);
            var slope = (ReachMid - ReachNear) / MidAt;

            if (p <= MidAt) return ReachNear + slope * p;

            // Past the court: on at the same pace, then quicker and quicker to the top.
            var u = p - MidAt;
            var top = 1f - MidAt;
            var bend = (ReachFar - ReachMid - slope * top) / (top * top);
            return ReachMid + slope * u + bend * u * u;
        }

        /// <summary>
        /// The throw for a power along an aim: the point that far out along it at the height of the
        /// hoop he is shooting at, and the throw that comes down through it. How high the hoop
        /// hangs is all of the hoop that goes into it.
        /// </summary>
        private bool Aimed(Vector3 from, float aim, float power, out Vector3 to, out Vector3 velocity)
        {
            to = from + Dir(aim) * Reach(power);
            to.Z = Rim.Z;
            return Launch(from, to, out velocity);
        }

        /// <summary>The hoop a heading points at: of the two rings, the one nearest that way from where he stands.</summary>
        private int HoopToward(Vector3 from, float heading)
        {
            var best = _hoop;
            var off = float.MaxValue;

            for (var i = 0; i < _ring.Count; i++)
            {
                var d = Math.Abs(AngleDiff(heading, Toward(from, _ring[i])));
                if (d >= off) continue;

                off = d;
                best = i;
            }

            return best;
        }

        /// <summary>How far, flat, to the nearer hoop's ring.</summary>
        private float Nearest(Vector3 at)
        {
            var near = float.MaxValue;
            foreach (var r in _ring) near = Math.Min(near, Flat(r - at));
            return near;
        }

        /// <summary>The hoop whose ring is nearest, flat.</summary>
        private int NearestHoop(Vector3 at)
        {
            var best = 0;

            for (var i = 1; i < _ring.Count; i++)
            {
                if (Flat(_ring[i] - at) < Flat(_ring[best] - at)) best = i;
            }

            return best;
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
            // The throw, fixed now: where the line was going is where the ball comes down.
            _lastPower = _power;
            _launchFrom = From(me, _aim);

            if (!Aimed(_launchFrom, _aim, _power, out _launchTo, out _launch))
            {
                _launch = Vector3.Zero;
                _launchTo = Vector3.Zero;
            }

            _shotAt = me.Position;

            try
            {
                // The arms only, on the upper body: no jump under them. Michael had the hop in and
                // out again on 2026-09-26 -- "no jump on the animation just do the hands".
                Function.Call(Hash.TASK_PLAY_ANIM, me.Handle, ShotDict, ShotAnim, 8f, -8f, -1, 48, 0f, false, false, false);
                _clipLive = true;
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
                if (Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM, me.Handle, ShotDict, ShotAnim, 3))
                    phase = Function.Call<float>(Hash.GET_ENTITY_ANIM_CURRENT_TIME, me.Handle, ShotDict, ShotAnim);
            }
            catch { }

            if (phase < ShotRelease && now - _at < ReleaseMs) return;

            Release(me);
        }

        /// <summary>
        /// Out of his hand and on its way, down through where the line was going when he let go.
        /// The push has moved his hands since, so the throw is worked out again from where the
        /// ball really is -- to the same point, never to the hoop.
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

                // From his hand, where the push has put it, down through where the line was going.
                var from = _ball.Position;
                Vector3 v;

                if (_launchTo == Vector3.Zero || !Launch(from, _launchTo, out v)) v = _launch;

                // Flown by the game with nothing slowing it, so it flies the line to the letter.
                // Street Golf's ball had a touch of drag, and with it every shot came down 30 to
                // 55 cm short of where the line was going (the log, 2026-09-26). Nought now.
                Function.Call(Hash.FREEZE_ENTITY_POSITION, _ball.Handle, false);
                Function.Call(Hash.SET_ENTITY_COLLISION, _ball.Handle, true, true);
                Function.Call(Hash.ACTIVATE_PHYSICS, _ball.Handle);
                Function.Call(Hash.SET_OBJECT_PHYSICS_PARAMS, _ball.Handle, -1f, -1f, 0f, 0f, 0f, -1f, -1f, -1f, -1f, -1f, -1f);
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
                CourtHost.Debug("Hoops: the ball would not fly: " + ex.Message);
            }

            _shots++;
            _scored = false;
            _touched = false;
            _crossed = false;
            _mode = Mode.Flying;
            _at = Game.GameTime;
        }

        /// <summary>
        /// The ball in the air: down through either hoop's ring is a basket -- the ring as placed,
        /// nothing else -- and the end of the flight is the verdict.
        /// </summary>
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

            for (var i = 0; i < _ring.Count; i++)
            {
                var r = _ring[i];
                if (!(_prev.Z > r.Z && pos.Z <= r.Z)) continue;

                // Where it came down through this ring's height, and whether that is inside the ring.
                var t = (_prev.Z - r.Z) / Math.Max(0.0001f, _prev.Z - pos.Z);
                var at = _prev + (pos - _prev) * t;
                var off = Flat(at - r);

                if (i == _hoop && !_crossed)
                {
                    _crossed = true;
                    _cross = at;

                    // How far the real flight came down from where the line was going. Nothing should come
                    // between them but the game's air, so a steady gap here is a thing to put right.
                    if (_launchTo != Vector3.Zero)
                    {
                        var aim = Flat3D(_launchTo - _launchFrom);
                        aim *= 1f / Math.Max(0.01f, aim.Length());
                        var gap = Flat3D(at - _launchTo);

                        CourtHost.Info("Hoops: the ball came down " + (Flat(gap) * 100f).ToString("0") + " cm from where the line was going (" +
                                 (Vector3.Dot(gap, aim) * 100f).ToString("+0;-0;0") + " cm along the throw).");
                    }
                }

                if (!_scored && off <= _ringSize[i]) Made(i, off);
            }

            _prev = pos;

            var age = now - _at;
            var still = false;

            try { still = _ball.Velocity.Length() < 0.4f && age > 900; }
            catch { }

            // A basket drops through the net before the next ball; a miss is called when it lands.
            if (_scored && age > 1200 || !_scored && (still || age > 3600) || age > 5000) Verdict();
        }

        private void Made(int hoop, float off)
        {
            _scored = true;

            var from = Flat(_ring[hoop] - _shotAt);
            var three = from > ThreeFrom;
            var points = three ? 3 : 2;

            _score += points;
            _made++;
            _streak++;
            if (_streak > _best) _best = _streak;

            var head = !_touched ? (three ? "SWISH -- THREE" : "SWISH") : three ? "THREE" : "BUCKET";
            var sub = "+" + points + (_streak > 1 ? " -- " + _streak + " in a row" : "") + ".  " + _score + " points.";
            Banner(head, sub, three ? Gold : Green);

            Sound("CHECKPOINT_PERFECT", "HUD_MINI_GAME_SOUNDSET");
            CourtHost.Info("Hoops: " + head + " through hoop " + (hoop + 1) + " from " + from.ToString("0.0") + " m, " +
                     (off * 100f).ToString("0") + " cm off the middle of its ring.");
        }

        /// <summary>The shot called, and a word on why it missed -- short, long or wide is what the meter needs to know.</summary>
        private void Verdict()
        {
            if (!_scored)
            {
                _streak = 0;

                string why;

                if (!_crossed) why = "Short -- more on the meter.";
                else
                {
                    // Where it came down through the ring's height, against where the ring is: along
                    // the line from his hands to it, and either side of it.
                    var line = Flat3D(Rim - _launchFrom);
                    var reach = line.Length();
                    line *= 1f / Math.Max(0.01f, reach);

                    var off = Flat3D(_cross - _launchFrom);
                    var along = Vector3.Dot(off, line) - reach;
                    var side = line.X * off.Y - line.Y * off.X;

                    why = Math.Abs(side) > 0.25f ? (side > 0f ? "Wide left -- aim a touch right." : "Wide right -- aim a touch left.")
                        : along < -0.25f ? "Short -- more on the meter."
                        : along > 0.25f ? "Long -- less on the meter."
                        : "Off the rim.";

                    CourtHost.Info("Hoops: missed hoop " + (_hoop + 1) + ", down through its height " +
                             (Flat(_cross - Rim) * 100f).ToString("0") + " cm from the middle of its ring.");
                }

                Banner("MISS", why, Red);
                Sound("CHECKPOINT_MISSED", "HUD_MINI_GAME_SOUNDSET");
            }

            _mode = Mode.Result;
            _at = Game.GameTime;
        }

        // ---- the rings, and the throw through one ----------------------------------------------

        /// <summary>
        /// The throw that comes down through a point: at Arc, or steeper when the point is close and
        /// high enough that Arc would only reach it on the way up -- a ball going up through a ring
        /// is no basket. Street Golf's physics flies it without drag, so the sum is the flight.
        /// </summary>
        private static bool Launch(Vector3 from, Vector3 to, out Vector3 velocity)
        {
            velocity = Vector3.Zero;

            var d = to - from;
            var flat = Flat3D(d);
            var dist = flat.Length();
            if (dist < 0.05f) return false;
            flat *= 1f / dist;

            // Past the top of the flight by the time it gets there: tan(arc) at least twice the
            // climb over the distance, and a little over.
            var need = (float)(Math.Atan2(2.2f * d.Z, dist) * 180.0 / Math.PI);
            var arc = Math.Min(80f, Math.Max(Arc, need));

            var th = arc * (float)Math.PI / 180f;
            var c = (float)Math.Cos(th);
            var denom = 2f * c * c * (dist * (float)Math.Tan(th) - d.Z);
            if (denom <= 0.01f) return false;

            var speed = (float)Math.Sqrt(Gravity * dist * dist / denom);
            velocity = flat * (speed * c) + Vector3.WorldUp * (speed * (float)Math.Sin(th));
            return true;
        }

        /// <summary>
        /// The rings where they were placed: RingAt for everybody, Michael's own placings from the ini
        /// over them, and after them any hoop he added on the court -- Ring3 and on, up to the first
        /// one missing.
        /// </summary>
        private void LoadRings()
        {
            _ring.Clear();
            _ringSize.Clear();

            for (var i = 0; i < MostRings; i++)
            {
                var have = i < RingAt.Length;
                var at = have ? RingAt[i] : Vector3.Zero;
                var size = have ? RingWide[i] : 0.23f;

                try
                {
                    var parts = CourtHost.Read("Hoops", "Ring" + (i + 1), "").Split(',');

                    if (parts.Length == 4)
                    {
                        var f = new float[4];
                        var ok = true;

                        for (var k = 0; k < 4 && ok; k++)
                            ok = float.TryParse(parts[k].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out f[k]);

                        if (ok)
                        {
                            at = new Vector3(f[0], f[1], f[2]);
                            size = Clamp(f[3], 0.08f, 1.2f);
                            have = true;
                        }
                    }
                }
                catch { }

                if (!have) break;

                _ring.Add(at);
                _ringSize.Add(size);
            }
        }

        private static string Pack(Vector3 c, float size)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0.000},{1:0.000},{2:0.000},{3:0.000}", c.X, c.Y, c.Z, size);
        }

        /// <summary>
        /// The ring placer up, on the ring of the hoop NEAREST him. It took the one he was looking
        /// at, and under hoop 1 looking up the court that was hoop 2, twenty metres off: the ring
        /// he was moving was out of sight, and the one beside him never moved (2026-09-26).
        /// </summary>
        private void StartRing(Ped me, int who, bool fresh)
        {
            _ringPlacing = true;
            _ringWho = who;
            _ringNew = fresh;
            _ringWas = _ring[who];
            _ringWasSize = _ringSize[who];
            _ringWait = true;

            float ground;
            _ringFloor = Floor(me.Position + Vector3.WorldUp * 0.5f, out ground) ? ground : me.Position.Z - 1f;

            Sound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        /// <summary>
        /// A NEW HOOP'S RING, for a hoop on the court the list does not have yet: a step in front of
        /// him at a rim's height, handed straight to the placer. B takes it away again. Michael asked
        /// for every hoop on the court to count, on 2026-09-26.
        /// </summary>
        private void AddRing(Ped me)
        {
            if (_ring.Count >= MostRings)
            {
                Banner("NO MORE HOOPS", MostRings + " is the most a court is read for.", Red);
                return;
            }

            float ground;
            var floor = Floor(me.Position + Vector3.WorldUp * 0.5f, out ground) ? ground : me.Position.Z - 1f;
            var at = me.Position + Dir(Deg360(GameplayCamera.Rotation.Z)) * 1.5f;

            _ring.Add(new Vector3(at.X, at.Y, floor + 3.05f));
            _ringSize.Add(0.23f);

            StartRing(me, _ring.Count - 1, true);
        }

        /// <summary>The one ring a game starts from, moved to where he is standing, and kept in the ini.</summary>
        private void StartHere(Ped me)
        {
            _start = me.Position;
            _startRead = true;

            var text = string.Format(CultureInfo.InvariantCulture, "{0:0.000},{1:0.000},{2:0.000}", _start.X, _start.Y, _start.Z);
            CourtHost.Put("Hoops", "Start", text);
            CourtHost.Info("Hoops: a game starts here now -- [Hoops] Start=" + text + ".");

            Banner("THE GAME STARTS HERE", "Put the ball down and the ring is here.", Green);
            Sound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        /// <summary>
        /// THE RING PLACER, for the developer tools: a flat ring on the hoop nearest him, slid
        /// about with the left stick the way the camera faces, up and down on the D-pad, and made
        /// bigger and smaller on D-pad right and left -- X for four times as fast. A keeps it, in
        /// the ini; B puts it back. Michael asked for a little hoop he could place right where the
        /// ball needs to pass through, on 2026-09-26.
        /// </summary>
        private void RingPlacing(Ped me, float dt)
        {
            Hold(me, false);

            var i = _ringWho;

            if (JustPressed(Control.PhoneSelect))
            {
                _ringPlacing = false;

                var text = Pack(_ring[i], _ringSize[i]);
                CourtHost.Put("Hoops", "Ring" + (i + 1), text);
                CourtHost.Info("Hoops: hoop " + (i + 1) + "'s ring placed -- [Hoops] Ring" + (i + 1) + "=" + text +
                         " (x, y, z, and from the middle to the edge).");

                _ringNew = false;

                Sound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                return;
            }

            if (JustPressed(Control.PhoneCancel))
            {
                _ringPlacing = false;

                if (_ringNew)
                {
                    // A new one, never kept: gone again.
                    _ring.RemoveAt(i);
                    _ringSize.RemoveAt(i);
                    if (_hoop >= _ring.Count) _hoop = 0;
                }
                else
                {
                    _ring[i] = _ringWas;
                    _ringSize[i] = _ringWasSize;
                }

                Sound("BACK", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                return;
            }

            var step = (Down(Control.Jump) ? 4f : 1f) * dt;

            float lx = 0f, ly = 0f;

            try
            {
                lx = Function.Call<float>(Hash.GET_DISABLED_CONTROL_NORMAL, 0, (int)Control.MoveLeftRight);
                ly = Function.Call<float>(Hash.GET_DISABLED_CONTROL_NORMAL, 0, (int)Control.MoveUpDown);
            }
            catch { }

            if (Math.Abs(lx) < 0.12f) lx = 0f;
            if (Math.Abs(ly) < 0.12f) ly = 0f;

            // Across the court the way the camera faces: up on the stick is away from him.
            var ahead = Dir(GameplayCamera.Rotation.Z);
            var right = new Vector3(ahead.Y, -ahead.X, 0f);
            var c = _ring[i] + (right * lx - ahead * ly) * (RingSlide * step);

            if (Down(Control.PhoneUp)) c.Z += RingSlide * step;
            if (Down(Control.PhoneDown)) c.Z -= RingSlide * step;

            _ring[i] = c;

            // D-pad right brought the placer up; it grows the ring once it has been let go.
            if (_ringWait && !Down(Control.PhoneRight)) _ringWait = false;

            var size = _ringSize[i];
            if (!_ringWait && Down(Control.PhoneRight)) size += RingGrow * step;
            if (Down(Control.PhoneLeft)) size -= RingGrow * step;
            _ringSize[i] = Clamp(size, 0.08f, 1.2f);
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
                Game.DisableControlThisFrame(Control.CharacterWheel);

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
            // The ring placer has the screen to itself.
            if (_ringPlacing)
            {
                RingScreen();
                return;
            }

            var dist = Flat(Rim - me.Position);
            var three = dist > ThreeFrom;

            if (_banner.Length > 0 && now < _bannerUntil)
            {
                CourtDraw.Rect(0.5f, 0.235f, 1f, 0.13f, Color.FromArgb(120, 0, 0, 0));
                CourtDraw.Text(_banner, 0.5f, 0.17f, 1.1f, _bannerInk, CourtDraw.FontPricedown, true, true, true);
                CourtDraw.Text(_bannerSub, 0.5f, 0.262f, 0.45f, Color.White, CourtDraw.FontBody, true, true, false);
            }
            else if (_mode == Mode.Holding)
            {
                CourtHost.Help(dist.ToString("0.0") + " m -- " + (three ? "a three" : "a two") +
                                   ". Hold ~INPUT_ATTACK~, and let go when the arc looks right.");
            }

            CourtBars.Draw("SCORE", _score.ToString(), Gold,
                          "STREAK", _streak.ToString(), _streak > 1 ? Green : Color.White,
                          "MADE", _made + " / " + _shots, Color.White);

            if (_mode == Mode.Holding)
            {
                if (_dev)
                    CourtButtons.Show(Control.Attack, "Hold to shoot",
                                     Control.PhoneCancel, "Put the ball down",
                                     Control.PhoneRight, "Place the near hoop",
                                     Control.PhoneLeft, "Add a hoop",
                                     Control.PhoneDown, "Start the game here");
                else
                    CourtButtons.Show(Control.Attack, "Hold to shoot",
                                     Control.PhoneCancel, "Put the ball down");
            }

            // Where a basket has to go through, for somebody placing them.
            if (_dev) Rings(-1);

            if (_mode == Mode.Holding || _mode == Mode.Charging) Line(me);
            if (_mode == Mode.Charging) Meter();
        }

        /// <summary>
        /// STREET GOLF'S AIM LINE, faded: the flight worked out the way the shot will go, out of the
        /// ball in his hand, drawn as a thin ribbon turned to the camera -- and gone to nothing by
        /// the top of the throw. Holding the ball, the last shot's power along where he is looking;
        /// with the meter running, the shot as the meter stands, so it rises and stretches as the
        /// meter plays. Michael asked for the idea of where the ball goes and not the spot, on
        /// 2026-09-26: the ring it used to end in sat exactly where the ball would come down.
        /// Nothing changes colour for looking at the hoop, and nothing steers the ball to it.
        /// </summary>
        private void Line(Ped me)
        {
            try
            {
                var eye = GameplayCamera.Position;
                var charging = _mode == Mode.Charging;
                var aim = charging ? _aim : Deg360(GameplayCamera.Rotation.Z + _fine);
                var from = From(me, aim);

                Vector3 to, v;
                if (!Aimed(from, aim, charging ? _power : _lastPower, out to, out v)) return;

                var pts = Fly(from, v, me.Position.Z - 1f, out _, out _);

                // Street Golf's own inks: cool while he lines it up, warm while the meter runs.
                var ink = charging ? Color.FromArgb(70, 255, 220, 120) : Color.FromArgb(45, 190, 235, 200);

                // The first part of the flight only, fading as it goes, and never more than thirty
                // pieces however far the throw.
                var shown = Math.Max(2, (int)(pts.Count * LineShown));
                var stride = Math.Max(1, (shown + 29) / 30);

                for (var i = 0; i + 1 < shown; i += stride)
                {
                    var left = 1f - i / (float)shown;
                    var a = (int)(ink.A * left);
                    if (a < 2) break;

                    Segment(pts[i], pts[Math.Min(i + stride, pts.Count - 1)], 0.03f, Color.FromArgb(a, ink.R, ink.G, ink.B), eye);
                }
            }
            catch
            {
                // No line this frame.
            }
        }

        /// <summary>
        /// The shot's clip on his arms: played at its pace, and stopped once the ball has gone and
        /// the arms have made their one push. Raise the Roof pumps twice.
        /// </summary>
        private void ShotClip(Ped me)
        {
            if (!_clipLive) return;

            try
            {
                if (!Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM, me.Handle, ShotDict, ShotAnim, 3))
                {
                    _clipLive = false;
                    return;
                }

                Function.Call(Hash.SET_ENTITY_ANIM_SPEED, me.Handle, ShotDict, ShotAnim, ShotSpeed);

                if (_released &&
                    Function.Call<float>(Hash.GET_ENTITY_ANIM_CURRENT_TIME, me.Handle, ShotDict, ShotAnim) >= ShotCut)
                {
                    Function.Call(Hash.STOP_ANIM_TASK, me.Handle, ShotDict, ShotAnim, -3f);
                    _clipLive = false;
                }
            }
            catch
            {
                _clipLive = false;
            }
        }

        /// <summary>How far apart in time the line's points are worked out.</summary>
        private const float LineStep = 0.03f;

        /// <summary>How much of the flight the line shows before it has faded to nothing: the rise and the top of it.</summary>
        private const float LineShown = 0.55f;

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

        /// <summary>The ring placer on screen: the ring, bright, and how high and how wide it is.</summary>
        private void RingScreen()
        {
            var i = _ringWho;

            CourtHost.Help("Hoop " + (i + 1) + "'s ring, the one nearest you: the ball has to come down through it. " +
                               "Left stick slides it, D-pad up and down raises it, D-pad left and right sizes it, X for faster.");

            CourtBars.Draw("HEIGHT", (_ring[i].Z - _ringFloor).ToString("0.00") + " m", Color.White,
                          "ACROSS", (_ringSize[i] * 200f).ToString("0") + " cm", Color.White,
                          "HOOP", (i + 1).ToString(), Gold);

            CourtButtons.Show(Control.PhoneSelect, "Keep it",
                             Control.PhoneCancel, "Put it back");

            Rings(i);
        }

        /// <summary>Both rings, for somebody with the developer tools: the one being placed bright, the other faint.</summary>
        private void Rings(int bright)
        {
            for (var i = 0; i < _ring.Count; i++)
            {
                var ink = i == bright ? Color.FromArgb(255, 255, 200, 60) : Color.FromArgb(150, 255, 255, 255);
                Circle(_ring[i], _ringSize[i], ink);
            }
        }

        /// <summary>A flat ring of lines, and a short line down its middle where the net hangs.</summary>
        private static void Circle(Vector3 c, float r, Color ink)
        {
            const int sides = 32;

            try
            {
                var prev = c + new Vector3(r, 0f, 0f);

                for (var k = 1; k <= sides; k++)
                {
                    var a = k * 2.0 * Math.PI / sides;
                    var next = c + new Vector3((float)Math.Cos(a) * r, (float)Math.Sin(a) * r, 0f);
                    Function.Call(Hash.DRAW_LINE, prev.X, prev.Y, prev.Z, next.X, next.Y, next.Z, (int)ink.R, (int)ink.G, (int)ink.B, (int)ink.A);
                    prev = next;
                }

                Function.Call(Hash.DRAW_LINE, c.X, c.Y, c.Z, c.X, c.Y, c.Z - 0.4f, (int)ink.R, (int)ink.G, (int)ink.B, (int)ink.A);
            }
            catch { }
        }

        /// <summary>
        /// Street Golf's meter: sixteen cells filling amber, and a white tick where the power is. No
        /// green on it and no distance under it: where the ball goes is his to judge.
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

            CourtDraw.Rect(left + width * 0.5f, y, width + 0.012f, h + 0.012f, Color.FromArgb(150, 0, 0, 0));

            for (var c = 0; c < cells; c++)
            {
                var f0 = c / (float)cells;
                var x = left + c * (cw + gap) + cw * 0.5f;

                CourtDraw.Rect(x, y, cw, h, _power > f0 ? Gold : Track);
            }

            CourtDraw.Rect(left + width * Clamp(_power, 0f, 1f), y, 0.002f, h + 0.012f, Color.White);
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

        // ---- what the court used to borrow from its host ----------------------------------------

        /// <summary>
        /// True once the model is loaded and ready to spawn from. Asked for without waiting: called
        /// again next tick it is usually there, so the cost of "no" is one frame, not one second.
        /// </summary>
        private static bool Ready(Model model)
        {
            try
            {
                if (!model.IsValid || !model.IsInCdImage) return false;
                if (model.IsLoaded) return true;

                model.Request();
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>The floor under a point, if the game has one there.</summary>
        private static bool Floor(Vector3 from, out float z)
        {
            try
            {
                var arg = new OutputArgument();
                var hit = Function.Call<bool>(Hash.GET_GROUND_Z_FOR_3D_COORD, from.X, from.Y, from.Z, arg, false, false);

                z = hit ? arg.GetResult<float>() : 0f;
                return hit;
            }
            catch
            {
                z = 0f;
                return false;
            }
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
