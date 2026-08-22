"""
HOODRICH radial wheel redesign -- direction "tight": EVERYTHING AT THE RING.

What this direction throws away
  * the top-of-screen readout (was at y 0.085, half a screen away from the ring)
  * the right-hand stat panel (made the whole composition lean)
  * the page title as a separate thing from the hovered item
  * the 3% hover reach and the 0.96 hub inset (two extra separators on top of the gap)
  * the script/cursive face (four type sizes -> three, two faces -> two, one display job)
  * panel header strips and alternating row washes

What it keeps, and where it puts it
  * the hub is now the ONLY readout: breadcrumb, item name, value, detail/reason.
  * a page's stat rows become a compact block CENTRED UNDER the ring, and the ring
    shifts up by half the block so the whole thing stays vertically centred.
  * hub and block share the Hub colour, so "dark disc / dark bar" = information and
    "wedge" = choice. Two object classes, not five.
  * disabled items still occupy their slot (muscle memory) but are drawn as a SHORT
    tooth -- 60% of the ring depth -- so an unpickable option is unmistakable by
    silhouette alone, not by a 5/255 colour difference.

Everything here maps 1:1 onto Draw.cs primitives:
  Rect / RectUniform  -> axis-aligned rectangles (block, separators)
  Wedge               -> 2px scanline rows, rasterised here exactly as Draw.Wedge does
  Disc                -> hub
  File                -> data/icons PNG masks, tinted at draw time
  Text / TextRight / MeasureText / Fit -> all text, top-edge anchored, no tracking

The background is procedural gameplay-ish scenery. Only the HUD layer is the design.
"""

import math
import os
import random
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = r"C:\projects\hoodrich"
ICONS = os.path.join(ROOT, "data", "icons")
OUT = os.path.join(ROOT, "preview", "wheel_tight.png")

W, H = 1920, 1080
ASPECT = W / H
ROW_PIXELS = 2  # Draw.cs RowPixels

# Every rectangle this design would ask DRAW_RECT for, counted, because a wedge is a
# stack of them and a design that costs frames is not a design.
RECTS = [0]

# ---------------------------------------------------------------- palette
BACKDROP = (0, 0, 0, 125)          # lighter than the 140 it was; the hub carries focus now
# Segment is lifted off near-black. The old (10,12,14,200) vanished completely into a
# night scene -- over a dimmed alley it composited to within a couple of values of the
# backdrop, so the ring existed only where its labels were. This sits ABOVE the dimmed
# world on dark scenes and below it on bright ones, so the ring is a ring either way.
SEGMENT = (30, 32, 36, 220)
SEGMENT_HOVER = (240, 242, 240, 240)

# Disabled is the SAME fill as enabled. Colour cannot carry this state: a translucent
# grey reads darker than its neighbours over a bright street and lighter over a dark
# alley, which is how the first pass of this very design ended up with a pale blob
# where an unpickable option should have been. The silhouette carries it instead --
# see DISABLED_DEPTH.
SEGMENT_DISABLED = (30, 32, 36, 220)
SEGMENT_DISABLED_HOVER = (52, 41, 18, 230)   # NEW palette entry this design needs
HUB = (8, 9, 11, 245)
TEXT = (255, 255, 255, 245)
TEXT_DIM = (176, 179, 181, 190)
TEXT_ON_HOVER = (16, 18, 20, 255)
TEXT_DISABLED = (150, 152, 156, 255)
ACCENT = (245, 245, 245, 255)
CASH = (126, 190, 79, 255)
WARN = (232, 177, 44, 255)
RULE = (176, 179, 181, 70)

# ---------------------------------------------------------------- geometry (height units)
R_INNER = 0.142
R_OUTER = 0.245
HUB_INSET = 0.007              # the one gap between "choices" and "the readout"
R_MID = (R_INNER + R_OUTER) * 0.5
GAP_DEG = 2.5
DISABLED_DEPTH = 0.60          # a disabled wedge is a short tooth

BLOCK_GAP = 0.022              # ring -> stat block
BLOCK_PAD = 0.014
BLOCK_ROW = 0.031
BLOCK_COL = 0.088              # group-label column (normalized x) so values line up

# ---------------------------------------------------------------- type
# Three sizes only. GTA scale ~= px / 64 at 1080p.
PX_LABEL = 37   # scale 0.62, FontLabel      -- hovered item name, in the hub. The one big thing.
PX_VALUE = 23   # scale 0.36, FontChaletLondon
PX_SMALL = 19   # scale 0.30 -- wedge labels, breadcrumb, detail, every stat row

