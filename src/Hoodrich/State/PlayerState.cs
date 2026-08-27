using System;
using System.Collections.Generic;
using GTA.Math;
using Hoodrich.Core;
using Hoodrich.Economy;
using Hoodrich.UI;

namespace Hoodrich.State
{
    /// <summary>
    /// Hoodrich's own progression. Cash is deliberately NOT stored here -- the game already
    /// owns the player's money and duplicating it would drift.
    /// </summary>
    /// <summary>One car the player owns, and where it was left.</summary>
    internal sealed class OwnedCar
    {
        public string Id = "";
        public string Name = "";
        public int Model;

        /// <summary>What makes it findable again. Nothing else in the world wears this.</summary>
        public string Plate = "";

        public int Paint = -1;
        public int Paint2 = -1;

        public Vector3 Where;
        public float Heading;
    }

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
        /// Whether the player has worked out whose son the man at the port is.
        ///
        /// Tao gives it away himself, because he cannot help it -- the whole of him is a boy
        /// borrowing his father's weight and checking whether you noticed. Nobody has to tell
        /// you; you have to ask, and he will not shut up once you do.
        ///
        /// A flag rather than something inferred, because knowing it is what puts the choice
        /// below on the table. Before you know, there is nothing to tell anybody.
        /// </summary>
        public bool KnowsCheng;

        /// <summary>
        /// Whether the player took that to the old man.
        ///
        /// The one door in this mod that shuts behind you. Tao is skimming his family's
        /// containers to run a side business he is not entitled to, and the person with the
        /// most to say about that is four miles away in Mirror Park wondering who has been at
        /// his stock. Say the name and the port carries on -- it just is not Tao standing on
        /// it any more.
        ///
        /// Deliberately not reversible and deliberately not a failure. It is a trade: you give
        /// up the only man in Los Santos who sells to you at five in the afternoon because he
        /// is too drunk to check the time, and you get an organisation that answers the phone.
        /// </summary>
        public bool ToldTheOldMan;

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
        /// How many port runs are behind him.
        ///
        /// The first one is a story -- Gerald puts you in front of a man who does not know you,
        /// and the pay is almost beside the point. Every one after is a job, and both of them
        /// talk to you completely differently for it. This is the number that tells them apart.
        /// </summary>
        public int PortRunsDone;

        /// <summary>
        /// How many times you have stood in Tao's yard and had a package put in your hands.
        ///
        /// SEPARATE FROM PortRunsDone ON PURPOSE, because the two men are asking different
        /// questions. Gerald wants to know whether he has paid you before; Tao only wants to
        /// know whether he has seen your face before, and he sees it a good twenty minutes and
        /// one freeway before Gerald gets paid.
        ///
        /// Counting them with the same number is what put the introduction speech in front of a
        /// man who had already heard it: take the package, lose the truck on the way home, and
        /// the run ends unpaid -- PortRunsDone still zero, so the next trip out Tao meets you
        /// for the first time all over again, having handed you a brick personally.
        /// </summary>
        public int PortYardVisits;

        /// <summary>
        /// How many of his packages you have taken out and squared up.
        ///
        /// Not a bool, because "the first one" and "every one after" are different
        /// conversations and a counter says which without a second flag. The first is bars and
        /// nothing else -- he is not asking a stranger what they would like -- and after that
        /// he lets you choose what you carry.
        /// </summary>
        public int FrontsDone;

        /// <summary>
        /// Whether you can call for backup.
        ///
        /// Earned on the first job you actually ride WITH them, which is the torch run. Before
        /// that they are people somebody else hands you for an afternoon; after it they are
        /// two men who have been in a car with you and will come out again if you ask.
        /// </summary>
        public bool HomiesUnlocked;

        /// <summary>Whether Hao has introduced himself, so he only does it once.</summary>
        public bool MetHao;

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

        /// <summary>
        /// Whether he has already been told the package is gone.
        ///
        /// The text fires the moment the last gram of HIS work is sold, which is a thing that
        /// happens on a corner with no menu open -- so it needs a latch, or every tick after
        /// that would send it again.
        /// </summary>
        public bool FrontDoneTexted;

