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

        private readonly Stash _stash;
        private readonly PlayerState _state;

        /// <summary>
        /// The cupboard, for anything that will not fit in your pockets when a batch lands.
        ///
        /// AddPackaged clamps silently at free space and returns what it took -- so a yield
        /// bigger than the room left was simply DELETED, and the ticker still announced all of
        /// it. Worked weight vanishing between the counter and your pocket is a worse bug than
        /// the one that stopped the batch starting.
        /// </summary>
        public Stash House;

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

        /// <summary>Whether the scenario fallback has been used for this batch.</summary>
        private bool _scenarioTried;

        /// <summary>How long the real clips get before the fallback is allowed in.</summary>
        private const int ScenarioAfterMs = 2500;

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
        public string TryStart(DrugDef product, DrugDef output, float bulkGrams,
                               float targetPurity)
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
            _scenarioTried = false;
            _bulkGrams = bulkGrams;
            _targetPurity = targetPurity;
            _startedAt = Game.GameTime;

            // How long it takes is how much went in, and nothing else now.
            //
            // There used to be a per-bag term on top of this, because the screen let you pick
            // what it was bagged into and that had to be worth picking. With the choice gone
            // the term had nothing driving it -- left at a package size of one it would have
            // put every batch on the old singles timing, which was the slowest of the four and
            // the one nobody would have chosen. A 250g batch ran sixty seconds that way and
            // runs twenty-six now, which is where the ounces setting already sat.
            var work = BaseDurationMs + (int)(bulkGrams * MsPerGram);

            _durationMs = Math.Min(MaxDurationMs, Math.Max(BaseDurationMs, work));

            StandAtTheCounter();
            _startPosition = Game.Player.Character.Position;

            Notify.Ticker(product.SplitVerb + " " + product.Amount(bulkGrams) + " of " + product.Name +
                          " at " + (targetPurity * 100f).ToString("0") + "%...");

            // The animation gets the first go, not the scenario. The scenario is only reached
            // from Update, and only after the clips have had a fair chance to stream in.
            _anim.Start(Game.Player.Character, product.Id);
            return null;
        }

        /// <summary>
        /// Where the work happens, and which way he faces while he does it.
        ///
        /// Read off the HUD standing at the worktop. The batch used to begin wherever the
        /// player happened to be stood when they closed the menu -- half a metre back, facing
        /// the fridge -- and the animation then played into thin air beside the counter rather
        /// than over it.
        ///
        /// Only applied at the counter. Cutting is a kitchen job, but a snap is a teleport, and
        /// a teleport that fires anywhere is worse than an animation that is slightly off.
        /// </summary>
        private static readonly Vector3 CounterSpot = new Vector3(-11.253f, -1428.113f, 31.101f);

        private const float CounterHeading = 356.486f;

        /// <summary>Close enough to the worktop that being put on the mark is a nudge.</summary>
        private const float CounterSnapRange = 3f;

        private static void StandAtTheCounter()
        {
            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            try
            {
                if (player.Position.DistanceTo(CounterSpot) > CounterSnapRange) return;

                player.Position = CounterSpot;
                player.Heading = CounterHeading;
            }
            catch
            {
                // He works where he stands.
            }
        }

        /// <summary>
        /// The last resort, if no clip in the catalogue will load on this install.
        ///
        /// This used to run FIRST, on every batch, before the animation was even asked for --
        /// so the scenario went out, the animation replaced it a frame or two later, and for
        /// that gap the player crouched. CROUCH_INSPECT is also the wrong shape for a worktop:
        /// it is somebody examining something on the floor.
        /// </summary>
        private static readonly string[] WorkScenarios =
        {
            "WORLD_HUMAN_DRUG_DEALER", "WORLD_HUMAN_STAND_IMPATIENT"
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
            // Not once the fallback is in. Start issues a TASK_PLAY_ANIM, which would cancel
            // the scenario on the very next tick and then fail again -- a man twitching between
            // two animations for the rest of the batch.
            if (!_anim.IsPlaying && !_scenarioTried)
            {
                _anim.Start(player, _product.Id);

                // Given a fair go and still nothing, so he at least does something with his
                // hands rather than standing to attention over a counter for the whole batch.
                if (!_anim.IsPlaying && !_scenarioTried &&
                    Game.GameTime - _startedAt > ScenarioAfterMs)
                {
                    _scenarioTried = true;
                    PlayWorkScenario();
                    Log.Debug("No prep animation would load; fell back to a scenario.");
                }
            }

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

            // Whatever would not fit goes in the cupboard rather than nowhere.
            var over = yield - made;
            if (over > 0.005f && House != null) made += House.AddPackaged(output.Id, over, purity);

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
