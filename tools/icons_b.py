# -*- coding: utf-8 -*-
#
# Group B: the product, and the tools it takes. See iconkit for the rules.

import math

from PIL import ImageDraw

from iconkit import *


# ---------------------------------------------------------------- the bag and what goes in it

def baggie():
    img, d = canvas()

    # The bag is plastic, so it is the quiet plane; the seal and what is inside are solid.
    rrect(d, 116, 150, 396, 470, 34, MID)
    stroke(d, [(146, 210), (146, 430)], 10, LOW)

    poly(d, [(146, 470), (366, 470), (352, 372), (296, 330), (226, 344), (160, 400)])
    disc(d, 218, 300, 26)
    disc(d, 300, 322, 26)
    rrect(d, 262, 262, 342, 296, 17)

    rrect(d, 106, 120, 406, 188, 18)
    rrect(d, 128, 148, 384, 160, 6, CLEAR)

    save(img, "baggie.png")


def _capsule(d, cx, cy, deg, length, r):
    """A two-tone capsule along an axis: the near half solid, the far half in the second tone."""
    half = length / 2.0

    poly(d, rot(rrect_pts(cx - half, cy - r, cx + half, cy + r, r), cx, cy, deg))

    far = [(cx, cy - r)] + arc(cx + half - r, cy, r, -90, 90, 20) + [(cx, cy + r)]
    poly(d, rot(far, cx, cy, deg), MID)

    # A seam where the two halves meet, and a glint along the near half.
    poly(d, rot([(cx - 4, cy - r), (cx + 4, cy - r), (cx + 4, cy + r), (cx - 4, cy + r)], cx, cy, deg), LOW)
    poly(d, rot([(cx - half + 40, cy - r + 14), (cx - 30, cy - r + 14), (cx - 30, cy - r + 26), (cx - half + 40, cy - r + 26)], cx, cy, deg), W)


def _pill_shape(d, cx, cy, r, deep, fill):
    """The silhouette of a tablet -- its top face and the wall under it -- in one tone."""
    disc(d, cx, cy + deep, r, fill)
    disc(d, cx, cy, r, fill)
    rect(d, cx - r, cy, cx + r, cy + deep, fill)


def _pill(d, cx, cy, r, mark="smile"):
    """
    One pressed tablet, seen slightly from above.

    THE SIDE WALL IS THE WHOLE THING. A disc with a bevel on it is a coin -- it has no
    thickness, and a pill's thickness is the one feature that says which of the two it is.
    So the wall is a second disc set down by a fifth of the radius, in the tone that means
    "a plane below the body", and the top face sits on it. What is left showing is a crescent
    of edge under the tablet, which is exactly what every drawing of a pill has and what the
    version before this one did not.
    """
    deep = r * 0.22

    # The wall first: the whole silhouette in the lower tone, then the face laid on top of it.
    _pill_shape(d, cx, cy, r, deep, MID)

    disc(d, cx, cy, r, W)

    # The chamfer where the face rolls into the wall.
    ring(d, cx, cy, r - r * 0.09, r * 0.05, LOW)

    # PUNCHED, NOT DRAWN. The stamp on a pressed pill is a recess, and a recess in this kit is
    # a hole -- CLEAR shows the panel through, the same thing the score lines on the bar do.
    # Drawn in a tone it would be a sticker on a disc.
    if mark == "heart":
        # The one press everybody knows besides the face, and the reason it is on the second
        # tablet rather than a second smiley: two identical stamps read as one stamp printed
        # twice, and the whole job of the second pill is to say there is more than one thing
        # in the bag. A heart is a different silhouette at any size a face stops being one.
        # Nudged down and across, not centred on the tablet. The big one's silhouette is cut
        # out of this one, so the face left showing is not the middle of it -- a stamp centred
        # on the disc lands half in the bite.
        poly(d, heart_pts(cx + r * 0.13, cy + r * 0.02, r / 36.0), CLEAR)
        return

    if mark != "smile":
        return

    eye = r * 0.14
    disc(d, cx - r * 0.33, cy - r * 0.27, eye, CLEAR)
    disc(d, cx + r * 0.33, cy - r * 0.27, eye, CLEAR)

    # Angles run clockwise on screen, so 32 to 148 sweeps through the bottom.
    stroke(d, arc(cx, cy - r * 0.04, r * 0.52, 32, 148, 26), r * 0.15, CLEAR)


