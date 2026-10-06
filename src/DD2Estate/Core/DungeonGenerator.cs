using System;
using System.Collections.Generic;
using System.Linq;

namespace DD2Estate.Core
{
    /// <summary>
    /// Builds a DD1-style dungeon from the numbers in the player's DD1 install. DD1's own generator is in its
    /// executable; what the data files give are counts, ranges and weights, and this follows them. The same seed
    /// gives the same map on every machine and runtime.
    /// </summary>
    public static class DungeonGenerator
    {
        // A layout that misses DD1's min_final_distance, or has no room for the loops DD1 asks for, is rebuilt;
        // after this many tries the best one is taken.
        private const int LayoutAttempts = 200;

        public static DungeonMap Generate(string dungeonId, string questType, int length, int tier, long seed, IDd1Files files)
        {
            return Generate(dungeonId, questType, length, tier, seed, GenerationRules.Load(files));
        }

        public static DungeonMap Generate(string dungeonId, string questType, int length, int tier, long seed, GenerationRules rules)
        {
            var r = rules.MapFor(questType, length, dungeonId);
            var props = rules.PropsFor(dungeonId);
            var rng = new Rng(seed);
            var boss = questType == QuestTypes.KillBoss;

            Layout layout = null;
            for (var attempt = 0; attempt < LayoutAttempts; attempt++)
            {
                var candidate = Layout.Grow(r, rng);
                candidate.PlaceEnds(rng, r.MinFinalDistance, boss);
                candidate.AddLoops(r, rng, boss);
                if (layout == null || candidate.Score(r) > layout.Score(r)) layout = candidate;
                if (candidate.FinalDistance >= r.MinFinalDistance && candidate.MissingLoops == 0) break;
            }

            var map = new DungeonMap
            {
                DungeonId = dungeonId,
                QuestType = questType,
                Length = length,
                Tier = tier,
                Seed = seed,
                Width = r.GridWidth,
                Height = r.GridHeight,
                EntranceId = layout.Entrance,
                FinalRoomId = layout.Final,
                TrapId = props.Traps.Count > 0 ? props.Traps[0].Id : FallbackTrap
            };
            for (var i = 0; i < layout.Cells.Count; i++)
                map.Rooms.Add(new Room { Id = i, X = layout.Cells[i] % r.GridWidth, Y = layout.Cells[i] / r.GridWidth, Type = RoomType.Empty });
            foreach (var edge in layout.Edges)
            {
                var hall = new Hallway { Id = map.Hallways.Count, RoomA = Math.Min(edge.A, edge.B), RoomB = Math.Max(edge.A, edge.B) };
                for (var s = 0; s < Math.Max(1, r.Spacing); s++) hall.Segments.Add(new Segment());
                map.Hallways.Add(hall);
            }

            var goal = rules.GoalFor(questType, dungeonId) ?? FallbackGoal(questType);
            FillRooms(map, r, props, goal, rng, boss);
            FillHallways(map, r, props, rng);
            PickWalls(map, props, rng);
            map.Objective = BuildObjective(map, questType, goal);
            return map;
        }

        private struct Edge
        {
            public int A, B;
        }

        /// <summary>Rooms on grid cells and the hallways between neighbouring cells.</summary>
        private sealed class Layout
        {
            public readonly List<int> Cells = new List<int>();
            public readonly List<Edge> Edges = new List<Edge>();
            public int Entrance, Final, FinalDistance;
            /// <summary>Loops DD1 asks for that found no place in this layout.</summary>
            public int MissingLoops;
            private int _w, _h;
            private int[] _roomAt;

