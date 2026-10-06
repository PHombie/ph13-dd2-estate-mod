#!/usr/bin/env python3
"""Compose a dungeon hallway and its rooms with their props from the player's own Darkest Dungeon (1) install.

SUPERSEDED for placement: this draws the mod's FIRST corridor (wall as a flat card, props moved towards the
camera). The corridor now follows DD1's own geometry: tools/dd1_corridor_reference.py is the reference, and
it imports this file's Spine reader and prop tables, which stay the checked reference for
src/DD2Estate/Dd1/SpineSkeleton.cs (tools/check_spine.py).

Reference implementation of the Spine reader in src/DD2Estate/Dd1/SpineSkeleton.cs (bones, slots, region /
mesh / skinned mesh attachments and the pose of an animation at a time) and of the placement arithmetic in
src/DD2Estate/Core/ExplorationProps.cs (PropPlacement) and src/DD2Estate/Dungeon/CorridorProps.cs under the camera
of CorridorView.cs: change one, change the other.
Nothing from DD1 is copied into the repo; output goes to _lab/ (gitignored).

    python tools/preview_corridor.py [--dd1 <install>] [--out <dir>] [--dungeon crypts|weald|warrens|cove|darkestdungeon]
                                     [--quest-art N] [--lead TILE] [--sheet] [--list]

Writes
    corridor_<dungeon>.png        a hallway: end cap, door, wall tiles, door, end cap, with a fresh curio, an
                                  investigated one, a scouted trap, a sprung trap, the dungeon's obstacle and the
                                  two doors (one walked through, one closed) where CorridorProps puts them
    corridor_room_<dungeon>.png   a room with its treasure, fresh and opened, a room curio, and the secret room
                                  with its stash
    corridor_screen_<dungeon>.png the 1920x1080 screen as the game's camera frames that hallway with the party's
                                  leader entering tile --lead (default 3): far layers, wall, props and party
                                  each at its own depth, the HUD's third left black
    corridor_props_sheet.png      (--sheet) every curio, trap, obstacle and door of the install in each of its
                                  states, to check the skeleton reader against DD1's art
--list prints every prop skeleton with its animations instead of drawing.

Where DD1 puts things (docs/recon/corridor-props.md has the long version):
  * scripts/world.darkest gives each kind of prop its place in a hallway tile: pixels right of the tile's
    centre (curio 135, in a room 75; trap 0; obstacle 75) and how far behind the heroes it stands (curio 75,
    trap 15; the wall itself is 400 behind them).
  * DD1's hallway is a real 3D scene seen by a camera 1400 in front of the heroes and 270 above the floor
    (scripts/camera.darkest; a room's camera: 1240 and 300). So a prop that stands z behind the heroes is drawn
    (1400 + 400) / (1400 + z) times the size of the wall art behind it, and its foot is
    270 * (1 / 1400 - 1 / (1400 + z)) * (1400 + 400) wall pixels above the heroes' feet.
  * The mod's corridor draws the wall tile 1:1 (720 px = 4.4 world units) with the party's feet on its own
    floor line (CorridorView.FloorHeight). A prop is put nearer the mod's camera by as much as DD1 draws it
    larger than the wall, at the height that shows its foot DD1's rise above the heroes' feet: it has DD1's
    size, and slides against the wall as the camera moves, as it does in DD1.
  * The strip picture shows every prop as the camera sees it when centred on it; the screen picture is one
    moment of the game's own framing.
  * The party is a row of grey placeholders: the mod draws DD2's hero models there.
"""
import argparse
import csv
import json
import math
import os
import re
import struct

from PIL import Image, ImageDraw, ImageFont

SCREEN_W, SCREEN_H = 1920, 1080
TILE = 720                           # CorridorView.TilePixels
ROOM_W = 1920
# The mod's corridor camera and party (CorridorView's tunables): keep in step.
TILE_UNITS = 4.4
FLOOR_HEIGHT = 0.42
PARTY_DEPTH = 0.6                    # the party walks this far in front of the wall
CAMERA_FOV = 40.0
CAMERA_LEAD = 1.0
VISIBLE_HEIGHT = TILE_UNITS * 1.5
HERO_SPACING = 1.25
FAR_DEPTH, MID_DEPTH = 3.0, 1.5      # corridor_bg and corridor_mid stand this far behind the wall
# CorridorProps' tunables
SCALE = 1.0
LIFT = 0.0
# FALLBACKS for a DD1 install without the two scripts; the files' own numbers are used when present.
WORLD_DEFAULTS = {"distance_to_interior_background": 400.0, "curio_tile_centre_x_offset": 135.0,
                  "curio_tile_centre_x_offset_room": 75.0, "curio_z_position": 75.0, "trap_z_position": 15.0,
                  "trap_tile_centre_x_offset": 0.0, "obstacle_tile_centre_x_offset": 75.0}
CORRIDOR_CAMERA = (80.0, 270.0, -1400.0)
ROOM_CAMERA = (960.0, 300.0, -1240.0)
SECRET_ROOM = "dungeons/_shared/secretroom.png"          # CorridorProps.SecretRoomWall
BACK = (0, 0, 0, 255)


# ---------------------------------------------------------------- .darkest text

def parse_darkest(text):
    """`name:` blocks holding `.key value value ...` entries -> {block: {key: [values]}}."""
    blocks = {}
    block = key = None
    for raw in text.splitlines():
        line = raw.split("//")[0]
        for tok in re.findall(r'"[^"]*"|\S+', line):
            if tok.startswith('"'):
                if block is not None and key is not None:
                    block[key].append(tok.strip('"'))
            elif tok.endswith(":") and not tok.startswith("."):
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


def read_json(path):
    with open(path, encoding="utf-8-sig") as f:
        text = f.read()
    try:
        return json.loads(text)
    except ValueError:
        return json.loads(re.sub(r",(\s*[\]}])", r"\1", text))


# ---------------------------------------------------------------- libgdx atlas

def parse_atlas(text):
    """-> (page file name, {region: dict(x, y, w, h, rotate, orig_w, orig_h, off_x, off_y)}); one page."""
    page = None
    regions = {}
    cur = None
    for raw in text.splitlines():
        line = raw.strip()
        if not line:
            continue
        if ":" in line:
            if cur is None:
                continue
            k, v = line.split(":", 1)
            vals = [s.strip() for s in v.split(",")]
            k = k.strip()
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
    """Upright image of a region: a `rotate: true` one is stored turned 90 degrees counter-clockwise."""
    if r["rotate"]:
        return page_img.crop((r["x"], r["y"], r["x"] + r["h"], r["y"] + r["w"])).transpose(Image.ROTATE_270)
    return page_img.crop((r["x"], r["y"], r["x"] + r["w"], r["y"] + r["h"]))


