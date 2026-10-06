# DD1's raid results: the two screens after an expedition

What DD1 shows when an expedition ends (Victory! / Escape / Defeat), where every part stands on its 1920x1080
screen, the art and the strings it uses, the rules behind the numbers, what the mod computes today and what is
missing. Written so that the screens can be built without opening DD1's files again. Research only: nothing
in the mod was changed.

Install read: `E:\Steam\steamapps\common\DarkestDungeon` (exe `_windows/win32/Darkest.exe`, 16 999 936
bytes). Paths below are relative to it.

How each fact was got, marked where it matters:
- **FILE**: a value in a data or layout file.
- **EXE**: read from the exe's own code (class `RaidResultsUI::Screen::RaidResultsDisplay`; the function
  addresses are in section 8). The layout files do not say which point of a picture a position names, which
  parts are drawn, or in what order; the code does.
- **ART**: measured from the pixels of a picture.
- **READ AS**: a reading that fits the art and the code but is not stated by either. Section 7 lists them.

Conventions: screen pixels of DD1's 1920x1080 layout, y down, art at native size, text 1:1 in DD1's styles
(docs/recon/dd1-ui.md: DwarvenAxe large = line 63, base 48, capitals 34; DwarvenAxe medium = 40 / 30 / 22;
Ubuntu medium = 28 / 23 / 17; Ubuntu small = 25 / 21 / 16). A text position is the corner of its line cell.
DD1's draw calls carry an anchor code (EXE): 0 = the position is the top-left corner, 1 = the middle of the top
edge, 2 = the top-right corner, 7 = the middle of the bottom edge (READ AS: the codes run row by row over a
3x3 grid; 0, 1 and 2 are certain from where the art puts things, 7 from the two title plates).

The owner's two screenshots are of DD1 in Russian; their words were matched against the Russian section of
the string table and are these ids: the title is `raid_results_quest_result_was_not_completed_escape`
("Escape"), the quest's name `town_quest_name_explore+1+<dungeon>+explore_all_rooms` ("Scout": a short explore
quest), then "Quest Rewards", "Collected Treasure", "Collected Heirlooms" and the button "Next"
(`raid_results_progression_heroes`). Page 2's button is "Return to Town".


## 1. Layout

### 1.1 Files

| file | what |
|---|---|
| `raid_results/raid_results.layout.darkest` | every position of the two pages (blocks `raid_results_screen_layout`, `raid_results_items_state_layout`, three `..._inventory_system_grid_layout`, `raid_results_heroes_state_layout`, `raid_results_hero_layout`; `wave_raid_results_*` is the Farmstead's endless mode, not used here) |
| `raid_results/raid_results.anim.darkest` | the timings (1.8) |
| `shared/progression/progression.layout.darkest` | the bar's button: `forward_pos` 801 984, `forward_text_offset` 160 -2, `forward_selected_overlay_offset` 0 -13 |
| `shared/resolve_level_bar/resolve_level_bar.layout.darkest`, `.anim.darkest` | the resolve badge and its bar |
| `shared/hero/hero.layout.darkest` | `hero_campaign_status_layout` (badge, stress pips, affliction), `hero_portrait_icon_layout` (disease mark) |
| `shared/estate/estate.layout.darkest` | how a currency total is drawn (`estate_large_currency_layout`, `estate_currency_heirloom_layout`) |
| `shared/inventory/inventory.layout.darkest` | an item card in a grid: `icon_size` 72 144, `icon_offset` 4 0, `amount_text_offset` 14 4; tooltip `offset` 85 0, `text_width` 200 |
| `shared/inventory/item.display.json` | `gold.icon_thresholds` 250 500 750 1000, `gold.can_unpack` false; `provision.icon_thresholds` 0 5 11 14, `provision.can_unpack` true |
| `fonts/fonts.darkest`, `colours/base.colours.darkest` | the styles (1.7) |
| `shared/app.darkest` `s_RaidResultsSubtitleContextTunables` | the Ancestor's subtitle on this screen: `screen_position` 960 990, `max_width` 1280, `background_enabled` 1 (same as the town's) |

There is no results layout under `raid/`, `campaign/`, `panels/`, `scrolls/` or `fe_flow/`.

### 1.2 What both pages share

Drawn in this order (EXE: `ShowBackgroundAndTitle`, then the page, then the bar):

