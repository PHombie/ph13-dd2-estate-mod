#!/usr/bin/env python3
"""Compose DD1's two results screens (after an expedition) from the player's own Darkest Dungeon (1) install.

Reference for the layout of src/DD2Estate/Dungeon/RaidResultsScreen.cs: the same files are read with the same
fallbacks as RaidResultsLayout.cs, and every part is put where the plugin puts it. Change one, change the other.
docs/recon/dd1-raid-results.md says what each number places. Nothing from DD1 is copied into the repo; output
goes to _lab/ (gitignored).

    python tools/preview_raid_results.py [--dd1 <install>] [--out <dir>]

Writes, at 1920x1080 and with everything revealed:
  raid_results_escape_1.png, raid_results_escape_2.png    the owner's two DD1 screenshots again (an Escape from a
                                                          short explore quest: 3,000 gold, 4 crests and a trinket
                                                          withheld; six cards worth 1,555; no heirlooms; four heroes)
  raid_results_victory_1.png, raid_results_victory_2.png  a quest completed: rewards given, sixteen stacks squeezed
                                                          into the row, heirlooms; a level gained, masks closed and
                                                          opened, a dead hero
  raid_results_defeat_1.png                               nobody came home

What differs from the plugin on purpose: gold is written in DD1's own units and heroes wear DD1's own portraits
(the plugin writes the estate's gold and draws DD2's heroes), so that the Escape pages can be laid over the
screenshots pixel for pixel. The three Spine effects are drawn from their atlas pieces at the moments that
matter (a mask closed: the two halves side by side; opened: the halves at the plate's ends).
"""
import argparse
import os

from PIL import Image

from dd1_bmfont import Dd1Text
from preview_corridor import parse_darkest, parse_atlas, cut_region, read_json

W, H = 1920, 1080
DIR = "raid_results/"
CARD = (72, 144)
THRESHOLDS = [0, 2, 8, 14, 24, 36, 48]          # FALLBACK: campaign/roster/roster.variables.json resolve_level_thresholds


class Layout:
    """The numbers of RaidResultsLayout.cs, read the same way; the second argument of v() is DD1's stock value."""

    def __init__(self, dd1):
        def load(rel):
            path = os.path.join(dd1, rel)
            return parse_darkest(open(path, encoding="utf-8-sig").read()) if os.path.isfile(path) else {}

        self.files = {name: load(rel) for name, rel in (
            ("layout", DIR + "raid_results.layout.darkest"), ("anim", DIR + "raid_results.anim.darkest"),
            ("progression", "shared/progression/progression.layout.darkest"), ("resolve", "shared/resolve_level_bar/resolve_level_bar.layout.darkest"),
            ("hero", "shared/hero/hero.layout.darkest"), ("estate", "shared/estate/estate.layout.darkest"), ("item", "shared/inventory/inventory.layout.darkest"))}

    def v(self, file, block, key, x, y):
        values = self.files[file].get(block, {}).get(key, [])
        try:
            return (float(values[0]), float(values[1])) if len(values) >= 2 else (x, y)
        except ValueError:
            return (x, y)

    def grid(self, block, columns, start_x):
        values = self.files["layout"].get(block, {})
        return dict(columns=int(float(values.get("number_of_columns", [columns])[0])), start=self.v("layout", block, "start_pos", start_x, 0),
                    pitch=self.v("layout", block, "offset", 80, 160), centred=values.get("is_centred", ["1"])[0] != "0")


def add(a, b):
    return (a[0] + b[0], a[1] + b[1])


def grid_slot(index, count, grid, one_row):
    """RaidResultRules.GridSlot: DD1's shared inventory grid."""
    columns, (px, py) = grid["columns"], grid["pitch"]
    if one_row:
        pitch = px * columns / count if count > columns else px
        return (index * pitch + ((columns - count) * px / 2 if grid["centred"] and count < columns else 0), 0)
    row, column = divmod(index, columns)
    in_row = min(columns, count - row * columns)
    return (column * px + ((columns - in_row) * px / 2 if grid["centred"] else 0), row * py)


def stack_picture(kind, amount, thresholds):
    """ItemDef.IconPath: the first picture whose threshold the stack does not pass."""
    index = 0
    while index < len(thresholds) - 1 and amount > thresholds[index]:
        index += 1
    return "panels/icons_equip/%s/inv_%s+_%d.png" % (kind, kind, index)


