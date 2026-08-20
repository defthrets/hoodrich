using System;
using System.Collections.Generic;
using GTA;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Gangs;
using Hoodrich.State;
using Hoodrich.UI;

namespace Hoodrich.Territory
{
    /// <summary>Whose ground the player is standing on, relative to their own crew.</summary>
    internal enum TurfStatus
    {
        /// <summary>Nobody claims it.</summary>
        Neutral,

        /// <summary>Your own gang's turf.</summary>
        Home,

        /// <summary>A gang you are not at war with.</summary>
        Foreign,

        /// <summary>A rival's turf. Dealing here gets you noticed.</summary>
        Hostile
    }

    /// <summary>
    /// Turf awareness and the consequences of dealing on someone else's block.
    ///
    /// Ownership keys off GTA's own zone codes (GET_NAME_OF_ZONE) rather than authored
    /// polygons: the map already carves Los Santos into named neighbourhoods, so a zone code
    /// is a free, exact, save-stable territory id.
    ///
    /// Aggression is targeted at specific peds via TASK_COMBAT_PED rather than by flipping a
    /// global relationship to Hate, so rivals only turn on you when they have actually clocked
    /// you dealing -- and the rest of the world stays playable.
    /// </summary>
    internal sealed class TurfWatch
    {
        private const int ScanIntervalMs = 1000;

        /// <summary>How long after a sale you still count as "seen dealing".</summary>
        private const int ExposureMs = 45_000;

        private const float SpotRange = 32f;
        private const float ShakedownRange = 22f;

        /// <summary>Per-check chance a rival who can see you decides to do something about it.</summary>
        private const float HostileSpotChance = 0.35f;

        /// <summary>Same, on unclaimed ground, where it is opportunistic rather than territorial.</summary>
        private const float NeutralShakedownChance = 0.10f;

        /// <summary>Once a crew has come for you, hold off this long before rolling again.</summary>
        private const int AggroCooldownMs = 60_000;

        private readonly GangRegistry _gangs;

        /// <summary>Assigned by Main. Zones taken in a war override the starting map.</summary>
        private readonly Affiliation _affiliation;
        private readonly PlayerState _state;
        private readonly Random _rng = new Random();

        private readonly HashSet<int> _aggroed = new HashSet<int>();

        private int _lastScan;
        private int _exposedUntil;
        private int _nextAggroAllowedAt;

        private string _zoneCode = "";
        private string _zoneName = "";
        private GangDef _owner;
        private TurfStatus _status = TurfStatus.Neutral;

        public TurfWatch(GangRegistry gangs, Affiliation affiliation, PlayerState state)
        {
            _gangs = gangs;
            _affiliation = affiliation;
            _state = state;
        }

        /// <summary>Raw zone code, e.g. "DAVIS". This is what goes in gangs.json turf lists.</summary>
        public string ZoneCode => _zoneCode;

        /// <summary>Friendly zone name from the game's text table, e.g. "Davis".</summary>
        public string ZoneName => string.IsNullOrEmpty(_zoneName) ? _zoneCode : _zoneName;

        public GangDef Owner => _owner;

        public TurfStatus Status => _status;

        public bool IsExposed => Game.GameTime < _exposedUntil;

        /// <summary>Called after every sale: this is what rivals can actually notice.</summary>
        public void MarkExposed()
        {
            _exposedUntil = Game.GameTime + ExposureMs;
        }

        /// <summary>
        /// Price multiplier for dealing here. Rival turf pays better precisely because it is
        /// dangerous; home turf pays a little less but comes with backup.
        /// </summary>
        public float TurfPriceMultiplier
        {
            get
            {
                switch (_status)
                {
                    case TurfStatus.Hostile: return 1.35f;
                    case TurfStatus.Foreign: return 1.15f;
                    case TurfStatus.Home: return 1.0f;
                    default: return 1.05f;
                }
            }
        }

        /// <summary>Heat multiplier per sale. Your own block is quiet; a rival's is not.</summary>
        public float TurfHeatMultiplier
        {
            get
            {
                switch (_status)
                {
                    case TurfStatus.Hostile: return 2.0f;
                    case TurfStatus.Foreign: return 1.4f;
                    case TurfStatus.Home: return 0.4f;
                    default: return 1.0f;
                }
            }
        }

        // ---- per-tick ----------------------------------------------------------

        public void Update()
        {
            var now = Game.GameTime;
            if (now - _lastScan < ScanIntervalMs) return;
            _lastScan = now;

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return;

            RefreshZone(player);
            DefendPlayer(player);

            if (!IsExposed) return;
            if (now < _nextAggroAllowedAt) return;

            switch (_status)
            {
                case TurfStatus.Hostile:
                    RollHostileTurf(player);
                    break;
                case TurfStatus.Neutral:
                case TurfStatus.Foreign:
                    RollShakedown(player);
                    break;
            }
        }

