#!/usr/bin/env python3
"""The Nomad Wagon, drawn from the player's own Darkest Dungeon (1) install.

The window itself is drawn by tools/preview_windows.py, which mirrors src/DD2Estate/Estate/NomadWagonPanel.cs and RosterWindow.cs
(change one, change the other); this entry point keeps the file names the older previews had.
Nothing from DD1 is copied into the repo; output goes to _lab/ (gitignored).

    python tools/preview_wagon.py [--dd1 <install>] [--out <dir>]

Writes wagon_window.png (1920x1080).
"""
import argparse
import os

import preview_windows as pw


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--dd1", default=os.environ.get("DD1_ROOT", r"E:\Steam\steamapps\common\DarkestDungeon"))
    ap.add_argument("--out", default=os.path.join(here, "..", "_lab", "preview"))
    args = ap.parse_args()
    if not os.path.isfile(os.path.join(args.dd1, "campaign", "town", "town_bg.png")):
        raise SystemExit("Darkest Dungeon (1) not found at %s (use --dd1 or DD1_ROOT)" % args.dd1)
    out = os.path.abspath(args.out)
    os.makedirs(out, exist_ok=True)
    kit = pw.Kit(args.dd1)
    print("wrote", pw.nomad_wagon(kit, out, name="wagon_window.png"))


if __name__ == "__main__":
    main()
