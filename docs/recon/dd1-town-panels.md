# DD1 UI: the town screens that are not buildings

Three pieces of DD1's town screen and their buttons on the estate's bar: the heirloom exchange ("Trade
Heirlooms"), the Activity Log and the Trinket Inventory ("realm inventory"). Per piece: what DD1's files say,
how each number is read, what the mod draws, where it differs from DD1 and why. Companion to `dd1-ui.md` (fonts,
text styles, the hamlet's chrome) and `dd1-windows.md` (the buildings' windows).

All numbers are DD1's stock values in pixels of its 1920x1080 screen, y down; the code reads them from the
player's install at runtime and keeps the stock value as a marked FALLBACK. Layout files are under
`campaign/town/` unless a path says otherwise. MEASURED = read off DD1's art, MOD = the mod's own number.

Offline previews (all in `_lab/preview/`, 1920x1080): `python tools/preview_town_panels.py` writes
`town_bar_buttons.png`, `town_heirloom_exchange.png`, `..._uneven.png`, `..._poor.png`, `town_activity_log.png`,
`..._scrolled.png`, `town_realm_inventory.png`, `..._refused.png`, `..._selling.png`, `..._empty.png`.

**None of this has been seen in the game.** Section 7 lists what only the game can confirm. DD1 ships no picture
of any of the three screens (its tutorial pictures show none), so every reading below rests on the layout
numbers, the art and the strings alone; where two readings were possible the one chosen is argued.

## 0. Decisions that hold for all three

| Question | Decision |
|---|---|
| What are they, next to a building's window? | `TownPanel` (`Estate/ActivityLogTownPanel.cs`): a screen in the hamlet's canvas with its parts at DD1's own places (`town.layout.darkest` `town_screen_layout`: `activity_log_pos` 144 132, `realm_inventory_pos` 881 128, `heirloom_exchange_pos` 340 708). A `RosterWindow` is a building's backdrop at `area_pos`; these are not. One panel is open at a time, never together with a building's window; it closes when a building, the estate map, the provision screen or the town crier comes up, and with the hamlet. |
| Where do they open from? | **The estate's bar, as in DD1**, with DD1's own buttons (`TownPanelButtons`, same file). The mod adds them itself from an `[EstateModule]` that waits for the hamlet's canvas: no line of `EstateSummary.cs` or `HamletScreen.cs` had to change. They are a child of the canvas just above the bar and Embark, below everything that opens later (windows, the estate map, the narration band). |
| The estate's bar and the roster | Stay lit and in reach: a panel's root ends where the roster column and the bar begin, like a window's. The exchange slides up from behind the bar; a mask at the bar's top edge does what DD1's bar does by lying over it. |
| Screens that are not 16:9 | The log keeps the middle of the room left of the roster (as a window), the inventory and its hero band keep their distance to the roster, the exchange keeps to the screen's left edge with the currencies; the first two shrink with a room narrower than 1550. |
| Right click | Closes the panel (DD1: back). The exchange sinks back behind the bar first. |
| Ellipsis | No label uses one: one line shrinks (`Dd1Ui.Line`), a paragraph wraps and shrinks (`Dd1Ui.Block`), the log's lines are as tall as their words. |
| Saving | A trade saves at once (`EstatePersistence.SaveNow("heirlooms")`); a sale is the wagon's (`"wagon"`); trinkets moved on and off heroes are saved once, when the inventory closes (`"trinkets"`), not at every drag; the log travels in every save as the section `activity_log`. |

### The buttons on the bar (`estate_summary/estate_summary.layout.darkest`, `estate_summary_layout`)

| DD1 element | Source | Mod element |
|---|---|---|
| "Trade heirlooms" icon | `heirloom_exchange_offset` 700 51 from the bar's corner (0, 958); `heirloom_exchange/he_icon_idle.png`, `he_icon_selected.png` (58x59) | Same art. The gold "idle" icon at rest, the grey "selected" one while the exchange is up |
| Row of screens at the bar's right end | `navigation_button_start_pos` 1800 45, `navigation_button_offset` -110 0; `activity_log/activity_log.icon.png`, `realm_inventory/realm_inventory.icon.png` (113x113; DD1's third, `glossary/glossary.icon.png`, has no screen in the mod) | Log at the row's end (1800), inventory next (1690) |
| The button under the pointer | `selected_overlay_offset` 0 0; `estate_summary.selected_overlay.png` (103x139: a gold rule with a diamond, and a rule below) | Same art, centred on a row button: its rules fall on the icon's top and bottom edge |
| Name of the screen over a button | `tooltip_offset` 0 -48, `navigation_button_tooltip_offset` 0 -28; `town_name_heirloom_exchange`, `town_name_activity_log`, `town_name_realm_inventory` | DD1's tooltip box with that name, standing on the button's top edge (and, when no panel may open, why not) |

**How the two positions are read: x is a button's left edge, y its middle line.** Three readings were tried.
(1) A corner: the 113 px icons would hang 36 px below the screen. (2) A middle: the exchange icon would then
span 671..729 and lie on the last heirloom's count, which starts at 660 (`currency_pos` 200 + 200 + 3 x 74 + 38)
and with three figures ends at 696. (3) Left edge and middle line: the icon starts at 700, 4 px after a
three-figure count, and is centred on 1009, the line the heirloom icons are centred on (990..1030). The DLC's
copy of the file (`dlc/735730_color_of_madness/.../estate_summary.layout.darkest`) confirms (3): it moves
`currency_pos` to 180 and the icon to 755, and with its fifth count (shards, starting at 714) a three-figure
number ends at 750. The row of screens is read the same way: left edges 1800 and 1690, 7 px from the screen's
right edge, 113 px icons on a 110 px step.

Not in a file: which screen stands first in the row (taken: the log at the end, as the art folder lists them),
and whether the grey icon means "open" or "under the pointer" (taken: open; DD1's other "selected" art is an
overlay, this one is a whole second icon).

## 1. Heirloom exchange: the rules (`Core/HeirloomExchange.cs`, `Estate/HeirloomExchange.cs`)

`campaign/heirloom_exchange/heirloom_exchange.json`, `exchange_rates`: twelve entries
`exchange_from_type` / `exchange_from_amount` / `exchange_to_type` / `exchange_to_amount`.

