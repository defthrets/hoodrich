using GTA;
using GTA.Native;

namespace Hoodrich.UI
{
    /// <summary>
    /// Paying the player, and clearing up after the green number it leaves behind.
    ///
    /// GTA shows a "+$170" beside the wallet when your money changes and takes it away again a
    /// few seconds later -- except that it did not. It sat in the corner of the screen for the
    /// rest of the session: through the drive back, through a conversation with Gerald, through
    /// everything, because the timer that retires it is driven by the game's own money path and
    /// a script writing the cash stat directly never starts it.
    ///
    /// So the mod cleans up after itself. Every payout goes through here, the number gets the
    /// few seconds it was always meant to have, and after that HUD_CASH_CHANGE is held down
    /// each frame until something else moves the money.
    ///
    /// That last part is what keeps this honest. The moment the player's balance changes from
    /// anywhere we did not touch -- a shop, a pickup, another mod -- the suppression drops and
    /// the game gets its HUD back. We only ever hide the number we put there.
    /// </summary>
    internal static class Cash
    {
        /// <summary>HUD_CASH_CHANGE. The green number, not the wallet beside it.</summary>
        private const int CashChange = 13;

        /// <summary>How long it is allowed to sit there before we take it away ourselves.</summary>
        private const int ReadItMs = 5000;

        private static int _paidAt;
        private static int _balance;

        /// <summary>Pays the player, and starts the clock on the number it puts on screen.</summary>
        public static void Give(int amount)
        {
            if (amount == 0) return;

            Game.Player.Money += amount;

            _paidAt = Game.GameTime;
            _balance = Game.Player.Money;
        }

        /// <summary>Takes money without arming any of the above. A fine is not a payout.</summary>
        public static void Take(int amount)
        {
            if (amount <= 0) return;

            Game.Player.Money -= amount;

            // The balance moved because WE moved it, so the watch below would otherwise read
            // this as somebody else's change and let a stuck number stay up.
            if (_paidAt != 0) _balance = Game.Player.Money;
        }

        /// <summary>Called every frame. Does nothing at all until we have actually paid out.</summary>
        public static void Tick()
        {
            if (_paidAt == 0) return;

            int money;

            try { money = Game.Player.Money; }
            catch { return; }

            // Somebody else's money, somebody else's HUD.
            if (money != _balance) { _paidAt = 0; return; }

            if (Game.GameTime - _paidAt < ReadItMs) return;

            try { Function.Call(Hash.HIDE_HUD_COMPONENT_THIS_FRAME, CashChange); }
            catch { /* then it stays, and it is no worse than it was */ }
        }
    }
}
