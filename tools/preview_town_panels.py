#!/usr/bin/env python3
"""The town screens that are not buildings (DD1's "Trade Heirlooms" panel, its Activity Log and its Trinket
Inventory) and their buttons on the estate's bar, as the plugin lays them out, drawn offline from the player's
own Darkest Dungeon (1) install with DD1's fonts. Nothing from DD1 is copied into the repo; output goes to _lab/
(gitignored).

Reference for src/DD2Estate/Estate/ActivityLogTownPanel.cs (TownPanel, TownPanelButtons), HeirloomExchangePanel.cs,
ActivityLogPanel.cs, ActivityLogText.cs and RealmInventoryPanel.cs: change one, change the other.

    python tools/preview_town_panels.py [--dd1 <install>] [--out <dir>] [--only exchange,log,inventory,bar]

Writes town_bar_buttons.png, town_heirloom_exchange.png, town_heirloom_exchange_uneven.png, town_heirloom_exchange_poor.png,
town_activity_log.png, town_activity_log_scrolled.png, town_realm_inventory.png, town_realm_inventory_refused.png,
town_realm_inventory_selling.png and town_realm_inventory_empty.png.

Everything is in DD1's 1920x1080 screen pixels, y down, art at its native size. A number marked FALLBACK is DD1's
own value of a layout entry, used when the file cannot be read; MOD marks what DD1's files have no number for;
MEASURED what was read off DD1's art. What the game supplies at run time (DD2 portraits and trinket icons, DD2's
item text) is stood in for by DD1 art of the same size and by sample words.
"""
import argparse
import json
import os
import re

from PIL import Image

import preview_windows as pw
from preview_windows import add, box, paste

TOWN = "campaign/town/town.layout.darkest"
SUMMARY = "campaign/town/estate_summary/estate_summary.layout.darkest"
ROOM_W = pw.SCREEN[0] - pw.ROSTER_W              # TownPanel.RoomWidth
NAME_LAYOUT = "shared/name/name.layout.darkest"  # DD1's screen-name widget (UI/Dd1ScreenName.cs): icon_offset 60 26, text_offset 192 118
HEIRLOOMS = ["bust", "portrait", "deed", "crest"]


# ---------------------------------------------------------------- DD1's words with their colours

_RAW = {}


def raw_string(kit, key):
    """Dd1Strings.Get: the text as DD1 writes it, colour marks and all."""
    if not _RAW:
        path = os.path.join(kit.dd1, "localization", "miscellaneous.string_table.xml")
        if os.path.isfile(path):
            with open(path, encoding="utf-8", errors="replace") as f:
                text = f.read()
            start = text.find('<language id="english">')
            end = text.find("</language>", start)
            for key2, value in re.findall(r'<entry id="([^"]+)"><!\[CDATA\[(.*?)\]\]></entry>', text[start:end], re.S):
                _RAW.setdefault(key2, value)
    return _RAW.get(key)


def story(kit, key, fallback, *args):
    """ActivityLogText.Story: printf places filled, {colour_start|id} / {colour_end} as [[id]] / [[/]] spans, other marks dropped."""
    fmt = raw_string(kit, key) or fallback or ""
    out, i, n = [], 0, 0
    while i < len(fmt):
        c = fmt[i]
        if c == "{":
            close = fmt.find("}", i)
            if close > i:
                mark = fmt[i + 1:close]
                if mark.startswith("colour_start|"):
                    out.append("[[" + mark[len("colour_start|"):] + "]]")
                elif mark == "colour_end":
                    out.append("[[/]]")
                i = close + 1
                continue
        if c == "%" and i + 1 < len(fmt):
            kind = fmt[i + 1]
            if kind == "%":
                out.append("%")
                i += 2
                continue
            if kind in "sd":
                out.append(str(args[n]) if n < len(args) else "")
                n += 1
                i += 2
                continue
        out.append(c)
        i += 1
    return " ".join("".join(out).split())


def tint(text, colour):
    return "[[%s]]%s[[/]]" % (colour, text)


def hero(name):
    return tint(name, "town_activity_log_character_name")


def building(name):
    return tint(name + ":", "town_activity_log_building_name")


def good(text):
    return tint(text, "town_activity_log_positive_result")


def bad(text):
    return tint(text, "town_activity_log_negative_result")


def names(kit, people):
    """ActivityLogText.Names."""
    link = " " + kit.string("str_and", "and") + " "
    parts = [hero(p) for p in people]
    return parts[0] if len(parts) == 1 else ", ".join(parts[:-1]) + link + parts[-1]


def rich_lines(kit, text, style, width, size=None):
    """Wraps text with [[colour]] spans: a list of lines, each a list of (word, colour)."""
    words, colour = [], None
    for piece in re.split(r"(\[\[[^\]]*\]\])", text):
        if piece.startswith("[[") and piece.endswith("]]"):
            colour = None if piece == "[[/]]" else piece[2:-2]
        else:
            # a word glued to the span before it stays glued ("Tavern:" + " " + ...)
            for j, chunk in enumerate(re.split(r"(\s+)", piece)):
                if chunk == "":
                    continue
                words.append((chunk, colour))
    lines, line, used = [], [], 0.0
    for chunk, col in words:
        if chunk.isspace():
            if line:
                line.append((" ", col))
                used += kit.text.width(" ", style, size)
            continue
        w = kit.text.width(chunk, style, size)
        if line and used + w > width:
            while line and line[-1][0] == " ":
                line.pop()
            lines.append(line)
            line, used = [], 0.0
        line.append((chunk, col))
        used += w
    if line:
        lines.append(line)
    return lines


