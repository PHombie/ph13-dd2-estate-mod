using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DD2Estate.Core;

namespace DD2Estate.CoreTests
{
    /// <summary>
    /// DD1's two events with a quest of their own: the Brigand Incursion (a hand-made map of the hamlet, upgrade
    /// steps lost when it is ignored) and the Shrieker (trinkets taken into its hoard, the quest that gives them
    /// back, the fight's four rounds).
    /// </summary>
    public static class PlotEventTests
    {
        private static byte[] MapBytes(string name)
        {
            if (!Dd1.Available) throw new SkipException("needs the DD1 install");
            var path = Path.Combine(Dd1.Root, "maps", name + ".dm");
            if (!File.Exists(path)) throw new SkipException("this DD1 install has no maps/" + name + ".dm");
            return File.ReadAllBytes(path);
        }

        // ---- DD1's hand-made maps ----------------------------------------------------------------------

        public static void TheIncursionsMapIsReadAsDd1WroteIt()
        {
            var map = Dd1Map.Read(MapBytes("town_invasion_0"));
            Check.Equal("rooH", map.EntranceId, "entrance");
            Check.Equal("rooB", map.FinalRoomId, "final room");
            Check.Equal(8, map.Areas.Count(a => a.IsRoom), "rooms");
            Check.Equal(7, map.Areas.Count(a => !a.IsRoom), "hallways");
            var entrance = map.Area("rooH").Tiles[0];
            Check.True(entrance.X == 11 && entrance.Y == 11 && entrance.Type == Dd1Map.RoomTile, "the entrance stands at 11, 11");
            Check.Equal("room_wall.start", entrance.Texture, "the entrance's picture");
            var street = map.Area("corC");
            Check.Equal(9, street.Tiles.Count, "tiles of the north-west street, its two door tiles among them");
            Check.True(street.Tiles[0].DoorTo == "rooB" && street.Tiles[8].DoorTo == "rooD", "it joins the final room and the room at 6, 6");
            Check.True(street.Tiles[3].Content == Dd1Map.Fight && street.Tiles[3].Mash == "town_incursion_weak_08", "its fight is a named one");
            Check.Equal(Dd1Map.SideDoor, street.Tiles[2].Content, "the locked door in its side");
            Check.Equal("town_incursion_06", map.Area("rooC").Tiles[0].Mash, "a room's named fight");
        }

