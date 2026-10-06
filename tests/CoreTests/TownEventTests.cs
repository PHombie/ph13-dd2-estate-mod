using System;
using System.Collections.Generic;
using System.Linq;
using DD2Estate.Core;
using Newtonsoft.Json.Linq;

namespace DD2Estate.CoreTests
{
    /// <summary>DD1's town event selection (Core/TownEvents.cs): the file reader against the real install, the rules on small catalogs of their own.</summary>
    public static class TownEventTests
    {
        private const string NormalSettings = "{\"settings\":[{\"id\":\"off\",\"event_chance_per_town_visits\":[0.0]},{\"id\":\"normal\",\"event_chance_per_town_visits\":[0.33,0.67,0.75,1.0]},{\"id\":\"always\",\"event_chance_per_town_visits\":[1.0]}]}";

        private static string Event(string id, double chance, int cooldown = 0, int week = 0, double perNotRolled = 0, string extra = "", string requirements = "")
        {
            return "{\"id\":\"" + id + "\",\"base_chance\":" + chance.ToString(System.Globalization.CultureInfo.InvariantCulture)
                   + ",\"per_not_rolled_additional_chance\":" + perNotRolled.ToString(System.Globalization.CultureInfo.InvariantCulture)
                   + ",\"cooldown\":" + cooldown + extra
                   + ",\"requirements\":{\"minimum_week\":" + week + requirements + "},\"tone\":\"good\",\"data\":[{\"type\":\"free_activity\",\"string_data\":\"bar\",\"number_data\":0.0}]}";
        }

        private static TownEventCatalog Catalog(params string[] events)
        {
            return TownEventCatalog.Parse("{\"events\":[" + string.Join(",", events) + "]}", NormalSettings,
                "{\"quest_type_event_guarantees\":[{\"dungeon_type\":\"crypts\",\"quest_type\":\"gather\",\"event_id\":\"promised\"}]}");
        }

        private static TownEventSituation Week(int week)
        {
            return new TownEventSituation { Week = week };
        }

        // ---- DD1's files ---------------------------------------------------------------------------------