| gives | for portrait | for deed | for crest | for bust |
|---|---|---|---|---|
| bust | 3 : 1 | 3 : 2 | 2 : 3 | |
| portrait | | 2 : 3 | 1 : 3 | 2 : 3 |
| deed | 3 : 1 | | 2 : 3 | 3 : 2 |
| crest | 6 : 1 | 3 : 1 | | 3 : 1 |

The rates are not fair both ways (two portraits make three busts, three busts make one portrait): the exchange
costs. `HeirloomExchangeRules` reads the file, keeps the file's order (it is the order of the rows on screen),
drops a rate that gives or takes nothing, trades a kind for itself, repeats a pair or names a kind the estate
does not keep. The stock table is the marked FALLBACK. Not read: the DLC's
`com.heirloom_exchange.json` (shards for the four heirlooms); the estate has no shards.

**How many may be given** is the screen's own arithmetic in DD1; no file states it. The mod's rule: the amount
steps through the numbers at least one of the kind's rates divides (busts: 2, 3, 4, 6, 8, 9, 10...), from the
smallest up to the largest the estate can pay. An estate that cannot pay the smallest still sees it, with every
row refused. A row is refused (`Refusal`) when its rate does not divide the amount (`NotAMultiple`: 4 busts at
3 for 1) or the estate holds fewer (`CannotPay`); it then shows what whole exchanges would give, dimmed.
Tests: `tests/CoreTests/HeirloomExchangeTests.cs` (8).

## 2. Heirloom exchange: the panel (`Estate/HeirloomExchangePanel.cs`)

`heirloom_exchange/heirloom_exchange.layout.darkest`, art `heirloom_exchange.*.png` beside it.

