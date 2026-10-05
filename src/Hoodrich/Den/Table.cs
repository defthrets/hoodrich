using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.UI;

namespace Hoodrich.Den
{
    /// <summary>A named bone on a prop: where it is, and which way it faces.</summary>
    internal static class Bone
    {
        /// <summary>GET_WORLD_ROTATION_OF_ENTITY_BONE, which ScriptHookVDotNet 3.6 has no name for.</summary>
        private const ulong WorldRotation = 0xCE6294A232D03786;

        public static int Index(Entity e, string name)
        {
            if (e == null || !e.Exists()) return -1;

            try { return Function.Call<int>(Hash.GET_ENTITY_BONE_INDEX_BY_NAME, e.Handle, name); }
            catch { return -1; }
        }

        public static Vector3 Position(Entity e, int bone)
        {
            return Function.Call<Vector3>(Hash.GET_WORLD_POSITION_OF_ENTITY_BONE, e.Handle, bone);
        }

        public static Vector3 Rotation(Entity e, int bone)
        {
            return Function.Call<Vector3>((Hash)WorldRotation, e.Handle, bone);
        }
    }

    /// <summary>
    /// A chair at one of the casino's tables, found the way the casino finds it.
    ///
    /// THE CHAIRS ARE PART OF THE TABLE. The Diamond's roulette and poker tables carry their own
    /// four stools, and each one is a bone -- Chair_Base_01 to Chair_Base_04 -- at the spot and
    /// the facing a seated player's clips are authored from. So sitting down is a scene on that
    /// bone and nothing else: no offset to guess, and the chair moves with the table when
    /// Michael moves the table in Menyoo. Michael asked on 2026-09-26 for you to sit at the
    /// tables, the way you do in the casino.
    /// </summary>
    internal sealed class Seat
    {
        public static readonly string[] Bones = { "Chair_Base_01", "Chair_Base_02", "Chair_Base_03", "Chair_Base_04" };

        /// <summary>Which of the four, 1 to 4, by the bone's own number.</summary>
        public int Number;

        public Vector3 At;
        public Vector3 Rot;

        /// <summary>Flat, from the chair to the middle of its table: the way a man sat in it looks.</summary>
        public Vector3 Facing;

        /// <summary>The nearest of a table's chairs within reach, leaving out one somebody is already sat in (0 leaves out none).</summary>
        public static Seat Nearest(Entity table, Vector3 from, float reach, int taken = 0)
        {
            if (table == null || !table.Exists()) return null;

            Seat best = null;
            var bestD = reach;

            for (var i = 0; i < Bones.Length; i++)
            {
                if (i + 1 == taken) continue;

                var bone = Bone.Index(table, Bones[i]);
                if (bone < 0) continue;

                Vector3 at;

                try { at = Bone.Position(table, bone); }
                catch { continue; }

                var dx = at.X - from.X;
                var dy = at.Y - from.Y;
                var d = (float)Math.Sqrt(dx * dx + dy * dy);
                if (d > bestD) continue;

                bestD = d;

                var centre = table.Position - at;
                centre.Z = 0f;
                if (centre.Length() > 0.01f) centre.Normalize();

                best = new Seat { Number = i + 1, At = at, Rot = Bone.Rotation(table, bone), Facing = centre };
            }

            return best;
        }

        /// <summary>The one of a table's chairs furthest from a point: the far end, from the way in.</summary>
        public static Seat Furthest(Entity table, Vector3 from)
        {
            if (table == null || !table.Exists()) return null;

            Seat best = null;
            var bestD = -1f;

            for (var i = 0; i < Bones.Length; i++)
            {
                var bone = Bone.Index(table, Bones[i]);
                if (bone < 0) continue;

                Vector3 at;

                try { at = Bone.Position(table, bone); }
                catch { continue; }

                var d = at.DistanceTo(from);
                if (d <= bestD) continue;

                bestD = d;

                var centre = table.Position - at;
                centre.Z = 0f;
                if (centre.Length() > 0.01f) centre.Normalize();

                best = new Seat { Number = i + 1, At = at, Rot = Bone.Rotation(table, bone), Facing = centre };
            }

            return best;
        }

        /// <summary>Whether this table has chairs of its own at all.</summary>
        public static bool Has(Entity table)
        {
            foreach (var b in Bones)
            {
                if (Bone.Index(table, b) >= 0) return true;
            }

            return false;
        }
    }

