#!/usr/bin/env python3
"""A GIF of a film the game made of its own screen (the dev command estate.film), at the game's own pace.

    python tools/bridge.py run estate.film on=true folder=D:/x/film every=3 width=960
    ... what is to be seen ...
    python tools/bridge.py run estate.film on=false > D:/x/film.json       (its "frames": the frames with the game's clock at each)
    python tools/film_gif.py <film folder> <frames.json> <out.gif> [width=720] [crop=x0,y0,x1,y1] [from=s] [to=s] [fps=15] [sheet=out.png]

<frames.json> is the list of frames (the "frames" of the reply, or the reply's result itself). crop is in shares of
the frame (0,0.06,0.76,0.66: the scene over a dungeon's HUD, left of a camp's scrolls); from and to in seconds of the
film; sheet also writes sixteen of the frames side by side. No video tools needed.
"""
import json
import os
import sys

from PIL import Image

folder, listing, out = sys.argv[1], sys.argv[2], sys.argv[3]
options = dict(a.split("=", 1) for a in sys.argv[4:])
width = int(options.get("width", 720))
crop = [float(v) for v in options.get("crop", "0,0,1,1").split(",")]
fps = float(options.get("fps", 15))
frames = json.load(open(listing, encoding="utf-8"))
if isinstance(frames, dict):
    frames = frames.get("frames") or (frames.get("result") or {}).get("frames") or []
t0 = frames[0]["time"]
lo, hi = float(options.get("from", 0)), float(options.get("to", 1e9))
picked, last = [], -1e9
for f in frames:
    t = f["time"] - t0
    if t < lo or t > hi or t - last < 1.0 / fps - 1e-4:
        continue
    last = t
    picked.append(f)
images = []
for f in picked:
    im = Image.open(os.path.join(folder, "%07d.jpg" % f["frame"])).convert("RGB")
    if f.get("flipped"):
        im = im.transpose(Image.FLIP_TOP_BOTTOM)
    w, h = im.size
    im = im.crop((int(crop[0] * w), int(crop[1] * h), int(crop[2] * w), int(crop[3] * h)))
    im = im.resize((width, int(round(width * im.height / im.width))), Image.LANCZOS)
    images.append(im)
if not images:
    sys.exit("no frames")
palette = [im.quantize(colors=96, method=Image.MEDIANCUT, dither=Image.NONE) for im in images]
duration = int(round(1000.0 / fps / 10.0)) * 10
palette[0].save(out, save_all=True, append_images=palette[1:], duration=duration, loop=0, optimize=False, disposal=1)
print("%s: %d frames of %d ms, %dx%d, %.1f MB" % (out, len(images), duration, images[0].width, images[0].height, os.path.getsize(out) / 1e6))
if "sheet" in options:
    step = max(1, len(images) // 16)
    cells = images[::step][:16]
    columns = 4
    rows = (len(cells) + columns - 1) // columns
    page = Image.new("RGB", (columns * cells[0].width, rows * cells[0].height))
    for i, im in enumerate(cells):
        page.paste(im, ((i % columns) * im.width, (i // columns) * im.height))
    page.save(options["sheet"])
    print(options["sheet"], page.size)