class Screen:
    def __init__(self, dd1):
        self.dd1 = dd1
        self.l = Layout(dd1)
        self.text = Dd1Text(dd1)
        display = read_json(os.path.join(dd1, "shared/inventory/item.display.json"))
        self.steps = {kind: entry.get("icon_thresholds", []) for kind, entry in display.items()}
        self.order = [c["id"] for c in read_json(os.path.join(dd1, "campaign/estate/estate.json"))["currencies"] if c.get("is_heirloom")]
        self.canvas = None

    def art(self, rel):
        path = os.path.join(self.dd1, rel)
        return Image.open(path).convert("RGBA") if os.path.isfile(path) else None

    def put(self, rel, at, tint=None, image=None):
        image = image or self.art(rel)
        if image is None:
            return
        if tint is not None:        # DD1's "unselectable": the card multiplied by a grey
            r, g, b, a = image.split()
            image = Image.merge("RGBA", (r.point(lambda p: p * tint // 255), g.point(lambda p: p * tint // 255), b.point(lambda p: p * tint // 255), a))
        self.canvas.alpha_composite(image, (int(round(at[0])), int(round(at[1]))))

    def piece(self, fx, name):
        """One upright picture out of an effect's sheet."""
        stem = os.path.join(self.dd1, "fx", fx, fx + ".sprite")
        _, regions = parse_atlas(open(stem + ".atlas", encoding="utf-8").read())
        return cut_region(Image.open(stem + ".png").convert("RGBA"), regions[name])

    # ---- what both pages share ----

    def start(self, outcome, quest, dungeon):
        l = self.l
        self.canvas = Image.new("RGBA", (W, H), (0, 0, 0, 255))
        self.put("loading_screen/loading_screen.%s_0.png" % dungeon, l.v("layout", "raid_results_screen_layout", "level_background_pos", 0, 0))
        panel = {"victory": "quest_completed", "escape": "quest_not_completed_escape", "defeat": "quest_not_completed_defeat"}[outcome]
        style = {"victory": "raid_results_quest_result_was_completed", "escape": "raid_results_quest_result_was_not_completed_escape",
                 "defeat": "raid_results_quest_result_was_not_completed_defeat"}[outcome]
        hang = l.v("layout", "raid_results_screen_layout", "completion_background_pos", 960, 0)
        self.put(DIR + "raid_results.%s_background.png" % panel, (int(hang[0] - 899 / 2), hang[1]))
        # the titles stand on their positions: the middle of the foot of the line cell
        result = l.v("layout", "raid_results_screen_layout", "quest_result_pos", 960, 150)
        title = l.v("layout", "raid_results_screen_layout", "quest_title_pos", 960, 212)
        self.text.draw(self.canvas, (result[0], result[1] - self.text.line_height("raid_results_quest_result")), {"victory": "Victory!", "escape": "Escape", "defeat": "Defeat"}[outcome],
                       "raid_results_quest_result", colour=self.text.colour(style, "harmful"), anchor="m")
        self.text.draw(self.canvas, (title[0], title[1] - self.text.line_height("raid_results_quest_title")), quest, "raid_results_quest_title", anchor="m")
        self.state = l.v("layout", "raid_results_screen_layout", "state_pos", 510, 0)

    def finish(self, word, path):
        l = self.l
        self.put("shared/progression/progression_bar.png", l.v("layout", "raid_results_screen_layout", "progression_bar_pos", 0, 958))
        forward = l.v("progression", "progression_layout", "forward_pos", 801, 984)
        self.put("shared/progression/progression_forward.png", forward)
        self.text.draw(self.canvas, add(forward, l.v("progression", "progression_layout", "forward_text_offset", 160, -2)), word, "town_progression_forward", anchor="m")
        self.canvas.convert("RGB").save(path)
        print("wrote", path)

    # ---- page 1 ----

    def items(self, rewards, given, treasure, heirlooms):
        """rewards: (kind, id, amount); treasure and heirlooms: (type, id, amount, gold of the card)."""
        l, o = self.l, self.state
        block = "raid_results_items_state_layout"
        frame = l.v("layout", block, "frame_offset", 450, 230)
        self.put(DIR + "raid_results.items_frames.png", (int(o[0] + frame[0] - 533 / 2), o[1] + frame[1]))
        self.text.draw(self.canvas, add(o, l.v("layout", block, "quest_inventory_title", 450, 242)), "Quest Rewards", "raid_results_quest_inventory_title", anchor="m")
        self.text.draw(self.canvas, add(o, l.v("layout", block, "party_gold_inventory_title", 200, 490)), "Collected Treasure", "raid_results_party_gold_inventory_title")
        self.text.draw(self.canvas, add(o, l.v("layout", block, "party_heirloom_inventory_title", 200, 714)), "Collected Heirlooms", "raid_results_party_heirloom_inventory_title")
        icon = l.v("item", "inventory_item_layout", "icon_offset", 4, 0)
        amount_at = l.v("item", "inventory_item_layout", "amount_text_offset", 14, 4)

        grid = l.grid("raid_results_quest_inventory_system_grid_layout", 8, 40)
        start = add(add(o, l.v("layout", block, "quest_inventory_grid_offset", 84, 308)), grid["start"])
        for i, (kind, ident, amount) in enumerate(rewards):
            slot = add(start, grid_slot(i, len(rewards), grid, False))
            rel = stack_picture("gold", amount, self.steps.get("gold", [])) if kind == "gold" else \
                "panels/icons_equip/heirloom/inv_heirloom+%s.png" % ident if kind == "heirloom" else "panels/icons_equip/trinket/rarity_%s.png" % ident
            self.put(rel, add(slot, icon), tint=None if given else 0x66)
            if kind != "trinket":
                self.text.draw(self.canvas, add(slot, amount_at), "{:,}".format(amount), "inventory_amount", colour="inventory_amount" if given else "inventory_amount_unselectable")

        # the bag's two rows in DD1's one-row grid; the cards show no count
        for cards, grid_block, offset_key, offset in ((treasure, "raid_results_party_gold_inventory_system_grid_layout", "party_gold_inventory_grid_offset", (84, 538)),
                                                      (heirlooms, "raid_results_party_heirloom_inventory_system_grid_layout", "party_heirloom_inventory_grid_offset", (84, 760))):
            grid = l.grid(grid_block, 6, 100)
            start = add(add(o, l.v("layout", block, offset_key, *offset)), grid["start"])
            for i, (kind, ident, amount, gold) in enumerate(cards):
                rel = stack_picture(kind, amount, self.steps[kind]) if kind in self.steps else "panels/icons_equip/%s/inv_%s+%s.png" % (kind, kind, ident)
                self.put(rel, add(add(start, grid_slot(i, len(cards), grid, True)), icon))

        # the treasure's total: the count first, the pile after it, the two centred as one group
        total = add(o, l.v("layout", block, "party_gold_total_offset", 620, 496))
        number = l.v("estate", "estate_large_currency_layout", "number_offset", 90, -16)
        pile = l.v("estate", "estate_large_currency_layout", "icon_offset", 0, -50)
        words = "{:,}".format(sum(card[3] for card in treasure))
        width = self.text.width(words, "town_large_currency_amount")
        left = total[0] - (number[0] + width) / 2
        self.text.draw(self.canvas, (left, total[1] + number[1]), words, "town_large_currency_amount", colour="town_currency_amount")
        self.put("shared/estate/currency.gold.large_icon.png", (left + width, total[1] + pile[1]))

        # the heirlooms' totals: icon and count for each kind, side by side, centred the same way
        at = add(o, l.v("layout", block, "party_heirloom_total_offset", 620, 720))
        small_icon = l.v("estate", "estate_currency_heirloom_layout", "icon_offset", 0, -10)
        small_number = l.v("estate", "estate_currency_heirloom_layout", "number_offset", 38, -4)
        gap = l.v("estate", "estate_currency_heirloom_layout", "next_currency_spacing", 2, 0)[0]
        counts = [str(sum(card[2] for card in heirlooms if card[1] == kind)) for kind in self.order]
        widths = [small_number[0] + self.text.width(count, "town_currency_amount") for count in counts]
        x = at[0] - (sum(widths) + gap * (len(widths) - 1)) / 2
        for kind, count, width in zip(self.order, counts, widths):
            self.put("shared/estate/currency.%s.icon.png" % kind, (x + small_icon[0], at[1] + small_icon[1]))
            self.text.draw(self.canvas, (x + small_number[0], at[1] + small_number[1]), count, "town_currency_amount")
            x += width + gap

    # ---- page 2 ----

    def heroes(self, rows):
        """rows: dict(name, portrait (a DD1 picture), xp, gained, stress, dead, quirks [(kind, name)], mask "closed" / "open" / None)."""
        l, o = self.l, self.state
        frame = l.v("layout", "raid_results_heroes_state_layout", "frame_offset", 450, 250)
        self.put(DIR + "raid_results.heroes_frames.png", (int(o[0] + frame[0] - 680 / 2), o[1] + frame[1]))
        first = add(o, l.v("layout", "raid_results_heroes_state_layout", "heroes_start_pos", 130, 250))
        step = l.v("layout", "raid_results_heroes_state_layout", "heroes_spacing", 0, 168)
        row_block = "raid_results_hero_layout"
        for i, hero in enumerate(rows[:4]):
            origin = (first[0] + step[0] * i, first[1] + step[1] * i)
            self.text.draw(self.canvas, add(origin, l.v("layout", row_block, "name_offset", 0, 16)), hero["name"], "raid_results_hero_name")
            portrait = add(origin, l.v("layout", row_block, "portrait_icon_offset", 5, 64))
            self.put(hero["portrait"], portrait)
            if hero.get("dead"):
                self.put(DIR + "deadhero_portrait.png", add(origin, l.v("layout", row_block, "portrait_dead_overlay_offset", 5, 64)))
            xp = hero["xp"] + (0 if hero.get("dead") else hero.get("gained", 0))
            if hero.get("gained") and not hero.get("dead"):
                self.text.draw(self.canvas, add(origin, l.v("layout", row_block, "resolve_from_quest_offset", 520, 20)), "+%d Resolve XP" % hero["gained"],
                               "raid_results_hero_resolve_from_quest", anchor="r")

            # DD1's campaign status widget: the bar's holder and fill, the badge over them, the stress pips
            status = add(origin, l.v("layout", row_block, "campaign_status_offset", 556, 3))
            bar = add(status, l.v("hero", "hero_campaign_status_layout", "resolve_level_bar_offset", 6, 4))
            level = max(i for i, t in enumerate(THRESHOLDS) if xp >= t)
            share = 1.0 if level >= len(THRESHOLDS) - 1 else (xp - THRESHOLDS[level]) / (THRESHOLDS[level + 1] - THRESHOLDS[level])
            holder = add(bar, l.v("resolve", "resolve_level", "bar_pos", 12, 28))
            fill_at = add(holder, l.v("resolve", "resolve_level_bar", "gradient_offset", 10, 16))
            fw, fh = (int(n) for n in l.v("resolve", "resolve_level_bar", "gradient_size", 16, 40))
            top, bottom = self.text.colour("resolve_gradient_top"), self.text.colour("resolve_gradient_bottom")
            filled = int(round(fh * share))
            for y in range(fh - filled, fh):
                t = 1 - y / (fh - 1)
                colour = tuple(int(round(bottom[c] + (top[c] - bottom[c]) * t)) for c in range(3)) + (255,)
                self.canvas.paste(colour, (int(fill_at[0]), int(fill_at[1]) + y, int(fill_at[0]) + fw, int(fill_at[1]) + y + 1))
            self.put("shared/resolve_level_bar/resolve_level_bar_mask.png", holder)
            self.put("shared/resolve_level_bar/resolve_level_bar_number_background_lvl%d.png" % level, add(bar, l.v("resolve", "resolve_level_number", "background_offset", -3, 0)))
            number = add(bar, l.v("resolve", "resolve_level_number", "number_offset", 30, 31))
            gained_level = level > max(i for i, t in enumerate(THRESHOLDS) if hero["xp"] >= t)
            self.text.draw(self.canvas, (number[0], number[1] - self.text.line_height("resolve_number") / 2), str(level), "resolve_number",
                           colour="resolve_new" if gained_level else "resolve_number", anchor="m")
            stress = add(status, l.v("hero", "hero_campaign_status_layout", "stress_bar_offset", -14, 100))
            spacing = l.v("hero", "hero_campaign_status_layout", "stress_bar_spacing", 10, 0)
            for k in range(10):
                full = not hero.get("dead") and k < hero.get("stress", 0)
                self.put("overlays/stress_pip_full.png" if full else "overlays/stress_pip_empty.png", (stress[0] + spacing[0] * k, stress[1] + spacing[1] * k - (0 if full else 1)))

            # the new quirks from the plate's middle, and the mask over them (closed) or parted to the plate's ends (open)
            quirks = add(add(origin, l.v("layout", row_block, "center_of_quirks_upperleft_offset", 175, 50)), l.v("layout", row_block, "new_quirks_from_quest_start_offset", 145, 18))
            mask = add(quirks, l.v("anim", "raid_results_heroes_masks", "offset", -7, 39))
            if hero.get("mask") == "open":
                for j, (kind, name) in enumerate(hero.get("quirks", [])[:3]):
                    self.text.draw(self.canvas, (quirks[0], quirks[1] + 24 * j), name, "quirk_name", colour={"positive": "quirk_positive", "negative": "quirk_negative"}.get(kind, "disease"), anchor="m")
            if hero.get("mask"):
                happy, sad = self.piece("raid_results_quirk_reveal", "quirk_happy"), self.piece("raid_results_quirk_reveal", "quirk_sad")
                # the halves' bones stand 24 px either side of the origin; "reveal" ends with them 141 and 144 px further out
                out = (141.375, 143.577) if hero["mask"] == "open" else (0, 0)
                self.put(None, (mask[0] - 24 - out[0] - happy.width / 2, mask[1] - happy.height / 2), image=happy)
                self.put(None, (mask[0] + 24 + out[1] - sad.width / 2, mask[1] - sad.height / 2), image=sad)


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--dd1", default=os.environ.get("DD1_ROOT", r"E:\Steam\steamapps\common\DarkestDungeon"))
    ap.add_argument("--out", default=os.path.join(here, "..", "_lab", "preview"))
    args = ap.parse_args()
    os.makedirs(args.out, exist_ok=True)
    screen = Screen(args.dd1)

    def portrait(cls):
        return "heroes/%s/%s_A/%s_portrait_roster.png" % (cls, cls, cls)

    def out(name):
        return os.path.join(args.out, name)

    # the owner's Escape, in DD1's own units
    screen.start("escape", "Scout", "crypts")
    screen.items([("gold", "", 3000), ("heirloom", "crest", 4), ("trinket", "very_common", 1)], False,
                 [("supply", "antivenom", 1, 15), ("provision", "", 1, 5), ("provision", "", 1, 5), ("supply", "torch", 1, 5), ("gold", "", 1150, 1150), ("gem", "jade", 1, 375)], [])
    screen.finish("Next", out("raid_results_escape_1.png"))
    screen.start("escape", "Scout", "crypts")
    screen.heroes([dict(name="Reynauld", portrait=portrait("crusader"), xp=2, stress=5), dict(name="Dismas", portrait=portrait("highwayman"), xp=2, stress=6),
                   dict(name="Albelin", portrait=portrait("vestal"), xp=0, stress=4), dict(name="Woodville", portrait=portrait("plague_doctor"), xp=0, stress=5)])
    screen.finish("Return to Town", out("raid_results_escape_2.png"))

    # a quest completed: sixteen stacks (food a ration a card: nineteen cards on six columns), heirlooms, a level gained
    bag = [("gold", "", 1750, 1750), ("gem", "ruby", 1, 1250), ("supply", "torch", 3, 15)] + [("provision", "", 1, 5)] * 4 + \
          [("gold", "", 600, 600), ("gem", "jade", 2, 750), ("supply", "shovel", 1, 25), ("gem", "emerald", 1, 750), ("supply", "bandage", 2, 30), ("supply", "holy_water", 1, 15),
           ("gem", "citrine", 3, 750), ("gem", "onyx", 1, 500), ("gem", "sapphire", 1, 1000), ("supply", "antivenom", 1, 15), ("supply", "medicinal_herbs", 1, 20), ("supply", "skeleton_key", 2, 40)]
    screen.start("victory", "Cleanse", "weald")
    screen.items([("gold", "", 3000), ("heirloom", "crest", 4), ("heirloom", "deed", 2), ("trinket", "uncommon", 1)], True, bag,
                 [("heirloom", "crest", 5, 0), ("heirloom", "deed", 2, 0), ("heirloom", "bust", 1, 0), ("heirloom", "portrait", 1, 0)])
    screen.finish("Next", out("raid_results_victory_1.png"))
    screen.start("victory", "Cleanse", "weald")
    screen.heroes([dict(name="Reynauld", portrait=portrait("crusader"), xp=6, gained=4, stress=5, mask="open", quirks=[("positive", "Hard Skinned"), ("negative", "Nervous"), ("disease", "The Red Plague")]),
                   dict(name="Dismas", portrait=portrait("highwayman"), xp=2, gained=4, stress=6, mask="closed", quirks=[("negative", "Clumsy")]),
                   dict(name="Albelin", portrait=portrait("vestal"), xp=14, gained=4, stress=4),
                   dict(name="Woodville", portrait=portrait("plague_doctor"), xp=0, stress=0, dead=True)])
    screen.finish("Return to Town", out("raid_results_victory_2.png"))

    screen.start("defeat", "Scout", "warrens")
    screen.items([("gold", "", 3000), ("heirloom", "crest", 4), ("trinket", "very_common", 1)], False, [], [])
    screen.finish("Next", out("raid_results_defeat_1.png"))


if __name__ == "__main__":
    main()
