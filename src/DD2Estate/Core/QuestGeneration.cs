using System;
using System.Collections.Generic;
using System.Linq;

namespace DD2Estate.Core
{
    /// <summary>A generated quest on the board, in DD1 terms.</summary>
    public sealed class QuestOffer
    {
        public string Dungeon, Type;
        /// <summary>1 short, 2 medium, 3 long.</summary>
        public int Length;
        /// <summary>DD1 difficulty: 1 apprentice, 3 veteran, 5 champion.</summary>
        public int Difficulty;
        /// <summary>The region's level when the quest was rolled.</summary>
        public int DungeonLevel;
        public QuestReward Reward;

        public override string ToString() => Dungeon + " " + Type + " length " + Length + " difficulty " + Difficulty;
    }

    /// <summary>Where the estate stands when a week's quests are rolled.</summary>
    public sealed class QuestBoardInput
    {
        /// <summary>Quests finished on the whole estate (DD1 counts its tutorial as the first).</summary>
        public int QuestsFinished;
        /// <summary>Points towards its level each region has (see <see cref="QuestGenerationRules.DungeonXp"/>).</summary>
        public IDictionary<string, int> DungeonXp = new Dictionary<string, int>();
        /// <summary>The resolve level of every hero on the roster.</summary>
        public IList<int> ResolveLevels = new List<int>();
        /// <summary>Quest types the dungeon generator can build; null = any.</summary>
        public ICollection<string> SupportedTypes;
        /// <summary>Regions the game layer has content for; null = any.</summary>
        public ICollection<string> KnownDungeons;
    }

    /// <summary>A week's generated quests and what the roll had to leave out.</summary>
    public sealed class QuestRoll
    {
        public readonly List<QuestOffer> Offers = new List<QuestOffer>();
        /// <summary>The number DD1's table asks for; fewer are offered when the open regions cannot hold them.</summary>
        public int Wanted;
        public readonly List<string> OpenDungeons = new List<string>();
        /// <summary>
        /// Rows of DD1's tables the roll could not use, one line each: a quest type the dungeon generator
        /// cannot build, a region the game layer has no content for. Never dropped without a line here.
        /// </summary>
        public readonly List<string> Unsupported = new List<string>();
    }

    /// <summary>
    /// DD1's weekly quest generation. The tables are DD1's (<see cref="QuestGenerationRules"/>,
    /// <see cref="QuestRewardRules"/>); how DD1 walks them is in its code, not its data, so these steps are
    /// the mod's reading of it and are marked where they are a choice:
    ///
    /// 1. A region generates quests once the estate has finished its <c>required_number_of_quests_finished</c>.
    /// 2. The week offers <c>number_of_quests_per_town_visit_table[n]</c> quests. READING: n is the number of
    ///    quests finished, less one. DD1's first town visit comes after its tutorial quest (one quest finished)
    ///    and offers the table's first entry; as long as no quest fails, quests finished and town visits rise
    ///    together.
    /// 3. READING: every open region gets one quest first, the rest go to regions drawn at random, and no
    ///    region holds more than <c>generated_quests_max_threshold</c> (the estate map draws four to a row).
    /// 4. A quest's type and length are drawn from the region's <c>generated_quest_table</c> row for its
    ///    level, by <c>chance</c>. Its difficulty is one of those the roster's resolve levels call for
    ///    (<c>generated_resolve_level_difficulties</c>). READING: a difficulty is drawn in proportion to the
    ///    heroes in its band, and a region does not get the same type, length and difficulty twice in a week
    ///    while another combination is left.
    /// 5. The reward follows the tables for the region, difficulty and length (<see cref="QuestRewardRules.Generated"/>).
    /// </summary>
    public static class QuestGeneration
    {
        /// <summary>Row of the quests-per-visit table for an estate that has finished this many quests.</summary>
        public static int VisitIndex(int questsFinished) => Math.Max(0, questsFinished - 1);

        /// <summary>Regions that generate quests for an estate that has finished this many, in DD1's order.</summary>
        public static List<string> OpenDungeons(QuestGenerationRules rules, int questsFinished)
        {
            return rules.Dungeons.Where(d => questsFinished >= d.QuestsFinished).Select(d => d.Id).ToList();
        }

