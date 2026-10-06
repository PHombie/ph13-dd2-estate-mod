# DD2 recon: top-level flow, game modes, persistence

Target: Darkest Dungeon II v2.04.85095 (Unity 2022.3.62f2, Mono). Source: decompiled `IronCrown.dll`, `LoadingSequencer.dll`.

Conventions
- All `file:line` references are relative to `D:\Mods\DD2\dd2-estate-mod\_ref\dd2-decomp\` (e.g. `IronCrown/Assets.Code.Game/GameModeMgr.cs:256`). Files in the global namespace sit directly in `IronCrown/`.
- **[NV]** = *not verified*: inferred from code shape, serialized scene data, or Unity/Steam convention, not read in decompiled code.
- Game install inspected read-only at `E:\Steam\steamapps\common\Darkest Dungeon® II\`. Save folders were not read. The game was not launched.
- Terminology trap: the code calls the *mode* (Confessions/Kingdoms) a **`GameType`** and the *state/screen* (driving, combat, inn...) a **`GameModeType`**. "Game mode" in code = state machine state.

---

## 1. Boot sequence, managers, scene loading, game states

### 1.1 Boot timeline

| # | What | Where |
|---|------|-------|
| 0 | `RuntimeInitializeOnLoad` (SubsystemRegistration): `SaveUtils.Init` resets save counters; `Globals.OnSubsystemRegistration` | `IronCrown/Assets.Code.Utils.Serialization/SaveUtils.cs:1807-1817`, `IronCrown/Globals.cs:246-250` |
| 1 | AfterAssembliesLoaded: `StaticInitializerAttribute.InitializeValues()` touches every `[StaticInitializer]` member listed in `StreamingAssets/StaticInitializerTypesAndMembers.txt` (type name / member name pairs) so all `CustomEnum<T>` statics (incl. `GameType`, `GameModeType`, `RunSaveFile`) are constructed in a fixed order | `IronCrown/Assets.Code.Utils.Attributes/StaticInitializerAttribute.cs:23-46, 70-95`; load type confirmed in `Darkest Dungeon II_Data/RuntimeInitializeOnLoads.json` (`loadTypes: 2`) |
| 2 | BeforeSceneLoad: `PlatformMgr.Initialize()` creates a DontDestroyOnLoad `PlatformMgr` GameObject with `SteamPlatformMgr` + `DLCManager`, calls `InitializePlatform()` (SteamAPI init, DLC ownership, starts Workshop UGC query) | `IronCrown/Assets.Code.Platform/PlatformMgr.cs:185-208`, `IronCrown/Assets.Code.Platform/SteamPlatformMgr.cs:42-123` |
| 3 | First scene: `LaunchSceneBhv.Awake()` → `Addressables.LoadSceneAsync("rh_splash_launch")`. **[NV]** that `LaunchSceneBhv` lives in build scene `level0` (only the class was read) | `IronCrown/Assets.Code.Loading/LaunchSceneBhv.cs:18-22` |
| 4 | Splash scene: `RedHookSplashSceneBhv.Awake()` starts the intro video and `PlayIntroCinematic()` (initializes Addressables, starts async load of the `GameInstaller` prefab via `m_gameInstallerAsset`) | `IronCrown/RedHookSplashSceneBhv.cs:104-120, 127-162` |
| 5 | Only when the video ends (`OnVideoEnd` → `ShowPUACard()`): legal card, waits for DLC query + Workshop mod query, **instantiates the `GameInstaller` prefab**, `DontDestroyOnLoad`, waits `PlatformMgr.IsSaveFilesPreloadingFinished()`, calls `m_gameInstaller.Install()` | `IronCrown/RedHookSplashSceneBhv.cs:122-125, 164-271` |
| 6 | `GameInstaller.Install()` sets `Globals.MainInstaller = this`, runs `SetupSystemResolvers()` → creates all core singletons, then runs LoadSequencer groups `"PreLoad"` and `"SystemStart"` over every system method tagged `[LoadSequencer]`/`[SystemStart]` | `IronCrown/GameInstaller.cs:84-91, 118-194`; `IronCrown/MonoBehaviourInstaller.cs:117-156` |
| 7 | `ProfileBhv.Initialize()` (a `[SystemStart]`) loads all profiles from disk, creates a default profile if none | `IronCrown/Assets.Code.Profile/ProfileBhv.cs:113-153` |
| 8 | `RedHookSceneManagerBhv` fires `EventScenesLoaded` once pending installers complete → `GameModeMgr.HandleScenesLoaded` → `SetModeByLoadedScenes()` → mode becomes `LOADING` (scene `rh_splash_launch`) | `IronCrown/Assets.Code.Loading/RedHookSceneManagerBhv.cs:147-187, 621-636`; `IronCrown/Assets.Code.Game/GameModeMgr.cs:215-254, 322-334`; `IronCrown/Assets.Code.Game/GameModeType.cs:315-339` |
| 9 | Splash waits `InstallPercentage >= 1` and mode != `UNDETERMINED`, then `RedHookSceneManagerBhv.LoadSceneAdditively("main_menu", null)` | `IronCrown/RedHookSplashSceneBhv.cs:276-286` |
| 10 | `main_menu` scene contains `MainMenuSystemsInstaller`; when it finishes: `if (GameModeMgr.CurrentMode == LOADING) SetMode(MAIN_MENU, isLoad:false)` | `IronCrown/MainMenuSystemsInstaller.cs:50-71` |
| 11 | `MainMenuUiScreenBhv.OnGameModeEnterStart(MAIN_MENU)` → `Show()` (unloads splash scene, refreshes Continue/New buttons, plays disclaimer/open timeline) | `IronCrown/Assets.Code.UI.Screens/MainMenuUiScreenBhv.cs:311-330, 369-434` |

The gameplay data libraries are **not** loaded at boot; they are (re)installed every time the player leaves the main menu (see 1.3 and 5.3).

### 1.2 Managers / singletons

Two singleton bases:
- `Assets.Code.Utils.Singleton<T>` (plain class): `Instance`, `Create()`, `HasInstance()`; `[AutoInitialize]` types self-create on first `Instance` access (`IronCrown/Assets.Code.Utils/Singleton.cs:8-66`).
- `Assets.Code.Utils.SingletonMonoBehaviour<T>`: `Instance` is set in `[SystemStart] Initialize()`, not in `Awake` (`IronCrown/Assets.Code.Utils/SingletonMonoBehaviour.cs:77-88`).

Registry: `Globals` (static). `Globals.MainInstaller` (`IMainInstaller`: `SystemsRoot`, `MainCamera`, `Systems`, `Loader`), `Globals.GetAllSystems()`, `Globals.RegisterPostInstallerCallback<T>(Action)`, `Globals.ArePendingInstallersComplete()` (`IronCrown/Globals.cs:31, 81-111, 252-281`; `IronCrown/IMainInstaller.cs`, `IronCrown/IInstaller.cs`).

Core systems created by `GameInstaller.SetupSystemResolvers()` (`IronCrown/GameInstaller.cs:118-194`):
- Plain singletons: `AddressableReferencesManager`, `EventManager`, **`GameModeMgr`**, **`GameTypeMgr`**, `ItemLoaderBhv`, `Localization`, `LUTManager`, `MapGenerationOverridesMgr`, **`ModMgr`**, `RichPresenceMgr`, `EventRecordingMgr`, `ResourceDatabaseKingdomMap`, `ResourceDatabaseGang` (lines 120-132).
- MonoBehaviour systems (prefab `AddressableResources/Systems/{TypeName}.prefab` or bare GameObject, parented under the installer; `IronCrown/MonoBehaviourInstaller.cs:266-339`): `MonoBehaviourRunner`, `AnalyticsBhv`, audio managers, `CommonUiBhv`, `DebugEditorPrefsButtonBhv`, `DebugMgr`, `GameSpeedMgr`, `InputSystemBhv`, `PlayerCollectionMgr`, `PlayerLoadoutMgr`, **`RedHookSceneManagerBhv`**, `RenderingManager`, **`SaveLoadMgr`**, `ScreenFaderBhv`, `ScreenStackBhv`, `TutorialMgr`, `MainMenuUIStateBhv`, **`KingdomBhv`**, **`CampaignBhv`**, **`ProfileBhv`**, **`RunBhv`**, `LibraryActors`, haptics libraries, `LibraryKingdomDifficulty` (lines 133-192).
- `DebugDevConsoleBhv` + `Console` only if `Debug.isDebugBuild || CommandLineUtils.IsConsoleEnabled()` (lines 176-180).

Scene-scoped installers (each is a `MonoBehaviourInstaller` component inside its scene, installs when the scene loads): `MainMenuSystemsInstaller` (`MainMenuControllerBhv`, `MainMenuAmbienceBhv`, optional `GameModeOverrideBhv`), `DrivingSystemsInstaller` (`GameUIBhv`, `MapMgrBhv`, `DrivingSimulationBhv`, `StoryBhv`...), `CombatSystemsInstaller` (`ArenaBhv`, `CombatBhv`, `CombatUiBhv`), `InnSystemsInstaller` (`InnPresentationBhv`, `InnUiBhv`), `AltarOfHopeSystemsInstaller` (`IronCrown/MainMenuSystemsInstaller.cs:50-58`, `IronCrown/DrivingSystemsInstaller.cs:40-50`, `IronCrown/CombatSystemsInstaller.cs:60-65`, `IronCrown/InnSystemsInstaller.cs:57-61`).

`PostMainMenuSystemsInstaller` (child of the GameInstaller) owns ~100 data libraries (`Library*`), `ResourceDatabase*` singletons, `InnBhv`, `AchievementsMgr`, `EmbarkBhv`, `RulesManager` (`IronCrown/PostMainMenuSystemsInstaller.cs:108-242`).

### 1.3 Loading sequencer

`LoadingSequencer.Src.LoadSequencer` discovers instance methods carrying `LoadSequencerAttribute` subclasses (`[SystemStart]` = group `"SystemStart"`, `[SystemStop]`, `[SystemClear]`, `[LoadSequencer(null,"PreLoad")]`) and runs them honouring a JSON `DependencyMap` (`LoadingSequencer/LoadingSequencer.Src/LoadSequencer.cs:131-177, 222-253, 331-346`; `SystemStartAttribute.cs:3-10`).
- `void` methods **without** `[RunInMainThread]` are wrapped in `new Task(...)` and `Start()`ed, i.e. they run **off the main thread** (`LoadSequencer.cs:150-159`; `Operation.cs:59-78`). `Library<,>.Initialize()` is such a method (`IronCrown/Assets.Code.Library/Library.cs:47-61`). Harmony patches on library init must be thread-safe and must not call Unity APIs.
- Methods returning `OperationExecutionState` are async with pending-task tracking (`LoadSequencer.cs:160-163`).

### 1.4 Scene loading

All scenes are Addressables. `RedHookSceneManagerBhv` wraps `Addressables.LoadSceneAsync(address, LoadSceneMode.Additive)` (`IronCrown/Assets.Code.Loading/RedHookSceneManagerBhv.cs:709-725`):
- `static bool LoadSceneAdditively(string sceneName, UnityEngine.Object source, bool setActive = false)` (also `AssetReference` / `IResourceLocation` overloads), reference-counted per `source` (lines 305-345).
- `static AsyncOperationHandle UnloadAdditiveScene(string sceneName, UnityEngine.Object source)`, `UnloadAdditiveSceneByForce(string)` (lines 347-355).
- After each load batch it raises `EventScenesLoaded` (after calling `IScenesLoadedStart.OnScenesLoadedStart()` on scene roots) (lines 621-665).

Scene names confirmed from bundle names in `StreamingAssets/aa/` (`scenes_scenes_<name>.bundle`): `altar_of_hope`, `camp`, `cinematic`, `combat`, `combat_results`, `combat_results_hero_story`, `emptyloading`, `hero_select`, `hero_story_intro`, `inn`, `mainscene`, `main_menu`, `main_menu_kingdom`, `relationship_test`, ~110 `combat_arena_*`, `combat_intro_boss_body`; plus `initial_loading_scenes_rh_splash_launch.bundle` and `kingdoms_map_scenes_basegame.bundle` / `kingdoms_gang_*_scenes_all.bundle`.

### 1.5 Game states (`GameModeType`) and the state machine

`Assets.Code.Game.GameModeType : CustomEnum<GameModeType>` (`IronCrown/Assets.Code.Game/GameModeType.cs:18-67`). Constructor (private, line 137):
`GameModeType(string name, int flag, SceneTransition defaultTransition, bool showTransitionThrobber, string sceneName, string inputMapName, bool isLoadedSceneSetMode, int loadedSceneModePriority, bool canTransitionToSelf, bool setSceneAsActive, bool isGameTypeStartPoint, bool isGameTypeEndPoint, bool isInBiome, bool isManualHandleGameOver, bool isRunEndCashOut, bool canAbandon, bool fillDebugRoster, bool isSceneLoadedAutomatically)`

| Mode | flag | scene | input map | start pt | end pt | inBiome | auto-load scene |
|---|---|---|---|---|---|---|---|
| `DEPRECATED_MENUS` | 1 | – | – | – | – | – | – |
| `DRIVING` | 2 | `MainScene` | Driving | yes | – | yes | no |
| `COMBAT` | 4 | `combat` | Combat | yes | – | yes | no |
| `HERO_SELECT` | 8 | `hero_select` | Driving | yes | – | yes | yes |
| `INN` | 0x10 | `inn` | Driving | yes | – | no | no |
| `LOADING` | 0x20 | `rh_splash_launch` | Driving | – | – | – | no |
| `HERO_STORY_INTRO` | 0x40 | `hero_story_intro` | Driving | yes | – | yes | no |
| `CINEMATIC` | 0x80 | `cinematic` | Cinematic | – | – | – | yes |
| `RESULTS` | 0x100 | `combat_results` | – | yes | – | yes | no |
| `MAIN_MENU` | 0x200 | `main_menu` | Driving | – | **yes** | – | yes |
| `ALTAR_OF_HOPE` | 0x400 | `altar_of_hope` | – | yes | – | yes | yes |
| `EMBARK` | 0x800 | (`embark*`) | – | yes | – | – | no |
| `REALTIME_CINEMATIC` | 0x1000 | – | – | yes | – | – | yes |
| `UNDETERMINED` | 0 | – | – | – | – | – | – |
| `SIZE` (sentinel, `name == null`) | | | | | | | |

Other per-mode flags: `m_isManualHandleGameOver` (COMBAT), `m_isRunEndCashOut` (INN), `m_canAbandon` (DRIVING, COMBAT), `m_fillDebugRoster`, `m_canTransitionToSelf` (DRIVING, COMBAT). Transition exceptions are a per-pair table built in `SetupExceptions()` (lines 107-135), executed when the `SIZE` sentinel is constructed (lines 157-160).

`Assets.Code.Game.GameModeMgr : Singleton<GameModeMgr>` (`IronCrown/Assets.Code.Game/GameModeMgr.cs`):
- `public void SetMode(GameModeType mode, bool isLoad, SceneTransition transitionOverride = null, bool? showTransitionThrobberOverride = null, bool unloadEverything = false, bool isGameOverTransition = false)` (256-269).
- `static GameModeType CurrentMode`, `GetPreviousMode()`, `GetNextMode()`, `IsChangingState()`, `static bool IsLoadingGameMode` (59-83, 105-118, 291-304).
- One-shot callbacks: `OnNextGameModeEnterStart/EnterComplete/ExitStart/ExitComplete(Action<GameModeType>)` (271-289).
- Transition coroutine `ChangeModeAndNotify` (372-449): (EXITING) `IGameModeExitAsyncPreStart` polling → disable input map → `IGameModeExitStart` → fade to black → `IGameModeExitComplete` → wait `Globals.ArePendingInstallersComplete()` (400-403) → `m_CurrentMode = next` → previous mode's auto scene unloaded → pools destroyed → `IGameModeChangeCleanup` → `Resources.UnloadUnusedAssets` + GC → `m_CurrentMode.LoadAddressableScene(setActive:true)` (only for `m_isSceneLoadedAutomatically` modes) → wait for scenes and `ModMgr.LoadingMods` → `PostScenesUnloaded` (464-525): (ENTERING) `IGameModeEnterAsyncPreStart` polling (repeats for scenes that got loaded meanwhile) → `IGameModeEnterStart` → `IGameModeEnterComplete` → fade in → enable the mode's input map (570-607).
- Who gets the callbacks (`CallOnAllInstallers<T>`, 120-146): every object in `Globals.GetAllSystems()` implementing the interface **plus every component under `Globals.MainInstaller.SystemsRoot` that implements it and is not a `SingletonMonoBehaviourBase`**; then every such component on root objects of loaded scenes (`CallOnScenes`, 168-213). Ordering via `IGameModeEnterStartPriorityOverride` etc. (`GameModeReceiverPriority { EARLIEST, EARLY, NORMAL, LATE, LATEST }`).
- Interfaces (`IronCrown/Assets.Code.Game/IGameMode*.cs`): `bool GameModeEnterAsyncPreStart(GameModeType previousGameMode, GameModeType nextGameMode, bool isLoad)`; `void OnGameModeEnterStart(GameModeType enteringGameMode, bool isLoad)`; `void OnGameModeEnterComplete(GameModeType, bool)`; `bool GameModeExitAsyncPreStart(GameModeType previousGameMode, GameModeType nextGameMode)`; `void OnGameModeExitStart(GameModeType)`; `void OnGameModeExitComplete(GameModeType)`; `void OnGameModeChangeCleanup(GameModeType from, GameModeType to, bool isGameOverTransition)`.
- The `AsyncPreStart` return value is the generic "I'm still loading, hold the transition" mechanism; systems load their own scenes inside it and return `false` until ready (e.g. `InnBhv`, `IronCrown/Assets.Code.Inn/InnBhv.cs:113-140`; `RunBhv`, `IronCrown/Assets.Code.Run/RunBhv.cs:119-133`).

Event bus: `EventManager.AddListener<T>(Action<T> callback, bool oneShot = false, int priority = 0)`, `RemoveListener<T>`, `TriggerEvent<T>(T evt)`; higher priority runs first; events derive from `EventBase` and usually expose a static `Trigger(...)` (`IronCrown/Assets.Code.Events/EventManager.cs:54-65, 109-116, 163-176`).

### Hook points for the mod (section 1)

| Target | Patch | Why |
|---|---|---|
| `RedHookSplashSceneBhv.Awake()` (`IronCrown/RedHookSplashSceneBhv.cs:104`) | postfix | Automated tests: boot only continues after the intro video (`OnVideoEnd`, line 122). Zero the private fields `m_postMovieWaitTime`, `m_cardDisplayTime`, `m_fadeTime` and invoke the private `OnVideoEnd(VideoPlayer)` early to skip ~all of the splash. |
| `GameInstaller.OnLoadFinished(bool success)` (`IronCrown/GameInstaller.cs:196-212`) | postfix (if `success`) | Earliest point where every core singleton exists. Create the mod's own systems here: `new GameObject("EstateBhv")`, parent it to `Globals.MainInstaller.SystemsRoot`, add a plain `MonoBehaviour` implementing the `IGameMode*` interfaces. It then receives all game-mode callbacks with no further registration (`GameModeMgr.cs:130-137`). Do **not** derive from `SingletonMonoBehaviourBase` (those are skipped at line 133 and expected to be in `Systems`). |
| `GameInstaller.SetupSystemResolvers()` (`IronCrown/GameInstaller.cs:118`) | postfix | Alternative: register a mod system in the protected `m_systems` (`SystemsContainer.RegisterResolver/Register`, `IronCrown/SystemsContainer.cs:26-47`) so its `[SystemStart]` methods are sequenced with the game's. Only needed if the mod wants LoadSequencer ordering. |
| `PostMainMenuSystemsInstaller.OnLoadFinished(bool)` (`IronCrown/PostMainMenuSystemsInstaller.cs:244-264`) | prefix/postfix | Runs on the main thread after all data libraries finished `Initialize()`; prefix runs before the `PostInit()` cross-reference passes (lines 252-260). Place to add/patch library rows (section 5). |
| `GameModeMgr.SetMode(...)` (`GameModeMgr.cs:256`) | prefix | Trace or redirect transitions (the vanilla `GameModeOverrideBhv` hook already lives here, lines 258-267). |
| *(no patch)* `Singleton<GameModeMgr>.Instance.OnNextGameModeEnterComplete(cb)` etc. | call | One-shot continuation after a transition; used by vanilla continue flow (`MainMenuControllerBhv.cs:330-331`). |
| `GameModeType` private ctor | reflection call | Define new states (e.g. `ESTATE`, `DUNGEON`). Created after `SIZE`, so `DetermineCurrentModeFromLoadedScenes` (iterates `i < SIZE.EnumValue`, line 326) ignores them, which is harmless. Use a free flag bit (≥ `0x2000`); masks in `SetGameObjectActiveByModeBhv`, `OverlayCamera`, `SetCameraLayerByGameModeBhv`, `PhysicsByModeBhv` compare `m_flag` (`IronCrown/Assets.Code.Game/SetGameObjectActiveByModeBhv.cs:46` etc.) and will treat the new mode as "inactive". Empty `sceneName` + `isSceneLoadedAutomatically:false` means the manager loads nothing and the mod's `GameModeEnterAsyncPreStart` does the loading. There are 738 references to the existing `GameModeType` constants in 201 files; new modes simply match none of them. |

---

## 2. Game modes: Confessions vs Kingdoms, and what a third mode touches

### 2.1 Representation

`Assets.Code.Game.GameType : CustomEnum<GameType>` with exactly two instances (`IronCrown/Assets.Code.Game/GameType.cs:9-16`):

```
EXPEDITION = new GameType("expedition", GameModeType.DRIVING, isMissingHeroesAddedAtInn:true, isStoreInventoryRolledOnEnterInn:true,
    isProfileSeedUsedForQuirkGeneration:true, isBiomeCompleteLogEntryValid:true, isActorDataPathRunGoalsValid:true,
    isStageCoachItemSlotEquipLimitValid:false, isSameRouteAsPreviousValid:false, null, { IDLE, HIRE })
