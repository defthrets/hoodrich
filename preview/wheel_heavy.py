"""
HOODRICH radial wheel -- redesign "heavy": committed to the ring, done properly.

Renders a 1920x1080 mock to preview/wheel_heavy.png.

EVERYTHING in here is restricted to what Draw.cs can already do:

    Draw.Rect / Draw.RectUniform   axis-aligned rectangles
    Draw.Wedge                     annulus sector, scanline-rasterised in 2px rows
    Draw.Disc                      filled circle, same rasteriser
    Draw.Text / Draw.TextRight     game fonts, drop shadow, centred / right-aligned
    Draw.MeasureText / Draw.Fit    width measuring and ellipsis trimming
    Draw.File                      a square PNG from data/icons, tinted at draw time

The wedges and the hub disc below are rasterised with the SAME algorithm as Draw.Wedge
(2 device-pixel rows, phase anchored to the wheel centre, analytic annulus + two
half-plane clips) so the edges in this mock are the edges the game will actually draw --
stair-steps included. No gradients, no rounded corners, no blur, no strokes.

WHAT THE DESIGN DOES
  * One thick ring. r 0.124 -> 0.252 of screen height: a 0.128 band, near enough double
    the current 0.115. Big enough to hold a 0.072 icon with air around it, so the ICON
    carries the wedge and there is no caption crammed into a tapering shape.
  * Labels live OUTSIDE the ring, on the wedge's own angle, pushed clear of the rim by
    half their MEASURED width (Draw.MeasureText already exists for this). Nothing is
    squeezed inside a wedge, and an eight-item ring is no more cramped than a two.
  * The hub carries the hovered item: page title as a breadcrumb, then its NAME, its
    VALUE and one line of detail. The top-of-screen readout is gone entirely -- one
    place for text, and it is in the middle of the thing the eye is already on.
  * The right-hand stat panel is gone. Pages with rows get a centred plinth under the
    ring, split into two columns at a group boundary, so the composition stays on the
    vertical axis instead of leaning right.
  * ONE separation device between peers: a 4 degree gap, with the unselected fill taken
    to 235 alpha so the gap is a real hole rather than a shade. No hover reach, no hub
    inset. Hover is colour inversion plus an amber KEEL along the wedge's INNER edge --
    the leading edge, the one pointing at the hub the answer is written in -- plus an
    amber underline under that wedge's outside label. (The hub gets a hairline rim, but
    that is a boundary around the readout, not a third way of dividing the choices.)
  * Disabled is not "darker". A disabled wedge is drawn as an EMPTY FRAME: four thin
    wedges outlining the slot, a hole punched darker than the backdrop, a lock under the
    dimmed icon, and its outside label struck through with a thin rect. It reads as a
    slot with nothing in it, which is what it is, and it keeps its angle so muscle
    memory holds.
  * Two type families, four sizes in a clear ladder. The script/cursive face is dropped.
"""

import math
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFont

# --------------------------------------------------------------------------- paths

ROOT = r"C:\projects\hoodrich"
ICONS = os.path.join(ROOT, "data", "icons")
OUT = os.path.join(ROOT, "preview", "wheel_heavy.png")

W, H = 1920, 1080
ASPECT = W / H

# --------------------------------------------------------------------------- palette
# Straight out of Palette.cs. (a, r, g, b)

BACKDROP        = (140, 0, 0, 0)
SEGMENT         = (200, 10, 12, 14)
SEGMENT_HOVER   = (240, 240, 242, 240)
SEGMENT_DISABLED= (200, 44, 46, 50)
HUB             = (225, 8, 9, 11)
PANEL_HEADER    = (235, 22, 24, 26)
TEXT            = (245, 255, 255, 255)
TEXT_DIM        = (190, 176, 179, 181)
TEXT_ON_HOVER   = (255, 16, 18, 20)
TEXT_DISABLED   = (255, 150, 152, 156)
ACCENT          = (255, 245, 245, 245)
CASH            = (255, 126, 190, 79)
WARN            = (255, 232, 177, 44)
DANGER          = (255, 214, 69, 58)


def alpha(c, a):
    return (a, c[1], c[2], c[3])


def text_on(fill):
    luma = (0.2126 * fill[1] + 0.7152 * fill[2] + 0.0722 * fill[3]) / 255.0
    return TEXT_ON_HOVER if luma > 0.55 else TEXT


