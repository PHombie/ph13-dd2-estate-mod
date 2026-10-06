#!/usr/bin/env python3
"""Compose the camp screen and the Survivalist from the player's own Darkest Dungeon (1) install.

The camp screen's layout is SUPERSEDED: it was rebuilt on DD1's raid panel (camping skills in the hero
banner, DD1 text styles on the two scrolls) and is drawn by tools/preview_raid_hud.py (raid_camp_*.png).
camp_meal.png and camp_respite.png here show the layout before that. The Survivalist part and --list are
current.

Reference for the layout numbers in src/DD2Estate/Dungeon/CampScreen.cs and src/DD2Estate/Estate/
SurvivalistPanel.cs, and for the rule arithmetic and the wording in Core/CampingRules.cs, Core/CampingBuffs.cs
(CampingBuffMap) and Dungeon/CampContent.cs (CampText): change one, change the other. Nothing from DD1 is
copied into the repo; output goes to _lab/ (gitignored).

    python tools/preview_camp.py [--dd1 <install>] [--out <dir>] [--cls crusader] [--list]

Writes camp_meal.png, camp_respite.png and survivalist.png (1920x1080). The DD2 rest stop (the game's "camp"
scene with the party's hero models), portraits and hero names are placeholders. --list prints every DD2
hero's camping skills as the Estate words them instead of drawing.

Camp screen (CampScreen.cs), over the dungeon HUD:
  * The top two thirds are DD2's rest stop. The party sits left of centre there, so DD1's scrolls go to the
    right: scrolls/meal_scroll.png (456x333) while the party has to eat, with the four meals as DD1 food cards
    where scripts/layout/screen.raid.darkest (meal_scroll: headerY 52, buttonY 148, buttonOffset 74) puts
    them; scrolls/event_scroll_campingrespite.png (456x237) after, with the title, the points, the text and
    the REST button at camp_layout's offsets (60,44 / 324,44 / 56,130 / 100,200).
  * The bottom third is the HUD: its hero plates stay (a click picks a hero or a companion), its middle and
    right columns are covered by the camp panel: the selected hero's ready skills as DD1 icons (72 px in the
    guild's 88 px frame, 108 apart) with the respite cost under each, what the pointed skill does, the log.

Survivalist (SurvivalistPanel.cs), in the building window:
  * campaign/town/buildings/camping_trainer/camping_trainer.layout.darkest places DD1's two grids: the class's
    own skills from 60,30 and the shared ones from 60,200, 110 apart, counted from the body point the guild's
    skill_pos is counted from (634,190 in the 1395x776 backdrop).
  * An unknown skill shows its price in gold (DD1's 1750, less the Bonfire's discount); a known
    one says "known", or "ready" inside a gold ring.
"""
import argparse
import glob
import json
import math
import os
import re

from PIL import Image, ImageDraw, ImageFont

SCREEN_W, SCREEN_H = 1920, 1080
DD1_STRESS_MAX, DD2_STRESS_MAX = 200.0, 10.0
CONDITIONAL_SHARE = 0.5              # CampingBuffMap.ConditionalShare

# ---- camp screen: keep in step with CampScreen.cs (screen pixels, y down) --------------------------------
HUD_H = 360                          # DungeonHud.PanelHeight
PLATE_X, PLATE_STEP, PLATE_Y = 40, 230, 28
PANEL_X = 950
SCROLL_POS = (1420, 40)
MEAL_SCROLL = (456, 333)
RESPITE_SCROLL = (456, 237)
NOTE_SIZE = (456, 96)
NOTE_GAP = 12
FRAME = 88
ICON_INSET = 8
SKILLS_POS = (20, 58)
SKILL_PITCH = 108
INFO_POS = (20, 196)
INFO_SIZE = (520, 152)
LOG_POS = (566, 16)
LOG_SIZE = (384, 330)
CARD = (72, 144)
MEAL_PITCH = 74
MEAL_Y = 148

# The survivalist's window is drawn by tools/preview_windows.py (SurvivalistPanel.cs, UpgradeUi.cs, RosterWindow.cs).


PARCHMENT = (222, 209, 178, 255)
GOLD = (219, 181, 92, 255)
DIM = (158, 148, 128, 255)
HUD_DIM = (138, 130, 115, 255)
WARN = (199, 51, 38, 255)
INK = (41, 28, 18, 255)
PANEL = (8, 8, 9, 255)

# CampingRules.Roots: the game and the DLC that carry camping skills, buffs or hero classes
ROOTS = ["", "dlc/445700_musketeer/", "dlc/580100_crimson_court/features/flagellant/", "dlc/702540_shieldbreaker/",
         "dlc/4964110_fires_edge/features/duelist/", "dlc/4964110_fires_edge/features/runaway/"]
ANALOGUES = {"runaway": "grave_robber", "duelist": "man_at_arms"}     # CampingRules.Analogues
DD2_HEROES = ["crusader", "highwayman", "plague_doctor", "occultist", "vestal", "jester", "leper", "hellion", "grave_robber",
              "man_at_arms", "flagellant", "abomination", "bounty_hunter", "runaway", "duelist"]
