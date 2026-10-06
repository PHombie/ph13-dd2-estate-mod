# One inventory: where the Estate runs two systems for one thing, and how to make each one

The owner's words (MODLOG, fifth round, item 4): "Then the mechanics must be made one. Right now we have in
effect two inventories in one game. Gold and so on."

This note maps every thing a player owns or carries, says where DD1's side and DD2's side both keep or change
it, ranks the overlaps, designs the single model (DD1's logic first) and gives an ordered work plan.

- Analysis only: nothing was built, run or changed. Facts marked **INFERRED** come from reading code and data
  and still want one look in the game; section 8 lists them.
- Mod line numbers: `src/DD2Estate/` on `dev` at `5654c4b` (2026-10-05). DD2 line numbers:
  `_ref/dd2-decomp/IronCrown/` (v2.04.85095). DD2 data: `StreamingAssets/Excel/*.Group.csv`. DD1 data: the
  owner's DD1 install.
- Branches in flight touch the same files (DD1 item tooltips: `DungeonHud`, `RaidScrolls`, `InventoryContent`;
  the raid results screens: `DungeonRun.Finish/BringHome`, `Core/Inventory.Settle`). Merge them before steps
  4, 5 and 9 of the plan.

## 0. In short

**What is wrong.** The estate's purse is a pile of DD2 item stacks in DD2's player inventory (33 slots of 40),
sharing those slots with every other DD2 item. Every won fight opens DD2's own loot window and pays DD2 things
(relics, materials, "mastery points", DD2 trinkets, combat, inn and stagecoach items) straight into that
inventory, past the DD1 bag. The DD1 bag's supplies cannot be used in a fight; the fight's item button uses a
different item from a different inventory. DD2's inventory screen opens from the character sheet and shows a
second view of the estate's stores, where the purse can be thrown away stack by stack.

**The model.** One ledger per thing, owned by the Estate. DD2's containers stay only where DD2's fight engine
reads them at fight time (a hero's two trinket slots, a hero's combat item slot, the flame's run value); the
mod is their only writer. DD2's player inventory is kept empty and never shown.

| Thing | The one store | DD2's side becomes |
|---|---|---|
| Gold | a number in the estate's save | nothing: no `gold` item is ever held |
| Heirlooms | `EstateState.Heirlooms` (as now) | baubles and materials no longer drop |
| Trinkets, unworn | the mod's own list (DD1's trinket storage) | player inventory emptied; a sweep catches strays |
| Trinkets, worn | the hero's DD2 trinket slots (engine socket) | written only through the mod's calls |
| On an expedition | DD1's bag, 16 slots, DD1 stacks: supplies, loot, gems, quest items **and found trinkets** | - |
| Fight loot | DD1's battle loot, on DD1's loot scroll, into the bag | DD2's loot tables and window off |
| Items in a fight | the bag's supplies, offered as DD2 combat items for the fight | the hero's combat slot is a view of the bag |
| Light | `Exploration.Light`, read back after a fight | `RunValueType.TORCH` is the socket |
| DD2-only currencies and items | none | never granted; old ones removed on load |

## 1. What exists today

### 1.1 The purse and the estate's stores

| Thing | Stored in | Written by, when | Shown by | Limits |
|---|---|---|---|---|
| **Gold** (the purse) | DD2 item `gold` (sub type `relics`) as stacks in `GameTypeMgr.PlayerInventory` (`PlayerItemInventory`); save: `kingdom.json` section `inventory`. Read `EstateState.Gold` (`Estate/EstateState.cs:98`), write `EstateState.AddGold` (`:100-107`) | Hamlet: tavern and abbey (`ActivityLedger.cs:148, 164, 263, 270`), sanitarium (`SanitariumLedger.cs:117, 130, 178`), blacksmith (`Blacksmith.cs:310`), guild (`Guild.cs:278, 294`), survivalist (`Survivalist.cs:132`), wagon (`NomadWagon.cs:178`, `Trinkets.cs:443`), building steps (`UpgradeRules.cs:272`), provision bill (`Dungeon/Provisioning.cs:67`). Expedition end: quest pay (`QuestBoard.cs:505, 590`), the haul (`DungeonRun.cs:1260-1261`). New estate: `Dd2/EstateSession.cs:100` (60). Dev: `UpgradeDev.cs:47`, `TestRun.cs:72`. **DD2: "Take All" on the fight loot window** (`LootUiControllerBhv.cs:337-339`) | `EstateSummary.cs:145`; every panel's prices; `ProvisionScreen.cs:337-338`; DD2's inventory screen (as stacks of relics); DD2's loot window | stack 40 (`item_data_export.Group.csv:13-15`); slots 20 + 13 = 33 (`inventory_rules_export.Group.csv:3`, `kingdom_rules_data_export.Group.csv:33-35` `player_inventory_max_slots 13`), shared with every other DD2 item. Empty inventory: 33 x 40 = **1320**. `AddGold` calls `ItemInventory.AddItems` (`ItemInventory.cs:779-828`), which returns what did not fit; the return value is dropped |
| **Heirlooms** | `EstateState.Heirlooms` (`EstateState.cs:29`); save `estate_mod.heirlooms` | quest pay (`QuestBoard.cs:506`), haul (`DungeonRun.cs:1266`), exchange (`HeirloomExchange.cs:97-98`), building steps (`UpgradeRules.cs:273`), dev | `EstateSummary.cs:146`, exchange panel, upgrade pane | none |
| **Trinkets, unworn** | DD2 items of type TRINKET in `PlayerInventory` (`kingdom.json` `inventory`); the DD1 rarity each came in under in `Trinkets.Grades` (`estate_mod.sections.trinkets.grades`, `Trinkets.cs:36, 117-127`) | `Trinkets.Give` (`:301`): quest reward (`QuestBoard.cs:575-594`), wagon (`NomadWagon.cs:170-184`), haul (`DungeonRun.cs:1272-1276`), test set-up. `Trinkets.Sell` (`:435`). `RealmInventory.Equip` / `Unequip` (`RealmInventory.cs:219, 269`). Dismissal (`RosterLifecycle.cs:314`). **DD2: fight loot "Take All" (no grade); a dead hero's trinkets through the next loot window; drags on DD2's character sheet; the discard key on DD2's inventory screen** | Trinket Inventory (`RealmInventoryPanel`), the wagon's sell row, **DD2's inventory screen** | DD2's slots (the 33 shared ones; `AddItemsWithOverflow` adds slots past the limit, `ItemInventory.cs:858-866`); one of each (`m_possessionLimit 1`, `LibraryLoot.GetCanCollect`, `LibraryLoot.cs:103-134`). DD1: 9999 slots, duplicates allowed |
| **Trinkets, worn** | the hero's `TrinketItemInventory` (`ActorInstance.GetTrinketInventory()`), 2 slots (`hero_rules_data_export.Group.csv:2`); save `actors.json` | `RealmInventory` (hamlet only: `Away`, `RealmInventory.cs:70`). **DD2's character sheet wherever it opens with `isInventoryEditable: true`**: roster right click (`RosterPanel.cs:578`), every `UpgradeUi.ShowSheet` (`UpgradeUi.cs:262-265`), the dungeon HUD (`DungeonHud.cs:518-526`), DD2's own keys on the results scene (`CombatResultsPresentationBhv.cs:344-366`). Death: into DD2's next loot window (`ActorInstance.cs:1215-1216`) | `RaidHeroPanel.cs:216-228` (view only), Trinket Inventory, DD2's sheet | 2 a hero in both games |
| Nomad Wagon's stock | `NomadWagon.Stock` (`sections.nomad_wagon`) | weekly | wagon window | DD1's |

### 1.2 On an expedition

| Thing | Stored in | Written by, when | Shown by | Limits |
|---|---|---|---|---|
| **The bag** | `Core.Inventory` (`Core/Inventory.cs:29`), held by `DungeonRun._bag`; save `sections.expedition.bag` (`DungeonRun.cs:132-147`) | see the rows below | `InventoryGrid` in `DungeonHud` (`DungeonHud.cs:330-335`), provision screen, loot scroll | 16 slots, DD1 stack limits (`InventoryRaidRules.cs:28-29`, read from `inventory/base.inventory.system_configs.darkest`) |
| Provisions and supplies (food, torch, shovel, key, bandage, antivenom, herbs, holy water, laudanum, firewood) | bag stacks, DD1 units | bought at `ProvisionScreen` through `ProvisionShop` (`Core/Provision.cs:96-208`); class gifts and firewood (`Provisioning.cs:39-55`). Used: `DungeonRun.UseItem` (`:333-404`), hunger (`:760-786`), obstacles (`:730-757`), curios (`:794-821`), camp (`:1077-1090`, `TakeFood` `:1170`). Left over: sold at DD1's sell value on the way home (`Inventory.Settle`, `Core/Inventory.cs:140-170`, keep rates `InventoryRaidRules.cs:82-87`) | bag, provision screen | food 12, torch 8, shovel 4, others 6, firewood 1. **Not usable in a DD2 fight.** Out of a fight: the mod's own stand-in effects (`InventoryRaidRules.cs:110-125`: +10/15% health, -1 stress) |
| Gold found | bag stack `gold` in **DD1 gold** (stack 1750) | curio loot (`DungeonRun.Stow`, `:978-1012`), camp loot (`:1179-1199`) | bag card, amount shown divided by 50 (`InventoryContent.cs:62-73`) | paid into the purse at the end, divided by 50 and rounded (`:1260`) |
| Gems, heirlooms found | bag stacks | `Stow`, camp loot | bag | gems 5 a stack; portrait 3, bust 6, deed 6, crest 12 |
| Quest items | the count is `Exploration.QuestItems` (`Core/Exploration.cs:54`); the bag mirrors it (`DungeonRun.SyncQuestItems`, `:408-416`) | exploration events | bag | 1 a slot |
| **Trinkets found** | **a list beside the bag**: `DungeonRun._trinkets` (`:65`), save `sections.expedition.trinkets` (`:142`) | `Stow` (`:983-989`); to the stores at `BringHome` (`:1271-1277`) with DD1's keep rate | one line of the log; no card anywhere (ui-parity-raid D9) | none: they take no slot |
| Light | `Exploration.Light`; copied one way into `RunValueType.TORCH` (`DungeonRun.ApplyLight`, `:934-938`) | DD1's rules; **DD2 fight effects and loot change TORCH only** | DD1 torch meter / DD2's torch widget | 0..100 in both |

### 1.3 DD2's own things that reach an Estate session

All of these arrive through DD2's fight loot (section 2.2) or are part of the hero.

| Thing | Stored in | How it arrives | What it can do in the Estate |
|---|---|---|---|
| Relics | = the purse, row 1 | 2 or 4 a road fight (`RELICS_TINY` / `RELICS_LITTLE`, `loot_data_export_CURRENCIES.Group.csv:49-68`), 12 x 1-2 and more from a lair boss (`RELICS_LOT` `:88`) | counted as estate gold 1:1 |
| DD2 trinkets from fights | `PlayerInventory`, no DD1 grade | 3 in 9 road fights (`kingdoms_road_faction_rewards_trinket_chc`, `loot_data_export_COMBATS.Group.csv:217-232`); lair boss tables | join the estate's stores (valued by `Trinkets.UngradedAs`, `Trinkets.cs:98-102`). DD1's own rate: table `A` gives a trinket in 8 of 174 draws |
| **Combat items** (85 kinds) | `PlayerInventory` (stack 2-4), and each hero's `CombatItemInventory`, 1 slot (`CombatItemInventory.cs:13-17`, `CombatRules.cs:36`); `kingdom.json` `inventory`, `actors.json` | lair boss loot (`ALL_COMBAT_ITEMS`, `loot_data_export_SUPPLIES.Group.csv:1`); a dismissed hero's go back to the stores (`RosterLifecycle.cs:315`) | equipped on DD2's character sheet (the slot opens DD2's inventory, `CharacterSheetStatsUiBhv.cs:812-828`), used from DD2's combat bar. The mod shows, sells and counts none of it |
| Inn ("rest") items | `PlayerInventory` | lair boss loot (`ALL_REST_ITEMS`, `loot_data_export_SUPPLIES.Group.csv:169`) | nothing: used at an inn only |
| Stagecoach items, trophies, pets | `PlayerInventory`; the coach's own slots (`kingdom.json` `stagecoach`) | lair boss loot (`SC_UPGRADES_ALL`, `SC_TROPHY`, `loot_data_export_STAGECOACH.Group.csv:1, 137`) | nothing: the coach sheet opens from the inn and road UI only |
| Inn materials | `PlayerInventory` item `materials`, stack 40 | **10 after every road fight** (`kingdoms_road_rewards_construction_currency`, `loot_data_export_COMBATS.Group.csv:195-200`) | nothing; a slot every four fights |
| Baubles (7 faction items) | `PlayerInventory`, stack 40 | `kingdoms_road_rewards_baubles` (`:187-194`), chosen by biome conditions | nothing. **INFERRED**: no biome is active in the Estate, so none may drop |
| Deliverables (`locked_strongbox` ...) | `PlayerInventory` | cave fights (`shared_caves_rewards_locked_strongbox`) | nothing |
| Hero upgrade points ("mastery points") | run value `RunValueType.HERO_UPGRADE_POINTS` (`RunValueType.cs:26`), `kingdom.json` `run_values`; cap 1000 | 1 in 5 road fights (`kingdoms_road_rewards_hero_point`, `:171-178`), every lair boss | shown as a line on DD2's loot window (`LootScreenWidgetBhv.cs:436-450`); spent only at an inn's trainer (`InnUpgradeSkillsBhv.cs:251`): never |
| Flame points | run value `TORCH` | lair boss loot `TORCH_SOME` (+15) | announced on the loot window, then overwritten by `ApplyLight` |
| Candles of Hope | profile value | not in the Kingdoms tables read (road, caves, lair boss) | - |

