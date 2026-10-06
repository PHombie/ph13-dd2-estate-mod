using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DD2Estate.Core;
using Newtonsoft.Json.Linq;

namespace DD2Estate.CoreTests
{
    /// <summary>
    /// The quest board's rules against the DD1 install: every table the core reads is compared with the same
    /// file read here with nothing but Newtonsoft, and the generator is checked against those tables.
    /// </summary>
    public static class QuestGenerationTests
    {
        private static QuestGenerationRules _rules;
        private static QuestRewardRules _rewards;
        private static TrinketRules _trinkets;

        private static QuestGenerationRules Rules => _rules ?? (_rules = QuestGenerationRules.Load(Dd1.Files));
        private static QuestRewardRules Rewards => _rewards ?? (_rewards = QuestRewardRules.Load(Dd1.Files));
        private static TrinketRules Trinkets => _trinkets ?? (_trinkets = TrinketRules.Load(Dd1.Files));

        private static JObject Raw(string relative)
        {
            if (!Dd1.Available) throw new SkipException("needs the DD1 install");
            return JObject.Parse(File.ReadAllText(Path.Combine(Dd1.Root, relative.Replace('/', Path.DirectorySeparatorChar))).TrimStart((char)0xFEFF));
        }

        private static QuestBoardInput Estate(int finished, params int[] resolveLevels)
        {
            return new QuestBoardInput
            {
                QuestsFinished = finished,
                ResolveLevels = resolveLevels.Length > 0 ? resolveLevels : new[] { 0, 0, 0, 0 },
                SupportedTypes = QuestTypes.All
            };
        }

        public static void GenerationRulesMatchDd1Files()
        {
            var raw = Raw(QuestGenerationRules.GenerationFile)["generation"];
            Check.True(Rules.Missing.Count == 0, "files not read: " + string.Join(", ", Rules.Missing));

            var dungeons = (JArray)raw["dungeon"]["generated_dungeons"];
            Check.Equal(dungeons.Count, Rules.Dungeons.Count, "generated dungeons");
            for (var i = 0; i < dungeons.Count; i++)
            {
                Check.Equal((string)dungeons[i]["id"], Rules.Dungeons[i].Id, "dungeon id");
                Check.Equal((int)dungeons[i]["required_number_of_quests_finished"], Rules.Dungeons[i].QuestsFinished, "quests finished for " + Rules.Dungeons[i].Id);
            }
            Check.Equal((int)raw["dungeon"]["generated_quests_max_threshold"], Rules.MaxPerDungeon, "most quests in a region");

            var bands = (JArray)raw["difficulty"]["generated_resolve_level_difficulties"];
            Check.Equal(bands.Count, Rules.Difficulties.Count, "difficulty bands");
            for (var i = 0; i < bands.Count; i++)
            {
                Check.Equal((int)bands[i]["difficulty"], Rules.Difficulties[i].Difficulty, "difficulty");
                Check.True(bands[i]["resolve_levels"].Select(t => (int)t).SequenceEqual(Rules.Difficulties[i].ResolveLevels), "resolve levels of difficulty " + Rules.Difficulties[i].Difficulty);
            }

            var rows = 0;
            foreach (var table in raw["type"]["available_quests_table"])
            {
                var id = (string)table["dungeon"];
                var levels = (JArray)table["generated_quest_table"];
                for (var level = 0; level < levels.Count; level++)
                {
                    var read = Rules.TemplatesFor(id, level);
                    Check.Equal(((JArray)levels[level]).Count, read.Count, id + " level " + level + " rows");
                    for (var i = 0; i < read.Count; i++)
                    {
                        Check.Equal((string)levels[level][i]["type"], read[i].Type, "type");
                        Check.Equal((int)levels[level][i]["length"], read[i].Length, "length");
                        Check.Near((double)levels[level][i]["chance"], read[i].Chance, 1e-9, "chance");
                        rows++;
                    }
                }
                // a level past the table reads its last row
                Check.Equal(Rules.TemplatesFor(id, levels.Count - 1).Count, Rules.TemplatesFor(id, 99).Count, "level past the table");
            }
            Check.True(rows > 0, "no quest rows read");
            Check.Equal(0, Rules.TemplatesFor("no_such_place", 0).Count, "unknown region");

            var numbers = Raw(QuestGenerationRules.NumberFile).SelectToken("generation.number.number_of_quests_per_town_visit_table").Select(t => (int)t).ToArray();
            Check.True(numbers.SequenceEqual(Rules.QuestsPerVisit), "quests per town visit");
            Check.Equal(numbers[0], Rules.QuestCount(0), "first visit");
            Check.Equal(numbers[numbers.Length - 1], Rules.QuestCount(500), "a visit past the table");
            Check.Equal(numbers[0], Rules.QuestCount(QuestGeneration.VisitIndex(1)), "DD1's first town visit has one quest behind it");
        }