# --------------------------------------------------------------------------- geometry
# All radii are fractions of screen HEIGHT, exactly as the C# side stores them.

R_IN = 0.124
R_OUT = 0.252
R_MID = (R_IN + R_OUT) * 0.5
R_LABEL = R_OUT + 0.026

GAP_DEG = 4.0            # the one and only separation device
KEEL = 0.016             # radial thickness of the hover keel, at the inner edge
FRAME_BAND = 0.0055      # radial thickness of a disabled slot's edge bands

ICON_H = 0.072           # big. the icon IS the wedge.
LOCK_H = 0.028

# Segment at 235 rather than 200: at 200 over a 140-black backdrop the 4-degree gap
# between two unselected wedges is a few values of difference and the ring reads as one
# doughnut again. At 235 the wedge is effectively solid and the gap is a real hole.
SEGMENT_A = 235

# Hairline around the hub disc, so the readout is a distinct object from the ring.
HUB_RIM = (120, 255, 255, 255)

ROW_PIXELS = 2           # Draw.cs rasterises in 2 device-pixel rows


def to_x(height_fraction):
    return height_fraction / ASPECT


# --------------------------------------------------------------------------- surface


class Surface:
    """A 1920x1080 frame. Fills go straight into a float buffer; text and icons go into
    an overlay that is composited last, because in Draw.cs order they always sit on top."""

    def __init__(self, base_rgb):
        self.buf = base_rgb.astype(np.float32)
        self.over = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        self.od = ImageDraw.Draw(self.over)

    # ---- fills (Draw.Rect / Draw.Wedge / Draw.Disc) ------------------------

    def _blend(self, x0, x1, y0, y1, c):
        a = c[0] / 255.0
        if a <= 0:
            return
        x0 = max(0, int(round(x0)))
        x1 = min(W, int(round(x1)))
        y0 = max(0, int(round(y0)))
        y1 = min(H, int(round(y1)))
        if x1 <= x0 or y1 <= y0:
            return
        rgb = np.array(c[1:], dtype=np.float32)
        sl = self.buf[y0:y1, x0:x1]
        sl *= (1.0 - a)
        sl += rgb * a

    def rect(self, cx, cy, w, h, c):
        """Draw.Rect: normalised centre, normalised width (of screen) and height."""
        self._blend((cx - w * 0.5) * W, (cx + w * 0.5) * W,
                    (cy - h * 0.5) * H, (cy + h * 0.5) * H, c)

    def rect_uniform(self, cx, cy, w_h, h, c):
        """Draw.RectUniform: both sizes given as fractions of screen HEIGHT."""
        self.rect(cx, cy, to_x(w_h), h, c)

    def rect_from(self, left, top, w, h, c):
        self._blend(left * W, (left + w) * W, top * H, (top + h) * H, c)

    def wedge(self, cx, cy, r_in, r_out, a_from, a_to, c):
        """Faithful port of Draw.Wedge -- 2px scanlines, analytic annulus, two half-plane
        clips, phase anchored to the wheel centre so neighbouring wedges share a grid."""
        if r_out <= r_in or c[0] <= 0:
            return
        span = a_to - a_from
        if span <= 0:
            return
        if span > 170.0:
            mid = a_from + span * 0.5
            self.wedge(cx, cy, r_in, r_out, a_from, mid, c)
            self.wedge(cx, cy, r_in, r_out, mid, a_to, c)
            return

        a0 = math.radians(a_from)
        a1 = math.radians(a_to)
        d0x, d0y = math.sin(a0), math.cos(a0)
        d1x, d1y = math.sin(a1), math.cos(a1)

        r_out2, r_in2 = r_out * r_out, r_in * r_in

        def crosses(bearing):
            f = a_from % 360.0
            t = a_to % 360.0
            b = bearing % 360.0
            if f <= t:
                return f <= b <= t
            return b >= f or b <= t

        def reach_top(a):
            co = math.cos(a)
            return r_out * co if co > 0 else r_in * co

        def reach_bottom(a):
            co = math.cos(a)
            return r_in * co if co > 0 else r_out * co

        top_dy = r_out if crosses(0.0) else max(reach_top(a0), reach_top(a1))
        bot_dy = -r_out if crosses(180.0) else min(reach_bottom(a0), reach_bottom(a1))

        px_top = int(math.floor((cy - top_dy) * H))
        px_bottom = int(math.ceil((cy - bot_dy) * H))
        px_centre = int(round(cy * H))
        px_top -= ((px_top - px_centre) % ROW_PIXELS + ROW_PIXELS) % ROW_PIXELS

        for py in range(px_top, px_bottom, ROW_PIXELS):
            row_y = (py + ROW_PIXELS * 0.5) / H
            dy = cy - row_y
            dy2 = dy * dy
            if dy2 > r_out2:
                continue

            hi = math.sqrt(r_out2 - dy2)
            lo = math.sqrt(r_in2 - dy2) if dy2 < r_in2 else 0.0

            lo_b, hi_b = -hi, hi

            # inside == clockwise of d0 and anticlockwise of d1
            if abs(d0y) < 1e-9:
                if dy * d0x > 0:
                    continue
            elif d0y > 0:
                lo_b = max(lo_b, dy * d0x / d0y)
            else:
                hi_b = min(hi_b, dy * d0x / d0y)

            if abs(d1y) < 1e-9:
                if dy * d1x < 0:
                    continue
            elif d1y > 0:
                hi_b = min(hi_b, dy * d1x / d1y)
            else:
                lo_b = max(lo_b, dy * d1x / d1y)

            if hi_b <= lo_b:
                continue

            x_c = cx * W
            y0, y1 = py, py + ROW_PIXELS
            if lo <= 0:
                self._blend(x_c + lo_b * H, x_c + hi_b * H, y0, y1, c)
            else:
                self._blend(x_c + lo_b * H, x_c + min(hi_b, -lo) * H, y0, y1, c)
                self._blend(x_c + max(lo_b, lo) * H, x_c + hi_b * H, y0, y1, c)

    def disc(self, cx, cy, r, c):
        px_top = int(math.floor((cy - r) * H))
        px_bottom = int(math.ceil((cy + r) * H))
        px_centre = int(round(cy * H))
        px_top -= ((px_top - px_centre) % ROW_PIXELS + ROW_PIXELS) % ROW_PIXELS
        for py in range(px_top, px_bottom, ROW_PIXELS):
            row_y = (py + ROW_PIXELS * 0.5) / H
            dy = cy - row_y
            if abs(dy) > r:
                continue
            half = math.sqrt(r * r - dy * dy)
            self._blend(cx * W - half * H, cx * W + half * H, py, py + ROW_PIXELS, c)

    # ---- text (Draw.Text / Draw.TextRight / MeasureText / Fit) -------------

    def text(self, s, x, y, scale, c, font="label", centre=True, right=False,
             shadow=True, upper=False):
        if not s:
            return
        if upper:
            s = s.upper()
        f = pil_font(font, scale)
        anchor = "ra" if right else ("ma" if centre else "la")
        px, py = x * W, y * H
        col = (c[1], c[2], c[3], c[0])
        if shadow:
            self.od.text((px + 2, py + 2), s, font=f, fill=(0, 0, 0, 175), anchor=anchor)
        self.od.text((px, py), s, font=f, fill=col, anchor=anchor)

    def over_rect(self, cx, cy, w, h, c):
        """A Draw.Rect that has to land on TOP of text -- the strike-through."""
        self.od.rectangle([(cx - w * 0.5) * W, (cy - h * 0.5) * H,
                           (cx + w * 0.5) * W, (cy + h * 0.5) * H],
                          fill=(c[1], c[2], c[3], c[0]))

    def icon(self, name, cx, cy, height_fraction, c):
        """Draw.File: a square PNG from data/icons, tinted at draw time."""
        img = load_icon(name)
        size = max(2, int(round(height_fraction * H)))
        img = img.resize((size, size), Image.LANCZOS)
        a = img.split()[3].point(lambda v: int(v * c[0] / 255.0))
        solid = Image.new("RGBA", img.size, (c[1], c[2], c[3], 255))
        solid.putalpha(a)
        self.over.alpha_composite(solid, (int(round(cx * W - size / 2)),
                                          int(round(cy * H - size / 2))))

    def finish(self):
        base = Image.fromarray(np.clip(self.buf, 0, 255).astype(np.uint8), "RGB").convert("RGBA")
        return Image.alpha_composite(base, self.over).convert("RGB")


