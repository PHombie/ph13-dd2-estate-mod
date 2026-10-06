#!/usr/bin/env python3
"""Goes through every passage of an Estate session and writes down where DD2's own world shows: its stagecoach
above all (the Estate plays DD1's expeditions; there is no coach on one), its road, its inn. Two watchers run
inside the game the whole way: the film (estate.skytrap film=N: every N-th frame, small) and the scene watch
(estate.scenewatch: per frame the scenes with something drawn, and whether a coach is among it).

    python -u tools/coach_watch.py <folder> [legs] [afterfight=dd2] [coach=true]
                                                        the game at its main menu, on the test estate

afterfight=dd2 and coach=true put DD2's results scene and its coach back for the trip (scenes.state): how it was.

Legs, in this order (all by default; name some, comma separated, to go only through those; "enter" and "setout"
are needed by the ones after them):
    enter     the main menu into the Estate's hamlet
    setout    hamlet, quest, provisions, the dungeon's first room
    hallway   into a hallway; a fight called there and won; back in the hallway (the loot scroll)
    room      on to the next room; a fight there (its own, or called) and won; back in the room
    flee      a fight called and fled
    camp      a camp to the end of a quiet night
    ambush    a camp whose night is an ambush; the fight won
    boss      a boss's fight called from the dungeon and won
    retreat   the quest abandoned: the results screens, the way home, the hamlet
    lost      a second expedition; a fight in which everybody dies; the results; the hamlet
    again     a third, with other heroes: a fight won after the loss (a fight must still come to its end), home
    leave     the Estate left for the main menu

Written into <folder>: film/ (the frames), watch.json (the legs, the scene watch's changes, the film's list),
"NN <leg>.png" (a sheet of frames per leg; frames with a coach in view are marked red), coach.png (only the frames
with a coach in view), states/ (dungeon.state, ledger.books and the like after each fight). The summary it prints
names every leg in which a coach was drawn while the fader was not black.
"""
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bridge  # noqa: E402
import play_dungeon as pd  # noqa: E402

from PIL import Image, ImageDraw  # noqa: E402

ALL = ["enter", "setout", "hallway", "room", "flee", "camp", "ambush", "boss", "retreat", "lost", "again", "leave"]
LEGS = []           # (name, the game's frame number it began at)
NOTES = []
FOLDER = None
FILM_EVERY = 4


def note(*words):
    line = " ".join(str(w) for w in words)
    NOTES.append(line)
    print("   " + line, flush=True)


def frame_now():
    return pd.run("estate.scenewatch")["now"]


def leg(name):
    LEGS.append((name, frame_now()))
    print(".. " + name, flush=True)


def keep(name, value):
    os.makedirs(os.path.join(FOLDER, "states"), exist_ok=True)
    with open(os.path.join(FOLDER, "states", name + ".json"), "w", encoding="utf-8") as f:
        json.dump(value, f, indent=1, ensure_ascii=False)


def state(name):
    """What a fight left behind: the expedition, the party, the books."""
    out = {}
    for command in ("dungeon.state", "dungeon.bag", "estate.state", "ledger.books", "ledger.state", "fight.loot"):
        try:
            out[command] = pd.run(command)
        except RuntimeError as e:
            out[command] = "ERROR " + str(e)
    keep(name, out)
    return out


def wait(test, seconds, step=0.4):
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


def prompts(limit=6):
    """Answers what the expedition asks (a loot scroll: take all; a curio, hunger: the first answer)."""
    for _ in range(limit):
        s = pd.run("dungeon.state")
        if s.get("picking"):
            pd.run("dungeon.answer", option=0)
        elif s.get("prompt"):
            note("prompt:", s["prompt"][:60], "->", (s.get("options") or ["?"])[0])
            pd.run("dungeon.answer", option=0)
        elif s.get("busy"):
            pass
        else:
            return s
        time.sleep(0.7)
    return pd.run("dungeon.state")


