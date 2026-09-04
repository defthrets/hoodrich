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

        /// <summary>
        /// How many dabs one tick may place.
        ///
        /// The ceiling on catching up. Four at sixty frames a second is 240 marks a second
        /// standing still, which is already more than the decal pool holds -- past this the
        /// only thing a higher rate buys is recycling your own paint faster.
        /// </summary>
        private const int MaxDabsPerTick = 4;

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

                // And it ends the dwell. Coming back to the same wall later starts the clock
                // again -- a drip is what one long press does, not what a spot remembers.
                _dwelling = false;
                _running = false;

                // A stroke does not continue across a lifted trigger either.
                _streaking = false;
            }

            Spraying = spraying;

            if (!spraying) return;

            // Every tick, because he can keep looking around with the trigger held.
            Steer();

            var now = Game.GameTime;
            if (now < _nextDab) return;

            // Rate rather than every frame. A dab per frame at 60fps empties the decal pool in
            // about seven seconds and puts three hundred splatters inside one square metre.
            var gap = (int)(1000f / Math.Max(1f, _cfg.LiveRate));
            if (gap < 1) gap = 1;

            // MORE THAN ONE DAB A TICK WHEN THE RATE ASKS FOR IT, which it now does. This ran
            // one dab per tick, so sixty frames a second was a hard ceiling of sixty marks a
            // second however high Rate was set -- turning the dial past that changed nothing
            // and said nothing, which is the worst kind of setting.
            //
            // Catching up rather than free-running: the clock advances by one gap per dab, so
            // the marks land at the spacing the rate asked for rather than in a clump.
            var dabs = 0;

            while (now >= _nextDab && dabs < MaxDabsPerTick)
            {
                Dab();

                _nextDab += gap;
                dabs++;
            }

            // A frame that took a long time -- a load, an alt-tab -- must not leave the clock
            // owing hundreds of dabs that then arrive over the following seconds.
            if (_nextDab < now) _nextDab = now + gap;
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
        private void Put(Vector3 at, Vector3 into, Vector3 side, float size, int hit)
        {
            // Which texture this tool wants. Set here rather than once per session because the
            // player can swap tools between one mark and the next.
            _marks.Wanted = _cfg.SprayCanLook ? _cfg.CanDecal : 0;

            var c = Shade();

            // BAKED IN HERE, not applied at placing time. The mark keeps the final multipliers,
            // so restoring one after a save replays exactly what went on the wall rather than
            // driving an already-driven colour a second time.
            var gain = _cfg.SprayCanLook ? _cfg.CanColourGain : 1f;

            _marks.Put(at, into, side, size,
                       c.R / 255f * gain, c.G / 255f * gain, c.B / 255f * gain, hit);
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
                    // FLOORED AGAINST THE MARK, not against two centimetres.
                    //
                    // This was Math.Max(0.02f, ...) and that fixed floor quietly ate the whole
                    // point of CanDensity. At density 3 the step wants to be 7.3mm and the
                    // floor forced it to 20mm -- 2.7 times wider -- so below 1.3 m/s of sweep
                    // the fill placed nothing at all and every mark was a dab. Two of the
                    // three things CanDensity moves were working and the third was not.
                    //
                    // A tenth of the mark is a real floor: it stops a runaway on a tiny mark
                    // without overriding what the setting asked for at any normal size.
                    var step = Math.Max(size * 0.1f, size * _cfg.LiveOverlap);

                    var fill = (int)(gap / step);
                    if (fill > _cfg.LiveMaxFill) fill = _cfg.LiveMaxFill;

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

                        Put(mid, into, midSide, midSize, hit.Entity);
                    }
                }
            }

            // ---- one stretched decal instead of a run of round ones ----
            //
            // The saving only exists if a streak REPLACES the dabs rather than joining them, so
            // this returns without laying the round mark below when it lays one.
            //
            // It waits for the reticle to travel far enough to be worth stretching. A tick is
            // 15ms and the hand moves a millimetre or two in that time, so placing a streak per
            // tick would be a chain of round-ish stamps again and cost exactly what it did
            // before. StreakIdleMs is the floor underneath that: stand still and the reticle
            // never travels, so without it holding the trigger on a wall would paint nothing.
            if (_cfg.StrokeStreaks && Streaked(hit, at, into, size))
            {
                Running(hit, at, into, size);

                _lastAt = at;
                _lastNormal = hit.Normal;
                _hasLast = true;
                return;
            }

            Put(at, into, side, size, hit.Entity);
            _paintedAt = Game.GameTime;

            Running(hit, at, into, size);

            _lastAt = at;
            _lastNormal = hit.Normal;
            _hasLast = true;
        }

        /// <summary>
        /// Lays the stroke as one stretched decal, or says it did not.
        ///
        /// Returns true when it put something down, in which case the caller must NOT also lay
        /// its round mark -- that is the entire saving and it is easy to lose.
        /// </summary>
        private bool Streaked(Hit hit, Vector3 at, Vector3 into, float size)
        {
            var now = Game.GameTime;

            // A new stroke: nothing to stretch from yet.
            if (!_streaking || !_hasLast || Vector3.Dot(_lastNormal, hit.Normal) < 0.94f)
            {
                _streaking = true;
                _streakFrom = at;
                _streakDir = Vector3.Zero;
                _streakBegan = now;

                return false;
            }

            var span = _streakFrom.DistanceTo(at);

            // Not far enough to be worth stretching. Let the round mark happen -- but only if
            // one is due, or a slow hand puts down sixty a second and saves nothing.
            if (span < size * _cfg.StreakStep)
            {
                return now - _paintedAt < _cfg.StreakIdleMs;
            }

            // ---- LONG ENOUGH TO LAY, BUT NOT NECESSARILY YET ----
            //
            // This used to lay here, every time, which made StreakStep the length of every
            // streak in the mod rather than the shortest one. Half a metre of dead straight
            // line down a shutter came out as seven separate decals laid end to end, all of
            // them the same colour, all of them collinear -- seven things in a pool of two
            // thousand doing the work of one.
            //
            // So the streak keeps growing while it is still honest to draw it as one quad: it
            // is straight, it is not overlong, and it has not been holding paint back long
            // enough to feel like lag. The FIRST of those is the real one -- see StreakBend.
            // The other two are ceilings so nothing can grow forever.
            //
            // The direction is taken once, here, at the first point far enough away to have a
            // reliable one. Taken per tick it would be the direction of a millimetre of hand
            // movement, which is noise.
            if (_streakDir.LengthSquared() < 0.5f)
            {
                _streakDir = at - _streakFrom;
                _streakDir.Normalize();
            }
            else if (span < size * _cfg.StreakLongest &&
                     now - _streakBegan < _cfg.StreakHoldMs &&
                     !Bent(at, size))
            {
                // Still growing. Nothing is drawn and -- this is the point -- nothing is
                // dabbed either, so the whole run stays one decal.
                return true;
            }

            // A flick across a courtyard is not a stroke, and joining those two points draws a
            // line through open air. Same guard the round fill has always had.
            if (span > 6f)
            {
                _streakFrom = at;
                return false;
            }

            var along = at - _streakFrom;
            along.Normalize();

            var across = Vector3.Cross(hit.Normal, along);

            if (across.LengthSquared() < 0.0001f)
            {
                _streakFrom = at;
                return false;
            }

            across.Normalize();

            var c = Shade();

            // Half a mark longer than the gap it covers, so consecutive streaks overlap at their
            // ends instead of meeting exactly and leaving a seam at every join.
            var wide = size;
            var tall = span + size * 0.5f;

            if (_cfg.StrokeSideways)
            {
                var swap = wide;
                wide = tall;
                tall = swap;
            }

            _marks.Streak(_streakFrom + (at - _streakFrom) * 0.5f, into,
                          _cfg.StrokeSideways ? along : across,
                          wide, tall,
                          c.R / 255f, c.G / 255f, c.B / 255f, hit.Entity);

            _streakFrom = at;
            _streakDir = Vector3.Zero;
            _streakBegan = now;
            _paintedAt = now;

            return true;
        }

        /// <summary>
        /// Whether the stroke has bent far enough that one straight quad would lie about it.
        ///
        /// MEASURES THE ERROR ITSELF RATHER THAN AN ANGLE. A quad drawn from the start of the
        /// streak along the direction it set off in is a straight line; the hand is wherever it
        /// is. The distance between the two is exactly what stretching would get wrong, in
        /// metres, and comparing THAT to the size of a mark answers the only question that
        /// matters -- would anybody see it.
        ///
        /// An angle would not: ten degrees is nothing over five centimetres and a visible
        /// corner over a metre, so a fixed angle would cut short strokes that were fine and let
        /// long ones cut the corners off letters.
        /// </summary>
        private bool Bent(Vector3 at, float size)
        {
            var off = at - _streakFrom;

            // Along the line, then what is left over -- which is the sideways miss.
            var down = Vector3.Dot(off, _streakDir);
            var side = (off - _streakDir * down).Length();

            return side > size * _cfg.StreakBend;
        }

        /// <summary>
        /// Too much paint in one place, and what it does about it.
        ///
        /// A drip is not decoration here -- it is the only thing in the whole engine that
        /// punishes holding the trigger, and holding the trigger is otherwise free. Sweeping
        /// gives a clean line and sitting still gives a run, which is the same bargain a real
        /// can offers.
        ///
        /// NOT ON FLOORS AND NOT ON CEILINGS. Down-the-surface is world-down with the part
        /// facing out of the wall taken off it, so on anything close to level there is nothing
        /// left of it and no drip starts. That is correct rather than a special case: paint
        /// does not run down a pavement.
        /// </summary>
        private void Running(Hit hit, Vector3 at, Vector3 into, float size)
        {
            if (!_cfg.Drips) return;

            var now = Game.GameTime;

            // Moved on: this is a new spot, and whatever was running from the old one stops
            // where it got to rather than following the reticle across the wall.
            if (!_dwelling || _dwellAt.DistanceToSquared(at) > _cfg.DripArea * _cfg.DripArea)
            {
                _dwelling = true;
                _dwellAt = at;
                _dwellFrom = now;
                _runsHere = 0;
                _running = false;

                return;
            }

            if (_running)
            {
                Creep(now);
                return;
            }

            if (now - _dwellFrom < _cfg.DripAfterMs) return;
            if (_runsHere >= _cfg.DripRuns) return;

            // Straight down the face of whatever was hit.
            var n = hit.Normal;
            var down = new Vector3(0f, 0f, -1f);

            down = down - n * Vector3.Dot(down, n);

            if (down.LengthSquared() < 0.04f) return;   // level enough that nothing would run

            down.Normalize();

            // Started a little off centre, and somewhere different each time, so three runs off
            // one spot are three runs rather than one drawn three times.
            // Across the wall, square to the way the drip will run. This is the decal's
            // side vector, and the long axis comes out perpendicular to it.
            var across = Vector3.Cross(n, down);

            if (across.LengthSquared() < 0.0001f) return;

            across.Normalize();

            _runAcross = across;
            _runAt = at + across * (float)((_rng.NextDouble() - 0.5) * size * 1.2);
            _runDown = down;
            _runInto = into;
            _runNormal = n;
            _runEntity = hit.Entity;
            _runLen = 0f;
            _runSize = size * _cfg.DripWidth;
            _nextRunStep = now;
            _runMark = null;
            _running = true;
            _runsHere++;
        }

        /// <summary>
        /// One drip, crawling.
        ///
        /// ONE DECAL, STRETCHED, not a row of round ones. The first version marched splatters
        /// down the wall and they read as a dotted line however much they were made to overlap
        /// -- because a splatter is not a disc, it is mostly transparent speckle, and two of
        /// them on top of each other is more speckle rather than a solid mark. The main strokes
        /// only look solid because the line fill puts one down every 3.7mm for a 55mm mark,
        /// which is ninety-three percent overlap and forty times the cost.
        ///
        /// ADD_DECAL takes a width AND a height, and every mark in this engine handed it the
        /// same number twice -- which is why a decal was only ever a blob. Told two different
        /// numbers it draws a streak, and a streak is a drip: one decal for the whole run
        /// instead of forty, so it is both solid and far cheaper.
        ///
        /// It still arrives rather than appearing. The same decal is taken down and put back
        /// longer about ten times over, so the run visibly travels down the wall.
        /// </summary>
        private void Creep(int now)
        {
            if (now < _nextRunStep) return;

            _nextRunStep = now + _cfg.DripStepMs;

            // A tenth of the run per step, so it arrives in about ten of them however long the
            // run is set to be. The old version stepped by a mark width, which tied how fast a
            // drip travelled to how wide it was.
            _runLen += Math.Max(0.006f, _cfg.DripLength * 0.1f);

            if (_runLen >= _cfg.DripLength)
            {
                _runLen = _cfg.DripLength;
                _running = false;
            }

            // Centred on the run. A decal is drawn AROUND its point rather than from it, so a
            // streak that starts at the spray has to sit half its own length below it.
            var spot = _runAt + _runDown * (_runLen * 0.5f);

            var wide = _runSize;
            var tall = _runLen;

            if (_cfg.DripSideways)
            {
                var swap = wide;
                wide = tall;
                tall = swap;
            }

            var side = _cfg.DripSideways ? _runDown : _runAcross;

            if (_runMark == null)
            {
                var c = Shade();

                _runMark = _marks.Streak(spot, _runInto, side, wide, tall,
                                         c.R / 255f, c.G / 255f, c.B / 255f, _runEntity);

                if (_runMark == null) _running = false;

                return;
            }

            _marks.Restreak(_runMark, spot, side, tall);
        }

        // Where the streak being laid started, and when anything was last put down. See Dab.
        private Vector3 _streakFrom;
        private Vector3 _streakDir;
        private int _streakBegan;
        private bool _streaking;
        private int _paintedAt;

        // ---- running paint ----
        //
        // Two things are being tracked and they are not the same thing. The DWELL is how long
        // the spray has been on one spot, which is what decides that there is too much paint
        // there. The RUN is one drip crawling down from it, which has its own life and keeps
        // going for as long as it has left even while the dwell continues.
        private Vector3 _dwellAt;
        private int _dwellFrom;
        private bool _dwelling;
        private int _runsHere;

        private bool _running;
        private Vector3 _runAt, _runDown, _runInto, _runNormal, _runAcross;
        private float _runLen, _runSize;
        private int _nextRunStep;
        private int _runEntity;

        /// <summary>The one decal this drip is, while it is still growing.</summary>
        private Mark _runMark;

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
                                r, g, b, 0.95f, _cfg.LiveJetScale, out _liveJet);

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
