# Monster difficulty by DD1 quest tier

How an estate fight is made as hard as the DD1 quest it belongs to: apprentice (DD1 level 1), veteran (3),
champion (5), Darkest Dungeon (6). Engine facts are from the decompiled `IronCrown` (`_ref\dd2-decomp\IronCrown\`,
cited as `Namespace/File.cs:line`) and the game's tables (`EXCEL` = `...\StreamingAssets\Excel\`). DD1 numbers are
read from the DD1 install. Nothing here has been run in the game from this side; what that leaves open is listed
in section 8 and marked "not verified" where it matters.

Every number of section 3 can be derived again: `python tools/check_content.py --difficulty` reads both installs,
prints the DD1 growth, the size of the DD2 fight pools, the derived shares and compares them with
`tier_rules.<tier>.scaling` in `dungeons.json` (exit 1 when they drift apart by more than 0.08).

## 0. Summary

* Hosted as a Kingdom without a map, **none of DD2's own difficulty ladders reaches an estate fight**: ordainment
  needs an expedition run, the escalation never leaves 1 (nothing advances the day), run levels need a run manager,
  there is no region index. The fight pools of `dungeons.json` (bigger line-ups per tier) were the only thing that
  rose with the tier. Meanwhile every estate hero carries Kingdoms' escalation-1 buff (+20% damage, +15% health,
  +10% DOT resistance) at all times.
* The tier is now brought to the fight by `Dd2/Difficulty.cs`, for one fight at a time and without writing
  anything the game saves:
  1. **stats** for every enemy (health, damage, DOT damage, speed, crit) from `tier_rules.<tier>.scaling.<kind>`;
  2. **ordainment** with the dungeon's row for the tier at the game's own chance (lair bosses: their row, always);
  3. **gang bosses**: their gang's battle modifier for the tier and their blessing buffs;
  4. **the escalation that conditions see** is the tier's (1/2/3/3): loot rarity, the gang factions' skills,
     which Death shows up. The run value itself stays 1, so the heroes' buff never flickers.
* The stat shares are derived, not chosen: DD1's growth of a whole fight (monster variants `_A/_B/_C` and the mash
  tables per level) is set against the growth of the estate's heroes (the smith's DD1 gear, the guild's mastery),
  and what the DD2 pools already add by composition is taken off. Result (every enemy of the fight, against the
  same monster at apprentice):

  | tier | hallway | room | boss |
  |---|---|---|---|
  | apprentice | x1.00 health, x1.00 damage | x1.00, x1.00 | x1.00, x1.00 |
  | veteran | x1.20, x1.00, +1 speed | x1.60, x1.40, +1 speed | x1.65, x1.45, +1 speed |
  | champion | x2.05, x1.65, +2 speed | x2.50, x2.25, +2 speed | x2.75, x2.40, +2 speed |
  | darkest | x2.40, x2.10, +2 speed | x2.30, x2.50, +2 speed | x2.10, x2.40, +2 speed |

  With the pools' own growth on top, a veteran fight holds x1.75 the health of an apprentice fight of the same kind
  and a champion fight x3.0 to x3.2; against heroes with the gear and mastery DD1 allows at that tier this is DD1's
  own ratio (section 3.4).

## 1. What makes enemies stronger in DD2

An actor's stats are the sum of the `ActorDataStats` containers under `ActorInstance.ActorData`:
total = (base + all `add`) x (1 + all `multiply`) (`Assets.Code.Stat/StatInstance.cs:151-159`). Health buffs are
`multiply_stats` on `health_max`, damage buffs `add_stats` on `health_damage_dealt_percent` (base 1; summed in
`Assets.Code.Skill/SkillCalculation.cs:2954-2965`), so every "+x%" below adds up with the others.
When an actor is created inside a started game it picks up four shared containers by its tags
(`ActorInstance.OnAddedToRun`, `Assets.Code.Actor/ActorInstance.cs:6788-6820`): torch levels, run value levels,
biome modifiers/upgrades, kingdom events. A fifth, the battle modifier, is added when it joins its team
(`ActorInstance.OnAddedToTeam`, `:6569-6584`); a sixth, the boss modifier, at creation (`PostCreate`, `:766`, `:856-859`).

| # | mechanism | computed in | fed by | what it gives (data) |
|---|---|---|---|---|
| 1 | **Fight composition**: `<faction>_mash_1xx` starters, tables `_mashes_normal` / `_hard` / `_normal_champions` / `_hard_champions` / `_brutal_champions` (one or two `_b` "champion" actors), cultist `_enhanced`, gang `siege_easy/normal/hard` | `LibraryBattleConfigurationTables.RollBattleConfiguration` (`Assets.Code.Combat.BattleConfiguration/LibraryBattleConfigurationTables.cs:41-44`), option conditions in `BattleConfigurationOption.GetIsValidOption` | conditions on the options: region index (`biome_typical_*`), act (`run_boss_is_*`), `escalation_is_*`, biome, infernal torch | bigger and nastier line-ups. Measured over the estate's pools: summed health x1.10 (veteran rooms), x1.20 (champion rooms), x1.46 / x1.55 (hallways, whose apprentice pool is the two-to-three-enemy starters) |
| 2 | **Region index** `BiomeManager.BiomeTypicalCount` | `Assets.Code.Map.Generation.Biome/BiomeManager.cs:123`, `:573-584` (visited TYPICAL biomes) | `EventGeneratingBiome` (the road map) | no stats by itself: selects table options (`ConditionType.BIOME_TYPICAL_COUNT`, `Assets.Code.Condition/ConditionCalculation.cs:538`), run levels (4) and boss modifier rows (6) |
| 3 | **Biome "strength"** `BiomeInstance.m_SiegeStrength` | `Assets.Code.Map.Generation.Biome/BiomeInstance.cs:7`; condition `BIOME_SIEGE_STRENGTH` (`ConditionCalculation.cs:510-517`) | Kingdoms siege (`KingdomSiegeManager.cs:353`: `EventKingdomSiegeCombatStart.Trigger(biomeType, strength)`), clamped 3..10 (`EXCEL\kingdom_rules_data_export.Group.csv`: `m_SiegeStrengthMin/Max`) | no stats: only picks `siege_mashes_easy` (<4), `_normal` (4-6) or `_hard` (>=7) inside `gang_mashes_siege_master` (`EXCEL\battle_configuration_table_data_export.Group.csv:1464-1470`, thresholds `kingdom_siege_attack_data_export.Group.csv:19-43`) |
| 4 | **Run levels** (`LibraryRunLevel`, `RunLevelDefinition`) | `RunDataManager.TryUpdateRunLevel` (`Assets.Code.Run/RunDataManager.cs:246-276`) | region index, profile unlocks | run stats, not actor stats: `battle_modifier_chance` 0 / 0.33 / 0.5 / 0.5 for region 1 / 2 / 3 / 4+, Death's table chance (`EXCEL\run_levels_data_export.Group.csv:53-88`); `base` holds the Expedition's run value bounds |
| 5 | **Run value levels** (`RunValueLevelDefinition`): Kingdoms **escalation**, stagecoach armour/wheels | `RunValues.UpdateRunValueLevels` (`Assets.Code.Run/RunValues.cs:346-380`) on every `SetValue` (`:296-324`); per-tag containers handed out by `RunValues.GetActorData` (`:486`) | `RunValueType.ESCALATION` (`RunValueType.cs:34`), raised by the kingdom events `e_escalation_2/3` on the days of the Kingdoms difficulty (`KingdomEventManager.cs:203-221`) | `EXCEL\kingdom_escalation_levels_data_export.Group.csv`: escalation 1: **heroes** +20% damage, +15% health, +10% bleed/blight/burn resistance (`escalation_1_heroes`, `:35-44`); 2: monsters +25% health, +20% damage (`:80-89`); 3: monsters +50% health, +40% damage (`:130-139`); plus siege/scouting/route run stats |
| 6 | **Ordainment** (`BossModifierDefinition`, 49 rows) | `BossCalculation.RollBossModifier` (`Assets.Code.Boss/BossCalculation.cs:14-60`): rows of the running act's `Boss`, valid for the actor's tags, inside the row's region index range, weight `m_Chance` + run stat `boss_modifier_chance_modifier` | `RunBhv.RunManager.Boss`, `BiomeTypicalCount` | `EXCEL\boss_data_export.Group.csv` + `boss_blessing_data_export.Group.csv`: `<act>_boss_typical_biome_1/2/3` for regular monsters (tags `<act>_blessing`), chance 0 / 0.25-0.5 / 0.33-0.9; `<act>_infernal_flame_lair_boss_biome_N` for tags `biome_boss, boss_hand, meat`, chance 0 (given by an item). Buffs: `brain_`/`lungs_blessing_biome_1/2/3` = +10% health / +20% / +30% health and damage, `generic_blessing_biome_1/2/3` = +20 / 30 / 40% and +1 DOT duration; plus the act's trick (`ActorDataEffects`) |
| 7 | **Battle modifiers** (`BattleModifierDefinition`, 27 rows) | `BattleModifierCalculation.RollBattleModifier` (`Assets.Code.Combat.BattleModifier/BattleModifierCalculation.cs:18-75`): if the config has `m_IsRollBattleModifier`, roll against run stat `battle_modifier_chance`, then a weighted pick among rows whose conditions hold; else the config's `m_BattleModifierOverrideId` **without checking its conditions** (`:65-68`). Attached per actor by tag (`ActorInstance.cs:6572-6584`) | run stat from run levels (4), torch levels (9), biome modifiers; option "always modify" (Expedition only) | `EXCEL\battle_modifier_data_export.Group.csv`: `elite_monsters` +20% health and damage, `hastened` +2 speed, `hale` +15% DOT resistance, `impervious` +25% stun / +15% debuff resistance, `gargantuan` 2 block+ and +33% move resistance, `frenzied`, `prowling`, ... one per fight. Kingdoms: `battlemodifier_<gang>_1..4` (chance 0, forced by the gang's siege configs; `kingdom_battle_modifier_data_export.Group.csv`) |
| 8 | **Arena modifiers** (`ArenaModifierDefinition`) | started by effects with `m_ArenaModifierStartId`, applied in `ActorInstance.SetArenaModifier` (`ActorInstance.cs:4158-4174`) | `actorless_round_start_effects,start_coast_fog` on every `coastal_mash_*` config (25% a round from round 2) | two rows, both for heroes: `coast_fog` (blind/vulnerable, stress), `coast_boss_fog` (`EXCEL\arena_modifier_data_export.Group.csv`). Not a ladder |
| 9 | **Torch levels** (`TorchLevelDefinition`) | `TorchManager.UpdateTorch` (`Assets.Code.Torch/TorchManager.cs:86-145`); group from `GetActiveTorchLevelGroup` (`:147-181`): the act's in an expedition, the gang's `escalation_torch_level_groups[escalation]` in a Kingdom, a stagecoach flame item over both | `RunValueType.TORCH` (0..100) | Kingdoms group (the same five bands for every gang and escalation, `EXCEL\kingdom_torch_levels_data_export.Group.csv`): monsters 76-100: 10% a round to be blinded, 51-75: 5%; 26-50: `battle_modifier_chance` +0.15; 1-25: +0.33 and 5% a round for a strength token; 0: +1.0 and 10%. Heroes: stress resistance +6% / +3%, then death's door resistance -3 / -6 / -9%, blind, crit +3 / +6% |
| 10 | **Biome modifiers / upgrades** (`BiomeModifierDefinition`, `BiomeUpgradeDefinition`) | `BiomeManager.CurrentBiomeStartPreGeneration` (`BiomeManager.cs:1069-1125`), handed out by `BiomeManager.GetActorData` (`:1135`) | chosen at the inn for the next region | Kingdoms `bm_enemy_spd_buff_*`, `bm_enemy_token_buff_*`, inn "ultimate" upgrades for monsters (`EXCEL\kingdom_biome_modifier_data_export`, `kingdom_biome_upgrade_data_export`) |
| 11 | **Kingdom events** | `KingdomEventManager.GetActorData` (`Assets.Code.Kingdom/KingdomEventManager.cs:386`) | daily event roll | `e_coven_buff`, `e_beastmen_buff` (`EXCEL\kingdom_event_data_export.Group.csv`) |
| 12 | **Kingdoms difficulty** (`KingdomDifficulty`: radiant, normal, stygian, blood_moon) | `Assets.Code.Kingdom/KingdomDifficultyValueType.cs` | chosen at kingdom creation | no enemy stats: loss day, roster size, the days of escalation 2 and 3 (15/33 down to 8/25), siege spawn run stats, hero skill sets (`EXCEL\kingdom_difficulty_data_export.Group.csv`) |
| 13 | **External buffs** (`DataExternalBuffs`, `LibraryDataExternalBuffs`) | not a mechanism of its own: the container type every row above uses to carry `Buff` ids (`Assets.Code.Buff/DataExternalBuffs.cs`); buffs become active through `BuffContainer.RefreshActiveBuffs` | | any set can be hung under an actor's data by code |
| 14 | **Escalation conditions inside monster data** | `ConditionType.RUN_VALUE` (`ConditionCalculation.cs:451-454`) | escalation | the Kingdoms factions change skills with it: coven dodge 2 / 1+ / 2+, stress on hit 1 / 1-2 / 2, large blight at 3, sycophant's area vomit above 1, banquet swarms at 3, vermin `_b` ectoplasm at 3 (`EXCEL\effect_data_export.Group.csv:1130-1186, 3571-3610, 14215-14228, 17639-17750`, `courtier_*_data_export`, `dlc_catacombs\catacombs_faction_export`) |
| 15 | **Per-config run stats** (`m_RunDataStatsId`: `no_retreat`, `ambush`) | `RunDataManager.HandleEventBattleConfigurationSelected` (`RunDataManager.cs:118-124`), cleared on the result (`:134-137`) | the config | retreat chance, `battle_modifier_chance` +1 for `ambush` |

There is no "enemy stats by run level" table: `LibraryRunLevel` carries run stats and hero act-outs only, and no
table gives a monster different base stats by region. The only stat variants of a monster are its `_b` champion
twin and class changes (`_ignited`).

## 2. What of that works in the estate

Context: `GameType.KINGDOM`, managers owned by `KingdomBhv` (`Assets.Code.Kingdom/KingdomBhv.cs:647-766`), no map, no
day, own game mode, fights started with a mod combat source. `RunDataManager.InitPreLoad(null, m_BiomeManager)`
(`KingdomBhv.cs:728`): no run manager. `KingdomManager.InitPostLoad` picks a random *released* gang when none was
chosen (`KingdomManager.cs:165-173`); only `beastmen` is flagged `m_IsReleased` in the data
(`EXCEL\kingdom_gang_data_export.Group.csv:4`), so the estate's gang is beastmen (not verified in game).

| mechanism | in the estate | why | what it would need |
|---|---|---|---|
| composition (1) | **works** | pools address leaf tables and configs directly; the checker keeps them condition-free | - |
| region index (2) | always 0 | nothing ever generates a biome | fake visited biomes (`BiomeManager.m_VisitedBiomeInstances`, saved); not used |
| biome strength (3) | no biome: `BiomeManager.GetActiveBiome()` is null, conditions on biome and strength are false | | `EventKingdomSiegeCombatStart.Trigger(biomeType, strength)`: `BiomeManager` is its only listener (`BiomeManager.cs:465-468`), sets a biome instance until the next `EventBattleResult` (`:449-463`). Gives biome conditions, `GameTypeMgr.ActiveBiome` (combat music, ambience, narration) and the siege table choice. Not needed for difficulty (the pools name the siege tables themselves); worth a look for ambience |
| run levels (4) | **silently nothing** | `TryUpdateRunLevel` returns when `m_RunManager == null` (`RunDataManager.cs:248`); Kingdoms' base run stats come from `base_kingdom` instead (`KingdomManager.cs:143-145`, `EXCEL\kingdom_rules_data_export.Group.csv:33-35`) | - |
| escalation (5) | **stuck at 1**: default 1, bounds 1..3 (`EXCEL\kingdom_rules_data_export.Group.csv:33-35`); the events that raise it are scheduled by day and the estate has no day | heroes carry `escalation_1_heroes` in every fight and in the hamlet (not verified in game: `difficulty.state` lists the active levels) | writable with `RunValues.SetValue(RunValueType.ESCALATION, n)`; see 3.6 for why it is not written |
| ordainment (6) | **silently nothing** | `RollBossModifier` needs `RunBhv.RunManager != null` (`BossCalculation.cs:18`); the run manager exists only while an expedition runs (`RunBhv.cs:189`, `:219`), a Kingdom never makes one | a postfix that supplies the row (done) |
| battle modifiers (7) | **work**: rolled for configs with `m_IsRollBattleModifier`, chance from the torch only (0 above 50 light, 0.15, 0.33, 1.0 at 0) | the estate writes DD1's light into `RunValueType.TORCH` | the gangs' forced modifiers apply although their conditions (gang, escalation) do not hold; `coven_boss`, `beastmen_boss` and `courtier_boss` have neither a roll nor an override (`EXCEL\kingdom_battle_configuration_data_export.Group.csv:259-264, 574-579, 1156-1161`), so nothing modifies a gang boss unless forced (done) |
| arena modifiers (8) | work (`start_coast_fog` has no biome condition) | | - |
| torch levels (9) | **work**, with the Kingdoms bands of the beastmen group | a gang always exists | - |
| biome modifiers, kingdom events (10, 11) | nothing | no inn, no day | not used |
| Kingdoms difficulty (12) | `normal` (the default row), irrelevant | | - |
| escalation conditions (14) | all read 1 | | answered per fight (done) |
| `ConditionType.BOSS` | no act: every `run_boss_is_X` is false and every `run_boss_is_not_X` true | `ConditionCalculation.cs:459-465` | the Denial locks would spawn with their re-run buff (+50% health: `EXCEL\boss_blessing_data_export.Group.csv:95-99`, effect `extended_boss_brain_blessing_e`, condition `run_boss_is_not_brain`); answered per fight for the five act bosses (done) |

## 3. The design

### 3.1 DD1: how much a level adds (from the DD1 install)

Monsters (`monsters\<m>\<m>_A|_B|_C\*.info.darkest`, the 74 with all three variants), level 3 and 5 against level 1:

| | health | damage | speed | accuracy | crit | dodge | each resistance |
|---|---|---|---|---|---|---|---|
| level 3 (`_B`) | x1.43 | x1.41 | +1 | +6.3 | +3.9 | +8.5 | +20 |
| level 5 (`_C`) | x1.98 | x1.88 | +2 | +19.9 | +5.9 | +21.0 | +40 |

Whole fights (`dungeons\<d>\<d>.<level>.mash.darkest` of crypts, weald, warrens, cove: weighted mean of summed
health and summed mean skill damage, against level 1 of the same kind):

| | hall | room | boss |
|---|---|---|---|
| level 3 | x1.58 health, x1.32 damage | x1.61, x1.37 | x1.50, x1.39 |
| level 5 | x2.35, x1.87 | x2.25, x1.91 | x2.05, x1.88 |

A fight grows more than a monster because the tables also shift to bigger monsters. A level-1 room holds x1.46 the
health of a level-1 hall (3.90 against 3.25 monsters). The Darkest Dungeon's ordinary fights (the `named ... _mash_`
rows of `darkestdungeon.6.mash.darkest`) average 110 health and 18.2 damage: a level-5 room (118, 21.6).

Heroes (`heroes\<c>\<c>.info.darkest`, 15 classes), weapon/armour level 1..5: damage x1.00 / 1.19 / 1.39 / 1.53 /
1.72, health x1.00 / 1.20 / 1.40 / 1.59 / 1.79, dodge 6.5 +5 a level, crit +1 a level, speed +0/0/1/1/2; skill
accuracy 90.4 +5 a skill level. Gear and skill level 4 open at resolve 3, level 5 at resolve 5
(`upgrades\heroes\*.upgrades.json`). The monsters a party meets have (appearance-weighted) skill accuracy
74.0 / 82.2 / 95.1 and dodge 5.6 / 14.3 / 26.9 at level 1 / 3 / 5, so with the gear of the tier:

* a DD1 hero's hit chance goes 84.8% -> 91.1% (x1.075) at veteran and -> 83.5% (x0.985) at champion: skill levels
  just about answer the monsters' dodge;
* a DD1 monster's hit chance goes 67.5% -> 60.7% (x0.90) at veteran and -> 68.6% (x1.02) at champion: armour dodge
  is ahead at veteran and caught up with at champion.

### 3.2 The estate's heroes (what the scaling assumes)

DD2 has neither accuracy nor a dodge rating, and the estate's hero growth is built by other modules:

* **Resolve** 0..6 with DD1's thresholds and DD1's rule of who may go where (`Estate/Resolve.cs`).
* **Blacksmith** (`Estate/Blacksmith.cs`): weapon and armour levels 1..5 on DD1's terms, giving DD1's own average
  growth: damage +19 / 39 / 53 / 72%, crit +1..4, speed +0/1/1/2, health +20 / 40 / 59 / 79%, and DD1's dodge gain
  as that share less damage taken (-5 / 10 / 15 / 20%).
* **Guild** (`Estate/Guild.cs`): DD2 skill mastery in place of DD1 skill levels: 1 / 2 / 3 mastered skills at
  resolve 1 / 2 / 3, all at resolve 5. A mastered skill deals x1.317 the damage of the plain one (mean of the 193
  `<skill>` / `<skill>_u` pairs in `EXCEL\hero_*_data_export.Group.csv`).
* The escalation-1 buff of section 2 (+20% damage, +15% health), the same at every tier.

So a hero at the cap of the tier, against a fresh one: veteran (resolve 3-4: gear 4, 3 of 5 skills mastered) damage
x1.53 x 1.19 = x1.82, effective health x1.59 / 0.85 = x1.87; champion and darkest (resolve 5-6: gear 5, all
mastered) x1.72 x 1.317 = x2.27 and x1.79 / 0.80 = x2.24. Trinkets, quirks and combat items are DD2's own growth
and are left to DD2's own answers (composition, ordainment tricks, battle modifiers).

### 3.3 The rule

The ratio "what a fight holds / what the party brings" is kept at DD1's value for the tier:

```
health share + 1 = DD1 fight health growth x mastery / DD1 hero hit change / pool composition (health)
damage share + 1 = DD1 fight damage growth x DD1 monster hit change / (1 - armour's damage cut) / pool composition (damage)
```

* "mastery / DD1 hero hit change": the guild's mastery stands where DD1's skill accuracy stood, and DD1's monsters
  answered that accuracy with dodge. DD2 has no dodge, so the difference goes into health (veteran x1.11, champion
  x1.34).
* "DD1 monster hit change / (1 - cut)": the smith gives DD1's armour dodge as damage taken; DD1's monsters answered
  the dodge with accuracy, so the difference goes into damage (veteran x1.06, champion x1.27).
* "pool composition": what the estate's pools add by line-up alone (mean of the four domains): hallway x1.46 health
  x1.42 damage (veteran), x1.55 x1.45 (champion); room x1.10 x1.03, x1.20 x1.09. Bosses: 1 (the same fight).
  The hallway pools jump (apprentice hallways are DD2's two-to-three-enemy starters), so hallway monsters need
  less; a result below 1 is held at 1.
* Speed is DD1's (+1, +2), crit DD1's (+4.6 and +6.5 points over the monsters actually met). DOT damage takes the
  damage share (`dot_effect_value_dealt_multiplier` for bleed, blight, burn: `Assets.Code.Dot/DotContainer.cs:141-142`),
  because the heroes' health grows and a tick would otherwise shrink against it.
* Resistances stay untouched (the `resist` key exists and is 0): DD1's +20 / +40 were matched by +10 points of
  effect chance per hero skill level, which DD2's mastery does not have. Adding them would cost stun, move and DOT
  parties their tools with nothing given back.
* Darkest: every fight is worth a champion room in absolute terms (as in DD1), so the share is whatever lifts the
  cultist pools there. Its bosses take the champion boss numbers without the mastery term: DD2's act bosses are
  already made for mastered heroes, what they have not met is DD1 gear.

### 3.4 The numbers (`tier_rules.<tier>.scaling` in `dungeons.json`)

Shares as stored (0.6 = +60%); "fight" = pool composition x stat share against an apprentice fight of the same
kind; "DD1" = the same fight in DD1 (3.1); "ratio" = fight / hero growth of 3.2, with DD1's own ratio in brackets.

| tier, kind | hp | dmg | dot | speed | crit | fight health, damage | DD1 | health ratio | damage ratio |
|---|---|---|---|---|---|---|---|---|---|
| veteran hallway | 0.20 | 0 | 0 | 1 | 0.05 | x1.75, x1.42 | x1.58, x1.32 | 0.96 (0.96) | 0.76 (0.75) |
| veteran room | 0.60 | 0.40 | 0.40 | 1 | 0.05 | x1.76, x1.44 | x1.61, x1.37 | 0.97 (0.98) | 0.77 (0.77) |
| veteran boss | 0.65 | 0.45 | 0.45 | 1 | 0.05 | x1.65, x1.45 | x1.50, x1.39 | 0.91 (0.91) | 0.78 (0.79) |
| champion hallway | 1.05 | 0.65 | 0.65 | 2 | 0.065 | x3.18, x2.39 | x2.35, x1.87 | 1.40 (1.39) | 1.07 (1.06) |
| champion room | 1.50 | 1.25 | 1.25 | 2 | 0.065 | x3.00, x2.45 | x2.25, x1.91 | 1.32 (1.33) | 1.09 (1.09) |
| champion boss | 1.75 | 1.40 | 1.40 | 2 | 0.065 | x2.75, x2.40 | x2.05, x1.88 | 1.21 (1.21) | 1.07 (1.07) |
| darkest hallway | 1.40 | 1.10 | 1.10 | 2 | 0.065 | a champion room's worth | | 1.32 | 1.09 |
| darkest room | 1.30 | 1.50 | 1.50 | 2 | 0.065 | a champion room's worth | | 1.32 | 1.09 |
| darkest boss | 1.10 | 1.40 | 1.40 | 2 | 0.065 | x2.10, x2.40 on the shipped boss | | 1.22 on DD2's own balance | 1.07 |

Reading: against a party geared for the tier, a veteran fight is a little *easier* than the apprentice fight was
for a fresh party (DD1 has the same dip: gear 4 arrives with resolve 3), and a champion fight a third longer with
slightly harder hits. The fights are bigger than DD1's (x1.75 against x1.58) exactly by what mastery adds on the
heroes' side.

Where monsters and heroes drift apart:

* **A party below the tier's gear.** DD1 lets under-geared heroes in, and so does the estate. Gear 3 with two
  masteries in a veteran room: ratios 1.12 / 0.92 (DD1 with the same gear: 1.14 / 0.95). No gear and no mastery in a
  champion room: 3.0 / 2.45, not winnable, as in DD1.
* **DOT heroes lag from veteran on.** The smith's weapon adds `health_damage_dealt_percent`, which bleed, blight and
  burn ignore, while enemy health goes to x1.6 and x2.5. Unless the weapon level also gives
  `sub_stat,dot_effect_value_dealt_multiplier,<bleed|blight|burn>,<damage share>` (the stat the enemies get here),
  a Plague Doctor's or Flagellant's ticks are worth half at champion. Not mine to change; flagged for the smith.
* **Apprentice is easier than DD2's first region** by the escalation-1 buff (enemy health effectively /1.2, their
  damage /1.15), which suits level-0 heroes without mastery and the full-size lair bosses of the first boss quests.
  If it turns out too easy, shares can be given to the apprentice blocks; they are 0, not special-cased.
* **Stress is not scaled** (DD2 has no "stress dealt" stat on actors). Stress pressure per round is the same at
  every tier; fights last a third longer at champion, so total stress rises by that much, less than in DD1.
* **Veteran hallways with an ordained monster** (below): the blessing's +20-30% damage sits on a 0 damage share and
  cannot be taken off, so that monster hits x1.2-1.3 where its neighbours hit x1.0.

### 3.5 The other three mechanisms

* **Ordainment** (`tiers.<tier>.ordain`, `bosses[].tiers.<tier>.boss_modifier`). Each enemy created during the
  fight is offered the dungeon's row at its chance (Ruins 0.25 / 0.33 with the `brain` rows, Weald 0.33 / 0.75
  `lungs`, Cove 0.4 / 0.8 `eyes`, Warrens 0.5 / 0.9 `arms`, Darkest Dungeon 0.9 `final`), valid only for actors
  with the row's tags (`BossModifierDefinition.GetIsValidForActorClass`). A lair boss (`biome_boss`), its hands and
  meat get `<act>_infernal_flame_lair_boss_biome_2/3` outright. The health and damage the row's buffs carry are
  taken off that actor's stat share (never below 0), so the tier's total holds whether or not the roll hit, and the
  blessing adds its trick (resistances, longer DOTs, token theft, ...) and the game's own ordained look and sound.
* **Gang bosses** (Mother of Threads, Meat Hook, Archduke; no blessing tags). `battle_modifier`
  (`battlemodifier_<gang>_2/3`) is forced for the fight and `external_buffs` (the blessing set of the tier) hangs
  under each of the boss's listed actors, netted like a row.
* **Escalation for conditions** (`tier_rules.<tier>.escalation`: 1 / 2 / 3 / 3). While the plan is live, every
  `run_value escalation` condition is answered with it. What changes: the Kingdoms factions fight with their
  escalation-2/3 skills (row 14 of section 1), an appended Death is `death_e2/e3`, and the loot tables move from
  escalation-1 to escalation-2+ contents (section 4).
* **Act of an act boss** (`bosses[].run_boss`, the five Darkest Dungeon bosses). `boss` conditions are answered
  with it, so the Shackles of Denial are fought as in the Denial act, without the re-run health.

Not used, and why:

* **Escorts** (`escort_tables`). DD1's boss room holds the boss; what a level adds is stats. A lair wave in front
  would add a room's worth of health (x1.7 to x2.5 at these tiers) to a fight that is already DD1-proportional,
  through a chain nobody has played in the estate (`m_IsNextBattleOptional` lets the player stop before the boss).
  `DungeonContent.PickBoss` starts the boss alone at every tier; the data and the checker's rules for it stay.
* **Tier-based battle modifier chance.** Kingdoms itself drives the chance by light only and gives stats by tier;
  the estate now does the same with DD1's numbers. Darkness is the dial, as in DD1.
* **A synthetic biome**, see section 2.

### 3.6 Why the escalation run value is not written

Setting `RunValueType.ESCALATION` to 2 or 3 would be the native switch, and it does three things at once
(section 1, row 5): monsters get +25/+20% or +50/+40%, the loot and skill conditions follow, and the **heroes lose
their escalation-1 buff** (15% of maximum health, 20% damage). Switched around each fight, a hero's health bar
would read 40/46 in the corridor and 35/40 in the fight; switched per expedition, the hamlet and the dungeon would
disagree. The value is also saved with the Kingdom (`RunValues.SaveToJson`, `RunValues.cs:529-539`) and two loops
re-set every run value from what `GetValue` returns (`:131-140`, `:392-428`), so an override of the getter would
leak into the save. Hence: the value stays 1 for good, the stats come from the mod's container, and only the
*conditions* are answered with the tier, in `ConditionCalculation.GetGameValueForConditionType`.

## 4. Risks

* **Persistence.** Nothing the scaling touches is saved. No run value is written (`kingdom.json` keeps escalation 1
  and the torch the dungeon set). The stats containers hang under enemies' `BattleActorData`; enemies are not in
  the roster, are not written by the estate's snapshots (vanilla per-turn snapshots are suppressed) and are removed
  with the battle. A saved expedition resumes with no plan; the next fight makes its own. `boss_modifier` is a
  field of a saved actor (`ActorInstance.cs:956-959`), but only enemies ever get one.
* **Leaks into the next fight.** A plan lives from `Difficulty.Apply` to `Difficulty.Clear`, is replaced by the next
  `Apply`, and expires by itself once the hub mode is back for more than 5 frames (`Difficulty.Live`); all four
  patches do nothing without a live plan or outside an estate session. A fight started without `Apply` (the
  `estate.battle` bridge command) runs unscaled. A plan set with the `difficulty.apply` bridge command is sticky on
  purpose: it stays until `difficulty.clear` or the next `Apply`.
* **Heroes.** Never touched: only actors joining team 1 are fitted, and no hero class has a blessing tag.
* **Loot.** (a) Loot is rolled when the `BattleResult` is built (`Assets.Code.Combat/BattleResult.cs:194`) and, for
  fights with an appended or chained configuration, already when the `CombatScenarioData` is made
  (`CombatScenarioData.cs:248-258`): `Apply` must come before `EstateSession.StartBattle`, and `Clear` after the
  results screen, which is where the snippets put them. (b) With the tier's escalation the Kingdoms tables change
  contents: trinket drops lose `TRINKETS_COMMODITY_COMMON` and gain epic and hero trinkets from veteran on
  (`EXCEL\loot_data_export_COMBATS.Group.csv:209-232`, `loot_data_export_TRINKETS.Group.csv:1-16`), siege fights
  pay `siege_normal_rewards` / `siege_hard_rewards` instead of `siege_easy_rewards` (`:707-714`). This is DD2's
  counterpart of DD1's better trinkets in harder dungeons and is intended; amounts of relics and baubles do not
  scale and remain the quest reward's business. (c) The gang boss configs carry no loot table at all.
  (d) Stronger monsters drop nothing extra by themselves (`m_DeathLootIds` are per class).
* **Act bosses under scaled health** (not verified). Their scripts were never run with multiplied health; the
  game's own `extended_brain_max_hp_buff` multiplies the locks the same way, the other four have no precedent.
  If a phase change misbehaves, set `tier_rules.darkest.scaling.boss.hp` to 0.
* **A patch that fails to apply** disables one mechanism only; `difficulty.state` lists the four with `applied`.

## 5. Where the code is

| file | what |
|---|---|
| `src\DD2Estate\Dd2\Difficulty.cs` | `Difficulty.Apply(dungeonId, tierName, kind, bossId)`, `Difficulty.Clear()`, the plan, the per-enemy stats container (`Fit`), the answers the patches ask for |
| `src\DD2Estate\Dd2\DifficultyPatches.cs` | `EnemiesTakeTheTierStats` (postfix `ActorInstance.OnAddedToTeam`), `TierOrdainsMonsters` (postfix `BossCalculation.RollBossModifier`), `TierForcesTheGangModifier` (postfix `BattleModifierCalculation.RollBattleModifier`), `TierAnswersGameConditions` (postfix private `ConditionCalculation.GetGameValueForConditionType`) |
| `src\DD2Estate\Dd2\DifficultyDev.cs` | bridge commands `difficulty.state`, `difficulty.apply`, `difficulty.clear`, `difficulty.enemies` |
| `src\DD2Estate\Dungeon\DungeonContent.cs` | `Scaling(tier, kind)`, `Escalation(tier)`, `Ordain(dungeon, tier, out row, out chance)`, `Boss(dungeon, boss, tier)`, `TierName(dungeon, dd1Tier)`, `ResolveTier`; pools now also draw the DLC blocks (`dlc_extras`) when their tables are loaded, with the block's own arenas, and never a fight that fields an actor of a DLC the player does not own; `PickBoss` also finds wanderers and `configs` lists and starts the boss alone at every tier |
| `src\DD2Estate\Data\dungeons.json` | `tier_rules.<tier>.scaling.{hallway,room,boss}` with `hp, dmg, dot, speed, crit` (and optional `resist`); `run_boss` on the five act bosses |
| `tools\check_content.py` | validates the scaling blocks, the escalation and `run_boss`; `--difficulty [--dd1 <dir>]` derives the shares from both installs |

The stats reach an enemy as one `ActorDataStats` built from the text DD2's own tables use, e.g. champion room:
`multiply_stat,health_max,1.5` / `add_stat,health_damage_dealt_percent,1.25` /
`sub_stat,dot_effect_value_dealt_multiplier,bleed,1.25` (blight, burn) / `add_stat,speed,2` /
`add_stat,crit_chance,0.065`, added under `ActorInstance.BattleActorData` (source COMBAT), followed by
`ActorInstance.RefreshStats()` so that health keeps its share of the new maximum (`ActorInstance.cs:4870-4899`).
The game empties that container itself (`OnRemovedFromTeam`, `:6599-6602`; `OnBattleEnd`, `:4120-4134`).

## 6. Call sites (files I do not own)

`Dungeon\DungeonRun.cs`, `StartFight`: the plan must exist before the scenario is made.

```csharp
        private void StartFight(EncounterSlot slot)
        {
            string config, arena;
            var boss = slot.Kind == EncounterKind.Boss ? _quest?.Boss : null;
            if (boss != null) DungeonContent.PickBoss(_map.DungeonId, boss, _quest.Tier, out config, out arena);
            else DungeonContent.Pick(_map.DungeonId, slot, _rng, out config, out arena);
            if (config == null)
            {
                Plugin.Log.LogError("Dungeon: no fight available for " + slot.Kind + " tier " + slot.Tier + "; treated as won");
                Enqueue(_x.ResolveFight(FightOutcome.Won));
                return;
            }
            _fighting = true;
            _fightOutcome = FightOutcome.Retreated;
            SetFrozen(true);
            // The quest names its tier; a free expedition has the DD1 difficulty of the slot.
            Difficulty.Apply(_map.DungeonId, _quest?.Tier ?? DungeonContent.TierName(_map.DungeonId, slot.Tier), slot.Kind, boss);
            EstateSession.StartBattle(config, arena);
        }
```

`Dungeon\DungeonRun.cs`, `OnReturnedFromFight`, right after `_fighting = false;` (before the save):

```csharp
            _fighting = false;
            Difficulty.Clear();
```

`Dd2\EstateSession.cs`, `Tick`, in the block that ends the session (next to `DungeonRun.Current?.Dispose();`):

```csharp
                Difficulty.Clear();
```

Optional, `Dev\DevCommands.cs`, `estate.boss`: add
`Difficulty.Apply((string)o["dungeon"], (string)o["tier"] ?? "apprentice", DD2Estate.Core.EncounterKind.Boss, (string)o["boss"]);`
before `EstateSession.StartBattle(config, arena);` so the boss smoke test fights the tier it names.
`EstateSession.StartBattle` itself needs no change.

## 7. Test plan (bridge)

`run` = `python tools/bridge.py run`. From the hamlet of an estate session, with a party.

1. `run difficulty.state`. Expect `patches`: four entries, all `"applied": true`. `engine`: `gameType` kingdom,
   `escalationRunValue` 1, `runValueLevels` containing `escalation_1_heroes`, `gang` `beastmen`,
   `expeditionRunning` false, `activeBiome` null, `battleModifierChance` 0 at full light. `plan` null.
2. Apprentice: `run difficulty.apply dungeon=crypts tier=apprentice kind=hallway`, then
   `run estate.battle config=fanatic_mash_201 arena=combat_arena_forest_dungeon_interior`; when the fight is up,
   `run difficulty.enemies`. Expect four enemies, `ordained` null, `tierShares` all 0:

   | cls | baseHp | maxHp | hpMultiplier | damageMultiplier | speed |
   |---|---|---|---|---|---|
   | `fanatic_whipper` | 16 | 16 | 1 | 1 | 2 |
   | `fanatic_flayer` | 12 | 12 | 1 | 1 | 3 |
   | `shared_lost_soul_widow` | 13 | 13 | 1 | 1 | 3 |
   | `fanatic_immolatist` | 16 | 16 | 1 | 1 | 7 |

   End it (`python tools/bridge.py invoke '$CombatBhv' ForceEndCombat true`, take the loot), then
   `run difficulty.clear`.
3. Veteran: the same with `tier=veteran`. `difficulty.state` during the fight: `plan.live` true,
   `escalationForConditions` 2, `escalationRunValue` still 1. `difficulty.enemies`: maxHp 19, 14, 16, 19
   (`hpMultiplier` 1.2), `damageMultiplier` 1, `dotMultiplier` 1, speed 3, 4, 4, 8, `crit` 0.05. About one enemy
   in four shows `ordained: brain_boss_typical_biome_2`; for it `tierShares.hp` is 0, `hpSharesBySource` shows
   0.2 under `boss` instead of `combat`, maxHp is the same, and `damageMultiplier` is 1.2.
4. Champion: `tier=champion`: maxHp 33, 25, 27, 33 (`hpMultiplier` 2.05), `damageMultiplier` 1.65,
   `dotMultiplier` 1.65, speed 4, 5, 5, 9, `crit` 0.065, `escalationForConditions` 3. One in three is
   `ordained: brain_boss_typical_biome_3` with `tierShares` hp 0.75, dmg 0.35 and the same totals.
5. Rooms, for the other column: `kind=room` gives 26, 19, 21, 26 with damage 1.4 at veteran and 40, 30, 32 or 33,
   40 with damage 2.25 at champion.
6. A boss: `run difficulty.apply dungeon=crypts tier=champion kind=boss boss=librarian`, `run estate.boss
   dungeon=crypts boss=librarian tier=champion`: the Librarian `ordained: brain_infernal_flame_lair_boss_biome_3`,
   `hpMultiplier` 2.75 (0.3 `boss` + 1.45 `combat`), `damageMultiplier` 2.4; the book stacks x2.75 without a row.
   A gang boss: `dungeon=weald tier=veteran kind=boss boss=mother_of_threads` with `estate.boss`:
   `battleModifier: battlemodifier_coven_2`, `hpMultiplier` 1.65, `damageMultiplier` 1.45.
7. Shackles: `dungeon=darkestdungeon tier=darkest kind=boss boss=shackles_of_denial`: each lock `baseHp` 55,
   `maxHp` 116 (x2.1); 143 would mean the re-run buff is still on.
8. After the snippets are in: `run quests.embark index=N` on a veteran quest, walk into a hallway fight,
   `run difficulty.enemies` shows the plan line of that dungeon without any `difficulty.apply`; back in the
   corridor `run difficulty.state` shows `plan` null.

Hand check of any enemy: `maxHp` = round(`baseHp` x (1 + sum of `hpSharesBySource`)).

## 8. Not verified without the game

* that the four patches apply on this build (signatures are from the decompile; the private target is patched by name);
* that an `ActorDataStats` added under `BattleActorData` in the `OnAddedToTeam` postfix is counted and that
  `RefreshStats()` there leaves a fresh enemy at full health of the new maximum (read from the code path the game
  uses for `elite_monsters`; the hamlet's smith uses the same container type on heroes);
* summons, later waves and class changes (`fanatic_librarian_ignited`, corpses) under the container; corpses keep
  the health share on purpose (DD1's corpses scale too);
* that heroes really carry `escalation_1_heroes` and that the gang is beastmen (`difficulty.state` shows both);
* the ordained look, sound and tooltip on monsters ordained by the postfix;
* forced `battlemodifier_<gang>_N` on a gang boss (its effects were written for siege line-ups);
* the Kingdoms factions' escalation-2/3 skills and the loot contents under the answered escalation;
* the five act bosses with scaled health, and the Shackles without the re-run buff;
* the balance itself: the ratios of 3.4 are arithmetic on data, no fight was played. DD2 damage has parts the
  shares do not reach (stress, tokens, crit effects), so real fights may sit a little below the table.
