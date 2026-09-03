using System;
using GTA;
using GTA.Native;
using Color = System.Drawing.Color;

namespace Hoodrich.UI
{
    /// <summary>
    /// Money moving, said where you are already looking.
    ///
    /// GTA puts a green "+$170" beside the wallet in the top right and takes it away again a
    /// few seconds later -- except that it did not, for us. It sat in the corner for the rest
    /// of the session: through the drive back, through a conversation with Gerald, through
    /// everything, because the timer that retires it is driven by the game's own money path
    /// and a script writing the cash stat directly never starts it.
    ///
    /// SO IT IS OURS NOW, AND IT IS NOT IN THE CORNER. The top right is the furthest point on
    /// the screen from the minimap, and the minimap is where your eye already is when you are
    /// driving away from a sale -- which is precisely when this number appears. A readout you
    /// have to go and look for is a readout you find out about afterwards.
    ///
    /// The game's own one is held down for as long as ours is up, so there is never a moment
    /// with two of them. And the suppression is only ever ours to hold: the instant the balance
    /// moves from somewhere we did not touch -- a shop, a pickup, another mod -- we let go and
    /// the game gets its HUD back. We only ever hide the number we put there.
    /// </summary>
    internal static class Cash
    {
        /// <summary>HUD_CASH_CHANGE. The green number, not the wallet beside it.</summary>
        private const int CashChange = 13;

        /// <summary>How long it stays up.</summary>
        private const int ReadItMs = 4200;

        /// <summary>The last stretch of that, spent fading and rising out.</summary>
        private const int LeaveMs = 900;

        /// <summary>
        /// Just above the minimap, and left-aligned with it.
        ///
        /// The minimap's top edge sits near 0.783 at the default safe zone, so this clears it
        /// with a little air. Not measured off the game -- there is no native that will tell
        /// you where the minimap actually is, and the safe zone can move it -- so these are the
        /// figures for a normal setup and they are constants precisely so there is one place to
        /// nudge them if somebody runs a strange one.
        /// </summary>
        private const float LeftAt = 0.0165f;
        private const float RestAt = 0.7555f;

        /// <summary>How far it drifts up as it goes.</summary>
        private const float Rise = 0.014f;

        private static int _shownAt;
        private static int _amount;
        private static int _balance;

        /// <summary>
        /// The last move, kept after the corner readout has gone.
        ///
        /// _shownAt is cleared when the number retires, which is right for the thing on screen
        /// and useless to anything that wants to ASK what last happened. The phone's bank card
        /// is open long after a sale and still wants to show the line. So the timestamp is
        /// kept separately and never cleared.
        /// </summary>
        private static int _movedAt;

        /// <summary>What the balance last did, and when. Nought until money has moved.</summary>
        public static int LastMove { get { return _amount; } }
        public static int MovedAt { get { return _movedAt; } }

        /// <summary>Pays the player, and puts the number up.</summary>
        public static void Give(int amount)
        {
            if (amount == 0) return;

            Game.Player.Money += amount;
            Mark(amount);
        }

        /// <summary>
        /// Takes money, and says so.
        ///
        /// It used to be silent on the reasoning that a fine is not a payout. Which is true and
        /// is not the point: the question this answers is "what just happened to my money", and
        /// money leaving is the half of that anybody actually wants telling about.
        /// </summary>
        public static void Take(int amount)
        {
            if (amount <= 0) return;

            Game.Player.Money -= amount;
            Mark(-amount);
        }

        private static void Mark(int amount)
        {
            _shownAt = Game.GameTime;
            _movedAt = _shownAt;
            _amount = amount;

            try { _balance = Game.Player.Money; }
            catch { _balance = 0; }
        }

        /// <summary>Called every frame. Does nothing at all until money has actually moved.</summary>
        public static void Tick()
        {
            if (_shownAt == 0) return;

            int money;

            try { money = Game.Player.Money; }
            catch { return; }

            // Somebody else's money, somebody else's HUD. Ours comes down with it, because a
            // number of ours sat next to a number of theirs is worse than either alone.
            if (money != _balance)
            {
                _shownAt = 0;
                return;
            }

            var age = Game.GameTime - _shownAt;

            if (age > ReadItMs)
            {
                _shownAt = 0;
                return;
            }

            // Held down every frame for as long as ours is up, rather than once at the end.
            // This is the whole reason there are not two of them on screen.
            try { Function.Call(Hash.HIDE_HUD_COMPONENT_THIS_FRAME, CashChange); }
            catch { /* then theirs stays, and ours is beside it, which is no worse than before */ }

            try { Show(age); }
            catch { /* a frame without it is not worth taking the tick down for */ }
        }

        private static void Show(int age)
        {
            var left = age > ReadItMs - LeaveMs
                ? (ReadItMs - age) / (float)LeaveMs
                : 1f;

            if (left <= 0f) return;

            // Rises as it goes, so it leaves rather than switching off. Squared, so almost all
            // of the travel happens at the end and it sits still while you are reading it.
            var gone = 1f - left;
            var y = RestAt - Rise * gone * gone;

            var alpha = (int)(255f * left);

            var text = (_amount > 0 ? "+$" : "-$") + Math.Abs(_amount).ToString("N0");

            var ink = _amount > 0 ? Palette.Cash : Palette.Danger;

            // A shadow under it, because this lands over the map and the road behind the map,
            // and a thin green figure on a pale street is a figure nobody reads.
            Draw.Text(text, LeftAt + Draw.ToX(0.0016f), y + 0.0016f, 0.46f,
                      Color.FromArgb((int)(alpha * 0.75f), 0, 0, 0),
                      Draw.FontChaletLondon, centre: false);

            Draw.Text(text, LeftAt, y, 0.46f, Palette.Alpha(ink, alpha),
                      Draw.FontChaletLondon, centre: false);
        }
    }
}
