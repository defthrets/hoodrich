using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.UI
{
    /// <summary>
    /// The gun on the bench in front of Stretch, with the camera on it.
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
    /// And it shows the parts ON: scroll onto a suppressor and the suppressor is on it, because
    /// the component is fitted to the object rather than drawn beside it.
    ///
    /// IT IS ON A TABLE AND THE CAMERA GOES TO IT, which is the third arrangement of this and
    /// the first one that is not fighting the engine. The first two floated the gun in front of
    /// the gameplay camera and cut a hole in the panel to see it through -- because 2D is
    /// composited after the world, and nothing 3D can ever be drawn in front of a rectangle.
    /// That worked, and it was always a workaround: the hole had to be filled with a painted
    /// backdrop, and anything a metre from the camera sits inside the near depth of field, so
    /// the gun came out soft while the shop behind it was sharp.
    ///
    /// A bench answers all of it at once. The gun lies on a real table in real light, the panel
    /// moves aside instead of being cut open, and the camera is OURS -- pointed at the table
    /// from a fixed spot, at a distance the game is happy to focus at.
    ///
    /// WHERE THE BENCH IS COMES FROM THE INI, because a coordinate can only honestly come from
    /// somebody who has stood on it. Until one is written down the default is a guess in front
    /// of Stretch, and a guess is why it is a setting.
    /// </summary>
    internal sealed class GunModel
    {
        /// <summary>Degrees a second. Slow enough to read the silhouette, quick enough to be alive.</summary>
        private const float Spin = 26f;

        /// <summary>
        /// Lying flat, at the angle a weapon model has to be held at to lie flat.
        ///
        /// NOT GUESSED. These are read straight off the rifle Michael laid on that crate in
        /// Menyoo -- pitch -87.33, roll 74.54 -- because a weapon's model does not point the
        /// way you would expect and "rolled ninety degrees" puts it on its nose. He had
        /// already solved it by eye; this is his answer, written down.
        ///
        /// The YAW is what turns. With the model already flat, yaw is the spin about the
        /// table, which is the one that reads as a thing being shown to you.
        /// </summary>
        private const float LiePitch = -87.33f;
        private const float LieRoll = 74.54f;
        private const float LieYaw = 180f;

        /// <summary>
        /// Where the camera stands, worked out FROM the bench rather than asked for.
        ///
        /// ONE COORDINATE IS ONE WALK. Asking for a camera position as well would be two, and
        /// the second is the one nobody can judge while standing still -- so it is derived:
        /// back along the bench's own heading, up a little, looking down at the top of it.
        /// </summary>
        private const float CamBack = 1.05f;
        private const float CamUp = 0.42f;
        private const float CamFov = 40f;

        /// <summary>How long the camera takes to go, and to come back.</summary>
        private const int EaseMs = 550;

        /// <summary>Set by Main: where the bench is, and which way it faces.</summary>
        public Func<Vector3> Bench;
        public Func<float> Facing;

        /// <summary>
        /// The rifle Michael laid on that crate, and how far in front of it ours sits.
        ///
        /// THE MARKER IS BETTER THAN THE COORDINATE. The bench in the ini is a number somebody
        /// worked out; the rifle is a thing standing on the crate, put there by hand, at
        /// exactly the height the crate's top actually is. Finding it and measuring from it
        /// cannot be off by the half metre an arithmetic guess was off by -- and if the crate
        /// is ever moved, the gun moves with it and nothing needs editing.
        ///
        /// The ini is still the fallback, for an install where the scene did not load.
        /// </summary>
        private const string Marker = "w_ar_assaultrifle";
        private const float InFront = 0.34f;
        private const float MarkerNear = 4f;

        private readonly List<int> _made = new List<int>();

        private int _object;
        private uint _weapon;
        private string _fitted = "";
        private float _turn;
        private int _lastAt;

        private int _cam;

        public bool Live => _object != 0;

        /// <summary>
        /// Where the gun actually goes: in front of the rifle on the crate, if it is there.
        ///
        /// OUR OWN OBJECT IS EXCLUDED, and it has to be -- pick the Assault Rifle off the shelf
        /// and the thing being placed is the same model as the marker, so without this it would
        /// find itself and walk a third of a metre forward every frame until it was in the road.
        /// </summary>
        private Vector3 Where()
        {
            var at = Bench == null ? Vector3.Zero : Bench();

            try
            {
                var want = new Model(Marker);
                var best = Vector3.Zero;
                var gap = MarkerNear;

                foreach (var prop in World.GetNearbyProps(at, MarkerNear))
                {
                    if (prop == null || !prop.Exists()) continue;
                    if (prop.Handle == _object) continue;
                    if (prop.Model != want) continue;

                    var d = prop.Position.DistanceTo(at);
                    if (d > gap) continue;

                    gap = d;
                    best = prop.Position;
                }

                if (best == Vector3.Zero) return at;

                var face = Facing == null ? 0f : Facing();
                var rad = face * (float)Math.PI / 180f;

                // The way the camera looks from, which is the side "in front of it" means.
                var toward = new Vector3((float)Math.Sin(rad), (float)-Math.Cos(rad), 0f);

                return best + toward * InFront;
            }
            catch
            {
                return at;
            }
        }

        // ---- the camera ----------------------------------------------------------------

        /// <summary>
        /// Onto the bench, and back to the game afterwards.
        ///
        /// A SCRIPTED CAMERA RATHER THAN THE GAMEPLAY ONE. The gameplay camera is the player's
        /// and cannot be told where to focus, which is what made the last arrangement blurry.
        /// This one is ours, so the gun is framed at a distance the game keeps sharp, and the
        /// shop is behind it rather than the gun being in front of the shop.
        /// </summary>
        public void Watch(bool on)
        {
            try
            {
                if (!on)
                {
                    if (_cam == 0) return;

                    Function.Call(Hash.RENDER_SCRIPT_CAMS, false, true, EaseMs, true, false);
                    Function.Call(Hash.DESTROY_CAM, _cam, false);

                    _cam = 0;
                    return;
                }

                if (_cam != 0 || Bench == null) return;

                var at = Where();
                var face = Facing == null ? 0f : Facing();

                var rad = face * (float)Math.PI / 180f;

                // Backwards along the bench's own heading: the side somebody stands to look
                // at it.
                var back = new Vector3((float)Math.Sin(rad), (float)-Math.Cos(rad), 0f);

                var eye = at + back * CamBack + new Vector3(0f, 0f, CamUp);

                _cam = Function.Call<int>(Hash.CREATE_CAM_WITH_PARAMS, "DEFAULT_SCRIPTED_CAMERA",
                                          eye.X, eye.Y, eye.Z, 0f, 0f, 0f, CamFov, true, 2);

                if (_cam == 0) return;

                Function.Call(Hash.POINT_CAM_AT_COORD, _cam, at.X, at.Y, at.Z + 0.04f);
                Function.Call(Hash.SET_CAM_ACTIVE, _cam, true);
                Function.Call(Hash.RENDER_SCRIPT_CAMS, true, true, EaseMs, true, false);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not put the camera on the bench: " + ex.Message);
            }
        }

        // ---- the gun -------------------------------------------------------------------

        /// <summary>
        /// The weapon to show, and everything bolted to it.
        ///
        /// REBUILT WHEN THE ANSWER CHANGES AND NOT OTHERWISE. Creating a weapon object is not
        /// free and the parts list is walked every frame the screen is open, so the components
        /// are joined into a key and compared -- a frame that wants the same gun with the same
        /// parts on it keeps the object it already has, turning.
        /// </summary>
        public void Show(uint weapon, List<uint> parts)
        {
            var key = Key(parts);

            if (_object != 0 && weapon == _weapon && key == _fitted) return;

            Clear();

            if (weapon == 0 || Bench == null) return;

            try
            {
                var at = Where();

                _object = Function.Call<int>(Hash.CREATE_WEAPON_OBJECT, weapon, 0,
                                             at.X, at.Y, at.Z, true, 1.0f, 0);

                if (_object == 0) return;

                // EVERY ONE EVER MADE, so none of them can be lost. See Sweep.
                _made.Add(_object);

                _weapon = weapon;
                _fitted = key;

                Function.Call(Hash.SET_ENTITY_COLLISION, _object, false, false);
                Function.Call(Hash.FREEZE_ENTITY_POSITION, _object, true);
                Function.Call(Hash.SET_ENTITY_INVINCIBLE, _object, true);
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
                Log.Debug("Could not put a gun on the bench: " + ex.Message);
                Clear();
            }
        }

        /// <summary>On its side on the bench, turning where it lies.</summary>
        public void Turn()
        {
            if (_object == 0 || Bench == null) return;

            var now = Game.GameTime;

            var step = _lastAt == 0 ? 0f : (now - _lastAt) * 0.001f;
            _lastAt = now;

            _turn = (_turn + Spin * step) % 360f;

            try
            {
                var at = Where();

                Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, _object, at.X, at.Y, at.Z,
                              false, false, false);

                Function.Call(Hash.SET_ENTITY_ROTATION, _object,
                              LiePitch, LieRoll, LieYaw + _turn, 2, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not turn the gun on the bench: " + ex.Message);
            }
        }

        public void Clear()
        {
            Sweep();

            if (_object == 0) return;

            try
            {
                // THROUGH THE ENTITY, NOT THE NATIVE. DELETE_OBJECT wants a POINTER to the
                // handle and zeroes it -- handing it a fresh OutputArgument built from a copy
                // deletes nothing and reports nothing, which is a rifle left lying on a bench
                // with no line in the log to say so.
                var thing = Entity.FromHandle(_object);

                if (thing != null && thing.Exists())
                {
                    thing.IsPersistent = false;
                    thing.Delete();
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not take the gun off the bench: " + ex.Message);
            }

            _made.Remove(_object);

            _object = 0;
            _weapon = 0;
            _fitted = "";
            _lastAt = 0;

            Sweep();
        }

        /// <summary>Everything off the bench and the camera back to the player.</summary>
        public void Stand()
        {
            Watch(false);
            Clear();
        }

        /// <summary>
        /// Anything ever made that is still lying there, taken away.
        ///
        /// THE FIELD IS NOT ENOUGH ON ITS OWN. _object is one handle, and anything that
        /// overwrites it without deleting first strands a rifle with nothing left pointing at
        /// it. A list of everything ever made is swept on every clear, so the worst case is a
        /// gun on the bench until the counter is next used rather than until the game is shut.
        /// </summary>
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
