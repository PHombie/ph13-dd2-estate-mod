# Quest board, trinket rewards and the Nomad Wagon

What DD1 rule each number comes from, which DD2 call each trinket operation uses, and which steps are the
mod's own decisions. DD1 paths are relative to the DD1 install (read at runtime, nothing is copied); DD2
references are `file:line` in `_ref/dd2-decomp/IronCrown/` (v2.04.85095) or a data table under
`StreamingAssets/Excel/`.

Code: `Core/QuestGenerationRules.cs`, `Core/QuestGeneration.cs`, `Core/QuestRewards.cs`,
`Core/QuestRewardsTrinkets.cs` (pure rules, tested by `tests/CoreTests/QuestGenerationTests.cs`),
`Estate/QuestBoard.cs`, `Estate/Trinkets.cs`, `Estate/NomadWagon.cs`, `Estate/NomadWagonPanel.cs`,
`Estate/QuestPanel.cs` + `QuestMapParts.cs` + `QuestMapText.cs` (the estate map), dev commands in
`Estate/QuestMapDev.cs` and `Estate/NomadWagonDev.cs`. Offline previews: `tools/preview_quest_map.py`,
`tools/preview_wagon.py`.

Estate gold = DD1 gold / 50 everywhere (`ActivityRules.ToGold`, `UpgradeRules.Price`).

## 1. DD1 numbers and where they are read