def fight(call, end, name):
    """A fight begins by `call`, stands for a few seconds, ends by `end`; the hub is waited for."""
    call()
    if not wait(lambda: pd.mode() == "COMBAT" and pd.settled(), 50):
        note(name + ": no fight began; mode", pd.mode())
        return False
    time.sleep(5)
    keep(name + "_world_fight", pd.run("estate.world"))
    try:
        keep(name + "_scenes_fight", pd.run("scenes.state"))
    except RuntimeError:
        pass
    end()
    t0 = time.time()
    seen = set()
    while time.time() - t0 < 80:
        m = pd.mode()
        if m not in seen:
            seen.add(m)
            if m == "RESULTS":
                time.sleep(1.2)
                try:
                    keep(name + "_world_results", pd.run("estate.world"))
                except RuntimeError:
                    pass
        if m == "ESTATE" and pd.settled():
            break
        time.sleep(0.25)
    else:
        note(name + ": the hub did not come back; mode", pd.mode())
        return False
    note(name + ": back in the hub after %.1f s; modes on the way: %s" % (time.time() - t0, ", ".join(sorted(seen))))
    time.sleep(2.5)
    return True


def win():
    pd.call({"cmd": "invoke", "expr": "$CombatBhv", "method": "ForceEndCombat", "args": [True]})


def flee():
    pd.call({"cmd": "invoke", "expr": "$CombatBhv", "method": "ForceEndCombat", "args": [False]})


def party():
    return [h["guid"] for h in pd.run("estate.state")["heroes"] if h["party"]]


def die():
    for guid in party():
        note(pd.run("roster.kill", guid=guid))
        time.sleep(0.3)


# ---- the legs ---------------------------------------------------------------------------------------------------

def do_enter():
    pd.no_key_at_loading()
    pd.run("estate.enter")
    wait(in_hub, 90)
    time.sleep(6)
    try:
        pd.run("tutorial.state", enabled=False)
    except RuntimeError:
        pass


def do_setout():
    quests = pd.run("quests.list")
    index = next((i for i, q in enumerate(quests) if not q.get("Boss")), 0)
    note("quest:", quests[index]["Name"], quests[index]["Dungeon"], quests[index]["LengthName"])
    pd.run("quests.open")
    time.sleep(1.0)
    pd.run("quests.select", index=index)
    for _ in range(3):
        pd.run("quests.forward")
        time.sleep(1.5)
        p = pd.run("provision.state")
        if isinstance(p, dict) and p.get("open"):
            break
    pd.run("provision.kit")
    time.sleep(0.5)
    pd.run("provision.setout")
    wait(lambda: pd.run("dungeon.state")["view"] == "Dungeon" and in_hub() and pd.run("dungeon.state")["room"] is not None, 60)
    time.sleep(6)
    pd.fast()
    prompts()


def do_hallway():
    s = prompts()
    if s["room"] is not None and s["room"] >= 0 and s["reachable"]:
        pd.run("dungeon.goto", room=s["reachable"][0])
        time.sleep(0.8)
        pd.run("corridor.walk", dir=1, seconds=1.5)
        time.sleep(2.5)
    prompts()
    state("hallway_before")
    if fight(lambda: note(pd.run("fightbag.fight")), win, "hallway"):
        state("hallway_after")
        prompts()


def do_room():
    t0 = time.time()
    while time.time() - t0 < 70 and pd.mode() == "ESTATE":
        s = prompts()
        if s["room"] is not None and s["room"] >= 0:
            break
        pd.run("corridor.door")
        time.sleep(1.2)
    time.sleep(3)
    if pd.mode() == "ESTATE" and pd.settled():
        note("room: no fight of its own; one is called")
        ok = fight(lambda: note(pd.run("fightbag.fight")), win, "room")
    else:
        ok = fight(lambda: None, win, "room")
    if ok:
        state("room_after")
        prompts()


def do_flee():
    prompts()
    if fight(lambda: note(pd.run("fightbag.fight")), flee, "flee"):
        state("flee_after")
        prompts()


def camp_up():
    r = pd.run("camp.start", force=True)
    note("camp:", r)
    if not wait(lambda: pd.run("camp.state")["view"] in ("shown", "failed"), 30):
        note("camp: the rest stop did not come up:", pd.run("camp.state")["view"])
    time.sleep(3)
    keep("camp_world", pd.run("estate.world"))
    try:
        keep("camp_inn", pd.run("camp.inn"))
    except RuntimeError as e:
        note("camp.inn:", e)
    note("meal:", pd.run("camp.eat", meal="none"))
    time.sleep(2)


def do_camp():
    prompts()
    camp_up()
    note("sleep:", pd.run("camp.sleep", ambush=False))
    wait(lambda: pd.run("camp.state").get("camp") is None and pd.run("camp.state")["view"] == "hidden", 30)
    time.sleep(3)
    prompts()


