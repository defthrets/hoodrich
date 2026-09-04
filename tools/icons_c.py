# -*- coding: utf-8 -*-
#
# Group C: the sets' marks. Emblems, so each is one strong shape with a second tone worked
# into it -- these are drawn small beside names and large on the war banner, and have to hold
# at both.

import math

from iconkit import *


def gang_families():
    img, d = canvas()

    # Three bars, each with its lower half turned from the light.
    for y in (108, 224, 340):
        rrect(d, 72, y, 440, y + 68, 22)
        rect(d, 90, y + 40, 422, y + 68, MID)

    save(img, "gang_families.png")


def gang_ballas():
    img, d = canvas()

    outer = [(256, 30), (482, 256), (256, 482), (30, 256)]
    poly(d, outer)
    poly(d, scale(outer, 256, 256, 0.64), CLEAR)
    poly(d, scale(outer, 256, 256, 0.50), MID)
    poly(d, scale(outer, 256, 256, 0.26))

    save(img, "gang_ballas.png")


def gang_vagos():
    img, d = canvas()

    for i in range(12):
        a = math.radians(i * 30)
        tip = (256 + 236 * math.cos(a), 256 + 236 * math.sin(a))
        l = (256 + 150 * math.cos(a - 0.16), 256 + 150 * math.sin(a - 0.16))
        r = (256 + 150 * math.cos(a + 0.16), 256 + 150 * math.sin(a + 0.16))
        poly(d, [l, tip, r])

    disc(d, 256, 256, 124)
    ring(d, 256, 256, 92, 14, MID)
    disc(d, 256, 256, 48, MID)

    save(img, "gang_vagos.png")


def gang_aztecas():
    img, d = canvas()

    steps = [(60, 452, 452, 400), (100, 400, 412, 336), (140, 336, 372, 272), (180, 272, 332, 208), (220, 208, 292, 150)]

    for x0, y1, x1, y0 in steps:
        rect(d, x0, y0, x1, y1)
        rect(d, (x0 + x1) // 2, y0, x1, y1, MID)

    # The stair up the middle, and the doorway at the top.
    rect(d, 236, 160, 276, 452, LOW)
    rrect(d, 240, 100, 272, 150, 8)
    rrect(d, 246, 116, 266, 150, 4, CLEAR)

    save(img, "gang_aztecas.png")


def gang_marabunta():
    img, d = canvas()

    pts = [(256, 130), (140, 342), (372, 342)]

    stroke(d, pts, 12, LOW, closed=True)

    for cx, cy in pts:
        disc(d, cx, cy, 78)
        ring(d, cx, cy, 50, 12, MID)
        disc(d, cx, cy, 20, MID)

    save(img, "gang_marabunta.png")


def gang_lost():
    img, d = canvas()

    for y in (96, 212, 328):
        stroke(d, [(90, y), (256, y + 120), (422, y)], 58)
        stroke(d, [(124, y + 4), (256, y + 100), (388, y + 4)], 10, MID)

    save(img, "gang_lost.png")


def gang_triads():
    img, d = canvas()

    rrect(d, 84, 70, 428, 152, 16)
    rrect(d, 212, 130, 300, 440, 16)
    rrect(d, 150, 400, 362, 452, 14)

    rect(d, 104, 116, 408, 138, MID)
    rect(d, 232, 152, 250, 400, MID)
    rect(d, 170, 424, 342, 440, MID)

    save(img, "gang_triads.png")


def gang_armenians():
    img, d = canvas()

    poly(d, [(28, 430), (196, 118), (298, 292), (366, 190), (484, 430)], MID)

    # Snow on the two peaks.
    poly(d, [(196, 118), (250, 210), (228, 206), (206, 226), (184, 200), (156, 210)])
    poly(d, [(366, 190), (410, 262), (392, 256), (372, 272), (354, 252), (330, 258)])

    rect(d, 28, 412, 484, 430)
    poly(d, [(196, 118), (298, 292), (270, 292), (196, 180)], LOW)

    save(img, "gang_armenians.png")


def gang_koreans():
    img, d = canvas()

    for x in (96, 206, 316):
        poly(d, [(x, 410), (x + 78, 102), (x + 130, 102), (x + 52, 410)])
        poly(d, [(x + 26, 410), (x + 104, 102), (x + 130, 102), (x + 52, 410)], MID)

    save(img, "gang_koreans.png")


ALL = [gang_families, gang_ballas, gang_vagos, gang_aztecas, gang_marabunta, gang_lost,
       gang_triads, gang_armenians, gang_koreans]