### 1.4 The hero's own progress and what it costs

| Thing | Stored in | Paid with | DD2's counterpart |
|---|---|---|---|
| Weapon and armour levels | `Blacksmith.ByHero` (`sections.blacksmith`, `Blacksmith.cs:41, 62`), put on the hero as the stats container `estate_gear` | gold, DD1's prices | none reachable |
| Skills learned and mastered | on the DD2 actor (saved with it), through the trainer's own calls (`Guild.cs:15-32, 278, 294`) | gold | DD2 pays the same with hero upgrade points at an inn: unreachable, but the points still drop (1.3) |
| Skills equipped | DD2's character sheet (`isSkillEditable: true`) | - | one system (DD1 has the same on its hero sheet) |
| Camping skills | `sections.survivalist` | gold | - |
| Resolve | `Resolve.Xp` (`sections.resolve`) | - | - |
| Quirks, diseases | the actor's `QuirkContainer` | gold at the Sanitarium (`SanitariumLedger.cs:115-117`; stays in `sections.sanitarium`) | DD2's candle costs (`hero_rules_data_export.Group.csv:5-7`) unreachable. **One leak**: if DD1's ward rules cannot be read, `SanitariumPanel.Open` falls back to DD2's hospital shop (`SanitariumPanel.cs:131-139`), which sells DD2 items for relics into DD2's inventory |
| Stress, health in town | the actor | gold at the tavern and abbey (`sections.activities`) | DD2's inn items: unreachable |

### 1.5 Two units of gold, three ways to round

- The purse counts "estate gold" = DD1 gold / 50. The scale exists because the purse is DD2 relics (comments at
  `ActivityRules.cs:18-19`, `DungeonRun.cs:33-34`, `EstateState.cs:13-14`). Two constants hold it:
  `ActivityRules.GoldScale` and `DungeonRun.GoldScale` (aliased by `InventoryContent.GoldScale`).
- The bag counts DD1 gold; DD2's fight loot pays relics 1:1 into the purse.
- Conversions: `ActivityRules.ToGold` rounds half up, callers clamp to at least 1 (`ActivityRules.cs:138-141`);
  `InventoryContent.ToEstateGold` rounds half away from zero (`InventoryContent.cs:35-38`);
  `ProvisionShop.Cost` rounds the whole bill up (`Core/Provision.cs:130`); shelf prices show a decimal
  ("1.5" for one food, `InventoryContent.cs:69-73`).

### 1.6 Save sections that hold any of this

`kingdom.json` (written by `KingdomBhv`, `KingdomBhv.cs:605-632`): `inventory` (DD2 player inventory: gold
stacks, unworn trinkets, every stray DD2 item), `stagecoach`, `run_values` (hero upgrade points, torch),
`loot_manager` (`LootManager.m_NextWindowItems`, `LootManager.cs:30`: loot waiting for the next window),
`roster`; and the mod's `estate_mod` (`EstatePersistence.SectionKey`, `EstatePersistence.cs:32`): `format`,
`week`, `heirlooms`, `buildings`, `completed_quests`, `sections.{trinkets, nomad_wagon, expedition, quests,
blacksmith, survivalist, resolve, sanitarium, activities, ...}`. `actors.json`: each hero's worn trinkets and
combat item slot.