    /// <summary>
    /// The player in one of those chairs: down into it, sat there fidgeting the way people at a
    /// table do, one thing asked of him at a time -- a chip down, the cards picked up, a groan --
    /// and back up out of it. Every clip is the casino's own for a seated player and every one
    /// is a scene on the chair.
    /// </summary>
    internal sealed class Sitter
    {
        private enum Mode { Entering, Idle, Fidget, Once, Hold, Leaving, Up }

        private readonly Ped _me;
        private readonly Random _rng;
        private readonly string[] _fidgets;

        public readonly Seat Seat;

        private Mode _mode = Mode.Entering;
        private int _scene = -1;
        private bool _left;
        private string _idle;
        private int _fidgetAt;

        private const int FidgetMinMs = 11000;
        private const int FidgetMaxMs = 24000;

        public Sitter(Ped me, Seat seat, string idle, string[] fidgets, Random rng)
        {
            _me = me;
            Seat = seat;
            _idle = idle;
            _fidgets = fidgets ?? new string[0];
            _rng = rng;
        }

        /// <summary>A woman sat down: the casino has her own set of clips for most of it.</summary>
        public bool Female => _me != null && _me.Exists() && _me.Gender == Gender.Female;

        /// <summary>Sat in the chair and not in the middle of getting in or out of it.</summary>
        public bool Down => _mode == Mode.Idle || _mode == Mode.Fidget || _mode == Mode.Once || _mode == Mode.Hold;

        /// <summary>Something asked of him is still running.</summary>
        public bool Busy => _mode == Mode.Once && !Scene.Done(_scene);

        public bool Entering => _mode == Mode.Entering;

        /// <summary>Up and out of it, and the game can let him go.</summary>
        public bool Gone => _mode == Mode.Up;

        public int SceneId => _scene;

        public float Phase => Scene.Phase(_scene);

        /// <summary>Down into the chair, from whichever side of it he is stood.</summary>
        public void Enter()
        {
            try
            {
                var h = Seat.Rot.Z * (float)Math.PI / 180f;
                var right = new Vector3((float)Math.Cos(h), (float)Math.Sin(h), 0f);
                var from = _me.Position - Seat.At;
                _left = Vector3.Dot(from, right) < 0f;
            }
            catch
            {
                _left = true;
            }

            _scene = Scene.At(Seat.At, Seat.Rot);
            Scene.Player(_scene, _me, Scene.SharedPlayer, _left ? "sit_enter_left" : "sit_enter_right", false);
            _mode = Mode.Entering;
        }

        public void Update()
        {
            switch (_mode)
            {
                case Mode.Entering:
                case Mode.Once:
                case Mode.Fidget:
                    if (Scene.Done(_scene)) Idle();
                    break;

                case Mode.Idle:
                    if (!Scene.Running(_scene)) { Idle(); break; }
                    if (Game.GameTime >= _fidgetAt && _fidgets.Length > 0) Fidget();
                    break;

                case Mode.Leaving:
                    if (Scene.Done(_scene))
                    {
                        _mode = Mode.Up;
                        Let();
                    }

                    break;
            }
        }

        /// <summary>Sat, looping the table's idle. A new one given here is kept for the rest of the sitting.</summary>
        public void Idle(string clip = null)
        {
            if (!string.IsNullOrEmpty(clip)) _idle = clip;

            _scene = Scene.At(Seat.At, Seat.Rot);
            Scene.Player(_scene, _me, Scene.SharedPlayer, _idle, true);
            _mode = Mode.Idle;
            _fidgetAt = Game.GameTime + _rng.Next(FidgetMinMs, FidgetMaxMs);
        }

        private void Fidget()
        {
            var clip = _fidgets[_rng.Next(_fidgets.Length)];
            _scene = Scene.At(Seat.At, Seat.Rot);
            Scene.Player(_scene, _me, Scene.SharedPlayer, clip, false);
            _mode = Mode.Fidget;
        }

        /// <summary>
        /// One thing, once, and then back to the idle on its own. The scene is returned so the
        /// props that go with it -- the cards in his hand -- can be put in it the same frame.
        /// </summary>
        public int Once(string dict, string clip)
        {
            _scene = Scene.At(Seat.At, Seat.Rot);
            Scene.Player(_scene, _me, dict, clip, false);
            _mode = Mode.Once;
            return _scene;
        }

