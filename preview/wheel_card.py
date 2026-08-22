"""
HOODRICH wheel redesign -- direction "card": RING PLUS ONE CARD.

Renders a 1920x1080 design sheet to preview/wheel_card.png containing:

  * two full 16:9 game frames (root page, and the nested Dealing page under load)
  * native-resolution detail crops of the card and of the ring
  * a legend naming every primitive used

EVERY mark on the HUD frames is drawn with an emulation of a Draw.cs primitive:
    blend_rect  -> Draw.Rect / Draw.RectUniform     (axis-aligned alpha-blended rect)
    wedge       -> Draw.Wedge   (scanline rows of RowPixels=2, exactly as Draw.cs does it)
    disc        -> Draw.Disc    (scanline rows of RowPixels*2)
    icon        -> Draw.File    (square white-mask PNG from data/icons, tinted)
    text        -> Draw.Text / Draw.TextRight (game fonts, drop shadow)
Nothing else. No gradients, no rounded corners, no blur, no strokes, no rotation.
The only place PIL is allowed to do something Draw.cs cannot is the fake gameplay
background and the design-sheet furniture around the frames.
"""

import math
import os
import random

from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(ROOT)
ICONS = os.path.join(REPO, "data", "icons")
OUT = os.path.join(ROOT, "wheel_card.png")

W, H = 1920, 1080
ASPECT = W / float(H)
ROW_PIXELS = 2                      # Draw.cs: private const int RowPixels = 2


# ---------------------------------------------------------------- palette ----
# Straight out of Palette.cs. (a, r, g, b) -> we carry them as (r,g,b,a).
BACKDROP        = (0, 0, 0, 140)
SEGMENT         = (10, 12, 14, 200)
SEGMENT_HOVER   = (240, 242, 240, 240)
SEGMENT_DISABLED= (10, 12, 14, 130)         # Palette.Alpha(Segment, 130)
HUB             = (8, 9, 11, 225)
TEXT            = (255, 255, 255, 245)
TEXT_DIM        = (176, 179, 181, 190)
TEXT_ON_HOVER   = (16, 18, 20, 255)
TEXT_DISABLED   = (150, 152, 156, 150)
ACCENT          = (245, 245, 245, 255)
CASH            = (126, 190, 79, 255)
WARN            = (232, 177, 44, 255)
DANGER          = (214, 69, 58, 255)


def alpha(c, a):
    return (c[0], c[1], c[2], int(a))


def text_on(fill):
    luma = (0.2126 * fill[0] + 0.7152 * fill[1] + 0.0722 * fill[2]) / 255.0
    return TEXT_ON_HOVER if luma > 0.55 else TEXT


# ------------------------------------------------------------------ fonts ----
FONT_DIR = r"C:\Windows\Fonts"


def _variation(path, wanted):
    """Try to open a variable font at a named instance; None if not possible."""
    try:
        f = ImageFont.truetype(path, 40)
        names = [n.decode() if isinstance(n, bytes) else n
                 for n in f.get_variation_names()]
    except Exception:
        return None
    for w in wanted:
        for n in names:
            if n.strip().lower() == w.lower():
                return (path, n)
    return None


# FontLabel  = Chalet Comprime Cologne Sixty -> a condensed grotesque
# FontBody   = Chalet London Nineteen Sixty  -> a neutral grotesque
_cond = _variation(os.path.join(FONT_DIR, "bahnschrift.ttf"),
                   ["SemiBold SemiCondensed", "SemiBold Condensed",
                    "Regular SemiCondensed", "SemiCondensed", "Condensed"])
COND_PATH, COND_INSTANCE = _cond if _cond else (os.path.join(FONT_DIR, "impact.ttf"), None)
BODY_PATH = os.path.join(FONT_DIR, "segoeui.ttf")
BODY_SB_PATH = os.path.join(FONT_DIR, "segoeuisl.ttf")

_font_cache = {}


def font(kind, px):
    px = max(6, int(round(px)))
    key = (kind, px)
    if key in _font_cache:
        return _font_cache[key]
    if kind == "cond":
        f = ImageFont.truetype(COND_PATH, px)
        if COND_INSTANCE:
            try:
                f.set_variation_by_name(COND_INSTANCE)
            except Exception:
                pass
    else:
        f = ImageFont.truetype(BODY_PATH, px)
    _font_cache[key] = f
    return f