## 2. DD2's side in a Kingdom-type game

### 2.1 The player inventory

- Made by the kingdom: `m_PlayerInventory = new PlayerItemInventory()` (`KingdomBhv.cs:659`), starting gold
  `KingdomRules.m_StartingGold` = 0 (`:763`, `kingdom_rules_data_export.Group.csv:24`).
- `PlayerItemInventory` (`Assets.Code.Player/PlayerItemInventory.cs:35-38`): base slots
  `InventoryRules.m_defaultSize` (20) plus the run stat `PLAYER_INVENTORY_MAX_SLOTS` (13 in `base_kingdom`;
  +2 for each of four profile unlocks the Estate's fresh profile does not have,
  `run_levels_data_export.Group.csv:87-114`); does not expand; **accepts overflow**.
- A stack holds `ItemDefinition.m_maxQty` plus the run stat `ITEM_MAX_QTY`
  (`RunDataManager.GetItemCalculatedMaxQty`, `RunDataManager.cs:159-168`); gold: 40.
- `AddItems` (`ItemInventory.cs:779-828`) tops up stacks, fills empty slots, **returns what did not fit** and
  raises `EventInventoryFull`. `AddItemsWithOverflow` (`:858-866`) and `TryTakeAllFrom` (`:1093-1137`) add
  slots past the limit instead; the inventory is then "overfilled" (`IsOverfilled`, `:61-67`).
- So the cap of 1320 (gotcha 46) is exact for an empty inventory, and it **falls by 40 for every slot
  something else holds**: a trinket bought while gold is low, a stack of materials, a combat item. The test
  estate showed 1320 "with 12 trinkets" because the gold went in first and the trinkets were pushed into
  overflow slots by `Trinkets.Give` (`Trinkets.cs:306`). In a played estate trinkets and fight loot take
  ordinary slots first, and the cap is lower and moves.
- Which loss is silent: the mod's own `AddGold` (quest pay, haul, sales, refunds). DD2's "Take All" does not
  lose gold (it overflows).
- Every item can be discarded (`ItemDefinition.m_canDiscard` defaults true, `ItemDefinition.cs:81`; no row of
  `item_data_export` clears it): the discard key on DD2's inventory screen works on `gold`
  (`PlayerInventoryItemBhv.cs:284-370, 789`).
