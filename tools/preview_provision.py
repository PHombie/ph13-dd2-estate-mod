#!/usr/bin/env python3
"""Compose the provision screen and the dungeon HUD with the party's bag from the player's own Darkest
Dungeon (1) install.

SUPERSEDED for layout: both screens were rebuilt on DD1's own layout files (the raid panel, the provision
screen's bar, tray and title) and are drawn by tools/preview_raid_hud.py (raid_hud*.png, raid_provision.png).
What this tool draws is the layout before that; its reading of the shop's stock and prices from the DD1
files is still how the plugin reads them.

Reference for the layout numbers in src/DD2Estate/Dungeon/ProvisionScreen.cs, InventoryGrid.cs and
DungeonHud.cs: change one, change the other. Nothing from DD1 is copied into the repo; output goes to _lab/
(gitignored).

    python tools/preview_provision.py [--dd1 <install>] [--out <dir>] [--length 1..3] [--purse N]

Writes provision.png and hud_bag.png (1920x1080). Hero portraits, names and the map are placeholders; items,
stack sizes, shop stock and prices come from the DD1 files the way the plugin reads them
(inventory/*.inventory.items.darkest, campaign/provision/provision.json).

Provision screen (all art at native size):
  * campaign/town/provision/provision.background.png fills the screen; provision.character_background.png
    and provision.character.png go where campaign/town/town.layout.darkest (town_background_layout) puts every
    building's art and keeper.
  * campaign/town/provision/provision.layout.darkest places the two grids: the shelves (7 columns, 80x170
    pitch) and the party's bag (8 columns, 80x160 pitch), each over its own background picture. Icons are
    DD1's 72x144 cards from panels/icons_equip.
  * The price sits in the 26 px between the shelf rows. The strip under the art (purse, bill, buttons) covers
    the keeper where he hangs out of the art, as DD1's estate bar does. The roster column on the right shows
    the quest and the party.

Dungeon HUD (bottom third of the screen): hero plates on the left as before, the middle column holds the
status lines above the bag (8x2 slots, DD1 cards at 3/4 size), the map stays on the right.
"""
import argparse
import json
import os

from PIL import Image, ImageDraw, ImageFont

SCREEN_W, SCREEN_H = 1920, 1080

# ---- provision screen: keep in step with ProvisionScreen.cs (screen pixels, y down) ----------------------
CARD = (72, 144)                     # DD1 item icon
ART_SIZE = (1395, 776)               # provision.character_background.png
TITLE_POS = (40, 24)                 # art-relative: "Provisions"
PRICE_HEIGHT = 26                    # under a shelf card: the gap DD1's 170 px row pitch leaves
STRIP_GAP = 8                        # strip under the art
STRIP_HEIGHT = 150
ROSTER_X = 1548                      # the hamlet's roster column: quest and party
ROSTER_W = 372
BUTTON_H = 58
SET_OUT_W, KIT_W, BACK_W = 240, 230, 150

LAYOUT_DEFAULTS = {
    ("town_background_layout", "area_pos"): (144, 132),
    ("town_background_layout", "character_pos"): (132, 240),
    ("provision_store_background_layout", "pos"): (814, 144),
    ("provision_store_grid_layout", "start_pos"): (120, 20),
    ("provision_store_grid_layout", "offset"): (80, 170),
    ("provision_store_grid_layout", "number_of_columns"): (7,),
    ("provision_party_background_layout", "pos"): (800, 532),
    ("provision_party_grid_layout", "start_pos"): (60, 28),
    ("provision_party_grid_layout", "offset"): (80, 160),
    ("provision_party_grid_layout", "number_of_columns"): (8,),
}

# ---- dungeon HUD: keep in step with DungeonHud.cs (panel pixels, y down from the panel's top) ------------
PANEL_H = 360
PLATE_X, PLATE_STEP, PLATE_Y = 40, 230, 28
MIDDLE_X, MIDDLE_W = 958, 432
STATUS_Y, STATUS_H = 12, 118
BAG_SCALE = 0.75                     # DD1 cards at 54x108
BAG_COLUMNS = 8
BAG_Y = 136
LEAVE_BOX = (40, 304, 300, 44)       # x, y, w, h: under the plates
MAP_BOX = (1420, 16, 480, 330)