STRING_TABLES = ["localization/heroes.string_table.xml", "localization/miscellaneous.string_table.xml",
                 "dlc/580100_crimson_court/localization/CC.string_table.xml",
                 "dlc/702540_shieldbreaker/localization/shieldbreaker.string_table.xml",
                 "dlc/4964110_fires_edge/features/duelist/localization/duelist.string_table.xml",
                 "dlc/4964110_fires_edge/features/runaway/localization/runaway.string_table.xml"]


# ---------------------------------------------------------------- DD1 files (as Core/CampingRules.cs reads them)

def read_json(path):
    with open(path, encoding="utf-8-sig") as f:
        text = f.read()
    try:
        return json.loads(text)
    except ValueError:
        return json.loads(re.sub(r",(\s*[\]}])", r"\1", text))


class Rules:
    def __init__(self, dd1):
        self.dd1 = dd1
        raw = read_json(os.path.join(dd1, "shared", "rules.json"))
        self.points = raw.get("camp_start_camping_points", 12)
        self.ambush = raw.get("ambush_camping_base_chance", 0.33)
        self.meals = raw.get("meals_table", [])
        self.active_limit = max(4, raw.get("max_number_of_camping_skills", 0))
        self.buffs, self.skills, self.order = {}, {}, []
        self.threshold = 4
        for root in ROOTS:
            for path in sorted(glob.glob(os.path.join(dd1, root, "shared", "buffs", "*.buffs.json"))):
                for buff in read_json(path).get("buffs", []):
                    self.buffs.setdefault(buff["id"], buff)
        for root in ROOTS:
            folder = (root + "raid/camping").rstrip("/")
            for path in sorted(glob.glob(os.path.join(dd1, folder, "*.camping_skills.json"))):
                data = read_json(path)
                if root == "":
                    self.threshold = data.get("configuration", {}).get("class_specific_number_of_classes_threshold", 4)
                for skill in data.get("skills", []):
                    known = self.skills.get(skill["id"])
                    if known is None:
                        known = dict(skill, classes=set())
                        icon = "%s/skill_icons/camp_skill_%s.png" % (folder, skill["id"])
                        known["icon"] = icon if os.path.isfile(os.path.join(dd1, icon)) else "raid/camping/skill_icons/camp_skill_%s.png" % skill["id"]
                        known["price"] = sum(c["amount"] for r in skill.get("upgrade_requirements", []) for c in r.get("currency_cost", []) if c["type"] == "gold")
                        self.skills[skill["id"]] = known
                        self.order.append(skill["id"])
                    known["classes"].update(skill.get("hero_classes", []))
        self.strings = read_strings(dd1)
        match = re.search(r"(\d+)\s+Camping skills active", re.sub(r"\{[^}]*\}", "", self.strings.get("tutorial_popup_map_camping_skills_description", "")), re.I)
        if match:
            self.active_limit = int(match.group(1))

    def shared(self, skill):
        return len(skill["classes"]) >= self.threshold

    def class_skills(self, cls):
        own = [self.skills[i] for i in self.order if not self.shared(self.skills[i]) and cls in self.skills[i]["classes"]]
        if not own and cls in ANALOGUES:
            own = [self.skills[i] for i in self.order if not self.shared(self.skills[i]) and ANALOGUES[cls] in self.skills[i]["classes"]]
        return own

    def shared_skills(self):
        return [self.skills[i] for i in self.order if self.shared(self.skills[i])]

    def uses_analogue(self, cls):
        return cls in ANALOGUES and not any(not self.shared(s) and cls in s["classes"] for s in self.skills.values())

    def name(self, skill):
        text = self.strings.get("camping_skill_name_" + skill["id"]) or skill["id"].replace("_", " ")
        return " ".join(w[:1].upper() + w[1:] for w in text.lower().split(" "))

    def firewood(self, length):
        lists = read_json(os.path.join(self.dd1, "campaign", "provision", "provision.json")).get("raid_starting_length_inventory_item_lists", [])
        if not 0 <= length < len(lists):
            return 0
        return sum(e.get("amount", 0) for e in lists[length] if e.get("type") == "supply" and e.get("id") == "firewood")


def read_strings(dd1):
    wanted = ('"camping_skill_', "tutorial_popup_map_camping_skills_description", "str_meal_title_", "str_ui_meal_title", "camping_respite_", "town_name_",
              '"buff_stat_tooltip_', '"buff_rule_tooltip_')
    entry = re.compile(r'<entry id="([^"]+)"><!\[CDATA\[(.*?)\]\]></entry>')
    strings = {}
    for table in STRING_TABLES:
        path = os.path.join(dd1, *table.split("/"))
        if not os.path.isfile(path):
            continue
        english = False
        with open(path, encoding="utf-8", errors="replace") as f:
            for line in f:
                if "<language " in line:
                    if english:
                        break
                    english = 'id="english"' in line
                    continue
                if not english or not any(w in line for w in wanted):
                    continue
                match = entry.search(line)
                if match:
                    strings.setdefault(match.group(1), match.group(2))
    return strings


