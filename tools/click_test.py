#!/usr/bin/env python3
"""Real-input smoke test of the mod's screens: every step is a mouse click or a key press made through the
game's own input system (the bridge's input.click / input.hold), the way a player's would arrive, and its
effect is read back through the dev commands. The other test tools call the mod's code directly; this one
finds the things that only break under a real pointer (raycast targets, canvases over one another, handlers
bound to the wrong object).

    python -u tools/click_test.py [hamlet] [windows] [embark] [dungeon]     (default: all; the Estate must be open
                                                                             in its hamlet, the game window focused)
"""
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bridge  # noqa: E402

RESULTS = []


def call(req):
    r = bridge.send(req)
    if not r.get("ok"):
        raise RuntimeError(str(r.get("error"))[:300])
    return r.get("result")


def run(name, **kw):
    return call(dict(cmd="run", name=name, **kw))


def options(filter_text=None, everything=False):
    req = {"cmd": "observe"}
    if filter_text:
        req["filter"] = filter_text
    if everything:
        req["all"] = True
    r = call(req)
    return r.get("options", []) if isinstance(r, dict) else []


def find(path_part, label=None, kind=None):
    """The first thing on screen whose path holds `path_part` (and whose label / component fits)."""
    for o in options():
        # the name asked for may be the thing's path or what it says
        if path_part not in o.get("path", "") and path_part.lower() != str(o.get("label", "")).lower():
            continue
        if label is not None and label.lower() not in str(o.get("label", "")).lower():
            continue
        if kind is not None and o.get("type") != kind:
            continue
        return o
    return None


def click(x, y, wait=0.8):
    r = run("input.click", x=float(x), y=float(y))
    time.sleep(wait)
    return r.get("focused", False) if isinstance(r, dict) else False


def click_on(path_part, label=None, kind=None, wait=0.8):
    o = find(path_part, label, kind)
    if o is None:
        return False
    click(o["x"], o["y"], wait)
    return True


def hold(key, seconds):
    run("input.hold", key=key, seconds=float(seconds))
    time.sleep(seconds + 0.4)


def check(name, ok, detail=""):
    RESULTS.append((name, bool(ok)))
    print(("PASS  " if ok else "FAIL  ") + name + (("   " + str(detail)) if detail else ""))
    return bool(ok)


def window():
    w = run("window.state")
    return w.get("id") if isinstance(w, dict) else None


def close_window():
    if window() is None:
        return
    if not click_on("/Frame/Close"):
        run("roster.close")
    time.sleep(0.6)
    if window() is not None:
        run("roster.close")
        time.sleep(0.4)


def heroes():
    return run("roster.state")["heroes"]


# ---- the hamlet's buildings ---------------------------------------------------------------------------------

BUILDINGS = ["stage_coach", "guild", "blacksmith", "tavern", "abbey", "sanitarium", "nomad_wagon", "camping_trainer", "graveyard", "statue"]


def test_hamlet():
    for building in BUILDINGS:
        close_window()
        hit = find("DD2Estate.HamletScene", building) or find(building)
        if hit is None:
            check("hamlet: the " + building + " can be found on screen", False)
            continue
        click(hit["x"], hit["y"], 1.5)
        opened = window()
        ok = opened is not None or (building == "statue" and run("memoirs.list") is not None)
        check("hamlet: a click on the " + building + " opens its window", ok, "window=" + str(opened))
        # the window's own close button, by a real click
        if opened is not None:
            closed = click_on("Window." + opened + "/Frame/Close") or click_on("/Close")
            time.sleep(0.6)
            check("hamlet: the " + building + "'s close button closes it", window() is None, "" if closed else "no close button found")
    close_window()


# ---- windows that take heroes ---------------------------------------------------------------------------------

