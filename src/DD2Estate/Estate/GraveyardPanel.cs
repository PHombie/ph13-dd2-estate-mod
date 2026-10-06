using DD2Estate.UI;
using TMPro;
using UnityEngine;

namespace DD2Estate.Estate
{
    /// <summary>
    /// The graveyard's screen, after DD1's: everyone the estate has lost, the latest first, each as DD1 lays
    /// a grave out (graveyard.layout.darkest): a tombstone by the resolve level the hero died at, the slab
    /// (dead_hero_backdrop.png) with the hero's face, name and the week they fell inside its margins, and
    /// under the slab, where the name begins, how they died, in the words DD1 has for it (str_death_*). The
    /// list scrolls inside DD1's list area with DD1's scroll bar. tools/preview_windows.py draws the same
    /// layout offline: change one, change the other.
    /// </summary>
    internal class GraveyardPanel : MonoBehaviour
    {
        private const string Art = "campaign/town/buildings/graveyard/";
        private const string LayoutFile = Art + "graveyard.layout.darkest";
        private static readonly Vector2 SlabSize = new Vector2(600f, 118f);      // dead_hero_backdrop.png
        private const float StoneSize = 118f;                                    // 0_1.png .. 6.png
        private const float PortraitSize = 85f;
        private const float NameLine = 40f;                                      // DwarvenAxe medium: town_graveyard_hero_name, ..._death_week

        // DD1 picks the tombstone by the resolve level the hero died at: 0_1.png, 2.png .. 6.png.
        private static string Stone(Graveyard.Fallen fallen)
        {
            var level = Mathf.Clamp(fallen.Level, 0, 6);
            return Art + (level <= 1 ? "0_1" : level.ToString()) + ".png";
        }

        private RosterWindow _window;
        private RectTransform _list;
        private Vector2 _entrySize, _portrait, _name, _deathBy;
        private float _left, _right, _top, _spacing;
        private int _shown = -1;

        public static void Open()
        {
            RosterLifecycle.BuryDead();
            var window = RosterWindow.Open(Graveyard.BuildingId, ActivityText.Building(Graveyard.BuildingId), Art + "graveyard");
            if (window == null) return;
            var panel = window.gameObject.AddComponent<GraveyardPanel>();
            panel._window = window;
            panel.Build(window.Frame);
            window.Refresh = panel.Refresh;
            panel.Refresh();
        }

        private void Build(RectTransform frame)
        {
            // FALLBACK numbers: DD1's own values of these entries, used when the file cannot be read.
            var layout = Dd1Ui.Layout(LayoutFile);
            const string main = "graveyard_main_layout", grave = "dead_hero_layout";
            var listPos = _window.Body + Dd1Ui.Offset(layout, main, "list_position", 148f, 148f);
            var listSize = Dd1Ui.Offset(layout, main, "list_area_size", 720f, 600f);
            _entrySize = Dd1Ui.Offset(layout, main, "entry_size", 1000f, 160f);
            _spacing = Dd1Ui.Number(layout, main, "entry_spacing", 20f);
            _portrait = Dd1Ui.Offset(layout, grave, "portrait_position", 0f, 0f);
            _name = Dd1Ui.Offset(layout, grave, "name_position", 96f, 0f);
            _deathBy = Dd1Ui.Offset(layout, grave, "death_by_position", 96f, 128f);
            // background_margins: left, right, top, bottom of what lies on the slab (10 25 10 20). Read so the
            // 85 px face fits the 88 px left between top and bottom, and hero_image_margins' 10 (the face's
            // right) puts the name where name_position has it; the statue's margins are counted the same way.
            var block = layout?.Find(grave);
            var margins = block != null && block.Values("background_margins").Count >= 4;
            _left = margins ? block.Float("background_margins", 0, 10f) : 10f;
            _right = margins ? block.Float("background_margins", 1, 25f) : 25f;
            _top = margins ? block.Float("background_margins", 2, 10f) : 10f;
            _list = Dd1Ui.ScrollList("Fallen", frame, listPos, listSize, Dd1Ui.Number(layout, main, "scrollbar_offset", 20f), out _);
        }

        private void Refresh()
        {
            var fallen = Graveyard.All;
            if (fallen.Count == _shown) return;
            _shown = fallen.Count;

            UpgradeUi.Clear(_list);
            var pitch = _entrySize.y + _spacing;
            for (var i = 0; i < fallen.Count; i++) AddGrave(fallen[fallen.Count - 1 - i], i, i * pitch);
            _list.sizeDelta = new Vector2(0f, Mathf.Max(0f, fallen.Count * pitch - _spacing));
            _list.anchoredPosition = Vector2.zero;
        }

        private void AddGrave(Graveyard.Fallen fallen, int index, float y)
        {
            var grave = UiKit.Rect("Fallen." + index, _list).PlaceTopLeft(new Vector2(0f, y), Dd1Ui.TopLeft, new Vector2(StoneSize + SlabSize.x, _entrySize.y));
            Dd1Ui.Art("Stone", grave, Stone(fallen), Vector2.zero, new Vector2(StoneSize, StoneSize));
            var slab = Dd1Ui.Art("Slab", grave, Art + "dead_hero_backdrop.png", new Vector2(StoneSize, 0f), SlabSize, new Color(0.1f, 0.1f, 0.11f, 0.9f));
            var on = slab.transform;
            // Face and name count from one corner: the slab's, inside its margins.
            var corner = new Vector2(_left, _top);

            // DD2's portrait of the class; that the fallen are drawn without colour is the mod's own (no DD1 file says so).
            var sprite = HeroNames.Portrait(fallen.ClassId, greyscale: true);
            var portrait = UiKit.Image("Portrait", on, sprite, sprite != null ? new Color(0.8f, 0.8f, 0.8f) : new Color(0.2f, 0.2f, 0.2f));
            portrait.preserveAspect = true;
            ((RectTransform)portrait.transform).PlaceTopLeft(corner + _portrait, Dd1Ui.TopLeft, new Vector2(PortraitSize, PortraitSize));

            var textAt = corner + _name;
            var textWidth = SlabSize.x - textAt.x - _right;
            var week = Dd1Ui.Line("Week", on, "town_graveyard_hero_death_week", new Vector2(SlabSize.x - _right, textAt.y), new Vector2(150f, NameLine), TextAlignmentOptions.TopRight);
            week.text = WindowText.Format("str_graveyard_week_string", "Week %d", fallen.Week);
            Dd1Ui.Line("Name", on, "town_graveyard_hero_name", textAt, new Vector2(textWidth - 156f, NameLine)).text = fallen.Name;
            // The mod's own line: the class, and where the hero fell.
            var what = HeroNames.ClassName(fallen.ClassId) + (string.IsNullOrEmpty(fallen.Where) ? "" : ", " + fallen.Where);
            Dd1Ui.Line("Class", on, "town_graveyard_story", textAt + new Vector2(0f, NameLine), new Vector2(textWidth, 26f)).text = what;

            // Under the slab, in DD1's words: "... was slain by a vile Bone Rabble." / "... bled out." / "... succumbed to
            // an unknown peril." It begins where the name does (both keys say 96); its height is counted from the
            // slab's top, 10 px under the slab.
            var storyAt = new Vector2(StoneSize + corner.x + _deathBy.x, _deathBy.y);
            Dd1Ui.Line("Story", grave, "town_graveyard_story", storyAt, new Vector2(StoneSize + SlabSize.x - storyAt.x, 26f)).text = fallen.Name + " " + Graveyard.Story(fallen);
        }
    }
}
