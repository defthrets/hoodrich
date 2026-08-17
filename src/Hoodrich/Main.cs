using System;
using GTA;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Dealing;
using Hoodrich.Economy;
using Hoodrich.Gangs;
using Hoodrich.Locations;
using Hoodrich.Missions;
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
        private readonly Delivery _delivery;
        private readonly WeaponRegistry _weapons;
        private readonly Market _market;
        private readonly Bust _bust;
        private readonly DeadDrop _deadDrop;
        private readonly PostUp _postUp;
        private readonly ZoneMap _zoneMap;
        private readonly GangLeaders _leaders;
        private readonly LeaderTalk _leaderTalk;
        private readonly Conversation _talk;
        private readonly InfoPanel _info;
        private readonly StashScreen _stashScreen;
        private readonly StashHouse _stash;
        private readonly SleepSpot _sleep;
        private readonly Kitchen _kitchen;
        private readonly Dog _chop;
        private readonly MissionBook _missions;
        private readonly Fixer _fixer;
        private readonly FixerTalk _fixerTalk;
        private readonly MissionRunner _jobs;
        private readonly CookScreen _cook;
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
                _delivery = new Delivery();
                _weapons = WeaponRegistry.Load();
                _zoneMap = ZoneMap.Load();
                _missions = MissionBook.Load();

                // Everything the save writes into has to exist before the save is read.
                _state = new PlayerState();
                _crew = new Affiliation(_gangs);
                _stash = new StashHouse(_cfg);
                _sleep = new SleepSpot(_state, () => SaveGame.Save(_state, _crew, _market, _stash, true));
                _market = new Market(_cfg);

                SaveGame.Load(_state, _crew, _market, _stash);

                _turf = new TurfWatch(_gangs, _crew, _state);
                _crew.Turf = _turf;
                _bust = new Bust(_cfg, _state) { Turf = _turf };
                _deadDrop = new DeadDrop(_cfg, _state);

                _pricing = new Pricing(_cfg, _state) { Turf = _turf, Crew = _crew, Market = _market };
                _deal = new StreetDeal(_state, _pricing) { Turf = _turf, Crew = _crew, Bust = _bust };
                _cutting = new Cutting(_state.Stash, _state);
                _cook = new CookScreen();
                _kitchen = new Kitchen(OpenKitchen, () => _cutting.IsBusy);
                _chop = new Dog();
                _postUp = new PostUp(_cfg, _state, _pricing) { Turf = _turf, Crew = _crew };
                _leaders = new GangLeaders(_cfg, _gangs, _zoneMap, _crew, _state);

                _jobs = new MissionRunner(_state, _crew, _gangs, _zoneMap);

                _fixer = new Fixer(_crew);


                _talk = new Conversation();

                _info = new InfoPanel();
                _stashScreen = new StashScreen();
                _leaderTalk = new LeaderTalk(_leaders, _gangs, _crew, _state, _drugs, _pricing, _cfg);
                _leaders.Talk = _talk;
                _leaders.TalkBuilder = def => _leaderTalk.Root(def);

                _fixerTalk = new FixerTalk(_fixer, _missions, _jobs, _crew, _state);
                _fixer.Talk = _talk;
                _fixer.TalkBuilder = () => _fixerTalk.Root();

                var pages = new WheelPages(_cfg, _state, _drugs, _pricing, _deal, _cutting,
                                           _gangs, _crew, _turf, _dealers, _weapons, _market, _stash, _postUp, _leaders);

                pages.ShowVanillaWheel = () => _wheel.ShowVanillaWheel();
                pages.Info = _info;
                pages.Delivery = _delivery;
                pages.StashScreen = _stashScreen;

                

                _menu = new RadialMenu(_cfg);
                _wheel = new WheelController(_cfg, _menu, pages.BuildRoot);

                Interval = 0;
                Tick += OnTick;
                Aborted += OnAborted;

                Log.Info("Paths: data=" + Paths.Data + "  writable=" + Paths.Writable);

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

                // Moving product owns the screen outright, the same as any other full UI.
                if (_stashScreen.IsOpen)
                {
                    if (!available || !_stash.AtDoor) _stashScreen.Close();
                    else
                    {
                        _stashScreen.Update();
                        _stashScreen.Draw();
                        SlowTick();
                        _failures = 0;
                        return;
                    }
                }

                // Working product owns the screen while the choice is being made.
                if (_cook.IsOpen)
                {
                    if (!available || !_kitchen.InReach) _cook.Close();
                    else
                    {
                        _cook.Update();
                        _cook.Draw();
                        SlowTick();
                        _failures = 0;
                        return;
                    }
                }

                // A popup readout owns the screen the same way a conversation does: the wheel
                // would fight it for the same buttons.
                if (_info.IsOpen)
                {
                    if (!available) _info.Close();
                    else
                    {
                        _info.Update();
                        _info.Draw();
                        SlowTick();
                        _failures = 0;
                        return;
                    }
                }


                // A conversation owns the screen while it is up: the wheel would fight it for
                // the same buttons, and you cannot be talking to a man and shopping at once.
                if (_talk.IsOpen)
                {
                    if (!available || WalkedAwayFromTalk())
                    {
                        _talk.Close();
                        _leaders.ReleaseFromTalk();
                        _fixer.ReleaseFromTalk();
                    }
                    else
                    {
                        _talk.Update();
                        _talk.Draw();
                        SlowTick();
                        _failures = 0;
                        return;
                    }
                }

                _wheel.Update(available);

                if (available)
                {
                    _crew.Update();
                    _turf.Update();
                    _dealers.Update(_turf, _crew, _state);
                    _dealers.GreetIfNeeded();
                    _delivery.Update();
                    _cutting.Update();
                    _bust.Update();
                    _deadDrop.Update();
                    _market.Update(_drugs);
                    _stash.Update();
                    _sleep.Update();
                    _kitchen.Update();
                    _chop.Update();
                    _sleep.RestoreOnLoad();
                    _leaders.Update();
                    _leaders.UpdatePrompt();
                    _fixer.Update();
                    _fixer.UpdatePrompt();
                    _jobs.Update();
                    UpdateLoan();
                }

                // In-flight deals keep ticking even when unavailable so they can abort cleanly.
                _deal.Update();
                _postUp.Update();
                _cutting.Draw();
                _bust.Draw();
                _postUp.Draw();
                _leaders.Draw();
                _stash.Draw();
                _sleep.Draw();
                _kitchen.Draw();
                _jobs.Draw();

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

            _postUp.Prune();
            _turf.Prune();

            if (_cfg.SaveIntervalSeconds > 0 && now - _lastSave >= _cfg.SaveIntervalSeconds * 1000)
            {
                _lastSave = now;
                SaveGame.Save(_state, _crew, _market, _stash);
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

        /// <summary>
        /// Ends a conversation you have walked out of. Talking is a thing done at arm's length,
        /// so a dialogue box that follows you down the street would be a bug, not a feature.
        /// </summary>
        private bool WalkedAwayFromTalk()
        {
            var subject = _talk.Subject as LeaderDef;
            if (subject == null) return false;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return true;

            // Measured to the MAN, on the ground plane. Measuring to his authored spot closed
            // the conversation the instant it opened: that spot's height is probed from across
            // the map and is often still zero, so a player standing 30m above sea level was
            // "30 metres away" from somebody they were stood next to.
            return _leaders.DistanceTo(subject) > 8f;
        }

        /// <summary>Opens the kitchen screen with everything it needs to start a batch.</summary>
        private void OpenKitchen()
        {
            _cook.Open(_state.Stash, _drugs, _pricing,
                       (drug, grams, purity) => _cutting.TryStart(drug, grams, purity));
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
                SaveGame.Save(_state, _crew, _market, _stash, true);
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
            try { _delivery?.RestoreWorld(); } catch { /* teardown */ }
            try { _deadDrop?.RestoreWorld(); } catch { /* teardown */ }
            try { _postUp?.RestoreWorld(); } catch { /* teardown */ }
            try { _leaders?.RestoreWorld(); } catch { /* teardown */ }
            try { _fixer?.RestoreWorld(); } catch { /* teardown */ }
            try { _chop?.RestoreWorld(); } catch { /* teardown */ }
            try { _jobs?.RestoreWorld(); } catch { /* teardown */ }
            try { _stash?.RestoreWorld(); } catch { /* teardown */ }
        }
    }
}
