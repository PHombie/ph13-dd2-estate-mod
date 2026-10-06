using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Assets.Code.Actor;
using Assets.Code.Library;
using Assets.Code.Utils;
using DD2Estate.Core;
using DD2Estate.Dd2;
using DD2Estate.Dungeon;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DD2Estate.Estate
{
    /// <summary>
    /// DD1's Activity Log: what happened in town and on expeditions, week by week. The estate's systems are
    /// not told about the log; it listens to what they already raise (a week advancing, an expedition seen and
    /// ended, a hero leaving the roster, a grave, a building step, the week's results of the Tavern, the Abbey
    /// and the Sanitarium, the wagon's trade, an heirloom trade) and looks twice a second at what raises
    /// nothing (a new face on the roster, a resolve level, the week's town event). Where DD1 has a sentence
    /// for an event the entry is DD1's (<see cref="ActivityLogText"/>); the rest is in the mod's words.
    ///
    /// An entry keeps its text as it is shown (TextMeshPro rich text in DD1's colours), the week it belongs
    /// to and what DD1 draws beside it: the hero's class for the portrait, the building for its sign. Saved
    /// as the "activity_log" section of the estate. None of DD1's files gives the log a length; the mod keeps
    /// <see cref="WeeksKept"/> weeks and <see cref="MaxEntries"/> entries.
    /// </summary>
    [EstateModule]
    internal static class ActivityLog
    {
        public const string Id = "activity_log";
        private const string SectionKey = "activity_log";
        // DD1's estate bar shows the scroll of fx/estate_activity_log (a Spine sprite): rolled up, and unrolled while open.
        private const string IconSheet = "fx/estate_activity_log/estate_activity_log.sprite.png";

        /// <summary>The mod's own numbers: how far back the log goes.</summary>
        public const int WeeksKept = 52;
        public const int MaxEntries = 800;

        /// <summary>What an entry is drawn as (activity_log/*.png).</summary>
        public static class Kinds
        {
            /// <summary>A line of text.</summary>
            public const string Note = "note";
            /// <summary>DD1's banner of a quest's end, the sentence under it.</summary>
            public const string Success = "success", Failure = "failure", Abandon = "abandon";
            /// <summary>A hero in a building: the building's sign, the hero's face, the text.</summary>
            public const string Hero = "hero";
            /// <summary>A resolve level gained.</summary>
            public const string Level = "level";
            /// <summary>A building's upgrade.</summary>
            public const string Building = "building";
            /// <summary>A region's level.</summary>
            public const string Region = "region";
            /// <summary>News of the Darkest Dungeon.</summary>
            public const string Darkest = "darkest";
        }

        public class Entry
        {
            public int Week;
            public string Kind = Kinds.Note;
            /// <summary>TextMeshPro rich text; a line break parts the things that happened to one hero in one place.</summary>
            public string Text = "";
            /// <summary>Class id of the hero shown; null for none.</summary>
            public string Hero;
            /// <summary>Building or region id shown; null for none.</summary>
            public string Building;
        }

        // The expedition under way, as it set out: who went, and what the estate held, so that what it holds
        // when they are back says what they brought.
        private class Memo
        {
            public long Seed;
            public List<string> Names = new List<string>();
            public string Dungeon, QuestType, Title;
            public int Difficulty = 1, Length = 1, RegionLevel;
            public int Gold;
            public Dictionary<string, int> Heirlooms = new Dictionary<string, int>();
            public List<string> Trinkets = new List<string>();
        }

        private class Known
        {
            public string Name, ClassId, Title;
            /// <summary>The class as the roster's entry has it, which is what a grave is marked with.</summary>
            public string RosterClass;
        }

        private static readonly List<Entry> Entries = new List<Entry>();
        private static Memo _expedition;
        private static int _activitiesWeek, _sanitariumWeek;
        private static string _eventKey;
        private static readonly HashSet<string> GoalClasses = new HashSet<string>();

        // What the estate looked like at the last look, to tell what changed. Not saved: the first look after a
        // load or a new estate only takes stock.
        private static bool _stock;
        private static readonly Dictionary<uint, Known> Heroes = new Dictionary<uint, Known>();
        private static readonly Dictionary<uint, int> Levels = new Dictionary<uint, int>();
        private static readonly HashSet<string> SoldWares = new HashSet<string>();
        private static List<string> _stores = new List<string>();
        private static int _gold;
        private static readonly HashSet<Graveyard.Fallen> Buried = new HashSet<Graveyard.Fallen>();
        private static float _nextLook;
        private static JArray _plot;

        /// <summary>Raised when the log has another entry or another week.</summary>
        public static event Action Changed;

        /// <summary>Oldest first.</summary>
        public static IReadOnlyList<Entry> All => Entries;

        private static void Register()
        {
            EstateState.RegisterSection(SectionKey, Save, Load, Reset);
            EstateState.WeekAdvanced += week => Guard("week", () =>
            {
                Trim();
                Raise();
            });
            EstateState.ExpeditionEnded += expedition => Guard("expedition end", () => OnExpeditionEnded(expedition));
            NarrationMoments.ExpeditionSeen += run => Guard("embark", () => OnExpeditionSeen(run));
            NarrationMoments.HeroFell += fallen => Guard("death", () => OnHeroFell(fallen));
            NarrationMoments.Tick += () => Guard("look", Look);
            NarrationMoments.SessionEnded += () => _stock = false;
            RosterLifecycle.Left += guid => Guard("departure", () => OnLeft(guid));
            ActivityLedger.Changed += () => Guard("activities", OnActivities);
            SanitariumLedger.Changed += () => Guard("sanitarium", OnSanitarium);
            UpgradeRules.Built += tree => Guard("upgrade", () => OnBuilt(tree));
            NomadWagon.Changed += () => Guard("wagon", OnWagon);
            HeirloomExchange.Traded += offer => Guard("heirlooms", () =>
                Add(Kinds.Note, ActivityLogText.Building(HeirloomExchange.ScreenName) + " " + HeirloomExchange.Counted(offer.Rate.From, offer.Gives) + " for "
                                + ActivityLogText.Good(HeirloomExchange.Counted(offer.Rate.To, offer.Takes)) + "."));

            // DD1 keeps the log's button at the right end of the estate's bar.
            TownPanelButtons.Add(new TownPanelButtons.Entry
            {
                Id = Id, Art = IconSheet + "#scroll_closed", OpenArt = IconSheet + "#scroll_open", Scale = 0.76f, Size = new Vector2(78f, 68f), Place = 3,
                Name = () => ScreenName, Toggle = ActivityLogPanel.Toggle, IsOpen = () => ActivityLogPanel.IsOpen
            });
        }

        public static string ScreenName => WindowText.Plain("town_name_activity_log") ?? "Activity Log";

        // The systems listened to raise their events without a net: nothing thrown here may reach them.
        private static void Guard(string what, Action action)
        {
            try { action(); }
            catch (Exception e) { Plugin.Log.LogError("Activity log: " + what + " failed: " + e); }
        }

        // ---- entries -------------------------------------------------------------------------------------

        /// <summary>Adds an entry to the present week. For anything else that wants to be on record.</summary>
        public static Entry Add(string kind, string text, string heroClass = null, string building = null)
        {
            var entry = new Entry { Week = EstateState.Current.Week, Kind = kind ?? Kinds.Note, Text = text ?? "", Hero = heroClass, Building = building };
            Entries.Add(entry);
            Plugin.Log.LogInfo("Activity log, week " + entry.Week + ": " + ActivityLogText.Plain(entry.Text).Replace('\n', ' '));
            Trim();
            Raise();
            return entry;
        }

        /// <summary>The weeks on record, newest first; the present week is always among them.</summary>
        public static List<int> Weeks()
        {
            var weeks = new List<int> { EstateState.Current.Week };
            for (var i = Entries.Count - 1; i >= 0; i--)
                if (!weeks.Contains(Entries[i].Week)) weeks.Add(Entries[i].Week);
            weeks.Sort((a, b) => b.CompareTo(a));
            return weeks;
        }

        /// <summary>A week's entries in the order they happened.</summary>
        public static List<Entry> Of(int week) => Entries.Where(e => e.Week == week).ToList();

        private static void Trim()
        {
            var oldest = EstateState.Current.Week - WeeksKept + 1;
            Entries.RemoveAll(e => e.Week < oldest);
            if (Entries.Count > MaxEntries) Entries.RemoveRange(0, Entries.Count - MaxEntries);
        }

        private static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { Plugin.Log.LogError("Activity log: a Changed handler failed: " + e); }
        }

        // ---- save section --------------------------------------------------------------------------------

        private static JToken Save()
        {
            var entries = new JArray();
            foreach (var entry in Entries)
            {
                var json = new JObject { ["w"] = entry.Week, ["k"] = entry.Kind, ["t"] = entry.Text };
                if (entry.Hero != null) json["h"] = entry.Hero;
                if (entry.Building != null) json["b"] = entry.Building;
                entries.Add(json);
            }
            var section = new JObject
            {
                ["entries"] = entries, ["activities_week"] = _activitiesWeek, ["sanitarium_week"] = _sanitariumWeek, ["event"] = _eventKey,
                ["goals"] = new JArray(GoalClasses.OrderBy(c => c, StringComparer.Ordinal))
            };
            if (_expedition != null)
            {
                section["expedition"] = new JObject
                {
                    ["seed"] = _expedition.Seed, ["names"] = new JArray(_expedition.Names), ["dungeon"] = _expedition.Dungeon, ["type"] = _expedition.QuestType,
                    ["title"] = _expedition.Title, ["difficulty"] = _expedition.Difficulty, ["length"] = _expedition.Length, ["region_level"] = _expedition.RegionLevel,
                    ["gold"] = _expedition.Gold, ["heirlooms"] = JObject.FromObject(_expedition.Heirlooms), ["trinkets"] = new JArray(_expedition.Trinkets)
                };
            }
            return section;
        }

        private static void Load(JToken json)
        {
            Reset();
            if (!(json is JObject section)) return;
            foreach (var item in Json.Array(section["entries"]))
            {
                var text = (string)item["t"];
                if (text == null) continue;
                Entries.Add(new Entry { Week = Json.Int(item["w"], 1), Kind = (string)item["k"] ?? Kinds.Note, Text = text, Hero = (string)item["h"], Building = (string)item["b"] });
            }
            _activitiesWeek = Json.Int(section["activities_week"], 0);
            _sanitariumWeek = Json.Int(section["sanitarium_week"], 0);
            _eventKey = (string)section["event"];
            foreach (var cls in Json.Array(section["goals"])) GoalClasses.Add((string)cls);
            if (section["expedition"] is JObject memo)
            {
                _expedition = new Memo
                {
                    Seed = Json.Long(memo["seed"], 0), Dungeon = (string)memo["dungeon"], QuestType = (string)memo["type"], Title = (string)memo["title"],
                    Difficulty = Json.Int(memo["difficulty"], 1), Length = Json.Int(memo["length"], 1), RegionLevel = Json.Int(memo["region_level"], 0),
                    Gold = Json.Int(memo["gold"], 0)
                };
                foreach (var name in Json.Array(memo["names"])) _expedition.Names.Add((string)name);
                foreach (var id in Json.Array(memo["trinkets"])) _expedition.Trinkets.Add((string)id);
                if (memo["heirlooms"] is JObject heirlooms)
                    foreach (var p in heirlooms.Properties()) _expedition.Heirlooms[p.Name] = Json.Int(p.Value, 0);
            }
            Raise();
        }

        private static void Reset()
        {
            Entries.Clear();
            _expedition = null;
            _activitiesWeek = _sanitariumWeek = 0;
            _eventKey = null;
            GoalClasses.Clear();
            _stock = false;
            Raise();
        }

        // ---- expeditions ---------------------------------------------------------------------------------

        private static bool IsDarkest(string dungeon) => dungeon == QuestBoard.DarkestDungeon;

        // DD1's sentences name its own bosses and its own descents; the estate's are other ones.
        private static bool HasDd1Story(Memo memo) => memo.QuestType != "kill_boss" && !IsDarkest(memo.Dungeon);

        // DD1 calls the quest the mod generates as "activate" inventory_activate.
        private static string Dd1Type(string questType) => questType == "activate" ? "inventory_activate" : questType;

        private static string Where(Memo memo)
        {
            var place = DungeonContent.DisplayName(memo.Dungeon);
            return string.IsNullOrEmpty(memo.Title) || memo.Title == place ? place : ActivityLogText.Good(memo.Title) + ", " + place;
        }

        private static string Tail(Memo memo) => " (" + ActivityLogText.Difficulty(memo.Difficulty) + " " + ActivityLogText.Length(memo.Length) + ")";

        private static void OnExpeditionSeen(DungeonRun run)
        {
            var map = run.Exploration.Map;
            // A run with the seed already on record is the same expedition, restored from a save.
            if (_expedition != null && _expedition.Seed == map.Seed) return;
            var memo = new Memo
            {
                Seed = map.Seed, Dungeon = map.DungeonId, QuestType = map.QuestType, Title = run.Title, Length = Math.Max(1, map.Length),
                Difficulty = IsDarkest(map.DungeonId) ? 6 : Math.Max(1, map.Tier), RegionLevel = QuestBoard.DungeonLevel(map.DungeonId),
                Gold = EstateState.Gold, Trinkets = Trinkets.Stored()
            };
            foreach (var hero in Provisioning.Party()) memo.Names.Add(ActivityLedger.HeroName(hero));
            foreach (var kind in EstateState.HeirloomIds) memo.Heirlooms[kind] = EstateState.Current.Heirloom(kind);
            _expedition = memo;

            var names = ActivityLogText.Names(memo.Names);
            var id = "str_embarked_on_" + Dd1Type(memo.QuestType) + "_" + memo.Dungeon;
            var text = HasDd1Story(memo) && ActivityLogText.Has(id)
                ? ActivityLogText.Story(id, null, names, ActivityLogText.Difficulty(memo.Difficulty), ActivityLogText.Length(memo.Length))
                : names + " set out: " + Where(memo) + "." + Tail(memo);
            Add(IsDarkest(memo.Dungeon) ? Kinds.Darkest : Kinds.Note, text, null, memo.Dungeon);
        }

        private static void OnExpeditionEnded(EstateState.Expedition expedition)
        {
            var memo = _expedition;
            _expedition = null;
            var run = DungeonRun.Current;
            if (memo == null)
            {
                // An expedition that set out before the log was kept: what is known now must do.
                memo = new Memo
                {
                    Dungeon = expedition.Dungeon, QuestType = run != null ? run.Exploration.Map.QuestType : null, Title = run != null ? run.Title : null,
                    Difficulty = Math.Max(1, expedition.Difficulty), Length = Math.Max(1, expedition.Length), RegionLevel = int.MaxValue, Gold = int.MaxValue
                };
                var actors = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance;
                foreach (var guid in expedition.Survivors)
                {
                    var hero = actors != null ? actors.GetLibraryElement(guid) : null;
                    if (hero != null) memo.Names.Add(ActivityLedger.HeroName(hero));
                }
            }
            memo.Difficulty = Math.Max(memo.Difficulty, expedition.Difficulty);

            var status = run != null ? run.Exploration.Status : RaidStatus.InProgress;
            if (status == RaidStatus.InProgress) status = expedition.Success ? RaidStatus.Succeeded : RaidStatus.Abandoned;
            var names = ActivityLogText.Names(memo.Names);
            string text;
            if (!expedition.Success && expedition.Survivors.Count == 0 && memo.Names.Count > 0)
                text = ActivityLogText.Story("str_allperished", "%s joined each other in a heroic demise. There were no survivors.", names);
            else
            {
                var id = "str_returned_from_" + Dd1Type(memo.QuestType) + "_" + memo.Dungeon + (expedition.Success ? "_success" : "_failure");
                // DD1's quests of a town event and of the Shrieker have their own sentence, by the quest's id
                var own = PlotQuests.Dd1IdOf(expedition.QuestId) != null ? "str_returned_from_" + expedition.QuestId + (expedition.Success ? "_success" : "_failure") : null;
                if (own != null && ActivityLogText.Has(own))
                    text = ActivityLogText.Story(own, null, names);
                else if (HasDd1Story(memo) && ActivityLogText.Has(id))
                    text = ActivityLogText.Story(id, null, names, ActivityLogText.Difficulty(memo.Difficulty), ActivityLogText.Length(memo.Length));
                else
                    text = names + (expedition.Success ? " came home with the work done: " : status == RaidStatus.Abandoned ? " turned back: " : " were driven out: ") + Where(memo) + "." + Tail(memo);
            }
            var banner = expedition.Success ? Kinds.Success : status == RaidStatus.Abandoned ? Kinds.Abandon : Kinds.Failure;
            if (IsDarkest(memo.Dungeon))
            {
                // DD1 sets the Darkest Dungeon's news in a frame of its own, under the banner.
                Add(banner, "", null, memo.Dungeon);
                Add(Kinds.Darkest, text, null, memo.Dungeon);
            }
            else Add(banner, text, null, memo.Dungeon);

            // from the expedition's own record where it has one: the quest's pay and the bag, as the results
            // screen shows them; else from what the estate holds over what it held
            var haul = expedition.Books != null ? Brought(expedition.Books) : Haul(memo);
            if (haul.Count > 0) Add(Kinds.Note, "Brought home: " + UpgradeText.Join(haul) + ".");

            var level = QuestBoard.DungeonLevel(memo.Dungeon);
            if (expedition.Success && level > memo.RegionLevel)
            {
                var place = DungeonContent.DisplayName(memo.Dungeon);
                var id = "str_" + memo.Dungeon + "_level_" + level + "_unlocked";
                Add(Kinds.Region, ActivityLogText.Has(id) ? ActivityLogText.Story(id, null, place) : ActivityLogText.Good(place + ":") + " region level " + level + " reached.", null, memo.Dungeon);
            }
        }

        // The quest's pay and the bag, added up, from the record the results screen and the purse share.
        private static List<string> Brought(DD2Estate.Dungeon.ExpeditionBooks books)
        {
            var parts = new List<string>();
            if (books.Gold > 0) parts.Add(ActivityLogText.Good(books.Gold + " gold"));
            foreach (var kind in EstateState.HeirloomIds.Concat(books.HeirloomKinds.Where(k => Array.IndexOf(EstateState.HeirloomIds, k) < 0)))
            {
                var count = books.Heirloom(kind);
                if (count > 0) parts.Add(ActivityLogText.Good(HeirloomExchange.Counted(kind, count)));
            }
            foreach (var id in books.QuestTrinkets.Concat(books.HaulTrinkets))
                parts.Add("the trinket " + ActivityLogText.Good(Trinkets.Name(id)));
            return parts;
        }

        // What the estate holds now over what it held when the party set out: the quest's pay and the bag. For an
        // expedition that ended without a record of its own.
        private static List<string> Haul(Memo memo)
        {
            var parts = new List<string>();
            var gold = EstateState.Gold - memo.Gold;
            if (gold > 0) parts.Add(ActivityLogText.Good(gold + " gold"));
            foreach (var kind in EstateState.HeirloomIds)
            {
                if (!memo.Heirlooms.TryGetValue(kind, out var before)) continue;
                var more = EstateState.Current.Heirloom(kind) - before;
                if (more > 0) parts.Add(ActivityLogText.Good(HeirloomExchange.Counted(kind, more)));
            }
            if (memo.Gold != int.MaxValue)
            {
                var had = new List<string>(memo.Trinkets);
                foreach (var id in Trinkets.Stored())
                    if (!had.Remove(id)) parts.Add("the trinket " + ActivityLogText.Good(Trinkets.Name(id)));
            }
            return parts;
        }

        // ---- the roster ----------------------------------------------------------------------------------

        private static void OnHeroFell(Graveyard.Fallen fallen)
        {
            var name = ActivityLogText.Hero(fallen.Name);
            string text;
            if (string.IsNullOrEmpty(fallen.Where) || fallen.Where == "the Hamlet") text = name + " died in the Hamlet.";
            else
            {
                text = ActivityLogText.Story("str_perished", "%s met their final fate during the quest.", name);
                // the grave's own words for who dealt the blow (GraveyardPanel)
                var by = WindowText.Plain("str_death_attack_monster") ?? "was slain by a vile";
                if (by.StartsWith("was ", StringComparison.Ordinal)) by = by.Substring(4);
                text += " " + ActivityLogText.Bad("(" + fallen.Where + (string.IsNullOrEmpty(fallen.By) ? "" : ", " + by + " " + fallen.By) + ")");
            }
            Add(Kinds.Hero, text, fallen.ClassId, Graveyard.BuildingId);
        }

        // A hero left the roster: dismissed, or buried. The grave tells the two apart.
        private static void OnLeft(uint guid)
        {
            Levels.Remove(guid);
            if (!Heroes.TryGetValue(guid, out var hero)) return;
            Heroes.Remove(guid);
            foreach (var fallen in Graveyard.All)
            {
                if (fallen.Name != hero.Name || fallen.ClassId != hero.RosterClass || Buried.Contains(fallen)) continue;
                Buried.Add(fallen);
                return;
            }
            if (!_stock) return;
            Add(Kinds.Hero, ActivityLogText.Building(ActivityText.Building(StageCoachId)) + " " + ActivityLogText.Hero(hero.Name) + " the " + hero.Title + " was dismissed.", hero.ClassId, StageCoachId);
        }

        private const string StageCoachId = "stage_coach";

        // ---- what raises nothing: looked at twice a second -----------------------------------------------

        private static void Look()
        {
            if (Time.unscaledTime < _nextLook && _stock) return;
            _nextLook = Time.unscaledTime + 0.5f;
            var actors = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance;
            if (actors == null) return;
            var first = !_stock;
            if (first)
            {
                Heroes.Clear();
                Levels.Clear();
                Buried.Clear();
                foreach (var fallen in Graveyard.All) Buried.Add(fallen);
            }

            foreach (var guid in RosterLifecycle.LivingGuids())
            {
                var actor = actors.GetLibraryElement(guid);
                if (actor == null) continue;
                var name = ActivityLedger.HeroName(actor);
                if (!Heroes.TryGetValue(guid, out var hero))
                {
                    Heroes[guid] = hero = new Known();
                    if (!first)
                    {
                        hero.Name = name;
                        Add(Kinds.Hero, ActivityLogText.Building(ActivityText.Building(StageCoachId)) + " " + ActivityLogText.Hero(name) + " the " + HeroNames.Title(actor) + " has joined the estate.",
                            actor.ActorDataId, StageCoachId);
                    }
                }
                hero.Name = name;
                hero.ClassId = actor.ActorDataId;
                hero.Title = HeroNames.Title(actor);
                hero.RosterClass = RosterLifecycle.Roster?.GetReadOnlyRosterEntryByActorGuid(guid)?.ActorClassId ?? actor.ActorDataId;

                var level = Resolve.Level(guid);
                if (Levels.TryGetValue(guid, out var before) && level > before && !first)
                {
                    Add(Kinds.Level, ActivityLogText.Story("str_is_now_a",
                        "{colour_start|notable}%s{colour_end} is now {colour_start|town_activity_log_positive_result}%s %s (Lvl. %d){colour_end}.",
                        name, ActivityLogText.ResolveName(level), HeroNames.ClassName(actor), level), actor.ActorDataId);
                }
                Levels[guid] = level;
                if (level >= Resolve.MaxLevel) GoalClasses.Add(actor.ActorDataId);
            }

            var active = TownEvents.Current;
            if (active != null)
            {
                var key = active.Week + ":" + active.Id;
                if (key != _eventKey)
                {
                    _eventKey = key;
                    Add(Kinds.Note, ActivityLogText.Story("str_town_event_started", "{colour_start|town_activity_log_positive_result}Town Event: %s{colour_end}", TownEventText.Title(active.Id)));
                }
            }

            // only in the hamlet: asking for the week's wares rolls them, and that is the wagon's to decide when
            if (EstateSession.InHub && EstateSession.View == EstateSession.Screen.Hamlet) TakeStock();
            _stock = true;
        }

        // ---- the Tavern and the Abbey --------------------------------------------------------------------

        private static readonly Regex Relief = new Regex(@"^stress (\d+) to (\d+)\.\s*");
        private static readonly Regex NoRelief = new Regex(@"^had no stress to shed\.\s*");
        private static readonly Regex Missing = new Regex(@"^Wandered off: missing for (\d+) weeks?\.\s*");
        private static readonly Regex Quirk = new Regex(@"^Came away with a quirk: (.+)\.\s*$");
        private static readonly Regex Lost = new Regex(@"^Cost the estate another (\d+) gold\.\s*");
        private static readonly Regex Won = new Regex(@"^Brought (\d+) gold home\.\s*");
        private const string Stays = "Refuses to leave and stays another week.";
        private const string Back = " has found the way back to the hamlet.";

        private static string ClassOf(string heroName)
        {
            foreach (var hero in Heroes.Values)
                if (hero.Name == heroName) return hero.ClassId;
            return null;
        }

        // The ledger keeps the week's results as lines of its own wording ("Dismas, Bar: stress 5 to 3. Came
        // away with a quirk: Tippler."). What a line says is put in DD1's sentence for it; a line that reads
        // otherwise than expected goes into the log as the ledger wrote it.
        private static void OnActivities()
        {
            var week = EstateState.Current.Week;
            if (week == _activitiesWeek) return;
            foreach (var building in ActivityRules.BuildingIds)
            {
                foreach (var line in ActivityLedger.ResultsFor(building))
                {
                    _activitiesWeek = week;
                    AddActivity(building, line);
                }
            }
        }

        private static void AddActivity(string building, string line)
        {
            var place = ActivityText.Building(building);
            foreach (var activity in ActivityRules.For(building))
            {
                var mark = ", " + ActivityText.Activity(activity.Id) + ": ";
                var at = line.IndexOf(mark, StringComparison.Ordinal);
                if (at <= 0) continue;
                var who = line.Substring(0, at);
                var rest = line.Substring(at + mark.Length);
                var hero = ActivityLogText.Hero(who);
                var lead = ActivityLogText.Building(place) + " " + hero;
                var texts = new List<string>();
                Match m;
                if ((m = Relief.Match(rest)).Success)
                {
                    var relieved = Math.Max(0, int.Parse(m.Groups[1].Value) - int.Parse(m.Groups[2].Value));
                    texts.Add(ActivityLogText.Story("str_" + activity.Id + "_stress_relief_story",
                        "{colour_start|town_activity_log_building_name}%s:{colour_end} {colour_start|notable}%s{colour_end} recovered {colour_start|town_activity_log_positive_result}%d{colour_end} {colour_start|stressNOT1}stress{colour_end}.",
                        place, who, relieved));
                    rest = rest.Substring(m.Length);
                }
                else if ((m = NoRelief.Match(rest)).Success)
                {
                    texts.Add(lead + " had no stress to shed.");
                    rest = rest.Substring(m.Length);
                }

                const string storyHead = "{colour_start|town_activity_log_building_name}%s:{colour_end} {colour_start|notable}%s{colour_end} ";
                if (rest.StartsWith(Stays, StringComparison.Ordinal))
                {
                    texts.Add(ActivityLogText.Story("str_" + activity.Id + "_activity_lock_story", storyHead + "refuses to leave yet.", place, who));
                    rest = rest.Substring(Stays.Length).Trim();
                }
                else if ((m = Missing.Match(rest)).Success)
                {
                    texts.Add(ActivityLogText.Story("str_" + activity.Id + "_go_missing_story", storyHead + "hasn't been seen since the previous evening.", place, who));
                    rest = rest.Substring(m.Length);
                }
                else if ((m = Quirk.Match(rest)).Success)
                {
                    // DD1 has a sentence per quirk; which DD1 quirk it was the ledger's line does not say.
                    texts.Add(lead + " came away changed. New Quirk: " + m.Groups[1].Value);
                    rest = "";
                }
                else if ((m = Lost.Match(rest)).Success)
                {
                    texts.Add(ActivityLogText.Story("str_" + activity.Id + "_currency_lost_story",
                        storyHead + "{colour_start|town_activity_log_negative_result}lost %d gold{colour_end}.", place, who, int.Parse(m.Groups[1].Value)));
                    rest = rest.Substring(m.Length);
                }
                else if ((m = Won.Match(rest)).Success)
                {
                    texts.Add(ActivityLogText.Story("str_" + activity.Id + "_currency_gained_story",
                        storyHead + "{colour_start|town_activity_log_positive_result}gained %d gold{colour_end}.", place, who, int.Parse(m.Groups[1].Value)));
                    rest = rest.Substring(m.Length);
                }
                if (rest.Trim().Length > 0) texts.Add(lead + ": " + rest.Trim());
                Add(Kinds.Hero, string.Join("\n", texts), ClassOf(who), building);
                return;
            }

            if (line.EndsWith(Back, StringComparison.Ordinal))
            {
                var who = line.Substring(0, line.Length - Back.Length);
                Add(Kinds.Hero, ActivityLogText.Building(place) + " " + ActivityLogText.Hero(who) + Back, ClassOf(who), building);
                return;
            }
            Add(Kinds.Note, ActivityLogText.Building(place) + " " + line, null, building);
        }

        // ---- the Sanitarium ------------------------------------------------------------------------------

        private static readonly Regex NothingLeft = new Regex(@"^there was nothing left to treat\. (\d+) gold returned\.$");
        private static readonly Regex DidNotTake = new Regex(@"^the treatment of (.+) did not take\.$");
        private static readonly Regex LockedIn = new Regex(@"^(.+) is locked in\.$");
        private static readonly Regex Cured = new Regex(@"^cured of (.+?)\.(?: The cure took (.+) with it\.)?$");
        private static readonly Regex Gone = new Regex(@"^(.+) is gone\.$");

        private static void OnSanitarium()
        {
            var week = EstateState.Current.Week;
            if (week == _sanitariumWeek) return;
            foreach (var line in SanitariumLedger.LastResults())
            {
                _sanitariumWeek = week;
                AddTreatment(line);
            }
        }

        private static void AddTreatment(string line)
        {
            var place = ActivityText.Building(SanitariumRules.Building);
            const string head = "{colour_start|town_activity_log_building_name}%s:{colour_end} {colour_start|notable}%s{colour_end} ";
            foreach (var ward in SanitariumRules.Wards)
            {
                var mark = ", " + ActivityText.Activity(ward.Id) + ": ";
                var at = line.IndexOf(mark, StringComparison.Ordinal);
                if (at <= 0) continue;
                var who = line.Substring(0, at);
                var rest = line.Substring(at + mark.Length).Trim();
                var lead = ActivityLogText.Building(place) + " " + ActivityLogText.Hero(who);
                string text;
                Match m;
                if ((m = NothingLeft.Match(rest)).Success) text = lead + " had nothing left to treat. " + m.Groups[1].Value + " gold returned.";
                else if ((m = DidNotTake.Match(rest)).Success) text = lead + ActivityLogText.Bad(" underwent treatment to no effect: ") + m.Groups[1].Value;
                else if ((m = LockedIn.Match(rest)).Success)
                    text = ActivityLogText.Story("str_lock_quirk_story", head + "had reinforcement therapy to make a quirk permanent: %s", place, who, m.Groups[1].Value);
                else if ((m = Cured.Match(rest)).Success)
                {
                    text = ActivityLogText.Story("str_disease_treatment_remove_negative_quirk", head + "underwent effective disease treatment. Disease Cured: %s", place, who, m.Groups[1].Value);
                    if (m.Groups[2].Success)
                        text += "\n" + ActivityLogText.Story("str_disease_treatment_removed_diseases_crit_story", head + "underwent effective disease treatment: Diseases Cured. %s", place, who, m.Groups[2].Value);
                }
                else if ((m = Gone.Match(rest)).Success)
                    text = ActivityLogText.Story("str_treatment_remove_negative_quirk", head + "underwent effective quirk treatment. Quirk Removed: %s", place, who, m.Groups[1].Value);
                else text = lead + ": " + rest;
                Add(Kinds.Hero, text, ClassOf(who), SanitariumRules.Building);
                return;
            }
            Add(Kinds.Note, ActivityLogText.Building(place) + " " + line, null, SanitariumRules.Building);
        }

        // ---- buildings and trade -------------------------------------------------------------------------

        private static void OnBuilt(UpgradeRules.Tree tree)
        {
            if (tree == null) return;
            var level = UpgradeRules.Level(tree);
            // DD1's sentence of the tree's step ("str_tavern_bar_upgrade_lvl_2"); the mod's own trees have none.
            var id = "str_" + tree.Id.Replace('.', '_') + "_upgrade_lvl_" + level;
            var text = ActivityLogText.Has(id)
                ? ActivityLogText.Story(id, null, level)
                : ActivityLogText.Building(ActivityText.Building(tree.Building)) + " " + UpgradeText.TreeName(tree.Id) + ". " + ActivityLogText.Good("Level: " + level);
            Add(Kinds.Building, text, null, tree.Building);
        }

        // What the wagon has sold and what the estate holds, as of now.
        private static void TakeStock()
        {
            SoldWares.Clear();
            foreach (var ware in NomadWagon.Current())
                if (ware.Sold) SoldWares.Add(ware.Id);
            _stores = Trinkets.Stored();
            _gold = EstateState.Gold;
        }

        // The wagon says that something changed, not what: a ware newly marked sold was bought, a trinket gone
        // from the stores with gold come in was sold.
        private static void OnWagon()
        {
            if (!_stock || !EstateSession.InHub || EstateSession.View != EstateSession.Screen.Hamlet) return;
            var wagon = ActivityLogText.Building(ActivityText.Building(NomadWagon.BuildingId));
            var gold = EstateState.Gold;
            var bought = NomadWagon.Current().Where(w => w.Sold && !SoldWares.Contains(w.Id)).ToList();
            foreach (var ware in bought)
            {
                var price = bought.Count == 1 && _gold > gold ? _gold - gold : NomadWagon.Price(ware);
                Add(Kinds.Note, wagon + " " + ActivityLogText.Good(Trinkets.Name(ware.Id)) + " bought for " + price + " gold.", null, NomadWagon.BuildingId);
            }
            if (bought.Count == 0 && gold > _gold)
            {
                // what is no longer in the stores and on no hero either (one put on a moment ago was not sold)
                var left = new List<string>(Trinkets.Stored());
                foreach (var guid in RosterLifecycle.LivingGuids())
                    foreach (var id in RealmInventory.Worn(guid))
                        if (id != null) left.Add(id);
                var gone = _stores.Where(id => !left.Remove(id)).ToList();
                foreach (var id in gone)
                    Add(Kinds.Note, wagon + " " + Trinkets.Name(id) + " sold for " + ActivityLogText.Good((gone.Count == 1 ? gold - _gold : Trinkets.SellPrice(id)) + " gold") + ".", null, NomadWagon.BuildingId);
            }
            TakeStock();
        }

        // ---- the caretaker's goals -----------------------------------------------------------------------

        public class Goal
        {
            public string Text;
            public bool Done;
        }

        private static JArray Plot
        {
            get
            {
                if (_plot != null) return _plot;
                using (var stream = typeof(ActivityLog).Assembly.GetManifestResourceStream("DD2Estate.Data.plot_quests.json"))
                using (var reader = new StreamReader(stream))
                    _plot = JObject.Parse(reader.ReadToEnd())["quests"] as JArray ?? new JArray();
                return _plot;
            }
        }

        /// <summary>DD1's quest goals: the estate's story quests, done or still to do.</summary>
        public static List<Goal> QuestGoals()
        {
            var goals = new List<Goal>();
            var done = EstateState.Current.CompletedQuests;
            foreach (var quest in Plot)
            {
                var text = (string)quest["goal_text"];
                if (string.IsNullOrEmpty(text)) text = (string)quest["name"];
                if (!string.IsNullOrEmpty(text)) goals.Add(new Goal { Text = text, Done = done.Contains((string)quest["id"]) });
            }
            return goals;
        }

        /// <summary>DD1's roster goals: a hero of every class raised to the last resolve level.</summary>
        public static List<Goal> RosterGoals()
        {
            var goals = new List<Goal>();
            foreach (var cls in RosterLifecycle.AvailableClasses())
                goals.Add(new Goal { Text = WindowText.Format("str_caretaker_goal_hero_resolve", "Raise a %s to Resolve Level 6", HeroNames.ClassName(cls)), Done = GoalClasses.Contains(cls) });
            return goals;
        }

        // ---- dev bridge ----------------------------------------------------------------------------------

        public static object Describe(int weeks = 0)
        {
            var list = new List<object>();
            var shown = 0;
            foreach (var week in Weeks())
            {
                if (weeks > 0 && shown++ >= weeks) break;
                var entries = new List<object>();
                foreach (var entry in Of(week)) entries.Add(new { kind = entry.Kind, text = ActivityLogText.Plain(entry.Text), hero = entry.Hero, building = entry.Building });
                list.Add(new { week, entries });
            }
            return new
            {
                week = EstateState.Current.Week, entries = Entries.Count, weeksKept = WeeksKept, maxEntries = MaxEntries, stockTaken = _stock,
                expedition = _expedition != null ? new { seed = _expedition.Seed, names = _expedition.Names, dungeon = _expedition.Dungeon, title = _expedition.Title } : null,
                activitiesWeek = _activitiesWeek, sanitariumWeek = _sanitariumWeek, townEvent = _eventKey, weeks = list
            };
        }

        /// <summary>For tests: empties the log (the weeks already answered for stay answered).</summary>
        public static void Clear()
        {
            Entries.Clear();
            Raise();
        }
    }
}
