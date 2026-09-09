# -*- coding: utf-8 -*-
#
# Group A: the interface, the money, the places and the people. See iconkit for the rules.
#
# Every icon is a bold silhouette first -- that is what reads at twenty pixels -- and then the
# planes and grooves that make it a drawing at sixty: a MID plane for a face turned from the
# light, LOW lines for seams and texture, CLEAR for holes.

import math

from iconkit import *


# ---------------------------------------------------------------- arrows

def _arrow(d, to_right=True):
    shaft = rrect_pts(70, 216, 330, 296, 40)
    head = [(300, 112), (300, 400), (456, 256)]

    if not to_right:
        shaft = [(512 - x, y) for x, y in shaft]
        head = [(512 - x, y) for x, y in head]

    poly(d, shaft)
    poly(d, head)


def arrow_right():
    img, d = canvas()
    _arrow(d, True)
    save(img, "arrow_right.png")


def arrow_left():
    img, d = canvas()
    _arrow(d, False)
    save(img, "arrow_left.png")


def arrow_updown():
    img, d = canvas()
    poly(d, rrect_pts(216, 120, 296, 392, 40))
    poly(d, [(112, 176), (400, 176), (256, 36)])
    poly(d, [(112, 336), (400, 336), (256, 476)])
    save(img, "arrow_updown.png")


def arrow_leftright():
    img, d = canvas()
    poly(d, rrect_pts(120, 216, 392, 296, 40))
    poly(d, [(176, 112), (176, 400), (36, 256)])
    poly(d, [(336, 112), (336, 400), (476, 256)])
    save(img, "arrow_leftright.png")


# ---------------------------------------------------------------- the feed

def reply():
    img, d = canvas()
    rrect(d, 52, 84, 460, 352, 80)
    poly(d, [(128, 330), (128, 468), (250, 344)])

    # Three words being typed, as the darker dots every bubble on earth has.
    for x in (170, 256, 342):
        disc(d, x, 218, 30, MID)

    save(img, "reply.png")


def repost():
    img, d = canvas()

    # Two arcs chasing each other, with fat heads. Fat, because at row size a slim head is
    # a stub and the whole thing reads as a broken ring.
    head = [(0, -96), (0, 96), (120, 0)]

    stroke(d, arc(256, 256, 150, 200, 335, 40), 58)
    poly(d, shift(rot(head, 0, 0, 130), 386, 176))

    stroke(d, arc(256, 256, 150, 20, 155, 40), 58)
    poly(d, shift(rot(head, 0, 0, 310), 126, 336))

    save(img, "repost.png")


def _heart_pts(cx, cy, k):
    """A heart as one polygon, so it can be drawn hollow without punching the panel."""
    pts = []
    for i in range(120):
        t = math.pi * 2 * i / 120.0
        x = 16 * math.sin(t) ** 3
        y = -(13 * math.cos(t) - 5 * math.cos(2 * t) - 2 * math.cos(3 * t) - math.cos(4 * t))
        pts.append((cx + x * k, cy + y * k))
    return pts


def heart():
    img, d = canvas()
    poly(d, _heart_pts(256, 236, 13.2))

    # The lower half turned from the light, and a glint on the upper left lobe.
    lower = [(x, y) for x, y in _heart_pts(256, 236, 13.2) if y > 300]
    poly(d, [(lower[0][0], 300)] + lower + [(lower[-1][0], 300)], MID)
    ellipse(d, 172, 170, 28, 16, MID)

    save(img, "heart.png")


def like():
    img, d = canvas()
    poly(d, _heart_pts(256, 236, 13.2))
    poly(d, _heart_pts(256, 236, 8.6), CLEAR)

    # A smaller heart sat in the hollow, quieter: the thing you have not done yet.
    poly(d, _heart_pts(256, 240, 4.4), MID)

    save(img, "like.png")


def tick():
    img, d = canvas()
    poly(d, circle(256, 256, 226, 8, 22.5))

    ring(d, 256, 256, 178, 12, MID, 64)

    # The check is a hole, and the elbow gets a disc so the two strokes meet round.
    stroke(d, [(150, 266), (226, 342), (368, 196)], 76, CLEAR)

    save(img, "tick.png")


