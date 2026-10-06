#!/usr/bin/env python3
"""Dev loop helper for the DD2 Estate mod (Windows).

    python tools/dev.py build            build + deploy the plugin (game must be closed)
    python tools/dev.py build -p:NoDeploy=true    compile only: nothing is copied into the game
    python tools/dev.py start            launch DD2 through Steam, wait for the bridge, skip the disclaimer
    python tools/dev.py stop             close the game (exact PID)
    python tools/dev.py restart          stop + build + start
    python tools/dev.py shot <name>      in-engine screenshot to _lab/shots/<name>.png + a 1/3 copy <name>_small.png
"""
import os
import subprocess
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "tools"))
import bridge  # noqa: E402

APP_ID = "1940340"
EXE = "Darkest Dungeon II.exe"
SHOTS = os.path.join(ROOT, "_lab", "shots")


def pids():
    out = subprocess.run(["tasklist", "/FI", f"IMAGENAME eq {EXE}", "/FO", "CSV", "/NH"], capture_output=True, text=True).stdout
    return [int(line.split('","')[1]) for line in out.splitlines() if line.startswith(f'"{EXE}"')]


def stop():
    for pid in pids():
        subprocess.run(["taskkill", "/PID", str(pid), "/F"], capture_output=True)
        print("killed", pid)
    for _ in range(20):
        if not pids():
            return
        time.sleep(0.5)


def build():
    # MSBuild switches given after the command go to MSBuild. Without this `build -p:NoDeploy=true` was a plain
    # build: the switch was dropped and the plugin was copied into the game all the same.
    switches = [a for a in sys.argv[2:] if a.startswith("-p:")]
    r = subprocess.run(["dotnet", "build", os.path.join(ROOT, "src", "DD2Estate", "DD2Estate.csproj"), "-c", "Release", "-nologo", "-v", "q"] + (["-p:SkipCore=true"] if os.environ.get("DD2ESTATE_SKIP_CORE") else []) + switches,
                       capture_output=True, text=True)
    lines = [l for l in (r.stdout + r.stderr).splitlines() if "error" in l or "Build succeeded" in l]
    print("\n".join(sorted(set(lines))))
    return r.returncode == 0


def call(req):
    return bridge.send(req)


def wait_bridge(seconds=120):
    deadline = time.time() + seconds
    while time.time() < deadline:
        try:
            if call({"cmd": "ping"}).get("ok"):
                return True
        except OSError:
            pass
        time.sleep(1)
    return False


def start():
    if not pids():
        os.startfile(f"steam://rungameid/{APP_ID}")
    if not wait_bridge():
        print("bridge did not come up")
        return False
    # wait for the main menu, then dismiss the disclaimer the way a key press would
    deadline = time.time() + 90
    while time.time() < deadline:
        r = call({"cmd": "get", "expr": "$MainMenuUiScreenBhv.m_disclaimerShown"})
        if r.get("ok"):
            if r["result"] is True:
                break
            call({"cmd": "invoke", "expr": "$MainMenuUiScreenBhv", "method": "OnMainMenuPress"})
        time.sleep(1.5)
    else:
        print("main menu did not appear")
        return False
    time.sleep(3)
    print("main menu ready")
    return True


def shot(name):
    os.makedirs(SHOTS, exist_ok=True)
    path = os.path.join(SHOTS, name + ".png")
    if os.path.exists(path):
        os.remove(path)
    call({"cmd": "shot", "path": path})
    for _ in range(40):
        if os.path.exists(path) and os.path.getsize(path) > 0:
            break
        time.sleep(0.1)
    time.sleep(0.2)
    from PIL import Image
    im = Image.open(path)
    small = os.path.join(SHOTS, name + "_small.png")
    im.resize((im.width // 3, im.height // 3)).save(small)
    print(small)


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else ""
    if cmd == "build":
        sys.exit(0 if build() else 1)
    elif cmd == "start":
        sys.exit(0 if start() else 1)
    elif cmd == "stop":
        stop()
    elif cmd == "restart":
        stop()
        if not build():
            sys.exit(1)
        sys.exit(0 if start() else 1)
    elif cmd == "shot":
        shot(sys.argv[2])
    else:
        print(__doc__)
        sys.exit(2)
