using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Gangs;
using Hoodrich.State;
using Hoodrich.UI;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.Territory
{
    /// <summary>How hard you go in. Cost rises far faster than strength.</summary>
    internal enum AttackStrength
    {
        Light = 0,
        Medium = 1,
        Heavy = 2,
        Massive = 3
    }

    /// <summary>
    /// Taking a block by force.
    ///
    /// A war is fought in REINFORCEMENTS, not on a timer: both sides start with a pool, every
    /// death takes one off, and whoever runs out first loses. That means a war is a thing you
    /// grind down rather than a clock you survive, and it is why a well-developed zone is
    /// genuinely hard to take.
    ///
    /// The strength tiers, quadratic attack cost and reinforcement maths are adapted from
    /// lucasvinbr's GTA5GangMod (MIT licensed).
    /// </summary>
    internal sealed class TurfWar
    {
        private const int SpawnIntervalMs = 2500;
        private const float SpawnMinDistance = 45f;
        private const float SpawnMaxDistance = 95f;
        private const float AbandonDistance = 260f;
        private const int KillScanIntervalMs = 900;

        private readonly Settings _cfg;
        private readonly PlayerState _state;
        private readonly Affiliation _crew;
        private readonly TerritoryState _territory;
        private readonly Random _rng = new Random();

        private readonly List<Ped> _enemies = new List<Ped>();
        private readonly List<Ped> _allies = new List<Ped>();
        private readonly HashSet<int> _counted = new HashSet<int>();

        private GangDef _defender;
        private string _zone = "";
        private string _zoneName = "";
        private AttackStrength _strength;

        private int _enemyLeft;
        private int _ourLeft;
        private int _lastSpawn;
        private int _lastKillScan;
        private Vector3 _origin;

        public TurfWar(Settings cfg, PlayerState state, Affiliation crew, TerritoryState territory)
        {
            _cfg = cfg;
            _state = state;
            _crew = crew;
            _territory = territory;
        }

        public bool IsActive { get; private set; }

        public GangDef Defender => _defender;
        public string ZoneName => _zoneName;
        public int EnemyLeft => _enemyLeft;
        public int OurLeft => _ourLeft;

        // ---- the numbers -------------------------------------------------------

        /// <summary>Cost rises with the square of intensity: overwhelming force is expensive.</summary>
        public int AttackCost(AttackStrength strength)
        {
            if (strength == AttackStrength.Light) return _cfg.BaseCostToTakeTurf;

            var scale = (int)strength / 3f;
            return _cfg.BaseCostToTakeTurf + (int)(_cfg.MaxExtraCostToTakeTurf * scale * scale);
        }

        /// <summary>Reinforcements you bring, scaled by how hard you committed.</summary>
        public int AttackerReinforcements(AttackStrength strength)
        {
            var maxIntensity = (int)AttackStrength.Massive;
            maxIntensity *= maxIntensity;

            var intensity = (int)strength;
            intensity *= intensity;

            var fromStrength = _cfg.ExtraKillsPerTurfValue * _cfg.MaxTurfValue
                               * (maxIntensity == 0 ? 0f : intensity / (float)maxIntensity);

            return (int)(fromStrength + _cfg.BaseKillsBeforeWarVictory);
        }

        /// <summary>What the defenders field, driven by how developed the block is.</summary>
        public int DefenderReinforcements(string zoneCode)
        {
            return (int)(_cfg.ExtraKillsPerTurfValue * _territory.ValueOf(zoneCode)
                         + _cfg.BaseKillsBeforeWarVictory);
        }

        /// <summary>The weakest attack that still out-numbers the defenders. For the wheel.</summary>
        public AttackStrength RecommendedStrength(string zoneCode)
        {
            var needed = DefenderReinforcements(zoneCode);
            for (var s = AttackStrength.Light; s < AttackStrength.Massive; s++)
            {
                if (AttackerReinforcements(s) >= needed) return s;
            }
            return AttackStrength.Massive;
        }

        // ---- starting one ------------------------------------------------------

        /// <summary>Returns a player-facing refusal, or null if the war began.</summary>
        public string TryStart(string zoneCode, string zoneName, GangDef defender, AttackStrength strength)
        {
            if (IsActive) return "You are already in a war.";
            if (!_crew.IsAffiliated) return "You need a crew behind you.";
            if (string.IsNullOrEmpty(zoneCode)) return "Nowhere to take.";
            if (defender == null) return "Nobody holds this block.";
            if (defender.Id == _crew.Current.Id) return "This is already your block.";

            var cost = AttackCost(strength);
            if (Game.Player.Money < cost)
            {
                return "A " + strength.ToString().ToLowerInvariant() + " push costs $" +
                       cost.ToString("N0") + ". You are short $" + (cost - Game.Player.Money).ToString("N0") + ".";
            }

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return "Not right now.";

            Game.Player.Money -= cost;

            _defender = defender;
            _zone = zoneCode;
            _zoneName = zoneName;
            _strength = strength;
            _enemyLeft = DefenderReinforcements(zoneCode);
            _ourLeft = AttackerReinforcements(strength);
            _origin = player.Position;
            _lastSpawn = 0;
            _counted.Clear();
            IsActive = true;

            Notify.Important("~r~WAR: " + _zoneName + "~s~ -- " + defender.Name + " are defending.");
            Log.Info("Turf war started in " + zoneCode + " vs " + defender.Id +
                     " (" + strength + ", " + _ourLeft + " v " + _enemyLeft + ").");
            return null;
        }

        // ---- per-tick ----------------------------------------------------------

        public void Update()
        {
            if (!IsActive) return;

            var player = Game.Player.Character;
            if (player == null || !player.Exists())
            {
                End(false, "You are gone.");
                return;
            }

            if (!player.IsAlive)
            {
                End(false, "They put you down.");
                return;
            }

            if (player.Position.DistanceTo(_origin) > AbandonDistance)
            {
                End(false, "You left the block.");
                return;
            }

            var now = Game.GameTime;

            if (now - _lastKillScan >= KillScanIntervalMs)
            {
                _lastKillScan = now;
                CountCasualties();
            }

            if (now - _lastSpawn >= SpawnIntervalMs)
            {
                _lastSpawn = now;
                MaintainSpawns(player);
            }

            if (_enemyLeft <= 0) End(true, null);
        }

        /// <summary>Every corpse on either side comes off that side's pool exactly once.</summary>
        private void CountCasualties()
        {
            for (var i = _enemies.Count - 1; i >= 0; i--)
            {
                var ped = _enemies[i];
                if (ped == null || !ped.Exists()) { _enemies.RemoveAt(i); continue; }
                if (ped.IsAlive) continue;

                if (_counted.Add(ped.Handle)) _enemyLeft = Math.Max(0, _enemyLeft - 1);

                ped.MarkAsNoLongerNeeded();
                _enemies.RemoveAt(i);
            }

            for (var i = _allies.Count - 1; i >= 0; i--)
            {
                var ped = _allies[i];
                if (ped == null || !ped.Exists()) { _allies.RemoveAt(i); continue; }
                if (ped.IsAlive) continue;

                if (_counted.Add(ped.Handle)) _ourLeft = Math.Max(0, _ourLeft - 1);

                ped.MarkAsNoLongerNeeded();
                _allies.RemoveAt(i);
            }

            if (_ourLeft <= 0) End(false, "Your side broke.");
        }

        private void MaintainSpawns(Ped player)
        {
            // Never field more at once than are left to lose.
            var enemyWant = Math.Min(_cfg.WarMaxConcurrentPerSide, _enemyLeft);
            while (_enemies.Count < enemyWant)
            {
                var ped = SpawnFighter(_defender, player, hostile: true);
                if (ped == null) break;
                _enemies.Add(ped);
            }

            var allyWant = Math.Min(_cfg.WarMaxConcurrentPerSide - 1, _ourLeft);
            while (_allies.Count < allyWant)
            {
                var ped = SpawnFighter(_crew.Current, player, hostile: false);
                if (ped == null) break;
                _allies.Add(ped);
            }
        }

        private Ped SpawnFighter(GangDef gang, Ped player, bool hostile)
        {
            if (gang == null) return null;

            var model = ResolveMemberModel(gang);
            if (model == null) return null;

            Vector3 spot;
            if (!TrySpawnSpot(player.Position, out spot)) return null;

            try
            {
                var ped = World.CreatePed(model.Value, spot);
                if (ped == null || !ped.Exists()) return null;

                var h = ped.Handle;

                // Membership is by relationship group everywhere else in the mod; keep that true
                // here so a war spawn is indistinguishable from an ambient gang member.
                if (gang.GroupHash != 0)
                {
                    Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, h, gang.GroupHash);
                }

                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, h, true, true);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, h, 46, true);   // always fight
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, h, 5, true);    // can use vehicles
                Function.Call(Hash.SET_PED_ACCURACY, h, _cfg.WarMemberAccuracy);
                Function.Call(Hash.SET_PED_COMBAT_ABILITY, h, 1);

                ped.Health = _cfg.WarMemberHealth;
                ped.Armor = _cfg.WarMemberArmor;
                ped.IsPersistent = true;

                GiveWarWeapon(ped);

                if (hostile)
                {
                    Function.Call(Hash.TASK_COMBAT_PED, h, player.Handle, 0, 16);
                }
                else
                {
                    // Allies hunt whatever is hostile around you rather than a fixed target.
                    Function.Call(Hash.TASK_COMBAT_HATED_TARGETS_AROUND_PED, h, 120f, 0);
                }

                Function.Call(Hash.SET_PED_KEEP_TASK, h, true);
                return ped;
            }
            catch (Exception ex)
            {
                Log.Debug("War spawn failed: " + ex.Message);
                return null;
            }
            finally
            {
                try { model.Value.MarkAsNoLongerNeeded(); } catch { }
            }
        }

        private void GiveWarWeapon(Ped ped)
        {
            try
            {
                var pool = _cfg.WarWeapons;
                if (pool.Count == 0) return;

                var name = pool[_rng.Next(pool.Count)];
                var hash = Function.Call<int>(Hash.GET_HASH_KEY, name);
                Function.Call(Hash.GIVE_WEAPON_TO_PED, ped.Handle, hash, 250, false, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not arm a war ped: " + ex.Message);
            }
        }

        private Model? ResolveMemberModel(GangDef gang)
        {
            foreach (var name in gang.MemberModels)
            {
                if (string.IsNullOrEmpty(name)) continue;
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage) continue;
                    if (!model.Request(1200)) continue;
                    return model;
                }
                catch
                {
                    // Try the next one.
                }
            }
            return null;
        }

        private bool TrySpawnSpot(Vector3 origin, out Vector3 spot)
        {
            spot = Vector3.Zero;

            for (var attempt = 0; attempt < 8; attempt++)
            {
                var angle = _rng.NextDouble() * Math.PI * 2.0;
                var distance = SpawnMinDistance + (float)_rng.NextDouble() * (SpawnMaxDistance - SpawnMinDistance);

                var candidate = origin + new Vector3(
                    (float)Math.Cos(angle) * distance, (float)Math.Sin(angle) * distance, 0f);

                Vector3 onFoot;
                try { onFoot = World.GetNextPositionOnSidewalk(candidate); }
                catch { continue; }

                if (onFoot == Vector3.Zero) continue;

                try
                {
                    if (World.GetGroundHeight(onFoot, out var groundZ, GetGroundHeightMode.Normal))
                    {
                        onFoot.Z = groundZ;
                    }
                }
                catch
                {
                    // Sidewalk Z is usually fine.
                }

                spot = onFoot;
                return true;
            }

            return false;
        }

        // ---- ending ------------------------------------------------------------

        private void End(bool won, string reason)
        {
            var zone = _zone;
            var zoneName = _zoneName;
            var defender = _defender;

            IsActive = false;
            Cleanup();

            if (won)
            {
                _territory.SetOwner(zone, _crew.Current.Id);

                var reward = _cfg.RewardForTakingTurf * (1 + (int)_strength);
                Game.Player.Money += reward;
                _state.AddRespect(60f + 20f * (int)_strength);

                var standing = _crew.CurrentStanding;
                if (standing != null) standing.Rep = Math.Min(1000f, standing.Rep + 40f);

                var theirs = _crew.StandingFor(defender.Id);
                theirs.Rep = Math.Max(-100f, theirs.Rep - 40f);

                _state.Touch();

                Notify.Important("~g~" + zoneName + " is yours.~s~  +$" + reward.ToString("N0"));
                Log.Info("Turf war won: " + zone + " taken from " + defender.Id + ".");
            }
            else
            {
                _state.AddRespect(-20f);
                _state.Touch();

                Notify.Failure("you lost " + zoneName + ". " + (reason ?? ""));
                Log.Info("Turf war lost in " + zone + ": " + (reason ?? "defeated") + ".");
            }
        }

        private void Cleanup()
        {
            foreach (var ped in _enemies)
            {
                try { if (ped != null && ped.Exists()) ped.MarkAsNoLongerNeeded(); } catch { }
            }
            foreach (var ped in _allies)
            {
                try { if (ped != null && ped.Exists()) ped.MarkAsNoLongerNeeded(); } catch { }
            }

            _enemies.Clear();
            _allies.Clear();
            _counted.Clear();
            _defender = null;
            _zone = "";
            _zoneName = "";
        }

        /// <summary>Both reinforcement pools, drawn as opposing bars.</summary>
        public void Draw()
        {
            if (!IsActive) return;

            const float y = 0.10f;
            const float w = 0.16f;
            const float h = 0.018f;

            var ourMax = Math.Max(1, AttackerReinforcements(_strength));
            var theirMax = Math.Max(1, DefenderReinforcements(_zone));

            var ours = Math.Max(0f, Math.Min(1f, _ourLeft / (float)ourMax));
            var theirs = Math.Max(0f, Math.Min(1f, _enemyLeft / (float)theirMax));

            DrawBar(0.5f - w * 0.55f, y, w, h, ours, Palette.Cash);
            DrawBar(0.5f + w * 0.55f, y, w, h, theirs, Palette.Danger);

            Hud.Text(_crew.Current.Tag + "  " + _ourLeft, 0.5f - w * 0.55f, y + 0.020f, 0.32f,
                      Palette.Cash, Hud.FontLabel);
            Hud.Text(_defender.Tag + "  " + _enemyLeft, 0.5f + w * 0.55f, y + 0.020f, 0.32f,
                      Palette.Danger, Hud.FontLabel);
            Hud.Text(_zoneName.ToUpperInvariant(), 0.5f, y - 0.030f, 0.36f, Palette.Text, Hud.FontLabel);
        }

        private static void DrawBar(float cx, float cy, float w, float h, float fraction, Color fill)
        {
            Hud.Rect(cx, cy, w + 0.004f, h + 0.004f, Color.FromArgb(190, 8, 8, 10));
            Hud.Rect(cx, cy, w, h, Color.FromArgb(160, 30, 32, 34));

            var filled = w * fraction;
            Hud.Rect(cx - (w - filled) * 0.5f, cy, filled, h, fill);
        }

        public void RestoreWorld()
        {
            IsActive = false;
            Cleanup();
        }
    }
}
