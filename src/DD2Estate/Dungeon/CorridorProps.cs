using System;
using System.Collections.Generic;
using System.IO;
using DD2Estate.Core;
using DD2Estate.Dd1;
using DD2Estate.Estate;
using UnityEngine;

namespace DD2Estate.Dungeon
{
    /// <summary>One thing the rules put in the hallway or room on screen, and how it is drawn.</summary>
    internal class CorridorProp
    {
        public PropKind Kind;
        /// <summary>DD1 prop id; null for a hidden door and the way out.</summary>
        public string Id;
        /// <summary>Tile of the view it stands in, counted from the door the hallway is walked from (a room is tile 0).</summary>
        public int Tile;
        /// <summary>Hallway tile of the map; -1 for a door and for what stands in a room.</summary>
        public int Segment = -1;
        /// <summary>The room a door leads to, or the room a curio stands in; -1 otherwise.</summary>
        public int Room = -1;
        /// <summary>Its foot in the scene's units (DD1's: x along the hallway, y up from the floor, z behind the party),
        /// and the units one pixel of its art takes (DD1: one).</summary>
        public Vector3 Foot;
        public float UnitsPerPixel;
        /// <summary>The DD1 animation it shows (<see cref="ExplorationProps"/>); null while it is not to be seen (an unspotted trap).</summary>
        public string State;
        /// <summary>DD1 skeleton it is drawn from; null when DD1 has no art for it.</summary>
        public string Skeleton;
        /// <summary>Stood there by the dev bridge to look at: the rules know nothing of it and do not change it.</summary>
        public bool Staged;
        /// <summary>A door the party is going through: shown open whatever the rules say of it, until the area is gone.</summary>
        public bool InUse;
        internal PropModel Model;

        internal bool Is(PropSight sight) => !Staged && Kind == sight.Kind && Id == sight.Id && Tile == sight.Tile && Segment == sight.Segment && Room == sight.Room;
    }

    /// <summary>
    /// What the party walks past, drawn from DD1's own art where DD1 puts it: the curios, traps and obstacles
    /// of the hallway's tiles and its two doors, or the curio of a room. What stands where, and in which
    /// state, is the rules' to say (<see cref="ExplorationProps"/>): every frame the props are set to it, so a
    /// trap shows once spotted and stays sprung, a chest stays open, rubble goes when cleared, and a loaded game
    /// or a scouted hallway looks right without being told. A prop stands where DD1 stands it: on the floor, at
    /// its own depth behind the party (<see cref="CorridorNumbers"/>: curio and obstacle 75, trap 15, a door in the
    /// wall), one unit to a pixel of its art, so the corridor's camera gives it DD1's size and DD1's movement
    /// against wall, floor and party. tools/dd1_corridor_reference.py draws the same offline;
    /// docs/recon/dd1-corridor-rendering.md (section 4.5) has the sources.
    /// </summary>
    internal class CorridorProps
    {
        // Tunables, live through the dev bridge (corridor.props scale= lift= standoff=).
        /// <summary>On top of DD1's size (one unit to a pixel of the art).</summary>
        public static float Scale = 1f;
        /// <summary>DD1 units above the floor (DD1: 0).</summary>
        public static float Lift = 0f;
        /// <summary>How far before a prop the leader stops when sent to it, in DD1 units. The mod's own: DD1 lets a
        /// prop be used from anywhere within its reach (m_InteractionAreaPixelWidth 470, half to each side).</summary>
        public static float StandOff = 200f;

        /// <summary>DD1 art of the secret room, shared by all dungeons: a tile name with a folder in it is a path of its own for <see cref="CorridorView.ShowArea"/>.</summary>
        public const string SecretRoomWall = "dungeons/_shared/secretroom";

        /// <summary>The share of a secret room's width, from the left, that is the way back to the hallway.</summary>
        public const float ExitShare = 0.15f;

        /// <summary>
        /// The player turned to a prop the party stands at: a click on it (the party has walked up to it by
        /// then), or the dev bridge. Curio, SecretDoor and Exit only; a door or a trap answers by being walked into.
        /// </summary>
        public static event Action<CorridorProp> Clicked;

        public static bool HasListener => Clicked != null;

