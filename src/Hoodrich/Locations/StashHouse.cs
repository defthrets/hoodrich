using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Economy;
using Hoodrich.UI;

namespace Hoodrich.Locations
{
    /// <summary>
    /// Aunt Denise's place on Forum Drive: the one house you keep product in.
    ///
    /// Property used to be something you shopped for, block by block, at a price scaled by how
    /// developed the turf was. That was a second economy bolted onto a game about a corner, and
    /// it never earned its keep. There is one stash house instead, it is the house the story
    /// already gives you, and it is yours from the start -- so the only decision left is how
    /// much you carry versus how much you leave at home.
    /// </summary>
    internal sealed class StashHouse
    {
        /// <summary>Aunt Denise's, Forum Drive, Davis.</summary>
        /// <summary>
        /// Franklin's aunt's, on Forum Drive.
        ///
        /// Internal rather than private now, because somebody else has a use for it: a patrol
        /// car that sometimes pulls up outside a particular door and sits there with the light
        /// on wants to know which door, and this is the one address in the mod that is his.
        /// </summary>
        internal static readonly Vector3 House = new Vector3(-14.3f, -1438.4f, 31.1f);

        /// <summary>
        /// Anywhere in or around the house counts.
        ///
        /// A two-metre door point meant standing INSIDE put you out of range, because the
        /// interior sits several metres off the doorstep. A radius covers the yard, the porch
        /// and every room without needing to know where the game hides the interior.
        /// </summary>
        private const float UseRange = 14f;

        /// <summary>How close somebody has to be for their shouting to be our problem.</summary>
        private const float QuietRange = 22f;

        /// <summary>Who actually lives here. Nobody else gets touched.</summary>
        private static readonly string[] HouseholdModels =
        {
            "ig_denise", "cs_denise", "cs_denise",
        };


        private Blip _blip;

        private readonly float _capacity;

        public StashHouse(Settings cfg)
        {
            _capacity = Math.Max(1f, cfg.HideoutStashCapacity);
            Stash = new Stash { Capacity = _capacity };
        }

        /// <summary>What is being kept here.</summary>
        public Stash Stash { get; }

        public Vector3 Position => House;

        public string Name => "Aunt Denise's";

        /// <summary>True when the player is close enough to move product in or out.</summary>
        public bool AtDoor => DistanceTo() <= UseRange;

        public float DistanceTo()
        {
            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return 9999f;

            return player.Position.DistanceTo(House);
        }

        /// <summary>True on the frame the player crosses into the house.</summary>
        private bool _inside;

        /// <summary>Four times a second. Neither job is one you can catch happening.</summary>
        private const int SweepIntervalMs = 250;

        private int _lastSweep;

        public void Update()
        {
            EnsureBlip();

            // Hush and the household sweep are both gated on being at the door, and both do a
            // full ped sweep -- so standing in your own kitchen ran two world scans every
            // frame. Four times a second is plenty for silencing a conversation and removing
            // an aunt; neither is something you can catch happening.
            var now = Game.GameTime;
            if (now - _lastSweep >= SweepIntervalMs)
            {
                _lastSweep = now;

                Hush();
                QuietenHousehold();
            }

            TickCutHint();

            var here = AtDoor;
            if (here == _inside) return;

            _inside = here;

            // Told once on the way in, rather than a marker on the floor. It is a house, not a
            // pickup: standing in the right two metres should not be part of using it.
            if (_inside)
            {
                Notify.Ticker("~g~You're at the spot.~s~ Open your inventory to move work in or out.");
                Notify.Ticker("~g~Or text a plug from your contacts~s~ and have it brought here.");

                // The third one comes LATER, on purpose.
                //
                // Three tickers fired on the same frame are one block of text, and the eye
                // reads the first line of a block. Held back until the other two have been and
                // gone, this one arrives on its own with nothing to compete with -- which is
                // what it needs, because it is the step everybody misses: weight that has not
                // been through the kitchen cannot be sold, and nothing else in the house says
                // so.
                _sayCutAt = Game.GameTime + CutHintDelayMs;
            }
            else
            {
                _sayCutAt = 0;
            }
        }