        private void RefreshZone(Ped player)
        {
            try
            {
                var pos = player.Position;
                var code = Function.Call<string>(Hash.GET_NAME_OF_ZONE, pos.X, pos.Y, pos.Z) ?? "";

                if (code != _zoneCode)
                {
                    _zoneCode = code;

                    try { _zoneName = World.GetZoneLocalizedName(pos); }
                    catch { _zoneName = code; }

                    // Who holds a block is fixed by gangs.json and never changes hands. Turf is
                    // the map's geography, not a scoreboard.
                    _owner = _gangs.OwnerOfZone(code);
                    _status = Classify(_owner);
                    AnnounceZone();
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Zone probe failed: " + ex.Message);
            }
        }

        private TurfStatus Classify(GangDef owner)
        {
            if (owner == null) return TurfStatus.Neutral;

            var mine = _affiliation.Current;
            if (mine == null) return TurfStatus.Foreign;
            if (owner.Id == mine.Id) return TurfStatus.Home;

            // Hostile means THEY have a problem with you, not that the file once said their
            // gang and yours do not get on. Somebody's block is only dangerous ground if you
            // have given them a reason, or if they came with one -- which the Ballas and the
            // Vagos did.
            return _affiliation.Beefing(owner.Id) ? TurfStatus.Hostile : TurfStatus.Foreign;
        }

        private void AnnounceZone()
        {
            if (!_affiliation.IsAffiliated) return;

            switch (_status)
            {
                case TurfStatus.Home:
                    Notify.Ticker("~g~" + ZoneName + "~s~ -- your block");
                    break;
                case TurfStatus.Hostile:
                    Notify.Important("~r~" + ZoneName + "~s~ -- " + _owner.Name + " turf");
                    break;
            }
        }

        // ---- reactions ---------------------------------------------------------

        /// <summary>Rivals who can see you dealing on their block come for you.</summary>
        private void RollHostileTurf(Ped player)
        {
            var spotters = FindWatchers(player, SpotRange, rivalsOnly: true);
            if (spotters.Count == 0) return;

            // More eyes on you, more likely one of them acts.
            var chance = 1f - (float)Math.Pow(1f - HostileSpotChance * HeatScale(), spotters.Count);
            if (_rng.NextDouble() > chance) return;

            Engage(spotters, player,
                "~r~" + _owner.Name + " clocked you dealing on their block.~s~");
        }

        /// <summary>On unclaimed ground it is a stick-up, not a war.</summary>
        private void RollShakedown(Ped player)
        {
            var nearby = FindWatchers(player, ShakedownRange, rivalsOnly: false);
            if (nearby.Count == 0) return;

            var chance = 1f - (float)Math.Pow(1f - NeutralShakedownChance * HeatScale(), nearby.Count);
            if (_rng.NextDouble() > chance) return;

            Engage(nearby, player, "~o~Someone wants what you're holding.~s~");
        }

        /// <summary>Heat makes you conspicuous; it scales every spotting roll.</summary>
        private float HeatScale() => 1f + _state.Notoriety / 100f;

        private List<Ped> FindWatchers(Ped player, float range, bool rivalsOnly)
        {
            var found = new List<Ped>();

            try
            {
                foreach (var ped in World.GetNearbyPeds(player, range))
                {
                    if (ped == null || !ped.Exists() || !ped.IsAlive) continue;
                    if (ped.Handle == player.Handle) continue;
                    if (ped.IsInVehicle()) continue;
                    if (_aggroed.Contains(ped.Handle)) continue;

                    var gang = _affiliation.GangOf(ped);
                    if (gang == null) continue;
                    if (rivalsOnly && (_owner == null || gang.Id != _owner.Id)) continue;
                    if (_affiliation.IsAlly(ped)) continue;

                    // They have to actually be able to see you.
                    if (!Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, ped.Handle, player.Handle, 17))
                    {
                        continue;
                    }

                    found.Add(ped);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Watcher scan failed: " + ex.Message);
            }

            return found;
        }

        private void Engage(List<Ped> crew, Ped player, string message)
        {
            var sent = 0;

            foreach (var ped in crew)
            {
                try
                {
                    _aggroed.Add(ped.Handle);

                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 46, true); // always fight
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 0, false); // no cover camping
                    Function.Call(Hash.TASK_COMBAT_PED, ped.Handle, player.Handle, 0, 16);
                    Function.Call(Hash.SET_PED_KEEP_TASK, ped.Handle, true);
                    sent++;
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not task ped " + ped.Handle + ": " + ex.Message);
                }
            }

            if (sent == 0) return;

            _nextAggroAllowedAt = Game.GameTime + AggroCooldownMs;
            _state.AddNotoriety(6f);
            Notify.Important(message + " (" + sent + ")");
            Log.Info("Turf aggro: " + sent + " peds engaged on " + _zoneCode + " (" + _status + ").");

            // On your own turf your people weigh in.
            if (_affiliation.IsAffiliated && crew.Count > 0)
            {
                var backup = _affiliation.CallBackup(crew[0]);
                if (backup > 0) Notify.Ticker("~g~" + backup + " of yours moving in.~s~");
            }
        }

        /// <summary>
        /// Passive protection: if anything is already fighting the player and allies are around,
        /// they join in. This is the day-to-day meaning of "affiliated".
        /// </summary>
        private void DefendPlayer(Ped player)
        {
            if (!_affiliation.IsAffiliated) return;
            if (_affiliation.NearbyAllies == 0) return;

            try
            {
                foreach (var ped in World.GetNearbyPeds(player, 40f))
                {
                    if (ped == null || !ped.Exists() || !ped.IsAlive) continue;
                    if (ped.Handle == player.Handle) continue;
                    if (_affiliation.IsAlly(ped)) continue;
                    if (!ped.IsInCombatAgainst(player)) continue;

                    _affiliation.CallBackup(ped);
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Defend scan failed: " + ex.Message);
            }
        }

        /// <summary>Drops aggro bookkeeping for peds that have despawned.</summary>
        public void Prune()
        {
            if (_aggroed.Count < 200) return;
            _aggroed.Clear();
        }
    }
}