# --------------------------------------------------------------------------- fonts

_FONT_CACHE = {}
_ICON_CACHE = {}

# Stand-ins for the game faces:
#   label -> Chalet Comprime Cologne (Draw.FontLabel), condensed grotesque
#   body  -> Chalet London           (Draw.FontBody),  plain grotesque
FONT_PX = {"label": 78.0, "body": 64.0}


def pil_font(kind, scale):
    px = max(8, int(round(scale * FONT_PX[kind] * (H / 1080.0))))
    key = (kind, px)
    if key in _FONT_CACHE:
        return _FONT_CACHE[key]
    if kind == "label":
        f = ImageFont.truetype(r"C:\Windows\Fonts\bahnschrift.ttf", px)
        f.set_variation_by_name("SemiBold Condensed")
    else:
        f = ImageFont.truetype(r"C:\Windows\Fonts\segoeui.ttf", px)
    _FONT_CACHE[key] = f
    return f


_MEASURE = ImageDraw.Draw(Image.new("RGBA", (8, 8)))


def measure(s, scale, kind="label", upper=False):
    """Draw.MeasureText: rendered width as a normalised screen fraction."""
    if not s:
        return 0.0
    if upper:
        s = s.upper()
    return _MEASURE.textlength(s, font=pil_font(kind, scale)) / W


