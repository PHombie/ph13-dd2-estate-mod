#!/usr/bin/env python3
"""DD1's Glossary (the book on the estate's bar and the screen it opens) as the plugin lays it out, drawn offline
from the player's own Darkest Dungeon (1) install with DD1's fonts. Nothing from DD1 is copied into the repo;
output goes to _lab/ (gitignored).

Reference for src/DD2Estate/Estate/Glossary.cs (which terms, in which order) and GlossaryPanel.cs (where they
stand): change one, change the other. The four lists of term numbers are read out of Glossary.cs itself.

    python tools/preview_glossary.py [--dd1 <install>] [--out <dir>] [--scroll <pixels>] [--all] [--terms]

Writes town_glossary.png (the head of the list), town_glossary_scrolled.png and town_glossary_bar.png (the bar's
row with the book shut). --all draws every term DD1 lists instead of the estate's choice, to lay beside DD1's own
screen; --terms prints every term with its number and the list it is in.

Everything is in DD1's 1920x1080 screen pixels, y down, art at its native size. A number marked FALLBACK is DD1's
own value of a layout entry, used when the file cannot be read; MOD marks what DD1's files have no number for;
MEASURED what was read off DD1's art or a DD1 frame; EXE what was read out of DD1's exe.
"""
import argparse
import glob
import os
import re

from PIL import Image

import preview_town_panels as tp
import preview_windows as pw
from preview_windows import add, box, paste

DIR = "campaign/town/glossary/"
LAYOUT = DIR + "glossary.layout.darkest"
NAME = "shared/name/name.layout.darkest"
LOG = "campaign/town/activity_log/activity_log.layout.darkest"
MAIN = "glossary_main_layout"
NAME_END = 406                                   # MEASURED from glossary_background.png: the two rules of the name end there
RULE = (584, 7)                                  # separator.png
RULE_GREY = (51, 51, 51, 255)
ARROW = (41, 25)                                 # scrollbar_uparrow.png
RAIL_W = 21                                      # scrollbarmid.png
ARROW_LINES = 3                                  # MOD: GlossaryPanel.ArrowLines
BLOCK, GAP, EMPTY = 1000, 10, 3                  # EXE: DD1's search for term numbers (Glossary.BlockSize, BlockGap, EmptyBlocks)
LISTS = ("Shown", "Dd1Rules", "Dlc", "Undecided")
# The bar's row as TownPanelButtons has it: sheet, shut, open, scale, offset, place (RealmInventory.cs, ActivityLog.cs,
# ActivityLogTownPanel.cs, Glossary.cs).
ROW = [
    ("options", "fx/estate_settings/estate_settings.sprite.png", "candles", None, 0.65, (0, 9.5), 0),
    ("glossary", "fx/estate_glossary/estate_glossary.sprite.png", "book_closed", "book_open", 0.84, (0, 2.5), 1),
    ("realm_inventory", "fx/estate_realm_inventory/estate_realm_inventory.sprite.png", "chest_closed", "chest_open", 0.65, (0, 0), 2),
    ("activity_log", "fx/estate_activity_log/estate_activity_log.sprite.png", "scroll_closed", "scroll_open", 0.76, (0, 0), 3),
]


# ---------------------------------------------------------------- DD1's terms (Glossary.Read)

def english(path, into):
    """Dd1Strings.ReadEnglish for the glossary's ids: the first entry of an id is kept."""
    with open(path, encoding="utf-8", errors="replace") as f:
        text = f.read()
    start = text.find('<language id="english">')
    if start < 0:
        return
    end = text.find("</language>", start)
    for key, value in re.findall(r'<entry id="(str_glossary_[^"]+)"><!\[CDATA\[(.*?)\]\]></entry>', text[start:end], re.S):
        into.setdefault(key, value)


