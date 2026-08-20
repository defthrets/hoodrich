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
        private const int StreamMs = 2500;

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

                Wait(StreamMs);

                // Ask again after it has had a chance to stream. Asking only once, immediately,
                // reports nothing for an interior that is perfectly real and simply not loaded.
                if (interior == 0)
                {
                    interior = Function.Call<int>(Hash.GET_INTERIOR_AT_COORDS, to.X, to.Y, to.Z);
                }

                if (interior == 0)
                {
                    Log.Warn("No interior at " + to + " for " + _spec.Ipl +
                             "; the coordinate in Hoodrich.ini is wrong.");

                    player.Position = Door;
                    Wait(400);
                    Fade(true);

                    Notify.Problem("that room ain't there. check [" + _spec.Section + "] Inside in the ini.");
                    return;
                }

                _inside = true;

                Wait(200);
                Fade(true);

                Notify.Ticker("~g~" + Capital(_spec.Name) + ".~s~");
            }
            catch (Exception ex)
            {
                Log.Error("Could not enter the " + _spec.Name, ex);

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
