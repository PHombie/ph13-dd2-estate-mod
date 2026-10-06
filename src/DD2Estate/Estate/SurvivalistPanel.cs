using System.Collections.Generic;
using System.Text;
using Assets.Code.Actor;
using DD2Estate.Core;
using DD2Estate.Dungeon;
using DD2Estate.UI;
using UnityEngine;

namespace DD2Estate.Estate
{
    /// <summary>
    /// The Survivalist's screen, after DD1's (<see cref="HeroActionFrame"/>: the class's picture, the hero's
    /// slot and name in the banner, DD1's lines about the class at camp in the text column). The hero's
    /// camping skills stand in the body in DD1's two grids, the class's own above and the shared ones under
    /// them (camping_trainer.layout.darkest: skill_start_pos, skill_spacing, so many to a row), an unknown
    /// skill dark and without colour under DD1's padlock with its price beneath (icon_cost_offset). Click an
    /// unknown skill to learn it. A known skill is framed with DD1's mark of a chosen ability while it is
    /// ready for the next expedition: click it to put it aside or to make it ready. Pointing at a skill shows
    /// its name, its cost and what it does at a camp in DD1's tooltip; the window's line of text is DD1's own
    /// for this screen. tools/preview_windows.py draws the same layout offline: change one, change the other.
    /// </summary>
    internal class SurvivalistPanel : MonoBehaviour
    {
        private const string Art = UpgradeUi.BuildingsDir + "camping_trainer/camping_trainer";
        private const string LayoutFile = UpgradeUi.BuildingsDir + "camping_trainer/camping_trainer.layout.darkest";
        private const string ReadyMark = "shared/character/selected_ability.png";    // 90x90: DD1's frame of a chosen ability
        private const string Padlock = "shared/character/lockedskill.png";

        private const float IconSize = 72f;         // raid/camping/skill_icons/camp_skill_<id>.png
        private const float MarkSize = 90f;

        private RosterWindow _window;
        private HeroActionFrame _heroes;
        private RectTransform _grid;
        private Vector2 _classStart, _classPitch, _sharedStart, _sharedPitch, _cost, _locked, _tip;
        private int _classPerRow, _sharedPerRow;
        private float _tipWidth;
        private string _shown;

        public static void Open()
        {
            var window = RosterWindow.Open(Survivalist.BuildingId, ActivityText.Building(Survivalist.BuildingId), Art);
            if (window == null) return;
            var panel = window.gameObject.AddComponent<SurvivalistPanel>();
            panel._window = window;
            panel.Build(window.Frame);
            window.Refresh = panel.Refresh;
            panel.Refresh();
        }

        private static CampingRules Rules => CampContent.Rules;

        private void Build(RectTransform frame)
        {
            _heroes = new HeroActionFrame(_window, Survivalist.BuildingId) { Picked = Redraw };
            // FALLBACK numbers: DD1's own values of these entries, used when the file cannot be read.
            var layout = Dd1Ui.Layout(LayoutFile);
            const string own = "camping_trainer_class_specific_skill_grid_layout", shared = "camping_trainer_shared_skill_grid_layout";
            const string tree = "camping_trainer_upgrade_tree_layout", tip = "camping_trainer_upgrade_requirement_tooltip_layout";
            _classStart = _heroes.BodyPos + Dd1Ui.Offset(layout, own, "skill_start_pos", 60f, 30f);
            _classPitch = Dd1Ui.Offset(layout, own, "skill_spacing", 110f, 90f);
            // skill_number_of_rows: with a row every 90 px and the shared grid 170 px under the class's, it is how many stand in a row.
            _classPerRow = Mathf.Max(1, Mathf.RoundToInt(Dd1Ui.Number(layout, own, "skill_number_of_rows", 4f)));
            _sharedStart = _heroes.BodyPos + Dd1Ui.Offset(layout, shared, "skill_start_pos", 60f, 200f);
            _sharedPitch = Dd1Ui.Offset(layout, shared, "skill_spacing", 110f, 90f);
            _sharedPerRow = Mathf.Max(1, Mathf.RoundToInt(Dd1Ui.Number(layout, shared, "skill_number_of_rows", 4f)));
            _cost = Dd1Ui.Offset(layout, tree, "icon_cost_offset", 32f, 90f);
            _locked = Dd1Ui.Offset(layout, tree, "icon_locked_offset", 0f, 0f);
            _tip = Dd1Ui.Offset(layout, tree, "icon_tooltip_offset", 90f, 0f);
            _tipWidth = Dd1Ui.Number(layout, tip, "tooltip_text_width", 280f);

            _grid = UiKit.Rect("Skills", frame).Stretch();
            // DD1's own line for this screen.
            _window.Hint(WindowText.Plain("str_camping_longer_quests_only") ?? "(Only medium and long quests feature Camping.)");
            if (_window.Upgrades != null) _window.Upgrades.transform.SetAsLastSibling();
        }

        private void OnEnable()
        {
            UpgradeRules.Changed += Refresh;      // a bigger bonfire lowers the prices at once
            Survivalist.Changed += Refresh;
        }

