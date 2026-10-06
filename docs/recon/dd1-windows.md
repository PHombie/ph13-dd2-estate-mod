# DD1 UI: the hamlet's windows, element by element

Input to the final acceptance pass (MODLOG "Acceptance bar"). Per window: what DD1 puts where and which file says
so, what the mod draws for it, where the mod differs and why, and my own estimate (0-100) of how close the window
now is to DD1 with what keeps it below 100. Companion to `dd1-ui.md` (fonts, text styles, the hamlet's chrome).

All numbers are DD1's stock values in pixels of its 1920x1080 screen, y down; the code reads them from the
player's install and keeps the stock value as a marked fallback. "art px" = pixels of a building's backdrop
(`<id>.character_background.png`, 1395x776), which DD1 puts at `town_background_layout.area_pos` 144 132.
Layout files are under `campaign/town/` unless a path says otherwise; `B/` = `campaign/town/buildings/`.

Offline previews (all in `_lab/preview/`, 1920x1080): `python tools/preview_windows.py` draws every window
(`window_<name>.png`, `upgrades_<building>.png`); the older generators call into it and keep their file names
(`tavern_panel.png`, `sanitarium_panel.png`, `wagon_window.png`, `guild_panel.png`, `survivalist.png`, ...).

**None of this has been seen in the game.** Section 15 lists what only the game can confirm.

Two real DD1 screens ship with DD1 itself, as its tutorial pictures (`shared/tutorial_popup/`:
`tutorial_popup.stage_coach2.png` = the upgrade pane with a tooltip, `...stress_relief.png` = a tavern slot with a
hero, `...locking_pos_quirks.png` = the sanitarium's quirk choice, `...quest_select.png` = a region of the map).
They are small and skewed, but they settled several readings below; where a reading rests on one, it says so.

## 0. Decisions that hold for every window

| Question | Decision |
|---|---|
| Own hero list or the hamlet's roster? | **The hamlet's roster column, always** (DD1). No window draws a hero list of its own any more. While a window that takes heroes is open (Tavern, Abbey, Sanitarium, Guild, Blacksmith, Survivalist) a **click** on a roster row hands that hero to the window instead of toggling the party, and a row can be **dragged** onto a slot (DD1's way). Right click still opens the character sheet, the dismiss control still works. In the other windows (Stage Coach, Nomad Wagon, Graveyard, Memoirs, town event) the roster behaves as on the hamlet. |
| Where do hints, results and "what the last click did" go? | On **one line inside the window**, at DD1's `B/building.layout.darkest` `building_base_body_layout.info_text_offset` 580 760 (art px 1032, 730, centred), in DD1's `town_building_info` style (Ubuntu, #5d5a50) at rest and in neutral / harmful colour for a message. It wraps to two lines and shrinks; it is never cut. Nothing is drawn below the window any more. |
| The estate's bar | Stays lit and in reach, as in DD1: a window ends where the bar begins (`town_screen_layout.estate_summary_pos` 0 975 + `estate_summary_layout.pos_offset` 0 -17 = y 958), the keeper's feet go under the bar. Windows therefore show **no purse of their own**: gold and heirlooms are on the bar. |
| Ellipsis | No label in these windows uses it. One line shrinks to its box (`Dd1Ui.Line`, `Dd1Ui.NoCut`, `UpgradeUi.Label`), a paragraph wraps and shrinks (`Dd1Ui.Block`). |
| Tooltips | DD1's box (`shared/tooltip/tooltip_background.png`, cut in nine by `tooltip.layout.darkest` `border_texture_threshold` 0.15) with the `tooltip` style; heading in `upgrade_tree_tooltip_title`'s colour (notable). `Dd1Tooltip` in `UI/UiKitWindows.cs`; the Estate Map's tooltip is the same class. |
| Text | Every label names a DD1 text style and is drawn 1:1 in that style's font and colour (`Dd1Ui.Line/Block`, `UiKit.Text(..., "style")`). |
| Right click | Steps back (a hero not yet paid for, a picked slot, a memoir's page), then leaves the building (DD1). |

Code: `Estate/RosterWindow.cs` (frame, info line, roster hand-over, drag: `HeroDrag`, DD1 strings of the heroes'
table: `WindowText`), `UI/UiKitWindows.cs` (`Dd1Ui`, `Dd1Tooltip`), `Estate/UpgradeUi.cs` (slot, prices, steps,
`HeroActionFrame`).

## 1. The frame every window shares (`RosterWindow.cs`)

| DD1 element | Source (file, block, key) | Mod element |
|---|---|---|
| Backdrop 1395x776 | `town.layout.darkest` `town_background_layout.area_pos` 144 132; `B/<id>/<id>.character_background.png` | Same art at the same place (was: centred in the free room, 77 px further left, 8 px lower) |
| Keeper | `town_background_layout.character_pos` 132 240; `<id>.character.png` | Same; cut off at the bar's top edge as DD1's bar covers it |
| Screen name | `B/building.layout.darkest` `building_base_layout.name_pos` 104 126; style `town_name`; string `town_name_<id>` | Centred between the two rules drawn in every backdrop (art px 105..407 x 30..122: centre 256, 76). `name_pos` is the corner of a widget whose inner offset is not in a file; the centre is measured from the art |
| Close button | `building_base_layout.close_pos` 1496 144; `shared/progression/progression_close.png` | Same art, same place (was a text "X" in a box) |
| "+" for upgrades | `shared/progression/info_icon_frame.png`, `more_info_icon.png`, `less_info_icon.png`; DD1's help: "[CLICK] the + icon to see upgrade options" | Same art (was a text button "Upgrades" / "Back"). **Place is the mod's**: DD1's files have none. Before the name (art px 40, 46); while the pane is open, on the corner of the building's icon (94, 97), and the icon itself (under the pointer after the click) closes the pane too |
| Text of the screen | `building_base_body_layout.info_text_offset` 580 760; style `town_building_info` | The window's one line of text (section 0) |
| Roster, estate bar | `town_screen_layout.roster_list_pos`, `estate_summary_pos` | The hamlet's own, untouched and lit |
| Leaving | Close button, right click | Same, and a click on the darkened town |

Deviations: the town behind the window is darkened by a 72% black wash (DD1's own treatment is in code, not data);
DD1's zoom-in (`building_animation` transition_time 0.3, base_scale 0.4) is not played; DD1's quick navigation
strip (`building_navigation`) is not built.

**Closeness: 90.** Below 100: the "+" button's place is a guess, no transition, no quick navigation.

## 2. Upgrade pane (`UpgradePane.cs`)

| DD1 element | Source | Mod element |
|---|---|---|
| Pane art | `building_base_layout.upgrade_base_pos` 172 259 + `building_base_upgrade_layout.frame_offset` -18 -115; `B/blgupgradebg.png` | Same |
| Building icon in the notch | `<id>.icon.png`; notch measured from the pane art (9, 10) | Same |
| What upgrading does | `verbose_offset` 20 30, `verbose_width` 380; style `town_building_upgrade_verbose`; string `building_verbose_<id>` (in `localization/heroes.string_table.xml`) | Same text and style (was the mod's own hint and a purse row) |
| "Upgraded:" | `upgrade_title_offset` 458 36; `town_building_upgrade_title`; `building_upgrade_title` | Same |
| Share built | `upgrade_percent_offset` 480 62; `town_building_upgrade_percent` | Same |
| A tree: title | `upgrade_trees_offset` 0 195, `upgrade_trees_spacing` 0 160; `building_base_upgrade_tree_layout.title_offset` 20 -35; `town_upgrade_tree_title`; `upgrade_tree_name_<tree>` | Same |
| Tree icon + its tooltip | `icon_offset` 30 0; `icon_tooltip_offset` 30 106; `upgrade_tree_tooltip_description_<tree>` | Same (tooltip is new) |
| Steps | `requirement_spacing` 70 0; `B/upgrade/upgrade.layout.darkest` `upgrade_requirement_layout` (`icon_offset` 40 10, `background_offset` 30 0, `background_connector_offset` 0 24); the three step icons, backing, link, highlight | Same |
| Price of a step | `cost_offset` 62 72: icon, amount, icon, amount in a row (tutorial picture); `town_currency_amount`; red = `town_currency_cant_afford_amount` | Same row, under **the next step only** (was: every step, stacked). A row of two heirlooms is wider than the 70 px between steps, so DD1 cannot show them under every step either; the later steps name their price in their tooltip |
| "Free" (a week's event) | `free_offset` 62 72; `town_free` | Same |
| Step tooltip | `building_upgrade_requirement_tooltip_layout.tooltip_offset` 130 0, `tooltip_text_width` 300; DD1's sentences `upgrade_tree_tooltip_description_*_format`; "Prerequisites:" `upgrade_prerequisite_tooltip_title` | DD1's box at DD1's offset, DD1's sentence where it has one for the step (recruits, roster size, trinkets, slots, discounts, experienced recruits, cure-all chance, "Increases stress recovery" + the numbers); the mod's words for the rest; last line = what stands between the estate and the step |
| Rule under a row | `divider_offset` 0 118; `tree_divider_large.png` | Same |

Deviations (the mod must differ): **Hero Paths**, the mod's own fourth tree at the Stage Coach, shares the third
row with Experienced Recruits (DD1's pane has three rows; two short trees side by side, the second 300 px in). It
uses DD1's title style, DD1's step art, and the Nomad Wagon's signpost icon. The count "1 / 5" beside a tree's
title is gone (DD1 has none; the steps show it).

**Closeness: 90.** Below 100: Hero Paths in a shared row; prices under the next step only is an inference; no
purchase flourish or sound.

## 3. Stage Coach (`StageCoachPanel.cs`, name fix in `StageCoach.cs`)

| DD1 element | Source (`B/stage_coach/stage_coach.layout.darkest`) | Mod element |
|---|---|---|
| A recruit's place | body (`building_base_layout.body_base_pos` 596 102) + `hero_recruit_store.store_item_pos` 450 80, every `store_item_spacing` 0 100 | Same (was a list of the mod's own from 746, 132) |
| Plate | `hero_layout.hero_background_offset` -100 -8; `stage_coach.hero_background.png` | Same |
| Resolve badge and number | `hero_resolve_level_number_background_offset` -63 12, `..._text_offset` -30 43; `shared/resolve_level_bar/..._lvl<n>.png`; `resolve_number` | Same: 0 for a green recruit, the rank of an experienced one (was a text "Veteran II") |
| Portrait in a hero slot | the item's place; `hero_slot/hero_slot.background.png` | Same slot, DD2's portrait cut off inside the frame |
| Name | `hero_name_offset` 100 12 | Same place, style `town_roster_name` |
| Class | `hero_description_offset` 100 42 | "Class, Path", style `town_character_class` |
| Hiring | drag the portrait to the roster; tooltip `str_hero_slot_unlocked_stagecoach_tt` | Drag to the roster column, **or click the row** (was a "Hire" button). DD1's tooltip, plus ", or click." |
| Empty coach | `stagecoach_layout.empty_info_text_offset` 575 375, width 600; `stagecoach_empty_info`; `town_building_info` | Same |

Removed: "Barracks 7 / 12" and "5 recruits wait. Hiring costs nothing." (DD1 has neither; the roster's header
counts, a full barracks says so in the row's tooltip and on the info line when a blocked recruit is clicked).

**Bounty Hunter's name**: the game's `hero_name_canonical_<class>` lookup draws another name each time for some
classes. `StageCoach.Recruit.Shown` is filled once, when the coach is filled (`Pin`), saved with the offer
(`"shown"`), shown by the panel and passed to `RosterLifecycle.CreateHero` at hiring: the hero carries the name
the coach showed. A class without a name of its own is still hired without one.

Deviations: the path beside the class; DD2 portraits; click as well as drag; DD1's scroll bar is replaced by
closer rows when a week's event brings more than seven recruits; DD1's "show character" preview and the rename
dialog are not built (right click on a hired hero opens DD2's sheet).

**Closeness: 88.**

## 4. Tavern and Abbey (`BuildingPanel.cs`)

| DD1 element | Source (`B/building.layout.darkest`) | Mod element |
|---|---|---|
| A row per activity | body + `building_activity_list_layout.base_pos` 70 50, every `activity_spacing` 0 230 | Same |
| Activity name | `building_activity_layout.name_offset` 170 36 (between the gold rules of the backdrop); `town_activity_name`; `town_activity_name_<id>` | Same |
| Description | `description_offset` 170 90, `description_width` 250; `town_activity_description`; `town_activity_description_<id>` ("Drink your cares away.") | DD1's line (new), then "Stress -2 to -3" in gold (the mod's: DD1 leaves the numbers to be found out) |
| Slots | `slot_list_pos` 440 119, `slot_spacing` 135 0; `hero_slot.background.png` | Same; DD2's portrait cut off inside the frame |
| Price over a slot | `building_activity_slot_layout.cost_offset` 38 -20 (read as the middle of the price); coin + amount, `town_currency_amount` | Same (was: also a price beside the name, and a "1 of 3 slots" line) |
| Hero's name over a slot | `hero_slot/hero_slot.layout.darkest` `town_hero_slot_layout.name_offset` 45 -70; `hero_slot_name` (tutorial picture) | Same (was just above the slot, small) |
| Slot not built yet | `locked_overlay_offset` -42 -70; `<id>.locked_hero_slot_overlay.png` | Same |
| Week's event on a slot | `free_overlay_offset` -42 -70; `<id>.<activity>.free_event.png` / `.locked_event.png`; "Free" at `free_offset` 42 0, `town_free` | Same (new) |
| Hero in a paid slot | `overlay_offset` -26 -70; `<id>.<activity>.hero_slot_overlay.png` | Same |
| Putting a hero in | drag from the roster (`str_empty_hero_slot_<activity>` "Drag a hero here.") | Drag from the roster, or click the slot and then the hero's roster row |
| Confirm | `confirm_button_offset` 42 115; `hero_activity/hero_activity.confirm_button.png`; tooltip `str_hero_slot_unlocked_<activity>` at `confirm_button_tooltip_offset` 0 38, width 200 | Same: **nothing is paid until the check mark is clicked** (was: paid at once) |
| Cancel | `cancel_button_offset` 7 112; `hero_activity.cancel_button.png`; `str_hero_slot_locked_<activity>` | Same art and place |
| Last week's results | DD1: its Activity Log, another screen | On the window's line of text ("Last week: ..."), which shrinks to hold them (was a strip under the window, on the bar) |

Removed: the window's own hero column, the purse, the activity icons left of the names (DD1 has none there; they
are the upgrade trees' icons), "n of m slots".

Deviations: cancelling a paid stay gives the price back until the week ends (the mod's rule; DD1 asks and keeps
the money); DD1's caretaker never takes a slot; no Activity Log screen.

**Closeness: 90.**

## 5. Sanitarium (`SanitariumPanel.cs`)

As section 4 for the two wards (`B/sanitarium/sanitarium.layout.darkest` `sanitarium_activity_layout`: name 170
42, description 170 90, slots 440 119 / 135 0), plus:

| DD1 element | Source | Mod element |
|---|---|---|
| Ward description | `town_activity_description_treatment`, `..._disease_treatment` | DD1's line (new), then the mod's price lines (coin, amount, "Remove a bad quirk" ...): DD1 names a price only once a quirk is chosen |
| Choice: header bar | `quirk_treatment.header_backdrop_centre_position` 480 470; `quirkheader.png` / `diseaseheader.png` | Same |
| Header text | `title_centre_position` 500 475; `str_quirks` / `str_diseases`; colour `sanitarium_treatment_header` (tutorial picture: "Quirks") | Same (was "Sahar: choose a quirk") |
| Backdrop with the mask | `backdrop_centre_position` 500 500; `quirk_treatment_backdrop.png` / `disease_treatment_backdrop.png` | Same |
| Good quirks, left | `positive_list_position` -230 400, `choice_spacing` 0 28; colour `sanitarium_treatment_positive_entry`; highlight `posquirk_highlight.png` at -50 0; padlock `lockedquirk.png` at -37 -2 | Same; style `town_choice_activity` |
| Bad quirks, right | `negative_list_position` 330 400 (right aligned); colour `sanitarium_treatment_negative_entry` (red); `negquirk_highlight.png` at -170 0; `remove_quirk_negative.png` at 5 -2 | Same |
| Diseases | `disease_list_position` -90 410; colour `sanitarium_treatment_disease_entry`; `disease_highlight.png` | Same |
| "Lock <quirk>" / "Remove <quirk>" | `sanitarium_activity_slot_layout.choice_remove_quirk_right_text_pos` -115 156, `..._left_text_pos` 130 156; `str_sanitarium_lock_quirk_format`, `..._remove_quirk_format` | Same strings at those places beside the mask (was "Remove: X" left of the button, and price captions over the lists) |
| Quirk tooltip | `choice_tooltip_left_justified_offset` 350 0, `..._right_justified_offset` -330 0, width 200 | DD1's tooltip box there, DD2's quirk text in it |
| Price and check mark | `sanitarium_activity_layout.shared_cost_pos` 62 570 (read as the middle of the price: so it stands clear above the button, as the tutorial picture shows), `shared_confirm_pos` 62 588; tooltip `str_hero_slot_unlocked_treatment` at `shared_confirm_tooltip_offset` 0 46, width 350 | Same |
| Treatment of a hero in a cell | - | The mod's line under the cell ("Remove: Known Cheat") |

`quirk_treatment` positions: the file does not say what they count from; header and backdrop count from the start
of the list of wards, lists / price / button from the first cell. Read so, the button falls into the frame drawn
in the backdrop and the result matches DD1's tutorial picture.

**Closeness: 90.** Below 100: DD2 quirk names and texts; the mod's price lines; refund on cancel.

## 6. Guild, Blacksmith, Survivalist: the hero banner (`UpgradeUi.HeroActionFrame`)

| DD1 element | Source (`B/hero_action/hero_action.layout.darkest`) | Mod element |
|---|---|---|
| Banner box | drawn in the three backdrops (art px 676..1342 x 28..126) | The backdrop's own |
| Hero slot | `hero_action_layout.base_pos` 220 44 (+ body 452 -30 = 672, 14), `body_pos` 240 121, `hero_action_banner_layout.hero_slot_offset` -230 -100 (counted from the body: 682, 35, the box's left end) | Same slot; the hero comes from the roster by drag or click (was: a row of every hero's portrait in the box) |
| "Drag a hero here." | `help_offset` 100 46; `town_action_banner_help` (#333333); `str_empty_hero_slot_action` | Same; the slot starts empty the first time, then keeps the hero last worked on |
| Hero's name | `name_offset` 105 45; `town_action_banner_name` | Same |
| Text column: frame | `verbose_pos` 0 105, `hero_action_verbose_layout.frame_offset` -20 0; `verbose_frame.png` | Same (new) |
| Text column: heading, text | `title_text_offset` 5 32 (`town_action_verbose_title_text`), `body_text_offset` 5 74, `body_text_width` 260 (`town_action_verbose_body_text`); `action_verbose_body_<building>_<class>` (heroes' string table) | DD1's text about the class in this building (new). Heading = "Class, Path" |
| Body | `body_pos` 240 121 -> art px 912, 135 | What each building's own layout counts from |

The mod's own: the **path** in the text column's heading; one line above the name with the hero's standing in the
building ("Resolve level 1   Mastered 1 of 1"), style `town_character_class`; what keeps the next mastery shut,
under DD1's text. DD2 classes DD1 never had (Duelist, Runaway, ...) have no DD1 text: the column shows the mod's
lines only.

### 6a. Guild (`GuildPanel.cs`) — **closeness 80**

| DD1 element | Source (`B/guild/guild.layout.darkest`) | Mod element |
|---|---|---|
| Skill rows | `guild_layout.skill_pos` 44 -4, `skill_spacing` 0 91 | Same rows; **two columns** ending at the frame's edge (a DD2 hero has 11 skills, DD1's 7) |
| Skill picture in its frame | `guild/skill_frame.png` (88, 8 px around the 72 px picture); padlock `shared/character/lockedskill.png` | Same frame and padlock, DD2's skill icon |
| Ranks | `guild_upgrade_tree_layout.requirement_spacing` 75 0; DD1: five ranks | **Two steps**: known, mastered (DD2's skills) |
| Price | `icon_cost_visible` 1, `icon_cost_offset` 34 70 | Same place, the price of the skill's next step |
| Rule | `divider_offset` 0 82; `tree_divider_medium.png` | Same, as wide as a cell |
| Tooltips | `icon_tooltip_offset` 90 0; `requirement_tooltip_tree_icon_below_offset` 0 40 | DD1's box: name, state, what a click costs or why not |

Deviations: two columns and the text column narrowed from 260 to about 196 px to make room; the skill's name
written small under its steps (DD1 names a skill only in its tooltip; eleven unlabelled DD2 icons are hard to
tell apart); DD2 has no skill description the Guild can show.

### 6b. Blacksmith (`BlacksmithPanel.cs`) — **closeness 84**

| DD1 element | Source (`B/blacksmith/blacksmith.layout.darkest`) | Mod element |
|---|---|---|
| The two boxes | `blacksmith_layout.frame_pos` 30 0; `blacksmith.frame.png` | Same place (was 264 px further left) |
| Weapon, armour | `equipment_pos` 50 20, `equipment_spacing` 0 176 | Same; `blacksmith.weapon.icon.png` / `.armour.icon.png` stand for the piece (DD1 shows the hero's own gear art, which DD2 heroes do not have) |
| Levels | `blacksmith_upgrade_tree_layout.requirement_spacing` 75 0 | Same |
| Price | `upgrade_requirement_layout.cost_offset` 62 72 | Under the step that can be bought next |
| Tooltips | `icon_tooltip_offset` 90 15; `blacksmith_upgrade_requirement_tooltip_layout.tooltip_offset` 110 0, width 220 | DD1's box: what the level gives, what is still asked |

The mod's own: two lines under each row (what the piece gives now and at its next level).

### 6c. Survivalist (`SurvivalistPanel.cs`) — **closeness 86**

| DD1 element | Source (`B/camping_trainer/camping_trainer.layout.darkest`) | Mod element |
|---|---|---|
| Class skills | `camping_trainer_class_specific_skill_grid_layout.skill_start_pos` 60 30, `skill_spacing` 110 90 | Same (was 278 px further left, 55 lower) |
| Shared skills | `camping_trainer_shared_skill_grid_layout.skill_start_pos` 60 200 | Same |
| Unknown skill | padlock `shared/character/lockedskill.png`; price at `camping_trainer_upgrade_tree_layout.icon_cost_offset` 32 90 | Same |
| Skill chosen for the road | `shared/character/selected_ability.png` | Same art (was a gold box and the words "ready" / "known") |
| Tooltip | `icon_tooltip_offset` 90 0; `camping_trainer_upgrade_requirement_tooltip_layout.tooltip_text_width` 280 | DD1's box: respite cost, what the skill does, what a click does (was a paragraph under the grids) |

The mod's own: captions "Crusader" / "Shared" over the two grids (`town_upgrade_tree_title`); the rules of a camp
on the window's line of text (two lines, never cut).

## 7. Nomad Wagon (`NomadWagonPanel.cs`) — **closeness 82**

| DD1 element | Source (`B/nomad_wagon/nomad_wagon.layout.darkest`) | Mod element |
|---|---|---|
| Table | `inventory_system_background_layout.pos` 230 150; `inventory_grid_background.png` | Same |
| Wares | `inventory_system_grid_layout`: 6 columns, `start_pos` 55 -10, `offset` 100 180 | Same; DD1's rarity card with DD2's trinket icon on it |
| Price under a card | coin + amount; red when the estate cannot pay | Same, in `town_currency_amount` |
| Tooltip | DD1's tooltip box | Same box: DD1's rarity name (`trinket_rarity_<r>`), DD2's item text, what a click does |
| Selling | DD1: from the Trinket Inventory (`realm_inventory`), "Sell trinket for:", question `realm_inventory_sell_trinket_confirm_question_format`, "Yes" / "No" | The mod's row of the estate's unworn trinkets under the table, headed by DD1's `town_name_realm_inventory`; DD1's words in the tooltip and the question; DD1's scroll arrows (`shared/widgets/scrollbar_leftarrow.png`, `..._rightarrow.png`) page it |

Removed: the header line that ended in an ellipsis ("2 trinkets on the table. Click one to buy it; new wares
co...") and the purse. What the wagon has to say stands on the window's line of text.

Deviations: no Trinket Inventory screen (DD2's character sheet is where trinkets are worn), so the stores are a
row, laid over the still life painted into the backdrop behind a 55% shade; DD2 icons on DD1 cards.

## 8. Graveyard (`GraveyardPanel.cs`) — **closeness 80**

| DD1 element | Source (`B/graveyard/graveyard.layout.darkest`) | Mod element |
|---|---|---|
| List | `graveyard_main_layout.list_position` 148 148, `list_area_size` 720 600, `entry_size` 1000 160, `entry_spacing` 20 | Same |
| Scroll bar | `scrollbar_offset` 20; `shared/widgets/scrollbarmid.png`, `scrollpip.png` | Same art (new) |
| Tombstone | `0_1.png` .. `6.png` by the resolve level at death | The plainest stone for everybody: the grave records keep no level (hook `GraveyardPanel.LevelOf`, see the hand-back) |
| Slab | `dead_hero_backdrop.png` (600x118) beside the stone | Same |
| Hero's face, name | `dead_hero_layout.portrait_position` 0 0, `name_position` 96 0, `background_margins` 10 25 10 20; `town_graveyard_hero_name` | DD2's portrait without colour; name in DD1's style |
| Week | `town_graveyard_hero_death_week`; `str_graveyard_week_string` | Same, right aligned on the slab |
| How they died | `death_by_position` 96 128; `town_graveyard_story`; `str_death_attack_monster` "was slain by a vile", `str_death_unknown_unknown` | Same strings with the killer's name |

The mod's own: class and place of death on the slab. The count stands on the window's line of text. How the parts
of an entry lie together (stone left of the slab, text beside the portrait, the story under the slab) is read
from the numbers; DD1's own picture of it was not to hand.

## 9. Ancestor's Memoirs (`MemoirsPanel.cs`) — **closeness 84**

| DD1 element | Source (`B/statue/statue.layout.darkest`) | Mod element |
|---|---|---|
| The Ancestor's line | `statue_main_layout.quote_position` 590 80, `quote_width` 505; `town_statue_quote`; `str_statue_ancestor_quote` | Same style (was italic at size 24) |
| Shelves | `list_position` 270 170, `list_area_size` 600 580, `entry_spacing` 10 | Same, the full height (the count moved to the window's line of text) |
| Scroll bar | `scrollbar_offset` 20 -50; `shared/widgets/` | Same art (new) |
| Category bar | `*_title_bar.png`; `town_statue_media_heading` | Same style |
| Entry | `media_entry_*_backdrop.png`, `portrait_*.png`, `playmedia.png` / `lockedmedia.png`; `town_statue_archive_entry`; locked: `town_statue_archive_locked_entry` | Same style and dimming (was gold DwarvenAxe) |

Deviations: DD1 plays a memoir; the mod turns the shelves over to a page of its words (`town_statue_quote`, a
size down), with DD1's back arrows (`shared/progression/progression_back.png`) and right click to return; a
second line per entry (which boss, which region, or what unlocks it).

## 10. Town event notice (`TownEventPanel.cs`) — **closeness 92**

| DD1 element | Source (`town_event/town_event.layout.darkest`) | Mod element |
|---|---|---|
| Place | `town.layout.darkest` `town_screen_layout.town_event_pos` 144 132 | Same (was centred); the bar stays lit, the crier's feet go under it |
| Heading | `town_event_layout.name_pos` -40 -6; `town_name`; `town_name_town_Event` | Between the backdrop's rules, DD1's style |
| Close | `close_pos` 1352 12; `progression_close.png` | Same art and place (was a text "X") |
| Crier, tone frame, title, picture | `character_pos` -5 100; `town_event_story_layout`: `tone_frame_offset` 1030 115, `title_offset` 1030 150, `image_offset` 1030 216 | Same; title in `town_event_story_title` |
| Story | `description_offset` 1030 480, width 485; `town_event_story_description` (Ubuntu, 21 14 5) | Same style (was italic DwarvenAxe) |
| What it does | `result_pos` 770 610 + `town_event_info_layout.element_start_pos` 250 36, width 485; `town_event_info` | Same style, centred on the band |

Below 100: DD1's confirm button for events that ask for payment is not built (no such event is in); the small
notice on the hamlet is the mod's own.

## 11. Building name plates on the hamlet (`HamletScene.cs`) — **closeness 90**

| DD1 element | Source (`town.layout.darkest`) | Mod element |
|---|---|---|
| Ink blot | `building_text_layout.background_offset` -70 -70; `B/blg_name_background.png` | Same |
| Name | `<id>_layout.text_offset` from the middle of the hover box; `name_offset` 0 0; `town_screen_building_name`; `town_name_<id>` | Centred on that point (was centred on the blot, 34 px to the right); "Ancestor's Memoirs" for the statue, as DD1 |
| What the building is for | `description_offset` 0 50; `town_screen_building_description`; `str_<id>_summary` ("Recruit New Heroes") | New |

Below 100: whether DD1 centres the two lines on the point or starts them there is not in a file (centred: the
offsets are 0 for most buildings, which only reads well centred); the alert icon (`alert_offset`) is not drawn.

## 12. Estate Map (`QuestPanel.cs`, `QuestMapParts.cs`, `QuestMapText.cs`) — **closeness 90**

Layout unchanged (it already followed `quest_select/quest_select.layout.darkest`, see the class text of
`QuestMapLayout`). This round: every label is set in its DD1 style at 1:1 instead of a size picked by eye
(`town_quest_select_dungeon_name`, `..._dungeon_level`, `town_quest_name`, `town_quest_description`,
`town_quest_camping`, `town_quest_specifics`, `town_quest_goals`, `town_quest_goal_start_description`,
`town_quest_rewards`, `inventory_amount`, `town_name`, `town_quest_select_party_name`,
`town_progression_forward`); no label ends in an ellipsis; the tooltip is DD1's box.

Below 100: the line over the party tray says who goes (DD1: the party's name); DD2 trinket icons on DD1 cards;
the level-cap notice in the panel's foot is the mod's wording; DD1's town event icon on a region is not drawn.

## 13. Where the mod must differ, and how it is seated

| Addition | Where | Seated as |
|---|---|---|
| DD2 path beside the class | Stage Coach (class line), hero banner's text column (heading), roster rows (RosterPanel, not this round) | In DD1's own text slot for the class, in that slot's DD1 style |
| Hero Paths upgrade tree | Stage Coach pane, third row, right half | DD1's tree parts (title style, icon, steps, price row, tooltip) |
| DD2 skill icons | Guild cells | Inside DD1's skill frame, under DD1's padlock while unknown |
| DD2 trinket icons | Wagon, map rewards | On DD1's rarity card |
| The estate's gold scale | every price | DD1's coin and `town_currency_amount`, DD1's red when too dear |
| Refund on cancel | Tavern, Abbey, Sanitarium | DD1's cancel button; its tooltip says the price comes back |

## 14. Files

`UI/UiKitWindows.cs` (new), `Estate/RosterWindow.cs`, `UpgradeUi.cs`, `UpgradePane.cs`, `UpgradeText.cs`,
`BuildingPanel.cs`, `SanitariumPanel.cs`, `StageCoachPanel.cs`, `StageCoach.cs`, `GuildPanel.cs`,
`BlacksmithPanel.cs`, `SurvivalistPanel.cs`, `NomadWagonPanel.cs`, `GraveyardPanel.cs`, `MemoirsPanel.cs`,
`TownEventPanel.cs`, `HamletScene.cs`, `QuestPanel.cs`, `QuestMapParts.cs`; `tools/preview_windows.py` (new) and
the generators that now call it.

Test bridge: every command still works. `RosterWindow.Pick(guid)` does what a click on a hero's roster row does
while a window that takes heroes is open (through the bridge's `invoke`); `RosterWindow.Current.Snapshot()` and
`BuildingPanel.Snapshot()` say what the window shows. `SanitariumPanel.Snapshot().said` is now the text on the
window's line (the hint when nothing was said).

## 15. Not verified without the game

1. Everything drawn by the new code: nothing here has run in the game. First things to look at: the frame at
   144, 132 with the bar lit under it; the "+" and the pane; the info line's wrapping and colour.
2. Roster hand-over: `RosterWindow.HookRoster` switches the rows' own click (`RosterRowClick.enabled`) off and
   puts a `HeroDrag` beside it while a window takes heroes; rows are found as children of `Roster/List`. To
   check: a click picks the hero and no longer toggles the party; the party click is back after the window
   closes and after another window opens straight from it; rows rebuilt mid-window (a hero hired, dismissed).
3. Dragging (roster row to slot, recruit to roster): uGUI drag events through DD2's input module, the ghost
   portrait following the pointer, the drop test. Click paths do the same job if dragging does not work.
4. DD1's tooltip box as a nine-sliced sprite (border 19 px of 128) and its measuring (`GetPreferredValues`).
5. One-line labels shrinking instead of being cut (`enableAutoSizing` with no wrapping) at DD1's native sizes;
   the hero's name in DwarvenAxe large inside the banner box.
6. DD2 portraits at 98 px cut off inside the 85 px slot in the banner, the activity slots and the coach.
7. Free / locked event art on activity slots, "Free" under an upgrade step (need the events).
8. The scroll bar (`Dd1Ui.ScrollList`: Unity's `Scrollbar` with DD1's rail and pip) in the Graveyard and the
   Memoirs, and that it hides when the list fits.
9. Hiring under the pinned name (Bounty Hunter), and an old save whose offer has no `shown` yet.
10. Screens that are not 16:9: the frame shrinks with the room left of the roster (`RosterWindow.Fit`).
11. `UpgradePane` inside the Tavern / Abbey / Sanitarium now that they are `RosterWindow`s (`upgrade.open`).

## 16. After DD1's frames (2026-10-05): what changed in the windows

Measured on real DD1 frames (`_lab/dd1_ref/town/`); where sections 1 to 15 say otherwise, this holds. The
shared findings (name widget, scroll bar, bar's row, tooltips) are in `dd1-ui.md` section 5.

- **Frame (1)**: the "+" stands on the icon's bottom middle (220,258), the open building's news on its icon;
  no Embark while a window is up (the button's grey picture, no word); no how-to sentence on the foot: the
  info line shows last week's results and messages only. Picture buttons rest as painted and are DD1's
  `button_highlight` under the pointer (`Dd1Ui.ArtButton`), with `/ui/town/button_mouse_over`.
- **Upgrade pane (2)**: no price under a tree whose next step waits for another tree (a lock only); the tree
  icon's tooltip keeps `building.layout.darkest`'s fixed width 300.
- **Tavern and Abbey (4), Sanitarium (5)**: a slot shows its price only over a hero who stands in it unpaid;
  taking a paid hero out asks in DD1's dialog (`TownConfirm`); while a hero is dragged from the roster
  (`HeroDrag.InHand`) free slots wear `hero_slot.positive_frame.png`, or `hero_slot.locked_for_hero.png` where the
  activity is not for that hero (GUESS: which frame shows when is read from the files' names).
- **Hero banner (6)**: the slot's brackets show only with the pointer on the slot (GUESS that the pointer
  brings them; DD1's Guild with a hero in shows none at rest). Prices in gold all come from
  `UpgradeUi.BuildGoldPrice`; the Blacksmith's piece tooltip keeps its layout's fixed width 220.
  Seen on DD1's Guild and not built: no marks over the skills in use (the mod keeps them: its Guild is where
  skills are picked), and four lock steps after every skill.
- **Graveyard (8), Memoirs (9)**: DD1's whole scroll bar. A death is recorded with DD1's two ids
  (`RosterLifecycle.Dd1Death`), so a grave reads "perished of hunger.", "was skewered by a trap." and the like.
- **Town event (10)**: DD1's bell on the bar brings the notice back (the plate at the hamlet's top is gone);
  the name is the widget with `town_event.icon.png`; the choice of the fallen asks in DD1's dialog.
- **Estate Map (12)** and the provision screen: the name widget at their `name_pos`; the estate's crest and
  band stay on screen under the red arrows; the fire's count reads "x0"; questions through `TownConfirm`.
  Provision: the button says "Embark" (DD1's own screen does, not "Set Off"); "Ruins" ends 20 px before the
  particulars, which end at `quest_info_pos` (997,91 and 1064..1300,96 on the frame); "Scouting (25.0%)" at
  `scouting_stat_pos` in the particulars' style; the bar's gold is the bar's own count (`EstateSummary.ShowGold`).
