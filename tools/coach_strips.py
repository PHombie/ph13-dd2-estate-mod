#!/usr/bin/env python3
"""One passage of an Estate session before (upper row) and after (lower row) a change: frames out of two films
of tools/coach_watch.py, side by side.

    python tools/coach_strips.py <out.png> "<title>" "<leg name>" <end|start|all|a-b,c-d> <before folder> <after folder> [columns=8] [cell=400]

The leg's name is the one coach_watch.py prints ("hallway fight won", "camp, night ambush won", ...). The frames are
taken evenly over
  end      from the fight's last clear frames to the hub's first quarter of a second
  start    from the hub's last frames before the wipe to the fight's first frames
  all      the whole leg
  a-b,c-d  frame numbers: a-b of the film before, c-d of the film after
"""
import json
import os
import sys

from PIL import Image, ImageDraw, ImageFont

out, title, leg, how, before, after = sys.argv[1:7]
options = dict(a.split("=", 1) for a in sys.argv[7:])
columns, cell = int(options.get("columns", 8)), int(options.get("cell", 400))


def font(size):
    for name in ("arialbd.ttf", "arial.ttf", "segoeui.ttf"):
        try:
            return ImageFont.truetype(os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts", name), size)
        except OSError:
            pass
    return ImageFont.load_default()


def span(folder, manual):
    watch = json.load(open(os.path.join(folder, "watch.json"), encoding="utf-8"))
    legs = watch["legs"]
    film = watch["film"]
    if manual:
        lo, hi = (int(v) for v in manual.split("-"))
        return [f for f in film if lo <= f["frame"] <= hi]
    start = next(frame for name, frame in legs if name == leg)
    end = next((frame for name, frame in legs if frame > start), 10 ** 9)
    frames = [f for f in film if start <= f["frame"] < end]
    if how == "all":
        return frames
    fight = [i for i, f in enumerate(frames) if f["where"].startswith("COMBAT") and f["where"].endswith("fader clear")]
    if not fight:
        return frames
    if how == "end":
        last = fight[-1]
        back = next((i for i in range(last, len(frames)) if frames[i]["where"].startswith("ESTATE") and frames[i]["where"].endswith("fader clear")), len(frames) - 1)
        return frames[max(0, last - 6):min(len(frames), back + 24)]
    first = fight[0]
    wipe = next((i for i in range(first, -1, -1) if frames[i]["where"].startswith("ESTATE") and frames[i]["where"].endswith("fader clear")), 0)
    return frames[max(0, wipe - 14):min(len(frames), first + 36)]


def row(folder, manual):
    frames = span(folder, manual)
    picked = [frames[int(round(k * (len(frames) - 1) / (columns - 1.0)))] for k in range(columns)] if len(frames) > columns else frames
    cells = []
    for f in picked:
        im = Image.open(os.path.join(folder, "film", "%07d.jpg" % f["frame"])).convert("RGB")
        if f.get("flipped"):
            im = im.transpose(Image.FLIP_TOP_BOTTOM)
        cells.append((im.resize((cell, cell * 270 // 480), Image.LANCZOS), f))
    seconds = (frames[-1]["frame"] - frames[0]["frame"]) if frames else 0
    return cells, seconds


manual = how.split(",") if "-" in how else [None, None]
h = cell * 270 // 480
band = 26
page = Image.new("RGB", (columns * cell, 2 * (h + 18 + band) + 34), (18, 18, 18))
draw = ImageDraw.Draw(page)
draw.text((8, 6), title, font=font(20), fill=(255, 225, 150))
y = 34
for label, folder, m in (("BEFORE", before, manual[0]), ("AFTER", after, manual[1])):
    cells, frames = row(folder, m)
    draw.text((8, y + 3), "%s  (%d game frames)" % (label, frames), font=font(16), fill=(230, 230, 230))
    y += band
    for i, (im, f) in enumerate(cells):
        page.paste(im, (i * cell, y))
        draw.text((i * cell + 3, y + h + 2), "%d %s" % (f["frame"], f["where"].replace(" (estate)", "").replace("fader ", "/ ")), font=font(11), fill=(190, 190, 190))
    y += h + 18
page.save(out)
print(out, page.size)
