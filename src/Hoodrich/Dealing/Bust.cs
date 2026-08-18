using System;
using System.Drawing;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Economy;
using Hoodrich.State;
using Hoodrich.Territory;
using Hoodrich.UI;

namespace Hoodrich.Dealing
{
    /// <summary>
    /// The buyer who was never a buyer.
    ///
    /// This is the only failure in the mod you can play your way out of. A uniform with line of
    /// sight is instant and undodgeable, corner heat is a slow inevitability, and a search you
    /// stand still for is a coin flip you have already lost. The narc hands you a clock and a
    /// decision instead: drop them before the call lands, or get far enough from where the
    /// handoff happened that what they say no longer places you.
    ///
    /// It used to carry a witness scan and a wanted-level call of its own as well. Both were
    /// duplicates of PostUp's -- the scan at nearly three times the range, so it counted cops
    /// who could not have seen anything, and the wanted call flat rather than never-lower, so a
    /// two-star bust could pull you DOWN from three stars. Only the narc survived the move.
    /// </summary>
    internal sealed class Bust
    {
        /// <summary>Corner heat added when a call lands, and when you stop one with a body.</summary>
        private const float HeatOnBust = 14f;
        private const float HeatOnKill = 5f;

        private readonly Settings _cfg;
        private readonly PlayerState _state;
        private readonly Random _rng = new Random();

        public TurfWatch Turf;

        /// <summary>The corner this happened on, so the fallout lands where the work is.</summary>
        public PostUp Post;

        private Ped _narc;
        private int _callStartedAt;
        private Vector3? _callOrigin;

        public Bust(Settings cfg, PlayerState state)
        {
            _cfg = cfg;
            _state = state;
        }

        public bool CallInProgress => _narc != null;

        /// <summary>0..1 through the narc's phone call.</summary>
        public float CallProgress
        {
            get
            {
                if (_narc == null || _cfg.UndercoverCallSeconds <= 0f) return 0f;
                var span = _cfg.UndercoverCallSeconds * 1000f;
                return Math.Min(1f, (Game.GameTime - _callStartedAt) / span);
            }
        }

        // ---- called from the deal ----------------------------------------------

        /// <summary>
        /// Rolled once a sale has landed. Returns true if this one was undercover.
        ///
        /// Not called when a cop already saw the handoff or the corner has gone hot on its own
        /// -- you are already in trouble, and stacking a second clock on top of stars you cannot
        /// outrun is not a decision, it is a pile-on.
        /// </summary>
        public bool OnSale(Ped buyer, DrugDef product)
        {
            if (_cfg.PoliceBustChancePercent <= 0f) return false;
            if (_narc != null) return false;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return false;

            var chance = _cfg.PoliceBustChancePercent / 100f
                         * (1f + _state.Notoriety / 100f)
                         * (Turf == null ? 1f : Turf.TurfHeatMultiplier)
                         * (product == null ? 1f : product.HeatFactor);

            if (_rng.NextDouble() > chance) return false;

            StartCall(buyer);
            return true;
        }

        private void StartCall(Ped buyer)
        {
            if (buyer == null || !buyer.Exists() || !buyer.IsAlive) return;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            _narc = buyer;
            _callStartedAt = Game.GameTime;
            _callOrigin = player.Position;

            try
            {
                // Held for the length of the call and let go the moment it resolves, either way.
                // A man who has made his call is just a man in a street.
                buyer.IsPersistent = true;

                // Back off and make the call. The animation is the tell.
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, buyer.Handle, true);
                Function.Call(Hash.TASK_USE_MOBILE_PHONE_TIMED, buyer.Handle,
                              (int)(_cfg.UndercoverCallSeconds * 1000f));
            }
            catch (Exception ex)
            {
                Log.Debug("Could not task the narc: " + ex.Message);
            }

            Notify.Failure("that one is a narc. Drop them or get gone.");
            Log.Info("Bust: undercover buyer started a call.");
        }

        // ---- per-tick ----------------------------------------------------------

        public void Update()
        {
            if (_narc == null) return;

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive)
            {
                Clear();
                return;
            }

            // Streamed out from under us. Nobody earned anything, so drop it quietly rather than
            // crediting a kill that never happened.
            if (!_narc.Exists())
            {
                Clear();
                return;
            }

            // Dropped them in time.
            if (!_narc.IsAlive)
            {
                Notify.Ticker("~g~The call never got made.~s~");

                // Cheaper than the bust, not free. A body on the pavement is its own problem.
                _state.AddNotoriety(6f);
                if (Post != null) Post.AddCornerHeat(HeatOnKill);
                Clear();
                return;
            }

            // Got clear of where it happened in time.
            if (_callOrigin.HasValue &&
                player.Position.DistanceTo(_callOrigin.Value) > _cfg.UndercoverEscapeDistance)
            {
                Notify.Ticker("~g~You are clear.~s~ They lost you.");
                Clear();
                return;
            }

            if (CallProgress < 1f) return;

            Notify.Failure("they called it in.");
            PostUp.Wanted(Math.Max(1, Math.Min(5, _cfg.BustWantedStars)));
            _state.AddNotoriety(15f);

            // A squad car pulling up where you are stood is exactly what makes a corner hot, so
            // it has to cost the pitch as well as the player. Without this you could eat bust
            // after bust on the same spot and the corner would never notice.
            if (Post != null) Post.AddCornerHeat(HeatOnBust);
            Clear();
        }

        private void Clear()
        {
            if (_narc != null && _narc.Exists())
            {
                try
                {
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, _narc.Handle, false);
                    _narc.MarkAsNoLongerNeeded();
                }
                catch
                {
                    // Letting a ped go is never worth an exception.
                }
            }

            _narc = null;
            _callOrigin = null;
        }

        /// <summary>Called on unload. The world does not keep our narc.</summary>
        public void RestoreWorld()
        {
            Clear();
        }

        /// <summary>Countdown bar while the narc is on the phone -- your window to act.</summary>
        public void Draw()
        {
            if (_narc == null) return;

            const float x = 0.5f;
            const float y = 0.80f;
            const float w = 0.20f;
            const float h = 0.016f;

            var remaining = 1f - CallProgress;

            UI.Draw.Rect(x, y, w + 0.004f, h + 0.004f, Color.FromArgb(190, 8, 8, 10));
            UI.Draw.Rect(x, y, w, h, Color.FromArgb(160, 30, 32, 34));

            var filled = w * remaining;
            UI.Draw.Rect(x - (w - filled) * 0.5f, y, filled, h, Palette.Danger);

            UI.Draw.Text("CALLING IT IN", x, y - 0.040f, 0.34f, Palette.Danger, UI.Draw.FontLabel);
        }
    }
}
