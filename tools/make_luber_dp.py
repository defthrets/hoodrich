# -*- coding: utf-8 -*-
"""
LUber's display picture: data/icons/luber_dp.png, as a white mask.

A SQUARE, BECAUSE AN AVATAR IS A SQUARE. luber.png is a wide lockup and it is the right shape
for a band across the top of a screen and the wrong shape for the round hole a contact photo
goes in -- squeezed into one it is a stripe. This is the same brand arranged for that hole.

WHY THERE IS A PICTURE AT ALL. The delivery driver texts you, and until the game has finished
rendering his headshot a message has no picture -- so the phone drew the first letter of his
name and the game's feed drew CHAR_DEFAULT, which on this install is the LS Customs badge. A
car customiser signing for burgers. This is what goes there instead.

REVERSED OUT, like the rest of the set: the tile is the ink and the brand is holes in it, so
it takes the panel's colour at draw time and the panel's own background shows through the
name. It is the biggest lump of ink in the whole icon folder, which is exactly what an avatar
at forty pixels wants to be.

THE SHAPES COME FROM make_luber.py rather than being drawn again here. One car, one moped and
one cup exist in this codebase and they live in that file; a second copy would be two things
to fix every time the moped is wrong.

    python tools/make_luber_dp.py
"""

import os
import sys

from PIL import Image, ImageChops, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import make_luber as mark

HERE = os.path.dirname(os.path.abspath(__file__))
ICONS = os.path.join(os.path.dirname(HERE), "data", "icons")

OUT = os.path.join(ICONS, "luber_dp.png")

SIZE = 256
OVER = 4

# How round the tile is, and how much of it the brand is allowed to fill.
CORNER = 0.22
INSET_X = 0.80
INSET_Y = 0.64

# The row of pictures against the word, and the air between them.
ICON_H = 0.46
ICON_GAP = 0.22

INK = (255, 255, 255, 255)


def brand(font):
    """The three pictures over the name, as ink on nothing. No tile, no slab."""
    box = font.getbbox(mark.WORD)
    top, bottom = box[1], box[3]

    wordW = int(font.getlength(mark.WORD))
    wordH = bottom - top

    iconH = wordH * ICON_H
    gap = wordH * ICON_GAP
    between = iconH * mark.ICON_GAP

    rowW = sum(iconH * ratio for _, ratio in mark.ROW) + between * (len(mark.ROW) - 1)

    w = int(max(wordW, rowW))
    h = int(iconH + gap + wordH)

    img = Image.new("RGBA", (w, h), (255, 255, 255, 0))
    d = ImageDraw.Draw(img)

    x = (w - rowW) * 0.5

    for draw, ratio in mark.ROW:
        draw(d, x, 0, iconH)
        x += iconH * ratio + between

    d.text(((w - wordW) * 0.5, iconH + gap - top), mark.WORD, font=font, fill=INK)

    return img


def main():
    side = SIZE * OVER

    font, named = mark.face(side, "Bold Condensed")

    art = brand(font)

    # Scaled to whichever of the two inner limits it hits first, so a change to the word or to
    # the row cannot push it off the tile.
    room = (side * INSET_X, side * INSET_Y)

    scale = min(room[0] / float(art.size[0]), room[1] / float(art.size[1]))

    art = art.resize((max(1, int(art.size[0] * scale)), max(1, int(art.size[1] * scale))),
                     Image.LANCZOS)

    tile = Image.new("RGBA", (side, side), (255, 255, 255, 0))

    ImageDraw.Draw(tile).rounded_rectangle([0, 0, side - 1, side - 1],
                                           radius=int(side * CORNER), fill=INK)

    holes = Image.new("L", (side, side), 0)
    holes.paste(art.getchannel("A"),
                ((side - art.size[0]) // 2, (side - art.size[1]) // 2))

    tile.putalpha(ImageChops.subtract(tile.getchannel("A"), holes))

    out = tile.resize((side // OVER, side // OVER), Image.LANCZOS)
    out.save(OUT)

    print("wrote %s (%dx%d)" % (OUT, out.size[0], out.size[1]))
    print("face: %s" % named)


if __name__ == "__main__":
    main()