def drop():
    img, d = canvas()
    poly(d, rrect_pts(216, 60, 296, 300, 40))
    poly(d, [(112, 260), (400, 260), (256, 400)])
    rrect(d, 96, 424, 416, 468, 20)

    # Two marks of motion above the head, fainter as they go up.
    stroke(d, [(160, 200), (200, 200)], 22, MID)
    stroke(d, [(312, 200), (352, 200)], 22, MID)
    stroke(d, [(180, 140), (206, 140)], 18, LOW)
    stroke(d, [(306, 140), (332, 140)], 18, LOW)

    save(img, "drop.png")


def disc_():
    img, d = canvas()
    disc(d, 256, 256, 210)
    ring(d, 256, 256, 150, 22, MID)
    disc(d, 256, 256, 30, MID)
    save(img, "disc.png")


# ---------------------------------------------------------------- signs

def warning():
    img, d = canvas()

    tri = [(256, 40), (476, 440), (36, 440)]
    poly(d, tri)
    stroke(d, tri, 44, W, closed=True)

    inner = [(256, 118), (424, 412), (88, 412)]
    stroke(d, inner, 14, MID, closed=True)

    rrect(d, 232, 176, 280, 330, 22, CLEAR)
    disc(d, 256, 384, 30, CLEAR)

    save(img, "warning.png")


def locked():
    img, d = canvas()

    stroke(d, arc(256, 196, 108, 180, 360, 40), 46)
    rrect(d, 108, 216, 404, 468, 40)

    # The lip of the body is a plane of its own, and the keyhole is a hole.
    rrect(d, 108, 216, 404, 262, 40, MID)
    rect(d, 108, 250, 404, 262, MID)

    disc(d, 256, 328, 36, CLEAR)
    rrect(d, 236, 328, 276, 412, 14, CLEAR)

    save(img, "locked.png")


def key():
    img, d = canvas()

    ring(d, 132, 256, 88, 46)
    rrect(d, 200, 234, 470, 278, 22)
    rect(d, 372, 276, 404, 336)
    rect(d, 426, 276, 462, 322)

    # A groove along the blade, and a dimple in the bow.
    rrect(d, 226, 250, 440, 262, 6, MID)
    disc(d, 132, 256, 20, MID)

    save(img, "key.png")


def eyes():
    img, d = canvas()

    for cx in (146, 366):
        lens = arc(cx, 176, 150, 45, 135, 24) + arc(cx, 336, 150, 225, 315, 24)
        poly(d, lens)
        disc(d, cx, 256, 52, MID)
        disc(d, cx, 256, 28, CLEAR)
        disc(d, cx - 16, 240, 10, W)

    save(img, "eyes.png")


def footfall():
    img, d = canvas()

    def foot(cx, cy, flip):
        ellipse(d, cx, cy, 62, 96)
        ellipse(d, cx, cy + 128, 46, 50)
        rect(d, cx - 46, cy + 60, cx + 46, cy + 130)

        toes = [(-46, -108, 14), (-20, -122, 17), (8, -124, 16), (34, -114, 13), (54, -98, 11)]
        for dx, dy, r in toes:
            disc(d, cx + (dx if not flip else -dx), cy + dy, r)

        # The arch, turned away.
        ellipse(d, cx + (18 if not flip else -18), cy + 60, 30, 40, MID)

    foot(166, 290, False)
    foot(346, 200, True)

    save(img, "footfall.png")


def rank():
    img, d = canvas()

    for i, y in enumerate((96, 210, 324)):
        stroke(d, [(90, y + 120), (256, y), (422, y + 120)], 58)
        stroke(d, [(120, y + 118), (256, y + 20), (392, y + 118)], 10, MID)

    save(img, "rank.png")


