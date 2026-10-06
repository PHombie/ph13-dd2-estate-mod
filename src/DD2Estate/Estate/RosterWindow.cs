using System;
using System.Collections.Generic;
using System.IO;
using DD2Estate.Dd1;
using DD2Estate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DD2Estate.Estate
{
    /// <summary>
    /// A building's screen inside the hamlet canvas, framed the way DD1 frames every town screen: the
    /// building's wide backdrop where town.layout.darkest puts it (town_background_layout area_pos), its keeper
    /// hanging out of it at the left (character_pos), DD1's name of the screen at building_base_layout name_pos
    /// (<see cref="Dd1ScreenName"/>: the building's icon, its name beside it between the two rules drawn in the
    /// backdrop, and the "+" that opens the building's upgrades), DD1's close button at close_pos, one line of
    /// text at building_base_body_layout info_text_offset, where DD1 writes what a screen has to say, and left
    /// of it all DD1's column of buttons that lead to the other buildings (<see cref="BuildingNavigation"/>).
    /// The roster column and the estate's bar stay as they are, as in DD1: the window ends where they begin,
    /// and the keeper's feet go under the bar.
    ///
    /// The screen grows out of its middle when it opens and shrinks back into it when it closes
    /// (building.layout.darkest building_animation); between two buildings it changes at once.
    ///
    /// Heroes come from the roster column, as in DD1: while a screen takes heroes (<see cref="HeroPicked"/>) a
    /// click on a roster row hands that hero to the screen instead of the party, and a row can be dragged onto
    /// a place the screen has named (<see cref="AddDrop"/>). One window at a time; it goes away with the hamlet.
    /// tools/preview_windows.py draws the same frame offline: change one, change the other.
    /// </summary>
    internal class RosterWindow : MonoBehaviour
    {
        /// <summary>Size of DD1's `&lt;building&gt;.character_background.png`.</summary>
        public const float FrameWidth = 1395f;
        public const float FrameHeight = 776f;

        private const string TownLayout = "campaign/town/town.layout.darkest";
        private const string BuildingLayout = "campaign/town/buildings/building.layout.darkest";
        private const string SummaryLayout = "campaign/town/estate_summary/estate_summary.layout.darkest";
        private const string ProgressionDir = "shared/progression/";

        // DD1's screen: 1920x1080, the roster column begins at town_screen_layout roster_list_pos.
        private const float ScreenHeight = 1080f;
        private const float RoomWidth = 1920f - RosterPanel.Width;
        private static readonly Vector2 CloseSize = new Vector2(32f, 32f);     // progression_close.png
        private static readonly Vector2 InfoSize = new Vector2(700f, 44f);     // the line and its second one, above the frame's foot
        private const float SayLasts = 6f;

        /// <summary>
        /// False: a window is there, and gone, in one frame, as it was before the screens moved (for a test
        /// that looks at the screen the moment it has asked for it).
        /// </summary>
        public static bool Animate = true;

        private class DropSpot
        {
            public RectTransform Target;
            public Action<uint> Dropped;
        }

        private static RosterWindow _current;
        private static RosterWindowLeaving _leaving;
        private static int _leftFrame = -1;

        private RectTransform _frame;
        private CanvasGroup _group;
        private Dd1ScreenName _name;
        private BuildingNavigation _navigation;
        private float _zoom = 1f, _zoomFrom = 1f, _zoomSeconds, _zoomStart, _leaveSeconds, _leaveScale = 1f;
        private TextMeshProUGUI _info;
        private Vector2 _areaPos;
        private string _hint, _said;
        private bool _saidTrouble;
        private float _saidUntil;
        private float _nextRefresh;
        private readonly List<DropSpot> _drops = new List<DropSpot>();

        /// <summary>The building's id (stage_coach, tavern, ...).</summary>
        public string Id { get; private set; }

        /// <summary>The backdrop's rect: children are placed in its pixels (top-left origin, y down).</summary>
        public RectTransform Frame => _frame;

        /// <summary>DD1's building_base_layout body_base_pos in the backdrop's pixels: what a building's own layout file counts from.</summary>
        public Vector2 Body { get; private set; }

        /// <summary>Called twice a second while the window is open (and by the owner after it acts).</summary>
        public Action Refresh;

        /// <summary>The building's upgrade pane behind the "+"; null for a building without trees.</summary>
        public UpgradePane Upgrades { get; private set; }

        /// <summary>DD1's tooltip box for whatever the pointer is on in this window.</summary>
        public Dd1Tooltip Tooltip { get; private set; }

        /// <summary>
        /// Set by a screen that takes heroes: called with the hero whose roster row was clicked, or who was
        /// dragged from the roster and let go over the window outside every named place.
        /// </summary>
        public Action<uint> HeroPicked;

        /// <summary>
        /// Set by a screen that has something to step back from (a picked slot, a hero not yet paid for): a
        /// right click on the backdrop asks it first, and leaves the building only when it returns false.
        /// </summary>
        public Func<bool> BackOut;

        public static bool IsOpen => _current != null;

        public static RosterWindow Current => _current;

        /// <summary>Whether the open window is this building's.</summary>
        public static bool Shows(string id) => _current != null && _current.Id == id;

        /// <summary>For the test bridge: what a click on a building's button in the open window's quick navigation does.</summary>
        public static string Go(string building)
        {
            return _current != null && _current._navigation != null ? _current._navigation.Click(building) : "no window is open";
        }

        /// <summary>For the test bridge: what a click on a hero's roster row does while a window that takes heroes is open.</summary>
        public static bool Pick(uint guid)
        {
            if (_current == null || _current.HeroPicked == null) return false;
            _current.Take(guid);
            return true;
        }

        /// <param name="art">DD1 path and file stem of the building's art, e.g. campaign/town/buildings/graveyard/graveyard.</param>
        /// <param name="upgradesOnly">The window shows the building's upgrades and nothing else.</param>
        public static RosterWindow Open(string id, string title, string art, bool upgradesOnly = false)
        {
            // From one building straight into another (DD1's quick navigation): no going and no coming.
            var straight = _current != null || _leftFrame == Time.frameCount;
            Close(false);
            if (_leaving != null) Destroy(_leaving.gameObject);     // one screen at a time, also of those on their way out
            var canvas = RosterPanel.CanvasRoot;
            if (canvas == null)
            {
                Plugin.Log.LogWarning("Hamlet: no canvas to open " + id + " in");
                return null;
            }

            // FALLBACK numbers below: DD1's own values of these entries, used when a layout file cannot be read.
            var town = Dd1Ui.Layout(TownLayout);
            var building = Dd1Ui.Layout(BuildingLayout);
            var summary = Dd1Ui.Layout(SummaryLayout);
            var areaPos = Dd1Ui.Offset(town, "town_background_layout", "area_pos", 144f, 132f);
            var keeperPos = Dd1Ui.Offset(town, "town_background_layout", "character_pos", 132f, 240f) - areaPos;
            var namePos = Dd1Ui.Offset(building, "building_base_layout", "name_pos", 104f, 126f) - areaPos;
            var closePos = Dd1Ui.Offset(building, "building_base_layout", "close_pos", 1496f, 144f) - areaPos;
            var body = Dd1Ui.Offset(building, "building_base_layout", "body_base_pos", 596f, 102f) - areaPos;
            var infoPos = body + Dd1Ui.Offset(building, "building_base_body_layout", "info_text_offset", 580f, 760f);
            // The estate's bar (progression_bar.png) begins at estate_summary_pos + pos_offset: the window ends there.
            var barTop = Dd1Ui.Offset(town, "town_screen_layout", "estate_summary_pos", 0f, 975f).y + Dd1Ui.Offset(summary, "estate_summary_layout", "pos_offset", 0f, -17f).y;

            var root = UiKit.Rect("Window." + id, canvas);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = new Vector2(0f, Mathf.Max(0f, ScreenHeight - barTop));
            root.offsetMax = new Vector2(-RosterPanel.Width, 0f);
            root.gameObject.AddComponent<RectMask2D>();     // the keeper stands behind the bar, as in DD1
            var window = root.gameObject.AddComponent<RosterWindow>();
            window.Id = id;
            window.Body = body;
            window._areaPos = areaPos;

            // A sibling of the frame, not its parent: a click bubbles up to the first handler, and a click on
            // the frame must not find this one.
            var shade = UiKit.Image("Shade", root, null, HamletShade.Colour, true);
            UiKit.Stretch((RectTransform)shade.transform);
            shade.gameObject.AddComponent<RosterWindowShade>();
            HamletShade.Attach(shade);      // drawn across the whole screen, under the roster column

            window._frame = UiKit.Rect("Frame", root).PlaceTopLeft(areaPos, Dd1Ui.TopLeft, new Vector2(FrameWidth, FrameHeight));
            window._group = window._frame.gameObject.AddComponent<CanvasGroup>();
            var backdrop = Dd1Ui.Sprite(art + ".character_background.png");
            var back = UiKit.Image("Backdrop", window._frame, backdrop, backdrop != null ? Color.white : new Color(0.05f, 0.05f, 0.055f, 0.97f), true);
            UiKit.Stretch((RectTransform)back.transform);
            back.gameObject.AddComponent<RosterWindowShade>().RightClickOnly = true;    // DD1: a right click leaves the building

            // The keeper stays while the upgrades are open: the pane's art is black at three parts in four, and he shows through it.
            var keeper = Dd1Ui.Sprite(art + ".character.png");
            if (keeper != null)
            {
                var image = UiKit.Image("Keeper", window._frame, keeper);
                ((RectTransform)image.transform).PlaceTopLeft(keeperPos, Dd1Ui.TopLeft, keeper.rect.size);
            }

            // DD1's name of the screen: the building's icon, its name, and the "+" of a building that has upgrades.
            // Icon and name lie under the upgrade pane, whose art frames the icon and is clear over the name.
            var more = !upgradesOnly && UpgradeRules.TreesOf(id).Count > 0;
            window._name = Dd1ScreenName.Add(window._frame, namePos, art + ".icon.png", title, more ? (Action)window.ToggleUpgrades : null);

            Dd1Ui.ArtButton("Close", window._frame, ProgressionDir + "progression_close.png", closePos, CloseSize, Close, "X");

            window._info = Dd1Ui.Block("Info", window._frame, "town_building_info", infoPos, InfoSize, TextAlignmentOptions.Top, null, 13f);

            // DD1 keeps a building's upgrades on the keeper's side of its screen.
            window.Upgrades = UpgradePane.Attach(window._frame, id, window.OnUpgradesToggled, upgradesOnly);
            window.Tooltip = new Dd1Tooltip(window._frame, FrameWidth, FrameHeight);
            // A building's screen is worth having without the way to the others.
            try { window._navigation = BuildingNavigation.Attach(root, id, !straight && Animate, barTop); }
            catch (Exception e) { Plugin.Log.LogError("Hamlet: the quick navigation of the " + id + " screen failed: " + e); }

            // DD1: a building's screen comes up from base_scale to its size in transition_time and goes the same way in transition_out_time.
            var smallest = Dd1Ui.Number(building, "building_animation", "base_scale", 0.4f);
            window._leaveSeconds = Dd1Ui.Number(building, "building_animation", "transition_out_time", 0.3f);
            window._leaveScale = smallest;
            if (!straight && Animate)
            {
                window._zoomSeconds = Dd1Ui.Number(building, "building_animation", "transition_time", 0.3f);
                window._zoomFrom = smallest;
                window._zoomStart = Time.unscaledTime;
            }
            _current = window;
            window.Zoom();
            window.Fit();
            return window;
        }

        private void ToggleUpgrades()
        {
            if (Upgrades != null) Upgrades.SetOpen(!Upgrades.IsOpen);
        }

        private void OnUpgradesToggled(bool open)
        {
            Tooltip?.Hide(null);
            _name?.SetOpen(open);
            LiftMore();
        }

        // The pane puts itself over everything when it opens (and a screen that rebuilds its own things puts it
        // there again); the way back out of it, the "+" turned cross, lies on top of the pane.
        private void LiftMore()
        {
            if (_name == null || _name.More == null || Upgrades == null || !Upgrades.IsOpen) return;
            var pane = Upgrades.transform.GetSiblingIndex();
            if (_name.More.GetSiblingIndex() < pane) _name.More.SetSiblingIndex(pane);
        }

        public static void Close() => Close(true);

        private static void Close(bool leave)
        {
            if (_current == null) return;
            var window = _current;
            _current = null;
            _leftFrame = Time.frameCount;
            window.HeroPicked = null;
            window.HookRoster();        // gives the roster's rows back to the party at once
            if (leave && Animate) window.Leave();
            Destroy(window.gameObject);
        }

        // DD1 lets a building's screen shrink back into its middle. The window itself goes at once, as
        // everything that asks IsOpen expects; what shrinks is its picture, handed to an object that takes no
        // clicks and lives for that third of a second.
        private void Leave()
        {
            if (_frame == null || _leaveSeconds <= 0f || !gameObject.activeInHierarchy) return;
            Tooltip?.Hide(null);
            var own = (RectTransform)transform;
            var rest = UiKit.Rect(name + ".Leaving", own.parent);
            rest.anchorMin = own.anchorMin;
            rest.anchorMax = own.anchorMax;
            rest.offsetMin = own.offsetMin;
            rest.offsetMax = own.offsetMax;
            rest.SetSiblingIndex(own.GetSiblingIndex());
            rest.gameObject.AddComponent<RectMask2D>();
            _frame.SetParent(rest, false);
            if (_leaving != null) Destroy(_leaving.gameObject);
            _leaving = rest.gameObject.AddComponent<RosterWindowLeaving>();
            _leaving.Begin(_frame, _group, _leaveSeconds, _leaveScale);
        }

        // ---- what the window says ------------------------------------------------------------------------

        /// <summary>The line the window shows when it has nothing else to say (DD1's building info colour).</summary>
        public void Hint(string text)
        {
            _hint = text;
            ShowInfo();
        }

        /// <summary>A line in place of the hint for a few seconds; null takes it away.</summary>
        public void Say(string text, bool trouble = false)
        {
            _said = text;
            _saidTrouble = trouble;
            _saidUntil = text == null ? 0f : Time.unscaledTime + SayLasts;
            ShowInfo();
        }

        /// <summary>What stands on the info line now.</summary>
        public string Said => _info != null ? _info.text : null;

        private void ShowInfo()
        {
            if (_info == null) return;
            _info.text = _said ?? _hint ?? "";
            _info.color = _said == null ? Dd1Fonts.Colour("town_building_info", UpgradeUi.Dim) : _saidTrouble ? UiKit.Harmful : UiKit.Neutral;
        }

        /// <summary>
        /// Shows or hides the tooltip of <paramref name="owner"/>; <paramref name="at"/> is its upper left corner in
        /// the backdrop's pixels. <paramref name="fixedWidth"/>: the box is <paramref name="width"/> wide whatever it
        /// says (a DD1 tooltip layout with tooltip_is_auto_width 0).
        /// </summary>
        public void Tip(object owner, bool show, Vector2 at, string title, string body, float width = 300f, bool fixedWidth = false)
        {
            if (Tooltip == null) return;
            if (show && !(string.IsNullOrEmpty(title) && string.IsNullOrEmpty(body))) Tooltip.ShowAt(owner, at, title, body, width, !fixedWidth);
            else Tooltip.Hide(owner);
        }

        // ---- heroes from the roster ----------------------------------------------------------------------

        /// <summary>Names a place a hero can be dragged onto from the roster.</summary>
        public void AddDrop(RectTransform target, Action<uint> dropped)
        {
            _drops.Add(new DropSpot { Target = target, Dropped = dropped });
        }

        /// <summary>Forgets the places named so far (their objects are being rebuilt).</summary>
        public void ClearDrops() => _drops.Clear();

        private void Take(uint guid)
        {
            try { HeroPicked?.Invoke(guid); }
            catch (Exception e) { Plugin.Log.LogError("Hamlet: " + name + " failed to take a hero: " + e); }
        }

        private void Drop(uint guid, PointerEventData pointer)
        {
            for (var i = _drops.Count - 1; i >= 0; i--)
            {
                var spot = _drops[i];
                if (spot.Target == null)
                {
                    _drops.RemoveAt(i);
                    continue;
                }
                if (!spot.Target.gameObject.activeInHierarchy || !RectTransformUtility.RectangleContainsScreenPoint(spot.Target, pointer.position, pointer.pressEventCamera)) continue;
                try { spot.Dropped(guid); }
                catch (Exception e) { Plugin.Log.LogError("Hamlet: " + name + " failed to take a dropped hero: " + e); }
                return;
            }
            // Let go over the window but beside every slot: the same as a click on the row.
            if (RectTransformUtility.RectangleContainsScreenPoint(_frame, pointer.position, pointer.pressEventCamera)) Take(guid);
        }

        // While the screen takes heroes, the roster's rows hand them to it: the row's own click (in or out of
        // the party) is switched off and a drag handle put beside it. Rows are rebuilt when the roster
        // changes, so this is looked after every frame; it is a few components.
        private void HookRoster()
        {
            var list = RosterPanel.CanvasRoot != null ? RosterPanel.CanvasRoot.Find("Roster/List") : null;
            if (list == null) return;
            var take = HeroPicked != null;
            for (var i = 0; i < list.childCount; i++)
            {
                var row = list.GetChild(i);
                var click = row.GetComponent<RosterRowClick>();
                if (click == null) continue;
                var handle = row.GetComponent<HeroDrag>();
                if (take)
                {
                    if (handle == null)
                    {
                        var guid = click.Guid;
                        handle = row.gameObject.AddComponent<HeroDrag>();
                        handle.Guid = guid;
                        handle.Portrait = () => HeroPortrait(guid);
                        handle.Clicked = () => Take(guid);
                        handle.RightClicked = () => UpgradeUi.ShowSheet(guid);
                        handle.Dropped = pointer => Drop(guid, pointer);
                    }
                    if (click.enabled) click.enabled = false;
                }
                else
                {
                    if (handle != null)
                    {
                        handle.enabled = false;
                        Destroy(handle);
                    }
                    if (!click.enabled) click.enabled = true;
                }
            }
        }

        private static Sprite HeroPortrait(uint guid)
        {
            var actor = SanitariumLedger.Actor(guid);
            return actor != null ? HeroNames.Portrait(actor) : null;
        }

        // ---- upkeep --------------------------------------------------------------------------------------

        // DD1's screen leaves 1550 pixels left of the roster. A wider screen keeps the window in the middle of
        // that room, as the town is; on one narrower than 16:9 the frame shrinks with the room. While the
        // window opens the frame grows out of its own middle.
        private void Fit()
        {
            var area = ((RectTransform)transform).rect;
            if (area.width <= 0f) return;
            var fit = Mathf.Min(1f, area.width / RoomWidth);
            var left = Mathf.Max(0f, (area.width - RoomWidth) * 0.5f);
            var scale = fit * _zoom;
            var middle = new Vector2(left + (_areaPos.x + FrameWidth * 0.5f) * fit, -(_areaPos.y + FrameHeight * 0.5f) * fit);
            var at = middle + new Vector2(-FrameWidth, FrameHeight) * (0.5f * scale);
            if (!Mathf.Approximately(scale, _frame.localScale.x)) _frame.localScale = new Vector3(scale, scale, 1f);
            if ((_frame.anchoredPosition - at).sqrMagnitude > 0.01f) _frame.anchoredPosition = at;
            if (_navigation != null) _navigation.Place(left, fit);
        }

        // DD1: a building's screen comes up from building_animation base_scale to its size in transition_time.
        // Its files give the buildings no easing; this is the one of its panels (shared/app.darkest
        // g_PanelTransitionTunables: easeOutSine in, easeInSine out). The screen clears as it grows: a guess,
        // the files do not speak of it.
        private void Zoom()
        {
            var share = _zoomSeconds > 0f ? Mathf.Clamp01((Time.unscaledTime - _zoomStart) / _zoomSeconds) : 1f;
            var eased = Mathf.Sin(share * Mathf.PI * 0.5f);
            _zoom = Mathf.Lerp(_zoomFrom, 1f, eased);
            if (_group != null && !Mathf.Approximately(_group.alpha, eased)) _group.alpha = eased;
            if (share >= 1f) _zoomSeconds = 0f;
        }

        private void Update()
        {
            if (_zoomSeconds > 0f) Zoom();
            Fit();
            LiftMore();
            HookRoster();
            if (_said != null && Time.unscaledTime > _saidUntil) Say(null);

            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 0.5f;
            // DD1 marks the open building's news on its name's icon (and not on its button of the quick navigation)
            _name?.SetAlert((Upgrades == null || !Upgrades.IsOpen) && UpgradeAlerts.Has(Id));
            try { Refresh?.Invoke(); }
            catch (Exception e) { Plugin.Log.LogError("Hamlet: " + name + " failed to refresh: " + e); }
        }

        // The hamlet is hidden by deactivating its canvas; a window does not wait for it to come back.
        private void OnDisable()
        {
            HeroPicked = null;
            HookRoster();
            if (_current == this) _current = null;
            Destroy(gameObject);
        }

        /// <summary>For tests: what the window's own parts show.</summary>
        public object Snapshot()
        {
            return new { id = Id, info = Said, takesHeroes = HeroPicked != null, upgrades = Upgrades != null && Upgrades.IsOpen, navigation = _navigation != null ? _navigation.Snapshot() : null };
        }
    }

    /// <summary>
    /// What is left of a building window that has closed: its picture, shrinking into its middle and clearing
    /// (DD1: building.layout.darkest building_animation transition_out_time and base_scale, with the easing of
    /// DD1's panels going out, easeInSine). It takes no clicks and goes with the hamlet.
    /// </summary>
    internal class RosterWindowLeaving : MonoBehaviour
    {
        private RectTransform _frame;
        private CanvasGroup _group;
        private Vector2 _middle;
        private float _scale, _alpha = 1f, _smallest, _seconds, _start;

        public void Begin(RectTransform frame, CanvasGroup group, float seconds, float smallest)
        {
            _frame = frame;
            _group = group;
            _seconds = seconds;
            _smallest = smallest;
            _start = Time.unscaledTime;
            // from where the window stood: it may have been closed while it was still coming up
            _scale = frame.localScale.x;
            _middle = frame.anchoredPosition + new Vector2(RosterWindow.FrameWidth, -RosterWindow.FrameHeight) * (0.5f * _scale);
            if (group != null)
            {
                _alpha = group.alpha;
                group.blocksRaycasts = false;
                group.interactable = false;
            }
        }

        private void Update()
        {
            var share = _seconds > 0f ? (Time.unscaledTime - _start) / _seconds : 1f;
            if (share >= 1f || _frame == null)
            {
                Destroy(gameObject);
                return;
            }
            var eased = 1f - Mathf.Cos(share * Mathf.PI * 0.5f);
            var scale = _scale * Mathf.Lerp(1f, _smallest, eased);
            _frame.localScale = new Vector3(scale, scale, 1f);
            _frame.anchoredPosition = _middle + new Vector2(-RosterWindow.FrameWidth, RosterWindow.FrameHeight) * (0.5f * scale);
            if (_group != null) _group.alpha = _alpha * (1f - eased);
        }

        private void OnDisable() => Destroy(gameObject);
    }

    /// <summary>Closes the building window: any click on the darkened town around it, a right click on the window.</summary>
    internal class RosterWindowShade : MonoBehaviour, IPointerClickHandler
    {
        public bool RightClickOnly;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (RightClickOnly && eventData.button != PointerEventData.InputButton.Right) return;
            // DD1: a right click steps back; out of the building once there is nothing left to step back from.
            var window = RosterWindow.Current;
            if (RightClickOnly && window != null && window.BackOut != null && window.BackOut()) return;
            RosterWindow.Close();
        }
    }

    /// <summary>
    /// A hero that can be picked up: DD1 moves heroes between the roster, a building's slots and the stage coach
    /// by dragging their portrait. While it is dragged the portrait follows the pointer; where it is let go is
    /// for the owner to judge. A plain click is told apart from a drag.
    /// </summary>
    internal class HeroDrag : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private const float GhostSize = 85f;     // DD1's hero slot

        /// <summary>
        /// The hero whose portrait is in the player's hand right now, from the moment it is picked up until it is
        /// let go; 0 for nobody. A screen's slots show by it where the hero may go (DD1's hero_slot.positive_frame
        /// and hero_slot.locked_for_hero).
        /// </summary>
        public static uint InHand { get; private set; }

        /// <summary>Raised when a hero is picked up and when one is let go.</summary>
        public static event Action InHandChanged;

        /// <summary>The hero this handle picks up.</summary>
        public uint Guid;
        public Func<Sprite> Portrait;
        public Action Clicked, RightClicked;
        public Action<PointerEventData> Dropped;

        private RectTransform _ghost;

        private static void Hold(uint guid)
        {
            if (InHand == guid) return;
            InHand = guid;
            try { InHandChanged?.Invoke(); }
            catch (Exception e) { Plugin.Log.LogError("Hamlet: a screen failed to follow the hero in hand: " + e); }
        }

        /// <summary>For the test bridge: a hero in hand without the mouse (0 lets go), to look at what the slots show.</summary>
        public static void HoldForTest(uint guid) => Hold(guid);

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.dragging) return;
            if (eventData.button == PointerEventData.InputButton.Left) Clicked?.Invoke();
            else if (eventData.button == PointerEventData.InputButton.Right) RightClicked?.Invoke();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || RosterPanel.CanvasRoot == null) return;
            var sprite = Portrait?.Invoke();
            var image = UiKit.Image("DraggedHero", RosterPanel.CanvasRoot, sprite, sprite != null ? new Color(1f, 1f, 1f, 0.9f) : new Color(0.2f, 0.2f, 0.2f, 0.9f));
            image.preserveAspect = true;
            _ghost = ((RectTransform)image.transform).Place(Dd1Ui.Middle, Dd1Ui.Middle, Vector2.zero, new Vector2(GhostSize, GhostSize));
            Follow(eventData);
            // DD1's sound of a hero picked up
            DD2Estate.Dd2.EstateAudio.Ui("ui/town/character_pickup");
            Hold(Guid);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_ghost != null) Follow(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_ghost == null) return;
            Drop();
            Dropped?.Invoke(eventData);
        }

        private void Follow(PointerEventData eventData)
        {
            var canvas = (RectTransform)_ghost.parent;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, eventData.position, eventData.pressEventCamera, out var local))
                _ghost.anchoredPosition = local - Vector2.Scale(canvas.rect.size, Dd1Ui.Middle - canvas.pivot);
            _ghost.SetAsLastSibling();
        }

        private void Drop()
        {
            if (_ghost == null) return;
            Destroy(_ghost.gameObject);
            _ghost = null;
            // let go before the owner hears where: the slots are judged with an empty hand again
            if (InHand == Guid) Hold(0u);
        }

        private void OnDisable() => Drop();
    }

    /// <summary>
    /// DD1's English words for the windows: the main string table (through <see cref="Dd1Strings"/>) and, for
    /// what DD1 writes about a building's upgrades and about a class in the guild, the smithy and the
    /// survivalist's camp, the heroes' table (localization/heroes.string_table.xml: building_verbose_&lt;id&gt;,
    /// action_verbose_body_&lt;building&gt;_&lt;class&gt;, hero_class_name_&lt;class&gt;).
    /// </summary>
    internal static class WindowText
    {
        private const string HeroesTable = "localization/heroes.string_table.xml";
        private static readonly string[] HeroesPrefixes = { "building_verbose_", "action_verbose_", "hero_class_name_" };
        private static Dictionary<string, string> _heroes;

        /// <summary>The text of a DD1 string id as written; null when DD1 has none.</summary>
        public static string Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var text = Dd1Strings.Get(id);
            if (text != null) return text;
            if (_heroes == null) _heroes = ReadHeroes();
            return _heroes.TryGetValue(id, out text) ? text : null;
        }

        /// <summary>A DD1 text without its colour marks and line breaks; null when DD1 has none.</summary>
        public static string Plain(string id)
        {
            var text = Get(id);
            return text != null ? Dd1Strings.Format(text) : null;
        }

        /// <summary>A DD1 printf-style text filled in (<paramref name="fallback"/> is DD1's own wording, for a missing table).</summary>
        public static string Format(string id, string fallback, params object[] args)
        {
            return Dd1Strings.Format(Get(id) ?? fallback, args);
        }

        private static Dictionary<string, string> ReadHeroes()
        {
            var texts = new Dictionary<string, string>();
            var path = Dd1Install.PathOf(HeroesTable);
            if (path == null || !File.Exists(path)) return texts;
            try { Dd1Strings.ReadEnglish(File.ReadLines(path), texts, HeroesPrefixes); }
            catch (Exception e) { Plugin.Log.LogWarning("DD1 heroes string table could not be read: " + e.Message); }
            return texts;
        }
    }
}