        public static void CatalogReadsDd1Files()
        {
            var catalog = TownEventCatalog.Load(Dd1.Files);
            // the districts' own event is read from its feature folder where the install has it
            var districts = Dd1.Files.Exists(TownEventCatalog.FeatureFiles[0]);
            var sources = new List<string> { "base.town_events.events.json", "mode.town_events.events.json" };
            if (districts) sources.Add("districts.town_events.events.json");
            Check.True(catalog.Sources.SequenceEqual(sources), "base and mode files are read, and the districts' one: " + string.Join(", ", catalog.Sources));
            Check.True(catalog.SkippedSources.Any(s => s.StartsWith("arena.")), "the arena file is left out");
            Check.Equal(districts ? 49 : 48, catalog.Events.Count, "47 base events, the stage coach one and the districts' Cornerstones");
            Check.True(catalog.Chances("normal").SequenceEqual(new[] { 0.33, 0.67, 0.75, 1.0 }), "normal frequency");
            Check.True(catalog.Chances("plentiful").SequenceEqual(new[] { 0.67, 0.67, 0.80, 1.0 }), "plentiful frequency");
            Check.True(catalog.Chances("off").SequenceEqual(new[] { 0.0 }), "off");
            Check.Equal(0, catalog.Chances("no such setting").Length, "unknown setting");
            Check.Equal(8, catalog.Guarantees.Count(), "guarantees");
            Check.Equal("free_abbey", catalog.GuaranteeFor("crypts", "gather"), "gather in the Ruins");
            Check.Equal("embark_party_buff_cove_buff", catalog.GuaranteeFor("cove", "inventory_activate"), "activate in the Cove");
            Check.Equal(null, catalog.GuaranteeFor("crypts", "explore"), "no promise for exploring");

            var dead = catalog.Find("dead_recruit");
            Check.True(dead != null && dead.BaseChance == 3 && dead.PerNotRolled == 1 && dead.Cooldown == 3 && dead.MinimumWeek == 15 && dead.DeadHeroes == 3, "dead_recruit numbers");
            Check.True(dead.Effects.Count == 1 && dead.Effects[0].Type == "dead_recruit" && dead.Effects[0].Number == 3, "dead_recruit effect");
            var armour = catalog.Find("upgrade_tag_free_armour");
            Check.True(armour.Upgrades.Count == 1 && armour.Upgrades[0].Tree == "blacksmith.armour" && armour.Upgrades[0].Code == "a", "armour needs the first smithing step");
            var invasion = catalog.Find("plot_quest_town_invasion_0");
            Check.True(invasion.MinimumWeek == 40 && invasion.HeroLevels.Count == 1 && invasion.HeroLevels[0].Level == 5 && invasion.HeroLevels[0].Count == 4, "invasion needs four heroes of level 5");
            var thief = catalog.Find("trinket_retention_add_from_storage");
            Check.True(thief.Unique && thief.TrinketsInStorage == 8 && thief.Tone == "bad", "the thief is unique and needs trinkets");
            Check.True(catalog.Find("stage_coach_bonus_recruits").Source.StartsWith("mode."), "the stage coach event comes from the mode file");
            Check.True(catalog.Find("embark_party_buff_crypts_buff").BaseChance == 0, "dungeon buffs only come by guarantee");
            Check.Equal(3, catalog.Find("lock_bar_discount_tavern").Effects.Count, "a lock and two discounts");
            foreach (var pair in catalog.Guarantees) Check.True(catalog.Find(pair.Value) != null, "guaranteed event exists: " + pair.Value);

            var xp = catalog.Buff("town_event_crypts_resolve_xp");
            Check.True(xp != null && xp.Stat == "resolve_xp_bonus_percent" && xp.Amount == 0.33 && xp.Rule == "in_dungeon" && xp.RuleText == "crypts", "resolve xp buff");
            var damage = catalog.Buff("town_event_weald_damage_low");
            Check.True(damage.Stat == "combat_stat_multiply" && damage.SubStat == "damage_low" && damage.Amount == 0.15 && damage.RuleText == "weald", "damage buff");
            Check.True(catalog.Buff("town_event_in_activity_bar_stress_damage").Amount == -0.33 && catalog.Buff("town_event_in_activity_bar_stress_damage").RuleText == "bar", "activity debuff");
            Check.True(catalog.Buff("town_event_idle_stress_heal").Amount == 2 && catalog.Buff("town_event_virtue_chance").Amount == 0.15 && catalog.Buff("town_event_affliction_chance").Amount == -0.15, "idle, virtue, affliction");
            foreach (var def in catalog.Events)
                foreach (var effect in def.Effects)
                    if (effect.Type == "embark_party_buff" || effect.Type == "in_activity_buff" || effect.Type == "idle_buff")
                        Check.True(catalog.Buff(effect.Text) != null, "buff of " + def.Id + " is in DD1's buff file: " + effect.Text);
        }

        public static void NoEventWithoutDd1()
        {
            var catalog = TownEventCatalog.Load(new NoDd1Files());
            Check.True(catalog.IsEmpty, "no events");
            var state = new TownEventState();
            var outcome = TownEventRules.Visit(catalog, state, Week(30), new Rng(1));
            Check.True(outcome.Event == null, "nothing happens");
        }

        public static void FirstEventComesWithWeekSix()
        {
            // DD1's own table: nothing can happen before week 6, and by then four visits passed without an event,
            // so the chance has reached 1.
            var catalog = TownEventCatalog.Load(Dd1.Files);
            for (var seed = 1; seed <= 200; seed++)
            {
                var state = new TownEventState();
                var rng = new Rng(seed);
                for (var week = 2; week <= 5; week++)
                    Check.True(TownEventRules.Visit(catalog, state, Week(week), rng).Event == null, "no event in week " + week);
                Check.Equal(4, state.VisitsWithoutEvent, "four eventless visits");
                var outcome = TownEventRules.Visit(catalog, state, Week(6), rng);
                Check.True(outcome.Event != null && outcome.Chance == 1.0, "week 6 brings an event (seed " + seed + ")");
                Check.True(outcome.Event.MinimumWeek <= 6 && outcome.Event.BaseChance > 0, "an event that may happen in week 6");
                Check.Equal(0, state.VisitsWithoutEvent, "the count starts again");
            }
        }

