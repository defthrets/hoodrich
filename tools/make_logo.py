# -*- coding: utf-8 -*-
#
# The wordmark: POSTED UP, arched, as a white mask.
#
# White on transparent like every other icon in the set, because the colour goes on at draw
# time -- the same file is the dim mark on a panel header and a bright one on a dark screen.
#
# BLACKLETTER, AND THE FONT SHIPS WITH THE REPO. It was Impact -- the only heavy condensed
# face on a stock Windows box -- and a varsity block letter is not what this mod is. The
# reference is the Old English across the front of a Los Santos tee, which is Fraktur, and
# there is no Fraktur on a stock Windows box either: the machine this was written on has
# msgothic and two Nanums and nothing else even close.
#
# So it is carried rather than found. UnifrakturCook-Bold sits in tools/fonts with its OFL
# licence beside it, which is the same arrangement Fumes uses, and it means the wordmark
# renders the same on any machine instead of depending on what somebody has installed.
#
# THE ARCH AND THE PROPORTIONS ARE UNCHANGED. Same sweep, same file width, and the tracking
# and squeeze are tuned so the finished aspect lands back on what the C# already believes --
# see WordmarkAspect in Draw.cs. Fraktur sets with far more air in it than Impact and the
# capitals are wider, so both numbers move to stand still.
#
# Rendered per glyph and laid along the top of a circle rather than warped as one image: a
# warped bitmap smears the strokes, and at header size a smeared stroke is all you see.

import math
import os
import sys

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(os.path.dirname(HERE), 'data', 'icons')

TEXT = 'POSTED UP'
FONT = os.path.join(HERE, 'fonts', 'UnifrakturCook-Bold.ttf')

SIZE = 420          # per-glyph render height, before the arch -- up from 300, for crisper strokes
SWEEP = 19.0        # degrees the whole word covers -- a gentler arch than 26, so it sits in a status bar
SQUEEZE = 1.00      # Fraktur's own width is right; Impact had to be widened
TRACK = 13          # tighter than 19: at small sizes the gaps were louder than the letters
WIDE = 1536         # the file's width -- up from 1024


def glyphs(font):
    """Each character on its own tile, tight-cropped, plus the advance to the next."""
    out = []
    probe = ImageDraw.Draw(Image.new('L', (10, 10)))

    for ch in TEXT:
        if ch == ' ':
            out.append((None, int(SIZE * 0.26)))
            continue

        box = probe.textbbox((0, 0), ch, font=font)
        w = max(1, box[2] - box[0] + 8)
        h = max(1, box[3] - box[1] + 8)

        tile = Image.new('RGBA', (w, h), (0, 0, 0, 0))
        ImageDraw.Draw(tile).text((-box[0] + 4, -box[1] + 4), ch, font=font,
                                  fill=(255, 255, 255, 255))

        if SQUEEZE != 1.0:
            tile = tile.resize((max(1, int(tile.width * SQUEEZE)), tile.height), Image.LANCZOS)

        out.append((tile, tile.width + TRACK))

    return out


def build():
    font = ImageFont.truetype(FONT, SIZE)
    gs = glyphs(font)

    total = sum(w for _, w in gs)
    radius = total / math.radians(SWEEP)

    pad = 300
    canvas = Image.new('RGBA', (int(total * 1.4) + pad, int(total * 0.8) + pad), (0, 0, 0, 0))
    cx = canvas.width / 2.0
    cy = canvas.height * 0.30 + radius        # circle centre, below the word

    walked = -total / 2.0

    for tile, w in gs:
        a = (walked + w / 2.0) / radius       # radians from the top of the circle

        if tile is not None:
            rot = tile.rotate(-math.degrees(a), resample=Image.BICUBIC, expand=True)
            px = cx + math.sin(a) * radius
            py = cy - math.cos(a) * radius
            canvas.alpha_composite(rot, (int(px - rot.width / 2), int(py - rot.height / 2)))

        walked += w

    return canvas.crop(canvas.getbbox())


if not os.path.isdir(OUT):
    os.makedirs(OUT)

if not os.path.isfile(FONT):
    print('no font at ' + FONT)
    sys.exit(1)

art = build()
k = WIDE / float(art.width)
art = art.resize((WIDE, max(1, int(round(art.height * k)))), Image.LANCZOS)

p = os.path.join(OUT, 'logo.png')
art.save(p, optimize=True)

print('  %-12s %dx%d  aspect %.4f  %d KB'
      % ('logo.png', art.width, art.height, art.width / float(art.height),
         os.path.getsize(p) // 1024))
print()
print('  put this in the C# as the wordmark aspect: %.4f' % (art.width / float(art.height)))