        public static void DungeonLevelsCountPointsByLength()
        {
            var raw = Raw(QuestGenerationRules.ProgressionFile)["dungeon"];
            var xp = raw["quest_completion_xp_table"].Select(t => (int)t).ToArray();
            var thresholds = raw["level_threshold_table"].Select(t => (int)t).ToArray();
            for (var length = 0; length < xp.Length; length++) Check.Equal(xp[length], Rules.DungeonXp(length), "points of length " + length);
            Check.True(Rules.DungeonXp(1) < Rules.DungeonXp(2) && Rules.DungeonXp(2) < Rules.DungeonXp(3), "a longer quest counts for more");
            Check.Equal(thresholds.Length - 1, Rules.MaxLevel, "top level");
            for (var level = 0; level < thresholds.Length; level++)
            {
                Check.Equal(level, Rules.DungeonLevel(thresholds[level]), "level at its threshold");
                if (level > 0) Check.Equal(level - 1, Rules.DungeonLevel(thresholds[level] - 1), "level one point short");
            }
            Check.Equal(Rules.MaxLevel, Rules.DungeonLevel(100000), "points past the table");

            // DD1's tutorial is a short quest: it alone must bring the Ruins to level 1, as in DD1's first week.
            Check.Equal(1, Rules.DungeonLevel(Rules.DungeonXp(1)), "one short quest");
        }

        public static void RewardTablesMatchDd1Files()
        {
            var raw = Raw(QuestGenerationRules.GenerationFile)["generation"]["rewards"];
            Check.True(!Rewards.IsFallback, "rewards not read");
            var gold = (JArray)raw["item_table"];
            var cells = 0;
            for (var difficulty = 0; difficulty < gold.Count; difficulty++)
                for (var length = 0; length < ((JArray)gold[difficulty]).Count; length++)
                {
                    var expected = gold[difficulty][length].Where(i => (string)i["type"] == "gold").Sum(i => (int)i["amount"]);
                    Check.Equal(expected, Rewards.Gold(difficulty, length), "gold at difficulty " + difficulty + " length " + length);
                    if (expected > 0) cells++;
                }
            Check.True(cells >= 9, "gold table too small");
            Check.Equal(0, Rewards.Gold(99, 1), "difficulty past the table");
            Check.True(Rewards.Gold(1, 1) < Rewards.Gold(1, 2) && Rewards.Gold(1, 2) < Rewards.Gold(1, 3), "gold grows with length");
            Check.True(Rewards.Gold(1, 1) < Rewards.Gold(3, 1) && Rewards.Gold(3, 1) < Rewards.Gold(5, 1), "gold grows with difficulty");

            foreach (var map in raw["heirloom_type_map"])
                Check.True(map["types"].Select(t => (string)t).SequenceEqual(Rewards.HeirloomTypes((string)map["dungeon"])), "heirloom types of " + map["dungeon"]);
            foreach (var table in raw["heirloom_amount_table"])
            {
                var amounts = (JArray)table["amounts"];
                for (var difficulty = 0; difficulty < amounts.Count; difficulty++)
                    for (var length = 0; length < ((JArray)amounts[difficulty]).Count; length++)
                        Check.Equal((int)amounts[difficulty][length], Rewards.HeirloomAmount((string)table["type"], difficulty, length), table["type"] + " at " + difficulty + "/" + length);
            }
            Check.Equal(0, Rewards.HeirloomAmount("bust", 6, 1), "no heirloom row for the Darkest Dungeon");

            foreach (var table in raw["trinket_chance_table"])
            {
                var chances = (JArray)table["chances"];
                for (var difficulty = 0; difficulty < chances.Count; difficulty++)
                    for (var length = 0; length < ((JArray)chances[difficulty]).Count; length++)
                    {
                        var read = Rewards.TrinketChances(difficulty, length).Where(c => c.Key == (string)table["rarity"]).Sum(c => c.Value);
                        Check.Near((double)chances[difficulty][length], read, 1e-9, table["rarity"] + " at " + difficulty + "/" + length);
                    }
            }
        }