        public static void TheIncursionsMapBecomesAnExpeditionsMap()
        {
            var notes = new FixedMapNotes();
            var map = FixedMap.Build(Dd1Map.Read(MapBytes("town_invasion_0")), "town", QuestTypes.KillBoss, 5, 77, Dd1.Generation.PropsFor("town"), notes);
            Check.True(map.TileGrid, "a hand-made map says so");
            Check.Equal(7, map.Rooms.Count, "rooms (the one behind the locked door is left out)");
            Check.Equal(7, map.Hallways.Count, "hallways");
            Check.True(notes.LeftOut.Any(n => n.Contains("rooA")), "the room left out is named");
            Check.Equal(RoomType.Entrance, map.Rooms[map.EntranceId].Type, "the entrance");
            var boss = map.Rooms[map.FinalRoomId];
            Check.True(boss.Type == RoomType.Boss && boss.Battle != null && boss.Battle.Kind == EncounterKind.Boss, "the boss waits in DD1's final room");
            Check.Equal(1, map.Rooms.Count(r => r.Type == RoomType.Boss), "one boss room");
            Check.Equal("start", map.Rooms[map.EntranceId].Wall, "the entrance's wall is the one the map names");
            Check.True(map.Rooms.All(r => r.Wall == "start" || r.Wall == "altar" || r.Wall == "square"), "every room has one of the town's three walls");
            Check.Equal(4, map.Rooms.Count(r => r.Battle != null && r.Type != RoomType.Boss), "room fights");
            Check.Equal(2, map.Rooms.Count(r => r.Type == RoomType.Treasure && r.CurioId != null), "guarded treasures");
            var lengths = map.Hallways.Select(h => h.Segments.Count).ToList();
            Check.Equal("2,2,2,7,7,7,7", string.Join(",", lengths), "tiles between the doors, the short hallways first");
            Check.Equal(6, map.Hallways.Sum(h => h.Segments.Count(s => s.Content == HallContent.Battle)), "hallway fights");
            Check.Equal(4, map.Hallways.Sum(h => h.Segments.Count(s => s.Content == HallContent.Hunger)), "hunger tiles");
            Check.Equal(6, map.Hallways.Sum(h => h.Segments.Count(s => s.Content == HallContent.Curio && s.PropId != null)), "curio tiles, each with a curio of the town's list");
            Check.True(map.Hallways.All(h => h.Segments.All(s => s.MapX >= 0 && s.MapY >= 0)), "every tile knows its place on DD1's map");
            Check.True(map.Hallways.All(h => h.Segments.All(s => new[] { 0, 2, 3, 4, 5 }.Contains(s.Wall))), "the town's five corridor walls");
            Check.Equal(ObjectiveKind.KillBoss, map.Objective.Kind, "the goal");
            // every room can be walked to
            Check.True(map.Distances(map.EntranceId).All(d => d >= 0), "every room is joined to the entrance");
            Check.Equal(2, map.Distances(map.EntranceId)[map.FinalRoomId], "the boss is two hallways from the door");

            var again = DungeonMap.FromJson(map.ToJson());
            Check.True(again.TileGrid && again.Hallways[3].Segments[1].MapX == map.Hallways[3].Segments[1].MapX && again.Hallways[3].Segments[1].MapY == map.Hallways[3].Segments[1].MapY,
                "the tiles' places survive a save");
        }

        public static void AGeneratedMapKeepsNoTilePlaces()
        {
            var map = DungeonGenerator.Generate("crypts", QuestTypes.Explore, 1, 1, 5, Dd1.Generation);
            Check.True(!map.TileGrid && map.Hallways.All(h => h.Segments.All(s => s.MapX < 0)), "the generator's maps are as they were");
            Check.True(!DungeonMap.FromJson(map.ToJson()).TileGrid, "and stay so through a save");
        }

        public static void TheShriekersPerchIsOneRoomWithItsFight()
        {
            var source = Dd1Map.Read(MapBytes("crow_map1"));
            Check.Equal(1, source.Areas.Count, "areas");
            Check.Equal("crow_1", source.Areas[0].Tiles[0].Mash, "the room's fight");
            var map = FixedMap.Build(source, "weald", QuestTypes.KillBoss, 1, 3, Dd1.Generation.PropsFor("weald"));
            Check.True(map.Rooms.Count == 1 && map.Hallways.Count == 0, "one room, no hallway");
            Check.True(map.Rooms[0].Type == RoomType.Boss && map.EntranceId == 0 && map.Rooms[0].Wall == "entrance", "the only room is the boss's and shows the entrance's wall");

            var x = new Exploration(map, Dd1.Raid, Dd1.Curios, 9);
            var events = x.StartFight(ShriekerRules.Monster);
            Check.True(events.OfType<FightStarts>().Any(f => f.Wanderer == ShriekerRules.Monster), "the fight begins where the party stands");
            events = x.ResolveFight(FightOutcome.Won);
            Check.True(events.OfType<ObjectiveCompleted>().Any() && x.ObjectiveComplete, "the fight won is the quest's goal");
            Check.True(x.Leave().OfType<QuestEnded>().Any(e => e.Status == RaidStatus.Succeeded), "and the party goes home with it");
        }

