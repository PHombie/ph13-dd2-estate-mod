#!/usr/bin/env python3
"""Plays the current expedition like tools/play_dungeon.py, but fights are fought for real by tools/auto_fight.py
(no forced wins): a first measure of how hard the quest is. Prints every fight's cost to the party.
A won fight's loot is on DD1's "Victory!" scroll when the corridor is back; play_dungeon's main loop takes it.

    python -u tools/real_play.py [max_steps]
"""
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import auto_fight as af  # noqa: E402
import play_dungeon as pd  # noqa: E402

FIGHTS = []


def party():
    return [(h["cls"], h["hp"], h["stress"]) for h in pd.run("estate.state")["heroes"] if h["party"]]


def real_fight():
    if not pd.wait_mode("COMBAT", 40):
        raise RuntimeError("no fight started")
    time.sleep(6)
    before = party()
    foes = af.enemies()
    start = time.time()
    af.main(480)
    if not pd.wait_mode("ESTATE", 90):
        raise RuntimeError("did not return to the estate")
    time.sleep(2)
    after = party()
    FIGHTS.append(dict(enemies=len(foes), hp=sum(e["maxHp"] for e in foes), seconds=round(time.time() - start), before=before, after=after))
    print("FIGHT %d: %d enemies with %d hp, %ds\n   before %s\n   after  %s" % (len(FIGHTS), len(foes), sum(e["maxHp"] for e in foes), time.time() - start, before, after), flush=True)


def main(max_steps):
    pd.win_fight = real_fight
    try:
        code = pd.main(max_steps)
    except Exception as e:  # a lost party ends the expedition under the script's feet
        print("stopped:", str(e)[:200])
        code = 1
    print("fights fought: %d; party now %s" % (len(FIGHTS), party()))
    return code


if __name__ == "__main__":
    sys.exit(main(int(sys.argv[1]) if len(sys.argv) > 1 else 900))
