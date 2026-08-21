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

import os
from PIL import Image, ImageDraw

OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "data", "icons")
S = 512          # working size
F = 8            # downsample factor -> 64px files
W = (255, 255, 255, 255)
CLEAR = (0, 0, 0, 0)


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
    """The heart again, so a post's likes and the reputation bar agree with each other."""
    img, d = canvas()

    d.ellipse([64, 78, 280, 294], fill=W)
    d.ellipse([232, 78, 448, 294], fill=W)
    d.polygon([(78, 232), (434, 232), (256, 470)], fill=W)

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
    A rock. Angular on purpose -- everything else in the drug set is round or soft, so the
    silhouette is made entirely of corners. One facet punched out so it is not a blob.
    """
    img, d = canvas()
    d.polygon([(60, 296), (146, 128), (322, 92), (452, 214), (420, 396), (214, 452)], fill=W)
    d.polygon([(206, 200), (322, 176), (350, 268), (240, 300)], fill=CLEAR)
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


print('writing to %s' % OUT)
skull()
police()
heart()
reply()
repost()
like()
tick()
crack()
pills()
heroin()
