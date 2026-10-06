#!/usr/bin/env python3
"""Compose the quest selection map ("Estate Map", the screen behind Embark) from the player's own Darkest
Dungeon (1) install.

Reference for the layout numbers in src/DD2Estate/Estate/QuestPanel.cs, QuestMapParts.cs and QuestMapText.cs:
change one, change the other. Nothing from DD1 is copied into the repo; output goes to _lab/ (gitignored).

    python tools/preview_quest_map.py [--dd1 <install>] [--out <dir>]

Writes quest_map.png (mid game, a story quest selected, a party DD1 has a name for, the week's town event behind
the tray and in the panel's foot), quest_map_early.png (the first weeks: three regions still locked, a hero who
refuses apprentice work, the tooltip of a region) and quest_map_late.png (every tier, several quests to a region
in rows of four, the Darkest Dungeon open, a party of three asked DD1's question). Hero portraits, hero names
and the hamlet's own chrome (the roster column and the estate's bar, drawn by tools/preview_hamlet.py) are
stand-ins; positions, art, names of quests, lengths and difficulties, level thresholds and level caps come from
the DD1 files the way the plugin reads them, and so does what a generated quest pays (quest.generation.json:
gold in DD1's own numbers, the heirloom amounts, the rarity of the trinket). The trinket is DD2's
in the game: here its card (DD1's card of the rarity) carries a DD1 trinket picture of that rarity as a
stand-in for DD2's icon. Not drawn: what only moves (the flicker of dd_effect_<dungeon>.png, a bar filling
up, the effect of a named party, the fades).

How the screen is put together (1920x1080, all art at native size, y down):
  * campaign/town/quest_select/quest_select.background.png fills the screen; the hamlet's roster column
    lies over its right 370 px and the estate's bar (gold, heirlooms, the bar's buttons) over its foot, as
    DD1's do: the map is a layer under them.
  * quest_select.layout.darkest, one `quest_select_dungeon_layout_<dungeon>` block per region: `quest_map_pos`
    is the region's anchor on the map, `dungeon_effect_overlay_pos` where dd_effect_<dungeon>.png goes.
  * `town_quest_select_dungeon_layout` hangs a region's parts on that anchor: the bar art (background_offset),
    the name (right aligned at dungeon_name_offset, where the bar's ink smudge is), the level in the skull's
    mouth (dungeon_level_offset), the progress fill (dungeon_xp_bar_offset / _size), the padlock of a locked
    region (locked_icon_offset), the tooltip (dungeon_xp_bar_tt_*), and the quest markers: rows of
    `quest_number_of_quests_in_row`, each row centred on quest_button_start_offset, quest_button_spacing apart
    (a row of four at the Cove ends a little under the roster column's edge).
    A marker is the length frame (96 px, grey for generated quests, gold for story quests, spikes for medium
    and long) with the type icon (48 px, coloured by difficulty) in its middle; the selected one lies on
    quest_select_selected.png (selected_background_offset).
  * `town_quest_select_quest_layout` places the details panel (quest_select.questverbose_bg.png at
    description_pos) and everything on it: name, description, length | difficulty (the difficulty in DD1's
    colour of it), goals, rewards (item cards of panels/icons_equip in a centred row, each icon_offset inside
    its cell), and the strip at the bottom that DD1 uses for notices (the week's town event).
  * town.layout.darkest puts the party tray (embark_party_pos, slots from embark_party.layout.darkest, filled
    from the right) with the party's name above it (party_name_pos + text_offset) and
    shared/progression/progression.layout.darkest the back arrows and the forward button ("Provision"), which
    stands on the bar where the hamlet's Embark stands. The tray and the button are a layer over the bar.
"""
import argparse
import json
import os
import re

from PIL import Image, ImageDraw, ImageFont

SCREEN_W, SCREEN_H = 1920, 1080
ROSTER_X, ROSTER_W = 1550, 370       # the hamlet's roster column (RosterPanel.Width)
BAR_TOP = 958                        # the estate's bar: estate_summary_pos + pos_offset (975 - 17)

DIR = "campaign/town/quest_select/"

# ---- the mod's own numbers: keep in step with QuestPanel.cs / QuestMapParts.cs ---------------------------
ICON_SIZE = 113                      # quest_select.icon.png, at town_quest_select_layout.name_pos
TITLE_GAP = 12                       # screen name, right of the icon
TITLE_SIZE = 46
PANEL_TEXT_X = 34                    # left edge of running text on the details panel (description_text_offset.x)
DESCRIPTION_BOTTOM = 380             # the red field of the panel art ends here
CARD = (72, 144)                     # DD1 item card
TRINKET_ICON = 64                    # DD2's trinket icon on DD1's rarity card
PARTY_NAME_WIDTH = 760               # room for the line above the party tray (DD1: the party's name)
SLOT_SIZE = 85                       # campaign/town/hero_slot/hero_slot.background.png
PORTRAIT_INSET = 3                   # DD2's portrait in DD1's slot, as in the roster
TOOLTIP_WIDTH = 300                  # Dd1Tooltip's default
MAP_RIGHT = ROSTER_X                 # a tooltip stays left of the roster column
NAME_SIZE, LEVEL_SIZE = 30, 22       # region name, region level
QUEST_NAME_SIZE, BODY_SIZE, FORWARD_SIZE = 30, 20, 40

