#!/usr/bin/env python3
"""The heroes' walk, class by class: each class's model is put into the dungeon view alone (corridor.cast), its
walk is measured over the cycle (corridor.gaitcheck: ankles on their marks, knees bending the way of the step, toes
pointing that way, feet on the floor) and photographed standing and at eight points of the cycle.

    python -u tools/gait_bench.py [--numbers] [class ...]        (the dungeon view must be up; default: every class)

--numbers: the table only, no sheets. In the table "facing a>b" is the pelvis's turn to the camera standing and
walking, "knees" the knee's angle at its most bent (180: straight) standing, on the ground and in the air.

Sheets land in _lab/shots/bench/<class>.png; the table is printed and written to _lab/shots/bench/report.txt.
Heroes made for the look are taken out of the roster again at the end; nothing is saved.
"""
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bridge  # noqa: E402
import dev  # noqa: E402
from gait_gif import grab  # noqa: E402
from gait_sheet import run  # noqa: E402

from PIL import Image  # noqa: E402

CLASSES = ["abomination", "bounty_hunter", "crusader", "duelist", "flagellant", "grave_robber", "hellion", "highwayman",
           "jester", "leper", "man_at_arms", "occultist", "plague_doctor", "runaway", "vestal"]
# where the leader stands in a 2560x1440 frame
CROP = (800, 300, 1560, 990)
TILE = (456, 414)
OUT = os.path.join(dev.SHOTS, "bench")
NUMBERS_ONLY = False


def wait_ready(seconds=25):
    deadline = time.time() + seconds
    state = None
    while time.time() < deadline:
        state = run("corridor.gait")["heroes"]
        if state and state[0]["ready"] and state[0]["measured"]:
            return True
        time.sleep(0.5)
    return False


def one(cls):
    cast = run("corridor.cast", classes=[cls])
    if not isinstance(cast, list):
        return {"class": cls, "faults": [str(cast)]}, None
    run("corridor.gait", hold=-1)
    if not wait_ready():
        return {"class": cls, "name": cast[0]["name"], "faults": ["the model did not come up, or was never seen standing"]}, None
    time.sleep(1.0)
    report = run("corridor.gaitcheck", points=24)[0]
    report["class"] = cls
    report["name"] = cast[0]["name"]
    if NUMBERS_ONLY:
        return report, None
    path = os.path.join(OUT, "_frame.png")
    tiles = []
    for hold in (-1, 0.0, 0.125, 0.25, 0.375, 0.5, 0.625, 0.75, 0.875):
        run("corridor.gait", hold=hold)
        time.sleep(0.7)
        im = grab(path)
        scale = im.width / 2560.0
        tile = im.crop(tuple(int(v * scale) for v in CROP)).resize(TILE, Image.LANCZOS)
        tiles.append(tile.point(lambda v: int(255 * (v / 255.0) ** 0.6)))
    run("corridor.gait", hold=-1)
    sheet = Image.new("RGB", (TILE[0] * 3, TILE[1] * 3))
    for i, t in enumerate(tiles):
        sheet.paste(t, (TILE[0] * (i % 3), TILE[1] * (i // 3)))
    out = os.path.join(OUT, cls + ".png")
    sheet.save(out)
    return report, out


def line(r):
    if "scale" not in r:
        return "%-14s %-10s  FAULT: %s" % (r["class"], r.get("name") or "", "; ".join(r["faults"]))
    return ("%-14s %-10s facing %5.1f>%4.1f  knees idle %3.0f stance %3.0f swing %3.0f  rise %.3f drop %.3f  ankle %.3f  slope %4.1f  miss %.3f  "
            "knee %+.3f%s  toe %4.1f  ball %+.3f  stretch %.3f  %s") % (
        r["class"], r["name"] or "", r["poseFacing"], r["walkFacing"], r["idleKnee"], r["stanceKnee"], r["swingKnee"], r["maxRise"], r["maxDrop"],
        r["ankleHeight"], r["footSlope"], r["maxMiss"], r["minKneeAhead"], "*" if r.get("frontGuessed") else " ", r["maxToeOff"], r["minBall"],
        r["maxStretch"], "ok" if not r["faults"] else "FAULT: " + "; ".join(r["faults"]))


def main(classes):
    os.makedirs(OUT, exist_ok=True)
    state = run("dungeon.state")
    if state.get("view") != "Dungeon":
        print("the dungeon view is not up")
        return 2
    bridge.send({"cmd": "set", "expr": "DD2Estate.Dungeon.CorridorScene", "member": "ForegroundMode", "value": 0})
    run("corridor.torch", level=100)
    before = len(run("roster.state")["heroes"])
    lines = []
    bad = 0
    try:
        for cls in classes:
            report, sheet = one(cls)
            text = line(report)
            lines.append(text)
            bad += 1 if report["faults"] else 0
            print(text, flush=True)
    finally:
        run("corridor.gait", hold=-1)
        print(run("corridor.cast", clear=True))
        bridge.send({"cmd": "set", "expr": "DD2Estate.Dungeon.CorridorScene", "member": "ForegroundMode", "value": 1})
        run("corridor.torch", level=-1)
        try:
            os.remove(os.path.join(OUT, "_frame.png"))
        except OSError:
            pass
    after = len(run("roster.state")["heroes"])
    lines.append("roster %d before, %d after" % (before, after))
    print(lines[-1])
    with open(os.path.join(OUT, "report.txt"), "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    return 1 if bad or before != after else 0


if __name__ == "__main__":
    names = [a for a in sys.argv[1:] if not a.startswith("--")]
    NUMBERS_ONLY = "--numbers" in sys.argv
    sys.exit(main(names or CLASSES))
