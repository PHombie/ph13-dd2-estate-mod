using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DD2Estate.Core;

namespace DD2Estate.CoreTests
{
    /// <summary>What the party sees standing in a hallway or a room (Core/ExplorationProps.cs), and where DD1 stands it.</summary>
    public static class PropTests
    {
        private static readonly RaidRules FallbackRules = RaidRules.Load(new NoDd1Files());

        /// <summary>
        /// Entrance 0 - crate, spikes, rubble, hidden door - room 1 (a fight guarding a chest); room 2 is the
        /// secret room behind the hidden door. Hand-made, so these tests need no DD1 files.
        /// </summary>
        private static DungeonMap Map()
        {
            var map = new DungeonMap { DungeonId = "crypts", QuestType = QuestTypes.Explore, Length = 2, Tier = 1, Width = 2, Height = 1, TrapId = "spikes" };
            map.Rooms.Add(new Room { Id = 0, X = 0, Type = RoomType.Entrance });
            map.Rooms.Add(new Room { Id = 1, X = 1, Type = RoomType.Treasure, Battle = new EncounterSlot { Kind = EncounterKind.Room, Tier = 1 }, CurioId = "heirloom_chest" });
            map.Rooms.Add(new Room { Id = 2, X = -1, Y = -1, Type = RoomType.Secret, CurioId = "secret_stash" });
            var hall = new Hallway { Id = 0, RoomA = 0, RoomB = 1 };
            hall.Segments.Add(new Segment { Content = HallContent.Curio, PropId = "crate" });
            hall.Segments.Add(new Segment { Content = HallContent.Trap, PropId = "spikes" });
            hall.Segments.Add(new Segment { Content = HallContent.Obstacle, PropId = "rubble" });
            hall.Segments.Add(new Segment { Content = HallContent.Secret, SecretRoomId = 2 });
            map.Hallways.Add(hall);
            map.Objective = new QuestObjective { Kind = ObjectiveKind.Explore, Required = 2 };
            return map;
        }

        private static Exploration Start(DungeonMap map) => new Exploration(map, FallbackRules, null, 1) { ScoutingEnabled = false };

        private static List<PropSight> Hallway(Exploration x, int toward)
        {
            var sights = new List<PropSight>();
            ExplorationProps.InHallway(x, 0, toward, sights);
            return sights;
        }

        private static List<PropSight> InRoom(Exploration x, int room)
        {
            var sights = new List<PropSight> { new PropSight { Id = "left over from the last frame" } };
            ExplorationProps.InRoom(x, room, sights);
            return sights;
        }

        private static string State(List<PropSight> sights, PropKind kind, int tile = -1)
        {
            var found = sights.Where(s => s.Kind == kind && (tile < 0 || s.Tile == tile)).ToList();
            Check.Equal(1, found.Count, kind + " sights" + (tile >= 0 ? " in tile " + tile : ""));
            return found[0].State;
        }

