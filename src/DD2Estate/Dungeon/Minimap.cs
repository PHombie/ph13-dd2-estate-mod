using System;
using System.Collections.Generic;
using DD2Estate.Core;
using DD2Estate.Dd1;
using DD2Estate.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// The dungeon map of DD1's map panel, drawn with DD1's map icons: a room is a 64 px icon on a grid of
    /// 24 px tiles (scripts/layout/panel.map.darkest, map_layout.tilesize), a hallway is its tiles in a row.
    /// DD1 keeps one door tile at either end of a hallway between the room's own cell and the first tile
    /// that is walked (scripts/starting_save/persist.map.json shows a room at x, the door at x + 1, the four
    /// tiles at x + 2..5, the far door at x + 6, the next room at x + 7), so two rooms a hallway of n tiles
    /// apart stand n + 3 tiles apart and the doors lie under the room icons.
    ///
    /// The scale is the one of DD1's own frames (_lab/dd1_ref/raid: its icons were looked for in them): the
    /// art at half its size, a room 32 px wide, a hallway's four tiles 48 px long, two rooms 83 or 84 px
    /// apart. The layout file's ".scale 1.00" is not what the frames show; a frame beats the file.
    ///
    /// The map is bigger than its window (map_layout.clip). As in DD1 it follows the party, can be dragged
    /// with the right button ("[RIGHT-CLICK] and [DRAG] to pan map") and, the owner's wish, with the left one
    /// held as well (a press that moves less than the event system's drag threshold is still a click on the
    /// room under it), goes back to the party a while after the last drag (manual_to_follow_delay) and zooms
    /// with the wheel between input.min_zoom_scale and
    /// max_zoom_scale. Where DD1 holds the party: not in the window's middle but 16 px left of it and 19 px
    /// above (the party's mark stands at (1285, 881) of the screen in ten of DD1's frames; the window's middle
    /// is (1300.5, 899.5)): half the window's size counted from the panel's corner, not from the window's own.
    /// Every room and tile is visible from the start (as unknown), contents show once scouted or visited, a
    /// click on a room next to the party walks there, and the room the party is on its way to wears DD1's
    /// mark for it (moving_room.png, seen in the frames of a hallway).
    /// The pointer is told what a room holds and what a marker on a hallway tile stands for, in DD1's words.
    /// tools/preview_raid_hud.py draws the same map offline: change one, change the other.
    /// </summary>
    internal class Minimap : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
    {
        private const string Icons = "panels/icons_map/";
        private const float RoomSize = 64f;                 // room_*.png, marker_room_visited.png
        private const float TileArt = 24f;                  // hall_*.png, marker_*.png
        private static readonly Vector2 IndicatorSize = new Vector2(51f, 48f);     // indicator.png
        private const int DoorTiles = 1;                    // DD1's door tile at either end of a hallway
        private const float FollowSpeed = 6f;               // how fast the window slides back to the party
        // The scale DD1's frames show the map at when nobody has touched the wheel. GUESS: the wheel's limits
        // (input.min_zoom_scale 0.35, max_zoom_scale 1.5) are read on the same footing, so the map can be drawn
        // back a little and brought three times closer.
        private const float FrameScale = 0.5f;

        private Exploration _x;
        private RectTransform _window, _content;
        private float _tile, _zoom;
        private readonly List<Image> _rooms = new List<Image>();
        private readonly List<Image> _visited = new List<Image>();
        private readonly List<Vector2> _roomAt = new List<Vector2>();
        private readonly List<List<Image>> _tiles = new List<List<Image>>();
        private readonly List<List<Image>> _markers = new List<List<Image>>();
        private RectTransform _indicator, _moving;
        private Image _banner;
        private TMPro.TextMeshProUGUI _bannerText;
        private Vector2 _party, _focus;
        private bool _focusSet;
        private float _manualUntil;
        private bool _dragging;             // a button is down and has carried the map: it stays where it is put
        private int _drags;                 // dev bridge: how many times the map was carried
        private float _nextRefresh;
        private int _hoverRoom = -1;

        /// <summary>
        /// A room or a hallway tile the pointer rests on: the room's index (-1 for a tile), what to say about it
        /// and its icon; -1 and nulls when the pointer leaves.
        /// </summary>
        public Action<int, string, RectTransform> Hint;

        /// <param name="panel">The right panel (DD1's 720x360 map panel); the map takes the window DD1 clips it to.</param>
        public static Minimap Build(Transform panel)
        {
            var layout = RaidLayout.Current;
            // The window takes drags and the wheel; the icons inside it take the clicks.
            var window = UiKit.Image("Minimap", panel, null, Color.clear, true);
            var rect = (RectTransform)window.transform;
            rect.PlaceTopLeft(layout.MapClip.position, RaidUi.TopLeft, layout.MapClip.size);
            rect.gameObject.AddComponent<RectMask2D>();
            var map = rect.gameObject.AddComponent<Minimap>();
            map._window = rect;
            map._tile = layout.MapTile;
            map._zoom = Mathf.Clamp(FrameScale * layout.MapScale, layout.MapMinZoom, layout.MapMaxZoom);
            map._content = UiKit.Rect("Content", rect);
            map._content.Place(RaidUi.Middle, RaidUi.Middle, Vector2.zero, Vector2.zero);
            return map;
        }

        public void Show(Exploration exploration)
        {
            _x = exploration;
            RaidUi.Clear(_content);
            _rooms.Clear();
            _visited.Clear();
            _roomAt.Clear();
            _tiles.Clear();
            _markers.Clear();
            _indicator = _moving = null;
            _focusSet = false;
            _hoverRoom = -1;
            if (_x == null) return;

            var map = _x.Map;
            // Rooms stand on the generator's grid; a hallway of n tiles puts its ends n + 3 tiles apart.
            var segments = map.Hallways.Count > 0 ? map.Hallways[0].Segments.Count : 4;
            // (a hand-made DD1 map gives its rooms and tiles in tiles of its own: a room every so many, a hallway bent)
            var step = map.TileGrid ? _tile : (segments + 2 * DoorTiles + 1) * _tile;
            foreach (var room in map.Rooms) _roomAt.Add(new Vector2(room.X * step, room.Y * step));

            // hallway tiles first so the room icons lie over their ends
            for (var h = 0; h < map.Hallways.Count; h++)
            {
                var hall = map.Hallways[h];
                var a = _roomAt[hall.RoomA];
                var b = _roomAt[hall.RoomB];
                var count = hall.Segments.Count;
                var tiles = new List<Image>();
                var markers = new List<Image>();
                for (var i = 0; i < count; i++)
                {
                    var p = Vector2.Lerp(a, b, (i + 1f + DoorTiles) / (count + 2f * DoorTiles + 1f));
                    if (map.TileGrid && hall.Segments[i].MapX >= 0 && hall.Segments[i].MapY >= 0) p = new Vector2(hall.Segments[i].MapX * step, hall.Segments[i].MapY * step);
                    // a tile takes the pointer for its marker's tooltip; drags and the wheel go on to the window
                    var tile = UiKit.Image("h" + hall.Id + "_" + i, _content, Dd1Install.Sprite(Icons + "hall_dark.png"), null, true);
                    ((RectTransform)tile.transform).Place(RaidUi.Middle, RaidUi.Middle, p, new Vector2(TileArt, TileArt));
                    var hover = tile.gameObject.AddComponent<MinimapTileHover>();
                    hover.Map = this;
                    hover.HallwayId = h;
                    hover.Segment = i;
                    tiles.Add(tile);
                    var marker = UiKit.Image("m", tile.transform, null);
                    UiKit.Stretch((RectTransform)marker.transform);
                    marker.enabled = false;
                    markers.Add(marker);
                }
                _tiles.Add(tiles);
                _markers.Add(markers);
            }

            foreach (var room in map.Rooms)
            {
                var icon = UiKit.Image("r" + room.Id, _content, Dd1Install.Sprite(Icons + "room_unknown.png"), null, true);
                ((RectTransform)icon.transform).Place(RaidUi.Middle, RaidUi.Middle, _roomAt[room.Id], new Vector2(RoomSize, RoomSize));
                icon.gameObject.SetActive(room.X >= 0);     // secret rooms are off the grid
                var click = icon.gameObject.AddComponent<MinimapRoomClick>();
                click.RoomId = room.Id;
                click.Map = this;
                _rooms.Add(icon);
                var visited = UiKit.Image("v", icon.transform, Dd1Install.Sprite(Icons + "marker_room_visited.png"));
                UiKit.Stretch((RectTransform)visited.transform);
                visited.enabled = false;
                _visited.Add(visited);
            }

            // DD1 marks the room the pointer would send the party to.
            var moving = UiKit.Image("Moving", _content, Dd1Install.Sprite(Icons + "moving_room.png"));
            _moving = (RectTransform)moving.transform;
            _moving.Place(RaidUi.Middle, RaidUi.Middle, Vector2.zero, new Vector2(RoomSize, RoomSize));
            _moving.gameObject.SetActive(false);

            var indicator = UiKit.Image("Indicator", _content, Dd1Install.Sprite(Icons + "indicator.png"));
            _indicator = (RectTransform)indicator.transform;
            _indicator.Place(RaidUi.Middle, RaidUi.Middle, Vector2.zero, IndicatorSize);
            if (_banner == null)
            {
                // DD1's banner over the map while a scout's finds come up (panels/icons_map/scoutingbanner.png, 366x63);
                // it hangs in the window, not on the map that slides under it
                _banner = UiKit.Image("Scouting", _window, Dd1Install.Sprite(Scouting.Banner));
                ((RectTransform)_banner.transform).Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(366f, 63f));
                // DD1's style for the word: scouting_text (DwarvenAxe medium, drawn 1:1) in its colour of the same name
                _bannerText = UiKit.Text("Word", _banner.transform, Scouting.BannerText, "scouting_text", Dd1Fonts.Colour("scouting_text", UiKit.Notable));
                UiKit.Stretch((RectTransform)_bannerText.transform);
                _banner.gameObject.SetActive(false);
            }
            Refresh();
        }

        /// <summary>DD1's home button: the window goes back to the party at once.</summary>
        public void CentreOnParty()
        {
            _manualUntil = 0f;
            _focus = _party;
            Apply();
        }

        private void Update()
        {
            if (_x == null || _indicator == null) return;
            if (Time.unscaledTime >= _nextRefresh || Scouting.Revealing)
            {
                _nextRefresh = Time.unscaledTime + 0.2f;
                Refresh();
            }

            // indicator_layout: the party's mark rises and falls and swells a little as it does
            var layout = RaidLayout.Current;
            var period = Mathf.Max(0.01f, layout.IndicatorUpTime + layout.IndicatorDownTime);
            var phase = Time.unscaledTime % period;
            var up = phase < layout.IndicatorUpTime ? phase / Mathf.Max(0.01f, layout.IndicatorUpTime) : 1f - (phase - layout.IndicatorUpTime) / Mathf.Max(0.01f, layout.IndicatorDownTime);
            up = Mathf.SmoothStep(0f, 1f, up);
            _indicator.anchoredPosition = _party + new Vector2(0f, layout.IndicatorBounce * up);
            _indicator.localScale = Vector3.one * Mathf.Lerp(layout.IndicatorMinScale, layout.IndicatorMaxScale, up);

            if (!_dragging && Time.unscaledTime >= _manualUntil) _focus = Vector2.Lerp(_focus, _party, Mathf.Clamp01(Time.unscaledDeltaTime * FollowSpeed));
            Apply();
        }

        // A drag whose end never came (the window was put away under the pointer) does not hold the map for good.
        private void OnDisable() => _dragging = false;

        // Only the player's wheel changes the scale (DD1 does not draw the map back by itself). What the map
        // follows stands where DD1 holds it: left of and above the window's middle by the window's own corner.
        private void Apply()
        {
            _content.localScale = new Vector3(_zoom, _zoom, 1f);
            var clip = RaidLayout.Current.MapClip;
            _content.anchoredPosition = -_focus * _zoom + new Vector2(-clip.xMin, clip.yMin);
        }

        /// <summary>Dev bridge: the scale, what the window follows and where the party's mark stands on DD1's screen.</summary>
        public object Describe()
        {
            Vector2 mark = Vector2.zero;
            var screen = (RectTransform)_window.parent.parent;
            if (_indicator != null)
            {
                var local = screen.InverseTransformPoint(_indicator.position);
                mark = new Vector2(local.x + 960f, 1080f - local.y);
            }
            // the rooms the party could walk to now, where their icons stand on DD1's screen, and whether the window shows them
            var rooms = new List<object>();
            for (var i = 0; _x != null && i < _rooms.Count; i++)
            {
                if (!_rooms[i].gameObject.activeSelf || !_x.CanMoveTo(i)) continue;
                var local = screen.InverseTransformPoint(_rooms[i].transform.position);
                rooms.Add(new { id = i, x = local.x + 960f, y = 1080f - local.y, inWindow = RectTransformUtility.RectangleContainsScreenPoint(_window, RectTransformUtility.WorldToScreenPoint(null, _rooms[i].transform.position), null) });
            }
            var corners = new Vector3[4];
            _window.GetWorldCorners(corners);
            var topLeft = screen.InverseTransformPoint(corners[1]);
            var bottomRight = screen.InverseTransformPoint(corners[3]);
            return new
            {
                window = new[] { topLeft.x + 960f, 1080f - topLeft.y, bottomRight.x + 960f, 1080f - bottomRight.y },
                reachable = rooms,
                scale = _zoom, room = RoomSize * _zoom, tile = _tile * _zoom,
                party = new[] { _party.x, _party.y }, focus = new[] { _focus.x, _focus.y },
                mark = new[] { mark.x, mark.y },
                following = !_dragging && Time.unscaledTime >= _manualUntil,
                dragging = _dragging, drags = _drags,
                followsIn = _dragging ? RaidLayout.Current.MapFollowDelay : Mathf.Max(0f, _manualUntil - Time.unscaledTime),
                destination = _moving != null && _moving.gameObject.activeSelf
            };
        }

        /// <summary>Dev bridge: the wheel, by steps (map_layout.zoom_key_value each); 0 puts DD1's scale back.</summary>
        public void DevZoom(int steps)
        {
            var layout = RaidLayout.Current;
            _zoom = steps == 0 ? Mathf.Clamp(FrameScale * layout.MapScale, layout.MapMinZoom, layout.MapMaxZoom)
                : Mathf.Clamp(_zoom + steps * layout.MapZoomStep, layout.MapMinZoom, layout.MapMaxZoom);
            Apply();
        }

        private void Refresh()
        {
            var map = _x.Map;
            // Scouting words the rules' knowledge in DD1's icons and holds back what a scout has found until
            // its turn in the reveal has come.
            for (var i = 0; i < map.Rooms.Count; i++)
            {
                var sprite = Dd1Install.Sprite(Scouting.RoomIcon(_x, i));
                if (_rooms[i].sprite != sprite) _rooms[i].sprite = sprite;
                _rooms[i].transform.localScale = Vector3.one * Scouting.RoomScale(i);
                _visited[i].enabled = Scouting.RoomVisited(_x, i);
            }
            for (var h = 0; h < map.Hallways.Count; h++)
                for (var seg = 0; seg < map.Hallways[h].Segments.Count; seg++)
                {
                    var tile = Dd1Install.Sprite(Scouting.TileIcon(_x, h, seg));
                    if (_tiles[h][seg].sprite != tile) _tiles[h][seg].sprite = tile;
                    var marker = Scouting.TileMarker(_x, h, seg);
                    _markers[h][seg].enabled = marker != null;
                    if (marker == null) continue;
                    _markers[h][seg].sprite = Dd1Install.Sprite(marker);
                    _markers[h][seg].transform.localScale = Vector3.one * Scouting.MarkerScale(h, seg);
                }

            if (_banner != null)
            {
                var banner = Scouting.BannerAlpha;
                _banner.gameObject.SetActive(banner > 0f);
                if (banner > 0f)
                {
                    _banner.color = new Color(1f, 1f, 1f, banner);
                    _bannerText.alpha = banner;
                    _bannerText.transform.localScale = Vector3.one * Scouting.BannerTextScale;
                }
            }

            var found = false;
            if (_x.RoomId >= 0 && _x.RoomId < _rooms.Count && map.Rooms[_x.RoomId].X >= 0)
            {
                _party = _roomAt[_x.RoomId];
                found = true;
            }
            else if (_x.HallwayId >= 0 && _x.Segment >= 0 && _x.HallwayId < _tiles.Count && _x.Segment < _tiles[_x.HallwayId].Count)
            {
                _party = ((RectTransform)_tiles[_x.HallwayId][_x.Segment].transform).anchoredPosition;
                found = true;
            }
            _indicator.gameObject.SetActive(found);
            if (found && !_focusSet)
            {
                _focus = _party;
                _focusSet = true;
            }

            // DD1's mark of the room the party is on its way to (its frames of a hallway: the blue arrows lie on
            // the room at the hallway's far end); in a room, on the neighbour the pointer would send the party to
            var run = DungeonRun.Current;
            var goal = run != null && run.Exploration == _x ? run.Destination : -1;
            if (goal < 0 && _hoverRoom >= 0 && _hoverRoom < _rooms.Count && _x.CanMoveTo(_hoverRoom)) goal = _hoverRoom;
            var target = goal >= 0 && goal < _roomAt.Count && map.Rooms[goal].X >= 0;
            _moving.gameObject.SetActive(target);
            if (target) _moving.anchoredPosition = _roomAt[goal];
        }

        // ---- the window: drag and wheel --------------------------------------------------------------

        // DD1: "[RIGHT-CLICK] and [DRAG] to pan map" (str_help_raid_hallway_3). The left button carries the map
        // as well: the event system calls a press a drag only once the pointer has left its drag threshold, and
        // takes the click away from the room under the press then; a press that stays inside it is the room's click.
        private static bool Carries(PointerEventData eventData)
        {
            return eventData.button == PointerEventData.InputButton.Right || eventData.button == PointerEventData.InputButton.Left;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_x == null || !Carries(eventData)) return;
            _dragging = true;
            _drags++;
            // what the pointer said about the room it was pressed on is not true of where the map goes
            Hint?.Invoke(-1, null, null);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_x == null || !_dragging || !Carries(eventData)) return;
            var scale = _window.lossyScale.x > 0f ? _window.lossyScale.x : 1f;
            _focus -= eventData.delta / (scale * _zoom);
            _manualUntil = Time.unscaledTime + RaidLayout.Current.MapFollowDelay;
            Apply();
        }

        // The map goes back to the party manual_to_follow_delay after the button is let go, however long it was held.
        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_dragging) return;
            _dragging = false;
            _manualUntil = Time.unscaledTime + RaidLayout.Current.MapFollowDelay;
        }

        public void OnScroll(PointerEventData eventData)
        {
            var layout = RaidLayout.Current;
            if (Mathf.Approximately(eventData.scrollDelta.y, 0f)) return;
            _zoom = Mathf.Clamp(_zoom + Mathf.Sign(eventData.scrollDelta.y) * layout.MapZoomStep, layout.MapMinZoom, layout.MapMaxZoom);
            Apply();
        }

        internal void RoomHovered(int roomId, bool inside)
        {
            if (!inside)
            {
                if (_hoverRoom == roomId) _hoverRoom = -1;
                Hint?.Invoke(-1, null, null);
                return;
            }
            _hoverRoom = roomId;
            if (_x == null || roomId < 0 || roomId >= _rooms.Count) return;
            var what = Scouting.RoomTooltip(_x, roomId);
            var go = _x.CanMoveTo(roomId) ? RaidText.Get("str_move_to_this_room", "Move to this room") : null;
            var text = what != null && go != null ? RaidTooltip.Titled(what, go) : what ?? go;
            if (text != null) Hint?.Invoke(roomId, text, (RectTransform)_rooms[roomId].transform);
        }

        // DD1 names what the marker on a hallway tile stands for (str_map_ac_*_tooltip).
        internal void TileHovered(int hallwayId, int segment, bool inside)
        {
            if (!inside)
            {
                Hint?.Invoke(-1, null, null);
                return;
            }
            if (_x == null || hallwayId < 0 || hallwayId >= _tiles.Count || segment < 0 || segment >= _tiles[hallwayId].Count) return;
            var what = Scouting.TileHint(_x, hallwayId, segment);
            if (what != null) Hint?.Invoke(-1, what, (RectTransform)_tiles[hallwayId][segment].transform);
        }

        private static string RoomIcon(Room room)
        {
            switch (room.Type)
            {
                case RoomType.Entrance: return "room_entrance.png";
                case RoomType.Battle: return "room_battle.png";
                case RoomType.Treasure: return "room_treasure.png";
                case RoomType.Curio: return "room_curio.png";
                case RoomType.Boss: return "room_boss.png";
                default: return "room_empty.png";
            }
        }

        // DD1's own words for what a room holds (str_map_*_tooltip).
        private static string RoomName(Room room)
        {
            switch (room.Type)
            {
                case RoomType.Battle: return RaidText.Get("str_map_ac_battle_tooltip", "Battle");
                case RoomType.Treasure: return RaidText.Get(room.Battle != null ? "str_map_ac_guarded_treasure_tooltip" : "str_map_treasure_tooltip", "Treasure");
                case RoomType.Curio: return RaidText.Get(room.Battle != null ? "str_map_ac_guarded_curio_tooltip" : "str_map_ac_curio_tooltip", "Curio");
                case RoomType.Boss: return RaidText.Get("str_map_boss_tooltip", "Boss");
                default: return null;
            }
        }
    }

    internal class MinimapRoomClick : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public int RoomId;
        public Minimap Map;

        public void OnPointerClick(PointerEventData eventData)
        {
            // a hand that slips a little has still clicked the room; one that carried the map has not (the event
            // system sends no click after a drag)
            if (eventData.button != PointerEventData.InputButton.Left || eventData.dragging) return;
            DungeonRun.Current?.TravelTo(RoomId);
        }

        public void OnPointerEnter(PointerEventData eventData) => Map?.RoomHovered(RoomId, true);

        public void OnPointerExit(PointerEventData eventData) => Map?.RoomHovered(RoomId, false);

        private void OnDisable() => Map?.RoomHovered(RoomId, false);
    }

    internal class MinimapTileHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public int HallwayId, Segment;
        public Minimap Map;
        private bool _inside;

        public void OnPointerEnter(PointerEventData eventData)
        {
            _inside = true;
            Map?.TileHovered(HallwayId, Segment, true);
        }

        public void OnPointerExit(PointerEventData eventData) => Leave();

        // A hidden tile gets no exit event: its tooltip would stay.
        private void OnDisable() => Leave();

        private void Leave()
        {
            if (!_inside) return;
            _inside = false;
            Map?.TileHovered(HallwayId, Segment, false);
        }
    }
}
