# DD2 recon: combat entry/exit and hero instances

Target: Darkest Dungeon II v2.04.85095 (Unity 2022.3.62f2, Mono). Read-only analysis of the decompiled
`IronCrown` assembly plus the shipped CSV data. Nothing here was run in game.

Conventions used in this file

- Code references are `folder/file.cs:line`, relative to
  `D:\Mods\DD2\dd2-estate-mod\_ref\dd2-decomp\IronCrown\` (folder name = namespace).
  `Assembly-CSharp/...` is relative to `_ref\dd2-decomp\`.
- CSV references are relative to
  `E:\Steam\steamapps\common\Darkest Dungeon® II\Darkest Dungeon II_Data\StreamingAssets\Excel\`.
- "not verified" marks anything inferred rather than read (mostly serialized prefab/scene content,
  which is not visible in decompiled code).
- Singleton access patterns seen everywhere: `Singleton<T>.Instance` (plain C# systems, e.g.
  `GameTypeMgr`, `GameModeMgr`) and `SingletonMonoBehaviour<T>.Instance` (scene systems, e.g.
  `CombatBhv`, `RunBhv`, `KingdomBhv`). Libraries are
  `SingletonMonoBehaviour<Library<TKey, TElement>>.Instance`.

Quick orientation (the whole pipeline in one paragraph)

A battle is not started by a method call with arguments. It is started by (1) building an
`Assets.Code.Combat.CombatScenarioData`, (2) handing it to
`Singleton<GameTypeMgr>.Instance.SetCombatScenario(data, isLoad: true)` and (3) switching the game
mode with `Singleton<GameModeMgr>.Instance.SetMode(GameModeType.COMBAT, isLoad: false)`. Entering
the COMBAT mode makes `CombatBhv.OnGameModeEnterStart` call `CombatBhv.StartCombat()`, which builds
both teams and constructs `new Battle(BattleParameters)`. The battle ends by raising
`EventBattleResult`; the presentation layer then switches to `GameModeType.RESULTS` (loot window) and
`CombatResultsPresentationBhv.OnTimelineComplete()` picks the mode to return to.

---

## 1. Battle start

### 1.1 Lowest common entry point

`Assets.Code.Combat.CombatBhv.StartCombat()` — `Assets.Code.Combat/CombatBhv.cs:284`
(`public void StartCombat()`, also tagged `[ContextMenu("Start Combat")]`).

- Its only caller is `CombatBhv.OnGameModeEnterStart(GameModeType enteringGameMode, bool isLoad)`
  (`Assets.Code.Combat/CombatBhv.cs:258-272`): when `enteringGameMode == GameModeType.COMBAT`, it calls
  `SaveUtils.TryLoadCombat()` if `isLoad`, otherwise `StartCombat()`, then `PostCreate()`
  (`:458-462`, which starts the `SpawnActors()` coroutine and adds listeners).
  (Verified by grep: the only other `StartCombat()` call site is the unrelated private
  `TriggerCombatBhv.StartCombat()`.)
- The only non-load construction of a battle is `m_Battle = new Battle(battleParameters);`
  at `Assets.Code.Combat/CombatBhv.cs:448`
  (`public Battle(BattleParameters battleParameters)`, `Assets.Code.Combat/Battle.cs:78-89`).
- `StartCombat()` takes no arguments. Everything comes from global state:
  `Singleton<GameTypeMgr>.Instance.CombatScenarioData` (`Assets.Code.Combat/CombatBhv.cs:297-301`) and
  `Singleton<GameTypeMgr>.Instance.RosterManager` (`:390`).

So the practical "API" for starting a fight is the three-step sequence used verbatim by the two
off-road callers (Kingdom map siege and camp ambush):

```csharp
// Assets.Code.Kingdom/KingdomSiegeManager.cs:356-358 and Assets.Code.Inn/InnBhv.cs:372-374
CombatScenarioData combatScenarioData = new CombatScenarioData(...);
Singleton<GameTypeMgr>.Instance.SetCombatScenario(combatScenarioData, isLoad: true);
Singleton<GameModeMgr>.Instance.SetMode(GameModeType.COMBAT, isLoad: false);
```

- `GameTypeMgr.SetCombatScenario(CombatScenarioData combatScenarioData, bool isLoad)` —
  `Assets.Code.Game/GameTypeMgr.cs:428-435`. Stores it in `m_CurrentCombatScenarioData` and, when
  `isLoad` is true, calls `CombatScenarioData.Load()` (scene loading, see section 3). Note that here
  `isLoad` means "load the scenes now", not "load from save".
- `GameModeMgr.SetMode(GameModeType mode, bool isLoad, SceneTransition transitionOverride = null,
  bool? showTransitionThrobberOverride = null, bool unloadEverything = false,
  bool isGameOverTransition = false)` — `Assets.Code.Game/GameModeMgr.cs:256-269`.
- `GameTypeMgr.CombatScenarioData` (getter, `Assets.Code.Game/GameTypeMgr.cs:126-137`) returns the
  current scenario only once `IsLoadStarted` is true.

### 1.2 What `StartCombat()` needs (arguments/state)

All line numbers in `Assets.Code.Combat/CombatBhv.cs`.

| Input | Where it comes from | Lines |
|---|---|---|
| Battle configuration | `CombatScenarioData.CurrentBattleConfiguration` (`BattleConfigurationDefinition`) | 297-299 |
| Additional enemies | `CombatScenarioData.AdditionalBattleConfiguration` (rolled in the scenario constructor) | 309-313 |
| Number of teams | `SingletonMonoBehaviour<ArenaBhv>.Instance.GetNumberOfTeams()` (count of distinct `TeamLayout.TeamIndex` in the `combat` scene) | 314 |
| Team size | hard-coded `teamParameters.m_TeamSize = 4` | 318 |
| Enemy team (index 1) | `battleConfigurationDefinition.m_EnemyActors` via `CreateActorOnTeamParameters(..., useExistingActorIfExists: false, allowOversizeTeam: false)`; ids that do not fit go to `m_AdditionalActorClassIds` (summon queue) | 393-396, 482-530 |
| Player team (index 0) | 1) `battleConfigurationDefinition.m_PlayerActors` (hero-story fights), 2) Kingdom siege defenders (`CombatScenarioData.GetKingdomSiegeDefenseActorClassIds()`), 3) fallback: `Singleton<GameTypeMgr>.Instance.RosterManager.GetActorGuids(RosterStatusType.PARTY)` | 378-392 |
| Controllers | `BattleTeams.GetDefaultActorControllerType(i)`: team 0 = `ActorControllerType.INPUT`, others = `RANDOM` (`Assets.Code.Combat/BattleTeams.cs:697-704`) | 356 |
| Summon controllers | `battleConfigurationDefinition.PlayerSummonControllerConfiguration` / `EnemySummonControllerConfiguration` | 334-337 |
| Config effects | `battleConfigurationDefinition.HeroEffects` / `EnemyEffects` wrapped as `SourceDefinition<EffectDefinition>(item, SourceType.COMBAT, id)` | 415-421 |
| Story effects | `CombatScenarioData.m_StoryChoice.StoryDataEffects` | 422-441 |
| Ambush | `parameters2.m_AmbushTeamIndex = TextBasedEditorPrefs.GetInt(TextBasedEditorPrefsBaseType.BATTLE_TEST_AMBUSH_TEAM_INDEX)` — debug pref only | 444-445 |
| Battle modifier | `BattleModifierCalculation.RollBattleModifier(battleConfigurationDefinition)` | 446 |
| Result | `m_Battle = new Battle(new BattleParameters(parameters, parameters2, battleModifierDefinition)); m_Arena = new Arena();` | 447-449 |

Notes

- If team 0 ends up with no guids the team is skipped entirely
  (`if (teamParameters.m_Guids.Count <= 0) continue;`, `:411-414`), so an empty party produces a
  one-team battle rather than an error.
- Enemy *positions* are simply list order: `Team.AddActor(actor, m_Actors.Count, ...)`
  (`Assets.Code.Combat/Team.cs:162-165`), optionally shuffled when
  `BattleConfigurationDefinition.m_EnemyRandomOrder` is set (`CombatBhv.cs:487-490`). Actor size
  (`ActorDataClass.m_Size`) consumes team room (`CombatBhv.cs:493-520`).
- "Ambush" has no gameplay flag in normal play. `CombatSource.AMBUSH` only differs in its static
  properties (`Assets.Code.Combat/CombatSource.cs:44`) and shows a pop text
  (`Assets.Code.Map.Triggers/TriggerCombatBhv.cs:118-121`). The real surprise mechanic is
  `BattleTurnOrder.Parameters.m_AmbushTeamIndex` (`Assets.Code.Combat/BattleTurnOrder.cs:24-27`):
  the ambushed team gets no rolled turns in round 1 (`:336-343`), and it is only ever fed from the
  editor pref (`CombatBhv.cs:445`). Data-side there is also an `ambush` `RunDataStats` element
  (`combat_rules_data_export.Group.csv:44-48`: `retreat_chance` x-1, `battle_modifier_chance` +1).

### 1.3 `CombatScenarioData` (the real argument object)

`Assets.Code.Combat/CombatScenarioData.cs`. Public constructors:

| Signature | Lines | Used by |
|---|---|---|
| `CombatScenarioData(string battleConfigurationId, string backgroundScene, CombatSource loadedFrom)` | 210-213 | road trigger, debug. Reads `Singleton<GameTypeMgr>.Instance.RosterManager.GetActorGuids(RosterStatusType.PARTY)` |
| `CombatScenarioData(string battleConfigurationId, string backgroundScene, CombatSource loadedFrom, IReadOnlyList<uint> startingPartyActorGuids)` | 215-218 | camp ambush |
| `CombatScenarioData(string battleConfigurationId, string backgroundScene, CombatSource combatSource, string nodeSubType, ActorInstance storyActor, StoryChoiceDefinition storyChoice, int storyRetryCount, uint biomeKillContractGuid)` | 187-196 | story nodes, hero-story retry |
| `CombatScenarioData(string battleConfigurationId, string backgroundScene, IReadOnlyList<uint> partyActorGuids, uint kingdomSiegeGuid, KingdomSiegeDefenseDefinition kingdomSiegeDefenseDefinition, IReadOnlyList<uint> kingdomSiegeDefenseActorGuids)` | 198-208 | Kingdom sieges (forces `CombatSource.KINGDOM_INN_SIEGED`) |
| `CombatScenarioData(List<BattleConfigurationDefinition> battleConfigurations, int currentBattleConfigurationIndex, string backgroundScene, IReadOnlyList<uint> partyActorGuids, CombatSource loadedFrom)` | 220-223 | internal chain; lets the caller pass an explicit sequence |
| `CombatScenarioData(IReadOnlyList<BattleConfigurationDefinition> battleConfigurations, int currentBattleConfigurationIndex, IReadOnlyList<LootPreRoll> sequenceLootPreRolls, CombatScenarioData sourceCombatScenarioData)` | 225-229 | next wave of a sequence |

What the core constructor (`:231-285`) does:

- Copies the sequence; rolls `m_AdditionalBattleConfiguration =
  battleConfigurationDefinition.RollAdditionalBattleConfiguration()` (`:248-249`).
- Loot pre-rolls for multi-battle sequences (`GatherSequenceLootPreRolls`, `:471-487`).
- Arena: `m_BackgroundSceneName = backgroundScene` (`:260`), then **the config's
  `m_BackgroundSceneOverride` wins if present** (`:266-269`), then the debug pref
  `BATTLE_TEST_COMBAT_ARENA` wins over everything (`:270-274`).
- `m_IsExpeditionBoss` is set if any enemy `ActorDataClass.IsExpeditionBoss` (`:275-282`).
- String-id constructors go through `GatherBattleConfigurationSequence` (`:451-469`) which looks the
  id up in `Library<string, BattleConfigurationDefinition>` and then recursively appends
  `battleConfigurationToAdd.RollNextBattleConfiguration()` (`AddBattleConfigurationToSequence`,
  `:489-497`). That is how lair waves are built.

`CombatSource` (`Assets.Code.Combat/CombatSource.cs:7-78`) is a `CustomEnum` whose instances carry
`m_CanReturnToDriving`, `m_IsEndOnPartyDead`, `m_CheckForTestBattleConfiguration`, `m_UseLootTables`,
`m_UseStoryLootTables`, `m_NodeType`. Instances: `DEBUG, UNSET, BARRICADE, DUNGEON, CULTIST,
CULTIST_MOUNTAIN_01, CULTIST_MOUNTAIN_02, STORY_ASSIST, STORY_RESIST, STORY_COSMIC, STORY_HERO,
GUARDIAN, OASIS, CREATURE_DEN, CATHEDRAL, GAUNT_CHIRURGEON, REPAIR, AMBUSH, WARLORD,
KINGDOM_INN_SIEGED, KINGDOM_BOSS, BEASTMEN_ALPHA, CAMP_AMBUSH` (`:10-54`). The constructor is
private.

`BattleConfigurationDefinition` (`Assets.Code.Combat.BattleConfiguration/BattleConfigurationDefinition.cs:18`)
fields the battle reads: `m_EnemyActors` (`:36`), `m_EnemyRandomOrder` (`:38`), `m_PlayerActors`
(`:32`), `m_ResultActors` (`:40`), `m_BackgroundSceneOverride` (`:50`), `m_TorchOverride` (`:52`),
`m_BattleModifierOverrideId` (`:54`), `m_IsRollBattleModifier` (`:58`), `m_RollBattleModifierTag`
(`:60`), `m_RoundLimit` (`:30`), `m_RunLimit` (`:28`), `m_EndAtMaxStress` (`:74`),
`m_EndIsAlwaysComplete` (`:76`), `m_EndConditionsIsComplete` (`:84`), `m_IsNextBattleOptional`
(`:92`), `m_IsKeepCombatContainers` (`:64`), `m_HasEndSequence` (`:112`), `m_endBossCinematicName`
(`:116`), `HeroEffects/EnemyEffects/ActorlessEffects/ActorlessRoundStartEffects` (`:126-132`),
`RunDataStats` (`:134`), `GetLootTables(CombatSource, bool isComplete)` (`:420-432`).
CSV shape (`battle_configuration_data_export.Group.csv:1-9`):

```
element_start,city_dungeon_1a,BattleConfiguration
m_Chance,1,
m_BackgroundSceneOverride,combat_arena_city_dungeon_exterior,
m_NextBattleConfigurationTableId,biome_city_dungeon_round2,
m_IsNextBattleOptional,True,
m_EnemyActors,fanatic_whipper,fanatic_flayer,shared_lost_soul_widow,fanatic_immolatist,
m_CompleteLootTables,lair_prewave_1_rewards_all,
m_IsRollBattleModifier,True,
element_end
```

Battle modifiers: `BattleModifierCalculation.RollBattleModifier(BattleConfigurationDefinition)`
(`Assets.Code.Combat.BattleModifier/BattleModifierCalculation.cs:18-75`) — pref override, else if
`m_IsRollBattleModifier` roll against `RunStatType.BATTLE_MODIFIER_CHANCE` from
`Singleton<GameTypeMgr>.Instance.RunDataManager` (`:35`), else `BattleModifierOverride`.

### 1.4 All call paths that begin a battle

| # | Path | Where | What it builds |
|---|---|---|---|
| 1 | Road fight / lair / guardian / road ambush / boss node (`TriggerCombatBhv`, a `TriggerBhv` on a map prefab) | `Assets.Code.Map.Triggers/TriggerCombatBhv.cs:114-193` | Config id from serialized `m_battleConfigurationId` or rolled from `m_battleConfigurationTableId` via `LibraryBattleConfigurationTables.RollBattleConfiguration(id, RandomIdentifier.COMBAT, list)` (`:129-140`); `new CombatScenarioData(text, m_combatArenaBgOverride or null, m_combatSource)` (`:159-166`); `SetBiomeKillContract` from parent `TileNodeBhv` (`:169-174`); `SetMode(COMBAT)` or, with `m_combatIntroScene`, `SetMode(REALTIME_CINEMATIC)` (`:176-184`). The scenario is handed to `GameTypeMgr` later, in `OnGameModeExitComplete(DRIVING)` (`:195-207`). Which prefabs carry which source/table is serialized data — not verified. |
| 2 | Kingdom siege on arriving at a besieged inn | same file `:143-158` (`m_combatSource == CombatSource.KINGDOM_INN_SIEGED`) | 6-arg siege constructor with `KingdomMapCellInn.ActorGuids` |
| 3 | Kingdom map siege defence (started from the inn/map screen) | `Assets.Code.Kingdom/KingdomSiegeManager.cs:334-359` (`public void StartKingdomMapSiegeCombat(KingdomSiegeInstance, KingdomMapCellInn)`) | table `KingdomRules.m_SiegeManualBattleConfigTableId`, arena `BiomeData.GetManualSiegeCombatArena()`, `EventKingdomSiegeCombatStart.Trigger(biomeType, strength)` |
| 4 | Kingdom camp ambush | `Assets.Code.Inn/InnBhv.cs:340-375` | table `KingdomRules.m_CampAmbushBattleConfigTableId`, arena `BiomeData.GetCampAmbushCombatArena()`, `EventCampAmbushStart.Trigger(biomeType)`, `CombatSource.CAMP_AMBUSH` |
| 5 | Story nodes (resistance, cultist, creature den, chirurgeon, hero shrine, oasis ...) | `Assets.Code.Story/StoryCalculation.cs:427-487` | `SetMode(COMBAT)` first (`:433`), then the 8-arg story constructor (`:486-487`); table fallback `BiomeDefinition.m_StoryDefaultBattleConfigurationTable` (`:464-469`) |
| 6 | Boss intro cinematic then fight | `Assets.Code.RealtimeCinematic/RealtimeCinematicController.cs:104-118` | on exit of `REALTIME_CINEMATIC` calls `Singleton<GameTypeMgr>.Instance.LoadCombatScenario()`; after the timeline `SetMode(COMBAT)` |
| 7 | Next battle of a sequence (lair waves) | `Assets.Code.Combat.Presentation/CombatPresentationBhv.cs:924-930` | `new CombatScenarioData(m_NextBattleConfigurations, m_NextBattleConfigurationIndex, m_NextLootPreRolls, current)`, `SetMode(COMBAT)` (COMBAT has `canTransitionToSelf: true`, `Assets.Code.Game/GameModeType.cs:43`); applied through `GameTypeMgr.ClearCombatScenario(next)` (`CombatPresentationBhv.cs:2940`) and `GameTypeMgr.UpdateCombatScenarioData()` (`Assets.Code.Game/GameTypeMgr.cs:461-472`) |
| 8 | Hero-story retry button | `Assets.Code.UI.Controllers/LootUiControllerBhv.cs:449-459` | story constructor with `m_storyRetryCount + 1` |
| 9 | Load from save | `Assets.Code.Utils.Serialization/SaveUtils.cs:1450-1483` (`LoadCombatScenarioData`), `:1414-1448` (`TryLoadCombat`) | `CombatScenarioData.FromJson`, `CombatBhv.LoadFromJson` -> `new Battle(JToken)` |
| 10 | Debug boot of the `combat` scene / debug restart | `Assets.Code.Game/GameTypeMgr.cs:347-367`, `CombatSystemsInstaller.cs:81-113` (`RollDebugCombatScenarioData`) | gated by `CombatSystemsInstaller.IsDebug` (start scene `combat` or pref `run_test_game_mode`) |

Tutorial: there is no tutorial-specific battle launcher. `Assets.Code.Tutorial/TutorialEventsBhv.cs`
only listens to `EventBattleResult` / `EventBattleStartRound` / `EventBattleStateChanged`
(`:97-99`). Tutorial fights are ordinary road triggers using tables such as
`intro_valley_road_mashes` (table id present in `battle_configuration_table_data_export.Group.csv`;
the prefab wiring is not verified).

Debug commands: no console command starts a battle. The debug surface is `TextBasedEditorPrefs`
(text prefs, file `StreamingAssets/editor_prefs.txt`, parsed at startup only when the process has the
`-allowEditorPrefs` argument: `Assets.Code.Utils/TextBasedEditorPrefs.cs:72-75`,
`Assets.Code.Utils/CommandLineUtils.cs:61-65`). Relevant prefs in
`Assets.Code.Utils/TextBasedEditorPrefsBaseType.cs`: `BATTLE_TEST_BATTLE_CONFIGURATION` (`:279`),
`BATTLE_TEST_TEAM_0` (`:291`), `BATTLE_TEST_TEAM_1` (`:295`), `BATTLE_TEST_COMBAT_ARENA` (`:357`),
`BATTLE_TEST_COMBAT_SOURCE` (`:373`), `BATTLE_TEST_AMBUSH_TEAM_INDEX` (`:469`),
`BATTLE_TEST_RETREAT` (`:349`), `RUN_TEST_GAME_MODE` (`:449`), `HERO_PARTY` (`:477`),
`DRIVING_DISABLE_COMBATS` (`:575`). Pref values can also be set at runtime in code
(`TextBasedEditorPrefs.SetEditorPrefsFromStringArray(string[] textFileLines, bool isAdditive, bool clear = false)`,
`TextBasedEditorPrefs.cs:115-169`; each pref type has `SetValue`, e.g.
`TextBasedEditorPrefsIndexType.SetValue(int value)`, `Assets.Code.Utils/TextBasedEditorPrefsIndexType.cs:39-48`).
Side effects: any set pref calls `AnalyticsBhv.DisableAnalyticsInRelease()` in release builds
(`TextBasedEditorPrefs.cs:165-168`), and some battle test prefs are declared as save/load disabling
dependencies (`TextBasedEditorPrefsBaseType.cs:799-813`: `BATTLE_TEST_TURN_ORDER`,
`BATTLE_TEST_BATTLE_CONFIGURATION`, `BATTLE_TEST_TEAM_0/1`, `BATTLE_TEST_ALL_SKILLS`,
`BATTLE_TEST_ALL_COMBAT_ITEM_SKILLS`, `HERO_PARTY`). Team overrides via `BATTLE_TEST_TEAM_0/1` are
additionally gated by the command-line flag (`CombatBhv.cs:623-631`).

### Hook points for the mod

- Call (public API, no patch needed), in this order:
  `new CombatScenarioData(string battleConfigurationId, string backgroundScene, CombatSource loadedFrom, IReadOnlyList<uint> startingPartyActorGuids)`
  -> `Singleton<GameTypeMgr>.Instance.SetCombatScenario(data, isLoad: true)`
  -> `Singleton<GameModeMgr>.Instance.SetMode(GameModeType.COMBAT, isLoad: false)`.
  Use the list-based constructor (`CombatScenarioData.cs:220`) with a one-element
  `List<BattleConfigurationDefinition>` when lair-style follow-up waves must be suppressed.
- Party source: put exactly the four chosen heroes into `RosterStatusType.PARTY` before step 3
  (section 5); `StartCombat()` reads `RosterManager.GetActorGuids(RosterStatusType.PARTY)`.
- Harmony targets:
  - `Assets.Code.Combat.CombatBhv.StartCombat()` — prefix/postfix to observe or to rewrite team
    composition; transpile or postfix-replace to inject `m_AmbushTeamIndex` (private field
    `m_Battle` holds the result).
  - `Assets.Code.Combat.BattleTurnOrder.Create(Parameters battleTurnOrderParameters)`
    (`BattleTurnOrder.cs:76-79`) — prefix to set `battleTurnOrderParameters.m_AmbushTeamIndex`
    (0 = heroes surprised, 1 = enemies surprised). Cheaper alternative: set
    `TextBasedEditorPrefsBaseType.BATTLE_TEST_AMBUSH_TEAM_INDEX.SetValue(n)` before the fight and
    reset to -1 after (not verified whether this pref participates in the save/load-disable
    dependency list).
  - `Assets.Code.Combat.BattleModifier.BattleModifierCalculation.RollBattleModifier` — postfix to
    force/forbid a battle modifier.
  - `Assets.Code.Combat.CombatScenarioData` constructor (`:231`) — postfix to force
    `m_BackgroundSceneName` when a config carries an unwanted `m_BackgroundSceneOverride`.

---

## 2. Surrounding state a battle assumes

### 2.1 Hard dependencies (dereferenced without null checks on the battle path)

| Dependency | Evidence |
|---|---|
| A started "game type" (`GameTypeMgr.IsGameTypeStarted`) | `GameTypeMgr.GameModeEnterAsyncPreStart` returns `!nextGameMode.m_isGameTypeStartPoint` when not started (`Assets.Code.Game/GameTypeMgr.cs:289-308`); `GameModeType.COMBAT` has `isGameTypeStartPoint: true` (`Assets.Code.Game/GameModeType.cs:43`), so entering COMBAT blocks until a run or kingdom has started |
| `CombatScenarioData` set and loaded | `m_CurrentCombatScenarioData.OnCombatStart()` (`GameTypeMgr.cs:323-326`); `Battle.PostCreate` reads `Singleton<GameTypeMgr>.Instance.CombatScenarioData.CurrentBattleConfiguration` / `.m_LoadedFrom` (`Assets.Code.Combat/Battle.cs:91-99`) |
| `GameTypeMgr.RosterManager` | `CombatBhv.cs:390`, `BattleTeams.GetIsPartyInBattle` (`Assets.Code.Combat/BattleTeams.cs:637-667`), `Team.Destroy` (`Assets.Code.Combat/Team.cs:109`), `ActorInstance.OnBattleEnd` (`Assets.Code.Actor/ActorInstance.cs:4122`), `ActorInstance.GetShouldSave` (`:911-936`) |
| `GameTypeMgr.CombatManager` | `GameTypeMgr.OnGameModeEnterStart` (`GameTypeMgr.cs:315`), `CombatManager.CombatStart` (`Assets.Code.Combat/CombatManager.cs:173-182`) |
| `GameTypeMgr.BiomeManager` | `GameTypeMgr.cs:314`; `BattleResult` ctor `Singleton<GameTypeMgr>.Instance.BiomeManager.GetBiomeKillContractInstance(guid)` (`Assets.Code.Combat/BattleResult.cs:185`); `ActorInstance.OnAddedToRun` (`ActorInstance.cs:6804`) |
| `GameTypeMgr.StageCoach` | `m_StageCoach.OnGameModeEnterStart` (`GameTypeMgr.cs:322`) |
| `GameTypeMgr.RunValues`, `AffinityManager`, `ActOutManager` | `GameTypeMgr.cs:316-327` |
| `GameTypeMgr.StressManager`, `BarkManager` | `Battle.Update` (`Battle.cs:146-157`) |
| `GameTypeMgr.RunDataManager` | `Battle.AttemptRetreat` (`Battle.cs:176`), `RollBattleModifier` (`BattleModifierCalculation.cs:35`), `BattleConfigurationOption.GetAdditionalChance` (`Assets.Code.Combat.BattleConfiguration/BattleConfigurationOption.cs:150-165`) |
| `GameTypeMgr.LootManager`, `PlayerInventory` | `Battle.BattleEnd` (`Battle.cs:198-201`), `GameTypeMgr.OnGameModeExitComplete` (`GameTypeMgr.cs:340`) |
| `GameTypeMgr.TorchManager` | `ActorInstance.OnAddedToRun` (`ActorInstance.cs:6794`) |
| `SingletonMonoBehaviour<RunBhv>.Instance` (object must exist; `RunScoreManager` may be null) | `Battle.cs:161`, `BattleStateMachine.cs:472`, `CombatPresentationBhv.cs:1000` |
| `SingletonMonoBehaviour<KingdomBhv>.Instance` (object must exist; managers may be null) | `CombatPresentationBhv.cs:1020`, `1042`; `ActorInstance.cs:6809`, `6373` |
| `SingletonMonoBehaviour<ArenaBhv>`, `CombatBhv`, `CombatUiBhv` | provided by the `combat` scene installer (`CombatSystemsInstaller.SetupSystemResolvers`, `CombatSystemsInstaller.cs:60-65`) |
| `ActorCreateGameObjectBhv`, `PopTextManager`, `CommonUiBhv`, `ScreenFaderBhv`, `RedHookSceneManagerBhv` | `CombatBhv.cs:698-743`, `CombatPresentationBhv.cs:957-963` |

All of the `GameTypeMgr` managers are injected by events: `EventRunPreLoad`
(`GameTypeMgr.HandleEventRunPreLoad`, `GameTypeMgr.cs:196-211`), `EventCampaignStarted`
(`:184-188`, roster and quests) or `EventKingdomStarted` (`:240-257`, everything).

### 2.2 The run object (GameType.EXPEDITION)

- `RunBhv.StartRun(GameModeType startGameModeType, bool isDebugLoaderRestart = false)` creates all
  managers (`Assets.Code.Run/RunBhv.cs:179-198`, `238-271`) and raises `EventRunPreLoad` and
  `EventRunStarted`.
- It is triggered automatically: `RunBhv.GameModeEnterAsyncPreStart` runs for every mode with
  `m_isGameTypeStartPoint` and, if `CampaignBhv.IsCampaignStarted` and no run is started, calls
  `StartRun(nextGameMode)` (`RunBhv.cs:107-156`). `CampaignBhv.GameModeEnterAsyncPreStart` likewise
  auto-starts the campaign (`Assets.Code.Campaign/CampaignBhv.cs:56-79`), using
  `RunBhv.GetNextRunStartType()` to decide what to load (`RunStartType.MAIN_MENU_NEW` = nothing,
  `MAIN_MENU_NEW_RUN` = campaign only, `MAIN_MENU_CONTINUE` = campaign and run;
  `Assets.Code.Run/RunStartType.cs:16-26`).
- **In Expedition the driving scene is mandatory.** The same method loads `GameModeType.DRIVING.m_sceneName`
  (`"MainScene"`, `GameModeType.cs:41`) additively and returns false until
  `SingletonMonoBehaviour<MapMgrBhv>.HasInstance()` (`RunBhv.cs:113-133`). The skip branch
  (`flag2 = false`) requires `DebugLoader.IsAnyDebugBootUp`, which needs
  `Debug.isDebugBuild && Application.isEditor` (`Assets.Code.Debugging/DebugLoader.cs:40-65`), i.e.
  never in the shipped build.
- `MAIN_MENU` is a game-type end point (`GameModeType.cs:57`, `isGameTypeEndPoint: true`): entering it
  ends the run (`RunBhv.cs:150-153`) and the campaign (`CampaignBhv.cs:67-77`).

### 2.3 Map node, biome, stagecoach, camera

- Map node: only path 1 needs one (`GetComponentInParent<TileNodeBhv>()` for the kill contract,
  `TriggerCombatBhv.cs:169-174`; `CommonUiBhv.HideMinimap()` `:117`). The combat core has no
  reference to `MapMgrBhv` (grep over `Assets.Code.Combat*`, `Assets.Code.Actor`, `Assets.Code.Loot`,
  `Assets.Code.Roster`, `Assets.Code.Effect`, `Assets.Code.Skill` returns nothing).
- Biome: `GameTypeMgr.ActiveBiome` (`GameTypeMgr.cs:109-124`) reads
  `BiomeManager.GetActiveBiome()` (`Assets.Code.Map.Generation.Biome/BiomeManager.cs:550-566`), which is
  the last visited biome unless a synthetic one is set. It is consumed by data conditions
  (`Assets.Code.Condition/ConditionCalculation.cs:469-520`, e.g. `city_biome_check` in battle tables),
  by game over bookkeeping (`Assets.Code.Run/RunScoreManager.cs:109`) and by biome goals
  (`BiomeManager.HandleEventBattleBegin/Result`, `:439-463`). With no biome it is `BiomeType.INVALID`
  /null: table rolls that depend on biome conditions return nothing, everything else on the battle
  path tolerates it (not verified at runtime).
- Stagecoach: only `Singleton<GameTypeMgr>.Instance.StageCoach` (the data object
  `Assets.Code.Game.StageCoach.StageCoach`) must be non-null. Its combat hook does nothing outside
  DRIVING (`Assets.Code.Game.StageCoach/StageCoach.cs:118-127`). Arena scenes may show a coach prop
  (`ResourceStageCoachSkin.m_CombatArenaPrefab`, `Assets.Code.Game.StageCoach/ResourceStageCoachSkin.cs:24`;
  how it is spawned is not verified).
- Camera rig and scene: section 3.

### 2.4 How Kingdoms satisfies the assumptions

- `KingdomBhv.CreateManagers()` builds the same manager set as a run plus kingdom-only ones
  (`Assets.Code.Kingdom/KingdomBhv.cs:647-670`), initialises them without a `RunManager`
  (`m_RunDataManager.InitPreLoad(null, m_BiomeManager)`, `m_TorchManager.InitPreLoad(null, m_KingdomManager, ...)`,
  `:718-742`) and publishes them with `EventKingdomStarted.Trigger(...)` (`:450`). There is no
  `RunScoreManager`/`DoomManager` in Kingdom mode, which is why every use of
  `RunBhv.Instance.RunScoreManager` on the battle path is null-checked.
- **No driving scene while a kingdom day is running.** `KingdomBhv.GameModeEnterAsyncPreStart`
  computes `flag2 = nextGameMode.m_isInBiome` and, once the kingdom is initialised,
  `flag2 &= m_KingdomDaySystem == null`; when false the driving scene is unloaded
  (`KingdomBhv.cs:254-288`). So a map siege launched from the inn (`KingdomSiegeManager.cs:334-359`)
  fights with only the `combat`, arena and inn-side systems loaded. This is the existing proof that
  DD2 combat runs without road/map.
- Biome for an off-road fight is faked by an event: `EventKingdomSiegeCombatStart.Trigger(biomeType, strength)`
  -> `BiomeManager.m_KingdomMapSiegeBiomeInstance = new BiomeInstance(biomeType, strength, NodeType.KINGDOM_INN_SIEGED)`
  (`BiomeManager.cs:465-468`); `EventCampAmbushStart.Trigger(biomeType)` ->
  `m_CampAmbushBiomeInstance = new BiomeInstance(biomeType, 0, NodeType.INN)` (`:470-473`). Both are
  cleared on `EventBattleResult` (`:449-463`). `GetActiveBiome()` prefers them (`:557-564`).
- Party: for map sieges the defenders are the heroes garrisoned in that inn
  (`KingdomMapCellInn.ActorGuids`) padded with `KingdomSiegeDefenseDefinition.ActorClassIds`
  (`CombatScenarioData.GetKingdomSiegeDefenseActorClassIds()`, `CombatScenarioData.cs:499-548`). They
  are resolved **by class id** through `LibraryActors.CreateActorIfNecessary(actorDataId)`
  (`CombatBhv.cs:384-387`, `248-256`; `Assets.Code.Actor/LibraryActors.cs:110-114`), which assumes one
  hero per class.
- Party wipe is survivable: `CombatSource.KINGDOM_INN_SIEGED` has `isEndOnPartyDead: false`
  (`CombatSource.cs:48`) and `KingdomBhv.HandleEventBattleResult` returns to the inn unless
  `KingdomManager.CheckGameOverRoster()` ends the kingdom (`KingdomBhv.cs:224-252`).

### 2.5 What breaks without a road/map (summary)

- Expedition: nothing can break because the game refuses to enter COMBAT until `MainScene` and
  `MapMgrBhv` exist (2.2). The cost is that the road scene is loaded; whether a map must be
  generated for `MapMgrBhv` to be happy while never driving is not verified.
- Kingdom with an active day system: nothing; this is a shipped path.
- Any host: the default return target after the loot screen is `GameModeType.DRIVING` unless the
  source is `KINGDOM_INN_SIEGED` or `CAMP_AMBUSH` (section 4), so the return must be redirected.
- `SaveUtils.SaveCurrentGameMode()` is called by the battle state machine after `START` and after
  every turn (`Assets.Code.Combat/BattleStateMachine.cs:495-559`). It writes the run/campaign or
  kingdom save plus a combat sub-save (`SaveUtils.RunSyncOperations`,
  `Assets.Code.Utils.Serialization/SaveUtils.cs:689-730`; `SaveCombat` `:1404-1412`). It can be
  switched off with the prefs `DISABLE_SAVE_GAME` / `DISABLE_LOAD_GAME`
  (`SaveUtils.GetIsGameSaveDisabled`, `:1755-1762`; `GetIsGameLoadDisabled`, `:1780`).

### Hook points for the mod

- To give a standalone battle a biome without a map, raise the shipped event before starting:
  `EventCampAmbushStart.Trigger(BiomeType biomeType)`
  (`Assets.Code.Inn.Events/EventCampAmbushStart.cs:15-18`). Listeners are `BiomeManager` (sets the
  synthetic biome) and `RosterManager.HandleEventCampAmbushStart`, which returns immediately when
  `KingdomBhv.Instance.KingdomMapManager == null` (`Assets.Code.Roster/RosterManager.cs:292-318`), i.e.
  it is side-effect free in Expedition. In Kingdom mode it would pull garrison heroes into the
  party, so there use `EventKingdomSiegeCombatStart.Trigger(biomeType, strength)` or a Harmony
  postfix on `BiomeManager.GetActiveBiome()` instead.
- Harmony targets for "no road":
  - `Assets.Code.Run.RunBhv.GameModeEnterAsyncPreStart(GameModeType, GameModeType, bool)` — the place
    that forces `MainScene`/`MapMgrBhv` (patching it is high risk; listed for completeness).
  - `Assets.Code.Kingdom.KingdomBhv.GameModeEnterAsyncPreStart(...)` — the Kingdom equivalent.
  - `Assets.Code.Utils.Serialization.SaveUtils.SaveCurrentGameMode(string nodeTypeName = null, bool isSoftSave = false)`
    — prefix to suppress or redirect vanilla saves during mod battles.
- Decision input for the mod design (evidence above): GameType.KINGDOM already has off-road battles,
  survivable wipes and a roster larger than the party; GameType.EXPEDITION has none of the three.

---

## 3. Scene and arena

### 3.1 Scenes

- `combat` — the combat systems scene. Name constant `CombatScenarioData.SCENE_NAME_COMBAT = "combat"`
  (`Assets.Code.Combat/CombatScenarioData.cs:27`); it is `GameModeType.COMBAT.m_sceneName`
  (`GameModeType.cs:43`; addressable `Assets/Scenes/combat.unity` in `StreamingAssets/aa/catalog.json`).
  Loaded additively by `CombatScenarioData.Load()` if not already loaded (`:313-320`). Its installer
  `CombatSystemsInstaller` registers `ArenaBhv`, `CombatBhv`, `CombatUiBhv`
  (`CombatSystemsInstaller.cs:60-65`). `ArenaBhv` owns the `TeamLayout` components and the
  `CombatPresentationBhv` (`Assets.Code.Combat/ArenaBhv.cs:16-39`, `65-87`).
- `combat_arena_*` — the background ("arena") scene. `CombatScenarioData.Load()` first scans loaded
  scenes for a name matching `SceneUtils.GetCombatArenaSceneRegex()` = `"(combat_arena_)"`
  (`Assets.Code.Utils/SceneUtils.cs:54-57`) and adopts it if found (`CombatScenarioData.cs:326-336`);
  otherwise it loads `m_BackgroundSceneName` additively (`:337-343`). It is unloaded in
  `CombatScenarioData.Unload()` (`:354-379`), called from `GameTypeMgr.ClearCombatScenario`
  (`GameTypeMgr.cs:437-447`) on COMBAT exit (`CombatPresentationBhv.cs:2935-2941`).
  `GameTypeMgr.OnGameModeEnterStart` makes it the active scene (`SetBgAsActiveScene`,
  `GameTypeMgr.cs:318-321`, `CombatScenarioData.cs:555-558`).
- `combat_results` / `combat_results_hero_story` — loot/result presentation, loaded additively by
  `CombatPresentationBhv.WaitForCombatStart()` from serialized `m_CombatResultsScene` /
  `m_CombatResultsHeroStoryScene` (`CombatPresentationBhv.cs:170-174`, `2860-2878`). It is
  `GameModeType.RESULTS.m_sceneName` (`GameModeType.cs:55`). `CombatResultsPresentationBhv` registers
  itself in `ArenaBhv.CombatPresentation.RegisteredCombatResultsPresentation`
  (`Assets.Code.Combat.Presentation/CombatResultsPresentationBhv.cs:57-83`).
- `combat_intro_*` — optional real-time cinematic before a boss
  (`CombatScenarioData.SetCombatIntroHandle`, `CombatScenarioData.cs:394-439`; example addressable
  `Assets/Scenes/Combat/mountain/combat_intro_boss_body.unity`).

### 3.2 How the arena is selected

1. `BattleConfigurationDefinition.m_BackgroundSceneOverride` if the config has one (140 of the 789
   base-game configs; lairs, creature dens, hero stories, mountain bosses) —
   `CombatScenarioData.cs:266-269`.
2. Else the `backgroundScene` constructor argument: `TriggerCombatBhv.m_combatArenaBgOverride`
   (`TriggerCombatBhv.cs:24-26`, `159-166`), `BiomeData.GetManualSiegeCombatArena()` /
   `GetCampAmbushCombatArena()` (`Assets.Code.Map.Generation.Biome/BiomeData.cs:208-213`, `1071-1079`),
   story `m_BackgroundScene` / `StoryChoiceDefinition.m_ResultBackgroundScene`
   (`StoryCalculation.cs:480-485`).
3. Else (road fights pass `null`) whatever `combat_arena_*` scene is already loaded. The generic
   helper for that is `Assets.Code.Loading.AdditivelyLoadScene` (serialized `m_SceneToLoad`, loads in
   `Start`, unloads in `OnDestroy`; `Assets.Code.Loading/AdditivelyLoadScene.cs:8-43`). That road
   tiles/nodes use it to preload the biome arena is inferred — not verified (prefab data).

There is no code table "biome -> arena". The mapping is by scene name. Addressable scene names found
in `StreamingAssets/aa/catalog.json` follow `combat_arena_<biome>_<kind>`:

- biomes: `valley`, `city`, `farm`, `forest`, `coast`, `tundra`, `caves`, `catacombs` (DLC),
  `mountain`, plus `kingdom_camp`, `stressworld`, `hero_story_<hero>_origin_<n>`.
- kinds per biome (not all exist for every biome): `faction`, `resist`, `cultist`,
  `story_cultist`, `gaunt`, `gaunt_chirurgeon`, `pillager`, `military`, `creature_den`,
  `inn_defense`, `urgent_repairs`, `warlord`, `dungeon_exterior`, `dungeon_interior`,
  `boss_beastmen`, `boss_courtier`, `boss_coven`, `barricade_gang_*`;
  mountain: `boss_brain`, `boss_lungs`, `boss_eyes`, `boss_arms`, `boss_body`, `story_cultist`.
  Examples: `Assets/Scenes/Combat/city/combat_arena_city_faction.unity`,
  `Assets/Scenes/Combat/Farm/combat_arena_farm_dungeon_interior.unity`.

### 3.3 Spawning and positioning

- Logic side: `BattleTeams.Create(Parameters, BattleModifierDefinition)`
  (`Assets.Code.Combat/BattleTeams.cs:92-131`) creates one `Team` per `TeamParameters`
  (`new Team(m_TeamSize, index, m_ActorControllertype, summonConfig, battleModifier, testState, additionalIds)`,
  `Assets.Code.Combat/Team.cs:79-88`) and adds actors in guid-list order
  (`Team.AddActor`, `Team.cs:162-188`) -> `ActorInstance.OnAddedToTeam(Team, int teamPosition, int newRank, ActorControllerBase, bool isLoad)`
  (`Assets.Code.Actor/ActorInstance.cs:6569-6597`), which calls `OnBattleStart()` (`:4109-4118`),
  `SetTeamPosition`, `SetFrontRank`. Team constants: `BattleTeams.HERO_TEAM_INDEX = 0`,
  `ENEMY_TEAM_INDEX = 1`, `DEFAULT_TEAM_SIZE = 4` (`BattleTeams.cs:74-78`).
- Hero order = `RosterManager.GetActorGuids(RosterStatusType.PARTY)`, sorted by
  `ActorInstance.GetFrontRank()` (`Assets.Code.Roster/RosterManager.cs:1391-1420`).
- Visual side: `CombatBhv.SpawnActors()` (`CombatBhv.cs:708-743`) queries `QueryTeamActors` per team,
  gets `ArenaBhv.GetTeamLayout(i)`, takes the slot
  `teamLayout.InlineLayout.Area.GetElement(actorInstance.TeamPosition)` (an `ActorSpacingElement`),
  sets its `Size`, and instantiates
  `SingletonMonoBehaviour<ActorCreateGameObjectBhv>.Instance.CreateActorGameObject(uint actorGuid, Transform parent, int characterRendingLayer, string characterAnimationState, bool loadSubclasses)`
  (`Assets.Code.Actor/ActorCreateGameObjectBhv.cs:165-203`) under the `TeamLayout` transform; it waits on
  `WaitForActorsToLoad`, then `CombatActorBhv.PostLoadInitialize(ActorSpacingElement, bool)`
  (`Assets.Code.Combat/CombatActorBhv.cs:880`). Mid-battle spawns go through `EventCreateActor`
  (`CombatBhv.HandleEventCreateActor`, `:179-188`; `SpawnActor`, `:695-706`).
- `TeamLayout` (`Assets.Code.Combat.Presentation/TeamLayout.cs:11-90`): serialized `m_TeamIndex`,
  `m_lineLayoutParent` (`InlineLayout`), `m_zoomInLayoutParent` (`ZoomInLayout`),
  `m_CameraZoomInGroup` (`CinemachineTargetGroup`). `CleanUp()` destroys all `ActorBhv` children on
  COMBAT exit (`ArenaBhv.EndCombat`, `ArenaBhv.cs:56-63`).
- Asset preloading: `ActorCreateGameObjectBhv.PreloadCombatScenario(CombatScenarioData)` /
  `RemovePreloadCombatScenario` (`ActorCreateGameObjectBhv.cs:246-260`), called from
  `CombatScenarioData.Load/Unload`.

### 3.4 Camera

- Cinemachine. `Assets.Code.Combat.Presentation.CombatCameraBhv : CinemachineVirtualCamera`
  (`Assets.Code.Combat.Presentation/CombatCameraBhv.cs:13`) holds a serialized list of conditional
  cameras (`m_ConditionalCameras`, `:34-35`) evaluated through `CombatConditional<...>` (`:88-112`)
  and reacts to skill presentations and presentation state changes
  (`IOnSkillPresentation`, `IOnCombatPresentationStateChange`).
- `CombatVirtualCameraMgr` (`Assembly-CSharp/CombatVirtualCameraMgr.cs:7-44`) disables all virtual
  cameras of its scene if the scene awakes while the mode is `INN` and re-enables them on entering
  COMBAT — the support code for pre-loading the combat scene from the Kingdom inn.
- Zoom-in presentation uses `TeamLayout.m_CameraZoomInGroup` and timelines owned by
  `CombatPresentationBhv` (`m_ZoomInTimelines`, `CombatPresentationBhv.cs:435-439`).
- Where the `CinemachineBrain`/main camera lives in the scene hierarchy is not verified.

### Hook points for the mod

- Choose the arena by passing `backgroundScene` to `CombatScenarioData` (must be an addressable
  scene name such as `combat_arena_city_faction`), remembering rule 1 above.
- Harmony targets: `CombatScenarioData.Load()` (prefix/postfix to preload or substitute scenes),
  `Assets.Code.Combat.CombatBhv.SpawnActors()` (private coroutine) and
  `ActorCreateGameObjectBhv.CreateActorGameObject(uint, Transform, int, string, bool)` for visuals.
- A custom dungeon scene must not have a name containing `combat_arena_`, or the next
  `CombatScenarioData.Load()` will adopt it as the arena (`CombatScenarioData.cs:326-336`).

---

## 4. Battle end

### 4.1 Detection

`CombatPresentationBhv.Update()` drives the logic: `UpdateBattleStateMachine()` ->
`m_CombatBhv.MoveNext()` -> `Battle.Update()` (`CombatPresentationBhv.cs:689-716`, `899-922`;
`CombatBhv.cs:146-158`; `Battle.cs:146-166`). After each state-machine tick:

```csharp
// Assets.Code.Combat/Battle.cs:161-164
if (m_BattleStateMachine.GetIsStateValidForEnd() && (m_BattleConditions.CheckForEnd()
    || (SingletonMonoBehaviour<RunBhv>.Instance.RunScoreManager != null
        && SingletonMonoBehaviour<RunBhv>.Instance.RunScoreManager.IsGameOver())))
{
    BattleEnd();
}
```

- States valid for end (`BattleState.m_IsValidForEnd`): `BEFORE_TURN`, `IN_TURN_SELECT`, `AFTER_TURN`,
  `END_RETREAT` (`Assets.Code.Combat/BattleState.cs:13`, `17`, `33`, `37`).
- End conditions built in `BattleConditions.PostCreate` (`Assets.Code.Combat/BattleConditions.cs:47-90`):
  `OnlyTeamBattleCondition()` (at most one team still has actors or queued summons;
  `Assets.Code.Combat.BattleCondition/OnlyTeamBattleCondition.cs:17-55`), `NoTeamBattleCondition()`,
  `PartyDeadBattleCondition()` if `combatSource.m_IsEndOnPartyDead` (it tests
  `RosterManager.GetIsPartyDead()`, `Assets.Code.Combat.BattleCondition/PartyDeadBattleCondition.cs:8`),
  `MaxStressTeamBattleCondition()` if `m_EndAtMaxStress`, `RoundBattleCondition(n)` if
  `m_RoundLimit > 0`; plus data-driven `EndActorAnyConditions` / `EndActorAllConditions`
  (`BattleConditions.cs:156-204`), and `m_IsForceEnd || m_IsRetreat` (`:132-140`).
- "Complete" (= victory) conditions: `OnlyTeamBattleCondition(0)` or `NoTeamBattleCondition()`
  (`BattleConditions.cs:74-78`), or `ForceEnd(isForceComplete: true)`, or
  `m_EndConditionsIsComplete`, or config `m_EndIsAlwaysComplete` (`BattleResult.cs:106`).
- Retreat: `CombatBhv.AttemptRetreat()` (`CombatBhv.cs:464-472`) -> `Battle.AttemptRetreat()`
  (`Battle.cs:173-187`): rolls `RunStatType.RETREAT_CHANCE`; success raises
  `EventBattleRetreat.Trigger(bool isDungeon)`, failure `EventBattleRetreatFailed.Trigger()`.
  `BattleStateMachine.HandleEventBattleRetreat` queues `START_RETREAT` (`BattleStateMachine.cs:186-189`),
  which applies `CombatRules.RetreatEffects` / `RetreatPerHeroEffects` / `RetreatSingleHeroEffects`
  (`:355-365`; data `combat_rules_data_export.Group.csv:9-11`, `22-35`: doom +2, 2 stress per hero)
  and then `END_RETREAT` -> `m_BattleConditions.Retreat()` (`:366-369`). UI entry:
  `BattleInfoUiBhv.ButtonFuncAttemptRetreat()` (`Assets.Code.ui/BattleInfoUiBhv.cs:1162-1169`).
  Retreat is blocked when any actor has `ActorDataClass.m_IsRetreatInvalidating`
  (`BattleTeams.GetIsRetreatInvalid`, `BattleTeams.cs:669-695`).
- Forced end: `CombatBhv.ForceEndCombat(bool isForceComplete)` (`CombatBhv.cs:474-480`), used by the
  debug buttons (`Assets.Code.ui/BattleInfoUiBhv.cs:1404`, `1409`;
  `Assets.Code.UI.Controllers/PauseMenuUiControllerBhv.cs:476`, `487`) and by
  effects with `m_WinCombat` / `m_LoseCombat` (`Assets.Code.Effect/EffectInstance.cs:476-482`).

### 4.2 Signal: `EventBattleResult`

`Battle.BattleEnd()` (`Battle.cs:194-204`):

1. `new BattleResult(combatScenarioData.BattleConfigurations, CurrentBattleConfigurationIndex,
   AdditionalBattleConfiguration, LootPreRolls, m_BattleConditions, m_BattleTracking, m_LoadedFrom,
   m_NodeSubType, StartingPartyActorGuids, m_StoryActorGuid, m_StoryChoice, m_StoryRetryCount,
   m_KingdomSiegeGuid, KingdomSiegeDefenseActorGuids, BiomeKillContractGuid)`.
2. `Singleton<GameTypeMgr>.Instance.LootManager.AddLoot(battleResult.m_LootRewards, SourceType.COMBAT, battleResult.m_LootReason, battleResult.m_LootReasonId)`.
3. `Destroy(battleResult)` -> every `IBattleSystem.Destroy`; `Team.Destroy(destroyTeamActors: team.m_TeamIndex != 0, battleResult)`
   (`BattleTeams.cs:142-150`, `Team.cs:103-121`) -> `actor.OnRemovedFromTeam()`,
   `actor.OnBattleEnd(battleResult)`, and `actor.Destroy()` for enemies **and for any team-0 actor
   that is not in the roster by guid**; then `EventBattleExit.Trigger()` (`Battle.cs:101-110`).
4. `EventBattleResult.Trigger(battleResult)` (`Assets.Code.Combat.Events/EventBattleResult.cs:14-17`).

`BattleResult` (`Assets.Code.Combat/BattleResult.cs:14-97`) — how to read the outcome:

| Outcome | Test |
|---|---|
| This fight won | `IsFightComplete` (`:60`) |
| Whole sequence won | `IsBattleSequenceComplete` (`:62-72`) |
| More waves follow | `HasNextBattle` (`:74-84`), `m_NextBattleConfigurationIndex` (`:22`) |
| Retreated | `m_IsRetreat` (`:28`) |
| Debug/effect forced | `m_IsForceEnd` (`:26`) |
| Lost | `!IsFightComplete && !m_IsRetreat`; party wipe is `Singleton<GameTypeMgr>.Instance.RosterManager.GetIsPartyDead()` (`RosterManager.cs:1062-1065`) |
| Context | `m_CombatSource`, `m_NodeSubType`, `m_StartingPartyActorGuids`, `CurrentBattleConfiguration`, `m_IsBiomeBossBattle`, `m_IsExpeditionBossBattle`, `m_IsGangBossBattle`, `m_LootRewards`, `m_LootReason`, `m_LootReasonId` (`:18-58`) |

Other battle events (all in `Assets.Code.Combat.Events/`): `EventBattleBegin` (battle index, config,
source, isLoad), `EventBattleExit`, `EventBattleStateChanged`, `EventBattleStartRound`,
`EventBattleEndRound`, `EventBattleRetreat`, `EventBattleRetreatFailed`, `EventBattleStall`,
`EventCombatActorDeath`, `EventCombatResultsStateChanged`. Subscribe with
`EventManager.AddListener<T>(Action<T> callback, bool oneShot = false, int priority = 0)`
(`Assets.Code.Events/EventManager.cs:54`), unsubscribe with `RemoveListener<T>` (`:109`).

Systems that react to `EventBattleResult` (from the `AddListener<EventBattleResult>` grep):
`RunScoreManager` (score + game over, `Assets.Code.Run/RunScoreManager.cs:185-208`), `RosterManager`
(kills still-captured heroes, `RosterManager.cs:320-327`), `BiomeManager` (`BiomeManager.cs:449-463`),
`RunDataManager` (clears battle stats, `Assets.Code.Run/RunDataManager.cs:99-100`), `TorchManager`,
`PlayerItemInventory`, `ActOutManager`, `KingdomBhv` (`KingdomBhv.cs:183-186`), `KingdomManager`
(gang boss victory, `Assets.Code.Kingdom/KingdomManager.cs:253-259`), `KingdomSiegeManager`
(`KingdomSiegeManager.cs:123-129`), `CombatBhv` (story unlock, `CombatBhv.cs:190-200`),
`CombatPresentationBhv` (`:518-522`), `AchievementsMgr`, `TutorialEventsBhv`, narration/bark/UI.

### 4.3 Loot and rewards

- Tables: `BattleConfigurationDefinition.GetLootTables(CombatSource combatSource, bool isComplete)`
  returns complete/incomplete loot tables gated by `combatSource.m_UseLootTables` /
  `m_UseStoryLootTables` (`BattleConfigurationDefinition.cs:420-432`).
- `BattleResult` ctor: sequences use pre-rolled loot (`LootPreRoll`, `BattleResult.cs:146-177`),
  single fights add the config tables (`:178-181`), kill contract completion (`:182-192`), then
  `list.AddRange(battleTracking.DeathLootIds)` (per-monster `ActorDataClass.m_DeathLootIds`,
  `Assets.Code.Combat/BattleTracking.cs:277`) and
  `LibraryLoot.CollectLoot(list, RandomIdentifier.LOOT, m_LootRewards)` (`:193-194`).
  `m_LootReason` is one of `STORY`, `STORY_INCOMPLETE`, `KINGDOM_SIEGE`, `KILL_CONTRACT_COMPLETE`,
  `COMBAT_COMPLETE`, `COMBAT_PARTIAL_COMPLETE`, `COMBAT_INCOMPLETE` (`:195-229`).
- `LootManager.AddLoot(IReadOnlyList<Reward<LootType>> lootRewards, SourceType sourceType, LootReason reason, string reasonId)`
  (`Assets.Code.Loot/LootManager.cs:130`): `LootType.ITEM` -> `ItemInstance`s (queued for the loot
  window), `LootType.PROVISION` -> `Singleton<GameTypeMgr>.Instance.RunValues.ChangeValue(runValueType, qty, sourceType)`
  (hero upgrade points, torch, coach armor/wheels; `:158-161`). `LootType` values:
  `provision, item, sub_table, unique_sub_table, exclusive_sub_table, all_sub_table, nothing,
  profile_unlock, quest_step, biome_reward` (`Assets.Code.Loot/LootType.cs:9-27`).
- Shown in RESULTS mode: `CombatResultsPresentationBhv.OnTimelineStarted()` ->
  `LootManager.ShowLoot(m_BattleResult.m_LootReason, m_BattleResult.m_LootReasonId, CurrentBattleConfiguration, m_CombatSource, m_NodeSubType, EndTimeline)`
  (`CombatResultsPresentationBhv.cs:91-111`; `LootManager.ShowLoot`, `LootManager.cs:340-432`), which
  opens `CommonUiBhv.ShowLoot(...)`. Dead heroes' trinkets and combat items are moved into the next
  loot window (`ActorInstance.HandleEventActorDeathLogic`, `ActorInstance.cs:1211-1217` ->
  `LootManager.EmptyInventoryIntoNextWindowLoot`, `LootManager.cs:335-338`).

### 4.4 After the result: presentation and return of control

1. `CombatPresentationBhv.HandleBattleResult` stores `m_LastBattleResult` and sets
   `m_IsBattleFinished` (`CombatPresentationBhv.cs:518-522`); when the battle state becomes
   `INACTIVE` it queues `CheckForEndOfCombat()` (`:841-846`).
2. `CheckForEndOfCombat()` (`:949-1088`), in order:
   - expedition boss with `m_HasEndSequence` -> `BossEndUI()` (`:989-999`);
   - `RunBhv.Instance.RunScoreManager.IsGameOver()` -> outro video and/or
     `RunScoreManager.TriggerGameOver()` (`:1000-1019`);
   - `KingdomBhv.Instance.KingdomManager.IsGameOver()` -> `SetMode(GameModeType.RESULTS, false, SceneTransition.TILT_TO_TOP_AND_BACK)`
     and load the gang victory/defeat scene (`:1020-1041`);
   - Kingdom day system active and `m_CombatSource == CombatSource.KINGDOM_INN_SIEGED` -> only
     unloads the results scene (`:1042-1049`); the mode change is done by
     `KingdomBhv.HandleEventBattleResult` -> `SetMode(GameModeType.INN, isLoad: false)` (`KingdomBhv.cs:224-252`);
   - `RosterManager.GetIsPartyDead()` -> game over path if a `RunScoreManager` exists (`:1050-1077`);
   - `HasNextBattle` -> `ConfirmNextDungeonBattle(...)` (`:1078-1081`, `2352-2382`): optional waves
     show `CommonUiBhv.ShowDungeonConfirmationDialog(accept, decline, index, lootPreRolls)`;
   - otherwise `SetupCombatResults(); AddEndCombatPresentation();` (`:1082-1087`).
3. `EndCombat()` -> `SetNextGameMode()` (`:1100-1122`, `924-942`): next wave -> `SetMode(COMBAT)`;
   else if `RegisteredCombatResultsPresentation.IsConfigured` -> `SetMode(GameModeType.RESULTS, isLoad: false)`.
4. Leaving COMBAT: `CombatPresentationBhv.OnGameModeExitComplete` ->
   `GameTypeMgr.ClearCombatScenario(next)` (`:2935-2957`); `CombatBhv.OnGameModeExitComplete` ->
   `EndCombat()` (`CombatBhv.cs:274-281`, `661-674`); `ArenaBhv` cleans the layouts;
   `GameTypeMgr.OnGameModeExitComplete` refreshes the inventory and calls
   `LibraryActors.LibraryActorsInstance.RemoveUnsavedActors()` (`GameTypeMgr.cs:331-345`), which drops
   dead actors and spent monsters from the actor library.
5. RESULTS mode: `CombatResultsPresentationBhv.OnGameModeEnterComplete` spawns the party
   (`PartyPresentationBhv.SpawnParty()`, uses `RosterStatusType.PARTY`,
   `Assets.Code.Presentation/PartyPresentationBhv.cs:151-156`) and runs the timeline
   (`CombatResultsPresentationBhv.cs:177-238`; base `HeroPresentationController.RunPresentationTimeline`,
   `Assets.Code.Presentation/HeroPresentationController.cs:69-80`).
6. **Return of control**: `CombatResultsPresentationBhv.OnTimelineComplete()`
   (`CombatResultsPresentationBhv.cs:113-156`, `protected override void`):

   ```csharp
   if (Singleton<GameTypeMgr>.Instance.CombatScenarioData != null)        gameModeType = GameModeType.COMBAT;   // retry queued
   else if (m_BattleResult.m_CombatSource == CombatSource.KINGDOM_INN_SIEGED) gameModeType = GameModeType.INN;
   else if (m_BattleResult.m_CombatSource == CombatSource.CAMP_AMBUSH)
       gameModeType = m_BattleResult.IsBattleSequenceComplete ? GameModeType.EMBARK : GameModeType.INN;
   else                                                                    gameModeType = GameModeType.DRIVING;
   Singleton<GameModeMgr>.Instance.SetMode(gameModeType, isLoad: false);
   ```

   `CombatSource.m_CanReturnToDriving` exists (`CombatSource.cs:56`) but is not consulted here.

### 4.5 Party wipe

- Expedition: `RunScoreManager.HandleEventBattleResult` -> `SetGameOver(GameOverReason.PARTY_DEAD)`
  when `RosterManager.GetIsPartyDead()` (`RunScoreManager.cs:185-208`; `SetGameOver`, `:73-128`,
  raises `EventRunGameOver`). `CheckForEndOfCombat` then calls `TriggerGameOver()` =
  `SetMode(GameModeType.DRIVING, false, SceneTransition.GAME_OVER, true, ..., isGameOverTransition: true)`
  (`:145-154`). On the next mode pre-start `RunBhv` sees `m_RunScoreManager.IsGameOver()` and calls
  `ResetRun(RunStartType.IN_CAMPAIGN_GAME_OVER)` (`RunBhv.cs:135-143`, `273-278`) -> `EndRun` ->
  `EventRunEnded` -> `CampaignBhv.OnRunEnd` which removes every actor not returned by
  `RosterManager.GetRunEndKeepActors(gameOverReason.m_PartyKeepType)` and resets the rest
  (`CampaignBhv.cs:258-297`; `GameOverReason.PARTY_DEAD` keeps `PartyKeepType.NONE`,
  `Assets.Code.Game/GameOverReason.cs:10`). In short: a wipe ends the run.
- The same handler declares `GameOverReason.VICTORY` when the sequence is complete and
  `m_IsExpeditionBossBattle` (`RunScoreManager.cs:195-198`), i.e. beating any enemy tagged
  `end_boss` (`DrivingRules.m_endBossTag`, `driving_rules_data_export.Group.csv:17`) ends the run as a
  win. In Kingdom, beating a `gang_boss`-tagged enemy calls `KingdomManager.SetGameOver(GameOverReason.VICTORY)`
  (`KingdomManager.cs:253-259`; tag from `kingdom_rules_data_export.Group.csv:23`).
- Kingdom: no run score manager; `KingdomBhv.HandleEventBattleResult` clears loot, calls
  `MapMgrBhv.ClearGame()` if a map exists, runs `KingdomManager.CheckGameOverRoster()` and, if the
  kingdom survives, goes back to the inn (`KingdomBhv.cs:224-252`). `CheckGameOverRoster()` only
  ends the kingdom (`GameOverReason.KINGDOM_EMPTY_ROSTER`) when the replacement type is
  `RosterReplacementType.NONE` and there are no `PARTY` and no `KINGDOM` heroes left
  (`Assets.Code.Kingdom/KingdomManager.cs:367-373`).
- Individual death: section 6.4.

### Hook points for the mod

- Result: `EventManager.AddListener<EventBattleResult>(OnBattleResult)` — fires once per fight with
  the `BattleResult`. Register with a very low priority value if the mod must run after vanilla
  listeners (vanilla uses values from 1000 down to -1000; ordering semantics of `priority` not
  verified).
- Return of control: Harmony prefix on
  `Assets.Code.Combat.Presentation.CombatResultsPresentationBhv.OnTimelineComplete()` returning
  false after calling `Singleton<GameModeMgr>.Instance.SetMode(<mod hub mode>, isLoad: false)`
  (replicate the two cleanup lines `RemoveListeners(); m_CurrentStoryChoice = null;` or let the
  original run and patch only the `SetMode` target).
- Skip the loot screen entirely: prefix on
  `Assets.Code.Combat.Presentation.CombatPresentationBhv.SetNextGameMode()` (private) and set the
  mode yourself; loot has already been added by `LootManager.AddLoot` at that point but not shown
  (`LootManager.ClearShowWindowVariables()` is public, `LootManager.cs:434`).
- Prevent "wipe = run over" (Expedition host): prefix on
  `Assets.Code.Run.RunScoreManager.HandleEventBattleResult(EventBattleResult)` (private) or on
  `RunScoreManager.SetGameOver(GameOverReason)`; without it `CheckForEndOfCombat` takes the
  game-over branch. Same patch blocks the accidental `VICTORY` when an `end_boss` actor is used as
  a dungeon boss. Kingdom host: prefix `Assets.Code.Kingdom.KingdomManager.HandleEventBattleResult`
  and `KingdomManager.CheckGameOverRoster()`.
- Wave prompt: `CombatPresentationBhv.ConfirmNextDungeonBattle(...)` (private) if the mod wants its
  own "continue or leave" UI.
- Retreat: call `SingletonMonoBehaviour<CombatBhv>.Instance.AttemptRetreat()`; patch
  `Battle.AttemptRetreat()` to change the chance source; retreat effects are data
  (`combat_rules_data_export.Group.csv`).

---

## 5. Heroes

### 5.1 The hero instance class

`Assets.Code.Actor.ActorInstance : ILibraryElement<uint>` (`Assets.Code.Actor/ActorInstance.cs:78`).
Heroes and monsters are the same class; a hero is an `ActorInstance` whose `ActorDataClass` has
`m_StartingRosterStatusType != null` (`ActorDataClass.IsPopulateInRoster`,
`Assets.Code.Actor/ActorDataClass.cs:209`; `IsInHeroSelect` = `IDLE`, `IsHireClass` = `HIRE`,
`:205-207`). All live instances are stored in `LibraryActors : Library<uint, ActorInstance>`
(`Assets.Code.Actor/LibraryActors.cs:13`), keyed by `m_ActorGuid`.

| Aspect | Member(s) | Lines (ActorInstance.cs unless noted) |
|---|---|---|
| Identity | `public readonly uint m_ActorGuid`; `ActorGuid`; `ActorDataId` = last of `m_ActorDataIds` (class chain, e.g. hero -> corpse); `m_ActorName` / `SetActorName(string)`; `m_ActorSkinId`, `m_ActorWeaponKitId`, `m_ActorPaletteId` | 102-110, 155, 394-402, 2313 |
| Class data | `ActorDataClass` (`DataContainer` with `ActorDataStats`, effects, quirk container def, paths, modes, skill sets) | 410; `ActorDataClass.cs:31-243` |
| Stats | `GetClampedStatValue(ActorStatType)`, `GetUnclampedStatValue(...)`, `RefreshStats()`; stats are summed over the `m_ActorData` container tree (class, path, mode, boss, trinkets, memories, buffs, tokens, quirks, run, battle, arena) | 4521-4551, 4870, 654-730 |
| HP | `m_Hp` (saved), `HpRaw`, `HpRounded`, `CurrentHpMax`, `UnwoundedHpMax`, `GetHpMax(bool includeWound, bool includeCombined)` | 243, 493-515, 4936 |
| Wounds (Kingdom) | `m_WoundPercent`, `ChangeWoundPercent(float, SourceType)` | 256, 5128 |
| Stress | `m_Stress` (saved), `Stress`, `StressMax`, `IsAtStressMax`, `GetIsOverstressed()`, `SetPendingOverstress/ApplyPendingOverstress()` | 254, 517, 548-560, 5214-5253 |
| Statuses | `m_Statuses` indexed by `ActorStatusType` (death's door etc.), `GetIsStatusActive(ActorStatusType)` | 306, 5325 |
| Skills | `m_CombatSkills : List<SkillInstance>`, `m_CombatSkillReplacements`, `GetCombatSkillInstance(string)`, `SetCombatSkillEquipped(string skillId, bool equip, bool refreshStats)`, `GetEquippedCombatSkillIds()`, `SetBaseCombatSkillIdsEquipped(IReadOnlyList<string>)`; limit `ActorDataClass.m_EquippedCombatSkillLimit` | 139-144, 2981, 2987, 3051, 3313; `ActorDataClass.cs:111` |
| Mastery / upgrades | `UnlockSkill(UnlockDefinition unlockDefinition, SourceType sourceType)` (swaps the skill id to its upgraded id and records it in `m_UnlockContainer`); `SkillInstance.SetIsUnlocked()`; `SkillInstance.GetIsUpgraded()` | 3779-3783, 350; `Assets.Code.Skill/SkillInstance.cs:140-154` |
| Quirks and diseases | `QuirkContainer` (`Assets.Code.Quirk.QuirkContainer : ActorContainer<QuirkInstance, QuirkDefinition>`); diseases are quirks tagged `"disease"`, curses `"quirk_curse"` | 578; `Assets.Code.Quirk/QuirkDefinition.cs:21-75` |
| Tokens / buffs / DOTs | `TokenContainer`, `BuffContainer`, `DotContainer` | 572-576 |
| Trinkets | `GetTrinketInventory()` (`TrinketItemInventory`, slots from `HeroRules.m_TrinketNumberOfSlots` = 2) | 3716; `Assets.Code.Trinket/TrinketItemInventory.cs:25-27`; `hero_rules_data_export.Group.csv:2` |
| Combat items | `GetCombatSkillInventory()` (`CombatItemInventory`), `AddCombatSkillInventoryItem(ItemDefinition, int qty = 1)` | 3648, 3703; `Assets.Code.Actor/CombatItemInventory.cs:9-49` |
| Memories | `GetMemoryInventory()` (`MemoryItemInventory`, 5 slots), `IsMemoried()` | 3748-3777; `Assets.Code.Memory/MemoryItemInventory.cs:9-18`; `hero_rules_data_export.Group.csv:4` |
| Hero path | `ActorDataPath` property, `SetActorPath(ActorDataPath actorDataPath, bool refundUnlockedSkills)`, `SetActorPathToDefault()`, `SetActorPathToRandom(bool)`, `SetActorPathToReserve()` | 431, 2382-2459 |
| Mode (stance) | `ActorDataMode`, `SetActorMode(ActorDataMode)`, `SetActorModeToDefault()` | 433, 2475-2491 |
| Position | `m_TeamPosition`, `m_FrontRank`, `SetTeamPosition(int, bool, bool)`, `SetFrontRank(int)`, `GetFrontRank()` | 163, 183, 3869, 5260-5265 |
| Counters | `m_ActorCountInstances`, `IncrementActorCount(ActorCountType, ...)`, `GetActorCount(...)` | 311, 5412-5433 |
| Lifecycle flags | `m_IsLiving`, `m_IsInParty`, `m_IsInBattle`, `m_IsInRun`, `m_IsSpawned`, `m_IsCaptured`, `m_IsHired`, `m_IsVisitingInn`, `m_EverCursed` | 280-296 |
| Run goals / loadout | `m_RunGoalDefinition`, `m_CompletedRunGoalIds`, `m_CurrentActorLoadoutGuid` (`Assets.Code.Loadout.ActorLoadout`) | 373-384; `Assets.Code.Loadout/ActorLoadout.cs:12-55` |

`SkillInstance` (`Assets.Code.Skill/SkillInstance.cs:13-43`): `m_SkillId`, `m_SourceType`,
`m_IsAlwaysEquipped`, `m_IsUnlocked`, `m_IsEquipped`, `m_EquippedOrder`, `m_SkillModifier`. Vanilla
upgrade code: `InnUpgradeSkillsBhv.ApplySkillToUpgrade` uses `SkillUtils.GetUnlockFromSkillId(skillId)`
then `actorInst.UnlockSkill(unlock, SourceType.INN)`; unlocking a locked skill spends
`RunValueType.HERO_UPGRADE_POINTS` and calls `SetIsUnlocked()`
(`Assets.Code.UI.Screens/InnUpgradeSkillsBhv.cs:244-272`).

### 5.2 Creating a hero from data

Hero class ids (`ActorDataClass` element ids in `hero_*_data_export.Group.csv`):
`flagellant, grave_robber, hellion, highwayman, jester, leper, man_at_arms, occultist,
plague_doctor, runaway, vestal`; DLC `crusader`, `duelist` (`dlc_dul_cru/hero_cru_data_export.Group.csv`,
`hero_dul_...`), `abomination` (`dlc_catacombs/hero_abm_data_export.Group.csv`); the hireable
`bounty_hunter` (`RosterManager.cs:391`; data `expedition/hero_bh_data_export.Group.csv`). Each has a
`<id>_corpse` death class.

Factory:

- `uint LibraryActors.CreateActor(string actorDataId)` (`LibraryActors.cs:85-108`). Accepts a class id,
  a skin id (`ResourceDatabaseActorSkins`) or a weapon-kit id; resolves the base `ResourceActor` via
  `Singleton<ResourceDatabaseActors>.Instance.GetResource(actorDataId)`; returns the new guid
  (0 on failure).
- `uint LibraryActors.CreateActor(ResourceActor actorResource, ResourceActorSkin actorSkinResource = null, ResourceActorWeaponKit actorWeaponKitResource = null)` (`:116-122`).
- `void LibraryActors.CreateActorInstance(uint actorGuid, ResourceActor, ResourceActorSkin, ResourceActorWeaponKit)`
  (`:124-131`) — explicit guid: `new ActorInstance(actorResource, actorGuid)`, `SetActorSkin`,
  `SetActorWeaponKit`, `PostCreate(isLoad: false, actorResource)`, `AddLibraryElement(actorInstance, overrideCSV: false)`.
- `uint LibraryActors.CreateActorIfNecessary(string actorDataId)` (`:110-114`) — first existing
  instance with that class/skin/kit id, else create.
- Access: `LibraryActors.LibraryActorsInstance` (`:19`);
  `SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance.GetLibraryElement(guid)`.
- Guids come from the private counter `m_NextActorGuid` (starts at 1; `:17`, `:166-172`), shared by
  heroes and monsters.

What `ActorInstance.PostCreate(bool isLoad, ResourceActor actorResource)` does for a new actor
(`ActorInstance.cs:757-902`): `SetInitialSkills` (reads `Singleton<GameTypeMgr>.Instance.CurrentGameType`
and, in Kingdom, `KingdomBhv.Instance.KingdomDifficulty`; unlock state from the profile via
`UnlockUtils.ExpeditionProfileNotExpeditionLocked`; `:2696-2792`), `SetInitialActorPath` (`:2390-2412`),
`SetActorModeToDefault`, `BossCalculation.RollBossModifier`, `OnActorSetClass(null)` (HP to max,
stress 0, `m_IsLiving = true`; `:2221-2270`), and — **only if `GameTypeMgr.IsGameTypeStarted`** —
`OnRunStart()` -> `OnAddedToRun()` which dereferences `TorchManager`, `RunValues`, `BiomeManager`,
`RunDataManager` (`:871-874`, `6705-6820`). An actor created before a game type starts picks these
up later through `HandleEventGameTypeStarted` (`:2135-2168`).

Quirks are not rolled in `PostCreate`. They are generated by
`QuirkContainer.GenerateInitialQuirks(bool clearSeedInstance)` (`Assets.Code.Quirk/QuirkContainer.cs:214-248`)
from `ActorInstance.OnSelectBoss()` in Expedition (`ActorInstance.cs:4000-4023`) or
`ActorInstance.OnAddedToRoster()` in Kingdom (`:6367-6378`). In Expedition the generation is seeded
per class from the profile (`GameType.EXPEDITION.m_IsProfileSeedUsedForQuirkGeneration`,
`Assets.Code.Game/GameType.cs:10`; `QuirkContainer.cs:217-237`), so two heroes of the same class get
the same starting quirks unless `RerollQuirks()` is used.

How the roster creates heroes: `RosterManager.CreateRosterEntry(ResourceActor rosterActorResource, RosterStatusType heroSelectRosterStatusType)`
(private, `RosterManager.cs:470-484`) = `LibraryActors.CreateActor(resource)` + `new RosterEntry(resource.name, guid)`
+ `SetRosterStatus(...)` + `actor.OnAddedToRoster()`. It is driven by
`AddMissingHeroesToRoster(bool isAllActorsUnlocked, bool ignoreActiveEntryLimit, RosterStatusType heroSelectRosterStatusType)`
(`:424-468`), which adds one entry for every `ActorDataClass` with `IsPopulateInRoster`, owned DLC,
`GetIsUnlocked()`, a starting status valid for the game type and **`!GetIsActorInRoster(item.Id)`**.

### 5.3 How the party of four is stored

- `Assets.Code.Roster.RosterManager` (`RosterManager.cs:35`), reachable as
  `Singleton<GameTypeMgr>.Instance.RosterManager`. Created by `CampaignBhv.CreateManagers()` in
  Expedition (`CampaignBhv.cs:103-115`; it belongs to the campaign, not the run) or by
  `KingdomBhv.CreateManagers()` in Kingdom (`KingdomBhv.cs:655`).
- State: `private List<RosterEntry> m_Entries` (`:39`), `m_ActiveEntryLimit` (`:42`),
  `m_RosterReplacementType` (`:45`). `public const int FULL_PARTY_SIZE = 4` (`:37`).
- `RosterEntry` (`Assets.Code.Roster/RosterEntry.cs:10-93`): `m_ActorClassId`, `m_ActorGuid`,
  `m_RosterStatus`, `m_RosterStatusDurationCounts`, `m_SourceActorGuid`;
  `SetRosterStatus(RosterStatusType rosterStatus, uint sourceActorGuid)` raises
  `EventRosterEntryStatusChanged`.
- `RosterStatusType` (`Assets.Code.Roster/RosterStatusType.cs:9-25`): `IDLE, PARTY, RESERVE, DEAD,
  LOAD, CAPTURED, HIRE, HIRE_REPLACED, KINGDOM`.
- The party is simply the entries with status `PARTY`: `GetActorGuids(RosterStatusType.PARTY)`
  (sorted by front rank, `:1391-1420`), `GetPartyActors()` (`:1016-1032`), `GetIsActorInParty(uint)`
  (`:993`), `GetIsPartyDead()` (`:1062`), `GetStatusCount(RosterStatusType)` (`:1460`).
- Normal party formation: `EventRosterConfirmParty.Trigger(IReadOnlyList<uint> partyActorGuids, IReadOnlyList<uint> preferedKingdomActorGuids)`
  from `HeroSelectBhv` (`Assets.Code.Campaign/HeroSelectBhv.cs:1937`) ->
  `RosterManager.HandleEventRosterConfirmParty` (`:242-257`) sets each entry to `PARTY` and calls
  `OnConfirmParty` (`:615-643`): Expedition -> `FillReserve()` (all `IDLE` become `RESERVE`, `:531-537`);
  Kingdom -> `FillKingdom(...)` (all `IDLE` are placed into inn cells with status `KINGDOM`, `:592-613`).
- Per-actor reaction: `ActorInstance.HandleEventRosterEntryStatusChanged` (`ActorInstance.cs:1365-1419`)
  -> `OnAddedToParty` (sets `m_IsInParty`, `AttemptSpawn()`, assigns team position / front rank =
  party count - 1 when no rank is set; `:4188-4213`), `OnRemovedFromParty` (`:4215-4226`); entering
  `RESERVE` switches non-memoried heroes to the reserve path (`:1399-1402`); status enter/exit
  effects (`ROSTER_STATUS_EXIT/ENTER`, `:1403-1412`).
- Other public mutators: `TryRemovePartyActor(uint actorGuid, RosterStatusType removeRosterStatusType)`
  (`:1067-1077`), `AddReplacementActorToParty(uint replacementActorGuid, uint replacingActorGuid, int teamPosition)`
  (`:1209-1230`), `Hire(uint hireActorGuid, uint replacingActorGuid)` (`:1170-1178`),
  `SetAllStatuses(RosterStatusType fromStatus, RosterStatusType toStatus, Action<uint> onRosterStatusChanged)`
  (`:1580-1590`). Entry lookup (`GetRosterEntryByActorGuid`, `:1519`) is private; the read-only view
  `GetReadOnlyRosterEntryByActorGuid(uint)` (`:1557`) is public.

### 5.4 Serialization

- Hero: `JToken ActorInstance.SaveToJson()` (`ActorInstance.cs:938-965`) =
  `JsonSerializationUtils.PerFieldToJson(this)` (every instance field not marked
  `[DontSerializeToJson]`: guid, data ids, skin/kit/palette, name, skills and replacements, hp,
  stress, wound, positions, flags, counters, cooldowns, histories, unlock container, run goals,
  loadout guid, respawn data) plus keys `"combat_inventory"`, `"trinket_inventory"`,
  `"memory_inventory"`, one entry per container (`IActorContainer.SaveToJson(jObject)`; the quirk
  key is `"QuirkContainer"`, `:86`), `"actor_path"`, `"actor_mode"`, `"boss_modifier"`, `"ai_runner"`.
- Load: `ActorInstance.LoadFromJson(JObject data)` (`:967-1006`) then `PostCreate(isLoad: true, resource)`.
  The full sequence is in `LibraryActors.LoadFromJson(JObject loadedJson)` (`LibraryActors.cs:42-78`):
  parse each property with `ActorInfo.Parse(jObject, validate: true)`
  (`Assets.Code.Actor/ActorInfo.cs:23-41`; uses `"m_ActorGuid"` and the last of `"m_ActorDataIds"`),
  `new ActorInstance(resource, guid)`, `UpdateNextActorGuid(guid)`, `LoadFromJson`, `PostCreate`,
  `AddLibraryElement`. After a campaign/kingdom load the inventories need
  `PostLoadFromJson()` (`ActorInstance.cs:2108-2133`).
- File: `LibraryActors.SaveToJson(string saveFolder, string actorFilename)` writes one JObject with
  properties named `$"ActorGUID{guid}"` for every actor where `GetShouldSave()` is true
  (`LibraryActors.cs:21-40`), through `PlatformMgr.Instance.SaveRunJson(path, jObject, RunSaveFile.ACTORS)`.
  Called from `CampaignBhv.Save` (`CampaignBhv.cs:201-215`) and `KingdomBhv.Save` (`KingdomBhv.cs:601-635`).
- Roster: `RosterManager.SaveToJson()` = `PerFieldToJson(this)` (`RosterManager.cs:1135-1138`), stored
  under `RunSaveFile.ROSTER` inside the campaign JSON (`CampaignBhv.cs:208`) or kingdom JSON
  (`KingdomBhv.cs:621`). `LoadFromJson` (`:1140-1149`) runs `RemoveInvalidRosterEntries(true)` and
  replays `EventRosterEntryStatusChanged` from `RosterStatusType.LOAD`.
- `ActorInstance.GetShouldSave()` (`ActorInstance.cs:911-936`): false if destroyed; otherwise requires
  `m_IsLiving`, and — when the actor's **class id** is not in the roster — a team. With
  `RosterReplacementType.RESPAWN`, roster members (by guid) are always saved, even dead.

### 5.5 Rosters larger than four

There is no DD1-style free roster. What exists:

- Expedition: the roster holds **one entry per unlocked hero class**. Four are `PARTY`, the rest
  `RESERVE` (`FillReserve`, `RosterManager.cs:531-537`); reserves are the pool for replacements at
  inns (`GetReplacementActorGuids`, `:1180-1186`) and `GameType.EXPEDITION.m_IsMissingHeroesAddedAtInn`
  re-adds dead classes at each inn (`GameType.cs:10`, `RosterManager.cs:343-350`).
- Kingdom: `KingdomDifficultyValueType.ROSTER_ACTIVE_ENTRY_LIMIT` (override range 4..13,
  `Assets.Code.Kingdom/KingdomDifficultyValueType.cs:21`) is passed to
  `RosterManager.InitPostLoad(int activeEntryLimit, RosterReplacementType rosterReplacementType)`
  (`KingdomBhv.cs:752`; `RosterManager.cs:77-81`). Non-party heroes have status `KINGDOM` and are
  garrisoned in inns: `KingdomMapCellBase.m_ActorGuids`, four slots per cell
  (`Assets.Code.Kingdom/KingdomMapCellBase.cs:19`, `75-82`, `AddActor`/`RemoveActor` `:110-139`).
  Replacement rules: `RosterReplacementType.NONE / RESPAWN / REFILL`
  (`Assets.Code.Roster/RosterReplacementType.cs:9-13`); `RefillKingdomMap()` at day start respawns dead
  heroes (`TryRespawn` -> `ActorInstance.Respawn(ActorDataClass)`, `RosterManager.cs:1342-1360`,
  `ActorInstance.cs:4311-4381`) or adds missing classes (`:539-590`); in `REFILL` a dead hero's entry
  is removed (`:189-204`). Heroes at the current cell are the swap pool (`:1187-1196`).
- One hero per class is enforced in both modes: `AddMissingHeroesToRoster` skips classes already in
  the roster (`:451`); `RemoveInvalidRosterEntries(bool isValidation)` deletes the earlier of two
  entries with the same `ActorClassId` (`:1319-1330`) and runs on `FillRoster` (`:93`), on
  `LoadFromJson` (`:1143`) and on leaving `ALTAR_OF_HOPE` (`:411`). Class-id lookups elsewhere
  assume uniqueness (`GetRosterEntryByActorClassId` `:1505`, `GetIsActorInRoster(string)` `:1545`,
  `LibraryActors.CreateActorIfNecessary`).
- The only code that creates a second entry of a class is the debug party builder
  (`SetPartyBasedOnClassIds`, `:960-979`: if the class entry is already `PARTY` it calls
  `CreateRosterEntry` again). So duplicates function at runtime until the next
  `RemoveInvalidRosterEntries` call.

### 5.6 Things that silently destroy or reset heroes (must be controlled by the mod)

| Event | Effect | Evidence |
|---|---|---|
| Any game-mode exit | `LibraryActors.RemoveUnsavedActors()` removes actors with `!GetShouldSave()` (dead, or class not in roster and no team) and calls `ActorInstance.Destroy()` | `GameTypeMgr.cs:343`; `LibraryActors.cs:80-83`, `198-202` |
| Battle end | team-0 actors not in the roster by guid are `Destroy()`ed; `OnBattleEnd` bookkeeping (restore pre-battle position, clear combat containers, heal up to `CombatRules.m_BattleEndMinHealthHealUpToPercent` = 0.2 of max) only runs for roster members | `Team.cs:107-119`; `ActorInstance.cs:4120-4149`; `combat_rules_data_export.Group.csv:8` |
| Run end (Expedition) | all actors not kept are removed; kept ones get `OnRunPersist()` -> `OnRunReset()`: trinkets and combat items cleared, skill upgrades removed, unlock container cleared, path reset, HP full, stress 0 | `CampaignBhv.cs:258-297`; `ActorInstance.cs:6727-6786` |
| Entering `MAIN_MENU` | run and campaign end (`m_isGameTypeEndPoint`) | `RunBhv.cs:150-153`; `CampaignBhv.cs:67-77` |
| Kingdom end | `RemoveAllLibraryElements()` | `KingdomBhv.cs:470-480` |
| Leaving `ALTAR_OF_HOPE` | removes survivors without memories | `RosterManager.cs:404-422` |
| Entering `HERO_SELECT` | all `PARTY`/`RESERVE` back to `IDLE` | `RosterManager.cs:371-380` |

### Hook points for the mod

- Create: `LibraryActors.LibraryActorsInstance.CreateActor("plague_doctor")` (after a game type has
  started), then `GetLibraryElement(guid)`. For explicit guids use `CreateActorInstance(...)` and
  `UpdateNextActorGuid(guid)`.
- Customise (all public): `SetActorName`, `SetActorPath(path, false)`, `SetCombatSkillEquipped`,
  `UnlockSkill(unlock, SourceType)`, `GetCombatSkillInstance(id).SetIsUnlocked()`,
  `QuirkContainer.Add(...)` / `RerollQuirks()`, `GetTrinketInventory().AddItems(itemDef, 1, false)`,
  `AddCombatSkillInventoryItem(itemDef, qty)`, `GetMemoryInventory().AddItems(...)`,
  `SetActorPalette(string)`, `SetActorSkin(IResourceActorArt)`. The debug party builder is a compact
  reference for every one of these calls (`RosterManager.cs:693-955`).
- Persist outside a run: store `actor.SaveToJson()` per hero in the mod's own file; restore with
  `LibraryActors.LibraryActorsInstance.LoadFromJson(JObject)` (a JObject whose property values are
  the saved hero objects) or the four explicit steps in 5.4, followed by `PostLoadFromJson()` on the
  three inventories. Guid collisions with the vanilla save's actors must be avoided by the mod
  (`m_NextActorGuid` is shared and private).
- Put the chosen four into the party: raise `EventRosterConfirmParty.Trigger(guids, null)` only if
  the vanilla side effects in `OnConfirmParty` are acceptable; otherwise set statuses directly via
  the private `m_Entries` / `GetRosterEntryByActorGuid` (Harmony `AccessTools`/`Traverse`) and
  `RosterEntry.SetRosterStatus(RosterStatusType.PARTY, 0u)`, then set `SetTeamPosition`/`SetFrontRank`
  0..3 for the desired marching order.
- Harmony targets needed for a DD1 roster:
  - `Assets.Code.Roster.RosterManager.RemoveInvalidRosterEntries(bool)` — allow duplicate classes.
  - `Assets.Code.Roster.RosterManager.AddMissingHeroesToRoster(...)` (private) — stop vanilla from
    auto-filling the roster.
  - `Assets.Code.Actor.ActorInstance.GetShouldSave()` and
    `Assets.Code.Actor.LibraryActors.RemoveUnsavedActors()` — keep benched/dead mod heroes alive in
    the library (or keep benched heroes only as JSON in the mod and instantiate just the party).
  - `Assets.Code.Campaign.CampaignBhv.OnRunEnd(...)` (private) and
    `Assets.Code.Actor.ActorInstance.OnRunReset()` — stop the per-run wipe of equipment/upgrades.
  - `Assets.Code.Roster.RosterManager.HandleEventRosterEntryStatusChanged` /
    `ActorInstance.HandleEventRosterEntryStatusChanged` if status side effects (reserve path,
    roster-status effects) are unwanted.

---

## 6. Out-of-combat effects

All of these work on an `ActorInstance` outside battle; vanilla story nodes, inns and road events use
the same calls. Outside battle `TeamIndex` is -1 and most combat-only listeners are inactive
(`ActorInstance.ApplyCombatEventEffects` returns unless `GameModeMgr.CurrentMode == GameModeType.COMBAT`,
`ActorInstance.cs:2178-2183`).

### 6.1 Direct calls on the hero

| Need | Call | Lines (ActorInstance.cs) | Notes |
|---|---|---|---|
| Damage | `ApplyHealthDamage(float damage, bool isCrit, bool isRiposte, ActorInstance damagingActor, DeathType deathType, SourceType sourceType, string sourceId, bool hasDisplayed)` | 4969-4976 | `damagingActor` is dereferenced (pass the hero itself, as the debug code does: `RosterManager.cs:722`). Runs the death's door logic: `HealthDamageCalculate` (`:5578-5633`) -> enter death's door (`ActorStatusType.DEATHS_DOOR`), survive check `ResistCalculation.CalculateDeath`, or `Kill(...)`. Raises `EventActorHealthDamage`. |
| Damage (no attacker object) | `ApplyHealthDamage(float damage, bool isCrit, bool isRiposte, uint damagingActorGuid, DeathType, SourceType, string sourceId, bool hasDisplayed, ActorHealthDamageCalculation healthDamageCalculation)` | 4978-4985 | caller supplies the calculation (`HealthDamageCalculate(ActorInstance damagingActor, SourceType damagingSource, float damage)`, `:5578`) |
| Heal | `ApplyHealthHeal(float heal, bool isCrit, SourceType sourceType, bool hasDisplayed)` | 5025-5037 | clamps to `CurrentHpMax`, clears death's door, raises `EventActorHealthHeal` |
| Stress | `ApplyStressDamage(float damage, bool canResist, SourceType sourceType, string sourceId, uint sourceActorGuid)` | 5174-5198 | clamps to `StressMax`; raises `EventStressDamage`. Meltdown/resolve handling is done by `StressManager` (`Singleton<GameTypeMgr>.Instance.StressManager.AttemptProcessTrigger()`, polled in `Battle.Update` `Battle.cs:150-153` and in driving `Assets.Code.Game/DrivingSimulationBhv.cs:32-38`) and `ApplyPendingOverstress()` (`:5239-5253`); whether overstress resolves in a custom mode without those polls is not verified |
| Stress heal | `ApplyStressHeal(float heal, SourceType sourceType)` | 5200-5212 | |
| Wound | `ChangeWoundPercent(float woundPercentChange, SourceType sourceType)` | 5128-5145 | Kingdom mechanic (max HP reduction) |
| Kill | `Kill(DeathType deathType, SourceType sourceType, string sourceId, float killDamage, uint killingActorGuid)` | 5039-5064 | see 6.4 |
| Add quirk / disease | `actor.QuirkContainer.Add(QuirkDefinition quirkDefinition, SourceType sourceType, string sourceId, uint sourceActorGuid)` | `Assets.Code.Quirk/QuirkContainer.cs:250-273` | definitions from `SingletonMonoBehaviour<Library<string, QuirkDefinition>>.Instance.GetLibraryElement(id)`; ignored if already present; curse/disease source types are forced; `LimitQuirks()` enforces per-tag caps and removes diseases when cursed (`:305-329`) |
| Random quirks | `QuirkContainer.AddRandomQuirks(string rarity, string tag, int amount, SourceType, string sourceId, uint sourceActorGuid)` | `QuirkContainer.cs:275-291` | tags: `positive`, `negative`, `disease`, `quirk_curse` (`QuirkDefinition.cs:21-27`) |
| Remove quirk / disease | inherited `ActorContainer` methods: `RemoveInstanceWithId(string elementId, bool isRandom, bool isSourceOnly, SourceType, string sourceId, uint sourceActorGuid, bool sendEvent = true)`, `RemoveAllInstancesWithTag(string tag, ...)`, `RemoveInstancesWithTag(string instanceTag, int numberOfInstancesToRemove, ...)`, `Remove(instance, ...)` | `Assets.Code.Actor.ActorContainer/ActorContainer.cs:419`, `509`, `532`, `220` | raises `EventQuirkRemoved` and strips quirk-sourced buffs (`QuirkContainer.cs:123-139`) |
| Trinket | `actor.GetTrinketInventory().AddItems(ItemDefinition itemDef, int qty, bool isPurchase)`; remove `RemoveItem(ItemDefinition, int)` / `TryRemoveItem` | `Assets.Code.Item/ItemInventory.cs:779`, `1171`, `483` | check `GetCanEquip(def, checkInventory: false, checkConditions: true)` as vanilla does (`ActorInstance.cs:2579-2584`) |
| Combat item | `actor.AddCombatSkillInventoryItem(ItemDefinition, int qty = 1)` | 3703-3714 | |
| Token / buff / DOT | `actor.TokenContainer`, `BuffContainer`, `DotContainer` | 572-576 | |

### 6.2 The data-driven route: effects

Vanilla applies almost everything through `EffectDefinition`s (CSV `effect_data_export.Group.csv`,
library `Library<string, EffectDefinition>`). One call covers damage, heal, stress, quirks, tokens,
loot, run values, kill, summons:

- `EffectApply.Apply(ActorInstance actor, EffectInstance effectInstance)`
  (`Assets.Code.Effect/EffectApply.cs:103-106`) with
  `new EffectInstance(EffectDefinition effect, SourceType sourceType, string sourceId, ActorInstance applyPerformerActor, ActorInstance applyTargetActor, float valueChange, float multiplier, bool isCritOverride, bool isCritResistOverride, bool isCritCalculationValid, bool isPopTextValid, bool isUiValid)`
  (`Assets.Code.Effect/EffectInstance.cs:217`); usage example `RosterManager.cs:883-887`.
- `EffectApply.Apply(AppliedEffects.Input<EffectDefinition> effectsInput, SourceType sourceType, string sourceId)`
  (`EffectApply.cs:22-25`) with `new AppliedEffects.Input<EffectDefinition>(IReadOnlyList<EffectDefinition> effects, ActorInstance actor)`
  (`Assets.Code.Effect/AppliedEffects.cs:206`) — evaluates chance/conditions; this is how retreat,
  stall and siege-loss effects are applied (`BattleStateMachine.cs:357-364`,
  `KingdomSiegeManager.cs:473-483`). Actorless form: `new AppliedEffects.Input<EffectDefinition>(effects)`
  (`AppliedEffects.cs:211`).
- Relevant `EffectDefinition` fields (`Assets.Code.Effect/EffectDefinition.cs`): `m_Chance` (29),
  `m_StressDamage` (53), `m_StressHeal` (65), `m_HealthDamageAmount` / `m_HealthDamagePercent`
  (139-145), `m_HealthHealAmount` / `m_HealthHealPercent` / `m_HealthHealUpToPercent` (151-161),
  `m_WoundAddPercent` (165), quirks `m_Quirks` / `m_QuirkAddTag` / `m_QuirkAddRarity` /
  `m_QuirkAddAmount` / `m_QuirkRemoveId` / `m_QuirkRemoveTag` / `m_QuirkRemoveAmount` (185-203),
  `m_IsKill` (245), `m_RunValues` (265), `m_LootIds` (267), `m_LoseCombat` / `m_WinCombat` (307-309),
  token/buff/DOT add/remove (79-123, 173-243).

### 6.3 Items, trinkets, currency

- Party stash: `Singleton<GameTypeMgr>.Instance.PlayerInventory`
  (`Assets.Code.Player.PlayerItemInventory : ItemInventory`). Give:
  `AddItemsWithOverflow(ItemDefinition itemDef, int qty, bool isPurchase)`
  (`Assets.Code.Item/ItemInventory.cs:858`) or `AddItems(...)` (`:779`); take: `RemoveItem(def, qty)`
  (`:1171`); query: `GetItemQty(...)` (`:357-394`). Definitions:
  `SingletonMonoBehaviour<Library<string, ItemDefinition>>.Instance.GetLibraryElement(id)`.
- Gold is an item: `PlayerItemInventory.GetGoldQty()` =
  `GetItemQty(RulesManager.GetRules<InventoryRules>().GOLD)`
  (`Assets.Code.Player/PlayerItemInventory.cs:169-172`; `Assets.Code.Game/InventoryRules.cs:49-59`).
- Run values (mastery points and other per-run counters): `Singleton<GameTypeMgr>.Instance.RunValues`
  — `ChangeValue(RunValueType runValueType, float changeValue, SourceType sourceType)`
  (`Assets.Code.Run/RunValues.cs:326`), `SetValue` (`:296`), `GetValue` (`:242`). Types: `TORCH`,
  `HERO_UPGRADE_POINTS`, `DOOM`, `STAGE_COACH_ARMOR`, `STAGE_COACH_WHEELS`, `ESCALATION`
  (`Assets.Code.Run/RunValueType.cs:24-34`).
- Loot by table id with the vanilla window:
  `Singleton<GameTypeMgr>.Instance.LootManager.AddAndShowLoot(IReadOnlyList<string> lootIds, ItemInventory lootTargetInventory, LootReason reason, string reasonId, SourceType sourceType, UnityAction onFinished = null)`
  (`Assets.Code.Loot/LootManager.cs:452`); silent: `AddAndDontShowLoot(...)` (`:480`); rolling only:
  `LibraryLoot.CollectLoot(IReadOnlyList<string> lootIds, IRandomIdentifierContainer randomIdentifier, Func<Reward<LootType>, bool> additionalCheck = null)`
  (`Assets.Code.Loot/LibraryLoot.cs:73`), `LibraryLoot.CollectItems(...)` (`:162`).
- Profile currency (candles) is a profile value (`m_ProfileValueType,candles` in
  `hero_rules_data_export.Group.csv:10-12`); its API was not traced in this pass — not verified.

### 6.4 Death and the hero death flow

`ActorInstance.Kill(...)` (`ActorInstance.cs:5050-5064`): sets `m_IsLiving = false`, applies
`ActorDataEffectType.DEATH` effects if `deathType.m_IsEffectsValid`, raises
`EventActorDeath.Trigger(killingActorGuids, m_ActorGuid, ActorDataId, deathType, sourceType, sourceIds, killDamage, isDeathClassValid, TeamIndex)`
(`Assets.Code.Actor.Events/EventActorDeath.cs`). `DeathType` values: `SKILL, EFFECT, CHAIN, TEAM,
ROUND, CAPTURE, DEBUG` (`Assets.Code.Actor/DeathType.cs:9-21`).

Listeners that matter for a hero:

- `RosterManager.HandleEventActorDeath` (`RosterManager.cs:189-204`): status -> `DEAD` with the
  killer as source (or entry removed under `REFILL`); releases anyone this actor had captured.
- `ActorInstance.HandleEventActorDeathLogic` (`ActorInstance.cs:1198-1256`): kill counters for the
  killer; the dying hero's trinkets/combat items go to the next loot window
  (`LootManager.EmptyInventoryIntoNextWindowLoot`); if `m_IsDeathClassValid`, the actor changes class
  to `m_DeathActorDataId` via `EventActorChangeClass.Trigger(...)` (the `<hero>_corpse` class, taken
  from `ResourceActor.m_DeathClassResource`, `:2274`); under `RESPAWN` it snapshots skills/path
  (`OnRespawnEnabledDeath`, `:4289-4309`).
- `ActorInstance.HandleEventActorDeathEffects` (`:1258-1291`): only for actors with a team
  (in-battle reactions of allies/enemies).
- `StressManager`, `AffinityManager`, `BarkManager`, `ActOutManager`, `RunLogManager`,
  `TorchManager`, `KingdomManager` (hero death counter, `KingdomManager.cs:276-287`),
  `KingdomMapManager`, narration, achievements, analytics — see the `AddListener<EventActorDeath>`
  grep; individual handlers were not read (not verified what each does outside combat).
- Cleanup: a dead actor fails `GetShouldSave()` and is removed from the actor library at the next
  game-mode exit (5.6); the `DEAD` roster entry stays (`RosterStatusType.DEAD` has
  `isActorRequired: false`, `Assets.Code.Roster/RosterStatusType.cs:15`).
- Captured heroes still captured at battle end are killed with `DeathType.CAPTURE`
  (`RosterManager.cs:320-327`).

### Hook points for the mod

- Use the direct `ActorInstance` methods in 6.1 for scripted curio results; use
  `EffectApply.Apply(...)` with existing or mod-added `EffectDefinition`s when vanilla presentation
  (pop text, resist rolls, UI refresh) is wanted.
- Observe: `EventManager.AddListener<EventActorDeath>`, `EventActorHealthDamage`,
  `EventActorHealthHeal`, `EventStressDamage`, `EventStressHeal`, `EventQuirkAdded`,
  `EventQuirkRemoved`, `EventActorOverstress`, `EventRosterEntryStatusChanged`.
- Harmony targets: `ActorInstance.Kill(DeathType, SourceType, IReadOnlyList<string>, float, IReadOnlyList<uint>)`
  (permadeath bookkeeping for the hamlet graveyard),
  `RosterManager.HandleEventActorDeath(EventActorDeath)` (private; keep the entry / move to the
  mod's graveyard), `ActorInstance.HandleEventActorDeathLogic` (private; where a hero's gear goes),
  `LootManager.EmptyInventoryIntoNextWindowLoot(ItemInventory)`.
- Stress resolution outside COMBAT/DRIVING: call
  `Singleton<GameTypeMgr>.Instance.StressManager.AttemptProcessTrigger()` from the mod's own update
  loop (mirrors `DrivingSimulationBhv.MonoUpdate`) — behaviour in a custom mode is not verified.

---

## 7. Enemy catalogue

### 7.1 Monster actor data

- Class data: `SingletonMonoBehaviour<Library<string, ActorDataClass>>.Instance.GetLibraryElement(id)`
  (concrete library `LibraryActorDataClass`, `Assets.Code.Actor/LibraryActorDataClass.cs`). Useful
  members (`Assets.Code.Actor/ActorDataClass.cs`): `m_Size` (43), tags via `ContainsTag(string)` /
  `GetPotentialTags()` (used at 213-217), `IsBiomeBoss` / `IsExpeditionBoss` / `IsGangBoss`
  (213-217; tags `biome_boss`, `end_boss`, `gang_boss` from `driving_rules_data_export.Group.csv:16-17`
  and `kingdom_rules_data_export.Group.csv:23`), `m_DeathLootIds` (95), `m_DeathFrontActorClassIds` /
  `m_DeathBackActorClassIds` (91-93), `m_IsRetreatInvalidating` (85), `m_IsBattleComplete` (67),
  `ActorDataStats` (177), `DLCNumber` (245), `IsPopulateInRoster` (209, false for monsters).
- Prefab/visual resource: `Singleton<ResourceDatabaseActors>.Instance.GetResource(actorDataId)`
  (`ResourceActor`), `SingletonMonoBehaviour<ActorCreateGameObjectBhv>.Instance.GetActorAccessorById(id)`
  (`Assets.Code.Actor/ActorCreateGameObjectBhv.cs:221`).
- Data files: one CSV per monster named `<faction>_<monster>_data_export.Group.csv` (for example
  `fanatic_flayer_data_export.Group.csv`, `coastal_boss_leviathan_data_export.Group.csv`,
  `boss_brain lock health_data_export.Group.csv`), DLC content under `dlc_dul_cru/`,
  `dlc_catacombs/`, `expedition/`. Faction prefixes seen: `fanatic` (City), `plague_eater` (Farm),
  `lost_battalion` (Forest), `coastal` (Coast), `cave_swine` (Cave), `cultist`, `gaunt`,
  `pillager`, `beastmen`, `courtier`, `coven`, `vermin` (Catacombs DLC), `boss_*` (Mountain).
  The faction-to-biome mapping is taken from the table conditions in 7.3, not from a code table.
- There is no "monsters by biome" index in code. To enumerate, filter the library:
  `Library<string, ActorDataClass>.GetLibraryElements(Predicate<ActorDataClass> filterPredicate)`
  (`Assets.Code.Library/Library.cs:214`) by tag/prefix.

### 7.2 Battle configurations

- `SingletonMonoBehaviour<Library<string, BattleConfigurationDefinition>>.Instance.GetLibraryElement(id)`;
  typed accessor `LibraryBattleConfiguration.LibraryBattleConfigurationInstance`, tag list
  `AllTags` (`Assets.Code.Combat.BattleConfiguration/LibraryBattleConfiguration.cs:21-23`).
  Enumeration: `GetLibraryElements()`, `GetLibraryElements(predicate)`,
  `GetLibraryElementRandom(Func<TElement, float> chance, IRandomIdentifierContainer)`
  (`Library.cs:175`, `214`, `237`; random-pick example `CombatSystemsInstaller.cs:101-112`).
- 789 configurations in the base file `battle_configuration_data_export.Group.csv` (more in
  `dlc_dul_cru/battle_configuration_data_export_DLC1.Group.csv` and
  `dlc_catacombs/battle_configuration_data_export_DLC2.Group.csv`).
- Naming conventions (data): road/"mash" fights `<faction>_mash_<nnn>` (e.g. `fanatic_mash_201`,
  `fanatic_mash_301`, `fanatic_mash_BC_01`); lair waves `<biome>_dungeon_<wave><variant>` (e.g.
  `city_dungeon_1a` ... `city_dungeon_3_a`); lair bosses are wave 3:
  `city_dungeon_3_a` (enemies `fanatic_librarian_stack_l, fanatic_librarian_stack_m,
  fanatic_librarian_stack_m, fanatic_librarian`), `farm_dungeon_3` (`plague_eater_harvest_*`,
  tag `glossary_farm_boss`), `forest_dungeon_3` (`lost_battalion_boss_dreaming_general,
  lost_battalion_boss_taproot`), `coast_dungeon_3` (`coastal_boss_leviathan`), all with
  `m_RunDataStatsId,no_retreat`, `m_CompleteLootTables,lair_boss_rewards_all`,
  `hero_effects,lair_midfight_heal`; act bosses `mountain_boss_brain` (+ `_2`..`_5`),
  `mountain_boss_lungs`, `mountain_boss_eyes` (+ `_phase_2`), `mountain_boss_arms`,
  `mountain_boss_body` (each with its `combat_arena_mountain_boss_*` override); cultist guardians
  `cultist_guardian_biome_<n>_altar_<k>`.
- Sequencing fields (`m_NextBattleConfigurationId`, `m_NextBattleConfigurationTableId`,
  `m_IsNextBattleOptional`, `m_AdditionalBattleConfigurationTableId`) are resolved by
  `RollNextBattleConfiguration()` / `RollAdditionalBattleConfiguration()`
  (`BattleConfigurationDefinition.cs:367-418`).

### 7.3 Battle configuration tables (difficulty tiers and biome routing)

- `LibraryBattleConfigurationTables : Library<string, Table<BattleConfigurationOption, string>>`
  (`Assets.Code.Combat.BattleConfiguration/LibraryBattleConfigurationTables.cs:12`).
  - `static bool RollBattleConfiguration(string id, IRandomIdentifierContainer randomIdentifier, List<string> outBattleConfigId, Func<string, bool> additionalCheck = null)` (`:41-44`)
  - `static void CollectAllPossibleConfigurations(string id, bool isValidOnly, HashSet<string> outEventIds)` (`:46-49`)
- Option types: `battle_config`, `sub_table`, `nothing`
  (`Assets.Code.Combat.BattleConfiguration/BattleConfigurationOptionType.cs:9-13`).
- Option validity (`BattleConfigurationOption.GetIsValidOption`,
  `Assets.Code.Combat.BattleConfiguration/BattleConfigurationOption.cs:89-124`): base condition/unlock
  checks; for `battle_config` options `m_RunLimit` against
  `Singleton<GameTypeMgr>.Instance.CombatManager.GetBattleConfigurationHistoryCount(def)` (`:112`), or
  "not the same as the last fight" via `CombatManager.GetLastBattleConfigurationHistory()` (`:118`).
  Tag-based extra chance from `RunStatType.BATTLE_CONFIGURATION_CHANCE` (`:150-165`). Both require a
  started game type (`CombatManager` and `RunDataManager` non-null).
- Table structure (data, `battle_configuration_table_data_export.Group.csv`):
  - master tables route by game type: `faction_mashes_road_master` -> `..._confessions` /
    `..._kingdoms` on conditions `is_confessions` / `is_kingdoms` (`:1-7`);
  - then by biome: `faction_mashes_road_confessions` -> `plague_eater_mashes_normal_confessions`,
    `fanatic_mashes_normal_confessions`, `lost_battalion_mashes_normal_confessions`,
    `coastal_mashes_normal_confessions` on `farm_biome_check`, `city_biome_check`,
    `forest_biome_check`, `shroud_biome_check`;
  - then by progress: `faction_mashes_resist_confessions` -> `..._region_1/2/3_confessions` on
    `biome_typical_equals_1`, `biome_typical_equals_2`, `biome_typical_equal_or_greater_than_3`;
  - leaf tables per faction and tier, listing `battle_config` ids:
    `<faction>_mashes_normal`, `<faction>_mashes_hard`, `<faction>_mashes_normal_champions`,
    `<faction>_mashes_hard_champions`, `<faction>_mashes_brutal_champions` for `fanatic`,
    `plague_eater`, `lost_battalion`, `coastal`, `swine`; analogous sets for `cultist`
    (`cultist_mashes_normal/hard/..._enhanced`), `pillager`, `gaunt`, `beast`, `military`;
    lairs `biome_<city|farm|forest|coast|tundra>_dungeon_starters` / `_round2*` / `_round3`;
    guardians `cultist_guardian_biome_<n>_boss*`; ambush `ambush_mashes_master`,
    `camp_ambush_mashes(_e1.._e3)`; tutorial `intro_valley_road_mashes`.
  Example leaf: `fanatic_mashes_hard` -> `fanatic_mash_301, 302, 303, 306, 307, 308, 309`.
- Because the master/biome/progress layers depend on run state (`BiomeManager.GetActiveBiome()`,
  game type, biome count), the reliable way to get "a hard Sprawl fight" is to address the leaf
  table directly (`fanatic_mashes_hard`) or pick a config id.

### 7.4 Related catalogues

- Battle modifiers: `Library<string, BattleModifierDefinition>` (`battle_modifier_data_export.Group.csv`).
- Arena modifiers: `Library<string, ArenaModifierDefinition>`; start/stop via
  `EventArenaModifierStart` / `EventArenaModifierStop` (`Assets.Code.Combat/Arena.cs:61-91`).
- Summon controllers: `Library<string, SummonControllerConfigurationDefinition>` (346 configs set
  `m_EnemySummonControllerConfigurationId`).
- Bosses of an expedition: `Library<string, BossDefinition>` (`boss_data_export.Group.csv`: `prologue,
  brain, lungs, eyes, arms, body` plus `ExtendedBoss` and `BossModifier` elements);
  `BossDefinition.m_EndBiomeType`, `m_RunDataStatsId`
  (`Assets.Code.Boss/BossDefinition.cs:23`, `45`). `SingletonMonoBehaviour<RunBhv>.Instance.RunManager.Boss`
  is the selected one and is read by the boss end UI (`CombatPresentationBhv.cs:1124-1134`).
- Difficulty scaling stats: `RunDataStats` attached per config (`m_RunDataStatsId`, 177 configs) are
  added to `RunDataManager.m_BattleDataContainer` on `EventBattleConfigurationSelected` and cleared
  on `EventBattleResult` (`Assets.Code.Run/RunDataManager.cs:118-137`); run-level and boss stats
  scale monsters through the same `RunDataManager` (details not traced — not verified).

### Hook points for the mod

- "Boss X": `Library<string, BattleConfigurationDefinition>.GetLibraryElement("forest_dungeon_3")`
  (or `city_dungeon_3_a`, `farm_dungeon_3`, `coast_dungeon_3`, `mountain_boss_*`) and pass the id or
  a one-element list to `CombatScenarioData`. Starting at `<biome>_dungeon_1x` with the string-id
  constructor reproduces the full three-wave lair including the optional-continue prompts.
- "A hard Sprawl fight": `LibraryBattleConfigurationTables.RollBattleConfiguration("fanatic_mashes_hard", RandomIdentifier.COMBAT, list)`;
  or, to avoid `CombatManager` history effects and to control randomness,
  `CollectAllPossibleConfigurations("fanatic_mashes_hard", isValidOnly: false, set)` and choose in
  mod code.
- Custom enemy groups without editing game data: construct/inject a `BattleConfigurationDefinition`
  (`public BattleConfigurationDefinition(string id, string csvText)`,
  `BattleConfigurationDefinition.cs:166`; then `Init()`, `PostInit()`,
  `Library.AddLibraryElement(def, overrideCSV: false)` as `LibraryBattleConfiguration.InitInternal`
  does, `LibraryBattleConfiguration.cs:30-45`). The CSV text format is the `key,value,...` line format
  shown in 1.3. Not verified at runtime.
- Harmony targets: `BattleConfigurationOption.GetIsValidOption(bool, bool, ref bool)` (remove the
  "no repeat"/run-limit rules), `BattleConfigurationDefinition.RollAdditionalBattleConfiguration()`
  (suppress reinforcements), `CombatManager.CombatStart(CombatScenarioData)` (private; history
  bookkeeping).
- Guard rails when using act or gang bosses: see 4.5 (`end_boss` / `gang_boss` victories end the
  run/kingdom unless patched).

---

## Appendix A: game-mode machinery relevant to hosting a dungeon

- `GameModeType` (`Assets.Code.Game/GameModeType.cs:39-65`) is a closed `CustomEnum` with a private
  constructor: `DRIVING` (scene `MainScene`), `COMBAT` (`combat`), `HERO_SELECT`, `INN` (`inn`),
  `LOADING`, `HERO_STORY_INTRO`, `CINEMATIC`, `RESULTS` (`combat_results`), `MAIN_MENU`,
  `ALTAR_OF_HOPE`, `EMBARK`, `REALTIME_CINEMATIC`. Flags per mode: `m_isGameTypeStartPoint`,
  `m_isGameTypeEndPoint`, `m_isInBiome`, `m_canTransitionToSelf`, `m_isSceneLoadedAutomatically`,
  `m_inputMapName`.
- `GameModeMgr.SetMode` runs `ChangeModeAndNotify` (`GameModeMgr.cs:372-449`): exit callbacks
  (`IGameModeExitAsyncPreStart`, `IGameModeExitStart`, `IGameModeExitComplete`), fade, pool
  cleanup, then enter callbacks (`IGameModeEnterAsyncPreStart` polled until all return true,
  `IGameModeEnterStart`, `IGameModeEnterComplete`) on all installer systems and on all components in
  loaded scenes (`:120-213`, `464-607`). One-shot callbacks: `OnNextGameModeEnterStart/EnterComplete/ExitStart/ExitComplete(Action<GameModeType>)`
  (`:271-289`). `IsChangingState()` (`:105`) must be false before calling `SetMode`.
- `GameType` (`Assets.Code.Game/GameType.cs:10-16`): `EXPEDITION`, `KINGDOM`; switch with
  `Singleton<GameTypeMgr>.Instance.SetGameType(GameType)` (`GameTypeMgr.cs:384-392`). Kingdom start:
  `KingdomBhv.StartKingdom(GameModeType startGameModeType, KingdomDifficultyConfiguration difficultyConfiguration, KingdomMapDefinition mapDefinition, bool isLoad, bool loadModeByCell)`
  (`KingdomBhv.cs:423-468`; caller `Assets.Code.Kingdom.UI/KingdomCreationFlowBhv.cs:276`).

## Appendix B: open questions (not answered by static reading)

1. Which host is cheaper: Expedition (must tolerate `MainScene` + `MapMgrBhv`, must patch wipe =
   game over and the per-run hero reset) or Kingdom (off-road combat and survivable wipes are
   native, but day system, sieges and map come along). Needs an in-game spike.
2. In which game mode the dungeon itself lives. A new `GameModeType` cannot be created through the
   public API; `INN`, `DRIVING` or `EMBARK` would have to be reused, or the dungeon runs as an
   additive scene inside one of them. `MAIN_MENU` cannot host it (it ends run and campaign).
3. Whether duplicate hero classes survive everything outside the roster (affinity/relationship
   system, banter, profile quirk seeds, UI portraits keyed by class id). Only the roster and
   `LibraryActors.CreateActorIfNecessary` were checked.
4. Interaction between the vanilla save system (`SaveUtils.SaveCurrentGameMode` every turn) and mod
   state; whether to disable it via `DISABLE_SAVE_GAME` or to coexist.
5. Stress/overstress, affinity ticks and barks outside COMBAT/DRIVING (they are polled only there).
6. Whether `MapMgrBhv` needs a generated map to stay stable if the party never drives.
7. Runtime validity of injecting new `BattleConfigurationDefinition`s and of setting editor prefs in
   a release build.