        public static void GeneratedRewardFollowsTheTables()
        {
            var rng = new Rng(11);
            foreach (var dungeon in Rules.Dungeons.Select(d => d.Id))
                foreach (var difficulty in Rules.Difficulties.Select(b => b.Difficulty))
                    for (var length = 1; length <= 3; length++)
                    {
                        var seen = new HashSet<string>();
                        var payable = Rewards.HeirloomTypes(dungeon).Where(t => Rewards.HeirloomAmount(t, difficulty, length) > 0).ToList();
                        for (var i = 0; i < 300; i++)
                        {
                            var reward = Rewards.Generated(dungeon, difficulty, length, rng);
                            Check.Equal(Rewards.Gold(difficulty, length), reward.Gold, "gold");
                            Check.Equal(Math.Min(QuestRewardRules.HeirloomTypesPerQuest, payable.Count), reward.Heirlooms.Count, "heirloom types in one reward");
                            foreach (var heirloom in reward.Heirlooms)
                            {
                                Check.True(payable.Contains(heirloom.Key), heirloom.Key + " is not paid in " + dungeon);
                                Check.Equal(Rewards.HeirloomAmount(heirloom.Key, difficulty, length), heirloom.Value, "amount of " + heirloom.Key);
                                seen.Add(heirloom.Key);
                            }
                            var chances = Rewards.TrinketChances(difficulty, length);
                            Check.Equal(chances.Count > 0 ? 1 : 0, reward.TrinketRarities.Count, "trinkets in one reward");
                            foreach (var rarity in reward.TrinketRarities) Check.True(chances.Any(c => c.Key == rarity), "rarity " + rarity + " has no chance here");
                            Check.True(reward.DungeonXp && reward.NamedTrinkets.Count == 0, "a generated quest counts for its region and names no trinket");
                        }
                        Check.Equal(payable.Count, seen.Count, "every heirloom type of " + dungeon + " turns up");
                    }

            // the trinket gets better with length and difficulty, never worse
            foreach (var difficulty in Rules.Difficulties.Select(b => b.Difficulty))
            {
                var ranks = Enumerable.Range(1, 3).Select(length => Rewards.TrinketChances(difficulty, length).Select(c => TrinketRules.Rank(c.Key)).DefaultIfEmpty(-1).Max()).ToList();
                Check.True(ranks[0] <= ranks[1] && ranks[1] <= ranks[2], "trinket rarity by length at difficulty " + difficulty);
            }
        }

        public static void WeekOffersTheNumberDd1Asks()
        {
            var rng = new Rng(5);
            var empty = QuestGeneration.Roll(Rules, Rewards, Estate(0), rng);
            var first = Rules.Dungeons.Min(d => d.QuestsFinished);
            if (first > 0) Check.Equal(0, empty.Offers.Count, "nothing is generated before the first quest is done");

            for (var finished = 1; finished <= 14; finished++)
                for (var attempt = 0; attempt < 40; attempt++)
                {
                    var input = Estate(finished, 0, 1, 2, 3, 5);
                    foreach (var dungeon in Rules.Dungeons) input.DungeonXp[dungeon.Id] = rng.Next(45);
                    var roll = QuestGeneration.Roll(Rules, Rewards, input, rng);
                    var open = Rules.Dungeons.Where(d => finished >= d.QuestsFinished).Select(d => d.Id).ToList();
                    Check.True(open.SequenceEqual(roll.OpenDungeons), "open regions at " + finished + " quests finished");
                    Check.Equal(Rules.QuestCount(finished - 1), roll.Wanted, "quests asked for");
                    Check.Equal(Math.Min(roll.Wanted, open.Count * Rules.MaxPerDungeon), roll.Offers.Count, "quests offered at " + finished + " finished");
                    Check.Equal(0, roll.Unsupported.Count, "stock DD1 has nothing the generator cannot build: " + string.Join("; ", roll.Unsupported));

                    foreach (var id in open)
                    {
                        var here = roll.Offers.Where(o => o.Dungeon == id).ToList();
                        Check.True(here.Count <= Rules.MaxPerDungeon, "too many quests in " + id);
                        if (roll.Offers.Count >= open.Count) Check.True(here.Count >= 1, id + " has no quest");
                        var level = Rules.DungeonLevel(input.DungeonXp[id]);
                        var rows = Rules.TemplatesFor(id, level);
                        foreach (var offer in here)
                        {
                            Check.Equal(level, offer.DungeonLevel, "level the quest was rolled at");
                            Check.True(rows.Any(r => r.Type == offer.Type && r.Length == offer.Length), offer + " is not in DD1's row for level " + level);
                            Check.True(offer.Difficulty == 1 || offer.Difficulty == 3 || offer.Difficulty == 5, "difficulty " + offer.Difficulty);
                            Check.Equal(Rewards.Gold(offer.Difficulty, offer.Length), offer.Reward.Gold, "reward gold");
                        }
                        // no combination twice while the row has others to give
                        var combinations = rows.Select(r => r.Type + r.Length).Distinct().Count() * 3;
                        if (here.Count <= combinations)
                            Check.Equal(here.Count, here.Select(o => o.Type + o.Length + "/" + o.Difficulty).Distinct().Count(), "the same quest twice in " + id);
                    }
                }
        }

