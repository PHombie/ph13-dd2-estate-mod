# DD2 content catalogue (v2.04.85095)

Survey of Darkest Dungeon II's own data tables, for the DD1-campaign-on-DD2-content mod. Read-only survey, nothing was copied from the game; short quoted excerpts only.

**Sources read** (all read-only):

| short name | path |
|---|---|
| `EXCEL` | `E:\Steam\steamapps\common\Darkest Dungeon® II\Darkest Dungeon II_Data\StreamingAssets\Excel\` |
| `LOC` | `...\StreamingAssets\Localization\` |
| `MODEX` | `...\StreamingAssets\mods\test_token_creation_export\` |
| `AA` | `...\StreamingAssets\aa\` (`catalog.json` 13.1 MB, 810 `.bundle`, ~4.29 GB) |
| decomp | `D:\Mods\DD2\dd2-estate-mod\_ref\dd2-decomp\` (ILSpy output of `IronCrown`, in this repo, git-ignored) - used only to confirm loader behaviour (CSV loader, library merge rules, mod loader, locale loader) |

**Conventions.** Ids are quoted exactly as in the data (case-sensitive). `[D1]` = `dlc_dul_cru`, `[D2]` = `dlc_catacombs`, `[D3]` = `dlc_supporter`, `[exp]` = `Excel/expedition/`, `[kg]` = `Excel/kingdom/`. "Not verified" = inferred, not read from data or code. All counts were computed by parsing every `*.Group.csv` under `EXCEL` (553 files, 31,219 `element_start` blocks).

**Naming gotchas found while surveying** (they will bite id generators):

- Group-file names contain spaces (`coastal_bosun b_data_export.Group.csv`, `boss_brain lock health_data_export.Group.csv`, `shared_head ves_...`) but the ids inside use underscores (`coastal_bosun_b`, `boss_brain_lock_health`). A few ids differ from file names more: `shared_gaunt_lost_soul.csv` holds id `shared_lost_soul`; `shared_gaunt_woodsman.csv` holds `shared_lost_soul_yeoman`; `shared_gaunt_urchin.csv` -> `shared_lost_soul_urchin`; `shared_gaunt_widow.csv` -> `shared_lost_soul_widow`; `boss_stomach/torso/god.csv` -> `boss_body_phase1/2/3`; `lost_battalion_dreaming general.csv` -> `lost_battalion_boss_dreaming_general`; `lost_battalion_taproot.csv` -> `lost_battalion_boss_taproot`; `shared_head hwy.csv` -> `shared_collector_head_hwy` (and `shared_head highwayman.csv` -> `shared_head_hwy`).
- Hero abbreviations are inconsistent: Highwayman is `hwm` in `hero_hwm`/`boss_failure hwm`/`boss_body_failure_hwm`, but `hwy` in herostory files, path ids (`hwy_rogue`) and `shared_collector_head_hwy`. Grave Robber is `gr` (files) but `grv` (path ids). Plague Doctor is `pd` (files) but `plg` (path ids). Man-at-Arms is `maa`. Runaway `run`, Occultist `occ`, Vestal `ves`, Hellion `hel`, Jester `jes`, Leper `lep`, Flagellant `flg`.
- Region ids are PascalCase (`Farm`, `MountainBrain`), `m_BiomeGroupTag` and condition ids are lower-case (`farm`, `farm_biome_check`), and one region has two names: the Coast's condition is `shroud_biome_check` (display name "The Shroud").
- Display names of regions differ from ids: `City`="The Sprawl", `Farm`="The Foetor", `Forest`="The Tangle", `Coast`="The Shroud", `Cave`="The Sluice" (`biome_name_*` in `LOC/Sources/general.txt`).

## 0. Headline numbers

| thing | count | where |
|---|---|---|
| CSV tables | 553 (base 424, `dlc_dul_cru` 65, `dlc_catacombs` 48, `kingdom/` 10, `expedition/` 5, `dlc_supporter` 1, `dlc_origin_skins` 0) | `EXCEL` |
| `element_start` blocks | 31,219 (Effect 4,588; ActorDataStats 3,419; Condition 3,000; ActorDataEffects 2,977; Buff 2,798; ActorDataSkill 1,737; BattleConfiguration 1,099; LootTable 1,014; NarrationEntry 981; Cost 980; Unlock 970; StoryChoice 800 ...) | all |
| Region ids (`BiomeType`) | 15: Valley, ValleyIntro, ValleyKingdom, City, Farm, Forest, Coast, Tundra, Cave, Catacombs, MountainBrain/Lungs/Eyes/Arms/Body | `biome_data_export*` + decomp `BiomeType` |
| Enemy actor ids (non-hero, non-corpse, non-herostory) | 163 (incl. 34 Mountain boss parts) | section 3 |
| Hero class ids | 15 (11 base + Bounty Hunter + 3 DLC) | section 6 |
| `BattleConfiguration` | 1,099 (1,099 unique ids) | section 5 |
| `BattleConfigurationTable` | 291 elements / 253 unique ids (same-id elements merge) | section 5 |
| `Item` elements | 615 (602 unique ids): trinket 211, stage_coach_upgrade 118, currency 109, combat 85, rest 72, memory 20 | section 7 |
| `Quirk` | 230 (116 `_pos`, 84 `_neg`, 26 `disease_*`, 4 `crimson_curse_*`) | section 7 |
| `LootTable` | 1,014 | section 7 |
| `Token` | 225; `Buff` 2,798; `Effect` 4,588; `Condition` 3,000; `Dot` 77 | engine |
| `RunGoal` 281; `StoryChoice` 800; `KingdomEvent` 131; `Achievement` 121 | | |
| Localization | 19,034 English keys in `LOC/Sources/**/*.txt`; 14 foreign `.po` + English; Russian present | section 9 |
| Addressables | 810 bundles; 185 `.unity` scenes in the catalog; 31,269 non-bundle asset entries | section 11 |

## 1. CSV format (`*.Group.csv`)

### 1.1 Grammar (read from data and confirmed in decomp `ResourceGroupCsvDatabase` + `CsvUtils`)

- Files: `*.Group.csv`, pure ASCII, CRLF, no BOM (checked on all 553). The file name only has to end in `.Group.csv` (`GetValidResourceFilePaths(folder, "Group", "csv")`); what matters is the content.
- A file is a sequence of **elements**. The loader literally scans for the substring `element_start` ... `element_end` (`text.IndexOf`), so text between elements is ignored and blocks are independent.
- Element header line: `element_start,<id>,<ClassName>`. Cell 1 = element **id**, cell 2 = element **class** (the library the element goes into: `Biome`, `Boss`, `ActorDataClass`, `Item`, `LootTable`, `BattleConfiguration` ...). The line is split with `RemoveEmptyEntries`, so the id must be non-empty.
- Body lines: `key,v1,v2,...,` (always a trailing comma, an Excel-export artefact). Cell 0 = property key, the rest = values.
  - `m_Something,value,` = assigned by reflection onto the C# field of that exact name (`m_Chance`, `m_EnemyActors`). Unknown member -> logged as `CSV: <key> member does not exist` (not fatal) unless the class parses that key itself.
  - lower_snake keys without `m_` (`routes`, `road_events`, `hero_effects`, `actorless_effects`, `boss_modifiers`, `quest_steps`, `spawn_effects`, `skills`) are parsed by the owning class by hand; they are lists of ids of other elements.
  - Lists are the remaining cells; **empty cells are dropped** by the generic parser (`m_OrderedMidNarrationIds,` with nothing = no value; a line with only a key is ignored because `Length > 1` is required). The `Table` family (`BattleConfigurationTable`, `LootTable`, `UnlockTable`, `InnTable`) has its own parser where the parallel rows (`m_chances`, `m_ids`, `m_types`, `m_tags`, `m_conditions`, `m_qtys`) are index-aligned, and an empty cell in `m_tags`/`m_conditions` is meaningful (`m_tags,,wandering,wandering,` = option 0 untagged, options 1-2 tagged `wandering`).
  - Stats: `key_map,health_max,speed,speed_number_of_turns,` then `add_stats,90,0,1,` (positional); `multiply_stats,...`; single form `add_stat,<name>,<v>` / `multiply_stat,<name>,<v>`; nested form `sub_stat,<stat>,<sub-key>,<v>` (e.g. `sub_stat,resistance,stun,0.1` or `sub_stat,scout_node_chance,Inn,10`).
  - Values: booleans `True`/`False`; ranges in loot quantities `[1-2]`; condition lists joined with `+` (AND) inside one cell (`is_confessions+has_0_stagecoach_armor`); references to other elements are plain id strings.
- One **actor** is a bundle of elements sharing the id and differing in class: `ActorDataClass` (size, tags), `ActorDataStats`, `ActorDataEffects`, `ActorDataExternalBuffs`, then one `ActorDataSkill` + `ActorDataStats` + `ActorDataEffects` triple per skill (the skill element id = skill id).
- The same trick recurs everywhere: an `Unlock` and a `Cost` share an id (`stagecoach_1`: class `Unlock` + class `Cost`), a `Biome` and its `RunDataStats`, an `Item` and its `ActorDataExternalBuffs`/`Cost`. Ids are unique per class, not globally.
- Class inventory (element counts over all files): Effect 4588, ActorDataStats 3419, Condition 3000, ActorDataEffects 2977, Buff 2798, ActorDataSkill 1737, BattleConfiguration 1099, LootTable 1014, NarrationEntry 981, Cost 980, Unlock 970, StoryChoice 800, ActorDataExternalBuffs 657, Item 615, ActorDataClass 433, SkillReplacement 431, StoryDataEffects 417, RunDataStats 329, BattleConfigurationTable 291, RunGoal 281, Quirk 230, ActorEffectTrigger 226, Token 225, TorchLevel 133, KingdomEvent 131, BiomeModifier 127, Achievement 121, Dot 77, InnUpgrade 68, ActorDataPath 57, BossModifier 49, Route 46, QuestStep 45, RoadEvent 40, Biome 15, Boss 6 (+ ExtendedBoss 2), Gang 3, Quest 4, KingdomMap 6 ... (80 classes in total).

### 1.2 How files are loaded and combined (decomp `ResourceGroupCsvDatabase`, `ModMgr`, `Library.AddLibraryElement`)

| folder | when loaded | merge mode |
|---|---|---|
| `EXCEL/*.Group.csv` | always | add |
| `EXCEL/<dlc>/` (`dlc_catacombs`, `dlc_dul_cru`, `dlc_origin_skins`, `dlc_supporter`) | only if the DLC is owned (`DLCManager.DoesOwnDLCByName`) | add / merge into same id (`dlcNumber` is stored on the resource) |
| `EXCEL/<gametype>/` (`expedition`, `kingdom`) | for the current `GameType` (`GatherGameTypeResources`) | **override** (replace by id) |
| mod `<mod>/*.Group.csv` | mod enabled (and `SaveUtils.inMods` profile source) | add |
| mod `<mod>/<dlc>/` | DLC owned | add |
| mod `<mod>/<gametype>/` | current game type | add |
| mod `<mod>/Overrides/*.Group.csv`, `Overrides/<dlc>/`, `Overrides/<gametype>/` | as above | **override** |

Same-id rule (`Library.AddLibraryElement`): if an element with the same id already exists in that library then (a) override flag set -> the old one is removed and the new one added; (b) else if the element type implements `IAddable` -> the new one is merged into the old one; (c) else -> error log `Library: Not adding <type>-<id> because it already exists and is not Addable` and the new element is dropped. `IAddable` types found in the decomp: `Table<,>` (so `BattleConfigurationTable`, `LootTable`, `UnlockTable`, `InnTable` **append their option rows**), `DataContainer`, `ActorDataEffects`, `ActorDataActOut`, `DataAffinityTickTriggers`, `DataExternalBuffs`, `EffectDataContainer`, `ItemDefinition`, `DataNodeReplacements`, `ActorDataRunGoals`, `ActorDataSkillReplacement`, `StatDataContainer`, `DataStoryChoiceReplacements`. This is how the DLC files extend base tables: `dlc_dul_cru/battle_configuration_table_data_export_DLC1.Group.csv` declares a second `faction_mashes_road_confessions` whose single option (`lost_battalion_crusader_quest`, chance 999) is appended to the base table of the same id.

Practical consequence for the mod: to inject a DD1-style fight/quest into an existing road or loot table, ship a root-level `*.Group.csv` that re-declares the table id with only the new option rows (merge). To replace a class definition (`Biome`, `Boss`, an actor's `ActorDataClass`, a `BattleConfiguration`) the file must sit under `Overrides/`.

### 1.3 Annotated excerpt A - a region (`EXCEL/biome_data_export.Group.csv`)

```
element_start,City,Biome                      <- id "City", class Biome (id == BiomeType name, PascalCase)
m_StoryDefaultBattleConfigurationTable,faction_mashes_resist_master,   <- BattleConfigurationTable id for Resistance fights here
m_InnTable,default,                           <- InnTable id (inn_tables_data_export): which inn variants the region rolls
m_BiomeGroupTag,city,                         <- lower-case group tag (conditions such as city_biome_check)
road_events,banter_event_shared,ambush_event_shared,roadevent_city_burning_books,roadevent_city_corpse,roadevent_city_iron_spikes,
routes,route_safe,route_oblivion_tear,route_city_rough_patch,route_city_hazard,route_city_combat_faction,route_city_combat_gaunt,route_city_combat_pillager,route_city_combat_military,
element_end                                   <- both lists = ids of RoadEvent / Route elements (route_data_export)
```

### 1.4 Annotated excerpt B - one actor (`EXCEL/shared_collector_data_export.Group.csv`, abridged)

```
element_start,shared_collector,ActorDataClass          <- actor id = shared_collector
m_Tags,monster,boss,brain_blessing,...,shared_collector,   <- tags drive conditions, loot, blessings, glossary
m_Size,1,                                              <- rank size (1 = one slot, 2 = large; 3/4 = bosses)
element_end
element_start,shared_collector,ActorDataStats
key_map,health_max,speed,speed_number_of_turns,        <- column names ...
add_stats,90,0,1,                                      <- ... and their values (HP 90, speed 0, 1 turn)
sub_stat,resistance,stun,0.1,                          <- nested stat: resistance.stun = 0.1
sub_stat,resistance,blight,0.4,
element_end
element_start,shared_collector,ActorDataEffects        <- passive hooks: lists of Effect ids
spawn_effects,add_1_deaths_door_armor,add_1_deaths_door_infernal_flame,
turn_start_effects,add_1_logic_a_if_0_collector_heads,add_1_logic_a_if_1_collector_heads,add_1_logic_a_if_2_collector_heads,
element_end
element_start,collector_collect_call,ActorDataSkill    <- one skill; the element id is the SKILL id
m_IsFriendly,True,
launch_ranks,1,2,3,4,                                  <- ranks the actor must stand on
m_AllConditionIds,performer_has_any_logic_a,           <- Condition ids that must all hold
m_Tags,collector_collect_call,
element_end
element_start,collector_collect_call,ActorDataEffects  <- what that skill does
performer_apply_limit_effects,collector_summon_head_hwy,collector_summon_head_maa,collector_summon_head_ves,
performer_apply_limit,3,
element_end
```

Every `Effect` an actor references lives in `effect_data_export` (4,588 elements): `m_SummonClassActorId` effects spawn other actors, `m_ChangeClassActorId` effects transform them, `m_TokenAddId` adds tokens, etc.

### 1.5 Table files grouped by purpose (553 files)

Tags after a name: `[D1]` dlc_dul_cru, `[D2]` dlc_catacombs, `[D3]` dlc_supporter, `[exp]` Excel/expedition, `[kg]` Excel/kingdom. `_data_export` / `.Group.csv` suffixes stripped. Spaces are real (in file names).

**Enemy actors (per faction/shared)** - 135 files. One file per enemy actor (ids are in the file; each file holds ActorDataClass + ActorDataStats + ActorDataSkill + ActorDataEffects ... for the actor and its `_corpse`). Prefix = faction. Full id list in section 3.

- `beastmen_*` (7): `beastmen_huntsman`, `beastmen_meat_hook`, `beastmen_meat_post`, `beastmen_rot_claw`, `beastmen_rot_claw_elite`, `beastmen_tribecaller`, `beastmen_tribewalker`
- `cave_swine_*` (5): `cave_swine_brute_b`, `cave_swine_brute`, `cave_swine_skiver_b`, `cave_swine_skiver`, `cave_swine_skulker`
- `coastal_*` (10): `coastal_boss_leviathan`, `coastal_boss_leviathan_hand`, `coastal_bosun b`, `coastal_bosun`, `coastal_cabin boy`, `coastal_captain b`, `coastal_captain`, `coastal_docker`, `coastal_fish monger`, `coastal_wharf rat`
- `courtier_*` (7): `courtier_archduke`, `courtier_banquet`, `courtier_chevalier`, `courtier_cocoon`, `courtier_esquire`, `courtier_swarm`, `courtier_sycophant`
- `coven_*` (10): `coven_cauldron`, `coven_custode`, `coven_dedicant`, `coven_hateful_virago`, `coven_matres sequelae`, `coven_matres spider`, `coven_mothers mushroom`, `coven_mothers spitter`, `coven_mothers_familiar`, `coven_mushroom`
- `cultist_*` (11): `cultist_cherub`, `cultist_evangelist`, `cultist_herald`, `cultist_altar`[exp], `cultist_cardinal`[exp], `cultist_deacon`[exp], `cultist_exemplar`[exp], `cultist_altar`[kg], `cultist_cardinal`[kg], `cultist_deacon`[kg], `cultist_exemplar`[kg]
- `fanatic_*` (16): `fanatic_blind_shaman`, `fanatic_blind_shaman_ignited`, `fanatic_flayer`, `fanatic_flayer_ignited`, `fanatic_immolatist b`, `fanatic_immolatist`, `fanatic_librarian`, `fanatic_librarian_ignited`, `fanatic_librarian_stack_l`, `fanatic_librarian_stack_m`, `fanatic_librarian_stack_s`, `fanatic_pit_fighter`, `fanatic_sacrificial`, `fanatic_whipper_b`, `fanatic_whipper`, `fanatic_whipper_ignited`
- `lost_battalion_*` (9): `lost_battalion_arbalist_b`, `lost_battalion_arbalist`, `lost_battalion_bishop`, `lost_battalion_dreaming general`, `lost_battalion_drummer`, `lost_battalion_foot soldier`, `lost_battalion_knight_b`, `lost_battalion_knight`, `lost_battalion_taproot`
- `plague_eater_*` (12): `plague_eater_butcher_b`, `plague_eater_butcher`, `plague_eater_corpse`, `plague_eater_dinner cart`, `plague_eater_harvest fetid meat`, `plague_eater_harvest putrid meat`, `plague_eater_harvest table`, `plague_eater_lady`, `plague_eater_livestock_b`, `plague_eater_livestock`, `plague_eater_lord`, `plague_eater_maid`
- `shared_ancestor_*` (1): `shared_ancestor_statue`
- `shared_barricade_*` (4): `shared_barricade_cover1`, `shared_barricade_cover2`, `shared_barricade_spearman`, `shared_barricade_swordsman`
- `shared_carrion_*` (2): `shared_carrion eater mutated`, `shared_carrion eater`
- `shared_collector_*` (1): `shared_collector`
- `shared_death_*` (2): `shared_death`, `shared_death_headstone`
- `shared_dog_*` (2): `shared_dog rabid b`, `shared_dog rabid`
- `shared_gaunt_*` (4): `shared_gaunt_lost_soul`, `shared_gaunt_urchin`, `shared_gaunt_widow`, `shared_gaunt_woodsman`
- `shared_ghoul_*` (1): `shared_ghoul`
- `shared_head_*` (4): `shared_head highwayman`, `shared_head hwy`, `shared_head maa`, `shared_head ves`
- `shared_lost_*` (2): `shared_lost_soul_chirurgeon`, `shared_lost_soul_patient`
- `shared_pillager_*` (6): `shared_pillager_antiquarian`, `shared_pillager_artillery`, `shared_pillager_firemouth`, `shared_pillager_melee`, `shared_pillager_mongrel`, `shared_pillager_ranged`
- `shared_shambler_*` (2): `shared_shambler`, `shared_shambler_tentacle`
- `shared_spider_*` (2): `shared_spider spitter`, `shared_spider webber`
- `vermin_*` (7): `vermin_bagman`[D2], `vermin_caretaker`[D2], `vermin_ectoplasm_b`[D2], `vermin_ectoplasm`[D2], `vermin_ectoplasm_large_b`[D2], `vermin_ectoplasm_large`[D2], `vermin_ectoplasmic_effigy`[D2]
- `shared_warlord_*` (1): `shared_warlord`[D1]
- `peasant_militia_*` (6): `peasant_militia melee a`, `peasant_militia melee b`, `peasant_militia melee c`, `peasant_militia ranged a`, `peasant_militia ranged b`, `peasant_militia ranged c`
- `catacombs_*` (1): `catacombs_faction_export`[D2] (vermin misc: Quirks, effects for the Catacombs faction)

**Heroes (class, skills, unlocks)** - 17 files. One file per hero class: class + corpse, ~20-56 stat blocks, 12-71 skills, effects, level `Unlock`s. `hero_rules` is the global hero Rules element.

`hero_flg`, `hero_gr`, `hero_hel`, `hero_hwm`, `hero_jes`, `hero_lep`, `hero_maa`, `hero_occ`, `hero_pd`, `hero_rules`, `hero_run`, `hero_ves`, `hero_abm`[D2], `hero_cru`[D1], `hero_dul`[D1], `hero_bh`[exp], `hero_bh`[kg]

**Hero stories (origin-story encounters)** - 128 files. Hero origin-story ("herostory") encounters: scripted fight actors/props per hero (`_1`/`_2` combats + props). 128 files, 1 per prop/actor.

- `herostory_flg_*` (2)
- `herostory_gr_*` (12)
- `herostory_hel_*` (6)
- `herostory_hwy_*` (10)
- `herostory_jes_*` (4)
- `herostory_leper_*` (5)
- `herostory_maa_*` (6)
- `herostory_note_*` (10)
- `herostory_occ_*` (5)
- `herostory_pd_*` (10)
- `herostory_run_*` (9)
- `herostory_ves_*` (12)
- `herostory_abm_*` (10) [D2]
- `herostory_cru_*` (15) [D1]
- `herostory_dul_*` (12) [D1]

**Bosses (Mountain confession bosses, boss_failure, god)** - 37 files. `boss_data_export` = the `Boss`/`ExtendedBoss` rows (5 confessions + prologue). The rest are the fight actors: brain locks, lungs front/core/back, eyes + stalks, arms phases, stomach/torso/god/cherub, spacers, and `boss_failure <hero>` (one per hero class). See section 4.

`boss_arms_phase1`, `boss_arms_phase2`, `boss_arms_phase3`, `boss_blessing`, `boss_brain lock health`, `boss_brain lock melee`, `boss_brain lock ranged`, `boss_brain lock stress`, `boss`, `boss_eyes`, `boss_eyes_stalk_l`, `boss_eyes_stalk_m`, `boss_eyes_stalk_s_corpse`, `boss_eyes_stalk_s`, `boss_failure flg`, `boss_failure gr`, `boss_failure hel`, `boss_failure hwm`, `boss_failure jes`, `boss_failure lep`, `boss_failure maa`, `boss_failure occ`, `boss_failure pd`, `boss_failure run`, `boss_failure ves`, `boss_god cherub spacer`, `boss_god cherub`, `boss_god`, `boss_lungs back`, `boss_lungs core`, `boss_lungs front`, `boss_spacer`, `boss_stomach`, `boss_torso`, `boss_failure_abm`[D2], `boss_failure cru`[D1], `boss_failure dul`[D1]

**Battle composition and combat rules** - 21 files. Battle tables/configs, battle/arena modifiers, summon controllers, combat rules, skill blocks/modifiers, torch levels (light levels), overstress, stress triggers, generic `move`/`pass`.

`arena_modifier`, `battle_configuration`, `battle_configuration_table`, `battle_modifier`, `combat_rules`, `kingdom_battle_configuration`, `kingdom_battle_modifier`, `move`, `overstress`, `pass`, `skill_block`, `skill_modifier`, `stress_triggers`, `summon_controller_configuration`, `torch_levels`, `torch_levels_group`, `torch_triggers`, `battle_configuration`[D2], `battle_configuration_table`[D2], `battle_configuration`[D1], `battle_configuration_table`[D1]

**Map, routes, regions, run flow** - 32 files. Biomes, goals, modifiers, kill contracts, statuses, routes, road events, node effects, run levels/goals/values/rules, doom, driving/story/score rules, roster, Kingdoms map.

`actor_status`, `biome`, `biome_data_export_kingdoms`, `biome_goal`, `biome_kill_contract`, `biome_modifier`, `biome_status`, `doom_levels`, `driving_rules`, `kingdom_map`, `node_effects`, `roadevent`, `roster`, `route`, `run_goal_category`, `run_goal`, `run_levels`, `run_rules`, `run_value_level`, `run_value_transaction`, `score_rules`, `story_rules`, `biome`[D2], `roadevent`[D2], `route`[D2], `run_goal`[D2], `node_effects`[D1], `node_replacement`[D1], `run_goal`[D1], `actor_modes_data_export_DLC2`[D2] (Abomination human/beast ActorDataMode), `quest_data_export`[D1] (Crusader Quest/QuestStep), `story_replacement_data_export`[D1] (story-choice draw-tag replacement)

**Items, loot, economy** - 40 files. Items (generic, trinkets, stagecoach upgrades, memories), loot tables (8 files), tokens, inventory rules, costs, infernal-flame construction (torch upgrade).

`cost`, `infernal_flame_construction`, `inventory_rules`, `item`, `item_subtype`, `items_combat skill`, `loot_data_export_COMBATS`, `loot_data_export_CURRENCIES`, `loot_data_export_INN_REWARDS`, `loot_data_export_MISC`, `loot_data_export_NODESandROAD`, `loot_data_export_STAGECOACH`, `loot_data_export_SUPPLIES`, `loot_data_export_TRINKETS`, `memory`, `memory_loot_table`, `stage_coach_rules`, `stagecoach_skin`, `stagecoach_upgrades`, `stagecoach_upgrades_hardcoded`, `temp_rapid_buff`, `temp_rapid_condition`, `temp_rapid_effect`, `token ignore`, `token`, `trinket_set`, `trinkets_actor_effect_trigger`, `trinkets`, `item`[D2], `loot`[D2], `stagecoach_upgrades`[D2], `token`[D2], `trinkets`[D2], `item`[D1], `item_subtype`[D1], `loot`[D1], `stagecoach_upgrades`[D1], `token`[D1], `trinkets_actor_effect_trigger`[D1], `trinkets`[D1]

**Effects, buffs, conditions, skills engine** - 21 files. Generic engine tables: Effect (4588 elements), Buff, Condition, Dot, Resist, ActorEffectTrigger (`ae trigger`). DLC copies are `dlcN_*`.

`ae trigger`, `buff`, `condition`, `dots`, `effect`, `resist`, `dlc2_ae trigger`[D2], `dlc2_buff`[D2], `dlc2_condition`[D2], `dlc2_dots`[D2], `dlc2_effect`[D2], `dlc2_items_combat skill`[D2], `dlc2_miscellaneous`[D2], `dlc1_ae trigger`[D1], `dlc1_buff`[D1], `dlc1_condition`[D1], `dlc1_effect`[D1], `dlc1_items_combat skill`[D1], `dlc1_miscellaneous`[D1], `dlc1_token ignore`[D1], `dlc3_miscellaneous`[D3]

**Quirks, affinity, relationships** - 7 files. Quirk, quirk act-outs, affinity (relationship stress/aff), relationship definitions.

`act_out`, `affinity`, `affinity_leaning_level`, `affinity_rules`, `quirk_act_out`, `quirk`, `relationship_definition`

**Story choices (curios, assists, resists)** - 11 files. StoryChoice elements for road encounters (assist, resist, creature den, oasis, gaunt chirurgeon, hero stories, curios) + alignments.

`story_choice_export_alignments`, `story_choice_export_assist`, `story_choice_export_creature_den`, `story_choice_export_gauntchirurgeon`, `story_choice_export_herostory`, `story_choice_export_oasis`, `story_choice_export_resist`, `story_choice_export_valley`, `story_choice`[D2], `story_choice`[D1], `curio_choice_export`

**Narration, barks, subtitles, haptics** - 31 files. Narrator lines (`narration_*`), barks triggers, cinematic subtitles, controller haptics. Not needed for gameplay logic.

`bark_triggers`, `cinematic_subtitles`, `haptics_data_export_device_intensity`, `haptics_data_export_disabled_audio`, `haptics_data_export_duration`, `haptics_data_export_intensity`, `haptics_data_export_mappings`, `narration_entry_data_export_affinity`, `narration_entry_data_export_altar_of_hope`, `narration_entry_data_export_combat`, `narration_entry_data_export_driving`, `narration_entry_data_export_hero_select`, `narration_entry_data_export_hero_story`, `narration_entry_data_export_inn`, `narration_entry_data_export_kingdom`, `narration_entry_data_export_loot`, `narration_entry_data_export_run`, `narration_entry_data_export_store`, `narration_type_data_export_affinity`, `narration_type_data_export_altar_of_hope`, `narration_type_data_export_combat`, `narration_type_data_export_driving`, `narration_type_data_export_hero_select`, `narration_type_data_export_hero_story`, `narration_type_data_export_inn`, `narration_type_data_export_kingdom`, `narration_type_data_export_loot`, `narration_type_data_export_run`, `narration_type_data_export_store`, `narration_entry`[D2], `narration_entry`[D1]

**Progression, unlocks, altar, inn** - 13 files. Altar of Hope rules, progression/unlock tracks and tables, Expedition-mode inn data.

`altar_of_hope_rules`, `inn_bonus`, `inn`, `inn_data_export_hard_coded`, `inn_tables`, `progression`, `unlock`, `unlock_tables`, `progression`[D2], `unlock_tables`[D2], `inn_bonus`[D1], `progression`[D1], `unlock_tables`[D1]

**Kingdoms mode** - 53 files. Kingdoms (`kingdom_*`) + `TEMP_*`/`temp_*` scratch tables + `kingdom/` override folder + test/editor stubs.

`TEMP_beastmen`, `editor`, `kingdom_biome_modifier`, `kingdom_biome_upgrade`, `kingdom_cost`, `kingdom_difficulty`, `kingdom_escalation_levels`, `kingdom_event`, `kingdom_event_hardcoded`, `kingdom_gang`, `kingdom_hero_skill_sets`, `kingdom_hero_stat_upgrade`, `kingdom_inn`, `kingdom_inn_data_export_hard_coded`, `kingdom_inn_upgrade`, `kingdom_misc`, `kingdom_quest_beastmen`, `kingdom_quest_courtier`, `kingdom_quest_courtier_data_items`, `kingdom_quest_courtier_data_mel_misc`, `kingdom_quest_courtier_data_quest`, `kingdom_quest_courtier_data_quirks`, `kingdom_quest_courtier_data_route`, `kingdom_quest_courtier_data_storychoice`, `kingdom_quest_coven`, `kingdom_quest_coven_stress_triggers`, `kingdom_quirk_temp`, `kingdom_rules`, `kingdom_run_value_transaction`, `kingdom_siege_attack`, `kingdom_siege_defense`, `kingdom_temp`, `kingdom_torch_levels`, `kingdom_torch_levels_group`, `kingdom_treasure`, `kingdom_wound_triggers`, `temp_boss_body`, `temp_courtier_misc`, `temp_coven_misc`, `test`, `kingdom_hero_skill_sets_data`[D2], `kingdom_hero_stat_upgrade`[D2], `temp_abom_misc`[D2], `TEMP_CRU_hero`[D1], `TEMP_CRU_quest`[D1], `TEMP_dlc_item`[D1], `kingdom_hero_skill_sets_data`[D1], `kingdom_hero_stat_upgrade`[D1], `kingdom_affinity_leaning_level`[kg], `kingdom_affinity_rules`[kg], `kingdom_hero_skill_sets_data`[kg], `kingdom_hero_stat_upgrade_data_export_kingdom_override`[kg], `kingdom_override`[kg]

**Meta** - 7 files. Achievements, actor paths (class paths/skill replacements), tutorial rules, stagecoach skin/rules.

`achievement`, `actor_paths`, `tutorial_rules`, `achievement`[D2], `actor_paths`[D2], `achievement`[D1], `actor_paths`[D1]


## 2. Regions (biomes)

Source: `biome_data_export.Group.csv` (12 `Biome` elements), `biome_data_export_kingdoms.Group.csv` (2: `ValleyKingdom`, `Tundra`), `dlc_catacombs/biome_data_export_DLC2.Group.csv` (1: `Catacombs`); `BiomeType` list and sub-types from decomp (`BiomeType.cs`); display names `LOC/Sources/general.txt` (`biome_name_*`, `biome_enemy_faction_*`) and `dlc_catacombs/misc_dlc2.txt`. 15 `Biome` elements = 15 `BiomeType` ids.

### 2.1 The 15 region ids

| `Biome` id | sub-type (decomp) | display name | where defined | `m_BiomeGroupTag` | `m_StoryDefaultBattleConfigurationTable` | notes (data) |
|---|---|---|---|---|---|---|
| `ValleyIntro` | START | The Valley | base | valley | - | prologue region; `m_InnTable,valley_intro`; route `route_valley_intro_combat`; doom disabled |
| `Valley` | START | The Valley | base | valley | - | start region of each act; `m_InnTable,valley`; route `route_valley_combat`; doom disabled; scout chances 1/1 |
| `ValleyKingdom` | START | The Valley | `biome_data_export_kingdoms` | valley | - | Kingdoms start; route `route_valley_kingdom_combat_gang`; doom disabled |
| `City` | TYPICAL | The Sprawl | base | city | `faction_mashes_resist_master` | faction Fanatics; lair Librarian |
| `Farm` | TYPICAL | The Foetor | base | farm | `faction_mashes_resist_master` | faction Plague Eaters; lair Harvest Child |
| `Forest` | TYPICAL | The Tangle | base | forest | `faction_mashes_resist_master` | faction Lost Battalion; lair Dreaming General; also `route_forest_combat_gang` (Kingdoms) |
| `Coast` | TYPICAL | The Shroud | base | coast | `faction_mashes_resist_master` | faction Coastal; lair Leviathan; `ArenaModifier` `coast_fog`, `coast_boss_fog`; also `route_coast_combat_gang` |
| `Tundra` | TYPICAL | The Tundra | `biome_data_export_kingdoms` (Kingdoms only) | tundra | `faction_mashes_resist_master` | faction Cultists (`cultist_tundra_mash_*`); lair Exemplar; `route_tundra_combat_gang` |
| `Cave` | OPTIONAL | The Sluice | base | cave | `underground_mashes_resist_master` | faction Swine; **no lair**; only `faction` + `gaunt` combat routes (no pillager/military) |
| `Catacombs` | OPTIONAL | The Catacombs | `dlc_catacombs` | catacombs | `underground_mashes_resist_master` | faction Vermin; **no lair**; `route_catacombs_combat_gaunt` has `m_Chance,0` (gaunts effectively off) |
| `MountainBrain` | END | The Mountain | base | mountain | - | Act 1 boss region (Denial); doom disabled; only `route_safe` |
| `MountainLungs` | END | The Mountain | base | mountain | - | Act 2 (Resentment); `m_RequiredStageCoachItemSlotType,Trophy` |
| `MountainEyes` | END | The Mountain | base | mountain | - | Act 3 (Obsession); requires Trophy slot |
| `MountainArms` | END | The Mountain | base | mountain | - | Act 4 (Ambition); requires Trophy slot |
| `MountainBody` | END | The Mountain | base | mountain | - | Act 5 (Cowardice, final); requires Trophy slot |

Per-region element lists (all `Biome` elements carry exactly these keys): `m_InnTable`, `m_BiomeGroupTag`, `road_events`, `routes`, optional `m_DisabledRunValueTypes,doom`, optional `m_RequiredStageCoachItemSlotType,Trophy`, optional `m_StoryDefaultBattleConfigurationTable`. A `RunDataStats` element with the same id adds scouting stats (Valley/ValleyIntro/ValleyKingdom: `scout_node_chance,1`, `scout_route_chance,1`).

### 2.2 Which enemy factions a region fields (from `routes`, `Route` elements, `route_data_export` + `_dlc2`)

`Route` has `m_RouteType` (`safe`, `combat`, `hazard`, `rough_patch`, `oblivion_tear`), `m_Chance`, optional `actorless_effects`, `m_EndingRowsToSkip`, `m_AllowsBark`, `m_OverrideNarrationTags`. 46 routes. Combat routes per region (chance in brackets):

| region | `_combat_faction` | `_combat_gaunt` | `_combat_pillager` | `_combat_military` | `_combat_gang` (Kingdoms) | hazard / rough patch / oblivion |
|---|---|---|---|---|---|---|
| City | 2 | 3 | 3 | 1 | - | hazard (`remove_1_stagecoach_armor`), rough_patch (`remove_1_stagecoach_wheel`), `route_oblivion_tear` (`doom_damage_1`), all chance 5 |
| Farm | 2 | 3 | 3 | (present) | - | same |
| Forest | present | present | present | present | `route_forest_combat_gang` | same |
| Coast | present | present | present | present | `route_coast_combat_gang` | same |
| Tundra | present | present | present | present | `route_tundra_combat_gang` | same |
| Cave | present | present | - | - | - | same (`route_caves_*`) |
| Catacombs | 4 | 0 | - | - | - | same (`route_catacombs_*`) |
| Valley | `route_valley_combat` (gaunts) | | | | `route_valley_kingdom_combat_gang` | none |
| Mountain* | none (`route_safe` only) | | | | | none |

Which battle table a combat route draws from is **not** in the CSVs (the route/node prefab carries `m_battleConfigurationTableId` on `TriggerCombatBhv` in Unity data; table roots are named `faction_mashes_road_master`, `gaunt_mashes_road_master`, `pillager_mashes_road_master`, `military_mashes_road_master`, `underground_mashes_road_master`, `beast_mashes_creature_den_master`, `dungeon_mashes_starter_master`, `camp_mashes_master` - mapping route->root not verified). The faction chosen inside `*_master` tables is selected by biome conditions: `farm_biome_check`, `city_biome_check`, `forest_biome_check`, `shroud_biome_check` (Coast), `tundra_biome_check`, `cave_biome_check`, `catacombs_biome_check`.

Faction (display) per region as stored in `LOC` (`biome_enemy_faction_*`): City=The Fanatics, Forest=The Lost Battalion, Farm=The Plague Eaters, Cave=The Swine, Valley=The Gaunts, Mountain=The Cultists. Coast, Tundra and Catacombs have no such key (coast faction = `coastal_*`, tundra = `cultist_*`, catacombs = `vermin_*`; names "Coastal"/"Vermin" are file prefixes, not verified as UI names).

### 2.3 Lair bosses (the 3-round `*_dungeon_*` fights; details in section 5)

| region | lair boss actor ids (as in `m_EnemyActors`) | display | trophy item (`stage_coach_upgrade`, sub_type `trophy`) | boss trinket loot table |
|---|---|---|---|---|
| City | `fanatic_librarian` (+ `fanatic_librarian_stack_s/m/l`; `fanatic_librarian_ignited` is the burning form) | Librarian | `trophy_fanatic_librarian`, `_b`, `_c` | `TRINKETS_CITY_BOSS` |
| Farm | `plague_eater_harvest_table` + `plague_eater_harvest_fetid_meat` + `plague_eater_harvest_putrid_meat` | Harvest Child | `trophy_plague_eater_harvest_table`, `_b`, `_c` | `TRINKETS_FARM_BOSS` |
| Forest | `lost_battalion_boss_dreaming_general` + `lost_battalion_boss_taproot` (death-chained) | Dreaming General / Tap Root | `trophy_lost_battalion_dreaming_general`, `_b`, `_c` | `TRINKETS_FOREST_BOSS` |
| Coast | `coastal_boss_leviathan` (+ summoned `coastal_boss_leviathan_hand`) | Leviathan | `trophy_coastal_boss_leviathan`, `_b`, `_c` | `TRINKETS_COAST_BOSS` |
| Tundra (Kingdoms) | `cultist_exemplar` + `cultist_altar` (Kingdoms `cultist_exemplar` is tagged `boss_travelogue,biome_boss`, HP 145) | Exemplar | `trophy_cultist_boss_exemplar`, `_b`, `_c` | `TRINKETS_TUNDRA_BOSS` |
| Cave / Catacombs | none | - | - | - |

The tag `biome_boss` (+ `boss_travelogue`) marks exactly these: `fanatic_librarian`, `fanatic_librarian_ignited`, `plague_eater_harvest_table`, `lost_battalion_boss_dreaming_general`, `coastal_boss_leviathan`, and Kingdoms' `cultist_exemplar`. Lair reward table `lair_boss_rewards_all` -> `confessions_lair_boss_rewards` (Expedition) / `kingdoms_lair_boss_rewards` (Kingdoms).

### 2.4 Run structure that selects regions (`Boss` + `RunDataStats` rows in `boss_data_export`)

Each act's `RunDataStats` (same id as the `Boss`) says how many regions the map generator chains: `run_generation_number_of_typical_biomes`, `..._optional_biomes`, `run_generation_optional_biome_chance`, doom limits, `hire_*`:

| boss row (act) | typical regions | optional regions (chance) | allowed typical regions (`run_generation_typical_biome_chance`) | doom max | `hire_typical_biomes_min-max`, `hire_chance` (meaning not verified; presumably recruit-node generation) |
|---|---|---|---|---|---|
| `prologue` | 0 | 0 | - (Valley intro) | 4 | none |
| `brain` (Act 1, Denial) | 2 | 0 | City, Forest | 4 | 0-1 regions, 45% |
| `lungs` (Act 2, Resentment) | 3 | 1 (50%) | City, Forest, Farm, Coast | 4 | 0-2, 40% |
| `eyes` (Act 3, Obsession) | 3 | 1 (50%) | City, Forest, Farm, Coast | 4 | 0-2, 37.5% |
| `arms` (Act 4, Ambition) | 3 | 1 (50%) | City, Forest, Farm, Coast | 4 | 0-2, 35% |
| `body` (Act 5, Cowardice) | 3 | 1 (50%) | City, Forest, Farm, Coast | 4 | 0-2, 33% |

Optional regions are `Cave` and (D2) `Catacombs`. The Kingdoms map is a different mode (section 8): fixed `KingdomMap` grids with `biome+Farm`, `biome+City`, `biome+Tundra`, `biome+Forest`, `biome+Coast` cells, `biome+Boss`, `inn+kingdom_inn`, `camp`.

### 2.5 Region-specific mechanics and modifier tables

- **Blessings** (`BossModifier`, 49 rows in `boss_data_export`): enemies tagged `<act>_blessing` (almost every regular monster has `brain_blessing,lungs_blessing,eyes_blessing,arms_blessing,final_blessing`) receive buffs scaled by which typical-region index (1/2/3) of the act the party is in (`m_NumberOfTypicalBiomesMin/Max`), chance 0 / 0.25 / 0.33 in the base rows; region 3 additionally spawns the Exemplar (`boss_blessing_exemplar`, rows `*_boss_exemplar_biome_3`, chance 1). `*_infernal_flame_lair_boss_biome_N` rows target actors tagged `biome_boss,boss_hand,meat`.
- **Biome modifiers** (`BiomeModifier`, `biome_modifier_data_export`, 36 Expedition rows, all `m_ValidBiomeTypes` in {City,Farm,Forest,Coast} except `deluge`/`outpost`/`big_game` (no City) and `drought` (Farm,Forest)): `true_evil, untouched, thick_mud, pestilent, corrupting, blood_moon, heat_wave, grave_calling, praise_the_sun, malaise, trench_run (chance 0), maze, junkyard, healthcare, no_time_to_bleed, bat_country, light_the_way, curious, the_gauntlet, booty, candles_and_war, candles_and_peace, strategic_plan, deluge, haze, recession, drought, heavy_winds, devious_plans, civilization, outpost, big_game, foreboding, treacherous, loathsome, marked_roads`. Display names `LOC biome_mutator_<id>`.
- **Kingdoms biome modifiers** (`kingdom_biome_modifier_data_export`): 18 `bm_*_<region>` rows for each of City, Coast, Farm, Forest, Tundra (`bm_extra_currency_`, `bm_longer_region_`, `bm_rare_combat_locations_`, `bm_rare_noncombat_locations_`, `bm_increased_/reduced_road_combat_`, `bm_reduced_road_traps_`, `bm_increased_/reduced_resist_`, `bm_increased_/reduced_assist_`, `bm_enemy_spd_buff_`, `bm_enemy_token_buff_`, `bm_enemy_token_debuff_`, `bm_beastmen_road_combat_`, `bm_guaranteed_cache_`, `bm_increased_studies_`, `bm_unique_`) = 90 rows, + `bm_crimson_contagion` (courtier quest) = 127 `BiomeModifier` elements in total.
- **Biome goals** (`BiomeGoal`, 16 rows): `naked_stage_coach, keep_torch_high, visit_story_assist_3, visit_story_cosmic_3, win_story_resist_3, story_resist_not_2, visit_cultist_2, win_dungeon, win_roadfights_6, avoid_watchtower, avoid_hospital, avoid_hoarder, road_fights_low, road_fights_not_4, visit_creature_den, visit_oasis`; reward tables `biome_goal_reward_default(_hard)` (trinkets / stagecoach / HERO_POINT / candles).
- **Biome kill contracts** (`BiomeKillContract`, 8 rows, Kingdoms escalation): `dungeon, creature_den, gaunt_chirurgeon, city_resist, farm_resist, forest_resist, coast_resist, tundra_resist` (each names a `m_CombatSource`, loot ids, 3-7 day duration).
- **Biome statuses** (`BiomeStatus`): `killed_chirurgeon` (Gaunt Chirurgeon killed -> 3 inns: fewer `GauntChirurgeon` nodes; gaunt road table switches to `gaunt_mashes_road_confessions_patients`), `killed_warlord` [D1] (military table switches to `military_other_faction_mashes`).
- **Biome upgrades** (`BiomeUpgrade`, 7, Kingdoms inn "ultimate" upgrades): `defense_ult_hero/monster`, `provisioner_ult_hero`, `wainwright_ult_hero/monster`, `trainer_ult_hero`, `physician_ult_hero`.
- **Doom / torch**: `m_DisabledRunValueTypes,doom` on Valley*/Mountain*. `doom_levels` (4 `DoomLevel`: 0..3, adding `doom_badstuff_1..3` run stats: more torch drain, more battle modifiers). `TorchLevelGroup` ids per act (`brain, lungs, eyes, arms, body`) and per infernal-torch tier (`infernal_torch`, `_2.._5`, `_combo`, `_killing_blow`): light bands 76-100 / 51-75 / 26-50 / 1-25 / 0 (read for the hero set `torch_<x>_heroes_1..5`; a parallel `torch_<x>_monsters_1..5` set exists; `torch_levels_data_export`, 133 `TorchLevel`). Each band attaches buffs/effects (e.g. blind at <=25, extra death-blow resist at high light) - the DD1 torch analogue.
- **Road events** (`RoadEvent`, 40): per region `roadevent_<region>_pickup_NN` (Valley, ValleyIntro, ValleyKingdom, City: `burning_books`, `corpse`, `iron_spikes`, Farm: `road_meat`, `road_meat_and_boxes`, Forest 3, Cave 3 (`caves_`), Coast 2, Tundra 2, Mountain x5 x2, Catacombs 3), shared `banter_event_shared`, `ambush_event_shared`, Kingdoms `ambush_event_kingdom_<caves|city|coast|farm|forest|tundra>`, `roadevent_courtier_pickup_01`.
- **Backdrops**: every typical region has its own combat-arena scenes (section 11).