        private static readonly HashSet<string> Atlases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly CorridorView _view;
        private readonly List<CorridorProp> _props = new List<CorridorProp>();
        private readonly List<PropSight> _sights = new List<PropSight>();
        private Exploration _x;
        private Hallway _hall;
        private Room _room;
        private int _target = -1;
        private Transform _root;
        private CorridorProp _hovered;
        private bool _held;
        private Exploration _heldX;
        private Hallway _heldHall;
        private Room _heldRoom;
        private int _heldTarget = -1;

        public CorridorProps(CorridorView view) { _view = view; }

        public IReadOnlyList<CorridorProp> All => _props;
        public bool InSecretRoom => _room != null && _room.Type == RoomType.Secret;
        public Hallway Hallway => _hall;
        public Room Room => _room;
        /// <summary>What the props hang on: a child of the scene's root, one of whose units is one DD1 unit.</summary>
        internal Transform Root => _root;

        // ---- what DungeonRun says is on screen ---------------------------------------------------------

        /// <summary>After CorridorView.ShowArea for a hallway: which hallway it is and which of its rooms lies ahead (to the right).</summary>
        public static void ShowHallway(Exploration exploration, Hallway hallway, int targetRoomId)
        {
            CorridorView.Ensure().Props.Set(exploration, hallway, null, targetRoomId);
        }

        /// <summary>After CorridorView.ShowArea for a room.</summary>
        public static void ShowRoom(Exploration exploration, Room room)
        {
            CorridorView.Ensure().Props.Set(exploration, null, room, -1);
        }

        internal static void RaiseClicked(CorridorProp prop) => Clicked?.Invoke(prop);

        private void Set(Exploration exploration, Hallway hallway, Room room, int target)
        {
            // the area it stands in is not on screen yet (the old one is fading out): it comes up with it
            if (_view.AreaPending)
            {
                _held = true;
                _heldX = exploration;
                _heldHall = hallway;
                _heldRoom = room;
                _heldTarget = target;
                return;
            }
            _held = false;
            _x = exploration;
            _hall = hallway;
            _room = room;
            _target = target;
            Build();
        }

        /// <summary>The asked-for area is on screen now: what was said to stand in it is stood up.</summary>
        internal void ApplyPending()
        {
            if (!_held) return;
            _held = false;
            Set(_heldX, _heldHall, _heldRoom, _heldTarget);
        }

        /// <summary>Dev bridge: the area on screen is about to be shown again; what stands in it comes back with it.</summary>
        internal void Hold(Hallway hallway, Room room)
        {
            if (_x == null) return;
            _held = true;
            _heldX = _x;
            _heldHall = hallway;
            _heldRoom = room;
            _heldTarget = _target;
        }

        /// <summary>The view shows another area: its props follow when DungeonRun says which one it is.</summary>
        internal void Forget()
        {
            Release();
            _x = null;
            _hall = null;
            _room = null;
            _target = -1;
        }

        /// <summary>Destroys what is on screen (the description stays, for a rebuild).</summary>
        internal void Release()
        {
            foreach (var prop in _props) prop.Model?.Destroy();
            _props.Clear();
            _hovered = null;
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
            _root = null;
        }

        /// <summary>Frees the art of the dungeon that was left.</summary>
        internal static void ReleaseArt()
        {
            foreach (var atlas in Atlases) SpineAtlas.Unload(atlas);
            Atlases.Clear();
        }

        // ---- building --------------------------------------------------------------------------------

        internal void Build()
        {
            Release();
            if (_x == null || (_hall == null && _room == null)) return;
            EnsureRoot();
            Follow(true);
        }

        private void EnsureRoot()
        {
            if (_root != null) return;
            _root = new GameObject("Props").transform;
            _root.SetParent(_view.SceneRoot, false);
        }

        // DD1: tile i of a hallway is centred on 720 i
        private static float TileCentre(int tile) => tile * CorridorNumbers.TileWidth;

        private CorridorProp Add(PropSight sight)
        {
            var prop = new CorridorProp { Kind = sight.Kind, Id = sight.Id, Tile = sight.Tile, Segment = sight.Segment, Room = sight.Room };
            if (sight.Kind == PropKind.SecretDoor || sight.Kind == PropKind.Exit) _props.Add(prop);     // a place, not a thing
            else Stand(prop, _room != null, _room != null ? CorridorNumbers.RoomCamera.x : TileCentre(sight.Tile));
            return prop;
        }

