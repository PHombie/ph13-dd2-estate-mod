# Estate save / continue — design

Target: Darkest Dungeon II v2.04.85095. Source: decompiled `IronCrown.dll`. The game was not launched and the
save folder was not read; everything below is from code.

Conventions
- `file:line` is relative to `_ref\dd2-decomp\IronCrown\` (folders = namespaces), e.g.
  `Assets.Code.Utils.Serialization/SaveUtils.cs:434`.
- **[NV]** = not verified: inferred, or it depends on data/scene content that the code does not show.
- "Snapshot" = one `Save_NNNN_<label>` folder. "Slot" = one `yyMMdd_HHmmss` folder holding snapshots.
  "Hub" = the mod's `ESTATE` game mode.

Code: `src\DD2Estate\Dd2\EstatePersistence.cs`, `src\DD2Estate\Dd2\PersistencePatches.cs`.

---

## 0. Summary

| Question | Answer |
|---|---|
| Does the vanilla snapshot writer work in `ESTATE` mode? | Yes, nothing throws. It writes `actors.json` + `kingdom.json` into `Save_NNNN_<label>` with **no sub-folder and no `map.json`**. |
| What would vanilla resume such a snapshot as? | `GameModeType.INN` (the Kingdom default), which cannot work. The mod never asks; it always resumes in the hub. |
| Is "no kingdom map is ever set" true? | **No.** `KingdomMapManager` builds a 1x5 **debug map** by itself at `EventKingdomStarted`. That map is what makes both saving and loading work, see 1.4 and 2.4. |
| Does the vanilla loader restore an Estate snapshot? | Yes: actors, roster, inventory, random streams, the debug map and the dormant day all come back through `KingdomBhv.StartKingdom(isLoad: true)`. |
| What can destroy the save? | A kingdom game over (blocked), the Kingdoms list of the Estate profile (slot hidden), and snapshot numbers past 9999 (renumbered). Trimming keeps the 3 newest snapshots. |
| Mid-fight policy | Confirmed: save only in the hub. Nothing writes run files except `SaveUtils.SaveCurrentGameMode`. |
| Achievements | One prefix on `AchievementsMgr.QueueAchievementUnlock`. |

---

## 1. (a) What `SaveCurrentGameMode` does in `ESTATE` mode

Call chain: `SaveUtils.SaveCurrentGameMode(nodeTypeName, isSoftSave)` (`Assets.Code.Utils.Serialization/SaveUtils.cs:434-440`)
→ private `SaveCurrentGameModeAsync(SaveGameContext, bool)` (346-423).

### 1.1 Before anything is written

1. `GetIsGameSaveDisabled()` (348, 1755-1762) is false on a normal boot: `GetIsSaveLoadDisabled()` (1674-1685) only
   looks at debug boot-ups (`CombatSystemsInstaller.IsDebug` etc. compare `RedHookSceneManagerBhv.StartingSceneName`,
   `CombatSystemsInstaller.cs:14-24`) and `KingdomBhv.IsDebug`.
2. If a previous save is still being finished (`s_saveGameTask != null`), the request is **queued** in `s_pendingSaves`
   (354-358) and written later by `CheckPendingSaves` (323-338) through the private method, in whatever game mode is
   current at that moment. `EstatePersistence.SaveNow` never lets that happen (4.2).
3. `UpdateSaveFolderAndNumber(GameModeMgr.CurrentMode, CurrentGameType)` (368, 1838-1906):
   - no slot yet (`RunSavePath` empty) and no load path → **new slot**: `RunStartDate = DateTime.Now.ToString("yyMMdd_HHmmss")`
     (1855), `SetRunSavePath(root + "/" + RunStartDate)` (1864), `EventRunSaveCreated.Trigger(gameType, RunStartDate)` (1865),
     `DeleteOldRunSaves(gameType)` (1866). The root is `profiles/mod_profile_<guid>_kingdoms`
     (`GetCurrentProfileRunSavesPath`, 198-220; `Assets.Code.Profile/ProfileBhv.cs:332-340`).
   - after a load (`RunLoadPath` inside the profile's folder) → the **same slot** is reused:
     `SetRunSavePath(GetSaveDirFromRunPath(RunLoadPath))` (1843-1851, 1908-1916).
   - the mode is none of COMBAT / ALTAR_OF_HOPE / EMBARK / HERO_SELECT, so the `else` branch runs: `SaveNumber++` and the
     sub-save counters are reset (1901-1905).
4. `GetSaveDirPath(nodeTypeName, mode)` (369, 1926-1963): for an unknown mode the label is `nodeTypeName`; if that is null
   it is `BiomeManager.LastReachedNodeTypeName`, which is `"Unknown"` when no node was reached
   (`Assets.Code.Map.Generation.Biome/BiomeManager.cs:145-155`). Result: `<slot>/Save_<SaveNumber:D4>_<label>` (1945), with
   **no nested `Save_NNNN`** (those are only for the four modes at 1946-1961).
   `SaveNow` passes `"Estate"` → `Save_0001_Estate`.
5. `Directory.CreateDirectory` (370).

### 1.2 `RunSyncOperations(saveDirPath, mode, context)` (689-730)

| Step | Lines | In the Estate |
|---|---|---|
| `map.json` | 691-694 | Skipped: only written when `SingletonMonoBehaviour<MapMgrBhv>.HasInstance()`; `MapMgrBhv` lives in `MainScene`, which the Estate never loads (`s_instance` is cleared on destroy, `Assets.Code.Utils/SingletonMonoBehaviour.cs:31-38, 127-135`). |
| owner | 695-703 | Game type is `KINGDOM` → `KingdomBhv.Save(...)` (1.3). |
| mode-specific sub-save | 704-723 | Only `COMBAT`, `INN`, `ALTAR_OF_HOPE`, `EMBARK`, `HERO_SELECT` are handled by an `if/else if` chain with no default. `ESTATE` matches none: **no sub-folder**, nothing thrown. |
| `PlatformMgr.SaveGameModeType(mode)` | 724 | Empty on PC (`Assets.Code.Platform/PlatformMgr.cs:314-316`; `SteamPlatformMgr` overrides only the three achievement methods, `SteamPlatformMgr.cs:159, 164, 169`). The mode is **not stored anywhere**. |
| `SaveCurrentProfile()` | 725 | Writes `mod_profile_<guid>.json` and the narration/tutorial/collection/loadout files (463-481). |
| `SetRunLoadPath(saveDirPath)` | 728 | Only reached when nothing threw. |

The whole call is wrapped in `try/catch` that only logs `"Save sync operations failed"` (384-391). A failed save is
therefore silent; `EstatePersistence.Write` detects it by checking that the load path moved to a folder with `kingdom.json`.

Afterwards a task runs `RunAsyncOperations` → `ShortenRunSaves(root)` when `ABBREVIATE_SAVE_FILES` is set (425-432; default
true, `Assets.Code.Utils/TextBasedEditorPrefsBaseType.cs:179`).

### 1.3 `KingdomBhv.Save` (`Assets.Code.Kingdom/KingdomBhv.cs:601-635`)

- `LibraryActors.SaveToJson` (605) → `actors.json`, every actor with `GetShouldSave()` (`Assets.Code.Actor/LibraryActors.cs:21-40`).
- `kingdom.json` = `Version` + sections `run_values`, `version`, `random`, `run_log_manager`, `inventory`, `stagecoach`,
  `act_out_manager`, `affinity_manager`, `loot_manager`, `kingdom_map`, `kingdom_manager`, `kingdom_difficulty`, `roster`,
  `quest_manager`, `biome_manager`, `combat_manager`, `siege_manager`, `event_manager`, and `kingdom_day` when
  `m_KingdomDaySystem != null` (609-630). Written through `PlatformMgr.Instance.SaveRunJson(path, jObject, RunSaveFile.KINGDOM)` (632).
- Not saved at all (rebuilt on load): `TorchManager`, `StressManager`, `BarkManager`, `WoundManager`, `RunDataManager`
  (they are created at 654-663 but have no line in 609-630). Estate-side values that must survive (light meter etc.)
  belong in the `estate_mod` section.

### 1.4 The three objects the question names

| Object | What happens | Evidence |
|---|---|---|
| `KingdomMapManager.SaveToJson()` | Never throws. `PerFieldToJson` catches and returns an empty object (`Assets.Code.Utils.Serialization/JsonSerializationUtils.cs:791-802`); a null field serializes as JSON null (`CachedToJson`, 821-826; `ComplexTypeSerializer.ToJson`, 446-461). **But the map is not null in the Estate**: on `EventKingdomStarted` with `m_IsLoad == false` and `m_KingdomMap == null`, the manager looks for a `KingdomMapRootBhv` in the loaded scenes, finds none, and calls `KingdomMapDefinition.GenerateDebugKingdomMapDefinition()` + `SetMap` (`Assets.Code.Kingdom/KingdomMapManager.cs:188-228`). That definition is one row `INN, BIOME(city), CAMP, BIOME(tundra), BOSS` with id `"debug"` (`KingdomMapDefinition.cs:95-106`). `SetStageCoachCoordinatesToStart` then leaves the *current* coordinates invalid and sets the *next* ones to the inn (575-582), which `SaveToJson` adds as `next_stage_coach_coordinates` (1331-1339). | as cited |
| `KingdomManager.SaveToJson()` | Plain `PerFieldToJson` (`KingdomManager.cs:399-402`): day 0, counters, `m_Gang`. The gang is a random released one picked in `InitPostLoad` (165-173). `m_GameOverReason` is `[DontSerializeToJson]` (66-67). | as cited |
| `KingdomDaySystem.SaveToJson()` on the dormant instance | Fine. The only saved field is `m_KingdomDayStateMachine` (`KingdomDaySystem.cs:12-21, 53-56`), whose two `KingdomDayState` fields are null because `PostCreate` was never called (`KingdomDayStateMachine.cs:9-24`). Null goes out as JSON null (see above) and comes back as null: `CustomEnumSerializer.FromJson` → `TryToCast(null)` → false (`JsonSerializationUtils.cs:538-545`; `Assets.Code.Utils/CustomEnum.cs:46-54`). | as cited |

`map.json` expectations: none for Kingdoms. `VerifyRunDirectory` explicitly exempts a missing `map.json` under the Kingdom
scheme (`Assets.Code.Serialization/SaveLoadMgr.cs:244`) and the loader calls `TryLoadMapFile(lastSavePath, isMapSaveRequired: false)` (`KingdomBhv.cs:546`).

### 1.5 What `GetGameModeTypeFromSave` returns for an Estate snapshot

`SaveUtils.GetGameModeTypeFromSave()` (275-303; `PlatformMgr.GetGameModeTypeFromSave()` just forwards, `PlatformMgr.cs:318-321`):
starts from `CurrentGameType.m_LoadDefaultGameModeType` (277) = `GameModeType.INN` for `KINGDOM`
(`Assets.Code.Game/GameType.cs:16`), then looks for `combat/`, `inn/`, `altar_of_hope/`, `embark/`, `hero_select/` and `map.json`.
An Estate snapshot has none of them → **`INN`**. Consequences:
- The vanilla continue path (`SaveLoadMgr.SetGameModeFromSave`, `SaveLoadMgr.cs:301-322`) would enter `INN`. Inside an Estate
  session the existing `GameModeMgr.SetMode` prefix would turn that into `ESTATE`; outside a session (Kingdoms menu) it would
  really be `INN` on a map whose current cell is null. The mod therefore drives the continue itself and hides the slot (3.3).
- `KingdomBhv.LoadSaveIfPossible` compares it with `EMBARK` only (581-584); no effect.

---

## 2. (b) The load path and what it needs

### 2.1 Vanilla sequence (Kingdoms → Continue)

`KingdomSaveSelectUIBhv.LoadSave` (`Assets.Code.Kingdom/KingdomSaveSelectUIBhv.cs:109-132`) →
`profile.SetCurrentKingdomSave(dir)` (117; `Assets.Code.Profile/ProfileInstance.cs:1606-1609`) →
`MainMenuControllerBhv.OnContinueGameType(GameType.KINGDOM, ...)` (`Assets.Code.UI.Managers/MainMenuControllerBhv.cs:293-315`):
`SetGameType` → `SaveLoadMgr.VerifySaveFiles()` → `ShowMissingDLCForProfileDialog` → if `SAVE_IS_VALID`, `BeginGameContinuation()`
→ `SetGameModeFromSave()` → the mode change makes `KingdomBhv.GameModeEnterAsyncPreStart` start
`StartKingdom(nextGameMode, null, null, isLoad, ...)` (`KingdomBhv.cs:289-318`) → `InitManagers` (769-780):
`InitManagersPreLoad()`, `VerifySaveFiles()`, `LoadSaveIfPossible()`, `InitManagersPostLoad()`.

The Estate calls `StartKingdom(EstateMode.Hub, null, null, isLoad: true, loadModeByCell: false)` directly at the main menu, as
the new-game path already does with `isLoad: false`. With `isLoad: true`, `SaveUtils.OnStartNewGame()` is skipped (437-440), so
the slot and the snapshot number carry on.

### 2.2 Which snapshot is picked

`SaveLoadMgr.VerifySaveFiles()` (129-154) → `SaveUtils.TrySetRunLoadPathToSelectedValidRunSaveFile` (2117-2125) →
`TrySetRunLoadPathToMostRecentValidRunSaveFile` (2127-2138; it first calls `ClearRunSaveLoadPath()`) →
`GetMostRecentValidRunSaveFile` (2153-2207): for `KINGDOM` with a non-empty, well-formed `CurrentKingdomSave` it only looks in
**that slot** (2162-2173); an empty result does *not* fall back to another slot, because the fallback tests `text == null` and
the result is `""` (2174, 2202-2205).

`GetMostRecentNodeRunSaveFile(slot)` (1250-1327) walks the `Save_*` folders newest first by name, takes the first that
`IsValidRunSubSaveDirectory` accepts (2284-2304, which runs `VerifyRunDirectory(doVersionCheck: false)`), and then **deletes
every snapshot with a higher number** (1312-1325). So a half-written newest snapshot is discarded and the one before it is
loaded: this is the game's own fallback and the Estate inherits it.

Trap: the validation result object is per game type and long-lived (`GetValidationResultForCurrentGameType`,
`SaveLoadMgr.cs:324-348`); when no snapshot is found its `saveDirectory` can still point at an earlier one, and
`VerifySaveFiles` would then report "valid" (137-141). Vanilla guards with `runLoadPath.IsNullOrEmpty()` (`KingdomBhv.cs:508`);
`EstatePersistence.Verified` checks the same plus "the load path is inside the Estate slot".

### 2.3 Validation scheme (`VerifyRunDirectory`, `SaveLoadMgr.cs:156-253`)

Scheme is `KINGDOM` (158). Per `RunSaveFile` (`Assets.Code.Utils.Serialization/RunSaveFile.cs`):

| File | Scheme | Estate snapshot | Result |
|---|---|---|---|
| `map.json` (85) | BOTH | absent | exempt for Kingdoms (244) |
| `combat/*`, `inn/inn.json`, `altar_of_hope/*`, `embark/*` (113-121) | BOTH | absent, and their folders too | only flagged when the folder exists (244) |
| `hero_select/hero_select.json` (143) | KINGDOM | absent, no folder | not flagged |
| `kingdom.json` (133, version 3 at 62) | KINGDOM | present | parsed (`TryLoadRunJsonWithValidation`, `PlatformMgr.cs:341-356`), version checked, and each section whose `m_ContainingFiles` has `KINGDOM` (198-223) must exist as an object with the right `Version` (202-230). `KingdomBhv.Save` writes all of them. |
| `actors.json` (127) | EXPEDITION | present | **not validated** for Kingdoms; a missing file is caught later in `LoadSaveIfPossible` (533-536 → `INVALIDATE_RUN`). |

Extra keys (`estate_mod`) are never looked at. An Estate snapshot therefore validates as `SAVE_IS_VALID`.
A game update that raises `KINGDOM_VERSION` or a section version makes old snapshots `INVALIDATE_RUN`
(`ValidateRunFileVersion`, `SaveUtils.cs:902-928`) unless the game ships a converter; `Continue()` then stops with `LastError`.

### 2.4 `KingdomBhv.LoadSaveIfPossible` (494-599) on an Estate snapshot

| Step | Lines | Estate |
|---|---|---|
| `CanUpdateRunSaveFolderAndNumber` | 518; `SaveUtils.cs:1191-1248` | one `Save_` in the path → `SaveNumber` parsed from the 4 digits (1227-1233) |
| actors | 522-536 → `LibraryActors.LoadFromJson` (`LibraryActors.cs:42-78`) | heroes rebuilt with their guids; an actor whose `ResourceActor` is missing (DLC removed) is skipped silently (67-68) |
| `kingdom.json` read | 537 | **the one `TryLoadRunJson(..., RunSaveFile.KINGDOM, ...)` call on PC**; the postfix takes `estate_mod` here |
| random | 544-545 → `RandomContainer.LoadFromJson` (`Assets.Code.Math/RandomContainer.cs:150-157`) | all saved streams restored |
| map file | 546 | no `map.json`, not required |
| inventory | 553-554; then `PlayerItemInventory.HandleEventGameTypeStarted` → `PostLoadFromJson()` instead of starting gold (`Assets.Code.Player/PlayerItemInventory.cs:124-134`) | restored |
| kingdom map | 563-564 → `KingdomMapManager.LoadFromJson` (1341-1353) | the debug map comes back |
| roster | 569-570 → `RosterManager.LoadFromJson` (`Assets.Code.Roster/RosterManager.cs:1140-1149`) | see 2.5 |
| kingdom day | 585-590 | `kingdom_day` is present, so the game itself creates the dormant `KingdomDaySystem` again |
| result | 598 | `SAVE_IS_VALID` → `EventKingdomStarted.Trigger(mode, kingdomLoaded: true, ...)` (`KingdomBhv.cs:450`) |

What `m_IsLoad == true` then does:
- `KingdomMapManager.HandleEventKingdomStarted` (153-187) iterates `m_KingdomMap.NumberOfCols`. **A kingdom saved without a
  map would throw a `NullReferenceException` here**; the debug map prevents it. It then takes `GetCurrentMapCell()` because
  `ESTATE` is not `m_isInBiome` (167-169); the current coordinates are `V2INT_INVALID` (`int.MinValue`,
  `Assets.Code.Math/Vector2Utils.cs:8`), `KingdomMap.TryGetCell` returns null (`KingdomMap.cs:215-227`), and the inn block is skipped (170).
- `ActorInstance.HandleEventKingdomStarted` re-links the three hero inventories (`Assets.Code.Actor/ActorInstance.cs:2125-2133`).
- `GameTypeMgr.HandleEventKingdomStartedLogic` → `OnGameTypeStarted(mode, isLoad, ...)` (`Assets.Code.Game/GameTypeMgr.cs:259-262, 347-367`);
  the combat branch (350-364) is for `COMBAT` only. `RosterManager.HandleEventGameTypeStarted` does **not** refill the roster on a
  load (`RosterManager.cs:172-179`); `KingdomMapManager.HandleEventGameTypeStarted` does not reset coordinates (237-250).
- `KingdomEventManager` recomputes event data for day 0 (`KingdomEventManager.cs:78-88`), `KingdomSiegeManager` and `QuestManager`
  do nothing on a load (`KingdomSiegeManager.cs:102-109`, `Assets.Code.Quest/QuestManager.cs:112-117`).

If the load **fails** inside `StartKingdom`, `kingdomLoaded` is false and the game silently builds a brand-new kingdom (new
debug map, refilled roster, starting gold). `Continue()` checks for that and ends the kingdom again (private
`KingdomBhv.EndKingdom`, 470-480) so the fresh state can never be saved over the real one.

### 2.5 Roster: `RemoveInvalidRosterEntries(isValidation: true)` (`RosterManager.cs:1262-1340`)

Runs inside `LoadFromJson` (1143), after the actors are loaded. An entry is dropped when
- its status needs an actor and the actor is not in the library (1270-1277); `DEAD` does not need one
  (`RosterStatusType.cs:15`), so dead heroes stay listed;
- its class is unknown or mis-defined (1278-1318);
- **another entry later in the list has the same class** (1319-1330). Duplicate heroes (design phase 2) will lose all but
  the last copy on every load until this is patched.

Dead heroes: `ActorInstance.GetShouldSave()` is false for a dead actor unless the roster replacement type is `RESPAWN`
(`ActorInstance.cs:911-936`), so their actor is not in `actors.json`; only the `DEAD` roster entry survives. The replacement
type comes from the default `KingdomDifficultyDefinition` (`KingdomDifficulty.cs:56-66, 161-169`) **[NV: CSV data]**. A
Graveyard needs its own record in `estate_mod`.

After the load each entry raises `EventRosterEntryStatusChanged(guid, LOAD → status)` (1146), which rebuilds the party data
containers (224-240). The `KingdomMapManager` listener ignores these (`KingdomMapManager.cs:320-334`: its two cell operations
need a `KINGDOM` status, which the Estate never uses).

### 2.6 `ProfileInstance.m_CurrentKingdomSave`

Set to the new slot by `OnKingdomSaveCreated` (`ProfileInstance.cs:1512-1526`) and by `SetCurrentKingdomSave`. It decides which
slot `GetMostRecentValidRunSaveFile` loads (2.2) and which slot `KingdomResultsScreenBhv` deletes (3.2). `Continue()` sets it to
the Estate slot explicitly, so a vanilla kingdom played in the same profile cannot be loaded by mistake.

### 2.7 First mode change after the load

`SetMode(EstateMode.Hub, isLoad: false)`, the same call the verified new-game path uses. For a mode that is neither `INN`,
`EMBARK`, `COMBAT`, `DRIVING` nor in-biome, none of the managers reads `isLoad`: `AffinityManager.OnGameModeEnterStart`
(`Assets.Code.Affinity/AffinityManager.cs:359-375`), `ActOutManager` (`Assets.Code.ActOut/ActOutManager.cs:140-153`), `CombatManager`
(`Assets.Code.Combat/CombatManager.cs:157-163`), `StageCoach` (`Assets.Code.Game.StageCoach/StageCoach.cs:118-127`), `RunValues`
(`Assets.Code.Run/RunValues.cs:192-202`), `BiomeManager` (`BiomeManager.cs:503-535`), `RosterManager.OnGameModeEnterPreStart`
(`RosterManager.cs:338-402`). Scene objects that implement `IGameModeEnterStart` were not all read **[NV]**.

Leaving `MAIN_MENU` runs `GameTypeMgr.OnGameModeExitComplete` (331-345): `PlayerInventory.RefreshSlots(checkToRemove: true)` and
`LibraryActors.RemoveUnsavedActors()`. Both also run on every mode change of a normal session; loaded heroes are "saved" actors.

---

## 3. (c) Housekeeping that could destroy or pollute the save

### 3.1 Snapshot trimming

`ShortenRunSaves(root)` (`SaveUtils.cs:1069-1160`), after every save:
- works on **one slot only: the last by name** (1080-1089);
- budget 4 saves / 200 files (1090-1091); the newest snapshot with an `inn/` folder is pinned (1103-1109) — the Estate has none;
- newest first, each plain snapshot costs one; the one that brings the budget to 0 is deleted, and everything older (1116-1157).

Net effect for the Estate: **the 3 newest snapshots are kept.** No patch needed.

Two side effects:
- **Numbers past 9999.** The folder number is formatted `D4` (102, 1945), parsed with `Substring(.., 4)` (1229-1230, 1266-1267)
  and ordered as text (1093, 1257). `Save_10000_Estate` sorts *below* `Save_9999_Estate`, so trimming would delete the
  newest snapshot after every save and the loader would read a wrong number. Vanilla never gets there; an Estate that saves
  at every room would. `EstatePersistence.Renumber` renames the remaining folders to `0001..` once the number reaches 9000
  (on continue, and before a save in a running session, where it also resets the private static `SaveUtils.SaveNumber`, line 126).
- If the Estate profile holds a slot with a later name (the player made a vanilla kingdom in it), the Estate slot is no longer
  the one being trimmed and grows by two files per save. Disk use only; not handled.

Boot-time trimming (`ShortenAllRunSavesForAllProfiles`, 1056-1067, from `LoadProfiles` 626-627) runs with `isMod: false` over the
regular profiles only.

### 3.2 Slots: naming, the 20-slot limit, deletion

- **Name.** `SaveLoadMgr.SetKingdomNameForNewKingdom("Estate")` (`SaveLoadMgr.cs:462-465`) is consumed when the first snapshot
  opens the slot: `EventRunSaveCreated` → `ProfileBhv.HandleEventRunSaveCreated` (`ProfileBhv.cs:274-287`) → records the owned
  DLC under `kingdom_<slot>` and calls `OnKingdomSaveCreated(slot, name)`, which does `m_KingdomSaveNameMappings.Add` and sets
  `m_CurrentKingdomSave` (`ProfileInstance.cs:1524-1525`). `Add` throws on a duplicate key; that needs two slots created in the
  same second, and it would surface in `SaveNow`'s `try/catch` because `UpdateSaveFolderAndNumber` is outside the game's own.
- **Limit.** `SaveUtils.IsMaxKingdomSaves()` (930-938) only gates the vanilla "new kingdom" button
  (`Assets.Code.Kingdom.UI/KingdomMainMenuUIBhv.cs:126-148`); the writer does not check it. The Estate slot counts as one of
  the 20 **in the Estate profile only**.
- **`DeleteOldRunSaves(KINGDOM)`** (940-1054) runs when a slot is created and on kingdom game over (`SaveLoadMgr.cs:102-105`).
  It keeps the 20 newest slot folders, zips the next ones and deletes the rest (947, 967-1019). The Estate slot is only at risk
  with 20 newer slots in the same profile, which the limit above prevents.
- **`KingdomResultsScreenBhv.Exit_Routine`** (`Assets.Code.Kingdom.UI/KingdomResultsScreenBhv.cs:119-134`) deletes
  `CurrentKingdomSave` after any kingdom game over. That screen is only reached through `KingdomPresentationBhv` on day state
  `END` with `IsGameOver()` (`Assets.Code.Kingdom.Presentation/KingdomPresentationBhv.cs:366-377`); the presentation lives in the
  kingdom map scene, which the Estate does not load **[NV: scene content]**. The other caller of
  `PlatformMgr.DeleteKingdomSave` is the delete button of a listed slot (`Assets.Code.ui/KingdomSaveItemBhv.cs:155-163`).
- **Game over.** `KingdomManager.SetGameOver` (380-387) is reached from `CheckGameOverDays/Inns/Roster` (342-373; called on day
  change 224-237, on a destroyed inn 244-251, and from `KingdomBhv.HandleEventBattleResult` 246, already skipped by
  `KingdomIgnoresEstateBattles`) **and from `KingdomManager.HandleEventBattleResult` (253-259) when the fight contained an actor
  class flagged `IsGangBoss`** (`Assets.Code.Combat/BattleResult.cs:132-135`). The day never changes in the Estate, but the
  design maps DD1 bosses onto Kingdoms gang bosses, so the last path is live. `EventKingdomGameOver` then writes a
  `KingdomGameOverEntry` into the profile (`ProfileBhv.cs:254-264`), runs `DeleteOldRunSaves`, and feeds narration, analytics and
  achievements. → patch `EstateNeverEndsAsKingdom` (prefix on `SetGameOver`, skips while a session is active).
- **`ProfileBhv.DeleteUnusedSaves`** (567-617; on every profile switch, `SaveUtils.cs:459`) keeps `mod_profile_<guid>_runs` and
  `_kingdoms` of every profile in the two lists (583-595). The Estate save is inside the Estate profile's `_kingdoms` folder, so
  it is safe as long as that profile exists; deleting the profile in the UI deletes the save with it (intended).

### 3.3 Vanilla Kingdoms UI, inside the Estate profile only

The Estate profile is an ordinary entry of the mod-profile list, so a player can select it by hand and open Kingdoms.
`KingdomSaveSelectUIBhv.GenerateKingdomSaveStats` (219-293) would list the Estate slot ("Estate", day 0, map `debug`), and
`LoadSave` would continue it as a kingdom in `INN` mode (1.5) while vanilla save points write `Inn`/`Combat` snapshots into the
slot. → patch `EstateSlotIsNotAKingdom`: a postfix removes every slot that contains a `Save_*_Estate` folder from the private
`m_KingdomSaveStats`; `RefreshSaves` only shows slots that have stats (164).

Left as is: with only an Estate slot the "Continue" button of that menu still shows
(`KingdomMainMenuUIBhv.SetMainMenuSelectablesEnabled`, 199-203, counts folders) and opens an empty list. Vanilla kingdoms created
in the Estate profile work normally and are ignored by the mod (its snapshots are told apart by the `_Estate` label and the
`estate_mod` key). Other profiles are never touched.

---

## 4. (d) Mid-fight behaviour and the save policy

### 4.1 Who writes saves

Every writer of run files goes through `SaveUtils.SaveCurrentGameMode`: `KingdomBhv.Save` is only called from `RunSyncOperations`
(702), and `PlatformMgr.SaveRunJson` only from `KingdomBhv.Save` (632), `LibraryActors.SaveToJson` (39), `RunBhv.Save`,
`CampaignBhv.Save` and `SaveUtils.TrySaveRunFile` (739) (repository-wide search). Callers of `SaveCurrentGameMode`:

| Caller | When | Reachable in the Estate |
|---|---|---|
| `Assets.Code.Combat/BattleStateMachine.cs:500` | leaving battle state `START` | **yes**, every fight |
| `BattleStateMachine.cs:550, 557` | after every turn / round | **yes** |
| `Assets.Code.Affinity/AffinityManager.cs:281` | a hero relationship is applied | possibly |
| `Assets.Code.Kingdom/KingdomDayStateMachine.cs:63`, `KingdomBhv.cs:365` | day states, entering `INN` | no (day dormant, `INN` redirected) |
| `InnStateMachine.cs:208`, `InnSystem.cs:449`, `EmbarkBhv.cs:192`, `TileNodeBhv.cs:321, 502`, `MapMgrBhv.cs:820`, `TriggerSaveBhv.cs:11`, altar screens, `CharacterSheetStatsUiBhv.cs:1015` (only in `HERO_SELECT`) | inn, road, altar | no |

Nothing bypasses it. Profile saves (`PlatformMgr.SaveCurrentProfile`) do happen during fights (unlocks etc.) but only write the
profile files, never a snapshot.

If the per-turn saves were let through, each would be a `Save_NNNN_Combat/Save_MMMM` snapshot with a `combat/` folder (1404-1412,
1946-1949); the newest snapshot would then say `COMBAT`, and resuming needs `SaveUtils.LoadCombatScenarioData()` plus the mod's
dungeon rebuilt around the fight.

### 4.2 Policy for the first version (confirmed)

- The `SaveCurrentGameMode` prefix lets a call through only when `EstatePersistence.Saving` is set. `SaveNow` sets it around its
  own call, so all vanilla save points stay blocked, including the two that can fire in the Estate.
- `SaveNow(reason)` works only when `EstateSession.InHub` (hub mode, no mode change running). Call it after hamlet actions, on
  room transitions and **when the hub is re-entered after a fight**.
- Quitting or crashing mid-fight resumes from the last hub snapshot; the fight is replayed from before it started. Known
  consequence: closing the game during a losing fight undoes it. Acceptable for now; the later fix is a real combat sub-save.
- One correction to the default: a save requested while the previous one is still being finished must not be handed to the game,
  which would queue it and write it some frames later in whatever mode is current (1.1 step 2). `SaveNow` keeps it and writes
  it as soon as the hub is idle (`Pending`); it is dropped if the hub is left first, in which case a snapshot at most a few
  frames older already exists.
- A save costs one synchronous serialization of all heroes and managers on the main thread plus five small profile files; use it
  at pauses, not per corridor step.

---

## 5. (e) Steam achievements

Achievements are per Steam account. Flow: gameplay events → `AchievementsMgr.Process*` → `QueueAchievementUnlock(defId)`
(`Assets.Code.Achievements/AchievementsMgr.cs:1809-1834`), which (1) records the achievement on the current profile, (2) raises
`EventAchievementUnlocked` (toast) and (3) appends to `m_storefrontUnlockQueue`; `LateUpdate` (1947-1966) pops that queue and
calls `PlatformMgr.UnlockAchievement` → `SteamUserStats.SetAchievement` (`Assets.Code.Platform/SteamPlatformMgr.cs:169-187`).

Cleanest switch: **prefix `AchievementsMgr.QueueAchievementUnlock`, skip while `EstateSession.Active`, `Starting`, or the Estate
profile is the selected one** (patch `NoAchievementsFromEstate`). All four processing paths end in that one call (1858, 1879, 1942;
the debug widget is the only other caller). Why this and not the alternatives:
- Blocking `SteamPlatformMgr.UnlockAchievement` alone is too late: the unlock is already on the profile and in the queue, the
  queue is throttled, and an entry can be popped after the session ended. Profile-recorded unlocks are also re-queued for the
  storefront later (`ProcessLocallyUnlockedAchievementsForStorefront`, 1766-1781, called at 251/262).
- The editor pref `DISABLE_ACHIEVEMENTS` (`TextBasedEditorPrefsBaseType.cs:157`; checked at 1785, 1811, 1838, 1866, 1932) is global
  state that a crash inside the Estate would leave switched on **[NV: whether prefs persist to disk]**.
- "Estate profile selected" matters because kill counters live on the profile (`IncrementActorTagDeathCount`, 312) and are
  re-evaluated whenever a game type starts (`HandleEventGameTypeStarted`, 1138-1151).

An achievement earned in vanilla play and still waiting in the queue is not affected.

---

## 6. Implementation

### 6.1 `EstatePersistence` (API)

| Member | Use |
|---|---|
| `bool HasSave()` | True when the Estate profile's kingdom folder has a slot with a `Save_*_Estate` snapshot that contains `actors.json` and a `kingdom.json` with the `estate_mod` key. Looks up the folder from `Plugin.ProfileGuid`, so it works at the main menu with any profile selected. The key is the last one in the file, so its presence also means the file was written to the end. |
| `void StartNew()` | New-game path: clears the loaded section and sets the slot name. |
| `IEnumerator Continue()` | Load path, see 6.2. Sets `Loaded` / `LastError`. |
| `void SaveNow(string reason)` | Hub only. Writes `Save_NNNN_Estate`; logs success or the reason for failure. |
| `bool Saving`, `bool Pending` | For the `SaveCurrentGameMode` prefix; a save is waiting. |
| `Func<JObject> SerializeEstate`, `Action<JObject> DeserializeEstate` | The mod section. With no serializer the section that was loaded is written back unchanged, so nothing is lost while the hooks are not wired. `"format": 1` is added when the serializer did not set a format itself. An exception from the serializer aborts the save on purpose (no `kingdom.json` → the snapshot is skipped and removed by the next load). |
| `bool DeleteSave()` | Start over: removes the Estate slot through `PlatformMgr.DeleteKingdomSave`. Main menu, Estate profile selected. |
| `bool BlocksAchievements`, `bool IsEstateSlot(string)`, `object Describe()` | Used by the patches / dev bridge. |

### 6.2 Continue recipe (what `EstateSession.Enter()` does when `HasSave()`)

1. `EstateProfile.Activate()`, `SetGameType(KINGDOM)`, `PostMainMenuSystemsInstaller.TryInstall()` — unchanged.
2. `Active = true`, then `yield return EstatePersistence.Continue()`:
   1. find the Estate slot; renumber its folders if the number is ≥ 9000;
   2. `profile.SetCurrentKingdomSave(slot)`, `SaveLoadMgr.VerifySaveFiles()`; stop unless `SAVE_IS_VALID` and the load path is in the slot;
   3. `MainMenuControllerBhv.ShowMissingDLCForProfileDialog(GameType.KINGDOM)`; stop if a recorded DLC is missing (otherwise its heroes would vanish silently, 2.4);
   4. `KingdomBhv.StartKingdom(EstateMode.Hub, null, null, isLoad: true, loadModeByCell: false)`, stepped by hand so an
      exception in the game's load code is caught and reported instead of killing `Enter()` with `Starting` stuck;
   5. re-check validity and that `estate_mod` was read; call `DeserializeEstate`;
   6. on any failure after step 4 the kingdom is ended again (private `KingdomBhv.EndKingdom` by reflection; if that itself
      throws, the tail of `EndKingdom` is replayed: actors removed, state reset, `EventKingdomEnded`).
   Steps 1-3 leave nothing to undo. Errors are not logged by `Continue()`; the caller logs `LastError`.
3. `!kingdom.IsKingdomStarted || (resume && !EstatePersistence.Loaded)` → the existing failure branch.
4. Dormant day: unchanged line (after a load the game has already recreated it).
5. **Skip `EnsureStartingRoster()`** on a continue (it would promote heroes into the party, including `DEAD` entries).
6. Scene + `SetMode(EstateMode.Hub, isLoad: false)` — unchanged.

### 6.3 Patches (`PersistencePatches.cs`)

| Class | Target | Why |
|---|---|---|
| `EstateSectionIsSaved` | prefix `PlatformMgr.SaveRunJson` | adds `estate_mod` when the file is `RunSaveFile.KINGDOM` and a session is active |
| `EstateSectionIsLoaded` | postfix `PlatformMgr.TryLoadRunJson` | takes `estate_mod` from the one read of `kingdom.json`; `DeserializeEstate` runs later, in `Continue()` |
| `EstateNeverEndsAsKingdom` | prefix `KingdomManager.SetGameOver` (private) | 3.2: gang-boss victory and the three loss checks |
| `EstateSlotIsNotAKingdom` | postfix `KingdomSaveSelectUIBhv.GenerateKingdomSaveStats` | 3.3 |
| `NoAchievementsFromEstate` | prefix `AchievementsMgr.QueueAchievementUnlock` | 5 |

Not patched, with reasons: trimming (3.1), `DeleteKingdomSave` (unreachable once the two patches above are in, 3.2),
`DeleteUnusedSaves` (3.2).

### 6.4 Edits in other files

`Dd2\SessionPatches.cs` — replace the body of `NoVanillaSnapshotsInEstate`:

```csharp
    /// <summary>Vanilla save points assume a road, an inn or a fight to resume in; only the mod's own hub snapshots are written.</summary>
    [HarmonyPatch(typeof(SaveUtils), nameof(SaveUtils.SaveCurrentGameMode))]
    internal static class NoVanillaSnapshotsInEstate
    {
        private static bool Prefix() => !EstateSession.Active || EstatePersistence.Saving;
    }
```

`Dd2\EstateSession.cs`, in `Enter()` — replace from `SetKingdomNameForNewKingdom` to `EnsureStartingRoster()`:

```csharp
                var resume = EstatePersistence.HasSave();
                var kingdom = SingletonMonoBehaviour<KingdomBhv>.Instance;
                Active = true;
                if (resume) yield return EstatePersistence.Continue();
                else
                {
                    EstatePersistence.StartNew();
                    yield return kingdom.StartKingdom(EstateMode.Hub, null, null, isLoad: false, loadModeByCell: false);
                }
                if (!kingdom.IsKingdomStarted || (resume && !EstatePersistence.Loaded))
                {
                    Plugin.Log.LogError("Estate: " + (EstatePersistence.LastError ?? "the game did not start its Kingdom-type session"));
                    Active = false;
                    EstateProfile.Deactivate();
                    yield break;
                }

                // (dormant-day block unchanged)

                if (!resume)
                {
                    EnsureStartingRoster();
                    EstateState.StartNew();
                }
```

and in `ExitToMenu()`, before `SetMode(MAIN_MENU)`: `if (InHub) EstatePersistence.SaveNow("exit");`

`Plugin.cs`, in `Awake()` after `EstateMode.Register()` — wire the section to `Estate\EstateState.cs` (it already has
`ToJson()` with its own `"format"` and `LoadFrom(JObject)`):

```csharp
            EstatePersistence.SerializeEstate = () => DD2Estate.Estate.EstateState.Current.ToJson();
            EstatePersistence.DeserializeEstate = DD2Estate.Estate.EstateState.LoadFrom;
```

Save points for the other modules: `EstatePersistence.SaveNow("...")` after a hamlet action, on a dungeon room transition, and
in `Tick()` where the dungeon view resumes after a fight.

`Dev\DevCommands.cs` — test commands:

```csharp
            AgentBridge.Register("estate.saveinfo", o => EstatePersistence.Describe());
            AgentBridge.Register("estate.save", o =>
            {
                EstatePersistence.SaveNow((string)o["reason"] ?? "dev");
                return EstatePersistence.Describe();
            });
            // Only until the real hooks are wired in Plugin.Awake (this replaces them for the session).
            AgentBridge.Register("estate.savehooks", o =>
            {
                var tag = (string)o["tag"] ?? "test";
                EstatePersistence.SerializeEstate = () => new Newtonsoft.Json.Linq.JObject { ["tag"] = tag };
                EstatePersistence.DeserializeEstate = s => Plugin.Log.LogInfo("estate section loaded: " + s.ToString(Newtonsoft.Json.Formatting.None));
                return "hooks set, tag " + tag;
            });
            AgentBridge.Register("estate.wipe", o =>
            {
                if (EstateSession.Active || EstateSession.Starting) return "leave the Estate first";
                if (!EstateProfile.Activate()) return "no Estate profile";
                try { return EstatePersistence.DeleteSave() ? "deleted" : "nothing to delete"; }
                finally { EstateProfile.Deactivate(); }
            });
```

---

## 7. Test plan (dev bridge)

`python tools/bridge.py run <name> [key=value ...]`; "log" = `BepInEx\LogOutput.log`.

| # | Do | Expect |
|---|---|---|
| 1 | at the main menu: `estate.wipe`, `estate.saveinfo` | `hasSave: false` |
| 2 | `estate.enter`, `estate.state` | new game, 15 heroes, 4 in party; note guids / hp / stress |
| 3 | `estate.savehooks tag=A`, `estate.save reason=t1` | log `Estate saved (t1): Save_0001_Estate`; `saveinfo`: `hasSave: true`, `saveNumber: 1`. On disk (look by hand): `mod_profile_<n>_kingdoms\<stamp>\Save_0001_Estate\` holds only `actors.json` and `kingdom.json`, and `kingdom.json` ends with `"estate_mod": {"tag": "A", "format": 1}` |
| 4 | `estate.battle config=lost_battalion_mash_210 arena=combat_arena_forest_faction`; during the fight `estate.save` | log `Estate save skipped (dev): not in the hub mode`; `saveNumber` still 1; no `Save_*_Combat` folder appears after several turns |
| 5 | force the win, take the loot, wait for the hub; `estate.state`; `estate.save reason=t2` | hp/stress/items changed; `Save_0002_Estate` |
| 6 | `estate.save` four more times, a second apart | `saveNumber: 6`; only the three newest folders remain |
| 7 | `estate.save` twice as fast as the bridge allows | no error in the log; if the second landed within the frame or two the first needs, it reports `pending` and its snapshot appears a moment later (timing-dependent, may not trigger) |
| 8 | `estate.exit`; at the menu `estate.saveinfo` | `hasSave: true` with the player's own profile selected again |
| 9 | `estate.savehooks tag=B`, `estate.enter` | log `Estate continued from <slot>/Save_0006_Estate` and `estate section loaded: {"tag":"A","format":1}`; `estate.state` equals step 5 (guids, hp, stress, party); the hamlet opens |
| 10 | `estate.save reason=t3`, `estate.saveinfo` | `saveNumber: 7` in the **same** slot (no second slot folder) |
| 11 | start a battle, kill the game process mid-fight, restart, `estate.enter` | continues from the snapshot of step 10; the player's profile selection was recovered first |
| 12 | select the Estate mod profile by hand, open Kingdoms → Continue | the list does not show "Estate"; switch back to your profile |
| 13 | search `Player.log` for `AchievementsMgr queued` after Estate fights | no entries from the session |
| 14 | optional: make `"Version": 3` → `2` at the top of the newest `kingdom.json`, `estate.enter` | the older snapshot is loaded and the edited folder is gone; with all snapshots edited: no session, `lastError` names `INVALIDATE_RUN` |

---

## 8. Risks not ruled out statically

1. **None of this ran in game.** In particular: loading the debug `KingdomMap` back from JSON (`KingdomMap.FromJson` →
   `ValidateKingdomMap`, cells' `PostLoadFromJson`), and the full `LoadSaveIfPossible` on a kingdom that never visited an inn.
2. `MainMenuControllerBhv.ShowMissingDLCForProfileDialog` is called outside its usual click handler; it only reads the
   validation list and the profile, but its dialog was not exercised from a coroutine.
3. Ending the kingdom by reflection after a failed load (`EndKingdom` at the main menu, without a mode change) mirrors what the
   end-point mode does (`KingdomBhv.cs:338-341`) but is an unusual moment for `EventKingdomEnded`. If the load threw before
   the map existed, `KingdomMapManager.Destroy()` throws too (`KingdomMapManager.cs:120-125`) and the managers after it keep
   their event listeners; the log then says to restart the game. A failed continue does not delete anything: the player is
   stuck on "continue fails" until the cause is fixed or the slot is removed (`DeleteSave`), so the UI needs a "start over".
4. `Renumber` renames folders while the game's trimming task may still be deleting older ones; a clash throws, is logged, and
   the renumbering is retried at the next save. It depends on the private static `SaveUtils.SaveNumber`.
5. `EventSaveCurrentGameModeCompleted` and the trimming events are raised from a worker thread (425-432, 1113-1158); mod code
   must not listen to them for Unity work. `EventSaveCurrentGameModeStarted` drives the game's autosave throbber
   (`Assets.Code.Game/GameUIBhv.cs:487-491`); whether that UI is present and visible in the hub mode is **[NV]**.
6. A game update that bumps `KINGDOM_VERSION` or a section version invalidates existing Estate saves (2.3); there is no
   migration.
7. Heroes with duplicate classes do not survive a load (2.5).
8. Analytics: a new Estate sends a `KingdomStart` event with map `debug` (`Assets.Code.Analytics/AnalyticsBhv.cs:681-691`); not touched here.
9. The Harmony prefix on `SaveCurrentGameMode` must stay effective for call sites compiled after patching (the method is tiny
   and inlinable); the blanket version is verified in game, the flag version behaves the same for vanilla callers **[NV]**.