        public static void FleeingAFightThatIsTheWholeQuestAbandonsIt()
        {
            var map = FixedMap.Build(Dd1Map.Read(MapBytes("crow_map1")), "weald", QuestTypes.KillBoss, 1, 3, Dd1.Generation.PropsFor("weald"));
            var x = new Exploration(map, Dd1.Raid, Dd1.Curios, 9);
            Check.Equal(0, x.FleeRaid().Count, "nothing to flee before the fight");
            x.StartFight(ShriekerRules.Monster);
            var events = x.FleeRaid();
            var ended = events.OfType<QuestEnded>().FirstOrDefault();
            Check.True(ended != null && ended.Status == RaidStatus.Abandoned && x.Status == RaidStatus.Abandoned, "the flight ends the quest as abandoned");
            Check.True(ended.Stress > 0, "with DD1's stress of giving up");
            Check.Equal(0, x.FleeRaid().Count, "once");
        }

        public static void ABrokenFileIsNotAMap()
        {
            var threw = false;
            try { Dd1Map.Read(new byte[100]); }
            catch (FormatException) { threw = true; }
            Check.True(threw, "bytes without DD1's magic are refused");
        }

        // ---- the quests' own rules ---------------------------------------------------------------------

        public static void Dd1sQuestsAreReadWithTheirOwnRules()
        {
            var rules = PlotEventRules.Load(Dd1.Files);
            var incursion = rules.Find("plot_town_invasion_0");
            Check.True(incursion != null, "the Incursion's quest");
            Check.True(incursion.Dungeon == "town" && incursion.Type == QuestTypes.KillBoss && incursion.Difficulty == 6 && incursion.Length == 1, "in the town, a boss, level 6, short");
            Check.Equal("town_invasion_0", incursion.MapName, "its map");
            Check.Equal("brigand_sapper_D", incursion.GoalMonster, "its goal's monster");
            Check.True(incursion.GeneratedByEvent && !incursion.Repeatable, "brought by its event, once");
            Check.True(incursion.CanRetreat && incursion.RetreatDeaths == 1 && !incursion.RetreatAlwaysFromRaid, "a retreat costs a hero");
            Check.True(incursion.RemoveOnIgnore.Count == 1 && incursion.RemoveOnIgnore[0].Tag == "building" && incursion.RemoveOnIgnore[0].Amount == 3, "ignored: three building steps");
            Check.Equal(0, incursion.RemoveOnFailure.Count, "failed: nothing more");

            var perch = rules.Find("plot_trinket_retention_0");
            Check.True(perch.Dungeon == "weald" && perch.Difficulty == 1 && perch.MapName == "crow_map1" && perch.GoalMonster == "crow_A", "the perch: Weald, level 1");
            Check.True(perch.RetentionCount == 8 && perch.RetentionMinRarity == "uncommon", "eight trinkets, uncommon or better");
            Check.True(!perch.CanRetreat && perch.RetreatAlwaysFromRaid && perch.Repeatable && !perch.GeneratedByEvent, "no way back but out; offered again");
            Check.Equal("rare", rules.Find("plot_trinket_retention_1").RetentionMinRarity, "the veteran quest's minimum");
            Check.Equal("very_rare", rules.Find("plot_trinket_retention_2").RetentionMinRarity, "the champion quest's minimum");
            Check.Equal(7, perch.QuirksOnCompletion.Count, "quirk lines on completion");
            Check.True(perch.QuirksOnCompletion[0].Quirks[0] == "corvids_curiosity" && perch.QuirksOnCompletion[0].Chance == 1 && perch.QuirksOnCompletion[3].Quirks[0] == "corvids_grace"
                       && perch.QuirksOnCompletion[3].Chance == 2, "the good ones weigh double when the quest is done");
            Check.True(perch.QuirksOnFailure[0].Chance == 2 && perch.QuirksOnFailure[3].Chance == 1, "and the bad ones when it is not");
            Check.True(perch.QuirksOnCompletion[6].Quirks.Count == 0 && perch.QuirksOnCompletion[6].Chance == 0, "the line without a quirk has no weight");

            var prize = rules.Find("plot_crow_trinket");
            Check.True(prize.GeneratedByEvent && prize.Repeatable && prize.Difficulty == 5 && prize.RetentionCount == 0, "the Prize: by its event, level 5, no hoard asked");
            var order = rules.Quests.Select(q => q.Id).ToList();
            Check.True(order.IndexOf("plot_trinket_retention_0") < order.IndexOf("plot_trinket_retention_1") && order.IndexOf("plot_trinket_retention_1") < order.IndexOf("plot_trinket_retention_2"),
                "the quests keep the file's order");
        }

