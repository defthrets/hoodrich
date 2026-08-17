using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using Hoodrich.Core;

namespace Hoodrich.UI
{
    /// <summary>
    /// The wheel widget: page stack, hover hit-testing and rendering.
    ///
    /// It owns no input bindings and no game state. <see cref="WheelController"/> feeds it a
    /// selection vector and tells it when to commit, which keeps the geometry testable and
    /// lets the same widget serve keyboard, mouse and gamepad.
    /// </summary>
    internal sealed class RadialMenu
    {
        private const float SegmentGapDegrees = 1.6f;
        private const int SlicesPerSegment = 14;
        private const int OpenAnimationMs = 140;

        private readonly Settings _cfg;
        private readonly List<WheelPage> _stack = new List<WheelPage>();

        private int _openedAt;
        private int _hovered = -1;
        private int _lastHovered = -1;

        /// <summary>Resolved once per open: true if wedges can be drawn, false to fall back to cards.</summary>
        private bool _wedgeMode;

        public RadialMenu(Settings cfg)
        {
            _cfg = cfg;
        }

        public bool IsOpen { get; private set; }

        public WheelPage Current => _stack.Count > 0 ? _stack[_stack.Count - 1] : null;

        public int Depth => _stack.Count;

        /// <summary>Index of the hovered segment, or -1 when the cursor is in the dead zone.</summary>
        public int Hovered => _hovered;

        public WheelItem HoveredItem
        {
            get
            {
                var page = Current;
                if (page == null || _hovered < 0 || _hovered >= page.Items.Count) return null;
                return page.Items[_hovered];
            }
        }

        // ---- lifecycle ---------------------------------------------------------

        public void Open(WheelPage root)
        {
            if (root == null)
            {
                Log.Warn("RadialMenu.Open called with a null page.");
                return;
            }

            _stack.Clear();
            _stack.Add(root);
            _hovered = -1;
            _lastHovered = -1;
            _openedAt = Game.GameTime;
            IsOpen = true;
            _wedgeMode = ResolveWedgeMode();

            if (_cfg.PlaySounds) Draw.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            _stack.Clear();
            _hovered = -1;
            _lastHovered = -1;
        }

        /// <summary>Pops one page. Returns false when already at the root (caller should close).</summary>
        public bool Back()
        {
            if (_stack.Count <= 1) return false;

            _stack.RemoveAt(_stack.Count - 1);
            _hovered = -1;
            _lastHovered = -1;
            _openedAt = Game.GameTime;
            if (_cfg.PlaySounds) Draw.PlaySound("BACK", "HUD_FRONTEND_DEFAULT_SOUNDSET");
            return true;
        }

        /// <summary>
        /// Activates the hovered item. Returns true when the wheel should close
        /// (a leaf action fired); false when it stays open (submenu, or nothing hovered).
        /// </summary>
        public bool Commit()
        {
            var item = HoveredItem;
            if (item == null) return false;

            if (!item.Enabled)
            {
                if (_cfg.PlaySounds) Draw.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                return false;
            }

            if (item.IsSubmenu)
            {
                WheelPage sub = null;
                try
                {
                    sub = item.Submenu();
                }
                catch (Exception ex)
                {
                    Log.Error("Submenu builder for '" + item.Label + "' threw.", ex);
                }

                if (sub == null || sub.Items.Count == 0)
                {
                    if (_cfg.PlaySounds) Draw.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                    return false;
                }

                _stack.Add(sub);
                _hovered = -1;
                _lastHovered = -1;
                _openedAt = Game.GameTime;
                if (_cfg.PlaySounds) Draw.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                return false;
            }

            if (_cfg.PlaySounds) Draw.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");

            // Run the action outside the draw path; a throwing handler must not kill the script.
            try
            {
                item.OnSelect?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Error("Wheel action '" + item.Label + "' threw.", ex);
                Notify.Failure("~r~Hoodrich:~s~ that action failed. See Hoodrich.log.");
            }

            return true;
        }

