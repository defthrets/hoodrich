# -*- coding: utf-8 -*-
#
# A line-for-line port of the SHIPPED Draw.Wedge / Draw.Disc and RadialMenu.Render, so the
# thing on screen can be reproduced offline and taken apart.
#
# The point is not to draw something nice. It is to draw exactly what the game draws, at the
# same aspect and the same resolution, and see whether the defect reproduces. If it does, it
# is in this arithmetic. If it does not, the difference is environmental -- resolution probe,
# draw order, or something the game is doing to the calls.

import math
import os
import sys
from PIL import Image, ImageDraw

ROOT = r'C:\projects\hoodrich'

# Straight out of RadialMenu.cs
R_IN, R_OUT = 0.124, 0.252
GAP_DEG = 4.0
KEEL = 0.016
FRAME_BAND = 0.0055
SEGMENT_A = 235

SEGMENT = (10, 12, 14, SEGMENT_A)
SEGMENT_HOVER = (240, 242, 240, 240)
WARN = (232, 177, 44, 255)
HUB = (8, 9, 11, 225)
BACKDROP = (0, 0, 0, 140)

ROW_PIXELS = 2
MAX_SPAN = 170.0


class Surface(object):
    def __init__(self, w, h):
        self.W, self.H = w, h
        self.ASPECT = w / float(h)
        self.img = Image.new('RGBA', (w, h), (0, 0, 0, 0))
        self.rects = 0

    def to_x(self, v):
        return v / self.ASPECT

    def rect(self, cx, cy, w, h, c):
        """DRAW_RECT: centre-anchored, all four values 0..1 of the screen."""
        if w <= 0 or h <= 0 or c[3] <= 0:
            return
        x0 = (cx - w * 0.5) * self.W
        y0 = (cy - h * 0.5) * self.H
        x1 = (cx + w * 0.5) * self.W
        y1 = (cy + h * 0.5) * self.H
        lay = Image.new('RGBA', (self.W, self.H), (0, 0, 0, 0))
        ImageDraw.Draw(lay).rectangle([x0, y0, x1, y1], fill=c)
        self.img.alpha_composite(lay)
        self.rects += 1


def crosses(a, b, bearing):
    span = b - a
    at = ((bearing - a) % 360.0 + 360.0) % 360.0
    return at <= span


def reach_top(rad, r_in, r_out):
    c = math.cos(rad)
    return r_out * c if c > 0 else r_in * c


def reach_bottom(rad, r_in, r_out):
    c = math.cos(rad)
    return r_out * c if c < 0 else r_in * c


def clip_half_plane(dx, dy_dir, dy, is_start, bounds):
    if abs(dy_dir) < 1e-6:
        side = -dx * dy if is_start else dx * dy
        return side >= 0.0

    bound = dx * dy / dy_dir
    lower = (dy_dir > 0.0) if is_start else (dy_dir < 0.0)

    if lower:
        if bound > bounds[0]:
            bounds[0] = bound
    else:
        if bound < bounds[1]:
            bounds[1] = bound

    return bounds[0] < bounds[1]


def wedge(s, cx, cy, r_in, r_out, a_from, a_to, c):
    if r_out <= r_in or c[3] <= 0:
        return

    span = a_to - a_from
    if span <= 0:
        return

    if span > MAX_SPAN:
        mid = a_from + span * 0.5
        wedge(s, cx, cy, r_in, r_out, a_from, mid, c)
        wedge(s, cx, cy, r_in, r_out, mid, a_to, c)
        return

    a0, a1 = math.radians(a_from), math.radians(a_to)
    d0x, d0y = math.sin(a0), math.cos(a0)
    d1x, d1y = math.sin(a1), math.cos(a1)

    r_out2, r_in2 = r_out * r_out, r_in * r_in

    top_dy = r_out if crosses(a_from, a_to, 0.0) else max(
        reach_top(a0, r_in, r_out), reach_top(a1, r_in, r_out))
    bot_dy = -r_out if crosses(a_from, a_to, 180.0) else min(
        reach_bottom(a0, r_in, r_out), reach_bottom(a1, r_in, r_out))

    px_top = int(math.floor((cy - top_dy) * s.H))
    px_bottom = int(math.ceil((cy - bot_dy) * s.H))
    px_centre = int(round(cy * s.H))
    px_top -= ((px_top - px_centre) % ROW_PIXELS + ROW_PIXELS) % ROW_PIXELS

    row_h = ROW_PIXELS / float(s.H)

    py = px_top
    while py < px_bottom:
        row_y = (py + ROW_PIXELS * 0.5) / float(s.H)
        dy = cy - row_y
        dy2 = dy * dy
        py += ROW_PIXELS

        if dy2 > r_out2:
            continue

        hi = math.sqrt(r_out2 - dy2)
        lo = math.sqrt(r_in2 - dy2) if dy2 < r_in2 else 0.0

        b = [-hi, hi]
        if not clip_half_plane(d0x, d0y, dy, True, b):
            continue
        if not clip_half_plane(d1x, d1y, dy, False, b):
            continue

        lo_b, hi_b = b
        if lo <= 0.0:
            emit(s, cx, row_y, row_h, lo_b, hi_b, c)
        else:
            emit(s, cx, row_y, row_h, lo_b, min(hi_b, -lo), c)
            emit(s, cx, row_y, row_h, max(lo_b, lo), hi_b, c)