        public static void Dd1sEventsAskWhatTheFilesSay()
        {
            var catalog = TownEventCatalog.Load(Dd1.Files);
            var incursion = catalog.Find("plot_quest_town_invasion_0");
            Check.True(incursion.BaseChance == 12 && incursion.PerNotRolled == 3 && incursion.Cooldown == 4 && incursion.MinimumWeek == 40 && incursion.Tone == "bad", "the Incursion: 12 +3, 4 visits, week 40");
            Check.True(incursion.HeroLevels.Count == 1 && incursion.HeroLevels[0].Level == 5 && incursion.HeroLevels[0].Count == 4, "four heroes of level 5");
            Check.True(incursion.EffectsOf(PlotEventRules.EventEffect).Single().Text == "plot_town_invasion_0", "it names its quest");
            var thief = catalog.Find(ShriekerRules.ThiefEffect);
            Check.True(thief.BaseChance == 5 && thief.PerNotRolled == 1 && thief.Unique && thief.MinimumWeek == 42 && thief.TrinketsInStorage == 8, "the thief: 5 +1, once, week 42, eight in the stores");
            Check.Equal(8.0, thief.EffectsOf(ShriekerRules.ThiefEffect).Single().Number, "it takes eight");
            var prize = catalog.Find("plot_quest_crow_trinket");
            Check.True(prize.BaseChance == 6 && prize.PerNotRolled == 2 && prize.Cooldown == 3 && prize.MinimumWeek == 15 && prize.Tone == "good", "the Prize: 6 +2, 3 visits, week 15");

            var situation = new TownEventSituation { Week = 42, TrinketsInStorage = 7 };
            Check.True(!TownEventRules.Meets(thief, situation), "seven trinkets do not tempt the thief");
            situation.TrinketsInStorage = 8;
            Check.True(TownEventRules.Meets(thief, situation), "eight do");
        }

        // ---- the Incursion ignored -----------------------------------------------------------------------

        private static List<UpgradeTreeTags> Trees(params string[] ids)
        {
            var trees = new List<UpgradeTreeTags>();
            foreach (var id in ids)
            {
                var tree = new UpgradeTreeTags { Id = id };
                tree.Tags.Add("building");
                tree.Codes.AddRange(new[] { "a", "b", "c", "d" });
                trees.Add(tree);
            }
            return trees;
        }

