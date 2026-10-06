# What the party walks past: corridor props, scouting on the map, secret rooms

> **Superseded in part (corridor rebuild, stage 2).** Sections 2, 3, 10 and 11 describe the placement of the
> first corridor. The props now stand as DD1 stands them, in a scene of DD1's own geometry: see
> `docs/recon/dd1-corridor-rendering.md` (4.5 and 13). What changed against this document: props are on the
> floor at their true depth (no longer moved towards the camera to fake their size); an obstacle's depth is
> `curio_z_position` 75 (DD1's executable reuses it), not 0; the foreground strips ARE drawn, by an overlay
> camera after the heroes; `CorridorProps.Lift` and `StandOff` are in DD1 units (stand-off 200);
> `tools/preview_corridor.py` draws the old placement and is kept only for its Spine reader, which
> `tools/dd1_corridor_reference.py` uses. Sections 1 and 4-9 stand.

Status: written, compiled (0 errors), the rules unit-tested in the core (9 new tests), the Spine reader checked
number by number against a Python reference on all 208 DD1 skeletons, the placement drawn offline
(`tools/preview_corridor.py`). **Not run in the game** (section 12 lists what that leaves open), and
`Dungeon/DungeonRun.cs` and `Dungeon/Minimap.cs` still need the snippets of section 9: until then the view is not
told which hallway it shows, and draws no props. DD1 paths are relative to the DD1 install.

## 1. DD1: where the art is

All of it is Spine 2.1.27: a binary `.skel`, a libgdx `.atlas`, one `.png` page. 88 skeletons under `props/`:
70 curios, 4 traps, 5 obstacles, 9 doors.

| What | Which skeleton | Named by |
|---|---|---|
| Curio, treasure, quest curio | `props/shared/curios/<sprite>/<sprite>.skel` | `curios/curio_props.csv`: prop name -> "Sprite Reference" (`locked_strongbox` is drawn as `heirloom_chest`); a quest curio (`reliquary`, `beacon`, `shipment_crates`...) has no row and is drawn under its own name |
| Trap | `props/shared/traps/<id>/` | `props/trap_definitions.json`, `graphics_file` of the prop (`spikes_ambush` uses the spikes' art) |
| Obstacle | `props/shared/obstacles/<id>/` | `props/obstacle_definitions.json`, `graphics_file` |
| Hallway door | `props/<dungeon>/doors/door0/`; the Darkest Dungeon: `props/darkestdungeon/quest_<n>/doors/door0/` | `props/<dungeon>/prop_definitions.json`, prop `<dungeon>_door` (`darkestdungeon_quest_<n>_door`) |
| End of a hallway | `dungeons/<d>/<d>.endhall.01.png` (720x720, drawn for the left end) | - |
| Secret room | `dungeons/_shared/secretroom.png` (1920x720, one for all dungeons) | - |
| Which prop a dungeon places | `dungeons/<d>/<d>.props.darkest`: `hall_curios`, `room_curios`, `room_treasures`, `traps`, `obstacles`, `secret_room_treasures` | read by `Core/GenerationRules.cs` |

Every id of those tables, and each of the 11 quest curios of `campaign/quest/quest.types.json`, has a skeleton in
this install (checked for crypts, weald, warrens, cove, darkestdungeon, town).

How the skeletons are made (it decides what the reader has to do):

* **The attachments are meshes, not regions** (one curio, the sconce, is regions; the dinner cart mixes). The art
  is packed at twice its drawn size and the mesh's vertices give the place and the size: the chest's `closed`
  region is 413x241, its mesh draws it 206x120. Five skeletons use **skinned meshes** (vertices weighted to bones):
  the thorny thicket (all three of its attachments), the ancestor obstacle, the lurker trap, the open grave's
  ghost, one Darkest Dungeon door.
* **A prop's states are animations that fade slots in and out**, not different skeletons. A curio has the slots
  `active`, `closed`, `open`, all attached in the setup pose; `idle` sets `closed` to alpha 1 and the others to 0,
  `investigate` shows `open`, `active` shows `closed` with `active` (a light rim, drawn behind it) pulsing between
  white and grey once a second. The setup pose itself shows all three on top of each other.
* Traps: `idle` (the plate), `active` (the plate with a red rim), `sprung` (2.2 s: spikes shoot up, dust).
  Obstacles: `idle`, `clear` (1-2 s: the pieces fall and fade to nothing). Doors: `closed`, `open`.
* The origin is the middle of the prop's foot; no atlas region is whitespace-stripped; no slot is additive; every
  atlas has one page.
* `props/prop_definitions.json` says which animation is which state: a curio's "investigated" is `investigate`
  (`curio_default.animation_remap_table`), a door's "investigate" and "investigated" are `open`, a trap's default
  is `sprung`, an obstacle's `idle`.

## 2. DD1: where a prop stands

`scripts/world.darkest`, block `world_parameters`:

| Key | Value | Meaning |
|---|---|---|
| `curio_tile_centre_x_offset` | 135 | a hallway curio stands 135 px right of its tile's middle |
| `curio_tile_centre_x_offset_room` | 75 | a room's curio: 75 px right of the room's middle |
| `curio_z_position` | 75 | 75 behind the heroes |
| `trap_tile_centre_x_offset`, `trap_z_position` | 0, 15 | a trap: the tile's middle, 15 behind the heroes |
| `obstacle_tile_centre_x_offset` | 75 | an obstacle: 75 px right of the middle; **no depth is given** (taken as 0: where the heroes walk) |
| `distance_to_interior_background` | 400 | the wall art is 400 behind the heroes (`distance_to_midground` 1000, `distance_to_farbackground` 1500, `distance_to_interior_foreground` -300) |

`scripts/camera.darkest`: `m_CorridorCameraParameters.OffsetFromPartyLeader 80 270 -1400` (the hallway camera is
270 above the floor and 1400 in front of the heroes), `m_RoomCameraParameters.Position 960 300 -1240`.

DD1's dungeon is a 3D scene, so two things follow for a prop that stands `z` behind the heroes
(`Core/ExplorationProps.cs`, `PropPlacement.Place`; D = 1400 or 1240, H = 270 or 300, W = 400):

* its **size against the wall art** behind it: `(D + W) / (D + z)`: curio 1.220, trap 1.272, obstacle 1.286, door 1;
* its **foot above the heroes' feet**, in wall pixels: `H * (1/D - 1/(D + z)) * (D + W)`: trap 3.7, curio 17.6,
  door 77.1 (the floor's whole rise from the heroes to the wall: `H * W / D`).

The check that this reading is right: with the party's feet where the mod's corridor already had them
(`CorridorView.FloorHeight`, 61 px above the tile's bottom edge on the wall), a door put 77 px higher sits in the
doorway painted on `corridor_door.basic.png` in all five dungeons, the pointed crypt door as well as the weald's
round gate and the Darkest Dungeon's wheel (`_lab/preview/corridor_<dungeon>.png`).

## 3. In the mod's corridor (`Dungeon/CorridorProps.cs`, `CorridorPropsArt.cs`)

The corridor draws the wall tile 1:1 (720 px = 4.4 units) under a camera of its own. A prop is put **nearer that
camera by as much as DD1 draws it larger than the wall** (`z = -D' * (1 - 1/size)`, D' the camera's distance from
the wall), at the height whose picture on the wall is DD1's rise above the heroes' feet, at the scale of the wall
art. So it has DD1's size without a scale of its own, and it slides against the wall as the camera moves the way
DD1's props do. The heroes are drawn over everything by their own overlay camera (gotcha 13), so a prop "in
front" of their plane hides nothing.

* Drawn as meshes, one `MeshRenderer` per run of pieces that share a texture (a region packed rotated has a
  texture of its own in `SpineAtlas`), with the material a `SpriteRenderer` gets and the texture set per renderer;
  ordered among the wall sprites by sorting order: doors 0+, curios 1000+, traps 2000+, obstacles 3000+, forty
  orders a tile.
* **What stands where and in which state is the rules' to say** (`Core/ExplorationProps.cs`, section 4). Every
  frame the props are set to it; nothing is remembered in the view. A loaded game, a scouted hallway, a trap that
  moves into a walked hallway all look right without the expedition telling the view.
* A state that is reached once (`investigate`, `sprung`, `clear`, `open`) is played when it happens in front of
  the player and shown at its last moment when the area is built in it. The others loop for as long as they last
  (the holy fountain's water, the thicket's sway, the glow of a curio within reach).
* **Hallway ends**: DD1's `endhall.01` closes the wall one tile beyond each door (mirrored on the right), and the
  camera may now look that far past the ends instead of stopping at the door tile's edge.
* **The pointer** reaches the strip through a clear UI panel of its own (canvas `DD2Estate.CorridorInput`, order
  3, under the HUD's 4), so whatever lies over the strip keeps its clicks. A click on a prop walks the party up
  to it (`CorridorView.Click`; the leader stops `StandOff` = 1.3 units before it, a door is walked into); what
  the tiles on the way hold stops the walk as it stops any other. On arrival `CorridorProps.Clicked` is raised for
  a curio or a hidden door. A prop is hit where its art is opaque (the atlas's alpha mask). A press on bare wall
  ahead of or behind the party walks it while held (DD1: "click ahead or behind the party to move them";
  `CorridorView.MouseWalk`). The pointer over a curio or a scouted trap lights it up as standing at it does.
* A secret room is left by `A` / left arrow, by a click on its left 15%, or by a click on any room of the map
  (snippet D5).

## 4. Rules of sight (`Core/ExplorationProps.cs`, tested in `tests/CoreTests/PropTests.cs`)

| Prop | State (DD1 animation) | When |
|---|---|---|
| Curio in a hallway | `idle` | always there to see, as in DD1 |
| | `active` | the party stands on its tile and it is unused (DD1 lights up what can be turned to) |
| | `investigate` | used |
| Curio or treasure in a room | `idle` / `active` / `investigate` | `active` once the party is in the room and the room's fight is won |
| Trap | not seen | until its tile is scouted (DD1: "Scouted traps will be visible on the ground as you approach them") |
| | `idle` | scouted; also a disarmed one (**assumed**: DD1 has no animation for "disarmed") |
| | `active` | the party stands at it, undecided |
| | `sprung` | it went off, spotted or not (new mark in the save: `Exploration.IsTrapSprung`) |
| Obstacle | `idle`, then `clear` | cleared |
| Door | `closed`, then `open` | the party has been in the door's room and on the tile next to it (DD1: a used door stays "investigated") |
| Hidden door | nothing is drawn | once known, the wall of its tile takes the click; DD1 marks it on the map only |
| Fight, hunger | nothing | DD1 shows neither before it happens; a scout puts them on the map |

A hallway is laid out the way it is walked: tile 0 the door behind, 1..n its tiles, n + 1 the door ahead; walked
the other way the same tiles come in the other order (DD1's maps carry a `reversed` flag for the same purpose).

## 5. The Spine reader (`Dd1/SpineSkeleton.cs`, `Dd1/SpineAtlas.cs`)

`SpineSkeleton` used to read the setup pose's region attachments (enough for the hamlet). It now reads the whole
file: bones with their flip and inherit flags, slots, the default skin's region / mesh / skinned mesh attachments,
and the animations: slot colour and attachment keys, bone rotate / translate / scale keys with linear, stepped
and bezier curves (Spine's ten-segment walk), mesh deformation keys, draw order keys. `Pose(animation, time,
atlas, pieces)` gives what is drawn at that moment as triangles (vertices from the skeleton's origin, UVs over
the upright atlas region). `Parts`, which the hamlet uses, is unchanged; a skeleton whose tail cannot be read
keeps its setup pose. `SpineAtlas.Region` gained `Texture` and `TextureRect`.

Not read: named skins (none is drawn), events (sound cues), IK keys (the open grave's ghost has one constraint)
and flip keys (no prop has any).

`python tools/check_spine.py` builds `tests/SpineCheck` (the plugin's own `SpineSkeleton.cs` against stand-ins
for Unity), writes every pose of every skeleton under `props/` and `fx/` (setup, and each animation at 0, 31%,
77% and 100% of its length) and compares with the Python reader of `tools/preview_corridor.py`: 1436 poses, 5256
pieces, largest vertex difference 0.0007 px; the hamlet's 401 setup parts equal the hamlet's own reference reader.

## 6. Scouting

Rules (already in `Core/Exploration.cs`, unchanged): `shared/rules.json` `scouting_chance_base` 0.25, plus the
torch band's `player_scouting_increase` (+15 / +7.5 / 0 / 0 / 0 points), plus the party's own
(`Exploration.ScoutBonus`: camping skills); rolled on entering a room for the first time
(`scouting_enter_dungeon_scout_chance` is 0); `scouting_crit_success` 0.5. What a scout shows is **not in the
data**; the reading kept: the hallways out of the room and the rooms behind them, a critical one a room further
and the hidden doors (DD1's own tutorial: "A critical scouting success will sometimes reveal a Secret Room!").
Curios scout by their library rows (`N - target`).

New: `Exploration.ScoutChance` (the chance as rolled, for a HUD or the bridge); the `Scouted` event names its
tiles (`Tiles`: hallway, segment, content), so a presentation can show them one by one.

What DD1 shows, and how `Dungeon/Scouting.cs` words it for the map:

| On the map | DD1 | `Scouting` |
|---|---|---|
| Room | `panels/icons_map/room_unknown` until known, then `room_entrance` / `battle` / `curio` / `treasure` / `boss` / `empty`; `marker_room_visited` over a room the party has been in | `RoomIcon`, `RoomVisited`, `RoomVisitedMarker` |
| Room tooltip | `str_map_ac_battle_tooltip` "Battle", `str_map_ac_curio_tooltip`, `str_map_treasure_tooltip`, `str_map_ac_guarded_curio_tooltip` "Room Battle with Curio", `..._guarded_treasure_...`, `str_map_boss_tooltip`, `str_map_quest_location_tooltip` | `RoomTooltip` (what still waits there; null for an unknown or finished room) |
| Hallway tile | `hall_dark` (unknown), `hall_dim` (seen by a scout), `hall_clear` (walked) | `TileIcon` |
| Tile content | `marker_battle` / `curio` / `trap` / `obstacle` / `hunger` / `secret` (the star) | `TileMarker`: while it is known and not dealt with; the star until the stash behind it is opened |
| Tile tooltip | `str_map_ac_trap_tooltip` "Trap", `..._obstacle_...`, `..._hidden_door_...` "Secret Door"... | `TileTooltip` (DD1 has no string for hunger: the hunger prompt's title is used) |
| A scout's finds coming up | `scripts/layout/panel.map.darkest`, `fog_of_war`: `time_between_reveals` 0.4, `maximum_total_reveal_time` 2.0, `tile_reveal_time` 0.4, `tile_content_reveal_time` 0.4, `tile_content_reveal_max_scale` 1.3, `scouting_text_scale_time` 0.25; banner `panels/icons_map/scoutingbanner.png` (366x63) with `str_scouting` "Scouting" | `Report(x, scouted)` starts it; until an item's turn has come the questions above still answer "unknown" for it; `Revealing`, `MarkerScale`, `RoomScale`, `TileReveal`, `RoomReveal`, `Banner`, `BannerAlpha`, `BannerTextScale`, `BannerText`; `Summary` is a line for the log ("Scouting: 2 x Battle, Curio, Trap.") |

In the corridor a scouted trap lies on the floor of its tile from the moment it is known (section 4).

## 7. Secret rooms

DD1's files: `scripts/map_generator.darkest` `.secret_rooms 0 0` (short quests) or `0 1`;
`dungeons/<d>/<d>.props.darkest` `secret_room_treasures: secret_stash`; `curios/curio_type_library.csv` block 52:
`secret_stash` ("Ancient Artifact"), Loot 100%: table `B` x 3 draws; item `skeleton_key` -> Loot `COLLECTOR` x 3;
art `dungeons/_shared/secretroom.png`, map marker `marker_secret.png`, tooltip "Secret Door". DD1's rule in its
own words (`tutorial_popup_scout_hidden_door_description`): "A critical scouting success will sometimes reveal a
Secret Room! Advance to the tile marked with a star, and press 'W' or click to enter. Fabulous wealth and riches
await those equipped with a key..."

The core had most of it (the generator hangs the room off a hallway tile; `SecretRoomFound` stops the party at
a known door once; `EnterSecretRoom`, `LeaveSecretRoom`, `InSecretRoom`; the stash is rolled like any curio).
Added:

* `Exploration.SecretRoomHere`: the secret room behind the tile the party stands on, once known;
* `Exploration.Examine()`: the party turns again to what stands where it is, any number of times: the curio it
  left alone (`CurioFound` again), the hidden door it walked on from (`SecretRoomFound` again). Nothing is rolled;
* `DungeonMap.FindSecretDoor(roomId, out hallway, out segment)`;
* the view: the secret room's own wall (`CorridorProps.SecretRoomWall`, a tile name with a folder in it is a DD1
  path for `CorridorView.ShowArea`), its stash at DD1's place in a room, the way out.

Flow with the snippets: a critical scout puts the star on the map -> the party reaches the tile -> "A secret
door." Enter / Walk on (or later: a click on that tile's wall walks the party there and goes in) -> the room,
the stash's question with "Use skeleton key" when the bag holds one -> `A`, a click on the room's left side or on
the map -> back on the hallway tile. A save inside the room loads inside the room.

Tests (`tests/CoreTests/ExplorationTests.cs`): `ExamineTurnsBackToWhatWasLeft`, `SecretRoomHoldsWhatDd1Says` (DD1's
library rows and loot tables; 160 generated maps: none in a short quest, at most one otherwise, each walked to,
entered, opened with a key and left), next to the older `SecretRoomNeedsScouting`.

## 8. Dev bridge (`Dungeon/CorridorPropsDev.cs`)

| Command | Does |
|---|---|
| `corridor.props` | what stands in the area on screen: kind, id, tile, state, animation, skeleton, mesh count, shader, foot, pixel bounds and the screen point to click; DD1's placement numbers; `scale=` `lift=` `standoff=` tune live, `rebuild=true` builds anew |
| `corridor.click index=N` or `kind=curio [tile=N]` | the click: the party walks up to the prop, then `CorridorProps.Clicked` (kinds: curio, trap, obstacle, door, secretdoor, exit) |
| `corridor.show kind=obstacle id=thorny_thicket tile=2 [animation=idle] [play=true]` | stands any DD1 prop in a tile to look at its art (gone at the next rebuild) |
| `corridor.pose index=N animation=sprung [play=true]` | a prop in an animation of the tester's choosing |
| `dungeon.scouting` | the chance and its parts, the reveal's timings, every known room and tile as the map words it |
| `dungeon.scout [reach=1] [target=all] [secret=true]` | a scout as the rules make one (reach 0: the whole map), with the reveal |
| `dungeon.secret [action=state|reveal|enter|leave]` | where the secret rooms are; show their doors; click the door of the hallway on screen; go back out |

## 9. Snippets for the files that are not this task's

They were applied to a copy of the project, which builds with 0 errors (the real files are untouched).

### `Dungeon/DungeonRun.cs`

D1. `Dispose()`: after `if (view != null) view.TileEntered -= OnTileEntered;`

```csharp
            CorridorProps.Clicked -= OnPropClicked;
```

and after `DungeonHud.Clear();` in the same method: `Scouting.Clear();`

D2. `SyncView()`: after `view.TileEntered += OnTileEntered;`

```csharp
            CorridorProps.Clicked -= OnPropClicked;
            CorridorProps.Clicked += OnPropClicked;
```

and after `view.ShowArea(_map.DungeonId, new[] { RoomWall(room) }, isRoom: true, startTile: 0);`

```csharp
                    CorridorProps.ShowRoom(_x, room);
```

D3. `RoomWall(Room room)`: first lines

```csharp
            // DD1's secret room is one picture for every dungeon
            if (room.Type == RoomType.Secret) return CorridorProps.SecretRoomWall;
```

D4. `ShowHallway(...)`: after `CorridorView.Ensure().ShowArea(_map.DungeonId, tiles, isRoom: false, startTile: startTile);`

```csharp
            CorridorProps.ShowHallway(_x, hall, target);
```

D5. `TravelTo(int roomId)`: after its first line (`if (Busy || ...) return;`)

```csharp
            // a secret room has no hallway of its own: any room clicked on the map leads back out to the hallway it opens from
            if (_x.InSecretRoom)
            {
                LeaveSecretRoom();
                return;
            }
```

D6. two new methods, before `OnTileEntered`

```csharp
        /// <summary>The player turned to something the party stands at (a click in the corridor: the view has walked the party up to it).</summary>
        private void OnPropClicked(CorridorProp prop)
        {
            if (Current != this || prop == null || Busy || !EstateSession.InHub || _x.Status != RaidStatus.InProgress) return;
            if (prop.Kind == PropKind.Exit) LeaveSecretRoom();
            // DD1: "advance to the tile marked with a star, and press W or click to enter"
            else if (prop.Kind == PropKind.SecretDoor) Enqueue(_x.EnterSecretRoom());
            // the curio the party left alone: the same question again
            else Enqueue(_x.Examine());
        }

        private void LeaveSecretRoom()
        {
            if (!_x.LeaveSecretRoom()) return;
            Say("The party slips back into the hallway.");
            SyncView();
        }
```

D7. `Process()`: the two cases `case Scouted _:` and `case SecretRoomFound _:` become

```csharp
                    case Scouted scouted:
                        // DD1 brings a scout's finds up on the map one after another under its banner; a scouted
                        // trap shows on the hallway's floor by itself (CorridorProps follows the rules)
                        Scouting.Report(_x, scouted);
                        Say(Scouting.Summary(_x, scouted));
                        break;
                    case SecretRoomFound _:
                        yield return Ask("A secret door.", "Enter", "Walk on");
                        if (_answer == 0) Add(_x.EnterSecretRoom());
                        break;
```

`RoomEntered` for the secret room goes through the existing case (SyncView, save); the stash's `CurioFound`
through `Investigate`, which already offers the items the curio answers to (the skeleton key).

### `Dungeon/Minimap.cs` (as it is now, after its restyle)

M1. fields: `private Image _banner; private TMPro.TextMeshProUGUI _bannerText;`

M2. `Show()`: before its last line `Refresh();`

```csharp
            if (_banner == null)
            {
                // DD1's banner over the map while a scout's finds come up (panels/icons_map/scoutingbanner.png, 366x63);
                // it hangs in the window, not on the map that slides under it
                _banner = UiKit.Image("Scouting", _window, Dd1Install.Sprite(Scouting.Banner));
                ((RectTransform)_banner.transform).Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(366f, 63f));
                _bannerText = UiKit.Text("Word", _banner.transform, Scouting.BannerText, 26f, UiKit.Parchment);
                UiKit.Stretch((RectTransform)_bannerText.transform);
                _banner.gameObject.SetActive(false);
            }
```

M3. `Update()`: `if (Time.unscaledTime >= _nextRefresh)` becomes
`if (Time.unscaledTime >= _nextRefresh || Scouting.Revealing)` (the finds come up frame by frame).

M4. `Refresh()`: the two loops over rooms and hallways become

```csharp
            // Scouting words the rules' knowledge in DD1's icons and holds back what a scout has found until
            // its turn in the reveal has come.
            for (var i = 0; i < map.Rooms.Count; i++)
            {
                var sprite = Dd1Install.Sprite(Scouting.RoomIcon(_x, i));
                if (_rooms[i].sprite != sprite) _rooms[i].sprite = sprite;
                _rooms[i].transform.localScale = Vector3.one * Scouting.RoomScale(i);
                _visited[i].enabled = Scouting.RoomVisited(_x, i);
            }
            for (var h = 0; h < map.Hallways.Count; h++)
                for (var s = 0; s < map.Hallways[h].Segments.Count; s++)
                {
                    var tile = Dd1Install.Sprite(Scouting.TileIcon(_x, h, s));
                    if (_tiles[h][s].sprite != tile) _tiles[h][s].sprite = tile;
                    var marker = Scouting.TileMarker(_x, h, s);
                    _markers[h][s].enabled = marker != null;
                    if (marker == null) continue;
                    _markers[h][s].sprite = Dd1Install.Sprite(marker);
                    _markers[h][s].transform.localScale = Vector3.one * Scouting.MarkerScale(h, s);
                }

            var banner = Scouting.BannerAlpha;
            _banner.gameObject.SetActive(banner > 0f);
            if (banner > 0f)
            {
                _banner.color = new Color(1f, 1f, 1f, banner);
                _bannerText.alpha = banner;
                _bannerText.transform.localScale = Vector3.one * Scouting.BannerTextScale;
            }
```

M5. `RoomHovered()`: `var room = ...; var what = _x.IsRoomKnown(roomId) ? RoomName(room) : null;` becomes
`var what = Scouting.RoomTooltip(_x, roomId);` (then `RoomIcon`, `RoomName` and `Marker` of the file are unused).
For a tile's hint: `Scouting.TileTooltip(_x, hallwayId, segment)`.

What changes on the map: the star of a hidden door stays after the party's first stop at it (today it goes with
the tile's "done" mark) until the stash is opened; a scout's finds pop up one by one; a room's tooltip says what
still waits there.

## 10. The mod's own calls (numbers and choices that are not DD1's)

* An obstacle's depth (0: on the heroes' line) and a door's (in the wall): `world.darkest` gives neither.
* A disarmed trap stays on the floor as its `idle` plate.
* A door is open once the party has been in its room and on the tile next to it.
* `CorridorProps.StandOff` 1.3 units; the way out of a secret room: its left 15% (`ExitShare`), `A` / left arrow.
* `Scouting.BannerHold` 1.2 s and `BannerFade` 0.4 s (DD1's file gives the reveal's timings, not the banner's
  stay); the order of a reveal: nearest first, a hallway's tiles before the room behind it.
* The star stays on the map until the stash is opened.
* `CorridorProps.Scale` (1) and `Lift` (0 px) are there to tune by eye in the game; the preview needed neither.

## 11. Left out, and why

* **The foreground strips** (`<d>.foreground_top.01.png`, `foreground_bottom.01.png`, DD1's layer 300 in front of
  the heroes): the heroes are drawn last by their overlay camera, so a sprite cannot be put in front of them.
* **DD1's colour grade by torchlight** (`colour_grade_0..4.png`) on walls and props.
* **Prop sounds** (`audio_path` of the definitions): DD1's banks are FMOD v103 (journal, gap 6).
* **IK and flip keys** of the Spine reader (the open grave's ghost stands a little stiff; no prop flips).
* **A picture for the hidden door and for a waiting fight**: DD1 has none either.
* **Prison doors, locked rooms, the prisoner, the teleporter, the `ancestor` obstacle, `spikes_ambush`**: the
  generator places none of them (their art would be found by the same lookups).
* **DD1's curio tracker** (`panels/icons_curio_tracker`: what an item did to a curio last time) and **disarming
  by clicking the trap with a hero chosen**: the HUD's questions stand in for both.
* **Gamepad** for the corridor's pointer.

## 12. Not verified without the game

1. That the props are drawn at all: meshes with the sprite material and a per-renderer `_MainTex` under the
   game's URP renderer, ordered after the wall sprites by sorting order (`corridor.props` reports shader, mesh
   count and screen point of every prop).
2. Size and footing against the DD2 hero models (`corridor.props scale= lift=`); the preview's party is grey boxes.
3. The pointer strip: that clicks and hover reach it under the HUD (canvas order 3) and that nothing of the game's
   own UI lies over the strip; prop picking by alpha mask; hold-to-walk.
4. The end caps and the camera looking one tile past each end.
5. Played animations in motion: `sprung`, `clear`, a curio's `active` glow, the Darkest Dungeon's doors, the
   thicket's skinned meshes (their poses are checked numerically, section 5).
6. The whole secret room flow with snippets D1-D7: star on the map, the question at the tile, the room's art,
   the stash with and without a key, the three ways out, a save inside the room.
7. The reveal on the map with snippets M1-M5: timing, pops, banner; the star staying after the first stop.
8. Atlases being released when the expedition ends (`CorridorView.Hide`) and loaded again for the next one.
9. Unspotted traps springing in front of the player (hidden prop -> `sprung`), and traps that move into a hallway
   walked before.