FONT_DIR = r"C:\Windows\Fonts"
F_LABEL = ImageFont.truetype(os.path.join(FONT_DIR, "bahnschrift.ttf"), PX_LABEL)
F_WEDGE = ImageFont.truetype(os.path.join(FONT_DIR, "bahnschrift.ttf"), PX_SMALL)
F_VALUE = ImageFont.truetype(os.path.join(FONT_DIR, "bahnschrift.ttf"), PX_VALUE)
F_SMALL = ImageFont.truetype(os.path.join(FONT_DIR, "bahnschrift.ttf"), PX_SMALL)
F_NOTE = ImageFont.truetype(os.path.join(FONT_DIR, "bahnschrift.ttf"), 17)


def tox(h):
    """Height fraction -> normalized x. Draw.ToX."""
    return h / ASPECT


# ---------------------------------------------------------------- primitives
def rect(layer, x, y, w, h, colour):
    """Draw.Rect: centre-anchored, w in normalized x, h in normalized y."""
    RECTS[0] += 1
    d = ImageDraw.Draw(layer, "RGBA")
    left = (x - w * 0.5) * W
    top = (y - h * 0.5) * H
    d.rectangle([left, top, left + w * W, top + h * H], fill=colour)


def rect_uniform(layer, x, y, wh, hh, colour):
    """Draw.RectUniform: both dimensions in height units."""
    rect(layer, x, y, tox(wh), hh, colour)


def _emit_row(d, cx, row_y, lo, hi, colour):
    if hi <= lo:
        return
    RECTS[0] += 1
    left = (cx + tox(lo)) * W
    right = (cx + tox(hi)) * W
    top = row_y
    d.rectangle([left, top, right, top + ROW_PIXELS], fill=colour)


def wedge(layer, cx, cy, r_in, r_out, a_from, a_to, colour):
    """Draw.Wedge, rasterised the same way: 2px scanline rows, analytic per row."""
    if r_out <= r_in or a_to <= a_from:
        return
    if a_to - a_from > 170.0:
        mid = (a_from + a_to) * 0.5
        wedge(layer, cx, cy, r_in, r_out, a_from, mid, colour)
        wedge(layer, cx, cy, r_in, r_out, mid, a_to, colour)
        return

    d = ImageDraw.Draw(layer, "RGBA")
    a0, a1 = math.radians(a_from), math.radians(a_to)
    d0x, d0y = math.sin(a0), math.cos(a0)
    d1x, d1y = math.sin(a1), math.cos(a1)
    ro2, ri2 = r_out * r_out, r_in * r_in

    px_centre = int(round(cy * H))
    px_top = int(math.floor((cy - r_out) * H))
    px_top -= ((px_top - px_centre) % ROW_PIXELS + ROW_PIXELS) % ROW_PIXELS
    px_bottom = int(math.ceil((cy + r_out) * H))

    for py in range(px_top, px_bottom, ROW_PIXELS):
        row_y_norm = (py + ROW_PIXELS * 0.5) / H
        dy = cy - row_y_norm
        dy2 = dy * dy
        if dy2 > ro2:
            continue
        hi = math.sqrt(ro2 - dy2)
        lo = math.sqrt(ri2 - dy2) if dy2 < ri2 else 0.0

        umin, umax = -hi, hi

        # inside0: u*d0y - dy*d0x >= 0
        if abs(d0y) < 1e-6:
            if -dy * d0x < 0:
                continue
        elif d0y > 0:
            umin = max(umin, dy * d0x / d0y)
        else:
            umax = min(umax, dy * d0x / d0y)

        # inside1: u*d1y - dy*d1x <= 0
        if abs(d1y) < 1e-6:
            if -dy * d1x > 0:
                continue
        elif d1y > 0:
            umax = min(umax, dy * d1x / d1y)
        else:
            umin = max(umin, dy * d1x / d1y)

        if umax <= umin:
            continue

        if lo <= 0.0:
            _emit_row(d, cx, py, umin, umax, colour)
        else:
            _emit_row(d, cx, py, umin, min(umax, -lo), colour)
            _emit_row(d, cx, py, max(umin, lo), umax, colour)