def people():
    """
    A contact card: a head and shoulders on the left of a rounded card, two lines of
    detail on the right.

    IT WAS THREE HEADS IN A ROW, and three heads in a row is a crowd -- which is the Gangs
    app, or the block, or anyone. What a contacts app opens is a CARD for one person, and
    that is the glyph every phone uses for it: a silhouette beside some writing. At sixty
    pixels the card edge, the head and two bars are all that survive, and they are enough.

    The details are punched out of the card rather than drawn on it, like the skull's eye
    sockets, so whatever colour the icon is tinted shows through them.
    """
    img, d = canvas()

    # The card.
    rrect(d, 40, 112, 472, 400, 46)

    # The person, punched out of it: head and shoulders.
    disc(d, 158, 212, 44, CLEAR)
    rrect(d, 96, 262, 220, 348, 40, CLEAR)

    # And two lines beside them, the longer one on top the way a name sits over a number.
    rrect(d, 262, 196, 424, 232, 18, CLEAR)
    rrect(d, 262, 262, 372, 298, 18, CLEAR)

    save(img, "people.png")


def pin():
    img, d = canvas()

    ellipse(d, 256, 478, 80, 16, LOW)

    disc(d, 256, 196, 152)
    poly(d, [(122, 272), (390, 272), (256, 478)])

    ring(d, 256, 196, 88, 18, MID)
    disc(d, 256, 196, 56, CLEAR)

    save(img, "pin.png")


def deal():
    img, d = canvas()

    # A hand, palm up, with a bag in it. A handshake is the obvious picture and it is
    # unreadable at forty pixels -- two forearms and a knot -- where an open hand holding
    # the product says the same thing in one shape.
    rrect(d, 166, 236, 346, 420, 58)
    for x in (172, 216, 260, 304):
        rrect(d, x, 128, x + 38, 270, 19)

    poly(d, rot(rrect_pts(96, 270, 160, 400, 30), 128, 335, -28))

    # The wrist, and the lines of the palm.
    rrect(d, 196, 400, 316, 474, 24, MID)
    stroke(d, [(200, 330), (300, 350)], 8, LOW)
    stroke(d, [(196, 372), (290, 386)], 8, LOW)

    # The bag sitting in it: plastic at the quiet tone, seal and contents solid.
    rrect(d, 196, 196, 316, 340, 22, MID)
    poly(d, [(210, 340), (302, 340), (296, 296), (256, 274), (216, 292)])
    rrect(d, 190, 184, 322, 214, 10)
    rrect(d, 204, 196, 308, 204, 4, CLEAR)

    save(img, "deal.png")


# ---------------------------------------------------------------- the phone and the block

def socials():
    img, d = canvas()

    rrect(d, 90, 56, 300, 456, 40)
    rrect(d, 112, 100, 278, 400, 8, MID)
    rrect(d, 166, 70, 224, 82, 6, CLEAR)
    disc(d, 195, 430, 14, CLEAR)

    rrect(d, 246, 118, 474, 272, 50)
    poly(d, [(300, 262), (280, 326), (346, 268)])

    for x in (312, 360, 408):
        disc(d, x, 195, 18, CLEAR)

    save(img, "socials.png")


def mobile():
    img, d = canvas()

    rrect(d, 150, 40, 362, 472, 44)
    rrect(d, 170, 92, 342, 420, 10, MID)

    rrect(d, 196, 150, 316, 176, 12)
    rrect(d, 196, 212, 282, 238, 12)
    rrect(d, 196, 274, 316, 300, 12)
    rrect(d, 196, 336, 250, 362, 12)

    rrect(d, 226, 58, 286, 70, 6, CLEAR)
    rrect(d, 226, 440, 286, 456, 8, CLEAR)

    save(img, "mobile.png")


def phone():
    img, d = canvas()

    spine = arc(256, 130, 214, 34, 146, 40)
    ribbon(d, spine, 96, 96)
    disc(d, spine[0][0], spine[0][1], 76)
    disc(d, spine[-1][0], spine[-1][1], 76)

    # The mouthpiece and the earpiece have grilles.
    for cx, cy in (spine[0], spine[-1]):
        for dx in (-22, 0, 22):
            for dy in (-22, 0, 22):
                disc(d, cx + dx, cy + dy, 8, MID)

    stroke(d, arc(256, 130, 214, 60, 120, 20), 12, MID)

    save(img, "phone.png")


