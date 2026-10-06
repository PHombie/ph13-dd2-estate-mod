#!/usr/bin/env python3
"""Puts the player's own estate aside and brings it back: the mod keeps one estate, and a test estate must not
cost the real one. The game has to be closed.

    python tools/estate_slot.py status     what is in the Estate profile and what is put aside
    python tools/estate_slot.py park       the estate goes to _lab/saves/main; the next "Estate" founds a new one
    python tools/estate_slot.py restore    the estate on disk (the test one) goes to _lab/saves/test_<time>,
                                           the one put aside comes back

"park" refuses when an estate is already put aside; "restore" refuses when none is.

Steam keeps the game's saves in its cloud and syncs them when the game closes and when it starts. A save file that is
gone when it syncs, and whose removal it did not watch (the game was closed), it may bring back from the cloud; and
files put there with their old dates make it distrust its own record and take the cloud's side. So: files are swapped
only after the sync that follows the game's exit is over (settle()), copies get the date of the swap (copy()), and
after the next start of the game "status" (or estate_pristine.py check) tells whether what was put there is still it.
"""
import glob
import os
import re
import shutil
import subprocess
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
STORE = os.path.join(ROOT, "_lab", "saves")
MAIN = os.path.join(STORE, "main")
CONFIG = r"E:\Steam\steamapps\common\Darkest Dungeon® II\BepInEx\config\ph13.dd2.estate.cfg"
SAVES = os.path.expandvars(r"%USERPROFILE%\AppData\LocalLow\RedHook\Darkest Dungeon II\SaveFiles")
EXE = "Darkest Dungeon II.exe"
STEAM_CLOUD_LOG = r"E:\Steam\logs\cloud_log.txt"


def settle(quiet=4.0, limit=60.0):
    """Waits until Steam's cloud log has been quiet for a few seconds: its sync at the game's exit is over."""
    if not os.path.isfile(STEAM_CLOUD_LOG):
        return
    start = time.time()
    while time.time() - start < limit:
        if time.time() - os.stat(STEAM_CLOUD_LOG).st_mtime >= quiet:
            return
        time.sleep(0.5)
    print("Steam's cloud log did not go quiet in %d s; going on" % limit)


def copy(src, dst):
    """A copy dated now: to Steam's cloud a file changed here, not an old one that turned up."""
    if os.path.isdir(src):
        shutil.copytree(src, dst, copy_function=shutil.copyfile)
    else:
        shutil.copyfile(src, dst)


def running():
    out = subprocess.run(["tasklist", "/FI", "IMAGENAME eq " + EXE, "/FO", "CSV", "/NH"], capture_output=True, text=True).stdout
    return EXE in out


def profile_number():
    try:
        text = open(CONFIG, encoding="utf-8", errors="ignore").read()
        found = re.search(r"^EstateProfileGuid\s*=\s*(\d+)", text, re.M)
        if found and int(found.group(1)) > 0:
            return int(found.group(1))
    except OSError:
        pass
    return 23


def profile_dir(number):
    dirs = glob.glob(os.path.join(SAVES, "*", "profiles", "mod_profile_%d_kingdoms" % number))
    if len(dirs) != 1:
        raise SystemExit("expected one Estate profile, found %d: %s" % (len(dirs), dirs))
    return os.path.dirname(dirs[0])


def entries(folder, number):
    """The profile's own files and its kingdoms folder (whose slots are the estates)."""
    return sorted(e for e in os.listdir(folder) if re.search(r"(^|_)%d(\.|_|$)" % number, e))


def slots(folder, number):
    kingdoms = os.path.join(folder, "mod_profile_%d_kingdoms" % number)
    return sorted(s for s in os.listdir(kingdoms) if os.path.isdir(os.path.join(kingdoms, s))) if os.path.isdir(kingdoms) else []


def describe(folder, number):
    kingdoms = os.path.join(folder, "mod_profile_%d_kingdoms" % number)
    out = []
    for slot in slots(folder, number):
        saves = sorted(s for s in os.listdir(os.path.join(kingdoms, slot)) if s.startswith("Save_"))
        out.append("%s (%d snapshots, last %s)" % (slot, len(saves), saves[-1] if saves else "none"))
    return out


def status():
    number = profile_number()
    folder = profile_dir(number)
    print("profile #%d in %s" % (number, folder))
    print("  estate on disk:", describe(folder, number) or "none (the next 'Estate' founds a new one)")
    print("  put aside:", describe(MAIN, number) if os.path.isdir(MAIN) else "nothing")
    print("  game running:", running())


def park():
    number = profile_number()
    folder = profile_dir(number)
    if os.path.isdir(MAIN):
        raise SystemExit("an estate is already put aside in " + MAIN + ": restore it first")
    if running():
        raise SystemExit("close the game first")
    if not slots(folder, number):
        raise SystemExit("there is no estate to put aside")
    settle()
    os.makedirs(MAIN)
    for entry in entries(folder, number):
        src = os.path.join(folder, entry)
        (shutil.copytree if os.path.isdir(src) else shutil.copy2)(src, os.path.join(MAIN, entry))
    # only the estate's slots leave the profile: the profile itself stays the mod's
    kingdoms = os.path.join(folder, "mod_profile_%d_kingdoms" % number)
    for slot in slots(folder, number):
        shutil.rmtree(os.path.join(kingdoms, slot))
    print("put aside:", describe(MAIN, number))


def restore():
    number = profile_number()
    folder = profile_dir(number)
    if not os.path.isdir(MAIN):
        raise SystemExit("nothing is put aside")
    if running():
        raise SystemExit("close the game first")
    settle()
    kept = os.path.join(STORE, "test_" + time.strftime("%y%m%d_%H%M%S"))
    os.makedirs(kept)
    for entry in entries(folder, number):
        shutil.move(os.path.join(folder, entry), os.path.join(kept, entry))
    for entry in os.listdir(MAIN):
        copy(os.path.join(MAIN, entry), os.path.join(folder, entry))
    back = describe(folder, number)
    # the copy put aside is kept under another name, in case this was a mistake
    os.rename(MAIN, os.path.join(STORE, "main_restored_" + time.strftime("%y%m%d_%H%M%S")))
    print("back:", back)
    print("the test estate is kept in", kept)
    print("after the next start of the game, 'status' must show this estate alone (Steam's cloud may bring the other back)")


if __name__ == "__main__":
    command = sys.argv[1] if len(sys.argv) > 1 else "status"
    if command == "status":
        status()
    elif command == "park":
        park()
    elif command == "restore":
        restore()
    else:
        print(__doc__)
        sys.exit(2)
