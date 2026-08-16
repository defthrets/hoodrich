using System;
using Hoodrich.Core;
using Hoodrich.Economy;
using Hoodrich.UI;

namespace Hoodrich.State
{
    /// <summary>
    /// Hoodrich's own progression. Cash is deliberately NOT stored here -- the game already
    /// owns the player's money and duplicating it would drift.
    /// </summary>
    internal sealed class PlayerState
    {
        /// <summary>Respect thresholds for ranks 0..4.</summary>
        private static readonly float[] RankThresholds = { 0f, 250f, 900f, 2400f, 6000f };

        public static readonly string[] RankNames = { "Pee-Wee", "Soldier", "Enforcer", "Shotcaller", "OG" };

        public readonly Stash Stash = new Stash();

        public float Respect;

        /// <summary>Rival/police attention, 0..100. Decays over time.</summary>
        public float Notoriety;

        public int TotalDealsMade;
        public long TotalEarned;

        private bool _dirty;

        public bool IsDirty => _dirty;

        /// <summary>Marks the state as needing a save on the next autosave tick.</summary>
        public void Touch() => _dirty = true;

        public void MarkSaved() => _dirty = false;

        public int Rank
        {
            get
            {
                var rank = 0;
                for (var i = RankThresholds.Length - 1; i >= 0; i--)
                {
                    if (Respect >= RankThresholds[i]) { rank = i; break; }
                }
                return rank;
            }
        }

        public string RankName => RankNames[Math.Min(Rank, RankNames.Length - 1)];

        /// <summary>Progress toward the next rank, 0..1. Returns 1 at max rank.</summary>
        public float RankProgress
        {
            get
            {
                var rank = Rank;
                if (rank >= RankThresholds.Length - 1) return 1f;

                var lo = RankThresholds[rank];
                var hi = RankThresholds[rank + 1];
                if (hi <= lo) return 1f;
                return Math.Min(1f, Math.Max(0f, (Respect - lo) / (hi - lo)));
            }
        }

        public void AddRespect(float amount)
        {
            if (Math.Abs(amount) < 0.0001f) return;

            var before = Rank;
            Respect = Math.Max(0f, Respect + amount);
            Touch();

            var after = Rank;
            if (after > before)
            {
                Notify.Important("~y~Rank up:~s~ " + RankName);
                Log.Info("Rank up to " + after + " (" + RankName + ") at " + Respect.ToString("F0") + " respect.");
            }
            else if (after < before)
            {
                Notify.Ticker("~r~Rank down:~s~ " + RankName);
            }
        }

        public void AddNotoriety(float amount)
        {
            Notoriety = Math.Min(100f, Math.Max(0f, Notoriety + amount));
            Touch();
        }

        // ---- persistence -------------------------------------------------------

        public Json ToJson()
        {
            return Json.Object()
                .Set("respect", Math.Round(Respect, 2))
                .Set("notoriety", Math.Round(Notoriety, 2))
                .Set("totalDeals", TotalDealsMade)
                .Set("totalEarned", TotalEarned)
                .Set("stash", Stash.ToJson());
        }

        public void LoadFrom(Json doc)
        {
            if (doc == null || doc.IsNull) return;

            try
            {
                Respect = Math.Max(0f, doc["respect"].AsFloat(Respect));
                Notoriety = Math.Min(100f, Math.Max(0f, doc["notoriety"].AsFloat(0f)));
                TotalDealsMade = Math.Max(0, doc["totalDeals"].AsInt(0));
                TotalEarned = Math.Max(0L, doc["totalEarned"].AsLong(0));

                // "inventory" is the 0.1.0 key; migrate it so old saves keep their product.
                Stash.LoadFrom(doc.Has("stash") ? doc["stash"] : doc["inventory"]);

                Log.Info("State loaded: rank " + Rank + " (" + RankName + "), " +
                         Respect.ToString("F0") + " respect, " +
                         Stash.TotalBulk.ToString("F1") + "g bulk / " +
                         Stash.TotalPackaged.ToString("F1") + "g packaged.");
            }
            catch (Exception ex)
            {
                Log.Error("Save file was unreadable; continuing with defaults.", ex);
            }
        }
    }
}