            /// <summary>
            /// Grows a tree of rooms: each new room takes a free cell next to the rooms so far and is joined to one
            /// of them, so every room can be reached.
            /// </summary>
            public static Layout Grow(MapRules r, Rng rng)
            {
                var l = new Layout { _w = Math.Max(1, r.GridWidth), _h = Math.Max(1, r.GridHeight) };
                var cells = l._w * l._h;
                l._roomAt = new int[cells];
                for (var i = 0; i < cells; i++) l._roomAt[i] = -1;
                var count = Math.Max(2, Math.Min(r.Rooms, cells));

                l.Put(rng.Next(cells));
                var frontier = new List<int>();
                var weights = new List<double>();
                var neighbours = new List<int>();
                while (l.Cells.Count < count)
                {
                    frontier.Clear();
                    weights.Clear();
                    for (var cell = 0; cell < cells; cell++)
                    {
                        if (l._roomAt[cell] >= 0) continue;
                        l.RoomsNextTo(cell, neighbours);
                        if (neighbours.Count == 0) continue;
                        frontier.Add(cell);
                        weights.Add(l.CellWeight(cell, neighbours.Count, r));
                    }
                    if (frontier.Count == 0) break;
                    var pick = frontier[rng.PickWeighted(weights)];
                    l.RoomsNextTo(pick, neighbours);
                    var parent = neighbours[rng.Next(neighbours.Count)];
                    l.Edges.Add(new Edge { A = parent, B = l.Put(pick) });
                }
                return l;
            }

            private int Put(int cell)
            {
                _roomAt[cell] = Cells.Count;
                Cells.Add(cell);
                return Cells.Count - 1;
            }

            private void RoomsNextTo(int cell, List<int> rooms)
            {
                rooms.Clear();
                int x = cell % _w, y = cell / _w;
                if (y > 0 && _roomAt[cell - _w] >= 0) rooms.Add(_roomAt[cell - _w]);
                if (x < _w - 1 && _roomAt[cell + 1] >= 0) rooms.Add(_roomAt[cell + 1]);
                if (y < _h - 1 && _roomAt[cell + _w] >= 0) rooms.Add(_roomAt[cell + _w]);
                if (x > 0 && _roomAt[cell - 1] >= 0) rooms.Add(_roomAt[cell - 1]);
            }

            // The nudge_* biases of the map block, read as: a positive neighbour bias packs rooms together, a
            // positive centre bias keeps them near the middle of the grid (inferred from the names). Plain
            // arithmetic only, so the weights are identical on every runtime.
            private double CellWeight(int cell, int roomsNextTo, MapRules r)
            {
                double cx = (_w - 1) / 2.0, cy = (_h - 1) / 2.0;
                var dx = Math.Abs(cell % _w - cx) / Math.Max(cx, 0.5);
                var dy = Math.Abs(cell / _w - cy) / Math.Max(cy, 0.5);
                var fromCentre = (dx + dy) / 2;
                var weight = 1 + r.NeighbourBias * 0.5 * (roomsNextTo - 1) + r.CentreBias * (0.5 - fromCentre);
                return Math.Max(0.1, weight);
            }

            private int[][] AllDistances()
            {
                var n = Cells.Count;
                var adjacent = new List<int>[n];
                for (var i = 0; i < n; i++) adjacent[i] = new List<int>();
                foreach (var e in Edges)
                {
                    adjacent[e.A].Add(e.B);
                    adjacent[e.B].Add(e.A);
                }
                var all = new int[n][];
                var queue = new Queue<int>();
                for (var from = 0; from < n; from++)
                {
                    var dist = new int[n];
                    for (var i = 0; i < n; i++) dist[i] = -1;
                    dist[from] = 0;
                    queue.Enqueue(from);
                    while (queue.Count > 0)
                    {
                        var room = queue.Dequeue();
                        foreach (var next in adjacent[room])
                        {
                            if (dist[next] >= 0) continue;
                            dist[next] = dist[room] + 1;
                            queue.Enqueue(next);
                        }
                    }
                    all[from] = dist;
                }
                return all;
            }

            private int[] Degrees()
            {
                var degree = new int[Cells.Count];
                foreach (var e in Edges)
                {
                    degree[e.A]++;
                    degree[e.B]++;
                }
                return degree;
            }

