using System;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.UI;
using Control = GTA.Control;

namespace Hoodrich.Locations
{
    /// <summary>
    /// One door and the room behind it, read out of the ini.
    ///
    /// Section is carried so the "that room is not there" message can name the exact block of
    /// settings to correct, rather than telling somebody a coordinate is wrong and leaving them
    /// to work out which of them.
    /// </summary>
    internal sealed class DoorSpec
    {
        public string Section = "";
        public string Name = "room";
        public string Ipl = "";
        public bool Blip = true;
        public BlipSprite Sprite = BlipSprite.Standard;

        public float DoorX, DoorY, DoorZ, DoorHeading;
        public float InsideX, InsideY, InsideZ, InsideHeading;
    }

    /// <summary>
    /// A door that puts you inside one of the game's own interiors.
    ///
    /// One thing about interiors that is worth stating plainly, because it shapes all of this.
    /// An MLO is not a prop and cannot be placed. It is baked into the map at one fixed
    /// coordinate, and REQUEST_IPL only decides whether that coordinate has anything in it -- it
    /// cannot decide WHERE. So "put an interior over there" is really "put a DOOR over there",
    /// and the door warps you across the map to wherever the interior actually lives. That is
    /// how every mod that uses a base-game interior does it, and from inside it is
    /// indistinguishable, because there are no windows.
    ///
    /// Every coordinate lives in Hoodrich.ini rather than in here. The door needs moving to
    /// wherever it should really stand, and the interior coordinate is a guess at where that
    /// particular room sits -- neither is worth a rebuild.
    ///
    /// One class rather than one per room. A second copy of this file with different constants
    /// in it is how a mod ends up with two of everything and a bug fixed in only one of them.
    ///
    /// The guess has a safety net. After the warp, the game is asked whether there is actually
    /// an interior where we just put you; if there is not, you come straight back out and get
    /// told the coordinate is wrong, rather than being left standing in a black void under the
    /// map wondering whether the mod has crashed.
    /// </summary>
    internal sealed class InteriorDoor
    {
        /// <summary>How close to the door before it offers to let you in.</summary>
        private const float DoorRange = 1.8f;

        /// <summary>How close to the inside mark before it offers to let you out.</summary>
        private const float ExitRange = 2.4f;

        /// <summary>Long enough for the fade, short enough not to feel like a loading screen.</summary>
        private const int FadeMs = 700;

        /// <summary>How long the interior gets to stream before we judge whether it is there.</summary>
        /// <summary>
        /// How long a room is given to arrive, and how often it is asked.
        ///
        /// A ceiling rather than a duration. The old flat wait was a guess -- too long on a
        /// machine that had the interior cached, and not long enough on the run that dropped
        /// somebody through the floor of the grow room.
        /// </summary>
        private const int StreamCeilingMs = 8000;
        private const int StreamStepMs = 100;

        /// <summary>
        /// How far above the recorded height the floor is looked for, and how far off that
        /// height the answer is still believed.
        ///
        /// The probe starts above him because it looks downward. The band is what stops it
        /// following a hit on the wrong surface: these rooms sit forty metres under the map,
        /// so a few metres either side of the recorded height is the floor of the room and
        /// anything further away is something else entirely.
        /// </summary>
        private const float FloorProbeUp = 3f;
        private const float FloorProbeBand = 4f;

        /// <summary>
        /// How far the room's own origin may be from the recorded coordinate before it is
        /// treated as a different room entirely.
        ///
        /// GET_INTERIOR_AT_COORDS will answer for an interior the point is merely NEAR, so an
        /// origin a hundred metres away means the ini is pointing at the wrong building rather
        /// than at the wrong corner of the right one -- and warping to it would be worse than
        /// failing honestly.
        /// </summary>
        private const float OriginTrust = 60f;

        /// <summary>
        /// Close enough to the entry mark to be in the doorway rather than out of the room.
        ///
        /// Only used by the watchdog below, which needs to tell "stepped through the frame for
        /// a second" apart from "walked out of the building".
        /// </summary>
        private const float DoorwaySlack = 8f;

        /// <summary>Long enough after the warp for the room to have decided he is in it.</summary>
        private const int SettleGraceMs = 2500;

        private readonly DoorSpec _spec;

        private Blip _blip;
        private bool _inside;
        private bool _busy;

