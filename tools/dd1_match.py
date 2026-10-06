#!/usr/bin/env python3
"""Finds DD1's own corridor art in a screenshot of DD1: for each picture given, the scale and the place at which
its upright part (the upper 600 rows of a wall tile) matches the screenshot best. This is how the mod's corridor
is checked against the real game: where the tiles stand, how large, and what stands beyond the door tiles.

    python tools/dd1_match.py <screenshot.png> <dungeon> <tile> [<tile> ...] [--mirror <tile>]

<tile>: corridor_door.basic, corridor_wall.03, endhall.01 ... (dungeons/<dungeon>/<dungeon>.<tile>.png).
Prints for each: the tile's width on screen (pixels of the screenshot), its left edge and top, and the score.
"""
import os
import sys

import cv2
import numpy as np

DD1 = r"E:\Steam\steamapps\common\DarkestDungeon"


def edges(grey):
    """Edge strength: the light differs between art and screen, the drawn lines do not."""
    gx = cv2.Sobel(grey, cv2.CV_32F, 1, 0, ksize=3)
    gy = cv2.Sobel(grey, cv2.CV_32F, 0, 1, ksize=3)
    return cv2.magnitude(gx, gy)


def find(screen, art, mirror=False, widths=None):
    rgba = cv2.imread(art, cv2.IMREAD_UNCHANGED)
    if rgba is None:
        return None
    if rgba.shape[2] == 3:
        rgba = np.dstack([rgba, np.full(rgba.shape[:2], 255, np.uint8)])
    if mirror:
        rgba = rgba[:, ::-1]
    upright = rgba[:600]
    grey = cv2.cvtColor(upright[..., :3], cv2.COLOR_BGR2GRAY).astype(np.float32) * (upright[..., 3].astype(np.float32) / 255.0)
    mask = (upright[..., 3] > 128).astype(np.float32)
    best = None
    # the screenshot is searched at a third of its size, then the best few are looked at again at full size
    small = cv2.resize(screen, None, fx=1 / 3.0, fy=1 / 3.0, interpolation=cv2.INTER_AREA)
    small_edges = edges(small)
    for width in widths or range(560, 1201, 8):
        scale = width / 720.0 / 3.0
        t = cv2.resize(grey, None, fx=scale, fy=scale, interpolation=cv2.INTER_AREA)
        m = cv2.resize(mask, None, fx=scale, fy=scale, interpolation=cv2.INTER_AREA)
        if t.shape[0] >= small.shape[0] or t.shape[1] < 16:
            continue
        te = edges(t) * (m > 0.9)
        # the tile may hang over either side of the screen: the screen is padded
        pad = t.shape[1] * 2 // 3
        padded = cv2.copyMakeBorder(small_edges, 0, 0, pad, pad, cv2.BORDER_CONSTANT, value=0)
        result = cv2.matchTemplate(padded, te, cv2.TM_CCORR_NORMED)
        _, score, _, at = cv2.minMaxLoc(result)
        if best is None or score > best[0]:
            best = (score, width, (at[0] - pad) * 3, at[1] * 3)
    return best


def main(argv):
    shot, dungeon = argv[0], argv[1]
    screen = cv2.imread(shot, cv2.IMREAD_GRAYSCALE).astype(np.float32)
    # the dungeon view only: the panel below would only add noise
    screen = screen[: int(screen.shape[0] * 2 / 3)]
    mirror = False
    for tile in argv[2:]:
        if tile == "--mirror":
            mirror = True
            continue
        art = os.path.join(DD1, "dungeons", dungeon, "%s.%s.png" % (dungeon, tile))
        found = find(screen, art, mirror)
        if found is None:
            print("%-28s no such art" % tile)
        else:
            score, width, left, top = found
            print("%-28s%s width %4d px  left %5d  right %5d  top %4d  score %.3f" % (tile, " (mirrored)" if mirror else "", width, left, left + width, top, score))
        mirror = False


if __name__ == "__main__":
    if len(sys.argv) < 4:
        print(__doc__)
        sys.exit(2)
    main(sys.argv[1:])