def do_ambush():
    prompts()
    camp_up()
    if fight(lambda: note("sleep:", pd.run("camp.sleep", ambush=True, surprised=False)), win, "ambush"):
        state("ambush_after")
        prompts()


def do_boss():
    prompts()
    if fight(lambda: note("boss:", pd.run("estate.boss", dungeon="crypts", boss="librarian", tier="apprentice")), win, "boss"):
        prompts()


def results_home(name):
    if not wait(lambda: pd.run("results.state").get("open"), 40):
        note(name + ": no results screen; view", pd.run("dungeon.state")["view"])
    time.sleep(3)
    for _ in range(8):
        r = pd.run("results.state")
        if not (isinstance(r, dict) and r.get("open")):
            break
        note("results:", pd.run("results.next")["did"])
        time.sleep(2.5)
    wait(lambda: pd.run("dungeon.state")["view"] == "Hamlet" and in_hub(), 40)
    time.sleep(6)


def do_retreat():
    prompts()
    note("leave:", pd.run("dungeon.leave"))
    time.sleep(1.5)
    s = pd.run("dungeon.state")
    if s.get("prompt"):
        prompts()
    results_home("retreat")


def embark():
    quests = pd.run("quests.list")
    index = next((i for i, q in enumerate(quests) if not q.get("Boss")), 0)
    note("embark:", pd.run("quests.embark", index=index, free=True))
    wait(lambda: pd.run("dungeon.state")["view"] == "Dungeon" and in_hub() and pd.run("dungeon.state")["room"] is not None, 60)
    time.sleep(6)
    prompts()


def do_lost():
    embark()
    # (as a night's ambush: a fight called that way ends the expedition when nobody is left, as a room's fight does;
    # one called as a hallway's is nothing to the exploration, and the party would be left standing dead)
    if fight(lambda: note(pd.run("fightbag.fight", ambush=True)), die, "lost"):
        state("lost_after")
    elif pd.mode() == "COMBAT":
        note("lost: the fight did not end with the party dead; it is fled")
        flee()
        wait(in_hub, 60)
    results_home("lost")


def do_again():
    """After a party was lost: the next party's fight must still come to its end."""
    party_now = party()
    if len(party_now) < 4:
        heroes = [h["guid"] for h in pd.run("estate.state")["heroes"] if not h["party"]]
        for guid in heroes[:4 - len(party_now)]:
            pd.run("estate.party", guid=guid)
        note("party:", party())
    embark()
    if fight(lambda: note(pd.run("fightbag.fight")), win, "again"):
        state("again_after")
        prompts()
    note("leave:", pd.run("dungeon.leave"))
    time.sleep(1.5)
    results_home("again")


def do_leave():
    pd.run("estate.exit")
    wait(lambda: pd.mode() == "MAIN_MENU" and pd.settled(), 60)
    time.sleep(5)


ROUTE = {
    "enter": ("into the Estate", do_enter), "setout": ("setting out", do_setout), "hallway": ("hallway fight won", do_hallway),
    "room": ("room fight won", do_room), "flee": ("fight fled", do_flee), "camp": ("camp, a quiet night", do_camp),
    "ambush": ("camp, night ambush won", do_ambush), "boss": ("boss fight won", do_boss), "retreat": ("retreat, results, home", do_retreat),
    "lost": ("fight lost, results, home", do_lost), "again": ("after the loss, a fight won", do_again),
    "leave": ("leaving the Estate", do_leave),
}


# ---- the sheets -------------------------------------------------------------------------------------------------

def coach_at(changes, frame):
    """The scene watch's last word at or before a frame."""
    last = None
    for c in changes:
        if c["frame"] > frame:
            break
        last = c
    return last


def tile(folder, f, w, h):
    try:
        im = Image.open(os.path.join(folder, "film", "%07d.jpg" % f["frame"])).convert("RGB")
    except OSError:
        return None
    if f.get("flipped"):
        im = im.transpose(Image.FLIP_TOP_BOTTOM)
    return im.resize((w, h))


