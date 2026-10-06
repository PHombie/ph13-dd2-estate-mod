#!/usr/bin/env python3
"""The hamlet's building windows as the plugin lays them out, drawn offline from the player's own Darkest Dungeon
(1) install with DD1's fonts. Nothing from DD1 is copied into the repo; output goes to _lab/ (gitignored).

Reference for src/DD2Estate/Estate/RosterWindow.cs (the frame every window shares), BuildingNavigation.cs (DD1's
column of building buttons beside it), UpgradeUi.cs (HeroActionFrame, slots, prices), UpgradePane.cs,
BuildingPanel.cs, SanitariumPanel.cs, StageCoachPanel.cs, GuildPanel.cs, BlacksmithPanel.cs, SurvivalistPanel.cs,
NomadWagonPanel.cs, GraveyardPanel.cs, MemoirsPanel.cs and src/DD2Estate/UI/UiKitWindows.cs (Dd1Tooltip,
Dd1Ui.Toned), Dd1ScreenName.cs (DD1's name of a screen: icon, name, "+"): change one, change the other.

    python tools/preview_windows.py [--dd1 <install>] [--out <dir>] [--only stage_coach,guild,...]

Writes window_<name>.png for: stage_coach, tavern, abbey, sanitarium, sanitarium_pick, guild, blacksmith,
survivalist, nomad_wagon, graveyard, memoirs, memoirs_page, and upgrades_<building>.png for every building with
trees. The older generators (preview_building_panel.py, preview_sanitarium.py, preview_upgrades.py,
preview_wagon.py, the survivalist of preview_camp.py) call into this module and keep their file names.

Everything is in DD1's 1920x1080 screen pixels, y down, art at its native size. A number marked FALLBACK is DD1's
own value of a layout entry, used when the file cannot be read; MOD marks what DD1's files have no number for.
What the game supplies at run time (DD2 portraits, skill and trinket icons) is stood in for by DD1 art of the
same size.
"""
import argparse
import os
import re

from PIL import Image

import preview_hamlet as ph
from dd1_bmfont import Dd1Text

SCREEN = (1920, 1080)
ROSTER_W = 370                       # RosterPanel.Width
FRAME = (1395, 776)                  # <building>.character_background.png
INFO_SIZE = (700, 44)                # MOD: room of the window's line of text
SLOT = 85                            # hero_slot.background.png
SLOT_INSET = 3                       # MOD: the slot's frame stays clear of the portrait
COST_ICON = 20                       # MOD: a heirloom or the coin beside an amount (DD1's art is 40 / 24 px)
NAV_ORDER = ["stage_coach", "blacksmith", "guild", "camping_trainer", "tavern", "abbey", "sanitarium", "nomad_wagon", "graveyard", "statue"]   # FALLBACK
BUILDINGS_DIR = "campaign/town/buildings/"
SLOT_DIR = "campaign/town/hero_slot/"
NODE_DIR = BUILDINGS_DIR + "upgrade/"
HEIRLOOMS = ["crest", "deed", "bust", "portrait"]

HEROES = [  # name, DD1 class for the stand-in art, class and path
    ("Reynauld", "crusader", "Crusader, Wanderer"), ("Dismas", "highwayman", "Highwayman, Wanderer"),
    ("Paracelsus", "plague_doctor", "Plague Doctor, Alchemist"), ("Barristan", "man_at_arms", "Man-at-Arms, Vanguard"),
    ("Audrey", "grave_robber", "Grave Robber, Nightsworn"), ("Boudica", "hellion", "Hellion, Ravager"),
    ("Tardif", "bounty_hunter", "Bounty Hunter, Wanderer"),
]


class Kit:
    """The DD1 install: art, layout files, strings and text styles, each read once."""

    def __init__(self, dd1):
        self.dd1 = dd1
        self.text = Dd1Text(dd1)
        self.town = ph.Town(dd1)
        self._art, self._layouts, self._strings, self._base = {}, {}, None, None

    def art(self, rel):
        if rel not in self._art:
            path = os.path.join(self.dd1, *rel.split("/"))
            self._art[rel] = Image.open(path).convert("RGBA") if os.path.isfile(path) else None
        return self._art[rel]

    def layout(self, rel):
        if rel not in self._layouts:
            path = os.path.join(self.dd1, *rel.split("/"))
            blocks = {}
            if os.path.isfile(path):
                with open(path, encoding="utf-8-sig") as f:
                    blocks = ph.parse_darkest(f.read())
            self._layouts[rel] = blocks
        return self._layouts[rel]

    def vec(self, rel, block, key, x, y):
        """Dd1Ui.Offset: a pair of numbers of a layout entry, or DD1's stock values."""
        entry = self.layout(rel).get(block, {})
        try:
            return (float(entry[key][0]), float(entry[key][1]))
        except (KeyError, IndexError, ValueError):
            return (x, y)

    def num(self, rel, block, key, fallback):
        entry = self.layout(rel).get(block, {})
        try:
            return float(entry[key][0])
        except (KeyError, IndexError, ValueError):
            return fallback

    def string(self, key, fallback=None):
        """WindowText.Get: the English text of the main string table, or of the heroes' table."""
        if self._strings is None:
            self._strings = {}
            for name in ("miscellaneous.string_table.xml", "heroes.string_table.xml"):
                path = os.path.join(self.dd1, "localization", name)
                if not os.path.isfile(path):
                    continue
                with open(path, encoding="utf-8", errors="replace") as f:
                    text = f.read()
                start = text.find('<language id="english">')
                end = text.find("</language>", start)
                for key2, value in re.findall(r'<entry id="([^"]+)"><!\[CDATA\[(.*?)\]\]></entry>', text[start:end], re.S):
                    self._strings.setdefault(key2, value)
        text = self._strings.get(key)
        if text is None:
            return fallback
        return " ".join(re.sub(r"\{[^}]*\}", "", text).split())      # Dd1Strings.Format: no colour marks, no line breaks

    def base(self):
        """The hamlet with its chrome: what lies under every window."""
        if self._base is None:
            canvas, _ = ph.compose(self.town, 1, set(), hover=False)
            ph.draw_chrome(canvas, self.town, self.text, ph.SAMPLE_HEROES[:8], 12, 5, dict(gold=560, bust=113, portrait=102, deed=41, crest=74))
            self._base = canvas
        return self._base.copy()


# ---------------------------------------------------------------- drawing helpers

def add(a, b):
    return (a[0] + b[0], a[1] + b[1])


def paste(canvas, img, pos, size=None, alpha=1.0, tint=None):
    if img is None:
        return
    if size is not None and (int(size[0]), int(size[1])) != img.size:
        img = img.resize((max(1, int(size[0])), max(1, int(size[1]))), Image.LANCZOS)
    if tint is not None or alpha < 1.0:
        r, g, b, a = img.split()
        if tint is not None:
            r, g, b = (ch.point(lambda v, t=t: int(v * t)) for ch, t in zip((r, g, b), tint))
        if alpha < 1.0:
            a = a.point(lambda v: int(v * alpha))
        img = Image.merge("RGBA", (r, g, b, a))
    canvas.alpha_composite(img, (int(round(pos[0])), int(round(pos[1]))))


def box(canvas, rect, colour):
    x, y, w, h = (int(round(v)) for v in rect)
    if w > 0 and h > 0:
        canvas.alpha_composite(Image.new("RGBA", (w, h), tuple(colour)), (x, y))


def line(kit, canvas, pos, words, style, anchor="l", colour=None, width=None):
    """Dd1Ui.Line: one line 1:1, shrinking to `width`."""
    return kit.text.draw(canvas, pos, words, style, colour=colour, anchor=anchor, max_width=width)


def block(kit, canvas, pos, words, style, width, height, anchor="l", colour=None):
    """Dd1Ui.Block: wraps at `width`, shrinks to stay inside `height`. pos is the corner (anchor l) or the top
    centre (anchor m) of the box. Returns the height used."""
    font = kit.text.font(style)
    size = font.native_size
    while True:
        lines = []
        for paragraph in words.split("\n"):
            lines += kit.text.wrap(paragraph, style, width, size) or [""]
        step = kit.text.line_height(style, size)
        if len(lines) * step <= height or size <= 11:
            break
        size -= 1
    for i, words_line in enumerate(lines):
        kit.text.draw(canvas, (pos[0], pos[1] + i * step), words_line, style, colour=colour, anchor=anchor, size=size)
    return len(lines) * step


def tooltip(kit, canvas, top_left, title, body, width=300, fixed=False):
    """Dd1Tooltip: DD1's box cut in nine (tooltip.layout.darkest border_texture_threshold), heading and text. `fixed`:
    the box is `width` wide whatever it says (a DD1 tooltip layout with tooltip_is_auto_width 0)."""
    pad_x, pad_y = 18, 14                            # MOD: the text stays inside the hairline drawn in the art
    lines = []
    if title:
        lines += [(w, "upgrade_tree_tooltip_title") for w in kit.text.wrap(title, "tooltip", width)]
    for paragraph in (body or "").split("\n"):
        colour = "neutral"
        if paragraph.startswith("!"):               # a line the plugin writes in DD1's warning red
            colour, paragraph = "harmful", paragraph[1:]
        elif paragraph.startswith("*"):             # ... in DD1's gold
            colour, paragraph = "notable", paragraph[1:]
        lines += [(w, colour) for w in kit.text.wrap(paragraph, "tooltip", width)]
    step = kit.text.line_height("tooltip")
    text_w = max([kit.text.width(w, "tooltip") for w, _ in lines] or [0])
    w, h = int((width if fixed else min(width, text_w)) + 2 * pad_x) + 1, int(len(lines) * step + 2 * pad_y)
    art = kit.art("shared/tooltip/tooltip_background.png")
    x, y = int(top_left[0]), int(top_left[1])
    x = max(6, min(x, 144 + FRAME[0] - w - 6))       # Dd1Tooltip.Place: inside the frame
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
    for i, (words, colour) in enumerate(lines):
        kit.text.draw(canvas, (x + pad_x, y + pad_y + i * step), words, "tooltip", colour=colour)