| Rule | DD1 source (file, key) | Stock value | Read by |
|---|---|---|---|
| Regions that generate quests and when | `campaign/quest/quest.generation.json:5` `generation.dungeon.generated_dungeons[].required_number_of_quests_finished` | crypts 1, weald 3, warrens 4, cove 4 | `QuestGenerationRules.Dungeons` |
| Most generated quests in one region | same file `:4` `generated_quests_max_threshold` | 4 | `QuestGenerationRules.MaxPerDungeon` |
| Difficulties the roster calls for | same file `:25` `generation.difficulty.generated_resolve_level_difficulties` | resolve 0-2: difficulty 1; 2-4: 3; 4-6: 5 | `QuestGenerationRules.Difficulties`, `DifficultyWeights` |
| Quest types and lengths per region and region level | same file `:53` `generation.type.available_quests_table[].generated_quest_table[level][]` (`type`, `length`, `chance`) | level 0: cleanse short; 1: explore/cleanse, short/medium; 2: + inventory_activate and gather, medium; 3-7: + explore/cleanse long (all chances 1) | `QuestGenerationRules.TemplatesFor` |
| Quests per town visit | `campaign/quest/number.quest.generation.json:4` `number_of_quests_per_town_visit_table` | 2, 6, 8, 9, 10, 11, 12, 13 | `QuestGenerationRules.QuestCount` |
| Points a finished quest gives its region | `campaign/progression/progression.json:4` `dungeon.quest_completion_xp_table` (by length) | 0, 2, 3, 4, 5, 6 | `QuestGenerationRules.DungeonXp` |
| Region levels | same file `:14` `dungeon.level_threshold_table` | 0, 2, 6, 10, 16, 22, 32, 42 | `QuestGenerationRules.DungeonLevel` |
| Gold of a generated quest | `quest.generation.json:1306` `generation.rewards.item_table[difficulty][length]` | d1: 3000/4500/7500; d3: 4500/6750/11250; d5: 6000/9000/15000; d6: 9000/13500/22500 | `QuestRewardRules.Gold` |
| Heirloom types a region pays in | same file `:1161` `rewards.heirloom_type_map` | all four regions: bust, portrait, deed, crest | `QuestRewardRules.HeirloomTypes` |
| Heirloom amounts | same file `:1199` `rewards.heirloom_amount_table[type].amounts[difficulty][length]` | bust/portrait d1 2/2/4, d3 2/3/6, d5 3/5/9; deed/crest d1 3/5/9, d3 4/6/12, d5 6/9/18 | `QuestRewardRules.HeirloomAmount` |
| Rarity of the reward trinket | same file `:1466` `rewards.trinket_chance_table[rarity].chances[difficulty][length]` | d1: common / uncommon / rare; d3: uncommon / rare / very_rare; d5: rare / very_rare / ancestral | `QuestRewardRules.TrinketChances` |
| Resolve experience | same file `:1408` `rewards.resolve_xp_table` | d1 2/3/4, d3 4/6/8, d5 8/12/16 | `Estate/Resolve.cs` (not changed here) |
| Plot quest rewards and rules | `campaign/quest/quest.plot_quests.json` `plot_quests[]`: `quest.completion_reward.items_definition.items` (gold, heirloom, trinket), `additional_trinket_completion_rewards`, `completion_dungeon_xp`, `dungeon_level`, `can_retreat`, `retreat_party_kill_count`, `is_roster_stress_cleared_on_completion` | e.g. `plot_kill_necromancer_1`: 4500 gold, 4 bust, 6 crest, 1 very_rare trinket, no region points | `QuestRewardRules.Plot` |
| Stress of an abandoned quest | `campaign/quest/quest.exit_penalty.json:2` `fail_penalty.stress_damage` (`regroup_penalty` is 0) | 20 (of DD1's 200) | `Core/RaidRules.AbandonStress` (already there) |
| What an abandoned quest keeps of the bag | `campaign/estate/estate.json:26` `quest_fail_keep_rates` | gold, trinket, heirloom, gem 1.0; provision, supply 0.25; quest_item 0 | `Core/InventoryRaidRules.KeepRate` (already there) |
| Level caps of heroes per difficulty | `campaign/quest/quest.restriction.json` `resolve_level_threshold_table` | 2, 2, 3, 4, 5, 99, 99 | `Estate/Resolve.cs` (not changed here) |
| Trinket rarities | `trinkets/base.rarities.trinkets.json` `rarities[]` | 14, six of them general: very_common ... ancestral | `TrinketRules.Rarities`, `Ladder` |
| Price of a trinket by rarity | `trinkets/base.entries.trinkets.json` `entries[].price` (every trinket of a rarity carries the same) | 5000 / 7500 / 10000 / 15000 / 25000 / 50000; trophy 0 | `TrinketRules.Price` |
| Wagon stock size | `campaign/town/buildings/nomad_wagon/nomad_wagon.building.json:18` `number_of_trinkets_upgrades` | 2, then 4 / 6 / 8 / 12 with `nomad_wagon.numitems` a..d | `UpgradeRules.Value` in `NomadWagon.StockSize` |
| Wagon discount | same file `:27` `trinket_cost_discount_upgrades` | 10% per step of `nomad_wagon.cost` a..e | `UpgradeRules.Price` / `Discount` |
| Wagon rarity mix | same file `:41` `rarity_generation_table` | very_common 6, common 5, uncommon 4, rare 2, very_rare 1 | `TrinketRules.RollWagonStock` |
| What the wagon pays for a trinket | same file `:36` `trinket_sell_value_discount_upgrades[0].discount_percent` | 0.85: a trinket sells for 15% of its price | `TrinketRules.SellValue` |
| Wagon screen layout | `campaign/town/buildings/nomad_wagon/nomad_wagon.layout.darkest` (`inventory_system_background_layout.pos` 230 150; grid: 6 columns, `start_pos` 55 -10, `offset` 100 180), `campaign/town/buildings/building.layout.darkest` (`building_base_layout.body_base_pos` 596 102), backdrop at `town_background_layout.area_pos` 144 132 | | `NomadWagonPanel.Build` |
| Card of a rarity | `panels/icons_equip/trinket/rarity_<rarity>.png` (72x144) | | `Trinkets.RarityArt` |
| Words | `localization/miscellaneous.string_table.xml`: `town_quest_goal_start_*`, `str_inventory_title_quest_item*`, `trinket_rarity_*`, `town_name_realm_inventory`, `realm_inventory_sell_trinket_confirm_question_format` | | `QuestMapText` |

Resulting estate prices (no discount): buy 100 / 150 / 200 / 300 / 500 (/ 1000 ancestral), sell 15 / 23 / 30 /
45 / 75 / 150. Quest gold: apprentice 60 / 90 / 150, veteran 90 / 135 / 225, champion 120 / 180 / 300.

Tests compare every table above with the raw DD1 file (`dotnet run --project tests/CoreTests -c Release --
--only QuestGeneration`, 12 tests).

## 2. How a week's board is rolled (`QuestGeneration.Roll`)

DD1's tables are data; how DD1's code walks them is not. Steps marked READING are the mod's reading of DD1's
behaviour and are the things to compare with DD1 itself.

1. A region generates quests once `quests finished >= required_number_of_quests_finished`.
2. The week offers `number_of_quests_per_town_visit_table[n]` quests. **READING:** `n` = quests finished - 1.
   DD1's first town visit comes after its tutorial (one quest finished) and reads the first entry (2); with
   no failed quest, quests finished and town visits rise together. If DD1 really counts visits, a failed
   week would move DD1 one row further than the mod.
3. **READING:** every open region gets one quest first, the rest go to regions drawn at random, and none
   holds more than `generated_quests_max_threshold` (4; DD1's map draws four markers to a row,
   `quest_number_of_quests_in_row`). So the week after the first quest has 2 quests in the Ruins, the next
   4 (the table asks for 6, one region holds 4), the third 8 over the Ruins and the Weald, and from 4 quests
   finished the four regions share 9 to 13.
4. Type and length: drawn from the region's row for its level by `chance`. Difficulty: one of those the
   roster calls for. **READING:** drawn in proportion to the heroes in each band (a level-2 hero counts for
   apprentice and veteran), and a region does not get the same type + length + difficulty twice while
   another combination is left.
5. Reward: gold and trinket rarity straight from the tables. **READING:** a generated quest pays in two of
   its region's heirloom types, drawn at random (`QuestRewardRules.HeirloomTypesPerQuest`); the file lists
   the types and the amount of each, the number shown is in DD1's code (its reward grid has four columns:
   gold, two heirlooms, the trinket).
