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

        /// <summary>
        /// The longest any batch can take.
        ///
        /// Thirty seconds was low enough to cancel the whole point of choosing a bag size. A
        /// 50g batch at half purity in singles wants 33 seconds and got 30; the same batch in
        /// ounces wants 8 and got 8 -- so past a very small size the fiddly option was free,
        /// and the trade the screen offers you was not a trade at all.
        /// </summary>
        private const int MaxDurationMs = 60_000;

        /// <summary>
        /// Every bag has to be filled, tied and put somewhere.
        ///
        /// Halved along with the cap being doubled. Together those keep the biggest realistic
        /// batch inside the ceiling -- so singles stay slower than ounces all the way up rather
        /// than both flattening against the cap and coming out identical.
        /// </summary>
        private const int MsPerPackage = 120;

        private readonly Stash _stash;
        private readonly PlayerState _state;

        private DrugDef _product;

        /// <summary>What it comes out as. The same product, unless it is being rolled.</summary>
        private DrugDef _output;
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

        /// <summary>
        /// The same, when what comes off the counter is a different thing to what went on it.
        ///
        /// A gram makes a joint, so the count is grams over grams-per-unit -- and stretching it
        /// still stretches it, because a joint rolled thin is still a joint somebody paid for.
        /// </summary>
        public static float YieldOf(DrugDef product, DrugDef output, float bulkGrams, float purity)
        {
            var stretched = Yield(bulkGrams, purity);

            if (output == null || product == null || output.Id == product.Id) return stretched;

            var per = Math.Max(0.1f, product.PerUnit);
            return (float)Math.Floor(stretched / per);
        }

        /// <summary>Returns a player-facing refusal, or null if cutting started.</summary>
        public string TryStart(DrugDef product, float bulkGrams, float targetPurity)
        {
            return TryStart(product, product, bulkGrams, targetPurity);
        }

        /// <summary>
        /// Works one product into another.
        ///
        /// Weed rolled into joints is the only case so far, and it is the reason this takes two
        /// products rather than one: what goes on the counter and what comes off it are not
        /// always the same thing.
        /// </summary>
        public string TryStart(DrugDef product, DrugDef output, float bulkGrams, float targetPurity)
        {
            return TryStart(product, output, bulkGrams, targetPurity, 1f);
        }

        /// <summary>
        /// The same, told what it is being bagged into.
        ///
        /// The size is not a different product -- an ounce and twenty-eight singles are the same
        /// weight of the same thing -- it is how long you are stood at that counter. Twenty-eight
        /// little bags is an afternoon; one ounce bag is a minute, and the corner will take all
        /// day to move it. That is the trade, and it is the only honest thing a bag size can do
        /// in a stash measured by weight.
        /// </summary>
        public string TryStart(DrugDef product, DrugDef output, float bulkGrams,
                               float targetPurity, float packageSize)
        {
            if (output == null) output = product;
            if (IsBusy) return "Already working.";
            if (product == null) return "Nothing selected.";
            if (bulkGrams <= 0f) return "Nothing to cut.";

            if (_stash.BulkOf(product.Id) < bulkGrams - 0.001f)
            {
                return "Only holding " + product.Amount(_stash.BulkOf(product.Id)) + " of bulk " + product.Name + ".";
            }

            var yield = YieldOf(product, output, bulkGrams, targetPurity);
            var gained = yield - bulkGrams;
            if (_stash.FreeSpace < gained - 0.001f)
            {
                return "No room for " + yield.ToString("0") + "g -- sell some first.";
            }

            var blocker = WhyCannotCut();
            if (blocker != null) return blocker;

            _product = product;
            _output = output;
            _bulkGrams = bulkGrams;
            _targetPurity = targetPurity;
            _startedAt = Game.GameTime;
            // How long depends on how many bags come out of it, not just how much went in.
            var yieldNow = YieldOf(product, output, bulkGrams, targetPurity);
            var bags = packageSize <= 0f ? yieldNow : yieldNow / packageSize;

            var work = BaseDurationMs + (int)(bulkGrams * MsPerGram) + (int)(bags * MsPerPackage);

            _durationMs = Math.Min(MaxDurationMs, Math.Max(BaseDurationMs, work));
            _startPosition = Game.Player.Character.Position;

            Notify.Ticker(product.SplitVerb + " " + product.Amount(bulkGrams) + " of " + product.Name +
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
            var outputHeld = _output;
            var bulk = _bulkGrams;
            var purity = _targetPurity;

            _product = null;

            var taken = _stash.RemoveBulk(product.Id, bulk);
            if (taken <= 0f)
            {
                // Stand him up first. Returning here without clearing the scenario left the
                // player crouched over a counter with no batch running and no way out of it.
                ClearWorkScenario();

                Notify.Problem("the weight was gone before you finished.");
                return;
            }

            var output = _output ?? product;

            var yield = YieldOf(product, output, taken, purity);
            var made = _stash.AddPackaged(output.Id, yield, purity);

            _state.Touch();

            ClearWorkScenario();

            var named = outputHeld ?? product;

            Notify.Ticker("~g~" + named.Amount(made) + "~s~ of " + named.Name + " at " +
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
            UI.Draw.Text(_product.WorkVerb.ToUpperInvariant() + " " + _product.Name.ToUpperInvariant() + "  " +
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
