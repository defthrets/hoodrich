using System;
using GTA;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Dealing;
using Hoodrich.Economy;
using Hoodrich.Gangs;
using Hoodrich.Locations;
using Hoodrich.State;
using Hoodrich.Supply;
using Hoodrich.Territory;
using Hoodrich.UI;
using Hoodrich.Weapons;
using Hoodrich.Wheel;

namespace Hoodrich
{
    /// <summary>
    /// Script entry point and the single owner of the update loop.
    ///
    /// Hoodrich deliberately exposes ONE Script subclass. SHVDN instantiates every Script it
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
        private readonly DealerManager _dealers;
        private readonly WeaponRegistry _weapons;
        private readonly Market _market;
        private readonly Bust _bust;
        private readonly DeadDrop _deadDrop;
        private readonly TerritoryState _territory;
        private readonly TurfWar _war;
        private readonly HideoutManager _hideouts;
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
                // otherwise win name resolution over Hoodrich.Core.Settings.
                _cfg = Core.Settings.Load();

                _drugs = Drugs.Load();
                _gangs = GangRegistry.Load();
                _dealers = DealerManager.Load(_cfg);
                _weapons = WeaponRegistry.Load();

                // Everything the save writes into has to exist before the save is read.
                _state = new PlayerState();
                _crew = new Affiliation(_gangs);
                _territory = new TerritoryState(_cfg);
                _hideouts = new HideoutManager(_cfg, _territory);
                _market = new Market(_cfg);

                SaveGame.Load(_state, _crew, _market, _territory, _hideouts);

                _turf = new TurfWatch(_gangs, _crew, _state) { Territory = _territory };
                _war = new TurfWar(_cfg, _state, _crew, _territory);
                _bust = new Bust(_cfg, _state) { Turf = _turf };
                _deadDrop = new DeadDrop(_cfg, _state);

                _pricing = new Pricing(_cfg, _state) { Turf = _turf, Crew = _crew, Market = _market };
                _deal = new StreetDeal(_state, _pricing) { Turf = _turf, Crew = _crew, Bust = _bust };
                _cutting = new Cutting(_state.Stash, _state);

                var pages = new WheelPages(_cfg, _state, _drugs, _pricing, _deal, _cutting,
                                           _gangs, _crew, _turf, _dealers, _weapons, _market, _war, _hideouts);

                _menu = new RadialMenu(_cfg);
                _wheel = new WheelController(_cfg, _menu, pages.BuildRoot);

                Interval = 0;
                Tick += OnTick;
                Aborted += OnAborted;

                Log.Info("Hoodrich " + Build.Version + " loaded. Wheel: " +
                         (_cfg.WheelMode == WheelMode.Replace
                             ? "weapon-wheel button"
                             : _cfg.WheelKey.ToString()) + ".");
            }
            catch (Exception ex)
            {
                _parked = true;
                Log.Error("Hoodrich failed to initialise and is disabled for this session.", ex);
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
                    _dealers.Update(_turf, _crew, _state);
                    _dealers.GreetIfNeeded();
                    _cutting.Update();
                    _bust.Update();
                    _deadDrop.Update();
                    _market.Update(_drugs);
                    _war.Update();
                    _hideouts.Update(_turf);
                    _territory.UpgradeTick();
                    UpdateLoan();
                }

                // In-flight deals keep ticking even when unavailable so they can abort cleanly.
                _deal.Update();
                _cutting.Draw();
                _bust.Draw();
                _war.Draw();
                _hideouts.Draw();

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
                    Log.Error("Too many consecutive failures; Hoodrich is parked for this session.");
                    Notify.Failure("disabled for this session. See scripts\\Hoodrich.log.");
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
                SaveGame.Save(_state, _crew, _market, _territory, _hideouts);
            }
        }

        /// <summary>
        /// Ticks the gang loan. Defaulting is the crew deciding you are a problem: the debt is
        /// written off, your standing with them is destroyed, and you are out.
        /// </summary>
        private void UpdateLoan()
        {
            var loan = _crew.Loan;
            if (loan == null || !loan.IsActive) return;

            if (!loan.Update(_cfg.LoanPeriodDays, _cfg.LoanDefaultAfterMissed, _cfg.LoanVigGrowthPercent)) return;

            var gang = _crew.GangById(loan.GangId);
            var standing = _crew.StandingFor(loan.GangId);
            standing.Rep = -100f;

            if (gang != null && _crew.IsAffiliated && _crew.Current.Id == gang.Id) _crew.Leave();

            _crew.Loan = null;
            _state.AddRespect(-40f);
            _state.Touch();

            Notify.Failure((gang == null ? "They" : gang.Name) + " wrote your debt off. You are done with them.");
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
                SaveGame.Save(_state, _crew, _market, _territory, _hideouts, true);
                Log.Info("Hoodrich unloaded cleanly.");
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
            try { _dealers?.RestoreWorld(); } catch { /* teardown */ }
            try { _deadDrop?.RestoreWorld(); } catch { /* teardown */ }
            try { _war?.RestoreWorld(); } catch { /* teardown */ }
            try { _hideouts?.RestoreWorld(); } catch { /* teardown */ }
        }
    }
}
