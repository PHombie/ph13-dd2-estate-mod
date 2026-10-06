#!/usr/bin/env python3
"""Sends input to the running Darkest Dungeon (1) and takes a picture of its window, to look at how DD1 itself
draws something.

    python tools/dd1_drive.py <name> [command ...]

Commands are WinDrive's ("scanmode on", "key 0x1B", "hold 0x44 1500", "click 640 360"), run in order with DD1 in
front; "wait 1.5" sleeps. Then DD2 is brought to the front again and DD1's window is captured: in front DD1 runs
in exclusive full screen and gives the window capture no frames (and a copy of the screen is a stale one), in the
background it renders on and can be captured. The picture lands in _lab/shots/<name>.png (+ <name>_small.png).

The toolkit's WinDrive.ps1 must be in %LOCALAPPDATA%\\universal-modder\\tools (copied from the plugin's um/ps1).
"""
import os
import subprocess
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SHOTS = os.path.join(ROOT, "_lab", "shots")
TOOLS = os.path.join(os.environ["LOCALAPPDATA"], "universal-modder")
DRIVE = os.path.join(TOOLS, "tools", "WinDrive.ps1")


def _ffmpeg():
    """The toolkit's ffmpeg (with gfxcapture). A Store Python keeps its %LOCALAPPDATA% under Packages\...\LocalCache."""
    import glob
    found = [os.path.join(TOOLS, "ffmpeg", "bin", "ffmpeg.exe")]
    found += glob.glob(os.path.join(os.environ["LOCALAPPDATA"], "Packages", "*", "LocalCache", "Local", "universal-modder", "ffmpeg", "bin", "ffmpeg.exe"))
    for path in found:
        if os.path.exists(path):
            return path
    raise FileNotFoundError("no ffmpeg of the toolkit's: run `um win setup`")


DD1, DD2 = "Darkest", "Darkest Dungeon II"


def drive(commands, proc=DD1):
    """Runs WinDrive commands, one session per stretch between waits; returns the replies."""
    replies = []
    batch = []

    def flush():
        if not batch:
            return
        r = subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", DRIVE, "-Proc", proc],
                           input="\n".join(batch) + "\n", capture_output=True, text=True, timeout=180)
        replies.extend(l for l in r.stdout.splitlines() if l.strip())
        batch.clear()

    for c in commands:
        if c.startswith("wait "):
            flush()
            time.sleep(float(c.split()[1]))
        else:
            batch.append(c)
    flush()
    return replies


def grab(name, exe="Darkest.exe"):
    os.makedirs(SHOTS, exist_ok=True)
    path = os.path.join(SHOTS, name + ".png")
    if os.path.exists(path):
        os.remove(path)
    source = "gfxcapture=window_exe=%s:capture_cursor=0:max_framerate=60,hwdownload,format=bgra" % exe
    subprocess.run([_ffmpeg(), "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", source, "-frames:v", "1", path],
                   capture_output=True, text=True, timeout=30)
    from PIL import Image
    im = Image.open(path)
    im.resize((im.width // 2, im.height // 2)).save(os.path.join(SHOTS, name + "_small.png"))
    return path


def act(name, commands):
    """Input with DD1 in front, then the picture with DD2 in front."""
    out = drive(["focus", "scanmode on"] + list(commands)) if commands else []
    drive(["focus"], proc=DD2)
    time.sleep(1.0)
    return out, grab(name)


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(2)
    replies, picture = act(sys.argv[1], sys.argv[2:])
    print(" | ".join(replies))
    print(picture)
