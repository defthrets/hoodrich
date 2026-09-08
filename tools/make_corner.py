# -*- coding: utf-8 -*-
"""
The four corners of every rounded panel: data/icons/corner_tl.png, _tr, _bl, _br.

WHY FOUR PICTURES. A rounded corner drawn out of rectangles is a stack of thin bands -- one a
pixel row at the panel radius, so about a hundred rectangles a panel at 1440p and half as
many again at 4K -- out of a per-frame budget of a few hundred that every script on the
machine shares. Past it the game drops the rest of the frame's rectangles, and with the
phone, a prompt and a card up at once the phone's were the ones that went.

A sprite comes out of a budget nobody is near. Each of these is ONE QUADRANT of a disc: the
quarter that fills the corner square of a panel, with the arc bulging outward and nothing
in the other three quarters. That is the whole trick against the bullseye an earlier build
got from a whole-disc sprite -- three quarters of a disc landed on flats that were already
painted, and two coats of a translucent colour is a darker ring. A quadrant overlaps
nothing, so the panel's alpha goes down once everywhere.

Four files rather than one rotated, so nothing depends on which way the game turns a
sprite. White, so the panel's own colour and alpha tint them.

    python tools/make_corner.py
"""

import os

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ICONS = os.path.join(os.path.dirname(HERE), "data", "icons")

SIZE = 256
OVER = 4


def quadrant(where):
    big = SIZE * OVER
    img = Image.new("RGBA", (big, big), (255, 255, 255, 0))
    d = ImageDraw.Draw(img)

    # The disc's centre sits on the corner of the texture that faces the panel's inside; the
    # part of it inside the texture is exactly the panel's rounded corner.
    cx = big if where in ("tl", "bl") else 0
    cy = big if where in ("tl", "tr") else 0
    r = big

    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(255, 255, 255, 255))

    return img.resize((SIZE, SIZE), Image.LANCZOS)


def main():
    for where in ("tl", "tr", "bl", "br"):
        out = os.path.join(ICONS, "corner_%s.png" % where)
        quadrant(where).save(out)
        print("wrote", out)


if __name__ == "__main__":
    main()
