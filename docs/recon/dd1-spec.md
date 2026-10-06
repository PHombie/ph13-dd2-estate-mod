# Darkest Dungeon 1 meta-game spec (survey of the user's own install)

Survey date 2026-10-04. Source: `E:\Steam\steamapps\common\DarkestDungeon\` (Steam appid 262060, buildid 25463639 per `appmanifest_262060.acf`). Everything below was read from that install unless tagged **(inferred)** or **(not verified)**. Nothing from the install is copied into the repo; this file only restates numbers, field names and formats.

`<DD1>` below means the install root. All data files are read-only inputs.

## 0. Conventions, naming and global facts

| Item | Value |
|---|---|
| Internal dungeon ids -> UI names | `crypts` = "Ruins", `weald` = "Weald", `warrens` = "Warrens", `cove` = "Cove", `darkestdungeon` = "Darkest Dungeon", `town` = hamlet-invasion map (UI "Hamlet"), tutorial = "The Old Road" (`dungeon_name_*` strings) |
| Currencies (`campaign/estate/estate.json`) | `gold` (non-heirloom) and heirlooms `bust`, `portrait`, `deed`, `crest`. A 5th heirloom stack type `urn` exists in `inventory/base.currency.inventory.items.darkest` but is not in the wallet list. |
| New-game wallet (`scripts/starting_save/persist.estate.json`) | gold 0, bust 10, portrait 10, deed 10, crest 20 (this is the tutorial starting save, not necessarily the post-tutorial wallet) |
| Quest-fail keep rates (`estate.json`) | gold 1.0, trinket 1.0, heirloom 1.0, jewellery 1.0, gem 1.0, journal_page 1.0, provision 0.25, supply 0.25, quest_item 0.0 (loot already in the raid inventory survives a failed quest at these rates) |
| Quest fail / regroup stress | `campaign/quest/quest.exit_penalty.json`: `fail_penalty.stress_damage` = 20, `regroup_penalty.stress_damage` = 0 |
| Data file kinds | `.json` plain JSON (read with utf-8-sig to be safe); `.darkest` = custom text blocks `name: .field value .field value ...`; `.csv` (curios); `.dm` and savegames = DSON binary (magic bytes `01 B1 00 00`); `.loc2` binary; `.skel/.atlas/.png` Spine 2.1.27; `.bank` FMOD Studio; `.ogv` Theora+Vorbis; `.tga` + `.fnt` BMFont |
| DLC folders present under `<DD1>\dlc\` | `1117860_arena_mp` (Arena, adds Circus building), `445700_musketeer`, `4964110_fires_edge`, `580100_crimson_court` (adds Districts, Courtyard dungeon), `702540_shieldbreaker`, `735730_color_of_madness`. All six are installed. DLC files override/extend base via extra files (`com.*.json` for Color of Madness, `rd.town_events...` for Fire's Edge, `shieldbreaker.*`, `musketeer.*`). This spec uses BASE files only. |
| Modes | `<DD1>\modes\base`, `new_game_plus` (= Stygian-style, `is_new_game_plus`, `unlocks_all_statue`), `radiant`. Rules in `shared/rules.json`: `new_game_plus_week_limit` 91, `new_game_plus_hero_death_limit` 13, monster max HP +15%, monster crit +2.5%. |
| Week loop | There is no explicit "week length" table in data. Town events require `minimum_week`; activities show `Week: %d/%d`; `game_over_reason_weeks` exists. 1 week per quest return / town visit is the assumption **(inferred, not verified)**. |


## 1. Hamlet buildings

### 1.1 Building index

Config: `<DD1>\campaign\town\buildings\<id>\<id>.building.json` (JSON). Common shape:

```
{ "on_start_town_visit_priority": 1,
  "requirements": { "number_of_quests_finished": N, "highest_dungeon_level": M },
  "data": { "activities": [ { "id": "...", "data": { side_effects, requirements, miscellaneous, cost_upgrades, slot_upgrades, stress_upgrades, ... } } ]   // abbey, tavern, sanitarium
            or "stores": [ { "id": "...", "data": {...} } ]                                                           // stage_coach, nomad_wagon
            or discount arrays tied to upgrade trees }  }
