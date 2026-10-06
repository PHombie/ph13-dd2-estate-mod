#!/usr/bin/env python3
"""DD1's Districts screen as the plugin lays it out, drawn offline from the player's own Darkest Dungeon (1)
install with DD1's fonts. Nothing from DD1 is copied into the repo; output goes to _lab/ (gitignored).

Reference for src/DD2Estate/Estate/DistrictsPanel.cs and DistrictText.cs (and the districts' button of
Districts.cs on the estate's bar): change one, change the other.

    python tools/preview_districts.py [--dd1 <install>] [--out <dir>]

Writes town_districts.png (the row's start, the Bank built, a rich estate), town_districts_far.png (the row's end,
three buildings built, a poor estate) and town_districts_locked.png (before "Cornerstones").

Everything is in DD1's 1920x1080 screen pixels, y down, art at its native size. A number marked FALLBACK is DD1's
own value of a layout entry, used when the file cannot be read; MOD marks what DD1's files have no number for;
MEASURED what was read off DD1's art. The buildings are posed by tools/preview_corridor.py's Spine reader, which
puts each picture where the plugin's SpineSkeleton.Parts has it.
"""
import argparse
import json
import os
import re

from PIL import Image

import preview_corridor as pc
import preview_windows as pw
from preview_windows import add, box, paste

FEATURE = "dlc/580100_crimson_court/features/districts/"          # DistrictRules.Feature
DIR = "campaign/town/district/"
LAYOUT = DIR + "district.layout.darkest"
TOWN = "campaign/town/town.layout.darkest"
SUMMARY = "campaign/town/estate_summary/estate_summary.layout.darkest"
WIDGETS = "shared/widgets/"
FRAME = (1395, 776)                              # district.background.png
ENTRY_FRAME, BUILT_FRAME = (330, 315), (330, 280)
BODY_END, BUILT_BODY_END = 222, 256              # MEASURED on entry_frame.png / entry_frame_purchased.png
NAME_WIDGET = (296, 82)                          # MOD: as tools/preview_town_panels.py
GREY = (102, 102, 102)                           # MOD: DD1's "darkness 0.4, saturation 0" of an unbuilt entry, read as 0.4 of white
SHADE = (0, 0, 0, 184)                           # HamletShade.Colour
# The hero classes DD2 fields (RosterLifecycle.AvailableClasses with every DLC), by DD1's ids, as DD2 names them.
CLASSES = [("abomination", "Abomination"), ("bounty_hunter", "Bounty Hunter"), ("crusader", "Crusader"), ("duelist", "Duelist"), ("flagellant", "Flagellant"),
           ("grave_robber", "Grave Robber"), ("hellion", "Hellion"), ("highwayman", "Highwayman"), ("jester", "Jester"), ("leper", "Leper"), ("man_at_arms", "Man-at-Arms"),
           ("occultist", "Occultist"), ("plague_doctor", "Plague Doctor"), ("runaway", "Runaway"), ("vestal", "Vestal")]
HERO_ROOTS = ["", "dlc/445700_musketeer/", "dlc/580100_crimson_court/features/flagellant/", "dlc/702540_shieldbreaker/", "dlc/4964110_fires_edge/features/duelist/",
              "dlc/4964110_fires_edge/features/runaway/"]             # CampingRules.Roots
SKILL_CHANCES = ("poison_chance", "bleed_chance", "debuff_chance", "stun_chance", "move_chance")
CAMP_STATS = {("combat_stat_add", "attack_rating"), ("combat_stat_add", "crit_chance"), ("combat_stat_add", "defense_rating"), ("combat_stat_add", "protection_rating"),
              ("combat_stat_add", "speed_rating"), ("combat_stat_multiply", "damage_low"), ("combat_stat_multiply", "damage_high"), ("combat_stat_multiply", "max_hp"),
              ("stress_dmg_received_percent", ""), ("hp_heal_received_percent", ""), ("damage_received_percent", "")}


# ---------------------------------------------------------------- DD1's data and words (DistrictRules, DistrictText)

def read_json(kit, rel):
    path = os.path.join(kit.dd1, *rel.split("/"))
    if not os.path.isfile(path):
        return None
    with open(path, encoding="utf-8-sig") as f:
        return json.load(f)