def parse_darkest(text):
    blocks, block, key = {}, None, None
    for raw in text.splitlines():
        for tok in raw.split("//")[0].split():
            if tok.endswith(":") and not tok.startswith("."):
                block = blocks.setdefault(tok[:-1], {})
                key = None
            elif tok.startswith(".") and len(tok) > 1 and (tok[1].isalpha() or tok[1] == "_"):
                key = tok[1:]
                if block is not None:
                    block[key] = []
            elif block is not None and key is not None:
                block[key].append(tok)
    return blocks


def layout_value(layout, block, key):
    values = layout.get(block, {}).get(key)
    if values and len(values) >= 2:
        return (float(values[0]), float(values[1]))
    return LAYOUT_DEFAULTS[(block, key)]


# ---------------------------------------------------------------- wording (as Dungeon/CampContent.cs, CampText)

def signed(value, digits=0):
    text = ("%.*f" % (digits, abs(value)))
    if digits:
        text = text.rstrip("0").rstrip(".")
    return ("+" if value >= 0 else "-") + text


def number(value):
    """CampText.Number: whole where it is, else with its decimals ("12.5")."""
    return ("%.2f" % value).rstrip("0").rstrip(".")


def stress(dd1_points):
    return number(dd1_points * DD2_STRESS_MAX / DD1_STRESS_MAX)


PLACE = re.compile(r"%([+-]?)(?:\.(\d))?([dsf%])")


def dd1_format(text, *values):
    """RaidText.Format, without the colours: DD1's printf places filled in order; a number written beforehand
    (a string at a number's place) goes in as it is."""
    values = list(values)

    def fill(m):
        if m.group(3) == "%":
            return "%"
        if not values:
            return ""
        value = values.pop(0)
        if m.group(3) == "s":
            return str(value)
        if isinstance(value, str):
            return "+" + value if m.group(1) == "+" and not value.startswith(("-", "+")) else value
        digits = int(m.group(2) or 0)
        written = "%.*f" % (digits, math.copysign(math.floor(abs(value) * 10 ** digits + 0.5) / 10 ** digits, value))
        return "+" + written if m.group(1) == "+" and value >= 0 else written

    text = PLACE.sub(fill, re.sub(r"\{\?[^}]*\}", "", text))
    return re.sub(r"\{colour_start\|[^}]+\}|\{colour_end\}", "", text)


def words(rules, key, fallback, *values):
    """CampText.Words: a DD1 line by id (the stock English beside it is a FALLBACK)."""
    return dd1_format(rules.strings.get(key) or fallback, *values)


RESISTANCES = {"bleed": "bleed", "poison": "blight", "disease": "disease", "move": "move", "debuff": "debuff", "stun": "stun", "death_blow": "death"}


def buff_lines(buff, amount, rank):
    """CampingBuffMap.Lines: (dd2 stat, sub, value)."""
    share = 1.0
    if buff.get("rule_type", "always") != "always":
        if buff["rule_type"] == "in_rank" and rank >= 0:
            share = 1.0 if (rank == round(buff.get("rule_data", {}).get("float", 0))) != buff.get("is_false_rule", False) else 0.0
        else:
            share = CONDITIONAL_SHARE
    value = amount * share
    stat, sub = buff.get("stat_type"), buff.get("stat_sub_type", "")
    lines = []
    if stat == "combat_stat_add":
        if sub == "attack_rating":
            lines.append(("health_damage_dealt_percent", None, value))
        elif sub == "crit_chance":
            lines.append(("crit_chance", None, value))
        elif sub in ("defense_rating", "protection_rating"):
            lines.append(("health_damage_received_percent", None, -value))
        elif sub == "speed_rating":
            lines.append(("speed", None, math.copysign(math.floor(abs(value) + 0.5), value)))
    elif stat == "combat_stat_multiply":
        if sub in ("damage_low", "damage_high"):
            lines.append(("health_damage_dealt_percent", None, value / 2))
        elif sub == "max_hp":
            lines.append(("health_max", None, value))
    elif stat == "stress_dmg_received_percent":
        lines.append(("resistance", "stress", -value))
    elif stat == "resistance" and sub in RESISTANCES:
        lines.append(("resistance", RESISTANCES[sub], value))
    elif stat == "hp_heal_received_percent":
        lines.append(("health_heal_received_percent", None, value))
    elif stat == "damage_received_percent":
        lines.append(("health_damage_received_percent", None, value))
    return [l for l in lines if abs(l[2]) > 1e-9]


