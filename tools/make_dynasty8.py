# -*- coding: utf-8 -*-
"""
The Dynasty 8 app and the Browser app: data/icons/dynasty8.png, dynasty8_sign.png and
browser.png, as white masks.

DYNASTY 8 IS THE GAME'S OWN REALTOR -- the one whose site is in the phone's browser, "the
leading realtor in Los Santos and Blaine County". Its logo is a house drawn as one thick line,
open at the bottom, with a sun rising behind the right-hand slope of the roof. Michael sent it
for the app that lists the stash houses (2026-09-27).

A MASK, SO THE SHAPE AND NOT THE COLOURS. Every icon in the mod is white on transparent and
tinted at draw time (see iconkit.py), so the green line and the yellow sun become the panel's
colour -- the sun at MID, so it sits BEHIND the roof the way it does in the logo rather than
merging with it. The house is open at the bottom like the logo, with the 8 standing in it: a
house and a number is the name, and at sixty pixels it is the only part of the wordmark that
survives.

THE SIGN is the same mark with DYNASTY 8 beside it, for the top of the app's page -- the wide
thing a page header has room for and a tile does not. See WheelPage.WithSign.

THE BROWSER is a globe: the outline, the equator, two meridians and two parallels, the inner
lines at MID so the outline carries it at small sizes.

    python tools/make_dynasty8.py
"""

import math
import os

from PIL import Image, ImageDraw, ImageFont

import iconkit as k

HOUSE_W = 46


def bold(size):
    """Bahnschrift Bold, then Arial Bold: whichever this machine has."""
    for path, want in ((r"C:\Windows\Fonts\bahnschrift.ttf", "Bold"),
                       (r"C:\Windows\Fonts\arialbd.ttf", None)):
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


def centred(d, text, cx, cy, font, fill=k.W):
    box = d.textbbox((0, 0), text, font=font)
    w = box[2] - box[0]
    h = box[3] - box[1]
    d.text((cx - w / 2.0 - box[0], cy - h / 2.0 - box[1]), text, font=font, fill=fill)


def house(d, x, y, s, eight=True):
    """
    The mark in a box s wide at (x, y): the sun, then the house over it, then the 8.

    Coordinates are shares of s so the tile and the sign draw the same thing at two sizes.
    """
    def p(u, v):
        return (x + u * s, y + v * s)

    # The sun first, behind: whatever is drawn later owns its pixels.
    sx, sy = p(0.70, 0.30)
    k.disc(d, sx, sy, s * 0.19, k.MID)

    # One thick line: up the left wall, over the peak, down the right slope and wall.
    k.stroke(d, [p(0.16, 0.90), p(0.16, 0.48), p(0.50, 0.17), p(0.84, 0.48), p(0.84, 0.90)],
             s * HOUSE_W / 512.0)

    if eight:
        centred(d, "8", x + 0.50 * s, y + 0.64 * s, bold(int(s * 0.42)))


def tile():
    img, d = k.canvas()
    house(d, 0, 20, k.S)
    k.save(img, "dynasty8.png")


def sign():
    """The mark and the name side by side, for the top of the page."""
    h = 512
    img = Image.new("RGBA", (int(h * 3.6), h), k.CLEAR)
    d = ImageDraw.Draw(img)

    house(d, 20, 10, h - 20)

    font = bold(int(h * 0.44))
    box = d.textbbox((0, 0), "DYNASTY 8", font=font)
    d.text((h + 10 - box[0], h * 0.5 - (box[3] - box[1]) / 2.0 - box[1]), "DYNASTY 8",
           font=font, fill=k.W)

    right = h + 10 + (box[2] - box[0]) + 30
    img = img.crop((0, 0, int(right), h))
    k.save(img, "dynasty8_sign.png")

    w, hh = img.size
    print("  sign aspect (w/h): %.4f  -> Dynasty8Sign in WheelPages.cs" % (w / float(hh)))


def globe():
    img, d = k.canvas()
    c = k.S / 2.0
    r = 206.0

    # The inner lines first and lighter, then the outline over their ends: the equator, a
    # parallel either side of it, one meridian as an ellipse and the axis through it.
    k.stroke(d, [(c - r, c), (c + r, c)], 26, k.MID)

    for v in (-0.52, 0.52):
        y = c + v * r
        half = math.sqrt(r * r - (v * r) ** 2)
        k.stroke(d, [(c - half, y), (c + half, y)], 22, k.MID)

    rx = r * 0.44
    meridian = [(c + rx * math.sin(t), c - r * math.cos(t))
                for t in [i * math.pi / 48.0 for i in range(96)]]
    k.stroke(d, meridian, 26, k.MID, closed=True)
    k.stroke(d, [(c, c - r), (c, c + r)], 26, k.MID)

    k.ring(d, c, c, r, 40)

    k.save(img, "browser.png")


if __name__ == "__main__":
    tile()
    sign()
    globe()
