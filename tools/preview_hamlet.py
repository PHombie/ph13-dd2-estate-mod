#!/usr/bin/env python3
"""Compose the DD1 hamlet town view from the player's own Darkest Dungeon (1) install.

Reference implementation of the layout maths in src/DD2Estate/Estate/HamletScene.cs (and the parsers in
src/DD2Estate/Dd1/): change one, change the other. Nothing from DD1 is copied into the repo; output goes to
_lab/ (gitignored).

    python tools/preview_hamlet.py [--dd1 <install>] [--out <dir>] [--level 1|2|3] [--locked id,id,...]
                                   [--heroes N]

Writes
    hamlet.png         the town as drawn at rest (idle art)
    hamlet_hover.png   every building in its hovered state (the `active` silhouette behind it) with the name
                       plate, the layout bbox (yellow), the alpha hit area (cyan outline) and the root (red)
    hamlet_chrome.png  the town with the hamlet's chrome over it, set in DD1's own fonts: estate title plate,
                       roster column, estate currency bar, Embark (HamletScreen.cs, RosterPanel.cs,
                       EstateSummary.cs; see "chrome" below and docs/recon/dd1-ui.md)
    hamlet_chrome_narration.png  the same with the Ancestor's words over the bar (NarrationBox.cs)

How DD1 lays the town out (campaign/town/town.layout.darkest):
  * `pos3d` is a world position (x right, y up, z away from the viewer) seen by a camera at `camera_position`
    looking down +z. The focal length equals the camera's distance to the z = 0 plane, so that plane is 1:1
    with the 1920x1080 screen and the camera position lands on the screen centre:
        k = -cam.z / (z - cam.z);  screen = (960 + (x - cam.x) * k,  540 - (y - cam.y) * k)
  * Sprites are NOT shrunk by k. Depth only moves the anchor (and sets the draw order: far first). Every
    building skeleton and every scenery layer is drawn at its native pixel size with its bottom-centre (the
    Spine root bone) on the projected point. `.pos` is the same point pre-projected for a 2D layout
    (x, y up from the camera's ground line at 540 + cam.y, draw order) and agrees with pos3d within a few px.
  * The ground is the only true 3D surface: a quad from `ground_start` (near edge) to `ground_end` (far edge).
"""
import argparse
import json
import math
import os
import re
import struct

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

from dd1_bmfont import Dd1Text

SCREEN_W, SCREEN_H = 1920, 1080

# Draw order is by depth, so the list order does not matter. circus (Arena DLC) is left out.
BUILDINGS = ["tavern", "stage_coach", "blacksmith", "guild", "abbey", "camping_trainer",
             "nomad_wagon", "sanitarium", "graveyard", "statue"]
NAMES = {
    "stage_coach": "Stage Coach", "abbey": "Abbey", "tavern": "Tavern", "sanitarium": "Sanitarium",
    "blacksmith": "Blacksmith", "guild": "Guild", "camping_trainer": "Survivalist",
    "nomad_wagon": "Nomad Wagon", "graveyard": "Graveyard", "statue": "Ancestor's Statue",
}
# Scenery drawn like a building: native size, bottom-centre on the projected position.
SCENERY = [("left_cliff_position", "town_left_cliff.png"), ("bridge_position", "town_bridge.png"),
           ("right_tree_position", "town_right_tree.png")]
SLOT_ACTIVE = "active"               # hover silhouette, drawn behind `idle`; hidden at rest
SLOT_IDLE = "idle"
HIT_ALPHA = 64                       # a building is hit where its idle art is at least this opaque (0..255)


# ---------------------------------------------------------------- .darkest text

def parse_darkest(text):
    """`name:` blocks holding `.key token token ...` entries -> {block: {key: [tokens]}}."""
    blocks = {}
    block = None
    key = None
    for raw in text.splitlines():
        for tok, quoted in _tokens(raw):
            if not quoted and tok.endswith(":") and not tok.startswith("."):
                block = blocks.setdefault(tok[:-1], {})
                key = None
            elif not quoted and tok.startswith(".") and len(tok) > 1 and not _is_number(tok):
                if block is None:
                    block = blocks.setdefault("", {})
                key = tok[1:]
                block[key] = []
            elif block is not None and key is not None:
                block[key].append(tok)
    return blocks


def _tokens(line):
    """(text, quoted) tokens of one line; `//` and `#` start a comment outside quotes."""
    out = []
    i, n = 0, len(line)
    while i < n:
        c = line[i]
        if c in " \t\r":
            i += 1
        elif c == "#" or line.startswith("//", i):
            break
        elif c == '"':
            j = line.find('"', i + 1)
            if j < 0:
                j = n
            out.append((line[i + 1:j], True))
            i = j + 1
        else:
            j = i
            while j < n and line[j] not in ' \t\r"#' and not line.startswith("//", j):
                j += 1
            out.append((line[i:j], False))
            i = j
    return out


def _is_number(tok):
    try:
        float(tok)
        return True
    except ValueError:
        return False


def floats(block, key, count, default=0.0):
    vals = []
    for tok in (block or {}).get(key, []):
        try:
            vals.append(float(tok))
        except ValueError:
            break
    while len(vals) < count:
        vals.append(default)
    return vals[:count]


# ---------------------------------------------------------------- libgdx atlas

def parse_atlas(text):
    """-> (page file name, {region name: dict(x, y, w, h, rotate, orig_w, orig_h, off_x, off_y)}).

    Single-page atlases only (all the town ones are)."""
    page = None
    regions = {}
    cur = None
    for raw in text.splitlines():
        line = raw.strip()
        if not line:
            continue
        if ":" in line:
            if cur is None:
                continue                      # page header (size/format/filter/repeat)
            k, v = line.split(":", 1)
            k = k.strip()
            vals = [s.strip() for s in v.split(",")]
            if k == "rotate":
                cur["rotate"] = vals[0] == "true"
            elif k == "xy":
                cur["x"], cur["y"] = int(vals[0]), int(vals[1])
            elif k == "size":
                cur["w"], cur["h"] = int(vals[0]), int(vals[1])
            elif k == "orig":
                cur["orig_w"], cur["orig_h"] = int(vals[0]), int(vals[1])
            elif k == "offset":
                cur["off_x"], cur["off_y"] = int(vals[0]), int(vals[1])
        elif page is None:
            page = line
        else:
            cur = dict(x=0, y=0, w=0, h=0, rotate=False, orig_w=0, orig_h=0, off_x=0, off_y=0)
            regions[line] = cur
    for r in regions.values():
        if r["orig_w"] == 0:
            r["orig_w"], r["orig_h"] = r["w"], r["h"]
    return page, regions


