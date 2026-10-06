using System;
using System.Collections.Generic;
using Assets.Code.Actor;
using Assets.Code.Game;
using Assets.Code.Rendering.MaterialPropertyBlocks;
using Assets.Code.Roster;
using Assets.Code.Utils;
using Cinemachine;
using DD2Estate.Core;
using DD2Estate.Dd1;
using DD2Estate.Dd2;
using DD2Estate.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// The dungeon view, built and filmed the way DD1 does it (docs/recon/dd1-corridor-rendering.md): a scene
    /// in DD1's units (<see cref="CorridorScene"/>: wall, floor plane, far layers, foreground strips), DD1's
    /// perspective camera (75 degrees across a 1920x720 view, 1400 in front of the party, 80 ahead of the
    /// leader, magnified 1.25; a fixed camera in a room), the party's DD2 hero models on the floor at z = 0,
    /// 154 apart, and DD1's walking (shared/rules.json). The dungeon view is the top two thirds of the screen,
    /// as in DD1; the panel has the rest. What the rules put in the hallway is drawn by <see cref="CorridorProps"/>.
    /// All positions here are in DD1 units unless said otherwise.
    /// </summary>
    internal partial class CorridorView : MonoBehaviour
    {
        public static CorridorView Instance { get; private set; }

        // Tunables of the mod's own, static so the dev bridge can set them (then corridor.rebuild).
        /// <summary>On top of the models' own size (CorridorNumbers.UnitScale is chosen so that 1 is DD1's hero height).</summary>
        public static float HeroScale = 1f;
        /// <summary>World units between two ranks in depth, so that overlapping models do not fight for it. DD1: all on z = 0.</summary>
        public static float HeroDepthStep = 0.02f;
        /// <summary>The dungeon view's share of the screen's height, from the top (DD1: 720 of 1080).</summary>
        public static float StripShare = 2f / 3f;
        /// <summary>The main camera draws into the view's rectangle only, so its axis is the view's middle as DD1's is.
        /// Off: the camera keeps the whole screen (wrong framing; to tell a fault of the rectangle from another).</summary>
        public static bool UseViewRect = true;
        /// <summary>Dev bridge: hold the walking-back camera at this blend (0..1); below 0: as the party walks.</summary>
        public static float BackOverride = -1f;
        /// <summary>On top of DD1's walking speeds.</summary>
        public static float WalkSpeedScale = 1f;
        // DD2's character shader ("Red Hook/Lit/Hero") takes its look from material properties every scene of the
        // game sets for its characters (the camp scene: _Brightness 0.8, _ShadowColour a grey blue;
        // ApplyMaterialPropertyOverrideBhv); real lights do next to nothing to it. Outside such a scene nothing
        // sets them and the models stand almost black. The corridor gives each hero its _Brightness from DD1's
        // character light (CorridorLight.Hero), and all of them a shadow colour and a saturation of the mod's own.
        public static Color HeroShadow = new Color(0.36f, 0.42f, 0.5f, 0f);
        /// <summary>The models' _Saturation; below 0: left as the game has it.</summary>
        public static float HeroSaturation = -1f;
        /// <summary>Share of their light the heroes who only look on keep while one of them investigates (the mod's own: see LightHeroes).</summary>
        public static float BystanderLight = 0.45f;
        private const uint HeroLookPriority = 4u;      // what the game's scenes use for theirs
        /// <summary>
        /// DD2's heroes have no walk cycle (they ride a coach). What stands in for it while the party moves:
        /// 0 nothing: the models glide in their idle pose;
        /// 1 a staggered bob;
        /// 2 the bob with a sway from foot to foot and a lean into the walk (the mod's proposal);
        /// 3 the game's own step for a change of rank: the animator's move_forward / move_backward when the party
        ///   sets off and move_complete when it stops, with the bob;
        /// 4 the legs walk (<see cref="HeroGait"/>): a step cycle laid over the model's pose, feet planted.
        /// </summary>
        public static int WalkStyle = 4;
        public static float BobHeight = 0.045f;        // world units
        public static float BobRate = 9f;
        public static float SwayDegrees = 2.5f;
        public static float LeanDegrees = 3f;
        /// <summary>The models are held in the rotation and scale they were made with, whatever turns them (DD1's
        /// heroes back away facing forward). Off: left to the game.</summary>
        public static bool KeepFacing = true;
        /// <summary>The shader's vertical gradient on the models, for DD1's darker feet (its light falls off below the
        /// box: up to 28 % at the floor). Off until looked at; a value below 0 leaves the material's own.</summary>
        public static bool HeroGradient = false;
        public static float HeroGradientOpacity = 0.3f, HeroGradientOffset = -1f, HeroGradientFalloff = -1f, HeroGradientCeiling = -1f, HeroGradientSpread = -1f;
        /// <summary>DD1: "click ahead or behind the party to move them" (held, as DD1's is).</summary>
        public static bool MouseWalk = true;

        /// <summary>Raised when the party steps onto another tile of a hallway (tile index). DD1 counts the tile from
        /// the party's position, a point <see cref="CorridorNumbers.LeaderAhead"/> behind the leader.</summary>
        public event Action<int> TileEntered;

        public bool InputEnabled = true;

        /// <summary>No movement at all (a fight is about to start).</summary>
        public bool Frozen;

        private readonly CorridorScene _scene = new CorridorScene();
        private Transform _party;
        private CinemachineVirtualCamera _vcam;
        private readonly List<ActorBhv> _actors = new List<ActorBhv>();
        private readonly List<int> _actorRank = new List<int>();        // the slot each model stands in
        private readonly List<uint> _slotGuids = new List<uint>();      // the hero of each slot
        private readonly List<HeroLook> _looks = new List<HeroLook>();
        private readonly List<MaterialPropertyOverride> _extras = new List<MaterialPropertyOverride>();
        private string _extrasFor;
        private float _nextLookCheck;
        private bool _lookFailed;
        private readonly List<Quaternion> _actorRotation = new List<Quaternion>();
        private readonly List<Vector3> _actorScale = new List<Vector3>();
        private int _walkAnimation;        // style 3: +1 the forward step is playing, -1 the backward one
        private UnityEngine.UI.Image _curtain;
        private float _curtainAlpha = -1f;

        /// <summary>What one hero's model has been given.</summary>
        private class HeroLook
        {
            public MaterialPropertyOverride Brightness;
            public float Value = float.NaN;
            public readonly List<MaterialPropertyBhv> Holders = new List<MaterialPropertyBhv>();
            public readonly HashSet<MaterialPropertyBhv> Lit = new HashSet<MaterialPropertyBhv>();
            public readonly HashSet<MaterialPropertyBhv> Extra = new HashSet<MaterialPropertyBhv>();
        }

        /// <summary>DD1's LightScalar per rank (1 = lit): a hero walking into a door darkens (stage 4).</summary>
        public readonly float[] HeroLightScalar = { 1f, 1f, 1f, 1f, 1f, 1f };
        private readonly List<Transform> _slots = new List<Transform>();
        private readonly List<string> _tiles = new List<string>();
        private string _dungeon = "crypts";
        private float _leadX;          // the leader's x
        private float _spacing = CorridorNumbers.ActorSpacing;     // from hero to hero: drawn out or closed up by the walk
        /// <summary>How far apart the heroes stand now (DD1 units).</summary>
        public float Spacing => _spacing;
        private float _speed;          // units a second, signed
        private float _back;           // DD1's walking-back counter, 0..BackTime seconds
        private float _devWalk;        // seconds of walking requested by the dev bridge
        private float _devDirection;
        private int _currentTile = -1;
        private bool _isRoom;
        private float _walkPhase;
        private GameObject _characterCamera;
        private CorridorProps _props;
        // a walk the player asked for by clicking a prop: where the leader goes, and what is told on arrival
        private float? _walkTo;
        private CorridorProp _walkProp;
        private Canvas _pointerCanvas;
        private bool _pointerInside, _pointerHeld;
        private Camera _rectCamera;
        private Rect _rectBefore;

        /// <summary>The leader's x.</summary>
        public float LeadX => _leadX;
        /// <summary>The area's length: a hallway's from the first door tile's middle to the last one's, a room's 1920.</summary>
        public float Length => _isRoom ? 1920f : Mathf.Max(0, _tiles.Count - 1) * CorridorNumbers.TileWidth;
        public int CurrentTile => _currentTile;
        public bool IsRoom => _isRoom;
        public float Speed => _speed;
        /// <summary>0 walking on .. 1 the walking-back camera.</summary>
        public float BackBlend { get; private set; }

        /// <summary>The camera, DD1 units, and how much its picture is magnified.</summary>
        public Vector3 CameraAt { get; private set; }
        public float CameraZoom { get; private set; } = 1f;

        /// <summary>The scene's root: one of its units is one DD1 unit.</summary>
        public Transform SceneRoot => _scene.Root;
        internal CorridorScene Scene => _scene;
        public float RoomLeft => _scene.RoomLeft;
        public float RoomWidth => _scene.RoomWidth;
        /// <summary>How high the wall stands (a room's picture is drawn larger).</summary>
        public float WallTop => _isRoom && _scene.RoomWidth > 0f ? CorridorNumbers.WallTop * _scene.RoomWidth / 1920f : CorridorNumbers.WallTop;

        /// <summary>What stands in the area on screen.</summary>
        public CorridorProps Props => _props ?? (_props = new CorridorProps(this));

        public static CorridorView Ensure()
        {
            if (Instance == null)
            {
                var go = new GameObject("DD2Estate.Corridor");
                if (EstateSession.Scene.IsValid()) SceneManager.MoveGameObjectToScene(go, EstateSession.Scene);
                Instance = go.AddComponent<CorridorView>();
            }
            Instance.gameObject.SetActive(true);
            return Instance;
        }

        /// <summary>The expedition is over: the view goes away and the dungeon's prop art is let go.</summary>
        public static void Hide()
        {
            if (Instance != null)
            {
                // nothing of an expedition stays: its models, its scene's meshes, its props and their atlases, the
                // grade's tables and both volumes; switching the view off gives the main camera its rectangle back and
                // takes the mod's overlay camera out of the stack (OnDisable). The canvas and the virtual camera are
                // the view's own and go with it.
                Instance.CancelTransition();
                Instance.ResetScript();
                Instance.StopWalk();
                Instance.Props.Forget();
                Instance.ClearParty();
                Instance._scene.Destroy();
                Instance._tiles.Clear();
                Instance.gameObject.SetActive(false);
            }
            CorridorProps.ReleaseArt();
            CorridorPost.ReleaseAll();
        }

        /// <summary>Shows a hallway or a room. <paramref name="tiles"/> are DD1 art names without dungeon
        /// prefix and extension, left to right: "corridor_door.basic", "corridor_wall.03" (720 px each) for a
        /// hallway, one "room_wall.library" (1920 px) for a room; a name with a folder in it is a file of the
        /// DD1 install as it is ("dungeons/_shared/secretroom"). The party stands where DD1 stands it: in a room
        /// with its position at area.room_position, in a hallway with the leader in the middle of the first door
        /// tile, or with its position in the middle of <paramref name="startTile"/>.
        /// What stands in the area follows through <see cref="CorridorProps.ShowHallway"/> / ShowRoom.</summary>
        public void ShowArea(string dd1Dungeon, IList<string> tiles, bool isRoom, int startTile = 0)
        {
            CorridorNumbers.Load();
            var onScreen = isActiveAndEnabled && _tiles.Count > 0 && _scene.Root != null;
            // a hallway after a hallway is the same one laid out the other way round (the map's "face the chosen
            // end"): the party has gone nowhere, so nothing fades
            var turned = onScreen && !_isRoom && !isRoom && _pending == null;
            if (onScreen && TransitionsOn && !turned)
            {
                // DD1: the old area fades out first (the party walking into the door if it leaves by one)
                BeginTransition(new AreaRequest { Dungeon = dd1Dungeon, Tiles = new List<string>(tiles), IsRoom = isRoom, StartTile = startTile });
                return;
            }
            _pending = null;
            Show(dd1Dungeon, tiles, isRoom, startTile);
            if (turned) CancelTransition();
            else BeginFadeIn();
        }

        private void Show(string dd1Dungeon, IList<string> tiles, bool isRoom, int startTile)
        {
            _dungeon = dd1Dungeon;
            _isRoom = isRoom;
            // DD1 parties do not walk about inside a room: the next move is chosen on the map.
            InputEnabled = !isRoom;
            _tiles.Clear();
            _tiles.AddRange(tiles);
            StopWalk();
            Props.Forget();
            Barrier = float.MaxValue;
            _speed = 0f;
            _back = 0f;
            _spacing = CorridorNumbers.ActorSpacing;        // a party arrives standing as the file has it
            _leadX = isRoom ? CorridorNumbers.RoomPosition + CorridorNumbers.LeaderAhead : StandIn(startTile);
            Build(true);
            _currentTile = isRoom ? 0 : TileOf(_leadX);
        }

        // DD1 [exe 0x9ae9a0, 0xaf3a59]: the party's position is put on the tile's middle (the leader 180 ahead of it)
        // and the party is then moved inside its limits. On entering a hallway that leaves the leader 180 beyond
        // the first door tile's middle, or 282 with a party of four, whose last hero stands at the limit.
        private float StandIn(int tile)
        {
            return Mathf.Clamp(Mathf.Max(0, tile) * CorridorNumbers.TileWidth + CorridorNumbers.LeaderAhead, MinLeadX, MaxLeadX);
        }

        /// <summary>Puts the party back in the middle of a hallway tile without raising TileEntered.</summary>
        public void PlaceInTile(int tile)
        {
            FinishTransition();
            _devWalk = 0f;
            _speed = 0f;
            StopWalk();
            SetLeadX(StandIn(tile), notify: false);
            _currentTile = tile;
        }

        /// <summary>Dev bridge: the leader at an x of the tester's choosing, no tile entered.</summary>
        public void PlaceAt(float leadX)
        {
            FinishTransition();
            _devWalk = 0f;
            _speed = 0f;
            StopWalk();
            SetLeadX(_isRoom ? leadX : Mathf.Clamp(leadX, MinLeadX, MaxLeadX), notify: false);
        }

        /// <summary>Removes the hero models (a fight spawns its own for the same heroes).</summary>
        public static void Suspend()
        {
            if (Instance == null) return;
            // a fight does not wait for a fade: the area asked for is put up at once
            Instance.FinishTransition();
            Instance.ResetScript();
            Instance._devWalk = 0f;
            Instance.StopWalk();
            Instance.ClearParty();
            Instance.gameObject.SetActive(false);
        }

        public static void Resume()
        {
            if (Instance == null) return;
            Instance.gameObject.SetActive(true);
            if (Instance._actors.Count == 0)
            {
                Instance.BuildParty();
                Instance.SetLeadX(Instance._leadX, notify: false);
            }
        }

        /// <summary>Everything anew, the hero models too (dev bridge: after a number was changed).</summary>
        public void Rebuild() => Build(false);

        // An area is built. The hero models stay from one area to the next when the party is the same: making
        // them is the dear part, and they keep the look they were given.
        private void Build(bool keepParty)
        {
            ResetScript();
            Props.Release();
            _scene.Destroy();
            if (!keepParty || PartyChanged()) ClearParty();
            _scene.Build(transform, ArtFile, _tiles, _isRoom);
            if (_vcam == null) BuildCamera();
            if (_party == null) BuildParty();
            Props.Build();
            _currentTile = -1;
            SetLeadX(_isRoom ? _leadX : Mathf.Clamp(_leadX, MinLeadX, MaxLeadX), notify: false);
            _currentTile = _isRoom ? 0 : TileOf(_leadX);
        }

        // Somebody died, joined or changed rank, or a model was taken away: the party is made anew.
        private bool PartyChanged()
        {
            if (_party == null) return true;
            foreach (var actor in _actors)
                if (actor == null) return true;
            var library = SingletonMonoBehaviour<Assets.Code.Library.Library<uint, ActorInstance>>.Instance;
            var guids = Standing();
            var living = 0;
            for (var i = 0; i < guids.Count; i++)
            {
                var hero = library.GetLibraryElement(guids[i]);
                if (hero == null || !hero.IsLiving) continue;
                if (living >= _slotGuids.Count || _slotGuids[living] != guids[i]) return true;
                living++;
            }
            return living != _slotGuids.Count || _actors.Count != _slotGuids.Count;
        }

        private void ClearParty()
        {
            foreach (var actor in _actors)
                if (actor != null) Destroy(actor.gameObject);
            _actors.Clear();
            EndRankMoves(true);
            _slotGuids.Clear();
            _actorRank.Clear();
            _actorRotation.Clear();
            _actorScale.Clear();
            _gaits.Clear();
            _walkAnimation = 0;
            _looks.Clear();
            _slots.Clear();
            if (_party != null) Destroy(_party.gameObject);
            _party = null;
        }

        // A tile name with a folder in it is a path of its own: the secret room is shared by all dungeons.
        private string ArtFile(string name)
        {
            return name.IndexOf('/') >= 0 ? name + ".png" : "dungeons/" + _dungeon + "/" + _dungeon + "." + name + ".png";
        }

        // ---- camera ----------------------------------------------------------------------------------

        private void BuildCamera()
        {
            var go = new GameObject("CorridorCamera");
            go.transform.SetParent(transform, false);
            _vcam = go.AddComponent<CinemachineVirtualCamera>();
            _vcam.m_Lens.NearClipPlane = 0.1f;
            _vcam.m_Lens.FarClipPlane = 200f;
            _vcam.Priority = 1000;
        }

        // DD1 (spec 3): a room's camera is fixed; a hallway's stands off the leader by OffsetFromPartyLeader and
        // goes to the walking-back offset and zoom as the party backs up.
        private void PlaceCamera()
        {
            if (_isRoom)
            {
                CameraAt = CorridorNumbers.RoomCamera;
                CameraZoom = 1f;
            }
            else
            {
                var s = BackBlend;
                var offset = Vector3.Lerp(CorridorNumbers.Offset, CorridorNumbers.BackOffset, s);
                CameraAt = DoorCamera(new Vector3(CameraLead() + offset.x, offset.y, offset.z));
                CameraZoom = Mathf.Lerp(CorridorNumbers.Zoom, CorridorNumbers.ZoomBack, s);
            }
            // the timescripts (spec 9): their zoom rests at 1 and is added, their focus pulls the camera to a point
            CameraZoom += Script("camera.zoom") - 1f;
            CameraAt = new Vector3(
                Mathf.Lerp(CameraAt.x, Script("camera.absolute_focus,x"), Mathf.Clamp01(Script("camera.absolute_focus_blend,x"))),
                Mathf.Lerp(CameraAt.y, Script("camera.absolute_focus,y"), Mathf.Clamp01(Script("camera.absolute_focus_blend,y"))),
                CameraAt.z);
            if (_vcam == null || _scene.Root == null) return;
            // the steady cam (spec 3.5): the point looked at wanders by the drift's offset, the camera itself goes the
            // other way by the scalar's excess, so the picture turns a little rather than slides; a script's shake
            // moves the whole camera, in pixels of DD1's view
            var drift = _driftOffset * CorridorScript.DriftStrength;
            var shake = Shudder() / Mathf.Max(0.05f, CameraZoom * CorridorNumbers.Focal / Mathf.Max(1f, -CameraAt.z));
            var lookAt = new Vector3(CameraAt.x + drift.x, CameraAt.y + drift.y, drift.z);
            var from = new Vector3(CameraAt.x - (CorridorScript.PositionScalar - 1f) * drift.x + shake.x, CameraAt.y - (CorridorScript.PositionScalar - 1f) * drift.y + shake.y, CameraAt.z + drift.z);
            lookAt += new Vector3(shake.x, shake.y, 0f);
            _vcam.transform.position = _scene.Root.TransformPoint(from);
            _vcam.transform.rotation = _scene.Root.rotation * Quaternion.LookRotation(lookAt - from, Vector3.up) * Quaternion.Euler(0f, 0f, _driftRoll * CorridorScript.DriftStrength);
            // DD1 magnifies the picture about its middle: a longer lens
            _vcam.m_Lens.FieldOfView = CorridorNumbers.VerticalFov(CameraZoom);
        }

        /// <summary>
        /// DD1's camera does not follow the party into a hallway's ends. Measured in the running game (2026-10-05,
        /// tools/dd1_match.py on the tutorial's hallway, step by step): the camera stands at x 440 while the leader
        /// is anywhere between the first door tile's middle and x 360, follows at leader + 80 from there, and
        /// stops at 280 short of the last door tile's middle while the leader walks on to the door. What it follows
        /// is the leader held between half a tile inside each door tile's middle. So a door tile is never in the
        /// middle of the screen, the party walks out to the door on its own, and what lies beyond the door tile is
        /// only ever seen at the screen's edge, where the light is at its darkest. Off: the camera follows the
        /// leader everywhere (the mod before this).
        /// </summary>
        public static bool ClampCamera = true;

        // The leader's x as the camera follows it.
        private float CameraLead()
        {
            if (!ClampCamera) return _leadX;
            var half = CorridorNumbers.TileWidth * 0.5f;
            var last = LastDoorX;
            return last - half <= half ? last * 0.5f : Mathf.Clamp(_leadX, half, last - half);
        }

        // DD1's camera has its axis in the middle of the dungeon view, a third of the screen above the panel.
        // The main camera is given the view's rectangle while the corridor is shown (the game's overlay cameras
        // draw into their base camera's rectangle); a black panel under the HUD covers the rest of the screen.
        private void KeepViewRect(bool on)
        {
            var main = on ? Camera.main : null;
            if (_rectCamera != null && (_rectCamera != main || !UseViewRect))
            {
                _rectCamera.rect = _rectBefore;
                _rectCamera = null;
            }
            if (main == null || !UseViewRect)
            {
                KeepStackRects(null);
                return;
            }
            var rect = new Rect(0f, 1f - StripShare, 1f, StripShare);
            if (_rectCamera == null)
            {
                _rectBefore = main.rect;
                _rectCamera = main;
            }
            if (main.rect != rect) main.rect = rect;
            // the cameras stacked on it (the game's character camera) look for what they draw in the same picture
            // (CorridorCulling.cs)
            KeepStackRects(main);
        }

        /// <summary>The width of the dungeon view in DD1's pixels (it is 720 of them high).</summary>
        public float ViewPixels
        {
            get
            {
                var main = Camera.main;
                return main != null ? 720f * main.aspect : 1920f;
            }
        }

        // DD1: the leader stands in the middle of the first door tile at the least, of the last one at the most
        // (the mod walks the party into the next room when it enters that tile).
        // DD1 [exe 0xaea9b0; the running game: a party of two backs to -38, the leader of any party walks on to
        // the last door tile's middle and some 45 past it as it stops]: the party may back until its last hero is
        // a quarter tile before the first door tile's middle, and walk on until its leader is at the last one's.
        // So the whole party is in sight at a hallway's start, with the camera where DD1 holds it.
        private float MinLeadX => Mathf.Min(MaxLeadX, -CorridorNumbers.LeaderAhead + Mathf.Max(0, PartySize() - 1) * _spacing);
        private float LastDoorX => Mathf.Max(0f, (_tiles.Count - 1) * CorridorNumbers.TileWidth);
        // DD1: the leader walks on a little past the last door's middle and stops there; the door is not walked through
        private float MaxLeadX => LastDoorX + (_tiles.Count > 1 ? CorridorNumbers.DoorOvershoot : 0f);

        // The heroes that walk (known before their models are made).
        private int PartySize()
        {
            if (_slots.Count > 0) return _slots.Count;
            var library = SingletonMonoBehaviour<Assets.Code.Library.Library<uint, ActorInstance>>.Instance;
            var living = 0;
            foreach (var guid in Standing())
            {
                var hero = library != null ? library.GetLibraryElement(guid) : null;
                if (hero != null && hero.IsLiving) living++;
            }
            return living;
        }

        /// <summary>DD1: floor((party x + 360) / 720), the party's position being LeaderAhead behind the leader.</summary>
        public int TileOf(float leadX)
        {
            var tile = Mathf.FloorToInt((leadX - CorridorNumbers.LeaderAhead + CorridorNumbers.TileWidth * 0.5f) / CorridorNumbers.TileWidth);
            return Mathf.Clamp(tile, 0, Mathf.Max(0, _tiles.Count - 1));
        }

        private void SetLeadX(float x, bool notify)
        {
            _leadX = x;
            PlaceParty();
            PlaceCamera();

            var tile = _isRoom ? 0 : TileOf(x);
            if (tile != _currentTile)
            {
                _currentTile = tile;
                if (notify) TileEntered?.Invoke(tile);
            }
        }

        // ---- party -----------------------------------------------------------------------------------

        private void BuildParty()
        {
            _party = new GameObject("Party").transform;
            _party.SetParent(transform, false);

            var creator = SingletonMonoBehaviour<ActorCreateGameObjectBhv>.Instance;
            var layer = LayerMask.NameToLayer("Characters");
            var guids = Standing();
            var library = SingletonMonoBehaviour<Assets.Code.Library.Library<uint, ActorInstance>>.Instance;
            for (var i = 0; i < guids.Count; i++)
            {
                var hero = library.GetLibraryElement(guids[i]);
                if (hero == null || !hero.IsLiving) continue;
                var slot = new GameObject("Slot" + i).transform;
                slot.SetParent(_party, false);
                _slots.Add(slot);
                _slotGuids.Add(guids[i]);
                var actor = creator.CreateActorGameObject(guids[i], slot, layer, "idle_neutral", false);
                if (actor == null) continue;
                _actors.Add(actor);
                _gaits.Add(new HeroGait(hero.ActorDataId));
                _actorRotation.Add(actor.transform.localRotation);
                actor.transform.localRotation = Quaternion.Euler(0f, HeroYaw, 0f) * actor.transform.localRotation;
                _actorScale.Add(actor.transform.localScale);
                _actorRank.Add(_slots.Count - 1);
                _looks.Add(new HeroLook());
            }
            PlaceParty();
        }

        /// <summary>
        /// The party's order has changed (a hero was moved to another rank): the models take the places of their
        /// new ranks. They are the same models: only who stands in which slot changes. When the party itself is
        /// not the one on screen (somebody died meanwhile), it is made anew.
        /// </summary>
        public void Reorder()
        {
            if (_party == null || _scene.Root == null) return;
            var library = SingletonMonoBehaviour<Assets.Code.Library.Library<uint, ActorInstance>>.Instance;
            var order = new List<uint>();
            foreach (var guid in Standing())
            {
                var hero = library.GetLibraryElement(guid);
                if (hero != null && hero.IsLiving) order.Add(guid);
            }
            var same = order.Count == _slotGuids.Count && _actors.Count == _slotGuids.Count;
            for (var i = 0; same && i < order.Count; i++) same = _slotGuids.Contains(order[i]);
            for (var i = 0; same && i < _actors.Count; i++) same = _actors[i] != null && order.Contains(_actors[i].GetActorGuid());
            if (!same)
            {
                Recast();
                SetLeadX(_leadX, notify: false);
                return;
            }
            ResetScript();
            var from = RankPlaces();
            for (var i = 0; i < _actors.Count; i++)
            {
                var rank = order.IndexOf(_actors[i].GetActorGuid());
                _actorRank[i] = rank;
                _actors[i].transform.SetParent(_slots[rank], false);
            }
            for (var i = 0; i < order.Count; i++) _slotGuids[i] = order[i];
            // nobody is put in the new place at once: each goes there from where they stood (CorridorRanks.cs)
            BeginRankMoves(from);
            PlaceParty();
        }

        // DD1: hero i stands at leader x - i * actor_spacing on the floor at z = 0, facing right. The walk's
        // stand-in (WalkStyle) is laid over that.
        private void PlaceParty()
        {
            if (_scene.Root == null) return;
            var walking = _walkPhase > 0.01f;
            var facing = _speed < 0f ? -1f : 1f;
            for (var i = 0; i < _slots.Count; i++)
            {
                if (_slots[i] == null) continue;
                var local = HeroAt(i, out var gone);
                local.x += RankShift(i);        // a hero on the way to this rank
                var size = HeroScale;
                if (i == _scriptActor)
                {
                    // the acting hero stands where the script puts it, as large as DD1's presentation camera shows it
                    var k = PresentationScale(0f);
                    local = new Vector3(CameraAt.x + (Script("actor.area_pos,x") - CameraAt.x) * k, CameraAt.y * (1f - k), local.z);
                    size *= k;
                }
                var at = _scene.Root.TransformPoint(local);
                var phase = _walkPhase + i * 0.9f;
                var bob = walking && WalkStyle >= 1 && !Stepping ? Mathf.Abs(Mathf.Sin(phase)) * BobHeight : 0f;
                var tilt = walking && WalkStyle == 2 ? Mathf.Sin(phase) * SwayDegrees - LeanDegrees * facing * Mathf.Clamp01(Mathf.Abs(_speed) / Mathf.Max(1f, CorridorNumbers.MaxForwardSpeed) + (_phase == Phase.WalkIn ? 1f : 0f)) : 0f;
                _slots[i].position = at + new Vector3(0f, bob, i * HeroDepthStep);
                _slots[i].rotation = _scene.Root.rotation * Quaternion.Euler(0f, 0f, tilt);
                // a hero that has walked into the door is gone until the next area is up
                _slots[i].localScale = gone ? Vector3.zero : Vector3.one * size;
            }
            if (!KeepFacing) return;
            for (var i = 0; i < _actors.Count; i++)
            {
                if (_actors[i] == null) continue;
                var actor = _actors[i].transform;
                var turned = Quaternion.Euler(0f, HeroYaw, 0f) * _actorRotation[i];
                if (actor.localRotation != turned) actor.localRotation = turned;
                if (actor.localScale != _actorScale[i]) actor.localScale = _actorScale[i];
            }
        }

        // Style 3: the game's own step animation, set off when the party starts to move and ended when it stops.
        private void WalkAnimation(bool moving, float direction)
        {
            var wanted = moving && WalkStyle == 3 ? (direction < 0f ? -1 : 1) : 0;
            if (wanted == _walkAnimation) return;
            try
            {
                foreach (var actor in _actors)
                {
                    if (actor == null) continue;
                    if (_walkAnimation != 0) actor.AttemptAnimatorTrigger("move_complete");
                    if (wanted != 0) actor.AttemptAnimatorTrigger(wanted > 0 ? "move_forward" : "move_backward");
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Corridor: the game's move animation could not be played: " + e.Message);
                WalkStyle = 2;
            }
            _walkAnimation = wanted;
        }

        // DD1's character light, one value per hero (spec 5.3). Models load after their actor is created and may
        // be rebuilt (a class change), so the look is given to whatever material holders are there now and to new
        // ones as they appear.
        private void LightHeroes()
        {
            if (_lookFailed) return;
            var look4 = Time.unscaledTime >= _nextLookCheck;
            if (look4) _nextLookCheck = Time.unscaledTime + 0.5f;
            try
            {
                // what every model gets besides its light: the shadow colour, a saturation, the vertical gradient
                var wanted = HeroShadow + "|" + HeroSaturation + "|" + (HeroGradient ? HeroGradientOpacity + "," + HeroGradientOffset + "," + HeroGradientFalloff + "," + HeroGradientCeiling + "," + HeroGradientSpread : "-");
                if (wanted != _extrasFor)
                {
                    foreach (var look in _looks)
                    {
                        foreach (var holder in look.Extra)
                            if (holder != null)
                                foreach (var extra in _extras) holder.RemoveOverride(extra, this);
                        look.Extra.Clear();
                    }
                    _extrasFor = wanted;
                    _extras.Clear();
                    _extras.Add(new MaterialPropertyOverride("_ShadowColour", HeroShadow, MaterialPropertyBlendType.TopTwo));
                    if (HeroSaturation >= 0f) _extras.Add(new MaterialPropertyOverride("_Saturation", HeroSaturation));
                    if (HeroGradient)
                    {
                        _extras.Add(new MaterialPropertyOverride("_UseGradient", true, false));
                        if (HeroGradientOpacity >= 0f) _extras.Add(new MaterialPropertyOverride("_GradientOpacity", HeroGradientOpacity));
                        if (HeroGradientOffset >= 0f) _extras.Add(new MaterialPropertyOverride("_GradientOffset", HeroGradientOffset));
                        if (HeroGradientFalloff >= 0f) _extras.Add(new MaterialPropertyOverride("_GradientFalloff", HeroGradientFalloff));
                        if (HeroGradientCeiling >= 0f) _extras.Add(new MaterialPropertyOverride("_GradientCeiling", HeroGradientCeiling));
                        if (HeroGradientSpread >= 0f) _extras.Add(new MaterialPropertyOverride("_GradientSpread", HeroGradientSpread));
                    }
                }
                for (var i = 0; i < _actors.Count; i++)
                {
                    var actor = _actors[i];
                    if (actor == null) continue;
                    var look = _looks[i];
                    var rank = _actorRank[i];
                    if (look4)
                    {
                        look.Holders.Clear();
                        look.Holders.AddRange(actor.GetComponentsInChildren<MaterialPropertyBhv>(true));
                    }
                    var scalar = rank < HeroLightScalar.Length ? HeroLightScalar[rank] : 1f;
                    // fiftieths: the override is made anew when the value changes
                    var light = CorridorLight.Hero(_leadX - rank * _spacing, CameraAt, scalar);
                    // DD1 shows an investigation through its presentation: the acting hero and the curio in front
                    // (layer B, brightened), everybody else on the blurred, half-drained layer A behind. A DD2 model
                    // cannot be blurred, so the bystanders are taken down instead: the script's boost is the actor's
                    // alone, and the others stand at a share of their light.
                    if (_scriptActor >= 0 && i != _scriptActor)
                        light = light / Mathf.Max(0.01f, Mathf.Pow(Mathf.Max(0.01f, CorridorLight.HeroBoost), CorridorLight.HeroGamma)) * BystanderLight;
                    var value = Mathf.Round(light * 50f) / 50f;
                    if (look.Brightness == null || !Mathf.Approximately(value, look.Value))
                    {
                        if (look.Brightness != null)
                            foreach (var holder in look.Lit)
                                if (holder != null) holder.RemoveOverride(look.Brightness, this);
                        look.Lit.Clear();
                        look.Value = value;
                        look.Brightness = new MaterialPropertyOverride("_Brightness", value);
                    }
                    foreach (var holder in look.Holders)
                    {
                        if (holder == null) continue;
                        if (look.Lit.Add(holder)) holder.AddOverride(look.Brightness, HeroLookPriority, 1f, this);
                        if (look.Extra.Add(holder))
                            foreach (var extra in _extras) holder.AddOverride(extra, HeroLookPriority, 1f, this);
                    }
                }
            }
            catch (System.Exception e)
            {
                // the look is cosmetic: never let it stop the view
                Plugin.Log.LogWarning("Corridor: the heroes' look could not be set: " + e.Message);
                _lookFailed = true;
            }
        }

        /// <summary>Dev bridge: the _Brightness each model has now, leader first.</summary>
        internal float[] HeroBrightnessNow()
        {
            var values = new float[_looks.Count];
            for (var i = 0; i < values.Length; i++) values[i] = _looks[i].Value;
            return values;
        }

        // DD1's scene fade multiplies the light of everything in the view; a black curtain over the view does the
        // same to what is on screen. The canvas blends in linear space: what is left is taken to the display's power.
        private void DrawCurtain()
        {
            if (_curtain == null) return;
            var left = CorridorLight.Curtain;
            var alpha = left >= 0.999f ? 0f : 1f - Mathf.Pow(left, QualitySettings.activeColorSpace == ColorSpace.Linear ? 2.2f : 1f);
            if (Mathf.Approximately(alpha, _curtainAlpha)) return;
            _curtainAlpha = alpha;
            _curtain.color = new Color(0f, 0f, 0f, alpha);
            _curtain.enabled = alpha > 0.002f;
        }

        /// <summary>Dev bridge: the models, to measure them.</summary>
        internal IReadOnlyList<ActorBhv> Actors => _actors;

        // ---- walking where the player points ---------------------------------------------------------

        /// <summary>Dev bridge: walk for a while as if a key were held (direction +1 right, -1 left).</summary>
        public void DevWalk(float direction, float seconds)
        {
            StopWalk();
            _devDirection = Mathf.Sign(direction);
            _devWalk = seconds;
        }

        /// <summary>
        /// The player turned to something in the area (a click, or the dev bridge). In a hallway the party
        /// walks up to it first, as DD1's does, and whoever listens to <see cref="CorridorProps.Clicked"/> hears
        /// of it on arrival; what the tiles on the way hold stops the walk as it stops any other. In a room
        /// the party stands at the curio already.
        /// </summary>
        public void Click(CorridorProp prop)
        {
            if (prop == null) return;
            if (_isRoom || prop.Kind == PropKind.Exit)
            {
                if (Told(prop)) CorridorProps.RaiseClicked(prop);
                return;
            }
            _devWalk = 0f;
            if (prop.Kind == PropKind.Door && Mathf.Abs(_leadX - Props.StandX(prop)) < CorridorNumbers.DoorReach)
            {
                // at the door already: it is used from where the party stands
                StopWalk();
                _speed = 0f;
                if (!Frozen && !DungeonHud.Asking) CorridorProps.RaiseClicked(prop);
                return;
            }
            if (prop.Kind == PropKind.Trap)
            {
                // DD1 (RaidDisplay::CanInteractWithTrap): a trap can be tried while it lies within the reach of the
                // party's front (m_InteractionAreaPixelWidth, half to either side); and it goes off when the
                // party's place comes onto its tile, the leader half a hero's lead short of it. Between the two
                // the leader stands to try it; past the second there is nothing left to try.
                var line = prop.Foot.x - CorridorNumbers.LeaderAhead;
                if (_leadX >= line) return;
                if (_leadX >= prop.Foot.x - CorridorNumbers.PropReach)
                {
                    StopWalk();
                    _speed = 0f;
                    if (!Frozen && !DungeonHud.Asking) CorridorProps.RaiseClicked(prop);
                    return;
                }
            }
            // what bars the way is turned to from where the party was stopped
            _walkTo = Mathf.Min(Mathf.Clamp(Props.StandX(prop), MinLeadX, MaxLeadX), Barrier);
            _walkProp = prop;
        }

        /// <summary>
        /// The leader walks no further than this (an obstacle the party left standing bars the way: DD1's party
        /// stands before it until it is cleared). Reset with every area shown.
        /// </summary>
        public float Barrier = float.MaxValue;

        public bool Walking => _walkTo.HasValue;

        /// <summary>How many tiles the area on screen has (a hallway: its two door tiles and what lies between).</summary>
        public int TileCount => _tiles.Count;

        /// <summary>The door at a hallway's last tile (or at its first); null in a room.</summary>
        public CorridorProp DoorAt(bool end)
        {
            if (_isRoom) return null;
            foreach (var prop in Props.All)
                if (prop.Kind == PropKind.Door && (end ? prop.Tile >= _tiles.Count - 1 : prop.Tile <= 0)) return prop;
            return null;
        }

        // What DD1's W turns to: the thing the party stands at (a curio lit to be turned to, a hidden door in
        // the party's tile, a secret room's way out), else the door whose tile the leader stands in.
        // Null: nothing is at hand.
        private CorridorProp AtHand()
        {
            CorridorProp door = null;
            var nearest = float.MaxValue;
            foreach (var prop in Props.All)
            {
                if (prop.Staged || prop.State == null) continue;
                switch (prop.Kind)
                {
                    case PropKind.Curio:
                        if (prop.State == ExplorationProps.Active) return prop;
                        break;
                    case PropKind.Trap:
                        // a spotted trap within the party's reach: W is the selected hero's try at it
                        if (!_isRoom && _leadX < prop.Foot.x - CorridorNumbers.LeaderAhead && _leadX >= prop.Foot.x - CorridorNumbers.PropReach
                            && DungeonRun.Current != null && DungeonRun.Current.CanDisarm(prop)) return prop;
                        break;
                    case PropKind.Obstacle:
                        // an obstacle the party left standing, in the tile it stands in
                        if (!_isRoom && prop.Tile == _currentTile && prop.State == ExplorationProps.Idle) return prop;
                        break;
                    case PropKind.SecretDoor:
                        if (!_isRoom && prop.Tile == _currentTile) return prop;
                        break;
                    case PropKind.Exit:
                        return prop;
                    case PropKind.Door:
                        if (_isRoom) break;
                        // DD1 (measured): W uses a door only while the leader stands in the door's own tile;
                        // from further off the key does nothing (a click on the door still walks the party up)
                        var away = Mathf.Abs(Props.StandX(prop) - _leadX);
                        if (away >= CorridorNumbers.DoorReach) break;
                        // between the two doors of a hallway one tile long, the one ahead
                        if (away > nearest || (Mathf.Approximately(away, nearest) && door != null && door.Tile > prop.Tile)) break;
                        nearest = away;
                        door = prop;
                        break;
                }
            }
            return door;
        }

        private void StopWalk()
        {
            _walkTo = null;
            _walkProp = null;
            _pointerHeld = false;
        }

        // Whatever is turned to is told to the expedition: a door too, which DD1 has the player use (W, or a click
        // on it) and never walks the party through by itself; a spotted trap, which the selected hero tries; an
        // obstacle the party left standing.
        private static bool Told(CorridorProp prop)
        {
            return prop.Kind == PropKind.Curio || prop.Kind == PropKind.SecretDoor || prop.Kind == PropKind.Exit || prop.Kind == PropKind.Door
                   || prop.Kind == PropKind.Trap || prop.Kind == PropKind.Obstacle;
        }

        private void Arrive()
        {
            var prop = _walkProp;
            var reached = _walkTo.HasValue && Mathf.Abs(_leadX - _walkTo.Value) < 2f;
            StopWalk();
            _speed = 0f;
            // a question raised on the way (the curio's own, a trap, a fight) has the word first
            if (reached && prop != null && Told(prop) && !Frozen && !DungeonHud.Asking) CorridorProps.RaiseClicked(prop);
        }

        internal void PointerDown(Vector2 screen)
        {
            if (Frozen || !EstateSession.InHub || DungeonHud.Asking || DungeonHud.Picking) return;
            var prop = PropAt(screen);
            if (prop != null) Click(prop);
            else if (MouseWalk && InputEnabled) _pointerHeld = true;
        }

        internal void PointerUp() { _pointerHeld = false; }

        internal void PointerInside(bool inside)
        {
            _pointerInside = inside;
            if (!inside) _pointerHeld = false;
        }

        private CorridorProp PropAt(Vector2 screen)
        {
            var camera = Camera.main;
            return camera != null ? Props.Pick(camera.ScreenPointToRay(screen)) : null;
        }

        // DD1: a press ahead of the party walks it on, a press behind it walks it back.
        private float PointerSide()
        {
            var camera = Camera.main;
            if (camera == null || Mouse.current == null || _scene.Root == null) return 0f;
            var ray = camera.ScreenPointToRay(Mouse.current.position.ReadValue());
            var origin = _scene.Root.InverseTransformPoint(ray.origin);
            var direction = _scene.Root.InverseTransformDirection(ray.direction);
            if (Mathf.Abs(direction.z) < 1e-6f) return 0f;
            // where the pointer is on the party's line
            var x = (origin + direction * (-origin.z / direction.z)).x;
            if (x > _leadX + 60f) return 1f;
            return x < _leadX - _spacing * Mathf.Max(0, _slots.Count - 1) - 60f ? -1f : 0f;
        }

        // The strip takes the pointer through the UI's own event system, so whatever lies over it (the HUD,
        // a question, one of the game's screens) keeps its clicks. Under the strip, the screen's lower third
        // is black: the main camera does not draw there (KeepViewRect) and DD1's panel stands on black.
        private void ShowPointerStrip(bool on)
        {
            if (_pointerCanvas == null)
            {
                if (!on) return;
                _pointerCanvas = UiKit.Canvas("DD2Estate.CorridorInput", 3);      // under the dungeon HUD (4)
                var black = UiKit.Image("Panel", _pointerCanvas.transform, null, Color.black, false);
                var below = (RectTransform)black.transform;
                below.anchorMin = Vector2.zero;
                below.anchorMax = new Vector2(1f, 1f - StripShare);
                below.offsetMin = Vector2.zero;
                below.offsetMax = Vector2.zero;
                _curtain = UiKit.Image("Curtain", _pointerCanvas.transform, null, new Color(0f, 0f, 0f, 0f), false);
                var over = (RectTransform)_curtain.transform;
                over.anchorMin = new Vector2(0f, 1f - StripShare);
                over.anchorMax = Vector2.one;
                over.offsetMin = Vector2.zero;
                over.offsetMax = Vector2.zero;
                _curtain.enabled = false;
                _curtainAlpha = -1f;
                var strip = UiKit.Image("Strip", _pointerCanvas.transform, null, Color.clear, true);
                var rt = (RectTransform)strip.transform;
                rt.anchorMin = new Vector2(0f, 1f - StripShare);
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                strip.gameObject.AddComponent<CorridorPointer>().View = this;
            }
            _pointerCanvas.gameObject.SetActive(on);
            if (!on) PointerInside(false);
        }

        private void OnEnable() { ShowPointerStrip(true); }

        private void OnDisable()
        {
            ShowPointerStrip(false);
            KeepViewRect(false);
            CorridorScene.ForegroundCamera.Release();
        }

        // ---- per frame -------------------------------------------------------------------------------

        private void Update()
        {
            KeepCharacterCameraOn();
            KeepViewRect(true);
            CorridorScene.ForegroundCamera.Keep(Camera.main, CorridorScene.ForegroundMode == 1, CorridorPost.Wanted);
            CorridorLight.Update(Time.deltaTime);
            // (the colours follow the light as DD1's do: towards its band, over a second or so: ShownLight)
            CorridorPost.Update(transform, _dungeon, ShownLight.Grade);

            TickTransition(Time.deltaTime);
            // under DD1's loading screen the party stands: the key that sends the screen away is not a step
            var busy = Frozen || !EstateSession.InHub || Transitioning || LoadingScreen.IsUp;
            var asking = DungeonHud.Asking;
            var direction = 0f;
            var led = false;        // walking to a point, not in a direction
            if (busy)
            {
                _devWalk = 0f;
                StopWalk();
            }
            else if (_devWalk > 0f)
            {
                _devWalk -= Time.deltaTime;
                direction = _devDirection;
            }
            else
            {
                var kb = Keyboard.current;
                if (InputEnabled && !asking && kb != null)
                {
                    if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) direction += 1f;
                    if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) direction -= 1f;
                }
                // the keyboard takes the party back from a walk the pointer started
                if (direction != 0f) StopWalk();
                else if (asking) StopWalk();
                else if (_walkTo.HasValue)
                {
                    led = true;
                    direction = _walkTo.Value >= _leadX ? 1f : -1f;
                }
                else if (_pointerHeld && InputEnabled) direction = PointerSide();

                // DD1's "back" leaves a secret room, which has no hallway of its own to choose on the map
                if (Props.InSecretRoom && !asking && kb != null && (kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame))
                    Click(Props.Find(PropKind.Exit));
                // DD1's W: "When in front of a door, [CLICK] on it or press [W]", "[CLICK] on objects or press [W]
                // to interact with them": the key is a click on what the party stands at
                else if (!asking && !DungeonHud.Picking && kb != null && kb.wKey.wasPressedThisFrame && !DungeonHud.SheetOpen)
                {
                    // a door at hand is walked up to and used, as a click on it would
                    Click(AtHand());
                }
            }

            Drift(Time.deltaTime, Mathf.Abs(_speed) > 1f);
            ApplyScript(direction != 0f);
            TickRankMoves(Time.deltaTime);
            // the party stands while its heroes change places
            Walk(direction, led, busy || _isRoom || _scriptRoutine != null || _rankMoves.Count > 0);
            _scene.Tick(CameraAt, CameraZoom, ViewPixels);
            LightHeroes();
            GroundShadows();
            GuardWeapons();
            DrawCurtain();

            Props.Hover(_pointerInside && !busy && !asking && Mouse.current != null ? PropAt(Mouse.current.position.ReadValue()) : null);
            Props.Tick(Time.deltaTime);
        }

        // DD1's walking (shared/rules.json): 1600 a second per second up to 400 forwards, 800 up to 200 backwards,
        // 1600 to a stop. Heroes do not turn round to walk back.
        private void Walk(float direction, bool led, bool still)
        {
            var dt = Time.deltaTime;
            if (still || _party == null) direction = 0f;
            var n = CorridorNumbers.ForwardAcceleration;
            if (direction > 0f)
                _speed = _speed < 0f ? Mathf.MoveTowards(_speed, 0f, CorridorNumbers.Deceleration * dt) : Mathf.MoveTowards(_speed, CorridorNumbers.MaxForwardSpeed, n * dt);
            else if (direction < 0f)
                _speed = _speed > 0f ? Mathf.MoveTowards(_speed, 0f, CorridorNumbers.Deceleration * dt) : Mathf.MoveTowards(_speed, -CorridorNumbers.MaxReverseSpeed, CorridorNumbers.ReverseAcceleration * dt);
            else
                _speed = still ? 0f : Mathf.MoveTowards(_speed, 0f, CorridorNumbers.Deceleration * dt);

            var target = _leadX;
            // the line of heroes draws out behind a leader walking on and closes up behind one walking back
            if (!_isRoom && Mathf.Abs(_speed) > 1f)
                _spacing = Mathf.MoveTowards(_spacing, _speed > 0f ? CorridorNumbers.SpacingForward : CorridorNumbers.SpacingBackward, CorridorNumbers.SpacingRate * dt);
            if (Mathf.Abs(_speed) > 0.01f)
            {
                var step = _speed * WalkSpeedScale * dt;
                target = led && _walkTo.HasValue ? Mathf.MoveTowards(_leadX, _walkTo.Value, Mathf.Abs(step)) : _leadX + step;
                // (a party that stands past what bars the way, placed there by something else, is not pulled back)
                target = Mathf.Clamp(target, MinLeadX, Mathf.Max(MinLeadX, Mathf.Min(MaxLeadX, Mathf.Max(Barrier, _leadX))));
                if (Mathf.Approximately(target, _leadX)) _speed = 0f;       // against the hallway's end, or what bars the way
            }
            var moved = !Mathf.Approximately(target, _leadX);

            // DD1's walking-back camera: the counter runs up while the party backs and down 1.5 times as fast otherwise
            var backing = moved && _speed < 0f;
            _back = Mathf.Clamp(_back + (backing ? dt : -CorridorNumbers.BackRecovery * dt), 0f, CorridorNumbers.BackTime);
            BackBlend = BackOverride >= 0f ? Mathf.Clamp01(BackOverride)
                : _isRoom ? 0f : 0.5f + 0.5f * Mathf.Cos((CorridorNumbers.BackTime - _back) / CorridorNumbers.BackTime * Mathf.PI);

            // the walk's stand-in runs while the party moves (and while it walks into a door, which keeps its own time)
            if (_phase != Phase.WalkIn) _walkPhase = moved ? _walkPhase + dt * BobRate : Mathf.MoveTowards(_walkPhase % Mathf.PI, 0f, dt * BobRate);
            WalkAnimation(moved || _phase == Phase.WalkIn, _speed);
            if (moved) SetLeadX(target, notify: true);
            else
            {
                PlaceParty();
                PlaceCamera();
            }
            // entering a tile may have shown another area, or raised a question: the walk is over then
            if (led && _walkTo.HasValue && (!moved || Mathf.Abs(_leadX - _walkTo.Value) < 0.5f)) Arrive();
        }

        // The game switches its character overlay camera off in every mode whose flag is not on its list;
        // the mod's mode is never on it, and each mode change switches it off again. The view is up while the
        // game's fade lifts over it on the way back from a fight, the heroes with it.
        private void KeepCharacterCameraOn()
        {
            if (_characterCamera == null)
            {
                var main = Camera.main;
                var child = main != null ? main.transform.Find("Character Camera") : null;
                if (child == null) return;
                _characterCamera = child.gameObject;
            }
            if (!_characterCamera.activeSelf && EstateSession.HubMode) _characterCamera.SetActive(true);
        }

        private void OnDestroy()
        {
            _props?.Release();
            _scene.Destroy();
            KeepViewRect(false);
            CorridorScene.ForegroundCamera.Release();
            CorridorPost.Release();
            if (_pointerCanvas != null) Destroy(_pointerCanvas.gameObject);
            if (Instance == this) Instance = null;
        }
    }

    /// <summary>The corridor strip as the UI sees it: a clear panel that hands the pointer to the view.</summary>
    internal class CorridorPointer : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public CorridorView View;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (View != null && eventData.button == PointerEventData.InputButton.Left) View.PointerDown(eventData.position);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (View != null) View.PointerUp();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (View != null) View.PointerInside(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (View != null) View.PointerInside(false);
        }
    }
}
