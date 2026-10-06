using System.Collections.Generic;

namespace DD2Estate.Core
{
    public enum PropKind
    {
        Curio,
        Trap,
        Obstacle,
        Door,
        /// <summary>A hidden door the party knows of: DD1 draws nothing for it, the wall of its tile is the door.</summary>
        SecretDoor,
        /// <summary>The way out of a secret room, which has no hallway of its own.</summary>
        Exit
    }

    /// <summary>One thing that stands in the hallway or room the party looks at, and the state the party sees it in.</summary>
    public struct PropSight
    {
        public PropKind Kind;
        /// <summary>DD1 prop id; the dungeon's id for a door; null for a hidden door and the way out.</summary>
        public string Id;
        /// <summary>Tile of the hallway as it is walked: 0 the door behind, 1..n its tiles, n + 1 the door ahead. A room is tile 0.</summary>
        public int Tile;
        /// <summary>The tile's index in <see cref="Hallway.Segments"/>; -1 for a door and for what stands in a room.</summary>
        public int Segment;
        /// <summary>The room a door leads to, or the room a curio stands in; -1 otherwise.</summary>
        public int Room;
        /// <summary>One of the <see cref="ExplorationProps"/> states, named as DD1 names the animation; null: not to be seen.</summary>
        public string State;

        /// <summary>The same thing in the same place, whatever its state.</summary>
        public bool Same(PropSight other) => Kind == other.Kind && Id == other.Id && Tile == other.Tile && Segment == other.Segment && Room == other.Room;
    }

    /// <summary>
    /// What the party sees standing in a hallway or a room, by DD1's rules of sight: a curio is there for all to
    /// see and lights up while it can be turned to, a trap is on the ground only once scouted (or sprung), an
    /// obstacle is gone once cleared, a door stays open once walked through, a hidden door is there once a
    /// critical scout found it. Fights and hunger are not seen before they happen. The states carry the names
    /// DD1 gives the animations of its props (<c>props/prop_definitions.json</c>: a curio's "investigated" is
    /// "investigate", a door's is "open"; a trap's is "sprung", an obstacle's "clear").
    /// </summary>
    public static class ExplorationProps
    {
        public const string Idle = "idle";
        /// <summary>DD1's highlight of a prop the party can turn to.</summary>
        public const string Active = "active";
        public const string Investigated = "investigate";
        public const string Sprung = "sprung";
        public const string Cleared = "clear";
        public const string Closed = "closed";
        public const string Open = "open";
        /// <summary>A hidden door or a way out: nothing is drawn, the place takes the click.</summary>
        public const string Known = "known";

        /// <summary>States that are reached once and stay; the others go on for as long as they last.</summary>
        public static bool PlaysOnce(string state) => state == Investigated || state == Sprung || state == Cleared || state == Open;

        /// <summary>The index in <see cref="Hallway.Segments"/> of a tile of the hallway as it is walked toward a room.</summary>
        public static int SegmentOf(Hallway hall, int towardRoomId, int tile) => towardRoomId == hall.RoomB ? tile - 1 : hall.Segments.Count - tile;

        /// <summary>
        /// The hallway as it is walked toward one of its rooms, written into <paramref name="into"/>: the two
        /// doors first, then what its tiles hold, in walking order. An empty tile gives nothing.
        /// </summary>
        public static void InHallway(Exploration x, int hallwayId, int towardRoomId, List<PropSight> into)
        {
            into.Clear();
            var map = x.Map;
            var hall = map.GetHallway(hallwayId);
            if (hall == null) return;
            var count = hall.Segments.Count;
            into.Add(Door(x, hall, 0, hall.Other(towardRoomId)));
            into.Add(Door(x, hall, count + 1, towardRoomId));
            var here = x.RoomId < 0 && x.HallwayId == hallwayId ? x.Segment : -1;
            for (var tile = 1; tile <= count; tile++)
            {
                var index = SegmentOf(hall, towardRoomId, tile);
                var segment = hall.Segments[index];
                var sight = new PropSight { Tile = tile, Segment = index, Room = -1 };
                var done = x.IsSegmentDone(hallwayId, index);
                var known = x.IsSegmentKnown(hallwayId, index);
                switch (segment.Content)
                {
                    case HallContent.Curio:
                        sight.Kind = PropKind.Curio;
                        sight.Id = segment.PropId;
                        sight.State = done ? Investigated : here == index ? Active : Idle;
                        break;
                    case HallContent.Trap:
                        sight.Kind = PropKind.Trap;
                        sight.Id = segment.PropId ?? map.TrapId;
                        // DD1: "scouted traps will be visible on the ground"; a disarmed one lies there, harmless
                        sight.State = x.IsTrapSprung(hallwayId, index) ? Sprung : !known ? null : !done && here == index ? Active : Idle;
                        break;
                    case HallContent.Obstacle:
                        sight.Kind = PropKind.Obstacle;
                        sight.Id = segment.PropId;
                        sight.State = done ? Cleared : Idle;
                        break;
                    case HallContent.Secret:
                        sight.Kind = PropKind.SecretDoor;
                        sight.State = known ? Known : null;
                        break;
                    default:
                        continue;
                }
                into.Add(sight);
            }
        }

        // DD1 keeps a door open once it was used: the party has been in the room and on the tile next to it.
        private static PropSight Door(Exploration x, Hallway hall, int tile, int roomId)
        {
            var end = roomId == hall.RoomA ? 0 : hall.Segments.Count - 1;
            return new PropSight
            {
                Kind = PropKind.Door, Id = x.Map.DungeonId, Tile = tile, Segment = -1, Room = roomId,
                State = x.IsRoomVisited(roomId) && x.IsSegmentVisited(hall.Id, end) ? Open : Closed
            };
        }

