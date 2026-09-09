# -*- coding: utf-8 -*-
"""
The LUber brand: an app badge and a wordmark, both as white masks.

    data/icons/luber_app.png   the square badge, for the phone's home screen
    data/icons/luber.png       the wordmark, for the band across the ride picker

WHY A BADGE AND NOT A PICTURE OF A CAR. The tile it replaces was car.png, which is the same
picture the owned-car blip, the tow and half the mod use -- so the one app on that phone with
a brand of its own was wearing the mod's generic car. A phone home screen is a grid of app
icons, and an app icon is a shape with a monogram in it.

REVERSED OUT, the same trick as the lot's sign: in a white-on-transparent mask the badge is
the ink and the letters are holes, so the badge takes the panel's colour at draw time and the
panel's own background shows through the L and the U. It cannot be the wrong colour on
anything it is drawn over.

LU AND NOTHING ELSE. The brand is the joke and the joke is in those two letters, so they are
the whole badge -- and two heavy characters are the most that survives being drawn at thirty
pixels on a phone tile. The wordmark spells the rest: LU heavy, ber lighter behind it, which
is the reading the name wants.

    python tools/make_luber.py
"""

import os

from PIL import Image, ImageChops, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ICONS = os.path.join(os.path.dirname(HERE), "data", "icons")

BADGE = os.path.join(ICONS, "luber_app.png")
MARK = os.path.join(ICONS, "luber.png")

OVER = 4

# ---- the badge --------------------------------------------------------------------------

# Drawn at this size and shrunk. Square, because a phone tile is.
BADGE_SIZE = 256

# The corner radius and the air round the letters, as a share of the badge.
CORNER = 0.22
INSET = 0.17


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

            return font
        except Exception:
            continue

    raise SystemExit("no usable font on this machine")


def badge():
    n = BADGE_SIZE * OVER

    img = Image.new("RGBA", (n, n), (255, 255, 255, 0))
    d = ImageDraw.Draw(img)

    d.rounded_rectangle([0, 0, n - 1, n - 1], radius=int(n * CORNER), fill=(255, 255, 255, 255))

    # The letters, sized to the room inside rather than to a guess: measured, then scaled so
    # they fill the inset box in both directions.
    room = n * (1.0 - INSET * 2)

    size = int(room)
    font = face(size, "Bold")

    box = font.getbbox("LU")
    w, h = box[2] - box[0], box[3] - box[1]

    size = int(size * min(room / float(w), room / float(h)))
    font = face(size, "Bold")

    box = font.getbbox("LU")
    w, h = box[2] - box[0], box[3] - box[1]

    holes = Image.new("L", (n, n), 0)
    ImageDraw.Draw(holes).text(((n - w) / 2.0 - box[0], (n - h) / 2.0 - box[1]),
                               "LU", font=font, fill=255)

    img.putalpha(ImageChops.subtract(img.getchannel("A"), holes))

    out = img.resize((BADGE_SIZE, BADGE_SIZE), Image.LANCZOS)
    out.save(BADGE)

    print("wrote %s (%dx%d)" % (BADGE, out.size[0], out.size[1]))


# ---- the wordmark -----------------------------------------------------------------------

MARK_SIZE = 200
MARK_PAD = 0.06


def wordmark():
    big = MARK_SIZE * OVER

    heavy = face(big, "Bold Condensed")
    light = face(int(big * 0.98), "SemiLight Condensed")

    lu = "LU"
    ber = "ber"

    luW = int(heavy.getlength(lu))
    berW = int(light.getlength(ber))

    box = heavy.getbbox("L")
    capTop, cap = box[1], box[3] - box[1]

    pad = int(cap * MARK_PAD)

    width = luW + berW + pad * 2
    height = cap + pad * 2

    img = Image.new("RGBA", (width, height), (255, 255, 255, 0))
    d = ImageDraw.Draw(img)

    d.text((pad, pad - capTop), lu, font=heavy, fill=(255, 255, 255, 255))

    # The lighter half sits on the same baseline, not the same top.
    lightBox = light.getbbox("b")

    d.text((pad + luW, pad + cap - (lightBox[3] - lightBox[1]) - lightBox[1]),
           ber, font=light, fill=(255, 255, 255, 255))

    out = img.resize((width // OVER, height // OVER), Image.LANCZOS)
    out.save(MARK)

    w, h = out.size

    print("wrote %s (%dx%d)" % (MARK, w, h))
    print("aspect (w/h): %.4f  <- LuberAspect in RideScreen.cs" % (w / float(h)))


if __name__ == "__main__":
    badge()
    wordmark()
