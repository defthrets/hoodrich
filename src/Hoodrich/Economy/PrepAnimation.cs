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
        /// Tried in order per drug. Falls through to the shared list, so a drug with no entry
        /// still gets hands rather than nothing.
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
                    new Clip("anim@amb@business@weed@weed_sorting_hi@", "sorting_base_inspector"),
                    new Clip("anim@amb@business@coc@coc_packing_hi@", "full_cycle_v1_packer"),
                    new Clip("anim@amb@business@weed@weed_inspecting_lo_med_hi@", "weed_inspecting_hi_base_inspector")
                },
                ["coke"] = new[]
                {
                    new Clip("anim@amb@business@coc@coc_packing_hi@", "full_cycle_v1_packer"),
                    new Clip("anim@amb@business@coc@coc_unpack_hi@", "full_cycle_v1_unpacker"),
                },
                ["crack"] = new[]
                {
                    new Clip("anim@amb@business@coc@coc_packing_hi@", "full_cycle_v1_packer"),
                    new Clip("anim@amb@business@meth@meth_monitoring_cooking@", "empty_bucket_base_cook"),
                },
                ["meth"] = new[]
                {
                    new Clip("anim@amb@business@meth@meth_monitoring_cooking@", "empty_bucket_base_cook"),
                    new Clip("anim@amb@business@meth@meth_drying_area@", "dry_meth_base_cook"),
                },
                ["heroin"] = new[]
                {
                    new Clip("anim@amb@business@coc@coc_packing_hi@", "full_cycle_v1_packer"),
                },
                ["ecstasy"] = new[]
                {
                    new Clip("anim@amb@business@coc@coc_packing_hi@", "full_cycle_v1_packer"),
                    new Clip("anim@heists@prison_heiststation@cop_reactions", "cop_a_idle"),
                },
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
            new Clip("anim@amb@business@coc@coc_packing_hi@", "full_cycle_v1_packer"),
            new Clip("amb@prop_human_bum_shopping_cart@male@base", "base"),
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

        private bool TryPlay(Ped player, Clip clip)
        {
            try
            {
                Function.Call(Hash.REQUEST_ANIM_DICT, clip.Dict);

                // Streaming is asynchronous and blocking the tick to wait for it would stutter
                // the game, so an unloaded dictionary is simply not ready yet. The caller
                // retries every frame and a batch lasts seconds, so it gets there.
                if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, clip.Dict)) return false;

                Function.Call(Hash.TASK_PLAY_ANIM, player.Handle, clip.Dict, clip.Name,
                              4f, -4f, -1, LoopFlag, 0f, false, false, false);

                // A clip name that is not in the dictionary fails silently, so the only honest
                // test is whether the ped is now visibly playing it.
                if (!Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM, player.Handle,
                                         clip.Dict, clip.Name, 3))
                {
                    return false;
                }

                _playingDict = clip.Dict;
                _playingClip = clip.Name;
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Prep animation '" + clip.Dict + "/" + clip.Name + "' failed: " + ex.Message);
                return false;
            }
        }

        public void Stop(Ped player)
        {
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