        public static void DifficultyFollowsTheRoster()
        {
            var weights = Rules.DifficultyWeights(new[] { 0, 0, 0, 3 });
            foreach (var band in Rules.Difficulties)
            {
                var heroes = new[] { 0, 0, 0, 3 }.Count(level => band.ResolveLevels.Contains(level));
                Check.Equal(heroes, weights.Where(w => w.Key == band.Difficulty).Sum(w => w.Value), "heroes calling for difficulty " + band.Difficulty);
            }
            Check.Equal(1, Rules.DifficultyWeights(new int[0]).Count, "an empty roster still gets quests");
            Check.Equal(Rules.Difficulties.Min(b => b.Difficulty), Rules.DifficultyWeights(new int[0])[0].Key, "an empty roster gets the lowest difficulty");

            var rng = new Rng(21);
            int low = 0, mid = 0, high = 0, total = 0;
            for (var i = 0; i < 400; i++)
            {
                var input = Estate(12, 0, 0, 0, 3);
                foreach (var dungeon in Rules.Dungeons) input.DungeonXp[dungeon.Id] = 16;
                foreach (var offer in QuestGeneration.Roll(Rules, Rewards, input, rng).Offers)
                {
                    total++;
                    if (offer.Difficulty == 1) low++;
                    else if (offer.Difficulty == 3) mid++;
                    else high++;
                }
            }
            Check.Equal(0, high, "nobody on the roster calls for champion quests");
            Check.True(low > mid && mid > 0, "three novices and one veteran: mostly apprentice quests, some veteran (" + low + "/" + mid + ")");
            Check.Near(0.25, mid / (double)total, 0.06, "share of veteran quests");

            foreach (var offer in QuestGeneration.Roll(Rules, Rewards, Estate(12, 6, 6, 5), rng).Offers)
                Check.Equal(Rules.Difficulties.Max(b => b.Difficulty), offer.Difficulty, "a roster of champions");
        }

        public static void QuestsAreDealtToEveryOpenRegion()
        {
            var rng = new Rng(3);
            var regions = new List<string> { "a", "b", "c", "d" };
            for (var wanted = 0; wanted <= 20; wanted++)
                for (var attempt = 0; attempt < 50; attempt++)
                {
                    var counts = QuestGeneration.Distribute(regions, wanted, 4, rng);
                    Check.Equal(Math.Min(wanted, 16), counts.Values.Sum(), "quests dealt");
                    Check.True(counts.Values.All(n => n <= 4), "a region over its limit");
                    if (wanted >= regions.Count) Check.True(counts.Values.All(n => n >= 1), "a region left out");
                    else Check.True(counts.Values.All(n => n <= 1), "a second quest before every region has one");
                }
            Check.Equal(0, QuestGeneration.Distribute(new List<string>(), 5, 4, rng).Count, "no region, no quest");
        }

