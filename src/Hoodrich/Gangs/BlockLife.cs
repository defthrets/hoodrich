using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Gangs
{
    /// <summary>People who live on a block, doing nothing in particular.</summary>
    internal sealed class BlockSpot
    {
        public Vector3 Where;
        public int Men = 3;
        public int Women = 2;

        /// <summary>How many of them are stood about with a drink or something to smoke.</summary>
        public int Loafers = 2;

        public float Roam = 10f;
    }

    /// <summary>
    /// The people who are just there.
    ///
    /// Every corner in this mod so far has been occupied by somebody who wants something from
    /// you -- a leader, a fixer, an armourer, a buyer. That is a game board, not a
    /// neighbourhood. These people want nothing. They stand outside their own building, they
    /// drink, they smoke, they talk to each other, and they will still be doing it when you
    /// leave. That is the entire specification and it is the most important thing on the block.
    ///
    /// They are Families and they are ours, so shooting one is exactly as expensive as it
    /// should be.
    /// </summary>
    internal sealed class BlockLife
    {
        private const float SpawnRange = 90f;
        private const float DespawnRange = 170f;
        private const int UpdateIntervalMs = 1500;

        private const int PedTypeCiv = 4;

        /// <summary>Standing about, hands free.</summary>
        private static readonly string[] Idles =
        {
            "WORLD_HUMAN_STAND_MOBILE", "WORLD_HUMAN_HANG_OUT_STREET",
            "WORLD_HUMAN_STAND_IMPATIENT", "WORLD_HUMAN_LEANING",
            "WORLD_HUMAN_MUSICIAN", "WORLD_HUMAN_STAND_MOBILE_UPRIGHT",
        };

        /// <summary>Standing about with something in their hand, which is most of a stoop.</summary>
        private static readonly string[] Loafing =
        {
            "WORLD_HUMAN_DRINKING", "WORLD_HUMAN_SMOKING", "WORLD_HUMAN_SMOKING_POT",
            "WORLD_HUMAN_AA_SMOKE", "WORLD_HUMAN_DRINKING",
        };

        /// <summary>
        /// Chatter between them.
        ///
        /// Two people stood near each other in silence read as two props. One line every so
        /// often, aimed at whoever is nearest, and they read as a conversation you happen to be
        /// walking past -- which is what they are.
        /// </summary>
        private static readonly string[] Chat =
        {
            "GENERIC_HOWS_IT_GOING", "GENERIC_HI", "CHAT_STATE", "GENERIC_YES",
            "GENERIC_INSULT_MED", "GENERIC_THANKS",
        };

        private const int ChatGapMinMs = 9000;
        private const int ChatGapMaxMs = 26000;

        private readonly GangRegistry _gangs;
        private readonly string _gangId;
        private readonly List<BlockSpot> _spots = new List<BlockSpot>();

        private readonly List<Ped> _people = new List<Ped>();
        private readonly Random _rng = new Random();

        private int _lastUpdate;
        private int _nextChat;

        public BlockLife(GangRegistry gangs, string gangId)
        {
            _gangs = gangs;
            _gangId = gangId;
        }

        public BlockLife At(Vector3 where, int men = 3, int women = 2, int loafers = 2, float roam = 10f)
        {
            _spots.Add(new BlockSpot { Where = where, Men = men, Women = women, Loafers = loafers, Roam = roam });
            return this;
        }

        // ---- per-tick ----------------------------------------------------------

        public void Update()
        {
            var now = Game.GameTime;
            if (now - _lastUpdate < UpdateIntervalMs) return;
            _lastUpdate = now;

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return;

            Prune();
            Chatter(now);

            foreach (var spot in _spots)
            {
                var away = player.Position.DistanceTo(spot.Where);

                if (away <= SpawnRange && !Populated(spot)) Populate(spot);
                else if (away > DespawnRange) Clear(spot);
            }
        }

        private bool Populated(BlockSpot spot)
        {
            foreach (var ped in _people)
            {
                if (ped == null || !ped.Exists()) continue;
                if (ped.Position.DistanceTo(spot.Where) <= spot.Roam * 2.2f) return true;
            }

            return false;
        }

        private void Populate(BlockSpot spot)
        {
            var gang = _gangs.Get(_gangId);
            if (gang == null) return;

            var made = 0;

            for (var i = 0; i < spot.Men; i++)
            {
                if (Place(gang, spot, false, i < spot.Loafers)) made++;
            }

            for (var i = 0; i < spot.Women; i++)
            {
                if (Place(gang, spot, true, false)) made++;
            }

            if (made > 0) Log.Info(made + " on the block at " + spot.Where + ".");
        }

        private bool Place(GangDef gang, BlockSpot spot, bool female, bool loafing)
        {
            // Scattered rather than stacked, and settled onto the ground only when the ground
            // agrees with the height that was authored from standing there.
            var at = Ground(spot.Where.Around(1.5f + (float)_rng.NextDouble() * spot.Roam * 0.7f));

            foreach (var name in Models(gang, female))
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1200)) continue;

                    var handle = Function.Call<int>(Hash.CREATE_PED, PedTypeCiv, model.Hash,
                                                    at.X, at.Y, at.Z,
                                                    (float)_rng.NextDouble() * 360f, false, false);

                    model.MarkAsNoLongerNeeded();
                    if (handle == 0) continue;

                    var ped = Entity.FromHandle(handle) as Ped;
                    if (ped == null || !ped.Exists()) continue;

                    ped.IsPersistent = true;

                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, ped.Handle, true, true);
                    Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle, gang.GroupHash);

                    // They live here. Wandering a few metres and settling into something is the
                    // whole behaviour -- a scenario nails them to one tile, and wander alone
                    // means nobody ever stops moving.
                    var scenario = loafing
                        ? Loafing[_rng.Next(Loafing.Length)]
                        : Idles[_rng.Next(Idles.Length)];

                    if (_rng.NextDouble() < 0.45)
                    {
                        Function.Call(Hash.TASK_WANDER_IN_AREA, ped.Handle,
                                      spot.Where.X, spot.Where.Y, spot.Where.Z, spot.Roam, 3f, 12f);
                    }
                    else
                    {
                        Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, ped.Handle, scenario, 0, true);
                    }

                    _people.Add(ped);
                    return true;
                }
                catch
                {
                    // Try the next model.
                }
            }

            return false;
        }

        /// <summary>
        /// Who to spawn.
        ///
        /// The gang's own member models first, and the neighbourhood civilian models after --
        /// a stoop that is five gang members and nobody else is a barracks, not a block.
        /// </summary>
        private IEnumerable<string> Models(GangDef gang, bool female)
        {
            if (female)
            {
                yield return "a_f_y_soucent_01";
                yield return "a_f_y_soucent_02";
                yield return "a_f_y_soucent_03";
                yield return "a_f_m_soucent_01";
                yield return "a_f_m_soucent_02";
                yield break;
            }

            foreach (var name in gang.MemberModels) yield return name;

            yield return "a_m_y_soucent_01";
            yield return "a_m_y_soucent_02";
            yield return "a_m_m_soucent_01";
        }

        /// <summary>One of them says something to whoever is nearest.</summary>
        private void Chatter(int now)
        {
            if (now < _nextChat) return;
            _nextChat = now + ChatGapMinMs + _rng.Next(ChatGapMaxMs - ChatGapMinMs);

            if (_people.Count == 0) return;

            var who = _people[_rng.Next(_people.Count)];
            if (who == null || !who.Exists() || !who.IsAlive) return;

            try
            {
                Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, who.Handle,
                              Chat[_rng.Next(Chat.Length)], "SPEECH_PARAMS_FORCE");
            }
            catch
            {
                // A missing line costs nothing.
            }
        }

        private void Prune()
        {
            for (var i = _people.Count - 1; i >= 0; i--)
            {
                var ped = _people[i];
                if (ped != null && ped.Exists() && ped.IsAlive) continue;

                // Dead ones are let go rather than deleted. Somebody who was shot on this stoop
                // should still be lying on it.
                try { if (ped != null && ped.Exists()) ped.MarkAsNoLongerNeeded(); }
                catch { /* teardown */ }

                _people.RemoveAt(i);
            }
        }

        private void Clear(BlockSpot spot)
        {
            for (var i = _people.Count - 1; i >= 0; i--)
            {
                var ped = _people[i];
                if (ped == null || !ped.Exists()) { _people.RemoveAt(i); continue; }
                if (ped.Position.DistanceTo(spot.Where) > spot.Roam * 2.2f) continue;

                try
                {
                    ped.MarkAsNoLongerNeeded();
                    ped.Delete();
                }
                catch { /* teardown */ }

                _people.RemoveAt(i);
            }
        }

        private static Vector3 Ground(Vector3 where)
        {
            try
            {
                if (World.GetGroundHeight(new Vector3(where.X, where.Y, where.Z + 1.5f),
                                          out var groundZ, GetGroundHeightMode.Normal) &&
                    groundZ > 0f && Math.Abs(groundZ - where.Z) <= 3f)
                {
                    where.Z = groundZ;
                }
            }
            catch
            {
                // Keep the authored height.
            }

            return where;
        }

        public void RestoreWorld()
        {
            foreach (var ped in _people)
            {
                try
                {
                    if (ped == null || !ped.Exists()) continue;
                    ped.MarkAsNoLongerNeeded();
                    ped.Delete();
                }
                catch { /* teardown */ }
            }

            _people.Clear();
        }
    }
}