        /// <summary>When to mention the kitchen, or 0 for not pending.</summary>
        private int _sayCutAt;

        /// <summary>Long enough for the first two to have cleared the screen.</summary>
        private const int CutHintDelayMs = 7000;

        /// <summary>
        /// The queued line about cutting, once the others are out of the way.
        ///
        /// Dropped rather than delayed again if you have already left -- a hint about the
        /// kitchen arriving while you are three streets away is worse than no hint.
        /// </summary>
        private void TickCutHint()
        {
            if (_sayCutAt == 0) return;
            if (Game.GameTime < _sayCutAt) return;

            _sayCutAt = 0;

            if (!_inside) return;

            Notify.Ticker("~o~Weight has to be cut before it will sell.~s~ " +
                          "The kitchen is in here -- take bulk to it and bag it up.");
        }

        private void EnsureBlip()
        {
            if (_blip != null && _blip.Exists()) return;

            try
            {
                _blip = World.CreateBlip(House);
                if (_blip == null || !_blip.Exists()) return;

                _blip.Sprite = BlipSprite.Safehouse;
                _blip.Color = BlipColor.Green;
                _blip.Name = "Stash house";
                _blip.IsShortRange = true;
                _blip.Scale = 0.85f;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not blip the stash house: " + ex.Message);
            }
        }

