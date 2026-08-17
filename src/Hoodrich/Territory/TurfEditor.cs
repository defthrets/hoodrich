using System;
using System.Drawing;
using Keys = System.Windows.Forms.Keys;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Gangs;
using Hud = Hoodrich.UI.Draw;
using Hoodrich.UI;

namespace Hoodrich.Territory
{
    /// <summary>
    /// Walks a turf block onto the streets it belongs on.
    ///
    /// The shipped rectangles in turf.json are placed by eye from map coordinates, which gets
    /// them into the right neighbourhood but not onto the right blocks. Rather than guess
    /// harder, this lets the player stand on the corner they mean and shove the rectangle
    /// there: the map is open while editing, so the shading updates as they move it.
    ///
    /// Edits are saved to Documents\Hoodrich\turf.json, which overrides the shipped file, so
    /// updating the mod never throws the player's work away.
    /// </summary>
    internal sealed class TurfEditor
    {
        private const float NudgeMetres = 5f;
        private const float FastNudgeMetres = 25f;
        private const float ResizeMetres = 10f;
        private const float RotateDegrees = 5f;

        private readonly TurfAreas _areas;
        private readonly Affiliation _crew;
        private readonly GangRegistry _gangs;
        private readonly TurfWatch _turf;

        private TurfArea _editing;
        private bool _dirty;

        /// <summary>Repeat delay for a held key, so one press does not slide the block off the map.</summary>
        private int _nextRepeat;

        public TurfEditor(TurfAreas areas, GangRegistry gangs, Affiliation crew, TurfWatch turf)
        {
            _areas = areas;
            _gangs = gangs;
            _crew = crew;
            _turf = turf;
        }

        public bool IsActive => _editing != null;

        /// <summary>
        /// Starts editing the block nearest the player, creating one under their feet if there
        /// is nothing close by. Editing without a gang is meaningless, so it needs affiliation.
        /// </summary>
        public void Start()
        {
            if (!_crew.IsAffiliated)
            {
                Notify.Failure("Join a crew before you start drawing up their blocks.");
                return;
            }

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            var pos = player.Position;
            var gang = _crew.Current;

            var nearest = _areas.Nearest(pos);
            if (nearest != null && string.Equals(nearest.GangId, gang.Id, StringComparison.OrdinalIgnoreCase))
            {
                _editing = nearest;
            }
            else
            {
                _editing = new TurfArea
                {
                    GangId = gang.Id,
                    Zone = _turf.ZoneCode,
                    Name = _turf.ZoneName,
                    X = pos.X,
                    Y = pos.Y,
                    Width = 200f,
                    Height = 200f,
                    Rotation = 30f
                };
                _areas.Add(_editing);
                _dirty = true;
            }

            // The whole point is watching the shading move, so open the map.
            Notify.Ticker("~y~Editing " + _editing.Name + "~s~. Open the map (Pause > Map) to see it.");
        }

        public void Stop(bool save)
        {
            if (_editing == null) return;

            _editing = null;

            if (save && _dirty)
            {
                _areas.Save();
                Notify.Important("Blocks saved.");
            }

            _dirty = false;
        }

        public void Update()
        {
            if (_editing == null) return;

            // Losing the gang mid-edit would leave a block nobody owns.
            if (!_crew.IsAffiliated)
            {
                Stop(true);
                return;
            }

            HandleKeys();
        }

        private void HandleKeys()
        {
            if (Game.IsKeyPressed(Keys.Escape) || Game.IsKeyPressed(Keys.Back))
            {
                Stop(true);
                return;
            }

            if (Game.IsKeyPressed(Keys.Delete))
            {
                var gone = _editing;
                _editing = null;
                _areas.Remove(gone);
                _dirty = true;
                _areas.Save();
                Notify.Ticker("Block removed.");
                return;
            }

            if (Game.IsKeyPressed(Keys.Enter))
            {
                Stop(true);
                return;
            }

            if (Game.GameTime < _nextRepeat) return;

            var fast = Game.IsKeyPressed(Keys.ShiftKey);
            var step = fast ? FastNudgeMetres : NudgeMetres;
            var moved = false;

            // North/east are world axes, which is what the map shows, so the block moves the
            // way the arrow points on screen regardless of which way the player faces.
            if (Game.IsKeyPressed(Keys.Up))    { _editing.Y += step; moved = true; }
            if (Game.IsKeyPressed(Keys.Down))  { _editing.Y -= step; moved = true; }
            if (Game.IsKeyPressed(Keys.Right)) { _editing.X += step; moved = true; }
            if (Game.IsKeyPressed(Keys.Left))  { _editing.X -= step; moved = true; }

            if (Game.IsKeyPressed(Keys.PageUp))   { _editing.Width += ResizeMetres; _editing.Height += ResizeMetres; moved = true; }
            if (Game.IsKeyPressed(Keys.PageDown)) { _editing.Width = Math.Max(20f, _editing.Width - ResizeMetres);
                                                    _editing.Height = Math.Max(20f, _editing.Height - ResizeMetres); moved = true; }

            if (Game.IsKeyPressed(Keys.Home)) { _editing.Height += ResizeMetres; moved = true; }
            if (Game.IsKeyPressed(Keys.End))  { _editing.Height = Math.Max(20f, _editing.Height - ResizeMetres); moved = true; }

            if (Game.IsKeyPressed(Keys.OemOpenBrackets))  { _editing.Rotation = Wrap(_editing.Rotation - RotateDegrees); moved = true; }
            if (Game.IsKeyPressed(Keys.OemCloseBrackets)) { _editing.Rotation = Wrap(_editing.Rotation + RotateDegrees); moved = true; }

            // Snap the block onto where the player is standing, which is faster than nudging
            // it across the map one step at a time.
            if (Game.IsKeyPressed(Keys.Space))
            {
                var player = Game.Player.Character;
                if (player != null && player.Exists())
                {
                    _editing.X = player.Position.X;
                    _editing.Y = player.Position.Y;
                    _editing.Zone = _turf.ZoneCode;
                    moved = true;
                }
            }

            if (!moved) return;

            _nextRepeat = Game.GameTime + 90;
            _dirty = true;
            _areas.Touch();
        }

