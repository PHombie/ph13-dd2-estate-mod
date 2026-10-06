using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Assets.Code.Actor;
using Assets.Code.Library;
using Assets.Code.Locale;
using Assets.Code.UI.Managers;
using Assets.Code.Utils;
using DD2Estate.Dd1;
using DD2Estate.Dd2;
using DD2Estate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DD2Estate.Estate
{
    /// <summary>
    /// The roster column on the right of the hamlet, laid out like DD1's with the art of the player's DD1
    /// install: the dark gradient down the screen's edge, the two frames, and a row per hero (portrait slot,
    /// name, stress pips, weapon and armour level, resolve badge with its experience bar; the wax seal of the
    /// party, the sign of the building a hero is busy in). Left click moves a hero in or out of the embarking
    /// party, right click opens DD2's own character sheet. Eight rows show, as in DD1; the wheel or the arrows
    /// scroll the rest, the four buttons in the top frame sort. The pointer on a row's badge gets DD1's tooltip
    /// of it (level, experience, stress), on a sort button DD1's name of the order.
    ///
    /// DD1's numbers (campaign/town/roster/roster.layout.darkest, roster.layout.anim.darkest,
    /// shared/resolve_level_bar/...layout.darkest, town.layout.darkest for the column's place) are read from the
    /// files, the stock values stand in if a file cannot be read. What DD1 has no row for is the mod's own: the
    /// tooltip of the rest of a row (class and path, which tell the heroes of one class apart, and the reason a
    /// hero cannot embark), the dismiss control, and DD2's badge of the hero's path, which hangs from the row's
    /// upper rule in the free place right of the resolve badge (<see cref="HeroPathBadge"/>; the pointer on it
    /// is told the path's name and DD2's words about it). The badge keeps its place on the screen while its row
    /// slides out under the pointer: a badge that went with the row would leave the pointer that rests on it,
    /// and the dismiss control would come under that pointer instead; as it is the control shows in the gap the
    /// slide opens between the two badges. tools/preview_hamlet.py draws the same layout offline (without the
    /// path's badge, which is the running game's art): change one, change the other.
    /// </summary>
    internal class RosterPanel : MonoBehaviour, IScrollHandler
    {
        /// <summary>Width of the column: the 1920 px screen less DD1's roster_list_pos (1550). Building windows end here.</summary>
        internal const float Width = 370f;

        private const string Dir = "campaign/town/roster/";
        private const string SlotDir = "campaign/town/hero_slot/";
        private const string ResolveDir = "shared/resolve_level_bar/";
        private const string BuildingsDir = "campaign/town/buildings/";

        // Sizes of DD1's art, for the boxes that stand in when a file is missing.
        private static readonly Vector2 ElementSize = new Vector2(395f, 104f);   // rosterelement.background.png
        private static readonly Vector2 FrameSize = new Vector2(383f, 60f);      // roster_topframe.png
        private static readonly Vector2 ArrowSize = new Vector2(62f, 49f);       // roster_uparrow.png
        private const float GradientWidth = 373f;                                // roster_bggrad.png
        private const float SlotSize = 85f;                                      // hero_slot.background.png
        private const float SortSize = 32f;                                      // roster_sort_*.png
        private const float BadgeSize = 64f;                                     // resolve_level_bar_number_background_lvl*.png
        private static readonly Vector2 PipEmpty = new Vector2(8f, 12f);         // overlays/stress_pip_empty.png
        private static readonly Vector2 PipFull = new Vector2(9f, 10f);          // overlays/stress_pip_full.png

        // The mod's own numbers (row pixels, y down).
        private const float PortraitInset = 3f;                                  // the slot's frame stays clear of the portrait
        // DD2's portrait art is a head on clear ground with a wide margin; drawn this large in the slot's 79 px
        // window and cut at its frame, the head fills the slot as DD1's faces do (the owner, 2026-10-06: the heroes'
        // pictures in the rows larger, the square cutting off what is too much). See BuildRow.
        private static float PortraitSize = 136f;
        private const int Pips = 10;                                             // DD1 draws stress as ten pips
        private const float PortraitEdge = 3f;                                   // DD1's roster portraits: a grey rim of three pixels round a black field
        private static readonly Vector2 BarMask = new Vector2(37f, 59f);         // resolve_level_bar_mask.png
        private const float TipRoom = 700f;                                      // how far left of the column a tooltip may stand
        // The badge of the hero's path, in the free place between the resolve bar's slot (which ends at 307) and
        // the screen's edge (370 with the row at rest): the middle line of DD2's picture, the row of the art it
        // hangs from (on the upper rule), and the picture's height (the longest strips of parchment end where
        // the row's field does, above the lower rule). At that height the seal is 36 pixels across.
        private const float PathMiddle = 344f;
        private const float PathTop = 2f;
        private const float PathHeight = 94f;
        private const float PathTipWidth = 320f;                                 // DD2's words about a path run to several lines
        private static readonly Vector2 DismissPos = new Vector2(321f, 8f);      // between the two badges, in the gap the row's slide opens there
        private const float DismissSize = 32f;                                   // shared/character/icon_dismiss.png
        private const float NameSmallest = 20f;
        private const float RefreshSeconds = 0.4f;

        // DD1's sounds of the column (its exe names them): a new order, a step up or down the list.
        private const string SortSound = "ui/town/sort_by";
        private const string ScrollUpSound = "ui/shared/button_scroll_up", ScrollDownSound = "ui/shared/button_scroll_down";
        private const string ColourTable = "colours/base.colours.darkest";
        private static readonly Regex Unselectable = new Regex("\\.id\\s+\"?town_roster_portrait_unselectable\"?\\s+\\.darkness\\s+([\\d.]+)\\s+\\.saturation\\s+([\\d.]+)");

        private static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        private static readonly Vector2 TopCentre = new Vector2(0.5f, 1f);
        private static readonly Vector2 Middle = new Vector2(0.5f, 0.5f);

        private static RosterPanel _current;

        /// <summary>The hamlet canvas the column was built in; building windows open in it too.</summary>
        public static Transform CanvasRoot { get; private set; }

        private enum Sort { None, Activity, Class, Level, Stress }

        // roster_sort_<name>.png, in the order DD1 has them on its screen (a real frame of its hamlet: the shield,
        // the crowned head, the pawn, the house).
        private static readonly Sort[] SortButtons = { Sort.Level, Sort.Stress, Sort.Class, Sort.Activity };
        private static readonly string[] SortArt = { "level", "stress", "class", "building" };
        // FALLBACK: DD1's own words (str_sort_roster_by_<art>: "Sort by Level"), for an install whose table cannot be read.
        private static readonly string[] SortWords = { "Level", "Stress", "Class", "Activity" };

        /// <summary>DD1's numbers; the values given here are its own, used when a file cannot be read.</summary>
        private class Layout
        {
            public Vector2 ElementPos, ElementSpacing, TopFrame, BottomFrame, Message, SortStart, SortSpacing, SortAscending, SortDescending, SortTip, ScrollUp, ScrollDown;
            public int ElementMax;
            public Vector2 Portrait, BuildingIcon, MarkIcon, Name, Stress, StressSpacing, Weapon, Armour, Badge, BadgeBack, BadgeNumber, Bar, BarFill, BarFillSize, BarMaskAt;
            public Vector2 BadgeTip, BadgeSpot, BadgeSpotSize;
            public float BadgeTipWidth;
            public float Slide, SlideSeconds;
            public bool SlideEasesIn;
            /// <summary>The portrait of a hero who cannot be picked: how much of its light and of its colour is left.</summary>
            public float Darkness = 0.4f, Saturation = 0.2f;

            public static Layout Read()
            {
                var roster = Dd1Install.Found ? DarkestFile.Load(Dir + "roster.layout.darkest") : null;
                var anim = Dd1Install.Found ? DarkestFile.Load(Dir + "roster.layout.anim.darkest") : null;
                var resolve = Dd1Install.Found ? DarkestFile.Load(ResolveDir + "resolve_level_bar.layout.darkest") : null;
                const string list = "town_roster_list_layout", row = "town_roster_element_layout", slide = "town_roster_mouseover_element_anim", tip = "resolve_level_tooltip";
                var layout = new Layout
                {
                    SortTip = Offset(roster, list, "roster_sort_tooltip_offset", 16f, -8f),
                    BadgeTip = Offset(roster, row, "resolve_level_bar_tooltip_offset", -170f, 4f),
                    BadgeSpot = Offset(resolve, tip, "hot_spot_pos", 0f, 0f),
                    BadgeSpotSize = Offset(resolve, tip, "hot_spot_size", 100f, 100f),
                    BadgeTipWidth = Number(resolve, tip, "text_width", 200f),
                    // DD1's stock easing of the slide is "easeInQuad": slow at first; any other name slides evenly
                    SlideEasesIn = string.Equals(anim?.Find(slide)?.String("easing_function", 0, "easeInQuad") ?? "easeInQuad", "easeInQuad", StringComparison.OrdinalIgnoreCase),
                    ElementPos = Offset(roster, list, "element_pos", 0f, 132f),
                    ElementSpacing = Offset(roster, list, "element_spacing", 0f, 97f),
                    ElementMax = Mathf.Max(1, Mathf.RoundToInt(Number(roster, list, "element_max", 8f))),
                    TopFrame = Offset(roster, list, "top_frame_offset", 20f, -50f),
                    BottomFrame = Offset(roster, list, "bottom_frame_offset", 20f, -10f),
                    Message = Offset(roster, list, "roster_message_offset", 60f, 78f),
                    SortStart = Offset(roster, list, "roster_sort_start_position", 148f, 80f),
                    SortSpacing = Offset(roster, list, "roster_sort_spacing", 6f, 0f),
                    SortAscending = Offset(roster, list, "roster_sort_current_ascending_overlay_offset", -8f, -8f),
                    SortDescending = Offset(roster, list, "roster_sort_current_descending_overlay_offset", -8f, 24f),
                    ScrollUp = Offset(roster, list, "scroll_up_button_offset", 0f, -38f),
                    ScrollDown = Offset(roster, list, "scroll_down_button_offset", 0f, -12f),
                    Portrait = Offset(roster, row, "portrait_icon_offset", 21f, 9f),
                    BuildingIcon = Offset(roster, row, "building_icon_offset", 20f, 10f),
                    MarkIcon = Offset(roster, row, "non_building_icon_offset", 14f, 10f),
                    Name = Offset(roster, row, "name_offset", 116f, 4f),
                    Stress = Offset(roster, row, "stress_offset", 116f, 43f),
                    StressSpacing = Offset(roster, row, "stress_spacing", 10f, 0f),
                    Weapon = Offset(roster, row, "weapon_level_offset", 156f, 65f),
                    Armour = Offset(roster, row, "armour_level_offset", 228f, 65f),
                    Badge = Offset(roster, row, "resolve_level_bar_offset", 258f, 4f),
                    BadgeBack = Offset(resolve, "resolve_level_number", "background_offset", -3f, 0f),
                    BadgeNumber = Offset(resolve, "resolve_level_number", "number_offset", 30f, 31f),
                    Bar = Offset(resolve, "resolve_level", "bar_pos", 12f, 28f),
                    BarFill = Offset(resolve, "resolve_level_bar", "gradient_offset", 10f, 16f),
                    BarFillSize = Offset(resolve, "resolve_level_bar", "gradient_size", 16f, 40f),
                    BarMaskAt = Offset(resolve, "resolve_level_bar", "mask_offset", 0f, 0f),
                    Slide = Number(anim, slide, "x_pixel_slide", 30f),
                    SlideSeconds = Mathf.Max(0.01f, Number(anim, slide, "seconds_time", 0.2f))
                };
                // colours/base.colours.darkest: colour: .id "town_roster_portrait_unselectable" .darkness 0.4 .saturation 0.2
                try
                {
                    var table = Dd1Install.Found ? Dd1Install.ReadText(ColourTable) : null;
                    var match = table != null ? Unselectable.Match(table) : null;
                    if (match != null && match.Success)
                    {
                        layout.Darkness = Mathf.Clamp01(float.Parse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture));
                        layout.Saturation = Mathf.Clamp01(float.Parse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture));
                    }
                }
                catch (Exception e) { Plugin.Log.LogWarning("DD1 colour table could not be read: " + e.Message); }
                return layout;
            }

            private static Vector2 Offset(DarkestFile file, string block, string key, float x, float y)
            {
                var entry = file?.Find(block);
                return entry != null && entry.Values(key).Count >= 2 ? entry.Vector2(key) : new Vector2(x, y);
            }

            private static float Number(DarkestFile file, string block, string key, float fallback)
            {
                var entry = file?.Find(block);
                return entry != null && entry.Has(key) ? entry.Float(key, 0, fallback) : fallback;
            }
        }

        private class Row
        {
            public uint Guid;
            public RectTransform Root;
            public Image Edge, Slot, Portrait, PortraitColour, Mark, Badge, Path;
            /// <summary>
            /// The picture the row's art is drawn by (null: a plain box); what holds the path's badge where it is
            /// on the screen while the row slides; the spot on what is painted of the badge.
            /// </summary>
            public RectTransform Ground, PathHolder, PathSpot;
            /// <summary>The path whose badge hangs on the row.</summary>
            public string PathShown;
            public TextMeshProUGUI Name, Weapon, Armour, Level;
            public RectTransform Experience;
            public readonly Image[] Pips = new Image[RosterPanel.Pips];
            public GameObject Dismiss;
            /// <summary>The pointer on the row; on the badge's hot spot inside it; on the badge of the hero's path.</summary>
            public UiHover Hover, OverBadge, OverPath;
            /// <summary>How far out the row is, and the slide it is on: from, to, and the share of its time gone.</summary>
            public float Slide, SlideFrom, SlideTo, SlideShare = 1f;
            public bool Grey;
            public int Place = -1;
        }

        private readonly List<Row> _rows = new List<Row>();
        private Layout _layout;
        private RectTransform _list;
        private TextMeshProUGUI _count;
        private Dd1Tooltip _tooltip;
        private GameObject _up, _down;
        private RectTransform _sortMark;
        private readonly RectTransform[] _sortButtons = new RectTransform[SortButtons.Length];
        private Sort _sort = Sort.None;
        private bool _descending;
        private int _first;
        private float _nextRefresh;

        public static RosterPanel Build(Transform canvasRoot)
        {
            CanvasRoot = canvasRoot;
            // The column hangs on the screen's right edge whatever the screen's width; inside it DD1's
            // coordinates count from roster_list_pos, y down.
            var root = UiKit.Rect("Roster", canvasRoot);
            root.anchorMin = new Vector2(1f, 0f);
            root.anchorMax = new Vector2(1f, 1f);
            root.pivot = new Vector2(1f, 1f);
            root.offsetMin = new Vector2(-Width, 0f);
            root.offsetMax = Vector2.zero;

            var panel = root.gameObject.AddComponent<RosterPanel>();
            _current = panel;
            var l = panel._layout = Layout.Read();
            var listEnd = l.ElementPos.y + l.ElementMax * l.ElementSpacing.y;

            // Takes the clicks of the town behind the column, and the wheel where no row lies.
            var sprite = Art(Dir + "roster_bggrad.png");
            var back = UiKit.Image("Back", root, sprite, sprite != null ? Color.white : new Color(0.02f, 0.02f, 0.025f, 0.86f), true);
            var backRect = (RectTransform)back.transform;
            backRect.anchorMin = new Vector2(0f, 0f);
            backRect.anchorMax = new Vector2(0f, 1f);
            backRect.pivot = TopLeft;
            backRect.offsetMin = Vector2.zero;
            backRect.offsetMax = new Vector2(GradientWidth, 0f);

            panel._list = UiKit.Rect("List", root);
            panel._list.PlaceTopLeft(l.ElementPos, TopLeft, new Vector2(ElementSize.x, l.ElementMax * l.ElementSpacing.y));

            // The frames lie over the first and the last row's edge.
            UiKit.Art("TopFrame", root, Dir + "roster_topframe.png", new Vector2(l.TopFrame.x, l.ElementPos.y + l.TopFrame.y), FrameSize);
            UiKit.Art("BottomFrame", root, Dir + "roster_bottomframe.png", new Vector2(l.BottomFrame.x, listEnd + l.BottomFrame.y), FrameSize);

            // DD1 has the count ("4/12") at the message offset, left of the sort buttons, and no word beside it (a
            // real frame of its hamlet).
            panel._count = UiKit.Text("Count", root, "", "roster_full", null, TextAlignmentOptions.TopLeft);
            panel._count.textWrappingMode = TextWrappingModes.NoWrap;
            ((RectTransform)panel._count.transform).PlaceTopLeft(l.Message, TopLeft, new Vector2(l.SortStart.x - l.Message.x, 40f));

            for (var i = 0; i < SortButtons.Length; i++)
            {
                var sort = SortButtons[i];
                var at = l.SortStart + i * new Vector2(SortSize + l.SortSpacing.x, l.SortSpacing.y);
                var icon = UiKit.Art("Sort." + SortArt[i], root, Dir + "roster_sort_" + SortArt[i] + ".png", at, new Vector2(SortSize, SortSize), new Color(0.2f, 0.2f, 0.2f), true);
                var button = icon.gameObject.AddComponent<Button>();
                button.targetGraphic = icon;
                button.onClick.AddListener(() => panel.SortBy(sort));
                panel._sortButtons[i] = (RectTransform)icon.transform;
                // DD1 names the order over its button (str_sort_roster_by_level ...): the box stands centred over a point
                // roster_sort_tooltip_offset from the button's corner, as the Trinket Inventory's sort tooltips do.
                var words = WindowText.Plain("str_sort_roster_by_" + SortArt[i]) ?? "Sort by " + SortWords[i];
                UiKit.Hover(icon.gameObject, inside =>
                {
                    if (inside) panel._tooltip?.ShowOver(button, new Vector2(TipRoom, 0f) + at + l.SortTip, null, words);
                    else panel._tooltip?.Hide(button);
                });
            }
            panel._sortMark = (RectTransform)UiKit.Art("SortMark", root, Dir + "roster_sort_current_overlay.png", Vector2.zero, new Vector2(49f, 16f), UiKit.Gold).transform;
            panel._sortMark.gameObject.SetActive(false);

            panel._up = Arrow("ScrollUp", root, Dir + "roster_uparrow.png", new Vector2(l.ElementPos.x + l.ScrollUp.x, l.ElementPos.y + l.ScrollUp.y), () => panel.Scroll(-1));
            panel._down = Arrow("ScrollDown", root, Dir + "roster_downarrow.png", new Vector2(l.ElementPos.x + l.ScrollDown.x, listEnd + l.ScrollDown.y), () => panel.Scroll(1));
            // Over the rows, and free to stand left of the column: the tooltips' frame reaches TipRoom further left
            // than the column (a tooltip keeps inside its frame), so their places count from there.
            var tips = UiKit.Stretch(UiKit.Rect("Tips", root));
            tips.offsetMin = new Vector2(-TipRoom, 0f);
            panel._tooltip = new Dd1Tooltip(tips, TipRoom + Width);
            return panel;
        }

        private static GameObject Arrow(string name, Transform parent, string art, Vector2 at, Action onClick)
        {
            var image = UiKit.Art(name, parent, art, at, ArrowSize, new Color(0.5f, 0.4f, 0.2f), true);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick());
            image.gameObject.SetActive(false);
            return image.gameObject;
        }

        private static Sprite Art(string dd1File) => Dd1Install.Found ? Dd1Install.Sprite(dd1File) : null;

        private void OnEnable()
        {
            _nextRefresh = 0f;
            RosterLifecycle.Changed += RefreshSoon;
        }

        private void OnDisable()
        {
            RosterLifecycle.Changed -= RefreshSoon;
        }

        private void OnDestroy()
        {
            if (_current == this) _current = null;
        }

        // A hero joined or left: do not wait for the next tick.
        private void RefreshSoon()
        {
            _nextRefresh = 0f;
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + RefreshSeconds;
                Refresh();
            }

            // DD1 slides the row under the pointer out to the left, and back when the pointer has gone: each way in
            // seconds_time, by its easing (town_roster_mouseover_element_anim: "easeInQuad", slow at first).
            var step = Time.unscaledDeltaTime / _layout.SlideSeconds;
            foreach (var row in _rows)
            {
                if (row.Place < 0) continue;
                var target = row.Hover.Inside ? _layout.Slide : 0f;
                if (!Mathf.Approximately(row.SlideTo, target))
                {
                    row.SlideFrom = row.Slide;
                    row.SlideTo = target;
                    row.SlideShare = 0f;
                }
                if (row.SlideShare >= 1f) continue;
                row.SlideShare = Mathf.Min(1f, row.SlideShare + step);
                row.Slide = Mathf.Lerp(row.SlideFrom, row.SlideTo, _layout.SlideEasesIn ? row.SlideShare * row.SlideShare : row.SlideShare);
                row.Root.anchoredPosition = new Vector2(-row.Slide, -row.Place * _layout.ElementSpacing.y);
                row.PathHolder.anchoredPosition = new Vector2(row.Slide, 0f);
            }
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (!Mathf.Approximately(eventData.scrollDelta.y, 0f)) Scroll(eventData.scrollDelta.y > 0f ? -1 : 1);
        }

        private void Scroll(int rows)
        {
            var before = _first;
            _first += rows;
            Refresh();
            if (_first != before) EstateAudio.Ui(_first < before ? ScrollUpSound : ScrollDownSound);
        }

        private void SortBy(Sort sort)
        {
            // First click: this order; second: reversed; third: back to the order of arrival.
            if (_sort != sort)
            {
                _sort = sort;
                _descending = false;
            }
            else if (!_descending) _descending = true;
            else _sort = Sort.None;
            EstateAudio.Ui(SortSound);
            Refresh();
        }

        private void Refresh()
        {
            if (!EstateSession.Active) return;
            var library = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance;
            var guids = Sorted(RosterLifecycle.LivingGuids(), library);
            if (!ShowsExactly(guids)) Rebuild(guids, library);

            _first = Mathf.Clamp(_first, 0, Mathf.Max(0, _rows.Count - _layout.ElementMax));
            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                var place = i >= _first && i < _first + _layout.ElementMax ? i - _first : -1;
                if (row.Place != place)
                {
                    row.Place = place;
                    row.Slide = row.SlideFrom = row.SlideTo = 0f;
                    row.SlideShare = 1f;
                    row.Root.gameObject.SetActive(place >= 0);
                    if (place >= 0) row.Root.anchoredPosition = new Vector2(0f, -place * _layout.ElementSpacing.y);
                    row.PathHolder.anchoredPosition = Vector2.zero;
                }
                if (place < 0) continue;
                var actor = library.GetLibraryElement(row.Guid);
                if (actor != null) Show(row, actor);
            }

            var size = StageCoach.RosterSize;
            _count.text = _rows.Count + "/" + size;
            _count.color = _rows.Count >= size ? UiKit.Harmful : Dd1Fonts.Colour("roster_full", UiKit.Gold);
            _up.SetActive(_first > 0);
            _down.SetActive(_first + _layout.ElementMax < _rows.Count);

            var marked = Array.IndexOf(SortButtons, _sort);
            _sortMark.gameObject.SetActive(marked >= 0);
            if (marked >= 0)
            {
                var offset = _descending ? _layout.SortDescending : _layout.SortAscending;
                _sortMark.anchoredPosition = _sortButtons[marked].anchoredPosition + new Vector2(offset.x, -offset.y);
            }
        }

        private void Show(Row row, ActorInstance actor)
        {
            var l = _layout;
            var inParty = EstateSession.IsInParty(row.Guid);
            var blocked = EstateSession.PartyBlockReason(row.Guid);

            row.Name.text = actor.ActorName;
            // DD1's row has no words but the name, and one tooltip: the badge's, with the hero's level, experience and
            // stress, resolve_level_bar_tooltip_offset from the row's corner as the row stands slid out. Class and
            // path, which tell the heroes of one class apart, and the reason a hero cannot embark are the mod's own
            // tooltip for the rest of the row.
            var rowTop = l.ElementPos.y + row.Place * l.ElementSpacing.y;
            ShowPath(row, actor);
            // the path's badge: its name and DD2's words about it, in a box left of the row as it stands slid out
            if (row.OverPath.Inside)
                _tooltip?.ShowLeftOf(row, new Vector2(TipRoom + l.ElementPos.x - l.Slide, rowTop), HeroPathBadge.Name(actor), HeroPathBadge.Words(actor), PathTipWidth, l.ElementSpacing.y);
            else if (row.OverBadge.Inside)
                _tooltip?.ShowLeftOf(row, new Vector2(TipRoom + l.ElementPos.x - l.Slide, rowTop), null, BadgeText(row.Guid, actor), l.BadgeTipWidth, l.ElementSpacing.y);
            else if (row.Hover.Inside)
                _tooltip?.ShowLeftOf(row, new Vector2(TipRoom + l.ElementPos.x - l.Slide, rowTop), HeroNames.Title(actor), blocked != null ? Dd1Ui.Tint(blocked, "harmful") : null, thingHeight: l.ElementSpacing.y);
            else _tooltip?.Hide(row);

            // DD1 dims the portrait of a hero who cannot be picked and takes most of its colour out
            // (town_roster_portrait_unselectable: darkness 0.4, saturation 0.2): the game's black-and-white portrait
            // with that share of the coloured one over it, both at that light. The game drops portrait art it thinks
            // nobody uses (leaving the estate and coming back does it): fetch it again when it is gone.
            // A hero of the party is dimmed the same way under the party's seal (DD1's own roster, with a party
            // formed on the Estate Map and in the hamlet alike: their portraits at about half their light).
            var grey = blocked != null || inParty;
            if (row.Portrait.sprite == null || row.Grey != grey)
            {
                row.Grey = grey;
                row.Portrait.sprite = HeroNames.Portrait(actor.ActorDataId, grey);
                row.PortraitColour.sprite = grey ? HeroNames.Portrait(actor.ActorDataId) : null;
            }
            row.Portrait.color = row.Portrait.sprite == null ? Color.clear : grey ? new Color(l.Darkness, l.Darkness, l.Darkness) : Color.white;
            row.PortraitColour.color = grey && row.PortraitColour.sprite != null ? new Color(l.Darkness, l.Darkness, l.Darkness, l.Saturation) : Color.clear;
            var shift = FaceShift(row.Portrait.sprite);
            ((RectTransform)row.Portrait.transform).anchoredPosition = shift;
            ((RectTransform)row.PortraitColour.transform).anchoredPosition = shift;
            // the slot itself is the same for everybody on DD1's roster: the seal and the dimmed face tell the party
            Swap(row.Slot, SlotDir + "hero_slot.background.png");

            // The party's seal, the sign of the building a hero is busy in, DD1's question mark for the missing.
            string mark = null;
            var markAt = l.MarkIcon;
            if (inParty) mark = Dir + "party.icon_roster.png";
            else if (SanitariumLedger.StayOf(row.Guid) != null) mark = BuildingIcon(SanitariumRules.Building, ref markAt);
            else if (ActivityLedger.StayOf(row.Guid) is ActivityLedger.Stay stay) mark = BuildingIcon(stay.Building, ref markAt);
            else if (ActivityLedger.MissingWeeks(row.Guid) > 0) mark = Dir + "missing.icon_roster.png";
            else if (blocked != null)
            {
                mark = SlotDir + "hero_slot.negative_frame.png";
                markAt = l.Portrait;
            }
            var markSprite = mark != null ? Art(mark) : null;
            row.Mark.enabled = markSprite != null;
            if (markSprite != null)
            {
                row.Mark.sprite = markSprite;
                ((RectTransform)row.Mark.transform).PlaceTopLeft(markAt, TopLeft, markSprite.rect.size);
            }

            var stress = Mathf.RoundToInt(Pips * Mathf.Clamp01(actor.Stress / Mathf.Max(1f, actor.StressMax)));
            for (var i = 0; i < Pips; i++)
            {
                var full = i < stress;
                var pip = row.Pips[i];
                var sprite = Art(full ? "overlays/stress_pip_full.png" : "overlays/stress_pip_empty.png");
                if (pip.sprite != sprite) pip.sprite = sprite;
                pip.color = sprite != null ? Color.white : full ? new Color(0.86f, 0.86f, 0.9f) : new Color(0.14f, 0.14f, 0.14f);
                // The lit pip is a pixel shorter at either end than the socket it sits in.
                ((RectTransform)pip.transform).PlaceTopLeft(l.Stress + i * l.StressSpacing + (full ? new Vector2(0f, 1f) : Vector2.zero), TopLeft, full ? PipFull : PipEmpty);
            }
            // DD1's bar under the badge: the way to the next resolve level, filling from the bottom
            row.Experience.sizeDelta = new Vector2(l.BarFillSize.x, l.BarFillSize.y * Resolve.Progress(row.Guid));

            row.Weapon.text = Blacksmith.Level(row.Guid, Blacksmith.Gear.Weapon).ToString();
            row.Armour.text = Blacksmith.Level(row.Guid, Blacksmith.Gear.Armour).ToString();

            var level = Mathf.Clamp(Resolve.Level(row.Guid), 0, Resolve.MaxLevel);
            row.Level.text = level.ToString();
            Swap(row.Badge, ResolveDir + "resolve_level_bar_number_background_lvl" + level + ".png");
            // The row's edge takes the colour of the resolve level; level 0 has none.
            var edge = level > 0 ? RowArt(Dir + "rosterelement_res" + level + ".png") : null;
            row.Edge.enabled = edge != null;
            if (edge != null && row.Edge.sprite != edge.Sprite)
            {
                row.Edge.sprite = edge.Sprite;
                ((RectTransform)row.Edge.transform).PlaceTopLeft(new Vector2(0f, edge.Top), TopLeft, edge.Size);
            }

            // the estate's last hero keeps the control: DD1 answers it with its refusal
            var noDismissal = row.Hover.Inside ? RosterLifecycle.DismissBlockReason(row.Guid) : "";
            var dismissable = noDismissal == null || noDismissal == RosterLifecycle.LastHeroStays;
            if (row.Dismiss.activeSelf != dismissable) row.Dismiss.SetActive(dismissable);
        }

        // DD2's badge of the hero's path, at its picture's own proportions; the pointer's spot lies on what is
        // painted of it (a class's own path has a short strip of parchment and clear field under it). The game
        // drops art it thinks nobody uses: a picture that has gone is fetched again.
        private static void ShowPath(Row row, ActorInstance actor)
        {
            var path = HeroPaths.Of(actor);
            var art = HeroPathBadge.Art(path);
            if (row.PathShown == path && row.Path.sprite == art) return;
            row.PathShown = path;
            row.Path.sprite = art;
            row.Path.color = art != null ? Color.white : Color.clear;
            row.PathSpot.gameObject.SetActive(art != null);
            if (art == null) return;
            var size = new Vector2(PathHeight * art.rect.width / Mathf.Max(1f, art.rect.height), PathHeight);
            ((RectTransform)row.Path.transform).PlaceTopLeft(new Vector2(PathMiddle, PathTop), TopCentre, size);
            var painted = HeroPathBadge.Painted(art);
            row.PathSpot.PlaceTopLeft(new Vector2(PathMiddle - size.x * 0.5f + painted.x * size.x, PathTop + painted.y * size.y), TopLeft,
                new Vector2(painted.width * size.x, painted.height * size.y));
        }

        // localization: resolve_bar_tooltip_level_line_format "Level: %d", _xp_line_format "Resolve XP: %d/%d",
        // _additional_stress_format "Stress: %d/%d" with the word in DD1's colour for stress. The experience is the
        // hero's against what the next level asks; the stress is DD2's own count.
        private static string BadgeText(uint guid, ActorInstance actor)
        {
            var experience = Resolve.Experience(guid);
            return ActivityLogText.Story("resolve_bar_tooltip_level_line_format", "Level: %d", Resolve.Level(guid)) + "\n"
                   + ActivityLogText.Story("resolve_bar_tooltip_xp_line_format", "Resolve XP: %d/%d", experience, experience + Resolve.ToNextLevel(guid)) + "\n"
                   + ActivityLogText.Story("resolve_bar_tooltip_additional_stress_format", "{colour_start|stress}Stress:{colour_end} %d/%d",
                       Mathf.RoundToInt(actor.Stress), Mathf.RoundToInt(actor.StressMax));
        }

        // DD1 has a roster sign for the buildings that keep a hero (abbey, sanitarium, tavern).
        private string BuildingIcon(string building, ref Vector2 at)
        {
            var file = BuildingsDir + building + "/" + building + ".icon_roster.png";
            if (!Dd1Install.Found || !Dd1Install.Exists(file)) return null;
            at = _layout.BuildingIcon;
            return file;
        }

        private static void Swap(Image image, string dd1File)
        {
            var sprite = Art(dd1File);
            if (sprite != null && image.sprite != sprite) image.sprite = sprite;
        }

        // ---- a row's art, edge to edge -------------------------------------------------------------------

        /// <summary>A row's picture cut to its painted rows: the sprite, where its first painted row lies in the art, its size.</summary>
        private class RowPicture
        {
            public Sprite Sprite;
            public float Top;
            public Vector2 Size;
        }

        private static readonly Dictionary<string, RowPicture> RowArts = new Dictionary<string, RowPicture>();

        /// <summary>
        /// DD1's art of a row (rosterelement.background.png and the rosterelement_res*.png edges, 395x104) keeps
        /// three clear rows above its upper rule and four under its lower one, and rows stand element_spacing
        /// (97) apart: a row's lower rule ends on the very line the next row's upper rule begins on. Drawn 1:1,
        /// as DD1 draws it, the two rules are one band. On a window that is not 1080 high the canvas is scaled
        /// (0.89 at 1706x960) and that line falls inside a screen pixel: there the upper picture is already
        /// fading into its clear rows and the lower one still fading in out of its own, both are half clear, and
        /// a quarter of what lies behind the roster shows through: a hairline between the rows, bright where the
        /// town behind is bright.
        ///
        /// So a row is not drawn as the whole art but as its painted rows alone (97 of them, as many as rows are
        /// apart), from a copy of the art whose clear rows repeat the painted row next to them: at its upper and
        /// lower edge the picture has nothing clear to fade into, and where two rows meet every screen pixel
        /// belongs to one of them whatever the scale. Nothing moves: each painted row stays where DD1 has it.
        /// Null (the caller keeps a plain box) when the art cannot be read.
        /// </summary>
        private static RowPicture RowArt(string dd1File)
        {
            if (RowArts.TryGetValue(dd1File, out var known) && (known == null || known.Sprite != null)) return known;
            RowPicture made = null;
            Texture2D texture = null;
            try
            {
                texture = Dd1Install.Found && Dd1Install.Exists(dd1File) ? Dd1Install.LoadTexture(dd1File, true, false) : null;
                if (texture != null)
                {
                    int width = texture.width, height = texture.height;
                    var pixels = texture.GetPixels32();
                    // a texture's rows count from the bottom. A row is painted when something in it is solid: the
                    // art has a dozen stray pixels of alpha 1 to 4 in the two rows under its lower rule
                    int low = -1, high = -1;
                    for (var y = 0; y < height; y++)
                    {
                        var painted = false;
                        for (var x = 0; x < width && !painted; x++) painted = pixels[y * width + x].a > 127;
                        if (!painted) continue;
                        if (low < 0) low = y;
                        high = y;
                    }
                    if (low >= 0)
                    {
                        for (var y = 0; y < height; y++)
                        {
                            var from = y < low ? low : y > high ? high : y;
                            if (from != y) Array.Copy(pixels, from * width, pixels, y * width, width);
                        }
                        texture.SetPixels32(pixels);
                        texture.Apply(false, true);
                        var sprite = Sprite.Create(texture, new Rect(0f, low, width, high - low + 1), Middle, 100f, 0, SpriteMeshType.FullRect);
                        sprite.name = dd1File + " (painted rows)";
                        sprite.hideFlags = HideFlags.HideAndDontSave;
                        made = new RowPicture { Sprite = sprite, Top = height - 1 - high, Size = new Vector2(width, high - low + 1) };
                        texture = null;     // the sprite's now
                    }
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Roster: the row art " + dd1File + " could not be cut to its painted rows: " + e.Message); }
            if (texture != null) Destroy(texture);
            return RowArts[dd1File] = made;
        }

        // ---- order -------------------------------------------------------------------------------------

        private List<uint> Sorted(List<uint> guids, Library<uint, ActorInstance> library)
        {
            if (_sort == Sort.None) return guids;
            var arrival = new Dictionary<uint, int>();
            for (var i = 0; i < guids.Count; i++) arrival[guids[i]] = i;
            var sign = _descending ? -1 : 1;
            guids.Sort((a, b) =>
            {
                var order = sign * Compare(a, b, library);
                return order != 0 ? order : arrival[a].CompareTo(arrival[b]);
            });
            return guids;
        }

        private int Compare(uint a, uint b, Library<uint, ActorInstance> library)
        {
            var first = library.GetLibraryElement(a);
            var second = library.GetLibraryElement(b);
            if (first == null || second == null) return (first == null).CompareTo(second == null);
            switch (_sort)
            {
                case Sort.Class: return string.Compare(HeroNames.Title(first), HeroNames.Title(second), StringComparison.CurrentCulture);
                case Sort.Level: return Resolve.Level(a).CompareTo(Resolve.Level(b));
                case Sort.Stress: return first.Stress.CompareTo(second.Stress);
                default: return Activity(a).CompareTo(Activity(b));
            }
        }

        // The party first, then the idle, then whoever is busy or missing.
        private static int Activity(uint guid)
        {
            if (EstateSession.IsInParty(guid)) return 0;
            return EstateSession.PartyBlockReason(guid) == null ? 1 : 2;
        }

        private bool ShowsExactly(List<uint> guids)
        {
            if (guids.Count != _rows.Count) return false;
            for (var i = 0; i < guids.Count; i++)
                if (_rows[i].Guid != guids[i]) return false;
            return true;
        }

        // ---- rows --------------------------------------------------------------------------------------

        private void Rebuild(IReadOnlyList<uint> guids, Library<uint, ActorInstance> library)
        {
            for (var i = _list.childCount - 1; i >= 0; i--) Destroy(_list.GetChild(i).gameObject);
            _rows.Clear();
            for (var i = 0; i < guids.Count; i++)
            {
                var actor = library.GetLibraryElement(guids[i]);
                if (actor == null) continue;
                _rows.Add(BuildRow(actor));
            }
        }

        private Row BuildRow(ActorInstance actor)
        {
            var l = _layout;
            var guid = actor.ActorGuid;
            var row = new Row { Guid = guid };

            // The row itself is clear glass of the art's size, which takes the pointer; its picture is drawn by
            // "Ground", edge to edge of what is painted, so that it meets the rows above and below (RowArt).
            var art = RowArt(Dir + "rosterelement.background.png");
            var back = UiKit.Image("Hero" + guid, _list, null, art != null ? Color.clear : new Color(0.03f, 0.03f, 0.035f, 0.95f), true);
            row.Root = ((RectTransform)back.transform).PlaceTopLeft(Vector2.zero, TopLeft, ElementSize);
            if (art != null) row.Ground = ((RectTransform)UiKit.Image("Ground", row.Root, art.Sprite).transform).PlaceTopLeft(new Vector2(0f, art.Top), TopLeft, art.Size);
            back.gameObject.AddComponent<RosterRowClick>().Guid = guid;
            row.Hover = UiKit.Hover(back.gameObject, inside => _nextRefresh = 0f);
            row.Root.gameObject.SetActive(false);

            // Slid out, the row would leave a pointer that rests near the screen's edge, slide back under it
            // and out again: it keeps a strip of its own to be hit on there.
            var reach = UiKit.Image("Reach", row.Root, null, Color.clear, true);
            ((RectTransform)reach.transform).PlaceTopLeft(new Vector2(ElementSize.x, 0f), TopLeft, new Vector2(l.Slide, ElementSize.y));

            // the edge of the hero's resolve level is the same picture in another colour: placed by its own painted rows (Show)
            row.Edge = UiKit.Image("Edge", row.Root, null);
            row.Edge.enabled = false;

            row.Slot = UiKit.Art("Slot", row.Root, SlotDir + "hero_slot.background.png", l.Portrait, new Vector2(SlotSize, SlotSize), new Color(0.07f, 0.07f, 0.07f));
            // DD1's portraits are faces that fill their square. DD2's are heads on clear ground with a wide
            // margin around them: drawn larger than the slot and cut off at its frame, they fill it as well.
            // DD1's roster portrait is an opaque square (a grey rim, a black field) that hides the slot under it, the
            // gold one of a party member too: the seal and ribbon alone mark the party.
            var rim = UiKit.Image("PortraitRim", row.Root, null, new Color32(72, 72, 72, 255));
            ((RectTransform)rim.transform).PlaceTopLeft(l.Portrait, TopLeft, new Vector2(SlotSize, SlotSize));
            var field = UiKit.Image("PortraitField", row.Root, null, Color.black);
            ((RectTransform)field.transform).PlaceTopLeft(l.Portrait + new Vector2(PortraitEdge, PortraitEdge), TopLeft, new Vector2(SlotSize - 2f * PortraitEdge, SlotSize - 2f * PortraitEdge));
            var window = UiKit.Rect("PortraitWindow", row.Root);
            window.PlaceTopLeft(l.Portrait + new Vector2(PortraitInset, PortraitInset), TopLeft, new Vector2(SlotSize - 2f * PortraitInset, SlotSize - 2f * PortraitInset));
            window.gameObject.AddComponent<RectMask2D>();
            row.Portrait = UiKit.Image("Portrait", window, null, Color.clear);
            row.Portrait.preserveAspect = true;
            ((RectTransform)row.Portrait.transform).Place(Middle, Middle, Vector2.zero, new Vector2(PortraitSize, PortraitSize));
            // what colour is left to the portrait of a hero who cannot be picked (see Show)
            row.PortraitColour = UiKit.Image("PortraitColour", window, null, Color.clear);
            row.PortraitColour.preserveAspect = true;
            ((RectTransform)row.PortraitColour.transform).Place(Middle, Middle, Vector2.zero, new Vector2(PortraitSize, PortraitSize));
            row.Mark = UiKit.Image("Mark", row.Root, null);
            row.Mark.enabled = false;

            row.Name = UiKit.Text("Name", row.Root, "", "town_roster_name", null, TextAlignmentOptions.TopLeft);
            row.Name.textWrappingMode = TextWrappingModes.NoWrap;
            row.Name.enableAutoSizing = true;
            row.Name.fontSizeMax = row.Name.fontSize;
            row.Name.fontSizeMin = NameSmallest;
            ((RectTransform)row.Name.transform).PlaceTopLeft(l.Name, TopLeft, new Vector2(l.Badge.x - l.Name.x - 2f, 40f));

            for (var i = 0; i < Pips; i++) row.Pips[i] = UiKit.Image("Pip" + i, row.Root, null);

            row.Weapon = Number("Weapon", row.Root, l.Weapon);
            row.Armour = Number("Armour", row.Root, l.Armour);

            // DD1's experience bar (shared/resolve_level_bar): a strip of resolve_gradient_* that grows from the bottom
            // of a slot, behind a mask with that slot cut out of it, under the badge.
            var fillAt = l.Badge + l.Bar + l.BarFill;
            var fill = UiKit.Image("Experience", row.Root, null, Color.white);
            // The slot's gradient runs from resolve_gradient_bottom at its foot to resolve_gradient_top at its head
            // (gradient_size); the strip shows as much of it as the hero has come.
            var gradient = fill.gameObject.AddComponent<RisingGradient>();
            gradient.Bottom = Dd1Fonts.Colour("resolve_gradient_bottom", new Color32(0x6d, 0x6a, 0x5e, 255));
            gradient.Top = Dd1Fonts.Colour("resolve_gradient_top", new Color32(0xae, 0xa9, 0x96, 255));
            gradient.Height = l.BarFillSize.y;
            row.Experience = (RectTransform)fill.transform;
            row.Experience.Place(TopLeft, new Vector2(0f, 0f), new Vector2(fillAt.x, -(fillAt.y + l.BarFillSize.y)), new Vector2(l.BarFillSize.x, 0f));
            UiKit.Art("ExperienceMask", row.Root, ResolveDir + "resolve_level_bar_mask.png", l.Badge + l.Bar + l.BarMaskAt, BarMask);

            row.Badge = UiKit.Art("Badge", row.Root, ResolveDir + "resolve_level_bar_number_background_lvl0.png", l.Badge + l.BadgeBack, new Vector2(BadgeSize, BadgeSize), new Color(0.2f, 0.2f, 0.2f));
            row.Level = UiKit.Text("Level", row.Root, "", "resolve_number", Dd1Fonts.Colour("resolve_number", Color.black));
            row.Level.textWrappingMode = TextWrappingModes.NoWrap;
            ((RectTransform)row.Level.transform).PlaceTopLeft(l.Badge + l.BadgeNumber, Middle, new Vector2(40f, 28f));

            // DD1's hot spot for the badge's tooltip (resolve_level_tooltip: hot_spot_pos, hot_spot_size), clear glass
            // over the badge; a click on it is still a click on the row.
            var spot = UiKit.Image("BadgeSpot", row.Root, null, Color.clear, true);
            ((RectTransform)spot.transform).PlaceTopLeft(l.Badge + l.BadgeSpot, TopLeft, l.BadgeSpotSize);
            row.OverBadge = UiKit.Hover(spot.gameObject, inside => _nextRefresh = 0f);

            // Its own click target in the row's corner: the row itself still toggles the party. Shown while the
            // pointer rests on the row of a hero who may be sent away.
            var dismiss = UiKit.Art("Dismiss", row.Root, "shared/character/icon_dismiss.png", DismissPos, new Vector2(DismissSize, DismissSize), new Color(0.36f, 0.08f, 0.06f, 0.9f), true);
            var button = dismiss.gameObject.AddComponent<Button>();
            button.targetGraphic = dismiss;
            button.onClick.AddListener(() => RosterLifecycle.RequestDismiss(guid));
            row.Dismiss = dismiss.gameObject;
            row.Dismiss.SetActive(false);

            // The hero's path: DD2's own badge of it (Show gives it its picture, size and spot). It lies over the
            // dismiss control, which the row's slide brings out from under it, and its spot over the resolve badge's
            // wide hot spot; a click on it is still a click on the row. The holder is moved against the row's slide.
            row.PathHolder = UiKit.Rect("PathBadge", row.Root).PlaceTopLeft(Vector2.zero, TopLeft, Vector2.zero);
            row.Path = UiKit.Image("Path", row.PathHolder, null, Color.clear);
            row.Path.preserveAspect = true;
            var pathSpot = UiKit.Image("PathSpot", row.PathHolder, null, Color.clear, true);
            row.PathSpot = (RectTransform)pathSpot.transform;
            row.OverPath = UiKit.Hover(pathSpot.gameObject, inside => _nextRefresh = 0f);
            pathSpot.gameObject.SetActive(false);
            return row;
        }

        // ---- dev bridge ----------------------------------------------------------------------------------

        /// <summary>
        /// The rows on show, top to bottom: where each stands, the rows of the art its picture is drawn by and how
        /// far its picture begins under the end of the row above (0: they meet), and the badge of the hero's path
        /// (DD2's picture, its place in the row, the spot the pointer finds it by, what the pointer is told).
        /// </summary>
        // ---- a portrait in the middle of its slot ---------------------------------------------------------

        /// <summary>
        /// DD2 does not paint every HEAD into the middle of its portrait: the highwayman's stands high, so that
        /// the slot cut his hair off, the grave robber's face low under a hat that fills the top, the jester's to
        /// the right. Drawn large in the slot, such a head stood off centre, and at the left it went under the
        /// party's seal and ribbon (the owner, 2026-10-06: "centre some of the icons, the highwayman and the grave
        /// robber for instance; look through who should be moved"). Each such portrait is moved by a few pixels,
        /// by eye, class by class: the numbers below are pixels of the slot at the portrait's stock size, to the
        /// right and up. (The middle of a portrait's PAINT was tried first and is no guide: scarves, cloaks and
        /// beaks weigh as much as the face; it moved the bounty hunter's helmet out of the middle.)
        /// The first three are the owner's own picks from sheets of fifteen shifts each, side by side: the
        /// highwayman straight DOWN till his whole head is in the slot (moved sideways he was "badly done"), the
        /// grave robber up, the runaway nearly where DD2 painted her (her hood is in the middle, not her face).
        /// </summary>
        public static bool CentreFaces = true;
        private const float NudgedAt = 136f;
        private static readonly Dictionary<string, Vector2> FaceNudge = new Dictionary<string, Vector2>
        {
            // the sprite's name ends with the class: portraits-color-highwayman, portraits-color-graverobber ...
            { "highwayman", new Vector2(0f, -14f) },
            { "graverobber", new Vector2(9f, 11f) },
            { "runaway", new Vector2(2f, 3f) },
            { "jester", new Vector2(-8f, 0f) },
            { "hellion", new Vector2(-5f, 0f) },
            { "occultist", new Vector2(4f, 0f) },
            { "vestal", new Vector2(-4f, 0f) },
            { "manatarms", new Vector2(3f, 0f) },
            { "abomination_human", new Vector2(-3f, 0f) },
        };

        /// <summary>How far a portrait is moved (the slot's pixels, y up) so that the face is in the slot's middle.</summary>
        internal static Vector2 FaceShift(Sprite sprite)
        {
            if (sprite == null || !CentreFaces) return Vector2.zero;
            var name = sprite.name;
            var cut = name.LastIndexOf('-');
            if (cut < 0 || !FaceNudge.TryGetValue(name.Substring(cut + 1), out var nudge)) return Vector2.zero;
            // never so far that the picture's own edge comes into the slot's window
            var most = Mathf.Max(0f, (PortraitSize - (SlotSize - 2f * PortraitInset)) * 0.5f);
            var scaled = nudge * (PortraitSize / NudgedAt);
            return new Vector2(Mathf.Clamp(scaled.x, -most, most), Mathf.Clamp(scaled.y, -most, most));
        }

        /// <summary>
        /// Dev: a class's nudge is set (by the ending of its sprite's name: highwayman, graverobber ...) and the
        /// rows that are up are moved by it; without numbers it only tells. For trying numbers in a running game.
        /// </summary>
        public static object Nudge(string cls, float? x, float? y)
        {
            if (!string.IsNullOrEmpty(cls) && x.HasValue && y.HasValue)
            {
                FaceNudge[cls] = new Vector2(x.Value, y.Value);
                if (_current != null)
                    foreach (var row in _current._rows)
                    {
                        if (row.Portrait == null) continue;
                        var shift = FaceShift(row.Portrait.sprite);
                        ((RectTransform)row.Portrait.transform).anchoredPosition = shift;
                        if (row.PortraitColour != null) ((RectTransform)row.PortraitColour.transform).anchoredPosition = shift;
                    }
            }
            var told = new Dictionary<string, float[]>();
            foreach (var pair in FaceNudge) told[pair.Key] = new[] { pair.Value.x, pair.Value.y };
            return told;
        }

        /// <summary>
        /// Dev: the portraits of the classes named, each in a slot as a row draws it, in a strip at the screen's
        /// left: every class can be looked at without a hero of it in the estate. Null or empty takes it away.
        /// A cell can be given a shift of its own in place of its class's (the same class several times over, each
        /// moved differently, side by side), and the party's seal can be laid on every cell as a row lays it.
        /// </summary>
        public static object Faces(IList<string> classes, IList<Vector2?> shifts = null, bool seal = false)
        {
            if (CanvasRoot == null) return "no hamlet";
            var old = CanvasRoot.Find("DevFaces");
            if (old != null) Destroy(old.gameObject);
            if (classes == null || classes.Count == 0) return "cleared";
            var strip = UiKit.Rect("DevFaces", CanvasRoot);
            strip.anchorMin = strip.anchorMax = strip.pivot = TopLeft;
            strip.anchoredPosition = new Vector2(40f, -150f);
            const int perRow = 5;
            const float step = SlotSize + 12f;
            strip.sizeDelta = new Vector2(perRow * step, Mathf.Ceil(classes.Count / (float)perRow) * step);
            var ground = UiKit.Image("Ground", strip, null, new Color(0f, 0f, 0f, 0.85f));
            UiKit.Stretch((RectTransform)ground.transform);
            var told = new List<object>();
            for (var i = 0; i < classes.Count; i++)
            {
                var at = new Vector2(6f + (i % perRow) * step, 6f + (i / perRow) * step);
                var rim = UiKit.Image("Rim" + i, strip, null, new Color32(72, 72, 72, 255));
                ((RectTransform)rim.transform).PlaceTopLeft(at, TopLeft, new Vector2(SlotSize, SlotSize));
                var field = UiKit.Image("Field" + i, strip, null, Color.black);
                ((RectTransform)field.transform).PlaceTopLeft(at + new Vector2(PortraitEdge, PortraitEdge), TopLeft, new Vector2(SlotSize - 2f * PortraitEdge, SlotSize - 2f * PortraitEdge));
                var window = UiKit.Rect("Window" + i, strip);
                window.PlaceTopLeft(at + new Vector2(PortraitInset, PortraitInset), TopLeft, new Vector2(SlotSize - 2f * PortraitInset, SlotSize - 2f * PortraitInset));
                window.gameObject.AddComponent<RectMask2D>();
                var sprite = HeroNames.Portrait(classes[i]);
                var face = UiKit.Image("Face" + i, window, sprite, sprite != null ? Color.white : Color.clear);
                face.preserveAspect = true;
                var shift = shifts != null && i < shifts.Count && shifts[i].HasValue ? shifts[i].Value : FaceShift(sprite);
                ((RectTransform)face.transform).Place(Middle, Middle, shift, new Vector2(PortraitSize, PortraitSize));
                var sealArt = seal ? Art(Dir + "party.icon_roster.png") : null;
                if (sealArt != null)
                {
                    // where a row lays it: DD1's non_building_icon_offset against its portrait_icon_offset
                    var mark = UiKit.Image("Seal" + i, strip, sealArt);
                    ((RectTransform)mark.transform).PlaceTopLeft(at + new Vector2(-7f, 1f), TopLeft, sealArt.rect.size);
                }
                told.Add(new { cls = classes[i], sprite = sprite != null ? sprite.name : null, shiftX = shift.x, shiftY = shift.y });
            }
            return told;
        }

        /// <summary>The size the rows' portraits are drawn at (dev: set it and see the rows that are up change).</summary>
        public static float PortraitsAt(float? size)
        {
            if (size.HasValue && size.Value > 10f)
            {
                PortraitSize = size.Value;
                if (_current != null)
                    foreach (var row in _current._rows)
                    {
                        if (row.Portrait != null) ((RectTransform)row.Portrait.transform).sizeDelta = new Vector2(PortraitSize, PortraitSize);
                        if (row.PortraitColour != null) ((RectTransform)row.PortraitColour.transform).sizeDelta = new Vector2(PortraitSize, PortraitSize);
                    }
            }
            return PortraitSize;
        }

        public static object DescribeRows()
        {
            var panel = _current;
            if (panel == null) return "no roster column";
            var library = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance;
            var rows = new List<object>();
            var shown = new List<Row>();
            foreach (var row in panel._rows)
                if (row.Place >= 0) shown.Add(row);
            shown.Sort((a, b) => a.Place.CompareTo(b.Place));
            float? end = null;
            foreach (var row in shown)
            {
                var actor = library.GetLibraryElement(row.Guid);
                var top = -row.Root.anchoredPosition.y + (row.Ground != null ? -row.Ground.anchoredPosition.y : 0f);
                var height = row.Ground != null ? row.Ground.rect.height : row.Root.rect.height;
                var path = (RectTransform)row.Path.transform;
                rows.Add(new
                {
                    guid = row.Guid, name = actor != null ? actor.ActorName : null, place = row.Place, slid = row.Slide,
                    picture = row.Ground != null ? new[] { top, top + height } : null, gapAbove = end.HasValue ? top - end.Value : (float?)null,
                    path = row.PathShown, pathName = actor != null ? HeroPathBadge.Name(actor) : null,
                    badge = row.Path.sprite != null ? row.Path.sprite.name : null,
                    // in the row's own pixels as it stands (the badge keeps its place on the screen while the row slides)
                    badgeAt = row.Path.sprite != null ? new[] { row.PathHolder.anchoredPosition.x + path.anchoredPosition.x - path.rect.width * 0.5f, -path.anchoredPosition.y, path.rect.width, path.rect.height } : null,
                    badgeSpot = row.PathSpot.gameObject.activeSelf
                        ? new[] { row.PathHolder.anchoredPosition.x + row.PathSpot.anchoredPosition.x, -row.PathSpot.anchoredPosition.y, row.PathSpot.rect.width, row.PathSpot.rect.height }
                        : null,
                    dismissAt = new[] { DismissPos.x, DismissPos.y, DismissSize, DismissSize }, dismissShown = row.Dismiss.activeSelf,
                    pointerOnBadge = row.OverPath.Inside, told = actor != null ? HeroPathBadge.Words(actor) : null
                });
                end = top + height;
            }
            return new { spacing = panel._layout.ElementSpacing.y, rows };
        }

        private static TextMeshProUGUI Number(string name, Transform parent, Vector2 at)
        {
            var label = UiKit.Text(name, parent, "", "town_roster_number", null, TextAlignmentOptions.TopLeft);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            ((RectTransform)label.transform).PlaceTopLeft(at, TopLeft, new Vector2(30f, 28f));
            return label;
        }
    }

    internal class RosterRowClick : MonoBehaviour, IPointerClickHandler
    {
        public uint Guid;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right)
            {
                UpgradeUi.ShowSheet(Guid);
                return;
            }
            // DD1's two sounds of a hero who does not join the party: one who may not go (busy in a building, or
            // refusing the quest chosen on the map), and a party that is full. DD1's exe names them
            // /ui/town/character_show_restriction and /ui/town/character_add_full.
            if (EstateSession.InHub && EstateSession.View == EstateSession.Screen.Hamlet && !EstateSession.IsInParty(Guid))
            {
                if (EstateSession.PartyBlockReason(Guid) != null) EstateAudio.Ui("ui/town/character_show_restriction");
                else if (EstateSession.PartySize >= Assets.Code.Roster.RosterManager.FULL_PARTY_SIZE) EstateAudio.Ui("ui/town/character_add_full");
            }
            EstateSession.ToggleParty(Guid);
        }
    }

    /// <summary>
    /// Colours a graphic from one colour at its foot to another <see cref="Height"/> above it, whatever the
    /// graphic's own height: DD1's experience bar is a strip of such a gradient.
    /// </summary>
    internal class RisingGradient : BaseMeshEffect
    {
        public Color Bottom = Color.white, Top = Color.white;
        public float Height;

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || Height <= 0f) return;
            var foot = ((RectTransform)transform).rect.yMin;
            var vertex = default(UIVertex);
            for (var i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref vertex, i);
                vertex.color = Color.Lerp(Bottom, Top, Mathf.Clamp01((vertex.position.y - foot) / Height));
                vh.SetUIVertex(vertex, i);
            }
        }
    }

    /// <summary>Display helpers for DD2 heroes.</summary>
    internal static class HeroNames
    {
        public static Sprite Portrait(ActorInstance actor) => Portrait(actor.ActorDataId);

        /// <summary>The portrait of a hero class, also when nobody of that class exists right now.</summary>
        public static Sprite Portrait(string classId, bool greyscale = false)
        {
            if (string.IsNullOrEmpty(classId)) return null;
            var resource = Singleton<ResourceDatabaseActors>.Instance.GetResource(classId, isErrorValid: false)?.GetTopMostParent();
            if (resource == null) return null;
            var sprite = greyscale ? resource.GetPortraitIconByType(ResourceActor.PortraitIconType.BlackAndWhite) : null;
            return sprite != null ? sprite : resource.GetPortraitIconByType(ResourceActor.PortraitIconType.Color);
        }

        public static string ClassName(ActorInstance actor) => ClassName(actor.ActorDataId);

        /// <summary>Class and path, which together tell the heroes of an estate apart: "Highwayman, Sharpshot".</summary>
        public static string Title(ActorInstance actor) => Title(actor.ActorDataId, HeroPaths.Of(actor));

        public static string Title(string classId, string pathId)
        {
            var path = HeroPaths.Name(pathId, classId);
            return string.IsNullOrEmpty(path) ? ClassName(classId) : ClassName(classId) + ", " + path;
        }

        /// <summary>
        /// The class's name as the game shows it (the class id is its own key in the string table, see
        /// ActorDescription.GetClassName); "man_at_arms" → "Man at Arms" when the table has no such entry.
        /// </summary>
        public static string ClassName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            var localized = Singleton<Localization>.Instance.TryGetString(id);
            if (!string.IsNullOrEmpty(localized)) return localized;
            var words = id.Split('_');
            for (var i = 0; i < words.Length; i++)
            {
                if (words[i].Length == 0 || words[i] == "at") continue;
                words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
            }
            return string.Join(" ", words);
        }
    }
}
