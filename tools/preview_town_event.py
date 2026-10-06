#!/usr/bin/env python3
"""Compose the town crier's notice and the Ancestor's Memoirs from the player's own Darkest Dungeon (1) install.

Reference for the layout numbers in src/DD2Estate/Estate/TownEventPanel.cs and MemoirsPanel.cs and for the
texts TownEventText.cs builds: change one, change the other. Nothing from DD1 is copied into the repo; output
goes to _lab/ (gitignored).

    python tools/preview_town_event.py [--dd1 <install>] [--out <dir>] [--event <id> ...] [--list]

Writes town_event_<id>.png for each event asked for (default: one of each kind), town_event_dead_recruit.png
with three graves to choose from, memoirs_shelves.png and memoirs_page.png, all 1920x1080, and prints every
DD1 event with its title, tone and the lines the notice shows. Hero portraits and names are placeholders, and
the font is whatever the machine has (the plugin draws with DD2's); what is checked here is where things
stand.

How the notice is put together (art at native size, positions relative to the 1395x776 backdrop, which
town.layout.darkest puts at town_event_pos = 144,132 like every building's):
  * town_event.background.png carries the name frame, the street and the scroll. The crier
    (town_event.character.png) stands at town_event_layout `character_pos`.
  * town_event_story_layout places what is on the scroll, each by the middle of its top edge: the tone frame
    (town_event.tone_frame_<tone>.png) at `tone_frame_offset`, the title at `title_offset` (it lands on the
    frame's upper band), the picture (town_event.image_<event>.png, 500x240, inside the frame's box) at
    `image_offset`, the story at `description_offset`, `description_text_width` wide.
  * What the event does stands on the frame's lower band: town_event_layout `result_pos` plus
    town_event_info_layout `element_start_pos`. "From Beyond" puts its graves there instead:
    `result_pos` plus town_event_result_layout `recruit_entry_start_pos`, within `recruit_max_width`.
  * The memoirs: statue.layout.darkest, measured from building.layout.darkest `body_base_pos`: the shelves at
    `list_position`, `list_area_size` big, a slab (600x118) every 118 + `entry_spacing`, a title bar
    (619x136, of which rows 16-118 are the bar) per category; the Ancestor's line at `quote_position`.
"""
import argparse
import json
import os
import re

from PIL import Image, ImageDraw, ImageFont

SCREEN = (1920, 1080)

# ---- layout: keep in step with TownEventPanel.cs / MemoirsPanel.cs (backdrop pixels) ------------------------
ART_SIZE = (1395, 776)               # town_event.background.png, statue.character_background.png
NAME_CENTRE = (256, 76)              # a building's name: the frame drawn in the art (RosterWindow)
NAME_BOX = (340, 70)
TITLE_HEIGHT = 38
DESCRIPTION_HEIGHT = 142
INFO_HEIGHT = 96
INFO_RISE = 8                        # the info block starts this far above DD1's first line and is centred
GRAVE_WIDTH = 168
GRAVE_RISE = 6
SLAB = (600, 118)
BAR = (619, 136)
BAR_ROWS = (16, 118)
PICTURE = 85
FOOTER = 44
KEEPER_POS = (-12, 108)              # statue: town.layout.darkest character_pos minus area_pos

DEFAULTS = {
    ("town_event_layout", "character_pos"): (-5, 100),
    ("town_event_layout", "story_pos"): (0, 0),
    ("town_event_layout", "result_pos"): (770, 610),
    ("town_event_story_layout", "title_offset"): (1030, 150),
    ("town_event_story_layout", "image_offset"): (1030, 216),
    ("town_event_story_layout", "description_offset"): (1030, 480),
    ("town_event_story_layout", "description_text_width"): (485,),
    ("town_event_story_layout", "tone_frame_offset"): (1030, 115),
    ("town_event_info_layout", "element_start_pos"): (250, 36),
    ("town_event_info_layout", "element_text_width"): (485,),
    ("town_event_result_layout", "recruit_entry_start_pos"): (0, 32),
    ("town_event_result_layout", "recruit_max_width"): (520,),
    ("building_base_layout", "body_base_pos"): (596, 102),
    ("statue_main_layout", "list_position"): (270, 170),
    ("statue_main_layout", "list_area_size"): (600, 580),
    ("statue_main_layout", "quote_position"): (590, 80),
    ("statue_main_layout", "quote_width"): (505,),
    ("statue_main_layout", "entry_spacing"): (10,),
    ("statue_entry_layout", "textbox_width"): (350,),
}
AREA_POS = (144, 132)                # town.layout.darkest town_background_layout area_pos

