#!/usr/bin/env python3
"""Watches every frame of a trip through the Estate's loadings for the blue of a sky where there is none (the game's
fog laid over an empty view, see EstateSky): into the Estate, out to the main menu and in again, from the hamlet into
a dungeon, into a room's fight and out of it, out to the main menu from the dungeon and in again, and under the pause
menu on the way. The frames are looked at inside the game (the dev command estate.skytrap), every one of them; every
4th is kept small, and the ones around each fade are put on a sheet per leg.

    python -u tools/sky_watch.py          (the game at its main menu; best on a copy of the estate)

It ends with the count of sky-like frames by the game's mode: any outside the main menu (which has a sky) and a fight
(whose arena may) is a fault.
"""
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bridge  # noqa: E402
import dev  # noqa: E402
import dd1_drive as drive  # noqa: E402
import play_dungeon as pd  # noqa: E402

from PIL import Image, ImageDraw  # noqa: E402

FOLDER = os.path.join(dev.SHOTS, "sky_" + time.strftime("%H%M%S"))
LEGS = []          # (name, the game's frame number it began at)


def trap(**kw):
    return pd.run("estate.skytrap", **kw)


def leg(name):
    LEGS.append((name, trap()["now"]))
    print("..", name, flush=True)


def wait_mode(mode, seconds=60):
    t0 = time.time()
    while time.time() - t0 < seconds and pd.mode() != mode:
        time.sleep(0.3)
    return pd.mode() == mode


def hush():
    bridge.send({"cmd": "invoke", "expr": "DD2Estate.Estate.NarrationBox", "method": "Hide"})


def esc():
    drive.drive(["focus", "scanmode on", "key 0x1B"], proc=drive.DD2)


def top_screen():
    r = bridge.send({"cmd": "get", "expr": "$ScreenStackBhv.GetTopMostScreenInstance()"})
    return str(r.get("result")) if r.get("ok") and r.get("result") is not None else ""


def paused():
    return "PauseMenu" in top_screen()


def pause(name):
    """The pause menu up for two seconds, and down again."""
    leg(name)
    before = top_screen()
    if before:
        print("   a screen is up already:", before[:140])
    esc()
    time.sleep(0.6)
    if not paused():
        print("   the pause menu did not come up; on top:", top_screen()[:140] or "nothing")
    time.sleep(1.8)
    for _ in range(3):
        if not paused():
            break
        esc()
        time.sleep(0.8)
    if paused():
        print("   the pause menu is still up")


def route():
    leg("into the Estate")
    pd.no_key_at_loading()
    pd.run("estate.enter")
    wait_mode("ESTATE")
    time.sleep(6)
    hush()
    pause("pause in the hamlet")

    leg("hamlet to the main menu")
    pd.run("estate.exit")
    wait_mode("MAIN_MENU")
    time.sleep(6)
    leg("into the Estate again")
    pd.run("estate.enter")
    wait_mode("ESTATE")
    time.sleep(6)
    hush()

    leg("hamlet to the dungeon")
    quests = pd.run("quests.list")
    index = next(i for i, q in enumerate(quests) if q["Dungeon"] == "warrens")
    pd.run("quests.open")
    pd.run("quests.select", index=index)
    for _ in range(2):
        pd.run("quests.forward")
        time.sleep(1.5)
        p = pd.run("provision.state")
        if isinstance(p, dict) and p.get("open"):
            break
    pd.run("provision.kit")
    pd.run("provision.setout")
    time.sleep(12)
    hush()
    pause("pause in the room")

    leg("room to the hallway")
    s = pd.run("dungeon.state")
    pd.run("dungeon.goto", room=s["reachable"][0])
    time.sleep(5)
    pause("pause in the hallway")

    leg("hallway to the room and its fight")
    t0 = time.time()
    while time.time() - t0 < 90 and pd.mode() == "ESTATE":
        state = pd.run("dungeon.state")
        if state.get("prompt"):
            # a curio on the way: left alone (the last answer)
            pd.run("dungeon.answer", option=max(0, len(state.get("options") or [1]) - 1))
            time.sleep(1.0)
            continue
        # DD1's way: the party walks up to the door at the hallway's end and uses it (W, or a click on it)
        pd.run("corridor.door")
        time.sleep(1.0)
    time.sleep(9)
    fought = pd.mode() != "ESTATE"
    if not fought:
        print("no fight was reached:", {k: v for k, v in pd.run("dungeon.state").items() if k in ("busy", "room", "hallway", "pending", "prompt", "leadX")}, "on top:", top_screen()[:140] or "nothing")
    else:
        pause("pause in the fight")
        leg("fight to its results")
        pd.call({"cmd": "invoke", "expr": "$CombatBhv", "method": "ForceEndCombat", "args": [True]})
        time.sleep(8)
        leg("results to the dungeon")
        try:
            pd.call({"cmd": "click", "path": pd.TAKE_ALL})
        except RuntimeError:
            pass
        wait_mode("ESTATE")
        time.sleep(8)
        hush()

    leg("dungeon to the main menu")
    pd.run("estate.exit")
    wait_mode("MAIN_MENU")
    time.sleep(6)
    leg("into the Estate, into the dungeon")
    pd.run("estate.enter")
    wait_mode("ESTATE")
    time.sleep(10)
    leg("end")


