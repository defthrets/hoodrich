"""
HOODRICH wheel redesign -- direction "list": not a wheel at all.

Renders a 1920x1080 design sheet to preview/wheel_list.png showing a centred
VERTICAL LIST that keeps the point-and-release input model of the radial wheel
but throws away the ring.

Everything drawn here is reproducible with Draw.cs primitives only:
  Draw.Rect / Draw.RectFrom / Draw.RectUniform   -> every box, rule, hairline, rail
  Draw.File                                      -> every icon (white PNG mask, tinted)
  Draw.Text / Draw.TextRight / Draw.MeasureText / Draw.Fit  -> every string
No wedges. No discs. No gradients on the HUD. No rounded corners. No blur.
The only shapes that are not axis-aligned rectangles are the pointer chevrons
and the back arrow, and those are drawn here exactly as the C# would draw them:
four or five stacked rects each, the same trick Draw.Wedge already uses.

The background is procedural garbage standing in for a game scene -- bright and
sun-blown on the left, dark and shadowed on the right, so the HUD has to survive
both. Blur/noise apply ONLY to that stand-in photo, never to the HUD.
"""

import os
import math
import random
from PIL import Image, ImageDraw, ImageFont, ImageFilter

W, H = 1920, 1080
ROOT = r"C:\projects\hoodrich"
ICONS = os.path.join(ROOT, "data", "icons")
OUT = os.path.join(ROOT, "preview", "wheel_list.png")

# ---------------------------------------------------------------- palette
# Straight out of src/Hoodrich/UI/Palette.cs. Nothing invented.
BACKDROP   = (0, 0, 0, 110)          # Palette.Backdrop, dropped from 140 to 110
SEGMENT    = (10, 12, 14, 200)       # Palette.Segment
SEG_OFF    = (10, 12, 14, 150)       # Palette.Alpha(Palette.Segment, 150)
SEG_HOVER  = (240, 242, 240, 240)    # Palette.SegmentHover
HUB        = (8, 9, 11, 225)         # Palette.Hub
PANEL_HEAD = (22, 24, 26, 235)       # Palette.PanelHeader
ROW_ALT    = (255, 255, 255, 26)     # Palette.PanelRowAlt
TEXT       = (255, 255, 255, 245)    # Palette.Text
TEXT_DIM   = (176, 179, 181, 190)    # Palette.TextDim
TEXT_ON_HV = (16, 18, 20, 255)       # Palette.TextOnHover
TEXT_OFF   = (150, 152, 156, 255)    # Palette.TextDisabled
ACCENT     = (245, 245, 245, 255)    # Palette.Accent
CASH       = (126, 190, 79, 255)     # Palette.Cash
WARN       = (232, 177, 44, 255)     # Palette.Warn
DANGER     = (214, 69, 58, 255)      # Palette.Danger

# Sheet annotation only -- deliberately a hue that is NOT in Palette.cs, so
# nothing on this page can be mistaken for something the mod draws.
NOTE       = (150, 186, 222, 255)
NOTE_DIM   = (128, 160, 192, 255)

# ---------------------------------------------------------------- geometry
# All of these are screen fractions, the same convention RadialMenu.cs uses.
# RAIL_W is expressed as a HEIGHT fraction run through ToX() so the list keeps
# its proportions on ultrawide, exactly like InnerRadius/OuterRadius do today:
#   RailWidth = Draw.ToX(0.818f)   -> 0.46 of the width at 16:9.
RAIL_W  = 0.46          # normalized x
HEAD_H  = 0.044         # header strip height, fraction of screen height
ROW_H   = 0.048         # row pitch, fraction of screen height  (51.8 px @1080)
GAP     = 0.010         # gap between the list and the stat strip
PAD     = 0.010         # horizontal inset inside the rail (normalized x)

ICON_X   = 0.0295       # icon centre, from rail left (normalized x)
ICON_S   = 0.028        # icon side, fraction of screen HEIGHT (Draw.File is square)
LABEL_X  = 0.055        # label column left
DETAIL_X = 0.175        # detail column left
VAL_PAD  = 0.012        # value right edge, in from rail right
CHEV_PAD = 0.007        # submenu chevron, in from rail right
GUTTER   = 0.014        # clear space kept between detail and value

