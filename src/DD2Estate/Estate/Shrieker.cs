using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Assets.Code.Combat;
using Assets.Code.Combat.Events;
using Assets.Code.Events;
using Assets.Code.Utils;
using DD2Estate.Core;
using DD2Estate.Dd2;
using DD2Estate.Dev;
using DD2Estate.Dungeon;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Estate
{
    /// <summary>
    /// DD1's Shrieker: the estate's trinkets it carries off, and the fight at its perch.
    ///
    /// The hoard (DD1's "trinket retention"; the estate's section "shrieker") holds what it took: eight of the
    /// stores' trinkets when the town event "A Thief in the Night" comes (<c>trinket_retention_add_from_storage</c>),
    /// and what the heroes of a lost party wore ("The Shrieker stole %d trinkets from your downed party
    /// members!"). While the hoard holds eight and one of them is uncommon or better, the Estate Map offers
    /// "Shrieker's Perch" in the Weald (<see cref="PlotQuests"/>), whose reward is the rarest eight of the hoard;
    /// the quest done, they lie in the stores again. Nothing in the hoard is ever lost: a quest failed is offered
    /// again (DD1: is_repeatable). The numbers and where each comes from: <see cref="ShriekerRules"/>.
    ///
    /// The fight is DD2's (Data/dungeons.json, wanderer "shrieker": carrion eaters at their den in the forest);
    /// DD1's rule that the Shrieker is gone after four rounds is kept here: as the fifth round begins the fight
    /// is ended as won (DD1: the Shrieker kills itself as it leaves, so the quest's goal is met), and the nest's
    /// loot is paid only for a fight won outright.
    ///
    /// Bridge: shrieker.state, shrieker.event, shrieker.prize, shrieker.steal, shrieker.hoard, shrieker.embark,
    /// shrieker.win, shrieker.lose, shrieker.flee, shrieker.rounds, shrieker.fallen, shrieker.order.
    /// </summary>
    [EstateModule]
    internal static class Shrieker
    {
        private const string SectionKey = "shrieker";
        /// <summary>The fight's id in Data/dungeons.json.</summary>
        public const string BossId = "shrieker";
        public const string ThiefEvent = "trinket_retention_add_from_storage";
        public const string PrizeEvent = "plot_quest_crow_trinket";

        /// <summary>One trinket in the hoard.</summary>
        public class Held
        {
            /// <summary>DD2 item id.</summary>
            public string Id;
            /// <summary>The DD1 rarity it was valued at when taken.</summary>
            public string Grade;
            public int Week;
            /// <summary>"stores" (the thief) or "fallen" (a lost party).</summary>
            public string From;
        }

        /// <summary>
        /// DD1: what the heroes of a lost party wore goes to the Shrieker, to be won back. The mod's rule before
        /// this was "lost with the party" (DungeonRun.KeepTrinketsOfALostParty false); false here keeps that.
        /// </summary>
        public static bool TakesFromTheFallen = true;

        /// <summary>
        /// OWNER'S CHOICE: which of DD1's three quests to the perch the hoard brings when more than one fits.
        /// False is DD1's exe as read [0x931ddf]: the file's order, so the apprentice quest (level 1, heroes of
        /// level 3 and up refuse it) whenever anything uncommon or better is in the hoard. True tries the
        /// hardest first: the champion quest for a very rare trinket, the veteran one for a rare one.
        /// </summary>
        public static bool HardestQuestFirst = false;

        /// <summary>Dev bridge: rounds the fight lasts in place of DD1's (0: DD1's).</summary>
        public static int RoundsOverride;

        private static ShriekerRules _rules;
        private static readonly List<Held> Hoard = new List<Held>();
        private static int _taken, _returned;
        private static Rng _rng = NewRng();

        // the fight in progress
        private static bool _fighting, _listening, _fled;
        private static int _round, _rounds;
        private static string _lastFight = "";

        private static void Register()
        {
            EstateState.RegisterSection(SectionKey, Save, Load, Reset);
            Bridge();
        }

        public static ShriekerRules Rules
        {
            get
            {
                if (_rules != null) return _rules;
                _rules = ShriekerRules.Load(new Dd1Files());
                if (_rules.Missing.Count > 0) Plugin.Log.LogWarning("Shrieker: DD1 files not readable, DD1's stock numbers in use: " + string.Join(", ", _rules.Missing));
                return _rules;
            }
        }

        private static Rng NewRng() => new Rng(DateTime.Now.Ticks ^ 0x43524F57L);

        // ---- the hoard -----------------------------------------------------------------------------------

        public static IReadOnlyList<Held> All => Hoard;

        /// <summary>How many of a trinket the hoard holds: they are the estate's still, and count where DD2 lets a trinket be owned once.</summary>
        public static int InHoard(string id)
        {
            var count = 0;
            foreach (var held in Hoard)
                if (held.Id == id) count++;
            return count;
        }

        private static List<string> Grades() => Hoard.Select(h => h.Grade).ToList();

        /// <summary>The quest the hoard brings by DD1's rule; null while it brings none.</summary>
        public static PlotEventQuest QuestNow()
        {
            IEnumerable<PlotEventQuest> quests = PlotQuests.Rules.Quests;
            if (HardestQuestFirst) quests = quests.OrderByDescending(q => q.Difficulty).ToList();
            return Rules.QuestFor(Grades(), quests);
        }

        /// <summary>What a quest to the perch gives back: the rarest of the hoard, as many as the quest's count.</summary>
        public static List<Held> PrizeOf(PlotEventQuest quest)
        {
            return Rules.Prize(Grades(), quest).Select(i => Hoard[i]).ToList();
        }

        /// <summary>Takes trinkets out of the hoard (they are handed back by whoever asks: the quest's own reward).</summary>
        public static int Release(IEnumerable<string> ids)
        {
            var released = 0;
            foreach (var id in ids ?? new string[0])
            {
                var at = Hoard.FindIndex(h => h.Id == id);
                if (at < 0) continue;
                Hoard.RemoveAt(at);
                released++;
            }
            _returned += released;
            return released;
        }

        private static void Add(string id, string from)
        {
            Hoard.Add(new Held { Id = id, Grade = Trinkets.Grade(id), Week = EstateState.Current.Week, From = from });
            _taken++;
        }

        // ---- the thief -----------------------------------------------------------------------------------

        /// <summary>
        /// DD1's "A Thief in the Night": up to <paramref name="count"/> unworn trinkets leave the estate's stores
        /// for the hoard (which ones: <see cref="ShriekerRules.Steal"/>). Returns what was taken.
        /// </summary>
        public static List<string> Steal(int count)
        {
            var stored = Trinkets.Stored();
            var taken = new List<string>();
            foreach (var index in Rules.Steal(stored.Select(Trinkets.Grade).ToList(), count, _rng))
            {
                var id = stored[index];
                if (!Trinkets.Take(id)) continue;
                Add(id, "stores");
                taken.Add(id);
            }
            Plugin.Log.LogInfo("Shrieker: " + taken.Count + " trinket(s) taken from the stores: " + string.Join(", ", taken.Select(id => id + " (" + Trinkets.Grade(id) + ")"))
                               + "; the hoard holds " + Hoard.Count);
            return taken;
        }

        /// <summary>
        /// DD1: the trinkets of a party that did not come back are the Shrieker's. Returns how many it took
        /// (0 with <see cref="TakesFromTheFallen"/> off: they are lost, as before).
        /// GUESS: every such trinket is taken whatever its rarity; DD1's files give its quests a minimum rarity
        /// each, which decides here whether a quest comes, not what the hoard takes in.
        /// </summary>
        public static int TakeFromTheFallen(IEnumerable<string> ids)
        {
            if (!TakesFromTheFallen || ids == null) return 0;
            var taken = 0;
            foreach (var id in ids)
            {
                if (Trinkets.Find(id) == null) continue;
                Add(id, "fallen");
                taken++;
            }
            if (taken == 0) return 0;
            Plugin.Log.LogInfo("Shrieker: " + taken + " trinket(s) of the fallen go to the hoard, which holds " + Hoard.Count);
            // DD1's own entry of the Activity Log
            try
            {
                var format = Dd1Strings.Get("str_trinket_retention_log_item")
                             ?? "The Shrieker stole %d trinkets from your downed party members! You will have to reclaim them when he reveals himself in the Weald.";
                ActivityLog.Add(ActivityLog.Kinds.Note, Dd1Strings.Format(format, taken));
            }
            catch (Exception e) { Plugin.Log.LogWarning("Shrieker: the log's entry could not be written: " + e.Message); }
            return taken;
        }

        // ---- the fight -----------------------------------------------------------------------------------

        /// <summary>Whether a quest is one of the Shrieker's (the perch for the hoard, or the Prize).</summary>
        public static bool IsQuest(PlotEventQuest plot) => plot != null && plot.GoalMonster != null && plot.GoalMonster.StartsWith(ShriekerRules.Monster + "_", StringComparison.Ordinal);

        /// <summary>The fight flew off under the party in its last round (asked once the hub is back).</summary>
        public static bool Fled => _fled;

        /// <summary>The fight at the perch begins: from here on its rounds are counted.</summary>
        public static void FightBegins(int difficulty)
        {
            _fighting = true;
            _fled = false;
            _round = 0;
            _rounds = RoundsOverride > 0 ? RoundsOverride : Rules.Rounds;
            if (!_listening)
            {
                EventManager.AddListener<EventBattleStartRound>(OnRound);
                _listening = true;
            }
            _lastFight = "began (level " + difficulty + ", " + _rounds + " rounds)";
            Plugin.Log.LogInfo("Shrieker: the fight begins; it is gone when round " + (_rounds + 1) + " begins");
        }

        private static void OnRound(EventBattleStartRound e)
        {
            if (!_fighting || DungeonRun.Current == null) return;
            _round = e.m_Round;
            if (_round <= _rounds) return;
            TakeWing("round " + _round + " began");
        }

        // DD1: the Shrieker's "escape" kills it as it goes, and the fight is over with its goal met.
        private static bool TakeWing(string why)
        {
            if (!_fighting || _fled) return false;
            try
            {
                if (!SingletonMonoBehaviour<CombatBhv>.HasInstance()) return false;
                _fled = true;
                _lastFight = "gone (" + why + ")";
                Plugin.Log.LogInfo("Shrieker: it takes wing (" + why + "): the fight ends");
                SingletonMonoBehaviour<CombatBhv>.Instance.ForceEndCombat(isForceComplete: true);
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Shrieker: the fight could not be ended: " + e);
                return false;
            }
        }

        /// <summary>The hub is back from the fight. True when the fight ended by the Shrieker's leaving.</summary>
        public static bool FightEnded()
        {
            var fled = _fighting && _fled;
            if (_fighting) _lastFight += "; over after round " + _round;
            _fighting = false;
            if (_listening)
            {
                EventManager.RemoveListener<EventBattleStartRound>(OnRound);
                _listening = false;
            }
            return fled;
        }

        /// <summary>What the nest pays for a fight won outright (DD1: monsters/nest, by the quest's level); empty when DD1 names nothing.</summary>
        public static List<LootDraw> NestLoot(int difficulty)
        {
            var draws = new List<LootDraw>();
            try
            {
                var nest = ShriekerRules.NestLoot(new Dd1Files(), difficulty);
                if (nest != null) draws.Add(nest);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Shrieker: the nest's loot could not be read: " + e.Message); }
            return draws;
        }

        // ---- save ----------------------------------------------------------------------------------------

        private static JToken Save()
        {
            var hoard = new JArray();
            foreach (var held in Hoard) hoard.Add(new JObject { ["id"] = held.Id, ["grade"] = held.Grade, ["week"] = held.Week, ["from"] = held.From });
            return new JObject
            {
                ["hoard"] = hoard, ["taken"] = _taken, ["returned"] = _returned, ["fallen"] = TakesFromTheFallen, ["hardest_first"] = HardestQuestFirst,
                ["rng"] = _rng.State.ToString(CultureInfo.InvariantCulture)
            };
        }

        private static void Load(JToken token)
        {
            Reset();
            if (!(token is JObject json)) return;
            foreach (var entry in json["hoard"] as JArray ?? new JArray())
            {
                var id = (string)entry["id"];
                if (string.IsNullOrEmpty(id)) continue;
                Hoard.Add(new Held { Id = id, Grade = (string)entry["grade"] ?? "common", Week = (int?)entry["week"] ?? 0, From = (string)entry["from"] ?? "stores" });
            }
            _taken = (int?)json["taken"] ?? Hoard.Count;
            _returned = (int?)json["returned"] ?? 0;
            TakesFromTheFallen = (bool?)json["fallen"] ?? true;
            HardestQuestFirst = (bool?)json["hardest_first"] ?? false;
            if (ulong.TryParse((string)json["rng"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var state)) _rng = Rng.FromState(state);
        }

        private static void Reset()
        {
            Hoard.Clear();
            _taken = _returned = 0;
            TakesFromTheFallen = true;
            HardestQuestFirst = false;
            _rng = NewRng();
            _fighting = _fled = false;
        }

        // ---- dev bridge ----------------------------------------------------------------------------------

        public static object Describe()
        {
            var rules = Rules;
            var now = QuestNow();
            var offered = QuestBoard.Current().Where(q => IsQuest(PlotQuests.Rules.Find(q.Dd1Quest))).ToList();
            return new
            {
                week = EstateState.Current.Week,
                hoard = Hoard.Select(h => new { id = h.Id, name = Trinkets.Name(h.Id), grade = h.Grade, rank = rules.Rank(h.Grade), week = h.Week, from = h.From }).ToList(),
                taken = _taken, returned = _returned, takesFromTheFallen = TakesFromTheFallen, hardestQuestFirst = HardestQuestFirst,
                stores = Trinkets.Stored().Count,
                questByDd1 = now?.Id,
                prize = now != null ? PrizeOf(now).Select(h => h.Id).ToList() : new List<string>(),
                onOffer = offered.Select(q => new { id = q.Id, name = q.Name, tier = q.Tier, goal = q.Goal, text = q.Intro, reward = QuestBoard.RewardText(q) }).ToList(),
                thief = DescribeEvent(ThiefEvent),
                prizeEvent = DescribeEvent(PrizeEvent),
                dd1 = new
                {
                    rarities = rules.Rarities, escapeTurn = rules.EscapeTurn, turnsPerRound = rules.TurnsPerRound, rounds = rules.Rounds, roundsOverride = RoundsOverride,
                    quests = PlotQuests.Rules.Quests.Where(IsQuest).Select(q => new
                    {
                        id = q.Id, difficulty = q.Difficulty, count = q.RetentionCount, minimumRarity = q.RetentionMinRarity, byEvent = q.GeneratedByEvent, map = q.MapName,
                        nest = ShriekerRules.NestLoot(new Dd1Files(), q.Difficulty)?.Table
                    }).ToList(),
                    missing = rules.Missing
                },
                fight = new { on = _fighting, round = _round, rounds = _rounds, fled = _fled, last = _lastFight }
            };
        }

        private static object DescribeEvent(string id)
        {
            var def = TownEvents.Catalog.Find(id);
            if (def == null) return null;
            return new
            {
                id, title = TownEventText.Title(id), week = def.MinimumWeek, trinketsInStores = def.TrinketsInStorage, chance = def.BaseChance, perLostDraw = def.PerNotRolled,
                cooldown = def.Cooldown, unique = def.Unique, happened = TownEvents.State.Happened.Contains(id), leftOut = TownEvents.Unsupported(def)
            };
        }

        private static void Bridge()
        {
            AgentBridge.Register("shrieker.state", o => Describe());
            // DD1's town event, this week: eight of the stores' trinkets go
            AgentBridge.Register("shrieker.event", o => TownEvents.Force(ThiefEvent) ?? Describe());
            // DD1's other event: "Shrieker's Prize", the quest for one of its own trinkets
            AgentBridge.Register("shrieker.prize", o => TownEvents.Force(PrizeEvent) ?? Describe());
            // the thief without its event: {"count":8}
            AgentBridge.Register("shrieker.steal", o =>
            {
                var taken = Steal((int?)o["count"] ?? 8);
                QuestBoard.SyncEventQuests();
                return new { taken, state = Describe() };
            });
            // the hoard by hand: {"add":"<DD2 trinket id>"} (a trinket the estate does not hold), {"clear":true}
            AgentBridge.Register("shrieker.hoard", o =>
            {
                if ((bool?)o["clear"] == true) Hoard.Clear();
                var id = (string)o["add"];
                if (id != null)
                {
                    if (Trinkets.Find(id) == null) return "no such trinket";
                    if ((string)o["grade"] != null) Trinkets.NoteGrade(id, (string)o["grade"]);
                    Add(id, (string)o["from"] ?? "fallen");
                }
                QuestBoard.SyncEventQuests();
                return Describe();
            });
            // DD1's rule for a lost party's trinkets, on or off: {"on":false}; {"take":["<id>", ...]} hands some over as if a party had been lost
            AgentBridge.Register("shrieker.fallen", o =>
            {
                if (o["on"] != null) TakesFromTheFallen = (bool)o["on"];
                if (o["take"] is JArray ids) TakeFromTheFallen(ids.Select(i => (string)i).ToList());
                QuestBoard.SyncEventQuests();
                return Describe();
            });
            // sets out for the perch (or the Prize) with the party as it stands, without the map and the provision screen
            AgentBridge.Register("shrieker.embark", o => PlotQuests.DevEmbark(q => IsQuest(PlotQuests.Rules.Find(q.Dd1Quest))) ?? (object)"embarked");
            // the quest settled without playing it: won (the trinkets come back, the survivors' quirks) or lost
            AgentBridge.Register("shrieker.win", o => PlotQuests.DevSettle(q => IsQuest(PlotQuests.Rules.Find(q.Dd1Quest)), true, false));
            AgentBridge.Register("shrieker.lose", o => PlotQuests.DevSettle(q => IsQuest(PlotQuests.Rules.Find(q.Dd1Quest)), false, false));
            // in the fight: it takes wing now
            AgentBridge.Register("shrieker.flee", o => TakeWing("the bridge") ? "gone" : (object)"no fight at the perch is on");
            // which quest a hoard brings that fits more than one: {"hardest":true}, or false for DD1's order as read
            AgentBridge.Register("shrieker.order", o =>
            {
                if (o["hardest"] != null) HardestQuestFirst = (bool)o["hardest"];
                QuestBoard.SyncEventQuests();
                return new { hardestQuestFirst = HardestQuestFirst, quest = QuestNow()?.Id };
            });
            // rounds the fight lasts: {"n":2}; 0 is DD1's
            AgentBridge.Register("shrieker.rounds", o =>
            {
                if (o["n"] != null) RoundsOverride = Math.Max(0, (int)o["n"]);
                return new { dd1 = Rules.Rounds, roundsOverride = RoundsOverride };
            });
        }
    }
}
