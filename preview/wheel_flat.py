# -*- coding: utf-8 -*-
#
# HOODRICH wheel redesign -- direction "FLAT SECTOR, NO GAPS"  (key: flat)
#
# The thesis: the current wheel separates things three times over (3 degree gaps, a 3% hover
# reach, a 0.96 hub inset) and says the same thing in three places (wedge label, hub title,
# top-of-screen readout, plus a right-hand panel that drags the whole composition sideways).
#
# This design does the opposite. Sectors TOUCH. Selection is a change of FILL, not of size.
# Division is one hairline. Every word sits inside or directly under the ring, on one vertical
# axis, so the composition is symmetric and the reading distance is short.
#
# WHAT IS REMOVED
#   * the 3 degree segment gap          -> sectors meet exactly, one hairline between them
#   * the 3% hover reach                -> hovered sector is the SAME shape, near-white fill
#   * the hub disc and its 0.96 inset   -> the centre is open backdrop; the ring floats
#   * the top-of-screen readout at 0.085 -> folded into the open centre
#   * the right-hand stat panel box     -> two centred columns under the ring, no box
#   * the panel header strip + accent rule + alternating row washes + PanelTitle
#   * the label duplicated in the readout (the wedge already says it, in white, lit up)
#   * two of the four type sizes
#
# WHAT IS DRAWN, AND WITH WHAT
#   Draw.Rect            backdrop, hairline rules, footer rules
#   Draw.Wedge           the sectors (no gap between them)
#   Draw.Rect            the hairline spokes, as a run of ~55 small squares along the radius
#   Draw.File            the data/icons PNGs, tinted
#   Draw.Text / TextRight / MeasureText / Fit    all copy
#   No Draw.Disc at all -- there is no hub any more.
#   Nothing here needs a gradient, a rounded corner, a blur or a stroked line.
#
# UNITS: fractions of screen HEIGHT. x is divided by aspect (Draw.ToX) before it becomes a
# width fraction. Panel/column widths are already WIDTH fractions and are not divided.
#
# THE IMAGE: two 950 px wide windows into a 1920x1080 game frame, at 1:1 pixel scale, so the
# type is shown at the size it will really be. The design lives on the centre column of the
# screen, so nothing outside those windows is being hidden.

import math
import os
import random

from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = r'C:\projects\hoodrich'
ICONS = os.path.join(ROOT, 'data', 'icons')
OUT = os.path.join(ROOT, 'preview', 'wheel_flat.png')

SW, SH = 1920, 1080          # the game frame each window looks into
ASPECT = SW / float(SH)
WIN_W = 950                  # window width, 1:1
CANVAS_W, CANVAS_H = 1920, 1080

# ---- palette (Palette.cs, as RGBA) -----------------------------------------

BACKDROP      = (0, 0, 0, 140)
SEGMENT       = (10, 12, 14, 210)
SEG_HOVER     = (240, 242, 240, 245)
SEG_DISABLED  = (10, 12, 14, 210)    # disabled: the SAME fill as enabled. See note below.
HAIRLINE      = (245, 245, 245, 78)  # Palette.Alpha(Accent, 78)
RULE          = (245, 245, 245, 55)
RULE_FAINT    = (245, 245, 245, 34)
TEXT          = (255, 255, 255, 245)
TEXT_DIM      = (176, 179, 181, 190)
TEXT_ON_HOVER = (16, 18, 20, 255)
TEXT_DISABLED = (150, 152, 156, 255)
ACCENT        = (245, 245, 245, 255)
CASH          = (126, 190, 79, 255)
WARN          = (232, 177, 44, 255)

# ---- geometry ---------------------------------------------------------------

INNER  = 0.125     # was 0.085. Bigger hole, so the readout can live in it.
OUTER  = 0.212     # was 0.200
MID    = (INNER + OUTER) * 0.5
BAND   = OUTER - INNER          # 0.087 -- thinner than today's 0.115

HAIR_LINE_PX = 2.0  # hairline thickness in device pixels, drawn as a run of small squares.

ICON_H   = 0.042
ICON_DY  = -0.020
LABEL_DY = 0.010

FOOT_TOP  = 0.5 + OUTER + 0.036
ROW_H     = 0.030
COL_W     = 0.215                     # width fraction
COL_GUT   = 0.036
FOOT_LEFT = 0.5 - (COL_W * 2 + COL_GUT) * 0.5


