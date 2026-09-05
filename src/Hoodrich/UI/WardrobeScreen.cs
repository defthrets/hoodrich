using System;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Control = GTA.Control;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.UI
{
    /// <summary>
    /// What he is wearing, one slot a row, with him stood in front of the camera while you
    /// change it. Left and right walk the drawables in a slot, Enter walks the colours of the
    /// one on him, and every step goes onto him as you land on it, because he is the preview.
    /// Backspace keeps whatever he has on and writes it into the save.
    ///
    /// NO NAMES. The story's clothes carry no text the game will give up to a script, so a
    /// row says which of how many rather than what it is called -- which is what you see in
    /// the mirror anyway.
    /// </summary>
    internal sealed class WardrobeScreen
    {
        /// <summary>Set by Main: what he settled on is written down.</summary>
        public Action Done;

        private readonly Curtain _curtain = new Curtain();

        private sealed class Slot
        {
            public string Name;
            public bool Prop;
            public bool Body;
            public int Index;
        }

        /// <summary>
        /// Every slot the game has, and the body they hang on.
        ///
        /// ALL TWELVE COMPONENTS AND ALL FIVE PROPS. Six of the twelve were listed before, on
        /// the reasoning that nobody changes their face at a wardrobe -- which is true right up
        /// until somebody wants the bag, the vest under the shirt, the badge on the chest or
        /// the earrings, and finds the closet does not have them. Nothing here is gated: what
        /// the model owns is what the rail offers, and a story character owns his whole
        /// wardrobe from the first frame whether or not the game's own shop ever sold it to him.
        ///
        /// SLOT 8 IS ALSO WHERE THE BALACLAVA GOES. It is on the rail anyway. The mask puts
        /// itself on and takes itself off around whatever is there, so the only way the two
        /// collide is choosing an undershirt while masked, and the answer to that is to take
        /// the mask off.
        /// </summary>
        private static readonly Slot[] Slots =
        {
            new Slot { Name = "Body", Body = true },

            new Slot { Name = "Face", Index = 0 },
            new Slot { Name = "Mask", Index = 1 },
            new Slot { Name = "Hair", Index = 2 },
            new Slot { Name = "Arms", Index = 3 },
            new Slot { Name = "Legs", Index = 4 },
            new Slot { Name = "Bag", Index = 5 },
            new Slot { Name = "Shoes", Index = 6 },
            new Slot { Name = "Chain", Index = 7 },
            new Slot { Name = "Undershirt", Index = 8 },
            new Slot { Name = "Vest", Index = 9 },
            new Slot { Name = "Badge", Index = 10 },
            new Slot { Name = "Top", Index = 11 },

            new Slot { Name = "Hat", Prop = true, Index = 0 },
            new Slot { Name = "Glasses", Prop = true, Index = 1 },
            new Slot { Name = "Ears", Prop = true, Index = 2 },
            new Slot { Name = "Watch", Prop = true, Index = 6 },
            new Slot { Name = "Bracelet", Prop = true, Index = 7 }
        };

        /// <summary>
        /// The bodies the rail can dress, in the order the Body row cycles them.
        ///
        /// THE ONLY WAY TO REACH THE ONLINE CLOTHES. A drawable number means nothing on its own
        /// -- it is an index into ONE model's wardrobe -- so there is no such thing as putting
        /// a multiplayer jacket on Franklin. Jacket forty-one of his own list is what he gets,
        /// and if his list is shorter than that he gets nothing at all. The online clothes live
        /// on the freemode models and the only door to them is to be one, which is what this
        /// row does. His face changes with it; that is the deal, and it is one keypress back.
        /// </summary>
        private static readonly PedHash[] Bodies =
        {
            PedHash.Franklin,
            PedHash.FreemodeMale01,
            PedHash.FreemodeFemale01
        };

        private static readonly string[] BodyNames = { "Franklin", "Online man", "Online woman" };

        private int _row;
        private int _lastRow = -1;
        private int _pickedAt;
        private int _openedAt;
        private int _cam;

        private const float PanelWidthH = 0.44f;
        private const float RowHeight = 0.034f;
        private const float PadH = 0.024f;
        private const int OpenGraceMs = 220;
        private const float CamOut = 2.3f;
        private const float CamUp = 0.55f;
        private const float CamFov = 42f;
        private const int Head = 31086;

        public bool IsOpen => _curtain.Showing;

        /// <summary>
        /// Which way he stands while the closet is open.
        ///
        /// The camera is placed in front of whatever way he happens to be facing, so the shot
        /// was different every time and half of them were of the inside of the wardrobe door.
        /// Turned to a fixed heading on the way in, the picture is the same one every time --
        /// him, against the open closet, with the room behind the camera.
        /// </summary>
        private const float FaceHeading = 222.743f;

        public void Open()
        {
            _row = 0;
            _lastRow = -1;
            _pickedAt = _openedAt = Game.GameTime;

            try
            {
                var me = Game.Player.Character;

                if (me != null && me.Exists())
                {
                    me.Heading = FaceHeading;
                    Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, me.Handle);
                }
            }
            catch
            {
                // He is looked at whichever way he ended up.
            }

            _curtain.Open();
            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");

            Look();
        }

        public void Close()
        {
            if (!IsOpen) return;

            try { Done?.Invoke(); }
            catch (Exception ex) { Log.Debug("Could not write down what he wears: " + ex.Message); }

            Unlook();
            InputGuard.Swallow();
            _curtain.Close();
            Hud.PlaySound("BACK", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        // ---- the camera: in front of him, on him -------------------------------------

        private void Look()
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return;

                var rad = me.Heading * (float)Math.PI / 180f;
                var forward = new Vector3(-(float)Math.Sin(rad), (float)Math.Cos(rad), 0f);
                var eye = me.Position + forward * CamOut + new Vector3(0f, 0f, CamUp);

                _cam = Function.Call<int>(Hash.CREATE_CAM, "DEFAULT_SCRIPTED_CAMERA", true);
                if (_cam == 0) return;

                Function.Call(Hash.SET_CAM_FOV, _cam, CamFov);
                Function.Call(Hash.SET_CAM_COORD, _cam, eye.X, eye.Y, eye.Z);
                Function.Call(Hash.POINT_CAM_AT_PED_BONE, _cam, me.Handle, Head, 0f, 0f, -0.35f, true);
                Function.Call(Hash.SET_CAM_ACTIVE, _cam, true);
                Function.Call(Hash.RENDER_SCRIPT_CAMS, true, true, 400, true, false);
            }
            catch (Exception ex)
            {
                Log.Debug("The wardrobe camera would not start: " + ex.Message);
                _cam = 0;
            }
        }

        private void Unlook()
        {
            if (_cam == 0) return;

            var cam = _cam;
            _cam = 0;

            try
            {
                Function.Call(Hash.RENDER_SCRIPT_CAMS, false, true, 400, true, false);
                Function.Call(Hash.SET_CAM_ACTIVE, cam, false);
                Function.Call(Hash.DESTROY_CAM, cam, false);
            }
            catch
            {
            }
        }

        // ---- input --------------------------------------------------------------

        public void Update()
        {
            if (!IsOpen) return;

            LockControls();

            try { Function.Call(Hash.HIDE_HUD_AND_RADAR_THIS_FRAME); }
            catch { }

            if (!_curtain.Taking) return;
            if (Game.GameTime - _openedAt < OpenGraceMs) return;

            if (Pressed(Control.PhoneCancel)) { Close(); return; }

            if (Pressed(Control.PhoneUp)) Move(-1);
            else if (Pressed(Control.PhoneDown)) Move(1);
            else if (Pressed(Control.PhoneLeft)) Step(-1);
            else if (Pressed(Control.PhoneRight)) Step(1);
            else if (Pressed(Control.PhoneSelect) || Pressed(Control.Context)) Colour();
        }

        private void Move(int step)
        {
            _lastRow = _row;
            _row = (_row + step + Slots.Length) % Slots.Length;
            _pickedAt = Game.GameTime;
            Hud.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        // ---- what is on him -------------------------------------------------------------

        private static int Count(Ped me, Slot s)
        {
            return s.Prop
                ? Function.Call<int>(Hash.GET_NUMBER_OF_PED_PROP_DRAWABLE_VARIATIONS, me.Handle, s.Index)
                : Function.Call<int>(Hash.GET_NUMBER_OF_PED_DRAWABLE_VARIATIONS, me.Handle, s.Index);
        }

        private static int Colours(Ped me, Slot s, int drawable)
        {
            if (drawable < 0) return 0;
            return s.Prop
                ? Function.Call<int>(Hash.GET_NUMBER_OF_PED_PROP_TEXTURE_VARIATIONS, me.Handle, s.Index, drawable)
                : Function.Call<int>(Hash.GET_NUMBER_OF_PED_TEXTURE_VARIATIONS, me.Handle, s.Index, drawable);
        }

        private static int Drawable(Ped me, Slot s)
        {
            return s.Prop
                ? Function.Call<int>(Hash.GET_PED_PROP_INDEX, me.Handle, s.Index)
                : Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, me.Handle, s.Index);
        }

        private static int Texture(Ped me, Slot s)
        {
            return s.Prop
                ? Function.Call<int>(Hash.GET_PED_PROP_TEXTURE_INDEX, me.Handle, s.Index)
                : Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, me.Handle, s.Index);
        }

        private static void Put(Ped me, Slot s, int drawable, int texture)
        {
            if (s.Prop)
            {
                if (drawable < 0) Function.Call(Hash.CLEAR_PED_PROP, me.Handle, s.Index);
                else Function.Call(Hash.SET_PED_PROP_INDEX, me.Handle, s.Index, drawable, texture, true);
            }
            else
            {
                Function.Call(Hash.SET_PED_COMPONENT_VARIATION, me.Handle, s.Index, drawable, texture, 0);
            }
        }

        /// <summary>Which body he is wearing now, or -1 for one this rail does not know.</summary>
        private static int BodyNow(Ped me)
        {
            if (me == null || !me.Exists()) return -1;

            for (var i = 0; i < Bodies.Length; i++)
            {
                if ((uint)me.Model.Hash == (uint)Bodies[i]) return i;
            }

            return -1;
        }

        /// <summary>
        /// Become one of the other bodies.
        ///
        /// The blocking form of the model request on purpose: this is one deliberate keypress,
        /// not a spawner, and half a body arriving is worse than a frame. Everything after the
        /// swap has to be redone -- the ped is a NEW ped, so the camera is pointed at a handle
        /// that no longer exists until it is rebuilt.
        /// </summary>
        private void Wear(int by)
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return;

                var now = BodyNow(me);
                var next = ((now < 0 ? 0 : now) + by + Bodies.Length) % Bodies.Length;
                if (next == now) return;

                var model = new Model(Bodies[next]);

                if (!model.IsValid || !model.IsInCdImage)
                {
                    Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                    return;
                }

                model.Request(3000);

                if (!model.IsLoaded)
                {
                    Log.Warn("The " + BodyNames[next] + " body would not load.");
                    Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                    return;
                }

                Unlook();

                Function.Call(Hash.SET_PLAYER_MODEL, Game.Player.Handle, model.Hash);
                model.MarkAsNoLongerNeeded();

                var him = Game.Player.Character;

                if (him != null && him.Exists())
                {
                    // A freemode model arrives with nothing on it at all. The game's own
                    // default set is a person in clothes rather than a mannequin, which is
                    // where somebody wants to start choosing from.
                    Function.Call(Hash.SET_PED_DEFAULT_COMPONENT_VARIATION, him.Handle);
                    him.Heading = FaceHeading;
                }

                Look();

                _pickedAt = Game.GameTime;
                Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");

                Log.Info("The closet put him in the " + BodyNames[next] + " body.");
            }
            catch (Exception ex)
            {
                Log.Warn("Could not change body: " + ex.Message);
                Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
            }
        }

        /// <summary>The next or previous drawable in the slot. A prop can also be nothing, which sits before its first.</summary>
        private void Step(int by)
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return;

                var s = Slots[_row];

                if (s.Body)
                {
                    Wear(by);
                    return;
                }
                var n = Count(me, s);
                if (n <= 0) { Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET"); return; }

                var now = Drawable(me, s);
                int next;

                if (s.Prop)
                {
                    // -1 .. n-1, with -1 being bare.
                    next = now + by;
                    if (next < -1) next = n - 1;
                    if (next >= n) next = -1;
                }
                else
                {
                    next = (now + by + n) % n;
                }

                Put(me, s, next, 0);
                _pickedAt = Game.GameTime;
                Hud.PlaySound("NAV_LEFT_RIGHT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not change a slot: " + ex.Message);
            }
        }

        /// <summary>The next colour of what is on him in the slot.</summary>
        private void Colour()
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return;

                var s = Slots[_row];
                if (s.Body) { Wear(1); return; }

                var d = Drawable(me, s);
                var n = Colours(me, s, d);
                if (n <= 1) { Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET"); return; }

                Put(me, s, d, (Texture(me, s) + 1) % n);
                _pickedAt = Game.GameTime;
                Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not change a colour: " + ex.Message);
            }
        }

        private static bool Pressed(Control control)
        {
            return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)control);
        }

        private static void LockControls()
        {
            Function.Call(Hash.DISABLE_ALL_CONTROL_ACTIONS, 0);

            foreach (var control in new[]
                     {
                         Control.PhoneUp, Control.PhoneDown, Control.PhoneLeft, Control.PhoneRight,
                         Control.PhoneSelect, Control.PhoneCancel
                     })
            {
                Function.Call(Hash.ENABLE_CONTROL_ACTION, 0, (int)control, true);
            }
        }

        // ---- drawing --------------------------------------------------------------

        public void Draw()
        {
            if (!IsOpen) return;

            var me = Game.Player.Character;
            if (me == null || !me.Exists()) return;

            var width = Hud.ToX(PanelWidthH);
            var pad = Hud.ToX(PadH);

            // Off to the right, so he stays in the middle of the picture.
            var left = 0.96f - width;
            var height = 0.140f + Slots.Length * RowHeight;
            var top = 0.5f - height * 0.5f + _curtain.Lift;

            Theme.Panel(left, top, width, height);

            var x = left + pad;
            var right = left + width - pad;
            var y = top + 0.020f;

            Hud.Text("WARDROBE", x, y - 0.004f, 0.74f, Palette.Text, Hud.FontCursive, centre: false);

            y += 0.052f;
            Hud.Text("WHAT HE HAS ON", x, y, 0.26f, Palette.TextDim, Hud.FontLabel, centre: false);
            y += 0.026f;
            Theme.Rule(x, y, right - x);
            y += 0.010f;

            var grown = Theme.Grown(_pickedAt);
            var barWide = (right - x) + pad * 0.7f;

            for (var i = 0; i < Slots.Length; i++)
            {
                var s = Slots[i];
                var here = i == _row;
                var lit = Theme.Lit(i, _row, _lastRow, grown);

                Theme.Plate(x - pad * 0.35f, y - 0.004f, barWide, RowHeight, lit);
                Theme.Sheen(x - pad * 0.35f, y - 0.004f, barWide, RowHeight, lit);

                var ink = Theme.Ink(here ? Palette.Text : Palette.TextDim, lit);

                Hud.Text(s.Name, x, y + 0.006f, 0.30f, ink, Hud.FontBody, centre: false);

                string value;
                try
                {
                    if (s.Body)
                    {
                        var which = BodyNow(me);
                        value = which < 0 ? "SOMEBODY ELSE" : BodyNames[which].ToUpperInvariant();
                    }
                    else
                    {
                        var d = Drawable(me, s);
                        var n = Count(me, s);
                        var c = Colours(me, s, d);
                        var t = Texture(me, s);

                        value = d < 0
                            ? "NONE  /  " + n
                            : (d + 1) + "  /  " + n + (c > 1 ? "     COLOUR " + (t + 1) + " / " + c : "");
                    }
                }
                catch
                {
                    value = "";
                }

                Hud.TextRight((here ? "<  " : "") + value + (here ? "  >" : ""), right, y + 0.008f, 0.24f,
                              here ? ink : Palette.TextDim, Hud.FontLabel);

                y += RowHeight;
            }

            Hud.Text(Hud.OnPad
                         ? "D-PAD  SLOT / CHANGE      A  COLOUR      B  DONE"
                         : "UP/DOWN  SLOT      LEFT/RIGHT  CHANGE      ENTER  COLOUR      BACKSPACE  DONE",
                     x, top + height - 0.030f, 0.24f, Palette.TextDim, Hud.FontLabel, centre: false);
        }
    }
}
