# -*- coding: utf-8 -*-
#
# The drawing kit every icon in the mod is made with.
#
# WHITE ON TRANSPARENT, TINTED AT DRAW TIME. CustomSprite multiplies the texture by the colour
# it is drawn in, so one file is the dim mark on a header, the bright one on a dark tile and
# the near-black one on a lit plate. That rules out colour in the file -- but not TONE.
#
# THREE TONES, WHICH IS WHAT MAKES THESE READ AS DRAWINGS RATHER THAN STENCILS. Alpha is the
# only channel we have and it survives the tint: a pixel at 150 comes out as a darker version
# of whatever the icon is drawn in. So a body is drawn at full alpha, the planes that sit
# behind or below it at MID, and the fine grooves and textures at LOW -- and a hole punched to
# CLEAR shows the panel through. ImageDraw does not blend on an RGBA canvas, it OVERWRITES, so
# whatever is drawn last owns its pixels: draw the body first and the detail on top.
#
# 128 PIXELS OUT, drawn at 512 and downsampled four times. The old set was 64, and the biggest
# places these are drawn -- the phone's apps, the pocket tiles, an avatar -- are sixty to
# seventy pixels on a 1440 screen, so every one of them was being stretched. Twice the pixels
# is the single largest quality gain available and it costs nothing at draw time.
#
# COORDINATES ARE IN THE 512 SPACE throughout. Everything here takes them that way.

import math
import os

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(os.path.dirname(HERE), "data", "icons")
FONTS = os.path.join(HERE, "fonts")

S = 512          # working size
F = 4            # downsample -> 128px files

W = (255, 255, 255, 255)      # the body
MID = (255, 255, 255, 150)    # a plane behind or below the body
LOW = (255, 255, 255, 88)     # grooves, texture, the faintest marks
CLEAR = (0, 0, 0, 0)          # a hole: the panel shows through

WRITTEN = []


def canvas(w=S, h=S):
    img = Image.new("RGBA", (w, h), CLEAR)
    return img, ImageDraw.Draw(img)


def save(img, name, out_w=None, out_h=None):
    """Downsample by F (or to the size given) and write."""
    if not os.path.isdir(OUT):
        os.makedirs(OUT)

    tw = out_w or img.width // F
    th = out_h or img.height // F

    small = img.resize((tw, th), Image.LANCZOS)
    p = os.path.join(OUT, name)
    small.save(p, optimize=True)

    WRITTEN.append(name)
    print("  %-22s %dx%d  %d bytes" % (name, small.width, small.height, os.path.getsize(p)))


# ---------------------------------------------------------------- geometry

def rot(pts, cx, cy, deg):
    """Points turned about a centre."""
    a = math.radians(deg)
    c, s = math.cos(a), math.sin(a)
    out = []
    for x, y in pts:
        dx, dy = x - cx, y - cy
        out.append((cx + dx * c - dy * s, cy + dx * s + dy * c))
    return out


def shift(pts, dx, dy):
    return [(x + dx, y + dy) for x, y in pts]


def scale(pts, cx, cy, k, ky=None):
    ky = k if ky is None else ky
    return [(cx + (x - cx) * k, cy + (y - cy) * ky) for x, y in pts]


def circle(cx, cy, r, n=72, start=0.0):
    return [(cx + r * math.cos(math.radians(start + 360.0 * i / n)),
             cy + r * math.sin(math.radians(start + 360.0 * i / n))) for i in range(n)]


def arc(cx, cy, r, a0, a1, n=32):
    """Points along an arc from a0 to a1 degrees (clockwise on screen when a1 > a0)."""
    return [(cx + r * math.cos(math.radians(a0 + (a1 - a0) * i / n)),
             cy + r * math.sin(math.radians(a0 + (a1 - a0) * i / n))) for i in range(n + 1)]


def star(cx, cy, r_out, r_in, n=5, start=-90.0):
    pts = []
    for i in range(n * 2):
        r = r_out if i % 2 == 0 else r_in
        a = math.radians(start + 180.0 * i / n)
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def bez(p0, p1, p2, n=32):
    """Quadratic Bezier."""
    out = []
    for i in range(n + 1):
        t = i / float(n)
        u = 1 - t
        out.append((u * u * p0[0] + 2 * u * t * p1[0] + t * t * p2[0],
                    u * u * p0[1] + 2 * u * t * p1[1] + t * t * p2[1]))
    return out


