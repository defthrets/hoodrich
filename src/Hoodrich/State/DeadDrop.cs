using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Economy;
using Hoodrich.UI;

namespace Hoodrich.State
{
    /// <summary>
    /// What happens to your product when you go down.
    ///
    /// Dying drops what you were carrying as a bag on the spot -- blipped, and recoverable if
    /// you can get back to it before it is gone. Getting arrested does not: the police keep it.
    /// That asymmetry is deliberate. It makes a shootout survivable and a bust final, which is
    /// what gives the heat system teeth.
    /// </summary>
    internal sealed class DeadDrop
    {
        private const float PickupRange = 2.2f;
        private const int CheckIntervalMs = 500;

        /// <summary>Prop models tried in order; the first present in the install wins.</summary>
        private static readonly string[] BagModels =
        {
            "prop_cs_heist_bag_02", "prop_cs_heist_bag_01", "prop_ld_suitcase_01",
            "prop_cs_duffel_01", "prop_michael_backpack"
        };

        private readonly Settings _cfg;
        private readonly PlayerState _state;

        /// <summary>
        /// What is in the bag, and how strong it is.
        ///
        /// Bulk used to be grams alone, and that was a laundry. AddBulk takes purity as an
        /// OPTIONAL argument defaulting to pure, so handing it back without one meant anything
        /// dropped came home at a hundred per cent -- walk into a search holding stepped-on
        /// weight, drop it, get searched, pick it up clean. The mod deliberately lets you drop
        /// product before a search, which is the whole reason this mattered: the exploit was
        /// not a corner case, it was the feature.
        ///
        /// The packaged side had it right all along and is the shape being copied here.
        /// </summary>

        private bool _wasDead;
        private bool _wasArrested;
        private int _lastCheck;

        public DeadDrop(Settings cfg, PlayerState state)
        {
            _cfg = cfg;
            _state = state;
        }

        /// <summary>Set by Main: whether the bag is on his back right now.</summary>
        public Func<bool> Satchel;

        /// <summary>Set by Main: taking it off him where he fell. See Strap.DropWhereHeFell.</summary>
        public Action<Vector3, Ped> DropSatchel;

        /// <summary>
        /// Dying and being arrested, watched EVERY FRAME, playable or not.
        ///
        /// THIS SAT IN THE PLAYABLE TICK, and the playable tick stands down the moment you are
        /// dead or cuffed -- so the one thing it existed to notice was the one thing it could
        /// never see. By the time it ran again you were stood outside Pillbox, alive, with
        /// everything still in your pockets. Two deaths in the log and no bag either time.
        /// Same shape as the job runner's Died: read on the frame he goes down.
        /// </summary>
        public void Watch()
        {
            var now = Game.GameTime;
            if (now - _lastCheck < CheckIntervalMs) return;
            _lastCheck = now;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            CheckArrest(player);
            CheckDeath(player);
        }

        private void CheckArrest(Ped player)
        {
            bool arrested;
            try
            {
                arrested = Function.Call<bool>(Hash.IS_PLAYER_BEING_ARRESTED, Game.Player.Handle, false)
                           || player.IsCuffed;
            }
            catch
            {
                arrested = false;
            }

            if (arrested && !_wasArrested)
            {
                _wasArrested = true;
                LoseToPolice();
            }
            else if (!arrested && _wasArrested && player.IsAlive)
            {
                _wasArrested = false;
            }
        }

        private void CheckDeath(Ped player)
        {
            var dead = !player.IsAlive;

            if (dead && !_wasDead)
            {
                _wasDead = true;

                // ---- THE BAG TAKES IT, OR THE POCKETS DO ----
                //
                // WEARING IT, HE DROPS IT. Twenty slots is a night's work and losing a cut of
                // it to a stray round in an alley is not a rule anybody would choose to play
                // with -- so the bag comes off where he fell, contents intact, and walking back
                // for it is the cost. Losing it is then something he decides by not going.
                //
                // NOT WEARING IT, THE POCKETS PAY -- AND THEY PAY FOR GOOD. What is in a
                // jacket goes down with the man in it. There is no second bag on the pavement
                // and no blip to walk back to, because that was the bag's whole job and it
                // made carrying one pointless: dying without the bag cost exactly what dying
                // with it cost, one extra walk.
                //
                // So the two outcomes say two different things now. Wearing it is a walk back.
                // Not wearing it is the reason to wear it.
                if (Satchel != null && Satchel())
                {
                    if (DropSatchel != null) DropSatchel(player.Position, player);
                    return;
                }

                DropBag(player.Position);
            }
            else if (!dead && _wasDead)
            {
                _wasDead = false;
            }
        }

