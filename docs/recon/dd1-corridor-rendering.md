# How Darkest Dungeon 1 draws a hallway and a room: the numbers

Status: complete in five stages. The specification (1); the scene, camera, sizes and prop placement (2), the
light, colour grade, grain and fades (3) and the door sequence with the walk's stand-ins (4), all seen in game;
the timescripts, the steady cam and the housekeeping (5), not yet seen in game. Section 13 describes the mod's
corridor stage by stage, section 15 what still differs from DD1. `tools/dd1_corridor_reference.py` renders the
specification (`_lab/preview/dd1_reference_*.png`). Section 11 describes the mod BEFORE stage 2.

Paths are relative to the DD1 install. Three kinds of source are told apart throughout:

* **[file]** a number or a formula in a file DD1 ships as text (`.darkest`, `.json`, `.glsl`, `.times`);
* **[exe]** read out of DD1's own executable, `_windows/win32/Darkest.exe` (16 999 936 bytes, the build of this
  install), by disassembling it in place (capstone; nothing copied, nothing run). The address of the function is
  given so a reading can be checked. Its source file names itself in the binary:
  `source_code\game\ui.raid\panel.dungeonview.cpp`;
* **[inferred]** my reading where neither says it outright. Section 12 lists every one.

The picture test of the whole: the reference renderer, built from these numbers alone, puts DD1's door skeleton
exactly into the doorway painted on `corridor_door.basic.png`, stands the heroes on the painted floor and hides
the wall's upper edge behind the foreground strip (`dd1_reference_door_crypts_t100.png`).

## 1. Where each thing is defined

| What | Where |
|---|---|
| Camera: offsets, zoom, field of view, room camera | `scripts/camera.darkest` [file]; how they combine: [exe] `0x9772a0` (update), `0x9778f0` (corridor), `0x974000` (projection) |
| Depths of the layers, prop offsets | `scripts/world.darkest` [file]; how they are used: [exe] `0xabe140` (walls), `0xabf7d0` (one wall tile), `0xabbe20` (foreground), `0xabfd70` / `0xac0490` (far layers), `0x952100` (props), `0x9524f0` (doors) |
| Tile width, hero spacing, where the party stands in a room, fades, door transition | `scripts/layout/screen.raid.darkest` blocks `area`, `fade_controls`, `door_transition`, `screen_guide` [file]; use: [exe] `0x9aec90` (party layout), `0xaf3630` (fade out, new area), `0xae9c20` (fade in), `0xb06100` (walking into a door) |
| Light colours per torch level | `colours/base.colours.darkest`: `lighting_none_base/half/edge`, `lighting_full_base/half/edge` [file]; the blend: [exe] `0xab7cd0` |
| Character light, wash, flicker, grain | `scripts/raid.lighting.darkest` [file]; uniforms set in [exe] `0xab7cd0`, per hero in `0xab42f0` |
| Shader math | `shaders/lit_sprite.glsl`, `character.glsl`, `film.glsl`, `composite.glsl`, `blur_*.glsl` [file] |
| Colour grade per torch level | `dungeons/<d>/colour_grade_0..4.png` [file]; which two and how blended: [exe] `0xb04680` |
| Walking | `shared/rules.json`: `m_ForwardAcceleration` ... [file]; walk animation rate: [exe] `0xab42f0` |
| What happens on screen at a curio, a trap | `scripts/timescript/*.times` [file] |

## 2. Coordinate system and units

2.1 **One world unit is one pixel of the art.** x runs along the hallway, y up from the floor (the floor is
y = 0), z away from the camera. The party walks on **z = 0**. [exe: every placement below]

2.2 **A hallway**: tile i is centred on x = 720 i and is 720 wide (`area.tile_width 720`); the tile the party is
in is `floor((party x + 360) / 720)` [exe `0xb06100`, `0xae9c20`: `(x + 360) * (1/720)`]. Tile 0 and the last tile
are the door tiles. **A room** is 1920 wide, x = 0..1920, its middle at 960.

2.3 **The party.** DD1 keeps one "party position" x; hero i (0 = leader) stands at
`party x + 180 - i * actor_spacing` (`area.actor_spacing 154`; the 180 is a constant of the executable) [exe
`0x9aec90`]. So the leader is 180 ahead of the party position and the four heroes are 154 apart, feet at
`area.actor_bottom 0`. Heroes face right. The tile of 2.2 is taken from the party position, i.e. from a point
180 behind the leader.

2.4 **The screen** is 1920x1080: the dungeon view is the top 1920x720 (`screen_guide .panel_top 720`,
`.x_centre 960`), the panel fills the 360 rows below. `m_CameraParameters.AspectRatio 2.666666` is 1920/720.

## 3. Camera

3.1 **Projection** [exe `0x974000`, `0x977700`]: a perspective camera looking straight down +z (no pitch), with a
**horizontal field of view of `RegularHorizontalFOV 75` degrees** over the 1920-wide view: projection x scale
`1 / tan(37.5 deg)`, y scale `aspect / tan(37.5 deg)`. In pixels: focal length
`f = 960 / tan(37.5 deg) = 1251.10`, so a thing at distance D from the camera is drawn `f / D` pixels per unit
(vertical field of view 32.1 degrees). `MaxHorizontalFOV 90` is only a cap.

3.2 **Hallway camera** [exe `0x9778f0`], from `m_CorridorCameraParameters`:

* position = leader + `OffsetFromPartyLeader 80 270 -1400`: 80 ahead of the leader, 270 above the floor, 1400 in
  front of the party's line ([inferred] that the offset is from the leader and not from the party position);
* the picture is then magnified by `Zoom 1.25` about the view's middle (a scale (zoom, zoom, 1) in the view
  transform, added to the timescripts' `camera.zoom`, which rests at 1) [exe `0x9776c8`];
* **walking back**: a counter rises by dt while the party moves backwards and falls by 1.5 dt otherwise, clamped
  to 0..2 s; `s = 0.5 + 0.5 cos((2 - counter) * pi / 2)` (0 walking on, 1 after two seconds of backing). The
  offset goes to `OffsetFromPartyLeaderWalkingBack -50 275 -675` and the zoom to `ZoomWalkingBack 0.8`, each
  linearly in s;
* **fight in the hallway**: offset to `BattleOffsetFromPartyLeader 180 280 -1240`, zoom to 1.0;
* every change of target (offset x, y, distance, zoom) is a tween of `TransitionTime 0.5` s.

3.3 **Room camera** [exe `0x9772a0`], from `m_RoomCameraParameters`: fixed at `Position 960 300 -1240`, zoom 1
(`ZoomedOutPosition 960 365 -1640` for large monsters, blended over 0.5 s; `CampPosition 960 220 -940`).

3.4 **Pixels per world unit** (`zoom * f / (z - camera z)`) and the screen row (0 = top of the view) of the
floor at that depth:

| Camera | z = -300 (foreground) | z = 0 (heroes) | z = 15 (trap) | z = 75 (curio) | z = 400 (wall) |
|---|---|---|---|---|---|
| hallway, walking on | 1.4217 | **1.1171**, row 661.6 | 1.1052, row 658.4 | 1.0603, row 646.3 | 0.8688, row 594.6 |
| hallway, walked back 2 s | 2.6690 | 1.4828, row 767.8 (feet below the view) | 1.4505 | 1.3345 | 0.9310, row 616.0 |
| hallway fight | 1.3310 | 1.0089, row 642.5 | 0.9969 | 0.9514 | 0.7629, row 573.6 |
| room | 1.3310 | 1.0089, row 662.7 | 0.9969, row 659.1 | 0.9514, row 645.4 | 0.7629, row 588.9 |

