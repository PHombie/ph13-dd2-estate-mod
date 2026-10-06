#!/usr/bin/env python3
"""DD1's dungeon view as DD1 itself draws it, rebuilt offline from the player's own Darkest Dungeon (1) install.

Ground truth for the mod's corridor (src/DD2Estate/Dungeon/Corridor*.cs): put an in-game screenshot next to the
frame this writes. Every number is read from DD1's files; the arithmetic is the one of
docs/recon/dd1-corridor-rendering.md (sections in brackets below). Nothing of DD1 is copied into the repo; the
pictures go to _lab/preview (gitignored).

    python tools/dd1_corridor_reference.py [--dd1 <install>] [--out <dir>] [--dungeon crypts|weald|warrens|cove]
        [--torch 0..100] [--leader X] [--tiles N] [--back 0..1] [--room NAME] [--heroes a,b,c,d]
        [--curio ID] [--trap ID] [--obstacle ID] [--boxes] [--no-post] [--all]

Writes (1920x1080; the dungeon view is the top 720 rows, DD1's panel below is left black)
    dd1_reference_hallway_<dungeon>_t<torch>.png   a hallway: the party a few steps before a curio, a trap in the
                                                   tile after it (DD1's view is too narrow to show a door as well)
    dd1_reference_door_<dungeon>_t<torch>.png      the same hallway with the party 500 before its far door
    dd1_reference_room_<dungeon>_t<torch>.png      a room with its curio
    dd1_reference_back_<dungeon>_t<torch>.png      (--back or --all) the same hallway under the walking-back camera
    dd1_reference_torch_<dungeon>.png              (--all) the hallway at torch 0, 25, 50, 75, 100, quarter size
and prints, per picture, what stands where: world position, pixels per world unit and the box on screen.

How DD1 draws a hallway [spec 2-6]:
  * a 3D scene in pixels-as-units: x along the hallway (tile i is centred on 720 i), y up from the floor, z away
    from the camera; the party walks on z = 0;
  * a perspective camera: horizontal field of view 75 degrees over a 1920x720 view, 1400 in front of the
    leader, 80 ahead of him and 270 above the floor, its picture magnified 1.25 about the view's middle;
  * the wall art's upper 600 rows stand upright at z = 400; its lower 120 rows ARE THE FLOOR: a horizontal
    quad from z = 400 to z = -600 at y = 0;
  * curios and obstacles stand at z = 75, traps at z = 15, doors in the wall, heroes at z = 0, 154 apart;
  * the foreground strips hang at z = -300, in front of the heroes;
  * walls, floor and props are lit by lit_sprite.glsl's ramp over the distance from the camera's axis, heroes
    by character.glsl's box light; the whole view goes through the torch's colour grade and the film grain.
What is inferred rather than read is marked INFERRED here and in the spec.
"""
import argparse
import json
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import preview_corridor as pc      # the Spine 2.1 reader and the DD1 prop tables (checked against the plugin's)

VIEW_W, VIEW_H = 1920, 720          # screen.raid.darkest screen_guide: x_centre 960, panel_top 720
SCREEN_H = 1080
# Not in any file, a constant of DD1's executable [spec 2.3]: hero i stands at party x + 180 - i * actor_spacing,
# so the leader is 180 ahead of the point DD1 calls the party's position (area.room_position is that point).
LEADER_AHEAD = 180.0


# ---------------------------------------------------------------- DD1's numbers

def _floats(block, key, fallback):
    vals = (block or {}).get(key)
    if not vals:
        return list(fallback)
    out = []
    for v in vals:
        try:
            out.append(float(v))
        except ValueError:
            pass
    return out if len(out) >= len(fallback) else list(fallback)


def _colour(block, key, fallback):
    """`.wash 0 0 0 15`, `.gradB #646464`, `.base #ff6a21` -> 0..1 floats (rgba)."""
    vals = (block or {}).get(key)
    if not vals:
        return list(fallback)
    if vals[0].startswith("#"):
        h = vals[0][1:]
        if len(h) == 3:
            h = "".join(c * 2 for c in h)
        out = [int(h[i:i + 2], 16) / 255.0 for i in range(0, len(h), 2)]
    else:
        out = [float(v) / 255.0 for v in vals]
    while len(out) < 4:
        out.append(1.0)
    return out


