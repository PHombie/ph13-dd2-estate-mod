using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Core
{
    public struct IntRange
    {
        public int Min, Max;

        public IntRange(int min, int max)
        {
            Min = min;
            Max = Math.Max(min, max);
        }

        public int Roll(Rng rng) => rng.Range(Min, Max);

        public bool Contains(int value) => value >= Min && value <= Max;

        public override string ToString() => Min + "-" + Max;
    }

    /// <summary>One <c>map:</c> block of DD1's <c>scripts/map_generator.darkest</c>: the numbers for a quest type, size and dungeon.</summary>
    public sealed class MapRules
    {
        public string QuestType, Size, Dungeon;
        public int Rooms;
        /// <summary>Target number of hallways. A connected map never has fewer than Rooms - 1.</summary>
        public int Corridors;
        public int GridWidth, GridHeight;
        /// <summary>Hallway tiles between two neighbouring rooms.</summary>
        public int Spacing;
        /// <summary>Chance that a hallway beyond the connecting ones (a loop) is built.</summary>
        public double Connectivity;
        public int MinFinalDistance;
        public double NeighbourBias, CentreBias;
        public IntRange HallBattles, HallTraps, HallObstacles, HallCurios, HallHunger;
        public IntRange RoomBattlesTotal, RoomBattles, RoomGuardedCurios, RoomCurios, RoomGuardedTreasures, RoomTreasures, SecretRooms;
        /// <summary>True when the numbers are the built-in stand-ins, not the player's DD1 data.</summary>
        public bool IsFallback;
    }

    public sealed class WeightedId
    {
        public string Id;
        public double Weight;
    }

    /// <summary>A dungeon's <c>&lt;id&gt;.props.darkest</c> plus the wall art variants found next to it.</summary>
    public sealed class DungeonProps
    {
        public List<WeightedId> HallCurios = new List<WeightedId>();
        public List<WeightedId> RoomCurios = new List<WeightedId>();
        public List<WeightedId> RoomTreasures = new List<WeightedId>();
        public List<WeightedId> Traps = new List<WeightedId>();
        public List<WeightedId> Obstacles = new List<WeightedId>();
        public List<WeightedId> SecretTreasures = new List<WeightedId>();
        public List<int> CorridorWalls = new List<int>();
        public List<string> RoomWalls = new List<string>();

        public static string Pick(List<WeightedId> list, Rng rng, string fallback)
        {
            if (list.Count == 0) return fallback;
            var index = rng.PickWeighted(list.Select(e => e.Weight).ToList());
            return index < 0 ? fallback : list[index].Id;
        }
    }

    /// <summary>A goal of DD1's <c>campaign/quest/quest.types.json</c>.</summary>
    public sealed class QuestGoal
    {
        public string Id, Type, CurioId, ItemId;
        public int Amount;
        public double Percentage;
        public int StartingItems;
    }

    /// <summary>Everything the dungeon generator reads from the DD1 install.</summary>
    public sealed class GenerationRules
    {
        private static readonly string[] Sizes = { "short", "medium", "long" };

        private readonly IDd1Files _files;
        private readonly List<MapRules> _maps = new List<MapRules>();
        private readonly Dictionary<string, DungeonProps> _props = new Dictionary<string, DungeonProps>();
        private JObject _questTypes;

        private GenerationRules(IDd1Files files) { _files = files; }

        public IReadOnlyList<MapRules> Maps => _maps;

        public static GenerationRules Load(IDd1Files files)
        {
            var rules = new GenerationRules(files);
            foreach (var block in DarkestFile.Parse(files.ReadText("scripts/map_generator.darkest")))
                if (block.Name == "map") rules._maps.Add(ReadMap(block));
            rules._questTypes = Json.ParseFile(files.ReadText("campaign/quest/quest.types.json"));
            return rules;
        }

        public static string SizeOf(int length) => Sizes[Math.Max(1, Math.Min(3, length)) - 1];

        /// <summary>
        /// The block for a quest. DD1 has no block for some combinations (a short boss quest, a long gather quest):
        /// the nearest size of the same quest type and dungeon is used then.
        /// </summary>
        public MapRules MapFor(string questType, int length, string dungeon)
        {
            var size = Math.Max(1, Math.Min(3, length)) - 1;
            MapRules best = null;
            var bestScore = int.MaxValue;
            foreach (var map in _maps)
            {
                if (map.QuestType != questType) continue;
                var sizeGap = Math.Abs(Array.IndexOf(Sizes, map.Size) - size);
                var score = sizeGap + (map.Dungeon == dungeon ? 0 : 10);
                if (score >= bestScore) continue;
                best = map;
                bestScore = score;
            }
            return best ?? Fallback(questType, length, dungeon);
        }

        private static MapRules ReadMap(DarkestBlock b)
        {
            IntRange R(string field) => new IntRange(b.Int(field, 0, 0), b.Int(field, 1, b.Int(field, 0, 0)));
            return new MapRules
            {
                Size = b.Text("size", 0, "short"),
                QuestType = b.Text("quest_type", 0, ""),
                Dungeon = b.Text("dungeon_type", 0, ""),
                Rooms = b.Int("base_room_number", 0, 9),
                Corridors = b.Int("base_corridor_number", 0, 0),
                GridWidth = b.Int("gridsize", 0, 4),
                GridHeight = b.Int("gridsize", 1, 4),
                Spacing = b.Int("spacing", 0, 4),
                Connectivity = b.Number("connectivity", 0, 0.9),
                MinFinalDistance = b.Int("min_final_distance", 0, 0),
                NeighbourBias = b.Number("nudge_neighbour_bias", 0, 0),
                CentreBias = b.Number("nudge_map_centre_bias", 0, 0),
                HallBattles = R("hallway_battle"),
                HallTraps = R("hallway_trap"),
                HallObstacles = R("hallway_obstacle"),
                HallCurios = R("hallway_curio"),
                HallHunger = R("hallway_hunger"),
                RoomBattlesTotal = R("total_room_battles"),
                RoomBattles = R("room_battle"),
                RoomGuardedCurios = R("room_guarded_curio"),
                RoomCurios = R("room_curio"),
                RoomGuardedTreasures = R("room_guarded_treasure"),
                RoomTreasures = R("room_treasure"),
                SecretRooms = R("secret_rooms")
            };
        }

        // FALLBACK, not DD1 data: only keeps a dungeon playable when map_generator.darkest is missing or unreadable.
        private static MapRules Fallback(string questType, int length, string dungeon)
        {
            var n = Math.Max(1, Math.Min(3, length));
            var rooms = 5 + 4 * n;
            var side = 2 + n;
            return new MapRules
            {
                IsFallback = true,
                QuestType = questType,
                Size = SizeOf(length),
                Dungeon = dungeon,
                Rooms = rooms,
                Corridors = rooms,
                GridWidth = side + 1,
                GridHeight = side,
                Spacing = 4,
                Connectivity = 0.9,
                MinFinalDistance = 2 * n + 1,
                HallBattles = new IntRange(n + 1, n + 2),
                HallTraps = new IntRange(1, n + 1),
                HallObstacles = new IntRange(0, n),
                HallCurios = new IntRange(rooms, rooms),
                HallHunger = new IntRange(1, n + 1),
                RoomBattlesTotal = new IntRange(n + 1, n + 2),
                RoomGuardedCurios = new IntRange(0, n),
                RoomGuardedTreasures = new IntRange(1, n),
                SecretRooms = new IntRange(0, n > 1 ? 1 : 0)
            };
        }

        public DungeonProps PropsFor(string dungeon)
        {
            if (_props.TryGetValue(dungeon, out var props)) return props;
            props = new DungeonProps();
            var dir = "dungeons/" + dungeon;
            foreach (var block in DarkestFile.Parse(_files.ReadText(dir + "/" + dungeon + ".props.darkest")))
            {
                var list = block.Name == "hall_curios" ? props.HallCurios
                    : block.Name == "room_curios" ? props.RoomCurios
                    : block.Name == "room_treasures" ? props.RoomTreasures
                    : block.Name == "traps" ? props.Traps
                    : block.Name == "obstacles" ? props.Obstacles
                    : block.Name == "secret_room_treasures" ? props.SecretTreasures
                    : null;
                if (list == null) continue;
                // a row may name several props: they share the row's weight
                var types = block.All("types");
                var weight = block.Number("chance", 0, 1);
                foreach (var id in types)
                    list.Add(new WeightedId { Id = id, Weight = weight / types.Count });
            }

            var wallPrefix = dungeon + ".corridor_wall.";
            foreach (var name in Sorted(_files.List(dir, wallPrefix + "*.png")))
                if (int.TryParse(Middle(name, wallPrefix, ".png"), out var number)) props.CorridorWalls.Add(number);
            var roomPrefix = dungeon + ".room_wall.";
            foreach (var name in Sorted(_files.List(dir, roomPrefix + "*.png")))
            {
                var variant = Middle(name, roomPrefix, ".png");
                if (variant.Length > 0 && variant != "entrance") props.RoomWalls.Add(variant);
            }
            _props[dungeon] = props;
            return props;
        }

        // the order of a directory listing depends on the file system: sort so that a seed gives the same dungeon everywhere
        private static IEnumerable<string> Sorted(string[] names) => (names ?? new string[0]).OrderBy(n => n, StringComparer.Ordinal);

        private static string Middle(string name, string prefix, string suffix)
        {
            return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                   && name.Length >= prefix.Length + suffix.Length
                ? name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length)
                : "";
        }

        /// <summary>The goal of a generated quest in a dungeon; null for boss quests and when DD1 defines none.</summary>
        public QuestGoal GoalFor(string questType, string dungeon)
        {
            if (_questTypes == null) return null;
            string goalId = null;
            foreach (var type in Json.Array(_questTypes["types"]))
            {
                if ((string)type["id"] != questType) continue;
                foreach (var wanted in new[] { dungeon, "all" })
                {
                    foreach (var list in Json.Array(type["goal_lists"]))
                    {
                        if ((string)list["dungeon"] != wanted) continue;
                        goalId = (string)Json.Array(list["goals"]).SelectMany(Json.Array).FirstOrDefault();
                        if (goalId != null) break;
                    }
                    if (goalId != null) break;
                }
            }
            if (goalId == null) return null;
            foreach (var goal in Json.Array(_questTypes["goals"]))
            {
                if ((string)goal["id"] != goalId) continue;
                var data = goal["data"] ?? new JObject();
                var item = data["item"];
                var starting = Json.Array(goal["starting_items"]).FirstOrDefault();
                return new QuestGoal
                {
                    Id = goalId,
                    Type = (string)goal["type"],
                    CurioId = (string)data["curio_name"],
                    ItemId = item != null ? (string)item["id"] : starting != null ? (string)starting["id"] : null,
                    Amount = item != null ? Json.Int(item["amount"], 0) : Json.Int(data["amount"], 0),
                    Percentage = Json.Number(data["percentage"], 0),
                    StartingItems = starting != null ? Json.Int(starting["amount"], 0) : 0
                };
            }
            return null;
        }
    }
}