def stat_text(rules, line):
    """CampText.Stat: a DD2 stat in DD1's words for the DD1 stat it stands for (buff_stat_tooltip_*)."""
    stat, sub, value = line
    percent = number(value * 100)
    if stat == "health_damage_dealt_percent":
        return words(rules, "buff_stat_tooltip_combat_stat_multiply_damage_low", "%+d%% DMG", percent)
    if stat == "health_damage_received_percent":
        return words(rules, "buff_stat_tooltip_damage_received_percent", "%+d%% DMG Taken", percent)
    if stat == "crit_chance":
        return words(rules, "buff_stat_tooltip_combat_stat_add_crit_chance", "%+d%% CRT", percent)
    if stat == "speed":
        return words(rules, "buff_stat_tooltip_combat_stat_add_speed_rating", "%+d SPD", number(value))
    if stat == "health_max":
        return words(rules, "buff_stat_tooltip_combat_stat_multiply_max_hp", "%+d%% MAX HP", percent)
    if stat == "health_heal_received_percent":
        return words(rules, "buff_stat_tooltip_hp_heal_received_percent", "%+d%% Healing Received", percent)
    if sub == "stress":
        return words(rules, "buff_stat_tooltip_stress_dmg_received_percent", "%+d%% Stress", number(-value * 100))
    dd1 = {"blight": "poison", "death": "death_blow"}.get(sub, sub)
    return words(rules, "buff_stat_tooltip_resistance_" + dd1, "%+d%% " + str(sub).title() + " Resist", percent)


DD2_EFFECTS = {"rw_play_with_fire_burn_on_hit": "runaway_firestarter_burn_buff"}      # CampingBuffMap.Dd2Effects
DD2_EFFECT_TEXT = {"runaway_firestarter_burn_buff": "blows set the target alight"}


def shown_rank(buff):
    return (1 if buff.get("is_false_rule") else 0) if buff.get("rule_type") == "in_rank" else -1


def rule_key(buff):
    """CampText.RuleKey: buffs under the same rule are worded together."""
    rule = buff.get("rule_type", "always")
    if rule == "always":
        return ""
    data = buff.get("rule_data", {})
    return rule + ("!" if buff.get("is_false_rule") else "") + number(data.get("float", 0)) + data.get("string", "")


def ruled(rules, buff, text):
    """CampText.Ruled: DD1's words for the rank rule, the one rule the Estate asks; a buff under any other
    rule is given at half its amount, always, and is worded as the plain buff of that half."""
    if buff.get("rule_type") != "in_rank":
        return text
    position = int(round(buff.get("rule_data", {}).get("float", 0))) + 1
    if buff.get("is_false_rule"):
        return words(rules, "buff_rule_tooltip_in_rank_false", "%s if not in position %d", text, position)
    return words(rules, "buff_rule_tooltip_in_rank", "%s if in position %d", text, position)


def stats_text(rules, lines, buff):
    sums = []
    for stat, sub, value in lines:
        for entry in sums:
            if entry[0] == stat and entry[1] == sub:
                entry[2] += value
                break
        else:
            sums.append([stat, sub, value])
    return ", ".join(ruled(rules, buff, stat_text(rules, s)) for s in sums if abs(s[2]) > 1e-9)


def buff_kind(rules, effect):
    buff = rules.buffs.get(effect.get("sub_type", ""))
    if buff is None:
        return "none"
    if buff["id"] in DD2_EFFECTS:
        return "effect"
    stat = buff.get("stat_type")
    if stat in ("scouting_chance", "party_surprise_chance", "monsters_surprise_chance"):
        return stat
    return "stat" if buff_lines(buff, 1, -1) else "none"


def buff_text(rules, effect):
    buff = rules.buffs.get(effect.get("sub_type", ""))
    amount = effect["amount"]
    kind = buff_kind(rules, effect)
    if kind == "scouting_chance":
        return words(rules, "buff_stat_tooltip_scouting_chance", "%+d%% Scouting Chance", number(amount * 100))
    if kind == "party_surprise_chance":
        return words(rules, "buff_stat_tooltip_party_surprise_chance", "%+d%% Chance Party Surprised", number(amount * 100))
    if kind == "monsters_surprise_chance":
        return words(rules, "buff_stat_tooltip_monsters_surprise_chance", "%+d%% Chance Monsters Surprised", number(amount * 100))
    if kind == "effect":
        return DD2_EFFECT_TEXT.get(DD2_EFFECTS[buff["id"]], "DD2 effect " + DD2_EFFECTS[buff["id"]])
    if kind == "none":
        return "(an on-hit effect: no DD2 counterpart)" if buff and buff.get("attack_additional_effect_ids") else "(no DD2 counterpart)"
    return stats_text(rules, buff_lines(buff, amount, shown_rank(buff)), buff)


def loot_text(rules, effect):
    table = effect.get("sub_type", "")
    trinket = table.startswith("T_")
    text = words(rules, "camping_skill_effect_loot_" + table,
                 "Produces a random trinket" if trinket else "Produce a Skeleton Key" if table == "KEYONLY" else "Produces a random supply item")
    return text + " (trinkets are not carried yet)" if trinket else text