# ---------------------------------------------------------------- Spine 2.1 binary skeleton

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
                # 32 bits, as the C# reader's int: a draw order offset may be negative
                return result - (1 << 32) if result >= (1 << 31) else result

    def float(self):
        v = struct.unpack_from(">f", self.b, self.p)[0]
        self.p += 4
        return v

    def colour(self):
        v = tuple(c / 255.0 for c in self.b[self.p:self.p + 4])
        self.p += 4
        return v

    def string(self):
        n = self.varint()            # byte length + 1; 0 = null
        if n == 0:
            return None
        v = self.b[self.p:self.p + n - 1].decode("utf8")
        self.p += n - 1
        return v

    def floats(self):
        n = self.varint()
        v = list(struct.unpack_from(">%df" % n, self.b, self.p))
        self.p += 4 * n
        return v

    def shorts(self):
        n = self.varint()
        v = list(struct.unpack_from(">%dH" % n, self.b, self.p))
        self.p += 2 * n
        return v

    def curve(self):
        kind = self.byte()
        if kind == 2:
            return (self.float(), self.float(), self.float(), self.float())
        return "stepped" if kind == 1 else None


def _read_skin(r, nonessential):
    skin = {}
    for _ in range(r.varint()):
        slot = r.varint()
        for _ in range(r.varint()):
            key = r.string()
            name = r.string() or key
            kind = r.byte()
            if kind == 0:            # region
                att = dict(type="region", path=r.string() or name, x=r.float(), y=r.float(), sx=r.float(), sy=r.float(),
                           rot=r.float(), w=r.float(), h=r.float(), colour=r.colour())
            elif kind == 1:          # bounding box: nothing to draw
                r.floats()
                continue
            elif kind == 2:          # mesh: vertices in the slot's bone
                att = dict(type="mesh", path=r.string() or name, uvs=r.floats(), triangles=r.shorts(), vertices=r.floats(),
                           colour=r.colour())
                r.varint()           # hull length
                if nonessential:
                    for _ in range(r.varint()):
                        r.varint()   # edges
                    r.float(); r.float()
            elif kind == 3:          # skinned mesh: every vertex is a weighted sum over bones
                att = dict(type="skinned", path=r.string() or name, uvs=r.floats(), triangles=r.shorts())
                bones, weights = [], []
                count = r.varint()   # floats that follow
                i = 0
                while i < count:
                    n = int(r.float())
                    bones.append(n)
                    for _ in range(n):
                        bones.append(int(r.float()))
                        weights.extend((r.float(), r.float(), r.float()))
                    i += 1 + 4 * n
                att["bones"], att["weights"], att["colour"] = bones, weights, r.colour()
                r.varint()
                if nonessential:
                    for _ in range(r.varint()):
                        r.varint()
                    r.float(); r.float()
            else:
                raise ValueError("unsupported Spine attachment type %d" % kind)
            skin[(slot, key)] = att
    return skin


def _read_animation(r, skins):
    anim = dict(colour={}, attachment={}, rotate={}, translate={}, scale={}, ffd=[], order=[], duration=0.0, skipped=set())

    def lasts(frames):
        anim["duration"] = max(anim["duration"], frames[-1][0])

    for _ in range(r.varint()):                      # slot timelines
        slot = r.varint()
        for _ in range(r.varint()):
            kind, count = r.byte(), r.varint()
            if kind == 4:
                frames = []
                for f in range(count):
                    t, c = r.float(), r.colour()
                    frames.append((t, c, r.curve() if f < count - 1 else None))
                anim["colour"][slot] = frames
            elif kind == 3:
                frames = [(r.float(), r.string(), "stepped") for _ in range(count)]
                anim["attachment"][slot] = frames
            else:
                raise ValueError("slot timeline %d" % kind)
            lasts(frames)
    for _ in range(r.varint()):                      # bone timelines
        bone = r.varint()
        for _ in range(r.varint()):
            kind, count = r.byte(), r.varint()
            frames = []
            if kind == 1:
                for f in range(count):
                    t, a = r.float(), r.float()
                    frames.append((t, a, r.curve() if f < count - 1 else None))
                anim["rotate"][bone] = frames
            elif kind in (0, 2):
                for f in range(count):
                    t, x, y = r.float(), r.float(), r.float()
                    frames.append((t, (x, y), r.curve() if f < count - 1 else None))
                anim["scale" if kind == 0 else "translate"][bone] = frames
            elif kind in (5, 6):                     # flips: no prop needs them
                frames = [(r.float(), r.boolean(), None) for _ in range(count)]
                anim["skipped"].add("flip")
            else:
                raise ValueError("bone timeline %d" % kind)
            lasts(frames)
    for _ in range(r.varint()):                      # ik timelines: read past (the open grave's ghost)
        r.varint()
        count = r.varint()
        for f in range(count):
            r.float(); r.float(); r.byte()
            if f < count - 1:
                r.curve()
        anim["skipped"].add("ik")
    for _ in range(r.varint()):                      # mesh deformation
        skin = skins[r.varint()]
        for _ in range(r.varint()):
            slot = r.varint()
            for _ in range(r.varint()):
                key = r.string()
                att = skin[(slot, key)]
                size = len(att["vertices"]) if att["type"] == "mesh" else len(att["weights"]) // 3 * 2
                count = r.varint()
                frames = []
                for f in range(count):
                    t = r.float()
                    n = r.varint()
                    deltas = None
                    if n:
                        start = r.varint()
                        deltas = [0.0] * size
                        for v in range(start, start + n):
                            deltas[v] = r.float()
                    frames.append((t, deltas, r.curve() if f < count - 1 else None))
                anim["ffd"].append((slot, key, size, frames))
                lasts(frames)
    for _ in range(r.varint()):                      # draw order
        offsets = [(r.varint(), r.varint()) for _ in range(r.varint())]
        t = r.float()
        anim["order"].append((t, offsets))
        anim["duration"] = max(anim["duration"], t)
    for _ in range(r.varint()):                      # events
        t = r.float()
        r.varint(); r.varint(); r.float()
        if r.boolean():
            r.string()
        anim["duration"] = max(anim["duration"], t)
    return anim


