using System;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Dealing;
using Hoodrich.Economy;
using Hoodrich.Gangs;
using Hoodrich.Locations;
using Hoodrich.Missions;
using Hoodrich.Social;
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
        private readonly Armourer _bigj;

        /// <summary>
        /// The block, talking about itself, and the screen it is read on.
        ///
        /// The feed keeps filling whether or not the screen is open, because a timeline that
        /// only writes itself while you are looking at it is a timeline you can watch being
        /// written, and that is the one thing it must never look like.
        /// </summary>
        private readonly SocialFeed _social;
        private readonly SocialScreen _socialScreen;

        /// <summary>
        /// The people who stand near the people who matter. One each for Lamar and Stretch;
        /// their coordinates are the men's own, so the two sets never need keeping in step.
        /// </summary>
        private readonly Entourage _lamarCrew;
        private readonly Entourage _stretchCrew;

        /// <summary>
        /// People who live here, and the other sets coming to take it off them.
        ///
        /// Kept together because they are the same idea from two ends: a block is only worth
        /// attacking if somebody is standing on it, and people standing on it are only
        /// interesting if somebody might come.
        /// </summary>
        private readonly CopWatch _copWatch;
        private readonly BlockLife _block;
        private readonly GangWar _war;
        private ArmourerTalk _bigjTalk;
        private DealerTalk _juanTalk;
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
                _cutting = new Cutting(_state.Stash, _state);
                _cook = new CookScreen();
                _kitchen = new Kitchen(OpenKitchen, () => _cutting.IsBusy);
                _chop = new Dog();
                _postUp = new PostUp(_cfg, _state, _pricing) { Turf = _turf, Crew = _crew, Bust = _bust };
                _bust.Post = _postUp;
                _leaders = new GangLeaders(_cfg, _gangs, _zoneMap, _crew, _state);

                _jobs = new MissionRunner(_state, _crew, _gangs, _zoneMap);

                _fixer = new Fixer(_crew);
                _bigj = new Armourer(_crew, _gangs);

                _social = SocialFeed.Load();
                _socialScreen = new SocialScreen(_social);

                // Two for Lamar, on their own marks -- one on watch, one smoking, because a
                // courtyard where both men are doing the same thing looks staged.
                // No standing crowds any more. Four stoops of people who did nothing mostly got
                // in the way of the fight, and the block reading as occupied during a war and
                // quiet the rest of the time is closer to true anyway.
                _block = new BlockLife(_gangs, "families");

                _copWatch = new CopWatch();

                _war = new GangWar(_gangs, _crew, _state)
                    .Defend("Lamar", Fixer.Spot)
                    .Defend("Grimes", new Vector3(-129.187f, -1461.375f, 33.823f));

                _lamarCrew = new Entourage(_gangs, "families", Fixer.Spot, 206f, "Lamar")
                    .Stand(new Vector3(-82.4f, -1613.8f, 31.485f), 120f, "WORLD_HUMAN_GUARD_STAND")
                    .Stand(new Vector3(-89.1f, -1607.9f, 31.485f), 250f, "WORLD_HUMAN_SMOKING_POT");

                // One for Stretch, on the spot it was read off the HUD at.
                var stretch = _leaders.Get("families");
                _stretchCrew = stretch == null
                    ? null
                    : new Entourage(_gangs, "families",
                                    new Vector3(stretch.SpotX, stretch.SpotY, stretch.SpotZ),
                                    stretch.Heading, stretch.Name)
                        .Stand(new Vector3(-161.084f, -1635.432f, 34.029f), 70.469f, "WORLD_HUMAN_GUARD_STAND");

                if (stretch != null)
                {
                    _war.Defend(stretch.Name, new Vector3(stretch.SpotX, stretch.SpotY, stretch.SpotZ));
                }


                // Handed over as functions rather than references, so the feed never holds on
                // to a system that can be torn down under it.
                _social.WhereYouAre = ZoneNameHere;
                _social.YourGang = () => _crew.IsAffiliated ? _crew.Current.Name : "";
                _social.Changed = () => { _state.Followers = _social.Followers; _state.Touch(); };

                _social.Start(_state.Followers);

                _state.RankedUp = rank => _social.On(SocialEvent.RankUp);

                _jobs.Social = _social;
                _war.Social = _social;
                _war.Busy = () => _jobs != null && _jobs.IsRunning;
                _copWatch.Social = _social;
                _postUp.Social = _social;
                _crew.Social = _social;

                _talk = new Conversation();

                // The bike job runs its own exchange on the court, so it needs the same screen.
                _jobs.Talk = _talk;

                _info = new InfoPanel();
                _stashScreen = new StashScreen();
                _leaderTalk = new LeaderTalk(_leaders, _gangs, _crew, _state, _drugs, _pricing, _cfg);
                _leaders.Talk = _talk;
                _leaders.TalkBuilder = def => _leaderTalk.Root(def);
                _leaderTalk.Social = _social;

                _fixerTalk = new FixerTalk(_fixer, _missions, _jobs, _crew, _state);
                _fixer.Talk = _talk;
                _fixer.TalkBuilder = () => _fixerTalk.Root();

                _bigjTalk = new ArmourerTalk(_bigj, _crew, _state);
                _bigj.Talk = _talk;
                _bigj.TalkBuilder = () => _bigjTalk.Root();
                _juanTalk = new DealerTalk(_delivery, _drugs, _pricing, _state, _crew)
                {
                    House = _stash.Stash
                };

                _delivery.Talk = _talk;
                _delivery.TalkBuilder = () => _juanTalk.Root();

                // He delivers to an address, so he needs the address -- and the only place you
                // can call him from is standing at it.
                _delivery.AtHome = () => _stash.AtDoor;
                _dealers.AtHome = () => _stash.AtDoor;
                _delivery.HouseDoor = _stash.Position;
                _delivery.House = _stash.Stash;

                var pages = new WheelPages(_cfg, _state, _drugs, _pricing, _cutting,
                                           _gangs, _crew, _turf, _dealers, _weapons, _market, _stash, _postUp, _leaders);

                pages.ShowVanillaWheel = () => _wheel.ShowVanillaWheel();
                pages.Info = _info;
                pages.Delivery = _delivery;
                pages.StashScreen = _stashScreen;
                pages.ShowSocials = () => _socialScreen.Open();
                pages.Followers = () => _social.Followers;
                pages.WipeSocials = () => _social.Wipe();



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

                // The feed owns the screen like every other full UI. It was the one screen that
                // did not, so the wheel could be opened on top of it, both fought over up and
                // down, and every walk-up prompt in the mod carried on showing behind it.
                if (_socialScreen.IsOpen)
                {
                    if (!available) _socialScreen.Close();
                    else
                    {
                        _socialScreen.Update();
                        _socialScreen.Draw();

                        // The block keeps talking while you read it, which is the entire point.
                        _social.Update();

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
                        _bigj.ReleaseFromTalk();
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
                    _delivery.UpdatePrompt();
                    _cutting.Update();
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
                    _bigj.Update();
                    _bigj.UpdatePrompt();

                    _social.Update();
                    _copWatch.Update();
                    _block.Update();
                    _war.Update();

                    _lamarCrew.Update();
                    if (_stretchCrew != null) _stretchCrew.Update();
                    _jobs.Update();
                    UpdateLoan();
                }

                // In-flight work keeps ticking even when unavailable so it can abort cleanly --
                // and because a narc's clock does not stop just because a cutscene started.
                _bust.Update();
                _postUp.Update();

                _war.Draw();
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
                    Notify.Failure("Hoodrich shut itself off for this session. Check the log.");
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
            if (_state.Notoriety > 0f && !_bust.CallInProgress && !_turf.IsExposed)
            {
                _state.AddNotoriety(-NotorietyDecayPerSecond * Math.Min(elapsedSeconds, 5f));
            }

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

            Notify.Failure((gang == null ? "They" : gang.Name) + " wrote your debt off. You're done with them.");
        }

        /// <summary>
        /// Ends a conversation you have walked out of. Talking is a thing done at arm's length,
        /// so a dialogue box that follows you down the street would be a bug, not a feature.
        /// </summary>
        private bool WalkedAwayFromTalk()
        {
            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return true;

            var subject = _talk.Subject as LeaderDef;
            if (subject == null) return false;

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

        /// <summary>
        /// The neighbourhood the player is actually in, in words.
        ///
        /// Asked of the game rather than worked out from coordinates, because the game already
        /// knows and its answer is the one the map agrees with.
        /// </summary>
        private string ZoneNameHere()
        {
            try
            {
                var player = Game.Player.Character;
                if (player == null || !player.Exists()) return "";

                var pos = player.Position;
                var code = Function.Call<string>(Hash.GET_NAME_OF_ZONE, pos.X, pos.Y, pos.Z);

                var zone = _zoneMap == null ? null : _zoneMap.Get(code);
                return zone == null || string.IsNullOrEmpty(zone.Name) ? "the block" : zone.Name;
            }
            catch
            {
                return "the block";
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
            // First, and outside every other try. Everything else in here is litter; this one
            // is the player being unable to attract a police car for the rest of the session
            // because the script unloaded while somebody was holding the switch.
            try { LawHold.ReleaseAll(); } catch { /* teardown */ }

            try { _wheel?.RestoreWorld(); }
            catch { try { Game.TimeScale = 1f; } catch { /* nothing more we can do */ } }

            try { _crew?.RestoreWorld(); } catch { /* teardown */ }
            try { _dealers?.RestoreWorld(); } catch { /* teardown */ }
            try { _delivery?.RestoreWorld(); } catch { /* teardown */ }
            try { _deadDrop?.RestoreWorld(); } catch { /* teardown */ }
            try { _postUp?.RestoreWorld(); } catch { /* teardown */ }
            try { _bust?.RestoreWorld(); } catch { /* teardown */ }
            try { _leaders?.RestoreWorld(); } catch { /* teardown */ }
            try { _fixer?.RestoreWorld(); } catch { /* teardown */ }
            try { _bigj?.RestoreWorld(); } catch { /* teardown */ }
            try { _socialScreen?.RestoreWorld(); } catch { /* teardown */ }
            try { _block?.RestoreWorld(); } catch { /* teardown */ }
            try { _war?.RestoreWorld(); } catch { /* teardown */ }
            try { _lamarCrew?.RestoreWorld(); } catch { /* teardown */ }
            try { _stretchCrew?.RestoreWorld(); } catch { /* teardown */ }
            try { _chop?.RestoreWorld(); } catch { /* teardown */ }
            try { _jobs?.RestoreWorld(); } catch { /* teardown */ }
            try { _stash?.RestoreWorld(); } catch { /* teardown */ }
        }
    }
}
