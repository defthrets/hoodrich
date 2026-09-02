using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Gangs
{
    /// <summary>
    /// Three of the set holding one spot, on a leash, with rifles under their coats.
    ///
    /// THIS IS THE GAP BETWEEN THE OTHER TWO. BlockLife nails a man to a mark outside his own
    /// building and he never leaves it; Walkers sends crews wandering the back streets and they
    /// are never anywhere in particular. A corner nobody is standing on and a street where
    /// everybody is walking somewhere are both wrong in the same way -- neither of them is a
    /// group of people who are AT a place.
    ///
    /// So these have a post and a leash. Twenty metres is far enough that they drift about it
    /// rather than stand in it, and short enough that they are recognisably the men on that
    /// corner rather than three people who happen to be nearby. Come back in ten minutes and
    /// they are still there.
    ///
    /// THEY ARE NOT DEAF, and that is deliberate. The takeover ring is blocked from
    /// non-temporary events because a crowd that scatters at the first bang ends the event --
    /// these are the opposite case. They are armed men on their own block: somebody who ignored
    /// a firefight starting next to him would be the thing that reads as fake, and the whole
    /// point of the rifles is that they answer.
    ///
    /// ONE POST PER INSTANCE, because that is what makes them a place rather than a system.
    /// Main holds nine of them at nine walked corners.
    /// </summary>
    internal sealed class Posted
    {
        /// <summary>One man, and what he is currently doing about it.</summary>
        private sealed class Man
        {
            public Ped Who;

            /// <summary>When he next changes between standing about and drifting.</summary>
            public int SwapAt;

            /// <summary>True while he is wandering rather than stood doing something.</summary>
            public bool Moving;

            /// <summary>What he does when he is stood. Picked once -- see Settle.</summary>
            public string Doing = "";
        }

        private readonly Vector3 _at;
        private readonly float _heading;
        private readonly GangRegistry _gangs;
        private readonly string _gangId;
        private readonly Random _rng = new Random();

        private readonly List<Man> _men = new List<Man>();

        private int _lastUpdate;

        /// <summary>True once all three are down. See the note in Update.</summary>
        private bool _staffed;

        public Posted(Vector3 at, float heading, GangRegistry gangs, string gangId)
        {
            _at = at;
            _heading = heading;
            _gangs = gangs;
            _gangId = gangId;
        }

        public void Update()
        {
            var now = Game.GameTime;
            if (now - _lastUpdate < UpdateIntervalMs) return;
            _lastUpdate = now;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            var away = player.Position.DistanceTo(_at);

            Bury();

            // TOPPED UP RATHER THAN FILLED ONCE, because asking for a model no longer waits
            // for it -- a corner whose third man was not resident yet used to get two men for
            // ever. It keeps asking until it has three and then stops, so somebody shot on this
            // corner stays shot until you leave and come back.
            if (!_staffed && away <= SpawnRange) Fill();

            if (_men.Count == 0) return;

            if (away > DespawnRange)
            {
                Clear();
                return;
            }

            Keep(now);
        }

        /// <summary>Forget anybody who has died or been cleaned up by the game.</summary>
        private void Bury()
        {
            for (var i = _men.Count - 1; i >= 0; i--)
            {
                var m = _men[i];

                if (m.Who != null && m.Who.Exists() && m.Who.IsAlive) continue;

                // A body is left where it fell rather than deleted. Somebody who was shot on
                // this corner two minutes ago should still be on it.
                if (m.Who != null && m.Who.Exists()) m.Who.MarkAsNoLongerNeeded();

                _men.RemoveAt(i);
            }
        }

        private void Fill()
        {
            var gang = _gangs == null ? null : _gangs.Get(_gangId);
            if (gang == null || gang.MemberModels.Count == 0) return;

            for (var i = _men.Count; i < Three; i++)
            {
                try
                {
                    // Spread round the mark rather than stacked on it -- three men created at
                    // one coordinate is three men shoved apart by the physics on the first
                    // frame, which is a scramble rather than a group standing together.
                    var a = _rng.NextDouble() * Math.PI * 2d;
                    var r = 0.8f + (float)_rng.NextDouble() * Apart;

                    var spot = new Vector3(_at.X + (float)Math.Cos(a) * r,
                                           _at.Y + (float)Math.Sin(a) * r, _at.Z);

                    var man = GangPeds.OnFoot(gang, gang.MemberModels, spot, _heading);

                    if (man == null) continue;

                    Arm(man);

                    _men.Add(new Man { Who = man, SwapAt = 0, Moving = false });
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not post one up: " + ex.Message);
                }
            }

            if (_men.Count < Three) return;

            _staffed = true;

            Log.Info("Posted " + _men.Count + " on the corner at " + _at + ".");
        }

        /// <summary>
        /// A rifle, and the willingness to use it.
        ///
        /// HOLSTERED, NOT IN HIS HANDS. GIVE_WEAPON_TO_PED with equipNow set would have three
        /// men standing on a residential corner holding rifles at all times, which is a
        /// checkpoint rather than a block. He pulls it when something starts.
        ///
        /// Nothing here blocks non-temporary events. See the note on the class: these are meant
        /// to react.
        /// </summary>
        private static void Arm(Ped man)
        {
            var h = man.Handle;

            Function.Call(Hash.GIVE_WEAPON_TO_PED, h,
                          Game.GenerateHash("WEAPON_COMPACTRIFLE"), 120, false, false);

            Function.Call(Hash.SET_PED_ACCURACY, h, 30);
            Function.Call(Hash.SET_PED_COMBAT_ABILITY, h, 2);
            Function.Call(Hash.SET_PED_COMBAT_RANGE, h, 2);

            // 46 is "will fight armed peds when not armed" and 5 is "always fight" -- between
            // them, a man who does not walk away from something starting on his own corner.
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, h, 46, true);
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, h, 5, true);

            Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, h, 0, false);
            Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, h, false);
        }

        /// <summary>
        /// Keep them doing something, and swap between the two things they do.
        ///
        /// EACH MAN ON HIS OWN CLOCK. One timer for the group would have all three light up and
        /// all three set off at the same instant, which is a squad drill. Staggered, somebody is
        /// always stood while somebody else is drifting, which is what three men on a corner
        /// look like.
        ///
        /// A man who has been pulled into a fight is left entirely alone -- re-tasking somebody
        /// mid-gunfight to go and have a cigarette is the mod overriding the thing it wanted.
        /// </summary>
        private void Keep(int now)
        {
            foreach (var m in _men)
            {
                if (m.Who == null || !m.Who.Exists() || !m.Who.IsAlive) continue;

                try
                {
                    if (Function.Call<bool>(Hash.IS_PED_IN_COMBAT, m.Who.Handle, 0)) continue;
                    if (Function.Call<bool>(Hash.IS_PED_RAGDOLL, m.Who.Handle)) continue;
                }
                catch
                {
                    continue;
                }

                if (now < m.SwapAt) continue;

                m.Moving = !m.Moving;

                m.SwapAt = now + (m.Moving
                    ? _rng.Next(WalkMinMs, WalkMaxMs)
                    : _rng.Next(StandMinMs, StandMaxMs));

                try
                {
                    Function.Call(Hash.CLEAR_PED_TASKS, m.Who.Handle);

                    if (m.Moving)
                    {
                        // THE LEASH. Wander in an area is the game's own "mill about here",
                        // and the radius is the whole point -- he goes where he likes inside
                        // twenty metres of the mark and never leaves it.
                        Function.Call(Hash.TASK_WANDER_IN_AREA, m.Who.Handle,
                                      _at.X, _at.Y, _at.Z, Leash, 3f, 8f);

                        Function.Call(Hash.SET_PED_KEEP_TASK, m.Who.Handle, true);
                        continue;
                    }

                    Settle(m);
                }
                catch
                {
                    // He keeps whatever he was doing.
                }
            }
        }

        /// <summary>
        /// Stood doing something, and it is the same something every time for that man.
        ///
        /// Picked once and kept. Rolling a fresh one each time he stops would have the man who
        /// was drinking come back from a wander smoking, which reads as a different person
        /// standing in the same place -- the same reasoning the takeover crowd uses.
        /// </summary>
        private void Settle(Man m)
        {
            if (string.IsNullOrEmpty(m.Doing)) m.Doing = Doings[_rng.Next(Doings.Length)];

            Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, m.Who.Handle, m.Doing, 0, true);
        }

        public void Clear()
        {
            foreach (var m in _men)
            {
                try
                {
                    if (m.Who != null && m.Who.Exists()) m.Who.Delete();
                }
                catch
                {
                    // Already gone.
                }
            }

            _men.Clear();
            _staffed = false;
        }

        public void RestoreWorld()
        {
            Clear();
        }

        /// <summary>
        /// What they do while they are stood.
        ///
        /// HANG_OUT_STREET is the talking one -- it is the loose-limbed gesturing idle the game
        /// uses for somebody mid-conversation, and with three of them inside a couple of metres
        /// it reads as three men talking rather than three men each performing separately. It
        /// is weighted heaviest for that reason.
        /// </summary>
        private static readonly string[] Doings =
        {
            "WORLD_HUMAN_HANG_OUT_STREET", "WORLD_HUMAN_HANG_OUT_STREET",
            "WORLD_HUMAN_HANG_OUT_STREET",
            "WORLD_HUMAN_SMOKING", "WORLD_HUMAN_SMOKING",
            "WORLD_HUMAN_DRINKING", "WORLD_HUMAN_DRINKING"
        };

        private const int Three = 3;

        /// <summary>How far they may drift from the mark, and how far apart they are put down.</summary>
        private const float Leash = 20f;
        private const float Apart = 1.6f;

        /// <summary>How long a stretch of standing about lasts, and a stretch of drifting.</summary>
        private const int StandMinMs = 22000;
        private const int StandMaxMs = 55000;
        private const int WalkMinMs = 12000;
        private const int WalkMaxMs = 30000;

        /// <summary>Near enough to be worth existing, and far enough to stop.</summary>
        private const float SpawnRange = 110f;
        private const float DespawnRange = 190f;

        private const int UpdateIntervalMs = 1500;
    }
}