def cc_string(kit, key):
    """The Crimson Court's table, where the buildings' names and sentences are."""
    if not hasattr(kit, "_cc"):
        kit._cc = {}
        path = os.path.join(kit.dd1, "dlc", "580100_crimson_court", "localization", "CC.string_table.xml")
        if os.path.isfile(path):
            with open(path, encoding="utf-8", errors="replace") as f:
                text = f.read()
            start = text.find('<language id="english">')
            end = text.find("</language>", start)
            for key2, value in re.findall(r'<entry id="([^"]+)"><!\[CDATA\[(.*?)\]\]></entry>', text[start:end], re.S):
                kit._cc.setdefault(key2, " ".join(re.sub(r"\{[^}]*\}", "", value).split()))
    return kit.string(key) or kit._cc.get(key)


def tags_of(kit, cls):
    for root in HERO_ROOTS:
        path = os.path.join(kit.dd1, *(root + "heroes/%s/%s.info.darkest" % (cls, cls)).split("/"))
        if os.path.isfile(path):
            with open(path, encoding="utf-8-sig", errors="replace") as f:
                return re.findall(r'^tag:\s*\.id\s+"([^"]+)"', f.read(), re.M)
    return []


def classes_for(kit, tags):
    return [name for cls, name in CLASSES if any(t in tags_of(kit, cls) for t in tags)] if tags else []


def fmt(text, *args):
    """Dd1Strings.Format, as far as these sentences need it."""
    args = list(args)

    def fill(m):
        if m.group(0) == "%%":
            return "%"
        value = args.pop(0) if args else ""
        if m.group(0) == "%+d":
            return "%+d" % value
        return str(value)
    return re.sub(r"%\+d|%d|%s|%%", fill, text)


def number(value):
    return ("%.2f" % value).rstrip("0").rstrip(".")


def buff_line(kit, buff, for_everyone):
    """DistrictText.BuffLine: None for a buff the estate does not give."""
    stat, sub, amount = buff.get("stat_type", ""), buff.get("stat_sub_type", ""), buff.get("amount", 0)
    if buff.get("rule_type", "always") != "always":
        return None                                  # a buff under a rule is not given
    eat = stat == "hp_heal_received_percent" and sub == "eat"
    if eat and not for_everyone:
        return None
    if not (eat or stat == "scouting_chance" or stat == "hp_heal_percent" or stat in SKILL_CHANCES or stat == "resistance" or (stat, sub) in CAMP_STATS):
        return None
    key = stat + ("_" + sub if sub else "")
    if (stat, sub) == ("combat_stat_add", "attack_rating"):
        key = "combat_stat_multiply_damage_low"      # REMAPPING: accuracy is damage dealt
    value = amount if sub == "speed_rating" else amount * 100
    text = kit.string("buff_stat_tooltip_" + key)
    return fmt(text, int(round(value))) if text else "%+d %s" % (round(value), key)


def lines_of(kit, building, buffs):
    """DistrictText.Lines: (words, is a classes' heading)."""
    lines, named = [], None
    for effect in building.get("buff_list", []):
        own_text = cc_string(kit, "str_%s_buff_%s" % (building["name"], effect.get("name", "")))
        tags = effect.get("hero_type_tags", [])
        classes = classes_for(kit, tags)
        own, kind = [], effect.get("type")
        if kind == "DistrictPrestigeBuffData" and own_text:
            own.append(own_text)
        elif kind == "DistrictEstateBuffData":
            own.append(fmt(own_text or "Interest gained on saved gold: %d%% per week.", int(round(effect.get("weekly_gold_interest", 0) * 100))))
        elif kind == "DistrictLightBuffData":
            if own_text:
                own.append(own_text)
            rules = read_json(kit, "shared/rules.json") or {}

            def full_light(table):
                rows = [r for r in (table or {}).get("range_table", []) if r["range"]["lower"] < 100 <= r["range"]["upper"]]
                return rows[0]["value"].get("player_scouting_increase", 0) if rows else 0
            gain = full_light(effect.get("darkness")) - full_light(rules.get("darkness"))
            if abs(gain) > 0.001:
                own.append(fmt((kit.string("buff_stat_tooltip_scouting_chance") or "%+d%% Scouting Chance").replace("%+d", "%s"), ("+" if gain >= 0 else "") + number(gain)) + " by torchlight")
        elif kind == "DistrictSupplyBuffData" and effect.get("item_type") == "provision":
            own.append(own_text or "Some food is granted for free each week.")
        elif kind == "DistrictRosterStressReliefBuffData":
            own.append(fmt((own_text or "Idle stress relief in town increased by %d per week.").replace("%d", "%s"), number(effect.get("stress_relief", 0) * 10 / 200)))
        elif kind == "DistrictHeroBuffData":
            for buff_id in effect.get("buff_list", []):
                text = buff_line(kit, buffs.get(buff_id, {"rule_type": "none"}), not tags)
                if text and text not in own:
                    own.append(text)
        elif kind == "DistrictCurioInteractionBuffData":
            dd1, points = number(effect.get("stress_heal", 0)), number(effect.get("stress_heal", 0) * 10 / 200)
            own.append(own_text.replace(dd1, points) if own_text and dd1 in own_text else ", ".join(effect.get("curio_tags", [])) + " curios heal " + points + " stress")
        elif kind == "DistrictCampingBuffData" and classes:
            either = classes[0] if len(classes) == 1 else ", ".join(classes[:-1]) + " or " + classes[-1]
            own.append(number(effect.get("camping_points", 0)) + " additional Respite Points if you have at least one " + either + " in your party.")
        if not own or (tags and not classes):
            continue
        heading = ", ".join(classes)
        if kind != "DistrictCampingBuffData" and heading and heading != named:
            lines.append((heading + ":", True))
            named = heading
        lines += [(text, False) for text in own]
    return lines