GOLD = (220, 181, 92, 255)
PARCHMENT = (222, 209, 178, 255)
INK = (33, 23, 15, 255)
GREY = (158, 148, 128, 255)


# ---------------------------------------------------------------- DD1 files

def read_json(path):
    with open(path, encoding="utf-8-sig") as f:
        return json.load(f)


def _is_number(tok):
    try:
        float(tok)
        return True
    except ValueError:
        return False


def parse_darkest(text):
    blocks, block, key = {}, None, None
    for raw in text.splitlines():
        line = raw.split("//")[0]
        for tok in line.split():
            if tok.endswith(":") and not tok.startswith("."):
                block = blocks.setdefault(tok[:-1], {})
                key = None
            elif tok.startswith(".") and len(tok) > 1 and not _is_number(tok):
                if block is None:
                    block = blocks.setdefault("", {})
                key = tok[1:]
                block[key] = []
            elif block is not None and key is not None:
                block[key].append(tok)
    return blocks


class Layout:
    FILES = ("campaign/town/town_event/town_event.layout.darkest", "campaign/town/buildings/building.layout.darkest",
             "campaign/town/buildings/statue/statue.layout.darkest")

    def __init__(self, dd1):
        self.blocks = {}
        for rel in self.FILES:
            path = os.path.join(dd1, *rel.split("/"))
            if os.path.isfile(path):
                with open(path, encoding="utf-8", errors="replace") as f:
                    self.blocks.update(parse_darkest(f.read()))

    def get(self, block, key):
        values = self.blocks.get(block, {}).get(key)
        stock = DEFAULTS[(block, key)]
        if not values or len(values) < len(stock):
            return stock
        return tuple(float(v) for v in values[:len(stock)])


def english(dd1):
    """The English section of DD1's string tables: the main table whole, the others for the events' texts.
    An entry may run over several lines (Dd1Strings.ReadEnglish)."""
    found = {}
    folder = os.path.join(dd1, "localization")
    entry = re.compile(r'<entry id="([^"]+)"><!\[CDATA\[(.*?)\]\]></entry>', re.S)
    tables = ["miscellaneous.string_table.xml"] + sorted(f for f in os.listdir(folder) if f.endswith(".string_table.xml") and f != "miscellaneous.string_table.xml")
    for index, name in enumerate(tables):
        with open(os.path.join(folder, name), encoding="utf-8", errors="replace") as f:
            text = f.read()
        start = text.find('<language id="english">')
        if start < 0:
            continue
        end = text.find("</language>", start)
        for m in entry.finditer(text[start:end if end > 0 else len(text)]):
            if m.group(1) in found:
                continue
            if index > 0 and not m.group(1).startswith(("town_event_", "str_vo_")):
                continue
            found[m.group(1)] = m.group(2)
    return found


def plain(text):
    return " ".join((text or "").split())


def fmt(format_text, *args):
    """Dd1Strings.Format: %s %d %+d %%, the {?name} notes and the colour marks dropped."""
    args = list(args)
    text = re.sub(r"\{[^}]*\}", "", format_text)

    def fill(m):
        if m.group(0) == "%%":
            return "%"
        value = args.pop(0) if args else ""
        if m.group(2) == "d":
            number = int(round(float(value)))
            return ("+" if m.group(1) and number >= 0 else "") + str(number)
        return str(value)

    return plain(re.sub(r"%%|%(\+?)([sd])", fill, text))


def words(ident):
    return " ".join(w.capitalize() for w in ident.split("_"))


def plural(name):
    """TownEventText.Plural: DD1's format only hangs an "s" on the class name."""
    if name.startswith("Man") and name.endswith("Arms"):
        return "Men" + name[3:]
    if name.endswith("man"):
        return name[:-3] + "men"
    return name + "s"