def cut_region(page_img, r):
    """Upright image of an atlas region.

    `xy` is the top-left corner in the page and `size` the upright size. A `rotate: true` region is stored
    turned 90 degrees counter-clockwise, so it occupies h x w page pixels and is turned back clockwise."""
    if r["rotate"]:
        box = (r["x"], r["y"], r["x"] + r["h"], r["y"] + r["w"])
        return page_img.crop(box).transpose(Image.ROTATE_270)   # PIL's 270 ccw = 90 degrees clockwise
    return page_img.crop((r["x"], r["y"], r["x"] + r["w"], r["y"] + r["h"]))


# ---------------------------------------------------------------- Spine 2.1 binary skeleton (setup pose)

class _Reader:
    def __init__(self, data):
        self.b = data
        self.p = 0

    def byte(self):
        v = self.b[self.p]
        self.p += 1
        return v

    def boolean(self):
        return self.byte() != 0

    def varint(self):
        result, shift = 0, 0
        while True:
            b = self.byte()
            result |= (b & 0x7F) << shift
            shift += 7
            if not (b & 0x80) or shift >= 35:
                return result

    def float(self):
        v = struct.unpack_from(">f", self.b, self.p)[0]
        self.p += 4
        return v

    def int(self):
        v = struct.unpack_from(">I", self.b, self.p)[0]
        self.p += 4
        return v

    def string(self):
        n = self.varint()            # byte length + 1; 0 = null
        if n == 0:
            return None
        v = self.b[self.p:self.p + n - 1].decode("utf8")
        self.p += n - 1
        return v

    def skip_floats(self):
        n = self.varint()
        self.p += 4 * n

    def skip_shorts(self):
        n = self.varint()
        self.p += 2 * n

    def skip_varints(self):
        for _ in range(self.varint()):
            self.varint()


def read_skeleton(data):
    """Bones, slots and the default skin's region attachments of a Spine 2.1 .skel.

    Named skins, events and animations follow in the file and are not read."""
    r = _Reader(data)
    r.string()                       # hash
    version = r.string()
    r.float()                        # width
    r.float()                        # height
    nonessential = r.boolean()
    if nonessential:
        r.string()                   # images path

    bones = []
    for _ in range(r.varint()):
        name = r.string()
        parent = r.varint() - 1
        bone = dict(name=name, parent=parent, x=r.float(), y=r.float(), sx=r.float(), sy=r.float(), rot=r.float())
        r.float()                    # length
        r.boolean(); r.boolean()     # flipX, flipY
        r.boolean(); r.boolean()     # inheritScale, inheritRotation
        if nonessential:
            r.int()                  # colour
        bones.append(bone)

    for _ in range(r.varint()):      # ik constraints
        r.string()
        for _ in range(r.varint()):
            r.varint()
        r.varint(); r.float(); r.byte()

    slots = []
    for _ in range(r.varint()):
        slots.append(dict(name=r.string(), bone=r.varint(), color=r.int(), attachment=r.string(), additive=r.boolean()))

    attachments = {}                 # (slot index, attachment name) -> region attachment
    for _ in range(r.varint()):      # default skin: slot count
        slot = r.varint()
        for _ in range(r.varint()):
            key = r.string()
            name = r.string() or key
            kind = r.byte()
            if kind == 0:            # region
                path = r.string() or name
                attachments[(slot, key)] = dict(path=path, x=r.float(), y=r.float(), sx=r.float(), sy=r.float(),
                                                rot=r.float(), w=r.float(), h=r.float(), color=r.int())
            elif kind == 1:          # bounding box
                r.skip_floats()
            elif kind == 2:          # mesh (chimney smoke, camp fire): shaped by animation only, not drawn
                r.string()
                r.skip_floats(); r.skip_shorts(); r.skip_floats()
                r.int(); r.varint()
                if nonessential:
                    r.skip_varints(); r.float(); r.float()
            else:
                raise ValueError("unsupported Spine attachment type %d" % kind)
    return dict(version=version, bones=bones, slots=slots, attachments=attachments)


def setup_pose(skel):
    """Region attachments in draw order: dict(slot, region, cx, cy, w, h, rot, alpha).

    cx/cy is the region centre relative to the skeleton origin with y up; w/h the drawn size; rot in degrees
    counter-clockwise."""
    world = []
    for b in skel["bones"]:
        if b["parent"] < 0:
            x, y, rot, sx, sy = b["x"], b["y"], b["rot"], b["sx"], b["sy"]
        else:
            p = world[b["parent"]]
            x = p["a"] * b["x"] + p["b"] * b["y"] + p["x"]
            y = p["c"] * b["x"] + p["d"] * b["y"] + p["y"]
            rot = p["rot"] + b["rot"]
            sx, sy = p["sx"] * b["sx"], p["sy"] * b["sy"]
        rad = math.radians(rot)
        world.append(dict(a=math.cos(rad) * sx, b=-math.sin(rad) * sy, c=math.sin(rad) * sx, d=math.cos(rad) * sy,
                          x=x, y=y, rot=rot, sx=sx, sy=sy))
    parts = []
    for i, slot in enumerate(skel["slots"]):
        att = skel["attachments"].get((i, slot["attachment"]))
        if att is None:
            continue
        bone = world[slot["bone"]]
        parts.append(dict(
            slot=slot["name"], region=att["path"],
            cx=bone["a"] * att["x"] + bone["b"] * att["y"] + bone["x"],
            cy=bone["c"] * att["x"] + bone["d"] * att["y"] + bone["y"],
            w=att["w"] * att["sx"] * bone["sx"], h=att["h"] * att["sy"] * bone["sy"],
            rot=bone["rot"] + att["rot"],
            alpha=(slot["color"] & 0xFF) / 255.0 * (att["color"] & 0xFF) / 255.0))
    return parts


