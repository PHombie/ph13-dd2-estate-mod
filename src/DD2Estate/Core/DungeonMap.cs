using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Core
{
    public enum RoomType { Entrance, Empty, Battle, Treasure, Curio, Boss, Secret }

    public enum HallContent { Empty, Battle, Curio, Trap, Obstacle, Hunger, Secret }

    public enum EncounterKind { Hallway, Room, Boss }

    /// <summary>A fight to be filled with DD2 monsters by the game layer. Tier is the DD1 quest difficulty (1, 3, 5).</summary>
    public sealed class EncounterSlot
    {
        public EncounterKind Kind;
        public int Tier;

        public JObject ToJson() => new JObject { ["kind"] = Kind.ToString(), ["tier"] = Tier };

        public static EncounterSlot FromJson(JToken json)
        {
            if (json == null || json.Type != JTokenType.Object) return null;
            return new EncounterSlot { Kind = Json.Enum(json["kind"], EncounterKind.Room), Tier = Json.Int(json["tier"], 1) };
        }
    }

    public sealed class Room
    {
        public int Id;
        /// <summary>Grid cell. A secret room is off the grid (-1, -1): it is entered from its hallway segment.</summary>
        public int X, Y;
        public RoomType Type;
        /// <summary>Fight on entry; it guards the curio or treasure when the room has one. Null: no fight.</summary>
        public EncounterSlot Battle;
        /// <summary>DD1 curio prop id (curio, treasure or quest curio). Null: none.</summary>
        public string CurioId;
        public bool QuestCurio;
        /// <summary>DD1 wall art: <c>&lt;dungeon&gt;.room_wall.&lt;Wall&gt;.png</c>; "entrance" is <c>&lt;dungeon&gt;.entrance_room_wall.png</c>.</summary>
        public string Wall;

        public JObject ToJson()
        {
            var json = new JObject { ["id"] = Id, ["x"] = X, ["y"] = Y, ["type"] = Type.ToString() };
            if (Battle != null) json["battle"] = Battle.ToJson();
            if (CurioId != null) json["curio"] = CurioId;
            if (QuestCurio) json["quest"] = true;
            if (Wall != null) json["wall"] = Wall;
            return json;
        }

        public static Room FromJson(JToken json)
        {
            return new Room
            {
                Id = Json.Int(json["id"], 0),
                X = Json.Int(json["x"], 0),
                Y = Json.Int(json["y"], 0),
                Type = Json.Enum(json["type"], RoomType.Empty),
                Battle = EncounterSlot.FromJson(json["battle"]),
                CurioId = (string)json["curio"],
                QuestCurio = Json.Bool(json["quest"], false),
                Wall = (string)json["wall"]
            };
        }
    }

    /// <summary>One hallway tile between the two door tiles.</summary>
    public sealed class Segment
    {
        public HallContent Content;
        /// <summary>Curio, trap or obstacle prop id, by content.</summary>
        public string PropId;
        public EncounterSlot Battle;
        /// <summary>Room behind the hidden door when the content is Secret, else -1.</summary>
        public int SecretRoomId = -1;
        /// <summary>DD1 wall art number: <c>&lt;dungeon&gt;.corridor_wall.NN.png</c>.</summary>
        public int Wall;
        /// <summary>
        /// Where the tile lies on a hand-made DD1 map (<see cref="DungeonMap.TileGrid"/>), in tiles: such a
        /// hallway may bend. -1: between its rooms, evenly spaced (every generated map).
        /// </summary>
        public int MapX = -1, MapY = -1;

        public JObject ToJson()
        {
            var json = new JObject { ["content"] = Content.ToString(), ["wall"] = Wall };
            if (PropId != null) json["prop"] = PropId;
            if (Battle != null) json["battle"] = Battle.ToJson();
            if (SecretRoomId >= 0) json["secret"] = SecretRoomId;
            if (MapX >= 0 && MapY >= 0)
            {
                json["x"] = MapX;
                json["y"] = MapY;
            }
            return json;
        }

        public static Segment FromJson(JToken json)
        {
            return new Segment
            {
                Content = Json.Enum(json["content"], HallContent.Empty),
                Wall = Json.Int(json["wall"], 0),
                PropId = (string)json["prop"],
                Battle = EncounterSlot.FromJson(json["battle"]),
                SecretRoomId = Json.Int(json["secret"], -1),
                MapX = Json.Int(json["x"], -1),
                MapY = Json.Int(json["y"], -1)
            };
        }
    }

    public sealed class Hallway
    {
        public int Id;
        public int RoomA, RoomB;
        /// <summary>Ordered from RoomA to RoomB.</summary>
        public List<Segment> Segments = new List<Segment>();

        public int Other(int roomId) => roomId == RoomA ? RoomB : RoomA;

        public JObject ToJson()
        {
            var segments = new JArray();
            foreach (var s in Segments) segments.Add(s.ToJson());
            return new JObject { ["id"] = Id, ["a"] = RoomA, ["b"] = RoomB, ["segments"] = segments };
        }

        public static Hallway FromJson(JToken json)
        {
            var hall = new Hallway { Id = Json.Int(json["id"], 0), RoomA = Json.Int(json["a"], 0), RoomB = Json.Int(json["b"], 0) };
            foreach (var s in Json.Array(json["segments"])) hall.Segments.Add(Segment.FromJson(s));
            return hall;
        }
    }

    /// <summary>
    /// A DD1-style dungeon: rooms on a grid joined by hallways of equal length. Room and hallway ids are their
    /// index in the lists.
    /// </summary>
    public sealed class DungeonMap
    {
        public const int Version = 1;

        public string DungeonId;
        public string QuestType;
        public int Length;
        public int Tier;
        public long Seed;
        public int Width, Height;
        public int EntranceId;
        /// <summary>The room the layout keeps farthest from the entrance; the boss room in boss quests.</summary>
        public int FinalRoomId = -1;
        /// <summary>The dungeon's trap prop, for traps that turn up later in hallways already walked. Null: none.</summary>
        public string TrapId;
        /// <summary>
        /// A hand-made DD1 map (<see cref="FixedMap"/>): its rooms' X and Y are tiles of DD1's own map, not cells
        /// of the generator's grid, its hallways are of different lengths and their tiles say where they lie.
        /// </summary>
        public bool TileGrid;
        public List<Room> Rooms = new List<Room>();
        public List<Hallway> Hallways = new List<Hallway>();
        public QuestObjective Objective;

        public Room GetRoom(int id) => id >= 0 && id < Rooms.Count ? Rooms[id] : null;

        public Hallway GetHallway(int id) => id >= 0 && id < Hallways.Count ? Hallways[id] : null;

        public Hallway HallwayBetween(int roomA, int roomB)
        {
            foreach (var h in Hallways)
                if ((h.RoomA == roomA && h.RoomB == roomB) || (h.RoomA == roomB && h.RoomB == roomA)) return h;
            return null;
        }

        public List<Hallway> HallwaysOf(int roomId)
        {
            var list = new List<Hallway>();
            foreach (var h in Hallways)
                if (h.RoomA == roomId || h.RoomB == roomId) list.Add(h);
            return list;
        }

        /// <summary>The hallway tile a secret room opens from. False for a room that is not one.</summary>
        public bool FindSecretDoor(int secretRoomId, out int hallwayId, out int segment)
        {
            foreach (var h in Hallways)
                for (var i = 0; i < h.Segments.Count; i++)
                {
                    if (h.Segments[i].Content != HallContent.Secret || h.Segments[i].SecretRoomId != secretRoomId) continue;
                    hallwayId = h.Id;
                    segment = i;
                    return true;
                }
            hallwayId = segment = -1;
            return false;
        }

        /// <summary>Rooms on the grid, i.e. without secret rooms.</summary>
        public int GridRoomCount
        {
            get
            {
                var n = 0;
                foreach (var r in Rooms)
                    if (r.Type != RoomType.Secret) n++;
                return n;
            }
        }

        /// <summary>Hallways walked from a room to every other room; -1 for rooms that cannot be reached that way.</summary>
        public int[] Distances(int fromRoomId)
        {
            var dist = new int[Rooms.Count];
            for (var i = 0; i < dist.Length; i++) dist[i] = -1;
            if (fromRoomId < 0 || fromRoomId >= Rooms.Count) return dist;
            var queue = new Queue<int>();
            dist[fromRoomId] = 0;
            queue.Enqueue(fromRoomId);
            while (queue.Count > 0)
            {
                var room = queue.Dequeue();
                foreach (var h in Hallways)
                {
                    if (h.RoomA != room && h.RoomB != room) continue;
                    var next = h.Other(room);
                    if (dist[next] >= 0) continue;
                    dist[next] = dist[room] + 1;
                    queue.Enqueue(next);
                }
            }
            return dist;
        }

        public JObject ToJson()
        {
            var rooms = new JArray();
            foreach (var r in Rooms) rooms.Add(r.ToJson());
            var halls = new JArray();
            foreach (var h in Hallways) halls.Add(h.ToJson());
            return new JObject
            {
                ["version"] = Version,
                ["dungeon"] = DungeonId,
                ["quest"] = QuestType,
                ["length"] = Length,
                ["tier"] = Tier,
                ["seed"] = Seed,
                ["width"] = Width,
                ["height"] = Height,
                ["entrance"] = EntranceId,
                ["final"] = FinalRoomId,
                ["trap"] = TrapId,
                ["tileGrid"] = TileGrid,
                ["objective"] = Objective?.ToJson(),
                ["rooms"] = rooms,
                ["hallways"] = halls
            };
        }

        public static DungeonMap FromJson(JObject json)
        {
            var map = new DungeonMap
            {
                DungeonId = (string)json["dungeon"],
                QuestType = (string)json["quest"],
                Length = Json.Int(json["length"], 1),
                Tier = Json.Int(json["tier"], 1),
                Seed = Json.Long(json["seed"], 0),
                Width = Json.Int(json["width"], 0),
                Height = Json.Int(json["height"], 0),
                EntranceId = Json.Int(json["entrance"], 0),
                FinalRoomId = Json.Int(json["final"], -1),
                TrapId = (string)json["trap"],
                TileGrid = Json.Bool(json["tileGrid"], false),
                Objective = QuestObjective.FromJson(json["objective"])
            };
            foreach (var r in Json.Array(json["rooms"])) map.Rooms.Add(Room.FromJson(r));
            foreach (var h in Json.Array(json["hallways"])) map.Hallways.Add(Hallway.FromJson(h));
            for (var i = 0; i < map.Rooms.Count; i++)
                if (map.Rooms[i].Id != i) throw new FormatException("Dungeon map: room ids must match their position.");
            for (var i = 0; i < map.Hallways.Count; i++)
                if (map.Hallways[i].Id != i) throw new FormatException("Dungeon map: hallway ids must match their position.");
            return map;
        }
    }
}