```
`*_upgrades` arrays: element 0 has no `upgrade_requirement_code` (base value); later elements are applied when the upgrade tree `upgrade_tree_id` (default = the activity/tree named like the activity) has the code purchased. Upgrade trees live in `<DD1>\upgrades\building\<building>.upgrades.json` (see 1.6).

| Building (UI name) | id | Unlock (`requirements`) | Function | Notes |
|---|---|---|---|---|
| Stage Coach | `stage_coach` | 0 quests, dungeon lvl 0 | store `hero_recruit`: weekly recruits, roster cap | `first_hero_classes` = plague_doctor, vestal |
| Abbey | `abbey` | 2 quests, lvl 0 | activities `meditation` ("Cloister"), `prayer` ("Transept"), `flagellation` ("Penance Hall") | stress heal |
| Tavern | `tavern` | 2 quests, lvl 0 | activities `bar`, `gambling` ("Gambling Hall"), `brothel` | stress heal |
| Sanitarium | `sanitarium` | 4 quests, lvl 0 | activities `treatment` ("Treatment Ward", quirks) and `disease_treatment` ("Medical Ward", requires `is_diseased`) | gold sinks |
| Blacksmith | `blacksmith` | 3 quests, lvl 0 | per-hero weapon/armour rank purchases (trees in `upgrades/heroes`); `equipment_cost_discount_upgrades` | |
| Guild | `guild` | 3 quests, lvl 0 | per-hero combat skill level purchases (trees in `upgrades/heroes`); `combat_skill_cost_discount_upgrades` | |
| Survivalist (`camping_trainer`) | `camping_trainer` | 0 quests, **highest_dungeon_level 2** | per-hero camping skill purchases; `camping_skill_cost_discount_upgrades` | camping skill unlock = 1750 gold each |
| Nomad Wagon | `nomad_wagon` | 0, 0 | store `trinket_supply`; trinket sell value x0.85 | |
| Graveyard | `graveyard` | 0, 0 | no data (`data: {}`); shows fallen heroes, "Deaths: %d/%d" | art: numbered images `buildings/graveyard/0_1.png`, `2.png`..`6.png` (purpose not verified), `dead_hero_backdrop.png`, `final_dd_team_tombstone.png` |
| Ancestor's Memoirs (`statue`) | `statue` | 0, 0 | no data; media viewer, config `buildings/statue/statue_media_info.json` | see 8 |
| (not a building) Provision | - | - | embark provisioning screen, `campaign/provision/provision.json` | see 6 |
| (not a building) Heirloom exchange | - | - | `campaign/heirloom_exchange/heirloom_exchange.json` | see 1.8 |
| (not a building) Estate Map / Quest select | `quest_select` | - | quest list | see 3 |
| (not a building) Trinket Inventory | `realm_inventory` | - | trinket storage (`trinket_storage` max_slots 9999) | |
| Town event board | `town_event` ("Hear Ye, Hear Ye!") | - | see 7 | |
| Circus (Arena DLC), Districts (Crimson Court DLC) | | | DLC only | ignore |

`hero_action/` and `hero_activity/` under buildings only hold UI art/layout (confirm/cancel buttons, verbose frame).

### 1.2 Abbey and Tavern activities (stress relief)

Shared structure per activity (identical shape in `abbey.building.json` and `tavern.building.json`):

* `side_effects.chance` = **0.375** for every activity. If it fires, one entry of `results` is picked by its `chance` weight (relative weights, not percents).
* Result types: `activity_lock` (hero refuses to leave the slot; cancel allowed, no refund), `go_missing` (absent for 1 week 75% / 2 weeks 25%), `add_quirk` (`quirk_library_name`), `apply_buff` (`buff_library_ids`), `add_currency` / `remove_currency` (`type`, `amount`), `add_trinket` / `remove_trinket` (rarity weights).
* `requirements`: `not_have_quirks` list = quirks that block the hero from using the activity.
* `miscellaneous.caretaker_friendly` = true on all six.
* Upgrade codes (tree id = `abbey.<activity>` / `tavern.<activity>`, codes a-f, strictly linear a->b->c->d->e->f): **a** stress heal tier 2, **b** cost tier 2, **c** slots 2, **d** stress heal tier 3, **e** cost tier 3, **f** slots 3. The `affliction_cure_upgrades` entry with code `g` has no matching tree level (dead data; base `chance` 1.00).
* Gold cost is multiplied by `hero_activity_cost_multiplier_table` (`estate.json`) indexed by hero resolve level 0..6: `[1.0, 1.0, 1.0, 1.40, 1.40, 1.80, 1.80]` **(index meaning inferred)**.
* A hero in an activity is unavailable for the next quest.

| Activity | Base cost (gold) / after b / after e | Stress heal base / a / d | Slots base / c / f | Blocked by quirks |
|---|---|---|---|---|
| meditation | 1000 / 850 / 700 | 45 / 56 / 70 | 1 / 2 / 3 | unquiet_mind, alcoholism, gambler, love_interest, god_fearing, flagellant |
| prayer | 1250 / 1050 / 900 | 55 / 69 / 86 | 1 / 2 / 3 | witness, faithless, alcoholism, gambler, love_interest, enlightened, flagellant |
| flagellation | 1500 / 1300 / 1100 | 65 / 81 / 100 | 1 / 2 / 3 | faithless, alcoholism, gambler, love_interest, enlightened, god_fearing |
| bar | 1000 / 850 / 700 | 45 / 56 / 70 | 1 / 2 / 3 | resolution, gambler, love_interest, enlightened, god_fearing, flagellant |
| gambling | 1250 / 1050 / 900 | 55 / 69 / 86 | 1 / 2 / 3 | known_cheat, alcoholism, love_interest, enlightened, god_fearing, flagellant |
| brothel | 1500 / 1300 / 1110 | 65 / 81 / 100 | 1 / 2 / 3 | deviant_tastes, alcoholism, gambler, enlightened, god_fearing, flagellant |

Side-effect result weights (chance weight in brackets; weights sum shown as total):

| Activity | Results |
|---|---|
| meditation (total 10) | activity_lock (1); go_missing (2); quirk enlightened (2); quirk improved_balance (1); quirk meditator (1); quirk calm (1); quirk unquiet_mind (2); add_trinket (0); remove_trinket (0) |
| prayer (total 11) | activity_lock (2); go_missing (1); quirk god_fearing (2); quirk witness (2); buff `townPrayerDestressBuff` (2); remove_currency 1000 gold (1); add_trinket any rarity, equal weights (1); remove_trinket (0) |
| flagellation (total 8) | activity_lock (2); go_missing (1); quirk flagellant (1); quirk faithless (1); buffs `townFlagellationDMGLowBuff`+`DMGHighBuff` (2); debuff `townFlagellationBleedDebuff` (1); trinkets (0) |
| bar (total 9.5) | activity_lock (1); go_missing (2); quirk alcoholism (1); quirk resolution (1); apply_buff (3) = 33% `townHungoverAccDebuff`+`townHungoverDEFDebuff` / 67% `townDrinkingHPBuff`; remove_currency 500 gold (0.5); add_trinket (0); remove_trinket (1; rarity weights very_rare 1, rare 1, uncommon 2, common 2, very_common 2) |
| gambling (total 13) | activity_lock (3); go_missing (1); quirks gambler / known_cheat / bad_gambler / skilled_gambler (1 each); add_currency 2000 gold (1.2; inner `chance` 0.4); remove_currency 1000 gold (1.8; inner `chance` 0.6); add_trinket (1); remove_trinket (1; rarity weights 1,1,2,3,3) |
| brothel (total 12) | activity_lock (2); go_missing (1); quirks love_interest / syphilis / deviant_tastes (1 each); buff `townBrothelSPDBuff` (2); debuff `townBrothelSPDDebuff` (1); buff `townBrothelDestressBuff` (1); remove_currency 750 gold (1); add_trinket (0); remove_trinket (1; rarity weights 1,1,3,3,3) |

Buff magnitudes (`shared/buffs/base.buffs.json`, ids exactly as above): townPrayerDestressBuff stress_dmg_received_percent -0.20; townBrothelDestressBuff -0.25; townFlagellationDMG Low/High Buff damage_low/high x +0.20 (the matching Debuffs are -0.20); townFlagellationBleedDebuff bleed resistance -0.25; townHungoverAccDebuff attack_rating -0.10 (additive); townHungoverDEFDebuff defense_rating -0.05; townDrinkingHPBuff max_hp x +0.20; townBrothelSPDBuff speed_rating +6; townBrothelSPDDebuff speed_rating -4. Event buffs: `town_event_in_activity_*_stress_heal` +0.33 stress_heal_received_percent (duration `activity_end`), `*_stress_damage` -0.33, `town_event_idle_stress_heal` +2 stress_heal_received_percent (`idle_start_town_visit`), `town_event_<dungeon>_resolve_xp` +0.33 resolve XP and `_damage_low/high` +0.15 (duration `quest_end`, `rule_type in_dungeon`), `town_event_affliction_chance` -0.15 / `town_event_virtue_chance` +0.15 resolve_check_percent.

### 1.3 Sanitarium (`sanitarium.building.json`)

Two activities. Costs listed by `sanitarium.cost` level (base, a, b, c, d, e) except where noted.

| Item | Base | a | b | c | d | e |
|---|---|---|---|---|---|---|
| treatment: positive-quirk (lock) cost (`positive_quirk_cost_upgrades`, role inferred) gold | 7500 | 6750 | 6000 | 5250 | 4500 | 3750 |
| treatment: remove negative quirk (`negative_quirk_cost_upgrades`) | 1500 | 1350 | 1200 | 1050 | 900 | 750 |
| treatment: remove permanent negative quirk (`permanent_negative_quirk_cost_upgrades`) | 5000 | 4500 | 4000 | 3500 | 3000 | 2500 |
| disease_treatment cost (`sanitarium.disease_quirk_cost` codes a / c / e) | 750 | 650 (a) | - | 550 (c) | - | 450 (e) |
| disease_treatment cure-all chance (same tree, codes b / d) | 0.33 | - | 0.67 (b) | - | 1.00 (d) | - |
| treatment slots (`sanitarium.slots`) | 1 | - | 2 (b) | - | 3 (d) | - |
| disease_treatment slots (`sanitarium.slots`) | 1 | 2 (a) | - | 3 (c) | - | - |

`quirk_treatment_upgrades.chance` = 1.0 (a treatment always succeeds). Gold costs are also scaled by the hero-level multiplier table **(inferred, same field family)**.

### 1.4 Other building upgrade effects (what each tree code does)

| Tree (`upgrades/building/*.upgrades.json`) | Codes | Effect per code |
|---|---|---|
| `stage_coach.numrecruits` | a..e | recruits per week 2 -> 3, 4, 5, 6, 7 (tooltip says the change takes effect later) |
| `stage_coach.rostersize` | a..e | roster cap 9 -> 12, 16, 20, 24, 28 |
| `stage_coach.upgraded_recruits` | a, b, c | adds experience tiers. tier1 (a): chance 0.1875, +1 camping skill, guaranteed replacement of a previous-raid dead hero of level 3. tier2 (b): chance 0.125, +1 positive quirk, +1 negative quirk, +1 combat skill, +1 camping skill, dead-hero levels [4]. tier3 (c): chance 0.0625, +2/+2/+2/+2, dead-hero levels [5,6]. Prerequisites: a needs guild.skill_levels.b + blacksmith.weapon.b + blacksmith.armour.b; b needs a + guild c + blacksmith c; c needs b + guild d + blacksmith d |
| `blacksmith.weapon`, `blacksmith.armour` | a..d | unlock per-hero weapon / armour ranks 1..4 for purchase (per-hero gold 750 / 1750 / 3000 / 6000, uniform for all 15 classes; hero tree codes 0..3 require the previous rank + blacksmith code a..d) |
| `blacksmith.cost` | a..e | equipment cost discount 10% per code (max 50%) |
| `guild.skill_levels` | a..d | unlock combat-skill levels 1..4 (per hero skill: unlock 1000 gold, then 250 / 750 / 1250 / 2500 gold for levels 1..4; hero tree codes 1..4 require guild.skill_levels a..d; 7 combat skills per class) |
| `guild.cost` | a..e | combat skill cost discount 10% per code |
| `camping_trainer.cost` | a..e | camping skill cost discount 10% per code (camping skill unlock 1750 gold, `use_limit` 1 per camp, 3 skills equipped per hero) |
| `nomad_wagon.numitems` | a..d | trinkets in stock 2 -> 4, 6, 8, 12 |
| `nomad_wagon.cost` | a..e | trinket cost discount 10% per code; sell-back factor fixed 0.85 |
| `abbey.*`, `tavern.*` | a..f | see 1.2 |
| `sanitarium.*` | a..e / a..d | see 1.3 |

Nomad store rarity weights (`rarity_generation_table`): very_common 6, common 5, uncommon 4, rare 2, very_rare 1. Blacksmith/guild/camping per-hero trees: `<DD1>\upgrades\heroes\<class>.upgrades.json` (15 files; tags `weapon`, `armour`, `combat_skill`; `first_level_not_upgrade`).

Art level of a building in the hamlet scene follows `.level_thresholds 0.33 0.66 1.00 1.00` in `town.layout.darkest` (statue: 0.25 0.50 1.00 1.00): fraction of that building's upgrades purchased selects `level01/02/03` art; `_locked` art while unlock requirements are unmet **(inferred)**. DD1's exe builds the names as `fx/town_%s_level%02d/...` and `fx/town_%s_locked/...` (strings beside `level_thresholds`). The statue has three exteriors (`town_statue_level01` a stump under a dead tree, `02` the pedestal in scaffolding, `03` the statue standing) and no upgrade tree of its own (`upgrades/building` has eight files, none for it): what its 0.25 / 0.50 are fractions of is in the exe, not in the files. The mod measures it against the whole town's upgrades (`UpgradeWindow.Share`) **(guess)**; the other reading of the same numbers would be the four Darkest Dungeon quests (one done 0.25, two 0.50), which `town_render_data.json` already uses for the town's backdrop. The graveyard has one skeleton and `ground_layout` one picture whatever their thresholds.

### 1.5 Display names (strings: `town_name_<id>`, `town_activity_name_<id>`, `town_activity_description_<id>`, `upgrade_tree_name_<tree>`)

Buildings: Abbey, Blacksmith, Survivalist (camping_trainer), Graveyard, Guild, Nomad Wagon, Sanitarium, Stage Coach, Ancestor's Memoirs (statue), Tavern, Hamlet (feflow), Provision, Estate Map (quest_select), Trinket Inventory, Trade Heirlooms, Hear Ye Hear Ye (town_event).
Activities: bar "Bar" (Drink your cares away.), gambling "Gambling Hall" (The thrill of winning.), brothel "Brothel" (Pleasures of the flesh.), meditation "Cloister" (Peace through meditation.), prayer "Transept" (Pray to a Higher Power.), flagellation "Penance Hall" (Flagellation brings absolution.), treatment "Treatment Ward" (Treat Quirks and other problematic behaviors.), disease_treatment "Medical Ward" (Treat Diseases, humours, and other physical maladies.).
Trees: abbey.meditation Cloister, abbey.prayer Transept, abbey.flagellation Penance Hall, blacksmith.weapon Weaponsmithing, blacksmith.armour Armorsmithing, blacksmith.cost Furnace, camping_trainer.cost Bonfire, guild.skill_levels Instructor Mastery, guild.cost Training Regimen, nomad_wagon.numitems Wagon Size, nomad_wagon.cost Merchant Network, sanitarium.cost Treatment Library, sanitarium.disease_quirk_cost Medical Devices, sanitarium.slots Patient Cells, stage_coach.numrecruits Stagecoach Network, stage_coach.rostersize Hero Barracks, stage_coach.upgraded_recruits Experienced Recruits, tavern.bar Bar, tavern.gambling Gambling, tavern.brothel Brothel.

### 1.6 Upgrade tree costs (heirlooms; all gold = 0)

Format of `<DD1>\upgrades\building\*.upgrades.json`: `{"trees":[{"id","is_instanced":false,"tags":[...],"requirements":[{"code":"a","currency_cost":[{"type","amount"}...],"prerequisite_requirements":[{"tree_id","requirement_code"}]}]}]}`. Each code requires the previous letter in the same tree unless noted in 1.4.


| Tree id | Cost per code (heirlooms) | Tree total |
|---|---|---|
| abbey.meditation | a: 4 bust, 5 crest ; b: 8 bust, 10 crest ; c: 11 bust, 14 crest ; d: 15 bust, 19 crest ; e: 19 bust, 24 crest ; f: 23 bust, 29 crest | 80 bust, 101 crest |
| abbey.prayer | a: 4 bust, 5 crest ; b: 8 bust, 10 crest ; c: 11 bust, 14 crest ; d: 15 bust, 19 crest ; e: 19 bust, 24 crest ; f: 23 bust, 29 crest | 80 bust, 101 crest |
| abbey.flagellation | a: 4 bust, 5 crest ; b: 8 bust, 10 crest ; c: 11 bust, 14 crest ; d: 15 bust, 19 crest ; e: 19 bust, 24 crest ; f: 23 bust, 29 crest | 80 bust, 101 crest |
| blacksmith.weapon | a: 8 deed, 8 crest ; b: 20 deed, 21 crest ; c: 32 deed, 35 crest ; d: 44 deed, 49 crest | 104 deed, 113 crest |
| blacksmith.armour | a: 8 deed, 8 crest ; b: 20 deed, 21 crest ; c: 32 deed, 35 crest ; d: 44 deed, 49 crest | 104 deed, 113 crest |
| blacksmith.cost | a: 4 deed, 4 crest ; b: 9 deed, 10 crest ; c: 14 deed, 15 crest ; d: 18 deed, 20 crest ; e: 24 deed, 26 crest | 69 deed, 75 crest |
| camping_trainer.cost | a: 15 crest ; b: 35 crest ; c: 54 crest ; d: 72 crest ; e: 92 crest | 268 crest |
| guild.skill_levels | a: 6 portrait, 14 crest ; b: 15 portrait, 38 crest ; c: 24 portrait, 60 crest ; d: 33 portrait, 84 crest | 78 portrait, 196 crest |
| guild.cost | a: 2 portrait, 6 crest ; b: 5 portrait, 14 crest ; c: 8 portrait, 21 crest ; d: 11 portrait, 29 crest ; e: 14 portrait, 36 crest | 40 portrait, 106 crest |
| nomad_wagon.numitems | a: 10 crest ; b: 26 crest ; c: 42 crest ; d: 58 crest | 136 crest |
| nomad_wagon.cost | a: 8 crest ; b: 18 crest ; c: 28 crest ; d: 38 crest ; e: 46 crest | 138 crest |
| sanitarium.cost | a: 3 bust, 3 crest ; b: 8 bust, 5 crest ; c: 12 bust, 8 crest ; d: 16 bust, 10 crest ; e: 21 bust, 13 crest | 60 bust, 39 crest |
| sanitarium.disease_quirk_cost | a: 3 bust, 3 crest ; b: 8 bust, 5 crest ; c: 12 bust, 8 crest ; d: 16 bust, 10 crest ; e: 21 bust, 13 crest | 60 bust, 39 crest |
| sanitarium.slots | a: 10 bust, 6 crest ; b: 20 bust, 13 crest ; c: 30 bust, 19 crest ; d: 60 bust, 38 crest | 120 bust, 76 crest |
| stage_coach.numrecruits | a: 3 deed, 4 crest ; b: 8 deed, 10 crest ; c: 16 deed, 15 crest ; d: 20 deed, 20 crest ; e: 25 deed, 26 crest | 72 deed, 75 crest |
| stage_coach.rostersize | a: 3 deed, 4 crest ; b: 8 deed, 10 crest ; c: 16 deed, 15 crest ; d: 20 deed, 20 crest ; e: 25 deed, 26 crest | 72 deed, 75 crest |
| stage_coach.upgraded_recruits | a: 9 bust, 12 crest ; b: 12 bust, 16 crest ; c: 15 bust, 20 crest | 36 bust, 48 crest |
| tavern.bar | a: 2 portrait, 5 crest ; b: 4 portrait, 10 crest ; c: 6 portrait, 14 crest ; d: 8 portrait, 19 crest ; e: 10 portrait, 24 crest ; f: 11 portrait, 29 crest | 41 portrait, 101 crest |
| tavern.gambling | a: 2 portrait, 5 crest ; b: 4 portrait, 10 crest ; c: 6 portrait, 14 crest ; d: 8 portrait, 19 crest ; e: 10 portrait, 24 crest ; f: 11 portrait, 29 crest | 41 portrait, 101 crest |
| tavern.brothel | a: 2 portrait, 5 crest ; b: 4 portrait, 10 crest ; c: 6 portrait, 14 crest ; d: 8 portrait, 19 crest ; e: 10 portrait, 24 crest ; f: 11 portrait, 29 crest | 41 portrait, 101 crest |

All 8 building files together: 516 bust, 2103 crest, 421 deed, 241 portrait (gold 0).


### 1.7 Heirloom exchange (`campaign/heirloom_exchange/heirloom_exchange.json`)

`exchange_rates[]` of `{exchange_from_type, exchange_from_amount, exchange_to_type, exchange_to_amount}`:

| From | To |
|---|---|
| 3 bust | 1 portrait; 3 bust -> 2 deed; 2 bust -> 3 crest |
| 2 portrait | 3 bust; 2 portrait -> 3 deed; 1 portrait -> 3 crest |
| 3 deed | 2 bust; 3 deed -> 1 portrait; 2 deed -> 3 crest |
| 3 crest | 1 bust; 6 crest -> 1 portrait; 3 crest -> 1 deed |


## 2. Roster, stagecoach, hero levels

| Item | Value | Source |
|---|---|---|
| Base classes (15) | abomination, antiquarian, arbalest, bounty_hunter, crusader, grave_robber, hellion, highwayman, houndmaster, jester, leper, man_at_arms, occultist, plague_doctor, vestal | `campaign/roster/base.roster.groups.json` |
| Roster size per `stage_coach.rostersize` level | 9 (base), 12, 16, 20, 24, 28 (a..e) | `stage_coach.building.json` |
| Recruits offered per week per `numrecruits` level | 2 (base), 3, 4, 5, 6, 7 (a..e) | same |
| First recruits | classes `plague_doctor`, `vestal` | `first_hero_classes` |
| Recruit cost | no cost field in the store config (free) | **(not verified in code)** |
| Experienced recruits | see 1.4 (`upgraded_recruits_upgrades`) | |
| Resolve XP needed per hero level | level 0: 0, 1: 2, 2: 8, 3: 14, 4: 24, 5: 36, 6: 48 (`resolve_level_thresholds` = [0,2,8,14,24,36,48]) | `campaign/roster/roster.variables.json` |
| Idle hero stress heal on a town visit | `town_visit_town_progression.idle_hero_stress_heal` 5.0; `town_visit_non_town_progression` 0.0 | same (meaning of the two keys **inferred**: 5 per week-advancing visit) |
| Hero name ids | `hero_name_%d`; 562 name strings in `localization/names.string_table.xml` | |
| Quest hero-level cap | `campaign/quest/quest.restriction.json`: `resolve_level_threshold_table` = [2, 2, 3, 4, 5, 99, 99], indexed by quest difficulty 0..6. Read as the MAXIMUM hero resolve level allowed on that difficulty (99 = no cap). Evidence: town event `remove_quest_hero_level_restriction` ("Helping Hand: seasoned survivors help new recruits", info text "Level Restrictions Removed for Next Quest") **(inferred)**. In effect: difficulty 1 (Apprentice) admits level 0-2, difficulty 3 (Veteran) levels 0-4, difficulty 5/6 (Champion/Darkest) any level. | |
| Quest difficulty band by hero level (for quest generation) | resolve levels [0,1,2] -> difficulty 1; [2,3,4] -> 3; [4,5,6] -> 5 | `quest.generation.json` `difficulty.generated_resolve_level_difficulties` |
| Death | permanent. Death cause strings `str_death_*` (attack, bleed, poison, heart_attack from stress, hunger, trap, obstacle, ddexit, townexit). Party stress when a hero dies: `death_party_stress_chance` 0.5, `death_party_stress_damage` 12. Graveyard UI shows `Deaths: %d/%d` and `Week %d` per fallen hero. | `shared/rules.json`, strings |
| Dismissing a hero | other heroes take stress by dismissed hero level: `dismissed_hero_stress_penalties` = [{upper_level 4: 5}, {upper_level 12: 10}, {upper_level 1000000: 20}] (unit of `upper_level` unclear **(not verified)**). Cannot dismiss the last hero. | `rules.json`, `str_hero_cant_dismiss_confirm` |
| Stress between weeks | stress is a persistent hero field (`stress` in `persist.roster.json`), only reduced by town activities, idle heal, events, quest completion at the Darkest Dungeon (`is_roster_stress_cleared_on_completion` for DD quests), camping. Quest failure adds 20 stress to every hero in the party. | |
| Never Again (DD repeat) | `campaign/roster/roster.settings.json`: `strict` (heroes who finished a DD quest cannot re-enter) vs `permissive` (can enter with buff `never_again_affliction`, `resolve_check_percent` -0.25, minimum stress 80) | |
| Missing heroes | `go_missing` side effect: absent 1 or 2 weeks (`roster.missing_duration` in save) | 1.2 |
| Starting save (tutorial) | heroes: Reynauld (crusader), Dismas (highwayman) survive; party started with 4 (`start_heroes_size` 4); `starting_roster.darkest` is a name->class list of 17 named heroes (usage **not verified**) | `scripts/starting_save/*`, `scripts/starting_roster.darkest` |
| Prisoners (rescued heroes) | `dungeons/_shared/shared.json` `prisoner_spawn_data.hero_spawn_difficulty_table` keyed by quest difficulty: key 1: level 5, +2 pos quirks, 0 neg, 2 camping skills, 1-3 shared camping, 7 combat skills; key 3: level 3, pos 1-2, neg 1-2, camp 1-2, shared 1-3, combat 1-2; key 5 and 6: level 5, pos 2-3, neg 1-2, camp 1-2, shared 1-3, combat 2-3 (as read; key 1 looks like a special/tutorial entry) | |
| Town activity lock-out side effects | `activity_lock`, `go_missing` (1.2). Locked hero shows "Cancel Treatment" and cancelling gives no refund. | |
| Hero quirk limits (`rules.json`) | max positive 5, max negative 5, max diseases 3, max locked positive 3, max locked negative 3, negative quirk can lock after 2 turns with chance 0.25 (`quirk_chance_to_lock_negative`) | |

DD2 reimplementation note: the data above says WHAT the town does; hero stats/quirks themselves come from DD2.


## 3. Quests

Files in `<DD1>\campaign\quest\`: `quest.generation.json` (63 KB), `quest.plot_quests.json` (66 KB), `quest.types.json` (23 KB), `quest.restriction.json`, `quest.exit_penalty.json`, `number.quest.generation.json`. Dungeon progress: `campaign/progression/progression.json`.

### 3.1 Types, lengths, difficulties

* Quest type ids (`quest.types.json` `types`): `kill_boss`, `explore`, `cleanse`, `gather`, `activate`, `inventory_activate`. Each maps (per dungeon or `all`) to goal lists:
  * explore -> goal `explore_all_rooms` (`explore_room`, percentage 0.9)
  * cleanse -> goal `battle_all_rooms` (`battle_room`, percentage 1.0)
  * gather -> `gather_holy_relic` (crypts, curio `reliquary`, item `holy_relic` x3), `gather_medicines` (weald, `chirurgeons_satchel`, `medicines` x3), `gather_grain` (warrens, `foodstuff_crate`, `grain_sack` x3), `gather_shipments` (cove, `shipment_crates`, `ancestors_crate` x3)
  * activate -> `activate_iron_maiden` (amount 1), `activate_teleporter`
  * inventory_activate -> starting items 3 x quest item, then activate 3 curios: crypts `corrupted_altar` (holy_water), weald `infected_corpse` (antivenom), warrens `animalistic_shrine` (pickaxe), cove `protective_ward` (eldritch_lantern); DD2 `beacon` (beacon_light)
  * kill_boss -> goals `kill_<boss>_A/B/C` (`kill_monster`, amount 1; formless flesh: 4 of any `formless_*`), DD: `kill_shuffler_D`, `kill_ancestor_heart_D`, `kill_brigand_sapper_D`, crows
  * town progression goals (tutorial): `town_progression_explore` (3 rooms), `_battle` (2), `_trait` (1 affliction), `_deaths_door` (1)
* Length ids 1..4: Short, Medium, Long, Exhausting (`town_quest_length_*`). Camps per quest strings 0..4 (`town_quest_number_of_camps0..4`) and firewood granted per length in `provision.json` (sections 4.5 and 6.1).
* Difficulty ids: 1 Apprentice (Lvl 1), 3 Veteran (Lvl 3), 5 Champion (Lvl 5), 6 Darkest (Lvl 6) (`town_quest_difficulty_*`). Tables are indexed 0..6, odd indexes are the ones used.
* Quest art: `campaign/town/quest_select/quest_select_<type>_<difficulty>.png` (types activate, cleanse, explore, gather, inventory_activate, kill_boss x difficulties 1,3,5,6), `quest_select_length_generated_0..5.png` / `_length_plot_0..4.png`, per-plot images (`quest_select_plot_*`).

### 3.2 Weekly quest list generation (`quest.generation.json`, `number.quest.generation.json`)

Data present (algorithm itself is in the exe, so the combination below is **inferred**):

* `number.quest.generation.json`: `number_of_quests_per_town_visit_table` = [2, 6, 8, 9, 10, 11, 12, 13] (index likely the highest dungeon level 0..7).
* `dungeon.generated_quests_max_threshold` = 4; `dungeon.generated_dungeons` (dungeon becomes eligible after N finished quests overall): crypts 1, weald 3, warrens 4, cove 4.
* `difficulty.generated_resolve_level_difficulties`: see section 2.
* `type.available_quests_table`: per dungeon, 8 rows indexed by dungeon level 0..7, each a weighted list `{type, length, chance(=1)}`:
  * row 0: cleanse L1
  * row 1: explore L1, cleanse L1, cleanse L2, explore L2
  * row 2: row 1 + inventory_activate L2, gather L2
  * rows 3..7: explore/cleanse L1, L2, L3, inventory_activate L2, gather L2
  * identical for crypts, weald, warrens, cove (kill_boss and activate are never generated, they come from plot quests).
* Rewards for generated quests (`rewards` block):
  * Gold `item_table[difficulty][length]` (length index 1..3): d1 3000 / 4500 / 7500; d3 4500 / 6750 / 11250; d5 6000 / 9000 / 15000; d6 9000 / 13500 / 22500 (length 1/2/3).
  * Heirlooms `heirloom_amount_table[type][difficulty][length index 0..3]`: bust & portrait: d1 [0,2,2,4], d3 [0,2,3,6], d5 [0,3,5,9]; deed & crest: d1 [0,3,5,9], d3 [0,4,6,12], d5 [0,6,9,18]. `heirloom_type_map` lists all 4 types for every dungeon; how many types a quest rolls is not in data (**not verified**).
  * Resolve XP `resolve_xp_table[difficulty][length]`: d1 [0,2,3,4,4,4], d3 [0,4,6,8,8,8], d5 [0,8,12,16,16,16], d6 all 0 (plot quests set their own).
  * Trinket `trinket_chance_table[rarity][difficulty][length]` (entry 1 = one guaranteed trinket of that rarity): d1: short common, medium uncommon, long rare; d3: short uncommon, medium rare, long very_rare; d5: short rare, medium very_rare, long ancestral. (very_common never rolls here.)
* Dungeon progress (`progression.json`): `dungeon.level_threshold_table` = [0, 2, 6, 10, 16, 22, 32, 42] (cumulative dungeon XP for dungeon level 0..7); `quest_completion_xp_table` = [0, 2, 3, 4, 5, 6] (indexed by quest length **(inferred)**; only the tutorial plot quest sets `completion_dungeon_xp` true in plot data, generated quests award it via this table). Quest card tooltips use `town_quest_*` strings.
* `quest.restriction`: see section 2.

### 3.3 Plot / boss quests (`quest.plot_quests.json`, 34 entries)

Fields per entry: `id`, `dungeon_level` (dungeon level required before the quest is offered **(inferred)**), `quest{type, dungeon, difficulty, length, map_name, goal_ids, completion_reward{resolve_xp, items_definition}}`, `additional_trinket_completion_rewards`, `plot_quest_dependency`, `post_raid_video`, `is_progression`, `is_repeatable`, `has_statue_contents` (adds Memoirs entry), `completion_dungeon_xp`, `can_retreat`, `retreat_always_from_raid`, `retreat_party_kill_count`, `is_surprise_enabled`, `is_scouting_enabled`, `is_stall_enabled`, `is_roster_stress_cleared_on_completion`, `roster_buffs_to_apply_on_failure`, `party_quirks_to_apply_on_completion/failure`, `trinket_retention_*`, `suggested_trinkets`, `additional_provisions`, `is_generated_by_event`.

Boss progression pattern, per dungeon, two boss lines, three tiers each (A/B/C = difficulty 1/3/5), all `kill_boss` length 2 (medium):

| Dungeon level gate | Crypts | Weald | Warrens | Cove |
|---|---|---|---|---|
| 2 (d1) | necromancer_1 | hag_1 | swine_prince_1 | siren_1 |
| 3 (d1) | prophet_1 | brigand_cannon_1 | formless_flesh_1 | drowned_crew_1 |
| 4 (d3) | necromancer_2 | hag_2 | swine_prince_2 | siren_2 |
| 5 (d3) | prophet_2 | brigand_cannon_2 | formless_flesh_2 | drowned_crew_2 |
| 6 (d5) | necromancer_3 | hag_3 | swine_prince_3 | siren_3 |
| 7 (d5) | prophet_3 | brigand_cannon_3 | formless_flesh_3 | drowned_crew_3 |

Rewards: d1 4500 gold + dungeon heirlooms (crypts bust 4 + crest 6; weald deed 6 + crest 4; warrens portrait 3 + crest 4; cove crest 12), 4 resolve XP; d3 6750 gold (+bust 6/crest 8; deed 8/crest 5; portrait 4/crest 5; crest 18), 8 XP; d5 15000 gold + 30 crest + a boss trinket (`boss_necromancer`, `boss_prophet`, `boss_hag`, `boss_cannon`, `boss_wilbur`, `boss_flesh`, `boss_siren`, `boss_crew`), 16 XP. Every boss quest also gives 1 very_rare trinket roll.

Full table (generated from file):


| id | dlvl | type | dungeon | diff | len | map | goal | XP | rewards | trinket roll | flags |
|---|---|---|---|---|---|---|---|---|---|---|---|
| plot_tutorial_crypts | 0 | explore | crypts | 1 | 1 | tutorial_crypts | explore_all_rooms | 2 | gold - x3000; heirloom crest x4 | very_common x1 |  |
| plot_kill_necromancer_1 | 2 | kill_boss | crypts | 1 | 2 | generated | kill_necromancer_A | 4 | gold - x4500; heirloom bust x4; heirloom crest x6 | very_rare x1 |  |
| plot_kill_prophet_1 | 3 | kill_boss | crypts | 1 | 2 | generated | kill_prophet_A | 4 | gold - x4500; heirloom bust x4; heirloom crest x6 | very_rare x1 |  |
| plot_kill_necromancer_2 | 4 | kill_boss | crypts | 3 | 2 | generated | kill_necromancer_B | 8 | gold - x6750; heirloom bust x6; heirloom crest x8 | very_rare x1 |  |
| plot_kill_prophet_2 | 5 | kill_boss | crypts | 3 | 2 | generated | kill_prophet_B | 8 | gold - x6750; heirloom bust x6; heirloom crest x8 | very_rare x1 |  |
| plot_kill_necromancer_3 | 6 | kill_boss | crypts | 5 | 2 | generated | kill_necromancer_C | 16 | gold - x15000; heirloom crest x30; trinket boss_necromancer x1 | very_rare x1 |  |
| plot_kill_prophet_3 | 7 | kill_boss | crypts | 5 | 2 | generated | kill_prophet_C | 16 | gold - x15000; heirloom crest x30; trinket boss_prophet x1 | very_rare x1 |  |
| plot_kill_hag_1 | 2 | kill_boss | weald | 1 | 2 | generated | kill_hag_A | 4 | gold - x4500; heirloom deed x6; heirloom crest x4 | very_rare x1 |  |
| plot_kill_brigand_cannon_1 | 3 | kill_boss | weald | 1 | 2 | generated | kill_brigand_cannon_A | 4 | gold - x4500; heirloom deed x6; heirloom crest x4 | very_rare x1 |  |
| plot_kill_hag_2 | 4 | kill_boss | weald | 3 | 2 | generated | kill_hag_B | 8 | gold - x6750; heirloom deed x8; heirloom crest x5 | very_rare x1 |  |
| plot_kill_brigand_cannon_2 | 5 | kill_boss | weald | 3 | 2 | generated | kill_brigand_cannon_B | 8 | gold - x6750; heirloom deed x8; heirloom crest x5 | very_rare x1 |  |
| plot_kill_hag_3 | 6 | kill_boss | weald | 5 | 2 | generated | kill_hag_C | 16 | gold - x15000; heirloom crest x30; trinket boss_hag x1 | very_rare x1 |  |
| plot_kill_brigand_cannon_3 | 7 | kill_boss | weald | 5 | 2 | generated | kill_brigand_cannon_C | 16 | gold - x15000; heirloom crest x30; trinket boss_cannon x1 | very_rare x1 |  |
| plot_kill_swine_prince_1 | 2 | kill_boss | warrens | 1 | 2 | generated | kill_swine_prince_A | 4 | gold - x4500; heirloom portrait x3; heirloom crest x4 | very_rare x1 |  |
| plot_kill_formless_flesh_1 | 3 | kill_boss | warrens | 1 | 2 | generated | kill_formless_flesh_A | 4 | gold - x4500; heirloom portrait x3; heirloom crest x4 | very_rare x1 |  |
| plot_kill_swine_prince_2 | 4 | kill_boss | warrens | 3 | 2 | generated | kill_swine_prince_B | 8 | gold - x6750; heirloom portrait x4; heirloom crest x5 | very_rare x1 |  |
| plot_kill_formless_flesh_2 | 5 | kill_boss | warrens | 3 | 2 | generated | kill_formless_flesh_B | 8 | gold - x6750; heirloom portrait x4; heirloom crest x5 | very_rare x1 |  |
| plot_kill_swine_prince_3 | 6 | kill_boss | warrens | 5 | 2 | generated | kill_swine_prince_C | 16 | gold - x15000; heirloom crest x30; trinket boss_wilbur x1 | very_rare x1 |  |
| plot_kill_formless_flesh_3 | 7 | kill_boss | warrens | 5 | 2 | generated | kill_formless_flesh_C | 16 | gold - x15000; heirloom crest x30; trinket boss_flesh x1 | very_rare x1 |  |
| plot_kill_siren_1 | 2 | kill_boss | cove | 1 | 2 | generated | kill_siren_A | 4 | gold - x4500; heirloom crest x12 | very_rare x1 |  |
| plot_kill_drowned_crew_1 | 3 | kill_boss | cove | 1 | 2 | generated | kill_drowned_crew_A | 4 | gold - x4500; heirloom crest x12 | very_rare x1 |  |
| plot_kill_siren_2 | 4 | kill_boss | cove | 3 | 2 | generated | kill_siren_B | 8 | gold - x6750; heirloom crest x18 | very_rare x1 |  |
| plot_kill_drowned_crew_2 | 5 | kill_boss | cove | 3 | 2 | generated | kill_drowned_crew_B | 8 | gold - x6750; heirloom crest x18 | very_rare x1 |  |
| plot_kill_siren_3 | 6 | kill_boss | cove | 5 | 2 | generated | kill_siren_C | 16 | gold - x15000; heirloom crest x30; trinket boss_siren x1 | very_rare x1 |  |
| plot_kill_drowned_crew_3 | 7 | kill_boss | cove | 5 | 2 | generated | kill_drowned_crew_C | 16 | gold - x15000; heirloom crest x30; trinket boss_crew x1 | very_rare x1 |  |
| plot_town_invasion_0 | 0 | kill_boss | town | 6 | 1 | town_invasion_0 | kill_brigand_sapper_D | 16 | trinket boss_tassle x1 |  | by event; retreat kills 1; no scouting; no surprise |
| plot_trinket_retention_0 | 0 | kill_boss | weald | 1 | 1 | crow_map1 | kill_crow_A | 1 |  |  | repeatable; no normal retreat; no scouting; no surprise |
| plot_trinket_retention_1 | 0 | kill_boss | weald | 3 | 1 | crow_map1 | kill_crow_B | 2 |  |  | repeatable; no normal retreat; no scouting; no surprise |
| plot_trinket_retention_2 | 0 | kill_boss | weald | 5 | 1 | crow_map1 | kill_crow_C | 4 |  |  | repeatable; no normal retreat; no scouting; no surprise |
| plot_crow_trinket | 0 | kill_boss | weald | 5 | 1 | crow_map1 | kill_crow_C | 4 |  | crow x1 | repeatable; by event; no normal retreat; no scouting; no surprise |
| plot_darkest_dungeon_1 | 0 | kill_boss | darkestdungeon | 6 | 2 | DD_map1 | kill_shuffler_D | 16 | gold - x15000; heirloom crest x18; trinket_unlock dd_trinket x3 | very_rare x1 | retreat kills 1; no scouting; no surprise; clears roster stress |
| plot_darkest_dungeon_2 | 0 | inventory_activate | darkestdungeon | 6 | 3 | DD_map2 | inventory_activate_beacons | 16 | gold - x15000; heirloom crest x18 | very_rare x1 | dep plot_darkest_dungeon_1; retreat kills 1; no scouting; no surprise; clears roster stress |
| plot_darkest_dungeon_3 | 0 | activate | darkestdungeon | 6 | 4 | DD_map3 | activate_teleporter | 16 | gold - x15000; heirloom crest x18 | very_rare x1 | dep plot_darkest_dungeon_2; retreat kills 1; no scouting; no surprise; clears roster stress |
| plot_darkest_dungeon_4 | 0 | kill_boss | darkestdungeon | 6 | 1 | DD_map4 | kill_ancestor_heart_D | 16 | gold - x15000; heirloom crest x18 | very_rare x1 | dep plot_darkest_dungeon_3; video epilog; no normal retreat; retreat kills 1; no scouting; no surprise; clears roster stress |


Other plot quest facts:
* `plot_town_invasion_0` (event-generated, kill_brigand_sapper_D on `maps/town_invasion_0.dm`): ignoring it removes 3 `building` upgrades (`upgrade_tags_to_remove_on_ignore`); reward trinket `boss_tassle`; event needs week >= 40 and 4 heroes at level 5.
* `plot_trinket_retention_0/1/2` + `plot_crow_trinket` (weald `crow_map1.dm`, kill `crow_A/B/C`): keep up to 8 trinkets of rarity >= uncommon / rare / very_rare; corvid quests apply `corvids_*` quirks (completion/failure weights in file).
* Darkest Dungeon chain (4 steps, each depends on the previous; `has_statue_contents`, all clear roster stress on completion, failure applies buff `darkest_dungeon_failure_roster_resolve_xp` (+100% resolve XP) when party min level 5, retreat kills 1 hero):
  1. `plot_darkest_dungeon_1`: kill_boss, difficulty 6, length 2, map `DD_map1.dm`, goal `kill_shuffler_D`, 16 XP, 15000 gold + 18 crest + unlocks `dd_trinket` x3 (`trinket_unlock`)
  2. `_2`: `inventory_activate`, length 3, `DD_map2.dm`, goal `inventory_activate_beacons` (3 beacon_light quest items provided, activate 3 `beacon` curios), suggested trinket `dd_trinket` x3
  3. `_3`: `activate`, length 4, `DD_map3.dm`, goal `activate_teleporter`
  4. `_4`: kill_boss, length 1, `DD_map4.dm`, goal `kill_ancestor_heart_D`, `post_raid_video` "epilog", `can_retreat` false, no retreat
  Torch setting for these quests: `torch_setting: "dd4"` (burn_down_enabled false) in `scripts/raid_settings.json`.
* **Unlock condition for the first Darkest Dungeon quest is NOT in the data files** (`plot_darkest_dungeon_1` has `dungeon_level` 0 and no dependency; the string `town_quest_locked` = "Explore other regions to unlock this one." and `town_quest_dungeon_not_released` = "Enter this, the most dreaded of regions."). It is hard-coded in the exe **(not verified)**. Caretaker goals strings `str_caretaker_goal_hero_resolve` ("Raise a %s to Resolve Level 6") hint at the roster-level requirement.
* Caretaker goal strings list every plot quest (`str_caretaker_goal_plot_*`) with tier names (e.g. necromancer 1/2/3 = "Apprentice Necromancer" / "Necromancer" / "Necromancer Lord").


## 4. Dungeon generation and raid rules

### 4.1 Where the data is

| File | Role |
|---|---|
| `scripts/map_generator.darkest` | 44 `map:` blocks, one per (quest_type, size, dungeon): grid, counts and placement ranges (below) |
| `dungeons/<d>/<d>.<N>.mash.darkest` (N=1..5; darkestdungeon `.6`) | monster encounter pools. `hall:`/`room:`/`boss:`/`stall:`/`named:` rows `.chance W .types id id id [.name X]`. DD1 combat is NOT needed, only the pool structure. |
| `dungeons/<d>/<d>.conditional.<1,3,5>.mash.darkest`, `_shared/conditional.N` | conditional encounters (shambler / collector) |
| `dungeons/<d>/<d>.props.darkest` | curios, treasures, traps, obstacles, secret treasures, prison doors per dungeon (weights) |
| `dungeons/<d>/<d>.dungeon.json` | only `id_index`, `is_released`, `requires_quest_to_display` (cove 0, crypts 1, warrens 2, weald 3, darkestdungeon 4, town 5) |
| `props/trap_definitions.json`, `obstacle_definitions.json`, `prop_definitions.json`, `props/<d>/prop_definitions.json` | trap/obstacle/door behaviour |
| `shared/rules.json` | raid numeric rules (hunger, torch, scouting, surprise, camp, hallway stress, ...) |
| `maps/*.dm` | hand-authored maps: `tutorial_crypts`, `crow_map1`, `town_invasion_0`, `DD_map1..4` (DSON binary, magic `01 B1 00 00`, string census: tutorial ~9 rooms/9 corridors, DD_map1 ~15 rooms/19 corridors, DD_map2 ~18/23, DD_map3 ~27/38, DD_map4 ~3/2 + final, crow_map1 1 room + exit; counts are string-census estimates **(not verified)**) |
| `scripts/starting_save/persist.map.json` | JSON (non-binary) example of the runtime map structure |

Mash numbering: files `.1 .3 .5` carry stall rows and conditional files exist only for 1/3/5, so N matches quest difficulty 1/3/5 **(inferred)**; `.2 .4` look unused by the base quest set; `.6` is the Darkest Dungeon. Entry counts (crypts): N=1 hall 21 / room 14 / boss 2 / stall 5 / named 6; N=3 32 / 26 / 2 / 6 / 1; N=5 38 / 34 / 2 / 6 / 1. Encounter size is 2-4 monsters (1-4 for a few). Conditional rows (tier 1 / 3 / 5): `shambler_A/B/C` hall chance 0.01 / 0.08 / 0.12 with `.darkness_range 0 0` (only at torch 0), `.limit 1`, `.can_be_ambush false`; `collector_A/B/C` hall chance 0.03 / 0.04 / 0.05 with `.inventory_valid_item_percent_range 0.79 1.0` (needs a mostly full inventory), `.limit 1`.

### 4.2 Graph generator parameters (`scripts/map_generator.darkest`)

Parameter meaning (**inferred from names and from the tutorial map**): `gridsize W H` = grid of room cells; `spacing` = number of hallway tiles between adjacent rooms (tutorial corridor with spacing-4 style = 4 hall tiles + 2 door tiles); `base_room_number` / `goal_room_number` rooms to place; `base_corridor_number` corridors; `connectivity` = probability of extra loops; `min_final_distance` = minimum graph distance entrance -> final/boss room; `nudge_*` = jitter of cell positions; `hallway_*` = count ranges of corridors that get that content; `room_*` = count ranges of rooms; `total_room_battles` = rooms that contain battles; `room_guarded_curio` / `room_guarded_treasure` = rooms whose curio/treasure is guarded by a battle (all room curios/treasures are guarded because `room_curio`, `room_treasure`, `room_battle` are 0-0 in every block); `hallway_curio` = one constant per size class (9/14/19), exact meaning not verified.

Per-dungeon constants (same in every block of that dungeon): spacing crypts 4, weald 4, warrens 3, cove 4; connectivity crypts 0.9, weald 0.85, warrens 0.95, cove 0.9; nudge (span, neighbour_bias, map_centre_bias): crypts (0-1, 0.5, 0.5), weald (1-2, 1, -1), warrens (0-0, 0, 1), cove (2-2, -1, 2). `goal_room_number` always equals `base_room_number`; `room_battle`, `room_curio`, `room_treasure` always 0-0.

Rooms/corridors by quest: explore short 9/10, medium 14/15, long 19/20; cleanse short 7/8, medium 11/11, long 14/15; kill_boss medium 11/11, long 14/11; gather, activate, inventory_activate medium 13/11. Quest length -> size: 1 short, 2 medium, 3 long (plot DD quests use fixed maps).

Columns (ranges are min-max): grid W-H, spacing, connectivity, min final distance, hallway battles, traps, obstacles, curio, hunger tiles, battle rooms (total_room_battles), guarded curio rooms, guarded treasure rooms, secret rooms.


| quest | size | dungeon | rooms | corr | grid | spc | conn | minDist | hBattle | hTrap | hObst | hCurio | hHunger | roomBat | gCurio | gTreas | secret |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| explore | short | crypts | 9 | 10 | 4-3 | 4 | 0.9 | 3 | 2-4 | 2-3 | 0-2 | 9-9 | 1-3 | 1-3 | 0-1 | 1-1 | 0-0 |
| explore | short | weald | 9 | 10 | 4-4 | 4 | 0.85 | 3 | 2-4 | 1-2 | 1-3 | 9-9 | 1-3 | 1-3 | 0-1 | 1-1 | 0-0 |
| explore | short | warrens | 9 | 10 | 4-3 | 3 | 0.95 | 3 | 2-4 | 3-4 | 0-2 | 9-9 | 1-3 | 1-3 | 0-1 | 1-1 | 0-0 |
| explore | short | cove | 9 | 10 | 4-4 | 4 | 0.9 | 3 | 2-4 | 2-3 | 0-2 | 9-9 | 1-3 | 1-3 | 0-1 | 1-1 | 0-0 |
| explore | medium | crypts | 14 | 15 | 5-4 | 4 | 0.9 | 7 | 3-4 | 3-5 | 0-3 | 14-14 | 2-5 | 3-4 | 1-2 | 1-2 | 0-1 |
| explore | medium | weald | 14 | 15 | 5-5 | 4 | 0.85 | 7 | 3-4 | 2-4 | 1-4 | 14-14 | 2-5 | 3-4 | 1-2 | 1-2 | 0-1 |
| explore | medium | warrens | 14 | 15 | 6-3 | 3 | 0.95 | 7 | 3-4 | 4-6 | 0-2 | 14-14 | 2-5 | 3-4 | 1-2 | 1-2 | 0-1 |
| explore | medium | cove | 14 | 15 | 5-5 | 4 | 0.9 | 7 | 3-4 | 3-5 | 0-3 | 14-14 | 2-5 | 3-4 | 1-2 | 1-2 | 0-1 |
| explore | long | crypts | 19 | 20 | 5-5 | 4 | 0.9 | 10 | 7-7 | 4-6 | 0-4 | 19-19 | 2-6 | 7-7 | 2-3 | 2-3 | 0-1 |
| explore | long | weald | 19 | 20 | 6-5 | 4 | 0.85 | 10 | 7-7 | 3-5 | 1-5 | 19-19 | 2-6 | 7-7 | 2-3 | 2-3 | 0-1 |
| explore | long | warrens | 19 | 20 | 5-4 | 3 | 0.95 | 10 | 7-7 | 5-7 | 0-3 | 19-19 | 2-6 | 7-7 | 2-3 | 2-3 | 0-1 |
| explore | long | cove | 19 | 20 | 6-5 | 4 | 0.9 | 10 | 7-7 | 4-6 | 0-4 | 19-19 | 2-6 | 7-7 | 2-3 | 2-3 | 0-1 |
| kill_boss | medium | crypts | 11 | 11 | 5-4 | 4 | 0.9 | 3 | 4-5 | 3-5 | 0-3 | 14-14 | 2-5 | 4-5 | 1-3 | 1-3 | 0-1 |
| kill_boss | medium | weald | 11 | 11 | 6-3 | 4 | 0.85 | 3 | 4-5 | 2-4 | 1-4 | 14-14 | 2-5 | 4-5 | 1-3 | 1-3 | 0-1 |
| kill_boss | medium | warrens | 11 | 11 | 4-3 | 3 | 0.95 | 3 | 4-5 | 4-6 | 0-2 | 14-14 | 2-5 | 4-5 | 1-3 | 1-3 | 0-1 |
| kill_boss | medium | cove | 11 | 11 | 6-3 | 4 | 0.9 | 3 | 4-5 | 3-5 | 0-3 | 14-14 | 2-5 | 4-5 | 1-3 | 1-3 | 0-1 |
| kill_boss | long | crypts | 14 | 11 | 6-3 | 4 | 0.9 | 7 | 8-8 | 4-6 | 0-4 | 19-19 | 2-6 | 8-8 | 3-4 | 3-4 | 0-1 |
| kill_boss | long | weald | 14 | 11 | 6-4 | 4 | 0.85 | 7 | 8-8 | 3-5 | 1-5 | 19-19 | 2-6 | 8-8 | 3-4 | 3-4 | 0-1 |
| kill_boss | long | warrens | 14 | 11 | 4-4 | 3 | 0.95 | 7 | 8-8 | 5-7 | 0-3 | 19-19 | 2-6 | 8-8 | 3-4 | 3-4 | 0-1 |
| kill_boss | long | cove | 14 | 11 | 6-4 | 4 | 0.9 | 7 | 8-8 | 4-6 | 0-4 | 19-19 | 2-6 | 8-8 | 3-4 | 3-4 | 0-1 |
| cleanse | short | crypts | 7 | 8 | 4-3 | 4 | 0.9 | 3 | 2-4 | 2-3 | 0-2 | 9-9 | 1-3 | 2-4 | 1-1 | 1-1 | 0-0 |
| cleanse | short | weald | 7 | 8 | 5-3 | 4 | 0.85 | 3 | 2-4 | 1-2 | 1-3 | 9-9 | 1-3 | 2-4 | 1-1 | 1-1 | 0-0 |
| cleanse | short | warrens | 7 | 8 | 3-3 | 3 | 0.95 | 3 | 2-4 | 3-4 | 0-2 | 9-9 | 1-3 | 2-4 | 1-1 | 1-1 | 0-0 |
| cleanse | short | cove | 7 | 8 | 5-3 | 4 | 0.9 | 3 | 2-4 | 2-3 | 0-2 | 9-9 | 1-3 | 2-4 | 1-1 | 1-1 | 0-0 |
| cleanse | medium | crypts | 11 | 11 | 5-3 | 4 | 0.9 | 3 | 4-5 | 3-5 | 0-3 | 14-14 | 2-5 | 4-5 | 1-3 | 1-3 | 0-1 |
| cleanse | medium | weald | 11 | 11 | 6-3 | 4 | 0.85 | 3 | 4-5 | 2-4 | 1-4 | 14-14 | 2-5 | 4-5 | 1-3 | 1-3 | 0-1 |
| cleanse | medium | warrens | 11 | 11 | 4-4 | 3 | 0.95 | 3 | 4-5 | 4-6 | 0-2 | 14-14 | 2-5 | 4-5 | 1-3 | 1-3 | 0-1 |
| cleanse | medium | cove | 11 | 11 | 6-3 | 4 | 0.9 | 3 | 4-5 | 3-5 | 0-3 | 14-14 | 2-5 | 4-5 | 1-3 | 1-3 | 0-1 |
| cleanse | long | crypts | 14 | 15 | 5-4 | 4 | 0.9 | 7 | 8-8 | 4-6 | 0-4 | 19-19 | 2-6 | 8-8 | 2-4 | 2-4 | 0-1 |
| cleanse | long | weald | 14 | 15 | 5-5 | 4 | 0.85 | 7 | 8-8 | 3-5 | 1-5 | 19-19 | 2-6 | 8-8 | 2-4 | 2-4 | 0-1 |
| cleanse | long | warrens | 14 | 15 | 4-4 | 3 | 0.95 | 7 | 8-8 | 5-7 | 0-3 | 19-19 | 2-6 | 8-8 | 2-4 | 2-4 | 0-1 |
| cleanse | long | cove | 14 | 15 | 5-5 | 4 | 0.9 | 7 | 8-8 | 4-6 | 0-4 | 19-19 | 2-6 | 8-8 | 2-4 | 2-4 | 0-1 |
| gather | medium | crypts | 13 | 11 | 5-4 | 4 | 0.9 | 4 | 4-5 | 3-5 | 0-3 | 14-14 | 2-5 | 4-5 | 1-2 | 1-2 | 0-1 |
| gather | medium | weald | 13 | 11 | 6-4 | 4 | 0.85 | 4 | 4-5 | 2-4 | 1-4 | 14-14 | 2-5 | 4-5 | 1-2 | 1-2 | 0-1 |
| gather | medium | warrens | 13 | 11 | 5-3 | 3 | 0.95 | 4 | 4-5 | 4-6 | 0-2 | 14-14 | 2-5 | 4-5 | 1-2 | 1-2 | 0-1 |
| gather | medium | cove | 13 | 11 | 6-4 | 4 | 0.9 | 4 | 4-5 | 3-5 | 0-3 | 14-14 | 2-5 | 4-5 | 1-2 | 1-2 | 0-1 |
| activate | medium | crypts | 13 | 11 | 5-4 | 4 | 0.9 | 4 | 4-5 | 3-5 | 0-3 | 14-14 | 2-5 | 4-5 | 1-2 | 1-2 | 0-1 |
| activate | medium | weald | 13 | 11 | 6-4 | 4 | 0.85 | 4 | 4-5 | 2-4 | 1-4 | 14-14 | 2-5 | 4-5 | 1-2 | 1-2 | 0-1 |
| activate | medium | warrens | 13 | 11 | 5-3 | 3 | 0.95 | 4 | 4-5 | 4-6 | 0-2 | 14-14 | 2-5 | 4-5 | 1-2 | 1-2 | 0-1 |
| activate | medium | cove | 13 | 11 | 6-4 | 4 | 0.9 | 4 | 4-5 | 3-5 | 0-3 | 14-14 | 2-5 | 4-5 | 1-2 | 1-2 | 0-1 |
| inventory_activate | medium | crypts | 13 | 11 | 5-4 | 4 | 0.9 | 4 | 4-5 | 3-5 | 0-3 | 14-14 | 2-5 | 4-5 | 1-2 | 1-2 | 0-1 |
| inventory_activate | medium | weald | 13 | 11 | 6-4 | 4 | 0.85 | 4 | 4-5 | 2-4 | 1-4 | 14-14 | 2-5 | 4-5 | 1-2 | 1-2 | 0-1 |
| inventory_activate | medium | warrens | 13 | 11 | 5-3 | 3 | 0.95 | 4 | 4-5 | 4-6 | 0-2 | 14-14 | 2-5 | 4-5 | 1-2 | 1-2 | 0-1 |
| inventory_activate | medium | cove | 13 | 11 | 6-4 | 4 | 0.9 | 4 | 4-5 | 3-5 | 0-3 | 14-14 | 2-5 | 4-5 | 1-2 | 1-2 | 0-1 |


### 4.3 What occupies rooms and corridor tiles

* **Corridor** = area kind 1 (id like `coAB`, `corA`); tiles in order: door tile (tile type 2), N hallway tiles (type 1), door tile (type 2). Tutorial corridor: 6 tiles = 2 doors + 4 hall tiles, each 720 px wide (`sidepos.x` steps of 720). **Room** = area kind 0 (id `rooA`...), a single tile (type 3) 1920 px wide (`bounds` -960..960). Tile type numbers read from `persist.map.json` **(inferred meaning)**.
* **Tile fields** (`persist.map.json`): `type`, `light`, `content`, `hap`, `knowledge`, `cur` (hash of curio id), `sprite_id`, `trap` / `obstacle` (hash or 3435973836 = 0xCCCCCCCC = none), `area_id`, `door_to{area_to, tile_to, implied, type}`, `mappos{x,y}`, `texture_id` (wall variant), `sidepos`, `mash_index` (-1 none, >= 0 index into mash list). Area fields: `id`, `kind`, `unfilled`, `bounds`, `knowledge`, `reversed`, `door0..door7`. Map root: `bounds`, `entrance_id`, `final_room_id`, `areas`. Observed `content` values: 0 empty, 1 battle, 6 curio/treasure (with `cur` set), 7 unknown **(not verified)**.
* **Hallway content types** (map icons `panels/icons_map/marker_*`): battle, curio, hunger, obstacle, trap, secret; hallway tiles also have a light state icon `hall_clear` / `hall_dim` / `hall_dark` / `hall_door`.
* **Room types** (icons `room_*`): entrance, battle, curio, treasure, boss, empty, unknown, plus `LockedRoom` (+Blue/Green/Red/Yellow), `PrisonerRoom`, `marker_secret`, `marker_room_visited`.
* **Curios**: hall curio rows and room curio rows (weights) per dungeon in `*.props.darkest` (section 5). Room treasures: unlocked_strongbox 2, locked_strongbox 3, heirloom_chest 3 (weights, every normal dungeon); secret room: `secret_stash`. Prison doors (`prison_doors`): `<dungeon>_door_shovel` and `<dungeon>_door_key` weight 1 each: door types `door_shovel` (needs shovel) and `door_key` (needs door key / skeleton key item); `prisoner` prop -> `prisoner_talk` -> a rescued hero (section 2).
* **Traps** (`props/trap_definitions.json`; prop type `trap`, default success effect `Heal Stress TrapD`, 8 stress healed, default animation `sprung`): crypts `spikes`, weald `poison_cloud`, warrens `blade_wheel`, cove `lurker`; `spikes_ambush` (generates an ambush). Fail effects by difficulty level (default / 3 / 5): spikes: `Stress 2` + HP -25% / -28% / -30% max HP; poison_cloud: `Blight 1/2/3` + `Stress 2`; blade_wheel: `Bleed 1/2/3` + `Stress 2` + HP -10%; lurker: `Lurker Trap Debuff 1/3/5` + `Stress 2/2/3` + HP -10%. Effect magnitudes: `Stress 1..5` = 10, 15, 25, 50, 100 stress; `Blight 1` = 2 dmg/turn for 3 turns (2,2,3,3,4 for levels 1-5); `Bleed 1` = 2/turn for 3 turns; `Heal Stress TrapD` 8. (`effects/base.effects.darkest`.)
* **Obstacles** (prop type `obstacle`, default interaction `shovel_obstacle`): crypts `rubble`, weald `thorny_thicket`, warrens `rubble`, cove `shipwreck`, town `town_rubble`; `ancestor` obstacle (`ancestor_talk` true, DD). Default values: using a shovel clears it free (`clear_obstacle`, item `shovel`); forcing through: `fail_effects` `Stress 2` (15 stress) on the party, HP -5% max (`health -0.05`), **torchlight -20** (`torchlight -20.0`). `torch_obstacle` interaction exists (clear with torch). Obstacle clearing can kill a hero (`str_death_obstacle_obstacle`).
* `difficulty_trap_base` (rules) = [0,0,0,0.2,0.2,0.4,0.5] by difficulty index; `trap_scout_disarm_bonus` 0.4 (scouted traps are easier to disarm); heroes' own trap-disarm stat is DD1 hero data **(not in this survey)**.
* Hallway walking stress (`rules.hallway_stress`): per hallway tile stress 2 with chance 0.3 going forward; 5 with chance 0.55 when backing up.
* **Return trip** (`rules.corridor_return_content`): when walking back through an already-cleared corridor, new content can appear: `ac_battle` base 0.05, `ac_hunger` 0.075, `ac_trap` 0.05, each +0.025 at torch 25-50 and 0-25, +0.05 at torch <= 0 (0 at torch > 50).
* **Hunger** (hallway tile): choice `str_ui_hunger_choice_eat` "Eat %.0f food, regain %d%% health" (`hallway_hunger_HPrestore` 0.05) or `..._starve` "Eat nothing, take %d%% damage plus stress damage" (`hallway_hunger_starve_HPdmg` 0.20 of max HP, +stress). `max_provisions_before_full` 4; `hunger_room_buffer` (raid inventory) 2 in the starting save; `provision_hp_heal` 0.05.
* **Scouting** (`rules`): `scouting_chance_base` 0.25, `scouting_crit_success` 0.5, `scouting_enter_dungeon_scout_chance` 0.0, `scouting_chance_scout_treasure` 0.0; party starts with `scoutChance_all` 0.33 in the sample save; torch bonus `player_scouting_increase` +15 / +7.5 / 0 / 0 / 0 per band (below); camping skill `scout_ahead` buff, trinkets add scouting chance; curios can reveal map (Scouting results in section 5, format `<N> - <target>` with N = reach and 0 = whole map **(inferred)**, targets `all`, `curios`, `traps`, `obstacles`, `hall_battles`, `room_battles`). Scouting UI: `panels/icons_map/scoutingbanner.png`.
* **Surprise / ambush** (`rules`): corridor / room: party surprised 0.10, monsters surprised 0.10; known (scouted) corridor/room: party -1 (never), monsters 0.25; ambush: party 1.0, monsters 0.0; caps 0.65 each. `ambush_camping_base_chance` 0.33. Torch band adds `monsters_surprised_increase` / `heroes_surprised_increase` (below).
* **Retreat**: heroes may retreat from a quest only if `can_retreat`; DD quests kill 1 random hero on retreat (`retreat_party_kill_count` 1). Combat retreat chance 0.7 (+0.05 per attempt), 20 stress, `min_hero_count_for_escape` 3.
* **Secret rooms**: `.secret_rooms` 0-1 (medium/long), 0-0 (short); content `secret_room_treasures` = `secret_stash` (loot table B x3, key -> COLLECTOR x3). Art: `dungeons/_shared/secretroom.png` (1920x720).
* **Ambush from traps**: `spikes_ambush` trap generates an ambush (`generate_ambush: trap`).

### 4.4 Torchlight (light) bands and effects (`shared/rules.json` `darkness`, keyed by torch value 0-100)

Torch starts at 100 (`persist.raid.json` `party.torchlight`), burn-down enabled in default raids (`scripts/raid_settings.json`, disabled for `dd4`). `tile_light_loss` = {clear 1.0, dim 6.0, dark 6.0} (torch lost per hallway tile by tile light state **(inferred)**). Curio/effect torch changes: `Darkness 1/2/3` -5/-10/-25; `Light 1/2` +6/+10; `ChemicalLight` +100; camp restores torch to 100 (`camp_restore_torch`); `ambush_torch_reduction` -100; the item `torch` is a normal supply (stack 8) whose light value is not in the data files I found **(not verified)**. Obstacle force-through costs -20.

| Torch value band | stress_chance_increase | stress_damage_increase | monster_attack_increase | monster_damage_increase | monster_crit_increase | loot_increase_gold | loot_increase_treasure_draws | player_crit_increase | player_def_increase | player_scouting_increase | monsters_surprised_increase | heroes_surprised_increase |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| > 75 (radiant) | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | +4 | +15 | +25 | 0 |
| 50 - 75 | +5 | +10 | 0 | 0 | +1 | 0 | 0 | 0 | +2.5 | +7.5 | +15 | 0 |
| 25 - 50 | +10 | +20 | +5 | +10 | +2 | +10 | +4 | +1 | 0 | 0 | +10 | +15 |
| 0 - 25 | +25 | +30 | +10 | +15 | +3 | +15 | +8 | +2 | 0 | 0 | +5 | +25 |
| <= 0 (out) | +35 | +40 | +12.5 | +25 | +5 | +30 | +12 | +3 | 0 | 0 | 0 | +40 |

(units: percentages / points as in the file; `lowest_excluded` true.) Loot bonus draws by torch (`loot/darkness_overrides.loot.json`): battle: torch 0 -> 75% chance of 2 extra `B` draws, 1 -> 75% 1x `B`, 25 -> 50%, 51 -> 25%, 76 -> 0; chest: torch 0 -> 95% 1x extra `A`, 1 -> 75%, 25 -> 50%, 51 -> 25%, 76 -> 0. Visuals: `colour_grade_0..4.png` per dungeon (5 torch bands), `scripts/raid.lighting.darkest` (light falloff, flicker, camp light), `torch_min`.

### 4.5 Camping (summary)

* Starting camping points `camp_start_camping_points` 12; `camp_rest_point_threshold` 6; `camp_rest_number_of_hero_threshold` 1; max 3 camping skills per hero; `camp_relieve_stress` 0; camp restores torch to 100; ambush chance while camping 0.33 (camp skills `reduce_ambush_chance`).
* Camps per quest: tied to starting `firewood` by length: short 0, medium 1, long 2, exhausting 4 (`provision.json` `raid_starting_length_inventory_item_lists` indices 1..4; index 5 = 0).
* Meals (`rules.meals_table`): none (0 ration, -20% HP, +15 stress), half (0.5 ration, 0, 0), full (1 ration, +10% HP, 0), feast (2 rations, +25% HP, -10 stress). UI strings `str_meal_title_0..3`, `str_ui_meal_title` "Repast".
* Camping skills: `raid/camping/default.camping_skills.json` (64 skills). Fields: `id`, `level` (0), `cost` (camping points), `use_limit` (1; one skill has 3), `effects[]` {`selection` (self / individual / party_other / party), `requirements`, `chance{code, amount}`, `type`, `sub_type`, `amount`}, `hero_classes`, `upgrade_requirements` (gold 1750). Cost histogram: 1 pt x6, 2 x16, 3 x28, 4 x13, 5 x1. Effect types: buff 89, stress_heal_amount 28, stress_damage_amount 13, health_heal_max_health_percent 9, reduce_ambush_chance 5, remove_disease 4, remove_bleeding 3, remove_poison 3, loot 3, remove_deaths_door_recovery_buffs 2, reduce_torch 1, health_damage_max_health_percent 1. Each skill lists its `hero_classes`; example `encourage` (2 pts, stress_heal_amount 15 on one hero) lists all 16 classes incl. musketeer, others are class-specific.

### 4.6 Other raid rules worth keeping (`shared/rules.json`)

| Key | Value |
|---|---|
| `curioTriggerChance_low / med / high` | 0.15 / 0.25 / 0.5 (barks/triggers) |
| `effectiveDifficultyDungeonStartingStress` by difficulty 0..6 | [0, 20, 30, 40, 50, 60, 70] (**meaning not verified**) |
| `effectiveDifficultyStressDmgModifiers` | [0, 0.25, 0.5, 0.75, 1.0, 1.25, 1.5] |
| `effectiveDifficultyAfflictionOnsetModifiers` | [0, 0.05, 0.10, 0.15, 0.20, 0.25, 0.33] |
| `disease_after_quest_min_resolve_level` / `min_chance` / `max_chance` | 2 / 0.05 / 0.32 |
| `deaths_door_*` stress | self 8 (repeat 4), companions 4 (repeat 2) with chances 1.0 / 0.25 |
| `passed_turn_stress_dmg` | 5 |
| `trinkets_equipped_warning_min_percent` | 0.5 |
| `stall_*` | `stall_party_size_threshold` 3, `stall_enemy_size_threshold` 2, rounds 3/4 (stress effect `stall_stress`, then summon from `stall:` mash rows) |


## 5. Curios

### 5.1 Files and counts

* `<DD1>\curios\curio_type_library.csv` (958 lines, comma separated spreadsheet export, blocks of 16 lines) defines **60 curio types**: interaction outcomes (the engine reads it; the extra columns are design notes).
* `<DD1>\curios\curio_props.csv` (70 lines): `Curio Prop Name, Sprite Reference, Curio Type, UI String Name, Audio Event Name` (maps prop id -> art id -> curio type; quest curios: `shipment_crates` -> `quest_gather`, `protective_ward` -> `quest_activate_with`; other quest curios such as `reliquary`, `teleporter`, `beacon`, `corrupted_altar` have art in `props/shared/curios/` and are defined by interaction types in `props/prop_definitions.json`, no entry in the 60).
* Per dungeon usage (`*.props.darkest`): crypts hall 13 / room 5 / treasure 3 (+secret_stash) = 22 curio ids; weald 12 / 4 / 3 (+1) = 20; warrens 12 / 4 / 3 (+1) = 20; cove 10 / 4 / 3 (+1) = 18; darkestdungeon 5 hall+room curios (stack_of_books, discarded_pack, sconce, crate, sack) + 3 treasures. Region tags in the library: ALL 14, Ruins 15, Weald 12, Warrens 9, Cove 8, Darkest Dungeon 2.
* Hall (hallway) curios total weights: crypts 79, weald 79, warrens 76, cove 66. Room curios 9-11. Room treasures 8 (2+3+3). Traps and obstacles: 1 type per dungeon (weight 1).

### 5.2 Outcome model

Each curio block: `ID STRING`, `REGION FOUND`, `FULL CURIO?`, `TAGS`, then a result table. Results (`RESULT TYPES`): Nothing, Loot, Quirk, Effect, Purge, Scouting, Teleport, Disease (all 60 blocks list all eight, unused ones blank). Columns: `WEIGHT` (weight of this result type), `% CHANCE`, then up to 3 sub-results `RESULT n`, `Rn WEIGHT`, `Rn %`.

* Procedure without an item (**reading, not verified against engine**): pick a result type by weight; pick a sub-result by sub-weight (Effect / Quirk / Disease / Scouting / Purge); for Loot the cells are `table code` + `<- # Draws` (the number is draws from loot table `code`, no probability) and ALL listed table/draw pairs are executed.
* Quirk results: `positive` / `negative` (random quirk of that sign) or a named quirk; Disease: named disease or `random`; Purge: `negative` (remove a negative quirk); Effect: effect ids from `effects/base.effects.darkest` (Stress N, Blight N, Bleed N, Darkness N, Light N, plus unique ones); Scouting `<N> - <target>`; `Teleport` and `Summon` exist as types (shamblers_altar -> Summon `summon_mash_shambler`).
* `Item Interactions`: row = item id (`skeleton_key`, `shovel`, `holy_water`, `torch`, `antivenom`, `bandage`, `medicinal_herbs`, `provision`, `dog_treats`), its own result type and sub-results, plus `CURIO TRACKER ID` (icon in `panels/icons_curio_tracker`: loot, buff, debuff, heal_gen, heal_stress, nothing, purge_neg ...; 15 icons) and `STRING` (text id suffix). Using the correct item usually removes the trap branch and adds draws.
* Loot table codes (`loot/loot.json` + `common_overrides.loot.json`): `A` = chests (C 50, G 38, H 48, S 28, T 8, journal page 2; J, P 0), `B` = battle (C 3, G 1), `C` gold by difficulty, `G` gems, `H` heirlooms by dungeon and difficulty, `S` supplies, `T` trinkets by rarity, `P` provisions, `CH`, `CS`, `CT`, `GH`, `GT`, `SC`, `JOURNALONLY`, `TORCHONLY`, `SHOVELONLY`, `KEYONLY`, `HOLYONLY`, `THANKS`, `COLLECTOR`, `T_ANCESTOR`, ... (full contents in section 6).
* Text ids (`localization/curios.string_table.xml`): `str_curio_title_<id>`, `str_curio_content_<id>`, `str_curio_tooltip_investigate_<id>`, `str_curio_<id>_<result>` (nothing / loot / effect / purge / scout / quirk), `str_curio_<id>_<item>_<result>` for item variants; generic: `curio_tooltip_pass` "Ignore", `curio_tooltip_item_slot`.
* Art: Spine 2.1.27 in `props/shared/curios/<id>/<id>.{skel,atlas,png}` (static frames `closed/open/active` etc. are plain atlas regions, section 9).

### 5.3 Three worked examples (from the CSV)

**A. `locked_strongbox` (Mixed, region ALL, tags Treasure/CCrave)**
* No item: Loot weight 1 (50%): table `A` x2 draws; Effect weight 1 (50%): `Blight 1` (50%) or `Bleed 1` (50%) (trap). Nothing / Quirk / Purge / Scouting / Teleport / Disease unused.
* `skeleton_key`: Loot `A` x3 draws, no trap ("safely unlocks the chest, disarming any trap").
* `shovel`: Nothing 50% (contents destroyed) else Loot `A` x2.
* Texts: `str_curio_locked_strongbox_nothing/effect`, `..._skeleton_key_loot`, `..._shovel_nothing/loot`.

**B. `eldritch_altar` (Mixed, ALL, tags Haunted/Unholy)**
* No item: Nothing weight 5 (50%); Effect weight 4 (40%): `Stress 3` (25 stress to the investigating hero); Purge weight 1 (10%): remove a negative quirk.
* `holy_water`: Purge `negative` (100%) ("the holy water purifies the altar").
* Room curio in every dungeon, weight 1.

**C. `travellers_tent` (Mixed, Weald, tags Scrounging/CCrave; room curio weight 3)**
* No item: Nothing weight 1 (12.5%); Loot weight 3 (37.5%): `SC` x4 draws + `CH` x4 draws + `JOURNALONLY` x1 draw; Effect weight 1 (12.5%): `Stress 3`; Scouting weight 3 (37.5%): `3 - obstacles` (weight 2, 40%), `2 - traps` (2, 40%), `2 - all` (1, 20%).
* No item interactions. A tutorial variant `travellers_tent_tutorial` (Loot `B` x2 only).

### 5.4 All 60 curios (generated from the CSV)

Columns: id, alignment (Good/Mixed/Bad), region, tags, result types with weight (percent) and sub-results `name*weight`, item interactions `item->result[sub-results]`. Loot sub-results are `table*draws`.


| # | id | align | region | tags | results | item interactions |
|---|---|---|---|---|---|---|
| 1 | unlocked_strongbox | Good | ALL | Treasure/All/CCrave | Loot 3 (75.00%) [A*2]; Effect 1 (25.00%) [Blight 1*1] |  |
| 2 | locked_strongbox | Mixed | ALL | Treasure/All/CCrave | Loot 1 (50.00%) [A*2]; Effect 1 (50.00%) [Blight 1*1, Bleed 1*1] | skeleton_key->Loot[A*3]; shovel->Loot[Nothing*1, A*2] |
| 3 | goal_strongbox | Good | ALL | Treasure/Goal/All/CCrave | Loot 1 (100.00%) [B*3, T*1] |  |
| 4 | heirloom_chest | Mixed | ALL | Treasure/All/CCrave | Loot 3 (75.00%) [H*2]; Effect 1 (25.00%) [Blight 1*1, Bleed 1*1] | skeleton_key->Loot[H*4]; antivenom->Loot[H*3] |
| 5 | eldritch_altar | Mixed | ALL | Haunted/Unholy/All | Nothing 5 (50.00%) []; Effect 4 (40.00%) [Stress 3*1]; Purge 1 (10.00%) [negative*1] | holy_water->Purge[negative*1] |
| 6 | altar_of_light | Good | Ruins | Worship | Effect 1 (100.00%) [Curio Damage Buff*1] | holy_water->Effect[Curio Damage BuffStrong*1] |
| 7 | stack_of_books | Mixed | ALL | Knowledge/All | Nothing 2 (16.67%) []; Loot 2 (16.67%) [JOURNALONLY*1]; Quirk 4 (33.33%) [positive*2, negative*1]; Effect 4 (33.33%) [Stress 3*2, Darkness 3*1] | torch->Effect[Stress 5*1] |
| 8 | iron_maiden | Mixed | Ruins | Torture/Haunted/All | Nothing 1 (20.00%) []; Loot 2 (40.00%) [A*2]; Quirk 1 (20.00%) [claustrophobia*1]; Disease 1 (20.00%) [tetanus*2, random*1] | medicinal_herbs->Loot[A*2] |
| 9 | pile_of_bones | Mixed | Warrens | Haunted/Unholy/All/Body/CCrave | Nothing 1 (25.00%) []; Loot 1 (25.00%) [A*2]; Quirk 1 (25.00%) [bloodthirsty*1]; Disease 1 (25.00%) [random*1] | holy_water->Loot[A*2] |
| 10 | discarded_pack | Good | ALL | Scrounging/All/Treasure/CCrave | Nothing 1 (20.00%) []; Loot 3 (60.00%) [S*1, B*2, JOURNALONLY*1]; Scouting 1 (20.00%) [2 - all*3, 3 - all*2, 0 - all*1] |  |
| 11 | sconce | Good | ALL | Scrounging/All | Loot 1 (100.00%) [TORCHONLY*1] |  |
| 12 | crate | Good | ALL | Scrounging/All | Nothing 1 (25.00%) []; Loot 3 (75.00%) [H*1] |  |
| 13 | sack | Good | ALL | Scrounging/All | Nothing 1 (25.00%) []; Loot 3 (75.00%) [C*1] |  |
| 14 | suit_of_armor | Good | Ruins | Haunted/Reflective/All | Quirk 1 (25.00%) [ruins_adventurer*1, ruins_tactician*1]; Effect 3 (75.00%) [Suit of Armor Shield*1] |  |
| 15 | dinner_cart | Mixed | Warrens | Food/Drink/All/Body/CCrave | Nothing 2 (25.00%) []; Loot 3 (37.50%) [P*1, CT*1]; Effect 2 (25.00%) [Blight 1*1]; Disease 1 (12.50%) [the_black_plague*1] | medicinal_herbs->Loot[P*3, CT*1] |
| 16 | decorative_urn | Mixed | Ruins | Scrounging/Haunted/All | Nothing 2 (22.22%) []; Loot 4 (44.44%) [GT*1, G*2]; Effect 2 (22.22%) [Blight 1*1]; Disease 1 (11.11%) [creeping_cough*2, random*1] | holy_water->Loot[GT*2, G*2]; shovel->Quirk[guilty_conscience*1] |
| 17 | locked_display_cabinet | Bad | Ruins | Scrounging/Treasure/All | Effect 1 (100.00%) [Blight 1*1, Bleed 1*1] | skeleton_key->Loot[CH*3, B*2]; shovel->Loot[CH*2, B*2] |
| 18 | confession_booth | Mixed | Ruins | Worship/Reflective/All | Loot 1 (25.00%) [CT*6, JOURNALONLY*1]; Effect 2 (50.00%) [Stress 2*1]; Purge 1 (25.00%) [negative*1] | holy_water->Effect[ConfessionBoothHealStress2*1] |
| 19 | holy_fountain | Good | Ruins | Fountain/Worship/All | Loot 1 (50.00%) [B*2]; Effect 1 (50.00%) [HolyFountainEffects*1] | holy_water->Effect[HolyFountainEffectsSuper*1] |
| 20 | bookshelf | Mixed | Ruins | Knowledge/All | Nothing 2 (20.00%) []; Loot 2 (20.00%) [JOURNALONLY*1]; Quirk 2 (20.00%) [positive*2, negative*1]; Effect 2 (20.00%) [Stress 2*1]; Scouting 2 (20.00%) [0 - curios*2, 0 - all*1] |  |
| 21 | alchemy_table | Mixed | Ruins | Knowledge/All/CCrave | Nothing 1 (25.00%) []; Loot 1 (25.00%) [B*1]; Effect 2 (50.00%) [Blight 1*2] | medicinal_herbs->Loot[B*2]; torch->Effect[ChemicalLight*1] |
| 22 | knife_rack | Mixed | Warrens | Scrounging/All | Nothing 1 (20.00%) []; Loot 2 (40.00%) [B*1, P*1]; Effect 2 (40.00%) [Bleed 1*1] | bandage->Loot[B*2, P*1] |
| 23 | sarcophagus | Mixed | Ruins | Haunted/Reflective/All/Body | Nothing 1 (20.00%) []; Loot 3 (60.00%) [CH*2]; Quirk 1 (20.00%) [thanatophobia*1] |  |
| 24 | mummified_remains | Mixed | Weald | Haunted/Unholy/All/Body | Nothing 1 (20.00%) []; Loot 2 (40.00%) [CT*1]; Effect 2 (40.00%) [Blight 1*1] | bandage->Loot[CT*2] |
| 25 | sacrificial_stone | Mixed | Warrens | Unholy/Worship/All | Quirk 1 (25.00%) [warren_explorer*1, warren_scrounger*1]; Effect 2 (50.00%) [Stress 4*1]; Purge 1 (25.00%) [negative*1] | provision->Purge[negative*1] |
| 26 | skull_altar | Good | Warrens | Unholy/Worship/All | Effect 1 (100.00%) [Skull Altar Adrenaline*2] |  |
| 27 | occult_scrawlings | Mixed | Warrens | Unholy/Knowledge/All | Nothing 1 (25.00%) []; Quirk 2 (50.00%) [positive*2, negative*1]; Effect 1 (25.00%) [Stress 3*1] | holy_water->Effect[UnnamedCurse*1] |
| 28 | moonshine_barrel | Mixed | Warrens | Drink/Scrounging/All/CCrave | Nothing 2 (22.22%) []; Loot 3 (33.33%) [P*1, CS*1]; Quirk 1 (11.11%) [alcoholism*1]; Effect 3 (33.33%) [Blight 1*1] | medicinal_herbs->Effect[Curio Damage BuffStrong*1] |
| 29 | locked_sarcophagus | Bad | Ruins | Haunted/Reflective/All | Effect 1 (100.00%) [Blight 1*1, Bleed 1*1] | skeleton_key->Loot[CH*2, CT*1]; shovel->Loot[CH*1, CT*1] |
| 30 | makeshift_dining_table | Mixed | Warrens | Food/Drink/All/CCrave | Nothing 1 (25.00%) []; Loot 1 (25.00%) [P*1, CS*1]; Effect 1 (25.00%) [Blight 1*1]; Disease 1 (25.00%) [tapeworm*1] | medicinal_herbs->Loot[P*2, CS*1] |
| 31 | pile_of_scrolls | Mixed | Warrens | Knowledge/All | Nothing 2 (28.57%) []; Loot 1 (14.29%) [JOURNALONLY*1]; Quirk 1 (14.29%) [positive*2, negative*1]; Effect 1 (14.29%) [Stress 2*1]; Scouting 2 (28.57%) [0 - hall_battles*2, 0 - room_battles*2, 2 - all*1] | torch->Purge[negative*1] |
| 32 | ancient_coffin | Good | Weald | Haunted/Body/All | Nothing 2 (33.33%) []; Loot 3 (50.00%) [CH*2]; Quirk 1 (16.67%) [weald_explorer*1, weald_adventurer*1] |  |
| 33 | old_tree | Mixed | Weald | Scrounging/All | Nothing 1 (25.00%) []; Loot 2 (50.00%) [A*2]; Effect 1 (25.00%) [Blight 1*1] | antivenom->Loot[A*3] |
| 34 | beast_carcass | Mixed | Weald | Food/All/Body/CCrave | Nothing 1 (14.29%) []; Loot 3 (42.86%) [P*1]; Quirk 1 (14.29%) [zoophobia*1]; Disease 2 (28.57%) [rabies*1, random*2] | medicinal_herbs->Loot[P*2] |
| 35 | pristine_fountain | Good | Weald | Fountain/All | Effect 1 (100.00%) [PristineFountainHealStress*1] | holy_water->Effect[PristineFountainHealStress2*1] |
| 36 | travellers_tent | Mixed | Weald | Scrounging/All/CCrave | Nothing 1 (12.50%) []; Loot 3 (37.50%) [SC*4, CH*4, JOURNALONLY*1]; Effect 1 (12.50%) [Stress 3*1]; Scouting 3 (37.50%) [3 - obstacles*2, 2 - traps*2, 2 - all*1] |  |
| 37 | cosmic_spiderweb | Mixed | Weald | All | Nothing 2 (40.00%) []; Loot 2 (40.00%) [B*1, GT*1]; Quirk 1 (20.00%) [slow_reflexes*1, slowdraw*1] | bandage->Loot[B*2, GT*1] |
| 38 | lost_luggage | Mixed | Weald | Treasure/Scrounging/All | Loot 1 (50.00%) [A*4, JOURNALONLY*1]; Effect 1 (50.00%) [Blight 1*1] | skeleton_key->Loot[A*3]; antivenom->Loot[A*3] |
| 39 | shallow_grave | Bad | Weald | Scrounging/Haunted/All/Body | Effect 1 (50.00%) [Blight 1*1]; Disease 1 (50.00%) [random*1] | shovel->Loot[GH*3] |
| 40 | troubling_effigy | Mixed | Weald | Unholy/Worship/All | Nothing 2 (25.00%) []; Quirk 3 (37.50%) [positive*1, negative*1]; Effect 3 (37.50%) [Blight 1*1, Stress 2*1, Bleed 1*2] | holy_water->Quirk[positive*1] |
| 41 | barnacle_crusted_chest | Good | Cove | Treasure/All | Nothing 1 (25.00%) []; Loot 2 (50.00%) [A*2]; Effect 1 (25.00%) [Bleed 1*1] | shovel->Loot[A*3] |
| 42 | brackish_tidepool | Bad | Cove | All/Fountain/Drink/CCrave | Effect 3 (75.00%) [brackish_tidepool*1]; Disease 1 (25.00%) [random*1] | antivenom->Effect[HealSelf 3*1, Heal Stress 3*1] |
| 43 | fish_idol | Mixed | Cove | Worship/Unholy/All | Effect 1 (100.00%) [fish_idol_curse_1*1, fish_idol_curse_2*1] | holy_water->Effect[Fish Idol Damage Buff Small*1] |
| 44 | giant_oyster | Mixed | Cove | Scrounging/Treasure/All | Nothing 1 (20.00%) []; Loot 2 (40.00%) [CT*2]; Effect 2 (40.00%) [Bleed 2*1] | shovel->Loot[CT*3]; dog_treats->Effect[Oyster Dodge Buff*1] |
| 45 | giant_fish_carcass | Mixed | Cove | All/Food/CCrave | Nothing 3 (50.00%) []; Loot 1 (16.67%) [GT*1, B*1, S*1]; Effect 1 (16.67%) [Bleed 1*2, Blight 1*1]; Disease 1 (16.67%) [the_red_plague*1] | medicinal_herbs->Loot[GT*2, B*1, S*2] |
| 46 | ships_figurehead | Good | Cove | All/Reflective | Effect 1 (100.00%) [Figurehead Buff*1, Figurehead Heal Stress*2] |  |
| 47 | eerie_coral | Mixed | Cove | All/Knowledge | Nothing 1 (25.00%) []; Effect 3 (75.00%) [eerie_coral_healstress*2, eerie_coral_stress*1] | medicinal_herbs->Purge[negative*1] |
| 48 | travellers_tent_tutorial | Good | Weald | Scrounging/All | Loot 1 (100.00%) [B*2] |  |
| 49 | bandits_trapped_chest | Bad | Weald | All/CCrave | Effect 1 (100.00%) [Blight 1*1] | skeleton_key->Loot[T*2] |
| 50 | shamblers_altar | Mixed | ALL | All | Nothing 1 (100.00%) [] | torch->Summon[summon_mash_shambler*1] |
| 51 | bas_relief | Mixed | Cove | All/Knowledge/Worship | Quirk 1 (100.00%) [positive*2, negative*1] | shovel->Effect[Stress 5*1] |
| 52 | secret_stash | Good | ALL | All/Treasure | Loot 1 (100.00%) [B*3] | skeleton_key->Loot[COLLECTOR*3] |
| 53 | ancestors_knapsack | Good | Darkest Dungeon | All/Treasure/Scrounging/Haunted/CCrave | Loot 1 (100.00%) [T_ANCESTOR*1, B*5] |  |
| 54 | tutorial_shovel | Good | Ruins | All/Treasure | Loot 1 (100.00%) [SHOVELONLY*1, A*2] |  |
| 55 | tutorial_key | Good | Ruins | All/Treasure | Loot 1 (100.00%) [KEYONLY*1, A*1] |  |
| 56 | tutorial_holy | Good | Ruins | All/Treasure | Loot 1 (100.00%) [HOLYONLY*1, A*1] |  |
| 57 | tutorial_food | Good | Ruins | All/Treasure | Loot 1 (100.00%) [P*2, A*2] |  |
| 58 | thanks_chest | Good | Darkest Dungeon | All/Treasure | Loot 1 (100.00%) [THANKS*1] |  |
| 59 | open_grave | Bad | ALL | All/Haunted/Unholy/Reflective | Effect 1 (100.00%) [Stress 5*1] |  |
| 60 | incursion_knapsack | Good | ALL | All/Treasure/Scrounging/Haunted | Loot 1 (100.00%) [T_INCURSIONSACK*1, CH*3] |  |


Props per dungeon (`dungeons/<d>/<d>.props.darkest`, `.chance` weights):

* **crypts** hall: discarded_pack 10, sconce 8, crate 10, sack 10, stack_of_books 8, altar_of_light 3, decorative_urn 5, locked_display_cabinet 5, bookshelf 8, alchemy_table 3, confession_booth 3, iron_maiden 5, shamblers_altar 1. room: suit_of_armor 2, holy_fountain 2, sarcophagus 3, locked_sarcophagus 3, eldritch_altar 1. trap spikes, obstacle rubble.
* **weald** hall: discarded_pack 10, sconce 8, crate 10, sack 10, mummified_remains 5, old_tree 8, beast_carcass 8, cosmic_spiderweb 5, lost_luggage 5, shallow_grave 3 (listed twice = 6), troubling_effigy 3, shamblers_altar 1. room: pristine_fountain 2, ancient_coffin 4, travellers_tent 3, eldritch_altar 1. trap poison_cloud, obstacle thorny_thicket.
* **warrens** hall: discarded_pack 10, sconce 8, crate 10, sack 10, stack_of_books 8, dinner_cart 3, knife_rack 5, pile_of_bones 8, occult_scrawlings 5, moonshine_barrel 5, pile_of_scrolls 3, shamblers_altar 1. room: skull_altar 2, sacrificial_stone 3, makeshift_dining_table 3, eldritch_altar 1. trap blade_wheel, obstacle rubble.
* **cove** hall: discarded_pack 10, sconce 8, crate 10, sack 10, barnacle_crusted_chest 8, brackish_tidepool 3, bas_relief 3, giant_fish_carcass 8, eerie_coral 5, shamblers_altar 1. room: fish_idol 3, ships_figurehead 2, giant_oyster 4, eldritch_altar 1. trap lurker, obstacle shipwreck.
* All dungeons: room_treasures unlocked_strongbox 2 / locked_strongbox 3 / heirloom_chest 3; secret_room_treasures secret_stash 1; prison_doors `<d>_door_shovel` 1 / `<d>_door_key` 1. Darkest Dungeon: one row each listing several ids (hall/room: stack_of_books, discarded_pack, sconce, crate, sack; treasures: unlocked_strongbox, locked_strongbox, heirloom_chest; trap spikes; obstacle rubble).

Quest goal curios (not in the 60): `reliquary`, `chirurgeons_satchel`, `foodstuff_crate`, `shipment_crates` (gather, `quest_gather`: pickup gives a quest item), `iron_maiden`, `teleporter` (activate), `corrupted_altar`, `infected_corpse`, `animalistic_shrine`, `protective_ward`, `beacon` (activate with item; interaction type `quest_activate_with`). Interaction table in `props/prop_definitions.json` `interaction_types` (`quest_gather`, `quest_activate`, `quest_activate_with`, `prisoner`, `door_default`, `door_shovel`, `door_key`, `shovel_obstacle`, `torch_obstacle`).


## 6. Provisions, inventory, loot, currencies

### 6.1 Provisioning (`campaign/provision/provision.json`)

Keys: `raid_starting_length_inventory_item_lists` (items you start with), `raid_starting_hero_class_item_lists`, `default_store_inventory_item_lists` (shop stock), `confirm_datas` (minimum food warning). All lists are indexed by quest length (index 0 empty; 1 short, 2 medium, 3 long, 4 exhausting, 5 other).

| Length index | Starting firewood (camps) | Shop stock: provisions | shovel | antivenom | bandage | herbs | skeleton key | holy water | laudanum | torch | min food before warning |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 short | 0 | 18 | 4 | 6 | 6 | 6 | 6 | 6 | 6 | 18 | 8 |
| 2 medium | 1 | 24 | 6 | 9 | 9 | 9 | 9 | 9 | 9 | 24 | 8 |
| 3 long | 2 | 36 | 8 | 12 | 12 | 12 | 12 | 12 | 9 | 36 | 12 |
| 4 exhausting | 4 | 42 | 10 | 15 | 15 | 15 | 15 | 15 | 15 | 42 | 12 |
| 5 (other) | 0 | 36 | 8 | 12 | 12 | 12 | 12 | 12 | 9 | 36 | 12 |

Prices (`inventory/base.supply.inventory.items.darkest`, buy / sell gold; **prices do not depend on quest length**; event discounts -0.5 / -1.0 via town events): provision (food) 75 / 5, torch 75 / 5, shovel 250 / 25, bandage 150 / 15, antivenom 150 / 15, holy_water 150 / 15, skeleton_key 200 / 20, medicinal_herbs 200 / 20, laudanum 100 / 20, firewood 0 (not sold, granted), dog_treats 0 (class starting item). Starting class items: houndmaster dog_treats x2; plague_doctor antivenom x1; grave_robber shovel x1; arbalest bandage x1; crusader holy_water x1; leper medicinal_herbs x1; jester medicinal_herbs x1; antiquarian skeleton_key x1.
Item effects (`effects/base.effects.darkest`): bandage cure_bleed; antivenom cure_poison; medicinal_herbs clear_debuff; holy_water 3-turn resists (blight, bleed, disease, debuff); laudanum clearDotStress; dog_treats 3-turn +15% attack, +50% damage; shovel/key/torch used on curios, doors, obstacles.

### 6.2 Inventory (`inventory/*.darkest`)

| System (`base.inventory.system_configs.darkest`) | max_slots | use_stack_limits |
|---|---|---|
| raid (party backpack) | 16 | true |
| quest_rewards | 16 | false |
| provision (embark shop/party) | 14 | false |
| trinket_store (Nomad) | 16 | true |
| trinket_storage | 9999 | true |
| hero_equipped_trinkets | 2 | true |
| loot | 99 | true |
| goal_starting_items, raid_results, effect_use_items | 99 | true |

Stack limits (`base_stack_limit`): gold 1750 (+750 with the antiquarian class bonus `antiquarian_gold`), heirloom portrait 3, bust 6, crest 12, deed 6, urn 1; provision 12, torch 8, shovel 4, bandage 6, antivenom 6, medicinal_herbs 6, skeleton_key 6, holy_water 6, laudanum 6, dog_treats 2, firewood 1; gems 5 per stack (ancient_idol 1, trapezohedron 1, antiqrelic 5, antiqrelicsmall 20, pewrelic 1); quest items 1.
Gem sell values: ancient_idol 4500, ruby 1250, sapphire 1000, emerald 750, onyx 500, jade 375, citrine 250, trapezohedron 3500, antiqrelicsmall 500, antiqrelic 1250, pewrelic 2500. Heirlooms and gold have no sell value. (Journal pages: loot entries, `icons_equip/journal_page`.)

### 6.3 Loot tables (`loot/loot.json`, `loot/common_overrides.loot.json`, `blueprint_overrides`, `darkness_overrides`)

Format: `{"loot_tables":[{"id","difficulty","dungeon","entries":[{"type": item|table|trinket|journal_page|nothing, "chances": weight, "data": {...}}]}]}`. Tables with the same id are selected by `difficulty` (1/3/5/6) and, for `H`, by `dungeon`. Generated from file:


| id | difficulty | dungeon | entries (weight x result) |
|---|---|---|---|
| B | 0 | - | 3x ->C ; 1x ->G |
| CH | 0 | - | 3x ->C ; 2x ->H |
| CS | 0 | - | 3x ->C ; 2x ->S |
| CT | 0 | - | 9x ->C ; 1x ->T |
| COURTIER | 0 | - | 50x ->C ; 38x ->G ; 48x ->H ; 0x ->J ; 0x ->P ; 28x ->S ; 32x ->T_COURTIER ; 3x journal_page{"min_page_index": 1, "max_page_index": 21} |
| GH | 0 | - | 3x ->G ; 2x ->H |
| GT | 0 | - | 9x ->G ; 1x ->T |
| SC | 0 | - | 2x ->S ; 1x ->C |
| C | 1 | - | 3x gold:-x25 ; 3x gold:-x50 ; 6x gold:-x100 ; 4x gold:-x250 ; 4x gold:-x500 ; 1x gold:-x750 ; 1x gold:-x1000 |
| C | 3 | - | 2x gold:-x25 ; 3x gold:-x50 ; 4x gold:-x100 ; 8x gold:-x250 ; 8x gold:-x500 ; 6x gold:-x750 ; 4x gold:-x1000 |
| C | 5 | - | 1x gold:-x25 ; 1x gold:-x50 ; 2x gold:-x100 ; 6x gold:-x250 ; 6x gold:-x500 ; 8x gold:-x750 ; 6x gold:-x1000 |
| C | 6 | - | 1x gold:-x25 ; 1x gold:-x50 ; 2x gold:-x100 ; 6x gold:-x250 ; 6x gold:-x500 ; 8x gold:-x750 ; 6x gold:-x1000 |
| G | 1 | - | 0x gem:ancient_idolx1 ; 1x gem:rubyx1 ; 1x gem:sapphirex1 ; 4x gem:emeraldx1 ; 4x gem:onyxx1 ; 10x gem:jadex1 ; 10x gem:citrinex1 |
| G | 3 | - | 1x gem:ancient_idolx1 ; 3x gem:rubyx1 ; 3x gem:sapphirex1 ; 10x gem:emeraldx1 ; 10x gem:onyxx1 ; 8x gem:jadex1 ; 8x gem:citrinex1 |
| G | 5 | - | 1x gem:ancient_idolx1 ; 6x gem:rubyx1 ; 6x gem:sapphirex1 ; 8x gem:emeraldx1 ; 8x gem:onyxx1 ; 5x gem:jadex1 ; 5x gem:citrinex1 |
| G | 6 | - | 2x gem:ancient_idolx1 ; 10x gem:rubyx1 ; 10x gem:sapphirex1 ; 10x gem:emeraldx1 ; 10x gem:onyxx1 ; 5x gem:jadex1 ; 5x gem:citrinex1 |
| H | 1 | crypts | 3x heirloom:bustx2 ; 1x heirloom:portraitx1 ; 1x heirloom:deedx2 ; 3x heirloom:crestx4 |
| H | 1 | weald | 1x heirloom:bustx2 ; 1x heirloom:portraitx1 ; 3x heirloom:deedx2 ; 3x heirloom:crestx4 |
| H | 1 | warrens | 1x heirloom:bustx2 ; 3x heirloom:portraitx1 ; 1x heirloom:deedx2 ; 3x heirloom:crestx4 |
| H | 1 | cove | 1x heirloom:bustx2 ; 1x heirloom:portraitx1 ; 1x heirloom:deedx2 ; 3x heirloom:crestx4 |
| H | 3 | crypts | 3x heirloom:bustx3 ; 1x heirloom:portraitx2 ; 1x heirloom:deedx3 ; 3x heirloom:crestx6 |
| H | 3 | weald | 1x heirloom:bustx3 ; 1x heirloom:portraitx2 ; 3x heirloom:deedx3 ; 3x heirloom:crestx6 |
| H | 3 | warrens | 1x heirloom:bustx3 ; 3x heirloom:portraitx2 ; 1x heirloom:deedx3 ; 3x heirloom:crestx6 |
| H | 3 | cove | 1x heirloom:bustx3 ; 1x heirloom:portraitx2 ; 1x heirloom:deedx3 ; 3x heirloom:crestx6 |
| H | 5 | crypts | 3x heirloom:bustx4 ; 1x heirloom:portraitx2 ; 1x heirloom:deedx4 ; 3x heirloom:crestx8 |
| H | 5 | weald | 1x heirloom:bustx4 ; 1x heirloom:portraitx2 ; 3x heirloom:deedx4 ; 3x heirloom:crestx8 |
| H | 5 | warrens | 1x heirloom:bustx4 ; 3x heirloom:portraitx2 ; 1x heirloom:deedx4 ; 3x heirloom:crestx8 |
| H | 5 | cove | 1x heirloom:bustx4 ; 1x heirloom:portraitx2 ; 1x heirloom:deedx4 ; 3x heirloom:crestx8 |
| H | 6 | - | 1x heirloom:bustx4 ; 1x heirloom:portraitx2 ; 2x heirloom:deedx4 ; 2x heirloom:crestx8 |
| NONE | 0 | - | 1x nothing ; 0x heirloom:bustx1 |
| Nothing | 0 | - | 1x nothing |
| P | 0 | - | 3x provision:-x2 ; 3x provision:-x4 ; 2x provision:-x6 ; 1x provision:-x8 |
| Tbase | 0 | - | 3x nothing ; 2x ->T |
| T | 1 | - | 45x very_common ; 40x common ; 10x uncommon ; 5x rare ; 0x very_rare ; 0x ancestral ; 0x ancestral_shambler |
| T | 3 | - | 45x very_common ; 40x common ; 35x uncommon ; 8x rare ; 2x very_rare ; 0x ancestral ; 0x ancestral_shambler |
| T | 5 | - | 45x very_common ; 40x common ; 35x uncommon ; 12x rare ; 5x very_rare ; 3x ancestral ; 0x ancestral_shambler |
| T | 6 | - | 45x very_common ; 40x common ; 35x uncommon ; 12x rare ; 5x very_rare ; 3x ancestral ; 0x ancestral_shambler |
| T_COLLECTOR | 0 | - | 0x very_common ; 0x common ; 0x uncommon ; 0x rare ; 0x very_rare ; 1x collector |
| T_ANTIQ_CAMP | 0 | - | 10x very_common ; 7x common ; 5x uncommon ; 2x rare ; 1x very_rare ; 0x ancestral ; 0x ancestral_shambler |
| T_MADMAN | 0 | - | 1x very_common ; 1x common ; 1x uncommon ; 0x rare ; 0x very_rare ; 1x madman |
| T_INCURSIONSACK | 0 | - | 1x very_rare ; 1x ancestral |
| T_COURTIER | 0 | - | 14x very_common ; 30x common ; 35x uncommon ; 12x rare ; 5x very_rare ; 3x ancestral ; 40x courtier |
| TORCHONLY | 0 | - | 1x supply:torchx1 |
| SHOVELONLY | 0 | - | 1x supply:shovelx1 |
| KEYONLY | 0 | - | 1x supply:skeleton_keyx1 |
| HOLYONLY | 0 | - | 1x supply:holy_waterx1 |
| SHAMBLER | 0 | - | 1x ancestral_shambler |
| PEW | 0 | - | 1x gem:pewrelicx1 |
| COLLECTOR | 0 | - | 3x gem:trapezohedronx1 ; 1x ->T_COLLECTOR |
| MADMAN | 0 | - | 5x ->S ; 1x ->T_MADMAN |
| T_ANCESTOR | 0 | - | 1x ancestral |
| ANTIQ | 0 | - | 5x gem:antiqrelicsmallx1 ; 1x gem:antiqrelicx1 |
| JOURNALONLY | 0 | - | 1x journal_page{"min_page_index": 1, "max_page_index": 21} |
| NEST_A | 0 | - | 1x gem:trapezohedronx1 |
| NEST_B | 0 | - | 2x gem:trapezohedronx1 ; 4x gem:trapezohedronx2 ; 4x gem:trapezohedronx3 |
| NEST_C | 0 | - | 4x gem:trapezohedronx3 ; 4x gem:trapezohedronx4 ; 2x gem:trapezohedronx5 |
| THANKS | 0 | - | 1x journal_page{"specific_page_index": 0} |
| test | 0 | - | 1x nothing |
| A (override file) | 0 | - | 50x ->C ; 38x ->G ; 48x ->H ; 0x ->J ; 0x ->P ; 28x ->S ; 8x ->T ; 2x journal_page{"min_page_index": 1, "max_page_index": 21} |
| S (override file) | 0 | - | 0x supply:firewoodx1 ; 3x provision:-x2 ; 1x supply:shovelx1 ; 1x supply:antivenomx1 ; 1x supply:bandagex1 ; 1x supply:medicinal_herbsx1 ; 1x supply:skeleton_keyx1 ; 1x supply:holy_waterx1 ; 2x supply:torchx1 |


Notes: table `A` and `S` exist only in `common_overrides.loot.json`. Gold per draw (table `C`) ranges 25 to 1000 (weights shift upward with difficulty). Quest gold/heirloom/trinket rewards are NOT drawn from these tables (section 3.2); these are the raid loot (curios, battles). Heirloom `H` amounts per draw: d1 bust 2 / portrait 1 / deed 2 / crest 4; d3 3 / 2 / 3 / 6; d5 4 / 2 / 4 / 8; d6 4 / 2 / 4 / 8 (weights vary by dungeon: crypts favours bust 3:1, weald favours deed, warrens favours portrait, cove flat 1/1/1 + crest 3).


## 7. Town events

* Settings (`campaign/town_events/town_events.settings.json`): `normal` `event_chance_per_town_visits` = [0.33, 0.67, 0.75, 1.0] (chance indexed by number of town visits since the last event, capped; `plentiful` [0.67, 0.67, 0.80, 1.0]; `off` [0.0]).
* Files: `base.town_events.events.json` (47 events), `mode.town_events.events.json` (`stage_coach_bonus_recruits`, +6 recruits, week >= 6), `arena.town_events.events.json`, `town_events.quest_type_event_guarantees.json` (see below). DLC adds `rd.town_events...`, `shieldbreaker.town_events...`.
* Event schema: `id`, `base_chance` (weight), `per_not_rolled_additional_chance` (pity), `cooldown` (weeks), `is_unique`, `requirements{minimum_week, dead_heroes, hero_level_counts[{level,count}], upgrades_purchased[{tree_id,requirement_code}], trinket_storage_count}`, `town_ambience_paramater_ids`, `tone` (good/neutral/bad), `sprite` + `sprite_attachment` (Spine `fx/town_event_*` attached to a building), `data[{type, string_data, number_data}]`.
* Event data types: `embark_party_buff` (buff ids), `idle_resolve_level` (class, +1 resolve level to idle heroes of that class), `bonus_recruit` (class, count 2), `in_activity_buff`, `activity_lock`, `activity_cost_change` (-0.5), `free_activity`, `provision_item_type_cost_change` (-0.5 / -1.0), `provision_item_type_amount_change` (-0.5), `upgrade_tag_discount` (trinket 0.5), `upgrade_tag_free` (armour / weapon / building, 1), `dead_recruit` (3), `remove_quest_hero_level_restriction`, `idle_buff` (`town_event_idle_stress_heal`), `plot_quest`, `trinket_retention_add_from_storage` (8), `roster_stress_heal` (arena), `stage_coach_bonus_recruits`.
* Guaranteed pairing (`quest_type_event_guarantees`): finishing a gather quest in cove -> `upgrade_tag_free_weapon`, crypts -> `free_abbey`, warrens -> `provision_supply_free`, weald -> `free_sanitarium`; inventory_activate in cove / crypts / warrens / weald -> the matching `embark_party_buff_<dungeon>_buff`.
* `embark_party_buff_<dungeon>_buff` events have `base_chance` 0 (they only occur through the guarantee pairing above); events with `minimum_week` 6 / 12 / 15 / 40 / 42 gate by campaign week.
* Art: `campaign/town/town_event/town_event.image_<event id>.png` per event plus `town_event.{background,character,confirm_button,icon}.png`; hamlet background swaps (`town_render_data.json`) for `in_activity_buff_stress_heal_buff/debuff`.

All 47 events (generated from file):


| id | weight | +pity | cooldown | tone | requirements | effect data | sprite |
|---|---|---|---|---|---|---|---|
| embark_party_buff_crypts_buff | 0.0 | 0.0 | 1 | good | week>=6 | embark_party_buff(town_event_crypts_resolve_xp); embark_party_buff(town_event_crypts_damage_low); embark_party_buff(town_event_crypts_damage_high) | - |
| embark_party_buff_weald_buff | 0.0 | 0.0 | 1 | good | week>=6 | embark_party_buff(town_event_weald_resolve_xp); embark_party_buff(town_event_weald_damage_low); embark_party_buff(town_event_weald_damage_high) | - |
| embark_party_buff_warrens_buff | 0.0 | 0.0 | 1 | good | week>=6 | embark_party_buff(town_event_warrens_resolve_xp); embark_party_buff(town_event_warrens_damage_low); embark_party_buff(town_event_warrens_damage_high) | - |
| embark_party_buff_cove_buff | 0.0 | 0.0 | 1 | good | week>=6 | embark_party_buff(town_event_cove_resolve_xp); embark_party_buff(town_event_cove_damage_low); embark_party_buff(town_event_cove_damage_high) | - |
| idle_resolve_level_plague_doctor | 2.0 | 0.0 | 2 | neutral | week>=6 | idle_resolve_level(plague_doctor,1.0) | town_event_the_plague@statue |
| idle_resolve_level_jester | 2.0 | 0.0 | 2 | neutral | week>=6 | idle_resolve_level(jester,1.0) | town_event_busking@statue |
| idle_resolve_level_highwayman | 2.0 | 0.0 | 2 | neutral | week>=6 | idle_resolve_level(highwayman,1.0) | town_event_laying_low@statue |
| idle_resolve_level_grave_robber | 2.0 | 0.0 | 2 | neutral | week>=6 | idle_resolve_level(grave_robber,1.0) | town_event_eat_the_rich@statue |
| idle_resolve_level_man_at_arms | 2.0 | 0.0 | 2 | neutral | week>=6 | idle_resolve_level(man_at_arms,1.0) | town_event_militia_training@statue |
| idle_resolve_level_arbalest | 2.0 | 0.0 | 2 | neutral | week>=6 | idle_resolve_level(arbalest,1.0); idle_resolve_level(musketeer,1.0) | town_event_archery_tourney@statue |
| idle_resolve_level_houndmaster | 2.0 | 0.0 | 2 | neutral | week>=6 | idle_resolve_level(houndmaster,1.0) | town_event_rats_among_us@statue |
| idle_resolve_level_abomination | 2.0 | 0.0 | 2 | neutral | week>=6 | idle_resolve_level(abomination,1.0) | town_event_gibbous_moon@statue |
| bonus_recruit_crusader | 2.0 | 0.0 | 2 | neutral | week>=6 | bonus_recruit(crusader,2.0) | - |
| bonus_recruit_bounty_hunter | 2.0 | 0.0 | 2 | neutral | week>=6 | bonus_recruit(bounty_hunter,2.0) | - |
| bonus_recruit_vestal | 3.0 | 0.0 | 2 | neutral | week>=6 | bonus_recruit(vestal,2.0) | - |
| bonus_recruit_hellion | 2.0 | 0.0 | 2 | neutral | week>=6 | bonus_recruit(hellion,2.0) | - |
| bonus_recruit_occultist | 3.0 | 0.0 | 2 | neutral | week>=6 | bonus_recruit(occultist,2.0) | - |
| bonus_recruit_leper | 2.0 | 0.0 | 2 | neutral | week>=6 | bonus_recruit(leper,2.0) | - |
| bonus_recruit_antiquarian | 2.0 | 0.0 | 2 | neutral | week>=6 | bonus_recruit(antiquarian,2.0) | - |
| in_activity_buff_stress_heal_buff | 5.0 | 0.0 | 2 | good | week>=6 | in_activity_buff(town_event_in_activity_meditation_stress_heal); in_activity_buff(town_event_in_activity_prayer_stress_heal); in_activity_buff(town_event_in_activity_flagellation_stress_heal); in_activity_buff(town_event_in_activity_bar_stress_heal); in_activity_buff(town_event_in_activity_gambling_stress_heal); in_activity_buff(town_event_in_activity_brothel_stress_heal) | town_event_ray_of_sunlight@statue |
| in_activity_buff_stress_heal_debuff | 3.0 | 0.0 | 2 | bad | week>=12 | in_activity_buff(town_event_in_activity_meditation_stress_damage); in_activity_buff(town_event_in_activity_prayer_stress_damage); in_activity_buff(town_event_in_activity_flagellation_stress_damage); in_activity_buff(town_event_in_activity_bar_stress_damage); in_activity_buff(town_event_in_activity_gambling_stress_damage); in_activity_buff(town_event_in_activity_brothel_stress_damage) | - |
| lock_bar_discount_tavern | 3.0 | 0.0 | 2 | neutral | week>=12 | activity_lock(bar); activity_cost_change(gambling,-0.5); activity_cost_change(brothel,-0.5) | town_event_empty_kegs@tavern |
| lock_gambling_discount_tavern | 3.0 | 0.0 | 2 | neutral | week>=12 | activity_lock(gambling); activity_cost_change(bar,-0.5); activity_cost_change(brothel,-0.5) | town_event_robbery@tavern |
| lock_brothel_discount_tavern | 3.0 | 0.0 | 2 | neutral | week>=12 | activity_lock(brothel); activity_cost_change(gambling,-0.5); activity_cost_change(bar,-0.5) | town_event_laundry_day@tavern |
| lock_meditation_discount_abbey | 3.0 | 0.0 | 2 | neutral | week>=12 | activity_lock(meditation); activity_cost_change(prayer,-0.5); activity_cost_change(flagellation,-0.5) | town_event_noisy_repairs@graveyard |
| lock_prayer_discount_abbey | 3.0 | 0.0 | 2 | neutral | week>=12 | activity_lock(prayer); activity_cost_change(meditation,-0.5); activity_cost_change(flagellation,-0.5) | town_event_absent_abbot@graveyard |
| lock_flagellation_discount_abbey | 3.0 | 0.0 | 2 | neutral | week>=12 | activity_lock(flagellation); activity_cost_change(prayer,-0.5); activity_cost_change(meditation,-0.5) | town_event_cell_cleaning@graveyard |
| provision_supply_discount | 2.0 | 0.0 | 1 | neutral | week>=6 | provision_item_type_cost_change(supply,-0.5); provision_item_type_cost_change(provision,-0.5) | town_event_new_shipment@stage_coach |
| provision_supply_free | 2.0 | 0.0 | 1 | good | week>=6 | provision_item_type_cost_change(supply,-1.0); provision_item_type_cost_change(provision,-1.0) | town_event_bumper_crop@statue |
| provision_reduced_amounts | 2.0 | 0.0 | 2 | bad | week>=12 | provision_item_type_amount_change(supply,-0.5); provision_item_type_amount_change(provision,-0.5) | - |
| nomad_wagon_trinket_discount | 3.0 | 0.0 | 2 | neutral | week>=6 | upgrade_tag_discount(trinket,0.5) | town_event_nomad_new_year@nomad_wagon |
| free_abbey | 2.0 | 0.0 | 2 | good | week>=6 | free_activity(meditation); free_activity(prayer); free_activity(flagellation) | town_event_all_saints_day@graveyard |
| free_tavern | 2.0 | 0.0 | 2 | good | week>=6 | free_activity(bar); free_activity(gambling); free_activity(brothel) | town_event_mardi_gras@tavern |
| free_sanitarium | 2.0 | 0.0 | 2 | good | week>=6 | free_activity(treatment); free_activity(disease_treatment) | town_event_caregivers_convention@sanitarium |
| free_all_activities | 5.0 | 0.0 | 2 | good | week>=6 | free_activity(meditation); free_activity(prayer); free_activity(flagellation); free_activity(bar); free_activity(gambling); free_activity(brothel); free_activity(treatment); free_activity(disease_treatment) | town_event_town_fair@statue |
| dead_recruit | 3.0 | 1.0 | 3 | good | week>=15 dead>=3 | dead_recruit(,3.0) | town_event_day_of_the_dead@graveyard |
| remove_quest_hero_level_restriction | 6.0 | 0.0 | 2 | good | week>=15 | remove_quest_hero_level_restriction() | - |
| upgrade_tag_free_armour | 4.0 | 0.0 | 2 | good | week>=6 upgrade blacksmith.armour.a | upgrade_tag_free(armour,1) | town_event_tinkers_day@blacksmith |
| upgrade_tag_free_weapon | 4.0 | 0.0 | 2 | good | week>=6 upgrade blacksmith.weapon.a | upgrade_tag_free(weapon,1) | town_event_tinkers_day@blacksmith |
| upgrade_tag_free_building_upgrade | 5.0 | 0.0 | 2 | good | week>=6 | upgrade_tag_free(building,1) | town_event_labour_force@statue |
| idle_stress_heal_buff | 8.0 | 0.0 | 2 | good | week>=6 | idle_buff(town_event_idle_stress_heal) | - |
| free_disease | 8.0 | 0.0 | 2 | good | week>=12 | free_activity(disease_treatment) | town_event_medical_breakthrough@sanitarium |
| embark_party_buff_affliction_chance | 6.0 | 0.0 | 2 | bad | week>=12 | embark_party_buff(town_event_affliction_chance) | - |
| embark_party_buff_virtue_chance | 6.0 | 0.0 | 2 | good | week>=6 | embark_party_buff(town_event_virtue_chance) | - |
| plot_quest_town_invasion_0 | 12.0 | 3.0 | 4 | bad | week>=40 levels [{"level": 5, "count": 4}] | plot_quest(plot_town_invasion_0) | - |
| trinket_retention_add_from_storage | 5.0 | 1.0 | 0 | bad | week>=42 trinkets>=8 unique | trinket_retention_add_from_storage(,8.0) | - |
| plot_quest_crow_trinket | 6.0 | 2.0 | 3 | good | week>=15 | plot_quest(plot_crow_trinket,1.0) | - |


Text ids: `town_event_title_<id>`, `town_event_description_<id>`, `town_event_info_<id>`, voice line `str_vo_town_event_<id>_0`; tooltip `town_event_tooltip_*`.


## 8. Story assets (Ancestor narration, bosses, memoirs, journal, video)

### 8.1 Narration (audio config `audio/narration.json`, text in `localization/miscellaneous.string_table.xml`)

* `audio/narration.json` (287 KB): `{"filters":["plot_darkest_dungeon_4"], "entries":[...]}`; 36 trigger entries, **473 audio events**. Entry = `{id, tone, chance, audio_events[]}`; event = `{audio_event (FMOD path), chance, priority, queue_only_on_empty, queue_while_audio_playing, max_raid_occurrences, max_town_visit_occurrences, max_campaign_occurrences, filter, check_all_tags, tags[]}`. `tags` select a line by plot quest id / dungeon / monster / hero class / curio (e.g. `plot_kill_necromancer_1`, `crypts`, `explore`, `travellers_tent_tutorial`). `audio/shared.narration.json` (23 KB) = Crimson Court DLC lines (`/vo/crimson_court/shared/...`).
* FMOD event path -> subtitle string id: `/vo/<group>/<name>` -> `str_vo_<group>_<name>_<part>` (part 0.. = sub-lines shown while the clip plays). Examples: `/vo/load/crypts_01` -> `str_vo_load_crypts_01_0`, `_1`, ...; `/vo/town/upgrade_stagecoach_01` -> `str_vo_town_upgrade_stagecoach_01_0`; `/vo/tutorial/first_town` -> `str_vo_tutorial_first_town_0` ("Welcome home, such as it is..."). 575 `str_vo_*` entries in `miscellaneous.string_table.xml` (+ 37 `str_vo_crimson_*`, `str_vo_house_of_ruin_*` 29, `str_vo_old_road_*` 16, `str_vo_epilog_*` 18 video subtitles).
* Event groups (`/vo/<group>/`): good 117, town 116, bad 91, town_event 47, neutral 37, load 24, tutorial 16, pit 3, others 22.

Narration trigger table (id, tone, chance, number of audio events, sample FMOD path):


| trigger id | tone | chance | #events | first event path |
|---|---|---|---|---|
| loading_screen_start | neutral | 1 | 27 | /vo/load/crypts_01 |
| quest_start | neutral | 1 | 26 | /vo/neutral/quest_start_01 |
| quest_end_completed | good | 1 | 28 | /vo/good/crypts_success_explore_01 |
| quest_end_not_completed | bad | 1 | 11 | /vo/bad/general_quest_fail_01 |
| combat_start | bad | 1 | 11 | /vo/tutorial/first_battle |
| kill_monster | good | 1 | 43 | /vo/good/boss_kill_01 |
| kill_hero | bad | 1 | 5 | /vo/bad/death_01 |
| crit_monster | good | 0.7 | 9 | /vo/good/general_crit_monster_01 |
| crit_hero | bad | 0.75 | 10 | /vo/bad/general_crit_hero_01 |
| deaths_door | bad | 0.9 | 5 | /vo/bad/deaths_door_01 |
| victory | good | 1 | 9 | /vo/good/victory_first_01 |
| battle_retreat | good | 0.85 | 3 | /vo/good/retreat_success_01 |
| battle_retreat_fail | good | 0.3 | 3 | /vo/bad/retreat_fail_01 |
| enter_hallway | neutral | 1 | 3 | /vo/tutorial/first_dungeon |
| torchlight_out | bad | 1 | 5 | /vo/bad/torchlight_01 |
| torchlight_full | bad | 1 | 5 | /vo/good/torchlight_01 |
| half_health_half_stress | bad | 1 | 1 | /vo/bad/half_health_half_stress_01 |
| afflicted | bad | 1 | 16 | /vo/bad/afflicted_01 |
| virtue | good | 1 | 11 | /vo/good/affliction_pass_01 |
| hunger | bad | 0 | 1 |  |
| hunger_starve | bad | 1 | 6 | /vo/bad/starve_01 |
| obstacle | bad | 1 | 8 | /vo/bad/obstacle_crypts |
| obstacle_clear_no_item | bad | 1 | 1 | /vo/bad/obstacle_01 |
| curio | neutral | 1 | 1 | /vo/tutorial/first_curio |
| trap | bad | 1 | 9 | /vo/bad/trap_01 |
| loot | good | 0.4 | 7 | /vo/good/loot_first_01 |
| camp | good | 1 | 5 | /vo/good/camp_01 |
| recruit_hero | neutral | 1 | 36 | /vo/town/recruit_crusader |
| dismiss_hero | bad | 1.0 | 8 | /vo/town/dismiss_general_01 |
| upgrade_building | neutral | 1 | 64 | /vo/town/upgrade_stagecoach_01 |
| town_visit_start | bad | 1 | 78 | /vo/tutorial/first_town |
| enter_quest_select | bad | 1 | 1 | /vo/tutorial/first_quest_select |
| enter_provision_select | bad | 1 | 1 | /vo/tutorial/first_provision |
| enter_building | bad | 1 | 10 | /vo/tutorial/first_stagecoach |
| ancestor_talk | neutral | 1 | 3 | /vo/neutral/darkest_04_obstacle_01 |
| change_monster_class | bad | 1 | 3 | /vo/bad/darkest_boss_02 |


Trigger meaning (id names): `loading_screen_start` = boss/dungeon intro on the loading screen (24 `/vo/load/<dungeon>_01..06` clips tagged by plot quest, + `/vo/neutral/darkest_0N_loading` for DD), `quest_start`, `quest_end_completed` / `quest_end_not_completed`, `combat_start`, `kill_monster`, `kill_hero`, `crit_*`, `deaths_door`, `victory`, `battle_retreat*`, `enter_hallway`, `torchlight_out` / `torchlight_full`, `afflicted` / `virtue`, `hunger` / `hunger_starve`, `obstacle` (per dungeon), `curio`, `trap`, `loot`, `camp`, `recruit_hero` (36, per class), `dismiss_hero`, `upgrade_building` (64), `town_visit_start` (78), `enter_quest_select`, `enter_provision_select`, `enter_building`, `ancestor_talk` (Ancestor obstacle in the Darkest Dungeon), `change_monster_class`. Limits `max_raid_occurrences`, `max_town_visit_occurrences`, `max_campaign_occurrences` give once-per-X behaviour.

### 8.2 Audio banks (`<DD1>\audio\`) - FMOD Studio banks (RIFF `FEV `, `FMT` version 103)

`audio/master_banks/master_bank.bank` + `master_bank.strings.bank` (strings use prefix-compressed path fragments, not plain `event:/` paths), `audio/secondary_banks/*.bank` (load orders in `audio/base.*.load_order.json`). Event counts below are `EVNT` chunk counts; content is judged from event/sample names **(content classification inferred)**.

| Bank | Size | Contents |
|---|---|---|
| `voiceover.bank` | 174 MB | **Ancestor narration** (396 events, `/vo/...`); loaded at app start (`base.app.load_order`) |
| `voiceover_shared_courtyard.bank` | 11 MB | Crimson Court shared narration `vo_narr_*` (36 events) - DLC content in the base folder, `base.narration.load_order` |
| `music.bank` | 154 MB | music (28 events + FSB5 streams) |
| `ambience.bank` | 157 MB | ambience (34 events, e.g. `amb_dun_town_os_base_*`, dungeon beds); hamlet ambience per building exists as `ambience/town/<building>` event fragments in the strings bank (`abbey, blacksmith, camping_trainer, general, graveyard, guild, sanitarium, statue, tavern, pit_exterior`) |
| `town.bank` | 3 MB | hamlet SFX (`town_enter_*`, `town_<building>_<activity>`, `town_gen_building_upgrade*`, `townevent_good/neutral/bad/banditincursion`) |
| `ui_town.bank`, `ui_shared.bank`, `ui_dungeon.bank` | 1.7 / 1.3 / 0.4 MB | UI sounds |
| `title_screen.bank` | 1 MB | title screen |
| `general.bank` | 12 MB | raid/combat/camp SFX (`gen_map_*`, `gen_combat_*`, `camp_skill_*`, door open/close per dungeon) |
| `raid_screen.bank` | 4 MB | raid screen |
| `en_<dungeon>.bank` (cove 12 MB, crypts 7, darkestdungeon 20, town 2.5, warrens 5, weald 22), `en_shared` 15 MB | monster/"enemy" SFX per dungeon (`char_en_*`) |
| `props_<dungeon>.bank`, `props_shared.bank` | 0.8-5 MB | curios/doors/traps SFX |
| `hero_<class>.bank` x15 | 1-2 MB each | hero SFX/barks |

### 8.3 Boss intro quests, memoirs, journal

* Boss intro texts: quest names/descriptions `town_quest_name_plot_<id>`, `town_quest_description_plot_<id>`, `town_quest_progress_plot_<id>` (+ per-tier `_0/_1/_2` for retention quests); Caretaker goal text `str_caretaker_goal_plot_<id>`; loading screen tip `str_plot_<id>_tip` ("Ancestor's Memoirs - The Cannon (1 / 3)"); Memoirs audio title `str_plot_<id>_audio_line` (e.g. "Awe and Ire"); `str_media_plot_<id>`; generic quests `town_quest_name_<type>+<length>+<dungeon>+<goal>` and `town_quest_description_<type>+<length>+<dungeon>+<goal>` (e.g. `town_quest_name_cleanse+1+crypts+battle_all_rooms` = "Skirmish").
* Loading screens: `loading_screen/loading_screen.plot_<quest id>.png` for every plot quest, `loading_screen.<dungeon>_0.png`, `town_0`, `town_visit`, `old_road` (all 1920x1080 PNG), layout `loading_screen.layout.darkest`; narration clip chosen by tag `plot_*` when `loading_screen_start` fires.
* Ancestor's Memoirs building (`statue`): `statue_media_info.json` `categories` (prologue videos, `dd_entries` regex `plot_darkest_dungeon_.*`, `boss_entries` `plot_kill_.*`, `epilog`, `backerjournal`) and `videos`: `house_of_ruin`, `old_road`, `epilog` (epilog accessible only after `plot_darkest_dungeon_4`). Portraits `buildings/statue/portrait_<boss>.png`, backdrops, title bars.
* Journal pages: `localization/journal.string_table.xml` `journal_page_title_<N>` / `journal_page_text_<N>`, 22 pages (N 0..21; 0 = the developers' thank-you page; 1-6 "Journal of Darius, Highwayman (n/6)"; later pages not inspected); found as loot (`journal_page` loot entries, `min_page_index` 1, `max_page_index` 21; table `THANKS` gives page 0) and curios (`JOURNALONLY`). UI: `shared/journal_popup/`.
* Ancestor on the map: obstacle `ancestor` (`ancestor_talk`), `scrolls/ancestor_talk.png`, string `str_ancestor_description` "A shimmering likeness of your Ancestor! He wishes to speak...".

### 8.4 Video (`<DD1>\video\`)

`house_of_ruin.ogv` (79 MB, 1920x1080, 29.97 fps), `old_road.ogv` (46 MB, 1920x1080, 29.97), `epilog.ogv` (63 MB, 1920x1080, 23.976); Ogg Theora (3.2.1) + Vorbis (header probe). Subtitle timing `<name>.sub`: one line per subtitle `start_ms,end_ms,` (e.g. `1155,2361,`), line n pairs with string `str_vo_<name>_<n>`. Titles: `str_media_house_of_ruin` "House of Ruin", `str_media_old_road` "Old Road", `str_media_epilog` "Victory".


## 9. Art formats for runtime loading

Summary: everything in the hamlet UI, dungeon walls, map icons, scrolls, panels, loading screens is plain PNG. Buildings, curios, traps, props, fx and all heroes/monsters are Spine 2.1.27 binary skeletons whose atlases are plain PNG regions (static frames can be cut out without a Spine runtime). Fonts are BMFont text + 1024x1024 uncompressed 32-bit TGA pages. Colour type: 8-bit RGB (type 2), RGBA (6) or indexed/greyscale+alpha in a few small icons; no 16-bit or interlaced files were seen in the sampled folders.

### 9.1 Hamlet scene (`<DD1>\campaign\town\`)

| File | Format / size | Role |
|---|---|---|
| `town_bg.png` (+ `town_bg_post_dd_1/2/3.png`, `town_bg_activity_stress_heal_buff/debuff.png`) | PNG 1920x1080 RGB | blurred full-screen backdrop (sky/mist; checked visually), swapped by `town_render_data.json` display states |
| `town_backdrop.png` | PNG 1626x612 RGBA | far backdrop layer |
| `town_bridge.png` | PNG 568x284 RGBA | foreground bridge |
| `town_ground.png` | PNG 2000x600 RGBA | ground strip (the Spine version is `fx/town_ground`, atlas 3844x1672) |
| `town_left_cliff.png` 332x654, `town_right_tree.png` 564x541 | PNG RGBA | framing props |
| `sky/sky01.png` 1920x179, `sky02.png` 1229x106, `ruins.png` 1418x438 | PNG RGBA | scrolling sky layers (`sky_anim .speedX1 0.1 .speedX2 0.3`) |
| `town.layout.darkest` | text | layout: `town_screen_layout` (camera_position 960 322 -1251, ground_start/end, left_cliff_position, bridge_position, right_tree_position, backdrop_position, panel sizes 1550x1080 / 1395x1080), one `<building>_layout:` block per building with `.pos3d x y z`, `.pos x y z`, `.scale`, `.text_offset`, `.level_thresholds`, `.bbox_offset`, `.bbox_size` (click box in px), `.alert_offset`; `time_of_day .time_per_grade 65.0 .transition_time 2.0`; `sky_layout`, `midground_layout` |
| `town.anim.darkest` | text | timings: screen_fade 0.5 s easeInQuad, building_name_fade 0.3 s, sky speeds, building_zoom 0.7 s easeOutSine, z 700 |
| `town_render_data.json` | JSON | `display_states[]` {name, `active_on_plot_quest_return`, `active_on_town_event`, `background_texture`, `time_of_day_data[{tint}]`}; tints are LUT PNGs `colours/town_screen_colour_{morning,afternoon,evening,night,dd1,inblg,...}.png` |
| `buildings/building.layout.darkest` | text | shared building panel layout (name_pos 104 126, body_base_pos 596 102, upgrade trees offsets, activity list, slot layout, animation `transition_time 0.3`, `base_scale 0.4`) |
| `buildings/<id>/<id>.layout.darkest` | text | per-building (e.g. stage_coach `hero_recruit_store base_size 780 780`) |

**The building exteriors are NOT in `campaign/town/`.** They are Spine skeletons in `<DD1>\fx\town_<building>_level01 / level02 / level03 / locked` (e.g. `town_abbey_level01`, `town_blacksmith_level0N`, `town_camping_trainer_*`, `town_guild_*`, `town_nomad_wagon_*`, `town_sanitarium_*`, `town_stage_coach_level01-03` (no locked), `town_tavern_*`, `town_statue_level01-03`, `town_graveyard`, `town_circus_*`, `town_ground`), each folder = `<name>.sprite.skel` + `.sprite.atlas` + `.sprite.png`. Example files:

* `fx/town_abbey_level01/`: `town_abbey_level01.sprite.skel` (1,303 B), `.atlas` (384 B), `.png` 1975x804 RGBA (570 KB). Atlas regions: `active` 779x901 (rotated), `idle` 800x987 (rotated), `light` 79x134. Skeleton animations include `idle`, `active`; slots `light_middle`.
* `fx/town_stage_coach_level01/`: `.skel` 279 B, `.atlas` 294 B, `.png` 1018x362 RGBA. Regions: `active` 510x358, `idle` 502x357 (not rotated).
* Tavern: regions `active` 768x753, `idle` 770x806, plus `light_door`, `light_first_floor`, `light_second_floor` overlays (window glow).

Spine facts: skeleton hash header then version string `2.1.27` in 2002 `.skel` files in the install (34 more are `2.1.08`, e.g. `fx/estate_exclamation`). Atlas = Spine/libGDX text format (`size`, `format: RGBA8888`, `filter: Linear,Linear`, `repeat: none`; regions with `rotate`, `xy`, `size`, `orig`, `offset`, `index`). Loadable options: (a) slice `idle` / `active` regions straight from the atlas PNG (undo `rotate: true` = 90 degrees; no Spine runtime), or (b) use the Spine 2.1 C# runtime if skeletal animation is wanted. A runtime newer than 3.x cannot read 2.1 binary skeletons.

### 9.2 Building interior panels and characters

Per building folder, e.g. `buildings/abbey/` (all PNG RGBA unless noted): `abbey.character.png` 811x757 (character NPC art in the panel), `abbey.character_background.png` 1395x776, `abbey.dd.character.png` 811x757 (post-Darkest-Dungeon variant), `abbey.icon.png` 113x113, `abbey.icon_roster.png` 64x64, `abbey.town_button.png` 56x56, per activity `abbey.<activity>.icon.png` 72x72 (RGB), `.hero_slot_overlay.png` 148x257, `.free_event.png` / `.locked_event.png` 171x179, `abbey.locked_hero_slot_overlay.png` 171x179; `buildings/stage_coach/`: `stage_coach.character.png` 811x757, `.character_background.png` 1395x776, `.hero_background.png` 600x101, `.icon.png`, `.numrecruits/.rostersize/.upgraded_recruits.icon.png` 72x72 (upgrade tree icons), `.town_button.png`. Generic upgrade UI: `buildings/blgupgradebg.png` 662x764, `blg_name_background.png` 208x224, `blg_townupgrade_costframe.png`. Currency icons `shared/estate/currency.<type>.icon.png`. Fonts: section 9.5.

### 9.3 Dungeon art (`<DD1>\dungeons\<d>\`, same sizes in crypts, weald, warrens, cove, town, darkestdungeon quest folders)

| File pattern | Size | Role |
|---|---|---|
| `<d>.corridor_wall.NN.png` | 720x720 | hallway wall tile; counts: crypts 7 (`.00`-`.06`), weald 10, warrens 6, cove 7, town 5, DD quest folders 5-6. Tiles are placed edge to edge, one per hallway tile (720 px = one tile step), variant picked by `texture_id` |
| `<d>.corridor_mid.png` | 720x720 RGBA | mid-layer (alpha silhouette of arches/pillars; visually checked) |
| `<d>.corridor_bg.png` | 720x720 | far background tile (render parameter `y_offset -80`) |
| `<d>.corridor_door.basic.png` | 720x720 | door tile at both ends of a hallway |
| `<d>.endhall.01.png` | 720x720 | dead-end cap |
| `<d>.foreground_top.01.png` / `foreground_bottom.01.png` | 720 x 226-332 / 720 x 101-215 | parallax foreground strips (`foreground_absolute_movement 0.06` in `dungeon_animations.darkest`) |
| `<d>.room_wall.<name>.png` | 1920x720 (one 1896x720, one 1920x719) | room backdrops (crypts: altar, arch, barrels, drain, empty, entrance, library, torture; weald: clearing, corruptedcabin, crypt, gate, handtree, poisonriver, shroomland; warrens: brickton, duct, effigy, ghetto, grate, meatlocker, shrine, sluice; cove: city, coral, grotto, handtree, shipwreck, temple, wallcrack, whale) |
| `<d>.entrance_room_wall.png` (+ `.plot_<quest>.png` variants), `<d>.final_room_wall.plot_<quest>.png` | 1920x720 | entrance and boss room variants |
| `colour_grade.png`, `colour_grade_0..4.png` | 16x256 | colour-grading LUT, one per torch band (+ per-quest variants in weald for crow/retention quests) |
| `_shared/secretroom.png`, `prisoner.png` | 1920x720 | secret room, prisoner room |
| `_shared/heartroom.png`, `starfield.png` | 1024x1024 | DD heart room / starfield backgrounds (`raid_visuals.json` animations) |
| `<d>.props.darkest`, `dungeon_render_parameters.darkest` (DD quest folders), `dungeon_animations.darkest` | text | layer offsets and parallax |

World geometry (`scripts/world.darkest`): `distance_to_interior_background` 400, `..._foreground` -300, `distance_to_midground` 1000, `distance_to_farbackground` 1500, `curio_tile_centre_x_offset` 135 (room 75), `curio_z_position` 75, `trap_z_position` 15, `trap_tile_centre_x_offset` 0, `obstacle_tile_centre_x_offset` 75. Camera (`scripts/camera.darkest`): corridor offset from party leader (80, 270, -1400), battle offset (180, 280, -1240), zoom 1.25 (walking back 0.8), FOV 75 regular / 90 max, aspect 2.67.

### 9.4 Curio, trap, obstacle, door art

Spine 2.1.27 folders under `<DD1>\props\shared\curios\<id>\`, `props\shared\traps\<id>\`, `props\shared\obstacles\<id>\`, `props\<dungeon>\doors\door0\`. Example `props/shared/curios/locked_strongbox/`: `.skel` 975 B, `.atlas` 375 B, `.png` 1029x394 RGBA; regions `active` 385x299 (rotated), `closed` 390x300 (rotated), `open` 422x371; animations `idle`, `investigate`. Skeleton count in `props/`: 88. Runtime definitions: `props/prop_definitions.json`, `trap_definitions.json`, `obstacle_definitions.json`, `props/<dungeon>/prop_definitions.json` (door variants).

### 9.5 UI panels, map icons, scrolls, fonts, loading screens

* `panels/` (634 files, plain PNG): `panel_map.png` 720x360, `panel_inventory.png` 720x360, `panel_hero.png` 720x224, `panel_banner.png` 754x136, `map_tab_highlight.png`, `retreat_button.png`, `quest_continue_raid.png`, `quest_return_to_hamlet.png`. **Map/minimap icons** `panels/icons_map/`: rooms 64x64 (`room_entrance`, `room_battle`, `room_curio`, `room_treasure`, `room_boss`, `room_empty`, `room_unknown`, `LockedRoom` + Blue/Green/Red/Yellow, `PrisonerRoom`, `marker_room_visited`, `moving_room`), hall markers 24x24 (`marker_battle`, `marker_curio`, `marker_hunger`, `marker_obstacle`, `marker_secret`, `marker_trap`, `hall_clear`, `hall_dim`, `hall_dark`, `hall_door`), `indicator.png` 51x48, `scoutingbanner.png` 366x63. Item icons `panels/icons_equip/<type>/inv_<type>+<id>.png` (e.g. `supply/inv_supply+torch.png` 72x144); `icons_curio_tracker` 15 icons; `icons_ability`.
* `scrolls/` (26 files): `event_scroll_basic.png` 456x518, `event_scroll_campingrespite.png` 456x237, `meal_scroll.png` 456x333, `event_scroll_loot*.png`, `inset_hunger.png` 347x147, `ancestor_talk.png` 72x72, bark balloons, `eat.png`, `starve.png`, `pass.png`.
* `loading_screen/` (45 files, mostly 1920x1080 RGB PNG, 78 MB). `shared/` UI folders (`shared/estate`, `shared/progression`, `shared/resolve_level_bar`, ...).
* Fonts (`fonts/`, 173 files): BMFont text `.fnt` (30) + 1024x1024 uncompressed 32-bit TGA pages (142). `fonts/fonts.darkest` maps ids: `ubuntu_small` (`ubuntu.fnt`), `ubuntu_medium` (`ubuntu_m.fnt`), `dwarven_axe_medium` (`dwarvenaxe-m.fnt`), `dwarven_axe_large` (`dwarvenaxe-l.fnt`), `popup`, `pips`; Russian overrides `russian_dwarven_axe_m/l`, `russian_popup`; CJK appended fonts. These are bitmap fonts, not TTF.
* Hamlet building-state colour LUTs: `<DD1>\colours\*.png`.


## 10. Localization

* Source files: `<DD1>\localization\*.string_table.xml` (17 files, e.g. `miscellaneous` 6.4 MB, `dialogue` 9.5 MB, `heroes`, `curios`, `journal`, `names`, `party_names`, `achievements`, platform sets `PSN`, `switch`, `xb1`, `steamdeck`, `iOS`, `apple_inapps`, `arena_base`, `backertrinkets`, `workshop`).
* Structure (verified): `<?xml ...?><root><language id="english"><entry id="..."><![CDATA[text]]></entry>...</language><language id="french">...</language>...</root>`. **Every language is inside every XML**: `brazilian, czech, english, french, german, italian, japanese, koreana, polish, russian, schinese, spanish` (12; `xb1` lacks japanese). So **Russian is inside the XML** and `russian.loc2` is only a compiled copy.
* Duplicate ids are intentional: several `<entry>` with the same id are alternative lines (random pick), e.g. barks and `str_prisoner_damage_*`.
* `.loc2` (`localization/<language>.loc2`, plus `pc/`, `iPhone/`, `nx/`, `ps4/`, `psv/`, `sDeck/`, `xb1/` platform subsets and `miscellaneous_<lang>.loc2`): **binary**. `localization/localization.bat` = `START ../_windows/win32/localization.exe >>error_try.txt`, i.e. `<DD1>\_windows\win32\localization.exe` compiles the XMLs into `.loc2` (also `project_paths.txt`). Header of `english.loc2` (859,017 B): three u32 (117220, 192628, 352492 = section sizes/offsets), then a 512-entry hash bucket table of `(offset, count)` pairs starting at 0x0C, then 12-byte records `(key, index, 1)` - exact layout **not fully decoded**. Recommendation: parse the XML, ignore `.loc2`.
* Tooling in folder: `subnames.ahk` (AutoHotkey script that turns Steam-backer `subscriber-list.csv` into `names.string_table.xml`), `missing_strings.csv`.
* Entry counts (english, `miscellaneous`): 4,156 entries (4,089 unique ids): prefixes `str_vo` 575, `str_inventory` 295, `str_monstername` 265, `str_quirk` 256, `town_quest` 221, `str_monster` 219, `[pc]str_help` 136, `str_glossary` 129, `town_event` 113, `str_returned` 108, `tutorial_popup` 83, `buff_rule` 76, `upgrade_tree` 63, `str_plot` 63, `camping_skill` 42. `curios`: 433 entries (415 `str_curio_*`, 15 `curio_tooltip_*`, obstacle strings). `heroes`: 577 (upgrade_tree 144, combat_skill 134, camping_skill 66, action_verbose 47, hero_class 16, ...). `journal`: 46. `names`: 562 `hero_name_*`.
* Formatting tokens in text: `{colour_start|<style>}...{colour_end}`, `{?name}%d` / `%s` printf-style placeholders (`{?level}%d`, `{?hero_names}%s`), `[pc]` prefix for PC-specific variants of help strings.

Id naming schemes (all verified by sampling):

| Content | Id pattern | Example |
|---|---|---|
| Building name | `town_name_<building>` | `town_name_abbey` = Abbey |
| Activity name / description | `town_activity_name_<activity>`, `town_activity_description_<activity>` | `meditation` = Cloister |
| Upgrade tree name/tooltip | `upgrade_tree_name_<tree>`, `upgrade_tree_tooltip_description_<tree>` | `upgrade_tree_name_abbey.meditation` |
| Activity result log | `str_<activity>_<result>_story`, `str_<building>_<activity>_upgrade_lvl_N` | `str_meditation_go_missing_story` |
| Caretaker / crier flavour | `str_caretaker_<activity>`, `str_crier_<activity>` | |
| Building help | `[pc]str_help_town_<building>_N_PC`, `building_verbose_<building>` (heroes xml) | |
| Quest (generated) | `town_quest_name_<type>+<length>+<dungeon>+<goal>`, `town_quest_description_...` | `town_quest_name_cleanse+1+crypts+battle_all_rooms` |
| Quest (plot) | `town_quest_name_plot_<id>`, `town_quest_description_plot_<id>`, `town_quest_progress_plot_<id>` | `plot_kill_hag_2` |
| Quest goals | `town_quest_goal_start_plural_<goal type>`, `..._single_...`; `str_caretaker_goal_plot_<id>` | `town_quest_goal_start_plural_explore_room` = "Explore %.0f%% of rooms." |
| Quest meta | `town_quest_difficulty_<1,3,5,6>`, `town_quest_length_<1..4>`, `town_quest_number_of_camps<0..4>`, `dungeon_name_<dungeon>` | |
| Curios | `str_curio_title_<id>`, `str_curio_content_<id>`, `str_curio_tooltip_investigate_<id>`, `str_curio_<id>_<result>` | see 5.2 |
| Narration | `str_vo_<group>_<name>_<part>` | see 8.1 |
| Plot audio/tip | `str_plot_<id>_audio_line`, `str_plot_<id>_tip` | |
| Town events | `town_event_title_/description_/info_<id>` | |
| Journal | `journal_page_title_<N>`, `journal_page_text_<N>` | |
| Items | `str_inventory_title_<type><id>`, `str_inventory_description_<type><id>` | `str_inventory_title_quest_itemancestors_crate` |
| Heroes | `hero_class_*`, `action_verbose_body_guild_<class>`, `<class>_weapon_*` | |
| Death causes | `str_death_<cause>_<source>` | `str_death_hunger_hunger` |
| Hunger / meal | `str_ui_hunger_*`, `str_meal_title_0..3`, `str_meal_heal_format` | |


## 11. Loadability from a Unity plugin and open items

| Asset | Format | Effort to load at runtime from the DD1 install |
|---|---|---|
| Hamlet UI, panels, scrolls, loading screens, map icons, dungeon walls/doors/rooms, colour LUTs, quest art | PNG (8-bit RGB/RGBA) | trivial (`Texture2D.LoadImage`) |
| Building exteriors, curios, traps, obstacles, doors, fx, town-event sprites | Spine 2.1.27 binary `.skel` + text atlas + PNG | static frames (`idle`/`active`/`closed`/`open`): easy, cut atlas regions (handle `rotate: true`); animation: needs the Spine 2.1 C# runtime (not compatible with Spine 3.x/4.x runtimes) |
| Fonts | BMFont `.fnt` + 1024x1024 TGA (32-bit uncompressed) | custom `.fnt` parser + TGA decoder (or render your own text with DD2 fonts) |
| Strings | XML (all 12 languages in each file) | easy (XmlReader; keep duplicate ids as variants) |
| Gameplay data | JSON (quest, town, buildings, upgrades, loot, inventory rules, rules.json) and `.darkest` text blocks | easy; `.darkest` needs a small `name: .field value` parser |
| Curio library | CSV spreadsheet | easy but layout is fixed 16-line blocks (see 5) |
| Fixed maps (`.dm`), savegames | DSON binary (magic `01 B1 00 00`) | needs a DSON decoder (known community format; not needed for generated dungeons) |
| Narration, music, ambience, SFX | FMOD Studio banks (bank format version 103, `fmod.dll`/`fmodstudio.dll` shipped in `_windows/win32`) | needs the FMOD Studio runtime (the bundled DLLs are old; a newer FMOD Studio can usually load older banks; whether it can coexist with DD2's audio is **not verified**) or offline extraction of the FSB5 streams |
| Intro/outro video | Ogg Theora 1080p + Vorbis (`.ogv`) | Unity `VideoPlayer` supports `.ogv` on desktop (not verified here) |

Not found in data / not verified (candidates for follow-up):
* exact weekly-quest selection algorithm, week increments per quest length, Darkest Dungeon unlock condition (code, not data);
* torch item light value, `tile_light_loss` semantics, scouting `N - target` semantics, content/tile enum numbers in the map JSON;
* number of heirloom types per generated quest, `hallway_curio` meaning, recruit generation (quirk/skill counts for base recruits), trap disarm formula;
* `.loc2` exact layout; FMOD event names for music/ambience (strings bank is prefix-compressed).
