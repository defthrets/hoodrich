# -*- coding: utf-8 -*-
"""
Every line at the same loudness, whoever recorded it and whenever.

THE PACK WAS RECORDED OVER MONTHS AND IT SOUNDS LIKE IT. Measured across the whole of it,
Vernon averages -15.8 dBFS and Tao -28.0 -- twelve decibels, which is not "a bit quieter", it
is one man shouting and another mumbling. The mod had a per-SPEAKER table of levels to bring
the loud ones down, hand-tuned by ear, which is a table somebody has to maintain every time a
take is replaced and which cannot help a line that is quiet within an otherwise loud speaker.

SO IT IS MEASURED AND FIXED IN THE FILE INSTEAD. Every take is brought to the same RMS, which
is loudness as an ear hears it rather than the tallest spike in the waveform, and then held
under a peak ceiling so nothing clips. One pass, no runtime cost, and it stays true because
take_voice.py does the same thing to every new take as it lands.

RMS AND NOT PEAK, because peak is whichever plosive was loudest. Two takes can peak identically
and be six decibels apart to listen to.

    python tools/level_voice.py            what it would do
    python tools/level_voice.py --write    do it
    python tools/level_voice.py --target -20 --ceiling -1

The files are in git and the masters are beside the repo, so this is reversible twice over.
"""

import argparse
import array
import glob
import math
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)

# Anything quieter than this is silence rather than speech, and averaging it in drags every
# measurement toward nothing. A take is mostly room tone between sentences.
FLOOR = 0.002


def chunks(raw):
    """Where the samples are: (offset, length), or None for anything that is not plain PCM."""
    if len(raw) < 44 or raw[0:4] != b"RIFF" or raw[8:12] != b"WAVE":
        return None

    fmt = bits = 0
    at = 12
    found = None

    while at + 8 <= len(raw):
        cid = raw[at:at + 4]
        size = struct.unpack_from("<i", raw, at + 4)[0]

        if size < 0 or at + 8 + size > len(raw):
            if cid == b"data" and at + 8 < len(raw):
                found = (at + 8, len(raw) - at - 8)
            break

        if cid == b"fmt " and size >= 16:
            fmt = struct.unpack_from("<h", raw, at + 8)[0]
            bits = struct.unpack_from("<h", raw, at + 8 + 14)[0]
        elif cid == b"data":
            found = (at + 8, size)

        at += 8 + size + (size & 1)

    if fmt != 1 or bits != 16 or not found:
        return None

    return found


def loudness(samples):
    """RMS over the parts that are actually speech, and the tallest sample."""
    peak = 0
    total = 0.0
    counted = 0

    for s in samples:
        a = abs(s)
        if a > peak:
            peak = a

        f = s / 32768.0
        if abs(f) < FLOOR:
            continue

        total += f * f
        counted += 1

    if counted == 0:
        return 0.0, peak

    return math.sqrt(total / counted), peak


def db(x):
    return 20 * math.log10(x) if x > 0 else -99.0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--write", action="store_true", help="actually change the files")
    ap.add_argument("--target", type=float, default=-20.0, help="RMS to bring every take to, dBFS")
    ap.add_argument("--ceiling", type=float, default=-1.0, help="peak nothing may go above, dBFS")
    ap.add_argument("--dir", default=os.path.join(ROOT, "data", "voice"))
    args = ap.parse_args()

    want = 10 ** (args.target / 20.0)
    roof = 10 ** (args.ceiling / 20.0) * 32767.0

    files = sorted(glob.glob(os.path.join(args.dir, "*.wav")))

    if not files:
        print("no wavs in " + args.dir)
        return 1

    before = []
    after = []
    touched = 0
    left = 0

    for path in files:
        raw = bytearray(open(path, "rb").read())
        where = chunks(raw)

        if not where:
            print("  left alone (not 16-bit PCM): " + os.path.basename(path))
            left += 1
            continue

        at, size = where
        end = at + min(size, len(raw) - at)
        end -= (end - at) % 2

        samples = array.array("h")
        samples.frombytes(bytes(raw[at:end]))

        if not len(samples):
            left += 1
            continue

        rms, peak = loudness(samples)

        if rms <= 0:
            left += 1
            continue

        before.append(db(rms / 1.0))

        gain = want / rms

        # AND NEVER INTO THE CEILING. Bringing a quiet take up is the whole point, but not at
        # the cost of squaring off the top of every consonant.
        if peak > 0:
            most = roof / peak
            if gain > most:
                gain = most

        if abs(gain - 1.0) < 0.02:
            after.append(db(rms))
            continue

        touched += 1

        if args.write:
            for i in range(len(samples)):
                v = int(round(samples[i] * gain))

                if v > 32767:
                    v = 32767
                elif v < -32768:
                    v = -32768

                samples[i] = v

            raw[at:end] = samples.tobytes()
            open(path, "wb").write(bytes(raw))

        after.append(db(rms * gain))

    def spread(v):
        return (min(v), sum(v) / len(v), max(v)) if v else (0, 0, 0)

    lo, mid, hi = spread(before)
    lo2, mid2, hi2 = spread(after)

    print()
    print("%d file(s), %d %s, %d left alone" %
          (len(files), touched, "changed" if args.write else "would change", left))
    print()
    print("  before   %.1f .. %.1f dBFS   (avg %.1f, spread %.1f dB)" % (lo, hi, mid, hi - lo))
    print("  after    %.1f .. %.1f dBFS   (avg %.1f, spread %.1f dB)" % (lo2, hi2, mid2, hi2 - lo2))

    if not args.write:
        print()
        print("nothing written. --write to do it.")

    return 0


if __name__ == "__main__":
    sys.exit(main())