        // ---- selection ---------------------------------------------------------

        /// <summary>
        /// Feeds the pointing direction. <paramref name="dirX"/> is right-positive and
        /// <paramref name="dirY"/> is up-positive, both roughly -1..1.
        /// </summary>
        public void UpdateSelection(float dirX, float dirY)
        {
            var page = Current;
            if (page == null || page.Items.Count == 0)
            {
                _hovered = -1;
                return;
            }

            var magnitude = (float)Math.Sqrt(dirX * dirX + dirY * dirY);
            if (magnitude < _cfg.DeadZone)
            {
                _hovered = -1;
                return;
            }

            var n = page.Items.Count;
            var step = 360f / n;

            // Clockwise from screen-up.
            var angle = (float)(Math.Atan2(dirX, dirY) * (180.0 / Math.PI));
            if (angle < 0f) angle += 360f;

            var index = (int)Math.Round(angle / step) % n;
            if (index < 0) index += n;

            _hovered = index;

            if (_hovered != _lastHovered)
            {
                if (_cfg.PlaySounds && _lastHovered != -1)
                {
                    Draw.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                }
                _lastHovered = _hovered;
            }
        }

        // ---- rendering ---------------------------------------------------------

        /// <summary>
        /// Wedges are rasterised with DRAW_RECT and need nothing streamed, so this is now purely
        /// a style preference rather than the fallback it started as.
        /// </summary>
        private bool ResolveWedgeMode() => _cfg.RenderMode != WheelRenderMode.Node;

        public void Render()
        {
            var page = Current;
            if (!IsOpen || page == null) return;

            var t = Ease((Game.GameTime - _openedAt) / (float)OpenAnimationMs);

            const float cx = 0.5f;
            const float cy = 0.5f;

            // The open animation fades rather than grows. Scaling the radii meant every frame
            // of it drew a smaller, part-formed ring, and a screenshot or a stutter caught
            // mid-animation showed a slice that looked broken rather than one that looked new.
            var rInner = _cfg.InnerRadius;
            var rOuter = _cfg.OuterRadius;

            Draw.SetDrawOrder(7);
            Draw.Rect(0.5f, 0.5f, 1f, 1f, Palette.Alpha(Palette.Backdrop, (int)(Palette.Backdrop.A * t)));

            var items = page.Items;
            var n = items.Count;
            if (n == 0)
            {
                DrawHub(cx, cy, rInner, page, t);
                return;
            }

            var step = 360f / n;
            var gap = n > 1 ? SegmentGapDegrees : 0f;

            for (var i = 0; i < n; i++)
            {
                var item = items[i];
                var mid = i * step;
                var from = mid - step * 0.5f + gap * 0.5f;
                var to = mid + step * 0.5f - gap * 0.5f;

                var hovered = i == _hovered;
                var fill = !item.Enabled
                    ? Palette.SegmentDisabled
                    : hovered
                        ? (item.Tint ?? Palette.SegmentHover)
                        : Palette.Segment;

                fill = Palette.Alpha(fill, (int)(fill.A * t));

                if (_wedgeMode)
                {
                    // A hovered segment reaches slightly further out, the way the vanilla wheel does.
                    var outer = hovered ? rOuter * 1.045f : rOuter;
                    Draw.Wedge(cx, cy, rInner, outer, from, to, fill);
                }
                else
                {
                    DrawCard(cx, cy, (rInner + rOuter) * 0.5f, mid, rOuter - rInner, step, fill, hovered);
                }

                DrawSegmentLabel(cx, cy, (rInner + rOuter) * 0.5f, mid, item, fill, t);
            }

            // Hairline ring on the outer edge, as the vanilla wheel has. Drawn after the
            // segments so a hovered wedge reaching past it still reads as breaking the line.
            if (_wedgeMode)
            {
                Draw.Arc(cx, cy, rOuter, 0f, 360f, 0.0022f,
                         Palette.Alpha(Palette.Ring, (int)(Palette.Ring.A * t)));
            }

            DrawHub(cx, cy, rInner, page, t);
            DrawPanel(page, t);
        }