def fit(s, max_w, scale, kind="body", upper=False):
    """Draw.Fit: trim with an ellipsis until it fits."""
    if not s or max_w <= 0:
        return s
    if measure(s, scale, kind, upper) <= max_w:
        return s
    lo, hi = 0, len(s)
    while lo < hi:
        mid = (lo + hi + 1) // 2
        if measure(s[:mid] + "...", scale, kind, upper) <= max_w:
            lo = mid
        else:
            hi = mid - 1
    if lo <= 0:
        return ""
    return s[:lo].rstrip(" ,") + "..."


def load_icon(name):
    if name not in _ICON_CACHE:
        _ICON_CACHE[name] = Image.open(os.path.join(ICONS, name)).convert("RGBA")
    return _ICON_CACHE[name]


# --------------------------------------------------------------------------- scene
# A HUD that only works on flat black is not a HUD. This is a dusk street with a bright
# wall, a lit sky, deep shadow and grain -- so the ring has to hold against all of it.


def street_scene(seed=7, horizon=0.545, sun_x=0.30):
    """A dusk street looking down a road. Deliberately full-range: a blown-out sunlit
    stucco wall, mid-grey tarmac, near-black shadow, warm sodium pools and film grain.
    If the wheel only reads over one of those it is not finished."""
    rng = np.random.default_rng(seed)
    y = np.linspace(0, 1, H, dtype=np.float32)[:, None]
    x = np.linspace(0, 1, W, dtype=np.float32)[None, :]

    def paint(mask, colour):
        img[:] = np.where(mask[..., None],
                          np.array(colour, dtype=np.float32)[None, None, :], img)

    # ---- sky -----------------------------------------------------------
    t = np.clip(y / horizon, 0, 1) ** 1.3
    sky = (np.array([44, 60, 88], np.float32)[None, None, :] * (1 - t[..., None]) +
           np.array([214, 168, 112], np.float32)[None, None, :] * t[..., None])
    img = np.empty((H, W, 3), np.float32)
    img[:] = sky

    d = np.sqrt(((x - sun_x) * ASPECT) ** 2 + (y - horizon + 0.02) ** 2)
    img += (np.clip(1 - d / 0.55, 0, 1) ** 2)[..., None] * np.array([110, 80, 34], np.float32)

    # ---- ground: tarmac running away to the horizon ---------------------
    ground = y >= horizon
    fall = np.clip((y - horizon) / (1 - horizon), 0, 1)
    tar = (np.array([104, 104, 110], np.float32)[None, None, :] *
           (0.60 + 0.80 * fall[..., None]))
    img = np.where(ground[..., None], tar, img)

    # footpath either side, converging on the vanishing point at (0.5, horizon)
    depth = np.maximum(fall, 1e-4)
    half_road = 0.03 + 0.72 * depth
    path = ground & (np.abs(x - 0.5) > half_road) & (np.abs(x - 0.5) < half_road + 0.13 * depth)
    paint(path, (150, 145, 132))
    kerb = ground & (np.abs(np.abs(x - 0.5) - half_road) < 0.006 * depth + 0.0012)
    paint(kerb, (186, 179, 162))

    # ---- buildings ------------------------------------------------------
    def block(x0, x1, top_y, shade):
        paint((x >= x0) & (x < x1) & (y >= top_y) & (y < horizon + 0.012), shade)

    block(0.000, 0.100, 0.185, (104, 106, 116))
    block(0.100, 0.185, 0.290, (142, 134, 124))
    block(0.185, 0.250, 0.235, (78, 80, 88))
    block(0.250, 0.318, 0.330, (124, 116, 106))
    block(0.690, 0.790, 0.215, (96, 98, 108))
    block(0.790, 0.900, 0.305, (146, 136, 122))
    block(0.900, 1.000, 0.165, (70, 72, 80))

    # a blown-out sunlit stucco wall sitting square behind the ring's right side
    paint((x >= 0.560) & (x < 0.700) & (y >= 0.255) & (y < horizon + 0.012),
          (232, 212, 176))
    paint((x >= 0.560) & (x < 0.700) & (y >= 0.255) & (y < 0.268), (250, 240, 216))

    # and deep shadow behind its left side, so the ring straddles both
    paint((x >= 0.318) & (x < 0.430) & (y >= 0.200) & (y < horizon + 0.012), (26, 27, 31))

    for _ in range(260):
        wx = rng.uniform(0.0, 1.0)
        wy = rng.uniform(0.19, horizon - 0.03)
        if 0.560 <= wx < 0.700:
            continue
        ww, wh = rng.uniform(0.005, 0.011), rng.uniform(0.014, 0.026)
        lit = rng.random() < 0.30
        paint((x >= wx) & (x < wx + ww) & (y >= wy) & (y < wy + wh),
              (238, 200, 128) if lit else (18, 19, 23))

    # ---- lane dashes, spaced in 1/z so they converge properly ------------
    for k in range(1, 16):
        z0, z1 = k * 1.0, k * 1.0 + 0.45
        ya = horizon + (1 - horizon) * (1.0 / z0)
        yb = horizon + (1 - horizon) * (1.0 / z1)
        if ya > 1.05:
            continue
        wdash = 0.0022 + 0.010 * (1.0 / z0)
        paint((np.abs(x - 0.5) < wdash) & (y <= ya) & (y > yb), (198, 192, 170))

    # ---- lights ---------------------------------------------------------
    for lx, ly, rad, warm in ((0.155, 0.885, 0.34, 1.0), (0.845, 0.760, 0.24, 0.7),
                              (0.500, 0.560, 0.18, 0.5)):
        dd = np.sqrt(((x - lx) * ASPECT) ** 2 + (y - ly) ** 2)
        img += (np.clip(1 - dd / rad, 0, 1) ** 2)[..., None] * \
            (np.array([150, 132, 96], np.float32) * warm)

    # ---- grain, chroma noise, vignette ----------------------------------
    img += rng.normal(0, 5.5, (H, W, 1)).astype(np.float32)
    img += rng.normal(0, 2.0, (H, W, 3)).astype(np.float32)
    vig = 1.0 - 0.30 * (((x - 0.5) * 1.85) ** 2 + ((y - 0.5) * 1.5) ** 2)
    img *= np.clip(vig, 0.52, 1.0)[..., None]

    return np.clip(img, 0, 255)