        public static void HallwaySightsFollowTheRules()
        {
            var x = Start(Map());
            var seen = Hallway(x, 1);
            Check.True(seen.Select(s => s.Kind).SequenceEqual(new[] { PropKind.Door, PropKind.Door, PropKind.Curio, PropKind.Trap, PropKind.Obstacle, PropKind.SecretDoor }), "doors first, then the tiles in walking order");
            Check.True(seen.Select(s => s.Tile).SequenceEqual(new[] { 0, 5, 1, 2, 3, 4 }), "tiles: the door behind, the door ahead, the four between");
            Check.True(seen[0].Room == 0 && seen[1].Room == 1 && seen[0].Id == "crypts" && seen[0].Segment == -1, "the doors lead to the hallway's rooms");
            Check.True(seen[2].Id == "crate" && seen[3].Id == "spikes" && seen[4].Id == "rubble" && seen[5].Id == null, "prop ids");
            Check.True(seen.Skip(2).Select(s => s.Segment).SequenceEqual(new[] { 0, 1, 2, 3 }), "segments toward room B");

            // before a step is taken: the curio and the rubble stand there for all to see, the trap and the hidden door do not
            Check.Equal(ExplorationProps.Idle, State(seen, PropKind.Curio), "a curio ahead");
            Check.Equal(ExplorationProps.Idle, State(seen, PropKind.Obstacle), "rubble ahead");
            Check.Equal(null, State(seen, PropKind.Trap), "an unscouted trap");
            Check.Equal(null, State(seen, PropKind.SecretDoor), "an unknown hidden door");
            Check.Equal(ExplorationProps.Closed, State(seen, PropKind.Door, 0), "the door behind, before the party walks");
            Check.Equal(ExplorationProps.Closed, State(seen, PropKind.Door, 5), "the door ahead");

            // a scout puts the trap on the ground, a critical one finds the door
            x.Scout(1, "traps");
            Check.Equal(ExplorationProps.Idle, State(Hallway(x, 1), PropKind.Trap), "a scouted trap");
            Check.Equal(null, State(Hallway(x, 1), PropKind.SecretDoor), "a plain scout finds no door");
            x.Scout(1, "all", true);
            Check.Equal(ExplorationProps.Known, State(Hallway(x, 1), PropKind.SecretDoor), "the hidden door");

            // the party at the crate: it lights up, the door behind stands open; opened, it stays open
            x.MoveTo(1);
            seen = Hallway(x, 1);
            Check.Equal(ExplorationProps.Active, State(seen, PropKind.Curio), "the party at the crate");
            Check.Equal(ExplorationProps.Open, State(seen, PropKind.Door, 0), "the door the party came through");
            Check.Equal(ExplorationProps.Idle, State(seen, PropKind.Trap), "the trap further on");
            x.UseCurio();
            Check.Equal(ExplorationProps.Investigated, State(Hallway(x, 1), PropKind.Curio), "the emptied crate");

            // the spotted trap under the party's eyes, then sprung by clumsy hands
            x.MoveTo(1);
            Check.True(x.Pending == PendingKind.Trap, "stopped at the trap");
            Check.Equal(ExplorationProps.Active, State(Hallway(x, 1), PropKind.Trap), "the party at the trap");
            Check.Equal(ExplorationProps.Investigated, State(Hallway(x, 1), PropKind.Curio), "the crate behind");
            x.ResolveTrap(false);
            Check.Equal(ExplorationProps.Sprung, State(Hallway(x, 1), PropKind.Trap), "sprung");

            // rubble until it is cleared
            x.MoveTo(1);
            Check.Equal(ExplorationProps.Idle, State(Hallway(x, 1), PropKind.Obstacle), "the party at the rubble");
            x.ResolveObstacle(true);
            Check.Equal(ExplorationProps.Cleared, State(Hallway(x, 1), PropKind.Obstacle), "cleared");

            // past the hidden door into the room: the far door opens, the hidden one stays where it is
            x.MoveTo(1);
            Check.True(x.Segment == 3 && x.SecretRoomHere == 2, "stopped at the hidden door");
            x.MoveTo(1);
            Check.Equal(1, x.RoomId, "in the room");
            seen = Hallway(x, 1);
            Check.Equal(ExplorationProps.Open, State(seen, PropKind.Door, 5), "the door the party went through");
            Check.Equal(ExplorationProps.Known, State(seen, PropKind.SecretDoor), "the hidden door is still there");

            // a trap nobody saw springs under the party; a disarmed one lies there
            var blind = Start(Map());
            blind.MoveTo(1);
            blind.MoveTo(1);
            Check.Equal(ExplorationProps.Sprung, State(Hallway(blind, 1), PropKind.Trap), "walked into");
            var careful = Start(Map());
            careful.Scout(1, "traps");
            careful.MoveTo(1);
            careful.MoveTo(1);
            careful.ResolveTrap(true);
            Check.Equal(ExplorationProps.Idle, State(Hallway(careful, 1), PropKind.Trap), "disarmed");

            Check.True(ExplorationProps.PlaysOnce(ExplorationProps.Sprung) && ExplorationProps.PlaysOnce(ExplorationProps.Investigated)
                       && ExplorationProps.PlaysOnce(ExplorationProps.Cleared) && ExplorationProps.PlaysOnce(ExplorationProps.Open), "states that are reached once");
            Check.True(!ExplorationProps.PlaysOnce(ExplorationProps.Idle) && !ExplorationProps.PlaysOnce(ExplorationProps.Active) && !ExplorationProps.PlaysOnce(ExplorationProps.Closed), "states that go on");
        }

