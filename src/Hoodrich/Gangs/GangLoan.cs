using System;
using GTA;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.State;
using Hoodrich.UI;

namespace Hoodrich.Gangs
{
    /// <summary>
    /// Money borrowed from your crew, and the weekly interest that makes it a mistake.
    ///
    /// You take a lump sum to buy weight you could not otherwise afford. Every week the vig is
    /// due; pay it and the clock resets, miss it and it compounds. Miss enough and the crew you
    /// borrowed from stops being your crew.
    ///
    /// Dates come from the game's own calendar, so a loan runs on in-game weeks -- sleeping and
    /// fast travel move it, standing still does not.
    ///
    /// Reimplemented from the idea in Los Santos RED, which is unlicensed: the mechanic is
    /// borrowed, none of the code is.
    /// </summary>
    internal sealed class GangLoan
    {
        public string GangId = "";

        /// <summary>What is still owed on the principal.</summary>
        public int Principal;

        /// <summary>Interest due each period.</summary>
        public int Vig;

        /// <summary>Absolute in-game day the vig falls due. See <see cref="CurrentDay"/>.</summary>
        public int DueDay;

        /// <summary>Periods gone by unpaid. Enough of these and they come for you.</summary>
        public int MissedPeriods;

        private bool _warned;

        public bool IsActive => Principal > 0;

        /// <summary>Wipes the debt without paying it. Used only by the full gang reset.</summary>
        public void Clear()
        {
            GangId = "";
            Principal = 0;
            Vig = 0;
            DueDay = 0;
            MissedPeriods = 0;
        }

        public bool IsOverdue => IsActive && CurrentDay() >= DueDay;

        /// <summary>Days until the vig is due; negative once it is late.</summary>
        public int DaysLeft => IsActive ? DueDay - CurrentDay() : 0;

        /// <summary>
        /// A monotonic in-game day number built from the game clock.
        ///
        /// World.CurrentDate is deprecated in this SHVDN, and its replacement clock type is not
        /// worth a dependency for what is simply day arithmetic. Months are counted as 31 days,
        /// which is wrong as a calendar and exactly right as a counter: it never runs backwards,
        /// and a seven-day period is always seven days.
        /// </summary>
        public static int CurrentDay()
        {
            var year = Function.Call<int>(Hash.GET_CLOCK_YEAR);
            var month = Function.Call<int>(Hash.GET_CLOCK_MONTH);
            var day = Function.Call<int>(Hash.GET_CLOCK_DAY_OF_MONTH);
            return year * 372 + month * 31 + day;
        }

        public int TotalOwed => Principal + Vig;

        // ---- lifecycle ---------------------------------------------------------

        public static GangLoan Open(string gangId, int principal, float vigPercent, int periodDays)
        {
            var loan = new GangLoan
            {
                GangId = gangId,
                Principal = principal,
                Vig = Math.Max(1, (int)Math.Round(principal * vigPercent / 100f)),
                MissedPeriods = 0
            };
            loan.PushDueDate(periodDays);
            return loan;
        }

        private void PushDueDate(int periodDays)
        {
            DueDay = CurrentDay() + Math.Max(1, periodDays);
            _warned = false;
        }

        /// <summary>Pays this period's interest and resets the clock. Principal is untouched.</summary>
        public bool PayVig(int periodDays)
        {
            if (!IsActive) return false;
            if (Game.Player.Money < Vig) return false;

            Game.Player.Money -= Vig;
            MissedPeriods = 0;
            PushDueDate(periodDays);

            Notify.Ticker("~g~-$" + Vig.ToString("N0") + "~s~ vig paid. Clock's reset.");
            return true;
        }

        /// <summary>Clears the debt outright.</summary>
        public bool PayOff()
        {
            if (!IsActive) return false;

            var owed = TotalOwed;
            if (Game.Player.Money < owed) return false;

            Game.Player.Money -= owed;
            Principal = 0;
            Vig = 0;
            MissedPeriods = 0;

            Notify.Important("~g~Debt cleared.~s~ -$" + owed.ToString("N0"));
            return true;
        }

        /// <summary>
        /// Ticked periodically. Warns a day out, and compounds the moment it goes late.
        /// Returns true when the loan has just gone into default.
        /// </summary>
        public bool Update(int periodDays, int defaultAfterMissed, float vigGrowthPercent)
        {
            if (!IsActive) return false;

            if (!_warned && DaysLeft <= 1 && !IsOverdue)
            {
                _warned = true;
                Notify.Important("~y~Vig's due tomorrow.~s~ $" + Vig.ToString("N0"));
                return false;
            }

            if (!IsOverdue) return false;

            MissedPeriods++;

            // Missing it makes the next one worse.
            Vig = Math.Max(1, (int)Math.Round(Vig * (1f + vigGrowthPercent / 100f)));
            PushDueDate(periodDays);

            if (MissedPeriods >= defaultAfterMissed)
            {
                Log.Info("Loan from " + GangId + " defaulted after " + MissedPeriods + " missed periods.");
                return true;
            }

            Notify.Failure("you missed the vig. It's $" + Vig.ToString("N0") + " now.");
            return false;
        }

        // ---- persistence -------------------------------------------------------

        public Json ToJson()
        {
            return Json.Object()
                .Set("gang", GangId)
                .Set("principal", Principal)
                .Set("vig", Vig)
                .Set("dueDay", DueDay)
                .Set("missed", MissedPeriods);
        }

        public static GangLoan FromJson(Json node)
        {
            if (node == null || node.IsNull) return null;

            var principal = node["principal"].AsInt(0);
            if (principal <= 0) return null;

            var loan = new GangLoan
            {
                GangId = node["gang"].AsString(""),
                Principal = principal,
                Vig = Math.Max(0, node["vig"].AsInt(0)),
                MissedPeriods = Math.Max(0, node["missed"].AsInt(0))
            };

            // A missing or nonsense due day gives a fresh period rather than an instant default.
            var due = node["dueDay"].AsInt(0);
            loan.DueDay = due > 0 ? due : CurrentDay() + 7;

            return loan;
        }
    }
}
