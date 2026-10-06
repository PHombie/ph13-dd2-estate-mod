#!/usr/bin/env python3
"""Which of DD2's fight arenas the mod uses have DD2's stagecoach standing in them (its road-side arenas do: the
wagon behind the heroes), and that nothing of it is drawn in an Estate session.

    python -u tools/arena_coach_survey.py <folder> [seconds=560]      the game at its main menu or in the Estate's hamlet

Every combat_arena_* the mod's data and code name gets one fight from the hamlet (estate.battle, won at once):
scenes.state tells whether the arena's scene has a coach (a StageCoachSkinSpawnerBhv, or art named for it) and
estate.world whether any renderer of it is drawn; a photograph of each. The arenas that have one are then fought in
once more with the coach shown ([Scenes] Dd2Coach, by scenes.state coach=true): how it was. Written into <folder>:
<arena>.png, <arena>_coach_shown.png, arenas.json (a second run goes on where the first stopped).

Seen 2026-10-06 (game v2.04): 15 of 48, all through "stagecoach_art_arenas" with a spawner of type COMBAT_ARENA:
the *_faction, *_gaunt and *_pillager arenas, city_military, the two barricade arenas, kingdom_camp_ambush.
"""
import glob
import json
import os
import re
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "tools"))
import bridge  # noqa: E402
import play_dungeon as pd  # noqa: E402

CONFIG = "lost_battalion_mash_210"      # any fight DD2 can put up anywhere


def shot(folder, name):
    path = os.path.join(folder, name + ".png")
    if os.path.exists(path):
        os.remove(path)
    bridge.send({"cmd": "shot", "path": path})
    for _ in range(60):
        if os.path.exists(path) and os.path.getsize(path) > 0:
            break
        time.sleep(0.1)
    time.sleep(0.15)


def wait(test, seconds, step=0.2):
    deadline = time.time() + seconds
    while time.time() < deadline:
        try:
            if test():
                return True
        except RuntimeError:
            pass
        time.sleep(step)
    return False


def in_hub():
    return pd.mode() == "ESTATE" and pd.settled()


def fight(arena, settle):
    try:
        pd.run("estate.battle", config=CONFIG, arena=arena)
    except RuntimeError as e:
        return str(e)[:160]
    if not wait(lambda: pd.mode() == "COMBAT" and pd.settled(), 45):
        wait(in_hub, 30)
        return "no fight came up"
    time.sleep(settle)
    return None


def leave():
    pd.call({"cmd": "invoke", "expr": "$CombatBhv", "method": "ForceEndCombat", "args": [True]})
    ok = wait(in_hub, 60, 0.15)
    time.sleep(0.6)
    return ok


def main(folder, budget):
    os.makedirs(folder, exist_ok=True)
    names = set()
    for pattern in ("src/DD2Estate/Data/*.json", "src/DD2Estate/Dungeon/*.cs", "src/DD2Estate/Estate/*.cs", "src/DD2Estate/Dd2/*.cs"):
        for path in glob.glob(os.path.join(ROOT, pattern)):
            names.update(re.findall(r"combat_arena_[a-z0-9_]+", open(path, encoding="utf-8").read()))
    listing = os.path.join(folder, "arenas.json")
    report = json.load(open(listing, encoding="utf-8")) if os.path.exists(listing) else []
    done = set(e["arena"] for e in report if not e.get("error"))
    arenas = sorted(names - done)
    print(len(names), "arenas named;", len(done), "seen already;", len(arenas), "to go")

    if pd.mode() != "ESTATE":
        pd.no_key_at_loading()
        pd.run("estate.enter")
        wait(in_hub, 90)
        time.sleep(5)
    try:
        pd.run("tutorial.state", enabled=False)
    except RuntimeError:
        pass
    pd.run("scenes.state", coach=False)
    t0 = time.time()
    for arena in arenas:
        if time.time() - t0 > budget * 0.62:
            print("out of time before", arena)
            break
        entry = {"arena": arena}
        error = fight(arena, 2.5)
        if error:
            entry["error"] = error
            report.append(entry)
            print("%-48s %s" % (arena, error), flush=True)
            continue
        scenes = pd.run("scenes.state")
        world = pd.run("estate.world")
        entry["spawners"] = [s for s in scenes["spawners"] if s["scene"] == arena]
        entry["named"] = [n for n in scenes["named"] if n.startswith(arena + ":")]
        entry["coachDrawn"] = [c for c in world["coaches"] if c["drawn"] > 0]
        entry["coachRenderers"] = sum(c["renderers"] for c in world["coaches"])
        shot(folder, arena)
        report.append(entry)
        print("%-48s coach in the scene: %-5s put up: %s  renderers of it: %d, drawn: %d" % (
            arena, bool(entry["spawners"] or entry["named"]), [s["spawned"] for s in entry["spawners"]], entry["coachRenderers"], len(entry["coachDrawn"])), flush=True)
        if not leave():
            print("   the hub did not come back; mode", pd.mode())
            break
    json.dump(report, open(listing, "w", encoding="utf-8"), indent=1)
    with_coach = [e["arena"] for e in report if e.get("spawners") or e.get("named")]
    print("%d arenas surveyed in all; with a coach in the scene: %s" % (len([e for e in report if not e.get("error")]), with_coach))
    print("could not be fought in:", [(e["arena"], e["error"]) for e in report if e.get("error")])

    # how it was: the same arenas with the coach shown
    pd.run("scenes.state", coach=True)
    try:
        for arena in with_coach:
            if os.path.exists(os.path.join(folder, arena + "_coach_shown.png")):
                continue
            if time.time() - t0 > budget:
                print("out of time before the coach of", arena)
                break
            error = fight(arena, 4.0)
            if error:
                print(arena, error)
                continue
            shot(folder, arena + "_coach_shown")
            world = pd.run("estate.world")
            print("%-48s coach shown, drawn: %s" % (arena, [(c["path"].split(": ")[1], c["drawn"]) for c in world["coaches"]]), flush=True)
            if not leave():
                break
    finally:
        print("the coach kept out again:", pd.run("scenes.state", coach=False)["showCoach"] is False)
    return 0


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(2)
    sys.exit(main(os.path.abspath(sys.argv[1]), float(sys.argv[2]) if len(sys.argv) > 2 else 560))