| DD1 element | Source (block, key) | Mod element |
|---|---|---|
| Panel 429x268 | `town_screen_layout.heirloom_exchange_pos` 340 708; `.background.png` | Same. Its foot (below 958) is behind the bar |
| Title | `heirloom_exchange_layout.title_pos` 215 24 (the panel's middle: centred); style `town_heirloom_exchange_title` (Ubuntu medium, notable); `town_name_heirloom_exchange` "Trade Heirlooms" | Same |
| What is given | `heirloom_from_pos` 0 0 + `heirloom_exchange_heirloom_from_layout`: `choice_start_offset` 79 110, `choice_spacing` 44 0 | Two columns: the heirloom (79), the number (123) |
| Arrows of a column | `arrow_up_offset` 0 -10, `arrow_down_offset` 0 84; `.arrow_up.png`, `.arrow_down.png` (32x30) | Same; an arrow that leads nowhere is dimmed |
| Arrow under the pointer | `arrow_up_selected_overlay_offset` -26 -18, `arrow_down_selected_overlay_offset` -26 70; `.selected_overlay.png` (52x52 glow) | Same, behind the arrow |
| Heirloom given | `icon_offset` 0 32; `shared/estate/currency.<id>.icon.png` (40x40) | Same |
| How many | `text_offset` 0 36; `town_heirloom_exchange_from_amount` (Ubuntu medium, neutral) | Same; dimmed when the estate holds fewer |
| What is taken | `heirloom_to_pos` 0 0 + `heirloom_exchange_heirloom_to_layout`: `choice_start_offset` 256 75, `choice_spacing` 0 44 | One row per rate of the heirloom given, in the file's order |
| Bent arrow to a row | `arrow_offset` 148 80; `.arrow_0.png` .. `.arrow_3.png` (72x180 each) | Arrow n for row n, all at the one offset |
| Row frame | `frame_offset` -32 -8; `.frame.png` (189x56, gold) | Same |
| Row that cannot be taken | `frame_invalid_offset` -32 -8; `.frame_invalid.png` (grey, with a no-entry sign) | Same; no button (the sign stands where the button would) |
| Heirloom taken, amount | `heirloom_icon_offset` 0 20; `heirloom_amount_offset` 52 4; `town_heirloom_exchange_to_amount` (Ubuntu medium, notable) | Same |
| Confirm | `confirm_offset` 112 20; `.confirm.png` (48x24) | Same: a click trades and saves |
| Coming and going | `.anim.darkest` `heirloom_exchange_transition_animation`: 0.4 s in (easeOutSine), 0.4 s out (easeInSine) | The panel rises from behind the bar and sinks back |
| Amount changing | `heirloom_exchange_to_amount_change_pusle` (sic): out 0.25 s, in 0.125 s, scale 1.25 | The amounts that changed swell and settle |

**How the offsets are read** (the file does not say what they count from):
- *From side: the middle of a column and its top.* The glow overlays are 52 px wide at -26: centred on the
  column, so the arrows (32 px at 0) are too. Arrow at -10..20, icon at 32..72, arrow at 84..114: 12 px either
  side of the icon. The background's violet glow is painted around 80, 162, the icon's middle read so. And the
  four bent arrows all start at y 84 of their art: 80 + 84 = 164.
- *Two columns, not four heirlooms.* Four icons 44 px apart would reach 251 and lie under the bent arrows (148..)
  and the rows (224..). One "choice" has an icon offset and a text offset at nearly one height (32 and 36): the
  first column shows the icon, the second the number, each with its pair of arrows.
- *To side: the middle of the icon and of the button, the top middle of the number.* The frame's dark field is
  centred 20 below the row's origin; the no-entry sign of the invalid frame is centred 113, 18 from it, where
  `confirm_offset` 112 20 puts the button's middle. The bent arrows' tips are at 20, 61, 105, 149 of their art
  (+80 = 100, 141, 185, 229) and the frames' middles at 95, 139, 183, 227.
- *Rows in the file's order from the top.* DD1 has four arrows and four rows' worth of panel; its DLC's shards
  buy four heirlooms, an heirloom buys three. The fourth row stays empty for the estate's four heirlooms.

Deviations: tooltips (a row: what is given and taken, the rate, or why not; they open over the panel's top
edge) are the mod's, DD1's files have none for this panel. No sound.

**Closeness: 88.** Below 100: three readings (two columns, anchors, row order) are inferences, though each has
two independent supports; the stepping rule is the mod's.

## 3. Activity Log: what is written (`Estate/ActivityLog.cs`, `ActivityLogText.cs`)

DD1's sentences are in `localization/miscellaneous.string_table.xml` (English). They carry printf places,
`{colour_start|id}` / `{colour_end}` marks (colours in `colours/base.colours.darkest`: `town_activity_log_
building_name`, `_character_name`, `_positive_result` = notable; `_negative_result` = harmful; `_entry`, `_story`
= neutral; `stressNOT1` = stress) and `{?name}` notes. `ActivityLogText.Rich` fills them and turns the marks into
TextMeshPro colour tags, so an entry is stored as it is shown. There are no ids named `activity_log_*`
or `str_activity_*` beyond `town_name_activity_log` and `str_activity_log_quirk_replaces`: the sentences are
named after what happened.

| Event | How the log learns of it | Sentence |
|---|---|---|
| A week passing | `EstateState.WeekAdvanced` | DD1's title bar per week, `str_week` "Week %d". The present week is always shown |
| Quest embarked on | `NarrationMoments.ExpeditionSeen` (a new map seed) | `str_embarked_on_<type>_<dungeon>` with the party's names, `str_difficulty_<n>`, `town_quest_length_<n>` ("... set forth to explore the Ruins. (Lvl. 1 Medium)"); the mod's `activate` is DD1's `inventory_activate` |
| Quest completed / failed / abandoned | `EstateState.ExpeditionEnded` + the run's `Exploration.Status` | DD1's banner (success, failure, abandon) and `str_returned_from_<type>_<dungeon>_success` / `_failure`; a party wiped: `str_allperished` |
| ... its haul | the estate's gold, heirlooms and stores now, less what they were at embarking (kept with the expedition's memo, in the save) | The mod's: "Brought home: 38 gold, 4 Busts, 2 Deeds and the trinket ..." (pay and bag together) |
| Region level gained | `QuestBoard.DungeonLevel` now against the memo's | `str_<dungeon>_level_<n>_unlocked` ("Ruins: Advanced to Mastery 1.") |
| Hero dead | `NarrationMoments.HeroFell` (the grave's record) | `str_perished` "%s met their final fate during the quest." + the mod's "(place, slain by a vile <killer>)" from `str_death_attack_monster`; in the hamlet: the mod's words |
| Hero hired | a new face on the roster (looked at twice a second) | The mod's: "Stage Coach: <name> the <class, path> has joined the estate." |
| Hero dismissed | `RosterLifecycle.Left` for a hero with no fresh grave | The mod's: "Stage Coach: <name> the <class, path> was dismissed." |
| Resolve level gained | `Resolve.Level` above the last look | `str_is_now_a` "%s is now %s %s (Lvl. %d)." with `str_resolve_<n>` ("Veteran") |
| Stress relieved, Tavern / Abbey | `ActivityLedger.Changed` + `ResultsFor(building)` | `str_<activity>_stress_relief_story` ("Tavern: Dismas had a night on the town and recovered 3 stress.") |
| ... refuses to leave | same | `str_<activity>_activity_lock_story` |
| ... wandered off | same | `str_<activity>_go_missing_story` |
| ... a quirk gained | same | The mod's: "<building>: <hero> came away changed. New Quirk: <DD2's name>" |
| ... gold lost / won | same | `str_<activity>_currency_lost_story`, `str_<activity>_currency_gained_story` (DD1 has them for some activities; the mod's words for the rest) |
| ... back from missing | same | The mod's (the ledger's own line) |
| Treatment: quirk removed | `SanitariumLedger.Changed` + `LastResults()` | `str_treatment_remove_negative_quirk` |
| ... quirk locked in | same | `str_lock_quirk_story` |
| ... disease cured (and the others with it) | same | `str_disease_treatment_remove_negative_quirk`, `str_disease_treatment_removed_diseases_crit_story` |
| ... did not take, nothing to treat | same | The mod's |
| Building step built | `UpgradeRules.Built` | `str_<tree with _ for .>_upgrade_lvl_<n>` ("Additional hero barracks have been added! ... Level: 2"); the mod's own trees: its words |
| Town event | `TownEvents.Current` changing (looked at) | `str_town_event_started` "Town Event: %s" |
| Trinket bought | `NomadWagon.Changed` + a ware newly marked sold | The mod's: "Nomad Wagon: <trinket> bought for N gold." |
| Trinket sold | `NomadWagon.Changed` + a trinket gone from the stores with gold come in | The mod's: "Nomad Wagon: <trinket> sold for N gold." |
| Heirloom trade | `HeirloomExchange.Traded` | The mod's: "Trade Heirlooms: 6 Busts for 9 Crests." |

Every system is listened to through what it already raises; **no other file needed a line**. The Tavern, the
Abbey and the Sanitarium keep their week's results as text lines of their own wording, and the log reads those
lines (who, which activity, what happened) to put them in DD1's sentences. A line that reads otherwise than
expected goes into the log as the ledger wrote it, so a reworded ledger costs the DD1 sentence, not the entry.

Deviations and their reasons:
- **Boss quests and the Darkest Dungeon are in the mod's words** ("... set out: <quest>, <region>."): DD1's
  sentences for them name DD1's bosses (the Necromancer, the Hag) and its four descents; the estate's are other
  ones. Darkest Dungeon news stands in DD1's frame for it (`darkest_dungeon_entry_backdrop.png` and icon).
- **A quirk gained is one sentence for all quirks**: DD1 has one per activity and DD1 quirk
  (`str_bar_add_quirk_alcoholism_story`); the ledger's line carries DD2's quirk name only.
- **Hires, dismissals, trinket and heirloom trades** are the mod's: DD1 logs none of them.
- **Stress is DD2's**: "recovered 3 stress" on DD2's 0..10 scale where DD1 says 30 of 200.
- **Length**: no DD1 file gives its log one. The mod keeps 52 weeks and 800 entries (`ActivityLog.WeeksKept`,
  `MaxEntries`) and builds the newest 8 weeks when the screen opens, 8 more whenever the list's foot is reached.

## 4. Activity Log: the screen (`Estate/ActivityLogPanel.cs`)

`activity_log/activity_log.layout.darkest`; backdrop and button art in `campaign/town/activity_log/`, the
entries' art in `activity_log/` (the install's root).