def pills():
    """
    Ecstasy: two pressed tablets, a smiley on one and a heart on the other.

    THE CAPSULES WERE THE WRONG DRUG. Two two-tone capsules is a picture of a prescription --
    it is what the painkiller this slot briefly held actually looks like -- and it did nothing
    to tell you which of the two PILL products you were holding, because the bar next to it is
    also a white pressed thing.

    TWO, SIDE BY SIDE AND BARELY TOUCHING, rather than one behind another. A tablet mostly
    hidden behind another tablet is a shape with no silhouette of its own; set down and across
    it reads as a second pill at a glance, which is what "a couple" has to mean at sixty
    pixels. The gap between them is carved rather than drawn: the front one's silhouette is
    punched out of the back one with a margin, so there is a clean line between the two in
    whatever colour the panel is.
    """
    img, d = canvas()

    # The smaller one, behind and down to the right. Far enough across that most of its
    # outline is its own: two tablets that overlap by a third are two tablets, and two that
    # overlap by two thirds are one tablet with a bump on it.
    _pill(d, 360, 364, 118, "heart")

    # A margin cut out of it, so the big one has a clean edge against it.
    _pill_shape(d, 186, 206, 146 + 14, 146 * 0.22, CLEAR)

    _pill(d, 186, 206, 146)

    save(img, "pills.png")


def xanax():
    img, d = canvas()

    rrect(d, 56, 204, 456, 308, 44)
    rect(d, 56, 272, 456, 308, MID)
    rect(d, 92, 272, 420, 308, MID)

    for x in (156, 256, 356):
        rect(d, x - 6, 214, x + 6, 298, CLEAR)

    save(img, "xanax.png")


def heroin():
    img, d = canvas()

    parts = []

    def part(pts, tone=W):
        parts.append((rot(pts, 256, 256, -38), tone))

    part(rrect_pts(200, 30, 312, 60, 12))
    part([(246, 56), (266, 56), (266, 136), (246, 136)])
    part(rrect_pts(206, 130, 306, 376, 16))
    part(rrect_pts(220, 224, 292, 362, 8), MID)

    for y in range(160, 360, 32):
        part([(220, y), (262, y), (262, y + 6), (220, y + 6)], LOW)

    part([(232, 376), (280, 376), (264, 404), (248, 404)])
    part([(251, 404), (261, 404), (261, 478), (251, 478)])

    for pts, tone in parts:
        poly(d, pts, tone)

    save(img, "heroin.png")


def coke():
    img, d = canvas()

    # A blade, two lines, and a rolled note.
    blade = rot(rrect_pts(40, 210, 300, 300, 10), 170, 255, -22)
    poly(d, blade)
    poly(d, rot([(70, 232), (120, 232), (120, 262), (70, 262)], 170, 255, -22), CLEAR)

    stroke(d, [(150, 356), (440, 260)], 22, MID)
    stroke(d, [(176, 412), (466, 316)], 22, MID)
    for i in range(12):
        t = i / 12.0
        disc(d, 150 + 290 * t + 6, 356 - 96 * t - 8, 5, LOW)
        disc(d, 176 + 290 * t - 4, 412 - 96 * t + 10, 5, LOW)

    note = rot(rrect_pts(300, 96, 470, 140, 22), 385, 118, 34)
    poly(d, note)
    poly(d, rot([(316, 108), (454, 108), (454, 128), (316, 128)], 385, 118, 34), MID)

    save(img, "coke.png")


def crack():
    img, d = canvas()

    def rock(pts, facet):
        poly(d, pts)
        poly(d, facet, MID)

    rock([(96, 300), (160, 210), (256, 226), (296, 320), (230, 410), (126, 388)],
         [(160, 210), (256, 226), (296, 320), (230, 410), (196, 320)])
    rock([(300, 160), (370, 130), (430, 190), (410, 270), (330, 270)],
         [(370, 130), (430, 190), (410, 270), (372, 220)])
    rock([(300, 336), (376, 320), (426, 380), (380, 452), (312, 430)],
         [(376, 320), (426, 380), (380, 452), (356, 380)])

    save(img, "crack.png")


