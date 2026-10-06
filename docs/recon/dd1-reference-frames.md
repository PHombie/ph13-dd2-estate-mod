# Reference frames from the real Darkest Dungeon (1)

Taken on 2026-10-05 from the Steam build (28063) driven by `tools/dd1_drive.py` / `_lab/dd1_ref/ref.py`, window
1920x1080 (one screen pixel per art pixel). Estate **DD2REF**, 9th row of the estate list (`profile_8`), mode
Darkest, no DLC, mod "DD2 Reference Test Estate". Everything is under `_lab/dd1_ref/`:
`hallway/` (stills, 30 fps recordings `rec*.mp4`, frames `rec*_tNNNN.png` = N/100 s into the recording, contact
sheets `rec*_sheet.png`, `rec*.txt` = when each key was sent), `raid/`, `town/`.

Tools written for this (in `_lab/dd1_ref/`): `ref.py` (input + picture, with the guard described in section 0),
`rec.py` (record while keys are sent), `measure.py` (per frame: where a wall tile stands, brightness, the
selected hero's bracket), `track.py` (follows the door tile through a door transition while the camera pushes
in), `frames.py` (frames and contact sheets out of a recording).

## 0. Driving DD1 on this machine: what had to be found out

* **DD2 is a borderless TOPMOST window over the whole 2560x1440 screen.** DD1's window (client 1920x1080 at
  320,180) lies under it, so a mouse click "on DD1" lands on DD2 even when DD1 is the foreground window (keys do
  go to DD1). `ref.py` therefore makes DD1's window topmost as well (`SetWindowPos HWND_TOPMOST`), checks before
  every mouse command that the window under the point really is DD1's, and refuses the command otherwise.
  `python _lab/dd1_ref/ref.py x --untop` puts DD1's window back into the ordinary band.
  **Incident:** before this was understood, the first three attempts to click "Campaign" sent a left-button
  press to DD2 at screen (1280,1124), an empty spot inside its Ancestor's Memoirs window (the release was
  refused by WinDrive, so no click was completed there; the button was released later over DD1).
* Left behind: DD1's window is back in the ordinary band (`--untop` was run at the end, so DD2 covers it again
  as before); DD1's own window thread uses the English layout until DD1 is restarted; the mod folder
  `<DD1>\mods\dd2ref_test_estate` is installed; Steam Cloud will carry `profile_8`.
* DD2 keeps reading the pointer while DD1 covers it: it shows hover labels under wherever the pointer is over DD1.
* **Typing the estate's name**: WinDrive's `type` (Unicode input) does nothing in DD1, and with the Russian
  layout active letter keys give nothing visible (the font has no Cyrillic: the owner's estates show as blank /
  "2" / "3"). The DD1 window was asked to switch to the English layout (`WM_INPUTLANGCHANGEREQUEST`,
  0x04090409), then plain key presses with Shift typed `DD2REF`.
* Creating an estate, as it went: click the letter of an empty row -> "Activate DLC" (every DLC ticked by
  default: ten boxes, all were unticked) -> "Select Campaign" (Radiant / Darkest / Stygian) -> the row's name
  box (default text "Darkest") -> a click elsewhere ends the edit -> the row's blue round button "Enable / View
  Mods" opens "Choose Mods to Enable" (nothing ticked by default; closed with its X) -> the row then says
  "DLC Disabled", "Mods Installed" -> click the row's letter: intro film (Esc), loading screen, the Old Road.

## 1. Files

All PNGs are the full 1920x1080 window. State of the estate when it was left: in the Hamlet, week 2, six heroes,
2,004,055 gold, nothing bought; the Old Road won, the first Ruins quest ("Scout") abandoned by retreat after
three rooms (so the map still offers only that quest). Read back with `tools/dd1_save_read.py`: `profile_8`,
"DD2REF", mode base, mods `DD2 Reference Test Estate (mod_local_source)`, no DLC.

### 1.1 `hallway/`