# ---------------------------------------------------------------- town layout

class Town:
    def __init__(self, dd1):
        self.dd1 = dd1
        with open(os.path.join(dd1, "campaign", "town", "town.layout.darkest"), encoding="utf-8-sig") as f:
            self.layout = parse_darkest(f.read())
        self.screen = self.layout["town_screen_layout"]
        self.cam = floats(self.screen, "camera_position", 3)

    def path(self, *rel):
        return os.path.join(self.dd1, *rel)

    def project(self, p):
        """World point -> (x, y) in screen pixels from the top-left of the 1920x1080 view."""
        k = -self.cam[2] / (p[2] - self.cam[2])
        return (SCREEN_W / 2 + (p[0] - self.cam[0]) * k, SCREEN_H / 2 - (p[1] - self.cam[1]) * k)

    def ground_quad(self):
        """Screen corners of the ground: far-left, far-right, near-right, near-left."""
        s = floats(self.screen, "ground_start", 3)
        e = floats(self.screen, "ground_end", 3)
        return [self.project((s[0], e[1], e[2])), self.project((e[0], e[1], e[2])),
                self.project((e[0], s[1], s[2])), self.project((s[0], s[1], s[2]))]

    def building(self, bid):
        lay = self.layout[bid + "_layout"]
        pos3d = floats(lay, "pos3d", 3)
        root = self.project(pos3d)
        box_off = floats(lay, "bbox_offset", 2)
        box_size = floats(lay, "bbox_size", 2)
        # bbox: bottom-centre on the root, offset x right / y up (its top and right edges then meet the art)
        bbox = (root[0] + box_off[0] - box_size[0] / 2, root[1] - box_off[1] - box_size[1],
                root[0] + box_off[0] + box_size[0] / 2, root[1] - box_off[1])
        # text_offset (and alert_offset) are y-down offsets from the bbox centre: read that way the alert
        # offsets land on each building's door or sign, read from the root they land in the mud.
        text = floats(lay, "text_offset", 2)
        anchor = ((bbox[0] + bbox[2]) / 2 + text[0], (bbox[1] + bbox[3]) / 2 + text[1])
        plate_off = floats(self.layout.get("building_text_layout", {}), "background_offset", 2)
        return dict(
            id=bid, root=root, z=pos3d[2], order=floats(lay, "pos", 3)[2], scale=floats(lay, "scale", 1, 1.0)[0],
            bbox=bbox, plate=(anchor[0] + plate_off[0], anchor[1] + plate_off[1]))   # top-left of the ink blot

    def variant(self, bid, level, locked):
        """Folder/file stem of a building's skeleton: fx/<stem>/<stem>.sprite.{atlas,png,skel}."""
        candidates = []
        if locked:
            candidates.append("town_%s_locked" % bid)
        candidates.append("town_%s_level%02d" % (bid, max(1, min(3, level))))
        candidates.append("town_%s_level01" % bid)
        candidates.append("town_%s" % bid)                      # graveyard has a single skeleton
        for stem in candidates:
            if os.path.isfile(self.path("fx", stem, stem + ".sprite.skel")):
                return stem
        return None

    def building_parts(self, bid, level, locked):
        stem = self.variant(bid, level, locked)
        base = self.path("fx", stem, stem + ".sprite")
        with open(base + ".atlas", encoding="utf-8-sig") as f:
            page_name, regions = parse_atlas(f.read())
        page = load_rgba(self.path("fx", stem, page_name))
        with open(base + ".skel", "rb") as f:
            parts = setup_pose(read_skeleton(f.read()))
        out = []
        for p in parts:
            if p["region"] not in regions:
                continue
            p["img"] = cut_region(page, regions[p["region"]])
            out.append(p)
        return out


# ---------------------------------------------------------------- drawing

def load_rgba(path):
    return Image.open(path).convert("RGBA")


def paste(canvas, img, left, top, alpha=1.0):
    """alpha_composite that tolerates off-canvas placement."""
    if alpha < 1.0:
        img = img.copy()
        img.putalpha(img.getchannel("A").point(lambda v: int(v * alpha)))
    layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    layer.paste(img, (int(round(left)), int(round(top))))
    canvas.alpha_composite(layer)


def tile_x(canvas, img, left, top):
    """A sky layer: repeated across the screen width (the game scrolls it by `sky_anim` speedX)."""
    x = left % img.width - img.width
    while x < canvas.width:
        paste(canvas, img, x, top)
        x += img.width


def ground_coefficients(town, tex_w, tex_h):
    """PIL PERSPECTIVE coefficients (screen -> texture) for the ground quad.

    Both edges are parallel to the screen, so every screen row is one texture row stretched sideways; the C#
    side draws the same thing as a strip mesh. With t = 0 at the near edge and 1 at the far edge:
        depth d = d0 + (d1 - d0) t, height h = h0 + (h1 - h0) t (both relative to the camera)
        Y - 540 = -f h / d   =>   t = -(f h0 + Y' d0) / D,   D = Y' (d1 - d0) + f (h1 - h0)
    """
    cam = town.cam
    f = -cam[2]
    s = floats(town.screen, "ground_start", 3)
    e = floats(town.screen, "ground_end", 3)
    d0, d1 = s[2] - cam[2], e[2] - cam[2]
    h0, h1 = s[1] - cam[1], e[1] - cam[1]
    dd, dh = d1 - d0, h1 - h0
    c = d0 * dh - dd * h0                       # d = f c / D
    den0 = f * dh - SCREEN_H / 2 * dd           # D = dd * Y + den0
    wn = tex_w / (e[0] - s[0])
    ox = cam[0] - s[0]
    return (wn * c / den0, wn * ox * dd / den0, wn * (ox * den0 - SCREEN_W / 2 * c) / den0,
            0.0, tex_h * d1 / den0, tex_h * (f * h1 - SCREEN_H / 2 * d1) / den0,
            0.0, dd / den0)