        /// <summary>
        /// Where he was stood when he pressed the key, and which way he was facing.
        ///
        /// The way out is the way in, rather than the coordinate in the ini. Those are usually
        /// the same spot to within a stride, and the ini value is still the fallback for a
        /// reload -- but "put him back where he was" is the thing that is actually meant, and
        /// saying it outright means the exit cannot drift away from the entrance.
        /// </summary>
        private Vector3 _cameFrom;
        private float _cameFacing;

        /// <summary>
        /// Where he was actually PUT inside, which is not always where the ini says.
        ///
        /// This is the grow room bug. The recorded coordinate for that room is twenty-one
        /// metres outside its own volume, so entering corrects it to the room's own origin and
        /// stands him there -- and then the way out was measured against the ini coordinate he
        /// had just been moved off. The prompt to leave was therefore twenty-one metres away,
        /// through a wall, at the one spot in the area that is not inside the room. Walk to it
        /// and you are stood outside the building at its real place on the map, which is
        /// exactly what it looked like from the outside: a door that dumps you at the docks.
        /// </summary>
        private Vector3 _standing;

        /// <summary>When the warp finished, so the watchdog does not fire during streaming.</summary>
        private int _enteredAt;

        public InteriorDoor(DoorSpec spec)
        {
            _spec = spec;
        }

        private Vector3 Door => new Vector3(_spec.DoorX, _spec.DoorY, _spec.DoorZ);
        private Vector3 Inside => new Vector3(_spec.InsideX, _spec.InsideY, _spec.InsideZ);

        /// <summary>The mark to leave from: where he was actually stood, or the ini's guess.</summary>
        private Vector3 Mark => _standing == Vector3.Zero ? Inside : _standing;

        /// <summary>The way back out: the doorway he used, or the ini's door after a reload.</summary>
        private Vector3 Back => _cameFrom == Vector3.Zero ? Door : _cameFrom;

        private float BackFacing => _cameFrom == Vector3.Zero ? _spec.DoorHeading : _cameFacing;

        public bool IsInside => _inside;

        public void Update()
        {
            if (_busy) return;

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return;

            EnsureBlip();

            if (_inside)
            {
                if (WanderedOut(player)) return;

                if (player.Position.DistanceTo(Mark) > ExitRange) return;

                Help.ShowThisFrame("Press ~INPUT_CONTEXT~ to leave the " + _spec.Name + ".");

                if (Game.IsControlJustPressed(Control.Context)) Leave(player);
                return;
            }

            // On foot. Driving a car into a warehouse you reached by teleport leaves the car
            // where it was and you inside without it, which reads as a bug even when it is not.
            if (player.IsInVehicle()) return;
            if (player.Position.DistanceTo(Door) > DoorRange) return;

            Help.ShowThisFrame("Press ~INPUT_CONTEXT~ to go into the " + _spec.Name + ".");

            if (Game.IsControlJustPressed(Control.Context)) Enter(player);
        }

