# -*- coding: utf-8 -*-
"""
Copies the files every mod of the set shares -- UI/Ledger.cs, the machine-wide rectangle
tally, and Core/Pace.cs, the tick watchdog -- from this repo into the others, rewriting the
namespace and the mod's name. Core/Petrol.cs goes only where it is used; see EXTRAS. Same idea as tools/sync-paint.py in Overspray: one copy is
edited, the rest are written, and a diff between any two is a bug.

    python tools/sync-ledger.py            writes what differs
    python tools/sync-ledger.py --check    says what differs, writes nothing, exit 1 if any
"""
import io
import os
import sys

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PROJECTS = os.path.dirname(HERE)

# repo folder -> (namespace root, the name the logs use)
MODS = {
    "bare-minimum": ("BareMinimum", "Bare Minimum"),
    "fumes": ("Fumes", "Fumes"),
    "five0patrol": ("Five0Patrol", "Five0 Patrol"),
    "bloodymess": ("BloodyMess", "Bloody Mess"),
    "overspray": ("Overspray", "Overspray"),
}

# Everybody gets these two: the rectangle tally and the tick watchdog are about the machine,
# so a mod that draws anything or holds a tick needs them whether it knows it or not.
FILES = [
    ("src/Hoodrich/UI/Ledger.cs", "src/{root}/UI/Ledger.cs"),
    ("src/Hoodrich/Core/Pace.cs", "src/{root}/Core/Pace.cs"),
]

# Petrol is NOT universal, and sending it everywhere was a mistake worth naming. It is how a
# mod says a car of its own is scenery and burns no fuel, so it is only of use to a mod that
# spawns cars and to the fuel mod that reads the flag. It was going to all five: four of them
# carried it as dead code, compiled into the dll, and a reader of Bloody Mess's public repo
# found a gore mod shipping a file about drug takeovers and car meets.
#
# Checked rather than assumed - hoodrich has 5 call sites and fumes 2; bare-minimum,
# five0patrol, bloodymess and overspray had none between them.
#
# The copies already sitting in those four repos are not removed by this script. Deleting a
# file it once wrote is how a sync tool eats work somebody meant to keep; they come out by
# hand, in their own repo, once.
EXTRAS = {
    "fumes": [
        ("src/Hoodrich/Core/Petrol.cs", "src/{root}/Core/Petrol.cs"),
    ],
}


def read(path):
    return io.open(path, encoding="utf-8-sig").read()


def write(path, s):
    d = os.path.dirname(path)
    if not os.path.isdir(d):
        os.makedirs(d)
    io.open(path, "w", encoding="utf-8-sig", newline="").write(s)


def render(src, root, name):
    s = read(src)
    s = s.replace("namespace Hoodrich.", "namespace %s." % root)
    s = s.replace("using Hoodrich.Core;", "using %s.Core;" % root)
    s = s.replace('Me = "Posted Up"', 'Me = "%s"' % name)
    return s


def main():
    check = "--check" in sys.argv
    differ = 0
    for repo, (root, name) in MODS.items():
        base = os.path.join(PROJECTS, repo)
        if not os.path.isdir(os.path.join(base, "src", root)):
            print("  skip  %s (no src/%s)" % (repo, root))
            continue
        for src, dst in FILES + EXTRAS.get(repo, []):
            want = render(os.path.join(HERE, src), root, name)
            target = os.path.join(base, dst.format(root=root))
            have = read(target) if os.path.exists(target) else None
            if have == want:
                continue
            differ += 1
            if check:
                print("  differs  %s/%s" % (repo, dst.format(root=root)))
            else:
                write(target, want)
                print("  sync  %s/%s" % (repo, dst.format(root=root)))
    if check and differ:
        sys.exit(1)
    print("%d file(s) %s" % (differ, "differ" if check else "written"))


if __name__ == "__main__":
    main()
