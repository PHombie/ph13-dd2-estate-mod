# Camping (DD1 rules, DD2 Kingdoms rest stop) and the Survivalist

Status: written and compiled, unit-tested in the core, **not run in the game** (section 9 lists what that leaves
open). Decomp paths are relative to `_ref/dd2-decomp/IronCrown/` (IC), DD1 paths to the DD1 install, DD2 tables to
`Darkest Dungeon II_Data/StreamingAssets/Excel/`.

## 1. DD1: where the rules are and what they say

| Rule | DD1 file and key | Stock value |
|---|---|---|
| Firewood per quest length | `campaign/provision/provision.json`, `raid_starting_length_inventory_item_lists[length]` (supply `firewood`) | short 0, medium 1, long 2 (index 4: 4) |
| Firewood item | `inventory/base.supply.inventory.items.darkest` | stack 1, price 0, sells for 0 |
| Respite points | `shared/rules.json` `camp_start_camping_points` | 12 |
| Meals | `shared/rules.json` `meals_table` (`rations_per` a hero, `healing` share of max health, `stress`) | none 0 / -20% / +15; half 0.5 / 0 / 0; full 1 / +10% / 0; feast 2 / +25% / -10 |
| Torch at camp, at an ambush | `camp_restore_torch`, `ambush_torch_reduction` | +100, -100 |
| Night ambush | `ambush_camping_base_chance` | 0.33 |
| Surprise in an ambush | `surprise_ambush_party_base_chance`, `surprise_ambush_monsters_base_chance` | 1.0, 0.0 |
| Stress relief for camping at all | `camp_relieve_stress` | 0 |
| Skills | `raid/camping/default.camping_skills.json` and the DLC's files (below): `cost`, `use_limit`, `effects[]` (`selection`, `requirements`, `chance{code,amount}`, `type`, `sub_type`, `amount`), `hero_classes`, `upgrade_requirements[].currency_cost` | 64 skills in the stock file; every one costs 1750 gold |
| Shared or class skill | same file, `configuration.class_specific_number_of_classes_threshold` | 4 |
| Buffs behind `type: buff` | `shared/buffs/*.buffs.json` by id: `stat_type`, `stat_sub_type`, `rule_type`, `is_false_rule`, `rule_data`, `duration_type`, `duration` | every camping buff: `combat_end`, 4 |
| Religious classes | `heroes/<class>/<class>.info.darkest`, `tag: .id "religious"` | crusader, vestal, leper, flagellant |
| Skills a recruit arrives with | same file, `generation:` `.number_of_class_specific_camping_skills`, `.number_of_shared_camping_skills` | 2 and 1 |
| Skills ready at a time | `shared/rules.json` `max_number_of_camping_skills` says 3; the tutorial text `tutorial_popup_map_camping_skills_description` (English) says "4 Camping skills active" | 4 is what DD1 plays by |
| Survivalist discount | `campaign/town/buildings/camping_trainer/camping_trainer.building.json` `camping_skill_cost_discount_upgrades`, tree `camping_trainer.cost` in `upgrades/building/camping_trainer.upgrades.json` | 5 steps of 10%; 15/35/54/72/92 crests |
| Where one may camp | English strings `str_cant_use_firewood_in_entrance_room` ("Can't camp in first room"), `str_camping_longer_quests_only` | not in data otherwise |
| Camp screen | `scripts/layout/screen.raid.darkest`: `meal_scroll` (headerY 52, buttonY 148, buttonOffset 74), `camp_layout` (respite scroll 732,60; title 60,44; points 324,44; description 56,130 w 360; REST 100,200); art `scrolls/meal_scroll.png`, `scrolls/event_scroll_campingrespite.png`, `raid/camping/skill_icons/camp_skill_<id>.png` | |
| Survivalist screen | `campaign/town/buildings/camping_trainer/camping_trainer.layout.darkest`: class grid from 60,30, shared grid from 60,200, spacing 110,90 | |

Corrections to the task's assumptions:

* **Camping skill prices are not in `upgrades/heroes/*.upgrades.json`** (those hold weapon, armour and combat
  skills only). Each skill carries its own price in its `upgrade_requirements`.
