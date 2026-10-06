using System;
using System.Collections.Generic;
using DD2Estate.Dd1;
using DD2Estate.Dd2;
using DD2Estate.UI;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Estate
{
    /// <summary>
    /// DD1's quick navigation: the column of small building pictures that stands left of a building's screen
    /// and leads from any building straight to any other. Its place is town.layout.darkest town_screen_layout
    /// button_navigation_pos (it slides in from button_navigation_offscreen_pos in building_animation
    /// quick_nav_transition_time), its insides are campaign/town/building_navigation/
    /// building_navigation.layout.darkest: one &lt;id&gt;.town_button.png (56 px) every button_spacing, in the
    /// order of the buildings' button_index. The building whose screen is open has its button in full colour
    /// (selected_hop_offset is a hop DD1 plays, not where it stands); the others wear DD1's colour town_navigation_button_unselected
    /// (darker, nearly grey). A building the estate has not earned (<see cref="BuildingLocks"/>) carries
    /// bld_quick_nav_locked_icon.png at locked_overlay_offset and does not open; one with news
    /// (<see cref="UpgradeAlerts"/>) carries DD1's exclamation mark at exclamation_point_offset.
    ///
    /// A button's place is its corner, and so is the padlock's; the exclamation mark stands with its middle at
    /// exclamation_point_offset (28 32 of the 56 px button) at its own full size, 24x60 in a 74x77 glow: it
    /// covers the button, as on DD1's own screen (a frame of its Stage Coach: the marks' middles 28.75, 31.4
    /// from the buttons' corners). The button of the building on show never carries one. The mod's own: the button under the pointer lights up, and DD1's tooltip box names the building
    /// (town_name_&lt;id&gt;) with the line the hamlet writes under it (str_&lt;id&gt;_summary); DD1's files have places
    /// for a title and a description (quick_nav_title_offset, quick_nav_desc_offset: 0 0) and nothing more.
    ///
    /// The column lives in the building window beside its frame and is counted in DD1's screen pixels; the
    /// window keeps it where its frame is (<see cref="Place"/>). tools/preview_windows.py draws the same column.
    /// </summary>
    internal class BuildingNavigation : MonoBehaviour
    {
        private const string TownLayout = "campaign/town/town.layout.darkest";
        private const string BuildingLayout = "campaign/town/buildings/building.layout.darkest";
        private const string Dir = "campaign/town/building_navigation/";
        private const string Block = "building_navigation_layout";
        private const float ButtonSize = 56f;       // <id>.town_button.png
        private const float Margin = 6f;            // the column takes the clicks that fall between its buttons
        private const float CheckSeconds = 0.5f;

        private class Entry
        {
            public string Id;
            public RectTransform Rect;
            public Image Image;
            public Sprite Lit, Resting;
            public GameObject Padlock;
            public SpineView Alert;
            public Vector2 At;
            public bool Hovered, Locked;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private RectTransform _column;
        private Dd1Tooltip _tooltip;
        private string _current;
        private float _slideFrom, _slideSeconds, _slideStart, _nextCheck;

        /// <summary>
        /// Adds the column to a building window, over everything else of it. <paramref name="room"/>: the
        /// window's root, which ends at the roster column and at the estate's bar.
        /// </summary>
        /// <param name="current">The building whose screen this is.</param>
        /// <param name="slideIn">The column comes in from the screen's edge (the window opened from the hamlet, not from another building).</param>
        /// <param name="bottomEdge">Where the estate's bar begins, in DD1's screen pixels: tooltips stay above it.</param>
        public static BuildingNavigation Attach(Transform room, string current, bool slideIn, float bottomEdge)
        {
            var root = UiKit.Rect("Navigation", room).PlaceTopLeft(Vector2.zero, Dd1Ui.TopLeft, Vector2.zero);
            var navigation = root.gameObject.AddComponent<BuildingNavigation>();
            navigation._current = current;
            navigation.Build(root, slideIn, bottomEdge);
            return navigation;
        }

        /// <summary>
        /// Puts DD1's screen where the window has it: <paramref name="left"/> is the screen's left edge in the
        /// window's root, <paramref name="scale"/> the size of a DD1 pixel there.
        /// </summary>
        public void Place(float left, float scale)
        {
            var rect = (RectTransform)transform;
            if (!Mathf.Approximately(rect.localScale.x, scale)) rect.localScale = new Vector3(scale, scale, 1f);
            if (!Mathf.Approximately(rect.anchoredPosition.x, left)) rect.anchoredPosition = new Vector2(left, 0f);
        }

        private void Build(RectTransform root, bool slideIn, float bottomEdge)
        {
            // FALLBACK numbers: DD1's own values of these entries, used when a layout file cannot be read.
            var town = Dd1Ui.Layout(TownLayout);
            var layout = Dd1Ui.Layout(Dir + "building_navigation.layout.darkest");
            var building = Dd1Ui.Layout(BuildingLayout);
            var pos = Dd1Ui.Offset(town, "town_screen_layout", "button_navigation_pos", 70f, 230f);
            var away = Dd1Ui.Offset(town, "town_screen_layout", "button_navigation_offscreen_pos", -40f, 230f);
            var first = pos + Dd1Ui.Offset(layout, Block, "button_start_position", 0f, 0f);
            var spacing = Dd1Ui.Offset(layout, Block, "button_spacing", 0f, 68f);
            // selected_hop_offset 0 -5 is a hop, not a place: DD1's own frame of an open Tavern has the Tavern's button
            // on the row's grid like the others (found by matching tavern.town_button.png: 70, 502 = 230 + 4 x 68).
            // The hop itself is not played (its course is not in a file).
            var hop = Dd1Ui.Offset(layout, Block, "selected_hop_offset", 0f, -5f);
            var alertAt = Dd1Ui.Offset(layout, Block, "exclamation_point_offset", 28f, 32f);
            var padlockAt = Dd1Ui.Offset(layout, Block, "locked_overlay_offset", 14f, 14f);

            _column = UiKit.Rect("Column", root).PlaceTopLeft(Vector2.zero, Dd1Ui.TopLeft, Vector2.zero);
            var alerts = UiKit.Rect("Alerts", _column).PlaceTopLeft(Vector2.zero, Dd1Ui.TopLeft, Vector2.zero);
            // DD1's files give the slide a time and no easing; none of its other town panels moves in a straight line.
            _slideSeconds = slideIn ? Dd1Ui.Number(building, "building_animation", "quick_nav_transition_time", 0.5f) : 0f;
            _slideFrom = away.x - pos.x;
            _slideStart = Time.unscaledTime;

            var ids = HamletScene.BuildingIds;
            int low = int.MaxValue, high = int.MinValue;
            for (var i = 0; i < ids.Length; i++)
            {
                var id = ids[i];
                if (!Buildings.HasScreen(id)) continue;
                // the building's place in the column; HamletScene.BuildingIds is DD1's stock order
                var index = Mathf.RoundToInt(Dd1Ui.Number(layout, "building_navigation_building_layout_" + id, "button_index", i));
                low = Mathf.Min(low, index);
                high = Mathf.Max(high, index);
                var art = UpgradeUi.BuildingsDir + id + "/" + id + ".town_button.png";
                var entry = new Entry { Id = id, Lit = Dd1Ui.Sprite(art), At = first + spacing * index };
                entry.Resting = Dd1Ui.Toned(art, "town_navigation_button_unselected", 0.8f, 0.2f) ?? entry.Lit;
                entry.Image = UiKit.Image("Button." + id, _column, entry.Lit, entry.Lit != null ? Color.white : new Color(0.12f, 0.11f, 0.1f, 0.95f), true);
                entry.Rect = ((RectTransform)entry.Image.transform).PlaceTopLeft(entry.At, Dd1Ui.TopLeft, new Vector2(ButtonSize, ButtonSize));
                entry.Padlock = Dd1Ui.Art("Padlock", entry.Rect, Dir + "bld_quick_nav_locked_icon.png", padlockAt).gameObject;
                // A mark is taller than a button: all of them lie over the whole column.
                entry.Alert = Dd1ScreenName.Exclamation(alerts, entry.At + alertAt);
                var captured = entry;
                UpgradeUi.Pointer(entry.Image, () => Clicked(captured), null, () => Hover(captured, true), () => Hover(captured, false));
                _entries.Add(entry);
            }
            alerts.SetAsLastSibling();

            if (_entries.Count > 0)
            {
                // Under the buttons: a click between two of them is not a click on the town behind the window.
                var backing = UiKit.Image("Backing", _column, null, Color.clear, true);
                var top = first + spacing * low + new Vector2(-Margin, Mathf.Min(0f, hop.y) - Margin);
                var bottom = first + spacing * high + new Vector2(ButtonSize + Margin, ButtonSize + Margin);
                ((RectTransform)backing.transform).PlaceTopLeft(top, Dd1Ui.TopLeft, bottom - top);
                backing.transform.SetAsFirstSibling();
            }

            _tooltip = new Dd1Tooltip(root, 0f, bottomEdge);
            Slide();
            Check();
        }

        // ---- state ---------------------------------------------------------------------------------------

        private void Update()
        {
            Slide();
            if (Time.unscaledTime < _nextCheck) return;
            try { Check(); }
            catch (Exception e)
            {
                Plugin.Log.LogError("Hamlet: the buildings' quick navigation failed: " + e);
                enabled = false;
            }
        }

        private void Slide()
        {
            if (_column == null) return;
            var share = _slideSeconds > 0f ? Mathf.Clamp01((Time.unscaledTime - _slideStart) / _slideSeconds) : 1f;
            // easeOutSine, as DD1's panels come in (shared/app.darkest g_PanelTransitionTunables)
            var x = _slideFrom * (1f - Mathf.Sin(share * Mathf.PI * 0.5f));
            if (!Mathf.Approximately(_column.anchoredPosition.x, x)) _column.anchoredPosition = new Vector2(x, 0f);
        }

        // Locks and news change while the window stands open (a quest's reward is counted, a step is built).
        private void Check()
        {
            _nextCheck = Time.unscaledTime + CheckSeconds;
            foreach (var entry in _entries)
            {
                entry.Locked = BuildingLocks.Locked(entry.Id);
                if (entry.Padlock.activeSelf != entry.Locked) entry.Padlock.SetActive(entry.Locked);
                // the open building's news stands on its screen's name, not on its button (DD1's own screens)
                var alert = !entry.Locked && entry.Id != _current && UpgradeAlerts.Has(entry.Id);
                if (entry.Alert != null && entry.Alert.gameObject.activeSelf != alert) entry.Alert.gameObject.SetActive(alert);
                Show(entry);
            }
        }

        private void Show(Entry entry)
        {
            var sprite = entry.Id == _current || (entry.Hovered && !entry.Locked) ? entry.Lit : entry.Resting;
            if (sprite != null && entry.Image.sprite != sprite) entry.Image.sprite = sprite;
        }

        // ---- input ---------------------------------------------------------------------------------------

        private void Hover(Entry entry, bool on)
        {
            entry.Hovered = on;
            Show(entry);
            // DD1's sound of the pointer coming onto a button (which of its buttons play it is in no file)
            if (on && entry.Id != _current) EstateAudio.Ui("ui/town/button_mouse_over");
            if (_tooltip == null) return;
            if (!on)
            {
                _tooltip.Hide(entry);
                return;
            }
            var summary = entry.Locked ? BuildingLocks.Summary : Dd1Strings.Plain(Dd1Strings.Get("str_" + entry.Id + "_summary"));
            _tooltip.ShowAt(entry, entry.At + new Vector2(ButtonSize + Margin, 0f), ActivityText.Building(entry.Id), string.IsNullOrEmpty(summary) ? null : summary);
        }

        private string Clicked(Entry entry)
        {
            if (entry.Id == _current) return "already there";
            if (BuildingLocks.Locked(entry.Id))
            {
                // DD1's sound of a door that stays shut
                const string shut = "ui/town/button_click_locked";
                EstateAudio.Ui(Dd1Audio.Project != null && Dd1Audio.Project.Knows(shut) ? shut : "ui/town/button_click");
                return "locked";
            }
            EstateAudio.Ui("ui/town/button_click");
            _tooltip?.Hide(null);
            // the window (and this column with it) is replaced by the other building's: entered as by a click on
            // the building in the town, so its door is heard on this way in too
            HamletScene.Enter(entry.Id);
            return RosterWindow.Shows(entry.Id) ? "open" : "not opened";
        }

        /// <summary>For tests: what a click on a building's button does.</summary>
        public string Click(string id)
        {
            var entry = _entries.Find(e => e.Id == id);
            return entry != null ? Clicked(entry) : "no such button";
        }

        /// <summary>For tests: the column as it stands (DD1 screen pixels: each button's corner).</summary>
        public object Snapshot()
        {
            var buttons = new List<object>();
            foreach (var entry in _entries)
                buttons.Add(new { id = entry.Id, x = entry.At.x, y = entry.At.y, open = entry.Id == _current, locked = entry.Locked, alert = entry.Alert != null && entry.Alert.gameObject.activeSelf });
            return buttons;
        }
    }
}
