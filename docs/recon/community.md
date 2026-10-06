# Darkest Dungeon II: community and official code-modding recon

Compiled 2026-10-04 by a web-research pass. Game: Steam app 1940340, current public build line 2.04.x (newest build in the Steam news feed is the 2.04.85095 hotfix, 2026-04-28; nothing newer as of this date).

How to read this file:
- **[confirmed]** = stated by the cited source. **[inference]** = my reading of the evidence. **[unconfirmed]** = could not be verified.
- Some web pages were read through a summarising fetch tool; where a claim matters I re-read the raw source (Steam news JSON, the official Google Doc, GitHub raw files) and say so. The Steam news JSON, the official guide and the Darkest2in1/DD2SteamMP/Plugin-DD2 sources below were read raw.
- Nothing was downloaded or executed. No game files were touched.

---

## 0. Headline findings (read this first)

1. **A near-identical project already exists and is two days old: `Stephane-Dedu/Darkest2in1` ("DarkestDungeon3").** It is a BepInEx 5 + Harmony plugin that adds a "The Hamlet" main-menu entry and plays DD1's hamlet, quest board, room-and-corridor dungeons, curios, camping and town events on top of DD2 heroes and DD2 combat. First commit 2026-10-02 17:26 UTC, last 2026-10-03 20:00 UTC, 73 commits. **No license file** (GitHub reports "None"), so reading it is fine but copying code is not licensed. https://github.com/Stephane-Dedu/Darkest2in1
2. **BepInEx 5.4.23.5 (Mono, win x64) is what every active 2026 DD2 plugin uses.** It is also the first BepInEx 5 build with a fix for Unity 2022.3.62f3 (see section 1).
3. **Red Hook's official modding system is data-only** (CSV tables + Addressables asset bundles built in a Unity project called "Darkside", published to Steam Workshop). The official guide lists "Scripting, library loading, code injection" and "Custom UI elements and UI changes" under "Not currently possible". No official statement for or against BepInEx was found.
4. **DD2 ships a developer layer you can drive from code**: `editor_prefs.txt` / `-allowEditorPrefs`, a built-in combat "battle test" flow (`battle_test_*` keys), an actor editor (Alt+J) and combat debug (Ctrl+J). Plugin-DD2 and Darkest2in1 both flip these from code.
5. **No anti-tamper or anti-cheat found**, but "no evidence" is not proof. DD2 does upload a crash report when a game callback throws (Darkest2in1 hit this).

---

## 1. Does BepInEx work with DD2 today?

**Yes. [confirmed]** Four independent 2026 projects load under BepInEx 5.x on the Mono build:

| Evidence | Detail | Source |
|---|---|---|
| DD2 Damage Meter | "tested with BepInEx 5.4.23.5", build target net48; Nexus page last updated 2026-08-26 | https://www.nexusmods.com/darkestdungeon2/mods/281 and https://github.com/superexboom/DD2DamageMeter (README; I infer these are the same project, the README text matches the Nexus text, authorship handles differ) |
| DD2 Item Spawner Plus | "tested with BepInEx 5.4.23.5" | https://github.com/superexboom/DD2ItemSpawnerPlus |
| Darkest2in1 | MODLOG: BepInEx 5.4.23.5 installed 2026-10-02 into the Epic install, `winhttp.dll` doorstop proxy; csproj targets net472 | https://raw.githubusercontent.com/Stephane-Dedu/Darkest2in1/main/MODLOG.md |
| DD2SteamMP | "BepInEx 5.x ... Unity/Mono BepInEx build, not IL2CPP"; last push 2026-09-19 | https://github.com/superexboom/DD2SteamMP |

**Which build.**
- BepInEx 5.4.23.5, released 2026-02-08, asset `BepInEx_win_x64_5.4.23.5.zip`. Release notes list: fix for Unity 2022.3.62f3 not having `get_graphicsDeviceID`, Doorstop upgraded to 4.5.0, log-writer fix for Unity 6. It also says BepInEx 5 is in LTS mode. https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5 (read via the GitHub API) [confirmed]
- The Unity fix is PR #1235 (merged 2026-01-27 into `v5-lts`). It changes `Chainloader.IsHeadless` so it no longer calls `SystemInfo.graphicsDeviceID` directly; it looks the getter up by reflection and falls back to `false`. https://github.com/BepInEx/BepInEx/pull/1235 [confirmed]
- **[inference]** Earlier 5.4.23.x builds (5.4.23.4, 2025-09-25 and older) likely fail at chainloader start on a player that lacks that getter. Your player is reported as 2022.3.62f2; whether f2 lacks the getter is **[unconfirmed]**. Use 5.4.23.5 either way.
- No DD2 project using BepInEx 6 (pre-release) was found. [confirmed absence in what I searched, GitHub + Nexus]
- Plugins compile as net472 or net48 against game DLLs plus `BepInEx.dll` and `0Harmony.dll` from `BepInEx/core`. **All game logic is in `IronCrown.dll`; `Assembly-CSharp.dll` is 26 KB** (3041 types in IronCrown). Darkest2in1 csproj references only IronCrown plus Unity modules, Newtonsoft.Json, Unity.Addressables, Unity.InputSystem, TextMeshPro, FMODUnity. https://raw.githubusercontent.com/Stephane-Dedu/Darkest2in1/main/MODLOG.md and https://raw.githubusercontent.com/Stephane-Dedu/Darkest2in1/main/src/DarkestDungeon3/DarkestDungeon3.csproj [confirmed]