# GTA text scale -> pixels at 1080p. Calibrated so the vanilla-ish 0.34 label
# lands at ~22px, which is what the current wheel labels measure on screen.
K_COND = 64.0
K_BODY = 60.0


def px_for(scale, kind):
    return scale * (K_COND if kind == "cond" else K_BODY)


# ---------------------------------------------- Draw.cs primitive emulation --
class Frame:
    """One 1920x1080 game frame. All coords follow Draw.cs conventions:
       x,y are normalised 0..1 and always the CENTRE of the shape;
       radii and heights are fractions of screen HEIGHT (use to_x to convert)."""

    def __init__(self, base):
        self.im = base.convert("RGB")
        self.px = self.im.load()

    # --- geometry helpers -------------------------------------------------
    @staticmethod
    def to_x(height_fraction):
        return height_fraction / ASPECT

    # --- Draw.Rect --------------------------------------------------------
    def rect(self, x, y, w, h, c):
        """Alpha-blended axis-aligned rectangle. w is normalised-x, h normalised-y."""
        if c[3] <= 0:
            return
        x0 = (x - w * 0.5) * W
        x1 = (x + w * 0.5) * W
        y0 = (y - h * 0.5) * H
        y1 = (y + h * 0.5) * H
        self._blend_box(x0, y0, x1, y1, c)

    def rect_from(self, left, top, w, h, c):
        self.rect(left + w * 0.5, top + h * 0.5, w, h, c)

    def rect_uniform(self, x, y, w, h, c):
        """Draw.RectUniform: w given in height units, corrected for aspect."""
        self.rect(x, y, self.to_x(w), h, c)

    def _blend_box(self, x0, y0, x1, y1, c):
        ix0 = int(math.floor(x0 + 0.5))
        ix1 = int(math.floor(x1 + 0.5))
        iy0 = int(math.floor(y0 + 0.5))
        iy1 = int(math.floor(y1 + 0.5))
        if ix1 <= ix0:
            ix1 = ix0 + 1
        if iy1 <= iy0:
            iy1 = iy0 + 1
        ix0 = max(0, ix0); iy0 = max(0, iy0)
        ix1 = min(W, ix1); iy1 = min(H, iy1)
        if ix1 <= ix0 or iy1 <= iy0:
            return
        a = c[3] / 255.0
        if a >= 0.999:
            self.im.paste((c[0], c[1], c[2]), (ix0, iy0, ix1, iy1))
            return
        patch = self.im.crop((ix0, iy0, ix1, iy1))
        solid = Image.new("RGB", patch.size, (c[0], c[1], c[2]))
        self.im.paste(Image.blend(patch, solid, a), (ix0, iy0))

    # --- Draw.Wedge -------------------------------------------------------
    def wedge(self, cx, cy, r_inner, r_outer, ang_from, ang_to, c):
        if r_outer <= r_inner or c[3] <= 0:
            return
        span = ang_to - ang_from
        if span <= 0:
            return
        if span > 170.0:
            mid = ang_from + span * 0.5
            self.wedge(cx, cy, r_inner, r_outer, ang_from, mid, c)
            self.wedge(cx, cy, r_inner, r_outer, mid, ang_to, c)
            return

        d2r = math.pi / 180.0
        a0, a1 = ang_from * d2r, ang_to * d2r
        d0x, d0y = math.sin(a0), math.cos(a0)
        d1x, d1y = math.sin(a1), math.cos(a1)
        ro2, ri2 = r_outer * r_outer, r_inner * r_inner

        px_top = int(math.floor((cy - r_outer) * H))
        px_bottom = int(math.ceil((cy + r_outer) * H))
        px_centre = int(round(cy * H))
        px_top -= ((px_top - px_centre) % ROW_PIXELS + ROW_PIXELS) % ROW_PIXELS

        row_h = ROW_PIXELS / float(H)

        py = px_top
        while py < px_bottom:
            row_y = (py + ROW_PIXELS * 0.5) / float(H)
            dy = cy - row_y                       # up-positive
            dy2 = dy * dy
            if dy2 > ro2:
                py += ROW_PIXELS
                continue

            outer_half = math.sqrt(ro2 - dy2)
            spans = []
            if dy2 < ri2:
                inner_half = math.sqrt(ri2 - dy2)
                spans.append((-outer_half, -inner_half))
                spans.append((inner_half, outer_half))
            else:
                spans.append((-outer_half, outer_half))

            for lo, hi in spans:
                # clip by the two boundary half-planes.
                # angle runs clockwise from up, so for a point p = (x, dy):
                #   cross(d0, p) = d0x*dy - d0y*x  must be <= 0
                #   cross(d1, p) = d1x*dy - d1y*x  must be >= 0
                lo, hi = self._clip_half(lo, hi, dy, d0x, d0y, True)
                if lo is None:
                    continue
                lo, hi = self._clip_half(lo, hi, dy, d1x, d1y, False)
                if lo is None:
                    continue
                if hi - lo <= 0:
                    continue
                mid = (lo + hi) * 0.5
                self.rect(cx + self.to_x(mid), row_y,
                          self.to_x(hi - lo), row_h * 1.02, c)
            py += ROW_PIXELS

    @staticmethod
    def _clip_half(lo, hi, dy, dx_, dy_, want_negative):
        """Clip [lo,hi] (horizontal offsets at height dy) by one boundary ray.
        value(x) = dx_*dy - dy_*x, linear in x. Keep value <= 0 (want_negative)
        or value >= 0."""
        if abs(dy_) < 1e-9:
            v = dx_ * dy
            ok = (v <= 0) if want_negative else (v >= 0)
            return (lo, hi) if ok else (None, None)

        bound = (dx_ * dy) / dy_          # value(bound) == 0
        if want_negative:
            # value decreases with x when dy_ > 0
            if dy_ > 0:
                lo = max(lo, bound)
            else:
                hi = min(hi, bound)
        else:
            if dy_ > 0:
                hi = min(hi, bound)
            else:
                lo = max(lo, bound)

        if hi <= lo:
            return (None, None)
        return (lo, hi)

    # --- Draw.Disc --------------------------------------------------------
    def disc(self, cx, cy, radius, c):
        """NOTE: Draw.Disc currently steps a float by RowPixels*2 from -radius,
        which is neither pixel-anchored nor the same grid the wedges use, and it
        leaves a visibly stepped edge. This emulates the one-line fix: walk whole
        pixel rows of RowPixels, anchored on the centre, exactly like Draw.Wedge."""
        if radius <= 0 or c[3] <= 0:
            return
        r2 = radius * radius
        row_h = ROW_PIXELS / float(H)

        px_top = int(math.floor((cy - radius) * H))
        px_bottom = int(math.ceil((cy + radius) * H))
        px_centre = int(round(cy * H))
        px_top -= ((px_top - px_centre) % ROW_PIXELS + ROW_PIXELS) % ROW_PIXELS

        py = px_top
        while py < px_bottom:
            row_y = (py + ROW_PIXELS * 0.5) / float(H)
            dy = cy - row_y
            dy2 = dy * dy
            if dy2 <= r2:
                half = math.sqrt(r2 - dy2)
                if half > 0:
                    self.rect(cx, row_y, self.to_x(half * 2.0), row_h * 1.02, c)
            py += ROW_PIXELS

    # --- Draw.File --------------------------------------------------------
    _icon_cache = {}

    def icon(self, name, x, y, height_fraction, c):
        side = int(round(height_fraction * H))
        if side < 1:
            return
        key = (name, side)
        if key not in Frame._icon_cache:
            path = os.path.join(ICONS, name)
            if not os.path.exists(path):
                Frame._icon_cache[key] = None
            else:
                src = Image.open(path).convert("RGBA")
                Frame._icon_cache[key] = src.resize((side, side), Image.LANCZOS)
        art = Frame._icon_cache[key]
        if art is None:
            return
        r, g, b, a = art.split()
        # white mask tinted at draw time, exactly what CustomSprite.Color does
        mask = a.point(lambda v: int(v * (c[3] / 255.0)))
        solid = Image.new("RGB", art.size, (c[0], c[1], c[2]))
        left = int(round(x * W - side * 0.5))
        top = int(round(y * H - side * 0.5))
        self.im.paste(solid, (left, top), mask)

    # --- Draw.Text / Draw.TextRight ---------------------------------------
    def text(self, s, x, y, scale, c, kind="cond", centre=True, shadow=True,
             upper=False, align_right=False):
        """y is the TOP edge of the line, as GTA's text natives place it."""
        if not s:
            return 0.0
        if upper:
            s = s.upper()
        f = font(kind, px_for(scale, kind))
        d = ImageDraw.Draw(self.im)
        w = d.textlength(s, font=f)
        px = x * W
        if centre:
            px -= w * 0.5
        elif align_right:
            px -= w
        py = y * H
        if shadow:
            d.text((px + 2, py + 2), s, font=f, fill=(0, 0, 0, 190))
        d.text((px, py), s, font=f, fill=(c[0], c[1], c[2]))
        return w / float(W)

    def measure(self, s, scale, kind="cond", upper=False):
        if not s:
            return 0.0
        if upper:
            s = s.upper()
        f = font(kind, px_for(scale, kind))
        return ImageDraw.Draw(self.im).textlength(s, font=f) / float(W)

    def fit(self, s, max_w, scale, kind="body"):
        if self.measure(s, scale, kind) <= max_w:
            return s
        while len(s) > 1 and self.measure(s + "...", scale, kind) > max_w:
            s = s[:-1]
        return s.rstrip() + "..."


