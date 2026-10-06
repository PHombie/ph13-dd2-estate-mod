#!/usr/bin/env python3
"""Compose the building upgrade view, the Guild and the Blacksmith from the player's own Darkest Dungeon (1) install.

Reference for the layout numbers in src/DD2Estate/Estate/UpgradePane.cs, GuildPanel.cs, BlacksmithPanel.cs and
UpgradeUi.cs, and for the rule arithmetic in UpgradeRules.cs / UpgradeText.cs / UpgradeHeroRules.cs / Guild.cs /
Blacksmith.cs (prices, resolve gates, the gear growth averaged from DD1's hero files):
change one, change the other. Nothing from DD1 is copied into the repo; output goes to _lab/ (gitignored).

    python tools/preview_upgrades.py [--dd1 <install>] [--out <dir>] [--levels tree=n,tree=n] [--hover tree:step]

Writes upgrades_<building>.png for the eight buildings with upgrade trees, guild_panel.png and
blacksmith_panel.png (1920x1080). This file reads the trees, their prices and what a step does; the drawing
(the window's frame, the pane, the Guild and the Blacksmith) is tools/preview_windows.py's. DD2 art (portraits,
skill icons) is stood in for by DD1 art of the same size.

How the upgrade view is put together (everything at native size, positions relative to the building's
1395x776 backdrop, which the building screens put at 144,132 and the windows centre left of the roster):
  * campaign/town/buildings/building.layout.darkest gives DD1's own numbers: `upgrade_base_pos 172 259` on
    the screen, the pane art (blgupgradebg.png, 662x764) at `frame_offset -18 -115` from it, the text block
    at `verbose_offset 20 30` (380 wide), "Upgraded" and the percentage at `upgrade_title_offset` /
    `upgrade_percent_offset` inside the banner drawn in the pane art, the first tree at
    `upgrade_trees_offset 0 195` and the next ones every `upgrade_trees_spacing 0 160`.
  * A tree: title at `title_offset 20 -35`, its 72 px icon at `icon_offset 30 0`, the steps from the icon's
    right edge every `requirement_spacing 70 0`, the rule at `divider_offset 0 118`.
  * A step (campaign/town/buildings/upgrade/upgrade.layout.darkest): its place is `base_size 102 72`; 50 px
    icon at `icon_offset 40 10`, the 72 px gold backing of a bought step at `background_offset 30 0`, the link
    to the step before at `background_connector_offset 0 24`, DD1's dark plate round the step that comes next
    at `outline_offset 13 -12`, its price hung from `cost_offset 62 72`.
  * The pane lies over the screen's own name (tools/preview_windows.py screen_name): the building's 113 px icon
    shows in the notch of the pane art, the name above the pane's body.
"""
import argparse
import json
import os
import re

from PIL import Image, ImageDraw, ImageFont