def draw_ground(canvas, town):
    tex = load_rgba(town.path("campaign", "town", "town_ground.png"))
    quad = town.ground_quad()
    warped = tex.transform(canvas.size, Image.PERSPECTIVE, ground_coefficients(town, tex.width, tex.height), Image.BICUBIC)
    mask = Image.new("L", canvas.size, 0)
    ImageDraw.Draw(mask).polygon(quad, fill=255)
    warped.putalpha(ImageChops.multiply(warped.getchannel("A"), mask))
    canvas.alpha_composite(warped)
    # DD1 hides everything below the near edge behind its estate bar; the texture's last row is black.
    near_y = int(round(quad[3][1]))
    ImageDraw.Draw(canvas).rectangle((0, near_y, canvas.width, canvas.height), fill=(0, 0, 0, 255))


def part_image(part, scale):
    w = max(1, int(round(part["w"] * scale)))
    h = max(1, int(round(part["h"] * scale)))
    img = part["img"].resize((w, h), Image.LANCZOS)
    if abs(part["rot"]) > 0.01:
        img = img.rotate(part["rot"], Image.BICUBIC, expand=True)
    return img


def compose(town, level, locked, hover):
    canvas = load_rgba(town.path("campaign", "town", "town_bg.png")).resize((SCREEN_W, SCREEN_H))

    sky = town.layout.get("sky_layout", {})
    for key, name in (("pos1", "sky01.png"), ("pos2", "sky02.png")):
        pos = floats(sky, key, 2)
        tile_x(canvas, load_rgba(town.path("campaign", "town", "sky", name)), pos[0], pos[1])
    pos = floats(town.layout.get("midground_layout", {}), "pos", 2)
    paste(canvas, load_rgba(town.path("campaign", "town", "sky", "ruins.png")), pos[0], pos[1])

    def anchored(key, name):
        img = load_rgba(town.path("campaign", "town", name))
        world = floats(town.screen, key, 3)
        x, y = town.project(world)
        return world[2], img, x - img.width / 2, y - img.height

    _, img, left, top = anchored("backdrop_position", "town_backdrop.png")
    paste(canvas, img, left, top)
    draw_ground(canvas, town)

    # far to near; at equal depth scenery goes under buildings, then the 2D draw order decides
    items = []
    for key, name in SCENERY:
        z, img, left, top = anchored(key, name)
        items.append((-z, 0, 0.0, name, ("layer", img, left, top)))
    infos = {}
    for bid in BUILDINGS:
        if bid + "_layout" not in town.layout:
            continue
        info = town.building(bid)
        info["parts"] = town.building_parts(bid, level, bid in locked)
        infos[bid] = info
        items.append((-info["z"], 1, -info["order"], bid, ("building", info)))
    items.sort(key=lambda it: it[:4])

    for _, _, _, _, item in items:
        if item[0] == "layer":
            paste(canvas, item[1], item[2], item[3])
            continue
        info = item[1]
        for part in info["parts"]:
            if part["slot"] == SLOT_ACTIVE and not hover:
                continue
            img = part_image(part, info["scale"])
            paste(canvas, img,
                  info["root"][0] + part["cx"] * info["scale"] - img.width / 2,
                  info["root"][1] - part["cy"] * info["scale"] - img.height / 2, part["alpha"])
    return canvas, infos


def annotate(canvas, town, infos, text=None):
    plate = load_rgba(town.path("campaign", "town", "buildings", "blg_name_background.png"))
    try:
        font = ImageFont.truetype("georgiab.ttf", 30)
    except OSError:
        font = ImageFont.load_default()

    # alpha hit areas (what the plugin raycasts against), outlined
    for info in infos.values():
        for part in info["parts"]:
            if part["slot"] != SLOT_IDLE:
                continue
            img = part_image(part, info["scale"])
            solid = img.getchannel("A").point(lambda v: 255 if v >= HIT_ALPHA else 0)
            edge = ImageChops.subtract(solid.filter(ImageFilter.MaxFilter(3)), solid)
            tint = Image.new("RGBA", img.size, (0, 255, 255, 0))
            tint.putalpha(edge)
            paste(canvas, tint,
                  info["root"][0] + part["cx"] * info["scale"] - img.width / 2,
                  info["root"][1] - part["cy"] * info["scale"] - img.height / 2)

    draw = ImageDraw.Draw(canvas)
    for info in infos.values():
        x, y = info["root"]
        draw.rectangle(info["bbox"], outline=(255, 220, 0, 255), width=2)
        draw.line((x - 8, y, x + 8, y), fill=(255, 0, 0, 255), width=2)
        draw.line((x, y - 8, x, y + 8), fill=(255, 0, 0, 255), width=2)
    for info in infos.values():
        paste(canvas, plate, info["plate"][0], info["plate"][1])
    draw = ImageDraw.Draw(canvas)
    # building_text_layout: the blot's corner lies background_offset from the text's place; the name stands at
    # name_offset and DD1's line about the building (str_<id>_summary) at description_offset, both centred on that
    # point (a building's text_offset counts from the middle of its hover box). HamletScene.BuildNamePlate.
    words = town.layout.get("building_text_layout", {})
    back = floats(words, "background_offset", 2) if "background_offset" in words else [-70.0, -70.0]
    name_at = floats(words, "name_offset", 2) if "name_offset" in words else [0.0, 0.0]
    summary_at = floats(words, "description_offset", 2) if "description_offset" in words else [0.0, 50.0]
    for info in infos.values():
        px, py = info["plate"][0] - back[0], info["plate"][1] - back[1]
        name = dd1_string(town, "town_name_" + info["id"], NAMES.get(info["id"], info["id"]))
        if text is None:
            draw.text((px, py + 24), name, font=font, fill=(222, 209, 178, 255), anchor="mm")
            continue
        text.draw(canvas, (px + name_at[0], py + name_at[1]), name, "town_screen_building_name", anchor="m")
        summary = dd1_string(town, "str_" + info["id"] + "_summary", "")
        if summary:
            text.draw(canvas, (px + summary_at[0], py + summary_at[1]), summary, "town_screen_building_description", anchor="m")