# ================================================================= LAYOUT ====
# One place for words. The ring says what the choices are; the card says what
# the one under the cursor IS. Nothing is printed anywhere else on the screen.

CX          = 0.500
CY          = 0.340        # lifted so ring + card together sit optically centred
R_INNER     = 0.088
R_OUTER     = 0.196
R_MID       = (R_INNER + R_OUTER) * 0.5
GAP_DEG     = 2.5          # the ONLY separation mechanism between segments

RIM_IN      = 0.202        # cursor rim: a thin wedge just outside the ring,
RIM_OUT     = 0.211        # kept clear of it so it reads even on a white fill

ICON_H      = 0.046
ICON_DY     = -0.026       # icon centre, relative to the wedge's mid point
LABEL_DY    = 0.014        # label TOP edge, relative to the wedge's mid point

CARD_W_H    = 0.66         # card width in HEIGHT units, so it is aspect stable
CARD_TOP    = 0.582
CARD_PAD_Y  = 0.020
CARD_PAD_X  = 0.0125       # normalised-x
STEM_TOP    = 0.552

# Three type sizes. That is the whole scale.
S_SMALL = 0.30   # body face  -- detail line, stat rows
S_MID   = 0.34   # condensed  -- wedge labels, hub title, card value, row heads
S_LARGE = 0.60   # condensed  -- the hovered item's name, and only that