| file | what it shows |
| --- | --- |
| `oldroad_h1_start_as_entered.png`, `..._2.png` | Old Road hallway (Weald art), two heroes, standing where the party arrives from the room; torch 100 |
| `oldroad_h1_start_backed_to_limit.png` | the same after holding A for 5 s: as far back as the game allows |
| `oldroad_h1_curio_and_end_door_in_view.png` | mid hallway, the tent curio and the end door coming into view |
| `oldroad_h1_end_after_hold_D.png` | the end, as far as D takes the party |
| `ruins_h1_start_as_entered.png`, `ruins_h1_start_backed_to_limit.png`, `ruins_h1_end_after_hold_D.png` | the same three places in the first Ruins hallway with four heroes (the first two are identical: A does nothing) |
| `ruins_h2_start_as_entered.png` | second Ruins hallway, start, torch ~70 |
| `ruins_h3_start_torch30.png`, `ruins_h3_near_end_torch100.png` | third Ruins hallway: start in red low light; near the end door at torch 100 |
| `rec01`, `rec04` (`.mp4`) | Old Road: a click on the map in the room, fade to the hallway (`rec01` has the tutorial pop-up at the end) |
| `rec02_oldroad_h1_start_hold_A_back.mp4` | A held 5 s at the start, two heroes: the walk-back camera comes and goes |
| `rec03_oldroad_h1_start_door_W.mp4` + 12 frames + sheet | W at the START door, two heroes: push-in, fade, the room |
| `rec05_oldroad_h1_walk_to_end_hold_D.mp4` | D held 6 s into the end of the hallway: the party stops, no transition |
| `rec06_oldroad_h1_end_door_W.mp4` + 12 frames + sheet | W at the END door, two heroes; the room behind it starts a fight |
| `rec07_ruins_room_to_hallway.mp4` | Ruins: room to hallway by a map click |
| `rec08_ruins_h1_start_hold_A_back_party4.mp4` | A held 5 s at the start with four heroes: nothing moves |
| `rec09_ruins_h1_start_door_W_party4.mp4` + 12 frames + sheet | W at the START door from the arrival place, four heroes |
| `rec10_ruins_h1_walk_to_end_hold_D_party4.mp4` | D held into the end with four heroes |
| `rec11_ruins_h1_end_door_W_party4.mp4` + 12 frames + sheet | W at the END door, four heroes, torch ~70 |

Each `recNN...txt` gives when the key went out by the script's clock (about 1.5 s; ffmpeg starts a little
later, so in the recordings the first frame that reacts is at about 1.37 s in `rec03`, `rec06`, `rec09` and
`rec11`). The frames' numbers are hundredths of a second into the recording.

### 1.2 `raid/`

| files | what |
| --- | --- |
| `loading_screen_old_road`, `loading_screen_ruins` | loading screens (quest name, dungeon art, hint) |
| `raid_room_start_old_road`, `raid_room_map_side` | first room of the Old Road: HUD with the inventory side, then the map side |
| `raid_hud_at_rest_entrance_room_ruins_inventory`, `raid_hud_map_side_ruins`, `raid_hud_hallway_at_rest_party4_map` | Ruins: the HUD at rest with a hero selected, four heroes; inventory with provisions; the map side; a hallway |
| `raid_character_sheet` | right click on a hero in a raid |
| `raid_curio_hover_tent`, `raid_curio_window_tent`, `raid_curio_result_tent` | a curio: hover outline, its window (hand / item slot / X), the loot window that follows |
| `raid_curio_window_sconce`, `raid_curio_window_chest_room`, `raid_curio_result_chest_loot`, `raid_curio_window_heirloom_chest`, `raid_curio_result_heirloom_chest`, `raid_curio_item_tooltip_key` | more curio windows (a room's chest: the usable item's slot lit), results, the inventory tooltip of the item a curio takes |
| `raid_curio_result_banner_pack_contains_loot_kleptomaniac` | a hero's quirk acting on a curio by himself: the banner text over the scene |
| `raid_loot_window_after_fight`, `raid_loot_window_victory_two_items` | loot windows ("Victory!", take all / X) |
| `raid_obstacle_window_rubble`, `..._with_bark`, `raid_obstacle_after_shovel` | an obstacle: its window with the shovel slot, a hero's bark, the hallway after digging |
| `raid_trap_spotted_hover`, `raid_trap_sprung_failed_disarm`, `raid_trap_sprung_stress_popup` | a scouted trap seen on the floor; the failed disarm (red screen, "TRAP!", the hero and the spikes alone in view); the stress number after it |
| `raid_torch_level_0..4_clean` / `_tooltip` | one hallway spot at torch 12, 37, 62, 87, 100 (a torch lit between each): the scene, and the gauge's tooltip. `raid_hallway_torch_low_a(+_tooltip)`, `raid_torch_tooltip_*`: more of the same |
| `raid_hallway_fight_hero_turn`, `raid_hallway_fight_party4_start`, `raid_hallway_fight_stress_popup_round2`, `raid_fight_skill_selected_targets`, `raid_room_fight_start_*` (3) | fights: hallway and room, round banner, a skill picked with its targets marked |
| `raid_map_scouting_animation`, `raid_map_after_scouting` | the map's scouting ring, and the map with scouted hallway squares (trap, curio, fight icons) |
| `raid_esc_menu`, `menu_help_raid` | the Esc menu in a raid ("Abandon Quest" in it); the Help overlay |
| `raid_retreat_icon_tooltip`, `raid_retreat_confirmation` | the retreat flag under the quest goal and its Yes / No box |
| `raid_quest_complete_room`, `raid_quest_complete_return_tooltip` | quest goal met: the crest over the room with its two buttons |
| `results_page1_old_road(_counting)`, `results_page2_old_road` | results after a won quest: rewards and treasure counting up; the heroes' page |
| `results_page1_escape(_counting)`, `results_page2_escape`, `..._settled`, `..._quirks_shown` | results after a retreat ("Escape"): page 1; page 2 as it animates (shields flare, then the quirks gained are written in) |
| `tutorial_popup_*` (8) | the tutorial boxes of the first two quests |