RAIL_TRACK_X = 0.016    # pointer rail centre, out from rail left
RAIL_TRACK_W = 0.0045   # pointer rail width (normalized x)

PX = lambda f: f * W    # normalized-x  -> pixels
PY = lambda f: f * H    # normalized-y  -> pixels


# ---------------------------------------------------------------- fonts
def bahn(size, name="SemiBold Condensed"):
    f = ImageFont.truetype(r"C:\Windows\Fonts\bahnschrift.ttf", size)
    try:
        f.set_variation_by_name(name)
    except Exception:
        pass
    return f


# Only three type sizes live inside the HUD. The current design has four
# (wedge label, wedge icon glyph, hub title, readout title) plus a fifth in the
# side panel; this has LABEL / META / MICRO and nothing else.
F_LABEL  = bahn(27)                  # Draw.FontLabel  @ ~0.42   -- row names, caps
F_TITLE  = bahn(23)                  # Draw.FontLabel  @ ~0.36   -- header title, caps
F_MICRO  = bahn(16)                  # Draw.FontLabel  @ ~0.26   -- strip column heads
F_META   = ImageFont.truetype(r"C:\Windows\Fonts\segoeui.ttf", 17)   # Draw.FontBody @ ~0.29
F_CRUMB  = bahn(17, "Condensed")

F_NOTE   = ImageFont.truetype(r"C:\Windows\Fonts\segoeuisl.ttf", 15)
F_NOTEB  = ImageFont.truetype(r"C:\Windows\Fonts\segoeuib.ttf", 15)


# ---------------------------------------------------------------- primitives
class Canvas:
    """Only ever calls things Draw.cs can do."""

    def __init__(self, im):
        self.im = im
        self.d = ImageDraw.Draw(im, "RGBA")

    # Draw.RectFrom(left, top, w, h, colour)
    def rect(self, l, t, w, h, c):
        self.d.rectangle([l, t, l + w - 1, t + h - 1], fill=c)

    # Draw.Text(...) -- shadow on, because Draw.Text sets SET_TEXT_DROP_SHADOW.
    def text(self, s, x, y, font, c, anchor="lm", shadow=True):
        if not s:
            return
        if shadow:
            self.d.text((x + 1.4, y + 1.4), s, font=font, fill=(0, 0, 0, 150), anchor=anchor)
        self.d.text((x, y), s, font=font, fill=c, anchor=anchor)

    # Draw.File(file, x, y, heightFraction, 0f, tint) -- square, tinted white mask.
    def icon(self, name, cx, cy, side, c):
        path = os.path.join(ICONS, name)
        if not os.path.exists(path):
            return False
        src = Image.open(path).convert("RGBA")
        src = src.resize((int(round(side)), int(round(side))), Image.LANCZOS)
        r, g, b, a = src.split()
        tint = Image.merge("RGBA", (
            r.point(lambda v: int(v * c[0] / 255)),
            g.point(lambda v: int(v * c[1] / 255)),
            b.point(lambda v: int(v * c[2] / 255)),
            a.point(lambda v: int(v * c[3] / 255)),
        ))
        box = (int(round(cx - side / 2)), int(round(cy - side / 2)))
        self.im.paste(tint, box, tint)
        return True

    # A coarse triangle out of four stacked rects. This is the SAME trick
    # Draw.Wedge and Draw.Disc already use, at four rows instead of hundreds.
    def tri(self, cx, cy, w, h, c, up=True):
        rows = 4
        for i in range(rows):
            f = (i + 0.5) / rows
            ww = w * (f if up else (1 - f) + 1.0 / rows)
            yy = cy - h / 2 + h * i / rows
            self.d.rectangle([cx - ww / 2, yy, cx + ww / 2, yy + h / rows], fill=c)


def measure(s, font):
    return font.getlength(s) if s else 0.0


def fit(s, maxw, font):
    """Draw.Fit -- trim to width with an ellipsis."""
    if not s or maxw <= 0:
        return s
    if measure(s, font) <= maxw:
        return s
    lo, hi = 0, len(s)
    while lo < hi:
        mid = (lo + hi + 1) // 2
        if measure(s[:mid] + "...", font) <= maxw:
            lo = mid
        else:
            hi = mid - 1
    if lo <= 0:
        return ""
    return s[:lo].rstrip(" ,") + "..."