class Numbers:
    """Everything the view is built from, each with the file it comes from. The second argument of every
    lookup is a FALLBACK for an install without the file; with the file present it is not used."""

    def __init__(self, dd1):
        self.dd1 = dd1

        def darkest(rel):
            path = os.path.join(dd1, rel.replace("/", os.sep))
            if not os.path.isfile(path):
                print("missing", rel, "- fallbacks used")
                return {}
            with open(path, encoding="utf-8-sig", errors="replace") as f:
                return pc.parse_darkest(f.read())

        cam = darkest("scripts/camera.darkest")
        c = cam.get("m_CorridorCameraParameters", {})
        self.offset = _floats(c, "OffsetFromPartyLeader", (80.0, 270.0, -1400.0))
        self.battle_offset = _floats(c, "BattleOffsetFromPartyLeader", (180.0, 280.0, -1240.0))
        self.back_offset = _floats(c, "OffsetFromPartyLeaderWalkingBack", (-50.0, 275.0, -675.0))
        self.transition_time = _floats(c, "TransitionTime", (0.5,))[0]
        self.zoom = _floats(c, "Zoom", (1.25,))[0]
        self.zoom_back = _floats(c, "ZoomWalkingBack", (0.8,))[0]
        c = cam.get("m_CameraParameters", {})
        self.hfov = _floats(c, "RegularHorizontalFOV", (75.0,))[0]
        self.aspect = _floats(c, "AspectRatio", (2.666666,))[0]
        c = cam.get("m_RoomCameraParameters", {})
        self.room_camera = _floats(c, "Position", (960.0, 300.0, -1240.0))

        w = darkest("scripts/world.darkest").get("world_parameters", {})
        self.wall_z = _floats(w, "distance_to_interior_background", (400.0,))[0]
        self.fg_z = _floats(w, "distance_to_interior_foreground", (-300.0,))[0]
        self.fg_top_y = _floats(w, "foreground_top_y_offset", (0.0,))[0]
        self.fg_bottom_y = _floats(w, "foreground_bottom_y_offset", (-10.0,))[0]
        self.mid_z = _floats(w, "distance_to_midground", (1000.0,))[0]
        self.far_z = _floats(w, "distance_to_farbackground", (1500.0,))[0]
        self.curio_x = _floats(w, "curio_tile_centre_x_offset", (135.0,))[0]
        self.curio_x_room = _floats(w, "curio_tile_centre_x_offset_room", (75.0,))[0]
        self.curio_z = _floats(w, "curio_z_position", (75.0,))[0]
        self.trap_x = _floats(w, "trap_tile_centre_x_offset", (0.0,))[0]
        self.trap_z = _floats(w, "trap_z_position", (15.0,))[0]
        self.obstacle_x = _floats(w, "obstacle_tile_centre_x_offset", (75.0,))[0]

        lay = darkest("scripts/layout/screen.raid.darkest")
        a = lay.get("area", {})
        self.tile = _floats(a, "tile_width", (720.0,))[0]
        self.spacing = _floats(a, "actor_spacing", (154.0,))[0]
        self.room_position = _floats(a, "room_position", (600.0,))[0]

        li = darkest("scripts/raid.lighting.darkest")
        sc = li.get("shade_character", {})
        self.wash = _colour(sc, "wash", (0.0, 0.0, 0.0, 15 / 255.0))
        self.torch_min = _floats(sc, "torch_min", (0.4,))[0]
        self.grain = _floats(li.get("grain", {}), "intensity", (0.1,))[0]
        self.flicker_range = _floats(li.get("flicker", {}), "range", (1.0, 1.1))
        lp = li.get("light_parameters", {})
        self.light_offset = _floats(lp, "offset_from_centre", (0.0, -60.0))
        self.falloff_start = _floats(lp, "falloff_start", (450.0, 140.0))
        self.falloff_distance = _floats(lp, "falloff_distance", (600.0, 250.0))

        # colours/base.colours.darkest: one `colour: .id "x" .rgba r g b a` a line
        self.light = {}
        fallback = {"base": (1.0, 1.0, 1.0), "half": (200 / 255.0,) * 3, "edge": (75 / 255.0,) * 3}
        path = os.path.join(dd1, "colours", "base.colours.darkest")
        found = {}
        if os.path.isfile(path):
            with open(path, encoding="utf-8-sig", errors="replace") as f:
                for line in f:
                    b = pc.parse_darkest(line).get("colour", {})
                    if b.get("id") and b["id"][0].startswith("lighting_") and "rgba" in b:
                        found[b["id"][0]] = _colour(b, "rgba", (1, 1, 1, 1))[:3]
        for level in ("none", "full"):
            for stop in ("base", "half", "edge"):
                self.light[level, stop] = np.array(found.get("lighting_%s_%s" % (level, stop), fallback[stop]), dtype=np.float32)

    # the focal length in view pixels: half the view's width over tan(half the horizontal field of view)
    @property
    def focal(self):
        return VIEW_W * 0.5 / math.tan(math.radians(self.hfov) * 0.5)

    def depth_scale(self, z):
        """World units a 1920 px picture takes at depth z to fill the ROOM camera's view exactly [spec 4.2]."""
        return 2.0 * math.tan(math.radians(self.hfov) * 0.5) * (z - self.room_camera[2]) / VIEW_W


