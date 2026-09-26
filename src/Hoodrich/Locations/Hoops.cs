using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.UI;
using Control = GTA.Control;

namespace Hoodrich.Locations
{
    /// <summary>
    /// SHOOTING HOOPS on the Chamberlain Hills court, on the court's own hoops. Michael asked for
    /// it on 2026-09-26: a basketball, a jump and a throw, and the aim and the shot the way his
    /// Street Golf does them.
    ///
    /// THE THROW IS FREE. The aim is where the camera looks. Hold the button and the meter runs
    /// up and back down, and the line out of his hand stretches with it -- from a lob at his feet
    /// to a heave forty metres down the street. Let go, and the ball comes down through the ring
    /// at the end of the line. Nothing works a basket out for him and nothing steers the ball at
    /// the hoop: Michael asked for it that way, "if you wanted to throw it really far you could,
    /// dont try and make it swosh".
    ///
    /// A BASKET IS THE BALL DOWN THROUGH THE HOOP'S RING. Each hoop has a flat ring where its rim
    /// is, and the ball's middle has to come down through it. The hoops are built into the map,
    /// so the game will not say where they are, and reading the backboards with rays found
    /// neither of this court's -- the log of 2026-09-26 -- so the balls went in through a guess.
    /// The rings are placed by hand on the court instead (the developer tools, D-pad right with
    /// the ball) and written into RingAt for everybody else.
    ///
    /// THE SHOT is both hands pushing the ball up from the chest -- the up half of Raise the Roof,
    /// played quick and cut after the one push -- over a little hop straight up. The game has no
    /// basketball of its own and no clip of anybody shooting one; that is the nearest thing to it.
    /// Held, he carries the ball in the ball-game idle that goes with it.
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
        /// One way to shoot: the clip, on the upper body; how far through it the ball goes; and how
        /// high over him the ball is when it does.
        /// </summary>
        private sealed class Style
        {
            public string Name;
            public string Dict;
            public string Clip;
            public float Release;
            public float Up;

            /// <summary>How much faster than the game plays it.</summary>
            public float Speed = 1f;

            /// <summary>
            /// Where it is stopped, once the ball has gone: Raise the Roof pumps the arms twice, and
            /// a shot is one push. Nought lets it play out.
            /// </summary>
            public float Cut;
        }

        /// <summary>
        /// THE WAYS TO SHOOT. The set shot is the one Michael kept, and the only one a player gets;
        /// the other two stay for the developer tools to try, D-pad left on the court. Every one is
        /// played over the little hop (see Hop), so the two-hand jumper went: it was the set shot
        /// with a jump.
        /// </summary>
        private static readonly Style[] Styles =
        {
            new Style { Name = "Set shot", Dict = "anim@mp_player_intupperraise_the_roof", Clip = "enter", Release = 0.30f, Up = 1.2f, Speed = 1.6f, Cut = 0.46f },
            new Style { Name = "Jump shot", Dict = "weapons@projectile@", Clip = "throw_h_fb_stand", Release = 0.36f, Up = 1.5f, Speed = 1.3f },
            new Style { Name = "Push shot", Dict = "weapons@projectile@", Clip = "throw_m_fb_stand", Release = 0.34f, Up = 1.2f, Speed = 1.3f }
        };

        /// <summary>Every clip set a game might ask for, all let go at the end. See Wanted.</summary>
        private static readonly string[] Dicts = { HoldDict, "anim@mp_player_intupperraise_the_roof", "weapons@projectile@" };

        private int _style;
        private int _shotStyle;
        private bool _styleRead;

        private Style Shot => Styles[_style];

        /// <summary>How near the spot to be offered a game, and how far the ring is drawn from.</summary>
        private const float OfferReach = 1.3f;
        private const float RingReach = 30f;

        /// <summary>How far from both hoops before the game is put away.</summary>
        private const float CourtReach = 26f;