class Events:
    def __init__(self, dd1, text):
        folder = os.path.join(dd1, "campaign", "town_events")
        self.text = text
        self.events = []
        for name in sorted(os.listdir(folder)):
            if name.endswith(".town_events.events.json") and name.startswith(("base.", "mode.")):
                self.events += read_json(os.path.join(folder, name))["events"]
        self.buffs = {b["id"]: b for b in read_json(os.path.join(dd1, "shared", "buffs", "base.buffs.json"))["buffs"] if b["id"].startswith("town_event_")}

    def find(self, ident):
        return next((e for e in self.events if e["id"] == ident), None)

    def title(self, ident):
        return plain(self.text.get("town_event_title_" + ident)) or words(ident)

    def description(self, ident):
        return plain(self.text.get("town_event_description_" + ident))

    def buff(self, ident):
        buff = self.buffs.get(ident)
        if buff is None:
            return words(ident)
        percent = int(round(buff["amount"] * 100))
        if buff["stat_type"] == "resolve_check_percent":
            line = "%+d%% chance to be Resolute" % percent          # the mod's words (TownEventText.Buff)
        else:
            key = "buff_stat_tooltip_" + buff["stat_type"] + ("_" + buff["stat_sub_type"] if buff["stat_sub_type"] else "")
            line = fmt(self.text[key], percent) if key in self.text else "%+d%% %s" % (percent, words(buff["stat_type"]))
        if buff["rule_type"] == "in_dungeon":
            dungeon = buff["rule_data"]["string"]
            line = fmt(self.text.get("buff_rule_tooltip_in_dungeon", "%s in %s"), line, self.text.get("dungeon_name_" + dungeon, words(dungeon)))
        return line

    def info(self, event):
        """TownEventText.Info before the arrival's notes."""
        own = self.text.get("town_event_info_" + event["id"])
        if own is not None:
            return [fmt(own)]
        t, lines = self.text, []
        for effect in event["data"]:
            kind, what, number = effect["type"], effect["string_data"], effect["number_data"]
            percent = int(round(number * 100))
            activity = t.get("town_activity_name_" + what, words(what))
            line = None
            if kind == "free_activity":
                line = fmt(t["town_event_info_format_free_activity"], activity)
            elif kind == "activity_lock":
                line = fmt(t["town_event_info_format_activity_lock"], activity)
            elif kind == "activity_cost_change":
                line = fmt(t["town_event_info_format_activity_cost_change"], activity, percent)
            elif kind == "provision_item_type_cost_change":
                line = fmt(t["town_event_info_format_provision_item_type_cost_change"], t.get("str_inventory_type_name_" + what, words(what)), percent)
            elif kind == "provision_item_type_amount_change":
                line = fmt(t["town_event_info_format_provision_item_type_amount_change"], t.get("str_inventory_type_name_" + what, words(what)), percent)
            elif kind == "upgrade_tag_free":
                line = fmt(t["town_event_info_format_upgrade_tag_free"], t.get("upgrade_tag_name_" + what, words(what)), max(1, int(round(number))))
            elif kind == "upgrade_tag_discount":
                line = fmt(t.get("town_event_info_format_upgrade_tag_discount", "%s cost %d%%"), t.get("upgrade_tag_name_" + what, words(what)), percent)
            elif kind == "embark_party_buff":
                line = fmt(t["town_event_info_format_embark_party_buff"], self.buff(what))
            elif kind == "idle_buff":
                line = fmt(t["town_event_info_format_idle_buff"], self.buff(what))
            elif kind == "idle_resolve_level":
                line = fmt(t["town_event_info_format_idle_resolve_level"].replace("%ss", "%s"), plural(words(what)), max(1, int(round(number))))
            elif kind == "stage_coach_bonus_recruits":
                line = fmt(t.get("town_event_info_format_stage_coach_bonus_recruits", "More recruits than usual"))
            elif kind == "bonus_recruit":
                line = "%d %s recruits wait at the Stage Coach" % (int(round(number)), words(what))
            elif kind == "dead_recruit":
                line = "One of the fallen may return to the living"
            if line and line not in lines:
                lines.append(line)
        return lines


# ---------------------------------------------------------------- drawing

def font(size, bold=True, italic=False):
    names = ("georgiaz.ttf", "georgiai.ttf") if italic else (("georgiab.ttf", "timesbd.ttf") if bold else ("georgia.ttf", "times.ttf"))
    for name in names:
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            pass
    return ImageFont.load_default()


_images = {}


def load(dd1, rel):
    if rel not in _images:
        path = os.path.join(dd1, *rel.split("/"))
        _images[rel] = Image.open(path).convert("RGBA") if os.path.isfile(path) else None
    return _images[rel]


def paste(canvas, img, pos):
    if img is None:
        return
    layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    layer.paste(img, (int(round(pos[0])), int(round(pos[1]))))
    canvas.alpha_composite(layer)


def fill(canvas, box, colour):
    layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    ImageDraw.Draw(layer).rectangle(box, fill=colour)
    canvas.alpha_composite(layer)


def add(a, b):
    return (a[0] + b[0], a[1] + b[1])