def read_skeleton(data):
    """Everything of a Spine 2.1 .skel that decides what is drawn: bones, slots, the default skin, animations."""
    r = _Reader(data)
    r.string()                       # hash
    skel = dict(version=r.string(), width=r.float(), height=r.float())
    nonessential = r.boolean()
    if nonessential:
        r.string()                   # images path
    bones = []
    for _ in range(r.varint()):
        name = r.string()
        parent = r.varint() - 1
        bone = dict(name=name, parent=parent, x=r.float(), y=r.float(), sx=r.float(), sy=r.float(), rot=r.float())
        r.float()                    # length
        bone["flip_x"], bone["flip_y"] = r.boolean(), r.boolean()
        bone["inherit_scale"], bone["inherit_rot"] = r.boolean(), r.boolean()
        if nonessential:
            r.colour()
        bones.append(bone)
    for _ in range(r.varint()):      # ik constraints
        r.string()
        for _ in range(r.varint()):
            r.varint()
        r.varint(); r.float(); r.byte()
    slots = []
    for _ in range(r.varint()):
        slots.append(dict(name=r.string(), bone=r.varint(), colour=r.colour(), attachment=r.string(), additive=r.boolean()))
    skins = [_read_skin(r, nonessential)]
    for _ in range(r.varint()):
        r.string()
        skins.append(_read_skin(r, nonessential))
    for _ in range(r.varint()):      # events
        r.string(); r.varint(); r.float(); r.string()
    animations = {}
    for _ in range(r.varint()):
        name = r.string()
        animations[name] = _read_animation(r, skins)
    if r.p != len(data):
        raise ValueError("%d bytes of the skeleton were not read" % (len(data) - r.p))
    skel.update(bones=bones, slots=slots, skin=skins[0], animations=animations)
    return skel


# ---- the pose of an animation at a time ----------------------------------------------------------------

BEZIER_SEGMENTS = 10


def _curve_percent(curve, percent):
    """Spine's CurveTimeline.getCurvePercent: linear, stepped, or a bezier walked in ten straight pieces."""
    if curve is None:
        return percent
    if curve == "stepped":
        return 0.0
    cx1, cy1, cx2, cy2 = curve
    px, py = 0.0, 0.0
    for i in range(1, BEZIER_SEGMENTS + 1):
        t = i / BEZIER_SEGMENTS
        u = 1 - t
        x = 3 * u * u * t * cx1 + 3 * u * t * t * cx2 + t * t * t
        y = 3 * u * u * t * cy1 + 3 * u * t * t * cy2 + t * t * t
        if x >= percent:
            return py + (y - py) * (percent - px) / (x - px) if x > px else y
        px, py = x, y
    return 1.0


def _sample(frames, time):
    """-> (value before, value after, share) of a timeline at a time; None before its first key."""
    if time < frames[0][0]:
        return None
    for i in range(len(frames) - 1):
        t0, t1 = frames[i][0], frames[i + 1][0]
        if time < t1:
            return frames[i][1], frames[i + 1][1], _curve_percent(frames[i][2], (time - t0) / (t1 - t0) if t1 > t0 else 0.0)
    return frames[-1][1], frames[-1][1], 0.0


def _mix(a, b, share):
    return a + (b - a) * share


def pose(skel, animation=None, time=0.0, regions=None):
    """What the skeleton draws, back to front: dict(slot, region, vertices [(x, y)], uvs [(u, v)], triangles
    [(a, b, c)], colour (r, g, b, a)). x right and y up from the skeleton's origin; u, v from the top-left
    of the upright atlas region. Slots an animation fades out (alpha 0) are left out."""
    anim = skel["animations"].get(animation) if animation else None
    local = [dict(b) for b in skel["bones"]]
    colours = [s["colour"] for s in skel["slots"]]
    attached = [s["attachment"] for s in skel["slots"]]
    order = list(range(len(skel["slots"])))
    deform = {}
    if anim:
        for bone, frames in anim["rotate"].items():
            s = _sample(frames, time)
            if s:
                turn = (s[1] - s[0] + 180.0) % 360.0 - 180.0
                local[bone]["rot"] = skel["bones"][bone]["rot"] + s[0] + turn * s[2]
        for bone, frames in anim["translate"].items():
            s = _sample(frames, time)
            if s:
                local[bone]["x"] = skel["bones"][bone]["x"] + _mix(s[0][0], s[1][0], s[2])
                local[bone]["y"] = skel["bones"][bone]["y"] + _mix(s[0][1], s[1][1], s[2])
        for bone, frames in anim["scale"].items():
            s = _sample(frames, time)
            if s:
                local[bone]["sx"] = skel["bones"][bone]["sx"] * _mix(s[0][0], s[1][0], s[2])
                local[bone]["sy"] = skel["bones"][bone]["sy"] * _mix(s[0][1], s[1][1], s[2])
        for slot, frames in anim["colour"].items():
            s = _sample(frames, time)
            if s:
                colours[slot] = tuple(_mix(a, b, s[2]) for a, b in zip(s[0], s[1]))
        for slot, frames in anim["attachment"].items():
            s = _sample(frames, time)
            if s:
                attached[slot] = s[0]
        for t, offsets in anim["order"]:
            if t > time:
                break
            order = _draw_order(len(skel["slots"]), offsets)
        for slot, key, size, frames in anim["ffd"]:
            s = _sample(frames, time)
            if s and attached[slot] == key:
                a = s[0] or [0.0] * size
                b = s[1] or [0.0] * size
                deform[(slot, key)] = [_mix(x, y, s[2]) for x, y in zip(a, b)]

    world = []
    for b in local:
        if b["parent"] < 0:
            x, y, rot, sx, sy, fx, fy = b["x"], b["y"], b["rot"], b["sx"], b["sy"], b["flip_x"], b["flip_y"]
        else:
            p = world[b["parent"]]
            x = p["a"] * b["x"] + p["b"] * b["y"] + p["x"]
            y = p["c"] * b["x"] + p["d"] * b["y"] + p["y"]
            sx = p["sx"] * b["sx"] if b["inherit_scale"] else b["sx"]
            sy = p["sy"] * b["sy"] if b["inherit_scale"] else b["sy"]
            rot = p["rot"] + b["rot"] if b["inherit_rot"] else b["rot"]
            fx, fy = p["fx"] != b["flip_x"], p["fy"] != b["flip_y"]
        cos, sin = math.cos(math.radians(rot)), math.sin(math.radians(rot))
        a, bb = (-cos * sx, sin * sy) if fx else (cos * sx, -sin * sy)
        c, d = (-sin * sx, -cos * sy) if fy else (sin * sx, cos * sy)
        world.append(dict(a=a, b=bb, c=c, d=d, x=x, y=y, rot=rot, sx=sx, sy=sy, fx=fx, fy=fy))

    pieces = []
    for index in order:
        slot = skel["slots"][index]
        att = skel["skin"].get((index, attached[index])) if attached[index] else None
        if att is None:
            continue
        colour = tuple(a * b for a, b in zip(colours[index], att["colour"]))
        if colour[3] <= 0.0:
            continue
        bone = world[slot["bone"]]
        if att["type"] == "region":
            vertices, uvs, triangles = _region_quad(att, bone, (regions or {}).get(att["path"]))
        elif att["type"] == "mesh":
            delta = deform.get((index, attached[index]))
            vs = att["vertices"]
            vertices = []
            for i in range(0, len(vs), 2):
                vx, vy = vs[i] + (delta[i] if delta else 0.0), vs[i + 1] + (delta[i + 1] if delta else 0.0)
                vertices.append((vx * bone["a"] + vy * bone["b"] + bone["x"], vx * bone["c"] + vy * bone["d"] + bone["y"]))
            uvs, triangles = _pairs(att["uvs"]), _triples(att["triangles"])
        else:
            delta = deform.get((index, attached[index]))
            vertices = []
            v = b = f = 0
            bones, weights = att["bones"], att["weights"]
            while v < len(bones):
                n = bones[v]
                v += 1
                wx = wy = 0.0
                for _ in range(n):
                    link = world[bones[v]]
                    vx, vy, weight = weights[b], weights[b + 1], weights[b + 2]
                    if delta:
                        vx, vy = vx + delta[f], vy + delta[f + 1]
                    wx += (vx * link["a"] + vy * link["b"] + link["x"]) * weight
                    wy += (vx * link["c"] + vy * link["d"] + link["y"]) * weight
                    v, b, f = v + 1, b + 3, f + 2
                vertices.append((wx, wy))
            uvs, triangles = _pairs(att["uvs"]), _triples(att["triangles"])
        pieces.append(dict(slot=slot["name"], region=att["path"], vertices=vertices, uvs=uvs, triangles=triangles, colour=colour))
    return pieces


