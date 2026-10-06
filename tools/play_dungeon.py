#!/usr/bin/env python3
"""Plays the current generated dungeon through the dev bridge: walks to unvisited rooms, answers prompts,
forces fights to a win, leaves when the objective is complete. It plays the bag the way a careful player
would: a shovel for obstacles, the right item for a curio, a meal when hungry, a torch when the light sinks
below LIGHT_FLOOR, spare food for the wounded; loot that does not fit is left behind. A curio's window does
not come by itself (DD1's does not either): the bot turns to every curio it stands at (curio.here), and a
spotted trap it simply walks into, as a party that does not stop does.

A fight's loot is DD1's: after a won fight DD2's results scene passes by itself (nothing of DD2's is granted,
so its loot window has nothing to show), the corridor is back and DD1's "Victory!" scroll is up over it. The
scroll is a prompt like a chest's ("Take All", "Close"): main() answers it with option 0, which takes what
fits and leaves the rest.

    python tools/play_dungeon.py [max_steps]      (an expedition must be running: estate.embark or quests.embark)
"""
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bridge  # noqa: E402

# DD2's own loot window. It must not open in the Estate any more; back_from_fight() looks for it only to say so.
DD2_TAKE_ALL = "GameInstaller(Clone)/screen_stack/layers/Layer - Loot/Loot(Clone)/Canvas_group/TakeAllButton/button_prefab"
TAKE_ALL = DD2_TAKE_ALL     # the name tools/sky_watch.py clicks it by (it expects the click to fail when nothing is there)
RESULTS_PATIENCE = 15   # seconds on DD2's results scene before its loot window is looked for
LIGHT_FLOOR = 50        # below this a torch is lit (DD1's dim band starts here)
FOOD_RESERVE = 4        # a meal for four is kept back for the next hunger check
HURT = 0.6              # heroes under this share of their health are fed from the spare food


def call(req):
    r = bridge.send(req)
    if not r.get("ok"):
        raise RuntimeError(r.get("error"))
    return r.get("result")


def run(name, **kw):
    return call(dict(cmd="run", name=name, **kw))


def mode():
    return call({"cmd": "get", "expr": "Assets.Code.Game.GameModeMgr.CurrentMode.GetName()"})


def no_key_at_loading():
    """DD1's loading screen on the way into a dungeon waits for a key (Dd2/LoadingScreen.cs). A script presses
    none: the screens leave by themselves from here on (until the game is closed)."""
    try:
        run("loading.auto", on=True)
    except RuntimeError:
        pass        # a plugin from before the loading screens


def settled():
    return not run("estate.state")["changing"]


def wait_mode(name, seconds=60):
    deadline = time.time() + seconds
    while time.time() < deadline:
        if mode() == name and settled():
            return True
        time.sleep(1)
    return False


def win_fight():
    if not wait_mode("COMBAT", 40):
        raise RuntimeError("no fight started")
    time.sleep(6)
    call({"cmd": "invoke", "expr": "$CombatBhv", "method": "ForceEndCombat", "args": [True]})
    back_from_fight()


def back_from_fight(seconds=80):
    """Waits for the hub after a fight. DD2's results scene passes by itself: no DD2 loot is granted, so no loot
    window opens (what the fight paid waits on DD1's scroll in the corridor, and main() takes it). Should DD2's
    window be up after all (an older build of the mod, or a fault), it is taken so that the run goes on, and
    the fault is printed."""
    deadline = time.time() + seconds
    on_results = 0
    while time.time() < deadline:
        m = mode()
        if m == "ESTATE" and settled():
            time.sleep(2)
            return
        if m == "RESULTS":
            on_results += 1
            if on_results % RESULTS_PATIENCE == 0:
                try:
                    call({"cmd": "click", "path": DD2_TAKE_ALL})
                    print("  WARNING: DD2's own loot window opened after the fight; fight loot is to come on DD1's scroll")
                except RuntimeError:
                    pass        # no such window: the scene is only taking its time
        time.sleep(1)
    raise RuntimeError("did not return to the estate")


def choose(options):
    """The answer a careful player gives: the meal, else the first option (the hand of a curio or an obstacle)."""
    for i, text in enumerate(options):
        if text.startswith("Use ") or text.startswith("Eat "):
            return i
    return 0


def offer_item():
    """With a curio's or an obstacle's scroll up: the item of the bag that goes on it is offered to the scroll's
    slot, as a right click on its card does (DD1: "[RIGHT-CLICK] an inventory item to apply it to the object").
    The shovel for an obstacle; for a curio the item it reacts to (curio.here names it). Returns the item's
    name when the scroll took one."""
    hud = run("hud.state")
    window = hud.get("window") if isinstance(hud, dict) else None
    bag = run("dungeon.bag")
    items = bag["items"] if isinstance(bag, dict) else []
    slot = -1
    if window == "obstacle":
        slot = next((i["slot"] for i in items if i["item"] == "supply:shovel"), -1)
    elif window == "curio":
        here = run("curio.here")
        slot = here.get("itemSlot", -1) if isinstance(here, dict) else -1
    if slot < 0:
        return None
    r = run("hud.item", slot=slot)
    if isinstance(r, dict) and not r["hud"].get("asking"):
        return next((i["name"] for i in items if i["slot"] == slot), "item")
    return None