class Camera:
    def __init__(self, n, x, y, z, zoom):
        self.x, self.y, self.z, self.zoom, self.f = x, y, z, zoom, n.focal

    @classmethod
    def corridor(cls, n, leader_x, back=0.0):
        """[spec 3.2] back = 0 walking on, 1 after two seconds of walking back (the blend is a cosine ease)."""
        s = back
        x = leader_x + n.offset[0] + (n.back_offset[0] - n.offset[0]) * s
        y = n.offset[1] + (n.back_offset[1] - n.offset[1]) * s
        z = n.offset[2] + (n.back_offset[2] - n.offset[2]) * s
        return cls(n, x, y, z, n.zoom + (n.zoom_back - n.zoom) * s)

    @classmethod
    def room(cls, n):
        return cls(n, n.room_camera[0], n.room_camera[1], n.room_camera[2], 1.0)

    def scale(self, z):
        """View pixels a world unit takes at depth z."""
        return self.zoom * self.f / (z - self.z)

    def project(self, x, y, z):
        k = self.scale(z)
        return VIEW_W * 0.5 + (x - self.x) * k, VIEW_H * 0.5 - (y - self.y) * k


# ---------------------------------------------------------------- the two shaders and the post effects

def torch_light(n, torch):
    """[spec 5.1] Base / Half / Edge: lighting_none_* to lighting_full_* by the torch (0..100). DD1's two sets
    are the same colours, so the torch does not change them."""
    t = np.float32(max(0.0, min(1.0, torch / 100.0)))
    return {stop: n.light["none", stop] * (1 - t) + n.light["full", stop] * t for stop in ("base", "half", "edge")}


def lit_ramp(light, view_x):
    """lit_sprite.glsl: EdgeLight -> HalfLight -> BaseLight over 1 - |x| / 960, x in view space (the camera's
    zoom is part of the view matrix: INFERRED). -> rgb factors, shape view_x.shape + (3,)."""
    d = np.clip(1.0 - np.abs(view_x) / 960.0, 0.0, 1.0)[..., None]
    a = light["edge"] + (light["half"] - light["edge"]) * np.clip(d * 2.0, 0.0, 1.0)
    b = light["half"] + (light["base"] - light["half"]) * np.clip((d - 0.5) * 2.0, 0.0, 1.0)
    return np.where(d < 0.5, a, b).astype(np.float32)


def character_light(n, cam, light, wx, wy, light_scalar=1.0):
    """character.glsl: full inside a box of falloff_start around the light's centre (the camera's axis moved
    by offset_from_centre), down to nothing falloff_distance further out; then the wash."""
    lx = np.clip(np.abs(wx - (cam.x + n.light_offset[0])) - n.falloff_start[0], 0.0, n.falloff_distance[0]) / n.falloff_distance[0]
    ly = np.clip(np.abs(wy - (cam.y + n.light_offset[1])) - n.falloff_start[1], 0.0, n.falloff_distance[1]) / n.falloff_distance[1]
    intensity = np.clip(1.0 - np.sqrt(lx * lx + ly * ly), 0.0, 1.0) * light_scalar
    rgb = light["base"][None, None, :] * intensity[..., None]
    wash = np.array(n.wash[:3], dtype=np.float32)
    return rgb * (1.0 - n.wash[3]) + rgb * wash * n.wash[3]


def load_lut(path):
    """colour_grade_N.png: 16 wide, 256 tall; x is red, y within a block of 16 rows is green, the block is blue."""
    im = np.asarray(Image.open(path).convert("RGB"), dtype=np.float32) / 255.0
    return im.reshape(16, 16, 16, 3)         # [blue][green][red]


def apply_lut(lut, rgb):
    """film.glsl's texture3D(lut, rgb * 15/16 + 1/32): trilinear between the 16 texel centres."""
    c = np.clip(rgb, 0.0, 1.0) * 15.0
    i0 = np.clip(np.floor(c).astype(np.int32), 0, 14)
    f = c - i0
    out = np.zeros_like(rgb)
    for db in (0, 1):
        for dg in (0, 1):
            for dr in (0, 1):
                w = (f[..., 0] if dr else 1 - f[..., 0]) * (f[..., 1] if dg else 1 - f[..., 1]) * (f[..., 2] if db else 1 - f[..., 2])
                out += lut[i0[..., 2] + db, i0[..., 1] + dg, i0[..., 0] + dr] * w[..., None]
    return out


