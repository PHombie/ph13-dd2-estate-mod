# UI parity audit, town side: the mod against DD1's own data

What this is: every town screen the mod has taken over from DD1, compared element by element with what DD1's
own files say (layout, art, fonts, colours, strings, Spine sheets), with a list of deviations to fix. Research
only: no code was changed, no game was run for it. The raid / dungeon side is audited separately
(`docs/recon/ui-parity-raid.md`).

Scope: hamlet screen and its chrome, roster column, every building window, upgrade pane, activity log, trinket
inventory, heirloom exchange, town event panel, estate map, provision screen, tooltips, narration, dialogs.
Left out on the owner's word: the Courtyard (Crimson Court), the Farmstead (Color of Madness), the Butcher's
Circus. DLC-only town UI is listed at the end as "DLC, ask".

## Snapshot and how to read this

- **The mod was a moving target.** The audit began on commit bde9559; while it ran, the main session committed
  38365e9, 387f701, a341427, 1775286 and 1cb401a (the estate bar's row, the name plate, the roster's head and
  rows). Sections 1 to 3 were re-read against commit 1cb401a (2026-10-05, 06:40); what those commits fixed is
  named there as fixed, not listed as open. All other sections describe files those commits did not touch. At
  that moment the working copy held uncommitted work on locked buildings (`Estate/Buildings.cs`,
  `HamletScene.cs`, `UpgradeWindow.cs`, a new `BuildingLocks.cs`: section 4, row HS03); it was not re-read, and
  `HamletScene.cs` line numbers in section 4 may be off by one because of it.
- **Paths.** DD1 paths are relative to `E:\Steam\steamapps\common\DarkestDungeon`; mod paths are relative to
  `src/DD2Estate/`. Numbers are pixels of DD1's 1920x1080 screen, y down. "art px" = pixels of a building's
  backdrop (`<id>.character_background.png`, 1395x776), which DD1 puts at `town_background_layout.area_pos`
  144 132; `B/` = `campaign/town/buildings/`.
- **Tables.** One row per deviation: `element | DD1 source (file: entry) and value | mod (file:line) and value |
  deviation | severity | confidence`. Severity: high = visibly wrong or missing to a player who knows DD1.
  "Documented as deliberate" = the existing notes (`docs/recon/dd1-ui.md`, `dd1-windows.md`,
  `dd1-town-panels.md`) already admit the difference; it is listed all the same. Things that match are named
  in one line under each table. Row ids (EM01, GU5 ...) exist only for cross reference inside a section.
- **"Needs eyes"** under a table = cannot be decided from files; someone has to look at DD1 (or the mod) running.
- **Evidence beyond the layout files**, cited where used:
  - *DD1 exe strings*: the text strings of DD1's executable (`_windows/win64/Darkest.exe`; a dump with addresses
    was at hand). Neighbouring strings belong to one source file, so they show which keys, art files, string
    ids, Spine animations and sound events DD1's code really uses for a screen. They prove that DD1 names a
    thing, not where it draws it.
  - *Spine*: the `fx/*.sprite.skel` skeletons read with the repo's own reader (`tools/preview_corridor.py`),
    which gives the drawn size and seat of every part and the animations' names and lengths.
  - *DD1 frames*: `_lab/shots/dd1_now.png` (a real DD1 hamlet, 1920x1080, week 1, taken on this machine on
    2026-10-05 by the main session; Russian text; Color of Madness on) and `_lab/shots/dd1_hover_scroll.png`.
    Pixels were measured on them. No DD1 frame of any other town screen was at hand.
  - *DD1's tutorial pictures* (`shared/tutorial_popup/*.png`): small, sheared and partly older than today's
    layout numbers; used for "what is there" and for ratios only.

## Top 25 to fix first

Ranked across all screens: what is on screen most often and most plainly wrong comes first, whole DD1 elements
the mod lacks next, then errors of one rule that repeat over many screens. "S n" = section n below.

1. **Estate bar, the currencies (S 2).** Measured on a real DD1 frame. The gold pile is DD1's animated Spine
   sprite `fx/estate_gold_pile` (94x86, sparkles at rest, a burst of rays and coins on spending) with its middle
   at `currency_pos` + `gold_pile_offset -65 -5` = 135, 995; the mod draws the still 88 px
   `currency.gold.large_icon.png` 109 px further right (`EstateSummary.cs:79`). DD1's gold count ends at
   x 290; the mod's begins there (`:82-87`). Every heirloom and count stands about 30 px too far right
   (`:88-104`), the exchange icon 29 px (`ActivityLogTownPanel.cs:376-384, 460`). No thousands separator.
2. **DD1's screen-name widget is read nowhere (S 5, 17, 18, 20, 21, 22).** `shared/name/name.layout.darkest`
   (`icon_offset 60 26`, `text_offset 192 118`, `sub_icon_offset 56 106`) puts a 113 px icon at every screen's
   `name_pos`, the name right of it and the "+" on the icon's lower left with a pulsing ring
   (`fx/building_upgrade_pulse`). The mod: no icon on any building window, the Activity Log (two icons), the
   Trinket Inventory or the town event panel; names centred between the rules instead; the "+" at its own two
   places without a ring (`RosterWindow.cs:42-47, 176-225`); on the Estate Map and the Provision screen icon and
   name sit about 60 px left of and 26 to 30 px above DD1's seats (`QuestPanel.cs:484-489`,
   `ProvisionScreen.cs:191-195`).
3. **Estate Map: the bottom bar is 16 px too high (S 21, EM01).** `QuestPanel.cs:426-427` draws
   `progression_bar.png` at y 942 instead of 958, so "Provision" hangs 11 px under the lit band; the map also
   covers the estate's gold, heirlooms and bar buttons.
4. **Provision screen: the button says "Embark" (S 22, PV01).** DD1's files have three words for the three
   forward buttons, each with its own sound: "Embark" (town), "Provision" (map), "Set Off"
   (`town_progression_forward_set_off`, `/ui/town/set_off_button`); the mod writes the town's word here
   (`ProvisionScreen.cs:233`). One look at DD1's provision screen confirms it before the one-line change.
5. **Glossary (S 2).** The red book is the second button of DD1's bar row (x 1690 on the frame): the mod leaves
   a gap there and has no Glossary screen (`campaign/town/glossary/`, 60 terms).
6. **Alerts on buildings and the name plates' anchor (S 4, HS01, HS02).** DD1's pulsing red markers
   (`fx/estate_exclamation`, animation `alert`) are not drawn; the rule measured on the DD1 frame is projected
   `pos3d` + (`alert_offset.x`, -`bbox_size.y`/2 + `alert_offset.y`). The mod's name plates add `bbox_offset`
   (`HamletScene.cs:310-315`), which that rule does not: up to 60 / 40 px off.
7. **Town dialogs are DD2's (S 26).** Dismissing a hero, selling a trinket and the town event's choice open
   DD2's confirmation dialog (`RosterLifecycle.cs:288`, `NomadWagonPanel.cs:260`, `RealmInventoryPanel.cs:429`,
   `TownEventPanel.cs:307`); DD1's dialog is already rebuilt for the raid (`Dungeon/RaidOverlays.cs:540-590`).
   DD1's own questions are also missing where the mod asks nothing: cancelling a treatment, "unequip all",
   the short-handed party on the map.
8. **Hero banner: the class illustration (S 10, HB1).** `heroes/<class>/<class>_guild_header.png` (715x630 at
   art px 662, 14) fills the upper right of Guild, Blacksmith and Survivalist once a hero is in the slot; the
   mod draws nothing (`UpgradeUi.cs:330-343`).
9. **Blacksmith: the hero's own gear (S 12, BS1).** DD1 shows the 72x144 cards
   `heroes/<class>/icons_equip/eqp_weapon_<n>.png` / `eqp_armour_<n>.png`; the mod shows the building's 72x72
   tree icons (`BlacksmithPanel.cs:127`), though `Dungeon/RaidHeroPanel.cs:254-287` already loads that art.
10. **Quick navigation strip (S 5).** Ten `<id>.town_button.png` buttons at 70, 230 every 68 px left of a
    building window (`building_navigation.layout.darkest`): not built.
11. **Upgrade pane (S 6).** Steps start 8 px after the tree icon instead of 40 px (`UpgradePane.cs:161`), so
    every step, link and price is 32 px too far left; `blg_townupgrade_costframe.png` round the buyable step is
    never drawn; heirloom icons in a price are 20 px for DD1's 40 (`UpgradeUi.cs:35`); the keeper is hidden
    while the pane is open (`RosterWindow.cs:219`) though DD1's pane is 75% black over him.
12. **Gold prices sit 12 to 14 px too low (S 11, 12, 13).** A DD1 cost offset is the price's vertical middle
    (`estate_currency_gold_layout.icon_offset 0 -12`); `UpgradeUi.BuildCosts` (`UpgradeUi.cs:175-195`) takes it
    as the top. In the Guild the price lies across the rule and the next row.
13. **Nomad Wagon: the mod's own "Trinket Inventory" block (S 14, NW1).** A shade, a title, cards with sell
    prices and arrows cover the still life painted into the backdrop (`NomadWagonPanel.cs:39-42, 109-119`);
    DD1 has the table only, and the mod now has the real Trinket Inventory to sell from.
14. **Estate Map: words (S 21, EM02, EM03, EM06).** The line over the party tray is the mod's sentences on a
    black strip instead of DD1's party name / "Build a Party From the Roster" with its fades
    (`QuestPanel.cs:536-539, 732-768`); generated quests carry the mod's names on the name band
    (`QuestBoard.cs:420-431`) and DD1's ("Scout", "Skirmish") only in an invented subtitle that pushes the
    description down 28 px.
15. **Town event: the bell (S 20).** DD1 brings the week's event back from a bell on the bar
    (`fx/estate_town_event`); the mod has an invented plate at the hamlet's top in sizes and a colour that are
    not DD1 styles (`TownEventPanel.cs:36-37, 141-165`).
16. **Locked buildings (S 4, HS03).** Never shown: the only caller passes `locked: false`
    (`UpgradeWindow.cs:111`); "Complete more quests to unlock" is never written. (Being worked on as this
    audit closed: an uncommitted `Estate/BuildingLocks.cs`.)
17. **Ancestor's Memoirs: an entry's structure (S 16, AM1).** DD1: one slab per boss with a "Part n" line, the
    part's title and a play or lock button per part; the mod: one slab per finished quest, the shut ones absent.
18. **Roster rows (S 3).** The rework landed while the audit ran (commit 1cb401a: experience bar, no caption,
    no health bar, plain slot). Still open: DD1's badge tooltip (level, experience, stress), the sort buttons'
    tooltips, the ink splat behind the row in hand, roster barks, the party made by clicking on the hamlet.
19. **Character sheet (S 25).** Right click opens DD2's sheet (`RosterPanel.cs:578`); DD1's sheet
    (`shared/character/`) with rename, recolour, previous / next hero is not rebuilt. A decision for the owner.
20. **Guild (S 11).** DD1 learns a skill on its padlocked picture; the mod adds a "Known" circle and leaves the
    picture dead (`GuildPanel.cs:176-195`); `guild/skill_frame.png` is drawn round every picture though DD1's
    exe never names it; the mark of the skills in use (`selected_ability.png`) is missing.
21. **Sanitarium (S 9).** The gold padlock `shared/character/lockquirk.png` and the skull
    `shared/character/seriousquirk.png` are never loaded (a grey padlock stands for both); a free week shows a
    coin and "0" instead of "Free" and the `free_event` art (`SanitariumPanel.cs:188, 313, 431-448`).
22. **Scroll bars (S 15, 16, 17, 18).** `Dd1Ui.ScrollList` (`UiKitWindows.cs:122-136`) draws the rail and the
    pip only: no `scrollbartop.png` / `scrollbarbottom.png`, arrows on some lists and not on others, arrows
    left standing when the rail hides.
23. **Trinket Inventory (S 18).** "Hold [SHIFT] to Sell Trinkets" is gold, DD1's colour id for it is neutral
    (`RealmInventoryPanel.cs:490-492`); the gear level number is in the wrong place and style (DD1:
    `hero_equipment_layout.level_offset 90 12`, `equipment_level`); `fx/trinket_sparkle` is missing; cards
    stand 4 px left (`inventory_item_layout.icon_offset 4 0` unread; same in the Wagon and the map's rewards).
24. **Sounds (S 27).** No pointer-over sound anywhere (`button_mouse_over*`), no "back" / "locked" / "invalid"
    click, silent bar buttons and panels (`page_open`, `trinket_open`, `heirloom_exchange_*`, `sort_by`,
    `character_equip` ...); controls made with a bare `Button` have no click sound at all.
25. **Estate Map: effects (S 21, EM08, EM10, EM13, EM07).** `dd_effect_<dungeon>.png` is painted for good
    where DD1 flickers it for a quarter second by chance (`QuestPanel.cs:333-337`); the level bar does not
    fill, glow or sound; the town event's sunburst behind the tray is missing; the difficulty word is not
    tinted.

Next in line (each is in its section): the bar buttons' "selected" look (ink splatter, flash, sparkles) and
the heirloom tooltips (S 2); the Stage Coach's empty text in the wrong style and the dead right click (S 7);
the Activity Log's entry insides and unused `separator.png` (S 17); the Graveyard entry's anchors and week
line (S 15); the Survivalist's own DD1 line (S 13); the banner's empty-slot string (S 10); the narration
band's fade (S 24).

## Findings that run through several screens

- **One widget, a dozen screens.** Item 2 above. Where the name's text stands beside the icon is in no file
  (needs eyes); that the icon belongs at `name_pos` + 60 26 is supported three ways (S 5, S 17, S 21).
- **A cost offset is the middle of a price** (item 12). `Estate/BuildingPanel.cs:289` and
  `Dungeon/ProvisionScreen.cs:158` already read the same kind of key as the middle; `UpgradeUi.BuildCosts`
  does not. The coin is 24 px in DD1 and 20 px in `UpgradeUi` (`CostIcon`).
- **A tree's steps start at the tree icon's right edge** (item 11): 102 + 70 k in the upgrade pane, 72 + 75 k
  in Guild and Blacksmith (the mod: 70 + 70 k and 75 + 75 k).
- **An inventory card stands 4 px inside its cell** (`shared/inventory/inventory.layout.darkest:
  inventory_item_layout.icon_offset 4 0`): applied by `Dungeon/InventoryGrid.cs` and the provision screen, not
  by the Wagon, the Trinket Inventory or the map's rewards.
- **Buttons at rest and under the pointer.** `Dd1Ui.ArtButton` dims every resting picture to 0.86
  (`UiKitWindows.cs:88-93`); `UiKit.Button` tints a lit one warm and darker (`UiKit.cs:253-254`). DD1 draws
  art as painted and brightens what is lit (`button_highlight` 1.5 1.5 1.3).
- **Dimmed things keep their colour.** DD1's "unselectable" colours also take the colour out (`saturation 0.2`
  or `0.0`: `inventory_unselectable`, `upgrade_tree_icon_not_purchased`, `town_statue_archive_locked_entry`,
  `hero_slot_dimmed`, `town_roster_portrait_unselectable`); the mod multiplies by a grey and keeps it.
- **Thousands.** DD1 writes "4,990" and "1,750"; the mod writes plain numbers everywhere.
- **Text on the windows' info line and invented tooltips.** DD1's building screens have no how-to or result
  sentences; the mod writes them on `info_text_offset` and in tooltips. Documented as deliberate; listed per
  screen.
- **DD2 art inside DD1 frames** (portraits, skill icons, trinket icons): deliberate and listed once per screen.

## Deviations by severity

Counted from the tables below, one row = one deviation. Rows that would have appeared in two sections are kept in one.