def disc(layer, cx, cy, radius, colour):
    """Draw.Disc: same 2px rows, no hole, no clip."""
    d = ImageDraw.Draw(layer, "RGBA")
    px_centre = int(round(cy * H))
    px_top = int(math.floor((cy - radius) * H))
    px_top -= ((px_top - px_centre) % ROW_PIXELS + ROW_PIXELS) % ROW_PIXELS
    for py in range(px_top, int(math.ceil((cy + radius) * H)), ROW_PIXELS):
        dy = cy - (py + ROW_PIXELS * 0.5) / H
        if abs(dy) > radius:
            continue
        half = math.sqrt(radius * radius - dy * dy)
        _emit_row(d, cx, py, -half, half, colour)


# ---------------------------------------------------------------- text
def measure(text, font):
    """Draw.MeasureText, in normalized x."""
    if not text:
        return 0.0
    return ImageDraw.Draw(Image.new("RGB", (1, 1))).textlength(text, font=font) / W


def text(layer, s, x, y, font, colour, centre=True):
    """Draw.Text: y is the TOP edge, x is the centre (or the left when centre=False)."""
    if not s:
        return
    d = ImageDraw.Draw(layer, "RGBA")
    d.text((x * W, y * H), s, font=font, fill=colour, anchor="ma" if centre else "la")


def text_right(layer, s, right_x, y, font, colour):
    if not s:
        return
    d = ImageDraw.Draw(layer, "RGBA")
    d.text((right_x * W, y * H), s, font=font, fill=colour, anchor="ra")


def fit(s, max_w, font):
    """Draw.Fit: shorten with an ellipsis until it fits."""
    if measure(s, font) <= max_w:
        return s
    while s and measure(s + "...", font) > max_w:
        s = s[:-1]
    return s.rstrip() + "..."


def wrap(s, max_w, font, max_lines=2):
    """Word wrap by MeasureText -- a dozen lines of C#, nothing exotic."""
    if not s:
        return []
    words, lines, cur = s.split(), [], ""
    for word in words:
        trial = word if not cur else cur + " " + word
        if measure(trial, font) <= max_w:
            cur = trial
        else:
            if cur:
                lines.append(cur)
            cur = word
            if len(lines) == max_lines:
                break
    if cur and len(lines) < max_lines:
        lines.append(cur)
    if len(lines) == max_lines:
        lines[-1] = fit(lines[-1], max_w, font)
    return lines


# ---------------------------------------------------------------- icons
_icon_cache = {}


def icon_mask(name):
    if name in _icon_cache:
        return _icon_cache[name]
    img = Image.open(os.path.join(ICONS, name)).convert("RGBA")
    alpha = img.getchannel("A")
    if alpha.getextrema()[0] == 255:          # opaque file: the art is in the luminance
        alpha = img.convert("L")
    _icon_cache[name] = alpha
    return alpha


def draw_file(layer, name, x, y, height_frac, colour):
    """Draw.File: square PNG mask, centre-anchored, tinted."""
    px = max(4, int(round(height_frac * H)))
    mask = icon_mask(name).resize((px, px), Image.LANCZOS)
    tint = Image.new("RGBA", (px, px), colour)
    layer.paste(tint, (int(round(x * W - px / 2)), int(round(y * H - px / 2))), mask)


# ---------------------------------------------------------------- the wheel
def text_on(fill):
    luma = (0.2126 * fill[0] + 0.7152 * fill[1] + 0.0722 * fill[2]) / 255.0
    return TEXT_ON_HOVER if luma > 0.55 else TEXT


def render_wheel(layer, page, hovered):
    items = page["items"]
    n = len(items)
    block = page.get("block")

    # One separator idea, not three: a 2.5 degree gap. No hover reach, no hub inset.
    block_h = 0.0
    if block:
        block_h = BLOCK_PAD * 2 + BLOCK_ROW * len(block)

    cx = 0.5
    cy = 0.5 - (BLOCK_GAP + block_h) * 0.5 if block else 0.5

    rect(layer, 0.5, 0.5, 1.0, 1.0, BACKDROP)

    step = 360.0 / n
    gap = GAP_DEG if n > 1 else 0.0

    for i, item in enumerate(items):
        mid = i * step
        a0 = mid - step * 0.5 + gap * 0.5
        a1 = mid + step * 0.5 - gap * 0.5
        hov = i == hovered
        enabled = item.get("enabled", True)

        if not enabled:
            fill = SEGMENT_DISABLED_HOVER if hov else SEGMENT_DISABLED
            r_out = R_INNER + (R_OUTER - R_INNER) * DISABLED_DEPTH
        else:
            fill = item.get("tint") or SEGMENT_HOVER if hov else SEGMENT
            r_out = R_OUTER

        wedge(layer, cx, cy, R_INNER, r_out, a0, a1, fill)

        rad = math.radians(mid)
        r_label = (R_INNER + r_out) * 0.5
        px = cx + tox(r_label * math.sin(rad))
        py = cy - r_label * math.cos(rad)

        if not enabled:
            colour = WARN if hov else TEXT_DISABLED
            draw_file(layer, item["icon"], px, py - 0.015, 0.024, colour)
            text(layer, item["label"].upper(), px, py + 0.003, F_WEDGE, colour)
        else:
            colour = text_on(fill)
            draw_file(layer, item["icon"], px, py - 0.028, 0.040, colour)
            text(layer, item["label"].upper(), px, py + 0.013, F_WEDGE, colour)

    draw_hub(layer, cx, cy, page, hovered)

    if block:
        draw_block(layer, cx, cy + R_OUTER + BLOCK_GAP, block)