        public static void WhatCannotBeBuiltIsReported()
        {
            var rng = new Rng(9);
            var input = Estate(12);
            foreach (var dungeon in Rules.Dungeons) input.DungeonXp[dungeon.Id] = 16;
            input.SupportedTypes = new[] { QuestTypes.Explore };
            input.KnownDungeons = Rules.Dungeons.Select(d => d.Id).Where(id => id != "cove").ToList();
            var roll = QuestGeneration.Roll(Rules, Rewards, input, rng);
            Check.True(roll.Offers.Count > 0 && roll.Offers.All(o => o.Type == QuestTypes.Explore), "only what can be built is offered");
            Check.True(roll.Offers.All(o => o.Dungeon != "cove") && !roll.OpenDungeons.Contains("cove"), "a region without content is left out");
            Check.True(roll.Unsupported.Any(line => line.StartsWith("cove:", StringComparison.Ordinal)), "the missing region is named");
            foreach (var type in new[] { QuestTypes.Cleanse, QuestTypes.Gather, QuestTypes.InventoryActivate })
                Check.True(roll.Unsupported.Any(line => line.Contains("'" + type + "'")), type + " is not reported");
            Check.Equal(roll.Unsupported.Count, roll.Unsupported.Distinct().Count(), "a row is reported once");

            // a region whose level row has nothing left is named and gets no quest
            var novice = Estate(12);
            novice.SupportedTypes = new[] { QuestTypes.Explore };
            var bare = QuestGeneration.Roll(Rules, Rewards, novice, rng);
            foreach (var dungeon in Rules.Dungeons)
                if (Rules.TemplatesFor(dungeon.Id, 0).All(r => r.Type != QuestTypes.Explore))
                {
                    Check.True(bare.Offers.All(o => o.Dungeon != dungeon.Id), "a quest from an empty row");
                    Check.True(bare.Unsupported.Any(line => line.StartsWith(dungeon.Id + " level 0: no quest type", StringComparison.Ordinal)), dungeon.Id + " is not named");
                }
        }

        public static void RollIsStableForASeed()
        {
            string Describe(long seed)
            {
                var input = Estate(9, 0, 2, 3, 4, 6);
                foreach (var dungeon in Rules.Dungeons) input.DungeonXp[dungeon.Id] = 10;
                return string.Join("|", QuestGeneration.Roll(Rules, Rewards, input, new Rng(seed)).Offers
                    .Select(o => o + " " + o.Reward.Gold + " " + string.Join(",", o.Reward.Heirlooms.Select(h => h.Key + h.Value)) + " " + string.Join(",", o.Reward.TrinketRarities)));
            }
            Check.Equal(Describe(77), Describe(77), "same seed, same week");
            Check.True(Describe(77) != Describe(78), "another seed, another week");
        }

        public static void PlotQuestRewardsAreDd1s()
        {
            var raw = (JArray)Raw(QuestRewardRules.PlotFile)["plot_quests"];
            Check.Equal(raw.Count, Rewards.PlotQuests.Count, "plot quests");
            foreach (var entry in raw)
            {
                var plot = Rewards.Plot((string)entry["id"]);
                Check.True(plot != null, entry["id"] + " not read");
                Check.Equal((string)entry["quest"]["dungeon"], plot.Dungeon, "dungeon");
                Check.Equal((int)entry["quest"]["difficulty"], plot.Difficulty, "difficulty");
                Check.Equal((int)entry["quest"]["length"], plot.Length, "length");
                Check.Equal((int)entry["dungeon_level"], plot.DungeonLevel, "dungeon level");
                Check.Equal((bool)entry["completion_dungeon_xp"], plot.Reward.DungeonXp, "counts for its region");
                Check.Equal((bool)entry["can_retreat"], plot.CanRetreat, "can be abandoned, " + plot.Id);
                Check.Equal((int)entry["retreat_party_kill_count"], plot.RetreatDeaths, "deaths on retreat, " + plot.Id);
                Check.Equal((bool)entry["is_roster_stress_cleared_on_completion"], plot.ClearsRosterStress, "clears stress, " + plot.Id);
                var items = ((JObject)entry["quest"]["completion_reward"]["items_definition"]["items"]).Properties().Select(p => p.Value).ToList();
                Check.Equal(items.Where(i => (string)i["type"] == "gold").Sum(i => (int)i["amount"]), plot.Reward.Gold, "gold of " + plot.Id);
                foreach (var heirloom in items.Where(i => (string)i["type"] == "heirloom"))
                    Check.Equal((int)heirloom["amount"], plot.Reward.Heirlooms[(string)heirloom["id"]], heirloom["id"] + " of " + plot.Id);
                Check.Equal(items.Count(i => (string)i["type"] == "heirloom"), plot.Reward.Heirlooms.Count, "heirloom types of " + plot.Id);
                Check.True(items.Where(i => (string)i["type"] == "trinket").Select(i => (string)i["id"]).SequenceEqual(plot.Reward.NamedTrinkets.Distinct()), "named trinkets of " + plot.Id);
                Check.Equal(entry["additional_trinket_completion_rewards"].Sum(t => (int)t["amount"]), plot.Reward.TrinketRarities.Count, "rolled trinkets of " + plot.Id);
                Check.Equal(items.Count(i => !new[] { "gold", "heirloom", "trinket" }.Contains((string)i["type"])), plot.Reward.Unmapped.Count, "reward items the mod has nothing for, " + plot.Id);
                Check.Equal((int)entry["quest"]["completion_reward"]["resolve_xp"], plot.Reward.ResolveXp, "resolve experience of " + plot.Id);
            }
            Check.True(Rewards.Plot("no_such_quest") == null && Rewards.Plot(null) == null, "unknown plot quest");
        }