def effect_text(rules, effect):
    """CampText.Effect: DD1's words (camping_skill_effect_*) with the Estate's numbers."""
    kind, amount = effect["type"], effect["amount"]
    text = {
        "stress_heal_amount": lambda: words(rules, "camping_skill_effect_stress_heal_amount", "-%d Stress", stress(amount)),
        "stress_damage_amount": lambda: words(rules, "camping_skill_effect_stress_damage_amount", "%+d Stress", stress(amount)),
        "health_heal_max_health_percent": lambda: words(rules, "camping_skill_effect_health_heal_max_health_percent", "Heal %d%% HP", number(amount * 100)),
        "health_damage_max_health_percent": lambda: words(rules, "camping_skill_effect_health_damage_max_health_percent", "Suffer %d%% HP DMG", number(amount * 100)),
        "remove_bleeding": lambda: words(rules, "camping_skill_effect_remove_bleeding", "Remove Bleeding"),
        "remove_poison": lambda: words(rules, "camping_skill_effect_remove_poison", "Remove Blight"),
        "remove_disease": lambda: words(rules, "camping_skill_effect_remove_disease", "Remove Disease"),
        "remove_deaths_door_recovery_buffs": lambda: words(rules, "camping_skill_effect_remove_deaths_door_recovery_buffs", "Remove Mortality debuffs"),
        "reduce_ambush_chance": lambda: (words(rules, "camping_skill_effect_reduce_ambush_chance", "Prevents nighttime ambush") if amount >= rules.ambush
                                         else "Nighttime ambush " + signed(-amount * 100) + "%"),
        "reduce_torch": lambda: words(rules, "camping_skill_effect_reduce_torch", "Reduce torchlight by %d", number(amount)),
        "refresh_camp_skill_uses": lambda: words(rules, "camping_skill_effect_refresh_camp_skill_uses", "Refresh Camping Skill Uses"),
        "loot": lambda: loot_text(rules, effect),
        "buff": lambda: buff_text(rules, effect),
    }.get(kind, lambda: kind.replace("_", " "))()
    chance = effect.get("chance", {}).get("amount", 1)
    if chance < 0.999:
        text = words(rules, "camping_skill_chance_effect_format", "(%d%% chance) %s", round(chance * 100), text)
    for requirement in effect.get("requirements", []):
        text = words(rules, "camping_skill_requirement_effect_format", "If %s: %s", words(rules, "camping_skill_requirement_" + requirement, requirement.replace("_", " ")), text)
    return text


def skill_lines(rules, skill):
    """CampText.Lines: DD1's words for whom an effect lands on, then its effects; unconditional stat buffs
    under one DD1 rule are summed into one entry."""
    lines = []
    for selection, who in (("self", "Self Only:"), ("individual", "One Companion:"), ("party_other", "All Companions:"), ("party", "Party:")):
        parts, groups = [], []
        for effect in skill["effects"]:
            if effect["selection"] != selection:
                continue
            summed = (effect["type"] == "buff" and effect.get("chance", {}).get("amount", 1) >= 0.999
                      and not effect.get("requirements") and buff_kind(rules, effect) == "stat")
            if not summed:
                parts.append(effect_text(rules, effect))
                continue
            buff = rules.buffs[effect["sub_type"]]
            rule = rule_key(buff)
            group = next((g for g in groups if g["rule"] == rule), None)
            if group is None:
                group = {"rule": rule, "buff": buff, "slot": len(parts), "lines": []}
                groups.append(group)
                parts.append(None)
            group["lines"].extend(buff_lines(buff, effect["amount"], shown_rank(buff)))
        for group in groups:
            parts[group["slot"]] = stats_text(rules, group["lines"], group["buff"])
        parts = [p for p in parts if p]
        if parts:
            lines.append(words(rules, "camping_skill_selection_" + selection, who) + " " + ", ".join(parts))
    return lines


def meal_effect(meal):
    parts = []
    if meal["healing"]:
        parts.append(signed(meal["healing"] * 100) + "% health")
    if meal["stress"]:
        parts.append(signed(meal["stress"] * DD2_STRESS_MAX / DD1_STRESS_MAX, 2) + " stress")
    return ", ".join(parts) or "no effect"


def food_for(meal, heroes):
    return int(math.ceil(meal["rations_per"] * heroes - 1e-9))


def price(dd1_gold, discount=0.0):
    """UpgradeRules.Price: DD1's price after the discount, halves up, never free."""
    return max(1, int(math.floor(dd1_gold * (1 - discount) + 0.5)))


# ---------------------------------------------------------------- drawing

def font(size, bold=True):
    for name in (("georgiab.ttf", "timesbd.ttf") if bold else ("georgia.ttf", "times.ttf")):
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


def paste(canvas, img, pos, size=None, dim=0.0):
    if img is None:
        return
    if size is not None and tuple(size) != img.size:
        img = img.resize((int(size[0]), int(size[1])), Image.LANCZOS)
    if dim > 0:
        alpha = img.getchannel("A")
        img = Image.blend(img, Image.new("RGBA", img.size, (0, 0, 0, 255)), dim)
        img.putalpha(alpha)
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
    for para in text.split("\n"):
        line = ""
        for word in para.split():
            trial = (line + " " + word).strip()
            if draw.textlength(trial, font=fnt) <= width or not line:
                line = trial
            else:
                lines.append(line)
                line = word
        lines.append(line)
    return lines


