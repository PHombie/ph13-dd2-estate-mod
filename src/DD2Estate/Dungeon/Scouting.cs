using System;
using System.Collections.Generic;
using DD2Estate.Core;
using DD2Estate.Dd1;
using DD2Estate.Estate;
using UnityEngine;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// What the dungeon map shows, the way DD1 shows it, for whoever draws the map: the icon of each room and
    /// hallway tile, the marker of what a tile holds, DD1's tooltip for both, and the way a scout's finds come
    /// up one after another under DD1's "Scouting" banner. The knowledge itself is the rules'
    /// (<see cref="Exploration"/>: known, visited, done); this class only words it in DD1's icons
    /// (<c>panels/icons_map</c>), strings (<c>str_map_*</c>) and timings
    /// (<c>scripts/layout/panel.map.darkest</c>, block <c>fog_of_war</c>).
    ///
    /// The expedition calls <see cref="Report"/> for every <see cref="Scouted"/> event; until an item's turn
    /// has come the questions below still answer "unknown" for it, so a map that asks every frame while
    /// <see cref="Revealing"/> is true plays DD1's reveal without knowing about it.
    /// </summary>
    internal static class Scouting
    {
        public const string Icons = "panels/icons_map/";
        /// <summary>DD1's banner over the map while a scout's finds come up (366 x 63).</summary>
        public const string Banner = Icons + "scoutingbanner.png";

        // FALLBACKS for an install without the layout file; Timings() reads DD1's own.
        private static float _maxTotal = 2f, _between = 0.4f, _tileTime = 0.4f, _contentTime = 0.4f, _contentScale = 1.3f, _textTime = 0.25f;
        private static bool _timingsRead;
        /// <summary>How long the banner stays once the last find is up, and how long it takes to go (the mod's own: DD1's file does not say).</summary>
        public static float BannerHold = 1.2f, BannerFade = 0.4f;

        private static Exploration _for;
        private static readonly Dictionary<int, float> RoomAt = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> TileAt = new Dictionary<int, float>();
        private static float _started = -1000f, _ends = -1000f;
        // set while the answers are wanted as they will be once every find is up
        private static bool _all;

        /// <summary>Raised by <see cref="Report"/>: something new is on the map.</summary>
        public static event Action<Scouted> Reported;

        // ---- what the map shows ----------------------------------------------------------------------

        /// <summary>DD1 icon of a room (a path in the DD1 install): unknown until scouted or entered.</summary>
        public static string RoomIcon(Exploration x, int roomId)
        {
            if (!x.IsRoomKnown(roomId) || Pending(x, RoomAt, roomId)) return Icons + "room_unknown.png";
            var room = x.Map.Rooms[roomId];
            switch (room.Type)
            {
                case RoomType.Entrance: return Icons + "room_entrance.png";
                case RoomType.Battle: return Icons + "room_battle.png";
                case RoomType.Treasure: return Icons + "room_treasure.png";
                case RoomType.Curio: return Icons + "room_curio.png";
                case RoomType.Boss: return Icons + "room_boss.png";
                default: return Icons + "room_empty.png";
            }
        }

        /// <summary>DD1's check mark over a room the party has been in (<c>marker_room_visited.png</c>); the entrance carries none.</summary>
        public static bool RoomVisited(Exploration x, int roomId)
        {
            return x.IsRoomVisited(roomId) && x.Map.Rooms[roomId].Type != RoomType.Entrance;
        }

        public const string RoomVisitedMarker = Icons + "marker_room_visited.png";

        /// <summary>DD1's map tooltip for a room; null while the room is unknown (DD1 says nothing of those).</summary>
        public static string RoomTooltip(Exploration x, int roomId)
        {
            if (!x.IsRoomKnown(roomId) || Pending(x, RoomAt, roomId)) return null;
            var room = x.Map.Rooms[roomId];
            var fight = room.Battle != null && !x.IsRoomFightWon(roomId);
            var curio = room.CurioId != null && !x.IsRoomCurioUsed(roomId);
            if (room.Type == RoomType.Boss) return fight ? Text("str_map_boss_tooltip", "Boss") : null;
            if (room.QuestCurio && curio) return Text("str_map_quest_location_tooltip", "Quest Location");
            if (room.Type == RoomType.Treasure && curio) return fight ? Text("str_map_ac_guarded_treasure_tooltip", "Room Battle with Treasure") : Text("str_map_treasure_tooltip", "Treasure");
            if (curio) return fight ? Text("str_map_ac_guarded_curio_tooltip", "Room Battle with Curio") : Text("str_map_ac_curio_tooltip", "Curio");
            return fight ? Text("str_map_ac_battle_tooltip", "Battle") : null;
        }

        /// <summary>DD1 icon of a hallway tile: walked (clear), seen by a scout (dim), or neither (dark).</summary>
        public static string TileIcon(Exploration x, int hallwayId, int segment)
        {
            if (x.IsSegmentVisited(hallwayId, segment)) return Icons + "hall_clear.png";
            return Seen(x, hallwayId, segment) ? Icons + "hall_dim.png" : Icons + "hall_dark.png";
        }

        /// <summary>
        /// DD1 marker of what a known tile still holds: a fight, a curio, a trap, an obstacle, a hidden door.
        /// Null for a tile that is unknown, empty or dealt with, and for the tile where the party will have to
        /// eat (<see cref="Shown"/>). The star of a hidden door stays until the stash behind it is opened.
        /// </summary>
        public static string TileMarker(Exploration x, int hallwayId, int segment)
        {
            var content = Shown(x, hallwayId, segment);
            switch (content)
            {
                case HallContent.Battle: return Icons + "marker_battle.png";
                case HallContent.Curio: return Icons + "marker_curio.png";
                case HallContent.Trap: return Icons + "marker_trap.png";
                case HallContent.Obstacle: return Icons + "marker_obstacle.png";
                case HallContent.Secret: return Icons + "marker_secret.png";
                default: return null;
            }
        }

        /// <summary>DD1's map tooltip for what a tile holds; null when there is no marker.</summary>
        public static string TileTooltip(Exploration x, int hallwayId, int segment)
        {
            switch (Shown(x, hallwayId, segment))
            {
                case HallContent.Battle: return Text("str_map_ac_battle_tooltip", "Battle");
                case HallContent.Curio: return Text("str_map_ac_curio_tooltip", "Curio");
                case HallContent.Trap: return Text("str_map_ac_trap_tooltip", "Trap");
                case HallContent.Obstacle: return Text("str_map_ac_obstacle_tooltip", "Obstacle");
                case HallContent.Secret: return Text("str_map_ac_hidden_door_tooltip", "Secret Door");
                default: return null;
            }
        }

        /// <summary>
        /// What the pointer is told on a hallway tile of the map: DD1's tooltip for the marker it wears
        /// (str_map_ac_battle_tooltip, _curio_, _trap_, _obstacle_, _hidden_door_). Null without a marker.
        /// </summary>
        public static string TileHint(Exploration x, int hallwayId, int segment) => TileTooltip(x, hallwayId, segment);

        private static bool Seen(Exploration x, int hallwayId, int segment)
        {
            return x.IsSegmentKnown(hallwayId, segment) && !Pending(x, TileAt, TileKey(hallwayId, segment));
        }

        // What the tile's marker stands for; Empty: no marker.
        // Hunger has none (the owner, 2026-10-06: "remove the showing of food consumption, as in the original
        // DD1"): DD1's map never tells where the party will have to eat, a scout does not see it, and nothing
        // is told of it beforehand: no marker, no tooltip, no word in the scout's line of the log. The meal
        // still comes where the map's maker put it (Exploration: PendingKind.Hunger).
        private static HallContent Shown(Exploration x, int hallwayId, int segment)
        {
            if (!Seen(x, hallwayId, segment)) return HallContent.Empty;
            var tile = x.Map.Hallways[hallwayId].Segments[segment];
            if (tile.Content == HallContent.Hunger) return HallContent.Empty;
            if (tile.Content == HallContent.Secret)
                return tile.SecretRoomId >= 0 && tile.SecretRoomId < x.Map.Rooms.Count && x.IsRoomCurioUsed(tile.SecretRoomId) ? HallContent.Empty : HallContent.Secret;
            return x.IsSegmentDone(hallwayId, segment) ? HallContent.Empty : tile.Content;
        }

        // ---- a scout's finds coming up ---------------------------------------------------------------

        /// <summary>
        /// The expedition scouted: the new rooms and tiles come up on the map one after another, nearest
        /// first, DD1's <c>time_between_reveals</c> apart and all within <c>maximum_total_reveal_time</c>.
        /// </summary>
        public static void Report(Exploration x, Scouted scouted)
        {
            if (x == null || scouted == null) return;
            Timings();
            if (!ReferenceEquals(_for, x)) Clear();
            _for = x;

            // hallway tiles in the order a walker from the party's side meets them, each room after the hallways before it
            var from = x.RoomId >= 0 && !x.InSecretRoom ? x.RoomId : x.TowardRoomId;
            var distance = x.Map.Distances(from);
            var finds = new List<KeyValuePair<float, int>>();      // order, then tile key (>= 0) or -1 - room id
            foreach (var tile in scouted.Tiles)
            {
                var hall = x.Map.Hallways[tile.HallwayId];
                int a = Near(distance, hall.RoomA), b = Near(distance, hall.RoomB);
                var along = a <= b ? tile.Segment : hall.Segments.Count - 1 - tile.Segment;
                finds.Add(new KeyValuePair<float, int>(Mathf.Min(a, b) + (along + 1f) / (hall.Segments.Count + 2f), TileKey(tile.HallwayId, tile.Segment)));
            }
            foreach (var room in scouted.Rooms) finds.Add(new KeyValuePair<float, int>(Near(distance, room), -1 - room));
            if (finds.Count == 0) return;
            finds.Sort((p, q) => p.Key != q.Key ? p.Key.CompareTo(q.Key) : p.Value.CompareTo(q.Value));

            var now = Time.unscaledTime;
            var step = finds.Count > 1 ? Mathf.Min(_between, _maxTotal / (finds.Count - 1)) : 0f;
            // the banner's word scales in first
            var first = now + _textTime;
            for (var i = 0; i < finds.Count; i++)
            {
                var at = first + i * step;
                if (finds[i].Value >= 0) TileAt[finds[i].Value] = at;
                else RoomAt[-1 - finds[i].Value] = at;
            }
            _started = now;
            _ends = first + (finds.Count - 1) * step + Mathf.Max(_tileTime, _contentTime);
            Reported?.Invoke(scouted);
        }

        private static int Near(int[] distance, int roomId)
        {
            return roomId >= 0 && roomId < distance.Length && distance[roomId] >= 0 ? distance[roomId] : 1000;
        }

        /// <summary>A scout's finds are still coming up: draw the map every frame.</summary>
        public static bool Revealing => Time.unscaledTime < _ends + BannerHold + BannerFade;

        /// <summary>0..1: how much of the banner shows (it stays a little after the last find, then fades).</summary>
        public static float BannerAlpha
        {
            get
            {
                var now = Time.unscaledTime;
                if (now < _started || now >= _ends + BannerHold + BannerFade) return 0f;
                return now < _ends + BannerHold ? 1f : 1f - (now - _ends - BannerHold) / BannerFade;
            }
        }

        /// <summary>DD1's <c>scouting_text_scale_time</c>: the banner's word grows from nothing to full size.</summary>
        public static float BannerTextScale => _textTime <= 0f ? 1f : Mathf.Clamp01((Time.unscaledTime - _started) / _textTime);

        /// <summary>DD1's word on the banner (<c>str_scouting</c>).</summary>
        public static string BannerText => Text("str_scouting", "Scouting");

        /// <summary>
        /// Size of a tile's marker now: 1 at rest. A marker a scout has just found pops, as DD1's do: from
        /// nothing up to <c>tile_content_reveal_max_scale</c> and back to rest within <c>tile_content_reveal_time</c>.
        /// </summary>
        public static float MarkerScale(int hallwayId, int segment) => Pop(TileAt, TileKey(hallwayId, segment), 0f);

        /// <summary>The same for a room's icon, which was there before as an unknown room: it swells and settles.</summary>
        public static float RoomScale(int roomId) => Pop(RoomAt, roomId, 1f);

        /// <summary>0..1: how far a just-found tile or room has come out of the dark (DD1's <c>tile_reveal_time</c>); 1 at rest.</summary>
        public static float TileReveal(int hallwayId, int segment) => Fade(TileAt, TileKey(hallwayId, segment));

        public static float RoomReveal(int roomId) => Fade(RoomAt, roomId);

        /// <summary>
        /// One line for the expedition's log: what the scout saw, in DD1's map words ("Scouting: 2 x Battle,
        /// Curio, Trap, Secret Door."). Rooms are counted by what waits in them.
        /// </summary>
        public static string Summary(Exploration x, Scouted scouted)
        {
            if (x == null || scouted == null) return BannerText + ".";
            var counts = new List<KeyValuePair<string, int>>();
            void Count(string what)
            {
                if (what == null) return;
                for (var i = 0; i < counts.Count; i++)
                {
                    if (counts[i].Key != what) continue;
                    counts[i] = new KeyValuePair<string, int>(what, counts[i].Value + 1);
                    return;
                }
                counts.Add(new KeyValuePair<string, int>(what, 1));
            }
            // as the map will read once the finds are up
            _all = true;
            try
            {
                foreach (var room in scouted.Rooms) Count(RoomTooltip(x, room));
                foreach (var tile in scouted.Tiles) Count(TileTooltip(x, tile.HallwayId, tile.Segment));
            }
            finally { _all = false; }
            if (counts.Count == 0) return BannerText + ": nothing waits ahead.";
            var parts = new List<string>();
            foreach (var pair in counts) parts.Add(pair.Value > 1 ? pair.Value + " x " + pair.Key : pair.Key);
            return BannerText + ": " + string.Join(", ", parts) + ".";
        }

        /// <summary>Forgets the reveal in progress (another expedition, or none).</summary>
        public static void Clear()
        {
            _for = null;
            RoomAt.Clear();
            TileAt.Clear();
            _started = _ends = -1000f;
        }

        private static int TileKey(int hallwayId, int segment) => hallwayId * 1024 + segment;

        // Still waiting for its turn in the reveal?
        private static bool Pending(Exploration x, Dictionary<int, float> times, int key)
        {
            if (_all || !ReferenceEquals(_for, x) || times.Count == 0 || !times.TryGetValue(key, out var at)) return false;
            if (Time.unscaledTime < at) return true;
            // long past its pop: no need to remember it
            if (Time.unscaledTime > at + _tileTime + _contentTime + 1f) times.Remove(key);
            return false;
        }

        private static float Pop(Dictionary<int, float> times, int key, float from)
        {
            if (times.Count == 0 || !times.TryGetValue(key, out var at) || _contentTime <= 0f) return 1f;
            var t = (Time.unscaledTime - at) / _contentTime;
            if (t <= 0f || t >= 1f) return 1f;
            // up to the top in the first half, back to rest in the second
            return t < 0.5f ? Mathf.Lerp(from, _contentScale, t * 2f) : Mathf.Lerp(_contentScale, 1f, t * 2f - 1f);
        }

        private static float Fade(Dictionary<int, float> times, int key)
        {
            if (times.Count == 0 || !times.TryGetValue(key, out var at) || _tileTime <= 0f) return 1f;
            return Mathf.Clamp01((Time.unscaledTime - at) / _tileTime);
        }

        private static void Timings()
        {
            if (_timingsRead) return;
            _timingsRead = true;
            const string file = "scripts/layout/panel.map.darkest";
            var fog = Dd1Install.Exists(file) ? Dd1.DarkestFile.Load(file)?.Find("fog_of_war") : null;
            if (fog == null) return;
            _maxTotal = fog.Float("maximum_total_reveal_time", 0, _maxTotal);
            _between = fog.Float("time_between_reveals", 0, _between);
            _tileTime = fog.Float("tile_reveal_time", 0, _tileTime);
            _contentTime = fog.Float("tile_content_reveal_time", 0, _contentTime);
            _contentScale = fog.Float("tile_content_reveal_max_scale", 0, _contentScale);
            _textTime = fog.Float("scouting_text_scale_time", 0, _textTime);
        }

        /// <summary>DD1's timings as read, for the dev bridge.</summary>
        public static object DescribeTimings()
        {
            Timings();
            return new { maxTotal = _maxTotal, between = _between, tile = _tileTime, content = _contentTime, contentScale = _contentScale, text = _textTime };
        }

        private static string Text(string id, string fallback)
        {
            var text = Dd1Strings.Plain(Dd1Strings.Get(id));
            return text.Length > 0 ? text : fallback;
        }
    }
}