        /// <summary>
        /// THE RINGS: for each hoop, in the order of Spots, the middle of its rim and how far from
        /// there to the edge of the ring the ball has to come down through. Placed by hand on the
        /// court (see RingPlacing); Michael's own placings are read from the ini over these, and go
        /// in here for the release once the ball really goes in through them. Until then they are
        /// a regulation court's, from each spot: 4.2 m down the court and 3.05 m up.
        /// </summary>
        private static readonly Vector3[] RingAt =
        {
            new Vector3(-198.515f, -1505.758f, 33.681f),
            new Vector3(-212.577f, -1521.766f, 33.666f)
        };

        private static readonly float[] RingWide = { 0.23f, 0.23f };

        /// <summary>
        /// PH_L_Hand: the ball rides in his LEFT hand. The ball-game idle holds that hand up in
        /// front of him for it, and the right one hangs; Michael moved it there on 2026-09-26.
        /// </summary>
        private const int LeftHand = 60309;

        /// <summary>
        /// Where in his hand: centimetres along the hand bone's own three axes, then degrees about
        /// them. Michael sets it with the positioner (D-pad down, holding the ball, with the
        /// developer tools on) and it is kept in Hoodrich.ini, [HandFit] prop_bskball_01.
        /// </summary>
        private float[] _grip = { 12f, 3f, -3f, 0f, 0f, 0f };

        private static readonly string[] GripNames = { "Move X", "Move Y", "Move Z", "Turn X", "Turn Y", "Turn Z" };

        /// <summary>The developer tools are on: the positioner is offered. Read as a game starts.</summary>
        private bool _dev;
        private bool _placing;
        private int _placeRow;
        private int _placeNext;

        /// <summary>Street Golf's meter, slowed for a hoop: up and back down over this long while the button is held.</summary>
        private const float ChargeTime = 2.6f;

        /// <summary>
        /// THE METER AGAINST HOW FAR: the ring at the end of the line comes down this far out from
        /// the ball -- a metre on an empty meter, ten at MidAt of it, forty at the top. Even and
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

        /// <summary>
        /// THE LITTLE HOP under every shot: the game's own jump, rising no faster than this -- about
        /// a foot off the floor -- and going nowhere along the ground, for this long. See Hop.
        /// </summary>
        private const float HopUp = 2.4f;
        private const int HopMs = 900;

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

        /// <summary>The two hoops' rings as this game has them, and the hoop he is shooting at: the one he looks at.</summary>
        private readonly Vector3[] _ring = new Vector3[2];
        private readonly float[] _ringSize = new float[2];
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

        /// <summary>Where the line's ring was when he let go: the ball comes down through it. See Release.</summary>
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

        /// <summary>The hop is on until then. See Hop.</summary>
        private int _hopUntil;

        /// <summary>The ring placer: whose ring, how it was before, and the floor it is measured from.</summary>
        private bool _ringPlacing;
        private int _ringWho;
        private Vector3 _ringWas;
        private float _ringWasSize;
        private float _ringFloor;
        private bool _ringWait;

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

        /// <summary>One of the placers is up: the D-pad is theirs, so the phone stays shut.</summary>
        public bool Placing => _placing || _ringPlacing;

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
            Hop(me, now);

            switch (_mode)
            {
                case Mode.Starting: Starting(me, now); break;
                case Mode.Holding:
                    if (_placing) BallPlacing(me, now);
                    else if (_ringPlacing) RingPlacing(me, dt);
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

            // His grip on the ball, and whether the builder's tools are his.
            _dev = false;

            try
            {
                var packed = Settings.Read("HandFit", BallName, "");
                if (!string.IsNullOrEmpty(packed)) _grip = Economy.Fit.Unpack(packed);

                _dev = string.Equals(Settings.Read("Developer", "Tools", "false").Trim(), "true", StringComparison.OrdinalIgnoreCase);
            }
            catch { }

            LoadRings();
            _hoop = spot;

            // The other ways to shoot are the builder's; everybody else shoots the set shot.
            if (!_dev) _style = 0;
            else if (!_styleRead)
            {
                _styleRead = true;
                _style = ReadStyle();
            }

            // Asked for now and let go at the end: the only things this ever loads.
            try
            {
                foreach (var d in Wanted()) Function.Call(Hash.REQUEST_ANIM_DICT, d);
                Models.Ready(new Model(BallName));
            }
            catch { /* asked again while it waits */ }

            _score = _streak = _shots = _made = 0;
            _fine = 0f;
            _placing = _ringPlacing = false;
            _hopUntil = 0;
            _banner = "";
            _mode = Mode.Starting;
            _at = Game.GameTime;

            Log.Info("Hoops: on the court at ring " + (spot + 1) + ", its hoop's ring at " + _ring[spot] + ".");
        }