def meth():
    img, d = canvas()

    stroke(d, [(200, 322), (450, 150)], 44)
    disc(d, 150, 340, 88)
    disc(d, 150, 340, 58, MID)
    disc(d, 150, 296, 14, CLEAR)

    # A wisp of smoke off the top.
    stroke(d, bez((150, 262), (120, 200), (170, 140), 20), 10, LOW)

    save(img, "meth.png")


def _lens(cx, cy, length, width, deg):
    """A pointed leaf shape along an axis."""
    pts = []
    for i in range(48):
        t = i / 48.0 * 2 * math.pi
        x = math.cos(t) * length / 2.0
        y = math.sin(t) * width / 2.0 * (1 - abs(math.cos(t)) * 0.35)
        pts.append((x, y))
    return shift(rot(pts, 0, 0, deg), cx, cy)


def weed():
    img, d = canvas()

    cx, cy = 256, 300

    for deg, length, width in ((-90, 300, 92), (-52, 270, 84), (-128, 270, 84),
                               (-16, 236, 74), (-164, 236, 74), (18, 190, 60), (162, 190, 60)):
        a = math.radians(deg)
        poly(d, _lens(cx + math.cos(a) * length / 2.0, cy + math.sin(a) * length / 2.0, length, width, deg))

    for deg, length in ((-90, 300), (-52, 270), (-128, 270), (-16, 236), (-164, 236), (18, 190), (162, 190)):
        a = math.radians(deg)
        stroke(d, [(cx, cy), (cx + math.cos(a) * (length - 30), cy + math.sin(a) * (length - 30))], 7, LOW)

    stroke(d, [(cx, cy), (cx, 470)], 16)

    save(img, "weed.png")


def leaf():
    img, d = canvas()

    poly(d, _lens(256, 236, 400, 190, -42))

    # Midrib and veins, cut into it.
    a = math.radians(-42)
    dx, dy = math.cos(a), math.sin(a)
    stroke(d, [(256 - dx * 190, 236 - dy * 190), (256 + dx * 190, 236 + dy * 190)], 12, LOW)

    for t in (-0.55, -0.25, 0.05, 0.35):
        px, py = 256 + dx * 190 * t, 236 + dy * 190 * t
        for side in (1, -1):
            nx, ny = -dy * side, dx * side
            stroke(d, [(px, py), (px + (nx + dx * 0.9) * 62, py + (ny + dy * 0.9) * 62)], 7, LOW)

    stroke(d, [(256 - dx * 190, 236 - dy * 190), (256 - dx * 250, 236 - dy * 250)], 16)

    save(img, "leaf.png")


def blunt():
    img, d = canvas()

    body = rot(rrect_pts(50, 230, 462, 290, 30), 256, 260, -14)
    poly(d, body)

    for t in (0.28, 0.46, 0.64):
        x = 50 + 412 * t
        pts = rot([(x - 10, 232), (x + 24, 232), (x + 4, 288), (x - 30, 288)], 256, 260, -14)
        poly(d, pts, LOW)

    tip = rot([(430, 230), (462, 230), (462, 290), (430, 290)], 256, 260, -14)
    poly(d, tip, MID)

    stroke(d, bez((466, 210), (500, 140), (470, 60), 24), 10, LOW)
    stroke(d, bez((440, 200), (462, 150), (440, 100), 20), 8, LOW)

    save(img, "blunt.png")


def bong():
    img, d = canvas()

    ellipse(d, 256, 404, 112, 70)
    rrect(d, 216, 92, 296, 400, 22)
    rrect(d, 196, 64, 316, 106, 16)

    stroke(d, [(262, 336), (362, 254)], 28)
    disc(d, 372, 244, 36)
    disc(d, 372, 244, 12, CLEAR)

    ellipse(d, 256, 420, 86, 40, MID)
    for cx, cy, r in ((230, 396, 8), (268, 384, 6), (250, 366, 5)):
        disc(d, cx, cy, r, LOW)

    save(img, "bong.png")