def price(building):
    """DistrictRules.Price: gold, heirlooms and the blueprint as DD1 writes them."""
    return [(cost["type"], cost["amount"]) for cost in building.get("currency_cost", [])]


# ---------------------------------------------------------------- the screen (DistrictsPanel)

def details(kit, canvas, pos, lines, width, height, built):
    """Dd1Ui.Block over lines of two colours: wraps at `width`, shrinks to stay inside `height`."""
    style = "town_district_entry_details"
    size = kit.text.font(style).native_size
    while True:
        rows = []
        for text, heading in lines:
            rows += [(w, heading) for w in (kit.text.wrap(text, style, width, size) or [""])]
        step = kit.text.line_height(style, size)
        if len(rows) * step <= height or size <= 11:
            break
        size -= 1
    for i, (text, heading) in enumerate(rows):
        colour = GREY if not built else "notable" if heading else "neutral"
        kit.text.draw(canvas, (pos[0], pos[1] + i * step), text, style, colour=colour, size=size)


def districts(kit, out, name, shift, built, purse, unlocked=True, hover=None):
    data = read_json(kit, FEATURE + "campaign/town/districts/districts_districts.json")
    if not data or not data.get("enabled", True):
        raise SystemExit("this DD1 install has no districts (%s)" % (FEATURE + "campaign/town/districts/districts_districts.json"))
    buff_file = read_json(kit, FEATURE + "shared/buffs/districts.buffs.json") or {}
    buffs = {b["id"]: b for b in buff_file.get("buffs", [])}
    buildings = sorted(enumerate(data["buildings"]), key=lambda p: (p[1].get("render_data", {}).get("town_priority", 0), p[0]))
    buildings = [b for _, b in buildings]

    main, entry = "district_layout", "district_entry_layout"
    at = kit.vec(TOWN, "town_background_layout", "area_pos", 144, 132)             # FALLBACK, as all below
    window = kit.vec(LAYOUT, main, "district_list_window_pos", 45, 100)
    window_w = kit.vec(LAYOUT, main, "district_list_window_size", 1308, 1000)[0]
    entry_w, gap = kit.num(LAYOUT, main, "entry_width", 350), kit.num(LAYOUT, main, "entry_spacing", 10)
    roof_speed, roof_y = kit.num(LAYOUT, main, "midground_speed", 0.5), kit.num(LAYOUT, main, "midground_y_offset", 65)
    bar_y = kit.num(LAYOUT, main, "district_list_scrollbar_offset", 380)
    building_y, frame_y = kit.num(LAYOUT, entry, "building_y_offset", 400), kit.num(LAYOUT, entry, "frame_y_offset", 400)
    title_y = kit.num(LAYOUT, entry, "title_y_offset", 13)
    desc = kit.vec(LAYOUT, entry, "desc_pos_offset", -144, 56)
    desc_w = kit.num(LAYOUT, entry, "desc_window_width", 280)
    cost_y = kit.num(LAYOUT, entry, "cost_start_y_offset", 235)
    cost_icon = kit.vec(LAYOUT, entry, "cost_item_icon_offset", -110, 0)
    cost_number = kit.vec(LAYOUT, entry, "cost_item_number_offset", 16, 4)
    cost_step = kit.num(LAYOUT, entry, "cost_item_entry_spacing", 100)
    buy_y = kit.num(LAYOUT, entry, "cost_checkmark_y_offset", 40)

    canvas = kit.base()
    top_of_bar = int(kit.vec(TOWN, "town_screen_layout", "estate_summary_pos", 0, 975)[1] + kit.vec(SUMMARY, "estate_summary_layout", "pos_offset", 0, -17)[1])
    box(canvas, (0, 0, pw.SCREEN[0], top_of_bar), SHADE)
    paste(canvas, kit.art(DIR + "district.background.png"), at)

    # the window on the row: everything in it is drawn on a layer and cut to the window
    height = int(frame_y + ENTRY_FRAME[1] + 4)
    total = len(buildings) * entry_w + max(0, len(buildings) - 1) * gap
    room = max(0, total - window_w)
    shift = max(0, min(shift, room))
    layer = Image.new("RGBA", (int(window_w) + 800, height), (0, 0, 0, 0))     # 400 px of margin on both sides for what is cut
    ox = 400
    roofs = kit.art(DIR + "district_midground.png")
    if roofs is not None:
        for i in range(int((window_w + room * roof_speed) // roofs.width) + 1):
            x = ox + i * roofs.width - shift * roof_speed
            if -roofs.width < x < layer.width:
                paste(layer, roofs, (max(0, x), roof_y)) if x >= 0 else layer.paste(roofs, (int(x), int(roof_y)), roofs)
    spots = {}
    for i, building in enumerate(buildings):
        left = i * (entry_w + gap) - shift
        if left + entry_w < -40 or left > window_w + 40:
            continue
        middle = ox + left + entry_w / 2
        is_built = building["name"] in built
        skel = FEATURE + building["render_data"]["sprite_paths"][0]
        prop = pc.Prop.load(kit.dd1, skel)
        if prop is not None:
            state = building["render_data"]["built_animation" if is_built else "not_built_animation"]
            pc.draw_pieces(layer, prop, prop.pose(state, 0.0), (middle, building_y))
        size = BUILT_FRAME if is_built else ENTRY_FRAME
        corner = (middle - size[0] / 2, frame_y)
        paste(layer, kit.art(DIR + ("entry_frame_purchased.png" if is_built else "entry_frame.png")), corner)
        if hover == building["name"]:
            paste(layer, kit.art(DIR + "entry_frame_selected.png"), corner)
        title = cc_string(kit, "str_%s_title" % building["name"]) or building["name"]
        kit.text.draw(layer, (middle, frame_y + title_y), title, "town_district_entry_title", colour="notable" if is_built else "neutral", anchor="m", max_width=size[0] - 24)
        details(kit, layer, (middle + desc[0], frame_y + desc[1]), lines_of(kit, building, buffs), desc_w, (BUILT_BODY_END if is_built else BODY_END) - desc[1] - 4, is_built)
        spots[building["name"]] = left
        if is_built:
            continue
        can_pay = unlocked
        for k, (currency, amount) in enumerate(price(building)):
            mid = middle + cost_icon[0] + cost_step * k
            rel = "shared/estate/currency.gold.icon.png" if currency == "gold" else FEATURE + "shared/estate/currency.blueprint.icon.png" if currency == "blueprint" \
                else "shared/estate/currency.%s.icon.png" % currency
            icon = kit.art(rel)
            if icon is not None:
                paste(layer, icon, (mid - icon.width / 2, frame_y + cost_y + cost_icon[1] + (40 - icon.height) / 2))
            enough = purse.get(currency, 0) >= amount
            can_pay = can_pay and enough
            # GoldPrice.Amount: thousands set apart, as DD1 writes an amount
            kit.text.draw(layer, (mid + cost_number[0], frame_y + cost_y + cost_icon[1] + cost_number[1]), "{:,}".format(amount), "town_district_cost_numbers", colour="notable" if enough else "harmful",
                          max_width=max(30, cost_step - cost_number[0] - 20))
        button = kit.art(DIR + "purchase_building_button.png")
        if button is not None:
            paste(layer, button, (middle - button.width / 2, frame_y + cost_y + buy_y), tint=None if can_pay else (0.4, 0.4, 0.4), alpha=1.0 if can_pay else 0.8)
    win = add(at, window)
    paste(canvas, layer.crop((ox, 0, ox + int(window_w), height)), win)

    blend = kit.art(DIR + "district_sideblend.png")
    if blend is not None:
        paste(canvas, blend, win)
        paste(canvas, blend.transpose(Image.FLIP_LEFT_RIGHT), (win[0] + window_w - blend.width, win[1]))

    # DD1's bar for a row that scrolls sideways, in the strip between the buildings' feet and their frames
    by = win[1] + bar_y
    arrow_l, arrow_r = kit.art(WIDGETS + "scrollbar_leftarrow.png"), kit.art(WIDGETS + "scrollbar_rightarrow.png")
    aw, ah = (arrow_l.size if arrow_l is not None else (25, 41))
    paste(canvas, arrow_l, (win[0], by + (21 - ah) / 2))
    paste(canvas, arrow_r, (win[0] + window_w - aw, by + (21 - ah) / 2))
    left, length, cap = win[0] + aw, window_w - 2 * aw, 12
    paste(canvas, kit.art(WIDGETS + "scrollbarhmid.png"), (left + cap, by), (length - 2 * cap, 21))
    paste(canvas, kit.art(WIDGETS + "scrollbarleft.png"), (left, by))
    paste(canvas, kit.art(WIDGETS + "scrollbarright.png"), (left + length - cap, by))
    pip = kit.art(WIDGETS + "scrollpip.png")
    if pip is not None and room > 0:
        span = length - 2 * cap
        handle = span * window_w / total             # Unity's Scrollbar: the handle is the window's share of the row
        centre = left + cap + (span - handle) * shift / room + handle / 2
        paste(canvas, pip, (centre - pip.width / 2, by + (21 - pip.height) / 2))

    # the screen's name between the backdrop's two rules, the way out, the blueprints held
    name_pos = kit.vec(LAYOUT, main, "name_pos", -46, -8)
    centre = (at[0] + name_pos[0] + NAME_WIDGET[0], at[1] + name_pos[1] + NAME_WIDGET[1])
    kit.text.draw(canvas, (centre[0], centre[1] - 32), kit.string("town_name_district", "Districts"), "town_name", anchor="m", max_width=296)
    paste(canvas, kit.art("shared/progression/progression_close.png"), add(at, kit.vec(LAYOUT, main, "close_pos", 1352, 12)))
    tally = add(at, kit.vec(LAYOUT, main, "currency_pos", 32, 130))
    icon = kit.art(FEATURE + "shared/estate/currency.blueprint.icon.png")
    if icon is not None:
        paste(canvas, icon, (tally[0] - icon.width / 2, tally[1]))
    kit.text.draw(canvas, add(tally, kit.vec(LAYOUT, main, "currency_number_offset", 20, 5)), str(purse.get("blueprint", 0)), "town_district_currency", colour="notable")

    path = os.path.join(out, name)
    canvas.convert("RGB").save(path)
    return path


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--dd1", default=os.environ.get("DD1_ROOT", r"E:\Steam\steamapps\common\DarkestDungeon"))
    ap.add_argument("--out", default=os.path.join(here, "..", "_lab", "preview"))
    args = ap.parse_args()
    if not os.path.isfile(os.path.join(args.dd1, "campaign", "town", "town_bg.png")):
        raise SystemExit("Darkest Dungeon (1) not found at %s (use --dd1 or DD1_ROOT)" % args.dd1)
    out = os.path.abspath(args.out)
    os.makedirs(out, exist_ok=True)
    kit = pw.Kit(args.dd1)
    rich = dict(gold=2400, blueprint=3, bust=113, portrait=102, deed=141, crest=874)
    poor = dict(gold=60, blueprint=1, bust=13, portrait=2, deed=41, crest=74)
    for path in (districts(kit, out, "town_districts.png", 0, {"bank"}, rich, hover="illuminators_guild"),
                 districts(kit, out, "town_districts_far.png", 10 ** 6, {"house_of_the_yellow_hand", "training_ring", "theater"}, poor),
                 districts(kit, out, "town_districts_locked.png", 1260, set(), dict(rich, blueprint=0), unlocked=False)):
        print("wrote", path)


if __name__ == "__main__":
    main()