        public static void Dd1WeightsDecideTheDraw()
        {
            var catalog = TownEventCatalog.Load(Dd1.Files);
            var counts = new Dictionary<string, int>();
            const int n = 40000;
            var rng = new Rng(77);
            for (var i = 0; i < n; i++)
            {
                var state = new TownEventState { VisitsWithoutEvent = 3 };
                var outcome = TownEventRules.Visit(catalog, state, Week(6), rng);
                counts.TryGetValue(outcome.Event.Id, out var c);
                counts[outcome.Event.Id] = c + 1;
            }
            // Week 6, no dead, no upgrades: every week-6 event without further needs, by base_chance.
            var open = catalog.Events.Where(e => e.MinimumWeek <= 6 && e.DeadHeroes == 0 && e.Upgrades.Count == 0 && e.HeroLevels.Count == 0 && e.TrinketsInStorage == 0 && e.BaseChance > 0).ToList();
            var total = open.Sum(e => e.BaseChance);
            Check.True(counts.Keys.All(id => open.Any(e => e.Id == id)), "only open events are drawn");
            foreach (var def in open)
            {
                counts.TryGetValue(def.Id, out var c);
                Check.Near(def.BaseChance / total, c / (double)n, 0.01, "share of " + def.Id);
            }
            Check.True(!counts.ContainsKey("upgrade_tag_free_armour") && !counts.ContainsKey("embark_party_buff_crypts_buff") && !counts.ContainsKey("free_disease"), "needs and zero weights keep events out");
        }

        public static void Dd1GuaranteesFollowTheQuest()
        {
            var catalog = TownEventCatalog.Load(Dd1.Files);
            var gather = new TownEventSituation { Week = 8, Dungeon = "weald", QuestType = "gather", QuestSucceeded = true };
            for (var seed = 1; seed <= 50; seed++)
            {
                var outcome = TownEventRules.Visit(catalog, new TownEventState(), gather, new Rng(seed));
                Check.True(outcome.Guaranteed && outcome.Event.Id == "free_sanitarium", "a gather quest in the Weald brings the convention");
            }
            // the blacksmith's event still wants its upgrade
            var cove = new TownEventSituation { Week = 8, Dungeon = "cove", QuestType = "gather", QuestSucceeded = true };
            Check.True(!TownEventRules.Visit(catalog, new TownEventState(), cove, new Rng(3)).Guaranteed, "no weapon event without the smithing step");
            cove.HasUpgrade = (tree, code) => tree == "blacksmith.weapon" && code == "a";
            var promised = TownEventRules.Visit(catalog, new TownEventState(), cove, new Rng(3));
            Check.True(promised.Guaranteed && promised.Event.Id == "upgrade_tag_free_weapon", "with the step built the promise holds");
            // too early, and after a failure
            gather.Week = 3;
            Check.True(TownEventRules.Visit(catalog, new TownEventState(), gather, new Rng(1)).Event == null, "not before the event's minimum week");
            gather.Week = 8;
            gather.QuestSucceeded = false;
            Check.True(!TownEventRules.Visit(catalog, new TownEventState(), gather, new Rng(1)).Guaranteed, "a failed quest promises nothing");
            // the dungeon buffs have a cooldown of one visit
            var activate = new TownEventSituation { Week = 9, Dungeon = "crypts", QuestType = "inventory_activate", QuestSucceeded = true };
            var state = new TownEventState();
            Check.True(TownEventRules.Visit(catalog, state, activate, new Rng(5)).Event.Id == "embark_party_buff_crypts_buff", "first activate quest");
            Check.True(TownEventRules.Visit(catalog, state, activate, new Rng(5)).Event?.Id != "embark_party_buff_crypts_buff", "not twice in a row");
            Check.True(TownEventRules.Visit(catalog, state, activate, new Rng(6)).Event.Id == "embark_party_buff_crypts_buff", "again one visit later");
        }