def film(n, dungeon_dir, torch, rgb, grain=True, seed=7):
    """[spec 6] film.glsl: the grade between the two LUTs around the torch level, then the grain."""
    level = min(4, int(torch / 25.0))
    blend = (torch - level * 25.0) / 25.0
    paths = [os.path.join(dungeon_dir, "colour_grade_%d.png" % min(4, level + k)) for k in (0, 1)]
    if all(os.path.isfile(p) for p in paths):
        a, b = apply_lut(load_lut(paths[0]), rgb), apply_lut(load_lut(paths[1]), rgb)
        rgb = a + (b - a) * blend
    else:
        print("no colour_grade_N.png in", dungeon_dir, "- ungraded")
    if grain and n.grain > 0:
        # x = u v Time 100; x = mod(x, 13) mod(x, 123); dx = mod(x, 0.01): white noise in 0..0.01 per pixel
        noise = np.random.default_rng(seed).random(rgb.shape[:2], dtype=np.float32)
        result = rgb + rgb * np.clip(0.1 + noise, 0.0, 1.0)[..., None]
        rgb = rgb + (result - rgb) * n.grain
    return np.clip(rgb, 0.0, 1.0)


# ---------------------------------------------------------------- drawing

def tex(img):
    return np.asarray(img.convert("RGBA"), dtype=np.float32) / 255.0


def sample(t, u, v, wrap_u=False):
    """Bilinear, u and v in 0..1 over the whole picture, clamped at the edges (wrapped in u when asked)."""
    h, w = t.shape[:2]
    x = u * w - 0.5
    y = np.clip(v * h - 0.5, 0.0, h - 1.0)
    x = np.mod(x, w) if wrap_u else np.clip(x, 0.0, w - 1.0)
    x0, y0 = np.floor(x).astype(np.int32), np.floor(y).astype(np.int32)
    fx, fy = (x - x0)[..., None], (y - y0)[..., None]
    x1 = (x0 + 1) % w if wrap_u else np.minimum(x0 + 1, w - 1)
    y1 = np.minimum(y0 + 1, h - 1)
    return (t[y0, x0] * (1 - fx) + t[y0, x1] * fx) * (1 - fy) + (t[y1, x0] * (1 - fx) + t[y1, x1] * fx) * fy


