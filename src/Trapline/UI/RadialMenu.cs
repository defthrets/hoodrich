using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using Trapline.Core;

namespace Trapline.UI
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
                Notify.Failure("~r~Trapline:~s~ that action failed. See Trapline.log.");
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

        private bool ResolveWedgeMode()
        {
            switch (_cfg.RenderMode)
            {
                case WheelRenderMode.Node:
                    return false;
                case WheelRenderMode.Wedge:
                    Draw.EnsureTextureDict(_cfg.WheelTextureDict);
                    return true;
                default:
                    // Auto: only use wedges once the dict is actually resident.
                    return Draw.EnsureTextureDict(_cfg.WheelTextureDict);
            }
        }

        public void Render()
        {
            var page = Current;
            if (!IsOpen || page == null) return;

            if (_cfg.RenderMode == WheelRenderMode.Auto && !_wedgeMode)
            {
                // Keep trying: the dict usually lands within a frame or two of the request.
                _wedgeMode = Draw.EnsureTextureDict(_cfg.WheelTextureDict);
            }

            var t = Ease((Game.GameTime - _openedAt) / (float)OpenAnimationMs);

            const float cx = 0.5f;
            const float cy = 0.5f;
            var rInner = _cfg.InnerRadius * t;
            var rOuter = _cfg.OuterRadius * t;

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
                    Draw.Wedge(_cfg.WheelTextureDict, _cfg.WheelTexture,
                                  cx, cy, rInner, outer, from, to, fill, SlicesPerSegment);
                }
                else
                {
                    DrawCard(cx, cy, (rInner + rOuter) * 0.5f, mid, rOuter - rInner, step, fill, hovered);
                }

                DrawSegmentLabel(cx, cy, (rInner + rOuter) * 0.5f, mid, item, hovered, t);
            }

            DrawHub(cx, cy, rInner, page, t);
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

        private static void DrawSegmentLabel(float cx, float cy, float rMid, float midAngleDeg,
                                             WheelItem item, bool hovered, float t)
        {
            if (t < 0.75f) return; // Hold the text back until the ring has nearly finished opening.

            var rad = midAngleDeg * (float)(Math.PI / 180.0);
            var px = cx + Draw.ToX(rMid * (float)Math.Sin(rad));
            var py = cy - rMid * (float)Math.Cos(rad);

            var colour = !item.Enabled
                ? Palette.TextDisabled
                : hovered ? Palette.TextOnHover : Palette.Text;

            if (!string.IsNullOrEmpty(item.Symbol))
            {
                Draw.Text(item.Symbol, px, py - 0.042f, 0.62f, colour, Draw.FontChaletComprimeCologne);
            }

            Draw.Text(item.Label.ToUpperInvariant(), px, py + 0.004f, 0.34f, colour,
                         Draw.FontChaletComprimeCologne);
        }

        private void DrawHub(float cx, float cy, float rInner, WheelPage page, float t)
        {
            if (_wedgeMode)
            {
                Draw.Disc(_cfg.WheelTextureDict, _cfg.WheelTexture, cx, cy, rInner * 0.96f,
                             Palette.Alpha(Palette.Hub, (int)(Palette.Hub.A * t)));
            }
            else
            {
                Draw.RectUniform(cx, cy, rInner * 1.7f, rInner * 1.7f,
                                    Palette.Alpha(Palette.Hub, (int)(Palette.Hub.A * t)));
            }

            if (t < 0.75f) return;

            var item = HoveredItem;

            // Title line: page name, or a breadcrumb once nested.
            var title = _stack.Count > 1 ? "< " + page.Title : page.Title;
            Draw.Text(title.ToUpperInvariant(), cx, cy - rInner * 0.72f, 0.32f, Palette.Accent);

            if (item != null)
            {
                var detail = !item.Enabled && !string.IsNullOrEmpty(item.DisabledReason)
                    ? item.DisabledReason
                    : item.Detail;

                Draw.Text(item.Label.ToUpperInvariant(), cx, cy - rInner * 0.18f, 0.40f, Palette.Text);

                if (!string.IsNullOrEmpty(item.Value))
                {
                    Draw.Text(item.Value, cx, cy + rInner * 0.16f, 0.36f,
                                 item.Enabled ? Palette.Cash : Palette.TextDisabled);
                }

                if (!string.IsNullOrEmpty(detail))
                {
                    Draw.Text(detail, cx, cy + rInner * 0.46f, 0.26f,
                                 item.Enabled ? Palette.TextDim : Palette.Warn);
                }
            }
            else if (!string.IsNullOrEmpty(page.Subtitle))
            {
                Draw.Text(page.Subtitle, cx, cy - rInner * 0.08f, 0.28f, Palette.TextDim);
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
