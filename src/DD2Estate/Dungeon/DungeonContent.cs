using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Assets.Code.Actor;
using Assets.Code.Combat.BattleConfiguration;
using Assets.Code.DLC;
using Assets.Code.Game;
using Assets.Code.Library;
using Assets.Code.Math.Randomizer;
using Assets.Code.Utils;
using DD2Estate.Core;
using DD2Estate.Dd1;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Dungeon
{
    /// <summary>DD1 file access for the rules core, over the player's own DD1 install.</summary>
    internal class Dd1Files : IDd1Files
    {
        public bool Exists(string relativePath) => Dd1Install.Exists(relativePath);

        public string ReadText(string relativePath) => Dd1Install.ReadText(relativePath);

        public string[] List(string relativeDir, string pattern)
        {
            var dir = Dd1Install.PathOf(relativeDir);
            if (dir == null || !Directory.Exists(dir)) return new string[0];
            var names = Directory.GetFiles(dir, pattern).Select(Path.GetFileName).ToList();
            // the Darkest Dungeon's wall art sits in per-quest subfolders
            if (relativeDir.TrimEnd('/').EndsWith("dungeons/darkestdungeon", System.StringComparison.OrdinalIgnoreCase))
            {
                var quest = Path.Combine(dir, "quest_" + Dd1Install.DarkestQuestArt);
                if (Directory.Exists(quest)) names.AddRange(Directory.GetFiles(quest, pattern).Select(Path.GetFileName));
            }
            return names.Distinct().ToArray();
        }
    }

    /// <summary>
    /// What a tier adds to every enemy of a fight, as written in <c>tier_rules.&lt;tier&gt;.scaling.&lt;kind&gt;</c>:
    /// shares of the enemy's own value (0.5 = +50%) for health, damage and damage over time, points of speed,
    /// crit chance and resistances as shares (0.05 = +5 points). docs/recon/difficulty.md says where the numbers
    /// come from; <c>python tools/check_content.py --difficulty</c> derives them again from both games' files.
    /// </summary>
    internal struct TierScaling
    {
        public float Hp, Dmg, Dot, Speed, Crit, Resist;

        public bool IsNone => Hp == 0f && Dmg == 0f && Dot == 0f && Speed == 0f && Crit == 0f && Resist == 0f;
    }

    /// <summary>What a boss's tier block says beyond the fight itself. Ids may be null.</summary>
    internal class BossTier
    {
        /// <summary>BossModifier row the boss is ordained with (lair bosses).</summary>
        public string BossModifier;
        /// <summary>DataExternalBuffs set attached directly when no row fits the boss's tags (gang bosses).</summary>
        public string ExternalBuffs;
        /// <summary>BattleModifier forced for the fight (the gang's own escalation modifier).</summary>
        public string BattleModifier;
        /// <summary>Boss row (act) the fight was made for: brain, lungs, eyes, arms, body. Act bosses only.</summary>
        public string RunBoss;
        /// <summary>The boss, its parts and everything it summons.</summary>
        public HashSet<string> Actors = new HashSet<string>();
    }

    /// <summary>
    /// Which DD2 fights live in which DD1 dungeon (the embedded <c>Data/dungeons.json</c>): turns an abstract
    /// encounter slot of the generated map into a battle configuration id and an arena scene, and tells the
    /// difficulty layer what the tier of that dungeon asks for.
    /// </summary>
    internal static class DungeonContent
    {
        private static JObject _data;

        private static JObject Data
        {
            get
            {
                if (_data != null) return _data;
                using (var stream = typeof(DungeonContent).Assembly.GetManifestResourceStream("DD2Estate.Data.dungeons.json"))
                using (var reader = new StreamReader(stream))
                    _data = JObject.Parse(reader.ReadToEnd());
                return _data;
            }
        }

        public const string Darkest = "darkest";

        public static string TierName(int dd1Tier) => dd1Tier >= 6 ? Darkest : dd1Tier >= 5 ? "champion" : dd1Tier >= 3 ? "veteran" : "apprentice";

        /// <summary>The tier a fight of this DD1 difficulty is in, by the name the dungeon has it under.</summary>
        public static string TierName(string dungeonId, int dd1Tier) => ResolveTier(dungeonId, TierName(dd1Tier));

        /// <summary>
        /// A tier name the data knows for this dungeon: the given one when the dungeon has it, else the
        /// dungeon's own single tier (the Darkest Dungeon), else the given name if the rules know it.
        /// </summary>
        public static string ResolveTier(string dungeonId, string tierName)
        {
            var tiers = FindDungeon(dungeonId)?["tiers"] as JObject;
            if (tiers != null)
            {
                if (tierName != null && tiers[tierName] != null) return tierName;
                if (tiers[Darkest] != null) return Darkest;
            }
            return tierName != null && Data["tier_rules"]?[tierName] != null ? tierName : "apprentice";
        }

        // The Darkest Dungeon has a single tier of its own.
        private static JToken TierOf(JObject dungeon, int dd1Tier)
        {
            var tiers = dungeon["tiers"] as JObject;
            return tiers?[TierName(dd1Tier)] ?? tiers?[Darkest];
        }

        private static JObject FindDungeon(string id)
        {
            foreach (var d in (JArray)Data["dungeons"])
                if ((string)d["id"] == id) return (JObject)d;
            return null;
        }

        private static JObject Dungeon(string id)
        {
            return FindDungeon(id) ?? throw new ArgumentException("no dungeon content for '" + id + "'");
        }

        public static string DisplayName(string dungeonId) => (string)Dungeon(dungeonId)["name"] ?? dungeonId;

        public static IEnumerable<JToken> Dungeons() => (JArray)Data["dungeons"];

        /// <summary>
        /// DD1's <c>requires_quest_to_display</c> (dungeons/town/town.dungeon.json): the Estate Map shows the
        /// dungeon only while a quest is on offer there.
        /// </summary>
        public static bool NeedsQuest(string dungeonId) => (bool?)FindDungeon(dungeonId)?["requires_quest"] ?? false;

        /// <summary>The boss of a dungeon whose quest is one of DD1's own (<c>dd1_quest</c>: a town event's quest); null for none.</summary>
        public static string BossOfDd1Quest(string dungeonId, string dd1QuestId)
        {
            if (dd1QuestId == null) return null;
            foreach (var boss in FindDungeon(dungeonId)?["bosses"] as JArray ?? new JArray())
                if ((string)boss["dd1_quest"] == dd1QuestId) return (string)boss["id"];
            return null;
        }

        /// <summary>Whether the data knows the dungeon at all.</summary>
        public static bool Knows(string dungeonId) => FindDungeon(dungeonId) != null;

        /// <summary>
        /// The bosses of a dungeon that are the final boss of one of DD2's Confessions (<c>run_boss</c>: "brain",
        /// "lungs", ...), in the data's order: the Confession's id with the boss's own id in the data.
        /// </summary>
        public static List<KeyValuePair<string, string>> ActBosses(string dungeonId)
        {
            var bosses = new List<KeyValuePair<string, string>>();
            foreach (var boss in FindDungeon(dungeonId)?["bosses"] as JArray ?? new JArray())
            {
                string act = (string)boss["run_boss"], id = (string)boss["id"];
                if (!string.IsNullOrEmpty(act) && !string.IsNullOrEmpty(id)) bosses.Add(new KeyValuePair<string, string>(act, id));
            }
            return bosses;
        }

        /// <summary>The data's own name of a boss ("Shackles of Denial"); null when it has none.</summary>
        public static string BossName(string dungeonId, string bossId) => (string)FindBoss(dungeonId, bossId)?["name"];

        // ---- what a tier asks for ----------------------------------------------------------------------

        /// <summary>The tier's stat shares for a kind of fight. An unknown tier or kind scales nothing.</summary>
        public static TierScaling Scaling(string tierName, EncounterKind kind)
        {
            var key = kind == EncounterKind.Hallway ? "hallway" : kind == EncounterKind.Boss ? "boss" : "room";
            var block = Data["tier_rules"]?[tierName ?? ""]?["scaling"]?[key] as JObject;
            if (block == null) return default(TierScaling);
            return new TierScaling
            {
                Hp = Number(block["hp"]), Dmg = Number(block["dmg"]), Dot = Number(block["dot"]),
                Speed = Number(block["speed"]), Crit = Number(block["crit"]), Resist = Number(block["resist"])
            };
        }

        /// <summary>
        /// The Kingdoms escalation (1..3) the tier plays as: what the game's own conditions on that value
        /// (loot rarity, the gangs' skills, which Death shows up) are answered with during the fight.
        /// </summary>
        public static int Escalation(string tierName)
        {
            var value = (int?)Data["tier_rules"]?[tierName ?? ""]?["escalation"] ?? 1;
            return Math.Max(1, Math.Min(3, value));
        }

        /// <summary>The row regular monsters of this dungeon's tier are ordained with, and how often. Null row = never.</summary>
        public static void Ordain(string dungeonId, string tierName, out string bossModifierId, out float chance)
        {
            bossModifierId = null;
            chance = 0f;
            var ordain = (FindDungeon(dungeonId)?["tiers"] as JObject)?[tierName ?? ""]?["ordain"] as JObject;
            if (ordain == null) return;
            bossModifierId = (string)ordain["boss_modifier"];
            chance = Number(ordain["chance"]);
        }

        /// <summary>What the boss's tier block says. Bosses of the dungeon first, then the wanderers. Null = unknown boss.</summary>
        public static BossTier Boss(string dungeonId, string bossId, string tierName)
        {
            var boss = FindBoss(dungeonId, bossId);
            if (boss == null) return null;
            var info = new BossTier { RunBoss = (string)boss["run_boss"] };
            foreach (var key in new[] { "actor_ids", "escort_actor_ids" })
                foreach (var actor in boss[key] as JArray ?? new JArray())
                    info.Actors.Add((string)actor);
            var block = BossBlock(boss, dungeonId, tierName);
            if (block != null)
            {
                info.BossModifier = (string)block["boss_modifier"];
                info.ExternalBuffs = (string)block["external_buffs"];
                info.BattleModifier = (string)block["battle_modifier"];
            }
            return info;
        }

        private static JObject FindBoss(string dungeonId, string bossId)
        {
            if (bossId == null) return null;
            foreach (var boss in FindDungeon(dungeonId)?["bosses"] as JArray ?? new JArray())
                if ((string)boss["id"] == bossId) return (JObject)boss;
            foreach (var boss in Data["wandering"] as JArray ?? new JArray())
                if ((string)boss["id"] == bossId) return (JObject)boss;
            return null;
        }

        /// <summary>
        /// The wanderer that stands where a DD1 monster stood (<c>dd1_slot</c>: "collector", "shambler") and
        /// roams this dungeon: its id in the data, for <see cref="PickBoss"/> and <see cref="Boss"/>. Null when
        /// the data has none, or keeps it out of the dungeon.
        /// </summary>
        public static string Wanderer(string dd1Monster, string dungeonId)
        {
            if (string.IsNullOrEmpty(dd1Monster)) return null;
            foreach (var boss in Data["wandering"] as JArray ?? new JArray())
            {
                if ((string)boss["dd1_slot"] != dd1Monster) continue;
                foreach (var dungeon in boss["dungeons"] as JArray ?? new JArray())
                    if ((string)dungeon == dungeonId) return (string)boss["id"];
            }
            return null;
        }

        /// <summary>The DD1 monster a wanderer of the data stands for (<c>dd1_slot</c>); null when it stands for none.</summary>
        public static string WandererDd1(string id)
        {
            foreach (var boss in Data["wandering"] as JArray ?? new JArray())
                if ((string)boss["id"] == id) return (string)boss["dd1_slot"];
            return null;
        }

        /// <summary>
        /// A wanderer's fight at a tier, where the party stands: its own arena where the data gives it one
        /// (the Shambler's), else one of the dungeon's for that kind of place. Null config = the data has no
        /// fight for it, or this game build cannot play it.
        /// </summary>
        public static void PickWanderer(string dungeonId, string id, string tierName, EncounterKind kind, Rng rng, out string config, out string arena)
        {
            config = null;
            arena = null;
            var boss = FindBoss(dungeonId, id);
            var tier = boss != null ? BossBlock(boss, dungeonId, tierName) : null;
            if (tier == null) return;
            config = (string)tier["config"];
            var table = (string)tier["config_table"];
            if (config == null && table != null) config = RollTable(table);
            if (config != null && (!Exists(config, false) || !Playable(config))) config = null;
            if (config == null) return;
            arena = (string)tier["arena"];
            if (arena != null) return;
            var arenas = FindDungeon(dungeonId)?["arenas"]?[kind == EncounterKind.Hallway ? "hallway" : "room"] as JArray;
            arena = arenas != null && arenas.Count > 0 ? (string)arenas[rng.Next(arenas.Count)] : "combat_arena_forest_dungeon_interior";
        }

        /// <summary>A wanderer's name ("The Collector"); the id when the data has none.</summary>
        public static string WandererName(string id)
        {
            foreach (var boss in Data["wandering"] as JArray ?? new JArray())
                if ((string)boss["id"] == id) return (string)boss["name"] ?? id;
            return id;
        }

        /// <summary>
        /// The data's own copy of DD1's conditions for the wanderers of a dungeon at a tier (each entry's
        /// <c>trigger</c>), as lines of DD1's conditional mash: what is used when the install's file cannot be read.
        /// </summary>
        public static List<ConditionalMash> WandererFallback(string dungeonId, string tierName)
        {
            var lines = new List<ConditionalMash>();
            foreach (var boss in Data["wandering"] as JArray ?? new JArray())
            {
                var slot = (string)boss["dd1_slot"];
                var trigger = boss["trigger"] as JObject;
                if (slot == null || trigger == null || Wanderer(slot, dungeonId) == null) continue;
                var chance = trigger["chance"] as JObject;
                var line = new ConditionalMash { Chance = Exact(chance?[tierName ?? ""]), Limit = (int?)trigger["limit_per_quest"] ?? 0, CanBeAmbush = false };
                line.Types.Add(slot);
                switch ((string)trigger["type"])
                {
                    case "inventory_fill": line.BagShare = new[] { Exact(trigger["threshold"]), 1.0 }; break;
                    case "darkness_or_altar": line.Light = new[] { 0.0, Exact(trigger["torch_max"]) }; break;
                    default: continue;
                }
                lines.Add(line);
            }
            return lines;
        }

        // A boss of a dungeon is written per tier; a wanderer per tier or per dungeon.
        private static JObject BossBlock(JObject boss, string dungeonId, string tierName)
        {
            var tiers = boss["tiers"] as JObject;
            var block = tiers?[tierName ?? ""] ?? (boss["per_dungeon"] as JObject)?[dungeonId ?? ""];
            return (block ?? tiers?.Properties().FirstOrDefault()?.Value) as JObject;
        }

        private static float Number(JToken token)
        {
            return token != null && (token.Type == JTokenType.Float || token.Type == JTokenType.Integer) ? (float)token : 0f;
        }

        private static double Exact(JToken token)
        {
            return token != null && (token.Type == JTokenType.Float || token.Type == JTokenType.Integer) ? (double)token : 0.0;
        }

        // ---- what a won fight pays --------------------------------------------------------------------

        private static BattleLootRules _battleLoot;

        /// <summary>DD1's battle loot rule for DD2's monsters (the <c>battle_loot</c> block; Core/BattleLoot.cs).</summary>
        public static BattleLootRules BattleLoot => _battleLoot ?? (_battleLoot = BattleLootRules.FromJson(Data["battle_loot"]));

        /// <summary>
        /// The enemies a battle configuration starts with, each with the ranks DD2 gives its class and whether
        /// the fight is won without beating it (DD2's m_IsBattleComplete: cover, a barricade, a corpse). A
        /// monster this game build does not know counts as an ordinary one. Empty for an unknown configuration.
        /// </summary>
        public static List<BattleEnemy> Enemies(string configId)
        {
            var enemies = new List<BattleEnemy>();
            if (string.IsNullOrEmpty(configId)) return enemies;
            try
            {
                var configs = SingletonMonoBehaviour<Library<string, BattleConfigurationDefinition>>.Instance;
                var config = configs.GetHasLibraryKey(configId) ? configs.GetLibraryElement(configId) : null;
                if (config == null) return enemies;
                var classes = SingletonMonoBehaviour<Library<string, ActorDataClass>>.Instance;
                foreach (var actorId in config.m_EnemyActors)
                {
                    if (string.IsNullOrEmpty(actorId)) continue;
                    var cls = classes.GetHasLibraryKey(actorId) ? classes.GetLibraryElement(actorId) : null;
                    enemies.Add(new BattleEnemy { Id = actorId, Size = cls != null ? Math.Max(1, cls.m_Size) : 1, Bystander = cls != null && cls.m_IsBattleComplete });
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("dungeon content: the enemies of '" + configId + "' could not be read: " + e.Message); }
            return enemies;
        }

        /// <summary>
        /// The table codes a fight pays when it is won: by its kind, the enemies of its configuration and, for a
        /// quest's boss, what belongs to the boss.
        /// </summary>
        public static List<LootDraw> BattleLootDraws(string dungeonId, EncounterKind kind, string configId, string bossId, string tierName)
        {
            var own = kind == EncounterKind.Boss && bossId != null ? Boss(dungeonId, bossId, tierName)?.Actors : null;
            return BattleLoot.Draws(dungeonId, kind, Enemies(configId), own, kind == EncounterKind.Boss ? bossId : null);
        }

        // ---- fights ------------------------------------------------------------------------------------

        /// <summary>
        /// The boss fight of a story quest. Null config = the data has no usable fight. The boss stands
        /// alone at every tier, as in DD1: what a tier adds is stats and the boss's blessing, not a wave in
        /// front of it (the data's escort tables are kept for a later decision, docs/recon/difficulty.md 3.5).
        /// </summary>
        public static void PickBoss(string dungeonId, string bossId, string tierName, out string config, out string arena)
        {
            config = null;
            arena = null;
            var boss = FindBoss(dungeonId, bossId);
            var tier = boss != null ? BossBlock(boss, dungeonId, tierName) : null;
            if (tier == null) return;
            arena = (string)tier["arena"];
            config = (string)tier["config"];
            var table = (string)tier["config_table"];
            if (config == null && table != null) config = RollTable(table);
            if (config == null && tier["configs"] is JArray several && several.Count > 0)
                config = (string)several[UnityEngine.Random.Range(0, several.Count)];
            // A boss that names other fights to fall back on (the Incursion's Vvulf: the Warlord is a DLC's) takes
            // the first of them this game build can play when its own cannot be.
            if (tier["fallbacks"] is JArray fallbacks && (config == null || !Exists(config, false) || !Playable(config)))
            {
                var own = config;
                config = null;
                foreach (var fallback in fallbacks)
                {
                    var other = (string)fallback["config"];
                    if (other == null && (string)fallback["config_table"] != null) other = RollTable((string)fallback["config_table"]);
                    if (other == null || !Exists(other, false) || !Playable(other)) continue;
                    config = other;
                    arena = (string)fallback["arena"] ?? arena;
                    break;
                }
                Plugin.Log.LogInfo("dungeon content: the boss '" + bossId + "' cannot be fought as written (" + (own ?? "no fight") + "); standing in: " + (config ?? "nothing"));
            }
            if (arena == null)
            {
                var arenas = FindDungeon(dungeonId)?["arenas"]?["room"] as JArray;
                if (arenas != null && arenas.Count > 0) arena = (string)arenas[0];
            }
        }

        private class Entry
        {
            public string Id;
            public double Weight;
            public bool IsTable;
            /// <summary>Arenas of the block the entry came from; null = the dungeon's own.</summary>
            public JArray Arenas;
        }

        /// <summary>Picks a fight for a hallway or room slot. Null config = nothing usable was found.</summary>
        public static void Pick(string dungeonId, EncounterSlot slot, Rng rng, out string config, out string arena)
        {
            var dungeon = Dungeon(dungeonId);
            var kind = slot.Kind == EncounterKind.Hallway ? "hallway" : "room";
            var tierName = ResolveTier(dungeonId, TierName(slot.Tier));

            var entries = new List<Entry>();
            AddPool(entries, TierOf(dungeon, slot.Tier)?[kind] as JObject, null);
            // Factions a DLC adds to the dungeon join its pools when the DLC's tables are loaded.
            foreach (var extra in dungeon["dlc_extras"] as JArray ?? new JArray())
                AddPool(entries, (extra["tiers"] as JObject)?[tierName]?[kind] as JObject, extra["arenas"]?[kind] as JArray ?? new JArray());

            var picked = Roll(entries, rng, out config);
            var arenas = picked?.Arenas != null && picked.Arenas.Count > 0 ? picked.Arenas : dungeon["arenas"]?[kind] as JArray;
            arena = arenas != null && arenas.Count > 0 ? (string)arenas[rng.Next(arenas.Count)] : "combat_arena_forest_dungeon_interior";
        }

        // Ids in "tables" are battle configuration tables (rolled the vanilla way); every other id is a
        // configuration. Weights cover both; an entry without one counts 1.
        private static void AddPool(List<Entry> entries, JObject pool, JArray arenas)
        {
            if (pool == null) return;
            var weights = pool["weights"] as JObject;
            foreach (var key in new[] { "configs", "tables" })
            {
                foreach (var token in pool[key] as JArray ?? new JArray())
                {
                    var id = (string)token;
                    var isTable = key == "tables";
                    // A DLC block whose data is not loaded (DLC not owned) adds nothing, without noise.
                    if (arenas != null && !Exists(id, isTable)) continue;
                    var weight = weights?[id];
                    entries.Add(new Entry { Id = id, IsTable = isTable, Arenas = arenas, Weight = weight != null ? (double)weight : 1.0 });
                }
            }
        }

        private static bool Exists(string id, bool isTable)
        {
            return isTable
                ? SingletonMonoBehaviour<Library<string, Table<BattleConfigurationOption, string>>>.Instance.GetHasLibraryKey(id)
                : SingletonMonoBehaviour<Library<string, BattleConfigurationDefinition>>.Instance.GetHasLibraryKey(id);
        }

        private static string RollTable(string tableId)
        {
            if (!Exists(tableId, true)) return null;
            var rolled = new List<string>();
            return LibraryBattleConfigurationTables.RollBattleConfiguration(tableId, RandomIdentifier.COMBAT, rolled) && rolled.Count > 0 ? rolled[0] : null;
        }

        // A fight is playable when the game knows every enemy in it and owns the DLC each one came with.
        private static bool Playable(string configId)
        {
            var config = SingletonMonoBehaviour<Library<string, BattleConfigurationDefinition>>.Instance.GetLibraryElement(configId);
            if (config == null) return false;
            var unowned = DLCManager.Instance != null ? DLCManager.Instance.GetUnownedDLCNumbers() : new List<int>();
            var classes = SingletonMonoBehaviour<Library<string, ActorDataClass>>.Instance;
            foreach (var actorId in config.m_EnemyActors)
            {
                if (!classes.GetHasLibraryKey(actorId)) return false;
                if (unowned.Contains(classes.GetLibraryElement(actorId).DLCNumber)) return false;
            }
            return true;
        }

        // An id this game build does not know, or a table that rolls nothing, is skipped and another entry is drawn.
        private static Entry Roll(List<Entry> entries, Rng rng, out string config)
        {
            config = null;
            for (var attempt = 0; attempt < 12 && entries.Count > 0; attempt++)
            {
                var total = entries.Sum(e => e.Weight);
                var roll = rng.NextDouble() * total;
                var index = 0;
                for (; index < entries.Count - 1; index++)
                {
                    roll -= entries[index].Weight;
                    if (roll < 0) break;
                }
                var entry = entries[index];
                entries.RemoveAt(index);

                config = entry.IsTable ? RollTable(entry.Id) : Exists(entry.Id, false) ? entry.Id : null;
                if (config != null && !Playable(config)) config = null;
                if (config != null) return entry;
                Plugin.Log.LogWarning("dungeon content: '" + entry.Id + "' is not usable in this game build");
            }
            return null;
        }
    }
}
