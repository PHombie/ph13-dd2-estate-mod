# DD2 Estate — design

The DD1 campaign loop (hamlet → quest → corridor dungeon → hamlet, week by week, with the Ancestor's story)
played with DD2's heroes, combat, monsters, bosses, items and quirks. A third main-menu entry next to
Confessions and Kingdoms; vanilla modes and profiles are untouched.

## 1. Architecture (verified in game unless marked "planned")

| Layer | Decision | Status |
|---|---|---|
| Loader | BepInEx 5.4.23.5 + Harmony plugin `DD2Estate.dll`; everything runs on the mod's own hidden host object | verified |
| Entry | "Estate" row cloned from the Kingdoms row on the main menu | verified |
| Profile | Dedicated **mod profile "Estate"** (game's mod-profile list); previous selection is stored in the plugin config and restored on exit or after a crash | verified |
| Host game type | `GameType.KINGDOM` with `KingdomBhv` as manager owner, started without a kingdom map; a dormant `KingdomDaySystem` keeps the road scene out | verified |
| Game mode | Own `GameModeType` **ESTATE** (reflection on the private ctor): no scene, start point, no vanilla system claims it. Hamlet and dungeon both live here | verified |
| Scene | The session keeps one empty scene `estate_hub` loaded (the engine cannot drop its "Temp Unload Scene" otherwise); world objects of the dungeon go here | verified |
| Fights | `CombatScenarioData(config, arena, EstateMode.DungeonFight, party)` → `SetCombatScenario` → `SetMode(COMBAT)`; native RESULTS/loot; every vanilla attempt to go to DRIVING/INN/EMBARK/HERO_SELECT/ALTAR is redirected to ESTATE (`GameModeMgr.SetMode` prefix) | verified |
| Heroes | Native `ActorInstance`s in the Kingdom roster (`RosterManager`); party = entries with status PARTY | verified (one per class for now) |
| Saving | Kingdom snapshot pipeline (`kingdom.json` + `actors.json`) plus an `estate` section injected into `kingdom.json`; vanilla per-turn snapshots are suppressed while in Estate | planned (currently no saving) |
| DD1 data/art | Read at runtime from the player's DD1 install (`Dd1Install`): PNG layers, Spine atlases sliced without a Spine runtime, `.darkest`/JSON rule files, `string_table.xml` | partly verified |
| UI | Own uGUI canvases at 1920x1080 reference with DD2's font; native DD2 screens are reused where they exist (character sheet, inventory, store, hospital, loot) | partly verified |
| Text | English only for now; all mod strings go through one table so languages can be added | planned |

Source layout: `Dd2/` game integration (session, modes, patches) · `Dd1/` DD1 file readers · `Estate/` hamlet
screens · `Dungeon/` exploration presentation · `Core/` rules and generators with no Unity/game references
(unit-testable) · `UI/` uGUI helpers · `Dev/` test bridge.

## 2. Game loop mapping

| DD1 | In the mod |
|---|---|
| Week | Estate week counter; advances when a quest ends (success, retreat or wipe) |
| Gold / heirlooms | Estate currencies kept in the estate save section (DD1 icons); DD2 relics/baubles found in fights convert to gold on return |
| Roster | DD2 heroes. Phase 1: one per class (15 with DLC). Phase 2: duplicates (needs roster patches) |
| Stress | DD2 stress carried between quests (already persistent on `ActorInstance`); relieved at Tavern/Abbey |
| Resolve level | Estate-side hero level/XP (DD1 thresholds) gating quest difficulty; DD2 skill mastery is bought at the Guild |
| Quirks / diseases | DD2 quirks; Sanitarium = DD2 hospital screen (lock/remove quirk, cure disease) |
| Trinkets | DD2 trinkets; Nomad Wagon = DD2 store screen with estate gold |
| Blacksmith | No weapon/armour levels in DD2 → sells and upgrades **combat items** and unlocks item tiers |
| Guild | Skill mastery (DD2 hero upgrade points bought with gold), path change |
| Survivalist | Camping is replaced by DD2 inn items used at dungeon camp sites; the Survivalist sells/unlocks them |
| Stage Coach | Weekly recruits: missing hero classes, upgrades add recruit count and starting mastery |
| Provisions | Mod-side dungeon inventory with DD1 provision art: food, torch, shovel, key, bandage, antivenom, herbs, holy water; DD2 combat items sold alongside |
| Torchlight | DD1 light meter (0–100) drawn DD1-style; its band also drives DD2's flame level in fights |
| Dungeon | DD1 generator rules (rooms + corridors of segments), DD1 corridor/room art, DD2 fights in rooms and hallways, DD1 curios with outcomes remapped onto DD2 effects |
| Camping | Medium/long quests: one/two camps = heal, stress relief, inn items, night ambush chance |
| Town events | DD1 event table, effects remapped |
| Death | Permanent; Graveyard lists the fallen |

## 3. Story: DD1 places, DD2 inhabitants

| DD1 dungeon (art) | DD2 factions | Bosses (DD1 → DD2) |
|---|---|---|
| Ruins (`crypts`) | Lost Battalion, Cultists, Gaunts | Necromancer → Dreaming General · Prophet → Librarian (with Fanatics) |
| Weald | Pillagers, Plague Eaters, Beastmen | Hag → Harvest Child · Brigand Pounder → Antiquarian |
| Warrens | Swine, Vermin (DLC), Beastmen | Swine Prince → Beastmen Alpha/Warlord · Flesh → Chirurgeon |
| Cove | Fisherfolk (Coastal) | Drowned Crew → Leviathan · Siren → Coven matriarch |
| Darkest Dungeon | Cultists | Shackles of Denial, Seething Sigh, Focused Fault, Ravenous Reach, Body of Work (finale) |
| Wandering | — | Collector (full inventory), Shambler (altar curio), Death, Ancient Adversary (the Ancestor's statue) |

The Ancestor's narration frame is kept; boss introductions and quest texts are rewritten for the new
inhabitants (`docs/STORY.md`, planned).

## 4. Phases

1. **Slice** (in progress): menu entry → hamlet view → embark → one generated Ruins dungeon with corridor
   walking, a room fight and loot → return to hamlet, week +1.
2. **Persistence**: save/continue, estate state, roster meta.
3. **Hamlet buildings**: stage coach, tavern, abbey, sanitarium, guild, blacksmith, nomad wagon, survivalist,
   graveyard, statue; upgrades with heirlooms.
4. **Dungeon depth**: curios, traps, obstacles, hunger, light, scouting, camping, retreat, quest types.
5. **Content**: all four dungeons, boss quests, Darkest Dungeon chain, town events, narration.
6. **DD1 UI fidelity** (user requirement): every DD1-derived screen rebuilt from DD1's own layout files, panel art
   and fonts so it looks as it does in DD1 (hamlet chrome, quest/provision/embark, dungeon HUD, prompts).
7. **Polish and release**: balance, gamepad, localisation, README, packaging.
