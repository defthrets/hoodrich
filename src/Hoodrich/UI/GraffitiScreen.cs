using System;
using System.Drawing;
using GTA;
using GTA.Native;
using Hoodrich.Paint;
using Control = GTA.Control;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.UI
{
    /// <summary>
    /// The can, as an app.
    ///
    /// THE ENGINE UNDER THIS IS THE SAME ENGINE THE STANDALONE USES -- the same files, kept in
    /// step by tools/sync-paint.py, not a port and not a rewrite. The only thing that differs
    /// between the two mods is how you get to it: F3 and a floating panel over there, a tile on
    /// the phone here. Everything about how paint lands, how wide it goes, what comes out of
    /// the can and how long it lasts is one implementation.
    ///
    /// So this file is deliberately thin. It picks a colour, hands over a tool, and wipes the
    /// walls. Anything it was tempted to decide for itself belongs in PaintConfig, where the
    /// other mod can see it too.
    ///
    /// Laid out like every other full screen here rather than like the standalone's panel,
    /// which is the point of doing it twice: this one has to look like it came with the phone.
    /// </summary>
    internal sealed class GraffitiScreen
    {
        private const float PanelWidthH = 0.60f;
        private const float PadH = 0.024f;
        private const float SwatchH = 0.062f;
        private const float ButtonH = 0.040f;

        /// <summary>
        /// The mark and the can, and the shape of the files behind them.
        ///
        /// The mark is a handstyle tag now rather than a block wordmark, and it is a taller
        /// file: the top of the box is an arrow, the bottom is drips, and the word itself gets
        /// about half. Keeping the old height would have kept the file the same size on screen
        /// and halved the part anybody reads.
        /// </summary>
        /// <summary>
        /// A masthead, not a billboard -- the same call as the standalone's panel. At 0.066 the
        /// app's name was the loudest thing on a screen whose job is choosing a colour.
        /// </summary>
        private const float LogoH = 0.034f;
        private const float LogoAspect = 5.6517f;
        private const float CanH = 0.052f;
        private const float CanAspect = 0.4412f;

        /// <summary>How many times wider than tall a cap icon is. Printed by tools/make_art.py.</summary>
        private const float CapAspect = 0.6406f;

        /// <summary>How long a shake lasts, how hard, and roughly how often.</summary>
        private const int ShakeMs = 900;
        private const int ShakeSpreadMs = 3500;
        private const float ShakeDegrees = 13f;
        private const double ShakeCycles = 4.0;

        /// <summary>Long enough that the press which opened it cannot also spend something.</summary>
        private const int OpenGraceMs = 220;

        /// <summary>
        /// The masthead, treated exactly as the standalone's panel treats its own.
        ///
        /// THE SAME LOOK, NOT THE SAME WORD. Everything below is lifted from Picker on purpose:
        /// the mark sprays itself on when the screen opens, never sits still, and glows in
        /// whatever is loaded in the can while the letters stay white. Two front ends onto one
        /// engine should not feel like two mods.
        ///
        /// The numbers are copied rather than shared because they live in a different assembly.
        /// If they move there they should move here, and the whole point is lost if they drift.
        /// </summary>
        private const int SprayFrames = 8;
        private const int SprayFrameMs = 45;

        private const float RockDegrees = 1.6f;
        private const double RockMs = 2600.0;
        private const float BobHeight = 0.0035f;
        private const double BobMs = 4100.0;

        private const int GlowDirs = 8;
        private const int GlowRings = 2;
        private const float GlowRadius = 0.0035f;
        private const float GlowStrength = 0.30f;
        private const double GlowMs = 2900.0;

        /// <summary>
        /// The rack, which lives in the paint engine rather than here.
        ///
        /// It used to be a pair of arrays in this file and another pair in Overspray's F3
        /// panel -- two hand-kept copies of one list that no tool compared, which is the exact
        /// thing the shared engine exists to stop. Adding a colour is one edit now.
        /// </summary>
        private static readonly Paint.Swatch[] Tins = Paint.Rack.All;

        private enum Row { Swatches, Cap, TakeCan, TakeExt, PutAway, Clear }

        /// <summary>Derived, because the bounds below were written out as a 3 in two places.</summary>
        private static readonly int LastRow = Enum.GetValues(typeof(Row)).Length - 1;

        private readonly PaintConfig _cfg;
        private readonly Marks _marks;

        /// <summary>
        /// Set by Main. Puts the can away properly -- plume off, prop gone, holstered to empty
        /// hands, engine disarmed -- which is the one thing this screen could not do before.
        ///
        /// It lived on its own home-screen tile for a day, beside this app, and two things
        /// about a spray can on one phone is one too many. Taking a can out is here; putting
        /// it back belongs on the same screen.
        /// </summary>
        public Action PutAway;

        private readonly Curtain _curtain = new Curtain();

        private readonly Random _rng = new Random();

        private int _pick = 3;
        private Row _row = Row.Swatches;

        /// <summary>The row the cursor was on before this one, and when it moved. See Theme.Lit.</summary>
        private int _lastRow = -1;
        private int _rowAt;

        /// <summary>The cursor frame that glides between the swatch and the buttons. See UI.Glide.</summary>
        private readonly Glide _glide = new Glide();
        private int _openedAt;

        private int _shakeFrom = int.MinValue / 2;
        private int _nextShake;

        /// <summary>
        /// Whether the wipe has been pressed once already.
        ///
        /// It throws away every mark in the world and there is no undo, so it asks. One press
        /// arms it, the second does it, moving off the row forgets it -- the cheapest
        /// confirmation there is, and it does not cost a second screen.
        /// </summary>
        private bool _armed;

        public GraffitiScreen(PaintConfig cfg, Marks marks)
        {
            _cfg = cfg;
            _marks = marks;
        }

        public bool IsOpen => _curtain.Showing;

        public Color Colour => Tins[_pick].Colour;

        /// <summary>How far the loaded paint scatters its shade. Zero for all but the two metallics.</summary>
        public float Sheen => Tins[_pick].Sheen;

        public void Open()
        {
            _curtain.Open();
            _openedAt = Game.GameTime;
            _armed = false;

            _lastRow = -1;
            _rowAt = Game.GameTime;
            _glide.Reset();

            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        public void Close()
        {
            if (!IsOpen) return;

            // The button that got you out of here does not also swing at somebody.
            Core.InputGuard.Swallow();

            _curtain.Close();
            _armed = false;

            Hud.PlaySound("BACK", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        // ---- input -------------------------------------------------------------

        public void Update()
        {
            if (!IsOpen) return;

            LockControls();

            if (!_curtain.Taking) return;
            if (Game.GameTime - _openedAt < OpenGraceMs) return;

            if (Pressed(Control.PhoneCancel)) { Close(); return; }

            if (Pressed(Control.PhoneUp)) Move(-1);
            else if (Pressed(Control.PhoneDown)) Move(1);
            else if (_row == Row.Swatches && Pressed(Control.PhoneLeft)) Step(-1);
            else if (_row == Row.Swatches && Pressed(Control.PhoneRight)) Step(1);
            else if (_row == Row.Cap && Pressed(Control.PhoneLeft)) Cycle(-1);
            else if (_row == Row.Cap && Pressed(Control.PhoneRight)) Cycle(1);
            else if (Pressed(Control.PhoneSelect)) Choose();
        }

        private void Choose()
        {
            switch (_row)
            {
                case Row.Swatches:
                    // Picking IS choosing. There is nothing to confirm.
                    Close();
                    break;

                case Row.Cap:
                    Cycle(1);
                    break;

                case Row.TakeCan:
                    Take(true);
                    break;

                case Row.TakeExt:
                    Take(false);
                    break;

                case Row.PutAway:
                    if (!(Can.Out() && _cfg.Armed))
                    {
                        Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                        break;
                    }

                    PutAway?.Invoke();
                    Close();
                    break;

                case Row.Clear:
                    if (!_armed)
                    {
                        _armed = true;
                        Hud.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                        break;
                    }

                    _marks.Clear();
                    _armed = false;

                    Hud.PlaySound("BACK", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                    Notify.Important("~g~Walls are clean.~s~");
                    break;
            }
        }

        /// <summary>
        /// Hands over the tool, and everything that follows from which one it is.
        ///
        /// The weapon underneath is the same object either way -- the extinguisher, because it
        /// is what carries the aim camera and a trigger, and a prop can do neither. What the
        /// choice really sets is the look and, through it, the reach and the cone: four metres
        /// and a metre across for a can, ten and two for a hose.
        ///
        /// One button per tool rather than a button and a switch. Two rows for one decision let
        /// the halves disagree, and they did: asking for an extinguisher and being handed the
        /// can's four-metre reach presented as the mod simply not painting.
        /// </summary>
        private void Take(bool asCan)
        {
            _cfg.SprayCanLook = asCan;

            // ARMS THE ENGINE. Until this, an extinguisher in his hands is an extinguisher --
            // which is why one picked up in a fire station no longer turns into a spray can,
            // and why the standalone handing him one does not make this mod grab it.
            _cfg.Armed = true;

            Can.Give(true);

            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");

            Notify.Important(asCan
                ? "~g~Can's in your hand.~s~  Aim and hold fire."
                : "~g~Extinguisher.~s~  Further reach, wider spray.");
        }

        private static bool Pressed(Control control)
        {
            return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)control);
        }

        /// <summary>The same lock every other full screen in this mod uses.</summary>
        private static void LockControls()
        {
            Function.Call(Hash.DISABLE_ALL_CONTROL_ACTIONS, 0);

            foreach (var control in new[]
                     {
                         Control.PhoneUp, Control.PhoneDown, Control.PhoneLeft, Control.PhoneRight,
                         Control.PhoneSelect, Control.PhoneCancel,
                         Control.LookLeftRight, Control.LookUpDown
                     })
            {
                Function.Call(Hash.ENABLE_CONTROL_ACTION, 0, (int)control, true);
            }
        }

        private void Move(int step)
        {
            var n = (int)_row + step;
            if (n < 0) n = LastRow;
            if (n > LastRow) n = 0;

            _lastRow = (int)_row;
            _rowAt = Game.GameTime;
            _row = (Row)n;

            // Walking away from the wipe forgets that it was armed.
            _armed = false;

            Hud.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        /// <summary>
        /// Puts the next cap on the can.
        ///
        /// Not written anywhere, unlike in the standalone -- this mod keeps no ini for the
        /// paint, so it starts on thin each session. Worth fixing the day somebody notices.
        /// </summary>
        private void Cycle(int by)
        {
            var caps = Caps.All.Length;

            _cfg.Cap = (_cfg.Cap + by % caps + caps) % caps;

            Hud.PlaySound("NAV_LEFT_RIGHT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        private void Step(int by)
        {
            _pick = (_pick + by + Tins.Length) % Tins.Length;
            Hud.PlaySound("NAV_LEFT_RIGHT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        // ---- drawing -----------------------------------------------------------

        public void Draw()
        {
            if (!IsOpen) return;

            var width = Hud.ToX(PanelWidthH);
            var left = 0.5f - width * 0.5f;
            var pad = Hud.ToX(PadH);

            var height = 0.340f + SwatchH + ButtonH * 5f;
            var top = 0.5f - height * 0.5f + _curtain.Lift;

            Theme.Panel(left, top, width, height);

            _glide.Begin();

            var grown = Theme.Grown(_rowAt);

            var x = left + pad;
            var right = left + width - pad;
            var y = top + 0.020f;

            // ---- the mark ----
            //
            // The same sprayed treatment as the standalone, in this mod's own word. A header
            // reading OVERSPRAY inside Posted Up would be a third name for a thing that
            // already has two -- what carries across is the look, not the wordmark.
            //
            // Falls back to the typed word, because a panel with a hole where its name goes is
            // worse than a plain heading.
            var logoW = Hud.ToX(LogoH) * LogoAspect;

            var clock = Game.GameTime;

            // Never still. Under two degrees and three thousandths of the screen -- small
            // enough that nobody reading a row notices, big enough that the header is not a
            // dead sticker when they do look at it. Two periods that do not divide into each
            // other, so it never quite repeats.
            var spin = (float)Math.Sin(clock / RockMs * Math.PI * 2.0) * RockDegrees;
            var bob = (float)Math.Sin(clock / BobMs * Math.PI * 2.0) * BobHeight;

            var mx = left + width * 0.5f;
            var my = y + LogoH * 0.5f + bob;

            var frame = Spraying();
            var lit = frame ?? "overspray.png";

            // ---- the glow, in whatever is loaded ----
            //
            // OFFSETS AND NOT A SCALE. A wordmark is four times wider than it is tall, so
            // growing it puts four times as many pixels on its width as its height -- a
            // horizontal smear rather than a halo. Offsets are the same distance every way.
            //
            // Through Legible, so black -- a real choice on this rack -- glows a light grey
            // rather than glowing nothing at all against a near-black screen.
            var breath = 0.72f + 0.28f * (float)Math.Sin(clock / GlowMs * Math.PI * 2.0);
            var halo = Legible(Colour);

            for (var ring = GlowRings; ring >= 1; ring--)
            {
                var soft = GlowStrength * (1f - (ring - 1) / (float)GlowRings) * breath;

                var ry = GlowRadius * ring;
                var rx = Hud.ToX(ry);

                for (var d = 0; d < GlowDirs; d++)
                {
                    var a = d * Math.PI * 2.0 / GlowDirs;

                    Hud.File(lit,
                             mx + (float)Math.Cos(a) * rx,
                             my + (float)Math.Sin(a) * ry,
                             logoW, LogoH, spin,
                             Color.FromArgb((int)(halo.A * soft), halo.R, halo.G, halo.B));
                }
            }

            // And the mark itself on top, crisp and WHITE. The colour is the glow and the mark
            // is the word: drawn in the loaded colour too, the header becomes one hue and stops
            // reading as a wordmark at all.
            if ((frame == null || !Hud.File(frame, mx, my, logoW, LogoH, spin, Palette.Text)) &&
                !Hud.File("overspray.png", mx, my, logoW, LogoH, spin, Palette.Text))
            {
                Hud.Text("OVERSPRAY", x, y - 0.004f, 0.74f, Palette.Text,
                         Hud.FontCursive, centre: false);
            }

            Hud.TextRight(Tins[_pick].Name, right, y + 0.010f, 0.34f, Legible(Colour));

            y += LogoH + 0.008f;

            // ---- the can, having a shake ----
            Shaker(left + width * 0.5f, y);

            y += CanH + 0.010f;

            Hud.Text("WHAT YOU'RE PUTTING UP", x, y, 0.26f, Palette.TextDim,
                     Hud.FontLabel, centre: false);
            Hud.TextRight(_marks.Count + (_marks.Count == 1 ? " mark" : " marks"),
                          right, y, 0.24f, Palette.TextDim);

            y += 0.026f;
            Hud.RectFrom(x, y, right - x, 0.0016f, Legible(Colour));
            y += 0.014f;

            // ---- the rack ----
            var gap = Hud.ToX(0.005f);
            var each = (right - x - gap * (Tins.Length - 1)) / Tins.Length;

            for (var i = 0; i < Tins.Length; i++)
            {
                var sx = x + i * (each + gap);

                // MARKED WHATEVER ROW YOU ARE ON. Lighting it only while the cursor is on the
                // swatch row leaves nothing on screen saying which colour is loaded the moment
                // you step down to a button.
                var on = i == _pick;
                var focused = _row == Row.Swatches;

                var sh = on ? SwatchH : SwatchH - 0.012f;
                var sy = y + (SwatchH - sh);

                Chip(sx, sy, each, sh, Tins[i]);

                // A near-black swatch on a near-black panel is an empty slot rather than a
                // colour, so the outline brightens as the swatch darkens.
                Outline(sx, sy, each, sh, 0.0012f,
                        Luma(Tins[i].Colour) < 0.18f ? Palette.TextDim
                                                 : Color.FromArgb(70, 255, 255, 255));

                if (on)
                {
                    // The frame does the pointing while the swatch row has the cursor; a quiet
                    // ring says which colour is loaded once it has moved down to the buttons.
                    if (focused) _glide.Target(sx - 0.0022f, sy - 0.0022f, each + 0.0044f, sh + 0.0044f);
                    else Outline(sx - 0.0022f, sy - 0.0022f, each + 0.0044f, sh + 0.0044f, 0.0026f, Palette.TextDim);
                }
            }

            y += SwatchH + 0.016f;

            // ---- the two tools ----
            //
            // The right-hand word says which one you are already carrying, so the app answers
            // "what have I got" without you closing it to look.
            var has = Can.Has();

            // ---- the nozzle ----
            //
            // A cap sets the NARROWEST line the can can draw, not the widest -- a fat one
            // cannot do fine work however close you hold it, while the far end stays governed
            // by how far off the wall you are standing.
            CapRow(x, right, y, Lit(Row.Cap, grown));

            y += ButtonH;

            Button(x, right, y, _row == Row.TakeCan, Lit(Row.TakeCan, grown), "TAKE A SPRAY CAN",
                   has && _cfg.SprayCanLook ? "IN HAND" : "ENTER", false);

            y += ButtonH;

            Button(x, right, y, _row == Row.TakeExt, Lit(Row.TakeExt, grown), "TAKE AN EXTINGUISHER",
                   has && !_cfg.SprayCanLook ? "IN HAND" : "ENTER", false);

            y += ButtonH;

            // ---- and back again ----
            //
            // The right-hand word says whether there is anything to put away, so pressing it
            // with empty hands is never a mystery.
            var out_ = Can.Out() && _cfg.Armed;

            Button(x, right, y, _row == Row.PutAway, Lit(Row.PutAway, grown), "PUT IT AWAY",
                   out_ ? "ENTER" : "NOT OUT", false);

            y += ButtonH;

            // ---- wipe it all ----
            Button(x, right, y, _row == Row.Clear, Lit(Row.Clear, grown),
                   _armed ? "PRESS AGAIN -- THIS CANNOT BE UNDONE" : "CLEAR EVERY WALL",
                   _armed ? "SURE?" : "ENTER", _armed);

            Hud.Text("UP/DOWN  MOVE      LEFT/RIGHT  CHANGE      ENTER  TAKE IT      BACKSPACE  BACK",
                     x, top + height - 0.028f, 0.22f, Palette.TextDim, Hud.FontLabel, centre: false);

            // Last, so it rides over whatever it is pointing at.
            _glide.Draw();
        }

        /// <summary>How lit a button row is: coming up under the cursor, going down where it just was.</summary>
        private float Lit(Row row, float grown)
        {
            return Theme.Lit((int)row, (int)_row, _lastRow, grown);
        }

        /// <summary>
        /// The cap row: three little buttons rather than a word.
        ///
        /// A WORD IS THE WRONG CONTROL FOR THIS. "STOCK" says nothing about what it does until
        /// you have tried all three and remembered, where three holes at three sizes say it
        /// before you press anything -- and the hole IS the setting, because a cap is only ever
        /// the narrowest line the can can draw.
        ///
        /// Drawn the same way every other icon in this mod is: white art on transparent,
        /// tinted at draw time, so one file is both the dim one and the loaded colour.
        /// </summary>
        private void CapRow(float x, float right, float y, float rowLit)
        {
            var active = _row == Row.Cap;

            // CAPS ARE FOR THE CAN, and the row has to say so without saying so. The engine
            // has always known -- an extinguisher never consults a cap -- but the screen did
            // not show it, so the row looked like a setting that applied to whatever was in
            // his hand.
            //
            // It says so by GOING QUIET while an extinguisher is the tool in hand -- plate and
            // all three caps down to a bit over a third. That is what every interface does with
            // a control not connected to anything at the moment, and it needs no explaining.
            //
            // Still usable while dimmed: picking your cap before you pick the can up is a
            // reasonable thing to do.
            var lit = _cfg.SprayCanLook ? 255 : 104;

            // Labelled like every other row, with no hint -- three pictures are going where
            // that word would have been.
            Button(x, right, y, active, rowLit, "CAP SIZE", "", false);

            var caps = Caps.All;

            // The plate stays square so the three read as a row of buttons; the cap inside it
            // is drawn at its own proportions, because a cap squeezed into a square is a cap
            // that looks like somebody stood on it.
            var side = ButtonH - 0.010f;
            var wide = Hud.ToX(side);
            var gap = Hud.ToX(0.004f);

            var capH = side * 0.88f;
            var capW = Hud.ToX(capH) * CapAspect;

            var edge = right;
            var top = y + (ButtonH - side) * 0.5f;

            for (var i = caps.Length - 1; i >= 0; i--)
            {
                var left = edge - wide;
                var on = i == _cfg.Cap;

                // A lit plate behind the chosen one and nothing behind the others -- a ring
                // round all three would make this row louder than the swatches above it.
                if (on)
                {
                    Hud.RectFrom(left, top, wide, side,
                                 Palette.Alpha(Palette.Brand, 46 * lit / 255));
                }

                var chip = on ? Legible(Colour) : Palette.TextDim;

                var tint = Color.FromArgb(chip.A * lit / 255, chip.R, chip.G, chip.B);

                if (!Hud.File(caps[i].Icon, left + wide * 0.5f, top + side * 0.5f,
                              capW, capH, 0f, tint))
                {
                    // No art in data\icons. A plain square at the cap's own scale is the icon
                    // with its ring taken off, and still says which of the three this is.
                    var d = side * 0.22f * (float)Math.Sqrt(caps[i].Width);

                    Hud.RectFrom(left + (wide - Hud.ToX(d)) * 0.5f, top + (side - d) * 0.5f,
                                 Hud.ToX(d), d, tint);
                }

                edge = left - gap;
            }
        }

        private void Button(float x, float right, float y, bool active, float lit, string label,
                            string hint, bool warn)
        {
            var bx = x - Hud.ToX(0.008f);
            var bw = (right - x) + Hud.ToX(0.016f);

            // The plate comes up under the row the cursor lands on and goes down under the one
            // it left, and the frame travels between them. The wipe is the exception: an
            // armed, irreversible thing keeps its red, because gold under "this cannot be
            // undone" is the wrong colour for the sentence.
            if (warn)
            {
                Hud.RectFrom(bx, y, bw, ButtonH, Palette.Alpha(Palette.Danger, (int)(40f * lit)));
                Hud.RectFrom(bx, y, 0.0022f, ButtonH, Palette.Alpha(Palette.Danger, (int)(255f * lit)));
            }
            else
            {
                Theme.Plate(bx, y, bw, ButtonH, lit);
                Theme.Sheen(bx, y, bw, ButtonH, lit);
            }

            if (active) _glide.Target(bx, y, bw, ButtonH);

            var dark = warn ? 0f : lit;

            var ink = Theme.Ink(warn ? Palette.Danger : active ? Palette.Text : Palette.TextDim, dark);

            Hud.Text(label, x, y + 0.010f, 0.30f, ink, Hud.FontBody, centre: false);

            Hud.TextRight(hint, right, y + 0.011f, 0.24f,
                          Theme.Ink(warn ? Palette.Danger : active ? Legible(Colour) : Palette.TextDim, dark),
                          Hud.FontLabel);
        }

        /// <summary>
        /// Which frame of the reveal is up, or null once the mark is just the mark.
        ///
        /// Once per open and no more. The standalone re-sprayed its mark every few seconds for
        /// a while and it turned an arrival into a tic -- the header redrawing itself while you
        /// are trying to read the row underneath it.
        ///
        /// The frames are the SAME CANVAS as overspray.png, uncropped, so handing over to it at
        /// the end is invisible. Cropped to their own ink each one would be a different shape
        /// drawn into the same box, and the word would slide about and jump on the last step.
        /// </summary>
        private string Spraying()
        {
            var i = (Game.GameTime - _openedAt) / SprayFrameMs;

            return i >= 0 && i < SprayFrames ? "overspray_" + i + ".png" : null;
        }

        /// <summary>
        /// One square on the rack.
        ///
        /// A METALLIC IS DRAWN AS THE RANGE IT SPRAYS, not as its middle. Chrome's middle is a
        /// mid-grey, and a flat mid-grey square next to the white one says "grey paint" -- the
        /// player would only find out it was chrome by going and covering a wall with it. Five
        /// bands lit from the top is the least a gradient can be and still read as metal.
        /// </summary>
        private static void Chip(float x, float y, float w, float h, Paint.Swatch s)
        {
            if (!s.Metallic)
            {
                Hud.RectFrom(x, y, w, h, s.Colour);
                return;
            }

            const int bands = 5;

            for (var i = 0; i < bands; i++)
            {
                // Brightest at the top down to darkest at the bottom, because light comes from
                // above and a chrome swatch shaded the other way reads as a hole in the panel.
                var t = 1f - i * 2f / (bands - 1);

                // A hair taller than its share, so rounding cannot leave a seam of phone
                // showing between two of them.
                Hud.RectFrom(x, y + h * i / bands, w, h / bands + 0.0004f,
                             Paint.Rack.Lit(s.Colour, t * s.Sheen));
            }
        }

        /// <summary>
        /// A thin outline, as four boxes.
        ///
        /// Draw.Frame here takes a fill AND a rule and is for panels; this wants the rule only,
        /// which is cheaper to write out than to talk that one into.
        /// </summary>
        private static void Outline(float left, float top, float w, float h, float thick, Color c)
        {
            Hud.RectFrom(left, top, w, thick, c);
            Hud.RectFrom(left, top + h - thick, w, thick, c);
            Hud.RectFrom(left, top, Hud.ToX(thick), h, c);
            Hud.RectFrom(left + w - Hud.ToX(thick), top, Hud.ToX(thick), h, c);
        }

        /// <summary>
        /// The can under the mark, shaking every few seconds.
        ///
        /// THE SAME HABIT HE HAS IN THE WORLD. He shakes the real one now and then while he is
        /// holding it, on the same sort of interval -- so this is the app showing you the tool
        /// rather than decorating itself.
        ///
        /// Damped rather than a plain sine: it starts hard, rattles and settles, which is what
        /// shaking a can looks like. Constant amplitude reads as a broken transform.
        /// </summary>
        private void Shaker(float cx, float top)
        {
            var now = Game.GameTime;

            if (now >= _nextShake)
            {
                _shakeFrom = now;
                _nextShake = now + ShakeMs + _rng.Next(ShakeSpreadMs);
            }

            var t = (now - _shakeFrom) / (float)ShakeMs;

            var spin = 0f;
            var bob = 0f;

            if (t < 1f)
            {
                var decay = 1f - t;
                decay *= decay;

                spin = (float)Math.Sin(t * Math.PI * 2.0 * ShakeCycles) * ShakeDegrees * decay;

                // Half the frequency on the bob, or it buzzes rather than shakes.
                bob = (float)Math.Sin(t * Math.PI * 2.0 * ShakeCycles * 0.5) * 0.0035f * decay;
            }

            var canW = Hud.ToX(CanH) * CanAspect;

            if (!Hud.File("spraycan.png", cx, top + CanH * 0.5f + bob, canW, CanH, spin,
                          Legible(Colour)))
            {
                return;
            }

            // A shadow underneath, squashed by the bob, so it is standing on the panel rather
            // than floating over it.
            var lift = 1f - bob / 0.0035f * 0.35f;

            Hud.RectFrom(cx - canW * 0.30f * lift, top + CanH + 0.002f,
                         canW * 0.60f * lift, 0.0022f, Color.FromArgb(90, 0, 0, 0));
        }

        /// <summary>How bright a colour reads. Rec. 601, plenty for "can I see this".</summary>
        private static float Luma(Color c)
        {
            return (0.299f * c.R + 0.587f * c.G + 0.114f * c.B) / 255f;
        }

        /// <summary>
        /// The same colour, lifted until it can be read on a dark panel.
        ///
        /// A SWATCH CAN BE ANY COLOUR. TEXT CANNOT. Black paint drawn as black text on a black
        /// panel is a blank space exactly where the name of the colour should be -- and naming
        /// it is most of what this screen is for, since the can itself only ever shows the
        /// nearest of the game's eight tints.
        /// </summary>
        private static Color Legible(Color c)
        {
            const float Floor = 0.35f;

            var l = Luma(c);
            if (l >= Floor) return c;

            var t = 1f - l / Floor;

            return Color.FromArgb(c.A,
                                  (int)(c.R + (255 - c.R) * t),
                                  (int)(c.G + (255 - c.G) * t),
                                  (int)(c.B + (255 - c.B) * t));
        }
    }
}