# ---------------------------------------------------------------- chrome (hamlet_chrome.png)
#
# Reference for HamletScreen.cs, RosterPanel.cs, EstateSummary.cs and NarrationBox.cs: change one, change the
# other. Everything is in DD1's 1920x1080 screen pixels, y down, art at its native size. A number marked
# FALLBACK is DD1's own value, used when the layout file cannot be read; MOD marks what DD1 has no number for.
#
#   town.layout.darkest          town_estate_title_layout  .pos, .text_offset: the name plate and the estate's
#                                                          name on its band
#                                town_screen_layout        .roster_list_pos, .estate_summary_pos
#   roster/roster.layout.darkest town_roster_list_layout   rows (element_pos, element_spacing, element_max),
#                                                          frames, count, sort buttons, scroll arrows
#                                town_roster_element_layout what lies where on a row
#   shared/resolve_level_bar/resolve_level_bar.layout.darkest  the level badge and its number
#   estate_summary/estate_summary.layout.darkest           the currency strip on the bar
#   shared/estate/estate.layout.darkest                    icon and number of one currency
#   shared/progression/progression.layout.darkest          back arrows (back_pos) and the forward button
#   shared/app.darkest           s_TownSubtitleContextTunables  where the Ancestor's words go

ROSTER_DIR = "campaign/town/roster/"
SLOT_DIR = "campaign/town/hero_slot/"
RESOLVE_DIR = "shared/resolve_level_bar/"
PROGRESSION_DIR = "shared/progression/"

PORTRAIT = 85                        # hero_slot.background.png, DD1's roster portraits
PORTRAIT_INSET = 3                   # MOD: the slot's frame stays clear of the portrait (the plugin draws DD2's portrait,
                                     #      a head with wide clear margins, at 98 px and cuts it off at this window)
PIPS = 10                            # DD1 draws stress as ten pips; DD2's stress runs 0..10
WEEK_RIGHT = 772                     # MOD: "Week n" ends here on the plate's band (the brush stroke frays after)
WEEK_TOP = 77                        # MOD: its cell top, centring DwarvenAxe-m's capitals on the band (72..122)
HEALTH_BAR = (0, -5, 100, 3)         # MOD: x, y from stress_offset, width, height; the middle of DD1's health bar gradient
CAPTION = (262, 66, 104, 34)         # MOD: class and path, right aligned in this box under the level badge
CAPTION_SIZE = 15                    # MOD: Ubuntu at this TextMeshPro size (native is 23)
CAPTION_SMALLEST = 11                # MOD: a long class or reason shrinks down to this before it is cut
DISMISS_POS = (321, 8)               # MOD: shared/character/icon_dismiss.png, between the level badge and the badge of the hero's path (DD2's art, not drawn here: middle line x 344, from row 2, 94 high; it keeps its place while the row slides, and the gap that opens holds this control)
COUNT_RIGHT = 364                    # MOD: "7/12" ends here, past the sort buttons
NARRATION_FOOT = 24                  # MOD: a long text stops this far above the screen's edge
NARRATION_LAYERS = 4                 # MOD: DD1's band (64% black) laid this deep, so the bar under the words does not read through

HEIRLOOMS = ["bust", "portrait", "deed", "crest"]   # FALLBACK order (campaign/estate/estate.json)


def lay(blocks, block, key, *fallback):
    """Numbers of a layout entry, or DD1's stock values when the file or the entry is missing."""
    entry = (blocks or {}).get(block, {})
    return floats(entry, key, len(fallback)) if key in entry else list(fallback)


def read_layout(town, *rel):
    path = town.path(*rel)
    if not os.path.isfile(path):
        return {}
    with open(path, encoding="utf-8-sig") as f:
        return parse_darkest(f.read())


def dd1_string(town, key, fallback):
    """English text of a DD1 string table entry."""
    path = town.path("localization", "miscellaneous.string_table.xml")
    if not os.path.isfile(path):
        return fallback
    with open(path, encoding="utf-8", errors="replace") as f:
        text = f.read()
    end = text.find('<language id="', text.find('<language id="english">') + 10)
    m = re.search(r'<entry id="%s"><!\[CDATA\[(.*?)\]\]>' % re.escape(key), text[:end if end > 0 else len(text)], re.S)
    return m.group(1) if m else fallback


def art(town, rel):
    path = town.path(*rel.split("/"))
    return load_rgba(path) if os.path.isfile(path) else None


def put(canvas, town, rel, pos, alpha=1.0):
    img = art(town, rel)
    if img is not None:
        paste(canvas, img, pos[0], pos[1], alpha)
    return img


def fill(canvas, box, colour):
    x, y, w, h = (int(round(v)) for v in box)
    if w > 0 and h > 0:
        canvas.alpha_composite(Image.new("RGBA", (w, h), tuple(colour[:3]) + (255,)), (x, y))


SAMPLE_HEROES = [
    # name, DD1 class for the stand-in portrait, class and path, stress 0..10, resolve level, health share,
    # weapon, armour, state: party / idle / a building id / missing, why they cannot embark
    ("Reynauld", "crusader", "Crusader, Wanderer", 3, 2, 1.0, 2, 2, "party", None),
    ("Dismas", "highwayman", "Highwayman, Wanderer", 5, 1, 1.0, 2, 1, "party", None),
    ("Paracelsus", "plague_doctor", "Plague Doctor, Alchemist", 2, 1, 1.0, 1, 1, "party", None),
    ("Barristan", "man_at_arms", "Man-at-Arms, Vanguard", 8, 3, 0.6, 12, 12, "party", None),
    ("Sahar", "hellion", "Duelist, Instructrice", 6, 0, 1.0, 1, 1, "sanitarium", "In the Sanitarium"),
    ("Senarpont", "highwayman", "Highwayman, Sharpshot", 4, 0, 1.0, 1, 1, "tavern", "At the Tavern"),
    ("Marchés", "highwayman", "Highwayman, Rogue", 0, 0, 1.0, 1, 1, "idle", None),
    ("Audrey", "grave_robber", "Grave Robber, Nightsworn", 10, 4, 1.0, 3, 2, "idle", "Resolve 4: this quest is beneath them"),
    ("Boudica", "hellion", "Hellion, Ravager", 1, 5, 1.0, 4, 4, "missing", "Missing"),
    ("Alhazred", "occultist", "Occultist, Wanderer", 2, 6, 1.0, 5, 5, "abbey", "At the Abbey"),
    ("Tardif", "bounty_hunter", "Bounty Hunter", 7, 2, 1.0, 2, 3, "idle", None),
]