def paragraph(canvas, pos, size, text, fnt, colour, line_height):
    draw = ImageDraw.Draw(canvas)
    y = pos[1]
    for line in wrap(draw, text, fnt, size[0]):
        if y + line_height > pos[1] + size[1]:        # in game the text shrinks to fit instead
            break
        draw.text((pos[0], y), line, font=fnt, fill=colour, anchor="la")
        y += line_height


def skill_cell(canvas, dd1, skill, pos, dim=False, ring=False):
    if ring:
        fill(canvas, (pos[0] - 3, pos[1] - 3, pos[0] + FRAME + 2, pos[1] + FRAME + 2), GOLD)
    frame = load(dd1, "campaign/town/buildings/guild/skill_frame.png")
    if frame is not None:
        paste(canvas, frame, pos, (FRAME, FRAME))
    else:
        fill(canvas, (pos[0], pos[1], pos[0] + FRAME - 1, pos[1] + FRAME - 1), (26, 26, 26, 255))
    paste(canvas, load(dd1, skill["icon"]), add(pos, (ICON_INSET, ICON_INSET)), (FRAME - 2 * ICON_INSET, FRAME - 2 * ICON_INSET), 0.62 if dim else 0.0)


SAMPLE_PARTY = [("Audrey", "grave_robber"), ("Paracelsus", "plague_doctor"), ("Dismas", "highwayman"), ("Reynauld", "crusader")]