# ---- text -------------------------------------------------------------------

def _load(paths, px):
    for p in paths:
        try:
            return ImageFont.truetype(p, px)
        except Exception:
            pass
    return ImageFont.load_default()


_CACHE = {}


def gfont(px, condensed):
    """FontLabel is Chalet Comprime Cologne (condensed); FontBody is Chalet London (plain)."""
    key = (px, condensed)
    if key in _CACHE:
        return _CACHE[key]
    if condensed:
        f = _load([r'C:\Windows\Fonts\bahnschrift.ttf', r'C:\Windows\Fonts\segoeuisl.ttf',
                   r'C:\Windows\Fonts\arialn.ttf'], px)
        try:
            f.set_variation_by_name('Condensed')
        except Exception:
            pass
    else:
        f = _load([r'C:\Windows\Fonts\segoeui.ttf', r'C:\Windows\Fonts\arial.ttf'], px)
    _CACHE[key] = f
    return f


def px_of(scale):
    """GTA text scale -> pixels. Matched to wheel_current.py so sizes compare like for like."""
    return max(9, int(round(scale * 52)))


class Type(object):
    """Collects every string for one frame so they can be composited in one pass."""

    def __init__(self):
        self.layer = Image.new('RGBA', (SW, SH), (0, 0, 0, 0))
        self.d = ImageDraw.Draw(self.layer)

    def measure(self, s, scale, condensed):
        return self.d.textlength(s, font=gfont(px_of(scale), condensed)) / float(SW)

    def put(self, s, x, y, scale, colour, condensed=True, align='c', shadow=True):
        """x,y are normalized; y is the TOP edge, matching Draw.Text."""
        if not s:
            return
        f = gfont(px_of(scale), condensed)
        w = self.d.textlength(s, font=f)
        px = x * SW
        if align == 'c':
            px -= w * 0.5
        elif align == 'r':
            px -= w
        py = y * SH
        if shadow:
            self.d.text((px + 1.4, py + 1.4), s, font=f, fill=(0, 0, 0, 200))
        self.d.text((px, py), s, font=f, fill=colour)

    def fit(self, s, max_w, scale, condensed):
        """Draw.Fit: trim to width (a WIDTH fraction) with an ellipsis."""
        if not s or self.measure(s, scale, condensed) <= max_w:
            return s
        while s and self.measure(s + '...', scale, condensed) > max_w:
            s = s[:-1]
        return s.rstrip(' ,') + '...' if s else ''

    def wrap(self, s, max_w, scale, condensed, lines=2):
        """Word wrap. In C# this is a loop over MeasureText, nothing exotic."""
        words, out, cur = s.split(), [], ''
        for word in words:
            trial = (cur + ' ' + word).strip()
            if cur and self.measure(trial, scale, condensed) > max_w:
                out.append(cur)
                cur = word
                if len(out) == lines:
                    break
            else:
                cur = trial
        if cur and len(out) < lines:
            out.append(cur)
        if len(out) == lines and cur and out[-1] != cur:
            out[-1] = self.fit(out[-1] + ' ' + cur, max_w, scale, condensed)
        return out[:lines]


# ---- shapes -----------------------------------------------------------------

def to_x(h):
    return h / ASPECT


