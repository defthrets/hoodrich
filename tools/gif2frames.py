# -*- coding: utf-8 -*-
"""
A GIF into the frames the HUD can draw.

    python tools/gif2frames.py <file.gif> <name> [--scale N]

Writes data/icons/<name>_0.png, <name>_1.png ... one per frame, every frame the FULL canvas
(so nothing shifts between them), composited the way the GIF says (disposal honoured), RGBA
with the transparency kept. Prints the frame count and the delay, which is what Draw.Animated
wants told.

The HUD draws these through ScriptHookV's texture loader, which knows nothing of GIFs: it
decodes one image into one texture. So an animation is a flipbook of PNGs, exactly the way the
Graffiti app's logo sprays itself on, and this makes the flipbook.

--scale N upsamples with NEAREST, which is the only right answer for pixel art: the game's
sprite draw is bilinear, and a 64-pixel frame drawn at a hundred and fifty pixels goes to
soup unless the pixels are already fat. 4 is the default; 1 keeps the file as it is.
"""
import os
import sys

from PIL import Image

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(HERE, "data", "icons")


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if len(args) != 2:
        print(__doc__)
        sys.exit(2)
    src, name = args
    scale = 4
    for a in sys.argv[1:]:
        if a.startswith("--scale"):
            scale = int(a.split("=", 1)[1]) if "=" in a else int(sys.argv[sys.argv.index(a) + 1])

    im = Image.open(src)
    frames = getattr(im, "n_frames", 1)
    delays = []
    written = []

    for i in range(frames):
        im.seek(i)
        delays.append(int(im.info.get("duration", 100)))
        frame = im.convert("RGBA")
        if scale != 1:
            frame = frame.resize((frame.width * scale, frame.height * scale), Image.NEAREST)
        path = os.path.join(OUT, "%s_%d.png" % (name, i))
        frame.save(path, optimize=True)
        written.append(path)

    delay = int(round(sum(delays) / float(len(delays)))) if delays else 100
    print("%d frame(s) of %dx%d, %d ms each -> data/icons/%s_N.png" % (
        frames, frame.width, frame.height, delay, name))
    print("Draw.Animated(\"%s\", %d, %d, ...)" % (name, frames, delay))
    if len(set(delays)) > 1:
        print("note: the GIF's delays vary (%s); the flipbook plays them all at %d ms" % (delays, delay))


if __name__ == "__main__":
    main()