Not got: hunger (the quest was left before the first meal), a camp (the short quest has none), a trap sprung by
walking into it unseen.

### 1.3 `town/`

| files | what |
| --- | --- |
| `fe_*` (7) | title screen; the estate list (top, bottom with the empty rows, our row); the DLC, campaign-mode and mod dialogs of a new estate |
| `town_at_rest_week1`, `town_at_rest_week2_*` (4) | the Hamlet at rest: week 1 (only the stage coach lit); week 2 with every building's marker, with the narrator's line, by day, with a building's hover label and a hero's bark |
| `town_activity_log_week1_arrival`, `town_activity_log_week2_caretaker_goals`, `town_event_window_week2` | what comes up on arriving: the log with the caretaker's goals, the town event ("Hear Ye") |
| `town_stage_coach*` (7) | stage coach: week 1 (two recruits), a recruit being dragged, after recruiting, week 2 (six recruits: the mod), its upgrade pane, a recruit's bark |
| `town_tavern`, `_hero_in`, `_upgrades`; `town_abbey`, `_upgrades` | tavern and abbey with their three activities; a hero in a slot; upgrade panes |
| `town_sanitarium`, `_hero_in_treatment`, `_quirk_selected_cost`, `_upgrades` | sanitarium: wards, a hero in with his quirks, a quirk picked with its price, upgrades |
| `town_blacksmith`, `town_guild`, `town_survivalist`: each plain, `_hero_in`, `_upgrades` | the three trainers: empty ("Drag a hero from the roster here."), with a hero (weapon and armour ranks; skills; camping skills with prices), with the upgrade pane open |
| `town_nomad_wagon`, `_trinket_tooltip`, `_upgrades` | nomad wagon with two trinkets, a trinket's tooltip, upgrades |
| `town_graveyard`, `town_statue_ancestors_memoirs` | graveyard (empty), the statue's memoirs list |
| `town_butchers_circus_enable_prompt` | the circus asks to be enabled (cancelled) |
| `town_character_sheet`, `_skill_tooltip` | a hero's sheet in town (right click in the roster) |
| `roster_sort_tooltip_1..4`, `roster_hero_tooltip`, `roster_count_tooltip` | roster header: Sort by Level / Stress / Class / Activity; pointer over a hero and over the count (no tooltip came) |
| `estate_bar_tooltip_1..5`, `_gold`, `_heirlooms`, `_heirloom_exchange`, `estate_crest_tooltip`, `town_embark_tooltip` | pointer over each estate-bar thing: the five buttons only get gold markers above and below (no text); the heirlooms give a text line |
| `estate_bar_1_town_event`, `_2_activity_log`, `_3_trinket_inventory`, `_4_glossary`, `_5_menu`, `estate_bar_heirloom_exchange` | what each estate-bar button opens |
| `menu_glossary`, `menu_controls`, `menu_options` + `_gameplay`, `_graphics`, `_audio`, `_other`, `_controls` | glossary, controls, the options pages (looked at, nothing changed) |
| `town_estate_map_quest_select`, `_party_formed`, `_week2`, `town_provision_screen`, `_bought` | estate map with the quest picked; the party dragged in; week 2; provisioning empty and with a basket bought |
| `tutorial_popup_*` (7) | the town's tutorial boxes |

