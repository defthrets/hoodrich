using System;
using System.Drawing;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Control = GTA.Control;

namespace Hoodrich.Paint
{
    /// <summary>
    /// Holding the trigger on a fire extinguisher and getting paint.
    ///
    /// The whole mod is this: while the extinguisher is out and the trigger is down, probe
    /// forward, and put a splatter on whatever the probe found. Everything else -- the pool,
    /// the picker, the plume -- exists to make that read as spraying rather than as decals
    /// appearing.
    ///
    /// GATED ON THE CONTROL, NOT ON IS_PED_SHOOTING. The extinguisher is a weapon by the game's
    /// reckoning but a strange one, and whether it counts as "shooting" is not something to
    /// find out from a mod that has already shipped. Reading the attack control is true
    /// whatever the answer, and it is also true on the frame the trigger goes down rather than
    /// once the plume has spun up.
    /// </summary>
    internal sealed class Sprayer
    {
        private const string Extinguisher = "WEAPON_FIREEXTINGUISHER";

        /// <summary>
        /// The plume, tinted.
        ///
        /// scr_lamgraff_paint_spray IS ROCKSTAR OWN PAINT SPRAY. It is the effect their
        /// graffiti scene uses -- player_scene_f_lamgraff starts it looped and then calls
        /// SET_PARTICLE_FX_LOOPED_COLOUR on it, which is exactly what this mod wants to do and
        /// is proof it takes an arbitrary colour rather than merely tolerating one.
        ///
        /// Found by pulling every ptfx name out of the decompiled script set rather than
        /// guessed, which matters more here than usual: a particle name a build does not have
        /// plays as silence, and silence is indistinguishable from the feature not working.
        ///
        /// THE FIRE TRUCK WATER JET IS NOT IN THAT LIST. Nothing in the entire decompiled set
        /// names a water-cannon effect, which is what you would expect of something driven by a
        /// vehicle weapon rather than by a script -- there is no name for a script to ask for.
        /// A paint spray is the better answer regardless: right shape, already built to be told
        /// what colour to be, and it is what the game itself reaches for when somebody paints
        /// a wall.
        ///
        /// A SECOND EFFECT RIDES ALONGSIDE IT ON THE EXTINGUISHER: a tinted cloud, because an
        /// extinguisher discharges a volume and not a thin line, and the jet on its own reads
        /// as a laser pointer. The can does not get one -- a spray can does not billow, and
        /// giving both the same plume was what made the two looks feel like one tool in a hat.
        /// </summary>
        private sealed class Plume
        {
            /// <summary>The named asset holding it. "core" is a named asset like any other.</summary>
            public readonly string Asset;

            public readonly string Name;
            public readonly float Size;

            /// <summary>
            /// Whether it hangs off the can or off the man.
            ///
            /// Rockstar's paint jet is authored around a can seated on PH_R_Hand, so on the
            /// can at zero offset it points at the wall by itself.
            ///
            /// The core jets are not authored around anything, and a BONE is the wrong thing
            /// to hang them off: a bone's local axes are its own and point wherever the
            /// skeleton happens to face, so aiming off one is guesswork. That guess is exactly
            /// what sent the spray out sideways across his shoulder. A ped's axes are not a
            /// guess -- +Y is the way he is looking -- so they go on the man.
            /// </summary>
            public readonly bool OnCan;

            public readonly float X, Y, Z, Pitch;

            public Plume(string asset, string name, float size, bool onCan = false,
                         float x = 0f, float y = 0f, float z = 0f, float pitch = 0f)
            {
                Asset = asset;
                Name = name;
                Size = size;
                OnCan = onCan;
                X = x;
                Y = y;
                Z = z;
                Pitch = pitch;
            }

            public override string ToString()
            {
                return Name + " out of " + Asset;
            }
        }

        /// <summary>Where a ped-mounted jet sits: out at his right hand, forward, shoulder high.</summary>
        private const float Right = 0.20f;
        private const float Forward = 0.42f;
        private const float Up = 0.48f;

