using System.Collections.Generic;
using System.Linq;
using DD2Estate.Core;
using Newtonsoft.Json.Linq;

namespace DD2Estate.CoreTests
{
    public static class RulesTests
    {
        public static void RngIsStable()
        {
            var a = new Rng(42);
            var b = new Rng(42);
            for (var i = 0; i < 1000; i++) Check.Equal(a.NextU64(), b.NextU64(), "same seed, same sequence");
            Check.True(new Rng(1).NextU64() != new Rng(2).NextU64(), "neighbouring seeds differ");
            Check.True(new Rng(0).NextU64() != 0, "seed 0 works");

            // The sequence is part of the save format and has to be the same on Mono and .NET: these numbers come
            // from an independent implementation of splitmix64 seeding + xorshift64* (shifts 12, 25, 27).
            var golden = new Rng(2026);
            Check.Equal(8352781564724613288UL, golden.NextU64(), "first number of seed 2026");
            Check.Equal(11183006460893451849UL, golden.NextU64(), "second number of seed 2026");
            Check.Equal(3043677940171885377UL, golden.NextU64(), "third number of seed 2026");
            var negative = new Rng(-1);
            Check.Equal(548566541892062739UL, negative.NextU64(), "first number of seed -1");
            Check.Equal(0.08410556472790731, negative.NextDouble(), "NextDouble of seed -1");
            Check.Equal(5, negative.Next(10), "Next(10) of seed -1");

            var first = new Rng(2026).NextU64();
            var again = Rng.FromState(new Rng(2026).State);
            Check.Equal(first, again.NextU64(), "state restore");

            var rng = new Rng(7);
            var buckets = new int[6];
            double sum = 0;
            const int n = 60000;
            for (var i = 0; i < n; i++)
            {
                var roll = rng.Range(2, 7);
                Check.True(roll >= 2 && roll <= 7, "range bounds");
                buckets[roll - 2]++;
                var x = rng.NextDouble();
                Check.True(x >= 0 && x < 1, "unit interval");
                sum += x;
            }
            foreach (var count in buckets) Check.Near(n / 6.0, count, n * 0.01, "even spread");
            Check.Near(0.5, sum / n, 0.01, "mean of NextDouble");

            var picks = new int[3];
            for (var i = 0; i < n; i++) picks[rng.PickWeighted(new double[] { 1, 0, 3 })]++;
            Check.True(picks[1] == 0, "zero weight is never picked");
            Check.Near(0.25, picks[0] / (double)n, 0.01, "weighted pick");
            Check.Equal(-1, rng.PickWeighted(new double[] { 0, 0 }), "nothing to pick");

            var list = Enumerable.Range(0, 20).ToList();
            rng.Shuffle(list);
            Check.True(list.OrderBy(x => x).SequenceEqual(Enumerable.Range(0, 20)) && !list.SequenceEqual(Enumerable.Range(0, 20)), "shuffle keeps the items");
        }

        public static void DarkestFilesParse()
        {
            var blocks = DarkestFile.Parse(
                "// comment\n" +
                "map:\n.size short\n.gridsize 4 3\n.connectivity 0.9\n.nudge_map_centre_bias -1\n.bias .5\n\n" +
                "hall_curios: .chance 10 .types crate sack // trailing\r\n" +
                "effect: .name \"Heal Stress TrapD\" .chance 100% .on_hit true\n");
            Check.Equal(3, blocks.Count, "blocks");
            Check.Equal("map", blocks[0].Name, "name");
            Check.Equal("short", blocks[0].Text("size"), "text");
            Check.True(blocks[0].Int("gridsize", 0, 0) == 4 && blocks[0].Int("gridsize", 1, 0) == 3, "two numbers");
            Check.Equal(0.9, blocks[0].Number("connectivity", 0, 0), "decimal");
            Check.Equal(-1.0, blocks[0].Number("nudge_map_centre_bias", 0, 0), "negative");
            Check.Equal(0.5, blocks[0].Number("bias", 0, 0), "a value that starts with a dot");
            Check.Equal(7, blocks[0].Int("missing", 0, 7), "fallback");
            Check.True(blocks[1].All("types").SequenceEqual(new[] { "crate", "sack" }), "list, comment cut");
            Check.Equal("Heal Stress TrapD", blocks[2].Text("name"), "quoted text");
            Check.Equal(0, DarkestFile.Parse(null).Count, "no file");
        }

