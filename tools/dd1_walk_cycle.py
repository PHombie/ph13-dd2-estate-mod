#!/usr/bin/env python3
"""DD1's own walk: how long each hero's walk animation lasts and how its feet move, read from the player's install.
The numbers set the pace of the mod's walk for DD2's models (HeroGait): steps a second at DD1's walking speed.

    python tools/dd1_walk_cycle.py [--dd1 <install>]
"""
import argparse
import glob
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import preview_corridor as corridor  # noqa: E402

DD1 = r"E:\Steam\steamapps\common\DarkestDungeon"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dd1", default=DD1)
    args = ap.parse_args()
    for path in sorted(glob.glob(os.path.join(args.dd1, "heroes", "*", "anim", "*.sprite.walk.skel"))):
        hero = os.path.basename(os.path.dirname(os.path.dirname(path)))
        try:
            skel = corridor.read_skeleton(open(path, "rb").read())
        except Exception as e:  # a skeleton the reader does not know
            print("%-16s unreadable: %s" % (hero, e))
            continue
        for name, anim in skel["animations"].items():
            # the bones that travel furthest along x are the feet: how far, and how often they turn round
            reach = []
            for bone, frames in anim["translate"].items():
                xs = [f[1][0] if isinstance(f[1], (tuple, list)) else f[1] for f in frames]
                if len(xs) > 2:
                    reach.append((max(xs) - min(xs), skel["bones"][bone]["name"], len(frames)))
            reach.sort(reverse=True)
            print("%-16s %-8s %.3f s   furthest: %s" % (hero, name, anim["duration"], ", ".join("%s %.0f px" % (n, d) for d, n, _ in reach[:3])))


if __name__ == "__main__":
    main()