# Stock values of the DD1 layout files, used where a file cannot be read.
LAYOUT_DEFAULTS = {
    ("town_quest_select_layout", "name_pos"): (104, 122),
    ("town_quest_select_layout", "party_name_pos"): (756, 834),
    ("town_quest_select_party_name_layout", "text_offset"): (190, 8),
    ("town_quest_select_dungeon_layout", "town_event_icon_offset"): (-32, -14),
    ("town_quest_select_quest_layout", "town_notification_icon"): (60, 750),
    ("town_quest_select_quest_layout", "town_notification_title_text"): (110, 750),
    ("town_quest_select_quest_layout", "town_notification_text"): (110, 774),
    ("town_quest_select_quest_layout", "town_notification_text_width"): (400,),
    ("embark_party_layout", "town_event_overlay_offset"): (206, 124),
    ("inventory_item_layout", "icon_offset"): (4, 0),
    ("inventory_item_layout", "amount_text_offset"): (14, 4),
    ("quest_select_dungeon_layout_crypts", "quest_map_pos"): (940, 220),
    ("quest_select_dungeon_layout_crypts", "dungeon_effect_overlay_pos"): (1050, 260),
    ("quest_select_dungeon_layout_weald", "quest_map_pos"): (1000, 525),
    ("quest_select_dungeon_layout_weald", "dungeon_effect_overlay_pos"): (1000, 560),
    ("quest_select_dungeon_layout_warrens", "quest_map_pos"): (815, 400),
    ("quest_select_dungeon_layout_warrens", "dungeon_effect_overlay_pos"): (720, 460),
    ("quest_select_dungeon_layout_cove", "quest_map_pos"): (1260, 400),
    ("quest_select_dungeon_layout_cove", "dungeon_effect_overlay_pos"): (1270, 480),
    ("quest_select_dungeon_layout_darkestdungeon", "quest_map_pos"): (1260, 100),
    ("quest_select_dungeon_layout_darkestdungeon", "dungeon_effect_overlay_pos"): (1230, 70),
    ("town_quest_select_dungeon_layout", "background_offset"): (-5, -10),
    ("town_quest_select_dungeon_layout", "dungeon_name_offset"): (190, -8),
    ("town_quest_select_dungeon_layout", "dungeon_level_offset"): (219, 20),
    ("town_quest_select_dungeon_layout", "dungeon_xp_bar_offset"): (14, 32),
    ("town_quest_select_dungeon_layout", "dungeon_xp_bar_size"): (194, 8),
    ("town_quest_select_dungeon_layout", "dungeon_xp_bar_tt_offset"): (0, -88),
    ("town_quest_select_dungeon_layout", "dungeon_xp_bar_tt_hot_area_offset"): (14, 0),
    ("town_quest_select_dungeon_layout", "dungeon_xp_bar_tt_hot_area_size"): (232, 48),
    ("town_quest_select_dungeon_layout", "quest_button_start_offset"): (160, 92),
    ("town_quest_select_dungeon_layout", "quest_button_spacing"): (76, 80),
    ("town_quest_select_dungeon_layout", "locked_icon_offset"): (184, -10),
    ("town_quest_select_quest_button_layout", "selected_background_offset"): (-94, -94),
    ("town_quest_select_quest_layout", "description_pos"): (125, 132),
    ("town_quest_select_quest_layout", "name_text_offset"): (200, 140),
    ("town_quest_select_quest_layout", "description_text_offset"): (34, 194),
    ("town_quest_select_quest_layout", "description_text_width"): (340,),
    ("town_quest_select_quest_layout", "specifics_text_offset"): (226, 390),
    ("town_quest_select_quest_layout", "camping_text_offset"): (58, 388),
    ("town_quest_select_quest_layout", "goals_text_offset"): (34, 430),
    ("town_quest_select_quest_layout", "goal_description_text_start_offset"): (50, 460),
    ("town_quest_select_quest_layout", "goal_description_text_spacing"): (44, 40),
    ("town_quest_select_quest_layout", "rewards_text_offset"): (200, 530),
    ("town_quest_select_quest_layout", "rewards_inventory_system_offset"): (22, 562),
    ("town_quest_rewards_inventory_system_grid_layout", "start_pos"): (20, 28),
    ("town_quest_rewards_inventory_system_grid_layout", "offset"): (80, 160),
    ("town_quest_rewards_inventory_system_grid_layout", "number_of_columns"): (4,),
    ("town_screen_layout", "embark_party_pos"): (754, 871),
    ("embark_party_layout", "background_offset"): (-1, 0),
    ("embark_party_layout", "hero_slot_start_offset"): (22, 16),
    ("embark_party_layout", "hero_slot_spacing"): (93, 0),
    ("progression_layout", "back_pos"): (228, 82),
    ("progression_layout", "forward_pos"): (801, 984),
    ("progression_layout", "forward_text_offset"): (160, -2),
}
QUESTS_IN_ROW = 4                    # quest_number_of_quests_in_row
LAYOUT_FILES = ("campaign/town/town.layout.darkest", DIR + "quest_select.layout.darkest",
                "campaign/town/embark_party/embark_party.layout.darkest", "shared/progression/progression.layout.darkest",
                "shared/inventory/inventory.layout.darkest")

# DD1's colours/base.colours.darkest, used where the file cannot be read.
COLOUR_DEFAULTS = {
    "neutral": (174, 172, 162, 255), "notable": (200, 180, 110, 255), "harmful": (177, 25, 0, 255),
    "town_quest_select_xp_bar_gradient_left": (0x8e, 0x03, 0x00, 255),
    "town_quest_select_xp_bar_gradient_right": (0xeb, 0x16, 0x00, 255),
    "town_quest_select_party_name_default": (0x5d, 0x5a, 0x50, 255),
}

# DD1's campaign/progression/progression.json and campaign/quest/quest.restriction.json, as fallbacks.
LEVEL_THRESHOLDS = [0, 2, 6, 10, 16, 22, 32, 42]
LEVEL_CAPS = [2, 2, 3, 4, 5, 99, 99]

DUNGEONS = ("crypts", "weald", "warrens", "cove", "darkestdungeon")
DIFFICULTY = {"apprentice": 1, "veteran": 3, "champion": 5, "darkest": 6}
LENGTH = {"short": 1, "medium": 2, "long": 3}
# The mod's own words for a quest type, used where DD1 has no name for this type, length and region.
TYPE_NAMES = {"explore": "Exploration", "cleanse": "Cleansing", "gather": "Gathering", "activate": "Activation",
              "inventory_activate": "Activation", "kill_boss": "Boss"}
# ...and for what it is about, where DD1 has no description (it generates no "activate" quests).
TYPE_DESCRIPTIONS = {
    "explore": "Walk the halls, mark what is there, and come back to tell of it.",
    "cleanse": "Meet whatever holds the rooms in battle, and leave none of it standing.",
    "gather": "What the estate lost lies where it fell. Find it and bring it home.",
    "activate": "Old works stand idle in the dark. Find them and set them going again.",
    "inventory_activate": "Carry what was entrusted to you to the places that wait for it.",
    "kill_boss": "Something has made this place its own. Find it and put an end to it.",
}
STRING_DEFAULTS = {
    "town_name_quest_select": "Estate Map", "town_progression_forward_provision": "Provision",
    "town_quest_goals": "Goals:", "town_quest_rewards": "Rewards", "town_quest_specifics_format": "%s | %s",
    "town_quest_locked": "Explore other regions to unlock this one.",
    "town_quest_dungeon_not_released": "Enter this, the most dreaded of regions.",
    "town_quest_length_1": "Short", "town_quest_length_2": "Medium", "town_quest_length_3": "Long",
    "town_quest_difficulty_1": "Apprentice (Lvl 1)", "town_quest_difficulty_3": "Veteran (Lvl 3)",
    "town_quest_difficulty_5": "Champion (Lvl 5)", "town_quest_difficulty_6": "Darkest (Lvl 6)",
    "town_quest_number_of_camps0": "This quest does not involve Camping",
    "town_quest_quest_select_small_team_confirm": "Grave danger awaits the underprepared. Do you wish to continue without a full contingent?",
    "town_quest_select_confirm_yes": "Still Embark", "town_quest_select_confirm_no": "Cancel Embark",
    "town_quest_progress_format": "Clear a path to the boss! Progress: %3.0f%%",
    "town_quest_progress_plot_kill_necromancer_1": "Slay the boss!",
    "str_quest_select_town_event_title": "Town Event:", "party_name_default": "Build a Party From the Roster",
    "dungeon_name_crypts": "Ruins", "dungeon_name_weald": "Weald", "dungeon_name_warrens": "Warrens",
    "dungeon_name_cove": "Cove", "dungeon_name_darkestdungeon": "Darkest Dungeon",
}
STRING_PREFIXES = ("town_quest_", "dungeon_name_", "town_name_quest_select", "town_progression_forward_", "str_quest_select_")
# shared/inventory/item.display.json: gold.icon_thresholds, should the file be missing
GOLD_PILES = [250, 500, 750, 1000]