        /// <summary>A room: its curio or treasure, and for a secret room the way back out.</summary>
        public static void InRoom(Exploration x, int roomId, List<PropSight> into)
        {
            into.Clear();
            var room = x.Map.GetRoom(roomId);
            if (room == null) return;
            if (room.CurioId != null)
            {
                // a guarded curio waits for its fight before it can be turned to
                var guarded = room.Battle != null && !x.IsRoomFightWon(roomId);
                into.Add(new PropSight
                {
                    Kind = PropKind.Curio, Id = room.CurioId, Segment = -1, Room = roomId,
                    State = x.IsRoomCurioUsed(roomId) ? Investigated : x.RoomId == roomId && !guarded ? Active : Idle
                });
            }
            if (room.Type == RoomType.Secret) into.Add(new PropSight { Kind = PropKind.Exit, Segment = -1, Room = roomId, State = Known });
        }
    }

    /// <summary>
    /// Where DD1 stands a prop: <c>scripts/world.darkest</c> (pixels right of the tile's centre, depth behind
    /// the heroes) and <c>scripts/camera.darkest</c> (where the camera looks from). DD1's hallway is a 3D scene
    /// with the heroes on a plane, the wall art a way behind it and the camera in front, above the floor; from
    /// that follow a prop's size against the wall art and how far above the heroes' feet its foot shows. The
    /// initial values are FALLBACKS for an install without the two files, not a copy to rely on.
    /// </summary>
    public sealed class PropPlacement
    {
        /// <summary>The wall art's distance behind the heroes (<c>distance_to_interior_background</c>).</summary>
        public double WallDepth = 400;
        public double CurioX = 135, CurioRoomX = 75, CurioDepth = 75;
        public double TrapX = 0, TrapDepth = 15;
        public double ObstacleX = 75;
        /// <summary>world.darkest gives an obstacle no depth of its own: it stands where the heroes walk.</summary>
        public double ObstacleDepth = 0;
        /// <summary>The hallway camera above the floor and in front of the heroes (<c>OffsetFromPartyLeader</c> y and -z).</summary>
        public double CorridorCameraHeight = 270, CorridorCameraDistance = 1400;
        /// <summary>The room camera (<c>m_RoomCameraParameters.Position</c> y and -z).</summary>
        public double RoomCameraHeight = 300, RoomCameraDistance = 1240;

        public static PropPlacement Load(IDd1Files files)
        {
            var p = new PropPlacement();
            var world = Block(files, "scripts/world.darkest", "world_parameters");
            if (world != null)
            {
                p.WallDepth = world.Number("distance_to_interior_background", 0, p.WallDepth);
                p.CurioX = world.Number("curio_tile_centre_x_offset", 0, p.CurioX);
                p.CurioRoomX = world.Number("curio_tile_centre_x_offset_room", 0, p.CurioRoomX);
                p.CurioDepth = world.Number("curio_z_position", 0, p.CurioDepth);
                p.TrapX = world.Number("trap_tile_centre_x_offset", 0, p.TrapX);
                p.TrapDepth = world.Number("trap_z_position", 0, p.TrapDepth);
                p.ObstacleX = world.Number("obstacle_tile_centre_x_offset", 0, p.ObstacleX);
            }
            var corridor = Block(files, "scripts/camera.darkest", "m_CorridorCameraParameters");
            if (corridor != null && corridor.All("OffsetFromPartyLeader").Count >= 3)
            {
                p.CorridorCameraHeight = corridor.Number("OffsetFromPartyLeader", 1, p.CorridorCameraHeight);
                p.CorridorCameraDistance = -corridor.Number("OffsetFromPartyLeader", 2, -p.CorridorCameraDistance);
            }
            var room = Block(files, "scripts/camera.darkest", "m_RoomCameraParameters");
            if (room != null && room.All("Position").Count >= 3)
            {
                p.RoomCameraHeight = room.Number("Position", 1, p.RoomCameraHeight);
                p.RoomCameraDistance = -room.Number("Position", 2, -p.RoomCameraDistance);
            }
            return p;
        }

        private static DarkestBlock Block(IDd1Files files, string file, string name)
        {
            foreach (var block in DarkestFile.Parse(files.ReadText(file)))
                if (block.Name == name) return block;
            return null;
        }

        /// <summary>
        /// Where DD1 shows a prop of a kind: pixels right of the middle of its tile (of the room, for a room's
        /// curio); its size against the wall art behind it (what stands nearer the camera is drawn larger); and
        /// how many wall pixels its foot shows above the heroes' feet (nearer is lower on the floor).
        /// </summary>
        public void Place(PropKind kind, bool room, out double right, out double size, out double rise)
        {
            double depth;
            switch (kind)
            {
                case PropKind.Curio:
                    right = room ? CurioRoomX : CurioX;
                    depth = CurioDepth;
                    break;
                case PropKind.Trap:
                    right = TrapX;
                    depth = TrapDepth;
                    break;
                case PropKind.Obstacle:
                    right = ObstacleX;
                    depth = ObstacleDepth;
                    break;
                default:
                    // a door hangs in the wall
                    right = 0;
                    depth = WallDepth;
                    break;
            }
            double distance = room ? RoomCameraDistance : CorridorCameraDistance, height = room ? RoomCameraHeight : CorridorCameraHeight;
            size = (distance + WallDepth) / (distance + depth);
            rise = height * (1 / distance - 1 / (distance + depth)) * (distance + WallDepth);
        }
    }
}
