using System;
using System.Collections.Generic;
using Assets.Code.Actor;
using Assets.Code.Audio;
using Assets.Code.Rendering.MaterialPropertyBlocks;
using Assets.Code.UI.Widgets;
using Assets.Code.Utils;
using DD2Estate.UI;
using FMODUnity;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// The heroes seated at the camp answer the pointer as they do at DD2's inn.
    ///
    /// How DD2 does it (Assets.Code.UI.Widgets.RestItemSlotBhv, one for every seat of the inn's scene, read in
    /// the running game: "Camp_Root/Inn Timeline/Inn Presentation/Floating UI/RestSlots/HeroA".."HeroD"): the seat
    /// is a rectangle of the inn's screen laid where the hero's model stands (373 wide, 430 to 500 high on a 1080
    /// high screen). The pointer on it plays the slot's roll-over sound and leans the scene's camera towards that
    /// seat (m_RolloverEvent: BlendActiveCameraBhv.SetActiveCamera); with a rest item in hand it lights the model
    /// (ApplyActorHighlight: a rim light in the slot's colour, a warm orange 1 / 0.73 / 0.53, given to the model's
    /// material blocks at priority 6) and has the hero take the pose of one being offered something (the
    /// animator's bool "inn_rollover_item", which every hero's controller has). The item given, it asks for the
    /// trigger "inn_use_item", which the heroes' controllers do not have: nothing plays. Heroes whose bond
    /// changes are lit for a moment (RestWidgetBhv.ApplyAffinityOutlines, the same rim light for half a second
    /// or more). That pose is the only animation the seated heroes have for the pointer: the triggers of a
    /// bark do nothing to a seated hero, "resolute" stands them up in their fighting stance for good.
    /// The Estate does not enter the inn's mode, so that screen is switched off (CampView); its colour and its
    /// sound are read from it and its calls are made here.
    ///
    /// The Estate's camp: a rectangle of the camp's screen where every seated model stands, as the camera sees it
    /// (over the HUD and the bars' own click zones, under the camp's scrolls). The pointer on it lights the hero
    /// and has them take the pose, with the inn's sound; a click selects the hero (the banner shows their camping
    /// skills), or names the companion a skill waits for, as a click on the bars under the hero does: both go the
    /// same way (DungeonHud.Select), with DD2's click. The selected hero keeps the pose, as DD2's hero does while
    /// an item is held over them. The heroes a skill is for, or who eat, are lit and take the pose for a moment,
    /// with the inn's sound of an item used (of food eaten).
    /// The camera does not lean (the rectangles follow the models as the camera sees them, and the camp's HUD
    /// covers the foot of the screen DD2's lean is framed for).
    /// </summary>
    internal static class CampHeroes
    {
        // RestItemSlotBhv's names and numbers
        private const string RolloverBool = "inn_rollover_item";
        private const string UseTrigger = "inn_use_item";
        private const uint HighlightPriority = 6u;

        /// <summary>[Camp] HeroModels: off leaves the seated heroes as scenery (the bars under them still take clicks).</summary>
        public static bool Enabled = true;

        // Tunables (dev bridge: camp.heroes).
        /// <summary>
        /// How wide a hero's rectangle is at most, in pixels of a 1080 high screen: the width of the inn's own
        /// slots (RestItemSlotBhv's rectangles are 373 wide). Two neighbours share the room between them.
        /// </summary>
        public static float SlotWidth = 373f;
        /// <summary>How high it is at most, from the bars under the hero up (the inn's own are 430 to 500 high).</summary>
        public static float SlotHeight = 500f;
        /// <summary>How much of the model's height on screen takes the pointer, counted from the feet.</summary>
        public static float HeightShare = 0.96f;
        /// <summary>What of a model counts for its box: the parts within this many world units of its root.</summary>
        public static float Reach = 4f;
        /// <summary>The selected hero keeps the pose of a hero the pointer is on.</summary>
        public static bool SelectedLooksUp = true;
        /// <summary>How long a hero a skill or the meal is for stays lit and in the pose, in seconds.</summary>
        public static float UsePulse = 1.1f;
        /// <summary>The fallback colour of the light, when the inn's own cannot be read.</summary>
        public static Color FallbackLight = new Color(1f, 0.73f, 0.53f, 1f);
        /// <summary>Dev: the rectangles are drawn.</summary>
        public static bool ShowZones;

        private class Seat
        {
            public uint Guid;
            public ActorBhv Actor;
            public RectTransform Rect;
            public Image Image;
            public bool Lit, Up;
            public float PulseUntil;    // lit and in the pose until then: a skill or the meal was for them
            public float Depth;
            public float X;             // the model's own place on the screen, in pixels from its left edge
            public bool Seen;
            public Vector4 Model;       // the model's box on the screen
            public Vector4 Box;         // the rectangle that takes the pointer; screen pixels: x min, y min, x max, y max
        }

        private static readonly List<Seat> Seats = new List<Seat>();
        private static RectTransform _root;
        private static uint _pointed, _forced;
        private static bool _live;
        private static float _nextSort;
        private static Color _light;
        private static bool _lightRead;
        private static EventReference _hoverSound;
        private static string _lightFrom = "fallback";
        private static readonly List<string> Did = new List<string>();

        private static readonly MaterialPropertyOverride RimFlip = new MaterialPropertyOverride("_RimFlip", 1f);
        private static readonly MaterialPropertyOverride RimColour = new MaterialPropertyOverride("_RimColour", Color.magenta, MaterialPropertyBlendType.Override);
        private static readonly MaterialPropertyOverride RimLight = new MaterialPropertyOverride("_RimLight", 1f);
        private static readonly MaterialPropertyOverride ColourTint = new MaterialPropertyOverride("_ColorTintColor", Color.white, MaterialPropertyBlendType.TopTwo);
        private static readonly MaterialPropertyOverride Rim = new MaterialPropertyOverride("_RIM", true, true);
        private static readonly MaterialPropertyOverride FlipRim = new MaterialPropertyOverride("_FLIP_RIM", true, true);

        /// <summary>The hero the pointer is on; 0 for none.</summary>
        public static uint Pointed => _forced != 0u ? _forced : _pointed;

        /// <summary>The camp's screen is up: the rectangles go under <paramref name="before"/> (the scrolls), over what came earlier.</summary>
        public static void Open(Transform canvas, Transform before)
        {
            Close();
            _root = UiKit.Rect("Heroes", canvas);
            UiKit.Stretch(_root);
            if (before != null) _root.SetSiblingIndex(before.GetSiblingIndex());
            _root.gameObject.SetActive(false);
            _lightRead = false;
        }

        /// <summary>The camp's screen goes: nobody is lit, nothing takes the pointer.</summary>
        public static void Close()
        {
            foreach (var seat in Seats) Leave(seat);
            Seats.Clear();
            _pointed = _forced = 0u;
            _live = false;
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
            _root = null;
        }

        /// <summary>
        /// Every frame the camp's screen is up. <paramref name="live"/>: the rest stop is in view and the party
        /// awake (not while the dark covers it).
        /// </summary>
        public static void Tick(bool live)
        {
            if (_root == null) return;
            live = live && Enabled && CampView.State == "shown";
            if (live != _live)
            {
                _live = live;
                _root.gameObject.SetActive(live);
                if (!live)
                {
                    foreach (var seat in Seats) Leave(seat);
                    _pointed = _forced = 0u;
                }
            }
            if (!live) return;
            Sync();
            Place();
            var camp = Camp.Current;
            var selected = camp != null && !camp.Closing ? camp.Selected : 0u;
            foreach (var seat in Seats)
            {
                var marked = seat.Guid == Pointed || Time.unscaledTime < seat.PulseUntil;
                Show(seat, marked, marked || (SelectedLooksUp && seat.Guid == selected));
            }
        }

        // One rectangle for every model the rest stop has seated.
        private static void Sync()
        {
            var seated = CampView.Seated;
            var same = seated.Count == Seats.Count;
            for (var i = 0; same && i < seated.Count; i++) same = seated[i] != null && Seats[i].Actor == seated[i];
            if (same) return;
            foreach (var seat in Seats)
            {
                Leave(seat);
                if (seat.Rect != null) UnityEngine.Object.Destroy(seat.Rect.gameObject);
            }
            Seats.Clear();
            _pointed = 0u;
            foreach (var actor in seated)
            {
                if (actor == null) continue;
                var guid = actor.GetActorGuid();
                var image = UiKit.Image("Hero" + guid, _root, null, Color.clear, true);
                var seat = new Seat { Guid = guid, Actor = actor, Image = image, Rect = (RectTransform)image.transform };
                seat.Rect.anchorMin = seat.Rect.anchorMax = Vector2.zero;
                seat.Rect.pivot = new Vector2(0.5f, 0f);
                RaidUi.Pointer(image, () => Click(guid), null, inside =>
                {
                    if (inside) Enter(guid);
                    else if (_pointed == guid) _pointed = 0u;
                });
                Seats.Add(seat);
            }
            _nextSort = 0f;
        }

        // The rectangles lie over the models as the camera sees them now.
        private static void Place()
        {
            var camera = Camera.main;
            if (camera == null) return;
            var scale = _root.lossyScale.x > 0f ? _root.lossyScale.x : 1f;
            var unit = Screen.height / 1080f;
            // the bars under the heroes keep the pointer for themselves
            var trays = DungeonHud.Trays;
            var floor = trays != null ? (1080f - trays.BarTop + 10f) * unit : 0f;
            // A hero's place is where the model stands (its root, as the bars under the heroes take it, and as the
            // inn's own slots are laid out); the model's box (arms, weapons and cloaks reach far to one side) gives
            // only the height.
            foreach (var seat in Seats)
            {
                seat.Seen = false;
                if (seat.Actor == null) continue;
                var at = camera.WorldToScreenPoint(seat.Actor.transform.position);
                if (at.z <= 0f) continue;
                seat.Seen = true;
                seat.X = at.x;
                seat.Depth = at.z;
                if (!Box(seat.Actor, camera, out seat.Model)) seat.Model = new Vector4(at.x, at.y, at.x, at.y + SlotHeight * unit);
            }
            foreach (var seat in Seats)
            {
                var shown = seat.Seen;
                if (shown)
                {
                    // half the way to the nearest neighbour on either side, the inn's own width at most
                    float left = SlotWidth * 0.5f * unit, right = left;
                    foreach (var other in Seats)
                    {
                        if (other == seat || !other.Seen) continue;
                        var gap = (other.X - seat.X) * 0.5f;
                        if (gap < 0f) left = Mathf.Min(left, -gap);
                        else right = Mathf.Min(right, gap);
                    }
                    var foot = floor;
                    var top = Mathf.Min(seat.Model.y + (seat.Model.w - seat.Model.y) * HeightShare, foot + SlotHeight * unit);
                    seat.Box = new Vector4(seat.X - left, foot, seat.X + right, top);
                    shown = top - foot > 8f && left + right > 8f;
                    if (shown)
                    {
                        seat.Rect.anchoredPosition = new Vector2((seat.X + (right - left) * 0.5f) / scale, foot / scale);
                        seat.Rect.sizeDelta = new Vector2((left + right) / scale, (top - foot) / scale);
                        var tint = ShowZones ? new Color(1f, 0.2f, 0.2f, 0.25f) : Color.clear;
                        if (seat.Image.color != tint) seat.Image.color = tint;
                    }
                }
                if (seat.Rect.gameObject.activeSelf != shown) seat.Rect.gameObject.SetActive(shown);
            }
            if (Time.unscaledTime < _nextSort) return;
            _nextSort = Time.unscaledTime + 0.5f;
            // the nearer hero is the one the pointer finds where two overlap
            var order = new List<Seat>(Seats);
            order.Sort((a, b) => b.Depth.CompareTo(a.Depth));
            for (var i = 0; i < order.Count; i++) order[i].Rect.SetSiblingIndex(i);
        }

        // A model's box on the screen, from the boxes of what it is drawn with. A model carries parts that are
        // parked far away (seen: something 2500 units off): only what is within reach of its root counts.
        private static bool Box(ActorBhv actor, Camera camera, out Vector4 box)
        {
            box = default;
            var any = false;
            var bounds = default(Bounds);
            var origin = actor.transform.position;
            var reach = Reach * Mathf.Max(0.01f, actor.transform.lossyScale.y);
            foreach (var renderer in actor.GetComponentsInChildren<Renderer>())
            {
                if (renderer == null || !renderer.enabled || renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer) continue;
                var part = renderer.bounds;
                if ((part.center - origin).magnitude > reach || part.extents.magnitude > reach) continue;
                if (!any) bounds = part;
                else bounds.Encapsulate(part);
                any = true;
            }
            if (!any) return false;
            float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
            var min = bounds.min;
            var max = bounds.max;
            for (var i = 0; i < 8; i++)
            {
                var point = camera.WorldToScreenPoint(new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z));
                if (point.z <= 0f) return false;
                xMin = Mathf.Min(xMin, point.x);
                xMax = Mathf.Max(xMax, point.x);
                yMin = Mathf.Min(yMin, point.y);
                yMax = Mathf.Max(yMax, point.y);
            }
            box = new Vector4(xMin, yMin, xMax, yMax);
            return true;
        }

        // ---- what the pointer does ----------------------------------------------------------------------------

        private static void Enter(uint guid)
        {
            if (_pointed == guid) return;
            _pointed = guid;
            ReadInn();
            Play(_hoverSound, "hover");
        }

        private static void Click(uint guid)
        {
            var camp = Camp.Current;
            if (camp == null || camp.Closing) return;
            Note("click " + guid);
            // the same way a click on the bars under the hero goes (CampScreen.HeroClicked, and the HUD's own refresh)
            DungeonHud.Select(guid);
        }

        /// <summary>A hero was chosen at the camp (by a model or by the bars): DD2's click.</summary>
        public static void Chosen(uint guid)
        {
            if (!Enabled) return;
            try { Play(AudioPathsBhv.ClickConfirm, "click"); }
            catch (Exception e) { Plugin.Log.LogWarning("Camp heroes: DD2's click could not be played: " + e.Message); }
        }

        /// <summary>
        /// A camping skill was used, or the meal eaten: the heroes it was for are lit and take the pose for a
        /// moment, with the inn's sound of an item used (of food eaten for the meal).
        /// </summary>
        public static void Used(ICollection<uint> heroes, bool meal)
        {
            if (!Enabled || heroes == null) return;
            foreach (var seat in Seats)
            {
                if (seat.Actor == null || !heroes.Contains(seat.Guid)) continue;
                seat.PulseUntil = Time.unscaledTime + UsePulse;
                // RestItemSlotBhv.LockItem asks for this; a controller that has it plays it
                try { Note("used " + seat.Guid + (seat.Actor.AttemptAnimatorTrigger(UseTrigger) ? " (" + UseTrigger + ")" : "")); }
                catch (Exception e) { Plugin.Log.LogWarning("Camp heroes: the animation could not be played: " + e.Message); }
            }
            try { Play(meal ? AudioPathsBhv.RestItemUseFood : AudioPathsBhv.RestItemUseGeneral, "use"); }
            catch (Exception e) { Plugin.Log.LogWarning("Camp heroes: the inn's sound could not be played: " + e.Message); }
        }

        // The model as DD2 lights it under the pointer (RestItemSlotBhv.ApplyActorHighlight), and looking up.
        private static void Show(Seat seat, bool lit, bool up)
        {
            if (seat.Actor == null) return;
            try
            {
                if (lit != seat.Lit)
                {
                    seat.Lit = lit;
                    var source = _root.gameObject;
                    if (lit)
                    {
                        ReadInn();
                        RimColour.Color = _light;
                        foreach (var block in seat.Actor.MaterialPropertyBlocks)
                        {
                            if (block == null) continue;
                            block.AddOverride(RimFlip, HighlightPriority, 1f, source);
                            block.AddOverride(RimColour, HighlightPriority, 1f, source);
                            block.AddOverride(RimLight, HighlightPriority, 1f, source);
                            block.AddOverride(ColourTint, HighlightPriority, 1f, source);
                            block.AddOverride(FlipRim, HighlightPriority, 1f, source);
                            block.AddOverride(Rim, HighlightPriority, 1f, source);
                        }
                    }
                    else
                    {
                        foreach (var block in seat.Actor.MaterialPropertyBlocks)
                        {
                            if (block == null) continue;
                            block.RemoveOverride(RimFlip, source);
                            block.RemoveOverride(RimColour, source);
                            block.RemoveOverride(RimLight, source);
                            block.RemoveOverride(ColourTint, source);
                            block.RemoveOverride(FlipRim, source);
                            block.RemoveOverride(Rim, source);
                        }
                    }
                }
                if (up != seat.Up)
                {
                    seat.Up = up;
                    seat.Actor.AttemptAnimatorSetBool(RolloverBool, up);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Camp heroes: the model could not be lit: " + e.Message); }
        }

        private static void Leave(Seat seat)
        {
            if (seat.Actor == null || _root == null)
            {
                seat.Lit = seat.Up = false;
                return;
            }
            Show(seat, false, false);
        }

        // The colour and the sound of the inn's own hero slots, out of the scene the rest stop is.
        private static void ReadInn()
        {
            if (_lightRead) return;
            _lightRead = true;
            _light = FallbackLight;
            _lightFrom = "fallback";
            _hoverSound = default;
            try
            {
                var scene = SceneManager.GetSceneByName(CampView.SceneName);
                if (!scene.IsValid() || !scene.isLoaded) return;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var slot in root.GetComponentsInChildren<RestItemSlotBhv>(true))
                    {
                        if (AccessTools.Field(typeof(RestItemSlotBhv), "m_actorHoverColor")?.GetValue(slot) is Color colour)
                        {
                            _light = colour;
                            _lightFrom = "RestItemSlotBhv.m_actorHoverColor";
                        }
                        if (AccessTools.Field(typeof(RestItemSlotBhv), "m_onHoverEventRef")?.GetValue(slot) is EventReference sound) _hoverSound = sound;
                        return;
                    }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Camp heroes: the inn's own slot could not be read: " + e.Message); }
        }

        private static void Play(EventReference sound, string what)
        {
            if (sound.IsNull || !SingletonMonoBehaviour<AudioMgr>.HasInstance()) return;
            SingletonMonoBehaviour<AudioMgr>.Instance.Play(sound);
            Note("sound " + what);
        }

        private static void Note(string what)
        {
            Did.Add(Time.frameCount + " " + what);
            if (Did.Count > 24) Did.RemoveAt(0);
        }

        // ---- dev bridge ---------------------------------------------------------------------------------------

        /// <summary>The pointer as if it were on a hero (0: where it really is).</summary>
        public static void DevPoint(uint guid)
        {
            if (guid != 0u && guid != _forced)
            {
                ReadInn();
                Play(_hoverSound, "hover");
            }
            _forced = guid;
        }

        public static void DevClick(uint guid) => Click(guid);

        public static object Describe()
        {
            var seats = new List<object>();
            foreach (var seat in Seats)
                seats.Add(new
                {
                    guid = seat.Guid, lit = seat.Lit, looksUp = seat.Up, depth = seat.Depth, shown = seat.Rect != null && seat.Rect.gameObject.activeSelf,
                    // screen pixels from the bottom left: x min, y min, x max, y max (what ui.hover and input.click take)
                    box = new[] { seat.Box.x, seat.Box.y, seat.Box.z, seat.Box.w },
                    model = new[] { seat.Model.x, seat.Model.y, seat.Model.z, seat.Model.w }, x = seat.X,
                    blocks = seat.Actor != null && seat.Actor.MaterialPropertyBlocks != null ? seat.Actor.MaterialPropertyBlocks.Count : 0
                });
            return new
            {
                enabled = Enabled, live = _live, pointed = Pointed, selected = Camp.Current != null ? Camp.Current.Selected : 0u, armed = Camp.Current?.Armed,
                light = new[] { _light.r, _light.g, _light.b, _light.a }, lightFrom = _lightFrom, hoverSound = !_hoverSound.IsNull,
                slotWidth = SlotWidth, slotHeight = SlotHeight, heightShare = HeightShare, reach = Reach, selectedLooksUp = SelectedLooksUp, usePulse = UsePulse,
                seats, did = Did
            };
        }
    }
}
