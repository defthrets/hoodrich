using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Paint
{
    /// <summary>One splatter, and everything needed to put it back.</summary>
    internal sealed class Mark
    {
        public Vector3 At;
        public Vector3 Into;
        public Vector3 Side;

        public float Size;

        /// <summary>
        /// How tall it is drawn, or 0 for as wide as it is.
        ///
        /// ADD_DECAL TAKES A WIDTH AND A HEIGHT and every mark until now handed it the same
        /// number twice, which is why a decal was only ever a blob. Told two different numbers
        /// it draws a streak -- and a streak is a drip, done as ONE decal instead of forty
        /// round ones chasing each other down a wall.
        /// </summary>
        public float Tall;

        public float R, G, B;

        /// <summary>
        /// Whether it landed on a vehicle rather than on the world.
        ///
        /// Not saved. A decal is placed in world space, so this one is hanging where the car
        /// was the moment it was sprayed -- putting it back next session would put it in the
        /// middle of a road.
        /// </summary>
        public bool OnVehicle;

        /// <summary>
        /// Which decal type actually put it on the wall.
        ///
        /// Per mark rather than per session, because the can and the extinguisher can now be
        /// using different ones -- and a mark restored with the wrong type is a mud splat
        /// where the player left paint.
        /// </summary>
        public int Type;

        /// <summary>
        /// How solid it went on, or 0 for whatever the config says today.
        ///
        /// PER MARK, because it is decided by how far away the wall was when it was laid --
        /// see PaintConfig.InkAt -- and that is a fact about the mark, not about the session.
        /// The sweep takes distant marks down and puts them back as you walk to them, so
        /// without this every restored mark would come back at the density of whatever you
        /// happened to be spraying at the time.
        ///
        /// Nought means "ask the config", which is what every save written before this says
        /// and what the extinguisher's marks say when no fade is configured.
        /// </summary>
        public float Ink;

        /// <summary>The handle the game last gave it, so a stale one can be cleaned up.</summary>
        public int Handle;

        /// <summary>True once the player has been far enough away for the game to drop it.</summary>
        public bool Away;

        /// <summary>
        /// When it went on the wall, in real milliseconds.
        ///
        /// Only ever used to protect it. Nothing else about a mark cares how old it is -- but
        /// the difference between "paint" and "the paint I am looking at right now" is the
        /// whole of why this was added, and there is no other way to tell them apart.
        ///
        /// Not saved. A mark loaded from a previous session is old by definition, and it should
        /// be: the protection is for the can that is still in your hand.
        /// </summary>
        public int Made;
    }

    /// <summary>
    /// Everything sprayed, and the fight to keep it on the wall.
    ///
    /// THE POOL IS THE WHOLE PROBLEM. The game holds a few hundred decals across the entire
    /// world and quietly drops the oldest when it runs out -- so a mod that paints continuously
    /// erases its own work from behind while the player is still pressing the trigger, and it
    /// looks exactly like a bug in the placement code.
    ///
    /// So the list here is the truth and the decals are a view of it. Anything beyond
    /// FarEnough is taken off the wall deliberately, which hands its slot back rather than
    /// waiting for the game to take it; anything back within NearEnough is put on again from
    /// the record. The player never sees either happen because both thresholds are further away
    /// than a splatter this size is legible.
    ///
    /// The two distances differ on purpose. One number means a mark on the boundary is added
    /// and removed on alternate frames forever.
    /// </summary>
    internal sealed class Marks
    {
        /// <summary>
        /// The ladder: blood, then paint. Nothing else, ever.
        ///
        /// 1010 is splatters_blood, and judged on SHAPE it is the best texture the game
        /// ships for a can: a dense middle with flung droplets round the edge, which is what
        /// comes off an aerosol at the end of a stroke. 1030 is splatters_paint -- pale,
        /// speckled, holes through it, but it takes any colour honestly -- and it is only
        /// here for an install where blood will not place.
        ///
        /// THE IMPACT FAMILY IS GONE FROM THIS LIST AND REFUSED EVERYWHERE ELSE. 4010, 4020
        /// and 4050 are the marks a BULLET leaves in metal, concrete and wood: a small solid
        /// lump of near-black with a soft edge. They were in here as a fallback and they were
        /// reachable from the ini as a can texture, a mix texture and the vehicle texture, and
        /// a wall with them on it is a wall somebody shot. This is a spray can. See Bullets.
        /// </summary>
        private static readonly int[] Types = { 1010, 1030 };

        /// <summary>What a type id is, for a log line that has to name one.</summary>
        private static string Called(int type)
        {
            switch (type)
            {
                case 1010: return "splatters_blood";
                case 1020: return "splatters_mud";
                case 1030: return "splatters_paint";
                case 1040: return "splatters_water";
                case 1110: return "a blood decal";
                case 4010: return "weapImpact_metal";
                case 4020: return "weapImpact_concrete";
                case 4050: return "weapImpact_wood";
                default: return "type " + type;
            }
        }

        /// <summary>Never expires. The whole point is that it stays.</summary>
        private const float Forever = -1f;

        /// <summary>
        /// How far away paint is taken down, and how much nearer it goes back up.
        ///
        /// FOUR HUNDRED, UP FROM A HUNDRED AND FIFTY. A hundred and fifty metres is the length
        /// of a couple of blocks -- you walk to the end of the street, turn round, and the wall
        /// behind you is bare. It was chosen when the only question was how many decals the
        /// pool could hold at once, and answered as if the paint were scenery you would not
        /// look back at. It is not scenery, it is the thing you spent the evening on.
        ///
        /// The gap between the two is what stops it flapping: stand exactly on a boundary and
        /// a single number would put paint up and take it down on alternate sweeps. A hundred
        /// metres of hysteresis is more than anybody drifts about while standing still.
        ///
        /// COSTS LESS THAN IT LOOKS, because of the near-ring pass in Sweep: everything within
        /// CloseUp is asked for a slot before anything beyond it, so a bigger ring does not
        /// take slots away from the wall in front of you. What it changes is which wall gets
        /// the ones LEFT OVER, and a wall two streets back is a better answer than nothing.
        /// </summary>
        private const float FarEnough = 400f;
        private const float NearEnough = 300f;

        /// <summary>How often the cull-and-restore pass runs. It is not a per-frame job.</summary>
        private const int SweepMs = 1500;

        private readonly List<Mark> _marks = new List<Mark>();
        private readonly PaintConfig _cfg;

        private int _type;
        private int _nextSweep;
        private int _refused;

        /// <summary>How many nearby marks could not get a decal last sweep. See Sweep.</summary>
        private int _stuck;
        private bool _proved;

        /// <summary>
        /// The intake: how many have gone up this frame, whether the game has stopped taking
        /// them, and the queue of away-marks waiting to go back up. See Sweep.
        /// </summary>
        private readonly List<Mark> _queue = new List<Mark>();
        private int _queueAt;
        private int _queueWas;
        private int _placedThisFrame;
        private bool _throttled;
        private int _poolFull;

        /// <summary>
        /// How many go up a frame from the queue. The game takes about thirty-two new decals a
        /// frame -- the log counted exactly that -- and the can itself needs some of them, so
        /// the queue takes twenty and leaves the rest.
        /// </summary>
        private const int PerFrame = 20;

        /// <summary>
        /// THE POOL, LEARNED. The game does not refuse a decal when its pool is full; it takes
        /// the oldest one down, silently, and the oldest is as likely to be your own piece on
        /// this wall as anything. So: how many of ours are up (_up), how many we let be up
        /// (_budget, learned from where evictions start), and the ones found dead this frame,
        /// near enough to go straight back (_lost). See Verify, Room and Learn.
        /// </summary>
        private readonly List<Mark> _lost = new List<Mark>();
        private int _up;
        private int _budget = BudgetCeiling;
        private int _verifyAt;
        private int _evicted;
        private int _upWhenEvicted;
        private int _calm;
        private int _saidPoolAt;

        /// <summary>
        /// How many alive-checks a frame, how far under the observed pool the budget sits, the
        /// least it will ever be, and the most.
        /// </summary>
        private const int VerifyPerFrame = 120;
        private const int BudgetHeadroom = 24;
        private const int BudgetFloor = 128;
        private const int BudgetCeiling = 200000;

        public Marks(PaintConfig cfg)
        {
            _cfg = cfg;
        }

        /// <summary>
        /// The decal type the tool about to paint would prefer, or 0 for whatever works.
        ///
        /// Set by the sprayer before each mark, because the two tools may not agree: the can
        /// can be told to lay mud while the extinguisher stays on paint. A preference, not an
        /// instruction -- if the game will not place it, the ladder still runs and something
        /// goes on the wall.
        /// </summary>
        public int Wanted;

        /// <summary>
        /// How long a mark is protected from being recycled, and how far apart two have to be
        /// before one may take the other's slot.
        ///
        /// The margin is a REAL distance, in metres. It used to be a number added to a squared
        /// one, which is twelve metres at point blank and seventy centimetres at a hundred --
        /// see Recycle, where the fault and the fix are written down.
        /// </summary>
        private const int FreshMs = 120000;
        private const float RecycleMetres = 12f;

        /// <summary>Past this much further again, no donor could be a better one. Squared.</summary>
        private const float Enough = 900f;

        public int Count => _marks.Count;

        /// <summary>Which decal the install actually has, once something has placed.</summary>
        public int TypeInUse => _type;

        /// <summary>Whether the one that takes colour properly is the one being used.</summary>
        public bool ColourIsHonest => _type == 1030 || _type == 0;

        /// <summary>Puts one on the wall and remembers it.</summary>
        public void Put(Vector3 at, Vector3 into, Vector3 side, float size,
                        float r, float g, float b, int hit = 0, float ink = 0f)
        {
            // A CAR IS NOT A WALL. The probe has always included vehicles and the paint has
            // never appeared on one, because the decal type walls use does not apply to them.
            // Vehicles get their own, and Place says in the log whether it actually stuck.
            var onCar = _cfg.VehicleDecal > 0 && IsVehicle(hit);
            // OLDEST FIRST, and taken off the wall rather than merely forgotten. Dropping it
            // from the list alone would leave a decal nothing owns, which is a slot gone for
            // the rest of the session.
            while (_marks.Count >= Math.Max(16, _cfg.MaxMarks))
            {
                Wipe(_marks[0]);
                _marks.RemoveAt(0);
            }

            var mark = new Mark
            {
                At = at, Into = into, Side = side,
                Size = size, R = r, G = g, B = b,
                Ink = ink,
                OnVehicle = onCar
            };

            // ROOM FIRST. See Room: once the learned budget is reached a far mark of ours
            // gives up its slot before this one is asked for, so what goes is the furthest
            // away rather than whatever the game would have taken, which is the oldest.
            mark.Handle = Room(at) ? Place(mark) : 0;

            // THE GAME'S POOL IS THE REAL CEILING, NOT MaxMarks. When it refuses outright, a
            // refusal takes the slot back off whatever is furthest away and already painted.
            // Paint in front of you always beats paint a street behind you, and the far one
            // stays in the list, so it comes back when you walk to it. That turns the pool
            // from a hard cap on how much you can paint into a budget spent on whatever you
            // are actually looking at.
            if (mark.Handle == 0 && !_throttled && Recycle(at)) mark.Handle = Place(mark);

            if (mark.Handle == 0)
            {
                // KEPT, NOT DROPPED. Either the game's intake is full for this frame -- in
                // which case it goes up next frame from the queue -- or the pool is, in which
                // case it goes up when a slot frees. A refused dab used to be thrown away,
                // which is paint you sprayed that was never anywhere.
                mark.Away = true;
                mark.Made = Game.GameTime;
                _marks.Add(mark);

                if (_throttled) return;

                _refused++;

                if (_refused == 12)
                {
                    // SAID WHEN IT BITES, not at startup. Everybody hits this eventually and
                    // nobody wants to be told about it before they have -- but the moment
                    // twelve marks in a row are refused, the player is standing in front of a
                    // wall that will not take paint and deserves to know it is the game's
                    // limit and not the mod, and what actually moves it.
                    Log.Warn("Twelve marks in a row would not place -- the game is full of " +
                             "decals. This is its own limit, not the mod's: nothing painted is " +
                             "lost, and it comes back as you walk up to it. What raises it is " +
                             "a limit adjuster: DecalLimitAdjuster.asi next to the game exe, " +
                             "with [ActiveDecals] Limit= set to what you want and PairRVA= " +
                             "pointed at the pair its log lists (on Enhanced 1.0.1158.13 that is " +
                             "0x0071D50E). DecalPatch.asi does the same up to 2048.");
                }

                return;
            }

            _refused = 0;

            // Stamped here as well as in Streak, because this is the one that matters: Put is
            // the ordinary dab from the can and it is what somebody is doing when they say the
            // paint vanished.
            mark.Made = Game.GameTime;

            _marks.Add(mark);
        }

        /// <summary>Whether what the ray hit was a vehicle.</summary>
        private static bool IsVehicle(int entity)
        {
            if (entity == 0) return false;

            try
            {
                return Function.Call<bool>(Hash.DOES_ENTITY_EXIST, entity) &&
                       Function.Call<bool>(Hash.IS_ENTITY_A_VEHICLE, entity);
            }
            catch
            {
                return false;
            }
        }

        private bool _saidAboutCars;

        /// <summary>
        /// Lays a STRETCHED mark and hands it back, so whoever owns it can keep changing it.
        ///
        /// The only thing here that returns its mark. A drip is one decal that grows, not a
        /// trail of decals that accumulate, so the thing drawing it has to be able to reach
        /// back and make it longer.
        /// </summary>
        public Mark Streak(Vector3 at, Vector3 into, Vector3 side, float wide, float tall,
                           float r, float g, float b, int hit = 0, float ink = 0f)
        {
            while (_marks.Count >= Math.Max(16, _cfg.MaxMarks))
            {
                Wipe(_marks[0]);
                _marks.RemoveAt(0);
            }

            var m = new Mark
            {
                At = at, Into = into, Side = side,
                Size = wide, Tall = tall,
                R = r, G = g, B = b,
                Ink = ink,
                OnVehicle = _cfg.VehicleDecal > 0 && IsVehicle(hit)
            };

            m.Handle = Room(at) ? Place(m) : 0;

            if (m.Handle == 0 && !_throttled && Recycle(at)) m.Handle = Place(m);

            if (m.Handle == 0)
            {
                // The pool is full and nothing could be taken for it: no drip. The intake
                // being full is different -- it goes up next frame like anything else.
                if (!_throttled) return null;
                m.Away = true;
            }

            m.Made = Game.GameTime;
            _marks.Add(m);

            return m;
        }

        /// <summary>Moves and re-stretches one, for a drip that is still running.</summary>
        public void Restreak(Mark m, Vector3 at, Vector3 side, float tall)
        {
            if (m == null) return;

            Wipe(m);

            m.At = at;
            m.Side = side;
            m.Tall = tall;

            m.Handle = Place(m);
            m.Away = m.Handle == 0;
        }

        /// <summary>
        /// A bullet mark asked for, turned into blood, and said once.
        ///
        /// Whoever asked -- an ini with 4020 in it, a mark saved with one from before this
        /// rule -- gets the splatter instead. Said once per session rather than per mark,
        /// because this runs three hundred times a second under a held trigger.
        /// </summary>
        private int Bullets(int asked)
        {
            if (!_saidAboutBullets)
            {
                _saidAboutBullets = true;

                Log.Warn("Decal type " + asked + " (" + Called(asked) + ") was asked for. That is " +
                         "the mark a bullet leaves and this is a spray can, so splatters_blood " +
                         "(1010) is used instead. Set CanDecal, MixDecal or VehicleDecal to " +
                         "1010 or 1030 to stop this line.");
            }

            return 1010;
        }

        private bool _saidAboutBullets;

        /// <summary>Puts one up, trying each decal type until the game accepts one.</summary>
        private int Place(Mark m)
        {
            // THE INTAKE IS FULL FOR THIS FRAME: nothing more is asked for. See Sweep.
            if (_placedThisFrame >= PerFrame + 12)
            {
                _throttled = true;
                return 0;
            }

            // A MARK THAT HAS ALREADY BEEN ON A WALL GOES BACK AS ITSELF, which is why this
            // is not simply the ladder. The sweep takes distant marks down and puts them back
            // as you return, and without this a mud tag comes back as paint.
            //
            // Failing that, whatever the tool about to paint asked for. Failing THAT, the
            // ladder -- an install that will not take the asked-for type should still show
            // something rather than leave a gap where the player's work was.
            //
            // Walked rather than built, at index -1, because this runs for every mark and a
            // fresh array per decal at three hundred a second is litter for the collector to
            // pick up mid-spray.
            var first = m.Type > 0 ? m.Type : (m.OnVehicle ? _cfg.VehicleDecal : Wanted);

            // NEVER A BULLET. See Types.
            if (first >= 4000) first = Bullets(first);

            for (var i = -1; i < Types.Length; i++)
            {
                int type;

                if (i < 0)
                {
                    if (first == 0) continue;
                    type = first;
                }
                else
                {
                    type = Types[i];

                    // Already tried above.
                    if (type == first) continue;

                    // Once one has worked, stop asking the others -- every refusal is a wasted
                    // call and there are a lot of these. Only applies to the ladder: a tool
                    // that asked for something gets to ask for it every time.
                    if (_type > 0 && type != _type) continue;
                }

                int handle;

                try
                {
                    handle = Function.Call<int>(Hash.ADD_DECAL, type,
                                                m.At.X, m.At.Y, m.At.Z,
                                                m.Into.X, m.Into.Y, m.Into.Z,
                                                m.Side.X, m.Side.Y, m.Side.Z,
                                                m.Size, m.Tall > 0f ? m.Tall : m.Size,
                                                m.R, m.G, m.B,
                                                // Its own, if it was laid with one. See Mark.Ink.
                                                m.Ink > 0f ? m.Ink : _cfg.Opacity,
                                                // FALSE, FALSE, FALSE -- what every single
                                                // ADD_DECAL call in the game's own scripts
                                                // passes. This had true in the first slot,
                                                // which was a guess, and a guess that differs
                                                // from all 20-odd of R*'s own calls is not a
                                                // guess worth keeping.
                                                Forever, false, false, false);
                }
                catch
                {
                    continue;
                }

                if (handle == 0) continue;

                _placedThisFrame++;
                _up++;

                // A HANDLE IS NOT A DECAL. ADD_DECAL hands back a number whether or not
                // anything ended up on the wall, so the first one that places gets asked
                // outright whether it lived -- otherwise "it returned a handle" gets treated
                // as proof of something nobody has actually seen.
                if (!_proved)
                {
                    _proved = true;

                    try
                    {
                        if (Function.Call<bool>(Hash.IS_DECAL_ALIVE, handle))
                        {
                            Log.Info("First decal is on the wall and alive: type " + type +
                                     ", " + m.Size.ToString("0.00") + "m across.");
                        }
                        else
                        {
                            Log.Warn("ADD_DECAL returned handle " + handle + " but " +
                                     "IS_DECAL_ALIVE says nothing is there. The call is being " +
                                     "accepted and discarded -- size was " +
                                     m.Size.ToString("0.00") + "m.");
                        }
                    }
                    catch
                    {
                        // The check is a diagnostic; never let it break placing paint.
                    }
                }

                // What the mark is, from now on and through a save.
                m.Type = type;

                // SAID ONCE, AND SAID EITHER WAY. Whether any decal type at all sticks to a
                // vehicle is the open question here, and the answer is worth one line in the
                // log rather than a report of "nothing happens" with nothing to go on.
                if (m.OnVehicle && !_saidAboutCars)
                {
                    _saidAboutCars = true;

                    var alive = false;
                    try { alive = Function.Call<bool>(Hash.IS_DECAL_ALIVE, handle); }
                    catch { }

                    if (alive)
                    {
                        Log.Info("Paint landed on a VEHICLE with decal type " + type +
                                 " and the game says it is there.");
                    }
                    else
                    {
                        Log.Warn("Decal type " + type + " was accepted on a VEHICLE but " +
                                 "IS_DECAL_ALIVE says nothing is on it. If nothing ever shows " +
                                 "on cars, this is why -- try another VehicleDecal, and if none " +
                                 "of them take then no decal type works on a vehicle.");
                    }
                }

                // The session's fallback story is about the LADDER, not about a tool that asked
                // for something unusual. Somebody testing mud on the can has not discovered
                // that their install lacks the paint decal.
                if (_type != type && first == 0)
                {
                    _type = type;

                    if (type == 1010)
                    {
                        Log.Info("Using splatters_blood (1010): the dense middle and flung " +
                                 "droplets of an aerosol. Colours multiply a red texture, so " +
                                 "black comes out perfect and bright greens and blues come out " +
                                 "muddy.");
                    }
                    else
                    {
                        Log.Warn("splatters_blood (1010) would not place; using " + Called(type) +
                                 " (" + type + "). Colours will be true and the marks will be " +
                                 "speckle rather than splatter.");
                    }
                }

                return handle;
            }

            // REFUSED AFTER SOMETHING WENT UP THIS FRAME: that is the intake, not the pool.
            // Whoever asked keeps the mark and asks again next frame, and recycles nothing.
            if (_placedThisFrame > 0) _throttled = true;

            return 0;
        }

        private void Wipe(Mark m)
        {
            if (m == null || m.Handle == 0) return;

            try { Function.Call(Hash.REMOVE_DECAL, m.Handle); }
            catch { /* it was going anyway */ }

            m.Handle = 0;
            if (_up > 0) _up--;
        }

        /// <summary>
        /// Takes distant paint off the wall and puts near paint back.
        ///
        /// Not for tidiness -- for the pool. A slot held by a splatter four streets away is a
        /// slot the one in front of you cannot have.
        /// </summary>
        /// <summary>
        /// Takes a decal off the furthest-away mark so a nearer one can have its slot.
        ///
        /// Only when the victim really is further off than what is being sprayed. Without
        /// that check a full pool trades one visible mark for another every single dab, which
        /// is a wall that flickers rather than a wall that fills.
        /// </summary>
        /// <summary>Where the last bounded scan left off. See Recycle.</summary>
        private int _scan;

        private bool Recycle(Vector3 near)
        {
            Vector3 me;

            try
            {
                var ped = Game.Player.Character;
                if (ped == null || !ped.Exists()) return false;
                me = ped.Position;
            }
            catch
            {
                return false;
            }

            var n = _marks.Count;
            if (n == 0) return false;

            // Squared throughout. Nothing here needs a real distance, only which of two is
            // bigger, and at this list size the square roots are the whole cost.
            var mine = me.DistanceToSquared(near);

            // IT HAS TO BEAT WHAT IS BEING SPRAYED BY A REAL MARGIN.
            //
            // This was four, in SQUARED metres, which is not a margin at all -- two marks on
            // the same wall a metre apart differ by more than that, so anything already up
            // could be taken down for anything else going up. On a wall with two thousand marks
            // on it that is not a pool being managed, it is a pool being churned: every sweep,
            // everything evicts everything, and what you get is whichever few hundred happened
            // to be asked last.
            //
            // A hundred and forty-four is twelve metres of separation before one mark may take
            // another's slot. A donor has to be properly somewhere else, not just marginally
            // further along the same wall.
            // THE MARGIN HAS TO BE A DISTANCE, NOT A NUMBER ADDED TO A SQUARE.
            //
            // This was `mine + RecycleMargin` with both in squared metres, described as twelve
            // metres of separation -- and it is twelve metres only at point blank. Squares grow
            // faster than the thing they are squares of, so adding a constant to one buys less
            // and less separation the further out you go: at twenty metres it is three, at
            // fifty it is one and a half, and at a hundred it is seventy centimetres.
            //
            // Which is exactly the churn the comment on RecycleMargin was written to prevent,
            // and it is why paint appeared to evaporate as you walked away from it. Once you
            // were any distance from a wall, every mark on it was a legal donor for every other
            // mark on it, and the pool spent its whole time taking your work down to put your
            // other work up.
            //
            // One square root per call -- not per candidate -- and the separation is twelve
            // metres wherever you are standing.
            var worst = -1;
            var worstD = (float)Math.Sqrt(mine) + RecycleMetres;

            worstD *= worstD;

            // A SECOND-BEST, HELD BACK FOR WHEN THERE IS NO BEST -- and this is the fix for
            // "it gets to a point and just stops spraying".
            //
            // Fresh paint is protected from being recycled, for two minutes, so that finishing
            // a piece cannot eat the start of it. That is right and it has a corner: spray hard
            // enough for long enough and EVERYTHING in the pool is fresh, every candidate is
            // protected, the recycler finds nothing, and the can quietly stops marking the wall
            // while still hissing in your hand.
            //
            // So a protected mark is still noted, just never preferred. If the strict pass
            // finds anything at all it wins; only when it finds nothing does the furthest away
            // of your own recent work give up its slot -- which is the right thing to lose,
            // because it is the far end of what you have been painting rather than the bit in
            // front of you.
            var spare = -1;
            var spareD = worstD;

            var now = Game.GameTime;

            // A BOUNDED, ROLLING SCAN -- NOT THE WHOLE LIST.
            //
            // This runs on every refused dab, which once the pool is full is twenty-odd times
            // a second, and the list can now hold fifty thousand. A full pass would be over a
            // million distance checks a second to answer a question that does not need an
            // exact answer: any mark comfortably behind you is a perfectly good donor.
            //
            // The cursor carries between calls, so successive refusals sweep different parts
            // of the list rather than re-reading the same window forever.
            var window = n < 256 ? n : 256;

            for (var k = 0; k < window; k++)
            {
                _scan++;
                if (_scan >= n) _scan = 0;

                var m = _marks[_scan];

                if (m.Away || m.Handle == 0) continue;

                // AND FRESH PAINT IS NEVER A DONOR. This is the fault behind "I sprayed it,
                // turned round, and it was gone": the sweep walks the list restoring old marks,
                // each one asks for a slot, and the only thing it looks at is distance -- so a
                // tag from last week, one metre nearer than the one you are still stood in
                // front of, takes its slot. You watched your own paint be recycled for paint
                // you had already forgotten about.
                //
                // For two minutes after it goes up, a mark cannot be taken down for anything.
                // Long enough to finish a piece and stand back and look at it, short enough
                // that it is not a permanent reservation.
                var d = me.DistanceToSquared(m.At);

                if (now - m.Made < FreshMs)
                {
                    // Noted and passed over. See spare, above.
                    if (d > spareD) { spareD = d; spare = _scan; }
                    continue;
                }

                if (d <= worstD) continue;

                worstD = d;
                worst = _scan;

                // Far enough behind you that looking harder cannot matter. Thirty metres
                // past you, worked out the same way as the margin above and for the same
                // reason -- a flat addition to a square is not a distance.
                if (d > worstD + Enough) break;
            }

            // Nothing old enough to take. The furthest of the new, or nothing at all.
            if (worst < 0) worst = spare;

            if (worst < 0) return false;

            var victim = _marks[worst];

            Wipe(victim);
            victim.Away = true;

            return true;
        }

        /// <summary>
        /// Every frame: a few marks off the queue and onto the wall. Every second and a half:
        /// the far ones come down and the queue is rebuilt from what is near and not up.
        ///
        /// THE GAME TAKES ABOUT THIRTY-TWO NEW DECALS A FRAME. The log found it: with six
        /// thousand marks in range and one sweep asking for all of them at once, exactly
        /// thirty-two went up per sweep and the rest were refused -- and every refusal was
        /// read as "the pool is full", so Recycle took a far mark's slot for each one, for
        /// nothing. Six thousand refusals a sweep was six thousand far marks wiped a sweep.
        /// THAT is what "old tags disappear" was: the intake being full for one frame,
        /// mistaken for the pool being full for good, and the cure tearing down paint a
        /// street away to make room it could not use anyway.
        ///
        /// So the wall is fed in order -- what you are stood at first, newest first inside
        /// that, then the ring beyond -- a few a frame, every frame. A refusal AFTER something
        /// has gone up this frame is the intake: stop, keep the place, carry on next frame,
        /// recycle nothing. Only a refusal with nothing placed yet this frame is the pool, and
        /// only then is a far slot taken. Six thousand marks now come back in about five
        /// seconds instead of five minutes, and nothing is torn down to do it.
        ///
        /// Sweep is called once a frame by the mod's tick, which makes it the frame clock for
        /// everything in here that has to know how much has already gone up.
        /// </summary>
        public void Sweep()
        {
            _placedThisFrame = 0;
            _throttled = false;

            Vector3 me;

            try
            {
                var ped = Game.Player.Character;
                if (ped == null || !ped.Exists()) return;
                me = ped.Position;
            }
            catch
            {
                return;
            }

            Drain(me);
            Verify(me);

            var now = Game.GameTime;
            if (now < _nextSweep) return;
            _nextSweep = now + SweepMs;

            Rescan(me);
            Learn();
        }

        /// <summary>
        /// A slice of the marks believed up, asked whether they still are.
        ///
        /// THE GAME TAKES DECALS DOWN WITHOUT A WORD. Its pool is a fixed number of slots and
        /// when they are gone the oldest goes to make room -- ours or its own -- and the
        /// handle simply stops being alive. The record had no way of knowing: a mark stayed
        /// "up" for the rest of the session, never put back, and that is the tag that was
        /// there a minute ago and is not now.
        ///
        /// A round-robin slice a frame, over the whole record, so a few thousand up marks are
        /// all looked at about once a second for the price of a hundred-odd native calls.
        /// A dead one is marked away and counted; if it is near it goes to the front of the
        /// queue and is back up next frame.
        /// </summary>
        private void Verify(Vector3 me)
        {
            var n = _marks.Count;
            if (n == 0 || _up == 0) return;

            var restore = NearEnough * NearEnough;
            var checks = 0;
            var looked = 0;

            while (looked < n && checks < VerifyPerFrame)
            {
                _verifyAt++;
                if (_verifyAt >= n) _verifyAt = 0;
                looked++;

                var m = _marks[_verifyAt];
                if (m.Away || m.Handle == 0) continue;

                checks++;

                var alive = true;

                try { alive = Function.Call<bool>(Hash.IS_DECAL_ALIVE, m.Handle); }
                catch { continue; }

                if (alive) continue;

                // Gone without a word: this is the pool, and the count of ours that were up
                // when it started going is the size of it.
                if (_evicted == 0) _upWhenEvicted = _up;
                _evicted++;

                m.Handle = 0;
                m.Away = true;
                if (_up > 0) _up--;

                var want = Math.Max(BudgetFloor, _upWhenEvicted - BudgetHeadroom);
                if (want < _budget) _budget = want;

                if (me.DistanceToSquared(m.At) < restore) _lost.Add(m);
            }
        }

        /// <summary>
        /// Whether one more can go up without the game taking something down for it.
        ///
        /// Under the budget, yes. At it, a far mark of ours gives up its slot first, and the
        /// answer is whether one could be found. The choice of WHAT goes is the whole point:
        /// left to the game it is the oldest decal in the world, which is as likely to be the
        /// start of the piece you are painting as anything; Recycle takes the furthest away.
        /// </summary>
        private bool Room(Vector3 near)
        {
            if (_up < _budget) return true;
            return Recycle(near);
        }

        /// <summary>
        /// What this sweep's evictions taught, said once, and the budget let creep back up
        /// while nothing is being taken.
        ///
        /// It creeps because the pool is shared: the game's own bullet holes and tyre marks
        /// come and go, and a budget learned on a busy street is too low for a quiet one.
        /// Sixteen every four calm sweeps, and only when we are actually up against it.
        /// </summary>
        private void Learn()
        {
            if (_evicted > 0)
            {
                _calm = 0;

                if (_saidPoolAt == 0 || _budget < _saidPoolAt - 256)
                {
                    _saidPoolAt = _budget;

                    Log.Info("The game took " + _evicted + " of our decals down while " + _upWhenEvicted +
                             " were up. That is the size of its own pool -- the compare thresholds " +
                             "are not it. Keeping " + _budget + " up from here, nearest first; the " +
                             "furthest give way and come back as you move.");
                }
            }
            else
            {
                _calm++;

                if (_calm >= 4 && _budget < BudgetCeiling && _up >= _budget - 40)
                {
                    _budget += 16;
                    _calm = 0;
                }
            }

            _evicted = 0;
        }

        /// <summary>A few off the queue and onto the wall. See Sweep.</summary>
        private void Drain(Vector3 me)
        {
            var restore = NearEnough * NearEnough;

            // THE ONES THE GAME TOOK DOWN, FIRST. They were on a wall you can see a moment
            // ago; nothing in the queue is more urgent. See Verify.
            while (_lost.Count > 0 && _placedThisFrame < PerFrame && !_throttled)
            {
                var m = _lost[_lost.Count - 1];
                _lost.RemoveAt(_lost.Count - 1);

                if (!m.Away || m.Handle != 0) continue;

                m.Handle = Room(m.At) ? Place(m) : 0;
                if (m.Handle == 0 && !_throttled && Recycle(m.At)) m.Handle = Place(m);

                if (m.Handle != 0)
                {
                    m.Away = false;
                    continue;
                }

                if (_throttled)
                {
                    _lost.Add(m);
                    return;
                }

                _poolFull++;
            }

            while (_queueAt < _queue.Count && _placedThisFrame < PerFrame && !_throttled)
            {
                var m = _queue[_queueAt++];

                if (!m.Away || m.Handle != 0) continue;
                if (me.DistanceToSquared(m.At) > restore) continue;

                m.Handle = Room(m.At) ? Place(m) : 0;

                // THE POOL, NOT THE INTAKE: a slot is taken off something far away, once.
                // Recycle only takes from marks a good margin further away than this one, so
                // the nearest paint wins and two marks cannot evict each other.
                if (m.Handle == 0 && !_throttled && Recycle(m.At)) m.Handle = Place(m);

                if (m.Handle != 0)
                {
                    m.Away = false;
                    continue;
                }

                if (_throttled)
                {
                    // The intake is full for this frame. This one goes first next frame.
                    _queueAt--;
                    return;
                }

                _poolFull++;
            }
        }

        /// <summary>
        /// The far ones down, and the queue rebuilt: the wall you are stood at first, newest
        /// first inside each ring.
        ///
        /// ONE PASS OVER THE WHOLE RING IN LIST ORDER IS WHY A WALL USED TO RENDER HALF-DONE.
        /// List order is when it was painted, so walking up to a wall put you in a competition
        /// against paint a hundred metres behind you that you cannot see, and the piece in
        /// front of your face came out with holes in it. Everything within CloseUp is queued
        /// before anything beyond it, so what you are LOOKING at is always complete and the
        /// thing that goes short is a wall down the road, which is the right thing to lose.
        /// Newest first inside each ring, which is backwards through the list -- among marks
        /// you can equally see, the most recent work is what should survive.
        /// </summary>
        private void Rescan(Vector3 me)
        {
            // Squared, against squared thresholds. A full pass over a quarter of a million
            // marks is fine; a quarter of a million square roots is the part that is not.
            var drop = FarEnough * FarEnough;
            var near = CloseUp * CloseUp;
            var restore = NearEnough * NearEnough;

            // THE FAR ONES COME DOWN BEFORE ANYTHING GOES UP, so the slots they were holding
            // are free for the queue rather than for the next rescan.
            for (var i = _marks.Count - 1; i >= 0; i--)
            {
                var m = _marks[i];

                if (m.Away) continue;

                if (me.DistanceToSquared(m.At) > drop)
                {
                    Wipe(m);
                    m.Away = true;
                }
            }

            _queue.Clear();
            _queueAt = 0;

            for (var i = _marks.Count - 1; i >= 0; i--)
            {
                var m = _marks[i];
                if (!m.Away) continue;

                if (me.DistanceToSquared(m.At) < near) _queue.Add(m);
            }

            for (var i = _marks.Count - 1; i >= 0; i--)
            {
                var m = _marks[i];
                if (!m.Away) continue;

                var d = me.DistanceToSquared(m.At);
                if (d >= near && d < restore) _queue.Add(m);
            }

            // SAID WHEN IT STARTS, not every sweep. Paint near enough to be on the wall and
            // not there is the one thing this class exists to prevent, and "some of my tag is
            // missing" is impossible to act on without a number. The queue is not a problem --
            // it is the next few seconds' work -- but the first time a lot of it appears is
            // worth a line, and so is the pool actually refusing.
            if (_queue.Count >= 200 && _queueWas < 200)
            {
                Log.Info(_queue.Count + " mark(s) in range coming back up, " + PerFrame + " a frame.");
            }

            _queueWas = _queue.Count;

            if (_poolFull > 0 && _stuck == 0)
            {
                Log.Info(_poolFull + " mark(s) near you could not get a slot since the last sweep " +
                         "-- the game's decal pool is full. Nothing is lost; they go back up as " +
                         "slots free, and the nearest paint always wins them.");
            }

            _stuck = _poolFull;
            _poolFull = 0;
        }

        /// <summary>
        /// The ring that gets the slots before anything else does.
        ///
        /// Forty metres, which is a wall and its neighbours rather than a district. Wide enough
        /// that a piece does not lose its edges when you step back to look at it, and narrow
        /// enough that it is genuinely what is in front of you competing for the pool.
        /// </summary>
        private const float CloseUp = 40f;

        /// <summary>Everything gone, off the wall and out of the record.</summary>
        /// <summary>How far a single area wipe reaches, and how coarsely they are spread.</summary>
        private const float WipeRadius = 3.5f;
        private const float WipeGrid = 4f;

        /// <summary>Everything within this of the player goes too, tracked or not.</summary>
        private const float WipeAround = 250f;

        /// <summary>
        /// Drops every mark and its decal, and says nothing about it.
        ///
        /// The quiet half of Clear, for when the list is being replaced rather than destroyed.
        /// </summary>
        private void Forget()
        {
            for (var i = 0; i < _marks.Count; i++) Wipe(_marks[i]);

            _marks.Clear();
            _queue.Clear();
            _lost.Clear();
            _queueAt = 0;
            _up = 0;
        }

        public void Clear()
        {
            // ---- the ones this list owns, by handle ----
            for (var i = 0; i < _marks.Count; i++) Wipe(_marks[i]);

            // ---- AND EVERYTHING ELSE, BY AREA ----
            //
            // REMOVE_DECAL only ever reaches a decal this list still has a handle for, and
            // that set has proven to be smaller than what is actually on the wall. Anything
            // that lost its handle -- dropped when the list hit its cap, recycled to free a
            // pool slot, or put there by a SECOND copy of this engine running alongside, which
            // is what was really happening -- survived a wipe and looked like the button only
            // clearing the most recent paint.
            //
            // REMOVE_DECALS_IN_RANGE does not care who placed what. It takes a point and a
            // radius, so the wipe becomes a question about places rather than about
            // bookkeeping, and bookkeeping is the thing that was wrong.
            var spots = new List<Vector3>();

            // A coarser grid once there is a lot of ground to cover. Fifty thousand marks
            // spread over a city would otherwise be thousands of sweeps in a single frame;
            // widening the cells trades precision nobody can see for a bounded cost.
            var grid = _marks.Count > 6000 ? WipeGrid * 3f : WipeGrid;
            var radius = _marks.Count > 6000 ? WipeRadius * 3f : WipeRadius;

            // A SET, NOT A LINEAR SEARCH. Checking each mark against every cell found so far
            // is fine for a few hundred marks and quadratic for fifty thousand -- at that size
            // it is billions of comparisons and the game stops dead on a button press.
            var seen = new HashSet<long>();

            foreach (var m in _marks)
            {
                // Snapped to a coarse grid so a wall covered in four hundred marks costs a
                // handful of calls instead of four hundred. They are clustered by nature --
                // that is what painting IS -- so this collapses very well.
                var cx = (int)Math.Round(m.At.X / grid);
                var cy = (int)Math.Round(m.At.Y / grid);
                var cz = (int)Math.Round(m.At.Z / grid);

                // Three cell indices packed into one long. The map is about sixteen thousand
                // metres across, so at this grid nothing comes near overflowing fourteen bits
                // a side; the masks keep a wild coordinate from corrupting a neighbour's bits
                // rather than guarding against a case that happens.
                var key = ((long)(cx & 0x3FFF) << 28) |
                          ((long)(cy & 0x3FFF) << 14) |
                          (long)(cz & 0x3FFF);

                if (!seen.Add(key)) continue;

                spots.Add(new Vector3(cx, cy, cz));
            }

            var swept = 0;

            foreach (var cell in spots)
            {
                try
                {
                    Function.Call(Hash.REMOVE_DECALS_IN_RANGE,
                                  cell.X * grid, cell.Y * grid, cell.Z * grid,
                                  radius);
                    swept++;
                }
                catch
                {
                    // One patch of wall keeps its paint. The rest still goes.
                }
            }

            // And a wide one where he is standing, which catches anything that was never in
            // this list at all -- the other mod's, or a session whose record was lost.
            try
            {
                var me = Game.Player.Character;

                if (me != null && me.Exists())
                {
                    var at = me.Position;
                    Function.Call(Hash.REMOVE_DECALS_IN_RANGE, at.X, at.Y, at.Z, WipeAround);
                    swept++;
                }
            }
            catch
            {
                // Nothing to undo.
            }

            var had = _marks.Count;
            _marks.Clear();

            Log.Info("Wiped every mark: " + had + " tracked, plus " + swept +
                     " area sweep(s) for anything this list had lost track of.");
        }

        /// <summary>Off the wall but still remembered, for a reload.</summary>
        public void LetGo()
        {
            for (var i = 0; i < _marks.Count; i++) Wipe(_marks[i]);
        }

        // ---- persistence -------------------------------------------------------

        public Json ToJson()
        {
            var arr = Json.Array();

            for (var i = 0; i < _marks.Count; i++)
            {
                var m = _marks[i];

                // Paint on a car is not saved. The decal is in world space, so it is hanging
                // where the car was standing -- restoring it next session puts it in the
                // middle of whatever road that was.
                if (m.OnVehicle) continue;

                arr.Add(Json.Object()
                    .Set("x", Math.Round(m.At.X, 3)).Set("y", Math.Round(m.At.Y, 3)).Set("z", Math.Round(m.At.Z, 3))
                    .Set("ix", Math.Round(m.Into.X, 3)).Set("iy", Math.Round(m.Into.Y, 3)).Set("iz", Math.Round(m.Into.Z, 3))
                    .Set("sx", Math.Round(m.Side.X, 3)).Set("sy", Math.Round(m.Side.Y, 3)).Set("sz", Math.Round(m.Side.Z, 3))
                    .Set("w", Math.Round(m.Size, 3))
                    .Set("h", m.Tall > 0f ? Math.Round(m.Tall, 3) : 0.0)
                    .Set("r", Math.Round(m.R, 3)).Set("g", Math.Round(m.G, 3)).Set("b", Math.Round(m.B, 3))
                    // Only when it has one. A mark with no ink of its own takes the config's,
                    // which is what every file written before the fade existed says.
                    .Set("o", m.Ink > 0f ? Math.Round(m.Ink, 3) : 0.0)
                    // Only when it differs from the session's own. Most marks match it, and
                    // ten bytes each across fifty thousand is half a megabyte of saying so.
                    .Set("t", m.Type == _type ? 0 : m.Type));
            }

            var doc = Json.Object();
            doc.Set("decal", _type);
            doc.Set("marks", arr);
            return doc;
        }

        public void LoadFrom(Json doc)
        {
            // FORGET, NOT WIPE. This used to call Clear, which is the CLEAR EVERY WALL button:
            // it sweeps whole areas of the map for decals it has lost track of and it announces
            // itself in the log.
            //
            // At load there is nothing to sweep -- the list is empty and the session has not
            // painted anything -- so all that reached the log was a line saying every mark had
            // been wiped, sitting one millisecond before the line saying how many had been
            // loaded. Reading that back it looks exactly like the mod destroying a save on
            // startup, which cost an hour of chasing a bug that was not there.
            Forget();
            if (doc == null || doc.IsNull) return;

            var type = doc["decal"].AsInt(0);
            if (type > 0) _type = type;

            foreach (var node in doc["marks"].Items)
            {
                var m = new Mark
                {
                    At = new Vector3(node["x"].AsFloat(), node["y"].AsFloat(), node["z"].AsFloat()),
                    Into = new Vector3(node["ix"].AsFloat(), node["iy"].AsFloat(), node["iz"].AsFloat()),
                    Side = new Vector3(node["sx"].AsFloat(), node["sy"].AsFloat(), node["sz"].AsFloat()),
                    Size = node["w"].AsFloat(0.4f),
                    Tall = node["h"].AsFloat(0f),
                    R = node["r"].AsFloat(1f),
                    G = node["g"].AsFloat(1f),
                    B = node["b"].AsFloat(1f),
                    Ink = node["o"].AsFloat(0f),

                    // Absent, zero, or matching means the session's own -- which is what every
                    // file written before mud was an option says.
                    Type = node["t"].AsInt(0)
                };

                // Left off the wall. The sweep puts back whatever is near enough to matter,
                // which on a load is usually none of it.
                //
                // Made is deliberately left at nought. A mark read out of a save is old paint
                // however recently it was sprayed -- the protection above is for the can that
                // is still in your hand, and a whole wall loading in as "fresh" would make it
                // meaningless on the one pass where it matters most.
                m.Away = true;
                _marks.Add(m);
            }

            Log.Info("Loaded " + _marks.Count + " marks.");
        }
    }
}