        public static void HallwayIsLaidOutTheWayItIsWalked()
        {
            var map = Map();
            var x = Start(map);
            x.Scout(0, "all", true);
            // walked back from room 1 to the entrance: the same tiles, the other way round
            var back = Hallway(x, 0);
            Check.True(back[0].Room == 1 && back[0].Tile == 0 && back[1].Room == 0 && back[1].Tile == 5, "the doors swap ends");
            Check.True(back.Skip(2).Select(s => s.Kind).SequenceEqual(new[] { PropKind.SecretDoor, PropKind.Obstacle, PropKind.Trap, PropKind.Curio }), "the tiles in the order they are met");
            Check.True(back.Skip(2).Select(s => s.Tile).SequenceEqual(new[] { 1, 2, 3, 4 }) && back.Skip(2).Select(s => s.Segment).SequenceEqual(new[] { 3, 2, 1, 0 }), "tiles and segments");
            for (var tile = 1; tile <= 4; tile++)
            {
                Check.Equal(tile - 1, ExplorationProps.SegmentOf(map.Hallways[0], 1, tile), "toward room B");
                Check.Equal(4 - tile, ExplorationProps.SegmentOf(map.Hallways[0], 0, tile), "toward room A");
            }

            // both views agree on what each segment shows
            var forth = Hallway(x, 1);
            foreach (var sight in forth.Skip(2))
                Check.Equal(sight.State, back.Single(s => s.Segment == sight.Segment && s.Kind == sight.Kind).State, "state of segment " + sight.Segment);

            // the same thing is the same thing whatever its state; a thing in another tile is another
            var again = Hallway(x, 1);
            x.MoveTo(1);
            x.UseCurio();
            var later = Hallway(x, 1);
            Check.True(later[2].Same(again[2]) && later[2].State != again[2].State, "the crate, opened");
            Check.True(!later[2].Same(later[3]) && !forth[2].Same(back[5]), "different things");

            var sights = new List<PropSight> { new PropSight() };
            ExplorationProps.InHallway(x, 7, 1, sights);
            Check.Equal(0, sights.Count, "no such hallway");
        }

        public static void WhatMovesInLaterIsSeenToo()
        {
            // an empty hallway shows its doors and nothing else
            var map = Map();
            foreach (var segment in map.Hallways[0].Segments)
            {
                segment.Content = HallContent.Empty;
                segment.PropId = null;
                segment.SecretRoomId = -1;
            }
            map.Hallways[0].Segments[1].Content = HallContent.Battle;
            map.Hallways[0].Segments[2].Content = HallContent.Hunger;
            var x = Start(map);
            x.Scout(0, "all");
            Check.True(Hallway(x, 1).All(s => s.Kind == PropKind.Door), "fights and hunger are not seen before they happen");

            // a trap that moves into a walked tile (as corridor_return_content does it) is the dungeon's own, and unseen
            map.Hallways[0].Segments[0].Content = HallContent.Trap;
            var seen = Hallway(x, 1);
            Check.True(seen.Count == 3 && seen[2].Kind == PropKind.Trap && seen[2].Id == "spikes" && seen[2].Tile == 1, "the new trap");
            Check.Equal(ExplorationProps.Idle, seen[2].State, "it sits on a tile the scout had seen");
        }

        public static void RoomSightsFollowTheRules()
        {
            var x = Start(Map());
            Check.Equal(0, InRoom(x, 0).Count, "an empty room (and the list is cleared)");
            var chest = InRoom(x, 1);
            Check.True(chest.Count == 1 && chest[0].Kind == PropKind.Curio && chest[0].Id == "heirloom_chest" && chest[0].Room == 1 && chest[0].Segment == -1 && chest[0].Tile == 0, "the room's chest");
            Check.Equal(ExplorationProps.Idle, chest[0].State, "seen from afar");

            // in the room: guarded until the fight is won, then within reach, then open
            x.Scout(0, "all", true);
            while (x.RoomId != 1)
            {
                if (x.Pending == PendingKind.Trap) x.ResolveTrap(true);
                else if (x.Pending == PendingKind.Obstacle) x.ResolveObstacle(true);
                else x.MoveTo(1);
            }
            Check.True(x.Pending == PendingKind.Fight, "the room's fight");
            Check.Equal(ExplorationProps.Idle, InRoom(x, 1)[0].State, "guarded");
            x.ResolveFight(FightOutcome.Won);
            Check.Equal(ExplorationProps.Active, InRoom(x, 1)[0].State, "within reach");
            x.UseCurio();
            Check.Equal(ExplorationProps.Investigated, InRoom(x, 1)[0].State, "opened");

            // the secret room: its stash and the way out
            var secret = InRoom(x, 2);
            Check.True(secret.Select(s => s.Kind).SequenceEqual(new[] { PropKind.Curio, PropKind.Exit }) && secret[0].Id == "secret_stash", "stash and way out");
            Check.Equal(ExplorationProps.Idle, secret[0].State, "the party is not in it");
            Check.Equal(ExplorationProps.Known, secret[1].State, "the way out");
            // one tile back into the hallway: the hidden door's (it stops the party only the first time)
            x.Step(0);
            Check.True(x.Segment == 3 && x.SecretRoomHere == 2, "at the hidden door again");
            x.EnterSecretRoom();
            Check.Equal(ExplorationProps.Active, InRoom(x, 2)[0].State, "at the stash");
            x.UseCurio();
            Check.Equal(ExplorationProps.Investigated, InRoom(x, 2)[0].State, "the stash, opened");
            Check.Equal(0, InRoom(x, 9).Count, "no such room");
        }

