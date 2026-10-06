using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Core
{
    /// <summary>One entry of an event's <c>data</c> list: what the event does (<c>type</c>, <c>string_data</c>, <c>number_data</c>).</summary>
    public sealed class TownEventEffect
    {
        public string Type;
        public string Text;
        public double Number;
    }

    /// <summary>A building upgrade step an event waits for (<c>requirements.upgrades_purchased</c>).</summary>
    public sealed class TownEventUpgrade
    {
        public string Tree;
        public string Code;
    }

    /// <summary>"At least <see cref="Count"/> heroes of resolve level <see cref="Level"/>" (<c>requirements.hero_level_counts</c>).</summary>
    public sealed class TownEventLevelCount
    {
        public int Level;
        public int Count;
    }

    /// <summary>One town event as DD1 writes it in <c>campaign/town_events/*.town_events.events.json</c>.</summary>
    public sealed class TownEventDef
    {
        public string Id;
        /// <summary>The file the event was read from, e.g. "base.town_events.events.json".</summary>
        public string Source;
        /// <summary>DD1's <c>base_chance</c>: the event's weight in the draw.</summary>
        public double BaseChance;
        /// <summary>DD1's <c>per_not_rolled_additional_chance</c>: weight added for every draw the event lost.</summary>
        public double PerNotRolled;
        /// <summary>Town visits the event stays away after it happened.</summary>
        public int Cooldown;
        /// <summary>DD1's <c>is_unique</c>: once per campaign.</summary>
        public bool Unique;
        /// <summary>DD1's <c>priority: higher</c>: drawn before the events without it.</summary>
        public bool HigherPriority;
        /// <summary>DD1's <c>priority</c> as written ("high", "higher", "highest"); empty for none.</summary>
        public string Priority = "";
        public int MinimumWeek;
        public int DeadHeroes;
        public int TrinketsInStorage;
        /// <summary>DD1's <c>minimum_number_of_district_buildings</c>: district buildings the campaign must have on offer (the districts' own event: 1).</summary>
        public int DistrictBuildings;
        /// <summary>The folder of a DD1 feature the event's file lies in ("dlc/.../features/districts/"; empty for the base game): its picture lies under the same folder.</summary>
        public string Root = "";
        public readonly List<TownEventLevelCount> HeroLevels = new List<TownEventLevelCount>();
        public readonly List<TownEventUpgrade> Upgrades = new List<TownEventUpgrade>();
        /// <summary>"good", "neutral" or "bad": the frame DD1 draws around the notice.</summary>
        public string Tone;
        /// <summary>Spine effect DD1 hangs on a building while the event lasts (not drawn by the mod).</summary>
        public string Sprite, SpriteAttachment;
        public readonly List<TownEventEffect> Effects = new List<TownEventEffect>();

        public IEnumerable<TownEventEffect> EffectsOf(string type) => Effects.Where(e => e.Type == type);

        /// <summary>
        /// An event with a priority and no weight at all (<c>base_chance</c> 0, nothing per lost draw) can never
        /// win a draw: it is DD1's way of scripting an event, which comes when its requirements are met (the
        /// districts' "Cornerstones" in week 10; the Crimson Court's chain is written the same way).
        /// </summary>
        public bool Scripted => Priority.Length > 0 && BaseChance <= 0 && PerNotRolled <= 0;

        /// <summary>"highest" before "higher" before "high".</summary>
        public int PriorityRank => Priority == "highest" ? 3 : Priority == "higher" ? 2 : Priority.Length > 0 ? 1 : 0;
    }

    /// <summary>A buff DD1's events name (<c>shared/buffs/base.buffs.json</c>): what it changes, by how much and where.</summary>
    public sealed class TownEventBuff
    {
        public string Id;
        /// <summary>e.g. "resolve_xp_bonus_percent", "combat_stat_multiply", "stress_heal_received_percent", "resolve_check_percent".</summary>
        public string Stat;
        /// <summary>e.g. "damage_low"; empty for stats without one.</summary>
        public string SubStat;
        public double Amount;
        /// <summary>"always", "in_dungeon", "in_activity".</summary>
        public string Rule;
        /// <summary>The dungeon or the activity the rule names.</summary>
        public string RuleText;
    }

    /// <summary>
    /// DD1's town event data, read from the player's install: the events, the chance of an event by town visits
    /// since the last one, the events certain quests guarantee, and the buffs the events name.
    /// </summary>
    public sealed class TownEventCatalog
    {
        public const string Folder = "campaign/town_events";
        public const string SettingsFile = Folder + "/town_events.settings.json";
        public const string GuaranteesFile = Folder + "/town_events.quest_type_event_guarantees.json";
        public const string BuffsFile = "shared/buffs/base.buffs.json";
        private const string EventFiles = "*.town_events.events.json";
        private const string BuffPrefix = "town_event_";

        /// <summary>
        /// The event files of the base campaign that are read, by their prefix. "arena." belongs to the Butcher's
        /// Circus (its events send the player to the arena), "shared_dlc." is empty; the DLC folders have their
        /// own files and are not looked at.
        /// </summary>
        public static readonly string[] CampaignFiles = { "base.", "mode." };

        /// <summary>
        /// Event files of DD1 features that are read although they lie in a DLC folder. The Districts ship
        /// inside the Crimson Court's folder but are a feature of their own (DD1 asks "Enable Districts?" by
        /// itself); their one event opens them. An install without the file has no such event.
        /// </summary>
        public static readonly string[] FeatureFiles = { DistrictRules.Feature + "campaign/town_events/districts.town_events.events.json" };

        public readonly List<TownEventDef> Events = new List<TownEventDef>();
        /// <summary>Event files that were read, and those that were left out with the reason.</summary>
        public readonly List<string> Sources = new List<string>();
        public readonly List<string> SkippedSources = new List<string>();

        private readonly Dictionary<string, TownEventDef> _byId = new Dictionary<string, TownEventDef>();
        private readonly Dictionary<string, double[]> _settings = new Dictionary<string, double[]>();
        // "dungeon|quest type" to event id
        private readonly Dictionary<string, string> _guarantees = new Dictionary<string, string>();
        private readonly Dictionary<string, TownEventBuff> _buffs = new Dictionary<string, TownEventBuff>();

        public bool IsEmpty => Events.Count == 0;

        public IEnumerable<string> SettingIds => _settings.Keys;

        public IEnumerable<KeyValuePair<string, string>> Guarantees => _guarantees;

        public static TownEventCatalog Load(IDd1Files files)
        {
            var catalog = new TownEventCatalog();
            var names = files.List(Folder, EventFiles);
            Array.Sort(names, StringComparer.Ordinal);
            foreach (var name in names)
            {
                if (!CampaignFiles.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
                {
                    catalog.SkippedSources.Add(name + (name.StartsWith("arena.", StringComparison.Ordinal) ? " (Butcher's Circus)" : " (not a base campaign file)"));
                    continue;
                }
                catalog.AddEvents(files.ReadText(Folder + "/" + name), name);
            }
            foreach (var file in FeatureFiles)
            {
                var text = files.ReadText(file);
                var campaign = file.IndexOf("campaign/", StringComparison.Ordinal);
                if (text != null) catalog.AddEvents(text, file.Substring(file.LastIndexOf('/') + 1), campaign > 0 ? file.Substring(0, campaign) : "");
            }
            catalog.AddSettings(files.ReadText(SettingsFile));
            catalog.AddGuarantees(files.ReadText(GuaranteesFile));
            // Only the buffs the events name are kept: the file holds two thousand.
            if (catalog.Events.Any(e => e.Effects.Any(x => x.Text != null && x.Text.StartsWith(BuffPrefix, StringComparison.Ordinal))))
                catalog.AddBuffs(files.ReadText(BuffsFile));
            return catalog;
        }

        /// <summary>A catalog from JSON texts as DD1 writes them (the tests build their own events this way).</summary>
        public static TownEventCatalog Parse(string eventsJson, string settingsJson, string guaranteesJson = null, string buffsJson = null)
        {
            var catalog = new TownEventCatalog();
            catalog.AddEvents(eventsJson, "inline");
            catalog.AddSettings(settingsJson);
            catalog.AddGuarantees(guaranteesJson);
            catalog.AddBuffs(buffsJson);
            return catalog;
        }

        public TownEventDef Find(string id)
        {
            return id != null && _byId.TryGetValue(id, out var def) ? def : null;
        }

        /// <summary>
        /// DD1's <c>event_chance_per_town_visits</c> of a frequency setting ("normal", "plentiful", "off"); a
        /// setting the file does not have means no events.
        /// </summary>
        public double[] Chances(string setting)
        {
            return setting != null && _settings.TryGetValue(setting, out var chances) ? chances : new double[0];
        }

        /// <summary>The event DD1 promises for finishing a quest of this type in this dungeon; null for none.</summary>
        public string GuaranteeFor(string dungeon, string questType)
        {
            if (dungeon == null || questType == null) return null;
            return _guarantees.TryGetValue(dungeon + "|" + questType, out var id) ? id : null;
        }

        public TownEventBuff Buff(string id)
        {
            return id != null && _buffs.TryGetValue(id, out var buff) ? buff : null;
        }

        private void AddEvents(string text, string source, string root = "")
        {
            var json = Json.ParseFile(text);
            if (json == null) return;
            Sources.Add(source);
            foreach (var entry in Json.Array(json["events"]))
            {
                var id = (string)entry["id"];
                if (string.IsNullOrEmpty(id) || _byId.ContainsKey(id)) continue;
                var def = new TownEventDef
                {
                    Id = id,
                    Source = source,
                    BaseChance = Json.Number(entry["base_chance"], 0),
                    PerNotRolled = Json.Number(entry["per_not_rolled_additional_chance"], 0),
                    Cooldown = Math.Max(0, Json.Int(entry["cooldown"], 0)),
                    Unique = Json.Bool(entry["is_unique"], false),
                    HigherPriority = (string)entry["priority"] == "higher",
                    Priority = (string)entry["priority"] ?? "",
                    Root = root,
                    Tone = (string)entry["tone"] ?? "neutral",
                    Sprite = (string)entry["sprite"] ?? "",
                    SpriteAttachment = (string)entry["sprite_attachment"] ?? ""
                };
                var needs = entry["requirements"];
                if (needs != null && needs.Type == JTokenType.Object)
                {
                    def.MinimumWeek = Json.Int(needs["minimum_week"], 0);
                    def.DeadHeroes = Json.Int(needs["dead_heroes"], 0);
                    def.TrinketsInStorage = Json.Int(needs["trinket_storage_count"], 0);
                    def.DistrictBuildings = Json.Int(needs["minimum_number_of_district_buildings"], 0);
                    foreach (var level in Json.Array(needs["hero_level_counts"]))
                        def.HeroLevels.Add(new TownEventLevelCount { Level = Json.Int(level["level"], 0), Count = Json.Int(level["count"], 0) });
                    foreach (var upgrade in Json.Array(needs["upgrades_purchased"]))
                    {
                        var tree = (string)upgrade["tree_id"];
                        var code = (string)upgrade["requirement_code"];
                        if (tree != null && code != null) def.Upgrades.Add(new TownEventUpgrade { Tree = tree, Code = code });
                    }
                }
                foreach (var data in Json.Array(entry["data"]))
                {
                    var type = (string)data["type"];
                    if (type == null) continue;
                    def.Effects.Add(new TownEventEffect { Type = type, Text = (string)data["string_data"] ?? "", Number = Json.Number(data["number_data"], 0) });
                }
                _byId[id] = def;
                Events.Add(def);
            }
        }

        private void AddSettings(string text)
        {
            var json = Json.ParseFile(text);
            if (json == null) return;
            foreach (var setting in Json.Array(json["settings"]))
            {
                var id = (string)setting["id"];
                if (id == null) continue;
                _settings[id] = Json.Array(setting["event_chance_per_town_visits"]).Select(c => Json.Number(c, 0)).ToArray();
            }
        }

        private void AddGuarantees(string text)
        {
            var json = Json.ParseFile(text);
            if (json == null) return;
            foreach (var entry in Json.Array(json["quest_type_event_guarantees"]))
            {
                string dungeon = (string)entry["dungeon_type"], quest = (string)entry["quest_type"], id = (string)entry["event_id"];
                if (dungeon != null && quest != null && id != null) _guarantees[dungeon + "|" + quest] = id;
            }
        }

        private void AddBuffs(string text)
        {
            var json = Json.ParseFile(text);
            if (json == null) return;
            foreach (var entry in Json.Array(json["buffs"]))
            {
                var id = (string)entry["id"];
                if (id == null || !id.StartsWith(BuffPrefix, StringComparison.Ordinal)) continue;
                _buffs[id] = new TownEventBuff
                {
                    Id = id,
                    Stat = (string)entry["stat_type"] ?? "",
                    SubStat = (string)entry["stat_sub_type"] ?? "",
                    Amount = Json.Number(entry["amount"], 0),
                    Rule = (string)entry["rule_type"] ?? "always",
                    RuleText = (string)entry.SelectToken("rule_data.string") ?? ""
                };
            }
        }
    }

    /// <summary>What the estate looks like when the party comes home: everything DD1's requirements ask about.</summary>
    public sealed class TownEventSituation
    {
        /// <summary>The week that begins with this town visit.</summary>
        public int Week;
        /// <summary>Heroes in the graveyard.</summary>
        public int DeadHeroes;
        /// <summary>Resolve levels of the living heroes.</summary>
        public IList<int> HeroLevels = new List<int>();
        /// <summary>Whether a step of a building's upgrade tree is built: (tree id, requirement code).</summary>
        public Func<string, string, bool> HasUpgrade = (tree, code) => false;
        public int TrinketsInStorage;
        /// <summary>District buildings the campaign has on offer: 0 on an estate without DD1's districts.</summary>
        public int DistrictBuildings;

        /// <summary>The quest the party came back from: DD1 dungeon id and quest type; null when there was none.</summary>
        public string Dungeon, QuestType;
        public bool QuestSucceeded;
    }

    /// <summary>What a campaign remembers about its town events between visits; saved with the estate.</summary>
    public sealed class TownEventState
    {
        /// <summary>Town visits in a row that brought no event: the index into DD1's chance table.</summary>
        public int VisitsWithoutEvent;
        /// <summary>Town visits an event still stays away.</summary>
        public readonly Dictionary<string, int> Cooldowns = new Dictionary<string, int>();
        /// <summary>Draws an event took part in and lost since it last happened.</summary>
        public readonly Dictionary<string, int> NotRolled = new Dictionary<string, int>();
        /// <summary>Events that happened at least once (what <c>is_unique</c> asks about).</summary>
        public readonly HashSet<string> Happened = new HashSet<string>();

        public JObject ToJson()
        {
            return new JObject
            {
                ["visits"] = VisitsWithoutEvent,
                ["cooldowns"] = JObject.FromObject(Cooldowns),
                ["not_rolled"] = JObject.FromObject(NotRolled),
                ["happened"] = new JArray(Happened.OrderBy(id => id, StringComparer.Ordinal))
            };
        }

        public static TownEventState FromJson(JToken json)
        {
            var state = new TownEventState();
            if (json == null || json.Type != JTokenType.Object) return state;
            state.VisitsWithoutEvent = Math.Max(0, Json.Int(json["visits"], 0));
            if (json["cooldowns"] is JObject cooldowns)
                foreach (var p in cooldowns.Properties()) state.Cooldowns[p.Name] = Json.Int(p.Value, 0);
            if (json["not_rolled"] is JObject notRolled)
                foreach (var p in notRolled.Properties()) state.NotRolled[p.Name] = Json.Int(p.Value, 0);
            foreach (var id in Json.Array(json["happened"]))
                if (id.Type == JTokenType.String) state.Happened.Add((string)id);
            return state;
        }
    }

    /// <summary>How a town visit's event was decided, for the log and the tests.</summary>
    public sealed class TownEventOutcome
    {
        /// <summary>The event of the visit; null when the visit passes without one.</summary>
        public TownEventDef Event;
        /// <summary>The event came from DD1's quest guarantees, not from the draw.</summary>
        public bool Guaranteed;
        /// <summary>The event is one DD1 scripts (<see cref="TownEventDef.Scripted"/>): its requirements were met, nothing was rolled.</summary>
        public bool Scripted;
        /// <summary>The chance the visit had of bringing an event (not rolled for a guaranteed one).</summary>
        public double Chance;
        /// <summary>The chance roll succeeded (also true when nothing could be drawn afterwards).</summary>
        public bool ChancePassed;
        /// <summary>Events that took part in the draw.</summary>
        public int Candidates;
    }

    /// <summary>
    /// DD1's rules for which event, if any, a town visit brings. DD1 is native code, so the rules are read off
    /// its data files; what the files leave open is decided here and marked INFERRED (docs/recon/town-events.md).
    ///
    /// A visit, in order:
    /// 0. An event DD1 scripts (a priority, no weight: <see cref="TownEventDef.Scripted"/>) comes with the first
    ///    visit that meets its requirements, the highest priority first, before any promise or roll (INFERRED:
    ///    with weight 0 it could never be drawn, and what it waits for is a week or a point of the story).
    /// 1. A quest finished with success may guarantee an event (town_events.quest_type_event_guarantees.json).
    ///    The event still has to meet its own requirements and be off cooldown (INFERRED: the file only pairs
    ///    quests with events). A guaranteed event skips the chance roll.
    /// 2. Otherwise the visit brings an event with the chance DD1's settings give for the number of visits since
    ///    the last event (event_chance_per_town_visits; the last entry holds for all later visits).
    /// 3. The event is drawn by weight from those whose requirements are met, that are off cooldown and, when
    ///    unique, have not happened: weight = base_chance + per_not_rolled_additional_chance x draws lost since
    ///    the event last happened. Events of higher priority are drawn first. An event that only a guarantee
    ///    brings has base_chance 0 and never wins a draw.
    /// 4. The winner's cooldown starts, its lost draws are forgotten, the count of eventless visits starts again.
    ///    A visit without an event (chance failed, or nothing could be drawn) adds one to that count (INFERRED:
    ///    this is why the first event of a campaign comes with the first visit of week 6).
    /// </summary>
    public static class TownEventRules
    {
        /// <summary>DD1's own frequency settings; the mod plays on "normal" unless told otherwise.</summary>
        public const string DefaultSetting = "normal";

        /// <summary>Whether the estate meets an event's <c>requirements</c>.</summary>
        public static bool Meets(TownEventDef def, TownEventSituation situation)
        {
            if (situation.Week < def.MinimumWeek) return false;
            if (situation.DeadHeroes < def.DeadHeroes) return false;
            if (situation.TrinketsInStorage < def.TrinketsInStorage) return false;
            // INFERRED: the buildings on offer, not the ones that stand (the one event that asks is the one that opens the districts)
            if (situation.DistrictBuildings < def.DistrictBuildings) return false;
            foreach (var need in def.HeroLevels)
            {
                // INFERRED: a hero above the level counts too (the file gives a level and a count, no comparison).
                var have = 0;
                foreach (var level in situation.HeroLevels)
                    if (level >= need.Level) have++;
                if (have < need.Count) return false;
            }
            foreach (var upgrade in def.Upgrades)
                if (!situation.HasUpgrade(upgrade.Tree, upgrade.Code)) return false;
            return true;
        }

        /// <summary>An event's weight in a draw right now.</summary>
        public static double Weight(TownEventDef def, TownEventState state)
        {
            state.NotRolled.TryGetValue(def.Id, out var lost);
            return def.BaseChance + def.PerNotRolled * lost;
        }

        /// <summary>Whether an event may happen at this visit at all (requirements, cooldown, uniqueness).</summary>
        public static bool Available(TownEventDef def, TownEventState state, TownEventSituation situation)
        {
            if (def.Unique && state.Happened.Contains(def.Id)) return false;
            if (state.Cooldowns.TryGetValue(def.Id, out var left) && left > 0) return false;
            return Meets(def, situation);
        }

        /// <summary>
        /// Decides the event of one town visit and updates the campaign's memory.
        /// <paramref name="usable"/> leaves out events the caller cannot stage (null: every event is usable).
        /// </summary>
        public static TownEventOutcome Visit(TownEventCatalog catalog, TownEventState state, TownEventSituation situation, Rng rng,
            string setting = DefaultSetting, Func<TownEventDef, bool> usable = null)
        {
            var outcome = new TownEventOutcome();
            var chances = catalog.Chances(setting);
            var chance = chances.Length == 0 ? 0.0 : chances[Math.Min(state.VisitsWithoutEvent, chances.Length - 1)];
            outcome.Chance = chance;

            // 0. DD1's scripted events ("off" switches these off with everything else)
            if (chances.Any(c => c > 0))
            {
                foreach (var def in catalog.Events)
                {
                    if (!def.Scripted || (outcome.Event != null && def.PriorityRank <= outcome.Event.PriorityRank)) continue;
                    if ((usable == null || usable(def)) && Available(def, state, situation)) outcome.Event = def;
                }
                outcome.Scripted = outcome.Event != null;
            }

            // 1. the quest's promise ("off" switches these off with everything else)
            if (outcome.Event == null && situation.QuestSucceeded && chances.Any(c => c > 0))
            {
                var promised = catalog.Find(catalog.GuaranteeFor(situation.Dungeon, situation.QuestType));
                if (promised != null && (usable == null || usable(promised)) && Available(promised, state, situation))
                {
                    outcome.Event = promised;
                    outcome.Guaranteed = true;
                }
            }

            // 2. and 3. chance, then the draw
            List<TownEventDef> drawn = null;
            if (outcome.Event == null && rng.NextDouble() < chance)
            {
                outcome.ChancePassed = true;
                drawn = new List<TownEventDef>();
                foreach (var def in catalog.Events)
                    if ((usable == null || usable(def)) && Available(def, state, situation) && Weight(def, state) > 0) drawn.Add(def);
                if (drawn.Any(d => d.HigherPriority)) drawn = drawn.Where(d => d.HigherPriority).ToList();
                outcome.Candidates = drawn.Count;
                var pick = rng.PickWeighted(drawn.Select(d => Weight(d, state)).ToList());
                if (pick >= 0) outcome.Event = drawn[pick];
            }

            // 4. memory. Cooldowns that were running count this visit off; the winner's starts after that.
            foreach (var id in state.Cooldowns.Keys.ToList())
            {
                if (state.Cooldowns[id] <= 1) state.Cooldowns.Remove(id);
                else state.Cooldowns[id]--;
            }
            if (outcome.Event == null)
            {
                state.VisitsWithoutEvent++;
                return outcome;
            }
            state.VisitsWithoutEvent = 0;
            state.Happened.Add(outcome.Event.Id);
            state.NotRolled.Remove(outcome.Event.Id);
            if (outcome.Event.Cooldown > 0) state.Cooldowns[outcome.Event.Id] = outcome.Event.Cooldown;
            if (drawn != null)
            {
                foreach (var loser in drawn)
                {
                    if (loser == outcome.Event || loser.PerNotRolled <= 0) continue;
                    state.NotRolled.TryGetValue(loser.Id, out var lost);
                    state.NotRolled[loser.Id] = lost + 1;
                }
            }
            return outcome;
        }
    }
}
