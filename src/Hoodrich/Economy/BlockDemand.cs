using System;
using System.Collections.Generic;
using GTA;
using Hoodrich.Core;

namespace Hoodrich.Economy
{
    /// <summary>
    /// A block you have been working stops being worth working.
    ///
    /// The market already moves prices city-wide, per product. Nothing moved with WHERE you
    /// were standing, so the map had no economic opinion at all: find one good corner, and the
    /// correct play for the rest of the save was to never leave it. Ninetyseven zones and only
    /// one of them ever mattered, which is a lot of city to build and then give the player a
    /// reason to ignore.
    ///
    /// This is the reason to move. Every gram sold on a block makes the next sale on that block
    /// a little slower to arrive, and standing somewhere else lets it recover. It is not a
    /// punishment for dealing -- it is the difference between a corner and a route.
    ///
    /// IT MOVES CUSTOMERS, NOT PRICES, and that distinction is the whole design. Demand in this
    /// mod is how often somebody walks up, never what they hand over; an eighth is an eighth's
    /// money whoever buys it. A worked-out block goes quiet. It does not start haggling.
    /// </summary>
    internal sealed class BlockDemand
    {
        /// <summary>How much has moved here lately.</summary>
        private sealed class Block
        {
            public float Worked;
        }

        /// <summary>
        /// Don't recalculate sixty times a second for a number that changes over minutes.
        /// </summary>
        private const int TickMs = 4000;

        private readonly Dictionary<string, Block> _blocks =
            new Dictionary<string, Block>(StringComparer.OrdinalIgnoreCase);

        private readonly Settings _cfg;

        private int _last;

        public BlockDemand(Settings cfg)
        {
            _cfg = cfg;
        }

        private float Half => Math.Max(1f, _cfg.BlockSaturationGrams);
        private float Floor => Clamp(_cfg.BlockDemandFloor, 0.05f, 1f);

        /// <summary>
        /// The most saturated a block can get: exactly the weight that reaches the floor.
        ///
        /// TWO REASONS, and the second one is why the number is this and not a round multiple.
        /// Without any ceiling, dumping a kilo in one place would leave that block dead for
        /// hours -- recovery is a flat rate, so an unbounded number is an unbounded wait, and
        /// the player would have permanently ruined a zone by doing the thing the mod is about.
        ///
        /// But the ceiling belongs at the FLOOR. Past that point more weight changes nothing
        /// anybody can observe, because the multiplier is already as low as it goes; all the
        /// extra does is lengthen the wait. Banking saturation that has no effect except to
        /// keep you away longer is a punishment with no signal attached, which is the worst
        /// kind there is. At the shipped numbers this is 750g, and an hour to clear it.
        /// </summary>
        private float Cap => Half * (1f / Floor - 1f);

        /// <summary>
        /// What this block is worth right now, as a multiplier on how often somebody walks up.
        ///
        /// 1.0 on a block nobody has worked. Half at BlockSaturationGrams, which is what makes
        /// that setting readable: it is the weight it takes to halve the trade. Then it keeps
        /// falling toward the floor rather than to zero, because a quiet corner is quiet and
        /// not closed -- there is always somebody.
        /// </summary>
        public float Multiplier(string zone)
        {
            if (string.IsNullOrEmpty(zone) || !_cfg.BlockSaturationEnabled) return 1f;

            Block b;
            if (!_blocks.TryGetValue(zone, out b) || b.Worked <= 0f) return 1f;

            var m = 1f / (1f + b.Worked / Half);
            return m < Floor ? Floor : m;
        }

        /// <summary>
        /// How worked over this block is, 0 to 1.
        ///
        /// DERIVED FROM THE MULTIPLIER rather than from the raw weight, so that what the player
        /// is told and what the player experiences are the same fact. Measured off the weight,
        /// it went on saying "worked over" and then "dried up" long after the trade had stopped
        /// getting any slower -- two labels, one behaviour, and a player entirely justified in
        /// concluding the words meant nothing.
        /// </summary>
        public float Saturation(string zone)
        {
            var m = Multiplier(zone);
            if (m >= 1f) return 0f;

            var s = (1f - m) / Math.Max(0.01f, 1f - Floor);
            return s < 0f ? 0f : s > 1f ? 1f : s;
        }

