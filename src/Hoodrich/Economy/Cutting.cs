using System;
using System.Drawing;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.State;
using Hoodrich.UI;

namespace Hoodrich.Economy
{
    /// <summary>
    /// Turns bulk weight into street-ready packages at a purity you choose.
    ///
    /// This is the greed dial. Cutting to 50% doubles your units but each unit is worth less
    /// and buyers are likelier to take offence, so gross goes up while risk goes up faster.
    /// Cutting takes real time and leaves you stationary and vulnerable, which is why WHERE you
    /// do it matters -- doing it on a rival's block is a bad idea.
    /// </summary>
    internal sealed class Cutting
    {
        /// <summary>Fixed setup time, plus per-gram time on top.</summary>
        private const int BaseDurationMs = 3000;
        private const float MsPerGram = 90f;
        private const int MaxDurationMs = 30_000;

        private readonly Stash _stash;
        private readonly PlayerState _state;

        private DrugDef _product;
        private float _bulkGrams;
        private float _targetPurity;
        private int _startedAt;
        private int _durationMs;
        private Vector3 _startPosition;

        /// <summary>Real hands on the product. Retried until the clip streams in.</summary>
        private readonly PrepAnimation _anim = new PrepAnimation();

        public Cutting(Stash stash, PlayerState state)
        {
            _stash = stash;
            _state = state;
        }

        public bool IsBusy => _product != null;

        public float Progress
        {
            get
            {
                if (_product == null || _durationMs <= 0) return 0f;
                return Math.Min(1f, (Game.GameTime - _startedAt) / (float)_durationMs);
            }
        }

        /// <summary>Grams of packaged product a given bulk amount yields at a purity.</summary>
        public static float Yield(float bulkGrams, float purity)
        {
            if (purity <= 0f) return 0f;
            return bulkGrams / Math.Max(Stash.MinPurity, Math.Min(Stash.MaxPurity, purity));
        }

        /// <summary>Returns a player-facing refusal, or null if cutting started.</summary>
        public string TryStart(DrugDef product, float bulkGrams, float targetPurity)
        {
            if (IsBusy) return "Already working.";
            if (product == null) return "Nothing selected.";
            if (bulkGrams <= 0f) return "Nothing to cut.";

            if (_stash.BulkOf(product.Id) < bulkGrams - 0.001f)
            {
                return "Only holding " + _stash.BulkOf(product.Id).ToString("0.#") + "g of bulk " + product.Name + ".";
            }

            var yield = Yield(bulkGrams, targetPurity);
            var gained = yield - bulkGrams;
            if (_stash.FreeSpace < gained - 0.001f)
            {
                return "No room for " + yield.ToString("0") + "g -- sell some first.";
            }

            var blocker = WhyCannotCut();
            if (blocker != null) return blocker;

            _product = product;
            _bulkGrams = bulkGrams;
            _targetPurity = targetPurity;
            _startedAt = Game.GameTime;
            _durationMs = Math.Min(MaxDurationMs, BaseDurationMs + (int)(bulkGrams * MsPerGram));
            _startPosition = Game.Player.Character.Position;

            Notify.Ticker(product.SplitVerb + " " + bulkGrams.ToString("0") + "g " + product.Name +
                          " at " + (targetPurity * 100f).ToString("0") + "%...");
            PlayWorkScenario();
            _anim.Start(Game.Player.Character, product.Id);
            return null;
        }

        /// <summary>
        /// Crouches the player over the work so it reads as an activity rather than a menu
        /// wait. Scenarios are tried in order; if none take, the batch still runs -- the
        /// animation is flavour, never a dependency.
        /// </summary>
        private static readonly string[] WorkScenarios =
        {
            "WORLD_HUMAN_CROUCH_INSPECT", "WORLD_HUMAN_DRUG_DEALER", "WORLD_HUMAN_STAND_IMPATIENT"
        };

