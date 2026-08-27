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
        public float R, G, B;

        /// <summary>The handle the game last gave it, so a stale one can be cleaned up.</summary>
        public int Handle;

        /// <summary>True once the player has been far enough away for the game to drop it.</summary>
        public bool Away;
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
        private bool _proved;

        public Marks(PaintConfig cfg)
        {
            _cfg = cfg;
        }

        public int Count => _marks.Count;

        /// <summary>Which decal the install actually has, once something has placed.</summary>
        public int TypeInUse => _type;

        /// <summary>Whether the one that takes colour properly is the one being used.</summary>
        public bool ColourIsHonest => _type == 1030 || _type == 0;

        /// <summary>Puts one on the wall and remembers it.</summary>
        public void Put(Vector3 at, Vector3 into, Vector3 side, float size,
                        float r, float g, float b)
        {
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
                Size = size, R = r, G = g, B = b
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
                    Log.Warn("Nothing will place. Either the pool is full or this install has " +
                             "none of the paint decals.");
                }

                return;
            }

            _refused = 0;
            _marks.Add(mark);
        }

        /// <summary>Puts one up, trying each decal type until the game accepts one.</summary>
        private int Place(Mark m)
        {
            for (var i = 0; i < Types.Length; i++)
            {
                var type = Types[i];

                // Once one has worked, stop asking the others -- every refusal is a wasted call
                // and there are a lot of these.
                if (_type > 0 && type != _type) continue;

                int handle;

                try
                {
                    handle = Function.Call<int>(Hash.ADD_DECAL, type,
                                                m.At.X, m.At.Y, m.At.Z,
                                                m.Into.X, m.Into.Y, m.Into.Z,
                                                m.Side.X, m.Side.Y, m.Side.Z,
                                                m.Size, m.Size,
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

                if (_type != type)
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

            var worst = -1;
            var worstD = -1f;

            for (var i = 0; i < _marks.Count; i++)
            {
                var m = _marks[i];

                if (m.Away || m.Handle == 0) continue;

                var d = me.DistanceTo(m.At);
                if (d <= worstD) continue;

                worstD = d;
                worst = i;
            }

            if (worst < 0) return false;
            if (worstD <= me.DistanceTo(near) + 1f) return false;

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

            for (var i = 0; i < _marks.Count; i++)
            {
                var m = _marks[i];
                var d = me.DistanceTo(m.At);

                if (!m.Away && d > FarEnough)
                {
                    Wipe(m);
                    m.Away = true;
                    continue;
                }

                if (m.Away && d < NearEnough)
                {
                    m.Handle = Place(m);
                    m.Away = m.Handle == 0;
                }
            }
        }

        /// <summary>Everything gone, off the wall and out of the record.</summary>
        public void Clear()
        {
            for (var i = 0; i < _marks.Count; i++) Wipe(_marks[i]);
            _marks.Clear();

            Log.Info("Wiped every mark.");
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

                arr.Add(Json.Object()
                    .Set("x", Math.Round(m.At.X, 3)).Set("y", Math.Round(m.At.Y, 3)).Set("z", Math.Round(m.At.Z, 3))
                    .Set("ix", Math.Round(m.Into.X, 3)).Set("iy", Math.Round(m.Into.Y, 3)).Set("iz", Math.Round(m.Into.Z, 3))
                    .Set("sx", Math.Round(m.Side.X, 3)).Set("sy", Math.Round(m.Side.Y, 3)).Set("sz", Math.Round(m.Side.Z, 3))
                    .Set("w", Math.Round(m.Size, 3))
                    .Set("r", Math.Round(m.R, 3)).Set("g", Math.Round(m.G, 3)).Set("b", Math.Round(m.B, 3)));
            }

            var doc = Json.Object();
            doc.Set("decal", _type);
            doc.Set("marks", arr);
            return doc;
        }

        public void LoadFrom(Json doc)
        {
            Clear();
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
                    R = node["r"].AsFloat(1f),
                    G = node["g"].AsFloat(1f),
                    B = node["b"].AsFloat(1f)
                };

                // Left off the wall. The sweep puts back whatever is near enough to matter,
                // which on a load is usually none of it.
                m.Away = true;
                _marks.Add(m);
            }

            Log.Info("Loaded " + _marks.Count + " marks.");
        }
    }
}