The view's middle row (360) is the camera's height: the horizon.

3.5 **Steady cam** [exe `0x977be0`, every frame while the camera's steady flag is set; `m_CameraParameters`]:
a point near the camera's aim wanders, and the camera turns after it. With `dark = (1 - torch/100)^2` and
`move = min(1, speed / top speed)^2` (walking backwards counts double):

```
timer -= dt
if timer < 0:
    timer  = TimeRange.x + move * TimeRange.y + dark * TimeRange.z              (2.0 - 0.5 move - 1.0 dark  s)
    d      = a random direction (z = 2 rand - 1, the rest on the ring around it), normalised
    target = (d.x * MaxDistance.x, -|d.y * MaxDistance.y|, |d.z * MaxDistance.z|)   (15, 10, 10)
    rollTarget = random(-RollRange, +RollRange) degrees                            (2)
roll      = (rollTarget - roll) * 0.8 * dt          (assigned, not added: it stays a fraction of a degree)
velocity *= MovementDrag                             (0.95, once a frame)
push      = (DriftSpeed.x + move * DriftSpeed.y + dark * DriftSpeed.z) * dt      (60 + 60 move + 160 dark)
toward    = normalise(target - offset)
if velocity == 0: velocity = toward * push
else:
    if dot(toward, heading) >= 0: velocity += toward * push
    turn = (TurnRate.x + move * TurnRate.y + dark * TurnRate.z) * dt degrees      (90 + 60 dark a second)
    if cos(turn) > dot(toward, heading): velocity is turned about cross(heading, toward)
offset   += velocity * dt
```

The camera looks at its aim + offset from its place - (`SteadyCamPositionOffsetScalar` 1.2 - 1) * offset.xy
[exe `0x9783c0`]: the picture turns by up to 0.7 degrees (15 units over 1400) rather than slides, a new goal
every 2 s, sooner and faster in the dark and on the move. (The executable passes `push` where the turn's angle
is meant; the mod turns by `turn`.) `raid.lighting.darkest`'s `camera: .steady 5000 .steady_limit 100 0
.shudder 0 0 0 0` are the timescript camera's resting values, not read by this function.

3.6 **Timescript camera** (`scripts/timescript/*.times`): `camera.absolute_focus` x/y/z with
`absolute_focus_blend` pull the camera towards a point; `camera.zoom`, `camera.tilt`, `camera.shudder`,
`camera.foreground_active`, `presentation_camera.*` are set by the scripts of section 9.

## 4. Layers, back to front

3.6 **The camera's limits in a hallway [game].** Measured in the running game step by step (2026-10-05,
`tools/dd1_drive.py` + `tools/dd1_match.py`; the wall tile is 832 px wide on a 2560 px screen, 0.3 % off the
camera of this spec): the camera's x is `clamp(leader x, 360, last - 360) + 80`, `last` being the middle of the
last door tile. At the start it stands at 440 while the leader walks from about -38 to 360 and follows from
there (camera - leader = 79 in every frame after that); at the far end it stops at `last - 280` while the leader
walks on to about `last + 44`. So a door tile is never in the middle of the view, and the party walks out to a
door on its own. In the executable [`0xabbac0`, called at `0xab74a2` before the camera update, only while no
fight is on]: `base.x = clamp(leader.x, first.x + 360, last.x - 360)`, written as a window of +-1080 kept inside
the drawn wall with its two extra tiles (constants 720 and 1080); the hallway offset (80, or -50 walking back),
the scripts' focus and the drift are added to that base.

3.7 **The party's limits and its place on entering [exe + game].** Walking forward is accepted while the leader
is before the last door tile's middle; walking back while the **last** hero is beyond x -180 [`0xaea9b0`]; there
is no hard stop, the party runs on for its stopping distance (50 forward, 12.5 back). With the tutorial's two
heroes that is -38.5 .. last + 50 for the leader: the -38 and +44 measured. A party of four cannot back further
than its leader at about 270. On entering a hallway the party's position is the tile's middle and the party is
then moved inside the same limits [`0x9ae9a0`, `0xaf3a59`]: the leader stands 180 beyond the first door tile's
middle, or 282 with four heroes (the last one at -180). Section 8's "leader in the middle of the first door
tile" was wrong. Two heroes stood 170 apart after walking forward and 139 after backing to the start, spreading
again over the next steps (153, 160, 165, 166, 170): the followers are not rigidly `actor_spacing` behind (not
modelled by the mod).

4.1 **Wall and floor are one picture drawn as two quads** [exe `0xabf7d0`, called from `0xabe140` with the
constants 600, 0.8333 (= 600/720) and -600]:

* the art's **upper 600 rows stand upright** at z = `distance_to_interior_background 400`, from y = 0 to y = 600;
* its **lower 120 rows are the floor**: a horizontal quad at y = 0 from z = 400 (row 600 of the art) to
  z = -600 (row 720), as wide as the wall quad.

So the floor is a real ground plane under the perspective camera: its far edge moves with the wall, its near
part with the heroes, and whatever stands on it at any depth neither slides nor floats. In a hallway each quad
is **720 wide** (`tile x - 360 .. + 360`), unscaled; DD1 draws `corridor_door.basic` and `corridor_wall.NN` this
way.

**The ends of a hallway** [exe + game, 2026-10-05; this replaces the first reading, which had `endhall.01` as a
wall tile beside each door tile]:

* beyond each door tile DD1 draws **one more wall and floor** of the same geometry [exe `0xabe91d..0xabed66`,
  `0xabf06d..0xabf479`]: left of the first door tile (x -1080..-360) with the picture of `tiles[1]`, right of the
  last one with the picture of `tiles[count - 2]`, **mirrored**;
* **`endhall.01`** is a picture of its own, not a wall tile [exe: loaded at `0xaeef22`, a deferred quad, ctor
  `0xab3850`, draw `0xab39d0`]: it stands at z = `curio_z_position` (75), in front of the wall, **a tile wide from
  the middle of the door tile outwards** (left: x -720..0, **mirrored**; right: x last..last + 720), from y -80 to
  h - 80, alpha `1 - view[0x7f4]` (what that value is was not read; the picture is fully there in the frames
  taken). Its pillar or tree stands over the door tile's outer half and its lower rows over the floor.
  `endhallfront.01` is loaded and never drawn.
* the view flag `[view+0x76d]` is set to 1 in the constructor and never written again: the "2D" branches read
  earlier (the endhall sprite pair at `0xabe7e2`, the UV-scroll far-layer branch of 4.4) are dead code. The live
  far-layer branch (`0xabff6e`) has not been read again.

Checked against the running game (the tutorial's Weald hallway, save `texture_id` 200,100,100,100,100,200):
`tools/dd1_match.py` finds `corridor_wall.01` left of the first door tile, and a frame rebuilt with the endhall
quad in the place above matches the real one at 0.67 against 0.42 without it (`_lab/shots/dd1_end_cap_test.png`,
`dd1_start_cap_test.png`).

4.2 **A room** [exe `0xabe605`]: the same two quads, but the 1920x720 picture is drawn as wide as the room
camera sees at the wall's depth: `width = 2 tan(37.5 deg) * (400 + 1240) = 2516.8` units (a scale of 1.3109),
centred on x = 960, the upright part `600 * 1.3109 = 786.5` high. Under the room camera that is exactly 1920
pixels wide and one pixel of art to one pixel of screen for the upright part (rows -11..589 of the view); the
floor's 120 rows are stretched over rows 589..720 and beyond.

4.3 **Foreground strips** [exe `0xabbe20`], hallways only, per tile: `foreground_top.01` and
`foreground_bottom.01` are scaled to **720 wide** (height = `h * 720 / w`: crypts 330 and 101) and hung at
z = `distance_to_interior_foreground -300`, i.e. **between the camera and the party**:

* top strip: from y = `600 + foreground_top_y_offset (0)` downwards (crypts: y 600..270);
* bottom strip: from y = `foreground_bottom_y_offset (-10)` upwards (crypts: y -10..91).

At 1.4217 px per unit they cover rows -109..360 (the solid part to about row 147, the drips to the middle of
the view) and rows 614..758: the upper one hides the wall's top edge (row 73), the lower one runs in front of
the heroes' feet (row 662). Being nearest they pass fastest. They are faded with `fade_controls`
`foreground_in_time 0.4`, `foreground_out_time 0.2`, `foreground_pre_battle_fade_time 0.2`,
`foreground_post_battle_fade_time 0.4` and switched by the timescripts' `camera.foreground_active`.
The alpha is a linear tween [exe `0xab7540`]: a fight's start sends it to 0 over the pre-battle time, its end to 1
over the post-battle time; out of a fight its target is `camera.foreground_active` (in time when that is above
the current value, out time when below). Nothing about walking enters it.
**Open [game]:** in the frames of 2026-10-05 (a party of two standing in the tutorial's Weald hallway, after its
first fight) no strips are on screen: nothing black moves at the strips' rate, and the rows where the bottom strip
would cover half the floor are clear (`_lab/shots/cmp_top_right_full.png`). The code above does not explain
that. The strips' u/v also take offsets that were not resolved (`0xabc009..0xabc0d9`: a time scroll and a
camera term). Their place and look in a hallway where they are visible has still to be seen in the game.

4.4 **Far background and midground** [exe `0xac0490` calls `0xabfd70` twice]: `corridor_bg` (opaque) with depth
`distance_to_farbackground 1500` and a factor 0.5, then `corridor_mid` with `distance_to_midground 1000` and a
factor 0.25. In the branch the four ordinary dungeons take, the function tiles the 720 px picture side by side
and scrolls its texture: `u offset = frac((x - camera x * factor) / 720)`; the depth is used only by the branch
of the Darkest Dungeon's animated corridor. They show through the windows of the wall art only. **[inferred]**:
drawn at the view's own size (720 px picture over the view's 720 rows, its middle row on the horizon), moving
`factor` pixels per unit the camera travels. The space these quads are drawn in and the sign of the scroll are
not established (section 12).

4.5 **What stands in the hallway** [exe `0x952100`, `0x9524f0`]: a Spine skeleton drawn one unit to a skeleton
pixel, its origin (the middle of its foot) at

| Thing | x | y | z |
|---|---|---|---|
| Door | tile x | 0 | `distance_to_interior_background` 400 (in the wall) |
| Curio | tile x + `curio_tile_centre_x_offset` 135 (room: room middle + `..._room` 75) | 0 | `curio_z_position` 75 |
| Obstacle | tile x + `obstacle_tile_centre_x_offset` 75 | 0 | **`curio_z_position` 75** (the executable reuses the curio's depth) |
| Trap | tile x + `trap_tile_centre_x_offset` 0 | 0 | `trap_z_position` 15; once sprung: z = 1 |
| Hero i | party x + 180 - 154 i | 0 | 0 |

All of them stand ON the floor plane (y = 0); none has a scale of its own. Draw order is by depth.

4.6 **Order of drawing**: far background, midground, wall + floor quads, end caps, props by depth, heroes,
foreground strips; then the post effects of section 6; then the panel and overlays.

## 5. Lighting

5.1 **Base / Half / Edge** [exe `0xab7cd0`]: per frame
`torch01 = torch / 100 * flicker`; `L = torch_min + (1 - torch_min) * torch01` (`shade_character.torch_min 0.4`).
The six colours `lighting_none_base/half/edge` and `lighting_full_base/half/edge`
(`colours/base.colours.darkest`) are blended `none * (1 - t) + full * t`, with t = torch01 for the sprite
shaders and t = L for the character shader, and multiplied by a **scene fade k** (1 normally; the door
transition of section 8 runs it to 0 and back). In the shipped file both sets are the same:

| | Base | Half | Edge |
|---|---|---|---|
| none and full | 255 255 255 | 200 200 200 | 75 75 75 |

so **the torch level does not change these three colours at all**; the torch is seen through the colour grade
(6.1). The camp has its own set (`camplight_full/none`: `#ff6a21`, `#495d69`, `#444b4f`).

5.2 **Walls, floor, props** (`lit_sprite.glsl`): with `x` the pixel's view-space x (distance from the camera's
axis, in world units times the camera's zoom [inferred that the zoom is part of the view transform]):

```
d   = 1 - |x| / 960                      (clamped to 0..1)
rgb = texture * vertexColour * ( d < 0.5 ? mix(Edge, Half, 2 d) : mix(Half, Base, 2 (d - 0.5)) )
rgb = mix(luminance(rgb), rgb, Saturation) * Intensity        luminance = 0.2126 r + 0.7152 g + 0.0772 b
```

Piecewise linear in x with stops at |x| = 0 (Base), 480 (Half), 960 (Edge), flat beyond. In a hallway (zoom
1.25) the stops are 0 / 384 / 768 world units from the camera's axis: on the wall (0.8688 px per unit) that is
0 / 334 / 667 pixels from the screen's middle, on the party's line 0 / 429 / 858. In a room (zoom 1): 0 / 480 /
960 units, on the wall 0 / 366 / 732 px. `Saturation` and `Intensity` are 1 except while a timescript changes
`layers.A_saturation` / `A_intensity` (section 9).

DD1 multiplies in the art's stored (gamma) space. DD2 renders in **linear** space
(`QualitySettings.activeColorSpace`), where a sprite shader multiplies the vertex colour after decoding the
texture. There is no exact vertex colour for DD1's multiplier m: sRGB is not a pure power near black, so
`decode(art * m)` is `decode(art) * m^2.2` only for bright art and nearer `decode(art) * m^1.45` for dark art.
Measured in game (stage 2, `_lab/shots/c2_lit.png` against `c2_unlit.png`, wall rows, 31 columns across the
screen): with the vertex colour `decode(m)` the lit-to-unlit ratio on screen was `m^1.52` everywhere (0.16 at
the edge where DD1 has 0.294), the stops themselves exactly where the specification puts them (334 / 667 px
from the middle at the wall). `CorridorLight.Gamma` 1.45 (= 2.2 / 1.52) is the exponent that gives m on DD1's
dark dungeon art; `RampGamma` is DD1's own value, `Ramp` the vertex colour.

5.3 **Heroes** (`character.glsl`; uniforms in [exe] `0xab7cd0`, `0xab42f0`):

```
v   = |pixel - light centre| - falloff_start          per axis, clamped to 0..falloff_distance
i   = clamp(1 - sqrt((v.x / falloff_distance.x)^2 + (v.y / falloff_distance.y)^2) * LightFalloffScalar, 0, 1)
rgb = texture * vertexColour * Base * i * Intensity * LightScalar
rgb = mix(rgb, rgb * wash.rgb, wash.a)
```

* light centre = the camera's axis moved by `light_parameters.offset_from_centre 0 -60`: at the camera's x, 60
  below its height (y = 210 in a hallway: chest height), in world units on the party's line;
* `falloff_start 450 140`, `falloff_distance 600 250` (zoomed-out room camera: `0 0`, `450 170`, `600 280`;
  camp: `camp_offset_from_centre 0 50`, `camp_falloff_start 200 150`, `camp_falloff_distance 400 150`);
* `Intensity` 1, `LightFalloffScalar` 1, `LightScalar` 1 per hero except on the way into a door (section 8);
* `shade_character.wash 0 0 0 15`: every hero is 15/255 = 5.9 % darker;
* `gradA`, `gradB`, `y_gradient_start_line 275`, `light_range 1.0 0.75` are passed to the shader, which
  computes a gradient and never uses it: **dead data**.

In a hallway the leader (80 from the axis) and ranks 2 and 3 (234, 388) are fully lit, rank 4 (542) is 92
outside the box: at chest height `1 - 92/600 = 0.85`. Feet (y = 0) are 70 below the box: `1 - 70/250 = 0.72`.

5.4 **Flicker** (`flicker: .variance 0.05 .range 1.0 1.1`) [exe `0xab7cd0`]: every frame
`flicker += random(-variance, +variance)`, clamped to the range; with flicker disabled in the options it is the
range's middle. It only multiplies `torch01` of 5.1, so with the shipped colours **it changes nothing on
screen**. (The torch that visibly flickers is the panel's flame, `fx/torch`.)

5.5 **Death's door** (`deaths_door_pulse: .wash #ff0000ff .period 1.5 .length 0.3 .num 2`): a hero's wash
pulses red; `select: .washspeed 0.05`.

## 6. Post effects

6.1 **Colour grade by torch** (`film.glsl`; [exe] `0xb04680`): five 16x16x16 tables per dungeon,
`dungeons/<d>/colour_grade_0.png` .. `_4.png` (16 wide, 256 high: x = red, y inside a block of 16 rows = green,
the block = blue), sampled as `texture3D(lut, rgb * 15/16 + 1/32)`:

```
i = min(4, floor(g / 25));  T = (g - 25 i) / 25
rgb = mix( lut[i](rgb), lut[min(4, i + 1)](rgb), T )
```

**Corrected 2026-10-06 (a second reading of the exe, for the question "does the light change by degrees"):
`g` is not the torch.** The grade reads a float of its own at `RaidDisplay+0x3588` [exe `0xb048de`; the two
tables and `TorchColourT` are set from it at `0xac2a81..0xac2b22`]. Every frame `0xb044e0` [called at
`0xae9fe7`] moves it towards `ceil(torch / 25) * 25` with a tween of 0.5 s (the float at `0x1123258`) and no
easing; the tween is made anew every frame while g is not at its goal, so its remaining time stays 0.5 and the
linear stepper [`0x741829`] gives `g += (goal - g) * dt / 0.5`: an exponential approach, 63 % after half a
second, 95 % after a second and a half. So **at rest the dungeon has one table a band** (76..100: 4, 51..75: 3,
26..50: 2, 1..25: 1, 0: 0); a tile's burn inside a band changes nothing on screen, and crossing into another
band is a second or so of drifting colour. (This section used to have the blend continuous in the torch's own
value, and the mod drew it so until `Core/LightEase.cs`.) Read once, by one helper session, not checked a second
time against the running DD1: what agrees with it is DD1's own naming (five flame loops, one a band) and the
bands of `shared/rules.json`; a recording of DD1 crossing 75 would settle the half second.

The gauge itself is NOT smoothed in DD1: `LightTorchOverlay::Render` [exe `0xb10ec0`] builds both bars from
`torch / 100` every frame. What covers the jump is the torch's art: `fx/torch` plays `ignite` (or `extinguish`),
1.0 s each, `fx/torch_sparks` a burst of 0.8 s, and `fx/torch_flame` goes to the band's loop (`radiant_loop`,
`dim_loop`, `shadowy_loop`, `dark_loop`, `out_loop`), when the band rose (fell) or the change was not the
walk's own burn (a torch lit, a torch put down, a skill, a camp, an ambush: the flag of `TorchModifiedEvent`)
[exe `0xb110bd..0xb11195`, the listener `0xb136c0`]. A tile's burn inside a band only shortens the bar.

Table 4 is full light, table 0 darkness. What a mid grey (136) becomes, crypts: 4: (148,166,173), 3:
(164,137,126), 2: (181,133,117), 1: (181,114,95), 0: (138,157,175); white: 255 / 220 / 245,238,237 /
248,235,233 / 221,218,221. Full light is cool and clear, the middle levels turn red-brown and crush the
shadows, darkness is grey-blue and drained (`dd1_reference_torch_crypts.png`). The view drawn before the grade
is the same at every level. `colour_grade.png` (no number) is the identity; `custom_colour_grade` of a torch
setting, quirk and stealth grades (`colours/*.png`) override through `PresentationGradeLUTOverride`.

6.2 **Film grain** (`film.glsl`, `grain: .intensity 0.1`): per pixel a white noise n in 0..1 that changes every
frame; `rgb += rgb * clamp(0.1 + n, 0, 1) * 0.1`: one to ten per cent brighter, about 5.5 % on average.

6.3 **Composite** (`composite.glsl`): `rgb = pow(pow(rgb, 2.2) * (gamma_scale + 1), 1/2.2) * global_fade`:
the player's brightness setting and the whole-screen fade (loading screens).

6.4 **Blur** (`blur_horiz.glsl`, `blur_vert.glsl`): a 5-tap gaussian (weights 0.227, 0.316, 0.070 at 0,
1.385, 3.231 px), mixed by `Amount`; driven by the timescripts' `layers.A_blurriness`.

## 7. Walking

`shared/rules.json` [file]: `m_ForwardAcceleration 1600`, `m_MaxForwardSpeed 400`, `m_ReverseAcceleration 800`,
`m_MaxReverseSpeed 200`, `m_Deceleration 1600` (units and units per second: a tile takes 1.8 s at full speed,
which is reached in 0.25 s; walking back is half as fast). `m_InteractionAreaPixelWidth 470`,
`m_InteractionAreaPixelHeight 400`: the reach in which a prop can be used.

Heroes do not turn round to walk back. Each hero plays `heroes/<class>/anim/<class>.sprite.walk` (1.067 s a
cycle for every class) while moving and `...sprite.idle` standing; the walk animation runs at
`1 + 0.25 * speed / top speed` times its own rate [exe `0xab42f0`]. Leaving the party position on a tile raises
that tile's events (2.2). The leader's range in a hallway: from the middle of the first door tile (party x =
-180) to 180 beyond the middle of the last [exe `0xaf3844`].

## 8. Going through a door: the timeline

[exe `0xb06100` (walking in), `0xaf3630` (fade out and new area), `0xae9c20` state machine (fade in);
`screen.raid.darkest` `door_transition` and `fade_controls`; `timescript/door_investigate_intro.times`]

| When | What |
|---|---|
| t0: the door is used (within reach, 7) | `door_investigate_intro`: depth sorting of heroes and the door off, the door's `open` animation (a single pose: the door skeleton's `open` and `closed` have no length) |
| from t0 | every hero walks to the door. A hero's `LightScalar = min(1, (distance to its goal / 250)^2)`, 250 = `party_darken_distance 200` + `visibility_distance_to_door 50`: each darkens to black over its last 250 units |
| once the party is within `party_door_distance_camera_start 650` of the door | tweens of `camera_position_blend_time 3.0` s start: the camera blends towards the door, and the scene fade k of 5.1 runs to 0 (walls, floor, props darken) |
| all heroes arrived (+ `post_party_at_door_wait 0.01` s) | fade out: `k -= dt / fade_out_time (0.7)` until 0 |
| k = 0 | the next area is built; every hero's `LightScalar` back to 1; **room**: party position = `area.room_position 600`, i.e. leader at x = 780, the others at 626, 472, 318; **hallway**: party position at its left end, i.e. the leader in the middle of the first door tile (x = 0) |
| then | fade in: `k += dt / fade_in_time (0.7)` until 1; the party already stands there, idle |

There is no walking-in on the far side and no door animation beyond the one pose. A room's exits are chosen on
the map; a hallway is always laid out so that the party enters at its left end and walks right (the map data's
`reversed` flag turns the tiles round).

## 9. Timescripts (what the screen does at a curio or a trap)

9.1 **The language** (`scripts/timescript/*.times`; read from the files, the semantics [inferred] from how
the scripts are written): commands run one after another.

| Command | Does |
|---|---|
| `tween: target[,component] .value V` / `.expr "..."` / `.track self` `[.time T] [.easing f]` | takes a property to a value, to an expression of the game's names (`centre`, `offset`, `actor_x`, `dir`, `hit`, `skill_area_pos_offset_x`...), or back to what it was before the script; at once, or over T seconds (then the script waits for it, unless in a group); easings seen: `easeOutSine`, `easeOutBack`, `easeOutQuad` |
| `group:` .. `end:` | its commands run together; the group ends with its longest |
| `lambda: name [.time T]` | calls into the game (`hero_prep`: the hero's investigating pose; `open_lambda`: the curio opens and its result shows; `on_zoom_start_lambda`, `teleport_start`...); in a group after T seconds |
| `wait: T` | T seconds |
| `lock:` / `unlock: target.property` | holds a property against the game's own changes (the actors' turn scale of `actor_scale.raid.darkest`) |
| `?target2`, `~target` | an optional target; every target |

9.2 **The properties**

| Property | Meaning | Rest |
|---|---|---|
| `camera.zoom` | added to the camera's own zoom as `zoom - 1` (3.2) | 1 |
| `camera.absolute_focus,x/y/z`, `camera.absolute_focus_blend,x/y/z` | a point the camera is pulled to, per axis, by the blend (0..1) [exe `0x9772a0`] | blend 0 |
| `camera.tilt`, `camera.shudder,x` / `,y` | roll in degrees; a shake: x its size, y its rate [the rate's unit inferred] | 0 |
| `camera.foreground_active` | the foreground strips on or off (faded by `fade_controls`) | 1 |
| `camera.presentation`, `presentation_camera.zoom` / `.blend`, `raid_presentation_data.blend` | a second camera (`camera.darkest` `presentation_camera: .z_position -1240`) that draws the acting actors, blended in: they appear at its zoom, larger than the scene behind them | 0 |
| `layers.A_saturation`, `A_intensity`, `A_blurriness` | the scene (walls, props): the shaders' `Saturation` and `Intensity` (5.2), the blur's `Amount` (6.4) | 1, 1, 0 |
| `layers.B_intensity` | the actors' `Intensity` | 1 |
| `layers.HUD_alpha`, `HUD_update` | the panel | 1 |
| `actor.area_pos,x/y`, `actor.scale`, `target.*` | where the acting hero (and a skill's targets) stand in the view | - |
| `curio.zlayer`, `actor.layer_sort_enabled`, `prop.layer_sort_enabled` | 101: the curio is drawn with the actors, in front of the scene; depth sorting off | 0 |

9.3 **The scripts of the corridor**

* `investigate_intro` (curios; obstacles [inferred]): `hero_prep`; curio to the front; in 0.1 s: hero to
  `centre - offset * 0.6`, focus y 250, `camera.zoom 1.2`, scene saturation 0.5 and blur 3, actors' intensity
  1.4, panel and foreground off, presentation camera zoom 1.7 blended in; `open_lambda`; then the hero drifts
  to `centre - offset * 0.5` over 1.0 s.
* `investigate_emphasis` (a bad result): shake 4 / 20, dying over 1.0 s.
* `investigate_extro`: everything back in 0.3 s (`.track self`), curio back in its layer, 0.2 s, presentation off.
* `disarm_intro` / `disarm_extro`: the same with focus y 230 and no `open_lambda`.
* `trap` (2.2 s): in 0.1 s: focus on the hero (x = `actor_x`, y 230), `camera.zoom 1.3`, presentation zoom 2.0,
  scene intensity 0.5 and blur 5, panel and foreground off; shake 5 / 30 dying over 1.8 s; back in 0.3 s.
* `door_investigate_intro`: layer sorting off and `open_lambda` (the door opens); its emphasis and extro are empty.
* `teleport`: a skill-like script for the teleporter curio; the mod's generator makes none.
* `announcement_times`: trap 1.0 s, disarm 0.5, curio 0.5; `pop_prop_times.trap 1.2`.

`centre` and `offset` are names of the executable and not recovered. [inferred]: `centre` is the middle of the
view, `offset` the step between two places of a presentation: the acting hero comes to stand half a step left
of the middle, at the party's front, facing the curio 235 units right of the middle.

## 10. On a 1920x1080 screen (from the reference renderer, crypts)

Hallway, walking on, camera 80 ahead of the leader:

| Thing | Skeleton size (px) | On screen | Where |
|---|---|---|---|
| Crusader (idle) | 178 x 351 | 199 x 391 | x 770..969, feet row 661.6 (box to 679) |
| Highwayman / Plague Doctor / Vestal | 193 x 317 / 189 x 303 / 185 x 312 | 215 x 354 / 214 x 338 / 206 x 348 | 172 px apart |
| Curio `discarded_pack` | 231 x 176 | 245 x 187 | foot row 646.3 |
| Trap `spikes` | 301 x 66 | 333 x 73 | row 658.4 |
| Door (crypts) | 202 x 429 | 176 x 373 | rows 222..595 |
| Wall tile | 720 x 600 (+120 floor) | 626 x 521 | rows 73..595, floor below |
| Foreground top / bottom | 720 x 330 / 720 x 101 | 1024 x 469 / 1024 x 144 | rows -109..360 / 614..758 |

Room: Crusader 180 x 353 at x 687..867, treasure chest 196 x 115 with its foot on row 645.4 at x 933..1130, the
wall's upright part rows -11..589.

The pack is half a Crusader's height (187 against 391); the leader stands 89 px left of the screen's middle; the view shows
1719 units of the party's line and 2210 of the wall: about one tile ahead of the leader.

## 11. The mod today against this (from `_lab/shots/raid_curio.png`, `acc_prop_obstacle.png`, 2560x1440 scaled to 1080 rows)

| | DD1 | Mod now | Off by |
|---|---|---|---|
| Wall art on screen | 0.8688 px per px, rows 73..595, edges hidden by the foreground | 1.0 px per px, fills all 720 rows | +15 %, and the floor part is a flat card |
| Floor | a ground plane in perspective | part of the wall card | props and heroes slide on it as the camera moves |
| Curio | 1.0603 px per px | 1.22 (wall x 1.220) | **+15 %** |
| Trap | 1.1052 | 1.27 | +15 % |
| Hero height (Crusader) | 391 px (351 x 1.1171) | about 293 px (measured on the screenshot) | **-25 %** |
| Curio against a hero (the pack against a Crusader) | 0.48 of his height | 0.73 (215 px against 293) | curios look **about 1.5 times too big** next to the party |
| Prop depth | behind the party (z 75 / 15), slower than the heroes | in front of the party's plane (to get its size), faster than the heroes | "objects move crookedly" |
| Foreground strips | yes, in front of the heroes | none | - |
| Light | ramp to 29 % at the edges, box light on heroes, grade per torch | flat, hero brightness by torch | "no lighting" |
| Door | walk in, darken, 0.7 s fade out, 0.7 s fade in | cut | "no animations" |
| Camera | 80 ahead of the leader, 0.5 s tweens, closes in walking back | lead 1.0 unit, no tween | - |

The earlier size rule `(D + W) / (D + z)` (`corridor-props.md` section 2) was right about a prop against the
wall; what was wrong is the wall itself (drawn 15 % too large, with the floor inside it) and the heroes' scale,
and that the size was faked by moving the prop towards the camera.

## 12. Inferred or not recovered

1. **Camera offset from the leader** (3.2): the key is named so; the executable's camera takes a position I did
   not trace to the leader or the party position (180 behind him). The leader 89 px left of the middle matches
   DD1 as played.
2. **The zoom is part of the view transform** (5.2): it sits beside the camera's position and direction in its
   object; if it were in the projection the ramp's stops would be 25 % wider in hallways.
3. **Far background and midground** (4.4): the tiling, the factors 0.5 / 0.25 and the scroll formula are read;
   the size on screen, the vertical place and the direction are not. Both show only through the windows.
4. **End caps** drawn like wall tiles (4.1); which shader the foreground strips use (they are near-black).
5. The **camera's blend towards the door** (8): parameter known, motion not recovered. (The steady cam is
   recovered: 3.5.)
5a. The timescripts' `centre` and `offset`, the unit of the shake's rate, and that an obstacle uses the
   investigate scripts (9).
6. **Hero and prop scale 1** (4.5): no scale is set at placement and none in the prop definitions; the door fits
   its painted doorway only at scale 1.
7. **Door reach and each hero's goal** at the door (8): the reach is the interaction area; the goal is taken to be
   the door's place.
8. Read but not used here: the Darkest Dungeon's animated corridor (`corridor_animation_data_*`,
   `flesh_effect_animation_data_*`, `darkest_dungeon_midground.glsl`), `raid_visuals.json` background
   animations (starfield, heart room), stealth and trait grades, the arena's mirrored party.

## 13. The mod's corridor (stage 2: scene, camera, sizes, placement)

Files: `Dungeon/CorridorNumbers.cs` (every number of this document as a static field, read from DD1's files
with DD1's shipped values as marked fallbacks), `CorridorScene.cs` (the scene), `CorridorView.cs` (camera,
party, walking), `CorridorLight.cs` (the ramp of 5.2), `CorridorProps*.cs` (placement), `CorridorDev.cs`.

What is DD1's, as specified:

* the scene in DD1's units under a root scaled by `CorridorNumbers.UnitScale`: wall quad + floor quad per tile
  (4.1), a room's picture at the room camera's width (4.2), end caps, foreground strips at z = -300 (4.3);
* the camera of section 3: 75 degrees across DD1's 1920x720 view, the hallway offset 80 / 270 / -1400 from the
  leader with zoom 1.25, the walking-back blend, the fixed room camera; the zoom is applied as a longer lens
  (`VerticalFov(zoom)`), which is the same magnification about the view's middle;
* props on the floor at their own depth, one unit to a pixel (4.5); the party at z = 0, 154 apart, the leader
  180 ahead of the point the tile is counted from (2.2, 2.3);
* walking by `shared/rules.json` (7): 1600 / 400 forwards, 800 / 200 backwards, 1600 to a stop;
* the light ramp on walls, floor, far layers and props (5.1, 5.2), with the torch's blend and the scene fade.

The mod's own choices:

* **`UnitScale` 0.006** world units to a DD1 unit: a DD2 Crusader's body mesh is 1.745 units from sole to crest,
  DD1's Crusader 292 of its units (the helmet's top; the raised sword reaches 335), so the models keep scale 1
  (`CorridorView.HeroScale`) and stand as tall on screen as DD1's. A tile is 4.32 world units.
* **The dungeon view is the main camera's rectangle** (`CorridorView.UseViewRect`): DD1's camera has its axis in
  the middle of the 1920x720 view, a third of the screen above the panel. While the corridor is shown the main
  camera's `rect` is the top two thirds of the screen (`StripShare`); the game's overlay cameras draw into
  their base camera's rectangle; a black panel on the corridor's own canvas (order 3, under the HUD) fills the
  lower third. The rectangle is given back when the view is switched off (fights, camp, hamlet).
* **Foreground in front of the heroes** (`CorridorScene.ForegroundMode`): the game's "Character Camera" (an
  overlay camera in the main camera's stack, layers 10 and 29) draws the hero models after everything the main
  camera draws. Mode 1 (default): a further overlay camera of the mod's own (`DD2Estate.ForegroundCamera`, a
  child of the main camera with the game's `CopyCameraFrustumBhv`) is kept **last** in that stack and draws
  only layer `ForegroundLayer` (27: drawn by neither of the other two), on which the strips lie. The game's
  character camera re-inserts itself behind unknown cameras whenever it is switched on, so the order is put
  right every frame. The pipeline's `UniversalAdditionalCameraData` is reached by reflection (the plugin does
  not reference the URP assembly). Mode 2: the strips on the "Characters" layer with a high sorting order,
  drawn by the game's own character camera. Mode 0: none.
* **Far background and midground** (4.4, inferred): a quad kept facing the camera at the layer's depth, sized so
  that the 720 px picture covers the view's 720 rows with its middle on the horizon, its texture moved
  `FarFollow` 0.5 / `MidFollow` 0.25 view pixels per unit of camera travel; the ramp runs over the view's pixels.
* **Light in vertex colours**: walls and floors are cut into columns `CorridorScene.ColumnWidth` (45 units) wide
  and recoloured when the camera or the light moves; a prop gets one colour, the ramp's at its foot (DD1 lights
  it per pixel).
* **Tile events**: the party enters a tile as in DD1 (when the point 180 behind the leader crosses its edge);
  entering the last tile still sends the party into the next room by itself (DD1 waits for the door to be
  used: stage 4), and walking back into the first tile returns it to the room it came from.
* `CorridorProps.StandOff` 200 units: where the leader stops before a clicked prop.
* The walk itself is still the old bob over the idle pose: stage 4.

### Stage 3: the heroes' light, the grade, grain, fade, flicker

Files: `CorridorLight.cs` (every term and its switch), `CorridorPost.cs` (grade and grain), the hero look and
the curtain in `CorridorView.cs`, the camera in `CorridorScene.ForegroundCamera`.

| DD1 term (section) | In the mod | Switch (bridge: `corridor.light`) |
|---|---|---|
| Ramp on walls, floor, props (5.2) | vertex colours, `m^Gamma` | `ramp=false`; `CorridorLight.Gamma` |
| Character light (5.3) | one value per hero: the box light at the hero's x and at `HeroLightHeight` 170, times the wash and the hero's `LightScalar`, as the model's `_Brightness` = `HeroBrightness * light^HeroGamma` through `MaterialPropertyBhv.AddOverride` | `heroes=false`; `heroBrightness`, `heroGamma` |
| Base / Half / Edge by torch (5.1) | read from `base.colours.darkest`, blended by `torch / 100 * flicker` | - (the shipped colours are equal) |
| Colour grade by torch (6.1) | the two tables around `torch / 25` blended into one, resampled to the pipeline's LUT size (`UniversalRenderPipeline.asset.colorGradingLutSize`) as a strip texture, on a URP `ColorLookup` override | `grade=false`; `contribution` |
| Grain (6.2) | the mean (+5.5 %) in the table, the noise by URP's `FilmGrain` override | `grain=false`; `grainIntensity` |
| Scene fade k (5.1, 8) | a black curtain on the corridor's canvas over the view, alpha `1 - k^2.2` | `fade=0..1` |
| Flicker (5.4) | DD1's random walk (60 steps a second) on the torch blend: nothing to see, as in DD1 | `flicker=0.3` makes it dim the scene (the mod's own, default 0) |
| Composite gamma, blur (6.3, 6.4) | not done (the game's own brightness setting; blur belongs to the timescripts) | - |

The mod's own choices:

* **Where the grade is applied.** The mod's overlay camera (last in the main camera's stack, the one that draws
  the foreground strips) runs the pipeline's post effects with a volume mask of its own layer only
  (`CorridorScene.ForegroundLayer`), and the mod's global `Volume` (`DD2Estate.CorridorVolume`, a child of the
  view, so it goes when the view is switched off) lies on that layer. So grade and grain are applied once, to
  the finished view (walls, heroes, foreground), by a camera that takes no other volume of the game, and the
  game's cameras do not take the mod's. The pipeline looks a user LUT up in display values, as DD1's shader
  does.
* **A hero is lit as a whole** (DD1: per pixel; its feet are up to 28 % darker than its chest), and **the torch
  no longer dims the models**: in DD1 it does not either (5.1); the grade does the darkening.
* `HeroBrightness` 2.0 (chosen in game against 1.35, 1.8 and 2.3: `_lab/shots/c3_hero_sheet.png`), `HeroGamma`
  2.0, `CorridorView.HeroShadow` (the old grey blue), `HeroSaturation` (off): DD2's models are darker than DD1's
  sprites.
* `CorridorView.HeroGradient` (off until looked at): the hero shader's vertical gradient (`_UseGradient`,
  `_GradientOpacity` 0.3, the other four left as the material has them unless set) for DD1's darker feet. The
  properties' meaning is not known from the game's code: to be tuned by eye (`corridor.style gradient=true ...`).
* The curtain also takes the flicker when `FlickerVisible` > 0.

### Stage 4: through a door, and the walk

Files: `CorridorTransition.cs` (settings, and the sequence as a part of `CorridorView`), `CorridorView.cs`
(`ShowArea` now asks for an area; walk stand-ins), `CorridorProps.cs` (what stands in an asked-for area is held
back until it is on screen).

The sequence (section 8), with DD1's times from `screen.raid.darkest`:

| Phase | What | Ends |
|---|---|---|
| `WalkIn` (only out of a hallway, by one of its door tiles, into a room) | every hero walks at DD1's forward speed to the door (its tile's middle, and `WalkDepthShare` of the way to the wall's depth); its light is `min(1, (distance / 250)^2)`, and within 50 of the door it is gone; the scene's light goes down by 1/3 a second; the camera's x blends from the party to the door over 3 s | all heroes in, plus 0.01 s |
| `FadeOut` | the scene's light down by 1/0.7 a second | at 0: the asked-for area and its props are built, the party at DD1's place (room: leader at 780; hallway: leader in the middle of the first door tile) |
| `FadeIn` | the light up by 1/0.7 a second | at 1 |

`CorridorView.ShowArea` starts it whenever an area is on screen; the first area of an expedition (and a loaded
one) is built at once and fades in. Leaving a room, entering or leaving a secret room: the two fades only
([inferred]: DD1's walk is into a hallway's door prop). While it runs the party cannot be moved
(`CorridorView.Transitioning`); a fight (`Suspend`) or `PlaceInTile` ends it at once with the asked-for area on
screen. The door prop opens by itself when the party enters its tile (the rules of sight), as DD1's
`door_investigate_intro` opens it. `CorridorTransition.Enabled`, `TimeScale` (0: no sequence, for test
scripts), `WalkIn`, `WalkDepthShare`, `CameraBlend`.

The mod still sends the party through a door when it ENTERS the door's tile (the leader 180 before the door's
middle), where DD1 waits for the door to be used; the walk into the door then covers the rest.

**The walk** (`CorridorView.WalkStyle`; DD2's models have no walk cycle): 0 glide in the idle pose; 1 a staggered
bob; **2 (default) the bob with a sway from foot to foot (`SwayDegrees` 2.5) and a lean into the walk
(`LeanDegrees` 3)**; 3 the game's own step for a change of rank (the animator's `move_forward` /
`move_backward` at the start, `move_complete` at the stop) with the bob. Walking back the camera goes to DD1's
other offset and zoom (3.2, stage 2); DD1's heroes back away facing forward: `CorridorView.KeepFacing` holds
each model in the rotation and scale it was made with.

### Stage 5: the timescripts, the steady cam, housekeeping

Files: `CorridorScript.cs` (the script reader `TimeScript`, the settings `CorridorScript`, and the playing as a
part of `CorridorView`), `CorridorPost.cs` (the scene's second volume), `CorridorView.cs`.

**Timescripts.** DD1's own files are read at runtime and played (9.1); a property the mod cannot show is
carried but does nothing. Hooks for the expedition, all doing nothing when the view is off, when
`CorridorScript.Enabled` is false or `CorridorTransition.TimeScale` is 0 (which also scales their time):
`CorridorView.Investigate(PropKind, heroGuid)` (investigate_intro, or disarm_intro for a trap; returns at
`open_lambda`), `EndInteraction()` (the extro; a moment left open closes itself when the party walks on or after
`AutoEnd` 6 s), `TrapSprung(heroGuid)` (trap.times), `Disarm(heroGuid)` (intro and extro).

| Property | In the mod |
|---|---|
| `camera.zoom`, `absolute_focus` x / y with their blends | added to the camera's zoom; the camera pulled to the point (`PlaceCamera`) |
| `camera.shudder` | the camera shaken by x pixels of DD1's view at y cycles a second |
| `camera.foreground_active` | `CorridorScene.ForegroundAlpha`, faded with DD1's 0.2 / 0.4 s |
| `layers.A_saturation` / `A_intensity` / `A_blurriness` | a second global volume (`DD2Estate.CorridorSceneVolume`) on a layer only the MAIN camera listens to (found at runtime: in its volume mask and in no other camera's of the stack): `ColorAdjustments` saturation and exposure, and the pipeline's gaussian `DepthOfField` as the blur. The main camera draws walls, floor and props; nothing of them writes depth, so all of it blurs alike, and the heroes, drawn after, stay sharp |
| `layers.B_intensity` | `CorridorLight.HeroBoost` on every hero's light |
| `presentation_camera.zoom` / `.blend` | the acting hero and the script's curio are scaled about the view's middle by the presentation camera's size over the scene camera's (`CorridorScript.Presentation`): DD1's close-up, feet below the view |
| `curio.zlayer` | the curio's meshes go to the heroes' layer, so the main camera's blur leaves it out |
| `actor.area_pos,x` | the acting hero's place, from the party's line to `centre - offset * 0.6 .. 0.5` (`centre`: the camera's x, `offset`: `actor_spacing`) |
| `layers.HUD_alpha` | NOT applied (the HUD is not the corridor's): `CorridorView.HudAlpha` gives it to whoever wants it |
| `camera.tilt`, `actor.scale`, `lock`, `hero_prep` | nothing (0 in these scripts; DD2's models have no investigating pose) |

**Steady cam** (3.5): `CorridorScript.Drift` (on), `DriftStrength` (1 = DD1), the seven numbers from
`camera.darkest`. The far layers are 160 px wider than the view so the turn never shows their edge.

**Housekeeping.** The hero models now live from area to area (`Build(keepParty)`): they are made anew only when
the party's living heroes or their order changed, after a fight (`Suspend` destroys them, as before, because a
fight makes its own) and by `corridor.rebuild`. `CorridorView.Hide` (the expedition is over) lets go of: the
hero models, the scene's meshes, the props and their Spine atlases (`CorridorProps.ReleaseArt`), both volumes,
the grade's texture and DD1's five tables (`CorridorPost.ReleaseAll`), a running transition or script; and by
switching the view off: the main camera's rectangle goes back, the mod's overlay camera is destroyed and taken
out of the stack, the corridor's canvas is hidden. What stays for the session: the view's object with its
virtual camera and its canvas (hidden), the two volume profiles (empty of textures), the wall and layer
textures (`Dd1Install`'s cache, not the corridor's), the parsed timescripts and numbers.

Bridge (`CorridorDev.cs`): `corridor.state` (camera in DD1 units and as the game's camera has it, pixels per
unit at each depth, the light's three colours and stops, layers, each hero's body height in DD1 units and its
box on a 1920x1080 screen, the foreground camera's status), `corridor.torch level=N` (-1: the expedition's),
`corridor.place tile=N | x=X`, `corridor.camera back=0..1` (-1: live), `corridor.light [term=bool ...]` (the
switches of stage 3; without arguments: the light's state, the grade's status and what it makes of a mid grey),
`corridor.transition [walk=true] [run=false timescale=N enabled= walkin= depth= cameraBlend=]` (the sequence on
the area on screen, or its settings), `corridor.style [walk=0..3 keepFacing= gradient= opacity= ...]`,
`corridor.investigate [hero=N kind=curio|obstacle] [end=true] [run=false enabled= blur= presentation= offset=]`,
`corridor.trap [hero=N] [disarm=true]`, `corridor.drift [enabled= strength=]`;
`corridor.props` now also gives each prop's `box1080` and tint. Props: `corridor.pose` is the prop animation command of old; the party is placed with
`corridor.place`. Any number: set the static field through the bridge, then `corridor.rebuild`.

## 14. The reference renderer

`python tools/dd1_corridor_reference.py [--dungeon crypts|weald|warrens|cove] [--torch 0..100] [--leader X]
[--back 0..1] [--room NAME] [--heroes a,b,c,d] [--curio ID] [--trap ID] [--obstacle ID] [--boxes] [--no-post]
[--all] [--json]` writes `_lab/preview/dd1_reference_{hallway,door,room,back,torch}_*.png` and prints every
element's world position, pixels per unit and box on screen. `--boxes` outlines heroes and props and marks the
horizon; `--no-post` leaves out grade and grain; `--all` adds the walking-back camera and the five torch
levels. It reuses the Spine reader of `tools/preview_corridor.py` (the one checked against the plugin's).
`tools/preview_corridor.py` itself still draws the mod's OLD placement and is superseded when stage 2 lands.

## 15. What still differs from DD1 in the corridor, and why

| DD1 | The mod | Why |
|---|---|---|
| Heroes are 2D sprites with a walk cycle, an investigating pose and DD1's proportions | DD2's 3D models, scaled to DD1's height; a stand-in for the walk (`WalkStyle`); no investigating pose | the mod's premise: DD2's heroes. DD2 has no walk animation |
| Each hero pixel is lit by the box light (feet darker) | one brightness per hero (`_Brightness`), optional shader gradient (`HeroGradient`, untuned) | no shader can be compiled at runtime; the hero shader's properties are what there is |
| A prop is lit per pixel by the ramp | one colour per prop (the ramp at its foot) | the prop's meshes are re-posed from Spine each frame; per-vertex would do it, at a cost not worth the difference (a prop is 250 wide against stops 384 apart) |
| The light multiplies stored colours | vertex colours with exponent `Gamma` 1.45 in a linear pipeline | exact only per art brightness (5.2); measured within a few per cent on the dungeon art |
| A door is used (click, W) and then walked into | walking into the door's tile sends the party through; the walk-in covers the last steps | the expedition's rules step on tile entry; changing that is the expedition's (`DungeonRun`) |
| Leaving a room: [not established] | the two fades | the executable's walk is into a hallway's door prop; a room has none |
| Far background and midground: tiled, scrolled by camera x * 0.5 / 0.25, in a space not established | the same factors, at the view's own size on the horizon | 12.3 |
| Grain: a new white noise every frame, brightening only | the pipeline's film grain (signed, textured) plus the mean in the grade | the pipeline's effect is what there is; the mean is exact |
| Blur: a 5-tap gaussian mixed by `Amount` 3..5 | the pipeline's gaussian depth of field at radius 0.9..1.5 | the same reason |
| Panel fades out while a hero investigates | stays (`CorridorView.HudAlpha` is there to read) | the questions and the loot scroll are on that panel in the mod |
| `centre`, `offset` of the timescripts | camera x, `actor_spacing` | not recovered (9.3) |
| Composite gamma (the player's brightness), death's door pulse on the models, quirk / stealth / flashback grades, the Darkest Dungeon's animated corridor, the starfield and heart rooms, the teleporter | not done | outside the corridor's daily picture; the generator makes no teleporter |
| Steady cam's turn uses the push as its angle (an oddity of the executable) | turns by the turn rate | the intended reading |
| Fight in a hallway: camera to the battle offset, zoom 1 | n/a | fights are DD2's own scene |
