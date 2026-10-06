#!/usr/bin/env python3
"""A contact sheet of the heroes' walk: the step cycle held at N points, the heroes cropped out of each frame.

    python tools/gait_sheet.py <name> [frames] [key=value ...]     (the dungeon view must be up)

key=value pairs go to corridor.gait before the frames are taken (stride=1.4 turn=0.5 pose=inn_idle ...);
close=true crops the two heroes in front at full size instead of the whole party.
The sheet lands in _lab/shots/<name>.png.
"""
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bridge  # noqa: E402
import dev  # noqa: E402

from PIL import Image  # noqa: E402

# where the party stands in a 2560x1440 frame, and the two heroes in front
PARTY = (300, 330, 1500, 980)
FRONT = (800, 480, 1400, 960)


def run(cmd, **args):
    r = bridge.send(dict({"cmd": "run", "name": cmd}, **args))
    if not r.get("ok"):
        raise RuntimeError(r.get("error"))
    return r.get("result")


def value(text):
    for cast in (int, float):
        try:
            return cast(text)
        except ValueError:
            pass
    return {"true": True, "false": False}.get(text.lower(), text)


def main(name, frames, settings):
    close = settings.pop("close", False)
    columns = settings.pop("columns", 4 if close else 2)
    if settings:
        run("corridor.gait", **settings)
    crop = FRONT if close else PARTY
    size = (crop[2] - crop[0], crop[3] - crop[1]) if close else (600, 325)
    tiles = []
    for i in range(frames):
        run("corridor.gait", hold=i / frames)
        time.sleep(0.5)
        dev.shot("_gait_%d" % i)
        im = Image.open(os.path.join(dev.SHOTS, "_gait_%d.png" % i))
        scale = im.width / 2560.0
        tile = im.crop(tuple(int(v * scale) for v in crop)).resize(size)
        # lifted for the eye: the corridor is dark by design
        tiles.append(tile.point(lambda v: int(255 * (v / 255.0) ** 0.6)))
    run("corridor.gait", hold=-1)
    rows = (frames + columns - 1) // columns
    sheet = Image.new("RGB", (size[0] * columns, size[1] * rows))
    for i, tile in enumerate(tiles):
        sheet.paste(tile, (size[0] * (i % columns), size[1] * (i // columns)))
    path = os.path.join(dev.SHOTS, name + ".png")
    sheet.save(path)
    for i in range(frames):
        for suffix in ("", "_small"):
            try:
                os.remove(os.path.join(dev.SHOTS, "_gait_%d%s.png" % (i, suffix)))
            except OSError:
                pass
    print(path)


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(2)
    count = int(sys.argv[2]) if len(sys.argv) > 2 and sys.argv[2].isdigit() else 8
    pairs = dict((a.split("=", 1)[0], value(a.split("=", 1)[1])) for a in sys.argv[2:] if "=" in a)
    main(sys.argv[1], count, pairs)