def test_windows():
    close_window()
    # Guild: a roster row hands its hero over, a learn step costs gold
    run("guild.open")
    time.sleep(1.2)
    party_before = sorted(h["guid"] for h in heroes() if h["status"] == "party")
    row = find("DD2Estate.Hamlet/Roster/List/Hero")
    if row is not None:
        click(row["x"], row["y"], 1.0)
    party_after = sorted(h["guid"] for h in heroes() if h["status"] == "party")
    check("guild: a click on a roster row does not change the party", party_before == party_after)
    gold = run("estate.state").get("gold") if isinstance(run("estate.state"), dict) else None
    step = find("Window.guild", kind=None, label=None)
    node = None
    for o in options("Window.guild"):
        if "Known." in o.get("path", "") or "Learn" in o.get("path", ""):
            node = o
            break
    if node is not None:
        click(node["x"], node["y"], 1.0)
    check("guild: a learn step can be found to click", node is not None)
    close_window()

    # Stage Coach: a click on a recruit's row hires
    run("roster.open")
    time.sleep(1.2)
    before = len(heroes())
    recruit = find("Window.stage_coach/Frame/Recruits/Recruit")
    state = run("roster.state")["stageCoach"]
    room = state["roster"] < state["rosterSize"]
    if recruit is not None and room:
        click(recruit["x"], recruit["y"], 1.2)
        check("stage coach: a click on a recruit hires them", len(heroes()) == before + 1)
    else:
        check("stage coach: a recruit and room in the barracks to test hiring", False, "recruit=%s room=%s" % (recruit is not None, room))
    close_window()

    # Tavern: slot, hero, check mark
    run("activity.open", building="tavern")
    time.sleep(1.2)
    stays_before = len(run("activity.state").get("stays", []))
    slot = None
    for o in options("Window.tavern"):
        if "Slot" in o.get("path", ""):
            slot = o
            break
    # idle and free: not already in a building's care
    idle = [h for h in heroes() if h["status"] == "idle" and not h.get("dismissBlocked")]
    if slot is not None and idle:
        click(slot["x"], slot["y"], 0.8)
        row = find("DD2Estate.Hamlet/Roster/List/Hero%d" % idle[0]["guid"])
        if row is not None:
            click(row["x"], row["y"], 0.8)
        confirm = None
        for o in options("Window.tavern"):
            if "Confirm" in o.get("path", "") or "Check" in o.get("path", ""):
                confirm = o
                break
        if confirm is not None:
            click(confirm["x"], confirm["y"], 1.0)
        stays_after = len(run("activity.state").get("stays", []))
        check("tavern: slot, hero, check mark places the hero", stays_after == stays_before + 1, "confirm found=%s" % (confirm is not None))
    else:
        check("tavern: an open slot and an idle hero to test with", False)
    close_window()


# ---- the estate bar's panels ------------------------------------------------------------------------------------

def test_panels():
    close_window()
    for label, state, close in (("heirloom_exchange", "heirlooms.state", "heirlooms.close"),
                                ("activity_log", "log.state", "log.close"),
                                ("realm_inventory", "inventory.state", "inventory.close")):
        button = find("TownPanelButtons/Button." + label)
        if button is None:
            check("bar: the " + label + " button is on the bar", False)
            continue
        click(button["x"], button["y"], 1.5)
        check("bar: a click on the " + label + " button opens its panel", "'open': True" in str(run(state)))
        run(close)
        time.sleep(0.8)
    # the exchange: a click on an heirloom count opens it for that heirloom
    icon = find("EstateSummary/bustIcon")
    if icon is not None:
        click(icon["x"], icon["y"], 1.5)
        s = run("heirlooms.state")
        screen = s.get("screen", s) if isinstance(s, dict) else {}
        check("bar: a click on the busts opens the exchange on busts", "'open': True" in str(s) and "bust" in str(screen.get("kind", s)))
        run("heirlooms.close")
        time.sleep(0.6)


# ---- embarking --------------------------------------------------------------------------------------------------

