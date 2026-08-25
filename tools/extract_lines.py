# -*- coding: utf-8 -*-
"""
Pulls every spoken line out of the mod and writes the two files the voice layer needs.

    python tools/extract_lines.py

Writes into data/voice/:

    voice_manifest.json   speaker + text + filename, one entry per line.
                          The mod reads this. It does NOT need to exist for the
                          mod to run -- with no manifest every line is subtitled
                          exactly as it always was.

    to_record.csv         the same lines as a work list: filename, speaker, text.
                          Feed this to ElevenLabs (or anything else) and save each
                          result as data/voice/<filename>.

NO HASHING, AND NOTHING HERE HAS TO AGREE WITH THE MOD ABOUT ANYTHING.

The original plan for this feature keyed the lookup on a SHA1 of the speaker and
the line, computed identically here and in C#, and warned that the two had to
match byte for byte forever. That is two normalisers in two languages that can
never see each other, and the first person to fix a typo in a line breaks the
link silently.

So this script writes down what was said and who said it, in the clear, and the
mod does all the matching with one piece of code on its side. This file cannot
disagree with VoiceIndex because it never makes the same decision twice.

WHAT IT DOES MEAN: if you edit a line of dialogue, re-run this. The manifest will
name the new text and the old WAV will be orphaned -- the mod logs how many lines
it could not find, so drift is visible rather than silent. Re-record just that one.

Format for the audio: 16-bit PCM WAV, mono, 22050 Hz. The mod reads 16-bit PCM
only and will say so in the log for anything else. Mono at 22 kHz is plenty for
speech and is a quarter the size of 44 kHz stereo -- which across 376 lines is
80 MB against 319.
"""
import io
import os
import re
import json
import csv
import glob
import collections

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "src")
DATA = os.path.join(ROOT, "data")
OUT = os.path.join(DATA, "voice")

# A C# string literal, allowing "a" + "b" run together across lines.
CONCAT = r'"(?:[^"\\]|\\.)*"(?:\s*\+\s*"(?:[^"\\]|\\.)*")*'
STR = re.compile(r'"((?:[^"\\]|\\.)*)"')


def joined(text):
    """Every piece of a concatenated literal, as the one string the game shows."""
    out = "".join(p for p in STR.findall(text))
    out = out.replace('\\"', '"').replace("\\n", " ").replace("\\u001f", " ")
    return re.sub(r"\s+", " ", out).strip()


def sources():
    for path in glob.glob(os.path.join(SRC, "**", "*.cs"), recursive=True):
        text = io.open(path, encoding="utf-8-sig", errors="replace").read()
        # Comments are prose about the dialogue, not the dialogue.
        text = re.sub(r"^\s*///.*$", "", text, flags=re.M)
        text = re.sub(r"^\s*//.*$", "", text, flags=re.M)
        yield os.path.relpath(path, ROOT).replace("\\", "/"), text


# ---- who says what ----------------------------------------------------------
#
# Only lines a CHARACTER says out loud. Phone texts, menu labels and the player's
# own choices are not speech and are deliberately not here -- see the exclusions
# at the bottom.

lines = []          # (speaker, text, where)
seen = set()


def add(speaker, text, where):
    speaker = (speaker or "").strip()
    text = (text or "").strip()

    if len(text) < 4:
        return
    # A bare tag or an all-caps speech name is not a sentence.
    if re.fullmatch(r"[A-Z_0-9 ~@\-\.]+", text):
        return

    key = (speaker.lower(), re.sub(r"\s+", " ", text.lower()))
    if key in seen:
        return

    seen.add(key)
    lines.append((speaker, text, where))


for where, text in sources():
    # Node(def, gang, "...") and new DialogueNode("Who", "...")
    for m in re.finditer(r"Node\(\s*def,\s*gang,\s*(" + CONCAT + r")", text, re.S):
        add("", joined(m.group(1)), where)

    for m in re.finditer(r"new DialogueNode\(\s*\"([^\"]+)\"\s*,\s*(" + CONCAT + r")", text, re.S):
        add(m.group(1), joined(m.group(2)), where)

    # Dialogue.Say("Who", "...")
    for m in re.finditer(r"Dialogue\.Say\(\s*\"([^\"]+)\"\s*,\s*(" + CONCAT + r")", text, re.S):
        add(m.group(1), joined(m.group(2)), where)

# The leaders' four lines each, out of the Add(...) table in GangLeaders.
gl = os.path.join(SRC, "Hoodrich", "Gangs", "GangLeaders.cs")