def draw_wheel(fr, page, hovered):
    items = page["items"]
    n = len(items)
    step = 360.0 / n
    gap = GAP_DEG if n > 1 else 0.0

    # 1. backdrop
    fr.rect(0.5, 0.5, 1.0, 1.0, BACKDROP)

    # 2. segments
    for i, it in enumerate(items):
        mid = i * step
        a0 = mid - step * 0.5 + gap * 0.5
        a1 = mid + step * 0.5 - gap * 0.5

        is_hover = (i == hovered)
        enabled = it.get("enabled", True)

        # FILL says pickable. RIM says where the cursor is. Two facts, two marks.
        if not enabled:
            fill = SEGMENT_DISABLED
        elif is_hover:
            fill = it.get("tint") or SEGMENT_HOVER
        else:
            fill = SEGMENT

        fr.wedge(CX, CY, R_INNER, R_OUTER, a0, a1, fill)

        if is_hover:
            rim = WARN if not enabled else ACCENT
            fr.wedge(CX, CY, RIM_IN, RIM_OUT, a0, a1, rim)

        # label colour
        if not enabled:
            col = alpha(WARN, 235) if is_hover else TEXT_DISABLED
        else:
            col = text_on(fill)

        rad = math.radians(mid)
        px = CX + Frame.to_x(R_MID * math.sin(rad))
        py = CY - R_MID * math.cos(rad)

        if it.get("icon"):
            fr.icon(it["icon"], px, py + ICON_DY, ICON_H, col)
        elif it.get("symbol"):
            fr.text(it["symbol"], px, py + ICON_DY - 0.022, 0.62, col, "cond")

        fr.text(it["label"], px, py + LABEL_DY, S_MID, col, "cond", upper=True)

    # 3. hub -- flush against the ring, no inset. It holds the breadcrumb and
    #    nothing else; a title is an address, not a readout.
    fr.disc(CX, CY, R_INNER, HUB)
    title = ("< " + page["title"]) if page.get("nested") else page["title"]
    fr.text(title, CX, CY - 0.012, S_MID, ACCENT, "cond", upper=True)


