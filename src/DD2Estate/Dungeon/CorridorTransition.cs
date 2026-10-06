using System.Collections.Generic;
using DD2Estate.Core;
using UnityEngine;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// Settings of the way from one area into the next (docs/recon/dd1-corridor-rendering.md, section 8), static
    /// so the dev bridge and the test scripts can set them.
    /// </summary>
    internal static class CorridorTransition
    {
        /// <summary>Off: an area is replaced by the next at once, as before stage 4.</summary>
        public static bool Enabled = true;
        /// <summary>How many times faster than DD1 the whole sequence runs. 0 or less: no sequence at all (test scripts).</summary>
        public static float TimeScale = 1f;
        /// <summary>DD1: leaving a hallway by a door, the heroes walk into it and darken on the way. Off: fades only.</summary>
        public static bool WalkIn = true;
        /// <summary>How far towards the wall's depth a hero walks on its way into the door (1: into the doorway, 0: it
        /// stays on the party's line). [inferred: DD1 measures a hero's distance to its goal in three dimensions.]</summary>
        public static float WalkDepthShare = 1f;
        /// <summary>DD1 blends the camera towards the door over camera_position_blend_time while the party walks in
        /// (how is not recovered: here the camera's x goes from the party to the door).</summary>
        public static bool CameraBlend = true;
    }

    internal partial class CorridorView
    {
        private enum Phase { None, WalkIn, FadeOut, FadeIn }

        /// <summary>What DungeonRun asked to be shown, kept until the old area has faded out.</summary>
        private class AreaRequest
        {
            public string Dungeon;
            public List<string> Tiles;
            public bool IsRoom;
            public int StartTile;
        }

        private Phase _phase = Phase.None;
        private AreaRequest _pending;
        private float _doorX, _doorBlend, _doorWait, _doorHeight = CorridorNumbers.DoorCameraHeight;
        private readonly List<Vector2> _walkIn = new List<Vector2>();       // each hero's x and z on its way into the door
        private readonly List<bool> _walkedIn = new List<bool>();

        /// <summary>The view is between two areas (the party walks into a door, the scene fades out or in): nothing
        /// else should happen on screen until it is over.</summary>
        public bool Transitioning => _phase != Phase.None;

        /// <summary>For DungeonRun's event routine: <c>while (CorridorView.TransitionRunning) yield return null;</c></summary>
        public static bool TransitionRunning => Instance != null && Instance.isActiveAndEnabled && Instance.Transitioning;

        /// <summary>An area was asked for and is not on screen yet (what stands in it is kept back as well).</summary>
        internal bool AreaPending => _pending != null;

        public string TransitionPhase => _phase.ToString();

        private static bool TransitionsOn => CorridorTransition.Enabled && CorridorTransition.TimeScale > 0f;

        // DD1 (0xb06100, 0xaf3630, 0xae9c20): the heroes walk into the door and darken; the scene's light runs to
        // nothing; the next area is built; the light comes back with the party standing in its place.
        private void BeginTransition(AreaRequest request)
        {
            _pending = request;
            StopWalk();
            _devWalk = 0f;
            _speed = 0f;
            if (_phase == Phase.WalkIn || _phase == Phase.FadeOut) return;      // already on the way out: only the goal changed
            var last = Mathf.Max(0, _tiles.Count - 1);
            var atDoor = !_isRoom && (_currentTile <= 0 || _currentTile >= last);
            if (CorridorTransition.WalkIn && atDoor && request.IsRoom && _slots.Count > 0)
            {
                _phase = Phase.WalkIn;
                _doorX = (_currentTile <= 0 ? 0 : last) * CorridorNumbers.TileWidth;
                _doorHeight = DoorMiddleHeight(_currentTile <= 0 ? 0 : last);
                Props.OpenDoor(_currentTile <= 0 ? 0 : last);
                _doorBlend = 0f;
                _doorWait = CorridorNumbers.DoorWait + CorridorNumbers.DoorSettle;
                _walkIn.Clear();
                _walkedIn.Clear();
                for (var i = 0; i < _slots.Count; i++)
                {
                    _walkIn.Add(new Vector2(_leadX - i * _spacing, 0f));
                    _walkedIn.Add(false);
                }
            }
            else _phase = Phase.FadeOut;
        }

        /// <summary>The light comes up on an area that has just been built (the first of an expedition, or after a swap).</summary>
        private void BeginFadeIn()
        {
            if (!TransitionsOn)
            {
                CorridorLight.Fade = 1f;
                _phase = Phase.None;
                return;
            }
            CorridorLight.Fade = 0f;
            _phase = Phase.FadeIn;
        }

        /// <summary>The area on screen comes up out of black once more: the loading screen that stood over it has
        /// just gone (DD1: its picture goes from one frame to the next and the dungeon fades in).</summary>
        public void FadeInAgain()
        {
            if (_tiles.Count == 0 || _scene.Root == null || _phase == Phase.WalkIn || _phase == Phase.FadeOut) return;
            BeginFadeIn();
        }

        /// <summary>Whatever is under way is brought to its end at once: the asked-for area on screen, fully lit.</summary>
        public void FinishTransition()
        {
            if (_pending != null) Swap();
            _phase = Phase.None;
            CorridorLight.Fade = 1f;
            ResetWalkIn();
        }

        /// <summary>The expedition is over: nothing more is shown.</summary>
        private void CancelTransition()
        {
            _pending = null;
            _phase = Phase.None;
            CorridorLight.Fade = 1f;
            ResetWalkIn();
        }

        private void ResetWalkIn()
        {
            _walkIn.Clear();
            _walkedIn.Clear();
            _doorBlend = 0f;
            for (var i = 0; i < HeroLightScalar.Length; i++) HeroLightScalar[i] = 1f;
        }

        private void Swap()
        {
            var request = _pending;
            _pending = null;
            ResetWalkIn();
            Show(request.Dungeon, request.Tiles, request.IsRoom, request.StartTile);
            Props.ApplyPending();
        }

        private void TickTransition(float deltaTime)
        {
            if (_phase == Phase.None) return;
            if (!TransitionsOn)
            {
                FinishTransition();
                return;
            }
            var dt = deltaTime * CorridorTransition.TimeScale;
            switch (_phase)
            {
                case Phase.WalkIn:
                    // DD1: with the party within reach of the door the scene's light starts down, slowly
                    CorridorLight.Fade = Mathf.Max(0f, CorridorLight.Fade - dt / Mathf.Max(0.01f, CorridorNumbers.DoorCameraBlendTime));
                    _doorBlend = Mathf.Min(1f, _doorBlend + dt / Mathf.Max(0.01f, CorridorNumbers.DoorCameraBlendTime));
                    if (!WalkIntoDoor(dt)) break;
                    _doorWait -= dt;
                    if (_doorWait <= 0f) _phase = Phase.FadeOut;
                    break;
                case Phase.FadeOut:
                    _doorBlend = Mathf.Min(1f, _doorBlend + dt / Mathf.Max(0.01f, CorridorNumbers.DoorCameraBlendTime));
                    CorridorLight.Fade -= dt / Mathf.Max(0.01f, CorridorNumbers.FadeOutTime);
                    if (CorridorLight.Fade > 0f) break;
                    CorridorLight.Fade = 0f;
                    if (_pending != null) Swap();
                    _phase = Phase.FadeIn;
                    break;
                case Phase.FadeIn:
                    CorridorLight.Fade += dt / Mathf.Max(0.01f, CorridorNumbers.FadeInTime);
                    if (CorridorLight.Fade < 1f) break;
                    CorridorLight.Fade = 1f;
                    _phase = Phase.None;
                    break;
            }
        }

        // Every hero walks to the door; its light is (distance / 250)^2 over the last 250 units (party_darken_distance
        // + visibility_distance_to_door), and within visibility_distance_to_door it is gone. True when all are in.
        private bool WalkIntoDoor(float dt)
        {
            var goal = new Vector2(_doorX, CorridorNumbers.WallZ * Mathf.Clamp01(CorridorTransition.WalkDepthShare));
            var step = CorridorNumbers.MaxForwardSpeed * Mathf.Max(0.1f, WalkSpeedScale) * dt;
            var dark = Mathf.Max(1f, CorridorNumbers.DoorDarken + CorridorNumbers.DoorVisibility);
            var all = true;
            for (var i = 0; i < _walkIn.Count; i++)
            {
                _walkIn[i] = Vector2.MoveTowards(_walkIn[i], goal, step);
                var left = Vector2.Distance(_walkIn[i], goal);
                if (i < HeroLightScalar.Length) HeroLightScalar[i] = Mathf.Min(1f, left * left / (dark * dark));
                _walkedIn[i] = left <= CorridorNumbers.DoorVisibility;
                if (left > 1f) all = false;
            }
            _walkPhase += dt * BobRate;
            return all;
        }

        /// <summary>Where hero i stands, DD1 units: on the party's line, or on its way into a door.</summary>
        private Vector3 HeroAt(int i, out bool gone)
        {
            gone = false;
            if ((_phase == Phase.WalkIn || _phase == Phase.FadeOut) && i < _walkIn.Count)
            {
                gone = _walkedIn[i];
                return new Vector3(_walkIn[i].x, 0f, _walkIn[i].y);
            }
            return new Vector3(_leadX - i * _spacing, 0f, 0f);
        }

        // The camera on the way into a door. DD1 (measured: the door tile's middle and the screen row 172 units above
        // the floor stay where they are on screen while the tile grows, by 1 / (1 - t / 3.0)): the camera's place
        // goes in a straight line, evenly over camera_position_blend_time, from where it rests towards a point on
        // the door (its middle, DoorCameraHeight above the floor, on the wall's plane). The fade cuts the flight
        // off a little over half way. It does not turn and does not slide sideways along the wall.
        private Vector3 DoorCamera(Vector3 rest)
        {
            if (!CorridorTransition.CameraBlend || _walkIn.Count == 0 || (_phase != Phase.WalkIn && _phase != Phase.FadeOut)) return rest;
            return Vector3.Lerp(rest, new Vector3(_doorX, _doorHeight, CorridorNumbers.WallZ), _doorBlend);
        }

        // DD1 (measured in two dungeons): the point the camera flies at is half the door prop's height above the
        // floor: 214 in the Ruins, whose door's art is 429 high, 171 in the Weald (349). The door's own art says
        // it; a door without art has the Weald's number.
        private float DoorMiddleHeight(int tile)
        {
            foreach (var prop in Props.All)
            {
                if (prop.Kind != PropKind.Door || prop.Tile != tile || prop.Model == null) continue;
                var bounds = prop.Model.Bounds;
                if (bounds.height > 1f) return prop.Foot.y + bounds.center.y * Mathf.Max(0.01f, prop.UnitsPerPixel);
            }
            return CorridorNumbers.DoorCameraHeight;
        }

        /// <summary>
        /// Dev bridge: the sequence on the area on screen, which is shown again in the same place. In a hallway with
        /// <paramref name="walkIn"/> the party walks into the door ahead first, from wherever it stands.
        /// </summary>
        public string DevTransition(bool walkIn)
        {
            if (_tiles.Count == 0 || _scene.Root == null) return "no area on screen";
            if (!TransitionsOn) return "transitions are off (CorridorTransition.Enabled, TimeScale)";
            FinishTransition();
            var request = new AreaRequest { Dungeon = _dungeon, Tiles = new List<string>(_tiles), IsRoom = _isRoom, StartTile = _isRoom ? 0 : _currentTile };
            var hall = Props.Hallway;
            var room = Props.Room;
            BeginTransition(request);
            // what stands in the area comes back with it
            Props.Hold(hall, room);
            if (walkIn && !_isRoom && _slots.Count > 0)
            {
                _phase = Phase.WalkIn;
                _doorX = Mathf.Max(0, _tiles.Count - 1) * CorridorNumbers.TileWidth;
                _doorHeight = DoorMiddleHeight(Mathf.Max(0, _tiles.Count - 1));
                Props.OpenDoor(Mathf.Max(0, _tiles.Count - 1));
                _doorBlend = 0f;
                _doorWait = CorridorNumbers.DoorWait + CorridorNumbers.DoorSettle;
                _walkIn.Clear();
                _walkedIn.Clear();
                for (var i = 0; i < _slots.Count; i++)
                {
                    _walkIn.Add(new Vector2(_leadX - i * _spacing, 0f));
                    _walkedIn.Add(false);
                }
            }
            return _phase.ToString();
        }
    }
}