        /// <summary>
        /// Keeps the house quiet.
        ///
        /// Denise shouting through the door and Franklin answering her is not ambient chatter,
        /// it is a scripted CONVERSATION -- the game still thinks he lives here, because the
        /// house is only reachable at all thanks to Open All Interiors. So stopping ambient
        /// speech was never going to touch it: the conversation has to be stopped as well, and
        /// the player's own line with it.
        /// </summary>
        private void Hush()
        {
            if (!AtDoor) return;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            try
            {
                // The scripted exchange, which is the actual offender.
                if (Function.Call<bool>(Hash.IS_SCRIPTED_CONVERSATION_ONGOING))
                {
                    Function.Call(Hash.STOP_SCRIPTED_CONVERSATION, false);
                }

                Function.Call(Hash.STOP_CURRENT_PLAYING_SPEECH, player.Handle);
                Function.Call(Hash.STOP_CURRENT_PLAYING_AMBIENT_SPEECH, player.Handle);

                // The household is removed outright now rather than quietened, so this is only
                // here for the frame or two before that lands. It used to sweep every ped within
                // 22 m and blank their ambient voice, which is permanent and irreversible --
                // anybody who had ever walked past the house was mute for the rest of the
                // session, our own leaders and homies included.
                foreach (var ped in World.GetNearbyPeds(player, QuietRange))
                {
                    if (ped == null || !ped.Exists() || ped.Handle == player.Handle) continue;
                    if (!IsHousehold(ped)) continue;

                    Function.Call(Hash.STOP_CURRENT_PLAYING_SPEECH, ped.Handle);
                    Function.Call(Hash.STOP_CURRENT_PLAYING_AMBIENT_SPEECH, ped.Handle);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not quieten the house: " + ex.Message);
            }
        }

        /// <summary>
        /// Leaves Denise where the game put her, and stops her shouting.
        ///
        /// She used to be deleted and replaced -- first by her own model, then by a stand-in,
        /// then by one that walked round the house. Every version of that was solving a problem
        /// nobody had: the couch is hers, she has sat on it since 2013, and a mod that removes
        /// somebody's aunt from her own front room to free up an animation is a mod doing too
        /// much.
        ///
        /// What actually needed fixing was the noise. She shouts through the door and Franklin
        /// answers her, which is a story scene playing on a loop in a house you are using as a
        /// warehouse, and that is what the block below turns off.
        ///
        /// A STANDING JOB rather than a one-off, because the game repopulates the interior
        /// whenever it streams back in, and the woman who comes back is not the one that was
        /// quietened. Each is done once and remembered by handle, so this is a cheap check that
        /// finds nothing almost every time it runs.
        /// </summary>
        private void QuietenHousehold()
        {
            if (!AtDoor) return;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            try
            {
                foreach (var ped in World.GetNearbyPeds(player, QuietRange))
                {
                    if (ped == null || !ped.Exists() || ped.Handle == player.Handle) continue;
                    if (!IsHousehold(ped)) continue;
                    if (_hushed.Contains(ped.Handle)) continue;

                    Settle(ped);

                    _hushed.Add(ped.Handle);
                    _quiet.Add(ped);

                    if (!_saidCouchIsFree)
                    {
                        _saidCouchIsFree = true;
                        Log.Info("Denise is where she always was, with the sound off.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not quieten the household: " + ex.Message);
            }
        }

        /// <summary>
        /// The mute itself. Not a freeze, not a deletion -- she keeps her couch and her scene.
        ///
        /// Blocking non-temporary events is what stops her reacting to the player, to gunfire
        /// and to everything else, which is most of what makes an ambient ped talk. The rest
        /// covers the lines she starts on her own.
        /// </summary>
        private static void Settle(Ped ped)
        {
            try
            {
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, true);
                Function.Call(Hash.STOP_CURRENT_PLAYING_AMBIENT_SPEECH, ped.Handle);
                Function.Call(Hash.SET_PED_CAN_PLAY_AMBIENT_ANIMS, ped.Handle, false);
                Function.Call(Hash.DISABLE_PED_PAIN_AUDIO, ped.Handle, true);

                // Not a target either. She is in a house you fire a lot of rounds near, and
                // she is not ours to get killed.
                Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, ped.Handle, false);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not settle the household: " + ex.Message);
            }
        }

        /// <summary>Who has already been quietened, so it is done once each.</summary>
        private readonly HashSet<int> _hushed = new HashSet<int>();
        private readonly List<Ped> _quiet = new List<Ped>();

        private bool _saidCouchIsFree;

        /// <summary>True for the household models -- Denise and Franklin's own kin.</summary>
        private static bool IsHousehold(Ped ped)
        {
            try
            {
                var model = (uint)ped.Model.Hash;

                foreach (var name in HouseholdModels)
                {
                    if (model == (uint)Function.Call<int>(Hash.GET_HASH_KEY, name)) return true;
                }
            }
            catch
            {
                // A ped we cannot identify is somebody else's, so leave them alone.
            }

            return false;
        }

        /// <summary>Nothing is drawn at the house. It is a building, not a checkpoint.</summary>
        public void Draw()
        {
        }

        public void RestoreWorld()
        {
            try { if (_blip != null && _blip.Exists()) _blip.Delete(); }
            catch { /* teardown */ }

            _blip = null;

            // Everybody gets their voice back. Deaf is fine while the mod is running and
            // looking after them; it is not a thing to leave behind on somebody's save.
            //
            // Nobody is deleted here because nobody was created. She is the game's, she has
            // always been the game's, and the only thing this file ever did to her was turn the
            // sound down.
            foreach (var her in _quiet)
            {
                try
                {
                    if (her == null || !her.Exists()) continue;

                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, her.Handle, false);
                    Function.Call(Hash.SET_PED_CAN_PLAY_AMBIENT_ANIMS, her.Handle, true);
                    Function.Call(Hash.DISABLE_PED_PAIN_AUDIO, her.Handle, false);
                    Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, her.Handle, true);
                    her.MarkAsNoLongerNeeded();
                }
                catch { /* teardown */ }
            }

            _quiet.Clear();
            _hushed.Clear();
        }

        public Json ToJson() => Stash.ToJson();

        public void LoadFrom(Json node)
        {
            if (node == null) return;

            Stash.LoadFrom(node);

            // Said again after loading, deliberately. Saves written by older builds still carry
            // a capacity of their own, and this is the one place that decides how big the house
            // is -- the ini, on every load, whatever the file happens to remember.
            Stash.Capacity = _capacity;
        }
    }
}
