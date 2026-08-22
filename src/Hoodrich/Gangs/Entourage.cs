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

        /// <summary>
        /// Per station: what he is carrying, or null for the set's usual.
        ///
        /// Everybody armed used to get the same rifle. A man stood at a party turning a pistol
        /// over in his hands is a different picture to a man on a shutter with a choppa, and
        /// the difference is the weapon.
        /// </summary>
        private readonly List<string> _weapons = new List<string>();

        /// <summary>
        /// Per station: whether the height given is furniture rather than floor.
        /// </summary>
        private readonly List<bool> _onProp = new List<bool>();

        /// <summary>
        /// Per station: a looping animation instead of a scenario, as dict then clip.
        ///
        /// Scenarios cover standing, smoking, drinking and guarding, and they cover nothing
        /// that looks like a party. There is no dancing scenario and no DJ scenario, so the two
        /// people who make a yard read as a party have to be animated rather than scripted.
        ///
        /// Candidates in pairs, first that plays wins, and a station whose clips are all
        /// missing falls back to its scenario -- so an install without the club DLC gets
        /// somebody stood there rather than somebody T-posing.
        /// </summary>
        private readonly List<string[]> _anims = new List<string[]>();

        /// <summary>
        /// Which pair of <see cref="_anims"/> is on him, per station, or -1 for none yet --
        /// and the time after which it is fair to ask whether it took.
        ///
        /// Both exist because the two interesting questions cannot be answered when they were
        /// being asked. A streaming request is not readable in the frame it is made, and a task
        /// is not readable in the frame it is issued.
        /// </summary>
        private readonly List<int> _animPick = new List<int>();
        private readonly List<int> _animDue = new List<int>();

        /// <summary>How long a clip gets to visibly start before it is written off.</summary>
        private const int AnimGraceMs = 900;

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
                               string[] models = null, bool armed = true, bool onProp = false,
                               string[] anim = null, string weapon = null)
        {
            _stations.Add(where);
            _facings.Add(facing);
            _doing.Add(scenario);
            _models.Add(models);
            _armed.Add(armed);
            _onProp.Add(onProp);
            _anims.Add(anim);
            _animPick.Add(-1);
            _animDue.Add(0);
            _weapons.Add(weapon);
            return this;
        }

        /// <summary>The clip pairs for station i, or null for a plain scenario.</summary>
        private string[] AnimAt(int index)
        {
            return index < _anims.Count ? _anims[index] : null;
        }

        /// <summary>
        /// Starts one of these clips on him, and says WHICH one -- or -1 if none can start yet.
        ///
        /// This asked two questions that could not yet have answers, and the dancer and the DJ
        /// stood still for the entire life of the mod because of it.
        ///
        /// REQUEST_ANIM_DICT is a request, not a load: it returns immediately and the file
        /// arrives some frames later, so HAS_ANIM_DICT_LOADED on the very next line read false
        /// essentially always. Every candidate was skipped and every animated station fell
        /// straight through to its scenario. The request is now made for all of them on the way
        /// past, and residency is asked of a LATER call.
        ///
        /// TASK_PLAY_ANIM likewise has not started by the time the next line runs, so
        /// IS_ENTITY_PLAYING_ANIM read false even on success. That did more than fail: it
        /// stacked the second and third candidates on top of the first, reported failure, and
        /// the caller then started the scenario -- which cancels the animation. Several times a
        /// second, forever. Verification still happens, in Update, a tick later, where the
        /// answer means something.
        /// </summary>
        /// <param name="from">Pair offset to start at, so a clip proved missing is not retried.</param>
        private static int PlayAnim(Ped ped, string[] pairs, int from)
        {
            if (pairs == null || pairs.Length < 2) return -1;

            // Ask for all of them, including ones already passed over. The list is short, the
            // call is cheap, and a dict already resident costs nothing to request again.
            for (var i = 0; i + 1 < pairs.Length; i += 2)
            {
                try { Function.Call(Hash.REQUEST_ANIM_DICT, pairs[i]); }
                catch { /* a name this install has never heard of */ }
            }

            for (var i = from < 0 ? 0 : from; i + 1 < pairs.Length; i += 2)
            {
                try
                {
                    if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, pairs[i])) continue;

                    Function.Call(Hash.TASK_PLAY_ANIM, ped.Handle, pairs[i], pairs[i + 1],
                                  4f, -4f, -1, LoopingAnim, 0f, false, false, false);
                    return i;
                }
                catch
                {
                    // Try the next pair.
                }
            }

            return -1;
        }

        /// <summary>Looping, full body -- a dancer who stops dancing is worse than no dancer.</summary>
        private const int LoopingAnim = 1;

        /// <summary>
        /// Where station <paramref name="index"/> actually is.
        ///
        /// Most marks are a spot on the concrete read off a HUD, so they are snapped to whatever
        /// the ground turns out to be -- an authored Z a few centimetres out otherwise leaves a
        /// man hovering or shin-deep in it.
        ///
        /// A man on the couch is the exception, and it is why he was in the air. His height IS
        /// the seat, and the probe does not reliably see furniture: it answers with the floor on
        /// one pass and the cushion on the next, so the authored seat height was thrown away and
        /// replaced by whichever the probe happened to find -- on spawn, and then again on every
        /// Settle. Marks flagged onProp keep exactly the height they were given.
        /// </summary>
        private Vector3 MarkAt(int index)
        {
            if (index >= _marks.Count) return _spot;

            return index < _onProp.Count && _onProp[index]
                       ? _marks[index]
                       : Ground(_marks[index]);
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

        private string WeaponAt(int index) => index < _weapons.Count ? _weapons[index] : null;

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
                var ped = SpawnMember(gang, MarkAt(i), Facing(i), ModelsFor(i, gang), ArmedAt(i),
                                      WeaponAt(i));
                if (ped == null) continue;

                _crew.Add(ped);
                Idle(i, ped, Doing(i), Facing(i), MarkAt(i), Seated(i), AnimAt(i));
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

        private Ped SpawnMember(GangDef gang, Vector3 mark, float facing, string[] models, bool armed,
                                string carrying)
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

                    var handle = Function.Call<int>(Hash.CREATE_PED, PedTypeCiv, model.Hash,
                                                    mark.X, mark.Y, mark.Z, facing, false, false);

                    // A different outfit on every one of them.
                    //
                    // The rotation above stops neighbours sharing a MODEL, which was enough
                    // while every list had three or four in it. The game ships exactly one
                    // Families female, so three stations asking for her got the same woman
                    // three times over, stood in one yard. The native only ever picks a
                    // combination the model actually ships, so there is nothing to get wrong.
                    Function.Call(Hash.SET_PED_RANDOM_COMPONENT_VARIATION, handle, 0);

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

                        var gun = string.IsNullOrEmpty(carrying) ? Weapon : carrying;

                        Function.Call(Hash.GIVE_WEAPON_TO_PED, ped.Handle,
                                      Function.Call<uint>(Hash.GET_HASH_KEY, gun), 120, true, true);

                        // In the hands, not on the back.
                        Function.Call(Hash.SET_CURRENT_PED_WEAPON, ped.Handle,
                                      Function.Call<uint>(Hash.GET_HASH_KEY, gun), true);

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

        /// <summary>
        /// Puts somebody into their idle.
        ///
        /// Two different natives, because a chair is not a pavement. IN_PLACE starts a scenario
        /// where the ped happens to be standing and has no way to say "this one is a seat" --
        /// so a sitting scenario handed to it either does nothing or plays the sit standing up,
        /// and the man on the couch stayed on his feet next to it. AT_POSITION takes the spot,
        /// the heading, and a flag that means exactly that.
        ///
        /// Worse, a scenario that never takes leaves task 118 inactive, and Settle re-issues on
        /// exactly that condition -- so the failure was not a man standing still, it was a man
        /// being re-tasked to sit down forever.
        /// </summary>
        private void Idle(int index, Ped ped, string scenario, float facing, Vector3 at,
                          bool seated, string[] anim = null)
        {
            try
            {
                // An animated station tries its clips first and only falls back to the scenario
                // if none of them are in this install.
                if (anim != null)
                {
                    ped.Heading = facing;

                    var from = index >= 0 && index < _animPick.Count ? _animPick[index] : 0;
                    var picked = PlayAnim(ped, anim, from);

                    if (picked >= 0)
                    {
                        if (index >= 0 && index < _animPick.Count)
                        {
                            _animPick[index] = picked;
                            _animDue[index] = Game.GameTime + AnimGraceMs;
                        }

                        return;
                    }

                    // Nothing resident yet. The scenario below is what he does in the meantime,
                    // and the next pass tries again -- by which time the dict has usually landed.
                }

                if (seated)
                {
                    Function.Call(Hash.TASK_START_SCENARIO_AT_POSITION, ped.Handle, scenario,
                                  at.X, at.Y, at.Z, facing, 0, true, true);
                    return;
                }

                Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, ped.Handle, scenario, 0, true);
                ped.Heading = facing;
            }
            catch
            {
                // He will stand there regardless.
            }
        }

        /// <summary>Whether station i is somebody sat on something.</summary>
        private bool Seated(int index)
        {
            return index < _onProp.Count && _onProp[index];
        }

        /// <summary>Puts anybody who has wandered back on their mark.</summary>
        /// <summary>
        /// Pins a stationed man to his own spot for the length of a fight.
        ///
        /// Once each, not per tick. The defensive area is ped state and it survives the combat
        /// task laid over it, so re-asserting it every pass would have no visible effect and
        /// cost a handful of native calls a frame for nothing.
        ///
        /// The radius is generous next to the gang war's, because these are people stood about
        /// in a yard rather than holding a line, and a man who cannot step behind the couch he
        /// is already next to reads as broken rather than as disciplined.
        /// </summary>
        private void Hold(Ped ped, Vector3 station)
        {
            if (_held.Contains(ped.Handle)) return;
            _held.Add(ped.Handle);

            try
            {
                Function.Call(Hash.REMOVE_PED_DEFENSIVE_AREA, ped.Handle, false);
                Function.Call(Hash.REMOVE_PED_DEFENSIVE_AREA, ped.Handle, true);

                Function.Call(Hash.SET_PED_SPHERE_DEFENSIVE_AREA, ped.Handle,
                              station.X, station.Y, station.Z, HoldRadius, false, false);

                // 1 is CM_Defensive, which is the mode that hugs cover. 0 is CR_Near, which
                // stops him drifting off looking for a longer firing angle.
                Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, ped.Handle, 1);
                Function.Call(Hash.SET_PED_COMBAT_RANGE, ped.Handle, 0);

                foreach (var on in HoldOn)
                {
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, on, true);
                }

                foreach (var off in HoldOff)
                {
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, off, false);
                }
            }
            catch
            {
                // He fights the way he always did.
            }
        }

        /// <summary>Handles already dug in, so it is said once each.</summary>
        private readonly HashSet<int> _held = new HashSet<int>();

        /// <summary>Far enough to reach the wall behind him, not far enough to follow anybody.</summary>
        private const float HoldRadius = 18f;

        /// <summary>Cover, stand and fight, and get to the post before looking for a wall.</summary>
        private static readonly int[] HoldOn = { 0, 5, 12, 29, 44, 58 };

        /// <summary>
        /// And the ones that would undo all of it.
        ///
        /// 13 and 43 are both "charge" in disguise. 37, 45, 51 and 62 DELETE the defensive area
        /// the moment he reaches it, and 51 switches him to advance while doing it -- so he
        /// would arrive at his post and immediately leave. 71 lets him charge past the edge.
        /// </summary>
        private static readonly int[] HoldOff = { 13, 37, 43, 45, 47, 51, 62, 71 };

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

                var mark = MarkAt(i);
                var away = ped.Position.DistanceTo(mark);

                if (away <= DriftRange)
                {
                    // Home, but knocked out of what he was doing -- put him back to it once,
                    // not every pass, or he restarts the scenario forever.
                    //
                    // An animated station is asked a different question. Task 118 is the
                    // SCENARIO task and an animation never sets it, so testing for it would
                    // re-issue the dance several times a second and it would never get past
                    // the first frame.
                    var clips = AnimAt(i);
                    if (clips != null)
                    {
                        var pick = i < _animPick.Count ? _animPick[i] : -1;
                        if (pick >= 0 && pick + 1 < clips.Length)
                        {
                            // Issued, but not long enough ago to have started. Asking now and
                            // acting on the answer is the bug this whole path had.
                            if (Game.GameTime < _animDue[i]) continue;

                            if (Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM, ped.Handle,
                                                    clips[pick], clips[pick + 1], 3))
                            {
                                continue;
                            }

                            // Resident, issued, given its grace, and still not playing: this
                            // install has the dict but not that CLIP. Step past it so the next
                            // attempt tries the one below, which is what a candidate list is
                            // supposed to do.
                            Log.Debug(string.Format(
                                "entourage: {0} station {1} has no {2} / {3}, trying the next",
                                _who, i, clips[pick], clips[pick + 1]));

                            _animPick[i] = pick + 2 < clips.Length ? pick + 2 : -1;
                        }
                    }
                    else if (Function.Call<bool>(Hash.GET_IS_TASK_ACTIVE, ped.Handle, 118)) continue;

                    // In it, and standing where he belongs: give him this ground and leave him.
                    //
                    // Settle already refuses to re-task a man who is fighting, which is right.
                    // But it also meant nobody ever told him NOT to follow the fight down the
                    // road -- so he went, and Settle walked him back afterwards as though
                    // nothing had happened. A sphere on his own station is what was missing.
                    if (ped.IsInCombat || ped.IsRagdoll)
                    {
                        Hold(ped, MarkAt(i));
                        continue;
                    }

                    Idle(i, ped, Doing(i), Facing(i), MarkAt(i), Seated(i), AnimAt(i));
                    continue;
                }

                try
                {
                    // Whatever spooked them has to be over first. Walking a man back into the
                    // thing he ran from is worse than leaving him where he stopped.
                    //
                    // He still gets his station as a defensive area though, even from out
                    // here. It does not drag him back -- the fight decides that -- but it is
                    // the edge he will not go past, so he stops drifting further with every
                    // man he chases.
                    if (ped.IsInCombat || ped.IsRagdoll)
                    {
                        Hold(ped, MarkAt(i));
                        continue;
                    }
                    if (Function.Call<bool>(Hash.IS_PED_FLEEING, ped.Handle)) continue;

                    // A long way off and nobody looking: put him back. Anything closer he walks,
                    // because being teleported in front of you is the one thing that gives it
                    // away as a script.
                    if (away > 60f && !ped.IsOnScreen)
                    {
                        ped.Position = mark;
                        ped.Task.ClearAll();
                        Idle(i, ped, Doing(i), Facing(i), MarkAt(i), Seated(i), AnimAt(i));
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
