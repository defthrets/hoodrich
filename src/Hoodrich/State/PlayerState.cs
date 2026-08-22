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
        /// What the block reckons your product is like. 0..1, and it starts in the middle.
        ///
        /// Neutral, not perfect. Nobody has bought anything off you yet, so there is no reason
        /// for the corner to think you are good OR bad -- you have not got a name. That makes
        /// the bar mean something in both directions from the first sale: selling clean earns
        /// a name you did not have, and selling rubbish costs you one before you ever had it.
        ///
        /// This is the missing half of the purity system. Pricing has always said that nobody
        /// knocks money off for a weak gram -- "they take it, clock it, and stop coming" -- and
        /// the taking and clocking were built while the stop-coming never was. So cutting had
        /// an unbounded reward against a capped, non-destructive penalty, and the arithmetic
        /// said cut everything to the floor, forever, for every product.
        ///
        /// It drifts toward whatever you have actually been selling and it drives DEMAND, not
        /// price. Push garbage and the corner goes quiet, which costs you the one thing cutting
        /// was supposed to be buying: units moved per hour standing out there.
        /// </summary>
        public float ProductRep = Neutral;

        /// <summary>No name either way. Where everybody starts.</summary>
        public const float Neutral = 0.5f;

        /// <summary>How fast one sale moves the block's opinion.</summary>
        private const float RepDriftPerSale = 0.06f;

        /// <summary>And how fast a refusal does, which is faster. Bad news travels.</summary>
        private const float RepDriftPerRefusal = 0.11f;

        /// <summary>
        /// Records a sale that landed, at the purity it went out at.
        /// </summary>
        public void SoldAt(float purity)
        {
            Drift(purity, RepDriftPerSale);
        }

        /// <summary>
        /// Records somebody handing it back.
        ///
        /// Worse than a quiet sale at the same purity, because a refusal is a person who now
        /// tells other people. Half the purity, drifted harder.
        /// </summary>
        public void RefusedAt(float purity)
        {
            Drift(purity * 0.5f, RepDriftPerRefusal);
        }

        private void Drift(float towards, float rate)
        {
            if (towards < 0f) towards = 0f;
            if (towards > 1f) towards = 1f;

            ProductRep += (towards - ProductRep) * rate;

            if (ProductRep < 0.1f) ProductRep = 0.1f;
            if (ProductRep > 1f) ProductRep = 1f;
        }

        /// <summary>
        /// How the block would put it.
        ///
        /// Banded around the neutral middle rather than down from a perfect top, so the words
        /// either side of where you start are the two things that can happen to you next.
        /// </summary>
        public string ProductRepWord
        {
            get
            {
                if (ProductRep >= 0.88f) return "they trust your work";
                if (ProductRep >= 0.72f) return "known for good product";
                if (ProductRep >= 0.58f) return "word is it's decent";
                if (ProductRep >= 0.42f) return "you ain't got a name yet";
                if (ProductRep >= 0.30f) return "word is you step on it";
                if (ProductRep >= 0.18f) return "they say you sell garbage";
                return "nobody wants your product";
            }
        }

        /// <summary>True while the block has not made its mind up either way.</summary>
        public bool ProductRepIsNeutral => ProductRep >= 0.42f && ProductRep < 0.58f;

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
        /// A package Stretch has fronted you, and where your sales counter stood when he did.
        ///
        /// Held here rather than in the stash, because the stash cannot tell his grams from
        /// yours and should not have to -- once it is in the bag it is just product. What makes
        /// it his is the promise, and this is the promise.
        /// </summary>
        public string FrontedDrug = "";
        public float FrontedGrams;
        public float FrontedAtGrams;

        public bool HasFrontedWork => !string.IsNullOrEmpty(FrontedDrug) && FrontedGrams > 0f;

        /// <summary>How much of his you have shifted since he handed it over.</summary>
        public float FrontedMoved => Math.Max(0f, GramsSold - FrontedAtGrams);

        /// <summary>Whether the package is gone and he owes you for it.</summary>
        public bool FrontedWorkDone => HasFrontedWork && FrontedMoved >= FrontedGrams - 0.001f;

        public void ClearFronted()
        {
            FrontedDrug = "";
            FrontedGrams = 0f;
            FrontedAtGrams = 0f;
        }

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

        /// <summary>
        /// Back to nobody: no respect, no rank, no record of what you have moved.
        ///
        /// Rank is derived from Respect rather than stored, so putting the one back to nothing
        /// puts the other back with it -- there is no second field to forget and no way for the
        /// two to disagree.
        ///
        /// ProductRep goes to Neutral rather than to zero. Zero is not "unknown", it is the
        /// worst possible name a man can have, and a reset that leaves the block believing you
        /// sell chalk is not a reset.
        /// </summary>
        public void ForgetName()
        {
            Respect = 0f;
            Notoriety = 0f;

            TotalDealsMade = 0;
            TotalEarned = 0L;
            GramsSold = 0f;

            ProductRep = Neutral;

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

        /// <summary>
        /// Whether the first-run guide has been shown.
        ///
        /// Saved, so it is once per save rather than once per session. A player who reloads
        /// should not be told what a corner is again, and a NEW save should be -- which is the
        /// same thing as saying this lives with the character rather than with the install.
        /// </summary>
        public bool SeenWelcome;

        public Json ToJson()
        {
            return Json.Object()
                .Set("seenWelcome", SeenWelcome)
                .Set("respect", Math.Round(Respect, 2))
                .Set("notoriety", Math.Round(Notoriety, 2))
                .Set("totalDeals", TotalDealsMade)
                .Set("totalEarned", TotalEarned)
                .Set("gramsSold", Math.Round(GramsSold, 2))
                .Set("productRep", Math.Round(ProductRep, 3))
                .Set("docksUnlocked", DocksUnlocked)
                .Set("sleptAtStashHouse", SleptAtStashHouse)
                .Set("followers", Followers)
                .Set("frontedDrug", FrontedDrug)
                .Set("frontedGrams", FrontedGrams)
                .Set("frontedAtGrams", FrontedAtGrams)
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

                // Saves from before the block had an opinion start neutral rather than at
                // zero, which would read as a bad name they were never given the chance to
                // earn -- or at one, which would be a good one they never earned either.
                ProductRep = Math.Min(1f, Math.Max(0.1f, doc["productRep"].AsFloat(Neutral)));
                DocksUnlocked = doc["docksUnlocked"].AsBool(false);
                SleptAtStashHouse = doc["sleptAtStashHouse"].AsBool(false);

                // Defaults to FALSE, so a save from before this existed shows the guide once
                // and then never again. That is the right way round: somebody who has been
                // playing already loses nothing by being told, and somebody new needs it.
                SeenWelcome = doc["seenWelcome"].AsBool(false);
                Followers = Math.Max(0, doc["followers"].AsInt(0));

                FrontedDrug = doc["frontedDrug"].AsString("");
                FrontedGrams = doc["frontedGrams"].AsFloat(0f);
                FrontedAtGrams = doc["frontedAtGrams"].AsFloat(0f);

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
