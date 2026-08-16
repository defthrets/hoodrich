using System;
using System.Collections.Generic;
using GTA;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.UI;

namespace Hoodrich.Gangs
{
    /// <summary>
    /// Who the player runs with, what that is worth, and what it costs.
    ///
    /// Relationship changes here are deliberately narrow. Affiliating makes YOUR gang respect
    /// you globally -- that is what "they help you on the street" means -- but it does NOT flip
    /// every rival to hate-on-sight, which would make the world unplayable. Rival aggression is
    /// situational and targeted, and lives in <see cref="Hoodrich.Territory.TurfWatch"/>.
    /// </summary>
    internal sealed class Affiliation
    {
        // SET_RELATIONSHIP_BETWEEN_GROUPS intensities.
        private const int RelCompanion = 0;
        private const int RelRespect = 1;
        private const int RelLike = 2;
        private const int RelNeutral = 3;
        private const int RelDislike = 4;
        private const int RelHate = 5;

        /// <summary>Relationships get reapplied on this cadence; other scripts can stomp them.</summary>
        private const int ReapplyIntervalMs = 15_000;

        private const float AllyScanRadius = 45f;
        private const int KillScanIntervalMs = 1200;

        /// <summary>Each nearby ally adds this to the sale price, up to <see cref="MaxLookoutBonus"/>.</summary>
        private const float LookoutBonusPerAlly = 0.04f;
        private const float MaxLookoutBonus = 0.20f;

        private readonly GangRegistry _gangs;
        private readonly Dictionary<string, GangStanding> _standings =
            new Dictionary<string, GangStanding>(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<int> _countedKills = new HashSet<int>();

        private int _playerGroupHash;
        private int _lastReapply;
        private int _lastKillScan;
        private int _lastAllyCount;
        private int _lastAllyCountAt;

        /// <summary>Groups we have altered, so they can be put back on unload.</summary>
        private readonly HashSet<int> _touchedGroups = new HashSet<int>();

        public Affiliation(GangRegistry gangs)
        {
            _gangs = gangs;
            try
            {
                _playerGroupHash = Function.Call<int>(Hash.GET_HASH_KEY, "PLAYER");
            }
            catch (Exception ex)
            {
                Log.Error("Could not resolve the PLAYER relationship group.", ex);
            }
        }

        /// <summary>The gang the player currently runs with, or null.</summary>
        public GangDef Current { get; private set; }

        public bool IsAffiliated => Current != null;

        /// <summary>Allies seen within <see cref="AllyScanRadius"/> on the last scan.</summary>
        public int NearbyAllies => _lastAllyCount;

        /// <summary>Price uplift from having your own people watching your back, as a multiplier.</summary>
        public float LookoutMultiplier => 1f + Math.Min(MaxLookoutBonus, _lastAllyCount * LookoutBonusPerAlly);

        public GangStanding StandingFor(string gangId)
        {
            if (string.IsNullOrEmpty(gangId)) return null;
            if (!_standings.TryGetValue(gangId, out var s))
            {
                s = new GangStanding { GangId = gangId };
                _standings[gangId] = s;
            }
            return s;
        }

        public GangStanding CurrentStanding => Current == null ? null : StandingFor(Current.Id);

        /// <summary>Registry lookup, exposed so callers do not need their own GangRegistry reference.</summary>
        public GangDef GangById(string gangId) => _gangs.Get(gangId);

        public IEnumerable<GangStanding> AllStandings => _standings.Values;

        // ---- joining and leaving -----------------------------------------------

        /// <summary>Returns a player-facing refusal, or null on success.</summary>
        public string Join(GangDef gang, float playerRespect)
        {
            if (gang == null) return "No such crew.";
            if (Current != null && Current.Id == gang.Id) return "You already run with " + gang.Name + ".";

            var standing = StandingFor(gang.Id);
            if (standing.Rep <= -50f) return gang.Name + " want you dead, not on the payroll.";
            if (playerRespect < gang.JoinRespect)
            {
                return "Need " + gang.JoinRespect.ToString("F0") + " respect. You have " +
                       playerRespect.ToString("F0") + ".";
            }

            var previous = Current;
            if (previous != null) ClearRelationsFor(previous);

            Current = gang;
            ApplyRelations(gang);

            if (previous != null)
            {
                // Switching sides is not free: the crew you walked out on remembers.
                var old = StandingFor(previous.Id);
                old.Rep = Math.Max(-100f, old.Rep - 40f);
                Notify.Ticker("~o~You walked out on " + previous.Name + ".~s~");
            }

            Notify.Important("~g~Running with " + gang.Name + ".~s~ " + gang.TurfHint);
            Log.Info("Affiliated with " + gang.Id + ".");
            return null;
        }

        public void Leave()
        {
            if (Current == null) return;

            var gang = Current;
            ClearRelationsFor(gang);

            var standing = StandingFor(gang.Id);
            standing.Rep = Math.Max(-100f, standing.Rep - 25f);

            Current = null;
            Notify.Ticker("~o~You are running solo.~s~");
            Log.Info("Left " + gang.Id + ".");
        }

        /// <summary>Restores the affiliation loaded from a save without the join checks or messaging.</summary>
        public void RestoreAffiliation(string gangId)
        {
            var gang = _gangs.Get(gangId);
            if (gang == null) return;

            Current = gang;
            ApplyRelations(gang);
            Log.Info("Restored affiliation with " + gang.Id + ".");
        }

        private void ApplyRelations(GangDef gang)
        {
            if (gang.GroupHash == 0 || _playerGroupHash == 0) return;

            try
            {
                SetBoth(RelRespect, gang.GroupHash, _playerGroupHash);
                _touchedGroups.Add(gang.GroupHash);
                _lastReapply = Game.GameTime;
            }
            catch (Exception ex)
            {
                Log.Error("Could not apply relationships for " + gang.Id, ex);
            }
        }

        private void ClearRelationsFor(GangDef gang)
        {
            if (gang.GroupHash == 0 || _playerGroupHash == 0) return;

            try
            {
                Function.Call(Hash.CLEAR_RELATIONSHIP_BETWEEN_GROUPS, RelNeutral, gang.GroupHash, _playerGroupHash);
                Function.Call(Hash.CLEAR_RELATIONSHIP_BETWEEN_GROUPS, RelNeutral, _playerGroupHash, gang.GroupHash);
                _touchedGroups.Remove(gang.GroupHash);
            }
            catch (Exception ex)
            {
                Log.Error("Could not clear relationships for " + gang.Id, ex);
            }
        }

        private static void SetBoth(int intensity, int a, int b)
        {
            Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, intensity, a, b);
            Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, intensity, b, a);
        }