        public void ClearFronted()
        {
            FrontedDrug = "";
            FrontedGrams = 0f;
            FrontedAtGrams = 0f;
            FrontDoneTexted = false;
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

        /// <summary>
        /// Which gang bosses you have actually stood in front of.
        ///
        /// Their map markers are hidden until you have met them, so the other eight sets are
        /// something you find rather than something you are handed. A fresh save shows one
        /// icon; the rest of the map fills in as you go, which is the difference between a
        /// city you are exploring and a menu of destinations you have not visited yet.
        ///
        /// Stored by gang id rather than by leader name so renaming a man in leaders.json does
        /// not wipe the fact that you know him.
        /// </summary>
        public readonly List<string> LeadersMet = new List<string>();

        /// <summary>
        /// Cars bought off Hao, by id.
        ///
        /// Saved, because his lot is rebuilt from cars.json every load and without this it
        /// would restock the exact car you are currently driving around in -- and then sell it
        /// to you again.
        /// </summary>
        public readonly List<string> CarsBought = new List<string>();

        /// <summary>
        /// Cars you have actually paid for, and enough about each to stand it back up.
        ///
        /// CarsBought is only a list of ids, and its whole job is stopping Hao restocking
        /// something you already own. It says nothing about the car itself -- so the vehicle
        /// lived exactly as long as the session did, and the only trace of a twenty-two
        /// thousand dollar purchase after a reload was a gap on his lot.
        /// </summary>
        public readonly List<OwnedCar> Owned = new List<OwnedCar>();

        public bool HasMet(string gangId)
        {
            if (string.IsNullOrEmpty(gangId)) return false;

            foreach (var id in LeadersMet)
            {
                if (string.Equals(id, gangId, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        /// <summary>Returns true only the first time, so the caller can say something.</summary>
        public bool MarkMet(string gangId)
        {
            if (string.IsNullOrEmpty(gangId) || HasMet(gangId)) return false;

            LeadersMet.Add(gangId);
            Touch();

            return true;
        }

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
        /// Puts Gerald's whole sequence back to the start.
        ///
        /// Everything the two-package chain runs on, in one go: the packages themselves, the
        /// ledger for whatever you are currently holding of his, the latch that stops him
        /// texting twice, and the port he only opens once both are cleared. The PORT RUN stage
        /// goes with it, or the docks stay half-open -- a save that has been sent to Elysian
        /// but not come back would otherwise sit there refusing to be sent again.
        ///
        /// Deliberately does NOT touch what you have sold, what you are carrying, your money
        /// or your standing. This is his storyline, not your record.
        /// </summary>
        public void ForgetFronts()
        {
            FrontsDone = 0;
            ClearFronted();

            DocksUnlocked = false;
            PortRunStage = 0;
            PortRunsDone = 0;
            PortYardVisits = 0;

            // The port going back to being a rumour takes the man on it with it.
            KnowsCheng = false;
            ToldTheOldMan = false;

            Touch();
        }

        /// <summary>
        /// Counts his packages as done without making you move them again.
        ///
        /// A repair, and it exists because of a specific bug rather than as a cheat. Gerald had
        /// two rows for the same finished package -- one that counted it and one that paid you
        /// and did not -- and pressing the one with money on it, which is the one anybody
        /// presses, cleared the ledger without crediting it. The rows are one settlement now, but
        /// that does not go back and give anybody their package back: a save that lost one is
        /// simply short, is told it has cleared none, and has no way to say otherwise.
        ///
        /// So there is a way to say otherwise. It is deliberately not automatic -- nothing in a
        /// save can tell the difference between somebody the bug robbed and somebody who has not
        /// started -- which makes it a thing the player asserts, not a thing the mod guesses.
        /// </summary>
        /// <summary>
        /// Marks whatever Gerald has fronted you as moved, without moving it.
        ///
        /// Done by winding back the mark his package was measured FROM rather than by setting
        /// a "done" flag of its own, so nothing downstream can tell the difference. He notices
        /// the same way, texts the same text, and the hand-in appears where it always does --
        /// which matters, because the thing people actually get stuck on is not the selling,
        /// it is a count that has gone wrong and left the port shut with nothing to press.
        ///
        /// With nothing in hand it credits a package outright instead. Pressing this always
        /// moves you one step closer to the port, whichever half of the sequence you are in,
        /// and that is the whole point of it.
        /// </summary>
        public string FinishFront()
        {
            if (HasFrontedWork)
            {
                if (FrontedWorkDone) return "That one's already moved. Go and see him.";

                FrontedAtGrams = GramsSold - FrontedGrams;
                Touch();

                Log.Info("Gerald's current package marked moved by hand.");
                return "His package is moved. He'll be in touch.";
            }

            if (FrontsDone >= 2) return "Both his packages are done. The port's open.";

            FrontsDone++;
            ClearFronted();
            Touch();

            Log.Info("Credited one of Gerald's packages by hand (" + FrontsDone + " of 2).");

            return FrontsDone >= 2
                ? "That's both of them. Ask about the port."
                : "That's one of two. He'll have another for you.";
        }

        public void CreditFronts()
        {
            FrontsDone = 2;
            ClearFronted();

            Touch();
            Log.Info("Gerald's packages marked cleared by hand.");
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
        /// <summary>
        /// Everything this mod has ever written down, gone.
        ///
        /// Money and guns are deliberately untouched, and that is not a choice so much as a
        /// fact: neither of them lives here. Cash is the game's and so is the contents of the
        /// weapon wheel, so a reset of OUR save cannot take them even if it wanted to. What
        /// goes is the part that is ours -- the standing, the record, the unlocks, the ledger
        /// with Gerald, what the block thinks of your product and who you have met.
        ///
        /// The stash goes with it. It has to: leaving a kilo in the house of somebody the mod
        /// has just decided is a stranger is not a fresh start, it is a stranger with a kilo.
        /// </summary>
        public void ForgetEverything()
        {
            Respect = 0f;
            Notoriety = 0f;
            TotalDealsMade = 0;
            GramsSold = 0f;
            ProductRep = Neutral;

            DocksUnlocked = false;
            KnowsCheng = false;
            ToldTheOldMan = false;
            PortRunStage = 0;
            PortRunsDone = 0;
            FrontsDone = 0;
            PortYardVisits = 0;
            HomiesUnlocked = false;
            MetHao = false;
            SleptAtStashHouse = false;
            Followers = 0;

            ClearFronted();

            MissionsDone.Clear();
            LeadersMet.Clear();
            CarsBought.Clear();
            Owned.Clear();

            SeenWelcome = false;
            SentForYou = false;

            _offered.Clear();
            Stash.Clear();

            Touch();
            Log.Info("Mod state reset: everything forgotten but the money and the guns.");
        }

        /// <summary>
        /// The whole thing open, and everything behind it done.
        ///
        /// For testing the far end of the mod without playing the near end of it again. It
        /// does NOT hand out respect it has not earned beyond what the unlocks imply -- rank
        /// comes off respect, so the rank has to move or half the gates it opens close again
        /// behind it.
        /// </summary>
        public void UnlockEverything(IEnumerable<string> missionIds, IEnumerable<string> gangIds)
        {
            DocksUnlocked = true;
            PortRunStage = 0;

            // Skipped ahead rather than earned, so the first real run still plays as the first.
            PortRunsDone = 0;
            PortYardVisits = 0;
            FrontsDone = 2;
            HomiesUnlocked = true;
            MetHao = true;
            SleptAtStashHouse = true;

            ClearFronted();

            if (missionIds != null)
            {
                foreach (var id in missionIds)
                {
                    if (!string.IsNullOrEmpty(id) && !MissionsDone.Contains(id)) MissionsDone.Add(id);
                }
            }

            if (gangIds != null)
            {
                foreach (var id in gangIds) MarkMet(id);
            }

            // Enough to clear the last rank gate in the mod, and no more.
            if (Respect < TopRespect) Respect = TopRespect;

            Touch();
            Log.Info("Everything unlocked by hand: " + MissionsDone.Count + " jobs, rank "
                     + RankName + ".");
        }

        /// <summary>What the last rank in the table costs.</summary>
        private const float TopRespect = 6000f;

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

        private Json LeadersJson()
        {
            var arr = Json.Array();
            foreach (var id in LeadersMet) arr.Add(Json.Str(id));
            return arr;
        }

        private Json OwnedJson()
        {
            var arr = Json.Array();

            foreach (var c in Owned)
            {
                arr.Add(Json.Object()
                    .Set("id", c.Id)
                    .Set("name", c.Name)
                    .Set("model", c.Model)
                    .Set("plate", c.Plate)
                    .Set("paint", c.Paint)
                    .Set("paint2", c.Paint2)
                    .Set("x", Math.Round(c.Where.X, 2))
                    .Set("y", Math.Round(c.Where.Y, 2))
                    .Set("z", Math.Round(c.Where.Z, 2))
                    .Set("h", Math.Round(c.Heading, 1)));
            }

            return arr;
        }

        private Json CarsJson()
        {
            var arr = Json.Array();
            foreach (var id in CarsBought) arr.Add(Json.Str(id));
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

        /// <summary>
        /// Un-marks one offer, so it can be made again.
        ///
        /// For the case where the offer was somebody ELSE'S. A plug texts you his number once
        /// and is never heard from again, which is right until the man on that pitch changes:
        /// the new one has a different number, a different price and no way to tell you either,
        /// because as far as this list is concerned that introduction already happened.
        /// </summary>
        public void ForgetOffered(string id)
        {
            if (string.IsNullOrEmpty(id)) return;

            _offered.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
            Touch();
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
                .Set("knowsCheng", KnowsCheng)
                .Set("toldTheOldMan", ToldTheOldMan)
                .Set("portRunStage", PortRunStage)
                .Set("portRunsDone", PortRunsDone)
                .Set("portYardVisits", PortYardVisits)
                .Set("frontsDone", FrontsDone)
                .Set("homiesUnlocked", HomiesUnlocked)
                .Set("metHao", MetHao)
                .Set("sleptAtStashHouse", SleptAtStashHouse)
                .Set("followers", Followers)
                .Set("frontedDrug", FrontedDrug)
                .Set("frontedGrams", FrontedGrams)
                .Set("frontDoneTexted", FrontDoneTexted)
                .Set("frontedAtGrams", FrontedAtGrams)
                .Set("lastJobAt", LastJobAtUtc)
                .Set("missionsDone", MissionsJson())
                .Set("leadersMet", LeadersJson())
                .Set("carsBought", CarsJson())
                .Set("ownedCars", OwnedJson())
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
                KnowsCheng = doc["knowsCheng"].AsBool(false);
                ToldTheOldMan = doc["toldTheOldMan"].AsBool(false);
                PortRunStage = Math.Max(0, Math.Min(3, doc["portRunStage"].AsInt(0)));
                PortRunsDone = Math.Max(0, doc["portRunsDone"].AsInt(0));

                // A save written before the yard was counted separately still knows how many
                // runs were paid for, and a paid run always went through the yard -- so the
                // visits are at least that, and Tao does not forget anybody on an upgrade.
                PortYardVisits = Math.Max(PortRunsDone,
                                          Math.Max(0, doc["portYardVisits"].AsInt(0)));
                FrontsDone = Math.Max(0, doc["frontsDone"].AsInt(0));
                HomiesUnlocked = doc["homiesUnlocked"].AsBool(false);
                MetHao = doc["metHao"].AsBool(false);
                SleptAtStashHouse = doc["sleptAtStashHouse"].AsBool(false);

                // Defaults to FALSE, so a save from before this existed shows the guide once
                // and then never again. That is the right way round: somebody who has been
                // playing already loses nothing by being told, and somebody new needs it.
                SeenWelcome = doc["seenWelcome"].AsBool(false);
                SentForYou = doc["sentForYou"].AsBool(false);
                Followers = Math.Max(0, doc["followers"].AsInt(0));

                FrontedDrug = doc["frontedDrug"].AsString("");
                FrontedGrams = doc["frontedGrams"].AsFloat(0f);
                FrontDoneTexted = doc["frontDoneTexted"].AsBool(false);
                FrontedAtGrams = doc["frontedAtGrams"].AsFloat(0f);

                LastJobAtUtc = Math.Max(0L, doc["lastJobAt"].AsLong(0L));

                MissionsDone.Clear();
                foreach (var node in doc["missionsDone"].Items)
                {
                    var id = node.AsString("");
                    if (!string.IsNullOrEmpty(id) && !HasDone(id)) MissionsDone.Add(id);
                }

                CarsBought.Clear();
            Owned.Clear();
                Owned.Clear();

                foreach (var node in doc["ownedCars"].Items)
                {
                    var id = node["id"].AsString("");
                    if (string.IsNullOrEmpty(id)) continue;

                    Owned.Add(new OwnedCar
                    {
                        Id = id,
                        Name = node["name"].AsString(id),
                        Model = node["model"].AsInt(0),
                        Plate = node["plate"].AsString(""),
                        Paint = node["paint"].AsInt(-1),
                        Paint2 = node["paint2"].AsInt(-1),
                        Where = new Vector3(node["x"].AsFloat(), node["y"].AsFloat(),
                                            node["z"].AsFloat()),
                        Heading = node["h"].AsFloat()
                    });
                }

                foreach (var node in doc["carsBought"].Items)
                {
                    var id = node.AsString("");
                    if (!string.IsNullOrEmpty(id) && !CarsBought.Contains(id)) CarsBought.Add(id);
                }

                LeadersMet.Clear();
                foreach (var node in doc["leadersMet"].Items)
                {
                    var id = node.AsString("");
                    if (!string.IsNullOrEmpty(id) && !HasMet(id)) LeadersMet.Add(id);
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