def wrap(draw, text, fnt, width):
    lines = []
    for paragraph in text.split("\n"):
        line = ""
        for word in paragraph.split():
            trial = (line + " " + word).strip()
            if draw.textlength(trial, font=fnt) <= width or not line:
                line = trial
            else:
                lines.append(line)
                line = word
        lines.append(line)
    return lines


def block(draw, top_centre, text, width, height, size, colour, bold=True, italic=False, centred=False, minimum=14):
    """A text block hung from the middle of its top edge, shrunk until it fits (TMP auto-size); with
    `centred` it sits in the middle of the height instead of at its top."""
    while True:
        fnt = font(size, bold, italic)
        lines = wrap(draw, text, fnt, width)
        step = int(size * 1.22)
        if len(lines) * step <= height or size <= minimum:
            break
        size -= 1
    y = top_centre[1] + ((height - len(lines) * step) / 2 if centred else 0)
    for line in lines:
        draw.text((top_centre[0] - draw.textlength(line, font=fnt) / 2, y), line, font=fnt, fill=colour)
        y += step


def one_line(draw, centre, text, width, size, colour, minimum=20):
    while size > minimum and draw.textlength(text, font=font(size)) > width:
        size -= 1
    fnt = font(size)
    box = draw.textbbox((0, 0), text, font=fnt)
    draw.text((centre[0] - (box[2] - box[0]) / 2 - box[0], centre[1] - (box[3] - box[1]) / 2 - box[1]), text, font=fnt, fill=colour)


_KIT = {}


def kit_for(dd1):
    """tools/preview_windows.py's view of the install: DD1's text styles, the hamlet with its chrome."""
    import preview_windows as pw
    if dd1 not in _KIT:
        _KIT[dd1] = pw.Kit(dd1)
    return _KIT[dd1]


def hamlet(dd1):
    """The hamlet under the notice: as under a building's window, the roster and the estate's bar stay as they are."""
    import preview_windows as pw
    kit = kit_for(dd1)
    canvas = kit.base()
    origin = tuple(int(v) for v in kit.vec(pw.TOWN, "town_screen_layout", "town_event_pos", 144, 132))      # FALLBACK
    bar_top = int(kit.vec(pw.TOWN, "town_screen_layout", "estate_summary_pos", 0, 975)[1]
                  + kit.vec("campaign/town/estate_summary/estate_summary.layout.darkest", "estate_summary_layout", "pos_offset", 0, -17)[1])
    fill(canvas, (0, 0, SCREEN[0] - pw.ROSTER_W, bar_top), (0, 0, 0, 184))            # the notice's shade
    return canvas, origin, bar_top


def frame(canvas, dd1, origin, bar_top, backdrop, keeper, keeper_pos, name):
    kit = kit_for(dd1)
    paste(canvas, load(dd1, backdrop), origin)
    figure = load(dd1, keeper)
    if figure is not None:
        at = add(origin, keeper_pos)
        paste(canvas, figure.crop((0, 0, figure.width, max(1, min(figure.height, int(bar_top - at[1]))))), at)   # the feet go under the bar
    kit.text.draw(canvas, (origin[0] + NAME_CENTRE[0], origin[1] + NAME_CENTRE[1] - 32), name, "town_name", anchor="m", max_width=296)
    close = kit.vec("campaign/town/town_event/town_event.layout.darkest", "town_event_layout", "close_pos", 1352, 12)
    paste(canvas, load(dd1, "shared/progression/progression_close.png"), add(origin, close))
    return ImageDraw.Draw(canvas)