def edibles():
    img, d = canvas()

    disc(d, 206, 108, 30)
    disc(d, 306, 108, 30)
    disc(d, 256, 150, 64)
    rrect(d, 176, 190, 336, 402, 70)
    ellipse(d, 154, 262, 34, 62)
    ellipse(d, 358, 262, 34, 62)
    ellipse(d, 214, 402, 42, 34)
    ellipse(d, 298, 402, 42, 34)

    ellipse(d, 256, 300, 52, 72, MID)
    disc(d, 232, 144, 10, CLEAR)
    disc(d, 280, 144, 10, CLEAR)
    disc(d, 256, 168, 12, MID)

    save(img, "edibles.png")


def shrooms():
    img, d = canvas()

    poly(d, arc(256, 250, 170, 180, 360, 40) + [(426, 250), (86, 250)])
    rect(d, 86, 236, 426, 254, MID)
    rrect(d, 216, 250, 296, 444, 34)

    for cx, cy, r in ((200, 182, 22), (272, 138, 18), (332, 200, 16)):
        disc(d, cx, cy, r, CLEAR)

    poly(d, arc(408, 340, 62, 180, 360, 24) + [(470, 340), (346, 340)])
    rrect(d, 390, 340, 426, 440, 16)
    disc(d, 408, 306, 10, CLEAR)

    save(img, "shrooms.png")


def acid():
    """
    LSD: one perforated tab with the face on it.

    IT WAS A WHOLE SHEET AND THAT WAS THE WRONG UNIT. Four by four squares with dots in
    them is a picture of a supply -- what a chemist has -- and the game sells this by the
    TAB, one at a time. A grid also lost everything at 128 pixels: sixteen cells, twelve
    perforation lines and sixteen dots came out as texture, and texture at icon size is a
    grey smudge that could be anything.

    THE STAMP EDGE IS THE WHOLE IDENTITY. Nothing else in the drawer has a bumpy outline --
    the baggie is a pouch, the bar is a rectangle with score lines, the tablets are round --
    so the silhouette says which drug this is before any detail is read.

    THE BUMPS TOUCH, WHICH IS WHAT MAKES IT A STAMP AND NOT A GEAR. They were spaced a
    diameter and a half apart, leaving flat edge between each pair, and a flat with a lump
    on it every so often reads as a cog. A real perforation is a ROW of holes punched
    edge-to-edge and then torn, so what is left is a continuous scallop -- convex arcs
    meeting at cusps with no straight line anywhere along the side. Setting the radius to
    exactly half the spacing is that, and it is the whole difference.

    THE SMILEY IS THE ONE THING EVERYBODY ALREADY KNOWS. Blotter art is a century of
    unrelated drawings and exactly one of them is common knowledge; a tab with a face on it
    needs no caption. Drawn in ink INSIDE the punched circle, so the reading is paper, then
    print, then face -- three plainly separate things, and the mouth cannot merge with the
    circle it sits in.
    """
    img, d = canvas()

    x0, y0, x1, y1 = 92, 92, 420, 420

    rect(d, x0, y0, x1, y1)

    # THE PERFORATIONS ARE CUT OUT OF THE SQUARE, not stuck onto it.
    #
    # They were additive: a smaller square with half-discs added along each edge. That gives
    # the same wavy outline and it is the wrong way round, and you can see which it is --
    # added bumps read as something built UP, a row of studs or a cog, where a stamp is
    # something TORN. What is left after a tear is paper with bites out of it, and the eye
    # knows the difference even when it could not tell you what it is looking at.
    #
    # So the square is solid and the holes are punched through the edge of it. The body grew
    # to match, because a bite removes what a bump added and the tile has to stay the same
    # size on the shelf.
    #
    # RADIUS EXACTLY HALF THE SPACING. Neighbouring holes then just touch, and the paper
    # between two of them comes to a point ON the edge line -- which is what a perforation
    # tears like. Any smaller and there is a flat between each pair, and a flat with notches
    # in it is a stamp somebody has drawn from memory.
    #
    # The corners stay solid. A real stamp tears BETWEEN the holes, so the corner is the one
    # place with paper on both sides of it and it is always square.
    n = 7
    step = (x1 - x0) / float(n)
    r = step * 0.5

    for i in range(n):
        at = x0 + step * (i + 0.5)

        disc(d, at, y0, r, CLEAR)
        disc(d, at, y1, r, CLEAR)
        disc(d, x0, at, r, CLEAR)
        disc(d, x1, at, r, CLEAR)

    # The print, punched through, and the face drawn back into it.
    disc(d, 256, 256, 82, CLEAR)

    # BIG EYES. They were twelve and they were the first thing to go on the way down to 32 --
    # a dot that small survives the resize as a smudge, and two smudges over a mouth is a
    # face you have to be told about. Sixteen is a THIRD of the mouth's width, which sounds
    # heavy written down and is what a printed smiley actually looks like.
    for ex in (226, 286):
        disc(d, ex, 224, 16)

    stroke(d, arc(256, 246, 44, 25, 155), 15, W, caps=True)

    # THE TONGUE, out of the middle of the mouth and straight down, centred on the face.
    #
    # It OVERLAPS the smile rather than starting under it: two white shapes a pixel apart
    # become one grey smear at 32, and one that touches stays one shape all the way down.
    #
    # AND ITS TOP STAYS INSIDE THE SMILE'S OWN LINE. It started at 276 and the stroke's upper
    # edge at the bottom of the arc is 282, so four units of tongue stood ABOVE the mouth --
    # which at this size is a tooth, or a crack in the lip, and either way is not what a
    # tongue looks like. Starting at 286 puts the rounded top inside the fifteen-unit band
    # the stroke occupies: still one shape, with nothing showing over the top of it.
    #
    # AND IT FINISHES BEFORE THE PRINT DOES. The punched circle is 82 across, and a tongue
    # that reaches its edge merges with the white paper beyond it -- which makes the print
    # look broken rather than the tongue look long. Centred is the safest place for that as
    # well as the right one: straight down the middle is where the hole is deepest.
    poly(d, rrect_pts(234, 286, 278, 328, 21))

    save(img, "acid.png")