        /// <summary>The clip sets this game needs: the idle and the set shot's -- every one, for somebody trying the others.</summary>
        private IEnumerable<string> Wanted()
        {
            yield return HoldDict;

            if (!_dev)
            {
                yield return Styles[0].Dict;
                yield break;
            }

            foreach (var d in Dicts)
            {
                if (d != HoldDict) yield return d;
            }
        }

        private void Starting(Ped me, int now)
        {
            var ready = false;

            try
            {
                ready = Models.Ready(new Model(BallName));
                foreach (var d in Wanted()) ready = ready && Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, d);
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
            _placing = _ringPlacing = false;
            _hopUntil = 0;
            InputGuard.Swallow();
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

                Attach(me);
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

        /// <summary>The ball into his left hand, where his grip says.</summary>
        private void Attach(Ped me)
        {
            if (_ball == null || !_ball.Exists()) return;

            try
            {
                var bone = Function.Call<int>(Hash.GET_PED_BONE_INDEX, me.Handle, LeftHand);
                Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, _ball.Handle, me.Handle, bone,
                              _grip[0] * 0.01f, _grip[1] * 0.01f, _grip[2] * 0.01f, _grip[3], _grip[4], _grip[5],
                              false, false, false, false, 2, true);
            }
            catch { }
        }

