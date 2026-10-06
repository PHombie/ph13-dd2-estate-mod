using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Assets.Code.Actor;
using Assets.Code.Library;
using Assets.Code.Source;
using Assets.Code.Utils;
using DD2Estate.Core;
using DD2Estate.Dungeon;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Estate
{
    /// <summary>A trinket a quest pays: the DD1 rarity it is rolled at and the DD2 trinket shown for it.</summary>
    internal class QuestTrinket
    {
        /// <summary>DD1 rarity ("common" ... "ancestral"), or "trophy" for a boss's own trinket.</summary>
        public string Rarity;
        /// <summary>DD2 item id; null when no trinket of the rarity was left to show (the reward is rolled again when paid).</summary>
        public string Item;

        public bool IsTrophy => Rarity == Trinkets.Trophy;
    }

    /// <summary>
    /// One card of a quest's reward row on DD1's results screen: what the quest offered, and whether it was
    /// handed over (DD1 lists the offer after a quest not completed too, and withholds it).
    /// </summary>
    internal class QuestPayment
    {
        public const string Gold = "gold", Heirloom = "heirloom", Trinket = "trinket";

        public string Kind;
        /// <summary>The heirloom's kind ("crest"), or the DD2 trinket (null: none could be shown).</summary>
        public string Id;
        /// <summary>A trinket's DD1 rarity.</summary>
        public string Rarity;
        /// <summary>Gold, or how many.</summary>
        public int Amount = 1;
        public bool Given;
    }

    /// <summary>One expedition on offer: a story quest from <c>Data/plot_quests.json</c> or a weekly generated one.</summary>
    internal class Quest
    {
        public string Id, Name, Dungeon, Type, Boss, Tier, LengthName;
        public bool Plot;
        public string Goal, Intro, Victory;
        /// <summary>Gold, in DD1's numbers.</summary>
        public int Gold;
        public readonly Dictionary<string, int> Heirlooms = new Dictionary<string, int>();
        public readonly List<QuestTrinket> Trinkets = new List<QuestTrinket>();
        /// <summary>Whether finishing it counts towards its region's level (DD1: generated quests and the tutorial do, boss quests do not).</summary>
        public bool DungeonXp = true;
        /// <summary>DD1's <c>can_retreat</c> of the plot quest: false when the party cannot abandon the quest (the last descent).</summary>
        public bool CanRetreat = true;
        /// <summary>DD1's <c>retreat_party_kill_count</c>: heroes of the party who die when it is abandoned (the Darkest Dungeon: one).</summary>
        public int RetreatDeaths;
        /// <summary>DD1's <c>is_roster_stress_cleared_on_completion</c>: finishing it takes the stress off every hero of the estate.</summary>
        public bool ClearsStress;
        /// <summary>
        /// Resolve experience the quest states for its survivors (DD1's plot quests, <c>completion_reward.resolve_xp</c>);
        /// 0: what DD1's table gives its difficulty and length.
        /// </summary>
        public int ResolveXp;
        /// <summary>
        /// The DD1 plot quest a story quest stands for (<c>dd1_quest_id</c> of Data/plot_quests.json): DD1's
        /// rules of that quest are asked of it (<see cref="QuestBoard.Dd1Plot"/>). Null for a generated quest.
        /// </summary>
        public string Dd1Quest;

        public int Length => LengthName == "long" ? 3 : LengthName == "medium" ? 2 : 1;

        /// <summary>DD1 difficulty number the generator and the fight pools use (1, 3, 5).</summary>
        public int TierNumber => Tier == "champion" || Tier == "darkest" ? 5 : Tier == "veteran" ? 3 : 1;

        /// <summary>DD1's own difficulty number of the tier (1, 3, 5, and 6 for the Darkest Dungeon): map icons, level caps.</summary>
        public int Difficulty => Tier == "darkest" ? 6 : TierNumber;

        public JObject ToJson()
        {
            var trinkets = new JArray();
            foreach (var trinket in Trinkets) trinkets.Add(new JObject { ["rarity"] = trinket.Rarity, ["item"] = trinket.Item });
            return new JObject
            {
                ["id"] = Id, ["name"] = Name, ["dungeon"] = Dungeon, ["type"] = Type, ["boss"] = Boss, ["tier"] = Tier,
                ["length"] = LengthName, ["plot"] = Plot, ["goal"] = Goal, ["intro"] = Intro, ["victory"] = Victory,
                ["gold"] = Gold, ["heirlooms"] = JObject.FromObject(Heirlooms), ["trinkets"] = trinkets, ["dungeon_xp"] = DungeonXp,
                ["can_retreat"] = CanRetreat, ["retreat_deaths"] = RetreatDeaths, ["clears_stress"] = ClearsStress, ["resolve_xp"] = ResolveXp,
                ["dd1_quest"] = Dd1Quest
            };
        }

        public static Quest FromJson(JToken json)
        {
            var quest = new Quest
            {
                Id = (string)json["id"], Name = (string)json["name"], Dungeon = (string)json["dungeon"], Type = (string)json["type"],
                Boss = (string)json["boss"], Tier = (string)json["tier"] ?? "apprentice", LengthName = (string)json["length"] ?? "short",
                Plot = (bool?)json["plot"] ?? false, Goal = (string)json["goal"], Intro = (string)json["intro"], Victory = (string)json["victory"],
                Gold = (int?)json["gold"] ?? 0,
                // a save from before DD1's rule was read: every quest counted for its region
                DungeonXp = (bool?)json["dungeon_xp"] ?? true,
                CanRetreat = (bool?)json["can_retreat"] ?? true,
                RetreatDeaths = (int?)json["retreat_deaths"] ?? 0,
                ClearsStress = (bool?)json["clears_stress"] ?? false,
                // a save from before the quest kept its own experience: QuestBoard.ResolveXpOf asks DD1 again
                ResolveXp = (int?)json["resolve_xp"] ?? 0,
                // a save from before the quest kept the name: QuestBoard.Dd1Plot looks it up by the quest's own id
                Dd1Quest = (string)json["dd1_quest"]
            };
            if (json["heirlooms"] is JObject heirlooms)
                foreach (var p in heirlooms.Properties()) quest.Heirlooms[p.Name] = (int)p.Value;
            foreach (var trinket in json["trinkets"] as JArray ?? new JArray())
                if ((string)trinket["rarity"] != null) quest.Trinkets.Add(new QuestTrinket { Rarity = (string)trinket["rarity"], Item = (string)trinket["item"] });
            return quest;
        }
    }

    /// <summary>Where a region stands, for the estate map: its level bar and whether it can be entered.</summary>
    internal struct DungeonProgress
    {
        public bool Open;
        /// <summary>Region level, 0..7 as in DD1; for the Darkest Dungeon the descents already made.</summary>
        public int Level;
        /// <summary>Points the quests finished in the region added up to (DD1: 2, 3 or 4 a quest, by its length).</summary>
        public int Done;
        /// <summary>Points still missing for the next level; 0 at the top.</summary>
        public int ToNextLevel;
        /// <summary>How far the region is from this level to the next, 0..1 (1 at the top).</summary>
        public float Share;
    }

    /// <summary>
    /// The quest board of the hamlet, run by DD1's rules read from the player's install
    /// (<see cref="QuestGeneration"/>, <see cref="QuestRewardRules"/>): how many quests a week offers and in
    /// which regions, their type, length and difficulty, what they pay (gold, heirlooms, a trinket), and how
    /// finished quests raise a region's level. The mod's story quests
    /// (<c>Data/plot_quests.json</c>) stand where DD1's plot quests stand: they keep their own unlock rules
    /// and are paid what the DD1 quest named in <c>dd1_quest_id</c> pays.
    /// Offers are rolled once per week and saved, so reloading does not reshuffle them.
    /// </summary>
    [EstateModule]
    internal static class QuestBoard
    {
        public const string DarkestDungeon = "darkestdungeon";

        private static JObject _plot;
        private static QuestGenerationRules _generation;
        private static QuestRewardRules _rewards;
        private static GenerationRules _goals;
        private static readonly List<Quest> Offers = new List<Quest>();
        private static int _offersWeek = -1;
        // What the board has been brought in line with: what the Darkest Dungeon is ([Rules] DarkestDungeon; null:
        // not yet, after a roll or a load) and, of its five stand-ins, the one that was due then.
        private static DarkestMode? _darkestMode;
        private static string _darkestDue;
        // points towards its level per region, and quests finished on the whole estate
        private static readonly Dictionary<string, int> RegionXp = new Dictionary<string, int>();
        private static int _finished;
        private static readonly List<string> RollNotes = new List<string>();
        private static int _wanted;

        /// <summary>Narration of the last finished story quest, shown once by the hamlet.</summary>
        public static string PendingNarration;

        public static void Register()
        {
            EstateState.RegisterSection("quests", Save, Load, Reset);
        }

        private static JToken Save()
        {
            return new JObject
            {
                ["week"] = _offersWeek,
                ["offers"] = new JArray(Offers.Select(q => q.ToJson())),
                ["xp"] = JObject.FromObject(RegionXp),
                ["finished"] = _finished
            };
        }

        private static void Load(JToken json)
        {
            Reset();
            if (json == null) return;
            // (the Darkest Dungeon's quests the save has on its board are brought in line with what the place is now: SyncDarkest)
            _offersWeek = (int?)json["week"] ?? -1;
            foreach (var q in json["offers"] as JArray ?? new JArray()) Offers.Add(Quest.FromJson(q));
            if (json["xp"] is JObject xp)
            {
                foreach (var p in xp.Properties()) RegionXp[p.Name] = (int)p.Value;
                _finished = (int?)json["finished"] ?? 0;
            }
            else if (json["done"] is JObject done)
            {
                // A save from before regions counted points: it holds quests per region. Each is taken for a
                // short one, and the week's offers are rolled again by the rules now in force.
                foreach (var p in done.Properties())
                {
                    RegionXp[p.Name] = (int)p.Value * Generation.DungeonXp(1);
                    _finished += (int)p.Value;
                }
                _offersWeek = -1;
            }
        }

        private static void Reset()
        {
            Offers.Clear();
            RegionXp.Clear();
            RollNotes.Clear();
            _finished = 0;
            _wanted = 0;
            _offersWeek = -1;
            _darkestMode = null;
            _darkestDue = null;
            PendingNarration = null;
        }

        // ---- rules -----------------------------------------------------------------------------------

        private static JObject PlotData
        {
            get
            {
                if (_plot != null) return _plot;
                using (var stream = typeof(QuestBoard).Assembly.GetManifestResourceStream("DD2Estate.Data.plot_quests.json"))
                using (var reader = new StreamReader(stream))
                    _plot = JObject.Parse(reader.ReadToEnd());
                return _plot;
            }
        }

        /// <summary>DD1's generation tables (the built-in stand-ins when the install cannot be read; the log says so).</summary>
        public static QuestGenerationRules Generation
        {
            get
            {
                if (_generation != null) return _generation;
                _generation = QuestGenerationRules.Load(new Dd1Files());
                if (_generation.Missing.Count > 0)
                    Plugin.Log.LogWarning("quest board: DD1 files not readable, stand-in rules in use: " + string.Join(", ", _generation.Missing));
                return _generation;
            }
        }

        /// <summary>DD1's reward tables and plot quests.</summary>
        public static QuestRewardRules Rewards
        {
            get
            {
                if (_rewards != null) return _rewards;
                _rewards = QuestRewardRules.Load(new Dd1Files());
                if (_rewards.IsFallback) Plugin.Log.LogWarning("quest board: DD1's reward tables not readable, quests pay a flat stand-in sum");
                return _rewards;
            }
        }

        // DD1's goals (quest.types.json): what a generated quest of a type asks for in a region.
        private static GenerationRules Goals
        {
            get
            {
                if (_goals != null) return _goals;
                try { _goals = GenerationRules.Load(new Dd1Files()); }
                catch (Exception e) { Plugin.Log.LogWarning("quest board: DD1's quest goals not readable (" + e.Message + ")"); }
                return _goals;
            }
        }

        // ---- regions ---------------------------------------------------------------------------------

        private static int Xp(string dungeonId) => RegionXp.TryGetValue(dungeonId ?? "", out var xp) ? xp : 0;

        public static int DungeonLevel(string dungeonId) => Generation.DungeonLevel(Xp(dungeonId));

        /// <summary>Quests finished on the whole estate: what DD1 opens its regions by.</summary>
        public static int QuestsFinished => _finished;

        /// <summary>
        /// The region's level bar. The Darkest Dungeon has no levels: its bar counts the descents of its story
        /// chain that are done, or, while it holds the five stand-ins, how many of those are won.
        /// </summary>
        public static DungeonProgress Progress(string dungeonId)
        {
            var progress = new DungeonProgress { Open = IsOpen(dungeonId) };
            if (dungeonId == DarkestDungeon && Estate.DarkestDungeon.Mode == DarkestMode.Bosses)
            {
                var five = DarkestBosses.Count;
                progress.Done = progress.Level = DarkestBosses.WonCount;
                progress.ToNextLevel = five > progress.Done ? 1 : 0;
                progress.Share = five > 0 ? progress.Done / (float)five : 1f;
                return progress;
            }
            if (dungeonId == DarkestDungeon)
            {
                var chain = ((JArray)PlotData["quests"]).Where(q => (string)q["dungeon"] == DarkestDungeon).Select(q => (string)q["id"]).ToList();
                progress.Done = progress.Level = chain.Count(IsCompleted);
                progress.ToNextLevel = chain.Count > progress.Done ? 1 : 0;
                progress.Share = chain.Count > 0 ? progress.Done / (float)chain.Count : 1f;
                return progress;
            }

            var thresholds = Generation.LevelThresholds;
            progress.Done = Xp(dungeonId);
            progress.Level = DungeonLevel(dungeonId);
            if (progress.Level + 1 >= thresholds.Length)
            {
                progress.Share = 1f;
                return progress;
            }
            int floor = thresholds[progress.Level], ceiling = thresholds[progress.Level + 1];
            progress.ToNextLevel = ceiling - progress.Done;
            progress.Share = ceiling > floor ? (progress.Done - floor) / (float)(ceiling - floor) : 1f;
            return progress;
        }

        private static JToken ModDungeon(string dungeonId) => DungeonContent.Dungeons().FirstOrDefault(d => (string)d["id"] == dungeonId);

        // The mod's own gate of a region (Data/dungeons.json): what its story quests wait for.
        private static bool ModOpen(JToken dungeon)
        {
            var unlock = dungeon?["unlock"] as JObject;
            if (dungeon == null || _finished < ((int?)unlock?["quests_completed"] ?? 0)) return false;
            return (unlock?["requires"] as JArray ?? new JArray()).All(r => IsCompleted((string)r));
        }

        // DD1's gate: the quests finished on the estate before quests are generated in the region.
        private static GeneratedDungeon Dd1Gate(string dungeonId) => Generation.Dungeons.FirstOrDefault(d => d.Id == dungeonId);

        /// <summary>
        /// Whether the region can be entered: DD1 generates quests there by now, or the mod's own gate is open
        /// (the Ruins from the start, for the first story quest; the Darkest Dungeon by its story quests).
        /// </summary>
        public static bool IsOpen(string dungeonId)
        {
            var dungeon = ModDungeon(dungeonId);
            if (dungeon == null) return false;
            if (dungeonId == DarkestDungeon)
            {
                // on the map and shut ([Rules] DarkestDungeon = shut), whatever the story quests say
                if (Estate.DarkestDungeon.Sealed) return false;
                // the five stand-ins: open from the first week, whatever the story quests say
                if (Estate.DarkestDungeon.Mode == DarkestMode.Bosses) return true;
            }
            var gate = Dd1Gate(dungeonId);
            return (gate != null && _finished >= gate.QuestsFinished) || ModOpen(dungeon);
        }

        /// <summary>What still stands between the estate and a locked region, in the mod's words; null when it is open.</summary>
        public static string LockReason(string dungeonId)
        {
            var dungeon = ModDungeon(dungeonId);
            if (dungeon == null || IsOpen(dungeonId)) return null;
            if (dungeonId == DarkestDungeon && Estate.DarkestDungeon.Sealed) return Estate.DarkestDungeon.SealedLine;
            var gate = Dd1Gate(dungeonId);
            var unlock = dungeon["unlock"] as JObject;
            var missing = (gate != null ? gate.QuestsFinished : (int?)unlock?["quests_completed"] ?? 0) - _finished;
            if (missing > 0) return missing + (missing == 1 ? " more quest" : " more quests") + " completed anywhere on the estate.";
            var requires = (unlock?["requires"] as JArray ?? new JArray()).Select(r => (string)r).ToList();
            return "Story quests that open the way: " + requires.Count(IsCompleted) + " of " + requires.Count + " done.";
        }

        /// <summary>Dev/test: set the points a region has towards its level (its level follows) and roll the offers again.</summary>
        public static void SetDone(string dungeonId, int done)
        {
            RegionXp[dungeonId] = Math.Max(0, done);
            _offersWeek = -1;
        }

        /// <summary>Dev/test: set the quests finished on the estate (DD1 opens regions by it) and roll the offers again.</summary>
        public static void SetFinished(int finished)
        {
            _finished = Math.Max(0, finished);
            _offersWeek = -1;
        }

        /// <summary>Dev/test: mark a story quest finished without playing it, and roll the offers again.</summary>
        public static bool SetCompleted(string questId)
        {
            if (!((JArray)PlotData["quests"]).Any(q => (string)q["id"] == questId)) return false;
            if (!IsCompleted(questId)) EstateState.Current.CompletedQuests.Add(questId);
            _offersWeek = -1;
            return true;
        }

        /// <summary>Dev/test: roll this week's offers again.</summary>
        public static void Reroll() => _offersWeek = -1;

        private static bool IsCompleted(string questId) => EstateState.Current.CompletedQuests.Contains(questId);

        // ---- the week's offers -----------------------------------------------------------------------

        /// <summary>This week's offers (rolled on first use each week).</summary>
        public static IReadOnlyList<Quest> Current()
        {
            var week = EstateState.Current.Week;
            if (_offersWeek != week) Roll(week);
            SyncDarkest();
            return Offers;
        }

        // The board follows what the Darkest Dungeon is ([Rules] DarkestDungeon). shut: none of its quests is on
        // the board, one a saved estate had there neither. bosses: the one of the five stand-ins that is due
        // (Estate/DarkestBosses.cs) and none of the mod's descents. dd1: the descents that are due, looked for as
        // a roll would, and no stand-in. A change in the middle of a week (the dev bridge), and a save from
        // before the place was what it is now, are brought in line the same way.
        private static void SyncDarkest()
        {
            var mode = Estate.DarkestDungeon.Mode;
            var due = mode == DarkestMode.Bosses ? DarkestBosses.DueQuestId : null;
            if (_darkestMode == mode && _darkestDue == due) return;
            _darkestMode = mode;
            _darkestDue = due;

            bool Belongs(Quest quest)
            {
                var standIn = DarkestBosses.IsOurs(quest);
                return mode == DarkestMode.Bosses ? standIn && quest.Id == due : mode == DarkestMode.Dd1 && !standIn;
            }

            var gone = Offers.Where(q => q.Dungeon == DarkestDungeon && !Belongs(q)).Select(q => q.Id).ToList();
            if (gone.Count > 0)
            {
                Offers.RemoveAll(q => q.Dungeon == DarkestDungeon && !Belongs(q));
                Plugin.Log.LogInfo("quest board: the Darkest Dungeon is " + DarkestBossRules.NameOf(mode) + ": off the board: " + string.Join(", ", gone));
            }
            if (mode == DarkestMode.Shut) return;
            var taken = new HashSet<string>(NomadWagon.WareIds());
            foreach (var item in RewardTrinkets()) taken.Add(item);
            var before = Offers.Count;
            var rng = new Rng(DateTime.Now.Ticks ^ 0x4444L);
            if (mode == DarkestMode.Dd1) AddStoryQuests(rng, taken, DarkestDungeon);
            else AddBossQuest(rng, taken);
            if (Offers.Count > before) Plugin.Log.LogInfo("quest board: the Darkest Dungeon is " + DarkestBossRules.NameOf(mode) + ": " + (Offers.Count - before) + " of its quests on the board");
        }

        // The Darkest Dungeon's stand-in that is due (Estate/DarkestBosses.cs), unless it is on the board: paid
        // what DD1 pays the descent this boss stands for in the mod's story (the gold, the heirlooms, the
        // trinket and the experience of DD1's own Darkest Dungeon quests), and none of that descent's rules
        // (a way back, no hero's life for a retreat, no stress lifted off the estate).
        private static void AddBossQuest(Rng rng, HashSet<string> taken)
        {
            var quest = DarkestBosses.Offer();
            if (quest == null || Offers.Any(q => q.Id == quest.Id)) return;
            var plot = Rewards.Plot(DarkestBosses.Dd1QuestOf(quest));
            if (plot != null) Pay(quest, plot.Reward, rng, taken);
            else
            {
                Plugin.Log.LogWarning("quest board: DD1's reward of a Darkest Dungeon quest could not be read (" + quest.Id + "): paid as a generated quest of its difficulty");
                Pay(quest, Rewards.Generated(quest.Dungeon, quest.Difficulty, quest.Length, rng), rng, taken);
                quest.DungeonXp = false;
            }
            Offers.Add(quest);
        }

        /// <summary>
        /// The mod's story quest against a boss (Data/plot_quests.json), for its words and the DD1 quest it is
        /// paid as; null when the story has none for the boss.
        /// </summary>
        internal static JToken StoryOfBoss(string dungeonId, string bossId)
        {
            return bossId == null ? null : ((JArray)PlotData["quests"]).FirstOrDefault(q => (string)q["dungeon"] == dungeonId && (string)q["boss"] == bossId);
        }

        /// <summary>The DD2 trinkets this week's offers show as rewards: nobody else should hand out the same ones.</summary>
        public static IEnumerable<string> RewardTrinkets()
        {
            return Offers.SelectMany(q => q.Trinkets).Where(t => t.Item != null).Select(t => t.Item).ToList();
        }

        /// <summary>What the last roll could not offer although DD1's tables ask for it (a quest type not built yet...).</summary>
        public static IReadOnlyList<string> Notes => RollNotes;

        /// <summary>Quests DD1's table asks for this week; fewer are generated when the open regions cannot hold them.</summary>
        public static int Wanted => _wanted;

        private static void Roll(int week)
        {
            Offers.Clear();
            RollNotes.Clear();
            _offersWeek = week;
            _darkestMode = null;
            _darkestDue = null;
            var rng = new Rng(DateTime.Now.Ticks ^ (week * 7919L));
            // trinkets already spoken for this week: the wagon's wares, then every reward as it is rolled
            var taken = new HashSet<string>(NomadWagon.WareIds());

            var input = new QuestBoardInput
            {
                QuestsFinished = _finished,
                DungeonXp = new Dictionary<string, int>(RegionXp),
                ResolveLevels = RosterLifecycle.LivingGuids().Select(Resolve.Level).ToList(),
                SupportedTypes = QuestTypes.All,
                KnownDungeons = DungeonContent.Dungeons().Select(d => (string)d["id"]).Where(id => id != DarkestDungeon).ToList()
            };
            var roll = QuestGeneration.Roll(Generation, Rewards, input, rng);
            _wanted = roll.Wanted;
            RollNotes.AddRange(roll.Unsupported);
            var number = 0;
            foreach (var offer in roll.Offers) Offers.Add(Generated(offer, week, ++number, rng, taken));

            AddStoryQuests(rng, taken);
            if (Estate.DarkestDungeon.Mode == DarkestMode.Bosses) AddBossQuest(rng, taken);
            AddEventQuests(rng, taken);

            Plugin.Log.LogInfo("quest board: week " + week + ", " + _finished + " quests finished: DD1 asks for " + roll.Wanted + " quests in " +
                               (roll.OpenDungeons.Count > 0 ? string.Join(", ", roll.OpenDungeons) : "no region yet") + "; " + roll.Offers.Count + " generated, " +
                               (Offers.Count - roll.Offers.Count) + " story");
            foreach (var note in RollNotes) Plugin.Log.LogWarning("quest board: not offered: " + note);
        }

        // The mod's story quests that are due and not on the board: not done yet, what they wait for done, their
        // region open. `only`: those of one region. The Darkest Dungeon's descents only while the place is DD1's
        // ([Rules] DarkestDungeon = dd1): not while it is shut, and not while the five stand-ins hold it.
        private static void AddStoryQuests(Rng rng, HashSet<string> taken, string only = null)
        {
            foreach (var entry in (JArray)PlotData["quests"])
            {
                var id = (string)entry["id"];
                if (IsCompleted(id) || Offers.Any(q => q.Id == id)) continue;
                var dungeonId = (string)entry["dungeon"];
                if (only != null && dungeonId != only) continue;
                if (dungeonId == DarkestDungeon && Estate.DarkestDungeon.Mode != DarkestMode.Dd1) continue;
                var unlock = entry["unlock"] as JObject;
                var requires = (unlock?["requires"] as JArray ?? new JArray()).Select(r => (string)r);
                if (!requires.All(IsCompleted)) continue;
                if (DungeonLevel(dungeonId) < ((int?)unlock?["dungeon_level"] ?? 0)) continue;
                if (dungeonId != DarkestDungeon && !IsOpen(dungeonId)) continue;
                var quest = new Quest
                {
                    Id = id, Name = (string)entry["name"], Dungeon = dungeonId, Type = (string)entry["type"], Boss = (string)entry["boss"],
                    Tier = (string)entry["tier"], LengthName = (string)entry["length"], Plot = true,
                    Goal = (string)entry["goal_text"], Intro = (string)entry["intro_narration"], Victory = (string)entry["victory_narration"],
                    Dd1Quest = (string)entry["dd1_quest_id"]
                };
                if (!QuestTypes.All.Contains(quest.Type))
                {
                    RollNotes.Add(id + ": quest type '" + quest.Type + "' is not built yet");
                    continue;
                }
                PlotReward(quest, (string)entry["dd1_quest_id"], rng, taken);
                Offers.Add(quest);
            }
        }

        // DD1's quests of a town event and of the Shrieker's hoard (Estate/PlotQuests.cs): paid as DD1's plot
        // quests are.
        private static void AddEventQuests(Rng rng, HashSet<string> taken)
        {
            foreach (var quest in PlotQuests.Offers())
            {
                PlotReward(quest, quest.Dd1Quest, rng, taken);
                PlotQuests.Paid(quest);
                Offers.Add(quest);
            }
        }

        /// <summary>
        /// The week's offers stay as they are, but for DD1's event quests, which are looked for again: a town
        /// event came (or went) in the middle of the week, or the Shrieker's hoard changed.
        /// </summary>
        public static void SyncEventQuests()
        {
            if (_offersWeek != EstateState.Current.Week) return;    // not rolled yet: the roll will find them
            Offers.RemoveAll(PlotQuests.IsOurs);
            var taken = new HashSet<string>(NomadWagon.WareIds());
            foreach (var item in RewardTrinkets()) taken.Add(item);
            AddEventQuests(new Rng(DateTime.Now.Ticks ^ 0x4556L), taken);
        }

        public static string TierOf(int difficulty) => difficulty >= 6 ? "darkest" : difficulty >= 5 ? "champion" : difficulty >= 3 ? "veteran" : "apprentice";

        private static string LengthOf(int length) => length >= 3 ? "long" : length == 2 ? "medium" : "short";

        private static Quest Generated(QuestOffer offer, int week, int number, Rng rng, HashSet<string> taken)
        {
            string verb;
            switch (offer.Type)
            {
                case QuestTypes.Cleanse: verb = "Cleanse"; break;
                case QuestTypes.Gather: verb = "Gather in"; break;
                case QuestTypes.Activate:
                case QuestTypes.InventoryActivate: verb = "Rekindle"; break;
                default: verb = "Explore"; break;
            }
            var quest = new Quest
            {
                Id = "gen_" + week + "_" + number, Name = verb + " the " + DungeonContent.DisplayName(offer.Dungeon), Dungeon = offer.Dungeon, Type = offer.Type,
                Tier = TierOf(offer.Difficulty), LengthName = LengthOf(offer.Length),
                Goal = QuestMapText.Goal(offer.Type, Goals?.GoalFor(offer.Type, offer.Dungeon))
            };
            Pay(quest, offer.Reward, rng, taken);
            return quest;
        }

        // A story quest is paid what the DD1 plot quest it stands for pays; one DD1 has no entry for is paid
        // like a generated quest of its region, difficulty and length.
        private static void PlotReward(Quest quest, string dd1QuestId, Rng rng, HashSet<string> taken)
        {
            var plot = Rewards.Plot(dd1QuestId);
            if (plot == null)
            {
                if (dd1QuestId != null) Plugin.Log.LogWarning("quest board: DD1 has no plot quest '" + dd1QuestId + "' (" + quest.Id + "): paid as a generated quest");
                Pay(quest, Rewards.Generated(quest.Dungeon, quest.Difficulty, quest.Length, rng), rng, taken);
                return;
            }
            Pay(quest, plot.Reward, rng, taken);
            quest.CanRetreat = plot.CanRetreat;
            quest.RetreatDeaths = plot.RetreatDeaths;
            quest.ClearsStress = plot.ClearsRosterStress;
        }

        // DD1's reward in the estate's terms: gold and heirlooms as they are, a DD2 trinket for every DD1 one.
        private static void Pay(Quest quest, QuestReward reward, Rng rng, HashSet<string> taken)
        {
            quest.Gold = Math.Max(0, reward.Gold);
            quest.DungeonXp = reward.DungeonXp;
            quest.ResolveXp = reward.ResolveXp;
            quest.Heirlooms.Clear();
            foreach (var heirloom in reward.Heirlooms)
                if (heirloom.Value > 0) quest.Heirlooms[heirloom.Key] = heirloom.Value;
            quest.Trinkets.Clear();
            // a trophy DD1 names: the trinket of the DD2 boss standing in its place
            foreach (var _ in reward.NamedTrinkets)
            {
                var item = Trinkets.RollTrophy(quest.Boss, rng, taken);
                if (item != null) quest.Trinkets.Add(new QuestTrinket { Rarity = Trinkets.Trophy, Item = item });
                else quest.Trinkets.Add(new QuestTrinket { Rarity = "very_rare", Item = Trinkets.Roll("very_rare", rng, taken) });
                if (quest.Trinkets[quest.Trinkets.Count - 1].Item != null) taken.Add(quest.Trinkets[quest.Trinkets.Count - 1].Item);
            }
            foreach (var rarity in reward.TrinketRarities)
            {
                var item = Trinkets.Roll(rarity, rng, taken);
                if (item != null) taken.Add(item);
                quest.Trinkets.Add(new QuestTrinket { Rarity = rarity, Item = item });
            }
            foreach (var item in reward.Unmapped) Plugin.Log.LogInfo("quest board: " + quest.Id + ": DD1 also pays '" + item + "', which the estate has nothing for");
        }

        // ---- embarking and coming back ---------------------------------------------------------------

        /// <summary>
        /// Why a quest cannot be set out on at all, whoever asks; null when it can. A quest of the Darkest Dungeon
        /// while the place is shut, and one that is not what the place holds now (a descent while the five
        /// stand-ins hold it, a stand-in that is not the one on offer).
        /// </summary>
        public static string EmbarkBlockReason(Quest quest)
        {
            if (quest == null || quest.Dungeon != DarkestDungeon) return null;
            return Estate.DarkestDungeon.Sealed ? Estate.DarkestDungeon.SealedLine : DarkestBosses.BlockReason(quest);
        }

        /// <param name="bag">The provisions the party sets out with (<see cref="Provisioning"/>); null: empty hands.</param>
        public static void Embark(Quest quest, Core.Inventory bag = null)
        {
            // The board offers no quest that is refused here (Current); this is the last door, for a quest that
            // reaches it some other way.
            var stopped = EmbarkBlockReason(quest);
            if (stopped != null)
            {
                Plugin.Log.LogWarning("quest board: " + quest.Id + " is not set out on: " + stopped);
                Report(stopped);
                return;
            }
            DungeonRun.Start(quest.Dungeon, quest.Type, quest.Length, quest.TierNumber, quest, bag);
        }

        /// <summary>A line for the hamlet to show once the party is back, after any story narration.</summary>
        public static void Report(string line)
        {
            PendingNarration = string.IsNullOrEmpty(PendingNarration) ? line : PendingNarration + "\n\n" + line;
        }

        /// <summary>
        /// What a quest offers, card by card as DD1 lists it on the quest's card in town and on its results
        /// screen: the gold, the heirlooms (in the purse's order), the trinkets. Nothing is given by this.
        /// </summary>
        public static List<QuestPayment> Offer(Quest quest)
        {
            var cards = new List<QuestPayment>();
            if (quest == null) return cards;
            if (quest.Gold > 0) cards.Add(new QuestPayment { Kind = QuestPayment.Gold, Amount = quest.Gold });
            foreach (var id in EstateState.HeirloomIds)
                if (quest.Heirlooms.TryGetValue(id, out var amount) && amount > 0) cards.Add(new QuestPayment { Kind = QuestPayment.Heirloom, Id = id, Amount = amount });
            foreach (var pair in quest.Heirlooms)
                if (pair.Value > 0 && Array.IndexOf(EstateState.HeirloomIds, pair.Key) < 0) cards.Add(new QuestPayment { Kind = QuestPayment.Heirloom, Id = pair.Key, Amount = pair.Value });
            foreach (var trinket in quest.Trinkets) cards.Add(new QuestPayment { Kind = QuestPayment.Trinket, Id = trinket.Item, Rarity = trinket.Rarity });
            return cards;
        }

        /// <summary>
        /// Resolve experience the quest states for its survivors; 0: DD1's table by difficulty and length. A
        /// story quest out of a save from before quests kept the number is looked up in DD1's files again.
        /// </summary>
        public static int ResolveXpOf(Quest quest)
        {
            if (quest == null) return 0;
            if (quest.ResolveXp > 0 || !quest.Plot) return quest.ResolveXp;
            var entry = ((JArray)PlotData["quests"]).FirstOrDefault(q => (string)q["id"] == quest.Id);
            return Rewards.Plot((string)entry?["dd1_quest_id"])?.Reward.ResolveXp ?? 0;
        }

        /// <summary>
        /// What DD1 says about the plot quest a story quest stands for (no scouting and no surprise in the
        /// Darkest Dungeon, the last descent's torch, the buffs of a failed descent); null for a generated
        /// quest, a free expedition, or a story quest DD1 has no counterpart of.
        /// </summary>
        public static PlotQuestInfo Dd1Plot(Quest quest)
        {
            if (quest == null || !quest.Plot) return null;
            var id = quest.Dd1Quest;
            if (id == null)
            {
                var entry = ((JArray)PlotData["quests"]).FirstOrDefault(q => (string)q["id"] == quest.Id);
                id = (string)entry?["dd1_quest_id"];
            }
            return Rewards.Plot(id);
        }

        /// <summary>
        /// Called when an expedition ends, before the week advances. DD1 pays a quest only when its goal was
        /// met: an abandoned or lost quest brings no reward and counts for nothing (what the party keeps of
        /// its bag and the stress of giving up are the expedition's own business, DungeonRun.Finish).
        /// Returns the reward row for the results screen: what was really handed over (a trinket the estate
        /// came to hold since is another of its rarity, or gold), or the offer, withheld.
        /// </summary>
        public static List<QuestPayment> Finished(Quest quest, bool success)
        {
            var cards = Offer(quest);
            if (quest == null || !success) return cards;
            EstateState.AddGold(quest.Gold);
            foreach (var pair in quest.Heirlooms) EstateState.Current.AddHeirloom(pair.Key, pair.Value);
            var paid = new List<string>();
            if (quest.Gold > 0) paid.Add(quest.Gold + " gold");
            foreach (var id in EstateState.HeirloomIds)
                if (quest.Heirlooms.TryGetValue(id, out var amount) && amount > 0) paid.Add(amount + " " + (amount == 1 ? id : id + "s"));
            cards.RemoveAll(card => card.Kind == QuestPayment.Trinket);
            // a quest to the Shrieker's perch gives back what lay in its hoard: out of the hoard first
            PlotQuests.Completing(quest);
            cards.AddRange(GiveTrinkets(quest, paid));
            foreach (var card in cards) card.Given = true;

            _finished++;
            var levelBefore = DungeonLevel(quest.Dungeon);
            if (quest.DungeonXp && quest.Dungeon != DarkestDungeon)
                RegionXp[quest.Dungeon] = Xp(quest.Dungeon) + Generation.DungeonXp(quest.Length);
            if (quest.Plot && !IsCompleted(quest.Id))
            {
                EstateState.Current.CompletedQuests.Add(quest.Id);
                PendingNarration = quest.Victory;
            }
            // one of the Darkest Dungeon's five stand-ins: it is won for good, and the next is due with the new week
            DarkestBosses.Won(quest);
            // What was paid is told by the results screen's reward row (and by the activity log), not by the hamlet.
            if (quest.ClearsStress) ClearRosterStress();
            var level = DungeonLevel(quest.Dungeon);
            if (level > levelBefore) Report(DungeonContent.DisplayName(quest.Dungeon) + ": region level " + level + " reached.");
            Plugin.Log.LogInfo("quest board: " + quest.Id + " finished: " + string.Join(", ", paid) + "; " + quest.Dungeon + " " + Xp(quest.Dungeon) + " points (level " + level + "), " + _finished + " quests finished");
            return cards;
        }

        /// <summary>Why the party cannot abandon this quest; null when it can. DD1 (<c>can_retreat</c>): its last descent has no way back.</summary>
        public static string RetreatBlockReason(Quest quest)
        {
            return quest != null && !quest.CanRetreat ? "There is no way back from this descent." : null;
        }

        /// <summary>
        /// DD1's price of abandoning a quest beyond the stress (<c>retreat_party_kill_count</c>: one hero for
        /// every descent into the Darkest Dungeon): that many living heroes of the party, drawn at random, die
        /// covering the retreat. Call once when an expedition ends abandoned, before the survivors are counted.
        /// Returns the names of the fallen.
        /// </summary>
        public static List<string> PayForRetreat(Quest quest, IEnumerable<uint> party)
        {
            var fallen = new List<string>();
            if (quest == null || quest.RetreatDeaths <= 0 || party == null) return fallen;
            var actors = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance;
            var living = party.Select(guid => actors.GetLibraryElement(guid)).Where(actor => actor != null && actor.IsLiving).ToList();
            var rng = new Rng(DateTime.Now.Ticks);
            for (var i = 0; i < quest.RetreatDeaths && living.Count > 0; i++)
            {
                var index = rng.Next(living.Count);
                var hero = living[index];
                living.RemoveAt(index);
                fallen.Add(hero.ActorName);
                // nobody named as the killer: an empty list, as RosterLifecycle.KillForTest explains
                RosterLifecycle.Kill(hero, SourceType.STORY);
            }
            if (fallen.Count > 0) Report(string.Join(" and ", fallen) + (fallen.Count == 1 ? " stays" : " stay") + " behind so that the others may leave. The dark keeps what it is owed.");
            return fallen;
        }

        // DD1: a descent into the Darkest Dungeon survived lifts the stress of the whole estate.
        private static void ClearRosterStress()
        {
            var actors = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance;
            foreach (var guid in RosterLifecycle.LivingGuids())
            {
                var actor = actors.GetLibraryElement(guid);
                if (actor != null && actor.Stress > 0f) actor.ApplyStressHeal(actor.Stress, SourceType.STORY);
            }
            Report("Word of the descent goes round the hamlet, and every hero breathes easier.");
        }

        // The trinket shown on the board, or another of its rarity if the estate came to hold that one since;
        // DD1's sell value of the rarity in gold when nothing is left to give.
        // The cards are what the results screen shows in the trinkets' places; the words go to the log.
        private static List<QuestPayment> GiveTrinkets(Quest quest, List<string> paid)
        {
            var cards = new List<QuestPayment>();
            var rng = new Rng(DateTime.Now.Ticks);
            foreach (var trinket in quest.Trinkets)
            {
                var item = trinket.Item != null && Trinkets.CanHold(trinket.Item) ? trinket.Item : null;
                if (item == null) item = trinket.IsTrophy ? Trinkets.RollTrophy(quest.Boss, rng) ?? Trinkets.Roll("very_rare", rng) : Trinkets.Roll(trinket.Rarity, rng);
                if (item != null && Trinkets.Give(item, trinket.Rarity))
                {
                    paid.Add("the trinket " + Trinkets.Name(item));
                    cards.Add(new QuestPayment { Kind = QuestPayment.Trinket, Id = item, Rarity = trinket.Rarity });
                    continue;
                }
                var gold = Trinkets.GoldInstead(trinket.IsTrophy ? "very_rare" : trinket.Rarity);
                if (gold <= 0) continue;
                EstateState.AddGold(gold);
                paid.Add(gold + " gold for a trinket the estate has no more of");
                cards.Add(new QuestPayment { Kind = QuestPayment.Gold, Amount = gold });
            }
            return cards;
        }

        public static string TierLabel(Quest quest)
        {
            switch (quest.Tier)
            {
                case "veteran": return "Veteran";
                case "champion": return "Champion";
                case "darkest": return "Darkest";
                default: return "Apprentice";
            }
        }

        public static string RewardText(Quest quest)
        {
            var parts = new List<string> { quest.Gold + " gold" };
            parts.AddRange(quest.Heirlooms.Where(h => h.Value > 0).Select(h => h.Value + " " + h.Key + (h.Value > 1 ? "s" : "")));
            foreach (var trinket in quest.Trinkets)
                parts.Add((trinket.Item != null ? Trinkets.Name(trinket.Item) : "a trinket") + " (" + trinket.Rarity + ")");
            return string.Join(", ", parts);
        }

        /// <summary>Dev bridge: the rules in force and where the estate stands by them.</summary>
        public static object Describe()
        {
            var rules = Generation;
            var regions = new List<object>();
            foreach (var dungeon in rules.Dungeons)
            {
                var level = DungeonLevel(dungeon.Id);
                regions.Add(new
                {
                    id = dungeon.Id, opensAt = dungeon.QuestsFinished, open = _finished >= dungeon.QuestsFinished, points = Xp(dungeon.Id), level,
                    quests = rules.TemplatesFor(dungeon.Id, level).Select(t => t.ToString()).ToList(), heirlooms = Rewards.HeirloomTypes(dungeon.Id)
                });
            }
            return new
            {
                week = EstateState.Current.Week, offersWeek = _offersWeek, questsFinished = _finished,
                visitRow = QuestGeneration.VisitIndex(_finished), questsPerVisit = rules.QuestsPerVisit, wanted = rules.QuestCount(QuestGeneration.VisitIndex(_finished)),
                maxPerRegion = rules.MaxPerDungeon, pointsByLength = rules.DungeonXpByLength, levelThresholds = rules.LevelThresholds,
                roster = RosterLifecycle.LivingGuids().Select(Resolve.Level).OrderBy(l => l).ToList(),
                difficulties = rules.DifficultyWeights(RosterLifecycle.LivingGuids().Select(Resolve.Level)).Select(w => new { difficulty = w.Key, heroes = w.Value }).ToList(),
                regions, notOffered = RollNotes, missingFiles = rules.Missing, rewardsFallback = Rewards.IsFallback
            };
        }
    }
}