def lean():
    img, d = canvas()

    poly(d, [(140, 150), (372, 150), (340, 470), (172, 470)])
    poly(d, [(158, 290), (354, 290), (340, 470), (172, 470)], MID)
    rrect(d, 118, 120, 394, 160, 14)

    straw = rot(rrect_pts(290, 34, 322, 300, 14), 306, 160, 12)
    poly(d, straw)

    for x, y in ((200, 330), (250, 380), (300, 340)):
        rrect(d, x, y, x + 34, y + 34, 6, LOW)

    save(img, "lean.png")


def dabs():
    img, d = canvas()

    disc(d, 196, 350, 92)
    rrect(d, 166, 110, 226, 300, 24)
    rrect(d, 150, 84, 242, 124, 16)
    ellipse(d, 196, 380, 66, 36, MID)

    stroke(d, [(270, 310), (366, 224)], 24)
    rrect(d, 340, 150, 416, 240, 12)
    rrect(d, 352, 176, 404, 228, 8, MID)

    stroke(d, [(52, 468), (160, 380)], 14, MID)
    disc(d, 48, 470, 16)

    save(img, "dabs.png")


def hash_():
    img, d = canvas()

    poly(d, [(80, 200), (140, 140), (400, 140), (340, 200)], MID)
    poly(d, [(340, 200), (400, 140), (400, 340), (340, 400)], LOW)
    rrect(d, 80, 200, 340, 400, 12)

    for cx, cy in ((120, 250), (200, 320), (280, 260), (160, 370), (300, 350)):
        disc(d, cx, cy, 6, LOW)

    poly(d, [(360, 420), (420, 400), (450, 440), (400, 470)])
    poly(d, [(320, 440), (352, 430), (366, 462), (330, 474)])

    save(img, "hash.png")