def _pairs(flat):
    return [(flat[i], flat[i + 1]) for i in range(0, len(flat), 2)]


def _triples(flat):
    return [(flat[i], flat[i + 1], flat[i + 2]) for i in range(0, len(flat), 3)]


def _draw_order(count, offsets):
    """Spine's draw order key: the listed slots move by their offset, the others keep their order."""
    order = [-1] * count
    unchanged = []
    original = 0
    for slot, offset in offsets:
        while original != slot:
            unchanged.append(original)
            original += 1
        order[original + offset] = original
        original += 1
    while original < count:
        unchanged.append(original)
        original += 1
    for i in range(count - 1, -1, -1):
        if order[i] == -1:
            order[i] = unchanged.pop()
    return order


def _region_quad(att, bone, region):
    """A region attachment as a quad over the packed pixels of its atlas region (RegionAttachment.updateOffset)."""
    w, h = att["w"], att["h"]
    x0, y0, x1, y1 = -w / 2, -h / 2, w / 2, h / 2
    if region is not None:
        x0 += region["off_x"] / region["orig_w"] * w
        y0 += region["off_y"] / region["orig_h"] * h
        x1 -= (region["orig_w"] - region["off_x"] - region["w"]) / region["orig_w"] * w
        y1 -= (region["orig_h"] - region["off_y"] - region["h"]) / region["orig_h"] * h
    cos, sin = math.cos(math.radians(att["rot"])), math.sin(math.radians(att["rot"]))
    vertices = []
    for lx, ly in ((x0, y0), (x0, y1), (x1, y1), (x1, y0)):
        lx, ly = lx * att["sx"], ly * att["sy"]
        ax, ay = lx * cos - ly * sin + att["x"], lx * sin + ly * cos + att["y"]
        vertices.append((ax * bone["a"] + ay * bone["b"] + bone["x"], ax * bone["c"] + ay * bone["d"] + bone["y"]))
    return vertices, [(0.0, 1.0), (0.0, 0.0), (1.0, 0.0), (1.0, 1.0)], [(0, 1, 2), (0, 2, 3)]


def bounds(pieces):
    xs = [v[0] for p in pieces for v in p["vertices"]]
    ys = [v[1] for p in pieces for v in p["vertices"]]
    return (min(xs), min(ys), max(xs), max(ys)) if xs else (0.0, 0.0, 0.0, 0.0)


# ---------------------------------------------------------------- a prop: skeleton + atlas + page

class Prop:
    _cache = {}

    def __init__(self, dd1, skel_rel):
        base = os.path.join(dd1, skel_rel.replace("/", os.sep))[:-len(".skel")]
        with open(base + ".skel", "rb") as f:
            self.skel = read_skeleton(f.read())
        with open(base + ".atlas", encoding="utf-8-sig") as f:
            page, self.regions = parse_atlas(f.read())
        self.page = Image.open(os.path.join(os.path.dirname(base), page)).convert("RGBA")
        self._images = {}
        self.rel = skel_rel

    @classmethod
    def load(cls, dd1, skel_rel):
        key = (dd1, skel_rel)
        if key not in cls._cache:
            cls._cache[key] = cls(dd1, skel_rel) if os.path.isfile(os.path.join(dd1, skel_rel.replace("/", os.sep))) else None
        return cls._cache[key]

    def image(self, region):
        if region not in self._images:
            r = self.regions.get(region)
            self._images[region] = cut_region(self.page, r) if r else None
        return self._images[region]

    def pose(self, animation=None, time=0.0):
        return pose(self.skel, animation, time, self.regions)

    def last(self, animation):
        """The pose an animation ends in (a sprung trap, an opened chest)."""
        anim = self.skel["animations"].get(animation)
        return self.pose(animation, anim["duration"] if anim else 0.0)