        /// <summary>
        /// Stat block to the right of the wheel. Only drawn for pages that supply rows, and
        /// only once the open animation has settled so it does not pop in mid-slide.
        /// </summary>
        private void DrawPanel(WheelPage page, float t)
        {
            if (page.Panel.Count == 0 || t < 0.9f) return;

            const float rowHeight = 0.032f;
            const float padding = 0.018f;

            var width = 0.235f;
            var height = padding * 2f + rowHeight * (page.Panel.Count + (string.IsNullOrEmpty(page.PanelTitle) ? 0 : 1));

            var left = 0.5f + _cfg.OuterRadius / Draw.Aspect + 0.045f;
            var cx = left + width * 0.5f;
            var cy = 0.5f;
            var top = cy - height * 0.5f;

            Draw.Rect(cx, cy, width, height, Palette.Alpha(Palette.Hub, 225));

            var y = top + padding * 0.6f;

            // Header strip, the way GTA's own menus title a column.
            if (!string.IsNullOrEmpty(page.PanelTitle))
            {
                Draw.Rect(cx, top + rowHeight * 0.5f, width, rowHeight, Palette.PanelHeader);
                Draw.Rect(cx, top + rowHeight - 0.0015f, width, 0.003f, Palette.Accent);

                Draw.Text(page.PanelTitle.ToUpperInvariant(), left + padding * 0.5f, y, 0.30f,
                          Palette.Accent, Draw.FontLabel, centre: false);
                y += rowHeight;
            }

            var index = 0;
            foreach (var row in page.Panel)
            {
                // A blank label AND value is a deliberate spacer between groups of rows.
                var isSpacer = string.IsNullOrEmpty(row.Label) && string.IsNullOrEmpty(row.Value);

                if (!isSpacer && (index & 1) == 1)
                {
                    Draw.Rect(cx, y + rowHeight * 0.34f, width, rowHeight, Palette.PanelRowAlt);
                }

                if (!isSpacer)
                {
                    Draw.Text(row.Label, left + padding * 0.5f, y, 0.28f, Palette.TextDim,
                              Draw.FontBody, centre: false);

                    Draw.TextRight(row.Value, left + width - padding * 0.5f, y, 0.28f,
                                   row.Tint ?? Palette.Text, Draw.FontBody);
                }

                y += rowHeight;
                index++;
            }
        }

        /// <summary>Node-mode segment: an axis-aligned card centred on the segment's mid angle.</summary>
        private static void DrawCard(float cx, float cy, float rMid, float midAngleDeg,
                                     float radialSize, float stepDeg, Color fill, bool hovered)
        {
            var rad = midAngleDeg * (float)(Math.PI / 180.0);
            var px = cx + Draw.ToX(rMid * (float)Math.Sin(rad));
            var py = cy - rMid * (float)Math.Cos(rad);

            // Width scales with how much of the ring this segment owns, clamped to stay readable.
            var w = Math.Min(0.22f, Math.Max(0.11f, rMid * stepDeg / 90f));
            var h = radialSize * 0.72f;
            var scale = hovered ? 1.06f : 1f;

            Draw.RectUniform(px, py, w * scale, h * scale, fill);
            Draw.RectUniform(px, py, w * scale * 0.97f, h * scale * 0.9f, Palette.Alpha(fill, fill.A));
        }

        /// <summary>Weapon art is wider than it is tall; these are height-relative.</summary>
        private const float IconWidth = 0.115f;
        private const float IconHeight = 0.056f;