# ---------------------------------------------------------------- background
def scene():
    """Stand-in for gameplay. Bright left, dark right, so the HUD is tested over both."""
    random.seed(11)
    im = Image.new("RGB", (W, H), (90, 96, 104))
    d = ImageDraw.Draw(im)

    # Sky, hazy LA afternoon.
    for y in range(H):
        f = y / H
        d.line([(0, y), (W, y)],
               fill=(int(158 - 66 * f), int(168 - 66 * f), int(180 - 58 * f)))

    # Sun blowout, left third. Concentric ellipses -- background only.
    ov = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    od = ImageDraw.Draw(ov, "RGBA")
    for r in range(620, 0, -18):
        a = int(33 * (1 - r / 620.0) ** 1.6) + 2
        od.ellipse([360 - r, 150 - r * 0.72, 360 + r, 150 + r * 0.72],
                   fill=(255, 240, 205, a))
    im = Image.alpha_composite(im.convert("RGBA"), ov).convert("RGB")
    d = ImageDraw.Draw(im)

    # Blocky skyline. Progressively darker to the right.
    x = -40
    while x < W:
        bw = random.randint(70, 210)
        bh = random.randint(150, 470)
        dark = 0.22 + 0.72 * (x / float(W))
        base = int(162 - 116 * dark)
        col = (base, base - 6, base - 12)
        d.rectangle([x, 560 - bh, x + bw, 700], fill=col)
        # windows
        for wy in range(560 - bh + 16, 690, 26):
            for wx in range(x + 12, x + bw - 12, 24):
                if random.random() < 0.55:
                    v = random.randint(-18, 34)
                    d.rectangle([wx, wy, wx + 12, wy + 14],
                                fill=(max(0, base + v), max(0, base + v - 4), max(0, base + v - 10)))
        x += bw + random.randint(4, 18)

    # Foreground silhouettes -- a pole, a wall, a parked car roofline.
    d.rectangle([1290, 300, 1318, 760], fill=(30, 31, 34))
    d.rectangle([1180, 292, 1430, 312], fill=(30, 31, 34))
    d.rectangle([0, 470, 210, 720], fill=(58, 52, 46))
    d.rectangle([1520, 640, 1920, 720], fill=(41, 43, 47))

    # Road.
    d.rectangle([0, 700, W, H], fill=(72, 74, 78))
    d.rectangle([0, 700, W, 712], fill=(126, 128, 130))
    for i in range(0, W, 190):
        d.rectangle([i + 40, 900, i + 150, 912], fill=(196, 190, 150))
    # Kerb shadow, right side darker.
    ov = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    od = ImageDraw.Draw(ov, "RGBA")
    for i in range(60):
        od.rectangle([W - i * 34, 0, W, H], fill=(0, 0, 6, 3))
    im = Image.alpha_composite(im.convert("RGBA"), ov).convert("RGB")

    # Film grain, then a touch of blur so it reads photographic rather than CG.
    grain = Image.effect_noise((W, H), 22).convert("L")
    im = Image.blend(im, Image.merge("RGB", (grain, grain, grain)), 0.07)
    # RGB, not RGBA: ImageDraw.Draw(im, "RGBA") only alpha-BLENDS when the ink
    # mode differs from the image mode. On an RGBA canvas every translucent
    # rect overwrites instead of compositing, which is not what Draw.Rect does.
    return im.filter(ImageFilter.GaussianBlur(1.2)).convert("RGB")


