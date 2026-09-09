using System;
using System.Collections.Generic;
using GTA;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Economy
{
    /// <summary>
    /// The player actually working the product, rather than standing still with a progress bar.
    ///
    /// Each drug gets its own hands: weed is bagged up, powder is cut and packed, meth is
    /// broken down, pills are counted out. The clips are the game's own business-property
    /// animations, and because clip names vary between game versions and editions, each drug
    /// carries a list of candidates -- the first one that both loads AND is actually seen
    /// playing wins, and if none of them do the scenario fallback still reads as work.
    /// </summary>
    internal sealed class PrepAnimation
    {

        /// <summary>Loop forever; the caller stops it when the batch finishes.</summary>
        private const int LoopFlag = 1;

        private sealed class Clip
        {
            public readonly string Dict;
            public readonly string Name;

            public Clip(string dict, string name)
            {
                Dict = dict;
                Name = name;
            }
        }

        /// <summary>
        /// THE GAME ALREADY HAS THIS ANIMATION AND IT IS BETTER THAN ANYTHING BELOW.
        ///
        /// WORLD_HUMAN_DRUG_PROCESSORS_WEED and _COKE are two ambient scenarios Rockstar
        /// authored for exactly this: somebody stood at a table with the product in front of
        /// them, weighing, bagging, cutting. Everything else in this file is a warehouse clip
        /// or a maid clip borrowed because it has the right SHAPE -- hands busy at a worktop --
        /// and each one is a near miss. These are not near misses. They are the thing.
        ///
        /// A SET, NOT A CLIP, which is the other half of why they are better. Each has a base
        /// loop and a handful of idles, and the scenario cycles between them: he works, he
        /// pauses to check a bag, he goes back to it. A single clip on loop is a pose; this is
        /// a person. See Tick.
        ///
        /// THE COKE ONES ARE AUTHORED FEMALE AND THE WEED ONES MALE, which is Rockstar's
        /// business and not a choice here -- there is no male coke variant to pick. They play
        /// on any ped because the skeleton is the same. A very close look at the powder set on
        /// Franklin shows its origins in the shoulders; nobody has ever noticed.
        ///
        /// female_b is the interesting one: its idles have BAKING SODA variants, which is
        /// literally what crack is cut with, so that is the set crack gets.
        ///
        /// Every dictionary and clip name here was taken out of the game's own dump rather
        /// than typed -- see the reference lists. A wrong name fails silently.
        /// </summary>
        private sealed class Bench
        {
            public readonly string Loop;
            public readonly string Idles;
            public readonly string[] Idle;
            public readonly string Scenario;

            public Bench(string root, string[] idle, string scenario)
            {
                Loop = root + "@base";
                Idles = root + "@idles";
                Idle = idle;
                Scenario = scenario;
            }
        }

        private static readonly Bench Weed = new Bench(
            "anim@amb@drug_processors@weed@male_a",
            new[] { "idle_a", "idle_b", "idle_c", "idle_d", "idle_e", "idle_f", "idle_g" },
            "WORLD_HUMAN_DRUG_PROCESSORS_WEED");

        private static readonly Bench Powder = new Bench(
            "anim@amb@drug_processors@coke@female_a",
            new[] { "idle_a", "idle_b", "idle_c", "idle_d" },
            "WORLD_HUMAN_DRUG_PROCESSORS_COKE");

        private static readonly Bench Soda = new Bench(
            "anim@amb@drug_processors@coke@female_b",
            new[] { "idle_a_bakingsoda", "idle_b_bakingsoda", "idle_c_bakingsoda", "idle_d_bakingsoda" },
            "WORLD_HUMAN_DRUG_PROCESSORS_COKE");

        /// <summary>
        /// Which bench a drug is worked on: the weed one for weed, the coke one for everything
        /// else, and the baking-soda one for crack.
        ///
        /// EVERYTHING ELSE, INCLUDING METH, and that is deliberate. Meth has its own clips
        /// further down this file and they are good ones -- but they are a man at a chemistry
        /// rig, and this is a kitchen counter in a house. The right animation for the wrong
        /// room loses to the near-right animation for the right one.
        /// </summary>
        private static Bench BenchFor(string drugId)
        {
            if (string.IsNullOrEmpty(drugId)) return Powder;

            if (drugId.Equals("weed", StringComparison.OrdinalIgnoreCase)) return Weed;
            if (drugId.Equals("crack", StringComparison.OrdinalIgnoreCase)) return Soda;

            return Powder;
        }

        /// <summary>The scenario that IS this drug's bench, for the last-resort fallback.</summary>
        public static string ScenarioFor(string drugId)
        {
            return BenchFor(drugId).Scenario;
        }

        /// <summary>
        /// How long he works before an idle, and how long an idle runs.
        ///
        /// The idles are three to six second cycles and are looped for the window rather than
        /// played once, because a clip played once ends and leaves him with no task at all --
        /// and "no task" on a tick that checks whether he is working restarts the whole thing.
        /// Looped and then blended back to the base loop, the join is a person changing what
        /// their hands are doing.
        /// </summary>
        private const int WorkMinMs = 7000;
        private const int WorkMaxMs = 15000;
        private const int IdleMinMs = 4000;
        private const int IdleMaxMs = 7000;

        /// <summary>
        /// Softer than the rest of this file's 4.0, because these transitions are seen.
        ///
        /// Every other clip here is tasked once at the start of a batch, where a fast blend out
        /// of standing still is right. The bench changes clip every ten seconds in front of
        /// you, and at 4.0 each change is a snap.
        /// </summary>
        private const float BenchBlend = 2f;

        private static readonly Random Rng = new Random();

        private Bench _bench;
        private bool _onIdle;
        private int _turnAt;

        /// <summary>
        /// How long after tasking a clip the player counts as working regardless.
        ///
        /// IS_ENTITY_PLAYING_ANIM IS FALSE ON THE FRAME YOU TASK IT and stays false through the
        /// blend, which this file has already been bitten by once -- see TryPlay. The caller
        /// restarts the animation whenever IsPlaying says no, so without this every hand-over
        /// from base to idle is a window where the batch decides nothing is running and starts
        /// the whole thing again from the top.
        /// </summary>
        private const int SettleMs = 500;
        private int _settled;

        /// <summary>
        /// The house animation for working a counter, tried before anything drug-specific.
        ///
        /// timetable@maid@ig_2@ is somebody stood at a worktop with both hands busy in front
        /// of them, which is what bagging up actually looks like -- the business clips
        /// underneath are packing crates and inspecting trays, authored for a warehouse rather
        /// than for Aunt Denise's kitchen.
        ///
        /// Several clip names, and that is deliberate rather than indecisive. A clip that is
        /// not in a dictionary fails SILENTLY -- the task is accepted and nothing moves -- so
        /// the only way to find the one this install has is to try them and check. TryPlay
        /// does check, and steps to the next on a miss, so an unlucky name costs a frame
        /// rather than the animation.
        /// </summary>
        private static readonly Clip[] Counter =
        {
            // EVERY PAIR IN HERE USED TO NAME A CLIP THAT DOES NOT EXIST.
            //
            // Twenty-five of them, checked against the game's own dump of every dictionary and
            // every clip in the build. The dictionaries were mostly real; the clip names were
            // not, so TASK_PLAY_ANIM was accepted and nothing moved -- for every drug, every
            // time, silently, which is exactly the failure mode the comment above warns about
            // and exactly the one nobody notices.
            //
            // The naming rule, once you can see the data: a clip in these ambient business
            // dictionaries is <action>_<track>, and the TRACK is usually a prop. The human is
            // one specific track name per dictionary, and it is never "packer":
            //
            //   coc_packing / coc_packing_hi     pressoperator
            //   coc_unpack_cut                   cokecutter, cokepacker
            //   meth_smash_weight_check          char01, char02
            //   meth_monitoring_cooking@cooking  cooker
            //   weed_sorting_seated              sorter01, sorter02
            //   weed_inspecting_*                inspector
            //
            // Every pair below was verified against that dump before it was written here.
            // Cutting product at a table. The most on-the-nose clip in the game for this.
            new Clip("anim@amb@business@coc@coc_unpack_cut@", "fullcut_cycle_v1_cokecutter"),
            new Clip("anim@amb@business@coc@coc_unpack_cut@", "fullcut_cycle_v2_cokecutter"),

            // Pressing and packing it once it is cut.
            new Clip("anim@amb@business@coc@coc_packing_hi@", "full_cycle_v1_pressoperator"),
            new Clip("anim@amb@business@coc@coc_packing@", "base_pressoperator"),

            // Breaking it up and weighing it out.
            new Clip("anim@amb@business@meth@meth_smash_weight_check@", "break_weigh_char01"),

            // Seated, hands in it. Lower down because the counter is a worktop you stand at.
            new Clip("anim@amb@business@weed@weed_sorting_seated@", "base_sorter_left_sorter01"),

            // Bench work, which is what this opened with before the business clips existed.
            new Clip("anim@amb@clubhouse@tutorial@bkr_tut_ig3@", "machinic_loop_mechandplayer"),
            new Clip("amb@world_human_hammering@male@base", "base"),

            // Somebody working at a kitchen surface. Every clip in this dict is prefixed ig_2_.
            new Clip("timetable@maid@ig_2@", "ig_2_base"),
            new Clip("timetable@maid@ig_2@", "ig_2_idle_a"),
            new Clip("timetable@maid@ig_2@", "ig_2_idle_b"),
            new Clip("timetable@maid@ig_2@", "ig_2_idle_c"),

            // Floyd cleaning his kitchen -- same shape of action, same shape of room.
            new Clip("timetable@floyd@clean_kitchen@base", "base")
        };

        /// <summary>
        /// Tried in order per drug, once the counter clips have had their go. Falls through to
        /// the shared list, so a drug with no entry still gets hands rather than nothing.
        /// </summary>
        private static readonly Dictionary<string, Clip[]> ByDrug =
            new Dictionary<string, Clip[]>(StringComparer.OrdinalIgnoreCase)
            {
                // Standing at a surface, both hands on the product. The seated sorting clip
                // that used to lead this list is exactly that -- seated -- and the counter in
                // Denise's kitchen is a worktop you stand at, so it read as a man crouched at
                // nothing.
                ["weed"] = new[]
                {
                    new Clip("anim@amb@business@weed@weed_inspecting_lo_med_hi@", "weed_stand_base_inspector"),
                    new Clip("anim@amb@business@weed@weed_inspecting_high_dry@", "weed_inspecting_high_base_inspector"),
                    new Clip("anim@amb@business@weed@weed_sorting_seated@", "base_sorter_left_sorter01")
                },
                ["coke"] = new[]
                {
                    new Clip("anim@amb@business@coc@coc_unpack_cut@", "fullcut_cycle_v1_cokecutter"),
                    new Clip("anim@amb@business@coc@coc_unpack_cut@", "fullcut_cycle_v1_cokepacker"),
                    new Clip("anim@amb@business@coc@coc_packing_hi@", "full_cycle_v1_pressoperator")
                },
                ["crack"] = new[]
                {
                    new Clip("anim@amb@business@coc@coc_unpack_cut@", "fullcut_cycle_v2_cokecutter"),
                    new Clip("anim@amb@business@meth@meth_monitoring_cooking@cooking@", "chemical_pour_long_cooker")
                },
                ["meth"] = new[]
                {
                    new Clip("anim@amb@business@meth@meth_monitoring_cooking@cooking@", "base_idle_tank_cooker"),
                    new Clip("anim@amb@business@meth@meth_smash_weight_check@", "break_weigh_char01"),
                    new Clip("anim@amb@business@meth@meth_monitoring_cooking@monitoring@", "base_idle_guage_monitor")
                },
                ["heroin"] = new[]
                {
                    new Clip("anim@amb@business@coc@coc_unpack_cut@", "fullcut_cycle_v1_cokecutter"),
                    new Clip("anim@amb@business@coc@coc_packing_hi@", "full_cycle_v1_pressoperator")
                },
                ["ecstasy"] = new[]
                {
                    new Clip("anim@amb@business@meth@meth_smash_weight_check@", "break_weigh_char01"),
                    new Clip("anim@amb@business@coc@coc_packing_hi@", "full_cycle_v1_pressoperator")
                }
            };

        /// <summary>
        /// Anything without its own hands ends up here.
        ///
        /// All of these are people working at a surface with both hands, because that is what
        /// this is: standing at a counter over the product. The old fallback had the player
        /// rummaging in a bin, which reads as looking for something rather than making it.
        /// </summary>
        private static readonly Clip[] Fallback =
        {
            new Clip("anim@amb@business@coc@coc_packing_hi@", "full_cycle_v1_pressoperator"),
            new Clip("timetable@floyd@clean_kitchen@base", "base"),
            new Clip("anim@heists@prison_heiststation@cop_reactions", "cop_a_idle")
        };

        private string _playingDict;
        private string _playingClip;

        /// <summary>
        /// Whether the player is VISIBLY working, not merely whether a task was handed out.
        ///
        /// This used to be "did we set the field", which is a different question. Anything that
        /// clears the ped's tasks -- a stumble, a scenario, another script -- left the field set
        /// and the player stood to attention over a counter for the rest of the batch, with
        /// nothing to notice it had stopped.
        /// </summary>
        public bool IsPlaying
        {
            get
            {
                if (_playingDict == null) return false;

                // Still blending in. See SettleMs.
                if (Game.GameTime < _settled) return true;

                try
                {
                    var player = Game.Player.Character;
                    if (player == null || !player.Exists()) return false;

                    return Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM, player.Handle,
                                               _playingDict, _playingClip, 3);
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Asks for every clip a drug might use, ahead of needing them.
        ///
        /// REQUEST_ANIM_DICT is asynchronous, so the first attempt at a cold dictionary always
        /// loses -- and a batch that starts before its animation has streamed in is a batch
        /// that spends its first seconds with the player stood still doing nothing.
        /// </summary>
        public static void Preload(string drugId)
        {
            try
            {
                // No drug named means the player is stood at the counter with the menu not yet
                // open, so there is nothing to narrow it to -- warm all of them.
                if (string.IsNullOrEmpty(drugId))
                {
                    foreach (var set in ByDrug.Values)
                    {
                        foreach (var clip in set) Function.Call(Hash.REQUEST_ANIM_DICT, clip.Dict);
                    }
                }
                else if (ByDrug.TryGetValue(drugId, out var clips))
                {
                    foreach (var clip in clips) Function.Call(Hash.REQUEST_ANIM_DICT, clip.Dict);
                }

                // The benches first, because they are what is going to be used. All three,
                // since the menu is open and the drug is not settled until it is picked.
                foreach (var bench in new[] { Weed, Powder, Soda })
                {
                    Function.Call(Hash.REQUEST_ANIM_DICT, bench.Loop);
                    Function.Call(Hash.REQUEST_ANIM_DICT, bench.Idles);
                }

                foreach (var clip in Counter) Function.Call(Hash.REQUEST_ANIM_DICT, clip.Dict);
                foreach (var clip in Fallback) Function.Call(Hash.REQUEST_ANIM_DICT, clip.Dict);
            }
            catch
            {
                // Try again next tick.
            }
        }

        /// <summary>Starts the right animation for a drug. True if any clip took.</summary>
        public bool Start(Ped player, string drugId)
        {
            Stop(player);

            if (player == null || !player.Exists()) return false;

            if (!ByDrug.TryGetValue(drugId ?? "", out var clips)) clips = Fallback;

            // THE BENCH FIRST, ahead of everything, because it is the animation this scene is
            // actually of rather than one borrowed for its shape. Everything below is what
            // happens on an install that has not got it.
            var bench = BenchFor(drugId);

            if (bench != null && Play(player, bench.Loop, "base", LoopFlag, BenchBlend))
            {
                _bench = bench;
                _onIdle = false;
                _turnAt = Game.GameTime + WorkMinMs + Rng.Next(WorkMaxMs - WorkMinMs);
                return true;
            }

            _bench = null;

            // The counter animation next, whatever is being worked. Cutting is one action in
            // one room, so it should look like one action -- the per-drug clips below are what
            // happens if this install has not got it.
            foreach (var clip in Counter)
            {
                if (TryPlay(player, clip)) return true;
            }

            foreach (var clip in clips)
            {
                if (TryPlay(player, clip)) return true;
            }

            // A drug-specific clip that does not exist on this install should not cost the
            // player their animation entirely.
            foreach (var clip in Fallback)
            {
                if (TryPlay(player, clip)) return true;
            }

            return false;
        }

        /// <summary>
        /// Works the bench: back and forth between the loop and an idle, on its own clock.
        ///
        /// Called every tick of a batch. Does nothing at all unless a bench is what took, which
        /// means an install without those dictionaries carries no cost for this existing.
        /// </summary>
        public void Tick(Ped player)
        {
            if (_bench == null || player == null || !player.Exists()) return;

            var now = Game.GameTime;
            if (now < _turnAt) return;

            if (_onIdle)
            {
                // Back to work.
                if (!Play(player, _bench.Loop, "base", LoopFlag, BenchBlend)) { _turnAt = now + 1500; return; }

                _onIdle = false;
                _turnAt = now + WorkMinMs + Rng.Next(WorkMaxMs - WorkMinMs);
                return;
            }

            if (_bench.Idle.Length == 0) { _turnAt = now + WorkMaxMs; return; }

            var pick = _bench.Idle[Rng.Next(_bench.Idle.Length)];

            if (!Play(player, _bench.Idles, pick, LoopFlag, BenchBlend)) { _turnAt = now + 1500; return; }

            _onIdle = true;
            _turnAt = now + IdleMinMs + Rng.Next(IdleMaxMs - IdleMinMs);
        }

        private bool TryPlay(Ped player, Clip clip)
        {
            try
            {
                Function.Call(Hash.REQUEST_ANIM_DICT, clip.Dict);

                // Streaming is asynchronous and blocking the tick to wait for it would stutter
                // the game, so an unloaded dictionary is simply not ready yet. The caller
                // retries every frame and a batch lasts seconds, so it gets there.
                if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, clip.Dict)) return false;

                // The last three are NOT position locks, whatever the community header says.
                // They are bPhaseControlled, IkFlags and bAllowOverrideCloneUpdate, confirmed
                // against ScriptHookVDotNet's own source. Passing true, true, true asked for a
                // clip whose phase is driven externally by nobody -- it sits on frame zero --
                // with IkFlags coerced to 1, which is AIK_DISABLE_LEG_IK and stops the feet
                // planting to the floor. false, 0, false is what every shipped resource uses.
                Function.Call(Hash.TASK_PLAY_ANIM, player.Handle, clip.Dict, clip.Name,
                              4f, -4f, -1, LoopFlag, 0f, false, 0, false);

                _settled = Game.GameTime + SettleMs;

                // AND THAT IS THE END OF IT. It used to ask, on this very line, whether the ped
                // was now playing the clip -- and take a "no" as proof the name was wrong.
                //
                // IS_ENTITY_PLAYING_ANIM IS ALWAYS FALSE ON THE FRAME YOU TASK IT. The task
                // takes at least a frame to start and longer with a blend-in, so every
                // candidate "failed", the loop walked the entire list re-tasking the ped on
                // each one, and what you actually saw was whichever clip happened to be LAST
                // -- the final fallback -- being re-issued several times a second, forever.
                //
                // Which is why the cutting animation was a man standing about: not a wrong
                // name, a test that could not pass.
                //
                // The test is gone rather than deferred, because it was only ever standing in
                // for "does this clip exist", and every pair in this file is now checked
                // against the game's own dictionary dump before it is written. That check
                // belongs at build time, not thirty times a second at runtime.
                _playingDict = clip.Dict;
                _playingClip = clip.Name;

                Log.Debug("Prep animation: " + clip.Dict + " / " + clip.Name + " took.");
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Prep animation '" + clip.Dict + "/" + clip.Name + "' failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Tasks one clip by name, and says whether it went out.
        ///
        /// The same call TryPlay makes, without the catalogue around it -- the bench knows its
        /// own names and does not walk a ladder of candidates.
        /// </summary>
        private bool Play(Ped player, string dict, string clip, int flag, float blend)
        {
            try
            {
                Function.Call(Hash.REQUEST_ANIM_DICT, dict);

                if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dict)) return false;

                Function.Call(Hash.TASK_PLAY_ANIM, player.Handle, dict, clip,
                              blend, -blend, -1, flag, 0f, false, 0, false);

                _playingDict = dict;
                _playingClip = clip;
                _settled = Game.GameTime + SettleMs;

                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Prep animation '" + dict + "/" + clip + "' failed: " + ex.Message);
                return false;
            }
        }

        public void Stop(Ped player)
        {
            _bench = null;
            _onIdle = false;

            if (_playingDict == null) return;

            try
            {
                if (player != null && player.Exists())
                {
                    Function.Call(Hash.STOP_ANIM_TASK, player.Handle, _playingDict,
                                  _playingClip ?? "", 3f);
                }

                // NOT removed from memory.
                //
                // This used to call REMOVE_ANIM_DICT here, which throws away the dictionary the
                // very next batch is about to ask for -- so every batch restarted the streaming
                // race from cold, and Start is called again on any tick the animation is not
                // running. Two seconds of a man standing still at the start of every single
                // batch, for the sake of freeing an animation that was immediately needed again.
            }
            catch
            {
                // Teardown.
            }

            _playingDict = null;
            _playingClip = null;
        }
    }
}
