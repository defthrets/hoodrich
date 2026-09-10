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

  --source   The whole sentences written into the C# talk files, which is most of what the
             people you stand in front of actually say.

  --feed     The social posts. OFF BY DEFAULT, because a post is read on a phone screen and
             nobody speaks it -- they were most of every list this ever printed and not one of
             them was ever going to be recorded.

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
        # A DEALER CAN BE TWO PEOPLE. The docks entry carries an "afterCheng" block: who is
        # stood there once the old man has been told about his son, sober and on time and a
        # different name entirely. It is loaded through the same reader as the block above and
        # every line in it is a line somebody says out loud -- and no list has ever included
        # one of them, so his whole part has been invisible to whoever was recording.
        for block in (d, d.get("afterCheng") or {}):
            who = block.get("name") or d.get("name", "")
            if not who:
                continue

            for field in ("greeting", "buyLine", "sourceReply", "sourceTooSoon", "farewell",
                          "numberLine", "openingText"):
                add(who, block.get(field))
            for field in ("textCalled", "textLeaving", "textOutside"):
                for line in block.get(field) or []:
                    add(who, line)

        # And the shop line, which the source builds with the money on you and the room at the
        # house stapled to the end, so it names itself rather than hashing: <slug>_shop when
        # the stock is whole, <slug>_shopcut when it has been stepped on. The recording leaves
        # the arithmetic out; the screen is already showing it. See DealerTalk.Node.
        for tag, text in (("shop", "Ain't got time to stand here. What you taking? Nothing here's been touched."),
                          ("shopcut", "Ain't got time to stand here. What you taking? It's all stepped on already.")):
            name = slug(d.get("name", "")) + "_" + tag
            if name not in seen:
                seen.add(name)
                rows.append((name + ".mp3", d.get("name", ""), text))

    # Lamar owns the mission list outright.
    #
    # "Lamar", NOT "Lamar Davis". The key is built from whatever string the screen was handed
    # as the speaker, and that comes from Fixer.Name, which is the bare first name. This said
    # Lamar Davis for a long time, so every mission row it printed was named lamar_davis_...
    # and not one of them was ever a file the game asked for. A recording made from this list
    # would have sat in the folder in silence.
    doc = load("missions.json")
    for m in (doc or {}).get("missions", []):
        for field in ("brief", "done", "briefAgain", "doneAgain"):
            add("Lamar", m.get(field))
        for line in m.get("briefMore") or []:
            add("Lamar", line)

    return rows


# ------------------------------------------------------------------- the feed
#
# NOBODY SPEAKS A POST. The feed is text on a phone screen and it is read, not heard, so its
# thousands of lines are not work waiting to be done -- they were the bulk of every list this
# tool has ever printed and none of it was ever going to be recorded.
#
# It is still listable, because the day somebody wants a voice reading the timeline out it is
# one flag away. Off by default is the only part that changed.
#
# Half of them could not be recorded anyway: a post is written with {hood}, {street} and the
# rest, filled in from wherever the player is standing when it goes up, and a key is a hash of
# the FINISHED sentence -- so the same post names a different file in every neighbourhood.
# That is the feature working, not a fault to fix.

def from_feed(root):
    rows, seen = [], set()

    p = os.path.join(root, "data", "socials.json")
    doc = json.load(io.open(p, encoding="utf-8-sig")) if os.path.exists(p) else {}

    who = {a.get("voice"): a.get("name") for a in doc.get("authors", []) if a.get("voice")}

    for voice, cats in (doc.get("voices") or {}).items():
        speaker = who.get(voice, voice)

        for lines in (cats or {}).values():
            for line in lines or []:
                if not line or len(line) < 12 or line.count(" ") < 2:
                    continue

                name = key(speaker, line)
                if name in seen:
                    continue

                seen.add(name)
                rows.append((name + ".mp3", speaker, tidy(line)))

    return rows


# --------------------------------------------------------------- the source
#
# A conversation written in C# is still a fixed sentence -- FixerTalk hands Node() a literal
# and the screen says it word for word. Those are as recordable as anything in the json and
# --data used to miss every one of them.
#
# ONLY THE ONES THAT ARE WHOLE. "Gimme like " + minutes + " minutes" is three pieces and only
# one of them is written down; the game keys those by tag instead (see Voiced/Voice.Named), so
# a half-sentence lifted out of one would name a file nothing ever asks for.

# WHICH NAME EACH FILE BUILDS ITS NODES WITH.
#
# Every one of these has a Node() helper that stamps the same speaker on everything it makes,
# so the file IS the speaker -- except DealerTalk, where the node is built with whichever
# dealer you walked up to, and both of them can reach most of it. Two names there, and a line
# from that file wants a recording under each.
#
# PortRun writes its speaker out in the call, so it is listed with none and answers for
# itself below.

SPEECH = [
    (r"src\Hoodrich\Gangs\LeaderTalk.cs",       ["Gerald"]),
    (r"src\Hoodrich\Locations\ArmourerTalk.cs", ["Stretch"]),
    (r"src\Hoodrich\Locations\HaoTalk.cs",      ["Hao"]),
    (r"src\Hoodrich\Missions\FixerTalk.cs",     ["Lamar"]),
    (r"src\Hoodrich\Missions\BikeRide.cs",      ["Lamar"]),
    (r"src\Hoodrich\Missions\Hunt.cs",          ["Lamar"]),
    (r"src\Hoodrich\Missions\TagRun.cs",        ["Lamar"]),
    (r"src\Hoodrich\Missions\PortRun.cs",       []),
    (r"src\Hoodrich\Supply\DealerTalk.cs",      ["Tao Cheng", "Gerald"]),
    (r"src\Hoodrich\Supply\Stoop.cs",           []),
]