PARCHMENT = (222, 209, 178, 255)
GOLD = (219, 181, 92, 255)
DIM = (158, 148, 128, 255)
WARN = (199, 51, 38, 255)
INK = (10, 9, 8, 255)

NAMES = {"provision": "Food"}


def read_json(path):
    with open(path, encoding="utf-8-sig") as f:
        return json.load(f)


def parse_darkest_blocks(text):
    """Blocks in file order: [(name, {key: [tokens]})]. Quoted values keep their spaces."""
    blocks, block, key = [], None, None
    for raw in text.splitlines():
        line = raw.split("//")[0]
        tokens, i = [], 0
        while i < len(line):
            c = line[i]
            if c.isspace():
                i += 1
            elif c == '"':
                end = line.find('"', i + 1)
                end = len(line) if end < 0 else end
                tokens.append(("q", line[i + 1:end]))
                i = end + 1
            else:
                j = i
                while j < len(line) and not line[j].isspace():
                    j += 1
                tokens.append(("t", line[i:j]))
                i = j
        for kind, tok in tokens:
            if kind == "t" and tok.endswith(":") and not tok.startswith("."):
                block = {}
                blocks.append((tok[:-1], block))
                key = None
            elif kind == "t" and len(tok) > 1 and tok[0] == "." and (tok[1].isalpha() or tok[1] == "_"):
                key = tok[1:]
                if block is not None:
                    block[key] = []
            elif block is not None and key is not None:
                block[key].append(tok)
    return blocks


class Layout:
    def __init__(self, dd1):
        self.blocks = {}
        for rel in ("campaign/town/town.layout.darkest", "campaign/town/provision/provision.layout.darkest"):
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


# ---- DD1 data, the way Core/InventoryItems.cs and Core/Provision.cs read it -------------------------------

def load_items(dd1):
    items = {}
    folder = os.path.join(dd1, "inventory")
    for name in sorted(os.listdir(folder)):
        if not name.endswith(".inventory.items.darkest"):
            continue
        with open(os.path.join(folder, name), encoding="utf-8-sig") as f:
            for block_name, block in parse_darkest_blocks(f.read()):
                if block_name != "inventory_item" or not block.get("type"):
                    continue
                item = {
                    "type": block["type"][0],
                    "id": (block.get("id") or [""])[0],
                    "stack": max(1, int(block.get("base_stack_limit", ["1"])[0])),
                    "buy": int(block.get("purchase_gold_value", ["0"])[0]),
                    "sell": int(block.get("sell_gold_value", ["0"])[0]),
                }
                items[(item["type"], item["id"])] = item
    for item in items.values():
        prefix = "inv_%s+%s_" % (item["type"], item["id"])
        icons = os.path.join(dd1, "panels", "icons_equip", item["type"])
        files = os.listdir(icons) if os.path.isdir(icons) else []
        item["variants"] = sum(1 for f in files if f.startswith(prefix) and f[len(prefix):-4].isdigit())
    return items


def icon_path(dd1, item, amount):
    name = "inv_%s+%s" % (item["type"], item["id"])
    if item["variants"] > 0:
        index = max(0, amount) * item["variants"] // (item["stack"] + 1)
        name += "_%d" % min(item["variants"] - 1, index)
    return os.path.join(dd1, "panels", "icons_equip", item["type"], name + ".png")


def load_stock(dd1, items, length):
    data = read_json(os.path.join(dd1, "campaign", "provision", "provision.json"))
    lists = data["default_store_inventory_item_lists"]
    stock = [(items[(e["type"], e["id"])], e["amount"]) for e in lists[min(length, len(lists) - 1)] if e["amount"] > 0]
    return stock, data["confirm_datas"][min(length, len(data["confirm_datas"]) - 1)]["minimum_food"]