| DD1 element | Source (block, key) | Mod element |
|---|---|---|
| Backdrop 1395x776 with the caretaker at his desk | `town_screen_layout.activity_log_pos` 144 132; `activitylog_bg.png` | Same, the town darkened around it (72% black, as behind a window) |
| "Activity Log" | `activity_log_layout.name_pos` -46 -12; style `town_name`; `town_name_activity_log` | Centred on 250, 70 (see below) |
| "Caretaker Goals" | `caretaker_goal_layout.name_pos` 650 -12; `str_caretaker_goals_heading` | Centred on 946, 70 |
| Close button | `close_pos` 1352 12; `shared/progression/progression_close.png` | Same |
| The log's window | `activity_log_entry_layout.text_pos` 25 160; `activity_log_layout.panel_size` 620 550 | A scrolling list there (wheel, rail, arrows) |
| Scroll rail | `scrollbar_offset` 0; `shared/widgets/scrollbarmid.png`, `scrollpip.png` | Same art, right after the window; hidden while the list fits |
| Scroll arrows | `scroll_arrows_offset` 15 7; `shared/widgets/scrollbar_uparrow.png`, `scrollbar_downarrow.png` (41x25) | Centred on the rail, 7 px off the window's top and bottom (the 15 is not used: read as an x, it puts them beside the rail) |
| First entry | `entry_start_pos` 0 10 | Same |
| A week | `week_title_bar.png` (619x136); style `town_activity_log_week_title` (DwarvenAxe large, notable); `str_week` | The bar, "Week n" centred in its field (MEASURED: the field's middle is 322, 67; the art starts 16 rows down) |
| A line of the log | `entry_spacing` 0 30; style `town_activity_log_entry` (Ubuntu small, 25 px line, neutral) | Lines follow each other 30 apart (5 px between two); a long one wraps |
| Between entries | `activity_log_entry_layout.vertical_spacing` 20 | 20 px before and after a frame or a banner |
| Quest's end | `raid_success_banner.png`, `raid_failure_banner.png`, `raid_abandon_banner.png` (619x58) | The banner, the sentence under it |
| A hero in a building | `hero_activity_entry_backdrop.png` (600x118); `hero_building_log_entry_layout`: `building_image_margins` 0 6 0 0, `hero_image_margins` 10 10 5 5, `textbox_width` 440, `textbox_top_margin` 5, `textbox_entry_spacing` 0 | The building's sign, the hero's face in DD1's hero slot, the text; several things that happened to one hero in one place are lines of one entry |
| A resolve level | `hero_level_up_entry_backdrop.png` | The hero's face, the sentence |
| A building's upgrade | `building_upgrade_entry_backdrop.png` | The building's icon, the sentence |
| A region's level | `dungeon_unlocked_entry_backdrop.png` | The sentence |
| The Darkest Dungeon | `darkest_dungeon_entry_backdrop.png`, `darkest_dungeon_log_icon.png` (85x85) | The icon, the sentence |
| The goals' window | `caretaker_goal_layout.text_pos` 720 160, `panel_size` 600 240, `entry_spacing` 10 10, `scrollbar_offset` 0 | A scrolling list there, entries 10 px in and 10 px apart |
| Goal headings | style `caretaker_goal_type_heading` (DwarvenAxe large, notable); `str_caretaker_goals_quest_goals_heading`, `..._roster_goals_heading` | Same |
| A goal | style `caretaker_goal_entry` (Ubuntu small); colours `caretaker_goal_completed` (notable), `caretaker_goal_not_completed` (neutral) | Same |

**The two names.** A town screen's name is a widget whose own text offset is in no file. On the buildings'
backdrops (`name_pos` -40 -6 from the backdrop) the mod centres the name on 256, 76, between the two rules
drawn there: the widget therefore puts its text 296, 82 from `name_pos`. This screen's two `name_pos` are
6 px up and left of a building's, and 696 apart; the same widget gives 250, 70 and 946, 70. The rules of this
backdrop are at the same height (y 27..36 and 108..117, middle 72) and both names stand as far from the start
of their rules (178 and 186 px). The rules run on to the right, over the list's width; a name centred on the
rules instead would stand 104 px further right, and both by the same amount.

**Inside a hero's entry.** The margins leave 160 px beside the 440 px text box: 10 + 85 (DD1's portraits are
85x85) + 10 for the face and 49 + 6 for the building's sign, which is what the three roster signs
(`<id>.icon_roster.png`, 64x64) are drawn into: 45..49 px of their 64. MOD: the 10 px by which everything
keeps clear of the frame painted into a backdrop; the sign and the face are centred on the frame's height.
Buildings without a roster sign (stage coach, graveyard) lend their 113 px icon at 64.

**The caretaker's goals** are DD1's two lists with the estate's own content: under "Quest Goals" the story
quests of `Data/plot_quests.json` with their goal texts (DD1: "Defeat the Sodden Crew"...), done once in
`CompletedQuests`; under "Roster Goals" DD1's `str_caretaker_goal_hero_resolve` "Raise a %s to Resolve Level 6"
for every class the install can field, done once a hero of the class has reached it (remembered in the save).

Deviations: `new_game_plus_week_count_pos` (DD1's "Week: n/m" of its timed modes) is not drawn; the estate has
no such mode. DD1 reads the log oldest or newest first: not in a file; the mod reads from the present week back.
The hamlet's own "Week n" on the name plate stays (HamletScreen is not this code's); DD1 has the week only here.

**Closeness: 85.** Below 100: the order of the weeks and the places inside an entry are inferences; DD2
portraits; the mod's words for what DD1 does not log.

## 5. Trinket Inventory: the moves (`Estate/RealmInventory.cs`)

The trinkets, the stores (`GameTypeMgr.PlayerInventory`) and a hero's two slots
(`ActorInstance.GetTrinketInventory()`, a `TrinketItemInventory`) are DD2's. Every move is the call DD2's own
character sheet makes (decompiled `IronCrown`):