## 3. Monsters (enemy actors)

How this was built: every `ActorDataClass` element whose id does not end in `_corpse`, does not start with `herostory`, and that is not tagged `hero` -> **163 unique actor ids** (the 433 `ActorDataClass` elements = 167 enemy elements [163 ids; `cultist_altar/cardinal/deacon/exemplar` exist twice, in `Excel/expedition/` and `Excel/kingdom/`] + 122 `*_corpse` elements + 128 `herostory_*` elements [origin-story fight actors/props, 128 ids] + 16 hero elements [15 ids; `bounty_hunter` is defined twice]). HP = `health_max` from `ActorDataStats` (base value, before blessings/modifiers). Size = `m_Size` (1 = one slot, 2 = "large" two slots, 3 = three slots (Meat Hook, Matres Sequelae, Exemplar, Dreaming General), 4 = four slots (arm/stomach/torso, Ancient Adversary)). Display names are the English `LOC` value for the actor id.

**Tier / variant conventions found in the data**

| convention | meaning | examples |
|---|---|---|
| plain id | base monster; tag set contains `monster` + faction tag | `fanatic_whipper`, `shared_pillager_melee` |
| `_b` suffix (tag `champion`) | champion/elite variant used in `*_C`/`_BC` tables (higher HP, unique display name, e.g. `fanatic_immolatist_b` = "Her Ladyship", `plague_eater_butcher_b` = "Tohno the Carver", `fanatic_whipper_b` = "Masterful Kinred") | `coastal_captain_b`, `lost_battalion_knight_b`, `cave_swine_brute_b` (tag `wilbur`), `shared_dog_rabid_b` |
| `_elite` | only `beastmen_rot_claw_elite` (HP 68 vs 36) | |
| `_ignited` | burning form of City fanatics, reached by a class-change effect (same faction, HP about +20-33%) | `fanatic_whipper_ignited`, `fanatic_librarian_ignited` |
| `a/b/c` | peasant militia tiers (Kingdoms allies) | `peasant_militia_melee_a/b/c` |
| tag `weak` | weakest mobs (low HP, used as fodder/summons) | `coastal_cabin_boy`, `fanatic_flayer`, `courtier_swarm` |
| tag `boss` / `biome_boss` / `end_boss` / `gang_boss` | boss families, see section 4 | |
| `_corpse` twin | every non-prop actor has a `<id>_corpse` `ActorDataClass` (not listed) | |
| `m_Size` 2+ with tag `large` | big monsters | `shared_ghoul`, `coastal_docker`, `fanatic_pit_fighter` |

**Counts per faction (unique ids)**

