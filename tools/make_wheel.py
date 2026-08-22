# -*- coding: utf-8 -*-
#
# The wheel as artwork instead of as a stack of rectangles.
#
# Everything the wheel draws is built out of DRAW_RECT, because that is the only filled shape
# the game gives a script. A circle made of rectangles is a staircase, and the only way to make
# the steps smaller is more rectangles -- which is what put the wheel over its per-frame draw
# budget and made the last wedge vanish. Measured: genuinely smooth rows cost 954 rectangles at
# 1080p and 1,382 at 1440p, against roughly 593 for the wheel this replaced.
#
# So it stops being rectangles. CustomSprite draws a PNG off disk, rotated and tinted, in ONE
# call -- the icons already go through it. A wedge drawn once here at 1024 with real
# anti-aliasing, then rotated into each of its positions, is a smooth wheel for about eight
# draw calls instead of five hundred.
#
# One set per item count, because a wedge for a five-item wheel is a different shape from one
# for eight. Every sprite is a white mask; the colour goes on at draw time, which is how the
# same file is a dark wedge, a near-white hovered one, or a gang's own colour.

import math
import os

from PIL import Image, ImageDraw

OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'data', 'icons')

# Matched to RadialMenu.cs. The canvas is the full ring, so the sprite can be drawn centred on
# the wheel and rotated about its own middle.
R_IN = 0.124
R_OUT = 0.252
GAP_DEG = 4.0
KEEL = 0.016
FRAME = 0.0055

SIDE = 1024           # a little over 1:1 at 4K, where the ring is 1089 pixels across
SS = 4                # supersample, then downsample -- this is where the smooth comes from

W = (255, 255, 255, 255)
CLEAR = (0, 0, 0, 0)


def canvas():
    img = Image.new('RGBA', (SIDE * SS, SIDE * SS), CLEAR)
    return img, ImageDraw.Draw(img)


def save(img, name):
    small = img.resize((SIDE, SIDE), Image.LANCZOS)
    p = os.path.join(OUT, name)
    small.save(p, optimize=True)
    print('  %-22s %dx%d  %d KB' % (name, small.width, small.height,
                                    os.path.getsize(p) // 1024))


def ring(d, r0, r1, a0, a1, fill):
    """
    An annular sector, in the wheel's own angles: clockwise, zero pointing up.

    PIL measures clockwise from three o'clock, so every angle turns by ninety. Drawn as a
    pieslice with the middle punched back out, which is the same shape Draw.Wedge builds out
    of rectangles -- just with the edges resolved properly.
    """
    c = SIDE * SS * 0.5
    ro = r1 / (R_OUT * 2.0) * SIDE * SS
    ri = r0 / (R_OUT * 2.0) * SIDE * SS

    d.pieslice([c - ro, c - ro, c + ro, c + ro], a0 - 90, a1 - 90, fill=fill)
    d.ellipse([c - ri, c - ri, c + ri, c + ri], fill=CLEAR)


def segment(n):
    """One filled wedge of an n-item wheel, pointing straight up."""
    img, d = canvas()
    half = 360.0 / n * 0.5 - GAP_DEG * 0.5

    ring(d, R_IN, R_OUT, -half, half, W)
    save(img, 'wheel_seg_%d.png' % n)


def keel(n):
    """The amber bar along the wedge's inner edge."""
    img, d = canvas()
    half = 360.0 / n * 0.5 - GAP_DEG * 0.5

    ring(d, R_IN, R_IN + KEEL, -half, half, W)
    save(img, 'wheel_keel_%d.png' % n)


def slot(n):
    """
    An outlined empty wedge: what a thing you cannot pick looks like.

    Drawn as the outer shape with a smaller one cut out of it, which leaves a band of even
    thickness all the way round -- including down the two straight edges, where a stack of
    rectangles could only ever manage a staircase.
    """
    img, d = canvas()
    half = 360.0 / n * 0.5 - GAP_DEG * 0.5

    # The frame, in degrees, is the same arc length at the mid radius as it is in depth.
    inset = math.degrees(FRAME / ((R_IN + R_OUT) * 0.5))

    ring(d, R_IN, R_OUT, -half, half, W)
    ring(d, R_IN + FRAME, R_OUT - FRAME, -half + inset, half - inset, CLEAR)

    save(img, 'wheel_slot_%d.png' % n)


def disc():
    """A circle, for the hub. One sprite, one draw, no staircase."""
    img, d = canvas()
    c = SIDE * SS * 0.5
    d.ellipse([0, 0, SIDE * SS - 1, SIDE * SS - 1], fill=W)
    save(img, 'wheel_disc.png')


if not os.path.isdir(OUT):
    os.makedirs(OUT)

disc()

for n in range(2, 9):
    segment(n)
    keel(n)
    slot(n)