# --------------------------------------------------------------------------- model


class Item:
    def __init__(self, label, icon, detail="", value="", enabled=True, reason="", tint=None):
        self.label = label
        self.icon = icon
        self.detail = detail
        self.value = value
        self.enabled = enabled
        self.reason = reason
        self.tint = tint


class Page:
    def __init__(self, title, subtitle="", nested=False, panel_title="", panel=None):
        self.title = title
        self.subtitle = subtitle
        self.nested = nested
        self.panel_title = panel_title
        self.panel = panel or []     # (label, value, tint, art, is_header)
        self.items = []


# --------------------------------------------------------------------------- renderer

CX, CY = 0.5, 0.5


def draw_wheel(s, page, hovered):
    s.rect(0.5, 0.5, 1.0, 1.0, BACKDROP)

    n = len(page.items)
    step = 360.0 / n
    gap = GAP_DEG if n > 1 else 0.0

    # ---- wedges --------------------------------------------------------
    for i, item in enumerate(page.items):
        mid = i * step
        a0 = mid - step * 0.5 + gap * 0.5
        a1 = mid + step * 0.5 - gap * 0.5
        hov = (i == hovered)

        if not item.enabled:
            draw_empty_slot(s, a0, a1)
            continue

        fill = (item.tint or SEGMENT_HOVER) if hov else alpha(SEGMENT, SEGMENT_A)
        s.wedge(CX, CY, R_IN, R_OUT, a0, a1, fill)

        if hov:
            # THE KEEL. The hovered wedge's leading edge is the one pointing at the hub,
            # because the hub is where the answer is written. A solid amber bar there
            # reads on a near-white wedge AND against the near-black hub, and it draws
            # the eye inward along the exact path the reading takes.
            s.wedge(CX, CY, R_IN, R_IN + KEEL, a0, a1, WARN)

    # ---- icons ---------------------------------------------------------
    for i, item in enumerate(page.items):
        mid = i * step
        rad = math.radians(mid)
        px = CX + to_x(R_MID * math.sin(rad))
        py = CY - R_MID * math.cos(rad)
        hov = (i == hovered)

        if not item.enabled:
            s.icon(item.icon, px, py - 0.014, ICON_H * 0.86, alpha(TEXT_DISABLED, 115))
            s.icon("locked.png", px, py + 0.042, LOCK_H, alpha(TEXT_DISABLED, 235))
        else:
            fill = (item.tint or SEGMENT_HOVER) if hov else alpha(SEGMENT, SEGMENT_A)
            s.icon(item.icon, px, py, ICON_H, text_on(fill))

    # ---- labels, outside the ring, on the wedge's own angle -------------
    for i, item in enumerate(page.items):
        mid = i * step
        rad = math.radians(mid)
        ux, uy = math.sin(rad), math.cos(rad)
        hov = (i == hovered)

        scale = 0.32
        w = measure(item.label, scale, "label", upper=True)
        # text box height, as the C# side would hard-code it for this scale
        th = 0.030

        ax = CX + to_x(R_LABEL * ux)
        ay = CY - R_LABEL * uy

        # push clear of the rim: half the measured width outward, and the same in y
        ax += to_x(0.0) + (w * 0.5 + 0.004) * (1 if ux > 0.02 else (-1 if ux < -0.02 else 0))
        ay -= th * (0.5 + 0.5 * uy)

        col = TEXT if hov else (alpha(TEXT, 205) if item.enabled else TEXT_DISABLED)
        s.text(item.label, ax, ay, scale, col, "label", upper=True)

        if hov:
            s.rect_from(ax - w * 0.5 - 0.003, ay + th * 0.92, w + 0.006, 0.0026, WARN)
        if not item.enabled:
            # struck through -- a thin rect, which is the only line Draw.cs owns
            s.over_rect(ax, ay + th * 0.46, w + 0.008, 0.0024, alpha(TEXT_DISABLED, 235))