        /// <summary>
        /// A clip kept going -- looped, or held on its last frame -- until the next thing is asked
        /// of him: the cards held up in front of his face while he decides.
        /// </summary>
        public int Hold(string dict, string clip, bool loop)
        {
            _scene = Scene.At(Seat.At, Seat.Rot);
            Scene.Player(_scene, _me, dict, clip, loop, !loop);
            _mode = Mode.Hold;
            return _scene;
        }

        /// <summary>Up out of the chair, off the side he came in from.</summary>
        public void Stand()
        {
            if (_mode == Mode.Leaving || _mode == Mode.Up) return;

            _scene = Scene.At(Seat.At, Seat.Rot);
            Scene.Player(_scene, _me, Scene.SharedPlayer, _left ? "sit_exit_left" : "sit_exit_right", false);
            _mode = Mode.Leaving;
        }

        /// <summary>His tasks cleared: he is stood where the clip left him and his again.</summary>
        public void Let()
        {
            try
            {
                if (_me != null && _me.Exists()) Function.Call(Hash.CLEAR_PED_TASKS, _me.Handle);
            }
            catch { }

            _mode = Mode.Up;
        }
    }

    /// <summary>
    /// A camera of the table's own while you are sat at it: over the felt while the chips go
    /// down, on the wheel while the ball goes round, on the cards when they turn over. Each move
    /// is a new camera eased into from the last, which is how the casino does it.
    /// </summary>
    internal sealed class TableCam
    {
        /// <summary>SHAKE_CAM, which ScriptHookVDotNet 3.6 has no name for.</summary>
        private const ulong ShakeCam = 0x6A25241C340D3822;

        private int _cam;
        private readonly List<int> _old = new List<int>();
        private readonly List<int> _oldUntil = new List<int>();

        /// <summary>
        /// A camera you can look about with: the right stick or the mouse turns it from where it
        /// was put, as far as a man sat in a chair can turn his head. Michael asked for it at the
        /// blackjack on 2026-09-26 -- "not stuck in this view only".
        /// </summary>
        private bool _free;

        /// <summary>
        /// Whether a mouse turns it too. Not at the roulette while the bets are open: there the
        /// mouse is the chip in your hand, and a pad's left stick is, so only a pad looks about.
        /// </summary>
        private bool _mouse = true;

        private float _pitch0;
        private float _yaw0;
        private float _pitch;
        private float _yaw;

        /// <summary>
        /// How far round a man sat at a table turns his head, and how far up and down he looks --
        /// as angles the camera ends at, not as how far it moves, because the seat's own camera
        /// already starts sixty degrees down at the felt and "thirty-five up" from there never
        /// reached the dealer's face.
        /// </summary>
        private const float YawMost = 90f;
        private const float PitchLowest = -80f;
        private const float PitchHighest = 25f;

        public bool Up => _cam != 0;

        /// <summary>From one point at another, and free to look about from there. See _free.</summary>
        public void LookFree(Vector3 from, Vector3 at, float fov, int easeMs = 900, float shake = 0.1f, bool mouse = true)
        {
            try
            {
                var d = at - from;
                var flat = (float)Math.Sqrt(d.X * d.X + d.Y * d.Y);

                _pitch0 = (float)(Math.Atan2(d.Z, flat) * 180.0 / Math.PI);
                _yaw0 = (float)(Math.Atan2(-d.X, d.Y) * 180.0 / Math.PI);
                _pitch = 0f;
                _yaw = 0f;

                var cam = Function.Call<int>(Hash.CREATE_CAM_WITH_PARAMS, "DEFAULT_SCRIPTED_CAMERA",
                                             from.X, from.Y, from.Z, _pitch0, 0f, _yaw0, fov, false, 2);
                if (cam == 0) return;

                if (shake > 0f) Function.Call((Hash)ShakeCam, cam, "HAND_SHAKE", shake);

                Swap(cam, easeMs);
                _free = true;
                _mouse = mouse;
            }
            catch (Exception ex)
            {
                Log.Debug("Den: the table camera would not go up: " + ex.Message);
            }
        }

