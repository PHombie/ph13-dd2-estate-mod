#!/usr/bin/env python3
"""Sets the dungeon view up for looking at the heroes' walk (waits for it after a restart, full torch, no
foreground strips) and prints where one hero's leg joints are at points of the step cycle.

    python tools/gait_lab.py [setup|joints|both]
"""
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bridge  # noqa: E402


def run(cmd, **args):
    r = bridge.send(dict({"cmd": "run", "name": cmd}, **args))
    if not r.get("ok"):
        raise RuntimeError(r.get("error"))
    return r.get("result")


def vec(expr):
    r = bridge.send({"cmd": "get", "expr": expr})
    v = r.get("result") if r.get("ok") else None
    return [round(v[k], 3) for k in "xyz"] if isinstance(v, dict) else v


def setup():
    deadline = time.time() + 120
    state = None
    while time.time() < deadline:
        try:
            state = run("dungeon.state")
            if isinstance(state, dict) and state.get("view") == "Dungeon":
                break
        except (RuntimeError, OSError):
            pass
        time.sleep(2)
    else:
        print("the dungeon view did not come up:", state)
        return False
    time.sleep(3)
    bridge.send({"cmd": "set", "expr": "DD2Estate.Dungeon.CorridorScene", "member": "ForegroundMode", "value": 0})
    run("corridor.torch", level=100)
    bridge.send({"cmd": "invoke", "expr": "DD2Estate.Estate.NarrationBox", "method": "Hide"})
    print("ready:", {k: state[k] for k in ("room", "hallway", "leadX") if k in state})
    return True


def joints():
    print("floor", vec("@Slot0:Transform.position"))
    for hold in (-1, 0.0, 0.125, 0.25, 0.375, 0.5, 0.625, 0.75, 0.875):
        run("corridor.gait", hold=hold)
        time.sleep(0.4)
        print("hold %6.3f | pelvis %s | L hip %s knee %s ankle %s ball %s | R hip %s knee %s ankle %s ball %s" % (
            hold, vec("@ROOTSHJnt:Transform.position"),
            vec("@l_Leg_HipSHJnt:Transform.position"), vec("@l_Leg_KneeSHJnt:Transform.position"), vec("@l_Leg_AnkleSHJnt:Transform.position"), vec("@l_Leg_BallSHJnt:Transform.position"),
            vec("@r_Leg_HipSHJnt:Transform.position"), vec("@r_Leg_KneeSHJnt:Transform.position"), vec("@r_Leg_AnkleSHJnt:Transform.position"), vec("@r_Leg_BallSHJnt:Transform.position")))
    run("corridor.gait", hold=-1)


if __name__ == "__main__":
    what = sys.argv[1] if len(sys.argv) > 1 else "both"
    if what in ("setup", "both") and not setup():
        sys.exit(1)
    if what in ("joints", "both"):
        joints()