            /// <summary>
            /// Entrance and final room. Boss quests: the two ends of the longest walk, the boss in a dead end.
            /// Other quests: any entrance from which some room is at least min_final_distance away.
            /// </summary>
            public void PlaceEnds(Rng rng, int minDistance, bool boss)
            {
                var dist = AllDistances();
                var degree = Degrees();
                var n = Cells.Count;
                var far = new int[n];
                for (var e = 0; e < n; e++)
                    for (var f = 0; f < n; f++)
                        if (f != e && degree[f] == 1 && dist[e][f] > far[e]) far[e] = dist[e][f];
                var longest = far.Max();

                var entrances = new List<int>();
                for (var e = 0; e < n; e++)
                    if (boss || longest < minDistance ? far[e] == longest : far[e] >= minDistance) entrances.Add(e);
                Entrance = entrances[rng.Next(entrances.Count)];

                var finals = new List<int>();
                for (var f = 0; f < n; f++)
                    if (f != Entrance && degree[f] == 1 && dist[Entrance][f] == far[Entrance]) finals.Add(f);
                Final = finals.Count > 0 ? finals[rng.Next(finals.Count)] : Entrance;
                FinalDistance = far[Entrance];
            }

            /// <summary>
            /// Hallways beyond the tree, up to base_corridor_number; each is built with the connectivity chance.
            /// A loop never brings the final room nearer than DD1's minimum. In a boss quest it does not bring it
            /// nearer at all and does not touch it: the boss stays in a dead end and no room is farther away.
            /// </summary>
            public void AddLoops(MapRules r, Rng rng, bool boss)
            {
                var candidates = new List<Edge>();
                for (var cell = 0; cell < _roomAt.Length; cell++)
                {
                    var a = _roomAt[cell];
                    if (a < 0) continue;
                    if (cell % _w < _w - 1) Candidate(a, _roomAt[cell + 1], candidates);
                    if (cell / _w < _h - 1) Candidate(a, _roomAt[cell + _w], candidates);
                }
                rng.Shuffle(candidates);
                var wanted = Math.Max(0, r.Corridors - Edges.Count);
                var keep = boss ? FinalDistance : Math.Min(FinalDistance, r.MinFinalDistance);
                // a site is a place where a loop fits; whether it gets built there is the connectivity roll
                var sites = 0;
                foreach (var c in candidates)
                {
                    if (sites >= wanted) break;
                    if (boss && (c.A == Final || c.B == Final)) continue;
                    Edges.Add(c);
                    if (AllDistances()[Entrance][Final] < keep)
                    {
                        Edges.RemoveAt(Edges.Count - 1);
                        continue;
                    }
                    sites++;
                    if (!rng.Chance(r.Connectivity)) Edges.RemoveAt(Edges.Count - 1);
                }
                MissingLoops = wanted - sites;
            }

            public int Score(MapRules r) => Math.Min(FinalDistance, r.MinFinalDistance) * 100 - MissingLoops;

            private void Candidate(int a, int b, List<Edge> candidates)
            {
                if (b < 0) return;
                foreach (var e in Edges)
                    if ((e.A == a && e.B == b) || (e.A == b && e.B == a)) return;
                candidates.Add(new Edge { A = a, B = b });
            }
        }