def draw_pieces(canvas, prop, pieces, origin, scale=1.0, flip=False):
    """Draws a pose with the skeleton's origin on canvas pixel `origin`; triangle by triangle, as the mod's
    meshes are."""
    ox, oy = origin
    for piece in pieces:
        img = prop.image(piece["region"])
        if img is None:
            continue
        w, h = img.size
        pts = [(ox + (-x if flip else x) * scale, oy - y * scale) for x, y in piece["vertices"]]
        src = [(u * w, v * h) for u, v in piece["uvs"]]
        xs, ys = [p[0] for p in pts], [p[1] for p in pts]
        left, top = int(math.floor(min(xs))) - 1, int(math.floor(min(ys))) - 1
        size = (int(math.ceil(max(xs))) - left + 2, int(math.ceil(max(ys))) - top + 2)
        layer = Image.new("RGBA", size, (0, 0, 0, 0))
        for a, b, c in piece["triangles"]:
            dst = [(pts[i][0] - left, pts[i][1] - top) for i in (a, b, c)]
            coeffs = _affine(dst, [src[i] for i in (a, b, c)])
            if coeffs is None:
                continue
            warped = img.transform(size, Image.AFFINE, coeffs, Image.BILINEAR)
            mask = Image.new("L", size, 0)
            ImageDraw.Draw(mask).polygon(dst, fill=255, outline=255)
            layer.paste(warped, (0, 0), mask)
        r, g, b, a = piece["colour"]
        if (r, g, b, a) != (1.0, 1.0, 1.0, 1.0):
            bands = layer.split()
            layer = Image.merge("RGBA", [bands[i].point(lambda v, k=k: int(v * k)) for i, k in enumerate((r, g, b, a))])
        _composite(canvas, layer, left, top)


def _affine(dst, src):
    """PIL's AFFINE coefficients (output pixel -> input pixel) taking triangle dst onto triangle src."""
    (x0, y0), (x1, y1), (x2, y2) = dst
    det = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0)
    if abs(det) < 1e-9:
        return None
    out = []
    for k in (0, 1):
        s0, s1, s2 = src[0][k], src[1][k], src[2][k]
        a = ((s1 - s0) * (y2 - y0) - (s2 - s0) * (y1 - y0)) / det
        b = ((s2 - s0) * (x1 - x0) - (s1 - s0) * (x2 - x0)) / det
        out.extend((a, b, s0 - a * x0 - b * y0))
    return out


def _composite(canvas, layer, left, top):
    box = (max(0, left), max(0, top), min(canvas.width, left + layer.width), min(canvas.height, top + layer.height))
    if box[0] >= box[2] or box[1] >= box[3]:
        return
    part = layer.crop((box[0] - left, box[1] - top, box[2] - left, box[3] - top))
    canvas.alpha_composite(part, (box[0], box[1]))


# ---------------------------------------------------------------- DD1's numbers and the mod's placement

class Dd1World:
    """scripts/world.darkest and scripts/camera.darkest, as Core/ExplorationProps.cs (PropPlacement) reads them."""

    def __init__(self, dd1):
        self.values = dict(WORLD_DEFAULTS)
        self.corridor_camera, self.room_camera = CORRIDOR_CAMERA, ROOM_CAMERA
        path = os.path.join(dd1, "scripts", "world.darkest")
        if os.path.isfile(path):
            with open(path, encoding="utf-8-sig") as f:
                block = parse_darkest(f.read()).get("world_parameters", {})
            for key in self.values:
                if block.get(key):
                    self.values[key] = float(block[key][0])
        path = os.path.join(dd1, "scripts", "camera.darkest")
        if os.path.isfile(path):
            with open(path, encoding="utf-8-sig") as f:
                blocks = parse_darkest(f.read())
            offset = blocks.get("m_CorridorCameraParameters", {}).get("OffsetFromPartyLeader")
            if offset and len(offset) >= 3:
                self.corridor_camera = tuple(float(v) for v in offset[:3])
            position = blocks.get("m_RoomCameraParameters", {}).get("Position")
            if position and len(position) >= 3:
                self.room_camera = tuple(float(v) for v in position[:3])

    def place(self, kind, room=False):
        """PropPlacement.Place -> (pixels right of the tile's centre, size against the wall art, wall pixels
        its foot shows above the heroes' feet)."""
        v = self.values
        wall = v["distance_to_interior_background"]
        if kind == "curio":
            x, depth = v["curio_tile_centre_x_offset_room" if room else "curio_tile_centre_x_offset"], v["curio_z_position"]
        elif kind == "trap":
            x, depth = v["trap_tile_centre_x_offset"], v["trap_z_position"]
        elif kind == "obstacle":
            # world.darkest gives an obstacle no depth of its own: it stands where the heroes walk
            x, depth = v["obstacle_tile_centre_x_offset"], 0.0
        else:                        # a door hangs in the wall
            x, depth = 0.0, wall
        camera = self.room_camera if room else self.corridor_camera
        height, distance = camera[1], -camera[2]
        size = (distance + wall) / (distance + depth)
        rise = height * (1.0 / distance - 1.0 / (distance + depth)) * (distance + wall)
        return x, size, rise


def camera():
    """CorridorView's camera: wall pixels per world unit, its height over the tile's bottom edge, its distance
    from the wall (the strip is the top two thirds of a screen whose height shows one and a half tiles)."""
    return TILE / TILE_UNITS, TILE_UNITS - VISIBLE_HEIGHT / 2, VISIBLE_HEIGHT / 2 / math.tan(math.radians(CAMERA_FOV / 2))


def foot(world, kind, room, centre):
    """CorridorProps.Stand: a prop's foot in the view's units (x right, y up from the tile's bottom edge, z away
    from the camera with the wall at 0) and the units one pixel of its art takes. `centre` is the tile's (or
    room's) middle in units."""
    right, size, rise = world.place(kind, room)
    ppu, eye, distance = camera()
    # where the party's feet show on the wall; DD1's rise is counted from there
    hero_line = eye + (FLOOR_HEIGHT - eye) * distance / (distance - PARTY_DEPTH)
    shown = hero_line + (rise + LIFT) / ppu
    return (centre + right / ppu, eye + (shown - eye) / size, -distance * (1 - 1 / size)), SCALE / ppu


def project(point, camera_x):
    """What the camera makes of a point of the view: wall pixels right of the camera's centre line, wall
    pixels above the tile's bottom edge, and the pixels a world unit takes at that depth."""
    ppu, eye, distance = camera()
    k = distance / (distance + point[2])
    return (point[0] - camera_x) * k * ppu, (eye + (point[1] - eye) * k) * ppu, k * ppu


# ---------------------------------------------------------------- DD1 prop tables

