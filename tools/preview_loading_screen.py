#!/usr/bin/env python3
"""Compose the mod's loading screens from the player's own Darkest Dungeon (1) install.

Reference for the layout of src/DD2Estate/Dd2/LoadingScreen.cs (the dd1 look) and LoadingLookDd2.cs (the dd2
look): the same files are read with the same fallbacks and every part is put where the plugin puts it. Change
one, change the other. Nothing from DD1 is copied into the repo; output goes to _lab/ (gitignored).

    python tools/preview_loading_screen.py [--dd1 <install>] [--out <dir>] [--ref <folder of DD1 frames>] [--boxes]
    python tools/preview_loading_screen.py --beside <DD2's own frame.png> <the mod's frame.png> [--out <file.png>]

The dd2 look (`[Look] LoadingScreen = dd2`, the stock) is DD2's own loading screen with DD1's picture, name and
tip in it; its fonts, its ink and its sign are the game's and are only seen in the game. Offline this tool
writes a PLAN of it, loading_dd2_<place>.png: DD1's picture with the name, the tip's band and the sign at the
plugin's places and sizes, the words in DD1's title font as a stand-in (--boxes outlines the parts). --beside
puts two frames taken in the game side by side (DD2's own loading screen and the mod's) and prints where the
loading sign's flame is in each.

The dd1 look, at 1920x1080:
  loading_raid_<dungeon>.png      the way into a dungeon: the region's picture, its name, one of its tips, the
                                  torch, and the line that asks for a key (at its brightest)
  loading_raid_first.png          DD1's first Ruins quest (the quest's own picture and tip): the frame
                                  loading_screen_ruins.png of the real game again
  loading_old_road.png            DD1's first screen of a new estate: the frame loading_screen_old_road.png again
  loading_town.png                the way home: the town-visit picture, "Hamlet", a town tip, no line at the foot
With --ref (default _lab/dd1_ref/raid, where the frames taken in the real game lie) the two frames are compared
with their compositions and the difference is printed; loading_*_diff.png shows where it is.

What DD1 does, and where it says so (exe: _windows/win32/Darkest.exe, addresses of this install's build):
  * loading_screen/loading_screen.layout.darkest: loading_screen_dungeon_tip_frame.posn 960 120 is the origin of
    the title and the tip. The tip's ground (loading_screen.tipoverlay.png, 638x227) hangs by the middle of its
    top edge from origin + background_offset (0 615); the title's ground (titleoverlay.png, 418x101) lies with
    its middle on origin + loading_screen_dungeon_title.offset (0 64), and the title's line with it; the tip's
    first line starts at origin + offset (0 635), in a box of .size 600 150; the torch (fx/torch_load, animation
    "loading") stands with its root on load_anim_position 954 930, a screen position; the line that asks for a
    key hangs from loading_screen_continue.posn 960 1025. [exe 0xb73300, 0xb72bf0; the two frames]
  * the picture: loading_screen.<quest>.png for a plot quest, else one of loading_screen.<dungeon>_<n>.png,
    each as likely as another; loading_screen.town_visit.png on the way home. [exe 0xb72520, 0xb72030, 0xb72330]
  * the title: dungeon_name_<dungeon>; "str_town_title" on the way home. The tip: str_<quest>_tip for a plot
    quest, else str_<dungeon>_tip; str_town_tip on the way home. An id the string table holds several times is
    one of several texts. [exe 0xb72520, 0xb72330]
  * fonts.darkest: raid_loading_screen_title (DwarvenAxe large, "notable"), raid_loading_screen_tip (Ubuntu
    small, "neutral"), loading_screen_continue (Ubuntu small, "neutral" pulsing to
    loading_screen_continue_pulse_to = 200 180 110 with no alpha, pulse_time 2.0 each way).
"""
import argparse
import math
import os
import re

from PIL import Image, ImageChops

from dd1_bmfont import Dd1Text
from preview_corridor import Prop, draw_pieces, parse_darkest