# ---------------------------------------------------------------- the design
def draw_list(c, left_f, top_f, crumb, title, subtitle, items, hovered,
              analogue=0.0, strip=None, rail=True):
    """
    One page of the redesign.

      left_f, top_f  rail left edge / top edge, as screen fractions
      crumb          dim breadcrumb before the title, "" at the root
      items          [{label, icon, detail, value, tint, submenu, enabled, reason}]
      hovered        index the stick is currently resolving to, or -1
      analogue       -1..1, where inside the hovered band the stick actually is
      strip          optional docked stat block (see below)

    Returns the bottom edge as a screen fraction.
    """
    L = PX(left_f)
    R = PX(left_f + RAIL_W)
    T = PY(top_f)
    headh = PY(HEAD_H)
    rowh = PY(ROW_H)
    n = len(items)

    # ---- header strip -------------------------------------------------
    # This is the ONLY place a page identifies itself. The hub disc is gone,
    # the top-of-screen readout is gone. Title, breadcrumb and page subtitle
    # all live on one 47px band welded to the top of the list.
    c.rect(L, T, R - L, headh, PANEL_HEAD)
    c.rect(L, T + headh - 3, R - L, 3, ACCENT)

    tx = L + PX(PAD)
    if crumb:
        # Stepped back-chevron: five rects, pointing left. Same trick the wedge
        # filler already uses, at five rows instead of several hundred.
        for i in range(5):
            f = (i + 0.5) / 5.0
            hh = 19 * f + 3
            c.rect(tx + i * 4, T + headh / 2 - hh / 2, 4, hh, ACCENT)
        tx += 30
        c.text(crumb, tx, T + headh / 2 + 1, F_CRUMB, TEXT_DIM)
        tx += measure(crumb, F_CRUMB) + 8
    c.text(title.upper(), tx, T + headh / 2 + 1, F_TITLE, ACCENT)

    if subtitle:
        c.text(subtitle, R - PX(PAD), T + headh / 2 + 1, F_META, TEXT_DIM, anchor="rm")

    listtop = T + headh

    # ---- pointer rail -------------------------------------------------
    # The one thing a ring gives you that a list does not: continuous feedback
    # on where the stick is between two choices. Two rects buy it back.
    if rail and n:
        rx = L - PX(RAIL_TRACK_X)
        rw = PX(RAIL_TRACK_W)
        c.rect(rx - rw / 2, listtop, rw, rowh * n, SEGMENT)
        if hovered >= 0:
            my = listtop + rowh * (hovered + 0.5) + analogue * rowh * 0.5
            c.rect(rx - rw / 2, my - rowh * 0.34, rw, rowh * 0.68, ACCENT)
        c.tri(rx, listtop - 13, 13, 9, (176, 179, 181, 150), up=True)
        c.tri(rx, listtop + rowh * n + 13, 13, 9, (176, 179, 181, 150), up=False)

    # ---- rows ---------------------------------------------------------
    for i, it in enumerate(items):
        y = listtop + i * rowh
        hov = (i == hovered)
        on = it.get("enabled", True)

        # ONE separation mechanism: a contiguous stack of fills with a hairline
        # between them. No gaps, no hover reach, no inset. The highlight IS the
        # selection, exactly as the vanilla weapon wheel does it.
        fill = SEG_HOVER if hov else (SEGMENT if on else SEG_OFF)
        c.rect(L, y, R - L, rowh, fill)
        if i < n - 1 and not hov:
            c.rect(L, y + rowh - 2, R - L, 2, ROW_ALT)

        # Disabled is a TYPE state, not a fill state. The old design separated
        # enabled from disabled by four to six values out of 255 and you could
        # not see it. Here the label greys, the icon greys, the value is
        # replaced by a padlock and the detail is replaced by the reason,
        # in amber. It costs nothing and it is unmissable.
        if hov:
            lab_c, det_c, ico_c = TEXT_ON_HV, (16, 18, 20, 210), TEXT_ON_HV
        elif on:
            lab_c, det_c, ico_c = TEXT, TEXT_DIM, TEXT
        else:
            lab_c, det_c, ico_c = TEXT_OFF, WARN, (150, 152, 156, 170)

        cy = y + rowh / 2
        if it.get("icon"):
            c.icon(it["icon"], L + PX(ICON_X), cy, PY(ICON_S), ico_c)

        c.text(it["label"].upper(), L + PX(LABEL_X), cy + 1, F_LABEL, lab_c)

        # Value column, right-aligned (Draw.TextRight).
        val_r = R - PX(CHEV_PAD if it.get("submenu") else VAL_PAD)
        if it.get("submenu"):
            val_r -= 14
            c.text(">", R - PX(CHEV_PAD), cy, F_LABEL,
                   TEXT_ON_HV if hov else (TEXT_DIM if on else (150, 152, 156, 130)),
                   anchor="rm")

        val_w = 0.0
        if on and it.get("value"):
            vc = it.get("tint") or (CASH if it.get("cash") else TEXT)
            if hov:
                vc = TEXT_ON_HV
            c.text(it["value"], val_r, cy + 1, F_META, vc, anchor="rm")
            val_w = measure(it["value"], F_META)
        elif not on:
            c.icon("locked.png", val_r - PY(0.009), cy, PY(0.018), (150, 152, 156, 200))
            val_w = PY(0.018) + 4

        # Detail column, left-aligned at a FIXED x so five rows read as a table
        # rather than as five sentences. Trimmed to whatever the value left it.
        det = it["detail"] if on else it.get("reason", "")
        dx = L + PX(DETAIL_X)
        room = (val_r - val_w - PX(GUTTER)) - dx
        c.text(fit(det, room, F_META), dx, cy + 1, F_META, det_c)

    bottom = listtop + rowh * n

    # ---- docked stat strip --------------------------------------------
    # The old right-hand panel made the whole composition lean. This one is the
    # same width as the list and hangs directly off the bottom of it, so the
    # thing stays one centred column no matter how much data a page carries.
    if strip:
        st = bottom + PY(GAP)
        if strip["mode"] == "cols":
            pad = PY(0.014)
            rows = max(len(col["rows"]) for col in strip["cols"])
            sh = pad * 2 + PY(0.030) + rows * PY(0.027)
            c.rect(L, st, R - L, sh, HUB)

            cw = (R - L) / len(strip["cols"])
            for ci, col in enumerate(strip["cols"]):
                cl = L + ci * cw
                if ci:
                    c.rect(cl - 1, st + pad, 2, sh - pad * 2, ROW_ALT)

                hy = st + pad + PY(0.015)
                ix = cl + PX(PAD) + PY(0.010)
                c.icon(col["icon"], ix, hy, PY(0.020), TEXT_DIM)
                c.text(col["head"].upper(), ix + PY(0.016), hy + 1, F_MICRO, TEXT_DIM)
                c.rect(cl + PX(PAD), st + pad + PY(0.028), cw - PX(PAD) * 2, 1,
                       (176, 179, 181, 70))

                ry = st + pad + PY(0.030)
                for (lab, val, tint) in col["rows"]:
                    lx = cl + PX(PAD)
                    rr = cl + cw - PX(PAD)
                    c.text(lab, lx, ry + PY(0.0135), F_META, TEXT_DIM)
                    room = rr - lx - measure(lab, F_META) - PX(GUTTER)
                    c.text(fit(val, room, F_META), rr, ry + PY(0.0135), F_META,
                           tint or TEXT, anchor="rm")
                    ry += PY(0.027)
            bottom = st + sh
        else:  # "cells" -- a short page's stats read better as a row than a column
            pad = PY(0.014)
            sh = pad * 2 + PY(0.050)
            c.rect(L, st, R - L, sh, HUB)
            cw = (R - L) / len(strip["cells"])
            for ci, (ico, lab, val, tint) in enumerate(strip["cells"]):
                cl = L + ci * cw
                if ci:
                    c.rect(cl - 1, st + pad, 2, sh - pad * 2, ROW_ALT)
                ix = cl + PX(PAD) + PY(0.010)
                c.icon(ico, ix, st + pad + PY(0.013), PY(0.020), TEXT_DIM)
                c.text(lab.upper(), ix + PY(0.016), st + pad + PY(0.014), F_MICRO, TEXT_DIM)
                c.text(val, cl + PX(PAD), st + pad + PY(0.038), F_META, tint or TEXT)
            bottom = st + sh

    return bottom / float(H)