        /// <summary>
        /// Ninety degrees off vertical, which is forward.
        ///
        /// These effects emit UPWARDS by default -- they are authored for hydrants and hoses --
        /// so without this the spray leaves over his head while the paint lands on the wall in
        /// front of him, and the two disagreeing is what reads as broken.
        /// </summary>
        private const float Level = -90f;

        /// <summary>
        /// The can's spray: Rockstar's own paint jet, on the can, AND NOTHING BEHIND IT.
        ///
        /// No fallback on purpose. Every other effect available here is steam, water or an
        /// extinguisher discharge -- a cloud -- and a cloud out of a six-inch can is worse than
        /// no effect at all: it is absurd on its face, and it hides the wall you are aiming at.
        /// So if the paint jet will not start, the can sprays invisibly and the paint still
        /// lands. That is a deliberate choice and the log says which happened.
        /// </summary>
        private static readonly Plume[] CanJets =
        {
            new Plume("scr_playerlamgraff", "scr_lamgraff_paint_spray", 1f, onCan: true)
        };

        /// <summary>The extinguisher's discharge, on the man, pointed where he is looking.</summary>
        private static readonly Plume[] HoseJets =
        {
            new Plume("core", "ent_sht_extinguisher", 0.8f, x: Right, y: Forward, z: Up, pitch: Level),
            new Plume("core", "ent_sht_water", 0.7f, x: Right, y: Forward, z: Up, pitch: Level),
            new Plume("core", "ent_sht_steam", 0.6f, x: Right, y: Forward, z: Up, pitch: Level)
        };

        /// <summary>The wider cloud around it. Extinguisher only, for the same reason.</summary>
        private static readonly Plume[] Clouds =
        {
            new Plume("core", "ent_sht_steam", 1.8f, x: Right, y: Forward, z: Up, pitch: Level),
            new Plume("core", "ent_sht_water", 1.6f, x: Right, y: Forward, z: Up, pitch: Level)
        };

        private readonly PaintConfig _cfg;
        private readonly Marks _marks;

        private int _nextDab;
        private int _fx = -1;
        private int _cloud = -1;
        private bool _wasSpraying;

        public Sprayer(PaintConfig cfg, Marks marks)
        {
            _cfg = cfg;
            _marks = marks;
        }

        /// <summary>What the picker last set.</summary>
        public Color Colour = Color.FromArgb(255, 40, 200, 90);

        /// <summary>
        /// How far each mark's shade is allowed to wander either side of Colour. 0 is flat
        /// paint, which is what almost everything is.
        ///
        /// Set alongside Colour by whoever owns the picker, and left at zero by anything that
        /// forces a colour for its own reasons -- a gang's tag is a gang's colour, not a
        /// shimmering approximation of one.
        /// </summary>
        public float Sheen;

        /// <summary>
        /// The picker's dial, as a MULTIPLIER rather than a width.
        ///
        /// The width itself comes from how far away the wall is -- see Dab -- so a fixed
        /// number here would fight the thing that makes it read as spray. This is nozzle
        /// pressure: same plume, more or less of it.
        /// </summary>
        public float Scale = 1f;

        /// <summary>
        /// The visible thing the plume comes out of, set from outside each tick.
        ///
        /// 0 means there is not one, and the hand is used instead.
        /// </summary>
        public int Nozzle;

        /// <summary>True while paint is actually coming out, for anything that wants to know.</summary>
        public bool Spraying { get; private set; }