def cbez(p0, p1, p2, p3, n=40):
    """Cubic Bezier."""
    out = []
    for i in range(n + 1):
        t = i / float(n)
        u = 1 - t
        out.append((u ** 3 * p0[0] + 3 * u * u * t * p1[0] + 3 * u * t * t * p2[0] + t ** 3 * p3[0],
                    u ** 3 * p0[1] + 3 * u * u * t * p1[1] + 3 * u * t * t * p2[1] + t ** 3 * p3[1]))
    return out


def rrect_pts(x0, y0, x1, y1, r, n=10):
    """A rounded rectangle as a polygon, so it can be rotated or stroked."""
    pts = []
    pts += arc(x1 - r, y0 + r, r, -90, 0, n)
    pts += arc(x1 - r, y1 - r, r, 0, 90, n)
    pts += arc(x0 + r, y1 - r, r, 90, 180, n)
    pts += arc(x0 + r, y0 + r, r, 180, 270, n)
    return pts


# ---------------------------------------------------------------- marks

def poly(d, pts, fill=W):
    d.polygon([(float(x), float(y)) for x, y in pts], fill=fill)


def disc(d, cx, cy, r, fill=W):
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=fill)


def ellipse(d, cx, cy, rx, ry, fill=W):
    d.ellipse([cx - rx, cy - ry, cx + rx, cy + ry], fill=fill)


def rrect(d, x0, y0, x1, y1, r, fill=W):
    d.rounded_rectangle([x0, y0, x1, y1], radius=r, fill=fill)


def rect(d, x0, y0, x1, y1, fill=W):
    d.rectangle([x0, y0, x1, y1], fill=fill)


def stroke(d, pts, width, fill=W, closed=False, caps=True):
    """
    A line of even thickness along a polyline, with round joins.

    PIL's own line() mitres a thick join with a notch, and at these widths the notch is a
    visible bite. Each segment is laid down as a quad and each joint gets a disc, which is a
    round join by construction.
    """
    pts = [(float(x), float(y)) for x, y in pts]
    if closed and pts[0] != pts[-1]:
        pts = pts + [pts[0]]

    half = width * 0.5

    for i in range(len(pts) - 1):
        (x0, y0), (x1, y1) = pts[i], pts[i + 1]
        dx, dy = x1 - x0, y1 - y0
        n = math.hypot(dx, dy) or 1.0
        nx, ny = -dy / n * half, dx / n * half
        d.polygon([(x0 + nx, y0 + ny), (x1 + nx, y1 + ny), (x1 - nx, y1 - ny), (x0 - nx, y0 - ny)],
                  fill=fill)

    joints = pts if closed else pts[1:-1]
    for x, y in joints:
        disc(d, x, y, half, fill)

    if caps and not closed:
        disc(d, pts[0][0], pts[0][1], half, fill)
        disc(d, pts[-1][0], pts[-1][1], half, fill)


def ring(d, cx, cy, r, width, fill=W, n=96):
    """An annulus that does not punch through whatever is under it."""
    stroke(d, circle(cx, cy, r, n), width, fill, closed=True)


def ribbon(d, spine, w0, w1, fill=W):
    """A strip whose thickness runs from w0 to w1 along a centreline."""
    left, right = [], []

    for i, (x, y) in enumerate(spine):
        t = i / float(len(spine) - 1)
        half = (w0 + (w1 - w0) * t) * 0.5

        if i == 0:
            dx, dy = spine[1][0] - x, spine[1][1] - y
        elif i == len(spine) - 1:
            dx, dy = x - spine[-2][0], y - spine[-2][1]
        else:
            dx, dy = spine[i + 1][0] - spine[i - 1][0], spine[i + 1][1] - spine[i - 1][1]

        n = math.hypot(dx, dy) or 1.0
        nx, ny = -dy / n, dx / n
        left.append((x + nx * half, y + ny * half))
        right.append((x - nx * half, y - ny * half))

    d.polygon(left + right[::-1], fill=fill)


def glyph(d, text, cx, cy, size, font_file, fill=W, tracking=0):
    """A word set in one of the bundled faces, centred on a point."""
    font = ImageFont.truetype(os.path.join(FONTS, font_file), size)
    box = d.textbbox((0, 0), text, font=font)
    w = box[2] - box[0]
    h = box[3] - box[1]
    d.text((cx - w / 2.0 - box[0], cy - h / 2.0 - box[1]), text, font=font, fill=fill)


def shade_band(d, x0, y0, x1, y1, fill=MID, slant=0):
    """A parallelogram wash, for the plane of a box that is turned away from the light."""
    d.polygon([(x0 + slant, y0), (x1 + slant, y0), (x1, y1), (x0, y1)], fill=fill)
