#!/usr/bin/env python3
"""Contact sheets of the game's trinket pictures on DD1's cards at several sizes, beside DD1's own trinkets:
for choosing `[Look] TrinketPictureScale` by looking (src/DD2Estate/Estate/TrinketPicture.cs).

The pictures are the running game's, written out by the plugin into a scratch folder (nothing of them belongs
in the repository):

    python tools/bridge.py run trinkets.export dir=C:/scratch/trinkets      (ask again until "running" is false)
    python tools/bridge.py run trinkets.art measure=true detail=true > C:/scratch/measure.json   (optional)

    python tools/trinket_contact_sheet.py stats  --pictures C:/scratch/trinkets [--measure C:/scratch/measure.json]
    python tools/trinket_contact_sheet.py sample --pictures ... --out sample.png [--factors 1.5,1.6,1.7,1.8,1.9]
    python tools/trinket_contact_sheet.py all    --pictures ... --out all.png

Drawn as the plugin draws them: a square of round(72 x factor) in the middle of the 72x144 card, cut off 5 px
inside the card's edge (the inner edge of the rarity border); the first row of a sheet is the old look (1.32,
cut by the card's own edge). Everything at the size a 2560x1440 screen shows it (4/3 of DD1's pixels;
--scale 1 for a 1920x1080 screen).
"""
import argparse
import glob
import json
import os
import random

from PIL import Image, ImageDraw, ImageFont

CARD = (72, 144)
FRAME = 5
OLD = 1.32
LADDER = ["very_common", "common", "uncommon", "rare", "very_rare", "ancestral", "trophy"]
BACK = (24, 22, 20)
INK = (235, 225, 200)


def dd1_root():
    for root in ("E:/Steam/steamapps/common/DarkestDungeon", "C:/Program Files (x86)/Steam/steamapps/common/DarkestDungeon"):
        if os.path.isdir(root):
            return root + "/"
    raise SystemExit("DD1's install was not found: give --dd1")


