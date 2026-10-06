#!/usr/bin/env python3
"""The heroes' weapons in the dungeon view: is every weapon mesh that the model shows also seen, and does it stay
in the hand while the hero walks and changes rank.

    python -u tools/weapons_check.py              the party as it stands: every weapon mesh, shown or not, and why
    python -u tools/weapons_check.py --classes    every hero class, four at a time, on a bare hallway (the view only;
                                                  the expedition is not touched): standing in the middle and at
                                                  the hallway's start (the view's edge), at four points of the
                                                  step, after two changes of rank; and what the walk parts from
                                                  the body (corridor.hold)
    python -u tools/weapons_check.py --cameras    the cameras stacked on the main one: where each looks for what it draws

The dungeon view must be up. Exit code 1 when a weapon that the model shows is not seen, when a mesh is parted from
the body by the walk, or when a camera looks at less than the whole view. Heroes made for the look are taken out
of the roster again; nothing is saved.
"""
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bridge  # noqa: E402

CLASSES = ["abomination", "bounty_hunter", "crusader", "duelist", "flagellant", "grave_robber", "hellion", "highwayman",
           "jester", "leper", "man_at_arms", "occultist", "plague_doctor", "runaway", "vestal"]
HALL = ["corridor_door.basic", "corridor_wall.01", "corridor_wall.02", "corridor_wall.03", "corridor_wall.04", "corridor_wall.05",
        "corridor_wall.06", "corridor_door.basic"]
# what of the model may leave the pelvis by so little (model units, degrees): rounding
SHIFT, TURN = 0.002, 0.2


def run(cmd, **args):
    r = bridge.send(dict({"cmd": "run", "name": cmd}, **args))
    if not r.get("ok"):
        raise RuntimeError(r.get("error"))
    return r.get("result")


def short(hero, part):
    return part.replace("msh_", "").replace(hero["cls"] + "_", "")


def weapons(tag):
    """One line a hero; returns the number of weapons that show in the model and are not seen."""
    report = run("corridor.weapons")
    if not isinstance(report, dict):
        print(tag, report)
        return 1
    for hero in report["heroes"]:
        on = [q for q in hero["parts"] if not q["state"].startswith("off")]
        bad = [q for q in on if q["state"] != "shown"]
        print("%-22s %-14s %-58s %s" % (tag, hero["cls"], ", ".join(short(hero, q["part"]) for q in on),
                                       "ok" if not bad else "MISSING: " + "; ".join("%s (%s)" % (short(hero, q["part"]), q["state"]) for q in bad)), flush=True)
    return report["missing"]


def wait_models(count, seconds=25):
    deadline = time.time() + seconds
    while time.time() < deadline:
        heroes = (run("corridor.gait") or {}).get("heroes") or []
        if len(heroes) == count and all(h["ready"] and h["measured"] for h in heroes):
            return True
        time.sleep(0.4)
    return False


def cameras():
    state = run("corridor.cameras")
    if not isinstance(state, dict):
        print(state)
        return 1
    bad = 0
    print("main %s rect %s aspect %.3f, stack rects %s" % (state["main"]["name"], state["main"]["rect"], state["main"]["aspect"], "kept" if state["stackRects"] else "NOT kept"))
    for camera in state["stack"]:
        short_of = camera["on"] and camera["looksAt"] < 0.999
        bad += 1 if short_of else 0
        print("   %-28s %s rect %s aspect %.3f looks at %.0f%% of the view's width%s" % (
            camera["name"], "on " if camera["on"] else "off", camera["rect"], camera["aspect"], camera["looksAt"] * 100, "  <-- culls the sides" if short_of else ""))
    return bad


def classes(names):
    bad = 0
    run("corridor.area", dungeon="crypts", tiles=HALL, start=3)
    bridge.send({"cmd": "set", "expr": "DD2Estate.Dungeon.CorridorScene", "member": "ForegroundMode", "value": 0})
    before = len(run("roster.state")["heroes"])
    try:
        for at in range(0, len(names), 4):
            group = names[at:at + 4]
            cast = run("corridor.cast", classes=group)
            if not isinstance(cast, list):
                print("cast:", cast)
                bad += 1
                continue
            if not wait_models(len(group)):
                print("the models of %s did not come up" % ", ".join(group))
                bad += 1
                continue
            time.sleep(1.2)
            run("corridor.place", tile=3)
            time.sleep(0.5)
            bad += weapons("standing")
            run("corridor.place", x=-10000)
            time.sleep(0.8)
            bad += weapons("at the hallway's start")
            run("corridor.place", tile=3)
            for hold in (0.0, 0.25, 0.5, 0.75):
                run("corridor.gait", hold=hold)
                time.sleep(0.45)
                bad += weapons("step %.2f" % hold)
            run("corridor.gait", hold=-1)
            for _ in range(2):
                run("corridor.cast", reverse=True)
                time.sleep(4.0)
            bad += weapons("after two changes")
            for one in run("corridor.hold", points=16):
                # (an effect's own planes and rig hang beside the pelvis too, switched off, and are left where they are: vfx_*)
                parted = [b for b in one["beside"] if b["moves"] and not b["bone"].lower().startswith("vfx") and (b["shift"] > SHIFT or b["turn"] > TURN)]
                carried = [b["bone"] for b in one["beside"] if b["carried"]]
                bad += len(parted)
                print("%-22s %-14s carried with the pelvis: %s%s" % ("the walk", one["cls"], (", ".join(carried[:3]) + (" ... (%d)" % len(carried) if len(carried) > 3 else "")) if carried else "-",
                                                                   "" if not parted else "  PARTED: " + "; ".join("%s by %.3f / %.1f deg (%s)" % (b["bone"], b["shift"], b["turn"], ",".join(b["moves"])) for b in parted)), flush=True)
        guard = run("corridor.weapons", guard="state")
        for note in guard.get("notes") or []:
            print("guard:", note)
    finally:
        run("corridor.gait", hold=-1)
        print(run("corridor.cast", clear=True))
        bridge.send({"cmd": "set", "expr": "DD2Estate.Dungeon.CorridorScene", "member": "ForegroundMode", "value": 1})
    after = len(run("roster.state")["heroes"])
    print("roster %d before, %d after" % (before, after))
    return bad + (1 if before != after else 0)


def main():
    state = run("dungeon.state")
    if state.get("view") != "Dungeon":
        print("the dungeon view is not up")
        return 2
    bad = 0
    names = [a for a in sys.argv[1:] if not a.startswith("--")]
    if "--cameras" in sys.argv or len(sys.argv) == 1:
        bad += cameras()
    if "--classes" in sys.argv or names:
        bad += classes(names or CLASSES)
    else:
        bad += weapons("the party")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