def megaphone():
    img, d = canvas()

    poly(d, [(96, 190), (330, 96), (330, 416), (96, 322)])
    poly(d, [(96, 260), (330, 260), (330, 416), (96, 322)], MID)
    rrect(d, 300, 72, 372, 440, 30)
    rrect(d, 70, 196, 110, 316, 14)
    rrect(d, 146, 316, 212, 436, 22)

    stroke(d, arc(372, 256, 74, -48, 48, 20), 24)
    stroke(d, arc(372, 256, 122, -40, 40, 20), 18, MID)

    save(img, "megaphone.png")


def music():
    img, d = canvas()

    ellipse(d, 150, 384, 60, 44)
    ellipse(d, 340, 350, 60, 44)
    rect(d, 186, 120, 212, 384)
    rect(d, 374, 84, 400, 350)
    poly(d, [(186, 120), (400, 84), (400, 150), (186, 186)])
    poly(d, [(186, 200), (400, 164), (400, 204), (186, 240)], MID)

    save(img, "music.png")


def tattoo():
    img, d = canvas()

    # The machine: frame, two coils, the grip, the needle.
    rrect(d, 150, 66, 362, 184, 24)
    for cx in (210, 302):
        disc(d, cx, 125, 34, CLEAR)
        disc(d, cx, 125, 22, MID)

    rrect(d, 226, 184, 286, 396, 18)
    for y in (232, 268, 304, 340):
        rect(d, 226, y, 286, y + 12, MID)

    rect(d, 249, 396, 263, 474)
    poly(d, [(236, 380), (276, 380), (256, 410)], MID)

    save(img, "tattoo.png")


def health():
    img, d = canvas()

    rrect(d, 194, 56, 318, 456, 30)
    rrect(d, 56, 194, 456, 318, 30)

    # Bevelled: a darker inner cross and a bright core.
    rrect(d, 222, 84, 290, 428, 20, MID)
    rrect(d, 84, 222, 428, 290, 20, MID)
    rrect(d, 240, 102, 272, 410, 12)
    rrect(d, 102, 240, 410, 272, 12)

    save(img, "health.png")


def scales():
    img, d = canvas()

    rect(d, 244, 96, 268, 420)
    rrect(d, 140, 420, 372, 462, 16)
    rrect(d, 64, 118, 448, 146, 14)
    disc(d, 256, 96, 28)

    for cx in (96, 416):
        stroke(d, [(cx, 146), (cx - 58, 300)], 9, MID)
        stroke(d, [(cx, 146), (cx + 58, 300)], 9, MID)
        poly(d, arc(cx, 292, 74, 0, 180, 24))
        stroke(d, arc(cx, 292, 74, 10, 170, 24), 10, MID)

    save(img, "scales.png")


def crown():
    img, d = canvas()

    poly(d, [(72, 336), (72, 150), (168, 262), (256, 100), (344, 262), (440, 150), (440, 336)])
    rrect(d, 72, 326, 440, 434, 22)

    for cx, cy in ((72, 150), (256, 100), (440, 150)):
        disc(d, cx, cy, 30)

    rect(d, 92, 352, 420, 384, MID)
    for x in (168, 256, 344):
        disc(d, x, 402, 22, CLEAR)

    save(img, "crown.png")


def skull():
    img, d = canvas()

    disc(d, 256, 212, 186)
    rrect(d, 108, 250, 404, 392, 60)
    rrect(d, 154, 362, 358, 472, 44)

    # Cheekbones, turned from the light.
    poly(d, [(112, 300), (206, 296), (200, 350), (128, 344)], MID)
    poly(d, [(400, 300), (306, 296), (312, 350), (384, 344)], MID)

    ellipse(d, 172, 222, 58, 66, CLEAR)
    ellipse(d, 340, 222, 58, 66, CLEAR)
    poly(d, [(256, 270), (294, 348), (218, 348)], CLEAR)

    rect(d, 154, 390, 358, 404, CLEAR)
    for x in (198, 238, 278, 318):
        rect(d, x - 6, 398, x + 6, 472, CLEAR)

    stroke(d, [(256, 34), (244, 80), (268, 122), (250, 166)], 9, LOW)

    save(img, "skull.png")