        /// <summary>
        /// In. Fade, load, warp, check, fade back.
        ///
        /// The check is the important part. The interior coordinate is a guess until somebody
        /// stands in there and reads it off the HUD, and a wrong guess without a check is the
        /// player under the map in the dark with no way out but a reload.
        /// </summary>
        private void Enter(Ped player)
        {
            _busy = true;

            // Taken before anything moves him, because this is what "back outside" means. He
            // is within a stride of the door -- the prompt does not appear otherwise -- so this
            // is the doorway, read off him rather than typed into a file.
            _cameFrom = player.Position;
            _cameFacing = player.Heading;

            try
            {
                Fade(false);

                Function.Call(Hash.REQUEST_IPL, _spec.Ipl);

                var to = Inside;

                // Asked for BEFORE the warp, so the streamer has the whole fade to work in
                // rather than being told about the room only once somebody is standing in it.
                Function.Call(Hash.REQUEST_COLLISION_AT_COORD, to.X, to.Y, to.Z);
                Function.Call(Hash.NEW_LOAD_SCENE_START_SPHERE, to.X, to.Y, to.Z, 40f, 0);

                // Frozen across the warp, and this is the fix.
                //
                // A warp puts him at a coordinate whether or not anything is loaded there, and
                // an unfrozen ped in empty space starts falling on the very next frame -- so by
                // the time the room streams in he is already below it, which is the falling
                // through the sky. Frozen, he waits where he was put.
                Function.Call(Hash.FREEZE_ENTITY_POSITION, player.Handle, true);

                player.Position = to;
                player.Heading = _spec.InsideHeading;

                // Pin the interior so the game does not decide the room is not worth streaming
                // while we are stood in the middle of it.
                var interior = Function.Call<int>(Hash.GET_INTERIOR_AT_COORDS, to.X, to.Y, to.Z);

                if (interior != 0)
                {
                    Function.Call(Hash.PIN_INTERIOR_IN_MEMORY, interior);
                    Function.Call(Hash.SET_INTERIOR_ACTIVE, interior, true);
                    Function.Call(Hash.REFRESH_INTERIOR, interior);

                    // Where the room ACTUALLY is, asked of the game rather than read off a
                    // coordinate somebody typed into an ini.
                    //
                    // This is what the grow room needed. The log said it plainly:
                    //
                    //   ipl bkr_biker_dlc_int_ware02 active=False,
                    //   interior=235521, he is in interior=0
                    //
                    // An interior IS registered at the recorded coordinate -- that is the
                    // 235521 -- but standing on that exact point put him OUTSIDE its volume,
                    // with no collision under him and no room around him. Near enough to find
                    // the room, not near enough to be in it, which is the one failure a
                    // hand-taken reading produces and the one a person cannot debug by looking.
                    //
                    // The interior knows its own origin. Offset zero from it is the middle of
                    // the room, which is somewhere a man can stand.
                    var origin = Function.Call<Vector3>(Hash.GET_OFFSET_FROM_INTERIOR_IN_WORLD_COORDS,
                                                        interior, 0f, 0f, 0f);

                    if (origin != Vector3.Zero && origin.DistanceTo(to) < OriginTrust)
                    {
                        Log.Info("The " + _spec.Name + " is really at " + origin +
                                 ", not " + to + " -- using the room's own origin.");
                        to = origin;
                    }
                }

                // Waited ON rather than waited OUT.
                //
                // This was a flat two and a half seconds, which is a guess at how long a room
                // takes to arrive -- too long on a machine that had it cached and, on the run
                // that produced this bug, not long enough. Now it asks: is there collision
                // round him yet, and is the interior itself ready. It gives up at a ceiling
                // rather than hanging, because a fade that never lifts is worse than a room
                // that never arrives.
                var waited = 0;
                var iplOn = false;
                var inRoom = 0;

                while (waited < StreamCeilingMs)
                {
                    Wait(StreamStepMs);
                    waited += StreamStepMs;

                    if (interior == 0)
                    {
                        interior = Function.Call<int>(Hash.GET_INTERIOR_AT_COORDS, to.X, to.Y, to.Z);

                        if (interior != 0)
                        {
                            Function.Call(Hash.PIN_INTERIOR_IN_MEMORY, interior);
                            Function.Call(Hash.SET_INTERIOR_ACTIVE, interior, true);
                        }
                    }

                    // Re-asked rather than asked once. REQUEST_IPL is a request and not a
                    // load -- it returns immediately whether or not anything happened, and the
                    // old code fired it once and warped on the very next line. If the IPL is
                    // still not active after all this waiting it gets asked again, because a
                    // dropped request looks exactly like a slow one from in here.
                    iplOn = Function.Call<bool>(Hash.IS_IPL_ACTIVE, _spec.Ipl);

                    if (!iplOn && waited % 1000 == 0)
                    {
                        Function.Call(Hash.REQUEST_IPL, _spec.Ipl);
                    }

                    var solid = Function.Call<bool>(Hash.HAS_COLLISION_LOADED_AROUND_ENTITY,
                                                    player.Handle);

                    var ready = interior == 0 ||
                                Function.Call<bool>(Hash.IS_INTERIOR_READY, interior);

                    var scene = Function.Call<bool>(Hash.IS_NEW_LOAD_SCENE_LOADED);

                    // Where he IS, not what is at the coordinate. The two disagree when the
                    // room exists but he has not landed inside its volume, which is the
                    // difference between a room that failed to load and a coordinate that
                    // points at the wrong side of one of its walls.
                    inRoom = Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, player.Handle);

                    if (solid && ready && scene && inRoom != 0) break;
                }

                Function.Call(Hash.NEW_LOAD_SCENE_STOP);

                // Written every time, not only on failure. This is the one thing in the mod
                // that cannot be worked out from the outside: "I fell through the floor" is the
                // same sentence whether the IPL never loaded, the interior is not at that
                // coordinate, or the coordinate is inside a wall. These four numbers tell those
                // three apart, and without them the next attempt is another guess.
                Log.Info("Entering " + _spec.Name + ": ipl " + _spec.Ipl + " active=" + iplOn +
                         ", interior=" + interior + ", he is in interior=" + inRoom +
                         ", waited " + waited + "ms");

                if (waited >= StreamCeilingMs)
                {
                    Log.Warn("The " + _spec.Name + " did not settle within " + StreamCeilingMs +
                             "ms (ipl active=" + iplOn + ", in interior=" + inRoom + ").");
                }

                if (interior == 0)
                {
                    Log.Warn("No interior at " + to + " for " + _spec.Ipl +
                             "; the coordinate in Hoodrich.ini is wrong.");

                    player.Position = Back;
                    player.Heading = BackFacing;
                    Function.Call(Hash.FREEZE_ENTITY_POSITION, player.Handle, false);

                    Wait(400);
                    Fade(true);

                    Notify.Problem("that room ain't there. check [" + _spec.Section + "] Inside in the ini.");
                    return;
                }

                // The floor the GAME reports, not the one somebody typed into the ini.
                //
                // A hand-entered Z is a reading taken once by standing in the room, and it is
                // the single number here that cannot be checked from outside the game. Now the
                // geometry has streamed, the game will say where the floor really is -- so ask
                // it, and stand him on that. Believed only within a sane band of the recorded
                // height: a probe that comes back with the street thirty metres up has found
                // the wrong surface entirely, and following it would put him on the pavement.
                float floorZ;
                var floorFound = World.GetGroundHeight(new Vector3(to.X, to.Y, to.Z + FloorProbeUp),
                                                       out floorZ, GetGroundHeightMode.Normal);

                if (floorFound && Math.Abs(floorZ - to.Z) <= FloorProbeBand)
                {
                    player.Position = new Vector3(to.X, to.Y, floorZ + 0.05f);
                }
                else
                {
                    Log.Warn("No floor under " + to + " in the " + _spec.Name +
                             " (probe found=" + floorFound + ", z=" + floorZ.ToString("0.00") +
                             "); standing him on the ini height instead.");
                }

                // Nothing under him and not in a room: that is open air, and letting go of him
                // here IS the falling through the sky. Better to come back out of the door he
                // went in by and be told why than to be dropped into the void under the map.
                if (!floorFound && inRoom == 0)
                {
                    Log.Warn("The " + _spec.Name + " is not there: no floor and no interior at " +
                             to + ". Check [" + _spec.Section + "] Inside in the ini.");

                    player.Position = Back;
                    player.Heading = BackFacing;
                    Function.Call(Hash.FREEZE_ENTITY_POSITION, player.Handle, false);

                    Wait(400);
                    Fade(true);

                    Notify.Problem("that room ain" + "'" + "t loading. check [" + _spec.Section +
                                   "] Inside in the ini.");
                    return;
                }

                _inside = true;

                // Where he ended up, not where the ini said to put him. Everything about
                // getting out again is measured from here.
                _standing = player.Position;
                _enteredAt = Game.GameTime;

                // Let go last, with the floor under him and the screen still black.
                Function.Call(Hash.FREEZE_ENTITY_POSITION, player.Handle, false);

                Wait(200);
                Fade(true);

                Notify.Ticker("~g~" + Capital(_spec.Name) + ".~s~");
            }
            catch (Exception ex)
            {
                Log.Error("Could not enter the " + _spec.Name, ex);

                // Unfrozen FIRST. Anything thrown after the freeze would otherwise leave
                // him unable to move for the rest of the save, with the fade lifting onto a
                // man who cannot walk -- worse than the failure that caused it.
                try { Function.Call(Hash.FREEZE_ENTITY_POSITION, player.Handle, false); }
                catch { /* nothing else to try */ }

                try { player.Position = Back; } catch { /* nothing else to try */ }
                Fade(true);
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>
        /// Whether he has got out of the room some way other than the door he came in by.
        ///
        /// These interiors are real places on the map, sitting forty metres under it, and their
        /// own openings lead into nothing. Walking out of one leaves a man stood in the dark at
        /// the far end of the map with the mod still believing he is in the grow room -- which
        /// reads as the teleport having gone wrong rather than as him having walked somewhere.
        ///
        /// Out of the interior but still where the interior is means exactly that, and he is
        /// put back at the shutter. Out of it and nowhere near it means something else moved
        /// him -- Pillbox, a cell, another script -- and then the right thing is to forget he
        /// was ever in there rather than to drag him across the map.
        /// </summary>
        private bool WanderedOut(Ped player)
        {
            if (_enteredAt != 0 && Game.GameTime - _enteredAt < SettleGraceMs) return false;

            int room;

            try
            {
                room = Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, player.Handle);
            }
            catch
            {
                return false;
            }

            if (room != 0) return false;

            var away = player.Position.DistanceTo(Mark);

            // A doorway reads as no interior for a step or two. Not acted on until he is
            // properly clear of it.
            if (away < DoorwaySlack) return false;

            if (away > OriginTrust)
            {
                Log.Info("Out of the " + _spec.Name + " and " + (int)away +
                         "m from it; something else moved him, so it is forgotten.");

                _inside = false;
                _standing = Vector3.Zero;
                return true;
            }

            Log.Info("Walked out of the " + _spec.Name + " itself; putting him back at the door.");

            Leave(player);
            return true;
        }