def draw_hub(layer, cx, cy, page, hovered):
    """
    The only readout on the screen.

    Breadcrumb, name, value, detail -- four lines, three sizes, all of it inside the disc
    the ring is already wrapped around. The old build said the hub was too small for this;
    it was, at rInner 0.085. At 0.135 the usable chord is 278 device pixels and everything
    the top readout used to carry fits, with wrapping instead of a whole other surface.
    """
    disc(layer, cx, cy, R_INNER - HUB_INSET, HUB)

    text(layer, page["crumb"].upper(), cx, cy - 0.101, F_SMALL, TEXT_DIM)

    item = page["items"][hovered] if hovered is not None else None
    if item is None:
        text(layer, page["title"].upper(), cx, cy - 0.030, F_LABEL, ACCENT)
        text(layer, page.get("subtitle", ""), cx, cy + 0.022, F_SMALL, TEXT_DIM)
        return

    enabled = item.get("enabled", True)
    name = item["label"].upper() + (" >" if item.get("sub") else "")
    name = fit(name, tox(math.sqrt(max((R_INNER - HUB_INSET) ** 2 - 0.052 ** 2, 0.0)) * 2), F_LABEL)
    text(layer, name, cx, cy - 0.052, F_LABEL, ACCENT if enabled else TEXT_DISABLED)

    y = cy - 0.006
    if item.get("value") and enabled:
        text(layer, item["value"], cx, y, F_VALUE, CASH)
        y += 0.028

    detail = item.get("reason") if not enabled else item.get("detail", "")
    max_w = tox(math.sqrt(max((R_INNER - HUB_INSET) ** 2 - 0.086 ** 2, 0.0)) * 2)
    for line in wrap(detail, max_w, F_SMALL, 2):
        text(layer, line, cx, y, F_SMALL, TEXT_DIM if enabled else WARN)
        y += 0.023


def draw_block(layer, cx, top, block):
    """
    The stat rows, under the ring, centred.

    Same colour as the hub so the eye files it with "things that tell you" rather than
    with "things you can pick". One row per group, laid out as chips instead of as a
    column of label/value pairs -- a wide short block sits under a circle; a tall thin
    one only ever sat beside it.
    """
    sep = tox(0.012)
    widths = []
    for row in block:
        w = BLOCK_COL
        for j, (label, value) in enumerate(row["chips"]):
            if j:
                w += sep
            w += measure(label, F_SMALL) + tox(0.008) + measure(value, F_SMALL)
        widths.append(w)

    width = max(widths) + tox(BLOCK_PAD) * 2
    height = BLOCK_PAD * 2 + BLOCK_ROW * len(block)
    cy = top + height * 0.5

    rect(layer, cx, cy, width, height, HUB)

    left = cx - width * 0.5 + tox(BLOCK_PAD)
    y = top + BLOCK_PAD

    for row in block:
        draw_file(layer, row["icon"], left + tox(0.010), y + 0.010, 0.019, TEXT_DIM)
        text(layer, row["group"].upper(), left + tox(0.024), y, F_SMALL, TEXT_DIM, centre=False)

        x = left + BLOCK_COL
        for j, (label, value) in enumerate(row["chips"]):
            if j:
                rect(layer, x - sep * 0.5, y + 0.011, 0.0007, 0.019, RULE)
            text(layer, label, x, y, F_SMALL, TEXT_DIM, centre=False)
            x += measure(label, F_SMALL) + tox(0.008)
            text(layer, value, x, y, F_SMALL, row.get("tint", TEXT), centre=False)
            x += measure(value, F_SMALL) + sep

        y += BLOCK_ROW