def plate(c, x_f, y_f, w_f, h_f):
    """Backing for SHEET ANNOTATION only. Nothing the mod draws."""
    c.rect(PX(x_f), PY(y_f), PX(w_f), PY(h_f), (6, 8, 12, 205))


def caption(c, x_f, y_f, kicker, body):
    x = PX(x_f)
    y = PY(y_f)
    c.rect(x - 9, y - 13, PX(RAIL_W) + 18, 27, (6, 8, 12, 190))
    c.rect(x, y - 8, 3, 17, NOTE)
    c.text(kicker, x + 11, y, F_NOTEB, NOTE, shadow=True)
    c.text(body, x + 11 + measure(kicker, F_NOTEB) + 9, y, F_NOTE, NOTE_DIM, shadow=True)


# ---------------------------------------------------------------- content
# Every string below comes from src/Hoodrich/Wheel/WheelPages.cs or data/drugs.json.

ROOT_ITEMS = [
    dict(label="Weapons", icon="guns.png",
         detail="Opens the game's own weapon wheel", value="Carbine Rifle"),
    dict(label="Dealing", icon="bong.png", submenu=True,
         detail="Re-up, bag up, go to work", value="58 ready, 224 to prep"),
    dict(label="Gangs", icon="mask.png", submenu=True,
         detail="You run with the Families", value="TGF"),
    dict(label="Inventory", icon="stash.png",
         detail="Everything you are carrying", value="4 kinds, 58g"),
    dict(label="Socials", icon="tattoo.png", enabled=False,
         detail="What the block is saying, and what you say back",
         reason="Not right now", value="1,204 followers"),
]