class View:
    """The 1920x720 dungeon view, back to front."""

    def __init__(self, n, cam, light, fade=1.0):
        self.n, self.cam, self.light, self.fade = n, cam, light, fade
        self.rgb = np.zeros((VIEW_H, VIEW_W, 3), dtype=np.float32)
        self.report = []

    def _over(self, top, left, colour, alpha):
        h, w = alpha.shape
        part = self.rgb[top:top + h, left:left + w]
        part *= (1.0 - alpha[..., None])
        part += colour * alpha[..., None]

    def _box(self, x0, y0, x1, y1):
        l, t = max(0, int(math.floor(x0))), max(0, int(math.floor(y0)))
        r, b = min(VIEW_W, int(math.ceil(x1))), min(VIEW_H, int(math.ceil(y1)))
        return (l, t, r, b) if r > l and b > t else None

    def note(self, name, world, z, box):
        self.report.append({"name": name, "world": [round(v, 1) for v in world], "px_per_unit": round(self.cam.scale(z), 4),
                            "screen": [round(v, 1) for v in box]})

    def upright(self, name, t, xa, xb, yb, yt, z, v0=0.0, v1=1.0, mirror=False, lit=True, alpha=1.0, note=True):
        """A picture standing at depth z over the world rectangle (xa..xb, yb..yt), its rows v0..v1."""
        cam = self.cam
        k = cam.scale(z)
        sx0, sy0 = cam.project(xa, yt, z)
        sx1, sy1 = cam.project(xb, yb, z)
        if note:
            self.note(name, (xa, yb, z), z, (sx0, sy0, sx1, sy1))
        box = self._box(sx0, sy0, sx1, sy1)
        if box is None:
            return
        l, top, r, b = box
        px = np.arange(l, r, dtype=np.float32) + 0.5
        py = np.arange(top, b, dtype=np.float32) + 0.5
        wx = cam.x + (px - VIEW_W * 0.5) / k
        wy = cam.y - (py - VIEW_H * 0.5) / k
        u = (wx - xa) / (xb - xa)
        v = v0 + (yt - wy) / (yt - yb) * (v1 - v0)
        inside = ((u >= 0) & (u <= 1))[None, :] & ((v >= min(v0, v1)) & (v <= max(v0, v1)))[:, None]
        if mirror:
            u = 1.0 - u
        uu, vv = np.meshgrid(u, v)
        c = sample(t, uu, vv)
        rgb = c[..., :3]
        if lit:
            rgb = rgb * lit_ramp(self.light, (wx - cam.x) * cam.zoom)[None, :, :] * self.fade
        self._over(top, l, rgb, c[..., 3] * inside * alpha)

    def floor(self, name, t, xa, xb, z_far, z_near, v0, v1):
        """The floor: rows v0..v1 of the wall art laid flat on y = 0 from z_far (v0) to z_near (v1) [spec 4.1]."""
        cam = self.cam
        horizon = VIEW_H * 0.5
        # the row of the far edge; everything below it down to the view's bottom is floor
        _, sy_far = cam.project(0.0, 0.0, z_far)
        top = max(0, int(math.floor(sy_far)))
        if top >= VIEW_H:
            return
        py = np.arange(top, VIEW_H, dtype=np.float32) + 0.5
        depth = cam.y * cam.zoom * cam.f / np.maximum(py - horizon, 1e-3)       # distance from the camera
        wz = cam.z + depth
        px = np.arange(0, VIEW_W, dtype=np.float32) + 0.5
        wx = cam.x + (px[None, :] - VIEW_W * 0.5) * depth[:, None] / (cam.zoom * cam.f)
        u = (wx - xa) / (xb - xa)
        v = v0 + (z_far - wz) / (z_far - z_near) * (v1 - v0)
        inside = (u >= 0) & (u <= 1) & ((wz <= z_far) & (wz >= z_near))[:, None]
        c = sample(t, u, np.broadcast_to(v[:, None], u.shape))
        rgb = c[..., :3] * lit_ramp(self.light, (wx - cam.x) * cam.zoom) * self.fade
        self._over(top, 0, rgb, c[..., 3] * inside)
        self.note(name, (xa, 0.0, z_far), z_far, (cam.project(xa, 0, z_far)[0], sy_far, cam.project(xb, 0, z_far)[0], VIEW_H))

    def flat(self, name, t, scroll_px):
        """The far background and the midground: the picture at the view's own size, repeated, moved sideways
        by scroll_px [spec 4.4: INFERRED]."""
        px = np.arange(0, VIEW_W, dtype=np.float32) + 0.5
        py = np.arange(0, VIEW_H, dtype=np.float32) + 0.5
        size = float(VIEW_H)                         # a 720 px tile over the view's 720 rows
        uu, vv = np.meshgrid((px + scroll_px) / size, py / VIEW_H)
        c = sample(t, np.mod(uu, 1.0), vv, wrap_u=True)
        wx_view = (px - VIEW_W * 0.5)                # the ramp runs over the view's pixels here
        rgb = c[..., :3] * lit_ramp(self.light, wx_view)[None, :, :] * self.fade
        self._over(0, 0, rgb, c[..., 3])

    def sprite(self, name, obj, pieces, x, y, z, hero=False, flip=False, light_scalar=1.0):
        """A Spine pose standing with its origin at (x, y, z), one world unit to a skeleton pixel [spec 4.5]."""
        cam = self.cam
        k = cam.scale(z)
        bx0, by0, bx1, by1 = pc.bounds(pieces)
        if flip:
            bx0, bx1 = -bx1, -bx0
        sx, sy = cam.project(x, y, z)
        left, top = int(math.floor(sx + bx0 * k)) - 2, int(math.floor(sy - by1 * k)) - 2
        w, h = int(math.ceil((bx1 - bx0) * k)) + 5, int(math.ceil((by1 - by0) * k)) + 5
        canvas = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        pc.draw_pieces(canvas, obj, pieces, (sx - left, sy - top), scale=k, flip=flip)
        self.note(name, (x, y, z), z, (sx + bx0 * k, sy - by1 * k, sx + bx1 * k, sy - by0 * k))
        box = self._box(left, top, left + w, top + h)
        if box is None:
            return
        l, t, r, b = box
        c = tex(canvas)[t - top:b - top, l - left:r - left]
        px = np.arange(l, r, dtype=np.float32) + 0.5
        py = np.arange(t, b, dtype=np.float32) + 0.5
        wx = cam.x + (px - VIEW_W * 0.5) / k
        wy = cam.y - (py - VIEW_H * 0.5) / k
        if hero:
            wxx, wyy = np.meshgrid(wx, wy)
            rgb = c[..., :3] * character_light(self.n, cam, self.light, wxx, wyy, light_scalar) * self.fade
        else:
            rgb = c[..., :3] * lit_ramp(self.light, (wx - cam.x) * cam.zoom)[None, :, :] * self.fade
        self._over(t, l, rgb, c[..., 3])