INK = (10, 9, 8, 255)
WHITE = (255, 255, 255, 255)


# ---- DD1 files ---------------------------------------------------------------------------------------------

def parse_darkest_blocks(text):
    """Blocks in file order: [(name, {key: [tokens]})]."""
    blocks, block, key = [], None, None
    for raw in text.splitlines():
        line = raw.split("//")[0].split("#")[0]
        for tok in line.split():
            if tok.endswith(":") and not tok.startswith("."):
                block = {}
                blocks.append((tok[:-1], block))
                key = None
            elif len(tok) > 1 and tok[0] == "." and (tok[1].isalpha() or tok[1] == "_"):
                key = tok[1:]
                if block is not None:
                    block[key] = []
            elif block is not None and key is not None:
                block[key].append(tok.strip('"'))
    return blocks


class Layout:
    def __init__(self, dd1):
        self.blocks = {}
        for rel in LAYOUT_FILES:
            path = os.path.join(dd1, rel)
            if os.path.isfile(path):
                with open(path, encoding="utf-8-sig") as f:
                    for name, block in parse_darkest_blocks(f.read()):
                        self.blocks.setdefault(name, block)

    def vec(self, block, key):
        values = self.blocks.get(block, {}).get(key)
        want = LAYOUT_DEFAULTS[(block, key)]
        if not values or len(values) < len(want):
            return want
        return tuple(int(float(v)) for v in values[:len(want)])

    def has(self, block):
        return block in self.blocks or (block, "quest_map_pos") in LAYOUT_DEFAULTS

    def in_row(self, dungeon):
        values = self.blocks.get("quest_select_dungeon_layout_" + dungeon, {}).get("quest_number_of_quests_in_row")
        return max(1, int(float(values[0]))) if values else QUESTS_IN_ROW


def load_colours(dd1):
    """colours/base.colours.darkest: `colour: .id "x" .rgba r g b a | .rgba #rrggbb | .shared_id "y"`."""
    colours, shared = dict(COLOUR_DEFAULTS), {}
    path = os.path.join(dd1, "colours", "base.colours.darkest")
    if os.path.isfile(path):
        with open(path, encoding="utf-8-sig") as f:
            for line in f:
                m = re.search(r'\.id\s+"?([\w.]+)"?\s+\.(rgba|shared_id)\s+(.*)', line)
                if not m:
                    continue
                value = m.group(3).strip()
                if m.group(2) == "shared_id":
                    shared[m.group(1)] = value.split()[0].strip('"')
                elif value.startswith("#"):
                    h = value[1:].split()[0]
                    h = "".join(c * 2 for c in h) if len(h) == 3 else h
                    if len(h) >= 6:
                        colours[m.group(1)] = (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), 255)
                else:
                    parts = value.split()
                    if len(parts) >= 3 and all(p.isdigit() for p in parts[:3]):
                        colours[m.group(1)] = tuple(int(p) for p in parts[:3]) + (255,)
    for name, target in shared.items():
        if target in colours:
            colours[name] = colours[target]
    return colours


def load_strings(dd1):
    """The English section of localization/miscellaneous.string_table.xml (it comes first in the file), and the
    party names of party_names.string_table.xml."""
    strings = dict(STRING_DEFAULTS)
    read_table(os.path.join(dd1, "localization", "miscellaneous.string_table.xml"), STRING_PREFIXES, strings)
    read_table(os.path.join(dd1, "localization", "party_names.string_table.xml"), ("party_name_",), strings)
    return strings


def read_table(path, prefixes, strings):
    if not os.path.isfile(path):
        return strings
    entry = re.compile(r'<entry id="([^"]+)"><!\[CDATA\[(.*?)\]\]></entry>')
    english, seen = False, set()
    with open(path, encoding="utf-8", errors="replace") as f:
        for line in f:
            if "<language " in line:
                if english:
                    break
                english = 'id="english"' in line
                continue
            if not english:
                continue
            if "</language>" in line:
                break
            m = entry.search(line)
            if m and m.group(1).startswith(prefixes) and m.group(1) not in seen:
                seen.add(m.group(1))
                strings[m.group(1)] = re.sub(r"\{[^}]*\}", "", m.group(2)).strip()      # no colour marks
    return strings


def party_name(dd1, strings, classes_back_to_front):
    """QuestMapText.PartyName: DD1's name of four classes in this order (shared/party_name/party_name_library.json)."""
    library = read_json(os.path.join(dd1, "shared", "party_name", "party_name_library.json")) or {}
    for entry in library.get("party_names", []):
        if entry.get("required_hero_class") == list(classes_back_to_front):
            return strings.get("party_name_%s" % entry.get("id"))
    return None


def gold_pile(dd1, dd1_gold):
    """QuestMapArt.GoldPile: the last of DD1's pictures of gold whose threshold the sum reaches."""
    display = read_json(os.path.join(dd1, "shared", "inventory", "item.display.json")) or {}
    thresholds = (display.get("gold") or {}).get("icon_thresholds") or GOLD_PILES
    pile = 0
    for i, threshold in enumerate(thresholds):
        if dd1_gold >= threshold:
            pile = i
    return pile


def read_json(path):
    with open(path, encoding="utf-8-sig") as f:
        return json.load(f)


def load_rules(dd1):
    thresholds, caps = LEVEL_THRESHOLDS, LEVEL_CAPS
    try:
        thresholds = read_json(os.path.join(dd1, "campaign", "progression", "progression.json"))["dungeon"]["level_threshold_table"]
    except (OSError, KeyError, ValueError):
        pass
    try:
        caps = read_json(os.path.join(dd1, "campaign", "quest", "quest.restriction.json"))["restriction"]["difficulty"]["resolve_level_threshold_table"]
    except (OSError, KeyError, ValueError):
        pass
    return thresholds, caps


# ---- what the screen says: keep in step with QuestMapText.cs and QuestBoard.cs -----------------------------

REWARDS = None                       # DD1's reward tables, read by compose()
DIFFICULTY = {"apprentice": 1, "veteran": 3, "champion": 5, "darkest": 6}
LENGTH = {"short": 1, "medium": 2, "long": 3}


def load_rewards(dd1):
    """quest.generation.json `rewards`, progression.json and one DD1 trinket per rarity to stand in for DD2's."""
    try:
        rewards = read_json(os.path.join(dd1, "campaign", "quest", "quest.generation.json"))["generation"]["rewards"]
        xp = read_json(os.path.join(dd1, "campaign", "progression", "progression.json"))["dungeon"]["quest_completion_xp_table"]
        entries = read_json(os.path.join(dd1, "trinkets", "base.entries.trinkets.json"))["entries"]
    except (OSError, ValueError, KeyError):
        return None
    stand_in = {}
    for entry in sorted(entries, key=lambda e: e["id"]):
        stand_in.setdefault(entry["rarity"], entry["id"])
    return {"gold": rewards["item_table"], "amounts": {t["type"]: t["amounts"] for t in rewards["heirloom_amount_table"]},
            "trinkets": rewards["trinket_chance_table"], "xp": xp, "stand_in": stand_in}


def to_gold(dd1_gold):
    return max(0, int(dd1_gold))