Not got: a hero in an abbey slot (picture lost), a town upgrade actually bought, the graveyard with a dead hero.

## 2. Hallway ends: findings

All numbers are from the Old Road's hallway (Weald art, 6 tiles: door, 4 walls, door; two heroes) unless a line
says otherwise. Screen pixels are of the 1920x1080 window; "units" are DD1's world units (art pixels). The wall
tile is 626 px wide at rest (720 x 0.8688), so one unit at the wall is 0.869 px and at the heroes 1.117 px.

### 2.1 Where the camera rests

| where | door tile on screen (left..right, middle) | camera x | leader (bracket middle) |
| --- | --- | --- | --- |
| start, as the party arrives from the room | 265..890, middle 577.5 | first door tile's middle **+ 440** | screen x 668 = door middle + 179 |
| start, backed as far as A allows | the same | + 440 (after the walk-back camera has settled) | screen x 423 = door middle - 41 |
| end, as far as D allows | 890..1516, middle 1203 | last door tile's middle **- 280** | screen x 1326 = door middle + 48 |

So at the start the view's left edge is 665 units left of the door tile's middle at the wall's depth (305 units
of the extra, mirrored wall tile show: screen x 0..265), and at the end its right edge is 825 units right of the
door's middle (465 units of the extra tile: screen x 1516..1920). The extra tile is 720 wide, so the view never
gets past it: nothing but wall art, the end cap picture and the foreground strips is ever seen; no black.

Holding **A at the start does not go through the door** and holding **D at the end does not either**: the party
just stops (leader 41 before the first door's middle, 48 past the last door's middle). A door is used with
**W** (or a click on it).

### 2.2 Walking backwards at the start (hold A, `rec02`)

From the leader at +179: the camera goes into its walk-back state while the party really moves (1.1 s, 220
units at 200 u/s): the door tile grows 626 -> 654 px (x1.045) and the camera slides from 440 to 348 (-92), the
side strips get about 60 % brighter (closer camera). The moment the party stops at the limit (A still held) the
camera turns back and is at rest again 0.8 s later (440, 626 px). So at the wall of the start the walk-back
camera never gets beyond a third of its full swing (-130, zoom 0.8 at 675 units).

### 2.3 Through a door (W), start door `rec03`, end door `rec06`

Both doors behave the same. t = 0 is the key press.

| t (s) | what the frames show |
| --- | --- |
| 0.00 | the selected hero's bracket, the HP bars and the quest text top left are gone in the next frame; the door prop swings open (one pose) |
| 0 .. 1.3 | **the camera flies at the door**: the door tile's middle stays at its screen x (577.5 at the start door, 1203 at the end door) and one screen row stays where it is (444 in the Weald, 405 in the Ruins), while the tile grows: x1.07 at 0.2 s, x1.12 at 0.33, x1.18 at 0.47, x1.28 at 0.67, x1.44 at 0.93, x1.65 at 1.2, x1.85 at 1.37, x2.06 at 1.57, x2.34 at 1.72 (start door; the end door within 1 %) |
| 0 .. 1.3 | the heroes walk **into the picture**, to the door's middle and away from the camera, getting smaller against the arch and darker; from about 0.8 s they are black shapes in the door's dark opening, at 1.0 s they cannot be told from it |
| 0 .. 1.3 | the wall darkens slowly and evenly: brightness x0.91 at 0.33 s, x0.82 at 0.67, x0.70 at 1.0, x0.56 at 1.33 (start door, a patch of wall no hero crosses) |
| 1.33 | the fade proper starts: from x0.56 to black in 0.38 s, a straight line (x0.38 at 1.47, x0.17 at 1.6, x0.01 at 1.73) |
| 1.72 | black (start door 1.72, end door 1.67). The torch gauge at the top fades with the scene |
| 1.72 .. 2.45 | the next area fades in, a straight line over 0.7 s; the party already stands there. No hold on black (two frames) |
| 2.6 | brackets and bars are back (0.15 s after full brightness) |

