# -*- coding: utf-8 -*-
"""
The soft shadow under the phone: data/icons/phone_shadow.png.

ONE PRE-BLURRED SPRITE. A blurred shape wants dozens of stacked rectangles to fake in-game,
and the phone is already the biggest rectangle spender in the mod, so the blur is baked here
and the game draws it as a single sprite for nothing.

The file is rendered at the handset's own proportions (PhoneMenu.BodyH * BodyRatio, at 16:9)
with the same margin on every side, so that when PhoneMenu draws it at "body plus one
margin all round" the fall-off is even -- a square file stretched over a tall phone would
blur twice as far up and down as it did sideways. MARGIN here and ShadowSpread in
PhoneMenu.Shadow describe the same distance: 0.048 of screen height, which at half of 1080p
is 26 pixels.

    python tools/make_shadow.py
"""

import os

from PIL import Image, ImageChops, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(os.path.dirname(HERE), "data", "icons", "phone_shadow.png")

# The body at half of 1080p: width = ToX(0.760 * 0.472) * 1920 / 2, height = 0.760 * 1080 / 2.
BODY_W = 194
BODY_H = 410

# 0.048 of screen height, at half of 1080p.
MARGIN = 26


def main():
    w = BODY_W + MARGIN * 2
    h = BODY_H + MARGIN * 2

    # A wide, faint ambient first, then a tighter, darker contact shadow on top of it. The
    # two together read as one shadow with a soft edge that still has a dark heart, which a
    # single blur cannot do -- one radius is either soft and pale or dark and hard.
    ambient = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(ambient).rounded_rectangle(
        [MARGIN + 2, MARGIN + 6, w - MARGIN - 2, h - MARGIN + 2], radius=14, fill=(0, 0, 0, 120))
    ambient = ambient.filter(ImageFilter.GaussianBlur(MARGIN * 0.55))

    contact = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(contact).rounded_rectangle(
        [MARGIN + 4, MARGIN + 8, w - MARGIN - 4, h - MARGIN + 4], radius=12, fill=(0, 0, 0, 200))
    contact = contact.filter(ImageFilter.GaussianBlur(MARGIN * 0.30))

    img = Image.alpha_composite(ambient, contact)

    # HOLLOW. The game composites script sprites above script rectangles and text whatever
    # order they were submitted in, so a shadow drawn "under" the phone lands on top of it:
    # the first build of this darkened the whole screen of the handset and the text went
    # unreadable. The body's own rectangle is punched clean out of the file -- with the
    # body's corner rounding, so the shadow still shows through the rounded-off tips --
    # and only the fringe exists, which is the only part of a shadow you were ever going
    # to see.
    hole = Image.new("L", (w, h), 255)
    ImageDraw.Draw(hole).rounded_rectangle([MARGIN, MARGIN, w - MARGIN - 1, h - MARGIN - 1],
                                           radius=4, fill=0)
    img.putalpha(ImageChops.multiply(img.getchannel("A"), hole))

    img.save(OUT)

    print("wrote %s (%dx%d)" % (OUT, w, h))


if __name__ == "__main__":
    main()