def turn_to_curio(s, turned):
    """DD1 asks nothing when the party comes to a curio: the player turns to it (a click on it, or W). The bot
    turns to every curio it stands at, once: its window comes up, or a curio without one is opened at once."""
    try:
        here = run("curio.here")
    except RuntimeError:
        return False        # a plugin from before curios waited to be turned to: their question comes by itself
    if not isinstance(here, dict) or not here.get("curio"):
        return False
    place = (s.get("room"), s.get("hallway"), s.get("segment"))
    if place in turned:
        return False
    turned.add(place)
    r = run("curio.here", turn=True)
    print("  curio:", here.get("title") or here["curio"], "-> turned to" if isinstance(r, dict) and r.get("turned") else "-> cannot be turned to now")
    return True


def bag_summary():
    bag = run("dungeon.bag")
    if not isinstance(bag, dict):
        return str(bag)
    return ", ".join("%s x%s" % (i["name"], i["amount"]) for i in bag["items"]) or "empty"


def tend(s):
    """Out of a prompt: light a torch when it is getting dark, feed the wounded from the spare food. True if something was used."""
    if s.get("torches") and s["light"] < LIGHT_FLOOR:
        left = run("dungeon.torch")
        print("  light %.0f: torch lit, %s left" % (s["light"], left))
        return True
    if (s.get("food") or 0) > FOOD_RESERVE:
        bag = run("dungeon.bag")
        for hero in bag["heroes"] if isinstance(bag, dict) else []:
            if hero["hpMax"] > 0 and hero["hp"] / hero["hpMax"] < HURT:
                r = run("dungeon.use", item="food", hero=hero["guid"])
                if isinstance(r, dict) and r.get("used"):
                    print("  " + r["message"])
                    return True
    return False


def next_room(here, graph, visited):
    """The neighbour to step to: an unvisited one if there is one, else the first step of the shortest known
    way to a room that has an unvisited neighbour (breadth first over the rooms seen so far), so the walk
    covers the map instead of bouncing between rooms it knows."""
    fresh = [r for r in graph[here] if r not in visited]
    if fresh:
        return fresh[0]
    first = {here: None}
    queue = [here]
    while queue:
        room = queue.pop(0)
        for nxt in graph.get(room, []):
            if nxt in first:
                continue
            first[nxt] = nxt if room == here else first[room]
            if nxt not in visited or any(r not in visited for r in graph.get(nxt, [])):
                return first[nxt]
            queue.append(nxt)
    # everything known is visited: keep moving so that a quest that wants more (battles, curios) can find it
    return graph[here][len(visited) % len(graph[here])]


def fast():
    """Test pace: the party walks three times as fast and the door sequence runs twenty times faster (it still
    runs, so the code is exercised). Call with the corridor on screen; harmless otherwise."""
    try:
        call({"cmd": "set", "expr": "DD2Estate.Dungeon.CorridorView", "member": "WalkSpeedScale", "value": 3.0})
        run("corridor.transition", run=False, timescale=20)
    except RuntimeError:
        pass


def main(max_steps):
    visited = set()
    graph = {}
    turned = set()      # where the bot has turned to a curio already
    fast()
    fights = prompts = used = 0
    for step in range(max_steps):
        if mode() != "ESTATE" or not settled():
            win_fight()
            fights += 1
            continue
        results = run("results.state")
        if isinstance(results, dict) and results.get("open"):
            print("the results are up after", step, "steps;", fights, "fights,", prompts, "prompts,", used, "items used")
            return 0
        s = run("dungeon.state")
        if s["view"] != "Dungeon":
            print("left the dungeon after", step, "steps;", fights, "fights,", prompts, "prompts,", used, "items used")
            return 0
        if s.get("picking"):
            # asked for a stack to drop: decline, the question comes back and is answered with "leave it"
            run("dungeon.answer", option=0)
            time.sleep(0.5)
            continue
        if s["prompt"]:
            options = s.get("options") or []
            # a curio's or an obstacle's scroll: the item of the bag that goes on it, if the party carries one
            offered = offer_item()
            if offered:
                print("  prompt:", s["prompt"], "->", offered)
                used += 1
            else:
                option = choose(options)
                print("  prompt:", s["prompt"], "->", options[option] if options else "option 0")
                run("dungeon.answer", option=option)
            prompts += 1
            time.sleep(0.5)
            continue
        if s["busy"]:
            time.sleep(0.5)
            continue
        if s["complete"]:
            print("objective complete, leaving with:", bag_summary())
            run("dungeon.leave")
            time.sleep(2)
            continue
        if tend(s):
            used += 1
            time.sleep(0.5)
            continue
        if turn_to_curio(s, turned):
            time.sleep(0.5)
            continue
        if s["room"] is not None and s["room"] >= 0:
            visited.add(s["room"])
            graph[s["room"]] = list(s["reachable"])
            if not s["reachable"]:
                print("nowhere to go", s)
                return 1
            target = next_room(s["room"], graph, visited)
            print(f"step {step}: room {s['room']} -> {target}  progress {s['progress']}/{s['required']} light {s['light']:.0f}"
                  f" torches {s.get('torches')} food {s.get('food')}")
            run("dungeon.goto", room=target)
            time.sleep(0.6)
            # the party stands at the door until it walks: the exploration only leaves the room on the first tile
            run("corridor.walk", dir=1, seconds=1.5)
            time.sleep(1.8)
            continue
        # in a hallway (or standing at its door): walk on; the door at its end is used (W, or a click on it), as
        # in DD1: the party does not walk through it by itself
        c = run("corridor.state")
        if isinstance(c, dict) and c.get("leadX", 0) >= c.get("length", 1e9) - 1:
            run("corridor.door")
            time.sleep(1.5)
            continue
        run("corridor.walk", dir=1, seconds=1.5)
        time.sleep(1.8)
    print("step limit reached")
    return 1


if __name__ == "__main__":
    sys.exit(main(int(sys.argv[1]) if len(sys.argv) > 1 else 400))
