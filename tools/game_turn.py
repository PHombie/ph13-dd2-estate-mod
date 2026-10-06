#!/usr/bin/env python3
"""One game, many workers, and the owner who plays it: who may build into the game's folder and run DD2 right now.

    python tools/game_turn.py take <name> [seconds]   wait for the turn (default 540 s); exit 0 when it is yours
    python tools/game_turn.py beat <name>             still at it: the turn lasts HOLD minutes more from now
    python tools/game_turn.py give <name>             done: the game is closed and the owner's estate put back
    python tools/game_turn.py who                     who holds it, who waits, whether the owner is in the game
    python tools/game_turn.py restore                 nobody holds the turn but the test estate is still in the
                                                      slot (a turn that died): put the owner's estate back

The turn is a file in the MAIN checkout's _lab/game_turn (the same for every worktree). Those who wait stand in
a line in the order they first asked; a waiter who has not asked again for WAIT_FRESH minutes loses the place.
A turn nobody gave back ends by itself HOLD minutes after it was taken or last beaten.

THE OWNER'S GAME MAY BE CLOSED ONCE WORK HAS BEGUN (their word, 2026-10-06: "if you have begun work, you may
close it"): taking a turn closes whatever game is up, the owner's too. What is never lost is their estate: it is
kept before the test estate goes in and put back when the turn is given. `who` still tells whose game is up.

The save is one for everybody, so the turn swaps it: taking a turn keeps the owner's estate as it stands
(_lab/saves/owner_latest) and puts the test estate (test_pristine) in the slot; giving it back closes the game and
puts the owner's estate back. A worker neither copies nor restores saves by hand.
"""
import datetime
import json
import os
import subprocess
import sys
import time

MAIN = r"D:\Mods\DD2\dd2-estate-mod"
DIR = os.path.join(MAIN, "_lab", "game_turn")
HOLDER = os.path.join(DIR, "holder.json")
LINE = os.path.join(DIR, "line")
TURNS = os.path.join(DIR, "turns.json")           # when turns were held: a game started inside one is a worker's
SWAPPED = os.path.join(DIR, "slot_is_test")       # the test estate is in the slot; the owner's is in OWNER
OWNER, TEST = "owner_latest", "test_pristine"
HOLD = 20 * 60          # a turn nobody gives back or beats ends after this
WAIT_FRESH = 12 * 60    # a waiter must ask again within this to keep the place
EXE = "Darkest Dungeon II.exe"


def read(path):
    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return None


def write(path, value):
    with open(path, "w", encoding="utf-8") as f:
        json.dump(value, f)


# ---- the game's process -------------------------------------------------------------------------------------

def pids():
    out = subprocess.run(["tasklist", "/FI", "IMAGENAME eq " + EXE, "/FO", "CSV", "/NH"], capture_output=True, text=True).stdout
    return [int(l.split('","')[1]) for l in out.splitlines() if l.startswith('"%s"' % EXE)]


def started():
    """When the running game was started (seconds since the epoch), None if that cannot be told."""
    command = "(Get-Process -Name 'Darkest Dungeon II' -ErrorAction SilentlyContinue | Sort-Object StartTime | Select-Object -First 1).StartTime.ToUniversalTime().ToString('o')"
    try:
        text = subprocess.run(["powershell", "-NoProfile", "-Command", command], capture_output=True, text=True, timeout=30).stdout.strip()
        if not text:
            return None
        stamp = datetime.datetime.fromisoformat(text[:26].rstrip("Z")).replace(tzinfo=datetime.timezone.utc)
        return stamp.timestamp()
    except Exception:
        return None


def owners_game():
    """True when the game is up and was not started inside a worker's turn: the owner is in it."""
    if not pids():
        return False
    at = started()
    if at is None:
        return True                                   # cannot tell: leave it alone
    for turn in read(TURNS) or []:
        if turn["taken"] - 5 <= at <= (turn.get("end") or time.time()) + 5:
            return False
    return True


def close_game():
    for pid in pids():
        subprocess.run(["taskkill", "/PID", str(pid), "/F"], capture_output=True)
    for _ in range(30):
        if not pids():
            return True
        time.sleep(0.5)
    return False


# ---- the save -----------------------------------------------------------------------------------------------

def save_tool(*args):
    r = subprocess.run([sys.executable, os.path.join(MAIN, "tools", "estate_pristine.py")] + list(args), capture_output=True, text=True)
    words = (r.stdout + r.stderr).strip().splitlines()
    print("   save:", words[-1] if words else "(nothing said)")
    return r.returncode == 0


def test_estate_in():
    """The owner's estate is kept (once: a turn that died left it kept) and the test estate put in the slot."""
    if not os.path.exists(SWAPPED):
        if not save_tool("keep", OWNER):
            return False
        write(SWAPPED, {"at": time.time()})
    return save_tool("back", TEST)


