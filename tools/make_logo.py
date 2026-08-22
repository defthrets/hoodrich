# -*- coding: utf-8 -*-
#
# The wordmark: POSTED UP, arched, as a white mask.
#
# White on transparent like every other icon in the set, because the colour goes on at draw
# time -- the same file is the dim mark on a panel header and a bright one on a dark screen.
#
# The reference is a varsity/college block face and no varsity font is installed on a stock
# Windows box, so this is Impact -- the only heavy condensed face that ships -- widened a
# little and TRACKED OUT, which is the part that matters. Impact sets almost solid and the
# reference has real air between its letters; without the tracking the arch just reads as a
# squashed headline rather than as a wordmark.
#
# Rendered per glyph and laid along the top of a circle rather than warped as one image: a
# warped bitmap smears the strokes, and at header size a smeared stroke is all you see.

import math
import os

from PIL import Image, ImageDraw, ImageFont

OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'data', 'icons')

TEXT = 'POSTED UP'
FONT = os.path.join(os.environ.get('WINDIR', r'C:\Windows'), 'Fonts', 'impact.ttf')

SIZE = 300          # per-glyph render height, before the arch
SWEEP = 26.0        # degrees the whole word covers
SQUEEZE = 1.15      # Impact is narrower than the reference
TRACK = 26          # pixels of air between letters, at SIZE
WIDE = 1024         # the file's width


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