| Move | DD2's code | The mod |
|---|---|---|
| Click a trinket of the stores | `PlayerInventoryItemBhv.TryAutoTransferTrinket`: `trinketInventory.CanAdd(def, qty)` and `TrinketItemInventory.GetCanEquip(def, checkInventory: true, checkConditions: true)`, then `trinketInventory.AddItems(def, qty, false)` and `stores.TakeItemQty(index, qty)`; sound `ItemSubType.TrinketEquipSfxEventRef` | `RealmInventory.Equip(guid, id)`: the same calls, the same sound |
| Drop it on one of the hero's slots | `TrinketInventoryItemBhv.AcceptsItem` (`GetCanEquip`), `GetSwapFunction` (none for a slot whose trinket has `m_IsUnequipInvalid`), `InventoryItemBhv.DefaultSwap`: `ItemInventory.SwapItems(source, from, slot, false)` | `Equip(guid, id, slot)`: `SwapItems`; what lay in the slot goes back to the stores |
| Take a worn trinket off | `TrinketInventoryItemBhv.OnTryAutoTransfer`: `stores.CanAdd`, `stores.AddItems(def, qty, false)`, `worn.TakeItemQty(index, qty)`; never for `m_IsUnequipInvalid` (`IsSelectable`); sound `AudioPathsBhv.TrinketUnequip` | `Unequip(guid, id)`: the same |
| Refused | `AudioPathsBhv.ClickInvalid`, no words | The same sound, and a reason |

`GetCanEquip`: `checkInventory` = the item is a trinket and the hero does not wear one already;
`checkConditions` = `ActorInstance.GetIsItemConditionsMet`: every `ConditionDefinition` of the item
(`m_conditionIds`, e.g. `performer_is_plague_doctor`) holds for the hero, and, for an item with
`m_IsUnequipIfNotInParty`, the hero is in the party.

**Reasons.** DD2 says in its item tooltip what an item asks (one line per condition,
`ConditionDescription.GetConditionString`, what `ItemDescription.GetDescription` appends). The refusal is
those lines for the conditions this hero fails (`ConditionCalculation.IsConditionMet`). DD2 has no words for
its other refusals; the mod's are "<hero> already wears <trinket>", "<hero> has no free trinket slot",
"<trinket> cannot be taken off", "<trinket> is only for a hero of the party".

**The possession limit** (`ItemDefinition.m_possessionLimit`, enforced by `LibraryLoot.GetCanCollect` when a
trinket is handed out) is not in question: a trinket only changes hands inside the estate. `Trinkets.CanHold`
keeps it where trinkets come in (quest pay, the wagon).

One deviation: DD2 refuses to take a trinket off when the party's inventory is full. The estate's stores take
overflow (`AddItemsWithOverflow`, as when a dismissed hero hands back what he wore), so a trinket can always
come off.

Selling is `NomadWagon.Sell` (DD1's sell value of the trinket's grade, `Trinkets.SellPrice`). "Unequip all
trinkets" (DD1's button) takes off everything every hero wears that can come off.

## 6. Trinket Inventory: the panel (`Estate/RealmInventoryPanel.cs`)

`realm_inventory/realm_inventory.layout.darkest`, art beside it.