        /// <summary>
        /// A plain description, because a hidden penalty reads as a bug.
        ///
        /// THE THRESHOLDS ARE SPACED IN GRAMS, NOT IN SATURATION, even though saturation is
        /// what they are compared against. The curve is steep at the start -- the first ounce
        /// moves it further than the fifth does -- so evenly spaced cutoffs bunched every label
        /// into the first hundred grams and had the wheel reporting "slowing" after two ounces,
        /// which is true to a decimal place and a lie in every way that matters.
        ///
        /// At the shipped numbers these land at roughly: fresh under 45g, steady under 120g,
        /// slowing under 220g, worked over under 480g, dried beyond it. An ounce or two changes
        /// nothing you would notice. Camping does.
        /// </summary>
        public string Word(string zone)
        {
            var s = Saturation(zone);

            if (s < 0.20f) return "fresh";
            if (s < 0.42f) return "steady";
            if (s < 0.62f) return "slowing";
            if (s < 0.86f) return "worked over";
            return "dried up";
        }

        /// <summary>Weight went out on this block.</summary>
        public void Sold(string zone, float grams)
        {
            if (string.IsNullOrEmpty(zone) || grams <= 0f) return;

            Block b;
            if (!_blocks.TryGetValue(zone, out b))
            {
                b = new Block();
                _blocks[zone] = b;
            }

            b.Worked += grams;

            var cap = Cap;
            if (b.Worked > cap) b.Worked = cap;
        }

        /// <summary>
        /// Blocks cool off while you are not on them.
        ///
        /// On the wall clock rather than the game clock, deliberately. The recovery a player is
        /// judging is "how long until I can come back", and they measure that in how long they
        /// have been away -- which is minutes of their evening, not whatever the sky is doing.
        /// </summary>
        public void Update()
        {
            var now = Game.GameTime;

            if (_last == 0)
            {
                _last = now;
                return;
            }

            var elapsed = now - _last;
            if (elapsed < TickMs) return;

            _last = now;

            if (_blocks.Count == 0) return;

            // A block sitting exactly on the half-demand mark comes all the way back in
            // BlockRecoveryMinutes. Everything else follows from that at the same rate.
            var perMs = Half / Math.Max(1000f, _cfg.BlockRecoveryMinutes * 60_000f);
            var off = perMs * elapsed;

            List<string> cooled = null;

            foreach (var kv in _blocks)
            {
                kv.Value.Worked -= off;

                if (kv.Value.Worked > 0f) continue;

                // Gone entirely rather than left at zero, so the dictionary does not grow a
                // permanent entry for every block the player has ever sold a single gram on.
                if (cooled == null) cooled = new List<string>();
                cooled.Add(kv.Key);
            }

            if (cooled == null) return;

            for (var i = 0; i < cooled.Count; i++) _blocks.Remove(cooled[i]);
        }

        /// <summary>Everything back on the table.</summary>
        public void Clear()
        {
            _blocks.Clear();
        }

        private static float Clamp(float v, float lo, float hi)
        {
            return v < lo ? lo : v > hi ? hi : v;
        }

        // ---- persistence -------------------------------------------------------

        public Json ToJson()
        {
            var obj = Json.Object();
            foreach (var kv in _blocks) obj.Set(kv.Key, Math.Round(kv.Value.Worked, 2));
            return obj;
        }

        public void LoadFrom(Json node)
        {
            _blocks.Clear();
            if (node == null || node.IsNull) return;

            var cap = Cap;

            foreach (var key in node.Keys)
            {
                var v = node[key].AsFloat(0f);
                if (v <= 0f) continue;

                _blocks[key] = new Block { Worked = v > cap ? cap : v };
            }
        }
    }
}