class Hero:
    """heroes/<class>/anim/<class>.sprite.<animation>.skel + .atlas; the page is the palette's:
    heroes/<class>/<class>_A/anim/<class>.sprite.<animation>.png."""
    _cache = {}

    def __init__(self, dd1, cls_id, animation):
        base = os.path.join(dd1, "heroes", cls_id, "anim", "%s.sprite.%s" % (cls_id, animation))
        with open(base + ".skel", "rb") as f:
            self.skel = pc.read_skeleton(f.read())
        with open(base + ".atlas", encoding="utf-8-sig") as f:
            page, self.regions = pc.parse_atlas(f.read())
        self.page = Image.open(os.path.join(dd1, "heroes", cls_id, cls_id + "_A", "anim", page)).convert("RGBA")
        self._images = {}
        self.animation = animation if animation in self.skel["animations"] else None

    @classmethod
    def load(cls, dd1, cls_id, animation="idle"):
        key = (dd1, cls_id, animation)
        if key not in cls._cache:
            try:
                cls._cache[key] = cls(dd1, cls_id, animation)
            except OSError:
                cls._cache[key] = None
        return cls._cache[key]

    def image(self, region):
        if region not in self._images:
            r = self.regions.get(region)
            self._images[region] = pc.cut_region(self.page, r) if r else None
        return self._images[region]

    def pose(self, time=0.0):
        return pc.pose(self.skel, self.animation, time, self.regions)


# ---------------------------------------------------------------- the two scenes

def hallway(n, dd1, dungeon_id, torch, leader_x=None, tiles=6, back=0.0, heroes=(), curio=None, trap=None, obstacle=None,
            fade=1.0, walk=False):
    """A hallway of `tiles` wall tiles between two door tiles; tile i is centred on x = 720 i [spec 2]."""
    d = pc.Dungeon(dd1, dungeon_id)
    walls = [w for w in d.walls() if not w.endswith(".00")] or d.walls()
    names = ["corridor_door.basic"] + [walls[i % len(walls)] for i in range(tiles)] + ["corridor_door.basic"]
    last = len(names) - 1
    if leader_x is None:
        leader_x = (last - 2) * n.tile + n.curio_x - 300.0      # a few steps before the curio, the trap beyond it
    elif leader_x < 0:
        leader_x = last * n.tile + leader_x                     # counted back from the far door
    cam = Camera.corridor(n, leader_x, back)
    view = View(n, cam, torch_light(n, torch), fade)
    T = n.tile

    # far background and midground. DD1 scrolls their texture by camera x times 0.5 and 0.25 [spec 4.4]
    for name, art, follow in (("far background", "corridor_bg", 0.5), ("midground", "corridor_mid", 0.25)):
        img = d.art(art)
        if img is not None:
            view.flat(name, tex(img), cam.x * follow)

    # walls and floor: the art's upper 600 rows upright at the wall's depth, the lower 120 flat [spec 4.1]
    split = 600.0 / 720.0
    def wall(name, art, centre, mirror=False):
        img = d.art(art)
        if img is None:
            return
        t = tex(img)
        if mirror:
            t = t[:, ::-1]
        view.upright(name, t, centre - T / 2, centre + T / 2, 0.0, 600.0, n.wall_z, 0.0, split)
        view.floor(name + " floor", t, centre - T / 2, centre + T / 2, n.wall_z, -600.0, split, 1.0)
    wall("end cap", "endhall.01", -T)                                   # INFERRED: drawn like a wall tile
    wall("end cap (mirrored)", "endhall.01", (last + 1) * T, mirror=True)
    for i, name in enumerate(names):
        wall("tile %d %s" % (i, name), name, i * T)

    # what stands in the hallway, far things first [spec 4.5]
    door = d.door()
    if door is not None:
        view.sprite("door (behind)", door, door.last("open"), 0.0, 0.0, n.wall_z)
        view.sprite("door (ahead)", door, door.pose("closed", 0.0), last * T, 0.0, n.wall_z)
    curio_tile, trap_tile, obstacle_tile = last - 2, last - 1, None
    if curio:
        prop = d.curio(curio)
        if prop is not None:
            view.sprite("curio " + curio, prop, prop.pose("idle", 0.0), curio_tile * T + n.curio_x, 0.0, n.curio_z)
    if obstacle:
        prop = d.obstacle(obstacle)
        if prop is not None:
            view.sprite("obstacle " + obstacle, prop, prop.pose("idle", 0.0), (last - 1) * T + n.obstacle_x, 0.0, n.curio_z)
    if trap:
        prop = d.trap(trap)
        if prop is not None:
            view.sprite("trap " + trap, prop, prop.pose("idle", 0.0), trap_tile * T + n.trap_x, 0.0, n.trap_z)

    # the party: the leader at leader_x, the others actor_spacing apart behind him; rank 4 first so rank 1 is on top
    for rank in reversed(range(len(heroes))):
        hero = Hero.load(dd1, heroes[rank], "walk" if walk else "idle")
        if hero is not None:
            view.sprite("hero %d %s" % (rank + 1, heroes[rank]), hero, hero.pose(0.0), leader_x - rank * n.spacing, 0.0, 0.0, hero=True)

    # foreground strips: 720 wide like the tile, in front of the party [spec 4.3]
    top, bottom = d.art("foreground_top.01"), d.art("foreground_bottom.01")
    for i in range(-1, last + 2):
        if top is not None:
            h = top.height * T / top.width
            y = 600.0 + n.fg_top_y
            view.upright("foreground top", tex(top), i * T - T / 2, i * T + T / 2, y - h, y, n.fg_z, lit=False, note=(i == 0))
        if bottom is not None:
            h = bottom.height * T / bottom.width
            view.upright("foreground bottom", tex(bottom), i * T - T / 2, i * T + T / 2, n.fg_bottom_y, n.fg_bottom_y + h, n.fg_z,
                         lit=False, note=(i == 0))
    return view, os.path.join(dd1, "dungeons", dungeon_id)


