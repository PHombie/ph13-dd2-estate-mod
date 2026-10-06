# Roster lifecycle — starting roster, stage coach, dismissal, death, graveyard

Target: Darkest Dungeon II v2.04.85095. Source: decompiled `IronCrown.dll` plus the shipped CSV data and the
DD1 install. **Nothing here was run in game**; the game was not launched for this work.

Conventions
- `file:line` is relative to `_ref\dd2-decomp\IronCrown\` (folders = namespaces), e.g. `Assets.Code.Roster/RosterManager.cs:189`.
- CSV paths are relative to `<DD2>\Darkest Dungeon II_Data\StreamingAssets\Excel\`; DD1 paths to the DD1 install.
- **[NV]** = not verified: inferred, or it needs the running game.
- "Hub" = the mod's `ESTATE` game mode. "Bury" = take a dead hero's entry out of the game's roster.

Code: `src\DD2Estate\Estate\RosterLifecycle.cs`, `StageCoach.cs`, `StageCoachPanel.cs`, `Graveyard.cs`,
`GraveyardPanel.cs`, `RosterWindow.cs`, `RosterNames.cs`, `RosterDice.cs`, `RosterDev.cs`, `RosterPanel.cs` (edited),
`src\DD2Estate\Dd2\RosterPatches.cs`.

---

## 0. Summary

| Question | Answer |
|---|---|
| Which roster rules does a Kingdom started with a null difficulty configuration get? | The default difficulty `normal`: replacement type **`none`**, active entry limit **12**. |
| Who creates the heroes of a new game? | The game itself, at `EventGameTypeStarted`: one hero of every available class (15 with all DLC). A patch stops that; the mod creates four. |
| What does the engine do with a dead hero under `none`? | Roster entry → `DEAD` and stays for ever; the actor becomes the class's corpse and is dropped from the actor library at the next mode change. No respawn, no refill, and the class is never added again. |
| What does the mod add? | A graveyard record at the moment of death, and the `DEAD` entry is removed on the way back into the hub (or a frame later for a death outside a fight). The class is then free for the stage coach. |
| Party wipe | Kingdoms handles it in `KingdomBhv.HandleEventBattleResult` (game over or back to an inn). That handler is switched off in the Estate and nothing else leaves the fight, so a postfix on it sets the mode to the hub. No game over: `KingdomManager.SetGameOver` is already blocked (`EstateNeverEndsAsKingdom`). |
| Save | Nothing new in the game's files. Two sections in `estate_mod.sections`: `stage_coach` and `graveyard`. After a burial the saved roster holds no `DEAD` entries. |

---

## 1. What the Kingdom host gives the Estate

`EstateSession.Enter()` calls `KingdomBhv.StartKingdom(EstateMode.Hub, null, null, isLoad, loadModeByCell: false)`.

- With `difficultyConfiguration == null` no difficulty is set by the caller (`Assets.Code.Kingdom/KingdomBhv.cs:432-435`).
  `KingdomDifficulty.InitPreLoad()` then takes the test pref `KINGDOM_TEST_DIFFICULTY` (empty in a normal boot) and
  otherwise the first definition with `m_IsDefault` (`Assets.Code.Kingdom/KingdomDifficulty.cs:38-68`).
- Data: `kingdom_difficulty_data_export.Group.csv:14-27` — element `normal`: `m_IsDefault,True` (15),
  `roster_active_entry_limit,12` (18), `m_RosterReplacementType,none` (19). (`radiant`, lines 1-13, is the only
  difficulty with `refill`; none uses `respawn`.)
- Both values reach the roster in `KingdomBhv.InitManagersPostLoad()`:
  `m_RosterManager.InitPostLoad(m_KingdomDifficulty.GetValue(KingdomDifficultyValueType.ROSTER_ACTIVE_ENTRY_LIMIT), m_KingdomDifficulty.GetRosterReplacementType())`
  (`KingdomBhv.cs:752`; `Assets.Code.Roster/RosterManager.cs:77-81`). Both fields are `[DontSerializeToJson]`
  (`RosterManager.cs:41-45`), so they are set again from the difficulty on every start, new or loaded.

`RosterReplacementType` (`Assets.Code.Roster/RosterReplacementType.cs:9-13`): `NONE`, `RESPAWN`, `REFILL`.

Hero classes of a Kingdom-type game: every `ActorDataClass` with a starting roster status (`IsPopulateInRoster`,
`Assets.Code.Actor/ActorDataClass.cs:209`) that is valid for the game type; for `GameType.KINGDOM` only `IDLE` is
(`Assets.Code.Game/GameType.cs:16`). In Kingdom data the bounty hunter is `idle` too
(`kingdom\hero_bh_data_export.Group.csv:37`; `hire` in `expedition\`), so there are 12 base classes
(flagellant, grave_robber, hellion, highwayman, jester, leper, man_at_arms, occultist, plague_doctor, runaway,
vestal, bounty_hunter) and 3 DLC classes (crusader, duelist: `dlc_dul_cru\`; abomination: `dlc_catacombs\`).
`ActorDataClass.DLCNumber` (`ActorDataClass.cs:245`) is 0 for the base folder and for the game type's own folder
(`Assets.Code.Resource/ResourceGroupCsvDatabase.cs:86-97, 182`) and the DLC's number for a DLC folder (`:192-206`).

## 2. Who fills the roster at game start

`StartKingdom` → `EventKingdomStarted` (`KingdomBhv.cs:450`) → `GameTypeMgr.HandleEventKingdomStartedLogic` →
`OnGameTypeStarted` → `EventGameTypeStarted.Trigger` (`Assets.Code.Game/GameTypeMgr.cs:259-262, 347-367`) →
`RosterManager.HandleEventGameTypeStarted` (listener priority 100, `RosterManager.cs:142, 172-187`): when the event
is not a load it calls `FillRoster` (`:83-118`) → `AddMissingHeroesToRoster(isAllActorsUnlocked, ignoreActiveEntryLimit: true, null)`
(`:424-468`), which creates an entry for every class that is a roster class, has its DLC owned
(`DLCManager.GetUnownedDLCNumbers`, `Assets.Code.DLC/DLCManager.cs:124-135`), is unlocked on the profile
(`ActorDataClass.GetIsUnlocked`, `ActorDataClass.cs:430-443`), is valid for the game type and
**`!GetIsActorInRoster(item.Id)`** (`RosterManager.cs:438, 447-451`).

So the roster was already full (15 heroes, see MODLOG "Spike results") before `EstateSession.EnsureStartingRoster()`
ran; its own `FillRoster` call never did anything.

Other callers of `AddMissingHeroesToRoster`: entering `INN` when the game type adds missing heroes there
(`m_IsMissingHeroesAddedAtInn` is false for `KINGDOM`, `GameType.cs:16`; `RosterManager.cs:343-350`), entering
`HERO_SELECT` (`:371-380`; the Estate redirects that mode) and `RefillKingdomMap` (section 4.3). None of them is
reached in an Estate session; the patch covers them anyway.

## 3. Creating a hero

`RosterManager.CreateRosterEntry(ResourceActor rosterActorResource, RosterStatusType heroSelectRosterStatusType)`
(private, `RosterManager.cs:470-484`):
1. `LibraryActors.LibraryActorsInstance.CreateActor(resource)` (`Assets.Code.Actor/LibraryActors.cs:116-131`):
   `new ActorInstance(resource, guid)` — the constructor names the hero `hero_name_canonical_<class>` from the string
   table (`Assets.Code.Actor/ActorInstance.cs:677, 2332-2350`) — then `PostCreate(isLoad: false, resource)`
   (`:757-902`: skills, path, mode, HP to max, stress 0, and `OnRunStart()` because a game type is running, `:871-874`).
2. `new RosterEntry(resource.name, guid)`, added to `m_Entries`, status = the class's starting status (`IDLE`).
3. `ActorInstance.OnAddedToRoster()` (`ActorInstance.cs:6367-6378`): in a kingdom
   `m_QuirkContainer.GenerateInitialQuirks(clearSeedInstance: false)` and `KingdomRules.OnAddedToRosterEffects`.
   Quirk generation is not profile-seeded in Kingdom (`GameType.cs:16`); the container asks for one `pos_start` and one
   `neg_start` quirk (`Assets.Code.Quirk/QuirkContainer.cs:214-248`; `quirk_data_export.Group.csv:1-2`).

This is the call the game itself uses in the middle of a kingdom (`RefillKingdomMap`), so creating a hero while the
Estate is running is a shipped path. The mod calls it by reflection with the resource from
`Singleton<ResourceDatabaseActors>.Instance.GetResource(classId, true, false)` (the same three arguments as
`RosterManager.cs:453`).

The name is a plain saved field: `m_ActorName` has no `[DontSerializeToJson]` (`ActorInstance.cs:155`),
`SetActorName(string)` (`:2313-2316`) just sets it, and the game only calls it on the player's request (hero select,
loadouts, the name field of the character sheet: `Assets.Code.ui/CharacterSheetActorTitleUiBhv.cs:206`). A renamed
recruit keeps the name across saves and across the change to the corpse class, and the player can rename any hero in
the character sheet; the graveyard records the name the hero carried at death.

## 4. Death under the Estate's Kingdom host

### 4.1 The moment of death

`ActorInstance.Kill(DeathType, SourceType, IReadOnlyList<string> sourceIds, float killDamage, IReadOnlyList<uint> killingActorGuids)`
(`ActorInstance.cs:5050-5064`; the five-argument overload with one source id and one killer guid is `:5039-5048`):
`m_IsLiving = false`, `DEATH` effects, `EventActorDeath.Trigger(...)` (`Assets.Code.Actor.Events/EventActorDeath.cs`).

Listeners run from high priority to low (`Assets.Code.Events/EventManager.cs:67-107`: a listener is inserted before
the first one with a lower priority). There is **no try/catch around a listener** (`:185-224`): an exception in one
ends the event for all later ones, which is why every hook of the mod catches its own.

| Order | Listener | Effect |
|---|---|---|
| 0 | `KingdomManager.HandleEventActorDeath` (`Assets.Code.Kingdom/KingdomManager.cs:276-287`) | `m_NumberOfHeroesKilled++` (a statistic) |
| 0 | `AffinityManager.HandleEventActorDeath` (`Assets.Code.Affinity/AffinityManager.cs:252-255`) | removes the hero's relationships |
| 0 | `ActorInstance.HandleEventActorDeathEffects` (`ActorInstance.cs:1258-1291`) | death reactions of the others, only in a team |
| -100 | `ActorInstance.HandleEventActorDeathLogic` (`:1013, 1198-1256`) | the hero's trinkets and combat items go to `LootManager.EmptyInventoryIntoNextWindowLoot` (`Assets.Code.Loot/LootManager.cs:335-338`); with a valid death class `EventActorChangeClass` turns the actor into `<class>_corpse` — and `OnActorSetClass` sets `m_IsLiving = true` again for the corpse (`:1147-1163, 2268`) |
| -1000 | `RosterManager.HandleEventActorDeath` (`RosterManager.cs:143, 189-204`) | `REFILL`: the entry is removed. **Otherwise (`NONE`, the Estate): `SetRosterStatus(RosterStatusType.DEAD, killer)`**, which raises `EventRosterEntryStatusChanged(PARTY → DEAD)`: party data containers are detached (`:224-240`), `ActorInstance.OnRemovedFromParty` (`ActorInstance.cs:1381-1388, 4215-4226`), the combat model keeps its art (`Assets.Code.Actor/ActorCreateGameObjectBhv.cs:89-94`) |

The corpse (`hero_hwm_data_export.Group.csv:1-11`: `m_IsBattleComplete,True`, `m_DeathRound,3`) stays on the team
and can "die" again; `Kill` then raises a second `EventActorDeath` for the same guid with the corpse's class id.

### 4.2 After the fight

- `Battle.BattleEnd()` destroys the teams (`Assets.Code.Combat/Team.cs:103-121`): a team-0 actor that is still in the
  roster by guid is kept, so the corpse survives the battle end.
- Every mode exit ends with `LibraryActors.RemoveUnsavedActors()` (`GameTypeMgr.cs:331-345`; `LibraryActors.cs:80-83, 198-202`).
  `ActorInstance.GetShouldSave()` (`ActorInstance.cs:911-936`) is false for the corpse: outside `RESPAWN` it needs the
  actor's class id to be in the roster or the actor to be in a team, and `m_IsLiving`. The class id is now
  `<class>_corpse`, which no entry has, and the team is gone. The actor is removed and destroyed on leaving COMBAT.
- The `DEAD` entry needs no actor (`Assets.Code.Roster/RosterStatusType.cs:15`, `isActorRequired: false`), so
  `RemoveInvalidRosterEntries` keeps it (`RosterManager.cs:1270-1277`), also across save and load.
- The fallen hero's gear: `m_NextWindowItems` is merged into the next loot window (`LootManager.cs:362-366`). After a
  wipe there is no loot window; `ClearShowWindowVariables()` (`:434-443`) does not clear that list, so the gear shows up
  with the next loot the party takes **[NV]**.

### 4.3 Respawn, refill, or a dead entry for ever?

- **Respawn**: only `RefillKingdomMap()` does it (`RosterManager.cs:539-590`, `TryRespawn` `:1342-1360`,
  `ActorInstance.Respawn` `ActorInstance.cs:4311-4381`), only for `RESPAWN`, and it is only called when the kingdom
  day state becomes `START` (`:284-290`). The Estate's day is dormant and its type is `NONE`: never.
- **Refill**: the same method adds missing classes only for `REFILL`, or when a respawn failed (`:543-564`). Never.
- **So under `NONE` a dead hero is a `DEAD` entry for the rest of the game**, and because every way of adding a hero
  goes through `!GetIsActorInRoster(classId)` (`:451`), which a `DEAD` entry satisfies (`:1545-1555`), the class is
  gone for good as well. That is the vanilla Kingdoms rule on normal difficulty; DD1 wants the opposite.

### 4.4 Party wipe

- The Estate's combat source has `isEndOnPartyDead: false` (`Dd2\EstateMode.cs`); the battle still ends when the last
  hero falls, because corpses do not count as a team (`Assets.Code.Combat.BattleCondition/OnlyTeamBattleCondition.cs:17-55`).
- Kingdoms: `KingdomBhv.HandleEventBattleResult` (priority -100, `KingdomBhv.cs:183, 224-252`): with no `PARTY` entry it
  clears the loot window, runs `KingdomManager.CheckGameOverRoster()` — game over `KINGDOM_EMPTY_ROSTER` when the type is
  `NONE` and there is no `PARTY` and no `KINGDOM` entry (`KingdomManager.cs:367-373`; benched Estate heroes are `IDLE`,
  so **every** Estate wipe would qualify) — and otherwise `SetMode(GameModeType.INN)`.
- The combat presentation does not leave the fight by itself: `CombatPresentationBhv.CheckForEndOfCombat()` takes the
  `RosterManager.GetIsPartyDead()` branch, unloads the results scene and returns when there is no `RunScoreManager`
  (`Assets.Code.Combat.Presentation/CombatPresentationBhv.cs:1050-1060`). In a kingdom the handler above is the only
  thing that changes the mode.
- In the Estate that handler is skipped (`KingdomIgnoresEstateBattles`, `Dd2\SessionPatches.cs`) and `SetGameOver` is
  blocked (`EstateNeverEndsAsKingdom`, `Dd2\PersistencePatches.cs`). Without a replacement **a wipe would leave the game
  sitting in COMBAT** — read from code, not seen in game **[NV]**.

---

## 5. What the mod does

### 5.1 Patches (`Dd2\RosterPatches.cs`)

| Class | Target | Why |
|---|---|---|
| `EstateRosterIsNotAutoFilled` | prefix `RosterManager.AddMissingHeroesToRoster` (private): returns an empty list while `EstateSession.Active` | section 2: the game would start every estate with one hero of each class, and would be the only thing that adds heroes behind the stage coach's back |
| `EstateRemembersTheFallen` | prefix `RosterManager.HandleEventActorDeath` (private) → `RosterLifecycle.OnActorDeath` | the last listener of a death: the actor still exists (name), the entry is not `DEAD` yet (so a corpse's second death is told apart). Records the graveyard entry. The original runs unchanged |
| `EstateBuriesItsDead` | postfix `GameTypeMgr.OnGameModeExitComplete` → `RosterLifecycle.BuryDead()` when the next mode is the hub | section 4.3: removes the `DEAD` entries right after the game dropped the dead actors and before the hub is entered, i.e. before the dungeon decides how the fight ended, the week advances or a snapshot is written |
| `EstateSurvivesALostParty` | postfix `KingdomBhv.HandleEventBattleResult` (private) → `RosterLifecycle.OnBattleResult` | section 4.4: with no `PARTY` entry left it clears the loot window variables (as vanilla does) and calls `SetMode(EstateMode.Hub)`. A postfix runs even though `KingdomIgnoresEstateBattles` skips the original. It does nothing if a change to the hub is already under way |

Not patched, on purpose: `RemoveUnsavedActors` / `GetShouldSave` (the engine's handling of the dead actor is what the
Estate wants), `KingdomManager.CheckGameOverRoster` (already unreachable), `RemoveInvalidRosterEntries` (one hero per
class stays the rule).

### 5.2 `RosterLifecycle`

| Member | What it does |
|---|---|
| `CreateStartingRoster()` | New estate: `crusader` + `highwayman` if the crusader class is available, else `man_at_arms` + `highwayman`; two more at random from the available classes with `DLCNumber == 0`; all four `PARTY`. If the roster is not empty (the fill patch did not apply) it keeps the game's heroes and puts the first four in the party, as before. It runs once per playthrough however often it is called: the session calls it (section 8), and the `stage_coach` section's new-game reset calls it too, so an estate never starts empty |
| `AvailableClasses()` | The engine's own filter of section 2, cached for the session |
| `CreateHero(classId, name, party)` | Section 3 by reflection; `SetActorName(name)` when a name is given |
| `DismissBlockReason` / `RequestDismiss` / `Dismiss` | Section 7 |
| `OnActorDeath`, `BuryDead`, `OnBattleResult` | Called by the patches. `BuryDead` is also called before the weekly roll, before a hire and when a building window opens; a death outside a fight (trap, curio) queues it for the next frame in the hub |
| `OnSessionStart()` | Sets the private `RosterManager.m_ActiveEntryLimit` to 0. The engine's limit (12) is below the stage coach's largest roster; over the limit `RemoveIdleOverLimitEntries` deletes random `IDLE` entries on `EventRosterConfirmParty` (`RosterManager.cs:486-529, 615-617, 242-257`). The Estate never raises that event, so this is a guard, not a fix |
| `LivingGuids()`, `ClassesOnRoster()`, `Count`, `Changed` | For the screens |
| `KillForTest(guid)` | Test hook, section 10 |

Why the corpse actor is left to the engine in `BuryDead`: it is removed at the next mode exit anyway, it is never saved
(4.2), and after a death in the hub the dungeon view may still show it.

### 5.3 Stage coach (`StageCoach`, `StageCoachPanel`)

DD1 data, read at runtime from `campaign\town\buildings\stage_coach\stage_coach.building.json`, store `hero_recruit`:

| Value | DD1 key | Level 0 | Levels 1..5 |
|---|---|---|---|
| Recruits per week | `number_of_recruits_upgrades[level].amount` | **2** | 3, 4, 5, 6, 7 |
| Roster size | `roster_size_upgrades[level].amount` | **9** | 12, 16, 20, 24, 28 |
| First coach | `first_hero_classes` | plague_doctor, vestal | |
| Hiring price | (the store has no cost field) | free | |

- Level: `EstateState.Current.BuildingLevel("stage_coach")`; if `Buildings` ever holds `stage_coach.numrecruits` /
  `stage_coach.rostersize` (DD1's two trees), those win.
- Roster size is capped by the number of available classes (15 with all DLC): 9 at level 0, 12 at level 1, 15 above.
- Without the DD1 file: 2 recruits a week and no cap but the class count.
- **Offer**: on `EstateState.WeekAdvanced` (after burying the week's dead) and, if a week has none, on first use.
  Candidates = available classes without a living hero, minus `StageCoach.ClassBlockers`; sorted, shuffled with
  `RosterDice(seed, week)`, the first `recruits_per_week` taken. In week 1 DD1's `first_hero_classes` come first.
  Same seed, week and roster → same recruits. Recruits not hired are gone with the next week.
- **Names**: a class that never had a hero on this estate arrives under the class's own DD2 name (`Name == null`). A
  class that had one (dead or dismissed; tracked in `seen`) arrives as someone else, named from DD1's
  `localization\names.string_table.xml` (english block, `hero_name_<n>`, 562 entries, 558 distinct), avoiding names of the
  living and the dead. A 20-name built-in list is used only when the table cannot be read. (DD2 has a pool of its
  own under the repeated key `hero_name`, `Localization\Sources\hero_names.txt:86-`, used by
  `HeroSelectBhv.RollNewActorName`, `Assets.Code.Campaign/HeroSelectBhv.cs:1838-1847`; it is drawn from the game's
  `LOCALIZATION` random stream, `Assets.Code.Locale/Localization.cs:224-242`, so it cannot be rolled from the estate's seed.)
- **Hire**: free, needs `Count < RosterSize`, creates the hero on the bench (`IDLE`), removes the recruit from the offer,
  saves (`EstatePersistence.SaveNow("recruit")`).
- Screen: DD1's `stage_coach.character_background.png` (1395x776) with `stage_coach.character.png` at -12,108 in it
  (`campaign\town\town.layout.darkest`, `town_background_layout`: `area_pos 144 132`, `character_pos 132 240`), recruit
  plates `stage_coach.hero_background.png` (600x101) at 746,132 one every 100 px (`buildings\building.layout.darkest`
  `body_base_pos 596 102` + `stage_coach.layout.darkest` `store_item_pos 450 80`, `store_item_spacing 0 100`; reading the
  item position as the plate's centre is an inference **[NV]**). DD2 portrait: `ResourceActor.GetPortraitIconByType(Color)`;
  class name: the string-table entry whose key is the class id (`Assets.Code.Actor/ActorDescription.cs:23-26`;
  `Localization\Sources\hero_names.txt:5-11`, e.g. `man_at_arms=Man-at-Arms`; monsters likewise, `enemy_names.txt:17`
  `fanatic_whipper=Whipper`), with the prettified id as fallback; the first hero of a class shows
  `hero_name_canonical_<class>` (`hero_names.txt:42`, `highwayman` → Dismas).

### 5.4 Graveyard (`Graveyard`, `GraveyardPanel`)

One record per death: name, class id, week, place, killer's class name (when the killer is another actor). The place
is `Graveyard.Place` if the expedition set it, else the first line of `DungeonRun.Current.StatusText` (the dungeon's
name), else "the Hamlet". Screen: DD1's graveyard backdrop and keeper, `dead_hero_backdrop.png` slabs, newest first,
scrolling (`ScrollRect`).

### 5.5 Roster column (`RosterPanel`)

Rows are the living heroes (`RosterLifecycle.LivingGuids()`; the column used to rebuild every tick while a `DEAD` entry
existed). A small `x` in the corner of a benched hero's row asks for dismissal. Header: `Roster n/size  party n/4`.
`RosterPanel.CanvasRoot` is the hamlet canvas the windows open in.

## 6. Save data

`estate_mod.sections` (see `estate-save-design.md` 6.1):

```
"stage_coach": { "seed": 123456, "week": 3, "offer": [ { "cls": "vestal", "name": null }, { "cls": "highwayman", "name": "Auber" } ], "seen": [ "crusader", "highwayman", ... ] }
"graveyard":   [ { "name": "Dismas", "cls": "highwayman", "week": 2, "where": "The Ruins", "by": "..." } ]
```

Consistency with the game's own files:
- A hired hero is an ordinary roster entry plus an actor with `GetShouldSave() == true`.
- A dismissed hero's entry and actor are both removed before the snapshot is written.
- A dead hero: the record is written at death, the entry is removed before the hub is entered. A snapshot can only be
  written in the hub, so the normal save has no `DEAD` entries. If one ever does (a death in the hub and a save in the
  same frame), the entry is loaded as `DEAD` (4.2) and buried on the first way into the hub; the record is already there.
- Quitting in the middle of a fight resumes from the last hub snapshot: the hero is alive and not in the graveyard.

## 7. Dismissal

`RosterLifecycle.Dismiss(guid)`: allowed only in the hamlet, for a hero who is not in the party, not busy
(`EstateSession.PartyBlockReason`) and not the last one (DD1: `str_hero_cant_dismiss_confirm`). Order:
1. the entry is removed from `m_Entries` — what the engine does to surplus idle heroes
   (`RemoveIdleOverLimitEntries`, `RosterManager.cs:517`), without any event;
2. trinkets and combat items go to the estate's inventory with `ItemInventory.TryTakeAllFrom`
   (`Assets.Code.Item/ItemInventory.cs:1093-1137`), as a hired hero hands them back (`ActorInstance.OnHireEnd`, `:4263-4271`);
3. the actor is removed from the library (→ `ActorInstance.Destroy()`). Leaving that to the next mode exit is not
   enough: if the class is hired again first, the old living actor passes `GetShouldSave()` (its class id is in the
   roster again) and stays in the library and in `actors.json` for ever.

A benched hero has no relationships to clean up: moving between `PARTY` and `IDLE` rebuilds the connections for
affinity-valid statuses only (`AffinityManager.cs:314-324, 421-440`; `RosterStatusType.IDLE` is not one).

The confirmation is the native dialog: `CommonUiBhv.ShowConfirmationDialog(ConfirmationDialogType.HotkeyCloseable, title, desc, confirmAction, "Dismiss", declineAction, "Keep")`
(`Assets.Code.UI.Managers/CommonUiBhv.cs:2580-2590`). The texts are shown as given; the decline button only exists
when a decline action is passed (`Assets.Code.UI.Widgets/ConfirmationDialogBhv.cs:62-76`).

Not done: DD1's stress penalty for the others (`dismissed_hero_stress_penalties`); DD1 stress runs to 200, DD2's to 10,
and the penalty is keyed by hero level, which the Estate does not have yet.

## 8. Edits outside this feature

`Dd2\EstateSession.cs` — replace `EnsureStartingRoster()` (it is only called for a new estate):

```csharp
        private static void EnsureStartingRoster()
        {
            // Four heroes, all in the party; EstateRosterIsNotAutoFilled keeps the game from adding one of every class.
            RosterLifecycle.CreateStartingRoster();
        }
