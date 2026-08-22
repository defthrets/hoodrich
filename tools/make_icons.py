# -*- coding: utf-8 -*-
#
# The three symbols GTA V does not have.
#
# There is no skull sprite anywhere in the game's HUD dictionaries -- "skull" returns nothing
# across every dump -- and no police badge either. CustomSprite loads a PNG off disk, so we
# draw our own rather than keep guessing at texture names that do not exist.
#
# Designed for ~20 device pixels tall. That is the whole constraint: at 20px a detailed skull
# is grey mush, so every shape here is big, blunt and high-contrast. Drawn at 8x and
# downsampled, which is what gives the edges their smoothness.

import math
import os
import sys

from PIL import Image, ImageDraw

OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "data", "icons")
S = 512          # working size
F = 8            # downsample factor -> 64px files
W = (255, 255, 255, 255)
CLEAR = (0, 0, 0, 0)


def bez(p0, p1, p2, n=32):
    """Quadratic Bezier, as a list of points."""
    out = []
    for i in range(n + 1):
        t = i / float(n)
        u = 1 - t
        out.append((u * u * p0[0] + 2 * u * t * p1[0] + t * t * p2[0],
                    u * u * p0[1] + 2 * u * t * p1[1] + t * t * p2[1]))
    return out


def ribbon(d, spine, w0, w1, fill):
    """
    A strip of varying thickness laid either side of a centreline.

    A curved part -- a magazine, a trigger guard -- drawn as a polygon by hand is a row of
    hand-placed points that never quite lie on a curve. This takes the line you actually mean
    and puts the thickness on afterwards.
    """
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


def ribbon_pts(spine, w0, w1, scale=1):
    """ribbon()'s outline, returned as points and scaled, for a supersampled canvas."""
    import math as _m
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

        n = _m.hypot(dx, dy) or 1.0
        nx, ny = -dy / n, dx / n
        left.append(((x + nx * half) * scale, (y + ny * half) * scale))
        right.append(((x - nx * half) * scale, (y - ny * half) * scale))

    return left + right[::-1]


def canvas():
    img = Image.new('RGBA', (S, S), CLEAR)
    return img, ImageDraw.Draw(img)


