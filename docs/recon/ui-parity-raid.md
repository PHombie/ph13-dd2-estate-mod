# Raid UI parity: the mod's dungeon screen against DD1's own files

## The pass of 2026-10-05 by DD1's frames (in the code, not yet seen in game)

DD1's own frames (`_lab/dd1_ref/raid/*.png`, 1920x1080) were measured for this pass: its art was looked for in
them and the places counted. Where a frame and a reading of the layout files disagreed, the frame won. The tables
below are as the audit wrote them; this is what has changed since.

| Rows | What is in the code now | Where |
|---|---|---|
| K1, K2, K6, K7, K9 | A curio is turned to (a click on it, or W): nothing is asked on arrival. A curio DD1 gives no window (crate, sack, discarded pack, sconce: "FULL CURIO?" No) is opened at once. The window has DD1's words, the hand and the way past as pictures on DD1's rails without captions, the slot empty; an item is offered by drag or right click; one the curio has no use for stays in the bag and the hero says DD1's "That item had no effect." in DD1's balloon. A quest's curio that wants the quest's item has a dead hand. The result's sentence (`str_curio_<curio>[_<item>]_<result>`) stands in the banner at the top. | `DungeonRun.TurnToCurio`, `Investigate`, `CurioResultText`; `Core/CurioCatalog`; `RaidBark.cs` |
| D7 | DD1's tracker marks under the cards (`panels/icons_curio_tracker`), remembered across expeditions in the estate's save; the bag no longer tells what works. | `CurioMemory.cs`, `InventoryGrid.SetMark` |
| L1, L2, L3, L4 | No window for a spotted trap: a click on it is the selected hero's attempt (class skill from DD1's hero file, +scouted bonus, -level penalty); walking on springs it. "TRAP!" and "Disarmed!" at the top. A found secret door asks nothing. The obstacle's scroll has the way past (the party stays this side of it), the shovel lying in the slot, DD1's "You don't have a shovel!". | `TrapDisarm.cs`, `DungeonRun.Disarm`, `ClearObstacle`, `CorridorView.Barrier` |
| D2, D3, D4, D6, J1, J2 | The right button uses an item, the left carries it (to a cell, the scroll's slot, a hero); cards that cannot be used are grey with a fifth of their colour; no tooltips on the two tabs; the log lines are off (`hud.log show=true`); refusals are DD1's line of user information over the pointer. | `DungeonHud` (bag), `InventoryGrid`, `RaidUi.Desaturated` |
| A1 | Bars, brackets, quest corner and torch go with `layers.HUD_alpha` of DD1's scripts; the torch also fades with the scene through a door. | `DungeonHud.Update` |
| H1, H2, H3 | DD1's torch is drawn (`overlays/torch.png`, the gauges, the flame from `torch_flame.plist`), with DD1's tooltip as its frames have it and shift-click to reduce. DD2's widget stays behind `hud.torch dd2=true`. | `RaidTorch.cs`, `RaidTorchFlame.cs` |
| I5, I6, I7, I8, P2 | Camp bonus icons beside the flag; the seal and the crest are DD1's sprites; "Quest Complete!" in the banner; the choice and the retreat question lie on a blurred picture of the screen (a Gaussian of 2.3 px in DD1's frames, no darkening). | `RaidQuestInfo`, `RaidConfirm`, `TutorialPopup.BlurredScreen` |
| B1, B2, B3, Q4, E7 | (owner, 2026-10-06) Places 1 to 5: the hero's five equipped skills in their own colours; "move" in the narrow place where DD1 has "pass" (20x72, put together from DD1's move square and its small sign of a move), no "pass". Chosen, the companions' places wear DD2's own mark of a friend a skill can go to (the green forked line of its fights, asked of the game by its address; DD1's `overlays/move.png` until it has come), and the one clicked gives up their place: the heroes go there with DD2's own step and curve of a fight (half a second), their bars with them. Escape, the right button or a second click on "move" puts it away. A hero is also carried to another rank by their bar. The party-order button puts the first order back. DD2's ranks follow. | `RaidHeroPanel.CombatSkills`, `RaidMoveArt`, `RaidMoveMarks`, `CorridorRanks.cs`, `DungeonRun.MoveHero`, `RaidTrays` |
| C3, C6, C8 and the panel's look | Portrait in DD1's rim, filling it; "33 / 33" beginning at the layout's x; a row a quirk, disease or trinket has moved in gold (worse: red, a guess), the arrow for what a camp gave (a guess); four rows (owner, 2026-10-06): DMG, CRIT, HP, SPD, the first three as what the hero adds in percent ("+20%"), read from DD2's stats as a fight reads them; gear and trinket tooltips beside their cards. | `RaidHeroPanel.cs` |
| E3, E4, E5, E6 | The map at the frames' scale (art at half size: rooms 32 px, 83 or 84 px apart), the party held 16 px left of and 19 px above the window's middle as in ten frames, no drawing back, the right button pans, the destination wears `moving_room.png`, the two side buttons' tooltips left of them. | `Minimap.cs` |
| G5, M3, N4 | The bars' tooltip without a title on DD1's hot area; no words under the bowls or under Take All and Close. | `RaidTrays`, `RaidScrolls` |

Still open: B4 (what a skill does), C4 (stat tooltips), C7 and C9 beyond the click the inventory work added, E2, E8, F2,
F3, G4, G8, G9, I9 (Escape still leaves the choice), L5, O1, O2, O5 to O9, P3, P4, Q3 (help panel), R1 (character sheet),
and G7 beyond the two lines that are spoken (an item without effect, the kleptomaniac's).

## Top 25 to fix first

Ranked by how much of the screen is wrong for how long. Ids point into the tables below.

Since the audit: items 1, 2 and 3 (G2, G3, G1) and the icon's pulse of G9 are in the code, not yet seen in game
(`Dungeon/RaidAnnouncement.cs`, `RaidPopText.cs`, `RaidTrayIcons.cs`, `RaidFeedback.cs`;
`docs/recon/dd1-raid-ui.md`, sections 4 and 9, has the readings and what stayed a guess). The tables below are
as they were written.

1. **G2: no announcement banners.** DD1's `overlays/announcement_frame.png` with "TRAP!", "Disarmed!", "Ambush!",
   "Surprised!", "New Quirk:", "... is at Death's Door!" (`screen.raid.darkest: announcement`, `announcement_times`,
   `announcement_show.times`). The mod writes these to its own log in its own words.
2. **G3: no pop text over the heroes.** DD1's `popup` font and the 42 layouts of `base.popup_text.layout.darkest`
   (damage, heal, stress, "Full", "Cured!", buff...). Trap, hunger, curio, camp and item effects change the bars silently.
3. **G1: no status icons over the health bars** (`overlays/tray_*.png` at `tray_icon_left_offset 58 -38`): camping
   buffs, death's door, disease and the rest cannot be seen on a hero.
4. **N1: no loot scroll after a fight** ("Victory!" / "You have found:", `str_overlay_loot_battle_*`).
5. **D9: a trinket found in the dungeon is seen nowhere**: not a card on the loot scroll, not a card in the bag.
6. **K1: a window for curios DD1 opens without one**: crate, sack, discarded pack, sconce ("FULL CURIO?" = No in
   `curio_type_library.csv`, no title or content strings), with a made-up title and text.
7. **K2: DD1's result sentence of a curio is never shown** (`str_curio_<id>[_<item>]_<result>`, 197 lines); the mod
   logs "Nothing happens." or "Something stirs (Quirk: ...)".
8. **L1: an invented window for every spotted trap** ("A trap lies ahead." / "Disarm it" / "Walk through").
9. **R1: the character sheet is DD2's**, in the dungeon as in the town (DD1: `shared/character`, 1395x776 at 144,132).
10. **J1 (+J2): the log under the quest info and the HUD's line over the inventory are the mod's own elements.**
    They stand in for items 1, 2 and 7 and should go when those are in.
11. **I5: camping buffs are not shown beside the retreat flag** (`quest_info.camp_bonus_icon_offset 140 110`, half-size
    skill icons with tooltips).
12. **D3, D4: items are used with the wrong button and cannot be dragged.** DD1: right click uses, the left button
    picks up and drags (onto the curio's slot, a hero, a trinket slot, another cell).
13. **D5: item tooltips carry the mod's sentences** instead of `str_inventory_description_*` and the
    "[Value: %s Gold Each]" line.
14. **D7: at a curio the bag dims what will not work** (knowledge DD1 hides); DD1's curio tracker icons
    (`panels/icons_curio_tracker`, `inventory_curio_tracker_layout.icon_offset 24 118`) are missing.
15. **K3, K4, K5: the curio scroll's words.** Captions under the hand and the way past that DD1 places for a
    controller only; no "Ignore" tooltip; the slot's tooltip is not "[OPTIONAL] Drag an item here to use it on this object."
16. **M1, M2: the hunger scroll's text and tooltips are the mod's**, though DD1 has the sentences
    (`str_ui_hunger_content`, `str_ui_hunger_choice_eat`, `str_ui_hunger_choice_starve`).
17. **O3, O4: camping skills.** The cost is an invented number on the icon (DD1: "Time Cost: %d" and
    "Uses Remaining: %d" in the tooltip), and the whole tooltip is in the mod's wording.
18. **I6, I7: quest complete.** No animated seal (`fx/quest_complete_seal`), no crest (`fx/quest_complete_crest`),
    no "Quest Complete!" banner words, a shade for DD1's blur.
19. **C1, C2, C3: the stat list.** Four rows of DD1's six, DMG as a percentage where DD1 writes a range, no
    buff / debuff arrows (`shared/hero/icon_stat_buff.png`).
20. **C5: the weapon and armour level numbers are one grey**; DD1 colours them by level (`equipment_level_0..4`).
21. **E1: markers on hallway tiles of the map have no tooltip** (the words are already in `Scouting.TileTooltip`).
22. **I2 (+I4): the goal line and the retreat tooltip are the mod's words** ("Explore rooms  3/9", "Abandon the
    Quest"); DD1: `town_quest_goal_start_*`, `retreat_raid_tooltip` "Abandon Quest".
23. **P1: every picture button rests at 82% of its art** and only reaches DD1's resting look under the pointer.
24. **Q1, D1, N3 (+Q2, Q3): DD1's keys.** W (door, interact), TAB (map / inventory), SPACE (take all), C (sheet),
    H (help) do nothing.
25. **B1, Q4, E7: no move skill in place 5, no pass sliver, no reordering of the party, no party-order button.**

Next in line: O1 and O6 (camp scrolls at the right edge, the box under them), H1 (torch tooltip's list), L2 and L3
(secret door window, obstacle's way past), G7 and G8 (barks, halos and item effects), A1 (HUD fade while a hero
investigates), and I1 once somebody has looked at DD1 (is the quest text only shown under the pointer?).

## How to read this

Audit of 2026-10-05, tree `dev` at `bde9559`. Nothing was run: the mod's code was read, and DD1's files in
`E:\Steam\steamapps\common\DarkestDungeon` (layouts, fonts, colours, the English sections of the string tables, PNG
sizes, Spine atlases and skeleton names). DD1 paths are relative to that folder; `screen.raid.darkest`,
`panel.*.darkest` and the like are in `scripts/layout/`. Mod paths are relative to `src/DD2Estate/`; a bare file name
is in `Dungeon/`.

- **Severity**: high = visibly wrong or missing; medium = visible to somebody who knows DD1; low = small, rare or a
  matter of a few pixels.
- **Confidence** is about the DD1 side: how sure the files alone make it. Where the files cannot settle a thing it is
  in the area's "needs eyes" list instead of, or as well as, the table.
- **(deliberate)** marks a deviation the mod's own notes already own up to (`docs/recon/dd1-raid-ui.md`, section 7).
  They are listed all the same: the rule is likeness.
- **Not a deviation, by the owner's word:** the torch widget in the dungeon is DD2's (`Dd2Torch.cs`). Only what hangs
  on it (tooltip, hot areas) is looked at.
- **Left out on purpose:** Crimson Court, Color of Madness (farmstead, endless mode: `kill_count_display`,
  `shard_escrow_display`, `wave_countdown_display`, `panel_inventory_wave.png`), Butcher's Circus; controller-only
  entries (`*_controller_*`, `input_preview`); fights; the town; the provision screen.
- **Raid results screens** (victory, retreat, defeat): the mod has none. `DungeonRun.Finish` (`DungeonRun.cs:1213-1249`)
  settles the bag and goes to the hamlet with a report line. DD1: `raid_results/raid_results.layout.darkest` and its
  art. Not audited here.
- **Pause menu**: Escape closes the mod's own dialogs (`DungeonHud.cs:626-631`) and otherwise opens DD2's pause menu.
  It is not DD1-styled (DD1: `shared/menu/menu.background.png`, 1023x832, at `menu_layout.base_pos 450 150`), so it
  is noted and not counted.

Counts: 105 deviations: 9 high, 39 medium, 57 low.

---

## 1. The frame

Matches DD1: the two 720 px panels under `panel_top 720` between `safe_left 240` and `safe_right 1680`,
`panels/side_decor.png` left and mirrored right, `panels/panel_transition.png` at 0,710 (`DungeonHud.cs:253-282`,
all through `RaidLayout`).

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| **A1** HUD while a hero investigates, disarms or is hit by a trap | `scripts/timescript/investigate_intro.times`, `disarm_intro.times`, `trap.times`: `layers.HUD_alpha .value 0.0 .time 0.1`, `layers.HUD_update 0.0`; back to 1.0 over 0.3 at the end | `CorridorScript.cs:291-293` computes `HudAlpha` "for the HUD to read"; nothing in `DungeonHud.cs` or `RaidOverlays.cs` reads it | the bars, brackets and panels stay fully drawn through the close-up | medium | medium |

Needs eyes: which layers DD1 means by "HUD" (the bars under the heroes only, or the panels and the torch as well).

## 2. Hero banner (portrait, name, class, skills)

Matches DD1: `panels/panel_banner.png` at -33,0; portrait window 85 px at 32,32; name and class in `banner_hero_name`
/ `banner_hero_class` with the layout's two colours (177 161 108; 154 152 143 at alpha 175); icons 72 px at 280,35
every 76; `selected_ability.png` around an armed camping skill.

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| **B1** fifth skill place and "pass" | `panels/icons_ability/ability_move.png` (72x72) in the place numbered 5 (`panel.banner.darkest: ability_layout .pos 280 35 .spacing 76`, so x 584), `ability_pass.png` (20x72) beside it, `focused_pass.png`; help line `[pc]str_help_raid_hallway_1`: "[CLICK] on a hero's move skill to change their party position." | `RaidHeroPanel.cs:298-324, 338-343`: up to five DD2 combat skills; no move, no pass | DD1's move icon and pass sliver are absent; a DD2 skill stands on "5" | medium | high (deliberate) |
| **B2** empty skill place | `panels/icons_ability/ability_none.png` (72x72) | `RaidHeroPanel.cs:338-342`: nothing is drawn for a place without a skill; a skill without an icon is a flat 0.12 grey box | DD1's empty-slot picture is not used | low | medium |
| **B3** skill that cannot be used | `colours/base.colours.darkest: skill_unselectable .darkness 0.4 .saturation 0.0` | `RaidHeroPanel.cs:342`: tint 0.32, colours kept | darker than DD1's and not desaturated | low | medium |
| **B4** skill tooltip | DD1 words a skill in full (name, level, accuracy, damage and crit modifiers, effects, ranks); styles `tooltip_header`, `tooltip` (ubuntu_small) | `RaidHeroPanel.cs:320`: the name, and "Mastered" for a mastered skill; `:363` centred above the icon, 320 wide (own numbers) | the tooltip says nothing of what the skill does | medium | medium |
| **B5** pointer over a skill | `panels/icons_ability/focused_ability.png` (110x110): by its name the frame of controller focus; under a pointer DD1 brightens (`button_highlight`) | `RaidHeroPanel.cs:348-362`: `focused_ability.png` shown under the pointer | possibly a frame DD1 shows for a controller only | low | low |
| **B6** affliction / virtue seal | `panels/seal.affliction.png`, `seal.heroic.png` (134x104) at `portrait_layout .seal_pos 24 23` | not drawn | missing | low | high (deliberate) |
| **B7** third line under the class | none: `name_layout` has `.pos 272 38` and `.class_y 76` only | `RaidHeroPanel.cs:58, 122-123`: the hero's path, 21 px under the class | an invented line | low | high (deliberate) |
| **B8** name or class that does not fit | DD1 draws its styles 1:1 | `RaidHeroPanel.cs:119, 121, 123`: shrinks to 18 / 12 (`RaidUi.Fit`) | text size can differ from DD1's | low | medium |

Needs eyes: whether name and class end at x 272 (the mod's reading) or start or centre there; whether combat skills
are drawn lit or dark while walking; whether `focused_ability.png` ever shows with a mouse; the crop of DD2's
portrait (drawn 106 px, cut at the 85 px hole, `RaidHeroPanel.cs:47-50`) against DD1's full-square portrait.

## 3. Hero panel (health, stress, stats, weapon, armour, trinkets)

Matches DD1: `panels/panel_hero.png` at 0,136; health and stress numbers in `stat` at 130,11 and 130,40 in the
layout's colours; stat rows every 20 px from 60,72 with values at +112; weapon and armour cards (DD1's own
`eqp_weapon_N.png` / `eqp_armour_N.png`) at 271,52 and 362,52; trinket cells at 489,52 and 581,52. Quirks: neither
`panel.hero.darkest` nor `panel_hero.png` has a place for them (DD1 lists them on the character sheet: R1), and the
mod's panel shows none; see `panel_personality.png` under "needs eyes".

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| **C1** stat rows | `shared/hero/hero.layout.darkest: hero_stats_layout .spacing 160 20 .value_offset 112 0` from `panel.hero.darkest: stat_layout .pos 60 72 .number_of_columns 1`; six rows: `str_ui_ATT_MOD` "ACC MOD", `str_ui_CRIT`, `str_ui_DMG`, `str_ui_DEF` "DODGE", `str_ui_PROT`, `str_ui_SPD` | `RaidHeroPanel.cs:86-92, 131-137`: four rows (CRIT, DMG, PROT, SPD) on DD1's first four lines | two rows missing; CRIT, DMG, PROT and SPD stand on lines 1 to 4 instead of 2, 3, 5, 6 | medium | high (deliberate) |
| **C2** DMG value | DD1 writes the damage range ("6-12") | `RaidHeroPanel.cs:89`: the damage multiplier as "+n%" | another kind of number under DD1's label | medium | medium |
| **C3** buffed or debuffed stat | `shared/hero/icon_stat_buff.png`, `icon_stat_debuff.png` (24x24) at `hero_stats_layout .icon_offset -26 2` | none | a stat changed by a camping buff or anything else is not marked | medium | medium |
| **C4** stat tooltip | `hero_stats_layout .tooltip_hotspot_size 160 20 .tooltip_offset -500 0 .tooltip_text_width 250` | none (`RaidHeroPanel.cs:135-136`: labels only) | no tooltip on a stat row | low | medium |
| **C5** level number of weapon and armour | style `equipment_level` (ubuntu_medium) at `hero_equipment_layout .level_offset 90 12`; colours `equipment_level_0` and `_1` 215 213 205, `_2` 105 190 75, `_3` 62 114 212, `_4` = harmful (177 25 0) | `RaidHeroPanel.cs:142-143`: one colour for every level: `equipment_level` = neutral (174 172 162) | not coloured by level, and greyer than DD1's even at level 1 | medium | medium |
| **C6** weapon / armour tooltip | `hero_equipment_layout .tooltip_hotspot_offset 32 52 .tooltip_hotspot_size 72 144 .tooltip_offset 120 52`; colours `equipment_tooltip_title` = notable, `equipment_tooltip_body` = neutral | `RaidHeroPanel.cs:162`: above the card's top right corner (+12, -12), 200 wide; words `:241-246`: "name, level n" and the mod's effect line | place (DD1: beside the card, level with its top) and wording | low | medium |
| **C7** trinket cards | a 72x144 card (`panels/icons_equip/trinket/inv_trinket+<id>.png`) in each cell of `hero_trinket_grid_layout .start_pos 32 52 .offset 92 160` | `RaidHeroPanel.cs:60, 145-151`: DD2's square icon, 72x72, in the middle of the cell | half the cell is empty | low | high (deliberate) |
| **C8** trinket tooltip place | `shared/inventory/inventory.layout.darkest: inventory_item_tooltip_layout .offset 85 0 .text_width 200` | `RaidHeroPanel.cs:162` (as the gear: above the card) | place | low | medium |
| **C9** changing trinkets in the dungeon | DD1 lets a trinket be dragged between the inventory and the two slots outside a fight (`str_user_information_cant_unequip_trinket_in_battle` is the refusal inside one) | none: the slots only show (`RaidHeroPanel.cs:216-228`) | no way to put on or take off a trinket from the HUD | medium | medium |

Needs eyes: whether the health and stress numbers are centred on x 130 and written "a/b" without spaces; whether a
card under the pointer wears a frame (`hero_equipment_layout .highlight_pos_offset -20 -20`); whether
`panels/panel_personality.png` (720x224, a second hero panel with two tabs at its left edge) is used by the shipped
game or is a leftover.

## 4. Right panel: the tabs and the inventory

Matches DD1: `panels/panel_map.png` / `panel_inventory.png` swapped as whole pictures; tab areas from
`tab_placement .pos 672 252 .size 48 90`; 16 cells in 8 columns from 20,28 every 80,160; card 72x144 at +4,0; count
in `inventory_amount` at 14,4; `overlays/eqp_mouseover.png` (118x187) centred on the card under the pointer (the
frame's own rectangle is x 24..95, y 22..163 of the picture: centring is right to a pixel), `eqp_unavailable_mouseover.png`
on a dimmed one; the tooltip at `inventory_item_tooltip_layout .offset 85 0`, 200 wide; shift-click discards with
DD1's line in `inventory_shift_click_remove_item`; quest items cannot be discarded (`cant_discard_quest_item_confirm`).

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| **D1** map / inventory by key | `[pc]menu_controls_element_2_6`: "TAB - alternate Map and Inventory" | `DungeonHud.cs:621-634`: Escape and T only | no TAB | medium | high |
| **D2** tooltip on the two tabs | none: no string for the tabs in the tables; `panels/map_tab_highlight.png` (32x56) is an empty picture (alpha 0 throughout) | `DungeonHud.cs:338-339, 352-360`: "Map" / "Inventory" | invented tooltips | low | medium |
| **D3** using an item | `[pc]str_help_raid_combat_3`: "[RIGHT-CLICK] on Anti-Venom or Bandages in inventory to heal ..."; `[pc]str_help_raid_room_6`: "[RIGHT-CLICK] an inventory item to apply it to the object" | `DungeonHud.cs:530-571`: a left click uses it (on the selected hero, or on the open curio), a right click puts away | the buttons are the other way round | medium | medium |
| **D4** dragging items | `curios.string_table.xml: curio_tooltip_item_slot` "[OPTIONAL] Drag an item here to use it on this object."; colour `inventory_selected .hvec4 1.4 1.4 1.7 1.0` for the card in hand | none: `InventoryGrid.cs:157-173` takes clicks and hover only | no picking up, no rearranging, no drag onto the curio's slot | medium | medium |
| **D5** item tooltip text | title `str_inventory_title_<type><id>`; body `str_inventory_description_<type><id>` (for a torch: "Increases the light level."); value line `str_inventory_gold_value_format` "[Value: %s Gold Each]" in `inventory_tooltip_gold_value`; discard line `str_discard_item_instructions` | `DungeonHud.cs:598-601` with `InventoryContent.cs:84-109`: DD1's title, the mod's own sentence ("Lit with a click: the torchlight rises."), DD1's discard line | description and value line are not DD1's | medium | high |
| **D6** card that cannot be used | `inventory_unselectable .rgba #666 .saturation 0.2`; `inventory_amount_unselectable #5d5a50` | `InventoryGrid.cs:115-117`: tint only | not desaturated | low | medium |
| **D7** what the bag shows while a curio is open | DD1 dims nothing by usefulness; it marks what the player has learnt: `inventory_curio_tracker_layout .icon_offset 24 118`, icons `panels/icons_curio_tracker/<result>.curio_tracker.png` (32x32: loot, buff, purge_neg, nothing ...), ids in column "CURIO TRACKER ID" of `curios/curio_type_library.csv` | `DungeonHud.cs:692-693`: every stack the curio has no use for is drawn dark; `Core/CurioCatalog.cs:48-49, 217` reads the tracker id, nothing draws it | the mod tells which items work before they were tried; DD1's tracker icons are missing | medium | medium |
| **D8** stack size | `inventory_amount` (dwarven_axe_medium, notable) at `amount_text_offset 14 4` | `InventoryGrid.cs:83-86`: the same, with a black twin 1.5 px down and right | invented shadow | low | medium |
| **D9** trinkets found in the dungeon | a trinket is a card of the loot scroll and of the bag (`panels/icons_equip/trinket/inv_trinket+<id>.png`, 72x144) | `DungeonRun.cs:979-985`: rolled as a DD2 trinket, kept in a list beside the bag, told in one log line | a found trinket is seen nowhere on the HUD | high | high |
| **D10** refusals and notes about items | DD1's own lines: `str_cant_use_torch_at_limit` "It's no use...this is all the light we are getting for now.", `str_cant_use_firewood_in_entrance_room` "Can't camp in first room", `str_user_information_*` | `DungeonRun.cs:340-398, 1058-1064`, `DungeonHud.cs:583`: the mod's words ("The light is as bright as it gets.", "Can't camp in the first room.", "Dropped: ..."), although `RaidUi.cs:32` loads `str_cant_use_*` | DD1's words unused where they exist | low | high |

Needs eyes: what a plain left click on an item does in DD1 (the help line for firewood, `[pc]str_help_raid_room_3`,
says "[CLICK]"); when and where the curio tracker's icon shows.

## 5. Right panel: the map

Matches DD1: window `map_layout .clip 16 665 19 340`; 24 px tiles, 64 px room icons from `panels/icons_map`; two rooms
n + 3 tiles apart; `hall_dark` / `hall_dim` / `hall_clear`; the six markers; `marker_room_visited.png`; `indicator.png`
bouncing by `indicator_layout`; wheel zoom between 0.35 and 1.5; follow again 10 s after a drag; a click on a
reachable room walks there; room tooltips in DD1's words (`str_map_*`, `str_move_to_this_room`); the home button
(`panels/focuscam_button.png` at 677,24, `str_centre_map_tooltip`).

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| **E1** tooltip on a hallway tile | `str_map_ac_battle_tooltip`, `str_map_ac_curio_tooltip`, `str_map_ac_trap_tooltip`, `str_map_ac_obstacle_tooltip`, `str_map_ac_hidden_door_tooltip`; `panel.map.darkest: map_layout .hallway_tooltip_y_offset 10` | `Scouting.cs:112-124` has the words; `Minimap.cs:101-111` gives tiles and markers no pointer | a marker on a hallway says nothing | medium | high |
| **E2** room tooltip place | `map_layout .room_tooltip_y_offset 10` | `DungeonHud.cs:324-328`: 24 right of and 24 above the icon's middle, growing upwards, 260 wide | own numbers | low | low |
| **E3** panning | `[pc]str_help_raid_hallway_3`: "[RIGHT-CLICK] and [DRAG] to pan map" | `Minimap.cs:275-282`: a drag with any button | the left button pans as well | low | medium |
| **E4** the map's scale | `map_layout .scale 1.00`; only the player zooms | `Minimap.cs:183-208` (`FitZoom`): draws back by itself until every reachable room is in the window | the map is often smaller than DD1's | low | high (deliberate) |
| **E5** destination mark | `panels/icons_map/moving_room.png` (64x64, blue arrows) | `Minimap.cs:131-135, 264-266`: on the reachable room under the pointer | DD1 most likely marks the room the party is walking to; the mod never marks that one | low | low |
| **E6** home button's tooltip | `home_button_layout .tooltip_offset 1206 28 .tooltip_width 200`: counted from the panels' corner (240,720) that is left of the button and level with it; `reorder_party_layout .tooltip_offset 1206 90` fits the same reading | `DungeonHud.cs:345-349`: above the button, 260 wide | place and width | low | medium |
| **E7** party-order button | `panel.tab.darkest: reorder_party_layout .button_pos 678 90 .on_off_transition_time 0.25 .desat_level 0.2`; art `panels/party_order_button.png` (38x46) | read (`RaidLayout.cs:240`), never drawn | button missing | medium | low |
| **E8** wheel step | `map_layout .zoom_key_value 0.25` (by its name a key's step) | `Minimap.cs:284-290`: the wheel uses it | may not be DD1's wheel step | low | low |

Needs eyes: whether `hall_door.png` shows at a hallway's ends (the mod leaves it out); what `fx/map_radar`
(animation `pulse`: a 153 px ring and glows) is drawn on; whether `moving_room.png` is hover or destination; whether
the party-order button shows with a mouse; what `home_button_layout .announcement_time 1.0` announces.

## 6. Scouting reveal on the map

Matches DD1: the order and pace of a reveal from `fog_of_war` (`time_between_reveals 0.4`,
`maximum_total_reveal_time 2.0`, `tile_content_reveal_time 0.4`, `tile_content_reveal_max_scale 1.3`,
`scouting_text_scale_time 0.25`); `panels/icons_map/scoutingbanner.png` (366x63) with `str_scouting`.

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| **F1** the banner's word | style `scouting_text` = dwarven_axe_medium (line height 40: drawn 1:1 it is size 34 in the mod's fonts); colour `scouting_text` = notable (200 180 110) | `Minimap.cs:146`: size 26, `UiKit.Parchment` (222 209 179) | three quarters of DD1's size and paler | low | high |
| **F2** a tile coming out of the dark | `fx/scout_hallway`, `fx/scout_room` (animation `scout`: a black cover); `fog_of_war .tile_reveal_time 0.4` | `Scouting.cs:219-221` computes the fade; `Minimap.cs:210-232` does not use it: an icon switches at once, only markers pop | no fade | low | medium |
| **F3** how long the banner stays | not in a file | `Scouting.cs:32`: holds 1.2 s, fades in 0.4 s | own numbers | low | low |

Needs eyes: where the banner hangs (the mod: top of the map window, 6 px down, `Minimap.cs:145`).

## 7. The party's overlays in the corridor

Matches DD1: a 100x10 health bar at `y_pos 698` in the `tray_health_bar_*` colours; ten pips from `stress_offset -1 12`
every 10 with `overlays/stress_pip_empty.png` / `stress_pip_full.png`; `overlays/selected_1.png` under the selected
hero, `target_h_1.png` under a companion a camping skill may go to; a click selects, a right click opens the sheet.

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| **G1** status icons over the bars | `screen.raid.status_bars.darkest: status_bars .tray_icon_left_offset 58 -38 .tray_icon_left_spacing 20 .tray_icon_right_offset 62 -38 .tray_icon_right_spacing 20 .tray_icon_hot_spot_size 20 24`; art `overlays/tray_*.png` (24x24: buff, debuff, bleed, poison, disease, deathsdoor, afflicted, virtued, town_event, resolve_xp_bonus, trap_disarm ...); tooltips `tray_icon_tooltip_*` (for a camping buff: `tray_icon_tooltip_buff_until_camp_format` "%s (Until Camp)") in `tray_icon_tooltip_positive` / `_negative` | none: `RaidOverlays.cs:97-133` builds bar, pips and brackets only | no icon for camping buffs, death's door, disease or any other lasting state | high | high |
| **G2** announcement banners | `screen.raid.darkest: announcement .frame_pos_left 572 184 .frame_pos_centre_bottom 960 668 .frame_pos_centre_top 960 210 .frame_pos_right 1348 184 .text_offset 0 -29`; `announcement_times .trap 1.0 .disarm 0.5 .curio 0.50 .curio_purge 1.2 .new_quirk 1.5 .surprised 1.0 .deaths_door 1.2`; art `overlays/announcement_frame.png` (619x136); styles `banner_header` (dwarven_axe_large), `banner_sub_header` (ubuntu_small); `scripts/timescript/announcement_show.times` (scale 0.8 to 1.0 in 0.7 s, easeOutElastic; text alpha in 0.25 s); words `str_its_a_trap` "TRAP!" (`trap_announcement` = harmful), `str_ui_disarm` "Disarmed!" (`disarm_announcement` = notable), `str_ambush_title` "Ambush!", `surprise_announcement` "Surprised!", `curio_announcement_purge_format`, `str_new_quirk_colon`, `str_ui_deathdoor` | none; the news goes to the mod's log in its own words: `DungeonRun.cs:645` "The trap is disarmed.", `:925` "... springs a trap!", `:1113` "Ambush! The camp is attacked in the night." | DD1's banner is missing everywhere | high | high |
| **G3** numbers and words popping over a hero | style `pop_text` = `fonts/popup.fnt` (DwarvenAxe 68, outline 4); `scripts/layout/base.popup_text.layout.darkest`: 42 layouts, for example `pop_text_layout_damage .start_offset 0 20 .pop_y_offset 100 .pop_time 1.5`, `pop_text_layout_stress_damage .start_offset 0 120 .pop_y_offset -80 .pop_time 1.0`, `_hero_heal`, `_stress_reduce`, `_full`, `_cured`, `_buff`; colours `pop_text_*` with `pop_text_outline_*`; icons `overlays/poptext_*.png` (64x64) | none: trap damage, hunger, curio effects, camp meals and skills, items change the bars silently and write a log line; `Dd1/Dd1Fonts.cs:64-65` builds four fonts, not `popup` | no pop text outside fights | high | high |
| **G4** the health bar changing | `status_bars .health_bar_damage_show_time 0.6 .health_bar_damage_lerp_time 0.5 .health_bar_damage_lerp_easing_function easeOutSine`, the same three for heal; colours `tray_health_bar_default_damage_top/bottom` #ffa500 / #a32a00, `tray_health_bar_default_heal_top/bottom` #00e500 / #004e21 | `RaidOverlays.cs:206-207`: the bar is resized at once | no strip for what was lost or won | low | high |
| **G5** tooltip of the bars | `status_bars .status_bar_tooltip_hot_area_offset 50 -10 .status_bar_tooltip_hot_area_height 35 .status_bar_tooltip_offset 50 -12`; two lines: `tray_status_bar_tooltip_health_format` (in `tray_status_bar_tooltip_health`), `tray_status_bar_tooltip_stress_format` | `RaidOverlays.cs:104-111, 148-155`: the hot area is the 110x300 zone over the hero; the box stands 44 px above the bar; the hero's name is added as a title | area, place, and a title DD1 does not have | low | medium |
| **G6** stress pips | ten pips for 0..100 and a second layer, `overlays/stress_pip_full_overstressed.png`, for 100..200 | `RaidOverlays.cs:209-220`: a pip a point of DD2's 0..10 | no second layer | low | high (deliberate) |
| **G7** heroes speaking (barks) | `shared/ui.layout.darkest: bark_layout .offset -25 300 .tail 62 180 .text 40 320 24 122 .char_delay 0.03 .lifetime_min 1.0 .lifetime_per_char 0.06`; art `scrolls/bark_balloon.png` (359x180), `_afflicted`, `_heroic`, `_side` (396x142); style `bark` (ubuntu_medium); colours `bark_neutral`, `bark_affllicted`, `bark_virtued` | none in the corridor or at a camp | heroes never speak | medium | high |
| **G8** effects on a hero | `fx/halo` (animations `afflicted`, `deaths_door`, `disease`, `heroic`, `quirk`, `surprised_hero`), `fx/stress_cloud`, `fx/interaction_curio` (`heroic`, `afflicted`), `fx/bandage`, `fx/antivenom`, `fx/holy_water`, `fx/laudanum`, `fx/medicinal_herbs`, `fx/cure_target` | none: no `fx/` path is used under `Dungeon/` | nothing plays on the hero when a curio, an item or a camping skill acts | medium | medium |
| **G9** small motions of a tray | `screen.raid.darkest: status_bar_tray_pulse .pulse_out_duration 0.25 .pulse_in_duration 0.125 .pulse_scale 0.5 0.5`, `status_bar_tray_icon_pulse`, `overlays .fade_in_time 1.0 .fade_out_time 1.0` | none | static | low | medium |

Needs eyes: the bracket's height (the mod puts its bar 3 px above the health bar, `RaidOverlays.cs:32-35, 113`;
`overlays.hero_start_pos 788 680` fits no corner of the picture); the trays under DD2's models against DD1's 168 px
step (the mod keeps them 112 px apart at least, `RaidOverlays.cs:39, 189-191`); which of the four frame positions
serves which announcement; what the `status_bars .icon_offset 50 30 .icon_world_y_offset 149` icon over a hero is.

## 8. Torch meter

The widget is DD2's by the owner's choice. What hangs on it:

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| **H1** tooltip text | title `torch_info .titleIdFormat "str_darkness_title_%d"`; a line per value of `shared/rules.json: darkness` (`player_scouting_increase`, `monsters_surprised_increase`, `heroes_surprised_increase`, `player_crit_increase`, `player_def_increase`, `monster_attack_increase`, `monster_damage_increase`, `monster_crit_increase`, `stress_chance_increase`, `stress_damage_increase`, `loot_increase_gold`, `loot_increase_treasure_draws`) under `str_darkness_player_scout`, `_monstersSurprised`, `_heroesSurprised`, `_player_crit`, `_player_def`, `_monster`, `_monster_crit`, `_stress`, `_loot`, `_battle`, `_hunger`, `_hazardous_events` | `RaidOverlays.cs:379-388`: DD1's title, then "Torch n", the scouting line, "In a fight the flame burns at n." | two invented lines; all of DD1's list but scouting missing | medium | medium (deliberate in part) |
| **H2** hot areas and the tooltip's place | `torch_info .mouseOverAreaSize 200 130 .stripMouseOverAreaOffset 0 70 .stripMouseOverAreaSize 860 24 .tooltip_offset 0 -70` | `RaidOverlays.cs:282-297`: DD1's two areas at DD1's place, the tooltip at y 210 (28 + 188 - 6); the widget on screen is DD2's at `Dd2Torch.cs:35` (0, -42) | areas and tooltip are placed for a picture that is not shown | low | low |
| **H3** reducing the torch | `str_reduce_torch_tip` "[SHIFT+CLICK] to reduce torch", `str_snuff_torch_tip` "[SHIFT+CTRL+CLICK] to snuff out torch", colours `torch_reduce_tip`, `torch_snuff_tip` (70 70 70) | none | no tip, no shift-click | low | medium |

Needs eyes: where the tooltip should hang under DD2's widget, and whether the pointer finds it where the flame is drawn.

## 9. Quest info, retreat, quest complete (top left)

Matches DD1: `overlays/quest_log.png` centred on 65,58 from 12,20; name in `raid_quest_info_name` at 110,26, goal in
`raid_quest_info_goals` at 110,58; `panels/retreat_button.png` centred on 68,120; DD1's confirm dialog with DD1's
question and answers (`retreat_confirm_raid_question`, the Darkest Dungeon's own question); the choice at
`complete_mid_screen_pos 948 590` with `quest_complete_choice_shared_frame.png`, `quest_return_to_hamlet.png`,
`quest_continue_raid.png` and their DD1 words.

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| **I1** when name and goal show | `quest_info .size 300 150 .fade_time 0.1 .fade_easing_function easeInSine`; `overlays/quest_log_glow.png` (374x158: an ink band right of the seal, a parchment tail under it) | `RaidOverlays.cs:444-451`: glow, name and goal always on | DD1 may show them only while the pointer is on the seal | medium | low |
| **I2** goal line | `town_quest_goal_start_plural_explore_room` "Explore %.0f%% of rooms.", `town_quest_goal_start_plural_battle_room` "Complete %.0f%% of room battles.", `town_quest_goal_start_plural_gather` / `_single_gather`, `_activate`, `_kill_monster` (the mod's town panel uses them: `Estate/QuestMapText.cs:145-152`) | `DungeonRun.cs:258-287`: "Explore rooms  3/9", "Win room battles", "Gather relics", "Activate the altars", "Slay the master of this place"; "Quest complete" in #dcb55c | the mod's words and its own count | medium | medium |
| **I3** quest name's box | `quest_info .size 300 150` | `RaidOverlays.cs:414-415, 447-448`: 384 wide, shrinking to size 20 | wider, may shrink | low | high (deliberate) |
| **I4** retreat tooltip | `retreat_raid_tooltip` "Abandon Quest", colour `raid_retreat_tooltip` = neutral, at `quest_info .retreat_tooltip_offset 148 -18` | `RaidOverlays.cs:456` takes `DungeonRun.LeaveLabel` (`DungeonRun.cs:295`: "Abandon the Quest" / "Return to the Hamlet") whenever an expedition exists: DD1's string is never reached | wording | low | high |
| **I5** camping buffs beside the retreat flag | `quest_info .camp_bonus_icon_offset 140 110 .camp_bonus_icon_spacing 50 0 .camp_bonus_icon_scale 0.5 .camp_bonus_icon_tooltip_max_width 240 .camp_bonus_icon_selected_overlay_offset 0 20`; the skill's icon at half size (36 px), `overlays/quest_info_camping_bonus_selected_overlay.png` (44x7); styles `quest_info_camp_bonus_skill_tooltip` (notable), `quest_info_camp_bonus_skill_stats_tooltip` (neutral); words `str_quest_info_camp_bonus_*` | none; `DungeonRun.cs:877` only writes "What the camp gave the party has worn off." | the party's standing camp buffs are not shown | medium | high |
| **I6** the seal in the corner once the quest is complete | `fx/quest_complete_seal` (Spine: `appear`, `idle_exploration`, `idle_combat`; parts aura 322x312, glow 319x260, seals 115x149) at `quest_info .complete_button_offset 0 30` | `RaidOverlays.cs:461-471`: the still picture `overlays/quest_complete.png` (120x152) over `quest_log_glow.png` moved 30 px down | no appear, no glow; the ink band behind it is a guess | medium | medium |
| **I7** the moment the goal is met | `fx/quest_complete_crest` (Spine: a crest with wings, shield and five seals), `str_quest_complete_banner` "Quest Complete!", `quest_info .complete_blur_time 0.2`, then the frame with the two buttons | `RaidOverlays.cs:494-514`: the still seal centred on the point, the frame, the two buttons, a 45% shade; `str_quest_complete_banner` is loaded (`RaidUi.cs:29`) and unused | no crest, no banner words, a shade for the blur | medium | medium |
| **I8** tooltips of the two ways | style `quest_complete_tooltip` (ubuntu_medium), colour neutral; `quest_info .complete_tooltip_offset 0 10 .complete_tooltip_width 200` | `RaidOverlays.cs:517-526`: `tooltip` style (ubuntu_small) coloured with `quest_complete_text` (notable) | smaller font, gold for grey | low | medium |
| **I9** leaving the choice | one of the two buttons | `RaidOverlays.cs:502-504`: a click anywhere on the shade continues as well | an extra way out | low | low |

Needs eyes: I1 first of all; what `quest_info .retreat_announcement_time 1.0` shows after "Yes"; how crest, words,
frame and buttons follow each other when the goal is met; whether the corner seal has the ink band behind it.

## 10. The mod's own: the log and the HUD's line

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| **J1** log lines under the quest info | none: no layout entry, no style | `DungeonHud.cs:50-53, 292-293, 714-728`: the last three lines at 24,196 in a 470x84 box, style `raid_quest_info_goals`, fading after 9 s; every line is the mod's wording (`Say(...)` in `DungeonRun.cs`) | an element DD1 does not have; it stands in for G2, G3, G7 and K2 | medium | high (deliberate) |
| **J2** the HUD's line over the inventory | `user_information/user_information.darkest: user_information_popup .y_offset -60.0 .time 4.0`; style `user_information_popup` (ubuntu_small), colour `user_information_neutral` (143 8 0); words `str_user_information_*` | `DungeonHud.cs:54-56, 295-300, 731-753`: a tooltip box 16 px in from and 8 px above the right panel, in notable gold, 4 s | a fixed place, gold on a box; DD1: a dark red line 60 px above the pointer | low | medium |

## 11. Curio prompt and "use item"

Matches DD1: `scrolls/event_scroll_sidebar.png` hanging from `sidebar_scroll .pos 1348 200`; heading at `header_y 44`
in `scroll_header`; text at -152,128, 330 wide, in `scroll_body`; `scrolls/byhand.png` at -152,240, the slot
(`use_inventory.png`) at -34,230, `scrolls/pass.png` at 75,240; title, text and the hand's tooltip from
`str_curio_title_*`, `str_curio_content_*`, `str_curio_tooltip_investigate_*`.

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| **K1** curios that open without a window | `curios/curio_type_library.csv`: "FULL CURIO?" is No for `discarded_pack`, `sconce`, `crate`, `sack` (and `ancestors_knapsack` and the tutorial props); none of them has a `str_curio_title_*` or `str_curio_content_*` line | `DungeonRun.cs:790-803` asks about every curio; `RaidScrolls.cs:128-150` shows the sidebar scroll with a title made from the id ("Discarded Pack") and the text "The party finds a discarded pack."; `Core/CurioCatalog.cs:234` reads the flag's label and drops its value | a window with invented words where DD1 opens the thing on a click | high | medium |
| **K2** what an interaction did | `curios.string_table.xml`: `str_curio_<id>[_<item>]_<result>` (76 loot, 51 effect, 37 nothing, 18 quirk, 9 disease, 6 purge lines; `str_curio_heirloom_chest_nothing` "The chest is empty.", `str_curio_heirloom_chest_skeleton_key_loot` "The key unlocks a hidden compartment!"); the same sentences are column "STRING" of `curio_type_library.csv` | `DungeonRun.cs:941-965, 1013-1040`: log lines of the mod ("Nothing happens.", "Something stirs (Quirk: ...).", "... feels calmer.", "An odd sensation passes (...)."); `RaidUi.cs:33` loads title, content and investigate tooltip only | DD1's sentence is never shown; some of the mod's lines show raw ids | high | medium |
| **K3** words under the hand and the way past | the layout places words for a controller only (`sidebar_scroll .investigate_controller_text_offset 38 2 .investigate_controller_text_width 200`, `.pass_controller_text_offset 38 0`); for the pointer it gives tooltips (`.investigate_tooltip_text_width 160`, `.pass_tooltip_text_width 160`) | `RaidScrolls.cs:279-287, 503-524`: captions under both pictures ("Investigate", "Leave it": `DungeonRun.cs:792, 802`), 9 px above the art's foot, 116 wide (own numbers) | captions DD1 most likely does not draw with a mouse | medium | medium |
| **K4** tooltip of the way past | `curio_tooltip_pass` "Ignore" (style `curio_pass_tooltip`, neutral) | none: `RaidScrolls.cs:141-149` sets a tooltip for the hand only | missing | low | high |
| **K5** tooltip of the item slot | `curio_tooltip_item_slot` "[OPTIONAL] Drag an item here to use it on this object.", `curio_tooltip_quest_item_slot` "Drag QUEST ITEM from inventory here.", `curio_tooltip_item_slot_gather_curio`; `sidebar_scroll .item_slot_tooltip_offset 0 8 .item_slot_tooltip_text_width 160` | `RaidScrolls.cs:296-300, 347-356`: "Nothing in the bag is of use here." / "Use an item" with "Click it in the inventory: a, b." / the answer's own words; beside the slot's top right corner, 200 wide | the mod's words, and they name the items that work | medium | high |
| **K6** the slot's two pictures | `scrolls/use_inventory.png`, `use_inventory_active.png` (72x144; the active one has a gold edge) | `RaidScrolls.cs:292-295`: the active one whenever the bag holds an item the curio reacts to, else the plain one at 60% | the slot gives away whether anything fits; DD1's rule for "active" is not in a file | low | low |
| **K7** an item lying in the slot | empty until the player puts one there | `RaidScrolls.cs:302-310, 326-337`: when one kind of item fits, it lies in the slot with its count and a click uses it | the mod picks for the player | low | medium |
| **K8** which string a curio uses | `curios/curio_props.csv`, column "UI String Name" (`tutorial_shovel` and `thanks_chest` point to `unlocked_strongbox`) | `RaidScrolls.cs:142-146` looks up `str_curio_title_<prop id>`; `CorridorProps.cs:434-436` (`PropText`) goes through the UI name and is not used here | a prop whose UI name differs gets made-up words | low | high |
| **K9** a curio that needs a quest item | the hand in `curio_investigate_hand_disabled` (#666, saturation 0.2); `str_curio_tooltip_investigate_corrupted_altar_with_quest_item` "This requires use of a QUEST ITEM from inventory." | `DungeonRun.cs:794`: a quest curio gets no item answers; the hand stays lit | no disabled hand, no DD1 line | low | medium |

Needs eyes: K1 (do crate, sack, pack and sconce really open without a window); where DD1 writes a result sentence
(`screen.raid.darkest: result_scroll .pos 1342 140 .header_y 52 .body_y 224 .body_width 286 .button_y 340 .button_size 384 36`
may be it, with `str_ui_ok` "[OK]"; the mod reads none of it); captions under the pictures with a mouse; whether the
scroll's art is centred on `pos.x` (by the corner reading the text spans -152..+178, the slot's middle is +6, the two
pictures' middles are -90 and +137); the colour of headings and text on scrolls (`base.colours.darkest` has no
`scroll_header` or `scroll_body`; the mod uses notable and neutral); whether event scrolls slide in; what
`skip_curio_display` (`.pos 1760 575`, style `skip_curio_text`) is and when it shows; what
`scrolls/choice_button_frame.png` (124x69, `choice_button .frame_offset 40 -10`) frames.

## 12. Trap, obstacle, secret door, plain questions

Matches DD1: an obstacle on the sidebar scroll with `str_obstacle_<id>_title` / `_description`, the hand with
`obstacle_tooltip_clear_by_hand`, the slot for the shovel.

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| **L1** a spotted trap | no window: `screen.raid.darkest` has no scroll for a trap; DD1 plays `disarm_intro.times` and announces "Disarmed!" or "TRAP!" (G2); the chance is the hero's (`resistance_trap_disarm_format` "Trap Disarm: %.0f%%") | `DungeonRun.cs:641-647` asks "A trap lies ahead." / "Disarm it" / "Walk through" with a fixed 0.6 chance; `RaidScrolls.cs:121-127`: sidebar scroll headed "Trap" (`str_map_ac_trap_tooltip`), hand and way past | an invented window at every spotted trap | high | medium (deliberate) |
| **L2** a found secret door | no window: a place in the hallway that is clicked (on the map `str_map_ac_hidden_door_tooltip`) | `DungeonRun.cs:675-678`: "A secret door." / "Enter" / "Walk on" on the basic scroll as a plain question | an invented window | medium | medium |
| **L3** obstacle: the way past | styles and colours `obstacle_pass_text`, `obstacle_pass_tooltip` stand beside the investigate ones: the scroll has a curio's three controls | `RaidScrolls.cs:103-119`: hand and slot only | no way past on the scroll | medium | medium (deliberate) |
| **L4** obstacle: words | `str_user_information_no_shovel` "You don't have a shovel!" | captions "Clear it by hand" / "Use a shovel (n carried)" (`DungeonRun.cs:731-732`); no "no shovel" line | captions as K3; DD1's refusal unused | low | medium |
| **L5** plain questions | DD1 asks with the confirm dialog (`shared/confirm_dialog`): `not_enough_room_confirm`, `cant_discard_quest_item_confirm` | `RaidScrolls.cs:388-407`: the basic scroll with answer rows on `scrolls/choice_button_frame.png` (124x69 stretched to 384x36) in style `confirm_dialog_answer` | an invented kind of window on stretched art | low | medium |

Needs eyes: how a disarm starts in DD1 (a click on the trap with a hero chosen, or walking onto it).

## 13. Hunger

Matches DD1: `scrolls/event_scroll_basic.png` from `basic_scroll .pos 1342 140`; heading `str_ui_hunger_title` at
`header_y 48`; `scrolls/inset_hunger.png` at 0,145; text at `body_y 308`, 345 wide; `scrolls/eat.png` at -150,400 and
`scrolls/starve.png` at 78,400.

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| **M1** text | `str_ui_hunger_content` "The exertions of adventuring have produced a growing hunger amongst the party." | `DungeonRun.cs:764-765`: "The party grows hungry. The bag holds n food." | the mod's words | medium | high (deliberate) |
| **M2** tooltips of the two pictures | `str_ui_hunger_choice_eat` "Eat %.0f food, regain %d%% health", `str_ui_hunger_choice_starve` "Eat nothing, take %d%% damage plus stress damage"; `basic_scroll .ok_tooltip_width 200 .cancel_tooltip_width 200` | `DungeonRun.cs:764`: "Eat n food: +h% health", "Go hungry: -d% health, stress", shown as tooltips by `RaidScrolls.cs:375, 384, 510` | the mod's words where DD1 has the sentence with places for the numbers | medium | high |
| **M3** words under the bowls | controller places only (`basic_scroll .ok_controller_text_offset 38 2`); styles `event_hunger_eat_text`, `event_hunger_starve_text` (ubuntu_medium, notable) | `RaidScrolls.cs:375, 384`: "Eat" / "Starve" (`str_ui_eat`, `str_ui_starve`) under the pictures | as K3 | low | low |

Needs eyes: where DD1 says there is too little food (the mod leaves a dark bowl with `str_meal_not_enough_provisions`
"(Not Enough Food)" under it, `RaidScrolls.cs:376-382`; the string's id belongs to the camp's meal).

## 14. Loot window

Matches DD1: `scrolls/event_scroll_loot.png`; heading at 228,40 and line at 228,136 (350 wide) from
`str_overlay_loot_chest_*`; cards from y 195 every 74; Take All at 80,358 and Close at 306,358 with
`str_overlay_loot_take_all` / `str_overlay_loot_close`; a card is taken by a click; what does not fit stays.

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| **N1** loot after a fight | `overlay.loot.darkest: loot_title .textFormat "str_overlay_loot_%s_title"`: `str_overlay_loot_battle_title` "Victory!", `str_overlay_loot_battle_description` "You have found:" | none: `DungeonRun.cs:855-895` goes straight on after a won fight; the exploration has no loot event for a fight | DD1's loot scroll after a battle does not exist | high | high |
| **N2** heading by where the loot comes from | `str_overlay_loot_chest_*`, `str_overlay_loot_camping_*` ("Loot!" / "Available for the taking..."), `str_overlay_loot_quest_*`, `str_overlay_loot_hero_death_*`, `str_overlay_loot_none_*` | `DungeonRun.cs:1003`: always the chest pair; a camping skill's loot goes into the bag unasked (`:1175-1195`) | one heading for everything; no scroll at a camp | medium | high |
| **N3** Take All by key | `[pc]menu_controls_element_2_7`: "SPACE - take ALL when LOOT window is up" | none | no key | low | high |
| **N4** words under Take All and Close | controller places only (`loot_buttons .take_all_controller_text_offset 38 2 .close_controller_text_offset 38 2`); tooltips in `loot_take_all_tooltip`, `loot_close_tooltip` | `RaidScrolls.cs:452-453`: captions under `byhand.png` and `pass.png` (both pictures are the mod's guess) | as K3; the pictures are a guess | low | low |
| **N5** a card's tooltip | `inventory_item_tooltip_layout .offset 85 0` (beside the card) | `RaidScrolls.cs:439-449`: under the card, 4 px down | place | low | medium |
| **N6** no room | `not_enough_room_confirm` (by its id a confirm dialog), `str_user_information_not_enough_room` "Not enough room!" | `DungeonHud.cs:454, 475`: the HUD's line | shown elsewhere | low | low |
| **N7** backdrop under many cards | `loot_background .min_items 4 .max_items 9 .dynamic_backdrop_y_offset 10`; pieces `event_scroll_loot_left_edge.png` (16x163), `event_scroll_loot_mid_section.png` (72x163), `event_scroll_loot_right_edge.png`; also `event_scroll_loot_overflow.png` (544x163) | `RaidScrolls.cs:428-436`: the pieces from five cards on, a middle piece stretched from 72 to 74 px; `event_scroll_loot_overflow.png` is never used | 2 px of stretch; one DD1 picture unused | low | low |

Trinkets on the scroll: see D9. Needs eyes: where the loot scroll hangs (`loot_background .pos 0 0` places the parts
inside it; the mod hangs it at `basic_scroll.pos`, `RaidScrolls.cs:420`); the two button pictures; when
`event_scroll_loot_overflow.png` is drawn.

**State after "one inventory" (2026-10-05, built, not yet seen in game):** N1, N2, D9 and C9 are in.
- N1: a won fight pays DD1's battle loot (`Core/BattleLoot.cs`, the `battle_loot` block of `Data/dungeons.json`)
  on the loot scroll under "Victory!" / "You have found:", before the room's curio and the quest-complete
  choice; DD2's own loot window never opens.
- N2: the heading and the line go by where the loot came from (`Dungeon/RaidLoot.cs`, `LootSource`): chest,
  battle, hero_death ("Reclaimed:" / "From the fallen hero..."). Left: a camping skill's loot still goes into
  the bag unasked; "quest" and "none" have a heading and no caller.
- D9: a trinket found is a card on the scroll and in the bag (DD1's card of its rarity, DD2's picture of the
  trinket, no number: DD1's frame `raid_loot_window_victory_two_items.png` shows a trinket's card in the bag
  without one).
- C9: outside a fight a click on a trinket of the bag puts it on the selected hero, a click on a worn one takes
  it off into the bag (a click where DD1 drags).
- "Needs eyes" answered from DD1's own frames (`_lab/dd1_ref/raid/raid_loot_window_after_fight.png`,
  `raid_loot_window_victory_two_items.png`, matched against the art pixel for pixel): the loot scroll's picture
  stands with its top left corner at **1120,200**, that is at `sidebar_scroll.pos` (1348 200), not at
  `basic_scroll.pos`; the two button pictures are `byhand.png` and `pass.png` as guessed, standing at 58,348
  and 284,347 of the scroll where the layout says 80 358 and 306 358 (22 px left, 10 px up); there are no
  words under them (N4). All three are in `RaidScrolls.ShowLoot` now. The same 22 / 10 px hold on the sidebar
  scroll (`raid_curio_window_chest_room.png`, `_tent.png`, `raid_obstacle_window_rubble.png`: `byhand.png` at
  1174,430 and `pass.png` at 1401,429 of the screen, where the mod puts them at 1196,440 and 1423,440): NOT
  changed, the curio window is not this work's.

## 15. Camping screen

Matches DD1: `scrolls/meal_scroll.png` with `str_ui_meal_title` at `headerY 52` and four rations from `buttonY 148`
every 74; `scrolls/event_scroll_campingrespite.png` with `camping_respite_title` at 60,44, the points in
`camping_points` ending at 324,44, `camping_respite_description` at 56,130 (360 wide), `overlays/announcement_rest.png`
at 100,200 with `camping_respite_rest` in `rest` at 128,17; the selected hero's camping skills in the banner's places
(`raid/camping/skill_icons/camp_skill_<id>.png`); a click on a hero picks whose skills show or whom a skill is for.

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| **O1** where the scrolls hang | `screen.raid.darkest: camp_layout .respite_scroll_pos 732 60` (the scroll is 456 wide: centred on 960) | `CampScreen.cs:42`: 1420, 40 | at the right edge instead of top centre | medium | high (deliberate) |
| **O2** how a scroll comes and goes | `camp_layout .scroll_animation_offset -400 .scroll_animation_time 0.15 .respite_fade_out_value 0.0 .respite_fade_out_time 0.15` | `CampScreen.cs:375-390`: built in place at once | no slide, no fade | low | high |
| **O3** a camping skill's cost | in the tooltip: `camping_skill_cost` "Time Cost: %d" (colour `attacktype`), `camping_skill_uses_remaining_format` "Uses Remaining: %d" | `CampScreen.cs:191-192` with `RaidHeroPanel.cs:351-357`: a number in the icon's corner (`inventory_amount`; red when too dear, "-" when used up); tooltip `CampScreen.cs:181-183`: "n respite   m uses left" | an invented badge; DD1's two tooltip lines unused | medium | medium |
| **O4** what a camping skill does | `camping_skill_selection_self` "Self Only:", `_individual` "One Companion:", `_party_other` "All Companions:", `_party` "Party:"; `camping_skill_effect_*` ("-%d Stress", "Heal %d%% HP", "Remove Bleeding" ...); `camping_skill_requirement_effect_format` "If %s: %s"; `camping_skill_chance_effect_format` "(%d%% chance) %s" | `CampScreen.cs:184` with `CampContent.cs:160-210`: "Self", "One companion", "-n stress", "(religious)", "(if shaken)", "stops bleeding" ... | the mod's wording throughout | medium | high |
| **O5** a skill refused | `str_user_information_not_enough_camping_points` "Not enough Camping points!", `str_user_information_already_used_camping_skill` "Already used this skill!", as the line at the pointer (J2) | `CampScreen.cs:179, 228-234, 283-288`: the core's own sentence in the box under the scroll and in the tooltip | words and place | low | medium |
| **O6** the box under the scroll | none | `CampScreen.cs:43-47, 130-135, 343-372`: what to do next, refusals, the camp's last three log lines, 456 wide, 44 px under the scroll | an element DD1 does not have | medium | high (deliberate) |
| **O7** meal cards | `meal_scroll .buttonY 148 .buttonOffset 74`; a meal's name only in its tooltip (`.tooltipTitleIdFormat "str_meal_title_%d"`) | `CampScreen.cs:419-425`: the food needed as a count on the card, the meal's name on the card's foot | two invented labels a card | low | medium |
| **O8** meal tooltip | title `str_meal_title_%d`; lines `str_meal_heal_format` "%+d%% HP", `str_meal_stress_format` "%-d Stress"; `meal_scroll .tooltipOffset 170 -10 .tooltipWidth 180` | `CampScreen.cs:427-435`: "n food", the mod's effect words "for every hero", a refusal; hung left of the scroll | wording and side | medium | medium |
| **O9** resting with points left | not in a layout; `shared/rules.json: camp_rest_point_threshold 6`, `camp_rest_number_of_hero_threshold 1` are read by nothing in the mod | `CampScreen.cs:246-257`: a second click after "Respite points remain. Click again to rest all the same." | an invented step and sentence | low | low |

"Ambush!" at night: see G2. Meal and skill effects over the heroes: see G3. Needs eyes: where the meal scroll hangs
(`meal_scroll` has no position); what the four meal buttons are (the mod: the four `inv_provision+_N.png` cards); how a
used-up or too dear camping skill looks; the colour of "REST" (no `rest` colour id; the mod: notable); whether DD1
asks before resting with points left.

## 16. Shared pieces: buttons, tooltip, confirm dialog, text, pointer

Matches DD1: the tooltip on `shared/tooltip/tooltip_background.png` cut at `border_texture_threshold 0.15`, text in
`tooltip`; the confirm dialog (`confirm_dialog.background.png` 840x600 from 960,200; question at -18,200, 520 wide,
in `confirm_dialog_question`; answers from 0,390 every 55 in `confirm_dialog_answer`;
`confirm_dialog.answer_text_selected_overlay.png` under the pointer).

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| **P1** picture buttons at rest | `colours/base.colours.darkest: button_highlight .hvec4 1.5 1.5 1.3 1.0`: the art as it is at rest, brighter under the pointer | `RaidUi.cs:231-246`: rests at 0.82 of its art, is its art (1.0) under the pointer, 0.7 / 0.6 / 0.5 pressed, 0.32 disabled | the retreat flag, the home button, the seal, the hand, the way past, the bowls, REST and the two ways home are darker than DD1's until hovered, and never brighter | medium | high |
| **P2** shade behind the confirm dialog | not in `confirm_dialog.layout.darkest` | `RaidOverlays.cs:565`: black at 50% over the whole display | perhaps not DD1's | low | low |
| **P3** text that does not fit | DD1 draws its styles 1:1 and wraps | `RaidUi.cs:191-211` (`Paragraph`, `Fit`): shrinks; eighteen labels of the dungeon HUD and the camp use one of them | sizes can differ from DD1's | low | medium |
| **P4** the pointer | `cursors/arrow.png` | DD2's cursor | not DD1's (shared with the town) | low | high |

Needs eyes: whether DD1 dims or blurs the scene behind the confirm dialog; the tooltip's inner margin (the mod: half
the border plus `text_offset 4`, `RaidUi.cs:330`).

## 17. Keys and pointer

Matches DD1: A and D walk, T lights a torch, a click on a room of the map walks there, a click on a hero selects, a
right click opens a sheet, a click on a curio turns to it, shift-click discards.

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| **Q1** entering a door, using what the party stands at | `[pc]str_help_raid_hallway_2`: "[CLICK] on it or press [W]"; `[pc]str_help_raid_hallway_4`: "[CLICK] on objects or press [W] to interact with them" | `CorridorView.cs:864-869, 881-882`: A, D, the arrows; no W | no key for doors and curios | medium | high |
| **Q2** character sheet by key | `[pc]menu_controls_element_2_8`: "C - Character (hero) panel details"; `[pc]menu_controls_element_2_3`: "Q / E - Cycle to next hero while viewing character sheet" | none | no C | low | high |
| **Q3** help | `[pc]menu_controls_element_0_0`: "H - Contextual help for any screen"; `shared/ui.layout.darkest: help_panel_layout`, `shared/help/help.background.png`; lines `[pc]str_help_raid_hallway_0..8`, `[pc]str_help_raid_room_0..9`, `[pc]str_help_raid_camping_0..3` | none | no help panel in the dungeon | low | high |
| **Q4** reordering the party | by the move skill (B1); the party-order button (E7) | none: `RaidOverlays.cs:186-192` only keeps the trays apart | no reordering outside a fight | medium | medium (deliberate) |

TAB is D1, SPACE is N3, the right button on an item is D3, the right button on the map is E3.

## 18. Edges of the scope

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| **R1** character sheet | `shared/character/characterpanel_bg.png`, `characterpanel_frames.png` (1395x776) at `shared/ui.layout.darkest: ui_screen_layout .character_pos 144 132`; contents by `shared/character/character.layout.darkest` (quirks 141 128, base stats 141 358, equipment 141 516, combat skills 780 156, camping skills 780 320, resistances 780 436) | `DungeonHud.cs:518-526` calls `Estate/UpgradeUi.cs:262-265`: DD2's own character sheet; not opened at a camp or while a question is up | a DD2 screen where DD1 has its own; quirks, resistances and camping skills cannot be read the DD1 way in the dungeon (the town shares this) | high | high |
| **R2** tutorial pop-ups | `shared/tutorial_popup/tutorial_popup.<topic>.png` (curio, loot, torch, hero_panel, map_nav, hallway_nav, disarm_trap, camping, quest_complete, scout_hidden_door, supplies ...), `tutorial_popup.layout.darkest` | none | no first-time explanations | low | high |

Narration in the dungeon uses the hamlet's box in style `subtitle_context_town` (`Estate/NarrationBox.cs:29`); DD1's
`subtitle_context_raid` is the same font and colour, so nothing shows; where DD1 puts a raid subtitle needs eyes
(MODLOG: the band covers the panel while a line is up).

---

## Appendix A: entries of DD1's raid files that nothing in the mod reads

For whoever takes the list above: these are where the numbers are.

- `screen.raid.darkest`: `announcement`, `announcement_times`, `pop_prop_times`, `result_scroll`, `skip_curio_display`,
  `choice_button`, `status_bar_tray_pulse` and its four siblings, `overlays .fade_in_time` / `.fade_out_time`;
  in `basic_scroll` and `sidebar_scroll` every `*_tooltip_offset` and `*_tooltip_(text_)width`; in `camp_layout`
  `scroll_animation_*`, `respite_fade_out_*`, `respite_rest_image_offset`; in `quest_info` `camp_bonus_icon_*`,
  `fade_time`, `retreat_announcement_time`, `complete_blur_time`.
- `screen.raid.status_bars.darkest`: `health_bar_damage_*`, `health_bar_heal_*`, `status_bar_tooltip_*`,
  `tray_icon_*`, `icon_offset`, `icon_world_y_offset`, `icon_tooltip_*`.
- `panel.map.darkest`: `map_layout .pos`, `.hallway_tooltip_y_offset`, `.room_tooltip_y_offset`;
  `home_button_layout .tooltip_offset`, `.tooltip_width`, `.announcement_time`.
- `panel.tab.darkest`: `reorder_party_layout` but for `button_pos`.
- `shared/hero/hero.layout.darkest`: `hero_equipment_layout .highlight_pos_offset`, `.tooltip_*`;
  `hero_stats_layout .icon_offset`, `.tooltip_*`.
- `shared/inventory/inventory.layout.darkest`: `inventory_curio_tracker_layout`.
- `scripts/layout/base.popup_text.layout.darkest`, `user_information/user_information.darkest`,
  `shared/ui.layout.darkest` (`bark_layout`, `help_panel_layout`, `ui_screen_layout`),
  `scripts/timescript/announcement_show.times` / `announcement_hide.times`.
- Colours of `base.colours.darkest` never asked for: `equipment_level_0..4`, `scouting_text`, `trap_announcement`,
  `disarm_announcement`, `user_information_neutral`, `inventory_selected`, `inventory_tooltip_gold_value`,
  `tray_health_bar_default_damage_*` / `_heal_*`, `tray_icon_tooltip_*`, `pop_text_*`, `bark_*`, `skill_unselectable`,
  `curio_investigate_hand_disabled`, `quest_complete_tooltip`, `quest_info_camp_bonus_*`, `raid_retreat_tooltip`,
  `torch_reduce_tip`.
- Fonts of `fonts/fonts.darkest` never built: `popup` (style `pop_text`), `pips`.

## Appendix B: seen in passing (rules, not looks; not counted)

- A curio's effect lands on a random hero (`DungeonRun.cs:1015`); DD1: "The selected hero will perform the
  interaction." (`[pc]str_help_raid_hallway_4`). The same for who springs a trap (`DungeonRun.cs:920`).
- A disarm succeeds at a fixed 0.6 (`DungeonRun.cs:644`); DD1 uses the hero's trap disarm chance.
- `Minimap.cs:308-346` (`RoomIcon`, `RoomName`, `Marker`) is dead code since `Scouting` took these over.
