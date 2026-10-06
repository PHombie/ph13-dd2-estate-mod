# DD2 Estate — story and content mapping

DD1's places and the Ancestor's frame, inhabited by DD2's factions and bosses. This file is the
reference for `src/DD2Estate/Data/dungeons.json` and `src/DD2Estate/Data/plot_quests.json`; every DD2
id in those two files is proved against the installed game by `python tools/check_content.py`
(exit 0 = all ids exist, 1 = problems, 2 = game not found). All narration is original text for
this mod, English only. Arena choices are made from scene names and what the vanilla game uses
them for; none of them has been looked at in game from this side yet (see section 9).

Contents: 0 data schema · 1 premise · 2 the four domains · 3 the Darkest Dungeon · 4 wanderers ·
5 DD1 → DD2 table · 6 tiers and DD2's scaling · 7 boss scaling · 8 what the code must guard ·
9 open questions · 10 changes against the DESIGN.md draft

## 0. Data schema

### 0.1 `dungeons.json`

```
{
  "format": 1,
  "dd2_version": "2.04.85095",          game build the ids were checked against
  "host_game_type": "kingdom",          Excel/kingdom overrides apply, Excel/expedition is not loaded
  "tier_rules": { "<tier>": { "dd1_difficulty": 1|3|5|6, "hero_level": n, "escalation": 1|2|3 } },
  "dungeons": [ {
    "id": "crypts",                     DD1 internal id (crypts, weald, warrens, cove, darkestdungeon)
    "name": "Ruins",
    "dd1_art": "crypts",                DD1 dungeon folder for corridor/room art
    "confession": { "act": "brain|lungs|eyes|arms|final", "reason": "..." },
                                        which act's blessing rows ordain this dungeon (BossModifier id prefix)
    "unlock": { "quests_completed": n, "requires": [plot quest ids] },
    "factions": [ { "id": "...", "weight": n, "lore": "one line" } ],   population share, for UI and weights
    "arenas": { "hallway": [scene], "room": [scene] },                 passed to CombatScenarioData
    "tiers": { "<tier>": {
        "ordain": null | { "boss_modifier": BossModifier id, "chance": 0..1 },   regular monsters, see 6.3
        "hallway": { "configs": [BattleConfiguration id], "tables": [BattleConfigurationTable id],
                     "weights": { "<config or table id>": n } },
        "room":    { same } } },
    "bosses": [ {
        "id": "...", "dd1_slot": "necromancer", "name": "...",
        "actor_ids": [ActorDataClass id],        the boss, its parts and everything it summons
        "escort_actor_ids": [ActorDataClass id], other actors standing in its fights (used by wanderers)
        "tags": ["boss", "biome_boss" | "gang_boss" | "end_boss"],   as in the game's ActorDataClass rows
        "guards": ["gang_boss_tag" | "end_boss_tag" | "end_sequence" | "end_boss_cinematic"],  see section 8
        "dlc": null | "<dlc folder>",
        "tiers": { "<tier>": {
            "config": BattleConfiguration id | null,
            "config_table": BattleConfigurationTable id | null,     exactly one of config / config_table / configs
            "configs": [BattleConfiguration id],                     (wanderers) pick one
            "chains_to": [id],                   waves the fight starts by itself (intended chains only)
            "arena": scene | null,
            "arena_source": "config_override" | "mod" | "dungeon",
                    config_override = the config's own m_BackgroundSceneOverride, shown whatever the mod passes
                    mod             = the config has none; pass `arena`
                    dungeon         = the config has none; use the dungeon's hallway/room arena
            "escort_tables": [BattleConfigurationTable id],   fights to win before the boss, in order
            "escort_native_chain": bool,         true = the last escort fight chains into the boss by itself
            "escort_arena": scene | null,        the escort configs' own override, when they have one
            "boss_modifier": BossModifier id | null,       ordain the boss with this row (LibraryBossModifier)
            "external_buffs": DataExternalBuffs id | null, the buffs the tier adds; delivered by boss_modifier
                                                           when that is set, otherwise attach them directly
            "battle_modifier": BattleModifier id | null,   force for the boss fight
            "notes": "..." } },
        "notes": "..." } ],
    "dlc_extras": [ { "dlc": "<dlc folder>", "factions": [...], "arenas": {...},
                      "tiers": { "<tier>": { "hallway": {...}, "room": {...} } }, "notes": "..." } ]
  } ],
  "wandering": [ {
    same header as a boss, plus
    "dungeons": [dungeon id], "trigger": { "type": "...", ... }, "lore": "one line",
    "tiers": { "<tier>": fight block }   and/or   "per_dungeon": { "<dungeon id>": fight block }
  } ]
}
```