        // ---- losing it ---------------------------------------------------------

        private void LoseToPolice()
        {
            var fraction = Clamp01(_cfg.LoseOnArrestPercent / 100f);
            if (fraction <= 0f) return;

            var taken = Confiscate(fraction, null, null);
            if (taken <= 0.005f) return;

            _state.Touch();
            Notify.Failure("they took " + taken.ToString("0.#") + "g off you.");
            Log.Info("Arrest: lost " + taken.ToString("0.##") + "g.");
        }

        private void DropBag(Vector3 where)
        {
            var fraction = Clamp01(_cfg.LoseOnDeathPercent / 100f);
            if (fraction <= 0f) return;

            // NOTHING IS WRITTEN DOWN, so there is nothing to hand back. Both dictionaries
            // stay null: Confiscate records what it took only when it is given somewhere to
            // record it, and this is the path whose whole point is that it is gone.
            //
            // THE "DeathBagRecoverable" SETTING WENT WITH IT. It was the entire recovery
            // mechanism before the satchel existed, and once the satchel arrived it made the
            // satchel pointless -- switched on, dying without the bag cost exactly what dying
            // with it cost. Two switches for one rule, and the one on the settings screen
            // contradicted the one in the fiction.
            var taken = Confiscate(fraction, null, null);

            if (taken <= 0.005f) return;

            _state.Touch();

            // SAID LATER. This runs on the frame he goes down, behind the death fade, and a
            // notification behind a black screen is a notification thrown away. Main asks for
            // it once he is stood outside Pillbox -- see TakeDeathNotice.
            _notice = "~r~You lost " + taken.ToString("0.#") + "g.~s~ It was in your pockets.";

            Log.Info("Death: lost " + taken.ToString("0.##") + "g out of his pockets, for good.");
        }

        /// <summary>What the death cost, handed over once there is a screen to read it on.</summary>
        public string TakeDeathNotice()
        {
            var said = _notice;
            _notice = null;
            return said;
        }

        private string _notice;

        /// <summary>
        /// Removes a fraction of everything held. When given dictionaries, records what was
        /// taken so it can be handed back; otherwise the product is simply gone.
        /// </summary>
        private float Confiscate(float fraction, Dictionary<string, Holding> bulkOut,
                                 Dictionary<string, Holding> packagedOut)
        {
            var stash = _state.Stash;
            var total = 0f;

            // Snapshot the keys first: removing mutates the collections being read.
            var bulkIds = new List<string>();
            var packagedIds = new List<string>();
            foreach (var d in AllDrugIds())
            {
                if (stash.BulkOf(d) > 0.005f) bulkIds.Add(d);
                if (stash.PackagedOf(d) > 0.005f) packagedIds.Add(d);
            }

            foreach (var id in bulkIds)
            {
                // Read BEFORE the removal, and from the bulk side rather than the packaged one.
                // RemoveBulk can empty the holding, and an emptied holding reports pure.
                var purity = stash.BulkPurityOf(id);

                var amount = stash.BulkOf(id) * fraction;
                var taken = stash.RemoveBulk(id, amount);
                if (taken <= 0.005f) continue;

                total += taken;
                if (bulkOut != null) bulkOut[id] = new Holding { Grams = taken, Purity = purity };
            }

            foreach (var id in packagedIds)
            {
                var purity = stash.PurityOf(id);
                var amount = stash.PackagedOf(id) * fraction;
                var taken = stash.RemovePackaged(id, amount);
                if (taken <= 0.005f) continue;

                total += taken;
                if (packagedOut != null) packagedOut[id] = new Holding { Grams = taken, Purity = purity };
            }

            return total;
        }

        /// <summary>
        /// Product ids currently held. Taken from the stash rather than the catalogue so a
        /// product removed from drugs.json cannot strand weight in a save.
        /// </summary>
        private IEnumerable<string> AllDrugIds()
        {
            var ids = new List<string>();
            var doc = _state.Stash.ToJson();

            foreach (var key in doc["bulk"].Keys) if (!ids.Contains(key)) ids.Add(key);
            foreach (var key in doc["packaged"].Keys) if (!ids.Contains(key)) ids.Add(key);

            return ids;
        }

        // ---- the bag -----------------------------------------------------------

        /// <summary>
        /// Takes back as much as will fit, and leaves the rest in the bag.
        ///
        /// It used to empty the bag whatever happened -- walk over your own product with a full
        /// bag and the message read "no room for any of it" while the bag was deleted and every
        /// gram in it destroyed. Now what does not fit stays on the pavement where you dropped
        /// it, and the bag stays with it, so you can go and make room.
        /// </summary>
        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

    }
}