def draw_empty_slot(s, a0, a1):
    """A disabled item is a slot with nothing in it, not a darker version of a filled one."""
    interior = (155, 0, 0, 0)
    edge = (195, 84, 88, 96)

    s.wedge(CX, CY, R_IN, R_OUT, a0, a1, interior)

    # four thin wedges = an outlined slot. two bands, two radial slivers.
    s.wedge(CX, CY, R_OUT - FRAME_BAND, R_OUT, a0, a1, edge)
    s.wedge(CX, CY, R_IN, R_IN + FRAME_BAND, a0, a1, edge)
    d_ang = math.degrees(FRAME_BAND / R_MID)
    s.wedge(CX, CY, R_IN, R_OUT, a0, a0 + d_ang, edge)
    s.wedge(CX, CY, R_IN, R_OUT, a1 - d_ang, a1, edge)


def draw_hub(s, page, hovered):
    # Two discs. Without the outer one the hub -- near-black at 225 -- is the same value
    # as an unselected wedge at 235, and the ring and the readout run together into one
    # black blob. A hairline rim is a boundary, not a third way of separating peers.
    s.disc(CX, CY, R_IN, HUB_RIM)
    s.disc(CX, CY, R_IN - 0.0028, HUB)

    # breadcrumb, always. this replaces the old hub title AND the top-of-screen readout.
    # "<" rather than a triangle glyph: Chalet Comprime has no arrow characters and a
    # missing glyph is a hollow box, which is how the old hub was already drawing it.
    crumb = ("< " + page.title) if page.nested else page.title
    s.text(crumb, CX, CY - 0.076, 0.26, alpha(TEXT_DIM, 225), "label", upper=True)
    s.rect(CX, CY - 0.0455, 0.062, 0.0022, (70, 255, 255, 255))

    inner_w = to_x(2.0 * math.sqrt(max(0.0, R_IN ** 2 - 0.058 ** 2))) - 0.012

    if hovered is None:
        s.text(fit(page.subtitle, inner_w, 0.30, "body"), CX, CY - 0.020, 0.30,
               alpha(TEXT_DIM, 220), "body")
        return

    item = page.items[hovered]
    name_col = TEXT if item.enabled else TEXT_DISABLED

    s.text(fit(item.label, to_x(0.205), 0.58, "label", upper=True),
           CX, CY - 0.038, 0.58, name_col, "label", upper=True)

    if item.value:
        s.text(fit(item.value, inner_w + 0.010, 0.34, "body"), CX, CY + 0.014, 0.34,
               CASH if item.enabled else TEXT_DISABLED, "body")

    line = item.reason if (not item.enabled and item.reason) else item.detail
    if line:
        s.text(fit(line, inner_w + 0.014, 0.24, "body"), CX, CY + 0.048, 0.24,
               alpha(TEXT_DIM, 225) if item.enabled else WARN, "body")


