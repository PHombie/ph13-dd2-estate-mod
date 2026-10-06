using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Core
{
    /// <summary>What a finished quest pays, in DD1 terms: the game layer scales the gold and finds the trinkets.</summary>
    public sealed class QuestReward
    {
        /// <summary>DD1 gold.</summary>
        public int Gold;
        public readonly Dictionary<string, int> Heirlooms = new Dictionary<string, int>();
        /// <summary>One trinket of each DD1 rarity listed ("common" ... "ancestral").</summary>
        public readonly List<string> TrinketRarities = new List<string>();
        /// <summary>DD1 trinkets a plot quest names outright (a boss's own trinket).</summary>
        public readonly List<string> NamedTrinkets = new List<string>();
        /// <summary>Resolve experience per survivor, where the quest states its own; 0 = the table by difficulty and length.</summary>
        public int ResolveXp;
        /// <summary>Whether the quest counts towards its region's level (DD1's boss quests do not).</summary>
        public bool DungeonXp = true;
        /// <summary>Reward items of a DD1 type the mod has nothing for ("trinket_unlock"), named so they can be reported.</summary>
        public readonly List<string> Unmapped = new List<string>();
    }

    /// <summary>What DD1 says about one of its plot quests beyond the reward.</summary>
    public sealed class PlotQuestInfo
    {
        public string Id, Dungeon, Type;
        public int Difficulty, Length, DungeonLevel;
        /// <summary><c>can_retreat</c>: whether the quest can be abandoned at all (DD1's last descent cannot).</summary>
        public bool CanRetreat = true;
        /// <summary><c>retreat_party_kill_count</c>: heroes of the party who die when the quest is abandoned (the Darkest Dungeon: one).</summary>
        public int RetreatDeaths;
        /// <summary><c>is_roster_stress_cleared_on_completion</c>: finishing it takes the stress off every hero of the estate.</summary>
        public bool ClearsRosterStress;
        /// <summary><c>is_surprise_enabled</c>: false in the Darkest Dungeon, where no fight starts with either side caught off guard.</summary>
        public bool SurpriseEnabled = true;
        /// <summary><c>is_scouting_enabled</c>: false in the Darkest Dungeon, where a room entered shows nothing of what lies ahead.</summary>
        public bool ScoutingEnabled = true;
        /// <summary><c>quest.torch_setting</c>: a key of <c>scripts/raid_settings.json</c> ("dd4" for the last descent); null for the default.</summary>
        public string TorchSetting;
        /// <summary><c>roster_buffs_to_apply_on_failure</c>: buffs of <c>shared/buffs/base.buffs.json</c> the estate's heroes get when the quest is not completed.</summary>
        public List<string> RosterBuffsOnFailure = new List<string>();
        /// <summary><c>roster_buff_on_failure_minimum_party_resolve_level</c>: the party's level those buffs ask for.</summary>
        public int RosterBuffMinPartyLevel;
        public QuestReward Reward = new QuestReward();
    }

    /// <summary>
    /// DD1's quest rewards, read from the player's install: the <c>rewards</c> block of
    /// <c>campaign/quest/quest.generation.json</c> for generated quests (gold by difficulty and length,
    /// the heirloom types a region pays in and their amounts, the rarity of the trinket) and
    /// <c>campaign/quest/quest.plot_quests.json</c> for the plot quests (each states its reward in full).
    /// </summary>
    public sealed class QuestRewardRules
    {
        public const string PlotFile = "campaign/quest/quest.plot_quests.json";

        /// <summary>
        /// DD1 behaviour, not in data: a generated quest pays in two of its region's heirloom types. The file
        /// lists the types a region can pay in and the amount of each; how many of them one quest shows is in
        /// DD1's code (its reward grid has four columns: gold, two heirlooms, the trinket).
        /// </summary>
        public const int HeirloomTypesPerQuest = 2;

        // FALLBACK, used only when quest.generation.json cannot be read: DD1's gold of a short apprentice quest.
        private const int FallbackGold = 3000;

        private JArray _gold;                                                   // [difficulty][length] -> items
        private readonly Dictionary<string, List<string>> _heirloomTypes = new Dictionary<string, List<string>>();
        private readonly Dictionary<string, JArray> _heirloomAmounts = new Dictionary<string, JArray>();
        private readonly List<KeyValuePair<string, JArray>> _trinketChances = new List<KeyValuePair<string, JArray>>();
        private readonly Dictionary<string, PlotQuestInfo> _plots = new Dictionary<string, PlotQuestInfo>();

        public bool IsFallback { get; private set; }

        public IReadOnlyCollection<PlotQuestInfo> PlotQuests => _plots.Values;

        public static QuestRewardRules Load(IDd1Files files)
        {
            var rules = new QuestRewardRules();
            var rewards = Json.ParseFile(files.ReadText(QuestGenerationRules.GenerationFile))?.SelectToken("generation.rewards");
            if (rewards == null) rules.IsFallback = true;
            else
            {
                rules._gold = rewards["item_table"] as JArray;
                foreach (var entry in Json.Array(rewards["heirloom_type_map"]))
                    rules._heirloomTypes[(string)entry["dungeon"] ?? ""] = Json.Array(entry["types"]).Select(t => (string)t).Where(t => t != null).ToList();
                foreach (var entry in Json.Array(rewards["heirloom_amount_table"]))
                    if (entry["amounts"] is JArray amounts) rules._heirloomAmounts[(string)entry["type"] ?? ""] = amounts;
                foreach (var entry in Json.Array(rewards["trinket_chance_table"]))
                    if (entry["chances"] is JArray chances && entry["rarity"] != null)
                        rules._trinketChances.Add(new KeyValuePair<string, JArray>((string)entry["rarity"], chances));
            }

            foreach (var entry in Json.Array(Json.ParseFile(files.ReadText(PlotFile))?["plot_quests"]))
            {
                var plot = ReadPlot(entry);
                if (plot != null) rules._plots[plot.Id] = plot;
            }
            return rules;
        }

        private static JToken Cell(JArray table, int row, int column)
        {
            if (table == null || row < 0 || row >= table.Count) return null;
            return table[row] is JArray cells && column >= 0 && column < cells.Count ? cells[column] : null;
        }

        /// <summary>DD1 gold of a generated quest (<c>item_table</c>, rows difficulty, columns length).</summary>
        public int Gold(int difficulty, int length)
        {
            if (_gold == null) return FallbackGold;
            var gold = 0;
            foreach (var item in Json.Array(Cell(_gold, difficulty, length)))
                if ((string)item["type"] == "gold") gold += Json.Int(item["amount"], 0);
            return gold;
        }

        /// <summary>The heirloom types a region's quests can pay in (<c>heirloom_type_map</c>).</summary>
        public IReadOnlyList<string> HeirloomTypes(string dungeon)
        {
            return _heirloomTypes.TryGetValue(dungeon ?? "", out var types) ? types : new List<string>();
        }

        /// <summary>How much of an heirloom type a quest pays (<c>heirloom_amount_table</c>, rows difficulty, columns length).</summary>
        public int HeirloomAmount(string type, int difficulty, int length)
        {
            return _heirloomAmounts.TryGetValue(type ?? "", out var amounts) ? Json.Int(Cell(amounts, difficulty, length), 0) : 0;
        }

        /// <summary>
        /// The weights of <c>trinket_chance_table</c> for a difficulty and a length, by DD1 rarity. Stock DD1
        /// gives one rarity the whole weight (apprentice short: common ... champion long: ancestral).
        /// </summary>
        public List<KeyValuePair<string, double>> TrinketChances(int difficulty, int length)
        {
            var chances = new List<KeyValuePair<string, double>>();
            foreach (var entry in _trinketChances)
            {
                var chance = Json.Number(Cell(entry.Value, difficulty, length), 0);
                if (chance > 0) chances.Add(new KeyValuePair<string, double>(entry.Key, chance));
            }
            return chances;
        }

        /// <summary>The reward DD1 rolls for a generated quest.</summary>
        public QuestReward Generated(string dungeon, int difficulty, int length, Rng rng)
        {
            var reward = new QuestReward { Gold = Gold(difficulty, length) };

            var types = HeirloomTypes(dungeon).Where(type => HeirloomAmount(type, difficulty, length) > 0).ToList();
            rng.Shuffle(types);
            foreach (var type in types.Take(HeirloomTypesPerQuest))
                reward.Heirlooms[type] = HeirloomAmount(type, difficulty, length);

            var chances = TrinketChances(difficulty, length);
            var pick = rng.PickWeighted(chances.Select(c => c.Value).ToList());
            if (pick >= 0) reward.TrinketRarities.Add(chances[pick].Key);
            return reward;
        }

        /// <summary>What DD1 says about one of its plot quests; null when it has no quest of that id.</summary>
        public PlotQuestInfo Plot(string dd1QuestId)
        {
            return dd1QuestId != null && _plots.TryGetValue(dd1QuestId, out var plot) ? plot : null;
        }

        private static PlotQuestInfo ReadPlot(JToken entry)
        {
            var id = (string)entry["id"];
            var quest = entry["quest"];
            if (id == null || quest == null || quest.Type != JTokenType.Object) return null;
            var plot = new PlotQuestInfo
            {
                Id = id,
                Dungeon = (string)quest["dungeon"],
                Type = (string)quest["type"],
                Difficulty = Json.Int(quest["difficulty"], 1),
                Length = Json.Int(quest["length"], 1),
                DungeonLevel = Json.Int(entry["dungeon_level"], 0),
                CanRetreat = Json.Bool(entry["can_retreat"], true),
                RetreatDeaths = Math.Max(0, Json.Int(entry["retreat_party_kill_count"], 0)),
                ClearsRosterStress = Json.Bool(entry["is_roster_stress_cleared_on_completion"], false),
                SurpriseEnabled = Json.Bool(entry["is_surprise_enabled"], true),
                ScoutingEnabled = Json.Bool(entry["is_scouting_enabled"], true),
                TorchSetting = (string)quest["torch_setting"],
                RosterBuffMinPartyLevel = Json.Int(entry["roster_buff_on_failure_minimum_party_resolve_level"], 0)
            };
            foreach (var buff in Json.Array(entry["roster_buffs_to_apply_on_failure"]))
                if (buff.Type == JTokenType.String) plot.RosterBuffsOnFailure.Add((string)buff);
            var reward = plot.Reward;
            reward.DungeonXp = Json.Bool(entry["completion_dungeon_xp"], false);
            var completion = quest["completion_reward"];
            reward.ResolveXp = Json.Int(completion?["resolve_xp"], 0);
            // "items" is an object keyed "0", "1"...: in the file's order
            if (completion?.SelectToken("items_definition.items") is JObject items)
                foreach (var property in items.Properties())
                {
                    var item = property.Value;
                    var type = (string)item["type"];
                    var itemId = (string)item["id"] ?? "";
                    var amount = Json.Int(item["amount"], 0);
                    if (amount <= 0) continue;
                    switch (type)
                    {
                        case "gold":
                            reward.Gold += amount;
                            break;
                        case "heirloom":
                            reward.Heirlooms.TryGetValue(itemId, out var have);
                            reward.Heirlooms[itemId] = have + amount;
                            break;
                        case "trinket":
                            for (var i = 0; i < amount; i++) reward.NamedTrinkets.Add(itemId);
                            break;
                        default:
                            reward.Unmapped.Add(type + ":" + itemId + " x" + amount);
                            break;
                    }
                }
            foreach (var extra in Json.Array(entry["additional_trinket_completion_rewards"]))
            {
                var rarity = (string)extra["rarity"];
                for (var i = 0; rarity != null && i < Json.Int(extra["amount"], 1); i++) reward.TrinketRarities.Add(rarity);
            }
            return plot;
        }
    }
}
