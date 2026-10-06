#!/usr/bin/env python3
"""Plays the current DD2 battle for real (no forced end): picks a skill button, then tells the battle which
enemy was chosen the way a click on that enemy does (EventSelectActor with isUserInput), and passes
(EventBattlePass) when no skill takes any enemy. Crude, but it exercises the natural victory/defeat path:
clicks on enemy colliders do not register through the bridge, the events do.

After the fight DD2's results scene passes by itself: the Estate takes no DD2 loot, so DD2's loot window has
nothing to show. What the fight paid is on DD1's scroll once the corridor is back (tools/play_dungeon.py takes it).

    python tools/auto_fight.py [seconds]
"""
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bridge  # noqa: E402

BAR = "Arena/CombatUI/CombatInterfaceBar/skill_selection_panel/"
HURT = 0.5              # a hero under this share of their health is tended before the enemy is struck
# DD2's own loot window: it must not open in the Estate any more, and is looked for only to say so.
DD2_TAKE_ALL = "GameInstaller(Clone)/screen_stack/layers/Layer - Loot/Loot(Clone)/Canvas_group/TakeAllButton/button_prefab"
RESULTS_PATIENCE = 15   # seconds on DD2's results scene before its loot window is looked for


def call(req):
    r = bridge.send(req)
    if not r.get("ok"):
        raise RuntimeError(r.get("error"))
    return r.get("result")


def mode():
    s = call({"cmd": "run", "name": "estate.state"})
    return s["mode"], s["changing"]


def options(filter_text):
    return call({"cmd": "observe", "filter": filter_text})["options"]


def enemies():
    """Guids of the living enemies and their health, from the mod's difficulty report."""
    found = call({"cmd": "run", "name": "difficulty.enemies"})
    return [e for e in (found.get("enemies") or []) if e.get("living")] if isinstance(found, dict) else []


def party():
    """The party's heroes with their health: from the expedition's bag report (it knows the maximum), else from
    the estate's state (health only; nobody counts as hurt then)."""
    try:
        bag = call({"cmd": "run", "name": "dungeon.bag"})
        if isinstance(bag, dict) and bag.get("heroes"):
            return [h for h in bag["heroes"] if h.get("hp", 0) >= 0]
    except RuntimeError:
        pass
    state = call({"cmd": "run", "name": "estate.state"})
    return [h for h in state.get("heroes", []) if h.get("party") and h.get("hp", 0) >= 0]


def trigger(event, *args):
    call({"cmd": "invoke", "expr": event, "method": "Trigger", "args": list(args)})


def main(limit):
    start = time.time()
    turn = 0
    acted = 0
    on_results = 0
    while time.time() - start < limit:
        m, changing = mode()
        if changing:
            time.sleep(1)
            continue
        if m == "RESULTS":
            # the scene passes by itself; DD2's loot window is only looked for when it does not
            on_results += 1
            if on_results % RESULTS_PATIENCE == 0:
                try:
                    call({"cmd": "click", "path": DD2_TAKE_ALL})
                    print("  WARNING: DD2's own loot window opened after the fight; fight loot is to come on DD1's scroll")
                except RuntimeError:
                    pass
            time.sleep(1)
            continue
        if m != "COMBAT":
            print(f"battle over after {time.time() - start:.0f}s, mode {m}, {acted} skills used")
            return 0
        skills = [o for o in options(BAR + "combat_skill_button") if o["interactable"]]
        if not skills:
            time.sleep(1)       # enemy turn or an animation
            continue
        turn += 1
        foes = enemies()
        before = sum(e["hp"] for e in foes)
        used = False
        # Somebody badly hurt: first see whether a skill takes that hero as its target (a heal, a guard); a
        # player would. Otherwise skills from the left, the first that accepts an enemy on the weakest it accepts.
        heroes = party()
        hurt = sorted((h for h in heroes if h.get("hpMax", 0) > 0 and h["hp"] < HURT * h["hpMax"]), key=lambda h: h["hp"])
        passes = [("ally", hurt[:1])] if hurt else []
        passes.append(("enemy", sorted(foes, key=lambda e: e["hp"])))
        for kind, targets in passes:
            for k in range(len(skills)):
                skill = skills[(turn + k) % len(skills)]
                try:
                    call({"cmd": "click", "id": skill["id"]})
                except RuntimeError:
                    break           # the bar changed under us: look again
                time.sleep(0.6)
                for target in targets:
                    trigger("Assets.Code.Actor.Events.EventSelectActor", target["guid"], True)
                    time.sleep(0.4)
                    if not [o for o in options(BAR + "combat_skill_button") if o["interactable"]]:
                        used = True
                        break
                if used:
                    break
                skills = [o for o in options(BAR + "combat_skill_button") if o["interactable"]]
                if not skills:
                    used = True
                    break
            if used or not skills:
                break
        if used:
            acted += 1
            time.sleep(3.5)
            after = sum(e["hp"] for e in enemies())
            print(f"  turn {turn}: enemies {len(foes)} hp {before} -> {after}")
        else:
            trigger("Assets.Code.Combat.Events.EventBattlePass", 0)
            print(f"  turn {turn}: passed")
            time.sleep(3)
    print("time limit reached, mode", mode())
    return 1


if __name__ == "__main__":
    sys.exit(main(int(sys.argv[1]) if len(sys.argv) > 1 else 420))