if os.path.exists(gl):
    body = io.open(gl, encoding="utf-8-sig", errors="replace").read()
    body = re.sub(r"^\s*//.*$", "", re.sub(r"^\s*///.*$", "", body, flags=re.M), flags=re.M)

    for m in re.finditer(r"\n\s+Add\(", body):
        i, depth, start, args, instr = m.end() - 1, 0, m.end(), [], False
        while i < len(body):
            c = body[i]
            if instr:
                if c == "\\":
                    i += 2
                    continue
                if c == '"':
                    instr = False
            elif c == '"':
                instr = True
            elif c in "([{":
                depth += 1
            elif c in ")]}":
                depth -= 1
                if depth == 0:
                    args.append(body[start:i])
                    break
            elif c == "," and depth == 1:
                args.append(body[start:i])
                start = i + 1
            i += 1

        if len(args) >= 8:
            who = joined(args[1])
            for a in args[4:8]:
                add(who, joined(a), "GangLeaders.cs")

# The delivery lines, which are spoken with a subtitle and belong to whoever is
# carrying the box. Port* is the man off the boat and Corner* is Gerald, and the
# arrays are named that way precisely so this is not a guess.
dl = os.path.join(SRC, "Hoodrich", "Supply", "Delivery.cs")

if os.path.exists(dl):
    body = io.open(dl, encoding="utf-8-sig", errors="replace").read()
    body = re.sub(r"^\s*//.*$", "", re.sub(r"^\s*///.*$", "", body, flags=re.M), flags=re.M)

    for m in re.finditer(r"string\[\]\s+(Port|Corner)(\w+)\s*=\s*\{(.*?)\};", body, re.S):
        who = "Tao Cheng" if m.group(1) == "Port" else "Gerald"
        for s in STR.findall(m.group(3)):
            add(who, s.replace('\\"', '"'), "Delivery.cs")

# The dealers' authored lines.
for name in ("dealers.json",):
    p = os.path.join(DATA, name)
    if not os.path.exists(p):
        continue

    doc = json.load(io.open(p, encoding="utf-8-sig"))
    rows = doc.get("dealers", doc) if isinstance(doc, dict) else doc

    for e in rows:
        if not isinstance(e, dict):
            continue
        who = e.get("name", "")
        for field in ("greeting", "buyLine", "sourceReply", "sourceTooSoon", "farewell"):
            if isinstance(e.get(field), str):
                add(who, e[field], name)

# ---- write it out -----------------------------------------------------------

os.makedirs(OUT, exist_ok=True)


def slug(who):
    # An empty speaker is a line LeaderTalk serves for whichever of the nine
    # leaders you are stood in front of. It is recorded once and matched on the
    # words alone -- see VoiceIndex -- so it is filed under what it actually is.
    s = re.sub(r"[^a-z0-9]+", "_", (who or "leader").lower()).strip("_")
    return s or "leader"


count = collections.Counter()
manifest = []

for speaker, text, where in lines:
    s = slug(speaker)
    count[s] += 1
    manifest.append({
        "speaker": speaker,
        "text": text,
        "file": "%s_%03d.wav" % (s, count[s]),
        "_from": where,
    })

with io.open(os.path.join(OUT, "voice_manifest.json"), "w", encoding="utf-8") as f:
    f.write(json.dumps({
        "_comment": [
            "Which file says which line. Generated by tools/extract_lines.py -- do not",
            "hand-edit, re-run it instead.",
            "",
            "The mod matches on speaker + text, normalised on its side only (colour codes",
            "stripped, whitespace collapsed, case folded). Nothing here has to agree with",
            "the mod about how that works, which is why editing a line of dialogue and",
            "re-running this is the whole of the maintenance.",
            "",
            "Audio: 16-bit PCM WAV, mono, 22050 Hz, in this folder.",
        ],
        "lines": manifest,
    }, indent=2, ensure_ascii=False) + "\n")

with io.open(os.path.join(OUT, "to_record.csv"), "w", encoding="utf-8-sig", newline="") as f:
    w = csv.writer(f)
    w.writerow(["file", "speaker", "text"])
    for row in manifest:
        w.writerow([row["file"], row["speaker"], row["text"]])

chars = sum(len(r["text"]) for r in manifest)

print("%d lines from %d speakers" % (len(manifest), len(count)))
print("%d characters, roughly %.0f minutes of speech" % (chars, chars / 14.0 / 60))
print()

for s, n in count.most_common(12):
    print("   %-22s %3d" % (s, n))

print()
print("wrote %s" % os.path.join(OUT, "voice_manifest.json"))
print("wrote %s" % os.path.join(OUT, "to_record.csv"))
print()
print("Record each row as data/voice/<file>. Lines with no audio stay subtitle-only,")
print("so you can do one character at a time and test as you go.")
