using System;
using GTA;
using Trapline.Core;
using Trapline.Economy;
using Trapline.UI;

namespace Trapline.State
{
    /// <summary>
    /// Everything Trapline persists about the player between sessions.
    ///
    /// Cash is deliberately NOT stored here -- the game already owns the player's money and
    /// duplicating it would drift. Only Trapline's own progression lives in this file.
    /// </summary>
    internal sealed class PlayerState
    {
        /// <summary>Respect thresholds for ranks 0..4.</summary>
        private static readonly float[] RankThresholds = { 0f, 250f, 900f, 2400f, 6000f };

        public static readonly string[] RankNames = { "Pee-Wee", "Soldier", "Enforcer", "Shotcaller", "OG" };

        public readonly Inventory Inventory = new Inventory();

        public float Respect;

        /// <summary>Rival/police attention, 0..100. Decays over time.</summary>
        public float Notoriety;

        public int TotalDealsMade;
        public long TotalEarned;

        private bool _dirty;

        /// <summary>Marks the state as needing a save on the next autosave tick.</summary>
        public void Touch() => _dirty = true;

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
                Notify.Ticker("~y~Rank up:~s~ " + RankName);
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

        public static PlayerState LoadOrNew(Settings cfg)
        {
            var state = new PlayerState { Respect = cfg.StartingRespect };

            var doc = JsonFile.Read(Paths.SaveFile);
            if (doc == null)
            {
                Log.Info("No save found; starting fresh at rank 0.");
                return state;
            }

            try
            {
                state.Respect = Math.Max(0f, doc["respect"].AsFloat(state.Respect));
                state.Notoriety = Math.Min(100f, Math.Max(0f, doc["notoriety"].AsFloat(0f)));
                state.TotalDealsMade = Math.Max(0, doc["totalDeals"].AsInt(0));
                state.TotalEarned = Math.Max(0L, doc["totalEarned"].AsLong(0));
                state.Inventory.LoadFrom(doc["inventory"]);

                Log.Info("Save loaded: rank " + state.Rank + " (" + state.RankName + "), " +
                         state.Respect.ToString("F0") + " respect, " +
                         state.Inventory.Total.ToString("F1") + "g held.");
            }
            catch (Exception ex)
            {
                Log.Error("Save file was unreadable; continuing with defaults.", ex);
            }

            return state;
        }

        /// <summary>Writes the save. <paramref name="force"/> bypasses the dirty check.</summary>
        public bool Save(bool force = false)
        {
            if (!_dirty && !force) return false;

            var doc = Json.Object()
                .Set("version", Build.Version)
                .Set("respect", Math.Round(Respect, 2))
                .Set("notoriety", Math.Round(Notoriety, 2))
                .Set("totalDeals", TotalDealsMade)
                .Set("totalEarned", TotalEarned)
                .Set("inventory", Inventory.ToJson());

            if (!JsonFile.Write(Paths.SaveFile, doc)) return false;

            _dirty = false;
            return true;
        }
    }
}