| part | where | source |
|---|---|---|
| The region's picture, full screen | 0, 0 (anchor 0), 1920x1080 | `level_background_pos` 0 0. EXE: the picture is the dungeon's loading screen, `loading_screen/loading_screen.<dungeon>_<n>.png` (the loading-screen manager's weighted pick by dungeon id; the base game has only `_0` for crypts, weald, warrens, cove, darkestdungeon, town). Not the plot quest's own loading picture |
| The outcome panel, 899x1080 | hangs from 960, 0 (anchor 1): x 510.5 .. 1409.5 | `completion_background_pos` 960 0; which of four pictures: 4.2 |
| The result's title ("Victory!", "Escape", "Defeat") | its line cell stands on 960, 150 (anchor 7): cell y 87 .. 150, baseline 135, capitals 101 .. 135, centred on x 960 | `quest_result_pos` 960 150; style `raid_results_quest_result` (DwarvenAxe large); colour by outcome (1.7) |
| The quest's name | cell stands on 960, 212 (anchor 7): cell y 172 .. 212, capitals 180 .. 202, centred on x 960 | `quest_title_pos` 960 212; style `raid_results_quest_title` (DwarvenAxe medium, notable) |
| The page | origin `state_pos` 510 0: every page number below is counted from here | |
| The bar | 0, 958 (anchor 0): `shared/progression/progression_bar.png` 1920x138, its lit band at y 980 .. 1041 | `progression_bar_pos` 0 958 |
| The red button | `shared/progression/progression_forward.png` 312x52 at 801, 984; its words centred on x 961, cell top y 982 (`town_progression_forward`: DwarvenAxe large, harmful red); under the pointer `progression_forward_selected_overlay.png` 311x24 at 801, 971 | `progression_layout` (the same widget as the town's Embark). Words: page 1 `raid_results_progression_heroes` "Next", page 2 `raid_results_progression_return_to_town` "Return to Town" (EXE) |

Where the titles fall on the art (ART; panel art x + 510.5 = screen x):
- completed panel: a blue plate x 273 .. 602, y 78 .. 144 between gold rules at y 74 .. 76 and 148 .. 150 (the
  title's capitals 101 .. 135 sit in it), then a dark band y 165 .. 218 (the quest name's capitals 180 .. 202);
- escape panel: the red sun's disc reaches from the top to y 155, x about 290 .. 610; the title is black on
  the disc. Under it the black band y 158 .. 216 with a thin rule at y 218;
- defeat and regroup panels: the same band at y 158 .. 216.

The completed panel is drawn 15 px left of the other three: its body is x 21 .. 844 of the 899 px and its
plate's middle is x 437, while the others' body is x 27 .. 860 around 449. On screen the plate's middle is
x 948 and the title's x 960 (ART; DD1 does not correct it).

Displays of another shape: nothing in the files. In the mod: the 1920x1080 frame of `RaidUi` (centred,
standing on the canvas' foot), the region's picture and the bar stretched to the canvas' width.

### 1.3 Page 1: rewards, treasure, heirlooms (`raid_results_items_state_layout`)

Frame `raid_results/raid_results.items_frames.png` (533x723, opaque) hangs from `frame_offset` 450 230 (anchor
1): screen x 693.5 .. 1226.5, y 230 .. 953. What is painted into it (ART, frame px -> screen):

| painted | frame px | screen |
|---|---|---|
| ribbon banner, blue field | field x 143 .. 383, y 15 .. 50; tails to x 87 and 435, y 10 .. 66 | x 836 .. 1077, y 245 .. 280 |
| two thin rules around the rewards row | y 74 .. 75 and 227 .. 229, x 133 .. 392 | y 304 .. 305 and 457 .. 459 |
| brown header band | rules y 257 .. 259 and 297 .. 299 | band y 490 .. 526 |
| brown field | y 300 .. 478 | y 530 .. 708 |
| purple header band | rules y 479 .. 481 and 519 .. 521 | band y 712 .. 748 |
| purple field, fading to black | y 522 .. 722 | y 752 .. 952 |

| part | layout value | screen | drawn how (EXE) |
|---|---|---|---|
| "Quest Rewards" | `quest_inventory_title` 450 242 | middle of the top edge at 960, 242: cell y 242 .. 282 on the ribbon | anchor 1; `raid_results_quest_inventory_title` (DwarvenAxe medium, notable) |
| reward cards | `quest_inventory_grid_offset` 84 308 + grid `raid_results_quest_inventory_system_grid_layout`: 8 columns, `start_pos` 40 0, `offset` 80 160, centred | row y 308 .. 452; card k of n at x = 638 + 80 k + 40 (8 - n) (1.5). n = 3: cards at x 838, 918, 998 | the quest's reward list: gold, then heirlooms, then trinkets, one card each (the reward list has no stack limit: one gold card whatever the amount) |
| "Collected Treasure" | `party_gold_inventory_title` 200 490 | corner 710, 490: cell y 490 .. 530 in the brown band | anchor 0 (left aligned); `raid_results_party_gold_inventory_title` |
| the gold total | `party_gold_total_offset` 620 496 | the pile and the number as one group, centred on x 1130: group width W = 90 + the number's width; pile `shared/estate/currency.gold.large_icon.png` (88x88) at x 1130 - W/2, y 446 .. 534; number's cell corner at x 1130 - W/2 + 90, y 480 (capitals y 494 .. 528, in the band) | DD1's shared cost widget with `estate_large_currency_layout` (`icon_offset` 0 -50, `number_offset` 90 -16); number in `town_large_currency_amount` (DwarvenAxe large) coloured as `town_currency_amount` (notable); thousands separated ("1,555"). The group swells when a stack is counted (1.8) |
| treasure cards | `party_gold_inventory_grid_offset` 84 538 + grid: 6 columns, `start_pos` 100 0, `offset` 80 160, centred | one row, y 538 .. 682; up to 6 cards: card k of n at x = 698 + 80 k + 40 (6 - n); more than 6: squeezed (1.5) | every stack of the bag that is worth gold (4.4), in bag order; a food stack is laid out one card per ration |
| "Collected Heirlooms" | `party_heirloom_inventory_title` 200 714 | corner 710, 714: cell y 714 .. 754 on the purple band | anchor 0; `raid_results_party_heirloom_inventory_title` |
| the heirloom totals | `party_heirloom_total_offset` 620 720 | one entry per kind brought home, side by side, the group centred on x 1130. An entry: icon `shared/estate/currency.<kind>.icon.png` (40x40) at y 710 .. 750, the count's cell corner 38 px right of the icon's corner at y 716; the next entry starts 2 px after the count's end | the same widget with `estate_currency_heirloom_layout` (`icon_offset` 0 -10, `number_offset` 38 -4, `next_currency_spacing` 2 0); count in `town_currency_amount` (Ubuntu small, notable) |
| heirloom cards | `party_heirloom_inventory_grid_offset` 84 760 + grid: 6 columns, `start_pos` 100 0, `offset` 80 160, centred | one row, y 760 .. 904; x as the treasure row | the bag's heirloom stacks, in bag order |

A card is the item's 72x144 picture (`panels/icons_equip/<type>/inv_<type>+<id>[_n].png`) with the stack's size
at the card's corner + 10, 4 (`amount_text_offset` 14 4 less `icon_offset` 4 0), style `inventory_amount`
(DwarvenAxe medium, notable). No slot frame is drawn under a card. A card under the pointer shows the item's
usual tooltip 85 px right of its slot, 200 wide.

Centre lines differ by a few pixels, as in DD1: the ribbon's field is centred on x 956.5, its words on 960, the
rewards row on 954, the treasure and heirloom rows on 934 (their 480 px start at the frame's left edge + 4),
the totals on 1130. Four heirloom totals with two-figure counts are about 215 px wide and end some 10 px past
the frame's right edge (x 1226), over the panel's black.

### 1.4 Page 2: the heroes (`raid_results_heroes_state_layout`, `raid_results_hero_layout`)

Frame `raid_results/raid_results.heroes_frames.png` (680x675, four rows painted into it) hangs from
`frame_offset` 450 250 (anchor 1): screen x 620 .. 1300, y 250 .. 925. The frame is drawn whole whatever the
party's size. Hero i (0 .. 3) has its origin at `heroes_start_pos` 130 250 + i x `heroes_spacing` 0 168:
**screen 640, 250 + 168 i** (written T below for the row's top, 250 + 168 i). The art's rows start at y 0, 168,
337, 505 of the picture: up to 1 px off the 168 pitch.

One row of the art (ART, relative to the row's top; screen x = art x + 620):

| painted | art px | screen |
|---|---|---|
| two brushed rules of the name line | y 6 .. 16 and 46 .. 56, x 9 .. 547 | x 629 .. 1167 |
| portrait box (black, thin frame) | x 19 .. 115, y 57 .. 153; hole x 25 .. 109 | x 639 .. 735; the 85 px hole from x 645 |
| blood-stained plate in a frame (a bowl shape, translucent) | frame x 115 .. 551, y 50 .. 157 | x 735 .. 1171; middle 953, T + 103.5 |
| black banner for the resolve badge | x 544 .. 674, y 1 .. 157: inner frame x 556 .. 664, y 12 .. 99; rules at y 114 and 137 running on to the picture's right edge; a jagged foot y 146 .. 157 | x 1164 .. 1294, middle x 1229.5 |

| part | layout value (from the hero's origin) | screen | drawn how (EXE) |
|---|---|---|---|
| name | `name_offset` 0 16 | corner 640, T + 16: capitals T + 24 .. T + 46, standing on the lower rule | anchor 0; `raid_results_hero_name` (DwarvenAxe medium, notable) |
| portrait, 85x85 | `portrait_icon_offset` 5 64 | 645, T + 64 | DD1: `heroes/<class>/.../<class>_portrait_roster.png`. A diseased hero carries `shared/hero/portrait_icon_disease.png` (24x24) at + 61, 61 (`hero_portrait_icon_layout.disease_icon_offset`) |
| dead hero | `portrait_dead_overlay_offset` 5 64 | 645, T + 64 | `raid_results/deadhero_portrait.png` (85x85, a skull) over the portrait. The name and the badge are still drawn; no experience line, no masks, no quirks |
| "+N Resolve XP" | `resolve_from_quest_offset` 520 20 | **right** edge at x 1160, cell top T + 20 (on the name line, ending before the banner) | anchor 2; `raid_results_hero_resolve_from_quest` (Ubuntu small, #b5af9c); only when the hero gained experience. It slides in from 175 px further left while fading in (1.8) |
| resolve badge, bar, stress, affliction | `campaign_status_offset` 556 3 | widget origin C = 1196, T + 3 | DD1's shared "campaign status" widget, as below |
| ... its tooltip | `campaign_status_resolve_level_bar_tooltip_offset` 100 8 | hot area 100x100 at C; tooltip at 1296, T + 11, 200 wide | lines `resolve_bar_tooltip_level_line_format`, `..._xp_line_format`, `..._additional_stress_format`, `..._affliction_format` |
| the masks | `new_quirks_from_quest_start_offset` 145 18 + `center_of_quirks_upperleft_offset` 175 50 + anim `raid_results_heroes_masks.offset` -7 39 | effect's origin at **953, T + 107**: the plate's middle | Spine `fx/raid_results_quirk_reveal` (2.3); only for a living hero with at least one new quirk or disease |
| the new quirks | `center_of_quirks_upperleft_offset` 175 50 + `new_quirks_from_quest_start_offset` 145 18, lines `new_quirks_from_quest_spacing` 0 24 apart | line j at **960, T + 68 + 24 j**; three lines (T + 68 .. T + 140) fill the plate's inside (T + 60 .. T + 148) | DD1's shared quirk list: style `quirk_name` (Ubuntu small), colours `quirk_positive` (notable), `quirk_negative` (harmful), a disease in `disease` (177 25 0). READ AS centred on x 960 |

The campaign status widget from C (FILE: `hero_campaign_status_layout`, `resolve_level_bar.layout.darkest`):

| part | values | screen |
|---|---|---|
| badge | `resolve_level_bar_offset` 6 4, then `resolve_level_number.background_offset` -3 0: `shared/resolve_level_bar/resolve_level_bar_number_background_lvl<0..6>.png` (64x64) | 1199, T + 7 |
| level number | `number_offset` 30 31 (the number's middle); `resolve_number` (Ubuntu medium, black) | middle 1232, T + 38 |
| experience bar | `resolve_level.bar_pos` 12 28: `resolve_level_bar_mask.png` (37x59) there; the fill is `gradient_size` 16 40 at `gradient_offset` 10 16, colours `resolve_gradient_top` #aea996 over `resolve_gradient_bottom` #6d6a5e | holder 1214, T + 35; fill x 1224 .. 1240, y T + 51 .. T + 91 |
| level-up pulse | `resolve_level_pulse.posn_offset` 30 30: Spine `fx/raid_results_resolve_pulse` (2.3) | origin 1232, T + 37 |
| stress | `stress_bar_offset` -14 100, `stress_bar_spacing` 10 0: ten pips, `overlays/stress_pip_empty.png` (8x12) / `stress_pip_full.png` (9x10) | 1182 + 10 k, T + 103: between the banner's inner frame and its first rule |
| affliction | `affliction_offset` 36 112 | 1232, T + 115: between the banner's two rules |
| afflicted badge | `afflicted_background_offset` -10 -15: `resolve_level_bar_number_afflicted_background.png` (77x77); it pulses red (`resolve_level_bar_pulse_afflicted_anim`: 255 0 0, 0.2 s, easeInOutQuad) | |

The "row of pips" under the badge on the owner's second screenshot is this stress row, not experience.

### 1.5 The grid rule (EXE: the shared inventory grid)

A grid has `number_of_columns` C, `start_pos`, `offset` (pitch) and `is_centred`. The slot of item i out of n:
row = i / C, column = i mod C, x = start.x + column x pitch.x, y = start.y + row x pitch.y; when centred, a
row holding r items is moved right by (C - r) x pitch.x / 2. The card stands 4 px inside its slot.

The treasure and the heirloom grids are drawn in the grid's one-row mode (EXE: a flag the results screen sets
on both): every card in row 0, and when there are more cards than columns the pitch shrinks to
pitch.x x C / n, so that the row keeps its 480 px and the cards overlap (12 cards: 40 px apart). In that mode
a stack whose item type has `can_unpack` true in `item.display.json` (food only) is laid out as one card per
unit. The rewards grid is the plain kind (a quest never pays more than 8 kinds of thing).

### 1.6 What a click does (EXE: `ProgressForwardInternal`)

The red button, on either page:
1. While the page's reveal is still running: the click finishes it at once (everything counted, bars full).
   Nothing else happens.
2. Page 1, reveal over: sound `/general/raid_screen/hero_click`, page 2 replaces page 1 at once (no slide; the
   panel, the titles and the bar stay).
3. Page 2, reveal over, masks still unopened: every mask opens (sound `/general/raid_screen/quirk_click`).
   Nothing else happens.
4. Page 2, nothing left: sound `/general/raid_screen/town_click`, on to the town.

A mask is a button of its own: a click opens that hero's quirks (`quirk_click`). There is no way back from page
2 to page 1.

### 1.7 Styles

| style | font | colour |
|---|---|---|
| `raid_results_quest_result` | dwarven_axe_large | by outcome: `raid_results_quest_result_was_completed` notable (200 180 110), `..._was_not_completed_escape` **0 0 0**, `..._was_not_completed_defeat` harmful (177 25 0), `..._was_not_completed_regroup` neutral (174 172 162) |
| `raid_results_quest_title`, `raid_results_quest_inventory_title`, `raid_results_party_gold_inventory_title`, `raid_results_party_heirloom_inventory_title`, `raid_results_hero_name` | dwarven_axe_medium | notable |
| `raid_results_hero_resolve_from_quest` | ubuntu_small | #b5af9c |
| `raid_results_gold_popup` | dwarven_axe_large | notable |
| `subtitle_context_raid_results` | dwarven_axe_large | notable |
| `inventory_amount` / `inventory_amount_unselectable` | dwarven_axe_medium | notable / #5d5a50 |
| `inventory_unselectable` (a card that is not to be had) | | tint #666666, saturation 0.2 |
| `town_progression_forward` | dwarven_axe_large | harmful |
| `town_large_currency_amount`, `town_currency_amount` | dwarven_axe_large, ubuntu_small | notable |
| `resolve_number` | ubuntu_medium | 0 0 0; `resolve_new` 255 255 255 |
| `quirk_name` | ubuntu_small | `quirk_positive` notable, `quirk_negative` harmful, `quirk_new_pulse_to` neutral, `quirk_tooltip_description` neutral |

### 1.8 Timings (`raid_results.anim.darkest`, and constants in the exe)

| entry | value | what it drives |
|---|---|---|
| `treasure_item_alpha.value` | 1.0 | a treasure card appears (it is invisible until its turn) |
| `treasure_item_wait.time` | 0.3 s | pause before the next card |
| `treasure_item_value_popup_offset_y` | 60 px in 1.0 s | the card's worth in gold rises from the card (`raid_results_gold_popup`) |
| `treasure_gold_total_scale_pulse` | 1.1 in 0.2 s | the total swells and settles (easeInOutQuad, there and back) each time a card is counted |
| `heirloom_total_scale_pulse` | 1.4 in 0.2 s | the same for an heirloom total |
| `shard_*` | | Farmstead only |
| `raid_results_heroes_xptext_xoffset` | from -175 to 0 in 2.0 s | "+N Resolve XP" slides in (easeInOutElastic) while its opacity goes 0 to 1 over the same 2.0 s (easeInOutQuad) |
| `raid_results_heroes_resolve_xp_bar.time` | 2.0 s | the experience bar fills from the old amount to the new |
| `raid_results_heroes_masks.offset` | -7 39 | the masks' place (1.4) |
| `raid_results_heroes_quirk_text_wait.time` | 0.666 s | after a mask is opened, before the names show |
| `raid_results_heroes_quirk_text_alpha` | 1.0 in 0.8 s | the names fade in |
| EXE, page 2's lead-in | 0.661 s, then 0.772 s more | with experience to show and a hero alive: `/general/raid_screen/resolve_in` at 0.661 s and `/general/raid_screen/mask_flash` at 1.433 s; otherwise only `mask_flash` at 1.433 s |
| `game_over_fade_anim` (`game_over/game_over.anim.darkest`) | in 1.0 s, out 1.0 s, easeOutSine | 4.11 |

Page 1 in order (EXE `InitCampaignAnimTweens`; the order of the steps inside one card is READ AS): the rewards
and the titles stand from the start. Then each treasure card in turn: it appears, the `treasure_glow` effect
plays on it with `/general/raid_screen/treasure_add`, its worth rises 60 px, the total takes the worth and
pulses, 0.3 s pass. Then each heirloom card the same way with `heirloom_glow`, `/general/raid_screen/heirloom_add`
and the total of its kind pulsing to 1.4.

Page 2 in order: all living heroes at once. The experience line slides in and the bar fills (2.0 s); a level
gained swaps the badge for the next level's and plays the pulse. While the line is still sliding a hero's
masks play `disabled_loop`; after it `active_loop` (they can be clicked); a click plays `reveal` (1.5 s) and the
names fade in from 0.666 s to 1.466 s.

### 1.9 In the layout file but not used by the game (EXE: no code reads them)

`next_pos` 1870 980, `back_pos` 50 980, `return_to_town_pos` 1870 980 and the three
`raid_results_screen_{next,back,return_to_town}_layout.text_offset` blocks: an older corner-button design. The
button is the shared progression button in the middle of the bar. `new_quirks_from_quest_controller_selected_icon_*`
are for a gamepad.


## 2. Art

### 2.1 The screens' own pictures (`raid_results/`)

| file | size | what |
|---|---|---|
| `raid_results.quest_completed_background.png` | 899x1080 | Victory: black panel with thin gold lines (x 52 and 811, from y 107 down to about 850), a blue plate with gold rules for the title, two red wax seals with ribbons beside it, a warm glow above |
| `raid_results.quest_not_completed_escape_background.png` | 899x1080 | Escape: black panel with brushed edges, a spiked red sun at the top (the title is written on its disc), red rays falling from the band; thin grey lines at x 68 and 827 |
| `raid_results.quest_not_completed_defeat_background.png` | 899x1080 | Defeat: the same sun with a crow's skull in front, dark red tentacles down both edges |
| `raid_results.quest_not_completed_regroup_background.png` | 899x1080 | Regroup (Crimson Court's courtyard, the Shrieker): a bat-faced head, a blood-red crest, a dripping scroll. Not needed by the mod |
| `raid_results.endless_wave_regroup_background.png`, `wave_raid_results.items_frames.png` | 899x1080, 533x723 | Farmstead. Not needed |
| `raid_results.items_frames.png` | 533x723 | page 1's plate (1.3), opaque |
| `raid_results.heroes_frames.png` | 680x675 | page 2's four rows (1.4), translucent plates |
| `deadhero_portrait.png` | 85x85 | the dead hero's portrait: a skull in red light |
| `dlc/735730_color_of_madness/raid_results/timeandspacehero_portrait.png` | 85x85 | a hero lost in the Farmstead's time and space. Not needed |

All four outcome panels are drawn the same way; nothing in them is animated. The emblem is part of the panel
picture, not a separate sprite.

### 2.2 Shared pictures the screens use

| file | size | what |
|---|---|---|
| `loading_screen/loading_screen.<dungeon>_0.png` | 1920x1080 | the picture behind the panel |
| `shared/progression/progression_bar.png` | 1920x138 | the bar |
| `shared/progression/progression_forward.png`, `progression_forward_selected_overlay.png` | 312x52, 311x24 | the red button (a black brush stroke between two red rules; its words are text) and its glow under the pointer |
| `shared/estate/currency.gold.large_icon.png` | 88x88 | the pile of gold beside the treasure total |
| `shared/estate/currency.{bust,portrait,deed,crest}.icon.png` | 40x40 | the heirloom totals |
| `panels/icons_equip/gold/inv_gold+_0..3.png`, `heirloom/inv_heirloom+<kind>.png`, `gem/...`, `provision/inv_provision+_0..3.png`, `supply/...`, `trinket/inv_trinket+<id>.png` | 72x144 | the cards. Gold's picture by amount: thresholds 250, 500, 750, 1000; food's by 0, 5, 11, 14 (`item.display.json`) |
| `shared/resolve_level_bar/resolve_level_bar_number_background_lvl0..6.png` | 64x64 | the badge by level |
| `shared/resolve_level_bar/resolve_level_bar_number_background_glow_lvl0..6.png` | 64x64 | the badge's glowing twin (READ AS: laid over the badge when a level was just gained) |
| `shared/resolve_level_bar/resolve_level_bar_number_afflicted_background.png` | 77x77 | behind the badge of an afflicted hero |
| `shared/resolve_level_bar/resolve_level_bar_mask.png` | 37x59 | the bar's holder under the badge, hollow where the 16x40 fill shows |
| `overlays/stress_pip_empty.png`, `stress_pip_full.png` | 8x12, 9x10 | stress |
| `shared/hero/portrait_icon_disease.png` | 24x24 | on a diseased hero's portrait |
| `shared/character/quirkreplaced.png` (26x26), `lockquirk.png`, `seriousquirk.png`, `singleton_quirk_positive.png`, `singleton_quirk_negative.png` (32x32) | | marks of DD1's quirk list (a quirk that pushed another out, a locked one, ...). Whether the results list draws them is not established |
| `shared/tooltip/tooltip_background.png` | 128x128 | tooltips (docs/recon/dd1-raid-ui.md) |

### 2.3 The three Spine effects (`fx/`, Spine 2.1.27 binary, read with the mod's own reader)

All attachments are meshes of one atlas region each (plain quads); colours are RGBA keys; times in seconds;
scales are x, y; y is up in Spine.

**`fx/raid_results_loot_glow/raid_results_loot_glow.sprite.{atlas,skel,png}`** (sheet 700x264). On a card as it
is counted.
- Regions: `heirloom_glow` 124x126 (purple blur), `heirloom_primary_rays` 155x155, `heirloom_secondary_rays`
  97x97, `treasure_primary_rays` 251x251 (gold star), `treasure_secondary_rays` 160x160, `treasure_coin01..05`
  (24x31, 30x32, 31x25, 27x30, 29x31).
- Bones: `root`; `item`; `offset` at 5, 0, parent of the glow and ray bones and of `coins`; coins at
  (15.8, 79.7), (-5.7, 74.3), (-9.9, 48.1), (-13.6, 23.3), (10.0, 22.1).
- `treasure_glow` (0.80 s): both treasure rays visible, heirloom parts hidden. Primary rays turn 0 to 315
  degrees and scale 0.5 / 1.0 at 0.1 / 0.5 at 0.43 / 0 at 0.67; secondary rays turn 0 to 45 degrees, scale
  0.5 / 1.0 at 0.17 / 0.5 at 0.5 / 0 at 0.67. The five coins fade in between 0.07 and 0.23, hold, fade out
  between 0.57 and 0.73, each tumbling up and falling back along its own short arc (about 30 to 40 px up and
  down, a quarter to a half turn); the `coins` bone grows 0.5 to 1.0.
- `heirloom_glow` (0.67 s): coins and treasure rays hidden. Glow scale 0.5 / 1.0 at 0.1 / 0.5 at 0.5 / 0 at
  0.67, opaque until 0.63; primary rays turn 0 to 315 degrees, scale 0.5 / 1.0 at 0.1 / 0.5 at 0.43 / 0 at 0.67;
  secondary rays turn 0 to 45 degrees, scale 0.5 / 1.0 at 0.17 / 0.5 at 0.5 / 0 at 0.67.

**`fx/raid_results_quirk_reveal/raid_results_quirk_reveal.sprite.*`** (sheet 303x571). The masks.
- Regions: `quirk_happy` 48x97 and `quirk_sad` 48x98 (the two halves of one black theatre mask, laughing and
  weeping), `ring_glow` 164x165, `primary_rays` 161x161, `secondary_rays` 122x121, `split_glow` 34x133 (a gold
  sliver), `horizontal_glow` 476x89, `vertical_glow` 89x238 (the last two stored turned in the sheet).
- Bones: all at the root's origin except `quirk_happy` at -24, 0 and `quirk_sad` at 24, 0: together a 96 px
  wide mask around the origin. Draw order: horizontal glow, ring, secondary rays, primary rays, vertical glow,
  happy, sad, split glow.
- `disabled_loop` (1.0 s, loop): only the two halves show.
- `active_loop` (1.0 s, loop): halves, ring, both rays; ring and rays breathe 1.0 / 1.2 at 0.33 / 1.3 at 0.5 /
  1.2 at 0.57 / 1.0 at 1.0. The glows are hidden.
- `reveal` (1.5 s, once): the halves part (happy 3 px left by 0.17 s, sad 3 px right by 0.23 s, each with a
  small recoil); split glow in at 0.17, gone by 0.8, growing 0.25 to 1.25; rays grow 0.25 / 1.0 at 0.17 / 1.25
  at 0.5 and fade out between 0.53 and 1.0; ring fades out by 0.67; vertical glow 0 / 1.0 at 0.2 / 1.1 at
  0.67 / 0 at 1.0; horizontal glow appears at 0.5 (scale 0 at 0.5, 1.3 x 0.9 at 1.0, 1.0 at 1.5) and fades out
  from 1.0 to 1.5. The names fade in under that last glow (0.666 s to 1.466 s).

**`fx/raid_results_resolve_pulse/raid_results_resolve_pulse.sprite.*`** (sheet 159x83). On the badge when a level
is gained.
- Regions `glow` 74x74 and `rays` 79x79, both on the root.
- `pulse` (1.0 s, loop): glow 0.8 / 1.0 at 0.27 / 1.1 at 0.43 / 1.0 at 0.53 / 0.8 at 1.0; rays 0.8 / 1.0 at
  0.2 / 1.1 at 0.43 / 1.0 at 0.6 / 0.8 at 1.0.

The mod's `Dd1/SpineSkeleton.Pose` reads all three (meshes, bone and colour keys). All three are simple enough
to be played as a handful of tweened sprites instead, from the numbers above.

### 2.4 Game over (`game_over/`)

`game_over.background.png` 1920x1080 at 0, 0; `game_over.layout.darkest`: `estate_info_pos` 400 70, from it
`name_offset` 570 86 (`game_over_estate_name`, DwarvenAxe large, notable), `number_of_weeks_offset` 780 355 and
`number_of_dead_heroes_offset` 780 570 (Ubuntu medium, neutral), `reason_offset` 580 720 (`game_over_estate_reason`,
DwarvenAxe medium, harmful). See 4.11: this is not the screen of a lost party.


## 3. Strings

English section of `localization/miscellaneous.string_table.xml` (one XML for all languages, `<language
id="english">` first, entries `<entry id="..."><![CDATA[...]]></entry>`; the `.loc2` files are the compiled
form and are not needed).

| id | English | where |
|---|---|---|
| `raid_results_quest_result_was_completed` | Victory! | title |
| `raid_results_quest_result_was_not_completed_escape` | Escape | title |
| `raid_results_quest_result_was_not_completed_defeat` | Defeat | title |
| `raid_results_quest_result_was_not_completed_regroup` (Crimson Court's table), `raid_results_quest_result_endless_wave_regroup` (Farmstead's) | Regroup! | not needed |
| `town_quest_name_<type>+<length>+<dungeon>+<goal>` | Scout / Explore / Map (explore, length 1 / 2 / 3), Skirmish / Cleanse / Exterminate (cleanse), "Reclaim Relics of the Light" and the other gather and activate names, the plot quests' own names | the quest's name under the title (the mod has these through `QuestMapText`) |
| `raid_results_quest_inventory_title` | Quest Rewards | ribbon |
| `raid_results_party_gold_inventory_title` | Collected Treasure | |
| `raid_results_party_heirloom_inventory_title` | Collected Heirlooms | |
| `raid_results_progression_heroes` | Next | page 1's button |
| `raid_results_progression_return_to_town` | Return to Town | page 2's button |
| `raid_results_hero_resolve_from_quest` | +%d Resolve XP | |
| `raid_results_new_quirks_title` | New Quirks: | in the table, but no code uses it (EXE): the quirks are listed without a heading |
| `str_new_quirk_colon`, `str_quirk_removed` | New Quirk: / Quirk Removed: | DD1's pop-ups elsewhere; not on this screen |
| `resolve_bar_tooltip_level_line_format` | Level: %d | badge tooltip |
| `resolve_bar_tooltip_xp_line_format` | Resolve XP: %d/%d | |
| `resolve_bar_tooltip_additional_stress_format` | {colour_start\|stress}Stress:{colour_end} %d/%d | |
| `resolve_bar_tooltip_affliction_format` | {colour_start\|afflicted}Affliction:{colour_end} %s | |
| `str_resolve_0` .. `str_resolve_6` | Seeker, Apprentice, Adventurer, Veteran, Master, Champion, Legend | the levels' names |
| `str_inventory_title_<type><id>`, `str_inventory_description_<type><id>` | e.g. "Gold greases palms, builds empires, and instigates murder.", "A sigil of honor earned by family actions long since past." | card tooltips (`InventoryText` reads the titles already) |
| `str_inventory_gold_value_format` | [Value: %s Gold Each] | card tooltip of a thing worth gold |
| `str_quirk_name_<id>`, `str_quirk_description_<id>` | | DD1's quirks; the mod's quirks are DD2's and bring their own names |
| `buff_stat_tooltip_resolve_xp_bonus_percent` | %+d%% Resolve XP | |
| `str_darkest_dungeon_failure_bonus` | Failure in the Darkest Dungeon has only strengthened the resolve of those left behind. (x2 Resolve XP Bonus in the next quest.) | town notice after a failed descent |
| `str_glossary_term_definition_42` | Earning Resolve XP progresses a hero towards the next Resolve Level. Resolve XP can only be earned by completing quests. Heroes do not earn Resolve XP if they abandon or fail quests. | DD1's own statement of the rule (4.6) |
| `str_allperished`, `str_perished`, `str_perished_from_darkest_dungeon_exit`, `str_death_*` | %s joined each other in a heroic demise. There were no survivors. / %s met their final fate during the quest. / ... | the activity log's lines (the mod uses them there); deaths are not written out on the results screen |
| `str_returned_from_<type>_<dungeon>_success` / `_failure` | | activity log (in use) |
| `retreat_confirm_raid_question` | Are you sure you want to retreat? The heroes will suffer the stress of defeat... | the question before (in use) |
| `game_over_estate_name_format`, `game_over_number_of_weeks_format`, `game_over_number_of_dead_heroes_format`, `game_over_reason_weeks`, `game_over_reason_dead_heroes`, `game_over_porgress_forward` (sic) | The %s Estate / Weeks: %d / Dead Heroes: %d / "Too much time has passed - the creature has grown strong and is ready to take to the stars!" / "The blood of the fallen has invigorated the Thing, accelerating its rebirth!" / Game Over | 4.11 |

There is no tooltip on the titles, the totals or the red button, and no string for "level gained" on this
screen: a new level shows as the badge's change and the pulse.


## 4. Logic

### 4.1 Order of things

DD1's application states (EXE names): `as_raid_finish` -> `as_raid_results_start` / `_update` / `_finish` ->
`as_raid_result_to_town_transition` -> `as_play_post_raid_video` (only `plot_darkest_dungeon_4`: "epilog") ->
`as_town_visit_start` (the town-visit loading screen, then the town, where the week has advanced and the town
event is told). Everything the screens show has already been applied when they appear: the results object
(`Campaign::RaidFinishResults`) is made at raid finish, the roster's `OnRaidFinish` gives the experience, the
quirks and the diseases from it, and the screen only reads it. Closing the game on the results screen loses
nothing.

Pages: 1 = items (always, even with an empty bag: the frame and the three titles stand), 2 = heroes (always).

### 4.2 Which outcome (EXE: `LoadAssets`)

1. The quest's goal was met when the party left: **Victory!**, the completed panel.
2. Otherwise, a quest that can be regrouped from: Regroup (not in the base game's four regions).
3. Otherwise count the party's heroes who are not dead. At least `min_hero_count_for_escape` (`shared/rules.json`:
   **3**; the same in every mode): **Escape**. Fewer: **Defeat**.

So an abandoned quest reads "Escape" only while three or four heroes are alive; coming home with one or two,
or not at all, reads "Defeat". The retreat from a Darkest Dungeon descent that costs a hero counts the hero as
dead.

### 4.3 Quest rewards

- The row always lists what the quest offered: gold, heirlooms, trinkets, as on the quest's card in town.
- They are given only when the goal was met. On Escape and Defeat every reward card is marked as not to be
  had (EXE: the row's item-state callback answers "available" for all when completed; otherwise only for a
  card that is wholly `trinket_retention`, the Shrieker's returned trinkets). READ AS drawn in DD1's
  unavailable look: the card tinted #666666 at saturation 0.2, its amount in #5d5a50. This is what the owner's
  Escape screenshot shows under "Quest Rewards": the offer, withheld.
- The reward's resolve experience is not a card.
- Amounts: `campaign/quest/quest.generation.json` `rewards` (gold `item_table`, `heirloom_amount_table`,
  `trinket_chance_table`) and the plot quests' `completion_reward` (docs/recon/dd1-spec.md 3.2, 3.3,
  docs/recon/quests-and-trinkets.md). The mod's `QuestBoard` holds them on the `Quest` already.

### 4.4 Collected Treasure

What comes home (FILE `campaign/estate/estate.json` `quest_fail_keep_rates`, applied when the goal was not
met; with the goal met everything is kept):

| item type | kept on a failed quest |
|---|---|
| gold, gem, heirloom, trinket, jewellery, journal_page | 1.0 |
| provision (food), supply | 0.25 |
| quest_item | 0.0 |

The treasure row (EXE: the row's filter) shows every stack of what came home that is **not a trinket** and is
either gold or has a `sell_gold_value` above 0. Its worth, added to the total as the card is counted: a gold
stack its amount; anything else amount x `sell_gold_value`:

| | `sell_gold_value` (`inventory/*.inventory.items.darkest`) |
|---|---|
| gems | ancient_idol 4500, trapezohedron 3500, pewrelic 2500, ruby 1250, antiqrelic 1250, sapphire 1000, emerald 750, onyx 500, antiqrelicsmall 500, jade 375, citrine 250 |
| food (`provision`) | 5 (bought for 75) |
| supplies | shovel 25 (250), skeleton_key 20 (200), medicinal_herbs 20 (200), laudanum 20 (100), bandage 15 (150), antivenom 15 (150), holy_water 15 (150), torch 5 (75) |
| nothing: firewood, dog_treats, quest items, heirlooms | 0: not shown here |

Leftover provisions are therefore sold at a fifteenth to a fifth of their price (a quarter of them only, after
a failed quest), and that is the whole "refund". The total on the owner's screenshot, 1,555, is such a sum.
Gold stacks are the bag's own (1750 a stack, 2500 with an Antiquarian: `base.inventory.extra_stack_limits`).
Food is laid out a ration a card (1.5); every other stack is one card with its count.

Trinkets found on the way are not shown on either page (EXE: excluded from the treasure row, and the heirloom
row takes heirlooms only). They are simply in the trinket inventory afterwards.

A party lost to the last hero brings nothing home.

### 4.5 Collected Heirlooms

The bag's heirloom stacks (stack limits: portrait 3, bust 6, deed 6, crest 12), one card a stack; the header's
totals count each kind brought home. Kept in full on a failed quest. The four kinds and their order:
`campaign/estate/estate.json` `currencies` (bust, portrait, deed, crest).

### 4.6 Resolve experience and levels

- Only a completed quest pays experience, and only to heroes alive at its end (FILE comment in
  `quest.generation.json`: "how much Resolve XP is awarded for a successful quest"; DD1's glossary, section 3;
  EXE: the line and the lead-in sound are skipped when the quest's experience is 0).
- Amount: generated quests `rewards.resolve_xp_table[difficulty][length]`: difficulty 1: 2 / 3 / 4 (short /
  medium / long), 3: 4 / 6 / 8, 5: 8 / 12 / 16; rows 0, 2, 4 and **6 are all zero**. Plot quests carry their own
  `completion_reward.resolve_xp`: boss quests 4 / 8 / 16 by tier, the four Darkest Dungeon descents 16, the
  tutorial 2 (docs/recon/dd1-spec.md 3.3).
- Bonuses (`resolve_xp_bonus_percent` buffs, added on top): the town events' `town_event_<region>_resolve_xp`
  +33% for one quest, `darkest_dungeon_failure_roster_resolve_xp` (the whole roster after a failed descent with
  a party at level 5 or above), the buffs `RESOLVEXPBONUS10/20/30` (+10 / 20 / 30%; `shared/buffs/base.buffs.json`).
- Levels: `campaign/roster/roster.variables.json` `resolve_level_thresholds` 0, 2, 8, 14, 24, 36, 48 (total
  experience for levels 0 .. 6).
- On the screen: "+N Resolve XP" is the hero's experience after less before (so with the bonuses); the bar
  shows the share of the way from the level's threshold to the next and fills over 2.0 s; passing a threshold
  shows the next level's badge and the pulse. How a bar that passes a threshold moves (to the top, then from
  the bottom again) is READ AS.

### 4.7 Quirks and diseases gained (EXE: the roster's raid-finish roll, per living hero; numbers FILE `shared/rules.json`)

With s = the hero's stress, capped at 100 (of DD1's 0 .. 200):

| | at stress 0 | at stress 100 | keys |
|---|---|---|---|
| negative quirk, quest completed | 0.30 | 0.50 | `negStress0Success`, `negStress100Success` |
| positive quirk, quest completed | 0.50 | 0.40 | `posStress0Success`, `posStress100Success` |
| negative quirk, quest not completed | 0.40 | 0.70 | `negStress0Failure`, `negStress100Failure` |
| positive quirk, quest not completed | 0.40 | 0.25 | `posStress0Failure`, `posStress100Failure` |

chance = at0 + (at100 - at0) x s / 100. Two independent rolls: one for a negative quirk, one for a positive
one; each success draws one quirk at random from the quirks of that kind the hero can take (not one already
held, not an incompatible one).

A disease: only for a hero whose resolve level is at least `disease_after_quest_min_resolve_level` (2). Chance
= max(`disease_max_chance` 0.32 - the hero's disease resistance x `disease_hero_disease_resist_weight` 0.33,
`disease_after_quest_min_chance` 0.05); success draws one disease at random.

Limits: at most `questQuirksPerHeroLimit` 3 new entries a hero a quest (a default in the exe; it is why the
plate holds three lines); `quirks_max_positive` 5, `quirks_max_negative` 5, `quirks_max_diseases` 3: a new quirk
on a full hero pushes an unlocked old one out.

Also in the same roll, not needed for the base regions: a plot quest's `party_quirks_to_apply_on_completion` /
`_on_failure` (the Shrieker's corvid quirks) and a monster's `raid_finish_quirk_class_id`.

On the screen the new quirks and the disease are hidden behind the masks until clicked (1.6).

### 4.8 Stress

- Quest not completed: every hero of the party takes `campaign/quest/quest.exit_penalty.json`
  `fail_penalty.stress_damage` 20 (`regroup_penalty` 0).
- Quest completed: no relief. Only the Darkest Dungeon's descents clear stress, and for the whole roster
  (`is_roster_stress_cleared_on_completion`).
- The pips on page 2 show the stress the hero comes home with.

### 4.9 Deaths

A hero who died on the expedition keeps a row: name, portrait under `deadhero_portrait.png`, badge. No cause of
death is written here (that is the graveyard's and the activity log's). A party wiped out shows page 1 with
the withheld rewards and two empty rows, then four dead rows.

### 4.10 Sound, music, narration

Events of `audio/secondary_banks/raid_screen.bank` (the mod's player finds them under `event:/general/`):

| event | when (EXE) |
|---|---|
| `/general/raid_screen/success` (bed, 5 samples) | the screen's music after a completed quest |
| `/general/raid_screen/fail` (bed, 2 samples) | the screen's music after a quest not completed |
| `/general/raid_screen/com` | Farmstead. Not needed |
| `/general/raid_screen/treasure_add` | a treasure card is counted |
| `/general/raid_screen/heirloom_add` | an heirloom card is counted |
| `/general/raid_screen/hero_click` | "Next" |
| `/general/raid_screen/resolve_in` | 0.661 s into page 2, when there is experience to show |
| `/general/raid_screen/mask_flash` | 1.433 s into page 2 |
| `/general/raid_screen/quirk_click` | a mask opened, or all of them by the button |
| `/general/raid_screen/town_click` | "Return to Town" |
| `/general/raid_screen/body_count*`, `shard_add` | Farmstead |

The three beds are entries of the game's music table beside `/music/mus_town` and `/music/mus_exploration`:
the exploration music gives way to them.

Narration (`audio/narration.json`): `quest_end_completed` (28 lines, by dungeon and quest type, chance 0.5
each, once a raid) and `quest_end_not_completed` (11 lines `/vo/bad/general_quest_fail_01..11`, chance 0.3
each). The mod plays both already (docs/recon/town-events.md 6); on DD1's results screen their subtitle is
drawn at 960, 990 over the bar (`s_RaidResultsSubtitleContextTunables`).

### 4.11 Game over

`game_over/` is not the screen of a lost party: a wipe goes through the two result pages like any other
outcome ("Defeat") and on to the town. Game over exists only where a campaign has limits (`shared/rules.json`
`new_game_plus_week_limit` 91 and `new_game_plus_hero_death_limit` 13; Stygian and Bloodmoon, `modes/new_game_plus`:
86 and 12): past the limit the game shows the background, the estate's name, the weeks, the dead and one of
the two reasons, with "Game Over" as the way out (fades of 1.0 s). The mod has no such limits.


## 5. The mod today, and the gaps

Where an expedition ends: `Dungeon/DungeonRun.cs`, `Finish(QuestEnded)`, reached from `Leave()` (the HUD's
return or abandon, through `Exploration.Leave` and `End`) and from a lost fight (`QuestEnded { Status = Failed
}`). In order it: clears the camp buffs; gives every living hero the abandon stress (`RaidRules.AbandonStress`
from `quest.exit_penalty.json`, scaled by 10 / 200 to DD2's stress); counts the expedition won or lost;
`QuestBoard.PayForRetreat` (the Darkest Dungeon's hero); `QuestBoard.Finished` (on success only: gold,
heirlooms, trinkets, the region's points, a story quest's closing words, stress wipe); `BringHome` (the bag
through `Inventory.Settle` with DD1's keep rates: coins, gems and supplies at sell value as one sum of gold,
heirlooms by kind, trinkets by the trinket keep rate); `EstateState.RaiseExpeditionEnded` (listeners:
`Resolve` gives the table's experience to survivors of a success, `TownEventEffects` the bonus share,
`ActivityLog` the week's entries, `Narration` the failure line, `TownEvents` the next week's event); the week
advances; the run is disposed, the corridor hidden, the hamlet shown, the estate saved.

What the player sees: nothing of the above but two lines of text queued with `QuestBoard.Report` ("The quest
is paid: ...", "The party's bag is emptied onto the table: ...") in the hamlet's narration box, and the
activity log's entries (banner, "Brought home: ...", level gained).

| DD1's screen needs | the mod today | gap |
|---|---|---|
| outcome Victory / Escape / Defeat | `RaidStatus` Succeeded / Abandoned / Failed | Escape or Defeat by the living heroes (3 or more), not by how the quest ended (4.2) |
| quest name | `DungeonRun.Title` (`Quest.Name`, the mod's own wording) | DD1's short name is available through `QuestMapText` (what the quest card shows) |
| region's picture | - | `loading_screen/loading_screen.<dungeon>_0.png`: not used anywhere yet |
| the offered rewards as a list, given or withheld | `Quest.Gold`, `.Heirlooms`, `.Trinkets`; `QuestBoard.Finished` pays and returns nothing | a list that survives the paying (a trinket may be re-rolled or turned to gold when paid: show what was really given) and exists on a failure too |
| treasure: every stack with its worth | `Inventory.Settle` returns sums only (`RaidHaul.Coins`, `GemGold`, `SupplyGold`) | the kept stacks in bag order with amount and DD1 worth each; food as single rations |
| heirloom stacks and totals | `RaidHaul.Heirlooms` (totals by kind) | the stacks |
| per hero: experience gained, level before and after | `Resolve.Experience`, `Level`, `ToNextLevel`; nothing remembers "before" | a snapshot before `RaiseExpeditionEnded` and a reading after it (this catches the town events' bonus) |
| per hero: quirks and a disease gained at the quest's end | none: no quirk or disease is rolled at the end of an expedition | the roll of 4.7 on DD2's quirks (`actor.QuirkContainer.AddRandomQuirks` with `QuirkDefinition.QUIRK_POSITIVE_TAG` / `QUIRK_NEGATIVE_TAG` is how the Stage Coach adds them), and the names kept for the screen |
| per hero: stress, affliction, disease mark | DD2's actor | stress 0 .. 10 as ten pips (the raid HUD does this); DD2 has no lasting affliction: the label stays empty |
| the dead of the expedition | `Graveyard.Fallen` (name, class, level), `ActivityLog`'s memo of names; `LivingParty()` knows only the living | the party as it set out (guid, name, class, portrait) kept with the run and its save, so that a dead hero has a row |
| resolve badge with its bar | the badge alone in `RosterPanel` and `StageCoachPanel` | the bar under it (holder, 16x40 fill) and its animation: a widget to share with the roster row |
| two pages, a button, tweens, three effects | - | all of it |
| results music | exploration music stops with the run | `/general/raid_screen/success` / `fail` through `Dd1AudioEngine` |

Found on the way, to verify outside this spec:
- **A Darkest Dungeon descent pays no experience.** `Resolve.OnExpeditionEnded` indexes `resolve_xp_table` by
  the expedition's difficulty; `DungeonRun.Finish` passes 6 for the Darkest Dungeon and row 6 of DD1's table is
  all zero. DD1 pays 16 through the plot quest's `completion_reward.resolve_xp`, which `QuestRewardRules` reads
  into `QuestReward.ResolveXp` and nothing uses. The same field makes DD1's boss quests pay 4 / 8 / 16 where the
  table's medium column gives 3 / 6 / 12.
- `darkest_dungeon_failure_roster_resolve_xp` and the trinkets' experience bonus are not in (the former is
  listed in docs/recon/dd1-missing-mechanics.md).


## 6. Build plan

Principle kept from today: **apply first, show after**. `Finish` goes on doing everything and saving; it
also fills a results record, and the screens are a view of that record shown between the corridor and the
hamlet. A game closed on the results screen has lost nothing; the record need not be saved.

### 6.1 Classes

| class | what | binds to |
|---|---|---|
| `Dungeon/RaidResults.cs` (plain data) | `Outcome` (Victory, Escape, Defeat), `QuestName`, `Dungeon`, `Rewards` (list: item def or trinket id, amount, `Given`), `Treasure` (list: `ItemDef`, amount, DD1 worth), `TreasureGold` (DD1 units), `Heirlooms` (list: kind, amount), `HeirloomTotals`, `Heroes` (list: guid, name, class id, portrait, `Dead`, `XpBefore`, `XpAfter`, `LevelBefore`, `LevelAfter`, stress, diseased, `NewQuirks` (name, kind: positive / negative / disease)) | filled in `DungeonRun.Finish` |
| `Dungeon/RaidResultsLayout.cs` | every number of section 1 read from the files named in 1.1 with DD1's stock values as fallbacks, the way `RaidLayout` does; the anim file's timings | `DarkestFile.Load` |
| `Dungeon/RaidResultsScreen.cs` (MonoBehaviour on the `RaidUi` 1920x1080 frame) | background, panel, titles, bar and button; page 1 and page 2 as two children; the reveal as a list of timed steps with "finish now"; the click rule of 1.6; music and sounds | `RaidResults`, `RaidResultsLayout`, `Dd1Fonts.Style`, `Dd1Install.Sprite`, `RaidText.Get` |
| `Dungeon/RaidResultsFx.cs` | the three effects: either `SpineSkeleton.Pose` drawn into a UI mesh (`CanvasRenderer.SetMesh`), or tweened sprites cut from the three atlases by the numbers of 2.3 | `SpineAtlas`, `SpineSkeleton` |
| `Estate/ResolveBar.cs` | badge, number, holder, fill, pulse; `Set(level, share)`, `Animate(fromXp, toXp, seconds)` | `Resolve`; to be used by `RosterPanel` too (DD1's row has the bar where the mod writes class and path) |
| `Estate/QuestAftermath.cs` | the rolls of 4.7 for the survivors, by DD1's numbers read from `shared/rules.json`; returns what each hero got | `RosterLifecycle`, DD2's quirk container; stress share = `actor.Stress / actor.StressMax x 2`, capped at 1 (DD2's 10 = DD1's 200) |
| `Dungeon/RaidResultsDev.cs` | bridge commands: `results.show {outcome, heroes, ...}` with made-up data, `results.state`, `results.click` | `AgentBridge` |
| `tools/preview_raid_results.py` | the two pages drawn offline from the install, as the other previews | `tools/dd1_bmfont.py` |

### 6.2 Changes to what exists

1. `DungeonRun`: keep the party as it set out (guid, name, class id) in the run and its JSON. In `Finish`,
   before anything is applied: experience and level of each of them. Then, as now, stress, the retreat's
   price, `QuestBoard.Finished`, `BringHome`, `RaiseExpeditionEnded`; then `QuestAftermath` for the living;
   then read experience and level again and build `RaidResults`. Outcome: success -> Victory; else living
   heroes of the party at least 3 (read `min_hero_count_for_escape`) -> Escape; else Defeat. Then dispose, hide
   the corridor, **show the screen instead of the hamlet**; "Return to Town" sets `EstateSession.View =
   Hamlet`. The save is written before the screen shows.
2. `QuestBoard.Finished`: return the list of what was given (the trinket actually handed over, or the gold paid
   in its place); a second entry point lists the offer for a quest not completed (`Given` false).
3. `Core/Inventory.Settle`: besides the sums, return the kept stacks (item, kept amount, worth) in slot order;
   `RaidHaul` gains `Stacks`. Food is split into single rations by the screen (read `can_unpack` from
   `shared/inventory/item.display.json`), not by `Settle`.
4. `QuestBoard.Report`: the two lines about pay and bag are no longer queued for the hamlet (the screen says
   it; the activity log keeps its entries). A story quest's closing words and the retreat's dead stay.
5. `Dd2/EstateAudio` / `Dd1AudioEngine`: the two beds as a music state of the results screen; the one-shots by
   name.
6. Narration: the box is already able to stand over any canvas at 960, 990; the failure line triggered by
   `ExpeditionEnded` then shows over the results screen as in DD1.

### 6.3 Gold on the screen

The estate counts in DD2's relics, 1 for 50 of DD1's gold (`DungeonRun.GoldScale`). Keep DD1's units inside
the record and convert when writing: the reward card `Quest.Gold` (already estate gold); a treasure card's
count through `InventoryText.Amount`; a card's rising worth through `InventoryText.Price` (one decimal); the
total as `InventoryContent.ToEstateGold` of the running DD1 sum, so that the last figure is exactly what
`BringHome` paid (rounding card by card would drift).

### 6.4 Where the mod must differ from DD1

| what | the mod | why |
|---|---|---|
| portraits | DD2's portrait in the 85 px hole, as `RaidHeroPanel` does (drawn 106 px, clipped) | DD2's heroes |
| trinket reward card | DD2's trinket icon on DD1's rarity card, as on the quest map | DD2's trinkets |
| quirks listed | DD2's quirk names, positive in notable, negative and diseases in harmful | DD2's quirks |
| stress pips | one pip a point (0 .. 10) | as in the raid HUD |
| affliction label | empty | DD2 has no lasting affliction |
| a hero's path | not shown on this screen | no room in DD1's row; the name is enough here |
| gold figures | estate gold (6.3) | the estate's currency |
| trinkets found | DD1 shows none; the mod could list them as extra cards in the rewards row when the quest is completed | owner's call: DD1 parity says none |

### 6.5 Order of work

1. `RaidResults` filled and logged by `Finish` (no screen yet): outcome, rewards, stacks, per-hero before and
   after. Checked through the bridge.
2. `RaidResultsLayout` + `tools/preview_raid_results.py`: both pages offline, compared with the owner's two
   screenshots at the pixel positions of 1.3 and 1.4.
3. The screen without motion: both pages, the button's rule, the hamlet after it.
4. `ResolveBar`, then the reveal's steps and "finish now".
5. `QuestAftermath` and the masks.
6. The effects, the sounds, the music.
7. Acceptance against DD1: an Escape (the owner's screenshots), a Victory with a level gained and a quirk, a
   Defeat with dead heroes, an empty bag, a bag of 16 stacks (the squeezed row).


## 7. Not settled by the files or the code read

Checked: both pages were put together offline from the install's pictures at the positions of 1.2 to 1.4
(Escape with three rewards, five treasure cards, four heirloom stacks and totals; Victory with four heroes,
one dead, one with masks, one with three quirk lines). Every part falls where the art is painted for it: the
titles in the sun's disc, the blue plate and the band, the ribbon's words on the ribbon, the cards between
the rules and in the two fields, the pile and the totals on the header bands, name and experience on the
name line, portrait in its hole, masks and quirk lines in the plate, badge, bar and pips in the banner. Not
seen in DD1 itself by this research: the screenshots were described, not read.

Each open point is small; the first four can be settled by looking at DD1 (the owner's screenshots, or one
more of a Victory and of an opened mask).

1. **How a withheld reward card looks** (4.3). The code marks it; the tint is taken from DD1's general
   "unselectable item" style. Compare with the rewards row of the owner's Escape screenshot.
2. **The quirk lines' alignment and whether the masks stay** (1.4, 2.3). The lines' origin is x 960 and three
   lines fill the plate exactly, so they are taken as centred; the masks' effect ends at 1.5 s while the names
   finish fading in at 1.466 s, so the masks are taken to give way to the names. One opened mask in DD1 shows
   it.
3. **Anchor 7** as "middle of the bottom edge of the line cell" for the two titles (it puts both in their
   plates on all four panels; a baseline anchor would sit 15 px lower).
4. **The Victory panel standing 15 px left** of the titles' centre line (1.2): taken as DD1's own slip.
5. The order inside one card's reveal, the place of the loot effect on the card (taken: its middle), the
   rising worth's exact start and whether it fades, its text ("+N" taken).
6. How the experience bar moves through a level gained, and what the `_glow_` badge is for.
7. The rounding of the keep rates on a failed quest (the mod floors; 0.25 of 3 torches is 0).
8. Hero order on page 2 (taken: the party's ranks 1 to 4, top to bottom) and whether all four reveal together
   (the code gives each hero a sequence of its own, started together).
9. Whether DD1's quirk list on this page draws the "replaced" mark for a quirk pushed out.
10. `can_unpack` for types not named in `item.display.json` (taken: false; only food is named true).


## 8. Where this was read in the exe

`_windows/win32/Darkest.exe` (32-bit x86, image base 0x400000), disassembled with capstone from the functions
that reference the strings above (`raid_results_*`, the layout keys, the `/general/raid_screen/*` events). The
addresses are virtual addresses of this build, for a later look.

| address | what |
|---|---|
| 0xb1d1e0 | reads `raid_results.layout.darkest` (key, offset in the layout struct; the struct is at 0x1240fb8: screen layout, +0x60 items state, +0x1c4 heroes state, +0x1dc hero layout) |
| 0xb1ed70 | `LoadAssets`: region picture, the outcome's panel, title id and colour (4.2) |
| 0xb24f60 | `ShowBackgroundAndTitle` |
| 0xb2cf70 | the page's draw: titles, state origin, progression bar |
| 0xb26610 | `ShowRegularItemsState` (page 1); 0xb257d0 the Farmstead's |
| 0xb275d0 | `ShowHeroesState` (page 2) |
| 0xb28f00 | `InitCampaignAnimTweens` (the reveals) |
| 0xb2d1d0 | the button's words by state (0, 1: "Next"; 2: "Return to Town"; 9: the arena's) |
| 0xb2d310 | `ProgressForwardInternal` (1.6); 0xb2bb10 "is the page's reveal over"; 0xb2d6d0 `ProgressToTown` |
| 0xb2e830 | the rewards row's item state (given or withheld); 0xb2f5e0 the treasure filter; 0xb2f590 the heirloom filter |
| 0xb6c0e0 | the shared inventory grid (1.5); 0xb50d10 the shared cost widget (totals, centred); 0xb51800 which currency layout |
| 0x9b87c0 | the roster's raid-finish roll (4.7); 0x8abb90 reads `shared/rules.json` (`min_hero_count_for_escape` at 0x29390cc, the quirk chances at 0x2938dac .. 0x2938ddc) |
| 0xb42e40 | reads `shared/inventory/item.display.json` |
| 0x7c4740 | the music table with the three `raid_screen` beds |
