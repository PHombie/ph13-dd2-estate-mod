#!/usr/bin/env python3
"""How bent a class's knees get over the walk's cycle, for a set of settings: the classes are put into the view,
the walk is posed at 24 points (corridor.gaitcheck) and the knees' angles printed (180: a straight leg).

    python tools/gait_knees.py <class>[,<class>...] [straighten=0.65] [stepScale=0.8] [swing=0.7] [facing=10] [detail=true]

The settings given are set for every class named (and stay set); detail=true prints the angles point by point.
"""
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from gait_sheet import run, value  # noqa: E402


def main(classes, settings):
    detail = settings.pop("detail", False)
    for cls in classes:
        if settings:
            run("corridor.gait", cls=cls, **settings)
    cast = run("corridor.cast", classes=classes)
    if not isinstance(cast, list):
        print(cast)
        return 1
    deadline = time.time() + 30
    while time.time() < deadline:
        heroes = run("corridor.gait", hold=-1)["heroes"]
        if heroes and all(h["ready"] and h["measured"] for h in heroes):
            break
        time.sleep(0.5)
    time.sleep(1.0)
    for r in run("corridor.gaitcheck", points=24, detail=True):
        print("%-13s facing %5.1f>%4.1f  knees idle %3.0f stance %3.0f swing %3.0f  rise %.3f drop %.3f  %s" % (
            r["cls"], r["poseFacing"], r["walkFacing"], r["idleKnee"], r["stanceKnee"], r["swingKnee"], r["maxRise"], r["maxDrop"],
            "ok" if not r["faults"] else "FAULT: " + "; ".join(r["faults"])))
        if detail:
            for n, (left, right, rise) in enumerate(r["points"]):
                print("    %.3f  L %5.1f  R %5.1f  pelvis %+.3f" % (n / 24.0, left, right, rise))
    print(run("corridor.cast", clear=True))
    return 0


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(2)
    sys.exit(main(sys.argv[1].split(","), dict((a.split("=", 1)[0], value(a.split("=", 1)[1])) for a in sys.argv[2:] if "=" in a)))