def lists(source):
    """The four lists of term numbers of Glossary.cs: number -> list name (the first list a number is in)."""
    with open(source, encoding="utf-8") as f:
        code = f.read()
    where = {}
    for name in LISTS:
        m = re.search(r"private static readonly int\[\] %s =\s*\{(.*?)\};" % name, code, re.S)
        if m is None:
            raise SystemExit("no list %s in %s" % (name, source))
        for number in re.findall(r"\d+", re.sub(r"//.*", "", m.group(1))):
            where.setdefault(int(number), name)
    return where


def terms(dd1, where):
    """Every term DD1 lists, in DD1's order: (number, name, definition, list)."""
    texts = {}
    english(os.path.join(dd1, "localization", "miscellaneous.string_table.xml"), texts)
    others = glob.glob(os.path.join(dd1, "localization", "*.string_table.xml")) + glob.glob(os.path.join(dd1, "dlc", "**", "*.string_table.xml"), recursive=True)
    for path in sorted(others, key=str.lower):
        if os.path.basename(path).lower() != "miscellaneous.string_table.xml":
            english(path, texts)
    found, seen, empty, block = [], set(), 0, 0
    while empty < EMPTY:
        any_, misses, i = False, 0, 0
        while misses < GAP:
            number = block * BLOCK + i
            name = texts.get("str_glossary_term_%d" % number)
            if not name:
                misses += 1
            else:
                misses, any_ = 0, True
                if number not in seen:
                    seen.add(number)
                    found.append((number, name, texts.get("str_glossary_term_definition_%d" % number, ""), where.get(number, "Unlisted")))
            i += 1
        empty = 0 if any_ else empty + 1
        block += 1
    # EXE: tolower and strncmp
    found.sort(key=lambda t: ("".join(chr(ord(c) + 32) if "A" <= c <= "Z" else c for c in t[1]).encode("utf-8"), t[0]))
    return found


def plain(text):
    """DD1's marks dropped (the colour of a DLC's line is not drawn here); line breaks stay."""
    return "\n".join(" ".join(part.split()) for part in re.sub(r"\{[^}]*\}", "", text).split("\n")).strip()


# ---------------------------------------------------------------- the bar's row (TownPanelButtons)

def region(kit, sheet, name):
    """Dd1Install.Region: one picture out of a Spine sheet, upright."""
    page = kit.art(sheet)
    atlas = os.path.join(kit.dd1, *(sheet[:-4] + ".atlas").split("/"))
    if page is None or not os.path.isfile(atlas):
        return None
    with open(atlas, encoding="utf-8") as f:
        rows = f.read().replace("\r", "").split("\n")
    for i, row in enumerate(rows):
        if row != name:
            continue
        info = {}
        for more in rows[i + 1:]:
            if not more.startswith(" "):
                break
            key, _, value = more.partition(":")
            info[key.strip()] = [v.strip() for v in value.split(",")]
        x, y = int(info["xy"][0]), int(info["xy"][1])
        w, h = int(info["size"][0]), int(info["size"][1])
        if info.get("rotate", ["false"])[0] == "true":           # it lies turned a quarter to the left
            return page.crop((x, y, x + h, y + w)).transpose(Image.ROTATE_270)
        return page.crop((x, y, x + w, y + h))
    return None


def bar_row(kit, canvas, open_=None):
    bar = add(kit.vec(tp.TOWN, "town_screen_layout", "estate_summary_pos", 0, 975), kit.vec(tp.SUMMARY, "estate_summary_layout", "pos_offset", 0, -17))     # FALLBACK
    first = add(bar, kit.vec(tp.SUMMARY, "estate_summary_layout", "navigation_button_start_pos", 1800, 45))
    step = kit.vec(tp.SUMMARY, "estate_summary_layout", "navigation_button_offset", -110, 0)
    for key, sheet, shut, opened, scale, offset, place in ROW:
        art = region(kit, sheet, opened if open_ == key and opened else shut)
        if art is None:
            continue
        size = (art.width * scale, art.height * scale)
        middle = (first[0] + step[0] * place + offset[0], first[1] + step[1] * place + offset[1])
        paste(canvas, art, (middle[0] - size[0] / 2, middle[1] - size[1] / 2), size)
        if key == "options":                         # the candle's wax and flame (TownPanelButtons.Register)
            for layer, at, share in (("wax", (3, -8.5), 0.65), ("flame01", (9, -36), 0.5)):
                over = region(kit, sheet, layer)
                if over is not None:
                    paste(canvas, over, (first[0] + at[0] - over.width * share / 2, first[1] + at[1] - over.height * share / 2), (over.width * share, over.height * share))


