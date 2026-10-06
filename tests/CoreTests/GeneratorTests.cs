using System;
using System.Collections.Generic;
using System.Linq;
using DD2Estate.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DD2Estate.CoreTests
{
    public static class GeneratorTests
    {
        private static readonly string[] SizeNames = { "", "short", "medium", "long" };

        // (quest type, length) pairs DD1 has a map block for
        private static IEnumerable<(string quest, int length)> Dd1Quests()
        {
            foreach (var length in new[] { 1, 2, 3 })
            {
                yield return (QuestTypes.Explore, length);
                yield return (QuestTypes.Cleanse, length);
            }
            yield return (QuestTypes.KillBoss, 2);
            yield return (QuestTypes.KillBoss, 3);
            yield return (QuestTypes.Gather, 2);
            yield return (QuestTypes.Activate, 2);
            yield return (QuestTypes.InventoryActivate, 2);
        }

        private static DungeonMap Make(string dungeon, string quest, int length, long seed, int tier = 1)
        {
            return DungeonGenerator.Generate(dungeon, quest, length, tier, seed, Dd1.Generation);
        }

        private static int[] Range(Dictionary<string, string[]> block, string key) => block[key].Select(int.Parse).ToArray();

        public static void SameSeedSameMap()
        {
            foreach (var dungeon in Dd1.Dungeons)
                foreach (var (quest, length) in Dd1Quests())
                {
                    // through the public entry point, which reads the files again each time
                    var a = DungeonGenerator.Generate(dungeon, quest, length, 3, 12345, Dd1.Files).ToJson().ToString(Formatting.None);
                    var b = DungeonGenerator.Generate(dungeon, quest, length, 3, 12345, Dd1.Files).ToJson().ToString(Formatting.None);
                    Check.Equal(a, b, dungeon + " " + quest + " " + length);
                    var c = Make(dungeon, quest, length, 12346, 3).ToJson().ToString(Formatting.None);
                    Check.True(a != c, "another seed gave the same map: " + dungeon + " " + quest + " " + length);
                }
        }

        public static void MapJsonRoundTrip()
        {
            foreach (var dungeon in Dd1.Dungeons)
                foreach (var (quest, length) in Dd1Quests())
                    for (var seed = 0; seed < 20; seed++)
                    {
                        var text = Make(dungeon, quest, length, seed).ToJson().ToString(Formatting.None);
                        var again = DungeonMap.FromJson(JObject.Parse(text)).ToJson().ToString(Formatting.None);
                        Check.Equal(text, again, "round trip " + dungeon + " " + quest + " " + length + " seed " + seed);
                    }
        }

        public static void EveryRoomReachable()
        {
            foreach (var dungeon in Dd1.Dungeons)
                foreach (var (quest, length) in Dd1Quests())
                    for (var seed = 0; seed < 150; seed++)
                    {
                        var map = Make(dungeon, quest, length, seed);
                        var where = dungeon + " " + quest + " " + length + " seed " + seed;
                        var dist = map.Distances(map.EntranceId);
                        var cells = new HashSet<int>();
                        foreach (var room in map.Rooms)
                        {
                            if (room.Type == RoomType.Secret)
                            {
                                var doors = map.Hallways.SelectMany(h => h.Segments).Count(s => s.Content == HallContent.Secret && s.SecretRoomId == room.Id);
                                Check.Equal(1, doors, "doors into secret room, " + where);
                                continue;
                            }
                            Check.True(dist[room.Id] >= 0, "room " + room.Id + " cannot be reached, " + where);
                            Check.True(room.X >= 0 && room.X < map.Width && room.Y >= 0 && room.Y < map.Height, "room off the grid, " + where);
                            Check.True(cells.Add(room.Y * map.Width + room.X), "two rooms in one cell, " + where);
                        }
                        Check.Equal(RoomType.Entrance, map.Rooms[map.EntranceId].Type, "entrance type, " + where);
                        Check.Equal(1, map.Rooms.Count(r => r.Type == RoomType.Entrance), "entrances, " + where);
                        var pairs = new HashSet<long>();
                        foreach (var hall in map.Hallways)
                        {
                            Room a = map.Rooms[hall.RoomA], b = map.Rooms[hall.RoomB];
                            Check.Equal(1, Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y), "hallway between rooms that are not neighbours, " + where);
                            Check.True(pairs.Add(hall.RoomA * 1000L + hall.RoomB), "two hallways between the same rooms, " + where);
                        }
                    }
        }

        public static void RoomAndHallwayCountsMatchDd1()
        {
            foreach (var dungeon in Dd1.Dungeons)
                foreach (var (quest, length) in Dd1Quests())
                {
                    var block = Dd1.RawMapBlock(quest, SizeNames[length], dungeon);
                    Check.True(block != null, "no DD1 block for " + quest + " " + SizeNames[length] + " " + dungeon);
                    var rooms = int.Parse(block["base_room_number"][0]);
                    var corridors = int.Parse(block["base_corridor_number"][0]);
                    var spacing = int.Parse(block["spacing"][0]);
                    var minDistance = int.Parse(block["min_final_distance"][0]);
                    var connectivity = double.Parse(block["connectivity"][0], System.Globalization.CultureInfo.InvariantCulture);
                    var loops = 0;
                    const int seeds = 150;
                    for (var seed = 0; seed < seeds; seed++)
                    {
                        var map = Make(dungeon, quest, length, seed);
                        var where = dungeon + " " + quest + " " + length + " seed " + seed;
                        Check.Equal(rooms, map.GridRoomCount, "rooms, " + where);
                        // connected needs rooms - 1; DD1's corridor number is the ceiling when it is higher
                        Check.True(map.Hallways.Count >= rooms - 1 && map.Hallways.Count <= Math.Max(rooms - 1, corridors), "hallways " + map.Hallways.Count + ", " + where);
                        loops += map.Hallways.Count - (rooms - 1);
                        foreach (var hall in map.Hallways)
                            Check.Equal(spacing, hall.Segments.Count, "segments per hallway, " + where);
                        // DD1's min_final_distance: some room is at least that far from the entrance
                        var far = map.Distances(map.EntranceId)[map.FinalRoomId];
                        Check.True(far >= minDistance, "final room only " + far + " away (DD1 minimum " + minDistance + "), " + where);
                    }
                    // each loop DD1 allows is built with the connectivity chance
                    var allowed = Math.Max(0, corridors - (rooms - 1));
                    Check.Near(allowed * connectivity, loops / (double)seeds, 0.03 + 0.12 * allowed, "loops per map, " + dungeon + " " + quest + " " + length);
                }

            // the sizes people know DD1 by, straight from the player's file
            Check.Equal(9, Make("crypts", QuestTypes.Explore, 1, 1).GridRoomCount, "short explore");
            Check.Equal(14, Make("crypts", QuestTypes.Explore, 2, 1).GridRoomCount, "medium explore");
            Check.Equal(19, Make("crypts", QuestTypes.Explore, 3, 1).GridRoomCount, "long explore");
            Check.Equal(3, Make("warrens", QuestTypes.Explore, 1, 1).Hallways[0].Segments.Count, "warrens hallway length");
        }

        public static void BossRoomIsAFarDeadEnd()
        {
            foreach (var dungeon in Dd1.Dungeons)
                foreach (var length in new[] { 2, 3 })
                {
                    var minDistance = int.Parse(Dd1.RawMapBlock(QuestTypes.KillBoss, SizeNames[length], dungeon)["min_final_distance"][0]);
                    for (var seed = 0; seed < 300; seed++)
                    {
                        var map = Make(dungeon, QuestTypes.KillBoss, length, seed, 5);
                        var where = dungeon + " boss " + length + " seed " + seed;
                        var bosses = map.Rooms.Where(r => r.Type == RoomType.Boss).ToList();
                        Check.Equal(1, bosses.Count, "boss rooms, " + where);
                        var boss = bosses[0];
                        Check.Equal(map.FinalRoomId, boss.Id, "boss room is the final room, " + where);
                        Check.True(boss.Battle != null && boss.Battle.Kind == EncounterKind.Boss && boss.Battle.Tier == 5, "boss fight slot, " + where);
                        Check.Equal(1, map.HallwaysOf(boss.Id).Count, "doors of the boss room, " + where);
                        var dist = map.Distances(map.EntranceId);
                        Check.Equal(dist.Max(), dist[boss.Id], "boss is not the farthest room, " + where);
                        Check.True(dist[boss.Id] >= minDistance, "boss only " + dist[boss.Id] + " rooms away (DD1 minimum " + minDistance + "), " + where);
                        Check.Equal(ObjectiveKind.KillBoss, map.Objective.Kind, "objective, " + where);
                    }
                }
            foreach (var (quest, length) in Dd1Quests().Where(q => q.quest != QuestTypes.KillBoss))
                Check.Equal(0, Make("crypts", quest, length, 7).Rooms.Count(r => r.Type == RoomType.Boss), "boss room in a " + quest + " quest");
        }

        public static void ContentCountsFollowDd1Ranges()
        {
            const int seeds = 300;
            foreach (var dungeon in Dd1.Dungeons)
                foreach (var (quest, length) in Dd1Quests())
                {
                    var block = Dd1.RawMapBlock(quest, SizeNames[length], dungeon);
                    var label = dungeon + " " + quest + " " + length;
                    var keys = new[] { "hallway_battle", "hallway_trap", "hallway_obstacle", "hallway_hunger", "total_room_battles", "secret_rooms" };
                    var sums = new double[keys.Length];
                    for (var seed = 0; seed < seeds; seed++)
                    {
                        var map = Make(dungeon, quest, length, seed);
                        var where = label + " seed " + seed;
                        var tiles = map.Hallways.SelectMany(h => h.Segments).ToList();
                        var fights = map.Rooms.Count(r => r.Battle != null && r.Type != RoomType.Boss);
                        var counts = new[]
                        {
                            tiles.Count(s => s.Content == HallContent.Battle),
                            tiles.Count(s => s.Content == HallContent.Trap),
                            tiles.Count(s => s.Content == HallContent.Obstacle),
                            tiles.Count(s => s.Content == HallContent.Hunger),
                            fights,
                            map.Rooms.Count(r => r.Type == RoomType.Secret)
                        };
                        for (var k = 0; k < keys.Length; k++)
                        {
                            var range = Range(block, keys[k]);
                            Check.True(counts[k] >= range[0] && counts[k] <= range[1], keys[k] + " " + counts[k] + " outside " + range[0] + "-" + range[1] + ", " + where);
                            sums[k] += counts[k];
                        }

                        // curios fill what the rest leaves, up to DD1's number
                        var curios = tiles.Count(s => s.Content == HallContent.Curio);
                        var wanted = Range(block, "hallway_curio");
                        var free = tiles.Count(s => s.Content == HallContent.Empty);
                        Check.True(curios <= wanted[1] && (curios >= wanted[0] || free == 0), "hallway curios " + curios + ", " + where);

                        var guardedCurios = map.Rooms.Count(r => r.Type == RoomType.Curio && r.Battle != null && !r.QuestCurio);
                        var guardedTreasures = map.Rooms.Count(r => r.Type == RoomType.Treasure && r.Battle != null);
                        Check.True(guardedCurios <= Range(block, "room_guarded_curio")[1], "guarded curio rooms, " + where);
                        Check.True(guardedTreasures <= Range(block, "room_guarded_treasure")[1], "guarded treasure rooms, " + where);
                        Check.True(map.Rooms.Where(r => r.Type == RoomType.Curio || r.Type == RoomType.Treasure || r.Type == RoomType.Secret).All(r => r.CurioId != null), "curio room without a curio, " + where);
                        Check.True(map.Rooms.Where(r => r.Type == RoomType.Battle).All(r => r.Battle != null && r.Battle.Kind == EncounterKind.Room), "battle room without a fight, " + where);
                        Check.True(map.Rooms[map.EntranceId].Battle == null && map.Rooms[map.EntranceId].CurioId == null, "the entrance is not empty, " + where);
                        foreach (var s in tiles)
                        {
                            Check.Equal(s.Content == HallContent.Battle, s.Battle != null, "hallway fight slot, " + where);
                            var needsProp = s.Content == HallContent.Curio || s.Content == HallContent.Trap || s.Content == HallContent.Obstacle;
                            Check.Equal(needsProp, s.PropId != null, "hallway prop id, " + where);
                        }
                    }

                    // a count is rolled evenly inside its range, so its mean sits in the middle
                    for (var k = 0; k < keys.Length; k++)
                    {
                        var range = Range(block, keys[k]);
                        var width = range[1] - range[0];
                        Check.Near((range[0] + range[1]) / 2.0, sums[k] / seeds, 0.05 + 0.12 * width, "mean " + keys[k] + ", " + label);
                    }
                }
        }

        public static void CuriosFollowDd1Weights()
        {
            foreach (var dungeon in Dd1.Dungeons)
            {
                var props = Dd1.Generation.PropsFor(dungeon);
                Check.True(props.HallCurios.Count >= 10 && props.RoomCurios.Count >= 4 && props.RoomTreasures.Count == 3, "props of " + dungeon);
                Check.True(props.CorridorWalls.Count >= 6 && props.RoomWalls.Count >= 7, "wall variants of " + dungeon);

                var hall = new Dictionary<string, int>();
                var treasure = new Dictionary<string, int>();
                var traps = new HashSet<string>();
                var obstacles = new HashSet<string>();
                var walls = new HashSet<int>();
                for (var seed = 0; seed < 700; seed++)
                {
                    var map = Make(dungeon, QuestTypes.Explore, 3, seed);
                    foreach (var s in map.Hallways.SelectMany(h => h.Segments))
                    {
                        walls.Add(s.Wall);
                        if (s.Content == HallContent.Curio) hall[s.PropId] = hall.TryGetValue(s.PropId, out var n) ? n + 1 : 1;
                        if (s.Content == HallContent.Trap) traps.Add(s.PropId);
                        if (s.Content == HallContent.Obstacle) obstacles.Add(s.PropId);
                    }
                    foreach (var r in map.Rooms.Where(r => r.Type == RoomType.Treasure))
                        treasure[r.CurioId] = treasure.TryGetValue(r.CurioId, out var n) ? n + 1 : 1;
                    foreach (var r in map.Rooms.Where(r => r.Type != RoomType.Secret && r.Type != RoomType.Entrance))
                        Check.True(props.RoomWalls.Contains(r.Wall), "unknown room wall " + r.Wall);
                    Check.Equal(props.Traps[0].Id, map.TrapId, "map trap id");
                }
                CompareShares(props.HallCurios, hall, 0.012, dungeon + " hallway curios");
                CompareShares(props.RoomTreasures, treasure, 0.04, dungeon + " room treasures");
                Check.True(traps.SetEquals(props.Traps.Select(t => t.Id)), dungeon + " traps");
                Check.True(obstacles.SetEquals(props.Obstacles.Select(t => t.Id)), dungeon + " obstacles");
                Check.True(walls.SetEquals(props.CorridorWalls), dungeon + " corridor walls used");
            }
        }

        private static void CompareShares(List<WeightedId> weights, Dictionary<string, int> counts, double tolerance, string what)
        {
            double total = counts.Values.Sum(), weightSum = weights.Sum(w => w.Weight);
            Check.True(total > 1000, what + ": too few samples");
            foreach (var group in weights.GroupBy(w => w.Id))
            {
                counts.TryGetValue(group.Key, out var n);
                Check.Near(group.Sum(w => w.Weight) / weightSum, n / total, tolerance, what + " share of " + group.Key);
            }
            Check.True(counts.Keys.All(id => weights.Any(w => w.Id == id)), what + ": an id that is not in the DD1 list");
        }

        public static void ObjectivesComeFromDd1Goals()
        {
            foreach (var dungeon in Dd1.Dungeons)
                for (var seed = 0; seed < 40; seed++)
                {
                    var explore = Make(dungeon, QuestTypes.Explore, 2, seed);
                    Check.Equal(ObjectiveKind.Explore, explore.Objective.Kind, "explore kind");
                    Check.Equal("explore_all_rooms", explore.Objective.GoalId, "explore goal");
                    var share = (double)Dd1.Generation.GoalFor(QuestTypes.Explore, dungeon).Percentage;
                    Check.Equal((int)Math.Ceiling(explore.GridRoomCount * share - 1e-9), explore.Objective.Required, "rooms to explore");
                    Check.True(explore.Objective.Required < explore.GridRoomCount, "DD1 does not ask for every room");

                    var cleanse = Make(dungeon, QuestTypes.Cleanse, 2, seed);
                    Check.Equal(cleanse.Rooms.Count(r => r.Battle != null), cleanse.Objective.Required, "room battles to win");

                    var gather = Make(dungeon, QuestTypes.Gather, 2, seed);
                    var goal = Dd1.Generation.GoalFor(QuestTypes.Gather, dungeon);
                    Check.Equal(ObjectiveKind.Gather, gather.Objective.Kind, "gather kind");
                    Check.Equal(goal.Amount, gather.Objective.Required, "items to gather");
                    Check.Equal(goal.Amount, gather.Rooms.Count(r => r.QuestCurio && r.CurioId == goal.CurioId), "gather curios on the map");
                    Check.True(goal.Amount > 0 && goal.ItemId != null && gather.Objective.ItemId == goal.ItemId, "gather item");

                    var activate = Make(dungeon, QuestTypes.InventoryActivate, 2, seed);
                    goal = Dd1.Generation.GoalFor(QuestTypes.InventoryActivate, dungeon);
                    Check.Equal(ObjectiveKind.Activate, activate.Objective.Kind, "activate kind");
                    Check.Equal(goal.Amount, activate.Rooms.Count(r => r.QuestCurio && r.CurioId == goal.CurioId), "activation curios on the map");
                    Check.True(goal.StartingItems >= goal.Amount && activate.Objective.StartingItems == goal.StartingItems && activate.Objective.ItemId == goal.ItemId, "items to activate with");

                    var plain = Make(dungeon, QuestTypes.Activate, 2, seed);
                    Check.True(plain.Objective.ItemId == null && plain.Objective.StartingItems == 0 && plain.Rooms.Count(r => r.QuestCurio) == plain.Objective.Required, "plain activate");
                }
            // the four dungeons do not share quest curios
            Check.Equal(4, Dd1.Dungeons.Select(d => Dd1.Generation.GoalFor(QuestTypes.Gather, d).CurioId).Distinct().Count(), "gather curios per dungeon");
        }

        public static void WorksWithoutDd1()
        {
            var files = new NoDd1Files();
            var rules = GenerationRules.Load(files);
            Check.True(rules.MapFor(QuestTypes.Explore, 1, "crypts").IsFallback, "fallback rules expected");
            var raid = RaidRules.Load(files);
            var curios = CurioCatalog.Load(files);
            foreach (var quest in QuestTypes.All)
                foreach (var length in new[] { 1, 2, 3 })
                    for (var seed = 0; seed < 30; seed++)
                    {
                        var map = DungeonGenerator.Generate("crypts", quest, length, 1, seed, files);
                        Check.True(map.Distances(map.EntranceId).Where((d, i) => map.Rooms[i].Type != RoomType.Secret).All(d => d >= 0), "fallback map not connected");
                        var ex = new Exploration(map, raid, curios, seed);
                        new Explorer(seed).Run(ex);
                        Check.True(ex.ObjectiveComplete, "fallback " + quest + " " + length + " seed " + seed + " cannot be completed");
                    }
        }
    }
}
