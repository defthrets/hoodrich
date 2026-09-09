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

        /// <summary>Set by Main: whether a handle belongs to a placed scene. See Tidy.</summary>
        public Func<int, bool> Scenery;

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

        /// <summary>
        /// How far past the marker ours sits, and which way.
        ///
        /// NEGATIVE, WHICH MEANS THE FAR SIDE. Measured towards the camera it landed on the
        /// near lip of the crate with the placed rifle behind it; measured away from the
        /// camera it sits properly on the top with the rifle in front of it, which is the
        /// arrangement of a thing being shown to somebody across a counter.
        /// </summary>
        private const float InFront = -0.34f;
        private const float MarkerNear = 4f;

        private readonly List<int> _made = new List<int>();

        private int _object;
        private uint _weapon;
        private string _fitted = "";

        /// <summary>
        /// What has been asked for but not built yet, and when the asking started.
        ///
        /// A WEAPON YOU DO NOT OWN HAS NO MODEL IN MEMORY. The game streams in the models for
        /// the guns you are carrying and nothing else -- so CREATE_WEAPON_OBJECT succeeded for
        /// the Combat Pistol and the Micro SMG, which are his, and quietly returned nothing for
        /// the Vintage, the Double Action, the Mk II, the Double Barrel, the Pipe Bomb and
        /// every melee weapon on the shelf, which are not. From the crate it looked like half
        /// the shop had no gun in it.
        ///
        /// THE MODEL HAS TO BE ASKED FOR AND THEN WAITED ON, and waiting is the awkward half:
        /// the blocking form of that request yields the script, and this is called from a draw.
        /// So it is asked for here and built on whichever later frame it turns up, which from
        /// the outside is a gun that appears a beat after you scroll onto it.
        /// </summary>
        private uint _want;
        private List<uint> _wantParts;
        private string _wantKey = "";
        private int _asked;
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

                    // HANDED BACK ON THE SPOT, WITH NO EASE. It used to ask for a half second
                    // interpolation home and then destroy the camera in the same breath -- so
                    // the game was left interpolating away from a camera that no longer
                    // existed, and the view stopped where it was. There is no fixing that with
                    // a shorter ease; the ease is the bug.
                    //
                    // Cut instead. A cut back to the player is a frame nobody minds; a camera
                    // that never comes back is the end of the session.
                    Function.Call(Hash.SET_CAM_ACTIVE, _cam, false);
                    Function.Call(Hash.RENDER_SCRIPT_CAMS, false, false, 0, true, false);
                    Function.Call(Hash.DESTROY_CAM, _cam, true);

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
            if (_object == 0 && weapon == _want && key == _wantKey) return;

            Clear();

            if (weapon == 0 || Bench == null) return;

            // Wanted rather than made. Build does the making, once the model has arrived.
            _want = weapon;
            _wantParts = parts;
            _wantKey = key;
            _asked = 0;
        }

        /// <summary>
        /// Builds it once the game has the model, and gives up quietly if it never arrives.
        ///
        /// GIVING UP MATTERS. A weapon this build of the game does not have would otherwise be
        /// requested on every frame the counter is open, for ever, and the shelf would sit
        /// empty with nothing in the log to say why.
        /// </summary>
        private void Build()
        {
            if (_object != 0 || _want == 0 || Bench == null) return;

            var now = Game.GameTime;

            if (_asked == 0) _asked = now;

            try
            {
                var model = Function.Call<int>(Hash.GET_WEAPONTYPE_MODEL, _want);

                if (model == 0)
                {
                    _want = 0;
                    return;
                }

                if (!Function.Call<bool>(Hash.HAS_MODEL_LOADED, model))
                {
                    Function.Call(Hash.REQUEST_MODEL, model);

                    if (now - _asked > WaitMs)
                    {
                        Log.Info("Gun counter: the game would not load the model for that one.");
                        _want = 0;
                    }

                    return;
                }

                Make(model);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not ask for a gun model: " + ex.Message);
                _want = 0;
            }
        }

        /// <summary>How long the model is waited on before that one is given up as absent.</summary>
        private const int WaitMs = 4000;

        private void Make(int model)
        {
            var weapon = _want;
            var parts = _wantParts;
            var key = _wantKey;

            try
            {
                var at = Where();

                _object = Function.Call<int>(Hash.CREATE_WEAPON_OBJECT, weapon, 0,
                                             at.X, at.Y, at.Z, true, 1.0f, 0);

                // HANDED BACK EITHER WAY. A model asked for and never released is memory this
                // mod is holding for the rest of the session.
                Function.Call(Hash.SET_MODEL_AS_NO_LONGER_NEEDED, model);

                if (_object == 0)
                {
                    _want = 0;
                    return;
                }

                // EVERY ONE EVER MADE, so none of them can be lost. See Sweep.
                _made.Add(_object);

                _weapon = weapon;
                _fitted = key;

                _want = 0;
                _asked = 0;

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
            // The one place per frame anything is driven from, so the waiting for a model that
            // is still streaming happens here too.
            Build();

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
                // NOT RELEASED FIRST. IsPersistent = false is SET_ENTITY_AS_NO_LONGER_NEEDED,
                // which hands the thing back to the game -- and a thing the game owns is not
                // ours to delete any more, so the delete that followed it did nothing. That is
                // why they piled up in the air: every one of them was politely handed over and
                // then asked to leave by somebody with no standing to ask.
                //
                // Delete outright. Entity.Delete claims it and removes it, in that order.
                var thing = Entity.FromHandle(_object);

                if (thing != null && thing.Exists()) thing.Delete();
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

            _want = 0;
            _wantParts = null;
            _wantKey = "";
            _asked = 0;

            Sweep();
        }

        /// <summary>
        /// Weapons left on and around the bench by an EARLIER RUN of the script, taken away.
        ///
        /// THE LIST OF WHAT WE MADE DIES WITH THE SCRIPT. Sweep can only clear handles this
        /// instance created, so anything a previous one left -- and the version before the
        /// delete was fixed left a lot -- is an orphan that nothing points at. Reloading does
        /// not help: the old instance tears down with the old broken code, and the new one has
        /// never heard of them.
        ///
        /// So this looks rather than remembers: any object near the bench whose model is one of
        /// the guns on the shelf, that is not ours this second and was not put there by a
        /// scene. That last test is the one that matters -- the rifle laid on that crate by hand
        /// IS a weapon object, sitting exactly where the tidying happens, and Scenery is the
        /// only thing that can tell it from a stray.
        ///
        /// Run once when the counter opens. It is a look at a dozen props, not a sweep of the
        /// city, and it happens on the frame the camera is already moving.
        /// </summary>
        public void Tidy(IEnumerable<uint> catalogue)
        {
            if (catalogue == null || Bench == null) return;

            try
            {
                var models = new HashSet<int>();

                foreach (var weapon in catalogue)
                {
                    if (weapon == 0) continue;

                    var m = Function.Call<int>(Hash.GET_WEAPONTYPE_MODEL, weapon);
                    if (m != 0) models.Add(m);
                }

                if (models.Count == 0) return;

                var gone = 0;

                foreach (var thing in World.GetNearbyProps(Bench(), TidyRange))
                {
                    if (thing == null || !thing.Exists()) continue;
                    if (thing.Handle == _object) continue;
                    if (!models.Contains(thing.Model.Hash)) continue;
                    if (Scenery != null && Scenery(thing.Handle)) continue;

                    thing.Delete();
                    gone++;
                }

                if (gone > 0) Log.Info("Gun counter: cleared " + gone + " gun(s) left over from an earlier run.");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not tidy the bench: " + ex.Message);
            }
        }

        /// <summary>How far around the bench is tidied. The crate and its immediate air.</summary>
        private const float TidyRange = 12f;

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

                    // Outright, for the reason Clear gives.
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