# ---------------------------------------------------------------- the screen (GlossaryPanel)

def words_block(kit, layer, pos, words, style, width):
    """GlossaryPanel.Words: wraps at `width`, 1:1, as tall as its words. Returns the height."""
    step = kit.text.line_height(style)
    lines = []
    for paragraph in words.split("\n"):
        lines += kit.text.wrap(paragraph, style, width) or [""]
    for i, text in enumerate(lines):
        kit.text.draw(layer, (pos[0], pos[1] + i * step), text, style)
    return max(1, len(lines)) * step


def glossary(kit, out, name, shown, scroll=0):
    canvas = kit.base()
    top = tp.bar_top(kit)
    box(canvas, (0, 0, tp.ROOM_W, top), (0, 0, 0, int(0.72 * 255)))
    bar_row(kit, canvas, open_="glossary")
    # TownPanel: nothing of a panel shows right of the room or below the bar's top edge
    panel = Image.new("RGBA", (tp.ROOM_W, top), (0, 0, 0, 0))
    at = kit.vec(LAYOUT, MAIN, "pos", 144, 132)                                                      # FALLBACK, here and below
    paste(panel, kit.art(DIR + "glossary_background.png"), at)
    paste(panel, kit.art(DIR + "glossary_character.png"), add(at, kit.vec(LAYOUT, MAIN, "character_position", -50, 100)))

    # DD1's name widget: the icon's corner and the lower left corner of the name's line, both from name_pos
    name_pos = add(at, kit.vec(LAYOUT, MAIN, "name_pos", -46, -8))
    paste(panel, kit.art(DIR + "glossary.icon.png"), add(name_pos, kit.vec(NAME, "name_layout", "icon_offset", 60, 26)))
    foot = add(name_pos, kit.vec(NAME, "name_layout", "text_offset", 192, 118))
    kit.text.draw(panel, (foot[0], foot[1] - kit.text.line_height("town_name")), kit.string("town_name_glossary", "Glossary"), "town_name",
                  max_width=max(120, at[0] + NAME_END - foot[0]))
    quote = add(at, kit.vec(LAYOUT, MAIN, "quote_position", 1000, 50))
    pw.block(kit, panel, quote, kit.string("str_glossary_ancestor_quote", ""), "glossary_quote", kit.num(LAYOUT, MAIN, "quote_width", 780),
             2 * kit.text.line_height("glossary_quote"), anchor="m")
    paste(panel, kit.art("shared/progression/progression_close.png"), add(at, kit.vec(LAYOUT, MAIN, "close_position", 1352, 12)))

    list_pos = add(at, kit.vec(LAYOUT, MAIN, "list_position", 680, 140))
    size = kit.vec(LAYOUT, MAIN, "list_area_size", 640, 580)
    bar_offset = kit.num(LAYOUT, MAIN, "scrollbar_offset", 20)
    spacing = kit.num(LAYOUT, MAIN, "entry_spacing", 10)
    term_width = min(size[0] - 1, kit.num(LAYOUT, "glossary_entry_layout", "term_textbox_width", 190))
    term_line, text_line = kit.text.line_height("glossary_term"), kit.text.line_height("glossary_term_definition")
    layer = Image.new("RGBA", (int(size[0]), 60 + 200 * max(1, len(shown))), (0, 0, 0, 0))
    y = 0
    rule = kit.art(DIR + "separator.png")
    for number, term, meaning, _ in shown:
        # EXE: a colon after the term
        used = max(term_line, words_block(kit, layer, (0, y), " ".join(re.sub(r"\{[^}]*\}", "", term).split()) + ":", "glossary_term", term_width))
        used = max(used, text_line, words_block(kit, layer, (term_width, y), plain(meaning), "glossary_term_definition", size[0] - term_width))
        y += used + spacing
        if rule is not None:
            paste(layer, rule, (0, y))
        else:
            box(layer, (0, y, RULE[0], RULE[1]), RULE_GREY)
        y += RULE[1] + spacing
    total = y
    offset = min(max(0, total - size[1]), scroll)
    tp.scroll_window(panel, layer, (list_pos[0], list_pos[1], size[0], size[1]), offset)
    rail_x = list_pos[0] + size[0] + bar_offset
    tp.rail(kit, panel, rail_x, list_pos[1], int(size[1]), at=offset / max(1.0, total - size[1]), shown=total > size[1])
    # MOD: as far off the window as the Activity Log's arrows, centred on the rail
    arrows = kit.vec(LOG, "activity_log_layout", "scroll_arrows_offset", 15, 7)
    arrow_x = rail_x + (RAIL_W - ARROW[0]) / 2
    paste(panel, kit.art("shared/widgets/scrollbar_uparrow.png"), (arrow_x, list_pos[1] - ARROW[1] - arrows[1]))
    paste(panel, kit.art("shared/widgets/scrollbar_downarrow.png"), (arrow_x, list_pos[1] + size[1] + arrows[1]))
    canvas.alpha_composite(panel)
    path = os.path.join(out, name)
    canvas.convert("RGB").save(path)
    return path, total


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--dd1", default=os.environ.get("DD1_ROOT", r"E:\Steam\steamapps\common\DarkestDungeon"))
    ap.add_argument("--out", default=os.path.join(here, "..", "_lab", "preview"))
    ap.add_argument("--scroll", type=int, default=0, help="how far down the second picture's list is, in pixels (default: %d clicks on the arrow)" % 8)
    ap.add_argument("--all", action="store_true", help="every term DD1 lists, not the estate's choice")
    ap.add_argument("--terms", action="store_true", help="print the terms with their numbers and lists")
    args = ap.parse_args()
    if not os.path.isfile(os.path.join(args.dd1, "campaign", "town", "town_bg.png")):
        raise SystemExit("Darkest Dungeon (1) not found at %s (use --dd1 or DD1_ROOT)" % args.dd1)
    where = lists(os.path.join(here, "..", "src", "DD2Estate", "Estate", "Glossary.cs"))
    every = terms(args.dd1, where)
    if args.terms:
        for number, name, meaning, group in every:
            print("%-9s %4d  %-22s %s" % (group, number, name, plain(meaning).replace("\n", " / ")))
        counts = {}
        for term in every:
            counts[term[3]] = counts.get(term[3], 0) + 1
        print(len(every), "terms:", ", ".join("%s %d" % pair for pair in sorted(counts.items())))
        return
    out = os.path.abspath(args.out)
    os.makedirs(out, exist_ok=True)
    kit = tp.Kit(args.dd1)
    shown = every if args.all else [t for t in every if t[3] == "Shown"]
    tag = "_all" if args.all else ""
    written = []
    canvas = kit.base()
    bar_row(kit, canvas)
    path = os.path.join(out, "town_glossary_bar.png")
    canvas.convert("RGB").save(path)
    written.append(path)
    path, total = glossary(kit, out, "town_glossary%s.png" % tag, shown)
    written.append(path)
    step = ARROW_LINES * kit.text.line_height("glossary_term_definition")
    written.append(glossary(kit, out, "town_glossary%s_scrolled.png" % tag, shown, scroll=args.scroll or 8 * step)[0])
    for path in written:
        print("wrote", path)
    print("%d terms, list %d px tall" % (len(shown), total))


if __name__ == "__main__":
    main()