        // ---- the rules on catalogs of the tests' own -------------------------------------------------------

        public static void ChanceGrowsWithEventlessVisits()
        {
            var catalog = Catalog(Event("only", 1));
            var expected = new[] { 0.33, 0.67, 0.75, 1.0, 1.0, 1.0 };
            for (var visits = 0; visits < expected.Length; visits++)
            {
                const int n = 20000;
                var rng = new Rng(100 + visits);
                var hits = 0;
                for (var i = 0; i < n; i++)
                {
                    var state = new TownEventState { VisitsWithoutEvent = visits };
                    var outcome = TownEventRules.Visit(catalog, state, Week(1), rng);
                    Check.Equal(expected[visits], outcome.Chance, "chance after " + visits + " visits");
                    if (outcome.Event != null)
                    {
                        hits++;
                        Check.Equal(0, state.VisitsWithoutEvent, "an event starts the count again");
                    }
                    else Check.Equal(visits + 1, state.VisitsWithoutEvent, "a visit without an event counts");
                }
                Check.Near(expected[visits], hits / (double)n, 0.012, "share of visits with an event after " + visits);
            }
            var off = new TownEventState { VisitsWithoutEvent = 9 };
            Check.True(TownEventRules.Visit(catalog, off, Week(1), new Rng(1), "off").Event == null, "off means off");
            Check.True(TownEventRules.Visit(catalog, off, Week(1), new Rng(1), "missing").Event == null, "an unknown setting means no events");
        }

        public static void CooldownKeepsAnEventAway()
        {
            var catalog = Catalog(Event("slow", 1, cooldown: 2));
            var state = new TownEventState();
            var rng = new Rng(9);
            Check.True(TownEventRules.Visit(catalog, state, Week(1), rng, "always").Event != null, "happens");
            Check.Equal(2, state.Cooldowns["slow"], "cooldown starts");
            Check.True(TownEventRules.Visit(catalog, state, Week(2), rng, "always").Event == null, "first visit of the cooldown");
            Check.True(TownEventRules.Visit(catalog, state, Week(3), rng, "always").Event == null, "second visit of the cooldown");
            Check.True(!state.Cooldowns.ContainsKey("slow"), "cooldown over");
            Check.Equal(2, state.VisitsWithoutEvent, "the visits in between were eventless");
            Check.True(TownEventRules.Visit(catalog, state, Week(4), rng, "always").Event != null, "back after two visits");

            var quick = Catalog(Event("quick", 1, cooldown: 0));
            var again = new TownEventState();
            Check.True(TownEventRules.Visit(quick, again, Week(1), rng, "always").Event != null && TownEventRules.Visit(quick, again, Week(2), rng, "always").Event != null, "no cooldown, no pause");
        }

        public static void UniqueEventHappensOnce()
        {
            var catalog = Catalog(Event("once", 1, extra: ",\"is_unique\":true"));
            var state = new TownEventState();
            var rng = new Rng(4);
            Check.True(TownEventRules.Visit(catalog, state, Week(1), rng, "always").Event != null, "happens");
            for (var week = 2; week < 12; week++)
                Check.True(TownEventRules.Visit(catalog, state, Week(week), rng, "always").Event == null, "never again");
            Check.True(state.Happened.Contains("once"), "remembered");
        }

