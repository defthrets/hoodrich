# -*- coding: utf-8 -*-
"""
The gun shop's sign: HOOD ARMOURY, as a white mask.

WHITE ON TRANSPARENT like every other mark in the set, because the colour goes on at draw
time -- the same file is the header on the shop screen and could be a dim mark anywhere else.

THE FACE. The reference is a heavy squared condensed sans with flat terminals, the kind of
thing stencilled on a crate. Bahnschrift Bold Condensed is the closest thing on a stock
Windows box: same DIN squareness, same flat cuts, and it is a variable font so the condensed
instance is a real one rather than a squeezed regular. Impact is the fallback -- heavier and
rounder, wrong in the details, right in the weight -- and Arial Bold behind that so this
always produces a file.

TRACKING AND A RULE. The reference letter-spaces the word and sits it on a heavy horizontal
line, and both are doing as much work as the letterforms: the rule is what makes it a sign
rather than a caption. Drawn per glyph so the tracking is exact.

Rendered four times the size it is used at and shrunk, which is how every mark in this set
is made -- the game scales it down again and clean edges survive that, aliased ones do not.

    python tools/make_armoury.py
"""

import os
import sys

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ICONS = os.path.join(os.path.dirname(HERE), "data", "icons")

OUT = os.path.join(ICONS, "armoury.png")

WORDS = "HOOD ARMOURY"

# Rendered at this height and shrunk by OVER at the end.
SIZE = 240
OVER = 4

# Air between letters, and the extra between the two words, as a share of the size.
TRACK = 0.055
WORD_GAP = 0.16

# The rule: how thick, and how far under the letters, as a share of the cap height.
RULE = 0.115
RULE_GAP = 0.135

# THE STENCIL BRIDGES.
#
# Bahnschrift is not a stencil face, so the cuts are made afterwards -- and they are made PER
# LETTER, down the middle of each glyph, not as bands across the whole word. That is the
# difference between a stencil and a font with lines through it: the bridge is a gap in the
# letterform, so it lands where the letter has ink and does nothing where it has not.
#
# SHORT CUTS AT THE TOP AND THE BOTTOM, not one down the whole letter. A full-height cut
# reads as a letter sawn in half -- it takes the apex out of the A, the join out of the M and
# the stem out of the Y, none of which a stencil does. A stencil bridges where a counter would
# otherwise fall out, and on this word that is the top and bottom of the round letters: the
# two O's, the D, the R's bowl. The same two nicks land harmlessly on the open letters, which
# is exactly what happens on a real stencil sheet.
#
# As a share of the cap height: how wide each cut is, and how far into the letter it reaches.
BRIDGE = 0.055
BRIDGE_IN = 0.26

# Air round the whole thing so nothing is clipped when the game scales it.
PAD = 0.04


def face(size):
    """Bold Condensed where the box has it, then Impact, then anything bold."""
    tries = [
        (r"C:\Windows\Fonts\bahnschrift.ttf", "Bold Condensed"),
        (r"C:\Windows\Fonts\impact.ttf", None),
        (r"C:\Windows\Fonts\arialbd.ttf", None),
    ]

    for path, instance in tries:
        if not os.path.exists(path):
            continue

        try:
            font = ImageFont.truetype(path, size)

            if instance:
                font.set_variation_by_name(instance)

            return font, os.path.basename(path) + (" " + instance if instance else "")
        except Exception:
            continue

    raise SystemExit("no usable font on this machine")


def main():
    big = SIZE * OVER

    font, named = face(big)

    track = int(big * TRACK)
    gap = int(big * WORD_GAP)

    # Measure the whole line per glyph, because that is how it is drawn.
    widths = []
    total = 0

    for i, ch in enumerate(WORDS):
        if ch == " ":
            widths.append(gap)
            total += gap
            continue

        w = int(font.getlength(ch))
        widths.append(w)
        total += w

        if i < len(WORDS) - 1 and WORDS[i + 1] != " ":
            total += track

    # The cap height off a letter with no descender, so the rule sits under the letters
    # rather than under the font's own idea of a baseline.
    box = font.getbbox("H")
    capTop, capBottom = box[1], box[3]
    cap = capBottom - capTop

    rule = max(1, int(cap * RULE))
    ruleGap = int(cap * RULE_GAP)

    pad = int(cap * PAD)

    width = total + pad * 2
    height = cap + ruleGap + rule + pad * 2

    img = Image.new("RGBA", (width, height), (255, 255, 255, 0))
    d = ImageDraw.Draw(img)

    x = pad
    y = pad - capTop

    # Where each glyph's bridge goes: down its own middle.
    cuts = []

    for i, ch in enumerate(WORDS):
        if ch == " ":
            x += widths[i]
            continue

        d.text((x, y), ch, font=font, fill=(255, 255, 255, 255))

        cuts.append(x + widths[i] * 0.5)

        x += widths[i]

        if i < len(WORDS) - 1 and WORDS[i + 1] != " ":
            x += track

    # THE RULE, the full width of the word and nothing wider. It is the thing that makes the
    # reference read as a sign, and it is the one part that is not letterforms.
    # CUT BEFORE THE RULE GOES ON, so the rule underneath stays whole -- a stencil breaks
    # the letters and not the line they stand on.
    bridge = cap * BRIDGE * 0.5

    reach = cap * BRIDGE_IN

    for at in cuts:
        d.rectangle([at - bridge, pad - 1, at + bridge, pad + reach], fill=(0, 0, 0, 0))
        d.rectangle([at - bridge, pad + cap - reach, at + bridge, pad + cap], fill=(0, 0, 0, 0))

    top = pad + cap + ruleGap

    d.rectangle([pad, top, width - pad - 1, top + rule - 1], fill=(255, 255, 255, 255))

    out = img.resize((width // OVER, height // OVER), Image.LANCZOS)
    out.save(OUT)

    w, h = out.size

    print("wrote %s (%dx%d)" % (OUT, w, h))
    print("face: %s" % named)
    print("aspect (w/h): %.4f  <- ArmouryAspect in GunScreen.cs" % (w / float(h)))


if __name__ == "__main__":
    main()
