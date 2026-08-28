# -*- coding: utf-8 -*-
"""
What to record, and what to call the file when you have.

Voice.cs names a recording after what is said -- the speaker, then a hash of the exact line --
so nothing has to be kept in a list by hand and there is no numbering to renumber. The cost of
that is you cannot guess a filename, which is what this is for. It reproduces Voice.Key exactly.

Two ways to get a list, and they answer different questions:

  --log      Read Hoodrich.log and pull out every line that WANTED audio and did not have it.
             Turn LogLevel up to Debug, play, and the game writes your to-record list for you.
             This is the reliable one: it only ever lists lines that actually reached a screen,
             with the speaker the screen actually used.

  --data     Walk the json and list what could be recorded before ever launching the game.
             Misses everything written in the source -- the tryout, the docks, the mission
             briefs -- because the speaker of those is only decided at runtime.

Output is CSV: filename, speaker, text. Feed the text column to ElevenLabs and save each
result as the filename column, into scripts\\Hoodrich\\voice.

    python tools/voice_lines.py --log > to_record.csv
    python tools/voice_lines.py --data --who Gerald > gerald.csv
"""

import argparse
import csv
import glob
import io
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


# ---------------------------------------------------------------- the naming
#
# These three must agree with Voice.cs to the character. If a key here does not match one the
# game logs, the recording is silently never found -- so they are written to mirror the C#
# line for line rather than to be short.

def tidy(line):
    """Voice.Tidy -- drop ~tag~ regions, flatten whitespace."""
    out, skipping = [], False

    for c in line:
        if c == "~":
            skipping = not skipping
            continue
        if skipping:
            continue
        out.append(" " if c in "\n\r\t" else c)

    text, collapsed, space = "".join(out), [], False

    for c in text:
        if c == " ":
            if not space:
                collapsed.append(c)
            space = True
        else:
            collapsed.append(c)
            space = False

    return "".join(collapsed).strip()


def slug(name):
    """Voice.Slug -- lowercase, runs of anything else become one underscore."""
    if not name:
        return "someone"

    out = []
    for c in name:
        if c.isalnum():
            out.append(c.lower())
        elif out and out[-1] != "_":
            out.append("_")

    return "".join(out).strip("_")


def fnv1a(text):
    """Voice.Hash -- FNV-1a, 32 bit, over the UTF-8 bytes."""
    h = 2166136261
    for b in text.encode("utf-8"):
        h = ((h ^ b) * 16777619) & 0xFFFFFFFF
    return h


def key(speaker, line):
    """Voice.Key."""
    return "%s_%08x" % (slug(speaker), fnv1a(tidy(line)))


# ------------------------------------------------------------------- the log
#
# Voice.Say writes one of these every time it comes up empty, which makes playing the game the
# most accurate way to find out what needs recording.

MISS = re.compile(r"Voice: nothing for (\S+) -- (.*)$")


def from_log(path):
    if not os.path.exists(path):
        sys.exit("no log at %s -- set LogLevel=Debug and play a bit first" % path)

    seen, rows = set(), []

    for raw in io.open(path, encoding="utf-8", errors="replace"):
        m = MISS.search(raw)
        if not m:
            continue

        name, text = m.group(1), m.group(2).rstrip()
        if name in seen:
            continue

        seen.add(name)
        rows.append((name + ".mp3", name.rsplit("_", 1)[0], text))

    return rows


# ------------------------------------------------------------------ the data
#
# Every speaker here is known without running anything, which is what makes them listable up
# front. The source-authored speeches are not, and that is what --log is for.

def from_data(root):
    rows, seen = [], set()

    def add(speaker, text):
        if not text or len(text) < 12 or text.count(" ") < 2:
            return

        name = key(speaker, text)
        if name in seen:
            return

        seen.add(name)
        rows.append((name + ".mp3", speaker, tidy(text)))

    def load(f):
        p = os.path.join(root, "data", f)
        return json.load(io.open(p, encoding="utf-8-sig")) if os.path.exists(p) else None

    # Dealers say their own shop lines.
    doc = load("dealers.json")
    for d in (doc or {}).get("dealers", []):
        for field in ("greeting", "buyLine", "sourceReply", "sourceTooSoon", "farewell",
                      "numberLine", "openingText"):
            add(d.get("name", ""), d.get(field))
        for field in ("textCalled", "textLeaving", "textOutside"):
            for line in d.get(field) or []:
                add(d.get("name", ""), line)

    # Lamar owns the mission list outright.
    doc = load("missions.json")
    for m in (doc or {}).get("missions", []):
        for field in ("brief", "done", "briefAgain", "doneAgain"):
            add("Lamar Davis", m.get(field))
        for line in m.get("briefMore") or []:
            add("Lamar Davis", line)

    # And the feed, where the author's own name is on every voice.
    doc = load("socials.json") or {}
    who = {a.get("voice"): a.get("name") for a in doc.get("authors", []) if a.get("voice")}
    for voice, cats in (doc.get("voices") or {}).items():
        for lines in (cats or {}).values():
            for line in lines or []:
                add(who.get(voice, voice), line)

    return rows


# ----------------------------------------------------------------------- cli

def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--log", nargs="?", const="", metavar="PATH",
                    help="read misses out of Hoodrich.log")
    ap.add_argument("--data", action="store_true", help="list what the json holds")
    ap.add_argument("--who", metavar="NAME", help="only this speaker")
    ap.add_argument("--root", default=HERE)
    args = ap.parse_args()

    if args.log is None and not args.data:
        ap.error("pick --log or --data")

    rows = []

    if args.log is not None:
        path = args.log or find_log(args.root)
        rows += from_log(path)

    if args.data:
        rows += from_data(args.root)

    if args.who:
        want = args.who.lower()
        rows = [r for r in rows if want in r[1].lower()]

    out = csv.writer(sys.stdout, lineterminator="\n")
    out.writerow(("filename", "speaker", "text"))
    out.writerows(rows)

    sys.stderr.write("  %d line(s)\n" % len(rows))


def find_log(root):
    """The log follows Paths.Writable, so look where it actually lands."""
    tries = glob.glob(r"C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V*"
                      r"\scripts\Hoodrich.log")
    tries += [os.path.join(os.path.expanduser("~"), "Documents", "Hoodrich", "Hoodrich.log"),
              os.path.join(root, "Hoodrich.log")]

    for p in tries:
        if os.path.exists(p):
            return p

    return tries[-1]


if __name__ == "__main__":
    main()