        public static void LostDrawsAddWeight()
        {
            // "patient" gains 2 for every draw it loses; "common" always weighs 8.
            var catalog = Catalog(Event("common", 8), Event("patient", 2, perNotRolled: 2));
            var state = new TownEventState();
            state.NotRolled["patient"] = 3;
            Check.Equal(8.0, TownEventRules.Weight(catalog.Find("patient"), state), "2 + 3 x 2");

            var fresh = new TownEventState();
            var rng = new Rng(21);
            var lost = 0;
            for (var i = 0; i < 200; i++)
            {
                var outcome = TownEventRules.Visit(catalog, fresh, Week(1), rng, "always");
                if (outcome.Event.Id == "patient")
                {
                    Check.True(!fresh.NotRolled.ContainsKey("patient"), "winning forgets the lost draws");
                    lost = 0;
                }
                else
                {
                    lost++;
                    Check.Equal(lost, fresh.NotRolled["patient"], "a lost draw is counted");
                }
                Check.True(!fresh.NotRolled.ContainsKey("common"), "events without the bonus are not counted");
            }

            // With the bonus the patient event wins more often than its base weight alone would let it.
            const int n = 30000;
            var wins = 0;
            var longRun = new TownEventState();
            for (var i = 0; i < n; i++)
                if (TownEventRules.Visit(catalog, longRun, Week(1), rng, "always").Event.Id == "patient") wins++;
            Check.True(wins / (double)n > 0.30, "more than the 20% of its base weight: " + (wins / (double)n).ToString("0.000"));

            // A visit whose chance fails is no draw.
            var idle = new TownEventState();
            TownEventRules.Visit(catalog, idle, Week(1), new Rng(1), "off");
            Check.True(idle.NotRolled.Count == 0, "no draw, nothing lost");
        }

        public static void RequirementsGateEvents()
        {
            var needs = ",\"dead_heroes\":2,\"hero_level_counts\":[{\"level\":3,\"count\":2}],\"upgrades_purchased\":[{\"tree_id\":\"guild.cost\",\"requirement_code\":\"b\"}],\"trinket_storage_count\":4";
            var catalog = Catalog(Event("picky", 1, week: 10, requirements: needs));
            var def = catalog.Find("picky");
            var situation = new TownEventSituation
            {
                Week = 10, DeadHeroes = 2, TrinketsInStorage = 4, HeroLevels = new List<int> { 3, 5, 0 },
                HasUpgrade = (tree, code) => tree == "guild.cost" && code == "b"
            };
            Check.True(TownEventRules.Meets(def, situation), "everything met");
            situation.Week = 9;
            Check.True(!TownEventRules.Meets(def, situation), "too early");
            situation.Week = 10;
            situation.DeadHeroes = 1;
            Check.True(!TownEventRules.Meets(def, situation), "too few dead");
            situation.DeadHeroes = 2;
            situation.HeroLevels = new List<int> { 3, 2, 2 };
            Check.True(!TownEventRules.Meets(def, situation), "too few seasoned heroes");
            situation.HeroLevels = new List<int> { 4, 6 };
            Check.True(TownEventRules.Meets(def, situation), "higher levels count");
            situation.TrinketsInStorage = 3;
            Check.True(!TownEventRules.Meets(def, situation), "too few trinkets");
            situation.TrinketsInStorage = 9;
            situation.HasUpgrade = (tree, code) => false;
            Check.True(!TownEventRules.Meets(def, situation), "upgrade missing");
            Check.True(TownEventRules.Visit(catalog, new TownEventState(), situation, new Rng(2), "always").Event == null, "an unmet event is not drawn");
        }

        public static void GuaranteeSkipsTheChanceRoll()
        {
            var catalog = Catalog(Event("promised", 0, cooldown: 1, week: 6), Event("other", 5, week: 6));
            var quest = new TownEventSituation { Week = 6, Dungeon = "crypts", QuestType = "gather", QuestSucceeded = true };
            var state = new TownEventState();
            for (var i = 0; i < 100; i++)
            {
                var fresh = new TownEventState();
                var outcome = TownEventRules.Visit(catalog, fresh, quest, new Rng(i));
                Check.True(outcome.Guaranteed && outcome.Event.Id == "promised" && !outcome.ChancePassed, "the promise holds at any roll");
                Check.Equal(0, fresh.NotRolled.Count, "no draw was made");
            }
            Check.True(TownEventRules.Visit(catalog, state, quest, new Rng(1)).Guaranteed, "first time");
            var second = TownEventRules.Visit(catalog, state, quest, new Rng(1), "always");
            Check.True(!second.Guaranteed && second.Event.Id == "other", "on cooldown the visit falls back to the draw, where a weight of 0 never wins");
            quest.Dungeon = "weald";
            Check.True(!TownEventRules.Visit(catalog, new TownEventState(), quest, new Rng(1), "always").Guaranteed, "another dungeon, no promise");
            quest.Dungeon = "crypts";
            Check.True(TownEventRules.Visit(catalog, new TownEventState(), quest, new Rng(1), "off").Event == null, "events switched off: no promise either");
            Check.True(!TownEventRules.Visit(catalog, new TownEventState(), quest, new Rng(1), "always", def => def.Id != "promised").Guaranteed, "an unusable event is not promised");
        }