* **This DD1 install has the runaway and the duelist**: DLC `dlc/4964110_fires_edge/features/{runaway,duelist}/`
  ships `raid/camping/<class>.camping_skills.json`, `shared/buffs/<class>.buffs.json`, hero files and strings.
  Their real DD1 sets are read. Only an install without that DLC falls back on an analogue (section 8).
* **A camping buff lasts four fights**, not "until the next camp or the quest's end": every one is
  `duration_type combat_end, duration 4`. DD1's tooltip still says "until next Camp" (`tray_icon_tooltip_buff_until_camp_format`),
  so a new camp ends them too.
* The flagellant's DLC file (`dlc/580100_crimson_court/features/flagellant/raid/camping/crimson_court.camping_skills.json`)
  lists him for his four skills only, while his hero file still gives a recruit one shared skill. The core reads
  that as "the shared skills are open to every class".

DLC folders read (`Core/CampingRules.cs`, `Roots`): the game's root, `dlc/445700_musketeer/`,
`dlc/580100_crimson_court/features/flagellant/`, `dlc/702540_shieldbreaker/` (ships the whole stock skill file again
with its class added: same ids merge, classes add up), `dlc/4964110_fires_edge/features/duelist/`, `.../runaway/`.
`IDd1Files` lists files, not folders, so the folders are named.

Effect types in the data and how often (all files): `buff` 198, `stress_heal_amount` 59, `stress_damage_amount` 30,
`health_heal_max_health_percent` 21, `reduce_ambush_chance` 11, `remove_bleeding` 10, `remove_poison` 10,
`remove_disease` 10, `loot` 8, `remove_deaths_door_recovery_buffs` 4, `reduce_torch` 2,
`health_damage_max_health_percent` 2, `refresh_camp_skill_uses` 1. Selections: `self`, `individual`,
`party_other`, `party`. Requirements: `religious`, `not_religious`, `afflicted`, `has_deaths_door_recovery_buffs`.

DD1 behaviour that is **not in the data** and is assumed (marked in the code the same way):

* effects sharing a chance `code` are the outcomes of one roll (gallows humor: 75% relief / 25% stress), rolled per
  hero it can land on;
