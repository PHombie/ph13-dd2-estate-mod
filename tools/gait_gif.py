#!/usr/bin/env python3
"""A GIF of the party walking in the dungeon view: the game is slowed down, frames are taken one by one and put
together at the pace of the game's own clock (no video tools needed).

    python tools/gait_gif.py <name> [dir=1] [seconds=2.2] [after=0.6] [fps=20] [width=900] [close=true] [sheet=true]
                             [key=value for corridor.gait ...]

The party stands for a moment, walks for <seconds> in direction dir (1 on, -1 back), stops; <after> seconds of the
stop are kept. The GIF lands in _lab/shots/<name>.gif; sheet=true also writes every third frame to <name>.png.
"""
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bridge  # noqa: E402
import dev  # noqa: E402
from gait_sheet import PARTY, FRONT, run, value  # noqa: E402

from PIL import Image  # noqa: E402

OWN = ("dir", "seconds", "after", "fps", "width", "close", "sheet", "lift", "colours", "tile")


def clock():
    return bridge.send({"cmd": "get", "expr": "UnityEngine.Time.time"})["result"]


def grab(path):
    if os.path.exists(path):
        os.remove(path)
    bridge.send({"cmd": "shot", "path": path})
    for _ in range(80):
        if os.path.exists(path) and os.path.getsize(path) > 0:
            break
        time.sleep(0.05)
    time.sleep(0.12)
    for _ in range(10):
        try:
            im = Image.open(path)
            im.load()
            return im
        except OSError:
            time.sleep(0.1)
    raise RuntimeError("no frame at " + path)


def main(name, settings):
    direction = settings.get("dir", 1)
    seconds = float(settings.get("seconds", 2.2))
    after = float(settings.get("after", 0.6))
    fps = float(settings.get("fps", 20))
    width = int(settings.get("width", 900))
    close = settings.get("close", False)
    lift = float(settings.get("lift", 1.0))      # below 1 lifts the dark for the eye (0.7); 1 is the game's own
    gait = dict((k, v) for k, v in settings.items() if k not in OWN)
    if gait:
        run("corridor.gait", **gait)
    crop = FRONT if close else PARTY
    if "tile" in settings:
        run("corridor.place", tile=int(settings["tile"]))
        time.sleep(0.6)
    height = int(width * (crop[3] - crop[1]) / (crop[2] - crop[0]))
    path = os.path.join(dev.SHOTS, "_gif_frame.png")

    # how long a frame takes to take, to slow the game by as much
    t = time.time()
    grab(path)
    grab(path)
    per = (time.time() - t) / 2
    slow = max(0.01, min(1.0, (1.0 / fps) / per))
    frames, stamps = [], []
    bridge.send({"cmd": "set", "expr": "UnityEngine.Time", "member": "timeScale", "value": slow})
    try:
        start = clock()
        walking = False
        while True:
            now = clock()
            if not walking and now - start >= 0.25:
                run("corridor.walk", dir=direction, seconds=seconds)
                walking = True
            if now - start >= 0.25 + seconds + after:
                break
            im = grab(path)
            scale = im.width / 2560.0
            tile = im.crop(tuple(int(v * scale) for v in crop)).resize((width, height), Image.LANCZOS)
            if lift != 1.0:
                tile = tile.point(lambda v: int(255 * (v / 255.0) ** lift))
            frames.append(tile.convert("RGB"))
            stamps.append(now)
    finally:
        bridge.send({"cmd": "set", "expr": "UnityEngine.Time", "member": "timeScale", "value": 1.0})
    stamps.append(clock())
    durations = [max(20, int(round((stamps[i + 1] - stamps[i]) * 1000 / 10.0)) * 10) for i in range(len(frames))]
    out = os.path.join(dev.SHOTS, name + ".gif")
    palette = [f.quantize(colors=int(settings.get("colours", 112)), method=Image.MEDIANCUT, dither=Image.NONE) for f in frames]
    palette[0].save(out, save_all=True, append_images=palette[1:], duration=durations, loop=0, optimize=False, disposal=1)
    print("%s: %d frames, %.2f s of game time, slowed to %.3f, %.1f MB" % (out, len(frames), stamps[-1] - stamps[0], slow, os.path.getsize(out) / 1e6))
    if settings.get("sheet", False):
        picked = frames[::3]
        columns = 6
        w, h = width // 2, height // 2
        sheet = Image.new("RGB", (w * columns, h * ((len(picked) + columns - 1) // columns)))
        for i, f in enumerate(picked):
            sheet.paste(f.resize((w, h)).point(lambda v: int(255 * (v / 255.0) ** 0.7)), (w * (i % columns), h * (i // columns)))
        sheet.save(os.path.join(dev.SHOTS, name + ".png"))
        print(os.path.join(dev.SHOTS, name + ".png"))
    try:
        os.remove(path)
    except OSError:
        pass


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(2)
    main(sys.argv[1], dict((a.split("=", 1)[0], value(a.split("=", 1)[1])) for a in sys.argv[2:] if "=" in a))
