using System;
using System.Collections.Generic;
using DD2Estate.Core;
using DD2Estate.Dev;
using DD2Estate.Estate;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// Test commands for what stands in the corridor, scouting and secret rooms, for the dev bridge
    /// (python tools/bridge.py run corridor.props, ... run corridor.click kind=curio, ... run dungeon.scout
    /// reach=1, ... run dungeon.secret action=reveal). They do what the mouse and the rules do, without the mouse.
    /// </summary>
    [EstateModule]
    internal static class CorridorPropsDev
    {
        private static void Register()
        {
            // What stands in the area on screen and how it is drawn. {"scale":1.1,"lift":4,"standoff":200} tune
            // the placement live (DD1's size x scale; DD1 units of lift and stand-off), {"rebuild":true} builds the props anew.
            AgentBridge.Register("corridor.props", o =>
            {
                var view = CorridorView.Instance;
                if (view == null) return "no corridor";
                var tuned = o["scale"] != null || o["lift"] != null || o["standoff"] != null;
                if (o["scale"] != null) CorridorProps.Scale = (float)o["scale"];
                if (o["lift"] != null) CorridorProps.Lift = (float)o["lift"];
                if (o["standoff"] != null) CorridorProps.StandOff = (float)o["standoff"];
                if (tuned || (bool?)o["rebuild"] == true) view.Props.Build();
                return Describe(view);
            });
            // A click on a prop: {"index":2}, or {"kind":"curio"} with an optional "tile". In a hallway the party
            // walks up to it first; the expedition hears of it on arrival (CorridorProps.Clicked).
            AgentBridge.Register("corridor.click", o =>
            {
                var view = CorridorView.Instance;
                if (view == null) return "no corridor";
                var prop = Find(view, o);
                if (prop == null) return "no such prop here";
                if (view.Frozen || DungeonHud.Asking) return "the party is busy";
                view.Click(prop);
                return new { prop = One(view, prop, view.Props.All), walking = view.Walking, heard = CorridorProps.HasListener };
            });
            // As if the pointer rested on a prop: {"kind":"trap"} (or "index"; "tile" to choose among several). On a
            // spotted trap that can still be tried every hero's tray then wears DD1's trap-disarm icon with the
            // hero's chance in its tooltip (raid.feedback lists the trays). {"clear":true} gives the pointer back.
            AgentBridge.Register("corridor.hover", o =>
            {
                var view = CorridorView.Instance;
                if (view == null) return "no corridor";
                if ((bool?)o["clear"] == true)
                {
                    CorridorProps.DevHovered = null;
                    return "the pointer is the pointer's again";
                }
                var prop = Find(view, o);
                if (prop == null) return "no such prop here";
                CorridorProps.DevHovered = prop;
                return new { prop = One(view, prop, view.Props.All), armedTrap = view.Props.Armed(prop), canDisarm = DungeonRun.Current != null && DungeonRun.Current.CanDisarm(prop) };
            });
            // Any DD1 prop in a tile of the area on screen, to look at its art: {"kind":"obstacle","id":"thorny_thicket",
            // "tile":2,"animation":"idle"}; {"play":true} runs the animation once. Gone at the next rebuild.
            AgentBridge.Register("corridor.show", o =>
            {
                var view = CorridorView.Instance;
                if (view == null) return "no corridor";
                if (!Enum.TryParse((string)o["kind"] ?? "curio", true, out PropKind kind) || kind == PropKind.SecretDoor || kind == PropKind.Exit) return "kind: curio, trap, obstacle or door";
                var id = (string)o["id"];
                if (kind == PropKind.Door && id == null) id = DungeonRun.Current?.Exploration.Map.DungeonId;
                if (id == null) return "id: a DD1 prop id (sarcophagus, spikes, rubble...)";
                var prop = view.Props.Stage(kind, id, (int?)o["tile"] ?? Mathf.Max(1, view.CurrentTile), (string)o["animation"] ?? "idle", (bool?)o["play"] ?? false);
                return prop.Model == null ? (object)("DD1 has no art for " + id) : One(view, prop, view.Props.All);
            });
            // A prop in an animation of the tester's choosing: {"index":2,"animation":"sprung","play":true}.
            AgentBridge.Register("corridor.pose", o =>
            {
                var view = CorridorView.Instance;
                if (view == null) return "no corridor";
                var prop = Find(view, o);
                if (prop?.Model == null) return "no such prop here (or it has no art)";
                view.Props.Pose(prop, (string)o["animation"], (bool?)o["play"] ?? false);
                return One(view, prop, view.Props.All);
            });

            // What the party knows of the map and how well it scouts.
            AgentBridge.Register("dungeon.scouting", o =>
            {
                var x = DungeonRun.Current?.Exploration;
                if (x == null) return "no expedition";
                return new
                {
                    chance = x.ScoutChance,
                    bonus = x.ScoutBonus,
                    torchlight = x.LightBand.Value("player_scouting_increase") / 100,
                    enabled = x.ScoutingEnabled,
                    revealing = Scouting.Revealing,
                    banner = Scouting.BannerAlpha,
                    timings = Scouting.DescribeTimings(),
                    map = Known(x)
                };
            });
            // A scout, as the rules make one: {"reach":1,"target":"all","secret":false}. reach 0 is the whole map;
            // targets: all, curios, traps, obstacles, hall_battles, room_battles; secret shows hidden doors too.
            AgentBridge.Register("dungeon.scout", o =>
            {
                var run = DungeonRun.Current;
                if (run == null) return "no expedition";
                var x = run.Exploration;
                var found = new List<object>();
                string summary = null;
                foreach (var e in x.Scout((int?)o["reach"] ?? 1, (string)o["target"] ?? "all", (bool?)o["secret"] ?? false))
                {
                    if (!(e is Scouted scouted)) continue;
                    Scouting.Report(x, scouted);
                    summary = Scouting.Summary(x, scouted);
                    foreach (var room in scouted.Rooms) found.Add(new { room, type = x.Map.Rooms[room].Type.ToString() });
                    foreach (var tile in scouted.Tiles) found.Add(new { hallway = tile.HallwayId, segment = tile.Segment, content = tile.Content.ToString() });
                }
                return new { summary = summary ?? "nothing new", found, chance = x.ScoutChance };
            });
            // Secret rooms: {"action":"state"} says where they are, "reveal" shows their doors on the map (a critical
            // scout does), "enter" clicks the door (the hallway must be on screen; the party walks to it), "leave"
            // goes back to the hallway.
            AgentBridge.Register("dungeon.secret", o =>
            {
                var run = DungeonRun.Current;
                if (run == null) return "no expedition";
                var x = run.Exploration;
                var view = CorridorView.Instance;
                switch ((string)o["action"] ?? "state")
                {
                    case "reveal":
                        // no room, no ordinary tile: hidden doors only, over the whole map
                        foreach (var e in x.Scout(0, "hidden_doors", true))
                            if (e is Scouted scouted) Scouting.Report(x, scouted);
                        break;
                    case "enter":
                        var door = view != null ? view.Props.Find(PropKind.SecretDoor) : null;
                        if (door == null || door.State == null) return "no known hidden door in the hallway on screen";
                        if (!CorridorProps.HasListener) return "DungeonRun does not listen to CorridorProps.Clicked yet";
                        view.Click(door);
                        break;
                    case "leave":
                        if (!x.InSecretRoom) return "the party is not in a secret room";
                        if (!CorridorProps.HasListener) return "DungeonRun does not listen to CorridorProps.Clicked yet";
                        view?.Click(view.Props.Find(PropKind.Exit));
                        break;
                }
                var rooms = new List<object>();
                foreach (var room in x.Map.Rooms)
                {
                    if (room.Type != RoomType.Secret || !x.Map.FindSecretDoor(room.Id, out var hall, out var segment)) continue;
                    var hallway = x.Map.Hallways[hall];
                    rooms.Add(new
                    {
                        room = room.Id,
                        curio = room.CurioId,
                        hallway = hall,
                        segment,
                        between = new[] { hallway.RoomA, hallway.RoomB },
                        known = x.IsSegmentKnown(hall, segment),
                        entered = x.IsRoomVisited(room.Id),
                        opened = x.IsRoomCurioUsed(room.Id)
                    });
                }
                return new { inside = x.InSecretRoom, doorHere = x.SecretRoomHere, rooms };
            });
        }

        private static CorridorProp Find(CorridorView view, JObject o)
        {
            var all = view.Props.All;
            if (o["index"] != null)
            {
                var index = (int)o["index"];
                return index >= 0 && index < all.Count ? all[index] : null;
            }
            if (!Enum.TryParse((string)o["kind"] ?? "curio", true, out PropKind kind)) return null;
            return view.Props.Find(kind, (int?)o["tile"] ?? -1);
        }

        private static object Describe(CorridorView view)
        {
            var props = view.Props;
            var list = new List<object>();
            foreach (var prop in props.All) list.Add(One(view, prop, props.All));
            return new
            {
                area = props.Room != null ? "room " + props.Room.Id + " (" + props.Room.Type + ")" : props.Hallway != null ? "hallway " + props.Hallway.Id : "not told (CorridorProps.ShowHallway / ShowRoom)",
                listener = CorridorProps.HasListener,
                scale = CorridorProps.Scale,
                lift = CorridorProps.Lift,
                standoff = CorridorProps.StandOff,
                dd1 = new
                {
                    wallDepth = CorridorNumbers.WallZ, curioX = CorridorNumbers.CurioX, curioRoomX = CorridorNumbers.CurioRoomX, curioDepth = CorridorNumbers.CurioZ,
                    trapX = CorridorNumbers.TrapX, trapDepth = CorridorNumbers.TrapZ, obstacleX = CorridorNumbers.ObstacleX, obstacleDepth = CorridorNumbers.CurioZ
                },
                props = list
            };
        }

        private static object One(CorridorView view, CorridorProp prop, IReadOnlyList<CorridorProp> all)
        {
            var index = -1;
            for (var i = 0; i < all.Count; i++)
                if (all[i] == prop) index = i;
            var model = prop.Model;
            // where to click it: the middle of what it draws, in screen pixels (input.click x= y=); and its box as
            // DD1's reference renderer prints it: on a 1920x1080 screen, y from the top
            float[] screen = null, box = null;
            var camera = Camera.main;
            var root = view.Props.Root;
            if (camera != null && model != null && prop.State != null && root != null)
            {
                var centre = model.Bounds.center * prop.UnitsPerPixel;
                var at = camera.WorldToScreenPoint(root.TransformPoint(prop.Foot + new Vector3(centre.x, centre.y, 0f)));
                screen = new[] { Mathf.Round(at.x), Mathf.Round(at.y) };
                box = CorridorDev.Box1080(camera, root.TransformPoint(prop.Foot + new Vector3(model.Bounds.xMin, model.Bounds.yMin, 0f) * prop.UnitsPerPixel),
                    root.TransformPoint(prop.Foot + new Vector3(model.Bounds.xMax, model.Bounds.yMax, 0f) * prop.UnitsPerPixel));
            }
            return new
            {
                index,
                kind = prop.Kind.ToString(),
                id = prop.Id,
                tile = prop.Tile,
                segment = prop.Segment,
                room = prop.Room,
                state = prop.State,
                staged = prop.Staged,
                skeleton = prop.Skeleton,
                animation = model?.Animation,
                time = model?.Time,
                playing = model?.Playing,
                meshes = model?.Layers,
                shader = model?.MaterialInfo,
                foot = new[] { prop.Foot.x, prop.Foot.y, prop.Foot.z },
                pixels = model != null ? new[] { model.Bounds.xMin, model.Bounds.yMin, model.Bounds.xMax, model.Bounds.yMax } : null,
                screen,
                box1080 = box,
                tint = model != null ? new[] { model.Tint.r, model.Tint.g, model.Tint.b } : null,
                title = prop.Kind == PropKind.Curio ? PropText.CurioTitle(prop.Id) : prop.Kind == PropKind.Obstacle ? PropText.ObstacleTitle(prop.Id) : null
            };
        }

        // Every room and tile the party knows something of, as the map words it.
        private static object Known(Exploration x)
        {
            var rooms = new List<object>();
            for (var i = 0; i < x.Map.Rooms.Count; i++)
            {
                if (!x.IsRoomKnown(i) || x.Map.Rooms[i].Type == RoomType.Secret) continue;
                rooms.Add(new { room = i, icon = Icon(Scouting.RoomIcon(x, i)), visited = x.IsRoomVisited(i), tooltip = Scouting.RoomTooltip(x, i) });
            }
            var tiles = new List<object>();
            foreach (var hall in x.Map.Hallways)
                for (var s = 0; s < hall.Segments.Count; s++)
                {
                    if (!x.IsSegmentKnown(hall.Id, s)) continue;
                    tiles.Add(new { hallway = hall.Id, segment = s, icon = Icon(Scouting.TileIcon(x, hall.Id, s)), marker = Icon(Scouting.TileMarker(x, hall.Id, s)), tooltip = Scouting.TileTooltip(x, hall.Id, s) });
                }
            return new { rooms, tiles };
        }

        private static string Icon(string path)
        {
            return path == null ? null : path.Substring(Scouting.Icons.Length).Replace(".png", "");
        }
    }
}