class Shapes(object):
    """One layer of rectangles. Everything in here is literally Draw.Rect."""

    def __init__(self):
        self.layer = Image.new('RGBA', (SW, SH), (0, 0, 0, 0))
        self.d = ImageDraw.Draw(self.layer)

    def rect(self, x, y, w, h, c):
        """Draw.Rect: centre-anchored, w and h already normalized on their own axis."""
        x0 = (x - w * 0.5) * SW
        x1 = (x + w * 0.5) * SW
        y0 = (y - h * 0.5) * SH
        y1 = (y + h * 0.5) * SH
        self.d.rectangle([x0, y0, max(x1, x0 + 1), max(y1, y0 + 1)], fill=c)

    def rule(self, cx, y, w, c, thick=0.0016):
        self.rect(cx, y, w, thick, c)

    def spoke(self, cx, cy, r_in, r_out, angle_deg, c, thick_px=2.0, step_px=1.6):
        """
        The hairline that divides two sectors.
        NOT a thin wedge: a 0.3 degree wedge is narrower than one 2 px scan row wherever the
        boundary runs near-horizontal, so it comes out dotted -- the exact defect the old
        Draw.Arc had. Walked along the RADIUS instead, one small square per step, which is
        solid at every angle and costs about 55 rects. Draw.Rect only.
        """
        rad = math.radians(angle_deg)
        sx, sy = math.sin(rad), math.cos(rad)
        t = thick_px / float(SH)
        steps = max(2, int(((r_out - r_in) * SH) / step_px))
        for k in range(steps + 1):
            r = r_in + (r_out - r_in) * k / float(steps)
            self.rect(cx + to_x(r * sx), cy - r * sy, to_x(t), t, c)

    def wedge(self, cx, cy, r_in, r_out, a_from, a_to, c):
        """
        Draw.Wedge, emulated row by row exactly as Draw.cs does it -- RowPixels = 2, rows
        landed on whole device pixels, one Rect per row. The slightly stepped edges you can
        see here are the real ones; this is not a smoothed preview.
        """
        span = a_to - a_from
        if span <= 0 or r_out <= r_in:
            return
        if span > 170.0:                      # MaxSpanDegrees
            mid = a_from + span * 0.5
            self.wedge(cx, cy, r_in, r_out, a_from, mid, c)
            self.wedge(cx, cy, r_in, r_out, mid, a_to, c)
            return

        a0, a1 = math.radians(a_from), math.radians(a_to)
        s0, c0 = math.sin(a0), math.cos(a0)
        s1, c1 = math.sin(a1), math.cos(a1)

        row = 2                                # RowPixels
        cy_px = cy * SH
        top = int(math.floor((cy - r_out) * SH))
        bot = int(math.ceil((cy + r_out) * SH))
        top -= (top - int(cy_px)) % row

        for py in range(top, bot, row):
            dy = (cy_px - (py + row * 0.5)) / float(SH)     # up-positive, height fraction
            if abs(dy) >= r_out:
                continue
            outer = math.sqrt(r_out * r_out - dy * dy)
            hole = math.sqrt(max(0.0, r_in * r_in - dy * dy))

            spans = [(-outer, -hole), (hole, outer)] if hole > 0 else [(-outer, outer)]

            # Two half-planes clip the sector: c0*dx - s0*dy >= 0 and c1*dx - s1*dy <= 0.
            for lo, hi in spans:
                lo2, hi2 = lo, hi
                for cc, ss, want_ge in ((c0, s0, True), (c1, s1, False)):
                    t = ss * dy
                    if abs(cc) < 1e-9:
                        if (0.0 >= t) != want_ge and abs(0.0 - t) > 1e-12:
                            lo2, hi2 = 0.0, -1.0
                        continue
                    bound = t / cc
                    ge = want_ge if cc > 0 else (not want_ge)
                    if ge:
                        lo2 = max(lo2, bound)
                    else:
                        hi2 = min(hi2, bound)
                if hi2 - lo2 <= 0:
                    continue
                x0 = (cx + to_x((lo2 + hi2) * 0.5)) * SW
                w = to_x(hi2 - lo2) * SW
                self.d.rectangle([x0 - w * 0.5, py, x0 + w * 0.5, py + row], fill=c)


# ---- icons ------------------------------------------------------------------

_ICON_CACHE = {}