def rich(kit, canvas, pos, text, style, width, height=None, base_colour=None, valign="top"):
    """Dd1Ui.Block for the log: wraps, colours the spans, shrinks to stay inside `height`. Returns the height used."""
    font = kit.text.font(style)
    size = font.native_size
    paragraphs = text.split("\n")
    while True:
        lines = []
        for paragraph in paragraphs:
            lines += rich_lines(kit, paragraph, style, width, size) or [[]]
        step = kit.text.line_height(style, size)
        if height is None or len(lines) * step <= height or size <= 13:
            break
        size -= 1
    y = pos[1]
    if height is not None and valign == "middle":
        y += max(0, (height - len(lines) * step) / 2)
    for i, line in enumerate(lines):
        x = pos[0]
        for chunk, colour in line:
            w = kit.text.width(chunk, style, size)
            if not chunk.isspace():
                kit.text.draw(canvas, (x, y + i * step), chunk, style, colour=colour or base_colour, size=size)
            x += w
    return len(lines) * step


def scroll_window(canvas, layer, window, offset=0):
    """Dd1Ui.ScrollList: the part of a list that shows through its window. window = (x, y, w, h) on the screen."""
    x, y, w, h = (int(round(v)) for v in window)
    piece = layer.crop((0, int(offset), w, int(offset) + h))
    canvas.alpha_composite(piece, (x, y))


def rail(kit, canvas, x, y, height, at=0.0, shown=True):
    """Dd1Ui.ScrollList's bar: DD1's rail stretched down the window, its pip where the list stands."""
    if not shown:
        return
    paste(canvas, kit.art("shared/widgets/scrollbarmid.png"), (x, y), (21, height))
    paste(canvas, kit.art("shared/widgets/scrollpip.png"), (x + (21 - 24) / 2, y + at * (height - 24)))


def name_at(kit, canvas, corner, name_pos, words, icon=None):
    """A screen's name as Dd1ScreenName draws it: the icon's corner at name_pos + icon_offset, the name's line from
    name_pos + text_offset.x with its foot on text_offset.y (DD1's own screens, measured)."""
    icon_at = kit.vec(NAME_LAYOUT, "name_layout", "icon_offset", 60, 26)                                              # FALLBACK
    text_at = kit.vec(NAME_LAYOUT, "name_layout", "text_offset", 192, 118)                                            # FALLBACK
    if icon:
        paste(canvas, kit.art(icon), (corner[0] + name_pos[0] + icon_at[0], corner[1] + name_pos[1] + icon_at[1]))
    kit.text.draw(canvas, (corner[0] + name_pos[0] + text_at[0], corner[1] + name_pos[1] + text_at[1] - kit.text.font("town_name").line_height), words, "town_name", max_width=320)


def bar_top(kit):
    return int(kit.vec(TOWN, "town_screen_layout", "estate_summary_pos", 0, 975)[1] + kit.vec(SUMMARY, "estate_summary_layout", "pos_offset", 0, -17)[1])


# ---------------------------------------------------------------- the bar's buttons (TownPanelButtons)

def bar_buttons(kit, canvas, open_=None, hover=None):
    bar = add(kit.vec(TOWN, "town_screen_layout", "estate_summary_pos", 0, 975), kit.vec(SUMMARY, "estate_summary_layout", "pos_offset", 0, -17))     # FALLBACK
    exchange = add(bar, kit.vec(SUMMARY, "estate_summary_layout", "heirloom_exchange_offset", 700, 51))
    first = add(bar, kit.vec(SUMMARY, "estate_summary_layout", "navigation_button_start_pos", 1800, 45))
    step = kit.vec(SUMMARY, "estate_summary_layout", "navigation_button_offset", -110, 0)
    spots = {}
    icon = kit.art("campaign/town/heirloom_exchange/" + ("he_icon_selected.png" if open_ == "exchange" else "he_icon_idle.png"))
    if icon is not None:                             # the numbers are a button's left edge and its middle line
        paste(canvas, icon, (exchange[0], exchange[1] - icon.height / 2))
        spots["exchange"] = (exchange[0], exchange[1] - icon.height / 2, icon.width, icon.height)
    row = [("log", "campaign/town/activity_log/activity_log.icon.png"), ("inventory", "campaign/town/realm_inventory/realm_inventory.icon.png")]
    for place, (key, rel) in enumerate(row):
        icon = kit.art(rel)
        if icon is None:
            continue
        left = (first[0] + step[0] * place, first[1] + step[1] * place)
        at = (left[0], left[1] - icon.height / 2)
        centre = (left[0] + icon.width / 2, left[1])
        paste(canvas, icon, at)
        spots[key] = (at[0], at[1], icon.width, icon.height)
        if hover == key:
            glow = kit.art("campaign/town/estate_summary/estate_summary.selected_overlay.png")
            if glow is not None:
                paste(canvas, glow, (centre[0] - glow.width / 2, centre[1] - glow.height / 2))
    if hover in spots:
        x, y, w, h = spots[hover]
        title = {"exchange": kit.string("town_name_heirloom_exchange", "Trade Heirlooms"), "log": kit.string("town_name_activity_log", "Activity Log"),
                 "inventory": kit.string("town_name_realm_inventory", "Trinket Inventory")}[hover]
        tooltip_up(kit, canvas, (x, y - 2), title, None)
    return spots


