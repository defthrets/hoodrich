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
megaphone()


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
    """A pistol silhouette, blocky. A slim barrel is the first thing to go, so it is thick."""
    img, d = canvas()
    d.rectangle([70, 190, 430, 268], fill=W)        # slide
    d.rectangle([96, 268, 190, 300], fill=W)        # frame under the slide
    d.polygon([(150, 300), (250, 300), (206, 452), (110, 452)], fill=W)   # grip
    d.rectangle([250, 268, 300, 306], fill=W)       # trigger guard top
    d.rectangle([236, 296, 252, 340], fill=W)       # trigger
    save(img, 'guns.png')


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

weed()
coke()
meth()
money()
cash()
guns()
ammo()
garage()
mask()
health()
tattoo()
stash()
warning()
locked()


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


def gang_aztecas():
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


def gang_triads():
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


gang_families()
gang_ballas()
gang_vagos()
gang_aztecas()
gang_marabunta()
gang_lost()
gang_triads()
gang_armenians()
gang_koreans()