def police():
    img, d = canvas()

    shield = [(256, 24), (466, 96), (466, 276), (256, 490), (46, 276), (46, 96)]
    poly(d, shield)
    poly(d, scale(shield, 256, 256, 0.86), MID)

    poly(d, star(256, 236, 160, 84), CLEAR)

    rrect(d, 96, 294, 416, 338, 10)
    for x in (140, 196, 252, 308, 364):
        rect(d, x, 310, x + 30, 322, MID)

    save(img, "police.png")


def mask():
    img, d = canvas()

    ellipse(d, 256, 256, 188, 232)
    ellipse(d, 256, 150, 150, 70, MID)

    ellipse(d, 186, 210, 50, 42, CLEAR)
    ellipse(d, 326, 210, 50, 42, CLEAR)

    for y in (326, 366, 406):
        for x in (196, 236, 276, 316):
            disc(d, x, y, 12, CLEAR)

    # The strap fittings.
    disc(d, 76, 240, 16, MID)
    disc(d, 436, 240, 16, MID)

    save(img, "mask.png")


def wifi():
    """
    Three arcs and a dot, fanning up from the corner. Drawn with real arcs at 512 so the
    curves survive the downsample -- the rectangle version in the status bar did not.
    """
    img, d = canvas()

    cx, cy = 256, 392

    for i, r in enumerate((100, 190, 280)):
        d.arc([cx - r, cy - r, cx + r, cy + r], 222, 318, fill=W, width=46)

    disc(d, cx, cy, 38)

    save(img, "wifi.png")


def bandana():
    """A bandana, knotted, hanging as a face cover.

    IT REPLACES THE BALACLAVA, which was drawn five times and was wrong five times -- see the
    note that used to be here. The last one worked, as an outline, and an outline is the one
    thing in this set that does not hold at eighteen pixels. A bandana is a better icon for the
    same idea: a triangle with a knot on it is a shape nothing else in the set could be
    mistaken for, and it is solid, so it survives being shrunk.

    SOLID IS SAFE HERE AND IT WAS NOT THERE. The reason a filled balaclava came out as a grey
    alien is that filling it made a white HEAD -- and this medium is white ink on a dark panel.
    A bandana filled white is a white cloth, which is what a bandana often is. The polarity
    only bites when the silhouette is a face.

    Three pieces, the way the reference has them: the rolled band across the top, the cloth
    hanging off it to a point, and the knot with its two ends splayed above.
    """
    img, d = canvas()

    # The cloth: a wide curved top falling away to a point.
    poly(d, cbez((78, 246), (152, 186), (360, 186), (434, 246))
         + cbez((434, 246), (426, 338), (348, 422), (256, 484))
         + cbez((256, 484), (164, 422), (86, 338), (78, 246)))

    # The rolled band over the top of it, the full width of the cloth.
    stroke(d, cbez((72, 240), (150, 170), (362, 170), (440, 240)), 46)

    # And the daylight between the two, in the middle only -- they are still one piece of cloth
    # at the ends, which is what stops the band reading as a hoop floating above it.
    stroke(d, cbez((142, 218), (198, 184), (314, 184), (370, 218)), 13, CLEAR)

    # THE TIE ENDS ARE STUBS, NOT WINGS. They were long curved ribbons splayed out either side
    # and the whole icon came out as a moth -- two wings, a body and a pair of legs where the
    # folds in the cloth were. A knot has short ends and they stick UP, close in.
    ribbon(d, [(234, 156), (176, 86)], 46, 20)
    ribbon(d, [(278, 156), (336, 86)], 46, 20)

    # The knot, last and over the top of them, and wider than either -- it is the thing that
    # says this is tied rather than draped.
    disc(d, 256, 152, 48)

    save(img, "bandana.png")