        public static void MapBlocksMatchTheFile()
        {
            var raw = Dd1.RawMapBlocks();
            var maps = Dd1.Generation.Maps;
            Check.True(raw.Count >= 40, "map blocks in the DD1 file: " + raw.Count);
            Check.Equal(raw.Count, maps.Count, "map blocks read");
            for (var i = 0; i < raw.Count; i++)
            {
                var b = raw[i];
                var m = maps[i];
                int N(string key, int index = 0) => int.Parse(b[key][index]);
                Check.True(m.QuestType == b["quest_type"][0] && m.Size == b["size"][0] && m.Dungeon == b["dungeon_type"][0], "block " + i + " header");
                Check.True(m.Rooms == N("base_room_number") && m.Corridors == N("base_corridor_number") && m.Spacing == N("spacing"), "block " + i + " counts");
                Check.True(m.GridWidth == N("gridsize") && m.GridHeight == N("gridsize", 1) && m.MinFinalDistance == N("min_final_distance"), "block " + i + " grid");
                Check.True(m.HallBattles.Min == N("hallway_battle") && m.HallBattles.Max == N("hallway_battle", 1), "block " + i + " hallway battles");
                Check.True(m.HallCurios.Min == N("hallway_curio") && m.HallHunger.Max == N("hallway_hunger", 1) && m.HallTraps.Min == N("hallway_trap"), "block " + i + " hallway contents");
                Check.True(m.RoomBattlesTotal.Max == N("total_room_battles", 1) && m.RoomGuardedTreasures.Min == N("room_guarded_treasure") && m.SecretRooms.Max == N("secret_rooms", 1), "block " + i + " rooms");
                Check.True(m.Connectivity > 0 && m.Connectivity <= 1 && !m.IsFallback, "block " + i + " connectivity");
                // the grid has room for the rooms
                Check.True(m.GridWidth * m.GridHeight >= m.Rooms, "block " + i + " grid too small");
            }

            // combinations DD1 has no block for borrow the nearest size of the same quest type and dungeon
            var rules = Dd1.Generation;
            Check.True(rules.MapFor(QuestTypes.KillBoss, 1, "weald").Size == "medium" && rules.MapFor(QuestTypes.KillBoss, 1, "weald").Dungeon == "weald", "short boss quest");
            Check.Equal("medium", rules.MapFor(QuestTypes.Gather, 3, "cove").Size, "long gather quest");
            Check.Equal("long", rules.MapFor(QuestTypes.Explore, 4, "crypts").Size, "exhausting quest");
            Check.True(rules.MapFor("no_such_quest", 1, "crypts").IsFallback, "unknown quest type");
        }