def draw_chrome(canvas, town, text, heroes, roster_size, week, purse, first=0, hover=None, narration=None):
    t = town.layout
    roster = read_layout(town, "campaign", "town", "roster", "roster.layout.darkest")
    summary = read_layout(town, "campaign", "town", "estate_summary", "estate_summary.layout.darkest")
    estate = read_layout(town, "shared", "estate", "estate.layout.darkest")
    progression = read_layout(town, "shared", "progression", "progression.layout.darkest")
    resolve = read_layout(town, "shared", "resolve_level_bar", "resolve_level_bar.layout.darkest")

    # ---- the estate's name plate (top left) ------------------------------------------------------------
    plate = lay(t, "town_estate_title_layout", "pos", 0, 0)
    title_at = lay(t, "town_estate_title_layout", "text_offset", 286, 70)
    put(canvas, town, "campaign/town/estate_title/estate_nameplate.png", plate)
    title = dd1_string(town, "estate_title_format", "The %s Estate") % dd1_string(town, "estate_title_default_data", "Darkest")
    text.draw(canvas, (plate[0] + title_at[0], plate[1] + title_at[1]), title, "town_estate_title")
    # DD1 keeps the week in its activity log; the mod has none, so the week stands at the end of the band.
    text.draw(canvas, (plate[0] + WEEK_RIGHT, plate[1] + WEEK_TOP), dd1_string(town, "str_week", "Week %d") % week,
              "town_activity_log_week_count", anchor="r")
    # DD1's way back out of a screen: the red arrows at the head of the band. Here they lead to the main menu.
    back = lay(progression, "progression_layout", "back_pos", 228, 82)
    put(canvas, town, PROGRESSION_DIR + "progression_back.png", back)

    # ---- the roster column (right) ---------------------------------------------------------------------
    rl = "town_roster_list_layout"
    origin = lay(t, "town_screen_layout", "roster_list_pos", 1550, 0)
    element_pos = lay(roster, rl, "element_pos", 0, 132)
    spacing = lay(roster, rl, "element_spacing", 0, 97)
    visible = int(lay(roster, rl, "element_max", 8)[0])
    top_frame = lay(roster, rl, "top_frame_offset", 20, -50)
    bottom_frame = lay(roster, rl, "bottom_frame_offset", 20, -10)
    message = lay(roster, rl, "roster_message_offset", 60, 78)
    sort_at = lay(roster, rl, "roster_sort_start_position", 148, 80)
    sort_gap = lay(roster, rl, "roster_sort_spacing", 6, 0)
    overlay_up = lay(roster, rl, "roster_sort_current_ascending_overlay_offset", -8, -8)
    scroll_up = lay(roster, rl, "scroll_up_button_offset", 0, -38)
    scroll_down = lay(roster, rl, "scroll_down_button_offset", 0, -12)
    list_end = element_pos[1] + visible * spacing[1]

    grad = art(town, ROSTER_DIR + "roster_bggrad.png")
    if grad is not None:
        paste(canvas, grad.resize((grad.width, SCREEN_H)), origin[0], origin[1])
    rows = heroes[first:first + visible]
    for i, hero in enumerate(rows):
        slide = -30 if hover == first + i else 0          # roster.layout.anim.darkest: x_pixel_slide 30
        draw_roster_row(canvas, town, text, roster, resolve,
                        (origin[0] + element_pos[0] + slide, origin[1] + element_pos[1] + i * spacing[1]), hero, hover == first + i)
    put(canvas, town, ROSTER_DIR + "roster_topframe.png", (origin[0] + top_frame[0], origin[1] + element_pos[1] + top_frame[1]))
    put(canvas, town, ROSTER_DIR + "roster_bottomframe.png", (origin[0] + bottom_frame[0], origin[1] + list_end + bottom_frame[1]))

    # DD1 writes "Full" at the message offset when the barracks are; the mod names the column there and keeps
    # the count in view past the four sort buttons.
    text.draw(canvas, (origin[0] + message[0], origin[1] + message[1]), "Roster", "roster_full")
    text.draw(canvas, (origin[0] + COUNT_RIGHT, origin[1] + message[1]), "%d/%d" % (len(heroes), roster_size), "roster_full",
              colour="harmful" if len(heroes) >= roster_size else None, anchor="r")
    x = origin[0] + sort_at[0]
    for name in ("building", "class", "level", "stress"):
        icon = put(canvas, town, ROSTER_DIR + "roster_sort_%s.png" % name, (x, origin[1] + sort_at[1]))
        if name == "stress":                              # the order in use: a gold tick over the button
            put(canvas, town, ROSTER_DIR + "roster_sort_current_overlay.png", (x + overlay_up[0], origin[1] + sort_at[1] + overlay_up[1]))
        x += (icon.width if icon else 32) + sort_gap[0]
    # scroll arrows, from the first row's corner and from the end of the list, only when there is somewhere to go
    if first > 0:
        put(canvas, town, ROSTER_DIR + "roster_uparrow.png", (origin[0] + element_pos[0] + scroll_up[0], origin[1] + element_pos[1] + scroll_up[1]))
    if first + visible < len(heroes):
        put(canvas, town, ROSTER_DIR + "roster_downarrow.png", (origin[0] + element_pos[0] + scroll_down[0], origin[1] + list_end + scroll_down[1]))

    # ---- the estate's bar (bottom): currencies and the way forward ---------------------------------------
    sl = "estate_summary_layout"
    base = lay(t, "town_screen_layout", "estate_summary_pos", 0, 975)
    offset = lay(summary, sl, "pos_offset", 0, -17)
    bar = (base[0] + offset[0], base[1] + offset[1])
    put(canvas, town, PROGRESSION_DIR + "progression_bar.png", bar)
    currency = lay(summary, sl, "currency_pos", 200, 42)
    step = lay(summary, sl, "currency_spacing", 74, 0)
    large_step = lay(summary, sl, "large_currency_spacing", 200, 0)
    at = [bar[0] + currency[0], bar[1] + currency[1]]
    icon = lay(estate, "estate_large_currency_layout", "icon_offset", 0, -50)
    number = lay(estate, "estate_large_currency_layout", "number_offset", 90, -16)
    put(canvas, town, "shared/estate/currency.gold.large_icon.png", (at[0] + icon[0], at[1] + icon[1]))
    text.draw(canvas, (at[0] + number[0], at[1] + number[1]), str(purse["gold"]), "town_large_currency_amount", colour="town_currency_amount")
    at[0] += large_step[0]
    at[1] += large_step[1]
    icon = lay(estate, "estate_currency_heirloom_layout", "icon_offset", 0, -10)
    number = lay(estate, "estate_currency_heirloom_layout", "number_offset", 38, -4)
    for heirloom in heirloom_order(town):
        put(canvas, town, "shared/estate/currency.%s.icon.png" % heirloom, (at[0] + icon[0], at[1] + icon[1]))
        text.draw(canvas, (at[0] + number[0], at[1] + number[1]), str(purse[heirloom]), "town_currency_amount")
        at[0] += step[0]
        at[1] += step[1]
    forward = lay(progression, "progression_layout", "forward_pos", 801, 984)
    forward_text = lay(progression, "progression_layout", "forward_text_offset", 160, -2)
    put(canvas, town, PROGRESSION_DIR + "progression_forward.png", forward)
    text.draw(canvas, (forward[0] + forward_text[0], forward[1] + forward_text[1]),
              dd1_string(town, "town_progression_forward_embark", "Embark"), "town_progression_forward", anchor="m")

    if narration:
        draw_narration(canvas, town, text, narration)


