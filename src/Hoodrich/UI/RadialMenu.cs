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
        /// <summary>
        /// The space between two segments.
        ///
        /// Wide enough to be seen at the inner radius, which is where a gap is narrowest --
        /// 1.6 degrees is two pixels there, and two pixels is not a division, it is a seam.
        /// </summary>
        private const float SegmentGapDegrees = 4f;

        /// <summary>How much of the ring a lone item takes, rather than all of it.</summary>
        private const float SingleSpanDegrees = 90f;
        private const int OpenAnimationMs = 140;

        /// <summary>
        /// The ring, and it is its own geometry rather than the ini's.
        ///
        /// InnerRadius and OuterRadius stay in Settings for anybody who wants to move the
        /// whole thing, but this design is drawn to a proportion: the band has to be deep
        /// enough for an icon at 0.072 and the hub wide enough for a name, a value and a line
        /// of detail. Reading those two out of an ini that defaults to 0.085 and 0.20 gave a
        /// band too thin for the icon and a hub too small for the words, which is most of what
        /// "janky" was.
        /// </summary>
        private const float RingInner = 0.124f;
        private const float RingOuter = 0.252f;

        /// <summary>Where a label sits, measured out from the rim.</summary>
        private const float LabelOut = 0.026f;

        /// <summary>
        /// The keel: an amber bar along the hovered wedge's INNER edge.
        ///
        /// Inner rather than outer, because the hub is where the answer is written and this
        /// draws the eye along the exact path the reading takes. It also reads on a near-white
        /// wedge AND against the near-black hub, which no single fill colour does.
        /// </summary>
        private const float KeelDepth = 0.016f;

        /// <summary>Radial thickness of the bands that outline an empty slot.</summary>
        private const float FrameBand = 0.0055f;

        /// <summary>The icon IS the wedge. Everything else on a wedge is secondary to it.</summary>
        private const float WedgeIcon = 0.072f;
        private const float LockIcon = 0.028f;

        /// <summary>An unselected wedge, a touch stronger than Palette.Segment on its own.</summary>
        private const int SegmentAlpha = 235;

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

        /// <summary>
        /// The lowest thing the wheel draws, so whatever comes after it knows where to start.
        ///
        /// The footer hint used to be placed at the ini's OuterRadius plus a margin. The ring
        /// is drawn to its own proportion now and is deeper than the ini default, so that put
        /// the hint ON the ring -- and on a page with stat rows it put it through the middle
        /// of them. It asks here instead.
        /// </summary>
        public float BottomEdge
        {
            get
            {
                var page = Current;
                var bottom = 0.5f + RingOuter + LabelOut + 0.030f;

                if (page == null || page.Panel.Count == 0) return bottom;

                var half = (page.Panel.Count + 1) / 2;
                var cutAt = half;

                for (var i = 0; i < page.Panel.Count; i++)
                {
                    var row = page.Panel[i];
                    var isHead = string.IsNullOrEmpty(row.Value) && !string.IsNullOrEmpty(row.Label);
                    if (isHead && Math.Abs(i - half) <= 2) { cutAt = i; break; }
                }

                var tall = Math.Max(cutAt, page.Panel.Count - cutAt);
                return 0.5f + RingOuter + 0.046f + 0.032f + 0.010f * 2f + tall * 0.030f;
            }
        }

        public WheelPage Current => _stack.Count > 0 ? _stack[_stack.Count - 1] : null;

        public int Depth => _stack.Count;

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
            var rInner = RingInner;
            var rOuter = RingOuter;

            Draw.SetDrawOrder(7);
            Draw.Rect(0.5f, 0.5f, 1f, 1f, Palette.Alpha(Palette.Backdrop, (int)(Palette.Backdrop.A * t)));

            var items = page.Items;
            var n = items.Count;
            if (n == 0)
            {
                DrawHub(cx, cy, rInner, page, t);
                return;
            }

            // The hub goes down BEFORE the wedges, and the hovered wedge before the rest.
            //
            // Nothing overlaps -- the hub disc ends exactly where the ring begins -- so the
            // order makes no visual difference at all. It makes a difference when the frame's
            // draw-call budget runs out, because GTA does not report that, it just stops
            // drawing. Whatever is emitted last is what disappears, so the two things you
            // cannot do without go first: the readout you are reading, and the wedge you are
            // pointing at.
            DrawHub(cx, cy, rInner, page, t);

            var step = 360f / n;
            var gap = n > 1 ? SegmentGapDegrees : 0f;

            // ONE item is not a ring, and drawing it as one looks like a fault.
            //
            // A single wedge spans the full 360 with no gap, so it comes out as a solid disc
            // with the hub floating in the middle of it -- and when that one item is hovered,
            // the disc is near-white and fills a third of the screen. The keel goes all the way
            // round with it. Pages that end up with one thing to offer are common: the re-up
            // page is exactly that until the port opens.
            //
            // Given a slice instead. It reads as a wheel with one thing on it, which is what
            // it is, and it leaves the rest of the ring as empty backdrop rather than as a
            // statement.
            if (n == 1) step = SingleSpanDegrees;

            // Every segment is its own wedge.
            //
            // It was drawing ONE ring in the segment colour and then overdrawing only the
            // hovered and disabled ones, to save rectangles. That is where the jank came from,
            // and all of it:
            //
            //   * A gap between two enabled segments cannot exist, because the ring underneath
            //     is the same colour -- so the wheel read as one solid doughnut with a bite out
            //     of it rather than as a row of choices.
            //   * Divisions therefore had to be faked with spokes, drawn AFTER the fills, so
            //     they landed on top of the highlight and vanished into it.
            //   * A disabled segment was translucent black composited over translucent black.
            //     Measured: four to six values out of 255 different from an enabled one. There
            //     was no way to tell an option you cannot pick from one you can.
            //   * The edges had to be drawn back on as hairline arcs, and those arcs were
            //     dotted -- see the note that used to be on Draw.Arc.
            //
            // And it did not even save anything. Counted at 1080p: the ring, five spokes and
            // three arcs come to 948 rectangles a frame, where five plain wedges and the hub
            // come to 593. The optimisation was thirty to forty per cent MORE expensive than
            // the thing it replaced, on top of causing every visual defect above.
            // Hovered first, then the rest in order. Same picture, different survival odds.
            for (var slot = 0; slot < n; slot++)
            {
                var i = slot == 0 ? Math.Max(0, _hovered)
                      : slot <= Math.Max(0, _hovered) ? slot - 1
                      : slot;

                if (_hovered < 0) i = slot;

                var item = items[i];
                var mid = i * step;
                var from = mid - step * 0.5f + gap * 0.5f;
                var to = mid + step * 0.5f - gap * 0.5f;

                var hovered = i == _hovered;

                // A thing you cannot pick is an EMPTY SLOT, not a darker version of a filled
                // one. Measured on the old build, a disabled wedge came out four to six values
                // out of 255 from an enabled one -- translucent grey over translucent black is
                // not a state, it is a rounding error. An outlined hole is a different shape,
                // and shape is the only thing that survives being composited over both a
                // bright street and a dark alley.
                if (!item.Enabled)
                {
                    if (!Sprite("wheel_slot_", n, mid, rOuter,
                                Color.FromArgb((int)(195 * t), 84, 88, 96)))
                    {
                        DrawEmptySlot(cx, cy, rInner, rOuter, from, to, t);
                    }
                    else
                    {
                        // The dark inside of the slot, which the outline sits on.
                        Draw.Wedge(cx, cy, rInner, rOuter, from, to,
                                   Color.FromArgb((int)(155 * t), 0, 0, 0));
                    }

                    continue;
                }

                var fill = hovered ? (item.Tint ?? Palette.SegmentHover)
                                   : Palette.Alpha(Palette.Segment, SegmentAlpha);

                fill = Palette.Alpha(fill, (int)(fill.A * t));

                if (_wedgeMode)
                {
                    // Artwork first, rectangles only if it is not there.
                    //
                    // A wedge drawn as one rotated sprite is the same shape with its edges
                    // resolved properly, for one draw call instead of a hundred. See Sprite()
                    // below for why that matters as much as how it looks.
                    if (!Sprite("wheel_seg_", n, mid, rOuter, fill))
                    {
                        Draw.Wedge(cx, cy, rInner, rOuter, from, to, fill);
                    }

                    if (hovered &&
                        !Sprite("wheel_keel_", n, mid, rOuter,
                                Palette.Alpha(Palette.Warn, (int)(255 * t))))
                    {
                        Draw.Wedge(cx, cy, rInner, rInner + KeelDepth, from, to,
                                   Palette.Alpha(Palette.Warn, (int)(255 * t)));
                    }
                }
                else
                {
                    DrawCard(cx, cy, (rInner + rOuter) * 0.5f, mid, rOuter - rInner, step, fill, hovered);
                }
            }

            // Icons and labels in their own passes, AFTER every wedge is down.
            //
            // A label now sits outside the rim, which means it overlaps the neighbouring
            // wedge's airspace. Drawn inline with the fills, the next wedge painted over it.
            for (var i = 0; i < n; i++)
            {
                DrawWedgeIcon(cx, cy, (rInner + rOuter) * 0.5f, i * step, items[i], i == _hovered, t);
            }

            for (var i = 0; i < n; i++)
            {
                DrawWedgeLabel(cx, cy, rOuter + LabelOut, i * step, items[i], i == _hovered, t);
            }

            DrawPlinth(page, rOuter, t);
        }
        /// <summary>Clear space kept between a panel row's label and its value.</summary>
        private const float RowGutter = 0.012f;

        /// <summary>
        /// How tall a side-panel row's art is, as a fraction of screen height.
        ///
        /// Matched to the 0.28 body text it sits beside rather than to the row box, so it
        /// reads as part of the line rather than as a bullet in front of it.
        /// </summary>
        private const float PanelArt = 0.017f;

        /// <summary>
        /// One piece of the ring, drawn as artwork.
        ///
        /// Every filled shape this HUD can make is a DRAW_RECT, and a circle built out of
        /// rectangles is a staircase. The only way to shrink the steps is more rectangles, and
        /// that is what put the wheel over the frame's draw budget and made the last wedge
        /// disappear -- measured at 954 rectangles at 1080p and 1,382 at 1440p for rows fine
        /// enough to look smooth, against about 593 for the wheel this replaced.
        ///
        /// So the shape is drawn once, offline, at 1024 with real anti-aliasing, and rotated
        /// into place. One call, no staircase, and the same file serves every position because
        /// the art points straight up and the rotation does the rest.
        ///
        /// A set per item count, because a fifth of a ring is not the same shape as an eighth.
        /// Outside 2..8 there is no art and this returns false, which is not a failure -- the
        /// caller falls back to the rectangles, which is exactly what it did before.
        /// </summary>
        private static bool Sprite(string prefix, int items, float midAngleDeg, float rOuter,
                                   Color c)
        {
            if (items < 1 || items > 8) return false;

            return Draw.File(prefix + items + ".png", 0.5f, 0.5f, rOuter * 2f, midAngleDeg, c);
        }

        /// <summary>
        /// A slot with nothing in it, which is what a thing you cannot pick looks like.
        ///
        /// Four thin wedges make the outline: a band at each radius and a sliver at each
        /// edge. That is more draw calls than a flat fill, and it buys the one thing a fill
        /// cannot -- a silhouette. Colour alone was measured at four to six values out of 255
        /// between disabled and enabled, which is invisible on a bright street and invisible
        /// again in a dark alley.
        /// </summary>
        private static void DrawEmptySlot(float cx, float cy, float rInner, float rOuter,
                                          float from, float to, float t)
        {
            var interior = Color.FromArgb((int)(155 * t), 0, 0, 0);
            var edge = Color.FromArgb((int)(195 * t), 84, 88, 96);

            Draw.Wedge(cx, cy, rInner, rOuter, from, to, interior);

            Draw.Wedge(cx, cy, rOuter - FrameBand, rOuter, from, to, edge);
            Draw.Wedge(cx, cy, rInner, rInner + FrameBand, from, to, edge);

            // The two radial slivers, converted from a thickness to an angle at the mid
            // radius so they come out the same width as the bands rather than as wedges.
            var rMid = (rInner + rOuter) * 0.5f;
            var slice = (float)(FrameBand / rMid * (180.0 / Math.PI));

            Draw.Wedge(cx, cy, rInner, rOuter, from, from + slice, edge);
            Draw.Wedge(cx, cy, rInner, rOuter, to - slice, to, edge);
        }

        /// <summary>
        /// The icon, centred in the band and big enough to be the wedge rather than decorate it.
        ///
        /// It used to sit at py-0.030 with the label at py+0.010, which left a hole between
        /// them and made both small. The label is outside the ring now, so the whole band
        /// belongs to the picture.
        /// </summary>
        private static void DrawWedgeIcon(float cx, float cy, float rMid, float midAngleDeg,
                                          WheelItem item, bool hovered, float t)
        {
            if (t < 0.75f) return;

            var rad = midAngleDeg * (float)(Math.PI / 180.0);
            var px = cx + Draw.ToX(rMid * (float)Math.Sin(rad));
            var py = cy - rMid * (float)Math.Cos(rad);

            if (!item.Enabled)
            {
                DrawArtOrGlyph(item, px, py - 0.014f, WedgeIcon * 0.86f,
                               Palette.Alpha(Palette.TextDisabled, 115));

                Draw.File("locked.png", px, py + 0.042f, LockIcon, 0f,
                          Palette.Alpha(Palette.TextDisabled, 235));
                return;
            }

            var fill = hovered ? (item.Tint ?? Palette.SegmentHover)
                               : Palette.Alpha(Palette.Segment, SegmentAlpha);

            DrawArtOrGlyph(item, px, py, WedgeIcon, Palette.TextOn(fill));
        }

        /// <summary>Our PNG, the game's sprite, a blip tag or the text glyph -- in that order.</summary>
        private static void DrawArtOrGlyph(WheelItem item, float px, float py, float size, Color ink)
        {
            // Ours first, and asked directly rather than through HasIcon -- which wants a
            // texture dictionary a PNG does not have.
            if (!string.IsNullOrEmpty(item.IconFile) &&
                Draw.File(item.IconFile, px, py, size, 0f, ink))
            {
                return;
            }

            if (!string.IsNullOrEmpty(item.IconBlip))
            {
                Draw.Text(item.IconBlip, px, py - size * 0.5f, size * 9f, ink, Draw.FontChaletLondon);
                return;
            }

            if (item.HasIcon)
            {
                var aspect = item.IconAspect;
                if (aspect < 0.25f || aspect > 4f || float.IsNaN(aspect)) aspect = 1f;

                Draw.Sprite(item.IconDict, item.IconTexture, px, py,
                            Draw.ToX(size * aspect), size, 0f, ink);
                return;
            }

            if (!string.IsNullOrEmpty(item.Symbol))
            {
                Draw.Text(item.Symbol, px, py - size * 0.5f, size * 9f, ink, Draw.FontLabel);
            }
        }

        /// <summary>
        /// The label, OUTSIDE the ring, on its own wedge's angle.
        ///
        /// Inside the band it competed with the icon for a space that could not hold both.
        /// Outside it has as much room as it wants, the icon gets the whole wedge, and the
        /// ring reads as a ring of pictures with names round it rather than as a ring of
        /// cramped cards.
        ///
        /// Pushed clear by half its own measured width on whichever side it is on, so a label
        /// at three o'clock starts at the rim instead of straddling it, and one at twelve is
        /// left centred.
        /// </summary>
        private static void DrawWedgeLabel(float cx, float cy, float rLabel, float midAngleDeg,
                                           WheelItem item, bool hovered, float t)
        {
            if (t < 0.75f) return;

            var rad = midAngleDeg * (float)(Math.PI / 180.0);
            var ux = (float)Math.Sin(rad);
            var uy = (float)Math.Cos(rad);

            const float scale = 0.32f;
            const float boxH = 0.030f;

            var label = item.Label.ToUpperInvariant();
            var w = Draw.MeasureText(label, scale, Draw.FontLabel);

            var ax = cx + Draw.ToX(rLabel * ux);
            var ay = cy - rLabel * uy;

            var side = ux > 0.02f ? 1f : (ux < -0.02f ? -1f : 0f);
            ax += (w * 0.5f + 0.004f) * side;

            // Text places by its TOP edge, so a label above the centre has to be lifted by its
            // whole height and one below it by none -- which is what the uy term does.
            ay -= boxH * (0.5f + 0.5f * uy);

            var ink = hovered ? Palette.Text
                    : item.Enabled ? Palette.Alpha(Palette.Text, 205)
                    : Palette.TextDisabled;

            Draw.Text(label, ax, ay, scale, ink, Draw.FontLabel);

            if (hovered)
            {
                Draw.RectFrom(ax - w * 0.5f - 0.003f, ay + boxH * 0.92f, w + 0.006f, 0.0026f,
                              Palette.Warn);
            }

            if (!item.Enabled)
            {
                // Struck through with a thin rect, which is the only kind of line this HUD owns.
                Draw.RectFrom(ax - w * 0.5f - 0.004f, ay + boxH * 0.46f, w + 0.008f, 0.0024f,
                              Palette.Alpha(Palette.TextDisabled, 235));
            }
        }

        /// <summary>
        /// The hub, which is now the only place on the screen that words are read.
        ///
        /// It carries the breadcrumb, the hovered item's name, its value and its detail -- so
        /// the top-of-screen readout is gone entirely. That readout sat four hundred pixels
        /// from the ring the eye was already on, and wrote the hovered item's name a second
        /// time when the wedge under the cursor was already saying it.
        /// </summary>
        private void DrawHub(float cx, float cy, float rInner, WheelPage page, float t)
        {
            if (!_wedgeMode)
            {
                Draw.RectUniform(cx, cy, rInner * 1.7f, rInner * 1.7f,
                                 Palette.Alpha(Palette.Hub, (int)(Palette.Hub.A * t)));
            }
            else
            {
                // Two discs. The hub at 225 and an unselected wedge at 235 are the same value,
                // so without a rim the ring and the readout run together into one black blob.
                // A hairline boundary is not a third way of separating peers -- it is the edge
                // between two different KINDS of thing.
                //
                // The FILL is rectangles and the RIM is a sprite, and the split is not a
                // preference.
                //
                // ScriptHookV draws its textures in a pass of its own, after everything a
                // script has drawn. So a filled circle sprite lands on top of the hub text --
                // the name, the value and the detail all went behind the disc, which is what
                // "the text needs to come to the front" was. Rectangles are in the script's
                // own layer, so text drawn after them sits over them the way it always did.
                //
                // The rim is a ring with nothing in the middle, so it cannot cover anything,
                // and it is the only part of the hub anybody reads as a curve.
                var rim = Color.FromArgb((int)(120 * t), 255, 255, 255);
                var fill = Palette.Alpha(Palette.Hub, (int)(Palette.Hub.A * t));

                // A rectangle per pixel row. The hub is the one circle in the mod whose edge
                // anybody actually looks at, and at the old row height its bands poked through
                // the rim as a ring of notches.
                Draw.Disc(cx, cy, rInner, fill, 1);

                if (!Draw.File("wheel_hub.png", cx, cy, rInner * 2f, 0f, rim))
                {
                    Draw.Disc(cx, cy, rInner, rim, 1);
                    Draw.Disc(cx, cy, rInner - 0.0028f, fill, 1);
                }
            }

            if (t < 0.75f) return;

            var crumb = _stack.Count > 1 ? "< " + page.Title : page.Title;
            Draw.Text(crumb.ToUpperInvariant(), cx, cy - 0.076f, 0.26f,
                      Palette.Alpha(Palette.TextDim, 225), Draw.FontLabel);
            Draw.Rect(cx, cy - 0.0455f, 0.062f, 0.0022f,
                      Color.FromArgb(70, 255, 255, 255));

            var chord = Chord(rInner, 0.058f);

            var item = HoveredItem;

            if (item == null)
            {
                if (string.IsNullOrEmpty(page.Subtitle)) return;

                Draw.Text(Draw.Fit(page.Subtitle, chord, 0.30f, Draw.FontBody), cx, cy - 0.020f,
                          0.30f, Palette.Alpha(Palette.TextDim, 220), Draw.FontBody);
                return;
            }

            var nameInk = item.Enabled ? Palette.Text : Palette.TextDisabled;

            Draw.Text(Draw.Fit(item.Label.ToUpperInvariant(), Draw.ToX(0.205f), 0.58f, Draw.FontLabel),
                      cx, cy - 0.038f, 0.58f, nameInk, Draw.FontLabel);

            if (!string.IsNullOrEmpty(item.Value))
            {
                Draw.Text(Draw.Fit(item.Value, chord + 0.010f, 0.34f, Draw.FontBody),
                          cx, cy + 0.014f, 0.34f,
                          item.Enabled ? Palette.Cash : Palette.TextDisabled, Draw.FontBody);
            }

            var line = !item.Enabled && !string.IsNullOrEmpty(item.DisabledReason)
                ? item.DisabledReason
                : item.Detail;

            if (!string.IsNullOrEmpty(line))
            {
                // Three lines, not one cut off mid-word. "Opens the game's own weapon w..."
                // is a caption that has given up, and the hub has the room for the rest of it
                // -- the only reason that room went unused is that nothing here could wrap.
                //
                // THREE rather than two because of how fast a chord narrows. The first line
                // holds about seventeen characters and the third about ten, so two lines cap a
                // caption at roughly thirty and most of them are longer than that; wrapping to
                // two just moved where the ellipsis landed. The ellipsis is still there for
                // anything genuinely too long, which is the honest end of the scale.
                //
                // Each line is measured against the chord at ITS OWN depth, taken at the line's
                // lower edge, which is the tight end for anything below the middle.
                var ink = item.Enabled ? Palette.Alpha(Palette.TextDim, 225) : Palette.Warn;

                var wrapped = Draw.Wrap(line, DetailScale, Draw.FontBody,
                                        Chord(rInner, DetailTop + DetailLine),
                                        Chord(rInner, DetailTop + DetailStep + DetailLine),
                                        Chord(rInner, DetailTop + DetailStep * 2f + DetailLine));

                for (var i = 0; i < wrapped.Length; i++)
                {
                    Draw.Text(wrapped[i], cx, cy + DetailTop + i * DetailStep,
                              DetailScale, ink, Draw.FontBody);
                }
            }
        }

        /// <summary>
        /// How wide a line may be at <paramref name="depth"/> from the middle of a circle of
        /// radius <paramref name="r"/>, in X units, with a margin off the curve.
        ///
        /// The widest line that fits inside a circle at a given height is a CHORD, not the
        /// diameter, and it narrows fast -- text set to the diameter overhangs the curve well
        /// before it gets near the top or the bottom of the hub.
        /// </summary>
        private static float Chord(float r, float depth)
        {
            var half = (float)Math.Sqrt(Math.Max(0.0, r * r - depth * depth));
            return Math.Max(0f, Draw.ToX(half * 2f) - 0.014f);
        }

        /// <summary>The caption under the hub: where it starts, its line pitch, and its size.</summary>
        private const float DetailTop = 0.044f;
        private const float DetailStep = 0.021f;
        private const float DetailScale = 0.24f;

        /// <summary>Roughly how tall a DetailScale line draws, for measuring its lower edge.</summary>
        private const float DetailLine = 0.017f;

        /// <summary>
        /// The old right-hand panel, folded back onto the vertical axis and squared up under
        /// the ring.
        ///
        /// Off to one side it made the whole composition lean: the ring sat at screen centre
        /// and the panel hung off the right, so the visual centre of mass was somewhere
        /// between them with nothing in it. Under the ring the wheel is symmetrical again, and
        /// splitting the rows into two columns keeps the block wide and low rather than tall
        /// and lopsided.
        /// </summary>
        private void DrawPlinth(WheelPage page, float rOuter, float t)
        {
            if (page.Panel.Count == 0 || t < 0.9f) return;

            const float plinthW = 0.44f;
            const float rowH = 0.030f;
            const float headH = 0.032f;
            const float pad = 0.010f;

            var top = 0.5f + rOuter + 0.046f;
            var left = 0.5f - plinthW * 0.5f;

            // Split at a group boundary near the middle, so a header keeps its own rows.
            var half = (page.Panel.Count + 1) / 2;
            var cutAt = half;

            for (var i = 0; i < page.Panel.Count; i++)
            {
                var row = page.Panel[i];
                var isHead = string.IsNullOrEmpty(row.Value) && !string.IsNullOrEmpty(row.Label);

                if (isHead && Math.Abs(i - half) <= 2) { cutAt = i; break; }
            }

            var tall = Math.Max(cutAt, page.Panel.Count - cutAt);
            var bodyH = pad * 2f + tall * rowH;

            Draw.RectFrom(left, top, plinthW, headH, Palette.PanelHeader);
            Draw.RectFrom(left, top + headH - 0.0028f, plinthW, 0.0028f, Palette.Accent);
            Draw.RectFrom(left, top + headH, plinthW, bodyH, Palette.Hub);

            if (!string.IsNullOrEmpty(page.PanelTitle))
            {
                Draw.Text(page.PanelTitle.ToUpperInvariant(), 0.5f, top + 0.0055f, 0.28f,
                          Palette.Accent, Draw.FontLabel);
            }

            var colW = (plinthW - pad * 3f) * 0.5f;

            Draw.RectFrom(0.5f - 0.0008f, top + headH + pad * 0.5f, 0.0016f, bodyH - pad,
                          Color.FromArgb(46, 255, 255, 255));

            for (var col = 0; col < 2; col++)
            {
                var first = col == 0 ? 0 : cutAt;
                var last = col == 0 ? cutAt : page.Panel.Count;

                var cl = left + pad + col * (colW + pad);
                var y = top + headH + pad;

                for (var i = first; i < last; i++)
                {
                    var row = page.Panel[i];

                    if (string.IsNullOrEmpty(row.Label) && string.IsNullOrEmpty(row.Value))
                    {
                        y += rowH;
                        continue;
                    }

                    // A row with a label and no value is a group heading.
                    if (string.IsNullOrEmpty(row.Value))
                    {
                        Draw.Text(row.Label.ToUpperInvariant(), cl, y + 0.002f, 0.26f,
                                  Palette.Alpha(Palette.Accent, 215), Draw.FontLabel, centre: false);
                        Draw.RectFrom(cl, y + rowH - 0.0075f, colW, 0.0016f,
                                      Color.FromArgb(60, 255, 255, 255));
                        y += rowH;
                        continue;
                    }

                    var indent = 0f;

                    if (!string.IsNullOrEmpty(row.ArtFile) &&
                        Draw.File(row.ArtFile, cl + Draw.ToX(PanelArt) * 0.5f, y + 0.0130f,
                                  PanelArt, 0f, Palette.TextDim))
                    {
                        indent = Draw.ToX(PanelArt) + 0.005f;
                    }

                    Draw.Text(row.Label, cl + indent, y + 0.0025f, 0.26f, Palette.TextDim,
                              Draw.FontBody, centre: false);

                    var taken = Draw.MeasureText(row.Label, 0.26f, Draw.FontBody) + indent;
                    var room = colW - taken - RowGutter;

                    Draw.TextRight(Draw.Fit(row.Value, room, 0.26f, Draw.FontBody),
                                   cl + colW, y + 0.0025f, 0.26f,
                                   row.Tint ?? Palette.Text, Draw.FontBody);

                    y += rowH;
                }
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

            // One rect. The second one drew the identical colour very slightly smaller on top
            // of the first, which is invisible and costs a draw call out of a per-frame budget
            // this wheel has already been over once.
            Draw.RectUniform(px, py, w * scale, h * scale, fill);
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
