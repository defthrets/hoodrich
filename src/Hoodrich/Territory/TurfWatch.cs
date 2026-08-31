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


        /// <summary>Per-check chance a rival who can see you decides to do something about it.</summary>
        private const float HostileSpotChance = 0.35f;

        /// <summary>Same, on unclaimed ground, where it is opportunistic rather than territorial.</summary>


        /// <summary>Once a crew has come for you, hold off this long before rolling again.</summary>
        private const int AggroCooldownMs = 60_000;

        private readonly GangRegistry _gangs;

        /// <summary>Assigned by Main. Zones taken in a war override the starting map.</summary>
        private readonly Affiliation _affiliation;
        private readonly PlayerState _state;
        private readonly Random _rng = new Random();

        private readonly HashSet<int> _aggroed = new HashSet<int>();

        /// <summary>
        /// Undoes everything Engage did to a ped this mod did not create.
        ///
        /// This is the only system in the mod that reached into AMBIENT peds and left them
        /// altered. Engage sets blocked non-temporary events, KEEP_TASK and no-cover on
        /// somebody the game owns, and nothing ever put any of it back -- and _aggroed
        /// guaranteed the ped was never looked at again, so it could not even be undone by
        /// accident. Every comparable system pairs the flag with its release; this did not, and
        /// it had no RestoreWorld at all, so a script unload could not fix it either.
        ///
        /// The result was a permanently broken pedestrian: a man stood in Davis with a combat
        /// order on a player who left twenty minutes ago, deaf to gunfire, sirens and his own
        /// schedule, for the rest of his life.
        /// </summary>
        private void LetThemGo()
        {
            foreach (var handle in _aggroed)
            {
                try
                {
                    var ped = (Ped)Entity.FromHandle(handle);
                    if (ped == null || !ped.Exists()) continue;

                    Function.Call(Hash.SET_PED_KEEP_TASK, ped.Handle, false);
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 46, false);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 5, false);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 0, true);
                    Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, ped.Handle, 1);

                    if (ped.IsAlive) Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);

                    ped.MarkAsNoLongerNeeded();
                }
                catch
                {
                    // He is gone, or the handle went stale. Either way he is not our problem.
                }
            }
        }

        /// <summary>Gives every ped this class touched back to the game.</summary>
        public void RestoreWorld()
        {
            LetThemGo();
            _aggroed.Clear();
        }

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
            // Marks time out on their own, whether or not anything else this tick runs.
            SweepMarks();

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
                // NOTHING on ground nobody owns.
                //
                // There used to be a stick-up here: stand on unclaimed pavement long enough
                // and somebody would be sent to take what you were carrying. It read as a
                // warning that never resolved -- a line about somebody coming, a blip, and
                // then a man walking at you for reasons the player had no way to connect to
                // anything he had done. Rivals coming for you on THEIR block is the same idea
                // with a reason attached, and that one stays.
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

                    // AND NOTHING IS ANNOUNCED. The map says it now.
                    //
                    // A banner every time you cross a zone line was worth having while whose
                    // block you were on was invisible -- it was the only way to find out. It
                    // is not the only way any more: the turf is drawn straight onto the
                    // minimap and the big map in the set's own colour, so the answer is on
                    // screen the whole time rather than for four seconds after you drive
                    // over a line you cannot see.
                    //
                    // Two things saying the same thing means the louder one is just noise,
                    // and a notification you did not ask for is the louder one. Everything
                    // this probe works out is still worked out -- the status drives the
                    // gang behaviour, and that has nothing to do with telling you about it.
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

        /// <summary>How long a mark stays on somebody who has come for you.</summary>
        private const int MarkMs = 25000;

        private readonly List<Blip> _marks = new List<Blip>();
        private readonly List<int> _markUntil = new List<int>();

        /// <summary>
        /// Puts a blip on somebody who is coming for you.
        ///
        /// "Someone wants what you're holding" with nothing on the map is a sentence about a
        /// man you cannot find, and not knowing what it meant was the whole complaint. A mark
        /// on him turns it into something happening in a direction.
        ///
        /// It times out rather than living as long as he does, because a red dot that outlives
        /// the moment is clutter on the minimap.
        /// </summary>
        private void Mark(Ped ped)
        {
            try
            {
                var blip = ped.AddBlip();
                if (blip == null || !blip.Exists()) return;

                blip.Color = BlipColor.Red;
                blip.Scale = 0.7f;
                blip.Name = "Coming for you";

                _marks.Add(blip);
                _markUntil.Add(Game.GameTime + MarkMs);
            }
            catch
            {
                // A blip is a nicety; the man is still walking over.
            }
        }

        /// <summary>Drops the marks once they have said what they were there to say.</summary>
        private void SweepMarks()
        {
            for (var i = _marks.Count - 1; i >= 0; i--)
            {
                if (Game.GameTime < _markUntil[i] && _marks[i] != null && _marks[i].Exists()) continue;

                try { if (_marks[i] != null && _marks[i].Exists()) _marks[i].Delete(); }
                catch { /* teardown */ }

                _marks.RemoveAt(i);
                _markUntil.RemoveAt(i);
            }
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
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 5, true); // always fight
                    // 46 is BF_CanFightArmedPedsWhenNotArmed, NOT BF_AlwaysFight. That is 5.
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 0, false); // no cover camping

                    // He commits, and he comes forward.
                    //
                    // Without these he is a man who has been told to fight and is still free to
                    // answer anything else that happens -- a siren, an argument down the road,
                    // his own schedule -- so the notice fired and then nothing visible followed.
                    // Movement 2 is advance, which is what somebody walking up to you for what
                    // you are carrying actually does.
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, true);
                    Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, ped.Handle, 2);

                    Function.Call(Hash.TASK_COMBAT_PED, ped.Handle, player.Handle, 0, 16);
                    Function.Call(Hash.SET_PED_KEEP_TASK, ped.Handle, true);

                    Mark(ped);
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

            // Handed back before the set is dropped, or they stay broken.
            LetThemGo();

            _aggroed.Clear();
        }
    }
}