| section | high | medium | low | all |
|---|---|---|---|---|
| 1. Hamlet screen: name plate and Embark | 0 | 0 | 4 | 4 |
| 2. Estate summary bar: gold, heirlooms, the bar's buttons | 4 | 5 | 7 | 16 |
| 3. Roster column | 1 | 6 | 12 | 19 |
| 4. Hamlet scene: building name plates, hover, alerts | 2 | 1 | 3 | 6 |
| 5. Building window frame (shared by every building) | 2 | 7 | 5 | 14 |
| 6. Upgrade pane | 0 | 3 | 9 | 12 |
| 7. Stage Coach | 0 | 2 | 7 | 9 |
| 8. Tavern and Abbey | 0 | 3 | 9 | 12 |
| 9. Sanitarium | 0 | 5 | 8 | 13 |
| 10. Hero banner (Guild, Blacksmith, Survivalist) | 1 | 4 | 5 | 10 |
| 11. Guild | 0 | 7 | 7 | 14 |
| 12. Blacksmith | 1 | 2 | 5 | 8 |
| 13. Survivalist | 0 | 3 | 4 | 7 |
| 14. Nomad Wagon | 1 | 3 | 6 | 10 |
| 15. Graveyard | 0 | 4 | 7 | 11 |
| 16. Ancestor's Memoirs (statue) | 1 | 2 | 6 | 9 |
| 17. Activity Log | 2 | 4 | 12 | 18 |
| 18. Trinket Inventory (realm inventory) | 1 | 9 | 15 | 25 |
| 19. Heirloom exchange | 0 | 2 | 4 | 6 |
| 20. Town event panel | 2 | 2 | 8 | 12 |
| 21. Estate Map (quest select / embark) | 3 | 13 | 14 | 30 |
| 22. Provision screen | 1 | 7 | 10 | 18 |
| 23. Tooltips (the box every screen shares) | 0 | 0 | 2 | 2 |
| 24. Narration (the Ancestor's subtitle band) | 0 | 1 | 4 | 5 |
| 25. Hero / character sheet | 1 | 0 | 0 | 1 |
| 26. Confirmation dialogs on the town side | 1 | 1 | 1 | 3 |
| 27. Hover feedback and UI sounds (all town screens) | 0 | 3 | 0 | 3 |
| **all** | **24** | **99** | **174** | **297** |

## 1. Hamlet screen: name plate and Embark

Mod: `src/DD2Estate/Estate/HamletScreen.cs`. DD1: `campaign/town/town.layout.darkest` (`town_estate_title_layout`), `campaign/town/estate_title/*.png`, `shared/progression/progression.layout.darkest` + art, `campaign/town/town.anim.darkest`, `colours/base.colours.darkest`.

Fixed while this audit ran (commit a341427), listed so nobody looks for them again: the "Week n" on the plate and the red back arrows with their "Return to Menu" hint are gone; the plate carries the estate's name alone, as on the DD1 frame.

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| Estate's name | `estate_title_format` "The %s Estate" filled with the estate's own name (`estate_title_default_data` "Darkest" is only the default) | `HamletScreen.cs:112`: always the default | The estate cannot be named. Documented | low | high |
| Embark under the pointer: tint | `colours/base.colours.darkest:1 button_highlight .hvec4 1.5 1.5 1.3 1.0` (a lit button is brightened); `progression_forward_selected_overlay.png` 311x24 at `forward_selected_overlay_offset 0 -13` | `UI/UiKit.cs:253-254`: every `UiKit.Button` multiplies its art by (1, 0.9, 0.7) under the pointer and (0.8, 0.6, 0.4) when pressed; `HamletScreen.cs:146-150` adds DD1's overlay | The overlay is DD1's; the tint goes the wrong way (darker and warmer where DD1 brightens). Same for every `UiKit.Button`; `Dd1Ui.ArtButton` (`UI/UiKitWindows.cs:88-93`) instead dims the resting picture to 0.86 | low | medium |
| Embark: sounds | DD1 exe strings: `/ui/town/button_mouse_over_embark` (pointer over the forward button), `/ui/town/embark_button` (click) | `UiKit.cs:260` plays `ui/town/button_click` on the click, `Dd2/EstateAudio.cs:450` plays `ui/town/embark_button` when the map shows; nothing on hover | No hover sound; two click sounds where DD1 has one | low | medium |
| Mode plates | `estate_title/estate_nameplate_ng.png`, `estate_nameplate_rm.png` (893x281), `radient_icon.png` (64x64) for DD1's Stygian and Radiant modes | `HamletScreen.cs:32`: always `estate_nameplate.png` | Not applicable while the estate has no such modes (see "Base-game modes, ask") | low | high |

Matches DD1 (checked against the files and the DD1 frame `_lab/shots/dd1_now.png`): plate art and `pos 0 0`; the name alone on the band at `text_offset 286 70` in `town_estate_title` (#5d5a50; the frame's name starts at x 286); forward button art at `forward_pos 801 984`, label `town_progression_forward_embark` in `town_progression_forward` with `forward_text_offset 160 -2` as top centre; overlay offset `0 -13`.

Needs eyes:
- What `town_estate_title_navigation_unselectable` (#666) and `str_town_title` "Hamlet" are used for (probably the title as the "back to the Hamlet" label of the estate map and the provision screen).

## 2. Estate summary bar: gold, heirlooms, the bar's buttons

Mod: `Estate/EstateSummary.cs` (currencies), `Estate/ActivityLogTownPanel.cs` (`TownPanelButtons`), registrations in `ActivityLogTownPanel.cs:312-325` (candles), `RealmInventory.cs:59-63`, `ActivityLog.cs:140-144`, `HeirloomExchange.cs:29-33`. DD1: `campaign/town/estate_summary/estate_summary.layout.darkest`, `shared/estate/estate.layout.darkest`, `fx/estate_*` (Spine 2.1 sheets), `colours/base.colours.darkest`.

Two kinds of evidence beyond the files:
- **Spine**: sizes of the `fx/estate_*` parts are the drawn sizes in the skeletons' own poses, read with `tools/preview_corridor.py`'s reader (root at 0,0).
- **DD1 frame**: `_lab/shots/dd1_now.png`, a real DD1 hamlet at 1920x1080 taken on this machine on 2026-10-05 (Russian text, Color of Madness on, so its layout is the DLC copy with `currency_pos 180 42` and `heirloom_exchange_offset 755 51`; everything else is the base file). Pixels were measured on it; "base" numbers below are the same rule with the base file's `currency_pos 200 42` / `700 51`. `_lab/shots/dd1_hover_scroll.png` is the same screen with the pointer on the scroll.

Fixed while this audit ran (commits 38365e9, 387f701): the row's order and places (candles 1800, chest 1580, scroll 1470, each centred on its place), the chest's size (0.65), the candles as the options button, resting buttons in full colour.

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| Gold pile: art | `fx/estate_gold_pile/estate_gold_pile.sprite.skel` (DD1 exe strings: loaded with the bar's other sprites). Region `gold` 126x115 drawn at 0.75 = 94x86 centred on the root. Animation `idle` 1.633 s loop: six `sparkle0n` (14 px) twinkle over the pile. Animation `spend` 0.8 s: `primary_rays` 93 to 134 px and `secondary_rays` 60 to 94 px flare behind and five `coin0n` fly up 50..110 px (exe: `spend` beside `/ui/town/buy`, `buy_free`) | `EstateSummary.cs:33, 79`: the still picture `shared/estate/currency.gold.large_icon.png` 88x88 | Other art (a still 88 px icon for an animated 94x86 pile), no sparkle at rest, no burst when gold is spent. The mod has a Spine reader (`Dd1/SpineSkeleton.cs`, used by `HamletScene`) | high | high |
| Gold pile: place | `estate_summary_layout.gold_pile_offset -65 -5` from `currency_pos`: the pile's middle. DD1 frame: bright part of the pile x 75..154, y 954..1035, middle 115, 995 = (180 - 65, 958 + 42 - 5). Base: middle 135, 995, art x 88..182 | `EstateSummary.cs:79`: icon corner at `currency_pos` + `estate_large_currency_layout.icon_offset 0 -50` = 200, 950, so x 200..288; `gold_pile_offset` is not read | The pile stands 109 px too far right (its middle at 244 for 135). Height agrees | high | high |
| Gold count: place and alignment | `estate_large_currency_layout.number_offset 90 -16`. DD1 frame: "4,990" spans x 183..265, capitals y 993..1027; its right end is `currency_pos.x` + 90 (270 on the frame, 290 base): the count ends where the mod's begins | `EstateSummary.cs:82-87`: top left corner at 290, 984, left aligned, running right towards the heirlooms and shrinking to 28 px before it reaches them | The count grows the wrong way: DD1's four figures are at about 205..288 (base), the mod's at 290..372. With the pile it makes the whole gold block sit about 100 px right of DD1's | high | high (frame); see needs eyes for the exact rule |
| Gold count: thousands | DD1 frame: "4,990" | `EstateSummary.cs:145`: `EstateState.Gold.ToString()`: "1320" | No thousands separator | low | medium (the frame is DD1 in Russian) |
| Heirloom icons and counts: x | `large_currency_spacing 200 0`, `currency_spacing 74 0`, `estate_currency_heirloom_layout.icon_offset 0 -10`, `number_offset 38 -4`. DD1 frame (art's bright box inside its 40 px icon taken into account): icon boxes start at x 349, 423, 497, 571; counts start at 389, 463, 537, 610. That is `currency_pos.x` + 169..171 + 74 n for the icon and + 209 + 74 n for the count; base: icons from 369..371, counts from 409 | `EstateSummary.cs:88-104`: icon at `currency_pos.x` + 200 + 74 n = 400, 474, 548, 622; count at 438, 512, 586, 660 | Every heirloom and its count stand 29 to 31 px too far right. Heights agree (icons y 990..1030, digits y 1002..1016 on the frame) | medium | high (measured), low (the rule behind the 29: see needs eyes) |
| Heirloom tooltips | `localization: town_estate_bar_tooltip_bust` "Busts: %d (Used to upgrade town buildings.)", `_portrait`, `_deed`, `_crest` (exe: `town_estate_bar_tooltip_%s`), at `estate_summary_layout.tooltip_offset 0 -48` | `EstateSummary.cs:97-103`: icon and count, no tooltip | Missing tooltips | medium | high |
| Click on an heirloom | The exchange has its own button; no file says the heirloom icons are buttons | `EstateSummary.cs:96-99`: a click on an heirloom's icon opens the exchange with it picked | Possibly invented (the code's comment says DD1 does this; not in a file) | low | low |
| Heirloom exchange button: place | `heirloom_exchange_offset 700 51` (DLC 755 51). DD1 frame: the icon's lit arrows span x 745..763, y 997..1021; in `he_icon_idle.png` (58x59) they span 19..37, 17..42: the icon is at 726..784, 980..1039, its middle at 755, 1009.5 = the offset. `fx/estate_heirloom_exchange` is 58x59 centred on its root | `ActivityLogTownPanel.cs:360, 376, 383-384, 460`: x is the icon's left edge ("the exchange keeps DD1's left edge"), y its middle line | 29 px too far right (base: DD1 671..729, mod 700..758). The row buttons were moved to "middle" on the same evidence; this one was left | medium | high |
| Heirloom exchange button: motion and sound | `fx/estate_heirloom_exchange`: `idle_loop` (the `idle` picture), `selected` 0.533 s (the icon spins), `selected_loop` (the `selected` picture), `unselected` 0.533 s (it spins back); exe: `/ui/town/heirloom_exchange_open`, `_close` | `HeirloomExchange.cs:31`: `he_icon_idle.png` and `he_icon_selected.png` swapped at once; `ActivityLogTownPanel.cs:416`: resting tint 0.88 | No spin between the two pictures, no sound, and the resting icon is 12% darker than the art (the row buttons were set to full colour, this one not) | low | high |
| Glossary button and screen | Second place of the row (DD1 frame: the red book at x 1690). `fx/estate_glossary` (idle: `book_closed` 100x108 drawn 85x92; `selected` 1.0 s: `book_open` drawn 113x94 + six sparkles). Screen: `campaign/town/glossary/glossary.layout.darkest` (`pos 144 132`, `name_pos -46 -8`, `list_position 680 140`, `list_area_size 640 580`, `quote_position 1000 50`, `quote_width 780`, `character_position -50 100`, `scrollbar_offset 20`, `entry_spacing 10`, `close_position 1352 12`, `term_textbox_width 190`), `glossary_background.png` 1395x776, `glossary_character.png` 811x757, `separator.png` 584x7, `glossary.icon.png` 113x113; strings `town_name_glossary`, `str_glossary_ancestor_quote`, `str_glossary_term_1..60` (+1002) with `str_glossary_term_definition_*` | none: `ActivityLogTownPanel.cs:312-313` names the book in a comment, places 0, 2 and 3 are filled, place 1 is empty | A whole DD1 town screen is missing, and its place in the row is a visible gap between the candles and the chest | high | high |
| The button of an open screen | Animation `selected`: an ink `splatter` behind the icon (log: 162x163 at 0.75 = 122x122, its middle 20 px left and 6 px above the root; chest: 147x145 at 0.65 = 96x94, 33 px left, 12 px up) and the open picture; the chest also has `flash` (pulsing 52 to 62 px), `gold` 48x17, `glow` 51x19 and six `sparkle0n` over a 1.0 s loop. `_lab/shots/dd1_hover_scroll.png`: under the pointer the scroll stays shut and only the rule overlay shows, so `selected` is not the hover state | `ActivityLogTownPanel.cs:462-478`: the open picture while the screen is open, nothing else | No splatter, no flash, gold, glow or sparkles | medium | high (missing parts), medium (that `selected` means "open") |
| Open and shut pictures: seat | `fx/estate_activity_log`: `scroll_closed` is centred on the root, `scroll_open` (drawn 102x104) has its middle 4 px below it; `fx/estate_realm_inventory`: `chest_closed` has its middle 2 px below the root, `chest_open` (drawn 89x95) 5.5 px above and 2.5 px right of it | `RealmInventory.cs:61`, `ActivityLog.cs:142` give no offset; `ActivityLogTownPanel.cs:468-478` centres the open picture on the same point as the shut one | Open scroll 4 px too high; shut chest 2 px too high, open chest 5.5 px too low | low | high |
| Candles: light | `fx/estate_settings` `idle_loop` 1.067 s: `candles` drawn 62x71, `wax` and three `wax_drip0n`, a 55 px `glow` round the flame, flames changing by the animation; `selected` 0.2 s then `selected_loop` 0.933 s: ink `splatter` (drawn 96x94) behind, glow 73 px, taller flames | `ActivityLogTownPanel.cs:314-325`: `candles`, `wax` and the four flame pictures cycled at 9 per second at half size | No glow round the flame, no drips, no "selected" look; the flame is a fixed four-picture loop | low | high |
| Tooltip place of a button | `estate_summary_layout.navigation_button_tooltip_offset 0 -28`, `tooltip_offset 0 -48` | `ActivityLogTownPanel.cs:453-455`: the box stands on the icon's top edge less 2 px, its left edge at the place's middle; neither key is read | Hard-coded place | low | medium |
| "Why not" in a button's tooltip, dimmed buttons | none in DD1's files | `ActivityLogTownPanel.cs:451-455, 419, 480`: a red reason line; buttons at half brightness while no panel may open (the candles too) | Invented state and text | low | medium |
| Bar buttons: sounds | DD1 exe strings beside the bar's sprites: `/ui/town/trinket_open`, `/ui/town/trinket_close`, `/ui/town/heirloom_exchange_open`, `_close`; elsewhere `/ui/town/page_open`, `page_close` | `ActivityLogTownPanel.cs:411-427`: a bare `Button`, no sound (only `UiKit.Button` plays `ui/town/button_click`) | All bar buttons are silent | medium | medium |

Matches DD1 (checked against the files and the frame): bar art `shared/progression/progression_bar.png` at `estate_summary_pos 0 975` + `pos_offset 0 -17`; the gold count's and the heirlooms' heights and styles (`town_large_currency_amount`, `town_currency_amount`; the count's colour is notable on the frame too); step `currency_spacing 74 0`; order bust, portrait, deed, crest; the row's places 1800 / 1580 / 1470 by 1003 as the sprites' middles, sizes 0.65 / 0.65 / 0.75, full colour at rest; the rule overlay `estate_summary.selected_overlay.png` under the pointer (seen on `dd1_hover_scroll.png`).

Needs eyes:
- The rule behind the heirlooms' 29 px and the gold count's place. Two readings fit the one frame: (a) a fixed shift (count right aligned at +90; heirlooms 29 px left of the layout's sum); (b) each currency's icon-plus-count block is centred on its place (heirloom: 38 + 20 px of "10" = 58, half is 29; gold: 88 + 2 + 82 px of "4,990" = 172, half 86, which also puts the count at 184..266). They differ once a count has one or three figures: a second DD1 frame with such counts decides.
- Where the town event's bell stands in the row and when it shows (the bell itself: see the Town event panel section).
- Whether a tooltip with the screen's name shows over a bar button (none on `dd1_hover_scroll.png`, which may have been taken before it appeared) and where.
- Whether the rule overlay also shows for the exchange button; whether heirloom icons are clickable.
- Whether the gold count is written with a separator in English too.

## 3. Roster column

Mod: `Estate/RosterPanel.cs` (rows, header, sort, scroll), `RosterRowClick` in the same file. **The file was rewritten while this audit ran**: line numbers are those of commit 1cb401a (2026-10-05, 06:40). DD1: `campaign/town/roster/roster.layout.darkest`, `roster.layout.anim.darkest`, `shared/resolve_level_bar/resolve_level_bar.layout.darkest`, art in `campaign/town/roster/`, `campaign/town/hero_slot/`, `shared/hero/`, `overlays/`. Pictures of DD1's rows: `shared/tutorial_popup/tutorial_popup.resolve_level.png` (shipped with DD1), the DD1 frame `_lab/shots/dd1_now.png`, and the side-by-side `_lab/shots/roster_dd1_vs_mod.png`.

Fixed while this audit ran (commits 1775286 and 1cb401a), listed so nobody looks for them again; they were read in the code, not seen in game:
- the header shows the count alone at `roster_message_offset 60 78` (the word "Roster" is gone); the frame has "4/12" at x 1611;
- the sort buttons stand in DD1's order level, stress, class, activity (`RosterPanel.cs:75`);
- the experience bar hangs under the badge (`resolve_level.bar_pos 12 28`, `gradient_offset 10 16`, `gradient_size 16 40`, `resolve_level_bar_mask.png`; `RosterPanel.cs:536-543`);
- the health bar and the class / path caption are gone from the row;
- the gold slot frame of a party member (`hero_slot.backgroundhightlight.png`) is hidden under an opaque portrait square, as on the frame (`RosterPanel.cs:511-514`).

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| Tooltip of a row | DD1's roster tooltip is the resolve badge's: `town_roster_element_layout.resolve_level_bar_tooltip_offset -170 4`; `resolve_level_tooltip.hot_spot_size 100 100`, `text_width 200`; strings `resolve_bar_tooltip_level_line_format` "Level: %d", `..._xp_line_format` "Resolve XP: %d/%d", `..._additional_stress_format` "Stress: %d/%d", `..._affliction_format`; colours `resolve_tooltip_level` / `_xp` / `_additional` (neutral), `_affliction` (harmful) | `RosterPanel.cs:59, 345-349`: a tooltip for the whole row at -306, 6 with class and path as its heading and the reason a hero cannot embark | DD1's level, experience and stress lines are missing; the tooltip that is there is the mod's own, at the mod's own place | medium | high |
| Experience bar: fill | `resolve_level_bar.gradient_size 16 40` filled with a gradient from `resolve_gradient_bottom` (#6d6a5e) to `resolve_gradient_top` (#aea996) | `RosterPanel.cs:539-540`: one flat colour, the middle of the two | Flat fill for a gradient | low | medium |
| Count when the barracks are full | style `roster_full` (notable); string `str_roster_list_full` "Full" beside the format `%d/%d` in DD1's `panel.town.roster_list.cpp` (exe strings) | `RosterPanel.cs:324-325`: always "n/m", in harmful red when full | "Full" is never written; the red is the mod's | low | medium |
| Dismiss control on a row | `shared/character/character.layout.darkest: character_layout.dismiss_hero_icon_pos 32 78` (`icon_dismiss.png` on the character sheet) | `RosterPanel.cs:552-553`: the icon at 330, 8 of a row under the pointer | Invented place (the mod has no DD1 character sheet). Documented | medium | high |
| Sort buttons: tooltips | `roster_sort_tooltip_offset 16 -8`; `str_sort_roster_by_building` "Sort by Activity", `_class`, `_level`, `_stress` | `RosterPanel.cs:206-215`: no tooltip | Missing tooltips | medium | high |
| Sort: sound and motion | exe: `/ui/town/sort_by`; `town_roster_list_layout.sort_max_x_travel 100.0`, `sort_time 1.0` (rows travel to their new places) | `RosterPanel.cs:211, 286-297, 473-483`: silent, rows rebuilt at once | No sound, no sort animation | low | medium |
| Ink splat behind the row in hand | `campaign/town/roster/selected_charslide.png` 284x292 at `town_roster_element_layout.character_slide_splat_offset -80 -70` (file used by DD1's exe) | `RosterPanel.cs:263-272`: the row slides, nothing behind it | Missing art | medium | medium (when it shows: needs eyes) |
| Slide easing, pick-up pulse | `roster.layout.anim.darkest: town_roster_mouseover_element_anim .easing_function "easeInQuad"`; `roster_element_animations .selection_target_scale 0.2 .selection_transition_time 0.15` | `RosterPanel.cs:264-270`: linear; no pulse | Linear where DD1 eases; no pulse | low | high |
| Barks from the roster | exe: `TownUI::Panel::RosterList::RandomBark`, `str_roster_list_low_stress_bark`, `_medium_`, `_high_`, `str_roster_list_darkest_dungeon_survivor_bark` (lines in `dialogue.string_table.xml`); balloon art `scrolls/bark_balloon_side.png` | none | Heroes of the roster never speak | medium | high (that DD1 has it), low (look) |
| Roster as a drop target | `campaign/town/roster/roster_live.png` 312x42 at `roster_live_top_offset 35 90` and `roster_live_bottom_offset 35 -30` (file used by DD1's exe); `roster_list_anim.focus_bars_anim_time 0.3` | none | Missing glow bars at the list's head and foot (most likely while a recruit is dragged over the roster) | low | low |
| Portrait of a hero who cannot be picked | `colours/base.colours.darkest:100 town_roster_portrait_unselectable .darkness 0.4 .saturation 0.2` | `RosterPanel.cs:353-359`: DD2's black-and-white portrait at 0.55 | Other numbers (0.55 and no colour for 0.4 and a fifth of the colour) | low | medium |
| Marker for "blocked for another reason" | `hero_slot.negative_frame.png` is a hero slot's "cannot go here" frame | `RosterPanel.cs:369-373`: laid over the roster portrait | Invented use. Documented | low | high |
| Town event on a hero | `campaign/town/roster/town_event.icon_roster.png` 29x26, `shared/hero/roster_icon_frame_town_event.png` 85x85 (both used by the exe) | none | Missing marker for heroes the week's event touches | low | medium |
| Stress beyond the first lap, affliction | `overlays/stress_pip_full_overstressed.png`; `resolve_level_number.afflicted_background_offset -10 -15` with `resolve_level_bar_number_afflicted_background.png` 77x77; style `town_roster_affliction` (white); `stress_halo_position 55 50`, `stress_halo_extra_size 10 10` | `RosterPanel.cs:382-392`: ten pips from DD2's 0..10 stress, one look | No high-stress or affliction look on a row (DD2 has a meltdown state to hang it on) | low | medium |
| Darkest Dungeon veteran | `completed_darkest_dungeon_quest_icon_offset 220 15`, `shared/hero/icon_completed_darkest_dungeon_quest.png` 42x48 | none | Missing icon | low | high |
| Building and "missing" icon: tooltip | `building_icon_tooltip_offset 0 0`, `non_building_icon_tooltip_offset 0 0` | the reason is in the row's own tooltip instead | DD1 hangs it on the icon | low | medium |
| Left click on a row | DD1 moves heroes by dragging (exe: `hero_drag_object.cpp`, `/ui/town/character_pickup`, `character_add`, `character_add_full`); the party is made on the estate map's tray | `RosterPanel.cs:574-582`: left click puts a hero in or out of the party on the hamlet itself | Other gesture, and a party is made on a screen that has no party tray | medium | medium |
| Right click on a row | DD1's character sheet | `RosterPanel.cs:576-579`: DD2's sheet | See the Hero / character sheet section | high | high |
| Wheel and arrows: sound | exe: `/ui/shared/button_scroll_up`, `button_scroll_down` | `RosterPanel.cs:226-234, 275-284`: silent | No scroll sound | low | medium |

Matches DD1 (checked against the files and the frame): column at `roster_list_pos 1550 0`; `roster_bggrad.png`; rows `element_pos 0 132`, `element_spacing 0 97`, `element_max 8`; frames at `top_frame_offset 20 -50` and `bottom_frame_offset 20 -10`; `portrait_icon_offset 21 9`, `building_icon_offset 20 10`, `non_building_icon_offset 14 10`, `name_offset 116 4` (`town_roster_name`), `stress_offset 116 43` + `stress_spacing 10 0`, `weapon_level_offset 156 65`, `armour_level_offset 228 65` (`town_roster_number`), `resolve_level_bar_offset 258 4` with `background_offset -3 0` and `number_offset 30 31` (`resolve_number`), `rosterelement_res1..6.png`; sort buttons at `roster_sort_start_position 148 80` + `roster_sort_spacing 6 0` and the overlay offsets `-8 -8` / `-8 24`; slide 30 px in 0.2 s; the party seal. The hero names on the frame look heavier than the mod draws them because that DD1 runs in Russian (`fonts.darkest: font_language_override`); not a deviation.

Needs eyes:
- The header when the barracks are full: the count, "Full", or both.
- Where DD1 hangs the two scroll arrows (`scroll_up_button_offset 0 -38`, `scroll_down_button_offset 0 -12`: the mod takes x from the left edge of the column, `RosterPanel.cs:219-220`); the frame has four heroes and no arrows.
- What a third click on a sort button does (DD1 has `sort_roster_label_none` "Custom").
- When the ink splat and `hero_slot.backgroundhightlight.png` show in DD1 (under the pointer, while dragged).
- When the glow art of the badge (`resolve_level_bar_number_background_glow_lvl<n>.png`) shows.
- The look and the timing of roster barks.
- `new_info_icon_offset 0 0`: what the "new info" icon is.

## 4. Hamlet scene: building name plates, hover, alerts

Mod: `Estate/HamletScene.cs`, `Estate/HamletBuildingHit.cs`. DD1: `campaign/town/town.layout.darkest` (the per-building blocks, `building_text_layout`), `campaign/town/town.anim.darkest`, `campaign/town/buildings/blg_name_background.png`, `fx/estate_exclamation`, `fx/town_<id>_*`. The DD1 frame `_lab/shots/dd1_now.png` was measured for HS01 and HS02.

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| HS01 alert marker on a building | `campaign/town/town.layout.darkest`: `<building>_layout.alert_offset` (tavern `-70 50`, abbey `-120 -40`, graveyard `0 -20`, statue `0 0`, ...); `fx/estate_exclamation` (animation "alert", 1.0 s loop: exclamation 24x60 swelling to 26x66, glow 74x77 to 84x87). Measured on `_lab/shots/dd1_now.png`: the marker's centre is the projected `pos3d` + (`alert_offset.x`, -`bbox_size.y`/2 + `alert_offset.y`): tavern 480,746; abbey 948,422; graveyard 960,549; statue 940,774. `bbox_offset` is not part of it | none (`Estate/HamletScene.cs:308-309` only mentions it) | Missing: week 1 of DD1 shows four pulsing red markers. Documented (`dd1-windows.md` §11). The measured rule is the one to build it by | high | high |
| HS02 name plate's reference point | `<building>_layout.text_offset` + `building_text_layout` (`background_offset -70 -70`, `name_offset 0 0`, `description_offset 0 50`); by the measurement of HS01 the point both offsets count from is the projected `pos3d` + (0, -`bbox_size.y`/2) | `HamletScene.cs:310-315`: counts from the hover box's centre with `bbox_offset` added: (`origin.x` + `bbox_offset.x`, `origin.y` - `bbox_offset.y` - `bbox_size.y`/2) | If the name uses the alert's point, each plate is off by (`bbox_offset.x`, -`bbox_offset.y`): blacksmith +60 / -40, camping_trainer -50 / +40, statue +40 / -35, stage_coach +35 / 0, abbey +30 / -30, graveyard +30 / -3, tavern 0 / -30, guild 0 / -20, sanitarium +20 / +20, nomad_wagon +10 / -10 px | medium | medium (measured for the alert, inferred for the name) |
| HS03 locked buildings | `fx/town_<id>_locked` (tavern, abbey, blacksmith, guild, sanitarium, nomad_wagon, camping_trainer); string `str_locked_building_summary` "Complete more quests to unlock" (DD1 exe strings list it beside `str_%s_summary` and `fx/town_%s_locked/...`); `/ui/town/button_click_locked`; `dd1_now.png` shows the ruined blacksmith, guild and sanitarium of week 1 | `HamletScene.cs:319-331` can draw the locked variant, but its only caller `Estate/UpgradeWindow.cs:111` always passes `locked: false`; `HamletScene.cs:434-437`, `449` always write `str_<id>_summary` | No locked look, never "Complete more quests to unlock": every building stands open from the first week | high | high |
| HS05 name plate fade | `building_name_fade_anim`: 0.3 s, `easing_function "easeInQuad"` | `HamletScene.cs:480-481`: linear over 0.3 s; the easing is not read | Linear instead of easeInQuad | low | high |
| HS06 hover and click area | `<building>_layout.bbox_offset` / `bbox_size` (tavern `0 30` / `346 316`, blacksmith `60 40` / `700 300`, sanitarium `20 -20` / `250 500`, ...) | `Estate/HamletBuildingHit.cs:18-25`: the painted pixels of the idle sprite | The area follows the silhouette, not DD1's boxes | low | medium |
| HS07 building animations and hover sound | each building skeleton has the animations "idle" and "active" (tavern 13.3 s: `light_door`, `light_first_floor`, `light_second_floor` breathe, `smoke01` / `smoke02` rise and fade; nomad wagon 8.7 s `light`; abbey 15 s `light_middle`); hover plays "active" (the same plus the silhouette); DD1 exe strings `/ui/town/button_mouse_over_town` | `HamletScene.cs:334-396`: the setup pose drawn once (lights still, both smoke puffs frozen); `HamletScene.cs:446`, `455` switch the "active" picture; no hover sound in `src` | Lights and smoke do not move; hovering is silent. (Scene art, on the edge of this audit's scope) | low | high |

The zoom into a building and the fade between town screens (`town.anim.darkest: building_zoom_anim`, `screen_fade_anim`) are listed once, in the Building window frame section.

Matches DD1 (checked, no row): `building_text_layout.background_offset -70 -70`, `name_offset 0 0`, `description_offset 0 50`
are read and applied; `campaign/town/buildings/blg_name_background.png` (208x224); styles `town_screen_building_name`
(dwarven_axe_large, notable) and `town_screen_building_description` (ubuntu_medium, neutral); strings `town_name_<id>` and
`str_<id>_summary`; fade time 0.3 s; the projection of `pos3d` (the four measured alerts fall on the projected x to the
pixel); the "active" silhouette behind "idle".

Needs eyes:
- HS02: hover each building in DD1 and compare the name's place; and whether the two lines are centred on the text point
  (mod) or start there. The blot's centre lies 35 px right of and 33 px below that point, which speaks for text that starts
  at the point rather than being centred on it.
- What the plate says over a locked building (name plus "Complete more quests to unlock", or the line alone).
- When DD1 raises an alert (its exe keeps a "novelty tracker") and on which buildings; week 1 shows tavern, abbey,
  graveyard and statue, not the stage coach.
- Does the hover silhouette fade in; does DD1 take the pointer by `bbox` or by the picture.
- The zoom and blur when a building is entered.
- Where `str_town_title` "Hamlet" is written.

## 5. Building window frame (shared by every building)

**Two readings that several rows of sections 5 and 6 rest on (how they were checked)**
1. **DD1's screen-name widget is `shared/name/name.layout.darkest`** (`name_layout`), placed at each screen's `name_pos`. The mod reads this file nowhere. Proof that it is the widget of the building screens: DD1 exe strings list `shared/name/name.layout.darkest` and `shared/name/name.anim.darkest` directly beside `shared/progression/less_info_icon.png`, `info_icon_frame.png`, `more_info_icon.png`; `name_pos` 104 126 + `icon_offset` 60 26 = screen 164,152 = art px 20,20, which is the notch of the upgrade pane art (pane at art px 10,12, notch inside about 11..121) to within 1-2 px; `sub_icon_pulse_offset` equals `sub_icon_offset` and the Spine sheet for it (`fx/building_upgrade_pulse`) carries a slot named `more_info_icon`.
2. **A tree's steps start at the right edge of the tree's icon, not at the row's origin.** `upgrade_requirement_layout.base_size` 102 72 is a step's cell; `background_connector_offset` 0 24 starts the 50 px link at the cell's left edge; with the cell at icon_offset.x + 72 the link runs from the tree icon's right edge to the first step, and at pitch 70 from each step's backing to the next. The tutorial picture `stage_coach2` gives the same: gap between tree icon and first step icon / step pitch = 36 / 62 = 0.58 (file reading: 40 / 70 = 0.57; mod: 8 / 70 = 0.11).

Mod: `Estate/RosterWindow.cs`, `Estate/HamletShade.cs`, `Estate/Buildings.cs`, `UI/UiKitWindows.cs`.

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| Building icon beside the screen name | `shared/name/name.layout.darkest: name_layout.icon_offset` 60 26 from `campaign/town/buildings/building.layout.darkest: building_base_layout.name_pos` 104 126 = screen 164,152 = art px 20,20; art `campaign/town/buildings/<id>/<id>.icon.png` 113x113 (DD1 exe strings: `campaign/town/buildings/%s/%s.icon.png`). Shown whenever the screen is open | `RosterWindow.cs:176` draws the name only. The icon exists only inside the open upgrade pane: `UpgradePane.cs:31,127` at art px 19,22 | The 113 px building icon at the window's top left is missing in the normal state of every building window (1 px left and 2 px low when the pane shows it) | high | high |
| Quick navigation strip | `campaign/town/town.layout.darkest: town_screen_layout.button_navigation_pos` 70 230, `button_navigation_offscreen_pos` -40 230; `campaign/town/building_navigation/building_navigation.layout.darkest`: `button_spacing` 0 68, `button_index` 0..9 = stage_coach, blacksmith, guild, camping_trainer, tavern, abbey, sanitarium, nomad_wagon, graveyard, statue; `selected_hop_offset` 0 -5; `exclamation_point_offset` 28 32; `locked_overlay_offset` 14 14 with `bld_quick_nav_locked_icon.png` 60x60; buttons `<id>/<id>.town_button.png` 56x56; colour `town_navigation_button_unselected` (darkness 0.8, saturation 0.2); `building_animation.quick_nav_transition_time` 0.5 | none | A column of ten building buttons in the 144 px margin left of the backdrop (y 230..898), sliding in, the open building's button hopped up 5 px and the others dimmed: not built. Documented as not built (dd1-windows.md section 1) | high | high for the data, medium that it shows with a mouse |
| Screen name: place and alignment | `name_layout.text_offset` 192 118 from `name_pos` = screen 296,244 = art px 152,112; style `town_name` (dwarven_axe_large, notable). x 152 is 19 px right of the icon's right edge (133), so it is the text's left end; y 112 is the top of the lower rule painted in every backdrop (rules at art y 30..41 and 112..122), so it is the text's bottom | `RosterWindow.cs:42-43,176`: centred on art px 256,76 in a 296x64 box (capitals at art y 58..92), shrinking when wider | Name centred between the rules instead of starting right of the icon. Left edge in the mod vs 152 in DD1: Guild 219, Abbey 211, Tavern 204, Graveyard 177, Sanitarium / Blacksmith / Survivalist 175, Stage Coach 168, Nomad Wagon 153. Also 5 px high if 112 is the line cell's bottom (capitals 63..97), 20 px high if it is the baseline | medium | high for x, low for y |
| "+" (upgrades toggle): place | `name_layout.sub_icon_offset` 56 106 from `name_pos` = screen 160,232 = art px 16,100; art `shared/progression/info_icon_frame.png` 59x59 with `more_info_icon.png` / `less_info_icon.png` 39x39. `sub_icon_pulse_offset` has the same value and the pulse's ring is centred on its Spine origin, so 16,100 is most likely the "+"'s centre: on the building icon's left edge, in its lower third. One place, open or closed | `RosterWindow.cs:46-47,198,225`: top left at art px 40,46 while closed (centre 69.5,75.5), moved to 94,97 while open (centre 123.5,126.5, the icon's lower right corner) | Wrong place in both states and it jumps when toggled. The docs call the place "the mod's: DD1's files have none"; the file is `shared/name/name.layout.darkest` | medium | medium (centre vs top-left anchor, see Needs eyes) |
| "+" pulse | `fx/building_upgrade_pulse/building_upgrade_pulse.sprite.*`: regions `circle_glow` 73x73, `bar_glow` 130x40; slots background, more_info_icon, bar_glow, circle_glow; animations `pulse` (1.07 s: ring 0.9 -> 1.1, alpha 0.38 -> 0.5) and `notify` (1.5 s: the ring plus a bar flash about 150x40); placed at `name_layout.sub_icon_pulse_offset` 56 106; `shared/name/name.anim.darkest: name_pulse_anim` pulse_scale 0.3, seconds_time 0.2, easeInOutQuad. DD1 exe strings: `notify`, `pulse`, `fx/building_upgrade_pulse/...skel` beside `building_verbose_%s` | none (`RosterWindow.cs:196-215`) | The glowing ring around the "+" is not drawn | medium | high that it exists, low on when it plays |
| Keeper while the upgrade pane is open | `campaign/town/buildings/blgupgradebg.png` 662x764: its body is black at alpha 191 of 255 (75%), drawn over the keeper; `tutorial_popup.stage_coach2.png` shows the keeper's face through the pane | `RosterWindow.cs:219` `_keeper.SetActive(!open)` | Keeper removed while the pane is open; in DD1 he stays, dimmed through the pane | medium | medium |
| Open and close | `building.layout.darkest: building_animation` transition_time 0.3, transition_out_time 0.3, base_scale 0.4; `campaign/town/town.anim.darkest: building_zoom_anim` seconds_time 0.7, easeOutSine, z_amount 700, blur_start_fraction 0.8; `screen_fade_anim` 0.5 easeInQuad; `shared/app.darkest: g_PanelTransitionTunables` 0.2 / 0.2, base_scale 0.5, easeOutSine / easeInSine | `RosterWindow.cs:122-193,232-240`: built and destroyed in one frame | No scale-in / scale-out of the window, no zoom of the town towards the building. Documented as not played | medium | high |
| Town behind the window | `town.anim.darkest: building_zoom_anim` (zoomed by z 700, blurred from 80% of the zoom on). No key darkens the town | `HamletShade.cs:17` flat black at 72% over the whole screen above the bar; `RosterWindow.cs:157-160` | Flat dark wash on a sharp, unzoomed town instead of a zoomed, blurred one. Documented as the mod's own | medium | medium |
| Text on the window's info line | `building.layout.darkest: building_base_body_layout.info_text_offset` 580 760, style `town_building_info` (ubuntu_small, #5d5a50). The only DD1 string found for this slot is `provision_sell_back_info` (provision screen). No how-to, result or feedback strings for buildings exist in the string tables | `RosterWindow.cs:183,245-268` and each panel: how-to sentences (`BuildingPanel.cs:48`, `SanitariumPanel.cs:65`), "Last week: ..." (`BuildingPanel.cs:294-298`), click feedback in neutral / harmful colour, two lines, shrinking to size 13 | Text DD1 does not have, in colours the style does not have. Position and resting style are DD1's. Documented as deliberate (dd1-windows.md section 0) | medium | medium |
| Name while the pane is open | The name widget does not change; the pane art is clear above its y 120 right of the notch, so name and rules stay as they are | `RosterWindow.cs:220` hides the name; `UpgradePane.cs:32,129-130` writes it again centred in a box at art px 140..426 x 41..111 (centre 283,76) | The name is redrawn 27 px further right than the mod's own closed name (and not at DD1's left edge 152) | low | medium |
| Alert on the name | `name_layout.alert_offset` 115 90 = art px 75,84; `fx/estate_exclamation` (exclamation 24x60, glow 74x77, animation `alert` 1 s). DD1 exe strings: `fx/estate_exclamation/...skel` beside `building_upgrade` and `novelty_tracker.json` | none | Not drawn | low | low (when it shows is in code) |
| "+" on a building without trees | `localization/heroes.string_table.xml: building_verbose_graveyard` "The Graveyard cannot be upgraded." (DD1 has the pane's text for it) | `RosterWindow.cs:186-187`: no pane and no "+" when `UpgradeRules.TreesOf` is empty | No "+" and no pane in the Graveyard | low | medium |
| Close button and "+": the mod's own behaviour | `building_base_layout.close_pos` 1496 144, `shared/progression/progression_close.png` 32x32. No tooltip string for either control in the string tables | `RosterWindow.cs:180-181` tooltip "Back to the hamlet (or right click)"; `UiKitWindows.cs:89` resting tint 0.86; `RosterWindow.cs:205-206` tooltip on "+" (`building_verbose_<id>`, "Back to the ..."); `RosterWindow.cs:402-414` a click on the darkened town closes | Invented tooltips, a dimmed resting state and click-outside-closes. Art and place of the close button are DD1's | low | medium |
| Keeper after the Darkest Dungeon | `<id>/<id>.dd.character.png` (same size as `<id>.character.png`: the keeper overgrown with flesh); DD1 exe strings `%s/%s/%s.dd.character.png`, beside `town_bg_post_dd_1..3.png` | never loaded | DD1's late-game variant of the keeper is not used | low | medium (what it is), low (when it shows) |

Matches DD1 (checked): backdrop art and place (`town_background_layout.area_pos`), keeper art and place (`character_pos` 132 240, cut by the bar), close button art and place, info line position (`body_base_pos` 596 102 + `info_text_offset`) and resting style, window ending at the estate bar (`estate_summary_pos` + `pos_offset`), right click leaving the building, the three `shared/progression` info icons as art.

Needs eyes
- Vertical anchor of the name: line cell bottom (capitals at art y 63..97) or baseline (78..112) at art y 112.
- Anchor of the "+": centre at art px 16,100 (sticks 8 px out of the backdrop's left edge) or top-left there (then it hangs under the icon's lower left corner at 16..75 x 100..159). Whether it changes place when the pane opens.
- When `pulse` / `notify` play (an affordable step? a new one?) and when the exclamation shows on the name.
- Whether DD1 also darkens the town behind a building screen, and whether the estate's name plate stays visible.
- Quick navigation: whether 70,230 is a button's corner or centre; that it shows with a mouse; its tooltip (`quick_nav_title_offset` / `quick_nav_desc_offset` are 0 0).
- Whether DD1 writes anything on the info line of a building screen.
- When `<id>.dd.character.png` replaces the keeper (a flicker after Darkest Dungeon quests, by the look of the neighbouring strings).
- Whether DD1's "+" appears in the Graveyard and the Memoirs.

Cross reference: `ActivityLogPanel.cs:45`, `RealmInventoryPanel.cs:59` and `TownEventPanel.cs` reuse the same "name centred at name_pos + (296, 82)" assumption; `QuestPanel.cs:484` and `ProvisionScreen.cs:191` put their 113 px icon at `name_pos` itself, without `name_layout.icon_offset` 60 26.

## 6. Upgrade pane

Mod: `Estate/UpgradePane.cs`, `Estate/UpgradeUi.cs`, `Estate/UpgradeText.cs`, `Estate/UpgradeWindow.cs`.

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| Where a tree's steps start | `building.layout.darkest: building_base_upgrade_tree_layout` icon_offset 30 0 (tree icon 72x72, right edge at 102), requirement_start_offset 0 0, requirement_spacing 70 0; `buildings/upgrade/upgrade.layout.darkest: upgrade_requirement_layout` base_size 102 72, icon_offset 40 10, background_offset 30 0, background_connector_offset 0 24. Step k's cell starts at 102 + 70k: its icon at 142 + 70k, 40 px right of the tree icon. Tutorial picture: gap / pitch = 0.58 | `UpgradePane.cs:161` `corner = at + stepSpacing * (k + 1)`: cell at 70 + 70k, icon at 110 + 70k, 8 px right of the tree icon; `icon_offset.x` and `requirement_start_offset` are not used for the steps | Every step, its backing, price and tooltip stand 32 px too far left; the gold link between the tree icon and the first bought step is hidden under the tree icon instead of bridging a 33 px gap | medium | medium |
| Frame around the step that can be bought next | `campaign/town/buildings/blg_townupgrade_costframe.png` 103x139 (dark plate with a bar at top and bottom) at `upgrade_requirement_layout.outline_offset` 13 -12 from the step's cell: it holds the step icon and its price. DD1 exe strings load the file; the tutorial picture shows it on the next step of both visible trees | not loaded anywhere in `src` | The plate that frames the buyable step and its price is missing | medium | medium |
| Heirloom icons in a price | `shared/estate/estate.layout.darkest: estate_currency_heirloom_layout` icon_offset 0 -10, number_offset 38 -4, next_currency_spacing 2 0; art `shared/estate/currency.<id>.icon.png` 40x40 (drawing about 30x35). The number starts 38 px after the icon's corner, so the icon is drawn at its own 40 px | `UpgradeUi.cs:35` `CostIcon = 20f` ("half of DD1's 40 px art"), `:184` | Heirloom icons under a step at half size (drawing about 15x18 instead of 30x35) | medium | medium |
| Price row under a step: offsets | From `upgrade_requirement_layout.cost_offset` 62 72: icon top at 62 (2 px under the step icon), number cell top at 68, next currency 2 px after the number | `UpgradeUi.cs:184-190`: icon top at 74, number cell top at 72, 2 px between icon and number, 5 px between currencies | Number 4 px lower, icons 12 px lower at their top edge, wider gap between the two currencies | low | medium |
| Step tooltip: heading | Tree name alone ("Stagecoach Network" in `tutorial_popup.stage_coach2.png`); colour `upgrade_tree_tooltip_title` | `UpgradePane.cs:383-384`: tree name + Roman numeral ("Stagecoach Network II") | Numeral added | low | medium |
| Step tooltip: last line | Body is the effect sentence (`upgrade_tree_tooltip_description_*_format`), then "Prerequisites:" where something is missing | `UpgradePane.cs:331-359`: "Click to build." / "Built." / "Price: ... Build the step before it first." / "Not enough crests." | Invented status line | low | medium |
| Prerequisites in a tooltip | `upgrade_prerequisite_tooltip_title` "Prerequisites:" then one `upgrade_prerequisite_requirement_tooltip_body_format` "%s Level %d" per need, colour `upgrade_tree_prerequisite_not_purchased` (harmful) | `UpgradePane.cs:354`, `UpgradeText.cs:91-103`: one line "Prerequisites: Instructor Mastery II and ..." | Roman numerals and a joined sentence instead of "<tree> Level <n>" lines | low | medium |
| Tooltip of a locked step | `upgrade.layout.darkest: upgrade_locked_upgrade_requirement_tooltip_layout` tooltip_offset 110 0, tooltip_text_width 250, tooltip_is_auto_width 1 | `UpgradePane.cs:117-118,384`: `building_upgrade_requirement_tooltip_layout` (130 0, 300) for every step | Locked steps use the open step's offset and width | low | medium |
| Tooltip width | `building_upgrade_requirement_tooltip_layout.tooltip_is_auto_width` 0, tooltip_text_width 300: a fixed width | `UiKitWindows.cs:291-294`: every tooltip shrinks to its words | Narrow boxes for short texts where DD1 keeps 300 | low | medium |
| Effect sentences | `upgrade_tree_tooltip_description_reduces_treatment_cost_format` "Reduces treatment cost by %d%%", `..._reduces_positive_quirk_treatment_cost_format`, `..._reduces_negative_quirk_treatment_cost_format`, `..._reduces_disease_quirk_treatment_cost_format`, `..._increases_stress_recovery` (DD1 exe strings list them with the activity and sanitarium code) | `UpgradeText.cs:160-201` has no case for the cost steps of Tavern, Abbey and Sanitarium: `:203-227` writes "Price: 25 gold, was 30", "Remove a bad quirk: ... gold, was ..."; `:136` appends "(x to y, was ...)" to "Increases stress recovery" | The mod's wording and numbers where DD1 has a sentence | low | medium |
| Hero Paths | DD1's Stage Coach has three trees, one a row (`upgrade_trees_spacing` 0 160) | `UpgradePane.cs:35-39,180-197`: a fourth tree shares row 3, starting 300 px in | The mod's own tree in a shared row. Documented as deliberate | low | high |
| Price of a step that waits for another tree | Colours `town_currency_amount` / `town_currency_cant_afford_amount` only | `UpgradePane.cs:304-306`: price in the mod's dim colour, icons at 55% | Invented third state | low | low |

Matches DD1 (checked): pane art and place (`upgrade_base_pos` 172 259 + `frame_offset` -18 -115 = art px 10,12, foot on the backdrop's foot), verbose text place / width / style / string, "Upgraded:" and the percentage (places, styles, left aligned inside the banner drawn in the pane), rows at `upgrade_trees_offset` 0 195 every 0 160, tree title offset and style, tree icon offset and files (`<tree>.icon.png` 72x72), tree icon tooltip offset 30 106 and DD1's description, the three step icons, backing and link relative to a step, highlight overlay, step pitch 70, divider, price centred on `cost_offset.x`, red for what the estate lacks, "Free" (the mod reads `cost_offset` for it instead of `free_offset`; both are 62 72), price under the next step only (the tutorial picture shows no price under locked steps). `upgrades/` at the install root: no building step costs more than two kinds of heirloom, so a DD1 row of two 40 px icons (about 98 px) fits the 100 px of `estate_cost_hot_spot_layout`.

Needs eyes
- Step start: 102 + 70k (icon's right edge, used above) or 100 + 70k (icon_offset + spacing); 2 px apart.
- Heirloom icon size under a step (file reading: 40 px; the old tutorial picture has smaller drawings on the same 40 px grid).
- Whether the cost frame stands on every tree's next step all the time (tutorial picture: yes, on two trees at once) or only under the pointer.
- Tooltip place relative to the step (the picture, with older numbers, has it about 10 px right of the step icon).
- What a next step looks like whose prerequisite in another tree is missing (icon, price, frame).
- What a purchase shows besides the new icon (DD1 exe strings: sound `/town/building_upgrade_%s`).

Cross reference: the same step-start reading gives 72 + 75k in the Guild and the Blacksmith (icon_offset 0, spacing 75); the mod's 75 + 75k is 3 px off there.

## 7. Stage Coach

Mod: `Estate/StageCoachPanel.cs`, `Estate/StageCoach.cs`.

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| "The Stage Coach is empty." | `fonts/fonts.darkest: stagecoach_empty_info_text` = ubuntu_medium; `colours/base.colours.darkest: stagecoach_empty_info_text` = neutral (174 172 162); at `stage_coach.layout.darkest: stagecoach_layout.empty_info_text_offset` 575 375, width 600. DD1 exe strings: `stagecoach_empty_info_text` beside `stagecoach_prisoner_rescued` | `StageCoachPanel.cs:78`: style `town_building_info` (ubuntu_small, #5d5a50) | Smaller font and a much darker colour than DD1's. Place, width and string are DD1's | medium | high |
| Right click on a recruit | `tutorial_popup_hero_panel_description`: right click on any hero's portrait opens the hero's panel | `StageCoachPanel.cs:132-139`: the plate's `HeroDrag` has no `RightClicked`; the click is swallowed (it does not reach the backdrop either) | Nothing happens; a recruit cannot be inspected before hiring. Documented ("show character preview not built") | medium | medium |
| More recruits than fit | `stagecoach_layout.scroll_bar_offset` 0, `scroll_bar_y_size_offset` 40; DD1 exe strings also read `scroll_bar_row_threshold` | `StageCoachPanel.cs:104-106`: rows moved closer together | No scroll bar; rows overlap their 100 px pitch when a town event brings more than seven. Documented as deliberate | low | high |
| Recruits' barks | `localization: str_stagecoach_idle` (16 lines, dialogue table); DD1 exe strings `StageCoachBarkEvent`, `str_stagecoach_idle` | none | Speech bubbles of the waiting recruits are missing | low | medium |
| Full barracks | `str_stagecoach_roster_full_rejection` "Your barracks are full! Upgrade the Stagecoach or dismiss a hero." (dialogue table) | `StageCoach.cs:421` "The barracks are full", in the tooltip and on the info line (`StageCoachPanel.cs:144,155`) | The mod's wording in the mod's places | low | medium |
| Hiring and its tooltip | Drag to the roster; `str_hero_slot_unlocked_stagecoach_tt` "Drag Hero to Roster to recruit" | `StageCoachPanel.cs:134,144-146`: a click on the row hires too; tooltip + ", or click." and, for a seasoned recruit, "Resolve level N: arrives trained and equipped." | Click to hire and the added tooltip lines are the mod's. Documented as deliberate | low | high |
| Plate under the pointer, blocked recruit | `stage_coach.hero_background.png` 600x101 at `hero_layout.hero_background_offset` -100 -8; DD1's colour for a dimmed slot is `hero_slot_dimmed` (darkness 0.4, saturation 0.2) | `StageCoachPanel.cs:125,129,143,149`: plate tinted 0.88 at rest, white under the pointer, 0.55 when blocked, portrait grey | Invented rest / hover / blocked tints | low | low |
| Class line, portrait | `hero_layout.hero_description_offset` 100 42 (the class); DD1's own 85 px portraits | `StageCoachPanel.cs:125,128`: "Class, Path"; DD2's portrait at 98 px cut inside the slot | Path beside the class, DD2 portrait. Documented as deliberate | low | high |
| Find a hero by name | `stage_coach.layout.darkest: add_hero_dialog_contents` text_box_size 350 200, character_limit 32; styles `stagecoach_add_hero_dialog_heading`, `stagecoach_add_hero_dialog_edit_box`; strings `str_type_in_hero_name`, `str_hero_found`, `str_could_not_find_hero`, `str_hero_already_recruited` | none | DD1's name-entry dialog (and whatever opens it) is not built | low | medium |

Matches DD1 (checked): recruits at `body_base_pos` + `hero_recruit_store.store_item_pos` 450 80 every 0 100, plate art and offset, resolve badge art / offset -63 12 / number centred at -30 43 in `resolve_number`, slot art, name at 100 12 and description at 100 42, empty text place / width / string, DD1's tooltip string, the three tree icons (`stage_coach.numrecruits / rostersize / upgraded_recruits.icon.png`). Seven plates fit (`number_of_recruits_upgrades` ends at 7).

Needs eyes
- `hero_layout.hero_divider_offset` -150 -10: which art DD1 draws there, if any (the mod draws nothing).
- Text styles of a recruit's name and class (the mod uses `town_roster_name` and `town_character_class`; DD1's choice is in code).
- Where DD1 shows the recruit tooltip, and how a recruit looks that cannot be hired.
- How often and where the `str_stagecoach_idle` bubbles appear.

## 8. Tavern and Abbey

Mod: `Estate/BuildingPanel.cs`, `Estate/ActivityText.cs`, `Estate/UpgradeUi.cs` (slot, price).

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| The Caretaker in a slot | `campaign/town/hero_slot/caretaker_portrait.png` 85x85 (DD1 exe strings: `campaign/town/hero_slot/%s_portrait.png`); tooltip `str_cant_place_hero_here_caretaker` "The Caretaker is currently enjoying this activity..."; barks `str_caretaker_<activity>`; he sits in slot 1 of `tutorial_popup.stress_relief.png` | none | Not built. Documented ("DD1's caretaker never takes a slot") | medium | high |
| Slots while a hero is dragged | `hero_slot.positive_frame.png` (gold), `hero_slot.negative_frame.png` (red), `hero_slot.locked_for_hero.png` (red frame with a cross), each 85x85; all three are loaded by DD1 (exe strings) | `BuildingPanel.cs:177-181,257`: slots do not change while dragging; only `hero_slot.backgroundhightlight.png` for a slot picked by click | No "may go here / may not / never for this hero" marking on the slots | medium | medium (the files are used; which state shows which is from their names) |
| Taking a hero out | `str_hero_slot_locked_cancel_confirm` "You will not be refunded the cost, but the hero will be immediately available for use on quests. Are you sure?" with `..._yes` / `..._no`; tooltip `str_hero_slot_locked_<activity>` "Cancel Treatment" | `BuildingPanel.cs:200,411-421`: no question, the price is returned; tooltip + ": the price comes back until the week ends." | No confirm dialog, refund instead of loss. Documented as deliberate | medium | high |
| The Crier in a slot | `campaign/town/hero_slot/crier_portrait.png` 85x85 (loaded by name in DD1 exe strings); `str_cant_place_hero_here_crier`; barks `str_crier_<activity>` | none | Not built | low | medium |
| Coin in a slot's price | `estate.layout.darkest: estate_currency_gold_layout` icon_offset 0 -12, number_offset 25 -14; `shared/estate/currency.gold.icon.png` 24x24 (number starts 25 px after the icon's corner: native size) | `UpgradeUi.cs:35,184,198-203`: 20x20 | Coin 17% smaller (the Sanitarium's price uses the 24 px coin: `UiKitWindows.cs:217`) | low | medium |
| Activity description | `town_activity_description_<id>` alone at `description_offset` 170 90 | `BuildingPanel.cs:234`: DD1's line, then "Stress -2 to -3" in gold | Added line. Documented as deliberate | low | high |
| Hero held for another week | no string or art found | `BuildingPanel.cs:202,275`: "won't leave" in red under the slot | Invented label | low | low |
| Putting a hero in by click | Drag only (`str_empty_hero_slot_<activity>` "Drag a hero here.") | `BuildingPanel.cs:302-319,357-394`: click a slot (it lights up), then a roster row; prompts on the info line | The mod's second way in. Documented as deliberate | low | high |
| Check mark when the estate cannot pay | Price in `town_currency_cant_afford_amount` | `BuildingPanel.cs:272`: also the check mark at 35% | Invented dimmed state | low | low |
| A hero's words on being committed | `str_<activity>_committed` (7 lines each for bar, gambling, brothel, meditation, prayer, flagellation, treatment, disease_treatment); DD1 exe strings `DoHeroCommittedBark`, `str_%s_committed` | none (also not in the Sanitarium) | No speech bubble after the check mark | low | medium |
| A hero leaving a slot | `campaign/town/hero_slot/hero_slot.layout.darkest: replace_slide_out_anim` time 0.2, offset 0 -50, fade 0.5, easeInOutSine | none | The portrait vanishes instead of sliding up and fading | low | medium |
| Slot portraits after the Darkest Dungeon | `campaign/town/hero_slot/dd_portrait_1.png`, `dd_portrait_2.png` 85x85 (flesh); DD1 exe strings `.*dd_portrait_[0-9]*\.png` | never loaded | Not used | low | low |

Matches DD1 (checked): rows at `body_base_pos` + `building_activity_list_layout.base_pos` 70 50 every 0 230; activity name at 170 36 (top-left anchor: drawn so, it sits between the gold rules painted in the backdrop) in `town_activity_name`; description at 170 90, width 250; slots at 440 119 every 135 0 (they land in the painted shelves); three slots per activity (`slot_upgrades` 1, 2, 3); hero's name at `town_hero_slot_layout.name_offset` 45 -70 in `hero_slot_name`; `<id>.<activity>.hero_slot_overlay.png` at -26 -70; `<id>.locked_hero_slot_overlay.png` and the `free_event` / `locked_event` art at -42 -70; price read as vertically centred on `cost_offset` 38 -20 (the gold layout's icon_offset 0 -12 centres the 24 px coin on it) and "Free" as top anchored at `free_offset` 42 0 (the Sanitarium's `shared_cost_pos` 570 / `shared_free_pos` 554 pair confirms both); check mark as top-centre at 42 115 and cross as top-left at 7 112 (both centre on the slot); DD1's tooltip strings and width 200. `hero_slot.positive_next.png` / `positive_prev.png` (24x74, `next_offset` 76 4, `prev_offset` -15 4) are not used here; DD1 exe strings tie them to `town_building_action_hero_previous` / `..._next`, the hero banner of the Guild, Blacksmith and Survivalist.

Needs eyes
- Whether the activity's overlay art (mug and bottle and so on) appears as soon as a hero stands in a slot or only after the check mark (the mod: after).
- What `confirm_button_tooltip_offset` 0 38 counts from, and where the empty slot's tooltip stands.
- Which of positive_frame / negative_frame / locked_for_hero shows when.
- When the Crier takes a slot, and when the `dd_portrait` faces appear.
- How DD1 shows a hero who stays another week.

## 9. Sanitarium

Mod: `Estate/SanitariumPanel.cs`.

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| Padlock on the good quirk chosen for locking | `tutorial_popup.locking_pos_quirks.png`: a gold padlock in a gold frame left of the highlighted "Tough" = `shared/character/lockquirk.png` 32x32 (DD1 exe strings load it together with `sanitarium/lockedquirk.png`) at `sanitarium.layout.darkest: quirk_treatment.positive_icon_offset` -37 -2 | `SanitariumPanel.cs:188,433`: `sanitarium/lockedquirk.png` (grey padlock in a grey frame) for both the chosen quirk and one already locked | The chosen quirk carries the grey padlock; DD1's two padlock files are not told apart and `shared/character/lockquirk.png` is never loaded | medium | medium |
| Mark of a locked-in ("severe") bad quirk | `tutorial_popup.permanent_neg_quirks.png`: red skull in a red frame right of the name = `shared/character/seriousquirk.png` 32x32 (loaded with the sanitarium art in DD1 exe strings) at `negative_icon_offset` 5 -2 | `SanitariumPanel.cs:431-434`: the grey padlock at 60% | Padlock instead of DD1's skull | medium | medium |
| A week in which treatment is free | `sanitarium_activity_layout.shared_free_pos` 62 554: "Free" (`town_free`) in the frame over the check mark; `sanitarium.treatment.free_event.png` / `sanitarium.disease_treatment.free_event.png` 171x179 (two nurses beside the cell) at `building_activity_slot_layout.free_overlay_offset` -42 -70 | `SanitariumPanel.cs:313,441-448`: coin and "0"; the panel has no event art | Neither "Free" nor the nurses (the Tavern and Abbey panel has both) | medium | high |
| Prices beside a ward | `town_activity_description_<ward>` alone; DD1 names a price only in the frame over the check mark | `SanitariumPanel.cs:205-212,246-254,358-359`: coin, amount and "Remove a bad quirk" / "Lock in a good quirk" / "Cure a disease", then "33% to cure every other one too" | The mod's own lines under each description. Documented as deliberate | medium | high |
| Taking a hero out of a cell | as in the Tavern: `str_hero_slot_locked_cancel_confirm`, no refund | `SanitariumPanel.cs:236,496-502`: no question, refund; `:376` the cross is hidden while another hero is being admitted | No confirm dialog, refund. Documented as deliberate | medium | high |
| Tooltip of a quirk | One line with the effect only ("+10% MAX HP" in `tutorial_popup.locking_pos_quirks.png`); colour `town_choice_activity_choice_tooltip` | `SanitariumPanel.cs:640`: the quirk's name as a heading, then the text | Added heading | low | medium |
| Thousands in a price | "1,750" in `tutorial_popup.locking_pos_quirks.png` | `SanitariumPanel.cs:447` and `UpgradeUi.cs:186`: plain `ToString()` ("1750") | No separator (also over the Tavern's and Abbey's slots) | low | medium |
| Colours of chosen and unavailable entries | `sanitarium_treatment_disease_entry_selected` 173 201 98 (rest 121 141 69); `sanitarium_treatment_positive_entry_locked` and `sanitarium_treatment_max_locked` darkness 0.4, saturation 0.2; `sanitarium_treatment_disease_entry_locked` harmful | `SanitariumPanel.cs:477-478`: one colour per list; what cannot be treated is that colour mixed 45% with black | A chosen disease does not brighten; unavailable entries are darkened differently | low | medium |
| Gold cross | `sanitarium/remove_quirk_positive.png` 32x32 (loaded by DD1, exe strings) | never loaded; `remove_quirk_negative.png` marks chosen bad quirks and diseases (`SanitariumPanel.cs:189,433`) | One DD1 state has no art in the mod. (`removequirk.png` is not referenced by DD1's executable; the mod is right not to use it) | low | low |
| Clickable area of an entry | `sanitarium_activity_slot_layout.choice_hot_spot_size` 120 20 | `SanitariumPanel.cs:484-485`: the highlight's 221x29 | Larger hot spot | low | medium |
| Entry under the pointer, mark of a locked quirk | Highlight art `posquirk_highlight.png` / `negquirk_highlight.png` / `disease_highlight.png` 221x29 for the chosen entry | `SanitariumPanel.cs:428-434`: highlight at 55% under the pointer, 100% when chosen; icons at 60% unless chosen | Invented hover and half-lit states | low | low |
| What a hero in a cell is in for | nothing under a cell | `SanitariumPanel.cs:231,379`: "Remove: Known Cheat" under the cell | The mod's line. Documented | low | high |
| A single thing to treat | The player picks | `SanitariumPanel.cs:553-555`: chosen for the player | Invented shortcut | low | low |

Matches DD1 (checked): ward rows as in the Tavern with `sanitarium_activity_layout.name_offset` 170 42; `quirkheader.png` / `diseaseheader.png` 450x51 and `quirk_treatment_backdrop.png` / `disease_treatment_backdrop.png` 420x250; "Quirks" / "Diseases" in dwarven_axe_medium, notable (the mod names `town_activity_name` with the colour of `sanitarium_treatment_header`: same font and colour); title 5 px under the header's top (= `title_centre_position` 475 against header 470); highlight offsets -50 / -170 / -50; icon offsets -37 -2 / 5 -2 / -37 -2; list colours (notable, harmful, 121 141 69); entries in ubuntu_small every 28 px (`choice_spacing`); price and check mark at `shared_cost_pos` 62 570 / `shared_confirm_pos` 62 588: the check mark (art y 727..759) and the price (695..721) fall into the box painted in the backdrop (art y 690..765); tooltip string, offset 0 46, width 350; "Lock %s" / "Remove %s" strings at -115 156 / 130 156; cell overlays, bars, hero names as in the Tavern. `sanitarium.cost / .disease_quirk_cost / .slots.icon.png` are the tree icons of the upgrade pane.

Needs eyes
- **Which cells stand directly above the "Quirks" header.** In `tutorial_popup.locking_pos_quirks.png` the Treatment Ward's cells (door overlays, two hero names, a barred third cell) are about 20-30 px above the header. In the mod the header is at art y 490, 260 px below the Treatment Ward's doors, with the whole Medical Ward row in between. Either DD1 hides or moves the other ward while a treatment is chosen, or the picture predates the present layout.
- **Where the two lists stand sideways.** The file does not say what `positive_list_position` -230 400 and `negative_list_position` 330 400 count from. The mod counts them from the first cell: good quirks start at art x 732, bad ones end at 1292, both outside the header (777..1227) and the backdrop (812..1232). In the picture both lists are inside the header's width. The picture's own numbers cannot be today's (its spans do not fit -230 / +330 under any one origin), so only a look at DD1 settles it.
- `quirk_treatment.entry_y_spacing` 10 is not read by the mod (it uses `choice_spacing` 28); the picture's pitch is about 31 px, between the two readings.
- Colour of "Lock <quirk>" / "Remove <quirk>" (the mod: `town_choice_activity_choice`; DD1 also has `town_choice_activity_selected_choice` 255 0 0) and on which side of the mask each stands.
- Which padlock is which (`shared/character/lockquirk.png` gold, `sanitarium/lockedquirk.png` grey) and what the gold cross `remove_quirk_positive.png` marks.
- Whether a cell's door / curtain shows before the check mark (the picture has doors on both heroes while the choice is still open).
- Where the tooltip of a bad quirk stands (`choice_tooltip_right_justified_offset` -330 0: the mod takes it as the box's right end).

## 10. Hero banner (Guild, Blacksmith, Survivalist)

**One finding that runs through four screens: where a gold price stands.**
`shared/estate/estate.layout.darkest`: `estate_currency_gold_layout.icon_offset 0 -12`, `.number_offset 25 -14`. The coin
(`shared/estate/currency.gold.icon.png`, 24x24) therefore spans 12 px above to 12 px below the cost position, and the
amount's 25 px line starts 14 px above it: **a cost offset is the vertical middle of a gold price, not its top.** Read so,
every DD1 cost offset lands cleanly: guild `icon_cost_offset` 34 70 puts the coin at y 58..82, ending exactly on the rule
at `divider_offset` 0 82; inventory `cost_offset` 37 157 puts it at 145..169, starting one pixel under the 144 px card;
blacksmith step `cost_offset` 62 72 puts it at 60..84, directly under the step's circle (10..60); activity slot
`cost_offset` 38 -20 puts it 8 px above the slot. The mod's `UpgradeUi.BuildCosts` (`Estate/UpgradeUi.cs:175-195`) takes
the offset as the **top** of the row, while `Estate/BuildingPanel.cs:289` and `Dungeon/ProvisionScreen.cs:158` take the
same kind of key as the **middle**. Rows GU5 (Guild), BS3 (Blacksmith), SV4 (Survivalist).

Mod: `HeroActionFrame` in `Estate/UpgradeUi.cs:299-409`. DD1: `B/hero_action/hero_action.layout.darkest`,
`campaign/town/hero_slot/hero_slot.layout.darkest`.

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| HB1 Class art behind the banner | `heroes/<class>/<class>_guild_header.png`, 715x630, opaque down to row ~470, faded out by row ~590, one per class (15 base classes; flagellant, shieldbreaker, musketeer, duelist, runaway in `dlc/`). DD1 exe strings: `heroes/%s/%s_guild_header.png`. Placed by `hero_action.layout.darkest: hero_action_banner_layout.header_offset -10 0` = art px 662, 14 (covers 662..1377 x 14..644) | Nothing. `header_offset` is never read (`Estate/UpgradeUi.cs:330-343`); no reference to `guild_header` anywhere in the repo; `docs/recon/dd1-windows.md` section 6 lists the banner as "drawn in the three backdrops" only | The large dim illustration of the chosen hero's class, which fills the upper right of all three screens once a hero is in the slot, is missing entirely | high | high that DD1 draws it; medium on layering (see Needs eyes) |
| HB2 Banner text while the slot is empty | String `action_select_hero` = "Drag a hero from the roster here." (DD1 exe strings: it follows `action_verbose_body_%s_%s` directly); style `town_action_banner_help` at `help_offset 100 46` | `Estate/UpgradeUi.cs:352`: `str_empty_hero_slot_action` = "Drag a hero here." | Wrong string in the banner. The shorter one is the empty slot's own string, which the mod also shows as the slot's tooltip (`UpgradeUi.cs:347`), so the same words appear twice | medium | medium-high |
| HB3 Previous / next hero on the slot | `campaign/town/hero_slot/hero_slot.positive_prev.png`, `hero_slot.positive_next.png` (24x74 each, a bracket with a red gem) at `hero_slot.layout.darkest: town_hero_slot_layout.prev_offset -15 4`, `next_offset 76 4`; both files are loaded by the exe; `HeroActionDisplay::StartHeroSelection` exists | None (`Estate/UpgradeUi.cs:158-169` builds background and portrait only) | Missing controls | medium | medium (art and keys certain; that they show on this slot is inferred) |
| HB4 Slot states | `hero_slot.backgroundhightlight.png`, `hero_slot.positive_frame.png` (gold), `hero_slot.negative_frame.png` (red), `hero_slot.locked_for_hero.png`, all 85x85, all loaded by the exe | The banner slot always shows `hero_slot.background.png`: `Refresh` calls `_slot.Show(portrait)` without `lit` (`UpgradeUi.cs:382`, `:152`); nothing changes while a hero is dragged over it | No highlight, no accept / refuse frame | low | medium |
| HB5 Hero swap animation | `hero_slot.layout.darkest: replace_slide_out_anim` time 0.2, offset 0 -50, fade 0.5, easeInOutSine | Portrait is swapped at once (`UpgradeUi.cs:382`) | Missing animation | low | medium |
| HB6 Line above the hero's name | Nothing: the banner has a slot, a name (`name_offset 105 45`, `town_action_banner_name`) and the help text | `Estate/UpgradeUi.cs:353`: `_detail`, style `town_character_class`, at name + (2, -25) = art px 779, 34 ("Resolve level 1   Mastered 1 of 1", "Camping skills 3 of 7   Ready 2 of 4", ...) | Element DD1 does not have, inside DD1's banner. Documented as deliberate | medium | high |
| HB7 Heading of the text column | `title_text_offset 5 32`, style `town_action_verbose_title_text`; no `action_verbose_title_*` string exists, so the heading is the class (`hero_class_name_<class>`) | `Estate/UpgradeUi.cs:390`: `HeroNames.Title(hero)` = "Class, Path" | DD2 path added. Documented as deliberate | low | medium |
| HB8 Text of the column | `action_verbose_body_<building>_<class>` alone, `body_text_width 260`, drawn 1:1 (Ubuntu small); texts run to 398 characters | `Estate/UpgradeUi.cs:357`: box 260 x 346 that shrinks the text down to 14 px; `:407` appends the mod's own lines after a blank line; `VerboseHeight` 420 hard-coded (`:302`, its comment says the frame is 485 high, the file is 236x478). In the Guild the column is 196 wide (`Estate/GuildPanel.cs:75`) | Text not 1:1 once it shrinks; extra text DD1 does not have. Narrowing and extra lines documented as deliberate | medium | high |
| HB9 Close button | `hero_action_banner_layout.close_button_offset 680 0` = art px 1352, 14 | Only its x is used, as the right limit of the name (`UpgradeUi.cs:340`, `:349`); the button itself stays at `building_base_layout.close_pos` 1496 144 = art px 1352, 12 (`Estate/RosterWindow.cs:138`) | 2 px higher than the banner's own key says | low | medium |
| HB10 Face in the slot | `heroes/<class>/<class>_A/<class>_portrait_roster.png`, 85x85, at `town_hero_slot_layout.icon_offset 0 0` | DD2 portrait drawn 98 px and cut off inside a 79 px window (`UpgradeUi.cs:33-34`, `:161-166`) | Other art. Documented as deliberate | low | high |

Matches DD1: `base_pos`, `banner_pos`, `verbose_pos`, `body_pos`, `name_offset`, `help_offset`, `hero_slot_offset` (counted from the body: 682, 35, inside the box painted at 675..1341 x 28..126 in all three backdrops, measured), `frame_offset` with `verbose_frame.png`, `title_text_offset`, `body_text_offset`, `body_text_width` (Blacksmith, Survivalist); the four text styles; `hero_slot.background.png`. `base_size 100 100` is not read and has no visible effect.

Needs eyes:
- Whether the class art hides the box rules painted in the backdrop (the art is opaque black there and would cover them at 662, 14), and whether it is drawn in all three buildings or the Guild only.
- Where the hero's name sits in the box: DwarvenAxe large has line height 63 and base 48; at y 59 its capitals end near the box's bottom rule. Check against DD1 that the name is not too low.
- Whether the bracket arrows really stand on the banner's slot (at -15 the left one crosses the box's left rule, at 76 the right one touches the name's first pixels).
- Whether DD1 opens these screens with an empty slot every time; the mod keeps the hero last worked on (`UpgradeUi.cs:306`, `:361`).
- The origin of `hero_slot_offset -230 -100` (the mod counts it from the body; the result fits the box).

## 11. Guild

Mod: `Estate/GuildPanel.cs`, `Estate/Guild.cs`. DD1: `B/guild/guild.layout.darkest`, `B/upgrade/upgrade.layout.darkest`,
`upgrades/heroes/<class>.upgrades.json`.

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| GU1 Where a skill is learned | A skill's tree has five requirements "0".."4" (`upgrades/heroes/crusader.upgrades.json`: "0" = 1000 gold, no prerequisite). `guild_upgrade_tree_layout.icon_cost_visible 1`, `icon_cost_offset 34 70`, `icon_locked_offset 0 0`, `requirement_start_offset 0 0`, `requirement_spacing 75 0`: the first requirement lies on the picture itself (padlock + price under the picture), the circles are ranks 2..5. Five circles right of the picture would end at art px 1421, outside the 1395 px backdrop. The same logic explains the Survivalist (110 px between pictures leaves no room for a circle). DD1 exe: `GuildDisplay` has two purchase handlers, `BlacksmithDisplay` one | `Estate/GuildPanel.cs:176-178`: an extra circle "Known" one step right of the picture; `:195`: a left click on the picture does nothing (`null`) | Learning sits on a circle DD1 does not have, and the padlocked picture, which DD1 lets you click, is dead | medium | medium |
| GU2 Ranks | Four circles per skill (ranks 2..5) | One circle "Mastered" (`GuildPanel.cs:34`, `:179-180`) | DD2 skills have two states. Documented as deliberate | medium | high |
| GU3 Columns and place | One column of seven at `guild_layout.skill_pos 44 -4` = art px 956, 131, every `skill_spacing 0 91` | Two columns of six: x 893 and 1145 (`GuildPanel.cs:33-37`, `:74`, `:78`, `:154`; `RightEdge` 1385, `ColumnGap` 12 hard-coded) | First column 63 px left of DD1's, a second column DD1 lacks. Documented as deliberate (11 DD2 skills) | medium | high |
| GU4 Box around a skill picture | `B/guild/skill_frame.png` (88x88) exists but is not referenced by the exe: no "skill_frame" in DD1 exe strings, while `blacksmith.frame.png` and `verbose_frame.png` are there. DD1's skill pictures on the character sheet (`tutorial_popup.map_combat_skills.png`) stand without a box | `Estate/GuildPanel.cs:162`: drawn around every picture, 8 px larger on each side | Probably an element DD1 does not draw | medium | low-medium |
| GU5 Price under the picture | `icon_cost_offset 34 70` + `estate_currency_gold_layout` (see the top of this file): coin at y 58..82 of the picture, amount line 56..81, on the picture's foot, ending at the rule | `Estate/GuildPanel.cs:189` to `UpgradeUi.BuildGold`: offset taken as the row's top, coin at 72..92, amount 70..96 | 12 to 14 px too low; the price lies across the rule (82..88) and on the frame of the row below (from 83) | medium | medium-high |
| GU6 Coin of a price | `shared/estate/currency.gold.icon.png` is 24x24; amount starts 25 px right of the coin's left (`number_offset 25 -14`) | `Estate/UpgradeUi.cs:35`: `CostIcon` 20, the coin is scaled to 20x20; amount at +22 (`:184-189`). Same in Blacksmith, Survivalist, Wagon | Coin 4 px smaller than DD1's art | low | medium |
| GU7 Rule under a row | `B/upgrade/tree_divider_medium.png`, 400x6, at `divider_offset 0 82` | `Estate/GuildPanel.cs:169`: same file squeezed to 240x6 (the cell's width) | Art scaled to 60% of its width | low | high |
| GU8 Look of a skill not learned | Colour `upgrade_tree_icon_not_purchased` .darkness 0.4 .saturation 0.0 (grey), under `shared/character/lockedskill.png` | `Estate/GuildPanel.cs:164`: multiplied by 0.4, colour kept. Same in `Estate/SurvivalistPanel.cs:145` | Not desaturated | low | medium-high |
| GU9 Mark of the skills in use | `shared/character/selected_ability.png` (90x90) around a hero's active skills; colours `skill_selected` (hvec4 1.5 1.5 1.3), `skill_unselectable` (darkness 0.4, saturation 0.0); the exe loads it together with `lockedskill.png` | Nothing in the Guild (`GuildPanel.cs:158-204`); the Survivalist has it | The Guild does not show which skills are equipped | medium | medium (that DD1's Guild shows it: Needs eyes) |
| GU10 Skill name under the steps | `guild_upgrade_tree_layout.title_visible 0`: no name on the row | `Estate/GuildPanel.cs:183-185`: the name in style `tooltip` at picture + (105, 61) | Element DD1 does not have. Documented as deliberate | medium | high |
| GU11 Step tooltip: place and width | `guild_upgrade_requirement_tooltip_layout`: `tooltip_offset 0 0`, `tooltip_text_width 200`, `tooltip_is_auto_width 1`, `tooltip_is_offset_from_tree_icon 1`; `guild_upgrade_tree_layout.requirement_tooltip_tree_icon_above_offset 0 -10`, `..._below_offset 0 40` | `Estate/GuildPanel.cs:67` reads the below offset only; `:199`, `:202` apply it from the step plus the step's 50 px; width is the default 300 (`RosterWindow.Tip`) | Counted from the step, not from the skill picture as the file says; 300 wide, not 200; the "above" case is not built | low | medium |
| GU12 Tooltip words | `upgrade_prerequisite_tooltip_title` "Prerequisites:", `upgrade_prerequisite_requirement_tooltip_body_format` "%s Level %d", `upgrade_prerequisite_resolve_level_tooltip_body_format` "Hero Resolve Level: %d"; unmet ones in colour `upgrade_tree_prerequisite_not_purchased` (harmful) | `Estate/Guild.cs:234`, `:265-266`: "Needs ...", "Needs resolve level N"; `GuildPanel.cs:191-193`: "Click to learn for N gold." | The mod's wording where DD1 has its own (the upgrade pane already uses DD1's title, `UpgradePane.cs:354`) | low | high |
| GU13 Step under the pointer | `B/upgrade/requirement_highlight_overlay.png` (50x50) | Built (`UpgradeUi.cs:103`) but never switched on: `Set` is called without `highlight` (`GuildPanel.cs:178`, `:180`; `BlacksmithPanel.cs:141`) | No highlight on a step in Guild and Blacksmith | low | medium |
| GU14 Window's line of text | No string for the Guild | `Estate/GuildPanel.cs:82`: the mod's own sentence | Text DD1 does not have | low | medium |

Matches DD1: `skill_spacing 0 91`, `requirement_spacing 75 0`, step art and its offsets (`upgrade_requirement_layout.icon_offset 40 10`, `background_offset 30 0`, `background_connector_offset 0 24`), `icon_tooltip_offset 90 0`, the rule's place, `lockedskill.png`, price style `town_currency_amount` and DD1's red when too dear.

Needs eyes:
- GU1: that DD1 shows four circles and learns on the padlocked picture.
- GU4: whether any box stands around a skill picture in DD1's Guild.
- GU9: whether DD1's Guild frames the active skills and lets a click on a picture switch them.
- Whether DD1 writes a price under a skill that is already learned (the next rank's).
- Whether DD1 writes the rank number under a picture (`town_character_skill_level`, as on the character sheet).
- Where the step tooltip stands (above / below the picture).

## 12. Blacksmith

Mod: `Estate/BlacksmithPanel.cs`, `Estate/Blacksmith.cs`. DD1: `B/blacksmith/blacksmith.layout.darkest`.

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| BS1 Picture of the piece | `heroes/<class>/icons_equip/eqp_weapon_<0..4>.png`, `eqp_armour_<0..4>.png`: tall cards of **72x144**, one per level (DD1 exe strings: `heroes/%s/icons_equip/%s`), at `blacksmith_layout.equipment_pos 50 20`, every `equipment_spacing 0 176`; a box of `blacksmith.frame.png` is 176 high, the card fills 20..164 of it | `Estate/BlacksmithPanel.cs:127`: `blacksmith.weapon.icon.png` / `blacksmith.armour.icon.png` at 72x72 (the building's own upgrade tree icons) | Wrong art and half the height; the hero's own gear, changing with its level, is not shown. Documented as deliberate ("DD2 heroes do not have"), but DD1 has this art for every class DD2 shares with it, and the mod already finds it for the dungeon HUD (`Dungeon/RaidHeroPanel.cs:254-287`) | high | high |
| BS2 Name of the piece | `<class>_weapon_<n>`, `<class>_armour_<n>` in `localization/heroes.string_table.xml` ("Battered Longsword", "Longsword", "Questing Sword", "Greatsword", "The Long Crusade"); colours `equipment_tooltip_title`, `equipment_tooltip_body` | `Estate/Blacksmith.cs:76`: "Weapon" / "Armour" plus a Roman numeral (`BlacksmithPanel.cs:129`, `:152`, `:171`) | The mod's wording; DD1 writes "Armor" | medium | high (strings), medium (where DD1 shows them) |
| BS3 Price under a step | `upgrade_requirement_layout.cost_offset 62 72` + `estate_currency_gold_layout`: coin at y 60..84 of the step, right under its circle | `Estate/BlacksmithPanel.cs:134`, `:159`: offset taken as the row's top, coin at 74..94 | 12 to 14 px too low | low | medium-high |
| BS4 Two lines under each row | Nothing | `Estate/BlacksmithPanel.cs:28-30`, `:170-174`: "Weapon II   +20% damage" and "Level III: ..." at y +100 and +126, 420 wide (hard-coded) | Text DD1 does not have, in the part of the box where DD1's 144 px card stands. Documented as deliberate | medium | high |
| BS5 Tooltip of the piece: offset | `blacksmith_upgrade_tree_layout.icon_tooltip_offset 90 15` | `Estate/BlacksmithPanel.cs:129`: literal `new Vector2(90f, 15f)` | Same value, but hard-coded instead of read | low | high |
| BS6 Step tooltip: width | `blacksmith_upgrade_requirement_tooltip_layout.tooltip_is_auto_width 0`, `tooltip_text_width 220`: a box of fixed width | 220 is passed as a maximum; `Dd1Tooltip.Fill` shrinks the box to its words (`UI/UiKitWindows.cs:291-294`) | Narrower box for short texts | low | medium |
| BS7 Step tooltip: words | As GU12 | `Estate/Blacksmith.cs:275-276`, `BlacksmithPanel.cs:145-147` | The mod's wording | low | high |
| BS8 Rule | `blacksmith_upgrade_tree_layout.divider_offset 0 75` | No rule drawn | Key not used | low | low (whether DD1 draws a rule inside the boxes) |

Matches DD1: `frame_pos 30 0` with `blacksmith.frame.png` (467x360), `equipment_pos`, `equipment_spacing`, `requirement_spacing 75 0`, four steps for levels 2..5 (a class's `.weapon` / `.armour` tree has four requirements), `tooltip_offset 110 0`, "Free" in `town_free` at `free_offset 62 72` (subject to BS3's 12 px).

Needs eyes:
- Whether DD1 writes a price under every step not yet bought or under the next one only (`BlacksmithPanel.cs:155-156` shows the next one only).
- Whether DD1 writes the level on the card (`equipment_level`, colours `equipment_level_0..4`: 215 213 205, 215 213 205, 105 190 75, 62 114 212, harmful).
- BS8: a rule at y +75.
- Hover glow of a card (`overlays/eqp_mouseover.png`, `eqp_unavailable_mouseover.png`, 118x187; loaded by the exe).

## 13. Survivalist

Mod: `Estate/SurvivalistPanel.cs`, `Estate/Survivalist.cs`, `Dungeon/CampContent.cs`. DD1: `B/camping_trainer/camping_trainer.layout.darkest`, `raid/camping/default.camping_skills.json`.

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| SV1 The screen's line of text | `str_camping_longer_quests_only` = "(Only medium and long quests feature Camping.)"; DD1 exe strings list it in the camping trainer's block, right after `upgrade_tree_tooltip_description_reduces_cost_of_camping_skills_format` | `Estate/SurvivalistPanel.cs:71-72`: the mod's own two sentences about respite points | DD1's own line is not used | medium | medium (the string is this screen's; that it stands at `info_text_offset` is inferred) |
| SV2 Captions over the two rows | `camping_trainer_upgrade_tree_layout.title_visible 0`; no caption strings | `Estate/SurvivalistPanel.cs:33`, `:127-131`: class name and "Shared" in `town_upgrade_tree_title` at row + (-8, -36) | Elements DD1 does not have. Documented as deliberate | medium | high |
| SV3 Box around a skill picture | No frame art in `B/camping_trainer/`; `B/guild/skill_frame.png` is not referenced by the exe (see GU4); `tutorial_popup.map_camping_skills.png` shows the pictures without a box | `Estate/SurvivalistPanel.cs:26`, `:143`: the Guild's `skill_frame.png` around every picture | Probably an element DD1 does not draw | medium | low-medium |
| SV4 Price under a skill | `camping_trainer_upgrade_tree_layout.icon_cost_offset 32 90` + `estate_currency_gold_layout`: coin at y 78..102 of the picture | `Estate/SurvivalistPanel.cs:155`: offset taken as the row's top, coin at 92..112 | 12 to 14 px too low | low | medium-high |
| SV5 Tooltip: words | `camping_skill_cost` = "Time Cost: %d" in colour `attacktype` (#8d7b5f); names as in `camping_skill_name_<id>`, which are capitals ("ENCOURAGE", "MAINTAIN EQUIPMENT") | `Estate/SurvivalistPanel.cs:159`: "N respite, M uses a camp, shared"; `Dungeon/CampContent.cs:88-93`: names turned into Title Case | The mod's wording and casing | low | medium |
| SV6 Rows of four | `skill_number_of_rows 4` in both grids (with `skill_spacing 110 90` it can only mean four to a row) | `Estate/SurvivalistPanel.cs:132-133`: one row, never wrapped | Key not used; no effect with DD1's data (4 own + 3 shared skills per class) | low | medium |
| SV7 Rule | `camping_trainer_upgrade_tree_layout.divider_offset 0 75` | No rule drawn | Key not used | low | low |

Matches DD1: both grids (`skill_start_pos 60 30` / `60 200`, `skill_spacing 110 90` = art px 972, 165 and 972, 335), the pictures (`raid/camping/skill_icons/camp_skill_<id>.png`, 72x72), `lockedskill.png`, `selected_ability.png` (90x90, 9 px around), a click on the picture learns (as DD1: one requirement "0", 1750 gold), price style. Also GU6 and GU8 apply here.
`B/camping_trainer/dueling_grounds.character_background.png` is byte for byte the same file as `camping_trainer.character_background.png` (same MD5) and is named by no layout, json or exe string: a leftover of the building's old name, nothing to build.

Needs eyes:
- SV3: a box around the pictures or none.
- How DD1 draws a known skill that is not chosen (the tutorial picture shows them dimmer than the chosen ones; the mod draws them at full brightness).
- Which tooltip DD1 shows on a camping skill and where (`icon_tooltip_offset 90 0` as the mod has it, or `camping_trainer_upgrade_requirement_tooltip_layout.tooltip_offset 210 0`).
- SV7: a rule under each row.

## 14. Nomad Wagon

Mod: `Estate/NomadWagonPanel.cs`, `Estate/NomadWagon.cs`, `Estate/Trinkets.cs`. DD1: `B/nomad_wagon/nomad_wagon.layout.darkest`, `shared/inventory/inventory.layout.darkest`.

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| NW1 The "stores" under the table | Nothing: DD1's wagon is the table of wares only; trinkets are sold in the Trinket Inventory (`campaign/town/realm_inventory/`), which the mod now has (`Estate/RealmInventoryPanel.cs`, Shift + click sells) | `Estate/NomadWagonPanel.cs:39-42`, `:109-119`, `:170-180`, `:208-219`: a 55% black shade 684x226 at art px 682, 482, the title "Trinket Inventory" in `town_activity_name` at 690, 486, a hint line, up to eight cards from 710, 530 every 80 px with sell prices, two scroll arrows | A whole block DD1 does not have, laid over the still life painted into the foot of `nomad_wagon.character_background.png`. Documented as deliberate, written when the mod had no Trinket Inventory; now a second place to sell | high | high |
| NW2 Card in its cell | `inventory_item_layout.icon_offset 4 0`: the 72x144 card stands 4 px inside its cell | `Estate/NomadWagonPanel.cs:164`, `:224`: card at the cell's corner; the key is not read here (the mod's `Dungeon/InventoryGrid.cs` and `ProvisionScreen.cs` do apply it) | Cards 4 px left of DD1's | low | high |
| NW3 Price under a card | `inventory_item_layout.cost_offset 37 157` + `estate_currency_gold_layout`: coin at y 145..169 from the cell's top | `Estate/NomadWagonPanel.cs:37`, `:238`: hard-coded, card centre x 36 and `Card.y + PriceGap` = 146 as the row's top (coin 148..168) | Not read from the file; about 1 to 3 px off | low | medium-high |
| NW4 Tooltip: place and width | `inventory_item_tooltip_layout.offset 85 0`, `text_width 200`: beside the card, level with its top, 200 wide | `Estate/NomadWagonPanel.cs:203`, `:217`: `Tooltip.Show(card, at + (0, -4))`: above the card, growing upwards, 300 wide | Other place, other width | medium | high (file), medium (how DD1 anchors it) |
| NW5 Tooltip: words and colours | Rarity `trinket_rarity_<r>`; class limit `trinket_hero_class_requirement_format` "[%s only]" in colour `specificclass`; value `str_inventory_gold_value_format` "[Value: %s Gold Each]" in colour `inventory_tooltip_gold_value`; colour ids per rarity in `colours/base.colours.darkest` (`common` 168 168 168, `uncommon` 96 149 75, `rare` 59 97 167, `very_rare` 194 69 0, `ancestral` 224 39 0, `trophy` 222 182 105) | `Estate/NomadWagonPanel.cs:242-248`: rarity word in `notable`, DD2's item text, an action line in `town_building_info` grey ("Click to buy for N gold."); heading always in the tooltip's gold | Rarity colours unused; DD1's value and class lines replaced by the mod's wording | low | medium |
| NW6 A ware that was bought | A store is an inventory (`inventory_system_grid_layout`): a bought item is gone from it. No "sold" art or string exists in DD1 | `Estate/NomadWagonPanel.cs:189-193`, `:225`: the card stays at 35% brightness with the word "sold" in `town_building_info` under it | State DD1 does not have | medium | medium |
| NW7 Card under the pointer; ware too dear | Colours `inventory_selected` (hvec4 1.4 1.4 1.7), `inventory_unselectable` (rgba #666, saturation 0.2), `inventory_amount_unselectable` (#5d5a50); `inventory_outline.variance_count 12` | Cards have no hover state; a ware the estate cannot pay for is drawn as any other, only its price turns red | Missing states | low | medium |
| NW8 Trinket picture | `panels/icons_equip/trinket/inv_trinket+<id>.png` (72x144) on `panels/icons_equip/trinket/rarity_<r>.png` | `Estate/NomadWagonPanel.cs:36`, `:226-228`: DD1's rarity card with DD2's icon at 64x64 in its middle | DD2 art. Documented as deliberate | medium | high |
| NW9 Window's line of text | No string for the wagon | `Estate/NomadWagonPanel.cs:166-168`: the mod's own sentences | Text DD1 does not have | low | medium |
| NW10 Full stores | `not_enough_room_confirm_nomad_wagon` = "Your realm inventory is full. Sell or discard some trinkets to make room." | `Estate/NomadWagon.cs:165`: "The estate holds one already" (DD2's one-of-each rule) | Other rule, other words | low | high |

Matches DD1: the table (`inventory_system_background_layout.pos 230 150` = art px 682, 120; `inventory_grid_background.png` 684x360), the grid (`number_of_columns 6`, `start_pos 55 -10`, `offset 100 180`), card size 72x144, the rarity card file (DD1 exe strings: `panels/icons_equip/trinket/rarity_%s.png`), rarity names, price style and DD1's red, DD1's sell question and "Yes" / "No".
`fx/trinket_sparkle` (atlas 38x42: `ancestral_sparkle`, `boss_sparkle`, 34x18 each; skeleton names `ancestral_trinket`, `boss_trinket`) is not drawn by the mod. DD1's wagon never needs it: `nomad_wagon.building.json` `rarity_generation_table` sells very_common to very_rare only. It matters for trophies and ancestral trinkets, i.e. for the Trinket Inventory (see that section) and for the mod's stores row (NW1).

Needs eyes:
- NW6: whether the remaining wares keep their places after a purchase.
- NW4: the tooltip's place beside a card of the right-hand column.
- NW7: what a card looks like under the pointer in DD1.

## 15. Graveyard

Mod: `Estate/GraveyardPanel.cs`, `Estate/Graveyard.cs`. DD1: `B/graveyard/graveyard.layout.darkest`.

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| GY1 Where the death line starts | `dead_hero_layout.name_position 96 0` and `death_by_position 96 128` have the same x: name and death line start under each other | Name counted from the slab plus a margin: slab x 106 = entry x 224 (`Estate/GraveyardPanel.cs:102`, `:106`); death line counted from the entry's corner: entry x 96 (`:115`), under the tombstone, 22 px left of the slab | The two keys are counted from two different corners; the death line starts 128 px left of the name | medium | medium |
| GY2 The line with the week | DD1 exe strings: `"%s %s, "` stands directly before `str_graveyard_week_string` ("Week %d"): the week is the tail of a longer line of two words and a comma, most likely resolve title and class (`str_resolve_<n>`: Seeker, Apprentice, Adventurer, Veteran, Master, Champion, Legend), e.g. "Veteran Crusader, Week 12"; style `town_graveyard_hero_death_week` (DwarvenAxe medium, 80 79 75) | `Estate/GraveyardPanel.cs:104-105`: "Week N" alone, right aligned at the slab's right end (the -6 and the 150x40 box are hard-coded); `:108-109`: the mod's own line "Class, place" in `town_graveyard_story` 40 px under the name | Class is in another line and another style, the resolve title is absent, the week stands apart; the place of death is the mod's (documented as deliberate) | medium | low-medium |
| GY3 Margins | `background_margins 10 25 10 20`, `tombstone_margins 0 0 0 0`, `hero_image_margins 0 10 0 0`, `text_box_margins 0 0 0 0`, `hero_story_spacing -2` | Only `background_margins` is read, as left, top, right, bottom (`Estate/GraveyardPanel.cs:67-71`). In that order the slab's content is 118 - 25 - 20 = 73 high, less than the 85 px face, so the face is centred by a formula of the mod's instead (y 16.5, `:100`). Read as left, right, top, bottom the content is 88 high and the face fits at y 10 | Four keys unused, the order of the fifth assumed | medium | low-medium |
| GY4 A grave can be clicked | DD1 exe: `GraveyardDisplay::BuildDeadHeroWidgets` makes a `ButtonWidget` per dead hero with a (hero, index) handler, plus `GetDeadHeroByIndex`: a click opens the fallen hero | Graves take no pointer (`Estate/GraveyardPanel.cs:89-116`) | Missing interaction | medium | medium |
| GY5 Words of a death | `str_death_<cause>_<source>` (DD1 exe strings: `str_death_%s_%s`), about 30 ids: bled out, succumbed to Blight, heart attack, hunger, trap, obstacle, hand of an ally ...; `str_death_attack_monster` has two wordings ("was slain by a vile", "met their end against a") | `Estate/GraveyardPanel.cs:112-114`: `str_death_attack_monster` (first wording only) or `str_death_unknown_unknown` | Every death that is not a monster's blow reads "succumbed to an unknown peril." | low | high |
| GY6 Hero's name inside the death line | Style `town_graveyard_story` (neutral) for the line; the strings carry no colour mark for the name | `Estate/GraveyardPanel.cs:115`: name wrapped in `notable` | A gold name DD1's data does not ask for | low | medium |
| GY7 Scroll bar | The exe loads six parts together (DD1 exe strings 00cfc810..00cfc8bc): `shared/widgets/scrollbarmid.png` (21x29), `scrollbartop.png`, `scrollbarbottom.png` (21x12), `scrollpip.png` (24x24), `scrollbar_uparrow.png`, `scrollbar_downarrow.png` (41x25) | `UI/UiKitWindows.cs:122-136` (`Dd1Ui.ScrollList`): `scrollbarmid.png` stretched over the whole height, plus the pip | No end caps, no arrows. Same in the Memoirs | low | medium-high |
| GY8 Stones of the last expedition | `B/graveyard/final_dd_team_tombstone.png`, `final_dd_team_final_blow_tombstone.png` (118x118) | `Estate/GraveyardPanel.cs:30-34` picks by level only | Not used | low | high |
| GY9 Death count of a Stygian estate | `graveyard_main_layout.new_game_plus_death_count_pos 650 84`; `graveyard_death_count_format` "Deaths: %d/%d"; style `town_graveyard_death_count` (DwarvenAxe large, 134 9 2) | Not built (the mod has no such mode) | Missing | low | high |
| GY10 Face of the fallen | `heroes/<class>/<class>_A/<class>_portrait_roster.png`, 85x85 (DD1 exe strings: `heroes/%s/%s_portrait_roster.png`) | `Estate/GraveyardPanel.cs:97-98`: DD2's black-and-white portrait at 80% brightness; the comment "DD1's dead are drawn without colour" rests on no file | DD2 art; the greying is an assumption | low | medium |
| GY11 Window's line of text | No string | `Estate/GraveyardPanel.cs:81`: "N heroes lie here." | Text DD1 does not have | low | medium |

Matches DD1: `list_position 148 148` (art px 600, 118), `list_area_size 720 600`, entry pitch 160 + 20, `scrollbar_offset 20`, the stone by resolve level (`0_1.png`, `2.png` .. `6.png`: `Estate/Graveyard.cs:47` fills `LevelOf`, so the note in `dd1-windows.md` section 8 about "the plainest stone for everybody" is out of date), `dead_hero_backdrop.png` (600x118) beside the 118 px stone (718 of the list's 720 px), the three text styles, `str_graveyard_week_string`, the death line's shape (name, words, killer, full stop: DD1 exe strings ` %s.`).

Needs eyes (the mod's own doc says DD1's picture of an entry was not to hand):
- The whole entry: where face, name, week line and death line lie on or under the slab (GY1 to GY3), and whether the death line is inside the slab or under it.
- GY2: the exact words of the week line.
- GY4: what a click on a grave opens.
- Order of the list (the mod: latest first).
- Whether the face is grey.

## 16. Ancestor's Memoirs (statue)

Mod: `Estate/MemoirsPanel.cs`, `Estate/Memoirs.cs`. DD1: `B/statue/statue.layout.darkest`, `B/statue/statue_media_info.json`.

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| AM1 What one entry holds | One slab per boss (`portrait_<boss>.png`, nine of them plus `portrait_darkest_dungeon.png`) with a line per part: `str_part` "Part" and its number in a box of `statue_entry_layout.part_textbox_width 75`, the part's title `str_<quest>_audio_line` ("Mastery Over Life and Death", "The Visitors", "The Dead Reviving The Dead") in a box of `textbox_width 350` (`textbox_top_margin 5`, `textbox_entry_spacing 0`), and a play or lock button per part (`button_margins 0 5 0 0`; `playmedia.png` / `lockedmedia.png`, 32x32). DD1 exe strings: `str_part`, `str_%s_audio_line`, `campaign/town/buildings/statue/portrait_%s.png`, `BuildCategoryEntryWidget` with a `ButtonWidget` | One slab per finished quest (`Estate/Memoirs.cs:214-232`): the quest's name, a second line of the mod's ("The Librarian (1 / 3), Ruins"), one mark at the slab's right end (`Estate/MemoirsPanel.cs:187-217`). Quests not finished are not listed (`Memoirs.cs:217`) | Other structure: three slabs where DD1 has one with three lines; no "Part N"; parts still shut are absent instead of shown with a lock | high | medium-high |
| AM2 Opening a memoir | DD1 plays it: sound with subtitles, or a film (DD1 exe strings: `video/`, `%s%s.ogv`) | `Estate/MemoirsPanel.cs:125-146`, `:233-251`: the shelves give way to a page: title bar, a line in `town_statue_archive_entry`, the text in `town_statue_quote` at 26 px (`:34`, not the font's 1:1 size), `shared/progression/progression_back.png` | A screen DD1 does not have. Documented as deliberate (no voice) | medium | high |
| AM3 Places inside a slab | `content_margins 10 10 10 10`, `video_image_margins 0 10 0 0`, `plot_quest_image_margins 0 10 0 0`, `button_margins 0 5 0 0`, `textbox_top_margin 5`, `textbox_entry_spacing 0` | Only the first two numbers of `content_margins` and `textbox_width` are read (`Estate/MemoirsPanel.cs:112-113`). Hard-coded: picture at x margin + 6, y centred (16.5) (`:198`); text 14 px right of the picture (`:201`); title at y 30, second line at y 58 (`:204`, `:207`); text width `textbox_width + 60` (`:204`); mark 20 px in from the right end, centred (`:215`) | Five keys unused, six numbers of the mod's own | medium | high (unread), low (what DD1's result looks like) |
| AM4 Scroll bar | `statue_main_layout.scrollbar_offset 20 -50`; parts as in GY7 | `Estate/MemoirsPanel.cs:116`: only the x is read; bar as tall as the list, no caps, no arrows | The -50 is ignored | low | medium |
| AM5 Collected Journals | Category `backerjournal` in `statue_media_info.json`: `str_media_backer_journals` "Collected Journals", `backer_journal_title_bar.png`, `media_entry_backer_journal_backdrop.png`, `backer_journal_icon.png` | Never shown: no entry is ever filed under it; the fallback list lacks it (`Estate/Memoirs.cs:64-70`); `backer_journal_icon.png` is used instead as the picture of a quest without a DD1 portrait (`Memoirs.cs:285-290`) | Category missing; its icon used for something else | low | high |
| AM6 Entry that is shut | Colour `town_statue_archive_locked_entry` .darkness 0.4 .saturation 0.2 | `Estate/MemoirsPanel.cs:196`, `:203`: picture x 0.45, text blended half way to black, colour kept | Brightness 0.45 to 0.5 instead of 0.4, not desaturated | low | medium |
| AM7 What is the button | The play mark (a `ButtonWidget` per entry part) | `Estate/MemoirsPanel.cs:190`: the whole slab is a button and tints under the pointer | Other hot spot, other hover look | low | medium |
| AM8 Picture of an entry | `B/statue/portrait_<boss>.png`, `<film>.png` (85x85, with their own painted frame) | `Estate/MemoirsPanel.cs:220-229`: DD2's portrait of the boss when it is loaded, DD1's picture otherwise | DD2 art without DD1's painted frame, on some entries only | low | high |
| AM9 Window's line of text | No string | `Estate/MemoirsPanel.cs:173-174`: "N of M chapters of the estate's story told" | Text DD1 does not have | low | medium |

Matches DD1: `list_position 270 170` (art px 722, 140), `list_area_size 600 580`, `entry_spacing 10`, `quote_position 590 80` as the middle of the line (art px 1042, 50: the middle of list plus scroll bar), `quote_width 505`, `str_statue_ancestor_quote` in `town_statue_quote`; categories, their order, bars and slabs from `statue_media_info.json`; labels `str_media_video` / `str_media_boss_quests` / `str_darkest_dungeon_audio` / `str_media_epilog_title` in `town_statue_media_heading`, centred on the bar's purple field (rows 38..96 of the 619x136 art, measured); slabs 600x118; pictures 85x85; marks 32x32; entry text in `town_statue_archive_entry`; `str_media_epilog_locked` for the shut epilogue.

Needs eyes:
- AM1: DD1's entry as it really looks (boss, three part lines, three buttons), and whether a boss is listed before its first part is unlocked.
- How DD1 stacks a category bar: the mod centres the 619 px bar on the 600 px list and lets its clear rows overlap (rows 16..118 count, `MemoirsPanel.cs:29-30`, `:182-184`), so a bar takes 102 + 10 px; stacked by its own size it would take 136 + 10 and start at the list's left edge.
- AM4: what the -50 does to the scroll bar.
- Whether the Ancestor's line changes while a memoir plays.

## 17. Activity Log

Mod: `Estate/ActivityLogPanel.cs`, `Estate/ActivityLog.cs`, `Estate/ActivityLogText.cs`. DD1: `campaign/town/activity_log/`, `activity_log/` at the install's root (entry art), `shared/widgets/`.

**The screen's icon.** Common to the Activity Log, the Trinket Inventory, the town event panel and the mod's own Estate Map: **DD1's "name" of a town screen is a 113x113 icon at `name_pos` with the name beside it.** Evidence: every layout with a `name_pos` has a `<screen>.icon.png` of 113x113 beside it (`activity_log.icon.png`, `caretaker_goals.icon.png`, `realm_inventory.icon.png`, `town_event.icon.png`, `quest_select.icon.png`, `provision.icon.png`, `glossary.icon.png`, every `buildings/<id>/<id>.icon.png`); the Activity Log has two `name_pos` and exactly two such icons; `name_pos` is negative (the icon overhangs the panel's corner) and the backdrops' rules begin where the icon ends (log: icon -46..67, rule from 72; goals: icon 650..763, rule from 760); and the mod already draws it that way on the Estate Map (`Estate/QuestPanel.cs:483-489`, comment "DD1 gives every town screen its icon and name").

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| Header icon "Activity Log" | `campaign/town/activity_log/activity_log.icon.png` 113x113 (scroll in a dark square); `activity_log.layout.darkest: activity_log_layout.name_pos` -46 -12; DD1 exe strings: `campaign/town/activity_log/activity_log.icon.png` | `Estate/ActivityLogPanel.cs:127,160-165`: only a text label; the file is not used anywhere in the mod (the bar button now uses `fx/estate_activity_log`, `Estate/ActivityLog.cs:36,142`) | The screen's corner icon is missing | high | medium |
| Header icon "Caretaker Goals" | `campaign/town/activity_log/caretaker_goals.icon.png` 113x113 (checklist); `caretaker_goal_layout.name_pos` 650 -12; DD1 exe strings name the file next to `activitylog_bg.png` | `Estate/ActivityLogPanel.cs:128`: text only; file never loaded | Second corner icon missing | high | medium |
| Place of the two names | `name_pos` -46 -12 and 650 -12 are the icon's corner (see above); the text's own offset is in no file. Backdrop rules: left x 72..637, right x 760..1326, y 27..36 and 108..117 | `Estate/ActivityLogPanel.cs:45-46` `NameWidget` 296 82, `NameSize` 296 64 (hard-coded, from the buildings' backdrops): names centred on 250,70 and 946,70 | Number is the mod's inference (documented as inference). With an icon ending at x 67 / 763 a name that starts beside the icon would begin near x 75 / 770; the mod's centred "Activity Log" begins near x 150 | medium | low |
| Scroll rail end caps | `shared/widgets/scrollbartop.png`, `scrollbarbottom.png` 21x12 each; DD1 exe strings list both with `scrollbarmid.png`, `scrollpip.png`, the two arrows (ScrollBarWidget) | `UI/UiKitWindows.cs:122-124`: only `scrollbarmid.png` stretched over the whole window height (21 x 550) and `scrollpip.png` | Rail has no top and bottom cap (both lists of this screen) | medium | high |
| Scroll arrows, place | `activity_log_layout.scroll_arrows_offset` 15 7; art `shared/widgets/scrollbar_uparrow.png` / `_downarrow.png` 41x25 | `Estate/ActivityLogPanel.cs:135,146-148`: x = rail middle (frame x 635), y = 7 above / below the 550 px window (128 and 717); the 15 is read and not used | Key's x ignored. Documented as deliberate | low | low |
| Scroll arrows, visibility | no state art; rail and arrows are one widget in DD1 (exe strings) | arrows are plain buttons that are never hidden (`ActivityLogPanel.cs:147-148`); the rail hides itself while the list fits (`UiKitWindows.cs:137` AutoHide) | With a short log two arrows float beside no rail | low | high (mod side) |
| Goals list scroll arrows | `caretaker_goal_layout.scrollbar_offset` 0 (same widget as the log's) | `Estate/ActivityLogPanel.cs:157`: rail and pip only, no arrow buttons | Goals list has no arrows, the log has | low | low |
| Divider between entries | `activity_log/separator.png` 584x7 (thin rule); DD1 exe strings: `activity_log/separator.png` directly beside `activity_log/week_title_bar.png` | never loaded; entries are parted by blank space only (`ActivityLogPanel.cs:247`: 5 px between two text lines, `vertical_spacing` 20 before/after a frame) | DD1 art of the log not used; where DD1 draws it is in no file (see Needs eyes) | medium | medium |
| Inside a hero entry (sign, face, text) | `hero_building_log_entry_layout`: `content_margins` 0 0 5 10, `building_image_margins` 0 6 0 0, `hero_image_margins` 10 10 5 5, `textbox_width` 440, `textbox_top_margin` 5, `textbox_entry_spacing` 0 | `Estate/ActivityLogPanel.cs:53,292-330`: none of the six keys is read. Hard-coded: `BoxInset` 10, sign at +10 (y +22), face at sign + 6 + 10 and centred on the frame's height, text from face + 10, width = 600 - 10 - 6 - x | With sign and face the text box is 414 px wide (DD1: 440) and starts at 170 from the frame's left (DD1's margins add up to 160); the face sits 10 px further right and, if the margins are left/right/top/bottom, 6.5 px lower (16.5 vs 10) | medium | medium |
| Text of a framed entry shrinks | `town_activity_log_entry` = `ubuntu_small`, 25 px line, drawn 1:1; `textbox_entry_spacing` 0 (several texts stacked) | `Estate/ActivityLogPanel.cs:327`: `Dd1Ui.Block(..., 13f)` auto-sizes down to 13 px to stay inside 98 px | A hero entry with 4+ lines is set smaller than DD1's 1:1 text (what DD1 does with a long entry is not in a file) | low | medium |
| Half-pixel placement | art is drawn 1:1 on whole pixels | `ActivityLogPanel.cs:294`: frames at x = (619 - 600) / 2 = 9.5; `:318` face at y + (118 - 85) / 2 = +16.5; `:300` Darkest icon y +16.5 | Frames, faces and the icon land on half pixels (soft at 1080p) | low | high |
| Buttons at rest are dimmed | DD1 art is drawn as painted | `UI/UiKitWindows.cs:88-93`: every `Dd1Ui.ArtButton` has `normalColor` 0.86 grey and is full bright only under the pointer (close button, scroll arrows; same helper in all four screens) | Close button and arrows are 14% darker than the art at rest | low | high (mod side) |
| Week count of the timed modes | `activity_log_layout.new_game_plus_week_count_pos` 310 120; string `activity_lot_week_count_format` "Week: %d/%d"; font ref `town_activity_log_week_count` (`dwarven_axe_medium`), colour neutral | not drawn (until commit a341427 the mod used this style for a week on the hamlet's name plate) | Missing; documented as deliberate (no timed mode) | low | high |
| Empty week | no string for it | `Estate/ActivityLogPanel.cs:239`: "Nothing on record yet." in `town_building_info` grey; `Estate/ActivityLog.cs:170-177` always lists the present week | Invented line and an always-present bar for the current week | low | high |
| Entries DD1 does not log | DD1 strings have no sentence for a hire, a dismissal, a trinket bought/sold, an heirloom trade, a haul | `Estate/ActivityLog.cs:135-137,352,415,448`: mod's sentences; hire and dismissal are drawn in DD1's hero frame with the Stage Coach's 113 px `stage_coach.icon.png` scaled to 64 (`ActivityLogPanel.cs:306-313`) | Invented entries; documented as deliberate. The lent 113 px icon is not drawn 1:1 | low | high |
| Faces in the log | DD1 portrait art 85x85 (`hero_image_margins` leave 85) | `Estate/UpgradeUi.cs:158-165`: DD2 portrait 98 px cut off inside `campaign/town/hero_slot/hero_slot.background.png` | DD2 art inside a building's hero slot frame; documented as deliberate | low | high |
| Unused entry art | `activity_log/hero_final_dd_entry_backdrop.png`, `trinket_retention_entry_backdrop.png` 600x118, `trinket_retention_log_icon.png` 113x113 (DD1 exe strings name all three) | not used: the mod has no such events (`Estate/TownEvents.cs:201` leaves the Shrieker's theft out) | Missing entry kinds (hero's last Darkest Dungeon quest; trinkets stolen) | low | high |
| Open / close sound | DD1 exe strings: `/ui/town/page_open`, `/ui/town/page_close` (which screen they belong to is not stated) | `Dd2/EstateAudio.cs:449-459` plays nothing when a `TownPanel` opens or closes | No sound on opening or closing the log | low | low |

Matches DD1 (checked): backdrop `activitylog_bg.png` 1395x776 at `activity_log_pos` 144 132; `close_pos` 1352 12 with `shared/progression/progression_close.png` 32x32; list window at `text_pos` 25 160, `panel_size` 620 550, `entry_start_pos` 0 10, `scrollbar_offset` 0, `vertical_spacing` 20; goals window `text_pos` 720 160, `panel_size` 600 240, `entry_spacing` 10 10; `week_title_bar.png` 619x136 (its field's middle measured again: 322, 67; opaque rows 16..118); the three `raid_*_banner.png` 619x58; the five entry backdrops used; `darkest_dungeon_log_icon.png` 85x85; font refs `town_name`, `town_activity_log_entry` (ubuntu_small), `town_activity_log_week_title` (dwarven_axe_large), `caretaker_goal_type_heading` (dwarven_axe_large), `caretaker_goal_entry` (ubuntu_small); colours `town_activity_log_building_name / _character_name / _positive_result` (notable), `_negative_result` (harmful), `caretaker_goal_completed` (notable) / `_not_completed` (neutral); strings `town_name_activity_log`, `str_caretaker_goals_heading`, `..._quest_goals_heading`, `..._roster_goals_heading`, `str_caretaker_goal_hero_resolve`, `str_week`, `str_town_event_started`.

Needs eyes
- Where the name stands beside its icon (left-aligned from the icon, or centred as now), for both names.
- `scroll_arrows_offset` 15 7: what the 15 counts from; whether DD1 shows rail and arrows while the list fits.
- `separator.png`: between which entries DD1 draws it (every entry, only plain lines, under a week's bar).
- `entry_spacing` 0 30 against `vertical_spacing` 20: the mod reads the first as a line step (5 px between plain lines); DD1 may use it between entries.
- Order of weeks (mod: newest first) and the bar's own spacing (`WeekBarLead` 12, `WeekBarStep` 112 are the mod's).
- Order of the four numbers in the three `*_margins` keys; whether the building picture in a hero entry is the 64x64 `<id>.icon_roster.png`; whether DD1's frame grows with a long text.
- Whether the log window really starts at x 25 (it is wider than, and left of, the header rules at 72..637).
- Font ref `town_activity_hero_portrait_name` (dwarven_axe_medium, no colour id): the margins leave no room for a name under the face in a 118 px frame, so it is probably a building's activity slot, not the log; confirm.

## 18. Trinket Inventory (realm inventory)

Mod: `Estate/RealmInventoryPanel.cs`, `Estate/RealmInventory.cs`. DD1: `campaign/town/realm_inventory/`, `shared/inventory/inventory.layout.darkest`, `shared/character/character.layout.darkest`, `shared/hero/hero.layout.darkest`, `panels/icons_equip/trinket/`, `fx/trinket_sparkle`. "sheet px" = pixels of DD1's character sheet (its panel stands at 144 132); the sheet's art `shared/character/characterpanel_frames.png` is drawn at `character_layout.frames_pos` 10 10, so sheet px = art px + 10.

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| Header icon | `campaign/town/realm_inventory/realm_inventory.icon.png` 113x113 (chest); `realm_inventory.layout.darkest: realm_inventory_layout.name_pos` -44 -10; DD1 exe strings name the file | `Estate/RealmInventoryPanel.cs:173-175`: text only; the file is not used (bar button uses `fx/estate_realm_inventory`, `Estate/RealmInventory.cs:43,61`) | Corner icon missing | high | medium |
| Place of the name | `name_pos` -44 -10 = the icon's corner; text offset in no file | `Estate/RealmInventoryPanel.cs:59-60`: `NameWidget` 296 82 hard-coded, centred on 252,72 (+ `content_offset` 0 2) | Mod's inference (documented); a name beside the icon would begin near x 75 | medium | low |
| Colour of "Hold [SHIFT] to Sell Trinkets" | `colours/base.colours.darkest`: `realm_inventory_trinket_sell_instruction` = neutral (174 172 162); `realm_inventory_trinket_sell_description` = notable (200 180 110), the id of "Sell trinket for:" | `Estate/RealmInventoryPanel.cs:490-492`: both strings get `Dd1Fonts.Colour("realm_inventory_trinket_sell_description", ...)` | The instruction is gold; DD1 has it grey (the colour id of the instruction is never asked for) | medium | high |
| Sparkle on ancestral and trophy trinkets | `fx/trinket_sparkle/trinket_sparkle.sprite.{atlas,png,skel}`: regions `ancestral_sparkle`, `boss_sparkle` 34x18; bones `*_top/_bottom/_left/_right`; animations `ancestral_trinket`, `boss_trinket`; DD1 exe strings: `fx/trinket_sparkle/trinket_sparkle.sprite.skel` | none (`Estate/RealmInventoryPanel.cs:707-716` draws card + icon only); the mod has both grades (`Estate/Trinkets.cs:39,50,100`) | Animated sparkle running round the card is missing | medium | medium |
| What is on a card | `panels/icons_equip/trinket/rarity_<rarity>.png` 72x144 under the trinket's own 72x144 art; `shared/inventory/inventory.layout.darkest: inventory_item_layout.icon_size` 72 144 | `Estate/RealmInventoryPanel.cs:57,707-716`: DD1's rarity card with DD2's icon at 64 px in its middle | Different picture on the card; documented as deliberate | medium | high |
| Card's place in its cell | `inventory_item_layout.icon_offset` 4 0 (a 72 px card 4 px into an 80 px cell). It is needed to land DD1's sheet trinket slots on the painted frames (462 + 32 + 4 = 498 sheet px = art 488), and the mod's own raid grid applies it (`Dungeon/RaidLayout.cs:246`, `Dungeon/InventoryGrid.cs:14-15`) | `Estate/RealmInventoryPanel.cs:601-602`: card at `inventory_grid_pos` + `offset` x column, no icon offset; the file `shared/inventory/inventory.layout.darkest` is not read here | All cards 4 px left of DD1's (30..102 instead of 34..106); the column rules (`:599`) were fitted to the mod's cards, so they shift too | low | medium |
| Sorting animation | `realm_inventory_layout.sort_time` 0.4 | `Estate/RealmInventoryPanel.cs:307-317`: the grid is rebuilt at once; key not read | Cards do not travel to their new places | low | medium |
| Order and direction of the four small buttons | `sort_button_pos` 567 22, `sort_button_spacing` 42 0 (no key per button) | `Estate/RealmInventoryPanel.cs:182-190`: row ends at 567: name 441, class 483, rarity 525, unequip-all 567 | Guess, documented as a guess | low | low |
| Third click on a sort button | strings `sort_trinket_label_none` "Custom"; no rule in a file | `Estate/RealmInventoryPanel.cs:307-317`: this order, reversed, then back to arrival order | Mod's rule | low | low |
| "Unequip all" asks first | strings `str_unequip_all_trinkets_description` "Unequip all trinkets on heroes?", `_yes`, `_no` (table PSN; all three in DD1 exe strings) | `Estate/RealmInventoryPanel.cs:335-339`: unequips at once and writes its own sentence into the line under the title | No question; invented result text | medium | medium |
| Sell question | `realm_inventory_sell_trinket_confirm_question_format`, `_yes`, `_no`, `_always` ("Always") | `Estate/RealmInventoryPanel.cs:419-439`: DD2's confirmation dialog, Yes / No only | DD2's dialog look; "Always" missing. Partly documented | low | high |
| Scroll arrows of the grid | `realm_inventory_uparrow.png` / `_downarrow.png` 62x49; no place in the file | `Estate/RealmInventoryPanel.cs:224-225`: at the rail's ends, x = 620.5 - 31 = 589.5 (half pixel), overlapping the window by 6 | Place is the mod's (documented); half-pixel x | low | low |
| Scroll rail | `scroll_bar_offset` 20 0 from the grid (`inventory_grid_pos` 30 195, `inventory_grid_size` 560 525); caps `shared/widgets/scrollbartop/bottom.png` | `Estate/RealmInventoryPanel.cs:216-220`: rail at x 610 from y 180 (grid top - 15) to 720, no caps (`UI/UiKitWindows.cs:122-124`) | Rail begins 15 px above the grid's top; no caps | low | medium |
| Row rules | `realminventory_h_grid.png` 628x12; `grid_visuals_offset` -15 | `Estate/RealmInventoryPanel.cs:216,597`: window and rules at x = (667 - 628) / 2 = 19.5 | Half-pixel x for every row rule and for the scroll window's edge | low | high |
| A trinket the hero cannot wear / pointer over a card | `colours/base.colours.darkest`: `inventory_unselectable` #666 with saturation 0.2; `inventory_selected` hvec4 1.4 1.4 1.7 | `Estate/RealmInventoryPanel.cs:710,745`: card and icon x 0.4, no desaturation; no brightening under the pointer | Dimmed but not greyed; no highlight on hover | low | medium |
| Card tooltip | `inventory_item_tooltip_layout.offset` 85 0, `text_width` 200 (beside the card, at its top) | `Estate/RealmInventoryPanel.cs:231-232,678`: beside the panel's left edge over the band, 300 wide, DD2's item text + mod's lines ("Click to put it on ...") | Other place, width and content; documented as deliberate | medium | high |
| Hero side: what stands beside the panel | DD1 shows the whole character sheet (`shared/character/characterpanel_bg.png` + `_frames.png` 1395x776) only when a hero's sheet is open; opened from the bar alone the inventory stands by itself | `Estate/RealmInventoryPanel.cs:63,239-250`: a 520x365 cut of `characterpanel_frames.png` (art 205,385) is always shown, also with no hero | A floating piece of the sheet, present even when no hero is chosen; documented as deliberate | medium | medium |
| Above the band | sheet rows art 385..493 are the Base Stats body (`character_layout.base_stats_pos` 141 358, `character_base_stats_layout.list_offset` 160 46, two columns) | `Estate/RealmInventoryPanel.cs:68-69,252-257,636-637`: hero slot face at 14,12, name (`town_roster_name`), class (`town_character_class`), a help line (`town_building_info`) | Invented header where DD1 shows the stats; documented as the mod's own | medium | high |
| "Equipment" title, x | `character_layout.equipment_pos` 141 516 + `character_equipment_layout.title_offset` 323 -12 = 464, 504 sheet px (the right column's `title_offset` 280 lands on its bar's middle, so x is the text's middle) | `Estate/RealmInventoryPanel.cs:64,259-261`: `BandTitle` 465,514 art px = 475,524 sheet px, hard-coded from the art; neither key is read | Title 11 px right of DD1's number (y agrees: a 40 px line from 504) | low | medium |
| Gear level number | `shared/hero/hero.layout.darkest: hero_equipment_layout.level_offset` 90 12 from `weapon_pos` 4 0 / `armour_pos` 95 0 (sheet px 342,562 and 433,562: beside the painted sword / armour glyph, above the slot); font ref `equipment_level` (ubuntu_medium); colours `equipment_level_0..4` (215 213 205, 215 213 205, 105 190 75, 62 114 212, harmful). The mod's raid panel does exactly this (`Dungeon/RaidHeroPanel.cs:142-143`) | `Estate/RealmInventoryPanel.cs:268,646`: style `town_roster_number` (neutral), right-aligned in the slot's lower right corner (slot + 68, + 116) | Other place, other style, no level colour | medium | high |
| Gear tooltip | `hero_equipment_layout.tooltip_offset` 120 52, hotspot 32 52 / 72 144; colours `equipment_tooltip_title` (notable), `equipment_tooltip_body` (neutral) | `Estate/RealmInventoryPanel.cs:271-272`: under the slot (0, 148), "<name>, level n" + the Blacksmith's effect line | Other place; wording the mod's | low | medium |
| Empty trinket slot tooltip | none in DD1's files | `Estate/RealmInventoryPanel.cs:279`: "An empty trinket slot. Click a trinket in the inventory, or drag one here." | Invented | low | high |
| Line under the title used for messages | `trinket_info_description` 150 100 holds the sell instruction / description only | `Estate/RealmInventoryPanel.cs:443-448,483-487`: refusals and results replace it for 6 s (neutral or harmful) | Invented use; documented | low | high |
| Click equips | DD1: drag between inventory and sheet | `Estate/RealmInventoryPanel.cs:342-360`: a click puts on / takes off; drag also works | Extra gesture; documented | low | high |
| Sounds | DD1 exe strings: `/ui/town/trinket_open`, `/ui/town/trinket_close`, `/ui/town/sort_by`, `/ui/town/character_equip`, `/ui/town/character_unequip`, `/ui/town/sell` | `Estate/RealmInventory.cs:230-351`: DD2's own equip / unequip / invalid sounds; nothing on open, close or sort; a sale is silent (`Dd2/EstateAudio.cs:459` rings only while the Nomad Wagon's window is open) | DD1's six sounds are not used here | low | medium |

Matches DD1 (checked): panel `realminv_bg.png` 667x780 at `realm_inventory_pos` 881 128; `content_offset` 0 2; `close_pos` 610 22; `sort_button_tooltip_offset` 16 -12 with `str_sort_trinkets_alphabetically / _by_class / _by_rarity`, `str_unequip_all_trinkets`; overlay `realm_inventory_sort_current_overlay.png` 49x16 at -8 -8 / -8 24; `trinket_info_description` 150 100 with `realm_inventory_trinket_sell_instruction` / `_description`; `trinket_sell_value` 365 116 + `shared/estate/estate.layout.darkest: estate_currency_gold_layout` (icon 0 -12, number 25 -14), `currency.gold.icon.png` 24x24, `town_currency_amount`; grid `number_of_columns` 7, `offset` 80 160, `inventory_grid_size` 560 525 (window ends at 720), `scroll_max_visible_rows` 4 (hard-coded 4); `realminventory_v_grid.png` 404x164 (six lines 80 apart, measured again) mid-gap; rarity cards 72x144 and `trinket_rarity_*` names; the four painted slots of the sheet: mod 271 / 362 / 488 / 579 x 591 art px against DD1's keys 271 / 362 / 488 / 580 x 592 (1 px); font refs `town_character_title` (dwarven_axe_medium, notable), `town_roster_name`, `town_character_class`; string `character_title_equipment`.

Needs eyes
- Where the name stands beside the icon; whether `content_offset` moves the name and the close button too.
- Which of the four small buttons stands where, and whether the row runs left from `sort_button_pos`.
- Where DD1 puts the two 62x49 arrows; how many row rules it draws; whether the rule art is centred in the panel (x 19.5) or starts on a whole pixel.
- Whether "Sell trinket for:" and the price show on plain hover or only with Shift held; whether the instruction and the price share one line (the instruction ends near x 365, where `trinket_sell_value` begins).
- Whether DD1's inventory dims trinkets the open hero cannot wear, and what the sparkle looks like in motion.
- Whether the inventory in DD1 can stand without a sheet (assumed yes above).
- Whether DD1's "Equipment" title really stands 10 px left of its bar's middle (file says 464, bar's middle is 474.5 sheet px).

## 19. Heirloom exchange

Mod: `Estate/HeirloomExchangePanel.cs`, `Estate/HeirloomExchange.cs`, `Core/HeirloomExchange.cs`. DD1: `campaign/town/heirloom_exchange/`, `campaign/heirloom_exchange/heirloom_exchange.json`.

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| Sounds | DD1 exe strings: `/ui/town/heirloom_exchange_open`, `/ui/town/heirloom_exchange_close`, `/ui/town/heirloom_exchange_confirm` | none (`Estate/HeirloomExchangePanel.cs`, `Dd2/EstateAudio.cs:449-459`) | Opening, closing and a trade are silent; documented ("No sound") | medium | high |
| How many may be given | no file (DD1's own arithmetic) | `Core/HeirloomExchange.cs` + `Estate/HeirloomExchangePanel.cs:217-221`: steps through the amounts at least one rate divides | Mod's rule; documented | medium | low |
| Tooltips | none in the layout | `Estate/HeirloomExchangePanel.cs:45,150-155,172-173,240-245,301-319`: tooltips over the panel's top edge for arrows, icon, rows, button, and for a failed trade | Invented; documented | low | high |
| Arrow that leads nowhere / arrows at rest | one art per arrow, no disabled art | `Estate/HeirloomExchangePanel.cs:266-267` + `UI/UiKitWindows.cs:88-93`: 0.4 grey at 70% when disabled, 0.86 grey at rest, full only under the pointer | States painted by tint; at rest the arrows and the confirm button are 14% darker than the art | low | high (mod side) |
| Row that cannot be taken | `frame_invalid_offset` -32 -8, `heirloom_exchange.frame_invalid.png` (no-entry sign painted in) | `Estate/HeirloomExchangePanel.cs:294-298,312`: amount in the mod's dim colour, icon x 0.55, no button | Dimming is the mod's | low | low |
| Amount given when the estate holds fewer | `town_heirloom_exchange_from_amount` neutral | `Estate/HeirloomExchangePanel.cs:265`: `UpgradeUi.Dim` (0.62 0.58 0.5) | Extra state colour | low | high |

The bar's button for this panel (place, spin, sound) is in the Estate summary bar section.

Matches DD1 (checked; an offline composite of the art at the mod's numbers looks coherent, glow under the first column, arrow tips on the rows): panel `heirloom_exchange.background.png` 429x268 at `heirloom_exchange_pos` 340 708, foot behind the bar; `title_pos` 215 24 with `town_heirloom_exchange_title` (ubuntu_medium, notable) and `town_name_heirloom_exchange`; from side `choice_start_offset` 79 110, `choice_spacing` 44 0, `arrow_up_offset` 0 -10, `arrow_down_offset` 0 84, overlays -26 -18 / -26 70 (`selected_overlay.png` 52x52), `icon_offset` 0 32, `text_offset` 0 36 (`town_heirloom_exchange_from_amount`, ubuntu_medium, neutral); to side `choice_start_offset` 256 75, `choice_spacing` 0 44, `arrow_offset` 148 80 (`arrow_0..3.png` 72x180), `frame_offset` / `frame_invalid_offset` -32 -8 (189x56), `heirloom_icon_offset` 0 20, `heirloom_amount_offset` 52 4 (`town_heirloom_exchange_to_amount`, notable), `confirm_offset` 112 20 (`confirm.png` 48x24); `.anim.darkest`: `transition_in_time` / `_out_time` 0.4 with easeOutSine / easeInSine, pulse 0.25 / 0.125 s at 1.25; icons `shared/estate/currency.<id>.icon.png` 40x40; rates from `campaign/heirloom_exchange/heirloom_exchange.json`.

Needs eyes
- The three readings the doc admits: two columns (heirloom, then number), every offset's anchor (middle of a column / of an icon / of the button; top middle of a number), rows in the file's order.
- Whether the panel slides up from behind the bar or is faded in (the file gives times and easing only).
- Whether DD1 dims an arrow that leads nowhere, and whether "selected" on the bar icon means open or under the pointer.

## 20. Town event panel

Mod: `Estate/TownEventPanel.cs`, `Estate/TownEventText.cs`, `Estate/TownEvents.cs`, `Estate/TownEventEffects.cs`. DD1: `campaign/town/town_event/`, `campaign/town_events/*.json`, `fx/estate_town_event`, `fx/town_event_*`.

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| The control that brings the event back | `fx/estate_town_event/estate_town_event.sprite.{atlas,png,skel}`: regions `bell` 83x161, `clapper` 32x46, `scroll_closed` 133x90, `scroll_open` 120x150; animations `idle`, `selected`; DD1 exe strings: the `.skel`, and the key binding `town_toggle_town_event`. It is one of the `estate_*` sprites of the bar's navigation row (`estate_summary.layout.darkest: navigation_button_start_pos` 1800 45, `navigation_button_offset` -110 0), like `estate_activity_log` and `estate_realm_inventory` | `Estate/TownEventPanel.cs:36-37,141-165`: an invented plate at the top of the hamlet (850, -58, 400x76): `town_event.icon.png` scaled from 113 to 76 px, a label at size 24 with the heading at size 20 in a hard-coded #dcb55c | DD1's bell on the bar is missing; the mod's notice is an element DD1 does not have, in sizes and a colour that are not DD1 styles. The doc admits the notice is the mod's own | high | medium |
| Header icon of the panel | `campaign/town/town_event/town_event.icon.png` 113x113; `town_event.layout.darkest: town_event_layout.name_pos` -40 -6 | `Estate/TownEventPanel.cs:207`: text only; the icon is used for the invented plate instead (`:153`) | Corner icon missing on the panel | high | medium |
| Heading's place | `name_pos` -40 -6 (the icon's corner); backdrop rules x 105..407, y 31..40 and 112..121 | `Estate/TownEventPanel.cs:43,207`: `NameCentre` 256 76 hard-coded; `name_pos` is not read | Mod's inference: centred between the rules (fits this backdrop's rules; unproven) | low | low |
| The fallen to choose from ("From Beyond") | `town_event_result_layout`: `recruit_entry_start_pos` 0 32 from `result_pos` 770 610, `recruit_offset` 100 0, `recruit_max_width` 520, `recruit_fade_out_time` 1.0, `recruit_fade_out_easing_function` easeInSine; `shared/hero/roster_icon_frame_town_event.png` 85x85 (DD1 exe strings) | `Estate/TownEventPanel.cs:47,248,266-268,275-296`: buttons 168 px apart centred in 520, a 54 px portrait, name at size 19 and "class, week n" at size 14 in non-DD1 colours; `recruit_offset` and the fade keys are not read | Not DD1's row of 85 px portraits 100 apart from the band's left; no fade of the ones not chosen; invented epitaph line and text sizes | medium | medium |
| Choosing one of the fallen | DD1: the description says only one can return; no dialog strings | `Estate/TownEventPanel.cs:299-317`: a click opens DD2's confirmation dialog with the mod's wording ("Return ... to the living?", "Return" / "Not yet") | Invented dialog | low | medium |
| What the event does: first line's height | `result_pos` 770 610 + `town_event_info_layout.element_start_pos` 250 36 = 1020, 646; `element_text_width` 485; `town_event_info` (ubuntu_small, notable) | `Estate/TownEventPanel.cs:46,247`: a 485x96 box from y 638, text centred in it | A single 25 px line stands at about 673 instead of 646 (27 px lower), two lines 15 px lower; documented as deliberate | low | high |
| Lines DD1 has no words for | no `town_event_info_format_bonus_recruit` / `_dead_recruit` string exists | `Estate/TownEventText.cs:75,95,98,115`: "<class> recruits arrive", "One of the fallen may return to the living", "(used)", "+n% chance to be Resolute"; `Estate/TownEventEffects.cs:57-77,529` notes | Invented info lines (DD1 shows the recruits themselves) | low | medium |
| Payment / confirm of an event | `town_event.confirm_button.png` 64x32; `town_event_layout.currency_cost_pos` 1025 618; `town_event_info_layout.interaction_currency_pos` 262 75 | not built | Missing; documented; no event of the base files uses it | low | high |
| The event's scene in the hamlet | 27 Spine sprites `fx/town_event_<name>/` (animations `idle`, `town_event`), named per event by `campaign/town_events/base.town_events.events.json: sprite` + `sprite_attachment` (statue, tavern, graveyard, stage_coach, nomad_wagon, sanitarium, blacksmith), e.g. `lock_gambling_discount_tavern` -> `town_event_robbery` on the tavern | not drawn (admitted in `docs/recon/town-events.md` section 1) | The week's event leaves no mark on the town view. These are NOT the panel's pictures: the panel uses the static `town_event.image_<id>.png`, and so does the mod | medium | high |
| Title colour | `colours/base.colours.darkest`: `town_event_story_title_good / _bad / _neutral` (all notable); DD1 exe strings: `town_event_story_title_%s` | `Estate/TownEventPanel.cs:225-226`: asks for the id `town_event_story_title`, which does not exist, and falls back to notable | Same colour with stock files; a per-tone colour is never read | low | high |
| Bar clearance | `town_screen_layout.estate_summary_pos` 0 975 + `estate_summary_layout.pos_offset` 0 -17 | `Estate/TownEventPanel.cs:41`: `BarClear` 122 hard-coded | Equal value, not read from the file | low | high |
| Close button at rest | `shared/progression/progression_close.png` as painted | `UI/UiKitWindows.cs:88-93`: 0.86 grey until hovered | 14% darker at rest | low | high (mod side) |

Matches DD1 (checked): backdrop `town_event.background.png` 1395x776 at `town_event_pos` 144 132; crier `town_event.character.png` 811x757 at `character_pos` -5 100; `close_pos` 1352 12; `tone_frame_<tone>.png` 596x645 at `story_pos` 0 0 + `tone_frame_offset` 1030 115 (top middle); title at `title_offset` 1030 150 in `town_event_story_title` (dwarven_axe_medium); picture `town_event.image_<event id>.png` 500x240 at `image_offset` 1030 216: all 48 events of `base` + `mode` (and the 2 arena ones) have a picture under their own id, so no mapping is needed (51 pictures; the one left over is the Crimson Court's); story at `description_offset` 1030 480, `description_text_width` 485, `town_event_story_description` (ubuntu_medium, 21 14 5); info in `town_event_info` (ubuntu_small, notable) centred on x 1020; strings `town_name_town_Event`, `town_event_title_<id>`, `town_event_description_<id>`, `town_event_info_<id>` (5), `town_event_info_format_<effect>`; sound `/town/town_event_display_<tone>` when it comes up (`Dd2/EstateAudio.cs:452`).

Needs eyes
- Where DD1 keeps the bell (which place of the bar's row), when it shows, and its drawn size (the skeleton's own pose: idle = closed scroll 66x45 + bell 42x81; `selected` 3.33 s with the open scroll and a swinging bell).
- Where the heading stands beside the icon.
- Whether the crier is drawn over or under the scroll and its tone frame (the mod draws the frame over him; his art reaches x 806, the scroll begins near 715).
- Whether bonus-recruit events also show their recruits on the scroll (the `recruit_*` keys and the missing info format suggest it), and how one of the fallen is taken (dragged to the roster?).
- Whether the longest stories (181 characters) fit five lines of ubuntu_medium at 1:1; the mod shrinks to 14 px if not.

## 21. Estate Map (quest select / embark)

Mod: `Estate/QuestPanel.cs`, `Estate/QuestMapParts.cs`, `Estate/QuestMapText.cs`, `Estate/QuestBoard.cs`. DD1: `campaign/town/quest_select/`, `campaign/town/embark_party/`, `campaign/town/hero_slot/`, `shared/name/name.layout.darkest`, `shared/progression/`, `shared/dd_effects.darkest`, `shared/party_name/`, `fx/dungeon_progress`, `fx/party_combo`. The mod's map in game: `_lab/shots/f9_embark_after.png`.

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| EM01 bottom bar and estate bar | `campaign/town/town.layout.darkest`: `town_screen_layout.estate_summary_pos 0 975` + `campaign/town/estate_summary/estate_summary.layout.darkest`: `estate_summary_layout.pos_offset 0 -17` = bar `shared/progression/progression_bar.png` (1920x138) at y 958, lit band y 980..1041; `shared/progression/progression.layout.darkest`: `forward_pos 801 984` (`progression_forward.png` 312x52, y 984..1036, inside the band). Gold, heirlooms and the bar's buttons sit on it on the town screen (`dd1_now.png`) | `Estate/QuestPanel.cs:426-427` draws the bar at y = 1080 - 138 = **942** (band 964..1025); the map's backdrop (`QuestPanel.cs:322`) lies over the hamlet's own `EstateSummary`; `QuestPanel.cs:251-259` same height for the corner strip | Bar 16 px too high: "Provision" (984..1036) hangs 11 px under the lit band (seen in `f9_embark_after.png`). No gold, no heirlooms, no bar buttons on the Estate Map. The mod's own hamlet (`Estate/EstateSummary.cs:55-56`) and provision screen (`Dungeon/ProvisionScreen.cs:213`) use 958 | high | high for the geometry; medium that DD1 keeps the currencies on this screen |
| EM02 line above the party tray (party name) | `campaign/town/quest_select/quest_select.layout.darkest`: `town_quest_select_layout.party_name_pos 756 834` + `town_quest_select_party_name_layout.text_offset 190 08`; fonts `town_quest_select_party_name` / `_default` (ubuntu_medium); colours notable / `#5d5a50`; strings (party_names table) `party_name_default` "Build a Party From the Roster", `party_name_0..` ("The Usual Suspects", ...) picked by `shared/party_name/party_name_library.json` (487 class sets); `quest_select.anim.darkest`: `town_quest_select_party_name_anim` fade in 1.0 / out 1.0 easeInOutSine | `QuestPanel.cs:536-539`, `732-768`; `Estate/QuestMapText.cs:196-217`: own sentences ("Choose a party in the roster: up to four heroes." in harmful red, "The party has 3 of 4 heroes.", "A, B, C and D" in neutral, a refusal sentence) on a 60 % black strip 760x30, moved 2 px up, shrinking to 15 px, no fade | Other words, other colours, an invented backing strip, no fade; DD1's default line and party names unused. Documented as deliberate (`docs/recon/dd1-windows.md` §12) | high | high |
| EM03 name of a generated quest | strings `town_quest_name_<type>+<length>+<dungeon>+<goal>`: "Scout" / "Explore" / "Map", "Skirmish" / "Cleanse" / "Exterminate", "Reclaim Relics of the Light", "Purify the Altars", ...; at `town_quest_select_quest_layout.name_text_offset 200 140`, style `town_quest_name`; DD1 exe strings `town_quest_name_%s` | `Estate/QuestBoard.cs:420-431` names them "Explore the Ruins", "Cleanse the Weald", "Gather in the ...", "Rekindle the ..."; `QuestPanel.cs:634` writes that on the name band | The red band shows the mod's phrase instead of DD1's quest name (which only appears in the invented subtitle, EM06) | high | high |
| EM04 screen icon position | `quest_select.layout.darkest`: `town_quest_select_layout.name_pos 104 122`; `shared/name/name.layout.darkest`: `name_layout.icon_offset 60 26` = icon `quest_select.icon.png` (113x113) at 164,148: inside the blank head of `quest_select.questverbose_bg.png` (panel at `description_pos 125 132`, head y 132..272) | `QuestPanel.cs:484` (and `Estate/QuestMapParts.cs:68`): icon's corner at `name_pos` 104,122 itself; `name.layout.darkest` is read nowhere in `src` | Icon 60 px left and 26 px above DD1's seat: it sticks 21 px out of the panel's left edge and 10 px over its top (`f9_embark_after.png`) | medium | medium (the offsets are ignored for certain; that they are the icon's corner is read from the art, see Needs eyes) |
| EM05 screen name position | `name_layout.text_offset 192 118` = text point 296,240 (19 px right of the icon's right edge 277); style `town_name`; string `town_name_quest_select` "Estate Map" | `QuestPanel.cs:485-489`: x = 104 + 113 + 12 = 229 (`TitleGap` 12 is the mod's, `QuestPanel.cs:49`), box y 122..235, middle-left (centre y about 178), shrinks down to 28 | Name starts 67 px further left and sits about 30 px higher than DD1's point; shrinking is the mod's | medium | medium |
| EM06 subtitle line | none: the description starts at `town_quest_select_quest_layout.description_text_offset 34 194`, `description_text_width 340` | `QuestPanel.cs:453` + `QuestMapText.cs:84-87`: "Ruins \| Scout" in `town_quest_camping` style at 34,194, 28 px high (`QuestPanel.cs:51`); description moved to 34,222 (`QuestPanel.cs:455`) | Invented line; DD1's description begins 28 px higher and has 28 px more room | medium | high |
| EM07 difficulty colour | `colours/base.colours.darkest`: `town_quest_difficulty_1` 169 217 102, `_3` 233 170 98, `_5` 255 87 49, `_6` = harmful: same ids as the strings `town_quest_difficulty_N` ("Apprentice (Lvl 1)"); DD1 exe strings `town_quest_difficulty_%d`; `town_quest_specifics_format` "%s \| %s" | `QuestPanel.cs:468`, `QuestMapText.cs:66-73`: the whole line in `town_quest_specifics` (neutral) | The difficulty word is not tinted green / orange / red | medium | medium (the colour ids exist; where DD1 applies them needs a look) |
| EM08 `dd_effect_<dungeon>.png` overlays | `shared/dd_effects.darkest`: `town_quest_select_dd_effect`: `effect_length 0.25`, `min_max_time_in_state 0.05 0.10`, `min_max_scale 1.0 1.20`, `post_dd1_chance 0.10`, `post_dd2 0.15`, `post_dd3 0.20`, `post_dd4 0.25`, `max_offset 5.0`; art crypts 247x209, weald 286x244, warrens 301x217, cove 289x255, darkestdungeon 263x298 at `quest_select_dungeon_layout_<d>.dungeon_effect_overlay_pos` | `QuestPanel.cs:333-337`: all five drawn steadily as soon as the Darkest Dungeon's level is above 0; `dd_effects.darkest` is not read | DD1: a quarter-second random flicker (scale up to 1.2, jitter up to 5 px) whose chance rises with each Darkest Dungeon quest done. Mod: tentacles painted on the map for good | medium | medium to high |
| EM09 region tooltip (level bar) | DD1 exe strings beside the quest select code: `town_quest_progress_format` "Clear a path to the boss! Progress: %3.0f%%", `town_quest_progress_<plot quest>` ("Slay the boss!"), `town_quest_locked`, `town_quest_dungeon_not_released`; colour `quest_select_progress_xpbar_tooltip` (neutral); `town_quest_select_dungeon_layout.dungeon_xp_bar_tt_offset 0 -88` | `QuestPanel.cs:387`, `QuestMapText.cs:219-229`: a heading (the region's name) and the mod's words ("Level 2. 3 more points to level 3: a short quest here counts 2, a medium 3, ...", "N more quests completed anywhere on the estate."); box grows upward from pos -8 (`QuestPanel.cs:61`) | DD1's progress sentence is not used; heading and lock reasons are invented | medium | medium |
| EM10 level bar fill animation and effect | `quest_select.anim.darkest`: `town_quest_select_xp_bar_anim` 2.0 s easeInOutSine; `town_quest_select_dungeon_layout.dungeon_xp_bar_fx_offset 0 4`; `fx/dungeon_progress` (animation "dungeon_progress" 2.0 s: glow 45 to 25 px, sparkle 62 to 36 px); sound `/ui/town/dungeon_progress` (DD1 exe strings) | `QuestPanel.cs:366-373`: a still fill (`fillAmount`); neither the anim block nor `dungeon_xp_bar_fx_offset` is read (`QuestMapParts.cs:72-84`) | No filling bar, no glow and sparkle, no sound when a region has gained progress | medium | medium |
| EM11 party tray: placing heroes, slot states | `campaign/town/hero_slot/`: `hero_slot.positive_frame.png` / `negative_frame.png` (85x85), `hero_slot.positive_prev.png` / `positive_next.png` (24x74) at `town_hero_slot_layout.prev_offset -15 4` / `next_offset 76 4`, `hero_slot.locked_for_hero.png`, `hero_slot.backgroundhightlight.png`, name `hero_slot_name` at `name_offset 45 -70`, `replace_slide_out_anim` 0.2 s offset 0 -50 fade 0.5; DD1 exe strings `HeroDragData`, `/ui/town/character_add`, `character_remove`, `character_show_restriction` | `QuestPanel.cs:499-534`: slots fill from the right in the order picked; a click on a slot sends the hero back; `negative_frame` only on a hero who refuses; tooltip "class, resolve level N ... Click to send back to the roster." | No drag onto a chosen slot, no way to set the marching order, no prev/next arrows, no drop frames, no slide-out; the tooltip is invented | medium | medium |
| EM12 party tray: portrait size | `hero_slot.layout.darkest`: `town_hero_slot_layout.icon_offset 0 0`: the portrait fills the 85x85 slot | `QuestPanel.cs:506-508`: 73x73 at inset 6, aspect kept; the roster and `ProvisionScreen.cs:50`, `265-271` use 98 px in a window inset 3 | Heads on the map's tray are smaller than DD1's and than the mod's own roster and provision tray | medium | high (mod differs from itself) |
| EM13 town event overlay on the tray | `campaign/town/embark_party/embark_party.layout.darkest`: `town_event_overlay_offset 206 124`, `town_event_fade_time 1.0` easeInSine; art `embark_party_buff_affliction_chance.png` (599x325), `embark_party_buff_virtue_chance.png` (599x325), `remove_quest_hero_level_restriction.png` (713x325); DD1 exe strings `campaign/town/embark_party/%s.png` | none, although `Estate/TownEvents.cs:143`, `178` run exactly these events | The sunburst behind the tray (and the crowd of heroes for "Helping Hand") is missing | medium | high |
| EM14 town event icon on a region | `town_quest_select_dungeon_layout.town_event_icon_offset -32 -14`, tooltip hot area -32 -14 / 32 32; `quest_select_town_event.png` (64x64); colour `quest_select_progress_town_event_icon_tooltip` | none | Missing. Documented (`dd1-windows.md` §12) | low | high |
| EM15 town event notice in the details panel | `town_quest_select_quest_layout.town_notification_icon 60 750`, `town_notification_title_text 110 750`, `town_notification_text 110 774`, `town_notification_text_width 400`; `quest_select_town_event_notification.png` (46x66); string `str_quest_select_town_event_title` "Town Event:"; font `town_quest_select_town_event_notification` (ubuntu_small) | none; that strip holds the mod's level-cap notice (EM16) | Missing | low | high |
| EM16 level-cap notice in the panel's foot | nothing stands there as a rule: the strip is for `trinket_retention_description_text_offset 62 750` (width 310), the town notice (EM15) or `continue_quest_text 30 750` (width 450). A hero who will not go says so: dialogue `str_quest_too_easy` ("This quest is beneath my experience." ...) with `/ui/town/character_show_restriction` | `QuestPanel.cs:45`, `480`; `QuestMapText.cs:184-194`: "Apprentice (Lvl 1): heroes of resolve level 0 to 2. The more seasoned think it beneath them." plus retreat rules, at 34,750, style `town_quest_trinket_retention`, neutral | Invented notice; DD1's bark and its sound are not used. Documented as deliberate (`dd1-windows.md` §12) | medium | high |
| EM17 short-handed party, no-retreat and Darkest Dungeon questions | confirm dialog: `town_quest_quest_select_small_team_confirm` with answers `town_quest_select_confirm_yes` "Still Embark" / `town_quest_select_confirm_no` "Cancel Embark"; `town_quest_quest_select_cant_retreat_confirm`, `town_quest_quest_select_darkest_dungeon_confirm` | `QuestPanel.cs:759-763`, `802`, `812-817`: the question is written on the status line and the forward button's word changes from "Provision" to "Still Embark"; no dialog, no "Cancel Embark"; retreat rules are sentences in the foot notice (`QuestMapText.cs:191-192`) | DD1's dialog replaced by a relabelled button | medium | medium to high |
| EM18 forward button "Provision" | `progression.layout.darkest`: `forward_selected_overlay_offset 0 -13` (`progression_forward_selected_overlay.png` 311x24) under the pointer, `forward_text_offset 160 -2`; DD1 exe strings `/ui/town/button_mouse_over_embark` | `QuestPanel.cs:541-549`: no overlay; the label's box is centred on the button's middle (y 26) instead of hanging from -2 (about 3 px higher than the hamlet's Embark, `Estate/HamletScreen.cs:155-157`); grey `#5d5a50` and dead while the party is not ready (`QuestPanel.cs:801-803`) | Hover glow missing (the hamlet's and the provision screen's buttons have it); label seat differs from the mod's two other forward buttons; the greyed state is the mod's | medium | high |
| EM19 party combination effect | `town_quest_select_party_name_layout.change_animation_offset 185 410`; `fx/party_combo` (animation "combo" 1.0 s: bar glow up to 465x42, two ball glows, six sparks); sound `/ui/town/party_comp` (DD1 exe strings) | none | Missing (follows from EM02) | low | high |
| EM20 reward cards: seat and count | `shared/inventory/inventory.layout.darkest`: `inventory_item_layout.icon_offset 4 0`, `amount_text_offset 14 4`; grid `town_quest_rewards_inventory_system_grid_layout.start_pos 20 28`, `offset 80 160` | `QuestPanel.cs:654-655`: card at the cell's corner (no +4); `QuestPanel.cs:665-666`: count at 5,0 in the card | Cards 4 px left of DD1 (row centred on 198 of the 400 px panel instead of 202); count 5 px left and 4 px above where the mod's own `Dungeon/InventoryGrid.cs:64`, `83` puts it | low | high |
| EM21 gold reward art | `shared/inventory/item.display.json`: `gold.icon_thresholds [250, 500, 750, 1000]` choose `panels/icons_equip/gold/inv_gold+_0..3.png`; a quest's 3000+ gold shows the largest pile (`_3`) | `QuestPanel.cs:661`: always `inv_gold+_2.png` | Wrong pile | low | medium |
| EM22 trinket reward card | DD1 draws the trinket's own card `panels/icons_equip/trinket/inv_trinket+<id>.png` (72x144) | `QuestPanel.cs:60`, `676-686`: DD1's `rarity_<rarity>.png` card with DD2's icon at 64 px, or "?" | Documented as deliberate (`dd1-windows.md` §13, `quests-and-trinkets.md` D4) | low | high |
| EM23 reward tooltips | `inventory_item_tooltip_layout.offset 85 0`, `text_width 200` (beside the card); names `str_inventory_title_gold` "Gold", `str_inventory_title_heirloombust` "Bust", ... with `str_inventory_description_*` | `QuestPanel.cs:669`, `701`: box above the card; `QuestMapText.cs:231-234`: "60 gold", "3 deeds" built from the ids | Place and words are the mod's | low | medium |
| EM24 camping tooltip | `town_quest_select_quest_layout.camping_tt_pos 10 375`, `camping_tt_size 80 30`, `camping_tt_offset 90 9`; colour `town_quest_camping_tooltip`; text `town_quest_number_of_camps<N>` | `QuestPanel.cs:461-466`: heading "Camping" (invented), box growing up from the hot area's corner; `camping_tt_offset` is not read (`QuestMapParts.cs:92-94`) | Tooltip seat ignores DD1's offset; heading invented | low | medium |
| EM25 label beside the back arrows | `progression_layout.back_pos 228 82`, `progression_back.png` (32x33); no text entry | `QuestPanel.cs:429-439`: a standing label "Return to the Hamlet" (ubuntu_small) right of the arrows; the hamlet's own arrows and hint were removed in commit a341427 | Invented element | low | high |
| EM26 quest marker tooltip | `town_quest_select_quest_button_layout` has only `length_icon_offset 0 0`, `type_icon_offset 0 0`, `selected_background_offset -94 -94`; hover sound `/ui/town/button_mouse_over` | `QuestPanel.cs:415-419`: tooltip with name, subtitle, specifics and the rewards in words | Invented element (see Needs eyes) | low | medium |
| EM27 marker row at the roster column | `quest_select_dungeon_layout_cove.quest_map_pos 1260 400` (darkestdungeon `1260 100`) + `quest_button_start_offset 160 92`, `quest_button_spacing 76 80` | `QuestMapParts.cs:145-159`, `QuestPanel.cs:348`: a row is pushed left until it ends at 1544 - 33 | A row of four at the Cove or the Darkest Dungeon sits 23 px left of the layout's seat | low | medium |
| EM28 italic narration | DD1's fonts are bitmap fonts without an italic; descriptions are upright | `QuestMapText.cs:96`: a story quest's description is wrapped in `<i>` | Slanted text DD1 never shows | low | high |
| EM29 sounds | DD1 exe strings: `/ui/town/dungeon_progress`, `/ui/town/party_comp`, `/ui/town/button_mouse_over` (markers), `/ui/town/button_mouse_over_embark`, `/ui/town/character_show_restriction`, `/ui/town/character_add_full`, `/ui/town/button_click_locked` | played: `dungeon_select` (`QuestPanel.cs:602`), `embark_button`, `provision_button`, `character_add`, `character_remove` (`Dd2/EstateAudio.cs:450-454`); the others occur nowhere in `src` | Silent hovers, no refusal sound | low | medium |
| EM30 Shrieker and Brigand Incursion on the map (base game) | `quest_select_plot_crow_trinket.png`, `quest_select_plot_trinket_retention_0..2.png` (48x48; exe `quest_select_%s.png`), `trinket_retention_description_text_offset 62 750` / width 310, `town_quest_trinket_retention_description_format`; `quest_select_dungeon_layout_town` (`quest_map_pos 1260 750`, overlay `630 728`) with `town_quest_select_town_event_plot_quest_plot_town_invasion_0_confirm` | none (`QuestMapParts.cs:27-34`: five regions) | Not ported | low | high |

Matches DD1 (checked, no row): `quest_map_pos` of the five regions; `background_offset -5 -10` with
`dungeon_progressionbar.png` / `dungeon_no_progressionbar.png` (282x84); `dungeon_level_offset 219 20` (centre of the skull's
mouth); `dungeon_xp_bar_offset 14 32`, `dungeon_xp_bar_size 194 8`, gradient `#8e0300` to `#eb1600` over black;
`locked_icon_offset 184 -10` (`locked_dungeon.png` 72x72); hot area `14 0` / `232 48`; markers
`quest_select_<type>_<difficulty>.png` (48x48) on `quest_select_length_<generated|plot>_<length>.png` (96x96), rows of four;
`selected_background_offset -94 -94` with `quest_select_selected.png` (192x192); pulse 0.6 s, scale 0.2, sine;
`description_pos 125 132` with `quest_select.questverbose_bg.png` (400x843); `name_text_offset`, `specifics_text_offset`,
`camping_text_offset`, `goals_text_offset`, `goal_description_text_start_offset`, `rewards_text_offset`, reward grid of four
centred columns at pitch 80; `embark_party_pos 754 871` + `background_offset -1 0`, slots `22 16` every `93`;
`back_pos 228 82`; `forward_pos 801 984`; every `town_quest_*` / `town_quest_select_*` font and colour id; strings
`town_name_quest_select`, `town_progression_forward_provision`, `town_quest_goals`, `town_quest_rewards`,
`town_quest_length_N`, `town_quest_difficulty_N`, `town_quest_specifics_format`, `town_quest_description_*`,
`town_quest_goal_start_*`, `town_quest_number_of_camps*`.

Needs eyes:
- `name_layout` anchors: is `icon_offset 60 26` the icon's corner (then the icon lies neatly inside the panel's head, as
  assumed in EM04) or its centre; is `text_offset 192 118` the foot, the middle or the top of the name's line (top would run
  into the red name band at y 272).
- Does the estate's name plate (crest and band) stay on screen behind the map and the provision screen? The mod covers it.
- Is the details panel shown before a quest is clicked? The mod always preselects one (`QuestPanel.cs:314-317`).
- Second half of EM01: gold, heirlooms and the bar's buttons on the Estate Map.
- Region name: the mod right-aligns it to end at +190 (the ink smudge of `dungeon_progressionbar.png` darkens towards the
  skull, which fits). `shared/tutorial_popup/tutorial_popup.quest_select.png` shows "Weald" at the bar's left end, but that
  picture's marker pitch (about 89) does not fit today's 76, so it may be older than the layout.
- Marker rows: centred on `quest_button_start_offset` (mod) or starting there. In the tutorial picture a row of four lies
  roughly centred under bar and skull.
- What DD1 writes beside the campfire ("0", "x1", nothing for 0 camps).
- EM07: is the difficulty tinted in the specifics line (and on the provision screen)?
- When the level bar fills and the `dungeon_progress` effect plays (on return from a quest?); where the `party_combo` glow
  lands (its bar lies about 394 px from the skeleton's origin, the layout offset is 185 410).
- How often the `dd_effect` flicker comes.
- Tray: which slot a hero takes, the fill order (mod: from the right), whether a name shows over a slot, what the
  forward button looks like with an empty party.
- Locked region: the mod writes its name in neutral grey (`QuestPanel.cs:361`); the style's colour is notable.
- Read by DD1's exe, unused by the mod, purpose not clear from files: `dungeon_heirloom_start_offset 160 -14` /
  `dungeon_heirloom_spacing 38 0` (would collide with the name), `all_quest_map_pos` / `all_quest_number_of_quests_in_row 11`,
  `has_visible_progression`.
- Does DD1 show anything when the pointer rests on a quest marker (EM26)?

## 22. Provision screen

Mod: `Dungeon/ProvisionScreen.cs`, `Dungeon/InventoryGrid.cs`, `Dungeon/InventoryContent.cs`. DD1: `campaign/town/provision/`, `shared/inventory/inventory.layout.darkest`, `shared/name/name.layout.darkest`, `campaign/provision/provision.json`.

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| PV01 forward button's word | strings `town_progression_forward_set_off` "Set Off" (beside `town_progression_forward_embark` "Embark" for the town and `town_progression_forward_provision` "Provision" for the map); DD1 exe strings list the three with `/ui/town/embark_button`, `/ui/town/provision_button`, `/ui/town/set_off_button` | `Dungeon/ProvisionScreen.cs:233`: `town_progression_forward_embark` "Embark" (while `Dd2/EstateAudio.cs:125` plays `set_off_button`); `docs/recon/dd1-raid-ui.md` §6 records "Embark" as DD1's | The button says "Embark" where DD1 says "Set Off" | high | medium to high |
| PV02 screen icon position | `campaign/town/provision/provision.layout.darkest`: `provision_layout.name_pos 104 126` + `shared/name/name.layout.darkest`: `name_layout.icon_offset 60 26` = `provision.icon.png` (113x113) at 164,152, in the dark corner of `provision.character_background.png` (at 144,132) | `ProvisionScreen.cs:191`: corner at 104,126 | Icon 60 px left and 26 px above DD1's seat: 40 px outside the backdrop's left edge, 6 px over its top | medium | medium |
| PV03 screen name position | `name_layout.text_offset 192 118` = 296,244; style `town_name`; `town_name_provision` "Provision" | `ProvisionScreen.cs:192-195`: x = 104 + 113 + 14 = 231 (`TitleGap` 14 is the mod's, `ProvisionScreen.cs:49`), line box y 151..215 | Name starts 65 px further left and about 30 px higher | medium | medium |
| PV04 shelf price | `shared/inventory/inventory.layout.darkest`: `inventory_item_layout.cost_offset 37 157`; a price in DD1 is a currency widget: `shared/estate/estate.layout.darkest`: `estate_currency_gold_layout.icon_offset 0 -12`, `number_offset 25 -14` (`shared/estate/currency.gold.icon.png` 24x24 + `town_currency_amount`) | `ProvisionScreen.cs:158-163`: the number alone, centred on 37,157; the mod's other windows put DD1's coin beside a price (`UI/UiKitWindows.cs:214-217`) | No coin beside the prices | medium | medium |
| PV05 estate bar | `estate_summary_layout`: gold, then bust / portrait / deed / crest (`large_currency_spacing 200 0`, `currency_spacing 74 0`) and the bar's buttons | `ProvisionScreen.cs:207-221`: a copy of the bar with the gold only | Heirlooms and buttons missing. Documented as deliberate (`dd1-raid-ui.md` §7) | medium | high |
| PV06 bill beside the gold | none: DD1 takes the gold as things are bought | `ProvisionScreen.cs:52`, `221`, `337-338`: gold shown after the bill, and "of N, after M for provisions" / "nothing bought yet" at 410,1002 | Invented text. Documented as deliberate | medium | high |
| PV07 "Standard Kit" | none | `ProvisionScreen.cs:53-54`, `236-245`: a text button at 1190,990 with a tooltip | Invented element. Documented as deliberate | medium | high |
| PV08 roster column | `town_screen_layout.roster_list_pos 1550 0`; PSN string `provision_trigger_r` "(R2 HOLD) Roster Focus"; string `town_provision_no_trinkets_equipped` | `ProvisionScreen.cs:91-94`: an ink backdrop over the whole hamlet canvas, roster included; the bill and purse painted into `provision.background.png` at x 1530..1880 show instead | No roster on this screen. Documented as deliberate (`dd1-raid-ui.md` §7); see Needs eyes | medium | medium |
| PV09 label beside the back arrows | `progression_layout.back_pos 228 82`; no text entry | `ProvisionScreen.cs:292-296`: standing label "Return to the Estate Map" | Invented element | low | high |
| PV10 empty tray slots | `embark_party_layout.hero_slot_start_offset 22 16`, `hero_slot_spacing 93 0`: four `hero_slot.background.png` | `ProvisionScreen.cs:260-264`: a slot is drawn only for each hero present | With fewer than four heroes the left slots are bare tray | low | high |
| PV11 tray tooltip | none | `ProvisionScreen.cs:273-282`: name, class and "Brings N ..." | Invented. Documented as deliberate | low | high |
| PV12 scouting stat | `provision_layout.scouting_stat_pos 1380 96`; font `scouting_text` (dwarven_axe_medium), colour notable, string `str_scouting` | not drawn | Missing. Documented (`dd1-raid-ui.md` §6, §7) | low | medium |
| PV13 item tooltip words | `str_inventory_title_*` + `str_inventory_description_*` ("Use to stanch the flow of bleeding."), `str_inventory_gold_value_format` "[Value: %s Gold Each]" | `Dungeon/InventoryContent.cs:84-96` own sentences; `ProvisionScreen.cs:352`: "N gold, M to a slot" | The mod's wording instead of DD1's description | low | high |
| PV14 keeper's Darkest Dungeon flicker | `provision.dd.character.png` (811x757); `shared/dd_effects.darkest`: `provision_dd_flicker_animation` (`effect_length 0.25`, chances 0.01 / 0.05 / 0.08 / 0.10, scale 1.0..1.2, `max_offset 5.0`) | `ProvisionScreen.cs:147`: `provision.character.png` only | Missing | low | medium |
| PV15 card that cannot be bought | `colours/base.colours.darkest`: `inventory_unselectable .rgba #666 .saturation 0.2` | `Dungeon/InventoryGrid.cs:115`: tinted `#666`, colour kept | Not desaturated | low | medium |
| PV16 question about trinkets | `town_provision_no_trinkets_equipped` "Your party is not fully outfitted with trinkets. Really embark?" | `ProvisionScreen.cs:405-415`: only the food question | Not asked | low | medium |
| PV17 "cannot buy" messages | DD1 exe strings beside `panel.town.provision_select.cpp`: `/ui/town/button_click_locked`, `not_enough_room_confirm_provisions` | `ProvisionScreen.cs:55`, `356-361`, `382`, `392`: a tooltip box at 1160,900 for 4 s: "The shelf is bare.", "The purse does not stretch to it.", "That is the party's own: it stays in the bag." | Invented messages; DD1's locked click sound unused | low | medium |
| PV18 buying and selling sounds | DD1 exe strings `/ui/town/buy`, `/ui/town/sell` | `EstateAudio.cs:458` plays `buy` only when the purse drops, and the purse drops on setting out (`ProvisionScreen.cs:418-424`), when the screen is already closed | A click on a shelf or on the bag is silent | low | medium |

Matches DD1 (checked, no row): `town_background_layout.area_pos 144 132` / `character_pos 132 240` with
`provision.background.png`, `provision.character_background.png` (1395x776), `provision.character.png`;
`provision_store_background_layout.pos 814 144`, grid 7 columns, `start_pos 120 20`, `offset 80 170`
(`inventory_grid_background_store.png` is an empty picture); `provision_party_background_layout.pos 800 532`, grid 8 columns,
`start_pos 60 28`, `offset 80 160`, 16 slots over `inventory_grid_background_party.png` (720x360);
`inventory_item_layout.icon_size 72 144`, `icon_offset 4 0`, `amount_text_offset 14 4`; tooltip `offset 85 0`, width 200;
shelf order of `campaign/provision/provision.json`; item art under `panels/icons_equip/`; `provision_sell_back_info` (string,
ubuntu_medium, `#5d5a50`) at 1164 510; `provision_quest_info_dungeon_name` / `provision_quest_info_specs` with
`dungeon_name_<id>` and "%s \| %s"; price colours `town_currency_amount` / `town_currency_cant_afford_amount`; tray art and
slot seats; bar at y 958; forward art at 801 984 with glow at 0 -13 and label at 160 -2; back arrows at 228 82; the food
question `town_provision_not_enough_food_confirm_format` with Yes / No.

Needs eyes:
- PV08: is the roster column on DD1's provision screen (then the painted bill and purse lie behind it)?
- PV01: the word on the button, to be sure.
- PV02 / PV03: the name widget's anchors (as on the Estate Map).
- PV04: coin beside each price, and whether 37,157 is the price's middle.
- Shelves: one card per item with the whole stock (mod) or several stacks cut by the stack limit.
- How DD1 buys several at once; the mod's right click for five (`ProvisionScreen.cs:57`, `371-375`) is its own.
- `quest_info_pos 1300 96` / `quest_specs_offset 20 -5`: the mod ends the dungeon's name at 1300 and stands both lines on
  their feet; `scouting_stat_pos 1380 96` would then lie on the specs, which hints at another reading.
- What an item's tooltip says in the shop (price line, description).

## 23. Tooltips (the box every screen shares)

Mod: `UI/UiKitWindows.cs` (`Dd1Tooltip`). DD1: `shared/tooltip/tooltip.layout.darkest`, `tooltip_background.png` 128x128.

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| Box and text offsets | `tooltip_background_layout.offset 12 -12`, `.text_offset 4 0` (besides `border_texture_threshold 0.15`, which the mod reads) | `UiKitWindows.cs:233-235`: its own `PadX 18`, `PadY 14`, `Margin 6`; the two keys are not read | Hard-coded paddings; the 12, -12 shift of the box from the place a layout gives is not applied | low | low |
| Width of a tooltip | per screen in DD1 (`tooltip_text_width` keys: 300, 250, 220, 200, 280; `tooltip_is_auto_width`) | `UiKitWindows.cs:234, 291-294`: 300 by default and always shrunk to the words | Every tooltip is "auto width"; DD1 has fixed-width ones (`tooltip_is_auto_width 0` for building upgrades and the blacksmith) | low | medium |

Needs eyes: the text's distance to the box's edge in DD1 (the tutorial picture `tutorial_popup.stage_coach2.png` shows about 10 px inside the hairline, which the mod's 18 px from the art's edge roughly gives).

## 24. Narration (the Ancestor's subtitle band)

Mod: `Estate/NarrationBox.cs`. DD1: `shared/app.darkest` (`s_TownSubtitleContextTunables`, `s_SubtitleGlobalTunables`), `shared/subtitles/subtitle_bg.png` 1920x240, style `subtitle_context_town` (DwarvenAxe large, notable).

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| Band's darkness | one `subtitle_bg.png` | `NarrationBox.cs:32-34, 87-88`: the art four deep | Much darker band than DD1's. Documented as deliberate (the bar's gold and Embark read through DD1's) | medium | high |
| Coming and going | `s_SubtitleGlobalTunables.background_fade_in_time 0.50`, `background_fade_out_time 0.50` | `NarrationBox.cs:61, 70`: shown and hidden at once | No fade | low | high |
| "click to continue" | none | `NarrationBox.cs:36-37, 91, 112`: Ubuntu small hint in the band's corner | Invented element | low | high |
| How long a line stays | DD1 takes a subtitle away with its voice line | `NarrationBox.cs:115-120`: stays until a click (or `KeepUntil`) | Other rule. Documented | low | medium |
| Long texts | `max_width 1280`; nothing about height | `NarrationBox.cs:35, 104-109`: a text of more than two lines keeps 24 px from the screen's foot and pushes the band up | Invented rule (DD1's lines are one or two long) | low | medium |

Matches DD1 (checked): `screen_position 960 990`, `max_width 1280`, `background_y_offset 100`, art, style and colour.

## 25. Hero / character sheet

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| The sheet itself | `shared/character/character.layout.darkest` on `characterpanel_bg.png` + `characterpanel_frames.png` (1395x776) at `shared/ui.layout.darkest: ui_screen_layout.character_pos 144 132`: name `name_pos 76 26` (`town_character_name`), rename `rename_icon_pos 32 38` (`icon_rename.png`), dismiss `dismiss_hero_icon_pos 32 78`, class `class_pos 76 80`, status with resolve bar and stress `campaign_status_pos 93 117`, the hero's figure `hero_pos 98 700`, quirks `quirks_pos 141 128` (lists at 100 56 and 565 56, 30 px apart), base stats `base_stats_pos 141 358`, equipment `equipment_pos 141 516`, position and target pips `hero_pips_pos 846 145` / `target_pips_pos 1153 145`, combat skills `combat_skill_grid_pos 780 156` (80x100 step, selection ring `selected_ability.png`), camping skills `780 320`, resistances `780 436`, diseases, palette `palette_icon_pos 20 720` (`icon_recolor.png`), previous and next hero `1162 772` / `1246 772` (`previous_hero.png`, `next_hero.png` 80x49), close `close_pos 1344 18` | `RosterPanel.cs:574`: `CommonUiBhv.ShowCharacterSheet(...)`, DD2's own sheet. The mod only shows a cut-out of DD1's equipment band beside the Trinket Inventory | The DD1 sheet is not rebuilt: a DD2-styled screen opens inside the DD1 hamlet. With it are missing DD1's rename, recolour, previous/next hero and the sheet's own dismiss. Documented as a deliberate reuse (MODLOG "DD2-native screens reused") | high | high |

## 26. Confirmation dialogs on the town side

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| The dialog | `shared/confirm_dialog/confirm_dialog.layout.darkest` (`base_pos 960 200`; question `text_offset -18 200`, `text_width 520`; answers `start_offset 0 390`, `spacing 0 55`, `button_size 500 38`, `selected_overlay_offset 0 -24`), `confirm_dialog.background.png` 840x600, `confirm_dialog.answer_text_selected_overlay.png` 466x81 | DD2's `CommonUiBhv.ShowConfirmationDialog` at `Estate/RosterLifecycle.cs:288` (dismiss), `NomadWagonPanel.cs:260` and `RealmInventoryPanel.cs:429` (sell a trinket), `TownEventPanel.cs:307` | A DD2-styled dialog inside the DD1 hamlet. The mod already has DD1's dialog for the raid (`Dungeon/RaidOverlays.cs:540-590`, numbers in `RaidLayout.cs:266-273`): the town does not use it | high | high |
| Dismissing: words | `str_hero_dismiss_confirm` "This will dismiss the hero permanently. Are you sure you want to dismiss this hero?", `_yes` "Yes", `_no` "No"; `str_hero_dismiss_warning_confirm` (fewer than four heroes left); `str_hero_cant_dismiss_confirm` + `_ok` (the last hero) | `RosterLifecycle.cs:288-293`: "Dismiss <name>?", the mod's sentence, "Dismiss" / "Keep"; the last hero gets no dialog (`:278`, the control is simply absent) | The mod's words where DD1 has its own | medium | high |
| The options menu behind the candles | DD1's own menu (`shared/menu/menu.layout.darkest`; seen on `_lab/shots/candles_menu.png`) | `ActivityLogTownPanel.cs:323-324`: DD2's pause menu (`CommonUiBhv.TogglePauseMenu`) | A DD2 screen behind a DD1 button. Deliberate (the options are the game's) | low | high |

## 27. Hover feedback and UI sounds (all town screens)

DD1's layout files name no sounds; its executable does. Town events found in the exe's strings: `/ui/town/button_click`, `button_click_back`, `button_click_locked`, `button_invalid`, `button_mouse_over`, `button_mouse_over_3`, `button_mouse_over_embark`, `button_mouse_over_town`, `building_zoomout`, `buy`, `buy_free`, `sell`, `character_add`, `character_add_full`, `character_remove`, `character_pickup`, `character_equip`, `character_unequip`, `character_show_restriction`, `dungeon_select`, `dungeon_progress`, `party_comp`, `embark_button`, `provision_button`, `set_off_button`, `heirloom_exchange_open`, `heirloom_exchange_close`, `heirloom_exchange_confirm`, `page_open`, `page_close`, `sort_by`, `trinket_open`, `trinket_close`; shared: `/ui/shared/button_scroll_up`, `button_scroll_down`, `window_popup`, `text_popup`.

| element | DD1 source (file: entry) and value | mod (file:line) and value | deviation | severity | confidence |
|---|---|---|---|---|---|
| Pointer-over sounds | `/ui/town/button_mouse_over`, `button_mouse_over_3`, `button_mouse_over_embark`, `button_mouse_over_town` | none anywhere (`UI/UiKit.cs` `UiHover` is silent) | No hover sound on any button or building | medium | medium |
| Click sounds by kind | `/ui/town/button_click_back`, `button_click_locked`, `button_invalid` | `UiKit.cs:260`: one sound, `ui/town/button_click`, for every `UiKit.Button`; controls made with a bare `Button` are silent (roster sort `RosterPanel.cs:207`, scroll arrows `:223`, dismiss `:549`, the bar's buttons `ActivityLogTownPanel.cs:411`) | No "back", "locked" or "invalid" sound; several controls silent | medium | medium |
| Screen and item sounds | `page_open` / `page_close`, `trinket_open` / `trinket_close`, `heirloom_exchange_open` / `_close` / `_confirm`, `sort_by`, `character_equip` / `character_unequip`, `character_pickup`, `character_add_full`, `character_show_restriction`, `buy_free`, `dungeon_progress`, `party_comp` | played by the mod: `button_click`, `building_zoomout`, `embark_button`, `provision_button`, `set_off_button`, `character_add`, `character_remove`, `buy`, `sell`, `dungeon_select` (`Dd2/EstateAudio.cs:125, 449-459`, `QuestPanel.cs:602`) plus building sounds `town/...` | The events in the DD1 column are never played | medium | medium |

Needs eyes (ears): which control plays which of the events above.

## DLC and modes: ask

Not audited; listed so that each is decided on purpose. The Courtyard, the Farmstead and the Butcher's Circus are out on the owner's word.

**Chrome (name plate, bar, roster).**

- DLC, ask: **Districts** (`campaign/town/district/*`, `fx/estate_districts`, `fx/purchase_district`, `shared/estate/currency.blueprint.icon.png`, styles `town_district_*`, `str_districts_locked_tooltip`, sounds `/ui/town/district_open`, `district_close`): the fifth place of the bar's row (x 1360 on the DD1 frame) and a screen.
- DLC, ask: **Shards** on the bar and in the wagon (`shared/estate/currency.shard.icon.png`; the Color of Madness copy of `estate_summary.layout.darkest` moves `currency_pos` to 180 and the exchange icon to 755; `com.heirloom_exchange.json`; `/ui/town/buy_shard`; `fx/estate_merchant_swap`).
- Excluded by the owner, not listed further: the Crimson Court's town parts (`dlc/580100_crimson_court/.../campaign/town/infestation/infestation.layout.darkest`, its provision and quest-select layouts; the spiked red gauge by the bridge on the DD1 frame is probably this), the Farmstead's quest-select layout, everything of `dlc/1117860_arena_mp` (Butcher's Circus).
- Base-game modes, ask: Stygian / Bloodmoon and Radiant change the chrome (`estate_nameplate_ng.png`, `estate_nameplate_rm.png`, `radient_icon.png`, the log's "Week: n/m" at `activity_log_layout.new_game_plus_week_count_pos 310 120`, the graveyard's "Deaths: n/m" at `new_game_plus_death_count_pos 650 84`).
- Controller-only parts (`*_input_preview_*`, `*controller*` keys, `overlays/button_*.png`) are not built anywhere in the mod.

**Building windows, upgrade pane, Stage Coach, Tavern, Abbey, Sanitarium.**

- Stage Coach "Rescued!": `stage_coach.layout.darkest: hero_layout.hero_rescued_text` 400 17, style `stagecoach_prisoner_rescued` (ubuntu_medium, notable), string `str_stagecoach_prisoner_rescued`. Prisoners are in the Crimson Court and Color of Madness map generators.
- Recruit switch: `building.layout.darkest: building_store_list_layout.increment_current_item_display_button_pos` -490 632; DD1 exe strings `stage_coach/hero_recruit_switch.png` (not in this install) and `comet_hero_recruit_switch.png` / `comet_hero_recruit_character.png`.
- `str_stagecoach_roster_limit_quirk_full_rejection`, `rosterelement_outline_roster_limit_quirk.png` (heroes with a roster limit).
- `%s/%s/%s.secondary.character.png` (a second keeper picture).
- Infestation banner on building screens: `building_infestation_layout.banner_pos`, `campaign/town/infestation/`.
- Districts: `campaign/town/district/`, `town_name_district`, `tutorial_popup.districts*.png`, `shared/estate/currency.blueprint.icon.png`; `currency.shard.icon.png`.
- Butcher's Circus entries in `town.layout.darkest` (`circus_layout`) were ignored as told.

**Hero screens, Nomad Wagon, Graveyard, Memoirs.**

- **Nomad Wagon, shard store (Color of Madness):** `dlc/735730_color_of_madness/campaign/town/buildings/nomad_wagon/com.nomad_wagon.layout.darkest` (`comet_layout`, `comet_inventory_system_grid_layout`), `trinket_supply_switch.png`, `comet_trinket_supply_switch.png`, `comet_trinket_supply_character.png`; the switch button's place is in the base file (`building.layout.darkest: building_store_list_layout.increment_current_item_display_button_pos -490 632`); font ref `town_nomad_shard_number`; `shared/estate/currency.shard.icon.png`.
- **Graveyard (Crimson Court):** `dlc/580100_crimson_court/.../buildings/graveyard/final_dd_quirk_evolution_death_disease_vampire_wasting.png`, strings `str_death_quirk_evolution_death_%s`.
- **Memoirs (Crimson Court, Color of Madness):** their own `statue_media_info.json`, `portrait_baron.png`, `portrait_viscount.png`, `portrait_countess.png`, `portrait_fanatic.png`, `plot_crimson_court_0.png`.
- **Trinket rarity cards of DLC:** `rarity_courtier.png`; (`rarity_crow`, `rarity_madman`, `rarity_collector`, `rarity_kickstarter` are base files the wagon never sells).
- **Hero classes that live in DLC folders but exist in DD2:** Flagellant (`dlc/580100_crimson_court/features/flagellant`), Duelist and Runaway (`dlc/4964110_fires_edge/features/duelist`, `.../runaway`), also Shieldbreaker and Musketeer. Their `<class>_guild_header.png`, `icons_equip/` and `action_verbose_body_<building>_<class>` texts are there. The mod's `WindowText` reads `localization/heroes.string_table.xml` and the main table only (`Estate/RosterWindow.cs:485-486`, `:512-520`), so these classes get no DD1 text in the banner's column although the install has one; `dd1-windows.md` section 6 calls them classes "DD1 never had".
- **Controller-only elements** (`town_hero_slot_layout.controller_button_bottom_offset` / `..._top_offset`, every `*_input_preview_*` key): not built, by design of the brief.

**Activity Log, Trinket Inventory, heirloom exchange, town event.**

- `campaign/town/town_event/town_event.image_cc_districts_unlock.png`, string `town_event_info_cc_districts_unlock` (Crimson Court).
- `town_event.interaction_redirect_to_circus.png` 141x141, `town_event_info_layout.interaction_redirect_pos` 40 76, events' `element_start_pos_override` / `element_text_width_override`, strings `town_event_tooltip_arena_*`, font ref and colour `town_event_tooltip`, `campaign/town_events/arena.town_events.events.json` (Butcher's Circus).
- Log entry art DD1's exe names but the base install lacks: `activity_log/quirk_contagion_entry_backdrop`, `shards_consumed_entry_backdrop`, `shards_consumed_log_icon.png`, `quirk_evolution_death_entry_backdrop`, `quirk_transition_entry_backdrop`, `trinket_activity_entry_backdrop`.
- Realm inventory: keys `hold_to_unequip_circus_*`, `sort_circus_compact_input_preview_pos`; exe names `compact_realm_inventory.layout.darkest` and `realm_inventory/hero_stats_background.png` (not in the base folder).
- Heirloom exchange: the fourth row and `arrow_3.png` only fill with the Color of Madness rates (`com.heirloom_exchange.json`, shards); `shared/estate/currency.%s.exchange_icon.png` exists only as the DLC's `currency.shard.exchange_icon.png`.
- Rarity cards `rarity_courtier`, `rarity_madman`, `rarity_collector`, `rarity_crow`, `rarity_kickstarter`, `rarity_ancestral_shambler`.

**Estate Map, Provision screen, hamlet scene.**

- `town_quest_select_dungeon_layout.roaming_icon_offset 9 -8`, `roaming_icon_space_between -30 0`, `roaming_tooltip_offset 0 70`
  (roaming boss icons on a region).
- `town_quest_select_quest_layout.wave_*` keys, `endless_wave_reward.png` / `endless_wave_highscore.png`, fonts
  `town_quest_select_endless_wave_highscore*` (Color of Madness).
- `town_quest_select_infestation_layout`, `provision_infestation_layout`, `campaign/town/infestation/`,
  `town_quest_select_dungeon_estate_inventory_dependency_layout` (Crimson Court).
- `town_quest_select_quest_modifier_layout`, `modifier_background.png`, fonts `town_quest_modifier_*` (the art is not in the
  base folder).
- `circus_layout`, `campaign/town/party_setup/`, `party_builder/party_name_background.png`,
  `town_progression_forward_fight` and its kin (Butcher's Circus).
- Districts (`fx/estate_districts`, `town_name_district`) and the shard currency.
- The large spiked red marker by the bridge in `_lab/shots/dd1_now.png`: not from `town.layout.darkest`'s base blocks; it
  looks like the Crimson Court's gauge.