        public static QuestRoll Roll(QuestGenerationRules rules, QuestRewardRules rewards, QuestBoardInput input, Rng rng)
        {
            var roll = new QuestRoll();
            foreach (var id in OpenDungeons(rules, input.QuestsFinished))
            {
                if (input.KnownDungeons != null && !input.KnownDungeons.Contains(id))
                {
                    roll.Unsupported.Add(id + ": DD1 generates quests here, the mod has no such region");
                    continue;
                }
                roll.OpenDungeons.Add(id);
            }

            // the rows each region can be given this week
            var usable = new Dictionary<string, List<QuestTemplate>>();
            var levels = new Dictionary<string, int>();
            foreach (var id in roll.OpenDungeons.ToList())
            {
                input.DungeonXp.TryGetValue(id, out var xp);
                var level = levels[id] = rules.DungeonLevel(xp);
                var rows = new List<QuestTemplate>();
                foreach (var row in rules.TemplatesFor(id, level))
                {
                    if (row.Chance <= 0) continue;
                    if (input.SupportedTypes != null && !input.SupportedTypes.Contains(row.Type))
                    {
                        var line = id + " level " + level + ": quest type '" + row.Type + "' is not built yet";
                        if (!roll.Unsupported.Contains(line)) roll.Unsupported.Add(line);
                        continue;
                    }
                    rows.Add(row);
                }
                if (rows.Count == 0)
                {
                    roll.Unsupported.Add(id + " level " + level + ": no quest type left to offer");
                    roll.OpenDungeons.Remove(id);
                    continue;
                }
                usable[id] = rows;
            }

            roll.Wanted = rules.QuestCount(VisitIndex(input.QuestsFinished));
            var counts = Distribute(roll.OpenDungeons, roll.Wanted, rules.MaxPerDungeon, rng);
            var difficulties = rules.DifficultyWeights(input.ResolveLevels);

            foreach (var id in roll.OpenDungeons)
            {
                var used = new HashSet<string>();
                var here = new List<QuestOffer>();
                for (var i = 0; i < counts[id]; i++)
                {
                    var offer = Pick(id, usable[id], difficulties, used, rng);
                    if (offer == null) break;
                    offer.DungeonLevel = levels[id];
                    here.Add(offer);
                }
                // easiest and shortest first: the order the markers stand in on the map
                foreach (var offer in here.OrderBy(o => o.Difficulty).ThenBy(o => o.Length).ThenBy(o => o.Type, StringComparer.Ordinal))
                {
                    offer.Reward = rewards.Generated(id, offer.Difficulty, offer.Length, rng);
                    roll.Offers.Add(offer);
                }
            }
            return roll;
        }

        /// <summary>How many quests each open region gets (step 3 of the class text).</summary>
        public static Dictionary<string, int> Distribute(IList<string> dungeons, int wanted, int maxPerDungeon, Rng rng)
        {
            var counts = dungeons.ToDictionary(id => id, id => 0);
            var left = Math.Min(wanted, dungeons.Count * maxPerDungeon);
            // fewer quests than regions: which regions go without is a draw as well
            var order = dungeons.ToList();
            if (left < order.Count) rng.Shuffle(order);
            foreach (var id in order)
            {
                if (left <= 0) break;
                counts[id]++;
                left--;
            }
            while (left > 0)
            {
                var room = dungeons.Where(id => counts[id] < maxPerDungeon).ToList();
                if (room.Count == 0) break;
                counts[room[rng.Next(room.Count)]]++;
                left--;
            }
            return counts;
        }

        private static QuestOffer Pick(string dungeon, List<QuestTemplate> rows, List<KeyValuePair<int, int>> difficulties, HashSet<string> used, Rng rng)
        {
            var candidates = new List<QuestOffer>();
            var weights = new List<double>();
            foreach (var row in rows)
            {
                // a DLC row may fix its own difficulty; it then stands once, at its own chance
                var bands = row.DifficultyOverride > 0
                    ? new List<KeyValuePair<int, int>> { new KeyValuePair<int, int>(row.DifficultyOverride, difficulties.Sum(d => d.Value)) }
                    : difficulties;
                foreach (var band in bands)
                {
                    candidates.Add(new QuestOffer { Dungeon = dungeon, Type = row.Type, Length = row.Length, Difficulty = band.Key });
                    weights.Add(row.Chance * band.Value);
                }
            }
            if (candidates.Count == 0) return null;

            var fresh = weights.Select((weight, i) => used.Contains(Key(candidates[i])) ? 0 : weight).ToList();
            var index = rng.PickWeighted(fresh);
            if (index < 0) index = rng.PickWeighted(weights);    // every combination is on the board already
            if (index < 0) return null;
            used.Add(Key(candidates[index]));
            return candidates[index];
        }

        private static string Key(QuestOffer offer) => offer.Type + "/" + offer.Length + "/" + offer.Difficulty;
    }
}
