using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Core
{
    /// <summary>"<see cref="Amount"/> upgrade steps of trees tagged <see cref="Tag"/>" (<c>upgrade_tags_to_remove_on_ignore</c>).</summary>
    public sealed class UpgradeTagCount
    {
        public string Tag;
        public int Amount;
    }

    /// <summary>One line of <c>party_quirks_to_apply_on_completion</c> / <c>_on_failure</c>: quirks and their weight in the draw.</summary>
    public sealed class PartyQuirkChance
    {
        public readonly List<string> Quirks = new List<string>();
        public double Chance;
    }

    /// <summary>
    /// What DD1 says about a plot quest that does not belong to its story: one a town event brings (the Brigand
    /// Incursion, "Shrieker's Prize") or one the Shrieker's hoard brings ("Shrieker's Perch"). Read from
    /// <c>campaign/quest/quest.plot_quests.json</c>; the reward and the quest's other rules are
    /// <see cref="PlotQuestInfo"/>'s.
    /// </summary>
    public sealed class PlotEventQuest
    {
        public string Id, Dungeon, Type;
        /// <summary><c>quest.map_name</c>: the hand-made map (<c>maps/&lt;name&gt;.dm</c>); null for a generated one.</summary>
        public string MapName;
        public int Difficulty = 1, Length = 1;
        /// <summary><c>quest.goal_ids</c>: "kill_brigand_sapper_D".</summary>
        public readonly List<string> Goals = new List<string>();
        /// <summary><c>is_generated_by_event</c>: on offer only in the week of a town event that names it [exe 0x93204b].</summary>
        public bool GeneratedByEvent;
        public bool Repeatable;
        /// <summary><c>retreat_always_from_raid</c>: fleeing its fight is abandoning the quest.</summary>
        public bool RetreatAlwaysFromRaid;
        public bool CanRetreat = true;
        public int RetreatDeaths;
        public readonly List<UpgradeTagCount> RemoveOnIgnore = new List<UpgradeTagCount>();
        public readonly List<UpgradeTagCount> RemoveOnFailure = new List<UpgradeTagCount>();
        /// <summary><c>trinket_retention_count</c>: trinkets of the Shrieker's hoard the quest asks for and gives back (8); 0: not such a quest.</summary>
        public int RetentionCount;
        /// <summary><c>trinket_retention_minimum_rarity</c>: the hoard's best trinket must be at least this rare.</summary>
        public string RetentionMinRarity;
        public readonly List<PartyQuirkChance> QuirksOnCompletion = new List<PartyQuirkChance>();
        public readonly List<PartyQuirkChance> QuirksOnFailure = new List<PartyQuirkChance>();

        /// <summary>The monster a kill goal names: "kill_brigand_sapper_D" names "brigand_sapper_D". Null for no such goal.</summary>
        public string GoalMonster
        {
            get
            {
                foreach (var goal in Goals)
                    if (goal.StartsWith("kill_", StringComparison.Ordinal)) return goal.Substring(5);
                return null;
            }
        }
    }

    /// <summary>DD1's plot quests outside its story, in the file's order (the order DD1 tries them in).</summary>
    public sealed class PlotEventRules
    {
        public const string EventEffect = "plot_quest";

        private readonly List<PlotEventQuest> _quests = new List<PlotEventQuest>();

        public IReadOnlyList<PlotEventQuest> Quests => _quests;

        public PlotEventQuest Find(string id)
        {
            foreach (var quest in _quests)
                if (quest.Id == id) return quest;
            return null;
        }

        public static PlotEventRules Load(IDd1Files files) => Parse(files.ReadText(QuestRewardRules.PlotFile));

        public static PlotEventRules Parse(string text)
        {
            var rules = new PlotEventRules();
            foreach (var entry in Json.Array(Json.ParseFile(text)?["plot_quests"]))
            {
                var id = (string)entry["id"];
                var quest = entry["quest"];
                if (id == null || quest == null || quest.Type != JTokenType.Object) continue;
                var plot = new PlotEventQuest
                {
                    Id = id,
                    Dungeon = (string)quest["dungeon"],
                    Type = (string)quest["type"],
                    MapName = (string)quest["map_name"],
                    Difficulty = Json.Int(quest["difficulty"], 1),
                    Length = Json.Int(quest["length"], 1),
                    GeneratedByEvent = Json.Bool(entry["is_generated_by_event"], false),
                    Repeatable = Json.Bool(entry["is_repeatable"], false),
                    RetreatAlwaysFromRaid = Json.Bool(entry["retreat_always_from_raid"], false),
                    CanRetreat = Json.Bool(entry["can_retreat"], true),
                    RetreatDeaths = Math.Max(0, Json.Int(entry["retreat_party_kill_count"], 0)),
                    RetentionCount = Math.Max(0, Json.Int(entry["trinket_retention_count"], 0)),
                    RetentionMinRarity = (string)entry["trinket_retention_minimum_rarity"] ?? ""
                };
                if (string.IsNullOrEmpty(plot.MapName)) plot.MapName = null;
                foreach (var goal in Json.Array(quest["goal_ids"]))
                    if (goal.Type == JTokenType.String) plot.Goals.Add((string)goal);
                ReadTags(entry["upgrade_tags_to_remove_on_ignore"], plot.RemoveOnIgnore);
                ReadTags(entry["upgrade_tags_to_remove_on_failure"], plot.RemoveOnFailure);
                ReadQuirks(entry["party_quirks_to_apply_on_completion"], plot.QuirksOnCompletion);
                ReadQuirks(entry["party_quirks_to_apply_on_failure"], plot.QuirksOnFailure);
                rules._quests.Add(plot);
            }
            return rules;
        }

        private static void ReadTags(JToken list, List<UpgradeTagCount> into)
        {
            foreach (var item in Json.Array(list))
            {
                var tag = (string)item["upgrade_tag"];
                var amount = Json.Int(item["amount"], 0);
                if (tag != null && amount > 0) into.Add(new UpgradeTagCount { Tag = tag, Amount = amount });
            }
        }

        private static void ReadQuirks(JToken list, List<PartyQuirkChance> into)
        {
            foreach (var item in Json.Array(list))
            {
                var line = new PartyQuirkChance { Chance = Json.Number(item["chance"], 0) };
                foreach (var quirk in Json.Array(item["quirk_ids"]))
                    if (quirk.Type == JTokenType.String) line.Quirks.Add((string)quirk);
                into.Add(line);
            }
        }

        /// <summary>
        /// One draw from a quest's quirk lines by their weights: the quirks of the line drawn (empty for the
        /// line that names none, and for a list without weight).
        /// </summary>
        public static List<string> DrawQuirks(IList<PartyQuirkChance> lines, Rng rng)
        {
            if (lines == null || lines.Count == 0) return new List<string>();
            var pick = rng.PickWeighted(lines.Select(l => l.Chance).ToList());
            return pick < 0 ? new List<string>() : new List<string>(lines[pick].Quirks);
        }
    }

    /// <summary>One upgrade tree of DD1's buildings as the removal rule needs it: its tags and its steps in order.</summary>
    public sealed class UpgradeTreeTags
    {
        public string Id;
        public readonly List<string> Tags = new List<string>();
        public readonly List<string> Codes = new List<string>();
    }

    /// <summary>One step taken back: the tree and the step's code.</summary>
    public sealed class UpgradeStepLost
    {
        public string Tree, Code;

        public override string ToString() => Tree + " " + Code;
    }

    /// <summary>
    /// DD1's price of a plot quest ignored or failed (<c>upgrade_tags_to_remove_on_ignore</c>,
    /// <c>upgrade_tags_to_remove_on_failure</c>): upgrade steps are taken back. The rule is in DD1's exe
    /// [0x94ad00], not in its files: for a tag and an amount, every tree that carries the tag and has a step
    /// built offers ONE step, the last one built in the tree's order; each such tree weighs the same; that
    /// many trees are drawn, none twice, and the step each offered is no longer built. So "three building
    /// upgrades" are the top steps of three different trees, or fewer when fewer trees have anything built.
    /// </summary>
    public static class UpgradeRemoval
    {
        public const string Folder = "upgrades/building";
        private const string Files = "*.upgrades.json";

        /// <summary>The trees of <c>upgrades/building/*.upgrades.json</c> with their tags, by file name and the file's order.</summary>
        public static List<UpgradeTreeTags> LoadTrees(IDd1Files files)
        {
            var trees = new List<UpgradeTreeTags>();
            var names = files.List(Folder, Files) ?? new string[0];
            Array.Sort(names, StringComparer.Ordinal);
            foreach (var name in names)
                foreach (var entry in Json.Array(Json.ParseFile(files.ReadText(Folder + "/" + name))?["trees"]))
                {
                    var id = (string)entry["id"];
                    if (id == null) continue;
                    var tree = new UpgradeTreeTags { Id = id };
                    foreach (var tag in Json.Array(entry["tags"]))
                        if (tag.Type == JTokenType.String) tree.Tags.Add((string)tag);
                    foreach (var step in Json.Array(entry["requirements"]))
                        if ((string)step["code"] != null) tree.Codes.Add((string)step["code"]);
                    trees.Add(tree);
                }
            return trees;
        }

        /// <summary>The step each tree of the tag would lose: its last built one. Trees with nothing built offer none.</summary>
        public static List<UpgradeStepLost> Candidates(IEnumerable<UpgradeTreeTags> trees, Func<string, string, bool> built, string tag)
        {
            var candidates = new List<UpgradeStepLost>();
            foreach (var tree in trees)
            {
                if (!tree.Tags.Contains(tag)) continue;
                string last = null;
                foreach (var code in tree.Codes)
                {
                    if (!built(tree.Id, code)) break;
                    last = code;
                }
                if (last != null) candidates.Add(new UpgradeStepLost { Tree = tree.Id, Code = last });
            }
            return candidates;
        }

        /// <summary>The steps lost for one tag: <paramref name="amount"/> of the candidates, drawn without putting back.</summary>
        public static List<UpgradeStepLost> Pick(IEnumerable<UpgradeTreeTags> trees, Func<string, string, bool> built, string tag, int amount, Rng rng)
        {
            var candidates = Candidates(trees, built, tag);
            var lost = new List<UpgradeStepLost>();
            for (var i = Math.Min(amount, candidates.Count); i > 0; i--)
            {
                var index = rng.Next(candidates.Count);
                lost.Add(candidates[index]);
                candidates.RemoveAt(index);
            }
            return lost;
        }
    }

    /// <summary>
    /// DD1's Shrieker outside its fight. Trinkets the estate loses to it are kept in a hoard of their own
    /// (DD1's "trinket retention"); a quest to the Shrieker's perch gives them back.
    ///
    /// From the files: the town event that fills the hoard from the estate's stores ("A Thief in the Night":
    /// <c>trinket_retention_add_from_storage</c>, 8 trinkets, base.town_events.events.json), the three quests
    /// (<c>plot_trinket_retention_0..2</c>: <c>trinket_retention_count</c> 8, a minimum rarity each), the order
    /// of the rarities (trinkets/base.rarities.trinkets.json, rarest first), the fight's length (the
    /// Shrieker's brain flies off from its turn <c>performing_turn_min</c> 12 on, at three turns a round).
    ///
    /// From the exe: the hoard is kept sorted rarest first [0x930d40]; a quest is offered while the hoard
    /// holds at least its count and the hoard's best trinket is at least as rare as its minimum [0x931dd5];
    /// the quests are tried in the file's order and one is offered at most [0x931ddf, 0x9320d0]; the quest's
    /// prize is the first <c>count</c> trinkets of the sorted hoard [0x931608].
    /// </summary>
    public sealed class ShriekerRules
    {
        public const string RaritiesFile = "trinkets/base.rarities.trinkets.json";
        public const string BrainsFile = "raid/ai/base.monster_brains.json";
        public const string ThiefEffect = "trinket_retention_add_from_storage";
        /// <summary>DD1's monster and the skill it leaves by.</summary>
        public const string Monster = "crow", EscapeSkill = "escape";
        /// <summary>DD1's rarity of the Shrieker's own trinkets ("Shrieker's Prize" pays one).</summary>
        public const string OwnRarity = "crow";

        // FALLBACK = DD1's stock list, used only when the file cannot be read
        private static readonly string[] StockRarities =
        {
            "darkest_dungeon", "trophy", "ancestral_shambler", "ancestral", "crow", "courtier", "collector", "madman", "very_rare", "rare", "uncommon", "common",
            "very_common", "kickstarter"
        };

        /// <summary>DD1's rarities, rarest first.</summary>
        public readonly List<string> Rarities = new List<string>();
        /// <summary>The Shrieker's turn from which it leaves, and its turns a round.</summary>
        public int EscapeTurn = 12, TurnsPerRound = 3;
        public readonly List<string> Missing = new List<string>();

        /// <summary>
        /// Rounds the fight lasts before the Shrieker is gone. READING: the brain counts the monster's own turns
        /// from 0, so with three turns a round its turns 0..11 are four rounds and the next one, the first of
        /// the fifth round, is the flight.
        /// </summary>
        public int Rounds => Math.Max(1, EscapeTurn / Math.Max(1, TurnsPerRound));

        public static ShriekerRules Load(IDd1Files files)
        {
            var rules = new ShriekerRules();
            foreach (var entry in Json.Array(Json.ParseFile(files.ReadText(RaritiesFile))?["rarities"]))
                if ((string)entry["id"] != null) rules.Rarities.Add((string)entry["id"]);
            if (rules.Rarities.Count == 0)
            {
                rules.Missing.Add(RaritiesFile);
                rules.Rarities.AddRange(StockRarities);
            }

            var found = false;
            foreach (var brain in Json.Array(Json.ParseFile(files.ReadText(BrainsFile))?["monster_brains"]))
            {
                if ((string)brain["id"] != Monster + "_A") continue;
                foreach (var desire in Json.Array(brain["skill_selection_desires"]))
                {
                    if ((string)desire.SelectToken("data.combat_skill_id") != EscapeSkill) continue;
                    var turn = Json.Int(desire.SelectToken("data.performing_turn_min"), -1);
                    if (turn < 0) continue;
                    rules.EscapeTurn = turn;
                    found = true;
                }
            }
            if (!found) rules.Missing.Add(BrainsFile);
            var info = files.ReadText("monsters/" + Monster + "/" + Monster + "_A/" + Monster + "_A.info.darkest");
            if (info == null) rules.Missing.Add("monsters/" + Monster);
            foreach (var block in DarkestFile.Parse(info))
                if (block.Name == "initiative") rules.TurnsPerRound = Math.Max(1, block.Int("number_of_turns_per_round", 0, rules.TurnsPerRound));
            return rules;
        }

        /// <summary>A rarity's place in DD1's list: 0 the rarest. One the list does not have counts as the commonest.</summary>
        public int Rank(string rarity)
        {
            var at = rarity != null ? Rarities.IndexOf(rarity) : -1;
            return at >= 0 ? at : Rarities.Count;
        }

        /// <summary>The hoard's order: rarest first, and as they came among equals [exe 0x930d40].</summary>
        public List<int> Sorted(IList<string> rarities)
        {
            return Enumerable.Range(0, rarities.Count).OrderBy(i => Rank(rarities[i])).ThenBy(i => i).ToList();
        }

        /// <summary>The best rarity in the hoard as its place in the list; the list's length for an empty hoard.</summary>
        public int Best(IList<string> rarities)
        {
            var best = Rarities.Count;
            foreach (var rarity in rarities) best = Math.Min(best, Rank(rarity));
            return best;
        }

        /// <summary>
        /// The quest the hoard brings, or null: the first of DD1's quests, in the file's order, whose count the
        /// hoard reaches and whose minimum rarity its best trinket meets. With DD1's three quests (uncommon,
        /// rare, very rare, in that order) this is the apprentice one whenever anything uncommon or better is
        /// in a hoard of eight.
        /// </summary>
        public PlotEventQuest QuestFor(IList<string> hoard, IEnumerable<PlotEventQuest> quests)
        {
            var best = Best(hoard);
            foreach (var quest in quests)
            {
                if (quest.RetentionCount <= 0 || quest.GeneratedByEvent) continue;
                if (hoard.Count >= quest.RetentionCount && best <= Rank(quest.RetentionMinRarity)) return quest;
            }
            return null;
        }

        /// <summary>The trinkets a quest gives back: the first of the sorted hoard, as many as its count (indices into the hoard).</summary>
        public List<int> Prize(IList<string> hoard, PlotEventQuest quest)
        {
            return Sorted(hoard).Take(Math.Max(0, quest != null ? quest.RetentionCount : 0)).ToList();
        }

        /// <summary>
        /// Which of the stores' trinkets the thief takes (indices into the list given). GUESS: the rarest first,
        /// drawn by lot among equals. DD1's files say how many (the event's number, 8) and that the stores must
        /// hold that many; which ones is in the exe and was not found. The hoard itself is kept rarest first
        /// and the quest gives back the rarest eight, so the same order is taken here.
        /// </summary>
        public List<int> Steal(IList<string> stored, int count, Rng rng)
        {
            var order = Enumerable.Range(0, stored.Count).ToList();
            rng.Shuffle(order);
            return order.OrderBy(i => Rank(stored[i])).Take(Math.Max(0, count)).ToList();
        }

        /// <summary>DD1's letter of a monster's level: A apprentice, B veteran, C champion.</summary>
        public static string Letter(int difficulty) => difficulty >= 5 ? "C" : difficulty >= 3 ? "B" : "A";

        /// <summary>
        /// What the nest pays when it is broken up (<c>loot:</c> of monsters/nest/nest_&lt;letter&gt;): DD1's loot
        /// table and the number of draws; null when the file has none.
        /// </summary>
        public static LootDraw NestLoot(IDd1Files files, int difficulty)
        {
            var name = "nest_" + Letter(difficulty);
            foreach (var block in DarkestFile.Parse(files.ReadText("monsters/nest/" + name + "/" + name + ".info.darkest")))
            {
                if (block.Name != "loot") continue;
                var code = block.Text("code");
                var count = block.Int("count", 0, 0);
                if (!string.IsNullOrEmpty(code) && code != "NONE" && count > 0) return new LootDraw { Table = code, Draws = count };
            }
            return null;
        }
    }
}