        private static void FillRooms(DungeonMap map, MapRules r, DungeonProps props, QuestGoal goal, Rng rng, bool boss)
        {
            map.Rooms[map.EntranceId].Type = RoomType.Entrance;
            if (boss)
            {
                var room = map.Rooms[map.FinalRoomId];
                room.Type = RoomType.Boss;
                room.Battle = new EncounterSlot { Kind = EncounterKind.Boss, Tier = map.Tier };
            }

            var free = map.Rooms.Where(room => room.Type == RoomType.Empty).ToList();
            rng.Shuffle(free);

            // total_room_battles caps the fights; guarded curios and treasures are counted in it
            var total = r.RoomBattlesTotal.Roll(rng);
            var guardedCurios = r.RoomGuardedCurios.Roll(rng);
            var guardedTreasures = r.RoomGuardedTreasures.Roll(rng);
            while (guardedCurios + guardedTreasures > total)
            {
                if (guardedCurios > guardedTreasures) guardedCurios--;
                else guardedTreasures--;
            }
            var plain = total - guardedCurios - guardedTreasures + r.RoomBattles.Roll(rng);
            var openTreasures = r.RoomTreasures.Roll(rng);
            var openCurios = r.RoomCurios.Roll(rng);
            // a cleanse quest with nothing to cleanse could never end
            if (map.QuestType == QuestTypes.Cleanse && guardedCurios + guardedTreasures + plain == 0) plain = 1;

            var next = 0;
            void Take(int count, RoomType type, bool fight, List<WeightedId> curios, string fallback)
            {
                for (var i = 0; i < count && next < free.Count; i++)
                {
                    var room = free[next++];
                    room.Type = type;
                    if (fight) room.Battle = new EncounterSlot { Kind = EncounterKind.Room, Tier = map.Tier };
                    if (curios != null) room.CurioId = DungeonProps.Pick(curios, rng, fallback);
                }
            }
            Take(guardedTreasures, RoomType.Treasure, true, props.RoomTreasures, FallbackTreasure);
            Take(guardedCurios, RoomType.Curio, true, props.RoomCurios, FallbackTreasure);
            Take(plain, RoomType.Battle, true, null, null);
            Take(openTreasures, RoomType.Treasure, false, props.RoomTreasures, FallbackTreasure);
            Take(openCurios, RoomType.Curio, false, props.RoomCurios, FallbackTreasure);

            // DD1 behaviour, not in data: quest curios stand in rooms, in empty ones first, else behind a fight.
            if (goal != null && goal.CurioId != null && (goal.Type == "gather" || goal.Type == "activate"))
            {
                var hosts = free.Skip(next).Concat(free.Take(next).Where(room => room.Type == RoomType.Battle)).ToList();
                for (var i = 0; i < goal.Amount && i < hosts.Count; i++)
                {
                    hosts[i].Type = RoomType.Curio;
                    hosts[i].CurioId = goal.CurioId;
                    hosts[i].QuestCurio = true;
                }
            }
        }

        // FALLBACK, not DD1 data: a quest that needs quest curios still gets some when quest.types.json is missing.
        private static QuestGoal FallbackGoal(string questType)
        {
            switch (questType)
            {
                case QuestTypes.Gather:
                    return new QuestGoal { Type = "gather", CurioId = "quest_curio", ItemId = "quest_item", Amount = 3 };
                case QuestTypes.Activate:
                case QuestTypes.InventoryActivate:
                    return new QuestGoal { Type = "activate", CurioId = "quest_curio", Amount = 3 };
                default:
                    return null;
            }
        }

        // FALLBACK ids, used only when the dungeon's props file is missing or lacks the row.
        private const string FallbackTreasure = "heirloom_chest";
        private const string FallbackSecret = "secret_stash";
        private const string FallbackTrap = "spikes";
        private const string FallbackObstacle = "rubble";

        private struct Tile
        {
            public Hallway Hall;
            public Segment Segment;
        }

        private static void FillHallways(DungeonMap map, MapRules r, DungeonProps props, Rng rng)
        {
            var tiles = new List<Tile>();
            foreach (var hall in map.Hallways)
                foreach (var segment in hall.Segments)
                    tiles.Add(new Tile { Hall = hall, Segment = segment });
            rng.Shuffle(tiles);

            // DD1 behaviour, not in data: a secret room has no hallway of its own, it opens from a hallway tile.
            foreach (var tile in Spread(tiles, HallContent.Secret, r.SecretRooms.Roll(rng)))
            {
                var room = new Room { Id = map.Rooms.Count, X = -1, Y = -1, Type = RoomType.Secret };
                room.CurioId = DungeonProps.Pick(props.SecretTreasures, rng, FallbackSecret);
                map.Rooms.Add(room);
                tile.Segment.SecretRoomId = room.Id;
            }
            foreach (var tile in Spread(tiles, HallContent.Battle, r.HallBattles.Roll(rng)))
                tile.Segment.Battle = new EncounterSlot { Kind = EncounterKind.Hallway, Tier = map.Tier };
            foreach (var tile in Spread(tiles, HallContent.Trap, r.HallTraps.Roll(rng)))
                tile.Segment.PropId = DungeonProps.Pick(props.Traps, rng, FallbackTrap);
            foreach (var tile in Spread(tiles, HallContent.Obstacle, r.HallObstacles.Roll(rng)))
                tile.Segment.PropId = DungeonProps.Pick(props.Obstacles, rng, FallbackObstacle);
            Spread(tiles, HallContent.Hunger, r.HallHunger.Roll(rng));
            // curios last: when a small map runs out of tiles it is curios that go missing, never fights or hunger
            if (props.HallCurios.Count > 0)
                foreach (var tile in Spread(tiles, HallContent.Curio, r.HallCurios.Roll(rng)))
                    tile.Segment.PropId = DungeonProps.Pick(props.HallCurios, rng, null);
        }