        private void Swap(int cam, int easeMs)
        {
            if (_cam == 0)
            {
                Function.Call(Hash.SET_CAM_ACTIVE, cam, true);
                Function.Call(Hash.RENDER_SCRIPT_CAMS, true, easeMs > 0, easeMs, true, false);
            }
            else
            {
                Function.Call(Hash.SET_CAM_ACTIVE_WITH_INTERP, cam, _cam, easeMs, 1, 1);
                _old.Add(_cam);
                _oldUntil.Add(Game.GameTime + easeMs + 250);
            }

            _cam = cam;
        }

        /// <summary>The look about, a frame at a time: a stick is a speed, a mouse a distance.</summary>
        private void Turn()
        {
            if (!_free || _cam == 0) return;

            try
            {
                var lx = Keys.MouseX;
                var ly = Keys.MouseY;

                float rate;

                if (Game.LastInputMethod == InputMethod.GamePad)
                {
                    if (Math.Abs(lx) < 0.15f) lx = 0f;
                    if (Math.Abs(ly) < 0.15f) ly = 0f;
                    rate = 110f * Game.LastFrameTime;
                }
                else
                {
                    if (!_mouse) return;
                    rate = 40f;
                }

                if (lx == 0f && ly == 0f) return;

                _yaw = Clamp(_yaw - lx * rate, -YawMost, YawMost);
                _pitch = Clamp(_pitch - ly * rate, PitchLowest - _pitch0, PitchHighest - _pitch0);

                Function.Call(Hash.SET_CAM_ROT, _cam, _pitch0 + _pitch, 0f, _yaw0 + _yaw, 2);
            }
            catch { }
        }

        private static float Clamp(float x, float lo, float hi)
        {
            return x < lo ? lo : x > hi ? hi : x;
        }

        /// <summary>From one point at another, eased over from wherever it was.</summary>
        public void Look(Vector3 from, Vector3 at, float fov, int easeMs = 900, float shake = 0.15f)
        {
            try
            {
                var cam = Function.Call<int>(Hash.CREATE_CAM_WITH_PARAMS, "DEFAULT_SCRIPTED_CAMERA",
                                             from.X, from.Y, from.Z, 0f, 0f, 0f, fov, false, 2);
                if (cam == 0) return;

                Function.Call(Hash.POINT_CAM_AT_COORD, cam, at.X, at.Y, at.Z);

                // The casino's own cameras breathe; a dead-still one is a security feed.
                if (shake > 0f) Function.Call((Hash)ShakeCam, cam, "HAND_SHAKE", shake);

                Swap(cam, easeMs);
                _free = false;
            }
            catch (Exception ex)
            {
                Log.Debug("Den: the table camera would not go up: " + ex.Message);
            }
        }

        /// <summary>Lets go of the cameras it has eased away from, and turns a free one.</summary>
        public void Update()
        {
            Turn();

            var now = Game.GameTime;

            for (var i = _old.Count - 1; i >= 0; i--)
            {
                if (now < _oldUntil[i]) continue;

                try { Function.Call(Hash.DESTROY_CAM, _old[i], false); }
                catch { }

                _old.RemoveAt(i);
                _oldUntil.RemoveAt(i);
            }
        }

        /// <summary>Back to the game's own camera. Safe to call when it never went up.</summary>
        public void Stop(int easeMs = 900)
        {
            if (_cam == 0 && _old.Count == 0) return;

            try { Function.Call(Hash.RENDER_SCRIPT_CAMS, false, easeMs > 0, easeMs, true, false); }
            catch { }

            try
            {
                if (_cam != 0)
                {
                    Function.Call(Hash.SET_CAM_ACTIVE, _cam, false);
                    Function.Call(Hash.DESTROY_CAM, _cam, false);
                }
            }
            catch { }

            foreach (var c in _old)
            {
                try { Function.Call(Hash.DESTROY_CAM, c, false); }
                catch { }
            }

            _old.Clear();
            _oldUntil.Clear();
            _cam = 0;
            _free = false;
        }
    }

    /// <summary>
    /// The casino's chips, on the felt where the bet is. One prop a spot: the chip of the biggest
    /// denomination the bet reaches, or a stack of them when it is more than one.
    /// </summary>
    internal static class Chips
    {
        public static readonly int[] Values = { 10, 50, 100, 500, 1000, 5000, 10000 };