DEALING_ITEMS = [
    dict(label="Re-up", icon="money.png", submenu=True,
         detail="Text a contact for bulk weight", value="$14,250", cash=True),
    dict(label="Post up", icon="cash.png", submenu=True, enabled=False,
         detail="Stand on a corner and let it come to you",
         reason="All you have is weight -- prep it first"),
    dict(label="The numbers", icon="health.png",
         detail="Prices, heat, and what this block does to both", value=""),
]

DEALING_STRIP = dict(mode="cols", cols=[
    dict(head="On you", icon="stash.png", rows=[
        ("Marijuana", "40 g to prep", TEXT_DIM),
        ("Meth", "18 g to prep", TEXT_DIM),
        ("", "", None),
        ("", "", None),
    ]),
    dict(head="At the house", icon="garage.png", rows=[
        ("Marijuana", "180 g to prep", TEXT),
        ("Meth", "94 g to prep", TEXT),
        ("Crack", "60 g to prep", TEXT),
        ("Heroin", "35 g to prep", TEXT),
    ]),
])

POSTUP_ITEMS = [
    dict(label="Marijuana", icon="weed.png", value="28 baggies", cash=True,
         detail="a gram $20  \u00b7  an eighth $50  \u00b7  an ounce $200"),
    dict(label="Cocaine", icon="coke.png", value="6 g", cash=True,
         detail="a gram $250  \u00b7  an 8-ball $850"),
    dict(label="Meth", icon="meth.png", value="94 g", cash=True,
         detail="a point $20  \u00b7  a gram $200  \u00b7  stepped on, they'll notice"),
    dict(label="Crack", icon="crack.png", value="60 rocks", cash=True,
         detail="a dub rock $20  \u00b7  a fifty rock $50  \u00b7  a hundred rock $100"),
    dict(label="Oxycodone", icon="pills.png", value="40 pills", cash=True,
         detail="a pill $25  \u00b7  two pills $50  \u00b7  four pills $100"),
    dict(label="Heroin", icon="heroin.png", enabled=False,
         detail="a point $10  \u00b7  a half $40  \u00b7  a gram $80",
         reason="Families don't touch it on their own block", value="35 g"),
    dict(label="Alprazolam", icon="xanax.png", value="12 bars", cash=True,
         detail="a bar $40  \u00b7  two bars $75  \u00b7  five bars $170"),
]

POSTUP_STRIP = dict(mode="cells", cells=[
    ("pin.png", "This spot", "Ballas hold it", DANGER),
    ("footfall.png", "Foot traffic", "busy", CASH),
    ("people.png", "Gang around", "two of yours", CASH),
])