        /// <summary>
        /// Out, to the doorway he came in by.
        ///
        /// Not to the coordinate in the ini. They are the same place in the ordinary case, and
        /// the ini is still what a reload falls back on -- but the room does not decide where
        /// the street is, and putting him back exactly where he was standing is the only
        /// version of this that cannot come out somewhere else.
        /// </summary>
        private void Leave(Ped player)
        {
            _busy = true;

            try
            {
                Fade(false);

                player.Position = Back;
                player.Heading = BackFacing;

                _inside = false;
                _standing = Vector3.Zero;

                Wait(400);
                Fade(true);
            }
            catch (Exception ex)
            {
                Log.Error("Could not leave the " + _spec.Name, ex);
                Fade(true);
            }
            finally
            {
                _busy = false;
            }
        }

        private static void Fade(bool inwards)
        {
            try
            {
                if (inwards) Function.Call(Hash.DO_SCREEN_FADE_IN, FadeMs);
                else
                {
                    Function.Call(Hash.DO_SCREEN_FADE_OUT, FadeMs);
                    Wait(FadeMs);
                }
            }
            catch
            {
                // A hard cut is survivable. Being stuck on black is not, so a failed fade OUT
                // never stops the warp, and the fade IN is attempted regardless.
            }
        }

        private static void Wait(int ms)
        {
            var until = Game.GameTime + ms;
            while (Game.GameTime < until) Script.Yield();
        }