def garage():
    img, d = canvas()

    poly(d, [(36, 236), (256, 56), (476, 236)])
    rect(d, 78, 230, 434, 452)
    rect(d, 336, 110, 378, 186)

    rrect(d, 136, 272, 376, 452, 8, MID)
    for y in (302, 342, 382, 422):
        rect(d, 148, y, 364, y + 10, CLEAR)

    save(img, "garage.png")


def car():
    img, d = canvas()

    poly(d, [(40, 300), (86, 236), (156, 228), (214, 150), (334, 150), (412, 228), (472, 246),
             (472, 344), (40, 344)])

    poly(d, [(166, 234), (218, 168), (258, 168), (258, 234)], MID)
    poly(d, [(274, 168), (322, 168), (392, 234), (274, 234)], MID)
    stroke(d, [(260, 236), (262, 330)], 8, MID)

    for cx in (144, 372):
        disc(d, cx, 346, 66, CLEAR)
        disc(d, cx, 346, 54)
        ring(d, cx, 346, 36, 10, MID)
        disc(d, cx, 346, 18, CLEAR)

    rect(d, 452, 262, 472, 286, MID)
    rect(d, 40, 262, 60, 286, MID)

    save(img, "car.png")


def tow():
    img, d = canvas()

    poly(d, [(232, 302), (196, 270), (390, 132), (424, 168)])
    rect(d, 392, 158, 420, 234)
    ring(d, 406, 250, 28, 20)

    rrect(d, 40, 236, 190, 352, 18)
    rrect(d, 62, 258, 150, 306, 8, CLEAR)
    rect(d, 186, 290, 362, 352)
    stroke(d, [(200, 304), (350, 304)], 8, MID)

    for cx in (122, 300):
        disc(d, cx, 376, 48)
        disc(d, cx, 376, 18, CLEAR)
        ring(d, cx, 376, 32, 8, MID)

    save(img, "tow.png")


def bank():
    img, d = canvas()

    poly(d, [(28, 196), (256, 62), (484, 196)])
    poly(d, [(80, 190), (256, 88), (432, 190)], MID)
    rect(d, 44, 196, 468, 244)

    for i in range(4):
        x = 76 + i * 100
        rect(d, x - 8, 254, x + 68, 270)
        rect(d, x, 270, x + 60, 396)
        rect(d, x + 26, 282, x + 34, 386, LOW)

    rect(d, 28, 402, 484, 428)
    rect(d, 44, 428, 468, 458, MID)

    save(img, "bank.png")


def card():
    img, d = canvas()

    rrect(d, 36, 138, 476, 374, 36)
    rect(d, 36, 186, 476, 240, CLEAR)

    rrect(d, 84, 270, 176, 336, 14, MID)
    for x in (104, 132, 156):
        rect(d, x, 280, x + 8, 326, LOW)

    for i in range(4):
        rrect(d, 204 + i * 64, 300, 252 + i * 64, 318, 6, MID)

    save(img, "card.png")


def cash():
    img, d = canvas()

    rrect(d, 36, 148, 476, 364, 20)
    stroke(d, rrect_pts(62, 172, 450, 340, 14), 8, MID, closed=True)

    ellipse(d, 256, 256, 74, 60, MID)
    glyph(d, "$", 256, 256, 92, "UnifrakturCook-Bold.ttf")

    for cx, cy in ((92, 200), (420, 200), (92, 312), (420, 312)):
        disc(d, cx, cy, 16, MID)

    save(img, "cash.png")


def money():
    img, d = canvas()

    rrect(d, 84, 322, 428, 404, 14, LOW)
    rrect(d, 62, 254, 450, 336, 14, MID)
    rrect(d, 84, 180, 428, 262, 14)

    stroke(d, rrect_pts(104, 196, 408, 246, 10), 6, MID, closed=True)
    ellipse(d, 256, 221, 50, 26, MID)
    disc(d, 256, 221, 12)

    save(img, "money.png")