| group | ids | | group | ids |
|---|---|---|---|---|
| fanatic (City) | 16 | | beastmen (Kingdoms) | 7 |
| plague_eater (Farm) | 11 | | coven (Kingdoms) | 10 |
| lost_battalion (Forest) | 9 | | courtier (Kingdoms) | 7 |
| coastal (Coast) | 10 | | peasant_militia (Kingdoms allies) | 6 |
| cave_swine (Cave) | 5 | | shared pillager | 6 |
| vermin [D2] (Catacombs) | 7 | | shared gaunt / lost soul (+ ghoul) | 7 |
| cultist (Mountain / Tundra / ambush) | 7 | | shared beasts (dog, carrion eater, spider) | 6 |
| Mountain boss parts (`boss_*`) | 34 | | military barricade + warlord [D1] | 5 |
| collector (+ heads) | 5 | | death (+ headstone) | 2 |
| shambler (+ tentacle) | 2 | | ancestor statue | 1 |

Sum = 163. Heroes (15 class ids) are in section 6.

**Roaming mini-bosses** (random encounters, the data tag is `wandering` on the table option) - use these ids:

| mini-boss | actor id(s) | HP / size | how it is generated (tables) |
|---|---|---|---|
| The Collector | `shared_collector` (+ `shared_collector_head_hwy`, `_head_maa`, `_head_ves`; `shared_head_hwy`) | 90 / 1 | `collector_mash_regions_confessions` (`collector_region_1/2/3`, `m_RunLimit,1`, chosen by `biome_typical_equals_1/2/>=3`), Kingdoms `collector_mash_escalation_kingdoms`, caves `collector_region_caves`; slotted into `faction_mashes_road_confessions`, `gaunt_mashes_road_confessions_*`, `pillager_mashes_road_confessions` with tag `wandering` and condition `heroes_have_trophy_equipped` (a Trophy stagecoach item equipped), doubled by `stagecoach_has_collectors_chandelier_equipped` |
| The Shambler | `shared_shambler` (+ `shared_shambler_tentacle`) | 85 / 2 | `shambler_mash` -> configs `shambler`, `shambler_hazard`, `shambler_rough_patch`; condition `shambler_torch_threshold` (run value `torch` <= 30) and `biome_typical_equal_or_greater_than_2`; also a Valley option |
| Death | `shared_death` (+ 2x `shared_death_headstone`) | 80 / 2 | `death_mashes` table appended to road fights through `m_AdditionalBattleConfigurationTableId,death_mashes` (weights 6/3/6/6/6/6/3 vs `nothing` 94); conditions `hero_party_has_flagellant` (or `option_allow_death_no_flagellant`), Kingdoms `death_e1..e3` by escalation; `m_RunLimit,1`; loot `death_rewards_all` |
| Exemplar (ambush form) | `cultist_exemplar` + `cultist_evangelist`/`altar`/`herald`/`cherub` | 101 / 3 | `cultist_doom_mash_1..4` in `cultist_mashes_ambush_hard_confessions`, only when `run_boss_is_body` (final act); loot `ambush_cultist_rewards_all` |
| Warlord [D1] | `shared_warlord` (+ `shared_barricade_spearman/swordsman/cover1/cover2`) | 77 / 2 | `military_region_1/2/3` [D1] -> `warlord_easy` / `warlord_hard` (tag `wandering`), configs `warlord_front_1-4`, `warlord_back_1-6`, `warlord_quest`; conditions `warlord_below_1`, `crusader_quest_complete`, `herostory_CRU_05`; node replacement `guardian_to_warlord` |
| Gaunt Chirurgeon | `shared_lost_soul_chirurgeon` (tag `boss`) | 50 / 1 | own node type `GauntChirurgeon`; configs `surgeon_gaunt_101a..204b` (19) + `patient_mash_*` (11); on death adds biome status `killed_chirurgeon` |
| Antiquarian | `shared_pillager_antiquarian` (tag `boss`) | 25 / 1 | `pillager_mash_antiq_1..6` (+ `_hazard_`, `_rough_patch_` antiq variants) |
| Ancient Adversary | `shared_ancestor_statue` (tag `boss`) | 225 / 4 | `ancestor_statue_<city, coast, farm, forest, tundra, cave, catacombs>`, Creature-Den-type node, `m_RoundLimit,6` |
| Kingdoms alpha | `beastmen_meat_hook` + `beastmen_meat_post` (boss fight), `beastmen_alpha_wave` | 160 / 3 | Kingdoms quest `beastmen` (`quest_beastmen_kill_alpha`), node replacement `node_insert_beastmen_alpha` |

**Cultist copies** (`Excel/expedition/` vs `Excel/kingdom/`): `cultist_exemplar` is HP 101, size 3, tags `boss,cultist_boss,boss_blessing_exemplar` in Expedition, but HP 145 with `boss_travelogue,biome_boss` (Tundra lair) in Kingdoms; `cultist_deacon`/`cultist_cardinal` carry the `boss` tag in Expedition only.

### 3.1 Full actor list by faction

#### City (`City`, "The Sprawl") - The Fanatics - 16 actor ids
Lair boss: `fanatic_librarian` + `fanatic_librarian_stack_{s,m,l}`.

| id | display name | size | HP | role / variant | data scope |
|---|---|---|---|---|---|
| `fanatic_blind_shaman` | Shaman | 1 | 12 | weak | base |
| `fanatic_blind_shaman_ignited` | Shaman | 1 | 15 |  | base |
| `fanatic_flayer` | Flayer | 1 | 12 | weak | base |
| `fanatic_flayer_ignited` | Flayer | 1 | 16 |  | base |
| `fanatic_immolatist_b` | Her Ladyship | 1 | 23 | champion (_b) | base |
| `fanatic_immolatist` | Immolatist | 1 | 16 |  | base |
| `fanatic_librarian` | Librarian | 1 | 116 | LAIR BOSS | base |
| `fanatic_librarian_ignited` | Librarian | 1 | 140 | LAIR BOSS | base |
| `fanatic_librarian_stack_l` | Books | 1 | 15 | prop/obstacle | base |
| `fanatic_librarian_stack_m` | Damaged Books | 1 | 15 | prop/obstacle | base |
| `fanatic_librarian_stack_s` | Scorched Books | 1 | 15 | prop/obstacle | base |
| `fanatic_pit_fighter` | Pit Fighter | 2 | 41 |  | base |
| `fanatic_sacrificial` | Sacrificial | 1 | 13 |  | base |
| `fanatic_whipper_b` | Masterful Kinred | 1 | 27 | champion (_b) | base |
| `fanatic_whipper` | Whipper | 1 | 16 | summoned by an effect | base |
| `fanatic_whipper_ignited` | Whipper | 1 | 20 |  | base |

#### Farm (`Farm`, "The Foetor") - The Plague Eaters - 11 actor ids
Lair boss: `plague_eater_harvest_table` + `..._harvest_fetid_meat` + `..._harvest_putrid_meat`.

| id | display name | size | HP | role / variant | data scope |
|---|---|---|---|---|---|
| `plague_eater_butcher_b` | Tohno the Carver | 1 | 34 | champion (_b) | base |
| `plague_eater_butcher` | Butcher | 1 | 23 |  | base |
| `plague_eater_dinner_cart` | Dinner Cart | 2 | 40 |  | base |
| `plague_eater_harvest_fetid_meat` | Fetid Meat | 1 | 47 | boss / mini-boss | base |
| `plague_eater_harvest_putrid_meat` | Putrid Meat | 1 | 51 | boss / mini-boss | base |
| `plague_eater_harvest_table` | Harvest Child | 2 | 120 | LAIR BOSS | base |
| `plague_eater_lady` | Lady | 1 | 20 |  | base |
| `plague_eater_livestock_b` | Black Phillip | 1 | 29 | champion (_b) | base |
| `plague_eater_livestock` | Livestock | 1 | 14 | weak | base |
| `plague_eater_lord` | Lord | 2 | 30 |  | base |
| `plague_eater_maid` | Maid | 1 | 18 |  | base |

#### Forest (`Forest`, "The Tangle") - The Lost Battalion - 9 actor ids
Lair boss: `lost_battalion_boss_dreaming_general` + `lost_battalion_boss_taproot`.

| id | display name | size | HP | role / variant | data scope |
|---|---|---|---|---|---|
| `lost_battalion_arbalist_b` | Bullseye Barrett | 1 | 20 | champion (_b) | base |
| `lost_battalion_arbalist` | Arbalist | 1 | 15 |  | base |
| `lost_battalion_bishop` | Bishop | 1 | 22 |  | base |
| `lost_battalion_boss_dreaming_general` | Dreaming General | 3 | 185 | LAIR BOSS | base |
| `lost_battalion_drummer` | Drummer | 1 | 19 |  | base |
| `lost_battalion_foot_soldier` | Foot Soldier | 1 | 17 |  | base |
| `lost_battalion_knight_b` | Fallen Templar | 2 | 41 | champion (_b) | base |
| `lost_battalion_knight` | Knight | 2 | 36 |  | base |
| `lost_battalion_boss_taproot` | Tap Root | 1 | 99 | boss / mini-boss | base |

#### Coast (`Coast`, "The Shroud") - Coastal / Dockside - 10 actor ids
Lair boss: `coastal_boss_leviathan` (+ summoned `coastal_boss_leviathan_hand`). No `biome_enemy_faction_Coast` loc key exists; "Coastal" is the file prefix (not verified as in-game faction name).

| id | display name | size | HP | role / variant | data scope |
|---|---|---|---|---|---|
| `coastal_boss_leviathan` | Leviathan | 2 | 155 | LAIR BOSS | base |
| `coastal_boss_leviathan_hand` | Leviathan's Hand | 2 | 24 | boss hand (summon); summoned by an effect | base |
| `coastal_bosun_b` | The Hull Keeper | 1 | 24 | champion (_b) | base |
| `coastal_bosun` | Bosun | 1 | 17 |  | base |
| `coastal_cabin_boy` | Cabin Boy | 1 | 13 | weak | base |
| `coastal_captain_b` | Admiral Marsh | 1 | 33 | champion (_b) | base |
| `coastal_captain` | Captain | 1 | 23 |  | base |
| `coastal_docker` | Docker | 2 | 46 |  | base |
| `coastal_fish_monger` | Fish Monger | 1 | 16 |  | base |
| `coastal_wharf_rat` | Wharf Rat | 1 | 14 |  | base |

#### Cave (`Cave`, "The Sluice") - The Swine - 5 actor ids
No lair (no `*_dungeon_*` configs); Creature Den + Ancestor statue + Collector (`collector_region_caves`).

| id | display name | size | HP | role / variant | data scope |
|---|---|---|---|---|---|
| `cave_swine_brute_b` | Wilbur | 2 | 56 | champion (_b) | base |
| `cave_swine_brute` | Swine Brute | 2 | 50 |  | base |
| `cave_swine_skiver_b` | Fulgore | 1 | 29 | champion (_b) | base |
| `cave_swine_skiver` | Swine Skiver | 1 | 22 |  | base |
| `cave_swine_skulker` | Swine Skulker | 1 | 18 |  | base |

#### Catacombs (`Catacombs`, "The Catacombs", DLC2 `dlc_catacombs`) - Vermin / Ectoplasm - 7 actor ids
No lair. Slime variants of creature-den fights.

| id | display name | size | HP | role / variant | data scope |
|---|---|---|---|---|---|
| `vermin_bagman` | Bagman | 1 | 26 |  | D2 |
| `vermin_caretaker` | Crypt Keeper | 2 | 48 |  | D2 |
| `vermin_ectoplasm_b` | Mucilage | 1 | 21 |  | D2 |
| `vermin_ectoplasm` | Ectoplasm | 1 | 17 |  | D2 |
| `vermin_ectoplasm_large_b` | Large Mucilage | 2 | 34 |  | D2 |
| `vermin_ectoplasm_large` | Large Ectoplasm | 2 | 30 |  | D2 |
| `vermin_ectoplasmic_effigy` | Ectoplasmic Effigy | 1 | 5 |  | D2 |

#### Mountain / Guardian nodes / Tundra (Kingdoms) / ambush - The Cultists - 7 actor ids
Two copies of altar/cardinal/deacon/exemplar exist: `Excel/expedition/` and `Excel/kingdom/` (different stats, see below). Tundra lair boss (Kingdoms only) = `cultist_exemplar` + `cultist_altar`.

| id | display name | size | HP | role / variant | data scope |
|---|---|---|---|---|---|
| `cultist_cherub` | Cherub | 1 | 14 | weak; summoned by an effect | base |
| `cultist_evangelist` | Evangelist | 1 | 28 | summoned by an effect | base |
| `cultist_herald` | Herald | 1 | 18 | summoned by an effect | base |
| `cultist_altar` | Altar | 1 | 20 | summoned by an effect | expedition+kingdom |
| `cultist_cardinal` | Cardinal | 2 | 46 | cultist boss | expedition+kingdom |
| `cultist_deacon` | Deacon | 2 | 41 | cultist boss | expedition+kingdom |
| `cultist_exemplar` | Exemplar | 3 | 101 | cultist boss | expedition+kingdom |

#### Kingdoms gang "beastmen" (boss region Tundra) - 7 actor ids
Gang boss fight: `beastmen_meat_hook` + `beastmen_meat_post`. Quest alpha fight `beastmen_alpha_wave`.

| id | display name | size | HP | role / variant | data scope |
|---|---|---|---|---|---|
| `beastmen_huntsman` | Huntsman | 1 | 23 |  | base |
| `beastmen_meat_hook` | Meat Hook | 3 | 160 | Kingdoms gang boss | base |
| `beastmen_meat_post` | Meat Post | 1 | 99 | boss / mini-boss; fight prop-boss | base |
| `beastmen_rot_claw` | Rot Claw | 2 | 36 |  | base |
| `beastmen_rot_claw_elite` | Alpha Rot Claw | 2 | 68 | elite | base |
| `beastmen_tribecaller` | Tribecaller | 1 | 27 |  | base |
| `beastmen_tribewalker` | Tribewalker | 1 | 24 |  | base |

#### Kingdoms gang "coven" (boss region Forest) - 10 actor ids
Gang boss fight: `coven_mothers_spitter` + `coven_matres_sequelae`.

| id | display name | size | HP | role / variant | data scope |
|---|---|---|---|---|---|
| `coven_cauldron` | Cauldron | 1 | 32 |  | base |
| `coven_custode` | Custode | 2 | 40 |  | base |
| `coven_dedicant` | Dedicant | 1 | 18 |  | base |
| `coven_hateful_virago` | Hateful Virago | 1 | 22 |  | base |
| `coven_matres_sequelae` | Mother of Threads | 3 | 160 | Kingdoms gang boss | base |
| `coven_matres_spider` |  | 1 | 75 | boss / mini-boss | base |
| `coven_mothers_mushroom` | Mother's Fungus | 1 | 18 | boss / mini-boss; summoned by an effect | base |
| `coven_mothers_spitter` | Mother's Spitter | 1 | 18 | boss / mini-boss; summoned by an effect | base |
| `coven_mothers_familiar` | Mother's Familiar | 1 | 24 | weak | base |
| `coven_mushroom` | Necrotic Fungus | 1 | 10 |  | base |

#### Kingdoms gang "courtier" (boss region Coast) - 7 actor ids
Gang boss fight: `courtier_archduke`.

| id | display name | size | HP | role / variant | data scope |
|---|---|---|---|---|---|
| `courtier_archduke` | Archduke | 2 | 174 | Kingdoms gang boss | base |
| `courtier_banquet` | Banquet | 2 | 34 | summoned by an effect | base |
| `courtier_chevalier` | Chevalier | 1 | 24 | summoned by an effect | base |
| `courtier_cocoon` | Cocoon | 1 | 15 | weak | base |
| `courtier_esquire` | Esquire | 1 | 21 | summoned by an effect | base |
| `courtier_swarm` | Swarm | 1 | 11 | weak; summoned by an effect | base |
| `courtier_sycophant` | Supplicant | 1 | 16 | weak; summoned by an effect | base |

#### Kingdoms inn-defence allies (`KingdomSiegeDefense`) - 6 actor ids
Player-side allies spawned at siege; a/b/c = peasant/militia/veteran tiers.

| id | display name | size | HP | role / variant | data scope |
|---|---|---|---|---|---|
| `peasant_militia_melee_a` | Conscript Fighter | 1 | 32 | summoned by an effect; ally (Kingdoms militia) | base |
| `peasant_militia_melee_b` | Partisan Fighter | 1 | 37 | summoned by an effect; ally (Kingdoms militia) | base |
| `peasant_militia_melee_c` | Veteran Fighter | 1 | 42 | summoned by an effect; ally (Kingdoms militia) | base |
| `peasant_militia_ranged_a` | Conscript Arbalister | 1 | 27 | summoned by an effect; ally (Kingdoms militia) | base |
| `peasant_militia_ranged_b` | Partisan Arbalister | 1 | 32 | summoned by an effect; ally (Kingdoms militia) | base |
| `peasant_militia_ranged_c` | Veteran Arbalister | 1 | 40 | summoned by an effect; ally (Kingdoms militia) | base |

#### Shared faction: Pillagers (`route_*_combat_pillager`, all typical biomes) - 6 actor ids

| id | display name | size | HP | role / variant | data scope |
|---|---|---|---|---|---|
| `shared_pillager_antiquarian` | Antiquarian | 1 | 25 | boss / mini-boss | base |
| `shared_pillager_artillery` | Implication | 2 | 38 |  | base |
| `shared_pillager_firemouth` | Firemouth | 1 | 15 |  | base |
| `shared_pillager_melee` | Pillager Hatchetman | 1 | 18 |  | base |
| `shared_pillager_mongrel` | Mongrel | 1 | 13 | weak | base |
| `shared_pillager_ranged` | Pillager Crackshot | 1 | 15 | weak | base |

#### Shared faction: Gaunts / Lost Souls (`route_*_combat_gaunt`, Valley, Gaunt Chirurgeon node) - 7 actor ids

| id | display name | size | HP | role / variant | data scope |
|---|---|---|---|---|---|
| `shared_lost_soul` | Lost Soul | 1 | 13 | weak | base |
| `shared_lost_soul_urchin` | Urchin | 1 | 11 | weak | base |
| `shared_lost_soul_widow` | Widow | 1 | 13 | weak | base |
| `shared_lost_soul_yeoman` | Woodsman | 2 | 42 |  | base |
| `shared_ghoul` | Ghoul | 2 | 36 |  | base |
| `shared_lost_soul_chirurgeon` | Chirurgeon | 1 | 50 | boss / mini-boss | base |
| `shared_lost_soul_patient` | Patient | 1 | 21 |  | base |

#### Shared: creature-den beasts (dogs, carrion eaters, spiders) - 6 actor ids
Used by `beast_mashes_*` (Creature Den node) and camp ambushes.

| id | display name | size | HP | role / variant | data scope |
|---|---|---|---|---|---|
| `shared_carrion_eater_mutated` | Carrion Devourer | 1 | 23 |  | base |
| `shared_carrion_eater` | Carrion Eater | 1 | 17 |  | base |
| `shared_dog_rabid_b` | Gander | 1 | 23 | champion (_b) | base |
| `shared_dog_rabid` | Rabid Gnasher | 1 | 14 |  | base |
| `shared_spider_spitter` | Spitter | 1 | 12 | weak | base |
| `shared_spider_webber` | Webber | 1 | 10 | weak | base |

#### Shared: Military barricade (`route_*_combat_military`) + Warlord (DLC1) - 5 actor ids

| id | display name | size | HP | role / variant | data scope |
|---|---|---|---|---|---|
| `shared_barricade_cover1` | Spiked Barricade | 1 | 20 | prop/obstacle | base |
| `shared_barricade_cover2` | Weapon Rack | 1 | 20 | prop/obstacle | base |
| `shared_barricade_spearman` | Spearman | 1 | 25 |  | base |
| `shared_barricade_swordsman` | Swordsman | 1 | 25 |  | base |
| `shared_warlord` | Warlord | 2 | 77 | boss / mini-boss | D1 |

#### Roaming mini-boss: The Collector + collected heads - 5 actor ids
Tag `wandering` in tables (needs a Trophy equipped).

| id | display name | size | HP | role / variant | data scope |
|---|---|---|---|---|---|
| `shared_collector` | The Collector | 1 | 90 | boss / mini-boss | base |
| `shared_head_hwy` |  | 1 | 20 |  | base |
| `shared_collector_head_hwy` | Collected Highwayman | 1 | 20 | summoned by an effect | base |
| `shared_collector_head_maa` | Collected Man-at-Arms | 1 | 20 | summoned by an effect | base |
| `shared_collector_head_ves` | Collected Vestal | 1 | 20 | summoned by an effect | base |

#### Roaming mini-boss: Death (Flagellant-linked) - 2 actor ids
Table `death_mashes`; appended to many road fights via `m_AdditionalBattleConfigurationTableId`.

| id | display name | size | HP | role / variant | data scope |
|---|---|---|---|---|---|
| `shared_death` | Death | 2 | 80 | boss / mini-boss | base |
| `shared_death_headstone` | Headstone | 1 | 99 | prop/obstacle | base |

#### Roaming mini-boss: Shambler - 2 actor ids
Tables `shambler_mash`, `shambler`, `shambler_hazard`, `shambler_rough_patch`.

| id | display name | size | HP | role / variant | data scope |
|---|---|---|---|---|---|
| `shared_shambler` | Shambler | 2 | 85 | boss / mini-boss | base |
| `shared_shambler_tentacle` | Shambler Tentacle | 1 | 12 | summoned by an effect | base |

#### Creature-Den boss: "Ancient Adversary" (per biome) - 1 actor ids
Config `ancestor_statue_<biome>`, 6-round limit.

| id | display name | size | HP | role / variant | data scope |
|---|---|---|---|---|---|
| `shared_ancestor_statue` | Ancient Adversary | 4 | 225 | boss / mini-boss | base |

#### Mountain act bosses and final boss parts (see section 4) - 34 actor ids

| id | display name | size | HP | role / variant | data scope |
|---|---|---|---|---|---|
| `boss_arms_phase1` | Ravenous Reach | 4 | 65 | Mountain act-boss part | base |
| `boss_arms_phase2` | Ravenous Reach | 4 | 75 | Mountain act-boss part | base |
| `boss_arms_phase3` | Ravenous Reach | 4 | 100 | Mountain act-boss part | base |
| `boss_brain_lock_health` | Padlock of Wasting | 1 | 55 | Mountain act-boss part | base |
| `boss_brain_lock_melee` | Latch of Regret | 1 | 55 | Mountain act-boss part | base |
| `boss_brain_lock_ranged` | Bolt of Lamentation | 1 | 55 | Mountain act-boss part | base |
| `boss_brain_lock_stress` | Shackle of Despair | 1 | 55 | Mountain act-boss part | base |
| `boss_eyes` | Focused Fault | 4 | 250 | Mountain act-boss part | base |
| `boss_eyes_stalk_l` | Cluster of Eyes | 1 | 12 | Mountain act-boss part | base |
| `boss_eyes_stalk_m` | Bifurcated Eye | 1 | 8 | Mountain act-boss part | base |
| `boss_eyes_stalk_s` | Cloistered Eye | 1 | 6 | Mountain act-boss part | base |
| `boss_body_failure_flg` | Spectre | 1 | 30 | final-boss "failure" form | base |
| `boss_body_failure_gr` | Spectre | 1 | 30 | final-boss "failure" form | base |
| `boss_body_failure_hel` | Spectre | 1 | 30 | final-boss "failure" form | base |
| `boss_body_failure_hwm` | Spectre | 1 | 30 | final-boss "failure" form | base |
| `boss_body_failure_jes` | Spectre | 1 | 30 | final-boss "failure" form | base |
| `boss_body_failure_lep` | Spectre | 1 | 30 | final-boss "failure" form | base |
| `boss_body_failure_maa` | Spectre | 1 | 30 | final-boss "failure" form | base |
| `boss_body_failure_occ` | Spectre | 1 | 30 | final-boss "failure" form | base |
| `boss_body_failure_pd` | Spectre | 1 | 30 | final-boss "failure" form | base |
| `boss_body_failure_run` | Spectre | 1 | 30 | final-boss "failure" form | base |
| `boss_body_failure_ves` | Spectre | 1 | 30 | final-boss "failure" form | base |
| `boss_body_cherub_spacer` |  | 1 | 99 | prop/obstacle | base |
| `boss_body_cherub` | Proclaimer | 1 | 26 | boss / mini-boss | base |
| `boss_body_phase3` | Body of Work | 2 | 999 | Mountain act-boss part | base |
| `boss_lungs_back` | Lung | 1 | 150 | Mountain act-boss part | base |
| `boss_lungs_core` | Seething Sigh | 2 | 250 | Mountain act-boss part | base |
| `boss_lungs_front` | Lung | 1 | 200 | Mountain act-boss part | base |
| `boss_body_spacer` |  | 1 | 99 | prop/obstacle | base |
| `boss_body_phase1` | Body of Work | 4 | 175 | Mountain act-boss part | base |
| `boss_body_phase2` | Body of Work | 4 | 200 | Mountain act-boss part | base |
| `boss_body_failure_abm` | Spectre | 1 | 30 | final-boss "failure" form | D2 |
| `boss_body_failure_cru` | Spectre | 1 | 30 | final-boss "failure" form | D1 |
| `boss_body_failure_dul` | Spectre | 1 | 30 | final-boss "failure" form | D1 |


