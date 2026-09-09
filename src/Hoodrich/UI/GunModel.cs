using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.UI
{
    /// <summary>
    /// The real gun, turning in the window of the counter screen.
    ///
    /// WHY THIS EXISTS AT ALL, and it is not because a photograph is prettier. Eleven of the
    /// weapons on that counter HAVE no photograph: the four texture packs this game ships --
    /// mpweaponscommon, gang0, gang1 and unusedfornow -- simply do not contain art for the AP
    /// Pistol, the SNS, the Vintage, the Double Action, the Compact Rifle, the Double Barrel,
    /// the Switchblade, the Machete, the Pipe Bomb or the Flare. That was established by
    /// sweeping seventy candidate pack names on a real install and writing down what answered.
    /// No amount of further guessing produces a picture that was never made.
    ///
    /// A MODEL CANNOT BE MISSING. Every one of those weapons has a model, because you can hold
    /// it -- so the object itself is the picture, and the problem is gone rather than narrowed.
    ///
    /// AND IT SHOWS THE PARTS ON. A photograph is of a bare gun; this is the gun you would
    /// actually walk out with, because the components are fitted to the object. Scroll onto a
    /// suppressor and the suppressor is on it.
    ///
    /// IT LIVES IN A HOLE IN THE PANEL. Everything drawn here is 2D and 2D is drawn after the
    /// world, so a rectangle is always in front of a thing in the room -- there is no order of
    /// calls that puts a model in front of a panel. The counter leaves the picture box
    /// unpainted (see Theme.PanelAround) and the object is placed in the room so that it lands
    /// in that gap, which is why this takes a screen position rather than a world one.
    ///
    /// NOTHING IS PICKED UP AND NOTHING IS SHOT. The object has no collision, is frozen, and
    /// is deleted the moment the screen closes or the selection changes.
    /// </summary>
    internal sealed class GunModel
    {
        /// <summary>
        /// How far in front of the camera it floats, and how much bigger it is made to
        /// compensate.
        ///
        /// IT WAS BLURRED, AND MOVING IT IS THE ONLY FIX. The game applies depth of field to
        /// the near field of the gameplay camera and a gun a metre and a third away is well
        /// inside it -- so it came out soft while the shop behind it was sharp. There is no
        /// flag to turn that off for one object; the gameplay camera's focus is not ours to
        /// set. What can be done is stand further back, which is out past the blur.
        ///
        /// AND THEN IT IS TINY, because apparent size is distance. CREATE_WEAPON_OBJECT takes
        /// a scale, so the object is made bigger by the same factor it was moved away by --
        /// three point two over one point three five is a shade under two and a half -- and it
        /// fills the window exactly as it did, in focus.
        /// </summary>
        private const float Depth = 3.2f;
        private const float Blow = 2.4f;

        /// <summary>Degrees a second. Slow enough to read the silhouette, quick enough to be alive.</summary>
        private const float Spin = 34f;

        /// <summary>Tipped down a little, so it is seen along the barrel rather than edge on.</summary>
        private const float Tilt = 12f;

        /// <summary>
        /// How far behind the gun the backdrop hangs, and how much wider than the window it is.
        ///
        /// FAR ENOUGH TO CLEAR THE LONGEST GUN. A sniper rifle turning end-on sweeps most of a
        /// metre, and a backdrop closer than that gets a barrel through it twice a revolution.
        /// Wider than the window because the panel's hole has a hard edge and a backdrop that
        /// only just covers it shows a sliver of grass at the corners on a wide monitor.
        /// </summary>
        private const float BackSet = 1.15f;
        private const float BackOver = 1.06f;

        /// <summary>
        /// Every weapon object this has created, so that losing track of one is survivable.
        ///
        /// THE FIELD IS NOT ENOUGH ON ITS OWN. _object is one handle, and anything that
        /// overwrites it without deleting first -- a throw halfway through Show, a path nobody
        /// thought of -- strands a rifle in the air with nothing left pointing at it. A list of
        /// everything ever made is swept on every clear, so the worst case is a gun that hangs
        /// there until the next time the counter is used rather than until the game is closed.
        /// </summary>
        private readonly List<int> _made = new List<int>();

        private int _object;
        private uint _weapon;
        private string _fitted = "";
        private float _turn;
        private int _lastAt;

        public bool Live => _object != 0;

        /// <summary>
        /// The weapon to show, and everything bolted to it.
        ///
        /// REBUILT WHEN THE ANSWER CHANGES AND NOT OTHERWISE. Creating a weapon object is not
        /// free and the parts list is walked every frame the screen is open, so the components
        /// are joined into a key and compared -- a frame that wants the same gun with the same
        /// parts on it keeps the object it already has, spinning.
        /// </summary>
        public void Show(uint weapon, List<uint> parts)
        {
            var key = Key(parts);

            if (_object != 0 && weapon == _weapon && key == _fitted) return;

            Clear();

            if (weapon == 0) return;

            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return;

                var at = me.Position + new Vector3(0f, 0f, 3f);

                // Ammo nought and the world model shown: this is a thing to look at, not a
                // pickup somebody can run over.
                _object = Function.Call<int>(Hash.CREATE_WEAPON_OBJECT, weapon, 0,
                                             at.X, at.Y, at.Z, true, Blow, 0);

                if (_object == 0) return;

                // EVERY ONE EVER MADE, so none of them can be lost. See Clear.
                _made.Add(_object);

                _weapon = weapon;
                _fitted = key;

                Function.Call(Hash.SET_ENTITY_COLLISION, _object, false, false);
                Function.Call(Hash.FREEZE_ENTITY_POSITION, _object, true);
                Function.Call(Hash.SET_ENTITY_INVINCIBLE, _object, true);

                // NOT ON THE MINIMAP AND NOT IN ANYBODY'S WAY.
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, _object, true, true);

                if (parts == null) return;

                foreach (var part in parts)
                {
                    if (part == 0) continue;

                    try
                    {
                        Function.Call(Hash.GIVE_WEAPON_COMPONENT_TO_WEAPON_OBJECT, _object, part);
                    }
                    catch
                    {
                        // A component the model will not take is one part missing off a
                        // preview, which is a smaller wrong thing than no preview.
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not make a gun to look at: " + ex.Message);
                Clear();
            }
        }

        /// <summary>
        /// Put it where the window is, and turn it.
        ///
        /// THE MATHS IS THE CAMERA'S, NOT THE SCREEN'S. A point on screen is a direction from
        /// the camera, and how far that direction is off-axis depends on the field of view and
        /// the shape of the monitor -- so the offset is worked out from both rather than from a
        /// number that happened to look right on one machine. At a given depth the visible
        /// height is 2 * depth * tan(fov/2), and the width is that times the aspect.
        /// </summary>
        public void Place(float sx, float sy, float sw, float sh)
        {
            if (_object == 0) return;

            var now = Game.GameTime;

            var step = _lastAt == 0 ? 0f : (now - _lastAt) * 0.001f;
            _lastAt = now;

            _turn = (_turn + Spin * step) % 360f;

            try
            {
                var cam = Function.Call<Vector3>(Hash.GET_GAMEPLAY_CAM_COORD);
                var rot = Function.Call<Vector3>(Hash.GET_GAMEPLAY_CAM_ROT, 2);

                var pitch = rot.X * (float)Math.PI / 180f;
                var yaw = rot.Z * (float)Math.PI / 180f;

                var cp = (float)Math.Cos(pitch);

                var forward = new Vector3((float)(-Math.Sin(yaw) * cp),
                                          (float)(Math.Cos(yaw) * cp),
                                          (float)Math.Sin(pitch));

                var right = new Vector3((float)Math.Cos(yaw), (float)Math.Sin(yaw), 0f);
                var up = Vector3.Cross(right, forward);

                var fov = Function.Call<float>(Hash.GET_GAMEPLAY_CAM_FOV);
                if (fov <= 1f) fov = 50f;

                var halfH = Depth * (float)Math.Tan(fov * 0.5f * Math.PI / 180f);

                var aspect = Function.Call<float>(Hash.GET_ASPECT_RATIO, false);
                if (aspect <= 0.1f) aspect = 16f / 9f;

                var halfW = halfH * aspect;

                var offRight = (sx - 0.5f) * 2f * halfW;
                var offUp = -(sy - 0.5f) * 2f * halfH;

                var at = cam + forward * Depth + right * offRight + up * offUp;

                Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, _object, at.X, at.Y, at.Z,
                              false, false, false);

                // Turned in the camera's frame rather than the world's, so it faces you
                // whichever way you happen to be standing.
                Function.Call(Hash.SET_ENTITY_ROTATION, _object,
                              Tilt, 0f, rot.Z + _turn, 2, true);

                Backdrop(cam, forward, right, up, halfH, aspect, sx, sy, sw, sh);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not place the gun in the window: " + ex.Message);
            }
        }

        /// <summary>
        /// A flat panel hung behind the gun, so it is read against something rather than
        /// against a hedge.
        ///
        /// DRAWN AS TWO TRIANGLES IN THE WORLD, which is the only way this can work. The window
        /// is a hole in the panel precisely so the world shows through it -- so anything drawn
        /// to fill that hole has to be drawn in the SAME pass as the gun, or it is either in
        /// front of it (2D, which covers the gun) or not there at all. DRAW_POLY is world
        /// space, flat, untextured and takes a colour, which is exactly a backdrop.
        ///
        /// It hangs further from the camera than the gun does and is sized for ITS depth, not
        /// the gun's -- the same window is a bigger rectangle the further back you put it, and
        /// a backdrop sized for the near plane leaves the corners open.
        ///
        /// The panel's own colour, so the window reads as part of the screen rather than as a
        /// hole with a grey card behind it.
        /// </summary>
        private static void Backdrop(Vector3 cam, Vector3 forward, Vector3 right, Vector3 up,
                                     float halfH, float aspect,
                                     float sx, float sy, float sw, float sh)
        {
            try
            {
                var depth = Depth + BackSet;

                // Sized at ITS OWN depth. halfH came in for the gun's plane; the backdrop is
                // further away, and the same angle covers more ground out there.
                var scale = depth / Depth;

                var hH = halfH * scale;
                var hW = hH * aspect;

                var x0 = (sx - sw * 0.5f * BackOver - 0.5f) * 2f * hW;
                var x1 = (sx + sw * 0.5f * BackOver - 0.5f) * 2f * hW;

                var y0 = -(sy - sh * 0.5f * BackOver - 0.5f) * 2f * hH;
                var y1 = -(sy + sh * 0.5f * BackOver - 0.5f) * 2f * hH;

                var mid = cam + forward * depth;

                var a = mid + right * x0 + up * y0;
                var b = mid + right * x1 + up * y0;
                var c = mid + right * x1 + up * y1;
                var d = mid + right * x0 + up * y1;

                // The panel's own colour, so the window reads as a recess in the screen
                // rather than as a hole with a card behind it.
                var ink = Theme.Body;

                // BOTH WINDINGS. A triangle is one-sided and which side you are looking at
                // depends on how the camera is turned; drawing each one twice, wound the other
                // way, costs two calls and removes the question.
                Face(a, b, c, ink);
                Face(a, c, b, ink);
                Face(a, c, d, ink);
                Face(a, d, c, ink);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not hang the backdrop: " + ex.Message);
            }
        }

        private static void Face(Vector3 a, Vector3 b, Vector3 c, System.Drawing.Color ink)
        {
            Function.Call(Hash.DRAW_POLY,
                          a.X, a.Y, a.Z, b.X, b.Y, b.Z, c.X, c.Y, c.Z,
                          (int)ink.R, (int)ink.G, (int)ink.B, 255);
        }

        public void Clear()
        {
            Sweep();

            if (_object == 0) return;

            try
            {
                // THROUGH THE ENTITY, NOT THE NATIVE. DELETE_OBJECT wants a POINTER to the
                // handle and zeroes it -- handing it a fresh OutputArgument built from a copy
                // deletes nothing and reports nothing, which is a rifle left hanging in the air
                // outside the shop with no line in the log. Entity.Delete does the pair of
                // calls the game actually wants: claim it as ours, then delete it.
                var thing = Entity.FromHandle(_object);

                if (thing != null && thing.Exists())
                {
                    thing.IsPersistent = false;
                    thing.Delete();
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not take the gun out of the window: " + ex.Message);
            }

            // AND IF IT SOMEHOW SURVIVED THAT, it is at least not ours any more and the game
            // is free to clear it the moment nobody is looking.
            try
            {
                var left = Entity.FromHandle(_object);

                if (left != null && left.Exists())
                {
                    Function.Call(Hash.SET_ENTITY_AS_NO_LONGER_NEEDED, new OutputArgument(_object));
                }
            }
            catch
            {
                // Nothing further to try.
            }

            _made.Remove(_object);

            _object = 0;
            _weapon = 0;
            _fitted = "";
            _lastAt = 0;

            // And anything the line above did not account for.
            Sweep();
        }

        /// <summary>Anything ever made that is still standing there, taken down.</summary>
        private void Sweep()
        {
            for (var i = _made.Count - 1; i >= 0; i--)
            {
                var handle = _made[i];

                if (handle != 0 && handle == _object) continue;

                _made.RemoveAt(i);

                try
                {
                    var thing = Entity.FromHandle(handle);
                    if (thing == null || !thing.Exists()) continue;

                    thing.IsPersistent = false;
                    thing.Delete();
                }
                catch
                {
                    // Next sweep, or the game's own clean-up.
                }
            }
        }

        private static string Key(List<uint> parts)
        {
            if (parts == null || parts.Count == 0) return "";

            var key = "";
            foreach (var part in parts) key += part.ToString("X8");

            return key;
        }
    }
}