def portrait(kit, dd1_class):
    return kit.art("heroes/%s/%s_A/%s_portrait_roster.png" % (dd1_class, dd1_class, dd1_class))


def slot(kit, canvas, pos, dd1_class=None, lit=False, grey=False):
    """UpgradeUi.BuildSlot: DD1's hero slot with a portrait cut off inside its frame."""
    paste(canvas, kit.art(SLOT_DIR + ("hero_slot.backgroundhightlight.png" if lit else "hero_slot.background.png")), pos)
    face = portrait(kit, dd1_class) if dd1_class else None
    if face is not None:
        inner = SLOT - 2 * SLOT_INSET
        paste(canvas, face, add(pos, (SLOT_INSET, SLOT_INSET)), (inner, inner), tint=(0.5, 0.5, 0.5) if grey else None)


ESTATE = "shared/estate/estate.layout.darkest"


def price(kit, canvas, cost_pos, pairs, purse=None, locked=False):
    """UpgradeUi.BuildPrice: a price hung from DD1's cost position. The row is centred on its x; gold has its 24 px
    coin from 12 px above the position and the amount 25 px on from 14 px above, an heirloom its 40 px icon from 10 px
    above and the amount 38 px on from 4 px above, the next heirloom 2 px after the amount (estate.layout.darkest)."""
    parts, x = [], 0
    for i, (kind, amount) in enumerate(pairs):
        gold = kind == "gold"
        block = "estate_currency_gold_layout" if gold else "estate_currency_heirloom_layout"
        icon_at = kit.vec(ESTATE, block, "icon_offset", 0, -12 if gold else -10)                   # FALLBACK
        number_at = kit.vec(ESTATE, block, "number_offset", 25 if gold else 38, -14 if gold else -4)
        gap = kit.vec(ESTATE, block, "next_currency_spacing", 0 if gold else 2, 0)[0]
        words = "{:,}".format(amount)
        parts.append((kind, amount, words, (x + icon_at[0], icon_at[1]), (x + number_at[0], number_at[1])))
        x += number_at[0] + kit.text.width(words, "town_currency_amount") + (gap if i < len(pairs) - 1 else 0)
    left = round(cost_pos[0] - x / 2)
    for kind, amount, words, icon_at, number_at in parts:
        paste(canvas, kit.art("shared/estate/currency.%s.icon.png" % kind), (left + icon_at[0], cost_pos[1] + icon_at[1]), None, 0.55 if locked else 1.0)
        short = purse is not None and purse.get(kind, 0) < amount
        colour = (158, 148, 128) if locked else "harmful" if short else None
        kit.text.draw(canvas, (left + number_at[0], cost_pos[1] + number_at[1]), words, "town_currency_amount", colour=colour)


def costs(kit, canvas, centre_top, pairs, purse=None, locked=False):
    """UpgradeUi.BuildCosts, the older row: icon, amount, icon, amount at half of DD1's icon size, centred on centre_top."""
    widths = [kit.text.width(str(amount), "town_currency_amount") for _, amount in pairs]
    total = sum(COST_ICON + 2 + w for w in widths) + 5 * (len(pairs) - 1)
    x = centre_top[0] - total / 2
    for (kind, amount), w in zip(pairs, widths):
        paste(canvas, kit.art("shared/estate/currency.%s.icon.png" % kind), (x, centre_top[1] + 2), (COST_ICON, COST_ICON), 0.55 if locked else 1.0)
        x += COST_ICON + 2
        short = purse is not None and purse.get(kind, 0) < amount
        colour = (158, 148, 128) if locked else "harmful" if short else None
        kit.text.draw(canvas, (x, centre_top[1]), str(amount), "town_currency_amount", colour=colour)
        x += w + 5


def node(kit, canvas, icon_pos, state, linked=True):
    """UpgradeUi.BuildNode: a step's 50 px icon, with DD1's gold backing and link once it is bought."""
    up = NODE_DIR + "upgrade.layout.darkest"
    icon = kit.vec(up, "upgrade_requirement_layout", "icon_offset", 40, 10)
    back = kit.vec(up, "upgrade_requirement_layout", "background_offset", 30, 0)
    link = kit.vec(up, "upgrade_requirement_layout", "background_connector_offset", 0, 24)
    if state == "bought":
        if linked:
            paste(canvas, kit.art(NODE_DIR + "requirement_purchased_background_connector.png"), add(icon_pos, (link[0] - icon[0], link[1] - icon[1])))
        paste(canvas, kit.art(NODE_DIR + "requirement_purchased_background.png"), add(icon_pos, (back[0] - icon[0], back[1] - icon[1])))
    name = {"bought": "requirement_purchased_icon.png", "open": "requirement_purchasable_icon.png"}.get(state, "requirement_locked_icon.png")
    paste(canvas, kit.art(NODE_DIR + name), icon_pos)


# ---------------------------------------------------------------- DD1's screen name (UI/Dd1ScreenName.cs)

NAME = "shared/name/name.layout.darkest"
NAME_WIDTH = 320                     # MOD: a name wider than this shrinks (DD1's longest, "Ancestor's Memoirs", is 304)


def sheet_picture(kit, sheet, name):
    """Dd1Install.Sprite("sheet.png#name"): one picture out of a Spine sheet (upright ones only, which these are)."""
    key = sheet + "#" + name
    if key not in kit._art:
        page, picture = kit.art(sheet), None
        path = os.path.join(kit.dd1, *(sheet[:-4] + ".atlas").split("/"))
        if page is not None and os.path.isfile(path):
            with open(path, encoding="utf-8") as f:
                lines = f.read().replace("\r", "").split("\n")
            for i, text in enumerate(lines):
                if text != name:
                    continue
                entry = dict(part.strip().split(":", 1) for part in lines[i + 1:i + 7] if ":" in part)
                x, y = (int(v) for v in entry["xy"].split(","))
                w, h = (int(v) for v in entry["size"].split(","))
                picture = page.crop((x, y, x + w, y + h))
        kit._art[key] = picture
    return kit._art[key]


def centred(canvas, img, centre, scale=1.0, alpha=1.0):
    """A picture with its middle at a point (a Spine effect stands on its origin); whole pixels, as the plugin places it."""
    if img is None:
        return
    size = (img.width * scale, img.height * scale)
    paste(canvas, img, (int(centre[0] - size[0] / 2), int(centre[1] - size[1] / 2)), size if scale != 1.0 else None, alpha)


def screen_name(kit, canvas, name_pos, icon, words, width=NAME_WIDTH):
    """Dd1ScreenName.Add: the screen's 113 px icon and its name, hung from the screen's name_pos by name_layout. The
    name begins at text_offset and its line stands on it (the foot of DwarvenAxe's 63 px line cell)."""
    paste(canvas, kit.art(icon), add(name_pos, kit.vec(NAME, "name_layout", "icon_offset", 60, 26)))                 # FALLBACK
    text_at = add(name_pos, kit.vec(NAME, "name_layout", "text_offset", 192, 118))                                    # FALLBACK
    kit.text.draw(canvas, (text_at[0], text_at[1] - kit.text.font("town_name").line_height), words, "town_name", max_width=width)


def screen_name_more(kit, canvas, name_pos, open_=False):
    """The "+" of Dd1ScreenName: its middle at sub_icon_offset from the ICON's corner (DD1's own screens: the "+" at
    200, 238 on every building, the icon's bottom middle), the ring of fx/building_upgrade_pulse over it (drawn
    in the middle of its "pulse": 0.9 .. 1.1 of its 73 px, alpha 0.38 .. 0.5)."""
    icon_at = add(name_pos, kit.vec(NAME, "name_layout", "icon_offset", 60, 26))                                      # FALLBACK
    centre = add(icon_at, kit.vec(NAME, "name_layout", "sub_icon_offset", 56, 106))                                   # FALLBACK
    centred(canvas, kit.art("shared/progression/info_icon_frame.png"), centre)
    centred(canvas, kit.art("shared/progression/" + ("less_info_icon.png" if open_ else "more_info_icon.png")), centre)
    centred(canvas, sheet_picture(kit, "fx/building_upgrade_pulse/building_upgrade_pulse.sprite.png", "circle_glow"), centre, 1.0, 0.44)


def exclamation(kit, canvas, centre):
    """Dd1Fx.Exclamation: fx/estate_exclamation at rest (its "alert" swells the mark to 1.1 and the glow to 1.15)."""
    sheet = "fx/estate_exclamation/estate_exclamation.sprite.png"
    centred(canvas, sheet_picture(kit, sheet, "glow"), centre)
    centred(canvas, sheet_picture(kit, sheet, "exclamation"), centre)