STATE_ROWS = [
    dict(label="Idle", icon="deal.png", detail="Segment 200 alpha, white label, dim detail",
         value="value"),
    dict(label="Pointed at", icon="deal.png", detail="SegmentHover 240, TextOn() punches it dark",
         value="value"),
    dict(label="Disabled", icon="deal.png", enabled=False,
         detail="Segment at 150 -- the scene shows through",
         reason="Reason replaces detail, in Warn amber", value="x"),
    dict(label="Submenu", icon="deal.png", submenu=True,
         detail="Chevron in the right gutter, value moves in", value="4 things"),
]


# ---------------------------------------------------------------- compose
def main():
    im = scene()
    c = Canvas(im)

    # Full-screen dim, as RadialMenu.Render does first.
    # 110 rather than 140: every string in this design sits on its own opaque
    # fill, so the backdrop no longer has to do the contrast work on its own,
    # and you can still see the street you are standing in.
    c.rect(0, 0, W, H, BACKDROP)

    LC = 0.022
    RC = 0.518

    # ---- left column ---------------------------------------------------
    caption(c, LC, 0.038, "A  ROOT",
            "5 items  \u00b7  Dealing pointed at  \u00b7  Socials disabled  \u00b7  no stat strip")
    draw_list(c, LC, 0.055, "", "Hoodrich", "Grove Street Families",
              ROOT_ITEMS, hovered=1, analogue=0.34)

    caption(c, LC, 0.400, "ROW STATES",
            "the whole vocabulary -- four fills, three type colours")
    draw_list(c, LC, 0.417, "", "States", "one row, four ways",
              STATE_ROWS, hovered=1, analogue=0.0, rail=False)

    # Notes block. Sheet annotation, not HUD.
    ny = 0.700
    plate(c, LC - 0.005, ny - 0.024, RAIL_W + 0.010, 0.212)
    notes = [
        ("REMOVED", "hub disc \u00b7 top-of-screen readout \u00b7 right-hand panel \u00b7 "
                    "wedge rasterisation \u00b7 3\u00b0 gaps \u00b7 1.03 hover reach \u00b7 0.96 hub inset"),
        ("TYPE", "three sizes, not five. LABEL 0.42 condensed caps \u00b7 "
                 "META 0.29 body \u00b7 MICRO 0.26 condensed caps"),
        ("INPUT", "unchanged model. index = round((1 - dirY) / 2 \u00d7 (n-1)), "
                  "deadzone clears it, release commits"),
        ("COST", "panel C in full is 34 rects, 31 strings, 11 sprites. The ring "
                 "costs 593 rects for FIVE wedges, before a word is drawn"),
    ]
    for k, v in notes:
        c.rect(PX(LC), PY(ny) - 8, 3, 17, NOTE)
        c.text(k, PX(LC) + 11, PY(ny), F_NOTEB, NOTE)
        # wrap the body by hand
        words = v.split(" ")
        line, lines = "", []
        for wd in words:
            t = (line + " " + wd).strip()
            if measure(t, F_NOTE) > PX(RAIL_W) - 108:
                lines.append(line)
                line = wd
            else:
                line = t
        lines.append(line)
        for li, ln in enumerate(lines):
            c.text(ln, PX(LC) + 108, PY(ny) + li * 19, F_NOTE, NOTE_DIM)
        ny += 0.020 + 0.0176 * len(lines)

    # ---- right column --------------------------------------------------
    caption(c, RC, 0.038, "B  DEALING",
            "nested \u00b7 3 items \u00b7 Post up disabled with its real reason \u00b7 stat strip docked under")
    draw_list(c, RC, 0.055, "Hoodrich", "Dealing", "224 still to prep",
              DEALING_ITEMS, hovered=0, analogue=-0.22, strip=DEALING_STRIP)

    caption(c, RC, 0.470, "C  POST UP",
            "two deep \u00b7 7 items \u00b7 the load case \u00b7 long detail strings trimmed by Draw.Fit")
    draw_list(c, RC, 0.487, "Hoodrich / Dealing", "Post up", "Pick what you are moving",
              POSTUP_ITEMS, hovered=2, analogue=0.12, strip=POSTUP_STRIP)

    im.save(OUT, "PNG")
    print("wrote", OUT, os.path.getsize(OUT), "bytes", im.size)


if __name__ == "__main__":
    main()