W, H = 1920, 1080
DIR = "loading_screen/"
TIP_ART = (638, 227)        # loading_screen.tipoverlay.png
TITLE_ART = (418, 101)      # loading_screen.titleoverlay.png
# LoadingScreen.NotInTheMod: DD1 tips about creatures the mod's dungeons do not hold
NOT_IN_THE_MOD = ("undead", "skeleton", "fishfolk", "thrall", "tentacled lurker", "fungal")


class Layout:
    """The numbers of LoadingScreen.cs, read the same way; the stock values are DD1's own."""

    def __init__(self, dd1):
        path = os.path.join(dd1, DIR + "loading_screen.layout.darkest")
        self.blocks = parse_darkest(open(path, encoding="utf-8-sig").read()) if os.path.isfile(path) else {}

    def pair(self, block, key, x, y):
        # the file writes some pairs with a comma between them
        values = [v.rstrip(",") for v in self.blocks.get(block, {}).get(key, [])]
        try:
            return (float(values[0]), float(values[1])) if len(values) >= 2 else (x, y)
        except ValueError:
            return (x, y)

    def number(self, block, key, fallback):
        values = self.blocks.get(block, {}).get(key, [])
        try:
            return float(values[0].rstrip(",")) if values else fallback
        except ValueError:
            return fallback


def english(dd1):
    """Every English entry of the main string table, in the file's order: [(id, text)]; an id may come several times."""
    path = os.path.join(dd1, "localization", "miscellaneous.string_table.xml")
    text = open(path, encoding="utf-8", errors="replace").read()
    start = text.find('<language id="english">')
    section = text[start:text.find("</language>", start)]
    return re.findall(r'<entry id="([^"]+)"><!\[CDATA\[(.*?)\]\]>', section, re.S)


def add(a, b):
    return (a[0] + b[0], a[1] + b[1])


