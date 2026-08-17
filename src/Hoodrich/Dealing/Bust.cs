using System;
using System.Drawing;
using GTA;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Economy;
using Hoodrich.State;
using Hoodrich.Territory;
using Hoodrich.UI;

namespace Hoodrich.Dealing
{
    /// <summary>
    /// The chance that a sale goes wrong with the police.
    ///
    /// Two separate failure modes, because they punish different mistakes:
    ///
    ///   Seen   -- a real cop with line of sight on the handoff. Instant. This is the mod
    ///             telling you not to deal in front of police, and it cannot be dodged.
    ///   Narced -- the buyer was undercover. You get a window: drop them or get out of the
    ///             radius before the call completes. This is the one heat makes likelier, and
    ///             it is survivable if you are paying attention.
    /// </summary>
    internal sealed class Bust
    {
        private const float CopScanRange = 70f;

        /// <summary>PED_TYPE values that count as police.</summary>
        private static readonly int[] CopPedTypes = { 6, 27, 29 };

        private readonly Settings _cfg;
        private readonly PlayerState _state;
        private readonly Random _rng = new Random();

        public TurfWatch Turf;

        private Ped _narc;
        private int _callStartedAt;
        private Vector3Holder _callOrigin;

        /// <summary>Small struct-free holder so the field can be null when idle.</summary>
        private sealed class Vector3Holder
        {
            public GTA.Math.Vector3 Value;
        }

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
        /// Rolled the moment a sale completes. Returns true if the deal drew police attention.
        /// </summary>
        public bool OnSale(Ped buyer, DrugDef product)
        {
            if (_cfg.PoliceBustChancePercent <= 0f) return false;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return false;

            // A cop who can actually see the handoff does not need to roll dice.
            var witness = FindCopWitness(player);
            if (witness != null)
            {
                Notify.Failure("a cop watched you do that.");
                ApplyWanted();
                _state.AddNotoriety(12f);
                Log.Info("Bust: cop witnessed a sale.");
                return true;
            }

            // Otherwise, the chance the buyer was never a buyer.
            var chance = _cfg.PoliceBustChancePercent / 100f
                         * (1f + _state.Notoriety / 100f)
                         * (Turf == null ? 1f : Turf.TurfHeatMultiplier)
                         * (product == null ? 1f : product.HeatFactor);

            if (_rng.NextDouble() > chance) return false;

            StartNarcCall(buyer);
            return true;
        }

        private Ped FindCopWitness(Ped player)
        {
            try
            {
                foreach (var ped in World.GetNearbyPeds(player, CopScanRange))
                {
                    if (ped == null || !ped.Exists() || !ped.IsAlive) continue;
                    if (ped.Handle == player.Handle) continue;

                    var type = Function.Call<int>(Hash.GET_PED_TYPE, ped.Handle);
                    var isCop = Array.IndexOf(CopPedTypes, type) >= 0;
                    if (!isCop) continue;

                    if (!Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY,
                                             ped.Handle, player.Handle, 17))
                    {
                        continue;
                    }

                    return ped;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Cop witness scan failed: " + ex.Message);
            }

            return null;
        }

        private void StartNarcCall(Ped buyer)
        {
            if (buyer == null || !buyer.Exists() || !buyer.IsAlive) return;

            _narc = buyer;
            _callStartedAt = Game.GameTime;

            var player = Game.Player.Character;
            _callOrigin = new Vector3Holder { Value = player.Position };

            try
            {
                // Back off and make the call. The animation is the tell.
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, buyer.Handle, true);
                Function.Call(Hash.TASK_USE_MOBILE_PHONE_TIMED, buyer.Handle,
                              (int)(_cfg.UndercoverCallSeconds * 1000f));
            }
            catch (Exception ex)
            {
                Log.Debug("Could not task the narc: " + ex.Message);
            }

            Notify.Failure("that one's a narc. Drop them or get gone.");
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

            // Dropped them in time.
            if (!_narc.Exists() || !_narc.IsAlive)
            {
                Notify.Ticker("~g~The call never got made.~s~");
                _state.AddNotoriety(6f);
                Clear();
                return;
            }

            // Got clear of the area in time.
            if (_callOrigin != null &&
                player.Position.DistanceTo(_callOrigin.Value) > _cfg.UndercoverEscapeDistance)
            {
                Notify.Ticker("~g~You're clear.~s~ They lost you.");
                Clear();
                return;
            }

            if (CallProgress < 1f) return;

            Notify.Failure("they called it in.");
            ApplyWanted();
            _state.AddNotoriety(15f);
            Clear();
        }

        private void ApplyWanted()
        {
            try
            {
                var stars = Math.Max(1, Math.Min(5, _cfg.BustWantedStars));
                Function.Call(Hash.SET_PLAYER_WANTED_LEVEL, Game.Player.Handle, stars, false);
                Function.Call(Hash.SET_PLAYER_WANTED_LEVEL_NOW, Game.Player.Handle, false);
            }
            catch (Exception ex)
            {
                Log.Error("Could not apply a wanted level.", ex);
            }
        }

        private void Clear()
        {
            if (_narc != null && _narc.Exists())
            {
                try { Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, _narc.Handle, false); }
                catch { }
            }

            _narc = null;
            _callOrigin = null;
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