def draw_card(fr, page, hovered):
    """The one place words live. Fixed width, grows downward."""
    items = page["items"]
    it = items[hovered] if 0 <= hovered < len(items) else None
    rows = page.get("panel", [])

    enabled = it.get("enabled", True) if it else True
    accent = ACCENT if enabled else WARN

    w = Frame.to_x(CARD_W_H)
    left = CX - w * 0.5
    right = CX + w * 0.5

    # --- measure first, because the card is a fixed width and a variable height
    y = CARD_TOP + CARD_PAD_Y
    name_h = 0.048
    detail_h = 0.030
    row_h = 0.028
    head_lead = 0.007

    h = CARD_PAD_Y + name_h + detail_h
    if rows:
        h += 0.014                                     # hairline + air
        for r in rows:
            h += row_h + (head_lead if r.get("head") else 0.0)
    h += CARD_PAD_Y - 0.004

    # --- stem: the card is part of the wheel, not a second widget
    fr.rect(CX, (STEM_TOP + CARD_TOP) * 0.5, 0.0022, CARD_TOP - STEM_TOP,
            alpha(ACCENT, 80))

    # --- plate
    fr.rect_from(left, CARD_TOP, w, h, HUB)
    # --- one coloured mark on the whole HUD, and it carries the state
    fr.rect_from(left, CARD_TOP, w, 0.0045, accent)

    if it is None:
        fr.text(page["title"], left + CARD_PAD_X, y, S_LARGE, TEXT, "cond",
                centre=False, upper=True)
        fr.text(page.get("subtitle", ""), left + CARD_PAD_X, y + name_h,
                S_SMALL, TEXT_DIM, "body", centre=False)
        return h

    # --- name (left) and value (right) share one line
    name_col = TEXT if enabled else TEXT_DISABLED
    fr.text(it["label"], left + CARD_PAD_X, y, S_LARGE, name_col, "cond",
            centre=False, upper=True)

    if it.get("value"):
        vcol = CASH if enabled else TEXT_DIM
        fr.text(it["value"], right - CARD_PAD_X, y + 0.014, S_MID, vcol,
                "cond", centre=False, align_right=True)

    y += name_h

    # --- detail, or the reason you cannot have it
    if not enabled and it.get("reason"):
        fr.text(it["reason"], left + CARD_PAD_X, y, S_SMALL, WARN, "body",
                centre=False)
    elif it.get("detail"):
        fr.text(it["detail"], left + CARD_PAD_X, y, S_SMALL, TEXT_DIM, "body",
                centre=False)
    y += detail_h

    # --- stat rows. No stripes, no header strip: hierarchy is caps + indent.
    if rows:
        fr.rect_from(left + CARD_PAD_X, y + 0.004, w - CARD_PAD_X * 2, 0.0015,
                     alpha(ACCENT, 60))
        y += 0.014

        indent = Frame.to_x(0.017) + 0.006
        for r in rows:
            if r.get("head"):
                y += head_lead
                if r.get("art"):
                    fr.icon(r["art"], left + CARD_PAD_X + Frame.to_x(0.017) * 0.5,
                            y + row_h * 0.36, 0.017, TEXT_DIM)
                fr.text(r["label"], left + CARD_PAD_X + indent, y, S_MID,
                        alpha(ACCENT, 200), "cond", centre=False, upper=True)
            else:
                fr.text(r["label"], left + CARD_PAD_X + indent, y + 0.002,
                        S_SMALL, TEXT_DIM, "body", centre=False)
                room = (w - CARD_PAD_X * 2 - indent
                        - fr.measure(r["label"], S_SMALL, "body") - 0.012)
                fr.text(fr.fit(r["value"], room, S_SMALL, "body"),
                        right - CARD_PAD_X, y + 0.002, S_SMALL,
                        r.get("tint") or TEXT, "body",
                        centre=False, align_right=True)
            y += row_h

    return h