def pay(quest):
    """(gold, [(heirloom, amount)], [trinket rarity]) of a sample quest.

    A generated quest is paid by DD1's tables for its difficulty and length, in the heirloom types the sample
    names (the game draws two of the region's four). A story quest states its pay in the sample: the numbers
    of the DD1 plot quest it stands for."""
    if quest["plot"] or REWARDS is None:
        return quest["gold"], quest["heirlooms"], quest.get("trinkets", [])
    difficulty, length = DIFFICULTY[quest["tier"]], LENGTH[quest["length"]]

    def cell(table):
        return table[difficulty][length] if difficulty < len(table) and length < len(table[difficulty]) else None

    gold = sum(i["amount"] for i in (cell(REWARDS["gold"]) or []) if i["type"] == "gold")
    heirlooms = [(h, cell(REWARDS["amounts"].get(h, [])) or 0) for h, _ in quest["heirlooms"]]
    trinkets = [t["rarity"] for t in REWARDS["trinkets"] if (cell(t["chances"]) or 0) > 0][:1]
    return to_gold(gold), heirlooms, trinkets


def progress(done, thresholds):
    """QuestBoard.Progress: level by quests done in the region, and the share of the way to the next one."""
    level = max(i for i, t in enumerate(thresholds) if done >= t)
    if level + 1 >= len(thresholds):
        return level, 1.0, 0
    floor, ceiling = thresholds[level], thresholds[level + 1]
    return level, (done - floor) / float(ceiling - floor), ceiling - done


def dd1_quest_text(strings, kind, quest):
    """DD1's name or description of a generated quest: town_quest_<kind>_<type>+<length>+<dungeon>+<goal>."""
    for length in (LENGTH[quest["length"]], 2, 1, 3):
        prefix = "town_quest_%s_%s+%d+%s+" % (kind, quest["type"], length, quest["dungeon"])
        for key in sorted(strings):
            if key.startswith(prefix):
                return strings[key]
    return None


def quest_name(strings, quest):
    """QuestMapText.QuestName: DD1's own name of a generated quest ("Scout", "Cleanse"), a story quest's own."""
    return (None if quest["plot"] else dd1_quest_text(strings, "name", quest)) or quest["name"]


def description(strings, quest):
    return quest.get("intro") or dd1_quest_text(strings, "description", quest) or TYPE_DESCRIPTIONS.get(quest["type"], "")


def specifics(strings, quest):
    """("Short | ", "Apprentice (Lvl 1)", ""): the difficulty apart, for DD1's colour of it."""
    fmt = strings["town_quest_specifics_format"]
    before, _, rest = fmt.partition("%s")
    between, _, after = rest.partition("%s")
    return (before + strings["town_quest_length_%d" % LENGTH[quest["length"]]] + between,
            strings["town_quest_difficulty_%d" % DIFFICULTY[quest["tier"]]], after)


def classes_of(state):
    """The party's DD1 class ids as the tray shows them, from the back rank to the front."""
    by_name = dict((name, cls.lower().replace(" ", "_").replace("-", "_")) for name, cls, _ in state["roster"])
    return [by_name.get(name, "") for name, _ in reversed(state["party"])]


def ready(state, caps):
    """QuestPanel.Ready: a quest is chosen, somebody goes, nobody of them refuses."""
    quest = state["offers"][state["selected"]] if state["selected"] is not None else None
    if quest is None or not state["party"]:
        return False
    cap = caps[DIFFICULTY[quest["tier"]]]
    return all(level <= cap for _, level in state["party"])


def node_tooltip(strings, dungeon, state, thresholds):
    """QuestMapText.RegionTooltip: DD1's words on a region's bar."""
    if dungeon in state["locks"]:
        return strings["town_quest_locked"]
    if dungeon == "darkestdungeon":
        return strings["town_quest_dungeon_not_released"]
    if any(q["dungeon"] == dungeon and q["plot"] and q["type"] == "kill_boss" for q in state["offers"]):
        return strings["town_quest_progress_plot_kill_necromancer_1"]
    _, share, _ = progress(state["done"][dungeon], thresholds)
    return strings["town_quest_progress_format"].replace("%3.0f", "%d" % round(share * 100)).replace("%%", "%")


# ---- drawing -----------------------------------------------------------------------------------------------

_KIT = {}


def kit_for(dd1):
    """tools/preview_windows.py's view of the install: DD1's text styles, its tooltip box, the hamlet's chrome."""
    import preview_windows as pw
    if dd1 not in _KIT:
        _KIT[dd1] = pw.Kit(dd1)
    return _KIT[dd1]


def styled(canvas, dd1, pos, words, style, anchor="la", colour=None, width=None):
    """QuestPanel.Label with a DD1 text style: the style's font 1:1, shrinking to `width`. anchor as PIL's: l / m / r,
    then a (pos is the top of the line) or m (its middle)."""
    text = kit_for(dd1).text
    y = pos[1] - (text.line_height(style) / 2.0 if anchor[1] == "m" else 0)
    return text.draw(canvas, (pos[0], y), words, style, colour=colour, anchor=anchor[0], max_width=width)


def styled_block(canvas, dd1, pos, words, style, width, height, colour=None):
    import preview_windows as pw
    return pw.block(kit_for(dd1), canvas, pos, words, style, width, height, colour=colour)


def font(size, bold=True, italic=False):
    names = ("georgiaz.ttf", "georgiai.ttf") if italic else ("georgiab.ttf", "timesbd.ttf") if bold else ("georgia.ttf", "times.ttf")
    for name in names:
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            pass
    return ImageFont.load_default()


def load(dd1, rel):
    path = os.path.join(dd1, rel)
    return Image.open(path).convert("RGBA") if os.path.isfile(path) else None


def paste(canvas, img, pos, size=None, alpha=1.0):
    if img is None:
        return
    if size is not None and size != img.size:
        img = img.resize(size, Image.LANCZOS)
    if alpha < 1.0:
        img = img.copy()
        img.putalpha(img.getchannel("A").point(lambda a: int(a * alpha)))
    layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    layer.paste(img, (int(round(pos[0])), int(round(pos[1]))))
    canvas.alpha_composite(layer)


def fill(canvas, box, colour):
    layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    ImageDraw.Draw(layer).rectangle(box, fill=colour)
    canvas.alpha_composite(layer)


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


def paragraph(canvas, pos, text, width, height, size, colour, bold=False, italic=False, min_size=14, spacing=1.25):
    """Wrapped text that shrinks until it fits the box, like TMP's auto size. Returns the height used."""
    draw = ImageDraw.Draw(canvas)
    while True:
        fnt = font(size, bold, italic)
        lines = wrap(draw, text, fnt, width)
        step = int(round(size * spacing))
        if len(lines) * step <= height or size <= min_size:
            break
        size -= 1
    for i, line in enumerate(lines):
        if (i + 1) * step > height + step // 2:
            break
        draw.text((pos[0], pos[1] + i * step), line, font=fnt, fill=colour, anchor="la")
    return len(lines) * step


def gradient(size, left, right):
    w, h = size
    img = Image.new("RGBA", (w, h))
    px = img.load()
    for x in range(w):
        t = x / float(max(1, w - 1))
        c = tuple(int(round(left[i] + (right[i] - left[i]) * t)) for i in range(4))
        for y in range(h):
            px[x, y] = c
    return img