def heirloom_order(town):
    path = town.path("campaign", "estate", "estate.json")
    try:
        with open(path, encoding="utf-8-sig") as f:
            ids = [c["id"] for c in json.load(f)["currencies"] if c.get("is_heirloom")]
        return [i for i in ids if i in HEIRLOOMS] or HEIRLOOMS
    except (OSError, ValueError, KeyError):
        return HEIRLOOMS


def draw_roster_row(canvas, town, text, roster, resolve, pos, hero, hovered=False):
    name, dd1_class, title, stress, level, health, weapon, armour, state, blocked = hero
    el = "town_roster_element_layout"
    portrait_at = lay(roster, el, "portrait_icon_offset", 21, 9)
    building_at = lay(roster, el, "building_icon_offset", 20, 10)
    mark_at = lay(roster, el, "non_building_icon_offset", 14, 10)
    name_at = lay(roster, el, "name_offset", 116, 4)
    stress_at = lay(roster, el, "stress_offset", 116, 43)
    stress_step = lay(roster, el, "stress_spacing", 10, 0)
    weapon_at = lay(roster, el, "weapon_level_offset", 156, 65)
    armour_at = lay(roster, el, "armour_level_offset", 228, 65)
    badge_at = lay(roster, el, "resolve_level_bar_offset", 258, 4)
    badge_back = lay(resolve, "resolve_level_number", "background_offset", -3, 0)
    badge_number = lay(resolve, "resolve_level_number", "number_offset", 30, 31)
    x, y = pos

    put(canvas, town, ROSTER_DIR + "rosterelement.background.png", pos)
    if level > 0:                                         # the row's edge takes the colour of the resolve level
        put(canvas, town, ROSTER_DIR + "rosterelement_res%d.png" % min(level, 6), pos)

    # portrait: DD1's slot frame with the hero's (DD2) portrait in it; here a DD1 portrait stands in
    in_party = state == "party"
    put(canvas, town, SLOT_DIR + ("hero_slot.backgroundhightlight.png" if in_party else "hero_slot.background.png"),
        (x + portrait_at[0], y + portrait_at[1]))
    face = art(town, "heroes/%s/%s_A/%s_portrait_roster.png" % (dd1_class, dd1_class, dd1_class))
    if face is not None:
        inner = PORTRAIT - 2 * PORTRAIT_INSET
        face = face.resize((inner, inner), Image.LANCZOS)
        if blocked:                                       # DD1 greys a hero who cannot be picked: DD2's grey portrait, dimmed
            face = face.convert("L").convert("RGBA").point(lambda v: int(v * 0.55))
            face.putalpha(255)
        paste(canvas, face, x + portrait_at[0] + PORTRAIT_INSET, y + portrait_at[1] + PORTRAIT_INSET)
    if in_party:
        put(canvas, town, ROSTER_DIR + "party.icon_roster.png", (x + mark_at[0], y + mark_at[1]))
    elif state == "missing":
        put(canvas, town, ROSTER_DIR + "missing.icon_roster.png", (x + mark_at[0], y + mark_at[1]))
    elif state not in ("idle", None):
        put(canvas, town, "campaign/town/buildings/%s/%s.icon_roster.png" % (state, state), (x + building_at[0], y + building_at[1]))
    elif blocked:                                         # any other reason (the quest on the map is beneath them): a red frame
        put(canvas, town, SLOT_DIR + "hero_slot.negative_frame.png", (x + portrait_at[0], y + portrait_at[1]))

    # name; under it the health bar (DD1's tray colours) and the stress pips
    hx, hy, hw, hh = HEALTH_BAR
    fill(canvas, (x + stress_at[0] + hx, y + stress_at[1] + hy, hw, hh), text.colour("tray_health_bar_background_bottom"))
    top, bottom = text.colour("tray_health_bar_default_current_top"), text.colour("tray_health_bar_default_current_bottom")
    fill(canvas, (x + stress_at[0] + hx, y + stress_at[1] + hy, hw * health, hh), tuple((top[i] + bottom[i]) // 2 for i in range(3)))
    text.draw(canvas, (x + name_at[0], y + name_at[1]), name, "town_roster_name", max_width=badge_at[0] - name_at[0] - 2)
    for i in range(PIPS):
        full = i < stress
        put(canvas, town, "overlays/" + ("stress_pip_full.png" if full else "stress_pip_empty.png"),
            (x + stress_at[0] + i * stress_step[0], y + stress_at[1] + (1 if full else 0) + i * stress_step[1]))
    text.draw(canvas, (x + weapon_at[0], y + weapon_at[1]), str(weapon), "town_roster_number")
    text.draw(canvas, (x + armour_at[0], y + armour_at[1]), str(armour), "town_roster_number")

    # resolve level: the badge with the level in it
    put(canvas, town, RESOLVE_DIR + "resolve_level_bar_number_background_lvl%d.png" % min(level, 6),
        (x + badge_at[0] + badge_back[0], y + badge_at[1] + badge_back[1]))
    number_font = text.font("resolve_number")
    text.draw(canvas, (x + badge_at[0] + badge_number[0], y + badge_at[1] + badge_number[1] - number_font.line_height / 2),
              str(level), "resolve_number", anchor="m")

    # class and path (or why the hero cannot embark), two short lines under the badge
    # Class on one line, path on the next, neither ever broken; a reason wraps. As TMP's auto-sizing does, the
    # whole caption shrinks (down to CAPTION_SMALLEST) until its lines fit the box; what still does not is cut.
    cx, cy, cw, ch = CAPTION
    size = CAPTION_SIZE
    while True:
        lines = (blocked or title).split(", ", 1) if not blocked else text.wrap(blocked, "small", cw, size)
        line_h = text.line_height("small", size)
        fits = len(lines) * line_h <= ch + 1 and all(text.width(line, "small", size) <= cw for line in lines)
        if fits or size <= CAPTION_SMALLEST:
            break
        size -= 0.5
    for i, line in enumerate(lines[:int((ch + 1) // line_h)]):
        text.draw(canvas, (x + cx + cw, y + cy + i * line_h), line, "small",
                  colour="harmful" if blocked else "neutral", anchor="r", size=size, max_width=cw)
    # the dismiss control shows while the pointer rests on the row of a hero who may be sent away
    if hovered and state == "idle":
        put(canvas, town, "shared/character/icon_dismiss.png", (x + DISMISS_POS[0], y + DISMISS_POS[1]))


def draw_narration(canvas, town, text, words):
    app = read_layout(town, "shared", "app.darkest")
    centre = lay(app, "s_TownSubtitleContextTunables", "screen_position", 960, 990)
    width = lay(app, "s_TownSubtitleContextTunables", "max_width", 1280)[0]
    rise = lay(app, "s_SubtitleGlobalTunables", "background_y_offset", 100)[0]
    lines = text.wrap(words, "subtitle_context_town", width)
    line_h = text.line_height("subtitle_context_town")
    height = len(lines) * line_h
    # DD1 centres its one or two lines on the position and starts the band `rise` above that; a longer text
    # keeps its foot on the screen and pushes the band up.
    top = min(centre[1] - height / 2, SCREEN_H - NARRATION_FOOT - height)
    band = art(town, "shared/subtitles/subtitle_bg.png")
    if band is not None:
        band_top = int(round(top - (rise - line_h / 2)))
        band = band.resize((SCREEN_W, max(band.height, SCREEN_H - band_top)))
        for _ in range(NARRATION_LAYERS):
            paste(canvas, band, 0, band_top)
    for i, line in enumerate(lines):
        text.draw(canvas, (centre[0], top + i * line_h), line, "subtitle_context_town", anchor="m")


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--dd1", default=os.environ.get("DD1_ROOT", r"E:\Steam\steamapps\common\DarkestDungeon"))
    ap.add_argument("--out", default=os.path.join(here, "..", "_lab", "preview"))
    ap.add_argument("--level", type=int, default=1)
    ap.add_argument("--locked", default="", help="comma separated building ids drawn with their locked art")
    ap.add_argument("--heroes", type=int, default=8, help="heroes on the roster of hamlet_chrome.png (more than 8 scroll)")
    args = ap.parse_args()

    if not os.path.isfile(os.path.join(args.dd1, "campaign", "town", "town_bg.png")):
        raise SystemExit("Darkest Dungeon (1) not found at %s (use --dd1 or DD1_ROOT)" % args.dd1)
    out = os.path.abspath(args.out)
    os.makedirs(out, exist_ok=True)
    town = Town(args.dd1)
    locked = set(s for s in args.locked.split(",") if s)

    rest, infos = compose(town, args.level, locked, hover=False)
    rest.convert("RGB").save(os.path.join(out, "hamlet.png"))
    text = Dd1Text(args.dd1)
    hovered, infos = compose(town, args.level, locked, hover=True)
    annotate(hovered, town, infos, text)
    hovered.convert("RGB").save(os.path.join(out, "hamlet_hover.png"))

    heroes = SAMPLE_HEROES[:max(1, min(args.heroes, len(SAMPLE_HEROES)))]
    words = "Ruin has come to our family. You remember our venerable house, opulent and imperial, gazing proudly from its stoic perch above the moor."
    # the pointer rests on the first idle hero in view: that row slides out and shows its dismiss control
    first = max(0, len(heroes) - 8)
    hover = next((i for i in range(first, len(heroes)) if heroes[i][8] == "idle" and not heroes[i][9]), None)
    for name, narration in (("hamlet_chrome.png", None), ("hamlet_chrome_narration.png", words)):
        chrome = rest.copy()
        draw_chrome(chrome, town, text, heroes, 12, 5, dict(gold=560, bust=113, portrait=102, deed=41, crest=74),
                    first=first, hover=hover, narration=narration)
        chrome.convert("RGB").save(os.path.join(out, name))

    print("camera %s  ground quad %s" % (town.cam, [tuple(round(v, 1) for v in p) for p in town.ground_quad()]))
    for bid, info in sorted(infos.items(), key=lambda kv: -kv[1]["z"]):
        print("%-16s z %6.0f  root (%7.1f, %6.1f)  %s  bbox %s" % (
            bid, info["z"], info["root"][0], info["root"][1], town.variant(bid, args.level, bid in locked),
            tuple(int(round(v)) for v in info["bbox"])))
    print("wrote %s, hamlet_hover.png, hamlet_chrome.png and hamlet_chrome_narration.png" % os.path.join(out, "hamlet.png"))


if __name__ == "__main__":
    main()