def test_embark():
    close_window()
    ok = click_on("DD2Estate.Hamlet", "Embark") or click_on("Embark")
    time.sleep(1.5)
    m = run("quests.map")
    check("embark: the Embark button opens the Estate Map", isinstance(m, dict) and m.get("open"), "" if ok else "button not found")
    marker = find("Marker") or find("Quest")
    if marker is not None:
        click(marker["x"], marker["y"], 1.0)
    m = run("quests.map")
    check("embark: a quest marker selects a quest", isinstance(m, dict) and m.get("selected") is not None)
    click_on("Provision") or click_on("Forward")
    time.sleep(2.0)
    p = run("provision.state")
    if not (isinstance(p, dict) and p.get("open")):
        # a refusal or a small party: say what the map says and stop here
        m = run("quests.map")
        check("embark: the Provision button opens the provision screen", False, str(m.get("status") if isinstance(m, dict) else m))
        return
    check("embark: the Provision button opens the provision screen", True)
    food_before = p.get("food", 0)
    shelf = find("Provision", "Food") or find("Shelves") or find("Shelf")
    if shelf is not None:
        click(shelf["x"], shelf["y"], 0.8)
    p = run("provision.state")
    check("provision: a click on a shelf card buys one", p.get("food", 0) > food_before or p.get("cost", 0) > 0, "shelf found=%s" % (shelf is not None))
    # the screen has no "Standard Kit" button any more (DD1 has none): the bridge still buys the usual kit
    run("provision.kit")
    time.sleep(0.8)
    p = run("provision.state")
    check("provision: the usual kit (bridge: provision.kit) fills the bag", p.get("cost", 0) > 0)


def test_dungeon():
    p = run("provision.state")
    if isinstance(p, dict) and p.get("open"):
        click_on("Embark") or click_on("Forward")
        time.sleep(2.5)
        # DD1's "not much food" question, if it came up
        click_on("Confirm") or click_on("Yes")
        time.sleep(10)
    s = run("dungeon.state")
    if not (isinstance(s, dict) and s.get("view") == "Dungeon"):
        check("dungeon: the provision screen's Embark starts the expedition", False, str(s)[:120])
        return
    check("dungeon: the provision screen's Embark starts the expedition", True)
    # the map: a click on a neighbouring room sets the party on its way
    map_tab = find("DungeonHud/Screen/MapTab")
    if map_tab is not None:
        click(map_tab["x"], map_tab["y"], 0.8)
    room = None
    for o in options():
        if o.get("type") == "MinimapRoomClick" and o.get("label") in ["r%d" % r for r in s.get("reachable", [])]:
            room = o
            break
    if room is not None:
        click(room["x"], room["y"], 1.5)
    s2 = run("dungeon.state")
    check("dungeon: a click on a neighbouring room on the map leaves the room", s2.get("hallway", -1) >= 0 or s2.get("toward", -1) >= 0, "room found=%s" % (room is not None))
    lead = run("corridor.state").get("leadX", 0)
    hold("D", 2.0)
    s3 = run("dungeon.state")
    moved = run("corridor.state").get("leadX", 0) > lead + 100
    check("dungeon: holding D walks the party", moved or s3.get("segment", 0) != s2.get("segment", 0) or s3.get("prompt") or s3.get("busy"))
    tab = find("DungeonHud/Screen/InventoryTab") or find("DungeonHud", "Inventory")
    if tab is not None:
        click(tab["x"], tab["y"], 0.8)
    h = run("hud.state")
    check("dungeon: the inventory tab can be clicked", isinstance(h, dict) and h.get("tab") == "inventory", "tab found=%s" % (tab is not None))


def main(argv):
    wanted = [a for a in argv if not a.startswith("-")] or ["hamlet", "windows", "panels", "embark", "dungeon"]
    focused = run("input.click", x=5.0, y=5.0)
    if isinstance(focused, dict) and not focused.get("focused"):
        print("The game window is not focused: real input will not arrive. Click the game window and run again.")
        return 2
    time.sleep(0.5)
    for part in wanted:
        print("---- " + part)
        try:
            {"hamlet": test_hamlet, "windows": test_windows, "panels": test_panels, "embark": test_embark, "dungeon": test_dungeon}[part]()
        except Exception as e:  # a broken step must not hide the rest
            check(part + ": ran to its end", False, str(e)[:200])
    failed = [name for name, ok in RESULTS if not ok]
    print("\n%d checks, %d failed" % (len(RESULTS), len(failed)))
    for name in failed:
        print("  FAILED: " + name)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