def save(img, name):
    if not os.path.isdir(OUT):
        os.makedirs(OUT)
    small = img.resize((S // F, S // F), Image.LANCZOS)
    p = os.path.join(OUT, name)
    small.save(p)
    print('  %-12s %dx%d  %d bytes' % (name, small.width, small.height, os.path.getsize(p)))


# ---------------------------------------------------------------------- skull
#
# Cranium and jaw as one silhouette, then the features punched back out to transparent so the
# tint colour shows through the holes rather than being painted on top of them. Two enormous
# eye sockets are what makes a 20px blob read as a skull; everything else is secondary.
def skull():
    img, d = canvas()

    # cranium
    d.ellipse([70, 40, 442, 390], fill=W)
    # cheeks squared off, so it is not simply a circle
    d.rounded_rectangle([100, 250, 412, 400], radius=60, fill=W)
    # jaw
    d.rounded_rectangle([150, 360, 362, 470], radius=44, fill=W)

    # eye sockets -- oversized on purpose
    d.ellipse([112, 150, 232, 286], fill=CLEAR)
    d.ellipse([280, 150, 400, 286], fill=CLEAR)

    # nose
    d.polygon([(256, 268), (300, 350), (212, 350)], fill=CLEAR)

    # the gap under the jaw, and two tooth divisions
    d.rectangle([150, 388, 362, 402], fill=CLEAR)
    for x in (222, 290):
        d.rectangle([x - 8, 396, x + 8, 470], fill=CLEAR)

    save(img, 'skull.png')


# --------------------------------------------------------------------- police
#
# A shield with a star knocked out of it. A badge outline alone is unreadable small -- it is
# just a blob -- so the star has to be big enough to survive the downsample. The first attempt
# had thin points and at 20px they closed up into a grey disc; these are deliberately fat.
def police():
    img, d = canvas()

    # shield
    d.polygon([(256, 20), (466, 92), (466, 272), (256, 492), (46, 272), (46, 92)], fill=W)

    # five-pointed star, punched out
    import math
    pts = []
    for i in range(10):
        ang = math.radians(-90 + i * 36)
        r = 170 if i % 2 == 0 else 88
        pts.append((256 + r * math.cos(ang), 250 + r * math.sin(ang)))
    d.polygon(pts, fill=CLEAR)

    save(img, 'police.png')


# ---------------------------------------------------------------------- heart
def heart():
    img, d = canvas()

    d.ellipse([64, 78, 280, 294], fill=W)
    d.ellipse([232, 78, 448, 294], fill=W)
    d.polygon([(78, 232), (434, 232), (256, 470)], fill=W)

    save(img, 'heart.png')


# ----------------------------------------------------------------------- feed
#
# The three under every post. All authored as ONE solid mass wherever possible -- an outline
# drawn at 512 becomes a 1px grey thread at 17 and disappears, so these are filled shapes with
# holes punched, the same trick as the skull.

def reply():
    """A speech bubble. Solid, with a tail on the lower left."""
    img, d = canvas()

    d.rounded_rectangle([56, 78, 456, 350], radius=76, fill=W)
    d.polygon([(128, 330), (128, 468), (250, 344)], fill=W)

    save(img, 'reply.png')


def repost():
    """
    Two arrows going opposite ways.

    Not the circular recycle glyph -- at this size the curve closes into a ring and reads as a
    letter O. Two straight bars with fat heads keeps the meaning.
    """
    img, d = canvas()

    # top bar, pointing right. The bars are deliberately fat: at 64px thick they came out two
    # pixels wide on screen and vanished into the background.
    d.rectangle([96, 120, 356, 228], fill=W)
    d.polygon([(330, 56), (330, 292), (486, 174)], fill=W)

    # bottom bar, pointing left
    d.rectangle([156, 284, 416, 392], fill=W)
    d.polygon([(182, 220), (182, 456), (26, 338)], fill=W)

    save(img, 'repost.png')


def like():
    """
    A hollow heart, so a like is not the same picture as the rep bar.

    It was byte-identical to heart.png -- the same drawing saved twice under two names, for
    two different meanings, in two different screens. A like is a thing you have not done
    yet, and an outline is what that looks like everywhere else on earth.
    """
    img, d = canvas()

    d.ellipse([64, 78, 280, 294], fill=W)
    d.ellipse([232, 78, 448, 294], fill=W)
    d.polygon([(78, 232), (434, 232), (256, 470)], fill=W)

    d.ellipse([116, 130, 268, 282], fill=CLEAR)
    d.ellipse([244, 130, 396, 282], fill=CLEAR)
    d.polygon([(132, 250), (380, 250), (256, 402)], fill=CLEAR)

    save(img, 'like.png')


def tick():
    """
    The verified badge, drawn as a shape rather than a dot.

    An OCTAGON, not a scalloped burst and not a circle: twelve lobes go to mush at this size,
    and a plain disc is indistinguishable from the accent dot it replaces, but eight flats
    still read as "not a dot". Rotated 22.5 degrees so a flat sits on top and it does not look
    like it is balancing on a point.

    The check is a HOLE, same trick as the skull. A check stroke laid on top would be a
    two-pixel scratch. The elbow gets its own disc because PIL mitres thick lines with a notch,
    and at this size the notch splits the check into two dashes.
    """
    import math
    img, d = canvas()

    pts = [(256 + 220 * math.cos(math.radians(22.5 + i * 45)),
            256 + 220 * math.sin(math.radians(22.5 + i * 45))) for i in range(8)]
    d.polygon(pts, fill=W)

    d.line([(150, 266), (226, 342)], fill=CLEAR, width=76)
    d.line([(226, 342), (368, 196)], fill=CLEAR, width=76)
    d.ellipse([226 - 38, 342 - 38, 226 + 38, 342 + 38], fill=CLEAR)

    save(img, 'tick.png')


# ------------------------------------------------------------------- product
#
# Three drugs have no sprite of their own and their fallbacks COLLIDE: Heroin and Ecstasy both
# land on shop_health_icon_a and Crack lands on the ammo icon, so on an install without the
# mp_specitem_* names two different drugs draw the same picture. These are the difference.

def crack():
    """
    A cut stone, which is what the rock actually looks like.

    It was an irregular six-sided lump with a facet knocked out of it, and at the size these
    draw an irregular lump is a blob -- it measured closer to the Ballas diamond than to
    anything in the drug set. A gem shape is the right idea anyway: a crown of facets over a
    point, symmetrical, so the silhouette is unmistakable even when it is twenty pixels tall.

    The facet lines are punched back out to transparent rather than painted on, so the tint
    colour shows through them and the stone reads as cut rather than as a solid diamond.
    """
    img, d = canvas()

    top, waist, tip = 120, 210, 470
    l, r = 56, 456
    il, ir = 148, 364

    # Crown above the girdle, pavilion below it, as one silhouette.
    d.polygon([(il, top), (ir, top), (r, waist), (256, tip), (l, waist)], fill=W)

    # The girdle, and the three crown facets, cut back out.
    d.line([(l + 14, waist), (r - 14, waist)], fill=CLEAR, width=16)
    d.line([(il, top), (il + 40, waist)], fill=CLEAR, width=14)
    d.line([(ir, top), (ir - 40, waist)], fill=CLEAR, width=14)

    # Two long pavilion facets running to the point.
    d.line([(l + 30, waist + 12), (256, tip - 10)], fill=CLEAR, width=14)
    d.line([(r - 30, waist + 12), (256, tip - 10)], fill=CLEAR, width=14)

    save(img, 'crack.png')


def pills():
    """
    Two capsules at opposing angles.

    A capsule is a rounded rectangle whose corner radius is half its height, which reads at any
    size. The dividing band is punched out at 70px -- about 2.2 rendered, the thinnest feature
    in this file proven to survive the downsample. Two of them, angled, so the pair cannot be
    mistaken for the single bar in repost.png.
    """
    img, d = canvas()

    for box, ang in (((70, 96, 400, 226), -22), ((112, 286, 442, 416), 18)):
        cap = Image.new('RGBA', (S, S), CLEAR)
        c = ImageDraw.Draw(cap)
        c.rounded_rectangle(list(box), radius=65, fill=W)
        mid = (box[0] + box[2]) // 2
        c.rectangle([mid - 22, box[1], mid + 22, box[3]], fill=CLEAR)
        img.alpha_composite(cap.rotate(ang, resample=Image.BICUBIC,
                                       center=(mid, (box[1] + box[3]) // 2)))
    save(img, 'pills.png')


def heroin():
    """
    A syringe on the diagonal -- and the diagonal IS the read, because nothing else in the set
    is a long thin thing at an angle.

    Every stroke is mass, and all of it is fat. The needle is a TAPER off the barrel rather
    than a line: a thin line is about a pixel rendered and would vanish, leaving a bar floating
    in space. The first version had graduation marks punched into the barrel and a slimmer
    body, and at the size this draws it came out as a faint diagonal scratch -- so the detail
    is gone and every part is thicker.
    """
    img, d = canvas()
    syr = Image.new('RGBA', (S, S), CLEAR)
    c = ImageDraw.Draw(syr)

    c.rounded_rectangle([116, 170, 386, 342], radius=30, fill=W)   # barrel
    c.rounded_rectangle([92, 122, 156, 390], radius=24, fill=W)    # finger flange
    c.rectangle([34, 218, 104, 294], fill=W)                        # plunger rod
    c.rounded_rectangle([2, 170, 58, 342], radius=22, fill=W)      # thumb rest
    c.polygon([(386, 196), (386, 316), (504, 256)], fill=W)        # needle, as a taper

    img.alpha_composite(syr.rotate(-32, resample=Image.BICUBIC, center=(256, 256)))
    save(img, 'heroin.png')


def megaphone():
    """
    Saying it where they can hear it. Marks the DISS section.

    A horn opening right, a grip hanging under it, and one arc of sound. At the size this
    draws the ARC is the only thing separating it from a plain wedge, so it is drawn far
    thicker than looks right at full size -- and the horn is a blunt wedge rather than a cone,
    because a knife edge vanishes on the downsample.
    """
    img, d = canvas()

    d.polygon([(170, 202), (170, 310), (348, 428), (348, 84)], fill=W)      # the horn
    d.rounded_rectangle([322, 84, 372, 428], radius=24, fill=W)             # the mouth
    d.rounded_rectangle([116, 212, 192, 300], radius=26, fill=W)            # the throat
    d.polygon([(126, 288), (198, 288), (176, 452), (110, 452)], fill=W)     # the grip

    d.arc([300, 26, 502, 486], start=-52, end=52, fill=W, width=56)

    # A bite where the grip meets the horn, so the two read as separate parts of one thing
    # rather than a single blob.
    d.polygon([(188, 296), (240, 296), (214, 332)], fill=CLEAR)

    save(img, 'megaphone.png')


print('writing to %s' % OUT)


# ============================================================ the wedge and row set
#
# One per named Icon in Icons.cs, so nothing on a wheel or a dialogue row is borrowing a shop
# sprite that means something else. Same rules throughout: solid mass, detail punched out to
# transparent, nothing thinner than about seventy units at 512 or it closes up at render size.

def weed():
    """A five-point leaf. The only drug in the set with a shape everybody already knows."""
    img, d = canvas()
    import math
    # A ROLLED JOINT, not a leaf.
    #
    # A leaf is made of points, and points are the first thing the downsample takes -- three
    # fat leaflets still came out as an arrowhead. A joint is a fat diagonal bar with a burning
    # tip and a band round it, which is three blunt shapes and survives at any size. It is also
    # paraphernalia, which is what the rest of the drug set now is.
    j = Image.new('RGBA', (S, S), CLEAR)
    c = ImageDraw.Draw(j)

    c.polygon([(40, 300), (108, 232), (430, 250), (430, 330)], fill=W)   # the body, tapered
    c.rounded_rectangle([300, 244, 348, 336], radius=14, fill=CLEAR)      # the band
    c.ellipse([392, 232, 492, 332], fill=W)                              # the cherry

    img.alpha_composite(j.rotate(24, resample=Image.BICUBIC, center=(256, 256)))

    # Smoke off it: two blobs, because a curl is a thread.
    d.ellipse([300, 90, 372, 162], fill=W)
    d.ellipse([372, 40, 424, 92], fill=W)
    save(img, 'weed.png')


def coke():
    """
    A razor blade, and a line off it.

    Not a brick. The drug set reads as PARAPHERNALIA -- the needle, the pipe, the blade -- which
    is a far stronger idea than three differently shaped parcels, and it is the difference
    between six icons you can tell apart and six you have to learn.

    The blade sits on the skew because a horizontal bar is a horizontal bar; angled, with the
    two mounting slots punched out, it is a razor and nothing else in the set is close.
    """
    img, d = canvas()

    b = Image.new('RGBA', (S, S), CLEAR)
    c = ImageDraw.Draw(b)

    c.polygon([(36, 178), (476, 178), (440, 292), (72, 292)], fill=W)
    c.rectangle([36, 178, 476, 216], fill=W)
    c.ellipse([158, 196, 214, 252], fill=CLEAR)
    c.ellipse([298, 196, 354, 252], fill=CLEAR)

    img.alpha_composite(b.rotate(-16, resample=Image.BICUBIC, center=(256, 256)))

    # The line it has just been used on.
    d.rounded_rectangle([104, 388, 408, 428], radius=20, fill=W)

    save(img, 'coke.png')


def meth():
    """
    A pipe. Bulb on one end of a straight stem.

    The BULB is what separates it from the syringe, which is a taper on the end of a barrel --
    at this size a circle and a point are the two shapes that can never be confused, whereas
    two long thin things at an angle are one picture drawn twice.

    Hollowed out, because a solid bulb is a lollipop.
    """
    img, d = canvas()

    b = Image.new('RGBA', (S, S), CLEAR)
    c = ImageDraw.Draw(b)

    c.ellipse([40, 190, 272, 422], fill=W)
    c.ellipse([106, 256, 206, 356], fill=CLEAR)
    c.rounded_rectangle([236, 266, 490, 346], radius=40, fill=W)
    c.ellipse([150, 168, 212, 230], fill=CLEAR)

    img.alpha_composite(b.rotate(18, resample=Image.BICUBIC, center=(256, 256)))

    save(img, 'meth.png')


def money():
    """A folded roll, seen end on. Rings rather than notes -- notes vanish at this size."""
    img, d = canvas()
    # A STACK, so it is not the single note that Cash already is. As a roll it read as a
    # lozenge with a dot in it, which at this size is the same picture as the note.
    for i, y in enumerate((300, 214, 128)):
        x = 60 + i * 34
        d.rounded_rectangle([x, y, x + 330, y + 96], radius=20, fill=W)
        d.rounded_rectangle([x + 16, y + 16, x + 314, y + 80], radius=12, fill=CLEAR)
        d.rounded_rectangle([x + 30, y + 26, x + 300, y + 70], radius=10, fill=W)
    save(img, 'money.png')


def cash():
    """
    A note with a dollar sign cut out of it.

    The sign is the whole point. A blank note with a disc on it is a rounded rectangle with a
    hole, which at this size is a button -- the S and its bar are what makes it money, and they
    are punched rather than drawn so the row colour shows through them.

    The strokes are 46 units, which is under this file's usual floor. It is safe here because
    they are HOLES in a solid mass rather than marks on empty space: a hole that closes up
    leaves a slightly plainer note, whereas a stroke that closes up leaves nothing.
    """
    img, d = canvas()

    d.rounded_rectangle([30, 140, 482, 372], radius=26, fill=W)
    d.rounded_rectangle([54, 164, 458, 348], radius=18, fill=CLEAR)
    d.rounded_rectangle([70, 180, 442, 332], radius=12, fill=W)

    # The S, as three bars and two joins -- a drawn curve is a grey thread at this size.
    d.rounded_rectangle([196, 196, 316, 242], radius=20, fill=CLEAR)
    d.rounded_rectangle([196, 234, 242, 286], radius=16, fill=CLEAR)
    d.rounded_rectangle([196, 268, 316, 314], radius=20, fill=CLEAR)
    d.rounded_rectangle([270, 224, 316, 278], radius=16, fill=CLEAR)

    # And the bar through it.
    d.rectangle([238, 168, 274, 344], fill=CLEAR)

    save(img, 'cash.png')


def guns():
    """
    A STOCKLESS AK -- the pistol-grip build -- lying flat, muzzle right.

    Third drawing of this icon and the second reference. The one before was a full-stock AK on
    the diagonal, which matched the reference it was drawn against; this reference has no stock
    and lies horizontal, so it is a redraw rather than a rotate.

    It has to survive being a SILHOUETTE. The reference is two-tone -- orange furniture on black
    metal -- and every icon here is a white mask tinted at draw time, so the handguard and grip
    stop being a separate colour and join one outline. What carries it instead is the profile:
    the banana magazine, the pistol grip, a long thin barrel, and the back of the receiver
    simply ending where a stock would be.

    ONE MASS AT THE FRONT. The first attempt at this pose drew the gas tube, the barrel and the
    receiver as three separate bars with daylight between them, and a thin straight magazine --
    which together read as an MP5. Everything from the receiver forward now overlaps its
    neighbour so there is no seam, and the top line runs unbroken from the rear sight to the
    front sight, which is what the reference does.
    """
    cw, ch = 1080, 620
    ss = 4
    big = Image.new('RGBA', (cw * ss, ch * ss), CLEAR)
    d = ImageDraw.Draw(big)

    ax = 250.0

    def P(pts):
        d.polygon([(x * ss, y * ss) for x, y in pts], fill=W)

    def R(x0, y0, x1, y1):
        d.rectangle([x0 * ss, y0 * ss, x1 * ss, y1 * ss], fill=W)

    # ---- receiver, and the back of it simply ending ----------------------
    R(70, ax - 56, 400, ax + 48)
    P([(70, ax - 56), (36, ax - 40), (36, ax + 30), (70, ax + 48)])

    # Top cover and rear sight, level with the handguard so the top line is unbroken.
    R(150, ax - 84, 330, ax - 50)
    R(206, ax - 104, 272, ax - 80)

    # ---- pistol grip, where a stock is not -------------------------------
    P([(146, ax + 44), (236, ax + 44), (212, ax + 244), (140, ax + 244), (124, ax + 150)])

    # ---- trigger guard ----------------------------------------------------
    d.polygon(ribbon_pts(bez((242, ax + 50), (256, ax + 122), (330, ax + 108)), 22, 22, ss),
              fill=W)

    # ---- the magazine, fat and curving FORWARD ----------------------------
    d.polygon(ribbon_pts(bez((300, ax + 40), (352, ax + 168), (500, ax + 236)), 114, 86, ss),
              fill=W)

    # ---- the front half, as one mass --------------------------------------
    hg0, hg1 = 385, 635
    R(hg0, ax - 84, hg1, ax - 30)                       # gas tube, flush with the top line
    P([(hg0, ax - 30), (hg1, ax - 30), (hg1 - 26, ax + 44), (hg0 + 6, ax + 44)])
    P([(hg1 - 34, ax - 84), (hg1 + 6, ax - 84), (hg1 + 6, ax - 24), (hg1 - 34, ax - 24)])

    b0, b1 = hg1 - 10, hg1 + 210
    R(b0, ax - 20, b1, ax + 20)

    # ---- front sight, sat on the barrel rather than floating past it -------
    P([(b1 - 62, ax - 20), (b1 - 26, ax - 20), (b1 - 26, ax - 96),
       (b1 - 44, ax - 112), (b1 - 62, ax - 96)])
    R(b1 - 24, ax - 34, b1, ax + 34)

    art = big.resize((cw, ch), Image.LANCZOS)
    art = art.crop(art.getbbox())

    out = Image.new('RGBA', (S, S), CLEAR)
    k = min((S - 28) / float(art.width), (S - 28) / float(art.height))
    art = art.resize((max(1, int(art.width * k)), max(1, int(art.height * k))), Image.LANCZOS)
    out.alpha_composite(art, ((S - art.width) // 2, (S - art.height) // 2))

    save(out, 'guns.png')


def leaf():
    """
    A seven-blade cannabis leaf, for the Dealing wedge.

    There is a note on weed() above from an earlier attempt at one: "A leaf is made of points,
    and points are the first thing the downsample takes -- three fat leaflets still came out as
    an arrowhead." That was true, and it was THREE leaflets drawn for a twenty-pixel icon. The
    wheel actually draws these at about seventy-eight, and what makes a cannabis leaf
    recognisable is not the serration -- it is the seven-blade fan. Seven blades hold that
    shape down to about twenty-four pixels; the teeth are allowed to blur away and it still
    reads, which is the opposite trade from the one that failed.

    The outline is built as POINTS and rotated arithmetically rather than drawn upright and
    rotated as a tile. A rotated tile with expand=True does not keep its root at the bottom
    centre, so compositing as though it does puts every blade slightly wrong -- the first pass
    at this welded the outer four into a slab across the bottom.
    """
    big = Image.new('RGBA', (S * 4, S * 4), CLEAR)
    d = ImageDraw.Draw(big)

    ox, oy = S * 4 * 0.5, S * 4 * 0.80          # the root, low in the frame
    spread = 26.0
    lengths = [290, 370, 425, 465, 425, 370, 290]
    fats = [44, 52, 58, 62, 58, 52, 44]

    for i in range(len(lengths)):
        ang = math.radians((i - (len(lengths) - 1) / 2.0) * spread)
        ca, sa = math.cos(ang), math.sin(ang)

        # Half-width follows sin(pi * t^0.55): narrow at the stem, widest a third of the way
        # up, tapering to a point. Fatter reads as a petal, thinner as a spider's leg.
        left, right = [], []
        teeth = 7 + (i % 2)

        for k in range(121):
            t = k / 120.0
            w = fats[i] * 4 * math.sin(math.pi * (t ** 0.55)) * (1.0 - 0.10 * t)

            if 0.10 < t < 0.94:
                w *= 1.0 - 0.17 * (0.5 + 0.5 * math.cos(t * teeth * 2 * math.pi))

            y = -lengths[i] * 4 * t
            left.append((-w, y))
            right.append((w, y))

        pts = left + right[::-1]
        d.polygon([(ox + x * ca - y * sa, oy + x * sa + y * ca) for x, y in pts], fill=W)

    # A short stem, so it is a leaf rather than a firework.
    d.polygon([(ox - 52, oy - 24), (ox + 52, oy - 24), (ox + 28, oy + 400), (ox - 28, oy + 400)],
              fill=W)

    art = big.resize((S, S), Image.LANCZOS)
    box = art.getbbox()
    art = art.crop(box)

    out = Image.new('RGBA', (S, S), CLEAR)
    k = min((S - 24) / float(art.width), (S - 24) / float(art.height))
    art = art.resize((int(art.width * k), int(art.height * k)), Image.LANCZOS)
    out.alpha_composite(art, ((S - art.width) // 2, (S - art.height) // 2))

    save(out, 'leaf.png')


def mobile():
    """
    A plain mobile phone, for the socials.

    Not phone.png. That one has a message bubble coming off it because it means TEXT SOMEBODY
    -- it is the plug's icon. This is the feed itself, so it is the handset on its own with
    the screen full of lines, and the two read as related rather than as the same thing.

    The tattoo machine that used to sit here was a good drawing of the wrong object.
    """
    img, d = canvas()

    d.rounded_rectangle([146, 44, 366, 486], radius=44, fill=W)
    d.rounded_rectangle([180, 108, 332, 404], radius=12, fill=CLEAR)

    d.rounded_rectangle([228, 70, 284, 84], radius=7, fill=CLEAR)
    d.ellipse([232, 428, 280, 476], fill=CLEAR)

    # A feed on the screen: an avatar and its line, three times down.
    for i in range(3):
        y = 130 + i * 92
        d.ellipse([196, y, 244, y + 48], fill=W)
        d.rounded_rectangle([256, y + 6, 318, y + 22], radius=8, fill=W)
        d.rounded_rectangle([256, y + 30, 300, y + 44], radius=7, fill=W)

    save(img, 'mobile.png')



# ---------------------------------------------------------------- feed glyphs
#
# Three pictures the feed needs that nothing else in the mod already draws. They render at
# about twenty-four pixels -- a fifth of a wheel icon -- so every shape here is blunter than it
# would otherwise be: no outlines, no thin strokes, and nothing that relies on a detail.

def eyes():
    """Two eyes, looking sideways. The pupils are the whole read, so they are enormous."""
    img, d = canvas()

    for cx in (168, 344):
        d.ellipse([cx - 92, 176, cx + 92, 336], fill=W)          # the almond
        d.ellipse([cx - 44, 212, cx + 44, 300], fill=CLEAR)      # punched out
        d.ellipse([cx - 30, 226, cx + 30, 286], fill=W)          # the pupil

    save(img, 'eyes.png')


def cap():
    """A baseball cap in profile -- a dome and a brim. Means somebody is lying."""
    img, d = canvas()

    d.pieslice([128, 132, 384, 388], 180, 360, fill=W)           # the crown
    d.rectangle([128, 250, 384, 300], fill=W)
    d.polygon([(360, 250), (470, 262), (470, 306), (356, 300)], fill=W)   # the brim
    d.ellipse([234, 108, 278, 152], fill=W)                      # the button

    save(img, 'cap.png')


def crown():
    """Three points and a band. Nothing else in the set is this shape."""
    img, d = canvas()

    d.polygon([(96, 372), (96, 168), (176, 258), (256, 132), (336, 258), (416, 168), (416, 372)],
              fill=W)
    d.rectangle([80, 372, 432, 436], fill=W)

    for x in (176, 256, 336):
        d.ellipse([x - 26, 130, x + 26, 182], fill=W)

    save(img, 'crown.png')


def ammo():
    """Three rounds standing up. Fewer, fatter shapes than a real magazine."""
    img, d = canvas()
    for x in (96, 220, 344):
        d.rounded_rectangle([x, 200, x + 72, 452], radius=18, fill=W)
        d.polygon([(x, 200), (x + 72, 200), (x + 36, 76)], fill=W)
    save(img, 'ammo.png')


def garage():
    """A roller door under a pitched roof. Slats punched, not drawn."""
    img, d = canvas()
    d.polygon([(40, 230), (256, 70), (472, 230)], fill=W)
    d.rectangle([90, 230, 422, 452], fill=W)
    for y in (280, 340, 400):
        d.rectangle([130, y, 382, y + 26], fill=CLEAR)
    save(img, 'garage.png')


def mask():
    """A balaclava: a rounded head with two eye holes and a mouth punched out."""
    img, d = canvas()
    d.rounded_rectangle([110, 70, 402, 450], radius=140, fill=W)
    d.ellipse([150, 200, 236, 268], fill=CLEAR)
    d.ellipse([276, 200, 362, 268], fill=CLEAR)
    d.rounded_rectangle([190, 330, 322, 396], radius=28, fill=CLEAR)
    save(img, 'mask.png')


def health():
    """A cross. Nothing else needed and nothing else survives this small."""
    img, d = canvas()
    d.rectangle([196, 60, 316, 452], fill=W)
    d.rectangle([60, 196, 452, 316], fill=W)
    save(img, 'health.png')


def tattoo():
    """A needle machine, on the diagonal, same reasoning as the syringe."""
    img, d = canvas()
    m = Image.new('RGBA', (S, S), CLEAR)
    c = ImageDraw.Draw(m)
    # Upright, NOT on the diagonal. The diagonal is the syringe's read, and two diagonal
    # needles in one set is one picture used twice.
    c.rounded_rectangle([164, 60, 348, 250], radius=30, fill=W)
    c.rounded_rectangle([196, 96, 316, 170], radius=18, fill=CLEAR)
    c.rounded_rectangle([214, 250, 298, 360], radius=22, fill=W)
    c.polygon([(236, 360), (276, 360), (256, 486)], fill=W)
    c.rounded_rectangle([120, 190, 180, 250], radius=18, fill=W)
    img.alpha_composite(m)
    save(img, 'tattoo.png')


def stash():
    """A duffle: a fat rounded bag with a strap arcing over it."""
    img, d = canvas()
    d.rounded_rectangle([40, 200, 472, 420], radius=96, fill=W)
    d.arc([150, 90, 362, 300], start=180, end=360, fill=W, width=54)
    d.rectangle([236, 200, 276, 420], fill=CLEAR)
    save(img, 'stash.png')


def warning():
    """A triangle with a bar and a dot punched out. The house alert shape."""
    img, d = canvas()
    d.polygon([(256, 40), (492, 452), (20, 452)], fill=W)
    d.rounded_rectangle([222, 180, 290, 330], radius=30, fill=CLEAR)
    d.ellipse([220, 356, 292, 428], fill=CLEAR)
    save(img, 'warning.png')


def locked():
    """A padlock. The shackle is a fat arc, the body a slab, the keyhole a hole."""
    img, d = canvas()
    d.arc([146, 60, 366, 300], start=180, end=360, fill=W, width=58)
    d.rounded_rectangle([90, 236, 422, 460], radius=40, fill=W)
    d.ellipse([222, 300, 290, 368], fill=CLEAR)
    d.rectangle([238, 340, 274, 410], fill=CLEAR)
    save(img, 'locked.png')


# ============================================================ the sets
#
# One emblem each, tinted by the gang colour at the call site.
#
# Geometry rather than heraldry, deliberately. Nine crests all drawn as crests are nine grey
# smudges at the size these render; nine clearly DIFFERENT silhouettes can be told apart
# instantly, before you have learned which is which.

def gang_families():
    """
    Three bars stacked, the middle one long -- a set's own mark rather than a picture.

    It was a tree, on the strength of Grove Street. At the size these draw a tree is a ball on
    a stick, which is a lollipop, a balloon or a streetlight before it is ever a tree. Bars
    cannot be mistaken for anything and they are the only stacked shape in the nine.
    """
    img, d = canvas()
    d.rounded_rectangle([116, 106, 396, 186], radius=26, fill=W)
    d.rounded_rectangle([40, 216, 472, 296], radius=26, fill=W)
    d.rounded_rectangle([116, 326, 396, 406], radius=26, fill=W)
    save(img, 'gang_families.png')


def gang_ballas():
    """A cut diamond. Four-sided, and unmistakably not a circle."""
    img, d = canvas()
    d.polygon([(256, 30), (482, 256), (256, 482), (30, 256)], fill=W)
    d.polygon([(256, 150), (362, 256), (256, 362), (150, 256)], fill=CLEAR)
    save(img, 'gang_ballas.png')


def gang_vagos():
    """A sun. Rays fat enough to survive, round a solid centre."""
    import math
    img, d = canvas()
    for i in range(8):
        a = math.radians(i * 45)
        d.polygon([(256 + 60 * math.cos(a + 0.34), 256 + 60 * math.sin(a + 0.34)),
                   (256 + 60 * math.cos(a - 0.34), 256 + 60 * math.sin(a - 0.34)),
                   (256 + 240 * math.cos(a), 256 + 240 * math.sin(a))], fill=W)
    d.ellipse([146, 146, 366, 366], fill=W)
    save(img, 'gang_vagos.png')


def gang_aztecas_OLD():
    """A step pyramid. Three tiers, the fewest that still reads as steps."""
    img, d = canvas()
    d.rectangle([40, 372, 472, 462], fill=W)
    d.rectangle([104, 262, 408, 352], fill=W)
    d.rectangle([168, 152, 344, 242], fill=W)
    d.rectangle([224, 60, 288, 132], fill=W)
    save(img, 'gang_aztecas.png')


def gang_marabunta():
    """Three dots in a triangle. Nothing else in the set is dots."""
    img, d = canvas()
    for cx, cy in ((256, 110), (140, 340), (372, 340)):
        d.ellipse([cx - 84, cy - 84, cx + 84, cy + 84], fill=W)
    save(img, 'gang_marabunta.png')


def gang_lost():
    """A pair of wings. Two swept blocks and no feathers -- feathers are mush."""
    img, d = canvas()
    # Two chevrons, one above the other, pointing down.
    #
    # As swept wings either side of a hub it flattened into a lozenge with a dot in it. A
    # chevron holds its angle however small it gets, and two of them is a rank flash, which is
    # the right kind of idea for a club.
    for top in (96, 268):
        d.polygon([(40, top), (256, top + 150), (472, top),
                   (472, top + 86), (256, top + 236), (40, top + 86)], fill=W)
    save(img, 'gang_lost.png')


def gang_triads_OLD():
    """A coin with a square hole. The most specific silhouette of the nine."""
    img, d = canvas()
    d.ellipse([46, 46, 466, 466], fill=W)
    d.rectangle([186, 186, 326, 326], fill=CLEAR)
    save(img, 'gang_triads.png')


def gang_armenians():
    """Two peaks. Ararat, and the only landscape in the set."""
    img, d = canvas()
    d.polygon([(20, 452), (196, 130), (300, 300), (350, 220), (492, 452)], fill=W)
    d.polygon([(150, 240), (196, 176), (242, 240)], fill=CLEAR)
    save(img, 'gang_armenians.png')


def gang_koreans():
    """Three slashes. Nothing else in the set is a stripe."""
    img, d = canvas()
    for x in (60, 200, 340):
        d.polygon([(x, 452), (x + 90, 452), (x + 172, 60), (x + 82, 60)], fill=W)
    save(img, 'gang_koreans.png')


def gang_aztecas():
    """
    A stepped pyramid, drawn as steps rather than as a triangle.

    It was a solid triangle, which measured within a hair of the warning sign -- two icons
    that mean completely different things and read identically at twenty pixels. Cutting the
    silhouette into four tiers gives it a staircase edge nothing else in the mod has, and it
    is a truer pyramid besides.
    """
    img, d = canvas()

    tiers = [(196, 316, 96, 172), (150, 362, 172, 248), (104, 408, 248, 324), (58, 454, 324, 400)]
    for x0, x1, y0, y1 in tiers:
        d.rectangle([x0, y0, x1, y1], fill=W)

    # A notch out of each tread, so the steps stay separate when it is downsampled.
    for _, _, y0, _ in tiers[1:]:
        d.rectangle([236, y0 - 6, 276, y0 + 6], fill=CLEAR)

    save(img, 'gang_aztecas.png')


def gang_triads():
    """
    A hooked bar, not a coin.

    The coin was a filled disc with a square hole, and a filled disc is exactly what an icon
    that failed to load looks like -- it measured closest of anything in the set to the
    verified tick. This is two heavy strokes meeting at a right angle with a hook on the end:
    all corners, no curve, and the only right angle in the nine.
    """
    img, d = canvas()

    d.rectangle([70, 96, 442, 176], fill=W)          # the head
    d.rectangle([216, 96, 296, 400], fill=W)         # the stem
    d.polygon([(216, 400), (296, 400), (296, 456), (96, 456), (96, 376), (216, 376)], fill=W)

    save(img, 'gang_triads.png')


def footfall():
    """
    Two footprints, for how busy a pavement is.

    Foot traffic was borrowing heart.png, which is the likes mark on the feed and the bar
    icon on the readout -- so the same heart meant "people like this" in one place and "people
    walk past here" in another. Two prints, offset the way a stride is, and nothing else in
    the set is a pair of soft shapes at an angle.
    """
    img, d = canvas()

    for ox, oy in ((36, 40), (232, 150)):
        d.ellipse([ox, oy, ox + 176, oy + 210], fill=W)          # the sole
        d.ellipse([ox + 22, oy + 216, ox + 74, oy + 268], fill=W)  # toes
        d.ellipse([ox + 88, oy + 222, ox + 134, oy + 264], fill=W)

    save(img, 'footfall.png')


def rank():
    """Three chevrons, stacked. A rank flash, which is what the ladder is."""
    img, d = canvas()

    for top in (70, 210, 350):
        d.polygon([(256, top), (452, top + 96), (452, top + 148), (256, top + 52),
                   (60, top + 148), (60, top + 96)], fill=W)

    save(img, 'rank.png')


def people():
    """Three heads and shoulders. Followers, and whoever is stood around with you."""
    img, d = canvas()

    d.ellipse([176, 60, 336, 220], fill=W)
    d.pieslice([120, 236, 392, 500], 180, 360, fill=W)

    for cx in (72, 440):
        d.ellipse([cx - 66, 140, cx + 66, 272], fill=W)
        d.pieslice([cx - 108, 286, cx + 108, 490], 180, 360, fill=W)

    save(img, 'people.png')


def pin():
    """A map pin. Where you are, where he is, where the block is."""
    img, d = canvas()

    d.ellipse([116, 40, 396, 320], fill=W)
    d.polygon([(160, 268), (352, 268), (256, 476)], fill=W)
    d.ellipse([206, 130, 306, 230], fill=CLEAR)

    save(img, 'pin.png')


def deal():
    """
    Two arms clasped, coming in from opposite corners.

    It was two hands meeting end to end across the middle, which is a horizontal bar with
    dots on it -- 0.094 from the xanax bar, the same distance that had the Triad coin and the
    verified tick reading as one another. Angled in from the corners it makes a diagonal
    cross nothing else in the set has, and the clasp in the middle is the only part that has
    to survive the downsample.
    """
    img, d = canvas()

    # Forearms, lower-left and lower-right, meeting high in the middle.
    d.polygon([(20, 440), (128, 470), (330, 176), (250, 120)], fill=W)
    d.polygon([(492, 440), (384, 470), (182, 176), (262, 120)], fill=W)

    # The clasp: one block across the join, with a gap so two arms still read as two.
    d.rounded_rectangle([176, 168, 336, 300], radius=40, fill=W)
    d.polygon([(214, 196), (298, 196), (256, 262)], fill=CLEAR)

    save(img, 'deal.png')


def crate():
    """A shipping container. The port, and anything that arrives by the pallet."""
    img, d = canvas()

    d.rectangle([44, 130, 468, 400], fill=W)
    for x in range(96, 460, 62):
        d.rectangle([x, 150, x + 20, 380], fill=CLEAR)
    d.rectangle([44, 130, 468, 154], fill=W)

    save(img, 'crate.png')


def box():
    """An open carton. Room left, and room you have run out of."""
    img, d = canvas()

    d.polygon([(60, 200), (256, 120), (452, 200), (452, 430), (256, 480), (60, 430)], fill=W)
    d.polygon([(96, 214), (256, 152), (416, 214), (256, 276)], fill=CLEAR)
    d.rectangle([236, 250, 276, 400], fill=CLEAR)

    save(img, 'box.png')


def phone():
    """
    A phone with a message coming off it, because texting the plug is what it means.

    The first one was a bare handset outline -- a rounded rectangle with a hole in it, which
    at twenty pixels is a rounded rectangle with a hole in it. The screen is filled now rather
    than punched out, so the shape reads as a phone rather than as a frame, and a speech bubble
    lifts off the corner: that is the half that says TEXT rather than merely PHONE, and it
    breaks the silhouette so it cannot be mistaken for the door, the box or the crate.
    """
    img, d = canvas()

    # The body, and a screen that is part of the phone rather than a hole in it.
    d.rounded_rectangle([120, 96, 330, 486], radius=38, fill=W)
    d.rounded_rectangle([152, 150, 298, 396], radius=10, fill=CLEAR)

    # Earpiece and button, so the top and bottom are not identical.
    d.rounded_rectangle([196, 120, 254, 134], radius=7, fill=CLEAR)
    d.ellipse([203, 420, 247, 464], fill=CLEAR)

    # Three lines of a message on the screen.
    for i, w in enumerate((104, 84, 62)):
        d.rounded_rectangle([172, 186 + i * 48, 172 + w, 208 + i * 48], radius=11, fill=W)

    # The bubble coming off it, with a tail pointing back at the phone.
    d.rounded_rectangle([300, 40, 500, 190], radius=44, fill=W)
    d.polygon([(330, 176), (392, 176), (322, 246)], fill=W)
    d.ellipse([300, 40, 500, 190], outline=CLEAR)

    for i, cx in enumerate((352, 400, 448)):
        d.ellipse([cx - 17, 98, cx + 17, 132], fill=CLEAR)

    save(img, 'phone.png')


def spray():
    """A rattle can, for the tag runs."""
    img, d = canvas()

    d.rounded_rectangle([166, 150, 346, 476], radius=30, fill=W)
    d.rectangle([206, 96, 306, 154], fill=W)
    d.rounded_rectangle([186, 44, 326, 100], radius=18, fill=W)
    d.rectangle([196, 220, 316, 250], fill=CLEAR)

    for cx, cy in ((398, 96), (438, 168), (372, 196)):
        d.ellipse([cx - 26, cy - 26, cx + 26, cy + 26], fill=W)

    save(img, 'spray.png')


def fire():
    """A flame. One job ends with a car going up and nothing else in the set is a flame."""
    img, d = canvas()

    d.polygon([(256, 30), (392, 216), (356, 200), (410, 340), (256, 486),
               (102, 340), (156, 200), (120, 216)], fill=W)
    d.polygon([(256, 250), (322, 372), (256, 452), (190, 372)], fill=CLEAR)

    save(img, 'fire.png')


def car():
    """A car from the side. The job car, the whip, the one you are about to burn."""
    img, d = canvas()

    d.polygon([(40, 330), (96, 244), (176, 176), (338, 176), (424, 244), (472, 330)], fill=W)
    d.rectangle([40, 300, 472, 366], fill=W)
    d.polygon([(180, 216), (330, 216), (392, 288), (140, 288)], fill=CLEAR)
    d.rectangle([246, 210, 268, 292], fill=CLEAR)

    for cx in (150, 362):
        d.ellipse([cx - 62, 336, cx + 62, 460], fill=W)
        d.ellipse([cx - 24, 374, cx + 24, 422], fill=CLEAR)

    save(img, 'car.png')


def scales():
    """A balance. Weight, and how far you have stepped on it."""
    img, d = canvas()

    d.rectangle([236, 90, 276, 420], fill=W)
    d.rectangle([80, 132, 432, 172], fill=W)
    d.rectangle([146, 420, 366, 468], fill=W)
    d.ellipse([226, 56, 286, 116], fill=W)

    for cx in (110, 402):
        d.polygon([(cx - 86, 214), (cx + 86, 214), (cx + 44, 300), (cx - 44, 300)], fill=W)
        d.rectangle([cx - 8, 168, cx + 8, 216], fill=W)

    save(img, 'scales.png')


def dog():
    """Chop. A head with the ears up, which is the whole silhouette anybody needs."""
    img, d = canvas()

    d.polygon([(96, 60), (196, 210), (76, 240)], fill=W)
    d.polygon([(416, 60), (316, 210), (436, 240)], fill=W)
    d.rounded_rectangle([112, 160, 400, 380], radius=76, fill=W)
    d.rounded_rectangle([186, 330, 326, 452], radius=48, fill=W)

    d.ellipse([170, 236, 214, 280], fill=CLEAR)
    d.ellipse([298, 236, 342, 280], fill=CLEAR)
    d.ellipse([226, 372, 286, 420], fill=CLEAR)

    save(img, 'dog.png')


def bed():
    """A bed. The one at the stash house, which is how a day ends."""
    img, d = canvas()

    d.rectangle([44, 300, 468, 356], fill=W)
    d.rectangle([44, 300, 96, 452], fill=W)
    d.rectangle([416, 356, 468, 452], fill=W)
    d.rounded_rectangle([106, 236, 236, 300], radius=26, fill=W)
    d.polygon([(246, 300), (416, 300), (416, 236), (300, 236)], fill=W)

    save(img, 'bed.png')


def music():
    """A note, for the boombox on the corner."""
    img, d = canvas()

    d.rectangle([230, 60, 274, 400], fill=W)
    d.polygon([(230, 60), (430, 108), (430, 200), (230, 152)], fill=W)
    d.ellipse([110, 344, 274, 476], fill=W)

    save(img, 'music.png')


def key():
    """A key. Getting put on, and the door it opens."""
    img, d = canvas()

    d.ellipse([46, 166, 246, 366], fill=W)
    d.ellipse([106, 226, 186, 306], fill=CLEAR)
    d.rectangle([216, 236, 470, 296], fill=W)
    d.rectangle([388, 296, 428, 372], fill=W)
    d.rectangle([444, 296, 470, 352], fill=W)

    save(img, 'key.png')


# ======================================================================= more product
#
# A spread to pick from rather than one drawing per drug. Every one of these is a DIFFERENT
# silhouette from every other -- a cup, a sheet, a dome, a bar, a slab, a bear, a pen, a
# pouch, a bottle, a patch, a fat cylinder, a taped block, a shard cluster, a beaker, a pod.
# That is the whole design rule at twenty pixels: shape first, detail never.


def lean():
    """A double cup. Two rims stacked is the whole tell, and nothing else here has a straw."""
    img, d = canvas()

    d.polygon([(120, 150), (392, 150), (350, 470), (162, 470)], fill=W)   # the cup
    d.rectangle([98, 96, 414, 152], fill=W)                              # outer rim
    d.rectangle([120, 168, 392, 186], fill=CLEAR)                        # the second cup's rim
    d.polygon([(300, 30), (348, 30), (300, 150), (256, 150)], fill=W)    # straw

    save(img, 'lean.png')


def acid():
    """A sheet of blotter. A punched grid, which nothing else in the set is."""
    img, d = canvas()

    d.rounded_rectangle([64, 64, 448, 448], radius=18, fill=W)

    for gx in (64, 192, 320):
        for gy in (64, 192, 320):
            d.rectangle([gx + 108, gy + 20, gx + 124, gy + 172], fill=CLEAR)
            d.rectangle([gx + 20, gy + 108, gx + 172, gy + 124], fill=CLEAR)

    save(img, 'acid.png')


def shrooms():
    """A cap and a stalk. The only dome on a stem anywhere in the mod."""
    img, d = canvas()

    d.pieslice([56, 96, 456, 440], 180, 360, fill=W)
    d.rectangle([56, 262, 456, 292], fill=W)
    d.polygon([(206, 286), (306, 286), (286, 470), (226, 470)], fill=W)

    for cx, cy, r in ((160, 190, 34), (256, 150, 28), (344, 196, 30)):
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=CLEAR)

    save(img, 'shrooms.png')


def xanax():
    """A bar, scored. Long and low, which no other product here is."""
    img, d = canvas()

    # Notched through the OUTLINE, not scored across the face.
    #
    # Scored, it was a plain rounded bar with three hairlines in it, and at twenty pixels the
    # hairlines vanish -- it measured 0.036 from the blunt, which is two icons that are the
    # same picture. Cutting the notches in from the top and bottom edges makes the silhouette
    # itself segmented, and a silhouette survives the downsample.
    d.rounded_rectangle([40, 196, 472, 316], radius=26, fill=W)

    for x in (168, 256, 344):
        d.rectangle([x - 13, 186, x + 13, 246], fill=CLEAR)
        d.rectangle([x - 13, 266, x + 13, 326], fill=CLEAR)

    save(img, 'xanax.png')


def hash_():
    """A pressed slab, seen at an angle, with a corner broken off it."""
    img, d = canvas()

    d.polygon([(70, 200), (300, 118), (452, 190), (452, 366), (222, 452), (70, 380)], fill=W)
    d.polygon([(70, 200), (300, 118), (452, 190), (222, 274)], fill=CLEAR)
    d.polygon([(452, 190), (452, 258), (360, 236)], fill=CLEAR)
    d.line([(222, 274), (222, 452)], fill=CLEAR, width=14)

    save(img, 'hash.png')


def dabs():
    """A dab tool with the concentrate on the end of it."""
    img, d = canvas()

    d.polygon([(60, 430), (110, 470), (392, 188), (342, 148)], fill=W)
    d.arc([300, 60, 456, 216], 20, 300, fill=W, width=42)
    d.ellipse([120, 116, 246, 242], fill=W)
    d.ellipse([160, 156, 206, 202], fill=CLEAR)

    save(img, 'dabs.png')


def edibles():
    """A gummy bear. There is no mistaking it for anything else on any screen."""
    img, d = canvas()

    d.ellipse([176, 40, 336, 200], fill=W)                 # head
    d.ellipse([120, 60, 208, 148], fill=W)                 # ears
    d.ellipse([304, 60, 392, 148], fill=W)
    d.rounded_rectangle([146, 176, 366, 400], radius=76, fill=W)
    d.ellipse([56, 208, 176, 328], fill=W)                 # arms
    d.ellipse([336, 208, 456, 328], fill=W)
    d.ellipse([120, 350, 244, 474], fill=W)                # legs
    d.ellipse([268, 350, 392, 474], fill=W)

    d.ellipse([206, 96, 238, 128], fill=CLEAR)             # eyes
    d.ellipse([274, 96, 306, 128], fill=CLEAR)

    save(img, 'edibles.png')


def vape():
    """A cart. A slim pen with a window, which is not a shape anything else here has."""
    img, d = canvas()

    d.rounded_rectangle([206, 30, 306, 120], radius=26, fill=W)      # mouthpiece
    d.rounded_rectangle([186, 128, 326, 470], radius=34, fill=W)     # body
    d.rectangle([222, 186, 290, 340], fill=CLEAR)                    # the window
    d.rectangle([222, 186, 290, 270], fill=W)                        # half full
    d.ellipse([236, 408, 276, 448], fill=CLEAR)

    save(img, 'vape.png')


def speed():
    """A zip baggie with a fold in it. Soft-cornered, unlike every block in the set."""
    img, d = canvas()

    d.rounded_rectangle([96, 130, 416, 470], radius=40, fill=W)
    d.rectangle([116, 60, 396, 140], fill=W)
    d.rectangle([136, 88, 376, 106], fill=CLEAR)          # the zip
    d.polygon([(150, 300), (256, 246), (362, 300), (362, 440), (150, 440)], fill=CLEAR)

    save(img, 'speed.png')


def ketamine():
    """A vial. Bottle, neck, cap -- and nothing else here has a neck."""
    img, d = canvas()

    d.rounded_rectangle([196, 40, 316, 110], radius=16, fill=W)    # cap
    d.rectangle([226, 110, 286, 168], fill=W)                      # neck
    d.rounded_rectangle([136, 168, 376, 470], radius=40, fill=W)   # body
    d.rectangle([176, 300, 336, 430], fill=CLEAR)                  # the level
    d.rectangle([176, 360, 336, 430], fill=W)

    save(img, 'ketamine.png')


def fentanyl():
    """A patch, with the backing peeled off one corner."""
    img, d = canvas()

    d.rounded_rectangle([70, 96, 442, 416], radius=30, fill=W)
    d.rounded_rectangle([124, 150, 388, 362], radius=18, fill=CLEAR)
    d.rounded_rectangle([164, 190, 348, 322], radius=12, fill=W)
    d.polygon([(388, 362), (442, 416), (330, 416), (330, 362)], fill=CLEAR)

    save(img, 'fentanyl.png')


def blunt():
    """
    A blunt, and deliberately fatter and blunter than the joint.

    weed.png is a thin tapered joint at an angle. This is a straight fat cylinder with a band
    round the middle and a lit end, so the two never read as the same thing on the same screen.
    """
    img, d = canvas()

    # Stood on its end, with smoke coming off it.
    #
    # Lying flat it was a rounded bar, which is what the xanax bar is -- 0.036 apart, the two
    # closest things in the whole set. Upright it shares an axis with nothing else here, and
    # the smoke gives it a top half no bar has.
    d.rounded_rectangle([196, 168, 316, 480], radius=34, fill=W)
    d.rectangle([206, 300, 306, 340], fill=CLEAR)          # the band
    d.ellipse([186, 128, 326, 250], fill=W)                # the cherry
    d.ellipse([222, 160, 290, 218], fill=CLEAR)

    d.ellipse([300, 40, 372, 112], fill=W)                 # smoke
    d.ellipse([356, 0, 410, 54], fill=W)

    save(img, 'blunt.png')


def brick():
    """A taped kilo. The tape is the whole silhouette -- a plain block would be the box."""
    img, d = canvas()

    d.rounded_rectangle([56, 116, 456, 396], radius=16, fill=W)
    d.rectangle([230, 116, 282, 396], fill=CLEAR)          # tape, the short way
    d.rectangle([56, 232, 456, 280], fill=CLEAR)           # and the long way
    d.rectangle([236, 238, 276, 274], fill=W)              # where they cross

    save(img, 'brick.png')


def crystal():
    """A cluster of shards. All points, where the cut stone is all facets."""
    img, d = canvas()

    d.polygon([(206, 30), (296, 190), (256, 460), (166, 200)], fill=W)
    d.polygon([(56, 210), (166, 286), (150, 470), (60, 366)], fill=W)
    d.polygon([(456, 190), (352, 290), (382, 470), (466, 350)], fill=W)

    save(img, 'crystal.png')


def bong():
    """A beaker, a tube and a bowl. Tall and bottom-heavy, which nothing else is."""
    img, d = canvas()

    d.polygon([(120, 470), (392, 470), (322, 250), (190, 250)], fill=W)   # beaker
    d.rectangle([206, 60, 306, 260], fill=W)                             # tube
    d.rounded_rectangle([176, 30, 336, 78], radius=16, fill=W)            # mouthpiece
    d.polygon([(316, 300), (452, 236), (466, 276), (330, 340)], fill=W)   # stem
    d.ellipse([416, 200, 492, 276], fill=W)                              # bowl
    d.ellipse([438, 222, 470, 254], fill=CLEAR)

    save(img, 'bong.png')


def poppy():
    """A seed pod on a stem. The only thing in the set that grew."""
    img, d = canvas()

    d.ellipse([146, 130, 366, 380], fill=W)
    d.polygon([(206, 130), (256, 40), (306, 130)], fill=W)
    for x in (176, 226, 286, 336):
        d.polygon([(x, 132), (x + 20, 66), (x + 40, 132)], fill=W)
    d.rectangle([236, 360, 276, 480], fill=W)
    d.ellipse([196, 216, 250, 270], fill=CLEAR)
    d.ellipse([272, 216, 326, 270], fill=CLEAR)

    save(img, 'poppy.png')


# Every icon in the file, by name. Run with no arguments to draw all of them, or name the
# ones you want -- "python make_icons.py guns mobile" -- which is the difference between
# regenerating one symbol and rewriting sixty-nine files to change one.
ALL = [eyes, cap, crown, leaf, skull, police, heart, reply, repost, like, tick, crack, pills, heroin, megaphone, weed, coke, meth, money, cash, guns, mobile, ammo, garage, mask, health, tattoo, stash, warning, locked, gang_families, gang_ballas, gang_vagos, gang_aztecas_OLD, gang_marabunta, gang_lost, gang_triads_OLD, gang_armenians, gang_koreans, gang_aztecas, gang_triads, footfall, rank, people, pin, deal, crate, box, phone, spray, fire, car, scales, dog, bed, music, key, lean, acid, shrooms, xanax, hash_, dabs, edibles, vape, speed, ketamine, fentanyl, blunt, brick, crystal, bong, poppy]


if __name__ == '__main__':
    wanted = set(a.rstrip('_') for a in sys.argv[1:])
    drew = 0

    for fn in ALL:
        if wanted and fn.__name__.rstrip('_') not in wanted:
            continue
        fn()
        drew += 1

    if wanted and not drew:
        print('no icon called: ' + ', '.join(sorted(wanted)))
        sys.exit(1)

    print('%d icon(s) written' % drew)