        private void OnDisable()
        {
            UpgradeRules.Changed -= Refresh;
            Survivalist.Changed -= Refresh;
        }

        private void Redraw()
        {
            _shown = null;
            Refresh();
        }

        private void Refresh()
        {
            if (_heroes == null) return;
            _heroes.Refresh();
            var hero = _heroes.Hero;
            if (hero == null)
            {
                Show("nobody", null, null);
                return;
            }
            var book = Survivalist.Book(hero);
            var skills = Rules.SkillsFor(book.ClassId);
            string more = null;
            if (skills.Count == 0) more = hero.ActorName + " has no camping skills to learn: DD1's camping files could not be read, or know nothing of this class.";
            else if (Rules.UsesAnalogue(book.ClassId)) more = "DD1 has no camping skills for this class in this install: the " + CampingRules.Analogues[book.ClassId].Replace('_', ' ') + "'s stand in.";
            // The mod's own facts, for the slot's tooltip: DD1's screen has no place for them.
            _heroes.Describe("Camping skills " + book.Known.Count + " of " + skills.Count + "   Ready " + book.Active.Count + " of " + Rules.ActiveLimit, more);

            // Everything a cell shows, so the cells are only rebuilt when something changed.
            var key = new StringBuilder().Append(hero.ActorGuid).Append('|').Append(EstateState.Gold).Append('|').Append(UpgradeRules.Discount(Survivalist.CostTree));
            foreach (var skill in skills) key.Append('|').Append(skill.Id).Append(book.Knows(skill.Id) ? 'k' : '-').Append(book.IsActive(skill.Id) ? 'r' : '-');
            Show(key.ToString(), hero, book);
        }

        private void Show(string key, ActorInstance hero, CampSkillBook book)
        {
            if (key == _shown) return;
            _shown = key;
            _window.Tooltip.Hide(null);
            UpgradeUi.Clear(_grid);
            if (hero == null) return;
            AddGrid(hero, book, Rules.ClassSkills(book.ClassId), _classStart, _classPitch, _classPerRow);
            AddGrid(hero, book, Rules.SharedSkills(), _sharedStart, _sharedPitch, _sharedPerRow);
        }

        private void AddGrid(ActorInstance hero, CampSkillBook book, IReadOnlyList<CampSkill> skills, Vector2 start, Vector2 pitch, int perRow)
        {
            for (var i = 0; i < skills.Count; i++)
                AddCell(hero, book, skills[i], start + new Vector2(pitch.x * (i % perRow), pitch.y * (i / perRow)));
        }

        private void AddCell(ActorInstance hero, CampSkillBook book, CampSkill skill, Vector2 at)
        {
            var guid = hero.ActorGuid;
            var id = skill.Id;
            var known = book.Knows(id);
            var ready = book.IsActive(id);
            // DD1's look of what is not bought (upgrade_tree_icon_not_purchased: dark and without colour) under its padlock.
            var sprite = known ? CampContent.Icon(skill) : Dd1Shade.Of("upgrade_tree_icon_not_purchased", 0.4f, 0f).Art(skill.IconPath);
            var icon = UiKit.Image("Icon." + id, _grid, sprite, sprite != null ? Color.white : new Color(0.2f, 0.2f, 0.2f), true);
            ((RectTransform)icon.transform).PlaceTopLeft(at, UpgradeUi.TopLeft, new Vector2(IconSize, IconSize));
            if (!known) Dd1Ui.Art("Locked." + id, _grid, Padlock, at + _locked);
            if (ready)
            {
                var mark = (MarkSize - IconSize) * 0.5f;
                Dd1Ui.Art("Ready." + id, _grid, ReadyMark, at - new Vector2(mark, mark), new Vector2(MarkSize, MarkSize));
            }

            var price = Survivalist.Price(skill);
            if (!known) UpgradeUi.BuildGoldPrice("Price." + id, _grid, at + _cost, price, price > EstateState.Gold);

            // DD1's tooltip of a camping skill: its name as DD1 writes it, in capitals, what a use costs
            // (camping_skill_cost) and what it does; for a skill that cannot be learned now, why not.
            string Body()
            {
                var text = CampText.Cost(skill) + "\n" + CampText.Describe(skill);
                return !known && Survivalist.LearnBlock(hero, id) is string block ? text + "\n" + Dd1Ui.Tint(block + ".", "harmful") : text;
            }
            UpgradeUi.Pointer(icon,
                () => Act(known ? Survivalist.Toggle(guid, id) : Survivalist.Learn(guid, id),
                    hero.ActorName + (known ? (ready ? " puts " + CampText.Name(skill) + " aside." : " readies " + CampText.Name(skill) + ".") : " learns " + CampText.Name(skill) + ".")),
                () => UpgradeUi.ShowSheet(guid),
                () => _window.Tip(icon, true, at + _tip, CampText.Dd1String("camping_skill_name_" + id) ?? CampText.Name(skill), Body(), _tipWidth),
                () => _window.Tip(icon, false, Vector2.zero, null, null));
        }

        private void Act(string trouble, string done)
        {
            _window.Say(trouble != null ? trouble + "." : done, trouble != null);
            Redraw();
        }
    }
}
