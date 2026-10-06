#!/usr/bin/env python3
"""pair_diff.py <pair name> [art folder or file ...]: where DD1's own pictures stand in DD1's frame and in the mod's.

A pair is `_lab/mod_now/pairs/<name>.png` (tools/mod_frames.py: DD1's frame on top, the mod's screen in the same
state underneath, both 1920x1080). Every picture given (a file, or every .png of a folder of the DD1 install, path
from the install's root) is looked for in both halves by matching its opaque pixels; where it is found in both, the
two places are printed, and the difference when they are not the same. A picture found in DD1's half only is one
the mod does not draw (or draws changed); one found in neither is not on this screen.

    python tools/pair_diff.py town_tavern campaign/town/buildings/tavern shared/name
    python tools/pair_diff.py estate_bar_2_activity_log activity_log campaign/town/activity_log

This measures; it does not judge: a picture the mod draws tinted, scaled or half covered matches badly and is
reported as "not found". The score is how well the picture's light and dark agree with the frame's there
(masked correlation; 1 = pixel for pixel).
"""
import glob
import os
import sys

import cv2
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PAIRS = os.path.join(ROOT, "_lab", "mod_now", "pairs")
DD1 = r"E:\Steam\steamapps\common\DarkestDungeon"
FOUND = 0.90            # a match this good is the picture
SMALLEST = 24           # pictures smaller than this (either side) match anywhere
LARGEST = 1500


def pictures(args):
    out = []
    for a in args:
        path = a if os.path.isabs(a) else os.path.join(DD1, a)
        if os.path.isdir(path):
            out += sorted(glob.glob(os.path.join(path, "*.png")))
        elif os.path.isfile(path):
            out.append(path)
        else:
            out += sorted(glob.glob(path))
    return out


def find(frame, art):
    """Best place of the picture in the frame and how well it fits there: (x, y, score). The place is where the
    picture's own light and dark agree best with the frame's (a dark picture fits any dark corner by plain
    difference: correlation does not fall for that); the score is that agreement, 1 = pixel for pixel."""
    bgr = art[:, :, :3]
    mask = (art[:, :, 3] > 200).astype(np.uint8)
    if mask.sum() < SMALLEST * SMALLEST * 0.5:
        return None
    grey = cv2.cvtColor(bgr, cv2.COLOR_BGR2GRAY)
    if grey[mask > 0].std() < 6.0:
        return None                       # a flat picture: nothing to hold on to
    result = cv2.matchTemplate(frame, bgr, cv2.TM_CCOEFF_NORMED, mask=mask)
    result[~np.isfinite(result)] = -1.0
    _, score, _, at = cv2.minMaxLoc(result)
    return at[0], at[1], float(score)


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 1
    pair = cv2.imread(os.path.join(PAIRS, argv[0] + ".png"), cv2.IMREAD_COLOR)
    if pair is None:
        print("no such pair:", argv[0])
        return 1
    dd1, mod = pair[:1080], pair[1080:2160]
    same = moved = missing = 0
    for path in pictures(argv[1:]):
        art = cv2.imread(path, cv2.IMREAD_UNCHANGED)
        if art is None or art.ndim != 3:
            continue
        if art.shape[2] == 3:
            art = np.dstack([art, np.full(art.shape[:2], 255, np.uint8)])
        h, w = art.shape[:2]
        if w < SMALLEST or h < SMALLEST or w > LARGEST or h > 1080:
            continue
        a = find(dd1, art)
        if a is None or a[2] < FOUND:
            continue                      # not on DD1's screen in this state
        b = find(mod, art)
        name = os.path.relpath(path, DD1).replace("\\", "/")
        if b is None or b[2] < FOUND:
            missing += 1
            print("ONLY IN DD1  %-64s at %4d,%4d (%dx%d, fit %.2f; the mod's best %.2f)" % (name, a[0], a[1], w, h, a[2], b[2] if b else 0))
        elif (a[0], a[1]) != (b[0], b[1]):
            moved += 1
            print("MOVED        %-64s DD1 %4d,%4d  mod %4d,%4d  (%+d,%+d)" % (name, a[0], a[1], b[0], b[1], b[0] - a[0], b[1] - a[1]))
        else:
            same += 1
            print("same         %-64s at %4d,%4d" % (name, a[0], a[1]))
    print("%d in place, %d moved, %d only in DD1's frame" % (same, moved, missing))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