        public static void HigherPriorityIsDrawnFirst()
        {
            var catalog = Catalog(Event("loud", 1, extra: ",\"priority\":\"higher\",\"is_unique\":true"), Event("heavy", 1000));
            var state = new TownEventState();
            var rng = new Rng(8);
            Check.Equal("loud", TownEventRules.Visit(catalog, state, Week(1), rng, "always").Event.Id, "priority beats weight");
            Check.Equal("heavy", TownEventRules.Visit(catalog, state, Week(2), rng, "always").Event.Id, "then the others");
        }

        public static void ScriptedEventComesWhenItsTimeHasCome()
        {
            // a priority and no weight: DD1's way of writing an event that is not drawn but happens
            var scripted = Event("cornerstones", 0, week: 10, extra: ",\"priority\":\"higher\",\"is_unique\":true", requirements: ",\"minimum_number_of_district_buildings\":1");
            var catalog = Catalog(scripted, Event("other", 5, week: 1), Event("promised", 0, week: 1));
            Check.True(catalog.Find("cornerstones").Scripted && !catalog.Find("other").Scripted && !catalog.Find("promised").Scripted, "only a priority without a weight is a script");
            Check.Equal(1, catalog.Find("cornerstones").DistrictBuildings, "it asks for districts");

            var state = new TownEventState();
            var rng = new Rng(4);
            for (var week = 2; week <= 9; week++)
                Check.True(TownEventRules.Visit(catalog, state, new TownEventSituation { Week = week, DistrictBuildings = 11 }, rng)?.Event?.Id != "cornerstones", "not before week 10");
            var gather = new TownEventSituation { Week = 10, DistrictBuildings = 11, Dungeon = "crypts", QuestType = "gather", QuestSucceeded = true };
            var outcome = TownEventRules.Visit(catalog, state, gather, rng);
            Check.True(outcome.Scripted && !outcome.Guaranteed && !outcome.ChancePassed && outcome.Event.Id == "cornerstones", "week 10 brings it, before the quest's promise and without a roll");
            Check.True(state.Happened.Contains("cornerstones") && state.VisitsWithoutEvent == 0, "it counts as the visit's event");
            for (var week = 11; week <= 40; week++)
                Check.True(TownEventRules.Visit(catalog, state, new TownEventSituation { Week = week, DistrictBuildings = 11 }, rng).Event?.Id != "cornerstones", "once only");

            for (var seed = 1; seed <= 50; seed++)
            {
                Check.True(TownEventRules.Visit(catalog, new TownEventState(), new TownEventSituation { Week = 30 }, new Rng(seed), "always").Event.Id == "other", "an estate without districts never sees it");
                Check.True(TownEventRules.Visit(catalog, new TownEventState(), new TownEventSituation { Week = 30, DistrictBuildings = 11 }, new Rng(seed), "always", def => def.Id != "cornerstones").Event.Id == "other",
                    "nor one that cannot stage it");
            }
            Check.True(TownEventRules.Visit(catalog, new TownEventState(), new TownEventSituation { Week = 30, DistrictBuildings = 11 }, new Rng(1), "off").Event == null, "events switched off: no script either");

            var ranked = Catalog(Event("high", 0, extra: ",\"priority\":\"high\""), Event("highest", 0, extra: ",\"priority\":\"highest\",\"is_unique\":true"), Event("higher", 0, extra: ",\"priority\":\"higher\",\"is_unique\":true"));
            var order = new TownEventState();
            Check.Equal("highest", TownEventRules.Visit(ranked, order, Week(1), rng).Event.Id, "the highest priority first");
            Check.Equal("higher", TownEventRules.Visit(ranked, order, Week(2), rng).Event.Id, "then the next");
            Check.Equal("high", TownEventRules.Visit(ranked, order, Week(3), rng).Event.Id, "then the last");
        }

