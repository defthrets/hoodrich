using System;
using System.Collections.Generic;
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
        /// <summary>Respect needed to reach each rank, 0..4. Read by the Reputation page.</summary>
        public static readonly float[] RankThresholds = { 0f, 250f, 900f, 2400f, 6000f };

        public static readonly string[] RankNames = { "Pee-Wee", "Soldier", "Enforcer", "Shotcaller", "OG" };

        public readonly Stash Stash = new Stash();

        public float Respect;

        /// <summary>Rival/police attention, 0..100. Decays over time.</summary>
        public float Notoriety;

        public int TotalDealsMade;
        public long TotalEarned;

        /// <summary>Total grams moved hand-to-hand. Drives the docks unlock.</summary>
        public float GramsSold;

        /// <summary>
        /// Set once the player has asked their gang's corner dealer where he sources from.
        /// Until then the docks do not exist for them and their crew's dealer is the only
        /// way to buy -- which is the whole shape of the early game.
        /// </summary>
        public bool DocksUnlocked;

        /// <summary>
        /// True when the last thing you did was sleep at the stash house.
        ///
        /// The game puts Franklin back at whichever house it thinks is his, which after the
        /// story is the one in the hills -- so sleeping at Aunt Denise's and loading back in
        /// dropped you across the map from everything the mod is about.
        /// </summary>
        public bool SleptAtStashHouse;

        /// <summary>
        /// People who follow you.
        ///
        /// Saved, because it is the one number in the mod that only ever reflects what you have
        /// actually done -- respect can be ground out on a corner, but nobody follows you for
        /// standing still.
        /// </summary>
        public int Followers;

        /// <summary>Raised when a rank is crossed, so the block can notice.</summary>
        public Action<int> RankedUp;

        /// <summary>
        /// Jobs finished for Lamar, by id.
        ///
        /// He works through his list in order and only opens up the choice once you have been
        /// through all of it, so what matters is which ones are behind you rather than how many.
        /// Ids rather than a count, so reordering or adding a job does not silently re-lock work
        /// somebody has already done.
        /// </summary>
        public readonly List<string> MissionsDone = new List<string>();

        public bool HasDone(string missionId)
        {
            if (string.IsNullOrEmpty(missionId)) return false;

            foreach (var id in MissionsDone)
            {
                if (string.Equals(id, missionId, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        /// <summary>Forgets every job, so Lamar works down his list from the top again.</summary>
        public void ForgetMissions()
        {
            MissionsDone.Clear();
            Touch();
        }

        public void MarkDone(string missionId)
        {
            if (string.IsNullOrEmpty(missionId) || HasDone(missionId)) return;

            MissionsDone.Add(missionId);
            Touch();
        }

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

                // Crossing a threshold is an event with a moment attached rather than something
                // a later tick notices, so whoever cares is told here.
                if (RankedUp != null) RankedUp(after);
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

        private Json MissionsJson()
        {
            var arr = Json.Array();
            foreach (var id in MissionsDone) arr.Add(Json.Str(id));
            return arr;
        }

        public Json ToJson()
        {
            return Json.Object()
                .Set("respect", Math.Round(Respect, 2))
                .Set("notoriety", Math.Round(Notoriety, 2))
                .Set("totalDeals", TotalDealsMade)
                .Set("totalEarned", TotalEarned)
                .Set("gramsSold", Math.Round(GramsSold, 2))
                .Set("docksUnlocked", DocksUnlocked)
                .Set("sleptAtStashHouse", SleptAtStashHouse)
                .Set("followers", Followers)
                .Set("missionsDone", MissionsJson())
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
                GramsSold = Math.Max(0f, doc["gramsSold"].AsFloat(0f));
                DocksUnlocked = doc["docksUnlocked"].AsBool(false);
                SleptAtStashHouse = doc["sleptAtStashHouse"].AsBool(false);
                Followers = Math.Max(0, doc["followers"].AsInt(0));

                MissionsDone.Clear();
                foreach (var node in doc["missionsDone"].Items)
                {
                    var id = node.AsString("");
                    if (!string.IsNullOrEmpty(id) && !HasDone(id)) MissionsDone.Add(id);
                }

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