def crate():
    img, d = canvas()

    rect(d, 56, 116, 456, 440)
    for y in (196, 276, 356):
        rect(d, 56, y, 456, y + 8, LOW)

    stroke(d, [(56, 116), (456, 440)], 28, MID)
    stroke(d, [(456, 116), (56, 440)], 28, MID)
    stroke(d, [(56, 116), (456, 116), (456, 440), (56, 440)], 24, W, closed=True)

    save(img, "crate.png")


def box():
    img, d = canvas()

    poly(d, [(56, 176), (256, 266), (256, 474), (56, 384)], MID)
    poly(d, [(256, 266), (456, 176), (456, 384), (256, 474)], LOW)
    poly(d, [(256, 86), (456, 176), (256, 266), (56, 176)])

    stroke(d, [(156, 131), (356, 221)], 18, MID)
    stroke(d, [(256, 266), (256, 474)], 16)

    save(img, "box.png")


def brick():
    img, d = canvas()

    rrect(d, 56, 146, 456, 366, 28)
    rect(d, 56, 236, 456, 276, MID)
    rect(d, 236, 146, 276, 366, MID)

    for x0, x1 in ((72, 110), (402, 440)):
        stroke(d, [(x0, 160), (x1, 352)], 6, LOW)
        stroke(d, [(x1, 160), (x0, 352)], 6, LOW)

    rrect(d, 322, 176, 404, 214, 6, CLEAR)

    save(img, "brick.png")


def stash():
    """A backpack.

    THE TOP IS A SHALLOW CURVE, NOT A DOME. It was drawn as a half circle across the full
    width of the bag, which makes the curve as tall as the bag is half wide -- so the thing had
    a great balloon on top of it and the body underneath looked squashed. On a real one the
    rise across the top is under a third of the width: rounded shoulders and a top that is
    nearly flat between them.

    So the outline is written out rather than assembled from a dome and a box. That also fixes
    what was wrong underneath it -- the dome and the box were separate shapes with separate
    centres, and the hair line meant to separate the bag from the panel behind it came out thin
    across the crown and fat down the sides.

    A flat silhouette with white cuts, like the reference: parts separated by GAPS rather than
    by tone, because a grey plane at this size is a smudge and a two-pixel gap is not.
    """
    img, d = canvas()

    def shell(x0, x1, top, bot, rise, r):
        """A bag body: rounded shoulders, a nearly flat top, a rounded bottom."""
        lip = top + rise

        pts = [(x0, bot - r)]
        pts += cbez((x0, lip), (x0, top + 6), (x0 + rise * 0.6, top), (x0 + rise * 1.15, top))
        pts += [(x1 - rise * 1.15, top)]
        pts += cbez((x1 - rise * 1.15, top), (x1 - rise * 0.6, top), (x1, top + 6), (x1, lip))
        pts += [(x1, bot - r)]
        pts += bez((x1, bot - r), (x1, bot), (x1 - r, bot))
        pts += [(x0 + r, bot)]
        pts += bez((x0 + r, bot), (x0, bot), (x0, bot - r))

        return pts

    # The strap, behind everything. A loop, so it has a hole in it.
    rrect(d, 350, 168, 448, 420, 48)
    rrect(d, 378, 206, 422, 380, 22, CLEAR)

    # The panel behind, showing past the body on the right.
    poly(d, shell(168, 400, 132, 434, 66, 44))

    # The body, and the hair line that separates the two. The gap is the SAME outline grown by
    # ten all round rather than a second shape with its own numbers.
    poly(d, shell(94, 354, 96, 462, 76, 54), CLEAR)
    poly(d, shell(104, 344, 106, 452, 70, 46))

    # The grab handle, sat on the crown. Its legs land inside the curve rather than stopping at
    # the highest point of it, which is what had it floating.
    stroke(d, arc(224, 116, 44, 184, 356, 24), 21)

    # The front pocket, over the bottom half and standing proud on the left.
    rrect(d, 62, 262, 308, 462, 50, CLEAR)
    rrect(d, 72, 272, 298, 452, 42)

    # And its zip.
    rrect(d, 112, 320, 258, 352, 13, CLEAR)

    save(img, "stash.png")