        private void PlayWorkScenario()
        {
            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            foreach (var scenario in WorkScenarios)
            {
                try
                {
                    Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, player.Handle, scenario, 0, true);
                    return;
                }
                catch
                {
                    // Try the next one.
                }
            }
        }

        private void ClearWorkScenario()
        {
            try { _anim.Stop(Game.Player.Character); } catch { /* teardown */ }

            try
            {
                var player = Game.Player.Character;
                if (player != null && player.Exists()) player.Task.ClearAll();
            }
            catch
            {
                // Nothing to do.
            }
        }

        /// <summary>Why the player cannot start cutting right now, or null if they can.</summary>
        public string WhyCannotCut()
        {
            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return "Not right now.";
            if (player.IsInVehicle()) return "Get out of the vehicle first.";
            if (player.IsInCombat) return "Not while people are shooting at you.";
            if (player.IsRagdoll) return "Get up first.";
            if (player.Velocity.Length() > 1.2f) return "Stand still to cut.";
            return null;
        }

        public void Update()
        {
            if (_product == null) return;

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive)
            {
                Cancel("You went down mid-batch.");
                return;
            }

            if (player.IsInVehicle() || player.IsInCombat)
            {
                Cancel("Interrupted.");
                return;
            }

            // Wandering off ruins the batch. A small radius keeps it from being fussy.
            if (player.Position.DistanceTo(_startPosition) > 3.5f)
            {
                Cancel("You walked away from the batch.");
                return;
            }

            // The clip's dictionary streams in asynchronously, so the first attempt usually
            // loses the race. Keep asking until the player is visibly working.
            if (!_anim.IsPlaying) _anim.Start(player, _product.Id);

            if (Game.GameTime - _startedAt >= _durationMs) Complete();
        }

        private void Complete()
        {
            var product = _product;
            var bulk = _bulkGrams;
            var purity = _targetPurity;

            _product = null;

            var taken = _stash.RemoveBulk(product.Id, bulk);
            if (taken <= 0f)
            {
                Notify.Problem("the weight was gone before you finished.");
                return;
            }

            var yield = Yield(taken, purity);
            var made = _stash.AddPackaged(product.Id, yield, purity);

            _state.Touch();

            ClearWorkScenario();

            

            Notify.Ticker("~g~" + made.ToString("0") + "~s~ " + product.UnitName + " of " + product.Name + " at " +
                          (purity * 100f).ToString("0") + "%");
            Log.Info("Cut " + taken.ToString("0.#") + "g bulk " + product.Id + " -> " +
                     made.ToString("0.#") + "g at " + purity.ToString("0.00") + " purity.");
        }

        private void Cancel(string reason)

        {

            ClearWorkScenario();

            _product = null;
            Notify.Problem(reason);
        }

        /// <summary>Progress bar, drawn while a batch is in progress.</summary>
        public void Draw()
        {
            if (_product == null) return;

            const float x = 0.5f;
            const float y = 0.86f;
            const float w = 0.22f;
            const float h = 0.018f;

            Draw2.Bar(x, y, w, h, Progress);
            UI.Draw.Text("CUTTING " + _product.Name.ToUpperInvariant() + "  " +
                         (_targetPurity * 100f).ToString("0") + "%",
                         x, y - 0.042f, 0.34f, Palette.Text);
        }
    }

    /// <summary>Small composite HUD shapes that do not belong in the primitive layer.</summary>
    internal static class Draw2
    {
        public static void Bar(float cx, float cy, float w, float h, float fraction)
        {
            fraction = fraction < 0f ? 0f : fraction > 1f ? 1f : fraction;

            UI.Draw.Rect(cx, cy, w + 0.004f, h + 0.004f, Color.FromArgb(190, 8, 8, 10));
            UI.Draw.Rect(cx, cy, w, h, Color.FromArgb(160, 30, 32, 34));

            // Grow from the left edge rather than the centre.
            var filled = w * fraction;
            UI.Draw.Rect(cx - (w - filled) * 0.5f, cy, filled, h, Palette.Accent);
        }
    }
}
