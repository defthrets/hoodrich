# -*- coding: utf-8 -*-
#
# The mod's turf, drawn on the map, so it can be held up against a real one.
#
# zones_dump.json carries each zone's REAL bounds out of the game -- often several boxes per
# zone, because a neighbourhood is not a rectangle. Drawing those rather than the radius in
# zones.json is the difference between "roughly over there" and something comparable to a map.

import io
import json
import os

from PIL import Image, ImageDraw, ImageFont

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(REPO, 'tools')

zones = json.loads(io.open(os.path.join(REPO, 'tools', 'zones_dump.json'), encoding='utf-8').read())
gangs = json.loads(io.open(os.path.join(REPO, 'data', 'gangs.json'), encoding='utf-8').read())
gangs = gangs['gangs'] if isinstance(gangs, dict) and 'gangs' in gangs else gangs

owner = {}
for g in gangs:
    for code in g.get('turf', []):
        owner[code.upper()] = g['id']

COLOUR = {
    'families':  (60, 210, 75, 210),
    'ballas':    (170, 60, 220, 210),
    'vagos':     (240, 225, 55, 210),
    'marabunta': (60, 150, 235, 210),
    'aztecas':   (70, 220, 220, 210),
    'lost':      (30, 30, 30, 210),
    'triads':    (235, 120, 40, 210),
    'armenians': (150, 40, 40, 210),
    'koreans':   (110, 90, 230, 210),
}

# Los Santos and its edges -- the area the reference map actually colours.
X0, X1 = -3400, 1600
Y0, Y1 = -3600, 1400
W = 1080
H = int(W * (Y1 - Y0) / float(X1 - X0))


def px(x, y):
    return ((x - X0) / float(X1 - X0) * W,
            H - (y - Y0) / float(Y1 - Y0) * H)      # north up


img = Image.new('RGBA', (W, H), (226, 232, 226, 255))
d = ImageDraw.Draw(img, 'RGBA')

try:
    F = ImageFont.truetype('arialbd.ttf', 13)
    FS = ImageFont.truetype('arial.ttf', 10)
except Exception:
    F = FS = ImageFont.load_default()

held = []

for z in zones:
    code = z['Name'].upper()
    who = owner.get(code)

    for b in z.get('Bounds', []):
        mn, mx = b['Minimum'], b['Maximum']
        x0, y0 = px(mn['X'], mn['Y'])
        x1, y1 = px(mx['X'], mx['Y'])
        box = [min(x0, x1), min(y0, y1), max(x0, x1), max(y0, y1)]

        if box[2] < 0 or box[0] > W or box[3] < 0 or box[1] > H:
            continue

        if who:
            d.rectangle(box, fill=COLOUR[who], outline=(255, 255, 255, 180))
        else:
            d.rectangle(box, fill=(255, 255, 255, 40), outline=(180, 186, 180, 120))

    if who:
        c = z['Bounds'][0]
        cx, cy = px((c['Minimum']['X'] + c['Maximum']['X']) / 2.0,
                    (c['Minimum']['Y'] + c['Maximum']['Y']) / 2.0)
        held.append((who, code, cx, cy))

for who, code, cx, cy in held:
    d.text((cx, cy), code, fill=(0, 0, 0, 255), font=F, anchor='mm',
           stroke_width=3, stroke_fill=(255, 255, 255, 220))

# Legend
y = 10
for g in gangs:
    if not g.get('turf'):
        continue
    d.rectangle([10, y, 26, y + 14], fill=COLOUR[g['id']], outline=(0, 0, 0, 200))
    d.text((32, y + 1), '%s  --  %s' % (g['name'], ', '.join(g['turf'])),
           fill=(10, 10, 10, 255), font=FS)
    y += 19

p = os.path.join(OUT, 'turf_now.png')
img.convert('RGB').save(p)
print('wrote %s  (%dx%d)' % (p, W, H))
print()
for g in gangs:
    print('  %-11s %s' % (g['id'], ', '.join(g.get('turf', [])) or '-'))
