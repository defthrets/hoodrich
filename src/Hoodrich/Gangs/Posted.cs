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

            /// <summary>What he is stood doing right now. Re-rolled each time -- see Settle.</summary>
            public string Doing = "";
        }

        private readonly Vector3 _at;
        private readonly float _heading;
        private readonly GangRegistry _gangs;
        private readonly string _gangId;
        private readonly Random _rng = new Random();

        private readonly List<Man> _men = new List<Man>();

        private int _lastUpdate;

        /// <summary>When one of them may next look up at you. See Keep.</summary>
        private int _nextLook;

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

            Keep(now, player);
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

                    var spot = Ground(new Vector3(_at.X + (float)Math.Cos(a) * r,
                                                  _at.Y + (float)Math.Sin(a) * r, _at.Z));

                    var man = GangPeds.OnFoot(gang, gang.MemberModels, spot, _heading);

                    if (man == null) continue;

                    Arm(man);

                    // OUT OF PHASE FROM THE FIRST FRAME, AND ALREADY DOING SOMETHING.
                    //
                    // Every man used to be created with SwapAt of zero and Moving false, which
                    // is three men who stand still until the same tick and then all three
                    // change at once, for ever -- their timers are different lengths but they
                    // were started together, and starting together is what you see. Walking up
                    // on them found three blokes stood to attention in a row.
                    //
                    // So each one starts somewhere random in the middle of a stretch: one is
                    // already drifting, another is smoking, the third is a few seconds off
                    // changing his mind. They never line up again after that.
                    var walking = _rng.Next(2) == 0;

                    var one = new Man
                    {
                        Who = man,
                        Moving = walking,
                        SwapAt = Game.GameTime + _rng.Next(1000, walking ? WalkMaxMs : StandMaxMs)
                    };

                    if (walking) Roam(man); else Settle(one);

                    _men.Add(one);
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
        /// The height of the pavement under a point, rather than the height it was read at.
        ///
        /// THIS IS WHY THEY WERE HOVERING. CREATE_PED puts a man exactly where it is told and
        /// does nothing whatsoever about the floor -- there is no equivalent of the object
        /// call that drops a prop onto the ground.
        ///
        /// And the height it was told was never the pavement to begin with. Those marks were
        /// read off a player standing at them, and they are also spread a metre or two around
        /// the mark so three men do not appear inside each other -- so the ground under each of
        /// them is a kerb, a step or a verge away from the one under the reading. Asking the
        /// world what is actually beneath THIS point is the only thing that answers that.
        ///
        /// Probed from two metres up, because a probe started below a surface finds whatever is
        /// under it instead. If the world will not answer, the read height stands: it came off
        /// somebody standing there, so it is wrong by a little rather than by a storey.
        /// </summary>
        private static Vector3 Ground(Vector3 at)
        {
            try
            {
                float z;

                if (World.GetGroundHeight(new Vector3(at.X, at.Y, at.Z + 2f), out z,
                                          GetGroundHeightMode.Normal))
                {
                    return new Vector3(at.X, at.Y, z);
                }
            }
            catch
            {
                // The read height stands.
            }

            return at;
        }

        /// <summary>
        /// One of the guard guns, and the willingness to use it.
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

            Function.Call(Hash.GIVE_WEAPON_TO_PED, h, Arms.GuardHashAt(man.Position), 120, false, false);

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
        private void Keep(int now, Ped player)
        {
            // ONE OF THEM LOOKS UP, NOT ALL OF THEM. Everybody turning to face you at once is
            // a cutscene; one man clocking you while the other two carry on is a corner.
            //
            // Gated on a clock as well as the dice so it is an occasional thing rather than a
            // constant low-level staring, and only when you are close enough that a man would
            // actually have noticed you.
            var look = false;

            if (now >= _nextLook && player.Position.DistanceTo(_at) <= NoticeRange)
            {
                _nextLook = now + _rng.Next(NoticeMinMs, NoticeMaxMs);
                look = true;
            }

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

                if (look && _rng.Next(3) == 0)
                {
                    look = false;

                    try
                    {
                        GangPeds.Notice(m.Who, player);
                    }
                    catch
                    {
                        // He did not look. Nothing depends on it.
                    }
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
                        Roam(m.Who);
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
        /// THE LEASH. Wander in an area is the game's own "mill about here", and the radius is
        /// the whole point -- he goes where he likes inside twenty metres of the mark and never
        /// leaves it.
        /// </summary>
        private void Roam(Ped man)
        {
            Function.Call(Hash.TASK_WANDER_IN_AREA, man.Handle, _at.X, _at.Y, _at.Z,
                          Leash, 3f, 8f);

            Function.Call(Hash.SET_PED_KEEP_TASK, man.Handle, true);
        }

        /// <summary>
        /// Stood doing something, and something different from what he was doing before.
        ///
        /// THIS IS A REVERSAL AND IT IS DELIBERATE. It used to pick once and keep it for the
        /// man's whole life, on the reasoning that somebody who was drinking and comes back
        /// from a wander smoking reads as a different person in the same spot.
        ///
        /// That is right for the takeover crowd and wrong here, and the difference is the
        /// distance. Sixty people seen across a junction are read as a texture, so one of them
        /// swapping props is a continuity error. Three men you are stood next to for several
        /// minutes are read as people, and a man who does exactly one thing for ever is a
        /// waxwork -- you watch him finish a cigarette and start the identical cigarette again.
        ///
        /// Never the same thing twice running, because "different" that comes back the same
        /// half the time is not different. Four tries rather than a loop: the list is short and
        /// weighted, and it is better to repeat than to spin.
        /// </summary>
        private void Settle(Man m)
        {
            var pick = Doings[_rng.Next(Doings.Length)];

            for (var i = 0; i < 4 && pick == m.Doing; i++) pick = Doings[_rng.Next(Doings.Length)];

            m.Doing = pick;

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

            // What they are actually stood there for. Two of them, because the "hard" one is
            // the aggressive version of the same idle and the pair of them together read as
            // two men serving rather than one man doing a routine.
            "WORLD_HUMAN_DRUG_DEALER", "WORLD_HUMAN_DRUG_DEALER_HARD",

            // Against the wall and the fence. A corner with nobody leaning on anything is a
            // bus queue.
            "WORLD_HUMAN_LEANING", "WORLD_HUMAN_LEANING",

            "WORLD_HUMAN_SMOKING", "WORLD_HUMAN_SMOKING", "WORLD_HUMAN_SMOKING_POT",
            "WORLD_HUMAN_DRINKING", "WORLD_HUMAN_DRINKING",

            // On their phones, which is most of what anybody does stood anywhere.
            "WORLD_HUMAN_STAND_MOBILE", "WORLD_HUMAN_STAND_MOBILE_UPRIGHT",

            // Watching the road, and fed up of watching the road.
            "WORLD_HUMAN_GUARD_STAND", "WORLD_HUMAN_STAND_IMPATIENT"
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

        /// <summary>
        /// How near you have to be for a corner to be occupied, and how far to give it up.
        ///
        /// A HUNDRED AND TEN WAS A STREET AWAY. These are meant to be the men who are on that
        /// corner, and a corner that is only occupied once you are almost on it is a corner
        /// that is empty every time you look down the road at it -- which is worse than not
        /// having them, because you see it happen.
        ///
        /// Two hundred and twenty. Far enough that they are already there when the corner comes
        /// into view, and the gap up to three hundred and forty stops a corner flickering on
        /// and off while you stand at the edge of its range.
        /// </summary>
        private const float SpawnRange = 220f;
        private const float DespawnRange = 340f;

        /// <summary>How close you have to be to be worth looking at, and how often one does.</summary>
        private const float NoticeRange = 14f;
        private const int NoticeMinMs = 9000;
        private const int NoticeMaxMs = 22000;

        private const int UpdateIntervalMs = 1500;
    }
}
