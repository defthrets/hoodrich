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
        /// <summary>How far in front of the camera it floats. Near enough to read, far enough not to clip the lens.</summary>
        private const float Depth = 1.35f;

        /// <summary>Degrees a second. Slow enough to read the silhouette, quick enough to be alive.</summary>
        private const float Spin = 34f;

        /// <summary>Tipped down a little, so it is seen along the barrel rather than edge on.</summary>
        private const float Tilt = 12f;

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
                                             at.X, at.Y, at.Z, true, 1.0f, 0);

                if (_object == 0) return;

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
        public void Place(float sx, float sy)
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
            }
            catch (Exception ex)
            {
                Log.Debug("Could not place the gun in the window: " + ex.Message);
            }
        }

        public void Clear()
        {
            if (_object == 0) return;

            try
            {
                var handle = _object;
                Function.Call(Hash.DELETE_OBJECT, new OutputArgument(handle));
            }
            catch
            {
                // The game clears it when nobody is looking.
            }

            _object = 0;
            _weapon = 0;
            _fitted = "";
            _lastAt = 0;
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
