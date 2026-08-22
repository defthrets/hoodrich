# -*- coding: utf-8 -*-
#
# The wheel exactly as it draws today, so a redesign has something to be measured against.
#
# Every number here is read out of RadialMenu.cs and Settings.cs rather than eyeballed:
# InnerRadius 0.085, OuterRadius 0.20, SegmentGapDegrees 3, HoverReach 1.03, the hub inset of
# 0.96, the icon at py-0.030 and the label at py+0.010, the readout at y 0.085, and the panel
# at 0.5 + OuterRadius/aspect + 0.045 with width 0.235 and rows 0.032 tall.
#
# Units: everything in the mod is a fraction of screen HEIGHT, and Draw.ToX divides by aspect
# before it becomes a width fraction. So a radius r is r*1080 pixels in BOTH axes at 1920x1080,
# which is why the ring is round. Panel widths are already width fractions and are not divided.

import os
import random
from PIL import Image, ImageDraw, ImageFont, ImageFilter

W, H = 1920, 1080
ASPECT = W / float(H)
ROOT = r'C:\projects\hoodrich'
OUT = os.path.join(ROOT, 'preview', 'wheel_current.png')

# Palette.cs, as ARGB.
BACKDROP = (0, 0, 0, 140)
SEGMENT = (10, 12, 14, 200)
SEG_HOVER = (240, 242, 240, 240)
SEG_DISABLED = (44, 46, 50, 200)
HUB = (8, 9, 11, 225)
PANEL_HEADER = (22, 24, 26, 235)
PANEL_ROW_ALT = (255, 255, 255, 26)
TEXT = (255, 255, 255, 245)
TEXT_DIM = (176, 179, 181, 190)
TEXT_ON_HOVER = (16, 18, 20, 255)
TEXT_DISABLED = (150, 152, 156, 255)
ACCENT = (245, 245, 245, 255)
CASH = (126, 190, 79, 255)

INNER, OUTER = 0.085, 0.20
GAP_DEG = 3.0
HOVER_REACH = 1.03


def font(px, bold=False):
    for f in ([r'C:\Windows\Fonts\segoeuib.ttf', r'C:\Windows\Fonts\arialbd.ttf'] if bold
              else [r'C:\Windows\Fonts\segoeui.ttf', r'C:\Windows\Fonts\arial.ttf']):
        try:
            return ImageFont.truetype(f, px)
        except Exception:
            pass
    return ImageFont.load_default()


def px_from_scale(s):
    """GTA text scale to pixels. 0.34 lands near 18px at 1080p, 0.90 near 47px."""
    return max(9, int(round(s * 52)))


def gameplay_bg():
    """
    Something to composite over.

    A HUD judged on flat black is not judged at all -- the backdrop is only 140 alpha, so
    whatever is behind it comes through every panel and every wedge on this screen.
    """
    random.seed(11)
    img = Image.new('RGB', (W, H), (86, 92, 84))
    d = ImageDraw.Draw(img)

    for y in range(0, H, 4):
        k = y / float(H)
        d.rectangle([0, y, W, y + 4], fill=(int(120 - 70 * k), int(128 - 72 * k), int(118 - 66 * k)))

    # A road, a kerb, a couple of buildings -- enough to have light and dark under the HUD.
    d.polygon([(0, H), (W, H), (W * 0.72, H * 0.52), (W * 0.28, H * 0.52)], fill=(58, 60, 62))
    d.polygon([(0, H * 0.55), (W * 0.30, H * 0.52), (W * 0.30, H), (0, H)], fill=(96, 92, 84))
    d.rectangle([W * 0.06, H * 0.10, W * 0.26, H * 0.55], fill=(112, 104, 92))
    d.rectangle([W * 0.74, H * 0.14, W * 0.95, H * 0.55], fill=(126, 118, 104))

    for i in range(9):
        x = W * 0.5 + (i - 4) * 40
        d.rectangle([x - 5, H * 0.62 + i * 34, x + 5, H * 0.62 + i * 34 + 22], fill=(210, 208, 190))

    for _ in range(26000):
        x, y = random.randrange(W), random.randrange(H)
        r, g, b = img.getpixel((x, y))
        n = random.randint(-14, 14)
        img.putpixel((x, y), (max(0, min(255, r + n)), max(0, min(255, g + n)), max(0, min(255, b + n))))

    return img.filter(ImageFilter.GaussianBlur(0.6)).convert('RGBA')


base = gameplay_bg()
layer = Image.new('RGBA', (W, H), (0, 0, 0, 0))
d = ImageDraw.Draw(layer)

CX, CY = W * 0.5, H * 0.5
d.rectangle([0, 0, W, H], fill=BACKDROP)


def wedge(cx, cy, r0, r1, a0, a1, fill):
    """Clockwise from screen-up, the way UpdateSelection measures it."""
    box0 = [cx - r0, cy - r0, cx + r0, cy + r0]
    box1 = [cx - r1, cy - r1, cx + r1, cy + r1]
    # PIL angles run clockwise from 3 o'clock; the wheel runs clockwise from 12.
    s, e = a0 - 90, a1 - 90
    ring = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    rd = ImageDraw.Draw(ring)
    rd.pieslice(box1, s, e, fill=fill)
    rd.pieslice(box0, s - 2, e + 2, fill=(0, 0, 0, 0))
    # pieslice cannot punch a hole, so the inner disc is cut on the alpha channel.
    hole = Image.new('L', (W, H), 255)
    ImageDraw.Draw(hole).ellipse(box0, fill=0)
    ring.putalpha(Image.composite(ring.split()[3], Image.new('L', (W, H), 0), hole))
    layer.alpha_composite(ring)


