using System;
using System.Collections.Generic;
using GTA;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Paint
{
    /// <summary>
    /// What the street makes of a man standing there with a can.
    ///
    /// THE GAME THINKS HE IS ARMED, because underneath the can he is: the extinguisher is a
    /// weapon, and the moment a weapon is out every passer-by treats him as a threat and runs.
    /// That is right for a rifle and absurd for spray paint -- nobody in the world has ever
    /// sprinted away from somebody tagging a wall. They complain about it.
    ///
    /// So this does two things, and only while the tool is actually in his hands:
    ///
    ///   - stops the panic, by suppressing the shocking events an armed player raises
    ///   - replaces it with somebody telling him what they think
    ///
    /// Both stop the instant he puts it away. Nothing here is left switched on.
    /// </summary>
    internal sealed class Bystanders
    {
        /// <summary>How near somebody has to be to bother saying anything.</summary>
        private const float Earshot = 16f;

        /// <summary>Roughly how often anybody pipes up, and how long before the same one does again.</summary>
        private const int GripeEveryMs = 7000;
        private const int GripeSpreadMs = 9000;
        private const int SamePersonMs = 45000;

        /// <summary>Already-raised events are cleared now and then, not every frame.</summary>
        private const int CalmEveryMs = 1500;

        /// <summary>
        /// How many people are looked at before a pass gives up, and how far apart the looks
        /// are. One each, a frame or two apart -- NOT ALL OF THEM IN ONE TICK.
        ///
        /// WORTH ENDS ON A RAYCAST and a raycast is not free. The first version of this walked
        /// every ped in earshot until one passed, which outside Gerald's with the block turned
        /// out is thirty casts in a single frame; the tick watchdog measured it holding the
        /// frame for forty-six to fifty-one milliseconds, three hundred and sixty-eight times
        /// in one session, more than every other system in the mod put together.
        ///
        /// Capping it at six helped and did not fix it: six casts plus a whole-pool ped sweep
        /// still measured FIFTY-TWO MILLISECONDS, two hundred and eighty-four times in a nine
        /// hour session, which is three dropped frames every time somebody might have had
        /// something to say. A cap makes the spike smaller. It does not stop it being a spike.
        ///
        /// So the six are SPREAD. One candidate per pass, a couple of frames apart, until one
        /// of them bites or six have been tried -- the same six looks, over about half a
        /// second of wall clock instead of inside one frame. Nothing about what the street
        /// does changes; the work just stops arriving all at once.
        /// </summary>
        private const int ProbesMost = 6;
        private const int ProbeGapMs = 90;

        /// <summary>
        /// What they say.
        ///
        /// The game's own ambient lines, so every ped says it in their own voice -- a Vinewood
        /// jogger and a Davis corner boy are rude in different accents without a word of this
        /// being written down. Recording lines would mean one voice for the whole city.
        ///
        /// Weighted by repetition rather than by a table: insults and curses come up most,
        /// with the odd shrug and the odd person who just stares.
        /// </summary>
        private static readonly string[] Rude =
        {
            "GENERIC_INSULT_HIGH",
            "GENERIC_INSULT_HIGH",
            "GENERIC_INSULT_MED",
            "GENERIC_CURSE_HIGH",
            "GENERIC_CURSE_HIGH",
            "GENERIC_CURSE_MED",
            "PROVOKE_GENERIC",
            "PROVOKE_STARING",
            "GENERIC_WHATEVER",
            "GENERIC_SHOCKED_MED"
        };

        private readonly Random _rng = new Random();

        /// <summary>Who has had their say lately, so the same man is not shouting on a loop.</summary>
        private readonly Dictionary<int, int> _said = new Dictionary<int, int>();

        private int _nextGripe;
        private int _nextCalm;
        private bool _quieted;

        /// <summary>
        /// Called every tick.
        ///
        /// <paramref name="out"/> is whether the tool is in his hands. Everything here is
        /// conditional on it, and the moment it goes false the world is handed back exactly as
        /// it was found.
        /// </summary>
        public void Update(bool tool)
        {
            if (!tool)
            {
                Release();
                return;
            }

            try
            {
                Quiet();
                Gripe();
            }
            catch (Exception ex)
            {
                Log.Debug("Bystanders: " + ex.Message);
            }
        }

        /// <summary>Stops the running-away.</summary>
        private void Quiet()
        {
            // Every frame, because it is a NEXT FRAME suppression -- it lapses the moment it
            // stops being asked for, which is exactly the property wanted here. Put the can
            // away and the street is frightened of guns again with no cleanup required.
            Function.Call(Hash.SUPPRESS_SHOCKING_EVENTS_NEXT_FRAME);

            if (!_quieted)
            {
                _quieted = true;

                // A player-level toggle rather than a per-ped one, and it does have to be
                // turned back off -- see Release. It is the difference between somebody
                // noticing a man with a can and somebody reacting to a man with a gun.
                Function.Call(Hash.SET_IGNORE_LOW_PRIORITY_SHOCKING_EVENTS, Game.Player.Handle, true);
            }

            var now = Game.GameTime;
            if (now < _nextCalm) return;

            _nextCalm = now + CalmEveryMs;

            // Suppression only stops NEW events. Anything raised in the moment before the can
            // came out is still standing, and a crowd already running does not stop because
            // the reason went away.
            Function.Call(Hash.REMOVE_ALL_SHOCKING_EVENTS, false);
        }

        /// <summary>
        /// Somebody says what they think -- looked for ONE PERSON AT A TIME. See ProbesMost.
        ///
        /// A pass is now a little run of frames rather than a single one. Starting it takes
        /// the ped sweep and picks a random place in the list to begin; each frame after that
        /// tests one of them, and the run ends the moment somebody bites or after six tries.
        ///
        /// THE LIST IS TAKEN ONCE AND KEPT FOR THE RUN. GetNearbyPeds walks the whole ped pool
        /// and builds an object for every hit, which is the other half of the fifty-two
        /// milliseconds -- doing it six times to spread six raycasts would have moved the cost
        /// rather than removed it. Half a second of a pedestrian being where he was half a
        /// second ago is not a mistake anybody can see.
        /// </summary>
        private void Gripe()
        {
            var now = Game.GameTime;

            var me = Game.Player.Character;
            if (me == null || !me.Exists()) return;

            // ---- mid-run: one look, then back next frame ----
            if (_run != null)
            {
                if (now < _nextProbe) return;

                _nextProbe = now + ProbeGapMs;

                if (_tried >= ProbesMost || _tried >= _run.Length)
                {
                    // Nobody, and that is a normal answer. The next run comes round on the
                    // ordinary clock and tries six different people.
                    Done(now);
                    return;
                }

                var one = _run[(_from + _tried) % _run.Length];
                _tried++;

                if (!Worth(one, me, now)) return;

                Done(now);
                Say(one, me, now);
                return;
            }

            // ---- starting one ----
            if (now < _nextGripe) return;

            var near = World.GetNearbyPeds(me, Earshot);

            if (near == null || near.Length == 0)
            {
                Done(now);
                return;
            }

            // Shuffled by starting somewhere random rather than always at the nearest, or the
            // same unlucky pedestrian narrates the entire session.
            _run = near;
            _from = _rng.Next(near.Length);
            _tried = 0;
            _nextProbe = now;
        }

        /// <summary>The run is over, whoever it found. The long clock starts again here.</summary>
        private void Done(int now)
        {
            _run = null;
            _tried = 0;
            _nextGripe = now + GripeEveryMs + _rng.Next(GripeSpreadMs);
        }

        /// <summary>Whoever it landed on, turning round and saying it.</summary>
        private void Say(Ped ped, Ped me, int now)
        {
            _said[ped.Handle] = now;

            try
            {
                // Turned toward him first. A voice from somebody facing the other way is a
                // sound effect; a man turning round to say it is a reaction.
                Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, ped.Handle, me.Handle, 2500);

                Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, ped.Handle,
                              Rude[_rng.Next(Rude.Length)], "SPEECH_PARAMS_FORCE");
            }
            catch
            {
                // A silent bystander. Nothing else depends on it.
            }
        }

        /// <summary>The sweep a run is working through, where it started, and how far it got.</summary>
        private Ped[] _run;
        private int _from;
        private int _tried;
        private int _nextProbe;

        /// <summary>Whether this one is in a position to have an opinion.</summary>
        private bool Worth(Ped ped, Ped me, int now)
        {
            try
            {
                if (ped == null || !ped.Exists()) return false;
                if (ped.Handle == me.Handle) return false;
                if (!ped.IsAlive || ped.IsInVehicle()) return false;

                // Not anybody mid-fight.
                if (ped.IsInCombat) return false;

                // AND NOT THE POLICE, which the comment here used to promise while the code
                // only checked combat. A cop editorialising about your paintwork instead of
                // arresting you is funny exactly once and wrong every time after.
                //
                // 6 is COP, 27 SWAT, 29 ARMY -- the three that have a job to do about this.
                var kind = Function.Call<int>(Hash.GET_PED_TYPE, ped.Handle);
                if (kind == 6 || kind == 27 || kind == 29) return false;

                int last;
                if (_said.TryGetValue(ped.Handle, out last) && now - last < SamePersonMs) return false;

                // Has to be able to see him.
                // GetNearbyPeds already bounded the distance; this is only the sight line.
                // Shouting through a wall is the kind of detail nobody consciously notices
                // and everybody feels.
                return Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY,
                                           ped.Handle, me.Handle, 17);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Hands the street back.</summary>
        public void Release()
        {
            if (!_quieted) return;

            _quieted = false;

            // A HALF-FINISHED RUN GOES WITH IT. It holds Ped objects for a street that is
            // about to stop being looked at, and resuming one when the can comes back out
            // would test handles from wherever you were standing minutes ago.
            _run = null;
            _tried = 0;

            try
            {
                Function.Call(Hash.SET_IGNORE_LOW_PRIORITY_SHOCKING_EVENTS, Game.Player.Handle, false);
            }
            catch
            {
                // Teardown.
            }

            // The record of who has already grumbled goes too. It is only there to stop one
            // man repeating himself inside a single session with the can out, and holding
            // handles for peds the game has long since unloaded is a leak with no upside.
            _said.Clear();
        }
    }
}
