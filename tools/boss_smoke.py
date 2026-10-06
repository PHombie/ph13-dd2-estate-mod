#!/usr/bin/env python3
"""Smoke test of the story bosses: start each boss fight from the Estate hub, screenshot it, force a win and
check that control comes back to the hub.

    python tools/boss_smoke.py [first [count]]      (Estate entered, hamlet shown, a party selected)
"""
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bridge  # noqa: E402
import dev  # noqa: E402

BOSSES = [
    ("crypts", "librarian", "apprentice"), ("crypts", "exemplar", "apprentice"),
    ("weald", "mother_of_threads", "apprentice"), ("weald", "dreaming_general", "apprentice"),
    ("warrens", "meat_hook", "apprentice"), ("warrens", "harvest_child", "apprentice"),
    ("cove", "leviathan", "apprentice"), ("cove", "archduke", "apprentice"),
    ("darkestdungeon", "shackles_of_denial", "darkest"), ("darkestdungeon", "seething_sigh", "darkest"),
    ("darkestdungeon", "focused_fault", "darkest"), ("darkestdungeon", "ravenous_reach", "darkest"),
    ("darkestdungeon", "body_of_work", "darkest"),
]
TAKE_ALL = "GameInstaller(Clone)/screen_stack/layers/Layer - Loot/Loot(Clone)/Canvas_group/TakeAllButton/button_prefab"


def call(req):
    r = bridge.send(req)
    if not r.get("ok"):
        raise RuntimeError(r.get("error"))
    return r.get("result")


def state():
    s = call({"cmd": "run", "name": "estate.state"})
    return s["mode"], s["changing"]


def wait(pred, seconds):
    deadline = time.time() + seconds
    while time.time() < deadline:
        mode, changing = state()
        if not changing and pred(mode):
            return mode
        time.sleep(1)
    return None


def clickables():
    return call({"cmd": "observe"})["options"]


def one(dungeon, boss, tier):
    started = call({"cmd": "run", "name": "estate.boss", "dungeon": dungeon, "boss": boss, "tier": tier})
    if not wait(lambda m: m == "COMBAT", 90):
        return f"{started}: never reached COMBAT (mode {state()})"
    time.sleep(8)
    dev.shot("boss_" + boss)
    waves = 0
    for _ in range(8):
        call({"cmd": "invoke", "expr": "$CombatBhv", "method": "ForceEndCombat", "args": [True]})
        waves += 1
        deadline = time.time() + 60
        while time.time() < deadline:
            mode, changing = state()
            if changing:
                time.sleep(1)
                continue
            if mode == "ESTATE":
                return f"{started}: ok, {waves} wave(s), no loot screen"
            if mode == "RESULTS":
                time.sleep(4)
                try:
                    call({"cmd": "click", "path": TAKE_ALL})
                except RuntimeError:
                    pass
                back = wait(lambda m: m in ("ESTATE", "COMBAT"), 60)
                if back == "ESTATE":
                    return f"{started}: ok, {waves} wave(s)"
                if back == "COMBAT":
                    break       # next wave after the loot screen
                return f"{started}: stuck after the loot screen (mode {state()})"
            # still COMBAT: a "continue to the next battle" dialog, or the next wave already running
            labels = [o["label"] for o in clickables()]
            for o in clickables():
                if o["label"].lower() in ("continue", "advance", "confirm", "yes", "press on", "onward", "fight"):
                    call({"cmd": "click", "id": o["id"]})
                    break
            time.sleep(2)
            if any("skill" in o["path"] for o in clickables()):
                break           # a fresh battle is waiting for input: next wave
        else:
            return f"{started}: stuck in {state()[0]} after forcing the end; clickable: {labels[:8]}"
        time.sleep(6)
    return f"{started}: more than 8 waves?"


if __name__ == "__main__":
    first = int(sys.argv[1]) if len(sys.argv) > 1 else 0
    count = int(sys.argv[2]) if len(sys.argv) > 2 else len(BOSSES)
    for dungeon, boss, tier in BOSSES[first:first + count]:
        try:
            print(boss, "->", one(dungeon, boss, tier), flush=True)
        except Exception as e:  # keep going: one broken boss should not hide the others
            print(boss, "-> ERROR", e, flush=True)
        if state()[0] != "ESTATE":
            print("not back in the hub; stopping")
            break
        time.sleep(3)
