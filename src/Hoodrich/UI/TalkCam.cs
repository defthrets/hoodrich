using System;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.UI
{
    /// <summary>
    /// The camera for a conversation with somebody who is stood there: over his shoulder,
    /// on their face, the way every scene of two people talking is shot.
    ///
    /// ONE SHOT, WITH A CUT ON EVERY LINE. It sits behind one of his shoulders and looks at
    /// the other person's head, pushing in slowly while they talk; each new line of theirs
    /// swaps it to the other shoulder, which is the shot / reverse-shot rhythm a
    /// conversation has on screen without anybody having to move. Shallow depth of field so
    /// the street behind them goes soft. Pulled in off walls by a ray, so a talk in a
    /// doorway does not put the camera in the brickwork.
    ///
    /// Only when there is a person: a phone call has no shoulder to look over and keeps
    /// the game's own camera.
    /// </summary>
    internal static class TalkCam
    {
        private static int _cam;
        private static Ped _me;
        private static Ped _them;
        private static float _side = 1f;
        private static int _cutAt;
        private static int _lastTick;
        private static Vector3 _eye;
        private static Vector3 _look;
        private static bool _placed;

        private const int Head = 31086;
        private const float Fov = 38f;
        private const float BackFar = 1.05f;
        private const float BackNear = 0.78f;
        private const int PushMs = 12000;
        private const float Shoulder = 0.52f;
        private const float Rise = 0.10f;
        private const float OffCentre = 0.14f;
        private const float Closest = 0.35f;
        private const int HandBackMs = 450;
        private const int CutEveryMs = 1200;
        private const float Follow = 7f;

        public static bool Up => _cam != 0;

        public static void Start(Ped me, Ped them)
        {
            if (Up) return;

            try
            {
                if (me == null || !me.Exists() || !me.IsAlive) return;
                if (them == null || !them.Exists() || !them.IsAlive) return;
                if (Boxed(me) || Boxed(them)) return;
                if (Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE)) return;
                if (!Function.Call<bool>(Hash.IS_SCREEN_FADED_IN)) return;

                _me = me;
                _them = them;
                _side = (Game.GameTime & 1) == 0 ? 1f : -1f;
                _cutAt = Game.GameTime;
                _lastTick = Game.GameTime;
                _placed = false;

                _cam = Function.Call<int>(Hash.CREATE_CAM, "DEFAULT_SCRIPTED_CAMERA", true);
                if (_cam == 0) return;

                Function.Call(Hash.SET_CAM_FOV, _cam, Fov);
                Function.Call(Hash.SET_CAM_USE_SHALLOW_DOF_MODE, _cam, true);
                Function.Call(Hash.SET_CAM_NEAR_DOF, _cam, 0.4f);
                Function.Call(Hash.SET_CAM_FAR_DOF, _cam, 3.5f);
                Function.Call(Hash.SET_CAM_DOF_STRENGTH, _cam, 0.8f);

                Place(true);

                Function.Call(Hash.SET_CAM_ACTIVE, _cam, true);
                Function.Call(Hash.RENDER_SCRIPT_CAMS, true, true, HandBackMs, true, false);
            }
            catch (Exception ex)
            {
                Log.Debug("Talk camera would not start: " + ex.Message);
                Stop();
            }
        }

        /// <summary>A new line from them: the other shoulder, unless the last cut was a moment ago.</summary>
        public static void Cut()
        {
            if (!Up) return;
            if (Game.GameTime - _cutAt < CutEveryMs) return;

            _side = -_side;
            _cutAt = Game.GameTime;
            _placed = false;
        }

        public static void Update()
        {
            if (!Up) return;

            try
            {
                if (_me == null || !_me.Exists() || !_me.IsAlive || _them == null || !_them.Exists()
                    || Boxed(_me))
                {
                    Stop();
                    return;
                }

                Function.Call(Hash.SET_USE_HI_DOF);
                Place(false);
            }
            catch
            {
                Stop();
            }
        }

        public static void Stop()
        {
            if (_cam == 0) return;

            var cam = _cam;
            _cam = 0;

            try
            {
                Function.Call(Hash.RENDER_SCRIPT_CAMS, false, true, HandBackMs, true, false);
                Function.Call(Hash.SET_CAM_ACTIVE, cam, false);
                Function.Call(Hash.DESTROY_CAM, cam, false);
            }
            catch
            {
            }

            _me = null;
            _them = null;
        }

        /// <summary>
        /// A car has a roof between the camera and the face; a bike does not. Lamar delivers
        /// a job sat on a bicycle with you sat on another, and that is a scene like any
        /// other -- the shot goes over a shoulder that happens to be on a saddle.
        /// </summary>
        private static bool Boxed(Ped who)
        {
            try
            {
                if (!who.IsInVehicle()) return false;

                var ride = who.CurrentVehicle;
                if (ride == null || !ride.Exists()) return true;

                var model = ride.Model;
                return !(model.IsBicycle || model.IsBike || model.IsQuadBike);
            }
            catch
            {
                return true;
            }
        }

        private static void Place(bool snap)
        {
            var now = Game.GameTime;
            var dt = Math.Min(100, now - _lastTick) / 1000f;
            _lastTick = now;

            var mine = Function.Call<Vector3>(Hash.GET_PED_BONE_COORDS, _me.Handle, Head, 0f, 0f, 0f);
            var theirs = Function.Call<Vector3>(Hash.GET_PED_BONE_COORDS, _them.Handle, Head, 0f, 0f, 0f);

            var to = theirs - mine;
            to.Z = 0f;
            if (to.Length() < 0.2f) to = new Vector3(0f, 1f, 0f);
            to.Normalize();

            var right = new Vector3(to.Y, -to.X, 0f);

            // Pushing in over the length of the shot, and starting again on every cut.
            var t = Math.Min(1f, (now - _cutAt) / (float)PushMs);
            var back = BackFar + (BackNear - BackFar) * (t * t * (3f - 2f * t));

            var eye = mine - to * back + right * (_side * Shoulder) + new Vector3(0f, 0f, Rise);
            var look = theirs + right * (_side * OffCentre);

            // Off the wall behind him, if there is one.
            try
            {
                var hit = World.Raycast(mine, eye, IntersectFlags.Map);
                if (hit.DidHit)
                {
                    var pull = eye - mine;
                    var room = Math.Max(Closest, hit.HitPosition.DistanceTo(mine) - 0.15f);
                    pull.Normalize();
                    eye = mine + pull * room;
                }
            }
            catch
            {
            }

            if (snap || !_placed)
            {
                _eye = eye;
                _look = look;
                _placed = true;
            }
            else
            {
                // Eased, so a man shifting his weight does not shake the shot.
                var k = Math.Min(1f, dt * Follow);
                _eye += (eye - _eye) * k;
                _look += (look - _look) * k;
            }

            Function.Call(Hash.SET_CAM_COORD, _cam, _eye.X, _eye.Y, _eye.Z);
            Function.Call(Hash.POINT_CAM_AT_COORD, _cam, _look.X, _look.Y, _look.Z);
        }
    }
}
