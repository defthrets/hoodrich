using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Native;
using Hoodrich.Locations;
using Control = GTA.Control;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.UI
{
    /// <summary>
    /// Where to, before the car is sent. Left and right walk the places, the place itself is
    /// behind the sheet, live (see RideCam), the fare is on the right, and Enter books it.
    ///
    /// The app used to ask from the back seat, so you found out where you were going -- and
    /// what it cost -- after a two-minute wait for the car, from a list with nothing on it
    /// but names. This is the shape of the real thing: look, see the number, then call.
    ///
    /// The sheet is a strip across the bottom and a band across the top; everything between
    /// them is the picture. Framed with four corners so it reads as one, and with nothing
    /// else drawn over it, because the picture is the point.
    /// </summary>
    internal sealed class RideScreen
    {
        /// <summary>Set by Main: books a car to the stop. Returns a refusal or null.</summary>
        public Func<RideStop, string> Book;

        /// <summary>Set by Main: the fare from where he is stood, or -1 when it will not go.</summary>
        public Func<RideStop, int> Quote;

        /// <summary>Whether the place is shown live behind the sheet. Settings: Luber / Preview.</summary>
        public static bool Preview = true;

        private readonly Curtain _curtain = new Curtain();

        private RideStop[] _stops = new RideStop[0];
        private int[] _quotes = new int[0];
        private int _pick;
        private int _pickedAt;

        private const float TopBand = 0.070f;
        private const float Sheet = 0.235f;
        private const float SideX = 0.055f;
        private const float CornerLen = 0.030f;
        private const float CornerThick = 0.0022f;

        private const int Unasked = int.MinValue;

        public bool IsOpen => _curtain.Showing;

        public void Open()
        {
            // The marker first when there is one, then the places it knows.
            var list = new List<RideStop>();

            var mark = Luber.Waypoint();
            if (mark != null) list.Add(mark);

            list.AddRange(Luber.Stops);

            _stops = list.ToArray();
            _quotes = new int[_stops.Length];
            for (var i = 0; i < _quotes.Length; i++) _quotes[i] = Unasked;

            _pick = 0;
            _pickedAt = Game.GameTime;

            _curtain.Open();
            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");

            Look();
        }

        public void Close()
        {
            Leave("BACK");
        }

        private void Leave(string sound)
        {
            if (!IsOpen) return;

            RideCam.Stop();
            Core.InputGuard.Swallow();
            _curtain.Close();

            Hud.PlaySound(sound, "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        public void Update()
        {
            if (!IsOpen) return;

            // From here and not from Main: Main returns before its own camera tick while a
            // screen is up, so the circling and the HUD hiding would never run.
            RideCam.Update();

            LockControls();

            if (!_curtain.Taking) return;

            if (Pressed(Control.PhoneCancel)) { Close(); return; }

            if (Pressed(Control.PhoneLeft) || Pressed(Control.PhoneUp)) Step(-1);
            else if (Pressed(Control.PhoneRight) || Pressed(Control.PhoneDown)) Step(1);
            else if (Pressed(Control.PhoneSelect)) Choose();
        }

        private void Step(int by)
        {
            if (_stops.Length == 0) return;

            _pick = (_pick + by + _stops.Length) % _stops.Length;
            _pickedAt = Game.GameTime;

            Hud.PlaySound("NAV_LEFT_RIGHT", "HUD_FRONTEND_DEFAULT_SOUNDSET");

            Look();
        }

        private void Look()
        {
            if (!Preview || _stops.Length == 0) return;

            RideCam.Show(Game.Player.Character, _stops[_pick].At);
        }

        /// <summary>Asked once per place per opening, and only for the one on screen.</summary>
        private int Fare(int i)
        {
            if (_quotes[i] == Unasked) _quotes[i] = Quote == null ? -1 : Quote(_stops[i]);

            return _quotes[i];
        }

        private void Choose()
        {
            if (_stops.Length == 0) return;

            var stop = _stops[_pick];
            var no = Book == null ? "Not wired up" : Book(stop);

            if (!string.IsNullOrEmpty(no))
            {
                Notify.Failure(no);
                Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                return;
            }

            Leave("SELECT");
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

        // ---- drawing ------------------------------------------------------------

        public void Draw()
        {
            if (!IsOpen) return;

            // SOLID IS THE ALPHA. Lift is the slide -- a distance in screen height that is
            // zero once the panel is in place -- and used as an alpha it drew nothing at all.
            var lift = _curtain.Solid;
            var slide = _curtain.Lift;
            var live = RideCam.Showing;

            // THE PLACE, OR THE STREET. Live, what is behind is the destination, black only
            // while the camera jumps between places; with no preview the street is dimmed
            // the way every other full screen dims it.
            if (live)
            {
                var dark = 1f - RideCam.Lifted();
                if (dark > 0f) Hud.RectFrom(0f, 0f, 1f, 1f, Color.FromArgb((int)(255f * dark), 0, 0, 0));
            }
            else
            {
                Hud.RectFrom(0f, 0f, 1f, 1f, Dim(Palette.Backdrop, lift));
            }

            // The sheet comes up from the bottom edge and the band down from the top.
            var sheetTop = 1f - Sheet + slide;
            var bandTop = -slide;

            Hud.RectFrom(0f, bandTop, 1f, TopBand, Dim(Color.FromArgb(238, Theme.Body), lift));
            Hud.RectFrom(0f, sheetTop, 1f, Sheet, Dim(Color.FromArgb(238, Theme.Body), lift));
            Theme.Wash(0f, sheetTop, 1f, 0.05f, (int)(15f * lift));

            if (live) Corners(lift);

            var ink = Dim(Palette.Text, lift);
            var dim = Dim(Palette.TextDim, lift);

            // ---- the band: whose app, what it wants, and how far through the list ----
            var bandText = bandTop + TopBand * 0.5f - 0.013f;

            // THE WORDMARK RATHER THAN THE WORD, and the mod's car picture goes with it --
            // this is the app introducing itself, and an app has a logo. The typed name is
            // still behind it for an install without the file.
            var markH = 0.026f;
            var markW = Hud.ToX(markH * LuberAspect);

            if (!Hud.File("luber.png", SideX + markW * 0.5f, bandTop + TopBand * 0.5f,
                          markW, markH, 0f, ink))
            {
                Hud.Text("LUBER", SideX, bandText, 0.36f, ink, Hud.FontLabel, centre: false);
            }
            Hud.Text("WHERE TO?", 0.5f, bandText, 0.36f, dim, Hud.FontLabel);
            Hud.TextRight((_pick + 1) + " / " + _stops.Length, 1f - SideX, bandText, 0.36f, dim, Hud.FontLabel);

            if (_stops.Length == 0)
            {
                Hud.Text("Nowhere to go", 0.5f, sheetTop + 0.08f, 0.5f, dim, Hud.FontBody);
                return;
            }

            var n = _stops.Length;
            var stop = _stops[_pick];
            var grown = Theme.Grown(_pickedAt);

            // ---- the sheet: the place on the left, the money on the right ----
            var y = sheetTop + 0.030f;

            Hud.Text(stop.Name, SideX, y, 0.70f, ink, Hud.FontBody, centre: false);
            Hud.Text(stop.Area.ToUpperInvariant(), SideX, y + 0.070f, 0.30f, dim, Hud.FontLabel, centre: false);

            var fare = Fare(_pick);
            var me = Game.Player.Character;
            var metres = me != null && me.Exists() ? me.Position.DistanceTo(stop.At) : 0f;
            var km = metres / 1000f;
            var mins = Minutes(metres);

            // The number arrives a beat after the name, so a fast scroll reads as names
            // going past rather than as figures flickering.
            Hud.TextRight(fare < 0 ? "WON'T GO" : "$" + fare, 1f - SideX, y, 0.70f,
                          Dim(fare < 0 ? Palette.Danger : Palette.Cash, lift * grown), Hud.FontBody);
            Hud.TextRight(fare < 0 ? "" : "ABOUT " + mins + (mins == 1 ? " MIN" : " MINS") + "   -   " + km.ToString("0.0") + " KM",
                          1f - SideX, y + 0.070f, 0.30f, Dim(Palette.TextDim, lift * grown), Hud.FontLabel);

            // ---- the strip: one mark a place, the neighbours named either side ----
            var stripY = sheetTop + 0.140f;

            Dots(stripY, lift);

            if (n > 1)
            {
                var prev = _stops[(_pick - 1 + n) % n].Name.ToUpperInvariant();
                var next = _stops[(_pick + 1) % n].Name.ToUpperInvariant();

                Hud.Text("<  " + prev, SideX, stripY - 0.011f, 0.26f, dim, Hud.FontLabel, centre: false);
                Hud.TextRight(next + "  >", 1f - SideX, stripY - 0.011f, 0.26f, dim, Hud.FontLabel);
            }

            // ---- the foot ----
            Theme.Rule(SideX, sheetTop + Sheet - 0.046f, 1f - SideX * 2f, lift);

            Hud.Text(Hud.OnPad
                         ? "D-PAD  BROWSE      A  BOOK IT      B  BACK"
                         : "LEFT/RIGHT  BROWSE      ENTER  BOOK IT      BACKSPACE  BACK",
                     0.5f, sheetTop + Sheet - 0.035f, 0.24f, dim, Hud.FontLabel);
        }

        /// <summary>One mark a place; the one you are on is wide and green.</summary>
        private void Dots(float y, float lift)
        {
            var n = _stops.Length;
            var dot = Hud.ToX(0.006f);
            var wide = Hud.ToX(0.020f);
            var gap = Hud.ToX(0.006f);

            var x = 0.5f - ((n - 1) * (dot + gap) + wide) * 0.5f;

            for (var i = 0; i < n; i++)
            {
                var on = i == _pick;
                var w = on ? wide : dot;

                Hud.RectFrom(x, y, w, 0.004f, on ? Dim(Palette.Brand, lift) : Dim(Palette.TextDim, lift * 0.5f));

                x += w + gap;
            }
        }

        /// <summary>Four corners round the picture, so the picture reads as one thing.</summary>
        private static void Corners(float lift)
        {
            var c = Color.FromArgb((int)(150f * lift), 255, 255, 255);

            var t = CornerThick;
            var tx = Hud.ToX(t);
            var len = CornerLen;
            var lenX = Hud.ToX(len);

            var left = SideX;
            var right = 1f - SideX;
            var top = TopBand + 0.035f;
            var bottom = 1f - Sheet - 0.035f;

            Hud.RectFrom(left, top, lenX, t, c);
            Hud.RectFrom(left, top, tx, len, c);

            Hud.RectFrom(right - lenX, top, lenX, t, c);
            Hud.RectFrom(right - tx, top, tx, len, c);

            Hud.RectFrom(left, bottom - t, lenX, t, c);
            Hud.RectFrom(left, bottom - len, tx, len, c);

            Hud.RectFrom(right - lenX, bottom - t, lenX, t, c);
            Hud.RectFrom(right - tx, bottom - len, tx, len, c);
        }

        /// <summary>
        /// How long the ride is, from a straight line. Roads wind about a third further than
        /// the crow flies, and a Luber in traffic averages nearer thirteen metres a second
        /// than the twenty-two it is allowed -- the same two numbers a real app quietly
        /// rounds with. Never under a minute: nobody is told "0 mins".
        /// </summary>
        private static int Minutes(float metres)
        {
            const float roadWind = 1.3f;
            const float pace = 13f;

            var mins = (int)Math.Round(metres * roadWind / pace / 60f);

            return mins < 1 ? 1 : mins;
        }

        /// <summary>The shape of the wordmark file. See tools/make_luber.py.</summary>
        private const float LuberAspect = 2.4465f;

        private static Color Dim(Color c, float k)
        {
            if (k < 0f) k = 0f;
            if (k > 1f) k = 1f;

            return Color.FromArgb((int)(c.A * k), c.R, c.G, c.B);
        }
    }
}
