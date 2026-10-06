#!/usr/bin/env python3
"""The Tavern and the Abbey, drawn from the player's own Darkest Dungeon (1) install.

The window itself is drawn by tools/preview_windows.py, which mirrors src/DD2Estate/Estate/BuildingPanel.cs and RosterWindow.cs
(change one, change the other); this entry point keeps the file names the older previews had.
Nothing from DD1 is copied into the repo; output goes to _lab/ (gitignored).

    python tools/preview_building_panel.py [--dd1 <install>] [--out <dir>] [--plain]

Writes tavern_panel.png and abbey_panel.png, or with --plain (every slot empty, the window's hint on its line of text) tavern_panel_plain.png and abbey_panel_plain.png (1920x1080).
"""
import argparse
import os

import preview_windows as pw


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--dd1", default=os.environ.get("DD1_ROOT", r"E:\Steam\steamapps\common\DarkestDungeon"))
    ap.add_argument("--out", default=os.path.join(here, "..", "_lab", "preview"))
    ap.add_argument("--plain", action="store_true", help="every slot empty, no hero waiting")
    args = ap.parse_args()
    if not os.path.isfile(os.path.join(args.dd1, "campaign", "town", "town_bg.png")):
        raise SystemExit("Darkest Dungeon (1) not found at %s (use --dd1 or DD1_ROOT)" % args.dd1)
    out = os.path.abspath(args.out)
    os.makedirs(out, exist_ok=True)
    kit = pw.Kit(args.dd1)
    for building in ("tavern", "abbey"):
        print("wrote", pw.activities(kit, out, building, plain=args.plain, name="%s_panel%s.png" % (building, "_plain" if args.plain else "")))


if __name__ == "__main__":
    main()