Reading the numbers (five recordings: `rec03`, `rec06` two heroes in the Weald; `rec09`, `rec11` four heroes
in the Ruins; all agree):

* The growth is a **straight flight of the camera's position towards a point on the door, linear in time,
  that would take 3.0 s** (`camera_position_blend_time 3.0`) and is cut off by the black. With
  b = 1 - 1 / (the tile's growth), b rises by 0.333 a second in every recording: **b = t / 3.0**, and
  camera = rest place + b x (door point - rest place): x offset from the door 440 (1 - b) (or -280 (1 - b) at
  the end door), distance to the wall 1800 (1 - b), height 270 - b (270 - door point's height). Because the
  flight is a straight line at the point, the point keeps its place on screen while everything grows round it.
* **The door point**: the door tile's middle in x; on the wall's plane (z = 400: the rate fits a distance of
  1800 +- 20); its height is **half the door prop's height**: **214 units in the Ruins** (screen row 405
  keeps still in `rec09` and `rec11`; the crypts door is 429 high), 171 in the Weald (row 444; that door's
  size was not looked up). No easing at either end.
* How far the flight gets depends on how long the party walks: b = 0.57 (tile x2.3, camera 770 from the wall)
  with two heroes, 0.59 (x2.44) with four at the start door, **0.66 (x2.9, 610 from the wall) with four at
  the end door**.
* The **slow darkening** is a 3.0 s tween as well (brightness about 1 - t / 3: x0.56 after 1.33 s, x0.42
  after 1.75 s); the **fade proper** is the 0.7 s fade (`fade_out_time 0.7`) taking over from where the slow
  one stands (from 0.56: 0.38 s; from 0.42: 0.30 s).
* **The fade proper starts when the last hero has arrived**, and every hero walks **straight from where it
  stands to the door's middle on the wall** (diagonally into the depth, each on its own line, all starting at
  once; no lining up first; the near ones are gone in the opening while the far ones still walk). Time from W
  to the start of the fade against the farthest hero's sideways distance dx:

  | recording | farthest hero's dx | measured | 0.25 + sqrt(dx^2 + 400^2) / 400 |
  | --- | --- | --- | --- |
  | `rec06` end door, two heroes | 120 | 1.29 s | 1.29 |
  | `rec03` start door, two heroes (backed) | 183 | 1.33 s | 1.35 |
  | `rec09` start door, four heroes | 282 | 1.50 s | 1.47 |
  | `rec11` end door, four heroes | 454 | 1.75 s | 1.76 |

  i.e. walking speed 400 u/s over the straight line to a point 400 deep, plus a quarter of a second.
  A hero darkens over the last stretch of its own walk and is a black shape by the time it stands in the opening.
* In dim torch light the "black" between the two areas is a flat (6, 6, 6), not (0, 0, 0): the colour grade
  comes after the fade (`rec11`, torch about 70; at torch 100 it is (0, 0, 0)).
* Room to hallway (a click on the map, `rec04`, `rec07`): no walking, no camera move: the room fades out over
  0.67 s (a straight line) the moment the room is clicked, the hallway fades in over 0.7 s with the party
  standing at its start and the camera at rest (+440). Brackets back 0.15 s later.

### 2.4 What is beyond the door tiles

See 2.1: the camera's rest places leave 305 units (start) and 465 units (end) of the extra wall tile in view;
the walk-back camera at the start shows 709 units left of the door's middle at most (still inside the extra
tile, which ends at -1080). The frames `oldroad_h1_start_as_entered.png`, `oldroad_h1_start_backed_to_limit.png`
and `oldroad_h1_end_after_hold_D.png` show what is drawn there in the Weald: more forest wall (the mirrored
neighbour tile), the `endhall` trees in front of it, the dark foreground strips at the screen's edges.

### 2.5 A party of four (the Ruins' first two hallways), against two heroes

Each hero's x is read from its HP bar (the bar's middle is under the hero), in world units from the middle of
the first door tile (start) or of the last door tile (end). Hero 4 is the last one, hero 1 the leader.