        /// <summary>
        /// THE POSITIONER: the ball in his hand, moved and turned while he watches, a value at a
        /// time -- D-pad up and down pick which, left and right move it, X for bigger steps, A or B
        /// when it sits right. Kept in the ini as it goes. Michael asked to place it himself.
        /// </summary>
        private void BallPlacing(Ped me, int now)
        {
            Hold(me, false);

            if (JustPressed(Control.PhoneCancel) || JustPressed(Control.PhoneSelect))
            {
                _placing = false;
                Settings.Put("HandFit", BallName, Economy.Fit.Pack(_grip));
                Log.Info("Hoops: the ball sits at " + Economy.Fit.Pack(_grip) + " in his left hand.");
                Sound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                return;
            }

            if (JustPressed(Control.PhoneUp)) { _placeRow = (_placeRow + GripNames.Length - 1) % GripNames.Length; Sound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET"); }
            if (JustPressed(Control.PhoneDown)) { _placeRow = (_placeRow + 1) % GripNames.Length; Sound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET"); }

            var way = Down(Control.PhoneRight) ? 1 : Down(Control.PhoneLeft) ? -1 : 0;
            if (way == 0 || now < _placeNext) return;

            var step = _placeRow < 3 ? 0.5f : 5f;
            if (Down(Control.Jump)) step *= 4f;

            _grip[_placeRow] = (float)Math.Round((_grip[_placeRow] + way * step) * 100f) / 100f;
            _placeNext = now + 80;

            Attach(me);
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

            // The builder's tools: the ball in his hand, the hoop's ring, and the other ways to shoot.
            if (_dev)
            {
                if (JustPressed(Control.PhoneDown))
                {
                    _placing = true;
                    _placeRow = 0;
                    Sound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                    return;
                }

                if (JustPressed(Control.PhoneRight))
                {
                    StartRing(me);
                    return;
                }

                if (JustPressed(Control.PhoneLeft))
                {
                    _style = (_style + 1) % Styles.Length;
                    SaveStyle();
                    Sound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                    Banner(Shot.Name.ToUpperInvariant(), "D-pad left for another.", Color.White);
                    _bannerUntil = Game.GameTime + 1100;
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

            return me.Position + Vector3.WorldUp * Shot.Up + Dir(aim) * 0.25f;
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

        /// <summary>How far out the ring comes down for a power. See ReachNear.</summary>
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
        /// The throw for a power along an aim: the ring that far out along it at the height of the
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

            for (var i = 0; i < _ring.Length; i++)
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
            // The throw, fixed now: where the line's ring was is where the ball comes down.
            _lastPower = _power;
            _launchFrom = From(me, _aim);

            if (!Aimed(_launchFrom, _aim, _power, out _launchTo, out _launch))
            {
                _launch = Vector3.Zero;
                _launchTo = Vector3.Zero;
            }

            _shotAt = me.Position;

            _shotStyle = _style;
            var s = Styles[_shotStyle];

            try
            {
                // A little hop straight up, and the arms shooting on the upper body over it.
                Function.Call(Hash.TASK_JUMP, me.Handle, true, false, false);
                _hopUntil = Game.GameTime + HopMs;

                Function.Call(Hash.TASK_PLAY_ANIM, me.Handle, s.Dict, s.Clip, 8f, -8f, -1, 48, 0f, false, false, false);
                _clipLive = true;
            }
            catch { }

            _released = false;
            _mode = Mode.Shooting;
            _at = Game.GameTime;
        }

        /// <summary>
        /// THE LITTLE HOP. The game's jump carries him forward, and Michael asked for the shot's to
        /// go "just up a bit": while it is on, nothing moves him along the ground, and he rises no
        /// faster than HopUp -- a foot or so off the floor, straight up and straight down.
        /// </summary>
        private void Hop(Ped me, int now)
        {
            if (_hopUntil == 0) return;

            if (now > _hopUntil)
            {
                _hopUntil = 0;
                return;
            }

            try
            {
                var v = me.Velocity;
                Function.Call(Hash.SET_ENTITY_VELOCITY, me.Handle, 0f, 0f, Math.Min(v.Z, HopUp));
            }
            catch { }
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
        /// Out of his hand and on its way, down through where the line's ring was when he let go.
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

                // From his hand, where the push has put it, down through the line's ring.
                var from = _ball.Position;
                Vector3 v;

                if (_launchTo == Vector3.Zero || !Launch(from, _launchTo, out v)) v = _launch;

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

            for (var i = 0; i < _ring.Length; i++)
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

                    // How far the real flight came down from the line's ring. Nothing should come
                    // between them but the game's air, so a steady gap here is a thing to put right.
                    if (_launchTo != Vector3.Zero)
                    {
                        var aim = Flat3D(_launchTo - _launchFrom);
                        aim *= 1f / Math.Max(0.01f, aim.Length());
                        var gap = Flat3D(at - _launchTo);

                        Log.Info("Hoops: the ball came down " + (Flat(gap) * 100f).ToString("0") + " cm from the line's ring (" +
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
            Log.Info("Hoops: " + head + " through hoop " + (hoop + 1) + " from " + from.ToString("0.0") + " m, " +
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

                    Log.Info("Hoops: missed hoop " + (_hoop + 1) + ", down through its height " +
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

        /// <summary>The rings where they were placed: Michael's own from the ini, and RingAt for everybody else.</summary>
        private void LoadRings()
        {
            for (var i = 0; i < _ring.Length; i++)
            {
                _ring[i] = RingAt[i];
                _ringSize[i] = RingWide[i];

                try
                {
                    var parts = Settings.Read("Hoops", "Ring" + (i + 1), "").Split(',');
                    if (parts.Length != 4) continue;

                    var f = new float[4];
                    var ok = true;

                    for (var k = 0; k < 4 && ok; k++)
                        ok = float.TryParse(parts[k].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out f[k]);

                    if (!ok) continue;

                    _ring[i] = new Vector3(f[0], f[1], f[2]);
                    _ringSize[i] = Clamp(f[3], 0.08f, 1.2f);
                }
                catch { }
            }
        }

        private static string Pack(Vector3 c, float size)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0.000},{1:0.000},{2:0.000},{3:0.000}", c.X, c.Y, c.Z, size);
        }

        /// <summary>The ring placer up, on the ring of the hoop he is looking at.</summary>
        private void StartRing(Ped me)
        {
            _ringPlacing = true;
            _ringWho = _hoop;
            _ringWas = _ring[_hoop];
            _ringWasSize = _ringSize[_hoop];
            _ringWait = true;

            float ground;
            _ringFloor = Ground.Probe(me.Position + Vector3.WorldUp * 0.5f, out ground) ? ground : me.Position.Z - 1f;

            Sound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        /// <summary>
        /// THE RING PLACER, for the developer tools: a flat ring on the hoop he is looking at, slid
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
                Settings.Put("Hoops", "Ring" + (i + 1), text);
                Log.Info("Hoops: hoop " + (i + 1) + "'s ring placed -- [Hoops] Ring" + (i + 1) + "=" + text +
                         " (x, y, z, and from the middle to the edge).");

                Sound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                return;
            }

            if (JustPressed(Control.PhoneCancel))
            {
                _ringPlacing = false;
                _ring[i] = _ringWas;
                _ringSize[i] = _ringWasSize;
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
            // The placers have the screen to themselves.
            if (_placing)
            {
                PlacingScreen();
                return;
            }

            if (_ringPlacing)
            {
                RingScreen();
                return;
            }

            var dist = Flat(Rim - me.Position);
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
                                   ". Hold ~INPUT_ATTACK~, and let go when the ring is on the hoop.");
            }

            Den.Bars.Draw("SCORE", _score.ToString(), Gold,
                          "STREAK", _streak.ToString(), _streak > 1 ? Green : Color.White,
                          "MADE", _made + " / " + _shots, Color.White);

            if (_mode == Mode.Holding)
            {
                if (_dev)
                    Den.Buttons.Show(Control.Attack, "Hold to shoot",
                                     Control.PhoneCancel, "Put the ball down",
                                     Control.PhoneRight, "Place the hoop",
                                     Control.PhoneDown, "Place the ball",
                                     Control.PhoneLeft, "Shot: " + Shot.Name);
                else
                    Den.Buttons.Show(Control.Attack, "Hold to shoot",
                                     Control.PhoneCancel, "Put the ball down");
            }

            // Where a basket has to go through, for somebody placing them.
            if (_dev) Rings(-1);

            if (_mode == Mode.Holding || _mode == Mode.Charging) Line(me);
            if (_mode == Mode.Charging) Meter();
        }

        /// <summary>
        /// STREET GOLF'S AIM LINE, and only that: the flight worked out the way the shot will go,
        /// out of the ball in his hand, drawn as a thin ribbon turned to the camera. Holding the
        /// ball, the last shot's power along where he is looking; with the meter running, the shot
        /// as the meter stands, so it stretches and shrinks as the meter plays. It ends in a ring
        /// where the ball comes down through the hoop's height: on the hoop, and it goes in.
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

                Vector3 end;
                bool down;
                var pts = Fly(from, v, me.Position.Z - 1f, out end, out down);

                // Street Golf's own inks: cool while he lines it up, warm while the meter runs.
                var ink = charging ? Color.FromArgb(70, 255, 220, 120) : Color.FromArgb(45, 190, 235, 200);

                // No more than fifty pieces, however far the throw: a heave down the street is a
                // hundred steps of the sum.
                var stride = Math.Max(1, (pts.Count + 49) / 50);

                for (var i = 0; i + 1 < pts.Count; i += stride)
                    Segment(pts[i], pts[Math.Min(i + stride, pts.Count - 1)], 0.03f, ink, eye);

                if (down)
                {
                    Function.Call(Hash.DRAW_MARKER, 25, end.X, end.Y, end.Z + 0.02f,
                                  0f, 0f, 0f, 0f, 0f, 0f, 0.45f, 0.45f, 0.45f,
                                  255, 225, 140, 90, false, false, 2, false, 0, 0, false);
                }
            }
            catch
            {
                // No line this frame.
            }
        }

        /// <summary>
        /// The shot's clip on his arms: played at its style's pace, and for the set shot stopped once
        /// the ball has gone and the arms have made their one push. Raise the Roof pumps twice.
        /// </summary>
        private void ShotClip(Ped me)
        {
            if (!_clipLive) return;

            try
            {
                var s = Styles[_shotStyle];

                if (!Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM, me.Handle, s.Dict, s.Clip, 3))
                {
                    _clipLive = false;
                    return;
                }

                Function.Call(Hash.SET_ENTITY_ANIM_SPEED, me.Handle, s.Dict, s.Clip, s.Speed);

                if (s.Cut > 0f && _released &&
                    Function.Call<float>(Hash.GET_ENTITY_ANIM_CURRENT_TIME, me.Handle, s.Dict, s.Clip) >= s.Cut)
                {
                    Function.Call(Hash.STOP_ANIM_TASK, me.Handle, s.Dict, s.Clip, -3f);
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

        /// <summary>The positioner on screen: the six values, the one being moved in gold, and the buttons.</summary>
        private void PlacingScreen()
        {
            Help.ShowThisFrame("Placing the ball. D-pad up and down picks, left and right moves it, X for bigger steps, A when it sits right.");

            var rows = new object[GripNames.Length * 3];

            for (var i = 0; i < GripNames.Length; i++)
            {
                // Bottom up, so the first is at the top.
                var at = (GripNames.Length - 1 - i) * 3;
                rows[at] = GripNames[i].ToUpperInvariant();
                rows[at + 1] = _grip[i].ToString(i < 3 ? "0.0" : "0") + (i < 3 ? " cm" : " deg");
                rows[at + 2] = i == _placeRow ? Gold : Color.White;
            }

            Den.Bars.Draw(rows);

            Den.Buttons.Show(Control.PhoneSelect, "Done",
                             Control.PhoneRight, "Move it",
                             Control.PhoneDown, "Which",
                             Control.Jump, "Bigger steps");
        }

        /// <summary>The ring placer on screen: the ring, bright, and how high and how wide it is.</summary>
        private void RingScreen()
        {
            var i = _ringWho;

            Help.ShowThisFrame("Hoop " + (i + 1) + "'s ring: the ball has to come down through it. Left stick slides it, " +
                               "D-pad up and down raises it, D-pad left and right sizes it, X for faster.");

            Den.Bars.Draw("HEIGHT", (_ring[i].Z - _ringFloor).ToString("0.00") + " m", Color.White,
                          "ACROSS", (_ringSize[i] * 200f).ToString("0") + " cm", Color.White,
                          "HOOP", (i + 1).ToString(), Gold);

            Den.Buttons.Show(Control.PhoneSelect, "Keep it",
                             Control.PhoneCancel, "Put it back");

            Rings(i);
        }

        /// <summary>Both rings, for somebody with the developer tools: the one being placed bright, the other faint.</summary>
        private void Rings(int bright)
        {
            for (var i = 0; i < _ring.Length; i++)
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
        /// Street Golf's meter: sixteen cells filling amber, a white tick where the power is, and
        /// under it how far out the ring is. No green on it: where the hoop is, is his to judge.
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

            UI.Draw.Rect(left + width * 0.5f, y, width + 0.012f, h + 0.012f, Color.FromArgb(150, 0, 0, 0));

            for (var c = 0; c < cells; c++)
            {
                var f0 = c / (float)cells;
                var x = left + c * (cw + gap) + cw * 0.5f;

                UI.Draw.Rect(x, y, cw, h, _power > f0 ? Gold : Track);
            }

            UI.Draw.Rect(left + width * Clamp(_power, 0f, 1f), y, 0.002f, h + 0.012f, Color.White);
            UI.Draw.Text(Reach(_power).ToString("0.0") + " m", 0.5f, y + 0.014f, 0.35f, Color.White, UI.Draw.FontBody, true, true, false);
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