def toned(img, darkness, saturation):
    """Dd1Ui.Toned: a picture in one of DD1's dimmed colours (.darkness: the light it keeps, .saturation: the colour)."""
    if img is None:
        return None
    r, g, b, a = img.split()
    grey = Image.merge("RGB", (r, g, b)).convert("L")
    mixed = [Image.blend(grey, ch, saturation).point(lambda v: int(v * darkness)) for ch in (r, g, b)]
    return Image.merge("RGBA", mixed + [a])


# ---------------------------------------------------------------- the frame (RosterWindow.cs)

TOWN = "campaign/town/town.layout.darkest"
BUILDING = BUILDINGS_DIR + "building.layout.darkest"
NAVIGATION = "campaign/town/building_navigation/building_navigation.layout.darkest"


def navigation(kit, canvas, current, locked=(), alerts=()):
    """BuildingNavigation: DD1's column of building buttons left of a building's screen. The open building's button
    is hopped up and in full colour, the others in town_navigation_button_unselected (darkness 0.8, saturation 0.2)."""
    base = kit.vec(TOWN, "town_screen_layout", "button_navigation_pos", 70, 230)                                      # FALLBACK
    block = "building_navigation_layout"
    first = add(base, kit.vec(NAVIGATION, block, "button_start_position", 0, 0))
    step = kit.vec(NAVIGATION, block, "button_spacing", 0, 68)
    hop = kit.vec(NAVIGATION, block, "selected_hop_offset", 0, -5)
    mark = kit.vec(NAVIGATION, block, "exclamation_point_offset", 28, 32)
    lock = kit.vec(NAVIGATION, block, "locked_overlay_offset", 14, 14)
    marks = []
    for stock, building in enumerate(NAV_ORDER):
        index = int(kit.num(NAVIGATION, "building_navigation_building_layout_" + building, "button_index", stock))
        at = add(first, (step[0] * index, step[1] * index))
        art = kit.art("%s%s/%s.town_button.png" % (BUILDINGS_DIR, building, building))
        if building == current:
            at = add(at, hop)
        else:
            art = toned(art, 0.8, 0.2)
        paste(canvas, art, at)
        if building in locked:
            paste(canvas, kit.art("campaign/town/building_navigation/bld_quick_nav_locked_icon.png"), add(at, lock))
        elif building in alerts:
            marks.append(add(at, mark))
    for centre in marks:                 # over every button: a mark is taller than one
        exclamation(kit, canvas, centre)


class Window:
    def __init__(self, kit, building, title=None, pane=False, has_trees=True, backdrop=None, keeper=None, locked=(), alerts=()):
        self.kit = kit
        self.building = building
        self.canvas = kit.base()
        self.area = kit.vec(TOWN, "town_background_layout", "area_pos", 144, 132)                       # FALLBACK
        keeper_pos = kit.vec(TOWN, "town_background_layout", "character_pos", 132, 240)                 # FALLBACK
        body = kit.vec(BUILDING, "building_base_layout", "body_base_pos", 596, 102)                     # FALLBACK
        close = kit.vec(BUILDING, "building_base_layout", "close_pos", 1496, 144)                       # FALLBACK
        info = kit.vec(BUILDING, "building_base_body_layout", "info_text_offset", 580, 760)             # FALLBACK
        self.name_pos = kit.vec(BUILDING, "building_base_layout", "name_pos", 104, 126)                 # FALLBACK
        summary = kit.vec(TOWN, "town_screen_layout", "estate_summary_pos", 0, 975)[1] + kit.vec(
            "campaign/town/estate_summary/estate_summary.layout.darkest", "estate_summary_layout", "pos_offset", 0, -17)[1]
        self.bar_top = int(summary)
        self.body = (body[0] - self.area[0], body[1] - self.area[1])           # RosterWindow.Body
        self.info = add(self.body, info)

        # the darkened town: up to the roster column and down to the estate's bar
        box(self.canvas, (0, 0, SCREEN[0] - ROSTER_W, self.bar_top), (0, 0, 0, int(0.72 * 255)))
        navigation(kit, self.canvas, building, locked, alerts)
        stem = BUILDINGS_DIR + building + "/" + building
        paste(self.canvas, kit.art(backdrop or stem + ".character_background.png"), self.area)
        # the keeper stays while the upgrades are open: the pane's art is 75% black over him
        figure = kit.art(keeper or stem + ".character.png")
        if figure is not None:
            at = (int(keeper_pos[0]), int(keeper_pos[1]))
            paste(self.canvas, figure.crop((0, 0, figure.width, max(1, min(figure.height, self.bar_top - at[1])))), at)   # the feet go under the bar
        screen_name(kit, self.canvas, self.name_pos, stem + ".icon.png", title or kit.string("town_name_" + building, building))
        paste(self.canvas, kit.art("shared/progression/progression_close.png"), self.at((close[0] - self.area[0], close[1] - self.area[1])))
        self.has_trees = has_trees
        if has_trees and not pane:
            self.toggle(False)

    def at(self, pos):
        """A point of the backdrop on the screen."""
        return (self.area[0] + pos[0], self.area[1] + pos[1])

    def toggle(self, open_):
        """DD1's "+" (the X while the upgrades are open): one place, over the pane."""
        screen_name_more(self.kit, self.canvas, self.name_pos, open_)

    def hint(self, words, said=False, trouble=False):
        """RosterWindow.Hint / Say: DD1's place for what a screen has to say."""
        colour = "harmful" if trouble else "neutral" if said else None
        block(self.kit, self.canvas, self.at(self.info), words, "town_building_info", INFO_SIZE[0], INFO_SIZE[1], anchor="m", colour=colour)

    def save(self, out_dir, name):
        path = os.path.join(out_dir, name)
        self.canvas.convert("RGB").save(path)
        return path


class HeroAction:
    """HeroActionFrame: banner (slot, name), DD1's text column about the class, the body's origin."""

    FILE = BUILDINGS_DIR + "hero_action/hero_action.layout.darkest"

    def __init__(self, win, hero, detail, more=None, verbose_width=None):
        kit, f = win.kit, self.FILE
        origin = add(win.body, kit.vec(f, "hero_action_layout", "base_pos", 220, 44))                  # FALLBACK
        verbose = add(origin, kit.vec(f, "hero_action_layout", "verbose_pos", 0, 105))
        self.body = add(origin, kit.vec(f, "hero_action_layout", "body_pos", 240, 121))
        slot_pos = add(self.body, kit.vec(f, "hero_action_banner_layout", "hero_slot_offset", -230, -100))
        name_pos = add(origin, kit.vec(f, "hero_action_banner_layout", "name_offset", 105, 45))
        help_pos = add(origin, kit.vec(f, "hero_action_banner_layout", "help_offset", 100, 46))
        close_x = origin[0] + kit.vec(f, "hero_action_banner_layout", "close_button_offset", 680, 0)[0]
        width = verbose_width or kit.num(f, "hero_action_verbose_layout", "body_text_width", 260)
        title_pos = add(verbose, kit.vec(f, "hero_action_verbose_layout", "title_text_offset", 5, 32))
        body_pos = add(verbose, kit.vec(f, "hero_action_verbose_layout", "body_text_offset", 5, 74))
        self.text_left = body_pos[0]
        c = win.canvas
        slot(kit, c, win.at(slot_pos), hero[1] if hero else None)
        paste(c, kit.art(BUILDINGS_DIR + "hero_action/verbose_frame.png"), win.at(add(verbose, kit.vec(f, "hero_action_verbose_layout", "frame_offset", -20, 0))))
        if hero is None:
            line(kit, c, win.at(help_pos), kit.string("str_empty_hero_slot_action", "Drag a hero here."), "town_action_banner_help")
            return
        name_width = close_x - name_pos[0] - 12
        line(kit, c, win.at(name_pos), hero[0], "town_action_banner_name", width=name_width)
        line(kit, c, win.at(add(name_pos, (2, -25))), detail, "town_character_class", width=name_width)    # MOD: the hero's standing here
        line(kit, c, win.at(title_pos), hero[2], "town_action_verbose_title_text", width=width)
        blurb = kit.string("action_verbose_body_%s_%s" % (win.building, hero[1]), "")
        words = blurb + ("\n\n" + more if more else "") if blurb else (more or "")
        block(kit, c, win.at(body_pos), words, "town_action_verbose_body_text", width, 420 - (body_pos[1] - verbose[1]))


# ---------------------------------------------------------------- the windows

