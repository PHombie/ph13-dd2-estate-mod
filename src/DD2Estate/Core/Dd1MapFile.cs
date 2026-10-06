using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DD2Estate.Core
{
    /// <summary>
    /// One field of a DD1 binary file (its saves and its hand-made maps, <c>maps/*.dm</c>, share the format):
    /// a name, and either children or a value. The format keeps no types: a value is the bytes between its
    /// name and the next field, read as what the caller knows it to be.
    ///
    /// Layout (read off the files; the header's numbers are all 32-bit little endian): magic 01 B1 00 00,
    /// revision, header length, 0, size and count and offset of the first table (one 16-byte row per object:
    /// parent, its field, direct children, all children), 16 bytes of 0, count and offset of the second table
    /// (one 12-byte row per field: name hash, offset into the data, and a word of bit 0 "is an object",
    /// bits 2..10 the name's length with its 0, bits 11..30 the object's row in the first table), 0, the
    /// data's length and offset. A field's data is its name, then its value; a value of four bytes or more
    /// starts on a multiple of four counted from the data's start. Fields follow each other in the order of
    /// a walk through the tree, so an object's children are the fields after it.
    /// </summary>
    public sealed class Dd1Field
    {
        public string Name;
        public readonly List<Dd1Field> Children = new List<Dd1Field>();
        /// <summary>The value's bytes with the padding before them taken off; empty for an object.</summary>
        public byte[] Value = new byte[0];
        public bool IsObject;

        public Dd1Field Child(string name)
        {
            foreach (var child in Children)
                if (child.Name == name) return child;
            return null;
        }

        /// <summary>A field further down: "map/static_dynamic/areas". Null when a step is missing.</summary>
        public Dd1Field Find(string path)
        {
            var at = this;
            foreach (var step in path.Split('/'))
            {
                at = at.Child(step);
                if (at == null) return null;
            }
            return at;
        }

        public int Int(int fallback = 0) => Value.Length >= 4 ? BitConverter.ToInt32(Value, 0) : fallback;

        public float Float(int index = 0, float fallback = 0f) => Value.Length >= 4 * index + 4 ? BitConverter.ToSingle(Value, 4 * index) : fallback;

        public bool Bool() => Value.Length > 0 && Value[0] != 0;

        /// <summary>A string as DD1 writes it: its length with the closing 0, then the characters.</summary>
        public string Text()
        {
            if (Value.Length < 5) return null;
            var length = BitConverter.ToInt32(Value, 0);
            if (length <= 0 || length > Value.Length - 4) return null;
            return Encoding.ASCII.GetString(Value, 4, length - 1);
        }

        /// <summary>A whole file kept as a value (a map's unchanging half): its length, then the file.</summary>
        public Dd1Field Embedded()
        {
            return Value.Length >= 8 + 4 && Dd1Binary.IsFile(Value, 4) ? Dd1Binary.Parse(Value, 4) : null;
        }

        /// <summary>DD1 writes an area's id as four letters in one number ("rooA", "corC").</summary>
        public static string Letters(int id)
        {
            var bytes = BitConverter.GetBytes(id);
            foreach (var b in bytes)
                if (b < 32 || b > 126) return id.ToString();
            return Encoding.ASCII.GetString(bytes);
        }
    }

    public static class Dd1Binary
    {
        public static bool IsFile(byte[] bytes, int at = 0)
        {
            return bytes != null && bytes.Length >= at + 0x40 && bytes[at] == 0x01 && bytes[at + 1] == 0xB1 && bytes[at + 2] == 0 && bytes[at + 3] == 0;
        }

        /// <summary>The file's root field ("base_root"). Throws FormatException for anything that is not such a file.</summary>
        public static Dd1Field Parse(byte[] bytes, int at = 0)
        {
            if (!IsFile(bytes, at)) throw new FormatException("Not a DD1 binary file.");
            int objects = I(bytes, at + 0x14), objectsAt = at + I(bytes, at + 0x18);
            int fields = I(bytes, at + 0x2C), fieldsAt = at + I(bytes, at + 0x30);
            int dataLength = I(bytes, at + 0x38), dataAt = at + I(bytes, at + 0x3C);
            if (fields <= 0 || fieldsAt + 12 * fields > bytes.Length || objectsAt + 16 * objects > bytes.Length || dataAt + dataLength > bytes.Length)
                throw new FormatException("DD1 binary file: tables beyond the file's end.");

            var offsets = new int[fields + 1];
            for (var i = 0; i < fields; i++) offsets[i] = I(bytes, fieldsAt + 12 * i + 4);
            offsets[fields] = dataLength;
            // where each value ends: at the next field by offset (the fields are written in order; sorted to be sure)
            var sorted = offsets.OrderBy(o => o).ToArray();

            var next = 0;
            var root = Read(bytes, fieldsAt, objectsAt, objects, dataAt, dataLength, sorted, fields, ref next);
            return root;
        }

        private static Dd1Field Read(byte[] bytes, int fieldsAt, int objectsAt, int objects, int dataAt, int dataLength, int[] sorted, int fields, ref int next)
        {
            if (next >= fields) throw new FormatException("DD1 binary file: an object names more children than there are fields.");
            var row = fieldsAt + 12 * next++;
            var offset = I(bytes, row + 4);
            var info = I(bytes, row + 8);
            var nameLength = (info >> 2) & 0x1FF;
            if (offset < 0 || nameLength < 1 || offset + nameLength > dataLength) throw new FormatException("DD1 binary file: a field beyond the data's end.");
            var field = new Dd1Field { Name = Encoding.ASCII.GetString(bytes, dataAt + offset, nameLength - 1), IsObject = (info & 1) != 0 };
            if (field.IsObject)
            {
                var index = (info >> 11) & 0xFFFFF;
                if (index >= objects) throw new FormatException("DD1 binary file: an object without its row.");
                var children = I(bytes, objectsAt + 16 * index + 8);
                for (var i = 0; i < children; i++) field.Children.Add(Read(bytes, fieldsAt, objectsAt, objects, dataAt, dataLength, sorted, fields, ref next));
                return field;
            }
            var start = offset + nameLength;
            var end = dataLength;
            var at = Array.BinarySearch(sorted, offset);
            for (var i = Math.Max(0, at); i < sorted.Length; i++)
                if (sorted[i] > offset)
                {
                    end = sorted[i];
                    break;
                }
            var pad = (4 - start % 4) % 4;
            if (end - start - pad >= 4) start += pad;
            field.Value = new byte[Math.Max(0, end - start)];
            Array.Copy(bytes, dataAt + start, field.Value, 0, field.Value.Length);
            return field;
        }

        private static int I(byte[] bytes, int at) => BitConverter.ToInt32(bytes, at);
    }

    /// <summary>One tile of a DD1 map: a room is one tile, a hallway a row of them with a door tile at either end.</summary>
    public sealed class Dd1MapTile
    {
        /// <summary>DD1's tile type: 3 a room, 2 a hallway's door tile, 1 a hallway tile.</summary>
        public int Type;
        /// <summary>Where the tile lies on DD1's map, in tiles.</summary>
        public int X, Y;
        /// <summary>The area a door tile opens to; null for none.</summary>
        public string DoorTo;
        /// <summary>DD1's content number (<see cref="Dd1Map"/> says what is known of them).</summary>
        public int Content;
        /// <summary>The fight the tile names in the dungeon's mash file ("town_incursion_weak_07"); null for none.</summary>
        public string Mash;
        /// <summary>The picture the map gives the tile ("room_wall.altar"); null: the dungeon's own choice.</summary>
        public string Texture;
    }

    public sealed class Dd1MapArea
    {
        /// <summary>DD1's four letters: "rooA", "corC".</summary>
        public string Id;
        public bool IsRoom;
        public readonly List<Dd1MapTile> Tiles = new List<Dd1MapTile>();
    }

    /// <summary>
    /// A hand-made DD1 map (<c>maps/&lt;map_name&gt;.dm</c>, named by a plot quest's <c>map_name</c>): the
    /// Brigand Incursion's streets (town_invasion_0), the Shrieker's perch (crow_map1), the first Ruins quest.
    ///
    /// The numbers of a tile's content are not named anywhere in DD1's files. READING, from DD1's own map of
    /// its first Ruins quest, whose rooms and hallways are known from play: 0 nothing, 1 a fight, 3 a trap,
    /// 4 a curio, 6 a treasure (guarded when the tile names a fight), 7 hunger, 8 an obstacle, 10 a room's
    /// fight, 13 a door in a hallway's side (the Incursion's locked door), 9 what lies behind it.
    /// </summary>
    public sealed class Dd1Map
    {
        public const int Nothing = 0, Fight = 1, Trap = 3, Curio = 4, Treasure = 6, Hunger = 7, Obstacle = 8, SideRoom = 9, RoomFight = 10, SideDoor = 13;
        public const int RoomTile = 3, DoorTile = 2, HallTile = 1;

        public string EntranceId, FinalRoomId;
        public readonly List<Dd1MapArea> Areas = new List<Dd1MapArea>();

        public Dd1MapArea Area(string id)
        {
            foreach (var area in Areas)
                if (area.Id == id) return area;
            return null;
        }

        public static string FileOf(string mapName) => "maps/" + mapName + ".dm";

        /// <summary>Reads a map. Throws FormatException for a file that is not one.</summary>
        public static Dd1Map Read(byte[] bytes)
        {
            var root = Dd1Binary.Parse(bytes);
            var mapField = root.Child("map") ?? throw new FormatException("DD1 map: no 'map'.");
            var still = mapField.Find("static_dynamic/static_save")?.Embedded() ?? throw new FormatException("DD1 map: no static half.");
            var map = new Dd1Map
            {
                EntranceId = Dd1Field.Letters(mapField.Child("entrance_id")?.Int() ?? 0),
                FinalRoomId = Dd1Field.Letters(mapField.Child("final_room_id")?.Int() ?? 0)
            };
            var moving = mapField.Find("static_dynamic/areas");
            foreach (var areaField in still.Child("areas")?.Children ?? new List<Dd1Field>())
            {
                var area = new Dd1MapArea { Id = Dd1Field.Letters(areaField.Child("id")?.Int() ?? 0), IsRoom = (areaField.Child("kind")?.Int() ?? 0) == 0 };
                var movingTiles = moving?.Child(areaField.Name)?.Child("tiles");
                foreach (var tileField in areaField.Child("tiles")?.Children ?? new List<Dd1Field>())
                {
                    var position = tileField.Child("mappos");
                    var door = tileField.Find("door_to/area_to");
                    var content = movingTiles?.Child(tileField.Name);
                    var to = door != null ? Dd1Field.Letters(door.Int()) : null;
                    area.Tiles.Add(new Dd1MapTile
                    {
                        Type = tileField.Child("type")?.Int() ?? 0,
                        X = position != null ? (int)Math.Round(position.Float(0)) : 0,
                        Y = position != null ? (int)Math.Round(position.Float(1)) : 0,
                        DoorTo = to == "none" ? null : to,
                        Content = content?.Child("content")?.Int() ?? 0,
                        Mash = content?.Child("mash_name")?.Text()
                    });
                }
                map.Areas.Add(area);
            }
            // the pictures the map names for single tiles (the Incursion's rooms: altar, square, start)
            foreach (var entry in still.Find("ext_data/tile_textures")?.Children ?? new List<Dd1Field>())
            {
                var area = map.Area(Dd1Field.Letters(entry.Child("area")?.Int() ?? 0));
                var tile = entry.Child("tile")?.Int() ?? 0;
                var name = entry.Child("texture_name")?.Text();
                if (area != null && tile >= 0 && tile < area.Tiles.Count && !string.IsNullOrEmpty(name)) area.Tiles[tile].Texture = name;
            }
            if (map.Area(map.EntranceId) == null) throw new FormatException("DD1 map: the entrance '" + map.EntranceId + "' is not among its areas.");
            return map;
        }
    }

    /// <summary>What a hand-made DD1 map did not carry over, for the log and the report.</summary>
    public sealed class FixedMapNotes
    {
        public readonly List<string> LeftOut = new List<string>();
    }

    /// <summary>
    /// A hand-made DD1 map as an expedition's map: its rooms where DD1 has them, its hallways tile for tile
    /// with what DD1 put on each (a fight, a curio, hunger, a trap, an obstacle), the quest's boss in the room
    /// DD1 calls final (or in the only room). What DD1 leaves to chance on such a map is drawn here by DD1's
    /// own lists: which curio stands on a curio tile (the dungeon's props file), which wall a hallway tile shows.
    /// </summary>
    public static class FixedMap
    {
        private const string RoomWallPrefix = "room_wall.";

        public static DungeonMap Build(Dd1Map source, string dungeonId, string questType, int tier, long seed, DungeonProps props, FixedMapNotes notes = null)
        {
            var rng = new Rng(seed);
            var map = new DungeonMap { DungeonId = dungeonId, QuestType = questType, Length = 1, Tier = tier, Seed = seed, TileGrid = true };
            map.TrapId = props.Traps.Count > 0 ? props.Traps[0].Id : null;

            // the rooms the hallways' ends join, starting from the entrance: what hangs off a door in a hallway's
            // side (the Incursion's locked room) is not among them
            var joined = new HashSet<string> { source.EntranceId };
            foreach (var area in source.Areas)
            {
                if (area.IsRoom || area.Tiles.Count < 2) continue;
                string a = area.Tiles[0].DoorTo, b = area.Tiles[area.Tiles.Count - 1].DoorTo;
                if (a != null) joined.Add(a);
                if (b != null) joined.Add(b);
            }
            var roomIds = new Dictionary<string, int>();
            int maxY = 0;
            foreach (var area in source.Areas)
                if (area.IsRoom && area.Tiles.Count > 0 && joined.Contains(area.Id)) maxY = Math.Max(maxY, area.Tiles[0].Y);
            foreach (var area in source.Areas)
            {
                if (!area.IsRoom || area.Tiles.Count == 0) continue;
                if (!joined.Contains(area.Id))
                {
                    notes?.LeftOut.Add("room " + area.Id + " (content " + area.Tiles[0].Content + "): it lies behind a door in a hallway's side");
                    continue;
                }
                var tile = area.Tiles[0];
                // GUESS: DD1's map numbers its rows downwards (the Incursion's entrance, row 11, at the foot)
                var room = new Room { Id = map.Rooms.Count, X = tile.X, Y = maxY - tile.Y, Type = RoomType.Empty };
                var boss = questType == QuestTypes.KillBoss && (area.Id == source.FinalRoomId || source.Areas.Count(x => x.IsRoom) == 1);
                if (area.Id == source.EntranceId) room.Type = RoomType.Entrance;
                if (boss)
                {
                    room.Type = RoomType.Boss;
                    room.Battle = new EncounterSlot { Kind = EncounterKind.Boss, Tier = tier };
                }
                else if (tile.Content == Dd1Map.Fight || tile.Content == Dd1Map.RoomFight || (tile.Content == Dd1Map.Treasure && tile.Mash != null))
                {
                    room.Type = tile.Content == Dd1Map.Treasure ? RoomType.Treasure : RoomType.Battle;
                    room.Battle = new EncounterSlot { Kind = EncounterKind.Room, Tier = tier };
                }
                if (tile.Content == Dd1Map.Treasure)
                {
                    room.Type = RoomType.Treasure;
                    room.CurioId = DungeonProps.Pick(props.RoomTreasures, rng, null);
                }
                else if (tile.Content == Dd1Map.Curio)
                {
                    room.Type = RoomType.Curio;
                    room.CurioId = DungeonProps.Pick(props.RoomCurios, rng, null);
                }
                else if (tile.Content == Dd1Map.Hunger) notes?.LeftOut.Add("room " + area.Id + ": hunger in a room");
                room.Wall = tile.Texture != null && tile.Texture.StartsWith(RoomWallPrefix, StringComparison.Ordinal)
                    ? tile.Texture.Substring(RoomWallPrefix.Length)
                    : area.Id == source.EntranceId ? "entrance" : props.RoomWalls.Count > 0 ? props.RoomWalls[rng.Next(props.RoomWalls.Count)] : null;
                roomIds[area.Id] = room.Id;
                map.Rooms.Add(room);
            }
            map.EntranceId = roomIds[source.EntranceId];
            map.FinalRoomId = source.FinalRoomId != null && roomIds.TryGetValue(source.FinalRoomId, out var final) ? final : map.Rooms.Count == 1 ? 0 : -1;
            foreach (var room in map.Rooms)
            {
                map.Width = Math.Max(map.Width, room.X + 1);
                map.Height = Math.Max(map.Height, room.Y + 1);
            }

            // the hallways, the shortest first: the map's window takes its scale from the first
            var halls = source.Areas.Where(a => !a.IsRoom && a.Tiles.Count >= 2).OrderBy(a => a.Tiles.Count).ToList();
            foreach (var area in halls)
            {
                Dd1MapTile first = area.Tiles[0], last = area.Tiles[area.Tiles.Count - 1];
                if (first.DoorTo == null || last.DoorTo == null || !roomIds.ContainsKey(first.DoorTo) || !roomIds.ContainsKey(last.DoorTo))
                {
                    notes?.LeftOut.Add("hallway " + area.Id + ": an end without a room");
                    continue;
                }
                var hall = new Hallway { Id = map.Hallways.Count, RoomA = roomIds[first.DoorTo], RoomB = roomIds[last.DoorTo] };
                var previous = -1;
                for (var i = 1; i < area.Tiles.Count - 1; i++)
                {
                    var tile = area.Tiles[i];
                    var segment = new Segment { MapX = tile.X, MapY = maxY - tile.Y };
                    switch (tile.Content)
                    {
                        case Dd1Map.Fight:
                        case Dd1Map.RoomFight:
                            segment.Content = HallContent.Battle;
                            segment.Battle = new EncounterSlot { Kind = EncounterKind.Hallway, Tier = tier };
                            break;
                        case Dd1Map.Trap:
                            segment.Content = HallContent.Trap;
                            segment.PropId = DungeonProps.Pick(props.Traps, rng, null);
                            if (segment.PropId == null) segment.Content = HallContent.Empty;
                            break;
                        case Dd1Map.Curio:
                            segment.Content = HallContent.Curio;
                            segment.PropId = DungeonProps.Pick(props.HallCurios, rng, null);
                            if (segment.PropId == null) segment.Content = HallContent.Empty;
                            break;
                        case Dd1Map.Hunger:
                            segment.Content = HallContent.Hunger;
                            break;
                        case Dd1Map.Obstacle:
                            segment.Content = HallContent.Obstacle;
                            segment.PropId = DungeonProps.Pick(props.Obstacles, rng, null);
                            if (segment.PropId == null) segment.Content = HallContent.Empty;
                            break;
                        case Dd1Map.SideDoor:
                            notes?.LeftOut.Add("hallway " + area.Id + " tile " + i + ": a door in the hallway's side" + (tile.DoorTo != null ? " to " + tile.DoorTo : ""));
                            break;
                        case Dd1Map.Nothing:
                            break;
                        default:
                            notes?.LeftOut.Add("hallway " + area.Id + " tile " + i + ": content " + tile.Content);
                            break;
                    }
                    if (props.CorridorWalls.Count > 0)
                    {
                        // no two tiles in a row with the same wall when there is a choice (as the generator has it)
                        var choices = props.CorridorWalls.Where(w => w != previous).ToList();
                        if (choices.Count == 0) choices = props.CorridorWalls;
                        segment.Wall = previous = choices[rng.Next(choices.Count)];
                    }
                    hall.Segments.Add(segment);
                }
                // the engine walks a hallway tile by tile: one without tiles of its own gets one
                if (hall.Segments.Count == 0) hall.Segments.Add(new Segment { Wall = props.CorridorWalls.Count > 0 ? props.CorridorWalls[0] : 0 });
                map.Hallways.Add(hall);
            }

            if (questType == QuestTypes.KillBoss) map.Objective = new QuestObjective { Kind = ObjectiveKind.KillBoss, Required = 1 };
            else map.Objective = new QuestObjective { Kind = ObjectiveKind.Explore, Required = Math.Max(1, map.Rooms.Count) };
            return map;
        }
    }
}
