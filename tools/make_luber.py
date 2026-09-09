# -*- coding: utf-8 -*-
"""
The LUber logo: data/icons/luber.png, as a white mask.

A LEANING SLAB WITH THE WHOLE NAME KNOCKED OUT OF IT, and a row of little pictures above it
saying what the company does.

NOT A BOX ROUND HALF THE WORD. That is the old You|Tube lockup with the halves swapped, and it
looked it. The slab takes the WHOLE name, so there is no boxed half and no odd "LU" sitting on
its own on the phone's home screen, and the lean is the only thing doing the talking -- a
delivery brand leans, a taxi brand leans, and it costs nothing at 24 pixels.

REVERSED OUT, WHICH IS THE SAME TRICK AS HAO'S SIGN. In a white-on-transparent mask the slab is
the ink and the letters are holes, so the slab takes the panel's colour at draw time and the
panel's own background shows through the name. Nothing has to be painted the colour of whatever
it is sitting on, and it is the single biggest lump of ink in the set, which is why it is still
readable at the size the phone tile draws it.

THE THREE PICTURES ARE THE PRODUCT: a car with nobody in it, a moped with a box on the back,
and a cup. That is the whole business in three shapes, and it is the half of the brand a
wordmark cannot say. They are drawn as filled silhouettes with no interior detail, because
anything finer than a wheel disappears at eight pixels.

UPRIGHT ABOVE A LEANING SLAB, deliberately. Leaning them too makes the mark look like it is
falling over; a level row of pictograms over a leaning sign reads as a sign, which is what it
is. They lean with nothing and sit on their own baseline.

Bahnschrift Bold Condensed for the same reasons the other signs give, with Impact and Arial
Bold behind it so this always produces a file. Rendered four times the size it is used at and
shrunk, like every mark in the set.

    python tools/make_luber.py
"""

import os

from PIL import Image, ImageChops, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ICONS = os.path.join(os.path.dirname(HERE), "data", "icons")

OUT = os.path.join(ICONS, "luber.png")

WORD = "LUber"

SIZE = 200
OVER = 4

# The slab: air round the name, how round its corners are, and how far it leans -- all as a
# share of the cap height except the lean, which is a share of the slab's own height.
INSET_X = 0.22
INSET_Y = 0.17
CORNER = 0.16
LEAN = 0.20

# The row of pictures: how tall each one is against the slab, how far apart, and how far above.
ICON_H = 0.42
ICON_GAP = 0.24
ICON_LIFT = 0.13

# Air round the whole thing so nothing is clipped when the game scales it.
PAD = 0.04


def face(size, instance):
    tries = [
        (r"C:\Windows\Fonts\bahnschrift.ttf", instance),
        (r"C:\Windows\Fonts\impact.ttf", None),
        (r"C:\Windows\Fonts\arialbd.ttf", None),
    ]

    for path, want in tries:
        if not os.path.exists(path):
            continue

        try:
            font = ImageFont.truetype(path, size)

            if want:
                font.set_variation_by_name(want)

            return font, os.path.basename(path) + (" " + want if want else "")
        except Exception:
            continue

    raise SystemExit("no usable font on this machine")


# ---- the three pictures -----------------------------------------------------------------
#
# Each is drawn into a box of its own and knows only its own height; the width follows. They
# are silhouettes and nothing else -- no windows, no spokes, no lid seams. At the size these
# are actually seen a detail one pixel wide is a smudge, and three smudges is a worse mark
# than three clean shapes.

CAR_W = 1.56
MOPED_W = 1.46
CUP_W = 0.84

INK = (255, 255, 255, 255)


def car(d, x, y, h):
    """Side on, no driver. The one that carries people."""
    w = h * CAR_W

    # The body, and the cabin sat on it. Deep, because a shallow one reads as a shoe.
    d.rounded_rectangle([x, y + h * 0.36, x + w, y + h * 0.80], radius=h * 0.16, fill=INK)

    d.polygon([(x + w * 0.20, y + h * 0.42), (x + w * 0.33, y + h * 0.02),
               (x + w * 0.63, y + h * 0.02), (x + w * 0.78, y + h * 0.42)], fill=INK)

    # Wheels, sitting proud of the body so the shape is a car and not a loaf.
    r = h * 0.21

    for at in (0.25, 0.76):
        cx = x + w * at
        cy = y + h * 0.82
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=INK)


