# -*- coding: utf-8 -*-
"""
The handset itself, as one picture: data/icons/phone_frame.png.

WHY A PICTURE. The phone's body was three rounded rectangles, the glass a fourth, the speaker
slit and the home bar two more, the camera three discs -- and a rounded rectangle is drawn as
a stack of thin ones, eight a corner. That came to about two hundred rectangles before a
single app tile went down, out of a per-frame budget of a few hundred that every script on
the machine shares. When another HUD spent its share first, the phone's last rectangles were
silently dropped: the battery, the signal, the plate under an app. The picture costs one
sprite from a budget nobody else is anywhere near.

HOLLOW, AND IT HAS TO BE. The game composites script sprites above script rectangles and text
whatever order they were submitted in, so the frame lands on top of everything on the phone.
The screen is cut out of it: the glass is drawn underneath as one flat rectangle, and the
frame's inner corners round it off. The camera punch-hole sits on the frame, in the status
bar, where nothing is drawn under it anyway.

Rendered at the handset's own proportions (PhoneMenu.BodyH * BodyRatio at 16:9) with a small
margin for the side keys, and drawn by PhoneMenu.Body at body plus that margin all round, so
every mark is exactly where the rectangles used to put it. Every figure below is a PhoneMenu
constant in screen-height units; PX turns them into pixels.

    python tools/make_phone.py
"""

import os

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(os.path.dirname(HERE), "data", "icons", "phone_frame.png")

# The body at 1080p: height = 0.760 * 1080, width = ToX(0.760 * 0.472) * 1920 -- the same
# number of pixels per screen-height unit on both axes, which is what square pixels mean.
BODY_H_UNITS = 0.760
BODY_W_UNITS = 0.760 * 0.472
PX = 1080.0                      # pixels per screen-height unit, at 1080p
OVER = 4                         # rendered this many times larger, then shrunk, for clean edges

MARGIN_UNITS = 0.0028            # PhoneMenu.FrameMargin: room for the side keys
BODY_ROUND = 0.007               # PhoneMenu.BodyRound
EDGE = 0.0022                    # the rim's width
CATCH = 0.0009                   # the light along the top of the rim
BEZEL = 0.009                    # PhoneMenu.Bezel
SCREEN_ROUND = 0.005             # PhoneMenu.ScreenRound
STATUS_H = 0.030                 # PhoneMenu.StatusH
CAM_RADIUS = 0.0068              # PhoneMenu.CamRadius
CAM_RING = 0.0012                # PhoneMenu.CamRing
KEY_OUT = 0.0024


def px(units):
    return units * PX * OVER


def main():
    w = int(round(px(BODY_W_UNITS + MARGIN_UNITS * 2)))
    h = int(round(px(BODY_H_UNITS + MARGIN_UNITS * 2)))
    m = px(MARGIN_UNITS)

    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    left, top = m, m
    right, bottom = w - m, h - m

    # THE KEYS ON THE SIDES, first, so the rim covers their inner edge. Power on the right,
    # two volume keys on the left, in the rim's own grey, a couple of pixels proud of it.
    key = (98, 104, 106, 255)
    bh = px(BODY_H_UNITS)
    d.rectangle([right - 1, top + bh * 0.20, right + px(KEY_OUT), top + bh * 0.20 + px(0.046)], fill=key)
    d.rectangle([left - px(KEY_OUT), top + bh * 0.17, left + 1, top + bh * 0.17 + px(0.026)], fill=key)
    d.rectangle([left - px(KEY_OUT), top + bh * 0.17 + px(0.032), left + 1, top + bh * 0.17 + px(0.032) + px(0.026)], fill=key)

    # THE RIM, WITH A LIGHT ON IT. The lighter shape shows only along the top edge, where the
    # darker one, set a hair lower, does not cover it.
    d.rounded_rectangle([left, top, right, bottom], radius=px(BODY_ROUND), fill=(134, 140, 142, 255))
    d.rounded_rectangle([left, top + px(CATCH), right, bottom], radius=px(BODY_ROUND), fill=(72, 76, 78, 255))

    # The body inside the rim.
    e = px(EDGE)
    d.rounded_rectangle([left + e, top + e, right - e, bottom - e], radius=px(BODY_ROUND - EDGE),
                        fill=(8, 9, 10, 252))

    # THE SPEAKER, in the top bezel, and THE HOME BAR in the bottom one.
    mid = (left + right) / 2.0
    slit_w, slit_h = px(0.052), px(0.0030)
    slit_y = top + px(BEZEL) / 2.0
    d.rounded_rectangle([mid - slit_w / 2, slit_y - slit_h / 2, mid + slit_w / 2, slit_y + slit_h / 2],
                        radius=slit_h / 2, fill=(30, 33, 35, 255))

    bar_w, bar_h = px(0.040), px(0.0024)
    bar_y = bottom - px(BEZEL) / 2.0 - px(0.0012) + bar_h / 2
    d.rounded_rectangle([mid - bar_w / 2, bar_y - bar_h / 2, mid + bar_w / 2, bar_y + bar_h / 2],
                        radius=bar_h / 2, fill=(88, 94, 96, 255))

    # THE SCREEN, CUT OUT. Everything the phone draws sits in this hole, under the frame.
    b = px(BEZEL)
    hole = Image.new("L", (w, h), 255)
    ImageDraw.Draw(hole).rounded_rectangle([left + b, top + b, right - b, bottom - b],
                                           radius=px(SCREEN_ROUND), fill=0)
    alpha = Image.eval(img.getchannel("A"), lambda a: a)
    from PIL import ImageChops
    img.putalpha(ImageChops.multiply(alpha, hole))
    d = ImageDraw.Draw(img)

    # THE CAMERA, a punch-hole in the glass at the middle of the status bar: a ring, a dark
    # lens, and a point of light on it.
    cam_x = mid
    cam_y = top + b + px(STATUS_H) / 2.0
    r = px(CAM_RADIUS)
    d.ellipse([cam_x - r, cam_y - r, cam_x + r, cam_y + r], fill=(72, 76, 78, 255))
    r2 = px(CAM_RADIUS - CAM_RING)
    d.ellipse([cam_x - r2, cam_y - r2, cam_x + r2, cam_y + r2], fill=(6, 7, 9, 255))
    r3 = px(CAM_RADIUS * 0.22)
    hx, hy = cam_x - px(CAM_RADIUS * 0.28), cam_y - px(CAM_RADIUS * 0.28)
    d.ellipse([hx - r3, hy - r3, hx + r3, hy + r3], fill=(120, 150, 170, 200))

    out = img.resize((w // OVER, h // OVER), Image.LANCZOS)
    out.save(OUT)
    print("wrote %s (%dx%d)" % (OUT, out.size[0], out.size[1]))


if __name__ == "__main__":
    main()
