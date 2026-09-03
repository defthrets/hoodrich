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
        /// 1030 is splatters_paint: authored pale, so an arbitrary RGB comes out as that colour.
        ///
        /// The blood types behind it are a WORSE fallback than they look, and for a mod whose
        /// entire point is picking a colour they are close to useless -- the r/g/b arguments
        /// are multipliers over the source texture rather than a replacement, and Rockstar's
        /// own scripts pass 0.196, 0, 0 to darken blood. Green over red comes out near black.
        ///
        /// Kept anyway, because a dark splat still says somebody did something to that wall and
        /// beats a wall that did not change -- but it is logged loudly when it happens, since
        /// "my colours are all wrong" and "this install has no paint decal" are the same event.
        /// </summary>
        private static readonly int[] Types = { 1030, 1110, 1010 };

        /// <summary>Never expires. The whole point is that it stays.</summary>
        private const float Forever = -1f;

        private const float FarEnough = 150f;
        private const float NearEnough = 110f;

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
        /// The margin is in SQUARED metres, like everything else in the recycler -- a hundred
        /// and forty-four is twelve metres.
        /// </summary>
        private const int FreshMs = 120000;
        private const float RecycleMargin = 144f;

        public int Count => _marks.Count;

        /// <summary>Which decal the install actually has, once something has placed.</summary>
        public int TypeInUse => _type;

        /// <summary>Whether the one that takes colour properly is the one being used.</summary>
        public bool ColourIsHonest => _type == 1030 || _type == 0;

        /// <summary>Puts one on the wall and remembers it.</summary>
        public void Put(Vector3 at, Vector3 into, Vector3 side, float size,
                        float r, float g, float b, int hit = 0)
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
                OnVehicle = onCar
            };

            mark.Handle = Place(mark);

            // THE GAME'S POOL IS THE REAL CEILING, NOT MaxMarks. It holds a few hundred decals
            // across the entire world and refuses quietly once they are gone, so simply
            // allowing a bigger list buys nothing on its own -- the extra marks are recorded
            // and never make it onto a wall.
            //
            // So a refusal takes the slot back off whatever is furthest away and already
            // painted. Paint in front of you always beats paint a street behind you, and the
            // far one stays in the list, so it comes back when you walk to it. That turns the
            // pool from a hard cap on how much you can paint into a budget spent on whatever
            // you are actually looking at.
            if (mark.Handle == 0 && Recycle(at)) mark.Handle = Place(mark);

            if (mark.Handle == 0)
            {
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
                             "a limit adjuster. On GTA V Enhanced that is DecalPatch.asi, a " +
                             "drop-in ASI whose ini goes up to 2048 with no OpenIV and no RPF " +
                             "editing. On Legacy it is a gameconfig with raised pools, and it " +
                             "has to match your game build.");
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
                           float r, float g, float b, int hit = 0)
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
                OnVehicle = _cfg.VehicleDecal > 0 && IsVehicle(hit)
            };

            m.Handle = Place(m);

            if (m.Handle == 0 && Recycle(at)) m.Handle = Place(m);
            if (m.Handle == 0) return null;

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

        /// <summary>Puts one up, trying each decal type until the game accepts one.</summary>
        private int Place(Mark m)
        {
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
                                                m.R, m.G, m.B, _cfg.Opacity,
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

                    if (type == 1030)
                    {
                        Log.Info("Using splatters_paint (1030). Colours will be true.");
                    }
                    else
                    {
                        Log.Warn("splatters_paint (1030) would not place; fell back to " + type +
                                 ", which is a BLOOD decal. Its colour arguments multiply over " +
                                 "a red texture, so anything you pick will come out dark and " +
                                 "wrong. This is not the picker misbehaving.");
                    }
                }

                return handle;
            }

            return 0;
        }

        private static void Wipe(Mark m)
        {
            if (m == null || m.Handle == 0) return;

            try { Function.Call(Hash.REMOVE_DECAL, m.Handle); }
            catch { /* it was going anyway */ }

            m.Handle = 0;
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
            var worst = -1;
            var worstD = mine + RecycleMargin;

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
                if (now - m.Made < FreshMs) continue;

                var d = me.DistanceToSquared(m.At);
                if (d <= worstD) continue;

                worstD = d;
                worst = _scan;

                // Far enough behind you that looking harder cannot matter. 30m, squared.
                if (d > mine + 900f) break;
            }

            if (worst < 0) return false;

            var victim = _marks[worst];

            Wipe(victim);
            victim.Away = true;

            return true;
        }

        public void Sweep()
        {
            var now = Game.GameTime;
            if (now < _nextSweep) return;
            _nextSweep = now + SweepMs;

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

            // Squared, against squared thresholds. A full pass over fifty thousand marks is
            // fine; fifty thousand square roots is the part that is not.
            var drop = FarEnough * FarEnough;
            var restore = NearEnough * NearEnough;

            // THE FAR ONES COME DOWN BEFORE ANYTHING GOES UP, so the slots they were
            // holding are free for this same pass rather than the next one.
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

            // THEN TWICE, AND THE WALL YOU ARE STOOD AT GOES FIRST.
            //
            // ONE PASS OVER THE WHOLE RING IS WHY A WALL RENDERS HALF-DONE. The restore ring is
            // a hundred and ten metres and a used block holds thousands of marks inside it --
            // the log has counted three thousand near enough to want a slot at once. The pool
            // is two thousand and forty-eight at the absolute most and shared with every bullet
            // hole, tyre mark and blood splat in the world, so there are never enough slots for
            // everything in the ring and there never will be. Which ones get them is therefore
            // the whole question, and it was being answered by list order.
            //
            // List order is when it was painted. So walking up to a wall put you in a
            // competition against paint a hundred metres behind you that you cannot see, the
            // recent stuff won on age, and the piece in front of your face came out with holes
            // in it -- permanently, because nothing about standing there changes the answer.
            //
            // Near ring first fixes exactly that and costs one extra walk of a list. Everything
            // within CloseUp is asked before anything beyond it, so what you are LOOKING at is
            // always complete and the thing that goes short is a wall down the road, which is
            // the right thing to lose.
            //
            // Newest first inside each ring, which is backwards through the list -- among marks
            // you can equally see, the most recent work is what should survive.
            var stuck = Restore(me, 0f, CloseUp * CloseUp);

            stuck += Restore(me, CloseUp * CloseUp, restore);

            // SAID WHEN IT CHANGES, not every sweep. Paint that is near enough to be on the
            // wall and is not is the one thing this class exists to prevent, and "some of my
            // tag is missing" is impossible to act on without a number.
            if (stuck != _stuck)
            {
                _stuck = stuck;

                if (stuck > 0)
                {
                    Log.Info(stuck + " mark(s) near you cannot get on the wall -- the game's " +
                             "decal pool is full. Nothing is lost; they go back up as you " +
                             "move and free slots.");
                }
            }
        }

        /// <summary>
        /// Put back every mark whose distance falls in a band, newest first.
        ///
        /// Squared distances in, like everything else in here. Returns how many wanted a slot
        /// and did not get one.
        /// </summary>
        private int Restore(Vector3 me, float fromSq, float toSq)
        {
            var stuck = 0;

            for (var i = _marks.Count - 1; i >= 0; i--)
            {
                var m = _marks[i];

                if (!m.Away) continue;

                var d = me.DistanceToSquared(m.At);

                if (d < fromSq || d >= toSq) continue;

                m.Handle = Place(m);

                // THE SAME FALLBACK PLACING HAS ALWAYS HAD, and its absence here is why tags
                // came back cut off and why old ones stopped coming back at all.
                //
                // Put asks Recycle for a slot when the pool refuses; this did not. So the
                // restore filled the pool with whatever it reached first and then every
                // remaining mark failed, stayed away, and failed again on the next sweep.
                // Walking up to an old tag with a full pool did nothing at all, because
                // nothing was ever asked to make room for it.
                //
                // Recycle only takes from marks a good margin further away than this one, so
                // the nearest paint wins and two marks cannot evict each other.
                if (m.Handle == 0 && Recycle(m.At)) m.Handle = Place(m);

                m.Away = m.Handle == 0;

                if (m.Away) stuck++;
            }

            return stuck;
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