def moped(d, x, y, h):
    """The one that brings the food.

    THE BOX IS THE POINT. A scooter and a motorbike are the same handful of pixels at this
    size; the thing that says a delivery is on it is the crate over the back wheel, so the
    crate is drawn big and everything else is drawn only as much as it takes to hold it up.
    """
    w = h * MOPED_W

    r = h * 0.20

    for at in (0.19, 0.84):
        cx = x + w * at
        cy = y + h * 0.83
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=INK)

    # THE BODY IS ONE SHAPE, not a deck with things stood on it. A floor, a post and a box
    # floating behind it is a kick scooter with luggage; a moped is a seat over the back wheel
    # running down into a floor and up again at the front, and the parts have to touch.
    d.rounded_rectangle([x + w * 0.14, y + h * 0.62, x + w * 0.74, y + h * 0.80],
                        radius=h * 0.08, fill=INK)

    d.rounded_rectangle([x + w * 0.06, y + h * 0.44, x + w * 0.42, y + h * 0.72],
                        radius=h * 0.10, fill=INK)

    # The legshield up to the bars, and the bars on top. THE TALLEST THING ON IT, by a clear
    # margin -- a scooter is low at the back and high at the front, and when the crate and the
    # bars came to the same height the whole shape read as a dumbbell with two wheels.
    d.polygon([(x + w * 0.60, y + h * 0.78), (x + w * 0.72, y + h * 0.14),
               (x + w * 0.86, y + h * 0.16), (x + w * 0.78, y + h * 0.80)], fill=INK)

    d.rounded_rectangle([x + w * 0.62, y, x + w, y + h * 0.15], radius=h * 0.07, fill=INK)

    # And the crate, sat ON the seat rather than hanging off the back of nothing.
    d.rounded_rectangle([x, y + h * 0.14, x + w * 0.34, y + h * 0.50],
                        radius=h * 0.08, fill=INK)


def cup(d, x, y, h):
    """The thing in the box, said in one shape."""
    w = h * CUP_W

    # The straw first, so the lid sits over where it goes in.
    d.polygon([(x + w * 0.52, y + h * 0.34), (x + w * 0.74, y), (x + w * 0.96, y + h * 0.10),
               (x + w * 0.72, y + h * 0.40)], fill=INK)

    d.rounded_rectangle([x, y + h * 0.16, x + w, y + h * 0.36], radius=h * 0.08, fill=INK)

    d.polygon([(x + w * 0.07, y + h * 0.36), (x + w * 0.93, y + h * 0.36),
               (x + w * 0.79, y + h), (x + w * 0.21, y + h)], fill=INK)


ROW = [(car, CAR_W), (moped, MOPED_W), (cup, CUP_W)]


def main():
    big = SIZE * OVER

    font, named = face(big, "Bold Condensed")

    # The name's own box, ascender to baseline -- "LUber" has a cap, an x-height and two
    # ascenders in it, so the slab is measured off the word rather than off a single letter.
    box = font.getbbox(WORD)
    wordTop, wordBottom = box[1], box[3]
    tall = wordBottom - wordTop

    cap = font.getbbox("L")[3] - font.getbbox("L")[1]

    insetX = int(cap * INSET_X)
    insetY = int(cap * INSET_Y)
    corner = int(cap * CORNER)
    pad = int(cap * PAD)

    wordW = int(font.getlength(WORD))

    slabW = wordW + insetX * 2
    slabH = tall + insetY * 2

    lean = int(slabH * LEAN)

    iconH = slabH * ICON_H
    iconGap = iconH * ICON_GAP
    lift = slabH * ICON_LIFT

    rowW = sum(iconH * ratio for _, ratio in ROW) + iconGap * (len(ROW) - 1)

    width = slabW + pad * 2 + lean
    height = int(iconH + lift) + slabH + pad * 2

    img = Image.new("RGBA", (width, height), (255, 255, 255, 0))
    d = ImageDraw.Draw(img)

    # ---- the pictures, level, centred on the slab ----
    #
    # Centred on where the slab ENDS UP rather than where it is drawn: the shear below moves
    # the slab's top edge right by the full lean and its bottom edge not at all, so its middle
    # travels half of it. Centring on the pre-shear middle would leave the row visibly off.
    x = pad + (slabW - rowW) * 0.5 + lean * 0.5
    y = pad

    for draw, ratio in ROW:
        draw(d, x, y, iconH)
        x += iconH * ratio + iconGap

    # ---- the slab ----
    slabTop = pad + int(iconH + lift)

    d.rounded_rectangle([pad, slabTop, pad + slabW - 1, slabTop + slabH - 1],
                        radius=corner, fill=INK)

    # ---- the name knocked out of it ----
    holes = Image.new("L", (width, height), 0)

    ImageDraw.Draw(holes).text((pad + insetX, slabTop + insetY - wordTop), WORD,
                               font=font, fill=255)

    img.putalpha(ImageChops.subtract(img.getchannel("A"), holes))

    # ---- and the slab leans, on its own ----
    #
    # Sheared as a strip rather than as the whole image, because the pictures above it are
    # meant to stay level. The shear is measured against the strip's own height, so the
    # bottom edge stays put and the top edge travels the full lean.
    strip = img.crop((0, slabTop - pad, width, height))
    sh = strip.size[1]

    strip = strip.transform((width, sh), Image.AFFINE,
                            (1, lean / float(sh), -lean, 0, 1, 0), resample=Image.BICUBIC)

    img.paste(strip, (0, slabTop - pad))

    out = img.resize((width // OVER, height // OVER), Image.LANCZOS)
    out.save(OUT)

    w, h = out.size

    print("wrote %s (%dx%d)" % (OUT, w, h))
    print("face: %s" % named)
    print("aspect (w/h): %.4f" % (w / float(h)))
    print("  -> LuberMark in WheelPages.cs, LuberAspect in RideScreen.cs")


if __name__ == "__main__":
    main()
