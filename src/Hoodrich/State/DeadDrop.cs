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
        private readonly Dictionary<string, Holding> _bagBulk = new Dictionary<string, Holding>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Holding> _bagPackaged = new Dictionary<string, Holding>(StringComparer.OrdinalIgnoreCase);

        private Prop _bag;
        private Blip _bagBlip;
        private int _bagDroppedAt;

        private bool _wasDead;
        private bool _wasArrested;
        private int _lastCheck;

        public DeadDrop(Settings cfg, PlayerState state)
        {
            _cfg = cfg;
            _state = state;
        }

        public bool HasBag => _bag != null && _bag.Exists();

        /// <summary>The bag on the floor: its lifetime, and picking it up. From the playable tick.</summary>
        public void Update()
        {
            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            CheckBagLifetime(player);
        }

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

            // Going down twice without picking the first one up used to leave that bag behind
            // as a persistent prop with a blip on it and nothing tracking either -- a marker
            // pointing at a bag that no longer contained anything.
            var lastBagGone = false;

            if (HasBag)
            {
                ClearBag();
                lastBagGone = true;
            }

            _bagBulk.Clear();
            _bagPackaged.Clear();

            // FOR GOOD, if the ini says so: nothing is written down, so there is nothing to
            // hand back. Otherwise into the bag's own record, to be handed back at the spot.
            var keep = _cfg.DeathBagRecoverable;

            var taken = Confiscate(fraction, keep ? _bagBulk : null, keep ? _bagPackaged : null);

            if (taken <= 0.005f)
            {
                if (lastBagGone) _notice = "~o~Whatever was in the last bag is gone.~s~";
                return;
            }

            _state.Touch();

            // SAID LATER. This runs on the frame he goes down, behind the death fade, and a
            // notification behind a black screen is a notification thrown away. Main asks for
            // it once he is stood outside Pillbox -- see TakeDeathNotice.
            if (!keep || !SpawnBag(where))
            {
                _bagBulk.Clear();
                _bagPackaged.Clear();

                _notice = "~r~You lost " + taken.ToString("0.#") + "g.~s~" +
                          (keep ? " There was nowhere to drop it." : " It went down with you.");

                Log.Info("Death: lost " + taken.ToString("0.##") + "g" + (keep ? " (no bag prop)." : " for good."));
                return;
            }

            _bagDroppedAt = Game.GameTime;

            _notice = (lastBagGone ? "~o~The last bag is gone.~s~ " : "") +
                      "~o~You dropped " + taken.ToString("0.#") + "g.~s~ It's on your map -- go get it.";

            Log.Info("Death: dropped " + taken.ToString("0.##") + "g at " + where + ".");
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

        private bool SpawnBag(Vector3 where)
        {
            foreach (var name in BagModels)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage) continue;
                    if (!model.Request(1500)) continue;

                    _bag = World.CreateProp(model, where, false, false);
                    model.MarkAsNoLongerNeeded();

                    if (_bag == null || !_bag.Exists()) continue;

                    Function.Call(Hash.PLACE_OBJECT_ON_GROUND_PROPERLY, _bag.Handle);
                    _bag.IsPersistent = true;

                    _bagBlip = _bag.AddBlip();
                    if (_bagBlip != null && _bagBlip.Exists())
                    {
                        _bagBlip.Sprite = BlipSprite.Package;
                        _bagBlip.Color = BlipColor.Yellow;
                        _bagBlip.Name = "Dropped product";
                        _bagBlip.ShowRoute = false;
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    Log.Debug("Bag model '" + name + "' failed: " + ex.Message);
                }
            }

            Log.Warn("No usable bag prop; dropped product is lost.");
            return false;
        }

        private void CheckBagLifetime(Ped player)
        {
            if (!HasBag) return;

            var lifeMs = (int)(_cfg.DeadDropDespawnMinutes * 60_000f);
            if (_cfg.DeadDropDespawnMinutes > 0f && Game.GameTime - _bagDroppedAt > lifeMs)
            {
                ClearBag();
                Notify.Ticker("~o~Someone else found your bag.~s~");
                Log.Info("Dead drop expired.");
                return;
            }

            if (player.Position.DistanceTo(_bag.Position) > PickupRange) return;
            if (!player.IsAlive) return;

            Recover();
        }

        /// <summary>
        /// Takes back as much as will fit, and leaves the rest in the bag.
        ///
        /// It used to empty the bag whatever happened -- walk over your own product with a full
        /// bag and the message read "no room for any of it" while the bag was deleted and every
        /// gram in it destroyed. Now what does not fit stays on the pavement where you dropped
        /// it, and the bag stays with it, so you can go and make room.
        /// </summary>
        private void Recover()
        {
            var stash = _state.Stash;
            var back = 0f;

            var bulkLeft = new Dictionary<string, Holding>(StringComparer.OrdinalIgnoreCase);
            var packagedLeft = new Dictionary<string, Holding>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in _bagBulk)
            {
                // AddBulk blends what arrives into whatever is already held, weighted by grams,
                // so a part-recovery into an existing pile averages correctly on its own. What
                // stays behind in the bag keeps the strength it had -- the bag is not a mixer,
                // it is the same product waiting where it was left.
                var took = stash.AddBulk(kv.Key, kv.Value.Grams, kv.Value.Purity);
                back += took;

                var over = kv.Value.Grams - took;
                if (over > 0.005f) bulkLeft[kv.Key] = new Holding { Grams = over, Purity = kv.Value.Purity };
            }

            foreach (var kv in _bagPackaged)
            {
                var took = stash.AddPackaged(kv.Key, kv.Value.Grams, kv.Value.Purity);
                back += took;

                var over = kv.Value.Grams - took;
                if (over > 0.005f) packagedLeft[kv.Key] = new Holding { Grams = over, Purity = kv.Value.Purity };
            }

            _bagBulk.Clear();
            _bagPackaged.Clear();

            foreach (var kv in bulkLeft) _bagBulk[kv.Key] = kv.Value;
            foreach (var kv in packagedLeft) _bagPackaged[kv.Key] = kv.Value;

            var leftOver = _bagBulk.Count > 0 || _bagPackaged.Count > 0;

            if (!leftOver) ClearBag();

            _state.Touch();

            Notify.Ticker(back > 0.005f
                ? (leftOver
                    ? "~g~Took what fits.~s~ " + back.ToString("0.#") + "g -- the rest is still there"
                    : "~g~Picked your bag back up.~s~ " + back.ToString("0.#") + "g")
                : "~o~No room for any of it.~s~ It's still on the floor");

            Log.Info("Dead drop recovered: " + back.ToString("0.##") + "g" +
                     (leftOver ? ", some left in the bag." : "."));
        }

        private void ClearBag()
        {
            try { if (_bagBlip != null && _bagBlip.Exists()) _bagBlip.Delete(); } catch { }
            try
            {
                if (_bag != null && _bag.Exists())
                {
                    _bag.MarkAsNoLongerNeeded();
                    _bag.Delete();
                }
            }
            catch { }

            _bag = null;
            _bagBlip = null;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        /// <summary>
        /// Everything ours, put back -- INCLUDING what is in the bag.
        ///
        /// This was one line, `ClearBag()`, which deletes the prop and the blip and walks away
        /// from the two dictionaries holding the contents. A script reload with a bag on the
        /// floor destroyed every gram in it, silently, and the save had already been touched
        /// when it was dropped, so the loss was banked before anybody noticed.
        ///
        /// DroppedBags.RestoreWorld had this bug and fixed it; its comment is the argument for
        /// this one too. Overflowing a full stash is strictly better than deleting the lot:
        /// whatever will not fit is the only part lost.
        ///
        /// Logged rather than announced. A teardown may have no screen left to draw a ticker
        /// on, and the log is the thing somebody reads afterwards to find out what happened.
        /// </summary>
        public void RestoreWorld()
        {
            try
            {
                var stash = _state == null ? null : _state.Stash;

                if (stash != null)
                {
                    var back = 0f;

                    foreach (var kv in _bagBulk) back += stash.AddBulk(kv.Key, kv.Value.Grams, kv.Value.Purity);
                    foreach (var kv in _bagPackaged) back += stash.AddPackaged(kv.Key, kv.Value.Grams, kv.Value.Purity);

                    if (back > 0.005f)
                    {
                        _state.Touch();

                        Log.Info("Dead drop handed back on teardown: " + back.ToString("0.#") +
                                 "g put in the stash rather than deleted with the bag.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not hand the dead drop back: " + ex.Message);
            }

            _bagBulk.Clear();
            _bagPackaged.Clear();

            ClearBag();
        }
    }
}
