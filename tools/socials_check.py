# -*- coding: utf-8 -*-
"""
Checks data\\socials.json, and says how the feed READS.

WHY THIS EXISTS. The feed is five and a half thousand template lines and a typo in one of
them is invisible: an unknown slot prints its own braces in play, which nobody is looking at
the phone to notice, and a duplicate line just makes the block repeat itself. Both are found
here in a second.

AND THE SECOND HALF IS A MIRROR. A register is not something you can judge by reading your
own writing -- everything you wrote sounds like you. So it is measured instead: words per
post, how many lines carry an abbreviation, how many carry an emoji, how many still have
their apostrophes. Written on 2026-09-22, when the answer was 11.2 words, 1.7% and 0.0%, and
the whole file read as one thoughtful bystander rather than as a street.

    python tools\\socials_check.py            everything
    python tools\\socials_check.py --sets     the per-set table only
    python tools\\socials_check.py --quiet    errors only; exit 1 if any

NOTE SETS. A post set whose name starts with an underscore is prose left for whoever opens
the JSON next, not lines anybody will ever read in play -- SocialFeed asks for sets by name
("Sale", "SaleLSD") and no name it builds begins with one. They are skipped here, both by the
checker and by the measurements, so a paragraph of explanation does not get counted as the
street talking.

The slot names the CODE fills are listed in Builtin below. They are not in the data file, so
this is the one place the two have to be kept in step: see Social.SocialFeed.ValueFor.
"""
import io
import json
import os
import re
import sys
from collections import Counter

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.join(os.path.dirname(HERE), "data", "socials.json")

# Filled by SocialFeed.ValueFor rather than by the slots table. Taken from the source with
#   grep -o 'string.Equals(key, "[a-zA-Z]*"' src\Hoodrich\Social\SocialFeed.cs
# rather than from memory, because a name missing here reports good data as broken.
BUILTIN = {"count", "here", "money", "rival", "street", "subject", "theircolour", "theirs", "you", "yours"}

SLOT = re.compile(r"\{([^{}]*)\}")
WORD = re.compile(r"[A-Za-z']+")
EMOJI = re.compile("[\U0001F300-\U0001FAFF☀-➿⬀-⯿]")

ABBREV = set("""fr ts ngl tbh otw idc wyd wsp hmu lmk ong otp lls smh rn atm gng ykwim nvm
istg icl ard bet lowkey highkey deadass tho tryna finna gotta wanna imma ima nun sum dat dem
dis bruh bro blood cuz twin slime gang op opps rip ll lld free lmao lmaoo lmaooo lol yktv
oomf idk ion nfs wtf af""".split())


def load():
    with io.open(DATA, encoding="utf-8-sig") as f:
        return json.load(f)


def lines_of(doc):
    """Every template line in the file, as (where, line)."""
    out = []

    for name, lines in doc.get("posts", {}).items():
        if name.startswith("_"):
            continue  # a note to whoever opens the file next; see NOTE SETS.
        for line in lines:
            out.append(("posts/" + name, line))

    for voice, sets in doc.get("voices", {}).items():
        for name, lines in sets.items():
            if isinstance(lines, list):
                for line in lines:
                    out.append(("voices/" + voice + "/" + name, line))

    for name, lines in doc.get("comments", {}).items():
        if isinstance(lines, list):
            for line in lines:
                out.append(("comments/" + name, line))

    return out


def check(doc):
    """Everything that is wrong with it. Empty means it is sound."""
    bad = []
    known = BUILTIN | set(doc.get("slots", {}).keys())

    for where, line in lines_of(doc):
        if not line or not line.strip():
            bad.append("%s: an empty line" % where)
            continue

        for key in SLOT.findall(line):
            if key not in known:
                bad.append("%s: unknown slot {%s} -- it will print its own braces: %s"
                           % (where, key, line[:70]))

        if line.count("{") != line.count("}"):
            bad.append("%s: unbalanced braces: %s" % (where, line[:70]))

    for name, lines in doc.get("posts", {}).items():
        seen = Counter(lines)
        for line, n in seen.items():
            if n > 1:
                bad.append("posts/%s: said %d times: %s" % (name, n, line[:70]))

    handles = Counter(a.get("handle", "") for a in doc.get("authors", []))
    for handle, n in handles.items():
        if n > 1:
            bad.append("authors: @%s appears %d times" % (handle, n))

    voices = set(doc.get("voices", {}).keys())
    for a in doc.get("authors", []):
        v = a.get("voice", "")
        if v and v not in voices:
            bad.append("authors: @%s has voice '%s' and there is no such voice"
                       % (a.get("handle", "?"), v))

    return bad


def measure(lines):
    n = len(lines)
    if n == 0:
        return None

    words = ends = caps = apos = abbrev = emoji = 0

    for raw in lines:
        s = SLOT.sub("X", raw).strip()
        ws = WORD.findall(s)
        words += len(ws)

        if s.endswith((".", "!", "?")):
            ends += 1
        if s and s[0].isupper():
            caps += 1
        if "'" in s:
            apos += 1
        if any(w.lower().strip("'") in ABBREV for w in ws):
            abbrev += 1
        if EMOJI.search(raw):
            emoji += 1

    p = lambda x: 100.0 * x / n
    return dict(n=n, words=words / float(n), ends=p(ends), caps=p(caps),
                apos=p(apos), abbrev=p(abbrev), emoji=p(emoji))


def table(doc, least=12):
    rows = [(k, v) for k, v in doc.get("posts", {}).items()
            if len(v) >= least and not k.startswith("_")]
    rows.sort(key=lambda kv: -len(kv[1]))

    print("%-24s %5s %6s %7s %7s %7s %7s" % ("set", "lines", "words", "Cap", "apost", "abbrev", "emoji"))
    print("-" * 66)

    for k, v in rows:
        m = measure(v)
        print("%-24s %5d %6.1f %6.1f%% %6.1f%% %6.1f%% %6.1f%%"
              % (k[:24], m["n"], m["words"], m["caps"], m["apos"], m["abbrev"], m["emoji"]))

    every = [l for k, v in doc.get("posts", {}).items() if not k.startswith("_") for l in v]
    m = measure(every)
    print("-" * 66)
    print("%-24s %5d %6.1f %6.1f%% %6.1f%% %6.1f%% %6.1f%%"
          % ("EVERY POST", m["n"], m["words"], m["caps"], m["apos"], m["abbrev"], m["emoji"]))


def main():
    args = set(sys.argv[1:])
    quiet = "--quiet" in args

    doc = load()
    bad = check(doc)

    if bad:
        print("%d problem(s):" % len(bad))
        for b in bad[:60]:
            print("   " + b)
        if len(bad) > 60:
            print("   ... and %d more" % (len(bad) - 60))
    elif not quiet:
        print("Sound: every slot is one the code fills, nothing is said twice, every voice exists.")

    if not quiet:
        print()
        table(doc)

    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