**Unity version discrepancy. [unconfirmed which is right for your Steam 2.04.x]**
- Community sources say **2022.3.16f1**: the Darkside project requirement on https://www.nexusmods.com/darkestdungeon2/mods/282 (posted 2026-09-21); the official guide ("Unity Version: 2022.3.16f", updated 2025-06-13, link in section 3); Darkest2in1 MODLOG recon on the Epic build (2026-10-02); the Metamorph hero repo https://github.com/dnauu-bmsotc/DD2-Hero-Mod-Metamorph.
- You report 2022.3.62f2. I found no source that says 2022.3.62 for DD2. Read it from the player directly if it matters (e.g. `UnityPlayer.dll` file version).
- The old Steam guide says 2020.3.7f1 (stale: guide created 2023-06-07, final update 2024-07-19, marked deprecated 2024-08-19): https://steamcommunity.com/sharedfiles/filedetails/?id=2985496131

**Doorstop / config tweaks.**
- **No DD2-specific tweak is documented anywhere I looked. [confirmed absence]** Typical READMEs just say "install BepInEx 5 for the normal Unity/Mono build, extract into the game folder". https://github.com/superexboom/DD2DamageMeter
- Doorstop 4.x key naming is `target_assembly=` in `doorstop_config.ini`. DD2SteamMP's README tells users to set `target_assembly=DD2SteamMP\DD2SteamMultiplayerDoorstop.dll` and keep Doorstop enabled; its entry point then calls `Doorstop.Entrypoint.Start()` in `BepInEx/core/BepInEx.Preloader.dll`. https://raw.githubusercontent.com/superexboom/DD2SteamMP/main/README.md , https://raw.githubusercontent.com/superexboom/DD2SteamMP/main/DD2SteamMultiplayerDoorstop/BepInExChainloader.cs [confirmed]
- `HideManagerGameObject`: BepInEx's own troubleshooting page says to try `HideManagerGameObject = true` when "plugins are loaded without errors but do not work afterwards" (manager object destroyed by the game). https://docs.bepinex.dev/articles/user_guide/troubleshooting.html [confirmed]. **For DD2 nobody documents needing it**; instead two plugins sidestep it in code: Darkest2in1 makes its own `DontDestroyOnLoad` + `HideFlags.HideAndDontSave` host object (comment: BepInEx's manager can be destroyed by scene loads in some games), and DD2 Damage Meter does `DontDestroyOnLoad(gameObject); gameObject.hideFlags = HideAndDontSave` in `Awake`. https://raw.githubusercontent.com/Stephane-Dedu/Darkest2in1/main/src/DarkestDungeon3/Plugin.cs , https://raw.githubusercontent.com/superexboom/DD2DamageMeter/main/Plugin.cs [confirmed]. Whether DD2 needs `HideManagerGameObject = true` or a changed `[Preloader.Entrypoint]` is **[unconfirmed]**; default settings are what everyone seems to run.

**Known breakages after game updates.**
- Binarizer's plugin broke on official updates: Nexus posts from 2023-12 to 2024-04 ask for an update; on 2024-05-03 tbonex28b writes that "no one knows what broke"; a 2025-09-09 post reports an overlay error. A community "reborn" build (2023-10-16) removed some code to get it running. https://www.nexusmods.com/darkestdungeon2/mods/207?tab=posts [confirmed]
- Enable Cheats (data mod) says it stops working with every Red Hook update until updated. https://www.nexusmods.com/darkestdungeon2/mods/56 [confirmed]
- DD2SteamMP and the autobattler both warn that game updates break hooks. https://github.com/superexboom/DD2SteamMP , https://github.com/syyzit/dd2-autobattler [confirmed]
- Darkest2in1 applies each Harmony patch class in its own try/catch because `PatchAll` stops at the first failure; one class failing is logged and skipped. Worth copying as a pattern. https://raw.githubusercontent.com/Stephane-Dedu/Darkest2in1/main/src/DarkestDungeon3/Plugin.cs [confirmed]

**Build cadence (so you know how often to expect breakage), from the Steam news JSON** (`https://api.steampowered.com/ISteamNews/GetNewsForApp/v0002/?appid=1940340&count=500&maxlength=0&format=json`): 1.06.66916 "Darkside of the Mountain" 2024-08-22; 2.00.73822 Kingdoms 2025-02-12 (launch 2025-01-27); 2.01.77075 Secrets of the Coven 2025-04-21; 2.02.79016 Steadfast Steward retail 2025-07-17; 2.03.79750 Curse of the Court 2025-08-28; 2.04.80601 Steadfast Steward Pt 2 2025-10-29; 2.04.83880/83892 2026-03-16/18; 2.04.84350 2026-04-06; 2.04.85095 2026-04-28. [confirmed] (Note: a summarising fetch of the same feed gave wrong years; the raw JSON dates above are correct, e.g. the 3rd-anniversary post is dated 2026-05-08.)

---

## 2. Existing DD2 code mods / plugins

### 2.1 Table