def straight(image):
    """A picture whose colours were multiplied by their alpha (DD1's sprite sheets), with the colours as they
    are: what SpineAtlas does with `straight`, for a canvas that multiplies them by the alpha again."""
    pixels = [(min(255, r * 255 // a), min(255, g * 255 // a), min(255, b * 255 // a), a) if a else (0, 0, 0, 0) for r, g, b, a in image.getdata()]
    made = Image.new("RGBA", image.size)
    made.putdata(pixels)
    return made


class Screen:
    def __init__(self, dd1):
        self.dd1 = dd1
        self.l = Layout(dd1)
        self.text = Dd1Text(dd1)
        self.strings = english(dd1)
        # DD1 draws its sprite sheets as they are stored, colours times alpha: seen on the torch's glow in the
        # real frames, which is as bright as the sheet's own numbers
        self.torch = Prop(dd1, "fx/torch_load/torch_load.sprite.skel")
        self.torch.page = straight(self.torch.page)

    def first(self, string_id, fallback=None):
        return next((text for key, text in self.strings if key == string_id), fallback)

    def tips(self, key, everything=False):
        """The texts of str_<key>_tip, without the ones the mod leaves out (all of them, if that leaves none)."""
        found = [text for string_id, text in self.strings if string_id == "str_%s_tip" % key]
        kept = [t for t in found if not any(word in t.lower() for word in NOT_IN_THE_MOD)]
        return found if everything or not kept else kept

    def art(self, rel):
        path = os.path.join(self.dd1, rel)
        return Image.open(path).convert("RGBA") if os.path.isfile(path) else None

    def marked(self, canvas, pos, text, style, alpha=1.0):
        """One centred line with DD1's colour marks, its cell's top at pos[1]."""
        parts = re.split(r"(\{colour_start\|[^}]+\}|\{colour_end\})", text)
        runs, colour = [], None
        for part in parts:
            if part.startswith("{colour_start|"):
                colour = part[len("{colour_start|"):-1]
            elif part == "{colour_end}":
                colour = None
            elif part:
                runs.append((part, colour))
        plain = "".join(run for run, _ in runs)
        x = pos[0] - self.text.width(plain, style) / 2
        drawn = ""
        for run, colour in runs:
            # every run starts where the whole line would have put it (kerning and all)
            self.text.draw(canvas, (x + self.text.width(drawn, style), pos[1]), run, style, colour=colour, alpha=alpha)
            drawn += run

    def compose(self, picture, title, tip, ask, torch_time=0.3):
        l = self.l
        origin = l.pair("loading_screen_dungeon_tip_frame", "posn", 960, 120)
        title_at = add(origin, l.pair("loading_screen_dungeon_title", "offset", 0, 64))
        torch_at = l.pair("loading_screen_dungeon_title", "load_anim_position", 954, 930)
        tip_at = add(origin, l.pair("loading_screen_dungeon_tip", "offset", 0, 635))
        tip_size = l.pair("loading_screen_dungeon_tip", "size", 600, 150)
        ground_at = add(origin, l.pair("loading_screen_dungeon_tip", "background_offset", 0, 615))
        ask_at = l.pair("loading_screen_continue", "posn", 960, 1025)

        canvas = Image.new("RGBA", (W, H), (0, 0, 0, 255))
        back = self.art(picture)
        if back is not None:
            canvas.alpha_composite(back, (0, 0))
        # DD1 draws the pixel below a half (the title's ground is 101 high: its top is at 133, not 133.5)
        ground = self.art(DIR + "loading_screen.tipoverlay.png")
        if ground is not None:
            canvas.alpha_composite(ground, (int(ground_at[0] - TIP_ART[0] // 2), int(ground_at[1])))
        plate = self.art(DIR + "loading_screen.titleoverlay.png")
        if title and plate is not None:
            canvas.alpha_composite(plate, (int(title_at[0] - TITLE_ART[0] // 2), int(title_at[1] - TITLE_ART[1] // 2 - 1)))
        if title:
            line = self.text.line_height("raid_loading_screen_title")
            self.text.draw(canvas, (title_at[0], title_at[1] - int(line) // 2), title, "raid_loading_screen_title", anchor="m")
        if tip:
            plain = re.sub(r"\{colour_start\|[^}]+\}|\{colour_end\}", "", tip)
            step = self.text.line_height("raid_loading_screen_tip")
            for i, row in enumerate(self.text.wrap(plain, "raid_loading_screen_tip", tip_size[0])):
                self.text.draw(canvas, (tip_at[0], tip_at[1] + i * step), row, "raid_loading_screen_tip", anchor="m")
        # the torch is drawn over the grounds (its sparks cross the ring in the real frames)
        draw_pieces(canvas, self.torch, self.torch.pose("loading", torch_time), (int(torch_at[0]), int(torch_at[1])))
        if ask:
            self.marked(canvas, ask_at, self.first("str_loading_screen_continue", "Press [SPACE] or [CLICK] to Continue"), "loading_screen_continue")
        return canvas.convert("RGB")

    def raid(self, dungeon, quest=None, tip=0):
        key = quest if quest and os.path.isfile(os.path.join(self.dd1, DIR + "loading_screen.%s.png" % quest)) else None
        picture = DIR + ("loading_screen.%s.png" % key if key else "loading_screen.%s_0.png" % dungeon)
        tips = self.tips(key or dungeon)
        return self.compose(picture, self.first("dungeon_name_" + dungeon, dungeon), tips[tip % len(tips)] if tips else None, True)

    def town(self, tip=0):
        tips = self.tips("town")
        return self.compose(DIR + "loading_screen.town_visit.png", self.first("str_town_title", "Hamlet"), tips[tip % len(tips)] if tips else None, False)


# ---- the dd2 look (src/DD2Estate/Dd2/LoadingLookDd2.cs) ------------------------------------------------------------
# The mod's own numbers and the game's, as the plugin has them. Change one, change the other.
DD2_TITLE_AT = (960, 118)       # Dd2LoadingLook.TitleAt: the middle of the name's line
DD2_TITLE_SIZE = 64             # .TitleSize (of DD2's title font, ND Dunkel Bold)
DD2_TIP_AT = (960, 955)         # .TipAt: DD2's subtitle (SubtitlesMgr LowerPosition, 415 under the middle)
DD2_BAND = (1420, 80)           # the subtitle's band (sprite ui_gradientmiddle, black at 0.59)
DD2_WORDS = 1120                # the width its words wrap at
DD2_TIP_SIZE = 30               # the subtitle's size
DD2_GOLD = (176, 148, 94)       # the subtitle's colour (0.69, 0.58, 0.37)
DD2_SIGN_AT = (1760, 927)       # the fader's Throbber: 800,-300 from the middle, its crown 87 lower
DD2_SIGN = 110                  # the crown's ring
DD2_STAND_IN = "raid_loading_screen_title"      # DD1's DwarvenAxe stands in for ND Dunkel (which is inside DD2's bundles)


def compose_dd2(screen, picture, title, tip, boxes=False):
    """A PLAN of the dd2 look on DD1's picture: where the name, the tip's band and the game's sign stand, at the
    plugin's numbers. The words are set in DD1's title font as a stand-in and the band and the sign are drawn
    plain: places and sizes are the plugin's, the look is the game's own and is only seen in the game."""
    from PIL import ImageDraw
    canvas = Image.new("RGBA", (W, H), (0, 0, 0, 255))
    back = screen.art(picture)
    if back is not None:
        canvas.alpha_composite(back, (0, 0))
    rows = []
    if tip:
        plain = re.sub(r"\{colour_start\|[^}]+\}|\{colour_end\}", "", tip)
        rows = screen.text.wrap(plain, DD2_STAND_IN, DD2_WORDS, size=DD2_TIP_SIZE)
        # more than one line: as even as they come (Dd2LoadingLook.Settle: the box is narrowed while the count stays)
        width = DD2_WORDS - 20
        while len(rows) > 1 and width >= DD2_WORDS // 2:
            narrower = screen.text.wrap(plain, DD2_STAND_IN, width, size=DD2_TIP_SIZE)
            if len(narrower) > len(rows):
                break
            rows = narrower
            width -= 20
    step =DD2_TIP_SIZE * 1.2 - 0.05 * DD2_TIP_SIZE        # ND Dunkel's line (105.6 of 88) less the subtitle's line spacing of -5
    band_h = DD2_BAND[1] + int(math.ceil(step * (len(rows) - 1))) if rows else 0
    if rows:
        # the band: black, strongest in the middle of its length and gone at its ends
        band = Image.new("RGBA", (DD2_BAND[0], band_h), (0, 0, 0, 0))
        for x in range(DD2_BAND[0]):
            share = 1.0 - abs(2.0 * x / (DD2_BAND[0] - 1) - 1.0)
            alpha = int(255 * 0.59 * min(1.0, share * 2.5))
            band.paste((0, 0, 0, alpha), (x, 0, x + 1, band_h))
        canvas.alpha_composite(band, (DD2_TIP_AT[0] - DD2_BAND[0] // 2, DD2_TIP_AT[1] - band_h // 2))
        top = DD2_TIP_AT[1] - step * len(rows) / 2
        for i, row in enumerate(rows):
            screen.text.draw(canvas, (DD2_TIP_AT[0], top + i * step), row, DD2_STAND_IN, colour=DD2_GOLD, anchor="m", size=DD2_TIP_SIZE)
    if title:
        line = screen.text.line_height(DD2_STAND_IN, size=DD2_TITLE_SIZE)
        screen.text.draw(canvas, (DD2_TITLE_AT[0], DD2_TITLE_AT[1] - line / 2), title, DD2_STAND_IN, colour=DD2_GOLD, anchor="m", size=DD2_TITLE_SIZE)
    draw = ImageDraw.Draw(canvas)
    r = DD2_SIGN // 2
    draw.ellipse((DD2_SIGN_AT[0] - r, DD2_SIGN_AT[1] - r, DD2_SIGN_AT[0] + r, DD2_SIGN_AT[1] + r), outline=(30, 30, 40, 255), width=6)
    draw.polygon([(DD2_SIGN_AT[0], DD2_SIGN_AT[1] - 26), (DD2_SIGN_AT[0] - 12, DD2_SIGN_AT[1] + 4), (DD2_SIGN_AT[0] + 12, DD2_SIGN_AT[1] + 4)], fill=(240, 170, 40, 255))
    if boxes:
        mark = (90, 200, 255, 255)
        draw.rectangle((DD2_TITLE_AT[0] - 800, DD2_TITLE_AT[1] - DD2_TITLE_SIZE * 0.75, DD2_TITLE_AT[0] + 800, DD2_TITLE_AT[1] + DD2_TITLE_SIZE * 0.75), outline=mark)
        if rows:
            draw.rectangle((DD2_TIP_AT[0] - DD2_BAND[0] // 2, DD2_TIP_AT[1] - band_h // 2, DD2_TIP_AT[0] + DD2_BAND[0] // 2, DD2_TIP_AT[1] + band_h // 2), outline=mark)
            draw.rectangle((DD2_TIP_AT[0] - DD2_WORDS // 2, DD2_TIP_AT[1] - band_h // 2 + 15, DD2_TIP_AT[0] + DD2_WORDS // 2, DD2_TIP_AT[1] + band_h // 2 - 15), outline=mark)
        draw.rectangle((DD2_SIGN_AT[0] - 50, DD2_SIGN_AT[1] - 87 - 50, DD2_SIGN_AT[0] + 50, DD2_SIGN_AT[1] - 87 + 50), outline=mark)
    return canvas.convert("RGB")


def sign_of(frame):
    """Where the game's loading sign burns in a frame: the middle of the warm bright pixels (its flame) in the
    lower right quarter, in 1920x1080 pixels; None when there is none."""
    w, h = frame.size
    part = frame.crop((w // 2, h // 2, w, h)).convert("RGB")
    xs, ys, n = 0, 0, 0
    data = part.load()
    for y in range(0, part.size[1], 2):
        for x in range(0, part.size[0], 2):
            r, g, b = data[x, y]
            if r > 200 and g > 120 and b < 110 and r - b > 120:
                xs += x
                ys += y
                n += 1
    if n < 6:
        return None
    return (round((w // 2 + xs / n) * W / w), round((h // 2 + ys / n) * H / h), n)


def beside(left_path, right_path, out_path, names=("DD2's own loading screen", "the mod's (DD2's layout, DD1's picture, name and tip)")):
    """Two frames of the game side by side at the same height, a line of words over each, and the place of the
    loading sign's flame in each (printed)."""
    from PIL import ImageDraw
    frames = [Image.open(p).convert("RGB") for p in (left_path, right_path)]
    height = 720
    scaled = [f.resize((int(round(f.size[0] * height / f.size[1])), height), Image.LANCZOS) for f in frames]
    gap, head = 12, 26
    sheet = Image.new("RGB", (sum(s.size[0] for s in scaled) + gap, height + head), (24, 24, 24))
    draw = ImageDraw.Draw(sheet)
    x = 0
    for frame, small, name in zip(frames, scaled, names):
        sheet.paste(small, (x, head))
        draw.text((x + 6, 6), "%s  (%dx%d)" % (name, frame.size[0], frame.size[1]), fill=(230, 220, 200))
        x += small.size[0] + gap
    sheet.save(out_path)
    return "%s\n  the sign's flame (1920x1080 pixels, count of flame pixels): %s | %s" % (out_path, sign_of(frames[0]), sign_of(frames[1]))


def compare(made, frame_path, out_path):
    """Mean difference per channel (0..255) between a composition and a frame of the real game; the torch's
    flames, the frame counter in the corner and the pulsing line are not expected to agree."""
    frame = Image.open(frame_path).convert("RGB")
    if frame.size != made.size:
        return "%s is %dx%d: not compared" % (os.path.basename(frame_path), frame.size[0], frame.size[1])
    diff = ImageChops.difference(made, frame)
    diff.point(lambda v: min(255, v * 4)).save(out_path)

    def mean(box):
        part = diff.crop(box)
        return sum(sum(part.getdata(band)) for band in range(3)) / (3.0 * part.size[0] * part.size[1])

    return "%s: whole screen %.2f, title %.2f, tip text %.2f, torch %.2f, foot line %.2f" % (
        os.path.basename(frame_path), mean((0, 30, W, H)), mean((751, 133, 1169, 234)), mean((641, 745, 1279, 830)), mean((880, 840, 1030, 1012)), mean((700, 1020, 1220, 1056)))


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--dd1", default=os.environ.get("DD1_ROOT", r"E:\Steam\steamapps\common\DarkestDungeon"))
    ap.add_argument("--out", default=os.path.join(here, "..", "_lab", "preview"))
    ap.add_argument("--ref", default=os.path.join(here, "..", "_lab", "dd1_ref", "raid"))
    ap.add_argument("--boxes", action="store_true", help="outline the parts of the dd2 plan")
    ap.add_argument("--beside", nargs=2, metavar=("DD2_FRAME", "MOD_FRAME"), help="two frames of the game side by side")
    args = ap.parse_args()
    if args.beside:
        out = args.out if args.out.lower().endswith(".png") else os.path.join(args.out, "loading_beside.png")
        os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
        print(beside(args.beside[0], args.beside[1], out))
        return
    os.makedirs(args.out, exist_ok=True)
    screen = Screen(args.dd1)

    def save(image, name):
        path = os.path.join(args.out, name)
        image.save(path)
        print(path)
        return path

    for dungeon in ("crypts", "weald", "warrens", "cove", "darkestdungeon"):
        save(screen.raid(dungeon), "loading_raid_%s.png" % dungeon)
        kept, everything = screen.tips(dungeon), screen.tips(dungeon, True)
        print("  %s: %d tips of DD1's %d" % (dungeon, len(kept), len(everything)))
    save(screen.town(), "loading_town.png")

    # the dd2 look: a plan of it for each place
    for dungeon in ("crypts", "weald", "warrens", "cove"):
        tips = screen.tips(dungeon)
        longest = max(tips, key=len) if tips else None
        save(compose_dd2(screen, DIR + "loading_screen.%s_0.png" % dungeon, screen.first("dungeon_name_" + dungeon, dungeon), longest, args.boxes), "loading_dd2_%s.png" % dungeon)
    tips = screen.tips("town")
    save(compose_dd2(screen, DIR + "loading_screen.town_visit.png", screen.first("str_town_title", "Hamlet"), tips[0] if tips else None, args.boxes), "loading_dd2_town.png")

    first = screen.raid("crypts", "plot_tutorial_crypts")
    save(first, "loading_raid_first.png")
    # DD1's own first screen (scripts/starting_save/persist.loading_screen.json): the Old Road's picture, name and tip
    road_tip = None
    for table in ("PSN.string_table.xml",):
        path = os.path.join(args.dd1, "localization", table)
        if os.path.isfile(path):
            m = re.search(r'<entry id="str_old_road_tip"><!\[CDATA\[(.*?)\]\]>', open(path, encoding="utf-8", errors="replace").read(), re.S)
            road_tip = m.group(1) if m else None
    road = screen.compose(DIR + "loading_screen.old_road.png", screen.first("dungeon_name_tutorial", "The Old Road"), road_tip, False)
    save(road, "loading_old_road.png")

    for made, frame, name in ((first, "loading_screen_ruins.png", "loading_raid_first_diff.png"), (road, "loading_screen_old_road.png", "loading_old_road_diff.png")):
        path = os.path.join(args.ref, frame)
        if os.path.isfile(path):
            print(compare(made, path, os.path.join(args.out, name)))


if __name__ == "__main__":
    main()