```

Until that is pasted the old body does nothing (its `FillRoster` call is the patched one and adds nobody) and the
four heroes are created a line later, when `EstateState.StartNew()` resets the `stage_coach` section.

Optional, `Dungeon\DungeonRun.cs` — name the place of a death exactly (`Begin()` and `Dispose()`):

```csharp
            Graveyard.Place = DungeonContent.DisplayName(_map.DungeonId);   // Begin()
            Graveyard.Place = null;                                          // Dispose()
```

Also worth knowing: `EstateSession.RosterGuids()` returns `DEAD` entries too (only between a death and the way back to
the hub); screens should use `RosterLifecycle.LivingGuids()`.

## 9. Not verified / risks

1. **None of it ran.** In particular: that the four patches apply (names are from the decompiled code), a kingdom that
   starts with an empty roster (other `EventGameTypeStarted` listeners were not all read), the direct COMBAT → hub
   change after a wipe, and that the hub is reached with no `DEAD` entries.
2. The reading that a wipe hangs in COMBAT without `EstateSurvivesALostParty` (4.4). If something else already brings
   the game home, the postfix sees the change in progress and does nothing; if that something acts later, there would
   be a second hub → hub change.
3. `GetResource(classId)` for a class that has no actor (portraits of recruits and of the fallen) loads the
   `ResourceActor` without a reference holder; whether the portrait sprite stays valid while the window is open **[NV]**.
4. The native confirmation dialog above the hamlet canvas and the mouse wheel on the graveyard list.
5. A hero killed outside a fight keeps a visible model in the corridor until the dungeon view rebuilds; the dungeon code
   has to react (`RosterLifecycle.Changed`).
6. After a wipe the roster may be empty; the stage coach still offers recruits (the week has advanced), so the estate
   is never stuck, but the hamlet shows no party until someone is hired.
7. `StageCoach` is also the name of a vanilla class (`Assets.Code.Game.StageCoach.StageCoach`); a file that imports
   both namespaces has to qualify it.

## 10. Test plan (dev bridge)

`python tools/bridge.py ...`; "log" = `BepInEx\LogOutput.log`. Commands added by this feature
(`Estate\RosterDev.cs`): `run roster.state | roster.hire index=N|cls=<id> | roster.dismiss guid=N [ask=true] |
roster.kill guid=N | roster.offer cls=<id> | roster.week | roster.bury | roster.open building=stage_coach|graveyard |
roster.close`.

**Killing a hero for a test**: `python tools/bridge.py invoke DD2Estate.Estate.RosterLifecycle KillForTest <guid>`
(same as `run roster.kill guid=<guid>`). It calls the engine's
`ActorInstance.Kill(DeathType.SKILL, SourceType.DEBUG, null, 0f, 0u)` (`ActorInstance.cs:5039`), the call a killing
effect ends in; it works in a fight and in the hub.

| # | Do | Expect |
|---|---|---|
| 1 | main menu: `run estate.wipe`, `run estate.enter`, wait for the hamlet, `run roster.state` | log `Estate roster: starts with crusader, highwayman, <two base classes> (15 classes ...)` and no `the game created` warning (while the old `EnsureStartingRoster` is still in place, its line `Estate roster: 0 heroes` comes first); 4 heroes, all `status: party`, 2 quirks each; `replacement: none`; `get DD2Estate.Estate.RosterLifecycle.Count` = 4; roster header `Roster 4/9  party 4/4` |
| 2 | same reply, `stageCoach` | log `Stage coach rules (DD1): recruits 2/3/4/5/6/7, roster size 9/12/16/20/24/28`; `recruitsPerWeek: 2`, `rosterSize: 9`, two recruits, `renamed: false`; plague_doctor and vestal unless they started |
| 3 | `run roster.open building=stage_coach`, `shot`, `observe Hire` | DD1 backdrop and coachman, two plates with DD2 portraits, names, class names, `Barracks 4 / 9`; a click outside or a right click closes |
| 4 | `run roster.hire index=0`, `run roster.state` | `hired the <cls>`; 5 heroes, the new one `idle` with the class's own name and 2 quirks; offer has 1 left; log `Estate saved (recruit)` |
| 5 | `run estate.exit`, `run estate.enter`, `run roster.state` | same 5 heroes, same offer and seed (save and load of the section) |
| 6 | bench one hero (click the row), `run roster.dismiss guid=<benched> ask=true` | native dialog "Dismiss <name>?"; Keep changes nothing; Dismiss removes the row, header `Roster 4/9`, log `was dismissed`. `run roster.dismiss guid=<party member>` → `not dismissed: In the party` |
| 7 | `run estate.battle config=lost_battalion_mash_210 arena=combat_arena_forest_faction`; in the fight `run roster.kill guid=<party member>`; `invoke '$CombatBhv' ForceEndCombat true`; take the loot | log `Graveyard: <name> the <cls> fell in week 1 (the Hamlet)`; during RESULTS `roster.state` still lists the entry as `status: dead`; back in the hub log `the <cls> (#n) is buried`, the entry is gone from `roster.state` and `estate.state`, `fallen` has the record; anything the hero carried was in the loot window |
| 8 | `run roster.offer cls=<dead class>`, `run roster.hire cls=<dead class>` | the recruit shows `renamed: true` with a DD1 name; after hiring `roster.state` shows a new guid with that name; `run estate.save`, exit, enter: name kept |
| 9 | `run roster.week` a few times | week +1 each, a fresh pair each time, never a class that is on the roster; a previously dismissed or dead class comes back renamed |
| 10 | fill the roster to 9 (`roster.week` + `roster.hire`) | the 10th hire answers `not hired: The barracks are full`; Hire buttons greyed out, note "The barracks are full" |
| 11 | wipe: start a battle, `run roster.kill` for all four party members (if the fight does not end by itself: `invoke '$CombatBhv' ForceEndCombat false`) | log `Estate: the party is lost; returning to the estate`; the hamlet opens, no game over, no hang; four graveyard records; benched heroes still there; `run roster.open building=graveyard` lists them newest first |
| 12 | wipe inside a dungeon (`run estate.embark`, reach a fight, kill all four) | as 11, then the expedition ends as lost, week +1, new offer, snapshot written with no `dead` entries (`estate.exit`, `estate.enter`, `roster.state`) |
| 13 | in the hamlet (no fight): `run roster.kill guid=<benched>` | record with `where: the Hamlet`; a frame later the entry is buried (the death outside a fight path) |
| 14 | kill the game process during a fight after a hero died, restart, `run estate.enter` | the hero is alive and the graveyard has no record of him (last hub snapshot) |