def stage_coach(kit, out, recruits=None):
    f = BUILDINGS_DIR + "stage_coach/stage_coach.layout.darkest"
    win = Window(kit, "stage_coach")
    item = add(win.body, kit.vec(f, "hero_recruit_store", "store_item_pos", 450, 80))                  # FALLBACK
    spacing = kit.vec(f, "hero_recruit_store", "store_item_spacing", 0, 100)
    plate = kit.vec(f, "hero_layout", "hero_background_offset", -100, -8)
    badge = kit.vec(f, "hero_layout", "hero_resolve_level_number_background_offset", -63, 12)
    number = kit.vec(f, "hero_layout", "hero_resolve_level_number_text_offset", -30, 43)
    name = kit.vec(f, "hero_layout", "hero_name_offset", 100, 12)
    description = kit.vec(f, "hero_layout", "hero_description_offset", 100, 42)
    if recruits is None:
        recruits = [("Gournai", "hellion", "Duelist, Instructrice", 0, False), ("Bigby", "abomination", "Abomination, Fiend", 0, False),
                    ("Tardif", "bounty_hunter", "Bounty Hunter, Wanderer", 0, False), ("Wadard", "highwayman", "Highwayman, Yellowhand", 0, False),
                    ("Musard", "crusader", "Crusader, Aggressor", 2, True)]
    c = win.canvas
    if not recruits:
        empty = add(win.body, kit.vec(f, "stagecoach_layout", "empty_info_text_offset", 575, 375))
        block(kit, c, win.at(empty), kit.string("stagecoach_empty_info", "The Stage Coach is empty. Check back after your next quest."), "town_building_info",
              kit.num(f, "stagecoach_layout", "empty_info_text_width", 600), 80, anchor="m")
    pitch = min(spacing[1], (752 - (item[1] + plate[1])) / max(1, len(recruits)))
    for i, (who, cls, what, tier, blocked) in enumerate(recruits):
        at = win.at((item[0], item[1] + pitch * i))
        paste(c, kit.art(BUILDINGS_DIR + "stage_coach/stage_coach.hero_background.png"), add(at, plate), tint=(0.55,) * 3 if blocked else (0.88,) * 3)
        paste(c, kit.art("shared/resolve_level_bar/resolve_level_bar_number_background_lvl%d.png" % tier), add(at, badge))
        w = kit.text.width(str(tier), "resolve_number")
        kit.text.draw(c, (at[0] + number[0] - w / 2, at[1] + number[1] - kit.text.line_height("resolve_number") / 2), str(tier), "resolve_number", colour=(0, 0, 0))
        slot(kit, c, at, cls, grey=blocked)
        line(kit, c, add(at, name), who, "town_roster_name", width=290)
        line(kit, c, add(at, description), what, "town_character_class", width=290)
    tooltip(kit, c, win.at((item[0], item[1] + SLOT + 6)), None, kit.string("str_hero_slot_unlocked_stagecoach_tt", "Drag Hero to Roster to recruit") + ", or click.")
    return win.save(out, "window_stage_coach.png")


def activity_rows(kit, building):
    import json
    path = os.path.join(kit.dd1, "campaign", "town", "buildings", building, building + ".building.json")
    with open(path, encoding="utf-8") as fh:
        return json.load(fh)["data"]["activities"]


def activities(kit, out, building, plain=False, name=None):
    """BuildingPanel.cs: the Tavern and the Abbey."""
    win = Window(kit, building)
    c = win.canvas
    stem = BUILDINGS_DIR + building + "/" + building
    origin = add(win.body, kit.vec(BUILDING, "building_activity_list_layout", "base_pos", 70, 50))    # FALLBACK
    spacing = kit.vec(BUILDING, "building_activity_list_layout", "activity_spacing", 0, 230)
    row = "building_activity_layout"
    name_off = kit.vec(BUILDING, row, "name_offset", 170, 36)
    desc_off = kit.vec(BUILDING, row, "description_offset", 170, 90)
    desc_w = kit.num(BUILDING, row, "description_width", 250)
    slots = kit.vec(BUILDING, row, "slot_list_pos", 440, 119)
    slot_step = kit.vec(BUILDING, row, "slot_spacing", 135, 0)
    cell = "building_activity_slot_layout"
    overlay = kit.vec(BUILDING, cell, "overlay_offset", -26, -70)
    boards = kit.vec(BUILDING, cell, "locked_overlay_offset", -42, -70)
    cost = kit.vec(BUILDING, cell, "cost_offset", 38, -20)
    confirm = kit.vec(BUILDING, cell, "confirm_button_offset", 42, 115)
    cancel = kit.vec(BUILDING, cell, "cancel_button_offset", 7, 112)
    hero_name = kit.vec(SLOT_DIR + "hero_slot.layout.darkest", "town_hero_slot_layout", "name_offset", 45, -70)
    # sample state: (row, slot) -> ("stay" | "wait", hero index)
    state = {} if plain else {(0, 0): ("stay", 1), (1, 0): ("wait", 4)}
    for i, act in enumerate(activity_rows(kit, building)):
        aid = act["id"]
        at = add(origin, (spacing[0] * i, spacing[1] * i))
        line(kit, c, win.at(add(at, name_off)), kit.string("town_activity_name_" + aid, aid), "town_activity_name", width=236)
        relief = act["data"]["stress_upgrades"][0]["heal_low"] / 20.0          # ActivityRules: DD1's 0..200 on DD2's 0..10
        words = kit.string("town_activity_description_" + aid, "")
        used = block(kit, c, win.at(add(at, desc_off)), words, "town_activity_description", desc_w, 100)
        line(kit, c, win.at(add(at, (desc_off[0], desc_off[1] + used))), "Stress -%d to -%d" % (int(relief), int(relief) + 1), "town_activity_description", colour="notable")
        price = int(act["data"]["cost_upgrades"][0]["cost_currency"]["amount"])
        most = max(u.get("number_of_slots", 0) for u in act["data"]["slot_upgrades"])
        open_slots = 1 if plain else 2
        for j in range(most):
            place = win.at(add(add(at, slots), (slot_step[0] * j, slot_step[1] * j)))
            if j >= open_slots:
                paste(c, kit.art(stem + ".locked_hero_slot_overlay.png"), add(place, boards))
                continue
            kind, who = state.get((i, j), (None, None))
            hero = HEROES[who] if who is not None else None
            slot(kit, c, place, hero[1] if hero else None)
            if hero:
                line(kit, c, add(place, hero_name), hero[0], "hero_slot_name", anchor="m", width=slot_step[0] - 4)
            if kind == "stay":
                paste(c, kit.art("%s.%s.hero_slot_overlay.png" % (stem, aid)), add(place, overlay))
                paste(c, kit.art(BUILDINGS_DIR + "hero_activity/hero_activity.cancel_button.png"), add(place, cancel))
            else:
                costs(kit, c, add(place, (cost[0], cost[1] - 13)), [("gold", price)])      # cost_offset: the middle of the price
            if kind == "wait":
                paste(c, kit.art(BUILDINGS_DIR + "hero_activity/hero_activity.confirm_button.png"), add(place, (confirm[0] - 32, confirm[1])))
    if plain:
        win.hint("Drag a hero from the roster into a slot, or click a slot and then the hero. The check mark pays; stress is relieved when the week ends.")
    else:
        win.hint("Audrey waits at the %s: the check mark pays %d gold." % (kit.string("town_activity_name_" + activity_rows(kit, building)[1]["id"], ""),
                 int(activity_rows(kit, building)[1]["data"]["cost_upgrades"][0]["cost_currency"]["amount"])), said=True)
    return win.save(out, name or ("window_%s%s.png" % (building, "_plain" if plain else "")))


SANITARIUM = BUILDINGS_DIR + "sanitarium/sanitarium.layout.darkest"


def sanitarium(kit, out, pick=False, name=None, disease=False):
    """SanitariumPanel.cs: the two wards; with `pick` a hero stands at a cell and chooses a quirk."""
    win = Window(kit, "sanitarium")
    c = win.canvas
    stem = BUILDINGS_DIR + "sanitarium/sanitarium"
    origin = add(win.body, kit.vec(BUILDING, "building_activity_list_layout", "base_pos", 70, 50))    # FALLBACK
    spacing = kit.vec(BUILDING, "building_activity_list_layout", "activity_spacing", 0, 230)
    row = "sanitarium_activity_layout"
    name_off = kit.vec(SANITARIUM, row, "name_offset", 170, 42)
    desc_off = kit.vec(SANITARIUM, row, "description_offset", 170, 90)
    desc_w = kit.num(SANITARIUM, row, "description_width", 250)
    slots = kit.vec(SANITARIUM, row, "slot_list_pos", 440, 119)
    slot_step = kit.vec(SANITARIUM, row, "slot_spacing", 135, 0)
    cell = "building_activity_slot_layout"
    overlay = kit.vec(BUILDING, cell, "overlay_offset", -26, -70)
    bars = kit.vec(BUILDING, cell, "locked_overlay_offset", -42, -70)
    cancel = kit.vec(BUILDING, cell, "cancel_button_offset", 7, 112)
    hero_name = kit.vec(SLOT_DIR + "hero_slot.layout.darkest", "town_hero_slot_layout", "name_offset", 45, -70)
    wards = [("treatment", [(30, "Remove a bad quirk"), (150, "Lock in a good quirk")], None),
             ("disease_treatment", [(15, "Cure a disease")], "33% to cure every other one too")]
    pick_ward = 1 if disease else 0
    for i, (wid, prices, chance) in enumerate(wards):
        at = add(origin, (spacing[0] * i, spacing[1] * i))
        line(kit, c, win.at(add(at, name_off)), kit.string("town_activity_name_" + wid, wid), "town_activity_name", width=236)
        text_at = add(at, desc_off)
        block(kit, c, win.at(text_at), kit.string("town_activity_description_" + wid, ""), "town_activity_description", desc_w, 52)
        y = text_at[1] + 56                                                     # MOD: the price lines
        for amount, what in prices:
            paste(c, kit.art("shared/estate/currency.gold.icon.png"), win.at((text_at[0], y)))
            line(kit, c, win.at((text_at[0] + 28, y)), str(amount), "town_currency_amount")
            line(kit, c, win.at((text_at[0] + 76, y)), what, "town_activity_description", width=190)
            y += 26
        if chance:
            line(kit, c, win.at((text_at[0], y)), chance, "town_activity_description", width=desc_w + 10)
        for j in range(3):
            place = win.at(add(add(at, slots), (slot_step[0] * j, slot_step[1] * j)))
            if j >= 1:
                paste(c, kit.art(stem + ".locked_hero_slot_overlay.png"), add(place, bars))
                continue
            if pick and i == pick_ward:
                slot(kit, c, place, HEROES[4][1], lit=True)
                line(kit, c, add(place, hero_name), HEROES[4][0], "hero_slot_name", anchor="m", width=slot_step[0] - 4)
            elif not pick and i == 0:
                slot(kit, c, place, HEROES[1][1])
                line(kit, c, add(place, hero_name), HEROES[1][0], "hero_slot_name", anchor="m", width=slot_step[0] - 4)
                paste(c, kit.art("%s.%s.hero_slot_overlay.png" % (stem, wid)), add(place, overlay))
                line(kit, c, add(place, (SLOT / 2, SLOT + 1)), "Remove: Known Cheat", "tooltip", anchor="m", width=slot_step[0] - 6)
                paste(c, kit.art(BUILDINGS_DIR + "hero_activity/hero_activity.cancel_button.png"), add(place, cancel))
            else:
                slot(kit, c, place)
    if pick:
        choice(kit, win, origin, add(origin, slots), disease)
        win.hint("")
    else:
        win.hint("Drag a hero from the roster into a cell, or click a cell and then the hero; then choose what to treat. The check mark pays; the treatment is done when the week ends.")
    return win.save(out, name or ("window_sanitarium%s.png" % ("_disease" if disease else "_pick" if pick else "")))