        private void EnsureBlip()
        {
            if (!_spec.Blip)
            {
                if (_blip != null && _blip.Exists()) { _blip.Delete(); _blip = null; }
                return;
            }

            if (_blip != null && _blip.Exists()) return;

            try
            {
                _blip = World.CreateBlip(Door);
                if (_blip == null || !_blip.Exists()) return;

                _blip.Sprite = _spec.Sprite;
                _blip.Color = BlipColor.Green;
                _blip.Scale = 0.8f;
                _blip.IsShortRange = true;

                // Big map only. SET_BLIP_DISPLAY 2 keeps it out of the minimap while leaving
                // it on the pause map -- a door you have to know about is not the same as a
                // door the corner of your screen keeps pointing at.
                Function.Call(Hash.SET_BLIP_DISPLAY, _blip.Handle, 2);
                _blip.Name = Capital(_spec.Name);
            }
            catch
            {
                // A blip is a nicety.
            }
        }

        /// <summary>Sentence case, for a blip name and a ticker.</summary>
        private static string Capital(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        public void RestoreWorld()
        {
            try { if (_blip != null && _blip.Exists()) _blip.Delete(); }
            catch { /* teardown */ }

            _blip = null;

            // The IPL is left loaded on purpose. Unloading an interior the player might be
            // standing in is a far worse ending than a warehouse nobody is looking at.
        }
    }
}
