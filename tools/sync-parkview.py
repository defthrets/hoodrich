# -*- coding: utf-8 -*-
"""
Copy Parkview into Posted Up, or check that the copy is current.

WHY THIS EXISTS. Parkview ships inside Posted Up (2026-09-25) but is still written in its own
repo -- C:\\projects\\parkview -- because that is where it is changed, day to day, on its own.
Two hand-kept copies of the same mod do not stay the same, so there is one source of truth and
this copies it. Same idea as Overspray's tools/sync-paint.py and tools/sync-ledger.py here.

WHAT IS COPIED:

    parkview\\src\\Parkview\\**\\*.cs   ->  src\\Hoodrich\\Parkview\\**      namespace rewritten
    parkview\\data\\**                 ->  parkview\\data\\**              verbatim
    parkview\\Parkview.ini             ->  parkview\\Parkview.ini          verbatim
    parkview\\README.txt               ->  parkview\\README.txt            verbatim

The rewrite is the namespace and nothing else -- Parkview becomes Hoodrich.Parkview -- and it
is deliberately that small: the moment it has to be clever the two are not the same mod any
more. Strings are left alone, so Parkview still reads scripts\\Parkview.ini and keeps its scenes,
captures, rooms and log in scripts\\Parkview\\ exactly as it did as its own dll.

build.ps1 runs this before every compile, so a Posted Up build can never quietly ship an older
Parkview than the one in its repo.

    python tools/sync-parkview.py            copy what differs
    python tools/sync-parkview.py --check    report drift, change nothing, exit 1 if any
"""
import io
import os
import re
import sys

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PARKVIEW = os.path.join(os.path.dirname(HERE), "parkview")

SRC_FROM = os.path.join(PARKVIEW, "src", "Parkview")
SRC_TO = os.path.join(HERE, "src", "Hoodrich", "Parkview")

# Content, copied byte for byte into hoodrich\parkview\ for the deploy and the release zip.
DATA = [
    ("data", "data"),
    ("Parkview.ini", "Parkview.ini"),
    ("README.txt", "README.txt"),
]
DATA_TO = os.path.join(HERE, "parkview")

HEADER = ("// GENERATED -- DO NOT EDIT. This is Parkview, copied in by tools/sync-parkview.py from\n"
          "// C:\\projects\\parkview\\src\\Parkview\\%s. Change it there; the next build overwrites this.\n")

# Declarations and references to the namespace, and nothing that is only the word.
RULES = [
    (re.compile(r"\bnamespace Parkview\b"), "namespace Hoodrich.Parkview"),
    (re.compile(r"\busing Parkview\b"), "using Hoodrich.Parkview"),
    (re.compile(r"(?<![\w.\"])Parkview\.(Core|UI)\b"), r"Hoodrich.Parkview.\1"),
]


def translate(text, rel):
    for pat, rep in RULES:
        text = pat.sub(rep, text)
    return HEADER % rel.replace("/", "\\") + text


def leaks(text):
    """A namespace reference the rules missed: 'Parkview.' in code, outside a string."""
    found = []
    for line in text.splitlines():
        code = re.sub(r'"(?:[^"\\]|\\.)*"', '""', line.split("//")[0])
        for m in re.finditer(r"(?<![\w.])Parkview\.[A-Z]\w*", code):
            found.append(m.group(0))
    return sorted(set(found))


def read(path, text=True):
    if text:
        return io.open(path, encoding="utf-8-sig").read()
    with open(path, "rb") as f:
        return f.read()


def write(path, data, text=True):
    d = os.path.dirname(path)
    if not os.path.isdir(d):
        os.makedirs(d)
    if text:
        io.open(path, "w", encoding="utf-8-sig", newline="").write(data)
    else:
        with open(path, "wb") as f:
            f.write(data)


def files_under(root):
    out = []
    for base, _, names in os.walk(root):
        for n in names:
            out.append(os.path.relpath(os.path.join(base, n), root).replace("\\", "/"))
    return sorted(out)


def main():
    check = "--check" in sys.argv

    if not os.path.isdir(SRC_FROM):
        # Not an error for somebody building Posted Up without Parkview's repo next to it:
        # the copy already in src\Hoodrich\Parkview is what they build.
        print("  parkview: no source at %s -- building the copy already here" % SRC_FROM)
        return 0

    problems = 0
    changed = 0

    # ---- the source ------------------------------------------------------------------------
    names = [n for n in files_under(SRC_FROM) if n.endswith(".cs")]

    for rel in names:
        want = translate(read(os.path.join(SRC_FROM, rel)), rel)

        left = leaks(want)
        if left:
            print("  LEAK   %-24s still says %s" % (rel, ", ".join(left)))
            problems += 1
            continue

        dst = os.path.join(SRC_TO, rel)
        have = read(dst) if os.path.exists(dst) else None

        if have == want:
            continue

        if check:
            print("  DRIFT  src/%-20s %s" % (rel, "missing" if have is None else "differs"))
            problems += 1
            continue

        write(dst, want)
        print("  %-6s src/%s" % ("new" if have is None else "sync", rel))
        changed += 1

    # A file in the copy that is not in Parkview was added on the wrong side -- which is how a
    # fork starts. Said, not deleted: removing a file a sync once wrote is how a sync eats work.
    if os.path.isdir(SRC_TO):
        for rel in files_under(SRC_TO):
            if rel.endswith(".cs") and rel not in names:
                print("  EXTRA  src/%-20s only in hoodrich -- move it to parkview or delete it" % rel)
                problems += 1

    # ---- the data ----------------------------------------------------------------------------
    for src_rel, dst_rel in DATA:
        src = os.path.join(PARKVIEW, src_rel)
        if not os.path.exists(src):
            continue

        pairs = []
        if os.path.isdir(src):
            for rel in files_under(src):
                pairs.append((os.path.join(src, rel), os.path.join(DATA_TO, dst_rel, rel), dst_rel + "/" + rel))
        else:
            pairs.append((src, os.path.join(DATA_TO, dst_rel), dst_rel))

        for a, b, label in pairs:
            want = read(a, text=False)
            have = read(b, text=False) if os.path.exists(b) else None

            if have == want:
                continue

            if check:
                print("  DRIFT  parkview/%-15s %s" % (label, "missing" if have is None else "differs"))
                problems += 1
                continue

            write(b, want, text=False)
            print("  %-6s parkview/%s" % ("new" if have is None else "sync", label))
            changed += 1

    if check:
        print("  parkview: %d file(s) checked, %d problem(s)" % (len(names), problems))
        return 1 if problems else 0

    print("  parkview: %d source file(s), %d copied%s" % (
        len(names), changed, ", %d problem(s)" % problems if problems else ""))
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