6. Rows the mod cannot use are reported, never dropped silently: a quest type outside
   `Core.QuestTypes.All` (explore, cleanse, gather, activate, inventory_activate, kill_boss: what
   `DungeonGenerator` builds), a region without content. They are in `QuestRoll.Unsupported`, logged as
   warnings (`quest board: not offered: ...`) and shown by `quests.rules` / `quests.offers` (`notOffered`).
   Stock DD1 has none; DLC rows (`farm_bloodcrate`, `kill_statue`) would be.

Region level: a finished quest adds `quest_completion_xp_table[length]` points (2 / 3 / 4), not 1 as
before; DD1's boss quests add none (`completion_dungeon_xp: false`), its tutorial does. The first story
quest stands for the tutorial, so after it the Ruins are level 1 and one quest is finished: DD1's first week.

Story quests (`Data/plot_quests.json`) keep their unlock rules (`unlock.requires`, `unlock.dungeon_level`,
the region's own gate in `Data/dungeons.json`). Each is paid what the DD1 quest in its `dd1_quest_id` pays
and takes that quest's rules: no region points for boss quests, `can_retreat`, `retreat_party_kill_count`,
`is_roster_stress_cleared_on_completion` (applied by `QuestBoard.Finished`: every living hero's stress to 0).

A region is open on the map when DD1 generates quests there or the mod's own gate is open (the Ruins from
the start, for the first story quest).

Failure: `QuestBoard.Finished(quest, false)` pays nothing and counts nothing. The stress of giving up
(`fail_penalty`) and what the bag keeps (`quest_fail_keep_rates`) were already applied by
`DungeonRun.Finish` / `BringHome`; they are DD1's and need no change.

Save section `quests`: `week`, `offers` (with `trinkets`, `dungeon_xp`, `can_retreat`, `retreat_deaths`,
`clears_stress`), `xp` (points per region), `finished`. A save from before (`done`: quests per region) is
read as so many short quests and the week is rolled again.

## 3. Trinkets: DD1 rarity, DD2 item

DD2 facts:

| What | Where |
|---|---|
| A trinket is an `ItemDefinition` with `m_type == ItemType.TRINKET` | `Assets.Code.Item/ItemDefinition.cs:73`, `ItemType.cs:16` |
| Its rarity is its sub type (`common`, `rare`, `epic`, `ancestral`, `cultist`); the `Rarity` enum is not used for items | `ItemDefinition.cs:187` (`SubType`), `ItemSubType.cs:10`, `Excel/item_subtype_data_export.Group.csv:6-27`; the game itself reports `SubType.m_Id` as "trinketRarity" (`Assets.Code.Analytics.Events/AnalyticsTitleEvent.cs:174`) |
| 211 trinkets: common 12, rare 49, epic 125 (15 from DLC), cultist 22, ancestral 3; each `m_possessionLimit 1` | `Excel/trinkets_data_export.Group.csv`, `trinkets_actor_effect_trigger_data_export.Group.csv`, `ItemDefinition.cs:79` |
| The estate's store (since "one inventory", docs/recon/inventory-unification.md) | a list of the estate's own: `Trinkets.Store`, saved as `sections.trinkets.stored` (ids in the order they came; DD1's `trinket_storage`: 9999 slots). DD2's party inventory (`GameTypeMgr.PlayerInventory`) holds none: what DD2's own code drops there is swept into the list (`Dd2/Dd2Sweep.cs`), also once after loading a save from before |
| Give, take | `Trinkets.Give(id, dd1Rarity)`, `Put(id)`, `Take(id)`, `InStore(id)`, `Stored()` |
| Worn | the hero's DD2 trinket slots, written by `RealmInventory` alone: `AddItems(def, 1, false)` for the first free slot, `SwapItems(new ItemInstance(def, 1), slot)` for a named one (it hands back what was there; `AddItemToSlot` does not tell the inventory that a quantity changed, and a trinket's effects hang on that), `TakeItemQty(index, 1)` to take off |
| On an expedition | a trinket found is an item of the bag (`ItemTypes.Trinket`, one to a slot) and changes hands with a hero of the party outside a fight (`RealmInventory.WearFromBag`, `TakeOffToBag`) |
| A dismissed hero's trinkets | `RealmInventory.HandBack(actor)`: popped off the hero into the list (`RosterLifecycle.Dismiss`) |
| Possession limit | `Trinkets.CanHold(id)`: DD2's `m_possessionLimit` of the item against `Trinkets.Held(id)` = the stores + the expedition's bag and loot scroll + what the living wear. One switch, `Trinkets.OneOfEach` (the owner's choice Q4), lifts it |
| Everything a loot table can give right now | `LibraryLoot.CollectAllPossibleLoot(id, isValidOnly, set)` `LibraryLoot.cs:141` -> `Table.CollectAllPossible` `Assets.Code.Math.Randomizer/Table.cs:360` -> `LootOption.CollectAllPossible` `Assets.Code.Loot/LootOption.cs:193`; validity (conditions, unlocks, DLC) `LootOption.GetIsValidOption` `:234` |
| The shop tables the old wagon used | `hoarder_shop_trinkets_common/rare/epic`, `Excel/loot_data_export_NODESandROAD.Group.csv:29-52` |
| Name | `ItemDescription.GetTitle(def, qty)` `Assets.Code.Item/ItemDescription.cs:48` (`item_name_<id>`) |
| Tooltip text | `ItemDescription.GetDescription(def, qty, includeRunStatModification, durationAmount, canSell, showDiscard, hideTitle, ...)` `ItemDescription.cs:135`; the native tooltip calls it in `Assets.Code.UI.Tooltips/ItemTooltipBhv.cs:54` |
| Icon | an addressable prefab: `InventoryUiUtils.IsItemIconLoaded(def)` / `GetItemIconPrefab(def)` `Assets.Code.UI.Items/InventoryUiUtils.cs:152`, `:121` (`ItemLoaderBhv.TryGetIfReady` `ItemLoaderBhv.cs:12`, `AddressableReferencesManager.GetHandle` `Assets.Code.Resource/AddressableReferencesManager.cs:265`). The sprite is read the way `AltarMemoryBhv.LoadIcon` does (`Assets.Code.ui/AltarMemoryBhv.cs:197`): spawn from the pool, `VariableAppearanceBhv.SetAppearance(1f)`, `GetActiveChild().GetComponent<Image>().sprite` (`Assets.Code.Utils.Behaviors/VariableTAppearanceBhv.cs:150`, `:120`), recycle |
| Sale question | `CommonUiBhv.ShowConfirmationDialog` `Assets.Code.UI.Managers/CommonUiBhv.cs:2580` |
| The native store the wagon no longer opens | `CommonUiBhv.ShowDrivingStore` `CommonUiBhv.cs:2303` |

### Design calls (the mod's own)

**D1. Rarity map** (`Trinkets.SubTypes`). DD1 has five general steps and "ancestral", DD2 three and
"ancestral":

| DD1 | DD2 sub type |
|---|---|
| very_common, common (DD1 shows both as "Common") | common |
| uncommon, rare | rare |
| very_rare | epic |
| ancestral | ancestral |

**D2. Pools** (`Trinkets.Pools`): the loot tables DD2's own trinket shop and generic trinket loot draw
from, with the region tables named outright because the estate is in no DD2 region (the shop gates them by
biome): common `TRINKETS_COMMODITY_COMMON`; rare `TRINKETS_COMMODITY_RARE`, `TRINKETS_GENERAL_ALL`,
`TRINKETS_<CAVE|CITY|COAST|FARM|FOREST|TUNDRA>_RARE`; epic `TRINKETS_COMMODITY_EPIC`, the six `_EPIC` region
tables, `TRINKETS_CATACOMBS_EPIC` (DLC), `TRINKETS_HERO_ALL_UNCONDITIONAL` + `TRINKETS_HERO_ALL` (class
trinkets whoever is on the roster, as DD1's wagon sells class trinkets), `TRINKETS_HOARDER`; ancestral
`TRINKETS_ANCESTOR_STATUE` (`Excel/loot_data_export_TRINKETS.Group.csv:41-65, 209-217, 417, 457`). Left out:
boss, collector, shambler, death, curio, cultist tables: DD2 keeps those for their own fights, as DD1 keeps
its "battle" rarities out of quest rewards. A table the build or its DLC lacks is skipped; an item whose own
sub type is not the pool's is left out. The draw is uniform over the pool with the mod's `Rng`.

**D3. One of each.** DD2's possession limit is kept (`Trinkets.OneOfEach`, a switch since the stores are
the estate's own; DD1 allows copies): a trinket the estate holds (stores, the expedition's bag, worn) is
never drawn. When a rarity's pool is spent the nearest other rarity is drawn (never the Ancestor's unless asked
for); when nothing is left a reward pays DD1's sell value of the rarity in gold instead.

**D4. The reward trinket is shown.** DD1 shows the actual trinket on the quest; so the DD2 trinket is drawn
when the week is rolled, saved with the offer and shown on the map (DD1's card of the rarity, DD2's icon,
DD2's tooltip text). It is given by `QuestBoard.Finished` on success; if the estate came to hold it in the
meantime, another of the rarity is drawn. Quest rewards and wagon wares of one week never show the same
trinket.

**D5. Boss trophies.** DD1's champion boss quests name a trophy (`boss_necromancer` ...). The mod gives the
DD2 boss's own trinket where DD2 has a table for the boss standing in (`Trinkets.BossTables`: librarian
`TRINKETS_CITY_BOSS`, dreaming_general `TRINKETS_FOREST_BOSS`, harvest_child `TRINKETS_FARM_BOSS`,
leviathan `TRINKETS_COAST_BOSS`, exemplar `TRINKETS_CULTIST`, meat_hook `TRINKETS_WARLORD_BOSS`), else a
very rare one. A trophy has no price in DD1 and cannot be sold. DD1's `trinket_unlock` (the Talisman of the
Flame of `plot_darkest_dungeon_1`) has no counterpart and is only logged.