def emit(s, cx, row_y, row_h, x0, x1, c):
    if x1 <= x0:
        return
    s.rect(cx + s.to_x((x0 + x1) * 0.5), row_y, s.to_x(x1 - x0), row_h, c)


def disc(s, cx, cy, r, c):
    py = int(round((cy - r) * s.H))
    stop = int(round((cy + r) * s.H))
    row_h = ROW_PIXELS / float(s.H)
    while py < stop:
        row_y = (py + ROW_PIXELS * 0.5) / float(s.H)
        dy = cy - row_y
        py += ROW_PIXELS
        if abs(dy) > r:
            continue
        half = math.sqrt(r * r - dy * dy)
        s.rect(cx, row_y, s.to_x(half * 2), row_h, c)


def empty_slot(s, cx, cy, r_in, r_out, a0, a1, t):
    interior = (0, 0, 0, int(155 * t))
    edge = (84, 88, 96, int(195 * t))
    wedge(s, cx, cy, r_in, r_out, a0, a1, interior)
    wedge(s, cx, cy, r_out - FRAME_BAND, r_out, a0, a1, edge)
    wedge(s, cx, cy, r_in, r_in + FRAME_BAND, a0, a1, edge)
    r_mid = (r_in + r_out) * 0.5
    slice_deg = math.degrees(FRAME_BAND / r_mid)
    wedge(s, cx, cy, r_in, r_out, a0, a0 + slice_deg, edge)
    wedge(s, cx, cy, r_in, r_out, a1 - slice_deg, a1, edge)


def render(w, h, items, hovered, t=1.0):
    s = Surface(w, h)
    cx = cy = 0.5

    s.rect(0.5, 0.5, 1.0, 1.0, BACKDROP)

    n = len(items)
    step = 360.0 / n
    gap = GAP_DEG if n > 1 else 0.0

    for i, (label, enabled) in enumerate(items):
        mid = i * step
        a0 = mid - step * 0.5 + gap * 0.5
        a1 = mid + step * 0.5 - gap * 0.5

        if not enabled:
            empty_slot(s, cx, cy, R_IN, R_OUT, a0, a1, t)
            continue

        hov = (i == hovered)
        fill = SEGMENT_HOVER if hov else SEGMENT
        fill = (fill[0], fill[1], fill[2], int(fill[3] * t))

        wedge(s, cx, cy, R_IN, R_OUT, a0, a1, fill)

        if hov:
            wedge(s, cx, cy, R_IN, R_IN + KEEL, a0, a1,
                  (WARN[0], WARN[1], WARN[2], int(255 * t)))

    disc(s, cx, cy, R_IN, (255, 255, 255, int(120 * t)))
    disc(s, cx, cy, R_IN - 0.0028, (HUB[0], HUB[1], HUB[2], int(HUB[3] * t)))

    return s


ITEMS = [('Weapons', True), ('Dealing', True), ('Gangs', True),
         ('Inventory', True), ('Socials', True)]

if __name__ == '__main__':
    modes = [(1920, 1080, 'FIXED rows=2 (what shipped)'),
             (1920, 1080, 'ADAPTIVE rows=4'),
             (3440, 1440, 'ADAPTIVE rows=5')]

    out = Image.new('RGB', (760, 460 * len(modes)), (28, 30, 32))
    d = ImageDraw.Draw(out)

    for k, (w, h, name) in enumerate(modes):
        globals()['ROW_PIXELS'] = 2 if k == 0 else max(2, int(round(h / 270.0)))
        s = render(w, h, ITEMS, hovered=4)
        # 1:1 crop of the ring itself, so the row stepping is visible rather than resized away.
        cw, ch = 720, 420
        crop = s.img.convert('RGB').crop((w // 2 - cw // 2, h // 2 - ch // 2,
                                          w // 2 + cw // 2, h // 2 + ch // 2))
        out.paste(crop, (20, 34 + k * 460))
        d.text((24, 12 + k * 460), '%s   %dx%d   %d rects' % (name, w, h, s.rects), fill=(255, 255, 0))
        print('%-10s %dx%d  aspect %.3f  %d rects' % (name, w, h, w / float(h), s.rects))

    p = os.path.join(ROOT, 'preview', 'wheel_repro.png')
    out.save(p)
    print('wrote ' + p)