class Dungeon:
    def __init__(self, dd1, dungeon, quest_art=1):
        self.dd1, self.id, self.quest_art = dd1, dungeon, quest_art
        self.props = {"hall_curios": [], "room_curios": [], "room_treasures": [], "traps": [], "obstacles": [], "secret_room_treasures": []}
        path = os.path.join(dd1, "dungeons", dungeon, dungeon + ".props.darkest")
        if os.path.isfile(path):
            with open(path, encoding="utf-8-sig") as f:
                for line in f:
                    m = re.match(r"\s*(\w+):.*?\.types\s+(.*)", line)
                    if m and m.group(1) in self.props:
                        self.props[m.group(1)].extend(m.group(2).split())
        self.sprites = {}
        path = os.path.join(dd1, "curios", "curio_props.csv")
        if os.path.isfile(path):
            with open(path, encoding="utf-8-sig", newline="") as f:
                for row in list(csv.reader(f))[1:]:
                    if len(row) >= 2 and row[0].strip():
                        self.sprites[row[0].strip()] = row[1].strip()

    def art(self, name):
        """dungeons/<d>/<d>.<name>.png; the Darkest Dungeon keeps its art per quest (Dd1Install.ArtPath)."""
        for folder in ("dungeons/%s/" % self.id, "dungeons/%s/quest_%d/" % (self.id, self.quest_art)):
            path = os.path.join(self.dd1, (folder + "%s.%s.png" % (self.id, name)).replace("/", os.sep))
            if os.path.isfile(path):
                return Image.open(path).convert("RGBA")
        return None

    def walls(self):
        folder = os.path.join(self.dd1, "dungeons", self.id)
        if not os.path.isdir(folder) or not any(n.startswith(self.id + ".corridor_wall.") for n in os.listdir(folder)):
            folder = os.path.join(folder, "quest_%d" % self.quest_art)
        return sorted(n[len(self.id) + 1:-4] for n in os.listdir(folder) if n.startswith(self.id + ".corridor_wall."))

    def rooms(self):
        folder = os.path.join(self.dd1, "dungeons", self.id)
        if not any(n.startswith(self.id + ".room_wall.") for n in os.listdir(folder)):
            folder = os.path.join(folder, "quest_%d" % self.quest_art)
        return sorted(n[len(self.id) + 1:-4] for n in os.listdir(folder) if n.startswith(self.id + ".room_wall."))

    def curio(self, prop_id):
        sprite = self.sprites.get(prop_id, prop_id)
        return Prop.load(self.dd1, "props/shared/curios/%s/%s.skel" % (sprite, sprite))

    def defined(self, table, prop_id):
        """graphics_file of a prop in props/<table>_definitions.json."""
        path = os.path.join(self.dd1, "props", table)
        if os.path.isfile(path):
            for prop in read_json(path).get("props", []):
                if prop.get("name") == prop_id:
                    rel = prop.get("default_data", {}).get("graphics_file")
                    if rel:
                        return Prop.load(self.dd1, rel)
        return None

    def trap(self, prop_id):
        return self.defined("trap_definitions.json", prop_id) or Prop.load(self.dd1, "props/shared/traps/%s/%s.skel" % (prop_id, prop_id))

    def obstacle(self, prop_id):
        return self.defined("obstacle_definitions.json", prop_id) or Prop.load(self.dd1, "props/shared/obstacles/%s/%s.skel" % (prop_id, prop_id))

    def door(self):
        name = "%s_quest_%d_door" % (self.id, self.quest_art) if self.id == "darkestdungeon" else self.id + "_door"
        return self.defined(self.id + "/prop_definitions.json", name) or Prop.load(self.dd1, "props/%s/doors/door0/door0.skel" % self.id)


# ---------------------------------------------------------------- composing

def font(size):
    for name in ("arialbd.ttf", "DejaVuSans-Bold.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            pass
    return ImageFont.load_default()


def paste(canvas, img, left, top, flip=False, scale=1.0):
    if img is None:
        return
    if flip:
        img = img.transpose(Image.FLIP_LEFT_RIGHT)
    if scale != 1.0:
        img = img.resize((max(1, int(round(img.width * scale))), max(1, int(round(img.height * scale)))), Image.LANCZOS)
    _composite(canvas, img, int(round(left)), int(round(top)))


def stand(canvas, world, prop, pieces, kind, centre, bottom, room=False, camera_x=None, middle=None, label=None):
    """Draws a prop where CorridorProps stands it. `centre`: the middle of its tile (or room) in units;
    `bottom`: canvas y of the tile's bottom edge; `camera_x`: where the camera is (default: on the prop);
    `middle`: canvas x of the camera's centre line (default: the camera's own x in wall pixels)."""
    if prop is None:
        return
    point, units = foot(world, kind, room, centre)
    if camera_x is None:
        camera_x = point[0]
    if middle is None:
        middle = camera_x * TILE / TILE_UNITS
    x, y, pixels = project(point, camera_x)
    origin = (middle + x, bottom - y)
    draw_pieces(canvas, prop, pieces, origin, units * pixels)
    if label:
        ImageDraw.Draw(canvas).text((origin[0], bottom + 14), label, font=font(20), fill=(222, 209, 178, 255), anchor="ma")


def party(canvas, lead_x, bottom, camera_x=None, middle=None):
    """Grey placeholders where the DD2 hero models walk (rank 1 leads on the right); lead_x in units."""
    layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)
    if camera_x is None:
        camera_x = lead_x
    if middle is None:
        middle = camera_x * TILE / TILE_UNITS
    for i in range(4):
        x, y, pixels = project((lead_x - i * HERO_SPACING, FLOOR_HEIGHT, -PARTY_DEPTH), camera_x)
        # a DD2 hero stands about two units tall
        draw.rounded_rectangle((middle + x - 0.33 * pixels, bottom - y - 2.0 * pixels, middle + x + 0.33 * pixels, bottom - y), 40,
                               fill=(150, 150, 160, 110), outline=(200, 200, 210, 160))
    canvas.alpha_composite(layer)


def hallway_contents(dungeon):
    """What stands in each tile of the previewed hallway: (kind, prop id, state)."""
    curios = dungeon.props["hall_curios"] or ["crate"]
    trap_id = (dungeon.props["traps"] or ["spikes"])[0]
    obstacle_id = (dungeon.props["obstacles"] or ["rubble"])[0]
    return [("curio", curios[0], "fresh"), ("trap", trap_id, "scouted"), ("curio", curios[min(1, len(curios) - 1)], "investigated"),
            ("obstacle", obstacle_id, "blocking"), ("trap", trap_id, "sprung"), ("curio", curios[min(2, len(curios) - 1)], "party here")]


def content_pose(dungeon, kind, prop_id, state):
    """-> (prop, pieces) of a tile's content in the state ExplorationProps gives it."""
    if kind == "curio":
        prop = dungeon.curio(prop_id)
        if prop is None:
            return None, None
        if state == "investigated":
            return prop, prop.last("investigate")
        return prop, prop.pose("active" if state == "party here" else "idle", 0.0)
    if kind == "trap":
        prop = dungeon.trap(prop_id)
        return (prop, prop.last("sprung") if state == "sprung" else prop.pose("idle", 0.0)) if prop else (None, None)
    prop = dungeon.obstacle(prop_id)
    return (prop, prop.pose("idle", 0.0)) if prop else (None, None)


