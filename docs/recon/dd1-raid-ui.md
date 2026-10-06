# DD1's raid screen: the expedition's HUD, its windows, the camp and the provision screen

What DD1's files say about how its raid screen is laid out, and how the mod rebuilds it over DD2's corridor.
Everything is read from the player's DD1 install at runtime; a number below is DD1's stock value (the FALLBACK
in the code) with the file, block and key it comes from. Screen pixels of DD1's 1920x1080 layout, y down, art
at native size, text in DD1's styles at 1:1 (docs/recon/dd1-ui.md has the fonts).

Code: `Dungeon/RaidLayout.cs` (every number of this page), `Dungeon/RaidUi.cs` (DD1 strings, art, tooltip),
`Dungeon/DungeonHud.cs`, `RaidHeroPanel.cs`, `RaidOverlays.cs` (status bars, torch, quest info, confirm
dialog), `RaidTrayIcons.cs` (the status icons over the bars), `RaidAnnouncement.cs` (the banner),
`RaidPopText.cs` (numbers and words over a hero), `RaidFeedback.cs` (what sets the last two off),
`RaidScrolls.cs` (the event scrolls and the question model), `Minimap.cs`, `InventoryGrid.cs`,
`CampScreen.cs`, `ProvisionScreen.cs`, `RaidDev.cs` (bridge commands).
Offline preview: `python tools/preview_raid_hud.py` -> `_lab/preview/raid_*.png` (15 pictures; it prints a
line if any value had to fall back).

**Where the layout lives.** Not in `raid/*.layout.darkest` (there is no such file; `raid/` holds AI and
camping data). The raid screen's numbers are in `scripts/layout/`: `screen.raid.darkest`,
`screen.raid.status_bars.darkest`, `panel.banner.darkest`, `panel.hero.darkest`, `panel.map.darkest`,
`panel.tab.darkest`, `pannel.inventory.darkest` (sic), `overlay.loot.darkest`; what the hero panel holds is in
`shared/hero/hero.layout.darkest`, an item in a grid in `shared/inventory/inventory.layout.darkest`, the
tooltip in `shared/tooltip/tooltip.layout.darkest`, the confirm dialog in
`shared/confirm_dialog/confirm_dialog.layout.darkest`. Camping has no layout file of its own: its numbers are
`meal_scroll` and `camp_layout` of `screen.raid.darkest`.

**What the files do not say** is which point of a picture or a text a position names. DD1 mixes conventions
(a corner, the middle, the middle of the top edge). Each reading below was settled by the art: where the
picture has a hole, a band or a number drawn into it. Section 9 lists the readings that stayed guesses.

## 1. The frame (`screen.raid.darkest`)

| entry | value | meaning |
|---|---|---|
| `screen_guide.panel_top` | 720 | the scene is the top 720 px, the panels the 360 px under it |
| `screen_guide.safe_left` / `safe_right` / `x_centre` | 240 / 1680 / 960 | two 720 px panels: the left one from 240, the right one from 960 |
| `panels/side_decor.png` (250x360) | | left of the panels at x 0, and mirrored right of them (its last 10 px lie under the panels) |
| `panel_transition_bar.pos` | 0 710 | `panels/panel_transition.png` (1920x20) over the seam |
| `overlays.hero_start_pos`, `hero_spacing` | 788 680, -168 0 | where DD1's heroes stand, rank 1 first: 788, 620, 452, 284 |

## 2. Left panel: the selected hero