        public static void PlacementComesFromDd1()
        {
            var p = PropPlacement.Load(Dd1.Files);

            // the numbers are the files' own: read them again with nothing but string splitting
            var world = new Dictionary<string, double>();
            foreach (var raw in File.ReadAllLines(Path.Combine(Dd1.Root, "scripts", "world.darkest")))
            {
                var parts = raw.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && parts[0].StartsWith(".")) world[parts[0].Substring(1)] = double.Parse(parts[1], CultureInfo.InvariantCulture);
            }
            Check.Equal(world["distance_to_interior_background"], p.WallDepth, "wall depth");
            Check.Equal(world["curio_tile_centre_x_offset"], p.CurioX, "curio x");
            Check.Equal(world["curio_tile_centre_x_offset_room"], p.CurioRoomX, "curio x in a room");
            Check.Equal(world["curio_z_position"], p.CurioDepth, "curio depth");
            Check.Equal(world["trap_tile_centre_x_offset"], p.TrapX, "trap x");
            Check.Equal(world["trap_z_position"], p.TrapDepth, "trap depth");
            Check.Equal(world["obstacle_tile_centre_x_offset"], p.ObstacleX, "obstacle x");
            Check.True(p.WallDepth > p.CurioDepth && p.CurioDepth > p.TrapDepth && p.TrapDepth > 0, "the wall is behind the curios, the curios behind the traps");

            var camera = File.ReadAllLines(Path.Combine(Dd1.Root, "scripts", "camera.darkest")).Select(l => l.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)).ToList();
            var corridor = camera.First(l => l.Length == 4 && l[0] == ".OffsetFromPartyLeader");
            var room = camera.First(l => l.Length == 4 && l[0] == ".Position");
            Check.Equal(double.Parse(corridor[2], CultureInfo.InvariantCulture), p.CorridorCameraHeight, "hallway camera height");
            Check.Equal(-double.Parse(corridor[3], CultureInfo.InvariantCulture), p.CorridorCameraDistance, "hallway camera distance");
            Check.Equal(double.Parse(room[2], CultureInfo.InvariantCulture), p.RoomCameraHeight, "room camera height");
            Check.Equal(-double.Parse(room[3], CultureInfo.InvariantCulture), p.RoomCameraDistance, "room camera distance");
            Check.True(p.CorridorCameraDistance > 0 && p.RoomCameraDistance > 0 && p.CorridorCameraHeight > 0, "the camera is in front of the heroes and above the floor");

            // a door hangs in the wall: the wall's own size, its foot where the floor meets the wall
            p.Place(PropKind.Door, false, out var right, out var size, out var rise);
            Check.True(right == 0 && Math.Abs(size - 1) < 1e-12, "a door is as large as its wall");
            Check.Near(p.CorridorCameraHeight * p.WallDepth / p.CorridorCameraDistance, rise, 1e-9, "the floor's rise from the heroes to the wall");

            // an obstacle stands where the heroes walk: on their line, as much larger than the wall as they are
            p.Place(PropKind.Obstacle, false, out right, out var obstacleSize, out var obstacleRise);
            Check.True(right == p.ObstacleX && obstacleRise == 0, "an obstacle stands on the heroes' line");
            Check.Near((p.CorridorCameraDistance + p.WallDepth) / p.CorridorCameraDistance, obstacleSize, 1e-12, "the size of what stands with the heroes");

            p.Place(PropKind.Trap, false, out right, out var trapSize, out var trapRise);
            Check.Equal(p.TrapX, right, "trap x");
            p.Place(PropKind.Curio, false, out right, out var curioSize, out var curioRise);
            Check.Equal(p.CurioX, right, "curio x");
            Check.Near((p.CorridorCameraDistance + p.WallDepth) / (p.CorridorCameraDistance + p.CurioDepth), curioSize, 1e-12, "curio size");
            Check.True(obstacleSize > trapSize && trapSize > curioSize && curioSize > size, "nearer the camera is larger");
            Check.True(obstacleRise < trapRise && trapRise < curioRise && curioRise < rise, "nearer the camera is lower on the floor");

            // a room's curio stands by the room's own offset under the room's camera
            p.Place(PropKind.Curio, true, out right, out var roomSize, out var roomRise);
            Check.Equal(p.CurioRoomX, right, "curio x in a room");
            Check.Near((p.RoomCameraDistance + p.WallDepth) / (p.RoomCameraDistance + p.CurioDepth), roomSize, 1e-12, "curio size in a room");
            Check.True(roomRise > 0 && roomRise != curioRise, "the room camera sees it from elsewhere");

            // without DD1 the fallbacks still stand things somewhere sensible
            var fallback = PropPlacement.Load(new NoDd1Files());
            fallback.Place(PropKind.Curio, false, out right, out size, out rise);
            Check.True(right > 0 && size > 1 && size < 2 && rise > 0 && rise < 100, "fallback placement: " + right + ", " + size + ", " + rise);
        }
    }
}