        public static void CornerstonesReadFromDd1()
        {
            if (!Dd1.Files.Exists(TownEventCatalog.FeatureFiles[0])) throw new SkipException("this DD1 install has no districts");
            var catalog = TownEventCatalog.Load(Dd1.Files);
            var def = catalog.Find("cc_districts_unlock");
            Check.True(def != null && def.Scripted && def.Unique && def.MinimumWeek == 10 && def.DistrictBuildings == 1 && def.Tone == "good", "week 10, once, with districts on offer");
            Check.Equal(DistrictRules.Feature, def.Root, "its picture lies in the feature's folder");
            Check.True(Dd1.Files.Exists(def.Root + "campaign/town/town_event/town_event.image_" + def.Id + ".png"), "and is there");
            Check.True(def.Effects.Count == 2 && def.Effects[0].Type == "districts_unlocked" && def.Effects[1].Type == "bonus_currency" && def.Effects[1].Text == DistrictRules.Blueprint && def.Effects[1].Number == 1,
                "it opens the districts and brings a blueprint");
            Check.True(catalog.Events.Where(e => e.Id != def.Id).All(e => !e.Scripted && e.Root == ""), "no other event is scripted or lies outside the campaign folder");

            for (var seed = 1; seed <= 100; seed++)
            {
                var state = new TownEventState();
                var rng = new Rng(seed);
                for (var week = 2; week <= 9; week++)
                    Check.True(TownEventRules.Visit(catalog, state, new TownEventSituation { Week = week, DistrictBuildings = 11 }, rng).Event?.Id != def.Id, "not before week 10 (seed " + seed + ")");
                Check.Equal(def.Id, TownEventRules.Visit(catalog, state, new TownEventSituation { Week = 10, DistrictBuildings = 11 }, rng).Event?.Id, "week 10 (seed " + seed + ")");
            }
        }

        public static void UnusableEventsAreNeverDrawn()
        {
            var catalog = Catalog(Event("staged", 1), Event("missing", 1000));
            var rng = new Rng(12);
            for (var i = 0; i < 300; i++)
            {
                var outcome = TownEventRules.Visit(catalog, new TownEventState(), Week(1), rng, "always", def => def.Id != "missing");
                Check.Equal("staged", outcome.Event.Id, "only what can be staged");
                Check.Equal(1, outcome.Candidates, "one candidate");
            }
            var none = TownEventRules.Visit(catalog, new TownEventState(), Week(1), rng, "always", def => false);
            Check.True(none.Event == null && none.ChancePassed && none.Candidates == 0, "nothing usable: the visit passes");
        }

        public static void StateSurvivesTheSave()
        {
            var catalog = Catalog(Event("a", 3, cooldown: 3, perNotRolled: 1), Event("b", 3, cooldown: 2, perNotRolled: 1), Event("c", 1, extra: ",\"is_unique\":true"));
            var state = new TownEventState();
            var rng = new Rng(31);
            for (var week = 1; week <= 12; week++) TownEventRules.Visit(catalog, state, Week(week), rng);
            var saved = state.ToJson().ToString();
            var loaded = TownEventState.FromJson(JObject.Parse(saved));
            Check.Equal(saved, loaded.ToJson().ToString(), "round trip");
            Check.Equal(state.VisitsWithoutEvent, loaded.VisitsWithoutEvent, "visits");
            Check.True(state.Happened.SetEquals(loaded.Happened), "happened");

            // the same dice after loading give the same campaign
            var a = Rng.FromState(rng.State);
            var b = Rng.FromState(rng.State);
            for (var week = 13; week <= 40; week++)
            {
                var first = TownEventRules.Visit(catalog, state, Week(week), a).Event?.Id;
                var second = TownEventRules.Visit(catalog, loaded, Week(week), b).Event?.Id;
                Check.Equal(first, second, "week " + week);
            }
            Check.True(TownEventState.FromJson(null).VisitsWithoutEvent == 0 && TownEventState.FromJson(new JArray()).Cooldowns.Count == 0, "no save, fresh state");
        }
    }
}