**D6. Grade.** A trinket that came through a DD1 rule keeps the DD1 rarity it came in under
(`Trinkets.Grade`, save section `trinkets`): that is the card it is drawn on and what the wagon pays for it.
Every trinket comes through a DD1 rule now: DD2 hands out none in the Estate (a fight pays DD1's battle loot,
whose trinkets are drawn for a DD1 rarity; `Dd2/Dd2Loot.cs`). A trinket without a grade can still turn up in
a save from before (DD2's loot window paid it): it is valued at the cheapest DD1 rarity of its DD2 rarity
(common: very_common, rare: uncommon, epic: very_rare); DD2 rarities without a DD1 step (cultist) as DD1's
"rare", the price of DD1's own fight-only trinkets.

**D7. Wagon price by the place on the table.** Each place rolls a DD1 rarity from the wagon's table, takes a
DD2 trinket of the mapped rarity and costs DD1's price of the rarity rolled. So the stock keeps DD1's rarity
mix and price mix exactly, and a DD2 "common" trinket costs 100 or 150 gold depending on the roll.

**D8. Selling.** DD1 sells from its Trinket Inventory screen (hold shift, a confirmation, 15% of the
price), and so does the estate: the Trinket Inventory (`RealmInventoryPanel`) is where trinkets are worn,
taken off and sold, with DD1's question ("Really sell %s for %d gold?") and DD1's 15%. DD2's character sheet
no longer changes what a hero wears, and DD2's inventory screen never opens (`Dd2/InventoryPatches.cs`). The
Merchant Network does not change the sell value (DD1's list has one entry and no tree).

**D9. A bigger wagon at once.** Building a Wagon Size step fills the new places the same week.

**D10. Event hook.** `NomadWagon.EventDiscount` is a further share off the prices for a town event to set
(DD1's Nomad New Year, `upgrade_tag_discount trinket 0.5`).

Save sections: `nomad_wagon` (`week`, `stock[]`: `id`, `rarity`, `sold`), `trinkets` (`grades`, `stored`).

## 4. Screens

Estate map: several markers per region in rows of four (DD1's `quest_number_of_quests_in_row`), the reward
row shows gold, the two heirlooms and the trinket card (more than four cards move closer), the region
tooltip speaks of points. Goals of generated quests use DD1's sentences (`town_quest_goal_start_*`) filled
from `quest.types.json`.

Nomad Wagon (`NomadWagonPanel`, opened through `Buildings.Register("nomad_wagon", ...)`): DD1 backdrop and
keeper (`RosterWindow`), the table art at backdrop pixel 682,120, cards from 737,110 every 100 x 180, six to
a row, the price under each; "Trinket Inventory" row from 700,526 every 80 px, eight to a page; the purse at
1196,30; a message line at 682,716; the upgrade pane behind the window's "Upgrades" button. Click a ware to
buy, click a stored trinket to sell (after the question), right click closes.

## 5. Dev bridge

| Command | Does |
|---|---|
| `quests.rules` | DD1's rules in force: quests finished, table row, quests asked for, per region gate / points / level / quest rows / heirlooms, roster levels and difficulty weights, what was not offered, missing files |
| `quests.offers` `{dungeon?}` | the week's offers with rewards in words, `wanted`, `notOffered` |
| `quests.roll` `{advance?, finished?}` | rolls the offers again; `advance:true` lets a week pass first (wagon restocks, coach returns) |
| `quests.finish` `{index, success?, week?}` | ends an offer without playing it: pays and counts it (or fails it), a week passes |
| `quests.progress` `{dungeon, done}` / `{finished}` | a region's points / the estate's quests finished |
| `quests.map`, `quests.open`, `quests.select`, `quests.forward`, `quests.close`, `quests.complete` | as before; `quests.map` now lists gold, heirlooms and trinkets per offer |
| `wagon.state` | wares (DD1 rarity, DD1 price, estate price, sold, why blocked), stores with sell values, the screen |
| `wagon.open` `{upgrades?}`, `wagon.close` | the window |
| `wagon.restock` | rolls the stock again |
| `wagon.buy` `{index}`, `wagon.sell` `{id}` | trade without the mouse (no question asked) |
| `wagon.discount` `{share}` | a town event's discount |
| `trinkets.state` | prices per rarity, DD2 pools per rarity (size, free, ids), stores, grades |
| `trinkets.inspect` `{id}` | name, DD2 rarity, grade, sell value, whether it can be held, icon loaded, description |
| `trinkets.roll` `{rarity, give?}` | draws a DD2 trinket for a DD1 rarity, into the stores unless `give:false` |
| `trinkets.give` `{id, rarity?}` | puts a named trinket in the stores |
| `trinkets.oneofeach` `{on?}` | the owner's choice Q4: a trinket owned once (DD2) or copies (DD1) |
| `ledger.state` | both sides of every thing the player owns: the estate's purse, heirlooms, stores and bag; DD2's player inventory, hero slots, hero upgrade points, flame, loot list; `clean` when DD2 holds nothing of its own |
| `ledger.sweep` `{dry?, heroes?}`, `ledger.migrate` `{dry?}`, `ledger.hold` `{on?}`, `ledger.stray` `{id, qty?}`, `ledger.pay` `{on?}` | the sweep that keeps DD2's inventory empty, what loading the save brought over, holding it back to look first, a DD2 item dropped there on purpose, the owner's choice Q7 |
| `dungeon.trinkets`, `dungeon.wear` `{slot, hero?, into?}`, `dungeon.takeoff` `{hero?, id / index}` | the bag's trinkets and the party's; bag to hero; hero to bag |

## 6. Hooks other files still have to call

Written and compiled, but called from nowhere yet, because the callers belong to other owners:

- `QuestBoard.RetreatBlockReason(quest)`: DD1's `can_retreat` (false for the last descent). For
  `DungeonRun.Leave` to refuse an abandon.
- `QuestBoard.PayForRetreat(quest, partyGuids)`: DD1's `retreat_party_kill_count` (1 for every Darkest
  Dungeon quest): kills that many random living heroes of the party and reports it. For `DungeonRun.Finish`
  when the status is `Abandoned`, before the survivors are counted.
- DONE (one inventory, step 5): a DD1 loot drop of kind `LootKind.Trinket` (table `T`: very_common ...
  ancestral; curios, camp loot, and now fights) is drawn with `Trinkets.Roll(dd1Rarity, rng)` and carried as
  an item of the bag, as DD1 carries it: a card on the loot scroll and in the bag, home with the party by
  DD1's keep rate (`Core.Inventory.Settle`, `DungeonRun.BringHome`), lost with a lost party.
- `NomadWagon.EventDiscount`: for the town event "Nomad New Year".
- `QuestPanel.Camps(quest)` still answers 0 (the camps of a quest by its length belong to the camping work).

`UpgradeText.Pending` loses the wagon's two tree ids at start-up (`NomadWagon.Register`), so the upgrade pane
stops calling them "not in effect"; the literals in `UpgradeText.cs` and the native-store case in
`Buildings.Open` are dead code now.

## 7. Not verified without the game

- Every DD2 call above in the Kingdom host: the pools' contents and sizes (`trinkets.state`), whether
  `GetIsValidOption` lets hero and region trinkets through on the fresh Estate profile, `GetCanCollect`
  outside a run, icons (prefab shape, pool spawn in the hamlet), the description's rich text in the mod's
  tooltip box (sprite and font tags).
- The wagon window and the map's trinket card in game (only drawn offline by the preview tools).
- The READINGs of section 2 against DD1 itself: the row of the quests-per-visit table, how quests are
  dealt to regions, how difficulties are mixed, two heirloom types per quest.
- Old saves: the `done` to `xp` conversion.