def sheets(result):
    """Per leg, the kept frames around its fades (and a few of the rest) on one sheet."""
    film = result["film"]
    bounds = [frame for _, frame in LEGS]
    for n, (name, start) in enumerate(LEGS[:-1]):
        frames = [f for f in film if start <= f["frame"] < bounds[n + 1]]
        if not frames:
            continue
        fading = [i for i, f in enumerate(frames) if "to black" in f["where"] or "to clear" in f["where"]]
        wanted = set()
        for i in fading:
            wanted.update(range(max(0, i - 2), min(len(frames), i + 3)))
        wanted.update(int(k * (len(frames) - 1) / 5.0) for k in range(6))
        picked = [frames[i] for i in sorted(wanted)]
        if len(picked) > 48:
            picked = [picked[int(k * (len(picked) - 1) / 47.0)] for k in range(48)]
        columns, w, h = 8, 320, 180
        rows = (len(picked) + columns - 1) // columns
        sheet = Image.new("RGB", (columns * w, rows * (h + 14)), (40, 0, 0))
        draw = ImageDraw.Draw(sheet)
        for i, f in enumerate(picked):
            path = os.path.join(FOLDER, "film", "%07d.jpg" % f["frame"])
            try:
                im = Image.open(path).convert("RGB")
            except OSError:
                continue
            if f.get("flipped"):
                im = im.transpose(Image.FLIP_TOP_BOTTOM)
            x, y = (i % columns) * w, (i // columns) * (h + 14)
            sheet.paste(im.resize((w, h)), (x, y))
            where = f["where"].replace(" (estate)", "").replace("fader ", "/ ")
            draw.text((x + 3, y + h + 1), "%d %s%s" % (f["frame"], where, "  SKY" if f["share"] >= 0.15 else ""), fill=(255, 120, 120) if f["share"] >= 0.15 else (200, 200, 200))
        out = os.path.join(FOLDER, "%02d %s.png" % (n, name))
        sheet.save(out)


def main():
    os.makedirs(FOLDER, exist_ok=True)
    trap(on=True, folder=FOLDER.replace("\\", "/"), film=4)
    try:
        route()
    finally:
        result = trap(on=False)
    sheets(result)
    print("frames %d, sky-like %d" % (result["frames"], result["sky"]))
    faults = 0
    for where, count in sorted(result["byMode"].items()):
        own = where.startswith("MAIN_MENU") or where.startswith("COMBAT") or where.startswith("RESULTS")
        faults += 0 if own else count
        print("   %5d  %s%s" % (count, where, "" if own else "   <-- a sky where there is none"))
    for c in result["caught"]:
        if not c["mode"].startswith("MAIN_MENU"):
            print("   caught: frame %d, mode %s, fader %s, scenes %s, fog pass %s, %s" % (c["frame"], c["mode"], c["fader"], c["scenes"], c["fog"], c["file"]))
    print("sheets in", FOLDER)
    print("FAULTS: %d" % faults if faults else "no sky where there is none")
    return 1 if faults else 0


if __name__ == "__main__":
    sys.exit(main())