def art(name, cx, cy, size_h, tint):
    p = os.path.join(ROOT, 'data', 'icons', name)
    if not os.path.exists(p):
        return
    n = int(size_h * H)
    im = Image.open(p).convert('RGBA').resize((n, n), Image.LANCZOS)
    solid = Image.new('RGBA', im.size, tint[:3] + (0,))
    solid.putalpha(im.split()[3].point(lambda a: a * tint[3] // 255))
    layer.alpha_composite(solid, (int(cx - n / 2), int(cy - n / 2)))


def text(s, x, y, scale, colour, centre=True, bold=False, right=False):
    f = font(px_from_scale(scale), bold)
    w = d.textlength(s, font=f)
    if centre:
        x -= w / 2
    if right:
        x -= w
    d.text((x, y), s, font=f, fill=colour)


ITEMS = [
    ('Weapons', 'guns.png', True, False),
    ('Dealing', 'bong.png', True, True),      # hovered
    ('Gangs', 'mask.png', True, False),
    ('Inventory', 'stash.png', True, False),
    ('Socials', 'tattoo.png', False, False),  # disabled
]

n = len(ITEMS)
step = 360.0 / n
r_in, r_out = INNER * H, OUTER * H
r_mid = (INNER + OUTER) * 0.5 * H

for i, (label, icon, enabled, hovered) in enumerate(ITEMS):
    mid = i * step
    a0 = mid - step * 0.5 + GAP_DEG * 0.5
    a1 = mid + step * 0.5 - GAP_DEG * 0.5

    fill = SEG_HOVER if hovered else (SEGMENT if enabled else SEG_DISABLED)
    wedge(CX, CY, r_in, r_out * (HOVER_REACH if hovered else 1.0), a0, a1, fill)

import math
for i, (label, icon, enabled, hovered) in enumerate(ITEMS):
    mid = i * step
    rad = math.radians(mid)
    px = CX + r_mid * math.sin(rad)
    py = CY - r_mid * math.cos(rad)

    ink = TEXT_DISABLED if not enabled else (TEXT_ON_HOVER if hovered else TEXT)
    art(icon, px, py - 0.030 * H, 0.056, ink)
    text(label.upper(), px, py + 0.010 * H, 0.34, ink, bold=True)

# Hub: a disc at 96% of the inner radius, with only the breadcrumb in it.
d.ellipse([CX - r_in * 0.96, CY - r_in * 0.96, CX + r_in * 0.96, CY + r_in * 0.96], fill=HUB)
text('HOODRICH', CX, CY - 0.012 * H, 0.34, ACCENT, bold=True)

# Readout, across the top of the screen at 0.085.
top = 0.085 * H
text('DEALING', CX, top - 0.006 * H, 0.90, TEXT)
text('Buy weight, bag it up, and go and stand somewhere', CX, top + 0.044 * H, 0.32, TEXT_DIM)

# Panel, to the right of the ring.
p_left = (0.5 + OUTER / ASPECT + 0.045) * W
p_w = 0.235 * W
row_h = 0.032 * H
pad = 0.018 * H
ROWS = [('Cash', '$14,250'), ('On you', '184g'), ('At the house', '2.4kg'),
        ('Free space', '116g'), ('Rank', 'Corner boy'), ('Running with', 'The Families')]
p_h = pad * 2 + row_h * (len(ROWS) + 1)
p_top = CY - p_h / 2

d.rectangle([p_left, p_top, p_left + p_w, p_top + p_h], fill=(8, 9, 11, 225))
d.rectangle([p_left, p_top, p_left + p_w, p_top + row_h], fill=PANEL_HEADER)
d.rectangle([p_left, p_top + row_h - 2, p_left + p_w, p_top + row_h + 1], fill=ACCENT)
text('WHAT YOU HAVE', p_left + pad * 0.5, p_top + pad * 0.6, 0.30, ACCENT, centre=False, bold=True)

y = p_top + pad * 0.6 + row_h
for i, (lab, val) in enumerate(ROWS):
    if i & 1:
        d.rectangle([p_left, y + row_h * 0.34 - row_h / 2, p_left + p_w, y + row_h * 0.34 + row_h / 2],
                    fill=PANEL_ROW_ALT)
    text(lab, p_left + pad * 0.5, y, 0.28, TEXT_DIM, centre=False)
    text(val, p_left + p_w - pad * 0.5, y, 0.28, CASH if '$' in val else TEXT, centre=False, right=True)
    y += row_h

base.alpha_composite(layer)

f = font(20, True)
dd = ImageDraw.Draw(base)
dd.rectangle([0, 0, 560, 44], fill=(0, 0, 0, 190))
dd.text((16, 11), 'CURRENT  -  as RadialMenu.cs draws it today', font=f, fill=(255, 255, 255, 255))

base.convert('RGB').save(OUT)
print('wrote %s' % OUT)
