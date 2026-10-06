using System.Collections.Generic;
using DD2Estate.Dd1;
using DD2Estate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DD2Estate.Estate
{
    /// <summary>
    /// The stage coach's screen, after DD1's: this week's recruits listed over the map table drawn in the
    /// backdrop, each on DD1's plate with the resolve badge, the portrait in a hero slot, the name and, where
    /// DD1 writes the class, class and path (the path is what tells this recruit from the estate's other
    /// heroes of the class). As in DD1 a recruit is hired by dragging the portrait onto the roster column, and
    /// in no other way. A coach that brings more recruits than the table holds keeps DD1's row spacing and
    /// scrolls, with DD1's scroll bar. An empty coach says so in DD1's words.
    ///
    /// Places are DD1's (campaign/town/buildings/stage_coach/stage_coach.layout.darkest: hero_recruit_store,
    /// hero_layout and stagecoach_layout, counted from building_base_layout body_base_pos).
    /// tools/preview_windows.py draws the same layout offline: change one, change the other.
    /// </summary>
    internal class StageCoachPanel : MonoBehaviour
    {
        private const string Art = "campaign/town/buildings/stage_coach/stage_coach";
        private const string LayoutFile = Art + ".layout.darkest";
        private const string ResolveDir = "shared/resolve_level_bar/";
        private static readonly Vector2 PlateSize = new Vector2(600f, 101f);     // stage_coach.hero_background.png
        private const float BadgeSize = 64f;                                     // resolve_level_bar_number_background_lvl*.png
        private const float RailWidth = 21f;                                     // shared/widgets/scrollbarmid.png
        private const float ListBottom = 752f;      // the last plate ends above the frame's foot
        private const float TextWidth = 290f;       // name and class: from DD1's offset to the plate's faded end

        private RosterWindow _window;
        private RectTransform _rows;
        private TextMeshProUGUI _empty;
        private Vector2 _item, _spacing, _plate, _badge, _badgeNumber, _name, _description;
        private float _barOffset, _barInset;
        private string _shown;

        public static void Open()
        {
            RosterLifecycle.BuryDead();
            // The window puts the building's upgrades (Stagecoach Network, Hero Barracks, Experienced Recruits,
            // Hero Paths) behind its "+"; what they change shows here at the next refresh.
            var window = RosterWindow.Open(StageCoach.BuildingId, ActivityText.Building(StageCoach.BuildingId), Art);
            if (window == null) return;
            var panel = window.gameObject.AddComponent<StageCoachPanel>();
            panel._window = window;
            panel.Build(window.Frame);
            window.Refresh = panel.Refresh;
            panel.Refresh();
        }

        private void OnEnable()
        {
            UpgradeRules.Changed += Refresh;
        }

        private void OnDisable()
        {
            UpgradeRules.Changed -= Refresh;
        }

        private void Build(RectTransform frame)
        {
            // FALLBACK numbers: DD1's own values of these entries, used when the file cannot be read.
            var layout = Dd1Ui.Layout(LayoutFile);
            const string store = "hero_recruit_store", hero = "hero_layout", coach = "stagecoach_layout";
            _item = _window.Body + Dd1Ui.Offset(layout, store, "store_item_pos", 450f, 80f);
            _spacing = Dd1Ui.Offset(layout, store, "store_item_spacing", 0f, 100f);
            _plate = Dd1Ui.Offset(layout, hero, "hero_background_offset", -100f, -8f);
            _badge = Dd1Ui.Offset(layout, hero, "hero_resolve_level_number_background_offset", -63f, 12f);
            _badgeNumber = Dd1Ui.Offset(layout, hero, "hero_resolve_level_number_text_offset", -30f, 43f);
            _name = Dd1Ui.Offset(layout, hero, "hero_name_offset", 100f, 12f);
            _description = Dd1Ui.Offset(layout, hero, "hero_description_offset", 100f, 42f);
            _barOffset = Dd1Ui.Number(layout, coach, "scroll_bar_offset", 0f);
            _barInset = Dd1Ui.Number(layout, coach, "scroll_bar_y_size_offset", 40f);
            var emptyAt = _window.Body + Dd1Ui.Offset(layout, coach, "empty_info_text_offset", 575f, 375f);
            var emptyWidth = Dd1Ui.Number(layout, coach, "empty_info_text_width", 600f);

            _rows = UiKit.Rect("Recruits", frame).Stretch();
            // DD1's own style for this text (fonts.darkest, base.colours.darkest: Ubuntu medium, neutral).
            _empty = Dd1Ui.Block("Empty", frame, "stagecoach_empty_info_text", emptyAt, new Vector2(emptyWidth, 80f), TextAlignmentOptions.Top);
            _empty.text = WindowText.Get("stagecoach_empty_info") ?? "The Stage Coach is empty.\nCheck back after your next quest.";
            // The pane was attached with the window, before the list: it lies over the keeper's side again.
            if (_window.Upgrades != null) _window.Upgrades.transform.SetAsLastSibling();
        }

        private void Refresh()
        {
            if (_rows == null) return;
            var offer = StageCoach.Offer;

            // Everything a row shows, so the rows are only rebuilt when something changed.
            var shown = RosterLifecycle.Count + "/" + StageCoach.RosterSize;
            var blocked = new List<string>();
            foreach (var recruit in offer)
            {
                var reason = StageCoach.HireBlockReason(recruit);
                blocked.Add(reason);
                shown += "|" + recruit.ClassId + ":" + recruit.PathId + ":" + StageCoach.DisplayName(recruit) + ":" + recruit.Tier + ":" + reason;
            }
            if (shown == _shown) return;
            _shown = shown;

            _window.Tooltip.Hide(null);
            UpgradeUi.Clear(_rows);
            _empty.gameObject.SetActive(offer.Count == 0);
            if (offer.Count == 0) return;

            // Seven plates fit the table at DD1's spacing. A coach that brings more (a week's event) keeps the
            // spacing and scrolls, as DD1's does.
            var first = _item + _plate;
            var pitch = Mathf.Max(1f, _spacing.y);
            var fits = Mathf.Max(1, Mathf.FloorToInt((ListBottom - first.y - PlateSize.y) / pitch) + 1);
            Transform parent = _rows;
            var origin = first;
            if (offer.Count > fits)
            {
                // The window is the box of the plates that fit, as far as the backdrop reaches: whole rows only.
                var size = new Vector2(Mathf.Min(PlateSize.x, RosterWindow.FrameWidth - first.x), pitch * (fits - 1) + PlateSize.y);
                var list = Dd1Ui.ScrollList("List", _rows, first, size, _barOffset - RailWidth, out var scroll);
                list.sizeDelta = new Vector2(0f, pitch * (offer.Count - 1) + PlateSize.y);
                scroll.scrollSensitivity = pitch;           // a notch of the wheel is one recruit
                // scroll_bar_y_size_offset: the rail is that much shorter than the list it scrolls; DD1's arrows go with it.
                var bar = Dd1ScrollBar.Of(scroll);
                bar.Place(new Vector2(first.x + size.x + _barOffset - RailWidth, first.y + _barInset * 0.5f), size.y - _barInset);
                bar.Step = pitch;
                parent = list;
                origin = Vector2.zero;
            }
            for (var i = 0; i < offer.Count; i++) AddRow(offer[i], parent, origin + _spacing * i, blocked[i]);
        }

        /// <param name="corner">The plate's corner in its parent's pixels.</param>
        private void AddRow(StageCoach.Recruit recruit, Transform parent, Vector2 corner, string blocked)
        {
            var back = Dd1Ui.Art("Recruit." + recruit.ClassId + "." + recruit.PathId, parent, Art + ".hero_background.png", corner, PlateSize, new Color(0.16f, 0.16f, 0.17f, 0.9f), true);
            var plate = back.transform;
            // Children count from the plate's corner; DD1 counts from the slot's.
            var origin = -_plate;

            // DD1 shows a recruit's resolve level: 0 for a green one, the rank of an experienced one.
            var level = Mathf.Clamp(recruit.Tier, 0, Resolve.MaxLevel);
            Dd1Ui.Art("Badge", plate, ResolveDir + "resolve_level_bar_number_background_lvl" + level + ".png", origin + _badge, new Vector2(BadgeSize, BadgeSize), new Color(0.2f, 0.2f, 0.2f));
            var number = UiKit.Text("Level", plate, level.ToString(), "resolve_number", Dd1Fonts.Colour("resolve_number", Color.black));
            number.textWrappingMode = TextWrappingModes.NoWrap;
            ((RectTransform)number.transform).PlaceTopLeft(origin + _badgeNumber, Dd1Ui.Middle, new Vector2(40f, 28f));

            var slot = UpgradeUi.BuildSlot("Slot", plate, origin);
            slot.Back.raycastTarget = false;        // the plate takes the pointer
            slot.Show(HeroNames.Portrait(recruit.ClassId), false, blocked != null);

            Dd1Ui.Line("Name", plate, "town_roster_name", origin + _name, new Vector2(TextWidth, 40f)).text = StageCoach.DisplayName(recruit);
            Dd1Ui.Line("Class", plate, "town_character_class", origin + _description, new Vector2(TextWidth, 30f)).text = HeroNames.Title(recruit.ClassId, StageCoach.PathOf(recruit));
            if (blocked != null) back.color = new Color(0.55f, 0.55f, 0.55f);

            // Hiring, DD1's way and DD1's only way: the portrait is dragged onto the roster.
            var handle = back.gameObject.AddComponent<HeroDrag>();
            handle.Portrait = () => HeroNames.Portrait(recruit.ClassId);
            handle.Dropped = pointer =>
            {
                var roster = RosterPanel.CanvasRoot != null ? RosterPanel.CanvasRoot.Find("Roster") as RectTransform : null;
                if (roster != null && RectTransformUtility.RectangleContainsScreenPoint(roster, pointer.position, pointer.pressEventCamera)) Hire(recruit);
            };
            UiKit.Hover(back.gameObject, inside =>
            {
                if (blocked == null) back.color = inside ? Color.white : new Color(0.88f, 0.88f, 0.88f);
                // DD1's line and nothing more; a recruit who cannot be hired says why instead.
                var body = blocked != null ? Dd1Ui.Tint(Trouble(blocked), "harmful") : WindowText.Plain("str_hero_slot_unlocked_stagecoach_tt") ?? "Drag Hero to Roster to recruit";
                // Under the slot, wherever the list has scrolled the row to.
                var at = inside ? InFrame((RectTransform)plate) - _plate + new Vector2(0f, UpgradeUi.SlotSize + 6f) : Vector2.zero;
                _window.Tip(back, inside, at, null, body);
            });
            if (blocked == null) back.color = new Color(0.88f, 0.88f, 0.88f);
        }

        // A full roster is told in DD1's words; the estate's own rules (one hero to a class and path) in the mod's.
        private static string Trouble(string reason)
        {
            return reason == StageCoach.BarracksFull ? StageCoach.FullRejection : reason + ".";
        }

        // A rect's upper left corner in the backdrop's pixels (y down).
        private Vector2 InFrame(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);          // 1 = the upper left corner
            var local = _window.Frame.InverseTransformPoint(corners[1]);
            return new Vector2(local.x, -local.y);
        }

        private void Hire(StageCoach.Recruit recruit)
        {
            var reason = StageCoach.HireBlockReason(recruit);
            if (reason != null) _window.Say(Trouble(reason), true);
            else if (StageCoach.Hire(recruit)) _window.Say(null);
            _shown = null;
            Refresh();
        }
    }
}