SCREEN_W, SCREEN_H = 1920, 1080
ART_SIZE = (1395, 776)
PANEL_ART_POS = (144, 132)           # BuildingPanel.cs (town.layout.darkest area_pos)
ROSTER_W = 372
WINDOW_ART_POS = ((SCREEN_W - ROSTER_W - ART_SIZE[0]) // 2, (SCREEN_H - ART_SIZE[1]) // 2 - 12)   # RosterWindow.cs

# ---- upgrade pane: keep in step with UpgradePane.cs (art-relative) ---------------------------------------
DD1_AREA_POS = (144, 132)            # where DD1 puts the backdrop; its layout files are in screen pixels
LAYOUT_DEFAULTS = {
    ("building_base_layout", "upgrade_base_pos"): (172, 259),
    ("building_base_upgrade_layout", "frame_offset"): (-18, -115),
    ("building_base_upgrade_layout", "verbose_offset"): (20, 30),
    ("building_base_upgrade_layout", "upgrade_title_offset"): (458, 36),
    ("building_base_upgrade_layout", "upgrade_percent_offset"): (480, 62),
    ("building_base_upgrade_layout", "upgrade_trees_offset"): (0, 195),
    ("building_base_upgrade_layout", "upgrade_trees_spacing"): (0, 160),
    ("building_base_upgrade_tree_layout", "title_offset"): (20, -35),
    ("building_base_upgrade_tree_layout", "icon_offset"): (30, 0),
    ("building_base_upgrade_tree_layout", "requirement_spacing"): (70, 0),
    ("building_base_upgrade_tree_layout", "divider_offset"): (0, 118),
    ("upgrade_requirement_layout", "icon_offset"): (40, 10),
    ("upgrade_requirement_layout", "background_offset"): (30, 0),
    ("upgrade_requirement_layout", "background_connector_offset"): (0, 24),
    ("upgrade_requirement_layout", "cost_offset"): (62, 72),
}
VERBOSE_WIDTH = 380
NOTCH_ICON = (9, 10)                 # building icon inside the pane art
TITLE_BOX = (140, 41, 426, 111)      # building name while the pane is open: right of the notch
TOGGLE_POS = (446, 58)               # "Upgrades" / "Back" button
TOGGLE_SIZE = (130, 40)
PURSE_OFFSET = (0, -8)               # heirloom purse: from the text block's corner
PURSE_STEP = 95
PURSE_ICON = 30
DESCRIPTION_OFFSET = (0, 26)         # effect text under the purse
DESCRIPTION_HEIGHT = 104             # up to the first tree's title
DESCRIPTION_LINE = 20
COST_LINE = 21                       # price lines under a step
COST_ICON = 20

# ---- guild / blacksmith: keep in step with UpgradeUi.cs, GuildPanel.cs, BlacksmithPanel.cs ----------------
HERO_BOX = (678, 31, 1342, 123)      # the frame drawn in guild/blacksmith.character_background.png
HERO_STRIP_RIGHT = 1310              # the window's close button sits on the frame's right end
HERO_PORTRAIT = 56
HERO_GAP = 6
HEADER_POS = (678, 136)              # selected hero's name; the purse is right-aligned on the same line
GRID_POS = (678, 186)                # guild_layout skill_spacing 0 91
GRID_COLUMN = 340
GRID_ROW = 91
GRID_ROWS = 6
SKILL_FRAME = 88
SKILL_NAME_OFFSET = (100, 0)
SKILL_NODE_OFFSET = (100, 32)
SKILL_NODE_SPACING = 75              # guild_upgrade_tree_layout requirement_spacing
SKILL_PRICE_OFFSET = (258, 44)
MESSAGE_CENTRE = (1010, 752)
HERO_CAPTION = 18                    # a line under each portrait (resolve level, gear levels)
SMITH_FRAME_POS = (678, 182)         # blacksmith.frame.png: DD1's two boxes, weapon over armour
SMITH_PIECE_OFFSET = (20, 20)        # blacksmith_layout equipment_pos minus frame_pos
SMITH_PIECE_SPACING = (0, 176)       # equipment_spacing
SMITH_STEP_SPACING = 75              # blacksmith_upgrade_tree_layout requirement_spacing
SMITH_TEXT_TOP = 80
SMITH_TEXT_LINE = 24

DD1_STRESS_MAX = 200.0
DD2_STRESS_MAX = 10.0
HEIRLOOMS = ["crest", "deed", "bust", "portrait"]

BUILDINGS = ["stage_coach", "blacksmith", "guild", "camping_trainer", "tavern", "abbey", "sanitarium", "nomad_wagon"]
PANEL_BUILDINGS = ("tavern", "abbey")

PARCHMENT = (222, 209, 178, 255)
GOLD = (219, 181, 92, 255)
DIM = (158, 148, 128, 255)
WARN = (199, 51, 38, 255)


# ---------------------------------------------------------------- DD1 files

def read_json(path):
    with open(path, encoding="utf-8-sig") as f:
        return json.load(f)


def parse_darkest(text):
    blocks, block, key = {}, None, None
    for raw in text.splitlines():
        line = raw.split("//")[0].split("#")[0]
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


def _is_number(tok):
    try:
        float(tok)
        return True
    except ValueError:
        return False


class Layout:
    def __init__(self, dd1):
        self.blocks = {}
        for rel in (("campaign", "town", "buildings", "building.layout.darkest"),
                    ("campaign", "town", "buildings", "upgrade", "upgrade.layout.darkest")):
            path = os.path.join(dd1, *rel)
            if os.path.isfile(path):
                with open(path, encoding="utf-8-sig") as f:
                    self.blocks.update(parse_darkest(f.read()))

    def vec(self, block, key):
        vals = self.blocks.get(block, {}).get(key, [])
        try:
            return (float(vals[0]), float(vals[1]))
        except (IndexError, ValueError):
            return LAYOUT_DEFAULTS[(block, key)]


def read_names(dd1):
    """town_name_<id>, town_activity_name_<id>, upgrade_tree_name_<tree> from the English section."""
    wanted = re.compile(r'<entry id="(town_name_|town_activity_name_|upgrade_tree_name_|upgrade_tree_tooltip_description_)([^"]+)"><!\[CDATA\[(.*?)\]\]></entry>')
    path = os.path.join(dd1, "localization", "miscellaneous.string_table.xml")
    found = {}
    if os.path.isfile(path):
        english = False
        with open(path, encoding="utf-8-sig", errors="replace") as f:
            for line in f:
                if "<language " in line:
                    if english:
                        break
                    english = 'id="english"' in line
                elif english and "</language>" in line:
                    break
                elif english and ('"town_' in line or '"upgrade_tree_' in line):
                    m = wanted.search(line)
                    if m:
                        found.setdefault(m.group(1) + m.group(2), m.group(3))
    return found


# ---------------------------------------------------------------- rules (UpgradeRules.cs)

class Tree:
    def __init__(self, building, data):
        self.id = data["id"]
        self.building = building
        self.steps = []
        for req in data.get("requirements", []):
            costs = [(c["type"], c["amount"]) for c in req.get("currency_cost", []) if c.get("amount", 0) > 0]
            needs = [(p["tree_id"], p["requirement_code"]) for p in req.get("prerequisite_requirements", [])
                     if p.get("tree_id") != self.id]
            self.steps.append(dict(code=req["code"], costs=costs, needs=needs))
        self.tracks = []        # (array name, subject, values by level, DD1 entries by level)

    def index(self, code):
        for i, step in enumerate(self.steps):
            if step["code"] == code:
                return i
        return -1


def load_trees(dd1):
    trees = {}
    for building in BUILDINGS:
        path = os.path.join(dd1, "upgrades", "building", building + ".upgrades.json")
        if not os.path.isfile(path):
            continue
        for data in read_json(path).get("trees", []):
            trees[data["id"]] = Tree(building, data)
    for building in BUILDINGS:
        path = os.path.join(dd1, "campaign", "town", "buildings", building, building + ".building.json")
        if os.path.isfile(path):
            collect_tracks(trees, building, read_json(path).get("data", {}), None)
    add_own_trees(trees)
    return trees


# HeroPaths.cs: the stage coach's Hero Paths, priced as the Hero Barracks' first, third and last step.
OWN_NAMES = {"stage_coach.hero_paths": "Hero Paths"}
OWN_ICONS = {"stage_coach.hero_paths": "campaign/town/buildings/nomad_wagon/nomad_wagon.cost.icon.png"}


def add_own_trees(trees):
    barracks = trees.get("stage_coach.rostersize")
    fallback = [[("deed", 3), ("crest", 4)], [("deed", 16), ("crest", 15)], [("deed", 25), ("crest", 26)]]
    tree = Tree("stage_coach", {"id": "stage_coach.hero_paths", "requirements": []})
    for i, like in enumerate((0, 2, 4)):
        costs = list(barracks.steps[like]["costs"]) if barracks and like < len(barracks.steps) else fallback[i]
        tree.steps.append(dict(code="abc"[i], costs=costs, needs=[]))
    trees[tree.id] = tree


def tree_name(names, tree_id):
    return OWN_NAMES.get(tree_id) or names.get("upgrade_tree_name_" + tree_id, tree_id)


def places(mine, rows=3, short=3):
    """UpgradePane.Places: one tree a row; when there are more trees than rows, short neighbours pair up from the bottom."""
    lines = [[i] for i in range(len(mine))]
    r = len(lines) - 1
    while r > 0 and len(lines) > rows:
        if len(lines[r]) == 1 and len(lines[r - 1]) == 1 and len(mine[lines[r][0]].steps) <= short and len(mine[lines[r - 1][0]].steps) <= short:
            lines[r - 1].append(lines[r][0])
            del lines[r]
            r -= 1
        r -= 1
    out = {}
    for row, line in enumerate(lines):
        for column, index in enumerate(line):
            out[index] = (row, column, len(line) > 1)
    return out


VALUE_KEYS = ("cost_currency.amount", "number_of_slots", "heal_low", "amount", "discount_percent", "chance")


def entry_value(entry):
    for key in VALUE_KEYS:
        node = entry
        for part in key.split("."):
            node = node.get(part) if isinstance(node, dict) else None
        if node is not None:
            return float(node)
    return None


def collect_tracks(trees, building, node, subject):
    """Every `*_upgrades` array of a building file becomes one track per upgrade tree it names."""
    if isinstance(node, list):
        for item in node:
            collect_tracks(trees, building, item, subject)
        return
    if not isinstance(node, dict):
        return
    if "id" in node and isinstance(node.get("data"), dict):
        subject = node["id"]
    for key, value in node.items():
        if key.endswith("_upgrades") and isinstance(value, list):
            by_tree = {}
            for entry in value:
                code = entry.get("upgrade_requirement_code")
                tree_id = entry.get("upgrade_tree_id") or (building + "." + subject if subject else None)
                if code is not None and tree_id in trees:
                    by_tree.setdefault(tree_id, []).append(entry)
            base = [e for e in value if e.get("upgrade_requirement_code") is None]
            for tree_id, entries in by_tree.items():
                tree = trees[tree_id]
                additive = key.endswith("_discount_upgrades") and "sell" not in key
                values, raw = [], []
                for level in range(len(tree.steps) + 1):
                    owned = [e for e in entries if 0 <= tree.index(e["upgrade_requirement_code"]) < level]
                    if additive:
                        values.append(sum(entry_value(e) or 0 for e in owned))
                        raw.append(owned[-1] if owned else None)
                    else:
                        last = owned[-1] if owned else (base[-1] if base else None)
                        values.append(entry_value(last) if last else 0.0)
                        raw.append(last)
                tree.tracks.append((key, subject, values, raw))
        else:
            collect_tracks(trees, building, value, subject)


def hero_prices(dd1):
    """DD1's per-hero purchases (uniform over its classes): skill unlock + levels, weapon, armour."""
    folder = os.path.join(dd1, "upgrades", "heroes")
    out = dict(skill=[], weapon=[], armour=[])
    for name in sorted(os.listdir(folder)) if os.path.isdir(folder) else []:
        for tree in read_json(os.path.join(folder, name)).get("trees", []):
            tags = tree.get("tags", [])
            kind = "skill" if "combat_skill" in tags else "weapon" if "weapon" in tags else "armour" if "armour" in tags else None
            if kind and not out[kind]:
                for req in tree.get("requirements", []):
                    gold = sum(c["amount"] for c in req.get("currency_cost", []) if c["type"] == "gold")
                    needs = [(p["tree_id"], p["requirement_code"]) for p in req.get("prerequisite_requirements", [])
                             if p["tree_id"] != tree["id"]]
                    out[kind].append(dict(gold=gold, needs=needs, resolve=req.get("prerequisite_resolve_level", 0)))
        if all(out.values()):
            break
    return out


def gear_growth(dd1):
    """Blacksmith.cs: what a weapon / armour level adds over level 1, averaged over DD1's classes
    (heroes/<class>/<class>.info.darkest), in whole percents and whole speed."""
    sums = dict(damage=[], crit=[], speed=[], health=[], dodge=[])
    classes = 0
    folder = os.path.join(dd1, "heroes")
    for name in sorted(os.listdir(folder)) if os.path.isdir(folder) else []:
        path = os.path.join(folder, name, name + ".info.darkest")
        if not os.path.isfile(path):
            continue
        weapons, armours = [], []
        with open(path, encoding="utf-8-sig", errors="replace") as f:
            for line in f:
                m = re.match(r'weapon:.*\.dmg (\d+) (\d+) \.crit ([\d.]+)% \.spd (-?\d+)', line)
                if m:
                    weapons.append((float(m.group(1)) + float(m.group(2)), float(m.group(3)), float(m.group(4))))
                m = re.match(r'armour:.*\.def ([\d.]+)% .*\.hp (\d+)', line)
                if m:
                    armours.append((float(m.group(1)), float(m.group(2))))
        if len(weapons) < 2 or len(armours) < 2:
            continue
        classes += 1

        def add(key, i, value):
            while len(sums[key]) <= i:
                sums[key].append(0.0)
            sums[key][i] += value
        for i, w in enumerate(weapons):
            add("damage", i, w[0] / weapons[0][0] - 1)
            add("crit", i, (w[1] - weapons[0][1]) / 100)
            add("speed", i, w[2] - weapons[0][2])
        for i, a in enumerate(armours):
            add("health", i, a[1] / armours[0][1] - 1)
            add("dodge", i, (a[0] - armours[0][0]) / 100)
    if not classes:
        return dict(damage=[], crit=[], speed=[], health=[], dodge=[], classes=0)
    out = {k: [round(v / classes * 100) / 100.0 for v in vals] for k, vals in sums.items()}
    out["speed"] = [int(round(v / classes)) for v in sums["speed"]]
    out["classes"] = classes
    return out


def gear_effect(growth, piece, level):
    """What a level gives over the gear a hero starts with, in the mod's own words."""
    if level <= 1:
        return "As issued"

    def at(key):
        vals = growth[key]
        return vals[min(level - 1, len(vals) - 1)] if vals else 0
    parts = []
    if piece == "weapon":
        if at("damage"):
            parts.append("+%d%% damage" % round(at("damage") * 100))
        if at("crit"):
            parts.append("+%d%% crit" % round(at("crit") * 100))
        if at("speed"):
            parts.append("+%d speed" % at("speed"))
    else:
        if at("health"):
            parts.append("+%d%% health" % round(at("health") * 100))
        if at("dodge"):
            parts.append("-%d%% damage taken" % round(at("dodge") * 100))
    return ", ".join(parts)


def to_gold(dd1_gold, discount=0.0):
    return max(1, int(dd1_gold * (1.0 - discount) + 0.5)) if dd1_gold > 0 else 0


# ---------------------------------------------------------------- text (UpgradeText.cs)

TRACK_LABELS = {
    "cost_upgrades": "Price",
    "slot_upgrades": "Places",
    "stress_upgrades": "Stress relief",
    "number_of_recruits_upgrades": "Recruits each week",
    "roster_size_upgrades": "Barracks",
    "upgraded_recruits_upgrades": "Seasoned recruits",
    "number_of_trinkets_upgrades": "Trinkets on offer",
    "positive_quirk_cost_upgrades": "Lock in a good quirk",
    "negative_quirk_cost_upgrades": "Remove a bad quirk",
    "permanent_negative_quirk_cost_upgrades": "Remove a locked-in quirk",
    "disease_quirk_cost_upgrades": "Cure a disease",
    "disease_quirk_cure_all_chance_upgrades": "Chance to cure all diseases at once",
}


def track_text(names, tree, track, level):
    key, subject, values, raw = track
    before, after = values[level - 1], values[level]
    if before == after and raw[level - 1] is raw[level]:
        return None
    where = ""
    if len(set(t[1] for t in tree.tracks)) > 1:        # one tree serving several activities: say which
        where = " (" + names.get("town_activity_name_" + subject, subject.replace("_", " ").title()) + ")"
    # UpgradeText.Dd1Line: DD1's own sentence where it has one for this kind of step, as DD1 writes it; the mod's
    # numbers otherwise. A change DD1's tooltip passes over in silence (UpgradeText.Unsaid) is passed over here too.
    dd1 = dd1_line(names, tree, key, subject, after, raw[level], level, values[0])
    if dd1 is not None:
        return dd1
    if key in UNSAID:
        return None
    if key.endswith("_discount_upgrades"):
        return "Prices %d%% lower (%d%% in all)" % (round((after - before) * 100), round(after * 100))
    label = TRACK_LABELS.get(key, key.replace("_upgrades", "").replace("_", " ").capitalize()) + where
    if key == "stress_upgrades":
        return "%s: %s, was %s" % (label, relief(raw[level]), relief(raw[level - 1]))
    if key == "upgraded_recruits_upgrades":
        e = raw[level]
        return "%s: %s%% arrive as veterans of rank %d, trained and equipped" % (label, number(e["chance"] * 100), e["level"])
    if key.endswith("chance_upgrades"):
        return "%s: %d%%, was %d%%" % (label, round(after * 100), round(before * 100))
    if "cost" in key:
        return "%s: %d gold, was %d" % (label, to_gold(after), to_gold(before))
    return "%s: %d, was %d" % (label, after, before)


DD1_DISCOUNT_LINES = {
    "guild.cost": "reduces_cost_of_combat_skill_upgrades_format", "blacksmith.cost": "reduces_cost_of_weapon_and_armour_upgrades_format",
    "camping_trainer.cost": "reduces_cost_of_camping_skills_format", "nomad_wagon.cost": "reduces_cost_of_items_format",
}
DD1_LINES = {
    "number_of_recruits_upgrades": "increases_number_of_heroes_generated_format", "roster_size_upgrades": "increases_size_of_roster_format",
    "number_of_trinkets_upgrades": "increases_number_of_items_generated_format", "stress_upgrades": "increases_stress_recovery",
    "upgraded_recruits_upgrades": "increases_upgraded_recruit_level_format", "disease_quirk_cure_all_chance_upgrades": "disease_cure_all_chance_format",
    "affliction_cure_upgrades": "increases_chance_of_affliction_cure",
}
# "Reduces treatment cost by %d%%": the share taken off the price the building began with
DD1_COST_LINES = {
    "cost_upgrades": "reduces_treatment_cost_format", "positive_quirk_cost_upgrades": "reduces_positive_quirk_treatment_cost_format",
    "negative_quirk_cost_upgrades": "reduces_negative_quirk_treatment_cost_format", "disease_quirk_cost_upgrades": "reduces_disease_quirk_treatment_cost_format",
}
UNSAID = {"permanent_negative_quirk_cost_upgrades"}     # DD1's string table has no sentence for it


def dd1_line(names, tree, key, subject, after, entry, level, start=0):
    """DD1's sentence for a step (upgrade_tree_tooltip_description_*_format) filled with the value it brings; None
    where DD1 has none that fits what the step does here."""
    number = int(round(after))
    if key.endswith("_discount_upgrades"):
        name = DD1_DISCOUNT_LINES.get(tree.id)
        number = int(round(after * 100))
    elif key == "slot_upgrades":
        name = {"treatment": "increases_number_of_quirk_slots_format", "disease_treatment": "increases_number_of_disease_slots_format"}.get(subject, "increases_number_of_slots_format")
    elif key in DD1_COST_LINES:
        name = DD1_COST_LINES[key]
        number = int(round((1 - after / start) * 100)) if start > 0 else 0
    else:
        name = DD1_LINES.get(key)
        if key == "upgraded_recruits_upgrades":
            number = entry.get("level", level)
        elif key == "disease_quirk_cure_all_chance_upgrades":
            number = int(round(after * 100))
    text = names.get("upgrade_tree_tooltip_description_" + name) if name else None
    if text is None:
        return None
    return re.sub(r"\{[^}]*\}", "", text).replace("%d", str(number)).replace("%%", "%")


def relief(entry):
    """In DD2 stress points, with decimals: the steps between DD1's tiers are smaller than a point."""
    low = entry.get("heal_low", 0) * DD2_STRESS_MAX / DD1_STRESS_MAX
    high = entry.get("heal_high", 0) * DD2_STRESS_MAX / DD1_STRESS_MAX
    return number(low) if abs(high - low) < 0.005 else "%s to %s" % (number(low), number(high))


def number(value):
    """At most two decimals, none when whole (C#: ToString("0.##"))."""
    return ("%.2f" % value).rstrip("0").rstrip(".")


def mastery_limit(prices, built, resolve):
    """Guild.cs: masteries a hero may have, by DD1's skill levels: each needs its guild step and resolve level."""
    skill = prices["skill"]
    opened = 0
    for purchase in skill[1:]:
        if purchase["resolve"] > resolve or not all(built(n) for n in purchase["needs"]):
            return opened
        opened += 1
    return None if len(skill) > 1 else 0          # None: no limit


def own_text(tree, level, prices, growth):
    """Trees whose effect is in the mod's Guild / Blacksmith rather than in a DD1 building file."""
    if tree.id == "guild.skill_levels":
        skill = prices["skill"]
        limit = mastery_limit(prices, lambda n: n[0] != tree.id or tree.index(n[1]) < level, 99)
        if limit is None:
            return ["Heroes of resolve level %d may master every skill they know" % skill[-1]["resolve"]]
        if limit:
            return ["A hero may master %d skill%s (at resolve level %d)" % (limit, "" if limit == 1 else "s", skill[limit]["resolve"])]
    if tree.id == "stage_coach.hero_paths":
        return ["Recruits arrive on %s path of their class; the estate may keep %s heroes of a class, each on a path of their own"
                % (("a second", "a third", "a fourth")[level - 1], ("two", "three", "four")[level - 1])]
    if tree.id in ("blacksmith.weapon", "blacksmith.armour"):
        piece = tree.id.split(".")[1]
        for i, purchase in enumerate(prices[piece]):
            if (tree.id, tree.steps[level - 1]["code"]) in purchase["needs"]:
                return ["%s level %s can be made for heroes of resolve level %d: %s"
                        % (piece.capitalize(), roman(i + 2), purchase["resolve"], gear_effect(growth, piece, i + 2))]
    return []


def roman(n):
    return ["0", "I", "II", "III", "IV", "V", "VI", "VII"][n]


# ---------------------------------------------------------------- drawing

DEFAULT_LEVELS = ("tavern.bar=2,tavern.gambling=1,stage_coach.numrecruits=1,stage_coach.rostersize=2,"
                  "guild.skill_levels=2,guild.cost=1,blacksmith.armour=2,blacksmith.weapon=1,sanitarium.cost=1,nomad_wagon.numitems=1")


def parse_levels(text):
    return {k: int(v) for k, v in (pair.split("=") for pair in text.split(",") if pair)}


def write_panes(kit, out, levels=None, purse=None, hover=None):
    """Draws upgrades_<building>.png for every building with trees: the pane itself is tools/preview_windows.py's
    (UpgradePane.cs); the trees, their prices and what a step does are read here."""
    import preview_windows as pw
    dd1 = kit.dd1
    levels = levels if levels is not None else parse_levels(DEFAULT_LEVELS)
    purse = purse or dict(crest=24, deed=9, bust=6, portrait=3)
    names = read_names(dd1)
    trees = load_trees(dd1)
    prices = hero_prices(dd1)
    growth = gear_growth(dd1)

    def describe(tree, level):
        return [t for t in (track_text(names, tree, track, level) for track in tree.tracks) if t] + own_text(tree, level, prices, growth)

    def built(tree_id):
        return min(levels.get(tree_id, 0), len(trees[tree_id].steps)) if tree_id in trees else 0

    def waits(tree, k):
        """UpgradeText.Prerequisites: what step k still waits for, as DD1's tooltip lists it ("<tree> Level <n>")."""
        lines = []
        if k > built(tree.id):
            lines.append("%s Level %d" % (tree_name(names, tree.id), k))
        for other, code in tree.steps[k]["needs"]:
            at = trees[other].index(code) if other in trees else -1
            if at >= built(other):
                lines.append("%s Level %d" % (tree_name(names, other), at + 1))
        return lines

    written = []
    for building in BUILDINGS:
        mine = [t for t in trees.values() if t.building == building]
        if not mine:
            continue
        over = hover
        if over is None:
            first = mine[0]
            over = (first.id, min(levels.get(first.id, 0), len(first.steps) - 1))
        written.append(pw.upgrades(kit, out, building, mine, lambda tree_id: tree_name(names, tree_id), levels, purse, hover=over,
                                   own_icons=OWN_ICONS, places=places, describe=describe, waits=waits))
    return written


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--dd1", default=os.environ.get("DD1_ROOT", r"E:\Steam\steamapps\common\DarkestDungeon"))
    ap.add_argument("--out", default=os.path.join(here, "..", "_lab", "preview"))
    ap.add_argument("--levels", default=DEFAULT_LEVELS, help="steps bought per tree, e.g. tavern.bar=2,guild.cost=1")
    ap.add_argument("--hover", default="", help="tree:step to describe, e.g. tavern.bar:2 (step counts from 0)")
    args = ap.parse_args()

    if not os.path.isfile(os.path.join(args.dd1, "campaign", "town", "town_bg.png")):
        raise SystemExit("Darkest Dungeon (1) not found at %s (use --dd1 or DD1_ROOT)" % args.dd1)
    out = os.path.abspath(args.out)
    os.makedirs(out, exist_ok=True)
    levels = parse_levels(args.levels)
    names = read_names(args.dd1)
    trees = load_trees(args.dd1)
    prices = hero_prices(args.dd1)
    growth = gear_growth(args.dd1)
    purse = dict(crest=24, deed=9, bust=6, portrait=3)

    for tree in trees.values():
        print("%-30s level %d/%d" % (tree.id, min(levels.get(tree.id, 0), len(tree.steps)), len(tree.steps)))
        for k, step in enumerate(tree.steps):
            lines = [t for t in (track_text(names, tree, track, k + 1) for track in tree.tracks) if t] + own_text(tree, k + 1, prices, growth)
            cost = ", ".join("%d %s" % (a, c) for c, a in step["costs"])
            needs = "; needs " + ", ".join("%s %s" % n for n in step["needs"]) if step["needs"] else ""
            print("   %s  %-22s %s%s" % (step["code"], cost, " | ".join(lines), needs))
    for kind in ("skill", "weapon", "armour"):
        print("%-6s DD1 gold %s -> estate %s, resolve %s" % (kind, [s["gold"] for s in prices[kind]], [to_gold(s["gold"]) for s in prices[kind]],
                                                            [s["resolve"] for s in prices[kind]]))
    print("gear growth over level 1 (mean of %d DD1 classes):" % growth["classes"], {k: v for k, v in growth.items() if k != "classes"})

    import preview_windows as pw
    kit = pw.Kit(args.dd1)
    hover = None
    if args.hover:
        tree_id, step = args.hover.split(":")
        hover = (tree_id, int(step))
    for path in write_panes(kit, out, levels, purse, hover):
        print("wrote %s" % path)
    print("wrote %s, %s" % (pw.guild(kit, out, name="guild_panel.png"), pw.blacksmith(kit, out, name="blacksmith_panel.png")))


if __name__ == "__main__":
    main()