def notice(dd1, layout, events, event, graves=None):
    art = "campaign/town/town_event/town_event."
    import preview_windows as pw
    kit = kit_for(dd1)
    canvas, origin, bar_top = hamlet(dd1)
    draw = frame(canvas, dd1, origin, bar_top, art + "background.png", art + "character.png",
                 layout.get("town_event_layout", "character_pos"), plain(events.text.get("town_name_town_Event", "Town Event")))
    story = add(origin, layout.get("town_event_layout", "story_pos"))
    width = layout.get("town_event_story_layout", "description_text_width")[0]

    tone = load(dd1, art + "tone_frame_" + event.get("tone", "neutral") + ".png")
    at = add(story, layout.get("town_event_story_layout", "tone_frame_offset"))
    if tone is not None:
        paste(canvas, tone, (at[0] - tone.width / 2, at[1]))
    at = add(story, layout.get("town_event_story_layout", "title_offset"))
    kit.text.draw(canvas, at, events.title(event["id"]), "town_event_story_title", colour="notable", anchor="m", max_width=width)
    picture = load(dd1, art + "image_" + event["id"] + ".png")
    at = add(story, layout.get("town_event_story_layout", "image_offset"))
    if picture is not None:
        paste(canvas, picture, (at[0] - picture.width / 2, at[1]))
    at = add(story, layout.get("town_event_story_layout", "description_offset"))
    pw.block(kit, canvas, at, events.description(event["id"]), "town_event_story_description", width, DESCRIPTION_HEIGHT, anchor="m")

    result = add(origin, layout.get("town_event_layout", "result_pos"))
    if graves:
        start = add(result, layout.get("town_event_result_layout", "recruit_entry_start_pos"))
        area = layout.get("town_event_result_layout", "recruit_max_width")[0]
        left = start[0] + (area - len(graves) * GRAVE_WIDTH) / 2
        for i, (name, epitaph) in enumerate(graves):
            x, y = left + i * GRAVE_WIDTH + 4, start[1] - GRAVE_RISE
            fill(canvas, (x, y, x + GRAVE_WIDTH - 8, y + INFO_HEIGHT + 2), (0, 0, 0, 90))
            cx = x + (GRAVE_WIDTH - 8) / 2
            fill(canvas, (cx - 27, y + 3, cx + 27, y + 57), (70, 62, 54, 255))
            one_line(draw, (cx, y + INFO_HEIGHT + 2 - 30), name, GRAVE_WIDTH - 16, 19, PARCHMENT, 12)
            one_line(draw, (cx, y + INFO_HEIGHT + 2 - 11), epitaph, GRAVE_WIDTH - 16, 14, GREY, 10)
    else:
        start = add(result, layout.get("town_event_info_layout", "element_start_pos"))
        info_width = layout.get("town_event_info_layout", "element_text_width")[0]
        lines = events.info(event)
        step = kit.text.line_height("town_event_info")
        top = start[1] - INFO_RISE + max(0, (INFO_HEIGHT - step * len(lines)) / 2)      # centred on the band, as the plugin's block is
        for i, words_line in enumerate(lines):
            kit.text.draw(canvas, (start[0], top + i * step), words_line, "town_event_info", anchor="m", max_width=info_width)
    return canvas


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--dd1", default=os.environ.get("DD1_ROOT", r"E:\Steam\steamapps\common\DarkestDungeon"))
    ap.add_argument("--out", default=os.path.join(here, "..", "_lab", "preview"))
    ap.add_argument("--event", action="append", help="event id to draw (repeatable); default: one of each kind")
    ap.add_argument("--list", action="store_true", help="only print the events")
    args = ap.parse_args()

    if not os.path.isfile(os.path.join(args.dd1, "campaign", "town", "town_bg.png")):
        raise SystemExit("Darkest Dungeon (1) not found at %s (use --dd1 or DD1_ROOT)" % args.dd1)
    text = english(args.dd1)
    events = Events(args.dd1, text)
    layout = Layout(args.dd1)

    print("%-44s %-8s %-26s %s" % ("event", "tone", "title", "what the notice says"))
    for event in events.events:
        print("%-44s %-8s %-26s %s" % (event["id"], event.get("tone", ""), events.title(event["id"]), " / ".join(events.info(event))))
    if args.list:
        return

    out = os.path.abspath(args.out)
    os.makedirs(out, exist_ok=True)
    wanted = args.event or ["free_abbey", "lock_bar_discount_tavern", "embark_party_buff_crypts_buff", "embark_party_buff_affliction_chance",
                            "idle_resolve_level_plague_doctor", "upgrade_tag_free_weapon", "free_all_activities", "stage_coach_bonus_recruits"]
    written = []
    for ident in wanted:
        event = events.find(ident)
        if event is None:
            print("no such event:", ident)
            continue
        path = os.path.join(out, "town_event_%s.png" % ident)
        notice(args.dd1, layout, events, event).convert("RGB").save(path)
        written.append(path)
    dead = events.find("dead_recruit")
    if dead is not None:
        path = os.path.join(out, "town_event_dead_recruit.png")
        notice(args.dd1, layout, events, dead, [("Reynauld", "Crusader, week 7"), ("Junia", "Vestal, week 12"), ("Baldwin of the Marsh", "Leper, week 14")]).convert("RGB").save(path)
        written.append(path)
    for name, page in (("memoirs_shelves.png", False), ("memoirs_page.png", True)):
        import preview_windows as pw
        written.append(pw.memoirs(kit_for(args.dd1), out, page=page, name=name))
    print("wrote:")
    for path in written:
        print("  " + path)


if __name__ == "__main__":
    main()