CALL = re.compile(r'\b(?:Node|Nothing)\(\s*(?:[A-Za-z_][\w.]*\s*,\s*){0,2}((?:"(?:[^"\\]|\\.)*"\s*\+?\s*)+)', re.S)
ASSIGN = re.compile(r'\bline\s*=\s*((?:"(?:[^"\\]|\\.)*"\s*\+?\s*)+);', re.S)
EXPLICIT = re.compile(r'new DialogueNode\(\s*"([^"]+)"\s*,\s*((?:"(?:[^"\\]|\\.)*"\s*\+?\s*)+)', re.S)
PIECE = re.compile(r'"((?:[^"\\]|\\.)*)"')


def from_source(root):
    rows, seen = [], set()

    def add(speaker, line):
        if not speaker or len(line) < 12 or line.count(" ") < 2:
            return

        name = key(speaker, line)
        if name in seen:
            return

        seen.add(name)
        rows.append((name + ".mp3", speaker, tidy(line)))

    def joined(blob):
        # \n IS A LINE BREAK, NOT TWO CHARACTERS. Voice.Tidy flattens whitespace before it
        # hashes, so a paragraph break in a speech becomes one space -- and a key built from
        # a literal backslash-n instead is a key the game will never ask for. Every long
        # speech in this mod has paragraph breaks in it.
        out = []
        for p in PIECE.findall(blob):
            out.append(p.replace('\\"', '"')
                        .replace("\\n", "\n").replace("\\r", "\r").replace("\\t", "\t")
                        .replace("\\\\", "\\"))
        return "".join(out)

    for rel, speakers in SPEECH:
        path = os.path.join(root, rel)
        if not os.path.exists(path):
            continue

        text = io.open(path, encoding="utf-8-sig").read()

        # Comment lines are prose about the code, and there is a great deal of it.
        body = "\n".join(l for l in text.split("\n")
                         if not l.strip().startswith("//"))

        def whole(m, group):
            """False when the literal runs on into a variable and is only half a sentence."""
            if m.group(group).rstrip().endswith("+"):
                return False
            return not body[m.end():].lstrip().startswith("+")

        # A node that names its own speaker answers for itself, whatever the file is.
        for m in EXPLICIT.finditer(body):
            if whole(m, 2):
                add(m.group(1), joined(m.group(2)))

        for rx in (CALL, ASSIGN):
            for m in rx.finditer(body):
                if not whole(m, 1):
                    continue

                line = joined(m.group(1))

                for who in speakers:
                    add(who, line)

    return rows


# ------------------------------------------------------------------ the have
#
# Which of these are already sat in the voice folder. The answer is what somebody actually
# wants from this: not "here is everything", but "here is what is left".

def recorded(folder):
    have = set()

    if folder and os.path.isdir(folder):
        for f in os.listdir(folder):
            stem, ext = os.path.splitext(f)
            if ext.lower() in (".wav", ".mp3"):
                have.add(stem.lower())

    return have


def find_voice(root):
    tries = glob.glob(r"C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V*"
                      r"\scripts\Hoodrich\voice")
    tries += [os.path.join(os.path.expanduser("~"), "Documents", "Hoodrich", "voice")]

    for p in tries:
        if os.path.isdir(p):
            return p

    return ""


# ----------------------------------------------------------------------- cli

def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--log", nargs="?", const="", metavar="PATH",
                    help="read misses out of Hoodrich.log")
    ap.add_argument("--data", action="store_true", help="list what the json holds")
    ap.add_argument("--source", action="store_true",
                    help="list the whole sentences written into the C#")
    ap.add_argument("--feed", action="store_true",
                    help="include the social posts, which are read rather than heard")
    ap.add_argument("--who", metavar="NAME", help="only this speaker")
    ap.add_argument("--need", nargs="?", const="", metavar="DIR",
                    help="only the ones not in the voice folder already")
    ap.add_argument("--root", default=HERE)
    args = ap.parse_args()

    if args.log is None and not args.data and not args.source and not args.feed:
        ap.error("pick --log, --data, --source or --feed")

    rows = []

    if args.log is not None:
        path = args.log or find_log(args.root)
        rows += from_log(path)

    if args.data:
        rows += from_data(args.root)

    if args.source:
        rows += from_source(args.root)

    if args.feed:
        rows += from_feed(args.root)

    # The same line can come out of two of those at once -- a mission brief is in the json and
    # in the log the moment it has been on screen once.
    rows = list({r[0]: r for r in rows}.values())

    if args.who:
        want = args.who.lower()
        rows = [r for r in rows if want in r[1].lower()]

    if args.need is not None:
        have = recorded(args.need or find_voice(args.root))

        # A LINE TWO PEOPLE CAN SAY IS ONE LINE. DealerTalk builds its nodes with whichever
        # dealer you walked up to, so every sentence in it comes out under both names -- but
        # most of them sit behind a branch only one of the two can reach, and there is no way
        # to tell which from the text.
        #
        # The recordings already know. If one of the names has a take of a line, that is who
        # says it, and the other name is not missing anything. What is left over is the lines
        # nobody has recorded under any name, which are the ones actually worth looking at.
        byhash = {}
        for r in rows:
            byhash.setdefault(os.path.splitext(r[0])[0].rsplit("_", 1)[-1], []).append(r)

        keep = []
        for r in rows:
            me = os.path.splitext(r[0])[0].lower()
            if me in have:
                continue

            kin = byhash[me.rsplit("_", 1)[-1]]
            if any(os.path.splitext(o[0])[0].lower() in have for o in kin if o is not r):
                continue

            keep.append(r)

        rows = keep

    rows.sort(key=lambda r: (r[1].lower(), r[2]))

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