def bed():
    img, d = canvas()

    rrect(d, 56, 136, 118, 344, 16)
    rrect(d, 394, 216, 456, 344, 12)
    rrect(d, 56, 266, 456, 340, 18)

    rrect(d, 132, 226, 254, 276, 20, MID)
    rect(d, 132, 300, 456, 340, MID)

    rect(d, 68, 340, 96, 406)
    rect(d, 416, 340, 444, 406)

    save(img, "bed.png")


def dog():
    img, d = canvas()

    poly(d, [(136, 172), (176, 56), (232, 154)])
    poly(d, [(376, 172), (336, 56), (280, 154)])
    disc(d, 256, 240, 140)

    rrect(d, 194, 266, 318, 384, 50, MID)
    disc(d, 256, 346, 28)
    stroke(d, [(256, 374), (256, 388)], 8)

    disc(d, 206, 220, 20, CLEAR)
    disc(d, 306, 220, 20, CLEAR)

    save(img, "dog.png")


def fire():
    img, d = canvas()

    outer = ([(256, 472)]
             + cbez((256, 472), (60, 440), (90, 220), (190, 170))
             + cbez((190, 170), (200, 100), (240, 60), (256, 30))
             + cbez((256, 30), (300, 120), (330, 140), (340, 120))
             + cbez((340, 120), (430, 220), (440, 420), (256, 472)))

    poly(d, outer, MID)
    poly(d, scale(outer, 256, 430, 0.56, 0.60))

    save(img, "fire.png")


def spray():
    """A rattle can, with the spray coming off it.

    THE OLD ONE, PUT BACK. The 512-pixel redraw made a tidier can and a worse icon: the spray
    shrank to three dots of nine, twelve and sixteen pixels tucked against the cap, which
    vanish -- so what was left was a plain cylinder with a MID band across it, and a plain
    cylinder is a battery, a lighter, a drinks can, anything. The thing that says AEROSOL is
    the stuff coming OUT of it, and on the old one that was three fat discs thrown clear of
    the nozzle where they can be seen.

    Same geometry as make_icons_old.py drew it, which worked at 64 pixels and works at 128.
    """
    img, d = canvas()

    rrect(d, 166, 150, 346, 476, 30)
    rect(d, 206, 96, 306, 154)
    rrect(d, 186, 44, 326, 100, 18)

    # The label, cut out.
    rect(d, 196, 220, 316, 250, CLEAR)

    # And the spray, thrown clear of the nozzle where it can actually be seen.
    for cx, cy in ((398, 96), (438, 168), (372, 196)):
        disc(d, cx, cy, 26)

    save(img, "spray.png")


def cap():
    img, d = canvas()

    poly(d, arc(256, 296, 176, 180, 360, 40) + [(432, 296), (80, 296)])
    poly(d, [(256, 296), (476, 306), (476, 338), (256, 338), (80, 338), (80, 296)])
    disc(d, 256, 120, 16)

    stroke(d, [(256, 296), (176, 150)], 8, MID)
    stroke(d, [(256, 296), (336, 150)], 8, MID)
    stroke(d, [(256, 296), (256, 122)], 8, MID)

    save(img, "cap.png")


def gang_f():
    img, d = canvas()
    glyph(d, "F", 256, 262, 440, "UnifrakturCook-Bold.ttf")
    save(img, "gang_f.png")


ALL = [wifi, arrow_right, arrow_left, arrow_updown, arrow_leftright,
       reply, repost, heart, like, tick, drop, disc_,
       warning, locked, key, eyes, footfall, rank, people, pin, deal,
       socials, mobile, phone, megaphone, music, tattoo, health, scales, crown, skull, police,
       mask, bandana,
       garage, car, tow, bank, card, cash, money, crate, box, brick, stash, bed, dog, fire, spray,
       cap, gang_f]