def choice(kit, win, list_origin, first_cell, disease):
    """SanitariumPanel.BuildChoice: header, backdrop, the lists, the price over the confirm button."""
    c, t, d = win.canvas, "quirk_treatment", BUILDINGS_DIR + "sanitarium/"
    backdrop_top = add(list_origin, kit.vec(SANITARIUM, t, "backdrop_centre_position", 500, 500))     # FALLBACK
    header_top = add(list_origin, kit.vec(SANITARIUM, t, "header_backdrop_centre_position", 480, 470))
    title_top = add(list_origin, kit.vec(SANITARIUM, t, "title_centre_position", 500, 475))
    cost_top = add(first_cell, kit.vec(SANITARIUM, "sanitarium_activity_layout", "shared_cost_pos", 62, 570))
    confirm_top = add(first_cell, kit.vec(SANITARIUM, "sanitarium_activity_layout", "shared_confirm_pos", 62, 588))
    cell = "sanitarium_activity_slot_layout"
    pitch = kit.vec(SANITARIUM, cell, "choice_spacing", 0, 28)[1]
    tip_w = kit.num(SANITARIUM, cell, "choice_tooltip_text_width", 200)
    tip_left = kit.vec(SANITARIUM, cell, "choice_tooltip_left_justified_offset", 350, 0)
    lock_text = add(backdrop_top, kit.vec(SANITARIUM, cell, "choice_remove_quirk_right_text_pos", -115, 156))
    remove_text = add(backdrop_top, kit.vec(SANITARIUM, cell, "choice_remove_quirk_left_text_pos", 130, 156))
    back = kit.art(d + ("disease_treatment_backdrop.png" if disease else "quirk_treatment_backdrop.png"))
    head = kit.art(d + ("diseaseheader.png" if disease else "quirkheader.png"))
    paste(c, back, win.at((backdrop_top[0] - 210, backdrop_top[1])))
    paste(c, head, win.at((header_top[0] - 225, header_top[1])))
    line(kit, c, win.at((title_top[0], header_top[1] + (51 - 40) / 2)), kit.string("str_diseases" if disease else "str_quirks", "Quirks"), "town_activity_name", anchor="m",
         colour="sanitarium_treatment_header")
    if disease:
        pos = add(first_cell, kit.vec(SANITARIUM, t, "disease_list_position", -90, 410))
        hi = kit.vec(SANITARIUM, t, "disease_selection_backdrop_offset", -50, 0)
        for k, words in enumerate(["The Red Plague", "Rabies"]):
            at = (pos[0], pos[1] + pitch * k)
            if k == 0:
                paste(c, kit.art(d + "disease_highlight.png"), win.at(add(at, hi)))
            line(kit, c, win.at((at[0], at[1] + 1)), words, "town_choice_activity", colour="sanitarium_treatment_disease_entry", width=190)
        chosen, verb = "The Red Plague", "Remove"
    else:
        pos = add(first_cell, kit.vec(SANITARIUM, t, "positive_list_position", -230, 400))
        hi = kit.vec(SANITARIUM, t, "positive_selection_backdrop_offset", -50, 0)
        icon = kit.vec(SANITARIUM, t, "positive_icon_offset", -37, -2)
        for k, words in enumerate(["Balanced", "Second Wind", "Eldritch Slayer", "Tough", "Unyielding"]):
            at = (pos[0], pos[1] + pitch * k)
            if k == 3:
                paste(c, kit.art(d + "posquirk_highlight.png"), win.at(add(at, hi)))
                paste(c, kit.art(d + "lockedquirk.png"), win.at(add(at, icon)))
            line(kit, c, win.at((at[0], at[1] + 1)), words, "town_choice_activity", colour="sanitarium_treatment_positive_entry", width=190)
        pos = add(first_cell, kit.vec(SANITARIUM, t, "negative_list_position", 330, 400))
        for k, words in enumerate(["Fear of Mankind", "Known Cheat", "Night Blindness", "Sitiomania"]):
            line(kit, c, win.at((pos[0], pos[1] + pitch * k + 1)), words, "town_choice_activity", anchor="r", colour="sanitarium_treatment_negative_entry", width=190)
        chosen, verb = "Tough", "Lock"
        tooltip(kit, c, win.at(add((add(first_cell, kit.vec(SANITARIUM, t, "positive_list_position", -230, 400))[0], pos[1] + pitch * 3), tip_left)), "Tough", "+10% MAX HP", tip_w)
    fmt = kit.string("str_sanitarium_lock_quirk_format" if verb == "Lock" else "str_sanitarium_remove_quirk_format", verb + " %s").replace("%s", chosen)
    if verb == "Lock":
        line(kit, c, win.at(lock_text), fmt, "town_choice_activity", anchor="r", colour="town_choice_activity_choice", width=210)
    else:
        line(kit, c, win.at(remove_text), fmt, "town_choice_activity", colour="town_choice_activity_choice", width=210)
    paste(c, kit.art("shared/estate/currency.gold.icon.png"), win.at((cost_top[0] - 30, cost_top[1] - 13)))     # shared_cost_pos: the middle of the price
    line(kit, c, win.at((cost_top[0] - 2, cost_top[1] - 13)), "15" if disease else "150", "town_currency_amount")
    paste(c, kit.art(BUILDINGS_DIR + "hero_activity/hero_activity.confirm_button.png"), win.at((confirm_top[0] - 32, confirm_top[1])))


SKILLS = [("Smite", 2), ("Stunning Blow", 1), ("Zealous Accusation", 1), ("Inspiring Cry", 1), ("Rallying Cry", 1), ("Battle Heal", 1),
          ("Reap", 0), ("Bulwark of Faith", 0), ("Holy Lance", 0), ("Radiance", 0), ("Tenacity", 0)]     # name, 0 unknown / 1 known / 2 mastered