# ============================================================== page data ====
# Straight out of Wheel/WheelPages.cs -- BuildRoot() and BuildDrugsPage().

ROOT_PAGE = {
    "title": "Hoodrich",
    "subtitle": "Chamberlain Hills Families",
    "nested": False,
    "items": [
        {"label": "Weapons", "icon": "guns.png",
         "detail": "Opens the game's own weapon wheel", "value": "Micro SMG"},
        {"label": "Dealing", "icon": "bong.png",
         "detail": "Re-up, bag up, go to work", "value": "351 still to prep"},
        {"label": "Gangs", "icon": "mask.png",
         "detail": "You run with the Families", "value": "CGF"},
        {"label": "Inventory", "icon": "stash.png",
         "detail": "Everything you are carrying", "value": "351g on you"},
        {"label": "Socials", "icon": "tattoo.png",
         "detail": "What the block is saying, and what you say back",
         "value": "4,182 followers",
         "enabled": False, "reason": "Not right now"},
    ],
    "panel": [],
}

DEALING_PAGE = {
    "title": "Dealing",
    "subtitle": "351 still to prep",
    "nested": True,
    "items": [
        {"label": "Re-up", "icon": "money.png",
         "detail": "Text a contact for bulk weight", "value": "$12,480"},
        {"label": "Post up", "icon": "cash.png",
         "detail": "Stand on a corner and let it come to you", "value": "",
         "enabled": False,
         "reason": "All you have is weight -- prep it first"},
        {"label": "The numbers", "icon": "health.png",
         "detail": "Prices, heat, and what this block does to both", "value": ""},
    ],
    # page.PanelTitle "What you're holding" is gone: the card is already the
    # holding place, and a title on a list inside a titled card is a section
    # nobody needed.
    "panel": [
        {"head": True, "label": "On you", "art": "stash.png", "value": ""},
        {"label": "Weed", "value": "40g to prep", "tint": CASH},
        {"label": "Cocaine", "value": "6g to prep", "tint": CASH},
        {"head": True, "label": "At the house", "art": "garage.png", "value": ""},
        {"label": "Weed", "value": "220g to prep"},
        {"label": "Cocaine", "value": "55g to prep"},
        {"label": "Meth", "value": "30g to prep"},
        {"label": "Ecstasy", "value": "60 to press"},
    ],
}