def camp_base(dd1):
    """The rest stop stands in as a night sky with four seats; the HUD's own parts as in preview_provision.py."""
    canvas = Image.new("RGBA", (SCREEN_W, SCREEN_H), (9, 12, 20, 255))
    draw = ImageDraw.Draw(canvas)
    top = SCREEN_H - HUD_H
    for y in range(top):
        shade = int(22 + 26 * y / top)
        draw.line((0, y, SCREEN_W, y), fill=(shade // 3, shade // 2, shade, 255))
    # CampView: the party sits between 16% and 65% of the width, the fire among them
    for share in (0.20, 0.34, 0.50, 0.62):
        x = int(SCREEN_W * share)
        draw.ellipse((x - 46, top - 300, x + 46, top - 208), fill=(52, 46, 44, 255))
        draw.rectangle((x - 60, top - 208, x + 60, top - 40), fill=(52, 46, 44, 255))
    draw.ellipse((SCREEN_W * 0.42 - 40, top - 120, SCREEN_W * 0.42 + 40, top - 40), fill=(214, 120, 40, 255))
    draw.text((SCREEN_W * 0.40, 60), "DD2 rest stop: scene \"camp\", the party's hero models (placeholder)", font=font(24, False), fill=DIM, anchor="mm")

    fill(canvas, (0, top, SCREEN_W, SCREEN_H), PANEL)
    fill(canvas, (0, top, SCREEN_W, top + 3), (89, 71, 41, 255))
    draw = ImageDraw.Draw(canvas)
    for i, (name, _) in enumerate(SAMPLE_PARTY):
        x, y = PLATE_X + i * PLATE_STEP, top + PLATE_Y
        fill(canvas, (x, y, x + 150, y + 150), (51, 51, 51, 255))
        draw = ImageDraw.Draw(canvas)
        draw.text((x, y + 173), name, font=font(26), fill=(255, 255, 255, 255) if i == 3 else GOLD, anchor="lm")
        for row, colour, share, text in ((198, (158, 20, 15, 255), 0.7, "24/34"), (232, (219, 219, 230, 255), 0.4, "4")):
            fill(canvas, (x, y + row, x + 150, y + row + 18), (31, 31, 31, 255))
            fill(canvas, (x, y + row, x + int(150 * share), y + row + 18), colour)
            ImageDraw.Draw(canvas).text((x + 158, y + row + 9), text, font=font(20), fill=PARCHMENT, anchor="lm")
    fill(canvas, (40, top + 304, 340, top + 348), (0, 0, 0, 150))
    ImageDraw.Draw(canvas).text((190, top + 326), "Abandon the Quest", font=font(22), fill=HUD_DIM, anchor="mm")
    return canvas


def camp_panel(canvas, dd1, rules, hero, cls, used, armed, hover, points, log, message=None):
    top = SCREEN_H - HUD_H + 3
    origin = (PANEL_X, top)
    draw = ImageDraw.Draw(canvas)
    draw.text(add(origin, (20, 12 + 19)), hero, font=font(28), fill=GOLD, anchor="lm")
    draw.text((origin[0] + 20 + draw.textlength(hero + "  ", font=font(28)), origin[1] + 12 + 21), "camping skills", font=font(20, False), fill=DIM, anchor="lm")
    ready = (rules.class_skills(cls) + rules.shared_skills())[:rules.active_limit]
    for i, skill in enumerate(ready):
        pos = add(origin, (SKILLS_POS[0] + i * SKILL_PITCH, SKILLS_POS[1]))
        spent = skill["id"] in used
        skill_cell(canvas, dd1, skill, pos, dim=spent or skill["cost"] > points, ring=skill["id"] == armed)
        text = "used" if spent else str(skill["cost"]) + ("  x%d" % skill["use_limit"] if skill["use_limit"] > 1 else "")
        ImageDraw.Draw(canvas).text((pos[0] + FRAME / 2, pos[1] + FRAME + 14), text, font=font(19),
                                    fill=HUD_DIM if spent else WARN if skill["cost"] > points else PARCHMENT, anchor="mm")
    info = add(origin, INFO_POS)
    if message:
        paragraph(canvas, info, INFO_SIZE, message, font(21), GOLD, 26)
    elif hover is not None:
        skill = rules.skills[hover]
        draw = ImageDraw.Draw(canvas)
        draw.text(info, rules.name(skill), font=font(21), fill=GOLD, anchor="la")
        draw.text((info[0] + draw.textlength(rules.name(skill) + "   ", font=font(21)), info[1] + 4), "%d respite" % skill["cost"], font=font(17, False), fill=PARCHMENT, anchor="la")
        paragraph(canvas, (info[0], info[1] + 28), (INFO_SIZE[0], INFO_SIZE[1] - 28), "\n".join(skill_lines(rules, skill)), font(18, False), PARCHMENT, 22)
    lines = []
    draw = ImageDraw.Draw(canvas)
    for entry in log:
        lines.extend(wrap(draw, entry, font(18, False), LOG_SIZE[0]))
    for n, line in enumerate(lines[-14:]):
        draw.text((origin[0] + LOG_POS[0], origin[1] + LOG_POS[1] + n * 22), line, font=font(18, False), fill=HUD_DIM, anchor="la")


def items_food(dd1):
    """The food card's stack limit and how many pictures DD1 has for it (ItemCatalog)."""
    stack = 12
    for name in sorted(glob.glob(os.path.join(dd1, "inventory", "*.inventory.items.darkest"))):
        with open(name, encoding="utf-8-sig") as f:
            for line in f:
                if '.type "provision"' in line:
                    match = re.search(r"\.base_stack_limit\s+(\d+)", line)
                    if match:
                        stack = int(match.group(1))
    variants = len([f for f in glob.glob(os.path.join(dd1, "panels", "icons_equip", "provision", "inv_provision+_*.png"))
                    if os.path.basename(f)[len("inv_provision+_"):-4].isdigit()])
    return stack, variants


def compose_meal(dd1, out_dir, rules):
    canvas = camp_base(dd1)
    party = len(SAMPLE_PARTY)
    carried = 6
    paste(canvas, load(dd1, "scrolls/meal_scroll.png"), SCROLL_POS)
    draw = ImageDraw.Draw(canvas)
    draw.text((SCROLL_POS[0] + MEAL_SCROLL[0] / 2, SCROLL_POS[1] + 52 + 20), rules.strings.get("str_ui_meal_title", "Repast"), font=font(34), fill=GOLD, anchor="mm")
    stack, variants = items_food(dd1)
    count = len(rules.meals)
    left = (MEAL_SCROLL[0] - ((count - 1) * MEAL_PITCH + CARD[0])) / 2
    for i, meal in enumerate(rules.meals):
        need = food_for(meal, party)
        can = need <= carried
        pos = (SCROLL_POS[0] + left + i * MEAL_PITCH, SCROLL_POS[1] + MEAL_Y)
        amount = stack * i // max(1, count - 1)
        index = min(variants - 1, max(0, amount) * variants // (stack + 1)) if variants else 0
        icon = load(dd1, "panels/icons_equip/provision/inv_provision+_%d.png" % index) if variants else load(dd1, "panels/icons_equip/provision/inv_provision+.png")
        paste(canvas, icon, pos, CARD, 0.7 if not can else 0.55 if need == 0 else 0.0)
        draw = ImageDraw.Draw(canvas)
        draw.text((pos[0] + 5, pos[1] + 2), str(need), font=font(20), fill=(255, 255, 255, 255) if can else WARN, anchor="la")
        draw.text((pos[0] + CARD[0] / 2, pos[1] + CARD[1] - 15), rules.strings.get("str_meal_title_%d" % i, meal["type"].title()), font=font(15), fill=PARCHMENT if can else HUD_DIM, anchor="mm")
    hovered = rules.meals[min(2, count - 1)]
    note = (SCROLL_POS[0], SCROLL_POS[1] + MEAL_SCROLL[1] + NOTE_GAP)
    fill(canvas, (note[0], note[1], note[0] + NOTE_SIZE[0], note[1] + NOTE_SIZE[1]), (8, 8, 9, 230))
    draw = ImageDraw.Draw(canvas)
    draw.text((note[0] + 12, note[1] + 8), rules.strings.get("str_meal_title_2", "Full"), font=font(20), fill=GOLD, anchor="la")
    draw.text((note[0] + 90, note[1] + 8), "%d food" % food_for(hovered, party), font=font(20, False), fill=PARCHMENT, anchor="la")
    draw.text((note[0] + 12, note[1] + 36), meal_effect(hovered) + " for every hero", font=font(20, False), fill=PARCHMENT, anchor="la")
    camp_panel(canvas, dd1, rules, "Reynauld", "crusader", set(), None, None, rules.points, ["The party makes camp.", "The fire is lit. First, the meal."],
               message="The party eats first: choose a meal on the scroll. %d food is in the bag." % carried)
    path = os.path.join(out_dir, "camp_meal.png")
    canvas.convert("RGB").save(path)
    return path


def compose_respite(dd1, out_dir, rules, cls):
    canvas = camp_base(dd1)
    own = rules.class_skills(cls) + rules.shared_skills()
    points = rules.points - 5
    paste(canvas, load(dd1, "scrolls/event_scroll_campingrespite.png"), SCROLL_POS)
    draw = ImageDraw.Draw(canvas)
    draw.text((SCROLL_POS[0] + 60, SCROLL_POS[1] + 44 + 26), rules.strings.get("camping_respite_title", "Respite"), font=font(34), fill=GOLD, anchor="lm")
    draw.text((SCROLL_POS[0] + 324 + 24, SCROLL_POS[1] + 44 + 26), str(points), font=font(46), fill=PARCHMENT, anchor="rm")
    paragraph(canvas, (SCROLL_POS[0] + 56, SCROLL_POS[1] + 130), (360, 64),
              rules.strings.get("camping_respite_description", "Use camping skills to bolster the party's body and mind."), font(19, False), PARCHMENT, 24)
    rx, ry = SCROLL_POS[0] + 100, SCROLL_POS[1] + 196
    fill(canvas, (rx, ry, rx + 256, ry + 40), (0, 0, 0, 150))
    ImageDraw.Draw(canvas).text((rx + 128, ry + 20), rules.strings.get("camping_respite_rest", "REST"), font=font(24), fill=PARCHMENT, anchor="mm")
    note = (SCROLL_POS[0], SCROLL_POS[1] + RESPITE_SCROLL[1] + NOTE_GAP)
    fill(canvas, (note[0], note[1], note[0] + NOTE_SIZE[0], note[1] + NOTE_SIZE[1]), (8, 8, 9, 230))
    ImageDraw.Draw(canvas).text((note[0] + 12, note[1] + 8), "A camp's buffs last 4 fights, or until the next camp.", font=font(19, False), fill=PARCHMENT, anchor="la")
    used = {own[0]["id"]} if own else set()
    hover = own[1]["id"] if len(own) > 1 else None
    camp_panel(canvas, dd1, rules, "Reynauld", cls, used, None, hover, points,
               ["The party makes camp.", "The fire is lit. First, the meal.", "The party eats 4 food (full rations): Audrey +3 health; Dismas +3 health.",
                "Reynauld: %s." % (rules.name(own[0]) if own else "?")])
    path = os.path.join(out_dir, "camp_respite.png")
    canvas.convert("RGB").save(path)
    return path


SAMPLE_HEROES = ["Dismas", "Reynauld", "Paracelsus", "Junia", "Barristan", "Audrey", "Boudica", "Sarmenti", "Baldwin"]


def compose_survivalist(dd1, out_dir, rules, cls):
    """The Survivalist's window: tools/preview_windows.py draws it (the frame every building window shares, DD1's
    hero banner and text column, the two skill grids); this entry point keeps the file name."""
    import preview_windows as pw
    return pw.survivalist(pw.Kit(dd1), out_dir, name="survivalist.png")


def list_skills(rules):
    print("respite %d, ambush %d%%, ready %d, firewood short/medium/long %d/%d/%d" % (
        rules.points, round(rules.ambush * 100), rules.active_limit, rules.firewood(1), rules.firewood(2), rules.firewood(3)))
    for meal in rules.meals:
        print("  meal %-6s %d food for four: %s" % (meal["type"], food_for(meal, 4), meal_effect(meal)))
    for cls in DD2_HEROES:
        note = " (by analogy: %s)" % ANALOGUES[cls] if rules.uses_analogue(cls) else ""
        print("\n%s%s" % (cls, note))
        for skill in rules.class_skills(cls) + rules.shared_skills():
            print("  %-22s %s, %d gold%s" % (rules.name(skill), words(rules, "camping_skill_cost", "Time Cost: %d", skill["cost"]).strip(), price(skill["price"]),
                                             "  x%d" % skill["use_limit"] if skill["use_limit"] > 1 else ""))
            for line in skill_lines(rules, skill):
                print("      " + line)


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--dd1", default=os.environ.get("DD1_ROOT", r"E:\Steam\steamapps\common\DarkestDungeon"))
    ap.add_argument("--out", default=os.path.join(here, "..", "_lab", "preview"))
    ap.add_argument("--cls", default="crusader", help="hero class whose skills are shown")
    ap.add_argument("--list", action="store_true", help="print every DD2 hero's camping skills instead of drawing")
    args = ap.parse_args()
    rules = Rules(args.dd1)
    if args.list:
        list_skills(rules)
        return
    os.makedirs(args.out, exist_ok=True)
    print(compose_meal(args.dd1, args.out, rules))
    print(compose_respite(args.dd1, args.out, rules, args.cls))
    print(compose_survivalist(args.dd1, args.out, rules, args.cls))


if __name__ == "__main__":
    main()