def add(a, b):
    return (a[0] + b[0], a[1] + b[1])


def marker_positions(layout, dungeon, count):
    """Centres of a region's quest markers: rows of `in_row`, each centred on the start offset
    (QuestMapLayout.MarkerCentres)."""
    origin = add(layout.vec("quest_select_dungeon_layout_" + dungeon, "quest_map_pos"),
                 layout.vec("town_quest_select_dungeon_layout", "quest_button_start_offset"))
    spacing = layout.vec("town_quest_select_dungeon_layout", "quest_button_spacing")
    in_row = layout.in_row(dungeon)
    positions = []
    for i in range(count):
        row, column = divmod(i, in_row)
        in_this_row = min(in_row, count - row * in_row)
        half = (in_this_row - 1) / 2.0 * spacing[0]
        positions.append((origin[0] - half + column * spacing[0], origin[1] + row * spacing[1]))
    return positions


def draw_node(canvas, dd1, layout, strings, colours, thresholds, dungeon, state):
    node = "town_quest_select_dungeon_layout"
    pos = layout.vec("quest_select_dungeon_layout_" + dungeon, "quest_map_pos")
    locked = dungeon in state["locks"]
    paste(canvas, load(dd1, DIR + ("dungeon_no_progressionbar.png" if locked else "dungeon_progressionbar.png")),
          add(pos, layout.vec(node, "background_offset")))
    draw = ImageDraw.Draw(canvas)
    name_at = add(pos, layout.vec(node, "dungeon_name_offset"))
    styled(canvas, dd1, name_at, strings["dungeon_name_" + dungeon], "town_quest_select_dungeon_name", "ra", None if not locked else "neutral", 320)
    event = state.get("event") or {}
    if event.get("dungeon") == dungeon:       # DD1 marks the region the week's town event is about
        paste(canvas, load(dd1, DIR + "quest_select_town_event.png"), add(pos, layout.vec(node, "town_event_icon_offset")))
    if locked:
        paste(canvas, load(dd1, DIR + "locked_dungeon.png"), add(pos, layout.vec(node, "locked_icon_offset")))
        return
    if dungeon == "darkestdungeon":
        done, total = state["darkest"]
        level, share = done, done / float(total)
    else:
        level, share, _ = progress(state["done"][dungeon], thresholds)
    bar_at = add(pos, layout.vec(node, "dungeon_xp_bar_offset"))
    bar = layout.vec(node, "dungeon_xp_bar_size")
    fill(canvas, (bar_at[0], bar_at[1], bar_at[0] + bar[0] - 1, bar_at[1] + bar[1] - 1), (0, 0, 0, 255))
    width = int(round(bar[0] * share))
    if width > 0:
        full = gradient(bar, colours["town_quest_select_xp_bar_gradient_left"], colours["town_quest_select_xp_bar_gradient_right"])
        paste(canvas, full.crop((0, 0, width, bar[1])), bar_at)
    level_at = add(pos, layout.vec(node, "dungeon_level_offset"))
    styled(canvas, dd1, level_at, str(level), "town_quest_select_dungeon_level", "ma")


def draw_marker(canvas, dd1, layout, quest, centre, selected):
    if selected:
        paste(canvas, load(dd1, DIR + "quest_select_selected.png"), add(centre, layout.vec("town_quest_select_quest_button_layout", "selected_background_offset")))
    frame = load(dd1, DIR + "quest_select_length_%s_%d.png" % ("plot" if quest["plot"] else "generated", LENGTH[quest["length"]]))
    icon = load(dd1, DIR + "quest_select_%s_%d.png" % (quest["type"], DIFFICULTY[quest["tier"]]))
    for img in (frame, icon):
        if img is not None:
            paste(canvas, img, (centre[0] - img.width / 2.0, centre[1] - img.height / 2.0))


def draw_tooltip(canvas, dd1, top_left, title, body):
    """QuestMapTooltip (Dd1Tooltip.ShowAt): DD1's tooltip box with its corner where the layout puts it; it stays on the map."""
    import preview_windows as pw
    kit = kit_for(dd1)
    lines = [w for part in ([title] if title else []) + (body or "").split("\n") for w in kit.text.wrap(part, "tooltip", TOOLTIP_WIDTH)]
    width = min(TOOLTIP_WIDTH, max([kit.text.width(w, "tooltip") for w in lines] or [0])) + 36
    x = max(6, min(top_left[0], MAP_RIGHT - width - 6))
    pw.tooltip(kit, canvas, (x, max(6, top_left[1])), title, body, TOOLTIP_WIDTH)


def card(canvas, dd1, layout, colours, rel, amount, pos):
    paste(canvas, load(dd1, rel), pos, CARD)
    # the count stands at amount_text_offset of the cell, the card icon_offset inside it
    icon, count = layout.vec("inventory_item_layout", "icon_offset"), layout.vec("inventory_item_layout", "amount_text_offset")
    at = (pos[0] + count[0] - icon[0], pos[1] + count[1] - icon[1])
    # some cards are bright in the corner: the count (DD1's inventory_amount) gets a black twin behind it
    styled(canvas, dd1, (at[0] + 1.5, at[1] + 1.5), str(amount), "inventory_amount", "la", (0, 0, 0), CARD[0] - 10)
    styled(canvas, dd1, at, str(amount), "inventory_amount", "la", None, CARD[0] - 10)


