# -*- coding: utf-8 -*-
"""
Files in, recordings named. The other half of voice_lines.py.

voice_lines.py answers "what do I still need to record". This answers "I have recorded it,
now what is it called" -- and it is worth having as a tool rather than doing by hand, because
the answer is a hash of the exact sentence and a filename typed one character wrong is a file
the game will never look for. Nothing goes wrong that anybody can see; the line simply stays
silent, and it stays silent for as long as nobody thinks to check.

WHAT IT MATCHES ON. A take comes out of the recorder named after its own words -- the
sentence, lowercased, with everything that is not a letter turned into a dash, cut off at
whatever length the tool cuts it off at. That is a PREFIX of the line, and a prefix long
enough to be unique is a better key than the order somebody happened to drop the files in.
So no list is kept, nothing has to be renamed by hand first, and dropping thirty takes in one
folder and running this once is the whole workflow.

Converted rather than copied. Every recording in the pack is 22050Hz 16-bit mono PCM. Voice
plays an mp3 perfectly well -- it prefers one, in fact -- but a pack that is one format is a
pack somebody can reason about, and ffmpeg is already on this machine.

    python tools/take_voice.py                       everything in Downloads
    python tools/take_voice.py --from some\\folder     somewhere else
    python tools/take_voice.py --dry                 say what it would do

Anything it cannot place is listed rather than skipped quietly, because a take that does not
match a line is the interesting case: it usually means the line was reworded after it was
sent off to be read.
"""

import argparse
import io
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)

sys.path.insert(0, HERE)

import voice_lines as V  # noqa: E402  -- one extraction, not two


SOUND = (".mp3", ".wav", ".m4a", ".ogg", ".flac", ".aac")


def slugify(text):
    return re.sub(r"-+", "-", re.sub(r"[^a-z0-9]+", "-", text.lower())).strip("-")


# Below this a name cannot identify anything; at or above the second, a name long enough to be
# its own line is trusted even when it is the head of two of them.
MIN_NAME = 12
SURE_NAME = 24


def candidates(slug, order):
    """
    Every line a take could be, and how sure we are, best kind first.

    ONE IS THE START OF THE OTHER is the ordinary case: a take is named after the head of its
    own sentence, cut off wherever the recorder cuts it off.

    THE TAKE IS SOMEWHERE INSIDE THE LINE is the awkward one. Recorders drop leading
    interjections -- "Ho --" off the front, one "ayy" out of four -- so the name begins a word
    or two in and no prefix test will ever see it. The words are still there, just not at the
    front, and finding them there is worth the second pass because the alternative is somebody
    renaming files by hand against a list of hashes.
    """
    starts = [f for f in order if f.startswith(slug) or slug.startswith(f)]
    if starts:
        return starts, "head"

    return [f for f in order if slug in f], "inside"


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--from", dest="src",
                    default=os.path.join(os.path.expanduser("~"), "Downloads"),
                    help="where the takes are (default: Downloads)")
    ap.add_argument("--into", default=os.path.join(ROOT, "data", "voice"))
    ap.add_argument("--since", type=float, default=0,
                    help="only files touched in the last N hours")
    ap.add_argument("--dry", action="store_true", help="say what it would do and stop")
    ap.add_argument("--root", default=ROOT)
    args = ap.parse_args()

    # Every line the mod can say, from the same two readers the to-record list uses. The feed
    # is left out for the reason it is always left out -- see voice_lines.
    lines = {}
    for name, speaker, text in V.from_data(args.root) + V.from_source(args.root):
        lines.setdefault(slugify(text), (os.path.splitext(name)[0], speaker, text))

    if not lines:
        sys.exit("no lines found -- is --root pointing at the repo?")

    # Longest first, so a take whose slug is a prefix of two lines lands on the longer one
    # rather than on whichever the dictionary happened to yield first.
    order = sorted(lines, key=len, reverse=True)

    takes = []
    for f in sorted(os.listdir(args.src)):
        if not f.lower().endswith(SOUND):
            continue
        p = os.path.join(args.src, f)
        if args.since:
            import time
            if time.time() - os.path.getmtime(p) > args.since * 3600:
                continue
        takes.append(p)

    if not takes:
        sys.exit("no recordings in %s" % args.src)

    done, stuck = [], []

    for path in takes:
        slug = slugify(os.path.splitext(os.path.basename(path))[0])

        if len(slug) < MIN_NAME:
            stuck.append((path, "the name is too short to identify a line"))
            continue

        found, how = candidates(slug, order)

        if not found:
            stuck.append((path, "no line in the mod has these words in it"))
            continue

        # ONE ANSWER, OR A LONG ENOUGH NAME TO TRUST THE LONGEST. order is sorted longest
        # first, so the old behaviour is the second branch -- a full-length take whose head
        # happens to open two lines lands on the longer of them, the way it always did.
        if len(found) > 1 and not (how == "head" and len(slug) >= SURE_NAME):
            stuck.append((path, "could be %d different lines -- put more of the sentence "
                                "in the name" % len(found)))
            continue

        hit = lines[found[0]]

        key, speaker, text = hit
        dest = os.path.join(args.into, key + ".wav")

        if os.path.exists(dest):
            stuck.append((path, key + ".wav is already in the pack"))
            continue

        if args.dry:
            done.append((key + ".wav", speaker, 0, os.path.basename(path)))
            continue

        r = subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", path,
                            "-ac", "1", "-ar", "22050", "-c:a", "pcm_s16le", dest],
                           capture_output=True, text=True)

        if r.returncode != 0 or not os.path.exists(dest):
            stuck.append((path, "ffmpeg: " + (r.stderr or "").strip()[:90]))
            continue

        done.append((key + ".wav", speaker, os.path.getsize(dest), os.path.basename(path)))

    for name, speaker, size, src in sorted(done):
        print("  ok  %-24s %-12s %8s   <- %s"
              % (name, speaker, size or "dry", src[:52]))

    for path, why in stuck:
        print("  --  %s\n      %s" % (os.path.basename(path)[:74], why))

    print()
    print("%d placed, %d left alone." % (len(done), len(stuck)))


if __name__ == "__main__":
    main()