        /// <summary>Puts every relationship we changed back. Called on script unload.</summary>
        public void RestoreWorld()
        {
            foreach (var hash in _touchedGroups)
            {
                try
                {
                    Function.Call(Hash.CLEAR_RELATIONSHIP_BETWEEN_GROUPS, RelNeutral, hash, _playerGroupHash);
                    Function.Call(Hash.CLEAR_RELATIONSHIP_BETWEEN_GROUPS, RelNeutral, _playerGroupHash, hash);
                }
                catch
                {
                    // Nothing useful to do during teardown.
                }
            }
            _touchedGroups.Clear();
        }

        // ---- per-tick ----------------------------------------------------------

        public void Update()
        {
            var now = Game.GameTime;

            if (Current != null && now - _lastReapply >= ReapplyIntervalMs)
            {
                ApplyRelations(Current);
            }

            if (now - _lastKillScan >= KillScanIntervalMs)
            {
                _lastKillScan = now;
                ScanKills();
                ScanAllies();
            }
        }

        /// <summary>Which gang a ped belongs to, by relationship group. Null for civilians.</summary>
        public GangDef GangOf(Ped ped)
        {
            if (ped == null || !ped.Exists()) return null;
            try
            {
                var hash = Function.Call<int>(Hash.GET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle);
                return _gangs.ByGroupHash(hash);
            }
            catch
            {
                return null;
            }
        }

        public bool IsAlly(Ped ped)
        {
            if (Current == null) return false;
            var g = GangOf(ped);
            return g != null && g.Id == Current.Id;
        }

        public bool IsRival(Ped ped)
        {
            var g = GangOf(ped);
            if (g == null) return false;
            if (Current == null) return false;
            return g.Id != Current.Id && (Current.IsRivalOf(g.Id) || g.IsRivalOf(Current.Id));
        }

