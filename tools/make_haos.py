# -*- coding: utf-8 -*-
"""
The car lot's sign: HAOS AUTOS, as a white mask.

REVERSED OUT, WHICH IS WHY THIS IS ITS OWN TOOL. The armoury's sign is letters. This one is
BLOCKS with letters knocked out of them, and in a white-on-transparent mask that inverts
cleanly: the blocks are the ink, so they take the panel's colour at draw time, and the letters
are holes, so the panel's own background shows through them. Nothing has to be painted the
colour of whatever it happens to be sitting on.

One block per letter, butted up with a hairline of nothing between them, which is what the
reference does -- it reads as a row of stamped plates rather than as a single black bar.

Bahnschrift Bold Condensed for the same reasons make_armoury.py gives, with Impact and Arial
Bold behind it so this always produces a file.

Rendered four times the size it is used at and shrunk, like every mark in the set.

    python tools/make_haos.py
"""

import os

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ICONS = os.path.join(os.path.dirname(HERE), "data", "icons")

OUT = os.path.join(ICONS, "haos.png")

WORDS = "HAOS AUTOS"

SIZE = 240
OVER = 4

# Air inside a block, round its letter: sideways and above/below, as a share of the cap height.
INSET_X = 0.16
INSET_Y = 0.13

# The hairline between blocks, and the wider gap between the two words.
SPLIT = 0.045
WORD_GAP = 0.30

# Air round the whole thing so nothing is clipped when the game scales it.
PAD = 0.04


def face(size):
    """The same three, in the same order, as the armoury's sign."""
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

    box = font.getbbox("H")
    capTop = box[1]
    cap = box[3] - box[1]

    insetX = int(cap * INSET_X)
    insetY = int(cap * INSET_Y)
    split = max(1, int(cap * SPLIT))
    wordGap = int(cap * WORD_GAP)
    pad = int(cap * PAD)

    blockH = cap + insetY * 2

    # Measure the row first: one block per letter, plus the splits and the word gap.
    widths = []
    total = 0

    for i, ch in enumerate(WORDS):
        if ch == " ":
            widths.append(wordGap)
            total += wordGap
            continue

        w = int(font.getlength(ch)) + insetX * 2
        widths.append(w)
        total += w

        # A hairline after every block except the last of a word.
        if i < len(WORDS) - 1 and WORDS[i + 1] != " ":
            total += split

    width = total + pad * 2
    height = blockH + pad * 2

    img = Image.new("RGBA", (width, height), (255, 255, 255, 0))
    d = ImageDraw.Draw(img)

    # THE BLOCKS FIRST, as solid ink.
    x = pad

    letters = []

    for i, ch in enumerate(WORDS):
        if ch == " ":
            x += widths[i]
            continue

        d.rectangle([x, pad, x + widths[i] - 1, pad + blockH - 1], fill=(255, 255, 255, 255))

        letters.append((ch, x + insetX))

        x += widths[i]

        if i < len(WORDS) - 1 and WORDS[i + 1] != " ":
            x += split

    # AND THE LETTERS KNOCKED OUT OF THEM. Drawn into a mask and subtracted, because writing
    # transparent pixels straight over the block would only blend them.
    holes = Image.new("L", (width, height), 0)
    hd = ImageDraw.Draw(holes)

    for ch, at in letters:
        hd.text((at, pad + insetY - capTop), ch, font=font, fill=255)

    alpha = img.getchannel("A")

    from PIL import ImageChops
    img.putalpha(ImageChops.subtract(alpha, holes))

    out = img.resize((width // OVER, height // OVER), Image.LANCZOS)
    out.save(OUT)

    w, h = out.size

    print("wrote %s (%dx%d)" % (OUT, w, h))
    print("face: %s" % named)
    print("aspect (w/h): %.4f  <- HaosAspect in CarScreen.cs" % (w / float(h)))


if __name__ == "__main__":
    main()