| where | hero 4 | hero 3 | hero 2 | leader | two heroes: last, leader |
| --- | --- | --- | --- | --- | --- |
| start, as the party arrives from the room (`ruins_h1_start_as_entered.png`) | **-181** | -27 | +127 | **+282** | +26, +180 |
| start, A held for 5 s (`ruins_h1_start_backed_to_limit.png`, `rec08`) | -181 | -27 | +127 | +282 | -183, -39 |
| end, as far as D allows (`ruins_h1_end_after_hold_D.png`, `rec10`) | -454 | -287 | -118 | **+50** | -120, +49 |

* **Backwards the limit is on the LAST hero** (x >= -180 from the first door's middle), **forwards on the
  LEADER** (he stops 50 past the last door's middle). A party of four arrives with its last hero already on
  the limit, so A does nothing at all at the start: no step, no camera move (`rec08`: nothing changes in 5 s).
* The last hero at -181 stands inside the door tile (it reaches to -360), 180 short of the extra wall tile. No
  hero ever stands in the extra tile at either end.
* Spacing: 154 on arrival (`actor_spacing`), 166..168 after walking forward, 143 after backing (two heroes).
* The camera's rest places are the same as with two heroes: +440 at the start, -280 at the end.
* **W uses the start door straight from the arrival place** (leader at +282, `rec09`): no walking back first;
  the transition starts in the frame after the key.
* **W and the end door**: with the leader 900, 794, 687, 576, 468 and **360** units before the door's middle W
  does nothing; at **251** before it the door is used (second Ruins hallway; steps of ~105 units, so the reach
  ends somewhere between 251 and 360).
* On W every hero heads straight for the door (2.3): no lining up.

## 3. Things seen that the other notes left open

Corridor (`dd1-corridor-rendering.md`):

* **12.5 "the camera's blend towards the door: motion not recovered"**: recovered, section 2.3: a linear 3.0 s
  flight of the camera's position to the door's middle at half the door's height on the wall plane, cut off
  by the fade. It starts with the key, not at "650 from the door" (every W here was pressed within 650).
* **12.7 "door reach and each hero's goal"**: the goal is the door's middle on the wall (z about 400), each
  hero on its own straight line (2.3); the reach ends between 251 and 360 units before the door's middle (2.5).
* **Section 8's table**: the scene darkening is a 3.0 s tween from the key, then the 0.7 s fade from where it
  stands; "all heroes arrived" is the trigger, and with four heroes at the end door that is 1.75 s after W.
* **3.7 (party limits)** confirmed for four heroes: last hero at -181 on arrival and backing, leader +50 at the
  end, leader at +282 on arrival (2.5).
* **3.2 walking back**: seen, but at a hallway's start it never gets further than x1.045 / -92 units, because
  the party stops after 220 units; with four heroes it never starts (2.2, 2.5).
* The mod kit's open question (`a note kept out of git (dd1-test-setup.md)` section 10): **a mod's `scripts/starting_save` IS used** for
  a new estate (wallet 2,000,000 + the Old Road's 5,000 less provisions); **all buildings are open in week 2**
  with the requirements at 0 (in week 1 only the stage coach is, the first-week tutorial holds the rest); the
  stage coach offers six recruits; the map offers only the first Ruins quest until it is won.

Raid UI:

* W (not D) uses a door; D into the end and A into the start only stop the party.
* A curio is used by a click or W within reach; out of reach the click does nothing (no walking to it).
* No hover outline was seen on a scouted trap; a click on it within reach tries to disarm it. The failed
  disarm tints the whole view red, the quest text and the torch gauge are gone for that moment, the other
  heroes dim, and "TRAP!" stands on the banner.
* The torch tooltip's heading as seen: DARK (12), SHADOWY (37), DIM LIGHT (62), RADIANT LIGHT (87 and 100),
  each with its own list of effects; the scene's grade changes with the band (`raid_torch_level_*`). Where
  one band ends and the next begins was not probed. A torch adds 25.
* The pause menu in a raid: Return to game, Abandon Quest, Help, Controls, Glossary, Options, Credits, Exit to
  Main Menu, Exit to Desktop; the build number bottom right.
* Retreat: the small flag under the quest goal -> a Yes / No box ("Retreat from quest?") -> the results screen
  titled "Escape" with the same two pages as a won quest.
* Results page 2 animates in stages: the rows, then a flare on each hero's shield, then the quirks gained
  written into the row (`results_page2_escape*`).
* In low light the black between two areas is not pure black (2.3).
