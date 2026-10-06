# Town events, the Ancestor's Memoirs, narration

What DD1 does, where its data says so, what the mod does with it, and what is still a reading of the data
rather than a fact (marked INFERRED: DD1 is native code, only its files can be read). Written with the code
(2026-10-04); nothing here has been seen in the running game yet (section 7).

Files: `Core/TownEvents.cs` (rules, pure C#), `Estate/TownEvents.cs` + `TownEventEffects.cs` (the estate's
side), `TownEventHooks.cs` (what other systems ask), `TownEventText.cs`, `TownEventPanel.cs`,
`TownEventDev.cs`; `Estate/Memoirs.cs`, `MemoirsPanel.cs`, `MemoirsDev.cs`; `Estate/Narration.cs`,
`NarrationRules.cs`, `NarrationStrings.cs` (class `Dd1Strings`), `NarrationMoments.cs`, `NarrationDev.cs`;
`tests/CoreTests/TownEventTests.cs` (16 tests); `tools/preview_town_event.py`.

## 1. DD1's data

| file | what it gives |
|---|---|
| `campaign/town_events/base.town_events.events.json` | 47 events |
| `campaign/town_events/mode.town_events.events.json` | `stage_coach_bonus_recruits` (the base mode's; `modes/radiant` has its own copy with week 5) |
| `campaign/town_events/arena.town_events.events.json` | 2 Butcher's Circus events: **not read** |
| `campaign/town_events/shared_dlc.town_events.events.json` | empty: not read |
| `dlc/580100_crimson_court/features/districts/campaign/town_events/districts.town_events.events.json` | `cc_districts_unlock` ("Cornerstones"): read when the install has it (`TownEventCatalog.FeatureFiles`). The Districts lie in the Crimson Court's folder but are a feature of their own; its picture `town_event.image_cc_districts_unlock.png` lies under the same feature folder (`TownEventDef.Root`) |
| other `dlc/*/.../town_events/*.json` | Crimson Court, Color of Madness, Shieldbreaker, the Flagellant, Fire's Edge: **not read** |
| `campaign/town_events/town_events.settings.json` | `event_chance_per_town_visits` per frequency setting: normal 0.33 / 0.67 / 0.75 / 1.0, plentiful 0.67 / 0.67 / 0.80 / 1.0, off 0 |
| `campaign/town_events/town_events.quest_type_event_guarantees.json` | 8 pairs (dungeon, quest type) -> event |
| `shared/buffs/base.buffs.json` | the 27 `town_event_*` buffs the events name (stat, amount, rule) |
| `campaign/roster/roster.variables.json` | `town_visit_town_progression.idle_hero_stress_heal` 5 (what "idle heroes +200% stress heal" multiplies) |
| `localization/miscellaneous.string_table.xml` | `town_event_title_<id>`, `town_event_description_<id>`, `town_event_info_<id>`, `town_event_info_format_<effect>`, `str_vo_town_event_<id>_0`, `town_name_town_Event` |
| `localization/PSN.string_table.xml` | the stage coach event's title, description and info (they are in no other table) |
| `campaign/town/town_event/` | `town_event.background.png` 1395x776, `.character.png` 811x757 (the crier), `.tone_frame_{good,neutral,bad}.png` 596x645, `.image_<event>.png` 500x240, `.icon.png` 113x113, `town_event.layout.darkest` |

Event fields: `base_chance` (weight), `per_not_rolled_additional_chance`, `cooldown`, `is_unique`,
`priority` ("higher" in the arena file and the districts' one; the DLC files also write "high" and "highest"),
`requirements` { `minimum_week`, `dead_heroes`, `hero_level_counts` [{level, count}], `upgrades_purchased`
[{tree_id, requirement_code}], `trinket_storage_count`, `minimum_number_of_district_buildings` }, `tone`,
`sprite` + `sprite_attachment` (a Spine effect hung on a building: not drawn), `data` [{`type`,
`string_data`, `number_data`}].

## 2. Which event a week gets (`TownEventRules.Visit`)

A town visit = the party coming home = `EstateState.WeekAdvanced`. In order:

0. **Script.** INFERRED: an event with a `priority` and no weight at all (`base_chance` 0,
   `per_not_rolled_additional_chance` 0: `TownEventDef.Scripted`) can never win a draw, so it is not drawn: it
   comes with the first visit that meets its requirements, the highest priority first, before any guarantee
   and without a chance roll. Stock DD1 writes its story beats this way (the Crimson Court's chain: week 3,
   "quest finished"); of the files the mod reads only the districts' "Cornerstones" is one (week 10, unique,
   at least one district building on offer). "off" switches these off with everything else. INFERRED too:
   `minimum_number_of_district_buildings` counts the buildings the campaign offers (`TownEventSituation.
   DistrictBuildings`: eleven with DD1's districts installed, else 0), not the ones built.
1. **Guarantee.** A quest finished with success in (dungeon, quest type) of the guarantee table brings that
   event, without a chance roll. INFERRED: the event must still meet its requirements and be off cooldown
   (the file only pairs quests with events; so a gather quest in week 3 promises nothing, and the dungeon
   buffs, cooldown 1, do not come two weeks running). DD1's quest type for these is `inventory_activate`;
   the estate's `activate` and `inventory_activate` are both read as that.
2. **Chance.** Otherwise an event comes with `event_chance_per_town_visits[n]`, n = visits since the last
   event (the last entry for all later visits).
3. **Draw.** By weight among the events whose requirements are met, that are off cooldown and, if unique,
   have not happened: `base_chance + per_not_rolled_additional_chance x draws lost since it last happened`.
   Higher priority first. `base_chance` 0 never wins (the four dungeon buffs come by guarantee only).
4. **Memory.** The winner's cooldown starts (it stays away for that many visits), its lost draws are
   forgotten, the visit count starts again. INFERRED: a lost draw is counted for an event that took part in
   a draw and lost; a visit without an event (chance failed, or nothing could be drawn) adds one to the
   visit count. Consequence: nothing can happen before week 6 (every event has `minimum_week` >= 6), four
   visits pass, the chance reaches 1, and the first event comes with the first visit of week 6.
5. INFERRED: `hero_level_counts` counts heroes at or above the level.

The setting is "normal" (DD1's default; `events.setting` changes it, it is saved). The estate's memory is
the `town_events` save section: visits, cooldowns, lost draws, events that happened, the dice, the week's
event (id, week, guaranteed, applied, shown, free upgrades left, graves offered, notes) and last week's.

## 3. The events

"self" = done by `TownEventEffects.cs`; "hook" = another system asks `TownEventHooks` (section 4: it does
nothing until that system's lines are in); "out" = left out of the draw, with the reason logged once.

| event (DD1 title) | weight, cooldown, needs | effect in DD1's file | in the mod |
|---|---|---|---|
| embark_party_buff_{crypts,weald,warrens,cove}_buff (Silence in the Crypts, Sunshine in the Thicket, Fresh Air in the Tunnels, Gentle Tide) | 0, 1, week 6; guaranteed by an activate quest there | buffs until quest end, in that dungeon: +33% resolve XP, +15% damage low and high | self: the party of the next expedition into that dungeon gets +15% damage dealt (a DD2 stats container on each hero, as the blacksmith's gear) and its survivors +33% of the resolve experience the quest paid (the fraction as a chance) |
| idle_resolve_level_{plague_doctor, jester, highwayman, grave_robber, man_at_arms} | 2, 2, week 6 | idle heroes of the class gain a resolve level | self: heroes of the class who were neither in the party nor in a building nor missing when the expedition ended gain the experience to the next level |
| idle_resolve_level_abomination (A Gibbous Moon) | 2, 2, week 6 | same | self when the Abomination's DLC is owned, else out |
| idle_resolve_level_arbalest (Archery Tournament; arbalest and musketeer), idle_resolve_level_houndmaster (Rats Among Us) | 2, 2, week 6 | same | **out**: DD2 has no Arbalest, Musketeer, Houndmaster |
| bonus_recruit_{vestal 3, occultist 3, hellion 2, leper 2, bounty_hunter 2} | cooldown 2, week 6 | 2 recruits of the class | self: up to 2 on this week's coach. DESIGN CALL: on any path of the class nobody walks, also one the Hero Paths tree has not opened. Out while the estate has the class on every path |
| bonus_recruit_crusader (Call of the Crusade) | 2, 2, week 6 | same | self when the Crusader's DLC is owned, else out |
| bonus_recruit_antiquarian (Antique Roadshow) | 2, 2, week 6 | same | **out**: no Antiquarian hero in DD2 |
| stage_coach_bonus_recruits (A Day Long Awaited) | 10 +2, 2, week 6 | 6 more recruits | self: up to 6 more by the coach's own rule (open paths, one of every class first) |
| in_activity_buff_stress_heal_buff (A Ray of Sunlight) 5 / _debuff (The Miserable Dark) 3, week 12 | cooldown 2 | six buffs: stress heal received +33% / -33% in each tavern and abbey activity | hook `ActivityReliefFactor` |
| lock_{bar,gambling,brothel}_discount_tavern, lock_{meditation,prayer,flagellation}_discount_abbey | 3, 2, week 12 | one activity locked, the two others -50% | hook `ActivityClosed`, `ActivityPrice` |
| free_abbey (All Saints Day), free_tavern (Mardis Gras) | 2, 2, week 6 | the three activities free | hook `ActivityPrice` |
| free_sanitarium (Caregivers Convention) 2, free_disease (Medical Breakthrough) 8, week 12 | cooldown 2 | wards free | hook `ActivityPrice` (ward id) |
| free_all_activities (Town Fair) | 5, 2, week 6 | all eight free | hook `ActivityPrice` |
| provision_supply_discount (Supply Run), provision_supply_free (Bumper Crop) | 2, 1, week 6 | supply and provision cost -50% / -100% | hook `ProvisionPriceFactor` |
| provision_reduced_amounts (Lost Shipment) | 2, 2, week 12 | supply and provision amounts -50% | hook `ProvisionStock` |
| nomad_wagon_trinket_discount (Nomad New Year) | 3, 2, week 6 | trinkets cost 50% | self: sets `NomadWagon.EventDiscount` (the wagon's own field for this) to 0.5 for the week |
| upgrade_tag_free_armour (A Job Well Done), upgrade_tag_free_weapon (Lost and Found) | 4, 2, week 6, needs blacksmith.armour / .weapon step a | one free upgrade of that kind | hook `FreeUpgrades` / `UseFreeUpgrade` ("armour", "weapon") |
| upgrade_tag_free_building_upgrade (Labour Force) | 5, 2, week 6 | one free building upgrade | hook `FreeUpgrades` / `UseFreeUpgrade` ("building") |
| idle_stress_heal_buff (One Good Week) | 8, 2, week 6 | idle heroes: stress heal received +200% | self: every idle hero sheds 5 x 3 = 15 of DD1's 200 stress at the week's end (0.75 of DD2's 10: the fraction as a chance). The estate has no weekly idle relief of its own, so the event gives the whole buffed amount |
| embark_party_buff_virtue_chance (Valiant Spirit) week 6 / _affliction_chance (The Dark is Rising) week 12 | 6, 2 | resolve check +15% / -15% until quest end | self: the party of the next expedition gets DD2's `overstress_chance_modifier` for `resolute` +0.15 / -0.15 (DD2: resolute 0.2 against meltdown 0.8; DD2's own buffs use +-0.05 .. 0.25) |
| dead_recruit (From Beyond) | 3 +1, 3, week 15, 3 dead | 3 dead heroes offered, one returns | self: up to 3 graves are offered on the notice; the chosen one returns as a new hero on the bench with name, class, path, resolve experience and gear levels (quirks and learned skills are not kept: DD2 drops the dead actor). Needs room in the barracks. Out while no grave has a free class-and-path place |
| remove_quest_hero_level_restriction (Helping Hand) | 6, 2, week 15 | no level restriction on the next quest | hook `LevelRestrictionLifted` |
| plot_quest_town_invasion_0 (Brigand Incursion!) | 12 +3, 4, week 40, 4 heroes of level 5 | starts a plot quest | self (`Estate/PlotQuests.cs`): DD1's quest `plot_town_invasion_0` ("Wolves at the Door", level 6, dungeon `town`) stands on the Estate Map for the week, on DD1's own map (`maps/town_invasion_0.dm`); an expedition to anywhere else that week takes the last step of three building trees (exe 0x92d260, 0x94ad00); abandoning it costs a hero. Built, not seen in game. Out once the quest is done (not repeatable) |
| plot_quest_crow_trinket (Shrieker's Prize) | 6 +2, 3, week 15 | starts a plot quest | self: DD1's quest `plot_crow_trinket` (level 5, the Shrieker's perch in the Weald) for the week; it pays a trinket of DD1's rarity `crow` (a DD2 epic one stands for it). Out while the Weald is not open |
| trinket_retention_add_from_storage (A Thief in the Night) | 5 +1, unique, week 42, 8 trinkets | the Shrieker takes 8 trinkets | self (`Estate/Shrieker.cs`): eight of the stores' unworn trinkets go to the Shrieker's hoard (the rarest first: a GUESS), and "Shrieker's Perch" is on the map while the hoard holds eight with one uncommon or better; the quest gives the rarest eight back |
| cc_districts_unlock (Cornerstones) | 0, unique, priority higher, week 10, 1 district building: scripted (section 2, step 0) | `districts_unlocked`; `bonus_currency` blueprint 1 | self: `Districts.Unlock`, `Districts.AddBlueprints(1)`; the Ancestor says DD1's line for it (`str_vo_crimson_court_town_event_cc_districts_unlock`: text only, its voice clip is in the Crimson Court's bank, which is not loaded). Out on an install without the districts' files (the event's own file is then missing too) |
| arena_first, arena_win | arena file | send the player to the Circus | **not read** |

An event is staged only when every one of its effects has a target, so a notice never promises what does
not happen. A buff's meaning comes from DD1's buff file (`stat_type`, `stat_sub_type`, `amount`, `rule_type`,
`rule_data.string`), not from its name.

### The notice (`TownEventPanel`)

DD1's "Hear Ye, Hear Ye!" screen from `town_event.layout.darkest` (offsets in the backdrop's pixels, an
element's x at its middle, y at its top): crier at `character_pos` -5,100; tone frame at `tone_frame_offset`
1030,115; title at `title_offset` 1030,150 (on the frame's upper band, rows 22-84 of the art); picture at
`image_offset` 1030,216; story at `description_offset` 1030,480, 485 wide; what the event does on the lower
band (rows 516-624), DD1's first line at `result_pos` + `element_start_pos` = 1020,646; From Beyond's graves
at `result_pos` + `recruit_entry_start_pos` within `recruit_max_width` 520. The mod's own: the info block is
centred on the band; the graves are centred in DD1's width; a line "(used)" after a spent free upgrade.

It comes up by itself once per event, after the hamlet's own report has been clicked away and before the
Ancestor's remark on the event; all week a small notice (DD1's `town_event.icon.png` and the title) stands
after the estate's band at the top of the hamlet (`TownEventPanel.NoticePos`, the mod's own: DD1 lists the
event in its activity log). `python tools/preview_town_event.py` draws the notice for any event.

## 4. Hooks: the lines for the files other systems own

Without an event every hook answers neutrally. `events.state` shows how often each was asked
(`hooksAsked`), `events.hooks` what they answer now.

**`Estate/ActivityRules.cs`** (lock, discount, free; Tavern and Abbey)

```csharp
        public static int Slots(Activity activity)
        {
            if (TownEventHooks.ActivityClosed(activity.Id)) return 0;      // DD1 activity_lock: closed for the week
            var tier = Pick(activity.Slots, activity);
```
```csharp
            if (tier == null || tier.Low <= 0f) return 0;
            return TownEventHooks.ActivityPrice(activity.Id, Math.Max(1, ToGold(tier.Low * CostMultiplier(heroLevel))));
```
(A hero who refused to leave a slot last week stays where he is; the panel may draw DD1's locked overlay,
`building_activity_slot_layout locked_overlay_offset`, from `TownEventHooks.ActivityClosed`.)

**`Estate/ActivityLedger.cs`**, `EndWeek` (A Ray of Sunlight / The Miserable Dark)

```csharp
            actor.ApplyStressHeal(ActivityRules.RollRelief(activity, actor.StressMax * TownEventHooks.ActivityReliefFactor(activity.Id), Random), SourceType.INN);
```
(`RollRelief` is linear in the stress scale, so the factor keeps its fraction-as-chance rounding. The hook
looks at the week that has just ended, whatever order the week's handlers run in.)

**`Estate/SanitariumRules.cs`**, last line of `Price`

```csharp
            return TownEventHooks.ActivityPrice(ward.Id, Math.Max(1, ActivityRules.ToGold(tier.Value * CostMultiplier(heroLevel))));
```
**`Estate/SanitariumLedger.cs`**, `Place`: a free week must not read as "not offered"

```csharp
            if (option.Cost <= 0 && TownEventHooks.ActivityPriceFactor(ward.Id) > 0f) return "The " + ActivityText.Activity(ward.Id) + " does not offer that";
```

**`Core/Provision.cs`** (Core cannot see the estate, so the shop gets two plain fields)

```csharp
    public sealed class ShopLine
    {
        public ItemDef Item;
        public int Stock, Bought;
        /// <summary>Share of the item's price asked at this visit (a town event's discount); 1 as a rule.</summary>
        public double PriceFactor = 1.0;

        public int Price => (int)Math.Round(Item.BuyGold * PriceFactor);
```
```csharp
        public int CostDd1 => _lines.Sum(l => l.Bought * l.Price);
```
```csharp
            var price = line.Price;      // in CanBuy, in place of the item.BuyGold line
            if (price > 0) count = Math.Min(count, (int)(((long)Purse * GoldScale - CostDd1) / price));
```
**`Dungeon/Provisioning.cs`**, `NewShop`, after the shop is made

```csharp
            foreach (var line in shop.Lines)
            {
                line.PriceFactor = TownEventHooks.ProvisionPriceFactor(line.Item.Type);
                line.Stock = TownEventHooks.ProvisionStock(line.Item.Type, line.Stock);
            }
```
**`Dungeon/ProvisionScreen.cs`**: the three `InventoryText.Price(...Item.BuyGold)` labels show `line.Price`
(lines 145, 278, 369).

**`Estate/Blacksmith.cs`** (one free weapon / armour level)

```csharp
        /// <summary>DD1's upgrade tag of a piece, as its town events name it.</summary>
        public static string Tag(Gear gear) => gear == Gear.Weapon ? "weapon" : "armour";

        /// <summary>What the hero pays for a piece's next level this week: nothing while a town event's gift lasts.</summary>
        public static int Price(UpgradeHeroRules.Purchase purchase, Gear gear) => TownEventHooks.FreeUpgrades(Tag(gear)) > 0 ? 0 : Price(purchase);
```
in `Block`: `var price = Price(next, gear);`; in `Fit`:
```csharp
            var free = TownEventHooks.FreeUpgrades(Tag(gear)) > 0;
            var price = free ? 0 : Price(Next(actor, gear));
            Set(actor, gear, Level(guid, gear) + 1);
            if (free) TownEventHooks.UseFreeUpgrade(Tag(gear));
            else EstateState.AddGold(-price);
```
`BlacksmithPanel.cs` line 141: `Blacksmith.Price(next, gear)` (0 reads as "Free").

**`Estate/UpgradeRules.cs`** (one free building step)

```csharp
        public static string ShortOf(Step step)
        {
            if (TownEventHooks.FreeUpgrades("building") > 0) return null;      // DD1 upgrade_tag_free: the week's gift
```
in `Buy`, around the payment loop:
```csharp
            if (!TownEventHooks.UseFreeUpgrade("building"))
                foreach (var cost in step.Costs)
                {
                    ...
                }
```

**`Estate/Resolve.cs`**, first line of `RefusalReason` (Helping Hand)

```csharp
            if (TownEventHooks.LevelRestrictionLifted) return null;
```

Optional, no behaviour depends on them:
* `Estate/Graveyard.cs`: `public static bool Remove(Fallen fallen) => _fallen.Remove(fallen);`; From Beyond
  takes the returned hero off the list through `Graveyard.All as IList<Fallen>` today.
* `Estate/StageCoach.cs`: `OfferForTest` is what the events use to put a recruit on the coach; it deserves a
  name without "Test". `StageCoachPanel` has to cope with up to 6 (+2) more recruits than the coach's size.
* `Dungeon/Camp.cs`, where a camp begins: `Narration.Trigger("camp", Narration.Scope.Dungeon);` (DD1 has five
  lines for it; this file does not call into the camping code while it is being written).

## 5. The Ancestor's Memoirs (`Memoirs`, `MemoirsPanel`)

DD1's statue screen (`campaign/town/buildings/statue/`): `statue_media_info.json` gives the categories
(label string, title bar, entry slab, order, and a regex over DD1 ids) and the films; `statue.layout.darkest`
the places (list at `list_position` 270,170 from `building.layout.darkest body_base_pos`, 600x580; the
Ancestor's line at `quote_position`; slabs 600x118 every 128; title bars 619x136 of which rows 16-118 show).

| DD1 category | what the estate files there |
|---|---|
| Prologue (`house_of_ruin`, `old_road`) | DD1's two films as text: their subtitles `str_vo_<film>_<n>`, a line per sentence. Always there, as in DD1 |
| The Ancestor's Path (`plot_kill_.*`) | every finished story quest of `Data/plot_quests.json` whose `dd1_quest_id` matches, in story order, and the opening expedition (`plot_tutorial_crypts`, which matches no category). Text: `intro_narration`, blank line, `victory_narration` |
| The Darkest Dungeon (`plot_darkest_dungeon_.*`) | the five descents |
| Epilogue (`epilog`) | DD1's closing film as text; listed but locked (DD1's `str_media_epilog_locked`) until the quest standing for `plot_darkest_dungeon_4` is finished (DD1: `access_if_plot_finished`) |

An entry's picture is the DD2 boss's own portrait when the game has it loaded (`dungeons.json` bosses
`actor_ids[0]`), else DD1's `portrait_<boss of the quest's DD1 slot>.png` (a Librarian memoir then shows
the Necromancer's face: the same stand-in the story file allows the quest map), `backer_journal_icon.png`
for the opening expedition. DD1 plays a memoir on click and prints its words at the quote position; here a
click opens the memoir's page in the list's place ("Back to the memoirs" returns). Under the shelves: "n of
30 chapters of the estate's story told". Completed quests come from `EstateState.CompletedQuests`.

## 6. Narration (`Narration`, `NarrationRules`, `Dd1Strings`)

DD1's table is `audio/narration.json`: 36 moments ("entries"), each with voice clips; a clip has `chance`,
`priority`, `tags` + `check_all_tags`, `max_raid_occurrences` / `max_town_visit_occurrences` /
`max_campaign_occurrences` (0 = no limit), `queue_only_on_empty`. The clip `/vo/<group>/<name>` has the
subtitles `str_vo_<group>_<name>_<part>` in the string table (parts joined with a space).

| moment in the estate | how it is noticed | DD1 trigger, tags | what is said |
|---|---|---|---|
| the hamlet comes up in a new week (also the very first time) | `NarrationMoments.HamletOpened`, once per week | `town_visit_start`, the week's event id | first ever: "Welcome home, such as it is..." (priority 3, once per campaign); with an event its line `/vo/town_event/<id>` (priority 2); else one of the 22 backstory lines (priority 1) |
| a building is clicked | `HamletScene.BuildingClicked` | `enter_building`, building id | `/vo/tutorial/first_<building>` once per campaign (stage coach, blacksmith, abbey, tavern, guild, graveyard, survivalist, nomad wagon; DD1 has none for the sanitarium); the statue: "In time, you will know..." once per visit |
| the Estate Map / the provisions open | `QuestPanel.IsOpen`, `ProvisionScreen.IsOpen` rising | `enter_quest_select`, `enter_provision_select` | once per campaign each |
| a building step is built | `UpgradeRules.Built` | `upgrade_building`, building id | 3 lines per building at 20% each, 40% DD1's own silence; each once per visit, then again without that limit |
| a hero is hired / dismissed | `RosterLifecycle.Changed` while the hamlet is up | `recruit_hero` class id / `dismiss_hero` | first of a class always, later 33%; DD1 has no lines for runaway, duelist, flagellant. Dismissal: one of 8, each once per visit |
| an expedition begins | `NarrationMoments.ExpeditionSeen`, new map seed | story quest: none of DD1's; else `quest_start`, dungeon + quest type | story quest: its `intro_narration` (DD1 reads the boss's memoir on the loading screen and a boss line in the first room: both are about DD1's bosses). Else DD1's line for the dungeon and quest type |
| the quest's goal is reached | `Exploration.ObjectiveComplete` rising | story quest: none; else `quest_end_completed`, dungeon + quest type | story quest (first time): its `victory_narration`, there and then; the hamlet's copy of it (`QuestBoard.PendingNarration`) is taken out so it is said once (if it was not shown before the party left, the hamlet keeps it). Else one of DD1's two lines (none for gather / activate in the Cove) |
| the expedition ends without success | `EstateState.ExpeditionEnded` | `quest_end_not_completed` | one of 11 |
| a hero dies | the graveyard grows | `kill_hero` | one of 5, each once per expedition, only when the Ancestor is silent |

DD1's lines about its own bosses and plot quests carry those as tags (`necromancer`, `plot_kill_hag_2`,
`plot_darkest_dungeon_1` ...); the estate never passes such a tag, so they are never reached: the boss
quests speak with `plot_quests.json` instead. Not used although DD1 has them: `enter_hallway` (its only
tagless line is the Old Road tutorial's), fight lines (`combat_start`, `kill_monster`, crits, death's door,
`victory`, retreat: they would be drawn over DD2's combat screen), torch, hunger, trap, curio, loot, `camp`
(one line for the camp's owner, section 4).

INFERRED (the file gives chances and priorities, not the procedure): the moment's own `chance` is rolled
first (1 for every moment used here). Then only the lines of the highest priority among those that fit the
tags, have words and are not used up are looked at, and their chances are shares of one roll: a total above
1 is scaled down to 1 (eleven failure lines at 0.3: always one of them), a total below 1 leaves the rest to
silence (a second recruit of a class: 0.33), and an entry without a clip is a share of silence DD1 put
there on purpose. Reason for this reading: DD1's groups add up to exactly 1 too often for chance (0.5 + 0.5
for the two success lines, five kill lines at 0.2, three upgrade lines at 0.2 plus a silent 0.4). A count is
kept per clip, so two entries of one clip (first recruit: 1.0 once per campaign; later: 0.33) share it.
`queue_only_on_empty` lines are dropped while a line is on screen or waiting.

Showing: through `NarrationBox.Show`, one line at a time. The band is built once on the canvas of whoever
speaks first, so before a line it is moved to the canvas that is up (hamlet or dungeon HUD); its place on
the screen is NarrationBox's (DD1 uses one subtitle position in town and on a raid). A line of this system
goes by itself after a reading time (DESIGN CALL: 2.5 s + 0.055 s per character, 4 to 20 s; DD1 times its
subtitles by the clip) or on a click; the hamlet's own report still waits for its click. A line waits for
the hamlet's report and the crier's notice; a line for the hamlet is dropped when the party sets out, and
the other way round. Memory: the `narration` save section (counts per clip for campaign, town visit and
expedition; the week greeted; the expedition begun).

## 7. Not seen in the game yet

Everything: the module was written without launching the game. In particular
* the notice and the memoirs screen in the real canvas (font sizes, the hamlet notice's place next to the
  estate band, narrow screens), the DD2 boss portraits on the memoirs' slabs;
* the buff container on party heroes (`add_stat health_damage_dealt_percent`, `sub_stat
  overstress_chance_modifier resolute`): it follows the blacksmith's working pattern, the sub-stat line is
  the game's own syntax, but neither a boosted hit nor a changed stress test has been watched;
* recruits put on the coach by an event (naming, the coach panel with more recruits than its size);
* From Beyond end to end (the memory of a dead hero's path, experience and gear is taken one frame after
  the death; the return through `RosterLifecycle.CreateHero`);
* the narration band being moved between the hamlet's canvas and the dungeon's, and the order report ->
  notice -> remark when the party comes home;
* every hook, once its lines are in the owner's file.

Bridge: `events.state|list|force|roll|clear|setting|open|close|return|hooks`,
`memoirs.list|open|close|read|back|unlock`, `narration.state|triggers|lines|trigger|say|story|hush|forget`
(each file's header says what its commands take).
