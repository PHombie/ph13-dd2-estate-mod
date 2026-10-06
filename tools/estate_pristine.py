#!/usr/bin/env python3
"""Keeps a copy of the estate on disk and puts it back: for a test estate that should start the same every time.
Nothing is deleted.

    python tools/estate_pristine.py keep [name]      copy the estate on disk to _lab/saves/<name> (default test_pristine);
                                                     the game closed
    python tools/estate_pristine.py back [name]      the estate is that copy again
    python tools/estate_pristine.py check [name]     is it? (the newest snapshot on disk against the copy's newest)

"back" does not take the newer snapshots away: Steam's cloud brings back, at the game's next start, save files that
went missing while the game was closed (seen in its cloud_log.txt: "Need to download file ... Save_0005_Estate"), and
the estate was then the old one again. The copy's newest snapshot is added to the slot as its newest instead, under
the next number; the mod continues from the newest snapshot, and the game trims the old ones itself. This works with
the game closed or at its main menu (the Estate not entered). Where the copy is of another slot than the one on disk,
or there is none on disk, the whole copy is put there as before (the game closed; what was there goes to
_lab/saves/used_<time>).
"""
import os
import re
import shutil
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import estate_slot as slot  # noqa: E402


def snapshots(slot_dir):
    return sorted(s for s in os.listdir(slot_dir) if re.match(r"Save_\d{4}_", s) and os.path.isdir(os.path.join(slot_dir, s)))


def in_session():
    """True when the game is up with the Estate entered (its files are in use then)."""
    if not slot.running():
        return False
    try:
        import bridge
        r = bridge.send({"cmd": "get", "expr": "DD2Estate.Dd2.EstateSession.Active"})
        s = bridge.send({"cmd": "get", "expr": "DD2Estate.Dd2.EstateSession.Starting"})
        return not (r.get("ok") and r.get("result") is False and s.get("ok") and s.get("result") is False)
    except OSError:
        return True


def keep(name):
    number = slot.profile_number()
    folder = slot.profile_dir(number)
    if slot.running():
        raise SystemExit("close the game first")
    store = os.path.join(slot.STORE, name)
    slot.settle()
    if os.path.isdir(store):
        os.rename(store, os.path.join(slot.STORE, name + "_before_" + time.strftime("%y%m%d_%H%M%S")))
    os.makedirs(store)
    for entry in slot.entries(folder, number):
        slot.copy(os.path.join(folder, entry), os.path.join(store, entry))
    print("kept:", slot.describe(store, number))


def back(name):
    number = slot.profile_number()
    folder = slot.profile_dir(number)
    store = os.path.join(slot.STORE, name)
    if not os.path.isdir(store):
        raise SystemExit("no copy named " + name)
    if in_session():
        raise SystemExit("leave the Estate first (the main menu, or the game closed)")
    kingdoms = "mod_profile_%d_kingdoms" % number
    kept, here = slot.slots(store, number), slot.slots(folder, number)
    if len(kept) == 1 and kept == here:
        source = os.path.join(store, kingdoms, kept[0])
        target = os.path.join(folder, kingdoms, here[0])
        newest = snapshots(source)[-1]
        present = snapshots(target)
        following = (int(present[-1][5:9]) if present else 0) + 1
        added = "Save_%04d%s" % (following, newest[9:])
        slot.copy(os.path.join(source, newest), os.path.join(target, added))
        print("back: the copy's %s is in the slot as %s, its newest: %s" % (newest, added, slot.describe(folder, number)))
        return
    if slot.running():
        raise SystemExit("the copy is of another slot than the one on disk: close the game first")
    slot.settle()
    used = os.path.join(slot.STORE, "used_" + time.strftime("%y%m%d_%H%M%S"))
    os.makedirs(used)
    for entry in slot.entries(folder, number):
        shutil.move(os.path.join(folder, entry), os.path.join(used, entry))
    for entry in os.listdir(store):
        slot.copy(os.path.join(store, entry), os.path.join(folder, entry))
    print("back:", slot.describe(folder, number))
    print("after the game's next start, 'check' tells whether Steam's cloud left it so")


def newest_files(folder, number):
    """The files of the newest snapshot of the only slot, by name, with their hashes."""
    import hashlib
    slots = slot.slots(folder, number)
    if len(slots) != 1:
        return None, {}
    slot_dir = os.path.join(folder, "mod_profile_%d_kingdoms" % number, slots[0])
    names = snapshots(slot_dir)
    if not names:
        return None, {}
    out = {}
    for base, _, files in os.walk(os.path.join(slot_dir, names[-1])):
        for f in files:
            with open(os.path.join(base, f), "rb") as handle:
                out[f] = hashlib.sha1(handle.read()).hexdigest()
    return slots[0] + "/" + names[-1], out


def check(name):
    number = slot.profile_number()
    folder = slot.profile_dir(number)
    store = os.path.join(slot.STORE, name)
    if not os.path.isdir(store):
        raise SystemExit("no copy named " + name)
    here, here_files = newest_files(folder, number)
    kept, kept_files = newest_files(store, number)
    same = bool(here_files) and here_files == kept_files
    print("the estate on disk (%s) is %sthe copy '%s' (%s)" % (here, "" if same else "NOT ", name, kept))
    return same


if __name__ == "__main__":
    command = sys.argv[1] if len(sys.argv) > 1 else ""
    which = sys.argv[2] if len(sys.argv) > 2 else "test_pristine"
    if command == "keep":
        keep(which)
    elif command == "back":
        back(which)
    elif command == "check":
        sys.exit(0 if check(which) else 1)
    else:
        print(__doc__)
        sys.exit(2)
