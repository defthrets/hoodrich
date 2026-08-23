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

        /// <summary>
        /// And how fast a refusal does, which is a great deal faster. Bad news travels.
        ///
        /// Nearly double what it was. A refusal used to cost about two sales at the middle of
        /// the scale, which is not what being caught selling somebody stepped-on product is --
        /// he does not simply not buy, he tells people, and the people he tells were the ones
        /// walking over to you next.
        ///
        /// The asymmetry at the top of the scale is deliberate rather than a side effect. A
        /// drift saturates, so once the block trusts you a good sale barely moves the number
        /// while a refusal still moves the whole distance: a reputation is slow to build and
        /// quick to lose, and that is the correct shape for one.
        /// </summary>
        private const float RepDriftPerRefusal = 0.20f;

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
        /// tells other people.
        ///
        /// SQUARED, and that is the part that makes a bad cut hurt properly. Half the purity
        /// was a straight line: getting caught at a third was only half again as bad as getting
        /// caught at three quarters, when in fact one of those is weak product and the other is
        /// barely product. Squaring bends it -- three quarters drags you toward 0.28, a half
        /// toward 0.13, a third toward 0.05 -- so the further you stepped on it, the further
        /// the opinion falls, rather than everything below the line costing roughly the same.
        /// </summary>
        public void RefusedAt(float purity)
        {
            if (purity < 0f) purity = 0f;
            if (purity > 1f) purity = 1f;

            Drift(purity * purity * 0.5f, RepDriftPerRefusal);
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
        /// Where the trip to the port has got to. 0 nothing, 1 fetch it, 2 deliver it.
        ///
        /// Saved, because it is a drive across the whole map in two halves and quitting
        /// halfway through it is the most ordinary thing a player does. Kept as a stage rather
        /// than a pair of bools so there is exactly one thing to read and no state where both
        /// halves are somehow true at once.
        /// </summary>
        public int PortRunStage;

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
        /// A package Gerald has fronted you, and where your sales counter stood when he did.
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
            // The clock starts whether or not this one was new, because it is a breather
            // between jobs rather than a reward for finishing a fresh one -- doing the same
            // torch job four times back to back should pace exactly like doing four different
            // ones.
            LastJobAtUtc = DateTime.UtcNow.Ticks;

            if (string.IsNullOrEmpty(missionId) || HasDone(missionId))
            {
                Touch();
                return;
            }

            MissionsDone.Add(missionId);
            Touch();
        }

        /// <summary>
        /// How long Lamar has nothing, after you have just done something for him.
        ///
        /// This replaced the rank wall. Ranks gated his list on a number that only went up if
        /// you stood on corners, so a player who wanted to do the JOBS found the jobs locked
        /// behind not doing the jobs. The chain already says which one is next; all that was
        /// missing was a reason not to run the whole list in one afternoon, and a man saying
        /// "gimme a minute" is that reason.
        ///
        /// Real minutes rather than the game clock, and stored as a wall-clock stamp rather
        /// than as a countdown: quit for the night, come back tomorrow, and the wait is over
        /// because it genuinely is.
        /// </summary>
        public const int JobCooldownMinutes = 10;

        public long LastJobAtUtc;

        /// <summary>Seconds still to wait, or zero.</summary>
        public int JobWaitSeconds
        {
            get
            {
                if (LastJobAtUtc <= 0L) return 0;

                try
                {
                    var since = DateTime.UtcNow - new DateTime(LastJobAtUtc, DateTimeKind.Utc);
                    var left = TimeSpan.FromMinutes(JobCooldownMinutes) - since;

                    // A stamp from the future means somebody moved the system clock. Treat it
                    // as expired rather than locking the list for a decade.
                    if (left.TotalSeconds <= 0d || left.TotalMinutes > JobCooldownMinutes) return 0;

                    return (int)Math.Ceiling(left.TotalSeconds);
                }
                catch
                {
                    return 0;
                }
            }
        }

        public bool JobsAreCooling => JobWaitSeconds > 0;

        /// <summary>The wait as something to put on a row: "8m 20s".</summary>
        public string JobWaitWord
        {
            get
            {
                var left = JobWaitSeconds;
                if (left <= 0) return "";

                var m = left / 60;
                var s = left % 60;

                return m > 0 ? m + "m " + s.ToString("00") + "s" : s + "s";
            }
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

        private Json OfferedJson()
        {
            var arr = Json.Array();
            foreach (var id in _offered) arr.Add(Json.Str(id));
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

        /// <summary>
        /// Whether the man who runs the set has sent for you yet.
        ///
        /// The first thing that happens in this mod. Saved rather than worked out, because
        /// "have you been told" is a fact about a conversation and there is nothing else in the
        /// state that could stand in for it -- no affiliation, no work, no sales, nothing.
        /// </summary>
        public bool SentForYou;

        /// <summary>
        /// Jobs Lamar has already texted about.
        ///
        /// Separate from the finished list, because "he told you it existed" and "you did it"
        /// are different facts and the first must survive you ignoring him.
        /// </summary>
        private readonly List<string> _offered = new List<string>();

        public bool HasBeenOffered(string id)
        {
            return !string.IsNullOrEmpty(id) &&
                   _offered.Exists(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
        }

        public void MarkOffered(string id)
        {
            if (string.IsNullOrEmpty(id) || HasBeenOffered(id)) return;
            _offered.Add(id);
        }

        public Json ToJson()
        {
            return Json.Object()
                .Set("seenWelcome", SeenWelcome)
                .Set("sentForYou", SentForYou)
                .Set("respect", Math.Round(Respect, 2))
                .Set("notoriety", Math.Round(Notoriety, 2))
                .Set("totalDeals", TotalDealsMade)
                .Set("totalEarned", TotalEarned)
                .Set("gramsSold", Math.Round(GramsSold, 2))
                .Set("productRep", Math.Round(ProductRep, 3))
                .Set("docksUnlocked", DocksUnlocked)
                .Set("portRunStage", PortRunStage)
                .Set("sleptAtStashHouse", SleptAtStashHouse)
                .Set("followers", Followers)
                .Set("frontedDrug", FrontedDrug)
                .Set("frontedGrams", FrontedGrams)
                .Set("frontedAtGrams", FrontedAtGrams)
                .Set("lastJobAt", LastJobAtUtc)
                .Set("missionsDone", MissionsJson())
                .Set("missionsOffered", OfferedJson())
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
                PortRunStage = Math.Max(0, Math.Min(2, doc["portRunStage"].AsInt(0)));
                SleptAtStashHouse = doc["sleptAtStashHouse"].AsBool(false);

                // Defaults to FALSE, so a save from before this existed shows the guide once
                // and then never again. That is the right way round: somebody who has been
                // playing already loses nothing by being told, and somebody new needs it.
                SeenWelcome = doc["seenWelcome"].AsBool(false);
                SentForYou = doc["sentForYou"].AsBool(false);
                Followers = Math.Max(0, doc["followers"].AsInt(0));

                FrontedDrug = doc["frontedDrug"].AsString("");
                FrontedGrams = doc["frontedGrams"].AsFloat(0f);
                FrontedAtGrams = doc["frontedAtGrams"].AsFloat(0f);

                LastJobAtUtc = Math.Max(0L, doc["lastJobAt"].AsLong(0L));

                MissionsDone.Clear();
                foreach (var node in doc["missionsDone"].Items)
                {
                    var id = node.AsString("");
                    if (!string.IsNullOrEmpty(id) && !HasDone(id)) MissionsDone.Add(id);
                }

                // Read back, or Lamar texts about the same job every time you load.
                _offered.Clear();
                foreach (var node in doc["missionsOffered"].Items)
                {
                    MarkOffered(node.AsString(""));
                }

                // A job already finished counts as told. An existing save has no offered list
                // at all, so without this he would text about everything you have ever done
                // the first time you load after updating.
                foreach (var id in MissionsDone) MarkOffered(id);

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
