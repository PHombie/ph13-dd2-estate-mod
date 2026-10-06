#!/usr/bin/env python3
"""Sheets and GIFs out of a film the dev command fx.film made (every frame of a passage, the game's clock held to a
fixed step; <folder>/NNNNNNN.jpg and <folder>/frames.json with the mode and the fader at each frame).

    python tools/passage_strip.py list  <folder>                         the stretches of the film (mode, fader), with frame numbers
    python tools/passage_strip.py sheet <folder> <out.png> [from=N] [to=N] [where=text] [step=1] [columns=6] [cell=640] [crop=x0,y0,x1,y1] [title=...]
    python tools/passage_strip.py pair  <before folder> <after folder> <out.png> [the same options; from/to as a-b,c-d]
    python tools/passage_strip.py gif   <folder> <out.gif> [from=N] [to=N] [width=720] [crop=...] [fps=30] [step=2]
    python tools/passage_strip.py gifpair <before folder> <after folder> <out.gif> anchors=N,M [lead=0.5] [tail=2.5] [fps=25] [width=720] [crop=...] [labels=BEFORE,AFTER] [sheet=out.png]
                                                                         the two films one over the other, each by its own clock from the frame the thing happened in
    python tools/passage_strip.py diff  <folder> [from=N] [to=N]         how much each frame differs from the one before (mean of 0..255)

from / to are frame numbers of the game (the files' names); where= keeps the frames whose "where" holds the text
("to black"); crop is in shares of the frame. A sheet writes each frame's number and its "where" under it.
"""
import json
import os
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFont, ImageStat