def icon(layer, name, cx, cy, h, colour):
    """Draw.File: a white mask PNG, square, tinted, centred, sized by screen height."""
    path = os.path.join(ICONS, name)
    if not os.path.exists(path):
        return False
    side = max(2, int(round(h * SH)))
    key = (name, side)
    if key not in _ICON_CACHE:
        src = Image.open(path).convert('RGBA').resize((side, side), Image.LANCZOS)
        _ICON_CACHE[key] = src.split()[3]
    mask = _ICON_CACHE[key]
    a = colour[3]
    if a < 255:
        mask = mask.point(lambda v: v * a // 255)
    tint = Image.new('RGBA', (side, side), (colour[0], colour[1], colour[2], 255))
    tint.putalpha(mask)
    layer.alpha_composite(tint, (int(cx * SW - side / 2.0), int(cy * SH - side / 2.0)))
    return True


def text_on(fill):
    luma = (0.2126 * fill[0] + 0.7152 * fill[1] + 0.0722 * fill[2]) / 255.0
    return TEXT_ON_HOVER if luma > 0.55 else TEXT


# ---- the gameplay behind it -------------------------------------------------

def scene(seed):
    """
    Mid-grey, roughly photographic. Sky, a lit road, blocky buildings, a bright shopfront and
    grain -- enough contrast range to prove the HUD survives being over something.
    """
    rnd = random.Random(seed)
    im = Image.new('RGB', (SW, SH))
    d = ImageDraw.Draw(im)

    for y in range(SH):
        t = y / float(SH)
        if t < 0.46:
            k = t / 0.46
            d.line([(0, y), (SW, y)], fill=(int(126 - 34 * k), int(138 - 36 * k), int(152 - 34 * k)))
        else:
            k = (t - 0.46) / 0.54
            d.line([(0, y), (SW, y)], fill=(int(74 - 26 * k), int(72 - 26 * k), int(70 - 24 * k)))

    horizon = int(SH * 0.46)
    x = -60
    while x < SW + 60:
        w = rnd.randint(90, 260)
        h = rnd.randint(120, 400)
        v = rnd.randint(52, 96)
        d.rectangle([x, horizon - h, x + w, horizon + 8], fill=(v, v + 3, v + 8))
        for wy in range(horizon - h + 16, horizon - 12, 26):
            for wx in range(x + 12, x + w - 12, 22):
                if rnd.random() < 0.34:
                    g = rnd.randint(150, 235)
                    d.rectangle([wx, wy, wx + 11, wy + 14], fill=(g, int(g * 0.94), int(g * 0.7)))
        x += w + rnd.randint(4, 26)

    # Road, kerbs, a bright wet patch under a light.
    d.polygon([(SW * 0.18, SH), (SW * 0.44, horizon), (SW * 0.60, horizon), (SW * 0.96, SH)],
              fill=(58, 58, 60))
    d.polygon([(SW * 0.46, SH), (SW * 0.505, horizon), (SW * 0.515, horizon), (SW * 0.55, SH)],
              fill=(126, 122, 96))
    d.ellipse([SW * 0.30, SH * 0.62, SW * 0.78, SH * 1.02], fill=(96, 96, 92))
    d.rectangle([SW * 0.02, SH * 0.50, SW * 0.20, SH * 0.74], fill=(168, 156, 122))
    d.rectangle([SW * 0.80, SH * 0.44, SW * 0.99, SH * 0.66], fill=(38, 40, 46))

    im = im.filter(ImageFilter.GaussianBlur(2.6))
    px = im.load()
    for i in range(150000):
        xx = rnd.randrange(SW)
        yy = rnd.randrange(SH)
        r, g, b = px[xx, yy]
        n = rnd.randint(-16, 16)
        px[xx, yy] = (max(0, min(255, r + n)), max(0, min(255, g + n)), max(0, min(255, b + n)))
    return im.convert('RGBA')


# ---- the wheel --------------------------------------------------------------

def render(page, seed):
    """One whole 1920x1080 game frame with the wheel on it."""
    base = scene(seed)

    dim = Image.new('RGBA', (SW, SH), BACKDROP)
    base.alpha_composite(dim)

    sectors = Shapes()
    hairs = Shapes()
    decor = Shapes()
    art = Image.new('RGBA', (SW, SH), (0, 0, 0, 0))
    ty = Type()

    items = page['items']
    n = len(items)
    step = 360.0 / n
    hovered = page.get('hover', -1)

    cx, cy = 0.5, 0.5

    # ---- sectors. No gap: they meet exactly. -------------------------------
    for i, it in enumerate(items):
        mid = i * step
        a, b = mid - step * 0.5, mid + step * 0.5

        # Disabled is NOT a different sector. The old wheel gave it a lighter fill (44,46,50),
        # which reads as half-highlighted -- the one thing it must not look like. Here the ring
        # stays whole, positions never move, and the whole signal is carried by the CONTENT
        # going to 40% grey. A disabled item you are actually pointing at is the only case that
        # changes the fill, and it goes DARK, not white, so the refusal is obvious before you
        # let go of the button.
        if not it.get('enabled', True):
            fill = (36, 14, 14, 225) if i == hovered else SEG_DISABLED
        elif i == hovered:
            fill = it.get('tint') or SEG_HOVER
        else:
            fill = SEGMENT

        sectors.wedge(cx, cy, INNER, OUTER, a, b, fill)

    # ---- one hairline per boundary. The ONLY separation mechanism. ---------
    if n > 1:
        for i in range(n):
            hairs.spoke(cx, cy, INNER, OUTER, i * step - step * 0.5, HAIRLINE,
                        thick_px=HAIR_LINE_PX)

    base.alpha_composite(sectors.layer)
    base.alpha_composite(hairs.layer)

    # ---- sector contents ---------------------------------------------------
    for i, it in enumerate(items):
        mid = i * step
        rad = math.radians(mid)
        px = cx + to_x(MID * math.sin(rad))
        py = cy - MID * math.cos(rad)

        enabled = it.get('enabled', True)
        if not enabled:
            fg = (150, 152, 156, 115)
            ink = (150, 152, 156, 95)
        elif i == hovered:
            fg = text_on(it.get('tint') or SEG_HOVER)
            ink = fg
        else:
            fg = TEXT
            ink = fg

        icon(art, it['icon'], px, py + ICON_DY, ICON_H, ink)
        ty.put(it['label'].upper(), px, py + LABEL_DY, 0.34, fg, condensed=True)

    # ---- the centre. The whole readout, in the hole the ring leaves. -------
    item = items[hovered] if 0 <= hovered < n else None
    crumb = ('< ' + page['title']) if page.get('nested') else page['title']

    if item is None:
        ty.put(crumb.upper(), cx, cy - 0.040, 0.44, ACCENT)
        ty.put(page.get('subtitle', ''), cx, cy + 0.008, 0.28, TEXT_DIM, condensed=False)
    else:
        enabled = item.get('enabled', True)

        ty.put(crumb.upper(), cx, cy - 0.084, 0.28, (176, 179, 181, 160))
        decor.rule(cx, cy - 0.056, to_x(0.058), RULE, thick=0.0018)

        value = item.get('value', '')
        if value:
            vc = CASH if (enabled and value.startswith('$')) else (TEXT if enabled else TEXT_DISABLED)
            ty.put(ty.fit(value, to_x(0.215), 0.46, True), cx, cy - 0.044, 0.46, vc)
            dy = cy + 0.004
        else:
            dy = cy - 0.026

        detail = item.get('reason', '') if not enabled else item.get('detail', '')
        dc = WARN if not enabled else TEXT_DIM
        for line in ty.wrap(detail, to_x(0.205), 0.26, False, lines=2):
            ty.put(line, cx, dy, 0.26, dc, condensed=False)
            dy += 0.025

    # ---- the footer. Two centred columns, no box, no washes. ---------------
    panel = page.get('panel')
    if panel:
        decor.rule(cx, FOOT_TOP, COL_W * 2 + COL_GUT, RULE)

        for col_i, col in enumerate(panel[:2]):
            left = FOOT_LEFT + col_i * (COL_W + COL_GUT)
            right = left + COL_W
            y = FOOT_TOP + 0.014

            for row in col:
                label, value, tint, art_file = row
                head = value is None

                lx = left
                if art_file and icon(art, art_file, lx + to_x(0.016) * 0.5, y + 0.011,
                                     0.016, TEXT_DIM if not head else (245, 245, 245, 200)):
                    lx += to_x(0.016) + 0.006

                if head:
                    ty.put(label.upper(), lx, y, 0.30, (245, 245, 245, 205), align='l')
                    decor.rect((left + right) * 0.5, y + 0.024, COL_W, 0.0014, RULE_FAINT)
                    y += ROW_H
                    continue

                ty.put(label, lx, y + 0.002, 0.28, TEXT_DIM, condensed=False, align='l')
                taken = (lx - left) + ty.measure(label, 0.28, False)
                room = COL_W - taken - 0.010
                ty.put(ty.fit(value, room, 0.28, False), right, y + 0.002, 0.28,
                       tint or TEXT, condensed=False, align='r')
                y += ROW_H

    base.alpha_composite(decor.layer)
    base.alpha_composite(art)
    base.alpha_composite(ty.layer)
    return base


# ---- content: the real pages ------------------------------------------------
# Root, straight out of WheelPages.BuildRoot(). Dealing, straight out of BuildDrugsPage()
# with HoldingRows() feeding the panel.

ROOT_PAGE = {
    'title': 'Hoodrich',
    'subtitle': 'Unaffiliated',
    'hover': 1,
    'items': [
        {'label': 'Weapons', 'icon': 'guns.png', 'value': 'Micro SMG',
         'detail': "Opens the game's own weapon wheel"},
        {'label': 'Dealing', 'icon': 'bong.png', 'value': '12.4 ready, 118 to prep',
         'detail': 'Re-up, bag up, go to work'},
        {'label': 'Gangs', 'icon': 'mask.png', 'value': 'SOLO',
         'detail': 'Nobody has put you on yet'},
        {'label': 'Inventory', 'icon': 'stash.png', 'value': '3 kinds  -  12.4g',
         'detail': 'Everything you are carrying'},
        {'label': 'Socials', 'icon': 'tattoo.png', 'value': '',
         'detail': 'What the block is saying, and what you say back',
         'enabled': False, 'reason': 'Not right now'},
    ],
}

DEALING_PAGE = {
    'title': 'Dealing',
    'subtitle': '12.4 ready, 118 to prep',
    'nested': True,
    'hover': 0,
    'items': [
        {'label': 'Re-up', 'icon': 'money.png', 'value': '$14,200',
         'detail': 'Ruban is two blocks over and picking up'},
        {'label': 'Post up', 'icon': 'cash.png', 'value': '',
         'detail': 'Stand on a corner and let it come to you',
         'enabled': False, 'reason': 'All you have is weight -- prep it first'},
        {'label': 'The numbers', 'icon': 'health.png', 'value': '',
         'detail': 'Prices, heat, and what this block does to both'},
    ],
    'panel': [
        [('ON YOU', None, None, 'stash.png'),
         ('Weed', '4.2g', CASH, None),
         ('Cocaine', '1.8g  -  40g to prep', CASH, None),
         ('Ecstasy', '12 pills', CASH, None)],
        [('AT THE HOUSE', None, None, 'garage.png'),
         ('Ecstasy', '40 pills  -  60 to press', TEXT, None),
         ('Meth', '118g to prep', TEXT, None),
         ('Heroin', '22.5g', TEXT, None)],
    ],
}


# ---- compose ----------------------------------------------------------------

def window(frame):
    x = (SW - WIN_W) // 2
    return frame.crop((x, 0, x + WIN_W, SH))


def main():
    canvas = Image.new('RGBA', (CANVAS_W, CANVAS_H), (18, 18, 20, 255))

    canvas.paste(window(render(ROOT_PAGE, 7)), (0, 0))
    canvas.paste(window(render(DEALING_PAGE, 21)), (CANVAS_W - WIN_W, 0))

    d = ImageDraw.Draw(canvas)
    d.rectangle([WIN_W, 0, CANVAS_W - WIN_W, CANVAS_H], fill=(18, 18, 20, 255))

    cap = gfont(17, True)
    small = gfont(14, False)

    def caption(x, head, sub):
        d.rectangle([x, 0, x + WIN_W, 40], fill=(0, 0, 0, 205))
        d.text((x + 14, 7), head, font=cap, fill=(236, 236, 236, 255))
        d.text((x + 14, 23), sub, font=small, fill=(150, 152, 156, 255))

    caption(0, 'ROOT  -  5 items  -  DEALING hovered, SOCIALS disabled',
            'centre 950 px of a 1920x1080 frame, 1:1')
    caption(CANVAS_W - WIN_W, 'DEALING  -  3 items + 8 stat rows  -  RE-UP hovered, POST UP disabled',
            'centre 950 px of a 1920x1080 frame, 1:1')

    d.rectangle([0, CANVAS_H - 44, CANVAS_W, CANVAS_H], fill=(0, 0, 0, 215))
    d.text((16, CANVAS_H - 36),
           'FLAT SECTOR  -  sectors touch, one 2 px hairline divides them, hover is a '
           'different FILL not a bigger shape.',
           font=cap, fill=(236, 236, 236, 255))
    d.text((16, CANVAS_H - 19),
           'REMOVED: 3 deg gaps  /  3% hover reach  /  hub disc + 0.96 inset  /  top-of-screen '
           'readout  /  right-hand panel box, header strip and row washes  /  the label said '
           'twice  /  two type sizes.   RING 0.125 - 0.212 (was 0.085 - 0.200).',
           font=small, fill=(150, 152, 156, 255))

    canvas.convert('RGB').save(OUT, 'PNG')
    print(OUT, os.path.getsize(OUT), 'bytes')


if __name__ == '__main__':
    main()
