using System;
using System.Collections.Generic;
using GTA;
using Hoodrich.Core;

namespace Hoodrich.Economy
{
    /// <summary>
    /// Street prices that move on their own.
    ///
    /// Each product carries a drift multiplier that takes a random step every few minutes
    /// inside a fixed band. It is a random walk rather than a re-roll, so prices wander instead
    /// of teleporting -- which is what turns dealing into watching a market: hold the coke
    /// while it climbs, dump the weed before it slides.
    /// </summary>
    internal sealed class Market
    {
        private sealed class Drift
        {
            public float Value = 1f;
            public float Previous = 1f;
        }

        private readonly Dictionary<string, Drift> _drift =
            new Dictionary<string, Drift>(StringComparer.OrdinalIgnoreCase);

        private readonly Random _rng = new Random();
        private readonly Settings _cfg;

        private int _lastStep;

        public Market(Settings cfg)
        {
            _cfg = cfg;
        }

        private float Low => 1f - _cfg.MarketMaxSwingPercent / 100f;
        private float High => 1f + _cfg.MarketMaxSwingPercent / 100f;

        /// <summary>Current price multiplier for a product. 1.0 until the market has moved.</summary>
        public float Multiplier(string drugId)
        {
            if (string.IsNullOrEmpty(drugId)) return 1f;
            return _drift.TryGetValue(drugId, out var d) ? d.Value : 1f;
        }

        /// <summary>+1 rising, -1 falling, 0 flat since the last step. For the status board.</summary>
        public int Trend(string drugId)
        {
            if (!_drift.TryGetValue(drugId, out var d)) return 0;
            var delta = d.Value - d.Previous;
            if (Math.Abs(delta) < 0.005f) return 0;
            return delta > 0f ? 1 : -1;
        }

        /// <summary>Arrow plus percentage, ready to drop into a panel row.</summary>
        public string TrendLabel(string drugId)
        {
            var mult = Multiplier(drugId);
            var pct = (mult - 1f) * 100f;
            var arrow = Trend(drugId) > 0 ? "^" : Trend(drugId) < 0 ? "v" : "-";
            return arrow + " " + (pct >= 0f ? "+" : "") + pct.ToString("0") + "%";
        }

        public void Update(Drugs catalogue)
        {
            if (_cfg.MarketDriftIntervalMinutes <= 0f) return;

            var now = Game.GameTime;
            var intervalMs = (int)(_cfg.MarketDriftIntervalMinutes * 60_000f);
            if (_lastStep != 0 && now - _lastStep < intervalMs) return;

            _lastStep = now;

            foreach (var drug in catalogue.All)
            {
                if (!_drift.TryGetValue(drug.Id, out var d))
                {
                    d = new Drift();
                    _drift[drug.Id] = d;
                }

                d.Previous = d.Value;

                // A step of up to a third of the band, either way, then clamped.
                var span = (High - Low) / 3f;
                var step = (float)(_rng.NextDouble() * 2.0 - 1.0) * span;

                // Gentle pull back toward 1.0 so a product cannot park at an extreme forever.
                var pull = (1f - d.Value) * 0.15f;

                d.Value = Clamp(d.Value + step + pull, Low, High);
            }

            Log.Debug("Market stepped.");
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;

        // ---- persistence -------------------------------------------------------

        public Json ToJson()
        {
            var obj = Json.Object();
            foreach (var kv in _drift) obj.Set(kv.Key, Math.Round(kv.Value.Value, 4));
            return obj;
        }

        public void LoadFrom(Json node)
        {
            _drift.Clear();
            if (node == null || node.IsNull) return;

            foreach (var key in node.Keys)
            {
                var v = Clamp(node[key].AsFloat(1f), 0.2f, 3f);
                _drift[key] = new Drift { Value = v, Previous = v };
            }
        }
    }
}
