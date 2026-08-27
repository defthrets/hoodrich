using System;
using GTA;
using Hoodrich.Core;

namespace Hoodrich.Supply
{
    /// <summary>
    /// A plug texts you out of nowhere with something he wants gone tonight.
    ///
    /// THE MESSAGES APP ONLY EVER RECEIVED. It holds every text in the game and lets you start
    /// a conversation with anybody in it, and none of that was ever a reason to open it -- a
    /// message you have already read as a notification is not worth going back for, so the app
    /// was an archive of things that had finished happening.
    ///
    /// This gives it a job. An offer lands as a text, it is good for a while, and it is only
    /// good at the man who sent it. Reading it is not enough; you have to be somewhere else
    /// before it runs out. That is the whole mechanic, and it costs no new screen: the plug's
    /// price is simply lower while it stands, and the wheel says so when you get there.
    ///
    /// STATIC, DELIBERATELY, and for the same reason Inbox is. There is one player with one
    /// phone, the offer is a fact about the world rather than about any particular menu, and
    /// the alternative was threading a reference through DealerTalk -- which is built per
    /// conversation and has no idea what a manager is.
    ///
    /// One at a time. Two competing offers is a spreadsheet.
    /// </summary>
    internal static class ColdCall
    {
        private static string _dealerId = "";
        private static string _drugId = "";
        private static float _off;
        private static int _until;

        /// <summary>Who it is with, or empty.</summary>
        public static string DealerId => Standing ? _dealerId : "";

        /// <summary>What he wants shifted.</summary>
        public static string DrugId => Standing ? _drugId : "";

        /// <summary>How far off his usual price, 0..1.</summary>
        public static float Off => Standing ? _off : 0f;

        /// <summary>Whether anything is on the table right now.</summary>
        public static bool Standing => _until != 0 && Game.GameTime < _until;

        /// <summary>Real minutes left, for a menu that wants to create some urgency.</summary>
        public static float MinutesLeft =>
            !Standing ? 0f : Math.Max(0f, (_until - Game.GameTime) / 60_000f);

        /// <summary>Whether this particular man is the one holding it.</summary>
        public static bool With(string dealerId)
        {
            return Standing && !string.IsNullOrEmpty(dealerId) &&
                   string.Equals(dealerId, _dealerId, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// What to multiply his price by.
        ///
        /// One for everybody who is not him, and for him once it has run out -- so the caller
        /// never has to ask whether there is an offer before working out a price.
        /// </summary>
        public static float Multiplier(string dealerId)
        {
            return With(dealerId) ? Math.Max(0.2f, 1f - _off) : 1f;
        }

        /// <summary>Puts one on the table.</summary>
        public static void Open(string dealerId, string drugId, float off, int lastsMs)
        {
            if (string.IsNullOrEmpty(dealerId)) return;

            _dealerId = dealerId;
            _drugId = drugId ?? "";
            _off = off < 0.05f ? 0.05f : off > 0.6f ? 0.6f : off;
            _until = Game.GameTime + Math.Max(60_000, lastsMs);
        }

        /// <summary>Taken, or gone off.</summary>
        public static void Close()
        {
            _dealerId = "";
            _drugId = "";
            _off = 0f;
            _until = 0;
        }
    }
}
