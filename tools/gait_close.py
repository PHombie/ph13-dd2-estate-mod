#!/usr/bin/env python3
"""The party at full size, standing and at points of the step cycle: to see hands, weapons and the turn of the body.

    python tools/gait_close.py <name> [key=value for corridor.gait ...]

Writes _lab/shots/<name>_a.png (standing | hold 0), <name>_b.png (hold 0.25 | hold 0.5), <name>_c.png (hold 0.75).
"""
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dev  # noqa: E402
from gait_gif import grab  # noqa: E402
from gait_sheet import run, value  # noqa: E402

from PIL import Image  # noqa: E402

CROP = (330, 380, 1480, 960)


def main(name, settings):
    if settings:
        run("corridor.gait", **settings)
    path = os.path.join(dev.SHOTS, "_close.png")
    tiles = []
    for hold in (-1, 0.0, 0.25, 0.5, 0.75):
        run("corridor.gait", hold=hold)
        time.sleep(1.0)
        im = grab(path)
        scale = im.width / 2560.0
        tiles.append(im.crop(tuple(int(v * scale) for v in CROP)).point(lambda v: int(255 * (v / 255.0) ** 0.6)))
    run("corridor.gait", hold=-1)
    w, h = tiles[0].size
    for suffix, group in zip("abc", (tiles[:2], tiles[2:4], tiles[4:])):
        sheet = Image.new("RGB", (w * len(group), h))
        for i, t in enumerate(group):
            sheet.paste(t, (w * i, 0))
        out = os.path.join(dev.SHOTS, "%s_%s.png" % (name, suffix))
        sheet.save(out)
        print(out)
    try:
        os.remove(path)
    except OSError:
        pass


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(2)
    main(sys.argv[1], dict((a.split("=", 1)[0], value(a.split("=", 1)[1])) for a in sys.argv[2:] if "=" in a))