        /// <summary>
        /// Puts a content on free tiles, one per hallway while hallways without it remain (DD1 behaviour, not in
        /// data: contents are spread over the map rather than stacked in one hallway).
        /// </summary>
        private static List<Tile> Spread(List<Tile> tiles, HallContent content, int count)
        {
            var placed = new List<Tile>();
            for (var pass = 0; pass < 2 && placed.Count < count; pass++)
            {
                foreach (var tile in tiles)
                {
                    if (placed.Count >= count) break;
                    if (tile.Segment.Content != HallContent.Empty) continue;
                    if (pass == 0 && tile.Hall.Segments.Any(s => s.Content == content)) continue;
                    tile.Segment.Content = content;
                    placed.Add(tile);
                }
            }
            return placed;
        }

        private static void PickWalls(DungeonMap map, DungeonProps props, Rng rng)
        {
            foreach (var room in map.Rooms)
            {
                if (room.Type == RoomType.Entrance) room.Wall = "entrance";
                else if (room.Type != RoomType.Secret && props.RoomWalls.Count > 0) room.Wall = props.RoomWalls[rng.Next(props.RoomWalls.Count)];
            }
            foreach (var hall in map.Hallways)
            {
                var previous = -1;
                foreach (var segment in hall.Segments)
                {
                    if (props.CorridorWalls.Count == 0) continue;
                    // no two tiles in a row with the same wall when there is a choice
                    var choices = props.CorridorWalls.Where(w => w != previous).ToList();
                    if (choices.Count == 0) choices = props.CorridorWalls;
                    segment.Wall = previous = choices[rng.Next(choices.Count)];
                }
            }
        }

        private static QuestObjective BuildObjective(DungeonMap map, string questType, QuestGoal goal)
        {
            var fights = map.Rooms.Count(room => room.Battle != null && room.Type != RoomType.Boss);
            var questCurios = map.Rooms.Count(room => room.QuestCurio);
            switch (questType)
            {
                case QuestTypes.KillBoss:
                    return new QuestObjective { Kind = ObjectiveKind.KillBoss, Required = 1 };
                case QuestTypes.Cleanse:
                    return new QuestObjective { Kind = ObjectiveKind.Cleanse, GoalId = goal?.Id, Required = Share(fights, goal) };
                case QuestTypes.Gather:
                    return new QuestObjective
                    {
                        Kind = ObjectiveKind.Gather, GoalId = goal?.Id, Required = Math.Max(1, questCurios), CurioId = goal?.CurioId, ItemId = goal?.ItemId
                    };
                case QuestTypes.Activate:
                case QuestTypes.InventoryActivate:
                    var starting = goal != null ? goal.StartingItems : 0;
                    return new QuestObjective
                    {
                        Kind = ObjectiveKind.Activate, GoalId = goal?.Id, Required = Math.Max(1, questCurios), CurioId = goal?.CurioId,
                        ItemId = starting > 0 ? goal.ItemId : null, StartingItems = starting
                    };
                default:
                    return new QuestObjective { Kind = ObjectiveKind.Explore, GoalId = goal?.Id, Required = Share(map.GridRoomCount, goal) };
            }
        }

        // "percentage" of the DD1 goal (explore 0.9, cleanse 1.0), rounded up; everything when the goal is missing
        private static int Share(int count, QuestGoal goal)
        {
            var share = goal != null && goal.Percentage > 0 ? goal.Percentage : 1.0;
            return Math.Max(1, Math.Min(count, (int)Math.Ceiling(count * share - 1e-9)));
        }
    }
}
