using System;
using GTA;
using GTA.Native;
using Trapline.Core;
using Trapline.Dealing;
using Trapline.Economy;
using Trapline.Gangs;
using Trapline.State;
using Trapline.Supply;
using Trapline.Territory;
using Trapline.UI;
using Trapline.Wheel;

namespace Trapline
{
    /// <summary>
    /// Script entry point and the single owner of the update loop.
    ///
    /// Trapline deliberately exposes ONE Script subclass. SHVDN instantiates every Script it
    /// finds and ticks them in an unspecified order; a single entry point means subsystem
    /// update order is ours to define, and there is exactly one place that has to be
    /// exception-safe.
    /// </summary>
    public sealed class Main : Script
    {
        /// <summary>Cadence for work that does not need to run every frame.</summary>
        private const int SlowTickMs = 1000;

        /// <summary>Notoriety bled off per second of not being noticed.</summary>
        private const float NotorietyDecayPerSecond = 0.15f;

        /// <summary>Consecutive tick failures before the script parks itself.</summary>
        private const int MaxConsecutiveFailures = 10;

        private readonly Core.Settings _cfg;
        private readonly PlayerState _state;
        private readonly Drugs _drugs;
        private readonly GangRegistry _gangs;
        private readonly Affiliation _crew;
        private readonly TurfWatch _turf;
        private readonly Pricing _pricing;
        private readonly StreetDeal _deal;
        private readonly Cutting _cutting;
        private readonly SupplierManager _suppliers;
        private readonly RadialMenu _menu;
        private readonly WheelController _wheel;

        private int _lastSlowTick;
        private int _lastSave;
        private int _failures;
        private bool _parked;

        public Main()
        {
            // Anything thrown out of a Script constructor kills the script before it ever ticks,
            // and SHVDN reports it as a bare load failure. Fail soft and park instead.
            try
            {
                // Fully qualified: Script exposes an inherited `Settings` property that would
                // otherwise win name resolution over Trapline.Core.Settings.
                _cfg = Core.Settings.Load();

                _drugs = Drugs.Load();
                _gangs = GangRegistry.Load();
                _suppliers = SupplierManager.Load();

                _state = new PlayerState();
                _crew = new Affiliation(_gangs);
                SaveGame.Load(_state, _crew);

                _turf = new TurfWatch(_gangs, _crew, _state);

                _pricing = new Pricing(_cfg, _state) { Turf = _turf, Crew = _crew };
                _deal = new StreetDeal(_state, _pricing) { Turf = _turf, Crew = _crew };
                _cutting = new Cutting(_state.Stash, _state);

                var pages = new WheelPages(_state, _drugs, _pricing, _deal, _cutting,
                                           _gangs, _crew, _turf, _suppliers);

                _menu = new RadialMenu(_cfg);
                _wheel = new WheelController(_cfg, _menu, pages.BuildRoot);

                Interval = 0;
                Tick += OnTick;
                Aborted += OnAborted;

                Log.Info("Trapline " + Build.Version + " loaded. Wheel: " +
                         (_cfg.WheelMode == WheelMode.Replace
                             ? "weapon-wheel button"
                             : _cfg.WheelKey.ToString()) + ".");
            }
            catch (Exception ex)
            {
                _parked = true;
                Log.Error("Trapline failed to initialise and is disabled for this session.", ex);
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (_parked || _cfg == null || !_cfg.Enabled) return;

            try
            {
                Draw.BeginFrame();

                var available = IsPlayable();

                _wheel.Update(available);

                if (available)
                {
                    _crew.Update();
                    _turf.Update();
                    _suppliers.Update();
                    _cutting.Update();
                }

                // In-flight deals keep ticking even when unavailable so they can abort cleanly.
                _deal.Update();
                _cutting.Draw();

                SlowTick();

                _failures = 0;
            }
            catch (Exception ex)
            {
                _failures++;
                Log.Error("Tick failed (" + _failures + "/" + MaxConsecutiveFailures + ").", ex);

                // Never leave the world in a modified state because of our own bug.
                TryRestore();

                if (_failures >= MaxConsecutiveFailures)
                {
                    _parked = true;
                    Log.Error("Too many consecutive failures; Trapline is parked for this session.");
                    Notify.Failure("disabled for this session. See scripts\\Trapline.log.");
                }
            }
        }

        private void SlowTick()
        {
            var now = Game.GameTime;
            if (now - _lastSlowTick < SlowTickMs) return;

            var elapsedSeconds = (now - _lastSlowTick) / 1000f;
            _lastSlowTick = now;

            // Heat only cools while you are not actively drawing attention.
            if (_state.Notoriety > 0f && !_deal.IsBusy && !_turf.IsExposed)
            {
                _state.AddNotoriety(-NotorietyDecayPerSecond * Math.Min(elapsedSeconds, 5f));
            }

            _deal.PruneCooldowns();
            _turf.Prune();

            if (_cfg.SaveIntervalSeconds > 0 && now - _lastSave >= _cfg.SaveIntervalSeconds * 1000)
            {
                _lastSave = now;
                SaveGame.Save(_state, _crew);
            }
        }

        /// <summary>True when the player is in normal control and the mod should be live.</summary>
        private bool IsPlayable()
        {
            try
            {
                var player = Game.Player?.Character;
                if (player == null || !player.Exists() || !player.IsAlive) return false;

                if (Game.IsPaused) return false;
                if (!Function.Call<bool>(Hash.IS_PLAYER_CONTROL_ON, Game.Player.Handle)) return false;
                if (Function.Call<bool>(Hash.IS_PAUSE_MENU_ACTIVE)) return false;
                if (Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE)) return false;
                if (!Function.Call<bool>(Hash.IS_SCREEN_FADED_IN)) return false;

                if (_cfg.PauseDuringMission && Function.Call<bool>(Hash.GET_MISSION_FLAG)) return false;

                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Playability probe failed: " + ex.Message);
                return false;
            }
        }

        private void OnAborted(object sender, EventArgs e)
        {
            TryRestore();

            try
            {
                SaveGame.Save(_state, _crew, true);
                Log.Info("Trapline unloaded cleanly.");
            }
            catch (Exception ex)
            {
                Log.Error("Save on abort failed.", ex);
            }
        }

        /// <summary>
        /// Puts back everything global we changed: time scale, timecycle, gang relationships,
        /// and any spawned supplier. A mod that leaves the world altered after unloading is
        /// worse than one that never loaded.
        /// </summary>
        private void TryRestore()
        {
            try { _wheel?.RestoreWorld(); }
            catch { try { Game.TimeScale = 1f; } catch { /* nothing more we can do */ } }

            try { _crew?.RestoreWorld(); } catch { /* teardown */ }
            try { _suppliers?.RestoreWorld(); } catch { /* teardown */ }
        }
    }
}