def tooltip_box(kit, title, body, width=300):
    """Dd1Tooltip.Fill: the box's lines and its size."""
    pad_x, pad_y = 18, 14
    lines = []
    if title:
        lines += [(w, "upgrade_tree_tooltip_title") for w in kit.text.wrap(title, "tooltip", width)]
    for paragraph in (body or "").split("\n") if body else []:
        colour = "neutral"
        if paragraph.startswith("!"):               # a line the plugin writes in DD1's warning red
            colour, paragraph = "harmful", paragraph[1:]
        elif paragraph.startswith("*"):             # ... in DD1's gold
            colour, paragraph = "notable", paragraph[1:]
        elif paragraph.startswith("~"):             # ... in DD1's building-info grey
            colour, paragraph = "town_building_info", paragraph[1:]
        lines += [(w, colour) for w in kit.text.wrap(paragraph, "tooltip", width)]
    step = kit.text.line_height("tooltip")
    text_w = max([kit.text.width(w, "tooltip") for w, _ in lines] or [0])
    return lines, (int(min(width, text_w) + 2 * pad_x) + 1, int(len(lines) * step + 2 * pad_y))


def tooltip(kit, canvas, top_left, title, body, width=300, right=None, bottom=None, grow_up=False):
    """Dd1Tooltip.ShowAt / Show (grow_up: top_left is the box's lower left corner) with its Place: the box keeps
    left of `right` and above `bottom`, and 6 px inside the screen."""
    lines, (w, h) = tooltip_box(kit, title, body, width)
    x, y = top_left[0], top_left[1] - (h if grow_up else 0)
    if right:
        x = min(x, right - w - 6)
    if bottom:
        y = min(y, bottom - h - 6)
    x, y = int(max(6, x)), int(max(6, y))
    art = kit.art("shared/tooltip/tooltip_background.png")
    if art is None:
        box(canvas, (x, y, w, h), (8, 7, 7, 242))
    else:
        e = int(round(art.width * kit.num("shared/tooltip/tooltip.layout.darkest", "tooltip_background_layout", "border_texture_threshold", 0.15)))
        xs, ys = (0, e, art.width - e, art.width), (0, e, art.height - e, art.height)
        dx, dy = (0, e, w - e, w), (0, e, h - e, h)
        for i in range(3):
            for j in range(3):
                piece = art.crop((xs[i], ys[j], xs[i + 1], ys[j + 1]))
                paste(canvas, piece, (x + dx[i], y + dy[j]), (dx[i + 1] - dx[i], dy[j + 1] - dy[j]))
    step = kit.text.line_height("tooltip")
    for i, (words, colour) in enumerate(lines):
        kit.text.draw(canvas, (x + 18, y + 14 + i * step), words, "tooltip", colour=colour)


def tooltip_up(kit, canvas, bottom_left, title, body, width=300):
    tooltip(kit, canvas, bottom_left, title, body, width, right=pw.SCREEN[0], bottom=pw.SCREEN[1], grow_up=True)


# ---------------------------------------------------------------- Trade Heirlooms (HeirloomExchangePanel)

def rates(kit):
    """Core/HeirloomExchange.cs: DD1's rates between the estate's four heirlooms, in the file's order."""
    path = os.path.join(kit.dd1, "campaign", "heirloom_exchange", "heirloom_exchange.json")
    with open(path, encoding="utf-8-sig") as f:
        data = json.load(f)
    return [(r["exchange_from_type"], r["exchange_from_amount"], r["exchange_to_type"], r["exchange_to_amount"]) for r in data["exchange_rates"]
            if r["exchange_from_type"] in HEIRLOOMS and r["exchange_to_type"] in HEIRLOOMS]