def vape():
    img, d = canvas()

    rrect(d, 224, 40, 288, 472, 30)
    rrect(d, 238, 40, 274, 112, 14, MID)
    rect(d, 224, 150, 288, 162, LOW)
    rect(d, 224, 400, 288, 412, LOW)

    disc(d, 256, 300, 14, CLEAR)
    disc(d, 256, 300, 6, W)

    for cx, cy, r in ((300, 30, 14), (330, 52, 10), (322, 90, 8)):
        disc(d, cx, cy, r, LOW)

    save(img, "vape.png")


def poppy():
    img, d = canvas()

    for deg in (0, 90, 180, 270):
        pts = shift(rot(circle(0, 0, 1, 40), 0, 0, 0), 0, 0)
        petal = [(x * 66, y * 104 - 104) for x, y in pts]
        poly(d, shift(rot(petal, 0, 0, deg), 256, 206))

    disc(d, 256, 206, 44, MID)
    for a in range(0, 360, 60):
        disc(d, 256 + 26 * math.cos(math.radians(a)), 206 + 26 * math.sin(math.radians(a)), 7, CLEAR)

    stroke(d, [(256, 300), (256, 474)], 16)
    poly(d, _lens(300, 400, 110, 44, -30), MID)

    save(img, "poppy.png")


def fentanyl():
    img, d = canvas()

    rrect(d, 100, 100, 412, 412, 40)
    rrect(d, 148, 148, 364, 364, 22, MID)

    for i in range(6):
        stroke(d, [(160, 170 + i * 34), (352, 170 + i * 34)], 4, LOW)

    # The peeled corner: the panel shows through where the backing has come away.
    poly(d, [(412, 296), (412, 412), (296, 412)], CLEAR)
    poly(d, [(412, 296), (296, 412), (322, 322)])

    save(img, "fentanyl.png")


def ketamine():
    img, d = canvas()

    rrect(d, 176, 156, 336, 472, 30)
    poly(d, [(176, 186), (216, 150), (296, 150), (336, 186)])
    rrect(d, 216, 96, 296, 170, 10)
    rrect(d, 204, 46, 308, 102, 12, MID)

    rect(d, 176, 256, 336, 386, MID)
    for y in (286, 316, 346):
        rect(d, 200, y, 312, y + 8, LOW)

    stroke(d, [(200, 200), (200, 440)], 10, LOW)

    save(img, "ketamine.png")


def speed():
    img, d = canvas()

    bolt = [(292, 36), (146, 288), (240, 288), (198, 476), (372, 212), (282, 212), (334, 36)]
    poly(d, bolt)
    poly(d, [(292, 36), (334, 36), (282, 212), (372, 212), (300, 320), (262, 236), (292, 236)], MID)

    save(img, "speed.png")


def crystal():
    img, d = canvas()

    poly(d, [(256, 36), (330, 200), (256, 306), (182, 200)])
    poly(d, [(256, 36), (330, 200), (256, 306)], MID)

    poly(d, [(110, 176), (190, 296), (150, 430), (72, 340)])
    poly(d, [(150, 236), (190, 296), (150, 430), (114, 300)], MID)

    poly(d, [(400, 176), (440, 300), (386, 430), (322, 336)])
    poly(d, [(400, 176), (440, 300), (386, 430), (392, 300)], MID)

    save(img, "crystal.png")


def _cut(name, fraction):
    img, d = canvas()

    ring(d, 256, 256, 200, 34)
    ring(d, 256, 256, 152, 10, MID)

    if fraction >= 0.999:
        disc(d, 256, 256, 146)
    elif fraction > 0.001:
        d.pieslice([256 - 146, 256 - 146, 256 + 146, 256 + 146], -90, -90 + 360 * fraction, fill=W)

    save(img, name)


def cut_100():
    _cut("cut_100.png", 1.0)


def cut_75():
    _cut("cut_75.png", 0.75)


def cut_50():
    _cut("cut_50.png", 0.5)


def cut_33():
    _cut("cut_33.png", 1 / 3.0)


def cut_25():
    _cut("cut_25.png", 0.25)


# ---------------------------------------------------------------- the tools