| DD1 element | Source (`realm_inventory_layout` unless said) | Mod element |
|---|---|---|
| Panel 667x780, against the roster | `town_screen_layout.realm_inventory_pos` 881 128; `realminv_bg.png` | Same |
| Everything inside | `content_offset` 0 2 | Same |
| "Trinket Inventory" | `name_pos` -44 -10; `town_name`; `town_name_realm_inventory` | Centred on 252, 72 (DD1's name widget, section 4): left of the buttons, over the line about selling |
| Close | `close_pos` 610 22; `progression_close.png` | Same |
| Sort buttons, "unequip all" | `sort_button_pos` 567 22, `sort_button_spacing` 42 0; `realm_inventory_sort_alphabetical.png`, `..._class.png`, `..._rarity.png`, `realm_inventory_unequip_trinkets.png` (32x32) | Four buttons ending at 567: name 441, class 483, rarity 525, unequip all 567 |
| Order in use | `sort_button_current_ascending_overlay_offset` -8 -8, `..._descending_...` -8 24; `realm_inventory_sort_current_overlay.png` (49x16) | Same: over the button ascending, under it descending. Click: this order; again: reversed; again: the stores' own (DD1's "Custom", `sort_trinket_label_none`) |
| What a button does | `sort_button_tooltip_offset` 16 -12; `str_sort_trinkets_alphabetically`, `str_sort_trinkets_by_class`, `str_sort_trinkets_by_rarity`, `str_unequip_all_trinkets` | DD1's words in DD1's tooltip at that offset |
| How to sell | `trinket_info_description` 150 100 (width 1000); style `realm_inventory_trinket_sell_description`; `realm_inventory_trinket_sell_instruction` "Hold [SHIFT] to Sell Trinkets" | Same; Shift and a click sells |
| What it sells for | `trinket_sell_value` 365 116 + `shared/estate/estate.layout.darkest` `estate_currency_gold_layout` (`icon_offset` 0 -12, `number_offset` 25 -14); `realm_inventory_trinket_sell_description` "Sell trinket for:" | While Shift is held over a card: those words, the coin and the price |
| Selling asks | `realm_inventory_sell_trinket_confirm_question_format`, `..._yes`, `..._no` | DD1's question in DD2's dialog (as the wagon's window does) |
| Grid | `inventory_grid_pos` 30 195, `inventory_grid_size` 560 525; `realm_inventory_grid_layout`: `number_of_columns` 7, `start_pos` 0 0, `offset` 80 160 | Same: 72x144 cards, 80 x 160 apart; three rows and the head of a fourth show |
| Rules of the grid | `grid_visuals_offset` -15; `realminventory_h_grid.png` (628x12), `realminventory_v_grid.png` (404x164: six lines 80 apart) | A row rule 15 above every row (so: in the gap under the row before), a row's six column rules lifted as much, standing in the gaps between the cards. The row rule is as wide as the panel's inside and centred in it |
| Rows shown | `scroll_max_visible_rows` 4 | Four rows of rules are always drawn |
| Scroll rail | `scroll_bar_offset` 20 0 | 20 px right of the grid (610), under the close button; wheel, rail and arrows scroll a row |
| Scroll arrows | `realm_inventory_uparrow.png`, `realm_inventory_downarrow.png` (62x49); no place in the file | MOD: at the rail's two ends; shown once a fourth row is filled |
| A card | `panels/icons_equip/trinket/rarity_<rarity>.png` (72x144) with the trinket's art | DD1's card of the trinket's DD1 grade, DD2's icon (64 px) on it; dimmed when the hero at hand cannot wear it |
| Tooltip | DD1's tooltip box | DD2's name; DD1's rarity name; DD2's item text (`ItemDescription.GetDescription`); what a click does, or DD2's reason; the sell value |

**Sort buttons: the row ends at `sort_button_pos`.** Started there and run rightwards, the second button (609)
would lie on the close button (610) and the third (651) outside the panel's frame; ending there the row is
441..599, clear of the title (which ends near 400) and of the close button. Which button stands where is not in
a file: the three orders as DD1's strings list them, then "unequip all".

**Where the hero is** (batch of 2026-10-06; before it a cut of DD1's sheet, the "band", stood beside the panel
on its own). DD1 shows this panel beside its character sheet (`shared/character/`), which stands where a
building's backdrop does; the panel covers the sheet's right half, and trinkets are dragged between the panel
and the sheet's equipment band. Opened from the bar alone, the panel stands by itself. The estate's sheet is
DD2's own character sheet, and DD1's Equipment is on it now, in place of DD2's "Trinkets"
(`Estate/SheetEquipment.cs`): the piece of `characterpanel_frames.png` with the four slots (MEASURED: the bar's
rules at y 493..495 and 533..535, the glyphs at y 550..582, the slots' frames at x 268..346, 359..437, 485..563,
576..654, y 588..736; the cut is x 261..663, y 485..746 with its edges faded out over 8 px: the art is plain
black there and so is DD2's sheet, no seam shows), the things on it placed by `character.layout.darkest`
(`equipment_pos` 141 516, `character_equipment_layout`) and `hero.layout.darkest` (`hero_equipment_layout`,
`hero_trinket_grid_layout`), the middle of its four slots on the middle of DD2's two (1274, 853 at 1920x1080),
then 16 px lower (the glyphs clear of the heading's rule at y 748..751) and 12 px to the left (DD1's four slots
are 386 wide, DD2's column 340: the last slot clear of the sheet's frame line at x 1463..1466).
In the weapon and armour slots DD1's own pictures of the class's pieces at the hero's Blacksmith level
(`Blacksmith.Card`: `heroes/<class>/icons_equip/eqp_weapon_<n>.png`, also for the classes of DD1's DLC folders;
a class the install has no art for gets the smithy's icon of the kind of piece), the level beside DD1's glyph
in the level's colour (`equipment_level_<n>`); pointing at a piece says its DD1 name and what the level gives
(`Blacksmith.Effect`). In the two trinket slots what the hero wears, as the panel's own cards
(`RealmInventoryPanel.WornCard`). The heading is DD1's word (`character_title_equipment`) in the sheet's own
heading (a copy of DD2's "Trinkets" heading), or on DD1's bar (`[Look] SheetEquipmentHeading = dd1`).

**Sheet and panel together.** DD2's sheet is 1486 px wide at 2..1488 (its canvas is over the hamlet's), the
panel stands at 881..1548: for as long as both are up the sheet stands aside, its right edge at the panel's
left (it slides there and back when the panel comes and goes; while DD2's own timeline slides the sheet in or
out, some 250 px in a quarter of a second, the sheet keeps its edge at the panel's). What leaves the screen is
the sheet's left column (skills, resistances): the part DD1's panel covers on DD1's sheet too; a hero's name of
more than ten letters or so loses its first letters at the screen's edge for as long as the panel is open. The hero at hand is the sheet's hero: a click on a roster row while
the panel is open opens that hero's sheet (the same `HeroDrag` and the same switching-off of `RosterRowClick`
as `RosterWindow` uses), the sheet's own arrows step on, and with no sheet up the panel has no hero (a click on
a card then says so; a drag onto a roster row still works). A card in the hand is drawn on a layer over both
(`DD2Estate.SheetTop`, order 60: the hamlet's canvas is 5, DD2's screens 10).

| Gesture | Effect |
|---|---|
| Click a card | On the sheet's hero, in the first free slot (`Equip`) |
| Drag a card onto a slot of the sheet | Into that slot; what was there goes back to the stores (`SwapItems`) |
| Drag a card onto the sheet anywhere else (the hero) | As a click |
| Drag a card onto a hero's roster row | On that hero, whose sheet opens |
| Click a worn trinket (on the sheet) | Off (`Unequip`) while the panel is open; opens the panel while it is not |
| Click an empty slot of the sheet | Opens the panel (DD2's own slot opens DD2's inventory) |
| Drag a worn trinket onto the panel | Off |
| Drag a worn trinket onto another hero's row | Off the one and onto the other, or nothing at all (`Give`) |
| Shift and click a card | Sold, after DD1's question |
| Right click on the panel | Closes the panel |

Away from the hamlet (a dungeon, a fight) the block is for looking: nothing is picked up there (trinkets change
hands with the bag on the raid panel).

Deviations: what the panel has to say (a refusal, a sale) takes the place of the line about selling for six
seconds; DD1's "add trinket" dialog (`add_trinket_dialog_contents`, a debug tool) and its controller prompts
(`*_input_preview_pos`) are not built; the Nomad Wagon's own row of unworn trinkets stays as it was; a worn
trinket cannot be moved from one slot of the sheet to the other by a drag.

**Closeness: 88.** Below 100: DD2's sheet, not DD1's, is what the panel stands beside; DD2 icons on DD1 cards;
the order of the small buttons and the arrows' places are guesses; clicking as well as dragging.

## 7. Not verified without the game

1. Everything drawn by the new code: nothing here has run in the game. First things to look at: the three
   buttons on the bar (the exchange after the last heirloom's count, the two icons in the bar's right corner
   over the roster column's foot), that they take clicks, and their glow and tooltip.
2. `TownPanelButtons` finds `EmbarkButton` / `EstateSummary` in the hamlet's canvas and seats itself after
   them; whether the estate map and the narration band then lie over the buttons as intended.
3. The exchange: sliding up from behind the bar (the root's `RectMask2D` cuts it at the bar's top), the pulse
   of a changed amount, arrows dimming, a trade showing on the bar's counts within the bar's own refresh (0.4 s).
4. The log: `GetPreferredValues` heights of wrapped rich text in the DD1 font; the scroll list with DD1's rail;
   weeks being added at the list's foot without a jump; portraits (DD2's, 98 px cut off in DD1's 85 px slot).
5. The log's sources in real play: every row of the table in section 3. Above all the reading of the ledgers'
   lines (a real week's end in the Tavern, the Abbey and the Sanitarium), the haul after a real quest, a death,
   a dismissal against a burial, a purchase and a sale at the wagon, a town event, a level gained, and that a
   loaded save does not write any of them twice.
6. The inventory: DD2's moves on a real hero (`AddItems` + `TakeItemQty`, `SwapItems` into a filled slot,
   taking off), the trinket's effects then showing in DD2's sheet and in a fight; DD2's own words for an unmet
   condition (a class trinket on another class); a trinket marked `m_IsUnequipInvalid` or
   `m_IsUnequipIfNotInParty`; the sounds.
7. The roster hand-over while the inventory is open (a click picks the hero and does not toggle the party; the
   party click is back the moment the panel closes, also when a building is opened straight from it), dragging
   a card (the ghost, the drop on a slot, on the band, on a roster row), Shift read through `Keyboard.current`.
8. DD2's trinket icons on DD1's cards in the grid and their loading; the cut of `characterpanel_frames.png`
   (a sprite made from a part of a texture); DD1 gear art for DD1's classes.
9. Screens that are not 16:9: the three anchorings (`TownPanel.Side`).
10. The save: the `activity_log` section through save, menu, continue; an expedition's memo across a restart
    in the middle of a dungeon; an old save without the section.

## 8. Files and test bridge

`Core/HeirloomExchange.cs`, `tests/CoreTests/HeirloomExchangeTests.cs`; `Estate/HeirloomExchange.cs`,
`HeirloomExchangePanel.cs`, `HeirloomExchangeDev.cs`; `Estate/ActivityLog.cs`, `ActivityLogText.cs`,
`ActivityLogPanel.cs`, `ActivityLogDev.cs`, `ActivityLogTownPanel.cs` (`TownPanel`, `TownPanelButtons`: shared by
all three); `Estate/RealmInventory.cs`, `RealmInventoryPanel.cs` (with `TrinketDrag`), `RealmInventoryDev.cs`;
`tools/preview_town_panels.py`.

| Command | Does |
|---|---|
| `heirlooms.state` | Rates, what the estate holds, smallest and largest amount per heirloom; the panel; the bar's buttons |
| `heirlooms.quote` `{from, amount, to?}` | What an amount buys right now, and why not |
| `heirlooms.trade` `{from, amount, to}` | Trades at DD1's rate and saves |
| `heirlooms.give` `{bust: 12, crest: -3}` | Adds and takes heirlooms for a test |
| `heirlooms.open` `{kind?}` / `heirlooms.close` | The panel, as the bar's button opens it |
| `heirlooms.pick` `{kind?, steps?}` | The panel's arrows: the heirloom, the amount up (or, below 0, down) |
| `heirlooms.confirm` `{to}` | A click on a row's confirm button |
| `log.state` `{weeks?}` | The log as plain text, newest week first; the screen; the bar's buttons |
| `log.week` `{week?}` | One week's entries as stored (rich text) |
| `log.add` `{text, kind?, hero?, building?}` | Writes an entry into the present week |
| `log.clear` | Empties the log |
| `log.goals` | The caretaker's goals as the screen lists them |
| `log.open` / `log.close` / `log.scroll` `{steps}` | The screen and its scroll arrows |
| `inventory.state` `{by?, descending?}` | The stores in one of DD1's orders, every hero's slots; the panel |
| `inventory.can` `{guid, id, slot?}` | Whether DD2 would let the hero wear it, and its reason |
| `inventory.equip` `{guid, id, slot?}` / `inventory.unequip` `{guid, id}` / `inventory.give` `{guid, id, to}` | The moves, saved at once |
| `inventory.unequip_all` | DD1's button |
| `inventory.sell` `{id}` | Sells at DD1's price for the grade, no question asked |
| `inventory.open` `{guid?}` / `inventory.close` / `inventory.hero` `{guid}` | The panel; what a click on a roster row does |
| `inventory.click` `{id}` | What a click on a card does (stored: on; worn: off) |
| `inventory.sort` `{by, descending?}` | The sort buttons (`name`, `class`, `rarity`, `arrival`) |

Trinkets come into the stores for a test with the wagon's commands (`trinkets.give`, `trinkets.roll`).
`TownPanelButtons.Click("activity_log")` through the bridge's `invoke` is a click on a bar button.

## 9. Districts: the screen (`Estate/DistrictsPanel.cs`, `DistrictText.cs`)

Added after the three above; the rules are in `dd1-missing-mechanics.md` section 0, the town event in
`town-events.md`. Layout `district/district.layout.darkest` (`district_layout`, `district_entry_layout`), art
beside it; the buildings are Spine sprites under `dlc/580100_crimson_court/features/districts/fx/` with two
pictures each (`idle` the empty lot, `built`). `python tools/preview_districts.py` writes `town_districts.png`,
`town_districts_far.png`, `town_districts_locked.png`. **Not seen in the game, and DD1's own screen was not
seen by whoever wrote it**: the readings rest on the numbers fitting each other and the art.

| Number | Read as | Why |
|---|---|---|
| no `district_pos` in `town.layout.darkest` | the backdrop (1395x776, a building's size) stands at `town_background_layout` `area_pos` 144 132 | the log's and the crier's backdrops of the same size stand there |
| `name_pos` -46 -8 | the name centred 296, 82 from it | the same widget as the log's (`ActivityLogPanel`): between the backdrop's two rules |
| `district_list_window_pos` 45 100, `_size` 1308 1000 | the clip of the row; every entry number counts from its corner; x of an element is its middle, y its top (as in the crier's layout) | see the next rows; the height is more than the screen has left, so the window is not what ends the row |
| `midground_y_offset` 65, `midground_speed` 0.5 | the roofs' top 65 under the window's (backdrop row 165), drifting at half the row's pace | MEASURED: the backdrop's sky ends at rows 417..429, the roofs' picture goes dark over its last 30 rows (435..465): the roofs stand on the sky's lower edge and close it off. Read from the backdrop's top they would start in the black header and end 60 rows above the sky's edge |
| `building_y_offset` 400 | the skeleton's origin; its own bone lifts the picture 20 px | the feet stand at backdrop row 480, in front of the roofs' dark foot; the tallest picture (344) ends at row 136, under the name's rules (MEASURED: the lower one ends at row 121). Read from the backdrop's top the tall buildings would run through the name |
| `district_list_scrollbar_offset` 380 | the top of DD1's sideways bar (`shared/widgets/scrollbarhmid.png`, 21 px); MOD: its arrows at the two ends | exactly the strip between the pictures' feet (380) and the frames (400) |
| `frame_y_offset` 400, `title_y_offset` 13, `desc_pos_offset` -144 56, `desc_window_width` 280 | the frame's top; the name's line cell on the frame's band; the text's corner and width | MEASURED on `entry_frame.png`: band between rules at rows 6..8 and 47..49, body to 221 |
| `cost_start_y_offset` 235, `cost_item_icon_offset` -110 0, `cost_item_entry_spacing` 100, `cost_item_number_offset` 16 4 | the icons' top; their middles at -110, -10, 90 from the entry's; the amount begins 16 right of an icon's middle | the 40 px icons lie between the frame's two ornaments (rows 222..231, 285..294); an icon's corner at -110 would put the amount inside it |
| `cost_checkmark_y_offset` 40 | the button's top, 40 under the price's | `purchase_building_button.png` (64x32) then ends on the frame's last row (307) |
| `currency_pos` 32 130, `currency_number_offset` 20 5 | the blueprint icon's middle and top, the count 20 right of the middle | the same reading as the price's |
| `desc_item_spacing` 20 | NOT USED: the lines are one wrapped block at the font's own line height | 20 is less than a line of the style (25); what DD1 spaces with it is not known |

What follows from this reading and wants a DD1 frame first: an unbuilt entry's frame hangs 39 px below the
backdrop's lower edge (its price straddles the edge, its button is wholly below it) and ends 11 px above the
estate's bar, while a built entry's frame (`entry_frame_purchased.png`, art to row 256) ends inside the backdrop.
Other guesses: the unbuilt entries' grey (base.colours.darkest gives "darkness 0.4, saturation 0", which the
colour reader does not take: 0.4 of white, as the mod reads the same pair for an item that cannot be chosen);
the districts' button dimmed with "Districts Locked" before the event rather than absent; the selected overlay
shown under the pointer; when DD1 shows the finished building against its bricks and dust (here: at once, the
dust over it for the 1.87 s of `fx/purchase_district` "purchased"); prices with thousands set apart, as the
town's other prices. No tutorial pop-ups. The buildings and the dust are played by `UI/SpineView`; the bar's
button is the sprite `fx/estate_districts` like the row's others (`idle`, `selected` while the screen is open).

| Command | Does |
|---|---|
| `districts.state` | Whether the install has districts, open or locked, blueprints, what stands; per building its price here and in DD1, what it gives, whom, which DD1 buffs are left out; the screen; the bar |
| `districts.unlock` / `districts.event` | Opens the districts without the event / DD1's "Cornerstones" itself this week (notice, line, one blueprint) |
| `districts.grant` `{blueprints?, gold?, crest?, ...}` | Adds and takes for a test |
| `districts.open` / `districts.close` / `districts.scroll` `{id}` | The screen (it also opens while locked, to be looked at) |
| `districts.press` `{id}` / `districts.answer` `{yes}` | The button of a building and DD1's question |
| `districts.build` `{id}` | Pays and builds, as "Yes" does |
| `districts.set` `{id, built}` / `{all: true}` / `districts.reset` | A building without a price / a new estate's districts |
| `districts.weekend` | The Bank's interest and the Puppet Theatre's relief, without a week passing |
| `districts.boss` `{quest}` | The blueprints a won story quest brings |
| `districts.party` | Per party hero: tags, stats container, scouting share; camp points, the week's food, the meals' factor |

## 10. After DD1's frames (2026-10-05): what changed in the panels

Measured on real DD1 frames (`_lab/dd1_ref/town/`, `_lab/mod_now/pairs/`); where the sections above say
otherwise, this holds. Shared findings are in `dd1-ui.md` section 5.

- **Names**: the Activity Log (twice: its own and the Caretaker's goals, each with its icon), the Trinket
  Inventory and the Glossary use DD1's screen-name widget (`Dd1ScreenName`) at their `name_pos`; the centred
  names "296, 82 from name_pos" of sections 4 and 6 are gone. (The Districts' panel still draws its own.)
- **Activity Log**: both lists stand 20 px right of their `text_pos` (week bar and entry frames from 189, the
  rails at 809 and 1484 on DD1's screen; no file has the 20), an entry's frame at the window's left edge. The
  Caretaker's goals: heading 12 px down the window, a `shared/menu/menu.check_box.png` before every goal, the
  words `entry_spacing.x` after it, rows `entry_spacing.y` apart (boxes at 377, 419, 461). GUESS: a met goal
  wears `menu.check_mark.png`. Seen and not built: DD1's first entry frame begins 146 px under the window's
  top (the mod: 122), and its week bar's art begins at the window's top (the mod: 2 px above).
- **Scroll bars**: `Dd1Ui.ScrollList` makes DD1's whole bar; the Trinket Inventory hands it its own arrows
  (`Dd1ScrollBar.Arrow`, `Follow`) and keeps a bar that comes and goes with a fourth row.
- **Trinket Inventory**: a sale asks in DD1's dialog with "Yes", "No", "Always" (GUESS: their order, and that
  "Always" stops the asking until the game is closed); "unequip all" asks there too; the sort buttons' and
  "unequip all" tooltips stand centred over their buttons (GUESS by the roster's sort buttons); a piece of gear
  is named as the Blacksmith names it.
- **The bar's buttons**: no tooltips; the row closes up over a button that is away (`Entry.ShownWhile`): the
  crier's bell has the fifth place, the Districts' button the sixth and is away until they are open.
- Bridge: section 8's commands stand; added `bar.state {"click":"town_event"}`, `ui.scrollbars`, `ui.scroll`,
  `confirm.*`.