        private void Stand(CorridorProp prop, bool room, float centre)
        {
            // DD1 (spec 4.5): the origin of the skeleton, the middle of its foot, on the floor at the kind's own depth
            float right, depth;
            switch (prop.Kind)
            {
                case PropKind.Curio:
                    right = room ? CorridorNumbers.CurioRoomX : CorridorNumbers.CurioX;
                    depth = CorridorNumbers.CurioZ;
                    break;
                case PropKind.Trap:
                    right = CorridorNumbers.TrapX;
                    depth = CorridorNumbers.TrapZ;
                    break;
                case PropKind.Obstacle:
                    // DD1's executable stands an obstacle at the curio's depth
                    right = CorridorNumbers.ObstacleX;
                    depth = CorridorNumbers.CurioZ;
                    break;
                default:
                    // a door: in the wall
                    right = 0f;
                    depth = CorridorNumbers.WallZ;
                    break;
            }
            prop.Foot = new Vector3(centre + right, Lift, depth);
            prop.UnitsPerPixel = Scale;

            switch (prop.Kind)
            {
                case PropKind.Curio: prop.Skeleton = PropArt.Curio(prop.Id); break;
                case PropKind.Trap: prop.Skeleton = PropArt.Trap(prop.Id); break;
                case PropKind.Obstacle: prop.Skeleton = PropArt.Obstacle(prop.Id); break;
                case PropKind.Door: prop.Skeleton = PropArt.Door(prop.Id); break;
            }
            if (prop.Skeleton == null) Plugin.Log.LogWarning("Corridor props: DD1 has no art for the " + prop.Kind.ToString().ToLowerInvariant() + " " + prop.Id);
            else
            {
                prop.Model = PropModel.Create(prop.Skeleton, _root, prop.Kind + " " + prop.Id + " " + prop.Tile, SortingOrder(prop));
                if (prop.Model != null)
                {
                    Atlases.Add(prop.Model.AtlasPath);
                    prop.Model.Root.localPosition = prop.Foot;
                    prop.Model.Root.localScale = new Vector3(prop.UnitsPerPixel, prop.UnitsPerPixel, 1f);
                }
            }
            _props.Add(prop);
        }

        // Far things first, as DD1's depths order them: doors in the wall, curios and obstacles, traps. A prop
        // may need a mesh per atlas texture; forty orders a tile leave room for the largest (26 slots).
        private static int SortingOrder(CorridorProp prop)
        {
            var layer = prop.Kind == PropKind.Door ? 0 : prop.Kind == PropKind.Curio ? 1000 : prop.Kind == PropKind.Obstacle ? 1500 : 2000;
            return layer + prop.Tile * 40 + (prop.Staged ? 20 : 0);
        }

        /// <summary>
        /// Dev bridge: stands any DD1 prop in a tile of the area on screen and holds or plays one of its
        /// animations, to look at art the generated dungeon does not happen to hold. Gone at the next rebuild.
        /// </summary>
        internal CorridorProp Stage(PropKind kind, string id, int tile, string animation, bool play)
        {
            EnsureRoot();
            var prop = new CorridorProp { Kind = kind, Id = id, Tile = tile, Staged = true };
            Stand(prop, _view.IsRoom, _view.IsRoom ? CorridorNumbers.RoomCamera.x : TileCentre(tile));
            Pose(prop, animation, play);
            return prop;
        }

        /// <summary>
        /// DD1 (seen in its frames: the door's art changes to its open picture in the frame after W): the door of a
        /// tile swings open as the party sets off into it, and stays so while the area is on screen.
        /// </summary>
        internal void OpenDoor(int tile)
        {
            foreach (var prop in _props)
            {
                if (prop.Kind != PropKind.Door || prop.Tile != tile || prop.Staged) continue;
                prop.InUse = true;
                Show(prop, ExplorationProps.Open, false);
            }
        }

        /// <summary>Dev bridge: a prop in an animation of the tester's choosing, until the rules change its state.</summary>
        internal void Pose(CorridorProp prop, string animation, bool play)
        {
            if (prop?.Model == null) return;
            prop.Model.Root.gameObject.SetActive(true);
            if (play) prop.Model.Play(animation, false);
            else prop.Model.Show(animation, false);
            if (prop.Staged) prop.State = prop.Model.Animation ?? "setup";
        }

        // ---- keeping up with the rules ---------------------------------------------------------------