## 4. Bosses

Three different boss concepts exist; do not confuse them:

1. **Act / confession bosses** (`Boss` rows, the five Mountain fights) - section 4.1. Expedition mode ("confessions" in condition ids) only.
2. **Lair bosses** (3rd round of a region's `*_dungeon_*` fight) - listed in section 2.3, round structure in section 5.
3. **Kingdoms gang bosses** (`Gang` rows + `kingdom_boss_mashes_master`) and **cultist guardians** (mini-bosses at region ends in Expedition) - sections 4.3 and 4.4.

### 4.1 Act bosses (`boss_data_export.Group.csv`: 6 `Boss`, 2 `ExtendedBoss`, 49 `BossModifier`, 6 `RunDataStats`)

`Boss` keys: `m_SelectBiomeType` (always `Valley` except prologue = `ValleyIntro`), `m_EndBiomeType` (the Mountain region), `m_PrerequisiteBossVictoryIds` (unlock order), `m_IntroNarrationId` / `m_OrderedMidNarrationIds` / `m_RecurringMidNarrationIds` / `m_OutroNarrationId` (narrator lines), `m_PrefabSubdirectoryId` (art folder), `boss_modifiers` (list of `BossModifier`), `doom_reset_hero_effects`, `doom_reset_actorless_effects`, `m_TorchLevelGroupId`, `m_IsRunGoalGenerating`, `m_IsExtendedById` (-> `ExtendedBoss` row: harder variant, with `m_RequiredStageCoachItemSlotType,Trophy` on `brain_extended`), `m_AcademicsHonorariumLimit`.

| `Boss` id | act / confession | end region | prerequisite | prefab dir | `doom_reset_hero_effects` | fight table (-> `battle_configuration_table`) | arena scene | fight actors (ids, HP, size) | defeated prompt |
|---|---|---|---|---|---|---|---|---|---|
| `prologue` | prologue | `ValleyIntro` | - | Prologue | `stress_damage_2` | prologue gaunts `intro_valley_road_mashes` (`intro_valley_gaunts_1..4`) | (not verified) | - | - |
| `brain` | Act 1, **Denial** | `MountainBrain` | - | Denial | `stress_damage_2` | `mountain_boss_table_brain` = 5 configs `mountain_boss_brain`, `_2`..`_5` (same four actors, 5 orderings) | `combat_arena_mountain_boss_brain` | `boss_brain_lock_melee`, `boss_brain_lock_health`, `boss_brain_lock_ranged`, `boss_brain_lock_stress` (55 HP each, size 1; "Latch of Regret", "Padlock of Wasting", "Bolt of Lamentation", "Shackle of Despair") | "Shackles Destroyed!" |
| `lungs` | Act 2, **Resentment** | `MountainLungs` | `brain` | Resentment | `affinity_neg1_100pct_nocrit` | `mountain_boss_table_lungs` -> `mountain_boss_lungs` | `combat_arena_mountain_boss_lungs` | `boss_lungs_front` (200), `boss_lungs_core` (250, size 2, "Seething Sigh"), `boss_lungs_back` (150); front/back `m_DeathChainIds,boss_lungs_core` | "Resentment Purged!" |
| `eyes` | Act 3, **Obsession** | `MountainEyes` | `lungs` | Obsession | `add_1_quirk_obsession` | `mountain_boss_table_eyes` -> `mountain_boss_eyes` (4x `boss_eyes_stalk_l`, summon controller `death_wave`, `m_AdditionalBattleConfigurationTableId,mountain_boss_table_eyes_phase_2`) then `mountain_boss_eyes_phase_2` | `combat_arena_mountain_boss_eyes` | phase 1: `boss_eyes_stalk_l` (12), `_m` (8), `_s` (6), `boss_eyes_stalk_s_corpse` (death phase round 3); phase 2: `boss_eyes` (250, size 4, "Focused Fault") | "Obsession Purged!" |
| `arms` | Act 4, **Ambition** | `MountainArms` | `eyes` | Ambition | - (actorless instead: `remove_1_stagecoach_armor`, `remove_1_stagecoach_wheel`) | `mountain_boss_table_arms` -> `mountain_boss_arms` | `combat_arena_mountain_boss_arms` | `boss_arms_phase1` (65), `_phase2` (75), `_phase3` (100) all size 4, "Ravenous Reach" (phase switching mechanism not verified) | "Ambition Purged!" |
| `body` | Act 5, **Cowardice** (final) | `MountainBody` | `arms` | Cowardice | `add_cowardice_failure_debuff` | `mountain_boss_table_body` -> `mountain_boss_body` (`m_endBossCinematicName,EndBossVictory`) | `combat_arena_mountain_boss_body` (+ `combat_intro_boss_body`) | `boss_body_phase1` (175, size 4, stomach), `boss_body_phase2` (200, size 4, torso; on death spawns front+back `boss_body_cherub`), `boss_body_phase3` (999, size 2, tags `god,worshipped`, "Body of Work"), `boss_body_cherub` (26, "Proclaimer"), spacers `boss_body_spacer`, `boss_body_cherub_spacer` (HP 99 props), failure forms below | "Confession Complete!" |
| `brain_extended`, `lungs_extended` | harder re-runs of Acts 1 and 2 (`ExtendedBoss`) | same | - | same | same | same boss_modifiers prefixed `brain_extended_*`/`lungs_extended_*` | | | |

All five also share `m_TorchLevelGroupId` = the act id (`brain`, `lungs`, `eyes`, `arms`, `body`) and `doom_reset_actorless_effects,torch_damage_10`.

**Final-boss "failure" forms** - one actor per hero class, summoned by the skill `god_face_your_failure` (`boss_god_data_export`), all tags `monster,cosmic,god_failure,boss`, HP 30, size 1, display name "Spectre", `m_DeathChainIds,boss_body_phase3`:
`boss_body_failure_flg`, `_gr`, `_hel`, `_hwm`, `_jes`, `_lep`, `_maa`, `_occ`, `_pd`, `_run`, `_ves` (base, files `boss_failure <abbr>`), `boss_body_failure_cru`, `boss_body_failure_dul` [D1] (`dlc_dul_cru/boss_failure cru|dul`), `boss_body_failure_abm` [D2] (`dlc_catacombs/boss_failure_abm`). Effects `god_summon_failure_<abbr>` use `m_ChangeClassActorId` with conditions `target_is_in_rank_1`, `performer_is_<hero>`, `hero_party_has_<hero>`. No Bounty Hunter failure form exists.

**Files making up the Mountain fights** (`EXCEL/`): `boss_brain lock {health,melee,ranged,stress}`, `boss_lungs {front,core,back}`, `boss_eyes`, `boss_eyes_stalk_{l,m,s,s_corpse}`, `boss_arms_phase{1,2,3}`, `boss_stomach` (=phase1), `boss_torso` (=phase2), `boss_god` (=phase3), `boss_god cherub`, `boss_god cherub spacer`, `boss_spacer`, `boss_failure <abbr>` x11, `boss_blessing_data_export` (blessing buffs), `temp_boss_body_data_export`. Art: bundles `boss_brain|lungs|eyes|arms|body_assets_all` (section 11). Narration entries `narration_entry_data_export_*` keyed `denial_*`, `resentment_*`, `obsession_*`, `ambition_*`, `cowardice_*`.

**Mode / DLC gating**: all act bosses and parts are base game, Expedition ("confessions") only. DLC adds only the three `boss_failure` forms above. Kingdoms has no Mountain; it ends in the gang boss (4.3). Cultist `boss` tags differ between the `expedition` and `kingdom` folders (section 3).

### 4.2 Lair bosses (regions) - ids, trophies, rewards

Same table as section 2.3 (Librarian, Harvest Child, Dreaming General + Tap Root, Leviathan, Exemplar). Fight composition lives in `battle_configuration_data_export` (`city_dungeon_3_a..d`, `farm_dungeon_3`, `forest_dungeon_3`, `coast_dungeon_3`) and `kingdom_battle_configuration_data_export` (`tundra_dungeon_3`). Boss-specific side actors: Librarian `fanatic_librarian_stack_l/m/s` (books, 15 HP props with `m_Tags ... book_stack`), Harvest Child `..._harvest_fetid_meat` (47) / `..._harvest_putrid_meat` (51), Dreaming General's `lost_battalion_boss_taproot` (HP 99, `m_DeathChainIds,lost_battalion_boss_dreaming_general`: killing the root kills the general), Leviathan's `coastal_boss_leviathan_hand` (24 HP, chained to Leviathan, spawned by effect `leviathan_summon_hand`), Exemplar's summons `cultist_altar|cherub|evangelist|herald` (`exemplar_summon_*` effects). Rewards: `lair_boss_rewards_all` (section 7). `m_IsNextBattleOptional,True` on all lair rounds.

### 4.3 Kingdoms gang bosses (`kingdom_gang_data_export`: 3 `Gang`; Kingdoms-only content)

| `Gang` id | `m_BossBiomeType` | boss fight config (`kingdom_boss_mashes_master`) | actors | quest (`Quest`) |
|---|---|---|---|---|
| `beastmen` | `Tundra` | `beastmen_boss` | `beastmen_meat_hook` (160, size 3, "Meat Hook"), `beastmen_meat_post` (99, prop-boss) | `beastmen` (quest steps `quest_beastmen_*`, 16 steps, ends `quest_beastmen_kill_final_boss`; alpha fight `beastmen_alpha_wave`) |
| `courtier` | `Coast` | `courtier_boss` | `courtier_archduke` (174, size 2, "Archduke"; summons `courtier_banquet`, `_chevalier`, `_esquire`, `_sycophant`, `_swarm`) | `courtier` (8 steps; `quest_courtier_formulate_cure`, cure items `courtier_cure`...) |
| `coven` | `Forest` | `coven_boss` | `coven_mothers_spitter` + `coven_matres_sequelae` (160, size 3, "Mother of Threads"; effects `matres_summon_mushroom` -> `coven_mothers_mushroom`, `matres_summon_spider` -> `coven_mothers_spitter`; `coven_matres_spider` also exists as an actor) | `coven` (12 steps; witchbane dagger, `coven_capture_mothers_familiar`) |

Each also has region-level boss-approach fights `<gang>_boss_region_301/302`, siege fights (`<gang>_siege_*`, section 5), ambushes (`<gang>_ambush_*`), camp ambushes, and arenas `combat_arena_<biome>_boss_<gang>` + `combat_arena_<biome>_barricade_gang_<gang>`. Escalation levels 1/2/3 (`kingdom_escalation_levels_data_export`) scale siege strength; difficulty rows `radiant`, `normal`, `stygian`, `blood_moon` (`kingdom_difficulty_data_export`).

### 4.4 Cultist guardians (Expedition; `cultist_guardian_biome_*` configs)

Guardian nodes (`NodeType.GUARDIAN`, `CombatSource.GUARDIAN`) at the end of typical regions are cultist fights chosen by `cultist_mashes_guardian_confessions` from the typical-region index (`biome_typical_equals_N`) and the act (`run_boss_is_*`):

| region index of the run | table (weight) | configs | actors |
|---|---|---|---|
| 1 | `cultist_guardian_biome_1_boss` (1) | `cultist_guardian_biome_1_altar_1..5` | `cultist_evangelist` x1-2 + `cultist_altar` + `cultist_cherub` or `cultist_herald` |
| 2 | `cultist_guardian_biome_2_boss_deacon` (6) | `cultist_guardian_biome_1_boss_1..5` (sic) | `cultist_deacon` + `cultist_altar` + herald/cherub (or evangelist + deacon + altar) |
| 2, act is not brain/lungs | `cultist_guardian_biome_2_boss_cardinal` (5) | `cultist_guardian_biome_2_boss_1..5` (weights 15/15/1/1/1) | `cultist_cardinal` + `cultist_altar` + evangelist/cherub/herald |
| 3, act is lungs | `..._cardinal` (1) | as above | cardinal |
| 3, act is not lungs | `cultist_guardian_biome_3_boss` (1) | `cultist_guardian_biome_3_boss_1` | `cultist_exemplar` + `cultist_altar` |

Loot `guardian_cultist_1/2/3_rewards_all`; all use `m_RunDataStatsId,no_retreat`, `m_IsRollBattleModifier,False`. [D1] `node_replacement_data_export`: `guardian_to_warlord` (`NodeReplacement` Guardian -> Warlord), referenced from `DataNodeReplacements` id `quest_trophy_crusader_helmet_b` (so it applies while that quest trophy is equipped - inferred, not verified).

## 5. Battle configurations

Files: `battle_configuration_data_export` (789 `BattleConfiguration`), `battle_configuration_table_data_export` (253 table elements, base), `kingdom_battle_configuration_data_export` (223 configs), `kingdom_quest_beastmen_data_export` (1: `beastmen_alpha_wave`), `dlc_catacombs/battle_configuration_*_DLC2` (71 configs, 27 tables), `dlc_dul_cru/battle_configuration_*_DLC1` (15 configs, 11 tables). Totals: **1,099 `BattleConfiguration`** (all ids unique) and **291 `BattleConfigurationTable`** elements (253 unique ids; same-id elements merge by appending options, section 1.2).

### 5.1 Two layers

- `BattleConfigurationTable` = a **weighted selector**. Parallel rows: `m_chances` (weights), `m_ids`, `m_types` (`battle_config` = leaf, `sub_table` = recurse into another table), `m_tags` (e.g. `wandering`, `KINGDOM_CHAMPS`, `death`), `m_conditions` (`Condition` ids joined by `+`; the option is only valid if all hold). Rolling = filter options by conditions, then weighted pick, recurse. An option can point at its own table id (a weight-19 self-reference in `faction_mashes_road_confessions` + merged per-biome rows = "base pool", versus the weight-1 `collector_mash_regions_confessions` options).
- `BattleConfiguration` = one concrete fight (up to 4 enemies; counts per fight: 1 enemy x24, 2 x73, 3 x453, 4 x548, 8 x1).

Property reference for `BattleConfiguration` (occurrences over 1,099 elements; meanings from names and decomp `BattleConfigurationDefinition`):

| key | n | meaning / observed values |
|---|---|---|
| `m_Chance` | 1099 | weight, always `1` in the data |
| `m_EnemyActors` | 1099 | ordered enemy actor ids; first = front rank (the City lair lists the book-stack props first and `fanatic_librarian` last, consistent with first = front rank; not verified in code) |
| `m_CompleteLootTables` | 980 | `LootTable` ids granted on win (e.g. `road_faction_rewards_all`, `lair_boss_rewards_all`); 54 distinct tables across the four loot keys |
| `m_CompleteStoryLootTables` | 516 | same, when the fight was reached through a Story/Resistance node (e.g. `resistance_faction_rewards_all`) |
| `m_IsRollBattleModifier` | 854 | roll a `BattleModifier` (`elite_monsters`, `frenzied_monsters`, `hale_monsters`, `hastened_monsters`, `impervious_monsters`, `infectious_monsters`, `gargantuan_monsters`, `entropic_monsters`, `prowling_monsters`, `unyielding_monsters`, `intimidating_monsters`, `dormant_monsters`, `ambush_monsters`, `final_showdown`, `urgent_repairs`, `urgent_repairs_wheels`, `battlemodifier_beastmen_1-4`, `_coven_1-4`, `_courtier_1-3`; 27 rows in `battle_modifier_data_export`) |
| `m_BattleModifierOverrideId` | 217 | force one: `urgent_repairs` 45, `urgent_repairs_wheels` 45 (hazard/rough-patch fights), `ambush_monsters` 23, Kingdoms siege modifiers |
| `m_EnemySummonControllerConfigurationId` | 405 | `death_wave` 367 (invisible wave bookkeeping for Death), `wave` 36 (shows wave progress), `invisible_wave`, `grave_robber_chapter_4_enemies` |
| `m_AdditionalBattleConfigurationTableId` | 403 | a second table rolled mid-fight/after (the roaming-boss hook): `death_mashes` 366, `beast_mashes_hard` 17, `surgeon_node_mashes_a/b` 18, `mountain_boss_table_eyes_phase_2` 2 |
| `m_RunDataStatsId` | 374 | `no_retreat` 352 (retreat chance x -1), `ambush` 22 |
| `m_BackgroundSceneOverride` | 173 | arena scene id (53 distinct, all in section 11); configs without it use the default arena for region + combat source |
| `m_Tags` | 247 | `cultist` 106, `glossary_repair(_wheels)`, `warlord` 10, `ancestor_statue` 7, `glossary_act1_boss`...`glossary_act5_boss`, `glossary_farm_boss`, `glossary_forest_boss` |
| `hero_effects` | 141 | `lair_midfight_heal` 85 (heal 15% between lair rounds), `add_slime_drip_spawner_token` 53 [D2], `move_shuffle_shambler_start` 3 |
| `actorless_round_start_effects` | 83 | `start_coast_fog` (Coast arena modifier) |
| `m_IsNextBattleOptional` | 112 | player may stop after this round (lair rounds) |
| `m_NextBattleConfigurationId` / `m_NextBattleConfigurationTableId` | 64 / 40 | chain the next lair round (single config or table) |
| `m_RunLimit` | 42 | max once per run (all `= 1`: collector, shambler, death, warlord...) |
| `m_PlayerActors`, `m_ResultActors`, `m_EndAtMaxStress`, `m_EndConditionsIsComplete`, `m_EndActorConditionClassIds`, `end_actor_all_conditions`, `m_TokenViewValid` | 27 / 28 / 24 / 6 / 5 / 5 / 27 | hero-origin-story fights (scripted player side, success conditions) |
| `m_TorchOverride` | 27 | `no_torch` (hero stories) |
| `actorless_effects` | 27 | `torch_damage_50` 23 (camp ambush), `torch_set_to_1` 3 (shambler) |
| `m_RoundLimit` | 7 | `6` (ancient adversary statues) |
| `m_HasEndSequence`, `m_EndSequenceDelay`, `m_endBossCinematicName` | 5 / 5 / 1 | Mountain brain, final boss (`EndBossVictory`) |
| `m_EnemyRandomOrder` | 4 | Death (`death_*`) shuffles ranks |

### 5.2 How tables are keyed

There is no per-biome/difficulty column; keying is done with **conditions** on the options (`condition_data_export` + others; each is a `Condition` with `m_ConditionType` + string/number):

| condition id (examples) | `m_ConditionType` | meaning |
|---|---|---|
| `is_confessions` / `is_kingdoms` | `game_type` = `kingdom` (`m_ConditionMetTarget,False` for confessions) | Expedition vs Kingdoms ("confessions" = Expedition) |
| `city_biome_check`, `farm_biome_check`, `forest_biome_check`, `shroud_biome_check` (Coast), `tundra_biome_check`, `cave_biome_check`, `catacombs_biome_check`, `current_biome_is_valley`... | `biome` = `City`/`Farm`/... | which region |
| `biome_typical_equals_1/2`, `biome_typical_equal_or_greater_than_2/3` | `biome_typical_count` | index of the typical region within the act (1st/2nd/3rd) - the **difficulty ramp** |
| `run_boss_is_brain/lungs/eyes/arms/body`, `run_boss_is_not_*` | `boss` | which act |
| `stagecoach_has_infernal_torch_5_equipped`, `stagecoach_has_collectors_chandelier_equipped`, `heroes_have_trophy_equipped`, `has_0_stagecoach_armor/wheels` | stagecoach item/run-value | player gear (champion/brutal fights gate on infernal torch; hazard/rough-patch gate on missing armor/wheels) |
| `escalation_is_1/2/over_2`, `kingdom_gang_is_beastmen`, `_coven`, `_courtier`, `quest_*_current_step_*`, `has_quest_*` | `run_value escalation`, `gang`, quest state | Kingdoms difficulty/gang/quest |
| `shambler_torch_threshold` (torch <= 30), `hero_party_has_flagellant`, `option_allow_death_no_flagellant`, `not_killed_chirurgeon_biomestatus`, `warlord_below_1`... | misc | roaming bosses, statuses |

Difficulty tiers inside a faction are expressed by id ranges and sub-tables: `<faction>_mash_1xx` (101-114, mostly **orphans**, not referenced by any table), `2xx` = "normal", `3xx` = "hard", suffix `C`/`C_2` = champion variants (`_b` actors), `_BC_01..04` = "brutal champions"; tables `<faction>_mashes_normal(_confessions|_kingdoms)`, `_hard`, `_normal_champions`, `_hard_champions`, `_brutal_champions` choose among them using `biome_typical_*`, `run_boss_*` and torch conditions (e.g. `fanatic_mashes_hard_confessions` weights 24:20:6:6:16:9 across `hard`, `hard_champions` x4, `brutal_champions`). Reading the tables: the faction **road** table (`faction_mashes_road_confessions`) always draws `*_normal_confessions` (champion sub-tables only when the infernal torch 5 is equipped or by region/act conditions); the **Resistance** (story fight) tables `faction_mashes_resist_region_N_confessions` pick `normal` in region 1, `normal`:`hard` = 1:1 in region 2 and 1:3 in region 3; the **Pillager** road tables `pillager_mashes_road_region_N_confessions` use `normal` in region 1, `normal`:`hard`(:`antiq` wandering) = 8:8(:2) in region 2 and 5:10(:3) in region 3.

**117 of the 1,099 configs are orphans** (referenced by no table): `fanatic_mash_101..111(+C)`, `coastal_mash_101..114`, `pillager_mash_101..106`, `*_ambush_1xx-3xx` (Kingdoms beastmen/courtier ambushes, referenced elsewhere), hero chapter fights (`<hero>_chapter_N`, started from hero stories), `coast_dungeon_1a`/`_2h_c`, `mountain_boss_eyes_quick_phase_test`, `im_just_a_ghoul`... Orphans are still valid ids; they are just not in any random pool.

**Entry-point (root) tables** - table ids never referenced by another table, i.e. looked up from code/prefabs (46 of 253): `faction_mashes_road_master`, `faction_mashes_resist_master`, `gaunt_mashes_road_master`, `pillager_mashes_road_master`, `military_mashes_road_master`, `underground_mashes_road_master`, `underground_mashes_resist_master`, `dregs_mashes_hazard_master`, `dregs_mashes_rough_patch_master`, `underground_mashes_hazard_master`, `underground_mashes_rough_patch_master`, `beast_mashes_creature_den_master`, `beast_mashes_hard`, `dungeon_mashes_starter_master`, `biome_{city,farm,forest,coast,tundra}_dungeon_round2`, `biome_city_dungeon_round3`, `camp_mashes_master`, `ambush_mashes_master`, `chirurgeon_mashes_surgeon_node`, `surgeon_node_mashes_a/b`, `cultist_mashes_guardian_master`, `cultist_mashes_story_cultist_master`, `cultist_mashes_mountain_1_master`, `cultist_mashes_mountain_2_master`, `death_mashes`, `ancestor_statue_mashes_master`, `kill_contract_mashes_resist_master`, `valley_road_mashes`, `intro_valley_road_mashes`, `gang_mashes_siege_master`, `kingdom_gang_mashes_road_master`, `kingdom_boss_mashes_master`, `beastmen_quest_alpha_wave_mashes`, `coven_quest_assist_iscoven_mashes`, `coven_quest_assist_notcoven_mashes`, `mountain_boss_table_{brain,lungs,eyes,eyes_phase_2,arms,body}`. (Inventory of how code maps node type -> root table: `TriggerCombatBhv.m_battleConfigurationTableId` is a serialized Unity field, so the mapping is not in the CSVs - not verified.)

Node types and combat sources (decomp, the vocabulary behind "keyed by node type"): `NodeType`: Cache, Gate, Hospital, Inn, Store (Hoarder), WatchTower, StoryCultist(+Mountain01/02), StoryAssist, StoryResist, StoryCosmic, StoryHero, StoryHeroReplacement, **Dungeon**, **Guardian**, CreatureDen, Oasis, HeroSelect, AltarOfHope, Bridge, GameResults, BossSelect, Landmark*, Cathedral, Mountain, GauntChirurgeon, Warlord, BeastmenAlpha, KingdomInn, KingdomInnSieged, KingdomCamp, KingdomBoss, StoryAssistGang, CacheGang, BridgeGang, CovenAssist. `CombatSource`: BARRICADE, DUNGEON, CULTIST, CULTIST_MOUNTAIN_01/02, STORY_ASSIST/RESIST/COSMIC/HERO, GUARDIAN, OASIS, CREATURE_DEN, CATHEDRAL, GAUNT_CHIRURGEON, REPAIR, AMBUSH, WARLORD, KINGDOM_INN_SIEGED, KINGDOM_BOSS, BEASTMEN_ALPHA, CAMP_AMBUSH.

### 5.3 Count per region / faction

Counts of `BattleConfiguration` elements by id prefix (the "fights that exist"; tables then select among them):

| region | faction | road/resist fights (`<faction>_mash_*`) | brutal champion (`_BC_`) | lair rounds (`<biome>_dungeon_*`) | ancestor statue | military barricade |
|---|---|---|---|---|---|---|
| City | City / Fanatics | 45 | 4 | 22 | 1 | 6 |
| Farm | Farm / Plague Eaters | 60 | 4 | 33 | 1 | 6 |
| Forest | Forest / Lost Battalion | 78 | 4 | 20 | 1 | 6 |
| Coast | Coast / Coastal | 56 | 4 | 24 | 1 | 6 |
| Cave | Cave / Swine | 66 | 3 | - | 1 | - |
| Tundra (Kingdoms) | Tundra / Cultists | 24 | 3 | 13 | 1 | 6 |
| Catacombs (D2) | Catacombs / Vermin | 21 | - | - | 1 | - |

Other families (counts of `BattleConfiguration` elements):

- swine hazard / rough-patch (Cave route hazards): 32
- vermin hazard / rough-patch (Catacombs, D2): 16
- pillager (road, hazard, rough-patch, antiq variants): 60
- gaunt (road, ghoul, hazard, rough-patch, valley, intro valley): 51
- Gaunt Chirurgeon surgeon nodes (`surgeon_gaunt_*`, `patient_mash_*`, `_slime` D2): 47
- military barricade (`military_*`): 49
- cultist road/story (`cultist_mash_*`, torch ambush, doom mash, mountain, guardian): 66
- creature-den beasts (`beasts_*`, `rabies_train`, `jaws_and_paws`, ... + `_slime`): 37
- hero-story combats (`<hero>_chapter_*`, `*_combat_*`): 27
- roaming mini-bosses (`collector_region_*`, `shambler*`, `death_*`, `warlord_*`): 22
- Mountain confession bosses (`mountain_boss_*`): 11
- camp ambush (`camp_ambush_*`): 6
- Kingdoms courtier gang (valley/siege/boss/ambush/quest): 54
- Kingdoms beastmen gang: 54
- Kingdoms coven gang: 76


Dungeon (lair) structure per region: round 1 (`<biome>_dungeon_1x`, 5-7 configs, arena `*_dungeon_exterior`, `m_NextBattleConfigurationTableId` -> `biome_<biome>_dungeon_round2`), round 2 (`<biome>_dungeon_2x[_c|_c2]`, 7-27 configs incl. champion `_c/_c2` variants gated by `stagecoach_has_infernal_torch_5_equipped` or `biome_typical_equal_or_greater_than_3+run_boss_is_not_brain+run_boss_is_not_lungs`, effect `lair_midfight_heal`, loot `lair_prewave_2_rewards_all`), round 3 boss (`<biome>_dungeon_3[_a-d]`, 1-4 configs, loot `lair_boss_rewards_all`). Counts: City 22 (5/13/4), Farm 33 (5/27/1), Forest 20 (5/14/1), Coast 24 (7/16/1), Tundra 13 (5/7/1, Kingdoms).

### 5.4 Annotated examples

**(a) Normal road fight, City / Fanatics** - `fanatic_mash_201`

```
route (City)  route_city_combat_faction  -> (table root, mapping in Unity data, not verified)
faction_mashes_road_master                 [is_confessions] -> faction_mashes_road_confessions
faction_mashes_road_confessions            (merged: base self-ref w19 + collector w1+w1 + faction rows + D1 crusader row w999)
   option [city_biome_check] -> fanatic_mashes_normal_confessions
fanatic_mashes_normal_confessions          weights 10:5:2:2:5 ; champion rows need infernal torch 5 / biome_typical_equals_2+run_boss_is_arms|body / typical>=3...
   option (no condition) -> fanatic_mashes_normal  -> battle_config fanatic_mash_201,202,204,205,208,210,211
element_start,fanatic_mash_201,BattleConfiguration
m_Chance,1,
m_AdditionalBattleConfigurationTableId,death_mashes,         <- chance of Death joining (needs Flagellant in party)
m_EnemyActors,fanatic_whipper,fanatic_flayer,shared_lost_soul_widow,fanatic_immolatist,   <- rank 1..4
m_CompleteLootTables,road_faction_rewards_all,               <- -> confessions_road_faction_rewards / kingdoms_road_faction_rewards
m_CompleteStoryLootTables,resistance_faction_rewards_all,    <- same fight reached from a Resistance node
m_EnemySummonControllerConfigurationId,death_wave,
m_IsRollBattleModifier,True,                                 <- may roll elite_monsters etc.
element_end
```

**(b) Lair / boss fight, City** - 3 rounds, last = Librarian

```
dungeon_mashes_starter_master  option [city_biome_check] -> biome_city_dungeon_starters -> city_dungeon_1a..1e   (arena combat_arena_city_dungeon_exterior)
   city_dungeon_1a: m_NextBattleConfigurationTableId,biome_city_dungeon_round2 ; m_IsNextBattleOptional,True ; loot lair_prewave_1_rewards_all
biome_city_dungeon_round2 -> [is_confessions] biome_city_dungeon_round2_confessions  (normal w3 | champions w2 if infernal_torch_5 | champions w2 if region>=3 and act not brain/lungs)
   -> city_dungeon_2a..2f / _c / _c2 ;  next table biome_city_dungeon_round3 ; hero_effects,lair_midfight_heal
element_start,city_dungeon_3_a,BattleConfiguration
m_Chance,1,
m_BackgroundSceneOverride,combat_arena_city_dungeon_interior,   <- one of the 53 explicit arenas
m_IsNextBattleOptional,True,
m_RunDataStatsId,no_retreat,                                    <- cannot retreat (retreat_chance x -1)
m_EnemyActors,fanatic_librarian_stack_l,fanatic_librarian_stack_m,fanatic_librarian_stack_m,fanatic_librarian,   <- books in front, boss at rank 4
m_CompleteLootTables,lair_boss_rewards_all,                     <- confessions_lair_boss_rewards: DOOM_MINUS_THREE, HERO_POINT, TORCH_SOME, RELICS_LOT, BAUBLES_LOT, ALL_COMBAT_ITEMS, ALL_REST_ITEMS, SC_UPGRADES_ALL, trinkets, SC_TROPHY
hero_effects,lair_midfight_heal,                               <- Effect: heal 15% (blocked by party_has_no_lair_heal_blockers_equipped condition)
m_IsRollBattleModifier,False,
element_end
```

**(c) Kingdoms fights** - (c1) siege, (c2) Tundra lair, (c3) road

```
(c1) siege: gang_mashes_siege_master -> siege_mashes_{valley,easy,normal,hard} -> <gang>_mashes_siege_<level> -> beastmen_siege_2xx
element_start,beastmen_siege_101,BattleConfiguration           <- in kingdom_battle_configuration_data_export
m_BattleModifierOverrideId,battlemodifier_beastmen_1,         <- gang-specific modifier
m_RunDataStatsId,no_retreat,
m_EnemyActors,beastmen_huntsman,beastmen_tribewalker,beastmen_huntsman,beastmen_tribecaller,
m_CompleteLootTables,siege_rewards_all,                       <- by escalation_is_1/2/over_2 + siege strength
element_end
(c2) tundra_dungeon_3  (Kingdoms-only region): m_EnemyActors,cultist_exemplar,cultist_altar, arena combat_arena_tundra_dungeon_interior, loot lair_boss_rewards_all -> kingdoms_lair_boss_rewards (biome_reward, CONSTRUCTION_CURRENCY_10, quest items)
(c3) road: faction_mashes_road_master [is_kingdoms] -> faction_mashes_road_kingdoms (-> faction_mashes_road_kingdoms_mix)
        -> <faction>_mashes_normal_kingdoms (champions gated by escalation_is_2 / escalation_is_over_2, tag KINGDOM_CHAMPS), Tundra: cultist_tundra_mash_201..303_BC
```

Other Kingdoms battle tables: `valley_road_mashes_kingdoms` (by `kingdom_gang_is_*` -> `valley_{beastmen,coven,courtier}_mashes`), `kingdom_gang_mashes_road_master` (gang boss approach fights when the region end node is `kingdomboss`), `camp_mashes_master` (`camp_ambush_mashes_e1..e3` by escalation + gang camp ambushes), `kill_contract_mashes_resist_master`.

## 6. Heroes

15 hero class ids (ActorDataClass tag `hero`): 11 base heroes, the Bounty Hunter (defined twice, see below), 1 from `dlc_catacombs`, 2 from `dlc_dul_cru`. Every hero has an `<id>` and an `<id>_corpse` `ActorDataClass`.

| class id | display | canonical name | file | skills (`ActorDataSkill`) | paths (`ActorDataPath`) | unlock track | origin/gating |
|---|---|---|---|---|---|---|---|
| `flagellant` | Flagellant | Damian | `hero_flg_data_export.Group.csv` | 49 | flg_maniac, flg_exanimate, flg_scourge | `flagellant` (14) | base game |
| `grave_robber` | Grave Robber | Audrey | `hero_gr_data_export.Group.csv` | 53 | grv_deadeye, grv_nightsworn, grv_venomdrop | `grave_robber` (13) | base game |
| `hellion` | Hellion | Boudica | `hero_hel_data_export.Group.csv` | 67 | hel_wanderer, hel_reserve, hel_ravager, hel_berserker, hel_carcass | `hellion` (14) | base game |
| `highwayman` | Highwayman | Dismas | `hero_hwm_data_export.Group.csv` | 50 | hwy_reserve, hwy_wanderer, hwy_sharpshot, hwy_rogue, hwy_yellowhand | `highwayman` (13) | base game |
| `jester` | Jester | Sarmenti | `hero_jes_data_export.Group.csv` | 63 | jes_reserve, jes_wanderer, jes_virtuoso, jes_soloist, jes_intermezzo | `jester` (14) | base game |
| `leper` | Leper | Baldwin | `hero_lep_data_export.Group.csv` | 57 | lep_wanderer, lep_reserve, lep_tempest, lep_poet, lep_monarch | `leper` (14) | base game |
| `man_at_arms` | Man-at-Arms | Barristan | `hero_maa_data_export.Group.csv` | 50 | maa_sergeant, maa_bulwark, maa_vanguard | `man_at_arms` (13) | base game |
| `occultist` | Occultist | Alhazred | `hero_occ_data_export.Group.csv` | 71 | occ_ritualist, occ_warlock, occ_aspirant | `occultist` (14) | base game |
| `plague_doctor` | Plague Doctor | Paracelsus | `hero_pd_data_export.Group.csv` | 59 | plg_surgeon, plg_alchemist, plg_physician | `plague_doctor` (13) | base game |
| `runaway` | Runaway | Bonnie | `hero_run_data_export.Group.csv` | 55 | run_arsonist, run_survivor, run_orphan | `runaway` (14) | base game |
| `vestal` | Vestal | Junia | `hero_ves_data_export.Group.csv` | 47 | ves_reserve, ves_wanderer, ves_seraph, ves_chaplain, ves_confessor | `vestal` (14) | base game |
| `bounty_hunter` | Bounty Hunter | Christiansen | `hero_bh_data_export.Group.csv` | 37 | bh_professional | `bounty_hunter` (10) | expedition/ + kingdom/ (two different definitions) |
| `abomination` | Abomination | Bigby | `hero_abm_data_export.Group.csv` | 67 | abm_unchained, abm_fiend, abm_moribund | `abomination` (13) | D2 dlc_catacombs |
| `crusader` | Crusader | Reynauld | `hero_cru_data_export.Group.csv` | 45 | cru_aggressor, cru_templar, cru_banneret | `crusader` (13) | D1 dlc_dul_cru |
| `duelist` | Duelist | Sahar | `hero_dul_data_export.Group.csv` | 54 | dul_reserve, dul_wanderer, dul_instructrice, dul_antagoniste, dul_intrepide | `duelist` (13) | D1 dlc_dul_cru |


Notes on the table: "skills" = number of `ActorDataSkill` elements in the hero file (includes upgraded `_u`/path variants and the generic `move`/`pass`); "paths" = `ActorDataPath` elements whose `m_ActorClassIds` names the class (`<abbr>_wanderer` / `<abbr>_reserve` are the default/untrained and bench paths, the 3 others are the unlockable specialisations, each gated by `m_UnlockId` `<class>_<n>`); canonical names are `hero_name_canonical_<class>` in `LOC` (Damian, Audrey, Boudica, Dismas, Sarmenti, Baldwin, Barristan, Alhazred, Paracelsus, Bonnie, Junia, Christiansen, Bigby, Reynauld, Sahar).

**Path ids** (all `ActorDataPath` in `actor_paths_data_export` + DLC variants):
Flagellant `flg_maniac, flg_exanimate, flg_scourge`; Grave Robber `grv_deadeye, grv_venomdrop, grv_nightsworn`; Hellion `hel_ravager, hel_berserker, hel_carcass`; Highwayman `hwy_rogue, hwy_sharpshot, hwy_yellowhand`; Jester `jes_virtuoso, jes_soloist, jes_intermezzo`; Leper `lep_tempest, lep_poet, lep_monarch`; Man-at-Arms `maa_sergeant, maa_bulwark, maa_vanguard`; Occultist `occ_ritualist, occ_warlock, occ_aspirant`; Plague Doctor `plg_surgeon, plg_alchemist, plg_physician`; Runaway `run_arsonist, run_survivor, run_orphan`; Vestal `ves_confessor, ves_chaplain, ves_seraph`; Bounty Hunter `bh_professional` (single); Abomination [D2] `abm_unchained, abm_fiend, abm_moribund`; Crusader [D1] `cru_aggressor, cru_templar, cru_banneret`; Duelist [D1] `dul_instructrice, dul_antagoniste, dul_intrepide`. Defaults: `wanderer`/`reserve` (shared), `hel_wanderer`, `hel_reserve`, `hwy_wanderer`, `hwy_reserve`, `jes_*`, `lep_*`, `ves_*`, `dul_wanderer`, `dul_reserve` (class-specific).

**Where hero data lives**

| what | table file(s) |
|---|---|
| class, stats, skills, skill effects, level `Unlock`s (`<class>_N`) | `hero_<abbr>_data_export` (15 files; BH: `expedition/hero_bh_data_export` 12 skills **and** `kingdom/hero_bh_data_export` 25 skills) |
| paths + path skill replacements (`SkillReplacement` 431: 345 base / 48 D1 / 38 D2) | `actor_paths_data_export`, `_DLC1`, `_DLC2` |
| default skill kits (Kingdoms; `SkillSet` 61: 44 base, 8 D1, 5 D2, 4 `kingdom/`) | `kingdom_hero_skill_sets_data_export`, `_DLC1`, `_DLC2`, `kingdom/kingdom_hero_skill_sets_data_Kingdoms` |
| level / mastery tracks (`UnlockTrack` per class: 13 or 14 unlocks, plus `stagecoach` 13, `memory` 8, `resources` 13, `prestige` 10, `pets` 10, `infernal_flame` 9) paid with Candles (`Cost` with `m_ProfileValueType,candles`) | `progression_data_export`, `_DLC1`, `_DLC2` |
| cosmetic unlock tables (`unlock_table_cosmetics_<class>`: weapon kits + palettes) | `unlock_tables_data_export`, `_DLC1`, `_DLC2` |
| quirks container / limits (`roster_quirk_container`: 1 positive + 1 negative at start, caps 3/3/1 disease; `leper_quirk_container` excludes `disease_leprosy`; `crusader_quirk_container` guarantees `quirk_kleptomania_neg`) | `hero_*`, `kingdom_temp`, `dlc1_miscellaneous` |
| global hero rules (`hero` Rules: 2 trinket slots, 5 memory slots, cure/remove/lock quirk costs in candles 12/16/32, reroll costs 6-24) | `hero_rules_data_export` |
| kingdoms hero levelling (`kingdom_hero_upgrade_1..5`, cost 1 `hero_upgrade_points`; `<hero>_kingdoms_buff_1..5`) | `kingdom_hero_stat_upgrade_data_export`, `_DLC1`, `_DLC2`, `kingdom/..._kingdom_override` |
| origin stories ("hero shrines", 2 per hero: `herostory_<abbr>_1/_2` actors; combats `<hero>_chapter_N` / `*_combat_N`; choices `story_choice_export_herostory`; arenas `combat_arena_hero_story_<hero>_origin_1/2`) | `herostory_*` (128 files: 91 base, 27 [D1], 10 [D2]), `story_choice_export_herostory`, `battle_configuration_data_export` |
| relationships / affinity | `affinity_data_export`, `affinity_leaning_level`, `affinity_rules`, `relationship_definition_data_export`, `act_out_data_export`, `quirk_act_out_data_export`, `party_names` (`LOC`: 360 `party_name_<4 class ids>`) |
| art/portraits/skill icons | `AA`: `hero_<class>_assets_basegame` (bundles), catalog keys `<class>_art_prefab`, `<class>_portraits`, `<class>_skill_icons`, `<class>_story_portraits`, `<class>_wpn_<path>_art_prefab` (15 `wpn_infernal`), `<class>_skn_origin_art_prefab` (14) |

**Bounty Hunter** - `bounty_hunter` exists in `Excel/expedition/hero_bh_data_export` (single path `bh_professional`, `m_StartingRosterStatusType,hire`, 12 skills, no `Unlock`s - a hireable merc; HP 48) and in `Excel/kingdom/hero_bh_data_export` (path `wanderer`, `skill_sets` `bh_skill_kit_1..4`, `idle` roster, 25 skills, 11 `Unlock`s; HP 42). Unlock track `bounty_hunter` (10) in `progression_data_export`. Display "Christiansen".

**DLC / content gating found in data** (decomp `DLCConfigType` lists exactly these four + `none`; the task's "5 DLCs" - a fifth entitlement was **not found** in data, decomp or bundles; the two game types `expedition`/`kingdom` are separate content switches, not DLC):

| DLC id (folder) | store name (`LOC dlc_*_label`) | gates | data files |
|---|---|---|---|
| `dlc_dul_cru` (DLC1) | The Binding Blade | heroes Crusader + Duelist; Warlord boss + military-barricade replacement (`military_region_N` rows, node `guardian_to_warlord`); Crusader profile quest (`Quest crusader`, 9 steps, `quest_data_export`); 2 trophies (`quest_trophy_crusader_helmet_a/b`), `quest_sanctified_scroll`, `quest_rumour_of_riches`, `blade_oil`, `unique_reliquary`; hero-story fights; `boss_failure cru/dul` | 65 CSVs; 9 trinkets + 6 items + 3 stagecoach upgrades; bundles `dlc_dul_cru/*` (hero_crusader, hero_duelist, faction_shared_warlord, 4 warlord arenas, stories) |
| `dlc_catacombs` (DLC2) | Inhuman Bondage | hero Abomination (human/beast `ActorDataMode`); region **Catacombs** (7 Vermin actors, 21+16 fights, `ancestor_statue_catacombs`, slime variants of creature den/surgeon fights), `boss_failure_abm`, Catacombs items (`catacombs_*` combat items, pets `catacombs_pet_cat`, salt barrel, lanterns), `trinket_catacombs_*` | 48 CSVs; 6 trinkets + 15 items + 3 stagecoach upgrades; bundles `dlc_catacombs/*` (biome_catacombs, faction_vermin, hero_abomination, `dlc_catacombs_scenes_scenes_all` with 9 arenas) |
| `dlc_supporter` (DLC3, "supporter pack") | cosmetics only | `StageCoachSkin` `wagon_skin_beastmen/courtiers/coven/slime`; per-hero weapon kits (bundles `dlc_supporter_hero_*`) | 1 CSV (`dlc3_miscellaneous`) |
| `dlc_origin_skins` | cosmetics only | per-hero "origin" skins (11 base + crusader/duelist/abomination skins in the DLC folders) | 0 CSV, 11 bundles |

## 7. Items, currencies, quirks, loot

### 7.1 Item categories (class `Item`, key `m_type`; 615 elements, 602 unique ids - `pet_*`, `catacombs_pet_cat`, `nightshade_concoction`, `stitching_kit` exist in both base and `kingdom/` override)

| `m_type` | count | sub_types (count) | main table files | role |
|---|---|---|---|---|
| `trinket` | 211 | rare 49, epic 125, common 12, cultist 22, ancestral 3 | `trinkets_data_export` (191), `_DLC1` (9), `_DLC2` (6), `trinkets_actor_effect_trigger_data_export` (5) | equippable, 2 slots per hero (`hero` Rules) |
| `combat` | 85 | `hybrid_combat` 5 (courtier blood items) | `item_data_export` (55), `kingdom_quest_*` (19), `_DLC2` (9), DLC1 (2) | consumables used in combat (`m_usedInCombat`) |
| `rest` | 72 | `hero` 15 | `item_data_export` (59), `_DLC1` 2, `_DLC2` 4, `kingdom_override` 2, `kingdom_quest_*` 5 | inn/camp items (food, drink, books, tools; hero-specific) |
| `stage_coach_upgrade` | 118 | common 21, rare 39, infernal 11, radiant 4, trophy 18, pet 25 (14 unique ids; 11 re-declared in `kingdom/`) | `stagecoach_upgrades_export` (81), `_hardcoded_export` (5), `_DLC1` 3, `_DLC2` 3, `kingdom_override` 11, `kingdom_quest_*` 15 | stagecoach slots: General 60, Pet 25, Trophy 18, Flame 15 |
| `memory` | 20 | - | `memory_data_export` | hero memories bought at the Altar of Hope |
| `currency` | 109 | deliverable 92, faction 7, quest 4, relics 1, profile 1, construction 1, cru_quest_currency 1 (+2 untyped) | `item_data_export` (62), kingdom quest files (41), DLC (5) | resources and quest deliverables |

`ItemSubtype` (19 rows, `item_subtype_data_export` + `_DLC1`): relics, faction, cultist, common, rare, epic, ancestral, trophy, animals, profile, hero, deliverable, pet, infernal, radiant, construction ... (sort priority, loot narration tags).

Item keys seen: `m_type`, `m_maxQty` (stack), `m_buyCostId`/`m_sellCostId` (-> `Cost`), `m_combinable`, `m_isConsumable`, `m_usedInCombat/Driving/Inn`, `m_slot` (`General|Pet|Trophy|Flame`), `sub_type`, `m_tags`, `m_effectIds`, `m_conditionIds`, `m_UnlockId` (unlock gating, e.g. `infernal_flame_1`, `pet_1`), `m_possessionLimit`, `m_DurationType/Amount` (`inn_end`...), `m_QuestResourceId`, `m_numberOfTargets`; every item with passive effects has an `ActorDataExternalBuffs` + `Buff` element of the same id.

### 7.2 Currencies and run resources (DD1 analogues in brackets)

| name (display) | id | kind | notes |
|---|---|---|---|
| **Relics** [gold] | item `gold` (`sub_type relics`, stack 40) | currency item | loot via `RELICS_*` tables (qty 1-48); `run` Rules `m_StartingGold,40`; inventory `m_goldId,gold` |
| **Candles of Hope** [heirloom-like meta currency] | item `candles` (`sub_type profile`, stack 4) | profile currency | `m_ProfileValueType,candles`; Altar of Hope unlocks, hero level tracks, memories (5), quirk cure/remove/lock/reroll |
| **Baubles** (faction trophies) [heirlooms] | `city_books` (Charred Books), `farm_spoons` (Silver Spoons), `forest_medals` (War Medals), `cave_dirt` (Fertile Dirt), `coast_starfish` (Untainted Sea Stars), `valley_baubles` (Rural Riches), `tundra_baubles` (Pinecones) (`sub_type faction`, stack 40) | currency items | `BAUBLES_*` tables pick the one matching the region; Kingdoms repairs cost tag `faction` |
| **Inn Materials** (Kingdoms) | `materials` (`sub_type construction`, stack 40) | currency item | `CONSTRUCTION_CURRENCY_*`; pays `InnUpgrade` costs |
| **Torch / light** [torch] | run value `torch` | provision | `TORCH_TINY..ALL(_NEG)`; torch bands drive `TorchLevel` buffs; `torch_consumable` item "A Glimmer of Hope" |
| **Doom** | run value `doom` | provision | `DOOM_PLUS/MINUS_ONE..FIVE`; `doom_levels`; disabled in Valley/Mountain |
| **Stagecoach armor / wheels** | run values `stage_coach_armor`, `stage_coach_wheels` | provision | `ARMOR_PLUS/MINUS_*`, `WHEELS_PLUS/MINUS_*`; repaired at Wainwright (`RunValueTransaction`) |
| **Hero points** [~ DD1 skill/level resources] | run value `hero_upgrade_points` (`HERO_POINT` .. `HERO_POINT_FIVE`) | provision | spent on hero upgrades (Kingdoms: `kingdom_hero_upgrade_N`, cost 1); biome-goal / lair rewards |
| Quest currencies | `quest_beastmen_pelt` (Beast Hide), `quest_courtier_fuel_reserves`, `quest_courtier_research_journal`, `quest_coven_academics_keyring`, `quest_coven_witch_buck` (Wooden Token), `quest_rumour_of_riches` [D1] | `sub_type quest` | Kingdoms/Crusader quests |
| "Mastery points" | **no item/run value with that name exists**. Closest: hero level/path unlocks (`UnlockTrack` `<class>` with `Unlock` `<class>_N` bought with candles) and `hero_upgrade_points`. | - | not verified that nothing else is called mastery in UI text (`LOC progression.txt` has `upgrade_track_*` titles) |

### 7.3 Trinkets

Trinkets: 211 `Item` elements with `m_type,trinket` (unique ids 211); files `trinkets_data_export` (191), `trinkets_data_export_DLC1` (9), `trinkets_data_export_DLC2` (6), `trinkets_actor_effect_trigger_data_export` (5). By id family (sub_type in brackets):

- `trinket_boss_*` (5; epic:5): deaths_head_01, deaths_head_02, deaths_head_03, deaths_head_04, deaths_head_05
- `trinket_tiered_*` (36; common:12,rare:12,epic:12): anchoring_charm_minor, bouncers_belt_minor, cleansing_censer_minor, clotting_cruor_minor, gilded_mind_minor, hale_draught_minor, heartseeker_minor, heat_shield_minor, protectorate_minor, sacrificial_host_minor, sharpness_charm_minor, wolfsblood_minor, anchoring_charm, bouncers_belt, cleansing_censer, clotting_cruor, gilded_mind, hale_draught, heartseeker, heat_shield, protectorate, sacrificial_host, sharpness_charm, wolfsblood, anchoring_charm_greater, bouncers_belt_greater, cleansing_censer_greater, clotting_cruor_greater, gilded_mind_greater, hale_draught_greater, heartseeker_greater, heat_shield_greater, protectorate_greater, sacrificial_host_greater, sharpness_charm_greater, wolfsblood_greater
- `trinket_general_*` (14; rare:13,epic:1): adrenalizing_ash, stirring_snuff, bulwark_band, buttressing_band, guarding_gauntlet, clandestine_cape, disarming_dagger, strange_sapper, strong_shackles, dead_ringer, distracting_dust, gnarly_knuckles, ravens_reach, thrilling_tablet
- `trinket_cave_*` (10; rare:4,epic:6): peculiar_pods, rousing_recorder, sneakers_standard, vanished_vanity, covert_cloak, staggering_striker, goading_gargoyle, rousing_ringer, bone_mallet, pig_sticker
- `trinket_city_*` (9; rare:5,epic:4): hastening_history, laden_lantern, parrying_patriarch, sacred_scribblings, tinderbox, enlightening_element, snappy_swig, boss_charred_litany, boss_smoldering_hymnal
- `trinket_coast_*` (9; rare:5,epic:4): clasp_knife, fishmongers_gloves, leather_strop, pristine_lure, seamens_boots, fishermans_line, nautical_compass, boss_carved_bodkin, boss_sodden_sweater
- `trinket_farm_*` (9; rare:5,epic:4): brilliant_brew, corrupting_cleaver, hint_of_home, hags_hoard, poison_ring, galvanizing_goblet, curing_cuppa, boss_ghastly_gruel, boss_kitchen_knives
- `trinket_forest_*` (9; rare:5,epic:4): armory_key, blistering_bugle, insulating_insignia, stone_mount, unwavering_standard, calibrating_censer, clenching_claws, boss_reverberating_redoubt, boss_footmans_grog
- `trinket_hero_*` (45; epic:45): bh_crime_lords_molars, bh_utility_belt, bh_vengeful_kill_list, gr_foreclosure_notice, gr_his_rings, gr_stiff_drink, hel_bloodied_branch, hel_empty_stein, hel_rotten_tomato, hwy_cursed_coin, hwy_rat_skull, hwy_tormenting_locket, jes_buskers_haul, jes_royal_summons, jes_severed_finger, lep_a_simple_flower, lep_inevitable_end, lep_uncommon_seashell, maa_price_of_pride, maa_standard_of_the_ninth, maa_undeserved_commendation, occ_scalded_skull, occ_seeing_sphere, occ_shamblers_eye, pd_annotated_textbook, pd_early_experiment, pd_storage_room_key, run_carved_toy, run_knitted_blanket, run_pile_of_ash, ves_icon_of_the_light, ves_profane_scroll, ves_smoldering_firewood, flg_searing_scripture, flg_his_prison, flg_emancipation, abm_antidote[D2], abm_confession[D2], abm_lock[D2], cru_fieldstone[D1], cru_pilfered_wealth[D1], cru_signed_indenture[D1], dul_academie_ring[D1], dul_dark_lanthorn[D1], dul_lovers_glove[D1]
- `trinket_antiq_*` (4; epic:4): celebrated_chalice, clarifying_carcanet, cleansing_clasp, shimmering_crown
- `trinket_curio_*` (15; epic:15): anatomical_map, astroglass_flute, befuddling_sundial, corrupted_bile_gland, faceless_visage, grim_mask, heart-shaped_padlock, obsidian_dronepipe, oversprung_pocketwatch, proxy_doll, pulsing_heart, true_entropy, caked_palette, blood_smeared_calculations, murder_weapon
- `trinket_hoarder_*` (6; epic:6): mortal_ward, inert_indicia, fates_foreteller, prodding_pendant, skeletons_sight, sparkleball
- `trinket_surgeon_*` (2; epic:2): appalling_apron, spiked_leather_cap
- `trinket_shambler_*` (4; epic:4): eyes_of_the_void, from_beyond, hierarchy_of_sights, unblinking_entropy
- `trinket_collector_*` (3; epic:3): barristans_head, dismas_head, junias_head
- `trinket_ancestors_*` (3; ancestral:3): coat, mustache_cream, pistol
- `trinket_cultist_*` (22; cultist:22): cruel_intent, hardened_heart, idle_thought, jealous_whisper, locked_jaw, misstep, selfish_motivation, sickening_silence, silent_treatment, snap_judgement, spoken_sharply, temptation, wounding_words, key_dark_impulse_bleed_res, key_dark_impulse_blight_res, key_dark_impulse_burn_res, key_dark_impulse_move_res, key_dark_impulse_disease_res, key_dark_impulse_debuff_res, key_dark_impulse_stun_res, key_dark_impulse_stress_res, key_dark_impulse_healing
- `trinket_catacombs_*` (3; epic:3): slime_cube[D2], slime_amulet[D2], statue_head[D2]
- `trinket_warlord_*` (3; epic:3): axe[D1], gauntlet[D1], helmet[D1]


Cultist trinkets (22, sub_type `cultist`, files in `trinkets_data_export`): `trinket_cultist_*` (e.g. `key_dark_impulse_*` x8 resistance variants, `cruel_intent`, `idle_thought`, `silent_treatment`, `hardened_heart`, `temptation` ...) are dropped by the Mountain/guardian cultists and use a distinct equip SFX (`ItemSubtype cultist`). `trinket_ancestors_coat`, `_mustache_cream`, `_pistol` (3, `ancestral`) drop from the Ancient Adversary statues (`TRINKETS_ANCESTOR_STATUE`). Boss trinkets per region (`TRINKETS_<CITY|FARM|FOREST|COAST|TUNDRA>_BOSS`): `trinket_city_boss_charred_litany`, `trinket_city_boss_smoldering_hymnal`, `trinket_coast_boss_carved_bodkin`, `trinket_coast_boss_sodden_sweater`, `trinket_farm_boss_ghastly_gruel`, `trinket_farm_boss_kitchen_knives`, `trinket_forest_boss_reverberating_redoubt`, `trinket_forest_boss_footmans_grog` (+ `trinket_boss_deaths_head_01..05`). Hero-class trinkets: `trinket_hero_<abbr>_*` (45). The only `TrinketSet` is the test set `test_cave_trinket_set`.

### 7.4 Other item ids

**Combat items** (`m_type,combat`; 85 elements: 55 generic in `item_data_export`, 9+9+1 Kingdoms quest items in `kingdom_quest_*`, 9 [D2] `catacombs_*` in `item_data_export_DLC2`, 1+1 [D1]; `m_usedInCombat,True`, `m_maxQty` stack 2 typical, `m_buyCostId`/`m_sellCostId` in `cost_data_export`):

`ablative_powders`, `adrenaline_tonic`, `antivenom`, `bandages`, `bear_trap`, `bone_saw`, `burn_salve`, `crows_feet`, `chalk_dust`, `clotting_powders`, `death_cap_spores`, `fire_bomb`, `fire_grenade`, `fishermans_net`, `healing_consumable_1`, `healing_consumable_2`, `holy_water`, `ichor_bomb`, `invigorating_intoxicant`, `laudanum`, `medicinal_herbs`, `neutralizing_powders`, `noisemaker`, `oil_flask`, `otherworldly_fragment`, `pouch_of_lye`, `pustule_salve`, `pyrotechnic_dazzler`, `rag`, `scrap_grenade`, `shimmering_powder`, `shred_of_decency`, `single_leech`, `smelling_salts`, `smoke_bomb`, `space_dust`, `spiked_ball`, `spring_water`, `stimulants`, `the_blood`, `thunderclap_grenade`, `torch_consumable`, `toxic_ichor`, `toxic_spores`, `trephine_bur`, `war_horn`, `makeshift_javelin`, `wilburs_flag`, `unnatural_pigment`, `beastmen_bell`, `beastmen_horn`, `beastmen_hatchet`, `signal_flare_1`, `signal_flare_2`, `signal_flare_3`, `quest_beastmen_medicated_meat`, `quest_courtier_accelerant`, `quest_courtier_syringe`, `courtier_blood_default`, `courtier_blood_human_infected`, `courtier_blood_creature_infected`, `courtier_blood_eldrich_infected`, `courtier_cure`, `courtier_proboscis`, `courtier_wing`, `coven_mandrake_root`, `coven_mushroom_item`, `coven_rook_skull`, `quest_coven_scrying_dust`, `quest_coven_finger_bomb`, `quest_coven_witchbane_dagger_01`, `quest_coven_witchbane_dagger_02`, `quest_coven_witchbane_dagger_03`, `quest_coven_witchbane_dagger_04`, `catacombs_black_light_lantern`[D2], `catacombs_salt`[D2], `catacombs_cat_food`[D2], `catacombs_buckler`[D2], `catacombs_chain`[D2], `catacombs_stratagem_offense`[D2], `catacombs_stratagem_defense`[D2], `catacombs_coin_purse`[D2], `catacombs_geode`[D2], `quest_tattered_banner`[D1], `ear_necklace`[D1]

**Inn / rest items** (`m_type,rest`; 72 elements, `m_usedInInn,True`; sub_type `hero` = 15 hero-specific items, otherwise food/drink/books/tools used in the inn or camp):

- generic: `armor_repair_kit`, `blasphemous_idol`, `book_bawdy_tales`, `book_desert`, `book_insults`, `book_poetry`, `boxing_gloves`, `calming_incense`, `candles_and_chocolate`, `clarifying_poultice`, `clotting_poultice`, `dart_board`, `drum`, `food_1`, `food_2`, `food_3`, `food_4`, `food_5`, `tinned_preserves`, `holy_beads`, `impermeable_poultice`, `medicinal_leeches`, `meditative_totem`, `mop`, `nightshade_concoction`, `orbitoclast`, `pipeweed`, `playing_cards`, `restorative_herbs`, `roast_pig`, `songbook_amorous`, `songbook_morose`, `songbook_rousing`, `soothing_poultice`, `speed_bag`, `stew`, `stimulating_poultice`, `stitching_kit`, `the_wine`, `tug_rope`, `wax_innoculant`, `whetstone`, `whiskey_barrel`, `whiskey_bottle`, `whiskey_flask`, `whittling_tools`, `wild_tea`, `quest_beastmen_survivors_tale`, `courtier_wig`, `courtier_cravat`, `coven_food`, `coven_herb_bundle`, `beastmen_blood`[D2], `beastmen_flesh`[D2], `catacombs_underground_orchid`[D2]
- hero-specific (`sub_type,hero`): `oddly_tuned_lute`, `morbid_joke`, `improvised_strategy`, `experimental_remedy`, `precious_collection`, `tar_filled_colambre`, `guided_meditation`, `the_very_best`, `war_paint`, `holy_hymnal`, `pain_box`, `bundle_of_contracts`, `abm_diary`[D2], `unique_reliquary`[D1], `blade_oil`[D1]

**Stagecoach upgrades** (`m_type,stage_coach_upgrade`; 118 elements; slot `m_slot`: General 60, Pet 25, Trophy 18, Flame 15; files `stagecoach_upgrades_export`, `_hardcoded_export`, `_DLC1`, `_DLC2`, `kingdom_override`, `kingdom_quest_*`):

- `common` (21): `storage_trunk`, `food_barrels`, `icebox`, `whiskey_still`, `strongbox`, `collectors_chandelier`, `worktable_loom`, `medicine_chest`, `bottle_case`, `compress_kit`, `mortar_and_pestle`, `leaf_suspension`, `iron_brazier`, `iron_banded_wheels`, `steel_plating`, `crows_nest`, `shrine_map`, `book_of_hoarders_signals`, `trackers_map`, `explosives_magazine`, `blueprint_tubes`
- `rare` (39): `quest_beastmen_spyglass`, `quest_beastmen_ambulance_designation`, `quest_beastmen_cold_storage`, `quest_beastmen_rotting_dead_01`, `quest_beastmen_rotting_dead_02`, `quest_beastmen_rotting_dead_03`, `quest_beastmen_rotting_dead_04`, `quest_courtier_fuel_requisition`, `quest_courtier_public_health_notice`, `quest_courtier_pharmacy`, `quest_coven_witchfinder_map`, `super_storage_trunk`, `guidebook_city`, `guidebook_coast`, `guidebook_farm`, `guidebook_forest`, `guidebook_tundra`, `stew_pot`, `griddle`, `windchimes`, `tea_service`, `trinket_organizer`, `merchants_guild_seal`, `medical_equipment`, `chirurgeons_table`, `alchemical_gear`, `chirurgeons_mixing_kit`, `wagon_jack`, `carriage_lamps`, `telescope`, `academics_map`, `chirurgeons_map`, `tinkers_bench`, `trapmakers_kit`, `assay_gear`, `super_strongbox`, `catacombs_salt_barrel`[D2], `catacombs_moonlight_lantern`[D2], `quest_sanctified_scroll`[D1]
- `infernal` (11): `infernal_torch`, `infernal_torch_2`, `infernal_torch_killing_blow`, `infernal_torch_3`, `infernal_torch_combo`, `infernal_torch_4`, `infernal_torch_boss`, `infernal_torch_5`, `infernal_torch_test`, `infernal_torch_shops`, `infernal_torch_random`
- `radiant` (4): `radiant_torch_L1`, `radiant_torch_L2`, `radiant_torch_L3`, `radiant_torch_L4`
- `trophy` (18): `quest_beastmen_alpha_head`, `trophy_coastal_boss_leviathan`, `trophy_coastal_boss_leviathan_b`, `trophy_coastal_boss_leviathan_c`, `trophy_cultist_boss_exemplar`, `trophy_cultist_boss_exemplar_b`, `trophy_cultist_boss_exemplar_c`, `trophy_fanatic_librarian`, `trophy_fanatic_librarian_b`, `trophy_fanatic_librarian_c`, `trophy_plague_eater_harvest_table`, `trophy_plague_eater_harvest_table_b`, `trophy_plague_eater_harvest_table_c`, `trophy_lost_battalion_dreaming_general`, `trophy_lost_battalion_dreaming_general_b`, `trophy_lost_battalion_dreaming_general_c`, `quest_trophy_crusader_helmet_a`[D1], `quest_trophy_crusader_helmet_b`[D1]
- `pet` (14): `quest_coven_boiled_head`, `quest_coven_gagged_boiled_head`, `quest_coven_tortured_familiar`, `pet_wolf`, `pet_snake`, `pet_owl`, `pet_rabbit`, `pet_slime`, `pet_shrieker`, `pet_carrion_eater`, `pet_croc`, `pet_tick`, `pet_shambler`, `catacombs_pet_cat`[D2]

**Memories** (`m_type,memory`, `memory_data_export`; 20): `memory_01` ... `memory_20` (each carries `ActorDataExternalBuffs` `memory_buff_NN` and a `Cost` of candles 5 (`memory_NN_buy_cost`); equipped in the 5 hero memory slots; loot tables `MEMORIES_TABLE`, `_2.._5` gated by `memory_1/3/5/7` unlocks).

**Currency items** (109): 17 real currencies + 92 `deliverable` (quest/inn-bonus objects handed in at an inn, 1:1 with `InnBonus` rows; e.g. `spider_gland`, `worm_slime`, `dog_teeth`, `hero_bones_01..03`, `<region>_rumour_<node>` x7 nodes x5 regions + cave/catacombs, `locked_strongbox`, `physicians_guild_seal`, `puzzling_trapezohedron`, `quest_*`). Non-deliverable currencies:

`default_item`, `candles`, `gold`, `city_books`, `farm_spoons`, `forest_medals`, `cave_dirt`, `coast_starfish`, `valley_baubles`, `tundra_baubles`, `materials`, `quest_beastmen_pelt`, `quest_courtier_fuel_reserves`, `quest_courtier_research_journal`, `quest_coven_academics_keyring`, `quest_coven_witch_buck`, `quest_rumour_of_riches`[D1]


### 7.5 Quirks and diseases

`Quirk` elements: **230** = 116 `quirk_*_pos` + 84 `quirk_*_neg` + 26 `disease_*` + 4 `crimson_curse_*` (Kingdoms courtier). Files: `quirk_data_export` (224), `kingdom_quest_courtier_data_quirks` (4), `dlc_catacombs/catacombs_faction_export` (2: `disease_the_goops`, `disease_the_goops_severe`). Keys: `m_Rarity` (`NORMAL` 133 / `RARE` 97), `m_Tags` (`positive`/`negative`/`disease`, start-pool tags `pos_start`/`neg_start`, inn-pool tags `pos_inn_normal|rare`, `neg_inn_normal|rare`, item-cure tags such as `stitching_kit`, `boxing_gloves`, `beads`, `whiskey_barrel`, curio tags `curio_*`, mechanic tags `meltdown`, `resolute`, `obsession`, `story_exert_cost_quirk`), `generation_all_conditions` (e.g. `performer_is_not_flagellant`), `m_ShowDescription*`, `m_DurationType/Amount` + `m_EscalationTags` (temporary quirks); effects live in same-id `Buff`/`ActorDataStats`/`ActorDataEffects` elements (`quirk_act_out_data_export` holds act-outs). Container rules: `roster_quirk_container` = `m_TagGenerations,pos_start,1,neg_start,1`, `m_TagLimits,positive,3,negative,3,disease,1`; `leper_quirk_container` excludes `disease_leprosy`; story container `leper_hero_story_quirk_container`. Costs at the Altar/inn: `hero` Rules `m_CureQuirkByTagCosts,disease,12`, `m_RemoveQuirkByTagCosts,negative,16,positive,16`, `m_LockQuirkByTagCosts,positive,32`.

Disease ids (26): `disease_bad_humours, disease_algal_bloom, disease_botulism, disease_brittle_bones, disease_bloody_flux, disease_cholera, disease_creeping_cough, disease_diphtheria, disease_dysentery, disease_fainting_spells, disease_hemorragic_fever, disease_periodic_paralysis, disease_rabies, disease_sepsis, disease_smallpox, disease_syphilis, disease_tarantism, disease_the_runs, disease_typhoid, disease_wasting_disease, disease_worries, disease_leprosy, disease_oozes, disease_hemorrhoids, disease_the_goops, disease_the_goops_severe`. Positive/negative ids all follow `quirk_<name>_pos|neg` (e.g. `quirk_calm_pos`, `quirk_cowardice_neg`, `quirk_kleptomania_neg`); full list: `Quirk` elements of `quirk_data_export`.

### 7.6 Loot table structure (`LootTable`, 1,014 elements)

Files (`EXCEL/`): `loot_data_export_COMBATS` (220), `_MISC` (244), `_INN_REWARDS` (131), `_CURRENCIES` (87), `_NODESandROAD` (84), `_TRINKETS` (62), `_SUPPLIES` (46), `_STAGECOACH` (41), `memory_loot_table_export` (5), `loot_data_export_DLC1` (31) [D1], `loot_data_export_DLC2` (63) [D2]. Rows: `m_chances` (weights), `m_qtys` (int or range `[1-2]`, negatives allowed, e.g. `-3` doom), `m_ids`, `m_types`, `m_tags` (e.g. `RELICS_QTY`, `BAUBLES_QTY`, `HERO_POINT_QTY`, `TORCH_QTY` - picked up by stat multipliers), `m_conditions` (as in battle tables), optional `m_unlockId`.

Option types (`m_types`, counts): `item` 1,806; `sub_table` 1,087 (roll another table); `all_sub_table` 223 (grant **all** listed sub-tables); `unique_sub_table` 39; `exclusive_sub_table` 11; `nothing` 69; `provision` 48 (run value: torch, doom, armor, wheels, hero_upgrade_points); `quest_step` 25; `biome_reward` 22 (Kingdoms region reward).

Example chain (`lair_boss_rewards_all`): `all_sub_table` w/ `is_confessions` -> `confessions_lair_boss_rewards` = 13 sub_tables, each granted: `DOOM_MINUS_THREE, HERO_POINT, TORCH_SOME, RELICS_LOT (qty 1-2), BAUBLES_LOT (1-2), ALL_COMBAT_ITEMS, ALL_REST_ITEMS, SC_UPGRADES_ALL, lair_boss_rewards_bonus, lair_boss_rewards_trinket_general, lair_boss_rewards_trinket_faction, lair_boss_rewards_trinket_boss, SC_TROPHY`; `RELICS_LOT` = `m_qtys,12`, `m_ids,gold`, `m_types,item`, `m_tags,RELICS_QTY`; `BAUBLES_LOT` = 12 of the region's faction currency chosen by `city_biome_check`/... conditions. Kingdoms variant `kingdoms_lair_boss_rewards` swaps `DOOM_MINUS_THREE` for `CONSTRUCTION_CURRENCY_10` and adds `biome_reward`.

Other families by id prefix: `confessions_road_*`/`kingdoms_road_*` (road combats), `*_resistance_*` (story fights), `kill_contract_*`, `guardian_cultist_*`, `camp_ambush_*`, `lair_prewave_1/2_rewards_all`, `lair_boss_*`, `hoarder_shop_*` (9: shop stock of the Hoarder = `Store` node), `hospital_shop_*` (14: Field Hospital stock), `inn_default_*`, `inn_<variant>_*` (one table set per inn variant), `inn_kingdom_*`, `trinkets_<region>_*`, `sc_*`/`pet_*` (stagecoach), `skiploot_quest_*` (48 quest-step overrides), `quest_<gang>_*`, `biome_goal_reward_*`, `repeatable_items_*` (altar repeat purchases), `MEMORIES_TABLE*`.

## 8. Economy and meta tables (DD1 hamlet-like functions)

DD2 has no hamlet; these tables are the closest analogues. "DD1 analogue" notes are my reading, not game data.

### 8.1 Altar of Hope (profile/meta progression; DD1 analogue: Abbey/Blacksmith/Guild upgrades + Heirloom-style meta spend) 

- `altar_of_hope_rules_data_export` -> `Rules` id `altar_of_hope`: `m_MemoryLootTableIds` (`MEMORIES_TABLE`, `MEMORIES_TABLE`, `_2`, `_3`, `_4`, `_5` - gated by `memory_1/3/5/7`), `m_SkipIntroProfileValueType,candles` / `m_SkipIntroProfileValue,12`, `m_MaxRerollInventory,3`, five `repeatable_item` rows (`trinket` -> `repeatable_items_trinkets` with costs 16/24/32 candles; `combat` -> `repeatable_items_combat` 4/6/8/12/16/20/24; `stage_coach_upgrade` -> `repeatable_items_stagecoach` 16/24/32; `rest` -> `repeatable_items_rest` 4-24; `memory_reroll` 4/8/16/32). All costs are `Cost` elements with `m_ProfileValueType,candles`.
- Permanent upgrades = `UnlockTrack` + `Unlock` + `Cost` triples in `progression_data_export` (+DLC): tracks `stagecoach` (13 unlocks; slots, wheels, candles chance), `memory` (8), `resources` (13; effects attached in `node_effects_data_export`: `resources_4` -> `hoarder_boost_1`, `_5` -> `cache_boost_1`, `_7` -> `hospital_boost_1/2`, `_8` -> `inn_boost_1`, `_1/_6` -> `allowance_boost_*`), `prestige` (10), `pets` (10), `infernal_flame` (9; costs 10, 40, ... candles; torch tiers `infernal_torch..infernal_torch_5`, `radiant_torch_L1..L4`), and one track per hero class. `unlock_tables_data_export` holds the tiered item pools unlocked at the Altar: `unlock_table_trinkets_tier_1..3`, `unlock_table_stagecoach_items_tier_1..3`, `unlock_table_combat_items_tier_1..3`, `unlock_table_inn_items_tier_1..3`, `unlock_table_cosmetics_<class>` (26 `UnlockTable` total).
- Presentation: node type `AltarOfHope`, scene `altar_of_hope`, bundle `altar_of_hope_assets_basegame`, `ui_assets_altar_sub_screen_options_panel`, narration `narration_*_altar_of_hope`.

### 8.2 Inn (Expedition: `inn_data_export`, `inn_bonus_data_export`, `inn_tables_data_export`, `inn_data_export_hard_coded`; DD1 analogue: Tavern/Abbey/Sanitarium in one stop)

- `Inn` elements (29 = 25 Expedition + 4 Kingdoms): Expedition ids `default, valley_intro, valley, goodquirk, badquirk, quirkcentral, superheal, stressheal, trinketshop, scupgradeshop, supplyshop, antipillager, antigaunt, anticultist, scoutbuff, helpscouted, mysteryscouted, fightscouted, stressfulrest, freestuff, thegoodplace, thebadplace, upgradecity, critcity, behealedchild`; Kingdoms `kingdom_camp`, `kingdom_camp_fast_travel`, `kingdom_inn`, `kingdom_inn_fast_travel`.
- `InnTable` (`default` = all 23 variants with weight 1, `valley_intro`, `valley`) - which variant a region offers is `Biome.m_InnTable`.
- `Inn` keys: `m_InnFeatureTypes` (`trainer`, `wainwright`, `actor_path_change`, `stage_coach_repair`, `stage_coach_change_skin`; Kingdoms also `physician`, `fast_travel`, `remove_disease`), `m_StoreLootTableIds` (6 stock tables per inn: combat items, food, inn items, trinkets, stagecoach trinkets, stagecoach items - `inn_default_*`, `inn_<variant>_*`), `m_BonusLootTableIds`, `m_NumberOfBiomeChoices` (2 = next-region choices), `m_QuirkGenerationChance/Amount/Tags` (quirks rolled at the inn), `hero_effects` / `kingdom_hero_effects`, `actorless_effects` (e.g. `torch_heal_100`), `run_value_transactions`, `m_ActorDataPathChangeCostId`, `m_HealthHealCostId`, `m_WoundHealCostId`.
- `InnBonus` (29): deliverables that pay out when handed in at an inn (`spider_gland`, `spider_appendage`, `worm_slime`, `dog_teeth`, `worm_secretion`, `dog_blood`, `locked_strongbox`, `hero_bones_01..03(_valley)`, `puzzling_trapezohedron`, `physicians_guild_seal`, `quest_*`), each with `m_BonusLootTableIds` (usually `HERO_POINT`) and optional `hero_effects` (stress heal, remove disease/quirk) or `m_InnRunDataStatsIds` (discounts).
- Hard-coded effects (`inn_data_export_hard_coded`): `inn_weak_heal` (heal up to 25%), `inn_standard_heal` (50%), `inn_full_heal`; relationship buffs `first_inn_*`, `the_good_place_*`, `the_bad_place_*`; discounts `inn_trinket_discount`, `inn_sc_upgrade_discount` (store cost x0.5).
- Items used at the inn = `rest` items (section 7); Wainwright = repair armor/wheels (`RunValueTransaction`, `run_value_transaction_data_export`, `kingdom_run_value_transaction_data_export`: Kingdoms repair: 12 faction baubles per armor/wheel point, discounted variant 2 points for 1 bauble); Trainer = path change (`actor_path_change`, `kingdom_inn_change_actor_data_path` cost 24 relics, discounts 24/16).

### 8.3 Field Hospital, Hoarder, Academic's Cache, Gaunt Chirurgeon (road nodes)

Node types (decomp `NodeType`) with display names from `LOC actor_stat_type_map_generation_node_spawn_multiplier_*`: `Store` = **Hoarder**, `Hospital` = **Field Hospital**, `Cache` = **Academic's Cache**, `StoryCosmic` = **Academic's Study**, `StoryAssist` = **Assistance Encounters**, `StoryResist` = **Resistance Encounters**, `StoryCultist` = **Cultist Encounters**, `WatchTower`, `Oasis`, `CreatureDen`, `GauntChirurgeon`, `Dungeon`, `Guardian`, `Inn`. Their loot/stock: `hoarder_shop_*` (9), `hospital_shop_*` (14: `hospital_shop_combat01..05`, `_inn01..03`, `_stagecoach01`, `_trinket01`, `_confessions`, `_kingdoms`, `_kingdoms_top_items`), `supply_cache_*` (4), `trinkets_curio_*`/`curio_*` (curio choices: `curio_choice_export`: 188 `StoryChoice` (tag `Cosmic`) + 149 `StoryDataEffects`, draw tags = curio names: `broken_clock`, `caged_creature`, `faceless_facsimile`, `familiar_desk`, `formless_sculpture`, `hovering_polyhedra`, `memories_of_a_dream`, `scary_song`, `sealed_doorway`, `shamblers_altar`, `thing_in_corner`, `thought_experiment`, `timeworn_volumes`, `unsettling_portrait`, `vintage_collection`). Sell only at Hoarder (`run` Rules `m_SellExecutingNodeTypes,Store`). Node spawn weights are `RunDataStats` `map_generation_node_spawn_multiplier`/`scout_node_chance` sub-stats (`run_levels_data_export`, Altar `resources_*` unlocks). Buy/sell prices: `Cost` elements (`cost_data_export`, `<item>_buy_cost`, `trinket_distant_sell_cost`, `combat_item_sell_cost`).

Story node data: `story_choice_export_*` (800 `StoryChoice`; `assist` 35 KB, `resist` 102 KB, `creature_den`, `gauntchirurgeon`, `herostory`, `oasis`, `valley`, `alignments`; alignments `INSPIRE, FEED, DONATE, EXERT, STEAL, EXTORT, SLAY, AVOID, FIGHT, CHARGE, TACTICS, AMBUSH, WITHDRAW, FLEE, INVESTIGATE, INTERFERE, COMPULSIVE, BASIC_HELP, TEST, TENTATIVE`), `story_rules`, `StoryDataEffects` (417).

### 8.4 Stagecoach and run economy

`stage_coach_rules_data_export` (`m_GeneralNumberOfSlots,1`, `m_TrophyNumberOfSlots,1`, `m_PetNumberOfSlots,0`, unlocks `stagecoach_2/8/12`, `pet_1`), `stagecoach_upgrades_export*` (118 items, section 7), `stagecoach_skin_data_export` (16 `StageCoachSkin`: `wagon_skin_base, filth, stress, stripeyblack, checkered, blood, skull, flames, moss, barnacles, ironclad, oblivion` + [D3] `beastmen, courtiers, coven, slime`), `infernal_flame_construction_export` (torch tiers: 93 buffs), `run_rules` (`m_StartingGold,40`, `m_SkipValleyRunValues,torch,100`), `run_levels_data_export` (`base` RunDataStats: `retreat_chance 0.66`, scout chances, `torch_drain_between_nodes 6`, torch 0-100, stagecoach armor/wheels 0-2, node scout chance 10 for Inn/Guardian/Dungeon), `run_value_level_data_export`, `driving_rules`, `score_rules`, `run_goal_*` (281 `RunGoal`, per hero `ActorDataRunGoals`, 3 difficulty categories `easy/medium/hard` after `tutorial`).

### 8.5 Kingdoms mode (the only mode with a persistent "estate"-like layer: inns/camps with upgrades, siege, escalation, wounds)

| table file | defines |
|---|---|
| `kingdom_rules_data_export` | `Rules kingdom`: base run stats, `m_InnInitialFreeUpgradeCounts,2,2,1,1`, siege counter 100, siege strength 3-10, `m_SiegeManualBattleConfigTableId,gang_mashes_siege_master`, `m_CampAmbushBattleConfigTableId,camp_mashes_master`, boss activation conditions per gang, `m_StartingGold,0`, `m_FirstKingdomDefaultPartyActorClassIds,plague_doctor,grave_robber,highwayman,man_at_arms`, siege loss effects (`stress_damage_2`, `wound_damage_lost_inn`, `remove_1_quirk_positive_25pct`) |
| `kingdom_difficulty_data_export` | 4 `KingdomDifficulty`: `radiant`, `normal` (default), `stygian`, `blood_moon`: `loss_day` 72/60/50/45, inns destroyed 60/50/40/30%, roster limit 12, `escalation_2_day` 15/15/10/8, `escalation_3_day` 33/33/28/25, free treasures 2/2/1/1 |
| `kingdom_map_data_export` | 6 `KingdomMap` grids (`kingdom_map_01/02/04/05/06/07`): cells `biome+<Region>`, `biome+Boss`, `inn+kingdom_inn`, `inn+kingdom_inn_fast_travel`, `camp`, `boss` |
| `kingdom_inn_data_export` (+ `_hard_coded`), `kingdom_inn_upgrade_data_export` | `Inn` rows (section 8.2) and **68 `InnUpgrade`** (`m_InnUpgradeCategory`: `defense` 13, `provisioner` 14, `wainwright` 10, `trainer` 9, `physician` 13, `kingdom_camp` 7, `kingdom_inn` 2; types `minor`/`major`/`ultimate`, `m_InnLevel` 1-3, `prerequisite_all_inn_upgrades`, cost `Cost` in `materials`, e.g. `inn_upgrade_physician_disease_cure` = 10 materials). Defense upgrades spawn `peasant_militia_*` (`KingdomSiegeDefense` `inn_upgrade_defense_peasant/militia/veteran`) |
| `kingdom_siege_attack/defense`, `kingdom_escalation_levels`, `kingdom_event` (131 `KingdomEvent`), `kingdom_event_hardcoded` | siege strength/delay/accrual, `light`/`heavy`/`destroy` attacks (escalation 1/2/3), random events (`e_spawn_sieges`, `e_beastmen_buff`, `e_coven_buff`, `e_extra_wheel_damage`, `e_escalation_2/3` ...) |
| `kingdom_treasure`, `kingdom_wound_triggers`, `kingdom_torch_levels(_group)`, `kingdom_hero_stat_upgrade*`, `kingdom_hero_skill_sets*`, `kingdom_affinity_*`, `kingdom_cost`, `kingdom_gang`, `kingdom_quest_*`, `kingdom_biome_modifier/upgrade`, `kingdom_battle_*`, `kingdom_misc`, `kingdom_temp`, `kingdom_quirk_temp`, `kingdom_run_value_transaction` | treasures (`treasure_1..3`, `siege`), wounds (`inn_start_very_small..large` day-based chances, `deaths_door`, `overstress_*`: wound percent changes; inns remove 10% (`kingdom_inn_standard_wound_percent_heal`), camps 5% (`kingdom_camp_standard_wound_heal`)), the rest as named |
| `kingdom/kingdom_override_data_export` | Kingdoms replacements: Valley story choices (`valley_supply_01`, `valley_trinket_01`, `valley_stagecoach_item_01`), pets (`pet_*` re-declared), `nightshade_concoction`, `stitching_kit`, ~50 buffs |

Kingdoms resources: `materials` (inn upgrades), faction baubles (repairs, `m_ItemTag,faction`), relics `gold` (start 0), quest currencies (section 7), torch/doom analogue `escalation` run value (1-3).

## 9. Localization

Root: `LOC` = `...\StreamingAssets\Localization\`.

| path | what |
|---|---|
| `fallback_language.txt` | content `en` |
| `Sources/*.txt` (+ `Sources/dlc_catacombs/`, `Sources/dlc_dul_cru/`, `Sources/game_type_override_expedition/`, `Sources/game_type_override_kingdom/`) | **English**, the editable source of truth: 19,034 `key=value` lines in 128 files (78 in `Sources/`, 50 in the four subfolders) (largest: `subtitles.txt` 1,114, `GENERATED_barks_items.txt` 974, `GSHEET_quirks.txt` 910, `combat.txt` 899, `GSHEET_barks_traveling_node_exit.txt` 770, `GENERATED_barks_act_outs.txt` 653, `kingdoms_misc.txt` 633, `hero_skill_names.txt` 614, `interface.txt` 584, `enemy_skill_names.txt` 556) |
| `Poedit/<iso>.po` (+ same subfolders; `iron_crown.pot` = template, 20,136 entries) | foreign translations (gettext): `cs, de_DE, es, es_lat, fr, it, ja, ko, pl, pt_BR, ru, tw_CN, uk, zh_CN` = **14 foreign languages + English = 15** |
| `Binary/<iso>.mo` | compiled `.mo` for only `cs, de_DE, es_lat, fr, it, pl, pt_BR, ru` (8); `LanguageDefinition.HasValidTranslation` prefers `.mo`, else the `.po`; text is actually loaded from `.po` (`ForeignLocalizationData`) |
| `BuildPotFile/`, `Poedit/*.meta.private.*` | tooling (`BuildPotFile.exe` regenerates `iron_crown.pot` from `Sources`, `msgfmt.exe`) |

**Russian is present**: `Poedit/ru.po` (5.6 MB; header `Language: ru`, `PO-Revision-Date: 2026-04-24`), `Poedit/dlc_dul_cru/ru.po`, `Poedit/dlc_catacombs/ru.po`, `Poedit/game_type_override_kingdom/ru.po`, `Binary/ru.mo` (1.4 MB). `game_type_override_expedition` has no `ru.po` (only `ko.po`). Coverage (my heuristic parse of `ru.po`: 24,699 entries, 12,889 with a non-empty non-fuzzy `msgstr`, 11,804 flagged `#, fuzzy`, 15,929 distinct keys vs 20,136 in the `.pot`): the Russian file is **partial** and many entries are machine-"fuzzy"; the loader (`PoFile.cs`) does **not** skip fuzzy entries (any non-empty `msgstr` is used) and, for keys with no foreign entry, falls back to English (fallback behaviour not verified in code).

**English file format** (`LocalizationSourceParser`): UTF-8 (many files start with a BOM), one entry per line `key=value` (split at the **first** `=`; key non-empty, value non-empty), lines whose first non-blank char is `#` are comments, blank lines ignored, `\n` literal in values = newline; only `*.txt` directly in the folder (`SearchOption.TopDirectoryOnly`).

**`.po` format** (`PoFile.cs`): standard gettext. `msgctxt "<key>"` is the **key** (text after an `@` in `msgctxt` is trimmed - used to disambiguate), `msgid "<English text>"`, `msgstr "<translation>"`; multi-line strings are concatenated quoted lines.

**Load priority** (`LoadedStrings.GetActiveEntryForLocKey`: the entry with the highest `m_priority` wins): `Sources/` root = 0, `Sources/<dlc>/` = 1, `Sources/game_type_override_<expedition|kingdom>/` = 2 (added on `EventGameTypeStarted`), **mod `Localization/` folders = 3** (`Localization.AddLoadPath(path, 3)`). So a mod can override any base string by redefining its key.

**Key naming scheme** (prefix counts over the 19,034 English keys: `bark` 3,875; `story` 2,013 (`story_bark_*` 1,888); `skill` 1,914 (`skill_name_*` 1,690); `vo` 1,319 (`vo_<context>_...` subtitles); `item` 1,030; `effect` 939; `quirk` 841; `token` 606; `affinity` 601; `inn` 449; `party` 363; `hero` 358; `actor` 348; `buff` 345; `tutorial` 328; `e` 315 (Kingdoms events); `upgrade` 267; `achievement` 242; `biome` 191; `kingdom` 157; `loot` 131; `run` 124; `herostory` 122; `battle` 84):

| key pattern | value |
|---|---|
| `<actor_id>` , `<actor_id>_corpse` | enemy/hero display name (`fanatic_whipper=Whipper`, `coastal_boss_leviathan=Leviathan`; heroes `hellion=Hellion`, `hero_select_<class>`, `hero_name_canonical_<class>`) |
| `quirk_<name>_pos` / `quirk_<name>_neg`, `disease_<name>` (+ `+female` variants) | quirk/disease names (files `GSHEET_quirks.txt`) |
| `item_name_<id>`, `item_description_<id>`, `item_quote_<id>`, `item_type_<type>` | items (653 names) |
| `token_<id>` (icon markup), `token_name_<id>`, `token_<id>_description` | tokens |
| `skill_name_<skill_id>`, `skill_desc_*`, `buff_desc_*`, `effect_tooltip_*` | skills/effects |
| `hero_path_name_<path>` (+ `+male` / `+female`), `buff_desc_path_descriptor_<path>_override` | paths |
| `biome_name_<BiomeId>`, `biome_enemy_faction_<BiomeId>`, `biome_mutator_<modifier_id>`, `road_indicator_*` | regions |
| `boss_defeated_prompt_<boss>`, `vo_*`, `GSHEET_barks_bosses` | bosses/narration |
| `upgrade_track_<track>_<n>_title`, `unlock_<id>_override`, `inn_upgrade_*`, `inn_bonus_*` | progression/inn |
| `party_name_<class1>_<class2>_<class3>_<class4>` | funny party names (360) |
| `bark_*`, `story_bark_<kind>_<n>+<hero>` | hero barks (gendered/hero-suffixed with `+`) |
| `run_goal_*`, `loot_screen_*`, `options_menu_*`, `actor_stat_type_*`, `e_<event>_*` (Kingdoms), `kingdom_*` | UI/system |

Markup conventions inside values: `{q}` = `"`; `#{name}` = colour token from `colors.txt` (e.g. `<color=#{buff}>`, `#{notable}`, `#{debuff}`, `#{crimson}`); `<sprite={q}tmp_x{q} name={q}y{q}>` = TextMeshPro inline sprite; `<i>`, `<sup>`, `<size=90%>`.

**How the official mod adds strings** (`MODEX/Localization/dd2_mod_strings_token.txt`, 348 bytes, the whole file):

```
#EXAMPLE
token_test_token_creation=<sprite={q}tmp_test_token_creation{q} name={q}test_token_creation{q}>
token_name_test_token_creation=<color=#{debuff}>test_token_creation</color>
token_test_token_creation_description=Example Token Description

item_name_bandages=Bandages <sprite={q}tmp_test_token_creation{q} name={q}test_token_creation{q}>
```

i.e. plain `key=value` text in `<mod>/Localization/*.txt`; new keys (`token_*` for the new token id `test_token_creation`) plus an **override** of an existing key (`item_name_bandages`). The inline `<sprite>` refers to the TMP sprite asset shipped in the mod's bundle (section 10). English only: for another language the loader reads `<iso>.po` from the same `Localization/` folder (`ForeignLocalizationData.LoadStringsAtPath` -> `Path.Combine(loadPath, iso + ".po")`), so a Russian mod string needs `<mod>/Localization/ru.po` with `msgctxt "<key>"` / `msgid` / `msgstr` blocks (not verified end-to-end in game).

## 10. The official mod example (`MODEX` = `StreamingAssets\mods\test_token_creation_export\`)

### 10.1 File tree (everything in the folder; sizes in bytes)

```
test_token_creation_export/
  manifest.json                         78      mod metadata (all fields empty in the example)
  manifest.json.meta                    158     Unity importer stub (TextScriptImporter) - not used by the game
  mod_icon.png                          24310   shown in the in-game mod list (ModMgr.MOD_ICON_NAME)
  mod_icon.png.meta                     2947    Unity importer stub
  dd2_mod_data_token.Group.csv          829     DATA: new Token + Effect + Buff elements (root-level csv = "Mod_Data")
  dd2_mod_data_token.Group.csv.meta     165
  Localization/                                  STRINGS (any *.txt, key=value)
    dd2_mod_strings_token.txt           348
    dd2_mod_strings_token.txt.meta      165
  Assets/                                        ASSETS (addressables content built with Unity)
    catalog.json                        5273    Addressables content catalog (keys, internal ids, resource types)
    catalog.jsondd                      5370    GENERATED by the game on load (copy of catalog.json with the {...nexusModPath} placeholder replaced by the real path)
    test_token_creation_assets_all.bundle  28847   the asset bundle: TMP sprite asset + ResourceToken + png + material
    test_token_creation_assets_all.bundle.meta 633
    catalog.json.meta                   158
  Assets.meta, Localization.meta, Overrides.meta, expedition.meta, kingdom.meta, dlc_*.meta  (172 each)
  Overrides/{dlc_catacombs, dlc_dul_cru, dlc_origin_skins, dlc_supporter}/   (empty; override-mode CSV drop zones)
  expedition/  kingdom/  dlc_catacombs/  dlc_dul_cru/  dlc_origin_skins/  dlc_supporter/   (empty; mode-/DLC-specific CSV drop zones)
```

The empty folders are the template of the loader's search paths (section 1.2): root `*.Group.csv`, `<dlc>/`, `<gametype>/`, `Overrides/`, `Overrides/<dlc>/`, `Overrides/<gametype>/`. `.meta` files are Unity artefacts (the example was exported from the Unity "Mod Tools" project; internal asset paths are `Assets/UserMods/test_token_creation/test_token_creation/...`) and can be omitted in a hand-built mod.

### 10.2 `manifest.json`

```json
{ "title": "", "description": "", "version": "", "game_mode": 0 }
```

`ModMgr.AssignModManifestValues` reads `title`, `description`, `version` (all three **must** be present: `GetValue("title").ToString()` is called without a null check) and the optional int `game_mode` -> `ModGameType`: `0 EXPEDITION_AND_KINGDOMS`, `1 EXPEDITION`, `2 KINGDOMS`. Non-empty values replace the folder name / description / version string shown in the list. Mods are discovered as **every sub-folder of `StreamingAssets/mods/`** (`VerifyModsAreLoaded` / `GetAllMods`; Workshop/Nexus entries are added through `AddMod` - not examined here), toggled on/off in the in-game mod screen (default off; state saved in a mod list), and sorted by `ModInfo.order` (higher first). Data mods only load when the main-menu "Mods" profile toggle is on (`SaveUtils.inMods`: separate mod profile/saves).

What each file type enables (`ModMgr.LoadModInfo`, bit flags `modType`): `Assets/*.json` (the first json = catalog) -> `Mod_Asset` (1); any file in `Localization/` -> `Mod_Loc` (4); any `*.csv` in the mod root -> `Mod_Data` (2); any `*.txt` in the mod root -> `Mod_EP` (8, parsed as `TextBasedEditorPrefs` additive settings). CSVs in sub-folders are picked up by `ResourceGroupCsvDatabase.GatherModResources` for every loaded mod.

### 10.3 `dd2_mod_data_token.Group.csv` - what it does (verbatim structure, 5 element kinds)

```
element_start,test_token_creation,Token                  <- NEW Token id; m_Tags,negative ; m_DurationType,performer_turn_end ; m_DurationAmount,3 ; m_Limit,1 ; m_ConsumeLimit,1 ...
element_start,add_1_test_token_creation,Effect           <- NEW Effect that adds 1 of that token (m_TokenAddId, m_TokenAddAmount,1, m_ShowValue,False)
element_start,test_token_creation,DataExternalBuffs      <- links an id to a list of Buff ids: "buffs,test_token_creation"
element_start,test_token_creation,Buff                   <- NEW Buff (m_DurationType,infinite)
element_start,test_token_creation,ActorDataEffects       <- hooks on that buff: "turn_end_effects,instant_kill_chance"
element_start,instant_kill_chance,Effect                 <- NEW Effect: m_Chance,0.5 ; m_IsKill,True ; m_IgnoreDeathClass,False
```

It shows the rule "a mod **adds** rows by declaring ids that do not exist yet": nothing is overridden (all ids are new, so no `Overrides/` needed); several classes share the id `test_token_creation` (Token, DataExternalBuffs, Buff, ActorDataEffects) exactly like base game files. Note some lines have no trailing comma (`buffs,test_token_creation`) - the parser does not need it. To append to an existing table (loot, battle) re-declare the table id (merge); to replace an existing element use `Overrides/`.

### 10.4 Assets - how the example ships art

`Assets/catalog.json` is a standard Unity Addressables catalog (`m_LocatorId: AddressablesMainContentCatalog`). Its `m_InternalIds` (4): `{Assets.Code.Mod.ModMgr.nexusModPath}\test_token_creation_assets_all.bundle` (placeholder replaced at load by the `Assets` folder path - this is what produces `catalog.jsondd`), and three asset paths `.../Sprite Assets/tmp_test_token_creation.asset` (`TMPro.TMP_SpriteAsset` - inline text sprite used by the `<sprite=...>` markup in the loc file), `.../test_token_creation.asset` (`Assets.Code.Token.ResourceToken`, class from the game assembly `IronCrown`), `.../test_token_creation.png` (`Texture2D`/`Sprite`; also a `Material`). Every entry carries the Addressables label **`Mod`**; `ModMgr.LoadModAddressables` calls `Addressables.LoadContentCatalogAsync(catalog)`, then for each game resource database (`ResourceDatabaseAddressable<,>` subclasses) does `locator.Locate("Mod", resourceType)` and registers the located assets under their asset name (replacing a base handle of the same name: log "we are replacing the handle with a new handle"), optionally calling the database's `AssignModdedOverrides`. Audio: `AudioBankUtils.GetModBankNames` + `PopulateModBankList` (FMOD banks in the mod folder). Unity must build the bundle with the game's own Addressables settings and script assembly (the example needs `IronCrown` types such as `ResourceToken`); asset-less data mods need only CSV + loc.

Summary of the three channels: **data** = `*.Group.csv` (root / `<dlc>` / `<gametype>` / `Overrides/...`), **text** = `Localization/*.txt` (priority 3, `<iso>.po` for non-English), **assets** = `Assets/catalog.json` + `.bundle` (label `Mod`, keys = asset names).

## 11. Addressables (`AA` = `StreamingAssets\aa\`)

Layout: `catalog.json` (13.1 MB; 32,079 internal ids = 810 bundles + 31,269 asset entries; 63,779 keys), `settings.json`, `AddressablesLink/link.xml`, and **810 `.bundle` files, ~4.29 GB** (721 in `aa/` directly - the layout the task described as `StandaloneWindows64\*.bundle` does **not** exist; 89 in DLC folders `aa/dlc_*/`). No `.json` data is stored in bundles' names; bundle names in the catalog carry an MD5 suffix (`..._<32 hex>.bundle`), the files on disk do not. Asset entries are Unity project paths (`Assets/Scenes/Combat/city/combat_arena_city_faction.unity`, `Assets/Data/Characters/Enemies/fanatic_whipper/...`, `Assets/Data/Biome/City/Prefabs/background_properties_city_arena_dungeon_exterior.prefab`); the key used from code is mostly the **asset name** (`<actor_id>_art_prefab`, scene name, `<class>_portraits`, `<class>_skill_icons`); 127 of the 163 enemy actor ids have an exact `<id>_art_prefab` key (exceptions: `cave_swine_*` -> `swine_*`, Mountain boss parts use family keys such as `boss_arms`, `boss_lungs_forward_rank`/`_rear_rank`, `boss_body`, collector heads, `shared_warlord`/`crusader_warlord` [D1], `coven_matres_spider`).

### 11.1 Combat arenas = dungeon / lair backdrops (one scene bundle per arena: `scenes_scenes_combat_arena_<biome>_<kind>`)

Backdrop pattern: `combat_arena_<region>_<kind>` where kind ~ combat source. `m_BackgroundSceneOverride` in a `BattleConfiguration` picks one of these by exact scene name (53 distinct values are used explicitly in the data; everything else uses the default arena of the region + combat source). Matrix of existing arena scenes (`x` = scene exists; sizes 0.1-9 MB each, 122 base scene bundles = 432 MB):

| backdrop biome | faction | gaunt | pillager | military | resist | story_cultist | cultist | creature_den | dungeon_exterior | dungeon_interior | gaunt_chirurgeon | inn_defense | urgent_repairs |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| city | x | x | x | x | x | x | x | x | x | x | x | x | x |
| farm | x | x | x | x | x | x | x | x | x | x | x | x | x |
| forest | x | x | x | x | x | x | x | x | x | x | x | x | x |
| coast | x | x | x | x | x | x | x | x | x | x | x | x | x |
| tundra | x | x | x | x | x | x | x | x | x | x | x | x | x |
| caves | x | x |  |  | x | x | x | x |  |  | x | x | x |
| catacombs (D2, one bundle) | x | x |  |  | x | x | x | x |  |  | x | x | x |
| valley |  | x |  |  |  |  |  |  |  |  |  | x |  |
| mountain |  |  |  |  |  | x |  |  |  |  |  |  |  |

Other combat-arena scene bundles (not in the matrix):

- misc (6): `combat_arena_city_warlord`, `combat_arena_coast_warlord`, `combat_arena_farm_warlord`, `combat_arena_forest_warlord`, `combat_arena_kingdom_camp_ambush`, `combat_arena_stressworld`
- hero origin-story arenas (Stress Realm) (25): `crusader_origin_1`, `crusader_origin_2`, `duelist_origin_1`, `duelist_origin_2`, `flagellant_origin_1`, `graverobber_origin_1`, `graverobber_origin_2`, `hellion_origin_1`, `hellion_origin_2`, `highwayman_origin_1`, `highwayman_origin_2`, `jester_origin_1`, `jester_origin_2`, `leper_origin_1`, `leper_origin_2`, `manatarms_origin_1`, `manatarms_origin_2`, `occultist_origin_1`, `occultist_origin_2`, `plaguedoctor_origin_1`, `plaguedoctor_origin_2`, `runaway_origin_1`, `runaway_origin_2`, `vestal_origin_1`, `vestal_origin_2`
- boss / gang arenas (19): `city_boss_beastmen`, `coast_barricade_gang_courtier`, `coast_boss_beastmen`, `coast_boss_courtier`, `combat_intro_boss_body`, `farm_boss_beastmen`, `forest_barricade_gang_coven`, `forest_boss_beastmen`, `forest_boss_coven`, `mountain_boss_arms`, `mountain_boss_body`, `mountain_boss_brain`, `mountain_boss_eyes`, `mountain_boss_lungs`, `tundra_barricade_gang_beastmen`, `tundra_boss_beastmen`, `valley_barricade_gang_beastmen`, `valley_barricade_gang_courtier`, `valley_barricade_gang_coven`


[D2] Catacombs arenas live in **one** bundle `dlc_catacombs/dlc_catacombs_scenes_scenes_all.bundle` (49 MB) with 9 scenes per the catalog: `combat_arena_catacombs_{creature_den,cultist,faction,gaunt,gaunt_chirurgeon,inn_defense,resist,story_cultist,urgent_repairs}`. [D1] warlord arenas are separate bundles `dlc_dul_cru/dlc_dul_cru_scenes_scenes_combat_arena_{city,coast,farm,forest}_warlord`; Crusader/Duelist origin arenas `..._combat_arena_hero_story_{crusader,duelist}_origin_{1,2}`.

**Reuse candidates for DD1-style dungeon backdrops** (all verified to exist as scenes): per-region `dungeon_exterior` + `dungeon_interior` pairs for City, Farm, Forest, Coast, Tundra (the lair rounds 1 and 2/3), `creature_den` for City/Farm/Forest/Coast/Tundra/Caves/Catacombs, `story_cultist`/`cultist` for all, `resist`, `urgent_repairs`, Mountain boss rooms `combat_arena_mountain_boss_{brain,lungs,eyes,arms,body}` + `combat_intro_boss_body`, Caves (`combat_arena_caves_*`, no dungeon arena), Stress Realm arenas `combat_arena_stressworld` and 25 hero origin arenas. Map-side (driving) scenes per region: `embark_<city|farm|forest|coast|cave|mountain>` (+ `embark_kingdom_<city|coast|farm|forest|tundra>`, `embark_catacombs` [D2]), camp scenes `embark_camp_<city|farm|forest|coast|cave|tundra>` (+ `embark_camp_catacombs`), node tiles `Assets/Data/Biome/<Region>/Map/NodeTiles/<region>_node_dungeon.prefab` (City/Coast/Farm/Forest/Tundra) and `background_properties_<region>_arena_dungeon_{exterior,interior}.prefab`.

Other scene bundles: `scenes_scenes_{altar_of_hope, camp, cinematic, combat, combat_results, combat_results_hero_story, emptyloading, hero_select, hero_story_intro, inn, main_menu, main_menu_kingdom, mainscene, relationship_test}`, Kingdoms map scenes `kingdom_map_{01,02,04,05,06,07}`, `kingdom_template`, `kingdom_victory/defeat_results_{beastmen,courtier,coven}` (in `kingdoms_map_scenes_basegame`, `kingdoms_gang_*_scenes_all`).

### 11.2 Environment art per region (`biome_*` bundles; model counts from the catalog `Assets/Art/Models/Environments/<x>`)

| region | bundles (MB) | catalog model assets |
|---|---|---|
| City | `biome_city` 42, `biome_city_models_platform_high` 132, `biome_city_scenes` 21, `biome_city_ui` 0.5 | 879 |
| Farm | `biome_farm` 129, `..._models_platform_high` 253, `..._scenes` 24, `_ui` 0.4 | 715 |
| Forest | `biome_forest` 42, `..._models_platform_high` 126, `..._scenes` 7, `_ui` 0.4 | 645 |
| Coast | `biome_coast` 46, `..._models_platform_high` 108, `..._scenes` 14, `_ui` 1 | 757 |
| Cave (Sluice) | `biome_cave` 28, `..._models_platform_high` 86, `..._scenes` 4, `_ui` 0.8 | 501 |
| Tundra | `biome_tundra` 5, `..._models_platform_high` 50, `..._scenes` 3 | 210 |
| Mountain | `biome_mountain` 21, `..._models_platform_high` 42, `..._scenes` 3, `_ui` 1, plus `biome_mountain_{brain,lungs,eyes,arms,body}` (0.04-0.16 each) | 288 |
| Valley | `biome_valley` 21, `biome_valley_common` 50, `..._models_platform_high` 59, `biome_valley_intro`, `biome_valley_kingdoms` | 421 |
| Catacombs [D2] | `dlc_catacombs/..._biome_catacombs` 46, `..._models_platform_high` 137, `..._scenes` 1, `_ui` 1.4, `..._biome_stress_realm` 1 | 748 (`Assets/DLC_catacombs/Art/Models/Environments`) |
| shared | `biome_common` 85 (+ `_models_platform_high` 1.6), `biome_camp` 3.6, `biome_inn` 3.7, `biome_stress_realm` 8, `biome_basegame_data`; catalog also has `Kingdom_Map` 215, `Camp` 19, `InnInterior` 16, `StressRealm` 24, `Common` 391 | |

### 11.3 All bundles grouped by purpose

**Combat arenas (scenes_scenes_combat_*)** - 122 bundles, 432 MB

See matrix above.

**Other scene bundles** - 11 bundles, 4 MB

`scenes_scenes_altar_of_hope`, `scenes_scenes_camp`, `scenes_scenes_cinematic`, `scenes_scenes_emptyloading`, `scenes_scenes_hero_select`, `scenes_scenes_hero_story_intro`, `scenes_scenes_inn`, `scenes_scenes_main_menu`, `scenes_scenes_main_menu_kingdom`, `scenes_scenes_mainscene`, `scenes_scenes_relationship_test`

**Biome environments: models/materials (`biome_<x>_assets`, `_models_platform_high`, `_scenes`, `_ui`)** - 39 bundles, 1407 MB

`biome_cave`, `biome_cave_models_platform_high`, `biome_cave_scenes`, `biome_cave_ui`, `biome_city`, `biome_city_models_platform_high`, `biome_city_scenes`, `biome_city_ui`, `biome_coast`, `biome_coast_models_platform_high`, `biome_coast_scenes`, `biome_coast_ui`, `biome_common`, `biome_common_models_platform_high`, `biome_farm`, `biome_farm_models_platform_high`, `biome_farm_scenes`, `biome_farm_ui`, `biome_forest`, `biome_forest_models_platform_high`, `biome_forest_scenes`, `biome_forest_ui`, `biome_mountain_arms`, `biome_mountain`, `biome_mountain_body`, `biome_mountain_brain`, `biome_mountain_eyes`, `biome_mountain_lungs`, `biome_mountain_models_platform_high`, `biome_mountain_scenes`, `biome_mountain_ui`, `biome_tundra`, `biome_tundra_models_platform_high`, `biome_tundra_scenes`, `biome_valley`, `biome_valley_common`, `biome_valley_intro`, `biome_valley_kingdoms`, `biome_valley_models_platform_high`

**Biome extras (camp, inn, stress realm, intro, kingdoms valley, base data)** - 4 bundles, 15 MB

`biome_basegame_data`, `biome_camp`, `biome_inn`, `biome_stress_realm`

**Mountain act bosses (`boss_*`)** - 5 bundles, 101 MB

`boss_arms`, `boss_body`, `boss_brain`, `boss_eyes`, `boss_lungs`

**Enemy factions (`faction_*`)** - 21 bundles, 506 MB

`faction_beastmen`, `faction_coastal`, `faction_coastal_boss`, `faction_courtier`, `faction_coven`, `faction_cultist`, `faction_fanatic`, `faction_fanatic_boss`, `faction_lost_battalion`, `faction_lost_battalion_boss`, `faction_plague_eater`, `faction_plague_eater_boss`, `faction_shared`, `faction_shared_barricade`, `faction_shared_carrion_eater`, `faction_shared_collector`, `faction_shared_creature_den`, `faction_shared_death`, `faction_shared_pillager`, `faction_shared_shambler`, `faction_swine`

**Kingdoms (`kingdoms_*`, stagecoach_kingdom_*)** - 18 bundles, 67 MB

`kingdoms`, `kingdoms_gang_beastmen`, `kingdoms_gang_beastmen_scenes`, `kingdoms_gang_courtier`, `kingdoms_gang_courtier_scenes`, `kingdoms_gang_coven`, `kingdoms_gang_coven_scenes`, `kingdoms_gangs`, `kingdoms_map`, `kingdoms_map_scenes_basegame`, `kingdoms_menu_beastmen`, `kingdoms_menu_courtier_assets_all_1f77cd754cfdea4d6599405ba6a7d264`, `kingdoms_menu_coven_assets_all_f1042d89cb087e356cb2ab438a2fb95d`, `kingdoms_ui_beastmen`, `kingdoms_ui_courtier`, `kingdoms_ui_coven`, `stagecoach_kingdom_beastmen`, `stagecoach_kingdom_coven`

**Heroes (`hero_*`)** - 12 bundles, 216 MB

`hero_bounty_hunter`, `hero_flagellant`, `hero_grave_robber`, `hero_hellion`, `hero_highwayman`, `hero_jester`, `hero_leper`, `hero_man_at_arms`, `hero_occultist`, `hero_plague_doctor`, `hero_runaway`, `hero_vestal`

**Hero origin stories (`hero_story_*`)** - 21 bundles, 149 MB

`hero_story_flagellant_1`, `hero_story_grave_robber_1`, `hero_story_grave_robber_2`, `hero_story_hellion_1`, `hero_story_hellion_2`, `hero_story_highwayman_1`, `hero_story_highwayman_2`, `hero_story_jester_1`, `hero_story_jester_2`, `hero_story_leper_1`, `hero_story_leper_2`, `hero_story_man_at_arms_1`, `hero_story_man_at_arms_2`, `hero_story_occultist_1`, `hero_story_occultist_2`, `hero_story_plague_doctor_1`, `hero_story_plague_doctor_2`, `hero_story_runaway_1`, `hero_story_runaway_2`, `hero_story_vestal_1`, `hero_story_vestal_2`

**Inn / Altar of Hope / Darkest / memories** - 6 bundles, 19 MB

`altar_of_hope`, `darkest`, `inn`, `inn_upgrades`, `memory`, `quirks`

**Stagecoach (`stagecoach_*`)** - 18 bundles, 55 MB

`stagecoach`, `stagecoach_skin_barnacles`, `stagecoach_skin_base_horses`, `stagecoach_skin_base_wagon`, `stagecoach_skin_blood`, `stagecoach_skin_checkered`, `stagecoach_skin_filth`, `stagecoach_skin_flames`, `stagecoach_skin_ironclad`, `stagecoach_skin_moss`, `stagecoach_skin_oblivion`, `stagecoach_skin_skull`, `stagecoach_skin_stress`, `stagecoach_skin_stripeyblack`, `stagecoach_torch_infernal_blue`, `stagecoach_torch_infernal_green`, `stagecoach_torch_infernal_purplegreen`, `stagecoach_torch_radiant`

**Items / tokens / node / quests** - 21 bundles, 139 MB

`actouts`, `allies_peasant`, `characters_shared`, `item_assets_courtier_blood_human_infected_icon`, `item_assets_item_icon`, `item_assets_items`, `item_assets_trinket_cultist_key_dark_impulse_icon_bleed_res`, `item_assets_trinket_cultist_key_dark_impulse_icon_blight_res`, `item_assets_trinket_cultist_key_dark_impulse_icon_burn_res`, `item_assets_trinket_cultist_key_dark_impulse_icon_debuff_res`, `item_assets_trinket_cultist_key_dark_impulse_icon_disease_res`, `item_assets_trinket_cultist_key_dark_impulse_icon_healing`, `item_assets_trinket_cultist_key_dark_impulse_icon_move_res`, `item_assets_trinket_cultist_key_dark_impulse_icon_stress_res`, `item_assets_trinket_cultist_key_dark_impulse_icon_stun_res`, `node`, `quests`, `story_choice_preview`, `timelines`, `tokens`, `tokens_assets_databasegame`

**UI, NPC/story portraits, fonts, tutorial, sprites, shaders, misc** - 423 bundles, 630 MB

by folder/prefix: `ui_assets_assets/art/ui` (351), `ui_assets_assets/prefabs/ui` (20), `addressable*` (6), `ui_assets_assets/includes/textmeshpro` (6), `scenes_assets_assets/scenes` (5), `ui_assets_assets/art/vfx` (5), `tutorial*` (4), `ui*` (4), `initial_loading_assets_ui/loading` (3), `sprite*` (3), `c*` (2), `initial*` (2), `sprite_assets_assets/art` (2), `ui_assets_assets/data` (2), `fonts*` (1), `initial_loading_assets_assets/art` (1), `lut*` (1), `scriptable*` (1), `shaders*` (1), `skill*` (1), `ui_assets_assets/art/sprites` (1), `ui_assets_assets/prefabs` (1)

Notes: `ui_assets_assets/art/ui/portraits/` holds NPC portraits (`npc_hoarder`, `npc_hospital`, `npc_altarofhope`, `npc_altarofhope_cache`, `npc_coach`, `npc_academic`, ...), loot/pet portraits (`portrait_loot_*`, `portrait_pet_*`) and story icons (`portraits-story-assist*`, `portraits-story-resist-<coast|cultist|fanatic|...>`); `ui_assets_assets/prefabs/ui/*` are screen prefabs; `ui_assets_assets/art/ui/hud/` is the HUD sprite set.

**DLC bundle folders** (loaded only when the DLC is owned; paths under `aa/<dlc>/`)

- `dlc_catacombs` - 20 bundles, 317 MB: `addressable_resources`, `biome_catacombs`, `biome_catacombs_models_platform_high`, `biome_catacombs_scenes_all`, `biome_common`, `biome_data_presets`, `biome_stress_realm`, `biome_ui`, `boss_body`, `faction_vermin`, `hero_abomination`, `hero_story_abomination_1`, `hero_story_abomination_2`, `item`, `lut_assets`, `scenes_scenes_all`, `tokens`, `ui`, `dlc_catacombs/dlc_origin_skins_hero_abomination_skin_origin`, `dlc_catacombs/dlc_supporter_hero_abomination`
- `dlc_dul_cru` - 40 bundles, 154 MB: `addressable_resources`, `biome_city`, `biome_city_models_platform_high`, `biome_coast`, `biome_coast_models_platform_high`, `biome_common`, `biome_farm`, `biome_farm_models_platform_high`, `biome_forest`, `biome_forest_models_platform_high`, `biome_stress_realm`, `boss_body`, `characters_shared`, `faction_shared_warlord`, `hero_crusader`, `hero_duelist`, `hero_story_crusader_1`, `hero_story_crusader_2`, `hero_story_duelist_1`, `hero_story_duelist_2`, `item_assets_items`, `lut_assets`, `node_assets_dlc_dul_cru`, `quests`, `scenes_scenes_combat_arena_city_warlord`, `scenes_scenes_combat_arena_coast_warlord`, `scenes_scenes_combat_arena_farm_warlord`, `scenes_scenes_combat_arena_forest_warlord`, `scenes_scenes_combat_arena_hero_story_crusader_origin_1`, `scenes_scenes_combat_arena_hero_story_crusader_origin_2`, `scenes_scenes_combat_arena_hero_story_duelist_origin_1`, `scenes_scenes_combat_arena_hero_story_duelist_origin_2`, `sprite`, `stagecoach`, `tokens`, `ui`, `dlc_dul_cru/dlc_origin_skins_hero_crusader_skin_origin`, `dlc_dul_cru/dlc_origin_skins_hero_duelist_skin_origin`, `dlc_dul_cru/dlc_supporter_hero_crusader`, `dlc_dul_cru/dlc_supporter_hero_duelist`
- `dlc_origin_skins` - 11 bundles, 38 MB: `hero_flagellant_skin_origin`, `hero_grave_robber_skin_origin`, `hero_hellion_skin_origin`, `hero_highwayman_skin_origin`, `hero_jester_skin_origin`, `hero_leper_skin_origin`, `hero_man_at_arms_skin_origin`, `hero_occultist_skin_origin`, `hero_plague_doctor_skin_origin`, `hero_runaway_skin_origin`, `hero_vestal_skin_origin`
- `dlc_supporter` - 18 bundles, 42 MB: `hero_bounty_hunter`, `hero_flagellant`, `hero_grave_robber`, `hero_hellion`, `hero_highwayman`, `hero_jester`, `hero_leper`, `hero_man_at_arms`, `hero_occultist`, `hero_plague_doctor`, `hero_runaway`, `hero_vestal`, `shared`, `stagecoach_skin_beastmen`, `stagecoach_skin_courtiers`, `stagecoach_skin_coven`, `stagecoach_skin_slime`, `ui`


Bundle groups the task asked for, in short: **arenas/environments per biome** = 11.1/11.2; **hero bundles** = `hero_<flagellant|grave_robber|hellion|highwayman|jester|leper|man_at_arms|occultist|plague_doctor|runaway|vestal|bounty_hunter>_assets_basegame` (12), `hero_story_<class>_{1,2}` (21), DLC heroes in `dlc_*/`; **monster bundles** = `faction_*` (21) and `boss_*` (5); **UI** = `ui_*`, `tutorial_ui*`, `sprite_assets_*`, `fonts*`, `item_*` icons; **inn** = `biome_inn`, `inn_assets_basegame`, `inn_upgrades`, `scenes_scenes_inn`; **kingdom** = `kingdoms*` (18); **altar_of_hope** = `altar_of_hope_assets_basegame`, `scenes_scenes_altar_of_hope`, `ui_assets_altar_sub_screen_options_panel`; **cinematics** = `scenes_scenes_cinematic`, `timelines_assets_all`, `scenes_scenes_combat_intro_boss_body`, `initial_loading_assets_assets/art/throbberstartup`, `initial_loading_assets_*` (intro video `red_hook_intro_4k_vp8_...`), narration/subtitles are data (`cinematic_subtitles_data_export`, 29 `CinematicSubtitles`).

### 11.4 Audio (`StreamingAssets\Audio\`)

129 FMOD `.bank` files, 2.14 GB (98 in the root, 13 in `dlc_catacombs/`, 18 in `dlc_dul_cru/`): `master`, `master.strings`; music `music_{altar,beastmen,camping,caves,city,coast,courtier,coven,farm,final_boss,forest,inn,mountain,panel,shared,title}` (16); region ambience `biome_{cave,city,coast,farm,forest,mountain,tundra,valley}` + `shared_biome`; bosses `boss_{arms,body,brain,eyes,lungs}`; heroes `hero_<class>`, `heroenemy_<class>`, `herostory_<class>`, `shared_hero`; enemies `shared_enemy`, `shared_enemy_{beastmen,courtier,coven}`; `battle`, `altar_of_hope`, `merchant`, `stagecoach`, `ui`, `ui_kingdoms`, `shared_kingdoms_ambience`, `cinematics_{intro,outro}`; narrator/hero VO banks `vo_*` (17); DLC banks `dlc_<dlc>/<kind>_dlc_<dlc>.bank`. There is no `biome_catacombs` in the root (it is `dlc_catacombs/biome_catacombs_dlc_catacombs.bank`) (there is no `music_tundra`; Tundra has only `biome_tundra`). Mods can ship their own FMOD banks in the mod folder (`AudioBankUtils.GetModBankNames`); event paths are strings such as `event:/ui/shared/trinket_equip_cultist` (`item_subtype_data_export`).

## 12. Observations relevant to the DD1 remap (surveyor notes; not requested, kept short)

1. **Region/faction/boss triples that exist in data** (Expedition): City=Fanatics -> Librarian; Farm=Plague Eaters -> Harvest Child; Forest=Lost Battalion -> Dreaming General (+Tap Root); Coast=Coastal -> Leviathan; plus Kingdoms-only Tundra=Cultists -> Exemplar. Cave (Swine) and Catacombs [D2] (Vermin) are **optional regions with no lair/dungeon**: the Swine have no boss actor of their own in the data (only `cave_swine_brute_b`, tagged `wilbur`, as champion).
2. **Final act is not a dungeon region**: five fixed Mountain fights (Denial/Resentment/Obsession/Ambition/Cowardice) with bespoke multi-part actors; there is no "Ancestor" actor/character in the data. The nearest things are `shared_ancestor_statue` ("Ancient Adversary", creature-den boss, 225 HP, per-region scene `combat_arena_<region>_creature_den`, barks `bark_ancestor_statue_attack`), its drops `trinket_ancestors_coat|mustache_cream|pistol` ("Ancestor's Coat / Mustache Cream / Pistol", `sub_type ancestral`) and the narrator lines (`vo_*`, `narration_*`, `subtitles.txt`).
3. **No hamlet, no persistent roster/town in Expedition**: persistence = profile `candles` + `UnlockTrack`s (Altar of Hope). The only "estate-like" persistent layer is Kingdoms (inns/camps with upgrade trees paid in `materials`, wounds, siege, escalation). 68 `InnUpgrade`s, 4 `KingdomDifficulty`, 131 `KingdomEvent`s.
4. **Combat uses ranks 1-4** (skills' `launch_ranks,1,2,3,4`; enemies per `BattleConfiguration`: 1 x24, 2 x73, 3 x453, 4 x548, 8 x1) with actor sizes 1-4 slots; bosses use size 2-4 plus ranked parts, so a DD1 boss line-up must be expressed in these slot rules.
5. **Injection points that need no code**: root-level mod CSV that re-declares an existing `BattleConfigurationTable`/`LootTable`/`UnlockTable`/`InnTable` id (options append); new `BattleConfiguration`s, `Item`s, `Quirk`s, `Token`s, `Effect`s, `Buff`s (new ids merge freely); `Boss`/`Biome` replacement requires `Overrides/`. Table roots called from Unity data (`dungeon_mashes_starter_master`, `biome_<region>_dungeon_round2`, `faction_mashes_road_master`...) can therefore be extended with DD1 fights by appending options.
6. **Dead/partial things to know**: 117 orphan battle configs; `trench_run` biome modifier has `m_Chance,0`; `route_catacombs_combat_gaunt` chance 0; `TrinketSet` has only `test_cave_trinket_set`; no `dlc_origin_skins`/`dlc_supporter` gameplay data; `mountain_boss_eyes_quick_phase_test` is a debug fight; `herostory_*` has two near-duplicate files `herostory_dul_2_wine_glass` / `herostory_dul_2_wineglass`.
7. **Locale**: Russian is available and largely fuzzy; a Russian mod needs `Localization/ru.po` next to the English `.txt` (priority 3 beats base text).
