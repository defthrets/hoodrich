using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using Hoodrich.Core;

namespace Hoodrich.Dealing
{
    /// <summary>
    /// Heat that belongs to a PLACE rather than to a session of standing still.
    ///
    /// It used to belong to the pitch: post up, work the corner until the police came, pack
    /// up, take two steps and post up again with a clean slate. That is not a corner getting
    /// hot, it is a counter being reset -- and it made the whole heat system optional, because
    /// the way to beat it was to press the same button twice.
    ///
    /// So the heat stays on the ground. Stand anywhere within RADIUS of a block you have
    /// already cooked and you inherit what you left there. Getting away from it means actually
    /// getting away from it -- a different street, not a different paving slab.
    ///
    /// It does cool, slowly, on a half-life. A city where every corner you ever worked stays
    /// permanently hot is a city that runs out of places to work, and "come back later" should
    /// be an answer as well as "go somewhere else".
    /// </summary>
    internal sealed class CornerHeat
    {
        /// <summary>
        /// How far a hot block reaches.
        ///
        /// Sixty metres is a couple of shopfronts either way -- far enough that moving down to
        /// the next doorway is obviously the same corner, close enough that the far end of the
        /// street is genuinely somewhere else.
        /// </summary>
        public const float Radius = 60f;

        /// <summary>How long a block takes to lose half its heat, in real milliseconds.</summary>
        private const float HalfLifeMs = 330000f;

        /// <summary>Below this a block is simply forgotten rather than tracked at nothing.</summary>
        private const float Floor = 0.30f;

        /// <summary>Decay is a per-block calculation, so it is not worth doing every frame.</summary>
        private const int TickMs = 1500;

        /// <summary>
        /// A cap on how many blocks are remembered at once.
        ///
        /// Nobody works more than a handful of corners in a session, and without a ceiling this
        /// is a list that only ever grows -- every pitch you ever stood on, kept forever, and
        /// scanned on every post-up.
        /// </summary>
        private const int MostBlocks = 24;

        private sealed class Block
        {
            public Vector3 At;
            public float Heat;
        }

        private readonly List<Block> _blocks = new List<Block>();

        private int _lastTick;

        /// <summary>How hot the ground already is where you are about to stand.</summary>
        public float At(Vector3 where)
        {
            var worst = 0f;

            foreach (var b in _blocks)
            {
                if (b.At.DistanceTo2D(where) > Radius) continue;
                if (b.Heat > worst) worst = b.Heat;
            }

            return worst;
        }

        /// <summary>Whether somewhere is already warm enough to be worth mentioning.</summary>
        public bool IsWarm(Vector3 where)
        {
            return At(where) > 1f;
        }

        /// <summary>
        /// Writes what a pitch has built up back onto the ground under it.
        ///
        /// Merged into whatever block is already there rather than added as a new one, or a
        /// long session on one corner would leave a trail of overlapping entries down the
        /// pavement and the hottest of them would be whichever the scan happened to hit first.
        /// </summary>
        public void Remember(Vector3 where, float heat)
        {
            if (heat < Floor) return;

            Block near = null;
            var nearest = Radius;

            foreach (var b in _blocks)
            {
                var away = b.At.DistanceTo2D(where);
                if (away > nearest) continue;

                nearest = away;
                near = b;
            }

            if (near != null)
            {
                // The worse of the two. Walking to the quiet end of a hot block does not make
                // the block quieter.
                if (heat > near.Heat) near.Heat = heat;
                return;
            }

            if (_blocks.Count >= MostBlocks) Coolest();

            _blocks.Add(new Block { At = where, Heat = heat });
        }

        /// <summary>Drops the least interesting block, to keep the list bounded.</summary>
        private void Coolest()
        {
            var at = -1;
            var worst = float.MaxValue;

            for (var i = 0; i < _blocks.Count; i++)
            {
                if (_blocks[i].Heat >= worst) continue;

                worst = _blocks[i].Heat;
                at = i;
            }

            if (at >= 0) _blocks.RemoveAt(at);
        }

        /// <summary>Everything cools while you are elsewhere.</summary>
        public void Tick()
        {
            var now = Game.GameTime;
            if (_lastTick == 0) { _lastTick = now; return; }

            var since = now - _lastTick;
            if (since < TickMs) return;

            _lastTick = now;

            var keep = (float)Math.Pow(0.5, since / HalfLifeMs);

            for (var i = _blocks.Count - 1; i >= 0; i--)
            {
                _blocks[i].Heat *= keep;
                if (_blocks[i].Heat < Floor) _blocks.RemoveAt(i);
            }
        }

        /// <summary>Wipes the lot. For the settings screen's reset.</summary>
        public void Forget()
        {
            _blocks.Clear();
            Log.Info("Corner heat forgotten.");
        }

        /// <summary>How many blocks are currently warm, for the readouts.</summary>
        public int WarmBlocks => _blocks.Count;
    }
}
