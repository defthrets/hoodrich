using System;
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
        private static readonly Vector3 House = new Vector3(-14.3f, -1438.4f, 31.1f);

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
            "ig_denise", "csb_denise", "cs_denise",
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
                ClearHousehold();
            }

            var here = AtDoor;
            if (here == _inside) return;

            _inside = here;

            // Told once on the way in, rather than a marker on the floor. It is a house, not a
            // pickup: standing in the right two metres should not be part of using it.
            if (_inside)
            {
                Notify.Ticker("~g~You're at the spot.~s~ Open your inventory to move work in or out.");
            }
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
        /// Takes Denise off the couch.
        ///
        /// She is not ours -- she is placed by the game, or by Open All Interiors -- so this is
        /// a deletion rather than anything we can politely undo. It is deliberate: the couch is
        /// a vanilla activity spot, and a ped sat in it is what stops you sitting down, putting
        /// the television on and rolling something. The house is meant to be a place you live
        /// in, and you cannot live in a room somebody else is permanently occupying.
        ///
        /// The game repopulates the interior on its own when the area next streams back in, so
        /// this holds only while the mod is loaded.
        /// </summary>
        private void ClearHousehold()
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

                    Settle(ped);
                    _settled = ped;

                    if (!_saidCouchIsFree)
                    {
                        _saidCouchIsFree = true;
                        Log.Info("Denise settled in the front room.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not clear the couch: " + ex.Message);
            }
        }

        /// <summary>
        /// Leaves her where she is and stops her doing anything.
        ///
        /// She used to be deleted outright -- hidden, decollided, claimed and removed on every
        /// sweep -- because a woman wandering round the room while you are counting product on
        /// her worktop is in the way. Taking the house's own occupant out of the house to fix
        /// that is too blunt: it is her place, Franklin lives there, and an empty front room
        /// reads as a bug rather than as a choice.
        ///
        /// So she stays and goes quiet. Frozen where she stands, deaf to everything happening
        /// around her, and with her ambient chatter stopped -- she is furniture that happens
        /// to be his aunt.
        ///
        /// Idempotent, because the sweep runs four times a second and will keep finding her.
        /// Every call here is a set rather than a toggle, so re-running it costs nothing and
        /// changes nothing.
        /// </summary>
        private static void Settle(Ped ped)
        {
            try
            {
                // Ours, so the population system does not recycle her mid-sentence.
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, ped.Handle, true, true);
                ped.IsPersistent = true;

                ped.IsVisible = true;
                Function.Call(Hash.SET_ENTITY_COLLISION, ped.Handle, true, true);

                // Still. The freeze is what stops her walking into the kitchen while you are
                // stood at the counter, which is the whole reason she was removed.
                Function.Call(Hash.FREEZE_ENTITY_POSITION, ped.Handle, true);

                // And quiet. Blocking non-temporary events stops her reacting to gunfire, to
                // the player, to anything -- which is most of what makes an ambient ped talk.
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, true);
                Function.Call(Hash.STOP_CURRENT_PLAYING_AMBIENT_SPEECH, ped.Handle);
                Function.Call(Hash.SET_PED_CAN_PLAY_AMBIENT_ANIMS, ped.Handle, false);
                Function.Call(Hash.DISABLE_PED_PAIN_AUDIO, ped.Handle, true);

                // Not a target. She is in a house you fire a lot of rounds near.
                Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, ped.Handle, false);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not settle the household: " + ex.Message);
            }
        }

        /// <summary>
        /// The one we froze, so she can be let go on unload.
        ///
        /// A frozen ped with non-temporary events blocked stays that way after the script has
        /// gone, and there is nothing left running to undo it -- a woman standing rigid in her
        /// own front room for the rest of the save, which is a worse state than the one this
        /// replaced.
        /// </summary>
        private Ped _settled;

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

            // Let her go. Frozen and deaf is fine while the mod is running and looking after
            // her; it is not something to leave behind on a save.
            try
            {
                if (_settled != null && _settled.Exists())
                {
                    Function.Call(Hash.FREEZE_ENTITY_POSITION, _settled.Handle, false);
                    Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, _settled.Handle, false);
                    Function.Call(Hash.SET_PED_CAN_PLAY_AMBIENT_ANIMS, _settled.Handle, true);
                    Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, _settled.Handle, true);
                    _settled.MarkAsNoLongerNeeded();
                }
            }
            catch { /* teardown */ }

            _settled = null;
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