        public static void TrinketPricesAndWagonAreDd1s()
        {
            Check.True(Trinkets.Missing.Count == 0, "files not read: " + string.Join(", ", Trinkets.Missing));
            var entries = (JArray)Raw(TrinketRules.EntriesFile)["entries"];
            foreach (var rarity in TrinketRules.Ladder)
            {
                var prices = entries.Where(e => (string)e["rarity"] == rarity).Select(e => (int)e["price"]).ToList();
                Check.True(prices.Count > 0, "no DD1 trinket of rarity " + rarity);
                var usual = prices.GroupBy(p => p).OrderByDescending(g => g.Count()).First().Key;
                Check.Equal(usual, Trinkets.Price(rarity), "price of " + rarity);
                Check.True(Trinkets.Price(rarity) > 0, rarity + " is free");
            }
            for (var i = 1; i < TrinketRules.Ladder.Length; i++)
                Check.True(Trinkets.Price(TrinketRules.Ladder[i - 1]) < Trinkets.Price(TrinketRules.Ladder[i]), "the ladder climbs in price");
            Check.Equal(0, Trinkets.Price("no_such_rarity"), "unknown rarity");
            foreach (var entry in entries.Take(40))
                Check.Equal((string)entry["rarity"], Trinkets.RarityOf((string)entry["id"]), "rarity of " + entry["id"]);

            var raws = Raw(TrinketRules.RaritiesFile)["rarities"].Select(r => (string)r["id"]).ToList();
            Check.True(raws.SequenceEqual(Trinkets.Rarities.Select(r => r.Key)), "rarities");
            foreach (var rarity in TrinketRules.Ladder) Check.True(raws.Contains(rarity), rarity + " is not a DD1 rarity");

            var store = Raw(TrinketRules.WagonFile)["data"]["stores"][0]["data"];
            var discount = (double)store["trinket_sell_value_discount_upgrades"][0]["discount_percent"];
            Check.Near(discount, Trinkets.SellDiscount, 1e-9, "sell discount");
            foreach (var rarity in TrinketRules.Ladder)
            {
                Check.Near(Trinkets.Price(rarity) * (1 - discount), Trinkets.SellValue(rarity), 0.5, "sell value of " + rarity);
                Check.True(Trinkets.SellValue(rarity) < Trinkets.Price(rarity), "selling pays less than buying");
            }

            var table = (JArray)store["rarity_generation_table"];
            Check.Equal(table.Count, Trinkets.WagonRarities.Count, "wagon rarities");
            var total = table.Sum(r => (double)r["chance"]);
            var rng = new Rng(17);
            const int n = 36000;
            var stock = Trinkets.RollWagonStock(n, rng);
            Check.Equal(n, stock.Count, "stock size");
            foreach (var row in table)
            {
                Check.True(Trinkets.Price((string)row["rarity"]) > 0, "the wagon sells a rarity without a price: " + row["rarity"]);
                Check.Near((double)row["chance"] / total, stock.Count(r => r == (string)row["rarity"]) / (double)n, 0.01, "share of " + row["rarity"]);
            }
            Check.Equal(0, Trinkets.RollWagonStock(0, rng).Count, "an empty wagon");
        }

        public static void RulesFallBackWithoutDd1()
        {
            var none = new NoDd1Files();
            var rules = QuestGenerationRules.Load(none);
            var rewards = QuestRewardRules.Load(none);
            var trinkets = TrinketRules.Load(none);
            Check.Equal(3, rules.Missing.Count, "missing generation files");
            Check.True(rewards.IsFallback && rewards.PlotQuests.Count == 0, "rewards fall back");
            Check.Equal(3, trinkets.Missing.Count, "missing trinket files");
            Check.True(trinkets.Price("common") > 0 && trinkets.WagonRarities.Count > 0, "the wagon still trades");

            var roll = QuestGeneration.Roll(rules, rewards, Estate(5), new Rng(1));
            Check.True(roll.Offers.Count > 0, "a board without DD1 still offers quests");
            foreach (var offer in roll.Offers)
            {
                Check.True(offer.Reward.Gold > 0, "a quest without pay");
                Check.Equal(0, offer.Reward.TrinketRarities.Count, "no trinket table, no trinket");
            }
        }
    }
}