# ---------------------------------------------------------------- gameplay backdrop
def scene_street(seed):
    """Daylight street: sky, blocks, road. Rough, but it has the tonal range a HUD meets."""
    rnd = random.Random(seed)
    sky = Image.new("RGB", (8, 64))
    d = ImageDraw.Draw(sky)
    for i in range(64):
        t = i / 63.0
        d.line([(0, i), (8, i)], fill=(int(96 + 96 * t), int(126 + 86 * t), int(158 + 52 * t)))
    img = sky.resize((W, H), Image.BICUBIC)
    d = ImageDraw.Draw(img)

    for i in range(26):
        bw = rnd.randint(90, 260)
        bx = rnd.randint(-100, W)
        bh = rnd.randint(180, 520)
        top = 430 - bh
        g = rnd.randint(96, 150)
        d.rectangle([bx, top, bx + bw, 470], fill=(g, g - 6, g - 12))
        for wy in range(top + 18, 460, 26):
            for wx in range(bx + 12, bx + bw - 12, 22):
                if rnd.random() < 0.55:
                    v = rnd.randint(150, 215)
                    d.rectangle([wx, wy, wx + 11, wy + 14], fill=(v, v, v - 10))

    d.rectangle([0, 460, W, 560], fill=(122, 120, 116))
    d.rectangle([0, 545, W, H], fill=(58, 58, 60))
    for i in range(9):
        y = 580 + i * i * 6
        d.rectangle([0, y, W, y + 3 + i], fill=(70, 70, 72))
    for i in range(7):
        y = 620 + i * i * 9
        d.rectangle([W // 2 - 70 - i * 30, y, W // 2 + 70 + i * 30, y + 6 + i * 2], fill=(196, 186, 120))

    for cx0, cw, col in ((250, 300, (140, 40, 38)), (1420, 340, (36, 52, 92)), (900, 210, (30, 30, 32))):
        d.rectangle([cx0, 500, cx0 + cw, 500 + cw // 3], fill=col)
        d.rectangle([cx0 + cw // 6, 470, cx0 + cw - cw // 5, 505], fill=tuple(int(c * 0.7) for c in col))
    for tx in (140, 620, 1180, 1760):
        d.rectangle([tx, 180, tx + 16, 500], fill=(78, 66, 54))
        for a in range(-4, 5):
            d.line([(tx + 8, 190), (tx + 8 + a * 34, 120 + abs(a) * 16)], fill=(52, 82, 46), width=9)
    return img


def scene_alley(seed):
    """Night alley under a sodium light: the other end of the range."""
    rnd = random.Random(seed)
    img = Image.new("RGB", (W, H), (26, 24, 26))
    d = ImageDraw.Draw(img)
    for y in range(0, 640, 34):
        off = 0 if (y // 34) % 2 == 0 else 34
        for x in range(-40, W, 68):
            v = rnd.randint(46, 70)
            d.rectangle([x + off, y, x + off + 62, y + 28], fill=(v + 10, v - 4, v - 8))
    d.rectangle([0, 620, W, H], fill=(34, 33, 36))
    for i in range(300):
        x, y = rnd.randint(0, W), rnd.randint(620, H)
        v = rnd.randint(40, 78)
        d.ellipse([x, y, x + rnd.randint(14, 90), y + rnd.randint(4, 16)], fill=(v, v, v + 6))

    glow = Image.new("RGB", (W, H), (0, 0, 0))
    gd = ImageDraw.Draw(glow)
    for r in range(520, 0, -20):
        v = int(200 * (1 - r / 520.0) ** 2)
        gd.ellipse([1380 - r, 60 - r, 1380 + r, 60 + r], fill=(v, int(v * 0.74), int(v * 0.34)))
    img = Image.blend(img, Image.blend(img, glow, 0.55).filter(ImageFilter.GaussianBlur(40)), 0.85)

    d = ImageDraw.Draw(img)
    d.rectangle([190, 500, 520, 720], fill=(38, 60, 52))
    d.rectangle([190, 490, 520, 512], fill=(52, 78, 68))
    d.rectangle([1500, 300, 1720, 700], fill=(46, 44, 42))
    return img


def dress(img, seed):
    """Grain and a vignette -- what stops it looking like flat vector shapes."""
    noise = Image.effect_noise((W, H), 26).convert("L")
    img = Image.blend(img, Image.merge("RGB", (noise, noise, noise)), 0.10)
    img = img.filter(ImageFilter.GaussianBlur(0.7))

    vig = Image.new("L", (W, H), 0)
    vd = ImageDraw.Draw(vig)
    for i in range(60):
        t = i / 59.0
        vd.ellipse([-W * 0.35 + t * W * 0.42, -H * 0.35 + t * H * 0.42,
                    W * 1.35 - t * W * 0.42, H * 1.35 - t * H * 0.42], fill=int(255 * t))
    vig = vig.filter(ImageFilter.GaussianBlur(60))
    return Image.composite(img, Image.new("RGB", (W, H), (10, 10, 12)), vig)


# ---------------------------------------------------------------- pages (real content)
ROOT_PAGE = {
    "crumb": "Hoodrich",
    "title": "Hoodrich",
    "subtitle": "The Families",
    "items": [
        {"label": "Weapons", "icon": "guns.png",
         "detail": "Opens the game's own weapon wheel", "value": "Micro SMG"},
        {"label": "Dealing", "icon": "bong.png", "sub": True,
         "detail": "Re-up, bag up, go to work", "value": "12g ready, 20g to prep"},
        {"label": "Gangs", "icon": "mask.png", "sub": True,
         "detail": "You run with the Families", "value": "TFF"},
        {"label": "Inventory", "icon": "stash.png",
         "detail": "Everything you are carrying", "value": "32g  ·  $4,180"},
        {"label": "Socials", "icon": "tattoo.png",
         "detail": "What the block is saying, and what you say back",
         "enabled": False, "reason": "Not right now"},
    ],
}

DEALING_PAGE = {
    "crumb": "< Dealing",
    "title": "Dealing",
    "subtitle": "12g ready, 20g to prep",
    "items": [
        {"label": "Re-up", "icon": "money.png", "sub": True,
         "detail": "Text a contact for bulk weight", "value": "$12,450"},
        {"label": "Post up", "icon": "cash.png", "sub": True,
         "detail": "Stand on a corner and let it come to you",
         "enabled": False, "reason": "All you have is weight -- prep it first"},
        {"label": "The numbers", "icon": "health.png",
         "detail": "Prices, heat, and what this block does to both"},
    ],
    "block": [
        {"group": "On you", "icon": "stash.png", "tint": CASH,
         "chips": [("Weed", "12g"), ("Cocaine", "3.5g  ·  20g to prep")]},
        {"group": "At the house", "icon": "garage.png", "tint": TEXT,
         "chips": [("Meth", "40g"), ("Ecstasy", "40 pills  ·  60 to press")]},
    ],
}


def frame(scene, page, hovered):
    RECTS[0] = 0
    base = dress(scene, 7).convert("RGBA")
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    render_wheel(layer, page, hovered)
    print("  %-9s %4d DRAW_RECT calls" % (page["title"], RECTS[0]))
    return Image.alpha_composite(base, layer)


def main():
    a = frame(scene_street(3), ROOT_PAGE, 1)        # Dealing hovered, Socials disabled
    b = frame(scene_alley(11), DEALING_PAGE, 1)     # Post up hovered AND disabled

    crop_w, margin, gutter = 900, 30, 60
    x0 = (W - crop_w) // 2
    sheet = Image.new("RGBA", (W, H), (14, 14, 16, 255))
    sheet.paste(a.crop((x0, 0, x0 + crop_w, H)), (margin, 0))
    sheet.paste(b.crop((x0, 0, x0 + crop_w, H)), (margin + crop_w + gutter, 0))

    d = ImageDraw.Draw(sheet, "RGBA")
    d.rectangle([margin + crop_w + gutter // 2 - 1, 40, margin + crop_w + gutter // 2, H - 40],
                fill=(90, 92, 96, 90))
    d.text((margin + 8, 26), "ROOT  ·  5 ITEMS  ·  ONE DISABLED", font=F_NOTE, fill=(150, 152, 156, 220))
    d.text((margin + crop_w + gutter + 8, 26),
           "DEALING  ·  NESTED  ·  DISABLED ITEM HOVERED  ·  STAT BLOCK",
           font=F_NOTE, fill=(150, 152, 156, 220))

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    sheet.convert("RGB").save(OUT)
    print(OUT, os.path.getsize(OUT), "bytes")


if __name__ == "__main__":
    main()