| Project | Author / host | What it does | Source open? | Last activity |
|---|---|---|---|---|
| **Darkest2in1 (DarkestDungeon3)** | Stephane-Dedu, GitHub | DD1 meta-game inside DD2: Hamlet UI, quests, provisions, corridor/room dungeons, curios, camping, loot, DD1 monsters/audio via DD1 install | Yes, **no license** | 2026-10-03 |
| **DD2SteamMP** (+ `DD2DebugDemoCore`) | superexboom, GitHub, MIT | Steam-lobby multiplayer; custom PvP / "debug-demo" battle setup (hero/monster presets, waves, torch, arena modifiers); Doorstop host that chainloads BepInEx | Yes, MIT | 2026-09-19 |
| **Plugin-DD2 (Binarizer's Lib)** and **"mod plugin reborn"** | Binarizer GitHub (MIT); Nexus 207 by HeartySnow | Incremental mod loader (CSV overrides, localization, external Addressables, skin/actor/skill add), plus QoL (skip skill unlock, quick save F3, quirk locks, <4 heroes, region boss bias) | Yes, MIT | repo 2025-01-05; last commit 2023-08-19; Nexus 2023-10-16 |
| **DD2 Damage Meter** (+ AdvancedStats) | Ahtsm (Nexus) / superexboom (GitHub) | IMGUI overlay, battle logs, run stats; exposes a small companion API used by DD2SteamMP | Yes, MIT | 2026-08-26 |
| **DD2 Cheat Panel, Debug Actor Plus, Item Spawner Plus, Seed Locker, Discard Loot Blocker** | superexboom, GitHub, MIT | In-game panels (Insert / Alt+J / F12 / Ctrl+F8): cheats, actor editor, item spawner, new-run seed lock | Yes | 2026-06 |
| **dd2-autobattler** | syyzit, GitHub, MIT | Plays DD2 combat by scoring legal skills each turn; AUTO and SHADOW overlay modes; JSONL logs | Yes | 2026-08-27 |
| **Harkest Dungeon** | amerikrainian, GitHub | Screen-reader accessibility (speech output for menus/tooltips); ships a vendored BepInEx 5 and an installer exe | Yes, no license | 2026-09-08 |
| **Custom Hero Texture Loader** | ARogueCop, Nexus 23 | Replaces hero textures from a `HeroSkins` folder; needs BepInEx 5 + XUnity.AutoTranslator | DLL only | 2021-11-06 (likely stale) |
| "Speedwagon" | named in the official guide as another Harmony-based mod | not found | unconfirmed | unconfirmed |

Sources: https://github.com/Stephane-Dedu/Darkest2in1 ; https://github.com/superexboom/DD2SteamMP ; https://github.com/Binarizer/Plugin-DD2 ; https://www.nexusmods.com/darkestdungeon2/mods/207 ; https://www.nexusmods.com/darkestdungeon2/mods/281 ; https://github.com/superexboom/DD2CheatPanel ; https://github.com/superexboom/DD2DebugActorPlus ; https://github.com/superexboom/DD2ItemSpawnerPlus ; https://github.com/superexboom/DD2SeedLocker ; https://github.com/syyzit/dd2-autobattler ; https://github.com/amerikrainian/harkest-dungeon ; https://www.nexusmods.com/darkestdungeon2/mods/23 . Repo metadata (created/pushed/license) from the GitHub REST API. [confirmed]

**How common are code mods?** I fetched all 282 Nexus DD2 mod pages (ids 1-282); only three mention BepInEx (ids 23, 207, 281). The rest are data/asset mods, so the BepInEx scene is small and lives mostly on GitHub. [confirmed for ids 1-282 as of 2026-10-04; pages hidden by adult-content filtering would not show]

### 2.2 Things that map to what you want to build

**Adds UI screens.**
- Darkest2in1 uses an IMGUI layer on a 1920x1080 virtual canvas for Hamlet / Embark / Camp / Crawl screens plus a uGUI "input blocker" so DD2's own UI does not react underneath; it injects a "The Hamlet" button on the main menu. Files: `Ui/HamletUi.cs` (72 KB), `Ui/CrawlUi.cs`, `Ui/EmbarkUi.cs`, `Ui/UiRoot.cs`. https://raw.githubusercontent.com/Stephane-Dedu/Darkest2in1/main/MODLOG.md [confirmed]
- DD2 Damage Meter: draggable, resizable IMGUI windows. https://github.com/superexboom/DD2DamageMeter [confirmed]
- DD2SteamMP: 15+ "sync adapters" for existing DD2 screens (main menu, embark, inn, stagecoach, store, altar, route choice, etc.), a `UiInputBlocker` and `HostVoteUiCoordinator`. File list: https://api.github.com/repos/superexboom/DD2SteamMP/git/trees/main?recursive=1 [confirmed]
- Cheat Panel / Debug Actor Plus / Item Spawner Plus: IMGUI-style panels with hotkeys (see their READMEs).

**Starts custom battles.** This is solved in two ways:
- *Darkest2in1 (code path).* From its MODLOG: build `CombatScenarioData(battleConfigId, arenaSceneName, CombatSource.DUNGEON, partyActorGuids)`, then `GameModeMgr.SetMode(GameModeType.COMBAT, isLoad:false)` and `GameTypeMgr.SetCombatScenario(s, isLoad:true)`. Register your own `BattleConfigurationDefinition`; arenas are named like `combat_arena_<region>_dungeon_interior/exterior`. Heroes come from `LibraryActors.CreateActor(classId)` into a `RosterManager` party. To use DD2's torch/stress/items it hosts a real DD2 run (`GameTypeMgr.SetGameType(GameType.EXPEDITION)`, `GameModeMgr.SetMode(DRIVING)`) with developer prefs `MAP_GENERATION_SKIP_VALLEY`, `RUN_TEST_SKIP_PROLOGUE`, `DRIVING_DISABLE_COMBATS`, `DISABLE_INTRO_CINEMATIC`, `DISABLE_TUTORIALS` set via `TextBasedEditorPrefsBaseType.<KEY>.SetValue(true)`, and ends it with `GameOverReason.ABANDON` then main menu. https://raw.githubusercontent.com/Stephane-Dedu/Darkest2in1/main/MODLOG.md , https://raw.githubusercontent.com/Stephane-Dedu/Darkest2in1/main/src/DarkestDungeon3/Dd2/Dd2Run.cs [confirmed]
- *DD2SteamMP / DD2DebugDemoCore (editor_prefs path).* DD2 has a native "battle test" configured by editor-pref keys: `battle_test_battle_configuration`, `battle_test_combat_arena`, `battle_test_combat_source`, `battle_test_controls`, `battle_test_team_0` / `battle_test_team_1` (+ `_controller`), `hero_test_paths`, `hero_test_start_skills`, `hero_test_start_combat_item`, `hero_test_start_trinkets`, `hero_quirks_per_hero`, `hero_test_start_effect`, `run_test_boss_modifier`. The mod adds its own `dd2demo_*` keys on top. https://raw.githubusercontent.com/superexboom/DD2SteamMP/main/DD2DebugDemoCore/Model/DebugCombatDraftImporter.cs [confirmed]
- The Darkest2in1 MODLOG notes that run-scoped state exists only inside a run (e.g. `Singleton<ResourceDatabaseActors>` only exists mid-run; outside runs load actor assets via Addressables by DLC label). DD2SteamMP's README describes snapshotting and restoring the active Run's `RandomContainer` around an arena launch, so a custom fight started from inside a run needs its RNG state protected. https://raw.githubusercontent.com/Stephane-Dedu/Darkest2in1/main/MODLOG.md , https://raw.githubusercontent.com/superexboom/DD2SteamMP/main/README.md [confirmed]

**Alters the map/road.**
- Darkest2in1 skips the valley and prologue and disables road fights while hosting (above); suppresses a road-results NullReferenceException with a Harmony prefix (`RoadResultsCopyStaysQuiet`, skips `OnGameModeEnterComplete` for the road scene) because DD2 uploaded a crash report on each fight; patches `CombatPresentationBhv.SetNextGameMode` / `CheckForEndOfCombat` and `LootManager.ShowLoot` to return to its own layer after fights. [confirmed, MODLOG]
- Plugin-DD2 (2023 class names) hooks `RunManager.GetRunNumberOfTypicalBiomes`, `InnPresentationBhv.GetCanEmbarkNextBiome`, `RunBhv.GetEndBiomeRequiredStageCoachItemSlotType`, `CampaignBhv.Initialize`. https://raw.githubusercontent.com/Binarizer/Plugin-DD2/main/DD2_Plugin_Binarizer/Hooks/HookGenerals.cs [confirmed; names may have changed since 2023]
- Data-only: "Explored Map" (Nexus 42) and "Additional Biome Modifiers And Goals" style mods. https://www.nexusmods.com/darkestdungeon2/mods/42 , https://www.nexusmods.com/darkestdungeon2/mods/56 [confirmed]

**Roster / hub features.**
- Plugin-DD2 hooks `HeroSelectBhv` (Init, PopulateActorInfo, ConfirmRosterSelection, CheckToSetConfirmButtonActive), `Roster.OnGameModeEnterPreStart` / `RemoveInvalidRosterEntries`, `HospitalScreenBhv` (quirk lock/remove, cost), `CharacterSheetUiBhv`, `MainMenuUiScreenBhv.OnContinueGameClick`, `InputSystemBhv.Update`; features include multiple actors per class and starting with fewer than 4 heroes. Hook list from the HookGenerals.cs link above; feature list from https://www.nexusmods.com/darkestdungeon2/mods/207 [confirmed]
- Darkest2in1 keeps its own persistent estate saves at `<persistentDataPath>/DarkestDungeon3/estate_N.json`, converting DD1 hero records to DD2 `ActorInstance`s per expedition and reading HP/stress/deaths/loot back after. [confirmed, MODLOG]

**Exposes a debug console.** The game has one built in (see 2.3). DD2 Debug Actor Plus "replaces and expands the built-in actor debug editor". https://github.com/superexboom/DD2DebugActorPlus [confirmed]

**Shared frameworks / libraries.**
- Closest thing: **Plugin-DD2** (CSV override layering via `ResourceGroupCsvDatabase.GatherResources`, localization via `EnglishLocalizationData` / `ForeignLocalizationData.TryPopulateStrings`, external Addressables via `AssetReference.RuntimeKeyIsValid` / `AssetReference.Asset` getter / `ResourceDatabaseObject<T>.OnEnable`, audio via `AudioBankMgr.Initialize` / `AudioEventUtils.GetEventGuid`, `ExternalResourceManager.cs`). Nexus 207 lists "Mods using this mod (8)". [confirmed; 2023 build]
- `DD2DebugDemoCore` (reusable actor/loadout/equipment helpers, editor-pref reader/writer). https://github.com/superexboom/DD2SteamMP [confirmed]
- DD2 Damage Meter exposes `DamageMeterMultiplayerApi`. [confirmed]
- No shared UI or localization library for DD2 plugins was found. [confirmed absence]

**Hook inventory in current use (Darkest2in1, 2.04, from MODLOG; class names are real 2026 names):** `CombatPresentationBhv.SetNextGameMode`, `CombatPresentationBhv.CheckForEndOfCombat`, `LootManager.ShowLoot`, `ActorInstance.ApplyStressDamage`, `CombatBhv.AttemptRetreat`, `ActorBhv.Show`, `LibraryActors.CreateActor`, `LibraryBattleConfigurationTables.RollBattleConfiguration`, `GameModeMgr.SetMode`, `GameTypeMgr.SetCombatScenario/SetGameType`, `RunBhv.SetNextRunStartType`, `QuirkContainer.Add/RemoveAllInstances`, `BuffContainer.TryAdd`, `DLCManager.GetAllOwnedDLCLabels`. DD2SteamMP: `Assets.Code.Platform.PlatformMgr.Initialize` postfix; ordainment resolved at `ActorInstance.PostCreate -> BossCalculation.RollBossModifier`. Separately, the Darkest2in1 MODLOG recon lists IronCrown namespaces `Assets.Code.Mod`, `Assets.Code.UI.Mods`, `Assets.Code.Combat.Test`, `Assets.Code.Kingdom*`, `Assets.Code.Map.*`, `Assets.Code.Run`, `Assets.Code.Roster`, `Assets.Code.Inn` (the first two are presumably the official mod loader and mods menu; that reading is my inference). [confirmed list, inference on purpose]

### 2.3 Built-in developer layer (useful and often missed)

- Enable with Steam launch option `-allowEditorPrefs` and a file `Darkest Dungeon II_Data\StreamingAssets\editor_prefs.txt` containing lines such as `ENABLE_CHEATS-true`, `fast_driving-true`, `fast_driving_mult-X`. https://steamcommunity.com/sharedfiles/filedetails/?id=3439563308 (guide dated 2025-03-06; its speed-up part is obsolete since Steadfast Steward added a speed option) [confirmed]
- Plugin-DD2 force-enables this by patching `CommandLineUtils.IsEditorPrefsEnabled`, so no launch option is needed. https://raw.githubusercontent.com/Binarizer/Plugin-DD2/main/DD2_Plugin_Binarizer/Hooks/HookGenerals.cs [confirmed]
- Once enabled the "Enable Cheats" mod page lists hotkeys: Alt+J actor editor, Ctrl+J combat debug (combat only), P debug audio player, L toggle localization, plus gold/torch/doom/heal/stress shortcuts. https://www.nexusmods.com/darkestdungeon2/mods/56 [confirmed]
- The official modding wishlist also asks Red Hook for a way to set EditorPrefs from a mod. See section 3. [confirmed]

---

## 3. Red Hook's official modding documentation

**Where it lives.**
- Official guide: Google Doc "DD2 Modder Guide", "Updated Jun 13, 2025" in its own header. https://docs.google.com/document/d/1ga3FNrL3eGDRMFekLx9-RKhTDLMxPO603XzXcZa8O78 (read raw via the export endpoint). Companion doc "Non-Steam Workshop Mod Creation and Installation" (id `1RYOe7yJqThgUv3s-1MlVGdBEv0dwof8zc2PVu57C-dE`), the Excel helper `dd2_mod_data_exporter.xlsm` (dated 12 Jun 2024), and `darkside.zip` (about 1.55 GB, modified "15 Sep", year not displayed) sit in a public Drive folder linked from mod.io: https://drive.google.com/drive/folders/1SlMxq3O2nuOp3P__G-0QIU748RGFIFnu and https://mod.io/g/darkest-dungeon-ii/r/darkest-dungeon-ii-mod-guide (author redhookjohn, about 2y 1mo old when read). [confirmed]
- The guide says the **Darkside** Unity project is on Steam under Tools as "Darkest Dungeon 2: Mod Tools" (always newest) and mirrored on Drive for non-Steam users. [confirmed]
- Steam news posts (raw JSON, dates verified): "Update on Upcoming Darkest Dungeon II Mod Support" 2024-05-06; progress reports 2024-06-26, 07-24, 08-02; Darkside of the Mountain build 1.06.66916 on 2024-08-22; "Expanded Modding Support and Origin Skins Available Now!" 2025-06-26; Darkside updates 2025-07-25, 2025-07-31 (Hero Creation Tool), 2025-08-07 (hero template: unlock track, chapter 5 boss Spectre); "Mac Mod Update" 2026-08-13; recurring "Mod Showcase" posts 2026-03-09 to 2026-09-21 (weeks 1-15). Feed: https://api.steampowered.com/ISteamNews/GetNewsForApp/v0002/?appid=1940340&count=500&maxlength=0&format=json ; the first post is also at https://store.steampowered.com/news/app/1940340/view/4174350531694214120 (page body did not render for the fetch tool; text read from the feed). Red Hook's own site returned 404 for its copy of that post: https://www.darkestdungeon.com/news/update-on-upcoming-darkest-dungeon-ii-mod-support/ . [confirmed]
- Steam store lists the "Steam Workshop" category; the Workshop browse page showed about 983 "ready to use" entries when fetched. https://store.steampowered.com/api/appdetails?appids=1940340 and https://steamcommunity.com/workshop/browse/?appid=1940340 [confirmed, count approximate]
- mod.io also hosts a DD2 mod listing (a browse page with dozens of mods; dozens of entries). Whether mod.io is an official distribution channel or a community mirror is **[unconfirmed]**. https://mod.io/g/darkest-dungeon-ii

**What the official system supports (from the guide, 2025-06-13).**
- Item types: trinkets, combat items, rest/inn items, memories, stagecoach upgrades (general, pet, trophy, infernal, radiant), flames; custom hero palettes; hero skins / custom hero models (and, via Hero Creation Tool, whole custom heroes based on a Highwayman template; custom paths are "pretty hacky"). Data: overrides of existing rows by type+ID; appending to loot tables. [confirmed]
- Data tables: CSV files in the mod's export folder (items, loot tables, buffs, effects, conditions, actor data, localization strings via `dd2_loc_strings.txt` and a `Localization` folder). The base game's own CSVs live in `Darkest Dungeon II_Data\StreamingAssets\Excel\` (517 files plus `dlc_*`, `expedition`, `kingdom` subfolders per Darkest2in1 MODLOG) and are the best reference. [confirmed]
- Art: Unity prefabs/materials built in Darkside, bundled automatically as Addressables for Workshop upload. Local test: copy the built `<MODNAME>_export` folder to `Darkest Dungeon II_Data\StreamingAssets\mods\` (folder must be named `mods`, lowercase; Steam, Epic and GOG install paths all listed). [confirmed]
- Audio: the guide says custom audio/SFX is not currently possible (testers had the FMOD plugin but not the DD2 FMOD library). Red Hook's 2025-07-25 Darkside update added an FMOD project and plugin. [confirmed]
- Heroes yes; **monsters: "Custom monsters (maybe?)"**, custom models/textures for monsters, nodes, stagecoach, custom VFX/SFX, weapon kits, biomes, nodes, conditional mod loading, custom cutscenes, and **custom UI elements and UI changes** are all listed under "Not currently possible" (that block carries an "OUT OF DATE AS OF STEADFAST STEWARD" tag; some entries have since moved, e.g. weapon kits shipped 2025-06-26). Authorship of that block is not stated; it may be tester-written. [confirmed text, attribution unconfirmed]

**Position on code / DLL mods.**
- The same list contains "Scripting, library loading, code injection" under "Not currently possible", and wishlist item 20 asks Red Hook for "code injection ... or the ability to load in a library ... (eg. harmony, bepinex)". [confirmed]
- The guide's "Implementation Ideas" section (Python scripting proposal; "C# Libraries and Harmony" proposal) is written in a modder's voice and is explicitly under "feature suggestions and other info for Red Hook". It says assembly sideloaders like BepInEx and Unity Doorstop already exist, that first-party support would need a mechanism to load assemblies at startup, and it names "Binarizer's Lib and Speedwagon" as earlier Harmony-based mods. Downsides it lists: running arbitrary code on players' PCs, Steam Workshop's malware checks, decompiling as a barrier, recompiling per major version. **This is a modder's proposal, not a Red Hook statement.** [confirmed text; attribution is my reading]
- **Red Hook has not, in anything I could read, said they forbid or endorse BepInEx/DLL mods. [unconfirmed]** Red Hook's Discord is not searchable from here. 
- **Can Workshop mods contain code? No, per the guide's own table. [confirmed]** Mechanism: Darkside exports CSVs + Addressables asset bundles. **[inference]** An asset bundle cannot carry new compiled C# beyond MonoBehaviour references to scripts that already exist in the game's assemblies, so new logic still needs a BepInEx-style plugin distributed outside Workshop.

**Older/unofficial guides.** Steam guide "[Deprecated] Modding Guide - Darkest Dungeon II" (tools: AssetStudio, UABEA, dnSpy on `ironcrown.dll`, BepInEx + Sinai's UnityExplorer, BuildPotFile): https://steamcommunity.com/sharedfiles/filedetails/?id=2985496131 . Nexus article "A note about DD2 modding" (tbonex28b, 2021-11-01; a commenter notes BepInEx can patch `ironcrown.dll` methods): https://www.nexusmods.com/darkestdungeon2/articles/1 . Metamorph hero worklog (Darkside + Blender + Unity 2022.3.16f1; palettes did not work on custom heroes): https://github.com/dnauu-bmsotc/DD2-Hero-Mod-Metamorph . Community Darkside add-ons (sprite/localization sync, needs Python + UnityPy): https://www.nexusmods.com/darkestdungeon2/mods/282 . [confirmed]

---

## 4. Anti-tamper / anti-cheat / running from a copy

**Anti-tamper or integrity checks.**
- Steam store page categories: Single-player, Steam Achievements, Workshop, Cloud, Trading Cards, Family Sharing; **no VAC / anti-cheat category and no third-party DRM or account notice**. https://store.steampowered.com/api/appdetails?appids=1940340 [confirmed]
- Darkest2in1's recon scan of the Epic build: "No anti-cheat. No loader installed." and BepInEx loaded without any workaround (2026-10-02). https://raw.githubusercontent.com/Stephane-Dedu/Darkest2in1/main/MODLOG.md [confirmed; it is a file-level scan plus a working run, not a security audit]
- Years of Harmony plugins run on the Steam build (section 2). Also a 2021 Nexus readme tells users to edit `IronCrown.dll` in dnSpy and save it in place; the game then loaded the modified DLL. https://www.nexusmods.com/darkestdungeon2/mods/56?tab=docs [confirmed for 2021-era build; not re-tested]
- **No integrity check that reacts to injected assemblies was found. [inference from absence]**
- One real gotcha: when a game callback throws (e.g. an NRE in the road results scene during a modded fight), **DD2 uploads a crash report to Red Hook each time** (Darkest2in1 MODLOG). Guard your patches so you don't spam their crash reporter. [confirmed]
- Whether code mods (or editor prefs) disable Steam achievements: **[unconfirmed]**, no source discusses it.

**Running a copy of the game folder outside Steam with `steam_appid.txt`.**
- **[unconfirmed] for the Steam build.** I found no report of anyone doing this for DD2, and no statement about whether DD2 calls `SteamAPI.RestartAppIfNecessary`.
- Related evidence: DD2SteamMP says "Start the game through Steam" with Steam running under the same account (its lobby features need the Steam API). https://raw.githubusercontent.com/superexboom/DD2SteamMP/main/README.md . Darkest2in1 worked on the Epic build in its Epic install folder (no Steam), and listed "does the Epic build run standalone, or only through the Epic launcher?" as an open question that day; it then clearly ran (`DRIVING -> COMBAT` log lines in its scripts), so it ran at least from the Epic folder. Its "partial copy" on another drive was not a working game only because the E: disk was failing, not because of a game restriction. https://raw.githubusercontent.com/Stephane-Dedu/Darkest2in1/main/MODLOG.md [confirmed]
- Suggestion: test a copy early in your own lab (copy folder, add `steam_appid.txt` containing `1940340`, launch the exe). If it fails, fall back to launching via Steam with BepInEx in the library folder, which is what every other project does.

---

## 5. Prior "DD1 in DD2" attempts, and Kingdoms

**Darkest2in1 is the only DD1-in-DD2 project found (public).** https://github.com/Stephane-Dedu/Darkest2in1
- Scope: "DD1's Hamlet, quests, provisioning, room-and-corridor dungeons, curios, camping, loot and town events, with DD2's heroes and DD2's combat." Reads DD1 data/art/audio at runtime from the user's own DD1 install; nothing from DD1 is shipped. [confirmed, README]
- How far it got in about 1.5 days (2026-10-02 17:26 UTC to 2026-10-03 20:00 UTC, 73 commits): Core library (48 unit tests at first status), DD1 dungeon generator reimplementation, quest board, hamlet buildings and Abbey/Tavern, estate saves, full corridor/room crawl with torch/hunger/traps/curios/camping, battle loot, DD1 monsters rendered via a Spine 2.1 CPU rasteriser over DD2 stand-in enemies with DD1 skills mapped to DD2 skills, DD2 hero 3D models rendered on an off-screen stage, DD1 audio, Blacksmith/Guild/Survivalist/Stagecoach screens. [confirmed, commit log and MODLOG]
- Self-declared deferred: corpse mechanics, DD1 afflictions/virtues vs DD2 meltdown/resolve, plot/arena/returning-dead town events, the Darkest Dungeon quest chain, curio outcomes (not rolled), gathering/activation quest types, Sanitarium and other building activities, DD2 biomes as zones; some features "not verified in-game". [confirmed, MODLOG]
- Pitfalls it recorded: off-screen hero render came out black until camera/lights used the `DeferredRenderFeature` layer mask (read by reflection); `ResourceDatabase*` singletons exist only mid-run; `ilspycmd 9.1` stack-overflows on IronCrown.dll (workaround below); map-gen corridor nudging; DD1 data inconsistencies. [confirmed]
- It also mentions a private older attempt ("DD1inDD2", marked bad code, not public). [confirmed mention, nothing else known]
- **Overlap warning:** goals are the same as this repo's estate idea. Check its roadmap before duplicating the Hamlet/Embark/Crawl UI work; consider a different slice (e.g. DD2-native estate) or coordinate. Licensing: none declared.

**Data-only DD1 content ported to DD2 (Nexus):** The Abomination (DD1 class port) https://www.nexusmods.com/darkestdungeon2/mods/208 ; Prophet DD1 Mini Boss https://www.nexusmods.com/darkestdungeon2/mods/200 . The official wishlist asks for a "DD1 character" Unity class so sprite-based DD1 actors need no dummy 3D model (not fulfilled as far as I can tell). https://docs.google.com/document/d/1ga3FNrL3eGDRMFekLx9-RKhTDLMxPO603XzXcZa8O78 [confirmed]

**Kingdoms (free update, released 2025-01-27; 2.00.x).**
- Red Hook's own description: "persistent rosters and a larger self-contained campaign" taken from DD1 and remixed with DD2 parts; "a board-game inspired strategy metagame" with persistent space and time; upgradeable Inns with extensive upgrade trees, quests, themed modules (Hunger of the Beast Clan, Secrets of the Coven 2025-04-14, Curse of the Court 2025-08-28). https://steamstore-a.akamaihd.net/news/externalpost/steam_community_announcements/1789580505316574 (Kingdoms Development Notes, 2025-01-23) and https://steamstore-a.akamaihd.net/news/externalpost/steam_community_announcements/1789580505442819 (launch) [confirmed]
- **It is hamlet-like in spirit (persistent roster, Inn upgrade trees) but not an estate**: the Inns are a network on a map, not a town screen. Whether any modder builds an estate on it: not found. [confirmed absence]
- Kingdoms code lives in `Assets.Code.Kingdom*` namespaces and CSVs in `StreamingAssets\Excel\kingdom\` (per Darkest2in1 MODLOG recon). Mods touching Kingdoms found: Kingdoms Factions In Confessions, Militia Barks Expansion, and "Darkest Delight" (changes to both modes) on mod.io. https://mod.io/g/darkest-dungeon-ii [confirmed]

---

## 6. Tooling for DD2 asset work

**Unity asset tools (latest releases per GitHub API on 2026-10-04):**
- **AssetStudio (Perfare)**: archived; last release v0.16.47 (2022-06-16). Still the tool named in 2021-2024 DD2 guides for browsing/exporting bundles. https://github.com/Perfare/AssetStudio [confirmed]
- **AssetStudioMod (aelurum)**: v0.19.0 (2025-09-04), maintained fork. https://github.com/aelurum/AssetStudio [confirmed]
- **AssetRipper**: 2.0.0 (2026-08-24), 1.3.14 (2026-04-25). https://github.com/AssetRipper/AssetRipper [confirmed]
- **UABEA**: v8 (2024-11-03), repo pushed 2026-05-11. Used on DD2 compressed bundles (prompts to decompress to memory or file; save is uncompressed, recompress via File > Compress). https://github.com/nesrak1/UABEA ; guide https://steamcommunity.com/sharedfiles/filedetails/?id=2985496131 [confirmed]
- **UnityPy** (Python): used by the community Darkside tools to find and replace textures/sprites inside DD2 bundles. https://www.nexusmods.com/darkestdungeon2/mods/282 [confirmed]
- **Per-tool compatibility with this game's 2022.3 build: [unconfirmed]**. No DD2-specific compatibility statement found for AssetStudio, AssetRipper or UABEA against 2022.3. The 2024-era guide used UABEA/AssetStudio successfully, but that predates (as far as I can tell) the move off Unity 2020.3.7f1; see section 1.
- **Decompiling IronCrown.dll**: `ilspycmd 9.1` stack-overflows (infinite recursion in `CSharpConversions.UserDefinedExplicitConversion`) and silently writes about 60 files; workaround = a small .NET 8 console app on ICSharpCode.Decompiler 11.1.0.9782, per-type on a 1 GB-stack thread with crash recovery; 3041 types, 0 failures, about 4 min. Source in the Darkest2in1 repo: https://github.com/Stephane-Dedu/Darkest2in1/tree/main/tools/dd2decomp . Older guides use dnSpy 6.1.8. https://www.nexusmods.com/darkestdungeon2/mods/56?tab=docs [confirmed]
- **Runtime inspection**: BepInEx + UnityExplorer (Sinai-dev; UI toggles with F7 by default). https://steamcommunity.com/sharedfiles/filedetails/?id=2985496131 [confirmed]

**Addressables.**
- Bundles: `Darkest Dungeon II_Data\StreamingAssets\aa\StandaloneWindows64\*.bundle`, e.g. `darkest_assets_all_<hash>.bundle`, `hero_<name>_assets_all_<hash>.bundle`; the bundle named `addressable_resources_assets_data` holds `SkillDatabase` (a skill = a `ResourceZoomInSkill` MonoBehaviour; adding one means adding the asset and a SkillDatabase entry). https://steamcommunity.com/sharedfiles/filedetails/?id=2985496131 , https://www.nexusmods.com/darkestdungeon2/mods/23 [confirmed]
- Code-side loading of your own bundles was solved in Plugin-DD2 by hooking `AssetReference.RuntimeKeyIsValid` and the `Asset` getter, and `ResourceDatabaseObject<ScriptableObject>.OnEnable`. https://raw.githubusercontent.com/Binarizer/Plugin-DD2/main/DD2_Plugin_Binarizer/Hooks/HookGenerals.cs [confirmed, 2023 build]
- Darkest2in1 loads portraits and story art via `Addressables.LoadAssetAsync` and, outside runs, resolves asset names through DLC labels (`DLCManager.GetAllOwnedDLCLabels`). [confirmed]
- The official guide's wishlist notes that unsubscribing from a mod leaves a small `catalog.jsondd` behind, so official mods ship per-mod Addressables catalogs. [confirmed, text of wishlist item 24; interpretation mine]
- Any tool for editing the main Addressables catalog directly (beyond UABEA/UnityPy edits): **[unconfirmed]**, none found.

**FMOD.**
- Banks live in `StreamingAssets`. Community route (2021 Nexus article): QuickBMS to pull audio out of `.bank`, "FMod Bank Tools" (FModPlus) to extract/rebuild, FMOD Studio to author banks; replacing same-named wavs works, adding new event IDs was unsolved. https://www.nexusmods.com/darkestdungeon2/articles/1 [confirmed, 2021]
- Official Darkside got an FMOD project + plugin on 2025-07-25, but the guide says the DD2 FMOD library was not provided and custom audio is not currently possible. [confirmed, see section 3]
- Current general tools: vgmstream r2117 (2026-05-19) https://github.com/vgmstream/vgmstream ; Fmod5Sharp v3.1.0 (2026-02-15) https://github.com/SamboyCoding/Fmod5Sharp . Not verified against DD2 banks. [unconfirmed for DD2]
- Darkest2in1 has its own FSB5 reader for DD1 audio (`Fsb5Index.cs`) and plays DD1 sound at runtime; DD2 music is turned down while it plays. [confirmed]

**Heroes/monsters: 3D meshes with a 2D look, not Spine. [confirmed for heroes]**
- Heroes are rigged, skinned 3D meshes: the old guide has a "Rigged Meshes" section (export the skeleton, weight-paint in Blender, fix bone order); textures are `tex_<hero>_col` and `tex_<hero>_ink` (a colour map and an ink-line map; the official guide calls the editable "col" texture the palette layer). The Metamorph repo says custom heroes need fully rigged and animated Blender models (roughly 20 short skill animations and 20 static poses). https://steamcommunity.com/sharedfiles/filedetails/?id=2985496131 , https://www.nexusmods.com/darkestdungeon2/mods/23 , https://github.com/dnauu-bmsotc/DD2-Hero-Mod-Metamorph [confirmed]
- Darkest2in1 renders DD2 hero models to an off-screen stage and uses an off-screen camera in dungeon screens; it hides the DD2 stand-in enemy once its DD1 sprite draws, which implies DD2 enemies are also models. [confirmed for heroes; monster format inferred]
- The only Spine in this ecosystem is DD1's (Spine 2.1 binary skeletons + libgdx atlases, rasterised on the CPU by Darkest2in1). [confirmed]
- Enemy assets partly exposed to modders: the 2025-07-25 Darkside update imported Ghoul and Lost Soul enemies and data. https://steamstore-a.akamaihd.net/news/externalpost/steam_community_announcements/1806064758651728 [confirmed]

---

## 7. Recommendations and pitfalls

**Recommended setup**
- BepInEx **5.4.23.5 win x64 (Mono)**, files into the game folder (`winhttp.dll` + `doorstop_config.ini` + `BepInEx/`). Defaults first; if plugins load but do nothing, set `HideManagerGameObject = true`, and in any case create your own `DontDestroyOnLoad` + `HideAndDontSave` host GameObject.
- Compile net472/net48 against `IronCrown.dll` (logic) and BepInEx's `0Harmony.dll` (HarmonyX); keep a private copy of reference DLLs outside the game folder so builds do not depend on a running install.
- Apply each Harmony patch class under its own try/catch (Darkest2in1 pattern) and log skips.
- Turn on `-allowEditorPrefs` (or patch `CommandLineUtils.IsEditorPrefsEnabled`) and `TextBasedEditorPrefsBaseType` for fast test setups; use the `battle_test_*` keys or `CombatScenarioData` + `GameModeMgr.SetMode(COMBAT)` to start fights.

**Read first**
1. https://github.com/Stephane-Dedu/Darkest2in1 (MODLOG.md, `Dd2/Dd2Run.cs`, `Dd2/Dd2Combat.cs`, `Plugin.cs`) - read for design, do not copy without a license.
2. https://github.com/superexboom/DD2SteamMP (`DD2DebugDemoCore`, Doorstop chainloader, adapters).
3. https://github.com/Binarizer/Plugin-DD2 (CSV/localization/Addressables hook points; MIT; 2023 names).
4. The official guide doc (section 3) and `StreamingAssets\Excel\*.csv`.

**Pitfalls and dead ends**
- Game updates break hooks (Binarizer's plugin went stale on official updates in late 2023); DD2SteamMP and the autobattler say so too. Expect to revalidate after every 2.0x hotfix.
- DD2 uploads a crash report when a callback throws; guard prefixes (`RoadResultsCopyStaysQuiet` example).
- Run-scoped singletons (`ResourceDatabaseActors`, `RosterManager`, torch/run values) only exist mid-run; outside a run use Addressables by DLC label or host a real run.
- Off-screen DD2 hero renders can come out black unless the camera/light masks match the deferred render feature.
- `ilspycmd 9.1` fails silently on IronCrown.dll.
- Official mods cannot carry code or custom UI; do not plan to ship through Workshop.
- Unity version for your Steam build is unresolved (2022.3.62f2 vs 2022.3.16f1 in all community sources); check before choosing Darkside/Unity versions for any bundle work.
- Running a Steam-build copy with `steam_appid.txt` is unverified; Steam features (lobby, overlay) need the Steam client.