        public static void AnIgnoredQuestTakesTheLastStepOfDifferentTrees()
        {
            var trees = Trees("tavern.bar", "abbey.prayer", "guild.cost", "blacksmith.weapon", "sanitarium.slots");
            trees[4].Tags.Clear();      // not a building's tree
            var levels = new Dictionary<string, int> { { "tavern.bar", 3 }, { "abbey.prayer", 1 }, { "guild.cost", 0 }, { "blacksmith.weapon", 4 }, { "sanitarium.slots", 2 } };
            Func<string, string, bool> built = (tree, code) => "abcd".IndexOf(code, StringComparison.Ordinal) < levels[tree];

            var candidates = UpgradeRemoval.Candidates(trees, built, "building");
            Check.Equal("tavern.bar c, abbey.prayer a, blacksmith.weapon d", string.Join(", ", candidates), "each tree with something built offers its last step");

            for (var seed = 0; seed < 50; seed++)
            {
                var lost = UpgradeRemoval.Pick(trees, built, "building", 3, new Rng(seed));
                Check.Equal(3, lost.Count, "three steps");
                Check.Equal(3, lost.Select(l => l.Tree).Distinct().Count(), "of three different trees");
            }
            Check.Equal(3, UpgradeRemoval.Pick(trees, built, "building", 9, new Rng(1)).Count, "no more than there are trees with a step");
            levels["tavern.bar"] = levels["abbey.prayer"] = levels["blacksmith.weapon"] = 0;
            Check.Equal(0, UpgradeRemoval.Pick(trees, built, "building", 3, new Rng(1)).Count, "nothing built, nothing lost");

            // drawn evenly: with two of three to lose each tree is hit two times in three
            levels["tavern.bar"] = levels["abbey.prayer"] = levels["blacksmith.weapon"] = 2;
            var hits = 0;
            for (var seed = 0; seed < 3000; seed++)
                if (UpgradeRemoval.Pick(trees, built, "building", 2, new Rng(seed)).Any(l => l.Tree == "abbey.prayer")) hits++;
            Check.Near(2.0 / 3, hits / 3000.0, 0.04, "share of draws that hit one tree");
        }

        public static void EveryBuildingTreeOfDd1CarriesTheTag()
        {
            var trees = UpgradeRemoval.LoadTrees(Dd1.Files);
            Check.True(trees.Count >= 20, "DD1's building trees are read (" + trees.Count + ")");
            Check.True(trees.All(t => t.Tags.Contains("building") && t.Codes.Count > 0 && t.Codes[0] == "a"), "each is tagged 'building' and starts with step a");
            Check.True(trees.Any(t => t.Id == "stage_coach.rostersize" && t.Codes.Count == 5), "the coach's roster tree has five steps");
        }

        // ---- the Shrieker ------------------------------------------------------------------------------

        public static void TheShriekersNumbersComeFromTheFiles()
        {
            var rules = ShriekerRules.Load(Dd1.Files);
            Check.Equal(0, rules.Missing.Count, "nothing missing");
            Check.True(rules.EscapeTurn == 12 && rules.TurnsPerRound == 3, "it leaves from turn 12 on, at three turns a round");
            Check.Equal(4, rules.Rounds, "so the fight lasts four rounds");
            Check.True(rules.Rank("ancestral") < rules.Rank("crow") && rules.Rank("crow") < rules.Rank("very_rare") && rules.Rank("very_rare") < rules.Rank("rare")
                       && rules.Rank("rare") < rules.Rank("uncommon") && rules.Rank("uncommon") < rules.Rank("common"), "DD1's rarities, rarest first");
            Check.Equal(rules.Rarities.Count, rules.Rank("no such rarity"), "an unknown rarity is the commonest");
            var nest = ShriekerRules.NestLoot(Dd1.Files, 1);
            Check.True(nest != null && nest.Table == "NEST_A" && nest.Draws == 1, "the apprentice nest pays NEST_A once");
            Check.Equal("NEST_C", ShriekerRules.NestLoot(Dd1.Files, 5).Table, "the champion nest pays NEST_C");
            Check.True(Dd1.Loot != null, "(the loot tables are readable)");
        }