def compose_hallway(dd1, out_dir, dungeon_id, quest_art=1):
    """The whole hallway as a strip of wall art, every prop seen with the camera on it."""
    world = Dd1World(dd1)
    dungeon = Dungeon(dd1, dungeon_id, quest_art)
    walls = dungeon.walls()
    contents = hallway_contents(dungeon)
    tiles = len(contents) + 2        # with the two door tiles
    margin = 60
    cap = dungeon.art("endhall.01")
    canvas = Image.new("RGBA", ((tiles + 2) * TILE, TILE + margin), BACK)
    # the canvas starts one tile left of the hallway (its end cap): view x = 0 is canvas x = TILE
    shifted = Image.new("RGBA", (tiles * TILE, TILE + margin), (0, 0, 0, 0))
    bottom = TILE

    bg, mid = dungeon.art("corridor_bg"), dungeon.art("corridor_mid")
    for i in range(tiles + 2):
        paste(canvas, bg, i * TILE, 0)
    for i in range(1, tiles + 1):
        paste(canvas, mid, i * TILE, 0)
    paste(canvas, cap, 0, 0)
    paste(canvas, cap, (tiles + 1) * TILE, 0, flip=True)
    door_tile = dungeon.art("corridor_door.basic")
    for i in range(tiles):
        paste(canvas, door_tile if i in (0, tiles - 1) else dungeon.art(walls[(i - 1) % len(walls)]), (i + 1) * TILE, 0)

    door = dungeon.door()
    if door is not None:
        # the door the party came through stands open, the one ahead is closed
        stand(shifted, world, door, door.last("open"), "door", 0.5 * TILE_UNITS, bottom, label="door (walked through)")
        stand(shifted, world, door, door.last("closed"), "door", (tiles - 0.5) * TILE_UNITS, bottom, label="door (closed)")
    for i, (kind, prop_id, state) in enumerate(contents):
        prop, pieces = content_pose(dungeon, kind, prop_id, state)
        stand(shifted, world, prop, pieces, kind, (i + 1.5) * TILE_UNITS, bottom, label="%s: %s (%s)" % (kind, prop_id, state))
    party(shifted, len(contents) * TILE_UNITS + 0.2, bottom)
    canvas.alpha_composite(shifted, (TILE, 0))

    path = os.path.join(out_dir, "corridor_%s.png" % dungeon_id)
    canvas.convert("RGB").save(path)
    return path


def compose_screen(dd1, out_dir, dungeon_id, quest_art=1, lead_tile=3):
    """One 1920x1080 frame as CorridorView's camera shows the hallway, the leader stepping into a tile."""
    world = Dd1World(dd1)
    dungeon = Dungeon(dd1, dungeon_id, quest_art)
    walls = dungeon.walls()
    contents = hallway_contents(dungeon)
    tiles = len(contents) + 2
    ppu, eye, distance = camera()
    length = tiles * TILE_UNITS
    half = VISIBLE_HEIGHT * SCREEN_W / SCREEN_H / 2
    cap = dungeon.art("endhall.01")
    cap_units = TILE_UNITS if cap is not None else 0.0
    lead_x = max(HERO_SPACING * 3 + 0.6, min(length - 0.6, lead_tile * TILE_UNITS + 0.05))
    # CorridorView.SetLeadX: the view runs ahead of the leader and may look past each end as far as the cap goes
    camera_x = max(half - cap_units, min(length - half + cap_units, lead_x + CAMERA_LEAD))
    canvas = Image.new("RGBA", (SCREEN_W, SCREEN_H), BACK)
    middle, bottom = SCREEN_W / 2, TILE          # the tile's bottom edge is the strip's

    def layer(img, x_units, depth, flip=False):
        if img is None:
            return
        x, y, pixels = project((x_units, 0.0, depth), camera_x)
        k = pixels / ppu
        paste(canvas, img, middle + x, bottom - y - img.height * k, flip=flip, scale=k)

    bg, mid = dungeon.art("corridor_bg"), dungeon.art("corridor_mid")
    for i in range(-1, tiles + 1):
        layer(bg, i * TILE_UNITS, FAR_DEPTH)
    for i in range(tiles):
        layer(mid, i * TILE_UNITS, MID_DEPTH)
    door_tile = dungeon.art("corridor_door.basic")
    for i in range(tiles):
        layer(door_tile if i in (0, tiles - 1) else dungeon.art(walls[(i - 1) % len(walls)]), i * TILE_UNITS, 0.0)
    layer(cap, -TILE_UNITS, 0.0)
    layer(cap, length, 0.0, flip=True)

    door = dungeon.door()
    if door is not None:
        stand(canvas, world, door, door.last("open"), "door", 0.5 * TILE_UNITS, bottom, camera_x=camera_x, middle=middle)
        stand(canvas, world, door, door.last("closed"), "door", (tiles - 0.5) * TILE_UNITS, bottom, camera_x=camera_x, middle=middle)
    # far things first, as CorridorProps.SortingOrder has them: curios, traps, obstacles
    for wanted in ("curio", "trap", "obstacle"):
        for i, (kind, prop_id, state) in enumerate(contents):
            if kind != wanted:
                continue
            prop, pieces = content_pose(dungeon, kind, prop_id, state)
            stand(canvas, world, prop, pieces, kind, (i + 1.5) * TILE_UNITS, bottom, camera_x=camera_x, middle=middle)
    party(canvas, lead_x, bottom, camera_x=camera_x, middle=middle)
    # the HUD's third
    ImageDraw.Draw(canvas).rectangle((0, TILE, SCREEN_W, SCREEN_H), fill=(8, 8, 9, 255))
    ImageDraw.Draw(canvas).text((SCREEN_W / 2, (TILE + SCREEN_H) / 2), "the dungeon HUD (leader entering tile %d of %d)" % (lead_tile, tiles - 1),
                                font=font(26), fill=(90, 84, 74, 255), anchor="mm")
    path = os.path.join(out_dir, "corridor_screen_%s.png" % dungeon_id)
    canvas.convert("RGB").save(path)
    return path