PLINTH_TOP = 0.5 + R_OUT + 0.046
PLINTH_W = 0.44           # normalised x
ROW_H = 0.030
HEAD_H = 0.032
PAD = 0.010


def draw_plinth(s, page):
    """The old right-hand panel, folded back onto the vertical axis and squared up under
    the ring. Rows split into two columns at a group boundary, so it stays wide and low
    instead of tall and lopsided."""
    if not page.panel:
        return

    rows = page.panel
    # split at the last header at or before the midpoint, so groups stay together
    half = (len(rows) + 1) // 2
    cut = half
    for i in range(len(rows)):
        if rows[i][4] and abs(i - half) <= 2:
            cut = i
            break
    left_rows, right_rows = rows[:cut], rows[cut:]
    tall = max(len(left_rows), len(right_rows))

    body_h = PAD * 2 + tall * ROW_H
    left = 0.5 - PLINTH_W * 0.5

    s.rect_from(left, PLINTH_TOP, PLINTH_W, HEAD_H, PANEL_HEADER)
    s.rect_from(left, PLINTH_TOP + HEAD_H - 0.0028, PLINTH_W, 0.0028, ACCENT)
    s.rect_from(left, PLINTH_TOP + HEAD_H, PLINTH_W, body_h, HUB)

    s.text(page.panel_title, CX, PLINTH_TOP + 0.0055, 0.28, ACCENT, "label", upper=True)

    col_w = (PLINTH_W - PAD * 3) * 0.5
    s.rect_from(0.5 - 0.0008, PLINTH_TOP + HEAD_H + PAD * 0.5,
                0.0016, body_h - PAD, (46, 255, 255, 255))

    for ci, col in enumerate((left_rows, right_rows)):
        cl = left + PAD + ci * (col_w + PAD)
        y = PLINTH_TOP + HEAD_H + PAD
        for label, value, tint, art, is_header in col:
            if is_header:
                s.text(label, cl, y + 0.002, 0.26, alpha(ACCENT, 215), "label",
                       centre=False, upper=True)
                s.rect_from(cl, y + ROW_H - 0.0075, col_w, 0.0016, (60, 255, 255, 255))
            else:
                indent = 0.0
                if art:
                    s.icon(art, cl + to_x(0.017) * 0.5, y + 0.0130, 0.017, TEXT_DIM)
                    indent = to_x(0.017) + 0.005
                s.text(label, cl + indent, y + 0.0025, 0.26, TEXT_DIM, "body", centre=False)
                taken = measure(label, 0.26, "body") + indent
                room = col_w - taken - 0.012
                s.text(fit(value, room, 0.26, "body"), cl + col_w, y + 0.0025, 0.26,
                       tint or TEXT, "body", right=True)
            y += ROW_H


def render_frame(page, hovered, scene):
    s = Surface(scene)
    draw_wheel(s, page, hovered)
    draw_hub(s, page, hovered)
    draw_plinth(s, page)
    return s.finish()


