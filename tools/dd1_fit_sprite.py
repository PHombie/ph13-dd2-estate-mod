#!/usr/bin/env python3
"""Finds where, how large and how turned DD1 draws a picture of one of its sprite sheets, in a real DD1 frame.
DD1's animated UI (the estate bar's buttons, for one) is Spine: the sheet gives the pictures, the skeleton their
place, size and angle, and the skeleton is a binary file. A frame of the real game says the same.

    python tools/dd1_fit_sprite.py <frame.png> <sheet.png> <region> <x0> <y0> <x1> <y1> [scale_lo scale_hi angle_lo angle_hi]

The frame must be 1920x1080 (DD1's layout space). (x0,y0)-(x1,y1) is the part of the frame to look in. Prints the
best scale, angle (degrees, counter-clockwise) and the picture's middle in the frame, with the match's score.
"""
import os
import sys

import numpy as np
from PIL import Image


def region(sheet, name):
    """One picture out of a Spine sheet, upright (see Dd1Install.Region in the mod)."""
    atlas = open(sheet[:-4] + ".atlas", encoding="utf-8").read().replace("\r", "").split("\n")
    for i, line in enumerate(atlas):
        if line != name:
            continue
        info = {}
        for more in atlas[i + 1:]:
            if not more.startswith(" "):
                break
            key, _, value = more.strip().partition(":")
            info[key] = [v.strip() for v in value.split(",")]
        x, y = int(info["xy"][0]), int(info["xy"][1])
        w, h = int(info["size"][0]), int(info["size"][1])
        page = Image.open(sheet).convert("RGBA")
        if info["rotate"][0] == "true":
            return page.crop((x, y, x + h, y + w)).rotate(-90, expand=True)
        return page.crop((x, y, x + w, y + h))
    raise SystemExit("no picture named %s in %s" % (name, sheet))


def fit(frame, art, box, scales, angles):
    x0, y0, x1, y1 = box
    target = np.asarray(frame.crop(box).convert("RGB")).astype(np.float32)
    best = None
    for scale in scales:
        for angle in angles:
            w, h = max(2, int(round(art.width * scale))), max(2, int(round(art.height * scale)))
            turned = art.resize((w, h), Image.LANCZOS).rotate(angle, expand=True, resample=Image.BICUBIC)
            a = np.asarray(turned).astype(np.float32)
            rgb, alpha = a[..., :3], a[..., 3] / 255.0
            th, tw = alpha.shape
            if th >= target.shape[0] or tw >= target.shape[1]:
                continue
            weight = alpha.sum() * 3 + 1e-6
            # a coarse walk, then a fine one around the best place
            for step, around in ((4, None), (1, 5)):
                if around is None:
                    ys = range(0, target.shape[0] - th, step)
                    xs = range(0, target.shape[1] - tw, step)
                else:
                    if place is None:
                        break
                    ys = range(max(0, place[0] - around), min(target.shape[0] - th, place[0] + around + 1))
                    xs = range(max(0, place[1] - around), min(target.shape[1] - tw, place[1] + around + 1))
                place, score = None, None
                for y in ys:
                    for x in xs:
                        window = target[y:y + th, x:x + tw]
                        error = (np.abs(window - rgb) * alpha[..., None]).sum() / weight
                        if score is None or error < score:
                            score, place = error, (y, x)
            if place is not None and (best is None or score < best[0]):
                best = (score, scale, angle, x0 + place[1] + tw / 2.0, y0 + place[0] + th / 2.0, tw, th)
    return best


def main(argv):
    frame = Image.open(argv[1]).convert("RGB")
    if frame.size != (1920, 1080):
        frame = frame.resize((1920, 1080), Image.LANCZOS)
    art = region(argv[2], argv[3])
    box = tuple(int(v) for v in argv[4:8])
    lo, hi, alo, ahi = (float(v) for v in argv[8:12]) if len(argv) >= 12 else (0.5, 1.0, -30.0, 30.0)
    coarse = fit(frame, art, box, np.arange(lo, hi + 1e-6, 0.05), np.arange(alo, ahi + 1e-6, 5.0))
    _, s, a, *_ = coarse
    fine = fit(frame, art, box, np.arange(s - 0.04, s + 0.041, 0.01), np.arange(a - 4.0, a + 4.01, 1.0))
    score, scale, angle, cx, cy, w, h = fine
    print("%s: scale %.2f angle %+.0f middle (%.1f, %.1f) drawn %dx%d of %dx%d, mean error %.1f of 255" % (
        argv[3], scale, angle, cx, cy, w, h, art.width, art.height, score))
    return fine


if __name__ == "__main__":
    if len(sys.argv) < 8:
        print(__doc__)
        sys.exit(2)
    main(sys.argv)