`panel.banner.darkest` (from the left panel's corner, 240,720)

| entry | value | meaning |
|---|---|---|
| `background_layout.pos` | -33 0 | `panels/panel_banner.png` (754x136; its first 40 px are clear) |
| `portrait_layout.pos` | 32 32 | 85 px portrait: the dark hole of the art is at x 29..119, y 30..120 |
| `portrait_layout.seal_pos` | 24 23 | `panels/seal.heroic.png` / `seal.affliction.png` around the portrait. Not drawn (3) |
| `name_layout.pos` | 272 38 | right edge and top of the name (`banner_hero_name`); the skills start at 280 |
| `name_layout.class_y` | 76 | top of the class (`banner_hero_class`), same right edge |
| `name_layout.hero_name_colour` / `hero_class_colour` | 177 161 108 255 / 154 152 143 175 | the two texts' colours (the styles have none in base.colours.darkest) |
| `ability_layout.pos`, `spacing` | 280 35, 76 | 72 px icons at 280, 356, 432, 508, 584: their middles are over the numbers 1..5 drawn into the art at x 349..653 |

Under the banner `panels/panel_hero.png` (720x224) at 0,136. `panel.hero.darkest` and
`shared/hero/hero.layout.darkest` (from the hero panel's corner, 240,856):

| entry | value | meaning |
|---|---|---|
| `health_layout.pos`, `.colour` | 130 11, #c00000 | "hp/max", centred on x 130 over the red band (x 30..220, y 10..36) |
| `stress_layout.pos`, `.colour` | 130 40, 150 150 150 | "stress/max" on the grey band |
| `stat_layout.pos`; `hero_stats_layout.spacing`, `value_offset` | 60 72; 160 20; 112 0 | a stat row every 20 px, its name at x 60, its value at x 172 (`stat`); six rows: `str_ui_ATT_MOD`, `_CRIT`, `_DMG`, `_DEF`, `_PROT`, `_SPD` |
| `hero_stats_layout.icon_offset` | -26 2 | `shared/hero/icon_stat_buff.png` / `icon_stat_debuff.png` (24x24) left of a row whose stat is buffed or debuffed |
| `hero_equipment.pos`; `hero_equipment_layout.weapon_pos`, `armour_pos`, `icon_offset` | 238 0; 4 0; 95 0; 29 52 | weapon card at 271,52 and armour card at 362,52 (72x144; the art's frames are at x 267..345 and 358..436) |
| `hero_equipment_layout.level_offset` | 90 12 | the level beside the sword and the armour drawn into the art: 332,12 and 423,12 (`equipment_level`), in the colour of the level: `equipment_level_0` and `_1` 215 213 205, `_2` 105 190 75, `_3` 62 114 212, `_4` harmful |
| `hero_trinket.pos`; `hero_trinket_grid_layout.start_pos`, `offset`; `inventory_item_layout.icon_offset` | 453 0; 32 52; 92 160; 4 0 | trinket cards at 489,52 and 581,52 |

## 3. Right panel: map and inventory

`panels/panel_map.png` and `panels/panel_inventory.png` (720x360) at 960,720: two whole pictures, each drawn
with its own tab lit; DD1 swaps them.

| entry | value | meaning |
|---|---|---|
| `map_layout.clip` | 16 665 19 340 | the map's window: x 16..665, y 19..340 of the panel |
| `map_layout.tilesize`, `scale` | 24, 1.00 | a hallway tile is 24 px, a room icon (`panels/icons_map/room_*.png`) 64 px |
| `map_layout.manual_to_follow_delay` | 10.0 | seconds after a drag until the window follows the party again |
| `map_layout.zoom_key_value`; `input.min_zoom_scale`, `max_zoom_scale` | 0.25; 0.35, 1.5 | a wheel step and the limits of the zoom |
| `indicator_layout` | bounce 5, up/down 0.5 s, scale 0.95..1.05 | the party's mark (`indicator.png`) |
| `tab_placement.pos`, `size` | 672 252, 48x90 | the lower tab (inventory); the map's tab is the 90 px above it. The art lights the map tab at y 163..253 and the inventory tab at y 240..330 |
| `home_button_layout.button_pos` | 677 24 | `panels/focuscam_button.png` (38x46): centre the map on the party (`str_centre_map_tooltip`) |
| `reorder_party_layout.button_pos` (panel.tab.darkest) | 678 90 | `panels/party_order_button.png`. Not drawn (3) |
| `raid_inventory_panel_grid_layout` (pannel.inventory.darkest) | 8 columns, start 20 28, offset 80 160 | the cells of the 16-slot bag |
| `inventory_item_layout.icon_size`, `icon_offset`, `amount_text_offset` | 72 144, 4 0, 14 4 | the card in its cell, the stack's size (`inventory_amount`) |
| `inventory_item_tooltip_layout.offset`, `text_width` | 85 0, 200 | an item's tooltip hangs beside its card |

**How DD1 spaces the map.** `scripts/starting_save/persist.map.json` (the tutorial's map) has a room at
`mappos` x -10.5, the hallway's tiles at -9.5 (a door), -8.5..-5.5 (the four tiles walked), -4.5 (a door) and
the next room at -3.5: two rooms a hallway of n tiles apart stand n + 3 tiles apart, and the door tiles lie
under the 64 px room icons, so four tiles show between two rooms. `Minimap` puts a tile i of n at
(i + 2) / (n + 3) of the way; `hall_door.png` is not drawn.

## 4. In the scene

`screen.raid.status_bars.darkest`, `status_bars` (under each hero)

| entry | value | meaning |
|---|---|---|
| `y_pos` | 698 | top of the health bar |
| `char_x_offset`, `health_bar_offset`, `health_bar_widths` (first), `health_bar_height` | -50, 50 0, 100, 10 | a 100x10 bar centred on the hero; colours `tray_health_bar_default_current_top/bottom` over `tray_health_bar_background_top/bottom` |
| `stress_offset`, `stress_spacing` | -1 12, 10 | ten pips from 51 px left of the hero, 12 px under the bar's top: `overlays/stress_pip_empty.png` (8x12), `stress_pip_full.png` (9x10) |
| `overlays/selected_1.png` (175x206) | | the selected hero's bracket; its bar is on rows 161..171 of the picture |
| `tray_icon_hot_spot_size` | 20 24 | a status icon's place over the bar; the pictures (`overlays/tray_*.png`, 24x24) have 20 px of picture in their middle and are centred on it |
| `tray_icon_left_offset`, `tray_icon_left_spacing` | 58 -38, 20 | one row of icons: its first place ends 58 px right of the tray's origin (8 px right of the hero), the next ones follow to the left |
| `tray_icon_right_offset`, `tray_icon_right_spacing` | 62 -38, 20 | the other row: its first place starts at 62, the next ones follow to the right. Both rows stand 38..14 px above the bar's top |
| `icon_world_y_offset` | 149 | "the hero's middle": the height above the ground the pop texts are counted from (9) |
| `status_bar_tray_icon_pulse` (screen.raid.darkest) | out 0.25, in 0.125, scale 1.25 | an icon that appears swells to 1.25 of its size and settles |

**The status icons** (`RaidTrayIcons.cs`). What lasts on a hero outside a fight in the Estate:

| icon | row | when | tooltip |
|---|---|---|---|
| `tray_deathsdoor.png` | left, 1st | DD2's death's door status (a hero at 0 health) | `tray_icon_tooltip_deathsdoor_title` over `tray_icon_tooltip_deathsdoor`, in `tray_icon_tooltip_negative` |
| `tray_deathsdoor_effects.png` | left | DD2's wound (what DD1 calls mortality debuffs) | `buff_bsrc_deathsdoor_recovery`, then `buff_stat_tooltip_combat_stat_multiply_max_hp` with the share of health that is gone |
| `tray_disease.png` | left | a disease quirk | the diseases' names (DD2's), in `tray_icon_tooltip_negative` |
| `tray_buff_plus.png` | right, 1st | a camping buff in the expedition's ledger | a line a buff: `buff_stat_tooltip_<stat>[_<sub>]` inside `buff_rule_tooltip_<rule>`, then `tray_icon_tooltip_buff_duration_combat_end_format` "%s (%d Battles)" with the fights left (`tray_icon_tooltip_buff_until_camp_format` for a buff without a count); `tray_icon_tooltip_positive`, or `_negative` for an amount that works against the hero |
| `tray_town_event.png` | right | the week's town event gives the party something on this expedition | the event's title, then its buffs the same way with `tray_icon_tooltip_buff_duration_quest_end_format` "%s (Quest)" |

`announcement`, `announcement_times` (screen.raid.darkest) and the two scripts (`RaidAnnouncement.cs`)

| entry | value | meaning |
|---|---|---|
| `overlays/announcement_frame.png` (619x136) | | the banner: a dark band (rows 22..112) between two rails, fading out at both ends |
| `frame_pos_left`, `frame_pos_right` | 572 184, 1348 184 | the banner's middle over DD1's heroes, over DD1's monsters |
| `frame_pos_centre_top`, `frame_pos_centre_bottom` | 960 210, 960 668 | its middle under the torch, across the heroes' feet |
| `text_offset` | 0 -29 | the words (`banner_header`: DwarvenAxe large), centred, the top of their line cell: the capitals (rows 11..45 of the cell) then stand in the middle of the band |
| `announcement_times` | trap 1.0, disarm 0.5, curio 0.5, curio_purge 1.2, new_quirk 1.5, surprised 1.0, deaths_door 1.2, round 0.5 | seconds a banner of the kind is up |
| `scripts/timescript/announcement_show.times` | `announcement_scale` 0.8 to 1.0 in 0.7 s, easeOutElastic; `announcement_text_alpha` 0 to 1 in 0.25 s | how it comes |
| `scripts/timescript/announcement_hide.times` | `announcement_text_alpha` to 0 in 0.25 s | how it goes |

| banner | words | colour | time | place | asked for by |
|---|---|---|---|---|---|
| trap | `str_its_a_trap` "TRAP!" | `trap_announcement` | trap | centre bottom | `DungeonRun.SpringTrap` |
| disarm | `str_ui_disarm` "Disarmed!" | `disarm_announcement` | disarm | centre bottom | `DungeonRun.Process` (a trap disarmed) |
| ambush | `str_ambush_title` "Ambush!" | harmful | surprised | centre top | `DungeonRun.StartAmbush`; the fight waits for it |
| surprised | `surprise_announcement` "Surprised!" | `surprised` | surprised | left (the party) or right (its enemies) | `DungeonRun.StartAmbush`, `StartFight`; the fight waits for it |
| new quirk | `str_new_quirk_colon` "New Quirk:" and the quirk's name | neutral, then `quirk_positive` or `quirk_negative` | new_quirk | centre bottom | DD2's `EventQuirkAdded` (`RaidFeedback`) |
| quirk removed | `curio_announcement_purge_format` "%s Quirk Removed!" | notable | curio_purge | centre bottom | DD2's `EventQuirkRemoved`, but for a disease |
| death's door | `str_ui_deathdoor` "%s is at Death's Door!" | `deathdoor` | deaths_door | centre top | DD2's `EventActorStatusChanged` |
| (dev bridge) | `str_curio_no_negative_quirks_to_remove`, or any words | neutral | curio | centre bottom | `raid.announce` |

`scripts/layout/base.popup_text.layout.darkest` (`RaidPopText.cs`)

| entry | value | meaning |
|---|---|---|
| `fonts/popup.fnt` (style `pop_text`) | DwarvenAxe 68, outline 4 | the glyph in the page's colour channels, glyph and outline in its alpha: two font assets, two labels, the colours `pop_text_<kind>` and `pop_text_outline_<kind>` |
| `pop_text_layout_<kind>.start_offset`, `pop_y_offset`, `pop_time` | damage and hero_heal 0 20, 100, 1.5; stress_damage 0 120, -80, 1.0; stress_reduce 0 40, 80, 1.0; full, cured, buff, debuff 0 80, 100, 0.8; death_avoided, deathblow 0 80, 100, 1.5 | where a text starts, how far it travels and for how long |
| `pop_text_shared.type_limit_delay_time` | 0.3 | two texts of one kind over one hero start this far apart |
| `pop_text_shared.icon_offset` | 5 0 | the gap between a kind's picture (`overlays/poptext_<kind>.png`, 64x64: buff, debuff, death_avoided of the kinds used) and its word |

| pop text | words | set off by |
|---|---|---|
| damage, hero_heal | the number, no sign | DD2's `EventActorHealthDamage`, `EventActorHealthHeal` (a trap, hunger, a curio, a meal, a camping skill, an item) |
| stress_damage, stress_reduce | the number, in DD2's points | DD2's `EventStressDamage`, `EventStressHeal` |
| full | `str_full` "Full!" | `DungeonRun.UseItem` (a hero who will eat no more) |
| cured | `str_ui_disease_cured` "%s Cured!"; `str_ui_cured` "Cured!" | DD2's `EventQuirkRemoved` for a disease; `Camp` (a wound mended) |
| buff, debuff | `str_ui_buff` "Buff!", `str_ui_debuff` "Debuff!" | `Camp` (a skill's buffs landing on a hero, once a hero) |
| death_avoided, deathblow | `str_ui_death_avoided` "Death's Door!", `str_ui_deathblow` "DEATHBLOW!" | DD2's `EventActorSurviveDeathsDoor`, `EventActorDeath` |

`torch_layout` and `torch_info`

| entry | value | meaning |
|---|---|---|
| `pos_y` | 28 | top of `overlays/torch.png` (900x188), centred on x 960 |
| `gauge_offset`, `gauge_size` | 26 89, 400 4 | the left gauge is x 26..426, y 89..93 of the picture, the right one its mirror image (474..874); both grow from the middle |
| `fade_amount` | 0.05 | the gauge ends softly over 5% of its full length |
| `flamepos` | 960 100 | the flame on the torch's head (`overlays/torch_flame.png`; DD1: a particle effect) |
| `mouseOverAreaSize`, `stripMouseOverAreaOffset/Size` | 200 130; 0 70, 860 24 | the tooltip's two hot areas, centred on x 960 |
| colours `torch_centre`, `torch_ends` | #ff7a00, #5f0400 | the gauge from its inner to its outer end |
| `str_darkness_title_0..4` | RADIANT LIGHT .. BLACK AS PITCH | the light level's name, brightest first |

`quest_info` (from `pos` 12,20)

| entry | value | meaning |
|---|---|---|
| `info_glow_offset` | -14 0 | `overlays/quest_log_glow.png` (374x158) behind the texts |
| `info_button_offset` | 65 58 | middle of `overlays/quest_log.png` (80x80) |
| `info_text_name_offset` | 110 26 | the quest's name (`raid_quest_info_name`) |
| `info_text_goals_start_offset`, `spacing` | 110 58, 0 30 | the goal lines (`raid_quest_info_goals`) |
| `retreat_button_offset` | 68 120 | middle of `panels/retreat_button.png` (64x64) |
| `complete_button_offset`, `complete_text_offset` | 0 30, 120 45 | once complete: `overlays/quest_complete.png` (120x152) and "Quest Complete!" (`raid_quest_complete`, style `raid_quest_info_complete`) |
| `complete_mid_screen_pos` | 948 590 | the choice in mid screen: the seal centred here |
| `complete_choice_shared_frame_pos` | 0 55 | `panels/quest_complete_choice_shared_frame.png` (619x136), hanging from the middle of its top edge |
| `complete_return_to_hamlet_pos`, `complete_continue_raid_pos` | -170 98, 90 98 | corners of `panels/quest_return_to_hamlet.png` and `quest_continue_raid.png` (124x69) |
| `complete_tooltip_offset`, `complete_tooltip_width` | 0 10, 200 | each button's tooltip under it (`raid_results_progression_return_to_town`, `str_continue_raid_tooltip`) |
| `complete_blur_time` | 0.2 | DD1 blurs the screen behind the choice |

## 5. The event scrolls

A scroll hangs from the middle of its top edge. Buttons are 124x69 pictures placed by their corner; their
visible part is the middle 56..64 px. Headings are `scroll_header`, text `scroll_body` (neither has a colour
in base.colours.darkest: `notable` and `neutral` are used).

| entry | value | meaning |
|---|---|---|
| `sidebar_scroll.pos` | 1348 200 | `scrolls/event_scroll_sidebar.png` (456x400): curios and obstacles |
| `sidebar_scroll.header_y` | 44 | heading, centred |
| `sidebar_scroll.body`, `body_width` | -152 128 ("128d" in the file), 330 | text, from the scroll's middle |
| `sidebar_scroll.investigate_button_pos` | -152 240 | `scrolls/byhand.png`; words in `curio_investigate_text` / `obstacle_investigate_text` for a controller only (`investigate_controller_text_offset`) |
| `sidebar_scroll.item_slot_pos`, `item_slot_size` | -34 230, 80 160 | the slot for an item: `scrolls/use_inventory.png`, `use_inventory_active.png` |
| `sidebar_scroll.pass_button_pos` | 75 240 | `scrolls/pass.png`; `curio_pass_text` for a controller only (`pass_controller_text_offset`) |
| `sidebar_scroll.investigate_tooltip_text_width`, `pass_tooltip_text_width`, `item_slot_tooltip_text_width` | 160, 160, 160 | what the pointer is told on the three: `str_curio_tooltip_investigate_<id>`, `curio_tooltip_pass`, `curio_tooltip_item_slot` |
| `basic_scroll.pos` | 1342 140 | `scrolls/event_scroll_basic.png` (456x518): hunger |
| `basic_scroll.header_y`, `inset_offset`, `body_y`, `body_width` | 48, 0 145, 308, 345 | heading; `scrolls/inset_hunger.png` (347x147) centred, top at 145; text centred |
| `basic_scroll.ok_button_pos`, `cancel_button_pos` | -150 400, 78 400 | `scrolls/eat.png`, `scrolls/starve.png` (`event_hunger_eat_text`, `event_hunger_starve_text`) |
| `basic_scroll.button_y`, `button_size` | 456, 384 36 | a line-of-text button |
| `loot_title.pos`, `loot_description.pos`, `.width` (overlay.loot.darkest) | 228 40, 228 136, 350 | `scrolls/event_scroll_loot.png` (456x475): heading and text, centred |
| `loot_tiles.startPosY`, `offset` | 195, 74 0 | the cards, centred, 74 px apart |
| `loot_background.min_items`, `max_items`, `dynamic_backdrop_y_offset` | 4, 9, 10 | above four cards a wider strip is laid under them: `event_scroll_loot_left_edge.png`, `_mid_section.png` (72x163) per card, `_right_edge.png`, 10 px above the cards |
| `loot_buttons.take_all_pos`, `close_pos` | 80 358, 306 358 | Take All and Close (`loot_take_all_text`, `loot_close_text`; `str_overlay_loot_take_all`, `str_overlay_loot_close`) |
| `tooltip_background_layout.border_texture_threshold`, `text_offset` | 0.15, 4 0 | `shared/tooltip/tooltip_background.png` (128x128) cut in nine, 19 px borders |
| `confirm_dialog_layout.base_pos`, `answers_offset` | 960 200, -12 -20 | `shared/confirm_dialog/confirm_dialog.background.png` (840x600) hanging from the middle of its top edge |
| `confirm_dialog_question_layout.text_offset`, `text_width` | -18 200, 520 | the question, centred |
| `confirm_dialog_answers_layout.start_offset`, `spacing`; `confirm_dialog_answer_layout.button_size`, `selected_overlay_offset` | 0 390, 0 55; 500 38, 0 -24 | the answers; `confirm_dialog.answer_text_selected_overlay.png` under the pointer |

DD1's own words used on the scrolls (English section of `localization/miscellaneous.string_table.xml` and
`curios.string_table.xml`): `str_curio_title_<id>`, `str_curio_content_<id>`,
`str_curio_tooltip_investigate_<id>` (the id is the prop's "UI String Name" of `curios/curio_props.csv`),
`curio_tooltip_pass`, `curio_tooltip_item_slot`, `curio_tooltip_item_slot_gather_curio`;
`str_obstacle_<id>_title` / `_description`, `obstacle_tooltip_clear_by_hand`; `str_ui_hunger_title`,
`str_ui_hunger_content`, `str_ui_hunger_choice_eat`, `str_ui_hunger_choice_starve`, `str_ui_eat`, `str_ui_starve`,
`str_meal_not_enough_provisions`; `str_overlay_loot_chest_title` / `_description`; `not_enough_room`,
`not_enough_room_confirm`, `str_discard_item_instructions`, `cant_discard_quest_item_confirm`;
`retreat_confirm_raid_question` (and `retreat_raid_party_kill_darkestdungeon_confirm_question`),
`retreat_confirm_raid_answer_yes/no`.

## 6. Camp and provision

`screen.raid.darkest`, `meal_scroll` and `camp_layout`

| entry | value | meaning |
|---|---|---|
| `meal_scroll.headerY`, `buttonY`, `buttonOffset` | 52, 148, 74 | `scrolls/meal_scroll.png` (456x333): "Repast" (`str_ui_meal_title`), the four rations |
| `meal_scroll.tooltipOffset`, `tooltipWidth`, `tooltipTitleIdFormat` | 170 -10, 180, `str_meal_title_%d` | a ration's tooltip |
| `camp_layout.respite_scroll_pos` | 732 60 | `scrolls/event_scroll_campingrespite.png` (456x237), top centre of the screen |
| `respite_title_offset`, `respite_points_offset` | 60 44, 324 44 | "Respite" and the points, which end before the hourglass drawn at x 333..373 (`camping_points`) |
| `respite_description_offset`, `_width` | 56 130, 360 | `camping_respite_description` |
| `respite_rest_offset`, `respite_rest_text_offset` | 100 200, 128 17 | `overlays/announcement_rest.png` (257x68) with "REST" (`rest`) |

`campaign/town/provision/provision.layout.darkest`, with `campaign/town/town.layout.darkest`

| entry | value | meaning |
|---|---|---|
| `town_background_layout.area_pos`, `character_pos` | 144 132, 132 240 | `provision.character_background.png` (1395x776), `provision.character.png` |
| `provision_layout.name_pos` | 104 126 | `provision.icon.png` (113x113); the name (`town_name_provision`, style `town_name`) beside it |
| `provision_store_background_layout.pos`; `provision_store_grid_layout` | 814 144; 7 columns, start 120 20, offset 80 170 | the shelves over `inventory_grid_background_store.png` |
| `provision_party_background_layout.pos`; `provision_party_grid_layout` | 800 532; 8 columns, start 60 28, offset 80 160 | the bag over `inventory_grid_background_party.png` |
| `inventory_item_layout.cost_offset` | 37 157 | middle of a price: half way down the 26 px the 170 px row pitch leaves under a card (`town_currency_amount`; `town_currency_cant_afford_amount` when the purse falls short) |
| `provision_layout.provision_sell_back_info_pos` | 1164 510 | middle of "[CLICK] inventory to sell back" (`provision_sell_back_info`), in the gap between the grids |
| `provision_layout.quest_info_pos`, `quest_specs_offset` | 1300 96, 20 -5 | the dungeon's name (`provision_quest_info_dungeon_name`) and the quest's particulars (`provision_quest_info_specs`) |
| `provision_layout.scouting_stat_pos` | 1380 96 | the party's scouting chance. Not drawn (7) |
| `town_screen_layout.embark_party_pos`; `embark_party_layout` | 754 871; background -1 0, slots from 22 16 every 93 | the four who go |
| `town_screen_layout.estate_summary_pos` + `estate_summary_layout.pos_offset`, `currency_pos` | 0 975 + 0 -17, 200 42 | the bar and the gold on it (as in dd1-ui.md) |
| `progression_layout.back_pos`, `forward_pos`, `forward_text_offset` | 228 82, 801 984, 160 -2 | the red arrows back; "Embark" (`town_progression_forward_embark`) |
| `town_provision_not_enough_food_confirm_format` | | DD1's question when the party takes less food than it advises |

## 7. Where the mod differs from DD1, and why

| what | the mod | why |
|---|---|---|
| hero figures | DD2's models, walking a corridor whose camera follows them | the mod's premise. The status bars therefore follow the models on screen (`RaidTrays.Follow`: the model's position through the main camera) instead of standing at `overlays.hero_start_pos`; those are the fallback while no model is found |
| stress pips | one pip per point | DD2's stress runs 0..10 (DD1: ten pips for 0..100, a second layer to 200) |
| skills in the banner | the hero's five equipped DD2 skills in the five numbered places, in their own colours; "move" in the narrow place beside them | DD1 has four skills there, dark outside a fight, "move" under 5 and "pass" beside it; a fight is DD2's own screen, so no skill is used from the banner, and there is nothing to pass (the owner, 2026-10-06) |
| stats | four rows (the owner, 2026-10-06): DMG, CRIT, HP, SPD. DMG is what a fight multiplies every blow of the hero by, as a percentage over 1 (`health_damage_dealt_percent` added up, times `health_damage_dealt_mult_percent`; what holds for melee or ranged blows only is not in it); CRIT what the hero adds to a skill's own chance; HP the share the maximum health is multiplied by; SPD the speed. DD1's names where it has them (`str_ui_DMG`, `_CRIT`, `_SPD`; "HP" is the mod's word, DD1's is "MAX HP"). DD1's gold marks a row that quirks, diseases or trinkets have moved, its arrow one a camping buff has | DD1 has six rows (ACC, CRIT, DMG, DODGE, PROT, SPD) and a damage range. DD2 has no accuracy or dodge numbers (they are tokens) and gives every skill a damage range of its own. What the blacksmith's levels give (`Core/GearRules`: +10% damage and +1% crit a weapon level, +10% maximum health an armour level) is added to the rest as DD2 adds it and shows in DMG, CRIT and HP; it is gear, not a buff, and brings neither gold nor arrow |
| picture buttons | rest as their art is; under the pointer the art times `button_highlight` (1.5 1.5 1.3), drawn with a material of the UI's shader whose tint is that factor (`RaidHighlight`) | a UI picture's own colour cannot pass white |
| weapon and armour | DD1's own pictures of the class's gear (`heroes/<class>/icons_equip/eqp_weapon_N.png`) at the blacksmith's level; tooltip in the mod's words | the mod's blacksmith is DD1's |
| trinkets | DD2's trinket icons, 72x72 in the middle of DD1's 72x144 slot | DD2's trinkets are square |
| affliction / virtue seal, party order button | not drawn | DD2 has no lasting affliction; the mod has no reordering of ranks |
| portrait | DD2's portrait drawn 106 px, cut off at the 85 px hole | DD2's portraits are heads with wide clear margins (dd1-ui.md, 3) |
| hero's path | a third line under the class, in the class's style | DD1 has no paths; the estate tells heroes of one class apart by them |
| torch tooltip | DD1's name of the level, the light, the scouting bonus, "in a fight the flame burns at n" | of DD1's list for a light level the mod plays the scouting chance; fights read the light as DD2's flame. The tooltip hangs under the torch (`torch_info.tooltip_offset` is counted from a point the files do not name) |
| torch flame | one picture, tinted and flickering | DD1 uses a particle effect (`overlays/torch_flame.plist`) |
| quest name | may run to 384 px, shrinking from there | the mod's quest names are longer than DD1's; `quest_info.size` is 300 wide |
| log lines | the last three lines of the expedition's log under the quest info, fading after 9 s | DD1 has no such element. What it tells in banners and in pop text over the heroes the mod now tells there too; a curio's own sentence and the Ancestor's voice it does not have here yet |
| pop text numbers | stress in DD2's points (a trap's fright is "1") | DD2's stress runs 0..10; the trays show it so |
| banners of a fight ("Ambush!", "Surprised!") | shown over the dungeon before the fight is loaded; the fight waits until they have been up for their time (`DungeonRun.BeginBattle`). After a camp they stand over the dark | a fight is DD2's own screen, in a scene of its own; DD1 announces these in the fight |
| banner during a trap or a curio | drawn over the trays it crosses | DD1's HUD is faded out then (`layers.HUD_alpha`), the mod's is not |
| status icons | five of DD1's, for what lasts outside a fight; the town event's bell stands in the tray's row | the others are a fight's states; where DD1 hangs the bell is not settled (9) |
| a status icon's tooltip | where the bars' tooltip stands: over the tray, centred on the hero | DD1's files give it no place |
| a camping buff's tooltip | DD1's words for DD1's buff ("+10 ACC (4 Battles)") | what the buff does to a DD2 hero is the mod's mapping (`CampingBuffMap`: accuracy is damage dealt) |
| the HUD's line | a tooltip-styled line over the inventory panel: what to pick, why a click did nothing | DD1's "user information" pop-up appears at the pointer |
| a trap | asked about in the sidebar scroll (the hand: disarm; the way past: walk through) | DD1 asks nothing: a spotted trap is disarmed by walking onto it. The question is the expedition's (`DungeonRun`) |
| an obstacle | no way past on the scroll | the expedition's question has none: the party turns back by walking |
| item on a curio | the slot shows DD1's art and, when one kind of item fits, that item; a click on a fitting item in the inventory uses it; the others are greyed. The slot's tooltip is DD1's (`curio_tooltip_item_slot`, which speaks of dragging) | DD1 has the player drag or click the item; the mod's question lists the items that fit |
| words on the sidebar scroll | none under the hand and the way past of a curio or an obstacle: the pointer is told (DD1's tooltips, the answer's own words where DD1 has none). A trap's pictures keep the answers' words under them | DD1 places those words for a controller only; the trap's window is the mod's own |
| item tooltips | DD1's description (`str_inventory_description_*`), a gem's "[Value: n Gold Each]" in estate gold, DD1's discard line; between them one line of the mod's for bandage, antivenom, herbs, holy water and laudanum ("On a hero: +10% health.") | DD1's sentences for those speak of bleeding, blight and debuffs, which a DD2 hero does not carry out of a fight; in the Estate they heal or calm |
| plain questions (a full bag) | the basic scroll, the text in the picture's frame, the answers as lines of text (`basic_scroll.button_size`) on `scrolls/choice_button_frame.png` | DD1 has no such question. The scroll is headed "Not enough room!" (`not_enough_room`) for the full bag |
| hunger | DD1's text (`str_ui_hunger_content`) and DD1's tooltips with the numbers (`str_ui_hunger_choice_eat`, `_starve`); "Eat" and "Starve" stay under the bowls | DD1 places those two words for a controller only; whether it shows them with a mouse was not settled |
| loot scroll | hangs where the basic scroll does; opens only when `DungeonRun` calls `DungeonHud.Loot` (snippet in the hand-back) | overlay.loot.darkest places the parts inside the scroll, not the scroll. Until the call is wired in, loot goes straight into the bag as before |
| quest-complete choice | a 45% shade behind it; a click on the shade goes on | DD1 blurs the screen (`complete_blur_time`) |
| retreat | DD1's confirm dialog first; the Darkest Dungeon's own question when leaving costs a hero | the old button left at once; the flag is small |
| using an item | DD1's way: on the selected hero. A shift-click throws a stack away; quest items cannot be thrown away | the old HUD picked the item up and gave it by a click on a hero's plate |
| camp button | gone: a click on the firewood makes camp | DD1 has none |
| keys | T lights a torch, TAB swaps map and inventory, SPACE takes all of the loot scroll, C shows the selected hero's sheet and puts it away, W is a click on what the party stands at (a lit curio, a hidden door in its tile, a secret room's way out, else the door of the hallway's end it stands at) | DD1's keys (`[pc]menu_controls_element_2_*`, `[pc]str_help_raid_hallway_2`, `_4`). H (help) has no window to open |
| camp scrolls | hang at 1420,40 instead of top centre (`respite_scroll_pos` 732 60); a ration's tooltip hangs left of the scroll | DD2's rest stop seats the party left of centre and leaves the right free |
| camp: a box under the scroll | what to do next, what was refused, the last three lines of the camp's log | DD1 shows the effects as pop-ups over the heroes |
| camp: meal names | under each ration, on the card | DD1 names them in the tooltip only |
| camping skills | the selected hero's skills in the banner's places, DD1's `selected_ability.png` around one waiting for a companion, `target_h_1.png` under the companions it may go to. The tooltip is DD1's: `camping_skill_cost` ("Time Cost: n"), `camping_skill_uses_remaining_format`, `camping_skill_selection_*`, `camping_skill_effect_*`, a buff in `buff_stat_tooltip_*` (the rank rule in `buff_rule_tooltip_in_rank`), with the Estate's numbers | DD1 shows camping skills in the banner too. A line can differ from DD1's in its number (stress on DD2's scale, a buff under a rule the Estate cannot ask given at half) and in its stat (accuracy as damage, dodge and protection as damage not taken) |
| provision: gold | the bar shows what the purse will hold after the bill; "of n, after m for provisions" beside it | DD1 takes the gold as things are bought; the mod pays on setting out |
| provision: Standard Kit | on the bar, right of Embark | the mod's own convenience |
| provision: roster column, heirlooms on the bar, scouting stat | not drawn | nobody joins or leaves here; the screen covers the hamlet's own bar |
| provision: party tray | a hero's tooltip says what they bring of their own | DD1 puts a class's supplies in the bag without a word |
| displays wider than 16:9 | the DD1 screen is centred; the seam and a black floor run the whole width | |
| displays narrower than 16:9 | the screen's sides are cut off; the quest info and the log move in | the canvas keeps DD1's height |

## 8. The mod's own numbers

| constant | value | what |
|---|---|---|
| `RaidHeroPanel.NameLeft` | 122 | the name's box starts where the portrait's hole ends |
| `RaidHeroPanel.PathGap` | 21 | the path line under the class |
| `RaidTrays.BracketGap` | 3 | the selection bracket's bar above the health bar |
| `RaidTrays.Zone` | 110x300 | the click area over a hero |
| `RaidTrays.MinStep` | 112 | two trays never overlap |
| `RaidTrays.TooltipRise`, `TooltipWidth` | 44, 260 | a tray's tooltip (the bars', a status icon's): its foot this far above the health bar, clear of the icons |
| `RaidAnnouncement.Smallest`, `Waiting` | 30, 4 | words wider than the banner's picture shrink no further; banners that may wait their turn |
| `RaidPopText.FadeShare`, `EdgeMargin`, `Most` | 0.25, 8, 24 | a pop text fades over the last quarter of its time, keeps inside the screen's sides, and no more than so many are up |
| `DungeonHud.LogPos`, `LogSize` | 24 196, 470x84 | the log lines |
| `DungeonHud.NoticeRise` | 16 8 | the HUD's line over the inventory panel |
| `RaidScrolls.CaptionRise`, `CaptionWidth` | 9, 116 | a button's words: 9 px up from the picture's foot, no wider than the gap to the item slot |
| `RaidScrolls.PlainBody` | y 162 | a plain question's text |
| `RaidQuestInfo.NameWidth` | 384 | the quest name's box |
| `RaidTorch.FlameSize` | 76 | the flame |
| `CampScreen.ScrollPos`, `NoteGap` | 1420 40, 44 | the camp's scrolls and the box under them |
| `ProvisionScreen.KitPos`, `BillPos`, `MessagePos` | 1190 990, 410 1002, 1160 900 | Standard Kit, the bill, messages |

## 9. Readings that stayed guesses

- **Name and class in the banner are right-aligned at 272.** Left-aligned or centred there they would lie on
  the skill icons, which start at 280; the art's light patch between portrait and skills is x 119..267.
- **Health and stress numbers are centred on x 130** (the bands are x 30..220). They could start there.
- **`info_button_offset` and `retreat_button_offset` are middles**, `complete_button_offset` a corner: as
  corners the first two would lie on the texts; as a middle the third would leave the screen.
- **The selection bracket's height.** `overlays.hero_start_pos` y 680 is not the bracket's corner, middle or
  foot in any way that puts its bar near the status bars; it is placed with its bar 3 px above the health bar.
- **The quest-complete frame hangs from its top edge** (so that the two buttons lie in its band) and the seal
  is centred on `complete_mid_screen_pos`.
- **Scroll buttons by their corner.** The two buttons of every scroll then stand 34 px right of symmetrical;
  no other reading of the three scrolls' numbers is symmetrical either.
- **Loot buttons use `byhand.png` and `pass.png`**: `scrolls/` has no other button art, and the styles
  `loot_take_all_text` / `loot_close_text` give them words.
- **`provision_layout.quest_info_pos` and `quest_specs_offset` are the feet of two line cells**, the name
  ending at the position: only then do the two texts share a baseline (DwarvenAxe medium's descent is 10 px,
  Ubuntu small's 4, the offset's y is -5), and only right-aligned does the name stay clear of
  `scouting_stat_pos` 80 px further.
- **The torch's light levels** are told apart by the lower edge of the band in `shared/rules.json` (75, 50,
  25, 0, below), which is how DD1's five titles are numbered.
- **Where DD1 hangs the loot scroll** and **where it writes a camping skill's cost** are not in a file.
- **The banner is centred on its `frame_pos_*` and `text_offset` is the top of the words' line cell.** Settled
  by the art: only then do DwarvenAxe large's capitals stand in the middle of the frame's band.
- **Which of the four places a banner takes.** Not in a file. Read: the low one for what DD1 shows closed in
  on a hero with its HUD faded out (trap, disarming, curio, quirk), the high one for death's door and the
  ambush, a side for a surprise (DD1's heroes stand left, its monsters right). `raid.announce` takes a
  `pos` to try the others.
- **A banner's time counts from the moment it starts to show**, and the hide script follows it. "Disarmed!"
  (0.5 s) is then up for less than the show script's 0.7 s of settling; counted from the end of that script
  every banner would stay 0.7 s longer.
- **`announcement_text_alpha` fades the frame as well as the words.** The scripts have no other alpha; a
  frame that did not fade would be gone from one frame to the next.
- **The colours of the banners but "TRAP!" and "Disarmed!"**, which have their own ids. Taken: the colours DD1
  gives the same words in its running text (`deathdoor`, `surprised`, `quirk_positive` / `quirk_negative`),
  harmful for "Ambush!", neutral for "New Quirk:".
- **"Ambush!" has no entry in `announcement_times`**: it is up as long as "Surprised!".
- **Where a pop text's offsets are counted from, and which way.** The layout gives `start_offset` and
  `pop_y_offset` and no origin. Read: from the hero's middle (`status_bars.icon_world_y_offset` 149 above the
  ground, DD1's one number for a point on an actor) with y upwards, to the middle of the text. A damage number
  then rises from the chest past the head and a stress number sinks from the head to the chest. Counted
  downwards from a hero's head instead, the same numbers would put a stress number at the hero's knees.
  `raid.pop` takes an `anchor` to try other heights.
- **How a pop text travels and ends.** The layout has a distance and a time: the travel is even and the text
  fades over the last quarter of its time. DD1's own curve and fade are in its code.
- **A kind's picture stands left of its word**, `pop_text_shared.icon_offset` apart.
- **The two rows of status icons.** `tray_icon_left_offset` 58 and `tray_icon_right_offset` 62 are read as the
  end of a row that grows to the left and the start of one that grows to the right (a single icon of the left
  row then stands in the tray's middle, which is where the selection bracket leaves it room). Which icon
  belongs to which row is not in a file: what harms is put on the left, what helps on the right.
- **A camping buff wears `tray_buff_plus.png`** (the gold arrow), not `tray_buff.png`: read as the mark of a
  buff that outlasts a fight. `tray_disease.png` is in the install, but it is not among the tray pictures
  DD1's executable names outright (the others used here are): DD1 itself may not show a disease on the tray.
- **`status_bars.icon_offset` 50 30 and its `icon_tooltip_*`** (a default width of 150, 200 for
  `resolve_xp_bonus` and `town_event`) are an icon of their own that the files do not explain; 30 px under
  the bar's top is the panel's seam on this screen. The town event's bell is put in the tray's right row
  instead, and none of these entries is read but `icon_world_y_offset`.

## 10. Verified, and not

Checked outside the game: the plugin builds; `tools/preview_raid_hud.py` reads every entry of this page from
the install without falling back and draws the screens from them with DD1's fonts; the previews were looked
at against the art (the portrait in the banner's hole, the skills over the numbers, the levels beside the
sword and the armour, the cards in the panel's cells and in the provision grids, the prices in the gap under
the shelves, the gauge on the torch's line, the scrolls' headings in their bands, the inset in its frame).

Not seen in game: everything in `Dungeon/Raid*.cs`, `DungeonHud.cs`, `Minimap.cs`, `InventoryGrid.cs`,
`CampScreen.cs`, `ProvisionScreen.cs` as rebuilt. The hand-back lists what to look at first.

The banner, the pop text and the status icons (`RaidAnnouncement.cs`, `RaidPopText.cs`, `RaidTrayIcons.cs`,
`RaidFeedback.cs`) were written without the game: the plugin builds, and `tools/preview_raid_hud.py` draws
them from the same files with the same readings (`raid_feedback.png`, `raid_feedback_icons.png`: the words
in the frame's band, the popup font in its two colours, the icons' rows over the bars). Not seen in game:
all of it, and in particular the popup font's two TextMeshPro assets, whether DD2's events arrive as
`RaidFeedback` expects, the height of the pop texts over DD2's models, and the second or two of banner
before an ambush's fight. Bridge commands to look at each without playing: `raid.announce`, `raid.pop`,
`raid.tray`, `raid.effect`, `raid.feedback` (`RaidDev.cs`).
