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
        private const int DictTimeoutMs = 1200;

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
                ["weed"] = new[]
                {
                    new Clip("anim@amb@business@weed@weed_sorting_seated@", "sorting_base_inspector"),
                    new Clip("anim@amb@business@weed@weed_inspecting_lo_med_hi@", "weed_inspecting_hi_base_inspector"),
                    new Clip("anim@amb@business@coc@coc_packing_hi@", "full_cycle_v1_packer")
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

        public bool IsPlaying => _playingDict != null;

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
                    Function.Call(Hash.STOP_ANIM_TASK, player.Handle, _playingDict, "", 3f);
                }

                Function.Call(Hash.REMOVE_ANIM_DICT, _playingDict);
            }
            catch
            {
                // Teardown.
            }

            _playingDict = null;
        }
    }
}