        public static void TheHoardBringsItsQuestAtEightTrinkets()
        {
            var shrieker = ShriekerRules.Load(Dd1.Files);
            var quests = PlotEventRules.Load(Dd1.Files).Quests;
            var seven = Enumerable.Repeat("very_rare", 7).ToList();
            Check.True(shrieker.QuestFor(seven, quests) == null, "seven trinkets bring no quest");
            var commons = Enumerable.Repeat("common", 8).ToList();
            Check.True(shrieker.QuestFor(commons, quests) == null, "eight common ones bring none either");
            var hoard = new List<string> { "common", "common", "uncommon", "common", "common", "common", "common", "common" };
            Check.Equal("plot_trinket_retention_0", shrieker.QuestFor(hoard, quests)?.Id, "one uncommon among eight brings the apprentice quest");
            hoard[0] = "ancestral";
            Check.Equal("plot_trinket_retention_0", shrieker.QuestFor(hoard, quests)?.Id, "DD1 tries its quests in the file's order: the first that fits is offered");
            // without the first quest the next that fits is taken
            Check.Equal("plot_trinket_retention_2", shrieker.QuestFor(hoard, quests.Where(q => q.Difficulty >= 5))?.Id, "the champion quest asks for very rare or better");
            Check.True(shrieker.QuestFor(hoard, quests.Where(q => q.Id == "plot_crow_trinket")) == null, "the Prize is not the hoard's quest");
        }

        public static void TheQuestGivesBackTheRarestEight()
        {
            var shrieker = ShriekerRules.Load(Dd1.Files);
            var quest = PlotEventRules.Load(Dd1.Files).Find("plot_trinket_retention_0");
            var hoard = new List<string> { "common", "rare", "common", "very_rare", "uncommon", "common", "rare", "common", "ancestral", "common", "uncommon" };
            var prize = shrieker.Prize(hoard, quest);
            Check.Equal(8, prize.Count, "eight");
            Check.Equal("8,3,1,6,4,10,0,2", string.Join(",", prize), "rarest first, and as they came among equals");
            Check.Equal(3, shrieker.Prize(hoard.Take(3).ToList(), quest).Count, "no more than there are");
        }

        public static void TheThiefTakesTheRarestOfTheStores()
        {
            var shrieker = ShriekerRules.Load(Dd1.Files);
            var stores = new List<string>();
            for (var i = 0; i < 12; i++) stores.Add("common");
            stores[2] = "ancestral";
            stores[5] = "very_rare";
            stores[9] = "rare";
            var seen = new HashSet<int>();
            for (var seed = 0; seed < 200; seed++)
            {
                var taken = shrieker.Steal(stores, 8, new Rng(seed));
                Check.Equal(8, taken.Count, "eight");
                Check.Equal(8, taken.Distinct().Count(), "different ones");
                Check.True(taken[0] == 2 && taken[1] == 5 && taken[2] == 9, "the three rare ones first");
                foreach (var index in taken.Skip(3)) seen.Add(index);
            }
            Check.Equal(9, seen.Count, "among the common ones the lot decides: every one of them goes sometimes");
            Check.Equal(3, shrieker.Steal(stores.Take(3).ToList(), 8, new Rng(1)).Count, "no more than the stores hold");
        }

        public static void EverySurvivorDrawsAQuirkAndTheOutcomeTipsTheScale()
        {
            var perch = PlotEventRules.Load(Dd1.Files).Find("plot_trinket_retention_0");
            var good = new HashSet<string> { "corvids_grace", "corvids_eye", "corvids_resilience" };
            int doneGood = 0, failedGood = 0;
            const int draws = 6000;
            for (var seed = 0; seed < draws; seed++)
            {
                var done = PlotEventRules.DrawQuirks(perch.QuirksOnCompletion, new Rng(seed));
                var failed = PlotEventRules.DrawQuirks(perch.QuirksOnFailure, new Rng(seed + 100000));
                Check.True(done.Count == 1 && failed.Count == 1, "the line without a quirk weighs nothing: every draw names one");
                if (good.Contains(done[0])) doneGood++;
                if (good.Contains(failed[0])) failedGood++;
            }
            Check.Near(6.0 / 9, doneGood / (double)draws, 0.03, "a good quirk after the quest is done");
            Check.Near(3.0 / 9, failedGood / (double)draws, 0.03, "a good quirk after it failed");
            Check.Equal(0, PlotEventRules.DrawQuirks(new List<PartyQuirkChance>(), new Rng(1)).Count, "no lines, no quirk");
        }
    }
}
