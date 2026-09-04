using System;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Economy
{
    /// <summary>
    /// The camera that swings round him while he takes something.
    ///
    /// A DRUG IS THE ONE THING IN THIS MOD YOU DO TO YOURSELF, and from behind his own
    /// shoulder it is a man fidgeting. The animation is on his hands and his face and the
    /// gameplay camera is looking at the back of his head -- so the work that went into
    /// finding the right clip is spent on something nobody can see.
    ///
    /// So it goes round the front for as long as it takes, and hands the camera straight back.
    ///
    /// A SLOW ARC RATHER THAN A CUT. It comes in off his shoulder, swings maybe seventy
    /// degrees round the front and pushes in a little while it goes, which is the whole
    /// grammar: a cut says "a thing happened", a move says "watch this for a moment". It ends
    /// by handing control back with an ease rather than snapping, because a snap at the end of
    /// a smooth move is the only frame anybody will remember.
    ///
    /// EVERY EXIT RELEASES IT. That is the entire risk here -- a script camera left running is
    /// a player who cannot see, cannot fight and cannot get it back without reloading. So Stop
    /// is idempotent, it is called from the ritual finishing, from the ritual being cut short,
    /// from the mod being switched off and from the teardown, and Update itself gives up and
    /// releases if the ritual ever outstays the ceiling.
    /// </summary>
    internal static class RitualCam
    {
        private static int _cam;
        private static int _until;
        private static int _from;

        private static float _sweep;
        private static float _close;

        /// <summary>Where it starts, relative to the way he is facing, and how far it travels.</summary>
        private const float StartBehind = 155f;
        private const float Sweep = 70f;

        /// <summary>How far out it starts and finishes, and how high up his body it looks.</summary>
        private const float FarOut = 2.1f;
        private const float CloseIn = 1.35f;
        private const float Rise = 0.35f;

        private const float Fov = 38f;

        /// <summary>SKEL_Head. Pointed at his face rather than his feet.</summary>
        private const int Head = 31086;

        /// <summary>How long the hand-back takes, and the ceiling nothing may outlast.</summary>
        private const int HandBackMs = 700;
        private const int MostMs = 12000;

        public static bool Up => _cam != 0;

        /// <summary>
        /// Take the camera, if this is a moment worth taking it for.
        ///
        /// Refused in a car, in the air, in a cutscene or while he is fighting -- all of them
        /// are cases where the player wants the camera he has, and taking it is somewhere
        /// between rude and dangerous.
        /// </summary>
        public static void Start(Ped me, int ms, bool wanted)
        {
            if (!wanted || Up) return;

            try
            {
                if (me == null || !me.Exists() || !me.IsAlive) return;

                if (me.IsInVehicle() || me.IsInAir || me.IsRagdoll || me.IsInCombat) return;

                if (Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE)) return;
                if (!Function.Call<bool>(Hash.IS_SCREEN_FADED_IN)) return;

                _from = Game.GameTime;
                _until = _from + Math.Min(MostMs, Math.Max(1200, ms));

                // Which way round he gets filmed. Both look fine and alternating stops a run
                // of them reading as one repeated shot.
                _sweep = (Game.GameTime & 1) == 0 ? Sweep : -Sweep;
                _close = 0f;

                _cam = Function.Call<int>(Hash.CREATE_CAM, "DEFAULT_SCRIPTED_CAMERA", true);

                if (_cam == 0) return;

                Function.Call(Hash.SET_CAM_FOV, _cam, Fov);

                Place(me, 0f);

                // At the head, so the shot is about his face and not his belt.
                Function.Call(Hash.POINT_CAM_AT_PED_BONE, _cam, me.Handle, Head, 0f, 0f, 0f, true);

                Function.Call(Hash.SET_CAM_ACTIVE, _cam, true);
                Function.Call(Hash.RENDER_SCRIPT_CAMS, true, true, HandBackMs, true, false);
            }
            catch (Exception ex)
            {
                Log.Debug("Ritual camera would not start: " + ex.Message);
                Stop();
            }
        }

        /// <summary>Called every frame while it is up. Moves it, and gives up if it overruns.</summary>
        public static void Update(Ped me)
        {
            if (!Up) return;

            try
            {
                if (me == null || !me.Exists() || !me.IsAlive || me.IsInVehicle())
                {
                    Stop();
                    return;
                }

                var now = Game.GameTime;

                if (now >= _until)
                {
                    Stop();
                    return;
                }

                var span = Math.Max(1, _until - _from);
                var t = (now - _from) / (float)span;

                if (t < 0f) t = 0f;
                if (t > 1f) t = 1f;

                // Eased at both ends. A linear orbit is a turntable; this starts slowly, gets
                // on with it, and settles rather than stopping.
                var eased = t * t * (3f - 2f * t);

                _close = eased;

                Place(me, eased);
            }
            catch
            {
                Stop();
            }
        }

        /// <summary>Hands it back. Safe to call at any time, including when it never started.</summary>
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
                // The render flag above is the one that matters and it is already off.
            }
        }

        /// <summary>Puts it where it belongs for a given point through the move.</summary>
        private static void Place(Ped me, float t)
        {
            var around = StartBehind + _sweep * t;

            var heading = me.Heading + around;

            var rad = heading * (float)Math.PI / 180f;

            var out_ = FarOut + (CloseIn - FarOut) * t;

            var at = me.Position;

            var x = at.X - (float)Math.Sin(rad) * out_;
            var y = at.Y + (float)Math.Cos(rad) * out_;

            // Off his root rather than off the ground, so a kerb does not put the shot in the
            // tarmac. Rises a touch as it closes, which is what stops it feeling like a dolly
            // on rails.
            var z = at.Z + Rise + 0.25f * t;

            Function.Call(Hash.SET_CAM_COORD, _cam, x, y, z);
        }
    }
}