        public void Update()
        {
            var spraying = Holding();

            if (spraying != _wasSpraying)
            {
                _wasSpraying = spraying;

                if (spraying) StartPlume();
                else StopPlume();

                // Lifting the trigger breaks the line. Without this, letting go, walking
                // across the street and pressing again draws a stroke between the two.
                _hasLast = false;
            }

            Spraying = spraying;

            if (!spraying) return;

            // Every tick, because he can keep looking around with the trigger held.
            Steer();

            var now = Game.GameTime;
            if (now < _nextDab) return;

            // Rate rather than every frame. A dab per frame at 60fps empties the decal pool in
            // about seven seconds and puts three hundred splatters inside one square metre.
            _nextDab = now + (int)(1000f / Math.Max(1f, _cfg.Rate));

            Dab();
        }

        /// <summary>Extinguisher out, trigger down.</summary>
        private bool Holding()
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists() || !me.IsAlive) return false;

                var want = Function.Call<uint>(Hash.GET_HASH_KEY, Extinguisher);
                var got = Function.Call<uint>(Hash.GET_SELECTED_PED_WEAPON, me.Handle);

                if (got != want) return false;
                if (!_cfg.PaintEnabled || !_cfg.Armed) return false;

                return Game.IsControlPressed(Control.Attack) ||
                       Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)Control.Attack);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// A size multiplier around 1, by however much SizeJitter allows.
        ///
        /// One place rather than two. The trail marks and the dab were rolling their own
        /// identical expression, which is exactly the shape of thing that gets tuned in one
        /// spot and not the other.
        /// </summary>
        private float Vary()
        {
            var j = _cfg.SizeJitter;

            return 1f - j + (float)_rng.NextDouble() * j * 2f;
        }

        /// <summary>
        /// Lays one mark down in whatever shade it happens to come out.
        ///
        /// Every mark goes through here rather than the caller working a colour out once and
        /// reusing it, which is what used to happen -- and it is why a metallic could not have
        /// worked before: the dab and the whole trail behind it shared one r,g,b, so all of
        /// them were the same shade by construction.
        /// </summary>
        private void Put(Vector3 at, Vector3 into, Vector3 side, float size)
        {
            // Which texture this tool wants. Set here rather than once per session because the
            // player can swap tools between one mark and the next.
            _marks.Wanted = _cfg.SprayCanLook ? _cfg.CanDecal : 0;

            var c = Shade();

            _marks.Put(at, into, side, size, c.R / 255f, c.G / 255f, c.B / 255f);
        }

        /// <summary>
        /// The shade this one mark lands in.
        ///
        /// Flat paint is one colour and returns it, which is the case that matters and is the
        /// first line. A metallic scatters: see Rack for why that scatter is the only thing
        /// that can make chrome read as chrome on a surface the game will not let anything
        /// reflect off.
        ///
        /// Uniform across the range, not clustered in the middle. A bell curve would put most
        /// marks near the base colour and make the extremes rare -- which is a slightly dirty
        /// flat colour, not metal. Metal wants the light and the dark ones to be as common as
        /// the mid ones.
        /// </summary>
        private Color Shade()
        {
            var sheen = Sheen * _cfg.MetallicShine;

            if (sheen <= 0f) return Colour;

            return Rack.Lit(Colour, (float)(_rng.NextDouble() * 2.0 - 1.0) * sheen);
        }

        /// <summary>Where the last splatter landed, so the gap to this one can be filled.</summary>
        private Vector3 _lastAt;
        private Vector3 _lastNormal;
        private bool _hasLast;

        /// <summary>One splatter, wherever the spray is pointing -- and the trail behind it.</summary>
        private void Dab()
        {
            var hit = Surface.InFront(_cfg.LiveRange);

            if (!hit.Landed)
            {
                // Off the end of a wall. The next hit must not draw a line across the gap from
                // the last thing that WAS on one.
                _hasLast = false;
                return;
            }

            // OFF THE SURFACE BY A HAIR. A decal placed exactly on the geometry fights it for
            // the same pixels and flickers -- z-fighting, and at spray rates it flickers a
            // hundred times a second across a whole wall.
            var at = hit.At + hit.Normal * 0.02f;

            // Into the wall, which is the opposite of the way it faces.
            var into = -hit.Normal;
            // A DIFFERENT WAY UP EVERY TIME. The full circle, not a nudge -- a splatter has
            // no correct orientation, so there is nothing to stay near, and anything less than
            // the whole turn leaves the repeat visible.
            var side = Surface.Along(hit.Normal, (float)(_rng.NextDouble() * Math.PI * 2.0));

            // THE FURTHER THE WALL, THE WIDER THE SPLATTER, and this is what turns a stream of
            // decals into a spray. Up close it is a tight dot you can write with; at arm's
            // reach and beyond it blooms into something you cover a garage door with.
            //
            // Quadratic rather than a cone. A cone is what a spray geometrically is and it is
            // not what one looks like -- the plume holds together while it has pressure and
            // opens out as it loses it, so the far half widens faster than the near half.
            var away = hit.Away;

            var size = _cfg.LiveSizeAtOneMetre *
                       (float)Math.Pow(Math.Max(0.2f, away), _cfg.LiveSpreadPower) *
                       Scale;

            // A little variation, or a held trigger paints one splatter repeatedly in place and
            // reads as a decal rather than as spray.
            size *= Vary();

            if (size < _cfg.LiveMinSize) size = _cfg.LiveMinSize;
            if (size > _cfg.LiveMaxSize) size = _cfg.LiveMaxSize;

            // ---- the trail between the last one and this one ----
            //
            // Only along a surface that is still FACING THE SAME WAY. Sweeping round a corner
            // puts two hits on two walls, and a straight line drawn between them runs through
            // open air -- so the marks would hang in space where nothing is.
            if (_cfg.Continuous && _hasLast)
            {
                var gap = _lastAt.DistanceTo(at);

                var samewall = Vector3.Dot(_lastNormal, hit.Normal) > 0.94f;

                // And only across a sane distance. Two hits a long way apart is a flick across
                // a courtyard, not a stroke, and joining those is a line nobody drew.
                if (samewall && gap > 0.001f && gap < 6f)
                {
                    var step = Math.Max(0.02f, size * _cfg.Overlap);

                    var fill = (int)(gap / step);
                    if (fill > _cfg.MaxFill) fill = _cfg.MaxFill;

                    for (var i = 1; i <= fill; i++)
                    {
                        var t = (float)i / (fill + 1);

                        // Its own roll and its own size, exactly like a real one. Marching a
                        // single stamp along the path is what makes a trail read as a printed
                        // repeat rather than as paint.
                        var mid = _lastAt + (at - _lastAt) * t;

                        var midSide = Surface.Along(hit.Normal,
                                                    (float)(_rng.NextDouble() * Math.PI * 2.0));

                        var midSize = size * Vary();

                        if (midSize < _cfg.LiveMinSize) midSize = _cfg.LiveMinSize;
                        if (midSize > _cfg.LiveMaxSize) midSize = _cfg.LiveMaxSize;

                        Put(mid, into, midSide, midSize);
                    }
                }
            }

            Put(at, into, side, size);

            _lastAt = at;
            _lastNormal = hit.Normal;
            _hasLast = true;
        }

        private readonly Random _rng = new Random();

        // ---- the plume ---------------------------------------------------------

        /// <summary>Which candidate took last, only so a change is worth one log line.</summary>
        private int _jetPick = -1;
        private int _hosePick = -1;
        private int _cloudPick = -1;

        /// <summary>What is actually running, so Steer knows where to point it.</summary>
        private Plume _liveJet;
        private Plume _liveCloud;

        private void StartPlume()
        {
            if (!_cfg.ColourTheSmoke) return;

            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return;

                var r = Colour.R / 255f;
                var g = Colour.G / 255f;
                var b = Colour.B / 255f;

                if (_cfg.SprayCanLook)
                {
                    // A can. One thin jet off the can itself, no cloud, nothing else.
                    _fx = Light(CanJets, ref _jetPick, Nozzle, me.Handle,
                                r, g, b, 0.95f, _cfg.CanJetScale, out _liveJet);

                    _cloud = -1;
                    _liveCloud = null;
                }
                else
                {
                    // A pressure vessel. It discharges a volume, so it gets both.
                    _fx = Light(HoseJets, ref _hosePick, 0, me.Handle,
                                r, g, b, 0.90f, _cfg.JetScale, out _liveJet);

                    _cloud = Light(Clouds, ref _cloudPick, 0, me.Handle,
                                   r, g, b, 0.45f, _cfg.JetScale, out _liveCloud);
                }

                Steer();
                Moan();
            }
            catch (Exception ex)
            {
                _fx = -1;
                _cloud = -1;
                Log.Debug("No coloured plume: " + ex.Message);
            }
        }

        /// <summary>
        /// Tilts the jet with the camera, every tick it is running.
        ///
        /// The PAINT never needed this -- it comes off a ray from the camera and has always
        /// gone exactly where the reticle is. This is the spray agreeing with it, which matters
        /// more than decoration: a jet visibly leaving at one angle while marks appear at
        /// another reads as the paint being broken rather than the effect being cosmetic.
        ///
        /// Yaw is not touched, because he already turns to face the aim. Only the tilt is left,
        /// and only for the ped-mounted ones -- Rockstar's can jet is authored at a fixed
        /// rotation and nudging it is how you get paint coming out sideways.
        /// </summary>
        private void Steer()
        {
            if (!_cfg.JetFollowsAim) return;

            try
            {
                var pitch = GameplayCamera.Rotation.X;

                if (_fx != -1 && _liveJet != null && !_liveJet.OnCan)
                {
                    Function.Call(Hash.SET_PARTICLE_FX_LOOPED_OFFSETS, _fx,
                                  _liveJet.X, _liveJet.Y, _liveJet.Z,
                                  _liveJet.Pitch + pitch, 0f, 0f);
                }

                if (_cloud != -1 && _liveCloud != null && !_liveCloud.OnCan)
                {
                    Function.Call(Hash.SET_PARTICLE_FX_LOOPED_OFFSETS, _cloud,
                                  _liveCloud.X, _liveCloud.Y, _liveCloud.Z,
                                  _liveCloud.Pitch + pitch, 0f, 0f);
                }
            }
            catch
            {
                // It keeps the angle it started at, which is level and forward.
            }
        }

        /// <summary>
        /// Whether an effect's asset is in memory, asking for it if it is not.
        ///
        /// "core" goes through the NAMED pair like anything else. It is a named asset -- the
        /// tag run in the other mod asks for it exactly this way and has for a long time.
        /// </summary>
        private static bool Ready(Plume p)
        {
            if (Function.Call<bool>(Hash.HAS_NAMED_PTFX_ASSET_LOADED, p.Asset)) return true;

            Function.Call(Hash.REQUEST_NAMED_PTFX_ASSET, p.Asset);
            return false;
        }

        /// <summary>
        /// Starts the best effect in a ladder that will actually start.
        ///
        /// ALWAYS FROM THE TOP, and that is the fix for a real bug rather than a style choice.
        /// This used to lock on to the first thing that worked and try only that one ever
        /// after -- so on the very first press, with Rockstar's paint jet still streaming in,
        /// it fell through to an extinguisher cloud and then never gave the paint jet another
        /// chance for the rest of the session. A candidate that is merely NOT READY YET is not
        /// a candidate that failed, and the difference between those two is a whole feature.
        ///
        /// The remembered index is now only used to notice a change worth logging.
        /// </summary>
        private int Light(Plume[] ladder, ref int remembered, int can, int ped,
                          float r, float g, float b, float alpha, float scale,
                          out Plume chosen)
        {
            chosen = null;

            for (var i = 0; i < ladder.Length; i++)
            {
                var p = ladder[i];

                // A can-mounted effect needs a can. Falling back to the ped would throw away
                // the authored aim that is the whole reason for putting it on the can.
                if (p.OnCan && can == 0) continue;

                if (!Ready(p)) continue;   // still streaming; try it again next press

                Function.Call(Hash.USE_PARTICLE_FX_ASSET, p.Asset);
                Function.Call(Hash.SET_PARTICLE_FX_NON_LOOPED_COLOUR, r, g, b);

                var on = p.OnCan ? can : ped;

                // A can-mounted jet takes its offset from the config, because the right value
                // is one you can only find by looking at it -- the ped-mounted ones keep the
                // fixed geometry they were tuned with.
                var ox = p.OnCan ? _cfg.CanJetSide : p.X;
                var oy = p.OnCan ? _cfg.CanJetOut : p.Y;
                var oz = p.OnCan ? _cfg.CanJetUp : p.Z;

                var fx = Function.Call<int>(Hash.START_PARTICLE_FX_LOOPED_ON_ENTITY,
                                            p.Name, on,
                                            ox, oy, oz,
                                            p.Pitch, 0f, 0f,
                                            p.Size * scale, false, false, false);

                // A HANDLE IS NOT AN EFFECT. A ptfx name the build does not have hands back a
                // handle for an effect that does not exist, so without this the ladder stops
                // at the first name that merely failed politely.
                if (fx == 0 || !Function.Call<bool>(Hash.DOES_PARTICLE_FX_LOOPED_EXIST, fx))
                {
                    continue;
                }

                Function.Call(Hash.SET_PARTICLE_FX_LOOPED_COLOUR, fx, r, g, b, false);
                Function.Call(Hash.SET_PARTICLE_FX_LOOPED_ALPHA, fx, alpha);

                if (remembered != i)
                {
                    remembered = i;
                    Log.Info("Plume: " + p + " at " + (p.Size * scale).ToString("0.00") + ".");
                }

                chosen = p;
                return fx;
            }

            return -1;
        }

        private int _dry;
        private bool _moaned;

        /// <summary>
        /// Says so, once, when the spray simply will not start.
        ///
        /// SILENCE WAS THE ACTUAL BUG HERE, more than once. A ladder failing quietly looks
        /// exactly like the feature being switched off, and a whole session went by with
        /// nothing coming out and not one line saying why.
        ///
        /// It is NOT a complaint when the can is out and Rockstar's jet has not arrived: that
        /// is the deliberate no-smoke rule doing its job, and it says so differently.
        /// </summary>
        private void Moan()
        {
            if (_fx != -1)
            {
                _dry = 0;
                _moaned = false;
                return;
            }

            // Several goes, not one. The first few legitimately fail while the asset streams,
            // and complaining about those is noise that trains you to ignore the log.
            if (++_dry < 8 || _moaned) return;

            _moaned = true;

            if (_cfg.SprayCanLook)
            {
                Log.Warn("The can is spraying invisibly: " + CanJets[0] + " will not start, " +
                         "and a can deliberately has no smoke fallback -- a cloud out of a " +
                         "spray can is worse than no effect. Paint is landing normally.");
                return;
            }

            var tried = new System.Text.StringBuilder();
            foreach (var p in HoseJets) tried.Append(p).Append("; ");

            Log.Warn("No spray effect will start after " + _dry + " goes. Tried: " + tried +
                     "Paint still lands -- this is the visible spray only.");
        }

        private void StopPlume()
        {
            Douse(ref _fx);
            Douse(ref _cloud);
        }

        private static void Douse(ref int fx)
        {
            if (fx == -1) return;

            try { Function.Call(Hash.STOP_PARTICLE_FX_LOOPED, fx, false); }
            catch { /* it stops when the asset unloads */ }

            fx = -1;
        }

        /// <summary>Everything off, for a reload.</summary>
        public void Stop()
        {
            StopPlume();
            Spraying = false;
            _wasSpraying = false;
        }
    }
}
