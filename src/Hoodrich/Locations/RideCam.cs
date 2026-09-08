using System;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Locations
{
    /// <summary>
    /// The look at a place before you book a Knowai to it: a scripted camera out at the
    /// destination, circling it slowly, while the picker sits across the bottom of the
    /// screen (see UI.RideScreen).
    ///
    /// LIVE, NOT A PHOTOGRAPH. A picture of each place would have to be shot once on every
    /// machine and kept on disk, and would be the same picture at midnight in the rain as at
    /// noon. The game already has the place; it only has to be pointed at. The cost is
    /// streaming: the world loads around the camera and not around him, so he is frozen
    /// where he stands until the ground under him is back -- the same thing the game does
    /// while it shows you a property -- and the picker refuses in a fight, in the air, in a
    /// car and under a wanted level, where a man frozen on the pavement is a man shot.
    /// </summary>
    internal static class RideCam
    {
        private static int _cam;
        private static Vector3 _at;
        private static float _spin;
        private static int _movedAt;
        private static int _lastTick;
        private static Ped _held;
        private static int _settleUntil;

        /// <summary>Metres out from the spot, and above it. High enough to read a block, low enough to read a door.</summary>
        private const float Around = 24f;
        private const float Up = 9f;

        /// <summary>The camera looks a little above the ground at the spot, not at the tarmac.</summary>
        private const float LookUp = 1.4f;

        private const float Fov = 50f;
        private const float SpinPerSec = 5f;
        private const int HandBackMs = 500;

        /// <summary>Black while the camera jumps, then the view lifts over this.</summary>
        private const int JumpMs = 220;
        private const int LiftMs = 520;

        /// <summary>The longest he stays frozen after the camera hands back, waiting for his ground.</summary>
        private const int SettleMostMs = 2500;

        private const float LoadRadius = 90f;

        public static bool Showing => _cam != 0;

        public static bool CanShow(Ped me)
        {
            try
            {
                if (me == null || !me.Exists() || !me.IsAlive) return false;
                if (me.IsInVehicle() || me.IsInAir || me.IsRagdoll || me.IsInCombat) return false;
                if (Game.Player.Wanted.WantedLevel > 0) return false;
                if (Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE)) return false;
                if (!Function.Call<bool>(Hash.IS_SCREEN_FADED_IN)) return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Looks at a spot; starts the camera if it is not up. Quietly does nothing when it cannot.</summary>
        public static void Show(Ped me, Vector3 at)
        {
            try
            {
                if (_cam == 0)
                {
                    if (!CanShow(me)) return;

                    _cam = Function.Call<int>(Hash.CREATE_CAM, "DEFAULT_SCRIPTED_CAMERA", true);
                    if (_cam == 0) return;

                    Function.Call(Hash.SET_CAM_FOV, _cam, Fov);
                    Function.Call(Hash.SET_CAM_ACTIVE, _cam, true);
                    Function.Call(Hash.RENDER_SCRIPT_CAMS, true, true, HandBackMs, true, false);

                    _held = me;
                    _settleUntil = 0;
                    Function.Call(Hash.FREEZE_ENTITY_POSITION, me.Handle, true);

                    _spin = (Game.GameTime / 40) % 360;
                    _lastTick = Game.GameTime;
                }

                _at = at;
                _movedAt = Game.GameTime;

                Function.Call(Hash.SET_FOCUS_POS_AND_VEL, at.X, at.Y, at.Z, 0f, 0f, 0f);
                Function.Call(Hash.NEW_LOAD_SCENE_STOP);
                Function.Call(Hash.NEW_LOAD_SCENE_START_SPHERE, at.X, at.Y, at.Z, LoadRadius, 0);

                Place();
            }
            catch (Exception ex)
            {
                Log.Debug("Ride camera would not show: " + ex.Message);
                Stop();
            }
        }

        /// <summary>Every tick, up or not: the circling while it is up, and his ground coming back after.</summary>
        public static void Update()
        {
            if (_cam != 0)
            {
                try
                {
                    var now = Game.GameTime;
                    var dt = Math.Min(100, now - _lastTick) / 1000f;
                    _lastTick = now;

                    _spin += SpinPerSec * dt;
                    if (_spin >= 360f) _spin -= 360f;

                    Function.Call(Hash.HIDE_HUD_AND_RADAR_THIS_FRAME);
                    Place();
                }
                catch
                {
                    Stop();
                }

                return;
            }

            if (_settleUntil == 0) return;

            try
            {
                var back = _held == null || !_held.Exists()
                           || Function.Call<bool>(Hash.HAS_COLLISION_LOADED_AROUND_ENTITY, _held.Handle)
                           || Game.GameTime >= _settleUntil;

                if (!back) return;

                if (_held != null && _held.Exists())
                {
                    Function.Call(Hash.FREEZE_ENTITY_POSITION, _held.Handle, false);
                }
            }
            catch
            {
            }

            _settleUntil = 0;
            _held = null;
        }

        /// <summary>How much of the view has come up since the last jump, 0..1. Black during the jump itself.</summary>
        public static float Lifted()
        {
            if (_cam == 0) return 1f;

            var t = (Game.GameTime - _movedAt - JumpMs) / (float)LiftMs;

            return t < 0f ? 0f : t > 1f ? 1f : t;
        }

        /// <summary>Hands the camera back. He stays frozen until his own ground has streamed in (Update).</summary>
        public static void Stop()
        {
            if (_cam == 0) return;

            var cam = _cam;
            _cam = 0;

            try
            {
                Function.Call(Hash.NEW_LOAD_SCENE_STOP);
                Function.Call(Hash.CLEAR_FOCUS);
                Function.Call(Hash.RENDER_SCRIPT_CAMS, false, true, HandBackMs, true, false);
                Function.Call(Hash.SET_CAM_ACTIVE, cam, false);
                Function.Call(Hash.DESTROY_CAM, cam, false);

                if (_held != null && _held.Exists())
                {
                    var p = _held.Position;
                    Function.Call(Hash.REQUEST_COLLISION_AT_COORD, p.X, p.Y, p.Z);
                }
            }
            catch
            {
            }

            _settleUntil = Game.GameTime + SettleMostMs;
        }

        /// <summary>Everything let go at once. For teardown, where there is no next tick to settle in.</summary>
        public static void Sweep()
        {
            Stop();

            try
            {
                Function.Call(Hash.CLEAR_FOCUS);

                var me = _held != null && _held.Exists() ? _held : Game.Player.Character;
                if (me != null && me.Exists()) Function.Call(Hash.FREEZE_ENTITY_POSITION, me.Handle, false);
            }
            catch
            {
            }

            _settleUntil = 0;
            _held = null;
        }

        private static void Place()
        {
            var rad = _spin * (float)Math.PI / 180f;

            var look = new Vector3(_at.X, _at.Y, _at.Z + LookUp);
            var eye = new Vector3(_at.X + (float)Math.Sin(rad) * Around,
                                  _at.Y + (float)Math.Cos(rad) * Around,
                                  _at.Z + Up);

            // Pulled in when a building is in the way. The flats are dense, and a camera
            // twenty-four metres out is inside somebody's kitchen more often than not.
            try
            {
                var hit = World.Raycast(look, eye, IntersectFlags.Map);

                if (hit.DidHit)
                {
                    var toward = look - hit.HitPosition;
                    toward.Normalize();
                    eye = hit.HitPosition + toward * 1.2f;
                }
            }
            catch
            {
            }

            Function.Call(Hash.SET_CAM_COORD, _cam, eye.X, eye.Y, eye.Z);
            Function.Call(Hash.POINT_CAM_AT_COORD, _cam, look.X, look.Y, look.Z);
        }
    }
}