KINGDOM    = new GameType("kingdom",    GameModeType.INN,     false,false,false,false,false, true, true, RosterStatusType.KINGDOM, { IDLE })
```
Fields: `m_LoadDefaultGameModeType`, `m_IsMissingHeroesAddedAtInn`, `m_IsStoreInventoryRolledOnEnterInn`, `m_IsProfileSeedUsedForQuirkGeneration`, `m_IsBiomeCompleteLogEntryValid`, `m_IsActorDataPathRunGoalsValid`, `m_IsStageCoachItemSlotEquipLimitValid`, `m_IsSameRouteAsPreviousValid`, `m_PartyRemovingAtInnRosterStatusType`, `m_ValidStartingRosterStatusTypes` (lines 18-36). Private ctor at line 38.

`CustomEnum<T>` is a self-registering class enum: instances append to a static list, `EnumValue` = creation index, name lookup is case-insensitive, `COUNT`, `Cast(int|string)`, `GetInstances()` (`IronCrown/Assets.Code.Utils/CustomEnum.cs:13-44, 85-105`). It serializes to JSON by name (`JsonSerializationUtils.cs:522-546`).

Current mode holder: `Assets.Code.Game.GameTypeMgr : Singleton<GameTypeMgr>`; field `m_CurrentGameType` (default `EXPEDITION`), `public void SetGameType(GameType newGameType)` raising `EventGameTypeChanged` (`IronCrown/Assets.Code.Game/GameTypeMgr.cs:37, 384-392`). It is reset to `EXPEDITION` every time `MAIN_MENU` is entered; the mode just left is kept in `MainMenuControllerBhv.ReturningFromGameType` (`IronCrown/Assets.Code.UI.Managers/MainMenuControllerBhv.cs:115, 240-245`).

Each mode has an **owner** MonoBehaviour that creates/destroys the per-playthrough managers and publishes them to `GameTypeMgr`:
- Confessions: `CampaignBhv` (roster + quests, survives across runs) and `RunBhv` (everything else).
- Kingdoms: `KingdomBhv` (all of it, plus Kingdom-only managers). It was bolted on as a third owner next to the two above and is the template.

### 2.2 Main-menu selection UI

One class, `Assets.Code.UI.Screens.MainMenuUiScreenBhv`, in scene `main_menu`. Buttons are serialized references; the button→handler wiring is in the scene (UnityEvents) **[NV]**, the handlers are:
- Top level: `m_confessionsButton` → `OnConfessionButtonPressed()` (reveals Continue/New, hides the Kingdoms button's parent and the mods toggle) and `OnConfessionBackPressed()` (`MainMenuUiScreenBhv.cs:1094-1119`); `m_kingdomsMainMenuButton` → `OnKingdomButtonClicked()` (1068-1085); `m_modsToggle` → `MainMenuControllerBhv.ToggleModsEnabled()` → `OnModSectionButtonToggle(bool isModSection)` (1024-1051; `MainMenuControllerBhv.cs:172-191`).
- Confessions sub-menu: `OnContinueGameClick()` (562-568), `OnNewGameClick()` (580-613); modded variants `OnModContinueGameClick()` / `OnModNewGame()` (1053-1066).
- Kingdoms lives in a **second scene**, `main_menu_kingdom` (`MainMenuControllerBhv.MAIN_MENU_KINGDOM_SCENE`, `MainMenuControllerBhv.cs:32`), assumed already loaded when the button is clicked (`MainMenuUiScreenBhv.cs:1076-1078`). **[NV]** it is loaded by an `AdditivelyLoadScene` component in `main_menu` (class at `IronCrown/Assets.Code.Loading/AdditivelyLoadScene.cs`). Its root UI is `Assets.Code.Kingdom.UI.KingdomMainMenuUIBhv`: `Show()` calls `SetGameType(GameType.KINGDOM)` (`KingdomMainMenuUIBhv.cs:206-216`); `OnNewKingdomClicked()` (126-148), `OnContiueKingdomClicked()` (150-160), `ExitButtonClicked()` → back to `main_menu` and `MainMenuUiScreenBhv.OnReturnFromKingdomMainMenu()` (162-182; `MainMenuUiScreenBhv.cs:1087-1092`).

### 2.3 Click → first playable scene

**Confessions, New** (`MainMenuUiScreenBhv.OnNewGameClick`, 580-613)
1. `SetGameType(EXPEDITION)`. If a save exists → abandon confirmation → `MainMenuControllerBhv.ClearSavedGameAndReloadDirectly()` sets `RunStartType.MAIN_MENU_ABANDON` or `MAIN_MENU_CASH_OUT` and `SetMode(DRIVING)` (`MainMenuControllerBhv.cs:586-615`).
2. Otherwise first-time profiles play the `BossSelect` video through `VideoPlayerControllerBhv` (CINEMATIC mode, then DRIVING); everyone else `SetMode(GameModeType.DRIVING, isLoad:false)` (600-612).
3. Leaving `MAIN_MENU`: `PostMainMenuSystemsInstaller.OnGameModeExitComplete` → `TryInstall()` → gathers CSVs and installs all data libraries (`PostMainMenuSystemsInstaller.cs:276-312`); the transition waits for it (`GameModeMgr.cs:400-403`).
4. Entering `DRIVING` (a "game type start point"): `RunBhv.GameModeEnterAsyncPreStart` loads `MainScene` additively and waits for `MapMgrBhv` (`RunBhv.cs:107-134`); `CampaignBhv.GameModeEnterAsyncPreStart` → `StartCampaign(nextRunStartType)` → `EventCampaignStarted` (`CampaignBhv.cs:56-79, 217-241`); then `RunBhv.StartRun(nextGameMode)` → `CreateManagers()` → `EventRunPreLoad` → `Init(load?)` → `EventRunStarted` (`RunBhv.cs:135-148, 238-271`); `GameTypeMgr.HandleEventRunStartedLogic` → `OnGameTypeStarted` → `EventGameTypeStarted` (`GameTypeMgr.cs:217-220, 347-367`).
5. `MapMgrBhv.GameModeEnterAsyncPreStart`: no map → `BeginNewCampaign()` → start node `ALTAR_OF_HOPE` if the profile has candles else `BOSS_SELECT` → `CreateNewRun()` (`SaveUtils.OnStartNewGame()`, seed, biome `VALLEY` or `VALLEY_INTRO`) (`IronCrown/Assets.Code.Map/MapMgrBhv.cs:482-550, 622-673, 987-997`).
6. First playable scene: **`MainScene` (DRIVING)** in the Valley. Altar of Hope / hero select are entered from map nodes through `TriggerGameModeBhv.Execute()` → `SetMode(ALTAR_OF_HOPE | HERO_SELECT)` (`IronCrown/Assets.Code.Map.Triggers/TriggerGameModeBhv.cs:27-55`); `HeroSelectBhv.ConfirmRosterSelection()` → `SetMode(DRIVING)` (`IronCrown/Assets.Code.Campaign/HeroSelectBhv.cs:1927-1940`).

**Confessions, Continue** (`ContinueGame(GameType.EXPEDITION)`, 546-560 → `MainMenuControllerBhv.OnContinueGameType`, 293-315): `SetGameType` → `SaveLoadMgr.VerifySaveFiles()` → DLC check → `BeginGameContinuation()` sets `RunStartType.MAIN_MENU_CONTINUE` and calls `SaveLoadMgr.SetGameModeFromSave()` → `SetMode(modeFromSave, isLoad:true, null, true)` (`MainMenuControllerBhv.cs:317-334`; `IronCrown/Assets.Code.Serialization/SaveLoadMgr.cs:301-322`). The mode is derived from which sub-folders exist in the save (`SaveUtils.GetGameModeTypeFromSave`, `SaveUtils.cs:275-303`).

**Kingdoms, New** (`KingdomMainMenuUIBhv.OnNewKingdomClicked` → `KingdomCreationFlowBhv`): steps `KingdomNameInput → SelectGang → GangDisclaimer → SelectMap → SetupDifficulty → Mod (only if SaveUtils.inMods) → CreatingKingdom` (`IronCrown/Assets.Code.Kingdom.UI/KingdomCreationFlowBhv.cs:33-43, 451-499`). `CreateKingdomRoutine()` (262-282):
```
ScreenFaderBhv.TransitionToBlack(...)
SaveLoadMgr.SetKingdomNameForNewKingdom(m_kingdomName)
KingdomBhv.SetSelectedScene(m_selectedKingdomMap.MapScene); KingdomBhv.SetSelectedGang(m_selectedGang)
postMainMenuSystemsInstaller.TryInstall(); wait IsInstalled          // installs libraries without a mode change
yield return KingdomBhv.StartKingdom(GameModeType.DRIVING, difficultyConfig, m_selectedKingdomMap.MapDefinition, isLoad:false, loadModeByCell:true)
```
`StartKingdom` (`IronCrown/Assets.Code.Kingdom/KingdomBhv.cs:423-468`) → `CreateManagers()` → `InitManagers()` → `SaveUtils.OnStartNewGame()` → `EventKingdomStarted.Trigger(...)` → no current map cell → `AdvanceToValley()` (482-492) → `KingdomMapManager.ScheduleTravelToCell(..., skipEmbark:true)` → `SetMode(GameModeType.DRIVING)` (`IronCrown/Assets.Code.Kingdom/KingdomMapManager.cs:1432-1436`). First playable scene: **`MainScene` (DRIVING)** through the Kingdom valley; the hub afterwards is `INN` (scene `inn` or `camp` by cell type, `InnBhv.cs:113-130`).

**Kingdoms, Continue** (`KingdomSaveSelectUIBhv.LoadSave`, `IronCrown/Assets.Code.Kingdom/KingdomSaveSelectUIBhv.cs:109-132`): `profile.SetCurrentKingdomSave(dir)` → `MainMenuControllerBhv.OnContinueGameType(GameType.KINGDOM, ...)` → same `SetGameModeFromSave()` path; `KingdomBhv.GameModeEnterAsyncPreStart` sees a start-point mode with the kingdom uninitialized and runs `StartKingdom(nextGameMode, null, null, isLoad, ...)` (`KingdomBhv.cs:289-318`), which loads via `LoadSaveIfPossible` (494-599).

The handshake that makes this work: while no game type is started, `GameTypeMgr.GameModeEnterAsyncPreStart` returns `!nextGameMode.m_isGameTypeStartPoint` (`GameTypeMgr.cs:289-308`), i.e. it **blocks entry into any start-point mode until some owner has published the managers** (`EventRunStarted` or `EventKingdomStarted`). Owners start themselves from their own `GameModeEnterAsyncPreStart` when `CurrentGameType` is theirs, and end themselves when `nextGameMode.m_isGameTypeEndPoint` (only `MAIN_MENU`).

### 2.4 Everything a third mode must register or special-case

**A. Type registries and tables**

| Item | Where | Note for "estate" |
|---|---|---|
| `GameType` instances | `GameType.cs:10-16` | New instance needs reflection on the private ctor (line 38). |
| `StreamingAssets/StaticInitializerTypesAndMembers.txt` | `StaticInitializerAttribute.cs:14-20, 70-95` | Not needed for runtime-created instances. |
| `SaveLoadMgr.m_ValidationResultGameTypeLists = new List<ValidationResult>[CustomEnum<GameType>.COUNT]` | `IronCrown/Assets.Code.Serialization/SaveLoadMgr.cs:66`, indexed at 327-347 and `IronCrown/Assets.Code.Profile/ProfileInstance.cs:257` | **Field initializer**: the third `GameType` must exist before the `GameInstaller` prefab is instantiated (splash, step 5) or the array must be resized by reflection. |
| `Excel/<gametype name>/` override folder | `ResourceGroupCsvDatabase.GatherGameTypeResources()` `IronCrown/Assets.Code.Resource/ResourceGroupCsvDatabase.cs:86-97` → `FileUtils.GetValidFilePaths` → `Directory.GetFiles` (`IronCrown/Assets.Code.Utils/FileUtils.cs:65-67`) | **Hard requirement**: only the Excel root is existence-checked; a missing `Excel/estate` throws `DirectoryNotFoundException` inside `PostMainMenuSystemsInstaller.TryInstall()` (`PostMainMenuSystemsInstaller.cs:305-306`). Existing folders: `Excel/expedition` (5 files), `Excel/kingdom` (10 files). |
| `Localization/Sources/game_type_override_<name>/` (and `Poedit/...`) | `Localization.HandleEventGameTypeStarted` (`IronCrown/Assets.Code.Locale/Localization.cs:65-69`), `LocalizationUtils.MakeGameTypeFolderPath` (`LocalizationUtils.cs:149-157`) | **Hard requirement** in English: a missing directory makes `EnglishLocalizationData.LoadStringsAtPath` return false and then **every** `GetLocalizedStrings` returns null (`IronCrown/Assets.Code.Locale/EnglishLocalizationData.cs:50-54, 86-107`). Foreign-language path **[NV]**. |
| Save folder suffix | `ProfileBhv.GetProfileRunSavesDirectoryNameByModClassification`: `EXPEDITION` → `_runs`, **anything else → `_kingdoms`** (`IronCrown/Assets.Code.Profile/ProfileBhv.cs:332-340`); consts 64-66 | A third type silently shares the Kingdom folder. |
| Save scheme | `enum RunSaveFileScheme { JOBJECT, EXPEDITION, KINGDOM, BOTH }` (`IronCrown/Assets.Code.Utils.Serialization/RunSaveFileScheme.cs`); `SaveLoadMgr.VerifyRunDirectory`: `!= KINGDOM ? EXPEDITION : KINGDOM` (`SaveLoadMgr.cs:158`) | A third type is validated as an Expedition save. |
| Save files | `RunSaveFile` instances + `m_ContainingFiles` map (`IronCrown/Assets.Code.Utils.Serialization/RunSaveFile.cs:85-153, 198-223`); `RunSaveFolder` (`RunSaveFolder.cs:8-22`) | Kingdoms added `KINGDOM`, `KINGDOM_MAP`, `KINGDOM_MANAGER`, `KINGDOM_DIFFICULTY`, `KINGDOM_DAY`, `SIEGE_MANAGER`, `EVENT_MANAGER`, `HERO_SELECT`. |
| Save writer/loader dispatch | `SaveUtils.RunSyncOperations`: `EXPEDITION` → `CampaignBhv.Save` + `RunBhv.Save`, **else → `KingdomBhv.Save`** (`SaveUtils.cs:695-703`) | NRE for a third type unless patched. |
| Other save branches | `SaveUtils.cs:375` (console), `947` (`EXPEDITION ? 1 : 21` slots kept), `1676-1680`, `1825-1835` (run/kingdom GUID), `1856-1863`, `2120`, `2163`, `2321-2348`; `SaveLoadMgr.cs:104, 317, 352, 433-450`; `ProfileBhv.cs:282, 291, 466-475, 561-562, 580-594`; `ProfileInstance.cs:287, 1447-1463, 1584` | |
| Official mod filter | `enum ModGameType { EXPEDITION_AND_KINGDOMS, EXPEDITION, KINGDOMS }` (`IronCrown/Assets.Code.Mod/ModGameType.cs`); `ModScreenWidgetBhv.FilterModsByCurrentGameMode` (`IronCrown/Assets.Code.UI.Widgets/ModScreenWidgetBhv.cs:109-122`), mod list file name (158-168) | A third type lists no official mods. |
| Data conditions | `ConditionType.GAME_TYPE` compares `CurrentGameType.GetName()` to the CSV string; validated with `CustomEnum<GameType>.Cast` (`IronCrown/Assets.Code.Condition/ConditionCalculation.cs:593-596`, `ConditionDefinition.cs:105, 280`) | Works for new names for free. |
| Rules | `RulesType`/`RulesManager` (`IronCrown/Assets.Code.Rules/RulesManager.cs:29-55`); Kingdoms added `KingdomRules` | |
| Rich presence | `RichPresenceType` instances carry a `GameType` (`IronCrown/Assets.Code.Platform/RichPresenceType.cs:14-52`); `RichPresenceMgr.cs:87, 266, 369-390` | |
| Tutorials | `TutorialType` valid-game-type arrays (`IronCrown/Assets.Code.Tutorial/TutorialType.cs:22-28`) | |
| LUT assets | `LUTAsset.m_GameTypes` (`IronCrown/Assets.Code.Rendering/LUTAsset.cs:27`), `LUTManager.cs:319-342, 417, 584` | |
| Audio banks | `AudioBankMgr.GameModeEnterAsyncPreStart` (`IronCrown/Assets.Code.Audio.Banks/AudioBankMgr.cs:278-300`), logs "Unhandled GameType" and continues | |
| Other enums Kingdoms extended | `RosterStatusType.KINGDOM` (`RosterStatusType.cs:25`), `GameOverReason.KINGDOM_*` (`GameOverReason.cs:20-24`), `RandomIdentifier.KINGDOM/SIEGE` (`RandomIdentifier.cs:118-120`), editor-pref group `KINGDOM_GROUP` (`TextBasedEditorPrefsBaseType.cs:93, 99-151`) | |
| Debug boot detection | `GameTypeMgr.SetGameTypeFromDebug()` (`GameTypeMgr.cs:394-421`), `MainMenuSystemsInstaller.cs:35`, `KingdomBhv.cs:163` | Editor-only paths. |

**B. Owner / lifecycle registration**

- `GameInstaller.SetupSystemResolvers`: `SetupMonoBehaviourSystemResolver<KingdomBhv>()` (`GameInstaller.cs:181`), `ResourceDatabaseKingdomMap`, `ResourceDatabaseGang` (131-132), `LibraryKingdomDifficulty` (192).
- `PostMainMenuSystemsInstaller.SetupSystemResolvers`: `LibraryKingdomEvent`, `LibraryKingdomSiegeAttack`, `LibraryKingdomSiegeDefense`, `LibraryKingdomTreasure`, `LibraryInnUpgrade` (145-149) and `LibraryKingdomEvent...PostInit()` (258).
- `GameTypeMgr`: listeners for `EventKingdomStarted` (set, priority 1000; logic, priority -1000) and `EventKingdomEnded` (`GameTypeMgr.cs:166-168, 240-282`). All 15 manager references (`RosterManager`, `QuestManager`, `RunDataManager`, `RunLogManager`, `PlayerInventory`, `TorchManager`, `AffinityManager`, `StressManager`, `BarkManager`, `ActOutManager`, `StageCoach`, `LootManager`, `RunValues`, `BiomeManager`, `CombatManager`) are dereferenced without null checks on every mode change while a game type is started (289-345), so an owner must supply all of them.
- Other listeners of `EventKingdomStarted`: `ActorInstance` (calls `PostLoadFromJson` on memory/combat/trinket inventories when `m_IsLoad`, `IronCrown/Assets.Code.Actor/ActorInstance.cs:1041, 2125-2133`), `AnalyticsBhv.cs:397`, `QuestManager.cs:98`, and the Kingdom managers themselves.
- `KingdomBhv` shows the owner contract: `CreateManagers()` (647-670), `InitManagersPreLoad()` (718-742), `InitManagersPostLoad()` (744-767), `LoadSaveIfPossible()` (494-599), `Save(string saveDirPath, GameModeType, SaveGameContext)` (601-635), `DestroyManagers()` (672-716), `EndKingdom()` (470-480).

**C. Menu / UI**

- `MainMenuUiScreenBhv`: fields `m_kingdomsMainMenuButton`, `m_kingdomsNewGameStartEventRef`, `m_modKingdomsButton` (167, 180, 183); handlers at 1068-1161.
- `MainMenuControllerBhv`: `MAIN_MENU_KINGDOM_SCENE`, `ReturningFromGameType`, `BeginGameContinuation` audio branch (`!= EXPEDITION` → Kingdom, 320-328), `ShowMissingDLCForProfileDialog` (522-570).
- New scene `main_menu_kingdom` with `KingdomMainMenuUIBhv`, `KingdomSaveSelectUIBhv`, `KingdomCreationFlowBhv` and its step classes (`IronCrown/Assets.Code.Kingdom.UI/`).
- `PauseMenuUiControllerBhv.ReturnToMainMenu` (non-Expedition exits unconditionally, `IronCrown/Assets.Code.UI.Controllers/PauseMenuUiControllerBhv.cs:504-521`), `SelectAbandonRunSelectable` (429-436).

**D. Call sites branching on the mode** — 280 references to `GameType.KINGDOM` / `GameType.EXPEDITION` in 77 files (full list in Appendix A), 608 direct uses of `SingletonMonoBehaviour<KingdomBhv>.Instance.` / `SingletonMonoBehaviour<RunBhv>.Instance.` in 121 files. What a *third instance* does at those sites without patches:
- `== KINGDOM` / `== EXPEDITION` tests are false: mode-specific UI and logic are skipped. Usually harmless.
- **Explicit "unhandled" guards** left by the developers, each a must-handle: `AudioBankMgr.cs:299`; `InnSystem.cs:139` (RollInnDefinitions); `InnBhv.cs:383` (EndInn); `ActorInstance.cs:2402` (`SetInitialActorPath`) and `ActorInstance.cs:2721` (`SetInitialSkills`) — new heroes get no path/skills; `RosterManager.cs:1199` (`GetReplacementActorGuids`); `ProfileInstance.cs:1460` (`GetCanAbandon`); `PlayerItemInventory.cs:268` (`GetIsSellingActive`); `RestItemSlotBhv.cs:359`.
- **Negated tests** put the third type in the other mode's branch: `ActorInstance.cs:2400`; `AudioConditionUtils.cs:183`; `InnSystem.cs:174`; `InnPresentationBhv.cs:1185, 1217, 1223, 1230, 1567`; `RichPresenceMgr.cs:266`; `LUTManager.cs:584`; `RosterManager.cs:624`; `SaveLoadMgr.cs:158, 352`; `InventoryUiBhv.cs:128, 279`; `CharacterSheetUiBhv.cs:321`; `ModScreenWidgetBhv.cs:139`; `HeroSelectBhv.cs:506, 1662, 1687`; `GameModeMgr.cs:593`; `SaveUtils.cs:2327`.
- **`else` = Kingdom**: `SaveUtils.cs:695-703`, `SaveUtils.cs:1825-1835`, `ProfileBhv.cs:335-339`, `InnBhv.cs:115-130` (dereferences `KingdomBhv.KingdomMapManager.GetCurrentMapCell()`), `MainMenuControllerBhv.cs:320-328`.
- Unguarded owner dereference example: `AchievementsMgr.ProcessUseItemsComplete` uses `KingdomBhv.Instance.KingdomManager.UseItemsStates` (`IronCrown/Assets.Code.Achievements/AchievementsMgr.cs:1940`).

### 2.5 Two ways to add Estate

**Option K — host Estate inside `GameType.KINGDOM`.** `CurrentGameType` stays `KINGDOM`, `KingdomBhv` remains the owner, a mod flag marks the playthrough as Estate. All 280 branches, the 9 "unhandled" guards, hero initialisation, the `Excel/kingdom` and `game_type_override_kingdom` data, and the whole save pipeline keep working unmodified. Cost: Kingdom gameplay (sieges, events, gang, day system, game over) must be neutralised or reused; note `KingdomResultsScreenBhv.Exit_Routine` **deletes the Kingdom save** after any game over (`IronCrown/Assets.Code.Kingdom.UI/KingdomResultsScreenBhv.cs:126-132`), and Estate slots appear in the Kingdoms save list of the profile.

**Option T — a true third `GameType` with its own owner.** Clean identity (analytics, achievements, rich-presence and Kingdom UI branches all skip), but requires: creating the instance before `SaveLoadMgr` exists; the two folder requirements; an `EstateBhv` duplicating `KingdomBhv`'s manager set-up; patches for the "unhandled"/negated/else sites above; its own save routing.

Recommendation: start with **Option K** for the first vertical slice (it is the lowest-risk way to get DD2 heroes, roster, combat and saving running in a persistent campaign) and keep Option T as a later refactor; section 2.4 is the checklist for T. Whether `KingdomBhv` can run with a stub map/gang is outside this document (open unknown).

### Hook points for the mod (section 2)

| Target | Patch | Why |
|---|---|---|
| `MainMenuUiScreenBhv.Awake()` (`MainMenuUiScreenBhv.cs:849-860`) | postfix | Add the Estate entry: clone `m_kingdomsMainMenuButton.gameObject` under the same parent (the parent is what `OnConfessionButtonPressed`/`OnConfessionBackPressed` hide and show, lines 1097, 1115, so a sibling follows automatically), clear its persistent `onClick`, add the mod handler, append the `Button` to the private `m_mainMenuSelectables` list (line 84) so `SetMainMenuSelectablesEnabled` (982-1006) covers it. |
| `MainMenuUiScreenBhv.OnKingdomButtonClicked()` (1068) | reference | Template for a mode sub-menu: fade to black, swap active scene/panel, `Show()`. The Estate sub-menu can be a mod-built canvas instead of a scene. |
| `KingdomCreationFlowBhv.CreateKingdomRoutine()` (`KingdomCreationFlowBhv.cs:262-282`) | reference / call | Template for "New": `PostMainMenuSystemsInstaller.TryInstall()` (public), wait `IsInstalled`, start the owner, then `SetMode`. Option K calls `KingdomBhv.SetSelectedScene`, `SetSelectedGang`, `StartKingdom(...)` (all public) directly. |
| `MainMenuControllerBhv.OnContinueGameType(GameType, out SaveValidationAction, out SaveFailureReason)` (`MainMenuControllerBhv.cs:293`) | call | "Continue" for Option K after `GetCurrentProfile().SetCurrentKingdomSave(dir)` (`ProfileInstance.cs:1606-1609`). |
| `MainMenuControllerBhv.OnGameModeEnterStart(GameModeType, bool)` (240-274) | postfix | On return to the menu the game type is forced back to `EXPEDITION` (245); use `ReturningFromGameType` plus the mod flag to reopen the Estate sub-menu. |
| `GameType` private ctor (`GameType.cs:38`) | reflection (Option T) | Read `GameType.KINGDOM` first to force the static initializer so indices stay `EXPEDITION=0, KINGDOM=1`, then construct `"estate"`. Must happen before step 5 of boot (`SaveLoadMgr.cs:66`). |
| `ResourceGroupCsvDatabase.GatherGameTypeResources()` (`ResourceGroupCsvDatabase.cs:86`) | prefix (Option T) | Avoid the missing-folder exception; gather from the mod's folder instead (private `GatherResourcesFromFolder(string folder, int dlcNumber, bool overrideCSV, List<string> exclusionList = null)`, line 216). |
| `LocalizationUtils.MakeGameTypeFolderPath(GameType, bool)` (`LocalizationUtils.cs:149`) | postfix (Option T) | Return an existing mod folder for the estate type; otherwise all English strings fail. |
| `ProfileBhv.GetProfileRunSavesDirectoryNameByModClassification(uint, GameType, bool)` (`ProfileBhv.cs:332`) | postfix (Option T) | Give the third type its own suffix; then also protect it in `DeleteUnusedSaves` (section 4). |
| `SaveLoadMgr.VerifyRunDirectory(bool)` (`SaveLoadMgr.cs:156`), `KingdomBhv.Save(...)` (`KingdomBhv.cs:601`) | prefix (Option T) | Route validation and writing to the mod's owner. |
| `ActorInstance.SetInitialActorPath()` (public, `ActorInstance.cs:2390`), `ActorInstance.SetInitialSkills(ResourceActor actorResource)` (private, 2696), `RosterManager.GetReplacementActorGuids()` (`RosterManager.cs:1180`), `PlayerItemInventory.GetIsSellingActive()` (`PlayerItemInventory.cs:244`), `ProfileInstance.GetCanAbandon(GameType)` (`ProfileInstance.cs:1447`) | prefix/postfix (Option T) | The explicit "Unhandled GameType" sites; without them new heroes get no path and no skills in a third game type. |
| `KingdomManager.CheckGameOverDays/CheckGameOverInns/CheckGameOverRoster()` (`IronCrown/Assets.Code.Kingdom/KingdomManager.cs:342-373`), `KingdomResultsScreenBhv.Exit_Routine` | prefix (Option K) | Keep a persistent Estate from ending and from having its save deleted. |

---

## 3. Run lifecycle

### 3.1 What holds the current run

There is no single run object. `Singleton<GameTypeMgr>.Instance` is the façade every system uses (`GameTypeMgr.cs:75-107`): `RosterManager` (party + roster; `GetPartyActors()`, `GetActorGuids(RosterStatusType)`), `QuestManager`, `RunDataManager`, `RunLogManager`, `PlayerInventory` (`PlayerItemInventory`), `TorchManager`, `AffinityManager`, `StressManager`, `BarkManager`, `ActOutManager`, `StageCoach`, `LootManager`, `RunValues`, `BiomeManager`, `CombatManager`, `CombatScenarioData`, static `ActiveBiome`. Heroes are `ActorInstance` objects in `SingletonMonoBehaviour<Library<uint, ActorInstance>>` (`LibraryActors`, a GameInstaller system; `IronCrown/Assets.Code.Actor/LibraryActors.cs:13`).

Owner-only state:
- `RunBhv`: `RunManager` (boss, inn instance/history, node/inn counters; `IronCrown/Assets.Code.Run/RunManager.cs:53-76`), `RunScoreManager`, `DoomManager`, `RunOptions` (`RunBhv.cs:79-89`).
- `CampaignBhv`: `m_RosterManager`, `m_QuestManager`, `LastRunScore` (`CampaignBhv.cs:27-37`).
- `KingdomBhv`: `KingdomManager`, `KingdomMapManager`, `KingdomDifficulty`, `KingdomEventManager`, `KingdomSiegeManager`, `WoundManager`, `KingdomDaySystem` (`KingdomBhv.cs:73-157`).
- Map: `SingletonMonoBehaviour<MapMgrBhv>` (scene `MainScene`), including the map seed `m_seed`.

RNG: static `Assets.Code.Math.RandomContainer`, one `UnityEngine.Random.State` per `RandomIdentifier` name (`LOOT`, `COMBAT`, `MAP_GEN`, `AI`, `ROSTER`, ... ; `isSaved` flag) (`IronCrown/Assets.Code.Game/RandomIdentifier.cs:10-122`; `IronCrown/Assets.Code.Math/RandomContainer.cs:145-248`: `SaveToJson()`, `LoadFromJson(JObject, bool)`, `SetSeed`, `GetSeed`, `Range`, `UpTo`, `CheckValue`). A new run seeds `MAP_GEN` then derives every other saved stream from it (`MapMgrBhv.SetGenerationSeed`, `MapMgrBhv.cs:576-606`). All non-enum streams are dropped at `GameTypeMgr.OnGameTypeEnded` (`GameTypeMgr.cs:372`).

### 3.2 Create / end / abandon (Confessions)

- `RunStartType` (name, `LoadType { NONE, CAMPAIGN_ONLY, CAMPAIGN_AND_RUN }`, `m_IsGameOver`, `m_IsResetMap`): `MAIN_MENU_NEW`, `MAIN_MENU_NEW_RUN`, `MAIN_MENU_CONTINUE`, `MAIN_MENU_ABANDON`, `MAIN_MENU_CASH_OUT`, `IN_CAMPAIGN_GAME_OVER` (`IronCrown/Assets.Code.Run/RunStartType.cs:8-26`). Set with `RunBhv.SetNextRunStartType(RunStartType)` before the mode change.
- Start: `RunBhv.StartRun(GameModeType startGameModeType, bool isDebugLoaderRestart = false)` (`RunBhv.cs:238-271`).
- Game over: `RunScoreManager.SetGameOver(GameOverReason)` raises `EventRunGameOver` (profile rewards are applied by `ProfileBhv.HandleEventRunGameOver` → `ProfileInstance.ApplyRunEnd`) (`IronCrown/Assets.Code.Run/RunScoreManager.cs:73-128`; `ProfileBhv.cs:237-252`). Reasons: `PARTY_DEAD`, `VICTORY`, `CASH_OUT`, `ABANDON` (with `PartyKeepType`, `GameOverReason.cs:10-18`). Triggers: battle result (`RunScoreManager.cs:185-208`), pause-menu abandon (`PauseMenuUiControllerBhv.cs:438-455`), inn "end run" (`IronCrown/Assets.Code.UI.Widgets/SubScreenInnEndRunBhv.cs:131-132`).
- After the results UI: `RunScoreManager.TriggerGameOver(bool skipTransition = false, bool abandoned = false)` → `SetMode(DRIVING, isLoad:false, GAME_OVER transition, true, ..., isGameOverTransition:true)` (`RunScoreManager.cs:145-154`; callers `CombatPresentationBhv.cs:1012-1075`).
- On re-entering `DRIVING`, `RunBhv.GameModeEnterAsyncPreStart` sees `m_RunScoreManager.IsGameOver()` → `ResetRun(RunStartType.IN_CAMPAIGN_GAME_OVER)` = `EndRun(RunEndType.RESET)` + `StartRun(DRIVING)` (`RunBhv.cs:135-148, 273-296`). `EventRunEnded` → `CampaignBhv.OnRunEnd` keeps/removes heroes and resets roster entries to `IDLE` (`CampaignBhv.cs:258-297`); `GameTypeMgr.HandleEventRunEnded` nulls the managers and calls `OnGameTypeEnded` (222-238). `MapMgrBhv` gets `m_ResetMap` from the start type (`MapMgrBhv.cs:409`) and rebuilds the map (`ClearAndRestartRun`, 632-643, 746-752).
- **"Back to the Altar of Hope" is therefore not a menu**: it is the first node (`NodeType.ALTAR_OF_HOPE`) of a freshly generated Valley in the same `MainScene`, entered through `TriggerGameModeBhv`; `AltarOfHopeBhv.EndAltarOfHope()` → `SetMode(DRIVING)` (`IronCrown/Assets.Code.AltarOfHope/AltarOfHopeBhv.cs:103-108`).
- Back to menu: `PauseMenuUiControllerBhv.ReturnToMainMenu()` → `ExitToMainMenu()` → `SetMode(MAIN_MENU)` + `MapMgrBhv.ClearGame()` on exit complete (`PauseMenuUiControllerBhv.cs:206-212, 504-529`). Entering `MAIN_MENU` (end point): `RunBhv.EndRun(RunEndType.GAME_MODE_END_POINT)` (`RunBhv.cs:150-153`), `CampaignBhv.EndCampaign()` (`CampaignBhv.cs:67-76`), `PostMainMenuSystemsInstaller` clears all libraries and CSV caches (`PostMainMenuSystemsInstaller.cs:285-299, 319-325`), `ModMgr.UnloadMods()` (`ModMgr.cs:379-390`).

### 3.3 Kingdoms differences

- No `RunScoreManager`. `KingdomManager.SetGameOver(GameOverReason)` (`KINGDOM_DAYS`, `KINGDOM_INNS_DESTROYED`, `KINGDOM_EMPTY_ROSTER`, `VICTORY`) → `EventKingdomGameOver` (`KingdomManager.cs:224-259, 342-387`) → `KingdomDayState.END` → `SetMode(RESULTS)` (`IronCrown/Assets.Code.Kingdom.Presentation/KingdomPresentationBhv.cs:366-368`) → `KingdomResultsScreenBhv` → `SetMode(MAIN_MENU)` and `PlatformMgr.DeleteKingdomSave(currentKingdomSave)`.
- Party wipe is not a game over: `KingdomBhv.HandleEventBattleResult` → `CheckGameOverRoster()`; if not over → `SetMode(GameModeType.INN)` (`KingdomBhv.cs:224-252`).
- `ProfileInstance.GetCanAbandon(KINGDOM)` is false (`ProfileInstance.cs:1454-1457`).
- Loop: `INN` (day state machine `START → DAY_CHANGED → EVENT_SPAWN → CURSE_ACTIVITY → SIEGE_RESOLVE → SIEGE_SELECT → SIEGE_SPAWN → MAP_UPDATE → WAIT_ON_PLAYER → RESOLVE_TRAVEL → END`, `IronCrown/Assets.Code.Kingdom/KingdomDayState.cs:9-29`) → `EMBARK` → `DRIVING` → ... → `INN` (`InnBhv.EndInn`, `InnBhv.cs:328-389`).

### Hook points for the mod (section 3)

| Target | Patch | Why |
|---|---|---|
| *(no patch)* `EventKingdomStarted.Trigger(GameModeType, bool isLoad, bool isDebugLoaderReset, RosterManager, QuestManager, RunDataManager, RunLogManager, PlayerItemInventory, TorchManager, AffinityManager, StressManager, BarkManager, ActOutManager, StageCoach, LootManager, RunValues, BiomeManager, CombatManager)` (`IronCrown/Assets.Code.Kingdom.Events/EventKingdomStarted.cs:79-82`) and `EventKingdomEnded.Trigger()` | call (Option T) | The public way for a mod-owned manager set to become "the current run": `GameTypeMgr` copies the references and raises `EventGameTypeStarted` with the current `GameType` (`GameTypeMgr.cs:240-262`). With `isLoad:true` it also makes each `ActorInstance` re-link its inventories. |
| `GameTypeMgr.GameModeEnterAsyncPreStart(...)` (`GameTypeMgr.cs:289`) | reference | Explains the "transition never completes" failure: entering a start-point mode with no owner started blocks forever. |
| `EventBattleResult` listener (see `KingdomBhv.HandleEventBattleResult`, priority -100, `KingdomBhv.cs:183, 224`) | `EventManager.AddListener` | Party-wipe / retreat handling for dungeon runs without touching combat code. |
| `EventGameTypeStarted` / `EventGameTypeEnded` (`IronCrown/Assets.Code.Game.Events/EventGameTypeStarted.cs`) | `EventManager.AddListener` | Mode-agnostic "playthrough began/ended" signal for mod state. |
| `RunScoreManager.TriggerGameOver`, `PauseMenuUiControllerBhv.OnAbandonRun()` (438) | prefix | Both dereference `RunBhv.Instance.RunScoreManager` unconditionally; relevant only if a non-Expedition mode exposes the abandon button (hidden when `GetCanAbandon` is false). |
| `PauseMenuUiControllerBhv.ReturnToMainMenu()` (504) | reference | Works for any non-Expedition type. |
| `RandomContainer.SetSeed(IRandomIdentifierContainer, int)` (public static) | call | Deterministic dungeon generation: implement `IRandomIdentifierContainer` (`GetIdentifier()`) for mod streams; they are saved with the `random` section and cleared when the game type ends. |

---

## 4. Persistence

### 4.1 Location

- Root: `SaveUtils.SAVE_PARENT_DIR` = `RunSaveLocation.DEFAULT.Path` = `Application.persistentDataPath` (`SaveUtils.cs:152-162`; `RunSaveLocation.cs:11`). `app.info` in the game data folder reads `RedHook` / `Darkest Dungeon II`, so on Windows this is `%USERPROFILE%\AppData\LocalLow\RedHook\Darkest Dungeon II` **[NV]** (Unity convention; folder not opened).
- Per platform user: `GAME_OPTIONS_PATH = <root>/SaveFiles/<PlatformMgr.GetPlatformID()>` (`SaveUtils.cs:164`); on Steam the ID is `SteamUser.GetSteamID().ToString()` (`SteamPlatformMgr.cs:149-152`). Holds `game_options.json` and `input.json` (`PlatformMgr.cs:234-259`; `IronCrown/Assets.Code.Inputs/InputSystemBhv.cs:398-434`).
- Profiles: `PROFILES_SAVE_ROOT_PATH = <root>/SaveFiles/<platformID>/profiles` (`SaveUtils.cs:166`). Legacy `<root>/SaveFiles/profiles` is migrated on first load (`SaveUtils.cs:88, 561-618`).
- PC saving is plain `File.WriteAllBytes` through `FileUtils.SaveTextToFile` (`IronCrown/Assets.Code.Utils/FileUtils.cs:118-140`).

### 4.2 Format

Indented JSON text (Newtonsoft `JObject.ToString()`), no encryption, no checksum (`PlatformMgr.SaveRunJson`, `SaveProfileToJson`, `PlatformMgr.cs:261-264, 309-312`).

Custom serializer `Assets.Code.Utils.Serialization.JsonSerializationUtils` (`IronCrown/Assets.Code.Utils.Serialization/JsonSerializationUtils.cs`):
- `JToken ToJson<T>(T)`, `T FromJson<T>(JToken)`, `JToken PerFieldToJson<T>(T)`, `T PerFieldFromJson<T>(JToken)`, `void PerFieldApplyTo<T>(T target, JToken)`, `JObject TryParse(string, bool)` (768-819, 916-931).
- Type dispatch (25-57): primitives/`Vector*`/`Quaternion`/`DateTime`/`Guid` → C# enum by name → `CustomEnum<>` by name → `IJsonSerializable` (instance `ToJson()` + static `FromJson(JToken)`) → array → `IList` → `IDictionary` (array of `[key, value]` pairs) → "complex type" = **every instance field including private and inherited**, keyed by field name, except `[DontSerializeToJson]` (430-498, 881-903).
- Unknown JSON keys are ignored on load (463-480); missing keys leave field defaults. On save only known fields are written (446-461), so foreign keys in a file do not survive a re-save.
- Library definitions serialize as their id string and are looked up again on load (`IronCrown/Assets.Code.Library/SerializedLibraryElement.cs:29-54`).
- Run-level objects implement `IRunSaveFileTarget { JToken SaveToJson(); void LoadFromJson(JObject data); }`.

### 4.3 Profiles

- Two lists: regular and **mod** profiles. Files `profile_<guid>.json` / `mod_profile_<guid>.json` with a `.backup` twin written at load (`ProfileBhv.cs:56-62, 99-109, 379-392`). GUID is a `uint` unique across both lists; next = max + 1 (`ProfileBhv.cs:756-768`). Max 10 per list (`m_MaxNumProfiles`, line 81; UI builds exactly that many slots, `IronCrown/Assets.Code.ui/ProfileSelectBhv.cs:175-210`). The current profile is the one whose file has `"is_current": true` (`ProfileBhv.cs:353-354, 383-386`).
- Enumeration: `ProfileBhv.LoadFromJson(string loadFolder, JObject loadFileJson = null)` globs `profile_*.json` and `mod_profile_*.json` (361-441). API: `GetNumberOfProfiles()`, `GetProfileAtIndex(int)`, `GetProfileByGUID(uint)`, `GetCurrentProfile()`, `GetCurrentProfileGuid()`, `CreateProfile(string profileName)` (also makes it current), `CreateNewProfile()`, `SetCurrentProfile(uint | ProfileInstance)`, `RemoveProfile(ProfileInstance)` (619-808). All of these act on the **active list**.
- Switching lists: `ProfileBhv.OnProfileSourceChange(bool inMods)` sets `SaveUtils.inMods` and swaps `m_Profiles`/`m_ProfileGuidInfo` (178-199). The main menu's mods toggle calls it and auto-creates a mod profile if none exists (`MainMenuUiScreenBhv.cs:1024-1051`). `SaveUtils.inMods` is reset to false at every boot (`ProfileBhv.cs:124`).
- `SetCurrentProfile` raises `EventCurrentProfileChanged` and calls `PlatformMgr.SaveAllProfiles()` (788-808).
- Per-profile files in the same folder: `narration_<guid>.json`, `tutorial_<guid>.json`, `collection_<guid>.json`, `loadout_<guid>.json`, `modlist_<guid>.json` (`NarrationMgr.cs:318-320`, `TutorialMgr.cs:237-239`, `PlayerCollectionMgr.cs:136-138`, `PlayerLoadoutMgr.cs:57-59`, `ModScreenWidgetBhv.cs:175-178`).
- **`ProfileBhv.DeleteUnusedSaves(string deleteFolder)` deletes every file and directory in the profiles folder that is not on its keep lists** (567-617); it runs inside `SaveUtils.SaveAllProfiles()` (`SaveUtils.cs:459`), i.e. on every profile switch/create/remove.

`ProfileInstance` content (all fields, `ProfileInstance.cs:45-116`): `m_ProfileGuid`, `m_Name`, intro/outro flags, viewed paths/kits/palettes/skins/tokens, `m_ProfileSeedInstances`, `m_UnlockContainer`, `m_QuestTracker`, `m_LastStageCoachSkin`, boss select/victory counts and flame lists, `m_CurrentKingdomSave`, `m_ProfileValues` (candles etc.), `m_HopeCollected`, run-end streak, `m_AltarToggles`, `m_TotalDistanceDriven`, `m_ActorDeaths`, `m_VisitedInns`, `m_PartyWipedRunBosses`, `m_RunVictoryEntries`, `m_UnlockedAchievements`, `m_EnabledDLCs` (key `<gametype>_<slot dir>`), `m_KingdomSaveNameMappings` (slot dir → display name), `m_KingdomGameOverEntries`, `m_LastSelectedGang`.

### 4.4 Run saves

Directory layout (PC), built by `SaveUtils.UpdateSaveFolderAndNumber` + `GetSaveDirPath` (`SaveUtils.cs:1838-1963`):

```
profiles/<profile_|mod_profile_><guid>_runs/      (Confessions)   or   ..._kingdoms/   (Kingdoms)
  <yyMMdd_HHmmss>/                 one per run (Confessions keeps the newest + N old, zipped) or per kingdom slot (max 20)
    Save_0001_<NodeType|Inn|Combat|Embark>/      a full snapshot per save point
      [Save_0001/ ...]             nested numbered snapshots for COMBAT / ALTAR_OF_HOPE / EMBARK / HERO_SELECT
        map.json  actors.json  campaign.json  run.json        (Confessions)
        map.json  actors.json  kingdom.json                   (Kingdoms)
        combat/combat.json, combat/combat_scenario_data.json | inn/inn.json | altar_of_hope/altar_of_hope.json | embark/embark.json | hero_select/hero_select.json