def owners_estate_back():
    if not os.path.exists(SWAPPED):
        return True
    if not save_tool("back", OWNER):
        print("   THE OWNER'S ESTATE IS NOT BACK: the test estate is still the slot's newest. Tell the coordinator.")
        return False
    os.remove(SWAPPED)
    return True


# ---- the turn -----------------------------------------------------------------------------------------------

def end_turn(h, at):
    turns = read(TURNS) or []
    for turn in turns:
        if turn["name"] == h["name"] and turn["taken"] == h["taken"]:
            turn["end"] = at
    write(TURNS, turns[-60:])


def holder():
    h = read(HOLDER)
    if h and time.time() - h.get("beat", 0) > HOLD:
        end_turn(h, h["beat"] + HOLD)
        try:
            os.remove(HOLDER)
        except OSError:
            pass
        return None
    return h


def line():
    out = []
    for name in os.listdir(LINE):
        entry = read(os.path.join(LINE, name))
        if not entry or time.time() - entry.get("seen", 0) > WAIT_FRESH:
            try:
                os.remove(os.path.join(LINE, name))
            except OSError:
                pass
            continue
        out.append(entry)
    return sorted(out, key=lambda e: e["asked"])


def stand(name):
    path = os.path.join(LINE, name + ".json")
    entry = read(path) or {"name": name, "asked": time.time()}
    entry["seen"] = time.time()
    write(path, entry)


def take(name, seconds):
    deadline = time.time() + seconds
    owner = False
    while True:
        h = holder()
        if h and h["name"] == name:
            beat(name)
            print("yours (already)")
            return 0
        stand(name)
        waiting = line()
        if h is None and waiting and waiting[0]["name"] == name:
            owner = False                             # the owner's game is closed like any other (their word)
            if not owner:
                try:
                    fd = os.open(HOLDER, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
                except FileExistsError:
                    fd = None
                if fd is not None:
                    now = time.time()
                    with os.fdopen(fd, "w", encoding="utf-8") as f:
                        json.dump({"name": name, "taken": now, "beat": now}, f)
                    write(TURNS, ((read(TURNS) or []) + [{"name": name, "taken": now, "end": None}])[-60:])
                    try:
                        os.remove(os.path.join(LINE, name + ".json"))
                    except OSError:
                        pass
                    close_game()                      # whatever game is up: a turn's leftover or the owner's
                    if not test_estate_in():
                        end_turn(read(HOLDER), time.time())
                        os.remove(HOLDER)
                        print("no turn: the save could not be swapped (see above). Go on without the game and tell the coordinator.")
                        return 1
                    print("yours: the test estate is in the slot. At most 15 minutes, then: python tools/game_turn.py give %s" % name)
                    return 0
        if time.time() >= deadline:
            if owner:
                print("not yet: THE OWNER IS IN THE GAME. No turn while their game is up; do not close it. Go on with work that needs no game and ask again in 10 minutes.")
            else:
                names = [e["name"] for e in waiting]
                ahead = names[:names.index(name)] if name in names else []
                print("not yet: held by %s; ahead of you: %s. Go on with other work and ask again within 10 minutes." % (h["name"] if h else "nobody", ", ".join(ahead) or "nobody"))
            return 1
        time.sleep(5)


def beat(name):
    h = holder()
    if not h or h["name"] != name:
        print("not yours")
        return 1
    h["beat"] = time.time()
    write(HOLDER, h)
    return 0


def give(name):
    h = read(HOLDER)
    if h and h["name"] == name:
        close_game()
        owners_estate_back()
        end_turn(h, time.time())
        os.remove(HOLDER)
        print("given back")
    else:
        print("was not yours (held by %s)" % (h["name"] if h else "nobody"))
    try:
        os.remove(os.path.join(LINE, name + ".json"))
    except OSError:
        pass
    return 0


def restore():
    if holder():
        print("a turn is held: its `give` puts the owner's estate back")
        return 1
    close_game()
    return 0 if owners_estate_back() else 1


def who():
    h = holder()
    print("holder:", "%s for %d s" % (h["name"], time.time() - h["taken"]) if h else "nobody")
    print("line:", ", ".join("%s (%d s)" % (e["name"], time.time() - e["asked"]) for e in line()) or "empty")
    print("game:", ("the OWNER's, up" if owners_game() else "a worker's, up") if pids() else "closed")
    print("slot:", "the test estate (the owner's is kept as %s)" % OWNER if os.path.exists(SWAPPED) else "the owner's estate")
    return 0


if __name__ == "__main__":
    os.makedirs(LINE, exist_ok=True)
    args = sys.argv[1:]
    if len(args) >= 2 and args[0] == "take":
        sys.exit(take(args[1], int(args[2]) if len(args) > 2 else 540))
    if len(args) == 2 and args[0] == "beat":
        sys.exit(beat(args[1]))
    if len(args) == 2 and args[0] == "give":
        sys.exit(give(args[1]))
    if args[:1] == ["restore"]:
        sys.exit(restore())
    if args[:1] == ["who"]:
        sys.exit(who())
    print(__doc__)
    sys.exit(2)
