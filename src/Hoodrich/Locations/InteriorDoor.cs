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

        private readonly DoorSpec _spec;

        private Blip _blip;
        private bool _inside;
        private bool _busy;

        public InteriorDoor(DoorSpec spec)
        {
            _spec = spec;
        }

        private Vector3 Door => new Vector3(_spec.DoorX, _spec.DoorY, _spec.DoorZ);
        private Vector3 Inside => new Vector3(_spec.InsideX, _spec.InsideY, _spec.InsideZ);

        public bool IsInside => _inside;

        public void Update()
        {
            if (_busy) return;

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return;

            EnsureBlip();

            if (_inside)
            {
                if (player.Position.DistanceTo(Inside) > ExitRange) return;

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

                    player.Position = Door;
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

                    player.Position = Door;
                    Function.Call(Hash.FREEZE_ENTITY_POSITION, player.Handle, false);

                    Wait(400);
                    Fade(true);

                    Notify.Problem("that room ain" + "'" + "t loading. check [" + _spec.Section +
                                   "] Inside in the ini.");
                    return;
                }

                _inside = true;

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

                try { player.Position = Door; } catch { /* nothing else to try */ }
                Fade(true);
            }
            finally
            {
                _busy = false;
            }
        }

        private void Leave(Ped player)
        {
            _busy = true;

            try
            {
                Fade(false);

                player.Position = Door;
                player.Heading = _spec.DoorHeading;

                _inside = false;

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