def guild(kit, out, name=None, hero=0):
    f = BUILDINGS_DIR + "guild/guild.layout.darkest"
    win = Window(kit, "guild")
    c = win.canvas
    tree = "guild_upgrade_tree_layout"
    spacing = kit.vec(f, "guild_layout", "skill_spacing", 0, 91)                                       # FALLBACK
    step = kit.vec(f, tree, "requirement_spacing", 75, 0)
    cost = kit.vec(f, tree, "icon_cost_offset", 34, 70)
    divider = kit.vec(f, tree, "divider_offset", 0, 82)
    step_icon = kit.vec(NODE_DIR + "upgrade.layout.darkest", "upgrade_requirement_layout", "icon_offset", 40, 10)
    cell_w = step[0] * 2 + step_icon[0] + 50
    pitch = cell_w + 12                                                          # MOD: two columns ending at the frame's edge
    first_x = (FRAME[0] - 10) - cell_w - pitch
    text_left = win.body[0] + kit.vec(HeroAction.FILE, "hero_action_layout", "base_pos", 220, 44)[0] + kit.vec(HeroAction.FILE, "hero_action_layout", "verbose_pos", 0, 105)[0] \
        + kit.vec(HeroAction.FILE, "hero_action_verbose_layout", "body_text_offset", 5, 74)[0]
    h = HEROES[hero] if hero is not None else None
    frame = HeroAction(win, h, "Resolve level 1   Mastered 1 of 1", "Next mastery: needs Instructor Mastery II.", verbose_width=first_x - 8 - 12 - text_left)
    prices = []
    if h is None:
        win.hint("Click an open step to pay for it. Right click a hero or a skill for the character sheet: skills are equipped there.")
        return win.save(out, name or "window_guild_empty.png")
    first_y = frame.body[1] + kit.vec(f, "guild_layout", "skill_pos", 44, -4)[1]
    for i, (skill, state) in enumerate(SKILLS):
        at = (first_x + pitch * (i // 6), first_y + spacing[1] * (i % 6))
        s = win.at(at)
        paste(c, kit.art(BUILDINGS_DIR + "guild/skill_frame.png"), add(s, (-8, -8)))
        icon = kit.art("heroes/%s/%s.ability.%s.png" % (h[1], h[1], ["one", "two", "three", "four", "five", "six", "seven"][i % 7]))
        paste(c, icon, s, (72, 72), tint=None if state else (0.4, 0.4, 0.4))
        if not state:
            paste(c, kit.art("shared/character/lockedskill.png"), s)
        paste(c, kit.art(NODE_DIR + "tree_divider_medium.png"), add(s, divider), (cell_w, 6))
        node(kit, c, add(s, (step[0] + step_icon[0], step_icon[1])), "bought" if state >= 1 else "open", linked=False)
        node(kit, c, add(s, (step[0] * 2 + step_icon[0], step_icon[1])), "bought" if state >= 2 else "locked")
        line(kit, c, add(s, (step[0] + step_icon[0] - 10, step_icon[1] + 51)), skill, "tooltip", colour=None if state else "town_building_info", width=cell_w - step[0] - step_icon[0] + 10)
        if not state:
            prices.append(add(s, cost))
    for at in prices:                                                            # over every cell: a price lies on the edge of the frame below
        costs(kit, c, at, [("gold", 1500)])
    s = win.at((first_x + pitch, first_y))
    tooltip(kit, c, add(s, (step[0], 40 + 50)), "Reap", "*Click to learn for 1,500 gold.")
    win.hint("Click an open step to pay for it. Right click a hero or a skill for the character sheet: skills are equipped there.")
    return win.save(out, name or "window_guild.png")


def blacksmith(kit, out, name=None):
    f = BUILDINGS_DIR + "blacksmith/blacksmith.layout.darkest"
    win = Window(kit, "blacksmith")
    c = win.canvas
    frame = HeroAction(win, HEROES[0], "Resolve level 1   Weapon II   Armour I")
    piece = add(frame.body, kit.vec(f, "blacksmith_layout", "equipment_pos", 50, 20))                  # FALLBACK
    spacing = kit.vec(f, "blacksmith_layout", "equipment_spacing", 0, 176)
    step = kit.vec(f, "blacksmith_upgrade_tree_layout", "requirement_spacing", 75, 0)
    up = NODE_DIR + "upgrade.layout.darkest"
    step_icon = kit.vec(up, "upgrade_requirement_layout", "icon_offset", 40, 10)
    step_cost = kit.vec(up, "upgrade_requirement_layout", "cost_offset", 62, 72)
    paste(c, kit.art(BUILDINGS_DIR + "blacksmith/blacksmith.frame.png"), win.at(add(frame.body, kit.vec(f, "blacksmith_layout", "frame_pos", 30, 0))))
    rows = [("Weapon", "weapon", 2, "+10% damage, +1% crit", "+20% damage, +2% crit", None, 25),
            ("Armour", "armour", 1, "As issued", "+10% health, +3 dodge", "Needs resolve level 2", 20)]
    for r, (label, art_name, level, now, then, wants, price) in enumerate(rows):
        at = win.at(add(piece, (spacing[0] * r, spacing[1] * r)))
        paste(c, kit.art(BUILDINGS_DIR + "blacksmith/blacksmith.%s.icon.png" % art_name), at)
        for k in range(2, 6):
            corner = add(at, (step[0] * (k - 1), 0))
            state = "bought" if k <= level else "open" if k == level + 1 and not wants else "locked"
            node(kit, c, add(corner, step_icon), state)
            if k == level + 1:
                costs(kit, c, add(corner, step_cost), [("gold", price)], locked=bool(wants))
        roman = ["0", "I", "II", "III", "IV", "V"]
        w = line(kit, c, add(at, (0, 100)), label + " " + roman[level], "town_action_verbose_body_text", colour="notable")
        line(kit, c, add(at, (w + 14, 100)), now, "town_action_verbose_body_text", width=420 - w - 14)
        words = "Level %s:   %s" % (roman[level + 1], then)
        w = line(kit, c, add(at, (0, 126)), words, "town_action_verbose_body_text", colour="town_building_info")
        if wants:
            line(kit, c, add(at, (w + 14, 126)), wants, "town_action_verbose_body_text", colour="harmful", width=420 - w - 14)
    win.hint("Click the open step to buy the next level. Gear stays with the hero.")
    return win.save(out, name or "window_blacksmith.png")


def survivalist(kit, out, name=None):
    f = BUILDINGS_DIR + "camping_trainer/camping_trainer.layout.darkest"
    win = Window(kit, "camping_trainer")
    c = win.canvas
    frame = HeroAction(win, HEROES[0], "Camping skills 3 of 7   Ready 3 of 4")
    own = add(frame.body, kit.vec(f, "camping_trainer_class_specific_skill_grid_layout", "skill_start_pos", 60, 30))     # FALLBACK
    own_pitch = kit.vec(f, "camping_trainer_class_specific_skill_grid_layout", "skill_spacing", 110, 90)
    shared = add(frame.body, kit.vec(f, "camping_trainer_shared_skill_grid_layout", "skill_start_pos", 60, 200))
    shared_pitch = kit.vec(f, "camping_trainer_shared_skill_grid_layout", "skill_spacing", 110, 90)
    cost = kit.vec(f, "camping_trainer_upgrade_tree_layout", "icon_cost_offset", 32, 90)
    tip = kit.vec(f, "camping_trainer_upgrade_tree_layout", "icon_tooltip_offset", 90, 0)
    icons = "raid/camping/skill_icons/camp_skill_%s.png"
    grids = [("Crusader", own, own_pitch, [("stand_tall", 2), ("zealous_speech", 0), ("zealous_vigil", 2), ("unshakeable_leader", 0)]),
             ("Shared", shared, shared_pitch, [("encourage", 0), ("first_aid", 2), ("pep_talk", 0)])]     # 0 unknown, 1 known, 2 ready
    for title, start, pitch, skills in grids:
        line(kit, c, win.at(add(start, (-8, -36))), title, "town_upgrade_tree_title")                    # MOD: the captions
        for i, (skill, state) in enumerate(skills):
            at = win.at((start[0] + pitch[0] * i, start[1]))
            paste(c, kit.art(BUILDINGS_DIR + "guild/skill_frame.png"), add(at, (-8, -8)))
            paste(c, kit.art(icons % skill), at, (72, 72), tint=None if state else (0.4, 0.4, 0.4))
            if not state:
                paste(c, kit.art("shared/character/lockedskill.png"), at)
                costs(kit, c, add(at, cost), [("gold", 35)])
            if state == 2:
                paste(c, kit.art("shared/character/selected_ability.png"), add(at, (-9, -9)))
    at = win.at((own[0] + own_pitch[0], own[1]))
    tooltip(kit, c, add(at, tip), "Zealous Speech", "5 respite\nAll companions: -15 stress, less stress taken for the rest of the quest\n*Click to learn for 35 gold.",
            kit.num(f, "camping_trainer_upgrade_requirement_tooltip_layout", "tooltip_text_width", 280))
    win.hint("A camp gives the party 12 respite points to spend on the skills its heroes have ready, 4 each at most. Click a skill to learn it; click a known one to make it ready or to put it aside.")
    return win.save(out, name or "window_survivalist.png")


def nomad_wagon(kit, out, name=None):
    f = BUILDINGS_DIR + "nomad_wagon/nomad_wagon.layout.darkest"
    win = Window(kit, "nomad_wagon")
    c = win.canvas
    table = add(win.body, kit.vec(f, "inventory_system_background_layout", "pos", 230, 150))            # FALLBACK
    start = add(table, kit.vec(f, "inventory_system_grid_layout", "start_pos", 55, -10))
    pitch = kit.vec(f, "inventory_system_grid_layout", "offset", 100, 180)
    columns = int(kit.num(f, "inventory_system_grid_layout", "number_of_columns", 6))
    paste(c, kit.art(BUILDINGS_DIR + "nomad_wagon/inventory_grid_background.png"), win.at(table))
    card = "panels/icons_equip/trinket/"
    wares = [("uncommon", "accuracy_stone", 150, False), ("rare", "adamant", 300, False), ("common", "agile_talon", 100, True),
             ("very_rare", "abyssal_tome", 1500, False), ("uncommon", "accuracy_stone", 150, False), ("common", "agile_talon", 100, False),
             ("rare", "adamant", 300, False)]
    for i, (rarity, item, price, sold) in enumerate(wares):
        at = win.at((start[0] + pitch[0] * (i % columns), start[1] + pitch[1] * (i // columns)))
        tint = (0.35,) * 3 if sold else None
        paste(c, kit.art(card + "rarity_%s.png" % rarity), at, tint=tint)
        paste(c, kit.art(card + "inv_trinket+%s.png" % item), at, tint=tint)
        if sold:
            line(kit, c, add(at, (36, 146)), "sold", "town_building_info", anchor="m")
        else:
            costs(kit, c, add(at, (36, 146)), [("gold", price)], purse=dict(gold=560))
    title = (690, 486)                                                           # MOD: DD1's Trinket Inventory, as a row
    box(c, win.at((table[0], title[1] - 4)) + (684, 530 + 144 + 30 - title[1] + 8), (0, 0, 0, int(0.55 * 255)))     # a shade over the painted still life
    line(kit, c, win.at(title), kit.string("town_name_realm_inventory", "Trinket Inventory"), "town_activity_name", width=250)
    line(kit, c, win.at(add(title, (260, 10))), "Unworn trinkets of the estate: click one to sell it.", "town_building_info", width=684 - 276)
    stores = (710, 530)
    for i, (rarity, item, price) in enumerate([("common", "agile_talon", 15), ("uncommon", "accuracy_stone", 23), ("rare", "adamant", 45)]):
        at = win.at((stores[0] + 80 * i, stores[1]))
        paste(c, kit.art(card + "rarity_%s.png" % rarity), at)
        paste(c, kit.art(card + "inv_trinket+%s.png" % item), at)
        costs(kit, c, add(at, (36, 146)), [("gold", price)])
    first = win.at(start)
    tooltip(kit, c, (first[0] + pitch[0], first[1] - 4 - 132), "Adamant Stone", "*Rare\n+15% PROT if the hero is in rank 1\nClick to buy for 300 gold.")
    win.hint("Click a trinket on the table to buy it; new wares come every week. Trinkets are worn from a hero's sheet.")
    return win.save(out, name or "window_nomad_wagon.png")


def graveyard(kit, out, name=None):
    f = BUILDINGS_DIR + "graveyard/graveyard.layout.darkest"
    win = Window(kit, "graveyard", has_trees=False)
    c = win.canvas
    pos = add(win.body, kit.vec(f, "graveyard_main_layout", "list_position", 148, 148))                 # FALLBACK
    size = kit.vec(f, "graveyard_main_layout", "list_area_size", 720, 600)
    entry = kit.vec(f, "graveyard_main_layout", "entry_size", 1000, 160)
    gap = kit.num(f, "graveyard_main_layout", "entry_spacing", 20)
    name_pos = kit.vec(f, "dead_hero_layout", "name_position", 96, 0)
    death_by = kit.vec(f, "dead_hero_layout", "death_by_position", 96, 128)
    margins = (10, 25, 10, 20)
    fallen = [("Baldwin", "leper", "Leper", 7, "the Weald", "Fungal Scratcher"), ("Junia", "vestal", "Vestal", 5, "the Ruins", "Bone Rabble"),
              ("Sarmenti", "jester", "Jester", 3, "the Warrens", None), ("Tardif", "bounty_hunter", "Bounty Hunter", 2, "the Ruins", "Cultist Brawler")]
    layer = Image.new("RGBA", c.size, (0, 0, 0, 0))
    for i, (who, cls, cls_name, week, where, by) in enumerate(fallen):
        e = win.at((pos[0], pos[1] + i * (entry[1] + gap)))
        paste(layer, kit.art(BUILDINGS_DIR + "graveyard/0_1.png"), e)
        slab = add(e, (118, 0))
        paste(layer, kit.art(BUILDINGS_DIR + "graveyard/dead_hero_backdrop.png"), slab)
        face = portrait(kit, cls)
        if face is not None:
            paste(layer, face.convert("LA").convert("RGBA"), add(slab, (margins[0], (118 - 85) / 2)), tint=(0.8,) * 3)
        left = name_pos[0] + margins[0]
        kit.text.draw(layer, add(slab, (600 - margins[2] - 6, name_pos[1] + margins[1])), kit.string("str_graveyard_week_string", "Week %d").replace("%d", str(week)),
                      "town_graveyard_hero_death_week", anchor="r")
        kit.text.draw(layer, add(slab, (left, name_pos[1] + margins[1])), who, "town_graveyard_hero_name")
        kit.text.draw(layer, add(slab, (left, name_pos[1] + margins[1] + 40)), "%s, %s" % (cls_name, where), "town_graveyard_story")
        how = (kit.string("str_death_attack_monster", "was slain by a vile") + " " + by + ".") if by else kit.string("str_death_unknown_unknown", "succumbed to an unknown peril.")
        w = kit.text.draw(layer, add(e, death_by), who, "town_graveyard_story", colour="notable")
        kit.text.draw(layer, add(e, (death_by[0] + w + 6, death_by[1])), how, "town_graveyard_story")
    area = win.at(pos) + (int(win.at(pos)[0] + size[0]), int(win.at(pos)[1] + size[1]))
    c.alpha_composite(layer.crop(tuple(int(v) for v in area)), (int(area[0]), int(area[1])))          # the list is a window on its rows
    rail = kit.art("shared/widgets/scrollbarmid.png")
    paste(c, rail, (area[2] + kit.num(f, "graveyard_main_layout", "scrollbar_offset", 20), area[1]), (21, size[1]))
    paste(c, kit.art("shared/widgets/scrollpip.png"), (area[2] + kit.num(f, "graveyard_main_layout", "scrollbar_offset", 20) - 1, area[1] + 60))
    win.hint("%d heroes lie here." % len(fallen))
    return win.save(out, name or "window_graveyard.png")


def memoirs(kit, out, page=False, name=None):
    f = BUILDINGS_DIR + "statue/statue.layout.darkest"
    d = BUILDINGS_DIR + "statue/"
    win = Window(kit, "statue", has_trees=False)
    c = win.canvas
    pos = add(win.body, kit.vec(f, "statue_main_layout", "list_position", 270, 170))                    # FALLBACK
    size = kit.vec(f, "statue_main_layout", "list_area_size", 600, 580)
    gap = kit.num(f, "statue_main_layout", "entry_spacing", 10)
    quote = add(win.body, kit.vec(f, "statue_main_layout", "quote_position", 590, 80))
    block(kit, c, win.at(quote), kit.string("str_statue_ancestor_quote", ""), "town_statue_quote", kit.num(f, "statue_main_layout", "quote_width", 505), 84, anchor="m")
    bar_off = kit.vec(f, "statue_main_layout", "scrollbar_offset", 20, -50)[0]
    layer = Image.new("RGBA", c.size, (0, 0, 0, 0))
    top = win.at(pos)
    if page:
        paste(layer, kit.art(d + "boss_quest_title_bar.png"), (top[0] + size[0] / 2 - 619 / 2, top[1] - 16))
        kit.text.draw(layer, (top[0] + size[0] / 2, top[1] - 16 + (16 + 118 - 40) / 2), "The Burned Gallery", "town_statue_media_heading", anchor="m", max_width=619 - 230)
        paste(layer, kit.art("shared/progression/progression_back.png"), add(top, (4, (102 - 33) / 2)))
        kit.text.draw(layer, (top[0] + size[0] / 2, top[1] + 104), "The Librarian (1 / 3), Ruins", "town_statue_archive_entry", anchor="m")
        words = ("I remember days when the sun shone, and laughter could be heard from the tavern. I was lord of this place, before the crows and rats made it "
                 "their domain. In truth, I cannot tell how much time has passed since I sent that letter. Excavations beneath the manor were well underway, "
                 "when a particular ragged indigent arrived in the hamlet.")
        y = top[1] + 136
        for words_line in kit.text.wrap(words, "town_statue_quote", size[0] - 36, 26):
            kit.text.draw(layer, (top[0] + size[0] / 2, y), words_line, "town_statue_quote", colour="neutral", anchor="m", size=26)
            y += kit.text.line_height("town_statue_quote", 26)
    else:
        shelves = [("video_title_bar.png", kit.string("str_media_video", "Prologue"), [("house_of_ruin.png", kit.string("str_media_house_of_ruin", "House of Ruin"), "", True),
                                                                                       ("old_road.png", kit.string("str_media_old_road", "Old Road"), "", True)]),
                   ("boss_quest_title_bar.png", kit.string("str_media_boss_quests", "The Ancestor's Path"),
                    [("portrait_necromancer.png", "The Burned Gallery", "The Librarian (1 / 3), Ruins", True),
                     ("portrait_hag.png", "The Keeper of the Catalogue", "Finish the quest before it to hear this", False)])]
        y = top[1]
        for bar, label, entries in shelves:
            paste(layer, kit.art(d + bar), (top[0] + size[0] / 2 - 619 / 2, y - 16))
            kit.text.draw(layer, (top[0] + size[0] / 2, y - 16 + (16 + 118 - 40) / 2), label, "town_statue_media_heading", anchor="m", max_width=619 - 230)
            y += 102 + gap
            for picture, title, subtitle, unlocked in entries:
                paste(layer, kit.art(d + "media_entry_plot_quest_backdrop.png"), (top[0], y))
                paste(layer, kit.art(d + picture), (top[0] + 16, y + (118 - 85) / 2), tint=None if unlocked else (0.45,) * 3)
                colour = kit.text.colour("town_statue_archive_entry")
                if not unlocked:
                    colour = tuple(int(v * 0.5) for v in colour)
                kit.text.draw(layer, (top[0] + 16 + 85 + 14, y + 30), title, "town_statue_archive_entry", colour=colour)
                kit.text.draw(layer, (top[0] + 16 + 85 + 14, y + 58), subtitle, "town_statue_archive_entry", colour=tuple(int(v * 0.65) for v in colour))
                paste(layer, kit.art(d + ("playmedia.png" if unlocked else "lockedmedia.png")), (top[0] + size[0] - 20 - 32, y + (118 - 32) / 2))
                y += 118 + gap
    area = (int(top[0] - 10), int(top[1] - 16), int(top[0] + size[0] + 10), int(top[1] + size[1]))
    c.alpha_composite(layer.crop(area), (area[0], area[1]))
    paste(c, kit.art("shared/widgets/scrollbarmid.png"), (top[0] + size[0] + bar_off, top[1] + (136 if page else 0)), (21, size[1] - (136 if page else 0)))
    paste(c, kit.art("shared/widgets/scrollpip.png"), (top[0] + size[0] + bar_off - 1, top[1] + (150 if page else 20)))
    win.hint("10 of 30 chapters of the estate's story told")
    return win.save(out, name or ("window_memoirs_page.png" if page else "window_memoirs.png"))


# ---------------------------------------------------------------- the upgrade pane (UpgradePane.cs)

def upgrades(kit, out, building, trees, names, levels, purse, hover=None, name=None, own_icons=None, places=None, describe=None, waits=None):
    """`trees`: the building's trees (preview_upgrades.Tree), `places`: preview_upgrades.places, `describe(tree, level)`:
    the lines of a step's tooltip, `waits(tree, step)`: what the step still waits for ("<tree> Level <n>")."""
    win = Window(kit, building, pane=True)
    c = win.canvas
    pane, row, tip = "building_base_upgrade_layout", "building_base_upgrade_tree_layout", "building_upgrade_requirement_tooltip_layout"
    locked_tip = "upgrade_locked_upgrade_requirement_tooltip_layout"
    base = kit.vec(BUILDING, "building_base_layout", "upgrade_base_pos", 172, 259)                      # FALLBACK
    origin = (base[0] - win.area[0], base[1] - win.area[1])
    frame = add(origin, kit.vec(BUILDING, pane, "frame_offset", -18, -115))
    verbose = add(origin, kit.vec(BUILDING, pane, "verbose_offset", 20, 30))
    title_pos = add(origin, kit.vec(BUILDING, pane, "upgrade_title_offset", 458, 36))
    percent_pos = add(origin, kit.vec(BUILDING, pane, "upgrade_percent_offset", 480, 62))
    first = add(origin, kit.vec(BUILDING, pane, "upgrade_trees_offset", 0, 195))
    spacing = kit.vec(BUILDING, pane, "upgrade_trees_spacing", 0, 160)
    tree_title = kit.vec(BUILDING, row, "title_offset", 20, -35)
    tree_icon = kit.vec(BUILDING, row, "icon_offset", 30, 0)
    step_start = kit.vec(BUILDING, row, "requirement_start_offset", 0, 0)
    step = kit.vec(BUILDING, row, "requirement_spacing", 70, 0)
    divider = kit.vec(BUILDING, row, "divider_offset", 0, 118)
    up = NODE_DIR + "upgrade.layout.darkest"
    step_icon = kit.vec(up, "upgrade_requirement_layout", "icon_offset", 40, 10)
    step_cost = kit.vec(up, "upgrade_requirement_layout", "cost_offset", 62, 72)
    step_plate = kit.vec(up, "upgrade_requirement_layout", "outline_offset", 13, -12)
    # a step that can be built, or is: a box of one width; a locked one: DD1's narrower box, as wide as its words
    tip_off = kit.vec(BUILDING, tip, "tooltip_offset", 130, 0)
    tip_w = kit.num(BUILDING, tip, "tooltip_text_width", 300)
    tip_fixed = kit.num(BUILDING, tip, "tooltip_is_auto_width", 0) < 0.5
    locked_off = kit.vec(up, locked_tip, "tooltip_offset", 110, 0)
    locked_w = kit.num(up, locked_tip, "tooltip_text_width", 250)
    locked_fixed = kit.num(up, locked_tip, "tooltip_is_auto_width", 1) < 0.5
    second_tree, second_gap = 320, 8                                           # MOD: UpgradePane.SecondTree, SecondTreeGap

    # over the keeper and the screen's name: the building's icon shows in the pane's notch, the name above its body
    paste(c, kit.art(BUILDINGS_DIR + "blgupgradebg.png"), win.at(frame))
    win.toggle(True)
    block(kit, c, win.at(verbose), kit.string("building_verbose_" + building, ""), "town_building_upgrade_verbose", kit.num(BUILDING, pane, "verbose_width", 380), 150)
    line(kit, c, win.at(title_pos), kit.string("building_upgrade_title", "Upgraded:"), "town_building_upgrade_title")
    built = sum(min(levels.get(t.id, 0), len(t.steps)) for t in trees)
    total = max(1, sum(len(t.steps) for t in trees))
    line(kit, c, win.at(percent_pos), "%d%%" % round(100.0 * built / total), "town_building_upgrade_percent")

    where = places(trees) if places else {i: (i, 0, False) for i in range(len(trees))}
    tips, prices = [], []
    for i, tree in enumerate(trees):
        r, col, shared = where[i]
        row_start = add(first, (spacing[0] * r, spacing[1] * r))
        at = add(row_start, (second_tree * col, 0))
        width = second_tree - 10 if shared else 620
        line(kit, c, win.at(add(at, tree_title)), names(tree.id), "town_upgrade_tree_title", width=width - tree_title[0])
        icon = kit.art((own_icons or {}).get(tree.id) or "%s%s/%s.icon.png" % (BUILDINGS_DIR, tree.building, tree.id))
        if col == 0:
            paste(c, kit.art(NODE_DIR + "tree_divider_large.png"), win.at(add(row_start, divider)))
        # DD1: a tree's steps begin at the right edge of its icon; the mod's second tree of a row keeps them close
        steps_at = add(add(at, (tree_icon[0] + (icon.width if icon is not None else 72), 0)), step_start)
        if col > 0:
            steps_at = add(steps_at, (second_gap - step_icon[0], 0))
        level = min(levels.get(tree.id, 0), len(tree.steps))
        missing = waits(tree, level) if waits and level < len(tree.steps) else []
        if level < len(tree.steps):                                             # DD1's plate round the step that comes next and its price
            paste(c, kit.art(BUILDINGS_DIR + "blg_townupgrade_costframe.png"), win.at(add(add(steps_at, (step[0] * level, step[1] * level)), step_plate)))
        paste(c, icon, win.at(add(at, tree_icon)))
        for k, st in enumerate(tree.steps):
            corner = add(steps_at, (step[0] * k, step[1] * k))
            state = "bought" if k < level else "open" if k == level and not missing else "locked"
            node(kit, c, win.at(add(corner, step_icon)), state)
            if k == level and st["costs"]:                                      # the next step's price only: DD1 shows none under the steps after it
                prices.append((win.at(add(corner, step_cost)), st["costs"], purse if state == "open" else None, state == "locked"))
            if hover == (tree.id, k):
                paste(c, kit.art(NODE_DIR + "requirement_highlight_overlay.png"), win.at(add(corner, step_icon)))
                # DD1's tooltip: under the tree's name what the step brings, then what it waits for, in DD1's warning colour
                body = list(describe(tree, k + 1)) if describe else []
                wait = waits(tree, k) if waits else []
                if wait:
                    body += [kit.string("upgrade_prerequisite_tooltip_title", "Prerequisites:")] + ["!" + w for w in wait]
                shut = state == "locked"
                tips.append((win.at(add(corner, locked_off if shut else tip_off)), names(tree.id), "\n".join(body), locked_w if shut else tip_w, locked_fixed if shut else tip_fixed))
    for at, pairs, purse_now, locked in prices:                                # over the steps' backings
        price(kit, c, at, pairs, purse=purse_now, locked=locked)
    for at, title, body, width, fixed in tips:
        tooltip(kit, c, at, title, body, width, fixed)
    return win.save(out, name or "upgrades_%s.png" % building)


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--dd1", default=os.environ.get("DD1_ROOT", r"E:\Steam\steamapps\common\DarkestDungeon"))
    ap.add_argument("--out", default=os.path.join(here, "..", "_lab", "preview"))
    ap.add_argument("--only", default="", help="comma separated: stage_coach, tavern, abbey, sanitarium, guild, blacksmith, survivalist, nomad_wagon, graveyard, memoirs, upgrades")
    args = ap.parse_args()
    if not os.path.isfile(os.path.join(args.dd1, "campaign", "town", "town_bg.png")):
        raise SystemExit("Darkest Dungeon (1) not found at %s (use --dd1 or DD1_ROOT)" % args.dd1)
    out = os.path.abspath(args.out)
    os.makedirs(out, exist_ok=True)
    kit = Kit(args.dd1)
    only = set(s for s in args.only.split(",") if s)

    def wanted(key):
        return not only or key in only

    written = []
    if wanted("stage_coach"):
        written.append(stage_coach(kit, out))
    for building in ("tavern", "abbey"):
        if wanted(building):
            written.append(activities(kit, out, building))
            written.append(activities(kit, out, building, plain=True))
    if wanted("sanitarium"):
        written += [sanitarium(kit, out), sanitarium(kit, out, pick=True), sanitarium(kit, out, pick=True, disease=True)]
    if wanted("guild"):
        written += [guild(kit, out), guild(kit, out, hero=None)]
    if wanted("blacksmith"):
        written.append(blacksmith(kit, out))
    if wanted("survivalist"):
        written.append(survivalist(kit, out))
    if wanted("nomad_wagon"):
        written.append(nomad_wagon(kit, out))
    if wanted("graveyard"):
        written.append(graveyard(kit, out))
    if wanted("memoirs"):
        written += [memoirs(kit, out), memoirs(kit, out, page=True)]
    if wanted("upgrades"):
        import preview_upgrades as pu
        written += pu.write_panes(kit, out)
    for path in written:
        print("wrote", path)


if __name__ == "__main__":
    main()