- **INFERRED**: while the inventory is overfilled, DD2's inventory screen refuses to close outside a fight
  (`CommonUiBhv.HidePlayerInventoryInternal`, `CommonUiBhv.cs:1904-1910`, with
  `CanShowOverfilledInventory`, `:1421-1437`, true in the Estate's hub mode and on the results scene). The
  only way out is to discard: relic stacks or trinkets.
- Nothing of DD2 spends gold in an Estate session: selling needs an inn or a store node
  (`PlayerItemInventory.GetIsSellingActive`, `:244-271`), the hospital shop is only the fallback of 1.4.
- At every change of mode `GameTypeMgr.OnGameModeExitComplete` refreshes the slots (`GameTypeMgr.cs:340`);
  at every fight's end item durations tick (`PlayerItemInventory.cs:119-122`).

### 2.2 A fight's loot

1. `Battle.BattleEnd` (`Assets.Code.Combat/Battle.cs:194-204`) builds the `BattleResult`; its constructor
   collects the fight's loot tables (`BattleResult.cs:146-194`): the configuration's own, when the combat
   source says so (`BattleConfigurationDefinition.GetLootTables`, `:420-432`), plus every dead monster's
   `m_DeathLootIds` (`BattleTracking.cs:277`).
2. The Estate's combat source is made with **`useLootTables: true`** (`Dd2/EstateMode.cs:36-41`). Of the
   about 600 configurations `Data/dungeons.json` can reach (its own and those of the tables it names), 262
   pay `road_faction_rewards_all`, 105 `siege_rewards_all`, 68 `road_caves_rewards_all`, 8
   `lair_boss_rewards_all`, 55 nothing (among them the Darkest Dungeon's: "no loot window", MODLOG), the rest
   a dozen smaller tables of the same families. None has a table for an unfinished fight: a retreat pays
   nothing.
3. In a Kingdom-type game `road_faction_rewards_all` is `kingdoms_road_faction_rewards`
   (`loot_data_export_COMBATS.Group.csv:611-634`): a hero point (1 in 5), relics, baubles, 10 materials, the
   region's reward (none: `BiomeManager.CurrentBiomeLootReward` is null, `LootOption.cs:112-122`), a trinket
   (3 in 9). Which rows hold depends on the escalation the tier plays as (`DungeonContent.Escalation`,
   `DungeonContent.cs:149-157`). A lair boss (`kingdoms_lair_boss_rewards`, `:1458-1465`) adds flame points,
   combat items, inn items, stagecoach items, trophies.
4. `LootManager.AddLoot` (`LootManager.cs:130-305`): items wait in `m_NextWindowItems`; "provision" rewards
   change run values at once (`:158-181`): hero upgrade points, torch, coach armour and wheels.
5. The results scene (`GameModeType.RESULTS`) shows `LootScreenWidgetBhv` through `LootManager.ShowLoot`
   (`CombatResultsPresentationBhv.cs:91-111`, `LootManager.cs:340-432`). "Take All" is
   `PlayerInventory.TryTakeAllFrom(lootInventory)` (`LootUiControllerBhv.cs:319-404`). With nothing to show
   the results pass by themselves (`LootManager.cs:396-404`; seen in the Darkest Dungeon).
6. A hero who dies has the combat item and trinkets emptied into `m_NextWindowItems`
   (`ActorInstance.cs:1209-1217`, `LootManager.cs:335-338`). **INFERRED**: after a lost party the mod calls
   `ClearShowWindowVariables` (`RosterLifecycle.cs:441`), which does not clear that list
   (`LootManager.cs:434-443`), and the list is saved (`loot_manager`): the dead party's trinkets turn up on
   the loot window of the next fight anybody wins. The same holds for a death outside a fight.

### 2.3 DD2 screens and flows still alive in an Estate session

| Screen or flow | Where it can be reached | What it changes |
|---|---|---|
| Fight loot window, "Take All", "Leave items" | after every won fight with a loot table | purse (relics), unworn trinkets, every stray item; run values |
| Character sheet with inventory editing | hamlet: right click on a roster row and on a hero in any window; dungeon: right click on the banner's portrait; results scene: DD2's own keys and the loot window's buttons | worn trinkets from the **whole estate's stores, also inside a dungeon**; the hero's combat item; equipped skills |
| Player inventory screen | opens when a trinket or combat slot of the sheet is clicked (`TrinketInventoryItemBhv.cs:165-189`, `CharacterSheetStatsUiBhv.cs:820-827`), and by the Inventory key on the results scene (`CombatResultsPresentationBhv.cs:360-366`). Gate: `CommonUiBhv.GetCanShowPlayerInventory` (`CommonUiBhv.cs:1848-1865`), false only in a fight | shows the purse as relic stacks beside everything else; discard; sort; the overfill lock |
| Combat bar's item button | every fight | uses the hero's DD2 combat item |
| Torch widget of a fight | every fight | reads `TORCH` (the mod's copy of DD1's light) |
| Hospital shop | only the fallback of 1.4 | relics for DD2 items |
| Not reachable: stagecoach sheet, inn, trainer, store, altar, inn storage | - | - |

## 3. Overlaps and contradictions, by how much a player notices

| # | What the player meets | Why | Kind |
|---|---|---|---|
| 1 | **Two loot screens.** A curio pays through DD1's scroll into the 16-slot bag; a fight pays through DD2's window ("Take All") into nowhere the Estate shows. DD1's "Victory! You have found:" scroll does not exist | 2.2; `DungeonRun.OnReturnedFromFight` goes straight on (`:859-899`); ui-parity-raid N1 | a DD2 screen changing what DD1 tracks |
| 2 | **Gold that vanishes.** Quest pay, the haul, sales and refunds stop being added once the stacks have no room, with no word. The room shrinks with every DD2 item lying in the stores | 2.1; `EstateState.cs:100-107` | one system's limit binding the other |
| 3 | **Two sets of supplies.** Bandages, antivenom, herbs, holy water, laudanum and torches bought for the bag do nothing in a fight; the fight's item button uses DD2's `bandages`, `antivenom`, `medicinal_herbs`, `holy_water`, `laudanum`, `torch_consumable`: other objects, from another inventory, put on a hero on another screen | 1.2, 1.3 | one thing kept in two places |
| 4 | **A second inventory screen.** A click on a trinket slot of the hero sheet opens DD2's inventory: the purse as stacks of 40 relics that can be thrown away, the trinket stores without their DD1 cards, materials and other things the Estate never mentions; it can refuse to close | 2.1, 2.3 | two UIs for one store |
| 5 | **Fight loot skips the bag.** Relics won in a fight are in the purse at once: not lost with a lost party, not cut by DD1's keep rates, never asking for a slot. Trinkets from fights are in the estate's stores while the party is still in the dungeon | 2.2 | contradiction of rules |
| 6 | **Found trinkets are invisible, worn trinkets are changed from town's stores.** A trinket from a curio is a log line until the party is home; yet through DD2's sheet a hero in the dungeon can put on anything the estate owns | `DungeonRun.cs:65, 983-989`; 2.3; ui-parity-raid C9, D9 | two stores, wrong one reachable |
| 7 | **Rewards with no use.** "+1 Mastery Point", 10 materials, baubles, inn items, stagecoach items, a trophy, "+15 Flame": shown as loot, usable nowhere; each stack takes one of the purse's slots | 1.3 | DD2 currencies with no place |
| 8 | **Two gold units.** The bag says 500 and it is 10; food costs 1.5; a fight pays "4"; small sums round three different ways | 1.5 | one thing in two units |
| 9 | **The dead's gear.** A fallen hero's trinkets wait for DD2's next loot window, even after the whole party was lost; DD1 offers them at once and loses them with the party | 2.2 step 6 | contradiction of rules |
| 10 | **Light in two places.** A fight can change DD2's flame; DD1's light does not hear of it and overwrites it | `DungeonRun.cs:934-938` | one thing kept in two places |
| 11 | **One of each trinket.** DD2's possession limit silently swaps a reward trinket for another, or for gold | `Trinkets.cs:264-273`, `QuestBoard.cs:575-594` | one system's limit binding the other (a design call of the mod, D3) |
| 12 | The Sanitarium's fallback to DD2's hospital shop | `SanitariumPanel.cs:131-139` | a DD2 screen (rare) |

Not overlaps, but on the same ground: the provision bill is paid on setting out where DD1 takes gold as
things are bought (`ProvisionScreen.cs:28-29`); the tavern's "trinket won / lost" side effects are skipped
(`ActivityLedger.cs:273-274`). Both become easy once the stores are the mod's own.

## 4. The single model

### 4.1 Principles

1. **One ledger per thing, in the estate's save.** A rule of DD1 reads and writes that ledger and nothing else.
2. **DD2 containers are sockets, not stores.** Where the fight engine must find something in a DD2 container
   (worn trinkets, the combat item slot, `TORCH`), the mod fills it and is its only writer.
3. **DD2's player inventory is a transit box.** Whatever DD2 code drops into it (a trinket a hero may no
   longer wear: `TrinketItemInventory.UpdateEquipped`, `TrinketItemInventory.cs:58-77`; a combat item:
   `CombatItemInventory.UpdateEquipped`, `CombatItemInventory.cs:30-43`) is moved to the right ledger the next
   time the hub is entered. It is never shown.
4. **Nothing is granted that has no ledger.** No DD2 loot table is rolled for an Estate fight.

### 4.2 Thing by thing

| Thing | The one store | Writers | DD2's side |
|---|---|---|---|
| Gold | `EstateState.Current.Gold` (int), `estate_mod.gold`. `EstateState.Gold` and `AddGold` keep their names (every caller and `tools/campaign_bot.py` use them); `AddGold` cannot fail | as today | the `gold` item is never held; a stray one is swept into the purse |
| Heirlooms | as today | as today | - |
| Trinkets, unworn | `Trinkets` keeps the list itself: `sections.trinkets.stored` (ids in arrival order) beside `grades`. `Stored`, `InStore`, `Give`, `Sell`, `CanHold` (`Trinkets.cs:264-315, 435-447`) work on the list | as today, minus DD2 | player inventory holds none; strays are swept in |
| Trinkets, worn | the hero's `TrinketItemInventory` | `RealmInventory` only: from the stores in the hamlet, from the bag in a dungeon, never in a fight (DD1: `str_user_information_cant_unequip_trinket_in_battle`). Calls: `worn.AddItems` / `AddItemToSlot(new ItemInstance(def, 1), slot)` / `TakeItemQty` (`ItemInventory.cs:779, 830, 1033`) | DD2's sheet shows them, cannot change them |
| The bag | `Core.Inventory`, 16 slots, DD1 stacks | DD1's rules | - |
| Trinkets found | a bag item: `ItemTypes.Trinket`, one to a slot, id = DD2 trinket id, DD1 rarity kept with it; a card on the loot scroll and in the bag (DD1 rarity card with DD2's icon, as `RealmInventoryPanel` draws them) | `Stow`; worn or taken off between bag and hero outside a fight; to the stores at `BringHome` with DD1's keep rate | - |
| Fight loot | DD1's battle loot (4.3) into the bag through `DungeonHud.Loot` | `DungeonRun` after a won fight | off |
| Supplies in a fight | the bag | the fight's item use takes from the bag (4.3) | the heroes' combat slots mirror the bag during the fight, empty otherwise |
| DD2 combat items other than the six twins | owner's choice (4.6 Q2); recommended: bag items found as fight loot | - | - |
| Light | `Exploration.Light` | DD1's rules, plus the change DD2 made to `TORCH` during a fight, read back on return | socket |
| Hero upgrade points, materials, baubles, inn items, stagecoach items, deliverables, flame points from loot | none | never granted | removed on load, zeroed |
| Gear levels, skills, camping skills, resolve, quirks, stress | as today | as today | the hospital fallback removed |

### 4.3 Fights: the hand-over in both directions

**Into the fight.**
- Worn trinkets and skills are on the actor already.
- Light: `ApplyLight()` as now; `StartFight` remembers the value.
- Supplies: for every bag stack with a DD2 twin, each living party hero's `CombatItemInventory` is given the
  twin with the bag's count (the slot count is raised with the inventory's own `AddSlots` /
  `AddOverflowSlotSilently`, `ItemInventory.cs:426, 772`). DD2 has a debug path that does exactly this, every
  combat item on every hero (`RosterManager.cs:802-816`, `CombatItemInventory.cs:14`), so the combat bar is
  built to list several. **To see in game before building on it** (step 6a): how the bar lays out two to six
  items.

  | Bag item (DD1) | DD2 item used in the fight | DD1's effect, for comparison |
  |---|---|---|
  | bandage | `bandages` | cures bleed |
  | antivenom | `antivenom` | cures blight |
  | medicinal_herbs | `medicinal_herbs` | clears debuffs |
  | holy_water | `holy_water` | resistances for three turns |
  | laudanum | `laudanum` | cures horror |
  | torch | `torch_consumable` | light +25 |
  | food, shovel, key, firewood | none | not fight items |

- A use in the fight lowers that hero's stack (`EventInventoryItemQtyChange`); the mod takes one from the bag
  and sets the other heroes' stacks to the bag's new count. One bag, four views of it, as in DD1 where any
  hero uses any item of the shared inventory on their turn.
- If the bar cannot show more than one item: the fallback is one twin per hero, chosen by the mod (the hero's
  class item of `provision.json` first, else the largest stack), still taken from and returned to the bag.

**Out of the fight** (in `DungeonRun.OnReturnedFromFight`, `:859-899`, before `_x.ResolveFight`):
1. The heroes' combat slots are emptied; nothing is added back (the bag was kept in step during the fight).
2. Light: `Exploration.AddLight(TORCH now - TORCH at the start)`, then `ApplyLight()`.
3. `LootManager.NextWindowItems` is harvested with DD2's own public call
   (`ConvertNextWindowItemsIntoLootCollectResult`, `LootManager.cs:489-499`, which empties the list): trinkets
   of heroes who died go on DD1's "hero death" loot scroll (`str_overlay_loot_hero_death_*`) if the party
   lives, and are lost with a lost party (4.6 Q6); mirrored supplies are dropped (they were views).
4. Won: DD1's battle loot is drawn and shown on DD1's scroll under `str_overlay_loot_battle_title`
   ("Victory!") / `_description` ("You have found:"), then the exploration goes on. Fled or lost: nothing.

**DD1's battle loot for DD2's monsters.** DD1 gives every monster a loot code and count in its own file
(`monsters/*/*.info.darkest`, `loot: .code "A" .count 1`): `A x1` for 100 of them, `A x2` for 24 (the large
ones), `T x2` for 22 (bosses), `NONE` for the Darkest Dungeon's. After a fight each dead monster's code is
drawn (`LootTables.Draw`, `Core/LootTables.cs:84`), and in the dark the "battle" bonus adds `B` draws
(`LootTables.DarknessBonusCodes("battle", light, rng)`, `:95-104`: written, called from nowhere today). Table
`A` (`loot/common_overrides.loot.json`): gold 50, gems 38, heirlooms 48, supplies 28, a trinket 8, a journal
page 2. DD2's monsters have no such line, so the rule is data of the mod: a `loot` block in
`Data/dungeons.json`: per enemy of the fight's configuration `A x1`; a list of large enemy ids with `A x2`;
per story boss `T x2`; `NONE` for the Darkest Dungeon's fights (they have no DD2 table today either). A camp
ambush pays like a hallway fight.

**DD2's loot is switched off at its source**: `useLootTables: false` in `EstateMode.Register`
(`EstateMode.cs:40`), and a prefix on `LootManager.AddLoot` that skips it while an Estate session runs (the
monsters' own death loot and its run-value changes come through there too) and logs what was skipped. The
results scene then passes by itself, as it does today in the Darkest Dungeon.

### 4.4 Screens

| DD2 screen | In the Estate |
|---|---|
| Fight loot window | never opens (nothing to show). DD1's loot scroll in the corridor takes its place |
| Player inventory screen | never opens: a postfix on `CommonUiBhv.GetCanShowPlayerInventory` answers false while an Estate session runs. Every way in passes that gate (`TryShowPlayerInventory`, `TogglePlayerInventory`, `ShowInnPlayerInventory`, `ShowHospital`) |
| Character sheet | stays as the hero sheet (stats, skills, quirks, the skill loadout), with `isInventoryEditable: false`: the mod's two calls (`RosterPanel.cs:578`, `UpgradeUi.cs:264`) and a prefix on `CommonUiBhv.ShowCharacterSheet` for DD2's own keys. Trinkets change in the Trinket Inventory (town) and on the raid panel (dungeon) |
| Combat bar's items | shows the bag's supplies |
| Fight's torch widget | unchanged |
| Results scene (victory poses) | passes by itself; cutting it out is a separate, later choice (4.6 Q5) |
| Hospital shop fallback | removed; the Sanitarium says its rules could not be read |

Mod screens that change: the loot scroll gains a heading per source (battle, chest, camping, hero death:
ui-parity-raid N2) and trinket cards; the bag gains trinket cards; `RaidHeroPanel`'s two trinket cells take
clicks (bag to hero, hero to bag).

### 4.5 An existing save (`estate_mod.format` 1 to 2)

`EstateState.LoadFrom` (`EstateState.cs:133-155`) runs after DD2 has restored heroes, roster and inventory
(`EstatePersistence.Restore`, `EstatePersistence.cs:209-223`; hook at `Plugin.cs:47`). At its end, when the
section's `format` is below 2, one pass, the same code as the hub's sweep:

1. Gold: `Gold = PlayerInventory.GetGoldQty()`; the stacks are removed.
2. Trinkets in the player inventory: appended to `sections.trinkets.stored`, removed; their grades are kept.
3. `sections.expedition.trinkets`: into the bag as trinket items; what the 16 slots cannot take goes to the
   estate's stores at once (in the player's favour, once) and is written in the log. The owner's own estate is
   mid-expedition (MODLOG: week 12, in the Warrens), so this case is the first one met.
4. Combat items in the player inventory and in the heroes' slots: a twin joins the bag when an expedition is
   on, else is paid at DD1's sell value of its twin; the others by the answer to Q7.
5. Materials, baubles, inn items, stagecoach items, deliverables: removed. `HERO_UPGRADE_POINTS` set to 0.
   `LootManager.NextWindowItems`: trinkets to the stores, the rest dropped.
6. One line in the Activity Log names what was moved and what was removed. `format` becomes 2.

The pass changes memory only; the file changes at the next `EstatePersistence.SaveNow`, into a new numbered
snapshot, so the old one stays on disk until the game trims it. Before the first run on the owner's estate:
a copy with `tools/estate_slot.py` (standing rule). There is no way back: an older build reads a format-2
save as an estate with no gold and no unworn trinkets.

A new estate: `EstateSession.cs:100` becomes `EstateState.Current.Gold = 60`.

### 4.6 Choices that need the owner's word

| # | Choice | Options | Recommendation |
|---|---|---|---|
| Q1 | Gold's numbers | (a) keep estate gold = DD1 / 50; (b) DD1's own numbers (3000 to start, 1000 for a skill, 75 for a torch) | (b), as the last step. The scale was only there to fit relic stacks; with a number nothing asks for it, DD1's texts ("[Value: 1250 Gold Each]"), card art by amount (250 / 500 / 750 / 1000) and prices become literal, the fractions and the three roundings go. Old saves: purse and saved prices x 50 |
| Q2 | DD2's other combat items (fire bombs, smoke bombs ... 79 kinds beyond the six twins) | (a) not in the Estate; (b) bag items that fights can drop (a share of DD1's supply draws), usable in fights, sold at home; (c) also on the provision shelves | (b): "items are DD2's", and they then live by DD1's bag rules. (a) is the stricter DD1 reading and less work |
| Q3 | DD2's inn items | (a) gone; (b) usable at DD1's camp fire | (a) now; (b) can be added later without touching the model |
| Q4 | Trinkets: one of each (DD2's limit, today) or copies (DD1) | keep / lift | keep: DD2's trinkets are written as unique. With the stores the mod's own it is one line either way |
| Q5 | After a won fight | (a) DD2's results scene passes by itself, then the corridor with DD1's scroll; (b) straight back to the corridor (a patch on `CombatPresentationBhv.SetNextGameMode`) | (a) first: it is known to work. (b) is closer to DD1 and can follow |
| Q6 | Trinkets of the dead | DD1: offered as loot at once if the party lives, lost with a lost party (its option `keep_battle_quest_fail_trinkets` keeps them) | DD1's default |
| Q7 | DD2 things already lying in an existing estate's stores | (a) removed without pay; (b) combat items paid at DD1's sell value of a supply (5 to 25 of DD1's gold each), the rest removed | (b), with the log line of 4.5 |
| Q8 | Supplies used on a hero outside a fight (the mod's own +10% health, -1 stress) | keep / drop once they work in fights as DD2's items | drop: they were stand-ins. Food eaten between meals is DD1's and stays |

## 5. Work plan

Each step builds and can be checked alone. Sizes: S under a day, M one to two days, L more.
Every check starts from a copy of the save (`tools/estate_slot.py`, `tools/estate_pristine.py`).

**Step 0. A measuring stick (S).** A dev command `ledger.state` printing both sides: purse, heirlooms, stores,
the bag, the side list of trinkets; and from DD2: the player inventory (slots used, limit, overflow, items by
type), each hero's trinket and combat slots, `HERO_UPGRADE_POINTS`, `TORCH`, `LootManager.NextWindowItems`.
Read-only. Files: new `Estate/LedgerDev.cs`. Every later step is "run it before and after".
Until it exists the same can be read with `python tools/bridge.py get` / `members` on
`Assets.Code.Utils.Singleton` roots (see Tooling in MODLOG).

**Step 1. Gold is a number (S). Fixes gotcha 46.**
- Files: `Estate/EstateState.cs` (field, `Gold`, `AddGold`, `ToJson`, `LoadFrom`, `Format = 2`, the gold part
  of the migration), `Dd2/EstateSession.cs:100`, a small `Dd2/Dd2Sweep.cs` called where the dead are buried
  (`Dd2/RosterPatches.cs:101-108`, the hub is next): any `gold` item DD2's loot window put in the player
  inventory is added to the purse and removed. No caller of `AddGold` or `Gold` changes.
- Could break: an old build on the new save (4.5); `ActivityLog`'s gold memo (`ActivityLog.cs:292, 367`: it
  reads `EstateState.Gold`, fine); the pristine test estate (old format: migrated on load).
- See: `test.setup {"gold":3000}` gives 3000 (was 1320); `upgrade.grant gold=5000`; `wagon.sell`;
  `quests.finish index=0` pays in full; exit to the menu and continue: the number is back; `ledger.state`:
  no gold in DD2's inventory; after a forced win and "Take All" the relics are in the purse on return.

**Step 2. DD2's inventory screen and inventory editing are closed (S).**
- Files: new `Dd2/InventoryPatches.cs` (postfix `CommonUiBhv.GetCanShowPlayerInventory`; prefix
  `CommonUiBhv.ShowCharacterSheet` clearing `isInventoryEditable`), `Estate/UpgradeUi.cs:264`,
  `Estate/RosterPanel.cs:578`, `Estate/SanitariumPanel.cs:131-139` (fallback out).
- Could break: until step 5 a trinket cannot be changed inside a dungeon (it could, through DD2's sheet: the
  wrong store); DD2's "inventory full" path on the loot window (not taken on the results scene,
  `LootUiControllerBhv.cs:354-374`).
- See: right click a hero in the hamlet and in the dungeon: the sheet opens, its trinket and item slots do
  nothing; the Inventory key on the results scene does nothing; `python tools/click_test.py` (hamlet and
  `panels` parts) still passes.

**Step 3. The trinket stores are the mod's own (M).**
- Files: `Estate/Trinkets.cs` (the list, `Save`/`Load`, `Stored`, `InStore`, `Give`, `Sell`, `CanHold`
  counting the list, the bag and what living heroes wear), `Estate/RealmInventory.cs:68, 219-301` (moves
  between the list and the hero), `Estate/RosterLifecycle.cs:312-316` (a dismissed hero's trinkets),
  `Dd2/Dd2Sweep.cs` (trinkets found in the player inventory), the trinket part of the migration.
  `NomadWagon`, `QuestBoard`, `ActivityLog`, `RealmInventoryPanel` go through `Trinkets` and need no change.
- Could break: the order of the Trinket Inventory ("arrival" was DD2's slot order); a trinket DD2 takes off a
  hero by itself (party-only trinkets when the hero is benched) now waits in the transit box until the hub's
  sweep; rolls that asked DD2 whether a trinket is held.
- See: `trinkets.give`, `trinkets.roll`, `inventory.state`, `inventory.equip` / `unequip` / `give` /
  `unequip_all` / `sell`, `wagon.buy` / `wagon.sell`, `roster.dismiss` of a hero wearing two; `ledger.state`
  shows DD2's inventory empty; save and continue; `click_test.py panels` (click, drag, Shift+click).
- If the owner prefers DD2's inventory to stay the container: this step shrinks to "trinkets only, no limit"
  (a postfix on `ItemInventory.InventoryLimit` for the player inventory); steps 1, 2 and 4 make that safe too.

**Step 4. Fights pay through DD1's scroll into the bag (M).**
- 4a (Core, tested without the game): `Core/BattleLoot.cs`: from the fight's kind, its enemy ids and the
  light, the list of table codes; the `loot` block of `Data/dungeons.json`; tests beside
  `tests/CoreTests/InventoryTests.cs` (counts per kind, the darkness bonus rows against DD1's file).
- 4b: `Dd2/EstateMode.cs:40` (`useLootTables: false`); prefix on `LootManager.AddLoot` in
  `Dd2/InventoryPatches.cs`.
- 4c: `Dungeon/DungeonRun.cs`: `OnReturnedFromFight` (`:859-899`) and the ambush branch (`:883-893`) queue
  the loot before the exploration's own events; `Stow` (`:978-1012`) takes the heading's source;
  `Dungeon/RaidScrolls.cs` / `DungeonHud.Loot` (`DungeonHud.cs:191`) for the headings.
- Could break: the test scripts wait for DD2's "Take All" (`tools/play_dungeon.py`, `auto_fight.py`,
  `real_play.py`, `campaign_bot.py`): they must answer DD1's scroll (`dungeon.answer option=0` takes all,
  `hud.take`); a fight that ends the expedition (the quest's boss) must show its loot before the quest-complete
  window; the estate's income changes (fewer trinkets, more gems and heirlooms: DD1's mix).
- See: `estate.embark`, walk into a fight, force the win (`CombatBhv.ForceEndCombat(true)` through the
  bridge's `invoke`); no DD2 window; "Victory!" over the corridor; `dungeon.bag` holds the loot;
  `ledger.state`: DD2's inventory empty, hero upgrade points unchanged. A new dev command `fight.loot
  {"kind":"room","enemies":3,"light":20}` rolls the rule without a fight.

**Step 5. Trinkets are bag items and can be worn in the dungeon (M).**
- Files: `Core/InventoryItems.cs` (`ItemTypes.Trinket`, `ForLoot`, stack 1), `Core/Inventory.cs:140-170`
  (`Settle` hands trinkets back by keep rate), `Dungeon/DungeonRun.cs` (`_trinkets` goes: `:65, 142, 171-172,
  983-989, 1271-1277`; the hero-death scroll), `Dungeon/InventoryGrid.cs` and `RaidScrolls.cs` (the card),
  `Dungeon/RaidHeroPanel.cs:145-151, 216-228` (cells take clicks), `Estate/RealmInventory.cs` (the same moves
  with the bag as the other side), the `expedition.trinkets` part of the migration.
- Could break: a full bag (DD1: the trinket stays on the scroll until room is made); saves made between
  steps; the results screen's rows (dd1-raid-results.md: trinkets are not in the treasure row).
- See: `hud.loot {"items":"trinket:<id>:1"}` (the item finder must learn the type), `dungeon.bag`, new
  `dungeon.wear {"slot":N,"hero":guid}` / `dungeon.takeoff`; leave the quest: `inventory.state` has the trinket
  with its grade; abandon: DD1's keep rate; `roster.kill` in the dungeon: the hero-death scroll.

**Step 6. The bag's supplies work in a fight (L).**
- 6a spike, dev only: `fight.items {"items":"bandages:3,holy_water:2"}` puts DD2 items on the party's combat
  slots; one screenshot of the combat bar with one, three and six items decides between the mirror and the
  one-per-hero fallback (4.3).
- 6b: new `Dungeon/FightSupplies.cs` (fill at `StartFight` `:825` and `StartAmbush` `:1128`, follow
  `EventInventoryItemQtyChange`, empty at `OnReturnedFromFight`); the twin table in `Data/dungeons.json` or
  beside `InventoryRaidRules.SupplyUses` (`Core/InventoryRaidRules.cs:115-125`).
- 6c (if Q2 is (b)): DD2 combat items as bag items (`ItemTypes.Dd2Combat`, DD2's icon and tooltip, stack =
  DD2's `m_maxQty`), a share of DD1's `S` draws.
- Could break: a stack changed under DD2's feet in the middle of a turn; a hero dying with mirrored items
  (they go to `NextWindowItems`: step 4's harvest must drop them); `CombatItemInventory.UpdateEquipped`
  pushing an item a hero may not use into the transit box.
- See: `dungeon.give item=bandage amount=3`, `estate.battle config=... arena=...`: the bar shows bandages on
  every hero; one used; after the fight `dungeon.bag` says 2 and `ledger.state` shows empty combat slots.

**Step 7. Light both ways (S).** `Dungeon/DungeonRun.cs:825-848, 859-899, 934-938`. See: in a fight change
`TORCH` through the bridge (`RunValues.ChangeValue`), win: `dungeon.state` shows the light changed by as much;
`difficulty.state` shows both.

**Step 8. The leftovers (S).** The sweep's last rules and the migration's removals (4.5 steps 4-6); the dead
comments about relics (`EstateState.cs:13-14, 96`, `ActivityRules.cs:18`, `DungeonRun.cs:33`,
`InventoryContent.cs:15`); `docs/recon/quests-and-trinkets.md` D6, D8 and section 6 brought up to date.
See: `ledger.state` on a copy of the owner's estate after loading it: DD2's inventory empty, the log line
names what went.

**Step 9. Gold in DD1's numbers (M), only on the owner's yes to Q1.** One constant
(`ActivityRules.GoldScale`, `DungeonRun.GoldScale`, `InventoryContent.GoldScale`, `ProvisionShop.GoldScale`)
set to 1 and then removed with its three roundings; `InventoryText.Price` / `Amount`; migration x 50 of the
purse and of saved amounts (`sections.quests.offers[].gold`, `stays[].paid` of `sections.activities` and
`sections.sanitarium`); `EstateSummary`'s number width; the preview tools that repeat the arithmetic
(`tools/preview_building_panel.py`, `preview_wagon.py`, `preview_provision.py`, `preview_upgrades.py`); tests.
See: `activity.rules`, `wagon.state` (`dd1Price` = `price`), `provision.state`, `guild.state`, `smith.state`
against DD1's files.

**Step 10. The books agree (S).** With the results screens merged: "Collected Treasure" counts what
`BringHome` pays (fight loot is in the bag now, so it is all there); the Activity Log's "brought home" line
from the same record. A `tools/ledger_check.py` for `campaign_bot.py`: after every week DD2's inventory is
empty and the purse moved by exactly quest pay + haul - spending.

**Optional, small, once the stores are the mod's:** the provision screen takes gold as things are bought
(DD1); the tavern's "trinket won / lost" side effects (`ActivityLedger.cs:273-274`).

Order and dependencies: 0, 1, 2 are independent of everything and remove the two faults a player can lose
things to (silent gold loss, the discard key). 3 comes after 2 (else DD2's sheet opens an empty inventory).
4 needs 1. 5 needs 3 and 4. 6 needs 4. 9 last.

## 6. Dev bridge commands for this work

Existing (`AgentBridge.Register`): `estate.enter`, `estate.state`, `estate.save`, `estate.saveinfo`,
`test.setup`, `upgrade.grant` (heirlooms and gold), `heirlooms.state` / `trade` / `give`, `wagon.state` /
`buy` / `sell` / `restock`, `trinkets.state` / `inspect` / `roll` / `give`, `inventory.state` / `can` /
`equip` / `unequip` / `give` / `unequip_all` / `sell` / `open` / `click` / `sort`, `quests.finish`,
`quests.embark`, `quests.provision`, `provision.state` / `buy` / `sell` / `kit` / `setout`, `estate.embark`,
`estate.battle`, `estate.boss`, `dungeon.state` / `bag` / `give` / `use` / `drop` / `answer` / `torch` /
`leave`, `hud.state` / `loot` / `take` / `tab` / `select`, `camp.give` / `start`, `roster.dismiss` / `kill`,
`difficulty.state` (torch, escalation), `log.state`, `guild.state`, `smith.state`, `survivalist.state`,
`sanitarium.state`, `activity.state`; and the bridge's own `get` / `members` / `invoke` on any type.

To add: `ledger.state` (step 0), `ledger.sweep {"dry":true}` and `ledger.migrate {"dry":true}` (what would
move, without moving it), `fight.loot` (step 4), `dungeon.wear` / `dungeon.takeoff` (step 5), `fight.items`
(step 6a).

## 7. What this replaces in earlier notes

- `docs/recon/quests-and-trinkets.md` section 3 ("the estate's store = the party inventory"), D6 (a trinket
  DD2 handed out after a fight), D8 ("the estate has no such screen"), section 6 (curio trinkets "straight
  into the estate's stores").
- `docs/recon/dd1-missing-mechanics.md` rows "Trinket inventory ... gold stops being added", "Battle loot ...
  fights pay through DD2's own loot window", "Trinkets found on the way sit in the bag and can be worn".
- `docs/recon/ui-parity-raid.md` N1, N2, C9, D9.
- `docs/recon/dd1-raid-results.md` 6.3 ("the estate counts in DD2's relics, 1 for 50") if Q1 is (b).
- MODLOG gotcha 46.

## 8. Read from code, not yet seen in the game

1. The overfill lock of DD2's inventory screen in the hub and on the results scene (2.1).
2. Whether baubles drop in the Estate at all (biome conditions with no biome), and what
   `siege_rewards_all` pays there (not read row by row). `kingdoms_road_faction_rewards` is declared in two
   files (`loot_data_export_COMBATS` and `_MISC`); only the first was read.
3. A lost party's trinkets appearing on a later loot window (2.2 step 6).
4. The combat bar with several items on one hero (4.3, step 6a), and what DD2 does when a stack it shows
   changes from outside during a turn.
5. That the results scene passes by itself for every fight once nothing is granted (seen only for the
   Darkest Dungeon's fights), also when a hero died in the fight.
6. DD2 handing a party-only trinket back to the player inventory when its hero is benched in the hamlet
   (`TrinketItemInventory.UpdateEquipped`).
7. On the DD1 side: whether food can be eaten during a battle (the twin table says no); the exact loot codes
   DD1 gives its bosses beyond `T x2`.

## 9. What was built (2026-10-05): compiled and unit-tested, NOT yet seen in the game

Steps 0, 2, 3, 4, 5, 6a, 7, 8 and 10 are in (step 1 was in before; steps 6b, 6c and 9 are not). One commit a
step on the worker's branch. Nothing of it has run in the game: every line below wants a look through its
bridge command.

| Step | In the code | Check from outside |
|---|---|---|
| 0 | `Estate/LedgerDev.cs` | `ledger.state` (both sides; `clean`) |
| 2 | `Dd2/InventoryPatches.cs`; `UpgradeUi.ShowSheet`; the Sanitarium's fallback gone | `ledger.screens`, `ledger.dd2inventory`, `ledger.sheet` |
| 3 | `Trinkets.Store` (`sections.trinkets.stored`), `RealmInventory` the only writer of a hero's trinket slots, `Dd2/Dd2Sweep.cs`; `EstateState.Format` 3 | `inventory.*`, `trinkets.*`, `wagon.*`, `ledger.sweep`, `ledger.migrate`, `ledger.hold`, `ledger.stray`, `trinkets.oneofeach` |
| 4 | `Core/BattleLoot.cs` + `battle_loot` in `Data/dungeons.json` + tests; `Dd2/Dd2Loot.cs` (DD2's loot manager takes nothing; a dead hero's gear is kept from DD2's window); `EstateMode` `useLootTables: false`; `DungeonRun.FindBattleLoot`, `Dungeon/RaidLoot.cs` (headings by source, the find travels in the save) | `fight.loot`, `hud.loot source=battle`, `ledger.state` (`lootTurnedAway`, `fightPays`, `lootOnScroll`) |
| 5 | `ItemTypes.Trinket`, `Inventory.Settle` (`RaidHaul.Trinkets`), the card in `InventoryGrid`, `RealmInventory.WearFromBag` / `TakeOffToBag`, clicks in `DungeonHud` and `RaidHeroPanel`, the "Reclaimed:" scroll | `hud.loot items=trinket:<id>:1`, `dungeon.trinkets`, `dungeon.wear`, `dungeon.takeoff`, `dungeon.bag` |
| 6a | `Dungeon/FightDev.cs` | `fight.items` |
| 7 | `DungeonRun.NoteTorch` / `TakeLightBack` | `fight.torch`, `dungeon.state` (`books`), `difficulty.state` (`expeditionLight`) |
| 8 | the sweep's rules for combat items, everything else and hero upgrade points; the Activity Log line; `InventoryRaidRules.Dd2ItemOf` | `ledger.sweep heroes=true`, `ledger.pay`, `dungeon.choices` |
| 10 | `Dungeon/ExpeditionBooks.cs`, the purse's journal in `EstateState`, `RaidResults.AgreesWithThePurse`, the log's line from the record | `ledger.purse`, `ledger.books`, `results.state`, `tools/ledger_check.py` |

**Where the build left the plan, and why:**
- 4.2 named `AddItemToSlot` for putting a trinket into a named slot. It does not tell the inventory that a
  quantity changed, and a trinket's effects hang on that (`DataItemInventory.OnItemQtyChange`): the call used is
  `SwapItems(new ItemInstance(def, 1), slot)`, which also hands back what was in the slot.
- 4.3 harvested DD2's loot list when the hub is back. That is too late: with a dead hero's trinkets in it DD2's
  loot window opens on the results scene. The list is never filled instead (a prefix on
  `LootManager.EmptyInventoryIntoNextWindowLoot`); what a save still carries there is taken out by the sweep.
- 4.3's "list of large enemy ids" is not a list: DD2's own size of the monster's class (`m_Size`) says it.
  New beside it: an enemy the fight is won without beating (`m_IsBattleComplete`: cover, a barricade, a
  corpse) pays nothing.
- The wanderers came into `dev` while step 4 was built. Their fights pay as boss fights do (the wanderer
  once, what stands with it nothing: DD1 writes `NONE` for the Collector's heads and the Shambler's tentacles),
  and the two DD1 has a monster for pay DD1's own lines, `battle_loot.bosses` in `Data/dungeons.json`: the
  Collector `COLLECTOR x1` (a trapezohedron three times in four, else a trinket of DD1's rarity "collector"),
  the Shambler `SHAMBLER x1` ("ancestral_shambler"), `B x3`, `T x1`. Those two rarities are drawn from DD2's
  own tables of the same monsters (`Trinkets.MonsterTables`: `TRINKETS_COLLECTOR_BOSS`,
  `TRINKETS_SHAMBLER_BOSS`) and keep the DD1 rarity as their grade (DD1 has a price and a card backing for
  both). Check: `fight.loot kind=boss boss=collector enemies=0` (`pays`, `rule.bosses`, `monsterTrinkets`).
- The save's format is 3, once, for all of it; every part of the migration goes by what lies where, not by
  the number, and runs after every load.
- Q8's stand-ins are behind their switch but still ON: the supplies do nothing in a fight yet (step 6b), and
  with the stand-ins out five things the provision shop sells would do nothing on a hero at all.
- Section 8's point 3 (a lost party's trinkets on a later loot window) cannot happen any more; points 1, 2, 5
  and 6 are still to be seen in the game.

**The owner's eight choices, each one switch:** Q1 `DungeonRun.GoldScale` (50; step 9 is more than the number);
Q2 nothing built (DD2's other combat items are not in the Estate; one found in a save is paid for, Q7); Q3
inn items removed; Q4 `Trinkets.OneOfEach` (true); Q5 the results scene passes by itself (no switch: (b) is a
patch nobody has tried); Q6 `DungeonRun.KeepTrinketsOfALostParty` (false); Q7
`Dd2Sweep.PayForStrayCombatItems` (true); Q8 `InventoryRaidRules.StandInSupplyUses` (true, see above).
`dungeon.choices` shows them all.

**From DD1's own frames** (`_lab/dd1_ref/raid/`): the loot scroll hangs at `sidebar_scroll.pos` (its picture's
corner at 1120,200), its two buttons stand 22 px left and 10 px up of `take_all_pos` / `close_pos` and have no
words under them; a trinket's card in the bag has no number. All in `RaidScrolls.ShowLoot`, `InventoryGrid`
and `tools/preview_raid_hud.py`.

## 10. Step 9 built (2026-10-06): gold in DD1's numbers; Q4 as a setting

The owner's yes to Q1 (b). Seen in the game on the test estate and on the owner's format-3 estate of that day.

- **The scale is gone, not set to 1.** `DungeonRun.GoldScale`, `ActivityRules.GoldScale`,
  `InventoryContent.GoldScale` / `ToEstateGold`, `ProvisionShop.GoldScale` / `CostDd1`,
  `DistrictRules.EstateGold` and `InventoryText.Price` no longer exist. What is left of the three roundings is
  `ActivityRules.WholeGold` (halves up) for a price that a multiplier or a discount leaves with a fraction.
  Sections 1.5 and 4.6 above describe the state before this.
- **A new estate** starts with `EstateState.StartingGold`: DD1's pay of `plot_tutorial_crypts`, 3000 (DD1's own
  wallet starts empty; its hamlet is first seen with that pay in it).
- **The save's format is 4.** `Core/EstateSaveRules.BroughtOver` multiplies, on a copy of the section, every
  amount an older save holds: `gold`, `sections.quests.offers[].gold`, `sections.expedition.quest.gold`,
  `stays[].paid` of `sections.activities` and `sections.sanitarium`, `sections.activity_log.expedition.gold`,
  and the sums written in words in the log's entries and the week's reports ("bought for 30 gold" becomes
  "bought for 1500 gold"). The bag's gold was DD1's already. One line in the Activity Log says so.
  `tests/CoreTests/EstateSaveTests.cs`; `ESTATE_SAVE=<kingdom.json>` runs it on a real file.
- **DD2's own gold** found in DD2's inventory (the purse of a format-1 save; nothing of DD2's pays gold in the
  Estate) is worth `EstateState.Dd2GoldWorth` = 50 each as the sweep moves it into the purse.
- **Q4 is a setting**, `[Rules] UniqueTrinkets` (true): never two of the same trinket. The rule of a draw is
  `Core/TrinketDraws`; `Trinkets.CanHold` no longer asks DD2's possession limit (one is one). A find's trinket
  that cannot be drawn is paid in coin at DD1's sell value of its rarity (`Trinkets.CoinInstead`), as a quest's
  reward already was.
- Check from outside: `ledger.state` (`savedFormat`, `format` 4), `dungeon.choices` (`dd2GoldWorth`,
  `startingGold`), `wagon.state` (`dd1Price` = `price` before any discount), `provision.state`, `ledger.books`,
  `python tools/ledger_check.py`, `trinkets.draws`, `trinkets.state` (`duplicates`).

## 10. Step 6b as built (2026-10-06): the bag in a fight, a bar of the mod's own

Step 6a had shown that DD2's combat bar has one item slot a hero, so the bag is not mirrored onto DD2's combat
items (4.3's first plan). The owner asked for the other way (batch of 2026-10-06, picture 27): a button by the
skill bar that puts the bag in the bar's place. DD2's combat-item slots stay empty; nothing of 4.3's "into the
fight" hand-over for supplies is done, and the twin table (`InventoryRaidRules.Dd2ItemOf`) is used only by the
sweep, as before.

**Rules** (`Core/FightItems.cs`, tests `FightItemTests`). Read from DD1:
- which supplies a fight has a use for and what they do: the effects of `effects/base.effects.darkest` that
  carry `.item 1` (`bandage` `.cure_bleed`, `antivenom` `.cure_poison`, `medicinal_herbs` `.clear_debuff`,
  `laudanum` `.clearDotStress`, `holy_water` `.duration 3 .buff_ids holy_water_*_resist`: four resistances of
  0.33 in `shared/buffs/base.buffs.json`; `dog_treats` is there too and is no item of this mod's heroes);
- on whom: every one of them has `.target "performer"`, and DD1's own help says "on the current hero"
  (`str_help_raid_combat_3`): the hero whose turn it is, nobody else;
- the torch (the game's code; refused in DD1's words `str_cant_use_torch_at_limit`,
  `str_cant_use_torch_during_ambush`) and food (`provision_hp_heal`, `max_provisions_before_full`; the corridor's
  count of what a hero has eaten goes on in the fight);
- everything else: `str_user_information_cant_use_items_in_battle`.
Not in data, DD1 behaviour: an item is no action of the turn; an item that would do nothing now is grey and is
not spent (seen in DD1's frames of a rested party). This answers section 8's point 7: food is eaten in a
battle (the twin table said no because DD2 has no such combat item).

**On DD2's fighting actor** (`Dungeon/FightBagRun.cs`, `DungeonRun.UseItemInFight`): bleed and blight are
DD2's dots of those types, horror is every dot that deals stress (`horror_dot_*`), debuffs are DD2's shown
negative tokens (blind, daze, immobilize, taunt, vulnerable, weak ...), holy water's buffs are a stats
container for three rounds (`Dungeon/FightBagBuffs.cs`, counted down by `EventBattleStartRound`, taken off when
the fight is over), a torch is `RunValues.ChangeValue(TORCH, +25)` and comes home through `TakeLightBack`.
Source type `INVENTORY`. Beside DD1's effect an item does what it does on a hero between fights (Q8's
stand-ins: a share of health, a point of stress) while `FightItemRules.StandInsInAFight` is on
(config `[Fight] BagStandIns`, true); `InventoryRaidRules.StandInSupplyUses = false` takes them out of both.

**On screen** (`Dungeon/FightBag.cs`, `FightBagView.cs`): a canvas of the mod's own at order 2, between DD2's
`CombatUI` (1) and its screens (10), pop texts (25), menus (30) and tooltips (100). The button is one more
place of DD2's bar before its first skill button: the plate and the pointer mark of DD2's "move" button, read
from that button, with DD2's inventory sign `icon_inventory` on it. The bag is the cells of DD1's
`panels/panel_inventory.png` at 0.9 with `InventoryGrid` on them, placed by the owner's picture (first card at
675, 797 of 1920x1080), on a black ground over DD2's skill buttons. It is there only while DD2's bar is (a
hero's turn waits for input, the bar's buttons are up, no menu or sheet of DD2's is open), with the bar's alpha.
A turn starts on the skills; a skill chosen (its key) brings the skills back; TAB toggles (in DD2 that key is
bound only to the driving map). Trinkets, gold, gems and heirlooms are shown and not used; nothing is thrown
away and no card is carried in a fight.

Bridge: `fightbag.state`, `fightbag.show`, `fightbag.use`, `fightbag.click`, `fightbag.hover`,
`fightbag.fight` (a hallway fight on the spot; `ambush=true`), `fightbag.afflict`, `fightbag.dots`,
`fightbag.tokens`, `fightbag.hud`, `fightbag.canvases`, `fightbag.sprites`, `fightbag.keys`, `fightbag.place`.