def sheet(folder, picked, changes, out, columns=8, w=300, h=169):
    if not picked:
        return
    rows = (len(picked) + columns - 1) // columns
    page = Image.new("RGB", (columns * w, rows * (h + 14)), (24, 24, 24))
    draw = ImageDraw.Draw(page)
    for i, f in enumerate(picked):
        im = tile(folder, f, w, h)
        x, y = (i % columns) * w, (i // columns) * (h + 14)
        if im is not None:
            page.paste(im, (x, y))
        c = coach_at(changes, f["frame"])
        coach = bool(c and c["coach"] > 0)
        where = f["where"].replace(" (estate)", "").replace("fader ", "/ ")
        draw.text((x + 3, y + h + 1), "%d %s%s" % (f["frame"], where, "  COACH" if coach else ""), fill=(255, 110, 110) if coach else (200, 200, 200))
        if coach:
            draw.rectangle((x, y, x + w - 1, y + h - 1), outline=(255, 60, 60), width=3)
    page.save(out)


def sheets(folder, legs, changes, film):
    bounds = [frame for _, frame in legs] + [10 ** 9]
    summary = []
    coach_frames = []
    for n, (name, start) in enumerate(legs):
        frames = [f for f in film if start <= f["frame"] < bounds[n + 1]]
        if not frames:
            continue
        seen = [f for f in frames if (coach_at(changes, f["frame"]) or {}).get("coach", 0) > 0 and "fader black" not in f["where"]]
        coach_frames += [(name, f) for f in seen]
        modes = sorted(set(f["where"].split(" ")[0] for f in seen))
        summary.append((name, len(frames), len(seen), modes))
        fading = [i for i, f in enumerate(frames) if "to black" in f["where"] or "to clear" in f["where"]]
        wanted = set()
        for i in fading[::3]:
            wanted.add(i)
        wanted.update(int(k * (len(frames) - 1) / 23.0) for k in range(24))
        first = next((i for i, f in enumerate(frames) if f in seen), None)
        if first is not None:
            wanted.update(range(max(0, first - 2), min(len(frames), first + 6)))
        picked = [frames[i] for i in sorted(wanted)]
        if len(picked) > 56:
            picked = [picked[int(k * (len(picked) - 1) / 55.0)] for k in range(56)]
        sheet(folder, picked, changes, os.path.join(folder, "%02d %s.png" % (n, name)))
    if coach_frames:
        per = {}
        for name, f in coach_frames:
            per.setdefault(name, []).append(f)
        picked = []
        for name in per:
            fs = per[name]
            picked += [fs[int(k * (len(fs) - 1) / 7.0)] for k in range(min(8, len(fs)))] if len(fs) > 8 else fs
        sheet(folder, picked, changes, os.path.join(folder, "coach.png"))
    return summary


def main(folder, wanted, settings):
    global FOLDER
    FOLDER = os.path.abspath(folder)
    os.makedirs(FOLDER, exist_ok=True)
    if settings:
        scenes = pd.run("scenes.state", **settings)
        print("scenes:", {k: scenes[k] for k in ("afterFight", "showCoach")}, flush=True)
    pd.run("estate.skytrap", on=True, folder=FOLDER.replace("\\", "/"), film=FILM_EVERY)
    pd.run("estate.scenewatch", on=True, every=2)
    t0 = time.time()
    try:
        for key in ALL:
            if key not in wanted:
                continue
            name, do = ROUTE[key]
            leg(name)
            try:
                do()
            except Exception as e:      # a leg that fails does not end the trip
                note(name + " FAILED:", repr(e))
        leg("end")
    finally:
        film = pd.run("estate.skytrap", on=False)["film"]
        watch = pd.run("estate.scenewatch", on=False)
    legs = LEGS[:-1]
    with open(os.path.join(FOLDER, "watch.json"), "w", encoding="utf-8") as f:
        json.dump({"legs": LEGS, "notes": NOTES, "watch": watch, "film": film}, f, indent=1)
    summary = sheets(FOLDER, legs, watch["changes"], film)
    print("%.0f s; frames %d, looked at %d, filmed %d" % (time.time() - t0, watch["frames"], watch["looked"], len(film)))
    print("a coach in view, by mode and fader (frames looked at):")
    for where, count in sorted(watch["coachFrames"].items()):
        print("   %5d  %s" % (count, where))
    print("by leg (filmed frames with a coach in view / filmed frames):")
    for name, total, seen, modes in summary:
        print("   %-28s %4d / %4d  %s" % (name, seen, total, ", ".join(modes)))
    print("sheets in", FOLDER)
    return 0


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(2)
    options = dict(a.split("=", 1) for a in sys.argv[2:] if "=" in a)
    legs = [a for a in sys.argv[2:] if "=" not in a]
    sys.exit(main(sys.argv[1], legs[0].split(",") if legs else ALL, dict((k, bridge.coerce(v)) for k, v in options.items())))
