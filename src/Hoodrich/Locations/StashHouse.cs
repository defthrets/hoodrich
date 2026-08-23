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
                KeepRoaming();
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
                // Whether ours is already stood in there, asked ONCE before the sweep rather
                // than discovered during it.
                //
                // This is the whole bug that put five of her in that front room. The old sweep
                // set a local flag when it happened to meet our stand-in -- and GetNearbyPeds
                // returns them in whatever order it likes, so meeting a fresh Denise FIRST made
                // a second stand-in, which orphaned the first, which was then never recognised
                // again because she is not wearing a household model. Four times a second, with
                // the game repopulating its own Denise the whole time.
                var have = _stand != null && _stand.Exists() && _stand.IsAlive;

                // Where the one being replaced was, if we still need to make her.
                var seat = Vector3.Zero;
                var facing = 0f;

                foreach (var ped in World.GetNearbyPeds(player, QuietRange))
                {
                    if (ped == null || !ped.Exists() || ped.Handle == player.Handle) continue;
                    if (_stand != null && _stand.Exists() && ped.Handle == _stand.Handle) continue;

                    if (!IsHousehold(ped)) continue;

                    // Her spot is worth having before she goes, and only from the first one.
                    if (!have && seat == Vector3.Zero)
                    {
                        seat = ped.Position;
                        facing = ped.Heading;
                    }

                    // Every household ped goes, ours or the game's. There are two of her in
                    // that room -- the game places one and the interior mod places another --
                    // and the game will put more back, so this is a standing job rather than a
                    // one-off.
                    try { ped.Delete(); }
                    catch { /* somebody else's to delete */ }
                }

                if (!have && seat != Vector3.Zero) StandIn(seat, facing);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not clear the couch: " + ex.Message);
            }
        }

        /// <summary>
        /// Puts our own woman where the game's was, and takes the game's away.
        ///
        /// A ped cannot be re-skinned in place -- the model IS the ped -- so the only way to
        /// have somebody else sat in that chair is to note where the occupant was, remove her,
        /// and create the one you wanted on the same mark.
        ///
        /// She is still Denise as far as everything else here is concerned: the same spot, the
        /// same freeze, the same silence. It is a change of face and nothing else, and it costs
        /// nothing to make because she has not had a line in this house since the day she was
        /// muted.
        /// </summary>
        private void StandIn(Vector3 at, float facing)
        {
            foreach (var name in StandInModels)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1200)) continue;

                    var handle = Function.Call<int>(Hash.CREATE_PED, 4, model.Hash,
                                                    at.X, at.Y, at.Z, facing, false, false);

                    model.MarkAsNoLongerNeeded();
                    if (handle == 0) continue;

                    var her = Entity.FromHandle(handle) as Ped;
                    if (her == null || !her.Exists()) continue;

                    // Claimed BEFORE anything else can run, so a sweep that fires while this
                    // one is still finishing sees her rather than deciding the room is empty.
                    _stand = her;
                    _settled = her;

                    // On the floor rather than wherever the ped we replaced happened to be. She
                    // was sat on a couch, and a woman created at a seated ped's own coordinate
                    // is a woman created a foot in the air -- which is most of the way to the
                    // five of them stacked at the ceiling.
                    try
                    {
                        Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, her.Handle,
                                      at.X, at.Y, at.Z, false, false, false);

                        if (World.GetGroundHeight(new Vector3(at.X, at.Y, at.Z + 1.5f),
                                                  out var floor, GetGroundHeightMode.Normal) &&
                            floor > 0f && Math.Abs(floor - at.Z) <= 2.5f)
                        {
                            Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, her.Handle,
                                          at.X, at.Y, floor, false, false, false);
                        }
                    }
                    catch { /* she is where she was put */ }

                    Settle(her);

                    if (!_saidCouchIsFree)
                    {
                        _saidCouchIsFree = true;
                        Log.Info("Stood " + name + " in for Denise at " + at + ".");
                    }

                    return;
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not stand in for Denise: " + ex.Message);
                }
            }

            // Nobody to stand in with. Nothing to fall back to either, because the one she
            // was replacing has already gone -- but that is the right way round: an empty room
            // for a session is recoverable and two of her is what we were fixing.
            Log.Warn("No stand-in model would load for the front room.");
        }

        /// <summary>Who stands in for her. First that this copy of the game has.</summary>
        private static readonly string[] StandInModels =
        {
            "ig_denise", "csb_denise", "cs_denise", "a_f_m_soucent_02"
        };

        /// <summary>The one we made, so she goes when the script does.</summary>
        private Ped _stand;

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
        private void Settle(Ped ped)
        {
            try
            {
                // Ours, so the population system does not recycle her mid-sentence.
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, ped.Handle, true, true);
                ped.IsPersistent = true;

                ped.IsVisible = true;
                Function.Call(Hash.SET_ENTITY_COLLISION, ped.Handle, true, true);

                // NOT frozen any more, and that is the point of this pass.
                //
                // The freeze was here because she walked into the kitchen while you were stood
                // at the counter -- but that is what she DOES, it is her house, and a woman
                // nailed to one square foot of her own front room is a prop. What actually
                // needed fixing was the shouting, which is the block below and stays.
                Function.Call(Hash.FREEZE_ENTITY_POSITION, ped.Handle, false);

                // And quiet. Blocking non-temporary events stops her reacting to gunfire, to
                // the player, to anything -- which is most of what makes an ambient ped talk.
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, true);
                Function.Call(Hash.STOP_CURRENT_PLAYING_AMBIENT_SPEECH, ped.Handle);
                Function.Call(Hash.SET_PED_CAN_PLAY_AMBIENT_ANIMS, ped.Handle, false);
                Function.Call(Hash.DISABLE_PED_PAIN_AUDIO, ped.Handle, true);

                // Not a target. She is in a house you fire a lot of rounds near.
                Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, ped.Handle, false);

                // And she is not in anybody's way while she does it.
                Function.Call(Hash.SET_PED_CONFIG_FLAG, ped.Handle, 17, true);

                Roam(ped);
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

        /// <summary>
        /// The middle of the rooms she uses, which is not the doorstep.
        ///
        /// House is the address -- the point on the porch that everything else in here measures
        /// from -- and wandering round THAT would take her down the path and into Forum Drive.
        /// This is the inside: worked out from the two interior coordinates the mod already
        /// knows for certain, the kitchen counter at the north end and the front room at the
        /// south, and it sits between them.
        /// </summary>
        private static readonly Vector3 Rooms = new Vector3(-12.5f, -1434.0f, 31.1f);

        /// <summary>
        /// How far she goes. Six metres reaches the counter and the couch and no further.
        ///
        /// The leash is wider than the wander on purpose: a ped who clips a doorway and ends up
        /// a metre outside the circle has not escaped, and dragging her back for it would be a
        /// woman twitching in her own hallway.
        /// </summary>
        private const float RoamRange = 6f;
        private const float LeashRange = 11f;

        /// <summary>How often the roam is checked on, and how long still counts as stuck.</summary>
        private const int RoamCheckMs = 4000;
        private const int StuckMs = 30000;

        private int _roamCheck;
        private int _movedAt;
        private Vector3 _wasAt;

        /// <summary>
        /// Sets her walking round her own house.
        ///
        /// TASK_WANDER_IN_AREA rather than a list of marks to walk between. A route is a woman
        /// on a patrol; a wander is somebody who lives here -- she picks her own destinations,
        /// stops for a while when she gets there, and the pauses are as much of it as the
        /// walking. The three arguments after the circle are the shortest walk she will bother
        /// with and how long she waits between them.
        /// </summary>
        private void Roam(Ped ped)
        {
            try
            {
                Function.Call(Hash.TASK_WANDER_IN_AREA, ped.Handle,
                              Rooms.X, Rooms.Y, Rooms.Z, RoamRange, 1.5f, 9f);

                _movedAt = Game.GameTime;
                _wasAt = ped.Position;
            }
            catch (Exception ex)
            {
                Log.Debug("Denise would not start walking: " + ex.Message);
            }
        }

        /// <summary>
        /// Keeps her in the house and keeps her moving.
        ///
        /// Two things go wrong with a wander and neither announces itself. She finds a doorway
        /// she cannot path back through and ends up in the yard, or the task quietly ends and
        /// she stands in the middle of the carpet for the rest of the session. Both are checked
        /// for rather than prevented, because the alternative is re-issuing the task on a timer
        /// and a wander that restarts every few seconds is a woman who cannot decide.
        /// </summary>
        private void KeepRoaming()
        {
            if (_stand == null || !_stand.Exists() || !_stand.IsAlive) return;

            if (Game.GameTime < _roamCheck) return;
            _roamCheck = Game.GameTime + RoamCheckMs;

            try
            {
                var at = _stand.Position;

                // Out of the house. Put back rather than walked back -- the nav mesh is the
                // reason she is out there, so asking it to bring her home is asking the thing
                // that failed to try again.
                if (at.DistanceTo(Rooms) > LeashRange)
                {
                    Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, _stand.Handle,
                                  Rooms.X, Rooms.Y, Rooms.Z, false, false, false);

                    Roam(_stand);
                    return;
                }

                if (at.DistanceTo(_wasAt) > 0.6f)
                {
                    _wasAt = at;
                    _movedAt = Game.GameTime;
                    return;
                }

                // Standing still is normal -- that is half of what a wander is. Standing still
                // for half a minute is a task that has ended.
                if (Game.GameTime - _movedAt < StuckMs) return;

                Roam(_stand);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not keep Denise walking: " + ex.Message);
            }
        }

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

            // The stand-in is OURS, so she is deleted rather than let go. Handed back she would
            // stay in that front room for the rest of the save with the game's own occupant
            // repopulating alongside her -- two women again, and this time one of them is a
            // stranger nobody can account for.
            try
            {
                if (_stand != null && _stand.Exists()) _stand.Delete();
            }
            catch { /* teardown */ }

            _stand = null;
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