        private static void DrawSegmentLabel(float cx, float cy, float rMid, float midAngleDeg,
                                             WheelItem item, Color fill, float t)
        {
            if (t < 0.75f) return; // Hold the text back until the ring has nearly finished opening.

            var rad = midAngleDeg * (float)(Math.PI / 180.0);
            var px = cx + Draw.ToX(rMid * (float)Math.Sin(rad));
            var py = cy - rMid * (float)Math.Cos(rad);

            // Contrast against whatever the wedge actually is: gang tints run from pale yellow
            // to deep maroon, so a fixed dark-on-hover rule would lose half of them.
            var colour = !item.Enabled ? Palette.TextDisabled : Palette.TextOn(fill);

            if (item.HasIcon)
            {
                // The game's weapon art is a white silhouette, so it is tinted the same colour the
                // label uses -- dark when it sits on a highlighted wedge, white otherwise.
                // Height is fixed and width follows the art's own aspect, so a square inventory
                // sprite stays square and a long weapon silhouette stays long.
                var iconWidth = Math.Min(IconWidth, IconHeight * Math.Max(0.2f, item.IconAspect));

                Draw.Sprite(item.IconDict, item.IconTexture, px, py - 0.030f,
                            Draw.ToX(iconWidth), IconHeight, 0f, colour);
            }
            else if (!string.IsNullOrEmpty(item.Symbol))
            {
                Draw.Text(item.Symbol, px, py - 0.042f, 0.62f, colour, Draw.FontLabel);
            }

            Draw.Text(item.Label.ToUpperInvariant(), px, py + 0.010f, 0.34f, colour, Draw.FontLabel);
        }

        private void DrawHub(float cx, float cy, float rInner, WheelPage page, float t)
        {
            if (_wedgeMode)
            {
                Draw.Disc(cx, cy, rInner * 0.96f, Palette.Alpha(Palette.Hub, (int)(Palette.Hub.A * t)));
            }
            else
            {
                Draw.RectUniform(cx, cy, rInner * 1.7f, rInner * 1.7f,
                                    Palette.Alpha(Palette.Hub, (int)(Palette.Hub.A * t)));
            }

            if (t < 0.75f) return;

            var item = HoveredItem;

            // Title line: page name, or a breadcrumb once nested. Condensed, like the vanilla
            // wheel's category label.
            var title = _stack.Count > 1 ? "< " + page.Title : page.Title;
            Draw.Text(title.ToUpperInvariant(), cx, cy - rInner * 0.72f, 0.32f, Palette.Accent,
                      Draw.FontLabel);

            if (item != null)
            {
                var detail = !item.Enabled && !string.IsNullOrEmpty(item.DisabledReason)
                    ? item.DisabledReason
                    : item.Detail;

                // The item's own name gets the standard HUD face, the way the vanilla wheel
                // sets the selected weapon's name -- it is the thing you actually read.
                Draw.Text(item.Label.ToUpperInvariant(), cx, cy - rInner * 0.18f, 0.40f,
                          Palette.Text, Draw.FontBody);

                if (!string.IsNullOrEmpty(item.Value))
                {
                    Draw.Text(item.Value, cx, cy + rInner * 0.16f, 0.36f,
                              item.Enabled ? Palette.Cash : Palette.TextDisabled, Draw.FontLabel);
                }

                if (!string.IsNullOrEmpty(detail))
                {
                    Draw.Text(detail, cx, cy + rInner * 0.46f, 0.26f,
                              item.Enabled ? Palette.TextDim : Palette.Warn, Draw.FontBody);
                }
            }
            else if (!string.IsNullOrEmpty(page.Subtitle))
            {
                Draw.Text(page.Subtitle, cx, cy - rInner * 0.08f, 0.28f, Palette.TextDim,
                          Draw.FontBody);
            }
        }

        /// <summary>Ease-out cubic, clamped to 0..1.</summary>
        private static float Ease(float x)
        {
            if (x <= 0f) return 0f;
            if (x >= 1f) return 1f;
            var inv = 1f - x;
            return 1f - inv * inv * inv;
        }
    }
}