        private static readonly string[] Names = { "10dollar", "50dollar", "100dollar", "500dollar", "1kdollar", "5kdollar", "10kdollar" };

        public static string ModelFor(int amount)
        {
            var i = 0;

            for (var k = 0; k < Values.Length; k++)
            {
                if (Values[k] <= amount) i = k;
            }

            return "vw_prop_chip_" + Names[i] + (amount > Values[i] ? "_st" : "_x1");
        }

        /// <summary>Every chip asked for on the way in, so the first bet is not the one that waits for them.</summary>
        public static void Ask()
        {
            foreach (var n in Names)
            {
                Models.Ready(new Model("vw_prop_chip_" + n + "_x1"));
                Models.Ready(new Model("vw_prop_chip_" + n + "_st"));
            }
        }

        /// <summary>A bet's chips on the table. Null while the model is still streaming: ask again next frame.</summary>
        public static Prop Put(Entity table, Vector3 local, int amount, float heading)
        {
            return Place(ModelFor(amount), table, local, heading);
        }

        /// <summary>A small prop set down on a table: flat, frozen, and with no body to be knocked about by.</summary>
        public static Prop Place(string name, Entity table, Vector3 local, float heading, bool visible = true)
        {
            if (table == null || !table.Exists()) return null;

            try
            {
                var model = new Model(name);
                if (!Models.Ready(model)) return null;

                var at = table.GetOffsetPosition(local);
                return Make(model, at, new Vector3(0f, 0f, heading), visible);
            }
            catch (Exception ex)
            {
                Log.Debug("Den: could not put " + name + " down: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// The same at a place in the world and a rotation of its own. Frozen unless told not to:
        /// a card or the ball has to be free for its clip to move it, and being made static rather
        /// than dynamic is what stops it falling through the table when nothing is.
        /// </summary>
        public static Prop Make(Model model, Vector3 at, Vector3 rot, bool visible = true, bool frozen = true)
        {
            var handle = Function.Call<int>(Hash.CREATE_OBJECT_NO_OFFSET, model.Hash, at.X, at.Y, at.Z, false, false, false);
            if (handle == 0) return null;

            var prop = GTA.Entity.FromHandle(handle) as Prop;
            if (prop == null) return null;

            Function.Call(Hash.SET_ENTITY_COLLISION, handle, false, false);
            if (frozen) Function.Call(Hash.FREEZE_ENTITY_POSITION, handle, true);
            Function.Call(Hash.SET_ENTITY_ROTATION, handle, rot.X, rot.Y, rot.Z, 2, true);
            if (!visible) Function.Call(Hash.SET_ENTITY_VISIBLE, handle, false, false);

            return prop;
        }

        public static void Gone(Prop p)
        {
            try
            {
                if (p != null && p.Exists()) p.Delete();
            }
            catch { }
        }

        public static void Show(Entity e, bool on)
        {
            try
            {
                if (e != null && e.Exists()) Function.Call(Hash.SET_ENTITY_VISIBLE, e.Handle, on, false);
            }
            catch { }
        }
    }

    /// <summary>
    /// The row of buttons along the bottom right that every table in the casino has: the game's
    /// own instructional_buttons movie, with the real key for each control drawn in it, so a pad
    /// shows a pad's buttons and a keyboard its keys.
    /// </summary>
    internal static class Buttons
    {
        private static int _movie;
        private static string _shown = "";

        /// <summary>Pairs of a control and what it does, rightmost first. Drawn this frame.</summary>
        public static void Show(params object[] pairs)
        {
            try
            {
                if (_movie == 0) _movie = Function.Call<int>(Hash.REQUEST_SCALEFORM_MOVIE, "instructional_buttons");
                if (_movie == 0 || !Function.Call<bool>(Hash.HAS_SCALEFORM_MOVIE_LOADED, _movie)) return;

                // Drawn again when he changes device, not only when the labels change: picking up a
                // pad left the keyboard's arrows in the row until something else changed it.
                var key = (UI.Draw.OnPad ? "pad|" : "keys|") + string.Join("|", pairs);

                if (key != _shown)
                {
                    _shown = key;

                    Method("CLEAR_ALL");

                    Function.Call(Hash.BEGIN_SCALEFORM_MOVIE_METHOD, _movie, "SET_CLEAR_SPACE");
                    Function.Call(Hash.SCALEFORM_MOVIE_METHOD_ADD_PARAM_INT, 200);
                    Function.Call(Hash.END_SCALEFORM_MOVIE_METHOD);

                    var slot = 0;

                    for (var i = 0; i + 1 < pairs.Length; i += 2)
                    {
                        var control = (Control)pairs[i];
                        var label = Core.Lang.T((string)pairs[i + 1]);
                        var button = Function.Call<string>(Hash.GET_CONTROL_INSTRUCTIONAL_BUTTONS_STRING, 2, (int)control, true);

                        Function.Call(Hash.BEGIN_SCALEFORM_MOVIE_METHOD, _movie, "SET_DATA_SLOT");
                        Function.Call(Hash.SCALEFORM_MOVIE_METHOD_ADD_PARAM_INT, slot++);
                        Function.Call(Hash.SCALEFORM_MOVIE_METHOD_ADD_PARAM_PLAYER_NAME_STRING, button);
                        Function.Call(Hash.BEGIN_TEXT_COMMAND_SCALEFORM_STRING, "STRING");
                        Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, label);
                        Function.Call(Hash.END_TEXT_COMMAND_SCALEFORM_STRING);
                        Function.Call(Hash.END_SCALEFORM_MOVIE_METHOD);
                    }

                    Function.Call(Hash.BEGIN_SCALEFORM_MOVIE_METHOD, _movie, "SET_BACKGROUND_COLOUR");
                    Function.Call(Hash.SCALEFORM_MOVIE_METHOD_ADD_PARAM_INT, 0);
                    Function.Call(Hash.SCALEFORM_MOVIE_METHOD_ADD_PARAM_INT, 0);
                    Function.Call(Hash.SCALEFORM_MOVIE_METHOD_ADD_PARAM_INT, 0);
                    Function.Call(Hash.SCALEFORM_MOVIE_METHOD_ADD_PARAM_INT, 80);
                    Function.Call(Hash.END_SCALEFORM_MOVIE_METHOD);

                    Function.Call(Hash.BEGIN_SCALEFORM_MOVIE_METHOD, _movie, "DRAW_INSTRUCTIONAL_BUTTONS");
                    Function.Call(Hash.SCALEFORM_MOVIE_METHOD_ADD_PARAM_INT, -1);
                    Function.Call(Hash.END_SCALEFORM_MOVIE_METHOD);
                }

                Function.Call(Hash.DRAW_SCALEFORM_MOVIE_FULLSCREEN, _movie, 255, 255, 255, 255, 0);
            }
            catch (Exception ex)
            {
                Log.Debug("Den: the button row would not draw: " + ex.Message);
            }
        }

        private static void Method(string name)
        {
            Function.Call(Hash.BEGIN_SCALEFORM_MOVIE_METHOD, _movie, name);
            Function.Call(Hash.END_SCALEFORM_MOVIE_METHOD);
        }

        /// <summary>Let go of the movie on the way out of the den.</summary>
        public static void Drop()
        {
            if (_movie == 0) return;

            try
            {
                var handle = new OutputArgument(_movie);
                Function.Call(Hash.SET_SCALEFORM_MOVIE_AS_NO_LONGER_NEEDED, handle);
            }
            catch { }

            _movie = 0;
            _shown = "";
        }
    }

    /// <summary>
    /// The black bars stacked above the buttons with what is on the table and what is in your
    /// pocket -- the game's own timer bars, the way every table in the casino shows them.
    /// </summary>
    internal static class Bars
    {
        private const string Dict = "timerbars";
        private const string Back = "all_black_bg";

        private const float CentreX = 0.9219f;
        private const float Width = 0.1563f;
        private const float Height = 0.0343f;
        private const float Bottom = 0.9245f;
        private const float Step = 0.0370f;
        private const float LabelRight = 0.906f;
        private const float ValueRight = 0.9896f;

        private static readonly Color Shade = Color.FromArgb(170, 255, 255, 255);

        /// <summary>Rows bottom up: a label, a value, and the ink for the value.</summary>
        public static void Draw(params object[] rows)
        {
            var ready = UI.Draw.EnsureTextureDict(Dict);
            var line = 0;

            for (var i = 0; i + 1 < rows.Length; i += 3)
            {
                var label = rows[i] as string;
                var value = rows[i + 1] as string;
                var ink = i + 2 < rows.Length && rows[i + 2] is Color c ? c : Color.White;

                var y = Bottom - Step * line;
                line++;

                if (ready) UI.Draw.Sprite(Dict, Back, CentreX, y, Width, Height, 0f, Shade);
                else UI.Draw.Rect(CentreX, y, Width, Height, Color.FromArgb(150, 0, 0, 0));

                UI.Draw.TextRight(label, LabelRight, y - 0.0112f, 0.30f, Color.White, UI.Draw.FontBody, false);
                UI.Draw.TextRight(value, ValueRight, y - 0.0175f, 0.45f, ink, UI.Draw.FontBody, false);
            }
        }
    }

    /// <summary>
    /// The casino's own sounds: the chips going down, the ball in the wheel, the machines. They
    /// live in script audio banks that have to be asked for, and asked again every frame until
    /// they are in.
    /// </summary>
    internal static class Sfx
    {
        public const string Table = "dlc_vw_table_games_frontend_sounds";
        public const string TableGames = "dlc_vw_table_games_sounds";
        public const string RouletteExits = "dlc_vw_table_games_roulette_exit_sounds";

        private static readonly string[] BankNames =
        {
            "DLC_VINEWOOD/CASINO_GENERAL",
            "DLC_VINEWOOD/CASINO_SLOT_MACHINES_01",
            "DLC_VINEWOOD/CASINO_SLOT_MACHINES_02",
            "DLC_VINEWOOD/CASINO_SLOT_MACHINES_03"
        };

        private static readonly bool[] Loaded = new bool[4];
        private static bool _said;

        /// <summary>Asks for the banks until they are all in. Cheap once they are.</summary>
        public static void Banks()
        {
            var all = true;

            for (var i = 0; i < BankNames.Length; i++)
            {
                if (Loaded[i]) continue;

                try { Loaded[i] = Function.Call<bool>(Hash.REQUEST_SCRIPT_AUDIO_BANK, BankNames[i], false, -1); }
                catch { Loaded[i] = false; }

                if (!Loaded[i]) all = false;
            }

            if (all && !_said)
            {
                _said = true;
                Log.Info("Den: the casino's sound banks are in.");
            }
        }

        public static void Release()
        {
            for (var i = 0; i < BankNames.Length; i++)
            {
                if (!Loaded[i]) continue;

                try { Function.Call(Hash.RELEASE_NAMED_SCRIPT_AUDIO_BANK, BankNames[i]); }
                catch { }

                Loaded[i] = false;
            }

            _said = false;
        }

        /// <summary>A table sound from the front of the screen: the chips, the errors, the win.</summary>
        public static void Front(string name)
        {
            try { Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, name, Table, true); }
            catch { }
        }

        /// <summary>A sound from a thing, with an id kept so it can be stopped. -1 if it would not start.</summary>
        public static int From(Entity e, string name, string set)
        {
            if (e == null || !e.Exists()) return -1;

            try
            {
                var id = Function.Call<int>(Hash.GET_SOUND_ID);
                Function.Call(Hash.PLAY_SOUND_FROM_ENTITY, id, name, e.Handle, set, false, 0);
                return id;
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>A sound from a thing that is left to finish on its own.</summary>
        public static void Once(Entity e, string name, string set)
        {
            var id = From(e, name, set);
            if (id < 0) return;

            try { Function.Call(Hash.RELEASE_SOUND_ID, id); }
            catch { }
        }

        public static void Stop(ref int id)
        {
            if (id < 0) return;

            try
            {
                Function.Call(Hash.STOP_SOUND, id);
                Function.Call(Hash.RELEASE_SOUND_ID, id);
            }
            catch { }

            id = -1;
        }

        public static void SceneOn(string name)
        {
            try
            {
                if (!Function.Call<bool>(Hash.IS_AUDIO_SCENE_ACTIVE, name)) Function.Call(Hash.START_AUDIO_SCENE, name);
            }
            catch { }
        }

        public static void SceneOff(string name)
        {
            try
            {
                if (Function.Call<bool>(Hash.IS_AUDIO_SCENE_ACTIVE, name)) Function.Call(Hash.STOP_AUDIO_SCENE, name);
            }
            catch { }
        }
    }
}