def font(size):
    for name in ("segoeui.ttf", "arial.ttf", "DejaVuSans.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            pass
    return ImageFont.load_default()


class Sheets:
    def __init__(self, pictures, measure, dd1, scale, factors):
        self.dir = pictures
        self.dd1 = dd1
        self.art = dd1 + "panels/icons_equip/trinket/"
        self.scale = scale
        self.factors = factors
        self.index = json.load(open(os.path.join(pictures, "index.json"), encoding="utf-8"))
        self.measure = {}
        if measure:
            data = json.load(open(measure, encoding="utf-8-sig"))
            self.measure = {p["id"]: p for p in (data.get("pictures") or [])}
        self._cards, self._pictures, self._painted, self._rarities = {}, {}, {}, None

    def px(self, v):
        return int(round(v * self.scale))

    def rarity_card(self, grade):
        if grade not in self._cards:
            path = self.art + "rarity_%s.png" % grade
            if not os.path.exists(path):
                path = self.art + "rarity_common.png"
            self._cards[grade] = Image.open(path).convert("RGBA").resize((self.px(CARD[0]), self.px(CARD[1])), Image.BILINEAR)
        return self._cards[grade]

    def picture(self, entry):
        if entry["id"] not in self._pictures:
            self._pictures[entry["id"]] = Image.open(os.path.join(self.dir, entry["file"])).convert("RGBA")
        return self._pictures[entry["id"]]

    def painted(self, entry):
        """The painted part's share of the picture's square: width, height (the plugin's measure, or the picture's own alpha)."""
        if entry["id"] in self._painted:
            return self._painted[entry["id"]]
        m = self.measure.get(entry["id"])
        if m and m.get("paint"):
            left, top, right, bottom = m["paint"]
            share = ((right - left) / m["w"], (bottom - top) / m["h"])
        else:
            im = self.picture(entry)
            box = im.split()[3].point(lambda a: 255 if a > 25 else 0).getbbox() or (0, 0, im.width, im.height)
            share = ((box[2] - box[0]) / im.width, (box[3] - box[1]) / im.height)
        self._painted[entry["id"]] = share
        return share

    def card(self, entry, factor, frame=FRAME):
        """One trinket on its card at a factor, as the plugin draws it."""
        base = self.rarity_card(entry["grade"]).copy()
        side = self.px(round(CARD[0] * factor))
        art = self.picture(entry).resize((side, side), Image.LANCZOS)
        layer = Image.new("RGBA", base.size, (0, 0, 0, 0))
        layer.paste(art, ((base.width - side) // 2, (base.height - side) // 2))
        inset = int(round(frame * self.scale))
        window = Image.new("L", base.size, 0)
        ImageDraw.Draw(window).rectangle([inset, inset, base.width - inset - 1, base.height - inset - 1], fill=255)
        layer.putalpha(Image.composite(layer.split()[3], Image.new("L", base.size, 0), window))
        return Image.alpha_composite(base, layer)

    def dd1_rarity(self, file_name):
        if self._rarities is None:
            self._rarities = {}
            files = glob.glob(self.dd1 + "trinkets/*.entries.trinkets.json") + glob.glob(self.dd1 + "dlc/*/*/trinkets/*.entries.trinkets.json")
            for f in files:
                try:
                    for e in json.load(open(f, encoding="utf-8-sig")).get("entries", []):
                        self._rarities[e["id"]] = e.get("rarity", "common")
                except (ValueError, OSError):
                    pass
        rarity = self._rarities.get(file_name[len("inv_trinket+"):-len(".png")], "common")
        return rarity if os.path.exists(self.art + "rarity_%s.png" % rarity) else "common"

    def dd1_row(self, count, seed):
        files = sorted(glob.glob(self.art + "inv_trinket+*.png"))
        random.Random(seed).shuffle(files)
        cards = []
        for path in files[:count]:
            base = Image.open(self.art + "rarity_%s.png" % self.dd1_rarity(os.path.basename(path))).convert("RGBA")
            cards.append(Image.alpha_composite(base, Image.open(path).convert("RGBA")).resize((self.px(CARD[0]), self.px(CARD[1])), Image.BILINEAR))
        return cards

    def rows(self):
        return [(OLD, 0, "%.2f before" % OLD)] + [(f, FRAME, "%.2f" % f) for f in self.factors]

    def stats(self):
        window = (CARD[0] - 2 * FRAME, CARD[1] - 2 * FRAME)
        print("%d pictures; the window is %dx%d of the card %dx%d; DD1's own trinkets are 65 wide and 111 high in the middle of the range"
              % ((len(self.index),) + window + CARD))
        for f in [OLD] + self.factors:
            side = round(CARD[0] * f)
            widths = sorted(self.painted(e)[0] * side for e in self.index)
            heights = sorted(self.painted(e)[1] * side for e in self.index)
            n = len(widths)
            lost = [max(0.0, w - window[0]) / w for w in widths]
            print("factor %.2f (side %3d): painted width p10 %3.0f, median %3.0f, p90 %3.0f; height median %3.0f | cut at the sides %3d of %d"
                  " (by more than a tenth of their width %3d, more than a quarter %3d); at top or foot %d"
                  % (f, side, widths[n // 10], widths[n // 2], widths[n * 9 // 10], heights[n // 2], sum(1 for w in widths if w > window[0] + 0.5), n,
                     sum(1 for c in lost if c > 0.10), sum(1 for c in lost if c > 0.25), sum(1 for h in heights if h > window[1] + 0.5)))

    def all(self, out, per_row=32):
        rank = {g: i for i, g in enumerate(LADDER)}
        entries = sorted(self.index, key=lambda e: (rank.get(e["grade"], 9), self.painted(e)[0], e["name"]))
        cw, ch = self.px(CARD[0]) + 6, self.px(CARD[1]) + 6
        lines = (len(entries) + per_row - 1) // per_row
        head = 44
        block = head + lines * ch + 16
        rows = self.rows()
        sheet = Image.new("RGB", (per_row * cw + 24, 60 + head + ch + 16 + block * len(rows)), BACK)
        d = ImageDraw.Draw(sheet)
        d.text((12, 10), "DD2's %d trinket pictures on DD1's cards at each size, cut by the card's frame." % len(entries), font=font(26), fill=INK)
        y = 60
        d.text((12, y + 6), "DD1's own trinkets (the size they are meant to have)", font=font(22), fill=INK)
        for i, c in enumerate(self.dd1_row(per_row, 7)):
            sheet.paste(c, (12 + i * cw, y + head))
        y += head + ch + 16
        for factor, frame, words in rows:
            d.text((12, y + 6), "factor " + words, font=font(24), fill=INK)
            for i, e in enumerate(entries):
                sheet.paste(self.card(e, factor, frame), (12 + (i % per_row) * cw, y + head + (i // per_row) * ch))
            y += block
        sheet.save(out)
        print(out, sheet.size)

    def sample(self, out, count=40):
        by_width = sorted(self.index, key=lambda e: self.painted(e)[0])
        n = len(by_width)
        entries = [by_width[int(i * (n - 1) / (count - 1))] for i in range(count)]
        half = len(entries) // 2
        cw, ch = self.px(CARD[0]) + 8, self.px(CARD[1]) + 8
        rows = [(None, 0, "DD1's own")] + self.rows()
        sheet = Image.new("RGB", (half * cw + 24 + 170, 70 + 2 * (len(rows) * (ch + 4) + 40)), BACK)
        d = ImageDraw.Draw(sheet)
        d.text((12, 10), "Trinket pictures at each size: %d of DD2's %d, from the narrowest to the widest; cut by the card's frame." % (count, n), font=font(22), fill=INK)
        dd1 = self.dd1_row(len(entries), 11)
        y = 60
        for part in range(2):
            chunk = entries[part * half:(part + 1) * half]
            for factor, frame, words in rows:
                d.text((12, y + ch // 2 - 14), words, font=font(22), fill=INK)
                for i, e in enumerate(chunk):
                    sheet.paste(dd1[part * half + i] if factor is None else self.card(e, factor, frame), (170 + i * cw, y))
                y += ch + 4
            y += 40
        sheet.save(out)
        print(out, sheet.size)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("what", choices=["stats", "sample", "all"])
    parser.add_argument("--pictures", required=True, help="the folder trinkets.export wrote (its index.json and the PNGs)")
    parser.add_argument("--measure", help="the reply of trinkets.art measure=true detail=true, as a file (else the pictures' own alpha is measured)")
    parser.add_argument("--out", help="the picture to write")
    parser.add_argument("--factors", default="1.5,1.6,1.7,1.8,1.9")
    parser.add_argument("--scale", type=float, default=4.0 / 3.0, help="screen pixels per pixel of DD1's 1920x1080 (4/3 for 2560x1440)")
    parser.add_argument("--dd1", help="DD1's install folder")
    args = parser.parse_args()
    dd1 = (args.dd1.rstrip("/\\") + "/") if args.dd1 else dd1_root()
    sheets = Sheets(args.pictures, args.measure, dd1, args.scale, [float(f) for f in args.factors.split(",") if f])
    if args.what == "stats":
        sheets.stats()
    elif not args.out:
        raise SystemExit("give --out")
    elif args.what == "sample":
        sheets.sample(args.out)
    else:
        sheets.all(args.out)


if __name__ == "__main__":
    main()