        private static float Wrap(float deg)
        {
            while (deg < 0f) deg += 360f;
            while (deg >= 360f) deg -= 360f;
            return deg;
        }

        public void Draw()
        {
            if (_editing == null) return;

            DrawCorners();

            const float x = 0.015f;
            var y = 0.30f;

            Hud.Rect(x - 0.005f, y - 0.012f, 0.235f, 0.30f, Color.FromArgb(180, 0, 0, 0));

            Hud.Text("EDITING TURF BLOCK", x, y, 0.34f, Palette.Warn, Hud.FontLabel);
            y += 0.030f;

            Hud.Text(_editing.Name + "  (" + _editing.Zone + ")", x, y, 0.30f, Palette.Text, Hud.FontBody);
            y += 0.026f;

            Hud.Text(((int)_editing.Width) + " x " + ((int)_editing.Height) + " m   " +
                     ((int)_editing.Rotation) + " deg", x, y, 0.28f, Palette.TextDim, Hud.FontBody);
            y += 0.034f;

            Line(x, ref y, "Arrows", "move  (Shift = far)");
            Line(x, ref y, "Space", "snap to me");
            Line(x, ref y, "PgUp / PgDn", "bigger / smaller");
            Line(x, ref y, "Home / End", "longer / shorter");
            Line(x, ref y, "[  ]", "rotate");
            Line(x, ref y, "Del", "delete block");
            Line(x, ref y, "Enter / Esc", "save and finish");
        }

        private static void Line(float x, ref float y, string key, string what)
        {
            Hud.Text(key, x, y, 0.27f, Palette.Cash, Hud.FontBody);
            Hud.Text(what, x + 0.075f, y, 0.27f, Palette.TextDim, Hud.FontBody);
            y += 0.024f;
        }

        /// <summary>
        /// Marks the rectangle's corners in the world, so it can be lined up against real
        /// kerbs without going in and out of the map every nudge.
        /// </summary>
        private void DrawCorners()
        {
            var gang = _gangs.Get(_editing.GangId);
            var colour = gang == null ? Color.White : gang.Colour;

            var rad = _editing.Rotation * (float)Math.PI / 180f;
            var cos = (float)Math.Cos(rad);
            var sin = (float)Math.Sin(rad);

            var hw = _editing.Width * 0.5f;
            var hh = _editing.Height * 0.5f;

            // Corners far from the player sit on unstreamed terrain, where the ground probe
            // fails; the player's own height is the closest thing to a sensible guess.
            var player = Game.Player.Character;
            var fallbackZ = player != null && player.Exists() ? player.Position.Z : 0f;

            for (var i = 0; i < 4; i++)
            {
                var lx = (i == 0 || i == 3) ? -hw : hw;
                var ly = (i < 2) ? hh : -hh;

                var wx = _editing.X + lx * cos - ly * sin;
                var wy = _editing.Y + lx * sin + ly * cos;

                var z = fallbackZ;
                try
                {
                    if (World.GetGroundHeight(new Vector3(wx, wy, 400f), out var groundZ, GetGroundHeightMode.Normal))
                    {
                        z = groundZ;
                    }
                }
                catch
                {
                    // Unstreamed corner; the marker still shows at the guessed height.
                }

                try
                {
                    Function.Call(Hash.DRAW_MARKER, 1, wx, wy, z - 1f, 0f, 0f, 0f, 0f, 0f, 0f,
                                  4f, 4f, 6f, colour.R, colour.G, colour.B, 120,
                                  false, false, 2, false, 0, 0, false);
                }
                catch
                {
                    // Markers are a convenience, never a reason to fail the edit.
                }
            }
        }
    }
}