# ========================================================== fake gameplay ====
def gameplay_background(seed):
    """A stand-in for a game scene: bright sky, mid buildings, dark road, plus
    grain. The HUD has to survive all three bands, not just the dark one."""
    rnd = random.Random(seed)
    im = Image.new("RGB", (W, H))
    d = ImageDraw.Draw(im)

    horizon = int(H * 0.50)

    # sky: hazy Los Santos afternoon, pale and BRIGHT -- the worst case for a
    # near-white hover fill and white text.
    for y in range(horizon):
        t = y / float(horizon)
        r = int(150 + 85 * t)
        g = int(168 + 74 * t)
        b = int(186 + 44 * t)
        d.line([(0, y), (W, y)], fill=(r, g, b))

    # sun blowout, top right -- a bright patch the HUD has to survive
    for i in range(40):
        rad = 250 - i * 6
        c = (min(255, 214 + i), min(255, 212 + i), min(255, 192 + i))
        d.ellipse([1560 - rad, 40 - rad, 1560 + rad, 40 + rad], fill=c)

    # distant block
    x = -40
    while x < W + 60:
        bw = rnd.randint(90, 240)
        bh = rnd.randint(70, 300)
        base = rnd.randint(96, 150)
        warm = rnd.randint(-14, 22)
        col = (base + warm, base + warm // 2, base - 8)
        d.rectangle([x, horizon - bh, x + bw, horizon + 8], fill=col)
        # window grid
        for wy in range(horizon - bh + 14, horizon - 8, 22):
            for wx in range(x + 12, x + bw - 12, 18):
                if rnd.random() < 0.55:
                    v = rnd.randint(70, 205)
                    d.rectangle([wx, wy, wx + 9, wy + 12],
                                fill=(v, v, min(255, v + 16)))
        x += bw + rnd.randint(4, 26)

    # road
    for y in range(horizon, H):
        t = (y - horizon) / float(H - horizon)
        v = int(58 + 46 * t)
        d.line([(0, y), (W, y)], fill=(v, v - 2, v - 4))

    # kerb + pavement
    d.rectangle([0, horizon + 4, W, horizon + 26], fill=(122, 118, 110))
    d.rectangle([0, horizon + 26, W, horizon + 34], fill=(150, 146, 138))

    # lane markings, running off to the left so the card is not judged against
    # a column of white dashes that only exist because I drew them
    for i in range(9):
        t = i / 9.0
        y0 = horizon + 60 + int(((H - horizon) * (t ** 1.9)) * 1.05)
        hgt = 6 + int(38 * t)
        wid = 8 + int(30 * t)
        cxp = int(W * 0.30 - t * W * 0.24)
        if y0 > H:
            break
        d.rectangle([cxp - wid // 2, y0, cxp + wid // 2, y0 + hgt],
                    fill=(206, 200, 168))

    # a couple of cars
    d.rectangle([300, horizon + 40, 520, horizon + 130], fill=(120, 34, 32))
    d.rectangle([330, horizon + 18, 490, horizon + 46], fill=(96, 28, 26))
    d.rectangle([1380, horizon + 70, 1660, horizon + 190], fill=(28, 40, 62))
    d.rectangle([1420, horizon + 40, 1620, horizon + 76], fill=(22, 32, 50))

    # palms
    for tx in (170, 1750):
        d.rectangle([tx, horizon - 250, tx + 16, horizon + 10], fill=(78, 66, 52))
        for k in range(7):
            ang = -0.5 + k * 0.42
            ex = tx + 8 + int(math.cos(ang) * 110)
            ey = horizon - 250 + int(abs(math.sin(ang)) * 60) - 20
            d.line([(tx + 8, horizon - 250), (ex, ey)], fill=(52, 78, 44), width=7)

    im = im.filter(ImageFilter.GaussianBlur(0.6))

    # grain
    npix = im.load()
    for _ in range(240000):
        rx = rnd.randrange(W)
        ry = rnd.randrange(H)
        n = rnd.randint(-13, 13)
        r, g, b = npix[rx, ry]
        npix[rx, ry] = (max(0, min(255, r + n)),
                        max(0, min(255, g + n)),
                        max(0, min(255, b + n)))

    # vignette, cheaply: darken the border bands
    vig = Image.new("L", (W, H), 0)
    vd = ImageDraw.Draw(vig)
    for i in range(70):
        vd.rectangle([i * 5, i * 3, W - i * 5, H - i * 3], outline=i * 2)
    vig = vig.filter(ImageFilter.GaussianBlur(90))
    im = Image.composite(im, Image.new("RGB", (W, H), (16, 16, 18)),
                         vig.point(lambda v: 255 - min(255, int(v * 0.62))))
    return im


def render_frame(page, hovered, seed):
    fr = Frame(gameplay_background(seed))
    draw_wheel(fr, page, hovered)
    draw_card(fr, page, hovered)
    return fr.im


# ============================================================ design sheet ===
def sheet():
    a = render_frame(ROOT_PAGE, 1, 7)          # hovering DEALING (enabled)
    b = render_frame(DEALING_PAGE, 1, 23)      # hovering POST UP (disabled)

    canvas = Image.new("RGB", (W, H), (17, 18, 20))
    d = ImageDraw.Draw(canvas)

    f_h1 = font("cond", 34)
    f_h2 = font("cond", 21)
    f_cap = font("body", 16)
    f_leg = font("body", 15)
    f_legb = ImageFont.truetype(BODY_PATH, 16)

    d.text((30, 16), "HOODRICH  /  RADIAL WHEEL REDESIGN", font=f_h1,
           fill=(240, 240, 240))
    d.text((30, 52), "direction: RING PLUS ONE CARD  -  one place for words, "
                     "centred, symmetrical", font=f_cap, fill=(150, 152, 156))
    d.rectangle([0, 76, W, 78], fill=(56, 58, 62))

    # frames
    fw, fh = 860, 484
    ax, ay = 30, 88
    bx, by = 1030, 88
    canvas.paste(a.resize((fw, fh), Image.LANCZOS), (ax, ay))
    canvas.paste(b.resize((fw, fh), Image.LANCZOS), (bx, by))
    d.rectangle([ax - 1, ay - 1, ax + fw, ay + fh], outline=(70, 72, 76))
    d.rectangle([bx - 1, by - 1, bx + fw, by + fh], outline=(70, 72, 76))

    cy0 = ay + fh + 6
    d.text((ax, cy0), "ROOT  -  5 items, DEALING hovered, SOCIALS disabled. "
                      "Short card: name / value / detail.",
           font=f_cap, fill=(176, 179, 181))
    d.text((bx, cy0), "DEALING  -  nested, POST UP hovered AND disabled. "
                      "The same card, grown down over 8 stat rows.",
           font=f_cap, fill=(176, 179, 181))

    d.rectangle([0, cy0 + 24, W, cy0 + 25], fill=(46, 48, 52))

    # native-resolution detail crops
    band = cy0 + 36
    d.text((30, band), "DETAIL  -  1:1 pixels, straight out of the 1920x1080 frame",
           font=f_h2, fill=(220, 220, 220))

    top = band + 30

    card_crop = b.crop((596, 618, 1324, 1022))
    canvas.paste(card_crop, (30, top))
    d.rectangle([29, top - 1, 30 + card_crop.width, top + card_crop.height],
                outline=(70, 72, 76))

    ring_crop = b.crop((744, 132, 1176, 564))
    canvas.paste(ring_crop, (790, top))
    d.rectangle([789, top - 1, 790 + ring_crop.width, top + ring_crop.height],
                outline=(70, 72, 76))

    # legend
    lx = 1266
    ly = top - 2
    lead = 19

    def block(head, colour, lines, gap=12):
        nonlocal ly
        d.text((lx, ly), head, font=f_legb, fill=colour)
        ly += 23
        for ln in lines:
            d.text((lx, ly), ln, font=f_leg, fill=(176, 179, 181))
            ly += lead
        ly += gap

    block("WHAT THIS REMOVES", (232, 177, 44), [
        "- the top-of-screen readout (script face at 0.90)",
        "- the right-hand panel; its rows live in the card now",
        "- the panel header strip and the alternating row wash",
        "- the 3% hover reach and the 0.96 hub inset",
        "- the script face. Two faces now, not three.",
    ])

    block("WHAT IT KEEPS, AND WHY", (126, 190, 79), [
        "- 2.5 deg gaps: the ONE separation mechanism",
        "- FILL says pickable: black / white / half-alpha hole",
        "- RIM says where the cursor is. Two facts, two marks,",
        "  so hovering something dead reads honestly instead",
        "  of lighting up like a thing you can have.",
        "- disabled items still draw: the angle-to-item map",
        "  must never move under your thumb.",
    ])

    block("TYPE  -  three sizes, two game faces", (220, 220, 220), [
        "0.60 condensed   the hovered name, and nothing else",
        "0.34 condensed   wedge labels, hub, value, row heads",
        "0.30 body        detail line and stat rows",
    ])

    d.text((lx, ly), "COST  -  n wedges + 1 rim + 1 disc + 6 rects.",
           font=f_leg, fill=(150, 152, 156))
    ly += lead
    d.text((lx, ly), "At n=8 that is under what the wheel draws today.",
           font=f_leg, fill=(150, 152, 156))

    canvas.save(OUT)
    return OUT


if __name__ == "__main__":
    p = sheet()
    print(p, os.path.getsize(p), "bytes")
