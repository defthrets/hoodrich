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

        private readonly Dictionary<string, float> _bagBulk = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
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

        public void Update()
        {
            var now = Game.GameTime;
            if (now - _lastCheck < CheckIntervalMs) return;
            _lastCheck = now;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            CheckArrest(player);
            CheckDeath(player);
            CheckBagLifetime(player);
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

            _bagBulk.Clear();
            _bagPackaged.Clear();

            var taken = Confiscate(fraction, _bagBulk, _bagPackaged);
            if (taken <= 0.005f) return;

            _state.Touch();

            if (!SpawnBag(where))
            {
                // Nowhere to put it: the product is simply gone.
                _bagBulk.Clear();
                _bagPackaged.Clear();
                Notify.Failure("you lost " + taken.ToString("0.#") + "g.");
                return;
            }

            _bagDroppedAt = Game.GameTime;
            Notify.Important("~o~You dropped " + taken.ToString("0.#") + "g.~s~ It's on your map -- go get it.");
            Log.Info("Death: dropped " + taken.ToString("0.##") + "g at " + where + ".");
        }

        /// <summary>
        /// Removes a fraction of everything held. When given dictionaries, records what was
        /// taken so it can be handed back; otherwise the product is simply gone.
        /// </summary>
        private float Confiscate(float fraction, Dictionary<string, float> bulkOut,
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
                var amount = stash.BulkOf(id) * fraction;
                var taken = stash.RemoveBulk(id, amount);
                if (taken <= 0.005f) continue;

                total += taken;
                if (bulkOut != null) bulkOut[id] = taken;
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

        private void Recover()
        {
            var stash = _state.Stash;
            var back = 0f;

            foreach (var kv in _bagBulk) back += stash.AddBulk(kv.Key, kv.Value);
            foreach (var kv in _bagPackaged) back += stash.AddPackaged(kv.Key, kv.Value.Grams, kv.Value.Purity);

            _bagBulk.Clear();
            _bagPackaged.Clear();
            ClearBag();
            _state.Touch();

            Notify.Ticker(back > 0.005f
                ? "~g~Picked your bag back up.~s~ " + back.ToString("0.#") + "g"
                : "~o~No room for any of it.~s~");

            Log.Info("Dead drop recovered: " + back.ToString("0.##") + "g.");
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

        public void RestoreWorld() => ClearBag();
    }
}