def room(n, dd1, dungeon_id, torch, wall_name=None, heroes=(), curio=None, fade=1.0):
    """A room: one 1920 px picture under the fixed room camera [spec 3.3]."""
    d = pc.Dungeon(dd1, dungeon_id)
    rooms = d.rooms()
    wall_name = wall_name or (rooms[len(rooms) // 2] if rooms else "room_wall.empty")
    if not wall_name.startswith("room_wall"):
        wall_name = "room_wall." + wall_name
    cam = Camera.room(n)
    view = View(n, cam, torch_light(n, torch), fade)
    img = d.art(wall_name)
    centre = n.room_camera[0]
    if img is not None:
        t = tex(img)
        s = n.depth_scale(n.wall_z)                     # the picture is drawn larger by as much as it is further off
        width = VIEW_W * s
        split = 600.0 / 720.0
        view.upright("room " + wall_name, t, centre - width / 2, centre + width / 2, 0.0, 600.0 * width / img.width, n.wall_z, 0.0, split)
        view.floor("room floor", t, centre - width / 2, centre + width / 2, n.wall_z, -600.0, split, 1.0)
    if curio:
        prop = d.curio(curio)
        if prop is not None:
            view.sprite("curio " + curio, prop, prop.pose("idle", 0.0), centre + n.curio_x_room, 0.0, n.curio_z)
    for rank in reversed(range(len(heroes))):
        hero = Hero.load(dd1, heroes[rank], "idle")
        if hero is not None:
            view.sprite("hero %d %s" % (rank + 1, heroes[rank]), hero, hero.pose(0.0), n.room_position + LEADER_AHEAD - rank * n.spacing,
                        0.0, 0.0, hero=True)
    return view, os.path.join(dd1, "dungeons", dungeon_id)


def finish(n, view, dungeon_dir, torch, post=True, boxes=False, caption=None):
    rgb = film(n, dungeon_dir, torch, view.rgb) if post else np.clip(view.rgb, 0.0, 1.0)
    frame = Image.new("RGB", (VIEW_W, SCREEN_H), (0, 0, 0))
    frame.paste(Image.fromarray((rgb * 255.0 + 0.5).astype(np.uint8)), (0, 0))
    draw = ImageDraw.Draw(frame)
    if boxes:
        for item in view.report:
            if item["name"].startswith(("hero", "curio", "trap", "obstacle", "door")):
                draw.rectangle(item["screen"], outline=(255, 220, 0))
        draw.line([(0, VIEW_H // 2), (VIEW_W, VIEW_H // 2)], fill=(0, 200, 255))     # the camera's height
    if caption:
        draw.text((24, VIEW_H + 20), caption, fill=(170, 170, 160), font=pc.font(22))
    return frame


def describe(title, view, cam):
    print("\n" + title)
    print("  camera x %.1f y %.1f z %.1f zoom %.3f: %.4f px per unit on the party's line" % (cam.x, cam.y, cam.z, cam.zoom, cam.scale(0.0)))
    seen = set()
    for item in view.report:
        if item["name"] in seen or item["name"].startswith("tile") and "floor" in item["name"]:
            continue
        seen.add(item["name"])
        s = item["screen"]
        print("  %-34s world %-24s %.4f px/unit  screen x %7.1f..%7.1f  y %6.1f..%6.1f  (%.0f x %.0f)" % (
            item["name"], item["world"], item["px_per_unit"], s[0], s[2], s[1], s[3], s[2] - s[0], s[3] - s[1]))


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--dd1", default=os.environ.get("DD1_ROOT", r"E:\Steam\steamapps\common\DarkestDungeon"))
    ap.add_argument("--out", default=os.path.join(here, "..", "_lab", "preview"))
    ap.add_argument("--dungeon", default="crypts")
    ap.add_argument("--torch", type=float, default=100.0)
    ap.add_argument("--leader", type=float, default=None, help="the leader's x in the hallway (tile i is centred on 720 i; negative: back from the far door)")
    ap.add_argument("--tiles", type=int, default=5, help="wall tiles between the two doors")
    ap.add_argument("--back", type=float, default=0.0, help="walking-back camera, 0..1")
    ap.add_argument("--room", default=None, help="a room wall of the dungeon (library, altar...)")
    ap.add_argument("--heroes", default="crusader,highwayman,plague_doctor,vestal")
    ap.add_argument("--curio", default=None)
    ap.add_argument("--trap", default=None)
    ap.add_argument("--obstacle", default=None)
    ap.add_argument("--boxes", action="store_true", help="outline heroes and props, and mark the camera's height")
    ap.add_argument("--no-post", action="store_true", help="no colour grade and no grain")
    ap.add_argument("--all", action="store_true", help="also the walking-back camera and the torch levels")
    ap.add_argument("--json", action="store_true", help="print the boxes as JSON")
    args = ap.parse_args()

    n = Numbers(args.dd1)
    os.makedirs(args.out, exist_ok=True)
    d = pc.Dungeon(args.dd1, args.dungeon)
    heroes = [h for h in args.heroes.split(",") if h]
    curio = args.curio or (d.props["hall_curios"] or ["crate"])[0]
    trap = args.trap or (d.props["traps"] or ["spikes"])[0]
    room_curio = args.curio or (d.props["room_treasures"] or d.props["room_curios"] or ["crate"])[0]
    tag = "%s_t%d" % (args.dungeon, round(args.torch))
    post = not args.no_post
    out = {}

    view, folder = hallway(n, args.dd1, args.dungeon, args.torch, args.leader, args.tiles, args.back, heroes, curio, trap, args.obstacle)
    name = "dd1_reference_%s_%s.png" % ("back" if args.back > 0 else "hallway", tag)
    finish(n, view, folder, args.torch, post, args.boxes, "DD1 reference: %s hallway, torch %d, camera %s" % (
        args.dungeon, args.torch, "walking back %.2f" % args.back if args.back > 0 else "walking on")).save(os.path.join(args.out, name))
    describe(name, view, view.cam)
    out[name] = view.report

    # the party before the far door, as it stands when the player sends it through
    view, folder = hallway(n, args.dd1, args.dungeon, args.torch, -500.0, args.tiles, 0.0, heroes)
    name = "dd1_reference_door_%s.png" % tag
    finish(n, view, folder, args.torch, post, args.boxes, "DD1 reference: %s hallway, the party 500 before the far door, torch %d" % (
        args.dungeon, args.torch)).save(os.path.join(args.out, name))
    describe(name, view, view.cam)
    out[name] = view.report

    view, folder = room(n, args.dd1, args.dungeon, args.torch, args.room, heroes, room_curio)
    name = "dd1_reference_room_%s.png" % tag
    finish(n, view, folder, args.torch, post, args.boxes, "DD1 reference: %s room, torch %d" % (args.dungeon, args.torch)).save(os.path.join(args.out, name))
    describe(name, view, view.cam)
    out[name] = view.report

    if args.all:
        view, folder = hallway(n, args.dd1, args.dungeon, args.torch, args.leader, args.tiles, 1.0, heroes, curio, trap, args.obstacle, walk=True)
        name = "dd1_reference_back_%s.png" % tag
        finish(n, view, folder, args.torch, post, args.boxes, "DD1 reference: %s hallway, walking back" % args.dungeon).save(os.path.join(args.out, name))
        describe(name, view, view.cam)
        out[name] = view.report
        levels = (0, 25, 50, 75, 100)
        sheet = Image.new("RGB", (VIEW_W // 2, (VIEW_H // 2) * len(levels)), (0, 0, 0))
        for row, level in enumerate(levels):
            view, folder = hallway(n, args.dd1, args.dungeon, level, args.leader, args.tiles, 0.0, heroes, curio, trap, args.obstacle)
            frame = finish(n, view, folder, level, post).crop((0, 0, VIEW_W, VIEW_H)).resize((VIEW_W // 2, VIEW_H // 2), Image.LANCZOS)
            ImageDraw.Draw(frame).text((12, 8), "torch %d" % level, fill=(255, 255, 255), font=pc.font(20))
            sheet.paste(frame, (0, row * (VIEW_H // 2)))
        sheet.save(os.path.join(args.out, "dd1_reference_torch_%s.png" % args.dungeon))
    if args.json:
        print(json.dumps(out, indent=1))
    print("\nwrote to", os.path.abspath(args.out))


if __name__ == "__main__":
    main()
