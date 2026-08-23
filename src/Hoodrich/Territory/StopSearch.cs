using System;
using GTA;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Territory
{
    /// <summary>
    /// The scene a stop-and-search actually is, shared by the two things that do one.
    ///
    /// There are two of them in this mod and they are the same event twice: a patrol car that
    /// rolls up because it saw you holding something, and an officer who walks over because you
    /// have been stood on one corner all afternoon. Both used to be a timer and a notification
    /// -- he arrives, three seconds pass, your pockets are empty -- which reads as a fine
    /// arriving in the post rather than as somebody going through them.
    ///
    /// Three beats, and none of them is a cutscene: your hands go up, he searches you, and then
    /// he leaves. You can walk away through any of it, which is the whole reason the scene is
    /// worth having -- standing still has to be a decision, and a decision needs something to
    /// watch.
    /// </summary>
    internal static class StopSearch
    {
        /// <summary>
        /// What a policeman going through somebody's pockets looks like.
        ///
        /// Tried in order and checked, because a clip name that is not in a dictionary fails
        /// SILENTLY -- the task is accepted and nothing moves. The last resort is a scenario
        /// rather than nothing: CODE_HUMAN_POLICE_INVESTIGATE is an officer examining something
        /// in front of him, which is the right shape even when it is not the right animation.
        /// </summary>
        private static readonly string[][] Frisking =
        {
            new[] { "mp_arresting", "a_uncuff" },
            new[] { "random@arrests", "generic_radio_chatter" },
            new[] { "amb@code_human_police_investigate@idle_a", "idle_a" },
            new[] { "amb@code_human_police_investigate@idle_b", "idle_b" }
        };

        /// <summary>
        /// Asks for the clips, early, and returns nothing.
        ///
        /// This has to be called when he STARTS coming over rather than when he arrives. A
        /// dictionary requested and checked in the same frame is a dictionary that has not
        /// loaded, which is how the DJ at the party ended up standing still for a fortnight --
        /// the walk across the street is the streaming budget, and it is plenty.
        /// </summary>
        public static void Want()
        {
            foreach (var clip in Frisking)
            {
                try { Function.Call(Hash.REQUEST_ANIM_DICT, clip[0]); }
                catch { /* it will not be the one that plays */ }
            }
        }

        /// <summary>
        /// Hands up, for as long as the search takes.
        ///
        /// On the PLAYER, which is unusual in this mod and is the point: you are the one being
        /// searched. It is a task rather than a lock -- move and it ends, which is exactly the
        /// behaviour wanted, because running is meant to stay available and meant to cost you.
        /// </summary>
        public static void HandsUp(Ped player, int ms)
        {
            if (player == null || !player.Exists()) return;

            try
            {
                Function.Call(Hash.TASK_HANDS_UP, player.Handle, Math.Max(500, ms), 0, -1, false);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not put his hands up: " + ex.Message);
            }
        }

        /// <summary>Him, going through your pockets.</summary>
        public static void Frisk(Ped cop)
        {
            if (cop == null || !cop.Exists() || !cop.IsAlive) return;

            foreach (var clip in Frisking)
            {
                try
                {
                    if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, clip[0])) continue;

                    // Locked on all three axes. These are authored inside a scene at a
                    // coordinate that is not the pavement he is stood on, and unlocked they
                    // drag him toward wherever the animation thinks the floor is.
                    Function.Call(Hash.TASK_PLAY_ANIM, cop.Handle, clip[0], clip[1],
                                  4f, -4f, -1, 1, 0f, true, true, true);

                    if (!Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM, cop.Handle,
                                             clip[0], clip[1], 3))
                    {
                        continue;
                    }

                    return;
                }
                catch { /* try the next one */ }
            }

            try
            {
                Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, cop.Handle,
                              "CODE_HUMAN_POLICE_INVESTIGATE", 0, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not start the search: " + ex.Message);
            }
        }

        /// <summary>
        /// And then he goes, in the car he came in if there is one.
        ///
        /// TASK_VEHICLE_DRIVE_WANDER given to a man stood beside his car walks him to it, puts
        /// him in it and drives it away -- one call for the whole thing, where a separate enter
        /// task followed by a drive task is the drive task cancelling the enter task.
        ///
        /// No car, no problem: he walks off. What he must not do is stand there. An officer
        /// left rooted on the pavement after taking your product is the same bug as a mission
        /// that never ends, and it is the last thing you see of the whole sequence.
        /// </summary>
        public static void SendOff(Ped cop)
        {
            if (cop == null || !cop.Exists() || !cop.IsAlive) return;

            try
            {
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, cop.Handle, false);
                Function.Call(Hash.CLEAR_PED_TASKS, cop.Handle);

                var car = cop.LastVehicle;

                if (car != null && car.Exists() &&
                    car.Position.DistanceTo(cop.Position) <= CarReach)
                {
                    Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, cop.Handle, car.Handle,
                                  17f, DrivingStyle);
                    return;
                }

                Function.Call(Hash.TASK_WANDER_STANDARD, cop.Handle, 10f, 10);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not send the officer on his way: " + ex.Message);
            }
        }

        /// <summary>How far he will walk back to his own car rather than set off on foot.</summary>
        private const float CarReach = 70f;

        /// <summary>Normal road driving. He is finished with you; he is not in a hurry.</summary>
        private const int DrivingStyle = 786603;
    }
}
