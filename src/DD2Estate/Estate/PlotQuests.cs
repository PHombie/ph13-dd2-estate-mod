using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Assets.Code.Actor;
using Assets.Code.Quirk;
using Assets.Code.Source;
using DD2Estate.Core;
using DD2Estate.Dd1;
using DD2Estate.Dd2;
using DD2Estate.Dev;
using DD2Estate.Dungeon;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Estate
{
    /// <summary>
    /// DD1's plot quests outside its story (<see cref="PlotEventRules"/>): the ones a town event brings for its
    /// week (the Brigand Incursion's "Wolves at the Door", "Shrieker's Prize") and the one the Shrieker's hoard
    /// brings ("Shrieker's Perch", <see cref="Shrieker"/>). They are DD1's own quests under DD1's own ids, read
    /// from the player's install: name, text, goal, reward, map, the rules of leaving and of what follows.
    ///
    /// What happens with them, and where the rule comes from:
    ///   on the Estate Map     an event's quest is on offer in the week of its event [exe 0x93204b]; the hoard's
    ///                         quest while the hoard brings one (<see cref="ShriekerRules.QuestFor"/>);
    ///   before setting out    DD1's own questions: for the event's quest, and for any other quest in that week
    ///                         ("By ignoring the Brigand Incursion event ...": town_quest_select_*_confirm);
    ///   the expedition        on DD1's hand-made map (<see cref="FixedMap"/>, maps/&lt;map_name&gt;.dm);
    ///   an expedition ends    every quest of the week's event the party did not set out on is IGNORED
    ///                         [exe 0x92d260] and takes its <c>upgrade_tags_to_remove_on_ignore</c>
    ///                         (<see cref="UpgradeRemoval"/>, exe 0x94ad00); a plot quest not completed takes
    ///                         its <c>upgrade_tags_to_remove_on_failure</c> [exe 0x7b6160] (the Incursion: none);
    ///   its survivors         one quirk each from the quest's lists (<c>party_quirks_to_apply_on_*</c>).
    ///
    /// The fights are DD2's (Data/dungeons.json: the town's pools and its boss "vvulf", the wanderer "shrieker").
    /// Saved as the estate's section "plot_quests". Bridge: plotquests.state, incursion.*; the Shrieker's
    /// commands are in <see cref="Shrieker"/>.
    /// </summary>
    [EstateModule]
    internal static class PlotQuests
    {
        private const string SectionKey = "plot_quests";
        public const string IncursionEvent = "plot_quest_town_invasion_0";
        public const string IncursionQuest = "plot_town_invasion_0";
        private const string QuirkLibrary = "shared/quirk/quirk_library.json";

        /// <summary>Upgrade steps a quest took, for the notice, the log and the bridge.</summary>
        private class Loss
        {
            public int Week;
            public string Quest, Why;
            public List<UpgradeStepLost> Steps = new List<UpgradeStepLost>();
        }

        private static PlotEventRules _rules;
        private static List<UpgradeTreeTags> _trees;
        private static Dictionary<string, bool> _quirkSigns;
        private static Rng _rng = NewRng();
        private static readonly List<Loss> Losses = new List<Loss>();
        // "week:quest" of the event quests already settled (set out on, or ignored), so that none is settled twice
        private static readonly HashSet<string> Settled = new HashSet<string>();
        private static string _lastMap = "";

        private static void Register()
        {
            EstateState.RegisterSection(SectionKey, Save, Load, Reset);
            EstateState.ExpeditionEnded += OnExpeditionEnded;
            Bridge();
        }

        private static Rng NewRng() => new Rng(DateTime.Now.Ticks ^ 0x564C5546L);

        // ---- DD1's data ----------------------------------------------------------------------------------

        public static PlotEventRules Rules
        {
            get
            {
                if (_rules != null) return _rules;
                try { _rules = PlotEventRules.Load(new Dd1Files()); }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Plot quests: DD1's quests could not be read: " + e.Message);
                    _rules = PlotEventRules.Parse(null);
                }
                return _rules;
            }
        }

        private static List<UpgradeTreeTags> Trees
        {
            get
            {
                if (_trees != null) return _trees;
                try { _trees = UpgradeRemoval.LoadTrees(new Dd1Files()); }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Plot quests: DD1's upgrade trees could not be read: " + e.Message);
                    _trees = new List<UpgradeTreeTags>();
                }
                return _trees;
            }
        }

        /// <summary>DD1's quest behind one of the board's quests; null for a quest that is not one of these.</summary>
        public static PlotEventQuest Find(Quest quest)
        {
            return quest != null && quest.Plot && quest.Dd1Quest != null && quest.Id == quest.Dd1Quest ? Rules.Find(quest.Id) : null;
        }

        public static bool IsOurs(Quest quest) => Find(quest) != null;

        /// <summary>DD1's id of one of these quests by its id or its name on the board; null for any other.</summary>
        public static string Dd1IdOf(string idOrName)
        {
            if (string.IsNullOrEmpty(idOrName)) return null;
            foreach (var plot in Rules.Quests)
            {
                if (!plot.GeneratedByEvent && plot.RetentionCount <= 0) continue;
                if (plot.Id == idOrName || Name(plot) == idOrName) return plot.Id;
            }
            return null;
        }

        // The mod's boss or wanderer that stands for the quest's monster (Data/dungeons.json).
        private static string BossOf(PlotEventQuest plot)
        {
            if (Shrieker.IsQuest(plot)) return DungeonContent.Wanderer(ShriekerRules.Monster, plot.Dungeon);
            return DungeonContent.BossOfDd1Quest(plot.Dungeon, plot.Id);
        }

        /// <summary>Why one of DD1's quests cannot be put on the board right now; null when it can.</summary>
        public static string Unstageable(string plotId)
        {
            var plot = Rules.Find(plotId);
            if (plot == null) return "DD1 has no quest " + plotId;
            if (!plot.GeneratedByEvent && plot.RetentionCount <= 0) return plotId + " is a quest of DD1's story";
            if (plot.Type != QuestTypes.KillBoss) return "quest type " + plot.Type + " is not built for an event's quest";
            if (!DungeonContent.Knows(plot.Dungeon)) return "the mod has no dungeon " + plot.Dungeon;
            if (BossOf(plot) == null) return "the mod has no fight for " + (plot.GoalMonster ?? plotId);
            // READING: DD1 keeps a finished quest that is not repeatable off the board for good [exe 0x931d53];
            // that its event then stays away too is read from the event's own test of the same list [0x9492e0]
            if (!plot.Repeatable && EstateState.Current.CompletedQuests.Contains(plot.Id)) return "done once (DD1: not repeatable)";
            if (!DungeonContent.NeedsQuest(plot.Dungeon) && !QuestBoard.IsOpen(plot.Dungeon)) return "the " + DungeonContent.DisplayName(plot.Dungeon) + " is not open yet";
            return null;
        }

        // ---- DD1's words ---------------------------------------------------------------------------------

        private static string Text(string id)
        {
            var text = Dd1Strings.Get(id);
            return text != null ? Dd1Strings.Plain(Dd1Strings.Format(text)) : null;
        }

        public static string Name(PlotEventQuest plot) => Text("town_quest_name_" + plot.Id) ?? TownEventText.Words(plot.Id);

        // DD1's sentence of a kill goal: "Kill 1 Brigand Vvulf."
        private static string Goal(PlotEventQuest plot)
        {
            var monster = plot.GoalMonster;
            if (monster == null) return null;
            var name = Text("str_monstername_" + monster) ?? TownEventText.Words(monster);
            return Dd1Strings.Plain(Dd1Strings.Format(Dd1Strings.Get("town_quest_goal_start_plural_kill_monster") ?? "Kill %d %s.", 1, name));
        }

        // The hoard's trinkets came from the stores (the thief) or from the fallen: DD1 words the two apart.
        private static bool FromStores(PlotEventQuest plot)
        {
            return plot.RetentionCount > 0 && Shrieker.PrizeOf(plot).Any(h => h.From == "stores");
        }

        /// <summary>DD1's line under a region's bar while one of these quests is on offer there; null for none.</summary>
        public static string RegionLine(string dungeonId)
        {
            foreach (var quest in QuestBoard.Current())
            {
                var plot = Find(quest);
                if (plot == null || quest.Dungeon != dungeonId) continue;
                var letter = plot.Difficulty >= 5 ? "2" : plot.Difficulty >= 3 ? "1" : "0";
                var text = plot.RetentionCount > 0
                    ? Text("town_quest_progress_plot_trinket_retention_" + (FromStores(plot) ? "add_from_storage_" : "") + letter)
                    : Text("town_quest_progress_" + plot.Id);
                if (text != null) return text;
            }
            return null;
        }

        /// <summary>
        /// DD1's questions before the party goes on from the Estate Map in a week whose town event has a quest:
        /// its warning for that quest, or its warning for leaving that quest alone.
        /// </summary>
        public static List<string> EmbarkQuestions(Quest selected)
        {
            var questions = new List<string>();
            try
            {
                foreach (var plotId in OnOfferByEvent())
                {
                    var ours = selected != null && selected.Id == plotId;
                    var text = Text("town_quest_select_" + (ours ? "" : "not_") + "town_event_plot_quest_" + plotId + "_confirm");
                    if (text != null) questions.Add(text);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Plot quests: the embark questions could not be put together: " + e.Message); }
            return questions;
        }

        /// <summary>DD1's question for abandoning a quest that costs a hero, by its dungeon ("retreat_raid_party_kill_town_..."); null: none of its own.</summary>
        public static string RetreatQuestion(string dungeonId, out string yes, out string no)
        {
            yes = Text("retreat_raid_party_kill_" + dungeonId + "_confirm_yes") ?? Text("retreat_raid_party_kill_" + dungeonId + "_confirm_answer_yes");
            no = Text("retreat_raid_party_kill_" + dungeonId + "_confirm_no") ?? Text("retreat_raid_party_kill_" + dungeonId + "_confirm_answer_no");
            return Text("retreat_raid_party_kill_" + dungeonId + "_confirm_question");
        }

        // ---- the board -----------------------------------------------------------------------------------

        // The quests of the week's town event that can be staged and have not been settled this week.
        private static List<string> OnOfferByEvent()
        {
            var ids = new List<string>();
            var active = TownEvents.Current;
            if (active == null) return ids;
            foreach (var effect in TownEvents.Effects(active, PlotEventRules.EventEffect))
            {
                var plot = Rules.Find(effect.Text);
                if (plot == null || !plot.GeneratedByEvent || Unstageable(plot.Id) != null) continue;
                if (Settled.Contains(active.Week.ToString(CultureInfo.InvariantCulture) + ":" + plot.Id)) continue;
                ids.Add(plot.Id);
            }
            return ids;
        }

        /// <summary>
        /// DD1's quests of this kind that are on offer now, without their reward (the board pays them as it
        /// pays every plot quest, then calls <see cref="Paid"/>).
        /// </summary>
        public static List<Quest> Offers()
        {
            var quests = new List<Quest>();
            try
            {
                foreach (var id in OnOfferByEvent()) quests.Add(Make(Rules.Find(id)));
                var perch = Shrieker.QuestNow();
                if (perch != null && Unstageable(perch.Id) == null) quests.Add(Make(perch));
            }
            catch (Exception e) { Plugin.Log.LogError("Plot quests: the offers could not be put together: " + e); }
            return quests;
        }

        private static Quest Make(PlotEventQuest plot)
        {
            return new Quest
            {
                Id = plot.Id, Dd1Quest = plot.Id, Plot = true, Dungeon = plot.Dungeon, Type = plot.Type, Boss = BossOf(plot),
                Tier = QuestBoard.TierOf(plot.Difficulty), LengthName = plot.Length >= 3 ? "long" : plot.Length == 2 ? "medium" : "short",
                Name = Name(plot), Goal = Goal(plot), Intro = Text("town_quest_description_" + plot.Id)
            };
        }

        /// <summary>
        /// After the board has given the quest DD1's reward: a quest to the perch also gives back the hoard's
        /// rarest trinkets, which DD1 shows as the quest's reward, and says how many.
        /// </summary>
        public static void Paid(Quest quest)
        {
            var plot = Find(quest);
            if (plot == null || plot.RetentionCount <= 0) return;
            var prize = Shrieker.PrizeOf(plot);
            foreach (var held in prize) quest.Trinkets.Add(new QuestTrinket { Rarity = held.Grade, Item = held.Id });
            var line = Dd1Strings.Format(Dd1Strings.Get("town_quest_trinket_retention_description_format") ?? "Defeat the Shrieker to recover %d of your lost trinkets.", prize.Count);
            quest.Intro = string.IsNullOrEmpty(quest.Intro) ? line : quest.Intro + "\n\n" + Dd1Strings.Plain(line);
        }

        /// <summary>
        /// The quest is done and about to be paid: what it gives back leaves the hoard first, so that the
        /// estate may hold it again (a trinket in the hoard counts as the estate's).
        /// </summary>
        public static void Completing(Quest quest)
        {
            var plot = Find(quest);
            if (plot == null || plot.RetentionCount <= 0) return;
            var released = Shrieker.Release(quest.Trinkets.Where(t => t.Item != null).Select(t => t.Item).ToList());
            Plugin.Log.LogInfo("Plot quests: " + quest.Id + " done: " + released + " trinket(s) leave the Shrieker's hoard for the stores");
        }

        // ---- the expedition ------------------------------------------------------------------------------

        /// <summary>
        /// DD1's hand-made map of the quest as an expedition's map; null for a quest without one (the caller
        /// generates). A map that cannot be read is replaced by the plainest one that holds the quest's fight.
        /// </summary>
        public static DungeonMap MapFor(Quest quest, int tier, long seed)
        {
            var plot = Find(quest);
            if (plot?.MapName == null) return null;
            var file = Dd1Map.FileOf(plot.MapName);
            try
            {
                var bytes = Dd1Install.ReadBytes(file) ?? throw new Exception("the DD1 install has no " + file);
                var notes = new FixedMapNotes();
                var props = GenerationRules.Load(new Dd1Files()).PropsFor(plot.Dungeon);
                var map = FixedMap.Build(Dd1Map.Read(bytes), plot.Dungeon, plot.Type, tier, seed, props, notes);
                _lastMap = file + ": " + map.Rooms.Count + " rooms, " + map.Hallways.Count + " hallways" + (notes.LeftOut.Count > 0 ? "; left out: " + string.Join("; ", notes.LeftOut) : "");
                Plugin.Log.LogInfo("Plot quests: " + quest.Id + " on DD1's map " + _lastMap);
                return map;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Plot quests: DD1's map " + file + " could not be used (" + e.Message + "); a plain one stands in");
                _lastMap = file + ": NOT READ (" + e.Message + "), a plain map stands in";
                return PlainMap(plot, tier, seed);
            }
        }

        // FALLBACK, only when DD1's own map cannot be read: the boss alone (a map of one room), or the boss
        // behind one hallway with a fight in it.
        private static DungeonMap PlainMap(PlotEventQuest plot, int tier, long seed)
        {
            var map = new DungeonMap { DungeonId = plot.Dungeon, QuestType = plot.Type, Length = 1, Tier = tier, Seed = seed, Width = 2, Height = 1 };
            var boss = new Room { Id = 0, Type = RoomType.Boss, Battle = new EncounterSlot { Kind = EncounterKind.Boss, Tier = tier }, Wall = "entrance" };
            if (!Shrieker.IsQuest(plot))
            {
                map.Rooms.Add(new Room { Id = 0, X = 0, Y = 0, Type = RoomType.Entrance, Wall = "entrance" });
                boss.Id = 1;
                boss.X = 1;
                boss.Wall = null;
                var hall = new Hallway { Id = 0, RoomA = 0, RoomB = 1 };
                hall.Segments.Add(new Segment());
                hall.Segments.Add(new Segment { Content = HallContent.Battle, Battle = new EncounterSlot { Kind = EncounterKind.Hallway, Tier = tier } });
                hall.Segments.Add(new Segment());
                map.Hallways.Add(hall);
            }
            map.Rooms.Add(boss);
            map.EntranceId = 0;
            map.FinalRoomId = boss.Id;
            map.Objective = new QuestObjective { Kind = ObjectiveKind.KillBoss, Required = 1 };
            return map;
        }

        // ---- what a quest's end brings its survivors -----------------------------------------------------

        // DD1's quirk library says which of the quests' quirks are good ones.
        private static bool? Positive(string dd1Quirk)
        {
            if (_quirkSigns == null)
            {
                _quirkSigns = new Dictionary<string, bool>();
                try
                {
                    var wanted = new HashSet<string>(Rules.Quests.SelectMany(q => q.QuirksOnCompletion.Concat(q.QuirksOnFailure)).SelectMany(l => l.Quirks));
                    foreach (var entry in Json.Array(Json.ParseFile(Dd1Install.ReadText(QuirkLibrary))?["quirks"]))
                    {
                        var id = (string)entry["id"];
                        if (id != null && wanted.Contains(id)) _quirkSigns[id] = Json.Bool(entry["is_positive"], false);
                    }
                }
                catch (Exception e) { Plugin.Log.LogWarning("Plot quests: DD1's quirk library could not be read: " + e.Message); }
            }
            return dd1Quirk != null && _quirkSigns.TryGetValue(dd1Quirk, out var positive) ? positive : (bool?)null;
        }

        /// <summary>
        /// DD1's <c>party_quirks_to_apply_on_completion</c> / <c>_on_failure</c> for one living hero of the
        /// party: one draw from the quest's lines by their weights. DD2 has none of the Shrieker's quirks
        /// ("Corvid's Eye", "Corvid's Appetite" ...): a good one drawn gives a random good quirk of DD2's, a
        /// bad one a random bad one (the stand-in is the mod's; which was drawn is DD1's, and is logged).
        /// GUESS: every survivor draws once (the file gives the lines and their weights, and its line without
        /// a quirk weighs nothing; who draws is in the exe).
        /// </summary>
        public static List<QuestAftermath.Gain> QuirksFor(Quest quest, ActorInstance hero, bool completed, Rng rng)
        {
            var gains = new List<QuestAftermath.Gain>();
            var plot = Find(quest);
            var lines = plot == null ? null : completed ? plot.QuirksOnCompletion : plot.QuirksOnFailure;
            if (lines == null || lines.Count == 0 || hero == null || !hero.IsLiving) return gains;
            try
            {
                var quirks = hero.QuirkContainer;
                if (quirks == null || !quirks.IsEnabled) return gains;
                foreach (var dd1 in PlotEventRules.DrawQuirks(lines, rng))
                {
                    var positive = Positive(dd1);
                    if (positive == null) continue;
                    var before = new HashSet<string>(quirks.GetIds());
                    quirks.AddRandomQuirks(null, positive.Value ? QuirkDefinition.QUIRK_POSITIVE_TAG : QuirkDefinition.QUIRK_NEGATIVE_TAG, 1, SourceType.QUIRK, null, 0u);
                    foreach (var quirk in quirks.GetInstances())
                    {
                        var definition = quirk?.Definition;
                        if (definition == null || before.Contains(definition.Id)) continue;
                        gains.Add(QuestAftermath.Describe(definition, hero));
                    }
                    Plugin.Log.LogInfo("Plot quests: " + hero.ActorName + " comes away from " + quest.Id + " with DD1's " + dd1 + " (" + (positive.Value ? "good" : "bad") + "): "
                                       + (gains.Count > 0 ? string.Join(", ", gains.Select(g => g.Id)) : "nothing DD2 had left to give"));
                }
            }
            catch (Exception e) { Plugin.Log.LogError("Plot quests: " + quest.Id + ": a quirk could not be given: " + e); }
            return gains;
        }

        // ---- ignored, failed -----------------------------------------------------------------------------

        private static void OnExpeditionEnded(EstateState.Expedition expedition)
        {
            if (!EstateSession.Active) return;
            try
            {
                // DD1 [exe 0x92d260]: as the party comes home, every quest of the week's event that it did not set out on
                foreach (var plotId in OnOfferByEvent())
                {
                    if (expedition.QuestId == plotId) Settle(plotId);
                    else Ignore(Rules.Find(plotId));
                }
                // DD1 [exe 0x7b6160]: a plot quest not completed
                var own = Rules.Find(expedition.QuestId);
                if (own != null && !expedition.Success) Remove(own, own.RemoveOnFailure, "failed");
            }
            catch (Exception e) { Plugin.Log.LogError("Plot quests: the expedition's end could not be settled: " + e); }
        }

        private static string Key(string plotId) => EstateState.Current.Week.ToString(CultureInfo.InvariantCulture) + ":" + plotId;

        private static void Settle(string plotId) => Settled.Add(Key(plotId));

        /// <summary>The week's event quest was left alone: what DD1 takes for it. Returns the steps lost.</summary>
        public static List<UpgradeStepLost> Ignore(PlotEventQuest plot)
        {
            if (plot == null || Settled.Contains(Key(plot.Id))) return new List<UpgradeStepLost>();
            Settle(plot.Id);
            Plugin.Log.LogInfo("Plot quests: " + plot.Id + " was ignored");
            return Remove(plot, plot.RemoveOnIgnore, "ignored");
        }

        // DD1 [exe 0x94ad00]: for each tag, that many trees lose their last step; an entry of the Activity Log says which.
        private static List<UpgradeStepLost> Remove(PlotEventQuest plot, List<UpgradeTagCount> tags, string why)
        {
            var lost = new List<UpgradeStepLost>();
            if (tags == null || tags.Count == 0) return lost;
            foreach (var tag in tags)
            {
                foreach (var step in UpgradeRemoval.Pick(Trees, UpgradeRules.Has, tag.Tag, tag.Amount, _rng))
                {
                    var tree = UpgradeRules.Find(step.Tree);
                    if (tree == null) continue;
                    Flatten(tree.Building);
                    var level = UpgradeRules.Level(tree);
                    if (level <= 0) continue;
                    EstateState.Current.Buildings[tree.Id] = level - 1;
                    lost.Add(step);
                }
            }
            Losses.Add(new Loss { Week = EstateState.Current.Week, Quest = plot.Id, Why = why, Steps = lost });
            if (Losses.Count > 12) Losses.RemoveAt(0);
            Plugin.Log.LogInfo("Plot quests: " + plot.Id + " " + why + ": " + (lost.Count > 0 ? string.Join(", ", lost) + " no longer built" : "nothing was built that it could take"));
            if (lost.Count == 0) return lost;
            UpgradeRules.RaiseChanged();
            try
            {
                // DD1's own entry: "Town Event: Quest failed <event>", then "<upgrade> upgrade destroyed" for each
                var eventName = TownEventText.Title(EventOf(plot.Id) ?? plot.Id);
                var lines = new List<string> { ActivityLogText.Story("str_town_event_upgrade_destroyed", "{colour_start|town_activity_log_positive_result}Town Event: Quest failed %s{colour_end}", eventName) };
                foreach (var step in lost)
                    lines.Add(ActivityLogText.Story("str_town_event_upgrade_destroyed_per_upgrade", "%s upgrade destroyed", UpgradeText.TreeName(step.Tree) + " " + UpgradeText.Roman(Index(step) + 1)));
                ActivityLog.Add(ActivityLog.Kinds.Building, string.Join("\n", lines), null, UpgradeRules.Find(lost[0].Tree)?.Building);
                // and a word in the hamlet, the mod's own: DD1 leaves it to the log and the buildings' looks
                QuestBoard.Report(TownEventText.Title(EventOf(plot.Id) ?? plot.Id) + ": " + UpgradeText.Join(lost.Select(s => UpgradeText.TreeName(s.Tree)).ToList())
                                  + (lost.Count == 1 ? " has" : " have") + " lost an upgrade to the raiders.");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Plot quests: the loss could not be put on record: " + e.Message); }
            return lost;
        }

        private static int Index(UpgradeStepLost step)
        {
            var tree = UpgradeRules.Find(step.Tree);
            return tree != null ? Math.Max(0, tree.IndexOf(step.Code)) : 0;
        }

        // A test shortcut stores one number under a building's bare id for all its trees (UpgradeRules.Stored):
        // before a single tree loses a step, each tree is given its own number.
        private static void Flatten(string building)
        {
            var state = EstateState.Current;
            if (building == null || state.BuildingLevel(building) <= 0) return;
            foreach (var tree in UpgradeRules.TreesOf(building)) state.Buildings[tree.Id] = UpgradeRules.Level(tree);
            state.Buildings.Remove(building);
        }

        // The town event that names a quest.
        private static string EventOf(string plotId)
        {
            foreach (var def in TownEvents.Catalog.Events)
                foreach (var effect in def.EffectsOf(PlotEventRules.EventEffect))
                    if (effect.Text == plotId) return def.Id;
            return null;
        }

        /// <summary>The event that names a quest arrives: DD1's line of the Ancestor's for it, where it has one.</summary>
        public static void Arrived(string eventId, string plotId)
        {
            var line = Dd1Strings.Parts("str_vo_town_event_" + eventId);
            if (line != null) Narration.Say(line, Narration.Scope.Hamlet, "town event");
            QuestBoard.SyncEventQuests();
        }

        // ---- save ----------------------------------------------------------------------------------------

        private static JToken Save()
        {
            var losses = new JArray();
            foreach (var loss in Losses)
                losses.Add(new JObject
                {
                    ["week"] = loss.Week, ["quest"] = loss.Quest, ["why"] = loss.Why,
                    ["steps"] = new JArray(loss.Steps.Select(s => new JObject { ["tree"] = s.Tree, ["code"] = s.Code }))
                });
            return new JObject { ["settled"] = new JArray(Settled.OrderBy(s => s, StringComparer.Ordinal)), ["losses"] = losses, ["rng"] = _rng.State.ToString(CultureInfo.InvariantCulture) };
        }

        private static void Load(JToken token)
        {
            Reset();
            if (!(token is JObject json)) return;
            foreach (var key in json["settled"] as JArray ?? new JArray())
                if ((string)key != null) Settled.Add((string)key);
            foreach (var entry in json["losses"] as JArray ?? new JArray())
            {
                var loss = new Loss { Week = (int?)entry["week"] ?? 0, Quest = (string)entry["quest"], Why = (string)entry["why"] };
                foreach (var step in entry["steps"] as JArray ?? new JArray())
                    if ((string)step["tree"] != null) loss.Steps.Add(new UpgradeStepLost { Tree = (string)step["tree"], Code = (string)step["code"] });
                Losses.Add(loss);
            }
            if (ulong.TryParse((string)json["rng"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var state)) _rng = Rng.FromState(state);
            // only this week's keys matter
            var week = EstateState.Current.Week.ToString(CultureInfo.InvariantCulture) + ":";
            Settled.RemoveWhere(key => !key.StartsWith(week, StringComparison.Ordinal));
        }

        private static void Reset()
        {
            Settled.Clear();
            Losses.Clear();
            _rng = NewRng();
        }

        // ---- dev bridge ----------------------------------------------------------------------------------

        private static int IndexOnBoard(Func<Quest, bool> which)
        {
            var offers = QuestBoard.Current();
            for (var i = 0; i < offers.Count; i++)
                if (IsOurs(offers[i]) && which(offers[i])) return i;
            return -1;
        }

        /// <summary>Dev bridge: sets out on one of these quests with the party as it stands and a free standard kit. Null, or why not.</summary>
        public static string DevEmbark(Func<Quest, bool> which)
        {
            QuestBoard.SyncEventQuests();
            var index = IndexOnBoard(which);
            if (index < 0) return "no such quest is on offer";
            if (EstateSession.PartySize == 0) return "nobody is in the party";
            return QuestPanel.EmbarkOffer(index, true) ? null : "could not embark";
        }

        /// <summary>
        /// Dev bridge: one of these quests settled without playing it. Won: its reward is paid (the hoard's
        /// trinkets come back), the party's living heroes draw their quirks, the quest leaves the board. Lost:
        /// what its failure costs, the quirks of a failure, and with <paramref name="retreat"/> DD1's price of
        /// abandoning it (a hero of the party). The week does not turn (roster.week does that).
        /// </summary>
        public static object DevSettle(Func<Quest, bool> which, bool won, bool retreat)
        {
            if (!EstateSession.Active) return "no estate session";
            QuestBoard.SyncEventQuests();
            var index = IndexOnBoard(which);
            if (index < 0) return "no such quest is on offer";
            var quest = QuestBoard.Current()[index];
            var plot = Find(quest);
            var party = Provisioning.Party();
            var fallen = !won && retreat ? QuestBoard.PayForRetreat(quest, party.Select(h => h.ActorGuid).ToList()) : new List<string>();
            var cards = QuestBoard.Finished(quest, won);
            var quirks = new List<object>();
            foreach (var hero in party)
                foreach (var gain in QuirksFor(quest, hero, won, _rng)) quirks.Add(new { hero = hero.ActorName, quirk = gain.Id, name = gain.Name, kind = gain.Kind.ToString() });
            Settle(quest.Id);
            var lost = won ? new List<UpgradeStepLost>() : Remove(plot, plot.RemoveOnFailure, "failed");
            QuestBoard.SyncEventQuests();
            if (EstateSession.InHub) EstatePersistence.SaveNow("plot quest settled");
            return new
            {
                quest = quest.Id, won,
                reward = cards.Select(c => new { kind = c.Kind, id = c.Id, rarity = c.Rarity, amount = c.Amount, given = c.Given }).ToList(),
                quirks, fallen, upgradesLost = lost.Select(s => s.ToString()).ToList(),
                stillOnOffer = QuestBoard.Current().Any(q => q.Id == quest.Id)
            };
        }

        public static object Describe()
        {
            var active = TownEvents.Current;
            return new
            {
                week = EstateState.Current.Week,
                townEvent = active?.Id,
                eventQuests = OnOfferByEvent(),
                onOffer = QuestBoard.Current().Where(IsOurs).Select(q => new
                {
                    id = q.Id, name = q.Name, dungeon = q.Dungeon, tier = q.Tier, difficulty = q.Difficulty, boss = q.Boss, goal = q.Goal, text = q.Intro,
                    reward = QuestBoard.RewardText(q), resolveXp = q.ResolveXp, canRetreat = q.CanRetreat, retreatDeaths = q.RetreatDeaths,
                    questions = EmbarkQuestions(q), regionLine = RegionLine(q.Dungeon)
                }).ToList(),
                askedBeforeAnyOtherQuest = EmbarkQuestions(null),
                dd1 = Rules.Quests.Where(q => q.GeneratedByEvent || q.RetentionCount > 0).Select(q => new
                {
                    id = q.Id, dungeon = q.Dungeon, difficulty = q.Difficulty, map = q.MapName, goal = q.GoalMonster, byEvent = q.GeneratedByEvent, repeatable = q.Repeatable,
                    retreatAlwaysFromRaid = q.RetreatAlwaysFromRaid, retreatDeaths = q.RetreatDeaths,
                    onIgnore = q.RemoveOnIgnore.Select(t => t.Tag + " x" + t.Amount).ToList(), onFailure = q.RemoveOnFailure.Select(t => t.Tag + " x" + t.Amount).ToList(),
                    hoardCount = q.RetentionCount, hoardMinimumRarity = q.RetentionMinRarity, fight = BossOf(q), leftOut = Unstageable(q.Id)
                }).ToList(),
                settledThisWeek = Settled.OrderBy(s => s, StringComparer.Ordinal).ToList(),
                losses = Losses.Select(l => new { week = l.Week, quest = l.Quest, why = l.Why, steps = l.Steps.Select(s => s.ToString()).ToList() }).ToList(),
                lastMap = _lastMap
            };
        }

        private static object DescribeIncursion()
        {
            var def = TownEvents.Catalog.Find(IncursionEvent);
            var plot = Rules.Find(IncursionQuest);
            string config = null, arena = null;
            var boss = plot != null ? BossOf(plot) : null;
            try
            {
                if (boss != null) DungeonContent.PickBoss(plot.Dungeon, boss, QuestBoard.TierOf(plot.Difficulty), out config, out arena);
            }
            catch (Exception e) { config = "(not known outside a session: " + e.Message + ")"; }
            var onIgnore = plot != null ? plot.RemoveOnIgnore : new List<UpgradeTagCount>();
            return new
            {
                week = EstateState.Current.Week,
                thisWeek = TownEvents.Current?.Id == IncursionEvent,
                dd1Event = def == null ? null : new
                {
                    id = def.Id, title = TownEventText.Title(def.Id), text = TownEventText.Description(def.Id), week = def.MinimumWeek,
                    heroes = def.HeroLevels.Select(l => l.Count + " of level " + l.Level).ToList(), chance = def.BaseChance, perLostDraw = def.PerNotRolled, cooldown = def.Cooldown,
                    happened = TownEvents.State.Happened.Contains(def.Id), leftOut = TownEvents.Unsupported(def)
                },
                quest = QuestBoard.Current().Where(q => q.Id == IncursionQuest).Select(q => new
                {
                    name = q.Name, goal = q.Goal, text = q.Intro, tier = q.Tier, reward = QuestBoard.RewardText(q), resolveXp = q.ResolveXp, retreatDeaths = q.RetreatDeaths,
                    question = EmbarkQuestions(q)
                }).FirstOrDefault(),
                questionBeforeAnyOtherQuest = EmbarkQuestions(null),
                fight = new { boss, config, arena },
                ifIgnoredNow = onIgnore.Select(t => new
                {
                    tag = t.Tag, amount = t.Amount,
                    oneOfEachOf = UpgradeRemoval.Candidates(Trees, UpgradeRules.Has, t.Tag).Select(s => s.ToString()).ToList()
                }).ToList(),
                done = EstateState.Current.CompletedQuests.Contains(IncursionQuest),
                losses = Losses.Where(l => l.Quest == IncursionQuest).Select(l => new { week = l.Week, why = l.Why, steps = l.Steps.Select(s => s.ToString()).ToList() }).ToList(),
                lastMap = _lastMap
            };
        }

        private static void Bridge()
        {
            AgentBridge.Register("plotquests.state", o => Describe());
            AgentBridge.Register("incursion.state", o => DescribeIncursion());
            // DD1's town event, this week, whatever the rules say (its week, its four heroes of level 5)
            AgentBridge.Register("incursion.force", o => TownEvents.Force(IncursionEvent) ?? DescribeIncursion());
            // DD1's map as it was read, without setting out: {"quest":"plot_trinket_retention_0"} for another quest's
            AgentBridge.Register("incursion.map", o =>
            {
                var plot = Rules.Find((string)o["quest"] ?? IncursionQuest);
                if (plot == null) return "DD1 has no such quest";
                var map = MapFor(Make(plot), plot.Difficulty >= 5 ? 5 : plot.Difficulty, 1);
                return new { read = _lastMap, map = map?.ToJson() };
            });
            // the event's quest left alone: what an expedition to anywhere else would cost, now
            AgentBridge.Register("incursion.ignore", o =>
            {
                var plot = Rules.Find(IncursionQuest);
                if (plot == null) return "DD1 has no such quest";
                if ((bool?)o["again"] == true) Settled.Remove(Key(plot.Id));
                var lost = Ignore(plot);
                QuestBoard.SyncEventQuests();
                if (EstateSession.InHub) EstatePersistence.SaveNow("incursion ignored");
                return new { lost = lost.Select(s => s.ToString()).ToList(), state = DescribeIncursion() };
            });
            // gives back the steps taken last (an undo for looking twice)
            AgentBridge.Register("incursion.rebuild", o =>
            {
                var loss = Losses.LastOrDefault(l => l.Steps.Count > 0);
                if (loss == null) return "nothing was taken";
                foreach (var step in loss.Steps)
                {
                    var tree = UpgradeRules.Find(step.Tree);
                    if (tree != null) EstateState.Current.Buildings[tree.Id] = Math.Min(tree.Steps.Count, UpgradeRules.Level(tree) + 1);
                }
                Losses.Remove(loss);
                UpgradeRules.RaiseChanged();
                return DescribeIncursion();
            });
            AgentBridge.Register("incursion.embark", o => DevEmbark(q => q.Id == IncursionQuest) ?? (object)"embarked");
            // settled without playing: won (Vvulf's trophy, the quest done for good), or lost; {"retreat":true} also takes DD1's price of abandoning it
            AgentBridge.Register("incursion.win", o => DevSettle(q => q.Id == IncursionQuest, true, false));
            AgentBridge.Register("incursion.lose", o => DevSettle(q => q.Id == IncursionQuest, false, (bool?)o["retreat"] ?? false));
            // the quest may come again (DD1: once done, never): forgets that it was done
            AgentBridge.Register("incursion.reset", o =>
            {
                EstateState.Current.CompletedQuests.Remove(IncursionQuest);
                Settled.Clear();
                QuestBoard.SyncEventQuests();
                return DescribeIncursion();
            });
        }
    }
}
