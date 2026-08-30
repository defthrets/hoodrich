# -*- coding: utf-8 -*-
"""
Put the settings a player's ini has never heard of into it, and change nothing else.

WHY THIS EXISTS. Deploying does not overwrite Hoodrich.ini, and it must not: it is the one
file in the install somebody has edited, and a mod that stamps on your settings every time it
updates is a mod you stop updating. The cost of that is a config which falls behind the code --
every release adds keys, the installed file never gains them, and after three of those there
are nineteen settings the mod reads and the player cannot see.

Defaults apply to a missing key, so nothing is broken by the gap. What is broken is TUNING: a
setting you cannot find is a setting you do not have.

So this copies the missing keys across, with the comment block that explains each one, into the
right section -- and it never touches a key that is already there, whatever it has been changed
to. Adding a key at its default is behaviour-neutral by definition, because the default was
already what was in force.

    python tools/topup_ini.py                        # both Steam installs
    python tools/topup_ini.py --dry-run
    python tools/topup_ini.py --target "D:\\GTAV\\scripts\\Hoodrich.ini"
"""

import argparse
import io
import os
import sys

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SOURCE = os.path.join(HERE, "Hoodrich.ini")

STEAM = r"C:\Program Files (x86)\Steam\steamapps\common"

DEFAULT_TARGETS = [
    os.path.join(STEAM, "Grand Theft Auto V", "scripts", "Hoodrich.ini"),
    os.path.join(STEAM, "Grand Theft Auto V Enhanced", "scripts", "Hoodrich.ini"),
]


def read(path):
    return io.open(path, encoding="utf-8-sig").read().splitlines()


def is_section(line):
    s = line.strip()
    return s.startswith("[") and s.endswith("]")


def section_of(line):
    return line.strip()[1:-1].strip()


def key_of(line):
    """The key on a settings line, or None for a comment, a blank or a heading."""
    s = line.strip()

    if not s or s.startswith(";") or s.startswith("#") or is_section(s):
        return None
    if "=" not in s:
        return None

    return s.split("=", 1)[0].strip()


def entries(lines):
    """
    Every key in the file, as section -> {key: (comment_lines, line)}.

    The comment block above a key travels WITH it. A setting arriving in somebody's config with
    no explanation is a setting they will not touch, and the whole point of moving it is that
    they can.
    """
    out = {}
    order = []

    section = ""
    pending = []

    for line in lines:
        if is_section(line):
            section = section_of(line)
            out.setdefault(section, {})
            order.append(section)
            pending = []
            continue

        key = key_of(line)

        if key is None:
            stripped = line.strip()

            # A blank line ends a comment block; a comment adds to it.
            if not stripped:
                pending = []
            elif stripped.startswith(";") or stripped.startswith("#"):
                pending.append(line)

            continue

        out.setdefault(section, {})[key] = (list(pending), line)
        pending = []

    return out, order


def topup(source_path, target_path, dry):
    src_lines = read(source_path)
    src, src_order = entries(src_lines)

    tgt_lines = read(target_path)
    tgt, _ = entries(tgt_lines)

    # What is missing, in the order the shipped file lists it.
    missing = []

    for section in src_order:
        for key in src.get(section, {}):
            if key in tgt.get(section, {}):
                continue
            if (section, key) in [(s, k) for s, k in missing]:
                continue

            missing.append((section, key))

    if not missing:
        print("  ok    %s -- nothing missing" % target_path)
        return 0

    # Where each section ends in the target, so an added key lands under its own heading
    # rather than at the bottom of the file under whatever happens to be last.
    ends = {}
    section = ""
    last_content = -1

    for i, line in enumerate(tgt_lines):
        if is_section(line):
            if section:
                ends[section] = last_content + 1
            section = section_of(line)
            last_content = i
            continue

        if line.strip():
            last_content = i

    if section:
        ends[section] = last_content + 1

    # Built back to front, so the insertion points stay valid as they are used.
    additions = {}

    for sec, key in missing:
        comment, line = src[sec][key]
        block = []

        if comment:
            block.append("")
            block.extend(comment)
        else:
            block.append("")

        block.append(line)
        additions.setdefault(sec, []).extend(block)

    result = list(tgt_lines)
    news = []

    for sec in sorted(additions, key=lambda s: ends.get(s, len(result)), reverse=True):
        if sec in ends:
            result[ends[sec]:ends[sec]] = additions[sec]
        else:
            # A whole section this install has never had.
            news.append(sec)
            result.append("")
            result.append("[" + sec + "]")
            result.extend(additions[sec])

    for sec, key in missing:
        print("    + %-10s %s" % ("[" + sec + "]", key))

    if news:
        print("    (new sections: %s)" % ", ".join(news))

    if dry:
        print("  dry   %s -- %d would be added" % (target_path, len(missing)))
        return len(missing)

    backup = target_path + ".bak"

    # Their file, so it is copied before it is touched. This runs against a live install.
    io.open(backup, "w", encoding="utf-8-sig", newline="").write("\n".join(tgt_lines) + "\n")
    io.open(target_path, "w", encoding="utf-8-sig", newline="").write("\n".join(result) + "\n")

    print("  ok    %s -- %d added (was backed up to %s)"
          % (target_path, len(missing), os.path.basename(backup)))

    return len(missing)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--target", action="append", default=None)
    ap.add_argument("--source", default=SOURCE)
    ap.add_argument("--dry-run", action="store_true")

    args = ap.parse_args()

    if not os.path.exists(args.source):
        print("No source ini at " + args.source)
        return 1

    targets = args.target or DEFAULT_TARGETS
    total = 0

    for t in targets:
        if not os.path.exists(t):
            print("  skip  %s -- not there" % t)
            continue

        total += topup(args.source, t, args.dry_run)

    print()
    print("%d setting(s) %s." % (total, "would be added" if args.dry_run else "added"))

    return 0


if __name__ == "__main__":
    sys.exit(main())