Tier names: `apprentice`, `veteran`, `champion` (four domains), `darkest` (Darkest Dungeon only).

How to draw a fight from a pool: build one list from `configs` and `tables`, pick an entry by
`weights` (missing = 1), and if it is a table let the game roll it
(`BattleConfigurationTable`). A faction's share of a pool is `60 x faction weight`, split over its
entries. Pool rules the checker enforces: every table is condition-free all the way down, no fight
chains another wave, no fight carries its own arena, and the only additional table is the vanilla
Death hook (4.3). `dlc_extras` entries are merged into the base pools and arena lists only when
that DLC is owned.

### 0.2 `plot_quests.json`

```
{
  "format": 1, "language": "en",
  "darkest_chain": { "count": 5, "reason": "..." },
  "quests": [ {                       in story order; `requires` only names earlier quests
    "id": "plot_librarian_1",
    "type": "kill_boss" | "explore",
    "dungeon": dungeon id, "boss": boss id | null, "tier": tier name,
    "length": "short" | "medium" | "long",
    "dd1_quest_id": "plot_kill_necromancer_1",   DD1 plot quest whose rewards, loading screen and
                                                 quest-select art the code may read from the DD1 install
    "name": "...", "goal_text": "...", "intro_narration": "...", "victory_narration": "...",
    "unlock": { "requires": [quest id], "dungeon_level": 0..7 }   level of that quest's dungeon
  } ]
}
```

30 quests: one opening expedition, 8 boss lines x 3 tiers, 5 Darkest Dungeon descents. Unlock
levels follow DD1 (first line at dungeon level 2/4/6, second line at 3/5/7); each tier also
requires the previous tier of its line. The first Darkest Dungeon quest requires the eight
apprentice bosses; each later descent requires the one before.

## 1. Premise

The house stood for generations on a hill above the hamlet, and the last lord to live in it was
bored. He read his way to a rumour: that the hill was older than the house, and that under its
deepest cellar lay a door to something older than the hill. He began to dig.

The dig ate money, then people, then sense, and every time it ran short of one of them he found
an expedient. Each expedient is still on the estate.

He needed the meaning of what the diggers brought up, so he hired a keeper for the forbidden
shelves and scholars to sit under him. He needed silence, so he hired a battalion to hold the
roads and keep the hamlet's mouth shut, and when the pay chest was empty he marched it into the
Weald on a false errand and cut the bridge behind it. He needed to know how flesh is put together
and went to the women of the wood, who sold him the craft for a price in tenants. He needed a body
for what he hoped to bring through the door and made bodies under the old aqueduct, penned the
failures, set his huntsmen to herd them, and in a hungry year sold the carcasses to the gentry as
cheap meat. He needed cargoes landed without questions and paid the sea its toll at the Cove, in
lanterns, then livestock, then daughters. He needed to be admired while all this went on, and
poured for his guests a vintage he should have left corked.

The diggers who went deepest came back with a creed. The hamlet, which had put up with a great
deal, finally came up the hill with torches. They never went home; what they could not burn they
stayed to guard, and the fire got into them. The guests fled by barge. The keeper of the books
took the mob's torches and taught it what deserves burning.

By then the lord had reached the door. What was behind it was no treasure and no god that could
be bargained with. It took his own failings and gave them flesh: what he refused to see, what he
never forgave, what he could not look away from, what he grasped for, and at the last what he ran
from. He went back up to his study, wrote to his heir, and answered the question with a pistol.

You are the heir. He narrates: a dead man who can finally afford candour, walking you through his
accounts one creditor at a time. The four domains hold the people he used. The fifth holds him.

This keeps DD1's frame (the letter, the hamlet rebuilt week by week, a confession attached to
every boss) and explains DD2's cast without DD2's road: nobody here is fleeing to a mountain, they
are all still where he left them. DD2's five confessions, which in DD2 belong to its own narrator,
become the Ancestor's.

## 2. The four domains

### 2.1 Ruins (`crypts`) — Denial

**Who lives here.** *Fanatics* (weight 5): the hamlet's mob, turned zealot by years of keeping
its own fire lit; they burn whatever is written and whoever reads. *Gaunts* (3): servants and
tenants hollowed out by hunger and by what they saw in the house. *Cultists* (2): the diggers'
congregation, keeping chapel among the family dead. With the Inhuman Bondage DLC the *Vermin* (2)
are added: the family vaults under the manor, whose caretakers never stopped interring.