        private void ScanAllies()
        {
            _lastAllyCount = 0;
            _lastAllyCountAt = Game.GameTime;
            if (Current == null) return;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            try
            {
                foreach (var ped in World.GetNearbyPeds(player, AllyScanRadius))
                {
                    if (ped == null || !ped.Exists() || !ped.IsAlive) continue;
                    if (ped.Handle == player.Handle) continue;
                    if (IsAlly(ped)) _lastAllyCount++;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Ally scan failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Credits kills of rival gang members. Polls nearby corpses rather than hooking a
        /// damage event, because SHVDN gives no kill callback.
        /// </summary>
        private void ScanKills()
        {
            if (Current == null) return;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            try
            {
                foreach (var ped in World.GetNearbyPeds(player, 90f))
                {
                    if (ped == null || !ped.Exists() || ped.IsAlive) continue;
                    if (_countedKills.Contains(ped.Handle)) continue;

                    var killer = Function.Call<int>(Hash.GET_PED_SOURCE_OF_DEATH, ped.Handle);
                    if (killer != player.Handle) continue;

                    _countedKills.Add(ped.Handle);

                    var gang = GangOf(ped);
                    if (gang == null) continue;

                    if (gang.Id == Current.Id)
                    {
                        // Killing your own costs you dearly.
                        var own = StandingFor(Current.Id);
                        own.Rep = Math.Max(-100f, own.Rep - 15f);
                        Notify.Ticker("~r~" + Current.Name + " saw that.~s~ -15 rep");
                        continue;
                    }

                    if (!Current.IsRivalOf(gang.Id) && !gang.IsRivalOf(Current.Id)) continue;

                    var standing = StandingFor(Current.Id);
                    standing.Kills++;
                    standing.Rep = Math.Min(1000f, standing.Rep + 3f);

                    var theirs = StandingFor(gang.Id);
                    theirs.Rep = Math.Max(-100f, theirs.Rep - 5f);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Kill scan failed: " + ex.Message);
            }

            if (_countedKills.Count > 400) _countedKills.Clear();
        }

        /// <summary>Orders nearby allies onto whoever is attacking the player.</summary>
        public int CallBackup(Ped target)
        {
            if (Current == null || target == null || !target.Exists()) return 0;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return 0;

            var sent = 0;
            try
            {
                foreach (var ped in World.GetNearbyPeds(player, AllyScanRadius))
                {
                    if (ped == null || !ped.Exists() || !ped.IsAlive) continue;
                    if (ped.Handle == player.Handle || ped.Handle == target.Handle) continue;
                    if (!IsAlly(ped)) continue;
                    if (ped.IsInCombat) continue;

                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 46, true);  // always fight
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 5, true);   // can use vehicles
                    Function.Call(Hash.TASK_COMBAT_PED, ped.Handle, target.Handle, 0, 16);
                    Function.Call(Hash.SET_PED_KEEP_TASK, ped.Handle, true);
                    sent++;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Backup dispatch failed: " + ex.Message);
            }

            return sent;
        }

        // ---- persistence -------------------------------------------------------

        public Json ToJson()
        {
            var obj = Json.Object();
            obj.Set("current", Current == null ? "" : Current.Id);

            var arr = Json.Array();
            foreach (var s in _standings.Values)
            {
                arr.Add(Json.Object()
                    .Set("gang", s.GangId)
                    .Set("rep", Math.Round(s.Rep, 2))
                    .Set("kills", s.Kills)
                    .Set("moneyEarned", s.MoneyEarned)
                    .Set("deals", s.Deals)
                    .Set("timeMs", s.TimeAffiliatedMs));
            }
            obj.Set("standings", arr);
            return obj;
        }

        public void LoadFrom(Json node)
        {
            _standings.Clear();
            if (node == null || node.IsNull) return;

            foreach (var item in node["standings"].Items)
            {
                var id = item["gang"].AsString(null);
                if (string.IsNullOrEmpty(id)) continue;

                _standings[id] = new GangStanding
                {
                    GangId = id,
                    Rep = item["rep"].AsFloat(0f),
                    Kills = Math.Max(0, item["kills"].AsInt(0)),
                    MoneyEarned = Math.Max(0L, item["moneyEarned"].AsLong(0)),
                    Deals = Math.Max(0, item["deals"].AsInt(0)),
                    TimeAffiliatedMs = Math.Max(0L, item["timeMs"].AsLong(0))
                };
            }

            var current = node["current"].AsString("");
            if (!string.IsNullOrEmpty(current)) RestoreAffiliation(current);
        }
    }
}