        internal void Tick(float seconds)
        {
            if (_root == null) return;
            if (_x != null) Follow(false);
            foreach (var prop in _props)
            {
                if (prop.Model == null) continue;
                prop.Model.Tick(seconds);
                // DD1's light where the prop stands
                prop.Model.SetTint(CorridorLight.Ramp((prop.Foot.x - _view.CameraAt.x) * _view.CameraZoom));
            }
        }

        // Sets what is on screen to what the rules say stands there. A tile's contents can change (something
        // may move into a hallway walked before): what is gone is taken away, what is new is stood up.
        private void Follow(bool built)
        {
            if (_hall != null) ExplorationProps.InHallway(_x, _hall.Id, _target, _sights);
            else ExplorationProps.InRoom(_x, _room.Id, _sights);

            for (var i = _props.Count - 1; i >= 0; i--)
            {
                var prop = _props[i];
                if (prop.Staged || Seen(prop) >= 0) continue;
                prop.Model?.Destroy();
                _props.RemoveAt(i);
                if (_hovered == prop) _hovered = null;
            }
            foreach (var sight in _sights)
            {
                CorridorProp prop = null;
                foreach (var candidate in _props)
                    if (candidate.Is(sight)) prop = candidate;
                var fresh = prop == null;
                if (fresh) prop = Add(sight);
                // the pointer lights a curio up as standing at it does; a trap on the floor is not lit by it (none
                // was seen lit in DD1's own frames: it tells the heroes' chances on their trays instead)
                var state = sight.State == ExplorationProps.Idle && prop == _hovered && prop.Kind == PropKind.Curio ? ExplorationProps.Active : sight.State;
                if (prop.InUse) state = ExplorationProps.Open;
                Show(prop, state, built || fresh);
            }
        }

        private int Seen(CorridorProp prop)
        {
            for (var i = 0; i < _sights.Count; i++)
                if (prop.Is(_sights[i])) return i;
            return -1;
        }

        private static void Show(CorridorProp prop, string state, bool built)
        {
            if (state == prop.State && !built) return;
            prop.State = state;
            var model = prop.Model;
            if (model == null) return;
            model.Root.gameObject.SetActive(state != null);
            if (state == null) return;
            // States that are reached once and stay: a prop that is built in one shows its last moment, one
            // that gets there in front of the player plays it (spikes shoot up, rubble falls apart).
            var once = ExplorationProps.PlaysOnce(state);
            if (once && built) model.Show(state, true);
            else model.Play(state, !once);
        }

        // ---- the pointer -----------------------------------------------------------------------------

        internal void Hover(CorridorProp prop)
        {
            // the dev bridge's pointer stays where it was put, while that prop stands
            if (DevHovered != null && _props.Contains(DevHovered)) prop = DevHovered;
            else DevHovered = null;
            _hovered = prop != null && (prop.Kind == PropKind.Curio || prop.Kind == PropKind.Trap) ? prop : null;
        }

        /// <summary>Dev bridge: as if the pointer rested on this prop (corridor.hover); null: the real pointer.</summary>
        internal static CorridorProp DevHovered;

        /// <summary>A trap of this hallway that the party has spotted and that has neither gone off nor been disarmed.</summary>
        internal bool Armed(CorridorProp prop)
        {
            return prop != null && prop.Kind == PropKind.Trap && !prop.Staged && _x != null && _hall != null && prop.Segment >= 0 && prop.Segment < _hall.Segments.Count
                   && _x.IsSegmentKnown(_hall.Id, prop.Segment) && !_x.IsSegmentDone(_hall.Id, prop.Segment);
        }

        /// <summary>
        /// The pointer rests on a trap that can still be tried: DD1 then shows every hero's chance to disarm it
        /// over the hero's health bar (overlays/tray_trap_disarm.png).
        /// </summary>
        public bool TrapUnderPointer => Armed(_hovered);

