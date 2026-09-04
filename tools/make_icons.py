# -*- coding: utf-8 -*-
#
# Every icon in the mod, drawn. See iconkit.py for the rules and the three tones; the
# drawings themselves are in icons_a.py (the interface, money, places, people), icons_b.py
# (the product and the tools) and icons_c.py (the sets' marks).
#
#   python tools\make_icons.py            every icon
#   python tools\make_icons.py skull car  just those
#
# make_icons_old.py is the 64-pixel set this replaced, kept for the record.

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import iconkit

GROUPS = []
for name in ("icons_a", "icons_b", "icons_c"):
    try:
        GROUPS.append(__import__(name))
    except ImportError as ex:
        if name not in str(ex):
            raise
        print("  (no %s yet)" % name)

if __name__ == "__main__":
    wanted = set(a.rstrip("_") for a in sys.argv[1:])
    drew = 0

    for group in GROUPS:
        for fn in group.ALL:
            if wanted and fn.__name__.rstrip("_") not in wanted:
                continue
            fn()
            drew += 1

    if wanted and not drew:
        print("no icon called: " + ", ".join(sorted(wanted)))
        sys.exit(1)

    print("%d icon(s) written" % drew)