* `individual` is a companion, never the user (DD1's string for it: "One Companion");
* a half ration left over is rounded up (three heroes on half rations eat 2);
* `remove_disease` (amount 0) takes every disease the hero has;
* the 0.65 cap on surprise chances (`surprise_max_party_surprised_chance`) is not applied to an ambush, whose
  base is 1.0.

`camp_rest_point_threshold` (6) and `camp_rest_number_of_hero_threshold` (1) are read by nothing here: what DD1
does with them is unknown.

## 2. DD2: how Kingdoms shows a camp

* A camp is an inn. `KingdomMapCellCamp : KingdomMapCellInnContainer` takes its `InnDefinition` from
  `RulesManager.GetRules<KingdomRules>().CampBase` (`Assets.Code.Kingdom/KingdomMapCellCamp.cs:25-33`; table
  `kingdom_rules_data_export.Group.csv:9` `m_CampBaseId,kingdom_camp`, `kingdom_inn_data_export.Group.csv:1-13`).
* It is shown by entering `GameModeType.INN` (`Assets.Code.Game/GameModeType.cs:47`, scene name `inn`).
  `InnBhv.GameModeEnterAsyncPreStart` (`Assets.Code.Inn/InnBhv.cs:69-168`) creates the `InnSystem`, picks the scene
  by the current map cell (`:121-129`: `CAMP` → `"camp"`, constant `SCENE_CAMP` `:33`) and loads it with
  `RedHookSceneManagerBhv.LoadSceneAdditively(m_Scene, m_loadingObject, setActive: true)` (`:133`); it unloads it in
  `OnGameModeExitComplete` with `UnloadAdditiveSceneByForce` (`:188`). `IsCamp()` is `m_Scene == "camp"` (`:261-269`).
* `InnSystem.GatherInnInstance` takes the inn from `KingdomBhv.KingdomMapManager.GetCurrentMapCell()`
  (`Assets.Code.Inn/InnSystem.cs:125-136`).
* Leaving: `InnBhv.EndInn` (`InnBhv.cs:328-389`). For a camp it rolls `RunStatType.CAMP_AMBUSH_CHANCE`
  (`:351`; `Assets.Code.Run/RunStatType.cs:157`; Kingdoms value 0.2, `kingdom_rules_data_export.Group.csv:48`),
  raises `EventCampAmbushStart` (`Assets.Code.Inn.Events/EventCampAmbushStart.cs`, handled by
  `RosterManager.HandleEventCampAmbushStart`, `Assets.Code.Roster/RosterManager.cs:292`), rolls a fight from
  `KingdomRules.m_CampAmbushBattleConfigTableId` (`:370`, `camp_mashes_master`) and starts it in
  `BiomeData.GetCampAmbushCombatArena()` with `CombatSource.CAMP_AMBUSH` (`:372`;
  `Assets.Code.Combat/CombatSource.cs:54`, `isEndOnPartyDead: true`). After it the results screen goes to EMBARK
  and sends the survivors home (`Assets.Code.Combat.Presentation/CombatResultsPresentationBhv.cs:134-139`).
* The scene (`aa/scenes_scenes_camp.bundle`, `Assets/Scenes/camp.unity`; read with UnityPy, nothing extracted):

```
camp_ground
Camp_Root (x = 2500)
  Inn Timeline            PlayableDirector, Animator, SetCameraOnTimelineBhv
    Inn Rendering         SetActiveWhenSceneActiveBhv
      Inn_ActorSpawnPositions   SpawnPositions  -> Position A..D (a crate, a crate, a barrel, a chest to sit on)
      Blend Camera        BlendActiveCameraBhv (priority 10): Default Cam (FOV 30 at 16:9), Seat A..D Cam
      Menu Camera         CinemachineVirtualCamera (priority 1)
      Environment         camp_ground, trees, bushes, camp_firepit (layer 21), moon, stars
      Character_Properties  Post Processing (Volume), MaterialProperties
      Fog (FogVolume), VFX (fire, smoke, leaves, bats), Lights (fire light, four character spotlights)
    Inn Presentation      InnPresentationBhv, PartyPresentationBhv (m_DefaultAnimatorState "inn_idle"), DataContextBhv
      Inn UI (Canvas, InnUiBhv), Floating UI (Canvas, RestSlots ...)
  FMOD (Inn)              InnSfxBhv, InnNarrationBhv
  InnSystemsInstaller, DebugLoader (INN)
```

* Heroes at an inn: `PartyPresentationBhv.SpawnActorByActorGuid` puts
  `ActorCreateGameObjectBhv.CreateActorGameObject(guid, spawnTransform, spawnTransform.gameObject.layer, animatorState, loadSubclasses: false)`
  on a spawn point (`Assets.Code.Presentation/PartyPresentationBhv.cs:199-234`, call at `:219`; default state
  `:37`; `Assets.Code.Presentation/SpawnPositions.cs:11`; `Assets.Code.Actor/ActorCreateGameObjectBhv.cs:165`).
  It is the call the corridor already uses.
* The rendering root shows only while its scene is the active one
  (`Assets.Code.Utils.Behaviors/SetActiveWhenSceneActiveBhv.cs:11-46`), which is why the scene is loaded with
  `setActive: true` (`Assets.Code.Loading/RedHookSceneManagerBhv.cs:305`, `RunLoad` `:505`, `SetActiveScene` `:565`).
* Out of INN mode the inn's own screens put themselves away: `InnPresentationBhv.Initialize` does
  `gameObject.SetActive(GameModeMgr.CurrentMode == GameModeType.INN || InnSystemsInstaller.IsDebug)`
  (`Assets.Code.Inn.Presentation/InnPresentationBhv.cs:519`; the two canvases are its children), and
  `InnUiBhv.OnGameModeEnterStart` deactivates itself for any other mode (`Assets.Code.UI.Managers/InnUiBhv.cs:68`).
* An additive load during a running mode is ordinary: `GameModeMgr.HandleScenesLoaded` → `ProcessSceneLoaded`
  → `NotifyOfCurrentMode` tells the new scene's components the current mode, with no mode change
  (`Assets.Code.Game/GameModeMgr.cs:215`, `:236`, `:336`).
* The inn adds the `Foreground` layer to the main camera while it is shown (`InnPresentationBhv.cs:322`) and takes
  it off on leaving (`:282`): the fire pit is on that layer.
* The LUT manager knows camp scenes by the active scene's name (`Assets.Code.Rendering/LUTManager.cs:558-562`).
* Ambush arena: `aa/scenes_scenes_combat_arena_kingdom_camp_ambush.bundle`, scene
  `combat_arena_kingdom_camp_ambush` (in `aa/catalog.json`).

## 3. Presentation: the option chosen

**(b): the camp scene alone as the backdrop of a mod screen.** `Dungeon/CampView.cs` loads `"camp"` the way
`InnBhv` does while the Estate's hub mode goes on, seats the party on the scene's spawn points with the scene's own
animator state, and adds a camera of the mod's own at the scene's `Default Cam` pose (priority 1000, lowered by
`CameraDrop` so the fire sits above the HUD). `Dungeon/CampScreen.cs` is DD1's camp over the dungeon HUD.

Why not (a), DD2's INN mode with the camp definition:

1. `InnBhv` and `InnSystem` take the scene and the inn from `KingdomMapManager.GetCurrentMapCell()`
   (`InnBhv.cs:121`, `InnSystem.cs:127`). The Estate's Kingdom has no map (`StartKingdom(..., null, null, ...)`):
   both dereference null.
2. The inn's state machine scores the run, collects loot and saves through `SaveUtils`, which the Estate blocks
   (`Dd2/SessionPatches.cs`, `NoVanillaSnapshotsInEstate`); its end goes to EMBARK or to a `CAMP_AMBUSH` fight whose
   lost party ends the kingdom (`CombatSource.cs:54`) and whose results screen sends heroes "home"
   (`CombatResultsPresentationBhv.cs:134-139`).
3. What the inn offers is DD2's: rest items, the stagecoach, hero swaps. The user asked for DD1's meal, respite
   and skills; none of the inn's UI would be used, all of it would have to be suppressed.
4. The session's own `SetMode` redirect sends INN to the hub; it would need an exception with a way back.

(b) touches none of that: one additive scene, one call per hero, no mode change, and the camp works without it (a
scene that fails to load in 20 s leaves the screen over a dark room).

Scene handling details that matter:

* Before unloading, the hub's scene is made active again: `UnloadAdditiveScene_Internal` parks on a "Temp Unload
  Scene" when asked to unload the active scene (`RedHookSceneManagerBhv.cs:362`, `:394`, `:755`), which the
  Estate's modes never clear (MODLOG gotcha 9).
* A fight is started only after the scene is gone (`Camp.Night` waits for `CampView.Hide`).
* If the session leaves the hub with a camp open, `CampScreen.Update` and `DungeonRun.Dispose` unload the scene.
* No inn audio: `InnAmbienceBhv` and the inn music key on INN mode. The camp is silent but for the scene's own.

## 4. DD1 effects on DD2 heroes

| DD1 effect | In the Estate | DD2 call |
|---|---|---|
| `stress_heal_amount` N (of 200) | N × StressMax / 200 points, the fraction rolled as the chance of one more (the Tavern's rule, `ActivityRules.RollRelief`) | `ActorInstance.ApplyStressHeal(points, SourceType.INN)` (`Assets.Code.Actor/ActorInstance.cs:5200`), `StressMax` `:548` |
| `stress_damage_amount` N | same scale and roll | `ApplyStressDamage(points, canResist: false, SourceType.STORY, skillId, 0)` (`:5174`) |
| `health_heal_max_health_percent` x | x of `CurrentHpMax` | `ApplyHealthHeal(amount, false, SourceType.INN, false)` (`:5025`), `CurrentHpMax` `:499` |
| `health_damage_max_health_percent` x, and the "none" meal | x of `CurrentHpMax`, never the last point of health | `ApplyHealthDamage(..., DeathType.EFFECT, SourceType.STORY, ...)` (`:4969`) |
| `remove_bleeding`, `remove_poison` | DD2's removal of bleed / blight marks; a DD2 hero carries none out of a fight (`DotContainer.GetIsCombatSpecificInternal` is true, `Assets.Code.Dot/DotContainer.cs:84`), so in practice nothing | `DotContainer.RemoveAllInstancesWithTypes(["bleed"] or ["blight"], false, SourceType.INN, id, 0)` (`DotContainer.cs:113`; types from `dots_data_export.Group.csv`) |
| `remove_disease` | every disease quirk (not curses), as the Sanitarium takes one | `QuirkContainer.Remove(instance, SourceType.HOSPITAL, null, 0)` (`Assets.Code.Actor.ActorContainer/ActorContainer.cs:220`), `QuirkDefinition.IsDisease` / `IsCurse` (`Assets.Code.Quirk/QuirkDefinition.cs:69`, `:71`) |
| `remove_deaths_door_recovery_buffs`, requirement `has_deaths_door_recovery_buffs` | DD2 Kingdoms' **wound**: what a hero takes there at death's door (`Assets.Code.Wound/WoundManager.cs:52`; `kingdom_wound_triggers_data_export.Group.csv:29`; `kingdom_rules_data_export.Group.csv:62` gives every Kingdom hero `wound_percent_max 0.5`) | `ActorInstance.IsWounded` (`:534`), `ChangeWoundPercent(-WoundPercent, SourceType.INN)` (`:5128`) |
| requirement `afflicted` | stress at half the hero's scale or more (DD1 afflicts at 100 of 200) | `Stress` (`:517`) ≥ 0.5 × `StressMax` |
| requirements `religious` / `not_religious` | DD1's tag of the class with the same id | — |
| `reduce_ambush_chance` a | taken off the night's chance (`CampSession.AmbushChance`) | — |
| `reduce_torch` | torchlight down by the amount | `Exploration.AddLight`, then `RunValues.SetValue(RunValueType.TORCH, ...)` as `DungeonRun.ApplyLight` does |
| `loot` table | DD1's table drawn by `LootTables.Draw`, put in the bag as far as it fits (`S`, `KEYONLY`); trinket tables give nothing yet | — |
| `refresh_camp_skill_uses` | the companion's used skills can be used again this camp | — |
| `buff` | section 5 | |

The meal: `Camp.Eat` takes `ceil(rations_per × heroes)` food out of the bag and applies the row's health and
stress with the calls above.

## 5. DD1 camping buffs on DD2 stats (`Core/CampingBuffs.cs`, `CampingBuffMap`)

A hero's buffs become one `ActorDataStats` container (`new ActorDataStats(id, csv)`, `Init`, `SetSource(SourceType.CLASS, id)`,
`ActorInstance.ActorData.AddChild`; `Assets.Code.Stat/StatDataContainer.cs:75-184` reads `add_stat`,
`multiply_stat`, `sub_stat`; `Assets.Code.Data/DataContainer.cs:63`, `:364`, `:373`; then `RefreshStats()` `:4870`
and `UpdatePreviousHpMax()` `:4901`). This is the Blacksmith's and `Dd2/Difficulty.cs`'s mechanism. A source is
needed because damage sums stats per source (`ActorInstance.GetAddStatValuesBySource`, `:4776`;
`Assets.Code.Skill/SkillCalculation.cs:2941`, `:2958`). The game does not save that tree: `Dungeon/CampBuffs.cs`
rebuilds it from the saved ledger.

| DD1 buff stat | DD2 stat line (`Assets.Code.Actor/ActorStatType.cs`) | Why |
|---|---|---|
| `combat_stat_multiply` `damage_low`, `damage_high` | `add_stat,health_damage_dealt_percent` (`:21`), half the amount each | DD1 always raises both ends together |
| `combat_stat_add` `attack_rating` (ACC) | `add_stat,health_damage_dealt_percent`, the amount | DD2 has no accuracy: the blows that would have missed, as damage |
| `combat_stat_add` `crit_chance` | `add_stat,crit_chance` (`:15`) | same thing |
| `combat_stat_add` `defense_rating` (DODGE), `protection_rating` (PROT) | `add_stat,health_damage_received_percent` (`:25`), minus the amount | DD2 has neither; the Blacksmith reads DD1's dodge the same way |
| `combat_stat_add` `speed_rating` | `add_stat,speed` (`:9`), whole points | same thing |
| `combat_stat_multiply` `max_hp` | `multiply_stat,health_max` (`:27`) | same thing |
| `stress_dmg_received_percent` (-x: less) | `sub_stat,resistance,stress,+x` (`:35`; 26 uses in DD2's tables) | DD2 resists a stress hit whole, at that chance (`Assets.Code.Resist/ResistCalculation.cs:361`): the same on average |
| `resistance` `bleed` / `poison` / `disease` / `move` / `debuff` / `stun` | `sub_stat,resistance,<bleed|blight|disease|move|debuff|stun>` | one to one |
| `hp_heal_received_percent` | `add_stat,health_heal_received_percent` (`:41`) | same thing |
| `damage_received_percent` | `add_stat,health_damage_received_percent` | same thing |
| `scouting_chance` | `Exploration.ScoutBonus` (the camp's share is added and taken back out) | DD1's own rule |
| `monsters_surprise_chance` +x | chance x that the enemies are surprised in each fight but a boss's | the mod has no base surprise to add it to |
| `party_surprise_chance` -x | lowers the chance the party is surprised in the night's ambush | same |
| `rw_play_with_fire_burn_on_hit` (an effect on every hit) | DD2's own effects container `runaway_firestarter_burn_buff` (`buff_data_export.Group.csv:11077-11079`: `on_hit_as_performer_to_target_effects,skill_dot_small_burn`), added to `ActorData` as DD2's buffs add theirs (`Assets.Code.Buff/BuffContainer.cs:109-134`, `:129`) | DD1's Play with Fire is DD2's Firestarter |

Rules (`rule_type`): a DD2 stats container holds always, so a buff under a rule is given at **half** its amount,
always (`CampingBuffMap.ConditionalShare`: `meleeonly`, `rangedonly`, `monsterSize`, `firstroundonly`, `has_buff`).
The exception is `in_rank` (the hellion's battle trance): the containers are rebuilt as a fight starts and the rule
is answered by the hero's place in the party then (index 0 of `RosterManager.GetActorGuids(PARTY)` is the front),
all or nothing. A skill's tooltip is in DD1's words (`Dungeon/CampContent.cs`, `CampText`): the rank rule as DD1 says it
("+25% DMG if in position 1", `buff_rule_tooltip_in_rank`), a buff given at half as the plain buff of that half.

Duration: `CampBuffLedger` counts DD1's four fights (`DungeonRun.OnReturnedFromFight`), and ends the buffs at the
next camp and when the expedition ends.

## 6. In the run (`Dungeon/DungeonRun.cs`, `Dungeon/Camp.cs`)

* Firewood: `Provisioning.NewShop` puts DD1's starting items of the quest's length in the bag (free, not sold
  back). One log is one camp; a log takes a slot (stack 1).
* Making camp: a click on the firewood in the bag (`ItemUseKind.Camp`, DD1's own way) or the HUD's "Make Camp"
  button. `DungeonRun.CampRefusal` allows it in a room that is not the entrance and has no fight left, out of any
  prompt. `MakeCamp` burns the log, ends the last camp's buffs, restores the torch and saves.
* `Camp` holds a `CampSession` (meal → skills → sleep). `DungeonRun.Busy` is true while it lasts, which stops
  walking, the bag, leaving and travel.
* Sleep: `CampSession.Sleep` rolls the ambush; `Camp.Night` darkens the screen, unloads the scene and calls
  `DungeonRun.EndCamp`. A quiet night resumes the corridor and saves. An ambush puts the light out
  (`ambush_torch_reduction`) and starts a fight through the existing path (`Difficulty.Apply`,
  `EstateSession.StartBattle`, source `EstateMode.DungeonFight`): a hallway fight of the dungeon's own pool at the
  quest's tier (`DungeonContent.Pick`), in Kingdoms' camp ambush arena (`CampView.AmbushArena`, empty for the
  dungeon's own). Its end is handled apart from the exploration's fights (`_ambush`): a win or a flight leaves
  the party in its room; a lost party ends the expedition as failed through the event queue.
* **Surprise** exists in DD2: `BattleTurnOrder.Parameters.m_AmbushTeamIndex` (`Assets.Code.Combat/BattleTurnOrder.cs:24-27`)
  names a team left out of the first round's turn order (`:336-339`). The game only ever sets it from a test
  preference (`Assets.Code.Combat/CombatBhv.cs:444-445`). `EstateFightsCanStartWithASurprise` (a Harmony prefix on
  `BattleTurnOrder.Create`, `:76`, called once per battle from `Assets.Code.Combat/Battle.cs:84`) sets it to
  `BattleTeams.HERO_TEAM_INDEX` (0, `Assets.Code.Combat/BattleTeams.cs:74`) for an ambush and to the enemy team (1,
  `:76`) when a surprise buff's roll succeeds. DD1 also shuffles a surprised party; that is not done.
* Save: the expedition section gains `campBuffs` (the ledger) and `camp` (session and log) next to `bag` and
  `exploration`. A camp caught by the save is reopened in `ResumeAfterLoad`; buffs go back on the heroes there.

## 7. The Survivalist (`Estate/Survivalist*.cs`)

* A hero's `CampSkillBook` (known, ready) is the estate's own data, section `survivalist` of the save, by guid. A
  hero without one gets DD1's recruit roll (2 class + 1 shared) on first asking, seeded by guid and class.
* Price: `UpgradeRules.Price(skill.PriceGold, "camping_trainer.cost")` = 1750 / 50 = 35 estate gold, less 10% per
  Bonfire step (`UpgradeRules` already reads `camping_skill_cost_discount_upgrades` as a discount track).
* Ready skills: 4 at most; a learned skill is ready at once while a place is free; a click on a known skill toggles.
* Not carried over: the building's own unlock condition (`camping_trainer.building.json` `requirements`).

## 8. The mod's own calls (numbers and choices that are not DD1's)

| Call | Value | Reason |
|---|---|---|
| A buff under a rule the Estate cannot ask | half, always | a hero whose blows meet the rule half the time gets the same on average |
| "Afflicted" | stress ≥ half the hero's scale | DD1's 100 of 200 |
| "Mortality debuffs" | Kingdoms wounds | the lasting mark of death's door in the game the Estate is hosted in |
| ACC → damage dealt, DODGE and PROT → damage taken | amount for amount | section 5 |
| Camp damage never kills | 1 health left | DD1 leaves a hero at death's door; a DD2 hero out of a fight has no such state |
| Ready skills | 4 | DD1's tutorial text over rules.json's 3 |
| Shared skills open to every class | — | the flagellant's files (section 1) |
| Analogues without the Fire's Edge DLC | runaway → grave robber's class skills, duelist → man-at-arms' | a scavenging scout; a fencing master |
| Ambush arena | `combat_arena_kingdom_camp_ambush` | the place the player was just looking at |
| Scrolls at the right of the screen | 1420,40 | DD2's rest stop seats the party left of centre |
| `CampView.CameraDrop` | 0.3 world units | estimate: the HUD covers the bottom third |
| Loot at camp is never asked about | what fits goes in the bag | no prompt under the camp screen |

## 9. Not verified without the game

1. The camp scene loading additively in the Estate's hub mode: the set showing, the inn's own UI staying away,
   the fire pit drawn, no error from the scene's installer (`InnSystemsInstaller`), unloading cleanly, twice in a row.
2. Heroes seated by `CreateActorGameObject` on the scene's spawn points in `inn_idle`; framing (`CameraDrop`,
   `CameraPan`, `FovScale` through `camp.view`); whether the mod's camera wins over the scene's.
3. The camp screen in the game's fonts; clicks on the HUD's plates under the camp canvas; the fade.
4. `m_AmbushTeamIndex` through the Harmony prefix: the surprised side missing round one.
5. `combat_arena_kingdom_camp_ambush` as an arena for an arbitrary battle configuration.
6. The `ActorDataStats` container with `sub_stat,resistance,stress` and the other lines on a hero in a fight; the
   Firestarter effects container added to `ActorData`.
7. Whether Estate heroes take Kingdoms wounds at all (if they do, nothing but three camping skills mends them).
8. Stress damage at camp driving a hero to DD2's meltdown out of a fight.
9. A save taken mid-camp and continued; buffs after a load.
10. The Survivalist's panel and the registration order with `UpgradeWindow`'s stand-in (the screen is claimed
    again on every load and new game).

## 10. Dev bridge

`camp.rules`, `camp.skills cls=`, `camp.state`, `camp.give firewood= food=`, `camp.start [force=true]`,
`camp.eat meal=none|half|full|feast`, `camp.select guid=`, `camp.use guid= skill= [target=]`,
`camp.sleep [ambush=true|false] [surprised=false]`, `camp.buffs`, `camp.view [drop=] [pan=] [fov=] [arena=]`;
`survivalist.open`, `survivalist.close`, `survivalist.state guid=`, `survivalist.roster`,
`survivalist.learn guid= skill=`, `survivalist.toggle guid= skill=`, `survivalist.grant guid=`.
Offline: `python tools/preview_camp.py [--cls <class>] [--list]`; core tests `dotnet run --project tests/CoreTests -c Release -- --only Camping`.
