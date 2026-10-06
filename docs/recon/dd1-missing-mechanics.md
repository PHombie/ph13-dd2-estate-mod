# DD1 mechanics outside combat: what the mod has, what it lacks

Inventory of Darkest Dungeon 1's screens, flows and rules outside combat, each marked **present / partial /
missing** in the mod, with what it would take to add the missing ones. Written 2026-10-05 from the DD1 install
(`E:\Steam\steamapps\common\DarkestDungeon`, paths below are relative to it), `MODLOG.md`, `docs/recon/*.md` and
the code under `src/DD2Estate`. Nothing was run; statuses come from reading code and the journal, so a row
marked "present" means "written and, where MODLOG says so, seen in game". Line numbers are those of the working
tree on that day (`Dungeon/DungeonRun.cs` and `Dd2/EstateSession.cs` had uncommitted edits by others).

Conventions
- **Fits?** yes / no / owner (needs the owner's word), with one line why.
- **Effort** S = under a day, M = one to three days, L = more.
- Mod paths are relative to `src/DD2Estate`. "MODLOG: <heading>" names a section of `MODLOG.md`.
- Estate gold = DD1 gold / 50 (`docs/recon/quests-and-trinkets.md`); DD2 stress runs 0..10 where DD1's runs 0..200.
- **Excluded by the owner, never recommended below:** the Courtyard (Crimson Court), the Farmstead (Color of
  Madness), the Butcher's Circus. Rows that touch them are marked EXCLUDED.

## 0. What "the outskirts" is

"The outskirts" (the owner's word, in Russian) is DD1's **Districts**: `town_name_district`, `str_dlc_title_districts`, `str_glossary_term_1003`
in `localization/miscellaneous.string_table.xml` (the Russian text's word for it, "Predmestye"; English "Districts"; glossary text: "The
outer hamlet, where new buildings may be erected."). It is a hamlet screen opened from a button on the estate bar
where the player constructs extra buildings, each bought once with gold, heirlooms and one **blueprint**, each
giving a permanent estate-wide or class-wide bonus.

It ships inside the Crimson Court DLC folder but as its own feature that DD1 lets a campaign enable without the
Courtyard ("Enable Districts?", `str_confirm_dlc_districts`). So it is not the Courtyard and is not excluded.

Files:
- Rules: `dlc/580100_crimson_court/features/districts/campaign/town/districts/districts_districts.json` (11
  buildings: cost, Spine art, buffs), `.../features/districts/shared/buffs/districts.buffs.json` (13 hero buffs),
  `.../features/districts/campaign/estate/districts.estate.json` (currency `blueprint`, kept on a failed quest),
  `.../features/districts/inventory/districts.currency.inventory.items.darkest`,
  `.../features/districts/loot/blueprint_overrides.loot.json` (loot table `BLUEPRINT` = 1 blueprint; the base
  game's `loot/blueprint_overrides.loot.json` makes it "nothing" when the feature is off),
  `.../features/districts/campaign/town_events/districts.town_events.events.json` (event `cc_districts_unlock`
  "Cornerstones": week 10 or later, unique, unlocks the screen, gives 1 blueprint, shows tutorial `districts`),
  mode variants in `.../features/districts/modes/{radiant,new_game_plus,bloodmoon}/...`.
- Screen: base game `campaign/town/district/district.layout.darkest` (`district_layout`, `district_entry_layout`),
  `district.background.png` (1395x776), `district_midground.png`, `entry_frame*.png`,
  `purchase_building_button.png`, `district.icon.png`; bar button `fx/estate_districts`; purchase effect
  `fx/purchase_district`; building art `dlc/580100_crimson_court/features/districts/fx/town_district_<name>/`
  (animations `idle` = not built, `purchased`, `built`); currency icon
  `.../features/districts/shared/estate/currency.blueprint.icon.png`; tutorial pictures
  `.../shared/tutorial_popup/tutorial_popup.districts.png`, `tutorial_popup.blueprints.png`.
- Words: `dlc/580100_crimson_court/localization/CC.string_table.xml` (`str_<building>_title`,
  `str_<building>_buff_*`), `localization/miscellaneous.string_table.xml` (`str_district_build_building_confirm`,
  `town_district_unavailable_tooltip`, `str_districts_locked_tooltip`, `tutorial_popup_districts_*`,
  `tutorial_popup_blueprints_description`, `town_event_*_cc_districts_unlock`).
- Blueprint sources: `loot: .code "BLUEPRINT" .count 1` in the veteran and champion variants of seven dungeon
  bosses (`monsters/<boss>/<boss>_B` and `_C/*.info.darkest`: necromancer, prophet, hag, brigand_cannon,
  swine_prince, siren, drowned_captain; no `formless_*` monster carries the line) = 14, plus 1 from the
  Cornerstones event: 15 for eleven buildings.

The eleven buildings of `districts_districts.json` (cost in DD1 gold; each also 1 blueprint):

| id | English title | cost | effect in the file |
|---|---|---|---|
| `spire_of_hope` | The Red Hook | 50000 gold, 750 crests | `DistrictPrestigeBuffData`: nothing but prestige |
| `bank` | Bank | 15000 gold, 50 portraits | `weekly_gold_interest` 0.05: 5% interest on held gold each week |
| `illuminators_guild` | Cartographer's Camp | 5000 gold, 300 crests | replaces the torchlight table of `shared/rules.json` with a kinder one (scouting +17.5 at full light, monsters surprised +30, heroes surprised -5 ...) and adds darkness loot bonuses |
| `granary` | Granary | 2500 gold, 60 busts | `districts_granary_1` (+15% healing from eating) for everyone; 4-10 free food each week on the provision screen |
| `buskers_corner` | Puppet Theatre | 5000 gold, 200 crests | idle heroes shed 10 more stress per week |
| `house_of_the_yellow_hand` | House of the Yellow Hand | 5000 gold, 100 portraits | tag `house_of_the_yellow_hand` (bounty_hunter, grave_robber, highwayman): +4% crit, +5% scouting |
| `altar_of_light` | Altar of the Light | 5000 gold, 200 deeds | tag `altar_of_light` (crusader, vestal, flagellant): +10% stun resist, +10% healing done |
| `training_ring` | Training Ring | 5000 gold, 300 crests | tag `training_ring` (arbalest, houndmaster, man_at_arms, musketeer, shieldbreaker): +4 accuracy, +10% max HP |
| `library` | Athenaeum | 2500 gold, 110 deeds | tag `library` (antiquarian, occultist, plague_doctor): +15% blight chance, +15% debuff chance; "Knowledge" curios heal 15 stress |
| `theater` | Performance Hall | 2500 gold, 75 crests | tag `theater` (jester): -10% stress taken, +2 speed, +20% damage of Finale |
| `outsiders_bonfire` | Outsiders Bonfire | 1000 gold, 300 crests | tag `outsiders_bonfire` (abomination, hellion, leper, runaway): +2 respite points when one is in the party |

Other DLCs add districts to the same screen: `cc_districts.json` (`blood_bank`, needs the Courtyard's Blood:
EXCLUDED), `dlc/735730_color_of_madness/campaign/town/districts/color_of_madness.districts.json` (`the_mill`,
`geologic_studyhall`, `tainted_well`, `miasmal_orchard`, paid in shards and memories from the Farmstead: EXCLUDED),
`dlc/4964110_fires_edge/features/fires_edge/campaign/town/districts/runaway_duelist.districts.json`
(`conservatory_of_steel`, `craftworks`, `archaeologic_salon`: "DLC: ask the owner", section 13).

In the mod (added after this inventory was written; nothing of it seen in game yet): `Core/Districts.cs` (rules,
`tests/CoreTests/DistrictTests.cs`), `Estate/Districts.cs` (state, payment, week end, hooks), `DistrictBuffs.cs`
(class buffs as stats containers), `DistrictText.cs`, `DistrictsPanel.cs` (the screen), `DistrictsDev.cs` (bridge
commands `districts.*`), `tools/preview_districts.py`. What each building does here:

| id | in the mod |
|---|---|
| `spire_of_hope` | nothing, as in DD1 |
| `bank` | 5% of the estate's gold at every week's end (the fraction as a chance of one more gold) |
| `illuminators_guild` | DD1's own torchlight table is replaced by the building's for every expedition; of it the mod reads scouting (+2.5 points at any light). Its loot-of-the-dark list is handed to `LootTables`, which nothing asks yet; surprise and the fight's numbers are not the mod's (DD2's flame) |
| `granary` | 4-10 food in the provision shop's bag, rolled once a week; +15% on what a meal, a hunger stop and a bite out of the bag heal |
| `buskers_corner` | every idle hero sheds 10 of DD1's 200 stress a week: half a DD2 point, as a 50% chance of one |
| `house_of_the_yellow_hand` | bounty hunter, grave robber, highwayman: +4% crit (stats container); +5 points of scouting per such hero in the party |
| `altar_of_light` | crusader, vestal, flagellant: +10% stun resistance, +10% healing dealt |
| `training_ring` | man-at-arms only (DD2 has none of DD1's other four): +4% damage dealt for DD1's +4 ACC (the camping buffs' stand-in), +10% max health |
| `library` | occultist, plague doctor: +15% blight and debuff resistance piercing for DD1's skill chances; 15 of 200 stress shed at a curio tagged Knowledge (75% chance of a DD2 point) by the hero who steps up |
| `theater` | jester: +10% stress resistance for DD1's -10% stress, +2 speed; the Finale's +20% damage is NOT given (no per-skill stat) |
| `outsiders_bonfire` | +2 respite points at every camp with an abomination, hellion, leper or runaway in the party |

Blueprints: 1 from "Cornerstones", 1 for each won story quest whose DD1 quest's monsters carry the loot line
(`BlueprintSources`: fourteen quests; the Harvest Child's stand for the Flesh and pay none). A boss's blueprint is
handed over where the expedition's bag is (`Districts.BossLoot` in `DungeonRun.Finish`) and shows as a card of
the results screen's "Collected Heirlooms" (DD1's own picture of it, `ItemDef.IconRoot`).

## 1. Missing or partial, fits: the order I would add them

1. **Raid results, page 1** (Victory! / Escape / Defeat banner over the dungeon's picture; Quest Rewards; Collected Treasure counted item by item into gold; Collected Heirlooms; Next). Missing: the mod ends an expedition with text lines in the hamlet. M. Section 9.
2. **Raid results, page 2** (each hero: portrait or the dead-hero portrait, "+N Resolve XP", the resolve bar filling, level-up pulse, New Quirks; Back / Return to Town). Missing. M. Section 9.
3. **Districts ("the outskirts")**: the screen, the blueprint currency from veteran and champion bosses, the eleven buildings and their bonuses, the "Cornerstones" event. Written since (section 0), not seen in game. Sections 0 and 4.
4. **Quirks and diseases gained at the end of a quest** (what page 2 lists as "New Quirks"): DD1's chances by stress and outcome. Missing. M. Section 5.
5. **Loading screens** (region picture, quest title, a tip, "press to continue", the town-visit picture on the way home): also the cure for "the screen pops in after black". Missing. M. Section 2.
6. **Buildings boarded up until earned** (tavern and abbey after 2 quests, blacksmith and guild after 3, sanitarium after 4, survivalist at region level 2). Missing; the locked art path exists. S. Section 3.
7. **Surprise at the start of a fight by DD1's chances** (10% each way, torchlight's share, a scouted fight never surprises the party). Partial: only camp buffs and night ambushes use the mod's surprise switch. S. Section 8.
8. **Weekly upkeep of the roster**: idle heroes shed 5 stress a week; dismissing a hero stresses the others; a hero under a quest's level starts it stressed. Missing. S. Section 5.
9. **The Ancestor's exploration lines** (torch lit and out, hunger, starving, trap, obstacle, curio, loot, half health): DD1's clips and subtitles are already readable. Missing. S. Section 11.
10. **Wandering bosses**: the Collector on a nearly full bag, the Shambler at its altar or in the dark. Partial: fights and triggers are data in `Data/dungeons.json`, no code fires them. M. Section 6.
11. **The Darkest Dungeon's own rules**: Never Again, the veteran's +50% resolve XP for the party, the roster's XP boost after a failed descent, no scouting and no surprise there, no torch burn in the last descent. Partial: only the retreat cost, the no-retreat finale and the stress wipe are in. M. Section 6.
12. **Embark and quest warnings, party names** (DD1's "not much food" question exists; warnings for unworn trinkets, stress and levels do not; DD1's party names are not shown). Partial. S. Section 6.
13. **Party order in the corridor** (drag a hero to another rank, as DD1's raid screen allows). Missing. S-M. Section 8.
14. **Tavern and abbey leftovers**: the Caretaker taking a slot, the town buffs and trinket gains or losses among the side effects, the coach's guaranteed veteran after a death. Partial. S. Section 4.
15. **Building navigation strip, glossary, hero rename** (three small hamlet pieces). Missing. S each. Section 3.
16. **Tutorial pop-ups** (DD1's pictures and texts for the first torch, curio, camp, trap, resolve level, stage coach ...). Missing. M. Section 12.
17. **Raid inventory details**: trinkets as bag items that can be worn mid-expedition, DD1's journal pages, the curio tracker. Partial. M. Section 8.
18. **Quirk compulsions at curios** (a kleptomaniac hero opens the chest and keeps the loot). Missing; needs a DD2 quirk map. M. Section 8.
19. **Brigand Incursion** (the town event that starts a fight in the hamlet's own streets, with building steps lost if ignored). Written since (section 6), not seen in game.
20. **The Shrieker** (a thief takes eight trinkets; a quest wins them back; "Shrieker's Prize"). Written since (section 6), not seen in game.
21. **Town by time of day** (DD1 regrades the hamlet as the visit goes on). Missing, cosmetic. S. Section 3.
22. **Ending**: the epilogue, the credits, the campaign going on afterwards. Partial (the last fight can be played). M. Section 10.

Needs the owner's word before anything is built (details in the tables): estate naming, several estates and
save slots, Radiant / Darkest / Stygian rules with the Stygian game over, New Game+, DD1's intro films,
DD1's gameplay toggles, DD1's own character sheet, the other DLCs' content (section 13).

## 2. Front end, saves, loading

| mechanic | DD1 source | mod status (evidence) | fits? | effort | what it needs |
|---|---|---|---|---|---|
| Title screen with save slots (scrolling list, estate name, week, place, delete) | `fe_flow/fe_flow.layout.darkest` (`fe_flow_slot`, `fe_flow_positions`), `fe_flow/saveslot*.png`, `nukesave_*.png` | partial: one estate behind the main menu's Estate entry, Continue / New Estate asked twice (`Menu/EstateMenu.cs`, MODLOG: Sixth round) | owner: DD2 has its own profile list; several estates mean several snapshot slots | M | a slot picker in `EstateMenu`, slot ids in `EstatePersistence.FindSnapshot` (today "newest wins": MODLOG: Steam's cloud and the save tools) |
| Naming the estate | `shared/name/name.layout.darkest`, strings `estate_title_format`, `estate_title_default_data` | partial: the plate always reads "The Darkest Estate" (`Estate/HamletScreen.cs:124`) | owner | S | a text field at New Estate, the name in the estate's save section |
| Campaign modes: Radiant, Darkest, Stygian / Bloodmoon | `modes/base`, `modes/radiant`, `modes/new_game_plus` (`mode_settings.json`, `campaign/estate/estate.json`, `campaign/roster/roster.variables.json`, `campaign/quest/quest.restriction.json`, `campaign/progression/progression.json`, `shared/rules.json`), `fe_flow` `mode_select_dialog_*` | missing: base tables only (no file mentions radiant or new_game_plus) | owner: balance is deliberately last (MODLOG: User directions, fourth round) | M | `Dd1Install.ReadText` to look in `modes/<mode>/` first; a choice at New Estate; saved with the estate |
| DLC / mod choice per save | `fe_flow/dlc.layout.darkest`, `fe_flow/ugc.layout.darkest` | missing | no: DD2 handles its own DLC | - | - |
| Intro films | `video/house_of_ruin.ogv`, `video/old_road.ogv` (+ `.sub`) | partial: the films' subtitles can be read as the prologue chapters of the memoirs (`Estate/Memoirs.cs:20`, `:66`); no film is played, and nothing plays when an estate is founded | owner: Theora video has no player in DD2's Unity; the story text differs (docs/STORY.md) | L | a Theora decoder, or stills with the `.sub` subtitles and the voice clip |
| Tutorial quest "The Old Road" and the first Ruins quest | `maps/tutorial_crypts.dm`, `plot_tutorial_crypts` in `campaign/quest/quest.plot_quests.json`, `scripts/starting_save/*`, `scripts/starting_roster.darkest` | partial: the first story quest stands for `plot_tutorial_crypts` (`Data/plot_quests.json` `plot_first_descent`); no Old Road; a new estate starts with 4 heroes where DD1 starts with 2 and sends a Plague Doctor and a Vestal on the first coach (`stage_coach.building.json` `first_hero_classes`) | owner (the 4-hero start was a decision) | M | a scripted two-fight road before the hamlet |
| Loading screen: region picture, quest title, tip, continue | `loading_screen/loading_screen.layout.darkest`, `loading_screen.<region>_0.png`, `loading_screen.plot_*.png`, `loading_screen.town_visit.png`, `loading_screen.old_road.png`, `tipoverlay.png`, `titleoverlay.png`; strings `str_<region>_tip_N`, `str_town_tip_N`, `str_loading_screen_continue`; narration `loading_screen_start` | missing (no file mentions loading_screen); MODLOG fifth round item 0b asks that the Estate "come up out of the dark" | yes | M | a `LoadingScreen` canvas shown from Embark until the corridor is built and from "Return to Town" until the hamlet is up; tips filtered to mechanics the mod has |
| Options that are DD1's own (see section 12) | `shared/options/options.value_definitions.json`, strings `menu_options_element_*` | missing: DD2's settings menu is used | owner | M | - |
| Credits | `shared/credits/credits.cdt`, `credits.layout.darkest` | missing | no (the mod has its own credits to give; DD1's are Red Hook's) | - | - |
| Controls / help sheet | `shared/controls/*`, `shared/help/help.background.png` | missing | no: DD2's own | - | - |

## 3. Estate and the weekly loop

| mechanic | DD1 source | mod status (evidence) | fits? | effort | what it needs |
|---|---|---|---|---|---|
| Week counter, a week passes per expedition | `str_week`, `campaign/town/town.layout.darkest` | present (`Estate/EstateState.cs` `AdvanceWeek`, `DungeonRun.Finish`) | - | - | - |
| Hamlet view, name plates, building art by upgrade level | `campaign/town/town.layout.darkest`, `town.anim.darkest`, `fx/town_<building>_level0N` | present (`Estate/HamletScene.cs`, MODLOG: State of the mod) | - | - | - |
| Buildings locked until earned | `requirements` in every `campaign/town/buildings/<b>/<b>.building.json`: tavern 2 quests, abbey 2, blacksmith 3, guild 3, sanitarium 4, camping_trainer `highest_dungeon_level` 2; others 0 | missing: all ten open in week 1; `HamletScene.SetBuilding(id, level, locked)` exists and is only ever called with `locked: false` (`Estate/UpgradeWindow.cs:111`) | yes | S | read `requirements`, compare with `QuestBoard` finished count and region levels, pass `locked`, refuse the click with DD1's tooltip |
| Town-progression goals of the first weeks (explore, win 2 fights, see an affliction, see death's door) | `campaign/quest/quest.types.json` `town_progression_goal_ids`, goals `town_progression_*` | missing | no: they exist to pace DD1's tutorial | - | - |
| Estate bar: gold, heirlooms, activity log, trinket inventory | `campaign/town/estate_summary/estate_summary.layout.darkest`, `fx/estate_activity_log`, `fx/estate_realm_inventory` | present (`Estate/EstateSummary.cs`, `ActivityLogTownPanel.cs`; MODLOG: DD1 town panels added, Four quick fixes) | - | - | - |
| Estate bar: districts button | `fx/estate_districts`, `campaign/town/district/district.icon.png` | written, not seen in game: the row's fifth place, DD1's plans under the ruler, dimmed with "Districts Locked" until "Cornerstones" (`Estate/Districts.cs` `AddButton`) | - | - | section 0 |
| Estate bar: glossary button and screen | `fx/estate_glossary`, `campaign/town/glossary/glossary.layout.darkest`, 82 terms `str_glossary_term_N` / `str_glossary_term_definition_N` (base and DLC tables together) | missing | yes, with the terms the mod has (drop accuracy, dodge, corpses ...) | S-M | a scroll list on DD1's layout; a filter list of term numbers |
| Estate bar: settings button | `fx/estate_settings` | partial: Escape opens DD2's pause menu (MODLOG: The blue-and-white screen) | no | - | - |
| Heirloom exchange | `campaign/heirloom_exchange/heirloom_exchange.json`, `campaign/town/heirloom_exchange/*` | present (`Core/HeirloomExchange.cs`, `Estate/HeirloomExchangePanel.cs`) | - | - | - |
| Activity log and Caretaker goals | `campaign/town/activity_log/activity_log.layout.darkest`, `activity_log/*.png` | present (`Estate/ActivityLog*.cs`) | - | - | - |
| Trinket inventory (realm inventory), selling | `campaign/town/realm_inventory/realm_inventory.layout.darkest` | present (`Estate/RealmInventory*.cs`). The fault of gold and trinkets sharing DD2's fixed inventory slots is gone ("one inventory", 2026-10-05: the purse is a number, the unworn trinkets a list of the estate's own, DD2's inventory is kept empty; built, not yet seen in game) | - | done with the owner's step 4 ("two inventories") | - |
| Building navigation strip (hop between buildings from inside one) | `campaign/town/building_navigation/building_navigation.layout.darkest`, `building_navigation_building_layout_<b>` | missing | yes | S | a column of ten icons in `Estate/RosterWindow.cs`' frame, with the exclamation mark and locked overlay |
| Town by time of day | `town.layout.darkest` `time_of_day` (`time_per_grade` 65 s, `transition_time` 2 s), `campaign/town/town_render_data.json` | missing | yes (cosmetic) | S | a tint over `HamletScene` stepped every 65 s |
| Town events | `campaign/town_events/*.json` | present: 42 of 48 when this was written (`Core/TownEvents.cs`, `Estate/TownEvent*.cs`, docs/recon/town-events.md section 3); three of the six left out are staged since (the Brigand Incursion, "Shrieker's Prize", "A Thief in the Night": section 6's two rows; `events.list` over the bridge says what is still out and why) | - | - | the others left out are rows below |
| Town event set pieces on the hamlet | `fx/town_event_<id>` (27 Spine pieces), `sprite` / `sprite_attachment` of an event in `campaign/town_events/base.town_events.events.json` | missing: `HamletScene.cs` draws none; `TownEventPanel` shows the crier's notice | yes (cosmetic) | S-M | draw the event's Spine piece in `HamletScene` while the event lasts |
| Idle heroes shed stress each week | `campaign/roster/roster.variables.json` `town_visit_town_progression.idle_hero_stress_heal` 5 (Radiant 10) | missing: "the estate has no such weekly relief of its own" (`Estate/TownEventEffects.cs:233`), only the event One Good Week gives it | yes | S | in the week-end pass: 5 of 200 scaled to DD2's 10 as a chance of one point, the way `IdleRelief` already does |
| Stress relief cost by hero level | `campaign/estate/estate.json` `hero_activity_cost_multiplier_table` | present (`Estate/Resolve.cs:38` sets `ActivityRules.HeroLevel`) | - | - | - |
| What a failed quest keeps | `campaign/estate/estate.json` `quest_fail_keep_rates` | present (`Core/InventoryRaidRules.cs`, `DungeonRun.BringHome`) | - | - | - |

## 4. Town buildings

| mechanic | DD1 source | mod status (evidence) | fits? | effort | what it needs |
|---|---|---|---|---|---|
| **Districts**: screen, blueprints, 11 buildings | section 0 | written, not seen in game (section 0 says what each building does here; left out: the tutorial pop-ups, the Finale's damage) | yes (named by the owner) | done | the plan this row had before the work: (1) currency `blueprint` in `EstateState` and on the bar; (2) the `BLUEPRINT` drop on a veteran or champion story boss in `QuestBoard.Finished`, plus the Cornerstones event in `Core/TownEvents.cs` (`districts_unlocked`, `bonus_currency`); (3) `DistrictsPanel` on `district.layout.darkest` with the Spine buildings (`Dd1/SpineSkeleton.cs` already plays them); (4) effects: bank interest in the week-end pass, granary food into `ProvisionScreen`, puppet theatre into the idle relief (section 3), bonfire into `Core/CampingRules` respite, cartographers into `Core/RaidRules.LightBands`, athenaeum into the curio outcome for tag Knowledge, the five class buffs as DD2 stat containers like the blacksmith's (`Estate/Blacksmith.cs`). Class tags map directly for 12 of the mod's 15 classes; the Duelist has none (DD1 gives her Fire's Edge's Conservatory of Steel) and the Training Ring keeps only the Man-at-Arms: owner to say who else trains there. Accuracy has no DD2 number (tokens): pick a stand-in |
| Stage Coach: weekly recruits, roster size, hire, dismiss | `campaign/town/buildings/stage_coach/stage_coach.building.json` | present (`Estate/StageCoach.cs`, `RosterLifecycle.cs`) | - | - | - |
| Stage Coach: experienced recruits | same, `upgraded_recruits_upgrades` | present (`StageCoach.RollTier`, `Season`) | - | - | - |
| Stage Coach: a guaranteed veteran after a levelled hero died on the last quest | same, `guaranteed_previous_raid_dead_hero_levels` | not found in `StageCoach.cs` | yes | S | remember the dead of the last expedition with their levels; force a tier on the next roll |
| Stage Coach: deck-based class draw | option `deck_based_stage_coach`, `generation` `.number_of_cards_in_deck` in `heroes/*/*.info.darkest` | replaced by the mod's class-and-path rule (MODLOG: third round) | no | - | - |
| Tavern, Abbey: three activities each, slots, price, relief, upgrades | `campaign/town/buildings/tavern/tavern.building.json`, `abbey/abbey.building.json` | present (`Estate/ActivityRules.cs`, `ActivityLedger.cs`, `BuildingPanel.cs`) | - | - | - |
| Side effects: refuses to leave, goes missing, gains a quirk, wins or loses gold | same, `side_effects.results` types `activity_lock`, `go_missing`, `add_quirk`, `add_currency`, `remove_currency` | present (`ActivityLedger.cs:242-270`) | - | - | - |
| Side effects: town buff, trinket won, trinket lost | same, types `apply_buff`, `add_trinket`, `remove_trinket` | missing: skipped with a comment (`ActivityLedger.cs:273`) | yes | S | `Trinkets.Roll` / remove a random unworn trinket (both exist); buffs through the stat container used for town events |
| Heroes refuse an activity because of a quirk | same, `requirements` `not_have_quirks` | present where DD2 has the quirk (`ActivityLedger.Refusal`) | - | - | - |
| The Caretaker occupies a slot some weeks | same, `miscellaneous.caretaker_friendly`; `shared/rules.json` `min_available_activity_slots_for_crier` | missing | yes | S | one random caretaker-friendly slot marked taken at week start, with DD1's portrait |
| Affliction cured by a week in town | same, `affliction_cure_upgrades` | n/a: DD2 has no lasting affliction | no | - | - |
| Sanitarium: remove a bad quirk, lock a good one, cure a disease, cure-all chance, slots, prices | `campaign/town/buildings/sanitarium/sanitarium.building.json` | present (`Estate/SanitariumRules.cs`, `SanitariumLedger.cs`, `SanitariumPanel.cs`); disease cure never exercised (MODLOG: Acceptance pass) | - | - | - |
| Sanitarium: dearer treatment of locked bad quirks | same, `permanent_negative_quirk_cost_upgrades`; `shared/rules.json` `quirk_negative_locked_after_turn_count`, `quirk_chance_to_lock_negative`, `quirks_max_locked_*` | not found | owner: DD2 quirks do not harden by themselves | M | a mod-side "hardened" flag per quirk and DD1's roll |
| Blacksmith: weapon and armour levels, discounts | `campaign/town/buildings/blacksmith/blacksmith.building.json`, `upgrades/heroes/*.upgrades.json` | present (`Estate/Blacksmith.cs`) | - | - | - |
| Guild: learn and master skills, discounts | `campaign/town/buildings/guild/guild.building.json` | present (`Estate/Guild.cs`) | - | - | - |
| Survivalist: camping skills | `campaign/town/buildings/camping_trainer/camping_trainer.building.json`, `raid/camping/default.camping_skills.json` | present (`Estate/Survivalist.cs`) | - | - | - |
| Nomad Wagon: weekly trinkets, discounts, size | `campaign/town/buildings/nomad_wagon/nomad_wagon.building.json` | present (`Estate/NomadWagon.cs`) | - | - | - |
| Graveyard: the fallen, cause and week | `campaign/town/buildings/graveyard/graveyard.layout.darkest` | present (`Estate/Graveyard*.cs`) | - | - | - |
| Ancestor's statue: memoirs by category | `campaign/town/buildings/statue/statue_media_info.json`, `statue.layout.darkest` | present as text chapters (`Estate/Memoirs*.cs`); the two films and DD1's boss recordings are not played | partial fit | - | see intro films, section 2 |
| Building upgrades with heirlooms | `upgrades/building/*.upgrades.json`, `campaign/town/buildings/upgrade/upgrade.layout.darkest` | present (`Estate/UpgradeRules.cs`, `UpgradePane.cs`) | - | - | - |
| Butcher's Circus building | `campaign/town/buildings/circus*/circus.building.json`, `dlc/1117860_arena_mp` | missing | EXCLUDED | - | - |

## 5. Roster and hero lifecycle

| mechanic | DD1 source | mod status (evidence) | fits? | effort | what it needs |
|---|---|---|---|---|---|
| Resolve XP and levels 0..6 | `campaign/roster/roster.variables.json` `resolve_level_thresholds`, `campaign/quest/quest.generation.json` `resolve_xp_table` | present (`Estate/Resolve.cs`); XP is granted to survivors of a successful quest only (`Resolve.OnExpeditionEnded`) | - | - | check against DD1 what a retreat pays (the data files do not say) |
| Heroes above a quest's level refuse it | `campaign/quest/quest.restriction.json` | present (`Resolve.RefusalReason`) | - | - | - |
| Heroes below a quest's level pay for it | `shared/rules.json` `effectiveDifficultyDungeonStartingStress` (0, 20, 30, 40, 50, 60, 70), `effectiveDifficultyStressDmgModifiers`, `effectiveDifficultyAfflictionOnsetModifiers` | missing (no file mentions effectiveDifficulty) | yes | S | at `DungeonRun.Begin`: quest difficulty minus hero level picks the row; stress applied at the door (scaled to 0..10), the stress modifier as a stat container for the expedition, and the warning on the Estate Map |
| Resolve XP bonuses | `shared/buffs/base.buffs.json` `resolve_xp_bonus_percent` buffs: `town_event_<region>_resolve_xp`, `completed_darkest_dungeon_quest_party_resolve_xp`, `darkest_dungeon_failure_roster_resolve_xp` | partial: the four town-event buffs (`TownEventEffects.cs`); the two Darkest Dungeon ones not | yes | S | section 6 |
| Level-up moment | `shared/resolve_level_bar/*`, `tutorial_popup_resolve_level_*`, `activity_log/hero_level_up_entry_backdrop.png` | partial: the level shows in the roster and the activity log writes "level gained"; no bar animation anywhere | yes | with results page 2 | - |
| Quirks gained at quest end | `shared/rules.json` `negStress0Success` 0.3, `negStress100Success` 0.5, `posStress0Success` 0.5, `posStress100Success` 0.4, `negStress0Failure` 0.4, `negStress100Failure` 0.7, `posStress0Failure` 0.4, `posStress100Failure` 0.25, `quirks_max_positive` 5, `quirks_max_negative` 5; `shared/quirk/quirk_library.json` (`incompatible_quirks`, `can_be_replaced_by_new_quirk`) | missing: no quirk is given in `DungeonRun.Finish`, `QuestBoard.Finished` or `Resolve` | yes: DD2 has positive and negative quirks and the calls are in use (`ActivityLedger` adds them) | M | per survivor: chance interpolated by stress between the 0 and 100 values, by outcome; draw from DD2's quirk pool; respect DD2's own slot limits; feed the result to page 2 |
| Diseases caught at quest end | `shared/rules.json` `disease_after_quest_min_resolve_level` 2, `disease_after_quest_min_chance` 0.05, `disease_max_chance` 0.32, `disease_hero_disease_resist_weight` 0.33 | missing | yes (DD2 has diseases; one slot: MODLOG: Acceptance pass) | S | same pass as the quirks |
| Stress carried home; town relief | - | present (DD2's own stress; `ActivityLedger`) | - | - | - |
| Afflictions and virtues outside a fight (act-outs in hallways and camps, refusing food, healing or camp skills; lasting until relieved in town) | `shared/trait/trait_library.json` (13 traits: `combat_start_turn_act_outs`, `reaction_act_outs`), `scripts/layout/screen.raid.act_out.darkest`, `scripts/timescript/overstressed_*.times` | n/a: DD2's meltdown / resolute is momentary | no | - | - |
| Heart attack at 200 stress | `shared/rules.json`, tutorial `heart_attack` | n/a (DD2's meltdown) | no | - | - |
| Death's door recovery (mortality debuffs) | `deaths_door` `.recovery_buffs` in `heroes/*/*.info.darkest`; option `deaths_door_recovery_debuffs` | present by stand-in: Kingdoms wounds, mended in town (`Estate/TownRest.cs`, MODLOG: Sixth round) | - | - | - |
| Permanent death, graveyard, party stress at a death | `shared/rules.json` `death_party_stress_chance`, `death_party_stress_damage` | present for death and graves (`RosterLifecycle.Kill`, `Graveyard.cs`); the party-stress roll for a death outside a fight is missing (no file reads `death_party_stress_*`; in a fight DD2 does its own) | yes | S | add to `RosterLifecycle.Kill` when no fight is on |
| Dismissal | `shared/character/icon_dismiss.png`, `str_hero_dismiss_warning_confirm` | present (`RosterLifecycle.Dismiss`) | - | - | - |
| Dismissal stresses the rest | `shared/rules.json` `dismissed_hero_stress_penalties` (level up to 4: 5; up to 12: 10; above: 20) | missing: "Not done" (docs/recon/roster-lifecycle.md section 7; it predates `Resolve`) | yes | S | in `Dismiss`: the row by the leaver's level, scaled, to every other hero |
| Rename a hero | `shared/character/icon_rename.png`, `character.layout.darkest` `rename_icon_pos`, `shared/name/name.layout.darkest` | missing | yes | S | a name override per guid in the roster's save section, a field in the roster row |
| Hero palettes (recolour) | `character.layout.darkest` `palette_icon_pos`, `heroes/<class>/<class>_A..D` | n/a: DD2 models; DD2 has its own skins | no | - | - |
| DD1 character sheet (quirks, resistances, class bonuses, skill and camping skill choice, gear, diseases) | `shared/character/character.layout.darkest` | replaced: right click opens DD2's sheet (MODLOG: State of the mod) | owner: DD1 look versus DD2's working sheet | L | - |
| Camping skill choice (4 of 7) | same, `character_camping_skill_grid_layout` | present at the Survivalist ("four ready", MODLOG parity table) | - | - | - |
| Roster column: sort buttons, drag, busy marks | `campaign/town/roster/roster.layout.darkest`, `roster_sort_*.png` | present (`Estate/RosterPanel.cs`) | - | - | - |
| Party compatibility (religious heroes refuse the Abomination) | `tag: .id "religious"` in `heroes/*/*.info.darkest` | missing | no: DD2 has its own relationships | - | - |
| Antiquarian's extras (bigger gold stacks, extra loot draws) | `heroes/antiquarian/antiquarian.info.darkest` `extra_stack_limit`, `extra_battle_loot`, `extra_curio_loot`; `inventory/base.inventory.extra_stack_limits.darkest` | n/a: DD2 has no such class | no | - | - |
| One hero per class and path, hero paths tree | - | the mod's own (MODLOG: third round) | - | - | - |

## 6. Quests

| mechanic | DD1 source | mod status (evidence) | fits? | effort | what it needs |
|---|---|---|---|---|---|
| Weekly quest generation, region levels, rewards | `campaign/quest/quest.generation.json`, `number.quest.generation.json`, `campaign/progression/progression.json` | present (`Core/QuestGeneration*.cs`, `QuestRewards*.cs`; four READINGs open: docs/recon/quests-and-trinkets.md section 2) | - | - | - |
| Quest types: explore, cleanse, gather, activate, inventory_activate, kill_boss | `campaign/quest/quest.types.json` | present (`Core/QuestObjective.cs`, `DungeonGenerator.cs`) | - | - | - |
| Estate Map: regions, markers, details, rewards, party tray | `campaign/town/quest_select/quest_select.layout.darkest` | present (`Estate/QuestPanel.cs`) | - | - | - |
| Party names on the Estate Map | `shared/party_name/party_name_library.json`, `localization/party_names.string_table.xml` (about 515 entries) | missing: the place shows the party's status text (`QuestPanel.cs:538`) | yes, for combinations of classes DD2 shares with DD1 | S | read the library, match the four class ids, fall back to the status text |
| Story boss quests, three tiers each | `campaign/quest/quest.plot_quests.json` `plot_kill_*_1..3` | present with DD2 bosses (`Data/plot_quests.json`, 24 quests); veteran and champion tiers unplayed (MODLOG: Acceptance pass) | - | - | - |
| Boss memoir on the loading screen, boss line in the first room | `loading_screen/loading_screen.plot_kill_*_N.png`, narration tags | partial: the mod's `intro_narration` text at the start | yes | with loading screens | - |
| Darkest Dungeon chain | `plot_darkest_dungeon_1..4`, `maps/DD_map1..4.dm`, `dungeons/darkestdungeon/quest_1..4` | partial: five descents on generated maps with DD1's art per quest (`Data/plot_quests.json` `plot_darkest_1..5`); descents 2-5 unplayed | - | - | hand-made `.dm` maps are not read (no `.dm` reader): L if wanted, owner to say |
| Darkest Dungeon: a retreat costs a hero; no retreat from the last; roster stress cleared on success | same, `retreat_party_kill_count`, `can_retreat`, `is_roster_stress_cleared_on_completion` | present (`QuestBoard.PayForRetreat`, `RetreatBlockReason`, `Finished`) | - | - | - |
| Darkest Dungeon: Never Again | `campaign/roster/roster.settings.json` `never_again_settings` (strict: may not re-enter; permissive: may, at 80 stress with `never_again_affliction`), option `never_again`, `shared/hero/icon_completed_darkest_dungeon_quest.png`, tutorial `quest_restriction_darkest_dungeon` | missing | yes | S | a per-hero flag set on a won descent; refuse on the Estate Map; the torch icon in the roster row |
| Darkest Dungeon: veterans raise the party's XP; a failed descent raises the roster's | `completed_darkest_dungeon_quest_party_resolve_xp` (+50%), `roster_buffs_to_apply_on_failure` `darkest_dungeon_failure_roster_resolve_xp` (+100% until a quest is completed, when the party's lowest level is 5: `roster_buff_on_failure_minimum_party_resolve_level`) | missing | yes | S | two multipliers in `Resolve.OnExpeditionEnded` |
| Darkest Dungeon: no scouting, no surprise, no stall rules | `is_scouting_enabled` false, `is_surprise_enabled` false | not found: scouting is rolled everywhere (`Core/Exploration.cs:453`) | yes | S | two flags on the quest passed to `Exploration` |
| Darkest Dungeon: the last descent's torch does not burn | `"torch_setting": "dd4"`, `scripts/raid_settings.json` (`burn_down_enabled` false, no overlays) | not found | yes | S | a flag that stops `ChangeLight` on tiles and hides the torch |
| Darkest Dungeon: Talisman of the Flame | reward `dd_trinket` (`trinket_unlock`) of `plot_darkest_dungeon_1`, `suggested_trinkets` of quest 2 | missing: "only logged" (docs/recon/quests-and-trinkets.md D5) | owner: needs a DD2 trinket to stand for it and the boss mechanic it answers | M | - |
| Quest and provision warnings | flags `has_quest_select_warnings`, `has_provision_warnings`; `shared/rules.json` `trinkets_equipped_warning_min_percent` 0.5, `trinkets_equipped_warning_dungeon_min_difficulty` 3; options `quest_select_warnings`, `provision_warnings` | partial: DD1's "not much food" question (MODLOG: Sixth round); nothing for unworn trinkets, stress, levels | yes | S | checks in `QuestPanel` before Provision, through the same confirm dialog |
| Event that guarantees a town event after a quest | `campaign/town_events/town_events.quest_type_event_guarantees.json` | present (MODLOG: Fourth round, "Silence in the Crypts") | - | - | - |
| Brigand Incursion (town invasion) | event `plot_quest_town_invasion_0` in `base.town_events.events.json`; `plot_town_invasion_0` (dungeon `town`, difficulty 6, `upgrade_tags_to_remove_on_ignore` building x3, `retreat_party_kill_count` 1); `dungeons/town/*`, `maps/town_invasion_0.dm`, `modes/new_game_plus/maps/town_invasion_0.dm`, `loading_screen.plot_town_invasion_0.png`, `quest_select_dungeon_layout_town` | written, not seen in game (`Estate/PlotQuests.cs`, `Core/PlotEventRules.cs`, `Core/Dd1MapFile.cs`; bridge `incursion.*`): the event is in the draw, its quest on the Estate Map for the week with DD1's questions, the expedition on DD1's own map read from the `.dm` (seven rooms; the room behind the locked side door is left out), DD2's pillagers and rabid dogs in the burning city's arenas, the Warlord for Vvulf (the Antiquarian's gang without The Binding Blade), three building trees lose their last step when another quest is taken that week (exe 0x94ad00), a retreat costs a hero | - | done | a look in game; DD1's ambience of the fight in the streets; the locked side room |
| The Shrieker | events `trinket_retention_add_from_storage` ("A Thief in the Night": week 42, 8 trinkets) and `plot_quest_crow_trinket`; quests `plot_trinket_retention_0..2`, `plot_crow_trinket` (`maps/crow_map1.dm`, `retreat_always_from_raid`, `trinket_retention_count` 8, `party_quirks_to_apply_on_completion` / `_on_failure` corvid quirks); `dungeons/weald/colour_grade_plot_crow_trinket_*.png`; `activity_log/trinket_retention_*.png` | written, not seen in game (`Estate/Shrieker.cs`, `Estate/PlotQuests.cs`, `Core/PlotEventRules.cs`; bridge `shrieker.*`): both events are in the draw; the hoard is the estate's section `shrieker` (the thief's eight, and what a lost party wore); "Shrieker's Perch" is offered while the hoard holds eight with one uncommon or better and gives the rarest eight back (exe 0x931dd5, 0x931608); the perch is DD1's one-room map; DD2's carrion eaters at their forest den stand for the Shrieker, the fight ends as the fifth round begins (DD1: `performing_turn_min` 12 at three turns a round), the nest's loot only for a fight won outright; a random DD2 quirk of the same sign stands for each corvid quirk drawn ("Regroup!" is not the Shrieker's: it belongs to DD1's epic quests) | - | done | a look in game; DD1's colour grades and log icon of the quest |
| Wandering bosses: Collector, Shambler | DD1: Collector in hallways with a filling bag, Shambler from `shamblers_altar` (curios) or in the dark | partial: fights, tiers and triggers written in `Data/dungeons.json` `wandering[]` (`inventory_fill` 0.79, `darkness_or_altar`); `DungeonContent` only reads them for a named boss; nothing rolls a trigger | yes | M | a roll per hallway tile in `DungeonRun` from the `trigger` block; the altar's torch question in the curio flow; a limit per quest |
| Courtyard quests, invitations, Farmstead quests, endless harvest | `dlc/580100_crimson_court/features/crimson_court/campaign/quest/*`, `dlc/735730_color_of_madness/campaign/quest/*` | missing | EXCLUDED | - | - |

## 7. Provisioning

| mechanic | DD1 source | mod status (evidence) | fits? | effort | what it needs |
|---|---|---|---|---|---|
| Provision screen: store and party grids, prices by length and difficulty, embark | `campaign/provision/provision.json`, `campaign/town/provision/provision.layout.darkest` | present (`Core/Provision.cs`, `Dungeon/ProvisionScreen.cs`) | - | - | - |
| Class supplies, quest items handed out, firewood by length | `provision.json`, `quest.types.json` `starting_items` | present (docs/recon/dd1-raid-ui.md section 7: "a hero's tooltip says what they bring") | - | - | - |
| Event prices and stock | town events `provision_supply_*`, `provision_reduced_amounts` | present (hooks `ProvisionPriceFactor`, `ProvisionStock`) | - | - | - |
| Free food from the Granary | `DistrictSupplyBuffData` | written, not seen in game (`Provisioning.NewShop` asks `Districts.FreeFood`) | - | - | - |
| Gold taken as things are bought | - | differs: the bill is paid on setting out (dd1-raid-ui.md section 7) | - | - | - |
| "Standard Kit" button | - | the mod's own | - | - | - |

## 8. Raid exploration

| mechanic | DD1 source | mod status (evidence) | fits? | effort | what it needs |
|---|---|---|---|---|---|
| Rooms and hallways from DD1's generator | `scripts/map_generator.darkest`, `dungeons/<d>/<d>.*.mash.darkest` | present (`Core/DungeonGenerator.cs`, `GenerationRules.cs`) | - | - | - |
| Corridor view, camera, light, door sequence, walk | `scripts/camera.darkest`, `world.darkest`, `raid.lighting.darkest`, `scripts/timescript/*.times` | present (`Dungeon/Corridor*.cs`; hallway ends still open: MODLOG fifth round item 0) | - | - | - |
| Map, travel by clicking a room | `scripts/layout/panel.map.darkest` | present (`Dungeon/Minimap.cs`) | - | - | - |
| Torchlight: loss per tile, torches, bands | `shared/rules.json` `tile_light_loss`, `darkness` | present (`Core/RaidRules.cs`, `Exploration.cs`); the band's fight numbers go through DD2's flame, its scouting and loot shares are DD1's | - | - | - |
| Surprise at the start of a fight | `shared/rules.json` `surprise_corridor_*`, `surprise_room_*` (0.1 each), `surprise_known_*` (party -1, monsters 0.25), `surprise_max_*` 0.65; band values `monsters_surprised_increase`, `heroes_surprised_increase` | partial: `FightSurprise` (`Dungeon/CampBuffs.cs:172`) is set only by a camp buff's chance and by a night ambush (`DungeonRun.cs:841`, `:1143`); "the mod has no surprise of its own" | yes | S | in `DungeonRun.StartFight`: base + band + camp buff, capped; scouted fights use the "known" row; never a boss |
| Scouting, on entering a room and from curios; reveal on the map | `scouting_chance_base` 0.25, `scouting_crit_success` | present (`Core/Exploration.cs`, `Dungeon/Scouting.cs`) | - | - | - |
| Hallway stress, more when backing up | `hallway_stress` | present (`RaidRules.WalkStress`, `BackingUpStress`) | - | - | - |
| Things moving into walked hallways | `corridor_return_content` | present (`Exploration.RollReturnContent`) | - | - | - |
| Curios: outcomes, items used on them, loot, quirks, diseases, purge, scouting | `curios/curio_type_library.csv`, `curio_props.csv` | present (`Core/CurioCatalog.cs`, `DungeonRun`); "Teleport" and "Summon" results to verify | - | - | - |
| Curio tracker (what an item did last time) | `panels/icons_curio_tracker`, option `curio_tracker` | missing: left out (docs/recon/corridor-props.md section 11) | yes | S-M | a per-estate memory of (curio, item) results and the small icons on the curio scroll |
| Quirk compulsions at curios | `shared/quirk/quirk_library.json` `curio_tag`, `curio_tag_chance` (21 quirks, e.g. kleptomaniac Treasure 0.35, curious All 0.2), `keep_loot` (3 quirks); `shared/rules.json` `curioTriggerChance_*` | missing | owner: DD2's quirks carry no curio tags; a map of DD2 quirks to DD1 tags has to be invented | M | the roll before the curio's question, the bark, the kept loot |
| Traps: spotted or not, disarm, sprung effects by level | `props/trap_definitions.json`, `props/prop_definitions.json`, `trap_scout_disarm_bonus`, `difficulty_trap_base` | partial: the flow and the rules are in (`Exploration.ResolveTrap`), but a spotted trap asks a question where DD1 disarms on a click, and a sprung trap is a flat 10% health and 1 stress (`DungeonRun.SpringTrap`) instead of the definition's effects | yes | S | apply `TrapLevel.Health` and its effects; real play still owed (MODLOG fifth round item 5) |
| Obstacles: shovel or by hand | `props/obstacle_definitions.json` | present (`Exploration`, `RaidRules.Obstacle`) | - | - | - |
| Hunger and meals | `hallway_hunger_*`, `meals_table`, `provision_hp_heal` | present (`Exploration`, `Camp`); eating outside a hunger tile to heal 5% to verify | - | - | - |
| Secret rooms | `dungeons/_shared/secretroom.png`, generator `secret_rooms` | present (MODLOG: Verified this round) | - | - | - |
| Camping | `raid/camping/default.camping_skills.json`, `camp_*`, `ambush_camping_base_chance` | present (`Core/Camping*.cs`, `Dungeon/Camp*.cs`) | - | - | - |
| Retreat from a fight, abandon the quest | `combat_retreat_*`, `campaign/quest/quest.exit_penalty.json` | present (DD2's own retreat; `DungeonRun.Leave`, DD1's confirm dialog) | - | - | - |
| Battle loot and curio loot through the loot scroll, full bag choice | `scripts/layout/overlay.loot.darkest`, `loot/loot.json`, `loot/darkness_overrides.loot.json` | present for curios, camps and fights (`Core/LootTables.cs`, `Core/BattleLoot.cs`, `Dungeon/RaidScrolls.cs`): a won fight pays DD1's battle loot on the "Victory!" scroll and DD2's loot window never opens ("one inventory", 2026-10-05; built, not yet seen in game) | done with the owner's step 4 | - | - |
| Bag: 16 slots, stack limits, quest items, throwing away | `inventory/base.inventory.system_configs.darkest`, `base.*.inventory.items.darkest` | present (`Core/Inventory*.cs`) | - | - | - |
| Trinkets found on the way sit in the bag and can be worn | `scripts/layout/pannel.inventory.darkest`, `shared/hero/hero.layout.darkest` `hero_trinket_grid_layout` | present ("one inventory", 2026-10-05; built, not yet seen in game): a trinket found is an item of the bag with a card of its own, and outside a fight a click puts it on the selected hero or takes a worn one off into the bag (`RealmInventory.WearFromBag`, `TakeOffToBag`); a click where DD1 drags | - | done | drag and drop in the bag is the raid audit's own row |
| Journal pages | loot `journal_page`, `shared/journal_popup/*`, `localization/journal.string_table.xml` (`journal_page_text_N`) | missing: the drop is skipped (`DungeonRun.cs:972`) | owner: the pages tell of DD1's own dead adventurers; they still read well | M | a collected-pages list in the estate, the pop-up, a reader (the Memoirs window could hold them) |
| Using supplies on a hero (bandage, antivenom, herbs, holy water, laudanum), keys and shovels on curios | `inventory/base.supply.inventory.items.darkest` | present (`Core/InventoryItems.cs`, `Dungeon/InventoryContent.cs`) | - | - | - |
| Changing the party's order | `shared/input_preview/input_preview.frame.dungeon_reset_party_order.png`, raid screen drag | missing: "the mod has no reordering of ranks" (dd1-raid-ui.md section 7); order is set on the Estate Map | yes | S-M | swap two heroes' slots in the party list and in `CorridorView`; DD2's fight reads the order |
| Changing equipped skills between fights | `tutorial_popup_map_combat_skills_*` | not found (the hero banner shows the five skills) | yes | M | to verify; DD2's skill picker opened from the banner |
| Hero barks at curios, traps, hunger | `localization/dialogue.string_table.xml`, `scrolls/bark_balloon*.png` | missing | owner: DD1's lines are DD1 heroes' voices; DD2 heroes have their own barks | M | - |
| Pop-up texts over heroes | `scripts/layout/base.popup_text.layout.darkest` | replaced by three log lines under the quest info (dd1-raid-ui.md section 7) | yes | M | DD2's own floating text, or DD1's layout |
| Quest goal panel, "Quest Complete!" seal, continue or return | `overlays/quest_complete.png`, `fx/quest_complete_seal`, `panels/quest_return_to_hamlet.png`, `quest_continue_raid.png` | present (`Dungeon/RaidOverlays.cs:463-508`) | - | - | - |
| Saving inside a dungeon | DD1 `persist.raid.json` | present (MODLOG: Verification round, "Killing the game mid-dungeon") | - | - | - |
| Prisoners, prison doors, the Fanatic, The Blood | Crimson Court | missing | EXCLUDED | - | - |
| Nightmare ambushes at camp | `dlc/702540_shieldbreaker/raid/flashback/shieldbreaker.flashbacks.json` | missing | DLC: ask the owner | - | section 13 |

## 9. Raid results

DD1 shows one screen in two pages when an expedition ends, whatever the outcome, before the town loads.

Layout `raid_results/raid_results.layout.darkest`; timings `raid_results/raid_results.anim.darkest`; art
`raid_results/raid_results.quest_completed_background.png`, `.quest_not_completed_escape_background.png`,
`.quest_not_completed_defeat_background.png`, `.quest_not_completed_regroup_background.png` (899x1080 each, at
`completion_background_pos` 960 0 over a full-screen `level_background`), `raid_results.items_frames.png`
(533x723), `raid_results.heroes_frames.png` (680x675), `deadhero_portrait.png` (85x85); effects
`fx/raid_results_loot_glow`, `fx/raid_results_quirk_reveal`, `fx/raid_results_resolve_pulse`; the bar
`shared/resolve_level_bar/*` (`resolve_level_bar.layout.darkest`, `.anim.darkest`, number backgrounds per level).
Strings in `localization/miscellaneous.string_table.xml`: `raid_results_quest_result_was_completed` "Victory!",
`..._was_not_completed_escape` "Escape", `..._was_not_completed_defeat` "Defeat",
`raid_results_quest_inventory_title` "Quest Rewards", `raid_results_party_gold_inventory_title` "Collected
Treasure", `raid_results_party_heirloom_inventory_title` "Collected Heirlooms", `raid_results_progression_heroes`
"Next", `raid_results_progression_return_to_town` "Return to Town", `raid_results_hero_resolve_from_quest` "+%d
Resolve XP", `raid_results_new_quirks_title` "New Quirks:". Sounds: the event names with `raidresults`,
`results_`, `treasure`, `resolve_level_` in `audio/master_banks/master_bank.strings.bank` (list them with the
mod's `audio.events`).

In the mod today (`Dungeon/DungeonRun.cs:1213-1275`): `Finish` applies everything at once (retreat stress,
`QuestBoard.Finished` pays the quest, `BringHome` turns the bag into gold and heirlooms, `Resolve` grants XP, the
week advances, the save is written) and tells it as lines of text over the hamlet through `QuestBoard.Report`
("The quest is paid: ...", "The party's bag is emptied onto the table: ..."). There is no results screen.

| mechanic | DD1 source | mod status (evidence) | fits? | effort | what it needs |
|---|---|---|---|---|---|
| Outcome banner over the region's picture: Victory! / Escape / Defeat, quest title | `raid_results_screen_layout` (`quest_result_pos` 960 150, `quest_title_pos` 960 212, `state_pos` 510 0, `progression_bar_pos` 0 958) | missing | yes | M (pages 1 and 2 together: M-L) | a `Dungeon/RaidResultsScreen.cs` shown by `Finish` after `CorridorView.Hide()` and before the hamlet; keep "apply first, show after" so a kill mid-screen loses nothing; map `RaidStatus.Succeeded / Abandoned / Failed` to the three backgrounds; what the left half shows is to be read off the owner's two DD1 screenshots (the layout only names a `level_background` at 0 0) |
| Quest Rewards row | `raid_results_items_state_layout` (`frame_offset` 450 230, `quest_inventory_title` 450 242, `quest_inventory_grid_offset` 84 308), `raid_results_quest_inventory_system_grid_layout` (8 columns, offset 80 160, centred) | missing; the data exists (`QuestBoard.Finished` knows gold, heirlooms, trinket) | yes | - | make `QuestBoard.Finished` return what it paid instead of only reporting it |
| Collected Treasure: each stack shown, its value popping up, the total counting | `party_gold_inventory_*` (title 200 490, grid 84 538, total 620 496; 6 columns); anim `treasure_item_wait` 0.3 s, `treasure_item_value_popup_offset_y` 60 px in 1.0 s, `treasure_gold_total_scale_pulse` 1.1 in 0.2 s | missing; `Inventory.Settle` gives only totals (`haul.Coins`, `GemGold`, `SupplyGold`) | yes | - | let `Settle` return the stacks with their DD1 values; show estate gold (DD1 / 50) |
| Collected Heirlooms and totals | `party_heirloom_inventory_*` (title 200 714, grid 84 760, total 620 720); `heirloom_total_scale_pulse` 1.4 | missing; `haul.Heirlooms` exists | yes | - | - |
| Trinkets brought home | shown among the items in DD1 | missing; `_trinkets` list exists (`DungeonRun.cs:1268`) | yes | - | a third row or in the treasure row |
| What a retreat or a defeat kept and lost | `campaign/estate/estate.json` `quest_fail_keep_rates` | rules present, not shown | yes | - | show only what came home |
| Page 2: hero rows | `raid_results_heroes_state_layout` (`frame_offset` 450 250, `heroes_start_pos` 130 250, `heroes_spacing` 0 168), `raid_results_hero_layout` (`portrait_icon_offset` 5 64, `name_offset` 0 16, `campaign_status_offset` 556 3, `resolve_from_quest_offset` 520 20, `new_quirks_from_quest_start_offset` 145 18, spacing 0 24) | missing | yes | M | per hero: XP and level before and after (`Resolve.Experience`, `Level`), the quirks and diseases of section 5, dead or alive |
| Resolve bar filling, level-up pulse, "+N Resolve XP" sliding in | anim `raid_results_heroes_xptext_xoffset` -175 to 0 in 2.0 s, `raid_results_heroes_resolve_xp_bar` 2.0 s; `fx/raid_results_resolve_pulse` | missing | yes | - | the bar widget is new and reusable in the roster row |
| New Quirks revealed one by one | anim `raid_results_heroes_quirk_text_wait` 0.666 s, `..._alpha` 0.8 s; `fx/raid_results_quirk_reveal`; art `shared/character/quirkreplaced.png`, `lockquirk.png` | missing (and no quirks are gained: section 5) | yes | - | - |
| Dead heroes on the list | `deadhero_portrait.png`, `portrait_dead_overlay_offset` 5 64 | missing; the graveyard has the names | yes | - | the party as it set out has to be remembered (dead actors are dropped by DD2) |
| Next / Back / Return to Town | `next_pos` 1870 980, `back_pos` 50 980, `return_to_town_pos` 1870 980 | missing | yes | - | then the town-visit loading screen (section 2) |
| "Regroup!" outcome | `raid_results.quest_not_completed_regroup_background.png`, `quest.exit_penalty.json` `regroup_penalty` | missing | only with the Shrieker (section 6) | - | - |
| Endless-wave results (kills, corpse pile, shards) | `wave_raid_results_layout`, `wave_raid_results.items_frames.png`, `raid_results.endless_wave_regroup_background.png` | missing | EXCLUDED (Farmstead) | - | - |
| Activity log entry of the quest | `activity_log/raid_success_banner.png`, `raid_failure_banner.png`, `raid_abandon_banner.png` | present (`Estate/ActivityLog.cs`) | - | - | - |
| Quest-end narration | narration `quest_end_completed`, `quest_end_not_completed` | present (docs/recon/town-events.md section 6) | - | - | - |

## 10. Game over, endings, New Game+

| mechanic | DD1 source | mod status (evidence) | fits? | effort | what it needs |
|---|---|---|---|---|---|
| Game over screen (estate name, weeks, dead heroes, reason) | `game_over/game_over.layout.darkest`, `game_over.background.png`; strings `game_over_*` | missing | only with Stygian rules | S | - |
| Stygian limits: 91 weeks, 13 dead heroes | `shared/rules.json` `new_game_plus_week_limit`, `new_game_plus_hero_death_limit`, `new_game_plus_monster_*` | missing | owner | M | with the mode choice (section 2) |
| A lost party does not end the campaign | - | present (MODLOG: Verification round, "A real wipe") | - | - | - |
| Epilogue film after the last descent | `plot_darkest_dungeon_4` `post_raid_video` "epilog", `video/epilog.ogv`, `epilog.sub` | partial: the epilogue's subtitles are a memoir chapter (`Estate/Memoirs.cs:69`); nothing is shown when the last descent is won | owner: the film is DD1's ending; the mod's story ends differently (docs/STORY.md) | M-L | the mod's own closing text on the narration band, or the film |
| Campaign continues after the ending; all memoirs unlocked | `modes/new_game_plus/mode_settings.json` `unlocks_all_statue` | to verify: the finale has only been smoke-tested (MODLOG: Smoke tests) | yes | S | play the last descent as a quest and see what `QuestBoard.Finished` leaves |
| New Game+ unlocked by finishing | tutorial `new_game_plus`, `fe_flow_new_game_plus_confirm_*` | missing | owner | M | - |
| Achievements | `localization/achievements.string_table.xml` (105 titles), `has_achievement` on plot quests | missing; the save design keeps DD2's own achievements from firing in the Estate (patch `NoAchievementsFromEstate`, docs/recon/estate-save-design.md section 5) | no | - | - |

## 11. Narration

DD1's table `audio/narration.json` has 36 moments. The mod reads it and plays the clips with subtitles
(`Estate/Narration*.cs`, `Dd1/Dd1Audio*.cs`).

| moments | mod status (evidence) | fits? | effort | what it needs |
|---|---|---|---|---|
| `town_visit_start`, `enter_building`, `enter_quest_select`, `enter_provision_select`, `upgrade_building`, `recruit_hero`, `dismiss_hero`, `quest_start`, `quest_end_completed`, `quest_end_not_completed`, `kill_hero`, `camp` | present (docs/recon/town-events.md section 6) | - | - | - |
| `torchlight_full`, `torchlight_out`, `hunger`, `hunger_starve`, `trap`, `obstacle`, `obstacle_clear_no_item`, `curio`, `loot`, `half_health_half_stress`, `enter_hallway` | missing: "not used although DD1 has them" (same section) | yes: all happen in the corridor, outside DD2's fight screen | S | calls to `Narration.Trigger` from the matching events in `DungeonRun.Process`, with the dungeon as tag |
| `loading_screen_start` | missing | yes | with loading screens | - |
| `combat_start`, `kill_monster`, `crit_hero`, `crit_monster`, `deaths_door`, `victory`, `battle_retreat`, `battle_retreat_fail`, `afflicted`, `virtue`, `change_monster_class` | missing on purpose: DD2's fights have their own narrator | no | - | - |
| `ancestor_talk` (the Ancestor's speeches in the Darkest Dungeon) | missing | no: about DD1's finale | - | - |

## 12. Tutorials and options that change rules

| mechanic | DD1 source | mod status (evidence) | fits? | effort | what it needs |
|---|---|---|---|---|---|
| Tutorial pop-ups | `shared/tutorial_popup/tutorial_popup.layout.darkest`, `tutorial_popup.<id>.png`, strings `tutorial_popup_<id>_title` / `_description` (69 titles over all tables). Base ones that describe things the mod has: `embark`, `quest_select`, `supplies`, `torch`, `hallway_nav`, `map_nav`, `curio`, `loot`, `disarm_trap`, `scout_hidden_door`, `camping`, `camping_quest`, `quest_complete`, `consider_retreat`, `resolve_level`, `stage_coach`, `stage_coach2`, `stress_relief`, `locking_pos_quirks`, `hero_panel`, `quest_restriction_darkest_dungeon`, `districts`, `blueprints` | missing (the word tutorial appears only in rule readers) | yes | M | one pop-up window, a "seen" set in the estate's save, a trigger per id; texts need a pass where the mod differs |
| Darkest Dungeon Config master switch | option `dd_mode` | missing | owner | - | - |
| Monsters leave corpses; combat delay penalties; standard enemy crits; combat retreats can fail; heart attacks | options `corpses`, `stall_penalty`, `multiplied_enemy_crits`, `retreats_can_fail`, `heart_attack` | n/a: fights are DD2's | no | - | - |
| Mortality debuffs | option `deaths_door_recovery_debuffs` | the wound stand-in is always on | owner | S | a config switch |
| Never Again: strict or permissive | option `never_again` | missing (section 6) | yes | with Never Again | - |
| Town events on or off | option `town_events` | to verify (`events.setting` exists in the dev bridge) | yes | S | a `[Rules]` entry in the BepInEx config |
| Keep trinkets of a lost party | option `keep_battle_quest_fail_trinkets` | n/a today: worn trinkets of the dead are not handled as DD1 does | owner | S | - |
| Embark warnings, quest warnings | options `provision_warnings`, `quest_select_warnings` | with the warnings (section 6) | yes | S | - |
| Curio tracker; auto-centre the map; party autosort; bark dismissal; tutorials on or off | options `curio_tracker`, `map_follow_party`, `roster_sort_party_to_top`, `bark_dismissal`, `tutorial` | missing | yes where the feature exists | S | config entries |

## 13. DLC content, for the owner to decide

| DLC folder | what it adds outside combat | status |
|---|---|---|
| `dlc/580100_crimson_court/features/crimson_court` | the Courtyard region and its quests (`campaign/quest/crimson_court.quest.*.json`), invitations, the Crimson Curse (`campaign/roster/roster.contagion.json`), the infestation meter (`campaign/progression/infestation.progression.json`, `campaign/town/infestation`), The Blood, the Fanatic, prisoners to rescue, its town events, the `blood_bank` district | **EXCLUDED** |
| `dlc/580100_crimson_court/features/districts` | Districts ("the outskirts") | recommended (sections 0, 4) |
| `dlc/580100_crimson_court/features/flagellant` | the Flagellant class | DD2 has its own Flagellant; nothing to bring |
| `dlc/735730_color_of_madness` | the Farmstead and the endless harvest, comet shards (`campaign/estate/com.estate.json`), the Jeweler's shard shop in the Nomad Wagon, shard mercenaries on the coach, quest modifiers (`campaign/quest/com.quest.modifiers.json`), four districts, five town events, crystalline monsters in the old regions, the wave results screen | **EXCLUDED** (all of it hangs on the Farmstead's shards) |
| `dlc/1117860_arena_mp` | the Butcher's Circus | **EXCLUDED** |
| `dlc/702540_shieldbreaker` | the Shieldbreaker class, her seven nightmares as camp ambushes (`raid/flashback/shieldbreaker.flashbacks.json`), snakes, Aegis Scales (`inventory/shieldbreaker.estate.inventory.items.darkest`), events `first_shieldbreaker`, `bonus_recruit_shieldbreaker` | DLC: ask the owner. DD2 has no Shieldbreaker; the nightmare pattern (a hero's past met at the camp fire) could be told with DD2's hero shrine stories |
| `dlc/445700_musketeer` | the Musketeer (an Arbalest in other clothes) | DLC: ask the owner. No DD2 class; nothing fits |
| `dlc/4964110_fires_edge` (The Fire's Edge) | the Duelist and the Runaway, trinkets, three districts (`conservatory_of_steel`, `craftworks`, `archaeologic_salon`), town events `outdoor_enthusiasts`, `a_bargain_for_twice_the_price`, `wellness_program`, `tangible_legacy`, `twinkle_in_her_eye`, `the_fires_edge`, `ashes_to_ashes`, `idle_resolve_level_duelist` | DLC: ask the owner. Both classes are DD2 classes, so the three districts and the class events would fit beside the base eleven once Districts exist |

## 14. Things checked and found complete enough to leave alone

Hamlet view and windows, stage coach, tavern and abbey core, sanitarium, guild, blacksmith, survivalist, nomad
wagon, graveyard, memoirs, building upgrades, heirloom exchange, activity log, trinket inventory, town events
(42 of 48), quest generation and rewards, the Estate Map, provisioning, the corridor, map, light, scouting,
hunger, obstacles, curios, secret rooms, camping, retreat and abandon, the bag, saving inside a dungeon, deaths
and graves, resolve levels and quest level caps. Their open points are verification, not missing mechanics, and
are listed in MODLOG ("Still to verify", "Acceptance pass", "User directions ... fifth round").
