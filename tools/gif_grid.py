#!/usr/bin/env python3
"""Several GIFs side by side in one, each with a caption (to compare variants at a glance).

    python tools/gif_grid.py <out.gif> <width of a cell> "<caption>=<file.gif>" ...
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFont, ImageSequence


def frames_of(path):
    im = Image.open(path)
    frames, total = [], 0
    for f in ImageSequence.Iterator(im):
        frames.append(f.convert("RGB"))
        total += f.info.get("duration", 60)
    # one pace for the whole clip: the mean of its frames
    return frames, total / max(1, len(frames))


def font(size):
    for name in ("arialbd.ttf", "arial.ttf", "segoeui.ttf"):
        try:
            return ImageFont.truetype(os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts", name), size)
        except OSError:
            pass
    return ImageFont.load_default()


def main(out, cell, pairs):
    clips = []
    for pair in pairs:
        caption, path = pair.split("=", 1)
        frames, duration = frames_of(path)
        clips.append((caption, frames, duration))
    count = min(len(c[1]) for c in clips)
    w = cell
    h = int(cell * clips[0][1][0].height / clips[0][1][0].width)
    columns = 2 if len(clips) > 1 else 1
    rows = (len(clips) + columns - 1) // columns
    f = font(max(14, cell // 30))
    sheet = []
    for i in range(count):
        page = Image.new("RGB", (w * columns, h * rows))
        draw = ImageDraw.Draw(page)
        for n, (caption, frames, _) in enumerate(clips):
            x, y = w * (n % columns), h * (n // columns)
            page.paste(frames[i].resize((w, h), Image.LANCZOS), (x, y))
            draw.text((x + 11, y + 9), caption, font=f, fill=(0, 0, 0))
            draw.text((x + 10, y + 8), caption, font=f, fill=(255, 225, 150))
        sheet.append(page.quantize(colors=72, method=Image.MEDIANCUT, dither=Image.NONE))
    duration = int(round(sum(c[2] for c in clips) / len(clips) / 10.0)) * 10
    sheet[0].save(out, save_all=True, append_images=sheet[1:], duration=duration, loop=0, optimize=False, disposal=1)
    print("%s: %d frames of %d ms, %.1f MB" % (out, count, duration, os.path.getsize(out) / 1e6))


if __name__ == "__main__":
    if len(sys.argv) < 4:
        print(__doc__)
        sys.exit(2)
    main(sys.argv[1], int(sys.argv[2]), sys.argv[3:])