def standard_kit(stock, minimum_food, length):
    kit = []
    for want in ("provision", "torch", "shovel", "skeleton_key"):
        for item, amount in stock:
            if (item["type"] if want == "provision" else item["id"]) != want:
                continue
            steps = max(1, length)
            n = max(minimum_food, item["stack"] * 2 // 3) if want == "provision" else item["stack"] * (steps + 1) // 2 if want == "torch" else steps
            kit.append((item, min(n, amount)))
    return kit


def bag_slots(stacks):
    """[(item, amount)] laid out the DD1 way: full stacks first, one slot per stack."""
    slots = []
    for item, amount in stacks:
        while amount > 0:
            take = min(amount, item["stack"])
            slots.append((item, take))
            amount -= take
    return slots


def item_name(item):
    if item["type"] in NAMES:
        return NAMES[item["type"]]
    return item["id"].replace("_", " ").capitalize()


def price_text(dd1_gold):
    return "%d" % dd1_gold


# ---- drawing ----------------------------------------------------------------------------------------------

def font(size, bold=True):
    for name in (("georgiab.ttf", "timesbd.ttf") if bold else ("georgia.ttf", "times.ttf")):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            pass
    return ImageFont.load_default()


def load(dd1, *rel):
    path = os.path.join(dd1, *rel)
    return Image.open(path).convert("RGBA") if os.path.isfile(path) else None


def paste(canvas, img, pos, size=None):
    if img is None:
        return
    if size is not None and size != img.size:
        img = img.resize(size, Image.LANCZOS)
    layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    layer.paste(img, (int(round(pos[0])), int(round(pos[1]))))
    canvas.alpha_composite(layer)


def fill(canvas, box, colour):
    layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    ImageDraw.Draw(layer).rectangle(box, fill=colour)
    canvas.alpha_composite(layer)


def outlined(draw, pos, text, fnt, colour, anchor="la"):
    x, y = pos
    for dx, dy in ((-1, -1), (1, -1), (-1, 1), (1, 1), (0, 2), (2, 0), (-2, 0), (0, -2)):
        draw.text((x + dx, y + dy), text, font=fnt, fill=(0, 0, 0, 255), anchor=anchor)
    draw.text((x, y), text, font=fnt, fill=colour, anchor=anchor)


def card(canvas, dd1, item, amount, pos, scale=1.0, dim=False, count=True):
    """One DD1 item card with its stack count in the top left corner."""
    size = (int(round(CARD[0] * scale)), int(round(CARD[1] * scale)))
    img = Image.open(icon_path(dd1, item, amount)).convert("RGBA")
    if dim:
        img = Image.blend(img, Image.new("RGBA", img.size, (0, 0, 0, 255)), 0.6)
    paste(canvas, img, pos, size)
    if count:
        draw = ImageDraw.Draw(canvas)
        # gold is carried in DD1 units and shown in the estate's (InventoryText.Amount)
        text = price_text(amount) if item["type"] == "gold" else str(amount)
        outlined(draw, (pos[0] + 5 * scale, pos[1] + 3 * scale), text, font(int(round(max(16, 22 * scale)))), DIM if dim else PARCHMENT)


def button(canvas, box, text, size=28, enabled=True):
    fill(canvas, (box[0], box[1], box[0] + box[2], box[1] + box[3]), (0, 0, 0, 150))
    ImageDraw.Draw(canvas).text((box[0] + box[2] / 2, box[1] + box[3] / 2), text, font=font(size), fill=PARCHMENT if enabled else DIM, anchor="mm")


def compose_provision(dd1, out_dir, length, purse):
    layout = Layout(dd1)
    items = load_items(dd1)
    stock, minimum_food = load_stock(dd1, items, length)
    # the sample purchase: the standard kit and two bandages, as far as the purse goes (ProvisionShop.Buy)
    wanted = standard_kit(stock, minimum_food, length) + [(i, 2) for i, _ in stock if i["id"] == "bandage"]
    bought, bill = {}, 0
    for item, n in wanted:
        n = min(n, (purse - bill) // item["buy"])
        if n > 0:
            bought[id(item)] = n
            bill += n * item["buy"]
    cost = bill

    canvas = Image.new("RGBA", (SCREEN_W, SCREEN_H), INK)
    paste(canvas, load(dd1, "campaign/town/provision/provision.background.png"), (0, 0), (SCREEN_W, SCREEN_H))
    art = layout.vec("town_background_layout", "area_pos")
    paste(canvas, load(dd1, "campaign/town/provision/provision.character_background.png"), art)
    paste(canvas, load(dd1, "campaign/town/provision/provision.character.png"), layout.vec("town_background_layout", "character_pos"))
    draw = ImageDraw.Draw(canvas)
    draw.text((art[0] + TITLE_POS[0], art[1] + TITLE_POS[1]), "Provisions", font=font(44), fill=GOLD, anchor="la")

    # shelves
    store = layout.vec("provision_store_background_layout", "pos")
    paste(canvas, load(dd1, "campaign/town/provision/inventory_grid_background_store.png"), store)
    start = layout.vec("provision_store_grid_layout", "start_pos")
    pitch = layout.vec("provision_store_grid_layout", "offset")
    columns = layout.vec("provision_store_grid_layout", "number_of_columns")[0]
    coin = load(dd1, "shared/estate/currency.gold.icon.png")
    for i, (item, amount) in enumerate(stock):
        x = store[0] + start[0] + pitch[0] * (i % columns)
        y = store[1] + start[1] + pitch[1] * (i // columns)
        left = amount - bought.get(id(item), 0)
        affordable = item["buy"] <= purse - bill
        card(canvas, dd1, item, left, (x, y), dim=left == 0 or not affordable)
        paste(canvas, coin, (x + 6, y + CARD[1] + 3), (20, 20))
        ImageDraw.Draw(canvas).text((x + 30, y + CARD[1] + PRICE_HEIGHT / 2), price_text(item["buy"]), font=font(20), fill=PARCHMENT if affordable else WARN, anchor="lm")

    # the bag
    party = layout.vec("provision_party_background_layout", "pos")
    paste(canvas, load(dd1, "campaign/town/provision/inventory_grid_background_party.png"), party)
    start = layout.vec("provision_party_grid_layout", "start_pos")
    pitch = layout.vec("provision_party_grid_layout", "offset")
    columns = layout.vec("provision_party_grid_layout", "number_of_columns")[0]
    # what the sample party brings by DD1's class table is in the bag before anything is bought (Provisioning.NewShop)
    carried = []
    for cls in ("crusader", "plague_doctor", "grave_robber"):
        for entry in read_json(os.path.join(dd1, "campaign", "provision", "provision.json"))["raid_starting_hero_class_item_lists"]:
            if entry["hero_class"] == cls:
                carried += [(items[(e["type"], e["id"])], e["amount"]) for e in entry["item_lists"]]
    carried += [(item, bought[id(item)]) for item, _ in stock if bought.get(id(item))]
    merged = []
    for item, n in carried:
        for i, (other, have) in enumerate(merged):
            if other is item:
                merged[i] = (item, have + n)
                break
        else:
            merged.append((item, n))
    slots = bag_slots(merged)
    for i, (item, amount) in enumerate(slots):
        card(canvas, dd1, item, amount, (party[0] + start[0] + pitch[0] * (i % columns), party[1] + start[1] + pitch[1] * (i // columns)))

    # strip under the art: purse and bill, hints, buttons
    top = art[1] + ART_SIZE[1] + STRIP_GAP
    fill(canvas, (art[0], top, art[0] + ART_SIZE[0], top + STRIP_HEIGHT), (5, 4, 4, 235))
    draw = ImageDraw.Draw(canvas)
    paste(canvas, coin, (art[0] + 24, top + 20), (32, 32))
    draw = ImageDraw.Draw(canvas)
    draw.text((art[0] + 66, top + 36), "Purse %d" % purse, font=font(28), fill=PARCHMENT, anchor="lm")
    draw.text((art[0] + 250, top + 36), "Provisions %d" % cost, font=font(28), fill=GOLD, anchor="lm")
    draw.text((art[0] + 500, top + 36), "Left %d" % (purse - cost), font=font(28), fill=PARCHMENT, anchor="lm")
    draw.text((art[0] + 24, top + 82), "Click the shelves to buy and the bag to sell back; a right click moves five.", font=font(20, False), fill=DIM, anchor="lm")
    draw.text((art[0] + 24, top + 107), "Leftovers are sold for a pittance when the party returns.", font=font(20, False), fill=DIM, anchor="lm")
    right = art[0] + ART_SIZE[0] - 16
    by = top + STRIP_HEIGHT - 16 - BUTTON_H
    button(canvas, (right - SET_OUT_W, by, SET_OUT_W, BUTTON_H), "Set out", 32)
    button(canvas, (right - SET_OUT_W - 12 - KIT_W, by, KIT_W, BUTTON_H), "Standard kit")
    button(canvas, (right - SET_OUT_W - 24 - KIT_W - BACK_W, by, BACK_W, BUTTON_H), "Back")
    ImageDraw.Draw(canvas).text((right, top + 36), "The usual minimum for this quest is %d food." % minimum_food, font=font(22, False), fill=DIM, anchor="rm")

    # roster column: the quest and the party
    fill(canvas, (ROSTER_X, 0, SCREEN_W, SCREEN_H), (5, 5, 6, 240))
    draw = ImageDraw.Draw(canvas)
    x = ROSTER_X + 16
    draw.text((x, 30), "The Expedition", font=font(30), fill=GOLD, anchor="lm")
    draw.text((x, 78), "Explore the Ruins", font=font(26), fill=PARCHMENT, anchor="lm")
    draw.text((x, 112), "Ruins · %s · Apprentice" % ["", "short", "medium", "long"][length], font=font(20, False), fill=DIM, anchor="lm")
    draw.text((x, 150), "Explore 90% of the rooms.", font=font(20, False), fill=PARCHMENT, anchor="lm")
    draw.text((x, 206), "Party", font=font(24), fill=GOLD, anchor="lm")
    for n, (name, cls, brings) in enumerate((("Reynauld", "Crusader", "brings 1 Holy water"), ("Dismas", "Highwayman", ""), ("Paracelsus", "Plague Doctor", "brings 1 Antivenom"), ("Audrey", "Grave Robber", "brings 1 Shovel"))):
        y = 232 + n * 62
        fill(canvas, (x - 8, y, SCREEN_W - 8, y + 58), (20, 20, 23, 204))
        fill(canvas, (x - 4, y + 2, x + 50, y + 56), (51, 51, 51, 255))
        draw = ImageDraw.Draw(canvas)
        draw.text((x + 58, y + 17), name, font=font(22), fill=PARCHMENT, anchor="lm")
        draw.text((x + 58, y + 43), cls + (" · " + brings if brings else ""), font=font(16, False), fill=DIM, anchor="lm")

    path = os.path.join(out_dir, "provision.png")
    canvas.convert("RGB").save(path)
    return path


def compose_hud(dd1, out_dir, length):
    items = load_items(dd1)
    canvas = Image.new("RGBA", (SCREEN_W, SCREEN_H), INK)
    # something to stand in for the corridor strip above the panel
    wall = load(dd1, "dungeons/crypts/crypts.corridor_wall.01.png")
    if wall is not None:
        for i in range(3):
            paste(canvas, wall, (i * 720 - 120, 0))
    top = SCREEN_H - PANEL_H
    fill(canvas, (0, top, SCREEN_W, SCREEN_H), (8, 8, 9, 255))
    fill(canvas, (0, top, SCREEN_W, top + 3), (89, 71, 41, 255))
    draw = ImageDraw.Draw(canvas)

    for i, name in enumerate(("Audrey", "Paracelsus", "Dismas", "Reynauld")):
        x, y = PLATE_X + i * PLATE_STEP, top + PLATE_Y
        fill(canvas, (x, y, x + 150, y + 150), (51, 51, 51, 255))
        draw = ImageDraw.Draw(canvas)
        draw.text((x, y + 173), name, font=font(26), fill=GOLD, anchor="lm")
        for row, colour, share, text in ((198, (158, 20, 15, 255), 0.7, "24/34"), (232, (219, 219, 230, 255), 0.3, "3")):
            fill(canvas, (x, y + row, x + 150, y + row + 18), (31, 31, 31, 255))
            fill(canvas, (x, y + row, x + int(150 * share), y + row + 18), colour)
            ImageDraw.Draw(canvas).text((x + 158, y + row + 9), text, font=font(20), fill=PARCHMENT, anchor="lm")
    lx, ly, lw, lh = LEAVE_BOX
    button(canvas, (lx, top + ly, lw, lh), "Abandon the Quest", 22)

    # middle column: status lines, then the bag
    draw = ImageDraw.Draw(canvas)
    x, y = MIDDLE_X, top + STATUS_Y
    draw.text((x, y + 18), "Explore the Ruins", font=font(30), fill=PARCHMENT, anchor="lm")
    draw.text((x + MIDDLE_W, y + 18), "Light 62", font=font(24), fill=PARCHMENT, anchor="rm")
    draw.text((x, y + 50), "Explore rooms  5/9", font=font(24), fill=PARCHMENT, anchor="lm")
    draw.text((x, y + 80), "Found: 5 gold, 2 Bust.", font=font(21, False), fill=DIM, anchor="lm")
    draw.text((x, y + 104), "A torch is lit.", font=font(21, False), fill=DIM, anchor="lm")

    def get(type_, id_=""):
        return items[(type_, id_)]

    bag = bag_slots([(get("provision"), 9), (get("supply", "torch"), 5), (get("supply", "shovel"), 1), (get("supply", "skeleton_key"), 1),
                     (get("supply", "bandage"), 2), (get("supply", "holy_water"), 1), (get("gold"), 2350), (get("heirloom", "bust"), 4),
                     (get("heirloom", "crest"), 8), (get("gem", "jade"), 2), (get("gem", "ruby"), 1), (get("supply", "medicinal_herbs"), 1)])
    w, h = int(CARD[0] * BAG_SCALE), int(CARD[1] * BAG_SCALE)
    slots = BAG_COLUMNS * 2
    for i in range(slots):
        sx, sy = MIDDLE_X + (i % BAG_COLUMNS) * w, top + BAG_Y + (i // BAG_COLUMNS) * h
        fill(canvas, (sx, sy, sx + w - 1, sy + h - 1), (22, 20, 18, 255))
        fill(canvas, (sx + 1, sy + 1, sx + w - 2, sy + h - 2), (12, 11, 10, 255))
        if i < len(bag):
            card(canvas, dd1, bag[i][0], bag[i][1], (sx, sy), BAG_SCALE)
    # the selected stack (an item waiting for a hero)
    sx, sy = MIDDLE_X + 4 * w, top + BAG_Y
    ImageDraw.Draw(canvas).rectangle((sx, sy, sx + w - 1, sy + h - 1), outline=GOLD, width=2)

    mx, my, mw, mh = MAP_BOX
    fill(canvas, (mx, top + my, mx + mw, top + my + mh), (14, 14, 16, 255))
    ImageDraw.Draw(canvas).text((mx + mw / 2, top + my + mh / 2), "map", font=font(24, False), fill=DIM, anchor="mm")

    path = os.path.join(out_dir, "hud_bag.png")
    canvas.convert("RGB").save(path)
    return path


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--dd1", default=os.environ.get("DD1_ROOT", r"E:\Steam\steamapps\common\DarkestDungeon"))
    ap.add_argument("--out", default=os.path.join(here, "..", "_lab", "preview"))
    ap.add_argument("--length", type=int, default=1, help="quest length: 1 short, 2 medium, 3 long")
    ap.add_argument("--purse", type=int, default=3000, help="gold in the purse")
    args = ap.parse_args()
    os.makedirs(args.out, exist_ok=True)
    print(compose_provision(args.dd1, args.out, args.length, args.purse))
    print(compose_hud(args.dd1, args.out, args.length))


if __name__ == "__main__":
    main()