        public static void RaidRulesComeFromTheInstall()
        {
            var rules = Dd1.Raid;
            var raw = JObject.Parse(Dd1.Files.ReadText("shared/rules.json"));
            Check.Equal((double)raw["scouting_chance_base"], rules.ScoutChanceBase, "scouting base");
            Check.Equal((double)raw["scouting_crit_success"], rules.ScoutCritChance, "scouting crit");
            Check.Equal((double)raw["hallway_hunger_HPrestore"], rules.HungerHeal, "hunger heal");
            Check.Equal((double)raw["hallway_hunger_starve_HPdmg"], rules.HungerStarveDamage, "hunger damage");
            Check.Equal((double)raw["trap_scout_disarm_bonus"], rules.TrapScoutDisarmBonus, "trap scout bonus");
            Check.Equal((double)raw["hallway_stress"]["hallway_per_tile_stress_damage_backing_up"], rules.BackingUpStress, "backing up stress");
            Check.Equal((double)raw["hallway_stress"]["hallway_per_tile_stress_damage_chance_fwd"], rules.WalkStressChance, "walk stress chance");
            Check.Equal(((JArray)raw["darkness"]["range_table"]).Count, rules.LightBands.Count, "light bands");
            Check.Equal(((JArray)raw["corridor_return_content"]).Count, rules.ReturnContents.Count, "return contents");
            Check.Equal((double)raw["surprise_room_party_base_chance"], rules.Number("surprise_room_party_base_chance", -5), "any other number by key");
            Check.Equal(-5.0, rules.Number("no_such_key", -5), "fallback for an unknown key");

            var penalties = (JArray)raw["difficulty_trap_base"];
            for (var tier = 0; tier < penalties.Count; tier++) Check.Equal((double)penalties[tier], rules.TrapPenalty(tier), "trap penalty at difficulty " + tier);
            Check.True(rules.TrapPenalty(5) > rules.TrapPenalty(1) && rules.TrapPenalty(99) == 0, "trap penalty grows with difficulty");

            // every trap and obstacle a dungeon places has its rules
            foreach (var dungeon in Dd1.Dungeons)
            {
                var props = Dd1.Generation.PropsFor(dungeon);
                foreach (var trap in props.Traps)
                {
                    Check.True(rules.Traps.TryGetValue(trap.Id, out var def), dungeon + ": trap " + trap.Id + " has no definition");
                    var apprentice = def.For(1);
                    var champion = def.For(5);
                    Check.True(apprentice.Level == 0 && champion.Level == 5 && def.For(3).Level == 3 && def.For(4).Level == 3, trap.Id + ": level picked by difficulty");
                    Check.True(apprentice.FailEffects.Length > 0 && champion.FailEffects.Length > 0, trap.Id + ": fail effects");
                    Check.True(champion.SuccessEffects.Length > 0, trap.Id + ": success effects are inherited");
                    Check.True(champion.Health <= apprentice.Health && champion.Health <= 0, trap.Id + ": health loss");
                    Check.True(!apprentice.FailEffects.SequenceEqual(champion.FailEffects) || champion.Health < apprentice.Health, trap.Id + ": harsher at champion level");
                }
            }
            Check.True(rules.Obstacle.ClearItem == "shovel" && rules.Obstacle.Torchlight < 0 && rules.Obstacle.Health < 0 && rules.Obstacle.FailEffects.Length > 0, "obstacle rules");

            // forcing an obstacle with DD1's rules costs torchlight, the shovel does not
            var map = new DungeonMap { DungeonId = "crypts", QuestType = QuestTypes.Explore, Tier = 1, Width = 2, Height = 1 };
            map.Rooms.Add(new Room { Id = 0, Type = RoomType.Entrance });
            map.Rooms.Add(new Room { Id = 1, X = 1, Type = RoomType.Empty });
            var hall = new Hallway { Id = 0, RoomA = 0, RoomB = 1 };
            hall.Segments.Add(new Segment { Content = HallContent.Obstacle, PropId = "rubble" });
            hall.Segments.Add(new Segment { Content = HallContent.Obstacle, PropId = "rubble" });
            map.Hallways.Add(hall);
            map.Objective = new QuestObjective { Kind = ObjectiveKind.Explore, Required = 2 };
            var ex = new Exploration(map, rules, null, 1);
            ex.MoveTo(1);
            var light = ex.Light;
            ex.ResolveObstacle(true);
            Check.Equal(light, ex.Light, "shovel");
            ex.MoveTo(1);
            light = ex.Light;
            var events = ex.ResolveObstacle(false);
            Check.Equal(light + rules.Obstacle.Torchlight, ex.Light, "by hand");
            Check.Equal(1, events.OfType<LightChanged>().Count(), "light event");
        }

        public static void FallbackRulesAreUsable()
        {
            var rules = RaidRules.Load(new NoDd1Files());
            Check.Equal(1, rules.LightBands.Count, "one band without DD1");
            Check.Equal(0, rules.BandIndex(100), "band at full light");
            Check.Equal(0, rules.BandIndex(0), "band in the dark");
            Check.True(rules.LightLossUnknown > 0 && rules.ScoutChanceBase > 0 && rules.Traps.Count == 0 && rules.ReturnContents.Count == 0, "fallback values");
            Check.Equal(0, CurioCatalog.Load(new NoDd1Files()).All.Count, "no curios without DD1");
            Check.Equal(0, LootTables.Load(new NoDd1Files()).Draw("A", 3, 1, "crypts", new Rng(1)).Count, "no loot tables without DD1");
            var props = GenerationRules.Load(new NoDd1Files()).PropsFor("crypts");
            Check.True(props.HallCurios.Count == 0 && props.CorridorWalls.Count == 0, "no props without DD1");
        }
    }
}