def compose_rooms(dd1, out_dir, dungeon_id, quest_art=1):
    world = Dd1World(dd1)
    dungeon = Dungeon(dd1, dungeon_id, quest_art)
    rooms = [r for r in dungeon.rooms() if not r.endswith("entrance")] or dungeon.rooms()
    treasure = (dungeon.props["room_treasures"] or ["heirloom_chest"])[-1]
    curio = (dungeon.props["room_curios"] or ["sarcophagus"])[0]
    stash = (dungeon.props["secret_room_treasures"] or ["secret_stash"])[0]
    secret = os.path.join(dd1, SECRET_ROOM.replace("/", os.sep))
    views = [(dungeon.art(rooms[0]), treasure, "idle", "room treasure: %s (fresh)" % treasure),
             (dungeon.art(rooms[0]), treasure, "investigate", "room treasure: %s (opened)" % treasure),
             (dungeon.art(rooms[min(1, len(rooms) - 1)]), curio, "active", "room curio: %s (the party in the room)" % curio),
             (Image.open(secret).convert("RGBA") if os.path.isfile(secret) else None, stash, "active", "secret room: %s (fresh)" % stash)]
    margin = 60
    canvas = Image.new("RGBA", (ROOM_W * 2, (TILE + margin) * 2), BACK)
    ppu = TILE / TILE_UNITS
    for i, (wall, prop_id, animation, label) in enumerate(views):
        view = Image.new("RGBA", (ROOM_W, TILE + margin), BACK)
        paste(view, wall, 0, 0)
        # a room is one screen wide and the camera stays on its middle
        centre = (wall.width if wall is not None else ROOM_W) / ppu / 2
        prop = dungeon.curio(prop_id)
        if prop is not None:
            stand(view, world, prop, prop.last(animation) if animation == "investigate" else prop.pose(animation, 0.0), "curio", centre, TILE,
                  room=True, camera_x=centre, label=label)
        # CorridorView.MinLeadX: where the leader stands in a room
        party(view, min(HERO_SPACING * 3 + 0.6, centre), TILE, camera_x=centre)
        canvas.alpha_composite(view, ((i % 2) * ROOM_W, (i // 2) * (TILE + margin)))
    path = os.path.join(out_dir, "corridor_room_%s.png" % dungeon_id)
    canvas.convert("RGB").save(path)
    return path


def prop_files(dd1):
    root = os.path.join(dd1, "props")
    found = []
    for folder, _, names in os.walk(root):
        for name in sorted(names):
            if name.endswith(".skel"):
                found.append(os.path.relpath(os.path.join(folder, name), dd1).replace(os.sep, "/"))
    return sorted(found)


def compose_sheet(dd1, out_dir):
    """Every prop in every state the mod shows, each on a 4 px grid with its origin marked (red)."""
    cell_w, cell_h = 760, 620
    files = prop_files(dd1)
    states = []
    for rel in files:
        prop = Prop.load(dd1, rel)
        for name in sorted(prop.skel["animations"]):
            states.append((rel, name))
    columns = 8
    rows = (len(states) + columns - 1) // columns
    scale = 0.5
    sheet = Image.new("RGBA", (int(columns * cell_w * scale), int(rows * cell_h * scale)), (40, 38, 46, 255))
    for i, (rel, name) in enumerate(states):
        prop = Prop.load(dd1, rel)
        cell = Image.new("RGBA", (cell_w, cell_h), (58, 54, 66, 255))
        origin = (cell_w // 2, cell_h - 70)
        try:
            draw_pieces(cell, prop, prop.last(name), origin)
        except Exception as e:           # a prop the reader cannot pose is shown as its error
            ImageDraw.Draw(cell).text((10, 40), repr(e)[:90], font=font(18), fill=(255, 90, 90, 255))
        draw = ImageDraw.Draw(cell)
        draw.line((origin[0] - 12, origin[1], origin[0] + 12, origin[1]), fill=(255, 40, 40, 255), width=2)
        draw.line((origin[0], origin[1] - 12, origin[0], origin[1] + 12), fill=(255, 40, 40, 255), width=2)
        draw.text((10, 8), "%s  [%s]" % (rel.split("/")[-1][:-5] if "doors" not in rel else rel.split("/")[1] + " door", name), font=font(26), fill=(222, 209, 178, 255))
        cell = cell.resize((int(cell_w * scale), int(cell_h * scale)), Image.LANCZOS)
        sheet.alpha_composite(cell, (int((i % columns) * cell_w * scale), int((i // columns) * cell_h * scale)))
    path = os.path.join(out_dir, "corridor_props_sheet.png")
    sheet.convert("RGB").save(path)
    return path


def list_props(dd1):
    for rel in prop_files(dd1):
        prop = Prop.load(dd1, rel)
        skel = prop.skel
        kinds = {}
        for att in skel["skin"].values():
            kinds[att["type"]] = kinds.get(att["type"], 0) + 1
        box = bounds(prop.pose("idle" if "idle" in skel["animations"] else None, 0.0))
        anims = ", ".join("%s %.2fs%s" % (n, a["duration"], " (not read: %s)" % "/".join(sorted(a["skipped"])) if a["skipped"] else "")
                          for n, a in sorted(skel["animations"].items()))
        print("%-58s bones %3d  %-36s x %6.0f..%-5.0f y %5.0f..%-5.0f  %s" % (
            rel[len("props/"):], len(skel["bones"]), " ".join("%s x%d" % kv for kv in sorted(kinds.items())), box[0], box[2], box[1], box[3], anims))


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--dd1", default=os.environ.get("DD1_ROOT", r"E:\Steam\steamapps\common\DarkestDungeon"))
    ap.add_argument("--out", default=os.path.join(here, "..", "_lab", "preview"))
    ap.add_argument("--dungeon", default="crypts")
    ap.add_argument("--quest-art", type=int, default=1, help="the Darkest Dungeon's art folder (quest_1..quest_4)")
    ap.add_argument("--lead", type=int, default=3, help="the tile the leader steps into in the screen picture")
    ap.add_argument("--sheet", action="store_true", help="also write every prop in every state")
    ap.add_argument("--list", action="store_true", help="print every prop skeleton instead of drawing")
    args = ap.parse_args()
    if args.list:
        list_props(args.dd1)
        return
    os.makedirs(args.out, exist_ok=True)
    print(compose_hallway(args.dd1, args.out, args.dungeon, args.quest_art))
    print(compose_rooms(args.dd1, args.out, args.dungeon, args.quest_art))
    print(compose_screen(args.dd1, args.out, args.dungeon, args.quest_art, args.lead))
    if args.sheet:
        print(compose_sheet(args.dd1, args.out))


if __name__ == "__main__":
    main()