        /// <summary>The prop under a ray of the camera; null over bare wall. Nearer things first.</summary>
        internal CorridorProp Pick(Ray ray)
        {
            if (_root == null) return null;
            // into the scene's units; the root is only scaled and moved, so the direction keeps its axes
            var origin = _root.InverseTransformPoint(ray.origin);
            var direction = _root.InverseTransformDirection(ray.direction);
            if (Mathf.Abs(direction.z) < 1e-6f) return null;
            CorridorProp best = null;
            foreach (var prop in _props)
            {
                if (prop.Model == null || prop.State == null || prop.State == ExplorationProps.Cleared) continue;
                var at = origin + direction * ((prop.Foot.z - origin.z) / direction.z);
                var point = new Vector2((at.x - prop.Foot.x) / prop.UnitsPerPixel, (at.y - prop.Foot.y) / prop.UnitsPerPixel);
                if (!prop.Model.Hit(point)) continue;
                if (best == null || SortingOrder(prop) > SortingOrder(best)) best = prop;
            }
            if (best != null) return best;

            // on the wall itself: a known hidden door takes its whole tile, the way out the side the party came in by
            var wall = origin + direction * ((CorridorNumbers.WallZ - origin.z) / direction.z);
            if (wall.y < 0f || wall.y > _view.WallTop) return null;
            var half = CorridorNumbers.TileWidth * 0.5f;
            foreach (var prop in _props)
            {
                if (prop.State == null) continue;
                if (prop.Kind == PropKind.SecretDoor && wall.x >= TileCentre(prop.Tile) - half && wall.x < TileCentre(prop.Tile) + half) return prop;
                if (prop.Kind == PropKind.Exit && wall.x >= _view.RoomLeft && wall.x < _view.RoomLeft + _view.RoomWidth * ExitShare) return prop;
            }
            return null;
        }

        /// <summary>Where the leader stands to turn to a prop; a door (also a hidden one) is walked up to.</summary>
        internal float StandX(CorridorProp prop)
        {
            if (prop.Kind == PropKind.Door || prop.Kind == PropKind.SecretDoor) return TileCentre(prop.Tile);
            return prop.Foot.x - StandOff;
        }

        /// <summary>The first prop of a kind in a tile (dev bridge); tile -1: anywhere.</summary>
        internal CorridorProp Find(PropKind kind, int tile = -1)
        {
            foreach (var prop in _props)
                if (prop.Kind == kind && (tile < 0 || prop.Tile == tile)) return prop;
            return null;
        }
    }

    /// <summary>
    /// DD1's own words for what stands in a dungeon: <c>localization/curios.string_table.xml</c> names every
    /// curio (<c>str_curio_title_*</c>, <c>str_curio_content_*</c>), the main table names the obstacles. The id
    /// is the prop's "UI String Name" of <c>curios/curio_props.csv</c>. Null when DD1 has no such text.
    /// </summary>
    internal static class PropText
    {
        private static Dictionary<string, string> _curios;
        private static CurioCatalog _catalog;

        private static string Curio(string prefix, string propId)
        {
            if (string.IsNullOrEmpty(propId)) return null;
            if (_curios == null)
            {
                _curios = new Dictionary<string, string>();
                var file = Dd1Install.PathOf("localization/curios.string_table.xml");
                try
                {
                    if (file != null && File.Exists(file)) Dd1Strings.ReadEnglish(File.ReadLines(file), _curios, null);
                }
                catch (Exception e) { Plugin.Log.LogWarning("DD1 curio texts could not be read: " + e.Message); }
                _catalog = CurioCatalog.Load(new Dd1Files());
            }
            var name = _catalog.Prop(propId)?.UiString;
            if (string.IsNullOrEmpty(name)) name = propId;
            return _curios.TryGetValue(prefix + name, out var text) || _curios.TryGetValue(prefix + propId, out text) ? Dd1Strings.Plain(text) : null;
        }

        /// <summary>"Ancient Artifact" for the secret stash, "Heirloom Chest"...</summary>
        public static string CurioTitle(string propId) => Curio("str_curio_title_", propId);

        /// <summary>DD1's line under the title: what the party sees.</summary>
        public static string CurioContent(string propId) => Curio("str_curio_content_", propId);

        /// <summary>What DD1 tells the pointer on the hand ("Open the chest..."). The ids of two curios' lines lack the "str_" the others start with.</summary>
        public static string CurioInvestigate(string propId) => Curio("str_curio_tooltip_investigate_", propId) ?? Curio("curio_tooltip_investigate_", propId);

        public static string ObstacleTitle(string propId) => Dd1Strings.Plain(Dd1Strings.Get("str_obstacle_" + propId + "_title")).NullIfEmpty();

        public static string ObstacleContent(string propId) => Dd1Strings.Plain(Dd1Strings.Get("str_obstacle_" + propId + "_description")).NullIfEmpty();

        private static string NullIfEmpty(this string text) => string.IsNullOrEmpty(text) ? null : text;
    }
}