**The Librarian** (DD1's Necromancer line). DD1's necromancers were scholars the Ancestor lured
and used; this is the same debt with a different face. He was hired to keep the books his master
dared not read alone, and he read all of them. When the mob broke in he did not run. He took its
torches, gave it a doctrine, and has been deciding ever since which pages may exist. Three visits:
the keeper at his index, the keeper with the stacks rebuilt and a new volume about you, and at the
last the reader with one book still closed on his knee. Fight: the Sprawl lair boss with his three
book stacks; he ignites partway through.

**The Exemplar** (DD1's Prophet line). The first man to come up from the deep shaft singing. The
Ancestor had him flogged and he said thank you. He preaches in the crypt under the house, and
because Exemplar is a title rather than a name, killing him only promotes someone. The second
wears the same robe over a face stretched to fit; the third has actually seen what he praises,
and everything he says is true. Fight: the Kingdoms Tundra lair boss with his altar, summoning
cherubs, heralds and evangelists.

### 2.2 Weald — Resentment

**Who lives here.** *The Lost Battalion* (4): the unpaid company, still at its posts, with the
wood grown through it. *Pillagers* (3): the living deserters and scavengers who strip the estate
and camp where no bailiff goes. *The Coven* (2): the herb-women and their daughters, brewing in
the hollows. *Beasts* (2): spiders, rabid dogs and carrion eaters, as in DD1's Weald. *Gaunts*
(1): woodcutters who waited too long to leave.

**Mother of Threads** (DD1's Hag line). He came to her when his own arts failed, to learn how
flesh is knit. She named her price in tenants and he paid from the rent rolls without reading the
names. She still sits at the loom; the thread is not wool. Cut down, she is knotted back together
by her daughters; the third time she has woven the wood itself into the work and left one place
open in the pattern. Fight: the Kingdoms coven boss, who trades her spitter for fungus and back.

**The Dreaming General** (DD1's Brigand Pounder line). DD1's brigands were mercenaries hired to
silence the hamlet; these are the same men with their colours still flying. The battalion was
never dismissed, so it never left. Its General lies under a tap root, and the root carries his
dreams through the whole wood as orders: muster rolls, arrears, reveille. By the third visit he
is more tree than man and the last order still stands, hold until relieved. Fight: the Tangle
lair boss; kill the Tap Root and the General dies with it.

### 2.3 Warrens — Ambition

**Who lives here.** *Swine* (5): the bodies he made for the thing below, which the thing declined.
*Plague Eaters* (3): the hamlet's gentry, who bought the carcasses cheap and found they could eat
nothing else afterwards. *Beastmen* (2): his huntsmen and kennel-men, sent down with hooks and
chains to herd the stock until nobody could tell herdsman from herd. *Gaunts* (1): the labourers
who dug the pens and were shut in with them.

**Meat Hook** (DD1's Swine Prince line). The kennel-master. He hangs his kills on a post, and the
tribe hangs him on it to mend when he falls, as they would cure a carcass. Each time he comes
back leaner and the swine follow him more willingly; the last thing to stand at the post was
never taught to kneel and remembers who ordered the hooks forged. Fight: the Kingdoms beastmen
boss and his Meat Post. (The Swine themselves have no boss in DD2's data; their champion brute
`cave_swine_brute_b` is tagged `wilbur` and comes in with the champion tables.)

**The Harvest Child** (DD1's Flesh line). DD1's Flesh was what he threw away. Here he sold it. The
gentry's table is still laid in the lowest larder, the discarded meat has learned to pile itself
on the platters, and a small thing sits at the head of the board that has grown no older and has
only grown. It is the one creditor on the estate that never asked him for more. Fight: the Foetor
lair boss between its two heaps of meat.

### 2.4 Cove — Obsession

**Who lives here.** *The Coastal folk* (6): dockers, bosuns and smugglers who landed his cargoes,
drowned in the fog and kept their shifts. *Courtiers* (3): the guests of the last revel, washed
back in on the barge they fled in. *Gaunts* (1): fisherfolk with nothing left to sell the sea.

**The Leviathan** (DD1's Siren line). DD1's Siren was a girl given to the water. Here the giving
was regular: a toll for safe passage, paid each quarter-day past the shoal, until something large
grew fond of the arrangement. It still comes in on the tide to collect, and the dockers still
leave tribute on the stones out of habit. Its summoned hands lay hold of a hero as the Siren's
song once took one. By the third visit it has stopped waiting for the tide and lies across the harbour floor
with a hand in every cellar. Fight: the Shroud lair boss.

**The Archduke** (DD1's Drowned Crew line). The crew this time is a court. He poured them a
vintage that left them thirsty for good; when the mob came they took to a barge, and the barge
came back holed and listing with the party still going. The Archduke receives in the wreck, sends
out invitations the dockside pays for, and at the last looks inland to the house where the first
bottle was opened, meaning to thank his host. Fight: the Kingdoms courtier boss, who calls his
court to the table as the fight goes on.

## 3. The Darkest Dungeon — Cowardice

Below the cellars the cultists are at home and the walls are warm. The chain has **five** quests,
one per DD2 act boss, because each of those fights is a confession and the Ancestor has five to
make. DD1's chain has four quests but only two boss fights (the second and third are curio
objectives with no DD2 counterpart), and five descents give DD1's "never again" rule twenty heroes
to spend instead of sixteen. Quest lengths: medium, long, long, long, short.

| # | quest | boss (DD2 act) | what he confesses |
|---|---|---|---|
| 1 | What I Would Not See | Shackles of Denial (`brain`) | He called it a discovery and the screams wind in the shafts. Four locks, each fastened by his own hand so that he need not know what he knew. |
| 2 | The Breath I Held | Seething Sigh (`lungs`) | The hamlet laughed at the idle lord and his hole in the ground. He kept every laugh warm for years. They were right, and he hated them for it. |
| 3 | The Unblinking Study | Focused Fault (`eyes`) | He could not look away from the work, and never once looked at who stood beside him. |
| 4 | The Grasping Hand | Ravenous Reach (`arms`) | He wanted to be owed by creation itself. The wanting took the silver, the hamlet and the estate, and reaches for the heir as the last thing unspent. |
| 5 | The Body of Work | Body of Work (`body`) | At the end he saw what he had made and fled. The heir is asked to do the one thing he could not: stay. |

The finale keeps DD2's fight unchanged (stomach, torso, then the thing itself with its proclaimers
and the party's own failures summoned as spectres). In this telling the spectres are the heir's
heroes as the Ancestor would have used them. The victory text closes the letter; the hamlet scene
after it is the epilogue (DD1's `epilog` video can play there, the DD2 end cinematic must not).

## 4. Wanderers and optional bosses

| who | where | when (suggested rule) | lore |
|---|---|---|---|
| The Collector | four domains | DD1 rule: hallway roll once the inventory is at least 79% full (3/4/5% by tier), once per quest | He came for the Ancestor's cabinet of curiosities and stayed for whoever carries the most out of it. |
| The Shambler | four domains | DD1 rule: its altar curio, or a hallway roll at torch 0 (1/8/12%) | It was here before the house and waits wherever the light is let die. |
| Death | everywhere | vanilla hook (4.3) or after three Death's Door escapes in one quest | The estate's oldest tenant, come to collect from those who have slipped her too often. |
| Ancient Adversary | four domains | a statue curio in a room; six-round fight | He raised his own likeness in every corner of the estate. Something has moved into them. |
| The Chirurgeon | Warrens | rare named room | The estate's physician, set to cutting the failures apart; he never stopped and has run short of failures. |
| The Antiquarian | Weald | hallway roll once the inventory is at least 70% full (DD2's own rule) | Hired to appraise the heirlooms; she now shows the pillagers which crates are worth carrying. |
| The Exemplar (successor) | Darkest Dungeon | rare named room | A title, not a man; down here there is always another to wear it. |
| The Warlord `dlc_dul_cru` | Weald | rare named room | The Battalion's last living officer, still levying the Old Road toll for a dead paymaster. |

Trigger numbers are proposals for the code and balance pass; only the ids are checked.

**4.3 Death's vanilla hook.** Most faction fights carry
`m_AdditionalBattleConfigurationTableId,death_mashes`. That table adds `death_e1/e2/e3` at weight 6
against 94 "nothing" when `hero_party_has_flagellant + is_kingdoms + escalation_is_N` hold. Under
the mod's kingdom host, with the `escalation` run value set per tier and a Flagellant in the party,
Death therefore arrives by herself. The checker's summary counts how many fights in each pool have
the hook.

## 5. DD1 thing → DD2 replacement → reason

| DD1 | DD2 replacement (ids) | reason |
|---|---|---|
| Ruins: bone soldiers, cultists | Fanatics `fanatic_*`, Gaunts `shared_lost_soul*`/`shared_ghoul`, Cultists `cultist_*` | the mob that stormed the manor, the household's lost, the diggers' creed |
| Necromancer (3 tiers) | The Librarian `fanatic_librarian`, table `biome_city_dungeon_round3` | the scholar he hired and ruined, as DD1's necromancers were |
| Prophet (3 tiers) | The Exemplar `cultist_exemplar`, `tundra_dungeon_3` | the preacher of what lies below; a title that survives its holder |
| Weald: brigands, fungal, spiders, dogs | Lost Battalion `lost_battalion_*`, Pillagers `shared_pillager_*`, Coven `coven_*`, beasts `shared_spider_*`/`shared_dog_rabid`/`shared_carrion_eater` | hired soldiers and deserters; witches and their fungus; the same vermin as DD1 |
| Hag | Mother of Threads `coven_matres_sequelae`, `coven_boss` | the witch he bargained with; Forest is the coven's vanilla boss region |
| Brigand Pounder | The Dreaming General `lost_battalion_boss_dreaming_general`, `forest_dungeon_3` | the brigands' story (mercenaries hired to cow the hamlet) told about their commander |
| Warrens: swine | Swine `cave_swine_*`, Plague Eaters `plague_eater_*`, Beastmen `beastmen_*` | the made things, those who ate them, those who herded them |
| Swine Prince / King / God | Meat Hook `beastmen_meat_hook`, `beastmen_boss` | DD2's Swine have no boss; the pit-master is the head of the herd |
| Flesh | The Harvest Child `plague_eater_harvest_table`, `farm_dungeon_3` | a table of discarded meat is the Flesh almost literally |
| Cove: pelagics | Coastal `coastal_*`, Courtiers `courtier_*` | the drowned dock gangs; the guests who fled by water |
| Siren | The Leviathan `coastal_boss_leviathan`, `coast_dungeon_3` | what the tribute was paid to; its hands take a hero as the song did |
| Drowned Crew | The Archduke `courtier_archduke`, `courtier_boss` | a ship's company that came back; Coast is the courtiers' vanilla boss region |
| Darkest Dungeon monsters | Cultists (deacon, cardinal, the Mountain fights) | they were always the Darkest Dungeon's faction |
| Shuffling Horror / Templars / Cyst / Heart | Shackles of Denial, Seething Sigh, Focused Fault, Ravenous Reach, Body of Work (`mountain_boss_*`) | five confessions instead of four objectives |
| Collector | `shared_collector`, `collector_region_1/2/3` | same character; DD1 trigger kept |
| Shambler | `shared_shambler`, `shambler` | same character; DD1 trigger kept |
| the `ancestor` obstacle and the statue building | Ancient Adversary `shared_ancestor_statue`, `ancestor_statue_<region>` | DD2 already has his statue as a fight |
| Brigand Vvulf (town event boss) | The Warlord `shared_warlord` (DLC, optional) | a bandit captain with barricades and guards |
| Sanitarium horrors (no DD1 boss) | The Chirurgeon `shared_lost_soul_chirurgeon` | the physician of the experiments; wandering only |
| Antiquarian (hero class) | The Antiquarian `shared_pillager_antiquarian` | in DD2 she is on the other side; wandering only |
| Crimson Court (DLC) | Courtiers in the Cove | the same curse, folded into the base story |
| Shrieker, and DD1's DLC bosses (the Crimson Court's Fanatic and Crocodilian, the Miller, the Thing from the Stars) | not mapped | no DD2 counterpart worth forcing |
| (none) | Death `shared_death` | DD2 only; the hook is already in the fights |
| (none) | Vermin `vermin_*` (DLC, optional, Ruins) | the Catacombs are a crypt; "Crypt Keeper" belongs under the manor |

Not used at all: the Kingdoms sieges as such (their fight lists are used, their rules are not),
`beastmen_alpha_wave` (an eight-enemy quest wave with quest clean-up effects), hero origin fights,
Kingdoms militia actors, the military barricade troops outside the Warlord and
`military_forest_mashes`.

## 6. Tiers and DD2's own scaling

### 6.1 How DD2 scales a fight

Read from `battle_configuration_data_export`, `battle_configuration_table_data_export`,
`kingdom_battle_configuration_data_export`, `boss_data_export`, `boss_blessing_data_export`:

* **Composition rungs.** Regional factions have `<faction>_mash_1xx` starter fights (the ones used
  here fill two or three of the four rank slots, 33-68 HP in total; no table references them),
  `2xx` = table `<faction>_mashes_normal` (four slots), `3xx` = `_mashes_hard` (the big unit types),
  `C`/`C_2` = `_normal_champions` / `_hard_champions` (one named `_b` enemy), `BC` =
  `_brutal_champions` (two of them). Cultists use `cultist_mashes_normal` / `_hard` /
  `_normal_enhanced` (deacon) / `_hard_enhanced` (cardinal) / `_brutal_enhanced` (both).
* **Region index and act** in Expedition, **escalation 1/2/3** in Kingdoms decide the mix. In
  Kingdoms the Resistance tables draw normal only at escalation 1, normal:hard 1:1 at 2 and 1:3 at
  3; inside them the champion tables enter at weight 2 against 10 at escalation 2 and 5 against 10
  (16 against 24 in hard) at 3, and the brutal table needs escalation 3 plus the level-5 infernal
  torch. The same run value picks `collector_region_N`, `death_eN` and the gang battle modifiers.
* **Gangs** (beastmen, coven, courtier) come pre-tiered: `<gang>_mashes_siege_easy` / `_normal` /
  `_hard`, each fight forcing `battlemodifier_<gang>_1/2/3`.
* **Ordainment.** `BossModifier` rows give an actor extra HP and damage plus an act-specific trick,
  by region index: `<act>_boss_typical_biome_1/2/3` for regular monsters (of the 82 actors in the
  pools all but the two barricade props and the ectoplasm corpse carry the `*_blessing` tags) and
  `<act>_infernal_flame_lair_boss_biome_1/2/3` for lair bosses, their hands
  and the Harvest Child's meat (tags `biome_boss`, `boss_hand`, `meat`). Buff sets:
  `generic_blessing_biome_1/2/3` = +20/30/40% HP and damage; `brain_` and `lungs_blessing_biome_1/2/3`
  = +10% HP / +20% / +30% HP and damage.
* **Battle modifiers** rolled per fight (`elite_monsters` = +20% HP and damage, and so on).

### 6.2 The mapping

| tier | DD1 | `escalation` | hallway pool | room pool | regular monsters ordained with |
|---|---|---|---|---|---|
| apprentice | level 1 | 1 | `1xx` starters, gang "valley siege" trios, small gaunt and beast packs | `normal` tables, gang `siege_easy` | nothing |
| veteran | level 3 | 2 | `normal`, gang `siege_easy` | `hard` (2) + `normal_champions` (1), gang `siege_normal` | `<act>_boss_typical_biome_2` |
| champion | level 5 | 3 | `hard` (2) + `normal_champions` (1), gang `siege_normal` | `hard_champions` (3) + `brutal_champions` (1), gang `siege_hard` (3) + boss-approach (1) | `<act>_boss_typical_biome_3` |
| darkest | level 6 | 3 | `cultist_mashes_hard` (2) + `_normal_enhanced` (1) | `_hard_enhanced` (3), `_brutal_enhanced` (1), the two Mountain tables (2 each) | `final_boss_typical_biome_3` |

As a rule a tier's hallway pool is the previous tier's room pool, so a corridor fight is one rung
below the rooms around it (gaunts and the coven's apprentice room use their own small lists).
Numbers in brackets are relative weights inside a faction.

### 6.3 Ordainment per domain

Each domain is tied to one confession and uses that act's rows, at the game's own chances. This
also gives the four domains DD2's act-to-act slope without touching any stat:

| domain | act | veteran row: chance, effect | champion row: chance, effect |
|---|---|---|---|
| Ruins | `brain` (Denial) | 0.25: +20% HP and damage, stun/move/debuff resistance | 0.33: +30% |
| Weald | `lungs` (Resentment) | 0.33: +20%, longer DOTs, burn rider | 0.75: +30% |
| Cove | `eyes` (Obsession) | 0.40: +30%, copies positive tokens on a crit | 0.80: +40% |
| Warrens | `arms` (Ambition) | 0.50: +30%, turns block and dodge into strength and crit | 0.90: +40% |
| Darkest Dungeon | `final` (Cowardice) | - | 0.90: +40%, inverts a positive token on a crit |

### 6.4 Pool sizes

Counts are "`configs` listed + `tables` listed = distinct fights they resolve to" (from the checker).

| dungeon | apprentice hallway / room | veteran hallway / room | champion hallway / room |
|---|---|---|---|
| Ruins | 10+0 = 10 / 6+2 = 19 | 4+2 = 17 / 3+4 = 30 | 3+4 = 30 / 5+4 = 28 |
| Weald | 15+1 = 19 / 8+3 = 37 | 6+3 = 39 / 7+5 = 76 | 7+4 = 70 / 10+5 = 72 |
| Warrens | 11+0 = 11 / 6+3 = 40 | 4+3 = 38 / 9+4 = 57 | 9+4 = 57 / 5+6 = 51 |
| Cove | 18+0 = 18 / 6+2 = 25 | 4+2 = 23 / 3+3 = 35 | 3+3 = 35 / 5+4 = 35 |
| Darkest Dungeon (`darkest`) | 0+2 = 12 / 0+4 = 21 | | |
| Ruins + Vermin (DLC) | - / 0+1 = 10 | 0+1 = 10 / 0+1 = 12 | 0+1 = 12 / 0+1 = 15 |

Arenas: Ruins `combat_arena_forest_dungeon_interior`, `combat_arena_city_dungeon_interior` · Weald
`combat_arena_forest_{faction,gaunt,pillager}` in corridors and `forest_{resist,creature_den,dungeon_exterior,barricade_gang_coven}`
in rooms · Warrens `combat_arena_caves_{faction,gaunt}` and `caves_{resist,creature_den}` · Cove
`combat_arena_coast_{faction,gaunt}` and `coast_{resist,creature_den,dungeon_exterior,barricade_gang_courtier}`
· Darkest Dungeon `combat_arena_mountain_story_cultist`, `combat_arena_stressworld`.

## 7. Boss scaling

DD2 has one version of each boss. What its data offers, and what each line uses:

| boss | apprentice | veteran | champion |
|---|---|---|---|
| Librarian, Exemplar, Dreaming General, Harvest Child, Leviathan (region lair bosses) | the lair's third round alone (`<region>_dungeon_3`, City: table `biome_city_dungeon_round3`) | one fight from `biome_<region>_dungeon_round2_normal` first; those configs chain into the boss by themselves. Boss ordained with `<act>_infernal_flame_lair_boss_biome_2` | `biome_<region>_dungeon_round2_champions` first (Tundra: `_enhanced`, same seven fights), boss ordained with `..._biome_3` |
| Mother of Threads, Meat Hook, Archduke (Kingdoms gang bosses) | `<gang>_boss` alone | `<gang>_quest_boss_region_mashes` first (sequenced by the mod), then the boss with `battlemodifier_<gang>_2` and the region-2 blessing buffs attached directly | `<gang>_mashes_siege_hard` first, then the boss with `battlemodifier_<gang>_3` and the region-3 blessing buffs |
| the five act bosses | - | - | `darkest` only: reused exactly as shipped |
| Collector, Shambler, Death, Chirurgeon, Antiquarian, Warlord | reused as shipped, scaled only by the tier's ordain row (they carry the `*_blessing` tags); `collector_region_1/2/3` and `death_e1/e2/e3` are the same line-ups under three ids | | |
| Ancient Adversary | reused as shipped; it has no blessing tag, so nothing scales it | | |

Why two mechanisms: a `BossModifier` is only valid for an actor that has one of its tags
(`BossModifierDefinition.GetIsValidForActorClass`). Lair bosses have `biome_boss`; gang bosses have
`gang_boss` and none of the blessing tags, so for them the same buff set is named in
`external_buffs` for the code to attach, and the gang's own escalation modifier supplies the trick.
The lair rows have `m_Chance 0` in the data, so the mod assigns them rather than rolling.

There is no weakened variant of any boss in the data. If the apprentice bosses prove too strong for
level-1 parties the mod will have to ship its own negative buff; nothing in the game can be reused
for that.

## 8. What the code must guard

* **`end_boss`**: every part of the five act bosses. **`gang_boss`**: `beastmen_meat_hook`,
  `coven_matres_sequelae`, `courtier_archduke`. Both are listed per boss in `tags`, and `guards`
  names what to neutralise: `gang_boss_tag`, `end_boss_tag`, `end_sequence` (`mountain_boss_brain*`
  set `m_HasEndSequence` with a 5 s delay), `end_boss_cinematic` (`mountain_boss_body` names
  `m_endBossCinematicName,EndBossVictory`; the intro scene `combat_intro_boss_body` also exists).
* **`biome_boss`** (with `boss_travelogue`): Librarian, Exemplar (Kingdoms copy only), Dreaming
  General, Harvest Child, Leviathan. Their configs grant `lair_boss_rewards_all` (trophies and other
  stagecoach loot) and apply `lair_midfight_heal` (15% heal).
* **Arena overrides that win over the mod's arena**: all lair and act boss configs, the lair escort
  rounds, the statue fights (`combat_arena_<region>_creature_den`) and the Shambler
  (`combat_arena_hero_story_occultist_origin_2`). Recorded as `arena_source: config_override`.
  Nothing in the hallway and room pools has one.
* **Intended chains**: lair escort configs → boss (`m_NextBattleConfigurationId` /
  `m_NextBattleConfigurationTableId`, with `m_IsNextBattleOptional`); `mountain_boss_eyes` →
  `mountain_boss_table_eyes_phase_2`; Chirurgeon patient wave → `surgeon_node_mashes_a/b`. Nothing
  else in the data files chains.
* **`no_retreat`**: all boss fights, every gang fight (coven, beastmen, courtier), the two Mountain
  cultist tables. In the Weald champion rooms that is 25 of 72 fights.
* **Forced battle modifiers**: gang siege fights carry `m_BattleModifierOverrideId
  battlemodifier_<gang>_N`, whose own conditions are `is_kingdoms + kingdom_gang_is_<gang> +
  escalation`.
* **Coast fog**: every `coastal_mash_*` fight has `actorless_round_start_effects,start_coast_fog`
  (25% per round from round 2 to start the `coast_fog` arena modifier).
* **Torch**: `shambler` runs `torch_set_to_1` on the DD2 torch value.
* **Cultist actors** `cultist_altar`, `cultist_cardinal`, `cultist_deacon`, `cultist_exemplar` exist
  only in `Excel/expedition` and `Excel/kingdom`; the checker accepts them because the host game
  type is kingdom. A different host would change the Exemplar (101 HP, no `biome_boss` tag).

## 9. Open questions

1. Arenas were chosen by name. In particular: are the two lair interiors right for Ruins rooms,
   does `combat_arena_farm_boss_beastmen` suit Meat Hook in the Warrens (the caves have no boss
   arena; `combat_arena_caves_creature_den` is the fallback), and do the Darkest Dungeon pools look
   right in `combat_arena_mountain_story_cultist` and `combat_arena_stressworld`?
2. The Dreaming General's config forces `combat_arena_forest_dungeon_interior`, which is also the
   Ruins' generic interior. Swap the Ruins to another interior or patch the override?
3. Does a fight started through `CombatScenarioData(config, arena, ...)` follow
   `m_NextBattleConfiguration*` by itself, and what does `m_IsNextBattleOptional` show the player?
   If not, the mod sequences escort and boss with the list constructor and must not chain twice.
4. Do forced gang battle modifiers apply when their conditions fail (no gang set in the session)?
   If they are skipped silently the gang fights are simply unmodified.
5. Should `no_retreat` be lifted for ordinary gang fights so DD1's retreat rule holds everywhere?
6. Which biome does the session report? The pools avoid every biome condition, but the Death table
   and `start_coast_fog` still evaluate conditions.
7. Is the `escalation` run value writable without a Kingdoms gang, and does anything else react to it?
8. Vanilla loot tables on these configs (`road_faction_rewards_all`, `lair_boss_rewards_all`,
   `siege_rewards_all`, ...) will pay out DD2 loot; the design converts relics and baubles to gold,
   but trophies and Kingdoms resources need a decision.
9. The first Darkest Dungeon unlock (eight apprentice bosses) is a proposal; DD1's own rule is not
   in its data files.

## 10. Changes against the DESIGN.md section 3 draft

| draft | now | why |
|---|---|---|
| Ruins = Lost Battalion, Cultists, Gaunts; Necromancer → Dreaming General, Prophet → Librarian | Ruins = Fanatics, Gaunts, Cultists; Necromancer → Librarian, Prophet → Exemplar | the data has exactly eight full bosses (five lair, three gang) for DD1's eight lines; this is the only assignment that uses all of them. The Exemplar is the closest thing in DD2 to a prophet, and the Librarian to a ruined scholar |
| Weald = Pillagers, Plague Eaters, Beastmen; Hag → Harvest Child, Brigand Pounder → Antiquarian | Weald = Lost Battalion, Pillagers, Coven, beasts; Hag → Mother of Threads, Pounder → Dreaming General | the Antiquarian is a 25 HP escort-dependent mini-boss with no arena; the coven has a witch boss whose vanilla region is the forest; the Battalion's home arenas are forest arenas |
| Warrens = Swine, Vermin, Beastmen; Swine Prince → Beastmen Alpha/Warlord, Flesh → Chirurgeon | Warrens = Swine, Plague Eaters, Beastmen; Swine Prince → Meat Hook, Flesh → Harvest Child | the Warlord is DLC; the Chirurgeon is a 50 HP mini-boss; Meat Hook and the Harvest Child are full bosses that fit the two slots |
| Cove = Fisherfolk; Drowned Crew → Leviathan, Siren → Coven matriarch | Cove = Coastal + Courtiers; Siren → Leviathan, Drowned Crew → Archduke | the coven belongs to the forest; the courtiers' vanilla boss region is the coast |
| Vermin in the Warrens | Vermin (DLC) optional in the Ruins | the Catacombs are a crypt |
| wandering: Collector, Shambler, Death, Ancient Adversary | the same, plus Chirurgeon, Antiquarian, an Exemplar successor and the DLC Warlord | every boss-tagged actor has a home |
