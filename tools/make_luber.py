# -*- coding: utf-8 -*-
"""
The LUber logo: data/icons/luber.png, as a white mask.

ONE LOCKUP, NOT A BADGE AND A WORDMARK. The box goes round the LU and the rest of the name
sits outside it, which is the whole idea of the brand written down: the joke is in the two
letters, so the two letters are the part in the box.

REVERSED OUT WHERE IT IS BOXED, SOLID WHERE IT IS NOT. In a white-on-transparent mask that
means the box is ink and the L and U are holes -- so the box takes the panel's colour at draw
time and the panel's own background shows through the letters -- while "ber" beside it is
plain ink like any other mark in the set. Nothing has to be painted the colour of whatever it
is sitting on.

BASELINE, NOT BOX. "ber" lines up with the LU inside the box rather than with the box itself,
because the box has padding and the letters do not. Lining it up with the box would sit the
whole word a few pixels low and look like a mistake nobody could name.

Bahnschrift Bold Condensed for the same reasons the other two signs give, with Impact and
Arial Bold behind it so this always produces a file.

    python tools/make_luber.py
"""

import os

from PIL import Image, ImageChops, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ICONS = os.path.join(os.path.dirname(HERE), "data", "icons")

OUT = os.path.join(ICONS, "luber.png")

BOXED = "LU"
REST = "ber"

SIZE = 200
OVER = 4

# The box: air round its letters and how round its corners are, as a share of the cap height.
INSET_X = 0.22
INSET_Y = 0.20
CORNER = 0.20

# Air between the box and the rest of the name, and round the whole lockup.
GAP = 0.10
PAD = 0.05


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


def main():
    big = SIZE * OVER

    font, named = face(big, "Bold Condensed")

    # The cap height off a letter with no descender or overshoot.
    box = font.getbbox("L")
    capTop, cap = box[1], box[3] - box[1]

    insetX = int(cap * INSET_X)
    insetY = int(cap * INSET_Y)
    corner = int(cap * CORNER)
    gap = int(cap * GAP)
    pad = int(cap * PAD)

    boxedW = int(font.getlength(BOXED))
    restW = int(font.getlength(REST))

    boxW = boxedW + insetX * 2
    boxH = cap + insetY * 2

    # "ber" has a descender on nothing and an ascender on the b, so the lockup is as tall as
    # the box or as tall as that ascender, whichever wins.
    restBox = font.getbbox(REST)
    restTop, restBottom = restBox[1], restBox[3]

    # Where the boxed letters sit, and therefore where the rest has to sit.
    letterTop = pad + insetY

    topMost = min(pad, letterTop + (restTop - capTop))
    bottomMost = max(pad + boxH, letterTop + (restBottom - capTop))

    width = boxW + gap + restW + pad * 2
    height = int(bottomMost - topMost) + pad

    # Everything is measured from the box's own top, so shift if the b climbs above it.
    lift = pad - topMost

    img = Image.new("RGBA", (width, height), (255, 255, 255, 0))
    d = ImageDraw.Draw(img)

    # ---- the box ----
    d.rounded_rectangle([pad, pad + lift, pad + boxW - 1, pad + lift + boxH - 1],
                        radius=corner, fill=(255, 255, 255, 255))

    # ---- the letters knocked out of it ----
    holes = Image.new("L", (width, height), 0)

    ImageDraw.Draw(holes).text((pad + insetX, letterTop + lift - capTop), BOXED,
                               font=font, fill=255)

    img.putalpha(ImageChops.subtract(img.getchannel("A"), holes))

    # ---- and the rest of the name beside it, on the same baseline ----
    d = ImageDraw.Draw(img)

    d.text((pad + boxW + gap, letterTop + lift - capTop), REST, font=font,
           fill=(255, 255, 255, 255))

    out = img.resize((width // OVER, height // OVER), Image.LANCZOS)
    out.save(OUT)

    w, h = out.size

    print("wrote %s (%dx%d)" % (OUT, w, h))
    print("face: %s" % named)
    print("aspect (w/h): %.4f  <- LuberAspect, and the tile's icon aspect" % (w / float(h)))


if __name__ == "__main__":
    main()
