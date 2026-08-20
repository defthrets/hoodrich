using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Gangs
{
    /// <summary>
    /// The two men stood near somebody who matters.
    ///
    /// Nobody who runs anything stands on a corner by himself. A leader alone in an empty
    /// courtyard reads as a shopkeeper waiting for custom; the same man with two of his own
    /// people loitering nearby reads as somebody it would be a mistake to rob. They do nothing
    /// and they are not part of any system -- they are furniture that happens to be armed, and
    /// that is the entire job.
    ///
    /// Shared rather than written twice, because Lamar and Stretch want exactly the same thing
    /// and a second copy of this would be a second place to fix it.
    /// </summary>
    internal sealed class Entourage
    {
        /// <summary>How far out they stand, and how far apart from each other.</summary>
        private const float StandOff = 3.2f;

        private const float SpawnRange = 100f;
        private const float DespawnRange = 180f;
        private const int UpdateIntervalMs = 1100;

        /// <summary>How far they may drift before they are put back.</summary>
        private const float DriftRange = 5f;

        private const int PedTypeCiv = 4;

        /// <summary>
        /// Held rather than slung, which is the point.
        ///
        /// A rifle on a man's back is a man who owns a rifle. A rifle in his hands is a man
        /// standing guard, and that is the difference between decoration and a warning.
        /// </summary>
        private const string Weapon = "WEAPON_COMPACTRIFLE";

        /// <summary>
        /// Idles that keep a weapon in hand. A scenario that puts the gun away defeats the
        /// whole thing, so these are the standing-about ones rather than smoking or drinking.
        /// </summary>
        private static readonly string[] Scenarios =
        {
            "WORLD_HUMAN_GUARD_STAND", "WORLD_HUMAN_GUARD_PATROL", "WORLD_HUMAN_STAND_IMPATIENT"
        };

        private readonly GangRegistry _gangs;
        private readonly string _gangId;
        private readonly Vector3 _spot;
        private readonly float _heading;
        private readonly string _who;

        /// <summary>
        /// Exactly where they stand and what each of them is doing.
        ///
        /// Positions given outright rather than worked out from the leader's heading, because
        /// "a step back and to the right" is arithmetic and "on that corner, facing the stairs"
        /// is a decision somebody made standing there. The scenario is per-man too: two people
        /// doing the identical thing beside each other reads as one man copied.
        /// </summary>
        private readonly List<Vector3> _stations = new List<Vector3>();
        private readonly List<float> _facings = new List<float>();
        private readonly List<string> _doing = new List<string>();

        /// <summary>
        /// Per station: a model of its own, or null for whoever the set is made of.
        ///
        /// A woman working a courtyard is not a Families member, and a man holding a beer is not
        /// holding a rifle. Both used to be, because everybody here came out of one list and was
        /// handed one weapon.
        /// </summary>
        private readonly List<string[]> _models = new List<string[]>();
        private readonly List<bool> _armed = new List<bool>();

        private readonly List<Ped> _crew = new List<Ped>();
        private readonly List<Vector3> _marks = new List<Vector3>();

        private int _lastUpdate;

        public Entourage(GangRegistry gangs, string gangId, Vector3 spot, float heading, string who)
        {
            _gangs = gangs;
            _gangId = gangId;
            _spot = spot;
            _heading = heading;
            _who = who;
        }

        /// <summary>Adds one of them, on his own mark, doing his own thing.</summary>
        public Entourage Stand(Vector3 where, float facing, string scenario,
                               string[] models = null, bool armed = true)
        {
            _stations.Add(where);
            _facings.Add(facing);
            _doing.Add(scenario);
            _models.Add(models);
            _armed.Add(armed);
            return this;
        }

        private string[] ModelsFor(int index, GangDef gang)
        {
            if (index < _models.Count && _models[index] != null && _models[index].Length > 0)
            {
                return _models[index];
            }

            var own = new string[gang.MemberModels.Count];
            for (var i = 0; i < own.Length; i++) own[i] = gang.MemberModels[i];
            return own;
        }

        private bool ArmedAt(int index) => index >= _armed.Count || _armed[index];

        public void Update()
        {
            var now = Game.GameTime;
            if (now - _lastUpdate < UpdateIntervalMs) return;
            _lastUpdate = now;

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return;

            var away = player.Position.DistanceTo(_spot);

            if (_crew.Count > 0)
            {
                if (away > DespawnRange) Despawn();
                else Settle();

                return;
            }

            if (away <= SpawnRange) Spawn();
        }

        private void Spawn()
        {
            var gang = _gangs.Get(_gangId);
            if (gang == null) return;

            _marks.Clear();

            if (_stations.Count > 0)
            {
                _marks.AddRange(_stations);
            }
            else
            {
                // No marks given, so either side of him and a step back.
                var rad = _heading * (float)(Math.PI / 180.0);
                var right = new Vector3((float)Math.Cos(rad), -(float)Math.Sin(rad), 0f);
                var back = new Vector3(-(float)Math.Sin(rad), -(float)Math.Cos(rad), 0f);

                _marks.Add(_spot + right * StandOff + back * 1.2f);
                _marks.Add(_spot - right * StandOff + back * 1.6f);
            }

            for (var i = 0; i < _marks.Count; i++)
            {
                var ped = SpawnMember(gang, _marks[i], Facing(i), ModelsFor(i, gang), ArmedAt(i));
                if (ped == null) continue;

                _crew.Add(ped);
                Idle(ped, Doing(i), Facing(i));
            }

            if (_crew.Count > 0) Log.Info(_crew.Count + " of " + gang.Name + " stood with " + _who + ".");
        }

        private float Facing(int index)
        {
            return index < _facings.Count ? _facings[index] : _heading;
        }

        private string Doing(int index)
        {
            if (index < _doing.Count && !string.IsNullOrEmpty(_doing[index])) return _doing[index];

            return Scenarios[index % Scenarios.Length];
        }

        private Ped SpawnMember(GangDef gang, Vector3 mark, float facing, string[] models, bool armed)
        {
            // Started at a different place in the list for each of them, and wrapped.
            //
            // Walking the list from the top and taking the first that loads means the first
            // entry wins every single time, so every man on the block was the same man. The
            // list is short, so an offset is enough -- and it is stable per station rather than
            // random, so somebody does not change face every time you walk back up the street.
            var order = new List<string>();
            var from = Math.Abs(_crew.Count + _stations.Count * 3) % Math.Max(1, models.Length);

            for (var i = 0; i < models.Length; i++) order.Add(models[(from + i) % models.Length]);

            foreach (var name in order)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1200)) continue;

                    var spot = Ground(mark);

                    var handle = Function.Call<int>(Hash.CREATE_PED, PedTypeCiv, model.Hash,
                                                    spot.X, spot.Y, spot.Z, facing, false, false);

                    model.MarkAsNoLongerNeeded();
                    if (handle == 0) continue;

                    var ped = Entity.FromHandle(handle) as Ped;
                    if (ped == null || !ped.Exists()) continue;

                    ped.IsPersistent = true;

                    // They can be startled. Blocking non-temporary events -- which is what this
                    // used to do -- makes somebody incapable of reacting to gunfire, a car on
                    // the pavement or a fight in front of them, which is a mannequin rather than
                    // a neighbour. They scatter like anybody would, and Settle walks them back.
                    ped.BlockPermanentEvents = false;
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);

                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, ped.Handle, true, true);
                    Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, ped.Handle, false);

                    if (armed)
                    {
                        Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle, gang.GroupHash);

                        Function.Call(Hash.GIVE_WEAPON_TO_PED, ped.Handle,
                                      Function.Call<uint>(Hash.GET_HASH_KEY, Weapon), 120, true, true);

                        // In the hands, not on the back.
                        Function.Call(Hash.SET_CURRENT_PED_WEAPON, ped.Handle,
                                      Function.Call<uint>(Hash.GET_HASH_KEY, Weapon), true);

                        Function.Call(Hash.SET_PED_CAN_SWITCH_WEAPON, ped.Handle, false);
                    }

                    return ped;
                }
                catch
                {
                    // Try the next model.
                }
            }

            return null;
        }

        private static void Idle(Ped ped, string scenario, float facing)
        {
            try
            {
                Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, ped.Handle, scenario, 0, true);
                ped.Heading = facing;
            }
            catch
            {
                // He will stand there regardless.
            }
        }

        /// <summary>Puts anybody who has wandered back on their mark.</summary>
        private void Settle()
        {
            for (var i = _crew.Count - 1; i >= 0; i--)
            {
                var ped = _crew[i];

                if (ped == null || !ped.Exists() || !ped.IsAlive)
                {
                    // Dead or gone is left alone. Replacing a man who was just shot in front of
                    // you is worse than being two men down.
                    _crew.RemoveAt(i);
                    continue;
                }

                if (i >= _marks.Count) continue;

                var mark = Ground(_marks[i]);
                var away = ped.Position.DistanceTo(mark);

                if (away <= DriftRange)
                {
                    // Home, but knocked out of what he was doing -- put him back to it once,
                    // not every pass, or he restarts the scenario forever.
                    if (Function.Call<bool>(Hash.GET_IS_TASK_ACTIVE, ped.Handle, 118)) continue;
                    if (ped.IsInCombat || ped.IsRagdoll) continue;

                    Idle(ped, Doing(i), Facing(i));
                    continue;
                }

                try
                {
                    // Whatever spooked them has to be over first. Walking a man back into the
                    // thing he ran from is worse than leaving him where he stopped.
                    if (ped.IsInCombat || ped.IsRagdoll) continue;
                    if (Function.Call<bool>(Hash.IS_PED_FLEEING, ped.Handle)) continue;

                    // A long way off and nobody looking: put him back. Anything closer he walks,
                    // because being teleported in front of you is the one thing that gives it
                    // away as a script.
                    if (away > 60f && !ped.IsOnScreen)
                    {
                        ped.Position = mark;
                        ped.Task.ClearAll();
                        Idle(ped, Doing(i), Facing(i));
                        continue;
                    }

                    // Already walking back.
                    if (Function.Call<bool>(Hash.GET_IS_TASK_ACTIVE, ped.Handle, 224)) continue;

                    Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, ped.Handle,
                                  mark.X, mark.Y, mark.Z, 1.2f, 20000, 1.0f, 0, Facing(i));
                }
                catch
                {
                    // He will settle.
                }
            }
        }

        /// <summary>
        /// Only trusts a ground probe that agrees with the authored height, for the same reason
        /// everything else here does: a probe from above a courtyard finds the walkway over it.
        /// </summary>
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

        private void Despawn()
        {
            foreach (var ped in _crew)
            {
                try
                {
                    if (ped == null || !ped.Exists()) continue;
                    ped.MarkAsNoLongerNeeded();
                    ped.Delete();
                }
                catch { /* teardown */ }
            }

            _crew.Clear();
        }

        public void RestoreWorld() => Despawn();
    }
}