def guns():
    img, d = canvas()

    # The rifle, in parts: wood at MID, steel at full.
    poly(d, [(58, 234), (138, 240), (138, 294), (118, 302), (36, 302), (34, 248)], MID)
    rect(d, 134, 232, 348, 294)
    rect(d, 150, 242, 332, 250, LOW)

    rect(d, 346, 232, 434, 264, MID)
    rect(d, 346, 264, 420, 294)
    rect(d, 432, 244, 488, 262)
    rect(d, 446, 224, 456, 244)
    rect(d, 470, 238, 488, 268)

    poly(d, rot(rrect_pts(226, 294, 262, 366, 8), 244, 294, 16))
    ribbon(d, bez((282, 294), (300, 366), (362, 396), 20), 46, 40, MID)

    stroke(d, [(266, 294), (266, 322), (302, 322)], 10, MID)

    save(img, "guns.png")


def ammo():
    img, d = canvas()

    for x in (130, 256, 382):
        rrect(d, x - 30, 200, x + 30, 470, 8, MID)
        poly(d, [(x - 30, 202), (x - 30, 170), (x, 88), (x + 30, 170), (x + 30, 202)])
        rect(d, x - 30, 448, x + 30, 458, LOW)
        rect(d, x - 30, 200, x + 30, 214, LOW)

    save(img, "ammo.png")


def _spray_cap(name, nozzle):
    """A cap, FROM THE FRONT.

    IT WAS DRAWN FROM ABOVE, and that is why it read as a can: a tall rounded body with the
    stem standing up out of the top of it is the silhouette of an aerosol, whatever the little
    ellipse on top is meant to be. Nobody looks at a cap from above. You look at the face of
    it, which is the part with the hole in.

    So: a body WIDER AT THE TOP than the bottom, ribbed down the face, with the nozzle in the
    middle of it and the stem hanging BELOW on a flared skirt. That is the shape in every
    photograph of one, and the taper plus the stem underneath is what stops it being a can at
    any size.

    THE RIBS FOLLOW THE TAPER rather than running straight down, which is both what the real
    thing does and the only way they stay inside the outline without being clipped.

    The three caps differ by the size of the nozzle, which is the one thing about them a player
    is choosing. Its housing is punched out and the spray hole is drawn back in the middle of
    it -- a dark disc with a bright pinhole, exactly as it photographs.
    """
    img, d = canvas(328, 512)

    # ---- the body ----
    top, bot = 58, 356
    r = 44

    body = [(24 + r, top), (304 - r, top)]
    body += bez((304 - r, top), (304, top), (300, 104))
    body += [(280, 330)]
    body += bez((280, 330), (274, 356), (250, 362))
    body += bez((250, 362), (164, 390), (78, 362))
    body += bez((78, 362), (54, 356), (48, 330))
    body += [(28, 104)]
    body += bez((28, 104), (24, top), (24 + r, top))

    poly(d, body)

    # ---- the ribs, narrowing with the body ----
    for dx in range(-120, 121, 20):
        stroke(d, [(164 + dx, 86), (164 + dx * 0.826, 344)], 7, LOW)

    # ---- and the stem that goes into the can ----
    #
    # NO SKIRT. There was a flared ellipse as wide as the body sat under it, and there is no
    # such thing on the object -- the bowed bottom edge IS the skirt, and putting a second one
    # under it gave the cap a saucer to stand on. The stem comes straight out of the bottom.
    rrect(d, 132, 356, 196, 468, 16)
    rect(d, 138, 408, 190, 446, MID)

    # ---- and the nozzle, which is the whole point of the object ----
    disc(d, 164, 178, nozzle, CLEAR)
    disc(d, 164, 178, max(7, nozzle * 0.19), W)

    save(img, name, 82, 128)


def cap_fat():
    _spray_cap("cap_fat.png", 76)


def cap_stock():
    _spray_cap("cap_stock.png", 56)


def cap_thin():
    _spray_cap("cap_thin.png", 38)


ALL = [baggie, pills, xanax, heroin, coke, crack, meth, weed, leaf, blunt, bong, edibles, shrooms,
       acid, lean, dabs, hash_, vape, poppy, fentanyl, ketamine, speed, crystal,
       cut_100, cut_75, cut_50, cut_33, cut_25,
       guns, ammo, cap_fat, cap_stock, cap_thin]
