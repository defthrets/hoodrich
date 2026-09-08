using System;
using System.Collections.Generic;
using System.Drawing;
using Control = GTA.Control;
using GTA;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Economy;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.UI
{
    /// <summary>
    /// What somebody had on them, and who they were.
    ///
    /// THE CARD IS HALF THE POINT. A grid of things you can take is a loot screen and there
    /// are a thousand of those; a photograph, a name, a date of birth and a set turns the same
    /// grid into somebody you have just gone through the pockets of. Nothing about the numbers
    /// changes. The reading does.
    ///
    /// THE PHOTOGRAPH IS THE ACTUAL MAN. The game will render a headshot of any ped that
    /// exists, so it is asked for the one lying in front of you rather than a stand-in --
    /// which means the face on the card is the face on the pavement, down to the hat. It
    /// takes a few frames and there is a plate behind it in the meantime.
    ///
    /// A GRID, THE SAME AS THE POCKET AND THE BOOT. Everything in here has a picture drawn
    /// for it -- a gun, a bag, a sandwich, a roll of notes -- so it is shapes you recognise
    /// with a chip in the corner saying how much, and the card under it is where the chosen
    /// one becomes words.
    /// </summary>
    internal sealed class LootScreen
    {
        private const float PanelWidthH = 0.62f;
        private const float PadH = 0.024f;

        /// <summary>The identity strip: the photograph and the five things on the card.</summary>
        private const float CardH = 0.090f;
        private const float PhotoH = 0.076f;

        /// <summary>One square of the grid, and how many across.</summary>
        private const float TileH = 0.082f;
        private const int Columns = 5;
        private const int MaxRows = 3;

        private const float GridPad = 0.006f;

        /// <summary>The line under the grid that names the chosen thing.</summary>
        private const float NoteH = 0.050f;

        /// <summary>The gap between tiles, as an x fraction. Turned into y through the aspect.</summary>
        private const float Gap = 0.0018f;

        /// <summary>How much the chosen picture swells as its plate comes up.</summary>
        private const float PickGrow = 0.10f;

        private const int OpenGraceMs = 220;
        private const int RepeatMs = 140;
        private const int EnterMs = 170;
        private const float EnterRise = 0.014f;
        private const int TookFlashMs = 420;

        private readonly Curtain _curtain = new Curtain();
        private readonly Glide _glide = new Glide();

        private Bodies _bodies;
        private Body _body;
        private Ped _who;

        /// <summary>Set by Main: something came off the body, for the feed and the log.</summary>
        public Action<LootItem> Took;

        /// <summary>Set by Locations.Search: he can stand up again.</summary>
        public Action Done;

        private int _selected, _lastSelected = -1, _page;
        private int _openedAt, _pickedAt, _nextRepeat;
        private int _tookAt;
        private string _tookName = "";

        /// <summary>The headshot of the man on the floor. See the note on the class.</summary>
        private int _shot;
        private string _shotTxd = "";
        private int _shotAskedAt;

        public bool IsOpen => _curtain.Showing;

        public void Open(Bodies bodies, Body body, Ped who)
        {
            if (bodies == null || body == null) return;

            _bodies = bodies;
            _body = body;
            _who = who;

            _selected = 0;
            _lastSelected = -1;
            _page = 0;
            _tookName = "";
            _openedAt = _pickedAt = Game.GameTime;

            _glide.Reset();
            _curtain.Open();

            Photo();

            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        public void Close()
        {
            if (IsOpen) InputGuard.Swallow();

            _curtain.Close();

            DropPhoto();

            _bodies = null;
            _body = null;
            _who = null;

            if (Done != null) Done();
        }

        // ---- the photograph -----------------------------------------------------

        /// <summary>
        /// Asks the game for a picture of him.
        ///
        /// ITS OWN REGISTRATION, NOT THE FEED'S CACHE. UI.Headshots keeps faces made from
        /// MODELS, which is right for an account on a feed and wrong here -- two men of the
        /// same model are two different bodies and would share one photograph. This asks for
        /// the ped itself and hands the slot straight back when the screen shuts, so it never
        /// competes with the cache for long.
        /// </summary>
        private void Photo()
        {
            DropPhoto();

            _shotAskedAt = Game.GameTime;

            try
            {
                if (_who != null && _who.Exists())
                {
                    _shot = Function.Call<int>(Hash.REGISTER_PEDHEADSHOT, _who.Handle);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Loot: no headshot of the body: " + ex.Message);
                _shot = 0;
            }
        }

        private void DropPhoto()
        {
            try
            {
                if (_shot != 0) Function.Call(Hash.UNREGISTER_PEDHEADSHOT, _shot);
            }
            catch
            {
                // The game takes it back on its own eventually.
            }

            _shot = 0;
            _shotTxd = "";
        }

        /// <summary>The texture once the game has finished making it, or "" while it has not.</summary>
        private string Made()
        {
            if (!string.IsNullOrEmpty(_shotTxd)) return _shotTxd;
            if (_shot == 0) return "";

            try
            {
                if (!Function.Call<bool>(Hash.IS_PEDHEADSHOT_READY, _shot)) return "";
                if (!Function.Call<bool>(Hash.IS_PEDHEADSHOT_VALID, _shot)) { _shot = 0; return ""; }

                _shotTxd = Function.Call<string>(Hash.GET_PEDHEADSHOT_TXD_STRING, _shot) ?? "";

                if (!string.IsNullOrEmpty(_shotTxd))
                {
                    Log.Debug("Loot: photographed the body in " +
                              (Game.GameTime - _shotAskedAt) + "ms.");
                }

                return _shotTxd;
            }
            catch
            {
                return "";
            }
        }

        // ---- input --------------------------------------------------------------

        public void Update()
        {
            if (!IsOpen) return;

            LockControls();

            if (!_curtain.Taking) return;
            if (Game.GameTime - _openedAt < OpenGraceMs) return;

            // THE BODY IS STILL THERE, ISN'T IT. The game cleans corpses up on its own clock
            // and it does not care that somebody is looking through this one.
            if (_who == null || !_who.Exists() || _body == null)
            {
                Close();
                return;
            }

            if (Pressed(Control.PhoneCancel) || Pressed(Control.FrontendCancel))
            {
                Hud.PlaySound("BACK", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                Close();
                return;
            }

            if (_body.Items.Count == 0)
            {
                // Nothing left. One press of anything and he stands up.
                if (Pressed(Control.Jump) || Pressed(Control.FrontendAccept)) Close();
                return;
            }

            if (Pressed(Control.PhoneUp) || Pressed(Control.FrontendUp)) Move(0, -1);
            else if (Pressed(Control.PhoneDown) || Pressed(Control.FrontendDown)) Move(0, 1);
            else if (Pressed(Control.PhoneLeft) || Pressed(Control.FrontendLeft)) Move(-1, 0);
            else if (Pressed(Control.PhoneRight) || Pressed(Control.FrontendRight)) Move(1, 0);

            if (Game.GameTime < _nextRepeat) return;

            // SPACE TAKES THE ONE UNDER THE CURSOR. Held with sprint, it takes the lot --
            // the same pair of keys the boot and the house use to move things.
            if (Held(Control.Jump))
            {
                if (Held(Control.Sprint)) Everything();
                else One();
            }
        }

        private void Move(int dx, int dy)
        {
            var count = _body.Items.Count;
            if (count == 0) return;

            var at = Clamp(_selected, 0, count - 1);

            var to = dy != 0 ? at + dy * Columns : at + dx;

            if (to < 0 || to >= count) return;

            _lastSelected = at;
            _selected = to;
            _pickedAt = Game.GameTime;

            var perPage = Columns * MaxRows;
            _page = perPage <= 0 ? 0 : to / perPage;

            Hud.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        private void One()
        {
            if (_selected < 0 || _selected >= _body.Items.Count) return;

            var item = _body.Items[_selected];

            string why;

            if (!_bodies.Take(_body, item, out why))
            {
                Log.Info("Loot: would not take " + item.Name + " (" + why + ").");
                Notify.Failure(why);

                Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                _nextRepeat = Game.GameTime + RepeatMs * 3;
                return;
            }

            _tookAt = Game.GameTime;
            _tookName = item.Name;
            _nextRepeat = Game.GameTime + RepeatMs;

            Hud.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");

            if (Took != null) Took(item);

            if (_selected >= _body.Items.Count) _selected = Math.Max(0, _body.Items.Count - 1);
            _lastSelected = -1;
            _pickedAt = Game.GameTime;
        }

        /// <summary>
        /// The lot, in one press.
        ///
        /// FROM THE END BACKWARDS, because taking one shortens the list under the cursor and
        /// walking forwards through a list that is shrinking skips every other entry. It also
        /// stops at the first refusal rather than grinding through fourteen "pockets are full"
        /// messages.
        /// </summary>
        private void Everything()
        {
            var moved = 0;

            for (var i = _body.Items.Count - 1; i >= 0; i--)
            {
                var item = _body.Items[i];

                string why;

                if (!_bodies.Take(_body, item, out why))
                {
                    if (moved == 0)
                    {
                        Notify.Failure(why);
                        Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                    }

                    break;
                }

                moved++;
                _tookName = item.Name;

                if (Took != null) Took(item);
            }

            if (moved == 0)
            {
                _nextRepeat = Game.GameTime + RepeatMs * 3;
                return;
            }

            _tookAt = Game.GameTime;
            _nextRepeat = Game.GameTime + RepeatMs * 2;

            _selected = 0;
            _lastSelected = -1;
            _pickedAt = Game.GameTime;

            Hud.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        // ---- drawing ------------------------------------------------------------

        public void Draw()
        {
            if (!IsOpen || _body == null) return;

            var count = _body.Items.Count;

            var rows = count == 0 ? 1 : (count + Columns - 1) / Columns;
            if (rows > MaxRows) rows = MaxRows;

            var panelWidth = Hud.ToX(PanelWidthH);
            var pad = Hud.ToX(PadH);

            var tileW = (panelWidth - pad * 2f) / Columns;
            var tileH = TileH;

            var height = UiKit.HeadH + CardH + GridPad + rows * tileH + GridPad + NoteH + UiKit.FootH;

            var left = 0.5f - panelWidth * 0.5f;
            var top = 0.5f - height * 0.5f + _curtain.Lift;

            var age = Game.GameTime - _openedAt;
            var arrive = age >= EnterMs ? 1f : age / (float)EnterMs;
            arrive = 1f - (1f - arrive) * (1f - arrive);
            top += EnterRise * (1f - arrive);

            Theme.Panel(left, top, panelWidth, height, arrive);

            var x = left + pad;
            var right = left + panelWidth - pad;
            var wide = right - x;

            var y = UiKit.Head(left, top, panelWidth, pad, "people.png", "THE BODY",
                               "what was on him", _body.Affiliation.ToUpperInvariant(), arrive);

            Identity(x, y, wide, arrive);

            y += CardH + GridPad;

            _glide.Begin();

            Grid(x, y, tileW, tileH, rows, arrive);

            y += rows * tileH + GridPad;

            Note(x, y, wide, arrive);

            Foot(x, right, top + height - UiKit.FootH + 0.004f, arrive);

            _glide.Draw(arrive);
        }

        /// <summary>
        /// The card: his photograph, his name, and the four things a card carries.
        ///
        /// TWO COLUMNS OF LABELLED FIELDS rather than a sentence, because that is what an
        /// identity card looks like and the shape is doing as much work here as the words.
        /// </summary>
        private void Identity(float x, float y, float wide, float arrive)
        {
            var ink = Palette.Alpha(Palette.Text, (int)(255f * arrive));
            var dim = Palette.Alpha(Palette.TextDim, (int)(190f * arrive));

            var photoW = Hud.ToX(PhotoH);

            // The plate the picture sits on, so the space reads as a photograph before there
            // is one in it.
            Hud.RectFrom(x, y, photoW, PhotoH, Color.FromArgb((int)(30f * arrive), 255, 255, 255));

            var made = Made();

            if (!string.IsNullOrEmpty(made))
            {
                Hud.Sprite(made, made, x + photoW * 0.5f, y + PhotoH * 0.5f, photoW, PhotoH, 0f,
                           Color.FromArgb((int)(255f * arrive), 255, 255, 255));
            }
            else
            {
                Hud.Text("NO PHOTO", x + photoW * 0.5f, y + PhotoH * 0.5f - 0.008f, 0.24f,
                         Palette.Alpha(Palette.TextDim, (int)(150f * arrive)), Hud.FontLabel);
            }

            var tx = x + photoW + 0.010f;
            var room = wide - photoW - 0.010f;

            Hud.Text(Hud.Fit(_body.Name, room, 0.42f, Hud.FontBody), tx, y - 0.002f, 0.42f,
                     ink, Hud.FontBody, centre: false);

            // The four fields, two to a line, on the same grid so the labels line up.
            var half = room * 0.5f;
            var line = y + 0.030f;

            Field(tx, line, "D.O.B.", _body.Born, dim, ink);
            Field(tx + half, line, "HEIGHT", _body.Height, dim, ink);

            line += 0.024f;

            Field(tx, line, "ETHNICITY", _body.Ethnicity, dim, ink);
            Field(tx + half, line, "AFFILIATION", _body.Affiliation, dim, ink);
        }

        private static void Field(float x, float y, string label, string value, Color dim, Color ink)
        {
            Hud.Text(label, x, y, 0.21f, dim, Hud.FontLabel, centre: false);
            Hud.Text(value, x, y + 0.011f, 0.29f, ink, Hud.FontBody, centre: false);
        }

        private void Grid(float x, float y, float tileW, float tileH, int rows, float arrive)
        {
            if (_body.Items.Count == 0)
            {
                Hud.Text("Nothing on him.", x + (tileW * Columns) * 0.5f,
                         y + rows * tileH * 0.5f - 0.012f, 0.30f,
                         Palette.Alpha(Palette.TextDim, (int)(190f * arrive)), Hud.FontBody);
                return;
            }

            var perPage = Columns * rows;
            var first = _page * perPage;

            var age = Game.GameTime - _openedAt;
            var grown = Theme.Grown(_pickedAt);

            for (var i = 0; i < perPage; i++)
            {
                var at = first + i;
                if (at >= _body.Items.Count) break;

                var col = i % Columns;
                var row = i / Columns;

                var land = UiKit.Landed(age, i * 35, EnterMs);

                var show = arrive * land;
                if (show <= 0.01f) continue;

                var tx = x + col * tileW;
                var ty = y + row * tileH + EnterRise * 0.5f * (1f - land);

                var picked = at == _selected;
                var lit = Theme.Lit(at, _selected, _lastSelected, grown);

                Square(_body.Items[at], tx, ty, tileW, tileH, lit, show, picked, grown);

                if (picked)
                {
                    _glide.Target(tx + Gap, ty + Gap * Hud.Aspect,
                                  tileW - Gap * 2f, tileH - Gap * 2f * Hud.Aspect);
                }
            }

            var pages = (_body.Items.Count + perPage - 1) / perPage;
            if (pages <= 1) return;

            Hud.TextRight((_page + 1) + " / " + pages, x + tileW * Columns - 0.004f,
                          y + rows * tileH - 0.018f, 0.22f,
                          Palette.Alpha(Palette.TextDim, (int)(190f * arrive)), Hud.FontLabel);
        }

        /// <summary>One square: the ground, the plate under the cursor, the picture, the chip.</summary>
        private void Square(LootItem item, float x, float y, float w, float h, float lit, float show,
                            bool picked, float grown)
        {
            var gx = Gap;
            var gy = Gap * Hud.Aspect;

            var tx = x + gx;
            var ty = y + gy;
            var tw = w - gx * 2f;
            var th = h - gy * 2f;

            Hud.RectFrom(tx, ty, tw, th, Color.FromArgb((int)(26f * show), 255, 255, 255));

            Theme.Plate(tx, ty, tw, th, lit * show);

            var swell = picked ? 1f + PickGrow * grown : 1f;
            var cx = tx + tw / 2f;
            var cy = ty + th * 0.44f;

            var ink = Theme.Ink(Palette.Alpha(item.Tint, (int)(230f * show)), lit);

            switch (item.Kind)
            {
                case LootKind.Gun:
                    if (!string.IsNullOrEmpty(item.Icon))
                    {
                        GunArt.Draw(item.Icon, cx, cy, tw * 0.86f * swell, th * 0.40f * swell, ink);
                    }
                    break;

                case LootKind.Money:
                    Hud.File("cash.png", cx, cy, th * 0.52f * swell, 0f, ink);
                    break;

                case LootKind.Food:
                    if (!string.IsNullOrEmpty(item.Icon))
                    {
                        Hud.File(item.Icon, cx, cy, th * 0.54f * swell, 0f, ink);
                    }
                    break;

                case LootKind.Drug:
                    var art = Icons.ForDrug(item.Id);

                    if (art.HasFile) Hud.File(art.File, cx, cy, th * 0.52f * swell, 0f, ink);
                    break;
            }

            if (string.IsNullOrEmpty(item.Tag)) return;

            const float chipH = 0.013f;

            var chipW = Hud.MeasureText(item.Tag, 0.22f, Hud.FontLabel) + Hud.ToX(0.007f);

            Hud.RectFrom(tx + tw - chipW, ty + th - chipH, chipW, chipH,
                         Color.FromArgb((int)(215f * show), 12, 13, 15));

            Hud.TextRight(item.Tag, tx + tw - 0.0022f, ty + th - chipH + 0.0005f, 0.22f,
                          Palette.Alpha(Palette.Text, (int)(255f * show)), Hud.FontLabel);
        }

        /// <summary>The line under the grid: the chosen thing, said properly.</summary>
        private void Note(float x, float y, float wide, float arrive)
        {
            Theme.Plate(x, y, wide, NoteH - 0.006f, 0.55f * arrive);

            var tx = x + 0.010f;

            // WHAT JUST CAME OFF HIM, for a moment, over the top of everything else. It is
            // the one thing worth saying at the instant it happens.
            var flash = UiKit.Flash(_tookAt, TookFlashMs);

            if (flash > 0f && !string.IsNullOrEmpty(_tookName))
            {
                Hud.Text("Took the " + _tookName.ToLowerInvariant() + ".", tx, y + 0.008f, 0.32f,
                         Palette.Alpha(Palette.Cash, (int)(255f * arrive)), Hud.FontBody, centre: false);
                return;
            }

            if (_body.Items.Count == 0)
            {
                Hud.Text("That's everything he had.", tx, y + 0.008f, 0.32f,
                         Palette.Alpha(Palette.TextDim, (int)(215f * arrive)), Hud.FontBody, centre: false);
                return;
            }

            var at = Clamp(_selected, 0, _body.Items.Count - 1);
            var item = _body.Items[at];

            var grown = Theme.Grown(_pickedAt);

            Theme.Caption(item.Name, tx, y + 0.008f, grown, 0.32f);

            var says = About(item);

            if (string.IsNullOrEmpty(says)) return;

            Hud.Text(Hud.Fit(says, wide - 0.014f, 0.25f, Hud.FontBody), tx, y + 0.030f, 0.25f,
                     Palette.Alpha(Palette.TextDim, (int)((110f + 90f * grown) * arrive)),
                     Hud.FontBody, centre: false);
        }

        private static string About(LootItem item)
        {
            switch (item.Kind)
            {
                case LootKind.Gun:
                    return item.Count > 0
                        ? "Comes with the " + item.Count + " rounds still in it."
                        : "Empty. You'll need to feed it.";

                case LootKind.Money:
                    return "Straight in your pocket.";

                case LootKind.Food:
                    return "He wasn't going to eat it.";

                case LootKind.Drug:
                    return UiKit.PurityWord(item.Purity) + " -- " +
                           Math.Round(item.Purity * 100f) + "% pure, bagged up.";
            }

            return "";
        }

        private void Foot(float x, float right, float y, float arrive)
        {
            Theme.Rule(x, y, right - x, arrive);

            var ky = y + 0.011f;

            UiKit.KeyRight(right, ky, UiKit.Back, "LEAVE HIM", arrive);

            if (_body.Items.Count == 0) return;

            var kx = UiKit.Key(x, ky, null, "arrow_leftright.png", "PICK", arrive);

            kx = UiKit.Key(kx, ky, UiKit.Drop, null, "TAKE IT", arrive);

            UiKit.Key(kx, ky, UiKit.All, null, "THE LOT", arrive);
        }

        private static int Clamp(int v, int lo, int hi)
        {
            return v < lo ? lo : v > hi ? hi : v;
        }

        // ---- controls -----------------------------------------------------------

        private static bool Pressed(Control control)
        {
            return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)control);
        }

        private static bool Held(Control control)
        {
            return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)control);
        }

        /// <summary>
        /// Everything off, the camera left on. He is knelt over a body with a screen up; the
        /// one thing he should still be able to do is look around.
        /// </summary>
        private static void LockControls()
        {
            Core.Fists.Off();

            Function.Call(Hash.DISABLE_ALL_CONTROL_ACTIONS, 0);

            foreach (var control in new[] { Control.LookLeftRight, Control.LookUpDown })
            {
                Function.Call(Hash.ENABLE_CONTROL_ACTION, 0, (int)control, true);
            }
        }
    }
}