def exchange(kit, out, kind, amount, purse, name, tip=None):
    f = "campaign/town/heirloom_exchange/heirloom_exchange.layout.darkest"
    art = "campaign/town/heirloom_exchange/heirloom_exchange"
    canvas = kit.base_with(purse)
    bar_buttons(kit, canvas, open_="exchange")
    at = kit.vec(TOWN, "town_screen_layout", "heirloom_exchange_pos", 340, 708)                      # FALLBACK
    top = bar_top(kit)
    panel = Image.new("RGBA", (429, 268 + 200), (0, 0, 0, 0))        # the panel with room above it for nothing: it is cut at the bar
    c = Image.new("RGBA", pw.SCREEN, (0, 0, 0, 0))
    paste(c, kit.art(art + ".background.png"), at)
    main, frm, to = "heirloom_exchange_layout", "heirloom_exchange_heirloom_from_layout", "heirloom_exchange_heirloom_to_layout"
    title = add(at, kit.vec(f, main, "title_pos", 215, 24))
    kit.text.draw(c, title, kit.string("town_name_heirloom_exchange", "Trade Heirlooms"), "town_heirloom_exchange_title", anchor="m")

    # what is given: two columns, the heirloom and the number; offsets from a column's middle and top
    column = add(add(at, kit.vec(f, main, "heirloom_from_pos", 0, 0)), kit.vec(f, frm, "choice_start_offset", 79, 110))
    nxt = add(column, kit.vec(f, frm, "choice_spacing", 44, 0))
    up = kit.vec(f, frm, "arrow_up_offset", 0, -10)
    down = kit.vec(f, frm, "arrow_down_offset", 0, 84)
    icon = kit.vec(f, frm, "icon_offset", 0, 32)
    text = kit.vec(f, frm, "text_offset", 0, 36)
    held = purse[kind]
    steps = sorted({n for n in range(1, 400) if any(n % r[1] == 0 for r in rates(kit) if r[0] == kind)})
    smallest = steps[0]
    largest = max([n for n in steps if n <= held] or [smallest])
    for col, more, fewer in ((column, True, True), (nxt, amount < largest, amount > smallest)):
        paste(c, kit.art(art + ".arrow_up.png"), (col[0] + up[0] - 16, col[1] + up[1]), alpha=1.0 if more else 0.45)
        paste(c, kit.art(art + ".arrow_down.png"), (col[0] + down[0] - 16, col[1] + down[1]), alpha=1.0 if fewer else 0.45)
    paste(c, kit.art("shared/estate/currency.%s.icon.png" % kind), (column[0] + icon[0] - 20, column[1] + icon[1]))
    kit.text.draw(c, add(nxt, text), str(amount), "town_heirloom_exchange_from_amount", anchor="m", colour=(158, 148, 128) if amount > held else None)

    # what is taken: a framed row per rate; offsets from the middle of the icon and of the button
    rows = add(at, kit.vec(f, main, "heirloom_to_pos", 0, 0))
    start = add(rows, kit.vec(f, to, "choice_start_offset", 256, 75))
    pitch = kit.vec(f, to, "choice_spacing", 0, 44)
    link = add(rows, kit.vec(f, to, "arrow_offset", 148, 80))
    frame = kit.vec(f, to, "frame_offset", -32, -8)
    icon_at = kit.vec(f, to, "heirloom_icon_offset", 0, 20)
    takes_at = kit.vec(f, to, "heirloom_amount_offset", 52, 4)
    confirm = kit.vec(f, to, "confirm_offset", 112, 20)
    offers = [r for r in rates(kit) if r[0] == kind]
    for i, (_, give, other, take) in enumerate(offers):
        row = (start[0] + pitch[0] * i, start[1] + pitch[1] * i)
        valid = amount % give == 0 and amount <= held
        if i < 4:
            paste(c, kit.art(art + ".arrow_%d.png" % i), link)
        paste(c, kit.art(art + (".frame.png" if valid else ".frame_invalid.png")), add(row, frame))
        paste(c, kit.art("shared/estate/currency.%s.icon.png" % other), (row[0] + icon_at[0] - 20, row[1] + icon_at[1] - 20), tint=None if valid else (0.55,) * 3)
        kit.text.draw(c, add(row, takes_at), str(amount // give * take), "town_heirloom_exchange_to_amount", anchor="m", colour=None if valid else (158, 148, 128))
        if valid:
            paste(c, kit.art(art + ".confirm.png"), (row[0] + confirm[0] - 24, row[1] + confirm[1] - 12))
    del panel
    canvas.alpha_composite(c.crop((0, 0, pw.SCREEN[0], top)), (0, 0))       # DD1's bar covers what hangs below its top edge
    if tip:
        tooltip_up(kit, canvas, (at[0] + tip[0], at[1] - 2), tip[1], tip[2])
    path = os.path.join(out, name)
    canvas.convert("RGB").save(path)
    return path


# ---------------------------------------------------------------- Activity Log (ActivityLogPanel)

LOG_DIR = "campaign/town/activity_log/"
ENTRY_DIR = "activity_log/"
BOX = (600, 118)                                  # *_entry_backdrop.png
WEEK_BAR = (619, 136)                             # week_title_bar.png
BOX_INSET = 10                                    # MOD: the frame drawn into a backdrop
WEEK_LEAD, WEEK_STEP, WEEK_TEXT = 12, 112, (322, 67)      # MEASURED from week_title_bar.png


def log_sample(kit):
    """Weeks newest first; an entry is (kind, text, hero class, building)."""
    tavern, abbey, sanitarium = (kit.string("town_name_" + b, b.title()) for b in ("tavern", "abbey", "sanitarium"))
    coach, wagon, grave = kit.string("town_name_stage_coach", "Stage Coach"), kit.string("town_name_nomad_wagon", "Nomad Wagon"), "graveyard"
    party = ["Reynauld", "Dismas", "Paracelsus", "Junia"]
    head = "{colour_start|town_activity_log_building_name}%s:{colour_end} {colour_start|notable}%s{colour_end} "
    week12 = [
        ("note", story(kit, "str_town_event_started", "Town Event: %s", "A Ray of Sunlight"), None, None),
        ("hero", story(kit, "str_bar_stress_relief_story", head + "recovered %d stress.", tavern, "Dismas", 3) + "\n" + building(tavern) + " " + hero("Dismas")
         + " came away changed. New Quirk: " + bad("Tippler"), "highwayman", "tavern"),
        ("hero", story(kit, "str_meditation_stress_relief_story", head + "recovered %d stress.", abbey, "Junia", 2) + "\n"
         + story(kit, "str_meditation_activity_lock_story", head + "refuses to leave yet.", abbey, "Junia"), "vestal", "abbey"),
        ("hero", story(kit, "str_treatment_remove_negative_quirk", head + "underwent effective quirk treatment. Quirk Removed: %s", sanitarium, "Paracelsus", bad("Known Cheat")),
         "plague_doctor", "sanitarium"),
        ("level", story(kit, "str_is_now_a", "%s is now %s %s (Lvl. %d).", "Reynauld", kit.string("str_resolve_3", "Veteran"), "Crusader", 3), "crusader", None),
        ("building", story(kit, "str_stage_coach_rostersize_upgrade_lvl_2", "The roster size is increased. Level: %d", 2), None, "stage_coach"),
        ("hero", building(coach) + " " + hero("Boudica") + " the Hellion, Ravager has joined the estate.", "hellion", "stage_coach"),
        ("note", building(wagon) + " " + good("Ancestor's Pistol") + " bought for 150 gold.", None, "nomad_wagon"),
        ("note", building(kit.string("town_name_heirloom_exchange", "Trade Heirlooms")) + " 6 Busts for " + good("9 Crests") + ".", None, None),
    ]
    tail = lambda d, l: (kit.string("str_difficulty_%d" % d, "Lvl. %d" % d), kit.string("town_quest_length_%d" % l, "Short"))
    week11 = [
        ("note", story(kit, "str_embarked_on_explore_crypts", "%s set forth to explore the Ruins. (%s %s)", names(kit, party), *tail(1, 2)), None, "crypts"),
        ("hero", hero("Baldwin") + " " + " ".join(story(kit, "str_perished", "%s met their final fate during the quest.", "").split()) + " "
         + bad("(Ruins, slain by a vile Bone Rabble)"), "leper", grave),
        ("success", story(kit, "str_returned_from_explore_crypts_success", "%s were successful in exploring the Ruins. (%s %s)", names(kit, party), *tail(1, 2)), None, "crypts"),
        ("note", "Brought home: " + good("38 gold") + ", " + good("4 Busts") + ", " + good("2 Deeds") + " and the trinket " + good("Hint of Home") + ".", None, None),
        ("region", story(kit, "str_crypts_level_1_unlocked", "%s: Advanced to Mastery 1.", "Ruins"), None, "crypts"),
    ]
    week10 = [
        ("darkest", names(kit, party) + " set out: " + good("What I Would Not See") + ", Darkest Dungeon. (Lvl. 6 Medium)", None, "darkestdungeon"),
        ("abandon", "", None, "darkestdungeon"),
        ("darkest", names(kit, party[:3]) + " turned back: " + good("What I Would Not See") + ", Darkest Dungeon. (Lvl. 6 Medium)", None, "darkestdungeon"),
        ("failure", story(kit, "str_returned_from_cleanse_weald_failure", "%s did not measure up. (%s %s)", names(kit, ["Audrey", "Tardif"]), *tail(3, 1)), None, "weald"),
    ]
    return [(12, week12), (11, week11), (10, week10), (9, [])]


def log_note(kit, layer, y, x0, text, width, gap):
    return y + max(25, rich(kit, layer, (x0 + BOX_INSET + 2, y), text, "town_activity_log_entry", width))


def log_boxed(kit, layer, y, x0, entry, backdrop):
    kind, text, cls, where = entry
    corner = (x0 + (WEEK_BAR[0] - BOX[0]) / 2, y)
    paste(layer, kit.art(ENTRY_DIR + backdrop), corner)
    x = corner[0] + BOX_INSET
    if kind == "darkest":
        paste(layer, kit.art(ENTRY_DIR + "darkest_dungeon_log_icon.png"), (x + 6, y + (BOX[1] - 85) / 2))
        x += 6 + 85 + 10
    elif where and kind != "region":
        sign = kit.art(pw.BUILDINGS_DIR + "%s/%s.icon_roster.png" % (where, where))
        small = sign is not None
        if not small:
            sign = kit.art(pw.BUILDINGS_DIR + "%s/%s.icon.png" % (where, where))
        if sign is not None:
            paste(layer, sign, (x, y + (22 if small else (BOX[1] - 64) / 2)), (64, 64))
            x += (49 if small else 64) + 6                # the roster signs are drawn into the left 49 of their 64 pixels
    if cls:
        pw.slot(kit, layer, (x + 10, y + (BOX[1] - pw.SLOT) / 2), cls)
        x += 10 + pw.SLOT + 10
    else:
        x += 8
    width = corner[0] + BOX[0] - BOX_INSET - 6 - x
    rich(kit, layer, (x, y + BOX_INSET), text, "town_activity_log_entry", width, BOX[1] - 2 * BOX_INSET, valign="middle")
    return y + BOX[1]


def activity_log(kit, out, name, scrolled=0):
    f = LOG_DIR + "activity_log.layout.darkest"
    canvas = kit.base()
    top = bar_top(kit)
    box(canvas, (0, 0, ROOM_W, top), (0, 0, 0, int(0.72 * 255)))
    bar_buttons(kit, canvas, open_="log")
    at = kit.vec(TOWN, "town_screen_layout", "activity_log_pos", 144, 132)                           # FALLBACK
    paste(canvas, kit.art(LOG_DIR + "activitylog_bg.png"), at)
    name_at(kit, canvas, at, kit.vec(f, "activity_log_layout", "name_pos", -46, -12), kit.string("town_name_activity_log", "Activity Log"), "campaign/town/activity_log/activity_log.icon.png")
    name_at(kit, canvas, at, kit.vec(f, "caretaker_goal_layout", "name_pos", 650, -12), kit.string("str_caretaker_goals_heading", "Caretaker Goals"), "campaign/town/activity_log/caretaker_goals.icon.png")
    paste(canvas, kit.art("shared/progression/progression_close.png"), add(at, kit.vec(f, "activity_log_layout", "close_pos", 1352, 12)))

    panel = add(at, kit.vec(f, "activity_log_entry_layout", "text_pos", 25, 160))
    size = kit.vec(f, "activity_log_layout", "panel_size", 620, 550)
    start = kit.vec(f, "activity_log_layout", "entry_start_pos", 0, 10)
    line_gap = max(0, kit.vec(f, "activity_log_layout", "entry_spacing", 0, 30)[1] - 25)
    gap = kit.num(f, "activity_log_entry_layout", "vertical_spacing", 20)
    arrows = kit.vec(f, "activity_log_layout", "scroll_arrows_offset", 15, 7)
    width = size[0] - 2 * BOX_INSET
    layer = Image.new("RGBA", (int(size[0]), 4000), (0, 0, 0, 0))
    y = start[1]
    for week, entries in log_sample(kit):
        paste(layer, kit.art(ENTRY_DIR + "week_title_bar.png"), (start[0], y - WEEK_LEAD))
        kit.text.draw(layer, (start[0] + WEEK_TEXT[0], y - WEEK_LEAD + WEEK_TEXT[1] - 32), kit.string("str_week", "Week %d") % week, "town_activity_log_week_title",
                      anchor="m", max_width=236)
        y += WEEK_STEP
        if not entries:
            y = log_note(kit, layer, y, start[0], tint("Nothing on record yet.", "town_building_info"), width, gap) + gap
            continue
        before = None
        for entry in entries:
            kind = entry[0]
            if before is not None:
                y += line_gap if kind == "note" and before == "note" else gap
            if kind in ("success", "failure", "abandon"):
                paste(layer, kit.art(ENTRY_DIR + "raid_%s_banner.png" % kind), (start[0], y))
                y += 58
                if entry[1]:
                    y = log_note(kit, layer, y + line_gap, start[0], entry[1], width, gap)
            elif kind == "note":
                y = log_note(kit, layer, y, start[0], entry[1], width, gap)
            else:
                backdrop = {"hero": "hero_activity_entry_backdrop.png", "level": "hero_level_up_entry_backdrop.png", "building": "building_upgrade_entry_backdrop.png",
                            "region": "dungeon_unlocked_entry_backdrop.png", "darkest": "darkest_dungeon_entry_backdrop.png"}[kind]
                y = log_boxed(kit, layer, y, start[0], entry, backdrop)
            before = kind
        y += gap
    total = y
    offset = min(max(0, total - size[1]), scrolled)
    scroll_window(canvas, layer, (panel[0], panel[1], size[0], size[1]), offset)
    rail_x = panel[0] + size[0] + kit.num(f, "activity_log_layout", "scrollbar_offset", 0)
    rail(kit, canvas, rail_x, panel[1], int(size[1]), at=offset / max(1.0, total - size[1]), shown=total > size[1])
    arrow_x = rail_x + (21 - 41) / 2
    paste(canvas, kit.art("shared/widgets/scrollbar_uparrow.png"), (arrow_x, panel[1] - 25 - arrows[1]))
    paste(canvas, kit.art("shared/widgets/scrollbar_downarrow.png"), (arrow_x, panel[1] + size[1] + arrows[1]))

    # the caretaker's goals
    goal_pos = add(at, kit.vec(f, "caretaker_goal_layout", "text_pos", 720, 160))
    goal_size = kit.vec(f, "caretaker_goal_layout", "panel_size", 600, 240)
    spacing = kit.vec(f, "caretaker_goal_layout", "entry_spacing", 10, 10)
    glayer = Image.new("RGBA", (int(goal_size[0]), 1600), (0, 0, 0, 0))
    gy = 0
    goals = [(kit.string("str_caretaker_goals_quest_goals_heading", "Quest Goals"),
              [("Explore the upper Ruins and come back alive.", True), ("Find the Librarian in the Ruins and put out his fire.", True),
               ("Cut down the Mother of Threads in the Weald.", False), ("Bring the Pit-Master of the Warrens to his own hooks.", False)]),
             (kit.string("str_caretaker_goals_roster_goals_heading", "Roster Goals"),
              [((kit.string("str_caretaker_goal_hero_resolve", "Raise a %s to Resolve Level 6") or "") % c, d) for c, d in
               (("Crusader", False), ("Highwayman", False), ("Plague Doctor", True), ("Vestal", False))])]
    for heading, entries in goals:
        kit.text.draw(glayer, (spacing[0], gy), heading, "caretaker_goal_type_heading", max_width=goal_size[0] - 2 * spacing[0])
        gy += 64
        for words, done in entries:
            used = pw.block(kit, glayer, (spacing[0], gy), words, "caretaker_goal_entry", goal_size[0] - 2 * spacing[0], 400,
                            colour="caretaker_goal_completed" if done else "caretaker_goal_not_completed")
            gy += max(25, used) + spacing[1]
        gy += spacing[1]
    scroll_window(canvas, glayer, (goal_pos[0], goal_pos[1], goal_size[0], goal_size[1]))
    rail(kit, canvas, goal_pos[0] + goal_size[0] + kit.num(f, "caretaker_goal_layout", "scrollbar_offset", 0), goal_pos[1], int(goal_size[1]), shown=gy > goal_size[1])
    path = os.path.join(out, name)
    canvas.convert("RGB").save(path)
    return path


# ---------------------------------------------------------------- Trinket Inventory (RealmInventoryPanel)

INV_DIR = "campaign/town/realm_inventory/"
CARD = (72, 144)
TRINKET_ART = "panels/icons_equip/trinket/"


def card(kit, canvas, at, rarity, item, faded=False):
    tint_ = (0.4,) * 3 if faded else None
    paste(canvas, kit.art(TRINKET_ART + "rarity_%s.png" % rarity), at, tint=tint_)
    paste(canvas, kit.art(TRINKET_ART + "inv_trinket+%s.png" % item), at, tint=tint_)


def realm_inventory(kit, out, name, stored, hero=None, worn=(), order=None, tip=None, said=None, selling=None):
    f = INV_DIR + "realm_inventory.layout.darkest"
    main, grid = "realm_inventory_layout", "realm_inventory_grid_layout"
    canvas = kit.base()
    top = bar_top(kit)
    bar_buttons(kit, canvas, open_="inventory")
    at = kit.vec(TOWN, "town_screen_layout", "realm_inventory_pos", 881, 128)                        # FALLBACK

    # DD1's sheet stands beside the panel when a hero's sheet is open; the estate's sheet is DD2's own, with
    # DD1's Equipment on it (Estate/SheetEquipment.cs), and cannot be drawn offline: the panel is shown alone,
    # as it stands when it is opened from the bar. `hero` and `worn` only say whose words the tooltip speaks.

    # the panel
    paste(canvas, kit.art(INV_DIR + "realminv_bg.png"), at)
    content = add(at, kit.vec(f, main, "content_offset", 0, 2))
    name_at(kit, canvas, content, kit.vec(f, main, "name_pos", -44, -10), kit.string("town_name_realm_inventory", "Trinket Inventory"), "campaign/town/realm_inventory/realm_inventory.icon.png")
    paste(canvas, kit.art("shared/progression/progression_close.png"), add(content, kit.vec(f, main, "close_pos", 610, 22)))
    last = kit.vec(f, main, "sort_button_pos", 567, 22)
    step = kit.vec(f, main, "sort_button_spacing", 42, 0)
    buttons = [("name", "realm_inventory_sort_alphabetical.png"), ("class", "realm_inventory_sort_class.png"), ("rarity", "realm_inventory_sort_rarity.png"),
               (None, "realm_inventory_unequip_trinkets.png")]
    for i, (key, art_) in enumerate(buttons):                      # the row ends at sort_button_pos, left of the close button
        spot = add(content, (last[0] - step[0] * (3 - i), last[1] - step[1] * (3 - i)))
        paste(canvas, kit.art(INV_DIR + art_), spot)
        if key and order and order[0] == key:
            mark = kit.vec(f, main, "sort_button_current_descending_overlay_offset" if order[1] else "sort_button_current_ascending_overlay_offset", -8, 24 if order[1] else -8)
            paste(canvas, kit.art(INV_DIR + "realm_inventory_sort_current_overlay.png"), add(spot, mark))
    words = add(content, kit.vec(f, main, "trinket_info_description", 150, 100))
    if said:
        kit.text.draw(canvas, words, said[0], "realm_inventory_trinket_sell_description", colour="harmful" if said[1] else "neutral", max_width=667 - 150 - 24)
    elif selling:
        kit.text.draw(canvas, words, kit.string("realm_inventory_trinket_sell_description", "Sell trinket for:"), "realm_inventory_trinket_sell_description")
        value = add(content, kit.vec(f, main, "trinket_sell_value", 365, 116))
        est = "shared/estate/estate.layout.darkest"
        paste(canvas, kit.art("shared/estate/currency.gold.icon.png"), add(value, kit.vec(est, "estate_currency_gold_layout", "icon_offset", 0, -12)))
        kit.text.draw(canvas, add(value, kit.vec(est, "estate_currency_gold_layout", "number_offset", 25, -14)), str(selling), "town_currency_amount")
    else:
        kit.text.draw(canvas, words, kit.string("realm_inventory_trinket_sell_instruction", "Hold [SHIFT] to Sell Trinkets"), "realm_inventory_trinket_sell_description")

    grid_at = kit.vec(f, main, "inventory_grid_pos", 30, 195)
    grid_size = kit.vec(f, main, "inventory_grid_size", 560, 525)
    lift = kit.num(f, main, "grid_visuals_offset", -15)
    columns = int(kit.num(f, grid, "number_of_columns", 7))
    cell = kit.vec(f, grid, "start_pos", 0, 0)
    pitch = kit.vec(f, grid, "offset", 80, 160)
    bar_off = kit.vec(f, main, "scroll_bar_offset", 20, 0)
    window = ((667 - 628) / 2, grid_at[1] + lift)                   # as wide as a row rule, from the first rule down
    window_size = (628, grid_size[1] - lift)
    gx = grid_at[0] - window[0]
    filled = (len(stored) + columns - 1) // columns
    rows = max(4, filled)
    layer = Image.new("RGBA", (628, int(-lift + rows * pitch[1]) + 200), (0, 0, 0, 0))
    for row in range(rows + 1):
        y = -lift + row * pitch[1] + lift
        paste(layer, kit.art(INV_DIR + "realminventory_h_grid.png"), (0, y))
        if row < rows:
            paste(layer, kit.art(INV_DIR + "realminventory_v_grid.png"), (gx + cell[0] + CARD[0] + (pitch[0] - CARD[0]) / 2 - 2, y))
    spots = []
    for i, (rarity, item, refused) in enumerate(stored):
        spot = (gx + cell[0] + pitch[0] * (i % columns), -lift + cell[1] + pitch[1] * (i // columns))
        card(kit, layer, spot, rarity, item, faded=refused)
        spots.append(spot)
    total = max(window_size[1], -lift + filled * pitch[1])        # three rows and the head of a fourth show: no scrolling until a fourth is filled
    scroll_window(canvas, layer, (content[0] + window[0], content[1] + window[1], window_size[0], window_size[1]))
    rail_x = content[0] + grid_at[0] + grid_size[0] + bar_off[0]
    rail(kit, canvas, rail_x, content[1] + window[1], int(window_size[1]), shown=total > window_size[1])
    middle = rail_x + 21 / 2
    if total > window_size[1]:
        paste(canvas, kit.art(INV_DIR + "realm_inventory_uparrow.png"), (middle - 31, content[1] + window[1] - 49 + 6))          # MOD: at the rail's ends
        paste(canvas, kit.art(INV_DIR + "realm_inventory_downarrow.png"), (middle - 31, content[1] + window[1] + window_size[1] - 6))
    if tip is not None:
        # a card's box stands beside the panel, level with the card
        if tip[0] is None:
            top_ = at[1]
        else:
            top_ = content[1] + window[1] + spots[tip[0]][1]
        tooltip(kit, canvas, (at[0], top_), tip[1], tip[2], right=at[0], bottom=top)
    path = os.path.join(out, name)
    canvas.convert("RGB").save(path)
    return path


# ---------------------------------------------------------------- main

class Kit(pw.Kit):
    def base_with(self, purse):
        """The hamlet with its chrome and another purse on the bar."""
        canvas, _ = pw.ph.compose(self.town, 1, set(), hover=False)
        pw.ph.draw_chrome(canvas, self.town, self.text, pw.ph.SAMPLE_HEROES[:8], 12, 5, purse)
        return canvas


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--dd1", default=os.environ.get("DD1_ROOT", r"E:\Steam\steamapps\common\DarkestDungeon"))
    ap.add_argument("--out", default=os.path.join(here, "..", "_lab", "preview"))
    ap.add_argument("--only", default="", help="comma separated: bar, exchange, log, inventory")
    args = ap.parse_args()
    if not os.path.isfile(os.path.join(args.dd1, "campaign", "town", "town_bg.png")):
        raise SystemExit("Darkest Dungeon (1) not found at %s (use --dd1 or DD1_ROOT)" % args.dd1)
    out = os.path.abspath(args.out)
    os.makedirs(out, exist_ok=True)
    kit = Kit(args.dd1)
    only = set(s for s in args.only.split(",") if s)
    written = []
    if not only or "bar" in only:
        canvas = kit.base()
        bar_buttons(kit, canvas, hover="log")
        path = os.path.join(out, "town_bar_buttons.png")
        canvas.convert("RGB").save(path)
        written.append(path)
    if not only or "exchange" in only:
        rich_purse = dict(gold=560, bust=13, portrait=4, deed=7, crest=74)
        written.append(exchange(kit, out, "bust", 6, rich_purse, "town_heirloom_exchange.png", tip=(224, "Crest", "6 Busts for 9 Crests.\n~Rate: 2 Busts for 3 Crests.")))
        written.append(exchange(kit, out, "bust", 4, rich_purse, "town_heirloom_exchange_uneven.png", tip=(224, "Portrait", "!1 Portrait cost 3 Busts: 4 does not divide.\n~Rate: 3 Busts for 1 Portrait.")))
        written.append(exchange(kit, out, "crest", 3, dict(gold=40, bust=0, portrait=1, deed=0, crest=2), "town_heirloom_exchange_poor.png"))
    if not only or "log" in only:
        written.append(activity_log(kit, out, "town_activity_log.png"))
        written.append(activity_log(kit, out, "town_activity_log_scrolled.png", scrolled=1010))
    if not only or "inventory" in only:
        stored = [("uncommon", "accuracy_stone", False), ("rare", "adamant", False), ("common", "agile_talon", False), ("very_rare", "abyssal_tome", True),
                  ("ancestral", "ancestors_pistol", False), ("common", "archers_ring", False), ("uncommon", "bloody_dice", False), ("rare", "blood_charm", True),
                  ("very_common", "bleed_stone", False), ("very_rare", "berserk_charm", False), ("uncommon", "beast_slayers_ring", False)]
        stored = [s for s in stored if kit.art(TRINKET_ART + "inv_trinket+%s.png" % s[1]) is not None] or stored[:3]
        hero = ("Reynauld", "crusader", "Crusader, Wanderer", 2, 3)
        written.append(realm_inventory(kit, out, "town_realm_inventory.png", stored, hero=hero, worn=[("rare", "adamant")], order=("rarity", False),
                                       tip=(8, "Bleed Stone", "*Common\nTrinket\n+20% Bleed chance\n+10% Bleed resistance\n~Click to put it on Reynauld.\n~Sell trinket for: 15 gold (Shift and click).")))
        written.append(realm_inventory(kit, out, "town_realm_inventory_refused.png", stored, hero=hero, worn=[("rare", "adamant")],
                                       tip=(3, "Abyssal Tome", "*Very Rare\nTrinket\n+25% damage against the marked\nPlague Doctor only\n!Plague Doctor only.\n~Sell trinket for: 75 gold (Shift and click).")))
        written.append(realm_inventory(kit, out, "town_realm_inventory_selling.png", stored * 3, hero=hero, worn=[("rare", "adamant"), ("common", "agile_talon")],
                                       order=("name", True), selling=45))
        written.append(realm_inventory(kit, out, "town_realm_inventory_empty.png", [], said=("Click a hero in the roster first: the sheet opens beside the trinkets.", True)))
    for path in written:
        print("wrote", path)


if __name__ == "__main__":
    main()
