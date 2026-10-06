using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Core
{
    /// <summary>One row of a dungeon level's <c>generated_quest_table</c>: a quest type at a length, with its weight.</summary>
    public sealed class QuestTemplate
    {
        public string Type;
        /// <summary>1 short, 2 medium, 3 long.</summary>
        public int Length;
        public double Chance;
        /// <summary>DLC rows only (<c>difficulty_override</c>); 0 = the roster decides.</summary>
        public int DifficultyOverride;

        public override string ToString() => Type + "/" + Length + (DifficultyOverride > 0 ? "@" + DifficultyOverride : "");
    }

    /// <summary>A region DD1 generates quests in, and the quests the estate must have finished before it does.</summary>
    public sealed class GeneratedDungeon
    {
        public string Id;
        public int QuestsFinished;
    }

    /// <summary>A quest difficulty and the resolve levels that call for it (<c>generated_resolve_level_difficulties</c>).</summary>
    public sealed class DifficultyBand
    {
        public int Difficulty;
        public int[] ResolveLevels = new int[0];
    }

    /// <summary>
    /// What DD1's quest generator reads, from the player's install:
    /// <c>campaign/quest/quest.generation.json</c> (which regions generate quests and from when, which
    /// difficulties the roster calls for, the table of quest types and lengths per region and region level),
    /// <c>campaign/quest/number.quest.generation.json</c> (how many quests a town visit offers) and
    /// <c>campaign/progression/progression.json</c> (what a finished quest adds to its region and the levels
    /// that adds up to). The initial values of the fields are FALLBACKS for a missing file or key.
    /// </summary>
    public sealed class QuestGenerationRules
    {
        public const string GenerationFile = "campaign/quest/quest.generation.json";
        public const string NumberFile = "campaign/quest/number.quest.generation.json";
        public const string ProgressionFile = "campaign/progression/progression.json";

        public List<GeneratedDungeon> Dungeons = new List<GeneratedDungeon>();
        /// <summary><c>generated_quests_max_threshold</c>: the most generated quests one region holds.</summary>
        public int MaxPerDungeon = 4;
        public List<DifficultyBand> Difficulties = new List<DifficultyBand>();
        /// <summary><c>number_of_quests_per_town_visit_table</c>.</summary>
        public int[] QuestsPerVisit = { 2 };
        /// <summary><c>quest_completion_xp_table</c>: points a finished quest gives its region, by length.</summary>
        public int[] DungeonXpByLength = { 0, 2, 3, 4 };
        /// <summary><c>level_threshold_table</c>: points a region needs for level 0, 1, 2...</summary>
        public int[] LevelThresholds = { 0, 2, 6, 10, 16, 22, 32, 42 };
        /// <summary>Files that could not be read: their rules are the fallbacks.</summary>
        public List<string> Missing = new List<string>();

        // dungeon -> level -> rows
        private readonly Dictionary<string, List<List<QuestTemplate>>> _tables = new Dictionary<string, List<List<QuestTemplate>>>();

        public static QuestGenerationRules Load(IDd1Files files)
        {
            var rules = new QuestGenerationRules();
            var generation = Json.ParseFile(files.ReadText(GenerationFile))?["generation"];
            if (generation != null) rules.ReadGeneration(generation);
            else rules.Missing.Add(GenerationFile);

            var number = Json.ParseFile(files.ReadText(NumberFile))?.SelectToken("generation.number.number_of_quests_per_town_visit_table");
            var counts = Json.Array(number).Select(t => Json.Int(t, 0)).ToArray();
            if (counts.Length > 0) rules.QuestsPerVisit = counts;
            else rules.Missing.Add(NumberFile);

            var progression = Json.ParseFile(files.ReadText(ProgressionFile))?["dungeon"];
            var xp = Json.Array(progression?["quest_completion_xp_table"]).Select(t => Json.Int(t, 0)).ToArray();
            var thresholds = Json.Array(progression?["level_threshold_table"]).Select(t => Json.Int(t, 0)).ToArray();
            if (xp.Length > 1) rules.DungeonXpByLength = xp;
            if (thresholds.Length > 1) rules.LevelThresholds = thresholds;
            if (xp.Length <= 1 || thresholds.Length <= 1) rules.Missing.Add(ProgressionFile);

            if (rules.Dungeons.Count == 0) rules.UseFallbackDungeons();
            if (rules.Difficulties.Count == 0) rules.Difficulties.Add(new DifficultyBand { Difficulty = 1, ResolveLevels = new[] { 0, 1, 2, 3, 4, 5, 6 } });
            return rules;
        }

        private void ReadGeneration(JToken generation)
        {
            var dungeon = generation["dungeon"];
            MaxPerDungeon = Math.Max(1, Json.Int(dungeon?["generated_quests_max_threshold"], MaxPerDungeon));
            foreach (var entry in Json.Array(dungeon?["generated_dungeons"]))
            {
                var id = (string)entry["id"];
                if (id != null) Dungeons.Add(new GeneratedDungeon { Id = id, QuestsFinished = Json.Int(entry["required_number_of_quests_finished"], 0) });
            }

            foreach (var entry in Json.Array(generation["difficulty"]?["generated_resolve_level_difficulties"]))
                Difficulties.Add(new DifficultyBand
                {
                    Difficulty = Json.Int(entry["difficulty"], 1),
                    ResolveLevels = Json.Array(entry["resolve_levels"]).Select(t => Json.Int(t, 0)).ToArray()
                });

            foreach (var entry in Json.Array(generation["type"]?["available_quests_table"]))
            {
                var id = (string)entry["dungeon"];
                if (id == null) continue;
                var levels = new List<List<QuestTemplate>>();
                foreach (var level in Json.Array(entry["generated_quest_table"]))
                {
                    var rows = new List<QuestTemplate>();
                    foreach (var row in Json.Array(level))
                    {
                        var type = (string)row["type"];
                        if (type == null) continue;
                        rows.Add(new QuestTemplate
                        {
                            Type = type,
                            Length = Math.Max(1, Json.Int(row["length"], 1)),
                            Chance = Json.Number(row["chance"], 1),
                            DifficultyOverride = Json.Int(row["difficulty_override"], 0)
                        });
                    }
                    levels.Add(rows);
                }
                _tables[id] = levels;
            }
        }

        // FALLBACK, not DD1 data: keeps the board from standing empty when quest.generation.json cannot be read.
        private void UseFallbackDungeons()
        {
            foreach (var id in new[] { "crypts", "weald", "warrens", "cove" })
            {
                Dungeons.Add(new GeneratedDungeon { Id = id, QuestsFinished = Dungeons.Count == 0 ? 1 : 3 });
                _tables[id] = new List<List<QuestTemplate>>
                {
                    new List<QuestTemplate>
                    {
                        new QuestTemplate { Type = QuestTypes.Explore, Length = 1, Chance = 1 },
                        new QuestTemplate { Type = QuestTypes.Cleanse, Length = 1, Chance = 1 }
                    }
                };
            }
        }

        /// <summary>
        /// The rows of a region at a level. A level above the table's last row reads the last one, a region
        /// without a table has none.
        /// </summary>
        public IReadOnlyList<QuestTemplate> TemplatesFor(string dungeon, int level)
        {
            if (!_tables.TryGetValue(dungeon ?? "", out var levels) || levels.Count == 0) return new QuestTemplate[0];
            return levels[Math.Max(0, Math.Min(level, levels.Count - 1))];
        }

        public bool Generates(string dungeon) => Dungeons.Any(d => d.Id == dungeon);

        /// <summary>The region's level for the points its finished quests added up to.</summary>
        public int DungeonLevel(int xp)
        {
            var level = 0;
            for (var i = 0; i < LevelThresholds.Length; i++)
                if (xp >= LevelThresholds[i]) level = i;
            return level;
        }

        public int MaxLevel => LevelThresholds.Length - 1;

        /// <summary>Points a finished quest of this length adds to its region; a length past the table reads its last entry.</summary>
        public int DungeonXp(int length)
        {
            if (DungeonXpByLength.Length == 0) return 0;
            return DungeonXpByLength[Math.Max(0, Math.Min(length, DungeonXpByLength.Length - 1))];
        }

        /// <summary>Quests a town visit offers; a visit past the table reads its last entry.</summary>
        public int QuestCount(int visit)
        {
            return QuestsPerVisit[Math.Max(0, Math.Min(visit, QuestsPerVisit.Length - 1))];
        }

        /// <summary>
        /// The difficulties the roster calls for and how strongly: one count per hero whose resolve level is in
        /// the difficulty's band (a level-2 hero counts for apprentice and veteran alike). A roster nobody is on
        /// calls for the lowest difficulty.
        /// </summary>
        public List<KeyValuePair<int, int>> DifficultyWeights(IEnumerable<int> resolveLevels)
        {
            var levels = (resolveLevels ?? new int[0]).ToList();
            var weights = new List<KeyValuePair<int, int>>();
            foreach (var band in Difficulties)
            {
                var heroes = levels.Count(level => band.ResolveLevels.Contains(level));
                if (heroes > 0) weights.Add(new KeyValuePair<int, int>(band.Difficulty, heroes));
            }
            if (weights.Count == 0 && Difficulties.Count > 0)
                weights.Add(new KeyValuePair<int, int>(Difficulties.Min(b => b.Difficulty), 1));
            return weights;
        }
    }
}