```
- Every save point writes a **complete new snapshot directory**; the resume mode is inferred from which sub-folder exists (`SaveUtils.cs:275-303`). Old snapshots are trimmed asynchronously (`ShortenRunSaves`, 1069-1160: keep ~4 snapshots / 200 files and the newest inn snapshot) and old runs zipped/deleted (`DeleteOldRunSaves`, 940-1054).
- `run.json` sections (`RunBhv.Save`, `RunBhv.cs:482-510`): `run_values`, `version`, `random`, `inventory`, `stagecoach`, `act_out_manager`, `run_manager`, `run_log_manager`, `run_score_manager`, `affinity_manager`, `run_options`, `loot_manager`, `biome_manager`, `combat_manager`, `doom_manager`. `campaign.json`: `roster`, `last_run_score`, `quest_manager` (`CampaignBhv.cs:201-215`). `kingdom.json`: `Version` + `run_values`, `version`, `random`, `run_log_manager`, `inventory`, `stagecoach`, `act_out_manager`, `affinity_manager`, `loot_manager`, `kingdom_map`, `kingdom_manager`, `kingdom_difficulty`, `roster`, `quest_manager`, `biome_manager`, `combat_manager`, `siege_manager`, `event_manager`, optional `kingdom_day` (`KingdomBhv.cs:601-635`). `version` = `BuildVersion`, `RunCount`, `RunID`, `KingdomID` (`SaveUtils.cs:2077-2095`).
- Each section is wrapped with a `"Version"` int (`SaveUtils.MergeSaveDataToJObject`, 742-750) and validated against `RunSaveFile.m_Version`; mismatch or missing data resolves to a `SaveValidationAction` (`NO_SAVE, SAVE_IS_VALID, RESET_FILE, BACK_TRACK_ROW, BACK_TRACK_BIOME, BACK_TRACK_INN, INVALIDATE_RUN, INVALIDATE_CAMPAIGN, DONT_LOAD`) (`SaveUtils.cs:50-61, 859-928`; `SaveLoadMgr.cs:156-253`). Unknown extra keys in `run.json`/`kingdom.json` are never inspected.
- Per profile vs per run: profile = meta-progression and identity (4.3); run = everything listed above incl. heroes (`actors.json`), roster, RNG streams and the map.

### 4.5 When saving happens

`SaveUtils.SaveCurrentGameMode(string nodeTypeName = null, bool isSoftSave = false)` (`SaveUtils.cs:434-440`); writes happen synchronously on the main thread in `RunSyncOperations` (689-730), **which also calls `SaveCurrentProfile()` every time** (725). Events `EventSaveCurrentGameModeStarted` / `EventSaveCurrentGameModeCompleted` bracket it (364, 431). Call sites: node arrival/completion (`IronCrown/Assets.Code.Map.Generation/TileNodeBhv.cs:321, 502`), first driving frame after combat (`MapMgrBhv.cs:820`), combat turns/rounds (`IronCrown/Assets.Code.Combat/BattleStateMachine.cs:500, 550, 557`), inn states (`InnStateMachine.cs:208`, `InnSystem.cs:449`), embark start (`EmbarkBhv.cs:192`), altar purchases (`AltarOfHopeSystem.cs:172` and the `Altar*SubScreenBhv` classes), affinity (`AffinityManager.cs:281`), Kingdom day states `SIEGE_SELECT`/`WAIT_ON_PLAYER` (`KingdomDayStateMachine.cs:50-65`), `KingdomBhv.cs:365`, `TriggerSaveBhv.cs:11`, `CharacterSheetStatsUiBhv.cs:1015`.
Profile-only saves: `PlatformMgr.Instance.SaveCurrentProfile()` after boss select, kingdom end, unlocks, run-save creation (`ProfileBhv.cs:233, 263, 270, 286`).

Global kill switches: `SaveUtils.GetIsGameSaveDisabled()`, `GetIsGameLoadDisabled()`, private `GetIsProfileSaveDisabled()`, `GetIsProfileLoadDisabled()` (`SaveUtils.cs:1755-1805`), driven by editor prefs `disable_save_game`, `disable_load_game`, `disable_save_profile`, `disable_load_profile` and by "debug boot" detection (`GetIsSaveLoadDisabled`, 1674-1685).

### 4.6 Cloud sync

No code path touches Steam Cloud: there is no `SteamRemoteStorage`/`ISteamRemoteStorage` reference anywhere in the decompiled assemblies (repository-wide search; `SteamPlatformMgr.cs` uses only `SteamAPI`, `SteamInput`, `SteamApps`, `SteamUserStats`, `SteamUGC`, `SteamFriends`, `SteamTimeline`, `SteamUtils`). If saves sync, it is Steam Auto-Cloud configured on the Steamworks side over the folder in 4.1 **[NV]**; which sub-paths/patterns it covers is unknown.

### 4.7 Recommendations

**(a) Extra data alongside a profile**

| Where | Verdict | Evidence |
|---|---|---|
| New files/dirs inside `profiles/` | **No**, unless `DeleteUnusedSaves` is patched; they are deleted on the next `SaveAllProfiles()` | `ProfileBhv.cs:567-617` |
| Extra key inside `profile_<guid>.json` | Works only if re-injected on every save (unknown keys are dropped by the per-field writer) and read back by the mod itself | `JsonSerializationUtils.cs:446-480`; write goes through `PlatformMgr.SaveProfileToJson(JObject profileJson, string saveFileName)` (`PlatformMgr.cs:261`) |
| Extra key inside `kingdom.json` / `run.json` | **Best for per-playthrough data**: travels with every snapshot, is ignored by validation and by the loaders, inherits the game's backtracking, trimming and whatever cloud sync exists | writer `PlatformMgr.SaveRunJson(string path, JObject jsonObject, RunSaveFile saveFile)` (`PlatformMgr.cs:309`; not overridden by `SteamPlatformMgr`); reader `PlatformMgr.TryLoadRunJson(string lastSavePath, RunSaveFile saveFile, out JObject runJson, GameType gameType)` (328) |
| Own folder beside `profiles/`, e.g. `<root>/SaveFiles/<platformID>/estate/<profile file stem>/` | **Best for mod-global or per-profile data** (settings, meta, backups): nothing in vanilla enumerates or deletes it | only `SaveUtils.DoAdditionalSavesExist` scans `SaveFiles/*` and it ignores the platform-ID folder (`SaveUtils.cs:2009-2036`). Cloud coverage **[NV]** |

**(b) Dedicated profile so vanilla progress is never touched**

Use the game's own mod-profile list rather than inventing a third list:
1. Enter Estate only with `SaveUtils.inMods == true` and a mod profile created by the mod (`ProfileBhv.OnProfileSourceChange(inMods:true)`; `CreateProfile("Estate")`; remember its GUID in the plugin config; `SetCurrentProfile(guid)`).
2. While the mod list is active, `ProfileBhv.SaveToJson` only iterates mod profiles (342-359) and run saves go to `mod_profile_<guid>_runs|_kingdoms` (`SaveUtils.cs:198-220`), so `profile_*.json` and their run folders are not written. `DeleteUnusedSaves` keeps both lists' files (571-595).
3. On leaving Estate / returning to the menu, restore with `OnProfileSourceChange(false)` + `RefreshCurrentProfile()` (what `MainMenuUiScreenBhv.OnModSectionButtonToggle(false)` does, 1026-1038).
4. Also needed, because they are **not** profile-scoped: Steam achievements are unlocked regardless of profile type (`AchievementsMgr.LateUpdate` → `PlatformMgr.UnlockAchievement`, `AchievementsMgr.cs:1947-1965`; `SteamPlatformMgr.cs:169-187`) — set `TextBasedEditorPrefsBaseType.DISABLE_ACHIEVEMENTS` (checked at `AchievementsMgr.cs:1785, 1811, 1838, 1866, 1932`) or prefix `SteamPlatformMgr.UnlockAchievement` while Estate is active; `game_options.json` / `input.json` are shared by all profiles.
5. Side benefit: official CSV/asset mods only load when `inMods` (`ResourceGroupCsvDatabase.cs:124`, `ModMgr.cs:132`), so the Estate's own data can be shipped through the official mod pipeline (section 5).

### Hook points for the mod (section 4)

| Target | Patch | Why |
|---|---|---|
| `PlatformMgr.SaveRunJson(string, JObject, RunSaveFile)` (`PlatformMgr.cs:309`) | prefix | When `saveFile == RunSaveFile.KINGDOM` (or `RUN`), add `jsonObject["estate_mod"] = ...`. One patch covers every save point. |
| `PlatformMgr.TryLoadRunJson(string, RunSaveFile, out JObject, GameType)` (`PlatformMgr.cs:328`) | postfix | Read the mod section back when the game loads the same file (called from `KingdomBhv.LoadSaveIfPossible`, `KingdomBhv.cs:537`). |
| *(no patch)* `EventSaveCurrentGameModeStarted` / `EventSaveCurrentGameModeCompleted` | `EventManager.AddListener` | Write sidecar files at the game's own save cadence. |
| `SaveUtils.SaveCurrentGameMode(string, bool)` (`SaveUtils.cs:434`) | prefix | Suppress or redirect vanilla run saving while in mod-defined states where the owner's managers are not in a saveable state. |
| `PlatformMgr.SaveProfileToJson(JObject, string)` (`PlatformMgr.cs:261`) | prefix | Only if data must live inside the profile file; must add the key on every call. |
| `ProfileBhv.DeleteUnusedSaves(string)` (`ProfileBhv.cs:567`) | prefix (skip original and re-implement with extra keep names) | Only if the mod insists on storing files in `profiles/`. Prefer the sibling folder. |
| `ProfileBhv.OnProfileSourceChange(bool)`, `CreateProfile(string)`, `SetCurrentProfile(uint)` | call | Dedicated-profile handling; listen to `EventCurrentProfileChanged` to reload mod data. |
| `KingdomSaveSelectUIBhv.RefreshSaves()` (`KingdomSaveSelectUIBhv.cs:139`), `KingdomMainMenuUIBhv.Show()` | postfix (Option K) | Hide Estate slots from the Kingdoms list / hide vanilla slots from the Estate list. Slot stats are read straight from `kingdom.json` (219-293), so a marker key there is enough to classify. |
| `ProfileInstance.OnKingdomSaveCreated(string directoryName, string saveName)` (`ProfileInstance.cs:1512`) | reference | Slot naming: set the name first with `SaveLoadMgr.SetKingdomNameForNewKingdom(string)` (`SaveLoadMgr.cs:462-465`). |
| `SteamPlatformMgr.UnlockAchievement(string)` (`SteamPlatformMgr.cs:169`) | prefix | Block achievement unlocks during modded play. |

---

## 5. Official mod system and data tables

### 5.1 Loader

`Assets.Code.Mod.ModMgr : Singleton<ModMgr>, IGameModeEnterStart` (`IronCrown/Assets.Code.Mod/ModMgr.cs`).
- Sources: (1) folders under `Application.streamingAssetsPath + "/mods"` (`nexusModPath`, line 31; scanned by `VerifyModsAreLoaded()` 219-255 and `GetAllMods()` 351-377); (2) Steam Workshop: `SteamPlatformMgr.QueryUserSubscribedUGC()` → `OnUGCQueryCompleted` builds a `ModInfo` (id, title, description, metadata, install folder) and calls `ModMgr.AddMod(ModInfo)` (`SteamPlatformMgr.cs:382-459`; `ModMgr.cs:405-413`); new subscriptions arrive through `OnItemInstalled` (461-469). Steam app id `1940340` (line 20); `m_isModSupported = true` on Steam (line 118).
- `ModInfo` fields: `id`, `path`, `description`, `expandedDescription`, `metaData`, `modName`, `modCatalogPath`, `order`, `locator`, `isLoaded`, `type`, `on`, `gameType` (`IronCrown/Assets.Code.Mod/ModInfo.cs:13-37`). `manifest.json` keys: `title`, `description`, `version`, `game_mode` (int → `ModGameType`) (`ModMgr.cs:425-456`); icon `mod_icon.png`.
- Enabled state and order come from `profiles/modlist_<guid>.json` (Kingdoms: `modlist_<guid>_<slot dir>.json`) written by the mod screen (`ModScreenWidgetBhv.cs:137-197`).
- Mods are only active for **mod profiles**: asset loading is gated by `SaveUtils.inMods` (`ModMgr.cs:132`), CSV gathering too (`ResourceGroupCsvDatabase.cs:124`).

### 5.2 Accepted content (per mod folder; `ModMgr.LoadModInfo(ModInfo)`, 283-329)

| Content | Detection | Effect |
|---|---|---|
| Addressables catalog + bundles | first `*.json` in `<mod>/assets` (flag `Mod_Asset = 1`) | `{Assets.Code.Mod.ModMgr.nexusModPath}` in the catalog is replaced by the catalog's directory and written next to it as `<name>.jsondd`; later `Addressables.LoadContentCatalogAsync`; every location labelled `"Mod"` is added to the matching `ResourceDatabaseAddressable<,>` subclass (`m_ResourceLocationDictionary`, `m_ResourceNames`), replacing same-named resources, then `AssignModdedOverrides(assetName, locator)` (`ModMgr.cs:148-203, 288-305, 331-349`) |
| Localization | non-empty `<mod>/localization` (`Mod_Loc = 4`) | `Localization.AddLoadPath(dir, 3)` — priority 3 beats base (0), DLC (1), game-type override (2) (`ModMgr.cs:306-310`; `LocalizationUtils.cs:52-58`). Text format `key=value` lines, `#` comments (sample: `StreamingAssets/mods/test_token_creation_export/Localization/dd2_mod_strings_token.txt`) |
| Data CSV | any `*.csv` in the mod root (`Mod_Data = 2`) | gathered by `ResourceGroupCsvDatabase.GatherModResources()` from: mod root, `<mod>/<dlcName>/`, `<mod>/<gameTypeName>/`, `<mod>/Overrides/`, `<mod>/Overrides/<gameTypeName>/`, `<mod>/Overrides/<dlcName>/` (`ResourceGroupCsvDatabase.cs:122-171`) |
| Editor prefs | any `*.txt` in the mod root (`Mod_EP = 8`) | `TextBasedEditorPrefs.ParseTextFile(file, isAdditive:true)` — **a mod can set debug/cheat prefs** (`ModMgr.cs:316-325`); cleared on unload (88-99) |
| FMOD banks | `AudioBankUtils.GetModBankNames(mod)` | `AudioBankMgr.PopulateModBankList(path, names)` (`ModMgr.cs:205-211`) |

Nothing code-like: there is no `Assembly.Load`/`LoadFrom` anywhere in `IronCrown`, and the mod loader never touches `.dll` files. Code mods need an external loader (BepInEx).

### 5.3 Order relative to base data

Triggered each time the player leaves the main menu (`PostMainMenuSystemsInstaller.TryInstall()`, `PostMainMenuSystemsInstaller.cs:301-312`), after `ModMgr.StartLoadModInfo()` was called by the menu (`MainMenuUiScreenBhv.StartModdedGameLoad`, 570-578; `KingdomModStepBhv.cs:46-47`; `KingdomSaveSelectUIBhv.cs:118-122`):
1. `GatherResourcesIfEmpty()` → base: `StreamingAssets/Excel/*.Group.csv`, then `Excel/<owned DLC>/` (`ResourceGroupCsvDatabase.cs:173-209`).
2. `GatherGameTypeResources()` → `Excel/<CurrentGameType name>/` with `overrideCSV: true` (86-97).
3. `GatherModResources()` → enabled mods in list order (122-171); a folder path containing `"Overrides"` sets `overrideCSV` (211-214).
4. `Install()` → every `Library*` runs `Initialize()` → `InitInternal()`.
5. Then mod asset catalogs: `ModMgr.OnGameModeEnterStart(MAIN_MENU)` had registered a post-install callback that runs `LoadModAssets()` (`ModMgr.cs:379-390`); `GameModeMgr` waits on `ModMgr.LoadingMods` (`GameModeMgr.cs:443-447`).
6. Returning to the main menu clears everything: `PostMainMenuSystemsInstaller.Clear()` → `Library.SystemClear()` removes all elements (`Library.cs:74-80`); `ResourceGroupCsvDatabase.Clear()` (`PostMainMenuSystemsInstaller.cs:285-299, 319-325`); `ModMgr.UnloadMods()`.

### 5.4 How data tables load and are looked up

- File format ("Group CSV"), parsed in `ResourceGroupCsvDatabase.GatherResourcesFromFolder` (216-264): files named `*.Group.csv`; blocks `element_start,<name>,<TypeKey>` … rows … `element_end`. Rows are `field,value,...` (see `StreamingAssets/mods/test_token_creation_export/dd2_mod_data_token.Group.csv`, e.g. `element_start,test_token_creation,Token` / `m_Chance,1,` / `element_end`). Blocks are bucketed by `<TypeKey>` as `NamedResourceData(name, data, dlcNumber, isOverride)`.
- Each library has a serialized `ResourceDatabaseText` (ScriptableObject) with `m_FileTypeFilters` (the type keys it accepts) and `m_FileExtension` (`IronCrown/Assets.Code.Resource/ResourceDatabaseText.cs:15-19`). `GetNumberOfResources()` rebuilds its list: loose files `*.<Type>.<ext>` from the Excel folder and from **every loaded mod path incl. sub-directories — not gated by `inMods`** (77-124), then the grouped blocks (`ResourceGroupCsvDatabase.AddToResourceDatabase`, `ResourceGroupCsvDatabase.cs:58-76`).
- A library constructs one definition per resource and calls `AddLibraryElement(def, resource.m_overrideCSV)`, e.g. `LibraryToken.InitInternal` (`IronCrown/Assets.Code.Token/LibraryToken.cs:24-54`), `LibraryItem.InitInternal` (`IronCrown/Assets.Code.Item/LibraryItem.cs:28-50`), generic `LibraryDataContainerCsv<T>.InitInternal` (`IronCrown/Assets.Code.Data/LibraryDataContainerCsv.cs:21-31`).
- Duplicate keys (`Library.AddLibraryElement`, `Library.cs:281-324`): `overrideCSV` → replace; otherwise if both implement `IAddable` → merge; otherwise logged error and ignored.
- Lookup: `SingletonMonoBehaviour<Library<string, TDefinition>>.Instance.GetLibraryElement(id)`, `TryGetLibraryElement`, `GetLibraryElements(Predicate<T>)`, `GetLibraryElementRandom(...)` (`Library.cs:141-279`); typed accessors such as `LibraryToken.LibraryTokenInstance`. Rules singletons: `RulesManager.GetRules<TRules>()` (`RulesManager.cs:52-55`).
- Addressable assets by id: `Singleton<ResourceDatabaseX>.Instance.GetResource(resourceId)` (`IronCrown/Assets.Code.Resource/ResourceDatabaseAddressable.cs:114-144`).
- The base CSVs are hashed for analytics tamper detection (`ResourceDatabaseText.TestAnalytics`, 147-154); any editor pref or cheat disables analytics in release (`TextBasedEditorPrefs.cs:165-168`).

### Hook points for the mod (section 5)

| Target | Patch | Why |
|---|---|---|
| *(no patch, preferred)* `Singleton<ModMgr>.Instance.AddMod(new ModInfo { path = <plugin data dir>, modName = ..., on = true })` then `StartLoadModInfo()` (`ModMgr.cs:405-413, 257-260`) | call | Registers the code mod's data folder as if it were a Workshop item: CSV, localization, catalog and banks then flow through the vanilla pipeline with correct ordering and unload/reload. Requires `SaveUtils.inMods` (fits the dedicated mod profile). `VerifyModsAreLoaded()` resets `on` from the mod-list file only for mods present in that file (238-254), so set `on` after it or patch it. |
| `ResourceGroupCsvDatabase.GatherModResources()` (`ResourceGroupCsvDatabase.cs:122`) | postfix | If data must load outside mod profiles: call the private `GatherResourcesFromFolder(folder, 0, overrideCSV, null)` for the plugin's folder. Runs right before `Install()`, so rows take part in every library's `InitInternal` and `PostInit`. |
| `ModMgr.GetModsPaths()` (`ModMgr.cs:213`) | postfix | Append the plugin data dir; consumed by exactly two callers (`ResourceGroupCsvDatabase.cs:129`, `ResourceDatabaseText.cs:83`). Lightest way to inject both grouped and loose CSV files. |
| `Library<TKey,TElem>.AddLibraryElement(TElem, bool overrideCSV)` (`Library.cs:281`) | call from a `PostMainMenuSystemsInstaller.OnLoadFinished` prefix | Add or replace rows built in code. Must be redone after every return to the main menu (libraries are cleared). Closed generic types must be patched/called per library. |
| `Localization.AddLoadPath(string loadPath, int priority)` (`Localization.cs:150`) | call | Mod strings; the loaded data object is replaced on every language change (`SetLanguage`, 121-148), so re-add in an `EventLanguageChanged` listener. The directory must exist (`EnglishLocalizationData.cs:50-54`). |
| `ResourceDatabaseAddressable<,>.AssignModdedOverrides(string, IResourceLocator)` (308) / `ModMgr.LoadAdditionalDatabaseResources` (331) | reference | How mod bundles override art by resource name. |

---

## 6. Debug / cheat facilities in the release build

### 6.1 Command-line arguments (`IronCrown/Assets.Code.Utils/CommandLineUtils.cs:14-71`)

| Arg | Effect |
|---|---|
| `-allowEditorPrefs` | Loads `StreamingAssets/editor_prefs.txt` at type init (`TextBasedEditorPrefs.cs:53-76`); subscribes all cheat hotkeys and the console toggle (`CommonUiBhv.cs:933-937`); shows the editor-prefs tab and debug buttons in Options (`OptionsMenuUiBhv.cs:148-161`); installs `GameModeOverrideBhv` (`MainMenuSystemsInstaller.cs:54-57`); enables the demo-build "play with editor prefs" button (`MainMenuUiScreenBhv.cs:386-403`). |
| `-consoleEnabled` | Adds `DebugDevConsoleBhv` and `Console` (`GameInstaller.cs:176-180`). `Console` is an **IMGUI log viewer only** (toggle key Backquote; filter/clear), no command input (`IronCrown/Assets.Code.Utils/Console.cs:14, 164-184, 248-265`). |
| `-experimental` | Shows the experimental options section; affects video playback path (`OptionsMenuUiBhv.cs:136-139`; `VideoPlayerControllerBhv.cs:194, 235`). |

There is no text command console and no other argument parsing (`Environment.GetCommandLineArgs` appears only in `CommandLineUtils`).

### 6.2 Editor prefs (345 keys)

`TextBasedEditorPrefsBaseType : CustomEnum<...>` with typed subclasses; values readable via `TextBasedEditorPrefs.GetBool/GetString/GetInt/GetFloat(...)`. File format: one `key-value` per line (first `-` splits), `#` or `//` comments (`TextBasedEditorPrefs.cs:55-56, 115-169`). They can be set three ways: (1) `-allowEditorPrefs` + `StreamingAssets/editor_prefs.txt` (the file does not exist in the shipped build; this route writes into the game folder); (2) a `*.txt` file in an official mod (5.2); (3) from code, with no file in the game folder: `TextBasedEditorPrefs.ParseTextFile(string filePath, bool isAdditive)` (public, line 100) or `TextBasedEditorPrefs.SetBool/SetString/SetInt(...)` (304-356).

Most useful for automated testing (`IronCrown/Assets.Code.Utils/TextBasedEditorPrefsBaseType.cs`, line numbers in brackets):
- Flow: `disable_disclaimer` [399], `disable_start_screen` [401], `disable_intro_cinematic` [403], `disable_create_profile` [173], `disable_tutorials` [607], `run_test_skip_prologue` [455], `run_test_game_mode` [449] (string; consumed by `GameModeOverrideBhv.GetGameModeOverride()` to replace the **next** `SetMode` target once, `IronCrown/GameModeOverrideBhv.cs:13-26`, `GameModeMgr.cs:258-267`; value `COMBAT` also flips `CombatSystemsInstaller.IsDebug`, `CombatSystemsInstaller.cs:26-52`).
- Saves: `disable_save_game` [171], `disable_load_game` [169], `disable_save_profile` [167], `disable_load_profile` [165], `load_old_version_saves` [159], `kingdom_load_directory_substring` [177], `number_of_old_saves_to_keep` [175], `zip_old_saves` [161].
- Start a specific battle: `battle_test_battle_configuration` [279] (+ `battle_test_additional_battle_configuration` [281], `battle_test_combat_arena` [357], `battle_test_combat_source` [373], `battle_test_battle_modifier` [287]) feed `CombatSystemsInstaller.RollDebugCombatScenarioData()` (`CombatSystemsInstaller.cs:81-113`), used when the game type starts in `COMBAT` with `CombatSystemsInstaller.IsDebug` (`GameTypeMgr.cs:350-363`).
- Party / heroes: `battle_test_team_0` [291] and `hero_party` [477] (comma list of actor class ids → `RosterManager.BuildTestParty`, `IronCrown/Assets.Code.Roster/RosterManager.cs:83-100`), `battle_test_team_1` [295], `hero_test_start_trinkets` [499], `hero_test_start_combat_item` [493], `hero_test_start_quirks` [495], `hero_quirks_per_hero` [531], `hero_test_start_skills` [503], `hero_test_path(s)` [521, 525], `hero_upgrade` [509], `hero_test_party_start_health_range` [505], `hero_test_party_start_stress_range` [507], `hero_test_stress` [513].
- Deterministic combat: `battle_test_hit` [315], `battle_test_miss` [317], `battle_test_crit` [319], `battle_test_always_resist` [333], `battle_test_never_resist` [335], `battle_test_turn_order` [355], `battle_test_end_at_round` [367], `battle_test_team_0_controller` / `_1_controller` (`INPUT`/`RANDOM`/`TEST`) [293, 297], `battle_test_all_skills` [323], `battle_test_infinite_combat_items` [327], `battle_test_retreat` [349], `battle_test_controls` [277].
- Built-in automation: `battle_run_coverage_test` [381] (+ `battle_coverage_test_all_skills`, `_add_delays`, `_battle_configurations` [383-387]); `input_simulator_enabled` [743] (+ `input_simulator_confession`, `_perf_logging`, `_boss_rush` [745-749]) drives a whole Confession with a virtual gamepad (`IronCrown/Code.FlyingRat.Simulator/InputSimulator.cs:53-80, 144-158, 233-270`; **[NV]** where the component is instantiated — no code reference creates it, so it is presumably on a prefab).
- Profile/unlocks: `profile_test_all_unlocks` [185], `profile_test_super_profile` [201], `profile_test_unlocks` [187], `profile_value_type` / `profile_value_amount` [181-183], `profile_test_hero_skins` [209], `altar_of_hope_all_memories` [213].
- Inn / Kingdom: `inn_test` [217], `inn_bonuses_test` [219], `inn_test_hire` [221], `inn_show_all_biomes` [225], `kingdom_inn_test` [101], `kingdom_camp_test` [103], `kingdom_siege_disable` [127], `kingdom_inn_free_upgrades_purchase` [111], `kingdom_test_inn_upgrades` [113], `kingdom_test_inn_level` [115], `kingdom_disable_curse_spread` [149].
- Run: `run_test_boss` [441], `run_test_victory` [445], `run_test_score` [447], `run_test_loot` [453], `map_generation_overrides` [407], `map_generation_simple_biome_override(_data)` [409, 585].
- Misc: `enable_cheats` [549], `disable_achievements` [157], `debug_skip_validation` [475] (skips CSV validation), `log_game_mode_changes` [757] (writes `GameModeFlow_<timestamp>.txt` into the run-saves folder, `IronCrown/Assets.Code.Utils/LogToFileUtils.cs:22-53`), `log_redhook_scene_loading` [751].

Caveat: many "debug boot" branches also require `Debug.isDebugBuild && Application.isEditor` (`DebugLoader.IsAnyDebugBootUp`, `IronCrown/Assets.Code.Debugging/DebugLoader.cs:40-65`; `EmbarkBhv.cs:76`) or a non-splash starting scene (`InnSystemsInstaller.cs:25-43`), which a release build never satisfies. Only the pref-driven paths above are reachable.

### 6.3 Cheats

- `Assets.Code.Game.Cheats` (static): `SubscribeCheatButtons()` (needs `enable_cheats`; `IronCrown/Assets.Code.Game/Cheats.cs:76-100`) registers `CheatHotKeyType` hotkeys (`IronCrown/Assets.Code.Utils/CheatHotKeyType.cs:21-99`): Shift+G gold, Shift/Ctrl+T torch, Shift/Ctrl+V doom, Shift+U upgrade points, Shift/Ctrl+H heal/damage party, Shift/Ctrl+S stress, Alt+Q skip quest step, Alt+J actor editor, Ctrl+J combat debug menu, Alt+Up/Down game speed, `/` screenshot, plus delete-all-saves actions with no default key.
- All handlers are public static and callable directly: `Cheats.OnAddGold(string, InputActionDelegateValues)` (pass `new InputActionDelegateValues(performed:true, canceled:false, started:false)` as in `DebugCheatMenuUiBhv.cs:92-93`), `OnHeal`, `OnDealDmg`, `OnAddUpgradePts`, `OnIncreaseGameSpeed`, `ChangeStageCoachArmor(int)`, ...
- Pause-menu cheat buttons (shown when `enable_cheats`, `PauseMenuUiControllerBhv.cs:116-119`), all public instance methods: `ButtonWinCombat()` / `ButtonLoseCombat()` → `CombatBhv.ForceEndCombat(bool isForceComplete)` (474-489; `IronCrown/Assets.Code.Combat/CombatBhv.cs:474`), `CheatButtonSkipToInn()` (569-588), `CheatButtonSkipToAltarOfHope()` (590-596), `CheatButtonSkipToHeroSelect()` (637-643), `CheatButtonAddTrophy()` (598-609), `CheatAddCandles()` (611-614), `ButtonShowCheatMenu()` (621-635, pushes the `DebugCheatMenuUiBhv` screen).
- Direct APIs that need no cheat flag: grant items `Singleton<GameTypeMgr>.Instance.PlayerInventory.AddItems(ItemDefinition, int qty, bool isPurchase)` (as in `Cheats.cs:172-173`); run values `RunValues.ChangeValue(RunValueType, float, SourceType.DEBUG)` (132); start a battle `new CombatScenarioData(string battleConfigurationId, string backgroundScene, CombatSource loadedFrom[, IReadOnlyList<uint> startingPartyActorGuids])` + `Singleton<GameTypeMgr>.Instance.SetCombatScenario(data, isLoad:true)` + `SetMode(GameModeType.COMBAT, false)` (pattern at `InnBhv.cs:372-374`, `KingdomSiegeManager.cs:356-358`; ctors `IronCrown/Assets.Code.Combat/CombatScenarioData.cs:210-218`); change state `Singleton<GameModeMgr>.Instance.SetMode(...)`.
- Dangerous: `Cheats.DeleteAllSaves` / `DeleteAllSavesAndProfiles` → `SaveUtils.DeleteAllSaves()` / `DeleteAllSavesAndProfiles()` wipe the real profiles folder (`Cheats.cs:291-305`; `SaveUtils.cs:1970-1987, 2047-2063`).

### Hook points for the mod (section 6)

| Target | Patch | Why |
|---|---|---|
| `CommandLineUtils.IsEditorPrefsEnabled()` / `IsConsoleEnabled()` (`CommandLineUtils.cs:61-71`) | postfix returning true (test builds only) | Unlocks every vanilla debug surface without launch arguments. Must be applied before `TextBasedEditorPrefs`' static constructor and before `GameInstaller.SetupSystemResolvers` to take full effect; otherwise call `TextBasedEditorPrefs.ParseTextFile(path, false)` manually. |
| *(no patch)* `TextBasedEditorPrefs.ParseTextFile(<plugin dir>/test_prefs.txt, isAdditive:true)` | call from plugin init | Scenario setup from a file kept inside the mod folder; nothing is written to the game folder. Note it disables analytics for the session (`TextBasedEditorPrefs.cs:165-168`), which is desirable for a modded client. |
| *(no patch)* `Cheats.*`, `PauseMenuUiControllerBhv.CheatButton*`, `CombatBhv.ForceEndCombat(bool)` | call | Test drivers: skip to inn, win/lose the current fight, grant resources. |
| `GameModeMgr.SetMode` + `OnNextGameModeEnterComplete` | call | Scripted navigation with completion callbacks; `GameModeMgr.IsLoadingGameMode` / `IsChangingState()` are the "busy" probes. |
| `SaveUtils.GetIsGameSaveDisabled()` / `disable_save_*` prefs | call / prefs | Run test sessions that cannot write to saves at all. |
| `Application.logMessageReceivedThreaded` (how `Console` taps logs, `Console.cs:138-146`) | subscribe | Collect errors for test assertions; `Debug.Log` output otherwise goes to Unity's `Player.log` **[NV]** path. |

---

## Open unknowns

1. BepInEx is not installed in the game folder yet (no `BepInEx/`, `winhttp.dll` or `doorstop_config.ini` in the game root at recon time). Plugin start time relative to `StaticInitializerAttribute.InitializeValues` and to `GameInstaller` creation is **[NV]**; the mod should not depend on it (force `GameType`'s static init explicitly; do all registration before the splash video ends).
2. Whether `KingdomBhv` can be started with a stub map definition/gang and its sieges, events and day loop neutralised by data alone (Option K), and what `KingdomManager`/`KingdomMapManager` assume about the map scene.
3. How many of the 608 direct `KingdomBhv.Instance.` / `RunBhv.Instance.` dereferences are reachable from `COMBAT`, `RESULTS` and loot screens under a third `GameType` (Option T) — needs the combat/inn recon or runtime testing.
4. Steam Cloud coverage of the save tree (Auto-Cloud root and patterns) and therefore whether a sibling `estate/` folder syncs.
5. Scene-serialized wiring: which UnityEvents call the menu handlers, how `main_menu_kingdom` is loaded, where `InputSimulator` is instantiated, and the exact hierarchy around `m_kingdomsMainMenuButton` (needed for the cloned button's layout).
6. Foreign-language behaviour for a missing `game_type_override_<name>` folder: `ForeignLocalizationData.LoadStringsAtPath` loads `<path>/<iso>.po` through `PoFile.Load` and `TryLoadStrings` fails the same way if it returns false (`IronCrown/Assets.Code.Locale/ForeignLocalizationData.cs:45-88`), but `PoFile.Load` on a missing file was not read.
7. Windows paths for `Application.persistentDataPath` and `Player.log` were inferred, not observed.

---

## Appendix A — every `GameType.KINGDOM` / `GameType.EXPEDITION` reference (280 in 77 files)

Paths relative to `IronCrown/`.

| File | Lines |
|---|---|
| `Assets.Code.Achievements/AchievementsMgr.cs` | 254, 375, 1146 |
| `Assets.Code.Achievements/ActorDeathCountEntry.cs` | 31 |
| `Assets.Code.Actor/ActorDataClass.cs` | 410, 414 |
| `Assets.Code.Actor/ActorInstance.cs` | 634, 876, 2393, 2400, 2701, 2708 |
| `Assets.Code.Analytics/AnalyticsBhv.cs` | 716 |
| `Assets.Code.Audio/AudioConditionUtils.cs` | 62, 183 |
| `Assets.Code.Audio.Ambience/KingdomResultsAmbienceBhv.cs` | 19 |
| `Assets.Code.Audio.Banks/AudioBankMgr.cs` | 279, 289 |
| `Assets.Code.Campaign/CampaignBhv.cs` | 61, 161, 176 |
| `Assets.Code.Campaign/HeroSelectBhv.cs` | 423, 502, 506, 1144, 1308, 1316, 1411, 1662, 1679, 1687 |
| `Assets.Code.Combat.BattleModifier/BattleModifierCalculation.cs` | 21 |
| `Assets.Code.Embark/EmbarkBhv.cs` | 334 |
| `Assets.Code.Game/GameModeMgr.cs` | 593 |
| `Assets.Code.Game/GameTypeMgr.cs` | 37, 400, 404, 410, 414 |
| `Assets.Code.Game/GameUIBhv.cs` | 281, 361, 1019, 1030, 1100 |
| `Assets.Code.Game/SelectionGameType.cs` | 10 |
| `Assets.Code.Game.StageCoach/ResourceDatabaseTorch.cs` | 23 |
| `Assets.Code.Inn/InnBhv.cs` | 88, 115, 172, 317, 321, 332, 340 |
| `Assets.Code.Inn/InnSystem.cs` | 79, 125, 174 |
| `Assets.Code.Inn.Presentation/InnPresentationBhv.cs` | 345, 629, 659, 670, 701, 1035, 1060, 1089, 1162, 1185, 1203, 1210, 1217, 1223, 1230, 1250, 1428, 1505, 1567 |
| `Assets.Code.Kingdom/KingdomBhv.cs` | 257, 353, 380, 504, 522, 537 |
| `Assets.Code.Kingdom/KingdomSaveSelectUIBhv.cs` | 123, 224, 230, 260, 262 |
| `Assets.Code.Kingdom.UI/KingdomMainMenuUIBhv.cs` | 104, 201, 208 |
| `Assets.Code.Map/MapMgrBhv.cs` | 634, 636, 646, 650, 727, 995 |
| `Assets.Code.Platform/RichPresence.cs` | 12 |
| `Assets.Code.Platform/RichPresenceMgr.cs` | 87, 266, 369, 373, 390 |
| `Assets.Code.Platform/RichPresenceType.cs` | 14, 20, 22, 24, 28, 30, 32, 34, 51, 52 |
| `Assets.Code.Player/PlayerItemInventory.cs` | 255, 262 |
| `Assets.Code.Profile/ProfileBhv.cs` | 282, 291, 335, 466, 467, 474, 475, 561, 562, 580, 581, 593, 594 |
| `Assets.Code.Profile/ProfileInstance.cs` | 287, 1450, 1454, 1584 |
| `Assets.Code.Rendering/FeatureSetterBhv.cs` | 121 |
| `Assets.Code.Rendering/LUTAsset.cs` | 27 |
| `Assets.Code.Rendering/LUTManager.cs` | 319, 338, 342, 417, 584 |
| `Assets.Code.Roster/RosterManager.cs` | 618, 624, 1183, 1187 |
| `Assets.Code.Run/RunBhv.cs` | 110, 408, 426 |
| `Assets.Code.Run/RunLogManager.cs` | 186 |
| `Assets.Code.Serialization/SaveLoadMgr.cs` | 104, 158, 317, 352, 356, 433, 447, 449, 450 |
| `Assets.Code.Tutorial/TutorialEventsBhv.cs` | 212, 343, 437, 438, 553, 559, 583, 711 |
| `Assets.Code.Tutorial/TutorialMgr.cs` | 71 |
| `Assets.Code.Tutorial/TutorialSaveInstance.cs` | 25, 37 |
| `Assets.Code.Tutorial/TutorialType.cs` | 22, 23, 26, 28 |
| `Assets.Code.ui/BattleInfoUiBhv.cs` | 284, 597, 803 |
| `Assets.Code.ui/CharacterSheetConditionsUiBhv.cs` | 67 |
| `Assets.Code.ui/CharacterSheetRelationshipActorUiBhv.cs` | 168 |
| `Assets.Code.ui/CharacterSheetStatsUiBhv.cs` | 599 |
| `Assets.Code.ui/CurrencyContainerBhv.cs` | 87 |
| `Assets.Code.ui/InnReplacementActorBhv.cs` | 85, 220 |
| `Assets.Code.ui/SkillSelectionUtils.cs` | 43, 175 |
| `Assets.Code.ui/ToastManager.cs` | 261, 275 |
| `Assets.Code.ui/UpgradeSkillButton.cs` | 146 |
| `Assets.Code.UI.Controllers/PauseMenuUiControllerBhv.cs` | 508 |
| `Assets.Code.UI.HeroSelect/HeroSelectActorUIBhv.cs` | 142, 506 |
| `Assets.Code.UI.Items/CombatInventoryItemBhv.cs` | 116 |
| `Assets.Code.UI.Items/InventoryItemStageCoachUpgradeBhv.cs` | 430 |
| `Assets.Code.UI.Items/PlayerInventoryItemBhv.cs` | 135, 433, 463, 499, 555 |
| `Assets.Code.UI.Items/TrinketInventoryItemBhv.cs` | 101 |
| `Assets.Code.UI.Managers/CommonUiBhv.cs` | 1184, 1872, 1891, 1897, 1932, 1942, 2421 |
| `Assets.Code.UI.Managers/MainMenuControllerBhv.cs` | 115, 245, 320, 522, 528, 543, 561, 565 |
| `Assets.Code.UI.Screens/InnUpgradeSkillsBhv.cs` | 207, 443, 495, 1132 |
| `Assets.Code.UI.Screens/InventoryUiBhv.cs` | 128, 231, 279, 732 |
| `Assets.Code.UI.Screens/MainMenuUiScreenBhv.cs` | 439, 444, 553, 566, 587, 1032, 1049, 1064, 1089 |
| `Assets.Code.UI.Screens/StageCoachConfigUiBhv.cs` | 230, 495, 628, 729, 742, 754, 773 |
| `Assets.Code.UI.Screens/StoryScreenBhv.cs` | 141, 162 |
| `Assets.Code.UI.Widgets/CharacterSheetUiBhv.cs` | 251, 321, 732, 846 |
| `Assets.Code.UI.Widgets/InnReplacementScreenWidgetBhv.cs` | 127 |
| `Assets.Code.UI.Widgets/ModScreenWidgetBhv.cs` | 113, 115, 117, 119, 139, 161 |
| `Assets.Code.UI.Widgets/PartyBrowserBhv.cs` | 35, 83 |
| `Assets.Code.UI.Widgets/RestItemSlotBhv.cs` | 182, 349, 353, 820 |
| `Assets.Code.UI.Widgets/SubScreenBiomeChoiceBhv.cs` | 58 |
| `Assets.Code.UI.Widgets/SubScreenBiomeResultsBhv.cs` | 120, 190, 207 |
| `Assets.Code.UI.Widgets/SubScreenCollectionBhv.cs` | 332, 457, 898 |
| `Assets.Code.UI.Widgets/SubScreenInnEndRunBhv.cs` | 49 |
| `Assets.Code.UI.Widgets/SubScreenRelationshipMatrixBhv.cs` | 68 |
| `Assets.Code.UI.Widgets/TokenGlossaryWidgetBhv.cs` | 161 |
| `Assets.Code.Unlock/UnlockUtils.cs` | 25 |
| `Assets.Code.Utils.Serialization/SaveUtils.cs` | 375, 626, 627, 695, 947, 1676, 1678, 1825, 1856, 2120, 2163, 2321, 2327, 2342, 2346, 2365 |
| `Assets.Code.Utils.Serialization.Events/EventRunSaveRemoved.cs` | 34 |

Data-driven uses of the `GameType` flags (no code change needed, set in the ctor): `m_IsActorDataPathRunGoalsValid` (`Assets.Code.Actor/ActorDataPath.cs:81, 111`), `m_IsStoreInventoryRolledOnEnterInn` (`Assets.Code.Inn/InnInstance.cs:365`), `m_IsStageCoachItemSlotEquipLimitValid` (`InnInstance.cs:914`), `m_IsSameRouteAsPreviousValid` (`Assets.Code.Map.Generation.Route/RouteCalculation.cs:51`), `m_IsProfileSeedUsedForQuirkGeneration` (`Assets.Code.Quirk/QuirkContainer.cs:217`), `m_IsMissingHeroesAddedAtInn` (`Assets.Code.Roster/RosterManager.cs:347`), `m_ValidStartingRosterStatusTypes` (`RosterManager.cs:451`), `m_IsBiomeCompleteLogEntryValid` (`Assets.Code.Run/RunLogManager.cs:280`), `m_PartyRemovingAtInnRosterStatusType` (`Assets.Code.ui/InnReplacementActorBhv.cs:191`, `Assets.Code.UI.Widgets/InnReplacementScreenWidgetBhv.cs:115-119`), `m_LoadDefaultGameModeType` (`Assets.Code.Utils.Serialization/SaveUtils.cs:277`). Enumeration of all game types: `CustomEnum<GameType>.GetInstances()` in `Assets.Code.Locale/LocalizationUtils.cs:164` and `Assets.Code.Resource/ResourceGroupCsvDatabase.cs:108`.
