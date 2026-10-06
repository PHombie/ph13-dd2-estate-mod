using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DD2Estate.Core;
using DD2Estate.Dd2;
using DD2Estate.Dungeon;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Estate
{
    /// <summary>
    /// DD1's town events: when the party comes home, the week may open with an event. Which one is DD1's own
    /// rule and data (<see cref="TownEventRules"/>, read from campaign/town_events in the player's install);
    /// this class keeps the campaign's memory, asks the rules once per week and holds the week's event until
    /// the next week begins. What an event does to the estate is in TownEventEffects.cs (the other half of this
    /// class); what other systems must ask is in <see cref="TownEventHooks"/>.
    ///
    /// Events whose effect needs something the mod does not have (a DD1 hero class DD2 lacks) are left out of
    /// the draw (<see cref="Unsupported"/>); the DLC event files are not read at all. The events with a quest
    /// of their own (the Brigand Incursion, "Shrieker's Prize") and the Shrieker's theft from the stores are
    /// staged with <see cref="PlotQuests"/> and <see cref="Shrieker"/>. Saved as the "town_events" section of
    /// the estate.
    /// </summary>
    [EstateModule]
    internal static partial class TownEvents
    {
        private const string SectionKey = "town_events";

        /// <summary>The event of one week as the estate lives it.</summary>
        public class Active
        {
            public string Id;
            public int Week;
            /// <summary>It came from DD1's quest guarantees.</summary>
            public bool Guaranteed;
            /// <summary>What it does on arrival (recruits on the coach, free upgrades handed out) has been done.</summary>
            public bool Applied;
            /// <summary>The notice has been put in front of the player.</summary>
            public bool Shown;
            /// <summary>Free purchases left this week, by DD1's upgrade tag ("weapon", "armour", "building").</summary>
            public readonly Dictionary<string, int> FreeUpgrades = new Dictionary<string, int>();
            /// <summary>The fallen who may come back ("From Beyond"), by <see cref="FallenKey"/>; one of them at most.</summary>
            public readonly List<string> Returnable = new List<string>();
            public bool Returned;
            /// <summary>What the arrival did, in the mod's words, for the notice ("Two Vestals wait at the Stage Coach").</summary>
            public readonly List<string> Notes = new List<string>();

            public TownEventDef Def => Catalog.Find(Id);
        }

        // The quest the party has just come back from: what DD1's guarantees ask about.
        private class LastQuest
        {
            public string Dungeon, QuestType;
            public bool Success;
        }

        private static TownEventCatalog _catalog;
        private static TownEventState _state = new TownEventState();
        private static string _setting = TownEventRules.DefaultSetting;
        private static Rng _rng = NewRng();
        private static Active _current, _previous;
        private static LastQuest _lastQuest;
        // Heroes who sat the last expedition out, taken when it ended (the week-end handlers of the buildings
        // run in no particular order and empty their slots).
        private static List<uint> _idle;
        private static readonly HashSet<string> _logged = new HashSet<string>();

        /// <summary>Raised when the week's event changed or something about it did (a free upgrade used, a hero returned).</summary>
        public static event Action Changed;

        private static void Register()
        {
            EstateState.RegisterSection(SectionKey, Save, Load, Reset);
            EstateState.ExpeditionEnded += OnExpeditionEnded;
            EstateState.WeekAdvanced += OnWeekAdvanced;
            NarrationMoments.HamletOpened += OnHamletOpened;
            NarrationMoments.ExpeditionSeen += OnExpeditionSeen;
            NarrationMoments.HeroFell += Remember;
            NarrationMoments.Tick += OnTick;
            Resolve.ExperienceBonuses.Add(ExperienceShare);
        }

        // ---- DD1's data ----------------------------------------------------------------------------------

        public static TownEventCatalog Catalog
        {
            get
            {
                if (_catalog != null) return _catalog;
                try
                {
                    _catalog = TownEventCatalog.Load(new Dd1Files());
                    Plugin.Log.LogInfo("Town events (DD1): " + _catalog.Events.Count + " events from " + string.Join(", ", _catalog.Sources)
                                       + (_catalog.SkippedSources.Count > 0 ? "; not read: " + string.Join(", ", _catalog.SkippedSources) : "")
                                       + "; chance by visits (" + _setting + ") " + string.Join("/", _catalog.Chances(_setting).Select(c => c.ToString("0.##", CultureInfo.InvariantCulture))));
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Town events could not be read from DD1: " + e.Message);
                    _catalog = TownEventCatalog.Parse(null, null);
                }
                return _catalog;
            }
        }

        public static TownEventState State => _state;

        /// <summary>DD1's frequency option: "normal" (the default), "plentiful" or "off".</summary>
        public static string Setting
        {
            get => _setting;
            set => _setting = string.IsNullOrEmpty(value) ? TownEventRules.DefaultSetting : value;
        }

        // ---- the week's event ----------------------------------------------------------------------------

        /// <summary>The event of the present week; null when the week has none.</summary>
        public static Active Current => EventOfWeek(EstateState.Current.Week);

        /// <summary>
        /// The event of a week: the present one or the one before. While the week advances the buildings finish
        /// the week that has just ended, so they ask for it by number rather than for "the current event".
        /// </summary>
        public static Active EventOfWeek(int week)
        {
            if (_current != null && _current.Week == week) return _current;
            return _previous != null && _previous.Week == week ? _previous : null;
        }

        /// <summary>The effects of one kind in an event; empty for no event.</summary>
        public static IEnumerable<TownEventEffect> Effects(Active active, string type)
        {
            var def = active?.Def;
            return def != null ? def.EffectsOf(type) : Enumerable.Empty<TownEventEffect>();
        }

        // ---- what the mod can stage ----------------------------------------------------------------------

        // Effects another system honours through TownEventHooks.
        private static readonly HashSet<string> HookEffects = new HashSet<string>
        {
            "in_activity_buff", "activity_lock", "activity_cost_change", "free_activity",
            "provision_item_type_cost_change", "provision_item_type_amount_change", "upgrade_tag_free",
            "remove_quest_hero_level_restriction"
        };

        /// <summary>
        /// Why the mod cannot stage an event right now; null when it can. An event is staged only when every
        /// one of its effects has a target in the mod, so that a notice never promises what does not happen.
        /// </summary>
        public static string Unsupported(TownEventDef def)
        {
            if (def.Effects.Count == 0) return "no effect listed";
            string classless = null;
            var staged = 0;
            foreach (var effect in def.Effects)
            {
                switch (effect.Type)
                {
                    case "idle_resolve_level":
                        // The archery tournament names two classes: one that exists is enough.
                        if (ClassExists(effect.Text)) staged++;
                        else classless = "DD2 has no " + effect.Text;
                        break;
                    case "bonus_recruit":
                        if (!ClassExists(effect.Text)) return "DD2 has no " + effect.Text;
                        // The roster only: this week's coach is not filled yet when the week's event is decided.
                        if (FreePath(effect.Text, null, true, false) == null) return "the estate has a " + effect.Text + " on every path";
                        staged++;
                        break;
                    case "stage_coach_bonus_recruits":
                        staged++;
                        break;
                    case "dead_recruit":
                        // DD1 asks for three graves; the estate also needs a place for whoever comes back.
                        if (ReturnableFallen().Count == 0) return "none of the fallen has a place to return to";
                        staged++;
                        break;
                    case "embark_party_buff":
                    case "idle_buff":
                        var reason = BuffProblem(effect.Text);
                        if (reason != null) return reason;
                        staged++;
                        break;
                    case "free_activity":
                    case "activity_lock":
                    case "activity_cost_change":
                        if (!ActivityExists(effect.Text)) return "the estate has no activity " + effect.Text;
                        staged++;
                        break;
                    case "upgrade_tag_free":
                        if (effect.Text != "weapon" && effect.Text != "armour" && effect.Text != "building") return "no upgrades of the kind " + effect.Text;
                        staged++;
                        break;
                    case "upgrade_tag_discount":
                        // DD1 has one such event, on the wagon's trinkets; the wagon takes a discount for the week.
                        if (effect.Text != "trinket") return "no prices of the kind " + effect.Text;
                        staged++;
                        break;
                    case "districts_unlocked":
                        // DD1's "Cornerstones": the districts open for building
                        if (!Districts.Available) return "this DD1 install has no districts";
                        staged++;
                        break;
                    case "bonus_currency":
                        // DD1 has one such event, and its currency is the districts' blueprint
                        if (effect.Text != DistrictRules.Blueprint || !Districts.Available) return "the estate keeps no " + effect.Text;
                        staged++;
                        break;
                    case "plot_quest":
                    {
                        // DD1's quests of an event (the Brigand Incursion, "Shrieker's Prize"): Estate/PlotQuests.cs
                        var why = PlotQuests.Unstageable(effect.Text);
                        if (why != null) return why;
                        staged++;
                        break;
                    }
                    case "trinket_retention_add_from_storage":
                        // DD1's "A Thief in the Night": the Shrieker takes from the estate's stores (Estate/Shrieker.cs)
                        staged++;
                        break;
                    default:
                        if (!HookEffects.Contains(effect.Type)) return "unknown effect " + effect.Type;
                        staged++;
                        break;
                }
            }
            return staged == 0 ? classless ?? "nothing to stage" : null;
        }

        /// <summary>Every event with what keeps it out of the draw (null: it can be staged), for the log, the report and the bridge.</summary>
        public static List<KeyValuePair<TownEventDef, string>> Support()
        {
            var list = new List<KeyValuePair<TownEventDef, string>>();
            foreach (var def in Catalog.Events) list.Add(new KeyValuePair<TownEventDef, string>(def, Unsupported(def)));
            return list;
        }

        private static bool Usable(TownEventDef def)
        {
            var reason = Unsupported(def);
            if (reason != null && _logged.Add(def.Id)) Plugin.Log.LogInfo("Town events: " + def.Id + " is left out of the draw: " + reason);
            return reason == null;
        }

        private static bool ClassExists(string classId)
        {
            var classes = RosterLifecycle.AvailableClasses();
            for (var i = 0; i < classes.Count; i++)
                if (classes[i] == classId) return true;
            return false;
        }

        private static bool ActivityExists(string id)
        {
            foreach (var building in ActivityRules.BuildingIds)
                if (ActivityRules.Find(building, id) != null) return true;
            return SanitariumRules.Find(id) != null;
        }

        // The buffs DD1's events hand out change four things; all four have a counterpart in the mod.
        private static string BuffProblem(string buffId)
        {
            var buff = Catalog.Buff(buffId);
            if (buff == null) return "DD1 has no buff " + buffId;
            switch (buff.Stat)
            {
                case "resolve_xp_bonus_percent":
                case "resolve_check_percent":
                case "stress_heal_received_percent":
                    return null;
                case "combat_stat_multiply":
                    return buff.SubStat == "damage_low" || buff.SubStat == "damage_high" ? null : "no counterpart for " + buff.Stat + " " + buff.SubStat;
                default:
                    return "no counterpart for " + buff.Stat;
            }
        }

        // ---- the week turns ------------------------------------------------------------------------------

        private static void OnExpeditionEnded(EstateState.Expedition expedition)
        {
            if (!EstateSession.Active) return;
            var run = DungeonRun.Current;
            _lastQuest = new LastQuest
            {
                Dungeon = expedition.Dungeon,
                QuestType = Dd1QuestType(run != null ? run.Exploration.Map.QuestType : null),
                Success = expedition.Success
            };
            _idle = IdleHeroes();
            EndExpedition(expedition);
        }

        /// <summary>
        /// DD1's guarantees and narration know the quest of setting curios going as "inventory_activate" (its
        /// plain "activate" belongs to the Darkest Dungeon). The estate's quest of that shape may carry either
        /// name, so both are read as DD1's.
        /// </summary>
        public static string Dd1QuestType(string questType)
        {
            return questType == QuestTypes.Activate ? QuestTypes.InventoryActivate : questType;
        }

        private static void OnWeekAdvanced(int week)
        {
            if (!EstateSession.Active) return;
            try
            {
                CloseWeek(week - 1);
                var situation = Situation(week);
                var outcome = TownEventRules.Visit(Catalog, _state, situation, _rng, _setting, Usable);
                if (_current != null) _previous = _current;
                _current = outcome.Event != null ? new Active { Id = outcome.Event.Id, Week = week, Guaranteed = outcome.Guaranteed } : null;
                Plugin.Log.LogInfo("Town events, week " + week + ": "
                                   + (outcome.Event != null
                                       ? outcome.Event.Id + (outcome.Scripted ? " (its time has come: DD1 scripts it)"
                                           : outcome.Guaranteed ? " (guaranteed by the " + situation.QuestType + " quest in " + situation.Dungeon + ")" : " (drawn from " + outcome.Candidates + ")")
                                       : outcome.ChancePassed ? "none (nothing could be drawn)" : "none")
                                   + ", chance " + outcome.Chance.ToString("0.##", CultureInfo.InvariantCulture)
                                   + ", eventless visits now " + _state.VisitsWithoutEvent);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Town events: the week's event could not be decided: " + e);
            }
            _lastQuest = null;
            _idle = null;
            RaiseChanged();
        }

        private static TownEventSituation Situation(int week)
        {
            var levels = new List<int>();
            foreach (var guid in RosterLifecycle.LivingGuids()) levels.Add(Resolve.Level(guid));
            return new TownEventSituation
            {
                Week = week,
                DeadHeroes = Graveyard.All.Count,
                HeroLevels = levels,
                HasUpgrade = UpgradeRules.Has,
                // DD1's trinket storage is the estate's own stores (the unworn ones): what the thief's event asks about
                TrinketsInStorage = Trinkets.Stored().Count,
                DistrictBuildings = Districts.Rules.Buildings.Count,
                Dungeon = _lastQuest?.Dungeon,
                QuestType = _lastQuest?.QuestType,
                QuestSucceeded = _lastQuest != null && _lastQuest.Success
            };
        }

        // The arrival's effects wait for the hamlet: by then every system has finished its own week end (the
        // coach has been filled, the dead are buried), in whatever order they were called.
        private static void OnHamletOpened()
        {
            var active = Current;
            if (active == null || active.Applied) return;
            try { Arrive(active); }
            catch (Exception e) { Plugin.Log.LogError("Town events: " + active.Id + " could not be applied: " + e); }
            active.Applied = true;
            RaiseChanged();
            // What arrived (the coach's extra recruits, the graves that were chosen) is part of the week now.
            if (EstateSession.InHub) EstatePersistence.SaveNow("town event");
        }

        internal static void RaiseChanged()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { Plugin.Log.LogError("Town events: a Changed handler failed: " + e); }
        }

        // ---- dev -----------------------------------------------------------------------------------------

        /// <summary>Makes an event this week's, whatever the rules say (it still counts as having happened). Null, or why not.</summary>
        public static string Force(string id)
        {
            if (!EstateSession.Active) return "no estate session";
            var def = Catalog.Find(id);
            if (def == null) return "no such event";
            var week = EstateState.Current.Week;
            // An event it replaces simply goes: its week-end effects belong to a week that ran its course.
            if (_current != null && _current.Week != week) _previous = _current;
            ClearBuffs();
            _current = new Active { Id = id, Week = week };
            _state.Happened.Add(id);
            _state.VisitsWithoutEvent = 0;
            if (def.Cooldown > 0) _state.Cooldowns[id] = def.Cooldown;
            Plugin.Log.LogInfo("Town events: " + id + " forced for week " + week + (Unsupported(def) is string reason ? " (the mod cannot stage all of it: " + reason + ")" : ""));
            if (HamletScreen.IsOpen) OnHamletOpened();
            RaiseChanged();
            return null;
        }

        /// <summary>Ends this week's event without its week-end effects.</summary>
        public static void Clear()
        {
            if (_current != null && _current.Week == EstateState.Current.Week) _current = null;
            ClearBuffs();
            RaiseChanged();
        }

        /// <summary>Asks DD1's rules again for the present week, as if the party had just come home from this quest.</summary>
        public static TownEventOutcome RollAgain(string dungeon, string questType, bool success)
        {
            var week = EstateState.Current.Week;
            _lastQuest = dungeon != null ? new LastQuest { Dungeon = dungeon, QuestType = Dd1QuestType(questType), Success = success } : null;
            var outcome = TownEventRules.Visit(Catalog, _state, Situation(week), _rng, _setting, Usable);
            _lastQuest = null;
            if (_current != null && _current.Week != week) _previous = _current;
            _current = outcome.Event != null ? new Active { Id = outcome.Event.Id, Week = week, Guaranteed = outcome.Guaranteed } : null;
            if (HamletScreen.IsOpen) OnHamletOpened();
            RaiseChanged();
            return outcome;
        }

        // ---- save ----------------------------------------------------------------------------------------

        private static Rng NewRng() => new Rng(DateTime.Now.Ticks ^ 0x70776E45L);

        private static JToken Save()
        {
            return new JObject
            {
                ["setting"] = _setting,
                ["rng"] = _rng.State.ToString(CultureInfo.InvariantCulture),
                ["state"] = _state.ToJson(),
                ["current"] = ToJson(_current),
                ["previous"] = ToJson(_previous),
                ["fallen"] = SaveFallen(),
                ["expedition"] = SaveExpedition()
            };
        }

        private static void Load(JToken token)
        {
            Reset();
            if (!(token is JObject json)) return;
            Setting = (string)json["setting"];
            if (ulong.TryParse((string)json["rng"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var state)) _rng = Rng.FromState(state);
            _state = TownEventState.FromJson(json["state"]);
            _current = FromJson(json["current"]);
            _previous = FromJson(json["previous"]);
            LoadFallen(json["fallen"]);
            LoadExpedition(json["expedition"]);
        }

        private static void Reset()
        {
            _state = new TownEventState();
            _setting = TownEventRules.DefaultSetting;
            _rng = NewRng();
            _current = _previous = null;
            _lastQuest = null;
            _idle = null;
            _logged.Clear();
            ResetEffects();
        }

        private static JToken ToJson(Active active)
        {
            if (active == null) return JValue.CreateNull();
            return new JObject
            {
                ["id"] = active.Id,
                ["week"] = active.Week,
                ["guaranteed"] = active.Guaranteed,
                ["applied"] = active.Applied,
                ["shown"] = active.Shown,
                ["free"] = JObject.FromObject(active.FreeUpgrades),
                ["returnable"] = new JArray(active.Returnable),
                ["returned"] = active.Returned,
                ["notes"] = new JArray(active.Notes)
            };
        }

        private static Active FromJson(JToken token)
        {
            if (!(token is JObject json) || string.IsNullOrEmpty((string)json["id"])) return null;
            var active = new Active
            {
                Id = (string)json["id"],
                Week = (int?)json["week"] ?? 0,
                Guaranteed = (bool?)json["guaranteed"] ?? false,
                Applied = (bool?)json["applied"] ?? false,
                Shown = (bool?)json["shown"] ?? false,
                Returned = (bool?)json["returned"] ?? false
            };
            if (json["free"] is JObject free)
                foreach (var p in free.Properties()) active.FreeUpgrades[p.Name] = (int?)p.Value ?? 0;
            foreach (var key in json["returnable"] as JArray ?? new JArray()) active.Returnable.Add((string)key);
            foreach (var note in json["notes"] as JArray ?? new JArray()) active.Notes.Add((string)note);
            return active;
        }

        /// <summary>Snapshot for the log and the test bridge.</summary>
        public static object Describe()
        {
            var active = Current;
            var def = active?.Def;
            return new
            {
                week = EstateState.Current.Week,
                setting = _setting,
                chances = Catalog.Chances(_setting),
                eventlessVisits = _state.VisitsWithoutEvent,
                current = active == null ? null : new
                {
                    id = active.Id,
                    title = TownEventText.Title(active.Id),
                    tone = def?.Tone,
                    guaranteed = active.Guaranteed,
                    applied = active.Applied,
                    shown = active.Shown,
                    info = TownEventText.Info(active),
                    freeUpgrades = active.FreeUpgrades,
                    returnable = active.Returnable,
                    returned = active.Returned,
                    unsupported = def != null ? Unsupported(def) : "DD1 has no such event"
                },
                previous = _previous == null ? null : new { id = _previous.Id, week = _previous.Week },
                cooldowns = _state.Cooldowns,
                lostDraws = _state.NotRolled,
                happened = _state.Happened.OrderBy(id => id, StringComparer.Ordinal).ToList(),
                expedition = DescribeExpedition(),
                hooksAsked = TownEventHooks.Asked
            };
        }
    }
}