def trinket_card(canvas, dd1, rarity, pos):
    """DD1's card of the rarity; in the game DD2's icon of the trinket (64 px) sits in its middle."""
    paste(canvas, load(dd1, "panels/icons_equip/trinket/rarity_%s.png" % rarity), pos, CARD)
    stand_in = REWARDS["stand_in"].get(rarity) if REWARDS else None
    art = load(dd1, "panels/icons_equip/trinket/inv_trinket+%s.png" % stand_in) if stand_in else None
    if art is not None:
        w, h = art.size
        side = min(w, h)
        crop = art.crop(((w - side) // 2, (h - side) // 2, (w + side) // 2, (h + side) // 2))
        paste(canvas, crop, (pos[0] + (CARD[0] - TRINKET_ICON) / 2, pos[1] + (CARD[1] - TRINKET_ICON) / 2), (TRINKET_ICON, TRINKET_ICON))


def draw_panel(canvas, dd1, layout, strings, colours, caps, state):
    block = "town_quest_select_quest_layout"
    origin = layout.vec(block, "description_pos")
    paste(canvas, load(dd1, DIR + "quest_select.questverbose_bg.png"), origin)

    # the screen's own heading, in the blank head of the panel art (DD1: icon and name of a town screen)
    icon_at = layout.vec("town_quest_select_layout", "name_pos")
    paste(canvas, load(dd1, DIR + "quest_select.icon.png"), icon_at)
    draw = ImageDraw.Draw(canvas)
    styled(canvas, dd1, (icon_at[0] + ICON_SIZE + TITLE_GAP, icon_at[1] + ICON_SIZE / 2), strings["town_name_quest_select"], "town_name", "lm")

    quest = state["offers"][state["selected"]] if state["selected"] is not None else None
    if quest is None:
        at = add(origin, layout.vec(block, "name_text_offset"))
        styled(canvas, dd1, at, "No quests this week", "town_quest_name", "ma", "neutral")
        return

    at = add(origin, layout.vec(block, "name_text_offset"))
    styled(canvas, dd1, (at[0], at[1] + 20), quest_name(strings, quest), "town_quest_name", "mm", None, 352)

    text_at = add(origin, layout.vec(block, "description_text_offset"))
    width = layout.vec(block, "description_text_width")[0]
    styled_block(canvas, dd1, text_at, description(strings, quest), "town_quest_description", width, origin[1] + DESCRIPTION_BOTTOM - text_at[1])

    draw = ImageDraw.Draw(canvas)
    styled(canvas, dd1, add(origin, layout.vec(block, "camping_text_offset")), "0", "town_quest_camping")
    # one centred line, the difficulty in DD1's colour of that difficulty (town_quest_difficulty_N)
    before, difficulty, after = specifics(strings, quest)
    words = kit_for(dd1).text
    middle = add(origin, layout.vec(block, "specifics_text_offset"))
    x = middle[0] - sum(words.width(part, "town_quest_specifics") for part in (before, difficulty, after)) / 2.0
    tint = colours.get("town_quest_difficulty_%d" % DIFFICULTY[quest["tier"]])
    for part, colour in ((before, None), (difficulty, tint[:3] if tint else None), (after, None)):
        if part:
            styled(canvas, dd1, (x, middle[1]), part, "town_quest_specifics", "la", colour)
            x += words.width(part, "town_quest_specifics")
    styled(canvas, dd1, add(origin, layout.vec(block, "goals_text_offset")), strings["town_quest_goals"], "town_quest_goals")
    goal_at = add(origin, layout.vec(block, "goal_description_text_start_offset"))
    styled_block(canvas, dd1, goal_at, quest["goal"], "town_quest_goal_start_description", origin[0] + PANEL_TEXT_X + width - goal_at[0], 56)
    styled(canvas, dd1, add(origin, layout.vec(block, "rewards_text_offset")), strings["town_quest_rewards"], "town_quest_rewards", "ma")

    # rewards: a centred row of DD1 item cards
    grid = "town_quest_rewards_inventory_system_grid_layout"
    start = add(add(add(origin, layout.vec(block, "rewards_inventory_system_offset")), layout.vec(grid, "start_pos")),
                layout.vec("inventory_item_layout", "icon_offset"))
    pitch = layout.vec(grid, "offset")[0]
    columns = layout.vec(grid, "number_of_columns")[0]
    gold, heirlooms, trinkets = pay(quest)
    rewards = [("panels/icons_equip/gold/inv_gold+_%d.png" % gold_pile(dd1, gold), gold)]
    rewards += [("panels/icons_equip/heirloom/inv_heirloom+%s.png" % h, n) for h, n in heirlooms if n > 0]
    cards = len(rewards) + len(trinkets)
    # DD1's grid has four columns, which is what its quests pay; more than that moves closer together
    if cards > columns:
        pitch = pitch * (columns - 1) / float(cards - 1)
    shift = max(0, columns - cards) * layout.vec(grid, "offset")[0] / 2.0
    for i, (rel, amount) in enumerate(rewards):
        card(canvas, dd1, layout, colours, rel, amount, (start[0] + shift + i * pitch, start[1]))
    for i, rarity in enumerate(trinkets):
        trinket_card(canvas, dd1, rarity, (start[0] + shift + (len(rewards) + i) * pitch, start[1]))

    # the panel's foot: the week's town event, when it is about this quest (the bell, "Town Event:", what it does)
    event = state.get("event") or {}
    if event.get("line") and event.get("dungeon") in (None, quest["dungeon"]):
        paste(canvas, load(dd1, DIR + "quest_select_town_event_notification.png"), add(origin, layout.vec(block, "town_notification_icon")))
        style = "town_quest_select_town_event_notification"
        room = min(layout.vec(block, "town_notification_text_width")[0], 400 - layout.vec(block, "town_notification_title_text")[0] - 12)
        styled(canvas, dd1, add(origin, layout.vec(block, "town_notification_title_text")), strings["str_quest_select_town_event_title"], style, "la", "neutral", room)
        styled_block(canvas, dd1, add(origin, layout.vec(block, "town_notification_text")), event["line"], style, room, 60, "neutral")


def draw_party(canvas, dd1, layout, strings, colours, caps, state):
    """What stands on the estate's bar and over it: the tray, the party's name, the way forward."""
    tray = layout.vec("town_screen_layout", "embark_party_pos")
    paste(canvas, load(dd1, "campaign/town/embark_party/embark_party.background.png"), add(tray, layout.vec("embark_party_layout", "background_offset")))
    start = add(tray, layout.vec("embark_party_layout", "hero_slot_start_offset"))
    spacing = layout.vec("embark_party_layout", "hero_slot_spacing")
    quest = state["offers"][state["selected"]] if state["selected"] is not None else None
    cap = caps[DIFFICULTY[quest["tier"]]] if quest else 99
    for i in range(4):
        # rank 1, the first hero chosen, on the right
        at = (start[0] + spacing[0] * (3 - i), start[1] + spacing[1] * (3 - i))
        paste(canvas, load(dd1, "campaign/town/hero_slot/hero_slot.background.png"), at)
        if i < len(state["party"]):
            name, level = state["party"][i]
            fill(canvas, (at[0] + PORTRAIT_INSET, at[1] + PORTRAIT_INSET, at[0] + SLOT_SIZE - PORTRAIT_INSET - 1, at[1] + SLOT_SIZE - PORTRAIT_INSET - 1), (62, 58, 52, 255))
            ImageDraw.Draw(canvas).text((at[0] + SLOT_SIZE / 2, at[1] + SLOT_SIZE / 2), name[:2], font=font(26), fill=(150, 140, 120, 255), anchor="mm")
            if level > cap:
                paste(canvas, load(dd1, "campaign/town/hero_slot/hero_slot.negative_frame.png"), at)

    # the party's name: DD1's for these four in this order, or its word to a party it has no name for
    name = party_name(dd1, strings, classes_of(state)) if len(state["party"]) == 4 else None
    top = add(layout.vec("town_quest_select_layout", "party_name_pos"), layout.vec("town_quest_select_party_name_layout", "text_offset"))
    if name:
        styled(canvas, dd1, top, name, "town_quest_select_party_name", "ma", None, PARTY_NAME_WIDTH)
    else:
        styled(canvas, dd1, top, strings["party_name_default"], "town_quest_select_party_name_default", "ma",
               colours["town_quest_select_party_name_default"][:3], PARTY_NAME_WIDTH)

    # the way forward, as painted, its word hanging from forward_text_offset like the hamlet's Embark
    forward = layout.vec("progression_layout", "forward_pos")
    paste(canvas, load(dd1, "shared/progression/progression_forward.png"), forward)
    at = add(forward, layout.vec("progression_layout", "forward_text_offset"))
    styled(canvas, dd1, at, strings["town_progression_forward_provision"], "town_progression_forward", "ma", colours["town_progression_forward"][:3], 300)


def draw_chrome(canvas, dd1):
    """The hamlet's own chrome that lies over the map: the roster column over its right edge and the estate's bar
    (gold, heirlooms) over its foot. The hamlet's Embark is put away while the map is open."""
    import preview_hamlet as ph
    kit = kit_for(dd1)
    layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    ph.draw_chrome(layer, kit.town, kit.text, ph.SAMPLE_HEROES[:8], 12, 5, dict(gold=560, bust=113, portrait=102, deed=41, crest=74))
    canvas.alpha_composite(layer.crop((ROSTER_X, 0, SCREEN_W, BAR_TOP)), (ROSTER_X, 0))
    bar = load(dd1, "shared/progression/progression_bar.png")
    if bar is None:
        return
    paste(canvas, bar, (0, BAR_TOP))
    # everything of the bar but its middle, where Embark stands
    for left, right in ((0, 780), (1140, SCREEN_W)):
        canvas.alpha_composite(layer.crop((left, BAR_TOP - 40, right, SCREEN_H)), (left, BAR_TOP - 40))


def draw_question(canvas, dd1, question, answers):
    """DD1's confirm dialog (shared/confirm_dialog), as RaidConfirm lays it out."""
    import preview_windows as pw
    kit = kit_for(dd1)
    fill(canvas, (0, 0, SCREEN_W, SCREEN_H), (0, 0, 0, 128))
    base, art = (960, 200), load(dd1, "shared/confirm_dialog/confirm_dialog.background.png")
    if art is not None:
        paste(canvas, art, (base[0] - art.width / 2.0, base[1]))
    pw.block(kit, canvas, (base[0] - 18, base[1] + 200), question, "confirm_dialog_question", 520, 160, anchor="m", colour="neutral")
    for i, words in enumerate(answers):
        styled(canvas, dd1, (base[0] - 12, base[1] - 20 + 390 + 55 * i + 5), words, "confirm_dialog_answer", "ma", None, 480)


def compose(dd1, out_dir, name, state):
    layout = Layout(dd1)
    strings = load_strings(dd1)
    colours = load_colours(dd1)
    thresholds, caps = load_rules(dd1)
    global REWARDS
    REWARDS = load_rewards(dd1)

    canvas = Image.new("RGBA", (SCREEN_W, SCREEN_H), INK)
    paste(canvas, load(dd1, DIR + "quest_select.background.png"), (0, 0))
    # DD1 lets the thing under the manor flicker up on the map once the Darkest Dungeon has been entered
    # (dd_effect_<dungeon>.png for a quarter of a second): not drawn in a still.

    for dungeon in DUNGEONS:
        draw_node(canvas, dd1, layout, strings, colours, thresholds, dungeon, state)
    selected = None
    for dungeon in DUNGEONS:
        quests = [(i, q) for i, q in enumerate(state["offers"]) if q["dungeon"] == dungeon]
        for (index, quest), centre in zip(quests, marker_positions(layout, dungeon, len(quests))):
            if index == state["selected"]:
                selected = (quest, centre)
            else:
                draw_marker(canvas, dd1, layout, quest, centre, False)
    if selected:
        draw_marker(canvas, dd1, layout, selected[0], selected[1], True)

    # the picture a town event about the party puts behind the tray: the middle of its foot at
    # town_event_overlay_offset; its foot is cut straight and hides behind the estate's bar
    event = state.get("event") or {}
    picture = load(dd1, "campaign/town/embark_party/%s.png" % event["id"]) if event.get("id") else None
    if picture is not None:
        foot = add(layout.vec("town_screen_layout", "embark_party_pos"), layout.vec("embark_party_layout", "town_event_overlay_offset"))
        paste(canvas, picture, (foot[0] - picture.width / 2.0, foot[1] - picture.height))

    back = layout.vec("progression_layout", "back_pos")
    paste(canvas, load(dd1, "shared/progression/progression_back.png"), back)

    draw_panel(canvas, dd1, layout, strings, colours, caps, state)
    draw_chrome(canvas, dd1)
    draw_party(canvas, dd1, layout, strings, colours, caps, state)

    if state.get("hover"):
        dungeon = state["hover"]
        pos = add(layout.vec("quest_select_dungeon_layout_" + dungeon, "quest_map_pos"), layout.vec("town_quest_select_dungeon_layout", "dungeon_xp_bar_tt_offset"))
        draw_tooltip(canvas, dd1, pos, None, node_tooltip(strings, dungeon, state, thresholds))

    # DD1's question to a party short of four, before it goes on to the provision screen
    if state.get("asked") and ready(state, caps) and len(state["party"]) < 4:
        draw_question(canvas, dd1, strings["town_quest_quest_select_small_team_confirm"],
                      (strings["town_quest_select_confirm_yes"], strings["town_quest_select_confirm_no"]))

    path = os.path.join(out_dir, name)
    canvas.convert("RGB").save(path)
    return path


# ---- sample states ---------------------------------------------------------------------------------------
# "done" is a region's points (DD1: a short quest counts 2, a medium 3, a long 4). A generated quest names its
# two heirloom types; what it pays is read from DD1's tables (pay()). A story quest states the pay of the DD1
# plot quest it stands for (quest.plot_quests.json), gold already at the estate's scale.

def quest(name, dungeon, type_id, length, tier, heirlooms, goal, plot=False, intro=None, gold=0, trinkets=()):
    return {"name": name, "dungeon": dungeon, "type": type_id, "length": length, "tier": tier, "gold": gold,
            "heirlooms": heirlooms, "goal": goal, "plot": plot, "intro": intro, "trinkets": list(trinkets)}


ROSTER = [("Reynauld", "Crusader", 2), ("Dismas", "Highwayman", 2), ("Paracelsus", "Plague Doctor", 1), ("Audrey", "Grave Robber", 1),
          ("Junia", "Vestal", 0), ("Boudica", "Hellion", 3)]
EXPLORE, CLEANSE = "Explore 90% of rooms.", "Complete 100% of room battles."

MID = {
    "done": {"crypts": 7, "weald": 3, "warrens": 2, "cove": 0},
    "locks": {"darkestdungeon": "Story quests that open the way: 0 of 8 done."},
    "darkest": (0, 5),
    "offers": [
        quest("Cleanse the Ruins", "crypts", "cleanse", "short", "apprentice", [("crest", 0), ("bust", 0)], CLEANSE),
        quest("The Keeper of the Catalogue", "crypts", "kill_boss", "medium", "apprentice", [("crest", 6), ("bust", 4)],
              "Find the Librarian in the Ruins and put out his fire.", True,
              "I hired a keeper for the books I dared not read alone. He read them all. When the mob broke the doors he did not flee; "
              "he took their torches and taught them what deserves burning. He is cataloguing still, and you are not on his shelves.",
              gold=90, trinkets=["very_rare"]),
        quest("Explore the Ruins", "crypts", "explore", "medium", "veteran", [("deed", 0), ("portrait", 0)], EXPLORE),
        quest("Gather in the Ruins", "crypts", "gather", "medium", "apprentice", [("crest", 0), ("deed", 0)], "Gather 3 Holy Relics."),
        quest("Explore the Weald", "weald", "explore", "medium", "apprentice", [("crest", 0), ("portrait", 0)], EXPLORE),
        quest("Cleanse the Weald", "weald", "cleanse", "short", "veteran", [("bust", 0), ("deed", 0)], CLEANSE),
        quest("Cleanse the Warrens", "warrens", "cleanse", "short", "apprentice", [("crest", 0), ("deed", 0)], CLEANSE),
        quest("Explore the Warrens", "warrens", "explore", "short", "apprentice", [("bust", 0), ("portrait", 0)], EXPLORE),
        quest("Cleanse the Cove", "cove", "cleanse", "short", "apprentice", [("crest", 0), ("bust", 0)], CLEANSE),
    ],
    "selected": 1,
    # crusader, highwayman, plague doctor and vestal from the front rank back: DD1's "The Usual Suspects"
    "party": [("Reynauld", 2), ("Dismas", 2), ("Paracelsus", 1), ("Junia", 0)],
    "roster": ROSTER,
    "event": {"id": "remove_quest_hero_level_restriction", "line": "Level Restrictions Removed for Next Quest"},
}

EARLY = {
    "done": {"crypts": 4, "weald": 0, "warrens": 0, "cove": 0},
    "locks": {"weald": "1 more quest completed anywhere on the estate.", "warrens": "2 more quests completed anywhere on the estate.",
              "cove": "2 more quests completed anywhere on the estate.", "darkestdungeon": "Story quests that open the way: 0 of 8 done."},
    "darkest": (0, 5),
    "offers": [
        quest("Explore the Ruins", "crypts", "explore", "short", "apprentice", [("crest", 0), ("bust", 0)], EXPLORE),
        quest("Cleanse the Ruins", "crypts", "cleanse", "medium", "apprentice", [("deed", 0), ("portrait", 0)], CLEANSE),
        quest("Cleanse the Ruins", "crypts", "cleanse", "short", "apprentice", [("crest", 0), ("portrait", 0)], CLEANSE),
        quest("Explore the Ruins", "crypts", "explore", "medium", "apprentice", [("bust", 0), ("deed", 0)], EXPLORE),
    ],
    "selected": 0,
    "party": [("Reynauld", 2), ("Boudica", 3), ("Audrey", 1)],
    "roster": ROSTER,
    "hover": "warrens",
}

LATE = {
    "done": {"crypts": 23, "weald": 17, "warrens": 12, "cove": 42},
    "locks": {},
    "darkest": (2, 5),
    "offers": [
        quest("Gather in the Ruins", "crypts", "gather", "medium", "champion", [("crest", 0), ("bust", 0)], "Gather 3 Holy Relics."),
        quest("Explore the Ruins", "crypts", "explore", "long", "veteran", [("deed", 0), ("portrait", 0)], EXPLORE),
        quest("Cleanse the Ruins", "crypts", "cleanse", "short", "champion", [("crest", 0), ("deed", 0)], CLEANSE),
        quest("The Last Reader", "crypts", "kill_boss", "medium", "champion", [("crest", 30)], "Find the Librarian in the Ruins and put out his fire.", True,
              "Every book I owned has passed through his hands and into the fire.", gold=300, trinkets=["trophy", "very_rare"]),
        quest("Explore the Weald", "weald", "explore", "medium", "veteran", [("crest", 0), ("portrait", 0)], EXPLORE),
        quest("Rekindle the Weald", "weald", "inventory_activate", "medium", "champion", [("bust", 0), ("deed", 0)], "Activate 3 Infected Corpses."),
        quest("Respun", "weald", "kill_boss", "medium", "veteran", [("crest", 5), ("deed", 8)], "Cut down the Mother of Threads in the Weald.", True,
              "A cut thread can be knotted. The daughters have sat their mother back at the loom.", gold=135, trinkets=["very_rare"]),
        quest("Reveille", "weald", "kill_boss", "medium", "veteran", [("crest", 5), ("deed", 8)], "Find the Dreaming General in his keep and end his command.", True,
              "The drums are sounding in the Weald again.", gold=135, trinkets=["very_rare"]),
        quest("Cleanse the Warrens", "warrens", "cleanse", "short", "veteran", [("crest", 0), ("deed", 0)], CLEANSE),
        quest("Explore the Warrens", "warrens", "explore", "long", "champion", [("bust", 0), ("portrait", 0)], EXPLORE),
        quest("Fresh Hooks", "warrens", "kill_boss", "medium", "veteran", [("crest", 5), ("portrait", 4)], "Kill Meat Hook, master of the pens under the Warrens.", True,
              "The tribe has dragged him back to the post and hung him there.", gold=135, trinkets=["very_rare"]),
        quest("Rekindle the Cove", "cove", "inventory_activate", "medium", "champion", [("crest", 0), ("bust", 0)], "Activate 3 Protective Wards."),
        quest("Explore the Cove", "cove", "explore", "short", "champion", [("deed", 0), ("bust", 0)], EXPLORE),
        quest("Cleanse the Cove", "cove", "cleanse", "long", "champion", [("crest", 0), ("portrait", 0)], CLEANSE),
        quest("Gather in the Cove", "cove", "gather", "medium", "veteran", [("crest", 0), ("deed", 0)], "Gather 3 Ancestor's Relics."),
        quest("What the Sea Was Owed", "cove", "kill_boss", "medium", "champion", [("crest", 30)], "Kill the Leviathan beneath the Cove.", True,
              "It has stopped waiting for the tide.", gold=300, trinkets=["trophy", "very_rare"]),
        quest("Last of the Vintage", "cove", "kill_boss", "medium", "champion", [("crest", 30)], "End the Archduke's revel in the wreck at the Cove.", True,
              "He has drunk the Cove dry and now looks inland.", gold=300, trinkets=["very_rare", "very_rare"]),
        quest("The Unblinking Study", "darkestdungeon", "kill_boss", "long", "darkest", [("crest", 18)], "Put out the Focused Fault.", True,
              "I could not look away. Not from the dig, not from the diagrams, not from the thing the diagrams described.", gold=300, trinkets=["very_rare"]),
    ],
    "selected": 14,
    "party": [("Reynauld", 4), ("Dismas", 4), ("Paracelsus", 3)],
    "roster": [(n, c, 4) for n, c, _ in ROSTER],
    "hover": "cove",
    "event": {"id": "embark_party_buff_cove_buff", "dungeon": "cove", "line": "+25% Resolve XP in Cove on Next Quest"},
}


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--dd1", default=os.environ.get("DD1_ROOT", r"E:\Steam\steamapps\common\DarkestDungeon"))
    ap.add_argument("--out", default=os.path.join(here, "..", "_lab", "preview"))
    args = ap.parse_args()
    os.makedirs(args.out, exist_ok=True)
    print(compose(args.dd1, args.out, "quest_map.png", MID))
    print(compose(args.dd1, args.out, "quest_map_early.png", EARLY))
    print(compose(args.dd1, args.out, "quest_map_late.png", LATE))
    # the same party of three pressing "Provision": DD1's question
    print(compose(args.dd1, args.out, "quest_map_question.png", dict(LATE, asked=True, hover=None)))


if __name__ == "__main__":
    main()