def font(size):
    for name in ("arialbd.ttf", "arial.ttf", "segoeui.ttf"):
        try:
            return ImageFont.truetype(os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts", name), size)
        except OSError:
            pass
    return ImageFont.load_default()


def load(folder):
    with open(os.path.join(folder, "frames.json"), encoding="utf-8") as f:
        return json.load(f)


def pick(folder, options, span=None):
    frames = load(folder)["frames"]
    lo, hi = int(options.get("from", 0)), int(options.get("to", 10 ** 9))
    if span:
        lo, hi = (int(v) for v in span.split("-"))
    text = options.get("where")
    out = [f for f in frames if lo <= f["frame"] <= hi and (not text or text in f["where"])]
    return out[::max(1, int(options.get("step", 1)))]


def picture(folder, f, crop):
    im = Image.open(os.path.join(folder, "%07d.jpg" % f["frame"])).convert("RGB")
    if f.get("flipped"):
        im = im.transpose(Image.FLIP_TOP_BOTTOM)
    w, h = im.size
    return im.crop((int(crop[0] * w), int(crop[1] * h), int(crop[2] * w), int(crop[3] * h)))


def cells(folder, frames, options):
    crop = [float(v) for v in options.get("crop", "0,0,1,1").split(",")]
    cell = int(options.get("cell", 640))
    small = font(max(11, cell // 40))
    out = []
    for f in frames:
        im = picture(folder, f, crop)
        im = im.resize((cell, int(round(cell * im.height / im.width))), Image.LANCZOS)
        tile = Image.new("RGB", (cell, im.height + small.size + 8), (16, 16, 16))
        tile.paste(im, (0, 0))
        ImageDraw.Draw(tile).text((4, im.height + 2), "%d  %s" % (f["frame"], f["where"]), font=small, fill=(230, 230, 230))
        out.append(tile)
    return out


def page(rows, columns, title=None, labels=None):
    """rows: lists of tiles; each list is laid out `columns` to a line."""
    lines = []
    for index, tiles in enumerate(rows):
        if labels:
            lines.append(labels[index])
        for i in range(0, len(tiles), columns):
            lines.append(tiles[i:i + columns])
    width = max(sum(t.width for t in line) for line in lines if not isinstance(line, str)) if any(not isinstance(l, str) for l in lines) else 800
    big = font(26)
    height = (44 if title else 0) + sum(36 if isinstance(line, str) else max(t.height for t in line) for line in lines)
    sheet = Image.new("RGB", (width, height), (0, 0, 0))
    draw = ImageDraw.Draw(sheet)
    y = 0
    if title:
        draw.text((8, 6), title, font=big, fill=(255, 255, 255))
        y = 44
    for line in lines:
        if isinstance(line, str):
            draw.text((8, y + 4), line, font=font(22), fill=(255, 210, 120))
            y += 36
            continue
        x = 0
        for tile in line:
            sheet.paste(tile, (x, y))
            x += tile.width
        y += max(t.height for t in line)
    return sheet


def main(argv):
    command = argv[0]
    if command == "list":
        frames = load(argv[1])["frames"]
        start, last = None, None
        for f in frames + [None]:
            where = f["where"].split(" '")[0] if f else None
            if where != last:
                if last is not None:
                    print("%7d .. %7d  %4d frames  %s" % (start, previous, count, last))
                start, last, count = (f["frame"] if f else None), where, 0
            if f:
                previous = f["frame"]
                count += 1
        return
    if command == "diff":
        options = dict(a.split("=", 1) for a in argv[2:])
        frames = pick(argv[1], options)
        before = None
        for f in frames:
            im = picture(argv[1], f, (0, 0, 1, 1)).resize((320, 180))
            if before is not None:
                print("%7d  %6.2f  %s" % (f["frame"], sum(ImageStat.Stat(ImageChops.difference(im, before)).mean) / 3.0, f["where"]))
            before = im
        return
    if command == "sheet":
        folder, out = argv[1], argv[2]
        options = dict(a.split("=", 1) for a in argv[3:])
        frames = pick(folder, options)
        sheet = page([cells(folder, frames, options)], int(options.get("columns", 6)), options.get("title"))
        sheet.save(out)
        print(out, sheet.size, len(frames), "frames")
        return
    if command == "pair":
        before, after, out = argv[1], argv[2], argv[3]
        options = dict(a.split("=", 1) for a in argv[4:])
        spans = [None, None]
        if "frames" in options:
            spans = options["frames"].split(",")
        rows = [cells(before, pick(before, options, spans[0]), options), cells(after, pick(after, options, spans[1]), options)]
        sheet = page(rows, int(options.get("columns", 6)), options.get("title"), ["BEFORE", "AFTER"])
        sheet.save(out)
        print(out, sheet.size, [len(r) for r in rows], "frames")
        return
    if command == "gifpair":
        # two films of the same happening, one over the other, each counted from the frame the thing happened in
        before, after, out = argv[1], argv[2], argv[3]
        options = dict(a.split("=", 1) for a in argv[4:])
        anchors = [int(v) for v in options["anchors"].split(",")]
        lead, tail = float(options.get("lead", 0.5)), float(options.get("tail", 2.5))
        fps = float(options.get("fps", 25))
        width = int(options.get("width", 720))
        crop = [float(v) for v in options.get("crop", "0,0,1,1").split(",")]
        labels = options.get("labels", "BEFORE,AFTER").split(",")
        films = []
        for folder, anchor in zip((before, after), anchors):
            frames = load(folder)["frames"]
            t0 = next(f["time"] for f in frames if f["frame"] >= anchor)
            films.append((folder, frames, t0))
        small = font(18)
        images = []
        ticks = int(round((lead + tail) * fps))
        for k in range(ticks):
            t = -lead + k / fps
            rows = []
            for (folder, frames, t0), label in zip(films, labels):
                f = min(frames, key=lambda g: abs(g["time"] - t0 - t))
                im = picture(folder, f, crop)
                im = im.resize((width, int(round(width * im.height / im.width))), Image.LANCZOS)
                ImageDraw.Draw(im).text((8, 6), "%s  %+.2f s" % (label, t), font=small, fill=(255, 220, 140), stroke_width=2, stroke_fill=(0, 0, 0))
                rows.append(im)
            both = Image.new("RGB", (width, sum(r.height for r in rows) + 4 * (len(rows) - 1)))
            y = 0
            for r in rows:
                both.paste(r, (0, y))
                y += r.height + 4
            images.append(both)
        palette = [im.quantize(colors=128, method=Image.MEDIANCUT, dither=Image.NONE) for im in images]
        duration = max(20, int(round(1000.0 / fps / 10.0)) * 10)
        palette[0].save(out, save_all=True, append_images=palette[1:], duration=duration, loop=0, optimize=False, disposal=1)
        print("%s: %d frames of %d ms, %dx%d, %.1f MB" % (out, len(images), duration, images[0].width, images[0].height, os.path.getsize(out) / 1e6))
        if "sheet" in options:
            step = max(1, len(images) // 8)
            cells_ = images[::step][:8]
            sheet = Image.new("RGB", (sum(c.width for c in cells_), cells_[0].height))
            x = 0
            for c in cells_:
                sheet.paste(c, (x, 0))
                x += c.width
            sheet.save(options["sheet"])
            print(options["sheet"], sheet.size)
        return
    if command == "gif":
        folder, out = argv[1], argv[2]
        options = dict(a.split("=", 1) for a in argv[3:])
        options.setdefault("step", "2")
        frames = pick(folder, options)
        crop = [float(v) for v in options.get("crop", "0,0,1,1").split(",")]
        width = int(options.get("width", 720))
        fps = float(options.get("fps", 30))
        images = []
        for f in frames:
            im = picture(folder, f, crop)
            images.append(im.resize((width, int(round(width * im.height / im.width))), Image.LANCZOS))
        if not images:
            sys.exit("no frames")
        palette = [im.quantize(colors=128, method=Image.MEDIANCUT, dither=Image.NONE) for im in images]
        duration = max(20, int(round(1000.0 / fps / 10.0)) * 10)
        palette[0].save(out, save_all=True, append_images=palette[1:], duration=duration, loop=0, optimize=False, disposal=1)
        print("%s: %d frames of %d ms, %dx%d, %.1f MB" % (out, len(images), duration, images[0].width, images[0].height, os.path.getsize(out) / 1e6))
        return
    sys.exit(__doc__)


if __name__ == "__main__":
    main(sys.argv[1:])