# --------------------------------------------------------------------------- content
# Real pages out of WheelPages.cs.


def root_page():
    p = Page("Hoodrich", "Unaffiliated")
    p.items = [
        Item("Weapons", "guns.png", "Opens the game's own weapon wheel", "Pistol"),
        Item("Dealing", "bong.png", "Re-up, bag up, go to work", "49g still to prep"),
        Item("Gangs", "mask.png", "Nobody has put you on yet", "SOLO"),
        Item("Inventory", "stash.png", "Everything you are carrying", "49g on you"),
        Item("Socials", "tattoo.png", "", "", enabled=False, reason="Not right now"),
    ]
    return p


def dealing_page():
    p = Page("Dealing", "49g still to prep", nested=True,
             panel_title="What you're holding")
    p.panel = [
        ("ON YOU", "", None, "stash.png", True),
        ("Weed", "40g to prep", CASH, None, False),
        ("Coke", "9g to prep", CASH, None, False),
        ("AT THE HOUSE", "", None, "garage.png", True),
        ("Weed", "120g to prep", TEXT, None, False),
        ("Ecstasy", "40 pills  \u00b7  60 to press", TEXT, None, False),
    ]
    p.items = [
        Item("Re-up", "money.png", "Text a contact for bulk weight", "$4,820"),
        Item("Post up", "cash.png", "Stand on a corner and let it come to you", "",
             enabled=False, reason="All you have is weight -- prep it first"),
        Item("The numbers", "health.png", "Prices, heat, and what this block does to both"),
    ]
    return p


def sell_page():
    """Eight items -- the worst case the ring has to survive. Real Post-up content."""
    p = Page("Post up", "Pick what you are moving", nested=True)
    names = [("Weed", "weed.png", "40g  ·  $3,200"), ("Coke", "coke.png", "9g  ·  $2,700"),
             ("Crack", "crack.png", "22 rocks"), ("Meth", "meth.png", "18g  ·  $1,440"),
             ("Heroin", "heroin.png", "6g  ·  $1,080"), ("Pills", "pills.png", "40 pills"),
             ("Acid", "acid.png", "12 tabs"), ("Lean", "lean.png", "3 cups")]
    for i, (nm, ic, val) in enumerate(names):
        p.items.append(Item(nm, ic, "Sell it by the gram out here", val,
                            enabled=(i != 6), reason="Nobody round here wants it"))
    return p


# --------------------------------------------------------------------------- sheet


def main():
    scene = street_scene(7)
    scene_b = street_scene(19, horizon=0.500, sun_x=0.74)

    root = render_frame(root_page(), 1, scene)
    deal = render_frame(dealing_page(), 0, scene_b)
    dense = render_frame(sell_page(), 3, scene)

    sheet = Image.new("RGB", (W, H), (14, 15, 17))
    d = ImageDraw.Draw(sheet)
    cap = ImageFont.truetype(r"C:\Windows\Fonts\bahnschrift.ttf", 17)
    cap.set_variation_by_name("SemiBold Condensed")

    def caption(t, x, y, col=(150, 154, 158)):
        d.text((x, y), t.upper(), font=cap, fill=col)

    fw, fh = 941, 529
    sheet.paste(root.resize((fw, fh), Image.LANCZOS), (14, 30))
    sheet.paste(deal.resize((fw, fh), Image.LANCZOS), (965, 30))
    caption("root page \u2014 5 items \u2014 DEALING hovered, SOCIALS disabled", 14, 8)
    caption("dealing \u2014 nested, panel folded onto the axis \u2014 POST UP disabled",
            965, 8)

    d.rectangle([14, 578, 1906, 579], fill=(40, 42, 46))

    # 1:1 crop, so the actual pixels are visible at the size they ship at
    crop = deal.crop((460, 318, 1460, 784))
    sheet.paste(crop, (14, 602))
    caption("1:1 \u2014 keel, hub, empty slot", 14, 583)

    mini = dense.resize((864, 486), Image.LANCZOS)
    sheet.paste(mini, (1036, 592))
    caption("8 items \u2014 the ring at full load", 1036, 583)

    d.rectangle([14, 1075, 1906, 1076], fill=(40, 42, 46))

    sheet.save(OUT)
    print("wrote", OUT, os.path.getsize(OUT), "bytes", sheet.size)


if __name__ == "__main__":
    main()
